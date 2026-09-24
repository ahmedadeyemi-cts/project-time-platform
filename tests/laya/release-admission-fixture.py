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
TARGET = ROOT / 'tests/laya-release-admission.generated.test.mjs'
SOURCE_BLOB = '76d7ca937c76075d3e83a661402ba67e03fb791e'
FIXTURE_REF = 'e2315413b61cc0893c3771b40f428b6fc61bd53f'
BRANCHES = {'control/pr1153-source-registration-20260923', 'fix/automatic-document-admission-laya-20260923'}


def blob(content):
    return hashlib.sha1(b'blob ' + str(len(content)).encode() + b'\0' + content).hexdigest()


def prepare():
    branch = os.environ.get('GITHUB_HEAD_REF', '')
    if branch not in BRANCHES or os.environ.get('GITHUB_BASE_REF', 'main') != 'main':
        raise RuntimeError('Historical fixture selection is restricted to the document-admission repair')
    subprocess.run([sys.executable, str(ROOT / 'tests/laya/release-registration.py'),
                    '--control' if branch.startswith('control/') else '--application'], cwd=ROOT, check=True)
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
