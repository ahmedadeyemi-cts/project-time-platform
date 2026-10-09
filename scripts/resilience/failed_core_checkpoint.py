"""Repair only the registered zero-traffic failure through the native Test release job.
Artifact integrity, original run identity, and current Azure state all precede mutation.
"""
import copy
import hashlib
import io
import json
import subprocess
import zipfile

REPO='ahmedadeyemi-cts/project-time-platform'
APP='ca-phd-test-api-westus3'
RG='rg-project-health-dashboard-test-app-westus3'
RUN=37997906253
SHA='09545e6d18c4b7422f62a7a71360a184529d24d9'
ARTIFACT=11648431056
ARCHIVE_HASH='61a0e11e22abd679a58adbf2cd4de007c9e355bb95d5ffda192858c0c6a5b662'
BASELINE=APP+'--0000021'
CANDIDATE=APP+'--cc-37997906253-1'
PREFIX='acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:'
OLD_IMAGE=PREFIX+'cdc5fdd09cb4b5d3d7d9c6671e6f4e38082f31c6aec41713fa080cefe08745ce'
NEW_IMAGE=PREFIX+'997ab57ffa7a8215a7e93b4a2587c937ec301b29c72d01ab3effaf2789d57f21'


def require(value,code):
    if not value:raise RuntimeError('checkpoint_'+code)


def verify_run(run):
    require(run.get('id')==RUN and run.get('workflow_id')==315562561 and run.get('head_sha')==SHA
            and run.get('head_branch')=='main' and run.get('event')=='workflow_dispatch'
            and run.get('status')=='completed' and run.get('conclusion')=='failure','run_identity')


def verify_record(record):
    expected={'result':'FAILED','releaseCommit':SHA,'baselineRevision':BASELINE,'baselineImage':OLD_IMAGE,
              'baselineMode':'Single','failurePhase':'candidate_staging',
              'failureReason':'zero_traffic_policy:missing_zero_traffic_candidate','rollback':'FAILED',
              'productionMutation':False,'oracleMutation':False,'celarSowAcceptance':'PENDING_NOT_EXECUTED'}
    require(all(record.get(k)==v for k,v in expected.items()),'record_identity')
    require(record.get('baselineCanary',{}).get('result')=='PASS','baseline_qualification')


def verify_live(app,baseline):
    p=app['properties'];b=baseline['properties']
    require(app.get('name')==APP and p['latestRevisionName']==CANDIDATE and p['latestReadyRevisionName']==CANDIDATE,'candidate_identity')
    require(p['template']['containers'][0]['image']==NEW_IMAGE,'candidate_image')
    require(p['configuration']['activeRevisionsMode']=='Multiple','mode')
    weights=p['configuration']['ingress']['traffic']
    require(weights and sum(t.get('weight',0) for t in weights)==100
            and all(not t.get('latestRevision') and (t.get('weight',0)==0 or t.get('revisionName')==BASELINE) for t in weights),'baseline_pin')
    require(baseline.get('name')==BASELINE and b.get('active') is True and b.get('healthState')=='Healthy'
            and b.get('provisioningState')=='Provisioned' and b['template']['containers'][0]['image']==OLD_IMAGE,'healthy_baseline')


def clean_template(value):
    result=copy.deepcopy(value);result.pop('revisionSuffix',None);return result


def stable_configuration(app):
    result=copy.deepcopy(app['properties']['configuration'])
    result.pop('activeRevisionsMode',None);result['ingress'].pop('traffic',None);return result


def fetch_checkpoint():
    run=json.loads(subprocess.check_output(['gh','api',f'repos/{REPO}/actions/runs/{RUN}'],stderr=subprocess.PIPE))
    verify_run(run)
    raw=subprocess.check_output(['gh','api',f'repos/{REPO}/actions/artifacts/{ARTIFACT}/zip'],stderr=subprocess.PIPE)
    require(len(raw)<16384 and hashlib.sha256(raw).hexdigest()==ARCHIVE_HASH,'archive_integrity')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        require(len(archive.namelist())==1,'archive_shape')
        record=json.loads(archive.read(archive.namelist()[0]))
    verify_record(record)
    return record


def repair_known_checkpoint(controller,current,az,check):
    if current['properties'].get('latestRevisionName')!=CANDIDATE:return False
    fetch_checkpoint()
    baseline=controller.revision(BASELINE)
    verify_live(current,baseline)
    controller.summary['phase']='registered_checkpoint_recovery'
    # Already-active baseline is intentionally not activated again (Azure rejects that).
    controller.traffic(BASELINE)
    suffix='cr-'+controller.run_id+'-'+controller.attempt+'b'
    recovery=APP+'--'+suffix
    az('containerapp','revision','copy','-g',RG,'-n',APP,'--from-revision',BASELINE,'--revision-suffix',suffix)
    restored=controller.wait(recovery,OLD_IMAGE)
    require(clean_template(restored['properties']['template'])==clean_template(baseline['properties']['template']),'restored_revision_template')
    controller.traffic(recovery)
    if controller.revision(CANDIDATE)['properties'].get('active'):
        az('containerapp','revision','deactivate','-g',RG,'-n',APP,'--revision',CANDIDATE)
    az('containerapp','revision','set-mode','-g',RG,'-n',APP,'--mode','single')
    state=controller.app()
    require(state['properties']['configuration']['activeRevisionsMode']=='Single'
            and state['properties']['latestReadyRevisionName']==recovery
            and state['properties']['template']['containers'][0]['image']==OLD_IMAGE,'restored_app_identity')
    require(stable_configuration(state)==stable_configuration(current),'configuration_drift')
    qualified=check('https://phd-west-test.onenecklab.com')
    controller.summary['priorRunRecovery']={'result':'PASS','failedRun':RUN,'recoveryRevision':recovery,
                                          'templateVerification':'EXACT_REVISION_TEMPLATE','canary':qualified}
    controller.persist()
    return True
