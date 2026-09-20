"""Exact sequential WBS/checkpoint/timer scope, with unchanged deployment authority."""
import os
from pathlib import Path
import subprocess

# A local equivalent commit is acceptable only with this identical trusted-main tree.
BASE = os.getenv('FLOWHIVE_SEQUENTIAL_BASE', '35a442bee3724d7d4e5865f4abc2aaa910d55326')
TREE = '33c6e1998b29050e0c14af43b6835c8320d4d465'
def git(*args): return subprocess.check_output(['git', *args]).decode()
assert git('rev-parse', BASE + '^{tree}').strip() == TREE
assert git('merge-base', BASE, 'HEAD').strip() == BASE
manifest = Path('.github/flowhive-sequential-wbs-files.txt').read_text().splitlines()
assert manifest == sorted(set(manifest))
changed = set(git('diff', '--name-only', BASE).splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
assert changed == set(manifest), f'Unexpected preparation scope: {changed ^ set(manifest)}'
registry = 'scripts/release-test/validate-protected-test-controller-branches.sh'
block = '''if [[ "$HEAD_BRANCH" == feature/flowhive-sequential-wbs-20260920 ]]; then
  python3 tests/flowhive-psa-sequential-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif'''
assert Path(registry).read_text().replace(block, 'if', 1) == git('show', BASE + ':' + registry)
workflow = '.github/workflows/flowhive-psa-release-control-ci.yml'
block = '''          if [[ "$GITHUB_HEAD_REF" == feature/flowhive-sequential-wbs-20260920 ]]; then
            python3 tests/flowhive-psa-sequential-scope.py
          elif'''
assert Path(workflow).read_text().replace(block, '          if', 1) == git('show', BASE + ':' + workflow)
for path in ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs']:
    assert Path(path).read_bytes() == subprocess.check_output(['git', 'show', BASE + ':' + path]), path
print('FLOWHIVE_SEQUENTIAL_SCOPE=PASS release_authority=unchanged')
