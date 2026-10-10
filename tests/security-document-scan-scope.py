"""Exact reviewed source scope; never authorizes deployment or alters its controller."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = '4c94c04a64af0d0925870dedb56bb4e0e48effb9'
BRANCH = 'fix/security-document-scan-evidence-20261010'
REPOSITORY = 'ahmedadeyemi-cts/project-time-platform'
MANIFEST = 'tests/security-document-scan-manifest.json'
ALLOWED = ['.github/workflows/flowhive-psa-release-control-ci.yml', '.github/workflows/laya-processed-source-ci.yml', '.github/workflows/pr1140-uat-recovery-ci.yml', '.github/workflows/pulse-document-integration-ci.yml', '.github/workflows/uat-migration-throttle-recovery-ci.yml', 'scripts/release-test/validate-module025-governed-release.sh', 'scripts/release-test/validate-protected-test-controller-branches.sh', 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentExtractionService.cs', 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentPipelineContracts.cs', 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentPipelineService.cs', 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs', 'src/backend/ProjectTime.Api/Modules/Module025CanonicalReferenceModule.cs', 'src/frontend/project-time-web/scripts/validate-module-011-private-document-pipeline.mjs', 'tests/FlowHiveDetailedPlannerTests/Program.cs', 'tests/ProjectTime.Api.AuthorizationTests/Program.cs', 'tests/ProjectTime.Api.AuthorizationTests/SecurityDocumentScanTests.cs', 'tests/flowhive-psa-admission.test.mjs', 'tests/security-document-scan-manifest.json', 'tests/security-document-scan-scope.py', 'tests/test-celar-sow-runtime-deadlines.py']
FROZEN = [
    '.github/workflows/projectpulse-deploy-test.yml',
    '.github/workflows/projectpulse-deploy-production.yml',
    '.github/workflows/module025-protected-uat-control.yml',
    '.github/workflows/deployment-concurrency-governance-ci.yml',
    'scripts/validate-deployment-concurrency-governance.mjs',
    'scripts/security/validate-repository-security-posture.py',
    '.github/flowhive-psa-protected-cutover.json',
    '.github/flowhive-psa-protected-test-approval.json',
    '.github/flowhive-psa-protected-test-candidate.json'
]


def require(value, message):
    if not value:
        raise ValueError(message)


def identity(branch, repository, base, number):
    require((branch, repository, base, str(number)) == (BRANCH, REPOSITORY, BASE, '1308'), 'Wrong review identity')


def paths(actual, expected):
    require(set(actual) == set(expected) and len(expected) == len(set(expected)), 'Unexpected or missing source path')
    for path in actual:
        require(not PurePosixPath(path).is_absolute() and '..' not in PurePosixPath(path).parts, 'Unsafe path')
        require(path not in FROZEN, 'Protected authority must remain byte-identical')


def content(data, digest, mode):
    require(mode in ('100644', '100755'), 'Links and submodules prohibited')
    require(hashlib.sha256(data).hexdigest() == digest, 'Source bytes differ from reviewed manifest')


def self_test():
    cases = [lambda: identity('wrong', REPOSITORY, BASE, 1308),
             lambda: identity(BRANCH, 'foreign/repo', BASE, 1308),
             lambda: identity(BRANCH, REPOSITORY, '0'*40, 1308),
             lambda: identity(BRANCH, REPOSITORY, BASE, 1309),
             lambda: paths(['a', 'extra'], ['a']), lambda: paths([], ['a']),
             lambda: paths(['../escape'], ['../escape']),
             lambda: paths([FROZEN[0]], [FROZEN[0]]),
             lambda: content(b'changed', hashlib.sha256(b'approved').hexdigest(), '100644'),
             lambda: content(b'approved', hashlib.sha256(b'approved').hexdigest(), '120000')]
    for case in cases:
        try:
            case()
        except ValueError:
            continue
        raise AssertionError('An unauthorized source mutation was accepted')
    print('SECURITY_DOCUMENT_SCAN_SCOPE_NEGATIVES=PASS checks=' + str(len(cases)))


def main(base):
    def git(*args):
        return subprocess.check_output(['git', '-C', str(ROOT), *args])
    identity(os.getenv('GITHUB_HEAD_REF', ''), os.getenv('GITHUB_REPOSITORY', REPOSITORY), base, os.getenv('PR_NUMBER', '1308'))
    if os.getenv('GITHUB_EVENT_PATH'):
        event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
        if event.get('pull_request'):
            pr = event['pull_request']
            identity(pr['head']['ref'], pr['head']['repo']['full_name'], pr['base']['sha'], event['number'])
            require(pr['base']['repo']['full_name'] == REPOSITORY, 'Wrong target repository')
    manifest = json.loads((ROOT / MANIFEST).read_text())
    require(manifest['base'] == BASE, 'Wrong fixed baseline')
    actual = git('diff', '--name-only', base, 'HEAD').decode().splitlines()
    require(manifest['files'] == ALLOWED, 'Only the exact registered repair paths are permitted')
    paths(actual, ALLOWED)
    require(set(manifest['sha256']) == set(actual) - {MANIFEST}, 'Every source file must be hash bound')
    for path in actual:
        require(not (ROOT/path).is_symlink(), 'Working file is a link')
        data = (ROOT/path).read_bytes()
        require(data == git('show', 'HEAD:'+path), 'Uncommitted source differs')
        if path != MANIFEST:
            mode = git('ls-tree', 'HEAD', '--', path).decode().split()[0]
            content(data, manifest['sha256'][path], mode)
    for path in FROZEN:
        require(git('diff', base, 'HEAD', '--', path) == b'', 'Protected authority changed: '+path)
    subprocess.run(['git', '-C', str(ROOT), 'diff', '--check', base, 'HEAD'], check=True)
    print('SECURITY_DOCUMENT_SCAN_EXACT_SCOPE=PASS deployment_authorization=NONE')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base')
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        main(args.base)
