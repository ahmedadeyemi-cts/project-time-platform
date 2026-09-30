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
        with patch.object(cutover,'get_app',return_value=broken),patch.object(cutover,'wait_app',return_value=self.before),patch.object(cutover,'local_admin',return_value='same'),patch.object(cutover,'rest',side_effect=lambda *x:calls.append(x)),patch.object(cutover,'cleanup_staged'):
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
if __name__=='__main__':unittest.main(verbosity=2)
