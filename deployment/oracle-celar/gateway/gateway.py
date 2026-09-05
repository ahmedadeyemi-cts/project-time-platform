#!/usr/bin/env python3
"""Authenticated local Celar gateway for the Oracle protected-Test runtime.

The public HTTPS boundary is Caddy. This process binds only to 127.0.0.1:8787
and exposes the exact five capabilities expected by the Project Time Platform:
health, chat completions, embeddings, OCR, and ClamAV scanning.

Request bodies and document text are deliberately never written to application
logs. The gateway is a transport/adapter boundary, not an external escalation
path and not a training endpoint.
"""

from __future__ import annotations

import hmac
import json
import os
import re
import socket
import struct
import subprocess
import tempfile
import time
from pathlib import Path
from typing import Any

import requests
from flask import Flask, Response, jsonify, request
from werkzeug.exceptions import RequestEntityTooLarge

PRIVACY_BOUNDARY = "private_pulse_runtime_only"
TOKEN_FILE = Path(os.environ.get("CELAR_RUNTIME_TOKEN_FILE", "/etc/celar-ai/gateway/runtime-token"))
GENERATION_MODEL = os.environ.get("CELAR_GENERATION_MODEL", "gemma3:4b").strip()
EMBEDDING_MODEL = os.environ.get("CELAR_EMBEDDING_MODEL", "embeddinggemma").strip()
OCR_MODEL = os.environ.get("CELAR_OCR_MODEL", "tesseract-5-eng").strip()
EMBEDDING_DIMENSION = int(os.environ.get("CELAR_EMBEDDING_DIMENSION", "768"))
OLLAMA_BASE_URL = os.environ.get("CELAR_OLLAMA_BASE_URL", "http://127.0.0.1:11434").rstrip("/")
CLAMAV_HOST = os.environ.get("CELAR_CLAMAV_HOST", "127.0.0.1").strip()
CLAMAV_PORT = int(os.environ.get("CELAR_CLAMAV_PORT", "3310"))
GATEWAY_VERSION = os.environ.get("CELAR_GATEWAY_VERSION", "1.0.0").strip()
MAX_UPLOAD_BYTES = int(os.environ.get("CELAR_MAX_UPLOAD_BYTES", str(32 * 1024 * 1024)))
MAX_JSON_REQUEST_BYTES = int(os.environ.get("CELAR_MAX_JSON_REQUEST_BYTES", str(2 * 1024 * 1024)))
MAX_GATEWAY_RESPONSE_BYTES = int(os.environ.get("CELAR_MAX_GATEWAY_RESPONSE_BYTES", "1000000"))
MAX_OCR_PAGES = int(os.environ.get("CELAR_MAX_OCR_PAGES", "50"))
OCR_TOTAL_TIMEOUT_SECONDS = int(os.environ.get("CELAR_OCR_TOTAL_TIMEOUT_SECONDS", "270"))
CHAT_TIMEOUT_SECONDS = int(os.environ.get("CELAR_CHAT_TIMEOUT_SECONDS", "840"))
EMBED_TIMEOUT_SECONDS = int(os.environ.get("CELAR_EMBED_TIMEOUT_SECONDS", "240"))

if CLAMAV_HOST != "127.0.0.1":
    raise RuntimeError("ClamAV must remain bound to 127.0.0.1")
if OLLAMA_BASE_URL != "http://127.0.0.1:11434":
    raise RuntimeError("Ollama must remain bound to 127.0.0.1:11434")
if not TOKEN_FILE.is_file():
    raise RuntimeError("Celar runtime token file is missing")
RUNTIME_TOKEN = TOKEN_FILE.read_text(encoding="utf-8").strip()
if len(RUNTIME_TOKEN) < 32:
    raise RuntimeError("Celar runtime token is too short")

app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = MAX_UPLOAD_BYTES + (1024 * 1024)
app.config["JSON_SORT_KEYS"] = False
app.config["PROPAGATE_EXCEPTIONS"] = False

SESSION = requests.Session()
SESSION.trust_env = False


def _error(code: str, status: int) -> tuple[Response, int]:
    return jsonify({"error": {"code": code}}), status


@app.before_request
def _authenticate() -> tuple[Response, int] | None:
    auth = request.headers.get("Authorization", "")
    supplied = auth[7:].strip() if auth.startswith("Bearer ") else ""
    if not supplied or not hmac.compare_digest(supplied, RUNTIME_TOKEN):
        response, status = _error("unauthorized", 401)
        response.headers["WWW-Authenticate"] = "Bearer"
        return response, status
    if request.headers.get("X-Pulse-AI-Privacy-Boundary", "") != PRIVACY_BOUNDARY:
        return _error("privacy_boundary_required", 403)
    return None


@app.errorhandler(RequestEntityTooLarge)
def _too_large(_: RequestEntityTooLarge) -> tuple[Response, int]:
    return _error("request_too_large", 413)


@app.errorhandler(404)
def _not_found(_: Exception) -> tuple[Response, int]:
    return _error("not_found", 404)


@app.errorhandler(Exception)
def _unhandled(exception: Exception) -> tuple[Response, int]:
    # Do not stringify the exception: parser/converter exceptions can contain
    # filenames or provider details. The class name is sufficient operationally.
    app.logger.error("Celar gateway request failed path=%s type=%s", request.path, type(exception).__name__)
    return _error("gateway_failure", 500)


def _bounded_request_json() -> dict[str, Any]:
    length = request.content_length
    if length is not None and length > MAX_JSON_REQUEST_BYTES:
        raise RequestEntityTooLarge()
    raw = request.get_data(cache=True)
    if len(raw) > MAX_JSON_REQUEST_BYTES:
        raise RequestEntityTooLarge()
    try:
        value = json.loads(raw.decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("invalid_json") from exc
    if not isinstance(value, dict):
        raise ValueError("json_object_required")
    return value


def _read_bounded_json(response: requests.Response, limit: int) -> dict[str, Any]:
    content_length = response.headers.get("Content-Length", "")
    if content_length.isdigit() and int(content_length) > limit:
        raise ValueError("provider_response_too_large")
    data = bytearray()
    for chunk in response.iter_content(chunk_size=65536):
        if not chunk:
            continue
        if len(data) + len(chunk) > limit:
            raise ValueError("provider_response_too_large")
        data.extend(chunk)
    try:
        parsed = json.loads(data.decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("provider_response_invalid") from exc
    if not isinstance(parsed, dict):
        raise ValueError("provider_response_invalid")
    return parsed


def _ollama_post(path: str, payload: dict[str, Any], timeout: int, limit: int) -> tuple[dict[str, Any], int]:
    try:
        upstream = SESSION.post(
            f"{OLLAMA_BASE_URL}{path}",
            json=payload,
            timeout=(5, timeout),
            stream=True,
            allow_redirects=False,
            headers={"Content-Type": "application/json"},
        )
    except requests.Timeout:
        return {"error": {"code": "private_runtime_timeout"}}, 504
    except requests.RequestException:
        return {"error": {"code": "private_runtime_unavailable"}}, 502
    with upstream:
        try:
            body = _read_bounded_json(upstream, limit)
        except ValueError:
            return {"error": {"code": "private_runtime_response_invalid"}}, 502
        if upstream.status_code < 200 or upstream.status_code >= 300:
            # Do not forward arbitrary upstream error text because it may echo
            # prompt material. Preserve only the status-class diagnostic.
            return {"error": {"code": f"private_runtime_http_{upstream.status_code}"}}, upstream.status_code
        return body, 200


def _ollama_models() -> set[str]:
    try:
        response = SESSION.get(
            f"{OLLAMA_BASE_URL}/api/tags",
            timeout=(3, 10),
            allow_redirects=False,
        )
        response.raise_for_status()
        payload = response.json()
    except (requests.RequestException, ValueError):
        return set()
    names: set[str] = set()
    if isinstance(payload, dict):
        models = payload.get("models")
        if isinstance(models, list):
            for model in models:
                if isinstance(model, dict):
                    name = model.get("name")
                    if isinstance(name, str) and name.strip():
                        names.add(name.strip())
    return names


def _clamav_ping() -> bool:
    try:
        with socket.create_connection((CLAMAV_HOST, CLAMAV_PORT), timeout=5) as client:
            client.sendall(b"zPING\0")
            client.settimeout(5)
            response = client.recv(64)
            return response.replace(b"\0", b"").strip() == b"PONG"
    except OSError:
        return False


def _clamav_signature_version() -> str:
    daily = Path("/var/lib/clamav/daily.cvd")
    if not daily.is_file():
        return "runtime_managed"
    try:
        result = subprocess.run(
            ["/usr/bin/sigtool", "--info", str(daily)],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            timeout=5,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired):
        return "runtime_managed"
    text = result.stdout.decode("utf-8", errors="replace")
    match = re.search(r"(?m)^Version:\s*([^\s]+)", text)
    return f"daily-{match.group(1)}" if match else "runtime_managed"


def _scan_stream(stream: Any) -> tuple[bool, bool, int, str]:
    size = 0
    with socket.create_connection((CLAMAV_HOST, CLAMAV_PORT), timeout=10) as client:
        client.settimeout(45)
        client.sendall(b"zINSTREAM\0")
        while True:
            chunk = stream.read(128 * 1024)
            if not chunk:
                break
            size += len(chunk)
            if size > MAX_UPLOAD_BYTES:
                raise RequestEntityTooLarge()
            client.sendall(struct.pack(">I", len(chunk)))
            client.sendall(chunk)
        client.sendall(struct.pack(">I", 0))
        response = bytearray()
        while len(response) < 16 * 1024:
            piece = client.recv(1024)
            if not piece:
                break
            response.extend(piece)
            if b"\0" in piece:
                break
    text = bytes(response).split(b"\0", 1)[0].decode("utf-8", errors="replace").strip()
    infected = text.upper().endswith(" FOUND")
    clean = text.upper().endswith(" OK")
    if not clean and not infected:
        raise RuntimeError("clamav_scan_failed")
    detected = ""
    if infected:
        match = re.search(r":\s*(.+?)\s+FOUND$", text, re.IGNORECASE)
        detected = match.group(1).strip() if match else "malware_detected"
    return clean, infected, size, detected


@app.get("/health")
def health() -> tuple[Response, int]:
    models = _ollama_models()
    ollama_ready = bool(models)
    generation_ready = GENERATION_MODEL in models
    embedding_ready = EMBEDDING_MODEL in models
    try:
        tesseract = subprocess.run(
            ["/usr/bin/tesseract", "--version"],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=5,
            check=False,
        )
        tesseract_ready = tesseract.returncode == 0 and tesseract.stdout.startswith(b"tesseract 5.")
    except (OSError, subprocess.TimeoutExpired):
        tesseract_ready = False
    clamav_ready = _clamav_ping()
    ready = ollama_ready and generation_ready and embedding_ready and tesseract_ready and clamav_ready
    payload = {
        "status": "ready" if ready else "degraded",
        "gatewayVersion": GATEWAY_VERSION,
        "ollamaReady": ollama_ready,
        "generationModelReady": generation_ready,
        "embeddingModelReady": embedding_ready,
        "tesseractReady": tesseract_ready,
        "clamavReady": clamav_ready,
        "generationModel": GENERATION_MODEL,
        "embeddingModel": EMBEDDING_MODEL,
        "embeddingDimension": EMBEDDING_DIMENSION,
        "ocrModel": OCR_MODEL,
        "clamavSignatureVersion": _clamav_signature_version(),
        "rawDocumentContentLogged": False,
        "trainingEnabled": False,
        "externalEscalationEnabled": False,
    }
    return jsonify(payload), 200 if ready else 503


@app.post("/v1/chat/completions")
def chat_completions() -> tuple[Response, int]:
    try:
        payload = _bounded_request_json()
    except ValueError:
        return _error("invalid_json", 400)
    if payload.get("model") != GENERATION_MODEL:
        return _error("generation_model_rejected", 400)
    if payload.get("stream", False) is not False:
        return _error("streaming_not_supported", 400)
    messages = payload.get("messages")
    if not isinstance(messages, list) or not (1 <= len(messages) <= 64):
        return _error("messages_invalid", 400)
    total_characters = 0
    for message in messages:
        if not isinstance(message, dict):
            return _error("messages_invalid", 400)
        role = message.get("role")
        content = message.get("content")
        if role not in {"system", "user", "assistant", "tool"} or not isinstance(content, str):
            return _error("messages_invalid", 400)
        total_characters += len(content)
    if total_characters > 500_000:
        return _error("prompt_too_large", 413)
    max_tokens = payload.get("max_tokens", 2048)
    if not isinstance(max_tokens, int) or isinstance(max_tokens, bool) or max_tokens < 1 or max_tokens > 8192:
        return _error("max_tokens_invalid", 400)

    allowed: dict[str, Any] = {
        "model": GENERATION_MODEL,
        "messages": messages,
        "stream": False,
        "max_tokens": max_tokens,
    }
    for name in ("temperature", "top_p", "seed", "stop", "response_format"):
        if name in payload:
            allowed[name] = payload[name]
    body, status = _ollama_post(
        "/v1/chat/completions",
        allowed,
        CHAT_TIMEOUT_SECONDS,
        MAX_GATEWAY_RESPONSE_BYTES,
    )
    return jsonify(body), status


@app.post("/v1/embeddings")
def embeddings() -> tuple[Response, int]:
    try:
        payload = _bounded_request_json()
    except ValueError:
        return _error("invalid_json", 400)
    if payload.get("model") != EMBEDDING_MODEL:
        return _error("embedding_model_rejected", 400)
    source = payload.get("input")
    if isinstance(source, str):
        inputs = [source]
    elif isinstance(source, list) and all(isinstance(item, str) for item in source):
        inputs = source
    else:
        return _error("embedding_input_invalid", 400)
    if not (1 <= len(inputs) <= 64):
        return _error("embedding_input_invalid", 400)
    if any(len(item) > 24_000 for item in inputs) or sum(len(item) for item in inputs) > 256_000:
        return _error("embedding_input_too_large", 413)

    body, status = _ollama_post(
        "/api/embed",
        {"model": EMBEDDING_MODEL, "input": inputs, "truncate": True},
        EMBED_TIMEOUT_SECONDS,
        8 * 1024 * 1024,
    )
    if status != 200:
        return jsonify(body), status
    vectors = body.get("embeddings")
    if not isinstance(vectors, list) or len(vectors) != len(inputs):
        return _error("embedding_response_invalid", 502)
    for vector in vectors:
        if not isinstance(vector, list) or len(vector) != EMBEDDING_DIMENSION:
            return _error("embedding_dimension_invalid", 502)
        if not all(isinstance(value, (int, float)) and not isinstance(value, bool) for value in vector):
            return _error("embedding_response_invalid", 502)
    response = {
        "object": "list",
        "model": EMBEDDING_MODEL,
        "data": [
            {"object": "embedding", "index": index, "embedding": vector}
            for index, vector in enumerate(vectors)
        ],
        "usage": {
            "prompt_tokens": int(body.get("prompt_eval_count", 0) or 0),
            "total_tokens": int(body.get("prompt_eval_count", 0) or 0),
        },
    }
    return jsonify(response), 200


@app.post("/v1/scan")
def scan() -> tuple[Response, int]:
    upload = request.files.get("file")
    if upload is None:
        return _error("file_required", 400)
    try:
        clean, infected, size, detected = _scan_stream(upload.stream)
    except RequestEntityTooLarge:
        raise
    except (OSError, RuntimeError, socket.timeout):
        return _error("scanner_unavailable", 503)
    signature = detected if infected else _clamav_signature_version()
    payload = {
        "status": "infected" if infected else "clean",
        "clean": clean,
        "infected": infected,
        "scanner": "clamav",
        "signature": signature,
        "sizeBytes": size,
    }
    return jsonify(payload), 200


def _save_upload(upload: Any, target: Path) -> int:
    size = 0
    with target.open("wb") as output:
        while True:
            chunk = upload.stream.read(128 * 1024)
            if not chunk:
                break
            size += len(chunk)
            if size > MAX_UPLOAD_BYTES:
                raise RequestEntityTooLarge()
            output.write(chunk)
    return size


def _run_bounded(command: list[str], deadline: float, maximum_seconds: int) -> subprocess.CompletedProcess[bytes]:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise subprocess.TimeoutExpired(command, 0)
    return subprocess.run(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        timeout=max(1, min(maximum_seconds, int(remaining))),
        check=False,
    )


def _ocr_image(path: Path, deadline: float) -> str:
    result = _run_bounded(
        ["/usr/bin/tesseract", str(path), "stdout", "-l", "eng", "--psm", "3"],
        deadline,
        60,
    )
    if result.returncode != 0:
        raise RuntimeError("ocr_failed")
    return result.stdout.decode("utf-8", errors="replace").replace("\x00", "").strip()


def _pdf_page_count(path: Path, deadline: float) -> int:
    result = _run_bounded(["/usr/bin/pdfinfo", str(path)], deadline, 20)
    if result.returncode != 0:
        raise RuntimeError("pdf_info_failed")
    text = result.stdout.decode("utf-8", errors="replace")
    match = re.search(r"(?m)^Pages:\s*(\d+)\s*$", text)
    if not match:
        raise RuntimeError("pdf_page_count_unavailable")
    return int(match.group(1))


def _page_sort_key(path: Path) -> int:
    match = re.search(r"-(\d+)\.png$", path.name)
    return int(match.group(1)) if match else 0


@app.post("/v1/extract")
def extract() -> tuple[Response, int]:
    upload = request.files.get("file")
    model = request.form.get("model", "").strip()
    document_id = request.form.get("documentId", "").strip()
    document_category = request.form.get("documentCategory", "").strip()
    if upload is None:
        return _error("file_required", 400)
    if model != OCR_MODEL:
        return _error("ocr_model_rejected", 400)
    if not document_id or len(document_id) > 128 or len(document_category) > 128:
        return _error("ocr_metadata_invalid", 400)

    deadline = time.monotonic() + OCR_TOTAL_TIMEOUT_SECONDS
    filename = upload.filename or "document"
    suffix = Path(filename).suffix.lower()
    with tempfile.TemporaryDirectory(prefix="celar-ocr-") as temp_dir:
        root = Path(temp_dir)
        source = root / f"source{suffix if len(suffix) <= 8 else ''}"
        try:
            _save_upload(upload, source)
            with source.open("rb") as handle:
                prefix = handle.read(5)
            is_pdf = suffix == ".pdf" or prefix == b"%PDF-"
            pages: list[dict[str, Any]] = []
            if is_pdf:
                page_count = _pdf_page_count(source, deadline)
                if page_count < 1 or page_count > MAX_OCR_PAGES:
                    return _error("ocr_page_limit_exceeded", 413)
                output_prefix = root / "page"
                converted = _run_bounded(
                    [
                        "/usr/bin/pdftoppm",
                        "-f", "1",
                        "-l", str(page_count),
                        "-r", "150",
                        "-png",
                        str(source),
                        str(output_prefix),
                    ],
                    deadline,
                    90,
                )
                if converted.returncode != 0:
                    return _error("pdf_render_failed", 422)
                images = sorted(root.glob("page-*.png"), key=_page_sort_key)
                if len(images) != page_count:
                    return _error("pdf_render_incomplete", 422)
                for index, image in enumerate(images, start=1):
                    text = _ocr_image(image, deadline)
                    if text:
                        pages.append({"pageNumber": index, "text": text})
            else:
                text = _ocr_image(source, deadline)
                if text:
                    pages.append({"pageNumber": 1, "text": text})
        except RequestEntityTooLarge:
            raise
        except subprocess.TimeoutExpired:
            return _error("ocr_timeout", 504)
        except (OSError, RuntimeError):
            return _error("ocr_failed", 422)

    if not pages:
        return _error("ocr_no_text_returned", 422)
    # Bound the returned OCR text independently of upload size.
    total = sum(len(page["text"]) for page in pages)
    if total > 1_000_000:
        return _error("ocr_response_too_large", 413)
    return jsonify({"pages": pages, "model": OCR_MODEL}), 200


if __name__ == "__main__":
    # Development-only invocation. The deployed service uses gunicorn and binds
    # exclusively to 127.0.0.1 from the systemd unit.
    app.run(host="127.0.0.1", port=8787, debug=False)
