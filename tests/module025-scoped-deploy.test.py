"""Protect the deployment boundary while adding scoped Module 025 acceptance."""
import re
import subprocess
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[1]
PATH = '.github/workflows/projectpulse-deploy-test.yml'
BASE = 'f1c6451bc1e0681e4338ae57fc06c1bb36b87319'
CURRENT = yaml.safe_load((ROOT / PATH).read_text())
PREVIOUS = yaml.safe_load(subprocess.check_output(['git', 'show', f'{BASE}:{PATH}'], cwd=ROOT, text=True))
JOB = CURRENT['jobs']['deploy']
STEPS = {s['name']: s for s in JOB['steps']}


class ScopedDeploymentTests(unittest.TestCase):
    def test_protection_and_infrastructure_unchanged(self):
        for key in ('permissions', 'concurrency'):
            self.assertEqual(CURRENT[key], PREVIOUS[key])
        for key in ('if', 'environment', 'runs-on', 'timeout-minutes'):
            self.assertEqual(JOB[key], PREVIOUS['jobs']['deploy'][key])
        self.assertEqual(JOB['environment'], 'test')
        self.assertEqual(len(JOB['steps']), len(PREVIOUS['jobs']['deploy']['steps']) + 1)
        environment = dict(JOB['env'])
        self.assertEqual(environment.pop('ACCEPTANCE_SCOPE'), "${{ inputs.acceptance_scope || 'full' }}")
        self.assertEqual(environment, PREVIOUS['jobs']['deploy']['env'])
        triggers = dict(CURRENT[True])
        inputs = dict(triggers['workflow_dispatch']['inputs'])
        inputs.pop('acceptance_scope')
        self.assertEqual(inputs, PREVIOUS[True]['workflow_dispatch']['inputs'])
        self.assertEqual(set(triggers), {'workflow_dispatch'})
        changed = {
            'Verify admitted controller identity before deployment mutations',
            'Install isolated live-browser acceptance dependencies',
            'Run protected-Test authenticated functional UAT',
            'Restore exact prior Test images after application failure',
        }
        # Summary name is stable but is intentionally extended with scoped results.
        previous_steps = PREVIOUS['jobs']['deploy']['steps']
        changed.add(previous_steps[-1]['name'])
        for step in previous_steps:
            if step['name'] not in changed:
                self.assertEqual(STEPS[step['name']], step, step['name'])

    def test_scoped_run_requires_main_and_supported_input(self):
        inputs = CURRENT[True]['workflow_dispatch']['inputs']
        self.assertEqual(inputs['acceptance_scope']['default'], 'full')
        self.assertEqual(inputs['acceptance_scope']['options'], ['full', 'sow_role'])
        guard = STEPS['Verify admitted controller identity before deployment mutations']['run']
        self.assertIn('[[ "$RELEASE_BRANCH_INPUT" == main ]] || fail', guard)
        self.assertIn("*) fail 'Unsupported deployment acceptance scope.'", guard)
        self.assertEqual(STEPS['Guard exact source and validate release'],
                         next(s for s in PREVIOUS['jobs']['deploy']['steps'] if s['name'] == 'Guard exact source and validate release'))

    def test_only_normal_sow_role_acceptance_runs(self):
        scoped = STEPS['Verify Module 025 scoped deployment identity and lifecycle']
        self.assertEqual(scoped['if'], "inputs.acceptance_scope == 'sow_role'")
        self.assertNotIn('continue-on-error', scoped)
        full = STEPS['Run protected-Test authenticated functional UAT']
        self.assertIn("inputs.acceptance_scope != 'sow_role'", full['if'])
        self.assertEqual(full['run'], next(s for s in PREVIOUS['jobs']['deploy']['steps'] if s['name'] == full['name'])['run'])
        body = scoped['run']
        ordered = ['jq -e --arg image', 'deployment_health_verified=true',
                   'verify-flowhive-installed-identity.py', 'run-module025-installed-sa-uat.py',
                   'run-flowhive-my-role-browser.py', 'MODULE025_SCOPED_DEPLOYMENT_ACCEPTANCE=PASSED']
        offsets = [body.index(token) for token in ordered]
        self.assertEqual(offsets, sorted(offsets))
        self.assertNotIn('run-flowhive-psa-live-uat.py', body)
        self.assertNotIn('PROTECTED_TEST_UAT_ENABLED=true', body)
        self.assertIn('.status == "passed"', body)
        self.assertIn('.tags.environment == "test"', body)
        self.assertIn('PROJECTPULSE_SOURCE_COMMIT', body)

    def test_failure_does_not_claim_success_or_remove_rollback(self):
        rollback = STEPS['Restore exact prior Test images after application failure']
        old = next(s for s in PREVIOUS['jobs']['deploy']['steps'] if s['name'] == rollback['name'])
        self.assertEqual(rollback['run'], old['run'])
        self.assertEqual(rollback['if'], old['if'].replace(' }}', " && steps.sow_role_uat.outputs.deployment_health_verified != 'true' }}"))
        scoped = STEPS['Verify Module 025 scoped deployment identity and lifecycle']
        self.assertTrue(scoped['run'].startswith('set -Eeuo pipefail'))
        self.assertIn("steps.sow_role_uat.outcome", JOB['steps'][-1]['run'])

    def test_scoped_shell_parses(self):
        for name in ('Verify Module 025 scoped deployment identity and lifecycle',
                     'Verify admitted controller identity before deployment mutations'):
            script = re.sub(r'\$\{\{.*?\}\}', 'test-value', STEPS[name]['run'])
            subprocess.run(['bash', '-n'], input=script, text=True, check=True)


if __name__ == '__main__':
    unittest.main()
