"""Exact additive core lane registration. Never authorizes a deployment."""
from pathlib import Path
import hashlib
import json

ROOT=Path(__file__).resolve().parents[1]
REG=json.loads((ROOT/'tests/core-test-controller-registration.json').read_text())


def normalize(data):
    if isinstance(data,str):data=data.encode()
    if hashlib.sha256(data).hexdigest()!=REG['controllerSha256']:
        import importlib.util
        spec=importlib.util.spec_from_file_location('accounting_projection',ROOT/'scripts/resilience/accounting_release_projection.py')
        projection=importlib.util.module_from_spec(spec);spec.loader.exec_module(projection)
        data=projection.normalize(data,'.github/workflows/projectpulse-deploy-test.yml')
    assert hashlib.sha256(data).hexdigest()==REG['controllerSha256'],'Unregistered core controller content'
    for block in (REG['coreBlock'],REG['inputBlock']):
        raw=block.encode();assert data.count(raw)==1,'Core block changed';data=data.replace(raw,b'',1)
    for current,prior in REG['replacements']:
        raw=current.encode();assert data.count(raw)>=1,'Legacy step guard changed';data=data.replace(raw,prior.encode(),1)
    assert hashlib.sha256(data).hexdigest()==REG['parentSha256'],'Legacy release controller changed'
    return data


def normalize_supervisor(data):
    reg=REG['supervisor']
    if hashlib.sha256(data).hexdigest()!=reg['sha256']:
        import importlib.util
        spec=importlib.util.spec_from_file_location('accounting_supervisor_projection',ROOT/'scripts/resilience/accounting_release_projection.py')
        projection=importlib.util.module_from_spec(spec);spec.loader.exec_module(projection)
        data=projection.normalize(data,'.github/workflows/module025-protected-uat-control.yml')
    assert hashlib.sha256(data).hexdigest()==reg['sha256'],'Unregistered supervisor content'
    for block in reg['blocks']:
        raw=block.encode();assert data.count(raw)==1;data=data.replace(raw,b'',1)
    assert hashlib.sha256(data).hexdigest()==reg['parentSha256'],'Legacy supervisor changed'
    return data
