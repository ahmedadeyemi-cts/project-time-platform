"""Exact security UAT follow-up source registration; does not authorize deployment."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = '9796b4cb4f090d78cfb7cd275bc45be2329eb173'
BRANCH = 'fix/security-audit-followup-20260929'
REPOSITORY = 'ahmedadeyemi-cts/project-time-platform'
MANIFEST = 'tests/security-audit-followup/manifest.json'
CI_DISPATCH = {'scripts/release-test/validate-protected-test-controller-branches.sh',
               'scripts/release-test/validate-module025-governed-release.sh'}
CI_WORKFLOWS = {'.github/workflows/'+name for name in ('flowhive-psa-release-control-ci.yml', 'flowhive-enterprise-psa-ci.yml', 'pr1140-uat-recovery-ci.yml', 'pr1151-uat-supersession-ci.yml', 'uat-migration-throttle-recovery-ci.yml')}

ALLOWED = {'src/backend/ProjectTime.Api/BoundedOfficeInput.cs', 'tests/ProjectTime.Api.AuthorizationTests/Program.cs', 'tests/validate-celar-ai-pr630-consolidated-legacy.mjs', '.github/workflows/uat-migration-throttle-recovery-ci.yml', 'src/backend/ProjectTime.Api/Modules/CrmErpOAuthPersistence.cs', 'tests/SecurityBoundaryTests/Program.cs', 'tests/security-audit-followup/test_scope.py', 'src/backend/ProjectTime.Api/Modules/ContractsPrepaidManagementModule.cs', 'scripts/release-test/validate-protected-test-controller-branches.sh', 'src/backend/ProjectTime.Api/Modules/CrmErpIntegrationModule.cs', 'scripts/release-test/validate-module025-governed-release.sh', '.github/workflows/pr1151-uat-supersession-ci.yml', '.github/workflows/flowhive-psa-release-control-ci.yml', 'src/backend/ProjectTime.Api/Modules/CrmProviderOperationLease.cs', 'tests/security-audit-followup/scope.py', 'tests/ProjectTime.Api.AuthorizationTests/CrmCredentialConcurrencyTests.cs', 'tests/security-audit-followup/manifest.json', 'tests/security-release/admission_fixture.py', 'tests/ProjectTime.Api.AuthorizationTests/OfficeRangeBudgetTests.cs', '.github/workflows/flowhive-enterprise-psa-ci.yml', '.github/workflows/pr1140-uat-recovery-ci.yml', 'tests/validate-celar-ai-pr630-consolidated.mjs'}

def require(value, message):
    if not value: raise RuntimeError(message)

def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], timeout=60)

def frozen(path):
    if path in CI_DISPATCH | CI_WORKFLOWS: return False
    return path.startswith('scripts/release-test/') or path.startswith('.github/')

def verify_identity(branch, repository, base, number):
    require((branch, repository, base, str(number)) == (BRANCH, REPOSITORY, 'main', '1218'), 'Wrong source identity')

def verify_paths(paths, manifest):
    require(manifest['base'] == BASE, 'Wrong fixed baseline')
    require(manifest['files'] == sorted(set(manifest['files'])), 'Paths must be sorted and unique')
    require(set(paths) == ALLOWED, 'Only the exact reviewed source and registration are permitted')
    require(set(paths) == set(manifest['files']), 'Unexpected or missing source files')
    require(set(manifest['sha256']) == set(paths) - {MANIFEST}, 'Every source file must be hash-bound')
    for path in paths:
        require(not PurePosixPath(path).is_absolute() and '..' not in PurePosixPath(path).parts, 'Unsafe source path')
        require(not frozen(path), 'Deployment authority is frozen: ' + path)

def verify_content(data, digest, mode):
    require(mode in ('100644', '100755'), 'Symlinks and submodules are prohibited')
    require(hashlib.sha256(data).hexdigest() == digest, 'Registered source bytes changed')

def main():
    verify_identity(os.getenv('GITHUB_HEAD_REF') or git('branch', '--show-current').decode().strip(),
                    os.getenv('GITHUB_REPOSITORY', REPOSITORY), os.getenv('GITHUB_BASE_REF', 'main'),
                    os.getenv('PR_NUMBER', '1218'))
    if os.getenv('GITHUB_EVENT_PATH'):
        event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
        if event.get('pull_request'):
            pr = event['pull_request']
            verify_identity(pr['head']['ref'], pr['head']['repo']['full_name'], pr['base']['ref'], event['number'])
            require(pr['base']['repo']['full_name'] == REPOSITORY, 'Wrong target repository')
    subprocess.run(['git', '-C', str(ROOT), 'merge-base', '--is-ancestor', BASE, 'HEAD'], check=True)
    manifest = json.loads((ROOT / MANIFEST).read_text())
    paths = git('diff', '--name-only', BASE, 'HEAD').decode().splitlines()
    verify_paths(paths, manifest)
    for path in paths:
        entry = git('ls-tree', 'HEAD', '--', path).decode().split()
        require(entry and entry[0] in ('100644', '100755'), 'Missing or unsafe source entry')
        require(not (ROOT/path).is_symlink(), 'Working source is a symlink')
        data = (ROOT/path).read_bytes()
        require(data == git('show', f'HEAD:{path}'), 'Working tree differs from committed content: '+path)
        if path != MANIFEST: verify_content(data, manifest['sha256'][path], entry[0])
    for path in git('ls-tree', '-r', '--name-only', BASE).decode().splitlines():
        if frozen(path):
            require((ROOT/path).is_file() and not (ROOT/path).is_symlink()
                    and (ROOT/path).read_bytes() == git('show', f'{BASE}:{path}'), 'Frozen deployment authority changed: '+path)
    subprocess.run(['git', '-C', str(ROOT), 'diff', '--check', BASE, 'HEAD'], check=True)
    print('SECURITY_AUDIT_FOLLOWUP_EXACT_SOURCE=PASS; deployment_authorization=NONE; native_protections=REQUIRED')

if __name__ == '__main__': main()
