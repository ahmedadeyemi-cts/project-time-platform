"""Exercise real recovery context gates against the reviewed migration125 controller."""
from pathlib import Path
import hashlib
import importlib.util
import os
import subprocess
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
BASE = 'b0334754cfc52e5c7e99300aa26b0a165fae947a'
OLD_BLOB = '634983f88d5ce3161b626010c3e20c41a80e3758'
NEW_BLOB = 'be0296f7ad5ac5839fb52ee9aac2502973e60cdb'
CURRENT = 'c' * 40
DEPLOYMENT = '.github/workflows/projectpulse-deploy-test.yml'
HELPERS = ('recover-pr1139-uat-orphan.py', 'recover-pr1140-uat-orphan.py',
           'recover-pr1140-migration-retry-orphan.py', 'verify-pr1151-uat-supersession.py')


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), ROOT / 'scripts/release-test' / name)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def blob(data):
    return hashlib.sha1(b'blob ' + str(len(data)).encode() + b'\0' + data).hexdigest()


class Api:
    def read(self, suffix):
        assert suffix == 'git/ref/heads/main', 'Context checks must not mutate GitHub'
        return {'object': {'sha': CURRENT}}


class CompatibilityTests(unittest.TestCase):
    def setUp(self):
        self.original = subprocess.check_output(['git', 'cat-file', 'blob', OLD_BLOB], cwd=ROOT)
        self.current = (ROOT / DEPLOYMENT).read_bytes()

    def check_context(self, helper, current_blob=NEW_BLOB, original_blob=OLD_BLOB,
                      wrong_main=False, dirty=False, no_ancestry=False, event='issue_comment'):
        env = {'GITHUB_REPOSITORY': helper.REPOSITORY, 'GITHUB_REF': 'refs/heads/main',
               'GITHUB_EVENT_NAME': event, 'GITHUB_SHA': CURRENT,
               'GITHUB_WORKFLOW_REF': f'{helper.REPOSITORY}/{helper.SUPERVISOR}@refs/heads/main'}
        def git(*args):
            if args == ('rev-parse', 'HEAD'): return CURRENT
            if args == ('rev-parse', f'{helper.OLD_SHA}:{DEPLOYMENT}'): return original_blob
            if args == ('rev-parse', f'{CURRENT}:{DEPLOYMENT}'): return current_blob
            if args[0] == 'merge-base':
                if no_ancestry: raise RuntimeError('Missing ancestry')
                return ''
            if args[0] == 'diff':
                if dirty: raise RuntimeError('Uncommitted control changes')
                return ''
            raise AssertionError('Unexpected Git operation: ' + repr(args))
        api = Api()
        if wrong_main: api.read = lambda _: {'object': {'sha': 'd' * 40}}
        with patch.dict(os.environ, env, clear=True), patch.object(helper, 'git', side_effect=git):
            return helper.verify_context(api)

    def test_whole_controller_has_only_two_reviewed_migration_metadata_additions(self):
        first = b'            database/migrations/109_module025_project_name.sql \\\n'
        second = b'"097_project_planning_identity_safe_admission"],'
        self.assertEqual(self.original.count(first), 1)
        self.assertEqual(self.original.count(second), 1)
        expected = self.original.replace(first, first + b'            database/migrations/125_automatic_document_admission_laya.sql \\\n', 1)
        expected = expected.replace(second, b'"097_project_planning_identity_safe_admission","125_automatic_document_admission_laya"],', 1)
        self.assertEqual(self.current, expected)
        self.assertEqual(blob(self.current), NEW_BLOB)
        self.assertEqual(blob(self.original), OLD_BLOB)

    def test_all_four_real_context_gates_accept_old_and_reviewed_current_controller(self):
        for name in HELPERS:
            h = load(name)
            for current in (OLD_BLOB, NEW_BLOB):
                with self.subTest(helper=name, current=current):
                    self.assertEqual(self.check_context(h, current), CURRENT)
                    self.assertEqual(h.MIGRATION125_DEPLOYMENT_BLOB, NEW_BLOB)

    def test_all_four_gates_reject_unknown_and_security_modified_controllers(self):
        changes = [self.current + b'\n# unreviewed\n',
                   self.current.replace(b'environment: test', b'environment: production', 1),
                   self.current.replace(b'Only the authorized', b'Only a different authorized', 1),
                   self.current.replace(b'contents: read', b'contents: write', 1)]
        for name in HELPERS:
            for content in changes:
                with self.subTest(helper=name, candidate=blob(content)):
                    self.assertNotEqual(blob(content), NEW_BLOB)
                    with self.assertRaises(RuntimeError): self.check_context(load(name), blob(content))

    def test_old_identity_main_ancestry_clean_tree_and_event_are_still_mandatory(self):
        for name in HELPERS:
            for change in ({'original_blob': NEW_BLOB}, {'wrong_main': True},
                           {'dirty': True}, {'no_ancestry': True}, {'event': 'pull_request'}):
                with self.subTest(helper=name, change=change):
                    with self.assertRaises(RuntimeError): self.check_context(load(name), **change)

    def test_the_eighth_orphan_uses_existing_complete_controller_verifier(self):
        helper = load('verify-module025-quarantine-controller.py')
        original = subprocess.check_output(['git', 'show', '045b66ca01baa68b2f5b3f6eb9e063c23c335981:' + DEPLOYMENT], cwd=ROOT)
        self.assertTrue(helper.permitted(original, self.current))
        self.assertFalse(helper.permitted(original, self.current + b'\n# changed\n'))
        supervisor = (ROOT / '.github/workflows/module025-protected-uat-control.yml').read_text()
        self.assertIn("verify-module025-quarantine-controller.py --base '045b66ca01baa68b2f5b3f6eb9e063c23c335981'", supervisor)


if __name__ == '__main__':
    unittest.main()
