"""Exact compatibility repair; production, application and deploy steps unchanged."""
from pathlib import Path
import hashlib
import subprocess
import runpy

BASE = 'ab889f59779ed17e0fe70fb8b8b6a3893d830337'
SELF = 'tests/module025-export-quarantine-scope.py'
HELPER = 'scripts/release-test/verify-module025-quarantine-controller.py'
SUPERVISOR = '.github/workflows/module025-protected-uat-control.yml'
REGISTRY = 'scripts/release-test/validate-protected-test-controller-branches.sh'

def show(commit,path): return subprocess.check_output(['git','show',commit+':'+path])

def main():
    assert subprocess.check_output(['git','merge-base',BASE,'HEAD']).decode().strip() == BASE
    assert set(subprocess.check_output(['git','diff','--name-only',BASE]).decode().splitlines()) == {SELF,HELPER,SUPERVISOR,REGISTRY}
    expected = show(BASE,SUPERVISOR).replace(b"      - '.github/workflows/projectpulse-deploy-test.yml'", b"      - '.github/workflows/projectpulse-deploy-test.yml'\n      - 'scripts/release-test/verify-module025-quarantine-controller.py'",1)
    assert Path(SUPERVISOR).read_bytes() == expected
    expected = show(BASE,REGISTRY).replace(b'if [[ "$HEAD_BRANCH" == fix/module025-standard-download-formats-20260919 ]]; then', b'if [[ "$HEAD_BRANCH" == fix/module025-export-quarantine-compatibility ]]; then\n  python3 tests/module025-export-quarantine-scope.py\n  node tests/validate-systemwide-image-build-controller.mjs\nelif [[ "$HEAD_BRANCH" == fix/module025-standard-download-formats-20260919 ]]; then',1)
    assert Path(REGISTRY).read_bytes() == expected
    helper = runpy.run_path(HELPER)
    original = show(helper['BASE'],helper['CONTROLLER'])
    current = Path(helper['CONTROLLER']).read_bytes()
    assert current == show(BASE,helper['CONTROLLER'])
    # The old queued run must still fail before Azure authentication on stale main.
    start = b'      - name: Guard exact source and validate release\n'
    def guard(raw): return raw.split(start,1)[1].split(b'\n      - name:',1)[0]
    assert guard(original) == guard(current)
    assert b'origin/main' in guard(current)
    allowed = helper['permitted']
    reviewed = original
    for a,b in helper['REPLACEMENTS']: reviewed = reviewed.replace(a,b,1)
    assert allowed(original,original) and allowed(original,reviewed) and allowed(original,current)
    assert not allowed(b'',current)
    for bad in [current+b'\n',current.replace(b'environment: test',b'environment: production'),
                current.replace(b'origin/main',b'origin/other'),current.replace(b'cancel-in-progress: false',b'cancel-in-progress: true')]:
        assert not allowed(original,bad)
    assert Path('.github/workflows/projectpulse-deploy-production.yml').read_bytes() == show(BASE,'.github/workflows/projectpulse-deploy-production.yml')
    print('MODULE025_EXPORT_QUARANTINE_SCOPE=PASS stale_main_guard=identical unreviewed_controllers=rejected')

if __name__=='__main__': main()
