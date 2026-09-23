"""Offline boundary tests. No real GitHub, Azure, provider, or billing calls."""
from copy import deepcopy
import hashlib
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    'supersession', ROOT / 'scripts/release-test/verify-pr1151-uat-supersession.py')
r = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r)
NEW = 'b' * 40


def record():
    return dict(id=r.RUN_ID, workflow_id=r.WORKFLOW_ID, run_attempt=1,
                event='workflow_dispatch', head_sha=r.OLD_SHA, head_branch='main',
                created_at=r.CREATED, updated_at=r.CREATED, path=r.DEPLOYMENT,
                status='queued', conclusion=None)


class Api:
    def __init__(self):
        self.run = record()
        self.jobs = {'total_count': 0, 'jobs': []}
        self.pending = []
        self.main = NEW
        self.reads = []
        self.on_read = lambda _: None

    def read(self, path):
        self.reads.append(path)
        self.on_read(len(self.reads))
        if path == 'git/ref/heads/main':
            return {'object': {'sha': self.main}}
        if '/jobs?' in path:
            return deepcopy(self.jobs)
        if path.endswith('/pending_deployments'):
            return deepcopy(self.pending)
        if path == f'actions/runs/{r.RUN_ID}':
            return deepcopy(self.run)
        raise AssertionError('Unexpected API read: ' + path)


class SupersessionTests(unittest.TestCase):
    def test_exact_identity_is_verified_without_cancellation_or_deployment(self):
        api = Api()
        result = r.verify(api, context=lambda _: NEW)
        self.assertEqual(result['result'], 'verified_non_executable_orphan')
        for key in ('cancelled', 'cancellation_attempted', 'deployment_performed'):
            self.assertFalse(result[key])
        self.assertEqual(len(api.reads), 6)
        self.assertEqual(result['superseding_commit'], NEW)

    def test_changed_identity_fails(self):
        changes = dict(id=1, workflow_id=1, run_attempt=2, event='push',
                       head_sha=NEW, head_branch='production', created_at='other',
                       path='.github/workflows/projectpulse-deploy-production.yml')
        for key, value in changes.items():
            with self.subTest(key=key):
                api = Api(); api.run[key] = value
                with self.assertRaises(RuntimeError): r.verify(api, context=lambda _: NEW)

    def test_identity_type_confusion_fails(self):
        for key in ('id', 'workflow_id', 'run_attempt'):
            api = Api(); api.run[key] = str(api.run[key])
            with self.assertRaises(RuntimeError): r.verify(api, context=lambda _: NEW)

    def test_jobs_or_approval_fail(self):
        for jobs in ({'total_count': 1, 'jobs': []},
                     {'total_count': 0, 'jobs': [{'id': 1}]},
                     {'total_count': False, 'jobs': []},
                     {'total_count': 0}, {'jobs': []}):
            api = Api(); api.jobs = jobs
            with self.assertRaises(RuntimeError): r.verify(api, context=lambda _: NEW)
        api = Api(); api.pending = [{'environment': {'name': 'test'}}]
        with self.assertRaises(RuntimeError): r.verify(api, context=lambda _: NEW)

    def test_all_progress_and_other_terminal_results_fail(self):
        for state in ('in_progress', 'waiting', 'pending', 'requested', 'completed'):
            api = Api(); api.run['status'] = state
            with self.assertRaises(RuntimeError): r.verify(api, context=lambda _: NEW)
        for conclusion in ('failure', 'success', 'timed_out', None):
            api = Api(); api.run.update(status='completed', conclusion=conclusion)
            with self.assertRaises(RuntimeError): r.verify(api, context=lambda _: NEW)
        api = Api(); api.run['updated_at'] = 'changed'
        with self.assertRaises(RuntimeError): r.verify(api, context=lambda _: NEW)

    def test_confirmed_cancellation_is_only_reported_from_github_evidence(self):
        api = Api(); api.run.update(status='completed', conclusion='cancelled')
        result = r.verify(api, context=lambda _: NEW)
        self.assertEqual(result['result'], 'already_cancelled')
        self.assertTrue(result['cancelled'])
        self.assertFalse(result['cancellation_attempted'])

    def test_invalid_shapes_fail_closed(self):
        for run, jobs, pending in ((None, {}, []), ({}, None, []), ({}, {}, None)):
            with self.assertRaises(RuntimeError): r.inspect(run, jobs, pending)

    def test_new_job_or_approval_during_reads_fails(self):
        for mutation in ('jobs', 'pending', 'run'):
            api = Api()
            def change(count):
                if count == 4:
                    if mutation == 'jobs': api.jobs['total_count'] = 1
                    elif mutation == 'pending': api.pending.append({'environment': 'test'})
                    else: api.run['status'] = 'in_progress'
            api.on_read = change
            with self.assertRaises(RuntimeError): r.verify(api, context=lambda _: NEW)

    def test_main_drift_at_either_recheck_fails(self):
        for values in ([NEW, 'c' * 40], [NEW, NEW, 'c' * 40]):
            sequence = iter(values)
            with self.assertRaises(RuntimeError): r.verify(Api(), context=lambda _: next(sequence))

    def environment(self):
        return dict(GITHUB_REPOSITORY=r.REPOSITORY, GITHUB_REF='refs/heads/main',
                    GITHUB_EVENT_NAME='push', GITHUB_SHA=NEW,
                    GITHUB_WORKFLOW_REF=f'{r.REPOSITORY}/{r.SUPERVISOR}@refs/heads/main')

    def fake_git(self, *args):
        if args == ('rev-parse', 'HEAD'): return NEW
        if args[0] == 'rev-parse': return r.DEPLOYMENT_BLOB
        return ''

    def test_context_and_provenance_are_mandatory(self):
        with patch.dict(os.environ, self.environment(), clear=True), patch.object(r, 'git', side_effect=self.fake_git):
            self.assertEqual(r.verify_context(Api()), NEW)
            for key, value in dict(GITHUB_REPOSITORY='other/repo',
                                  GITHUB_REF='refs/heads/feature',
                                  GITHUB_EVENT_NAME='pull_request',
                                  GITHUB_WORKFLOW_REF='another/workflow',
                                  GITHUB_SHA=r.OLD_SHA).items():
                with patch.dict(os.environ, {key: value}):
                    with self.assertRaises(RuntimeError): r.verify_context(Api())
            api = Api(); api.main = 'c' * 40
            with self.assertRaises(RuntimeError): r.verify_context(api)

    def test_changed_deploy_controller_or_unrelated_ancestry_fails(self):
        with patch.dict(os.environ, self.environment(), clear=True):
            def changed(*args):
                return NEW if args == ('rev-parse', 'HEAD') else 'wrong'
            with patch.object(r, 'git', side_effect=changed):
                with self.assertRaises(RuntimeError): r.verify_context(Api())
            def no_ancestor(*args):
                if args[0] == 'merge-base': raise RuntimeError('not ancestor')
                return self.fake_git(*args)
            with patch.object(r, 'git', side_effect=no_ancestor):
                with self.assertRaises(RuntimeError): r.verify_context(Api())

    def test_api_transport_is_get_only_and_errors_stop(self):
        result = subprocess.CompletedProcess([], 0, '{}', '')
        with patch.object(r.subprocess, 'run', return_value=result) as call:
            self.assertEqual(r.GitHub().read(f'actions/runs/{r.RUN_ID}'), {})
            args = call.call_args.args[0]
            self.assertEqual(args[args.index('--method') + 1], 'GET')
            self.assertEqual(call.call_args.kwargs['timeout'], 30)
        for output in ('', 'not json'):
            result = subprocess.CompletedProcess([], 0, output, '')
            with patch.object(r.subprocess, 'run', return_value=result):
                with self.assertRaises(ValueError): r.GitHub().read('anything')
        result = subprocess.CompletedProcess([], 1, '', '403')
        with patch.object(r.subprocess, 'run', return_value=result):
            with self.assertRaises(RuntimeError): r.GitHub().read('anything')

    def test_actual_old_deployment_guard_rejects_superseded_main_before_azure(self):
        data = (ROOT / r.DEPLOYMENT).read_bytes()
        self.assertEqual(hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest(), r.DEPLOYMENT_BLOB)
        source = data.decode()
        marker = '      - name: Guard exact source and validate release\n'
        start = source.index(marker)
        self.assertLess(start, source.index('uses: azure/login@'))
        body = source[start:].split('        run: |\n', 1)[1].split('          for required in', 1)[0]
        shell = '\n'.join(line[10:] if line.startswith('          ') else line for line in body.splitlines())
        shell += '\nprintf "AFTER_EXACT_MAIN_GUARD\\n"\n'
        with tempfile.TemporaryDirectory() as directory:
            executable = Path(directory) / 'git'
            executable.write_text('#!/bin/sh\ncase "$*" in\n"rev-parse HEAD") printf "%s\\n" "$TARGET_RELEASE_COMMIT" ;;\n"rev-parse origin/main") printf "%s\\n" "$MOCK_MAIN" ;;\n"fetch --no-tags origin main") exit 0 ;;\n*) exit 97 ;;\nesac\n')
            executable.chmod(0o700)
            env = dict(PATH=directory + ':' + os.defpath, GITHUB_EVENT_NAME='workflow_dispatch',
                       TARGET_RELEASE_COMMIT=r.OLD_SHA, TARGET_RELEASE_BRANCH='main', MOCK_MAIN=NEW)
            result = subprocess.run(['bash', '-c', shell], env=env, capture_output=True, text=True, timeout=10)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('not the current main branch head', result.stderr)
            self.assertNotIn('AFTER_EXACT_MAIN_GUARD', result.stdout)
            env['TARGET_RELEASE_COMMIT'] = NEW
            result = subprocess.run(['bash', '-c', shell], env=env, capture_output=True, text=True, timeout=10)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn('AFTER_EXACT_MAIN_GUARD', result.stdout)

    def test_new_scope_allows_only_literal_successor_insertion(self):
        spec = importlib.util.spec_from_file_location('legacy_scope', ROOT / 'tests/pr1140-uat-recovery-scope.py')
        legacy = importlib.util.module_from_spec(spec); spec.loader.exec_module(legacy)
        spec = importlib.util.spec_from_file_location('new_scope', ROOT / 'tests/pr1151-uat-supersession-scope.py')
        scope = importlib.util.module_from_spec(spec); spec.loader.exec_module(scope)
        migration = '            # MIGRATION_RETRY_RECOVERY_BEGIN\n            if [[ "$run_id" == \'35761573008\' ]]; then\n              python3 scripts/release-test/recover-pr1140-migration-retry-orphan.py \\\n                || fail \'Migration retry orphan recovery did not meet its exact safety contract.\'\n              quarantined_runs+=("$run_id")\n              continue\n            fi\n            # MIGRATION_RETRY_RECOVERY_END\n'
        before = 'before\n' + legacy.SUPERVISOR_ANCHOR + 'after\n'
        expected = before.replace(legacy.SUPERVISOR_ANCHOR, legacy.SUPERVISOR_ADDITION + legacy.SUPERVISOR_ANCHOR)
        expected = expected.replace('            # PR1140_UAT_RECOVERY_END\n', migration + '            # PR1140_UAT_RECOVERY_END\n')
        old, new = scope.PATCHES[r.SUPERVISOR]
        expected = expected.replace(old, new)
        legacy.verify_insertion(before, expected, legacy.SUPERVISOR_ANCHOR, legacy.SUPERVISOR_ADDITION)
        for changed in (expected + '# unrelated\n', expected.replace('35891884845', '35891884846'),
                        expected.replace(new, new + new), expected.replace("|| fail 'PR 1151", "|| true 'PR 1151")):
            with self.assertRaises(AssertionError):
                legacy.verify_insertion(before, changed, legacy.SUPERVISOR_ANCHOR, legacy.SUPERVISOR_ADDITION)


if __name__ == '__main__':
    unittest.main()
