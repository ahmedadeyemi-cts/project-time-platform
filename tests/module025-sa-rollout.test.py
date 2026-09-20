"""Prepare the exact UAT migration payload without Azure or deployment authority.

--source-only checks the unchanged release guards and builds the real image
context locally. The default additionally executes its actual entrypoint twice
against an owned disposable PostgreSQL database and rejects broken integrity or
postconditions. No application credentials or notification transport is used.
"""
import hashlib
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[1]
BASE = '373b37e9c76e430355ed2dc5f71ec6a133ead3aa'
RUNNER = 'scripts/release-test/build-and-run-module025-retention-migration-106.sh'
ADDED = (
    '116_module025_governed_ownership_transfer',
    '117_module025_template_candidates',
    '118_module025_work_tracking',
    '119_module025_temporary_handoffs',
    '120_module025_handoff_notifications',
)
ALL = ('106_module025_sow_sell_register', '110_module025_ungenerated_draft_delete',
       '111_connectwise_sell_provider') + ADDED
POSTCONDITION_HASH = 'ff93d686d186fc1051f368c4e42f71a6b5e547e4870f7d65e407a90177042934'


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def verify_source(text):
    baseline = subprocess.check_output(['git', 'show', BASE + ':' + RUNNER], cwd=ROOT, text=True)
    normalized = text
    for name in ADDED:
        for addition in (
            f'install -m 0444 "$ROOT/database/migrations/{name}.sql" "$CONTEXT/migration-{name[:3]}.sql"\n',
            f'psql -X -v ON_ERROR_STOP=1 --file migration-{name[:3]}.sql\n',
            f"echo 'MIGRATION_{name.upper()}=APPLIED_AND_VERIFIED'\n",
        ):
            require(normalized.count(addition) == 1, 'Each staged migration needs exactly one copy, apply and verified marker.')
            normalized = normalized.replace(addition, '', 1)
    old_files = 'migration-106.sql migration-110.sql migration-111.sql'
    new_files = old_files + ''.join(' migration-' + name[:3] + '.sql' for name in ADDED)
    require(normalized.count(new_files) == 3, 'Checksums, Docker COPY and immutable permissions must cover the exact migration set.')
    normalized = normalized.replace(new_files, old_files)
    new_evidence = '"111_connectwise_sell_provider",' + ','.join('"' + name + '"' for name in ADDED) + ']'
    require(normalized.count(new_evidence) == 1, 'Migration evidence must identify the complete applied set.')
    normalized = normalized.replace(new_evidence, '"111_connectwise_sell_provider"]', 1)
    match = re.search(r'  -- SA workspace schema is verified before the unchanged job can report success\.\n.*?(?=\)::text;\nSQL)', normalized, re.S)
    require(match is not None, 'Missing staged schema postconditions.')
    require(hashlib.sha256(match[0].encode()).hexdigest() == POSTCONDITION_HASH, 'The reviewed schema postcondition block changed.')
    normalized = normalized.replace(match[0], '', 1)
    require(normalized == baseline, 'The migration increment must preserve every existing release guard, authority, image and job-launch control.')


def run(args, *, env=None, text=None, success=True):
    result = subprocess.run(args, input=text, text=True, capture_output=True, env=env, cwd=ROOT, timeout=90)
    require((result.returncode == 0) == success,
            f'Unexpected exit for {args[0]}: {result.stderr[-3000:]}')
    return result.stdout.strip()


def sql(statement, env, success=True):
    return run(['psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1'], env=env, text=statement, success=success)


def table(file, name):
    source = (ROOT / 'database/migrations' / file).read_text()
    marker = 'CREATE TABLE IF NOT EXISTS ' + name + ' ('
    start = source.index(marker)
    return source[start:source.index('\n);', start) + 3]


def prepare_context(source, directory):
    # Execute exactly the real local packaging section, ending before az acr.
    begin = source.index('install -m 0444 ')
    end = source.index('\nDOCKERFILE\n', begin) + len('\nDOCKERFILE\n')
    env = {**os.environ, 'ROOT': str(ROOT), 'CONTEXT': str(directory), 'RELEASE': BASE}
    run(['bash'], env=env, text='set -Eeuo pipefail\n' + source[begin:end])
    checks = (directory / 'SHA256SUMS').read_text().splitlines()
    require(len(checks) == len(ALL) + 1, 'Unexpected files in integrity manifest.')
    for name in ALL:
        require((directory / ('migration-' + name[:3] + '.sql')).read_bytes()
                == (ROOT / 'database/migrations' / (name + '.sql')).read_bytes(), 'Packaged migration differs from source.')
    entry = (directory / 'entrypoint.sh').read_text()
    require(entry.count('cd /opt/projectpulse/release\n') == 1, 'Entrypoint image root changed.')
    # Only substitute the image mount path for the temporary local directory.
    import shlex
    local_entry = entry.replace('cd /opt/projectpulse/release\n', 'cd ' + shlex.quote(str(directory)) + '\n')
    (directory / 'local-entrypoint.sh').write_text(local_entry)
    run(['bash', '-n', str(directory / 'local-entrypoint.sh')])
    return local_entry


def integrity_tests(directory):
    # If any guard accidentally allows execution, a stub detects even one psql
    # call. This negative test never needs or contacts a database.
    binary = directory / 'bin'
    binary.mkdir()
    trace = directory / 'unexpected-psql'
    (binary / 'psql').write_text('#!/usr/bin/env bash\nprintf called >> "$PSQL_TRACE"\nexit 99\n')
    (binary / 'psql').chmod(0o700)
    env = {**os.environ, 'PATH': str(binary) + os.pathsep + os.environ['PATH'], 'PSQL_TRACE': str(trace),
           'MAIN_RELEASE_MIGRATION_MODE': 'apply', 'MAIN_RELEASE_EXPECTED_RELEASE_COMMIT': BASE}
    for name, value in [('MAIN_RELEASE_MIGRATION_MODE', 'rollback'), ('MAIN_RELEASE_EXPECTED_RELEASE_COMMIT', '0' * 40)]:
        run(['bash', str(directory / 'local-entrypoint.sh')], env={**env, name: value}, success=False)
    target = directory / 'migration-120.sql'
    original = target.read_bytes()
    target.chmod(0o600)
    target.write_bytes(original + b'\n-- changed after image manifest\n')
    run(['bash', str(directory / 'local-entrypoint.sh')], env=env, success=False)
    target.write_bytes(original)
    require(not trace.exists(), 'Integrity or mode guard allowed database execution.')


def bootstrap(env):
    # Real foundational Module 025 migrations; unrelated CRM/notification tables
    # are extracted verbatim from their already-deployed schema definitions.
    for name in ['001_initial_schema', '099_module025_sow_gsd_workspace', '109_module025_project_name']:
        sql((ROOT / 'database/migrations' / (name + '.sql')).read_text(), env)
    sql(table('026-crm-integration-framework.sql', 'crm_integration_providers'), env)
    crm = (ROOT / 'database/migrations/034_module_026_crm_erp_integrations.sql').read_text()
    start = crm.index('ALTER TABLE crm_integration_providers\n')
    sql(crm[start:crm.index(';', start) + 1], env)
    sql(table('034_module_026_crm_erp_integrations.sql', 'crm_integration_oauth_states'), env)
    for name in ['customer_directory_source_authority', 'customer_directory_source_authority_history']:
        sql(table('098_customer_directory_source_authority.sql', name), env)
    sql('CREATE TABLE project_notification_dispatches(project_notification_dispatch_id uuid PRIMARY KEY);', env)
    for name in ['enterprise_notification_policies', 'enterprise_notification_events', 'enterprise_notification_event_history']:
        sql(table('064_module_065_enterprise_notification_orchestration.sql', name), env)


def database_tests(directory, entry):
    require(os.environ.get('PGHOST') == '127.0.0.1', 'Rollout fixture only accepts a disposable loopback PostgreSQL server.')
    require(os.environ.get('PGUSER') == 'postgres', 'Rollout fixture requires the isolated PostgreSQL test role.')
    require(bool(os.environ.get('PGPASSWORD')), 'Disposable PostgreSQL password must be supplied.')
    database = 'module025_rollout_test_' + uuid.uuid4().hex
    env = {**os.environ, 'PGDATABASE': database, 'MAIN_RELEASE_MIGRATION_MODE': 'apply',
           'MAIN_RELEASE_EXPECTED_RELEASE_COMMIT': BASE}
    run(['createdb', database], env=env)
    try:
        bootstrap(env)
        for attempt in range(2):
            output = run(['bash', str(directory / 'local-entrypoint.sh')], env=env)
            require('MIGRATION_120_MODULE025_HANDOFF_NOTIFICATIONS=APPLIED_AND_VERIFIED' in output, 'Entrypoint did not verify the full migration chain.')
            if attempt == 0:
                # An existing administrator policy choice survives replay; the
                # migration cannot re-enable delivery or erase its configuration.
                sql("UPDATE enterprise_notification_policies SET enabled=false, delivery_boundary='locked' WHERE policy_code='MODULE025_HANDOFF';", env)
        require(sql("SELECT enabled::text||':'||delivery_boundary FROM enterprise_notification_policies WHERE policy_code='MODULE025_HANDOFF'", env) == 'false:locked', 'Replay changed an existing delivery policy.')
        require(sql("SELECT count(*) FROM enterprise_notification_events", env) == '0', 'Installing schema must never queue or send a notification.')
        require(sql("SELECT count(*) FROM enterprise_notification_policies WHERE policy_code LIKE 'MODULE025_%' AND owner_module='065'", env) == '4', 'Handoff policies are missing or duplicated.')
        require(sql("SELECT count(*) FROM enterprise_notification_policies WHERE policy_code LIKE 'MODULE025_%' AND enabled AND delivery_boundary='test_only'", env) == '3', 'New policies must stay inside the existing test delivery boundary.')
        # Exercise the actual terminal verification against a disabled audit
        # guard. A marker alone cannot make a damaged schema pass.
        verification = entry[entry.index('verified="$(psql'):entry.index("echo 'MODULE025_RETENTION_MIGRATION_106")]
        sql('ALTER TABLE module025_work_tracking_events DISABLE TRIGGER trg_module025_protect_work_tracking;', env)
        run(['bash'], env=env, text='set -Eeuo pipefail\n' + verification, success=False)
        sql('ALTER TABLE module025_work_tracking_events ENABLE REPLICA TRIGGER trg_module025_protect_work_tracking;', env)
        run(['bash'], env=env, text='set -Eeuo pipefail\n' + verification, success=False)
        sql('ALTER TABLE module025_work_tracking_events ENABLE TRIGGER trg_module025_protect_work_tracking;', env)
        run(['bash'], env=env, text='set -Eeuo pipefail\n' + verification)
    finally:
        run(['dropdb', '--if-exists', '--force', database], env=env)


def main():
    source = (ROOT / RUNNER).read_text()
    verify_source(source)
    for original, changed in [('refs/heads/main', 'refs/heads/unsafe'), ('sow_role|sow_exports)', '*)'), ('--timeout 1800', '--timeout 9999'), ('AND owner_module=\'065\'', 'AND owner_module=\'025\'')]:
        require(original in source, 'Negative source test anchor is missing.')
        try:
            verify_source(source.replace(original, changed, 1))
        except AssertionError:
            pass
        else:
            raise AssertionError('Changed release authority or verification was accepted.')
    with tempfile.TemporaryDirectory(prefix='module025-rollout-') as temporary:
        directory = Path(temporary)
        entry = prepare_context(source, directory)
        integrity_tests(directory)
        if '--source-only' not in sys.argv:
            database_tests(directory, entry)
    print('MODULE025_SA_ROLLOUT_SOURCE=PASS authority=unchanged integrity_guards=verified')
    if '--source-only' not in sys.argv:
        print('MODULE025_SA_ROLLOUT_DATABASE=PASS entrypoint=actual replay=verified policies=preserved transport=unused')


if __name__ == '__main__':
    main()
