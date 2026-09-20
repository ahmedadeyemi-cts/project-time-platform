"""Exact feature scope; existing deployment authority remains byte-for-byte unchanged."""
from pathlib import Path
import subprocess
BASE = '18f192c9d3737e630308321e8067dedfc580f0e9'
def git(*args): return subprocess.check_output(['git', *args]).decode()
manifest = Path('.github/flowhive-psa-workspace-files.txt').read_text().splitlines()
assert manifest == sorted(set(manifest)), 'Manifest must be exact, sorted and unique'
assert git('merge-base', BASE, 'HEAD').strip() == BASE
changed = set(git('diff', '--name-only', BASE).splitlines())
changed |= set(git('ls-files', '--others', '--exclude-standard').splitlines())
assert changed == set(manifest), f'Unexpected feature scope: {changed ^ set(manifest)}'
registry = 'scripts/release-test/validate-protected-test-controller-branches.sh'
block = '''if [[ "$HEAD_BRANCH" == feature/flowhive-psa-team-workspace-20260919 ]]; then
  python3 tests/flowhive-psa-workspace-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif'''
assert Path(registry).read_text().replace(block,'if',1) == git('show', BASE+':'+registry)
for path in ['.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt',
             '.github/workflows/projectpulse-deploy-test.yml',
             '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml',
             'scripts/release-test/flowhive-psa-admission.mjs']:
    assert Path(path).read_text() == git('show',BASE+':'+path), path
print('FLOWHIVE_WORKSPACE_SCOPE=PASS release_authority=unchanged')

workflow = '.github/workflows/flowhive-psa-release-control-ci.yml'
block = '          if [[ "$GITHUB_HEAD_REF" == feature/flowhive-psa-team-workspace-20260919 ]]; then\n            python3 tests/flowhive-psa-workspace-scope.py\n          elif'
assert Path(workflow).read_text().replace(block,'          if',1) == git('show',BASE+':'+workflow)
