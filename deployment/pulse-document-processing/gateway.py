"""Pulse-owned scan/OCR HTTP boundary; no model, database, or Oracle clients."""
from __future__ import annotations
import hashlib
import hmac
import json
import os
import re
import signal
import stat
import subprocess
import sys
import tempfile
import threading
import uuid
from dataclasses import asdict
from pathlib import Path
from flask import Flask, Request, jsonify, request
from werkzeug.exceptions import HTTPException
from clamd_client import BoundaryError, ClamdClient, MAX_UPLOAD_BYTES

PRIVACY = "private_pulse_runtime_only"
OCR_MODEL = "tesseract-5-eng"
MAX_TEXT = 1_000_000

class BoundedRequest(Request):
    max_form_memory_size = 64 * 1024
    max_form_parts = 8

def load_token(path: str = "/run/secrets/pulse-documents-token") -> str:
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd, "rb") as handle:
        info = os.fstat(handle.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_mode & 0o027:
            raise RuntimeError("Document-service secret must be a restricted regular file")
        token = handle.read(4097).strip()
    if not 32 <= len(token) <= 4096 or re.fullmatch(rb"[A-Za-z0-9_-]+", token) is None:
        raise RuntimeError("Document-service secret has invalid format")
    return token.decode("ascii")

def run_ocr(path: Path, directory: Path) -> dict:
    output = directory / "result.json"
    child = subprocess.Popen(
        [sys.executable, str(Path(__file__).with_name("ocr_job.py")), str(path), str(output)],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True,
        env={"PATH": "/usr/bin:/bin", "LANG": "C.UTF-8", "TZ": "UTC", "OMP_THREAD_LIMIT": "1"},
    )
    try:
        code = child.wait(timeout=180)
    except subprocess.TimeoutExpired:
        os.killpg(child.pid, signal.SIGKILL)
        child.wait()
        raise BoundaryError("ocr_deadline_exceeded", 504) from None
    if code != 0:
        raise BoundaryError("ocr_input_or_processing_rejected", 422)
    if not output.is_file() or output.stat().st_size > 6 * MAX_TEXT + 65536:
        raise BoundaryError("ocr_output_limit_exceeded", 413)
    payload = json.loads(output.read_text(encoding="utf-8"))
    pages = payload.get("pages")
    if not isinstance(pages, list) or not 1 <= len(pages) <= 50:
        raise BoundaryError("ocr_response_invalid", 502)
    total = 0
    for index, page in enumerate(pages, 1):
        if not isinstance(page, dict) or page.get("pageNumber") != index or not isinstance(page.get("text"), str):
            raise BoundaryError("ocr_response_invalid", 502)
        total += len(page["text"])
    if total > MAX_TEXT or not any(page["text"].strip() for page in pages):
        raise BoundaryError("ocr_text_unavailable_or_over_limit", 422)
    return {"pages": pages, "model": OCR_MODEL}

def create_app(token: str | None = None, scanner=None, ocr=None) -> Flask:
    app = Flask(__name__)
    app.request_class = BoundedRequest
    app.config.update(MAX_CONTENT_LENGTH=MAX_UPLOAD_BYTES + 65536, PROPAGATE_EXCEPTIONS=False)
    # Dependencies may be supplied by isolated tests, never via an HTTP request.
    service_token = token if token is not None else load_token()
    if len(service_token) < 32:
        raise RuntimeError("Document-service authentication is required")
    scanner = scanner if scanner is not None else ClamdClient()
    ocr = ocr if ocr is not None else run_ocr
    slot = threading.BoundedSemaphore(1)

    @app.before_request
    def authenticate():
        if request.path == "/health/live" and request.method == "GET":
            return None
        supplied = request.headers.get("Authorization", "")
        expected = "Bearer " + service_token
        if len(supplied) > 4200 or not hmac.compare_digest(supplied.encode(), expected.encode()):
            return jsonify(error={"code": "unauthorized"}), 401
        if request.headers.get("X-Pulse-AI-Privacy-Boundary") != PRIVACY:
            return jsonify(error={"code": "privacy_boundary_required"}), 403
        return None

    @app.after_request
    def no_store(response):
        response.headers["Cache-Control"] = "no-store"
        response.headers["X-Content-Type-Options"] = "nosniff"
        return response

    @app.errorhandler(BoundaryError)
    def boundary_error(error):
        return jsonify(error={"code": error.code}, clean=False), error.status

    @app.errorhandler(Exception)
    def safe_error(error):
        # Never log original filenames, document text, tokens, or parser stderr.
        status_code = error.code if isinstance(error, HTTPException) else 500
        code = "request_rejected" if isinstance(error, HTTPException) else "document_processing_failed"
        return jsonify(error={"code": code}, clean=False), status_code

    @app.get("/health/live")
    def live():
        return jsonify(status="live")

    @app.get("/health")
    def ready():
        engine = scanner.ready()
        result = subprocess.run(["/usr/bin/tesseract", "--version"], timeout=5,
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        if result.returncode:
            raise BoundaryError("ocr_unavailable")
        return jsonify(status="ready", scanner="clamav", engine=asdict(engine),
                       ocrModel=OCR_MODEL, modelProviderRequired=False)

    @app.post("/v1/scan")
    @app.post("/v1/extract")
    def process():
        if not slot.acquire(blocking=False):
            return jsonify(error={"code": "document_service_busy"}, clean=False), 429
        try:
            if request.mimetype != "multipart/form-data":
                raise BoundaryError("multipart_required", 415)
            uploads = request.files.getlist("file")
            if len(uploads) != 1 or len(request.files) != 1:
                raise BoundaryError("one_file_required", 400)
            extracting = request.path == "/v1/extract"
            document_id = None
            if extracting:
                if request.form.get("model") != OCR_MODEL:
                    raise BoundaryError("ocr_model_rejected", 400)
                try:
                    document_id = str(uuid.UUID(request.form.get("documentId", "")))
                except ValueError:
                    raise BoundaryError("document_identity_required", 400) from None
                if len(request.form.get("documentCategory", "")) > 128:
                    raise BoundaryError("document_metadata_invalid", 400)
            expected_hash = request.headers.get("X-Pulse-Content-SHA256")
            if expected_hash is not None and not re.fullmatch(r"[0-9a-f]{64}", expected_hash):
                raise BoundaryError("invalid_content_hash", 400)
            with tempfile.TemporaryDirectory(prefix="pulse-document-") as temporary:
                root = Path(temporary)
                source, size, digest = root / "source.upload", 0, hashlib.sha256()
                with source.open("xb") as target:
                    os.chmod(source, 0o600)
                    while chunk := uploads[0].stream.read(128 * 1024):
                        size += len(chunk)
                        if size > MAX_UPLOAD_BYTES:
                            raise BoundaryError("upload_limit_exceeded", 413)
                        digest.update(chunk)
                        target.write(chunk)
                source.chmod(0o400)
                if size == 0:
                    raise BoundaryError("empty_file", 400)
                if expected_hash and not hmac.compare_digest(expected_hash, digest.hexdigest()):
                    raise BoundaryError("document_version_changed", 409)
                scanned = scanner.scan(source)
                if scanned.sha256 != digest.hexdigest() or scanned.size != size:
                    raise BoundaryError("scan_content_mismatch")
                receipt = {"status": "clean" if scanned.clean else "infected",
                           "clean": scanned.clean, "infected": scanned.infected,
                           "scanner": "clamav", "signature": scanned.detection or scanned.engine.signatures,
                           "sizeBytes": size, "sha256": scanned.sha256, "engine": asdict(scanned.engine)}
                if scanned.clean == scanned.infected:
                    raise BoundaryError("scan_verdict_invalid")
                if not extracting:
                    return jsonify(receipt)
                if not scanned.clean:
                    raise BoundaryError("document_quarantined", 422)
                result = ocr(source, root)
                result.update(documentId=document_id, sha256=scanned.sha256, scan=receipt)
                return jsonify(result)
        finally:
            slot.release()
    return app
