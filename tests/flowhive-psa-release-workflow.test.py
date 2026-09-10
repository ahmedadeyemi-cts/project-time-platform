"""Validate parsed release workflows and exact noncandidate behavior preservation."""
import copy
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import yaml
ROOT=Path(__file__).resolve().parents[1]
CONTROLLER='.github/workflows/projectpulse-deploy-test.yml'
HISTORICAL_CONTROLLER='af5fcb463384096f668345ac7cc9bd00efef0a33'
HISTORICAL_WORKFLOW_BLOB='c372c4aa3a532f89fdcf72c62e17b9f3e84dd785'
NEW_NAMES={
 'Check out trusted main control plane for the PSA candidate',
 'Admit the exact reviewed PSA candidate using trusted main controls',
 'Install isolated live-browser acceptance dependencies',
 'Verify PSA candidate health and the live SOW-to-WBS lifecycle'
}
STABILIZATION_BRANCH='fix/flowhive-protected-cutover-20260910'

class UniqueKeyLoader(yaml.BaseLoader):
    def construct_mapping(self,node,deep=False):
        mapping={}
        for key_node,value_node in node.value:
            key=self.construct_object(key_node,deep=deep)
            if key in mapping:
                raise AssertionError(f'duplicate YAML key: {key}')
            mapping[key]=self.construct_object(value_node,deep=deep)
        return mapping

def load(text):return yaml.load(text,Loader=UniqueKeyLoader)

def git_show(revision,path):
    return subprocess.check_output(['git','show',f'{revision}:{path}'],cwd=ROOT,text=True)

def git_blob(text):
    return subprocess.check_output(['git','hash-object','--stdin'],cwd=ROOT,input=text,text=True).strip()

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
    controller_guard=next(s for s in steps if s.get('name')=='Verify admitted controller identity before deployment mutations')
    assert controller_guard['env']['EXPECTED_CONTROLLER_SHA']=='${{ inputs.admission_controller_sha }}'
    assert 'PSA admission controller identity changed before deployment' in controller_guard['run']
    assert steps.index(controller_guard)<steps.index(next(s for s in steps if s.get('name')=='Require protected Test login credential'))
    admission=byid['psa_admission'];assert admission['working-directory']=='control'
    assert admission['run']=='node scripts/release-test/flowhive-psa-admission.mjs'
    assert steps.index(admission)<steps.index(byid['release'])
    assert steps.index(byid['migration'])<steps.index(byid['deploy_api'])<steps.index(byid['deploy_web'])<steps.index(byid['psa_live_uat'])
    assert 'build-and-run-flowhive-psa-migrations.sh' in byid['migration']['run']
    release_guard=next(s for s in steps if s.get('name')=='Guard exact source and validate release')
    assert 'database/migrations/105_flowhive_reviewed_regeneration.sql' in release_guard['run']
    assert 'database/migrations/106_module025_sow_sell_register.sql' in release_guard['run']
    assert 'database/rollback/105_flowhive_reviewed_regeneration_rollback.sql' in release_guard['run']
    assert byid['psa_live_uat']['working-directory']=='control'
    assert byid['psa_live_uat']['timeout-minutes']=='20'
    assert byid['uat']['if']=="steps.psa_admission.outputs.authorized != 'true'"
    assert byid['module025_fixture']['if']=="${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.uat.outcome == 'success' }}"
    assert byid['module025_uat']['if']=="${{ !cancelled() && steps.psa_admission.outputs.authorized != 'true' && steps.module025_fixture.outcome == 'success' }}"
    for key in ['assigned_work_uat','utilization_uat']:
        assert byid[key]['if']=="${{ !cancelled() && (steps.uat.outcome == 'success' || steps.psa_live_uat.outputs.deployment_health_verified == 'true') }}"
    assert byid['assigned_work_uat']['env']['RELIABILITY_RELEASE_COMMIT']=='${{ env.TARGET_RELEASE_COMMIT }}'
    assert steps.index(byid['assigned_work_uat'])<steps.index(byid['utilization_uat'])<steps.index(byid['module025_fixture'])
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

    def test_controller_identity_guard_fences_supported_routes_without_mutation(self):
        guard=next(step for step in self.doc['jobs']['deploy']['steps']
                   if step.get('name')=='Verify admitted controller identity before deployment mutations')['run']
        def run_guard(event,branch,expected):
            with tempfile.TemporaryDirectory() as temp:
                control=Path(temp)/'control';control.mkdir()
                subprocess.run(['git','init','-q',str(control)],check=True)
                subprocess.run(['git','-C',str(control),'-c','user.name=fixture','-c','user.email=fixture@example.invalid','commit','--quiet','--allow-empty','-m','fixture'],check=True)
                actual=subprocess.check_output(['git','-C',str(control),'rev-parse','HEAD'],text=True).strip()
                env={**os.environ,'GITHUB_REF':'refs/heads/main','GITHUB_SHA':actual,
                     'GITHUB_EVENT_NAME':event,'RELEASE_BRANCH_INPUT':branch,
                     'EXPECTED_CONTROLLER_SHA':expected}
                result=subprocess.run(['bash','-euo','pipefail','-c',guard],cwd=temp,env=env,text=True,capture_output=True)
                self.assertEqual(list(Path(temp).iterdir()),[control])
                return result,actual
        success,actual=run_guard('workflow_dispatch','release/flowhive-sow-successor-20260908','')
        self.assertNotEqual(success.returncode,0)
        success,actual=run_guard('workflow_dispatch','release/flowhive-sow-successor-20260908',actual)
        self.assertEqual(success.returncode,0)
        result,_=run_guard('workflow_dispatch','main','')
        self.assertEqual(result.returncode,0)
        result,_=run_guard('workflow_dispatch','main','a'*40)
        self.assertNotEqual(result.returncode,0)
        result,_=run_guard('push','','')
        self.assertEqual(result.returncode,0)
        result,_=run_guard('workflow_dispatch','unsupported','')
        self.assertNotEqual(result.returncode,0)

    def test_historical_source_identity_and_conditions_are_real(self):
        historical_text=git_show(HISTORICAL_CONTROLLER,CONTROLLER)
        historical_blob=subprocess.check_output(['git','rev-parse',f'{HISTORICAL_CONTROLLER}:{CONTROLLER}'],cwd=ROOT,text=True).strip()
        self.assertEqual(historical_blob,HISTORICAL_WORKFLOW_BLOB)
        self.assertEqual(git_blob(historical_text),historical_blob)
        historical=load(historical_text)
        historical_job=historical['jobs']['deploy']
        self.assertEqual(' '.join(str(historical_job['if']).split()),
            "github.event_name == 'workflow_dispatch' || github.ref == 'refs/heads/main'")
        historical_steps=historical_job['steps']
        admission=next(step for step in historical_steps if step.get('name')=='Admit the exact reviewed PSA candidate using trusted main controls')
        self.assertEqual(admission['if'],"github.event_name == 'workflow_dispatch' && inputs.release_branch == 'release/flowhive-sow-successor-20260908'")
        mutation_steps=[step for step in historical_steps if 'az containerapp update' in step.get('run','') or 'az containerapp secret set' in step.get('run','')]
        self.assertTrue(mutation_steps)
        self.assertTrue(any('release_branch' not in str(step.get('if','')) for step in mutation_steps),
            'The pinned historical workflow has an alternate manual-dispatch mutation path.')

        current_text=(ROOT/CONTROLLER).read_text()
        current_blob=subprocess.check_output(['git','hash-object',CONTROLLER],cwd=ROOT,text=True).strip()
        self.assertEqual(git_blob(current_text),current_blob)
        current=load(current_text)
        current_if=' '.join(str(current['jobs']['deploy']['if']).split())
        self.assertEqual(current_if,
            "github.ref == 'refs/heads/main' && (github.event_name == 'push' || (github.event_name == 'workflow_dispatch' && (inputs.release_branch == 'main' || inputs.release_branch == 'release/flowhive-sow-successor-20260908')))")

    def test_duplicate_jobs_fixture_is_rejected(self):
        historical_text=git_show(HISTORICAL_CONTROLLER,CONTROLLER)
        with self.assertRaises(AssertionError):
            load(historical_text+'\njobs:\n  deploy:\n    if: github.event_name == \'push\'\n')
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
        assert doc['permissions']=={'actions':'write','contents':'read','issues':'write','pull-requests':'write'}
        assert doc['concurrency']['group']=='module025-protected-uat-control'
        assert doc['concurrency']['cancel-in-progress']=='false'
        job=doc['jobs']['admit'];assert 'environment' not in job
        assert "github.actor == 'ahmedadeyemi-cts'" in job['if'] and 'github.event.issue.number == 887' in job['if']
        assert all('azure/login' not in s.get('uses','') for s in job['steps'])
        dispatch=next(s for s in job['steps'] if s.get('name','').startswith('Authorize and dispatch once'))
        assert dispatch['env']['FLOWHIVE_PSA_PROTECTED_CUTOVER_FILE']=='.github/flowhive-psa-protected-cutover.json'
        assert dispatch['env']['FLOWHIVE_PSA_DISPATCH_EVIDENCE_FILE']=='${{ runner.temp }}/flowhive-psa-dispatch-attempt.json'
        assert 'enable' not in dispatch['name'].lower() and 'reseal' not in dispatch['name'].lower()
        artifact=next(s for s in job['steps'] if s.get('name')=='Upload sanitized FlowHive dispatch evidence')
        assert artifact['if']=='always()'
        assert artifact['uses'].startswith('actions/upload-artifact@')
        assert artifact['with']['if-no-files-found']=='ignore'
    def test_control_only_merge_cannot_trigger_an_unintended_deployment(self):
        from fnmatch import fnmatchcase
        triggers=self.doc['on']['push']['paths']
        controls=(ROOT/'.github/flowhive-psa-release-control-files.txt').read_text().splitlines()
        self.assertFalse(any(fnmatchcase(name,pattern) for name in controls for pattern in triggers))
        self.assertEqual(self.doc['on']['push']['branches'],['main'])
        for name in ['src/backend/ProjectTime.Api/Modules/Example.cs','src/frontend/project-time-web/src/Example.jsx']:
            self.assertTrue(any(fnmatchcase(name,pattern) for pattern in triggers))
        self.assertIn('workflow_dispatch',self.doc['on'])
    def test_all_unrelated_original_steps_remain_unchanged(self):
        base=os.environ.get('CONTROL_BASE')
        if not base:self.skipTest('Exact main controller comparison runs in PR CI with CONTROL_BASE.')
        old=load(subprocess.check_output(['git','show',base+':'+CONTROLLER],cwd=ROOT,text=True))
        reviewed = os.environ.get('GITHUB_HEAD_REF') == 'fix/flowhive-reviewed-regeneration-control-20260907'
        successor = os.environ.get('GITHUB_HEAD_REF') == 'control/flowhive-sow-successor-approval-20260909'
        # No controller changes are permitted in the exact seven-file digest repair.
        if old==self.doc:
            return
        # This integration starts from the already merged #875 controller.
        # Compare by unique step name because #874 deliberately moves the work
        # gates before SOW composition; never accept adding/dropping a step.
        before=old['jobs']['deploy']['steps']; after=self.doc['jobs']['deploy']['steps']
        stabilization = os.environ.get('GITHUB_HEAD_REF') == STABILIZATION_BRANCH
        old_steps={step['name']:step for step in before}
        self.assertEqual(len(old_steps),len(before))
        if stabilization:
            guard_name='Verify admitted controller identity before deployment mutations'
            self.assertEqual(len(after),len(before)+1)
            self.assertIn(guard_name,{step['name'] for step in after})
            after=[step for step in after if step.get('name') != guard_name]
        else:
            self.assertEqual(len(after),len(before))
        self.assertEqual(set(old_steps),{step['name'] for step in after})
        revised={'assigned_work_uat','utilization_uat','module025_fixture','module025_uat'}
        for step in after:
            a=copy.deepcopy(old_steps[step['name']]);b=copy.deepcopy(step)
            if b.get('id') in revised:
                a.pop('if',None);b.pop('if',None)
                if b['id']=='module025_fixture':
                    a['run']=a['run'].replace('echo "expires_at=$FIXTURE_EXPIRES_AT" >> "$GITHUB_OUTPUT"\n','')
                    b['run']=b['run'].replace('echo "expires_at=$FIXTURE_EXPIRES_AT" >> "$GITHUB_OUTPUT"\n','')
                if b['id']=='module025_uat':
                    a['env'].pop('MODULE025_UAT_EXPIRES_AT',None)
                    b['env'].pop('MODULE025_UAT_EXPIRES_AT',None)
            if reviewed and b.get('id') == 'assigned_work_uat':
                b['env'].pop('RELIABILITY_RELEASE_COMMIT',None)
            if successor:
                for key in ['if','run']:
                    if key in b:
                        b[key]=b[key].replace('release/flowhive-sow-successor-20260908','feature/flowhive-enterprise-psa-revamp-20260906')
                if b.get('run'):
                    b['run']=b['run'].replace(
                        'Only the approved successor FlowHive candidate branch, legacy V2 branch, or exact merged main may use manual Protected-Test deployment.',
                        'Only the authorized FlowHive V2 candidate branch or exact merged main may use manual Protected-Test deployment.')
                if b.get('id') == 'migration' or b.get('name') == 'Guard exact source and validate release':
                    ending='\n' if b['run'].endswith('\n') else ''
                    b['run']='\n'.join(line for line in b['run'].splitlines()
                                        if 'database/migrations/106_' not in line) + ending
                if step['name'] == 'Publish protected-Test release summary':
                    b['run']=b['run'].replace('Release lane: exact pre-merge FlowHive/SOW successor candidate; PR #887 remains unmerged', 'Release lane: exact pre-merge PSA candidate; feature PR #872 remains unmerged')
                    b['run']=b['run'].replace('Migrations 103/104/105/106: applied and verified', 'Migrations 103/104/105: applied and verified')
            if reviewed and (b.get('id') == 'migration' or b.get('name') == 'Guard exact source and validate release'):
                ending='\n' if b['run'].endswith('\n') else ''
                b['run']='\n'.join(line for line in b['run'].splitlines()
                                    if 'database/migrations/103_' not in line
                                    and 'database/migrations/104_' not in line
                                    and 'database/migrations/105_' not in line
                                    and 'database/rollback/103_' not in line
                                    and 'database/rollback/104_' not in line
                                    and 'database/rollback/105_' not in line) + ending
            if reviewed and step['name'] == 'Publish protected-Test release summary':
                b['run']=b['run'].replace("- Migrations 103/104/105: applied and verified", "- Migrations 103/104: applied and verified")
            self.assertEqual(a,b,step['name'])
        before_on=copy.deepcopy(old['on']);after_on=copy.deepcopy(self.doc['on'])
        if stabilization:
            before_on['workflow_dispatch']['inputs']['admission_controller_sha']=after_on['workflow_dispatch']['inputs']['admission_controller_sha']
        if reviewed:
            after_on['push']['paths'].remove('database/migrations/105_flowhive_reviewed_regeneration.sql')
            after_on['push']['paths'].remove('database/rollback/105_flowhive_reviewed_regeneration_rollback.sql')
        if successor:
            after_on['push']['paths'].remove('database/migrations/106_module025_sow_sell_register.sql')
            after_on['workflow_dispatch']['inputs']['release_branch']['default']='fix/shared-project-document-planning-20260819'
        self.assertEqual(before_on,after_on)
        self.assertEqual(old['jobs']['deploy']['env'],self.doc['jobs']['deploy']['env'])


if __name__=='__main__':unittest.main()
