#!/usr/bin/env python3
"""Celar Laya local-only pilot worker. No workflow writes or cloud fallback.

Protocol: one UTF-8 JSON object and newline per Unix-domain connection.
This is a private pilot interface, not a production or public HTTP API.
"""
from __future__ import annotations

import os

# Set these before importing model libraries. systemd also restricts networking.
os.environ.update({
    "HF_HOME": "/tmp/pulse-laya-cache",
    "HF_HUB_OFFLINE": "1",
    "TRANSFORMERS_OFFLINE": "1",
    "HF_HUB_DISABLE_TELEMETRY": "1",
    "HF_HUB_DISABLE_IMPLICIT_TOKEN": "1",
    "USE_TF": "0",
    "USE_FLAX": "0",
    "TOKENIZERS_PARALLELISM": "false",
    "OMP_NUM_THREADS": "1",
    "OPENBLAS_NUM_THREADS": "1",
    "MKL_NUM_THREADS": "1",
})

import importlib.metadata
import json
import math
import re
import socket
import socketserver
import threading
import time
from pathlib import Path
from typing import Any

VERSION = "pulse-laya-container-1"
MODEL_ID = "convaiinnovations/laya"
REVISION = "1c5edc17a7acd8701df6fc341c0d179f1c62c982"
DATA = Path("/opt/pulse-laya-model")
SOCKET_PATH = "/run/celar-laya/decision.sock"
MAX_REQUEST_BYTES = 16384
MAX_TEXT_BYTES = 8192
INFERENCE_LIMIT_SECONDS = 30.0
QUESTIONS = {
    "document_type": {
        "type": "choice",
        "instructions": "Classify this document by its main purpose.",
        "criteria": {
            "sow": "Statement of work defining project scope and deliverables",
            "invoice": "Request for payment for goods or services",
            "purchase_order": "Buyer authorization to purchase goods or services",
            "other": "A document that does not fit these categories",
        },
    }
}
WARMUP_TEXT = (
    "INVOICE INV-TEST-001. Services provided: network installation. "
    "Total amount due: USD 2500. Payment terms: net 30 days."
)


def emit(event: str, **metadata: Any) -> None:
    # Deliberately never log request text, raw answers, or customer identifiers.
    print(json.dumps({"event": event, **metadata}, allow_nan=False), flush=True)


def error(code: str, **fields: Any) -> dict[str, Any]:
    return {"ok": False, "error": code, "review_required": True, **fields}


def unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("duplicate_json_key")
        value[key] = item
    return value


def decode_request(data: bytes) -> dict[str, Any]:
    if len(data) > MAX_REQUEST_BYTES or not data.endswith(b"\n"):
        raise ValueError("invalid_request_framing")
    obj = json.loads(data.decode("utf-8"), object_pairs_hook=unique_object)
    if not isinstance(obj, dict):
        raise ValueError("expected_json_object")
    return obj


def check_answer(result: Any) -> dict[str, Any]:
    """Validate model output; a model's own 'action' field is never executed."""
    answer = result["answers"]["document_type"]
    labels = QUESTIONS["document_type"]["criteria"]
    probabilities = answer["probabilities"]
    if answer["choice"] not in labels or set(probabilities) != set(labels):
        raise ValueError("invalid_model_labels")
    values = list(probabilities.values())
    confidence = answer["confidence"]
    if not all(type(v) in (int, float) and math.isfinite(v) and 0 <= v <= 1
               for v in values + [confidence]):
        raise ValueError("invalid_model_probabilities")
    if abs(sum(values) - 1.0) > 0.01:
        raise ValueError("invalid_probability_sum")
    return answer


def preflight() -> Path:
    from download_model import validate
    expected = {'laya':'0.3.4','transformers':'5.10.1','torch':'2.14.0+cpu'}
    for name,version in expected.items():
        if importlib.metadata.version(name)!=version:raise RuntimeError('model_dependency_identity')
    return validate(DATA)


class Runtime:
    def __init__(self, model_dir: Path) -> None:
        import torch
        import laya
        from laya.common import build_sequence, serialize_state

        torch.set_num_threads(1)
        torch.set_num_interop_threads(1)
        self.lock = threading.Lock()
        self.completed = 0
        self.created = time.monotonic()
        emit("model_loading", model_revision=REVISION, device="cpu")
        start = time.monotonic()
        self.agent = laya.load(str(model_dir), device="cpu")
        self.load_seconds = round(time.monotonic() - start, 2)
        self.serialize_state = serialize_state
        qdef = QUESTIONS["document_type"]
        internal = {"t": qdef["type"], "ins": qdef["instructions"], "crit": qdef["criteria"]}
        self.max_len = int(self.agent.cfg.get("max_len", 512))
        empty, markers = build_sequence(
            self.agent.tok, "", internal, self.max_len,
            int(self.agent.cfg.get("head_max_len", 192)),
        )
        self.state_token_budget = self.max_len - len(empty)
        if len(markers) != len(qdef["criteria"]) or self.state_token_budget < 32:
            raise RuntimeError("STOP: Unsupported tokenizer or question budget.")
        emit("model_loaded", load_seconds=self.load_seconds,
             max_sequence_tokens=self.max_len, state_token_budget=self.state_token_budget)
        warmup = self.classify(WARMUP_TEXT)
        if warmup.get("ok") is not True:
            raise RuntimeError("STOP: Warm-up inference failed.")
        self.completed = 0
        self.versions = {name: importlib.metadata.version(name)
                         for name in ("laya", "torch", "transformers", "safetensors")}

    def health(self) -> dict[str, Any]:
        memory = {}
        try:
            member = next(line[3:] for line in Path("/proc/self/cgroup").read_text().splitlines()
                          if line.startswith("0::"))
            group = Path("/sys/fs/cgroup") / member.lstrip("/")
            for name in ("memory.current", "memory.peak"):
                memory[name + "_mib"] = round(int((group / name).read_text()) / 1024**2, 2)
            memory["events"] = (group / "memory.events").read_text().strip()
        except (OSError, ValueError, StopIteration):
            memory = {"status": "unavailable"}
        return {
            "ok": True, "status": "ready", "service_version": VERSION,
            "model_repo": MODEL_ID, "model_revision": REVISION,
            "pid": os.getpid(), "model_load_count": 1, "device": "cpu",
            "mode": "pulse_container", "integration_enabled": True,
            "production_accuracy_validated": False,
            "load_seconds": self.load_seconds,
            "uptime_seconds": round(time.monotonic() - self.created, 2),
            "completed_requests": self.completed, "inference_busy": self.lock.locked(),
            "max_sequence_tokens": self.max_len, "state_token_budget": self.state_token_budget,
            "versions": self.versions, "cgroup_memory": memory,
        }

    def classify(self, text: Any) -> dict[str, Any]:
        if not isinstance(text, str) or not text.strip():
            return error("nonempty_text_required")
        try:
            if len(text.encode("utf-8")) > MAX_TEXT_BYTES:
                return error("text_too_large", maximum_text_bytes=MAX_TEXT_BYTES)
        except UnicodeError:
            return error("invalid_text_encoding")
        if not self.lock.acquire(blocking=False):
            return error("busy", retryable=True)
        timer = threading.Timer(INFERENCE_LIMIT_SECONDS, self.deadline_exceeded)
        timer.daemon = True
        timer.start()
        try:
            start = time.monotonic()
            state = {"text": text}
            serialized = self.serialize_state(state)
            mask = self.agent.tok.mask_token
            if mask:
                serialized = serialized.replace(mask, " ")
            tokens = self.agent.tok(serialized, add_special_tokens=False)["input_ids"]
            if len(tokens) > self.state_token_budget:
                return error("input_exceeds_model_budget", input_state_tokens=len(tokens),
                             maximum_state_tokens=self.state_token_budget,
                             input_truncated=False)
            result = self.agent.predict(state, QUESTIONS)
            answer = check_answer(result)
            elapsed = round((time.monotonic() - start) * 1000, 2)
            self.completed += 1
            emit("decision_completed", latency_ms=elapsed)
            return {
                "ok": True, "document_type": answer["choice"],
                "probabilities": answer["probabilities"],
                "raw_model_confidence": answer["confidence"],
                "confidence_is_probability_of_correctness": False,
                "review_required": True, "automation_approved": False,
                "workflow_actions_performed": 0, "production_accuracy_validated": False,
                "input_truncated": False, "input_state_tokens": len(tokens),
                "latency_ms": elapsed, "model_revision": REVISION,
                "service_pid": os.getpid(), "question_schema": "document_type_smoke_v1",
            }
        except Exception as exc:
            emit("decision_error", exception_type=type(exc).__name__)
            return error("inference_failed")
        finally:
            timer.cancel()
            self.lock.release()

    @staticmethod
    def deadline_exceeded() -> None:
        # This stops ONLY this worker; Restart=no prevents an automatic restart loop.
        os.write(2, b'LAYA_INFERENCE_DEADLINE_EXCEEDED; worker stopping\n')
        os._exit(124)


class Handler(socketserver.StreamRequestHandler):
    def handle(self) -> None:
        self.request.settimeout(5.0)
        try:
            request = decode_request(self.rfile.readline(MAX_REQUEST_BYTES + 1))
            op = request.get("op")
            if op == "health" and set(request) == {"op"}:
                response = self.server.runtime.health()
            elif op == "classify" and set(request) == {"op", "text"}:
                response = self.server.runtime.classify(request["text"])
            else:
                response = error("unsupported_request")
        except (ValueError, UnicodeError, RecursionError, TimeoutError):
            response = error("invalid_request")
        except OSError:
            return
        try:
            self.wfile.write(json.dumps(response, allow_nan=False).encode("utf-8") + b"\n")
            self.wfile.flush()
        except OSError:
            pass


class LocalServer(socketserver.ThreadingMixIn, socketserver.UnixStreamServer):
    daemon_threads = True
    block_on_close = False
    request_queue_size = 4

    def __init__(self, path: str, runtime: Runtime):
        self.runtime = runtime
        self.slots = threading.BoundedSemaphore(4)
        super().__init__(path, Handler)
        os.chmod(path, 0o660)

    def process_request(self, request, client_address):
        if not self.slots.acquire(blocking=False):
            try:
                request.settimeout(0.2)
                request.sendall(b'{"ok":false,"error":"busy","review_required":true}\n')
            except OSError:
                pass
            finally:
                self.shutdown_request(request)
            return
        try:
            super().process_request(request, client_address)
        except Exception:
            self.slots.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self.slots.release()

    def handle_error(self, request, client_address):
        emit("request_handler_error")


def notify_ready() -> None:
    emit('container_model_ready', model_revision=REVISION)


def main() -> None:
    from runtime import harden
    harden('laya')
    model_dir=preflight()
    socket=Path(SOCKET_PATH)
    if socket.exists() or socket.is_symlink():
        import stat
        info=socket.lstat()
        if not stat.S_ISSOCK(info.st_mode) or info.st_uid!=os.getuid():
            raise RuntimeError('unexpected_decision_socket')
        socket.unlink()
    runtime=Runtime(model_dir)
    with LocalServer(SOCKET_PATH,runtime) as server:
        notify_ready()
        server.serve_forever(poll_interval=0.2)


if __name__ == "__main__":
    main()
