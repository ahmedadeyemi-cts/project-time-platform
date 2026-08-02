#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-enterprise-notification-065-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/065_enterprise_notification_runtime_completion.sql"
ROLLBACK="/workspace/database/rollback/065_enterprise_notification_runtime_completion_rollback.sql"

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}

value() {
  psql_exec -Atqc "$1" | tr -d '\r'
}

assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

expect_failure() {
  local label="$1"
  shift
  set +e
  "$@" >/tmp/projectpulse-065-expected-failure.log 2>&1
  local status=$?
  set -e
  if [[ "$status" == 0 ]]; then
    echo "ASSERTION_FAILED $label expected command failure" >&2
    cat /tmp/projectpulse-065-expected-failure.log >&2 || true
    exit 1
  fi
  echo "ASSERTION_PASSED $label=blocked"
}

docker run --detach --rm \
  --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  if psql_exec -Atqc 'SELECT 1;' >/dev/null 2>&1; then
    break
  fi
  [[ "$attempt" != 60 ]] || {
    docker logs "$CONTAINER" >&2 || true
    exit 1
  }
  sleep 1
done

psql_exec <<'SQL'
CREATE TABLE schema_migrations (
  migration_id TEXT PRIMARY KEY,
  description TEXT NOT NULL DEFAULT '',
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO schema_migrations (migration_id, description)
VALUES (
  '064_module_065_enterprise_notification_orchestration',
  'Test prerequisite'
);

CREATE TABLE enterprise_notification_run_history (
  enterprise_notification_run_history_id UUID PRIMARY KEY,
  run_type VARCHAR(60) NOT NULL CHECK (run_type IN (
    'scheduled_worker', 'manual_run', 'signed_event', 'preview'
  )),
  started_by_user_id UUID NULL,
  started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  completed_at TIMESTAMPTZ NULL,
  run_status VARCHAR(40) NOT NULL DEFAULT 'running' CHECK (run_status IN (
    'running', 'completed', 'partial', 'failed'
  )),
  observed_count INTEGER NOT NULL DEFAULT 0 CHECK (observed_count >= 0),
  created_count INTEGER NOT NULL DEFAULT 0 CHECK (created_count >= 0),
  dispatched_count INTEGER NOT NULL DEFAULT 0 CHECK (dispatched_count >= 0),
  queued_count INTEGER NOT NULL DEFAULT 0 CHECK (queued_count >= 0),
  suppressed_count INTEGER NOT NULL DEFAULT 0 CHECK (suppressed_count >= 0),
  failed_count INTEGER NOT NULL DEFAULT 0 CHECK (failed_count >= 0),
  source_states JSONB NOT NULL DEFAULT '[]'::JSONB,
  diagnostic_code VARCHAR(160) NOT NULL DEFAULT '',
  correlation_id VARCHAR(180) NOT NULL DEFAULT ''
);

CREATE OR REPLACE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse064_immutable$
BEGIN
  RAISE EXCEPTION 'Enterprise notification orchestration evidence is immutable.';
END;
$projectpulse064_immutable$;

CREATE TRIGGER trg_enterprise_notification_run_history_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_run_history
FOR EACH ROW EXECUTE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation();
SQL

apply_migration() {
  psql_exec -f "$MIGRATION" >/dev/null
}

rollback_migration() {
  psql_exec -f "$ROLLBACK" >/dev/null
}

insert_running() {
  local id="$1" correlation="$2"
  psql_exec -qc "
    INSERT INTO enterprise_notification_run_history (
      enterprise_notification_run_history_id,
      run_type,
      run_status,
      correlation_id
    ) VALUES (
      '$id',
      'scheduled_worker',
      'running',
      '$correlation'
    );"
}

complete_run() {
  local id="$1" status="$2"
  psql_exec -qc "
    UPDATE enterprise_notification_run_history
    SET completed_at = NOW(),
        run_status = '$status',
        observed_count = 12,
        created_count = 4,
        dispatched_count = 2,
        queued_count = 1,
        suppressed_count = 1,
        failed_count = CASE WHEN '$status' = 'partial' THEN 1 ELSE 0 END,
        source_states = '[{\"status\":\"healthy\"}]'::jsonb,
        diagnostic_code = CASE WHEN '$status' = 'partial' THEN 'PARTIAL_DELIVERY_FAILURE' ELSE '' END
    WHERE enterprise_notification_run_history_id = '$id';"
}

apply_migration
apply_migration
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='065_enterprise_notification_runtime_completion';")" migration_registered_once
assert_eq projectpulse065_guard_enterprise_notification_run_history "$(value "SELECT p.proname FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE t.tgname='trg_enterprise_notification_run_history_immutable' AND NOT t.tgisinternal;")" completion_guard_installed

RUN_ONE='00000000-0000-0000-0000-000000000651'
insert_running "$RUN_ONE" 'runtime-065-complete'
complete_run "$RUN_ONE" completed
assert_eq completed "$(value "SELECT run_status FROM enterprise_notification_run_history WHERE enterprise_notification_run_history_id='$RUN_ONE';")" running_to_completed_allowed
assert_eq 12 "$(value "SELECT observed_count FROM enterprise_notification_run_history WHERE enterprise_notification_run_history_id='$RUN_ONE';")" completion_counts_recorded
expect_failure finalized_update_blocked psql_exec -qc "UPDATE enterprise_notification_run_history SET diagnostic_code='tampered' WHERE enterprise_notification_run_history_id='$RUN_ONE';"
expect_failure finalized_delete_blocked psql_exec -qc "DELETE FROM enterprise_notification_run_history WHERE enterprise_notification_run_history_id='$RUN_ONE';"

RUN_TWO='00000000-0000-0000-0000-000000000652'
insert_running "$RUN_TWO" 'runtime-065-immutable-origin'
expect_failure immutable_origin_blocked psql_exec -qc "
  UPDATE enterprise_notification_run_history
  SET completed_at=NOW(), run_status='completed', run_type='manual_run'
  WHERE enterprise_notification_run_history_id='$RUN_TWO';"
assert_eq running "$(value "SELECT run_status FROM enterprise_notification_run_history WHERE enterprise_notification_run_history_id='$RUN_TWO';")" failed_transition_left_running

rollback_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='065_enterprise_notification_runtime_completion';")" rollback_removed_registration
assert_eq projectpulse064_block_enterprise_notification_evidence_mutation "$(value "SELECT p.proname FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE t.tgname='trg_enterprise_notification_run_history_immutable' AND NOT t.tgisinternal;")" rollback_restored_strict_guard
RUN_THREE='00000000-0000-0000-0000-000000000653'
insert_running "$RUN_THREE" 'runtime-065-rollback'
expect_failure rollback_blocks_completion psql_exec -qc "UPDATE enterprise_notification_run_history SET completed_at=NOW(), run_status='completed' WHERE enterprise_notification_run_history_id='$RUN_THREE';"

apply_migration
complete_run "$RUN_THREE" partial
assert_eq partial "$(value "SELECT run_status FROM enterprise_notification_run_history WHERE enterprise_notification_run_history_id='$RUN_THREE';")" reapply_restored_completion
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='065_enterprise_notification_runtime_completion';")" reapply_registered_once

echo 'ENTERPRISE_NOTIFICATION_RUNTIME_COMPLETION_MIGRATION_065=PASS'
