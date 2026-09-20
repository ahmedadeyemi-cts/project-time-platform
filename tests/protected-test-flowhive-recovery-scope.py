"""Constrain recovery to one identified stale run, preserving deployment controls."""
from pathlib import Path
import subprocess
BASE = '2fffe5f44add20fbf0e050bc1340a2e9b5e131f7'
ORPHAN = '045b66ca01baa68b2f5b3f6eb9e063c23c335981'
SUP = '.github/workflows/module025-protected-uat-control.yml'
REG = 'scripts/release-test/validate-protected-test-controller-branches.sh'
TEST = 'tests/module025-deployment-startup-recovery.test.mjs'
SELF = 'tests/protected-test-flowhive-recovery-scope.py'
def show(path): return subprocess.check_output(['git','show',BASE+':'+path]).decode()
assert subprocess.check_output(['git','merge-base',BASE,'HEAD']).decode().strip() == BASE
assert (set(subprocess.check_output(['git','diff','--name-only',BASE]).decode().splitlines()) | set(subprocess.check_output(['git','ls-files','--others','--exclude-standard']).decode().splitlines())) == {SUP, REG, TEST, SELF}
s = Path(SUP).read_text()
a=s.index('            # Exact FlowHive sequential-release orphan:')
b=s.index('            run_status=',a)
block=s[a:b]
for required in ['git merge-base --is-ancestor', 'git diff --quiet', 'verify_zero_job_quarantine', 'pending_deployments', 'type == "array" and length == 0', ORPHAN, '2026-09-20T17:09:14Z']:
    assert required in block
s=(s[:a]+s[b:]).replace("      QUARANTINED_ZERO_JOB_RUN_ID_8: '35524946948'\n",'')
assert s == show(SUP), 'All other admission/dispatch/verification logic must remain identical'
s=Path(REG).read_text()
added='if [[ "$HEAD_BRANCH" == fix/protected-test-flowhive-recovery-20260920 ]]; then\n  python3 tests/protected-test-flowhive-recovery-scope.py\n  node tests/module025-deployment-startup-recovery.test.mjs\n  node tests/validate-systemwide-image-build-controller.mjs\nelif'
assert s.replace(added,'if',1)==show(REG)
t = Path(TEST).read_text().replace(
    ", QUARANTINED_ZERO_JOB_RUN_ID_8: '35524946948'", '')
t = t.replace(
    ",\n    {...defaultRun, id: 35524946948, head_sha: '045b66ca01baa68b2f5b3f6eb9e063c23c335981', created_at: '2026-09-20T17:09:14Z', updated_at: '2026-09-20T17:09:14Z'}", '')
assert t == show(TEST), 'Preserve all existing recovery, startup and negative cases'
for path in ['.github/workflows/projectpulse-deploy-test.yml','.github/workflows/projectpulse-deploy-production.yml','scripts/release-test/verify-module025-quarantine-controller.py']:
    assert Path(path).read_text()==show(path)
deploy=show('.github/workflows/projectpulse-deploy-test.yml')
assert deploy.index('Guard exact source and validate release') < deploy.index('Sign in to protected Test subscription')
assert '[[ "$(git rev-parse origin/main)" == "$TARGET_RELEASE_COMMIT" ]]' in deploy
assert 'environment: test' in deploy and 'group: projectpulse-deploy-test' in deploy
print('PROTECTED_TEST_FLOWHIVE_RECOVERY_SCOPE=PASS deployment_controller=unchanged production=unchanged')
