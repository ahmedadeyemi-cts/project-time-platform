"""Exact automatic first-draft opt-in scope, with unchanged deployment authority."""
import os
from pathlib import Path
import subprocess

# A local equivalent commit is acceptable only with this identical trusted-main tree.
BASE = os.getenv('FLOWHIVE_AUTOMATIC_BASE', '045b66ca01baa68b2f5b3f6eb9e063c23c335981')
TREE = 'ef5fd9d50ce97ecabccd47d761ace16d494703cd'
def git(*args): return subprocess.check_output(['git', *args]).decode()
assert git('rev-parse', BASE + '^{tree}').strip() == TREE
assert git('merge-base', BASE, 'HEAD').strip() == BASE
manifest = Path('.github/flowhive-auto-first-draft-files.txt').read_text().splitlines()
assert manifest == sorted(set(manifest))
changed = set(git('diff', '--name-only', BASE).splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
assert changed == set(manifest), f'Unexpected preparation scope: {changed ^ set(manifest)}'
registry = 'scripts/release-test/validate-protected-test-controller-branches.sh'
block = '''if [[ "$HEAD_BRANCH" == feature/flowhive-auto-first-draft-20260920 ]]; then
  python3 tests/flowhive-psa-automatic-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
elif'''
assert Path(registry).read_text().replace(block, 'if', 1) == git('show', BASE + ':' + registry)
workflow = '.github/workflows/flowhive-psa-release-control-ci.yml'
block = '''          if [[ "$GITHUB_HEAD_REF" == feature/flowhive-auto-first-draft-20260920 ]]; then
            python3 tests/flowhive-psa-automatic-scope.py
          elif'''
assert Path(workflow).read_text().replace(block, '          if', 1) == git('show', BASE + ':' + workflow)
for path, added, restored in [
    ('scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
     'if [[ "$HEAD_BRANCH" == feature/flowhive-auto-first-draft-20260920 ]]; then\n  python3 tests/flowhive-psa-automatic-scope.py\n  exit 0\nelif', 'if'),
    ('.github/workflows/module-management-owner-drawer-ci.yml',
     '          if [[ "$HEAD_BRANCH" == feature/flowhive-auto-first-draft-20260920 ]]; then\n            python3 tests/flowhive-psa-automatic-scope.py\n            exit 0\n          fi\n', ''),
    ('tests/validate-celar-ai-pr630-consolidated.mjs',
     "const flowHiveAutomaticMode = branchName === 'feature/flowhive-auto-first-draft-20260920';\nif (flowHiveAutomaticMode) childProcess.execFileSync('python3', ['tests/flowhive-psa-automatic-scope.py'], {stdio:'inherit'});\nconst scopedCompatibilityMode = flowHiveAutomaticMode ||", 'const scopedCompatibilityMode =')]:
    assert Path(path).read_text().replace(added, restored, 1) == git('show', BASE + ':' + path), path
for path in ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs']:
    assert Path(path).read_bytes() == subprocess.check_output(['git', 'show', BASE + ':' + path]), path
print('FLOWHIVE_AUTOMATIC_SCOPE=PASS release_authority=unchanged')
