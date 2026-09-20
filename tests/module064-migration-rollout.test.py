"""Exercise immutable migration packaging and fail-closed execution without Azure."""
from pathlib import Path
import os
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
BASE = 'a59e60eabf25c5a74d1075f5e189d9c5500f0a84'
RUNNER = 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh'
NAME = '123_module064_external_generation_approval'
VERIFY = 'verify-module064-external-generation-approval.sql'
PACKAGE = (
    f'install -m 0444 "$ROOT/database/migrations/{NAME}.sql" "$CONTEXT/database/migrations/{NAME}.sql"\n'
    f'install -m 0444 "$ROOT/scripts/release-test/{VERIFY}" "$CONTEXT/database/{VERIFY}"\n'
    f'(cd "$CONTEXT" && sha256sum database/migrations/{NAME}.sql database/{VERIFY} > database/module064-approval.sha256)\n'
)
APPLY = (
    '(cd "$ROOT" && sha256sum --check --status database/module064-approval.sha256)\n'
    f'psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/migrations/{NAME}.sql"\n'
    f'psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/{VERIFY}"\n'
)
MARKER = "echo 'MIGRATION_123_MODULE064_EXTERNAL_GENERATION_APPROVAL=APPLIED_AND_VERIFIED'\n"


def verify_source(source):
    baseline = subprocess.check_output(['git', 'show', BASE + ':' + RUNNER], cwd=ROOT, text=True)
    normalized = source
    assert normalized.count(PACKAGE) == 1
    normalized = normalized.replace(PACKAGE, '', 1)
    assert normalized.count(APPLY) == 1 and normalized.count(MARKER) == 2
    normalized = normalized.replace(APPLY, '', 1).replace(MARKER, '')
    assert normalized == baseline, 'Unrelated migration image, job, release guard or authority changed'
    entrypoint = source.split("cat > \"$CONTEXT/entrypoint.sh\" <<'ENTRYPOINT'\n", 1)[1].split('\nENTRYPOINT\n', 1)[0]
    outer = source.replace(entrypoint, '')
    assert '\npsql ' not in outer, 'Host runner must never execute private database SQL'
    assert APPLY + MARKER in entrypoint
    for number in ('121', '122', '123'):
        assert f'--file "$ROOT/database/migrations/{number}_' in entrypoint
    assert outer.index('bash "$MIGRATION_RUNNER"') < outer.index(MARKER)
    controller = (ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text()
    assert controller.index('bash scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh') < controller.index('      - name: Deploy immutable Test API image')
    assert f'bash {RUNNER}' in (ROOT / 'scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh').read_text()


def main():
    source = (ROOT / RUNNER).read_text()
    verify_source(source)
    for broken in [source.replace(APPLY, '', 1), source.replace(MARKER, '', 1), source + '\npsql unauthorized\n']:
        try:
            verify_source(broken)
        except AssertionError:
            continue
        raise AssertionError('Rollout source mutation was not rejected')
    with tempfile.TemporaryDirectory(prefix='module064-migration-') as temporary:
        context = Path(temporary)
        (context / 'database/migrations').mkdir(parents=True)
        env = {**os.environ, 'ROOT': str(ROOT), 'CONTEXT': str(context)}
        subprocess.run(['bash', '-c', 'set -Eeuo pipefail\n' + PACKAGE], env=env, check=True)
        assert (context / 'database/migrations' / (NAME + '.sql')).read_bytes() == (ROOT / 'database/migrations' / (NAME + '.sql')).read_bytes()
        binary = context / 'bin'
        binary.mkdir()
        trace = context / 'calls'
        (binary / 'psql').write_text('#!/usr/bin/env bash\nprintf "%s\\n" "$*" >> "$SQL_CALLS"\nexit "${SQL_EXIT:-0}"\n')
        (binary / 'psql').chmod(0o700)
        env = {**os.environ, 'ROOT': str(context), 'PATH': str(binary) + os.pathsep + os.environ['PATH'], 'SQL_CALLS': str(trace)}
        command = 'set -Eeuo pipefail\n' + APPLY + MARKER
        result = subprocess.run(['bash', '-c', command], env=env, capture_output=True, text=True)
        assert result.returncode == 0 and 'APPLIED_AND_VERIFIED' in result.stdout
        assert len(trace.read_text().splitlines()) == 2
        trace.unlink()
        result = subprocess.run(['bash', '-c', command], env={**env, 'SQL_EXIT': '3'}, capture_output=True, text=True)
        assert result.returncode != 0 and 'APPLIED_AND_VERIFIED' not in result.stdout
        trace.unlink()
        packaged = context / 'database/migrations' / (NAME + '.sql')
        packaged.chmod(0o600)
        packaged.write_text(packaged.read_text() + '\n-- tampered\n')
        result = subprocess.run(['bash', '-c', command], env=env, capture_output=True, text=True)
        assert result.returncode != 0 and not trace.exists(), 'Changed migration reached the database'
    print('MODULE064_MIGRATION_ROLLOUT=PASS integrity=verified private_network_only=true pre_api_switch=true')


if __name__ == '__main__':
    main()
