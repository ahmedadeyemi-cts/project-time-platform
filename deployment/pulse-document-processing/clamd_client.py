"""Bounded Unix-socket ClamAV adapter. An uncertain result is never clean."""
from __future__ import annotations
import hashlib
import re
import socket
import struct
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

MAX_UPLOAD_BYTES = 32 * 1024 * 1024
MAX_SIGNATURE_AGE_SECONDS = 48 * 3600

class BoundaryError(Exception):
    def __init__(self, code: str, status: int = 503):
        super().__init__(code)
        self.code, self.status = code, status

@dataclass(frozen=True)
class Engine:
    version: str
    signatures: str
    updated_at: str

@dataclass(frozen=True)
class Scan:
    clean: bool
    infected: bool
    sha256: str
    size: int
    engine: Engine
    detection: str = ""

def parse_version(value: str, now: datetime | None = None) -> Engine:
    # VERSION describes clamd's loaded database, not a newer file on disk.
    match = re.fullmatch(r"ClamAV ([0-9][A-Za-z0-9.+~-]{0,63})/(\d{1,10})/(.{1,80})", value)
    if not match:
        raise BoundaryError("signature_freshness_unavailable")
    try:
        updated = datetime.strptime(match[3], "%a %b %d %H:%M:%S %Y").replace(tzinfo=timezone.utc)
    except ValueError:
        raise BoundaryError("signature_freshness_unavailable") from None
    age = ((now or datetime.now(timezone.utc)) - updated).total_seconds()
    if age < -300 or age > MAX_SIGNATURE_AGE_SECONDS:
        raise BoundaryError("signature_database_stale")
    return Engine(match[1], "daily-" + match[2], updated.isoformat())

class ClamdClient:
    def __init__(self, socket_path: str = "/run/clamav/clamd.sock", timeout: float = 45):
        self.socket_path, self.timeout = socket_path, timeout

    @staticmethod
    def _remaining(deadline: float) -> float:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise BoundaryError("scan_deadline_exceeded", 504)
        return remaining

    def _connect(self, deadline: float) -> socket.socket:
        client = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        try:
            client.settimeout(self._remaining(deadline))
            client.connect(self.socket_path)
            return client
        except BaseException:
            client.close()
            raise

    def _response(self, client: socket.socket, deadline: float, limit: int = 4096) -> str:
        reply = bytearray()
        while len(reply) <= limit:
            client.settimeout(self._remaining(deadline))
            part = client.recv(min(1024, limit + 1 - len(reply)))
            if not part:
                break
            reply.extend(part)
            if b"\0" in reply:
                break
        if len(reply) > limit or not reply.endswith(b"\0") or reply.count(b"\0") != 1:
            raise BoundaryError("scanner_response_invalid")
        try:
            return reply[:-1].decode("ascii")
        except UnicodeDecodeError:
            raise BoundaryError("scanner_response_invalid") from None

    def ready(self, deadline: float | None = None) -> Engine:
        deadline = deadline or time.monotonic() + 5
        try:
            with self._connect(deadline) as client:
                client.sendall(b"zVERSION\0")
                return parse_version(self._response(client, deadline, 512))
        except (OSError, TimeoutError):
            raise BoundaryError("scanner_unavailable") from None

    def _stream(self, path: Path, deadline: float) -> tuple[bool, bool, str, int, str]:
        """Raw transport result; callers must separately establish engine freshness."""
        size, digest = 0, hashlib.sha256()
        with self._connect(deadline) as client, path.open("rb") as stream:
            client.sendall(b"zINSTREAM\0")
            while chunk := stream.read(128 * 1024):
                size += len(chunk)
                if size > MAX_UPLOAD_BYTES:
                    raise BoundaryError("upload_limit_exceeded", 413)
                digest.update(chunk)
                client.settimeout(self._remaining(deadline))
                client.sendall(struct.pack(">I", len(chunk)) + chunk)
            if size == 0:
                raise BoundaryError("empty_file", 400)
            client.sendall(struct.pack(">I", 0))
            reply = self._response(client, deadline)
        if reply == "stream: OK":
            return True, False, digest.hexdigest(), size, ""
        match = re.fullmatch(r"stream: ([A-Za-z0-9._+:-]{1,200}) FOUND", reply)
        if match:
            if "Limits.Exceeded" in match[1] or "Encrypted" in match[1]:
                raise BoundaryError("scan_incomplete_or_encrypted", 422)
            return False, True, digest.hexdigest(), size, match[1]
        raise BoundaryError("scan_not_completed")

    def scan(self, path: Path) -> Scan:
        deadline = time.monotonic() + self.timeout
        try:
            before = self.ready(deadline)
            clean, infected, digest, size, detection = self._stream(path, deadline)
            after = self.ready(deadline)
            if after != before:
                raise BoundaryError("signature_changed_retry")
            return Scan(clean, infected, digest, size, after, detection)
        except (OSError, TimeoutError):
            raise BoundaryError("scanner_unavailable") from None
