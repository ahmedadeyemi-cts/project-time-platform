"""Exact migration-controller recovery review, including negative guard checks."""
from pathlib import Path
import hashlib
import runpy
import subprocess
BASE = 'efaf7371e3c4b587346c1f5645fc755bee70a8d8'
SUP = '.github/workflows/module025-protected-uat-control.yml'
HELPER = 'scripts/release-test/verify-module025-quarantine-controller.py'
REG = 'scripts/release-test/validate-protected-test-controller-branches.sh'
SELF = 'tests/uat-enterprise-controller-review.py'
def show(ref, path): return subprocess.check_output(['git', 'show', ref+':'+path])
assert subprocess.check_output(['git','merge-base',BASE,'HEAD']).decode().strip() == BASE
assert set(subprocess.check_output(['git','diff','--name-only',BASE]).decode().splitlines()) == {SUP, HELPER, REG, SELF}
helper = runpy.run_path(HELPER)
current = Path(helper['CONTROLLER']).read_bytes()
assert current == show(BASE, helper['CONTROLLER'])
assert hashlib.sha256(current).hexdigest() == helper['ENTERPRISE_SHA256']
def guard(raw):
    return raw.split(b'      - name: Guard exact source and validate release\n',1)[1].split(b'\n      - name:',1)[0]
for old in (helper['BASE'], '57c8d0264bdd828e6b3b53a8c5cb8b1b841e8f61', 'c15ef12d5ce1bc54c15d8b31c87a50daa94bad17'):
    original = show(old,helper['CONTROLLER'])
    assert guard(original) == guard(current)
    assert helper['permitted'](original,current)
    for bad in (current+b'\n',current.replace(b'environment: test',b'environment: production'),current.replace(b'origin/main',b'origin/other'),current.replace(b'cancel-in-progress: false',b'cancel-in-progress: true')):
        assert not helper['permitted'](original,bad)
assert not helper['permitted'](b'',current)
assert current.index(b'Guard exact source and validate release') < current.index(b'Sign in to protected Test subscription')
expected=show(BASE,SUP).decode()
for sha in ('57c8d0264bdd828e6b3b53a8c5cb8b1b841e8f61','c15ef12d5ce1bc54c15d8b31c87a50daa94bad17'):
    expected=expected.replace(f'git diff --quiet \'{sha}\' HEAD -- "$DEPLOY_WORKFLOW_PATH"', f"python3 scripts/release-test/verify-module025-quarantine-controller.py --base '{sha}'")
expected=expected.replace('A descendant with the identical deployment controller','A descendant with an exact reviewed deployment controller')
assert Path(SUP).read_text()==expected, 'Run identity, zero-job, ancestry, approval and dispatch checks must remain unchanged'
expected=show(BASE,REG).decode().replace('if [[ "$HEAD_BRANCH" == fix/enterprise-completion-20260919 ]]; then','if [[ "$HEAD_BRANCH" == fix/uat-enterprise-controller-review-20260920 ]]; then\n  python3 tests/uat-enterprise-controller-review.py\n  node tests/module025-deployment-startup-recovery.test.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\nelif [[ "$HEAD_BRANCH" == fix/enterprise-completion-20260919 ]]; then',1)
assert Path(REG).read_text()==expected
p='.github/workflows/projectpulse-deploy-production.yml'
assert Path(p).read_bytes()==show(BASE,p)
print('UAT_ENTERPRISE_CONTROLLER_REVIEW=PASS exact_controller_digest=true stale_main_guard=unchanged production=unchanged')
