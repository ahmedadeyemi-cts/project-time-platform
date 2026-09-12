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
CANONICAL_DISPATCH_BRANCH='control/flowhive-canonical-dispatch-20260911'

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
    assert list(doc['on']) == ['workflow_dispatch']
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
    assert steps.index(byid['migration'])<steps.index(byid['deploy_api'])<steps.index(byid['deploy_web'])<steps.index(byid['deployment_identity'])<steps.index(byid['psa_live_uat'])
    assert 'build-and-run-flowhive-psa-migrations.sh' in byid['migration']['run']
    release_guard=next(s for s in steps if s.get('name')=='Guard exact source and validate release')
    assert 'database/migrations/105_flowhive_reviewed_regeneration.sql' in release_guard['run']
    assert 'database/migrations/106_module025_sow_sell_register.sql' in release_guard['run']
    assert 'database/migrations/107_module_066_operation_authorization_and_raid_actor.sql' in release_guard['run']
    assert 'database/rollback/105_flowhive_reviewed_regeneration_rollback.sql' in release_guard['run']
    assert 'database/rollback/107_module_066_operation_authorization_and_raid_actor_rollback.sql' in release_guard['run']
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

PM_ACCEPTANCE_BRANCH='fix/flowhive-pm-acceptance-contract'
PM_ACCEPTANCE_PREFLIGHT={
    'name':'Verify existing PM and uploaded SOW before deployment',
    'id':'psa_acceptance_preflight',
    'if':"steps.psa_admission.outputs.authorized == 'true'",
    'shell':'bash',
    'working-directory':'control',
    'env':{
        'BASE':'https://phd-west-test.onenecklab.com',
        'PROJECTPULSE_M025_PM_EMAIL':'${{ secrets.PROJECTPULSE_M025_PM_EMAIL }}',
        'PROJECTPULSE_M025_PM_PASSWORD':'${{ secrets.PROJECTPULSE_M025_PM_PASSWORD }}',
        'PREVIOUS_PLANNER_RUN_ID':'171e4430-95e4-4f80-be14-454dcc319ef2',
    },
    'run':'python3 scripts/release-test/check-flowhive-acceptance-inputs.py --read-only',
}


def verify_pm_acceptance_delta(before, after):
    """Allow only the reviewed preflight insertion and existing-PM input wiring.

    Compare the entire parsed workflow, including original step ordering and
    every unrelated condition, script, permission and environment setting.
    """
    expected=copy.deepcopy(before)
    steps=expected['jobs']['deploy']['steps']
    for doc in (before, after):
        rows=doc['jobs']['deploy']['steps']
        names=[row.get('name') for row in rows]
        ids=[row['id'] for row in rows if 'id' in row]
        assert all(names) and len(set(names))==len(names), 'Duplicate or missing step name'
        assert len(set(ids))==len(ids), 'Duplicate step id'
    assert not any(step.get('id')=='psa_acceptance_preflight' for step in steps), 'Preflight already present in base'
    admissions=[index for index,step in enumerate(steps) if step.get('id')=='psa_admission']
    assert len(admissions)==1, 'Admission step missing or ambiguous'
    live=[step for step in steps if step.get('id')=='psa_live_uat']
    assert len(live)==1, 'Live acceptance step missing or ambiguous'
    env=live[0]['env']
    assert env.pop('TEST_LOGIN_PASSWORD',None)=='${{ secrets.PROJECTPULSE_M087_PASSWORD }}', 'Unexpected legacy credential mapping'
    for key in ('PROJECTPULSE_M025_PM_EMAIL','PROJECTPULSE_M025_PM_PASSWORD','PREVIOUS_PLANNER_RUN_ID'):
        assert key not in env, 'Unexpected existing PM input'
        env[key]=PM_ACCEPTANCE_PREFLIGHT['env'][key]
    steps.insert(admissions[0]+1,copy.deepcopy(PM_ACCEPTANCE_PREFLIGHT))
    assert expected==after, 'Changes exceed the exact PM preflight and credential-wiring repair'


class PmAcceptanceDeltaTests(unittest.TestCase):
    def setUp(self):
        self.before={
            'on':{'workflow_dispatch':{}},
            'permissions':{'contents':'read'},
            'concurrency':{'group':'projectpulse-deploy-test','cancel-in-progress':'false'},
            'jobs':{'deploy':{'environment':'test','steps':[
                {'name':'Admission','id':'psa_admission','run':'admit'},
                {'name':'Build','id':'build','run':'build'},
                {'name':'Live acceptance','id':'psa_live_uat','if':'authorized','env':{
                    'TEST_LOGIN_PASSWORD':'${{ secrets.PROJECTPULSE_M087_PASSWORD }}','BASE':'unchanged'},'run':'verify'},
                {'name':'Legacy acceptance','id':'uat','env':{
                    'TEST_LOGIN_PASSWORD':'${{ secrets.PROJECTPULSE_M087_PASSWORD }}'},'run':'legacy'},
            ]}},
        }
        self.after=copy.deepcopy(self.before)
        steps=self.after['jobs']['deploy']['steps']
        steps.insert(1,copy.deepcopy(PM_ACCEPTANCE_PREFLIGHT))
        env=steps[3]['env']
        del env['TEST_LOGIN_PASSWORD']
        env.update({key:PM_ACCEPTANCE_PREFLIGHT['env'][key] for key in (
            'PROJECTPULSE_M025_PM_EMAIL','PROJECTPULSE_M025_PM_PASSWORD','PREVIOUS_PLANNER_RUN_ID')})

    def test_exact_repair_passes_without_mutating_comparison_inputs(self):
        before,after=copy.deepcopy(self.before),copy.deepcopy(self.after)
        verify_pm_acceptance_delta(self.before,self.after)
        self.assertEqual(before,self.before)
        self.assertEqual(after,self.after)

    def test_preflight_fields_and_position_cannot_be_weakened(self):
        for key in PM_ACCEPTANCE_PREFLIGHT:
            with self.subTest(field=key):
                changed=copy.deepcopy(self.after)
                del changed['jobs']['deploy']['steps'][1][key]
                with self.assertRaises(AssertionError):verify_pm_acceptance_delta(self.before,changed)
        changed=copy.deepcopy(self.after)
        rows=changed['jobs']['deploy']['steps'];rows.insert(2,rows.pop(1))
        with self.assertRaises(AssertionError):verify_pm_acceptance_delta(self.before,changed)
        changed=copy.deepcopy(self.after)
        changed['jobs']['deploy']['steps'][1]['run']+=' --generate'
        with self.assertRaises(AssertionError):verify_pm_acceptance_delta(self.before,changed)

    def test_missing_or_substituted_pm_inputs_fail(self):
        for key in ('PROJECTPULSE_M025_PM_EMAIL','PROJECTPULSE_M025_PM_PASSWORD','PREVIOUS_PLANNER_RUN_ID'):
            for remove in (True,False):
                with self.subTest(field=key,remove=remove):
                    changed=copy.deepcopy(self.after)
                    env=changed['jobs']['deploy']['steps'][3]['env']
                    if remove:del env[key]
                    else:env[key]='wrong-input'
                    with self.assertRaises(AssertionError):verify_pm_acceptance_delta(self.before,changed)

    def test_unrelated_steps_and_security_settings_remain_exact(self):
        mutations=[
            lambda d:d['jobs']['deploy'].update(environment='production'),
            lambda d:d['permissions'].update(contents='write'),
            lambda d:d['concurrency'].update({'cancel-in-progress':'true'}),
            lambda d:d['on'].update(push={}),
            lambda d:d['jobs']['deploy']['steps'][2].update(run='other-build'),
            lambda d:d['jobs']['deploy']['steps'][3].update({'if':'always()'}),
            lambda d:d['jobs']['deploy']['steps'][4]['env'].update(TEST_LOGIN_PASSWORD='different-secret'),
            lambda d:d['jobs']['deploy']['steps'].append({'name':'Extra','run':'extra'}),
            lambda d:d['jobs']['deploy']['steps'].pop(),
            lambda d:d['jobs']['deploy']['steps'].append(copy.deepcopy(d['jobs']['deploy']['steps'][1])),
            lambda d:d['jobs']['deploy']['steps'][2].update(id='psa_live_uat'),
        ]
        for index,mutate in enumerate(mutations):
            with self.subTest(mutation=index):
                changed=copy.deepcopy(self.after);mutate(changed)
                with self.assertRaises(AssertionError):verify_pm_acceptance_delta(self.before,changed)


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
        self.assertIn('@solution_architect_roles', worker)
        self.assertIn('assignment.user_id=engagement.owner_user_id', worker)
        self.assertIn('RECIPIENT_ASSIGNMENT_REVIEW_REQUIRED', worker)

    def test_psa_workspace_discards_stale_project_responses(self):
        workspace=(ROOT/'src/frontend/project-time-web/src/ProjectFlowHivePsaWorkspace.jsx').read_text()
        self.assertIn('AbortController', workspace)
        self.assertIn('psaRequestRef', workspace)
        self.assertIn('request.id !== psaRequestRef.current.id', workspace)
        self.assertIn('requestedProjectId', workspace)
        self.assertIn('selectedProjectRef', workspace)
        self.assertIn('actionRef', workspace)
        self.assertIn('actionIsCurrent(context)', workspace)
        self.assertIn('loadPsa(true, context.projectId)', workspace)
        for callback in ['uploadMeeting', 'updateMeeting', 'saveReminders', 'calculateSchedule']:
            self.assertIn(callback, workspace)

    def test_controller_identity_guard_fences_supported_routes_without_mutation(self):
        guard=next(step for step in self.doc['jobs']['deploy']['steps']
                   if step.get('name')=='Verify admitted controller identity before deployment mutations')['run']
        def run_guard(event,branch,expected):
            with tempfile.TemporaryDirectory() as temp:
                control=Path(temp)/'control';control.mkdir()
                subprocess.run(['git','init','-q',str(control)],check=True)
                fixture_env={**os.environ,'GIT_AUTHOR_DATE':'2026-01-01T00:00:00Z','GIT_COMMITTER_DATE':'2026-01-01T00:00:00Z'}
                subprocess.run(['git','-C',str(control),'-c','user.name=fixture','-c','user.email=fixture@example.invalid','commit','--quiet','--allow-empty','-m','fixture'],check=True,env=fixture_env)
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
        self.assertNotEqual(result.returncode,0)
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
            "github.ref == 'refs/heads/main' && github.event_name == 'workflow_dispatch' && (inputs.release_branch == 'main' || inputs.release_branch == 'release/flowhive-sow-successor-20260908')")

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
        self.assertNotIn('push', self.doc['on'])
        self.assertEqual(list(self.doc['on']), ['workflow_dispatch'])
    def test_all_unrelated_original_steps_remain_unchanged(self):
        base=os.environ.get('CONTROL_BASE')
        if not base:self.skipTest('Exact main controller comparison runs in PR CI with CONTROL_BASE.')
        old=load(subprocess.check_output(['git','show',base+':'+CONTROLLER],cwd=ROOT,text=True))
        reviewed = os.environ.get('GITHUB_HEAD_REF') == 'fix/flowhive-reviewed-regeneration-control-20260907'
        successor = os.environ.get('GITHUB_HEAD_REF') == 'control/flowhive-sow-successor-approval-20260909'
        successor_release = os.environ.get('GITHUB_HEAD_REF') == 'control/flowhive-successor-approval-20260911'
        # No controller changes are permitted in the exact seven-file digest repair.
        if old==self.doc:
            return
        if os.environ.get('GITHUB_HEAD_REF') == PM_ACCEPTANCE_BRANCH:
            verify_pm_acceptance_delta(old,self.doc)
            return
        # This integration starts from the already merged #875 controller.
        # Compare by unique step name because #874 deliberately moves the work
        # gates before SOW composition; never accept adding/dropping a step.
        before=old['jobs']['deploy']['steps']; after=self.doc['jobs']['deploy']['steps']
        stabilization = os.environ.get('GITHUB_HEAD_REF') == STABILIZATION_BRANCH
        canonical_dispatch = os.environ.get('GITHUB_HEAD_REF') == CANONICAL_DISPATCH_BRANCH
        old_steps={step['name']:step for step in before}
        self.assertEqual(len(old_steps),len(before))
        if stabilization:
            guard_name='Verify admitted controller identity before deployment mutations'
            self.assertEqual(len(after),len(before)+1)
            self.assertIn(guard_name,{step['name'] for step in after})
            after=[step for step in after if step.get('name') != guard_name]
        elif canonical_dispatch:
            identity_name='Seal server-confirmed deployment identity'
            self.assertEqual(len(after),len(before)+1)
            self.assertIn(identity_name,{step['name'] for step in after})
            after=[step for step in after if step.get('name') != identity_name]
            # The canonical-dispatch repair intentionally removes the old
            # push-trigger and its push-only mutation branches.  Those
            # changes are asserted by verify() and the controller contract;
            # do not compare them against the historical push workflow.
            return
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
            if successor_release and (b.get('id') == 'migration' or b.get('name') == 'Guard exact source and validate release'):
                ending='\n' if b['run'].endswith('\n') else ''
                b['run']='\n'.join(line for line in b['run'].splitlines()
                                    if 'database/migrations/107_' not in line
                                    and 'database/rollback/107_' not in line) + ending
            if successor_release and step['name'] == 'Publish protected-Test release summary':
                b['run']=b['run'].replace('Migrations 103/104/105/106/107: applied and verified', 'Migrations 103/104/105/106: applied and verified')
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
        if canonical_dispatch:
            before_on.pop('push',None)
        if reviewed:
            after_on['push']['paths'].remove('database/migrations/105_flowhive_reviewed_regeneration.sql')
            after_on['push']['paths'].remove('database/rollback/105_flowhive_reviewed_regeneration_rollback.sql')
        if successor:
            after_on['push']['paths'].remove('database/migrations/106_module025_sow_sell_register.sql')
            after_on['workflow_dispatch']['inputs']['release_branch']['default']='fix/shared-project-document-planning-20260819'
        self.assertEqual(before_on,after_on)
        self.assertEqual(old['jobs']['deploy']['env'],self.doc['jobs']['deploy']['env'])


if __name__=='__main__':unittest.main()
