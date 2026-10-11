import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import yaml

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('input_boundaries',ROOT/'scripts/security/validate-workflow-input-boundaries.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)

class InputBoundaries(unittest.TestCase):
    def test_all_repository_workflows_pass(self):
        self.assertGreaterEqual(module.validate(ROOT),202)

    def test_reintroduction_rejects_strings_choices_booleans_brackets_and_whole_events(self):
        expressions=['inputs.release_commit','inputs.choice','inputs.boolean',"inputs['release_commit']",'inputs["choice"]',
            'INPUTS.CHOICE','github.event.inputs.commit',"github['event']['inputs']['commit']",
            "github.event[format('{0}{1}', 'in', 'puts')]",'toJSON(github.event)',"toJSON(github['event'])"]
        for expression in expressions:
            with self.subTest(expression=expression),tempfile.TemporaryDirectory() as directory:
                workflow={'jobs':{'build':{'steps':[{'run':"echo '${{ "+expression+" }}'"}]}}}
                path=Path(directory)/'.github/workflows';path.mkdir(parents=True)
                (path/'test.yml').write_text(yaml.safe_dump(workflow))
                with self.assertRaises(ValueError):module.validate(directory)

    def test_environment_values_and_trusted_source_identity_are_permitted(self):
        workflow={'jobs':{'build':{'steps':[{'env':{'INPUT':'${{ inputs.choice }}'},'run':'echo "$INPUT"; echo "${{ github.sha }}"'}]}}}
        self.assertEqual(module.violations(workflow),[])

    def guards(self):
        data=yaml.safe_load((ROOT/'.github/workflows/projectpulse-deploy-test.yml').read_text())
        names={'Validate stale SOW maintenance request','Verify admitted controller identity before deployment mutations'}
        return [s for s in data['jobs']['deploy']['steps'] if s.get('name') in names]

    def environment(self):
        return dict(os.environ,GITHUB_REF='refs/heads/main',GITHUB_SHA='a'*40,GITHUB_EVENT_NAME='workflow_dispatch',
            QUALIFICATION_PROVIDER='none',RECOVER_PRIVATE_RUNTIME='false',ACCEPTANCE_SCOPE='full',
            TARGET_RELEASE_BRANCH='main',RELEASE_BRANCH_INPUT='main',EXPECTED_CONTROLLER_SHA='',
            STALE_SOW_MODE='dry-run',STALE_SOW_CONFIRMATION='')

    def test_actual_guards_accept_existing_valid_selections(self):
        for step in self.guards():
            with tempfile.TemporaryDirectory() as directory:
                result=subprocess.run(['bash','-c',step['run']],cwd=directory,env=self.environment(),capture_output=True,text=True)
                self.assertEqual(result.returncode,0,result.stderr)

    def test_actual_guards_refuse_hostile_values_without_execution(self):
        cases=0
        for step in self.guards():
            keys=['QUALIFICATION_PROVIDER']
            if step['name'].startswith('Verify admitted'):keys.append('RECOVER_PRIVATE_RUNTIME')
            for key in keys:
                with tempfile.TemporaryDirectory() as directory:
                    marker=Path(directory)/'executed'
                    payloads=['$(touch '+str(marker)+')','`touch '+str(marker)+'`',"'; touch "+str(marker)+"; '",
                              'none\ntouch '+str(marker)]
                    for payload in payloads:
                        env=self.environment();env[key]=payload
                        result=subprocess.run(['bash','-c',step['run']],cwd=directory,env=env,capture_output=True,text=True)
                        self.assertNotEqual(result.returncode,0)
                        self.assertFalse(marker.exists())
                        cases+=1
        self.assertEqual(cases,12)

if __name__=='__main__':unittest.main()
