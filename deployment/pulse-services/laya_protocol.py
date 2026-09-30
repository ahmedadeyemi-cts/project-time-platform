"""Bounded authenticated gateway adapter; never loads models or selects providers."""
from __future__ import annotations

import json
import math
import socket
import time
from typing import Any

VERSION = "celar-laya-decisions-v1"
REVISION = "1c5edc17a7acd8701df6fc341c0d179f1c62c982"
SCHEMA = "document_type_smoke_v1"
SOCKET_PATH = "/run/celar-laya/decision.sock"
LABELS = ("sow", "invoice", "purchase_order", "other")
MAX_REQUEST = 16384
MAX_RESPONSE = 16384


class DecisionError(Exception):
    def __init__(self, code: str, status: int = 502):
        self.code, self.status = code, status
        super().__init__(code)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate_key")
        result[key] = value
    return result


def decode(raw: bytes) -> dict:
    try:
        obj = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_object,
                         parse_constant=lambda _: (_ for _ in ()).throw(ValueError()))
        if not isinstance(obj, dict):
            raise ValueError()
        return obj
    except (ValueError, UnicodeError, RecursionError) as exc:
        raise DecisionError("decision_invalid_json") from exc


def request_payload(raw: bytes) -> dict:
    if len(raw) > MAX_REQUEST:
        raise DecisionError("decision_request_too_large", 413)
    try:
        data = decode(raw)
    except DecisionError as exc:
        raise DecisionError("decision_invalid_request", 400) from exc
    text = data.get("text")
    if set(data) != {"text"} or not isinstance(text, str) or not text.strip():
        raise DecisionError("decision_nonempty_text_required", 400)
    try:
        if len(text.encode("utf-8")) > 8192:
            raise DecisionError("decision_text_too_large", 413)
    except UnicodeError as exc:
        raise DecisionError("decision_invalid_request", 400) from exc
    return {"op": "classify", "text": text}


def exchange(payload: dict, *, path: str = SOCKET_PATH, seconds: float = 12.0) -> dict:
    """One request, total monotonic deadline, no queue/retry or network fallback."""
    deadline = time.monotonic() + seconds
    def remaining():
        left = deadline - time.monotonic()
        if left <= 0:
            raise TimeoutError()
        return left
    try:
        with socket.socket(socket.AF_UNIX, socket.SOCK_STREAM) as sock:
            sock.settimeout(remaining())
            sock.connect(path)
            encoded = json.dumps(payload, ensure_ascii=False, allow_nan=False).encode("utf-8") + b"\n"
            if len(encoded) > MAX_REQUEST:
                raise DecisionError("decision_request_too_large", 413)
            sock.settimeout(remaining())
            sock.sendall(encoded)
            received = bytearray()
            while b"\n" not in received:
                sock.settimeout(remaining())
                part = sock.recv(min(4096, MAX_RESPONSE + 1 - len(received)))
                if not part:
                    raise DecisionError("decision_incomplete_response")
                received.extend(part)
                if len(received) > MAX_RESPONSE:
                    raise DecisionError("decision_response_too_large")
            if received.count(b"\n") != 1 or not received.endswith(b"\n"):
                raise DecisionError("decision_invalid_framing")
        result = decode(bytes(received))
    except TimeoutError as exc:
        raise DecisionError("decision_timeout", 504) from exc
    except OSError as exc:
        raise DecisionError("decision_unavailable", 503) from exc
    if result.get("ok") is not True:
        # Never return arbitrary worker strings or payloads to an HTTP caller.
        code, status = {
            "busy": ("decision_busy", 503),
            "input_exceeds_model_budget": ("decision_input_exceeds_model_budget", 422),
            "text_too_large": ("decision_text_too_large", 413),
            "nonempty_text_required": ("decision_nonempty_text_required", 400),
        }.get(result.get("error") if isinstance(result.get("error"), str) else "", ("decision_worker_failed", 502))
        raise DecisionError(code, status)
    if result.get("model_revision") != REVISION:
        raise DecisionError("decision_model_revision_mismatch")
    return result


def normalized(result: dict, *, health: bool = False) -> dict:
    if result.get("ok") is not True or result.get("model_revision") != REVISION:
        raise DecisionError("decision_model_revision_mismatch")
    common = {"adapter_version": VERSION, "model_revision": REVISION,
              "review_required": True, "automation_approved": False,
              "workflow_actions_performed": 0, "external_fallback_allowed": False,
              "production_accuracy_validated": False}
    if health:
        if not (result.get("status") == "ready" and type(result.get("model_load_count")) is int and result["model_load_count"] == 1
                and type(result.get("state_token_budget")) is int and result["state_token_budget"] == 450):
            raise DecisionError("decision_runtime_not_ready", 503)
        return {**common, "ok": True, "status": "ready", "runtime_connected": True,
                "state_token_budget": 450, "supported_labels": list(LABELS),
                "inference_busy": result.get("inference_busy") is True}
    probabilities = result.get("probabilities")
    if (result.get("document_type") not in LABELS
            or not isinstance(probabilities, dict) or set(probabilities) != set(LABELS)
            or result.get("question_schema") != SCHEMA
            or result.get("review_required") is not True
            or result.get("automation_approved") is not False
            or result.get("input_truncated") is not False
            or type(result.get("workflow_actions_performed")) is not int
            or result["workflow_actions_performed"] != 0
            or result.get("confidence_is_probability_of_correctness") is not False):
        raise DecisionError("decision_invalid_contract")
    values = list(probabilities.values()) + [result.get("raw_model_confidence")]
    if not all(type(x) in (int, float) and math.isfinite(x) and 0 <= x <= 1 for x in values):
        raise DecisionError("decision_invalid_scores")
    latency = result.get("latency_ms")
    tokens = result.get("input_state_tokens")
    if (abs(sum(probabilities.values()) - 1) > .01
            or type(tokens) is not int or not 0 < tokens <= 450
            or type(latency) not in (int, float) or not math.isfinite(latency)
            or not 0 <= latency <= 30000):
        raise DecisionError("decision_invalid_contract")
    return {**common, "ok": True, "document_type": result["document_type"],
            "probabilities": probabilities, "raw_model_confidence": result["raw_model_confidence"],
            "confidence_is_probability_of_correctness": False, "input_truncated": False,
            "input_state_tokens": tokens, "latency_ms": latency, "question_schema": SCHEMA}


def register(app) -> None:
    # Imported only at gateway startup, after the existing authentication hook.
    from flask import jsonify, request
    if not any(getattr(h, "__name__", "") == "_authenticate"
               for h in app.before_request_funcs.get(None, [])):
        raise RuntimeError("Existing Celar gateway authentication must be registered first")

    def respond(health=False):
        try:
            if health:
                result = normalized(exchange({"op": "health"}, seconds=3), health=True)
            else:
                if request.mimetype != "application/json":
                    raise DecisionError("decision_json_required", 415)
                if request.content_length is not None and request.content_length > MAX_REQUEST:
                    raise DecisionError("decision_request_too_large", 413)
                payload = request_payload(request.stream.read(MAX_REQUEST + 1))
                result = normalized(exchange(payload))
            response = jsonify(result)
            response.headers["Cache-Control"] = "no-store"
            return response
        except DecisionError as exc:
            response = jsonify({"ok": False, "error": {"code": exc.code},
                                "review_required": True, "workflow_actions_performed": 0,
                                "external_fallback_allowed": False})
            response.status_code = exc.status
            response.headers["Cache-Control"] = "no-store"
            if exc.code == "decision_busy":
                response.headers["Retry-After"] = "3"
            return response

    app.add_url_rule("/v1/decisions/health", "laya_decision_health",
                     lambda: respond(True), methods=["GET"])
    app.add_url_rule("/v1/decisions/document-type", "laya_document_type",
                     respond, methods=["POST"])
