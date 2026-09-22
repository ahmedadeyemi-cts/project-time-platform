"""Exact six-file PR1142 recovery; application and deployment authority stay unchanged."""
from pathlib import Path
import os
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = '4e8871cf9a7c4643483d178feecff67ec6d78d12'
BRANCH = 'fix/pr1140-protected-uat-dispatch-recovery'
SUPERVISOR = '.github/workflows/module025-protected-uat-control.yml'
REGISTRY = 'scripts/release-test/validate-protected-test-controller-branches.sh'
EXPECTED = {
    SUPERVISOR, REGISTRY,
    '.github/workflows/pr1140-uat-recovery-ci.yml',
    'scripts/release-test/recover-pr1140-uat-orphan.py',
    'tests/test-pr1140-uat-recovery.py',
    'tests/pr1140-uat-recovery-scope.py',
}
SUPERVISOR_ANCHOR = '            # PR1139_UAT_RECOVERY_END\n'
SUPERVISOR_ADDITION = (
    '            # PR1140_UAT_RECOVERY_BEGIN\n'
    '            if [[ "$run_id" == \'35739149661\' ]]; then\n'
    '              python3 scripts/release-test/recover-pr1140-uat-orphan.py \\\n'
    '                || fail \'PR 1140 orphan recovery did not meet its exact safety contract.\'\n'
    '              quarantined_runs+=("$run_id")\n'
    '              continue\n'
    '            fi\n'
    '            # PR1140_UAT_RECOVERY_END\n')
REGISTRY_ANCHOR = '# PR1139_RECOVERY_SCOPE_BEGIN\n'
REGISTRY_ADDITION = (
    '# PR1140_RECOVERY_SCOPE_BEGIN\n'
    'if [[ "$HEAD_BRANCH" == fix/pr1140-protected-uat-dispatch-recovery ]]; then\n'
    '  python3 tests/pr1140-uat-recovery-scope.py\n'
    '  python3 tests/test-pr1140-uat-recovery.py\n'
    '  node tests/validate-systemwide-image-build-controller.mjs\n'
    '  return\n'
    'fi\n'
    '# PR1140_RECOVERY_SCOPE_END\n')


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def original(path):
    return subprocess.check_output(['git', 'show', BASE + ':' + path], cwd=ROOT, text=True)


def verify_paths(paths):
    assert set(paths) == EXPECTED, 'Recovery must contain exactly the six reviewed paths'


def verify_insertion(before, after, anchor, addition):
    assert before.count(anchor) == 1 and addition not in before
    expected = before.replace(anchor, addition + anchor, 1)
    # A later reviewed repair adds one pinned recovery and its CI registration.
    # Compare the entire exact extended source, not a regex-stripped projection.
    if after != expected:
        extensions = {
            SUPERVISOR_ANCHOR: ('            # PR1140_UAT_RECOVERY_END\n', '            # MIGRATION_RETRY_RECOVERY_BEGIN\n            if [[ "$run_id" == \'35761573008\' ]]; then\n              python3 scripts/release-test/recover-pr1140-migration-retry-orphan.py \\\n                || fail \'Migration retry orphan recovery did not meet its exact safety contract.\'\n              quarantined_runs+=("$run_id")\n              continue\n            fi\n            # MIGRATION_RETRY_RECOVERY_END\n'),
            REGISTRY_ANCHOR: ('# PR1140_RECOVERY_SCOPE_BEGIN\n', '# MIGRATION_THROTTLE_SCOPE_BEGIN\nif [[ "$HEAD_BRANCH" == fix/uat-migration-throttle-recovery-20260922 ]]; then\n  python3 tests/uat-migration-throttle-scope.py\n  python3 tests/test-azure-migration-throttle.py\n  python3 tests/test-pr1140-migration-retry-recovery.py\n  node tests/validate-systemwide-image-build-controller.mjs\n  return\nfi\n# MIGRATION_THROTTLE_SCOPE_END\n'),
        }
        if anchor in extensions:
            marker, extension = extensions[anchor]
            assert expected.count(marker) == 1
            expected = expected.replace(marker, extension + marker, 1)
    assert after == expected, 'Unrelated supervisor or registry change'


def verify_sources():
    for path, anchor, addition in (
        (SUPERVISOR, SUPERVISOR_ANCHOR, SUPERVISOR_ADDITION),
        (REGISTRY, REGISTRY_ANCHOR, REGISTRY_ADDITION),
    ):
        verify_insertion(original(path), (ROOT / path).read_text(), anchor, addition)
    # Use the already-reviewed algorithm verbatim; only its pinned identity and
    # own filename/display marker differ. No generalized orphan whitelist.
    expected = original('scripts/release-test/recover-pr1139-uat-orphan.py')
    for before, after in (
        ('PR 1139', 'PR 1140'), ('pr1139', 'pr1140'), ('PR1139', 'PR1140'),
        ('35645759101', '35739149661'),
        ('af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea', BASE),
        ('2026-09-21T19:35:24Z', '2026-09-22T14:16:21Z'),
    ):
        expected = expected.replace(before, after)
    assert (ROOT / 'scripts/release-test/recover-pr1140-uat-orphan.py').read_text() == expected
    for path in (
        '.github/workflows/projectpulse-deploy-test.yml',
        '.github/workflows/projectpulse-deploy-production.yml',
        '.github/workflows/pr1139-uat-recovery-ci.yml',
        'scripts/release-test/recover-pr1139-uat-orphan.py',
        'tests/test-pr1139-uat-recovery.py',
        'tests/flowhive-psa-release-workflow.test.py',
        'scripts/release-test/flowhive-psa-admission.mjs',
        '.github/flowhive-psa-protected-test-candidate.json',
    ):
        assert (ROOT / path).read_text() == original(path), 'Frozen source changed: ' + path
    assert git('rev-parse', 'HEAD:.github/workflows/projectpulse-deploy-test.yml') == '634983f88d5ce3161b626010c3e20c41a80e3758'
    workflow = (ROOT / '.github/workflows/pr1140-uat-recovery-ci.yml').read_text()
    assert 'permissions:\n  contents: read\n' in workflow
    assert not any(value in workflow for value in ('actions: write', 'contents: write', 'secrets.', 'environment:', 'workflow_dispatch:', 'git push'))


def main():
    assert os.environ.get('GITHUB_HEAD_REF') == BRANCH, 'Wrong recovery branch'
    assert os.environ.get('PR_NUMBER') == '1142', 'Wrong recovery PR'
    assert os.environ.get('GITHUB_EVENT_NAME') == 'pull_request', 'Only PR CI may validate this scope'
    assert git('merge-base', 'origin/main', 'HEAD') == BASE, 'Main changed; re-review required'
    verify_paths(git('diff', '--name-only', BASE, 'HEAD').splitlines())
    for row in git('diff', '--name-status', BASE, 'HEAD').splitlines():
        assert row.split('\t', 1)[0] in ('A', 'M'), 'No deletes or renames'
    verify_sources()
    subprocess.run(['git', 'diff', '--check', BASE, 'HEAD'], cwd=ROOT, check=True)
    print('PR1142_RECOVERY_SCOPE=PASS files=6 deployment_controller=unchanged application=unchanged privacy=unchanged')


if __name__ == '__main__':
    main()
