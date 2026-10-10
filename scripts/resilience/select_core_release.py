"""Select core mode only for this exact merged, reviewed PR; never grants admission."""
import json
import os
from pathlib import Path
import subprocess

REPO='ahmedadeyemi-cts/project-time-platform'
sha=os.environ['GITHUB_SHA']
assert os.environ['GITHUB_REPOSITORY']==REPO and os.environ['GITHUB_REF']=='refs/heads/main'
prs=json.loads(subprocess.check_output(['gh','api','repos/'+REPO+'/commits/'+sha+'/pulls'],text=True))
matched=[p for p in prs if p.get('merged_at') and p.get('merge_commit_sha')==sha and p['head']['ref']=='feat/pulse-core-test-controller-20261009']
assert len(matched)<=1,'Ambiguous merged core PR'
core=bool(matched)
pr=matched[0] if core else None
if core:
    assert pr['head']['ref']=='feat/pulse-core-test-controller-20261009' and pr['base']['ref']=='main'
    assert pr['head']['repo']['full_name']==REPO
    base=subprocess.check_output(['git','rev-parse',sha+'^1'],text=True).strip()
    subprocess.run(['python3','tests/core-controller-scope.py','--base',base],check=True)
    subprocess.run(['python3','tests/security-release/test_controller_registration.py'],check=True)
accounting=[p for p in prs if p.get('merged_at') and p.get('merge_commit_sha')==sha and p['head']['ref']=='feat/accounting-milestones-20261009']
assert len(accounting)<=1 and not(core and accounting),'Ambiguous accounting release'
if accounting:
    assert accounting[0]['base']['ref']=='main' and accounting[0]['head']['repo']['full_name']==REPO
    base=subprocess.check_output(['git','rev-parse',sha+'^1'],text=True).strip()
    subprocess.run(['python3','tests/accounting-release-scope.py','--base',base],check=True)
    runs=json.loads(subprocess.check_output(['gh','api','repos/'+REPO+'/actions/runs?head_sha='+sha+'&event=push&per_page=100'],text=True))['workflow_runs']
    tests=sorted((r for r in runs if r['name']=='Accounting reporting and milestone billing tests'),key=lambda r:r['id'],reverse=True)
    assert tests and tests[0]['status']=='completed' and tests[0]['conclusion']=='success','Exact-main accounting tests must pass'
scope='sow_exports' if accounting else 'full'
with Path(os.environ['GITHUB_OUTPUT']).open('a') as f:
    f.write('core_only='+str(core).lower()+'\nacceptance_scope='+scope+'\n')
print('PROTECTED_TEST_CORE_MODE='+str(core).lower())
