"""Execute release failure paths locally without Azure, a model, or live Test."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[1]
WORKFLOW = ROOT / '.github/workflows/projectpulse-deploy-test.yml'
MIGRATION = ROOT / 'scripts/release-test/build-and-run-module025-retention-migration-106.sh'


class CompleteAcceptanceTests(unittest.TestCase):
    def test_my_role_runs_after_sow_failure_and_aggregate_stays_failed(self):
        workflow = yaml.safe_load(WORKFLOW.read_text())
        step = next(s for s in workflow['jobs']['deploy']['steps'] if s.get('id') == 'sow_role_uat')
        # Run the actual shell tail, including evidence validation and aggregate.
        script = 'set -Eeuo pipefail\n' + step['run'][step['run'].index('sow_result=0'):]
        for sow, role in [(0, 0), (1, 0), (0, 1), (1, 1), (130, 0), (143, 0)]:
            with self.subTest(sow=sow, role=role), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                executable = root / 'flowhive-psa-browser/bin/python'
                executable.parent.mkdir(parents=True)
                executable.write_text('''#!/usr/bin/env python3
import json, os, pathlib, sys
root=pathlib.Path(os.environ['EVIDENCE_DIR'])
sow='run-module025-installed-sa-uat.py' in sys.argv[1]
name='sow' if sow else 'role'
with (root/'calls').open('a') as f: f.write(name+'\\n')
code=int(os.environ['SOW_RESULT' if sow else 'ROLE_RESULT'])
path=root/('module025-installed-sa-uat.json' if sow else 'flowhive-my-role-browser.json')
path.write_text(json.dumps({'status':'passed' if code==0 else 'failed'}))
sys.exit(code)
''')
                executable.chmod(0o700)
                result = subprocess.run(['bash', '-c', script], env={**os.environ,
                    'RUNNER_TEMP': directory, 'EVIDENCE_DIR': directory,
                    'SOW_RESULT': str(sow), 'ROLE_RESULT': str(role)}, capture_output=True, text=True)
                self.assertEqual(result.returncode == 0, sow == role == 0, result.stderr)
                self.assertEqual((root/'calls').read_text().splitlines(), ['sow'] if sow in (130, 143) else ['sow', 'role'])
                if sow not in (130, 143):
                    evidence = json.loads((root/'module025-scoped-results.json').read_text())
                    self.assertEqual((evidence['sowLifecycleExit'], evidence['myRoleExit']), (sow, role))
                    self.assertFalse(evidence['fullRequestedScopePassed'])
                if sow or role:
                    self.assertNotIn('ACCEPTANCE=PASSED', result.stdout)

    def test_migration_builder_binds_reviewed_source_digest_mode_and_test_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            bin_dir = root / 'bin'
            bin_dir.mkdir()
            migration = root / 'database/migrations/106_module025_sow_sell_register.sql'
            migration.parent.mkdir(parents=True)
            migration.write_bytes((ROOT / migration.relative_to(root)).read_bytes())
            launcher = root / 'scripts/release-test/run-migration-job.sh'
            launcher.parent.mkdir(parents=True)
            launcher.write_text('''#!/usr/bin/env bash
set -Eeuo pipefail
[[ "$MAIN_RELEASE_MIGRATION_SCOPE" == module025-retention-106-test ]]
[[ "$MAIN_RELEASE_MIGRATION_MODE" == apply ]]
[[ "$MAIN_RELEASE_CONTROL_SHA" == "$RELIABILITY_CONTROL_SHA" ]]
[[ "$MAIN_RELEASE_EXPECTED_RELEASE_COMMIT" == "$RELIABILITY_RELEASE_COMMIT" ]]
[[ "$MAIN_RELEASE_MIGRATION_IMAGE" == testacr.azurecr.io/module025-retention-migrator@sha256:* ]]
printf 'launch\\n' >> "$TEST_CALLS"
''')
            (bin_dir / 'git').write_text('#!/bin/sh\nprintf "%s\\n" "$RELIABILITY_RELEASE_COMMIT"\n')
            (bin_dir / 'az').write_text('''#!/usr/bin/env python3
import json, os, pathlib, shutil, sys
args=sys.argv[1:]
with open(os.environ['TEST_CALLS'],'a') as f: f.write('az '+ ' '.join(args[:3])+'\\n')
if args[:2]==['containerapp','show']: print(json.dumps({'tags':{'environment':os.environ.get('TEST_TAG','test')}}))
elif args[:2]==['acr','build']:
  shutil.copytree(args[-1], pathlib.Path(os.environ['RUNNER_TEMP'])/'captured', dirs_exist_ok=True)
elif args[:3]==['acr','repository','show']: print('sha256:'+'a'*64)
else: sys.exit(2)
''')
            for file in bin_dir.iterdir(): file.chmod(0o700)
            calls = root/'calls'
            environment = {**os.environ, 'PATH':str(bin_dir)+':'+os.environ['PATH'],
                'PROJECTPULSE_RELEASE_ROOT':directory, 'RELIABILITY_RELEASE_COMMIT':'b'*40,
                'RELIABILITY_CONTROL_SHA':'c'*40, 'GITHUB_EVENT_NAME':'workflow_dispatch',
                'GITHUB_REF':'refs/heads/main', 'TARGET_RELEASE_BRANCH':'main', 'ACCEPTANCE_SCOPE':'sow_role',
                'AZURE_ACR_NAME':'testacr', 'AZURE_RESOURCE_GROUP':'test', 'AZURE_API_APP':'test-api',
                'GITHUB_RUN_ID':'12345', 'GITHUB_RUN_ATTEMPT':'1', 'RUNNER_TEMP':directory,
                'EVIDENCE_DIR':directory, 'TEST_CALLS':str(calls)}
            result = subprocess.run(['bash', str(MIGRATION)], env=environment, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(calls.read_text().count('launch\n'), 1)
            evidence = json.loads((root/'module025-retention-migration.json').read_text())
            self.assertEqual(evidence['sourceCommit'], 'b'*40)
            self.assertFalse(evidence['productionMutation'])
            captured = root/'captured'
            subprocess.run(['sha256sum', '--check', '--status', 'SHA256SUMS'], cwd=captured, check=True)
            entrypoint = (captured/'entrypoint.sh').read_text()
            self.assertIn('MAIN_RELEASE_MIGRATION_MODE', entrypoint)
            self.assertEqual(entrypoint.count('--file migration.sql'), 1)
            self.assertIn('module025_sell_receipt_validation', entrypoint)
            for change in [{'GITHUB_REF':'refs/heads/feature'}, {'ACCEPTANCE_SCOPE':'full'},
                           {'TARGET_RELEASE_BRANCH':'release/other'}, {'TEST_TAG':'production'}]:
                calls.write_text('')
                result = subprocess.run(['bash', str(MIGRATION)], env={**environment, **change}, capture_output=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn('acr build', calls.read_text())
                self.assertNotIn('launch', calls.read_text())

    def test_assembled_ci_includes_browser_and_retention_database_checks(self):
        ci = yaml.safe_load((ROOT/'.github/workflows/flowhive-psa-release-control-ci.yml').read_text())
        job = ci['jobs']['module025_browser']
        self.assertEqual(job['if'], "github.event_name == 'pull_request'")
        commands = '\n'.join(s.get('run','') for s in job['steps'])
        self.assertIn('tests/module025-document-actions.test.mjs', commands)
        self.assertIn('tests/test-module025-sow-sell-register-migration-106.sh', commands)
        self.assertNotIn('secrets.', json.dumps(job))


if __name__ == '__main__':
    unittest.main()
