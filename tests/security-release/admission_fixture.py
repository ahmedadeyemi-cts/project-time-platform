"""Run the unchanged historical admission assertions with an explicit fixture.

Only the two existing fixture selectors change in an ephemeral test copy.
The current candidate, approval files, live controller and assertions do not.
"""
from pathlib import Path
import argparse
import hashlib
import os
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'tests/flowhive-psa-admission.test.mjs'
TARGET = ROOT / 'tests/security-admission.generated.test.mjs'
SOURCE_BLOB = 'bef62ea96a938107be17c2a83cbbaf7549bdd7ba'
# Pin the security implementation's artifact minimization; candidate/approval stay frozen.
FIXTURE_REF = 'c107f17195db6aaed16daff596d123d7a31c8a67'
BRANCHES = {'feature/pulse-private-services-activation-20260930', 'feature/pulse-services-cutover-laya-20260930', 'feature/pulse-documents-integration-20260930', 'codex/flowhive-save-schedule-approval-polish', 'fix/uat-provider-answer-contract-20260930', 'fix/security-audit-followup-20260929', 'fix/installed-acceptance-browser-evidence-20260929', 'codex/approval-routing-bulk-review', 'fix/security-team-findings-20260928', 'fix/security-complete-20260929', 'fix/security-uat-followup-20260929', 'fix/security-uat-acceptance-20260929'}


def blob(content):
    return hashlib.sha1(b'blob ' + str(len(content)).encode() + b'\0' + content).hexdigest()


def prepare():
    branch = os.environ.get('GITHUB_HEAD_REF', '')
    if branch not in BRANCHES or os.environ.get('GITHUB_BASE_REF', 'main') != 'main':
        raise RuntimeError('Historical fixture selection is restricted to the exact security remediation inventory')
    subprocess.run([sys.executable, str(ROOT / ('tests/pulse-activation-release/scope.py' if branch == 'feature/pulse-private-services-activation-20260930' else 'tests/pulse-services-release/scope.py' if branch == 'feature/pulse-services-cutover-laya-20260930' else 'tests/pulse-document-integration/scope.py' if branch == 'feature/pulse-documents-integration-20260930' else 'tests/flowhive-save-release/scope.py' if branch == 'codex/flowhive-save-schedule-approval-polish' else 'tests/uat-provider-answer/scope.py' if branch == 'fix/uat-provider-answer-contract-20260930' else 'tests/security-audit-followup/scope.py' if branch == 'fix/security-audit-followup-20260929' else 'tests/installed-browser-resolution/scope.py' if branch == 'fix/installed-acceptance-browser-evidence-20260929' else 'tests/combined-release/scope.py' if branch == 'codex/approval-routing-bulk-review' else 'tests/security-uat-acceptance/scope.py' if branch == 'fix/security-uat-acceptance-20260929' else 'tests/security-uat-followup/scope.py' if branch == 'fix/security-uat-followup-20260929' else 'tests/security-completion/scope.py' if branch == 'fix/security-complete-20260929' else 'tests/security-release/scope.py'))], cwd=ROOT, check=True)
    original = SOURCE.read_bytes()
    if blob(original) != SOURCE_BLOB:
        raise RuntimeError('Historical test source changed; its assertions must be reviewed before fixture selection')
    replacements = {
        'const module025VerifierCorrection = module025ServiceScope ||':
            'const module025VerifierCorrection = true || module025ServiceScope ||',
        'const module025VerifierBase = module025ServiceScope ?':
            f"const module025VerifierBase = true ? '{FIXTURE_REF}' : module025ServiceScope ?",
    }
    text = original.decode('utf-8')
    for old, new in replacements.items():
        if text.count(old) != 1:
            raise RuntimeError('Historical fixture declaration is missing or ambiguous')
        text = text.replace(old, new, 1)
    restored = text
    for old, new in replacements.items():
        restored = restored.replace(new, old, 1)
    if restored.encode('utf-8') != original:
        raise RuntimeError('Fixture preparation changed an assertion, import, or runtime source')
    if text.count("test(") != original.decode().count("test("):
        raise RuntimeError('Fixture preparation changed test coverage')
    with TARGET.open('x', encoding='utf-8') as output:
        output.write(text)
    print('ADMISSION_FIXTURE_ASSERTIONS=BYTE_IDENTICAL; live_candidate_unchanged=true', flush=True)
    return TARGET.relative_to(ROOT).as_posix()


def main():
    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--prepare', action='store_true')
    mode.add_argument('--run-all', action='store_true')
    args = parser.parse_args()
    target = prepare()
    if args.prepare:
        return
    # Preserve the workflow's full original glob, replacing exactly its one
    # branch-dependent fixture, never dropping a test module or assertion.
    original = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / 'tests').glob('flowhive-psa-*.test.mjs'))
    old = SOURCE.relative_to(ROOT).as_posix()
    if original.count(old) != 1:
        raise RuntimeError('Expected the original admission suite exactly once')
    tests = [target if path == old else path for path in original]
    tests.append('tests/flowhive-team-calendar.test.mjs')
    try:
        result = subprocess.run(['node', '--test', *tests], cwd=ROOT)
        if result.returncode:
            raise SystemExit(result.returncode)
    finally:
        TARGET.unlink(missing_ok=True)


if __name__ == '__main__':
    main()
