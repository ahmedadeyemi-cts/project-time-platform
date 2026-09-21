"""Offline negative tests for the exact Protected Test queue recovery."""
from copy import deepcopy
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('recovery', ROOT/'scripts/release-test/protected-test-queue-recovery.py')
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)
BASE = recovery.BASE
OLD = '  group: projectpulse-deploy-test\n'
NEW = '  group: projectpulse-deploy-test-recovery-20260921\n'


def baseline(path):
    return subprocess.check_output(['git', '-C', str(ROOT), 'show', BASE + ':' + path])


class RecoveryTests(unittest.TestCase):
    def fixture(self, run_id=recovery.TARGET):
        sha, branch, created = recovery.KNOWN[run_id]
        return {'id': run_id, 'workflow_id': recovery.WORKFLOW, 'run_attempt': 1,
                'event': 'workflow_dispatch', 'head_sha': sha, 'head_branch': branch,
                'created_at': created, 'updated_at': created, 'status': 'queued', 'conclusion': None}

    def test_all_exact_reviewed_records(self):
        for run_id in recovery.KNOWN:
            recovery.validate_snapshot(run_id, self.fixture(run_id), {'total_count': 0, 'jobs': []}, [])

    def test_changed_identity_or_execution_is_rejected(self):
        mutations = {'id': 1, 'workflow_id': 1, 'run_attempt': 2, 'event': 'push',
                     'head_sha': '0'*40, 'head_branch': 'other', 'created_at': 'changed',
                     'updated_at': 'changed', 'status': 'in_progress', 'conclusion': 'success'}
        for key, value in mutations.items():
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                run = self.fixture(); run[key] = value
                recovery.validate_snapshot(recovery.TARGET, run, {'total_count': 0, 'jobs': []}, [])
        with self.assertRaises(RuntimeError):
            recovery.validate_snapshot(123, self.fixture(), {'total_count': 0, 'jobs': []}, [])

    def test_missing_evidence_is_rejected(self):
        for key in self.fixture():
            run = self.fixture(); del run[key]
            with self.subTest(missing=key), self.assertRaises(RuntimeError):
                recovery.validate_snapshot(recovery.TARGET, run, {'total_count': 0, 'jobs': []}, [])

    def test_jobs_and_approvals_fail_closed(self):
        for jobs in ({}, {'total_count': 0}, {'total_count': False, 'jobs': []},
                     {'total_count': 1, 'jobs': []}, {'total_count': 0, 'jobs': [{'id': 1}]}):
            with self.subTest(jobs=jobs), self.assertRaises(RuntimeError):
                recovery.validate_snapshot(recovery.TARGET, self.fixture(), jobs, [])
        for pending in (None, {}, [{'environment': {'name': 'test'}}]):
            with self.subTest(pending=pending), self.assertRaises(RuntimeError):
                recovery.validate_snapshot(recovery.TARGET, self.fixture(), {'total_count': 0, 'jobs': []}, pending)

    def test_only_concurrency_identity_changes_in_deployment(self):
        current = (ROOT/recovery.CONTROLLER).read_bytes()
        original = baseline(recovery.CONTROLLER)
        self.assertEqual(current.count(NEW.encode()), 1)
        self.assertEqual(current.replace(NEW.encode(), OLD.encode(), 1), original)
        self.assertEqual(hashlib.sha256(current).hexdigest(), recovery.CONTROLLER_SHA256)
        for old, unsafe in ((b'environment: test', b'environment: production'),
                            (b'cancel-in-progress: false', b'cancel-in-progress: true'),
                            (b"'refs/heads/main'", b"'refs/heads/unsafe'")):
            self.assertIn(old, current)
            self.assertNotEqual(hashlib.sha256(current.replace(old, unsafe, 1)).hexdigest(), recovery.CONTROLLER_SHA256)

    def test_supervisor_preserves_all_existing_guards(self):
        path = '.github/workflows/module025-protected-uat-control.yml'
        source = (ROOT/path).read_text()
        pattern = r'^[ \t]*# LAYA_UAT_RECOVERY_BEGIN ([a-z_]+)\n.*?^[ \t]*# LAYA_UAT_RECOVERY_END \1\n'
        matches = re.findall(pattern, source, re.M | re.S)
        self.assertCountEqual(matches, ['register', 'preflight', 'orphan'])
        normalized = re.sub(pattern, '', source, flags=re.M | re.S)
        selection = "python3 scripts/release-test/verify-module025-quarantine-controller.py --base '045b66ca01baa68b2f5b3f6eb9e063c23c335981'"
        self.assertEqual(normalized.count(selection), 1)
        normalized = normalized.replace(selection, "git diff --quiet '045b66ca01baa68b2f5b3f6eb9e063c23c335981' HEAD -- .github/workflows/projectpulse-deploy-test.yml", 1)
        self.assertEqual(normalized.encode(), baseline(path))
        self.assertLess(source.index('protected-test-queue-recovery.py'), source.index('case "$workflow_state"'))
        self.assertIn('startup_deadline_epoch=$(( $(date +%s) + 180 ))', source)

    def test_each_historical_controller_still_has_an_exact_proof(self):
        script = ROOT/'scripts/release-test/verify-module025-quarantine-controller.py'
        for commit in ('245b0915d895d83f1ceaed32460ad95a4a3d79be',
                       '57c8d0264bdd828e6b3b53a8c5cb8b1b841e8f61',
                       'c15ef12d5ce1bc54c15d8b31c87a50daa94bad17',
                       '045b66ca01baa68b2f5b3f6eb9e063c23c335981', BASE):
            subprocess.run(['python3', str(script), '--base', commit], cwd=ROOT, check=True)

    def test_application_provider_order_and_production_unchanged(self):
        for path in ('.github/workflows/projectpulse-deploy-production.yml',
                     'src/backend/ProjectTime.Api/Ai/ProjectPulseAiRouter.cs',
                     'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs',
                     'scripts/release-test/flowhive-psa-admission.mjs',
                     '.github/flowhive-psa-protected-test-candidate.json',
                     '.github/flowhive-psa-protected-cutover.json'):
            self.assertEqual((ROOT/path).read_bytes(), baseline(path), path)


if __name__ == '__main__': unittest.main()
