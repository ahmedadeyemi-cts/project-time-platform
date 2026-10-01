"""Exact-source, Test-only service cutover. Never creates a server or changes an account."""
import copy,hashlib,json,os,re,secrets,subprocess,sys,tempfile,time,urllib.error,urllib.request
from pathlib import Path
from test_resources import *
from activation_contracts import acceptance_job_name, validate_document_runtime
from state_preservation import revision_is_ready, require_cleanup_ownership, require_new_service_names
REPO='ahmedadeyemi-cts/project-time-platform'
ORIGIN='https://phd-west-test.onenecklab.com'
PREFIXES=('PROJECTPULSE_DOCUMENT_SERVICE_','PROJECTPULSE_LAYA_SERVICE_')
VERSION='2025-01-01'
class CutoverError(Exception):pass

def check(value,code):
    if not value:raise CutoverError(code)
def run(args,json_output=True):
    p=subprocess.run(args,capture_output=True,text=True,timeout=1800)
    if p.returncode:raise CutoverError('command_failed_'+args[0].split('/')[-1])
    return json.loads(p.stdout) if json_output and p.stdout.strip() else p.stdout

def az(*args):return run(['az',*args,'--only-show-errors','-o','json'])
def gh(path):return run(['gh','api','repos/'+REPO+'/'+path])
def write_private(path,body):
    fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    with os.fdopen(fd,'w') as f:json.dump(body,f)

def rest(method,resource,body=None,version=VERSION):
    check(resource.startswith('/subscriptions/'+SUB+'/'),'subscription_scope')
    path=None
    try:
        args=['rest','--method',method,'--url','https://management.azure.com'+resource+'?api-version='+version]
        if body is not None:
            path=PRIVATE/('request-'+secrets.token_hex(8)+'.json');write_private(path,body);args+=['--body','@'+str(path)]
        return az(*args)
    finally:
        if path:path.unlink(missing_ok=True)

def get_app(name):
    check(name in {API,*APPS.values()},'application_scope')
    return az('containerapp','show','-g',GROUP,'-n',name)

def env_map(app):return {v['name']:v for v in app['properties']['template']['containers'][0].get('env',[])}
def source_matches(app):
    check(env_map(app).get('PROJECTPULSE_SOURCE_COMMIT',{}).get('value')==SHA,'installed_source_mismatch')
    check('@sha256:' in app['properties']['template']['containers'][0]['image'],'api_image_not_pinned')
    check(app['properties']['latestRevisionName']==app['properties']['latestReadyRevisionName'],'api_revision_not_ready')

def wait_app(name,expected_images=None,*,expected_revision):
    end=time.monotonic()+900
    while time.monotonic()<end:
        a=get_app(name);p=a['properties']
        if p.get('provisioningState')=='Failed':raise CutoverError('service_provisioning_failed')
        if revision_is_ready(a,name,expected_revision,expected_images):
            return a
        time.sleep(10)
    raise CutoverError('service_readiness_deadline')

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,*args,**kwargs):return None

def public_api(path,session='',payload=None):
    allowed={'/api/auth/local/login','/api/auth/session/logout','/api/security/context','/api/platform-operations/overview',
             '/api/celar-ai/v1/documents/runtime/readiness','/api/ai-configuration/decisions/laya/health','/health'}
    check(path in allowed,'api_path_rejected')
    headers={'Accept':'application/json','Origin':ORIGIN,'Sec-Fetch-Site':'same-origin','Cache-Control':'no-store'}
    if session:headers.update({'Authorization':'Bearer '+session,'X-ProjectPulse-Session':session,'X-ProjectPulse-Module-Number':'064'})
    data=None
    if payload is not None:data=json.dumps(payload).encode();headers['Content-Type']='application/json'
    req=urllib.request.Request(ORIGIN+path,data=data,headers=headers,method='GET' if data is None else 'POST')
    try:
        with urllib.request.build_opener(NoRedirect).open(req,timeout=45) as response:
            raw=response.read(2_000_001);check(len(raw)<=2_000_000,'api_response_budget')
            return response.status,json.loads(raw) if raw else {}
    except urllib.error.HTTPError as error:
        status=error.code;error.close();return status,{}
    except (urllib.error.URLError,TimeoutError):raise CutoverError('api_transport_failed') from None

def local_admin(after=False):
    user=os.environ.get('PROJECTPULSE_TEST_UAT_ADMIN_EMAIL','');password=os.environ.get('PROJECTPULSE_TEST_UAT_ADMIN_PASSWORD','')
    check(user and password,'protected_local_superadmin_credential_required')
    session=''
    try:
        status,login=public_api('/api/auth/local/login',payload={'username':user,'password':password})
        check(status==200 and login.get('provider')=='LOCAL' and login.get('mustChangePassword') is False,'local_superadmin_login_failed')
        session=login.get('sessionToken','');check(len(session)>=20,'admin_session_missing')
        status,context=public_api('/api/security/context',session)
        roles=sorted({str(x.get('roleCode','')).upper() for x in context.get('roles',[])})
        check(status==200 and not context.get('isViewAs') and context.get('userId'),'admin_context_invalid')
        check(bool(set(roles)&{'SUPER_ADMINISTRATOR','SUPERADMINISTRATOR','GLOBAL_ADMINISTRATOR','GLOBALADMINISTRATOR'}),'permanent_superadmin_role_required')
        identity=hashlib.sha256(json.dumps({'userId':context['userId'],'roles':roles},sort_keys=True).encode()).hexdigest()
        status,overview=public_api('/api/platform-operations/overview',session)
        check(status==200 and overview.get('runtime',{}).get('releaseSha')==SHA,'server_source_mismatch')
        if after:
            status,health=public_api('/api/ai-configuration/decisions/laya/health',session,{})
            check(status==200 and health.get('status')=='ready' and health.get('runtimeLocation')=='pulse_container','api_laya_route_not_ready')
            status,health=public_api('/api/celar-ai/v1/documents/runtime/readiness',session)
            check(status==200,'api_documents_route_not_ready')
            validate_document_runtime(health)
        return identity
    finally:
        if session:
            status,_=public_api('/api/auth/session/logout',session,{})
            check(status in (200,204),'admin_session_cleanup_failed')

def preflight():
    check(os.environ.get('GITHUB_REPOSITORY')==REPO and os.environ.get('GITHUB_REF')=='refs/heads/main','trusted_main_required')
    canonical=os.environ.get('PULSE_CANONICAL_RELEASE')=='true'
    if canonical:
        from canonical_release import validate_context
        validate_context(os.environ,SHA,RUN)
    else:
        check(os.environ.get('GITHUB_ACTOR')=='ahmedadeyemi-cts','owner_request_required')
    check(os.environ.get('GITHUB_RUN_ATTEMPT')=='1','new_explicit_run_required')
    check(re.fullmatch(r'[0-9a-f]{40}',SHA) is not None and RUN.isdigit(),'release_identity')
    check(os.environ.get('PULSE_CONFIRMATION')=='SWITCH PULSE TEST DOCUMENTS AND LAYA','confirmation_required')
    check(gh('git/ref/heads/main')['object']['sha']==SHA,'main_changed')
    pr=gh('pulls/1222');check(pr.get('merged') and re.fullmatch('[0-9a-f]{40}',pr.get('merge_commit_sha','')),'reviewed_pr_merge_required')
    run(['git','merge-base','--is-ancestor',pr['merge_commit_sha'],SHA],json_output=False)
    runs=gh('actions/runs?head_sha='+SHA+'&per_page=100')['workflow_runs']
    for path in ('.github/workflows/projectpulse-ci.yml','.github/workflows/security-posture-ci.yml'):
        matching=[x for x in runs if x['path']==path and x['event']=='push']
        check(matching and max(matching,key=lambda x:x['id'])['conclusion']=='success','merged_source_ci_required')
    if canonical:
        from canonical_release import validate_run
        validate_run(gh('actions/runs/'+RUN),gh('actions/runs/'+RUN+'/jobs?filter=latest&per_page=10'),SHA,RUN)
    else:
        deploy=gh('actions/runs/'+os.environ['PULSE_ACCEPTED_DEPLOYMENT_RUN'])
        check(deploy['head_sha']==SHA and deploy['path']=='.github/workflows/projectpulse-deploy-test.yml' and deploy['conclusion']=='success','protected_application_acceptance_required')
    check(az('account','show')['id']==SUB,'test_subscription_required')
    environment=rest('GET',ENV)
    check(environment['properties']['defaultDomain']==DOMAIN,'environment_domain_changed')
    check(environment['properties']['vnetConfiguration']['internal'] is True,'private_environment_required')
    check(any(x['name']=='Consumption' and x['workloadProfileType']=='Consumption' for x in environment['properties']['workloadProfiles']),'qualified_consumption_profile_required')
    app=get_app(API);source_matches(app)
    check(app['properties']['configuration']['activeRevisionsMode']=='Single','single_revision_required')
    for prefix in PREFIXES:check(env_map(app).get(prefix+'MODE',{}).get('value','legacy')=='legacy','initial_cutover_only')
    admin=local_admin()
    if (PRIVATE/'before.json').exists():
        previous=json.loads((PRIVATE/'before.json').read_text())
        check(previous['properties']['template']==app['properties']['template'],'api_changed_during_preparation')
        check(json.loads((PRIVATE/'preflight.json').read_text())['adminIdentity']==admin,'admin_changed_during_preparation')
    else:
        write_private(PRIVATE/'before.json',app)
        write_private(PRIVATE/'preflight.json',{'source':SHA,'adminIdentity':admin})
    print('PULSE_SERVICE_PREFLIGHT=PASS local_superadmin_login=true existing_environment=true')

def signatures():
    accounts=az('storage','account','list')
    matches=[x for x in accounts if x['name']=='stphdtestfiles7825cc'];check(len(matches)==1,'signature_account_not_found')
    account=matches[0];check(account['id'].lower().startswith('/subscriptions/'+SUB+'/'),'signature_subscription')
    share=account['id']+'/fileServices/default/shares/'+STORAGE
    rest('PUT',share,{'properties':{'shareQuota':10}},'2024-01-01')
    keys=az('storage','account','keys','list','-n',account['name'],'-g',account['resourceGroup'])
    key=keys[0]['value'];check(key,'signature_storage_credential_missing')
    rest('PUT',ENV+'/storages/'+STORAGE,{'properties':{'azureFile':{'accountName':account['name'],'accountKey':key,'shareName':STORAGE,'accessMode':'ReadWrite'}}})
    key='';keys=[]
    print('PULSE_SIGNATURE_STORAGE=READY existing_storage_account=true business_share_not_mounted=true')

def journal(record):
    path=PRIVATE/'resource-journal.jsonl'
    fd=os.open(path,os.O_WRONLY|os.O_APPEND|os.O_CREAT|os.O_NOFOLLOW,0o600)
    with os.fdopen(fd,'w') as f:f.write(json.dumps(record)+'\n')

def cleanup_staged():
    path=PRIVATE/'resource-journal.jsonl'
    if not path.exists():return
    # Only resources created by this exact workflow are eligible for deletion.
    for line in reversed(path.read_text().splitlines()):
        item=json.loads(line)
        if item.get('source')!=SHA or item.get('run')!=RUN:raise CutoverError('cleanup_identity_mismatch')
        if item['kind']=='application':
            check(item['name'] in APPS.values(),'cleanup_application_scope')
            current=get_app(item['name'])
            require_cleanup_ownership(current,SHA,RUN,application=True)
            rest('DELETE',ROOT+'/providers/Microsoft.App/containerApps/'+item['name'])
        elif item['kind']=='job':
            check(item['name']==acceptance_job_name(RUN),'cleanup_job_scope')
            job_resource=ROOT+'/providers/Microsoft.App/jobs/'+item['name']
            current=rest('GET',job_resource)
            require_cleanup_ownership(current,SHA,RUN,application=False)
            rest('DELETE',job_resource)
        else:raise CutoverError('cleanup_resource_scope')
    print('PULSE_STAGED_RESOURCE_CLEANUP=PASS existing_apps_untouched=true')

def prepare():
    preflight()
    images=json.loads(Path(os.environ['PULSE_IMAGE_MANIFEST']).read_text());validate_images(images)
    app_names={x['name'] for x in az('containerapp','list','-g',GROUP)}
    require_new_service_names(app_names,set(APPS.values()))
    signatures()
    credentials={};created=[]
    for kind,name in APPS.items():
        created.append(name)
        token=secrets.token_urlsafe(48);secret=('pulse-documents-' if kind=='documents' else 'pulse-laya-')+RUN
        credentials[kind]={'token':token,'secret':secret}
        body=application(kind,images,token,secret,SHA,RUN)
        rest('PUT',ROOT+'/providers/Microsoft.App/containerApps/'+name,body)
        if name in created:journal({'kind':'application','name':name,'run':RUN,'source':SHA})
        print('PULSE_SERVICE_STAGED='+kind)
    write_private(PRIVATE/'credentials.json',credentials);write_private(PRIVATE/'created.json',created)
    for kind,name in APPS.items():
        images_for_app=[images['documents'],images['scanner']] if kind=='documents' else [images['laya-gateway'],images['laya']]
        a=wait_app(name,images_for_app,expected_revision=name+'--svc-'+RUN)
        check(a['properties']['configuration'].get('ingress',{}).get('external') is False,'service_ingress_drift')
        check(runtime_identity_isolated(a['properties']['configuration']),'service_identity_drift')
        print('PULSE_SERVICE_READY='+kind)
    job=acceptance_job_name(RUN)
    job_env=[{'name':'PULSE_EXPECTED_SOURCE','value':SHA}]
    job_secrets=[]
    for kind,c in credentials.items():
        job_secrets.append({'name':c['secret'],'value':c['token']});job_env.append({'name':'PULSE_'+kind.upper()+'_TOKEN','secretRef':c['secret']})
    body={'location':'westus3','identity':{'type':'UserAssigned','userAssignedIdentities':{IDENTITY:{}}},
          'tags':{'environment':'test','managedBy':'pulse-services-reviewed-cutover','source':SHA,'deploymentRun':RUN},
          'properties':{'environmentId':ENV,'workloadProfileName':'Consumption',
            'configuration':{'triggerType':'Manual','replicaTimeout':900,'replicaRetryLimit':0,
                'manualTriggerConfig':{'parallelism':1,'replicaCompletionCount':1},
                'registries':[{'server':ACR,'identity':IDENTITY}], 'identitySettings':[{'identity':IDENTITY,'lifecycle':'None'}], 'secrets':job_secrets},
            'template':{'containers':[{'name':'verify','image':images['documents'],
                'command':['python3','/opt/pulse-services/acceptance.py'],'resources':{'cpu':0.5,'memory':'1Gi'},'env':job_env}]}}}
    rest('PUT',ROOT+'/providers/Microsoft.App/jobs/'+job,body)
    journal({'kind':'job','name':job,'run':RUN,'source':SHA})
    execution=az('containerapp','job','start','-g',GROUP,'-n',job);name=execution['name'];end=time.monotonic()+1000
    while time.monotonic()<end:
        e=az('containerapp','job','execution','show','-g',GROUP,'-n',job,'--job-execution-name',name)
        status=e['properties']['status']
        if status=='Succeeded':break
        if status in ('Failed','Stopped','Degraded'):raise CutoverError('private_service_acceptance_failed')
        time.sleep(10)
    else:raise CutoverError('private_service_acceptance_deadline')
    write_private(PRIVATE/'service-acceptance.json',{'source':SHA,'job':job,'execution':name,'status':'passed','images':images})
    print('PULSE_PRIVATE_SERVICE_JOB=PASS no_customer_documents=true')

def switch():
    before=json.loads((PRIVATE/'before.json').read_text());pre=json.loads((PRIVATE/'preflight.json').read_text())
    accepted=json.loads((PRIVATE/'service-acceptance.json').read_text());credentials=json.loads((PRIVATE/'credentials.json').read_text())
    check(accepted['status']=='passed' and accepted['source']==SHA,'service_acceptance_required')
    check(gh('git/ref/heads/main')['object']['sha']==SHA,'main_changed')
    current=get_app(API);source_matches(current)
    check(current['properties']['template']==before['properties']['template'],'api_changed_before_cutover')
    from secret_preservation import existing_payload,merged_payload,unchanged_existing
    resource=ROOT+'/providers/Microsoft.App/containerApps/'+API
    metadata=current['properties']['configuration'].get('secrets',[])
    retrieved=rest('POST',resource+'/listSecrets')
    preserved=existing_payload(metadata,retrieved)
    additions=[{'name':c['secret'],'value':c['token']} for c in credentials.values()]
    merged=merged_payload(metadata,retrieved,additions)
    rest('PATCH',resource,{'properties':{'configuration':{'secrets':merged}}})
    readback=get_app(API)
    unchanged_existing(preserved,readback['properties']['configuration'].get('secrets',[]),
        rest('POST',resource+'/listSecrets'))
    check(readback['properties']['template']==current['properties']['template'],'api_changed_during_credential_update')
    del retrieved,preserved,merged
    template=copy.deepcopy(before['properties']['template']);env=template['containers'][0].get('env',[])
    env=[v for v in env if not v['name'].startswith(PREFIXES)]
    for kind,c in credentials.items():env.extend(configuration(kind,c['token'],c['secret'],'PR-1222-'+SHA))
    template['containers'][0]['env']=env;template['revisionSuffix']='psvc-'+RUN
    write_private(PRIVATE/'switch-started.json',{'source':SHA})
    rest('PATCH',ROOT+'/providers/Microsoft.App/containerApps/'+API,{'properties':{'template':template}})
    after=wait_app(API,expected_revision=API+'--psvc-'+RUN);source_matches(after)
    check(after['properties']['template']['containers'][0]['image']==before['properties']['template']['containers'][0]['image'],'api_image_changed')
    check(local_admin(after=True)==pre['adminIdentity'],'local_superadmin_identity_changed')
    status,health=public_api('/health');check(status==200 and health.get('status')=='healthy','public_health_failed')
    receipt={'status':'passed','sourceSha':SHA,'apiRevision':after['properties']['latestReadyRevisionName'],
        'services':{k:{'application':n,'runtime':'pulse_container'} for k,n in APPS.items()},'localSuperAdministratorLoginPassed':True,
        'localSuperAdministratorIdentityUnchanged':True,'serviceAcceptance':accepted,'newVmCreated':False,
        'productionMutation':False,'oracleHostMutation':False,'originalSecurityFindingsClosed':0}
    SAFE.mkdir(exist_ok=True,parents=True);(SAFE/'cutover.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print('PULSE_SERVICE_CUTOVER=PASS scanner_ocr_laya=pulse_container local_superadmin_preserved=true')

def rollback():
    if not (PRIVATE/'switch-started.json').exists():
        cleanup_staged()
        return
    before=json.loads((PRIVATE/'before.json').read_text());current=get_app(API)
    check(env_map(current).get('PROJECTPULSE_SOURCE_COMMIT',{}).get('value')==SHA,'rollback_source_changed')
    check(current['properties']['template']['containers'][0]['image']==before['properties']['template']['containers'][0]['image'],'rollback_image_changed')
    old={v['name']:v for v in before['properties']['template']['containers'][0].get('env',[]) if v['name'].startswith(PREFIXES)}
    template=copy.deepcopy(current['properties']['template'])
    template['containers'][0]['env']=[v for v in template['containers'][0].get('env',[]) if not v['name'].startswith(PREFIXES)]+list(old.values())
    template['revisionSuffix']='psvcr-'+RUN
    rest('PATCH',ROOT+'/providers/Microsoft.App/containerApps/'+API,{'properties':{'template':template}})
    wait_app(API,expected_revision=API+'--psvcr-'+RUN);check(local_admin()==json.loads((PRIVATE/'preflight.json').read_text())['adminIdentity'],'rollback_admin_check_failed')
    cleanup_staged()
    print('PULSE_SERVICE_ROLLBACK=PASS verified_legacy_route_restored=true')

if __name__=='__main__':
    SHA=os.environ.get('PULSE_SOURCE_SHA','');RUN=os.environ.get('GITHUB_RUN_ID','')
    PRIVATE=Path(os.environ['PULSE_PRIVATE_EVIDENCE']);PRIVATE.mkdir(mode=0o700,parents=True,exist_ok=True);PRIVATE.chmod(0o700)
    SAFE=Path(os.environ['PULSE_SAFE_EVIDENCE'])
    try:
        phase=sys.argv[1];check(phase in ('preflight','prepare','switch','rollback'),'invalid_phase')
        globals()[phase]()
    except Exception as e:
        code=str(e) if isinstance(e,(CutoverError,ValueError)) and re.fullmatch('[a-z_]{1,100}',str(e)) else type(e).__name__
        print('PULSE_SERVICE_ACTION=FAILED diagnostic='+code)
        raise SystemExit(1)
