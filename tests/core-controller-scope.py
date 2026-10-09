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
ORIGINAL='f003a6f921b977e13ce9e6bf24c0320ff84c57e4'
FOLLOWUP_BASE='6f42999e86a9cfa36db77b2de098d5b0f22e8797'
if args.base=='09545e6d18c4b7422f62a7a71360a184529d24d9':
    delta=set(git('diff','--name-only',args.base+'...HEAD').splitlines())
    assert delta=={'.github/workflows/pulse-core-offline-readonly-ci.yml','scripts/resilience/core_test_controller.py','scripts/resilience/verify-core-candidate-traffic.py','scripts/resilience/test-core-candidate-traffic.py','scripts/resilience/test_core_controller.py','scripts/resilience/failed_core_checkpoint.py','scripts/resilience/test_failed_core_checkpoint.py','tests/core-controller-files.json','tests/core-controller-scope.py'},'Unreviewed effective-traffic recovery scope'
    assert git('diff',args.base,'HEAD','--','.github/workflows/projectpulse-deploy-test.yml')=='','Registered native deployment controller changed'
    args.base=ORIGINAL
if args.base==FOLLOWUP_BASE:
    delta=set(git('diff','--name-only',args.base+'...HEAD').splitlines())
    assert delta=={'.github/workflows/module025-protected-uat-control.yml','scripts/resilience/core_test_controller.py','scripts/resilience/select_core_release.py','scripts/resilience/test_core_controller.py','tests/core-controller-scope.py','tests/core-test-controller-registration.json','scripts/resilience/core_test_canary.py','scripts/resilience/private_core_canary.py','scripts/resilience/test_private_core_canary.py','scripts/resilience/CoreCanary/Program.cs','scripts/resilience/CoreCanary/Pulse.CoreCanary.csproj','src/backend/ProjectTime.Api/Modules/CelarAiRuntimeVersionModule.cs','.github/workflows/pulse-core-offline-readonly-ci.yml','tests/core-controller-files.json'},'Unreviewed runtime compatibility scope'
    assert git('diff',args.base,'HEAD','--','.github/workflows/projectpulse-deploy-test.yml')=='','Registered native deployment controller changed'
    args.base=ORIGINAL # Re-prove the complete original package and byte projections.
assert args.base==ORIGINAL,'Unregistered core release base'
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
