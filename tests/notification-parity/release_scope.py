"""PR1204 exact source/migration registration; not deployment or live-delivery authorization."""
from pathlib import Path
import hashlib
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = '3f30bf1c6b56a5fe49116834e16b4ec2c1e13ea3'
BRANCH = 'feature/module065-email-teams-notification-parity-20260928'
MANIFEST = ROOT / 'tests/notification-parity/release-registration.json'
FROZEN = {
    '.github/workflows/projectpulse-deploy-test.yml',
    '.github/workflows/projectpulse-deploy-production.yml',
    '.github/workflows/module025-protected-uat-control.yml',
    '.github/flowhive-psa-protected-test-candidate.json',
    '.github/flowhive-psa-release-control-files.txt',
    '.github/workflows/flowhive-psa-installed-acceptance.yml',
    'scripts/release-test/flowhive-psa-admission.mjs',
    'scripts/release-test/run-project-planning-document-authority-migration-job.sh',
}

def require(condition, message):
    if not condition:
        raise RuntimeError(message)

def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], text=True, timeout=60)

def identity(base, branch, pr):
    require(base == BASE and branch == BRANCH and str(pr) == '1204', 'Incorrect release source identity')

def changed_files(changed, manifest):
    require(set(changed) == set(manifest['files']), 'Unreviewed source paths: ' + str(sorted(set(changed) ^ set(manifest['files']))))
    require(manifest['files'] == sorted(set(manifest['files'])), 'Source manifest must be sorted and unique')
    require(not set(changed) & FROZEN, 'Frozen release authority changed')

def amended(before, after, replacements, name):
    expected = before
    for item in replacements:
        require(expected.count(item['anchor']) == 1, f'Missing or ambiguous original boundary in {name}')
        expected = expected.replace(item['anchor'], item['replacement'], 1)
    require(after == expected, f'Unreviewed control or migration-runner change: {name}')

def content(actual, expected, name):
    require(hashlib.sha256(actual).hexdigest() == expected, f'Reviewed content changed: {name}')

def main():
    manifest = json.loads(MANIFEST.read_text())
    identity(manifest['base'], os.environ.get('GITHUB_HEAD_REF') or git('branch', '--show-current').strip(), os.environ.get('PR_NUMBER', manifest['prNumber']))
    require(manifest['branch'] == BRANCH and set(manifest['frozen']) == FROZEN, 'Manifest widened the frozen release authority')
    subprocess.run(['git', '-C', str(ROOT), 'merge-base', '--is-ancestor', BASE, 'HEAD'], check=True, timeout=30)
    changed = git('diff', '--name-only', BASE, 'HEAD').splitlines()
    changed_files(changed, manifest)
    for path in FROZEN:
        require((ROOT/path).read_text() == git('show', f'{BASE}:{path}'), f'Frozen deployment authority changed: {path}')
    for path, replacements in manifest['amendments'].items():
        amended(git('show', f'{BASE}:{path}'), (ROOT/path).read_text(), replacements, path)
    for path, digest in manifest['contentSha256'].items():
        content((ROOT/path).read_bytes(), digest, path)
    for path in changed:
        entry = git('ls-tree', 'HEAD', '--', path).split()
        require(entry and entry[0] in ('100644', '100755'), 'Source cannot be a symlink/submodule: '+path)
    subprocess.run(['git', '-C', str(ROOT), 'diff', '--check', BASE, 'HEAD'], check=True, timeout=30)
    print(f'NOTIFICATION_PARITY_EXACT_RELEASE_SCOPE=PASS; source_files={len(changed)}; active_test_and_production_controllers=unchanged; deployment_authorized=false')

if __name__ == '__main__':
    main()
