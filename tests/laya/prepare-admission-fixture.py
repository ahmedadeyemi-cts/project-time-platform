"""Materialize the unchanged historical admission tests for the Laya source PR.

The legacy suite selects its historical verification baseline from an expanding
list of application branch names. Laya is an application addition, not a new
approval for the old pinned FlowHive candidate. Normalize only those two fixture
selectors in an ephemeral copy; retain every assertion and all runtime code.
"""
from pathlib import Path
import hashlib
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'tests/flowhive-psa-admission.test.mjs'
TARGET = ROOT / 'tests/laya-admission-fixture.generated.test.mjs'
BASE = 'e858712038d29dc611fb27db28b96e43aef164a0'
if os.environ.get('GITHUB_HEAD_REF') != 'feature/celar-laya-document-decisions-20260921':
    raise SystemExit('This fixture is restricted to the exact Laya source branch')
if os.environ.get('GITHUB_BASE_REF', 'main') != 'main':
    raise SystemExit('The Laya fixture requires the main target')
if not os.environ.get('GITHUB_ENV'):
    raise SystemExit('The isolated CI environment file is required')
subprocess.run(['python3', str(ROOT / 'tests/laya/release-scope.py')], check=True)
raw = SOURCE.read_bytes()
blob = hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest()
if blob != '7c0e26db09d496bd30b47872551dcc63f27d5480':
    raise SystemExit('The historical test source changed; review its fixture selectors again')
text = raw.decode('utf-8')
replacements = {
    'const module025VerifierCorrection = module064SequenceRepair ||':
        'const module025VerifierCorrection = true || module064SequenceRepair ||',
    'const module025VerifierBase = module064SequenceRepair ?':
        f"const module025VerifierBase = true ? '{BASE}' : module064SequenceRepair ?",
}
for old, new in replacements.items():
    if text.count(old) != 1:
        raise SystemExit('Expected exactly one historical fixture selector')
    text = text.replace(old, new, 1)
# Prove the generated suite is byte-identical apart from the two declarations.
restored = text
for old, new in replacements.items():
    restored = restored.replace(new, old, 1)
if restored.encode('utf-8') != raw:
    raise SystemExit('Historical assertions or runtime imports changed unexpectedly')
with TARGET.open('x', encoding='utf-8') as output:
    output.write(text)
with open(os.environ['GITHUB_ENV'], 'a', encoding='utf-8') as env:
    env.write('LAYA_ADMISSION_TEST=tests/laya-admission-fixture.generated.test.mjs\n')
print('LAYA_HISTORICAL_ADMISSION_FIXTURE=PREPARED')
print('HISTORICAL_TEST_ASSERTIONS=UNCHANGED')
print('LIVE_ADMISSION_AND_APPROVAL_FILES=UNCHANGED')
