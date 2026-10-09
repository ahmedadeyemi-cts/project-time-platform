#!/usr/bin/env python3
import copy,importlib.util
from pathlib import Path
spec=importlib.util.spec_from_file_location('policy',Path(__file__).with_name('verify-core-candidate-traffic.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
b={'name':'ca-phd-test-api-westus3','properties':{'latestReadyRevisionName':'api--old'}}
c={'name':b['name'],'properties':{'configuration':{'activeRevisionsMode':'Multiple','ingress':{'traffic':[{'revisionName':'api--old','weight':100},{'revisionName':'api--new','weight':0}]}}}}
assert not m.verify(b,c)
for case in ('target','traffic','candidate','mode'):
    obj=copy.deepcopy(c)
    if case=='target':obj['name']='production-api'
    if case=='traffic':obj['properties']['configuration']['ingress']['traffic'][1]['weight']=10
    if case=='candidate':obj['properties']['configuration']['ingress']['traffic'].pop()
    if case=='mode':obj['properties']['configuration']['activeRevisionsMode']='Single'
    assert m.verify(b,obj),case
omitted=copy.deepcopy(c)
omitted['properties']['configuration']['ingress']['traffic'].pop()
omitted['properties']['latestRevisionName']='api--new'
assert not m.verify(b,omitted,'api--new')
assert m.verify(b,omitted,'api--wrong')
omitted['properties']['configuration']['ingress']['traffic'][0]={'latestRevision':True,'weight':100}
assert m.verify(b,omitted,'api--new')
print('CORE_CANDIDATE_POLICY_TEST=PASS')
