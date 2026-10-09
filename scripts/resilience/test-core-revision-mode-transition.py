#!/usr/bin/env python3
import copy,importlib.util
from pathlib import Path
spec=importlib.util.spec_from_file_location('policy',Path(__file__).with_name('verify-core-revision-mode-transition.py'))
p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)
b={'name':'ca-phd-test-api-westus3','properties':{'latestReadyRevisionName':'api--old','configuration':{'activeRevisionsMode':'Single','secrets':[],'registries':[],'dapr':None,'ingress':{'traffic':[{'latestRevision':True,'weight':100}]}}}}
a=copy.deepcopy(b);a['properties']['configuration']['activeRevisionsMode']='Multiple'
assert not p.verify(b,a)
for kind in ('mode','traffic','revision','resource','secrets'):
    c=copy.deepcopy(a)
    if kind=='mode':c['properties']['configuration']['activeRevisionsMode']='Single'
    if kind=='traffic':c['properties']['configuration']['ingress']['traffic'][0]['weight']=0
    if kind=='revision':c['properties']['latestReadyRevisionName']='changed'
    if kind=='resource':c['name']='production'
    if kind=='secrets':c['properties']['configuration']['secrets']=[{'name':'unexpected'}]
    assert p.verify(b,c),kind
print('CORE_REVISION_MODE_POLICY_TEST=PASS')
