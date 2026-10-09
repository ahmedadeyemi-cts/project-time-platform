#!/usr/bin/env python3
import copy
import importlib.util
from pathlib import Path
spec = importlib.util.spec_from_file_location('snap', Path(__file__).with_name('verify-core-test-revision-snapshot.py'))
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
image = 'acrphdtest7825cc.azurecr.io/project-health-dashboard-api@sha256:' + 'a'*64
base = {'name':mod.API,'properties':{'latestReadyRevisionName':'api--r1','configuration':{'ingress':{'traffic':[{'latestRevision':True,'weight':100}]}},'template':{'containers':[{'image':image}],'scale':{'minReplicas':1}}}}
assert not mod.verify(base)
for change in ('name','image','traffic','ready','replicas'):
    obj=copy.deepcopy(base)
    if change=='name': obj['name']='production-api'
    if change=='image': obj['properties']['template']['containers'][0]['image']='example:latest'
    if change=='traffic': obj['properties']['configuration']['ingress']['traffic'][0]['weight']=90
    if change=='ready': obj['properties']['latestReadyRevisionName']=''
    if change=='replicas': obj['properties']['template']['scale']['minReplicas']=0
    assert mod.verify(obj),change
print('CORE_TEST_REVISION_SNAPSHOT_POLICY=PASS')
