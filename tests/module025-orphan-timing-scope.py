"""Exact recovery scope: no deployment controller or application changes."""
from pathlib import Path
import hashlib, os, subprocess, runpy
BASE = 'c647b8f5c09641a458b522a71ff614767ac06042'
SELF = 'tests/module025-orphan-timing-scope.py'
EXPECTED = {'.github/workflows/module025-protected-uat-control.yml': '0dab7e555df284ae700d03b4eae81a5dc8e0b6661944cc42b68f254683dc78ce', 'scripts/release-test/verify-module025-quarantine-controller.py': '938eabf7a76136d2c0cccbf26913895934174ec95e0fb3ff6257cb0de3a9a6a5', 'scripts/release-test/validate-protected-test-controller-branches.sh': '38d40c3c2e5f74b524bfd81ce6b92173fc125acbd368592f2d402883e57606a7', 'tests/module025-deployment-startup-recovery.test.mjs': 'cdd249b53e03fdb622b249abb9d0e71e16b9179f6f3dc5fd770c94d8d0990c78'}
def main():
    assert (os.environ.get('GITHUB_HEAD_REF') or os.environ.get('GITHUB_REF_NAME')) == 'fix/module025-orphan-timing-quarantine'
    assert subprocess.check_output(['git','merge-base',BASE,'HEAD']).decode().strip() == BASE
    actual = set(subprocess.check_output(['git','diff','--name-only',BASE]).decode().splitlines())
    assert actual == set(EXPECTED) | {SELF}, 'Unexpected recovery files'
    for name, digest in EXPECTED.items():
        assert hashlib.sha256(Path(name).read_bytes()).hexdigest() == digest, name
    helper = runpy.run_path('scripts/release-test/verify-module025-quarantine-controller.py')
    original = subprocess.check_output(['git','show',helper['BASE']+':'+helper['CONTROLLER']])
    reviewed = original
    for old, new in helper['REPLACEMENTS']: reviewed = reviewed.replace(old,new,1)
    allowed = helper['permitted']
    assert allowed(original,original) and allowed(original,reviewed)
    assert not allowed(original,reviewed+b'\n')
    assert not allowed(original,original.replace(*helper['REPLACEMENTS'][0],1))
    assert not allowed(original,reviewed.replace(b'permissions:',b'changed-permissions:',1))
    assert not allowed(original,reviewed.replace(b'2520',b'9999',1))
    assert not allowed(b'',reviewed)
    print('MODULE025_ORPHAN_TIMING_SCOPE=PASS controller_negative_tests=5')
if __name__ == '__main__': main()
