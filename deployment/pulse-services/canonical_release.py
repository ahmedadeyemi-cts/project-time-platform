"""Validate the existing protected Test job before its private-service phase.
This module cannot dispatch, approve, enable or modify a workflow.
"""
import os,re
REPOSITORY='ahmedadeyemi-cts/project-time-platform'
WORKFLOW='.github/workflows/projectpulse-deploy-test.yml'
JOB='Validate, migrate, deploy, and verify protected Test'
REQUIRED_STEPS=(
 'Verify admitted controller identity before deployment mutations',
 'Guard exact source and validate release',
 'Seal server-confirmed deployment identity',
 'Build private Pulse service images',
 'Scan private Pulse documents image',
 'Scan private Pulse scanner image',
 'Scan private Pulse laya-gateway image',
 'Scan private Pulse laya image',
 'Publish scanned private Pulse service images',
)
APPLICATION_UAT_STEPS=(
 'Run protected-Test authenticated functional UAT',
 'Run protected-Test assigned-work visibility UAT',
 'Run protected-Test utilization role-scoping UAT',
 'Run protected-Test Module 025 SOW/GSD generation lifecycle UAT',
 'Disable exact-run Module 025 protected-Test authorization fixture',
 'Verify normal Solution Architect browser and retained register',
)
FINALIZATION_STEP='Finalize private Pulse activation after full application acceptance'
ACTIVATION_STEP='Activate and verify private Pulse document and Laya services'
def require(value,code):
    if not value:raise ValueError(code)
def validate_context(env,source,run):
    require(env.get('PULSE_CANONICAL_RELEASE')=='true','canonical_mode_required')
    require(env.get('GITHUB_REPOSITORY')==REPOSITORY and env.get('GITHUB_REF')=='refs/heads/main','trusted_main_required')
    require(env.get('GITHUB_JOB')=='deploy' and env.get('GITHUB_WORKFLOW_REF')==REPOSITORY+'/'+WORKFLOW+'@refs/heads/main','canonical_workflow_required')
    require(env.get('GITHUB_EVENT_NAME')=='workflow_dispatch' and env.get('GITHUB_RUN_ATTEMPT')=='1','fresh_dispatch_required')
    require(re.fullmatch('[0-9a-f]{40}',source or '') and re.fullmatch('[1-9][0-9]{0,19}',run or ''),'release_identity_invalid')
    require(env.get('GITHUB_SHA')==source and env.get('TARGET_RELEASE_COMMIT')==source and env.get('TARGET_RELEASE_BRANCH')=='main','exact_release_required')
    require(env.get('ACCEPTANCE_SCOPE')=='full','full_acceptance_required')
    require(env.get('PULSE_CONFIRMATION')=='SWITCH PULSE TEST DOCUMENTS AND LAYA','activation_selection_required')
def validate_run(run,jobs,source,number,*,final=False):
    require(isinstance(run,dict) and run.get('id')==int(number) and run.get('head_sha')==source,'run_identity_mismatch')
    require(run.get('path')==WORKFLOW and run.get('event')=='workflow_dispatch' and run.get('head_branch')=='main','run_origin_mismatch')
    require(run.get('status')=='in_progress' and run.get('conclusion') is None and run.get('run_attempt')==1,'run_not_active')
    require(run.get('actor',{}).get('login') in ('github-actions[bot]','ahmedadeyemi-cts'),'unrecognized_release_actor')
    rows=jobs.get('jobs',[]) if isinstance(jobs,dict) else []
    require(len(rows)==1 and rows[0].get('name')==JOB and rows[0].get('run_id')==int(number),'job_identity_mismatch')
    job=rows[0]
    require(job.get('head_sha')==source and job.get('status')=='in_progress' and job.get('conclusion') is None,'job_not_active')
    steps=job.get('steps',[])
    for required in REQUIRED_STEPS:
        matches=[s for s in steps if s.get('name')==required]
        require(len(matches)==1 and matches[0].get('status')=='completed' and matches[0].get('conclusion')=='success','prior_acceptance_required')
    active=[s for s in steps if s.get('status')=='in_progress']
    expected=FINALIZATION_STEP if final else ACTIVATION_STEP
    require(len(active)==1 and active[0].get('name')==expected,'activation_step_required')
    activation=[s for s in steps if s.get('name')==ACTIVATION_STEP]
    finalizer=[s for s in steps if s.get('name')==FINALIZATION_STEP]
    require(len(activation)==1 and len(finalizer)==1,'activation_lifecycle_required')
    positions=[next(i for i,s in enumerate(steps) if s is activation[0])]
    for required in APPLICATION_UAT_STEPS:
        matches=[s for s in steps if s.get('name')==required]
        require(len(matches)==1,'application_uat_gate_missing')
        positions.append(next(i for i,s in enumerate(steps) if s is matches[0]))
        require(matches[0].get('status')==('completed' if final else 'pending'),'application_uat_state_invalid')
        if final:require(matches[0].get('conclusion')=='success','application_uat_not_passed')
    positions.append(next(i for i,s in enumerate(steps) if s is finalizer[0]))
    require(positions==sorted(positions) and len(set(positions))==len(positions),'application_uat_order_invalid')
    if final:
        require(activation[0].get('status')=='completed' and activation[0].get('conclusion')=='success','activation_not_passed')
    require(all(s.get('conclusion') not in ('failure','cancelled','timed_out') for s in steps),'earlier_job_failure')
    return True
