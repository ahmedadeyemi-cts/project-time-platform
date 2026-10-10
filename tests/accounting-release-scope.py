#!/usr/bin/env python3
"""Prove accounting release scope and byte-identical predecessor protection."""
import argparse,hashlib,importlib.util,json,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser();p.add_argument('--base',required=True);args=p.parse_args()
manifest=json.loads((ROOT/'tests/accounting-release-files.json').read_text())
changed=set(subprocess.check_output(['git','diff','--name-only',args.base,'HEAD'],cwd=ROOT,text=True).splitlines())
assert changed==set(manifest),'Accounting release file scope changed'
for f in ['scripts/validate-deployment-concurrency-governance.mjs','.github/workflows/deployment-concurrency-governance-ci.yml','.github/deployment-concurrency-governance-v1.enabled','.github/workflows/projectpulse-deploy-production.yml','scripts/security/validate-repository-security-posture.py']:
 assert subprocess.check_output(['git','diff',args.base,'HEAD','--',f],cwd=ROOT)==b'','Immutable release protection changed'
spec=importlib.util.spec_from_file_location('projection',ROOT/'scripts/resilience/accounting_release_projection.py');m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
for f in ['.github/workflows/projectpulse-deploy-test.yml','.github/workflows/module025-protected-uat-control.yml']:
 assert m.normalize((ROOT/f).read_bytes(),f)==subprocess.check_output(['git','show',args.base+':'+f],cwd=ROOT),'Predecessor deployment behavior changed'
print('ACCOUNTING_EXACT_SCOPE_AND_PREDECESSOR=PASS')
