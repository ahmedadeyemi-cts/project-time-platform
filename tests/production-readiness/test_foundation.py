import copy
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('foundation', ROOT / 'scripts/production-readiness/check_foundation.py')
foundation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(foundation)


class FoundationTests(unittest.TestCase):
    def environment(self):
        config = json.loads((ROOT / 'docs/production-readiness/foundation/environment.example.json').read_text())
        for name in foundation.RESOURCES:
            config['resources'][name] = {'test': f'platform/test/{name}', 'production': f'platform/prod/{name}'}
        return config

    def test_committed_catalog_tracks_exact_current_sql(self):
        catalog = json.loads((ROOT / 'docs/production-readiness/foundation/initialization-review.json').read_text())
        self.assertEqual(foundation.catalog_issues(foundation.inventory(ROOT), catalog), [])

    def test_new_changed_removed_and_duplicate_scripts_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            sql = root / 'database/migrations/a.sql'
            sql.parent.mkdir(parents=True)
            sql.write_text('CREATE TABLE example (id int);')
            initial = foundation.inventory(root)
            catalog = {'schema_version': 1, 'scripts': [{**initial[0], 'decision': 'candidate_schema_or_reference', 'rationale': 'Fixture review'}]}
            self.assertEqual(foundation.catalog_issues(initial, catalog), [])
            sql.write_text('INSERT INTO example VALUES (1);')
            self.assertTrue(any('content changed' in issue for issue in foundation.catalog_issues(foundation.inventory(root), catalog)))
            (sql.parent / 'b.sql').write_text('SELECT 1;')
            self.assertTrue(any('new script' in issue for issue in foundation.catalog_issues(foundation.inventory(root), catalog)))
            sql.unlink()
            self.assertTrue(any('removed' in issue for issue in foundation.catalog_issues(foundation.inventory(root), catalog)))
            catalog['scripts'].append(copy.deepcopy(catalog['scripts'][0]))
            self.assertTrue(any('duplicate' in issue for issue in foundation.catalog_issues(initial, catalog)))

    def test_unflagged_sql_still_requires_review(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / 'database/migrations/plain.sql'
            path.parent.mkdir(parents=True)
            path.write_text('CREATE TABLE fresh (id int);')
            rows = foundation.inventory(root)
            self.assertEqual(rows[0]['review_default'], 'review_required')
            result = foundation.report(root, {'schema_version': 1, 'scripts': [{**rows[0], 'decision': 'review_required'}]}, self.environment())
            self.assertFalse(result['foundation_review_complete'])
            self.assertFalse(result['production_ready'])

    def test_distinct_declared_resources_pass_offline_checks(self):
        self.assertEqual(foundation.environment_issues(self.environment()), [])

    def test_missing_and_shared_resources_block(self):
        for name in foundation.RESOURCES:
            config = self.environment()
            config['resources'][name]['production'] = config['resources'][name]['test'].upper() + '/'
            self.assertTrue(any('shared' in issue for issue in foundation.environment_issues(config)))
            config['resources'][name]['production'] = ''
            self.assertTrue(foundation.environment_issues(config))

    def test_preparation_requires_explicit_disabled_flags(self):
        for flag in ('production_deployment_enabled', 'outbound_email_enabled', 'external_writes_enabled', 'background_jobs_enabled'):
            for value in (True, 'false', None):
                config = self.environment()
                config[flag] = value
                self.assertTrue(foundation.environment_issues(config))

    def test_credentials_are_rejected_without_echo(self):
        config = self.environment()
        secret = 'postgres://someone:VERY_SECRET@host/db'
        config['resources']['database']['production'] = secret
        issues = foundation.environment_issues(config)
        self.assertTrue(issues)
        self.assertNotIn('VERY_SECRET', json.dumps(issues))

    def test_empty_example_remains_blocked(self):
        base = ROOT / 'docs/production-readiness/foundation'
        result = foundation.report(ROOT, json.loads((base / 'initialization-review.json').read_text()), json.loads((base / 'environment.example.json').read_text()))
        self.assertFalse(result['foundation_review_complete'])
        self.assertFalse(result['sql_execution_supported'])
        self.assertFalse(result['live_environment_verified'])
        self.assertTrue(result['pending_script_reviews'])

    def test_candidates_never_claim_production_readiness(self):
        with tempfile.TemporaryDirectory() as directory:
            result = foundation.report(Path(directory), {'schema_version': 1, 'scripts': []}, self.environment())
            self.assertFalse(result['production_ready'])
            self.assertFalse(result['foundation_review_complete'])
            self.assertTrue(result['remaining_acceptance'])

    def test_bad_catalog_schema_and_decisions_fail(self):
        self.assertTrue(foundation.catalog_issues([], []))
        self.assertTrue(foundation.catalog_issues([], {'schema_version': 1, 'scripts': [{'path': 'x', 'decision': 'approved'}]}))


if __name__ == '__main__':
    unittest.main()
