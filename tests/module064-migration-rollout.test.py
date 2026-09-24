"""Exercise exact migration 123/124 packaging and fail-closed private execution offline."""
from pathlib import Path
import os
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
BASE = 'dc7297037c77a711276c7a36cd0048b4b54c6035'
RUNNER = 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh'
PACKAGES = (
    ('123_module064_external_generation_approval', 'verify-module064-external-generation-approval.sql',
     'module064-approval', 'MIGRATION_123_MODULE064_EXTERNAL_GENERATION_APPROVAL=APPLIED_AND_VERIFIED'),
    ('124_module025_service_scope', 'verify-module025-service-scope.sql',
     'module025-service-scope', 'MIGRATION_124_MODULE025_SERVICE_SCOPE=APPLIED_AND_VERIFIED'),
)


def fragments(spec):
    name, verify, manifest, marker = spec
    package = (
        f'install -m 0444 "$ROOT/database/migrations/{name}.sql" "$CONTEXT/database/migrations/{name}.sql"\n'
        f'install -m 0444 "$ROOT/scripts/release-test/{verify}" "$CONTEXT/database/{verify}"\n'
        f'(cd "$CONTEXT" && sha256sum database/migrations/{name}.sql database/{verify} > database/{manifest}.sha256)\n'
    )
    apply = (
        f'(cd "$ROOT" && sha256sum --check --status database/{manifest}.sha256)\n'
        f'psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/migrations/{name}.sql"\n'
        f'psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/{verify}"\n'
    )
    return package, apply, f"echo '{marker}'\n"


# Exact content proposal against current main, reviewed together with the release patch.
# Preserve the pre-existing 123/124 checks by removing only these exact additions.
LAYA_BASE = '14f9850a12868ed7f821fb96d2edf715a3ab92b1'
LAYA_RUNNER_EDITS = [(937, 937, '.sql"\nAUTOMATIC_ADMISSION_LAYA_MIGRATION_FILE="$ROOT/database/migrations/125_automatic_document_admission_laya'), (2224, 2224, ' source is missing."\n[[ -s "$AUTOMATIC_ADMISSION_LAYA_MIGRATION_FILE" ]] || fail "Automatic document admission migration 125'), (6345, 6345, '.sha256)\ninstall -m 0444 "$AUTOMATIC_ADMISSION_LAYA_MIGRATION_FILE" "$CONTEXT/database/migrations/125_automatic_document_admission_laya.sql"\n(cd "$CONTEXT" && sha256sum database/migrations/125_automatic_document_admission_laya.sql > database/automatic-admission-laya'), (11716, 11716, '\n\n(cd "$ROOT" && sha256sum --check --status database/automatic-admission-laya.sha256)\npsql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/migrations/125_automatic_document_admission_laya.sql"'), (18941, 18941, 'automatic_admission_laya_verification="$(psql -X -At -v ON_ERROR_STOP=1 <<\'SQL\'\nSELECT\n  EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=\'125_automatic_document_admission_laya\')::text || \'|\' ||\n  (to_regclass(\'public.pulse_ai_laya_classification_jobs\') IS NOT NULL)::text || \'|\' ||\n  EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=\'public\' AND table_name=\'project_intake_documents\' AND column_name=\'pulse_ai_laya_classification_status\')::text || \'|\' ||\n  EXISTS(SELECT 1 FROM pg_indexes WHERE schemaname=\'public\' AND indexname=\'ux_pulse_ai_laya_classification_jobs_identity\')::text;\nSQL\n)"\n[[ "$automatic_admission_laya_verification" == \'true|true|true|true\' ]] || {\n  echo "ERROR: Automatic document admission/Laya migration 125 verification failed: $automatic_admission_laya_verification" >&2\n  exit 1\n}\n'), (19302, 19302, "=APPLIED_AND_VERIFIED'\necho 'MIGRATION_125_AUTOMATIC_DOCUMENT_ADMISSION_LAYA"), (21792, 21792, "echo 'MIGRATION_125_AUTOMATIC_DOCUMENT_ADMISSION_LAYA=APPLIED_AND_VERIFIED'\n"), (22795, 22795, '\n\nif [[ -n "$EVIDENCE_ROOT" ]]; then\n  install -d -m 0700 "$EVIDENCE_ROOT"\n  automatic_admission_laya_sha256="$(sha256sum "$ROOT/database/migrations/125_automatic_document_admission_laya.sql" | cut -d \' \' -f 1)"\n  jq -n --arg releaseCommit "$RELEASE_COMMIT" --arg image "$RELIABILITY_MIGRATION_IMAGE" --arg sha256 "$automatic_admission_laya_sha256" \\\n    \'{status:"applied_and_verified",migration:"125_automatic_document_admission_laya",sha256:$sha256,releaseCommit:$releaseCommit,image:$image,environment:"protected-test",privateNetworkJob:true,deliveryBoundary:"test_only_or_locked",productionMutation:false}\' \\\n    > "$EVIDENCE_ROOT/migration-125.json"\nfi')]


def without_laya_additions(source):
    baseline = subprocess.check_output(['git', 'show', LAYA_BASE + ':' + RUNNER], cwd=ROOT, text=True)
    expected = baseline
    for start, end, replacement in reversed(LAYA_RUNNER_EDITS):
        expected = expected[:start] + replacement + expected[end:]
    assert source == expected, 'Unreviewed migration runner content or ordering changed'
    entrypoint = source.split("cat > \"$CONTEXT/entrypoint.sh\" <<'ENTRYPOINT'\n", 1)[1].split('\nENTRYPOINT\n', 1)[0]
    marker = "echo 'MIGRATION_125_AUTOMATIC_DOCUMENT_ADMISSION_LAYA=APPLIED_AND_VERIFIED'"
    assert entrypoint.count(marker) == 1
    assert entrypoint.index('/124_module025_') < entrypoint.index('/125_automatic_document_')
    assert entrypoint.index('[[ \"$automatic_admission_laya_verification\"') < entrypoint.index(marker)
    assert source.count(marker) == 2
    return baseline


def verify_source(source):
    source = without_laya_additions(source)
    # Main already contains 123 and all earlier rollouts. Permit only the exact
    # approved additive 124 package, entrypoint invocation and success receipt.
    baseline = subprocess.check_output(['git', 'show', BASE + ':' + RUNNER], cwd=ROOT, text=True)
    package124, apply124, marker124 = fragments(PACKAGES[1])
    assert source.count(package124) == 1
    assert source.count(apply124 + marker124 + '\n') == 1
    assert source.count(marker124) == 2
    normalized = source.replace(package124, '', 1).replace(apply124 + marker124 + '\n', '', 1).replace(marker124, '', 1)
    assert normalized == baseline, 'Unrelated migration image, job, release guard or authority changed'
    entrypoint = source.split('cat > "$CONTEXT/entrypoint.sh" <<\'ENTRYPOINT\'\n', 1)[1].split('\nENTRYPOINT\n', 1)[0]
    outer = source.replace(entrypoint, '')
    assert '\npsql ' not in outer, 'Host runner must never execute private database SQL'
    for spec in PACKAGES:
        package, apply, marker = fragments(spec)
        assert source.count(package) == 1 and source.count(apply) == 1 and source.count(marker) == 2
        assert apply + marker in entrypoint
        assert outer.index('bash "$MIGRATION_RUNNER"') < outer.index(marker)
    for number in ('121', '122', '123', '124'):
        assert f'--file "$ROOT/database/migrations/{number}_' in entrypoint
    assert entrypoint.index('/123_module064_') < entrypoint.index('/124_module025_')
    controller = (ROOT / '.github/workflows/projectpulse-deploy-test.yml').read_text()
    assert controller.index('bash scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh') < controller.index('      - name: Deploy immutable Test API image')
    assert f'bash {RUNNER}' in (ROOT / 'scripts/release-test/run-systemwide-enterprise-reliability-migrations-job.sh').read_text()


def main():
    source = (ROOT / RUNNER).read_text()
    verify_source(source)
    mutations = [source + '\npsql unauthorized\n', source.replace('ON_ERROR_STOP=1', 'ON_ERROR_STOP=0', 1)]
    for spec in PACKAGES:
        package, apply, marker = fragments(spec)
        mutations.extend([source.replace(package, '', 1), source.replace(apply, '', 1), source.replace(marker, '', 1)])
    for broken in mutations:
        try:
            verify_source(broken)
        except AssertionError:
            continue
        raise AssertionError('Rollout source mutation was not rejected')
    for spec in PACKAGES:
        name, verify, manifest, _ = spec
        package, apply, marker = fragments(spec)
        with tempfile.TemporaryDirectory(prefix='module025064-migration-') as temporary:
            context = Path(temporary)
            (context / 'database/migrations').mkdir(parents=True)
            env = {**os.environ, 'ROOT': str(ROOT), 'CONTEXT': str(context)}
            subprocess.run(['bash', '-c', 'set -Eeuo pipefail\n' + package], env=env, check=True)
            for relative in (f'database/migrations/{name}.sql', f'database/{verify}'):
                expected = ROOT / (relative if '/migrations/' in relative else f'scripts/release-test/{verify}')
                assert (context / relative).read_bytes() == expected.read_bytes()
            binary = context / 'bin'
            binary.mkdir()
            trace = context / 'calls'
            (binary / 'psql').write_text('#!/usr/bin/env bash\nprintf "%s\\n" "$*" >> "$SQL_CALLS"\nexit "${SQL_EXIT:-0}"\n')
            (binary / 'psql').chmod(0o700)
            env = {**os.environ, 'ROOT': str(context), 'PATH': str(binary) + os.pathsep + os.environ['PATH'], 'SQL_CALLS': str(trace)}
            command = 'set -Eeuo pipefail\n' + apply + marker
            result = subprocess.run(['bash', '-c', command], env=env, capture_output=True, text=True)
            assert result.returncode == 0 and 'APPLIED_AND_VERIFIED' in result.stdout
            assert len(trace.read_text().splitlines()) == 2
            trace.unlink()
            result = subprocess.run(['bash', '-c', command], env={**env, 'SQL_EXIT': '3'}, capture_output=True, text=True)
            assert result.returncode != 0 and 'APPLIED_AND_VERIFIED' not in result.stdout
            trace.unlink()
            # Neither altered migration bytes nor an altered schema verifier may
            # reach SQL execution or publish a success receipt.
            for relative in (f'database/migrations/{name}.sql', f'database/{verify}'):
                packaged = context / relative
                original = packaged.read_bytes()
                packaged.chmod(0o600)
                packaged.write_bytes(original + b'\n-- tampered\n')
                result = subprocess.run(['bash', '-c', command], env=env, capture_output=True, text=True)
                assert result.returncode != 0 and not trace.exists(), 'Changed package reached the database'
                assert 'APPLIED_AND_VERIFIED' not in result.stdout
                packaged.write_bytes(original)
    print('MODULE064_MIGRATION_ROLLOUT=PASS migrations=123,124 integrity=verified private_network_only=true pre_api_switch=true')


if __name__ == '__main__':
    main()
