"""Validate parsed release workflows and exact noncandidate behavior preservation."""
import copy
import os
from pathlib import Path
import subprocess
import unittest
import yaml
ROOT=Path(__file__).resolve().parents[1]
CONTROLLER='.github/workflows/projectpulse-deploy-test.yml'
NEW_NAMES={
 'Check out trusted main control plane for the PSA candidate',
 'Admit the exact reviewed PSA candidate using trusted main controls',
 'Install isolated live-browser acceptance dependencies',
 'Verify PSA candidate health and the live SOW-to-WBS lifecycle'
}

def load(text):return yaml.load(text,Loader=yaml.BaseLoader)

def verify(doc):
    assert doc['permissions']=={'id-token':'write','contents':'read','actions':'read'}
    assert doc['concurrency']=={'group':'projectpulse-deploy-test','queue':'max','cancel-in-progress':'false'}
    assert list(doc['jobs'])==['deploy']
    job=doc['jobs']['deploy'];assert job['environment']=='test'
    steps=job['steps']; byid={s['id']:s for s in steps if 'id' in s}
    assert 'working-directory' not in next(s for s in steps if s.get('name')=='Check out trusted main control plane for the PSA candidate')
    control=next(s for s in steps if s.get('name')=='Check out trusted main control plane for the PSA candidate')
    assert control['with']['ref']=='${{ github.sha }}' and control['with']['path']=='control'
    assert control['with']['persist-credentials']=='false'
    admission=byid['psa_admission'];assert admission['working-directory']=='control'
    assert admission['run']=='node scripts/release-test/flowhive-psa-admission.mjs'
    assert steps.index(admission)<steps.index(byid['release'])
    assert steps.index(byid['migration'])<steps.index(byid['deploy_api'])<steps.index(byid['deploy_web'])<steps.index(byid['psa_live_uat'])
    assert 'build-and-run-flowhive-psa-migrations.sh' in byid['migration']['run']
    assert byid['psa_live_uat']['working-directory']=='control'
    assert byid['psa_live_uat']['timeout-minutes']=='20'
    for key in ['uat','module025_fixture','module025_uat']:
        assert byid[key]['if']=="steps.psa_admission.outputs.authorized != 'true'"
    rb=next(s for s in steps if s.get('name')=='Restore exact prior Test images after application failure')
    assert "steps.psa_live_uat.outputs.deployment_health_verified != 'true'" in rb['if']
    assert "steps.uat.outputs.deployment_health_verified != 'true'" in rb['if']
    for step in steps:
        if 'if' in step and step.get('name') not in NEW_NAMES and 'recover_private_runtime' in step['if']:
            assert "github.ref == 'refs/heads/main'" in step['if']
    # Every script is syntax checked, including old controller shell blocks.
    for step in steps:
        if step.get('shell')=='bash' and 'run' in step:
            import re
            body=re.sub(r'\$\{\{.*?\}\}', 'fixture_value',step['run'])
            subprocess.run(['bash','-n'],input=body,text=True,check=True,capture_output=True)

class WorkflowContract(unittest.TestCase):
    def setUp(self):self.doc=load((ROOT/CONTROLLER).read_text())
    def test_parsed_workflow(self):verify(self.doc)
    def test_negative_production_concurrency_and_late_admission(self):
        for mutate in [lambda x:x['jobs']['deploy'].update(environment='production'),
          lambda x:x['concurrency'].update({'cancel-in-progress':'true'}),
          lambda x:x['permissions'].update({'contents':'write'}),
          lambda x:x['jobs']['deploy']['steps'].reverse()]:
            d=copy.deepcopy(self.doc);mutate(d)
            with self.assertRaises((AssertionError,StopIteration)):verify(d)
    def test_admission_cannot_mutate_cloud_or_publish_code(self):
        doc=load((ROOT/'.github/workflows/flowhive-psa-protected-test-admission.yml').read_text())
        assert list(doc['on'])==['issue_comment']
        assert doc['permissions']=={'actions':'write','contents':'read','issues':'write'}
        assert doc['concurrency']['group']=='module025-protected-uat-control'
        assert doc['concurrency']['cancel-in-progress']=='false'
        job=doc['jobs']['admit'];assert 'environment' not in job
        assert "github.actor == 'ahmedadeyemi-cts'" in job['if'] and 'github.event.issue.number == 872' in job['if']
        assert all('azure/login' not in s.get('uses','') for s in job['steps'])
    def test_all_unrelated_original_steps_remain_unchanged(self):
        base=os.environ.get('CONTROL_BASE')
        if not base:self.skipTest('Exact main controller comparison runs in PR CI with CONTROL_BASE.')
        old=load(subprocess.check_output(['git','show',base+':'+CONTROLLER],cwd=ROOT,text=True))
        before=old['jobs']['deploy']['steps']; after=[s for s in self.doc['jobs']['deploy']['steps'] if s.get('name') not in NEW_NAMES]
        self.assertEqual([s.get('name') for s in before],[s.get('name') for s in after])
        allowed={'Guard exact source and validate release','Apply and verify Migrations 086, 088, and 093 through 100 inside Test private network',
                 'Restore exact prior Test images after application failure','Publish protected-Test release summary'}
        for a,b in zip(before,after):
            if a.get('id') in {'uat','module025_fixture','module025_uat'}:
                b=copy.deepcopy(b);b.pop('if',None)
            if a.get('name') not in allowed:self.assertEqual(a,b,a.get('name'))
        for name in ('on',):self.assertEqual(old[name],self.doc[name])
        self.assertEqual(old['jobs']['deploy']['env'],self.doc['jobs']['deploy']['env'])

if __name__=='__main__':unittest.main()
