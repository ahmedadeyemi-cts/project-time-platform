#!/usr/bin/env python3
"""WSGI entrypoint with local specialist-model routing and bounded OCR.

Ollama is the execution runtime, not the answer authority. Pulse retrieves and
permission-scopes internal facts before inference; this layer chooses among the
approved local generation models for synthesis. Current public facts still need
current authoritative evidence from the application layer when freshness matters.
"""

from __future__ import annotations

import fcntl
import os
import subprocess
import tempfile
import time
from pathlib import Path
from typing import Any

from flask import jsonify, request
from PIL import Image, UnidentifiedImageError
from werkzeug.exceptions import RequestEntityTooLarge

import gateway
from gateway import app

LOCK_PATH = Path("/var/lib/celar-ai/gateway/ocr.lock")
CONTRACT_MODEL = gateway.GENERATION_MODEL
REASONING_MODEL = os.environ.get("CELAR_REASONING_MODEL", "qwen3:4b-instruct").strip()
FAST_GENERAL_MODEL = os.environ.get("CELAR_FAST_GENERAL_MODEL", "llama3.2:3b").strip()
MAX_OCR_IMAGE_PIXELS = int(os.environ.get("CELAR_MAX_OCR_IMAGE_PIXELS", "40000000"))
MAX_OCR_IMAGE_EDGE = int(os.environ.get("CELAR_MAX_OCR_IMAGE_EDGE", "12000"))
PDF_RASTER_MAX_EDGE = int(os.environ.get("CELAR_PDF_RASTER_MAX_EDGE", "3000"))
MAX_OCR_TEXT_BYTES = 1_000_000

Image.MAX_IMAGE_PIXELS = MAX_OCR_IMAGE_PIXELS


# Pulse recognizes this closed set after stripping punctuation/case from the
# structured error code/type. Preserve only these codes from an upstream 4xx;
# never forward arbitrary provider messages or prompt-adjacent error text.
SAFETY_REFUSAL_CODES: dict[str, str] = {
    "contentfilter": "content_filter",
    "contentpolicyviolation": "content_policy_violation",
    "jailbreakdetected": "jailbreak_detected",
    "moderationblocked": "moderation_blocked",
    "policyviolation": "policy_violation",
    "responsibleaipolicyviolation": "responsible_ai_policy_violation",
    "safetyrefusal": "safety_refusal",
    "safetyviolation": "safety_violation",
}


def _normalize_safety_code(value: Any) -> str:
    if not isinstance(value, str) or not value.strip():
        return ""
    return "".join(character.lower() for character in value[:80] if character.isalnum())


def _safe_refusal_code(body: dict[str, Any]) -> str | None:
    error = body.get("error")
    if not isinstance(error, dict):
        return None

    for field in ("code", "type"):
        normalized = _normalize_safety_code(error.get(field))
        if normalized in SAFETY_REFUSAL_CODES:
            return SAFETY_REFUSAL_CODES[normalized]

    inner = error.get("innererror")
    if isinstance(inner, dict):
        for field in ("code", "type"):
            normalized = _normalize_safety_code(inner.get(field))
            if normalized in SAFETY_REFUSAL_CODES:
                return SAFETY_REFUSAL_CODES[normalized]
    return None


def _ollama_post_preserving_refusal(
    path: str,
    payload: dict[str, Any],
    timeout: int,
    limit: int,
) -> tuple[dict[str, Any], int]:
    """Proxy bounded Ollama JSON while preserving only recognized refusals.

    Ordinary provider errors remain sanitized to status-class diagnostics. A
    structured 400/403/422 safety code is reduced to an allowlisted canonical
    code so PulseAiPrivateModelResponsePolicy can classify it as terminal and
    prevent later external-provider failover.
    """
    try:
        upstream = gateway.SESSION.post(
            f"{gateway.OLLAMA_BASE_URL}{path}",
            json=payload,
            timeout=(5, timeout),
            stream=True,
            allow_redirects=False,
            headers={"Content-Type": "application/json"},
        )
    except gateway.requests.Timeout:
        return {"error": {"code": "private_runtime_timeout"}}, 504
    except gateway.requests.RequestException:
        return {"error": {"code": "private_runtime_unavailable"}}, 502

    with upstream:
        try:
            body = gateway._read_bounded_json(upstream, limit)
        except ValueError:
            return {"error": {"code": "private_runtime_response_invalid"}}, 502

        if upstream.status_code < 200 or upstream.status_code >= 300:
            if upstream.status_code in {400, 403, 422}:
                refusal_code = _safe_refusal_code(body)
                if refusal_code is not None:
                    return {"error": {"code": refusal_code}}, upstream.status_code
            return {
                "error": {"code": f"private_runtime_http_{upstream.status_code}"}
            }, upstream.status_code
        return body, 200


# All deployed gateway handlers resolve this module attribute at request time,
# so replacing it here applies the same refusal-preserving bounded transport to
# both the specialist chat route below and any base gateway handler using it.
gateway._ollama_post = _ollama_post_preserving_refusal


def _order(name: str, default: list[str]) -> list[str]:
    raw = os.environ.get(name, "").strip()
    values = [value.strip() for value in raw.split(",") if value.strip()] if raw else list(default)
    result: list[str] = []
    for value in values:
        if value not in result:
            result.append(value)
    return result


def _budgets(name: str, default: list[int], expected: int, deadline_seconds: int | None = None) -> list[int]:
    raw = os.environ.get(name, "").strip()
    try:
        values = [int(value.strip()) for value in raw.split(",") if value.strip()] if raw else list(default)
    except ValueError as exc:
        raise RuntimeError(f"Invalid model-attempt budget: {name}") from exc
    if len(values) != expected or any(value < 10 for value in values):
        raise RuntimeError(f"Model-attempt budget does not match route order: {name}")
    if sum(values) > (gateway.CHAT_TIMEOUT_SECONDS if deadline_seconds is None else deadline_seconds):
        raise RuntimeError(f"Model-attempt budget exceeds end-to-end chat timeout: {name}")
    return values


STRUCTURED_ORDER = _order(
    "CELAR_STRUCTURED_GENERATION_ORDER",
    [CONTRACT_MODEL, REASONING_MODEL, FAST_GENERAL_MODEL],
)
GENERAL_ORDER = _order(
    "CELAR_GENERAL_GENERATION_ORDER",
    [REASONING_MODEL, FAST_GENERAL_MODEL, CONTRACT_MODEL],
)
STRUCTURED_ATTEMPT_SECONDS = _budgets(
    "CELAR_STRUCTURED_MODEL_ATTEMPT_SECONDS",
    [660, 120, 60],
    len(STRUCTURED_ORDER),
)
GENERAL_ATTEMPT_SECONDS = _budgets(
    "CELAR_GENERAL_MODEL_ATTEMPT_SECONDS",
    [360, 240, 180],
    len(GENERAL_ORDER),
)
SOW_TIMEOUT_SECONDS = int(os.environ.get("CELAR_SOW_TIMEOUT_SECONDS", "640"))
if not 1 <= SOW_TIMEOUT_SECONDS <= 640:
    raise RuntimeError("Invalid bounded SOW timeout")
SOW_ATTEMPT_SECONDS = _budgets(
    "CELAR_SOW_MODEL_ATTEMPT_SECONDS", [420, 120, 90], len(STRUCTURED_ORDER), SOW_TIMEOUT_SECONDS
)

APPROVED_GENERATION_MODELS = set(STRUCTURED_ORDER + GENERAL_ORDER)

if CONTRACT_MODEL not in APPROVED_GENERATION_MODELS:
    raise RuntimeError("The compatibility generation model is not in the approved local portfolio")

STRUCTURED_FEATURES = {
    "sow_gsd_planning",
    "project_flowhive_plan",
    "project_forge_plan_estimate",
    "timesheet_description",
    "closeout_communication",
}


def _validate_image_dimensions(path: Path) -> tuple[int, int]:
    try:
        with Image.open(path) as image:
            width, height = image.size
            if width < 1 or height < 1:
                raise ValueError("image_dimensions_invalid")
            if width > MAX_OCR_IMAGE_EDGE or height > MAX_OCR_IMAGE_EDGE:
                raise ValueError("image_edge_limit_exceeded")
            if width * height > MAX_OCR_IMAGE_PIXELS:
                raise ValueError("image_pixel_limit_exceeded")
            image.verify()
            return width, height
    except (UnidentifiedImageError, Image.DecompressionBombError, Image.DecompressionBombWarning) as exc:
        raise ValueError("image_decode_rejected") from exc


def _ocr_image_to_text(path: Path, output_base: Path, deadline: float) -> str:
    _validate_image_dimensions(path)
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise subprocess.TimeoutExpired(["tesseract"], 0)
    result = subprocess.run(
        ["/usr/bin/tesseract", str(path), str(output_base), "-l", "eng", "--psm", "3"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        timeout=max(1, min(60, int(remaining))),
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError("ocr_failed")
    text_path = output_base.with_suffix(".txt")
    if not text_path.is_file() or text_path.stat().st_size > MAX_OCR_TEXT_BYTES:
        raise RuntimeError("ocr_text_limit_exceeded")
    return text_path.read_text(encoding="utf-8", errors="replace").replace("\x00", "").strip()


def _bounded_extract() -> Any:
    upload = request.files.get("file")
    model = request.form.get("model", "").strip()
    document_id = request.form.get("documentId", "").strip()
    document_category = request.form.get("documentCategory", "").strip()
    if upload is None:
        return gateway._error("file_required", 400)
    if model != gateway.OCR_MODEL:
        return gateway._error("ocr_model_rejected", 400)
    if not document_id or len(document_id) > 128 or len(document_category) > 128:
        return gateway._error("ocr_metadata_invalid", 400)

    deadline = time.monotonic() + gateway.OCR_TOTAL_TIMEOUT_SECONDS
    filename = upload.filename or "document"
    suffix = Path(filename).suffix.lower()
    with tempfile.TemporaryDirectory(prefix="celar-ocr-") as temp_dir:
        root = Path(temp_dir)
        source = root / f"source{suffix if len(suffix) <= 8 else ''}"
        try:
            gateway._save_upload(upload, source)
            with source.open("rb") as handle:
                prefix = handle.read(5)
            is_pdf = suffix == ".pdf" or prefix == b"%PDF-"
            pages: list[dict[str, Any]] = []
            total_text_bytes = 0

            if is_pdf:
                page_count = gateway._pdf_page_count(source, deadline)
                if page_count < 1 or page_count > gateway.MAX_OCR_PAGES:
                    return gateway._error("ocr_page_limit_exceeded", 413)
                for page_number in range(1, page_count + 1):
                    output_prefix = root / f"raster-{page_number}"
                    remaining = deadline - time.monotonic()
                    if remaining <= 0:
                        raise subprocess.TimeoutExpired(["pdftoppm"], 0)
                    converted = subprocess.run(
                        [
                            "/usr/bin/pdftoppm",
                            "-f", str(page_number),
                            "-l", str(page_number),
                            "-singlefile",
                            "-scale-to", str(PDF_RASTER_MAX_EDGE),
                            "-png",
                            str(source),
                            str(output_prefix),
                        ],
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL,
                        timeout=max(1, min(60, int(remaining))),
                        check=False,
                    )
                    if converted.returncode != 0:
                        return gateway._error("pdf_render_failed", 422)
                    image_path = output_prefix.with_suffix(".png")
                    if not image_path.is_file():
                        return gateway._error("pdf_render_incomplete", 422)
                    text = _ocr_image_to_text(image_path, root / f"ocr-{page_number}", deadline)
                    image_path.unlink(missing_ok=True)
                    if text:
                        total_text_bytes += len(text.encode("utf-8"))
                        if total_text_bytes > MAX_OCR_TEXT_BYTES:
                            return gateway._error("ocr_response_too_large", 413)
                        pages.append({"pageNumber": page_number, "text": text})
            else:
                _validate_image_dimensions(source)
                text = _ocr_image_to_text(source, root / "ocr-1", deadline)
                if text:
                    total_text_bytes = len(text.encode("utf-8"))
                    if total_text_bytes > MAX_OCR_TEXT_BYTES:
                        return gateway._error("ocr_response_too_large", 413)
                    pages.append({"pageNumber": 1, "text": text})

        except RequestEntityTooLarge:
            raise
        except subprocess.TimeoutExpired:
            return gateway._error("ocr_timeout", 504)
        except ValueError:
            return gateway._error("ocr_image_limits_rejected", 413)
        except (OSError, RuntimeError):
            return gateway._error("ocr_failed", 422)

    if not pages:
        return gateway._error("ocr_no_text_returned", 422)
    return jsonify({"pages": pages, "model": gateway.OCR_MODEL}), 200


def _serialized_extract() -> Any:
    LOCK_PATH.parent.mkdir(parents=True, exist_ok=True)
    with LOCK_PATH.open("a+", encoding="utf-8") as lock_file:
        try:
            fcntl.flock(lock_file.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return jsonify({"error": {"code": "ocr_busy"}}), 503
        try:
            return _bounded_extract()
        finally:
            fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)


def _local_chat_completions() -> Any:
    """Route to local specialists inside one bounded end-to-end deadline.

    Runtime/server failures may fall through to the next approved local model.
    A 4xx response, including a policy/safety refusal, remains terminal.
    """
    try:
        payload = gateway._bounded_request_json()
    except ValueError:
        return gateway._error("invalid_json", 400)

    if payload.get("model") != CONTRACT_MODEL:
        return gateway._error("generation_model_rejected", 400)
    if payload.get("stream", False) is not False:
        return gateway._error("streaming_not_supported", 400)

    messages = payload.get("messages")
    if not isinstance(messages, list) or not (1 <= len(messages) <= 64):
        return gateway._error("messages_invalid", 400)
    total_characters = 0
    for message in messages:
        if not isinstance(message, dict):
            return gateway._error("messages_invalid", 400)
        role = message.get("role")
        content = message.get("content")
        if role not in {"system", "user", "assistant", "tool"} or not isinstance(content, str):
            return gateway._error("messages_invalid", 400)
        total_characters += len(content)
    if total_characters > 500_000:
        return gateway._error("prompt_too_large", 413)

    max_tokens = payload.get("max_tokens", 2048)
    if not isinstance(max_tokens, int) or isinstance(max_tokens, bool) or max_tokens < 1 or max_tokens > 8192:
        return gateway._error("max_tokens_invalid", 400)

    feature = request.headers.get("X-Pulse-AI-Feature", "").strip().lower()
    response_format = payload.get("response_format")
    structured = feature in STRUCTURED_FEATURES or (
        isinstance(response_format, dict) and response_format.get("type") == "json_object"
    )
    candidates = STRUCTURED_ORDER if structured else GENERAL_ORDER
    attempt_budgets = STRUCTURED_ATTEMPT_SECONDS if structured else GENERAL_ATTEMPT_SECONDS

    sow = feature == "sow_gsd_planning"
    if sow:
        attempt_budgets = SOW_ATTEMPT_SECONDS

    base_payload: dict[str, Any] = {
        "messages": messages,
        "stream": False,
        "max_tokens": max_tokens,
    }
    for name in ("temperature", "top_p", "seed", "stop", "response_format"):
        if name in payload:
            base_payload[name] = payload[name]

    deadline = time.monotonic() + (SOW_TIMEOUT_SECONDS if sow else gateway.CHAT_TIMEOUT_SECONDS)
    last_body: dict[str, Any] = {"error": {"code": "private_runtime_unavailable"}}
    last_status = 502
    attempted: list[str] = []

    for index, candidate in enumerate(candidates):
        if candidate not in APPROVED_GENERATION_MODELS:
            continue
        remaining = int(deadline - time.monotonic())
        if remaining < 10:
            last_body = {"error": {"code": "private_runtime_timeout"}}
            last_status = 504
            break
        attempt_timeout = max(10, min(attempt_budgets[index], remaining))
        attempted.append(candidate)
        candidate_payload = dict(base_payload)
        candidate_payload["model"] = candidate
        body, status = gateway._ollama_post(
            "/v1/chat/completions",
            candidate_payload,
            attempt_timeout,
            gateway.MAX_GATEWAY_RESPONSE_BYTES,
        )
        if status == 200:
            response = jsonify(body)
            response.headers["X-Celar-Local-Model"] = candidate
            response.headers["X-Celar-Local-Route"] = "structured" if structured else "general"
            response.headers["X-Celar-Local-Attempt-Seconds"] = str(attempt_timeout)
            return response, 200
        last_body, last_status = body, status
        if status < 500:
            break

    response = jsonify(last_body)
    response.headers["X-Celar-Local-Models-Attempted"] = ",".join(attempted)
    return response, last_status


app.view_functions["extract"] = _serialized_extract
app.view_functions["chat_completions"] = _local_chat_completions
