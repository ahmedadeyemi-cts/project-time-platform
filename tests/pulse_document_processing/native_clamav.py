"""Real clamd, isolated harmless signature. Not evidence of production database freshness."""
import hashlib
import subprocess
import tempfile
import time
from pathlib import Path
from clamd_client import BoundaryError, ClamdClient
with tempfile.TemporaryDirectory() as temporary:
    root = Path(temporary); database = root / "database"; database.mkdir()
    sentinel = b"Pulse scanner regression sentinel, harmless fixture.\n"
    (database / "fixture.hdb").write_text(f"{hashlib.md5(sentinel).hexdigest()}:{len(sentinel)}:Pulse.Regression.Sentinel\n")
    config = root / "clamd.conf"
    text = Path("/etc/clamav/clamd.conf").read_text()
    text = text.replace("DatabaseDirectory /var/lib/clamav", f"DatabaseDirectory {database}")
    text = text.replace("/run/clamav/clamd.sock", str(root / "clamd.sock"))
    text = text.replace("/run/clamav/clamd.pid", str(root / "clamd.pid"))
    config.write_text(text)
    daemon = subprocess.Popen(["clamd", "--foreground=true", "--config-file=" + str(config)],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            if (root / "clamd.sock").exists(): break
            if daemon.poll() is not None: raise AssertionError("clamd exited during fixture startup")
            time.sleep(0.1)
        client = ClamdClient(str(root / "clamd.sock"))
        for data, infected in [(b"Ordinary synthetic document.", False), (sentinel, True)]:
            source = root / "source.upload"; source.write_bytes(data)
            clean, observed, digest, size, _ = client._stream(source, time.monotonic() + 10)
            assert observed == infected and clean != infected
            assert digest == hashlib.sha256(data).hexdigest() and size == len(data)
        try: client.scan(source)
        except BoundaryError as error:
            assert error.code in {"signature_freshness_unavailable", "signature_database_stale"}
        else: raise AssertionError("Unknown freshness was accepted")
    finally:
        daemon.terminate()
        try: daemon.wait(timeout=10)
        except subprocess.TimeoutExpired: daemon.kill(); daemon.wait()
print("NATIVE_CLAMAV=PASS clean_and_detection=true unknown_freshness_rejected=true synthetic_signature_only=true")
