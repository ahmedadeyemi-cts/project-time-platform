#!/usr/bin/env python3
"""WSGI entrypoint with local specialist-model routing and OCR admission control.

Ollama is the execution runtime, not the answer authority. Pulse retrieves and
permission-scopes internal facts before inference; this layer chooses among the
approved local generation models for synthesis. Current public facts still need
current authoritative evidence from the application layer when freshness matters.
"""

from __future__ import annotations

import fcntl
import os
from pathlib import Path
from typing import Any

from flask import jsonify, request

import gateway
from gateway import app

LOCK_PATH = Path("/var/lib/celar-ai/gateway/ocr.lock")
CONTRACT_MODEL = gateway.GENERATION_MODEL
REASONING_MODEL = os.environ.get("CELAR_REASONING_MODEL", "qwen3:4b").strip()
FAST_GENERAL_MODEL = os.environ.get("CELAR_FAST_GENERAL_MODEL", "llama3.2:3b").strip()


def _order(name: str, default: list[str]) -> list[str]:
    raw = os.environ.get(name, "").strip()
    values = [value.strip() for value in raw.split(",") if value.strip()] if raw else list(default)
    result: list[str] = []
    for value in values:
        if value not in result:
            result.append(value)
    return result


STRUCTURED_ORDER = _order(
    "CELAR_STRUCTURED_GENERATION_ORDER",
    [CONTRACT_MODEL, REASONING_MODEL, FAST_GENERAL_MODEL],
)
GENERAL_ORDER = _order(
    "CELAR_GENERAL_GENERATION_ORDER",
    [REASONING_MODEL, FAST_GENERAL_MODEL, CONTRACT_MODEL],
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

_original_extract = app.view_functions["extract"]


def _serialized_extract() -> Any:
    LOCK_PATH.parent.mkdir(parents=True, exist_ok=True)
    with LOCK_PATH.open("a+", encoding="utf-8") as lock_file:
        try:
            fcntl.flock(lock_file.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return jsonify({"error": {"code": "ocr_busy"}}), 503
        try:
            return _original_extract()
        finally:
            fcntl.flock(lock_file.fileno(), fcntl.LOCK_UN)


def _local_chat_completions() -> Any:
    """Preserve the public model contract while routing to local specialists.

    Failover is permitted only for local runtime/server failures. A 4xx response
    (including a policy/safety refusal from a future Ollama-compatible model)
    remains terminal and is not bypassed by trying another model.
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

    base_payload: dict[str, Any] = {
        "messages": messages,
        "stream": False,
        "max_tokens": max_tokens,
    }
    for name in ("temperature", "top_p", "seed", "stop", "response_format"):
        if name in payload:
            base_payload[name] = payload[name]

    last_body: dict[str, Any] = {"error": {"code": "private_runtime_unavailable"}}
    last_status = 502
    attempted: list[str] = []
    for candidate in candidates:
        if candidate not in APPROVED_GENERATION_MODELS:
            continue
        attempted.append(candidate)
        candidate_payload = dict(base_payload)
        candidate_payload["model"] = candidate
        body, status = gateway._ollama_post(
            "/v1/chat/completions",
            candidate_payload,
            gateway.CHAT_TIMEOUT_SECONDS,
            gateway.MAX_GATEWAY_RESPONSE_BYTES,
        )
        if status == 200:
            response = jsonify(body)
            response.headers["X-Celar-Local-Model"] = candidate
            response.headers["X-Celar-Local-Route"] = "structured" if structured else "general"
            return response, 200
        last_body, last_status = body, status
        if status < 500:
            break

    response = jsonify(last_body)
    response.headers["X-Celar-Local-Models-Attempted"] = ",".join(attempted)
    return response, last_status


app.view_functions["extract"] = _serialized_extract
app.view_functions["chat_completions"] = _local_chat_completions
