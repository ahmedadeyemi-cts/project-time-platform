"""Offline negative tests for the exact PR1139 Test dispatch recovery."""
from copy import deepcopy
import importlib.util
from pathlib import Path
import hashlib
import os
import re
import subprocess
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "pr1139_recovery", ROOT / "scripts/release-test/recover-pr1139-uat-orphan.py")
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)
NEW = "b" * 40


def record():
    return {
        "id": recovery.RUN_ID, "workflow_id": recovery.WORKFLOW_ID,
        "run_attempt": 1, "event": "workflow_dispatch", "head_sha": recovery.OLD_SHA,
        "head_branch": "main", "created_at": recovery.CREATED,
        "updated_at": recovery.CREATED, "path": recovery.DEPLOYMENT,
        "status": "queued", "conclusion": None,
    }


class Api:
    def __init__(self, cancel_status=409, cancelled=False):
        self.run = record()
        self.jobs = {"total_count": 0, "jobs": []}
        self.pending = []
        self.cancel_status = cancel_status
        self.cancelled = cancelled
        self.mutations = []
        self.on_cancel = None

    def read(self, path):
        if "/jobs?" in path:
            return deepcopy(self.jobs)
        if path.endswith("/pending_deployments"):
            return deepcopy(self.pending)
        if path == "git/ref/heads/main":
            return {"object": {"sha": NEW}}
        return deepcopy(self.run)

    def cancel(self):
        self.mutations.append("cancel")
        if self.cancelled:
            self.run.update(status="completed", conclusion="cancelled")
        if self.on_cancel:
            self.on_cancel()
        return self.cancel_status


class RecoveryTests(unittest.TestCase):
    def run_recovery(self, api, verify=lambda _: NEW):
        return recovery.recover(api, verify=verify, sleep=lambda _: None)

    def test_exact_zero_job_identity(self):
        self.assertEqual(recovery.inspect(record(), {"total_count": 0, "jobs": []}, []),
                         "unchanged_zero_job")

    def test_identity_changes_are_rejected_before_mutation(self):
        changes = {"id": 1, "workflow_id": 1, "run_attempt": 2, "event": "push",
                   "head_sha": NEW, "head_branch": "production", "created_at": "changed",
                   "path": ".github/workflows/projectpulse-deploy-production.yml"}
        for key, value in changes.items():
            with self.subTest(key=key):
                api = Api(); api.run[key] = value
                with self.assertRaises(RuntimeError): self.run_recovery(api)
                self.assertEqual(api.mutations, [])

    def test_active_and_pending_states_rejected(self):
        for status in ("in_progress", "waiting", "pending", "requested"):
            api = Api(); api.run["status"] = status
            with self.assertRaises(RuntimeError): self.run_recovery(api)
            self.assertEqual(api.mutations, [])

    def test_jobs_block_recovery(self):
        for jobs in ({"total_count": 1, "jobs": []},
                     {"total_count": 0, "jobs": [{"id": 2}]},
                     {"total_count": False, "jobs": []}):
            api = Api(); api.jobs = jobs
            with self.assertRaises(RuntimeError): self.run_recovery(api)
            self.assertEqual(api.mutations, [])

    def test_pending_approvals_block_recovery(self):
        api = Api(); api.pending = [{"environment": {"name": "test"}}]
        with self.assertRaises(RuntimeError): self.run_recovery(api)
        self.assertEqual(api.mutations, [])

    def test_changed_timestamp_blocks_recovery(self):
        api = Api(); api.run["updated_at"] = "2026-09-21T19:40:00Z"
        with self.assertRaises(RuntimeError): self.run_recovery(api)

    def test_regular_cancel_success(self):
        api = Api(202, cancelled=True)
        result = self.run_recovery(api)
        self.assertEqual(result["result"], "cancelled")
        self.assertEqual(api.mutations, ["cancel"])

    def test_cancel_conflict_never_claims_cancellation(self):
        api = Api(409)
        result = self.run_recovery(api)
        self.assertEqual(result["result"], "verified_non_executable_orphan")
        self.assertFalse(result["cancelled"])
        self.assertFalse(result["deployment_performed"])
        self.assertEqual(api.mutations, ["cancel"])

    def test_accepted_but_unchanged_is_not_reported_cancelled(self):
        result = self.run_recovery(Api(202))
        self.assertEqual(result["result"], "verified_non_executable_orphan")
        self.assertFalse(result["cancelled"])

    def test_already_cancelled_has_no_mutation(self):
        api = Api(); api.run.update(status="completed", conclusion="cancelled")
        self.assertEqual(self.run_recovery(api)["result"], "already_cancelled")
        self.assertEqual(api.mutations, [])

    def test_other_terminal_result_is_not_discarded(self):
        for conclusion in ("success", "failure", "timed_out", None):
            api = Api(); api.run.update(status="completed", conclusion=conclusion)
            with self.assertRaises(RuntimeError): self.run_recovery(api)

    def test_job_appearing_after_cancel_stops(self):
        api = Api(202)
        api.on_cancel = lambda: api.jobs.update(total_count=1, jobs=[{"id": 2}])
        with self.assertRaises(RuntimeError): self.run_recovery(api)

    def test_approval_appearing_after_cancel_stops(self):
        api = Api(409)
        api.on_cancel = lambda: api.pending.append({"environment": "test"})
        with self.assertRaises(RuntimeError): self.run_recovery(api)

    def test_main_changes_during_recovery_stops(self):
        sequence = iter([NEW, "c" * 40])
        with self.assertRaises(RuntimeError):
            self.run_recovery(Api(), verify=lambda _: next(sequence))

    def test_permission_and_ambiguous_http_errors_stop(self):
        for status in (401, 403, 404, 500):
            process = subprocess.CompletedProcess([], 1, f"HTTP/2.0 {status} Error\n\n{{}}", "")
            with patch.object(recovery.subprocess, "run", return_value=process):
                with self.assertRaises(RuntimeError): recovery.GitHub().cancel()

    def test_cancel_http_status_parser(self):
        for status in (202, 409):
            process = subprocess.CompletedProcess([], 0 if status == 202 else 1,
                                                  f"HTTP/2.0 {status} Response\n\n{{}}", "")
            with patch.object(recovery.subprocess, "run", return_value=process):
                self.assertEqual(recovery.GitHub().cancel(), status)

    def test_trusted_context(self):
        environment = {
            "GITHUB_REPOSITORY": recovery.REPOSITORY, "GITHUB_REF": "refs/heads/main",
            "GITHUB_EVENT_NAME": "push", "GITHUB_SHA": NEW,
            "GITHUB_WORKFLOW_REF": f"{recovery.REPOSITORY}/{recovery.SUPERVISOR}@refs/heads/main",
        }
        def git(*args):
            if args == ("rev-parse", "HEAD"): return NEW
            if args[0] == "rev-parse": return recovery.DEPLOYMENT_BLOB
            return ""
        with patch.dict(os.environ, environment, clear=True), patch.object(recovery, "git", side_effect=git):
            self.assertEqual(recovery.verify_context(Api()), NEW)
            for key, value in (("GITHUB_REF", "refs/heads/feature"),
                               ("GITHUB_EVENT_NAME", "pull_request"),
                               ("GITHUB_WORKFLOW_REF", "other"),
                               ("GITHUB_SHA", recovery.OLD_SHA)):
                with patch.dict(os.environ, {key: value}):
                    with self.assertRaises(RuntimeError): recovery.verify_context(Api())

    def test_deployment_source_change_stops(self):
        environment = {
            "GITHUB_REPOSITORY": recovery.REPOSITORY, "GITHUB_REF": "refs/heads/main",
            "GITHUB_EVENT_NAME": "push", "GITHUB_SHA": NEW,
            "GITHUB_WORKFLOW_REF": f"{recovery.REPOSITORY}/{recovery.SUPERVISOR}@refs/heads/main",
        }
        def git(*args):
            return NEW if args == ("rev-parse", "HEAD") else "changed"
        with patch.dict(os.environ, environment, clear=True), patch.object(recovery, "git", side_effect=git):
            with self.assertRaises(RuntimeError): recovery.verify_context(Api())

    def test_supervisor_has_only_exact_addition(self):
        data = subprocess.check_output(["git", "show", "b0334754cfc52e5c7e99300aa26b0a165fae947a:" + recovery.SUPERVISOR], cwd=ROOT, text=True)
        pattern = r"^            # PR1139_UAT_RECOVERY_BEGIN\n.*?^            # PR1139_UAT_RECOVERY_END\n"
        blocks = re.findall(pattern, data, re.M | re.S)
        self.assertEqual(len(blocks), 1)
        self.assertIn('if [[ "$run_id" == \'35645759101\' ]]', blocks[0])
        self.assertIn("|| fail", blocks[0])
        original = re.sub(pattern, "", data, flags=re.M | re.S).encode()
        digest = hashlib.sha1(b"blob " + str(len(original)).encode() + b"\0" + original).hexdigest()
        self.assertEqual(digest, "7bfc6749e3e263e5c16a71ec6dafb9c28bcbcd21")

    def test_no_alternate_deployment_or_force_operation(self):
        text = (ROOT / "scripts/release-test/recover-pr1139-uat-orphan.py").read_text()
        self.assertNotIn('\"/force-cancel\"', text)
        self.assertNotIn('\"/dispatches\"', text)
        self.assertNotIn('\"DELETE\"', text)
        self.assertNotIn('\"approve\"', text)


if __name__ == "__main__":
    unittest.main()
