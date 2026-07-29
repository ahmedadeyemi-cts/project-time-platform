#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-pending-approval-051a-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/051a_pending_approval_day_status_lifecycle.sql"
ROLLBACK="/workspace/database/rollback/051a_pending_approval_day_status_lifecycle_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}

value() { psql_exec -Atqc "$1" | tr -d '\r'; }

assert_eq() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

expect_sql_failure() {
  local sql="$1"
  local expected="$2"
  local label="$3"
  local log="/tmp/pending-approval-051a-${label}.log"
  if psql_exec -c "$sql" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || {
    echo "ASSERTION_FAILED $label missing_expected_error=$expected" >&2
    cat "$log" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label"
}

expect_file_failure() {
  local file="$1"
  local expected="$2"
  local label="$3"
  local log="/tmp/pending-approval-051a-${label}.log"
  if psql_exec -f "$file" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || {
    echo "ASSERTION_FAILED $label missing_expected_error=$expected" >&2
    cat "$log" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label"
}

docker run --detach --rm \
  --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  if psql_exec -Atqc 'SELECT 1;' >/dev/null 2>&1; then break; fi
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations (
  migration_id TEXT PRIMARY KEY,
  description TEXT NOT NULL DEFAULT '',
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE timesheet_day_statuses (
  timesheet_day_status_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  timesheet_id UUID NOT NULL,
  user_id UUID NOT NULL,
  work_date DATE NOT NULL,
  status VARCHAR(40) NOT NULL DEFAULT 'draft',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT chk_timesheet_day_status CHECK (
    status IN ('draft','submitted','manager_approved','manager_declined','pm_declined')
  ),
  UNIQUE(timesheet_id, work_date)
);
INSERT INTO timesheet_day_statuses(timesheet_id,user_id,work_date,status) VALUES
  ('10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','2026-07-20','submitted'),
  ('10000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000002','2026-07-21','manager_approved');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='051a_pending_approval_day_status_lifecycle';")" migration_registered_once
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_indexes WHERE schemaname='public' AND indexname='ix_timesheet_day_statuses_pending_approval_stage';")" pending_stage_index_created

psql_exec <<'SQL'
UPDATE timesheet_day_statuses
SET status='pm_approved'
WHERE work_date='2026-07-21';
UPDATE timesheet_day_statuses
SET status='accounting_ready'
WHERE work_date='2026-07-21';
SQL
assert_eq accounting_ready "$(value "SELECT status FROM timesheet_day_statuses WHERE work_date='2026-07-21';")" manager_pm_ptc_lifecycle_allowed
expect_sql_failure \
  "UPDATE timesheet_day_statuses SET status='unsupported_status' WHERE work_date='2026-07-20';" \
  'chk_timesheet_day_status' \
  unsupported_status_rejected

expect_file_failure \
  "$ROLLBACK" \
  'Migration 051A rollback blocked: later approval lifecycle statuses are in use' \
  guarded_rollback_blocks_live_lifecycle

psql_exec "UPDATE timesheet_day_statuses SET status='manager_approved' WHERE work_date='2026-07-21';" >/dev/null
psql_exec -f "$ROLLBACK" >/dev/null

assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='051a_pending_approval_day_status_lifecycle';")" rollback_removed_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_indexes WHERE schemaname='public' AND indexname='ix_timesheet_day_statuses_pending_approval_stage';")" rollback_removed_index
expect_sql_failure \
  "UPDATE timesheet_day_statuses SET status='pm_approved' WHERE work_date='2026-07-21';" \
  'chk_timesheet_day_status' \
  rollback_restored_prior_constraint

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='051a_pending_approval_day_status_lifecycle';")" safe_reapply
psql_exec "UPDATE timesheet_day_statuses SET status='pm_approved' WHERE work_date='2026-07-21';" >/dev/null
assert_eq pm_approved "$(value "SELECT status FROM timesheet_day_statuses WHERE work_date='2026-07-21';")" reapply_restored_pm_stage

echo 'PENDING_APPROVAL_MIGRATION_051A=PASS'
