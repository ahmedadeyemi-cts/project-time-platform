"""Exact PR1140 scope; no deployment authority or live consent is granted here."""
from pathlib import Path
import json
import os
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
BASE = 'dc7297037c77a711276c7a36cd0048b4b54c6035'
BRANCH = 'feature/module025-service-scope-20260921'
MANIFEST = '.github/module025-service-scope-files.txt'
REGISTRATIONS = [['scripts/ci/validate-celar-ai-enterprise-source-boundary.sh', 'if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then', 'if [[ "$HEAD_BRANCH" == feature/module025-service-scope-20260921 ]]; then\n  python3 tests/service-scope/release_scope.py\n  exit 0\nfi\n\nif [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then'], ['scripts/release-test/validate-protected-test-controller-branches.sh', 'if [[ "$HEAD_BRANCH" == fix/module064-generation-sequence-20260921 ]]; then', 'if [[ "$HEAD_BRANCH" == feature/module025-service-scope-20260921 ]]; then\n  python3 tests/service-scope/release_scope.py\n  node tests/validate-systemwide-image-build-controller.mjs\nelif [[ "$HEAD_BRANCH" == fix/module064-generation-sequence-20260921 ]]; then'], ['.github/workflows/flowhive-psa-release-control-ci.yml', '          if [[ "$GITHUB_HEAD_REF" == fix/module064-generation-sequence-20260921 ]]; then', '          if [[ "$GITHUB_HEAD_REF" == feature/module025-service-scope-20260921 ]]; then\n            python3 tests/service-scope/release_scope.py\n          elif [[ "$GITHUB_HEAD_REF" == fix/module064-generation-sequence-20260921 ]]; then'], ['.github/workflows/module-management-owner-drawer-ci.yml', '          if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then', '          if [[ "$HEAD_BRANCH" == feature/module025-service-scope-20260921 ]]; then\n            python3 tests/service-scope/release_scope.py\n            exit 0\n          fi\n          if [[ "$HEAD_BRANCH" == fix/flowhive-sow-private-generation-20260921 ]]; then'], ['.github/workflows/celar-ai-ask-operations-ci.yml', '          if [[ "$HEAD_BRANCH" == fix/module076-migration084-ci-credentials-* ]]; then', '          if [[ "$HEAD_BRANCH" == feature/module025-service-scope-20260921 ]]; then\n            python3 tests/service-scope/release_scope.py\n          elif [[ "$HEAD_BRANCH" == fix/module076-migration084-ci-credentials-* ]]; then'], ['tests/flowhive-psa-admission.test.mjs', 'const module025VerifierCorrection = module064SequenceRepair ||', "const module025ServiceScope = process.env.GITHUB_HEAD_REF === 'feature/module025-service-scope-20260921';\nconst module025VerifierCorrection = module025ServiceScope || module064SequenceRepair ||"], ['tests/flowhive-psa-admission.test.mjs', 'const module025VerifierBase = module064SequenceRepair ?', "const module025VerifierBase = module025ServiceScope ? 'dc7297037c77a711276c7a36cd0048b4b54c6035' : module064SequenceRepair ?"]]


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT).decode()


def verify(actual, expected):
    assert expected == sorted(set(expected)), 'Manifest must be exact, unique and sorted'
    assert all(p and not any(x in p for x in ('..', '*', '?', '\\')) for p in expected)
    assert set(actual) == set(expected), f'Unexpected Service Scope changes: {set(actual) ^ set(expected)}'


def main():
    expected = (ROOT / MANIFEST).read_text().splitlines()
    verify(expected, expected)
    for mutated in (expected[1:], expected + ['.github/workflows/projectpulse-deploy-production.yml']):
        try:
            verify(mutated, expected)
        except AssertionError:
            continue
        raise AssertionError('Missing or unauthorized source was accepted')
    if '--self-test' in sys.argv:
        print('MODULE025_SERVICE_SCOPE_NEGATIVE_SCOPE=PASS'); return
    if os.getenv('GITHUB_HEAD_REF'):
        assert os.environ['GITHUB_HEAD_REF'] == BRANCH
    if os.getenv('GITHUB_EVENT_NAME'):
        assert os.environ['GITHUB_EVENT_NAME'] == 'pull_request'
    assert git('merge-base', BASE, 'HEAD').strip() == BASE
    actual = set(git('diff', '--name-only', BASE).splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
    verify(actual, expected)
    assert {p for p in actual if p.startswith('database/')} == {'database/migrations/124_module025_service_scope.sql'}
    frozen = (
        '.github/workflows/projectpulse-deploy-test.yml',
        '.github/workflows/projectpulse-deploy-production.yml',
        '.github/workflows/module025-protected-uat-control.yml',
        '.github/workflows/flowhive-psa-installed-acceptance.yml',
        '.github/flowhive-psa-protected-test-candidate.json',
        '.github/flowhive-psa-release-control-files.txt',
        'scripts/release-test/flowhive-psa-admission.mjs',
        'scripts/release-test/dispatch-flowhive-psa-test.mjs',
        'scripts/release-test/run-module025-installed-sa-uat.py',
        'scripts/release-test/run-flowhive-psa-live-uat.py',
        'scripts/release-test/resolve-flowhive-installed-deployment.py',
        'src/backend/ProjectTime.Api/Ai/ProjectPulseDeepSeekProvider.cs',
        'src/backend/ProjectTime.Api/Modules/ProjectFlowHiveExecutionPolicy.cs',
        'scripts/release-test/run-project-planning-document-authority-migration-job.sh',
    )
    for path in frozen:
        assert (ROOT / path).read_bytes() == subprocess.check_output(['git','show',BASE+':'+path], cwd=ROOT), path
    # Existing CI guards may add only the exact branch registration. All
    # existing tests, skips, environment rules and permissions remain unchanged.
    normalized = {}
    for path, old, new in REGISTRATIONS:
        source = normalized.get(path, git('show', BASE + ':' + path))
        assert source.count(old) == 1, path
        normalized[path] = source.replace(old, new, 1)
    for path, source in normalized.items():
        assert (ROOT / path).read_text() == source, f'Unrelated CI changes: {path}'
    path = 'docs/production-readiness/foundation/initialization-review.json'
    prior = json.loads(git('show', BASE + ':' + path))
    current = json.loads((ROOT / path).read_text())
    added = [entry for entry in current['scripts'] if entry['path'] == 'database/migrations/124_module025_service_scope.sql']
    assert len(added) == 1 and added[0]['decision'] == 'review_required'
    current['scripts'].remove(added[0])
    assert current == prior, 'Unrelated production review decisions changed'
    workflow = (ROOT / '.github/workflows/module025-service-scope-ci.yml').read_text()
    assert 'permissions:\n  contents: read\n' in workflow
    assert not any(value in workflow for value in ('contents: write', 'secrets.', 'environment:', 'git push', 'workflow_dispatch:'))
    assert 'run: npm run build' in workflow and 'run: git diff --exit-code' in workflow
    subprocess.run(['python3', 'tests/service-scope/source_boundaries.py'], cwd=ROOT, check=True)
    subprocess.run(['python3', 'tests/module064-migration-rollout.test.py'], cwd=ROOT, check=True)
    subprocess.run(['git','diff','--check',BASE], cwd=ROOT, check=True)
    print(f'MODULE025_SERVICE_SCOPE_RELEASE_SCOPE=PASS files={len(expected)} deployment_authority=unchanged live_consent=unchanged')


if __name__ == '__main__':
    main()
