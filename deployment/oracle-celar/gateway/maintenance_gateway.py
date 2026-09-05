#!/usr/bin/env python3
"""Least-privilege maintenance/status plane for the Oracle Celar runtime.

This process runs as the unprivileged celar-ai account on 127.0.0.1:8788.
GET status accepts the ordinary read-only runtime token or the dedicated
maintenance token. PUT schedule accepts only the dedicated maintenance token
and writes a closed-schema desired-state file. A separate root-owned systemd
service validates and applies that file; this process never invokes systemctl,
sudo, a shell, or arbitrary commands.
"""

from __future__ import annotations

import hmac
import json
import os
import re
import socket
import subprocess
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import requests
from flask import Flask, Response, jsonify, request
from werkzeug.exceptions import RequestEntityTooLarge

PRIVACY_BOUNDARY = "private_pulse_runtime_only"
RUNTIME_TOKEN_FILE = Path(os.environ.get("CELAR_RUNTIME_TOKEN_FILE", "/etc/celar-ai/gateway/runtime-token"))
MAINTENANCE_TOKEN_FILE = Path(os.environ.get("CELAR_MAINTENANCE_TOKEN_FILE", "/etc/celar-ai/gateway/maintenance-token"))
DESIRED_FILE = Path(os.environ.get("CELAR_MAINTENANCE_DESIRED_FILE", "/var/lib/celar-ai/gateway/maintenance-desired.json"))
POLICY_STATUS_FILE = Path(os.environ.get("CELAR_MAINTENANCE_POLICY_STATUS_FILE", "/var/lib/celar-ai/gateway/maintenance-policy-status.json"))
UPDATE_STATUS_FILE = Path(os.environ.get("CELAR_UPDATE_STATUS_FILE", "/var/lib/celar-ai/gateway/update-status.json"))
OLLAMA_BASE_URL = os.environ.get("CELAR_OLLAMA_BASE_URL", "http://127.0.0.1:11434").rstrip("/")
CLAMAV_HOST = os.environ.get("CELAR_CLAMAV_HOST", "127.0.0.1").strip()
CLAMAV_PORT = int(os.environ.get("CELAR_CLAMAV_PORT", "3310"))
GATEWAY_VERSION = os.environ.get("CELAR_GATEWAY_VERSION", "unknown").strip()
GENERATION_MODELS = [value.strip() for value in os.environ.get("CELAR_LOCAL_GENERATION_MODELS", "").split(",") if value.strip()]
EMBEDDING_MODEL = os.environ.get("CELAR_EMBEDDING_MODEL", "embeddinggemma").strip()
DEFAULT_ENABLED = os.environ.get("CELAR_MAINTENANCE_ENABLED", "true").lower() == "true"
DEFAULT_DAY = os.environ.get("CELAR_MAINTENANCE_DAY_OF_WEEK", "Sunday").strip()
DEFAULT_LOCAL_TIME = os.environ.get("CELAR_MAINTENANCE_LOCAL_TIME", "01:00").strip()
DEFAULT_TIME_ZONE = os.environ.get("CELAR_MAINTENANCE_TIME_ZONE", "America/Chicago").strip()
MAX_BODY_BYTES = 8192

if OLLAMA_BASE_URL != "http://127.0.0.1:11434":
    raise RuntimeError("Maintenance status may query only localhost Ollama")
if CLAMAV_HOST != "127.0.0.1":
    raise RuntimeError("Maintenance status may query only localhost ClamAV")


def _read_secret(path: Path, label: str) -> str:
    if not path.is_file():
        raise RuntimeError(f"{label} file is missing")
    value = path.read_text(encoding="utf-8").strip()
    if len(value) < 32:
        raise RuntimeError(f"{label} is too short")
    return value


RUNTIME_TOKEN = _read_secret(RUNTIME_TOKEN_FILE, "runtime token")
MAINTENANCE_TOKEN = _read_secret(MAINTENANCE_TOKEN_FILE, "maintenance token")

app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = MAX_BODY_BYTES
app.config["JSON_SORT_KEYS"] = False
app.config["PROPAGATE_EXCEPTIONS"] = False
SESSION = requests.Session()
SESSION.trust_env = False

ALLOWED_DAYS = {
    "Monday": "Mon",
    "Tuesday": "Tue",
    "Wednesday": "Wed",
    "Thursday": "Thu",
    "Friday": "Fri",
    "Saturday": "Sat",
    "Sunday": "Sun",
}
TIME_PATTERN = re.compile(r"^(?:[01]\d|2[0-3]):[0-5]\d$")
REQUEST_ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$")


def _error(code: str, status: int, message: str | None = None) -> tuple[Response, int]:
    payload: dict[str, Any] = {"error": {"code": code}}
    if message:
        payload["error"]["message"] = message
    return jsonify(payload), status


def _supplied_token() -> str:
    auth = request.headers.get("Authorization", "")
    return auth[7:].strip() if auth.startswith("Bearer ") else ""


def _token_matches(candidate: str, expected: str) -> bool:
    return bool(candidate) and hmac.compare_digest(candidate, expected)


@app.before_request
def _authenticate() -> tuple[Response, int] | None:
    if request.headers.get("X-Pulse-AI-Privacy-Boundary", "") != PRIVACY_BOUNDARY:
        return _error("privacy_boundary_required", 403)
    supplied = _supplied_token()
    if request.method == "PUT":
        if not _token_matches(supplied, MAINTENANCE_TOKEN):
            return _error("maintenance_authorization_required", 401)
        if request.headers.get("X-Celar-Maintenance-Intent", "") != "schedule_update":
            return _error("maintenance_intent_required", 403)
        return None
    if not (_token_matches(supplied, RUNTIME_TOKEN) or _token_matches(supplied, MAINTENANCE_TOKEN)):
        return _error("unauthorized", 401)
    return None


@app.errorhandler(RequestEntityTooLarge)
def _too_large(_: RequestEntityTooLarge) -> tuple[Response, int]:
    return _error("request_too_large", 413)


@app.errorhandler(404)
def _not_found(_: Exception) -> tuple[Response, int]:
    return _error("not_found", 404)


@app.errorhandler(Exception)
def _unhandled(exception: Exception) -> tuple[Response, int]:
    app.logger.error("Celar maintenance request failed path=%s type=%s", request.path, type(exception).__name__)
    return _error("maintenance_gateway_failure", 500)


def _bounded_json() -> dict[str, Any]:
    length = request.content_length
    if length is not None and length > MAX_BODY_BYTES:
        raise RequestEntityTooLarge()
    raw = request.get_data(cache=True)
    if len(raw) > MAX_BODY_BYTES:
        raise RequestEntityTooLarge()
    try:
        parsed = json.loads(raw.decode("utf-8", errors="strict"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ValueError("invalid_json") from exc
    if not isinstance(parsed, dict):
        raise ValueError("json_object_required")
    return parsed


def _read_json_file(path: Path) -> dict[str, Any] | None:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
        return value if isinstance(value, dict) else None
    except (OSError, json.JSONDecodeError, UnicodeDecodeError):
        return None


def _ollama_snapshot() -> tuple[str, list[dict[str, Any]]]:
    engine_version = "unavailable"
    models: list[dict[str, Any]] = []
    try:
        version_response = SESSION.get(f"{OLLAMA_BASE_URL}/api/version", timeout=(2, 5), allow_redirects=False)
        version_response.raise_for_status()
        version_payload = version_response.json()
        if isinstance(version_payload, dict) and isinstance(version_payload.get("version"), str):
            engine_version = version_payload["version"][:120]
    except (requests.RequestException, ValueError):
        pass

    try:
        tags_response = SESSION.get(f"{OLLAMA_BASE_URL}/api/tags", timeout=(2, 8), allow_redirects=False)
        tags_response.raise_for_status()
        tags_payload = tags_response.json()
        raw_models = tags_payload.get("models", []) if isinstance(tags_payload, dict) else []
        approved = GENERATION_MODELS + ([EMBEDDING_MODEL] if EMBEDDING_MODEL else [])
        for configured in approved:
            selected = None
            for item in raw_models if isinstance(raw_models, list) else []:
                if not isinstance(item, dict):
                    continue
                name = item.get("name")
                if name == configured or (":" not in configured and name == f"{configured}:latest"):
                    selected = item
                    break
            if selected is None:
                models.append({"configuredName": configured, "installed": False})
                continue
            details = selected.get("details") if isinstance(selected.get("details"), dict) else {}
            models.append({
                "configuredName": configured,
                "installed": True,
                "installedName": selected.get("name"),
                "digest": selected.get("digest"),
                "modifiedAt": selected.get("modified_at"),
                "sizeBytes": selected.get("size"),
                "family": details.get("family"),
                "parameterSize": details.get("parameter_size"),
                "quantizationLevel": details.get("quantization_level"),
            })
    except (requests.RequestException, ValueError):
        models = [{"configuredName": name, "installed": False} for name in GENERATION_MODELS + [EMBEDDING_MODEL]]
    return engine_version, models


def _clamav_version() -> str:
    try:
        with socket.create_connection((CLAMAV_HOST, CLAMAV_PORT), timeout=3) as client:
            client.sendall(b"zVERSION\0")
            client.settimeout(3)
            response = client.recv(512).replace(b"\0", b"").decode("utf-8", errors="replace").strip()
            return response[:240] if response else "unavailable"
    except OSError:
        return "unavailable"


def _tesseract_version() -> str:
    try:
        result = subprocess.run(
            ["/usr/bin/tesseract", "--version"],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=4,
            check=False,
        )
        first = result.stdout.decode("utf-8", errors="replace").splitlines()[0].strip() if result.stdout else ""
        return first[:120] if result.returncode == 0 and first else "unavailable"
    except (OSError, subprocess.TimeoutExpired):
        return "unavailable"


def _default_policy() -> dict[str, Any]:
    return {
        "enabled": DEFAULT_ENABLED,
        "cadence": "weekly",
        "dayOfWeek": DEFAULT_DAY,
        "localTime": DEFAULT_LOCAL_TIME,
        "timeZone": DEFAULT_TIME_ZONE,
    }


@app.get("/v1/maintenance/status")
def status() -> tuple[Response, int]:
    engine_version, models = _ollama_snapshot()
    desired = _read_json_file(DESIRED_FILE) or _default_policy()
    policy_status = _read_json_file(POLICY_STATUS_FILE) or {}
    update_status = _read_json_file(UPDATE_STATUS_FILE) or {}
    response = {
        "module": "084",
        "status": "ready",
        "gatewayVersion": GATEWAY_VERSION,
        "ollama": {
            "engineVersion": engine_version,
            "models": models,
        },
        "components": {
            "tesseractVersion": _tesseract_version(),
            "clamavVersion": _clamav_version(),
        },
        "maintenance": {
            "desired": {
                "enabled": bool(desired.get("enabled", DEFAULT_ENABLED)),
                "cadence": "weekly",
                "dayOfWeek": str(desired.get("dayOfWeek", DEFAULT_DAY))[:16],
                "localTime": str(desired.get("localTime", DEFAULT_LOCAL_TIME))[:8],
                "timeZone": str(desired.get("timeZone", DEFAULT_TIME_ZONE))[:64],
            },
            "applied": policy_status,
            "update": update_status,
        },
        "security": {
            "maintenanceMutationUsesDedicatedCredential": True,
            "runtimeTokenMayChangeSchedule": False,
            "shellExecutionExposed": False,
            "secretValuesReturned": False,
        },
        "generatedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    return jsonify(response), 200


@app.put("/v1/maintenance/schedule")
def update_schedule() -> tuple[Response, int]:
    try:
        payload = _bounded_json()
    except ValueError:
        return _error("invalid_json", 400)

    allowed_keys = {"enabled", "dayOfWeek", "localTime", "timeZone", "requestId"}
    if set(payload) - allowed_keys:
        return _error("unsupported_schedule_field", 400)
    enabled = payload.get("enabled")
    day = payload.get("dayOfWeek")
    local_time = payload.get("localTime")
    time_zone = payload.get("timeZone")
    request_id = payload.get("requestId")
    if not isinstance(enabled, bool):
        return _error("maintenance_enabled_invalid", 400)
    if not isinstance(day, str) or day not in ALLOWED_DAYS:
        return _error("maintenance_day_invalid", 400)
    if not isinstance(local_time, str) or TIME_PATTERN.fullmatch(local_time) is None:
        return _error("maintenance_time_invalid", 400)
    if time_zone != "America/Chicago":
        return _error("maintenance_timezone_not_approved", 400)
    if not isinstance(request_id, str) or REQUEST_ID_PATTERN.fullmatch(request_id) is None:
        return _error("maintenance_request_id_invalid", 400)

    desired = {
        "schema": 1,
        "enabled": enabled,
        "cadence": "weekly",
        "dayOfWeek": day,
        "localTime": local_time,
        "timeZone": time_zone,
        "requestId": request_id,
        "requestedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    DESIRED_FILE.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=DESIRED_FILE.parent, delete=False) as handle:
        json.dump(desired, handle, separators=(",", ":"))
        handle.write("\n")
        temp_name = handle.name
    os.chmod(temp_name, 0o640)
    os.replace(temp_name, DESIRED_FILE)

    return jsonify({
        "module": "084",
        "status": "accepted_for_reconciliation",
        "schedule": {key: desired[key] for key in ("enabled", "cadence", "dayOfWeek", "localTime", "timeZone")},
        "requestId": request_id,
        "stateChanged": True,
        "systemCommandExecuted": False,
    }), 202
