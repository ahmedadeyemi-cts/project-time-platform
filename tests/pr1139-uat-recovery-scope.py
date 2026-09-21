"""Closed source-only registration for the PR1139 dispatch recovery, PR1141."""
import hashlib
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
BASE = 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea'
EXPECTED = {
    '.github/workflows/module025-protected-uat-control.yml',
    '.github/workflows/pr1139-uat-recovery-ci.yml',
    'scripts/release-test/recover-pr1139-uat-orphan.py',
    'scripts/release-test/validate-protected-test-controller-branches.sh',
    'tests/test-pr1139-uat-recovery.py',
    'tests/pr1139-uat-recovery-scope.py',
}

def require(condition, message):
    if not condition:
        raise SystemExit(message)

def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], text=True).strip()

def unchanged_except(path, start, end, expected_blob):
    raw = (ROOT / path).read_text()
    pattern = rf'^[ \t]*# {start}\n.*?^[ \t]*# {end}\n'
    matches = re.findall(pattern, raw, re.M | re.S)
    require(len(matches) == 1, f'Expected one exact addition: {path}')
    original = re.sub(pattern, '', raw, flags=re.M | re.S).encode()
    digest = hashlib.sha1(b'blob ' + str(len(original)).encode() + b'\0' + original).hexdigest()
    require(digest == expected_blob, f'Existing source changed outside the recovery addition: {path}')

require(os.environ.get('GITHUB_HEAD_REF') == 'fix/pr1139-protected-uat-dispatch-recovery', 'Wrong recovery branch')
require(os.environ.get('PR_NUMBER') == '1141', 'Wrong recovery PR')
require(git('merge-base', 'origin/main', 'HEAD') == BASE, 'Recovery base changed; re-review required')
changed = set(git('diff', '--name-only', BASE, 'HEAD', '--').splitlines())
require(changed == EXPECTED, 'Recovery must contain exactly the six reviewed files')
for row in git('diff', '--name-status', BASE, 'HEAD', '--').splitlines():
    require(row.split('\t', 1)[0] in ('A', 'M'), 'Recovery cannot delete or rename files')
unchanged_except('.github/workflows/module025-protected-uat-control.yml',
                 'PR1139_UAT_RECOVERY_BEGIN', 'PR1139_UAT_RECOVERY_END',
                 '7bfc6749e3e263e5c16a71ec6dafb9c28bcbcd21')
unchanged_except('scripts/release-test/validate-protected-test-controller-branches.sh',
                 'PR1139_RECOVERY_SCOPE_BEGIN', 'PR1139_RECOVERY_SCOPE_END',
                 '0b653738a9980c154e88388040c85585493c6d14')
require(git('rev-parse', 'HEAD:.github/workflows/projectpulse-deploy-test.yml') ==
        '634983f88d5ce3161b626010c3e20c41a80e3758', 'Actual Test deployment changed')
for path in ('.github/workflows/projectpulse-deploy-production.yml',
             '.github/workflows/projectpulse-release-test-control-ci.yml',
             'scripts/release-test/flowhive-psa-admission.mjs',
             '.github/flowhive-psa-protected-test-candidate.json'):
    git('diff', '--exit-code', BASE, 'HEAD', '--', path)
git('diff', '--check', BASE, 'HEAD')
print('PR1141_EXACT_RECOVERY_SCOPE=PASS')
print('ACTUAL_DEPLOYMENT_AND_REMAINING_CONTRACT_CHECKS=UNCHANGED')
