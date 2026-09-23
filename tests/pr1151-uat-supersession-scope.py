"""Exact seven-file, read-only orphan-verification repair for merged PR1151/1152."""
from pathlib import Path
import os
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = '6957ed57c31ba694cc0aa91cd164bb4b64866e45'
BRANCH = 'fix/pr1151-uat-orphan-20260923'
SUPERVISOR = '.github/workflows/module025-protected-uat-control.yml'
REGISTRY = 'scripts/release-test/validate-protected-test-controller-branches.sh'
LEGACY = 'tests/pr1140-uat-recovery-scope.py'
WORKFLOW = '.github/workflows/pr1151-uat-supersession-ci.yml'
EXPECTED = {SUPERVISOR, REGISTRY, LEGACY, WORKFLOW,
            'scripts/release-test/verify-pr1151-uat-supersession.py',
            'tests/test-pr1151-uat-supersession.py',
            'tests/pr1151-uat-supersession-scope.py'}
SUPERVISOR_ANCHOR = '            # MIGRATION_RETRY_RECOVERY_END\n'
SUPERVISOR_ADDITION = '''            # PR1151_UAT_SUPERSESSION_BEGIN
            if [[ "$run_id" == '35891884845' ]]; then
              python3 scripts/release-test/verify-pr1151-uat-supersession.py \\
                || fail 'PR 1151/1152 supersession did not meet its exact safety contract.'
              quarantined_runs+=("$run_id")
              continue
            fi
            # PR1151_UAT_SUPERSESSION_END
'''
REGISTRY_ANCHOR = '# MIGRATION_THROTTLE_SCOPE_END\n'
REGISTRY_ADDITION = '''# PR1151_UAT_SUPERSESSION_SCOPE_BEGIN
if [[ "$HEAD_BRANCH" == fix/pr1151-uat-orphan-20260923 ]]; then
  python3 tests/pr1151-uat-supersession-scope.py
  python3 tests/test-pr1151-uat-supersession.py
  node tests/validate-systemwide-image-build-controller.mjs
  return
fi
# PR1151_UAT_SUPERSESSION_SCOPE_END
'''
LEGACY_ANCHOR = "    assert after == expected, 'Unrelated supervisor or registry change'\n"
LEGACY_ADDITION = '''    if after != expected:
        exact_successor = {
            SUPERVISOR_ANCHOR: (%r, %r),
            REGISTRY_ANCHOR: (%r, %r),
        }
        if anchor in exact_successor:
            marker, extension = exact_successor[anchor]
            assert expected.count(marker) == 1
            expected = expected.replace(marker, extension + marker, 1)
''' % (SUPERVISOR_ANCHOR, SUPERVISOR_ADDITION, REGISTRY_ANCHOR, REGISTRY_ADDITION)
PATCHES = {
    SUPERVISOR: (SUPERVISOR_ANCHOR, SUPERVISOR_ADDITION + SUPERVISOR_ANCHOR),
    REGISTRY: (REGISTRY_ANCHOR, REGISTRY_ADDITION + REGISTRY_ANCHOR),
    LEGACY: (LEGACY_ANCHOR, LEGACY_ADDITION + LEGACY_ANCHOR),
}


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True)


def verify_sources():
    for path, (before, after) in PATCHES.items():
        original = git('show', BASE + ':' + path)
        assert original.count(before) == 1
        assert (ROOT / path).read_text() == original.replace(before, after, 1), path
    assert git('rev-parse', 'HEAD:.github/workflows/projectpulse-deploy-test.yml').strip() == '634983f88d5ce3161b626010c3e20c41a80e3758'
    assert not git('diff', '--name-only', BASE, 'HEAD', '--', 'src', 'database',
                   '.github/workflows/projectpulse-deploy-test.yml',
                   '.github/workflows/projectpulse-deploy-production.yml').strip()
    workflow = (ROOT / WORKFLOW).read_text()
    assert 'permissions:\n  contents: read\n' in workflow
    assert not any(value in workflow for value in ('actions: write', 'contents: write',
                   'secrets.', 'environment:', 'workflow_dispatch:', 'git push'))


def main():
    assert os.environ.get('GITHUB_HEAD_REF') == BRANCH, 'Wrong recovery branch'
    assert os.environ.get('GITHUB_EVENT_NAME') == 'pull_request', 'Only PR CI validates this source boundary'
    assert git('merge-base', 'origin/main', 'HEAD').strip() == BASE, 'Main changed; re-review required'
    assert set(git('diff', '--name-only', BASE, 'HEAD').splitlines()) == EXPECTED, 'Unexpected repair scope'
    for row in git('diff', '--name-status', BASE, 'HEAD').splitlines():
        assert row.split('\t', 1)[0] in ('A', 'M'), 'No deletion or rename authorized'
    verify_sources()
    subprocess.run(['git', 'diff', '--check', BASE, 'HEAD'], cwd=ROOT, check=True)
    print('PR1151_UAT_SUPERSESSION_SCOPE=PASS files=7 actual_deployment_controller=unchanged')


if __name__ == '__main__':
    main()
