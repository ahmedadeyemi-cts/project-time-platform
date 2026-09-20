"""Exact source preparation boundary; grants no deployment admission."""
from pathlib import Path
import hashlib
import json
import os
import subprocess
import sys

BASE = 'a59e60eabf25c5a74d1075f5e189d9c5500f0a84'
BRANCH = 'fix/module064-authoritative-model-catalog-20260920'
MANIFEST = '.github/module064-authoritative-model-catalog-files.txt'
REGISTRATIONS = [
    ('tests/flowhive-migration-package.test.py',
     "'verify-flowhive-automatic-first-draft.sql',\n                 'verify-module064-external-generation-approval.sql'):",
     "'verify-flowhive-automatic-first-draft.sql'):"),
    ('tests/validate-celar-ai-pr630-consolidated.mjs',
     "const module064CatalogMode = branchName === 'fix/module064-authoritative-model-catalog-20260920';\n"
     "if (module064CatalogMode) childProcess.execFileSync('python3', ['tests/module064-authoritative-model-catalog-scope.py'], {stdio:'inherit'});\n"
     'const scopedCompatibilityMode = module064CatalogMode || ', 'const scopedCompatibilityMode = '),
    ('.github/workflows/celar-ai-enterprise-platform-ci.yml',
     "            'Provider order and generation policy' \\\n", "            'Private-first targets and capability routing' \\\n"),
    ('.github/workflows/module064-automatic-provider-health-ci.yml',
     '          if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then\n'
     '            python3 tests/module064-authoritative-model-catalog-scope.py\n'
     '            exit 0\n          fi\n', ''),
    ('.github/workflows/flowhive-psa-release-control-ci.yml',
     '          if [[ "$GITHUB_HEAD_REF" == fix/module064-authoritative-model-catalog-20260920 ]]; then\n'
     '            python3 tests/module064-authoritative-model-catalog-scope.py\n          elif', '          if'),
    ('scripts/ci/validate-celar-ai-enterprise-source-boundary.sh',
     'if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then\n'
     '  python3 tests/module064-authoritative-model-catalog-scope.py\n  exit 0\nfi\n\n', ''),
    ('.github/workflows/module-management-owner-drawer-ci.yml',
     '          if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then\n'
     '            python3 tests/module064-authoritative-model-catalog-scope.py\n'
     '            exit 0\n          fi\n', ''),
    ('scripts/release-test/validate-protected-test-controller-branches.sh',
     'if [[ "$HEAD_BRANCH" == fix/module064-authoritative-model-catalog-20260920 ]]; then\n'
     '  python3 tests/module064-authoritative-model-catalog-scope.py\n'
     '  node tests/validate-systemwide-image-build-controller.mjs\nelif', 'if'),
]


def git(*args):
    return subprocess.check_output(['git', *args]).decode()


def verify_paths(actual, expected):
    assert expected == sorted(set(expected)), 'Manifest must be sorted and unique'
    assert all(path and not any(part in path for part in ('..', '*', '?', '\\')) for path in expected)
    assert set(actual) == set(expected), f'Unexpected source changes: {set(actual) ^ set(expected)}'


def main():
    expected = Path(MANIFEST).read_text().splitlines()
    verify_paths(expected, expected)
    for mutation in [expected[1:], expected + ['.github/workflows/projectpulse-deploy-production.yml'],
                     expected + ['database/migrations/999_unauthorized.sql']]:
        try:
            verify_paths(mutation, expected)
        except AssertionError:
            continue
        raise AssertionError('Scope must reject removed and added files')
    if '--self-test' in sys.argv:
        print('MODULE064_AUTHORITATIVE_SCOPE_NEGATIVE_TESTS=PASS')
        return
    if os.getenv('GITHUB_HEAD_REF'):
        assert os.environ['GITHUB_HEAD_REF'] == BRANCH, 'Registration is specific to this source PR'
    if os.getenv('GITHUB_EVENT_NAME'):
        assert os.environ['GITHUB_EVENT_NAME'] == 'pull_request', 'Source review cannot authorize dispatch or deployment'
    assert git('merge-base', BASE, 'HEAD').strip() == BASE, 'Reviewed main must remain an ancestor'
    actual = set(git('diff', '--name-only', BASE).splitlines()) | set(git('ls-files', '--others', '--exclude-standard').splitlines())
    verify_paths(actual, expected)
    for path, addition, original in REGISTRATIONS:
        source = Path(path).read_text()
        assert source.count(addition) == 1, f'Missing or repeated source registration: {path}'
        assert source.replace(addition, original, 1) == git('show', BASE + ':' + path), f'Unrelated CI changes: {path}'
    protected = [
        '.github/workflows/projectpulse-deploy-test.yml',
        '.github/workflows/projectpulse-deploy-production.yml',
        '.github/workflows/module025-protected-uat-control.yml',
        '.github/workflows/flowhive-psa-installed-acceptance.yml',
        '.github/flowhive-psa-protected-test-candidate.json',
        '.github/flowhive-psa-release-control-files.txt',
        'scripts/release-test/flowhive-psa-admission.mjs',
        'scripts/release-test/dispatch-flowhive-psa-test.mjs',
        'scripts/release-test/build-and-run-module025-retention-migration-106.sh',
        'src/backend/ProjectTime.Api/Modules/Module025SowSellModule.cs',
        'src/backend/ProjectTime.Api/Modules/Module025SowSellPolicy.cs',
        'src/backend/ProjectTime.Api/Modules/Module025SowSellWorker.cs',
    ]
    for path in protected:
        assert Path(path).read_bytes() == subprocess.check_output(['git', 'show', BASE + ':' + path]), f'Protected authority changed: {path}'
    fixture = 'tests/flowhive-psa-admission.test.mjs'
    normalized = Path(fixture).read_text().replace(
        "const module064CatalogPreparation = process.env.GITHUB_HEAD_REF === 'fix/module064-authoritative-model-catalog-20260920';\n", '').replace(
        'const module025VerifierCorrection = module064CatalogPreparation || ', 'const module025VerifierCorrection = ').replace(
        "const module025VerifierBase = module064CatalogPreparation ? 'a59e60eabf25c5a74d1075f5e189d9c5500f0a84' : ", 'const module025VerifierBase = ')
    assert normalized == git('show', BASE + ':' + fixture), 'Only historical fixture selection may change; admission assertions remain intact'
    inventory_path = 'docs/production-readiness/foundation/initialization-review.json'
    inventory = json.loads(Path(inventory_path).read_text())
    migration_path = 'database/migrations/123_module064_external_generation_approval.sql'
    new_inventory = [item for item in inventory['scripts'] if item['path'] == migration_path]
    assert len(new_inventory) == 1 and new_inventory[0]['decision'] == 'review_required'
    assert new_inventory[0]['sha256'] == hashlib.sha256(Path(migration_path).read_bytes()).hexdigest()
    inventory['scripts'] = [item for item in inventory['scripts'] if item['path'] != migration_path]
    assert inventory == json.loads(git('show', BASE + ':' + inventory_path)), 'Prior production review decisions must remain unchanged'
    subprocess.run([sys.executable, 'tests/module064-migration-rollout.test.py'], check=True)
    assert not any(path.startswith('deployment/') for path in actual), 'Infrastructure is excluded'
    assert {path for path in actual if path.startswith('database/')} == {
        'database/migrations/123_module064_external_generation_approval.sql',
        'database/rollback/123_module064_external_generation_approval_rollback.sql',
    }, 'Only explicit approval storage is included'
    workflow = Path('.github/workflows/module064-authoritative-model-catalog-ci.yml').read_text()
    assert 'permissions:\n  contents: read\n' in workflow
    for prohibited in ['id-token:', 'environment:', 'secrets.', 'workflow_dispatch:', 'continue-on-error:']:
        assert prohibited not in workflow, f'Focused CI must be read-only and enforce all checks: {prohibited}'
    subprocess.run(['git', 'diff', '--check', BASE], check=True)
    print(f'MODULE064_AUTHORITATIVE_SCOPE=PASS files={len(expected)} deployment_authority=unchanged')


if __name__ == '__main__':
    main()
