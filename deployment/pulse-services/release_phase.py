"""Run the reviewed activation only inside the canonical protected Test job.
The function never edits/dispatches a workflow or changes an account. On failure
it restores the previous verified service selection before removing owned resources.
"""
import json,os,re,shutil,signal,sys
from pathlib import Path
import cutover
from canonical_release import validate_context,validate_run
from service_images import context,existing_images,fingerprint,installed_selection
from state_preservation import require_cleanup_ownership
from activation_contracts import acceptance_job_name

def initialize():
    source,safe=context();run=os.environ.get('GITHUB_RUN_ID','')
    validate_context(os.environ,source,run)
    root=Path(os.environ['RUNNER_TEMP']).resolve();private=Path(os.environ['PULSE_PRIVATE_EVIDENCE'])
    cutover.check(not private.is_symlink() and private.resolve()==root/'pulse-services-private','private_directory_scope')
    cutover.check(not private.exists(),'private_directory_already_exists')
    private.mkdir(mode=0o700);os.umask(0o077)
    cutover.SHA=source;cutover.RUN=run;cutover.PRIVATE=private;cutover.SAFE=safe
    cutover.check(Path(os.environ['PULSE_IMAGE_MANIFEST']).resolve()==safe/'registry-images.json','manifest_path_scope')
    validate_run(cutover.gh('actions/runs/'+run),cutover.gh('actions/runs/'+run+'/jobs?filter=latest&per_page=10'),source,run)
    return source,run,safe,private

def remove_success_job():
    path=cutover.PRIVATE/'service-acceptance.json'
    if not path.exists():return
    accepted=json.loads(path.read_text());name=acceptance_job_name(cutover.RUN)
    cutover.check(accepted.get('job')==name and accepted.get('status')=='passed','acceptance_job_identity_changed')
    resource=cutover.ROOT+'/providers/Microsoft.App/jobs/'+name
    current=cutover.rest('GET',resource)
    require_cleanup_ownership(current,cutover.SHA,cutover.RUN,application=False)
    cutover.rest('DELETE',resource)

def execute():
    source,run,safe,private=initialize()
    receipt={'sourceSha':source,'runId':run,'status':'failed','servicesActivated':False,
        'productionMutation':False,'newServerCreated':False,'originalSecurityFindingsClosed':0}
    build={};mutation_started=False
    try:
        build=json.loads((safe/'build-identities.json').read_text())
        cutover.check(build.get('source')==source and build.get('fingerprint')==fingerprint(),'build_receipt_changed')
        selected=installed_selection();mode=build.get('mode')
        cutover.check((not selected and mode=='initial') or
                      (selected and mode in ('verify_existing','upgrade_existing')),
                      'selection_changed_after_scan')
        if mode=='verify_existing':
            cutover.preflight(allow_selected=True)
            before=json.loads((private/'preflight.json').read_text())['adminIdentity']
            expected=json.loads((safe/'registry-images.json').read_text())
            cutover.check(existing_images(build['fingerprint'])==expected,'installed_images_changed')
            cutover.check(cutover.local_admin(after=True)==before,'local_admin_identity_changed')
            receipt.update(status='passed',servicesActivated=True,mode='existing_verified',images=expected,
                localSuperAdministratorLoginPassed=True,localSuperAdministratorIdentityUnchanged=True)
        elif mode=='upgrade_existing':
            mutation_started=True
            cutover.upgrade()
            receipt=json.loads((safe/'cutover.json').read_text())
            receipt.update(servicesActivated=True,mode='upgrade_existing',runId=run)
        else:
            mutation_started=True
            cutover.prepare();cutover.switch()
            receipt=json.loads((safe/'cutover.json').read_text())
            receipt.update(servicesActivated=True,mode='initial_activation',runId=run)
        receipt.update(status='pending_application_uat',applicationUatPassed=False)
        (safe/'cutover.json').write_text(json.dumps(receipt,indent=2)+'\n')
        print('PULSE_PRIVATE_SERVICES=NATIVE_READY applicationAcceptancePending=true mode='+receipt['mode'],flush=True)
        return 0
    except Exception as error:
        code=str(error) if isinstance(error,(ValueError,cutover.CutoverError)) and re.fullmatch('[a-z_]{1,100}',str(error)) else type(error).__name__
        receipt.update(status='failed',diagnostic=code,servicesActivated=False)
        try:
            if mutation_started:cutover.rollback()
            receipt['rollbackCompleted']=True
        except Exception as rollback_error:
            rollback_code=(str(rollback_error) if isinstance(rollback_error,(ValueError,cutover.CutoverError))
                           and re.fullmatch('[a-z_]{1,100}',str(rollback_error)) else type(rollback_error).__name__)
            receipt['rollbackCompleted']=False;receipt['rollbackDiagnostic']=rollback_code
            print('PULSE_PRIVATE_SERVICE_ROLLBACK_REQUIRES_REVIEW diagnostic='+rollback_code,flush=True)
        print('PULSE_PRIVATE_SERVICE_PHASE=FAILED diagnostic='+code,flush=True)
        return 1
    finally:
        (safe/'activation-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
        if receipt.get('status')=='failed':shutil.rmtree(private)

if __name__=='__main__':
    def interrupted(signum,frame):raise RuntimeError('activation_interrupted')
    signal.signal(signal.SIGTERM,interrupted)
    try:
        if len(sys.argv)>2 or (len(sys.argv)==2 and sys.argv[1]!='finalize'):raise ValueError('invalid_phase')
        if len(sys.argv)==2:
            from post_activation_acceptance import finalize
            raise SystemExit(finalize())
        raise SystemExit(execute())
    except Exception as error:
        print('PULSE_PRIVATE_SERVICE_INITIALIZATION_FAILED='+type(error).__name__);raise SystemExit(1)
