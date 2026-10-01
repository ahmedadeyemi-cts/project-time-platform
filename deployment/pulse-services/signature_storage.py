"""Prepare one antivirus share/mount with an authorized owner connection.
No service deployment, application routing, account setting, role assignment,
network rule, business-file access, workflow dispatch or deletion is supported.
"""
import argparse, copy, json, os, re, subprocess, tempfile
from pathlib import Path
from test_resources import SUB, GROUP, ENV, STORAGE
ROOT=Path(__file__).resolve().parents[2]
REPOSITORY='ahmedadeyemi-cts/project-time-platform'
ACCOUNT_NAME='stphdtestfiles7825cc'
DATA_GROUP='rg-project-health-dashboard-test-data-westus3'
ACCOUNT=f'/subscriptions/{SUB}/resourceGroups/{DATA_GROUP}/providers/Microsoft.Storage/storageAccounts/{ACCOUNT_NAME}'
SHARE=ACCOUNT+'/fileServices/default/shares/'+STORAGE
MOUNT=ENV+'/storages/'+STORAGE
BUSINESS_MOUNT=ENV+'/storages/phd-shared-files'
CONFIRMATION='PREPARE PULSE TEST ANTIVIRUS STORAGE'

class PreparationError(Exception): pass

def require(value, code):
    if not value: raise PreparationError(code)

def validate_mount(value):
    require(isinstance(value,dict),'signature_mount_invalid')
    spec=value.get('properties',{}).get('azureFile',{})
    require(spec.get('accountName')==ACCOUNT_NAME and spec.get('shareName')==STORAGE
        and spec.get('accessMode')=='ReadWrite','signature_mount_identity_mismatch')
    return True

def run(args, missing=False):
    result=subprocess.run(args,cwd=ROOT,capture_output=True,text=True,timeout=60)
    if result.returncode:
        code=(result.stderr or '').lower()
        if missing and any(x in code for x in ('resourcenotfound','sharenotfound','(notfound)')):
            return None
        raise PreparationError('infrastructure_operation_failed')
    return json.loads(result.stdout) if result.stdout.strip() else {}

def arm(method, resource, body=None, missing=False):
    # Fixed operation inventory. The main application and business share are absent.
    allowed={('GET',ACCOUNT),('GET',SHARE),('PUT',SHARE),('POST',ACCOUNT+'/listKeys'),
             ('GET',MOUNT),('PUT',MOUNT),('GET',BUSINESS_MOUNT)}
    require((method,resource) in allowed,'infrastructure_operation_rejected')
    version='2024-01-01' if resource.startswith(ACCOUNT) else '2025-01-01'
    args=['az','rest','--method',method,'--url','https://management.azure.com'+resource+'?api-version='+version]
    path=None
    try:
        if body is not None:
            fd,path=tempfile.mkstemp(prefix='pulse-antivirus-storage-',suffix='.json')
            os.fchmod(fd,0o600)
            with os.fdopen(fd,'w') as output: json.dump(body,output)
            args+=['--body','@'+path]
        return run(args+['--only-show-errors','-o','json'],missing=missing)
    finally:
        if path is not None: Path(path).unlink(missing_ok=True)

def verified_owner_context(source, pr, confirmation):
    require(confirmation==CONFIRMATION and re.fullmatch('[0-9a-f]{40}',source or '')
        and type(pr) is int and pr>0,'review_confirmation_required')
    require(not os.environ.get('GITHUB_ACTIONS'),'owner_bootstrap_not_ci')
    account=run(['az','account','show','--only-show-errors','-o','json'])
    require(account.get('id')==SUB and account.get('state')=='Enabled'
        and account.get('user',{}).get('type')=='user','authorized_test_user_connection_required')
    head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    require(head==source,'exact_reviewed_source_required')
    require(subprocess.run(['git','diff','--quiet','HEAD','--','deployment/pulse-services/signature_storage.py','deployment/pulse-services/test_resources.py'],cwd=ROOT).returncode==0,
        'bootstrap_source_modified')
    review=run(['gh','api',f'repos/{REPOSITORY}/pulls/{pr}'])
    require(review.get('merged') is True and review.get('merge_commit_sha')==source
        and review.get('base',{}).get('ref')=='main'
        and review.get('head',{}).get('repo',{}).get('full_name')==REPOSITORY,'merged_review_required')
    main=run(['gh','api',f'repos/{REPOSITORY}/git/ref/heads/main'])
    require(main.get('object',{}).get('sha')==source,'current_main_required')

def validate_share(value):
    require(isinstance(value,dict),'signature_share_invalid')
    p=value.get('properties',{})
    require(value.get('id','').lower()==SHARE.lower() and p.get('shareQuota')==10
        and p.get('enabledProtocols')=='SMB' and p.get('metadata',{}).get('purpose')=='pulse_antivirus_signatures',
        'existing_share_not_owned')

def prepare(source, pr, confirmation):
    verified_owner_context(source,pr,confirmation)
    account=arm('GET',ACCOUNT)
    require(account.get('id','').lower()==ACCOUNT.lower() and account.get('location')=='westus3',
        'storage_account_identity_changed')
    properties=account.get('properties',{})
    require(properties.get('provisioningState')=='Succeeded'
        and properties.get('publicNetworkAccess')=='Disabled'
        and properties.get('allowSharedKeyAccess') is True,'private_storage_prerequisite_failed')
    business=arm('GET',BUSINESS_MOUNT)
    require(business.get('properties',{}).get('azureFile',{}).get('accountName')==ACCOUNT_NAME,
        'existing_storage_relationship_changed')
    existing=arm('GET',MOUNT,missing=True)
    if existing is not None: validate_mount(existing)
    share=arm('GET',SHARE,missing=True)
    created_share=share is None
    if share is None:
        arm('PUT',SHARE,{'properties':{'shareQuota':10,'enabledProtocols':'SMB',
            'metadata':{'purpose':'pulse_antivirus_signatures','reviewed_source':source}}})
        share=arm('GET',SHARE)
    validate_share(share)
    if existing is None:
        keys=arm('POST',ACCOUNT+'/listKeys').get('keys',[])
        candidates=[x.get('value') for x in keys if x.get('keyName')=='key1']
        require(len(candidates)==1 and isinstance(candidates[0],str) and len(candidates[0])>=32,'storage_credential_unavailable')
        body={'properties':{'azureFile':{'accountName':ACCOUNT_NAME,'accountKey':candidates[0],
            'shareName':STORAGE,'accessMode':'ReadWrite'}}}
        arm('PUT',MOUNT,body)
        # Never publish this body/key or use it to access business-file contents.
        body.clear();candidates.clear();keys.clear()
    validate_mount(arm('GET',MOUNT))
    require(arm('GET',BUSINESS_MOUNT)==business,'existing_business_mount_changed')
    return {'source':source,'pullRequest':pr,'status':'prepared','signatureShare':STORAGE,
        'shareCreated':created_share,'mountCreated':existing is None,'existingBusinessMountUnchanged':True,
        'newServerCreated':False,'rolesChanged':False,'applicationRoutingChanged':False,'secretValuesPublished':False}

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--source',required=True);parser.add_argument('--pull-request',type=int,required=True)
    parser.add_argument('--confirmation',required=True)
    args=parser.parse_args()
    try: print(json.dumps(prepare(args.source,args.pull_request,args.confirmation)))
    except Exception as error:
        code=str(error) if isinstance(error,PreparationError) and re.fullmatch('[a-z_]+',str(error)) else type(error).__name__
        print(json.dumps({'status':'failed','diagnostic':code,'secretValuesPublished':False}));raise SystemExit(1)
