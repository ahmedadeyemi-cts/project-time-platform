"""Constrain recovery to one identified stale run, preserving deployment controls."""
from pathlib import Path
import subprocess
BASE = 'c15ef12d5ce1bc54c15d8b31c87a50daa94bad17'
SUP = '.github/workflows/module025-protected-uat-control.yml'
REG = 'scripts/release-test/validate-protected-test-controller-branches.sh'
TEST = 'tests/module025-deployment-startup-recovery.test.mjs'
SELF = 'tests/protected-test-foundation-recovery-scope.py'
def show(path): return subprocess.check_output(['git','show',BASE+':'+path]).decode()
assert subprocess.check_output(['git','merge-base',BASE,'HEAD']).decode().strip() == BASE
assert set(subprocess.check_output(['git','diff','--name-only',BASE]).decode().splitlines()) == {SUP, REG, TEST, SELF}
s = Path(SUP).read_text()
a=s.index('            # Exact foundation-release orphan:')
b=s.index('            run_status=',a)
block=s[a:b]
for required in ['git merge-base --is-ancestor', 'git diff --quiet', 'verify_zero_job_quarantine', 'pending_deployments', 'type == "array" and length == 0', BASE, '2026-09-19T22:39:09Z']:
    assert required in block
s=(s[:a]+s[b:]).replace("      QUARANTINED_ZERO_JOB_RUN_ID_7: '35473939797'\n",'')
assert s == show(SUP), 'All other admission/dispatch/verification logic must remain identical'
s=Path(REG).read_text()
added='if [[ "$HEAD_BRANCH" == fix/protected-test-foundation-recovery-20260919 ]]; then\n  python3 tests/protected-test-foundation-recovery-scope.py\n  node tests/module025-deployment-startup-recovery.test.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\nelif'
assert s.replace(added,'if',1)==show(REG)
for path in ['.github/workflows/projectpulse-deploy-test.yml','.github/workflows/projectpulse-deploy-production.yml','scripts/release-test/verify-module025-quarantine-controller.py']:
    assert Path(path).read_text()==show(path)
deploy=show('.github/workflows/projectpulse-deploy-test.yml')
assert deploy.index('Guard exact source and validate release') < deploy.index('Sign in to protected Test subscription')
assert '[[ "$(git rev-parse origin/main)" == "$TARGET_RELEASE_COMMIT" ]]' in deploy
assert 'environment: test' in deploy and 'group: projectpulse-deploy-test' in deploy
print('PROTECTED_TEST_FOUNDATION_RECOVERY_SCOPE=PASS deployment_controller=unchanged production=unchanged')
