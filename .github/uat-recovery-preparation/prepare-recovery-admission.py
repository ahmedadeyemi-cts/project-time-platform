"""Run the unchanged historical assertions with their explicit baseline fixture."""
from pathlib import Path
import hashlib
import os
import subprocess

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'tests/flowhive-psa-admission.test.mjs'
TARGET = ROOT/'tests/laya-admission-fixture.generated.test.mjs'
BASE = 'af060cbc311dcc7cce89c3ae2cc0bff040f5f6ea'
if os.environ.get('GITHUB_HEAD_REF') != 'fix/laya-protected-uat-recovery-20260921':
    raise SystemExit('Only the exact reviewed recovery branch may use this historical fixture')
if os.environ.get('GITHUB_BASE_REF', 'main') != 'main' or not os.environ.get('GITHUB_ENV'):
    raise SystemExit('The isolated main-targeted CI environment is required')
subprocess.run(['python3', str(ROOT/'tests/protected-test-queue-recovery.test.py')], check=True)
raw = SOURCE.read_bytes()
if hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest() != '7c0e26db09d496bd30b47872551dcc63f27d5480':
    raise SystemExit('Historical assertion source changed; review the fixture selectors again')
replacements = {
    'const module025VerifierCorrection = module064SequenceRepair ||':
        'const module025VerifierCorrection = true || module064SequenceRepair ||',
    'const module025VerifierBase = module064SequenceRepair ?':
        f"const module025VerifierBase = true ? '{BASE}' : module064SequenceRepair ?",
}
text = raw.decode()
for old, new in replacements.items():
    if text.count(old) != 1: raise SystemExit('Historical fixture selector is not unique')
    text = text.replace(old, new, 1)
restored = text
for old, new in replacements.items(): restored = restored.replace(new, old, 1)
if restored.encode() != raw: raise SystemExit('Historical assertions or imports changed')
with TARGET.open('x', encoding='utf-8') as handle: handle.write(text)
with open(os.environ['GITHUB_ENV'], 'a', encoding='utf-8') as handle:
    handle.write('LAYA_ADMISSION_TEST=tests/laya-admission-fixture.generated.test.mjs\n')
print('RECOVERY_HISTORICAL_ASSERTIONS=UNCHANGED; live_admission=UNCHANGED')
