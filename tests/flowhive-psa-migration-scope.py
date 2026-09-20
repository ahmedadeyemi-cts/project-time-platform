"""Exact private-network migration wrapper repair, with unchanged deployment authority."""
import os
from pathlib import Path
import subprocess

# A local equivalent commit is acceptable only with this identical trusted-main tree.
BASE = os.getenv('FLOWHIVE_MIGRATION_BASE', '52fd4e2debf20bcbb34ea2f68199d4140aa01eea')
TREE = '1affd3b1e5c6764f3c4b0f25af1f66dba92eb0bd'
def git(*args): return subprocess.check_output(['git', *args]).decode()
assert git('rev-parse', BASE + '^{tree}').strip() == TREE
assert git('merge-base', BASE, 'HEAD').strip() == BASE
manifest = Path('.github/flowhive-migration-runner-files.txt').read_text().splitlines()
assert manifest == sorted(set(manifest))
changed = set(git('diff', '--name-only', BASE).splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
assert changed == set(manifest), f'Unexpected preparation scope: {changed ^ set(manifest)}'
registry = 'scripts/release-test/validate-protected-test-controller-branches.sh'
block = '''if [[ "$HEAD_BRANCH" == fix/flowhive-migration-runner-20260920 ]]; then
  python3 tests/flowhive-psa-migration-scope.py
  python3 tests/flowhive-migration-package.test.py
  node tests/validate-systemwide-image-build-controller.mjs
elif'''
assert Path(registry).read_text().replace(block, 'if', 1) == git('show', BASE + ':' + registry)
workflow = '.github/workflows/flowhive-psa-release-control-ci.yml'
block = '''          if [[ "$GITHUB_HEAD_REF" == fix/flowhive-migration-runner-20260920 ]]; then
            python3 tests/flowhive-psa-migration-scope.py
          elif'''
assert Path(workflow).read_text().replace(block, '          if', 1) == git('show', BASE + ':' + workflow)
for path, added, restored in [
    ('scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
     'if [[ "$HEAD_BRANCH" == fix/flowhive-migration-runner-20260920 ]]; then\n  python3 tests/flowhive-psa-migration-scope.py\n  exit 0\nelif', 'if'),
    ('.github/workflows/module-management-owner-drawer-ci.yml',
     '          if [[ "$HEAD_BRANCH" == fix/flowhive-migration-runner-20260920 ]]; then\n            python3 tests/flowhive-psa-migration-scope.py\n            exit 0\n          fi\n', ''),
    ('tests/validate-celar-ai-pr630-consolidated.mjs',
     "const flowHiveMigrationMode = branchName === 'fix/flowhive-migration-runner-20260920';\nif (flowHiveMigrationMode) childProcess.execFileSync('python3', ['tests/flowhive-psa-migration-scope.py'], {stdio:'inherit'});\nconst scopedCompatibilityMode = flowHiveMigrationMode ||", 'const scopedCompatibilityMode =')]:
    assert Path(path).read_text().replace(added, restored, 1) == git('show', BASE + ':' + path), path
for path in ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/module025-protected-uat-control.yml', '.github/flowhive-psa-protected-test-candidate.json',
             '.github/flowhive-psa-release-control-files.txt', 'scripts/release-test/flowhive-psa-admission.mjs']:
    assert Path(path).read_bytes() == subprocess.check_output(['git', 'show', BASE + ':' + path]), path
print('FLOWHIVE_MIGRATION_SCOPE=PASS release_authority=unchanged')

builder = 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh'
def entrypoint(source): return source.split("<<'ENTRYPOINT'\n",1)[1].split('\nENTRYPOINT\n',1)[0]
assert entrypoint(Path(builder).read_text()) == entrypoint(git('show', BASE+':'+builder)), 'Private-network SQL and verification must remain unchanged'
