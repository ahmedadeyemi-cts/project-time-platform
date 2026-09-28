"""PR1209 exact source inventory. This does not authorize a deployment."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = 'a562371a0bbed74e881c40a4c246928dac9ec72e'
IMPLEMENTATION = '1c0f5a392384df5ef9c6f0895cfd716ef4e3ac05'
BRANCH = 'fix/security-team-findings-20260928'
REPOSITORY = 'ahmedadeyemi-cts/project-time-platform'
MANIFEST = 'tests/security-release/manifest.json'
CI_DISPATCH = {
    'scripts/release-test/validate-protected-test-controller-branches.sh',
    'scripts/release-test/validate-module025-governed-release.sh',
}


def require(value, message):
    if not value:
        raise RuntimeError(message)


def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], timeout=60)


def deployment_control(path):
    if path == '.github/workflows/validate-admin-experience-008-009-test-deployment.yml':
        return False
    return (path.startswith('deployment/')
            or (path.startswith('scripts/release-test/') and path not in CI_DISPATCH)
            or path == '.github/CODEOWNERS'
            or (path.startswith('.github/') and not path.startswith('.github/workflows/'))
            or (path.startswith('.github/workflows/') and
                any(word in path for word in ('deploy', 'protected-uat-control', 'installed-acceptance', 'oracle-test-runtime'))))


def verify_identity(branch, repository, base, number):
    require(branch == BRANCH, 'Wrong source branch')
    require(repository == REPOSITORY, 'Wrong source repository')
    require(base == 'main', 'Wrong PR base branch')
    require(str(number) == '1209', 'Wrong pull request')


def verify_paths(paths, manifest):
    require(manifest['base'] == BASE and manifest['implementation'] == IMPLEMENTATION, 'Wrong fixed baseline')
    require(manifest['files'] == sorted(set(manifest['files'])), 'Paths must be sorted and unique')
    require(set(paths) == set(manifest['files']), 'Unexpected or missing source files')
    hashes = {entry['path']: entry['sha256'] for entry in manifest['contentHashes']}
    require(len(hashes) == len(manifest['contentHashes']), 'Duplicate content hash entries')
    require(set(hashes) == set(paths) - {MANIFEST}, 'Every source file must be hash-bound')
    for path in paths:
        require(not PurePosixPath(path).is_absolute() and '..' not in PurePosixPath(path).parts, 'Unsafe source path')


def verify_content(data, digest, mode, frozen=None):
    require(mode in ('100644', '100755'), 'Symlinks and submodules are prohibited')
    require(hashlib.sha256(data).hexdigest() == digest, 'Registered source bytes changed')
    if frozen is not None:
        require(data == frozen, 'Deployment control changed after the security implementation')


def main():
    branch = os.getenv('GITHUB_HEAD_REF') or git('branch', '--show-current').decode().strip()
    verify_identity(branch, os.getenv('GITHUB_REPOSITORY', REPOSITORY),
                    os.getenv('GITHUB_BASE_REF', 'main'), os.getenv('PR_NUMBER', '1209'))
    event_path = os.getenv('GITHUB_EVENT_PATH')
    if event_path:
        event = json.loads(Path(event_path).read_text())
        pr = event.get('pull_request')
        if pr:
            verify_identity(pr['head']['ref'], pr['head']['repo']['full_name'], pr['base']['ref'], event['number'])
            require(pr['base']['repo']['full_name'] == REPOSITORY, 'Wrong target repository')
    subprocess.run(['git', '-C', str(ROOT), 'merge-base', '--is-ancestor', IMPLEMENTATION, 'HEAD'], check=True)
    manifest = json.loads((ROOT / MANIFEST).read_text())
    paths = git('diff', '--name-only', BASE, 'HEAD').decode().splitlines()
    verify_paths(paths, manifest)
    hashes = {entry['path']: entry['sha256'] for entry in manifest['contentHashes']}
    for path in paths:
        entry = git('ls-tree', 'HEAD', '--', path).decode().split()
        require(entry and entry[0] in ('100644', '100755'), 'Missing or unsafe source entry: ' + path)
        require(not (ROOT / path).is_symlink(), 'Working source is a symlink')
        data = (ROOT / path).read_bytes()
        require(data == git('show', f'HEAD:{path}'), 'Working source differs from checked-out commit: ' + path)
        if path != MANIFEST:
            frozen = git('show', f'{IMPLEMENTATION}:{path}') if deployment_control(path) else None
            verify_content(data, hashes[path], entry[0], frozen)
    # Freeze authority even when a path was not in the proposed change inventory.
    for path in git('ls-tree', '-r', '--name-only', IMPLEMENTATION).decode().splitlines():
        if deployment_control(path):
            require((ROOT / path).is_file() and (ROOT / path).read_bytes() == git('show', f'{IMPLEMENTATION}:{path}'),
                    'Frozen deployment control changed: ' + path)
    subprocess.run(['git', '-C', str(ROOT), 'diff', '--check', BASE, 'HEAD'], check=True)
    print('SECURITY_PR1209_EXACT_SOURCE=PASS; deployment_authorization=NONE; native_protections=REQUIRED')


if __name__ == '__main__':
    main()
