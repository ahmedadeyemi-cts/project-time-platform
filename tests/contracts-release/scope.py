"""Exact PR1205 source/migration registration; deployment authority is unchanged."""
from pathlib import Path
import hashlib
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = '6c70385d'
BRANCH = 'codex/module060-approval-contract-funding'
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

def require(value, message):
    if not value:
        raise RuntimeError(message)

def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], text=True, timeout=60).strip()

def verify_paths(paths, manifest):
    require(set(paths) == set(manifest['files']), 'Unexpected or missing release files')
    require(not set(paths) & FROZEN, 'Deployment authority cannot be changed by this registration')

def verify_hash(data, digest):
    require(hashlib.sha256(data).hexdigest() == digest, 'Registered source content changed')

def main():
    manifest = json.loads((ROOT/'tests/contracts-release/manifest.json').read_text())
    require(manifest['base'] == git('rev-parse', BASE), 'Wrong registered main baseline')
    require((os.getenv('GITHUB_HEAD_REF') or git('branch', '--show-current')) == BRANCH, 'Wrong source branch')
    require(str(os.getenv('PR_NUMBER', '1205')) == '1205', 'Wrong pull request')
    require(manifest['files'] == sorted(set(manifest['files'])), 'Manifest paths must be unique and sorted')
    subprocess.run(['git', '-C', str(ROOT), 'merge-base', '--is-ancestor', BASE, 'HEAD'], check=True)
    verify_paths(git('diff', '--name-only', BASE, 'HEAD').splitlines(), manifest)
    for path in FROZEN:
        require((ROOT/path).read_text().strip() == git('show', f'{BASE}:{path}'), 'Frozen deployment authority changed: '+path)
    for path, digest in manifest['sha256'].items():
        verify_hash((ROOT/path).read_bytes(), digest)
    require(set(manifest['sha256']) == set(manifest['files']) - {'tests/contracts-release/manifest.json'}, 'Every source file must be hash-bound')
    for path in manifest['files']:
        require(git('ls-tree','HEAD','--',path).split()[0] in ('100644','100755'), 'Symlinks and submodules are prohibited')
    subprocess.run(['git','-C',str(ROOT),'diff','--check',BASE,'HEAD'],check=True)
    print('CONTRACT_APPROVAL_FUNDING_EXACT_SCOPE=PASS; deployment_authority=UNCHANGED')

if __name__ == '__main__':
    main()
