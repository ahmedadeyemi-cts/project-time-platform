"""Negative authorization checks; no network, credentials or generation."""
import importlib.util
from pathlib import Path
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('authority', ROOT / 'scripts/release-test/authorize-module025-register-check.py')
authority = importlib.util.module_from_spec(spec)
spec.loader.exec_module(authority)


class RegisterControlTests(unittest.TestCase):
    def test_exact_owner_current_main_only(self):
        sha = 'a' * 40
        args = [f'VERIFY MODULE025 REGISTER SHA {sha} SOURCE RUN 12345',
                'ahmedadeyemi-cts', 'issue_comment', 'refs/heads/main', sha, sha]
        self.assertEqual(authority.parse_request(*args), '12345')
        for index, value in [(0, args[0] + '\n'), (0, args[0] + '; whoami'), (1, 'other'),
                             (2, 'pull_request'), (3, 'refs/heads/feature'), (4, 'b' * 40), (5, 'b' * 40)]:
            changed = args.copy()
            changed[index] = value
            with self.assertRaises(RuntimeError):
                authority.parse_request(*changed)

    def test_source_completed_with_retained_sa_record(self):
        run = {'id': 12345, 'workflow_id': 315562561,
               'path': '.github/workflows/projectpulse-deploy-test.yml', 'status': 'completed',
               'head_branch': 'main', 'head_sha': 'a' * 40, 'conclusion': 'failure'}
        jobs = {'total_count': 1, 'jobs': [{'steps': [{'name': name, 'conclusion': 'success'} for name in (
            'Deploy immutable Test API image', 'Deploy immutable Test web image',
            'Seal server-confirmed deployment identity',
            'Verify Module 025 scoped deployment identity and lifecycle')]}]}
        self.assertEqual(authority.verify_source(run, jobs, '12345'), 'a' * 40)
        for key, value in [('id', 12346), ('status', 'in_progress'), ('workflow_id', 1), ('head_branch', 'other')]:
            with self.assertRaises(RuntimeError):
                authority.verify_source(run | {key: value}, jobs, '12345')
        for step in jobs['jobs'][0]['steps']:
            step['conclusion'] = 'skipped'
            with self.assertRaises(RuntimeError):
                authority.verify_source(run, jobs, '12345')
            step['conclusion'] = 'success'

    def test_read_only_workflow_boundary(self):
        source = (ROOT / '.github/workflows/module025-retained-register-check.yml').read_text()
        workflow = yaml.load(source, Loader=yaml.BaseLoader)
        self.assertEqual(workflow['on'], {'issue_comment': {'types': ['created']}})
        self.assertEqual(workflow['permissions'], {'contents': 'read', 'actions': 'read'})
        self.assertEqual(workflow['concurrency'], {'group': 'module025-protected-uat-control', 'cancel-in-progress': 'false'})
        job = workflow['jobs']['register']
        self.assertEqual(job['environment'], {'name': 'test'})
        self.assertNotIn('permissions', job)
        self.assertIn("github.actor == 'ahmedadeyemi-cts'", job['if'])
        self.assertEqual(job['steps'][0]['with']['ref'], '${{ github.sha }}')
        self.assertEqual(job['steps'][0]['with']['persist-credentials'], 'false')
        self.assertNotRegex(source, r'azure/login|id-token|workflow_dispatch|/generate|docker|containerapp')
        self.assertIn('MODULE025_REGISTER_MODE: retained-sa', source)


if __name__ == '__main__':
    unittest.main()
