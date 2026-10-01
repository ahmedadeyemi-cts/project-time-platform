"""Deployment safety regressions: every Azure/account operation is mocked."""
import copy
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "deployment/pulse-services"))
import cutover
import test_resources as resources
from state_preservation import revision_is_ready, require_cleanup_ownership, require_new_service_names

SOURCE = "b" * 40
RUN = "101"
IMAGE = "registry/image@sha256:" + "a" * 64
NAME = resources.APPS["documents"]

def state(revision=None, run=RUN):
    revision = revision or NAME + "--svc-" + run
    return {"tags": {"source": SOURCE, "managedBy": "pulse-services-reviewed-cutover", "deploymentRun": run},
            "properties": {"provisioningState": "Succeeded", "latestRevisionName": revision,
                           "latestReadyRevisionName": revision,
                           "template": {"revisionSuffix": "svc-" + run,
                                        "containers": [{"name": "documents", "image": IMAGE}]}}}

class StatePreservationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        cutover.PRIVATE = Path(self.temp.name)
        cutover.SHA, cutover.RUN = SOURCE, RUN

    def test_previous_ready_revision_is_not_accepted(self):
        self.assertFalse(revision_is_ready(state(run="100"), NAME, NAME + "--svc-101", [IMAGE]))

    def test_target_latest_with_old_ready_is_not_accepted(self):
        body = state()
        body["properties"]["latestReadyRevisionName"] = NAME + "--svc-100"
        self.assertFalse(revision_is_ready(body, NAME, NAME + "--svc-101", [IMAGE]))

    def test_target_revision_and_images_are_both_required(self):
        self.assertTrue(revision_is_ready(state(), NAME, NAME + "--svc-101", [IMAGE]))
        with self.assertRaises(ValueError):
            revision_is_ready(state(), NAME, NAME + "--svc-101", ["wrong-image"])

    def test_unexpected_target_or_malformed_state_is_rejected(self):
        for body, name, revision in [(None, NAME, NAME + "--svc-101"), ({}, NAME, NAME + "--svc-101"),
                                      (state(), NAME, "other-app--svc-101"), (state(), NAME, NAME + "--")]:
            with self.subTest(revision=revision), self.assertRaises(ValueError):
                revision_is_ready(body, name, revision, [IMAGE])

    def test_matching_failed_revision_is_not_ready(self):
        body = state()
        body["properties"]["provisioningState"] = "Failed"
        with self.assertRaises(ValueError): revision_is_ready(body, NAME, NAME + "--svc-101", [IMAGE])

    def test_actual_waiter_waits_for_requested_revision(self):
        old, expected = state(run="100"), state()
        replicas = [{"properties": {"containers": [
            {"name": "documents", "ready": True, "runningState": "Running"}]}}]
        with patch.object(cutover, "get_app", side_effect=[old, expected]) as read, \
             patch.object(cutover.time, "sleep") as sleep, \
             patch.object(cutover, "az", return_value=replicas) as azure:
            self.assertIs(cutover.wait_app(NAME, [IMAGE], expected_revision=NAME + "--svc-101"), expected)
            self.assertEqual(read.call_count, 2)
            sleep.assert_called_once_with(10)
            azure.assert_called_once_with("containerapp", "replica", "list", "-g", resources.GROUP,
                                          "-n", NAME, "--revision", NAME + "--svc-101")

    def test_waiter_requires_explicit_target(self):
        with self.assertRaises(TypeError): cutover.wait_app(NAME)

    def test_same_source_newer_run_cannot_be_deleted(self):
        cutover.journal({"kind": "application", "name": NAME, "run": RUN, "source": SOURCE})
        with patch.object(cutover, "get_app", return_value=state(run="202")), patch.object(cutover, "rest") as writes:
            with self.assertRaises(ValueError): cutover.cleanup_staged()
            writes.assert_not_called()

    def test_original_run_still_can_clean_its_own_staged_app(self):
        cutover.journal({"kind": "application", "name": NAME, "run": RUN, "source": SOURCE})
        with patch.object(cutover, "get_app", return_value=state()), patch.object(cutover, "rest") as writes:
            cutover.cleanup_staged()
            writes.assert_called_once_with("DELETE", resources.ROOT + "/providers/Microsoft.App/containerApps/" + NAME)

    def test_missing_or_changed_ownership_never_authorizes_cleanup(self):
        for field in ("source", "managedBy", "deploymentRun"):
            body = state()
            body["tags"].pop(field)
            with self.subTest(field=field), self.assertRaises(ValueError):
                require_cleanup_ownership(body, SOURCE, RUN, application=True)
        body = state()
        body["properties"]["template"]["revisionSuffix"] = "svc-202"
        with self.assertRaises(ValueError): require_cleanup_ownership(body, SOURCE, RUN, application=True)

    def test_job_cleanup_reads_and_verifies_ownership_before_deleting(self):
        name = cutover.acceptance_job_name(RUN)
        cutover.journal({"kind": "job", "name": name, "run": RUN, "source": SOURCE})
        target = resources.ROOT + "/providers/Microsoft.App/jobs/" + name
        calls = []
        def azure(method, resource):
            calls.append((method, resource))
            return state()
        with patch.object(cutover, "rest", side_effect=azure): cutover.cleanup_staged()
        self.assertEqual(calls, [("GET", target), ("DELETE", target)])

    def test_newer_job_cannot_be_deleted(self):
        name = cutover.acceptance_job_name(RUN)
        cutover.journal({"kind": "job", "name": name, "run": RUN, "source": SOURCE})
        with patch.object(cutover, "rest", return_value=state(run="202")) as azure:
            with self.assertRaises(ValueError): cutover.cleanup_staged()
            self.assertEqual(azure.call_count, 1)
            self.assertEqual(azure.call_args.args[0], "GET")

    def test_initial_cutover_cannot_replace_existing_services(self):
        require_new_service_names({resources.API}, set(resources.APPS.values()))
        for name in resources.APPS.values():
            with self.subTest(name=name), self.assertRaises(ValueError):
                require_new_service_names({resources.API, name}, set(resources.APPS.values()))

    def test_actual_prepare_rejects_collision_before_storage_or_resource_writes(self):
        images = {n: resources.ACR + "/pulse-services-" + n + "@sha256:" + "a" * 64 for n in resources.COMPONENTS}
        path = cutover.PRIVATE / "images.json"
        path.write_text(json.dumps(images))
        with patch.dict(os.environ, {"PULSE_IMAGE_MANIFEST": str(path)}), \
             patch.object(cutover, "preflight"), patch.object(cutover, "az", return_value=[{"name": NAME}]), \
             patch.object(cutover, "signatures") as storage, patch.object(cutover, "rest") as writes:
            with self.assertRaises(ValueError): cutover.prepare()
            storage.assert_not_called()
            writes.assert_not_called()

    def test_retry_is_rejected_before_github_or_azure_access(self):
        variables = {"GITHUB_REPOSITORY": cutover.REPO, "GITHUB_REF": "refs/heads/main",
                     "GITHUB_ACTOR": "ahmedadeyemi-cts", "GITHUB_RUN_ATTEMPT": "2"}
        with patch.dict(os.environ, variables, clear=True), patch.object(cutover, "gh") as github, \
             patch.object(cutover, "az") as azure:
            with self.assertRaises(cutover.CutoverError): cutover.preflight()
            github.assert_not_called()
            azure.assert_not_called()

    def test_templates_tag_exact_deployment_run(self):
        images = {n: resources.ACR + "/pulse-services-" + n + "@sha256:" + "a" * 64 for n in resources.COMPONENTS}
        for kind in resources.APPS:
            body = resources.application(kind, images, "q" * 64, "fixture-token", SOURCE, RUN)
            self.assertEqual(body["tags"]["deploymentRun"], RUN)
            require_cleanup_ownership(body, SOURCE, RUN, application=True)

if __name__ == "__main__":
    unittest.main(verbosity=2)
