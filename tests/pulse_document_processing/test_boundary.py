"""Synthetic production-boundary tests; no application accounts or business data."""
import hashlib
import io
import stat
import tempfile
import threading
import unittest
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch
from clamd_client import BoundaryError, ClamdClient, Engine, Scan, parse_version
from gateway import OCR_MODEL, PRIVACY, create_app, load_token
from config_contract import validate_image

TOKEN = "synthetic-test-" + "x" * 40
HEADERS = {"Authorization": "Bearer " + TOKEN, "X-Pulse-AI-Privacy-Boundary": PRIVACY}
ENGINE = Engine("1.4.3", "daily-12345", "2026-09-30T00:00:00+00:00")

class ScannerFixture:
    def __init__(self):
        self.called, self.infected, self.error, self.mismatch = 0, False, None, False
    def ready(self):
        return ENGINE
    def scan(self, path):
        self.called += 1
        if self.error:
            raise self.error
        data = path.read_bytes()
        return Scan(not self.infected, self.infected,
                    "0" * 64 if self.mismatch else hashlib.sha256(data).hexdigest(),
                    len(data), ENGINE, "Synthetic.Detection" if self.infected else "")

class BoundaryTests(unittest.TestCase):
    def setUp(self):
        self.scanner, self.ocr_calls = ScannerFixture(), []
        def ocr(path, _):
            self.ocr_calls.append(path)
            self.assertEqual(path.name, "source.upload")
            self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o400)
            return {"pages": [{"pageNumber": 1, "text": "Synthetic OCR"}], "model": OCR_MODEL}
        self.app = create_app(TOKEN, self.scanner, ocr)
        self.client = self.app.test_client()
    def upload(self, route="/v1/scan", data=b"clean synthetic content", filename="sample.txt", headers=None, extra=None):
        form = {"file": (io.BytesIO(data), filename)}
        if route == "/v1/extract":
            form.update(model=OCR_MODEL, documentId="12345678-1234-4234-9234-123456789012")
        if extra:
            form.update(extra)
        return self.client.post(route, data=form, headers=HEADERS if headers is None else headers,
                                content_type="multipart/form-data")
    def test_auth_before_parsing(self):
        for headers in [{}, {"Authorization": "Bearer wrong"}, {"Authorization": "Bearer " + TOKEN}]:
            with self.subTest(headers=list(headers)):
                self.assertIn(self.upload(headers=headers).status_code, (401, 403))
        self.assertEqual(self.scanner.called, 0)
    def test_liveness_has_no_provider_requirement(self):
        self.assertEqual(self.client.get("/health/live").status_code, 200)
        self.assertEqual(self.client.get("/health").status_code, 401)
    def test_exact_hash_and_byte_receipt(self):
        response = self.upload()
        self.assertEqual(response.status_code, 200)
        self.assertTrue(response.json["clean"])
        self.assertEqual(response.json["sha256"], hashlib.sha256(b"clean synthetic content").hexdigest())
        self.assertEqual(response.json["sizeBytes"], len(b"clean synthetic content"))
        self.assertEqual(response.headers["Cache-Control"], "no-store")
    def test_ocr_requires_fresh_scan(self):
        self.assertEqual(self.upload("/v1/extract").status_code, 200)
        self.assertEqual((self.scanner.called, len(self.ocr_calls)), (1, 1))
        self.assertFalse(self.ocr_calls[0].exists())
    def test_infected_content_never_reaches_ocr(self):
        self.scanner.infected = True
        self.assertEqual(self.upload("/v1/extract").status_code, 422)
        self.assertEqual(self.ocr_calls, [])
        response = self.upload()
        self.assertFalse(response.json["clean"])
        self.assertTrue(response.json["infected"])
    def test_unavailable_stale_and_limit_are_not_clean(self):
        for code, status in [("scanner_unavailable", 503), ("signature_database_stale", 503),
                             ("scan_incomplete_or_encrypted", 422), ("scan_deadline_exceeded", 504)]:
            with self.subTest(code=code):
                self.scanner.error = BoundaryError(code, status)
                response = self.upload("/v1/extract", extra={"clean": "true"})
                self.assertEqual(response.status_code, status)
                self.assertFalse(response.json["clean"])
        self.assertEqual(self.ocr_calls, [])
    def test_scan_receipt_must_match_saved_bytes(self):
        self.scanner.mismatch = True
        self.assertEqual(self.upload("/v1/extract").status_code, 503)
        self.assertFalse(self.ocr_calls)
    def test_wrong_content_hash_fails_before_scan(self):
        self.assertEqual(self.upload(headers={**HEADERS, "X-Pulse-Content-SHA256": "0" * 64}).status_code, 409)
        self.assertEqual(self.scanner.called, 0)
    def test_hash_syntax_and_empty_content(self):
        self.assertEqual(self.upload(headers={**HEADERS, "X-Pulse-Content-SHA256": "bad"}).status_code, 400)
        self.assertEqual(self.upload(data=b"").status_code, 400)
        self.assertEqual(self.scanner.called, 0)
    def test_untrusted_filename_is_not_a_storage_path(self):
        response = self.upload("/v1/extract", filename="../../etc/private-file.png")
        self.assertEqual(response.status_code, 200)
        self.assertNotIn("private-file", response.get_data(as_text=True))
        self.assertFalse(self.ocr_calls[0].exists())
    def test_metadata_and_model_are_checked(self):
        for field, value in [("documentId", "../bad"), ("model", "arbitrary-model"), ("documentCategory", "a" * 129)]:
            with self.subTest(field=field):
                self.assertEqual(self.upload("/v1/extract", extra={field: value}).status_code, 400)
        self.assertEqual(self.scanner.called, 0)
    def test_duplicate_file_and_non_multipart_rejected(self):
        response = self.client.post("/v1/scan", data={"file": [(io.BytesIO(b"a"), "a"), (io.BytesIO(b"b"), "b")]},
                                    headers=HEADERS, content_type="multipart/form-data")
        self.assertEqual(response.status_code, 400)
        self.assertEqual(self.client.post("/v1/scan", json={"path": "/etc/passwd"}, headers=HEADERS).status_code, 415)
    def test_total_request_limit(self):
        self.app.config["MAX_CONTENT_LENGTH"] = 256
        self.assertEqual(self.upload(data=b"x" * 1024).status_code, 413)
        self.assertEqual(self.scanner.called, 0)
    def test_no_inference_or_embedding_routes(self):
        for path in ["/v1/chat/completions", "/v1/embeddings"]:
            self.assertEqual(self.client.post(path, json={}, headers=HEADERS).status_code, 404)
    def test_internal_errors_are_content_free(self):
        self.scanner.error = RuntimeError("SENSITIVE_ORIGINAL_TEXT token=" + TOKEN)
        response = self.upload()
        self.assertEqual(response.status_code, 500)
        self.assertNotIn("SENSITIVE", response.get_data(as_text=True))
        self.assertNotIn(TOKEN, response.get_data(as_text=True))
    def test_capacity_rejects_instead_of_unbounded_queue(self):
        entered, release = threading.Event(), threading.Event()
        original = self.scanner.scan
        def paused(path):
            entered.set(); release.wait(5)
            return original(path)
        self.scanner.scan = paused
        result = []
        def first():
            with self.app.test_client() as client:
                result.append(client.post("/v1/scan", data={"file": (io.BytesIO(b"a"), "a")}, headers=HEADERS))
        thread = threading.Thread(target=first); thread.start()
        try:
            self.assertTrue(entered.wait(3)); self.assertEqual(self.upload().status_code, 429)
        finally:
            release.set(); thread.join(5)
        self.assertEqual(result[0].status_code, 200)

class EngineAndSecretTests(unittest.TestCase):
    def test_only_immutable_candidate_images(self):
        validate_image("registry.example/pulse-documents@sha256:" + "a" * 64)
        for value in ["registry.example/pulse-documents:latest", "", "registry/x@sha256:bad", "registry/x@sha256:" + "a" * 63]:
            with self.subTest(value=value), self.assertRaises(ValueError): validate_image(value)
    def test_current_loaded_database(self):
        now = datetime(2026, 9, 30, 12, tzinfo=timezone.utc)
        engine = parse_version("ClamAV 1.4.3/12345/Wed Sep 30 11:00:00 2026", now)
        self.assertEqual(engine.signatures, "daily-12345")
    def test_stale_future_unknown_database_rejected(self):
        now = datetime(2026, 9, 30, 12, tzinfo=timezone.utc)
        for value in ["ClamAV 1.4.3", "ClamAV 1.4.3/123/Sun Sep 27 00:00:00 2026",
                      "ClamAV 1.4.3/123/Wed Sep 30 12:20:00 2026", "PONG", "ClamAV x/1/bad"]:
            with self.subTest(value=value), self.assertRaises(BoundaryError): parse_version(value, now)
    def test_signature_change_is_not_silently_accepted(self):
        client = ClamdClient()
        with patch.object(client, "ready", side_effect=[ENGINE, Engine("1.4.3", "daily-999", ENGINE.updated_at)]), \
             patch.object(client, "_stream", return_value=(True, False, "0" * 64, 1, "")):
            with self.assertRaisesRegex(BoundaryError, "signature_changed_retry"): client.scan(Path("unused"))
    def test_missing_socket_never_returns_clean(self):
        with self.assertRaises(BoundaryError):
            ClamdClient("/tmp/nonexistent-pulse-test.sock").scan(Path("unused"))
    def test_token_file_permissions_format_and_symlink(self):
        with tempfile.TemporaryDirectory() as root:
            token = Path(root) / "token"; token.write_text(TOKEN); token.chmod(0o600)
            self.assertEqual(load_token(str(token)), TOKEN)
            token.chmod(0o644)
            with self.assertRaises(RuntimeError): load_token(str(token))
            token.chmod(0o600); alias = Path(root) / "alias"; alias.symlink_to(token)
            with self.assertRaises(OSError): load_token(str(alias))
            token.write_text("short")
            with self.assertRaises(RuntimeError): load_token(str(token))
if __name__ == "__main__": unittest.main(verbosity=2)
