"""Reuse the full reviewed recovery suite for PR1140's exact pinned identity."""
from pathlib import Path
import importlib.util
import unittest

ROOT = Path(__file__).resolve().parents[1]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


legacy = load('pr1139_recovery_suite', 'tests/test-pr1139-uat-recovery.py')
recovery = load('pr1140_recovery', 'scripts/release-test/recover-pr1140-uat-orphan.py')
scope = load('pr1140_scope', 'tests/pr1140-uat-recovery-scope.py')
# Only this isolated test process is rebound. No source or live state changes.
legacy.recovery = recovery


class RecoveryTests(legacy.RecoveryTests):
    def test_supervisor_has_only_exact_addition(self):
        scope.verify_insertion(scope.original(scope.SUPERVISOR),
            (ROOT / scope.SUPERVISOR).read_text(), scope.SUPERVISOR_ANCHOR, scope.SUPERVISOR_ADDITION)

    def test_no_alternate_deployment_or_force_operation(self):
        text = (ROOT / 'scripts/release-test/recover-pr1140-uat-orphan.py').read_text()
        for forbidden in ('"/force-cancel"', '"/dispatches"', '"DELETE"', '"approve"'):
            self.assertNotIn(forbidden, text)

    def test_exact_scope_and_unchanged_algorithm(self):
        scope.verify_sources()
        scope.verify_paths(scope.EXPECTED)
        for paths in (scope.EXPECTED - {scope.SUPERVISOR},
                      scope.EXPECTED | {'.github/workflows/projectpulse-deploy-production.yml'}):
            with self.assertRaises(AssertionError):
                scope.verify_paths(paths)

    def test_extra_or_weakened_supervisor_source_is_rejected(self):
        before = scope.original(scope.SUPERVISOR)
        after = before.replace(scope.SUPERVISOR_ANCHOR,
                               scope.SUPERVISOR_ADDITION + scope.SUPERVISOR_ANCHOR)
        changed = [before, after + '\n# unreviewed\n',
                   after.replace(scope.SUPERVISOR_ADDITION, scope.SUPERVISOR_ADDITION * 2),
                   after.replace('35739149661', '35739149662'),
                   after.replace("|| fail 'PR 1140", "|| true 'PR 1140")]
        for value in changed:
            with self.assertRaises(AssertionError):
                scope.verify_insertion(before, value, scope.SUPERVISOR_ANCHOR, scope.SUPERVISOR_ADDITION)

    def test_pinned_identity_is_the_observed_pr1140_request(self):
        self.assertEqual(recovery.RUN_ID, 35739149661)
        self.assertEqual(recovery.OLD_SHA, '4e8871cf9a7c4643483d178feecff67ec6d78d12')
        self.assertEqual(recovery.CREATED, '2026-09-22T14:16:21Z')
        self.assertEqual(recovery.WORKFLOW_ID, 315562561)


if __name__ == '__main__':
    unittest.main()
