from pathlib import Path
import subprocess

BRANCH='fix/flowhive-candidate-uat-acceptance-20260926'
EXPECTED=sorted({
    '.github/workflows/flowhive-psa-release-control-ci.yml',
    '.github/workflows/module-management-owner-drawer-ci.yml',
    '.github/workflows/projectpulse-deploy-test.yml',
    'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
    'scripts/release-test/validate-protected-test-controller-branches.sh',
    'tests/flowhive-candidate-uat-acceptance-scope.py',
    'tests/flowhive-psa-admission.test.mjs',
    'tests/validate-celar-ai-pr630-consolidated.mjs',
})

def git(*args):
    return subprocess.check_output(['git',*args],text=True).strip()
subprocess.run(['git','fetch','origin','main','--no-tags'],check=True,stdout=subprocess.DEVNULL)
base=git('merge-base','origin/main','HEAD')
actual=sorted(filter(None,git('diff','--name-only',f'{base}...HEAD').splitlines()))
assert actual==EXPECTED, f'unexpected scope: {sorted(set(actual)^set(EXPECTED))}'
workflow=Path('.github/workflows/projectpulse-deploy-test.yml').read_text()
assert 'candidate_review_required' in workflow
assert "FLOWHIVE_RESULT_MODE='review_candidate'" in workflow
for f in [
    'scripts/release-test/validate-protected-test-controller-branches.sh',
    '.github/workflows/flowhive-psa-release-control-ci.yml',
    'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
    '.github/workflows/module-management-owner-drawer-ci.yml',
]:
    assert BRANCH in Path(f).read_text(), f'{BRANCH} not registered in {f}'
print('FLOWHIVE_CANDIDATE_UAT_ACCEPTANCE_SCOPE=PASS')
