"""Protected Test API deployment with pinned traffic, canary, recovery drill and promotion.
No Oracle operations, database migrations, secrets updates, or Production targets.
"""
import copy
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import tarfile
import tempfile
import time
from core_test_canary import check
from private_core_canary import check_revision
from failed_core_checkpoint import repair_known_checkpoint

SUB='cd32baeb-7b71-4bc0-8ea3-9f23a50903fe'
RG='rg-project-health-dashboard-test-app-westus3'
APP='ca-phd-test-api-westus3'
ACR='acrphdtest7825cc'
REPO='ahmedadeyemi-cts/project-time-platform'
ORIGIN='https://phd-west-test.onenecklab.com'


def run(*args, json_result=False):
    result=subprocess.run(args,check=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    return json.loads(result.stdout) if json_result else result.stdout.strip()


def az(*args, json_result=False):
    return run('az',*args,'--only-show-errors','-o','json' if json_result else 'none',json_result=json_result)


def bind_build_source(context, sha):
    if not re.fullmatch('[a-f0-9]{40}',sha):raise RuntimeError('build_source_invalid')
    recipe=context/'deployment/containers/api/Dockerfile'
    text=recipe.read_text()
    anchor='/p:UseAppHost=false'
    if text.count(anchor)!=1:raise RuntimeError('build_recipe_source_binding_denied')
    # Bind the existing assembly metadata property in the disposable build recipe.
    # The committed recipe, source files, and release protections stay byte-identical.
    text=text.replace(anchor,anchor+' /p:ProjectPulseSourceRevision='+sha,1)
    runtime='FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime'
    assert text.count(runtime)==1
    text=text.replace(runtime,'COPY scripts/resilience/CoreCanary/ scripts/resilience/CoreCanary/\nRUN dotnet publish scripts/resilience/CoreCanary/Pulse.CoreCanary.csproj --configuration Release --output /app/core-canary /p:UseAppHost=false\n\n'+runtime,1)
    copy='COPY --from=build /app/publish/ ./'
    assert text.count(copy)==1
    recipe.write_text(text.replace(copy,copy+'\nCOPY --from=build /app/core-canary/ /app/core-canary/',1))


def policy(name):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).with_name(name+'.py'))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module


def template(value):
    result=copy.deepcopy(value);result.pop('revisionSuffix',None)
    return result


class Controller:
    def __init__(self, evidence):
        self.evidence=evidence;self.before=None;self.baseline=None;self.mutated=False
        self.finished=False;self.candidate=None;self.recovery=None
        self.summary={'result':'BLOCKED','productionMutation':False,'oracleMutation':False,
                      'celarSowAcceptance':'PENDING_NOT_EXECUTED','rollback':'NOT_EXECUTED','phase':'admission'}

    def persist(self):
        # Only publish fixed safe fields; snapshots contain private env and stay runner-local.
        self.evidence.write_text(json.dumps(self.summary,indent=2)+'\n')

    def app(self):
        return az('containerapp','show','-g',RG,'-n',APP,json_result=True)

    def revision(self,name):
        return az('containerapp','revision','show','-g',RG,'-n',APP,'--revision',name,json_result=True)

    def traffic(self,name,zero=None):
        args=['containerapp','ingress','traffic','set','-g',RG,'-n',APP,'--revision-weight',name+'=100']
        if zero:args.append(zero+'=0')
        az(*args)
        traffic=self.app()['properties']['configuration']['ingress']['traffic']
        if sum(t.get('weight',0) for t in traffic)!=100 or any(t.get('latestRevision') or (t.get('weight',0)>0 and t.get('revisionName')!=name) for t in traffic):
            raise RuntimeError('named_traffic_pin_failed')

    def wait(self,name,image):
        for attempt in range(60):
            rev=self.revision(name);p=rev['properties']
            if p.get('provisioningState')=='Failed' or p.get('healthState')=='Unhealthy':
                raise RuntimeError('revision_unhealthy')
            if p.get('active') and p.get('healthState')=='Healthy' and p.get('provisioningState')=='Provisioned':
                if p['template']['containers'][0]['image']!=image:
                    raise RuntimeError('revision_image_digest_mismatch')
                return rev
            time.sleep(10)
        raise RuntimeError('revision_readiness_timeout')

    def recover(self):
        self.summary['phase']='recovery'
        # Pin baseline BEFORE creating a recovery revision; Single mode always selects latest.
        az('containerapp','revision','set-mode','-g',RG,'-n',APP,'--mode','multiple')
        if not self.revision(self.old)['properties'].get('active'):
            az('containerapp','revision','activate','-g',RG,'-n',APP,'--revision',self.old)
        self.traffic(self.old)
        suffix='cr-'+self.run_id+'-'+self.attempt
        self.recovery=APP+'--'+suffix
        az('containerapp','revision','copy','-g',RG,'-n',APP,'--from-revision',self.old,'--revision-suffix',suffix)
        rev=self.wait(self.recovery,self.old_image)
        if template(rev['properties']['template'])!=template(self.baseline['properties']['template']):
            raise RuntimeError('recovery_template_mismatch')
        self.traffic(self.recovery)
        if self.candidate:
            revisions=az('containerapp','revision','list','-g',RG,'-n',APP,json_result=True)
            selected=[r for r in revisions if r['name']==self.candidate]
            if len(selected)>1:raise RuntimeError('ambiguous_candidate_identity')
            if selected and selected[0]['properties'].get('active'):
                az('containerapp','revision','deactivate','-g',RG,'-n',APP,'--revision',self.candidate)
        if self.old_mode=='Single':
            az('containerapp','revision','set-mode','-g',RG,'-n',APP,'--mode','single')
        state=self.app()
        if state['properties']['configuration']['activeRevisionsMode']!=self.old_mode:
            raise RuntimeError('recovery_mode_mismatch')
        if template(state['properties']['template'])!=template(self.before['properties']['template']):
            raise RuntimeError('recovery_current_template_mismatch')
        self.summary['rollbackCanary']=check(ORIGIN)
        self.summary.update(rollback='PASS',recoveryRevision=self.recovery)
        self.persist()

    def admit(self):
        sha=os.environ['RELEASE_SHA']
        if not re.fullmatch('[a-f0-9]{40}',sha) or sha!=os.environ['EXPECTED_MAIN_SHA'] or sha!=os.environ['GITHUB_SHA']:
            raise RuntimeError('exact_main_identity_denied')
        if os.environ['GITHUB_REPOSITORY']!=REPO or os.environ['GITHUB_REF']!='refs/heads/main' or os.environ['GITHUB_EVENT_NAME']!='workflow_dispatch':
            raise RuntimeError('trusted_trigger_denied')
        if os.environ['GITHUB_WORKFLOW_REF']!=REPO+'/.github/workflows/projectpulse-deploy-test.yml@refs/heads/main':
            raise RuntimeError('registered_controller_denied')
        for key,value in [('AZURE_SUBSCRIPTION_ID',SUB),('AZURE_RESOURCE_GROUP',RG),('AZURE_API_APP',APP),('AZURE_ACR_NAME',ACR)]:
            if os.environ[key]!=value:raise RuntimeError('test_target_denied')
        if az('account','show',json_result=True)['id']!=SUB:raise RuntimeError('test_subscription_denied')
        if run('git','rev-parse','HEAD')!=sha or run('git','status','--porcelain'):
            raise RuntimeError('checkout_identity_or_cleanliness_denied')
        self.run_id=os.environ['GITHUB_RUN_ID'];self.attempt=os.environ['GITHUB_RUN_ATTEMPT']
        if not re.fullmatch('[0-9]{1,20}',self.run_id) or not re.fullmatch('[0-9]{1,3}',self.attempt):
            raise RuntimeError('run_identity_denied')
        current=self.app()
        repair_known_checkpoint(self,current,az,check)
        self.before=self.app()
        failures=policy('verify-core-test-revision-snapshot').verify(self.before)
        if failures:raise RuntimeError('baseline_denied:'+','.join(failures))
        self.old=self.before['properties']['latestReadyRevisionName']
        self.old_mode=self.before['properties']['configuration']['activeRevisionsMode']
        self.baseline=self.revision(self.old)
        self.old_image=self.baseline['properties']['template']['containers'][0]['image']
        # App and revision APIs serialize default fields and secretRef values differently.
        # Bind the current template by Azure's latest/ready revision identity, then compare
        # app-to-app and revision-to-revision throughout drift and recovery validation.
        if self.before['properties']['latestRevisionName']!=self.old or self.before['properties']['template']['containers'][0]['image']!=self.old_image:
            raise RuntimeError('baseline_not_current_template')
        if not self.baseline['properties'].get('active') or self.baseline['properties'].get('healthState')!='Healthy' or self.baseline['properties'].get('provisioningState')!='Provisioned':
            raise RuntimeError('baseline_revision_not_healthy')
        if self.old_mode not in ('Single','Multiple'):raise RuntimeError('baseline_mode_unsupported')
        for t in self.before['properties']['configuration']['ingress']['traffic']:
            if t.get('weight',0)>0 and not (t.get('revisionName')==self.old or t.get('latestRevision') and self.before['properties']['latestRevisionName']==self.old):
                raise RuntimeError('split_baseline_not_supported')
        self.summary.update(releaseCommit=sha,baselineRevision=self.old,baselineImage=self.old_image,baselineMode=self.old_mode)
        self.summary['baselineCanary']=check(ORIGIN)
        self.persist()
        return sha

    def execute(self):
        sha=self.admit()
        self.summary['phase']='immutable_image_build'
        tag='core-'+sha[:12]+'-'+self.run_id+'-'+self.attempt
        with tempfile.TemporaryDirectory() as temp:
            archive=Path(temp)/'source.tar'
            run('git','archive','--format=tar','--output='+str(archive),sha)
            context=Path(temp)/'source';context.mkdir()
            with tarfile.open(archive) as handle:handle.extractall(context,filter='data')
            bind_build_source(context,sha)
            az('acr','build','--registry',ACR,'--image','project-health-dashboard-api:'+tag,
               '--file',str(context/'deployment/containers/api/Dockerfile'),'--timeout','3600',str(context))
        digest=az('acr','repository','show','-n',ACR,'--image','project-health-dashboard-api:'+tag,json_result=True)['digest']
        if not re.fullmatch('sha256:[0-9a-f]{64}',digest):raise RuntimeError('immutable_image_denied')
        image=ACR+'.azurecr.io/project-health-dashboard-api@'+digest
        current=self.app()
        live_main=run('gh','api','repos/'+REPO+'/git/ref/heads/main','--jq','.object.sha')
        if live_main!=sha:raise RuntimeError('main_drift_during_build')
        if current!=self.before:
            # Azure timestamps are not part of admission; compare configuration/template/readiness.
            for field in ('configuration','template','latestReadyRevisionName','latestRevisionName'):
                if current['properties'].get(field)!=self.before['properties'].get(field):raise RuntimeError('baseline_drift_during_build')
        self.summary['phase']='candidate_staging'
        self.mutated=True
        if self.old_mode=='Single':az('containerapp','revision','set-mode','-g',RG,'-n',APP,'--mode','multiple')
        self.traffic(self.old) # CRITICAL: remove latestRevision before creating candidate.
        suffix='cc-'+self.run_id+'-'+self.attempt
        self.candidate=APP+'--'+suffix
        az('containerapp','revision','copy','-g',RG,'-n',APP,'--from-revision',self.old,'--image',image,
           '--set-env-vars','PROJECTPULSE_SOURCE_COMMIT='+sha,'--revision-suffix',suffix)
        self.traffic(self.old,self.candidate)
        rev=self.wait(self.candidate,image)
        failures=policy('verify-core-candidate-traffic').verify(self.before,self.app(),self.candidate)
        if failures:raise RuntimeError('zero_traffic_policy:'+','.join(failures))
        fqdn=rev['properties']['fqdn']
        domain=self.before['properties']['configuration']['ingress']['fqdn'].split('.',1)[1]
        if fqdn!=self.candidate+'.'+domain:raise RuntimeError('candidate_fqdn_unverified')
        self.summary.update(candidateRevision=self.candidate,candidateImage=image,effectiveCandidateTraffic=0)
        self.summary['phase']='candidate_canary'
        self.summary['candidateCanary']=check_revision(self.candidate,rev['properties']['template']['containers'][0]['name'],sha)
        # Prove recovery while candidate remains zero traffic; do not call this a failure drill.
        self.recover()
        self.summary['rollbackValidation']='LIVE_TEMPLATE_RESTORE_AND_AUTHENTICATED_CORE_READS'
        # Recovery created the latest revision. Restore candidate template as a new revision,
        # then run its canary again before promotion. This also permits safe Single-mode restore.
        az('containerapp','revision','set-mode','-g',RG,'-n',APP,'--mode','multiple')
        self.traffic(self.recovery)
        self.summary['phase']='promotion_staging'
        suffix='cp-'+self.run_id+'-'+self.attempt
        promotion=APP+'--'+suffix
        self.candidate=promotion
        az('containerapp','revision','copy','-g',RG,'-n',APP,'--from-revision',self.old,'--image',image,
           '--set-env-vars','PROJECTPULSE_SOURCE_COMMIT='+sha,'--revision-suffix',suffix)
        self.traffic(self.recovery,promotion)
        rev=self.wait(promotion,image)
        self.summary['promotionCanary']=check_revision(promotion,rev['properties']['template']['containers'][0]['name'],sha)
        self.traffic(promotion)
        if self.old_mode=='Single':az('containerapp','revision','set-mode','-g',RG,'-n',APP,'--mode','single')
        state=self.app()
        if state['properties']['latestReadyRevisionName']!=promotion or state['properties']['template']['containers'][0]['image']!=image:
            raise RuntimeError('installed_revision_identity_mismatch')
        self.summary['phase']='installed_canary'
        self.summary['installedCanary']=check(ORIGIN,sha)
        self.summary.update(result='PASS',phase='complete',deployedRevision=promotion,deployedImage=image)
        self.persist();self.finished=True


def main():
    evidence=Path(os.environ['CORE_EVIDENCE_FILE']);evidence.parent.mkdir(parents=True,exist_ok=True)
    controller=Controller(evidence)
    def interrupted(signum,frame):raise RuntimeError('controller_interrupted_'+str(signum))
    for signum in (signal.SIGINT,signal.SIGTERM):signal.signal(signum,interrupted)
    try:controller.execute()
    except Exception as exc:
        # Do not print subprocess output or exception payloads containing env or auth material.
        controller.summary['failureType']=type(exc).__name__
        controller.summary['failurePhase']=controller.summary['phase']
        if isinstance(exc,RuntimeError) and re.fullmatch(r'[a-z0-9_]+(?::[a-z_,]+| /(?:api|health)/[a-z0-9_/?=&-]+)?',str(exc)):
            controller.summary['failureReason']=str(exc) # Fixed controller codes/route paths only.
        if isinstance(exc,subprocess.CalledProcessError):
            controller.summary['failureCommand']=' '.join(exc.cmd[:3]) # CLI and verb only; never env/output.
        controller.summary['result']='FAILED'
        if controller.mutated and not controller.finished:
            try:
                # Recovery suffix must be unique even after the successful validation drill.
                controller.attempt=controller.attempt+'e'
                controller.recover()
            except Exception as recovery:
                controller.summary['rollback']='FAILED';controller.summary['recoveryFailureType']=type(recovery).__name__
        controller.persist()
        print('CORE_PROTECTED_TEST_RESULT=FAILED; see redacted evidence')
        return 1
    print('CORE_PROTECTED_TEST_RESULT=PASS')
    print('CELAR_SOW_ACCEPTANCE=PENDING_NOT_EXECUTED')
    return 0

if __name__=='__main__':raise SystemExit(main())
