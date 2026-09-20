"""Exact sequential WBS/checkpoint/timer scope, with unchanged deployment authority."""
import os
from pathlib import Path
import subprocess

# A local equivalent commit is acceptable only with this identical trusted-main tree.
BASE = os.getenv('FLOWHIVE_SEQUENTIAL_BASE', '3f55a0f62f44587d234a09a9f0ceabbe501da8c5')
TREE = '7fa63f26ccef8aeef6c4d09bcdc4eb91f67704cf'
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
for path, added, restored in [
    ('scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
     'if [[ "$HEAD_BRANCH" == feature/flowhive-sequential-wbs-20260920 ]]; then\n  python3 tests/flowhive-psa-sequential-scope.py\n  exit 0\nelif', 'if'),
    ('.github/workflows/module-management-owner-drawer-ci.yml',
     '          if [[ "$HEAD_BRANCH" == feature/flowhive-sequential-wbs-20260920 ]]; then\n            python3 tests/flowhive-psa-sequential-scope.py\n            exit 0\n          fi\n', ''),
    ('tests/validate-celar-ai-pr630-consolidated.mjs',
     "const flowHiveSequentialMode = branchName === 'feature/flowhive-sequential-wbs-20260920';\nif (flowHiveSequentialMode) childProcess.execFileSync('python3', ['tests/flowhive-psa-sequential-scope.py'], {stdio:'inherit'});\nconst scopedCompatibilityMode = flowHiveSequentialMode ||", 'const scopedCompatibilityMode =')]:
    assert Path(path).read_text().replace(added, restored, 1) == git('show', BASE + ':' + path), path
for path in ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs']:
    assert Path(path).read_bytes() == subprocess.check_output(['git', 'show', BASE + ':' + path]), path
print('FLOWHIVE_SEQUENTIAL_SCOPE=PASS release_authority=unchanged')
