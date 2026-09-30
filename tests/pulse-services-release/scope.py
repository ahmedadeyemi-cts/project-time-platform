"""Exact security UAT follow-up source registration; does not authorize deployment."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
BASE = 'c8ac122653b2d948343727815dfc3279d98c9cc6'
BRANCH = 'feature/pulse-services-cutover-laya-20260930'
REPOSITORY = 'ahmedadeyemi-cts/project-time-platform'
MANIFEST = 'tests/pulse-services-release/manifest.json'
CI_DISPATCH = {'scripts/release-test/validate-protected-test-controller-branches.sh',
               'scripts/release-test/validate-module025-governed-release.sh'}
CI_WORKFLOWS = {'.github/workflows/'+name for name in ('flowhive-enterprise-psa-ci.yml', 'flowhive-psa-release-control-ci.yml', 'pr1140-uat-recovery-ci.yml', 'pr1151-uat-supersession-ci.yml', 'uat-migration-throttle-recovery-ci.yml', 'pulse-services-ci.yml')}

ALLOWED = {'deployment/pulse-services/clamd.conf', 'deployment/pulse-services/laya-model.json', 'deployment/pulse-services/sandbox.c', 'tests/validate-celar-ai-pr630-consolidated-legacy.mjs', 'scripts/release-test/validate-module025-governed-release.sh', '.github/workflows/uat-migration-throttle-recovery-ci.yml', 'tests/pulse_services/native_laya.py', 'deployment/pulse-services/README.md', 'tests/security-release/admission_fixture.py', 'src/backend/ProjectTime.Api/Ai/PulseLayaServiceOptions.cs', 'deployment/pulse-services/Dockerfile.laya', '.github/workflows/pulse-services-ci.yml', '.github/workflows/flowhive-psa-release-control-ci.yml', 'deployment/pulse-services/docs_wsgi.py', 'deployment/pulse-services/download_model.py', 'tests/laya/processed-source-scope.py', 'tests/pulse_services/native_isolation.py', 'deployment/pulse-services/laya_protocol.py', 'deployment/pulse-services/laya_wsgi.py', 'deployment/pulse-services/dependency-remediation.json', 'deployment/pulse-services/scanner_start.py', 'src/backend/ProjectTime.Api/Ai/PulseAiExternalHttpsRuntimePolicy.cs', 'deployment/pulse-services/Dockerfile.documents', 'tests/pulse-services-release/manifest.json', '.github/workflows/flowhive-enterprise-psa-ci.yml', 'tests/validate-celar-ai-pr630-consolidated.mjs', 'deployment/pulse-services/laya_worker.py', 'deployment/pulse-services/Dockerfile.scanner', 'tests/PulseServiceCutoverTests/PulseServiceCutoverTests.csproj', 'tests/pulse_services/test_cutover.py', 'deployment/pulse-services/acceptance.py', 'deployment/pulse-services/gunicorn.conf.py', 'deployment/pulse-services/cutover.py', 'tests/PulseServiceCutoverTests/Program.cs', 'src/backend/ProjectTime.Api/Ai/PulseDocumentReadinessPolicy.cs', 'src/backend/ProjectTime.Api/Ai/CelarAiRealProbeService.cs', 'deployment/pulse-services/requirements.lock', 'tests/laya/backend/Fakes.cs', 'deployment/pulse-services/requirements.in', 'tests/pulse-services-release/test_scope.py', 'deployment/pulse-services/test_resources.py', 'deployment/pulse-services/base-images.json', 'scripts/release-test/validate-protected-test-controller-branches.sh', 'deployment/pulse-services/proposed-test-cutover.yml', 'deployment/pulse-services/laya-source-provenance.json', '.github/workflows/pr1140-uat-recovery-ci.yml', 'src/backend/ProjectTime.Api/Modules/LayaDecisionModule.cs', 'deployment/pulse-services/runtime.py', '.github/workflows/pr1151-uat-supersession-ci.yml', 'tests/pulse-services-release/scope.py', 'src/backend/ProjectTime.Api/Ai/LayaDecisionTransport.cs', 'deployment/pulse-services/Dockerfile.laya-gateway', 'tests/pulse_services/test_contracts.py'}

def require(value, message):
    if not value: raise RuntimeError(message)

def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], timeout=60)

def frozen(path):
    if path in CI_DISPATCH | CI_WORKFLOWS or path.startswith('deployment/pulse-services/') or path == 'src/backend/ProjectTime.Api/Modules/LayaDecisionModule.cs': return False
    return (path.startswith('scripts/release-test/') or path.startswith('.github/')
            or path.startswith('database/') or path.startswith('deployment/')
            or path.startswith('src/backend/ProjectTime.Api/Modules/')
            or path == 'src/backend/ProjectTime.Api/Program.cs')

def verify_identity(branch, repository, base, number):
    require((branch, repository, base, str(number)) == (BRANCH, REPOSITORY, 'main', '1222'), 'Wrong source identity')

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
                    os.getenv('PR_NUMBER', '1222'))
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
    print('PULSE_SERVICES_EXACT_SOURCE=PASS; deployment_authorization=NONE; native_protections=REQUIRED')

if __name__ == '__main__': main()
