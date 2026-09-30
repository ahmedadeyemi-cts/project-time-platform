"""Regression tests for service activation; all HTTP/cloud/account operations are mocked."""
import copy
import inspect
import os
import re
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "deployment/pulse-services"))
from laya_protocol import normalized, REVISION, LABELS
from activation_contracts import acceptance_job_name, validate_laya_health, validate_document_runtime
import acceptance
import cutover


def gateway_health():
    # Exercise the actual gateway serializer with the real worker's health shape.
    return normalized({"ok": True, "model_revision": REVISION, "status": "ready",
                       "model_load_count": 1, "state_token_budget": 450,
                       "inference_busy": False}, health=True)


def document_health():
    return {"status": "private_document_runtime_ready", "readiness": {
        "status": "private_document_runtime_ready", "clamAvConfigured": True,
        "malwareScannerEndpointPrivate": True, "ocrConfigured": True,
        "ocrEndpointPrivate": True, "workerEnabled": True,
        "automaticDocumentQueueEnabled": True, "documentServicePrincipalAuthorized": True,
        "processingTablesAvailable": True, "uploadStorageProductionReady": True}}


class ActivationContracts(unittest.TestCase):
    def test_actual_gateway_health_is_accepted_without_worker_only_fields(self):
        wire = gateway_health()
        self.assertNotIn("model_load_count", wire)
        self.assertIs(validate_laya_health(wire), wire)

    def test_laya_safety_fields_cannot_be_missing_or_changed(self):
        good = gateway_health()
        for field in ("adapter_version", "model_revision", "status", "ok", "runtime_connected",
                      "state_token_budget", "supported_labels", "review_required", "automation_approved",
                      "workflow_actions_performed", "external_fallback_allowed", "production_accuracy_validated",
                      "inference_busy"):
            for invalid in (None, "unexpected", [], 123):
                body = copy.deepcopy(good)
                body[field] = invalid
                with self.subTest(field=field, value=invalid), self.assertRaises(ValueError):
                    validate_laya_health(body)

    def test_bool_integer_substitution_is_rejected(self):
        for field, value in (("ok", 1), ("review_required", 1), ("automation_approved", 0),
                             ("workflow_actions_performed", False), ("state_token_budget", True)):
            body = gateway_health()
            body[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                validate_laya_health(body)

    def test_different_revision_and_label_set_are_rejected(self):
        body = gateway_health()
        body["model_revision"] = "0" * 40
        with self.assertRaises(ValueError): validate_laya_health(body)
        body = gateway_health()
        body["supported_labels"] = list(LABELS) + ["arbitrary_action"]
        with self.assertRaises(ValueError): validate_laya_health(body)

    def test_actual_acceptance_uses_public_health_validator(self):
        source = inspect.getsource(acceptance.main)
        self.assertIn("validate_laya_health(body)", source)
        self.assertNotIn("normalized(body,health=True)", source)

    def test_job_names_meet_azure_length_and_character_limits(self):
        for run_id in ("1", "36764663866", "9" * 20):
            name = acceptance_job_name(run_id)
            self.assertLessEqual(len(name), 31)
            self.assertRegex(name, r"^[a-z][a-z0-9-]*[a-z0-9]$")
            self.assertTrue(name.endswith(run_id))
        self.assertEqual(acceptance_job_name("36764663866"), "psvc-check-36764663866")

    def test_job_names_reject_invalid_run_identity(self):
        for value in ("", "0", "01", "-1", "1/../../other", "1 ", "1;command", "9" * 21, None, 123):
            with self.subTest(value=value), self.assertRaises(ValueError): acceptance_job_name(value)

    def test_creation_and_cleanup_share_the_same_name_contract(self):
        self.assertIn("job=acceptance_job_name(RUN)", inspect.getsource(cutover.prepare))
        self.assertIn("item['name']==acceptance_job_name(RUN)", inspect.getsource(cutover.cleanup_staged))

    def test_private_address_alone_cannot_pass_readiness(self):
        for state in ("private_document_runtime_partially_ready", "private_document_runtime_schema_unavailable"):
            body = document_health()
            body["status"] = body["readiness"]["status"] = state
            with self.assertRaises(ValueError): validate_document_runtime(body)
        self.assertIsInstance(validate_document_runtime(document_health()), dict)

    def test_each_required_document_capability_is_enforced(self):
        for field in document_health()["readiness"]:
            if field == "status": continue
            for invalid in (False, None, 1):
                body = document_health()
                body["readiness"][field] = invalid
                with self.subTest(field=field, value=invalid), self.assertRaises(ValueError):
                    validate_document_runtime(body)

    def test_partial_readiness_fails_and_closes_only_the_test_session(self):
        cutover.SHA = "b" * 40
        paths = []
        body = document_health()
        body["status"] = body["readiness"]["status"] = "private_document_runtime_partially_ready"
        def api(path, session="", payload=None):
            paths.append(path)
            responses = {
                "/api/auth/local/login": (200, {"provider": "LOCAL", "mustChangePassword": False, "sessionToken": "s" * 64}),
                "/api/security/context": (200, {"userId": "test-user", "isViewAs": False, "roles": [{"roleCode": "SUPER_ADMINISTRATOR"}]}),
                "/api/platform-operations/overview": (200, {"runtime": {"releaseSha": cutover.SHA}}),
                "/api/ai-configuration/decisions/laya/health": (200, {"status": "ready", "runtimeLocation": "pulse_container"}),
                "/api/celar-ai/v1/documents/runtime/readiness": (200, body),
                "/api/auth/session/logout": (204, {})}
            return responses[path]
        with patch.dict(os.environ, {"PROJECTPULSE_TEST_UAT_ADMIN_EMAIL": "fixture.local",
                                    "PROJECTPULSE_TEST_UAT_ADMIN_PASSWORD": "synthetic-test-only"}), \
             patch.object(cutover, "public_api", side_effect=api), patch.object(cutover, "rest") as azure:
            with self.assertRaises(ValueError): cutover.local_admin(after=True)
            azure.assert_not_called()
        self.assertEqual(paths[-1], "/api/auth/session/logout")
        self.assertFalse(any("reset" in path or "configure" in path for path in paths))


if __name__ == "__main__":
    unittest.main(verbosity=2)
