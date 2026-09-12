#!/usr/bin/env python3
"""Offline regression tests: no cloud, application, or model requests."""
from __future__ import annotations
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from urllib.request import Request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("resolver", ROOT / "scripts/release-test/resolve-flowhive-installed-deployment.py")
resolver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resolver)
REPO = "ahmedadeyemi-cts/project-time-platform"
APP, CONTROLLER, VERIFIER = "a" * 40, "b" * 40, "c" * 40
API = "registry.invalid/api@sha256:" + "d" * 64
WEB = "registry.invalid/web@sha256:" + "e" * 64


def case(conclusion="failure"):
    run = {"id": 1234, "workflow_id": resolver.WORKFLOW_ID,
           "path": resolver.WORKFLOW_PATH, "event": "workflow_dispatch",
           "head_branch": "main", "head_sha": CONTROLLER,
           "repository": {"full_name": REPO}, "run_attempt": 1,
           "status": "completed", "conclusion": conclusion, "updated_at": "2026-09-11T20:00:00Z"}
    steps = [{"name": name, "number": index + 1, "status": "completed", "conclusion": "success"}
             for index, name in enumerate(resolver.REQUIRED_STEPS)]
    steps += [{"name": "Verify PSA candidate health and the live SOW-to-WBS lifecycle",
               "number": len(steps) + 1, "status": "completed", "conclusion": conclusion}]
    steps += [{"name": name, "number": len(steps) + index + 1,
               "status": "completed", "conclusion": "skipped"} for index, name in enumerate(resolver.ROLLBACK_STEPS)]
    jobs = [{"name": resolver.DEPLOY_JOB, "run_id": run["id"], "status": "completed", "conclusion": conclusion, "steps": steps}]
    manifest = {"repository": REPO, "environment": "test", "sha": APP,
                "branch": "release/flowhive-sow-successor-20260908", "pullRequest": 915,
                "allowCustomerPublication": False, "allowCanonicalTaskAdoption": False,
                "migrations": [{"file": "103_first.sql"}, {"file": "107_forward.sql"}]}
    receipts = {
        "deployment-identity.json": {"environment": "test", "productionMutation": False,
            "applicationSha": APP, "applicationBranch": manifest["branch"],
            "deploymentRunId": "1234", "deploymentAttempt": "1", "controllerSha": CONTROLLER,
            "apiRevision": "api--rela-1234-1", "webRevision": "web--relw-1234-1", "apiImage": API, "webImage": WEB},
        "immutable-images.json": {"apiImage": API, "webImage": WEB},
        "flowhive-psa-migrations.json": {"status": "applied_and_verified", "environment": "test",
            "productionMutation": False, "releaseCommit": APP, "controlCommit": CONTROLLER,
            "migrations": ["103_first", "107_forward"]},
        "deployment-health-verified.json": {"deploymentHealthVerified": True, "sourceCommit": APP, "productionMutation": False},
    }
    return run, jobs, manifest, receipts


def pack(run, receipts):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        for name, value in receipts.items():
            bundle.writestr(name, json.dumps(value))
    archive = buffer.getvalue()
    artifact = {"id": 5678, "name": f"systemwide-enterprise-reliability-test-evidence-{run['id']}-{run['run_attempt']}",
                "expired": False, "digest": "sha256:" + hashlib.sha256(archive).hexdigest(),
                "workflow_run": {"id": run["id"], "head_branch": "main", "head_sha": run["head_sha"]}}
    return artifact, archive


def validate(data, artifact_mutation=None):
    run, jobs, manifest, receipts = data
    artifact, archive = pack(run, receipts)
    if artifact_mutation:
        artifact_mutation(artifact)
    return resolver.resolve(run, jobs, artifact, archive, manifest, REPO, VERIFIER)


class ResolutionTests(unittest.TestCase):
    def test_failed_acceptance_keeps_installation_and_failure_separate(self):
        context = validate(case())
        self.assertEqual(context["controllerSha"], CONTROLLER)
        self.assertEqual(context["verificationCodeSha"], VERIFIER)
        self.assertEqual(context["deploymentConclusion"], "failure")
        self.assertTrue(context["installationVerified"])
        self.assertTrue(context["liveIdentityRequired"])
        self.assertFalse(context["businessWritesPermitted"])
        self.assertFalse(context["functionalAcceptanceVerified"])
        self.assertEqual(len(context["failedAcceptanceSteps"]), 1)

    def test_successful_deployment_remains_supported(self):
        context = validate(case("success"))
        self.assertEqual(context["deploymentConclusion"], "success")
        self.assertEqual(context["failedAcceptanceSteps"], [])

    def test_each_installation_step_must_have_succeeded(self):
        for name in resolver.REQUIRED_STEPS:
            with self.subTest(name=name):
                data = case()
                next(s for s in data[1][0]["steps"] if s["name"] == name)["conclusion"] = "failure"
                with self.assertRaisesRegex(resolver.ResolutionError, "installation_step_not_successful"):
                    validate(data)

    def test_rollback_cannot_be_present_even_when_images_were_installed(self):
        for name in resolver.ROLLBACK_STEPS:
            with self.subTest(name=name):
                data = case()
                next(s for s in data[1][0]["steps"] if s["name"] == name)["conclusion"] = "success"
                with self.assertRaisesRegex(resolver.ResolutionError, "rollback_not_excluded"):
                    validate(data)

    def test_unknown_failure_is_not_waived(self):
        data = case()
        data[1][0]["steps"].append({"name": "Unknown cleanup", "status": "completed", "conclusion": "failure"})
        with self.assertRaisesRegex(resolver.ResolutionError, "non_acceptance_failure_unresolved"):
            validate(data)

    def test_missing_or_duplicate_job_or_step_is_rejected(self):
        for mutation in (lambda d: d[1].clear(), lambda d: d[1].append(copy.deepcopy(d[1][0])),
                         lambda d: d[1][0]["steps"].append(copy.deepcopy(d[1][0]["steps"][0]))):
            data = case()
            mutation(data)
            with self.assertRaises(resolver.ResolutionError):
                validate(data)

    def test_queued_cancelled_timed_out_runs_are_not_installations(self):
        for status, conclusion in (("queued", None), ("in_progress", None), ("completed", "cancelled"), ("completed", "timed_out")):
            with self.subTest(status=status, conclusion=conclusion):
                data = case()
                data[0].update(status=status, conclusion=conclusion)
                with self.assertRaisesRegex(resolver.ResolutionError, "selected_run_not_terminal"):
                    validate(data)

    def test_other_workflow_repository_and_branch_are_rejected(self):
        for mutation in (lambda d: d[0].update(workflow_id=1), lambda d: d[0].update(head_branch="other"),
                         lambda d: d[0].update(event="pull_request"), lambda d: d[0].update(repository={"full_name": "other/repo"})):
            data = case()
            mutation(data)
            with self.assertRaises(resolver.ResolutionError):
                validate(data)

    def test_wrong_attempt_artifact_is_rejected(self):
        with self.assertRaisesRegex(resolver.ResolutionError, "artifact_attempt_mismatch"):
            validate(case(), lambda a: a.update(name="systemwide-enterprise-reliability-test-evidence-1234-2"))

    def test_unbound_or_expired_artifact_is_rejected(self):
        for mutation in (lambda a: a.update(expired=True), lambda a: a["workflow_run"].update(id=4321),
                         lambda a: a["workflow_run"].update(head_sha=VERIFIER)):
            with self.assertRaises(resolver.ResolutionError):
                validate(case(), mutation)

    def test_artifact_digest_must_match_actual_download(self):
        with self.assertRaisesRegex(resolver.ResolutionError, "artifact_digest_mismatch"):
            validate(case(), lambda a: a.update(digest="sha256:" + "0" * 64))

    def test_missing_or_duplicate_receipt_is_rejected(self):
        data = case()
        data[3].pop("deployment-identity.json")
        with self.assertRaisesRegex(resolver.ResolutionError, "receipt_missing_or_ambiguous"):
            validate(data)
        data = case()
        data[3]["other/deployment-identity.json"] = data[3]["deployment-identity.json"]
        with self.assertRaisesRegex(resolver.ResolutionError, "receipt_missing_or_ambiguous"):
            validate(data)

    def test_recorded_controller_is_not_replaced_by_new_verifier(self):
        data = case()
        data[3]["deployment-identity.json"]["controllerSha"] = VERIFIER
        with self.assertRaisesRegex(resolver.ResolutionError, "run_binding_invalid"):
            validate(data)

    def test_image_disagreement_or_mutable_tag_is_rejected(self):
        for value in ("registry.invalid/api:latest", WEB):
            data = case()
            data[3]["deployment-identity.json"]["apiImage"] = value
            with self.assertRaisesRegex(resolver.ResolutionError, "image_receipt_mismatch"):
                validate(data)

    def test_wrong_or_broadened_approval_is_rejected(self):
        for mutation in (lambda m: m.update(sha=VERIFIER), lambda m: m.update(allowCustomerPublication=True),
                         lambda m: m.update(allowCanonicalTaskAdoption=True), lambda m: m.update(environment="production")):
            data = case()
            mutation(data[2])
            with self.assertRaises(resolver.ResolutionError):
                validate(data)

    def test_missing_migration_or_migration_release_mismatch_is_rejected(self):
        for mutation in (lambda r: r.update(status="started"), lambda r: r.update(releaseCommit=VERIFIER),
                         lambda r: r.update(controlCommit=VERIFIER), lambda r: r.update(migrations=["103_first"]),
                         lambda r: r.update(productionMutation=True)):
            data = case()
            mutation(data[3]["flowhive-psa-migrations.json"])
            with self.assertRaisesRegex(resolver.ResolutionError, "migrations_not_verified"):
                validate(data)

    def test_health_receipt_does_not_replace_installation_proof(self):
        data = case()
        data[3]["deployment-health-verified.json"]["sourceCommit"] = VERIFIER
        with self.assertRaisesRegex(resolver.ResolutionError, "health_not_verified"):
            validate(data)

    def test_same_image_fixture_cleanup_preserves_original_revision(self):
        data = case()
        data[1][0]["steps"].append({"name": "Run protected-Test assigned-work visibility UAT", "status": "completed", "conclusion": "success"})
        data[3]["module001b-revision-reconcile.json"] = {
            "phase": "converged", "expectedRevision": "api--m1bd-1234-1", "latestReadyRevision": "api--m1bd-1234-1",
            "expectedRevisionActive": True, "trafficWeight": "100", "activeRevisions": ["api--m1bd-1234-1"],
            "healthState": "Healthy", "productionMutation": False, "expectedImage": API, "observedImage": API}
        context = validate(data)
        self.assertEqual(context["initialApiRevision"], "api--rela-1234-1")
        self.assertEqual(context["apiRevision"], "api--m1bd-1234-1")
        data[3]["module001b-revision-reconcile.json"]["observedImage"] = WEB
        with self.assertRaisesRegex(resolver.ResolutionError, "fixture_cleanup_not_reconciled"):
            validate(data)

    def test_cross_host_download_redirect_does_not_forward_github_token(self):
        request = Request("https://api.github.com/repos/o/r/actions/artifacts/1/zip", headers={"Authorization": "Bearer " + "x" * 24})
        redirected = resolver.SafeRedirect().redirect_request(request, None, 302, "Found", {}, "https://artifact.example.invalid/file")
        self.assertIsNone(redirected.get_header("Authorization"))
        with self.assertRaisesRegex(resolver.ResolutionError, "insecure_github_redirect"):
            resolver.SafeRedirect().redirect_request(request, None, 302, "Found", {}, "http://artifact.example.invalid/file")

    def test_inventory_reads_all_pages_and_rejects_inconsistent_total(self):
        batches = [{"total_count": 101, "jobs": [{"id": i} for i in range(100)]}, {"total_count": 101, "jobs": [{"id": 100}]}]
        with patch.object(resolver, "api_json", side_effect=batches) as api:
            self.assertEqual(len(resolver.inventory("/repos/o/r/jobs", "jobs", "")), 101)
            self.assertEqual(api.call_count, 2)
        batches[1]["total_count"] = 102
        with patch.object(resolver, "api_json", side_effect=batches), self.assertRaisesRegex(resolver.ResolutionError, "inventory_changed"):
            resolver.inventory("/repos/o/r/jobs", "jobs", "")

    def exercise_main(self, comparison="ahead", drift=False):
        data = case()
        run, jobs, manifest, receipts = data
        artifact, archive = pack(run, receipts)
        run_reads = 0
        calls = []
        def api(path, token):
            nonlocal run_reads
            calls.append(path)
            if path.endswith("/actions/runs/1234"):
                run_reads += 1
                result = copy.deepcopy(run)
                if drift and run_reads > 1:
                    result["run_attempt"] = 2
                return result
            if "/compare/" in path:
                return {"status": comparison, "merge_base_commit": {"sha": CONTROLLER}}
            if "/attempts/1/jobs?" in path:
                return {"total_count": 1, "jobs": jobs}
            if "/artifacts?" in path:
                return {"total_count": 1, "artifacts": [artifact]}
            raise AssertionError("Unexpected mocked request: " + path)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".github").mkdir()
            (root / ".github/flowhive-psa-protected-test-candidate.json").write_text(json.dumps(manifest))
            output = root / "context.json"
            output.write_text('{"old":true}')
            env = {"OUTPUT_CONTEXT": str(output), "GH_TOKEN": "x" * 24, "GITHUB_REPOSITORY": REPO,
                   "GITHUB_SHA": VERIFIER, "DEPLOYMENT_RUN_ID": "1234"}
            previous = Path.cwd()
            try:
                os.chdir(root)
                with patch.dict(os.environ, env, clear=True), patch.object(resolver, "api_json", side_effect=api), patch.object(resolver, "api_bytes", return_value=archive), patch("sys.stdout", new_callable=io.StringIO), patch("sys.stderr", new_callable=io.StringIO):
                    result = resolver.main()
                contents = json.loads(output.read_text()) if output.exists() else None
            finally:
                os.chdir(previous)
        return result, contents, calls

    def test_real_entrypoint_accepts_newer_verifier_without_redeploy(self):
        result, context, calls = self.exercise_main()
        self.assertEqual(result, 0)
        self.assertEqual(context["controllerSha"], CONTROLLER)
        self.assertEqual(context["verificationCodeSha"], VERIFIER)
        self.assertTrue(any("/attempts/1/jobs?" in path for path in calls))

    def test_real_entrypoint_rejects_unrelated_controller_history(self):
        result, context, _ = self.exercise_main(comparison="diverged")
        self.assertEqual(result, 1)
        self.assertIsNone(context)

    def test_real_entrypoint_removes_stale_context_on_run_attempt_drift(self):
        result, context, _ = self.exercise_main(drift=True)
        self.assertEqual(result, 1)
        self.assertIsNone(context)


if __name__ == "__main__":
    unittest.main(verbosity=2)
