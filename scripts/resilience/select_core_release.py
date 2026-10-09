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
with Path(os.environ['GITHUB_OUTPUT']).open('a') as f:f.write('core_only='+str(core).lower()+'\n')
print('PROTECTED_TEST_CORE_MODE='+str(core).lower())
