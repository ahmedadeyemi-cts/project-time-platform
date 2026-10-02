"""Exercise the actual Test resource and rollback code without any cloud writes."""
import copy,importlib.util,json,os,re,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
ROOT=Path(__file__).resolve().parents[2];D=ROOT/'deployment/pulse-services'
sys.path.insert(0,str(D))
import test_resources as spec
import cutover
import yaml
class Resources(unittest.TestCase):
    def setUp(self):
        self.images={n:spec.ACR+'/pulse-services-'+n+'@sha256:'+'a'*64 for n in spec.COMPONENTS}
        self.token='q'*64
    def body(self,kind='documents'):
        return spec.application(kind,self.images,self.token,'test-documents-token','b'*40,'123456')
    def test_existing_environment_private_rootless_services(self):
        for kind in spec.APPS:
            b=self.body(kind);spec.validate_resource(b,kind,self.images)
            self.assertEqual(b['properties']['environmentId'],spec.ENV)
            self.assertNotIn('initContainers',b['properties']['template'])
    def test_reject_public_or_cross_environment_or_identity_access(self):
        for field in ('public','environment','identity','ports','scale','image','storage','credential'):
            b=self.body();p=b['properties'];c=p['configuration'];t=p['template']
            if field=='public':c['ingress']['external']=True
            if field=='environment':p['environmentId']='other'
            if field=='identity':c['identitySettings'][0]['lifecycle']='All'
            if field=='ports':c['ingress']['additionalPortMappings']=[{'external':True,'targetPort':3310}]
            if field=='scale':t['scale']['maxReplicas']=100
            if field=='image':t['containers'][0]['image']='untrusted:latest'
            if field=='storage':t['volumes'][-1]['storageName']='phd-shared-files'
            if field=='credential':t['containers'][1]['env']=[{'name':'PULSE_SERVICE_TOKEN','secretRef':'not-allowed'}]
            with self.subTest(field=field),self.assertRaises(ValueError):spec.validate_resource(b,'documents',self.images)
    def test_identity_casing_does_not_remove_runtime_restriction(self):
        b=self.body();c=b['properties']['configuration'];c['identitySettings'][0]['identity']=spec.IDENTITY.lower()
        self.assertTrue(spec.runtime_identity_isolated(c))
        c['identitySettings'].append({'identity':'system','lifecycle':'Main'})
        self.assertFalse(spec.runtime_identity_isolated(c))
    def test_images_cannot_be_tags_or_external_registry(self):
        for bad in ('latest','a'*64,'sha256:abc'):
            images=self.images.copy();images['laya']='other.example/laya@'+bad
            with self.assertRaises(ValueError):spec.validate_images(images)
    def test_configuration_hash_and_no_inline_token(self):
        for kind,prefix in [('documents','PROJECTPULSE_DOCUMENT_SERVICE_'),('laya','PROJECTPULSE_LAYA_SERVICE_')]:
            values=spec.configuration(kind,self.token,'synthetic-token','PR-1222-'+'b'*40)
            self.assertNotIn(self.token,json.dumps(values))
            lookup={x['name']:x for x in values}
            self.assertEqual(lookup[prefix+'TOKEN']['secretRef'],'synthetic-token')
            self.assertRegex(lookup[prefix+'CONFIGURATION_SHA256']['value'],r'^[0-9a-f]{64}$')
    def test_signature_mount_uses_supported_nonroot_permissions(self):
        body=self.body()
        volume=next(v for v in body['properties']['template']['volumes'] if v['name']=='signatures')
        self.assertEqual(volume['mountOptions'],'uid=65534,gid=65534,dir_mode=0750,file_mode=0640')
        self.assertEqual(volume['storageName'],spec.STORAGE)
        self.assertEqual(volume['storageType'],'AzureFile')
    def test_signature_mount_permissions_cannot_be_relaxed_or_extended(self):
        for options in ('uid=0,gid=0,dir_mode=0777,file_mode=0777',
            'uid=65534,gid=65534,dir_mode=0750,file_mode=0640,nosuid',
            'uid=65534,gid=65534,dir_mode=0750,file_mode=0640,nodev',
            'uid=65534,gid=65534,dir_mode=0750,file_mode=0640,noexec',''):
            body=self.body();volume=next(v for v in body['properties']['template']['volumes'] if v['name']=='signatures')
            volume['mountOptions']=options
            with self.subTest(options=options),self.assertRaises(ValueError):spec.validate_resource(body,'documents',self.images)
    def test_signature_mount_is_unique_and_required(self):
        for mode in ('missing','duplicate','wrong_type'):
            body=self.body();volumes=body['properties']['template']['volumes'];entry=next(v for v in volumes if v['name']=='signatures')
            if mode=='missing':volumes.remove(entry)
            elif mode=='duplicate':volumes.append(copy.deepcopy(entry))
            else:entry['storageType']='EmptyDir'
            with self.subTest(mode=mode),self.assertRaises(ValueError):spec.validate_resource(body,'documents',self.images)
    def test_mount_compatibility_keeps_mandatory_process_controls(self):
        source=(D/'sandbox.c').read_text()
        for control in ('PR_SET_NO_NEW_PRIVS','LANDLOCK_ACCESS_FS_EXECUTE','LANDLOCK_ACCESS_FS_MAKE_CHAR','LANDLOCK_ACCESS_FS_MAKE_BLOCK','SYS_landlock_restrict_self','geteuid()==0'):
            self.assertIn(control,source)
        self.assertNotIn('getenv(',source)
        self.assertIn('if(updater && path_rule(fd,"/var/lib/clamav",writes))',source)
        self.assertNotIn('path_rule(fd,"/var/lib/clamav",LANDLOCK_ACCESS_FS_EXECUTE)',source)

    def test_scanner_reload_protocol_and_nonconcurrent_memory(self):
        s=(D/'scanner_start.py').read_text()
        self.assertIn("c.sendall(b'zRELOAD'+bytes((0,)))",s)
        self.assertIn('ConcurrentDatabaseReload no',(D/'clamd.conf').read_text())
        self.assertIn("nativeReloadVerified",s)
    def test_manual_workflow_retains_protections(self):
        a=yaml.safe_load((D/'proposed-test-cutover.yml').read_text())
        triggers=a.get('on',a.get(True));self.assertEqual(set(triggers),{'workflow_dispatch'})
        self.assertEqual(a['concurrency'],{'group':'projectpulse-deploy-test','queue':'max','cancel-in-progress':False})
        self.assertEqual(a['permissions'],{'contents':'read','actions':'read','id-token':'write'})
        job=a['jobs']['cutover'];self.assertEqual(job['environment'],'test')
        self.assertIn("refs/heads/main",job['if']);self.assertIn('ahmedadeyemi-cts',job['if'])
        steps=job['steps'];names=[x.get('name','') for x in steps]
        self.assertLess(names.index('Verify accepted application, preserved local administrator and existing environment'),names.index('Build exact reviewed service images'))
        self.assertLess(names.index('Require image scan for laya'),names.index('Publish scanned immutable images to existing private registry'))
        self.assertLess(names.index('Stage private containers, current signatures and native acceptance'),names.index('Switch only verified Test service routes and verify local Super Administrator'))
        self.assertEqual(next(x for x in steps if x.get('name')=='Restore verified legacy routes on failed cutover')['if'],'failure()')
        for x in steps:
            if 'uses' in x:self.assertRegex(x['uses'],r'@[0-9a-f]{40}$')
            if x.get('uses','').startswith('actions/upload-artifact'):
                self.assertEqual(x['with']['path'],'${{ runner.temp }}/pulse-services-safe')
class CutoverLogic(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        cutover.PRIVATE=Path(self.tmp.name);cutover.SAFE=Path(self.tmp.name)/'safe';cutover.SHA='b'*40;cutover.RUN='123456'
        self.env=[{'name':'PROJECTPULSE_SOURCE_COMMIT','value':cutover.SHA},{'name':'EXISTING_SETTING','value':'preserve'}]
        self.before={'properties':{'template':{'containers':[{'name':'api','image':'private@sha256:'+'a'*64,'env':self.env}]},'configuration':{'activeRevisionsMode':'Single'},'latestRevisionName':'prior','latestReadyRevisionName':'prior'}}
    def test_preflight_refuses_wrong_caller_before_cloud_calls(self):
        with patch.dict(os.environ,{'GITHUB_REPOSITORY':cutover.REPO,'GITHUB_REF':'refs/heads/other'},clear=True),patch.object(cutover,'gh') as gh,patch.object(cutover,'az') as az:
            with self.assertRaises(cutover.CutoverError):cutover.preflight()
            gh.assert_not_called();az.assert_not_called()
    def test_local_admin_missing_credentials_fails_without_reset(self):
        with patch.dict(os.environ,{},clear=True),patch.object(cutover,'public_api') as api:
            with self.assertRaises(cutover.CutoverError):cutover.local_admin()
            api.assert_not_called()
    def test_unready_revision_can_rollback_without_other_changes(self):
        cutover.write_private(cutover.PRIVATE/'before.json',self.before)
        cutover.write_private(cutover.PRIVATE/'preflight.json',{'adminIdentity':'same'})
        cutover.write_private(cutover.PRIVATE/'switch-started.json',{'source':cutover.SHA})
        broken=copy.deepcopy(self.before);broken['properties']['latestRevisionName']='broken'
        broken['properties']['template']['containers'][0]['env']+=spec.configuration('documents','q'*64,'synthetic-token','PR-1222-'+cutover.SHA)
        calls=[]
        with patch.object(cutover,'wait_app_operation_settled',return_value=broken),patch.object(cutover,'wait_app',return_value=self.before),patch.object(cutover,'local_admin',return_value='same'),patch.object(cutover,'rest_stage',side_effect=lambda *x:calls.append(x[1:])),patch.object(cutover,'cleanup_staged'):
            cutover.rollback()
        self.assertEqual(len(calls),1);self.assertEqual(calls[0][0],'PATCH')
        body=calls[0][2];self.assertEqual(body['properties']['template']['containers'][0]['env'],self.env)
        self.assertNotIn('configuration',body['properties']);self.assertNotIn('secrets',str(body))
    def test_cutover_requires_private_native_acceptance(self):
        cutover.write_private(cutover.PRIVATE/'before.json',self.before)
        cutover.write_private(cutover.PRIVATE/'preflight.json',{'adminIdentity':'same'})
        cutover.write_private(cutover.PRIVATE/'credentials.json',{})
        cutover.write_private(cutover.PRIVATE/'service-acceptance.json',{'source':cutover.SHA,'status':'failed'})
        with patch.object(cutover,'rest') as remote:
            with self.assertRaises(cutover.CutoverError):cutover.switch()
            remote.assert_not_called()
    def test_cleanup_never_deletes_unowned_or_other_app(self):
        for name in (spec.API,'some-other-service'):
            p=cutover.PRIVATE/'resource-journal.jsonl';p.unlink(missing_ok=True)
            cutover.journal({'kind':'application','name':name,'run':cutover.RUN,'source':cutover.SHA})
            with patch.object(cutover,'rest') as remote:
                with self.assertRaises(cutover.CutoverError):cutover.cleanup_staged()
                remote.assert_not_called()
    def test_acceptance_job_azure_failures_have_finite_operation_codes(self):
        with patch.object(cutover,'az',side_effect=cutover.CutoverError('command_failed_az')):
            with self.assertRaisesRegex(cutover.CutoverError,'acceptance_job_start_failed'):
                cutover.az_stage('acceptance_job_start_failed','containerapp','job','start')
            with self.assertRaisesRegex(cutover.CutoverError,'acceptance_job_execution_read_failed'):
                cutover.az_stage('acceptance_job_execution_read_failed','containerapp','job','execution','show')
    def test_cutover_rest_failures_have_finite_operation_codes(self):
        with patch.object(cutover,'rest',side_effect=cutover.CutoverError('command_failed_az')):
            with self.assertRaisesRegex(cutover.CutoverError,'api_secret_update_failed'):
                cutover.rest_stage('api_secret_update_failed','PATCH','/subscriptions/'+cutover.SUB+'/fixture',{})
    def test_operation_wait_serializes_in_progress_before_success(self):
        states=[{'properties':{'provisioningState':'InProgress'}},{'properties':{'provisioningState':'Succeeded'}}]
        with patch.object(cutover,'get_app',side_effect=states) as read,patch.object(cutover.time,'sleep') as sleep:
            result=cutover.wait_app_operation_settled(cutover.API,read_code='fixture_read',deadline_code='fixture_deadline')
        self.assertEqual(result['properties']['provisioningState'],'Succeeded');self.assertEqual(read.call_count,2);sleep.assert_called_once_with(10)
    def test_operation_wait_rejects_failed_state(self):
        with patch.object(cutover,'get_app',return_value={'properties':{'provisioningState':'Failed'}}):
            with self.assertRaisesRegex(cutover.CutoverError,'service_provisioning_failed'):
                cutover.wait_app_operation_settled(cutover.API)
    def test_document_runtime_prerequisites_reuse_existing_shared_volume(self):
        template={'containers':[{'name':'api','env':[{'name':'EXISTING_SETTING','value':'preserve'}],
            'volumeMounts':[{'volumeName':'phd-shared-files-volume','mountPath':'/tmp/project-health-dashboard'}]}],
            'volumes':[{'name':'phd-shared-files-volume','storageName':'phd-shared-files','storageType':'AzureFile'}]}
        result=cutover.configure_document_runtime_prerequisites(copy.deepcopy(template))
        mounts=result['containers'][0]['volumeMounts'];env={x['name']:x.get('value') for x in result['containers'][0]['env']}
        self.assertIn({'volumeName':'phd-shared-files-volume','mountPath':cutover.UPLOAD_MOUNT},mounts)
        self.assertIn({'volumeName':'phd-shared-files-volume','mountPath':'/tmp/project-health-dashboard'},mounts)
        self.assertEqual(env['PROJECTPULSE_UPLOAD_ROOT'],cutover.UPLOAD_ROOT)
        self.assertEqual(env['PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT'],'true')
        self.assertEqual(env['PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS'],'true')
        self.assertEqual(env['PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID'],cutover.DOCUMENT_SERVICE_PRINCIPAL)
        self.assertEqual(env['EXISTING_SETTING'],'preserve')
    def test_document_runtime_prerequisites_fail_closed_without_reviewed_storage(self):
        template={'containers':[{'name':'api','env':[]}],'volumes':[]}
        with self.assertRaisesRegex(cutover.CutoverError,'shared_upload_storage_missing'):
            cutover.configure_document_runtime_prerequisites(template)
class SignatureStorage(unittest.TestCase):
    def setUp(self):
        import signature_storage
        self.m=signature_storage;self.source='a'*40
        self.mount={'properties':{'azureFile':{'accountName':self.m.ACCOUNT_NAME,
            'shareName':spec.STORAGE,'accessMode':'ReadWrite'}}}
        self.share={'id':self.m.SHARE,'properties':{'shareQuota':10,'enabledProtocols':'SMB',
            'metadata':{'purpose':'pulse_antivirus_signatures'}}}
        self.account={'id':self.m.ACCOUNT,'location':'westus3','properties':{
            'provisioningState':'Succeeded','publicNetworkAccess':'Disabled','allowSharedKeyAccess':True}}
        self.business={'properties':{'azureFile':{'accountName':self.m.ACCOUNT_NAME,
            'shareName':'project-health-dashboard','accessMode':'ReadWrite'}}}
    def fake(self,existing=False,bad_share=False,bad_mount=False):
        self.calls=[];self.present_share=existing;self.present_mount=existing
        def call(method,path,body=None,missing=False):
            self.calls.append((method,path,copy.deepcopy(body)))
            if path==self.m.ACCOUNT:return copy.deepcopy(self.account)
            if path==self.m.BUSINESS_MOUNT:return copy.deepcopy(self.business)
            if path==self.m.SHARE:
                if method=='PUT':self.present_share=True;return self.share
                if not self.present_share:return None
                value=copy.deepcopy(self.share)
                if bad_share:value['properties']['metadata']={}
                return value
            if path==self.m.MOUNT:
                if method=='PUT':self.present_mount=True;return self.mount
                if not self.present_mount:return None
                value=copy.deepcopy(self.mount)
                if bad_mount:value['properties']['azureFile']['shareName']='project-health-dashboard'
                return value
            if path==self.m.ACCOUNT+'/listKeys':return {'keys':[{'keyName':'key1','value':'SECRET_SENTINEL_'+'x'*40}]}
            raise AssertionError('Unexpected target')
        return call
    def test_activation_only_reads_prepared_mount(self):
        with patch.object(cutover,'rest',return_value=self.mount) as rest,patch.object(cutover,'az') as az:
            cutover.signatures()
        rest.assert_called_once_with('GET',self.m.MOUNT);az.assert_not_called()
    def test_wrong_account_business_share_and_read_only_mount_rejected(self):
        for key,value in [('accountName','other'),('shareName','project-health-dashboard'),('accessMode','ReadOnly')]:
            data=copy.deepcopy(self.mount);data['properties']['azureFile'][key]=value
            with self.subTest(key=key),self.assertRaises(self.m.PreparationError):self.m.validate_mount(data)
    def test_owner_prepares_only_dedicated_share_and_mount(self):
        with patch.object(self.m,'verified_owner_context') as owner,patch.object(self.m,'arm',side_effect=self.fake()):
            result=self.m.prepare(self.source,1232,self.m.CONFIRMATION)
        owner.assert_called_once_with(self.source,1232,self.m.CONFIRMATION)
        writes=[(m,p) for m,p,b in self.calls if m=='PUT']
        self.assertEqual(writes,[('PUT',self.m.SHARE),('PUT',self.m.MOUNT)])
        self.assertTrue(result['shareCreated']);self.assertTrue(result['mountCreated']);self.assertFalse(result['rolesChanged'])
        self.assertNotIn('SECRET_SENTINEL',json.dumps(result))
        self.assertFalse(any(p==self.m.BUSINESS_MOUNT and m!='GET' for m,p,b in self.calls))
    def test_prepared_storage_is_idempotent_without_keys_or_writes(self):
        with patch.object(self.m,'verified_owner_context'),patch.object(self.m,'arm',side_effect=self.fake(existing=True)):
            result=self.m.prepare(self.source,1232,self.m.CONFIRMATION)
        self.assertTrue(all(m=='GET' for m,p,b in self.calls));self.assertFalse(result['shareCreated']);self.assertFalse(result['mountCreated'])
    def test_existing_unowned_resources_never_overwritten(self):
        for share,mount in [(True,False),(False,True)]:
            with patch.object(self.m,'verified_owner_context'),patch.object(self.m,'arm',side_effect=self.fake(existing=True,bad_share=share,bad_mount=mount)),self.assertRaises(self.m.PreparationError):
                self.m.prepare(self.source,1232,self.m.CONFIRMATION)
            self.assertTrue(all(m=='GET' for m,p,b in self.calls))
    def test_unknown_source_stops_before_any_infrastructure_call(self):
        with patch.object(self.m,'verified_owner_context',side_effect=self.m.PreparationError('merged_review_required')),patch.object(self.m,'arm') as arm,self.assertRaises(self.m.PreparationError):
            self.m.prepare(self.source,1232,self.m.CONFIRMATION)
        arm.assert_not_called()
    def test_public_storage_is_rejected_before_preparation(self):
        self.account['properties']['publicNetworkAccess']='Enabled'
        with patch.object(self.m,'verified_owner_context'),patch.object(self.m,'arm',side_effect=self.fake()),self.assertRaises(self.m.PreparationError):
            self.m.prepare(self.source,1232,self.m.CONFIRMATION)
        self.assertEqual(len(self.calls),1)
    def test_forbidden_operations_cannot_call_azure(self):
        for method,path in [('DELETE',self.m.SHARE),('PUT',self.m.ACCOUNT),('PUT',self.m.BUSINESS_MOUNT),
                ('PATCH',self.m.MOUNT),('PUT',self.m.ENV+'/other'),('POST',self.m.ACCOUNT+'/regenerateKey')]:
            with self.subTest(method=method,path=path),patch.object(self.m,'run') as run,self.assertRaises(self.m.PreparationError):
                self.m.arm(method,path,{})
            run.assert_not_called()
    def test_preparation_requires_owner_not_ci_before_resource_mutation(self):
        with patch.dict(os.environ,{'GITHUB_ACTIONS':'true'}),patch.object(self.m,'run') as run,self.assertRaises(self.m.PreparationError):
            self.m.verified_owner_context(self.source,1232,self.m.CONFIRMATION)
        run.assert_not_called()
        with patch.dict(os.environ,{},clear=True),patch.object(self.m,'run',return_value={'id':spec.SUB,'state':'Enabled','user':{'type':'servicePrincipal'}}) as run,self.assertRaises(self.m.PreparationError):
            self.m.verified_owner_context(self.source,1232,self.m.CONFIRMATION)
        self.assertEqual(run.call_count,1)
    def test_private_body_file_is_removed_and_mode_restricted(self):
        filenames=[]
        def invoke(args,missing=False):
            filename=args[args.index('--body')+1][1:];filenames.append(filename)
            self.assertEqual(Path(filename).stat().st_mode & 0o777,0o600)
            self.assertEqual(json.loads(Path(filename).read_text())['private'],'synthetic')
            return {}
        with patch.object(self.m,'run',side_effect=invoke):self.m.arm('PUT',self.m.MOUNT,{'private':'synthetic'})
        self.assertFalse(Path(filenames[0]).exists())
    def test_missing_and_denied_are_not_conflated(self):
        import subprocess
        for code in ('ResourceNotFound','ShareNotFound','ManagedEnvironmentStorageNotFound'):
            for output in ('('+code+') unavailable',json.dumps({'error':{'code':code,'message':'unavailable'}})):
                with self.subTest(code=code),patch.object(self.m.subprocess,'run',return_value=subprocess.CompletedProcess([],1,'',output)):
                    self.assertIsNone(self.m.run([],missing=True))
                    with self.assertRaises(self.m.PreparationError):self.m.run([],missing=False)
        with patch.object(self.m.subprocess,'run',return_value=subprocess.CompletedProcess([],1,'','(AuthorizationFailed) SECRET')),self.assertRaises(self.m.PreparationError) as error:
            self.m.run([],missing=True)
        self.assertEqual(str(error.exception),'infrastructure_operation_failed')

if __name__=='__main__':unittest.main(verbosity=2)
