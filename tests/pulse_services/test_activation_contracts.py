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

    def test_partial_status_requires_full_service_capability_evidence(self):
        body = document_health()
        body["status"] = body["readiness"]["status"] = "private_document_runtime_partially_ready"
        self.assertIsInstance(validate_document_runtime(body), dict)
        for field in ("malwareScannerEndpointPrivate", "ocrEndpointPrivate", "workerEnabled",
                      "documentServicePrincipalAuthorized", "uploadStorageProductionReady"):
            failed = copy.deepcopy(body); failed["readiness"][field] = False
            with self.subTest(field=field), self.assertRaises(ValueError):
                validate_document_runtime(failed)
        unavailable = document_health()
        unavailable["status"] = unavailable["readiness"]["status"] = "private_document_runtime_schema_unavailable"
        with self.assertRaises(ValueError): validate_document_runtime(unavailable)
        self.assertIsInstance(validate_document_runtime(document_health()), dict)

    def test_each_required_document_capability_is_enforced(self):
        for field in document_health()["readiness"]:
            if field == "status": continue
            for invalid in (False, None, 1):
                body = document_health()
                body["readiness"][field] = invalid
                with self.subTest(field=field, value=invalid), self.assertRaises(ValueError):
                    validate_document_runtime(body)

    def test_partial_service_readiness_can_pass_and_closes_only_the_test_session(self):
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
            self.assertRegex(cutover.local_admin(after=True), r"^[0-9a-f]{64}$")
            azure.assert_not_called()
        self.assertEqual(paths[-1], "/api/auth/session/logout")
        self.assertFalse(any("reset" in path or "configure" in path for path in paths))


class NativeAcceptanceEvidence(unittest.TestCase):
    def setUp(self):
        import acceptance_evidence
        self.e=acceptance_evidence
        self.e.set_stage('initialization')
    def test_original_acceptance_assertions_remain_identical(self):
        import ast,subprocess
        base='8102500a1816aa6551c6130fb5b4c68d609f74a7'
        original=subprocess.check_output(['git','show',base+':deployment/pulse-services/acceptance.py'],cwd=ROOT,text=True)
        current=(ROOT/'deployment/pulse-services/acceptance.py').read_text()
        assertions=lambda text:[ast.dump(node,include_attributes=False) for node in ast.walk(ast.parse(text)) if isinstance(node,ast.Assert)]
        self.assertEqual(assertions(original),assertions(current))
        self.assertGreater(len(assertions(current)),10)
    def test_only_known_failure_codes_are_exported(self):
        import json
        self.e.set_stage('documents_health')
        for error in (RuntimeError('PRIVATE_SECRET'),ValueError('https://private.invalid?token=PRIVATE_SECRET'),AssertionError('PRIVATE_SECRET')):
            result=self.e.failure_record(error)
            self.assertFalse(result['passed']);self.assertNotIn('PRIVATE_SECRET',json.dumps(result))
            self.assertEqual(result['stage'],'documents_health')
        self.assertEqual(self.e.failure_record(ValueError('private_dns_rejected'))['diagnostic'],'private_dns_rejected')
    def test_diagnostic_address_classes_do_not_export_addresses(self):
        import json
        self.e.record_dns_addresses(['100.100.0.12','10.30.0.167','127.0.0.1','169.254.169.254','8.8.8.8'])
        result=self.e.failure_record(ValueError('private_dns_rejected'))
        self.assertEqual(result['dnsAddressClasses'],['azure_platform_reserved','link_local','loopback','other_address','rfc1918'])
        for value in ('100.100.0.12','10.30.0.167','127.0.0.1','169.254.169.254','8.8.8.8'):self.assertNotIn(value,json.dumps(result))
    def test_unknown_stage_is_rejected(self):
        with self.assertRaises(ValueError):self.e.set_stage('private customer name')
        self.assertEqual(self.e.failure_record(ValueError())['stage'],'initialization')
    def test_new_stage_clears_stale_http_and_dns_evidence(self):
        self.e.record_http_status(401);self.e.record_dns_addresses(['10.0.0.1'])
        self.e.set_stage('laya_health');result=self.e.failure_record(ValueError())
        self.assertIsNone(result['httpStatus']);self.assertEqual(result['dnsAddressClasses'],[])
    def test_http_status_cannot_contain_arbitrary_text_or_boolean(self):
        for status in ('PRIVATE_SECRET',True,99,600,None):
            self.e.record_http_status(status);self.assertIsNone(self.e.failure_record(ValueError())['httpStatus'])
        self.e.record_http_status(503);self.assertEqual(self.e.failure_record(ValueError())['httpStatus'],503)
    def test_json_tls_timeout_and_dns_errors_are_distinguishable(self):
        import socket,ssl,json
        for error,code in ((json.JSONDecodeError('PRIVATE_SECRET','PRIVATE_BODY',0),'response_invalid_json'),(ssl.SSLCertVerificationError('PRIVATE_SECRET'),'tls_verification_failed'),(socket.gaierror('PRIVATE_SECRET'),'dns_resolution_failed'),(TimeoutError('PRIVATE_SECRET'),'request_deadline_exceeded')):
            self.assertEqual(self.e.failure_record(error)['diagnostic'],code)
    def test_platform_reserved_addresses_are_authorized_for_internal_container_apps(self):
        import acceptance,socket
        from unittest.mock import Mock,patch
        self.e.set_stage('documents_unauthorized')
        for address in ('100.100.0.12','100.100.128.12','100.100.160.12','100.100.192.12'):
            with self.subTest(address=address):
                client=acceptance.PinnedHTTPS('ca-phd-test-documents-westus3.internal.'+acceptance.DOMAIN)
                row=(socket.AF_INET,socket.SOCK_STREAM,6,'',(address,443))
                context=Mock();context.wrap_socket.return_value=Mock()
                with patch.object(acceptance.socket,'getaddrinfo',return_value=[row]), \
                     patch.object(acceptance.socket,'create_connection',return_value=Mock()) as connect, \
                     patch.object(acceptance.ssl,'create_default_context',return_value=context):
                    client.connect()
                connect.assert_called_once()
                self.assertEqual(self.e.failure_record(ValueError())['dnsAddressClasses'],['azure_platform_reserved'])
    def test_non_private_non_platform_addresses_remain_rejected(self):
        import acceptance,socket
        from unittest.mock import patch
        for address in ('8.8.8.8','127.0.0.1','169.254.169.254','100.99.255.255','100.100.224.1'):
            with self.subTest(address=address):
                client=acceptance.PinnedHTTPS('ca-phd-test-documents-westus3.internal.'+acceptance.DOMAIN)
                row=(socket.AF_INET,socket.SOCK_STREAM,6,'',(address,443))
                with patch.object(acceptance.socket,'getaddrinfo',return_value=[row]), \
                     patch.object(acceptance.socket,'create_connection') as connect:
                    with self.assertRaisesRegex(ValueError,'private_dns_rejected'):client.connect()
                    connect.assert_not_called()
    def test_container_includes_diagnostic_helper(self):
        source=(ROOT/'deployment/pulse-services/Dockerfile.documents').read_text()
        self.assertIn('deployment/pulse-services/acceptance_evidence.py',source)


if __name__ == "__main__":
    unittest.main(verbosity=2)
