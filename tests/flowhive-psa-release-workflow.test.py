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
    release_guard=next(s for s in steps if s.get('name')=='Guard exact source and validate release')
    assert 'database/migrations/105_flowhive_reviewed_regeneration.sql' in release_guard['run']
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

def verify_ci_script_limits(doc):
    for job in doc['jobs'].values():
        for step in job.get('steps', []):
            body = step.get('run', '')
            assert len(body) <= 21000, 'GitHub run script limit exceeded'
            # Embedded expressions cause GitHub to construct a format() expression
            # with escaped shell quotes/braces. Keep long scripts literal instead.
            assert len(body) < 19000 or '${{' not in body, 'Long CI script must use step environment inputs'

class WorkflowContract(unittest.TestCase):
    def setUp(self):self.doc=load((ROOT/CONTROLLER).read_text())
    def test_parsed_workflow(self):verify(self.doc)
    def test_controller_ci_script_limits_and_safe_pr_input(self):
        for name in ['projectpulse-release-test-control-ci.yml', 'projectpulse-release-test-control-ci-reregistered.yml']:
            doc=load((ROOT/'.github/workflows'/name).read_text())
            verify_ci_script_limits(doc)
            step=next(s for s in doc['jobs']['validate']['steps'] if s.get('name')=='Validate governed protected-Test controller')
            self.assertEqual(step['env']['PR_NUMBER'], '${{ github.event.pull_request.number }}')
            self.assertEqual(step['run'].count('"$PR_NUMBER"'), 2)
            self.assertNotIn('${{', step['run'])
            subprocess.run(['bash','-n'], input=step['run'], text=True, check=True, capture_output=True)
        for body in ['x'*21001, 'x'*19000+'${{ github.event.pull_request.number }}']:
            with self.assertRaises(AssertionError):
                verify_ci_script_limits({'jobs':{'fixture':{'steps':[{'run':body}]}}})

    def test_ci_databases_use_masked_ephemeral_credentials_and_loopback_only(self):
        # The control-only branch intentionally has no FlowHive feature CI.
        # Both changed fixture jobs are exercised on the feature candidate itself.
        feature=ROOT/'.github/workflows/flowhive-enterprise-psa-ci.yml'
        if not feature.exists():self.skipTest('Feature database fixtures are validated on the exact candidate.')
        for name,job_name in [('flowhive-enterprise-psa-ci.yml','execution-database'),('flowhive-psa-release-control-ci.yml','migrations')]:
            doc=load((ROOT/'.github/workflows'/name).read_text())
            job=doc['jobs'][job_name]
            self.assertNotIn('services', job)
            self.assertNotIn('PGPASSWORD', job.get('env',{}))
            self.assertNotIn('FLOWHIVE_TEST_DB', job.get('env',{}))
            steps=job['steps']
            setup=next(s for s in steps if s.get('id')=='fixture_database')
            body=setup['run']
            for required in ['openssl rand -hex 32','::add-mask::$password','--publish 127.0.0.1::5432','--env-file "$env_file"','trap - ERR','CI_POSTGRES_CONTAINER=$name','timeout 5s docker inspect']:
                self.assertIn(required,body)
            self.assertNotIn('${{', body)
            cleanup=steps[-1]
            self.assertEqual(cleanup['name'],'Remove isolated PostgreSQL fixture')
            self.assertEqual(cleanup['if'],'always()')
            self.assertIn('docker rm -f "$name"',cleanup['run'])
            for step in [setup,cleanup]:
                subprocess.run(['bash','-n'],input=step['run'],text=True,check=True,capture_output=True)

    def test_successor_migration_fixture_uses_exact_head_as_explicit_staging(self):
        workflow=load((ROOT/'.github/workflows/flowhive-psa-release-control-ci.yml').read_text())
        candidate=next(s for s in workflow['jobs']['migrations']['steps'] if s.get('id')=='candidate')
        self.assertIn('test -s .github/flowhive-psa-protected-test-candidate.json', candidate['run'])
        self.assertIn("release/flowhive-sow-successor-20260908", candidate['run'])
        self.assertIn("echo 'staging=successor'", candidate['run'])
        control=next(s for s in workflow['jobs']['migrations']['steps'] if s.get('uses','').startswith('actions/checkout@') and s.get('with',{}).get('path')=='control')
        self.assertEqual(control['with']['fetch-depth'], '0')
        exercise=next(s for s in workflow['jobs']['migrations']['steps'] if s.get('name','').startswith('Exercise selected release SQL'))
        self.assertEqual(exercise['env']['FLOWHIVE_MIGRATION_STAGING'], '${{ steps.candidate.outputs.staging }}')
        fixture=(ROOT/'tests/flowhive-psa-migration-fixture.py').read_text()
        self.assertIn("FLOWHIVE_MIGRATION_STAGING", fixture)
        self.assertIn("106_module025_sow_sell_register.sql", fixture)
        self.assertIn("approval['sha'] != pr['head']['sha']", fixture)
        entrypoint=(ROOT/'scripts/release-test/apply-flowhive-psa-migrations.sh').read_text()
        self.assertIn('106_module025_sow_sell_register.sql', entrypoint)
        self.assertIn('FLOWHIVE_PSA_MIGRATIONS_103_104_105_106=APPLIED_AND_VERIFIED', entrypoint)
        builder=(ROOT/'scripts/release-test/build-and-run-flowhive-psa-migrations.sh').read_text()
        self.assertIn('106_module025_sow_sell_register', builder)
        self.assertIn('--argjson migrations "$MIGRATIONS_JSON"', builder)
        production_workflow=(ROOT/'.github/workflows/celar-ai-production-platform-ci.yml').read_text()
        self.assertIn("FLOWHIVE_PROXY_LIMIT='deployment/containers/web/default.conf.template'", production_workflow)
        self.assertIn('grep -Fxq "$FLOWHIVE_PROXY_LIMIT" .github/flowhive-enterprise-psa-release-files.txt', production_workflow)
        project_forge_workflow=(ROOT/'.github/workflows/module033-project-forge-ci.yml').read_text()
        self.assertIn("FLOWHIVE_PROXY_LIMIT='deployment/containers/web/default.conf.template'", project_forge_workflow)
        self.assertIn('grep -Fxq "$FLOWHIVE_PROXY_LIMIT" .github/flowhive-enterprise-psa-release-files.txt', project_forge_workflow)

    def test_sell_notification_revalidates_current_engagement_assignments(self):
        worker=(ROOT/'src/backend/ProjectTime.Api/Modules/Module025SowSellWorker.cs').read_text()
        self.assertIn('SowRecipientsStillValidAsync(connection, work.Package.EngagementId', worker)
        self.assertIn('engagement.account_executive_user_id', worker)
        self.assertIn('engagement.resale_user_id', worker)
        self.assertIn("engagement.status='confirmed'", worker)
        self.assertIn('@account_executive_roles', worker)
        self.assertIn('@inside_sales_roles', worker)
        self.assertIn('RECIPIENT_ASSIGNMENT_REVIEW_REQUIRED', worker)
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
        # No controller changes are permitted in the exact seven-file digest repair.
        if old==self.doc:
            return
        # This integration starts from the already merged #875 controller.
        # Compare by unique step name because #874 deliberately moves the work
        # gates before SOW composition; never accept adding/dropping a step.
        before=old['jobs']['deploy']['steps']; after=self.doc['jobs']['deploy']['steps']
        old_steps={step['name']:step for step in before}
        self.assertEqual(len(old_steps),len(before))
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
        if reviewed:
            after_on['push']['paths'].remove('database/migrations/105_flowhive_reviewed_regeneration.sql')
            after_on['push']['paths'].remove('database/rollback/105_flowhive_reviewed_regeneration_rollback.sql')
        self.assertEqual(before_on,after_on)
        self.assertEqual(old['jobs']['deploy']['env'],self.doc['jobs']['deploy']['env'])


if __name__=='__main__':unittest.main()
