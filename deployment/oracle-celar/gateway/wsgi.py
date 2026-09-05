#!/usr/bin/env python3
"""WSGI entrypoint with a cross-worker OCR admission lock.

The gateway keeps multiple request workers available for health, scanning,
embeddings, and inference while allowing only one expensive OCR conversion at a
time on the 4-vCPU Oracle host.
"""

from __future__ import annotations

import fcntl
from pathlib import Path
from typing import Any

from flask import jsonify

from gateway import app

LOCK_PATH = Path("/var/lib/celar-ai/gateway/ocr.lock")
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


app.view_functions["extract"] = _serialized_extract
