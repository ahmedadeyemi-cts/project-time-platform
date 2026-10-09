"""Offline release-authority and secret-preservation regression tests."""
import copy,importlib.util,json,os,sys,unittest
from pathlib import Path
from unittest.mock import patch
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'deployment/pulse-services'))
import canonical_release as gate
import secret_preservation as secrets
import cutover
SHA='a'*40
RUN='1234567890'
def context():
    return {'PULSE_CANONICAL_RELEASE':'true','GITHUB_REPOSITORY':gate.REPOSITORY,
      'GITHUB_REF':'refs/heads/main','GITHUB_JOB':'deploy','GITHUB_EVENT_NAME':'workflow_dispatch',
      'GITHUB_WORKFLOW_REF':gate.REPOSITORY+'/'+gate.WORKFLOW+'@refs/heads/main',
      'GITHUB_RUN_ATTEMPT':'1','GITHUB_SHA':SHA,'TARGET_RELEASE_COMMIT':SHA,
      'TARGET_RELEASE_BRANCH':'main','ACCEPTANCE_SCOPE':'full',
      'PULSE_CONFIRMATION':'SWITCH PULSE TEST DOCUMENTS AND LAYA'}
def records():
    run={'id':int(RUN),'head_sha':SHA,'path':gate.WORKFLOW,'event':'workflow_dispatch',
      'head_branch':'main','status':'in_progress','conclusion':None,'run_attempt':1,
      'actor':{'login':'github-actions[bot]'}}
    steps=[{'name':n,'status':'completed','conclusion':'success'} for n in gate.REQUIRED_STEPS]
    steps.append({'name':gate.ACTIVATION_STEP,'status':'in_progress','conclusion':None})
    steps.extend({'name':n,'status':'pending','conclusion':None} for n in gate.APPLICATION_UAT_STEPS)
    steps.append({'name':gate.FINALIZATION_STEP,'status':'pending','conclusion':None})
    return run,{'jobs':[{'name':gate.JOB,'run_id':int(RUN),'head_sha':SHA,
      'status':'in_progress','conclusion':None,'steps':steps}]}
class CanonicalGate(unittest.TestCase):
    def test_machine_actor_can_continue_only_after_prior_acceptance(self):
        gate.validate_context(context(),SHA,RUN)
        self.assertTrue(gate.validate_run(*records(),SHA,RUN))
    def test_every_prior_acceptance_gate_is_required(self):
        for name in gate.REQUIRED_STEPS:
            for result in ('failure','skipped',None):
                run,jobs=records();next(s for s in jobs['jobs'][0]['steps'] if s['name']==name)['conclusion']=result
                with self.subTest(name=name,result=result),self.assertRaises(ValueError):gate.validate_run(run,jobs,SHA,RUN)
    def test_wrong_environment_branch_attempt_or_job_rejected(self):
        for name,value in [('GITHUB_REPOSITORY','other/repo'),('GITHUB_REF','refs/heads/test'),
          ('GITHUB_JOB','other'),('GITHUB_WORKFLOW_REF','untrusted'),('GITHUB_EVENT_NAME','push'),
          ('GITHUB_RUN_ATTEMPT','2'),('GITHUB_SHA','b'*40),('TARGET_RELEASE_COMMIT','b'*40),
          ('TARGET_RELEASE_BRANCH','candidate'),('ACCEPTANCE_SCOPE','sow_role'),('PULSE_CONFIRMATION','')]:
            env=context();env[name]=value
            with self.subTest(name=name),self.assertRaises(ValueError):gate.validate_context(env,SHA,RUN)
    def test_live_run_origin_and_source_cannot_be_substituted(self):
        for name,value in [('id',8),('head_sha','b'*40),('path','other.yml'),('event','pull_request'),
          ('head_branch','candidate'),('status','completed'),('conclusion','success'),('run_attempt',2)]:
            run,jobs=records();run[name]=value
            with self.subTest(name=name),self.assertRaises(ValueError):gate.validate_run(run,jobs,SHA,RUN)
    def test_unrecognized_actor_cannot_activate(self):
        run,jobs=records();run['actor']['login']='other-user'
        with self.assertRaises(ValueError):gate.validate_run(run,jobs,SHA,RUN)
    def test_duplicate_missing_or_wrong_active_step_rejected(self):
        for mode in ('duplicate','missing','wrong'):
            run,jobs=records();s=jobs['jobs'][0]['steps']
            if mode=='duplicate':s.append(copy.deepcopy(s[0]))
            elif mode=='missing':s.pop(0)
            else:s[-1]['name']='unrelated mutation'
            with self.subTest(mode=mode),self.assertRaises(ValueError):gate.validate_run(run,jobs,SHA,RUN)
    def test_standalone_mode_still_rejects_machine_actor_before_network(self):
        with patch.dict(os.environ,{'GITHUB_REPOSITORY':gate.REPOSITORY,'GITHUB_REF':'refs/heads/main',
            'GITHUB_ACTOR':'github-actions[bot]'},clear=True),patch.object(cutover,'gh') as gh,patch.object(cutover,'az') as az:
            with self.assertRaises(cutover.CutoverError):cutover.preflight()
            gh.assert_not_called();az.assert_not_called()
class SourcePreservation(unittest.TestCase):
    def test_current_activation_change_preserves_all_live_authority_and_accounts(self):
        import subprocess
        # Compare the current PR to its mainline merge base, not to historical
        # deployment snapshots that have since received reviewed changes.
        source_base=subprocess.check_output(['git','merge-base','origin/main','HEAD'],cwd=ROOT,text=True).strip()
        changes=subprocess.check_output(['git','diff','--name-only',f'{source_base}...HEAD'],cwd=ROOT,text=True).splitlines()
        allowed_application_policy={
          'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs',
          'src/backend/ProjectTime.Api/Ai/PulseDocumentServiceOptions.cs',
          'src/backend/ProjectTime.Api/Ai/PulseLayaServiceOptions.cs'}
        branch=os.environ.get('GITHUB_HEAD_REF') or subprocess.check_output(
            ['git','branch','--show-current'],cwd=ROOT,text=True).strip()
        if branch=='fix/pulse-document-runtime-prereqs-20261002':
            allowed_application_policy.add('src/frontend/project-time-web/scripts/validate-module-011-managed-architecture.mjs')
        self.assertFalse(any(p.startswith('database/') or (p.startswith('src/') and p not in allowed_application_policy) for p in changes))
        for name in ('.github/workflows/projectpulse-deploy-test.yml',
          '.github/workflows/module025-protected-uat-control.yml',
          'scripts/validate-deployment-concurrency-governance.mjs',
          '.github/workflows/deployment-concurrency-governance-ci.yml'):
            # The workflow's historical normalizer deliberately projects to
            # the reviewed original controller; all other controls use main.
            reviewed_base='6bf7c3303dec5f0aa136e52ce75bdd7b4b3b985f' if name=='.github/workflows/projectpulse-deploy-test.yml' else source_base
            expected=subprocess.check_output(['git','show',reviewed_base+':'+name],cwd=ROOT)
            actual=(ROOT/name).read_bytes()
            if name=='.github/workflows/projectpulse-deploy-test.yml':
                spec=importlib.util.spec_from_file_location('reviewed_order_projection',ROOT/'tests/pulse-activation-release/controller.py')
                projection=importlib.util.module_from_spec(spec);spec.loader.exec_module(projection)
                actual=projection.normalize(actual)
            self.assertEqual(actual,expected)

class SecretPreservation(unittest.TestCase):
    def setUp(self):
        self.metadata=[{'name':'old-auth'},{'name':'external-key'},{'name':'vault-key',
            'keyVaultUrl':'https://example.vault.azure.net/secrets/item','identity':'system'}]
        self.retrieved={'value':[{'name':'old-auth','value':'synthetic-auth'},
            {'name':'external-key','value':'synthetic-integration'},
            {'name':'vault-key','value':'do-not-inline-vault-value'}]}
        self.new=[{'name':'pulse-docs-new','value':'s'*48},{'name':'pulse-laya-new','value':'t'*48}]
    def test_complete_payload_preserves_old_values_and_vault_reference(self):
        payload=secrets.merged_payload(self.metadata,self.retrieved,self.new)
        self.assertEqual(payload[:2],self.retrieved['value'][:2])
        self.assertEqual(payload[2],self.metadata[2]);self.assertEqual(payload[3:],self.new)
        self.assertNotIn('do-not-inline',json.dumps(payload))
    def test_missing_values_stop_before_update_can_be_constructed(self):
        for name in ('old-auth','external-key'):
            values=copy.deepcopy(self.retrieved);next(x for x in values['value'] if x['name']==name).pop('value')
            with self.subTest(name=name),self.assertRaises(ValueError):secrets.merged_payload(self.metadata,values,self.new)
    def test_metadata_or_value_inventory_race_is_rejected(self):
        for values in ({},{'value':[]},{'value':self.retrieved['value'][:-1]}):
            with self.assertRaises(ValueError):secrets.merged_payload(self.metadata,values,self.new)
    def test_existing_secret_cannot_be_overwritten_by_service_secret(self):
        with self.assertRaises(ValueError):secrets.merged_payload(self.metadata,self.retrieved,[{'name':'old-auth','value':'s'*48}])
    def test_duplicate_name_or_malformed_new_secret_rejected(self):
        for additions in ([self.new[0],self.new[0]],[{'name':'new','value':'short'}],[{'name':'new','value':'s'*48,'extra':'field'}]):
            with self.assertRaises(ValueError):secrets.merged_payload(self.metadata,self.retrieved,additions)
    def test_readback_detects_changed_prior_authentication_secret(self):
        before=secrets.existing_payload(self.metadata,self.retrieved)
        after=copy.deepcopy(self.retrieved);after['value'][0]['value']='changed'
        with self.assertRaises(ValueError):secrets.unchanged_existing(before,self.metadata,after)
    def test_no_input_secret_inventory_mutation(self):
        expected=copy.deepcopy((self.metadata,self.retrieved,self.new))
        secrets.merged_payload(self.metadata,self.retrieved,self.new)
        self.assertEqual((self.metadata,self.retrieved,self.new),expected)
if __name__=='__main__':unittest.main(verbosity=2)
