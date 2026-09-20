"""Exact preparation/timer/archive scope, with unchanged deployment authority."""
import os
from pathlib import Path
import subprocess

# A local equivalent commit is acceptable only with this identical trusted-main tree.
BASE = os.getenv('FLOWHIVE_PREPARATION_BASE', '373b37e9c76e430355ed2dc5f71ec6a133ead3aa')
TREE = '9139b91b2f9d0f3bab76116e8f34be769fd2eac9'
def git(*args): return subprocess.check_output(['git', *args]).decode()
assert git('rev-parse', BASE + '^{tree}').strip() == TREE
assert git('merge-base', BASE, 'HEAD').strip() == BASE
manifest = Path('.github/flowhive-psa-preparation-files.txt').read_text().splitlines()
assert manifest == sorted(set(manifest))
changed = set(git('diff', '--name-only', BASE).splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
assert changed == set(manifest), f'Unexpected preparation scope: {changed ^ set(manifest)}'
registry = 'scripts/release-test/validate-protected-test-controller-branches.sh'
block = '''if [[ "$HEAD_BRANCH" == feature/flowhive-document-readiness-archive-20260920 ]]; then
  python3 tests/flowhive-psa-preparation-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif'''
assert Path(registry).read_text().replace(block, 'if', 1) == git('show', BASE + ':' + registry)
workflow = '.github/workflows/flowhive-psa-release-control-ci.yml'
block = '''          if [[ "$GITHUB_HEAD_REF" == feature/flowhive-document-readiness-archive-20260920 ]]; then
            python3 tests/flowhive-psa-preparation-scope.py
          elif'''
assert Path(workflow).read_text().replace(block, '          if', 1) == git('show', BASE + ':' + workflow)
for path in ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs']:
    assert Path(path).read_bytes() == subprocess.check_output(['git', 'show', BASE + ':' + path]), path
print('FLOWHIVE_PREPARATION_SCOPE=PASS release_authority=unchanged')
