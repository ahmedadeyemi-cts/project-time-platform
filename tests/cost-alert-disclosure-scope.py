"""Exact reconciled module application repair registration; does not grant deployment authority."""
from pathlib import Path
import hashlib, os, subprocess
BASE = 'fdafa699f08b6c0238e8c5d8a8fb22412f686aed'
SELF = 'tests/cost-alert-disclosure-scope.py'
EXPECTED = {'src/frontend/project-time-web/src/App.jsx': '3ea66dc6d417bdc17646a9267d868d3ab663ab84d72d8d201cdcc81396d59661', 'scripts/release-test/validate-protected-test-controller-branches.sh': 'fa84635a3fa99960a48e0d2b66f0898d7735472438a9bfe95e7384687dd97a13', 'scripts/release-test/validate-module025-governed-release.sh': '8802a075dc6d7ab946b823f01bd3aa847589a6a4a7b36f2238eb5b24604ffdff'}

def check_files(actual):
    assert len(actual) == len(set(actual)), 'Duplicate file'
    assert set(actual) == set(EXPECTED) | {SELF}, 'Missing or unexpected repair file'
def check_bytes(name, value):
    assert hashlib.sha256(value).hexdigest() == EXPECTED[name], 'Repair content changed: ' + name
def rejected(action):
    try: action()
    except AssertionError: return
    raise AssertionError('Negative scope test failed')
def main():
    os.chdir(Path(__file__).resolve().parents[1])
    git = lambda *args: subprocess.check_output(['git', *args], text=True).strip()
    branch = os.environ.get('GITHUB_HEAD_REF') or os.environ.get('GITHUB_REF_NAME')
    assert branch == 'fix/cost-alert-disclosure-state-20260920', 'Unregistered branch'
    assert git('merge-base', BASE, 'HEAD') == BASE, 'Wrong base'
    assert git('merge-base', 'origin/main', 'HEAD') == BASE, 'Main advanced; revalidate'
    check_files(git('diff', '--name-only', BASE).splitlines())
    for name in EXPECTED: check_bytes(name, Path(name).read_bytes())
    actual = list(EXPECTED) + [SELF]
    for name in actual: rejected(lambda: check_files([p for p in actual if p != name]))
    for name in ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml', 'database/migrations/unreviewed.sql']:
        rejected(lambda: check_files(actual + [name]))
    for name in EXPECTED: rejected(lambda: check_bytes(name, Path(name).read_bytes() + b'changed'))
    assert git('diff', '--name-only', BASE, '--', 'deployment', 'database', '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml', '.github/flowhive-psa-protected-test-candidate.json') == '', 'Deployment authority must remain unchanged'
    subprocess.run(['git', 'diff', '--check', BASE], check=True)
    print(f'ENTERPRISE_REPAIR_EXACT_SCOPE=PASS files={len(actual)} deployment_authority=unchanged')
if __name__ == '__main__': main()
