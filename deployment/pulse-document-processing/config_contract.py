"""Read-only candidate configuration checks; never runs containers or changes Azure."""
import os
import re
from pathlib import Path

def validate_image(value: str) -> None:
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9./:_-]+@sha256:[0-9a-f]{64}", value):
        raise ValueError("A reviewed immutable registry image digest is required")

def validate() -> None:
    validate_image(os.environ.get("PULSE_DOCUMENT_IMAGE", ""))
    validate_image(os.environ.get("PULSE_CLAMAV_IMAGE", ""))
    raw = os.environ.get("PULSE_DOCUMENT_TOKEN_FILE", "")
    if not raw or not Path(raw).is_absolute():
        raise ValueError("A protected absolute secret-file path outside the checkout is required")
    path = Path(raw)
    if path.is_symlink() or Path(__file__).resolve().parents[2] in path.resolve().parents:
        raise ValueError("Secret files cannot be symlinks or repository content")
    from gateway import load_token
    load_token(str(path))
    print("PULSE_DOCUMENT_CONFIG=PASS; deployment_authorization=NONE")

if __name__ == "__main__":
    validate()
