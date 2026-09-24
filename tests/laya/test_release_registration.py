"""Offline release inheritance and exact-controller regressions; no live calls."""
import importlib.util
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


registration = load('release_registration', 'tests/laya/release-registration.py')
controller = load('quarantine_controller', 'scripts/release-test/verify-module025-quarantine-controller.py')


def source(ref):
    return subprocess.check_output(['git', 'show', ref + ':' + controller.CONTROLLER], cwd=ROOT)


class ReleaseRegistrationTests(unittest.TestCase):
    def test_only_exact_migration_controller_can_succeed_reviewed_controllers(self):
        current = source(registration.RELEASE_PATCH)
        for ref in (controller.BASE, '57c8d0264bdd828e6b3b53a8c5cb8b1b841e8f61',
                    'c15ef12d5ce1bc54c15d8b31c87a50daa94bad17', registration.REVIEW_BASE):
            old = source(ref)
            self.assertTrue(controller.permitted(old, current))
            self.assertFalse(controller.permitted(old, current + b'\n# unreviewed'))
            self.assertFalse(controller.permitted(old + b'changed', current))
            self.assertFalse(controller.permitted(old, current.replace(b'environment: test', b'environment: production')))

    def test_exact_main_guard_is_byte_identical_before_azure(self):
        marker = b'      - name: Guard exact source and validate release\n'
        def guard(data):
            start = data.index(marker)
            self.assertLess(start, data.index(b'uses: azure/login@'))
            return data[start:data.index(b'          for required in', start)]
        self.assertEqual(guard(source(registration.REVIEW_BASE)), guard(source(registration.RELEASE_PATCH)))

    def test_application_cannot_change_inherited_release_files(self):
        for path in registration.CORE | set(registration.GATE_BLOBS):
            changed = set(registration.APPLICATION_FILES) | {path}
            with patch.object(registration, 'branch', return_value=registration.APPLICATION), \
                 patch.object(registration, 'context', return_value=('b' * 40, changed)), \
                 patch.object(registration, 'git', return_value=b'unchanged SQL'):
                with self.assertRaisesRegex(AssertionError, 'Unexpected source paths'):
                    registration.check_application()

    def test_application_requires_migration_in_accepted_base(self):
        with patch.object(registration, 'branch', return_value=registration.APPLICATION), \
             patch.object(registration, 'context', return_value=('b' * 40, set(registration.APPLICATION_FILES))), \
             patch.object(registration, 'git', side_effect=[b'wrong SQL', b'expected SQL']):
            with self.assertRaisesRegex(AssertionError, 'dependency is not incorporated'):
                registration.check_application()

    def test_catalog_registration_never_asserts_review_approval(self):
        catalog = json.loads(registration.expected_core(registration.CATALOG_PATH))
        row = next(r for r in catalog['scripts'] if r['path'] == registration.CANONICAL_REVIEW_ENTRY['path'])
        self.assertEqual(row['decision'], 'review_required')
        self.assertEqual(row['review_default'], 'review_required')


if __name__ == '__main__':
    unittest.main()
