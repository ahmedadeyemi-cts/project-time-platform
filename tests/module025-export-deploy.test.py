"""Offline checks for the generation-free Protected Test acceptance path."""
import importlib.util
from pathlib import Path
import subprocess
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('exports', ROOT/'scripts/release-test/run-module025-export-uat.py')
exports = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exports)
BASE = '7183068e8b48b019d9f607c281486e0888ce49f4'
# Main already contains migrations 112-114 in the immutable image build.
# Pin that inherited step exactly; export preparation grants no runner changes.
MIGRATION_IMAGE_BASE = '373b37e9c76e430355ed2dc5f71ec6a133ead3aa'

class ExportReleaseTests(unittest.TestCase):
    def test_transport_blocks_generation_confirmation_sell_and_unrelated_records(self):
        exports.record_id = '00000000-0000-4000-8000-000000000001'
        calls = []
        exports.transport = lambda *a: calls.append(a) or (200,None,{})
        root = '/api/module025/sow-gsd/'+exports.record_id
        for method,path in [('POST',root+'/generate'),('POST',root+'/confirm'),('POST',root+'/sell'),
                            ('GET',root+'/generations/a'),('PUT',root+'2'),('POST','/api/ai/chat')]:
            with self.assertRaises(exports.sa.AcceptanceError): exports.http(path,method)
        self.assertEqual(calls,[])
        exports.http(root+'/draft-sow.docx')
        self.assertEqual(len(calls),1)

    def test_deployment_controls_and_existing_scopes_preserved(self):
        path = '.github/workflows/projectpulse-deploy-test.yml'
        old = yaml.safe_load(subprocess.check_output(['git','show',BASE+':'+path],cwd=ROOT))
        new = yaml.safe_load((ROOT/path).read_text())
        for key in ['permissions','concurrency']:
            self.assertEqual(old[key],new[key])
        old_job,new_job = old['jobs']['deploy'],new['jobs']['deploy']
        for key in ['if','environment','runs-on','timeout-minutes','env']:
            self.assertEqual(old_job[key],new_job[key])
        changed = {
            'Verify admitted controller identity before deployment mutations',
            'Install isolated live-browser acceptance dependencies',
            'Build immutable API, web, and migration images',
            'Apply and verify governed migrations through Module 025 project-name migration 109 inside Test private network',
            'Verify Module 025 scoped deployment identity and lifecycle',
            'Run protected-Test authenticated functional UAT',
            'Enable exact-run Module 025 protected-Test authorization fixture',
            'Publish protected-Test release summary',
        }
        actual = {a['name'] for a,b in zip(old_job['steps'],new_job['steps']) if a!=b}
        # Assert exact historical changed steps below; the inherited image-build
        # step is pinned separately and all other controller steps stay identical.
        self.assertEqual(len(old_job['steps']),len(new_job['steps']))
        self.assertEqual(actual,changed)
        steps = {s['name']:s for s in new_job['steps']}
        inherited = yaml.safe_load(subprocess.check_output(['git','show',MIGRATION_IMAGE_BASE+':'+path],cwd=ROOT))
        inherited_steps = {s['name']:s for s in inherited['jobs']['deploy']['steps']}
        build_step = 'Build immutable API, web, and migration images'
        self.assertEqual(steps[build_step],inherited_steps[build_step])
        scoped = steps['Verify Module 025 scoped deployment identity and lifecycle']
        self.assertIn('run-module025-export-uat.py',scoped['run'])
        self.assertIn('run-module025-installed-sa-uat.py',scoped['run'])
        self.assertIn('.generationPosts == 0',scoped['run'])
        self.assertIn("inputs.acceptance_scope != 'sow_exports'",steps['Run protected-Test authenticated functional UAT']['if'])
        self.assertIn("inputs.acceptance_scope != 'sow_exports'",steps['Enable exact-run Module 025 protected-Test authorization fixture']['if'])
        self.assertIn('sow_role|sow_exports)',steps['Verify admitted controller identity before deployment mutations']['run'])

if __name__=='__main__': unittest.main()
