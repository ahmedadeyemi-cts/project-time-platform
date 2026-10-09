"""Stateful Azure simulation: traffic ordering, live recovery, errors and source binding."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).parent))
import core_test_controller as core

SHA='a'*40
DIGEST='sha256:'+'b'*64
OLD_IMAGE=core.ACR+'.azurecr.io/project-health-dashboard-api@sha256:'+'c'*64
NEW_IMAGE=core.ACR+'.azurecr.io/project-health-dashboard-api@'+DIGEST
OLD=core.APP+'--old'
DOMAIN='test.westus3.azurecontainerapps.io'
BASE={'name':core.APP,'properties':{'latestRevisionName':OLD,'latestReadyRevisionName':OLD,
      'configuration':{'activeRevisionsMode':'Single','ingress':{'fqdn':core.APP+'.'+DOMAIN,'traffic':[{'latestRevision':True,'weight':100}]},'secrets':[{'name':'db'}]},
      'template':{'containers':[{'name':'api','image':OLD_IMAGE,'env':[{'name':'DB','secretRef':'db'}]}],'scale':{'minReplicas':1}}}}
ENV={'RELEASE_SHA':SHA,'EXPECTED_MAIN_SHA':SHA,'GITHUB_SHA':SHA,'GITHUB_REPOSITORY':core.REPO,
     'GITHUB_REF':'refs/heads/main','GITHUB_EVENT_NAME':'workflow_dispatch',
     'GITHUB_WORKFLOW_REF':core.REPO+'/.github/workflows/projectpulse-deploy-test.yml@refs/heads/main',
     'GITHUB_RUN_ID':'123','GITHUB_RUN_ATTEMPT':'1','AZURE_SUBSCRIPTION_ID':core.SUB,
     'AZURE_RESOURCE_GROUP':core.RG,'AZURE_API_APP':core.APP,'AZURE_ACR_NAME':core.ACR}

class Cloud:
    def __init__(self):
        self.state=copy.deepcopy(BASE);self.revs={OLD:self.rev(OLD,self.state['properties']['template'])};self.commands=[]
    def rev(self,name,t):return {'name':name,'properties':{'template':copy.deepcopy(t),'active':True,'healthState':'Healthy','provisioningState':'Provisioned','fqdn':name+'.'+DOMAIN}}
    def __call__(self,*args,json_result=False):
        self.commands.append(args)
        if args[:2]==('account','show'):return {'id':core.SUB}
        if args[:3]==('acr','repository','show'):return {'digest':DIGEST}
        if args[:2]==('acr','build'):return None
        if args[:2]==('containerapp','show'):return copy.deepcopy(self.state)
        if args[:3]==('containerapp','revision','show'):return copy.deepcopy(self.revs[args[args.index('--revision')+1]])
        if args[:3]==('containerapp','revision','set-mode'):
            self.state['properties']['configuration']['activeRevisionsMode']=args[-1].title();return
        if args[:3]==('containerapp','ingress','traffic'):
            entries=args[args.index('--revision-weight')+1:]
            self.state['properties']['configuration']['ingress']['traffic']=[{'revisionName':e.split('=')[0],'weight':int(e.split('=')[1])} for e in entries];return
        if args[:3]==('containerapp','revision','copy'):
            assert self.state['properties']['configuration']['activeRevisionsMode']=='Multiple'
            assert all(not t.get('latestRevision') for t in self.state['properties']['configuration']['ingress']['traffic'])
            name=core.APP+'--'+args[args.index('--revision-suffix')+1]
            t=copy.deepcopy(self.revs[args[args.index('--from-revision')+1]]['properties']['template'])
            if '--image' in args:
                t['containers'][0]['image']=args[args.index('--image')+1];t['containers'][0]['env'].append({'name':'PROJECTPULSE_SOURCE_COMMIT','value':SHA})
            t['revisionSuffix']=args[args.index('--revision-suffix')+1]
            self.revs[name]=self.rev(name,t);self.state['properties'].update(latestRevisionName=name,latestReadyRevisionName=name,template=t);return
        if args[:3] in [('containerapp','revision','activate'),('containerapp','revision','deactivate')]:
            self.revs[args[args.index('--revision')+1]]['properties']['active']=args[2]=='activate';return
        raise AssertionError(args)

def git(*args,**kwargs):
    if args[:2]==('git','rev-parse'):return SHA
    if args[:2]==('git','status'):return ''
    if args[0]=='gh':return SHA
    if args[:2]==('git','archive'):
        import tarfile,io
        output=args[3].split('=',1)[1]
        with tarfile.open(output,'w') as t:
            for name in ['src/backend/ProjectTime.Api']:
                item=tarfile.TarInfo(name);item.type=tarfile.DIRTYPE;t.addfile(item)
        return ''
    raise AssertionError(args)

class Tests(unittest.TestCase):
    def test_end_to_end_canary_recovery_before_promotion(self):
        cloud=Cloud();checks=[]
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,ENV,clear=True),patch.object(core,'az',cloud),patch.object(core,'run',git),patch.object(core,'check',side_effect=lambda base,source=None: checks.append((base,source)) or {'result':'PASS'}):
            c=core.Controller(Path(temp)/'safe.json');c.execute()
            self.assertTrue(c.finished);self.assertEqual(c.summary['rollback'],'PASS')
            self.assertEqual(c.summary['result'],'PASS')
            self.assertEqual(cloud.state['properties']['configuration']['activeRevisionsMode'],'Single')
            self.assertEqual(cloud.state['properties']['template']['containers'][0]['image'],NEW_IMAGE)
            self.assertEqual(checks[2],(core.ORIGIN,None)) # live restored-baseline check
            self.assertEqual(checks[-1],(core.ORIGIN,SHA))
            self.assertNotIn('secretRef',(Path(temp)/'safe.json').read_text())
    def test_canary_failure_leaves_baseline_and_recovery_restores_template(self):
        cloud=Cloud()
        def check(base,source=None):
            if source:raise RuntimeError('failed_canary')
            return {'result':'PASS'}
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,ENV,clear=True),patch.object(core,'az',cloud),patch.object(core,'run',git),patch.object(core,'check',side_effect=check):
            c=core.Controller(Path(temp)/'safe.json')
            with self.assertRaises(RuntimeError):c.execute()
            self.assertEqual(cloud.state['properties']['configuration']['ingress']['traffic'][0]['revisionName'],OLD)
            c.recover()
            self.assertEqual(core.template(cloud.state['properties']['template']),core.template(BASE['properties']['template']))
            self.assertEqual(c.summary['rollback'],'PASS')
    def test_production_admission_never_mutates(self):
        cloud=Cloud()
        with tempfile.TemporaryDirectory() as temp,patch.dict(os.environ,{**ENV,'AZURE_API_APP':'production'},clear=True),patch.object(core,'az',cloud):
            with self.assertRaises(RuntimeError):core.Controller(Path(temp)/'safe.json').admit()
            self.assertEqual(cloud.commands,[])
    def test_digest_drift_fails_closed(self):
        cloud=Cloud();c=core.Controller(Path('/tmp/unused'))
        with patch.object(core,'az',cloud):
            with self.assertRaises(RuntimeError):c.wait(OLD,NEW_IMAGE)
    def test_unknown_projection_rejected(self):
        spec=importlib.util.spec_from_file_location('projection',Path(__file__).parents[2]/'tests/core_test_controller_projection.py')
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        with self.assertRaises(AssertionError):module.normalize(b'unknown')

if __name__=='__main__':unittest.main()
