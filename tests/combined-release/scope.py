"""Exact combined approval/FlowHive source registration; does not authorize deployment."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = '5c371854343cc8f5c26f2ae26cc30c904edc35ea'
BRANCH = 'codex/approval-routing-bulk-review'
REPOSITORY = 'ahmedadeyemi-cts/project-time-platform'
MANIFEST = 'tests/combined-release/manifest.json'
CI_DISPATCH = {'scripts/release-test/validate-protected-test-controller-branches.sh',
               'scripts/release-test/validate-module025-governed-release.sh'}
MIGRATION_BUILDER = 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh'

def require(value, message):
    if not value: raise RuntimeError(message)

def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], timeout=60)

def verify_migration_delta(before, after):
    registration = json.loads((ROOT/'tests/combined-release/migration-registration.json').read_text())
    require(registration['path'] == MIGRATION_BUILDER, 'Wrong migration builder')
    for anchor, addition in registration['patches']:
        require(before.count(anchor) == 1, 'Ambiguous migration insertion')
        before = before.replace(anchor, anchor + addition, 1)
    require(before == after, 'Migration runner or existing authority changed')

def frozen(path):
    if path in CI_DISPATCH or path == MIGRATION_BUILDER: return False
    if path.startswith('scripts/release-test/'): return True
    if path.startswith('.github/') and not path.startswith('.github/workflows/'): return True
    if path.startswith('.github/workflows/'):
        return any(word in path for word in ('deploy', 'protected-uat-control', 'installed-acceptance', 'oracle-test-runtime'))
    return False

def verify_identity(branch, repository, base, number):
    require((branch, repository, base, str(number)) == (BRANCH, REPOSITORY, 'main', '1213'), 'Wrong source identity')

def verify_paths(paths, manifest):
    require(manifest['base'] == BASE, 'Wrong fixed baseline')
    require(manifest['files'] == sorted(set(manifest['files'])), 'Paths must be sorted and unique')
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
                    os.getenv('PR_NUMBER', '1213'))
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
    verify_migration_delta(git('show', f'{BASE}:{MIGRATION_BUILDER}').decode(), (ROOT/MIGRATION_BUILDER).read_text())
    subprocess.run(['git', '-C', str(ROOT), 'diff', '--check', BASE, 'HEAD'], check=True)
    print('COMBINED_APPROVAL_FLOWHIVE_EXACT_SOURCE=PASS; deployment_authorization=NONE; native_protections=REQUIRED')

if __name__ == '__main__': main()
