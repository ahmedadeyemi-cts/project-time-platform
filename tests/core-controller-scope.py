"""Exact core resilience package, immutable predecessor projection, no Production/Oracle writes."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--base',required=True);args=p.parse_args()
def git(*args):return subprocess.check_output(['git',*args],cwd=ROOT,text=True).strip()
assert len(args.base)==40 and all(c in '0123456789abcdef' for c in args.base),'Invalid base'
manifest=json.loads((ROOT/'tests/core-controller-files.json').read_text())
changed=set(git('diff','--name-only',args.base+'...HEAD').splitlines())
assert changed==set(manifest),'Core package differs from exact reviewed file set'
assert not any(x.startswith('database/') or 'production.yml' in x or x.startswith('ops/') for x in changed),'Forbidden data/Production/Oracle scope'
for file in ['scripts/validate-deployment-concurrency-governance.mjs','.github/workflows/deployment-concurrency-governance-ci.yml','.github/deployment-concurrency-governance-v1.enabled','.github/workflows/projectpulse-deploy-production.yml']:
    assert git('diff',args.base,'HEAD','--',file)=='','Immutable protection changed: '+file
spec=importlib.util.spec_from_file_location('projection',ROOT/'tests/core_test_controller_projection.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
projected=m.normalize((ROOT/'.github/workflows/projectpulse-deploy-test.yml').read_bytes())
assert projected==subprocess.check_output(['git','show',args.base+':.github/workflows/projectpulse-deploy-test.yml'],cwd=ROOT),'Existing release behavior changed'
assert m.normalize_supervisor((ROOT/'.github/workflows/module025-protected-uat-control.yml').read_bytes())==subprocess.check_output(['git','show',args.base+':.github/workflows/module025-protected-uat-control.yml'],cwd=ROOT),'Existing supervisor protections changed'
subprocess.run(['git','diff','--check',args.base+'...HEAD'],cwd=ROOT,check=True)
print('CORE_EXACT_SCOPE_AND_IMMUTABLE_PREDECESSOR=PASS')
