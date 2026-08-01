#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/057_module_001_multi_timer_document_grounded_ai.sql"
ROLLBACK="$ROOT/database/rollback/057_module_001_multi_timer_document_grounded_ai_rollback.sql"
BACKEND="$ROOT/src/backend/ProjectTime.Api/Modules/Module001MultiTimerModule.cs"

for file in "$MIGRATION" "$ROLLBACK" "$BACKEND"; do
  [[ -s "$file" ]] || { echo "MISSING_REQUIRED_FILE=$file" >&2; exit 1; }
done

require_text() {
  local file="$1" text="$2" label="$3"
  grep -Fq "$text" "$file" || {
    echo "ASSERTION_FAILED $label missing=$text file=$file" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label"
}

require_text "$MIGRATION" "057_module_001_multi_timer_document_grounded_ai" migration_id
require_text "$MIGRATION" "actual_elapsed_seconds BETWEEN 0 AND 86400" twenty_four_hour_seconds
require_text "$MIGRATION" "rounded_minutes BETWEEN 0 AND 1440" twenty_four_hour_rounding
require_text "$MIGRATION" "A maximum of five running timers is allowed per user." five_timer_database_guard
require_text "$MIGRATION" "ux_module001_running_assignment" unique_running_assignment
require_text "$MIGRATION" "ux_module001_running_non_project" unique_running_non_project
require_text "$MIGRATION" "PROJECT_AI_CONTEXT_AUTO_QUEUED" immutable_document_queue_evidence
require_text "$MIGRATION" "project_ai_generation_grounding" shared_project_ai_grounding_purpose
require_text "$MIGRATION" "engineering_visible" engineering_visibility_boundary
require_text "$MIGRATION" "rawDocumentSentToExternalProvider', FALSE" private_document_boundary
require_text "$BACKEND" "/api/timesheet/timers/start-batch" batch_start_endpoint
require_text "$BACKEND" "/api/timesheet/timers/v2/stop-all" atomic_stop_all_endpoint
require_text "$BACKEND" "Module001MultiTimerMaximumActive = 5" backend_five_timer_limit
require_text "$BACKEND" "Module001MultiTimerCapSeconds = 86_400" backend_twenty_four_hour_limit
require_text "$BACKEND" "AcquireModule001MultiTimerUserLockAsync" backend_user_lock
require_text "$ROLLBACK" "rollback blocked" guarded_rollback

if ! command -v docker >/dev/null 2>&1; then
  echo "MODULE001_057_POSTGRES_TEST=SKIPPED docker_unavailable"
  echo "MODULE001_057_STATIC_VALIDATION=PASS"
  exit 0
fi

CONTAINER="projectpulse-module001-057-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
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
  local sql="$2"
  local expected="$3"
  local log="/tmp/module001-057-${label}.log"
  if psql_exec -c "$sql" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || {
    echo "ASSERTION_FAILED $label missing_expected=$expected" >&2
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
INSERT INTO schema_migrations(migration_id, description) VALUES
 ('041_module_001_timesheet_timer_and_task_association','test'),
 ('052_pulse_ai_private_document_runtime','test'),
 ('053_pulse_ai_private_rag_orchestration','test');

CREATE TABLE app_users (
  user_id UUID PRIMARY KEY,
  email TEXT NOT NULL,
  display_name TEXT NOT NULL
);
CREATE TABLE projects (
  project_id UUID PRIMARY KEY,
  project_code TEXT NOT NULL,
  project_name TEXT NOT NULL
);
INSERT INTO app_users VALUES
 ('10000000-0000-0000-0000-000000000001','engineer@example.test','Engineer');
INSERT INTO projects VALUES
 ('20000000-0000-0000-0000-000000000001','P-057','Multi Timer Test');

CREATE TABLE module001_timer_sessions (
  timer_session_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES app_users(user_id),
  assignment_id UUID NULL,
  non_project_time_category_id UUID NULL,
  started_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  stopped_at_utc TIMESTAMPTZ NULL,
  effective_stopped_at_utc TIMESTAMPTZ NULL,
  actual_elapsed_seconds INTEGER NULL,
  rounded_minutes INTEGER NULL,
  timer_status VARCHAR(50) NOT NULL DEFAULT 'RUNNING',
  CONSTRAINT chk_module001_timer_actual_seconds CHECK (actual_elapsed_seconds IS NULL OR actual_elapsed_seconds BETWEEN 0 AND 43200),
  CONSTRAINT chk_module001_timer_rounded_minutes CHECK (rounded_minutes IS NULL OR (rounded_minutes BETWEEN 0 AND 720 AND rounded_minutes % 15 = 0))
);
CREATE UNIQUE INDEX ux_module001_one_running_timer_per_user
 ON module001_timer_sessions(user_id) WHERE timer_status='RUNNING';
CREATE TABLE module001_timer_daily_segments (
  timer_daily_segment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  timer_session_id UUID NOT NULL REFERENCES module001_timer_sessions(timer_session_id),
  allocated_rounded_minutes INTEGER NOT NULL,
  CONSTRAINT chk_module001_timer_segment_rounded CHECK (allocated_rounded_minutes BETWEEN 0 AND 720 AND allocated_rounded_minutes % 15 = 0)
);

CREATE TABLE project_intake_requests (
  project_intake_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);
CREATE TABLE project_intake_documents (
  project_intake_document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_intake_request_id UUID NOT NULL REFERENCES project_intake_requests(project_intake_request_id),
  project_id UUID NULL REFERENCES projects(project_id),
  document_type VARCHAR(80) NOT NULL DEFAULT 'other',
  document_category VARCHAR(80) NOT NULL DEFAULT 'other',
  original_file_name TEXT NOT NULL DEFAULT 'document.txt',
  stored_file_name TEXT NOT NULL DEFAULT 'document.txt',
  storage_path TEXT NOT NULL DEFAULT '/private/document.txt',
  engineering_visible BOOLEAN NOT NULL DEFAULT TRUE,
  ai_timesheet_context_enabled BOOLEAN NOT NULL DEFAULT FALSE,
  pulse_ai_processing_status VARCHAR(60) NOT NULL DEFAULT 'not_requested',
  pulse_ai_active_version_id UUID NULL,
  uploaded_by_user_id UUID NULL REFERENCES app_users(user_id),
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE pulse_ai_document_chunks (
  chunk_id VARCHAR(64) PRIMARY KEY,
  project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id),
  project_id UUID NOT NULL REFERENCES projects(project_id),
  engineering_visible BOOLEAN NOT NULL DEFAULT TRUE,
  ai_timesheet_context_enabled BOOLEAN NOT NULL DEFAULT FALSE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE pulse_ai_document_processing_jobs (
  pulse_ai_document_processing_job_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id),
  project_id UUID NOT NULL REFERENCES projects(project_id),
  requested_by_user_id UUID NULL REFERENCES app_users(user_id),
  requested_purpose VARCHAR(80) NOT NULL DEFAULT 'manual',
  priority SMALLINT NOT NULL DEFAULT 50,
  job_status VARCHAR(40) NOT NULL DEFAULT 'queued',
  correlation_id VARCHAR(160) NOT NULL DEFAULT '',
  requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX ux_test_active_document_job
 ON pulse_ai_document_processing_jobs(project_intake_document_id)
 WHERE job_status IN ('queued','scanning','extracting','awaiting_ocr','embedding','indexing','retry_wait','cancel_requested');
CREATE TABLE pulse_ai_document_processing_events (
  pulse_ai_document_processing_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  pulse_ai_document_processing_job_id UUID NOT NULL REFERENCES pulse_ai_document_processing_jobs(pulse_ai_document_processing_job_id),
  project_intake_document_id UUID NOT NULL REFERENCES project_intake_documents(project_intake_document_id),
  project_id UUID NOT NULL REFERENCES projects(project_id),
  actual_user_id UUID NULL REFERENCES app_users(user_id),
  effective_user_id UUID NULL REFERENCES app_users(user_id),
  event_code VARCHAR(100) NOT NULL,
  event_status VARCHAR(40) NOT NULL,
  correlation_id VARCHAR(160) NOT NULL DEFAULT '',
  evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO project_intake_requests(project_intake_request_id)
VALUES ('30000000-0000-0000-0000-000000000001');
SQL

psql_exec -f /workspace/database/migrations/057_module_001_multi_timer_document_grounded_ai.sql >/dev/null
psql_exec -f /workspace/database/migrations/057_module_001_multi_timer_document_grounded_ai.sql >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='057_module_001_multi_timer_document_grounded_ai';")" migration_registered_once
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.ux_module001_one_running_timer_per_user')::text,'');")" legacy_one_timer_index_removed
assert_eq ux_module001_running_assignment "$(value "SELECT to_regclass('public.ux_module001_running_assignment')::text;")" assignment_index_created
assert_eq ux_module001_running_non_project "$(value "SELECT to_regclass('public.ux_module001_running_non_project')::text;")" non_project_index_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE tgname='trg_module001_057_running_timer_limit' AND NOT tgisinternal;")" five_timer_trigger_created

psql_exec <<'SQL'
INSERT INTO module001_timer_sessions(user_id,non_project_time_category_id)
VALUES ('10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001');
SQL
expect_failure duplicate_running_target \
 "INSERT INTO module001_timer_sessions(user_id,non_project_time_category_id) VALUES ('10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001');" \
 "ux_module001_running_non_project"

psql_exec <<'SQL'
INSERT INTO module001_timer_sessions(user_id,non_project_time_category_id) VALUES
 ('10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000002'),
 ('10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000003'),
 ('10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000004'),
 ('10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000005');
SQL
assert_eq 5 "$(value "SELECT COUNT(*) FROM module001_timer_sessions WHERE timer_status='RUNNING';")" five_running_timers_allowed
expect_failure sixth_running_timer \
 "INSERT INTO module001_timer_sessions(user_id,non_project_time_category_id) VALUES ('10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000006');" \
 "maximum of five running timers"

psql_exec <<'SQL'
INSERT INTO module001_timer_sessions(
 user_id,non_project_time_category_id,started_at_utc,stopped_at_utc,
 effective_stopped_at_utc,actual_elapsed_seconds,rounded_minutes,timer_status
) VALUES (
 '10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000007',
 NOW()-INTERVAL '23 hours',NOW(),NOW(),82800,1380,'STOPPED_DRAFT'
);
SQL
assert_eq 1 "$(value "SELECT COUNT(*) FROM module001_timer_sessions WHERE actual_elapsed_seconds=82800 AND rounded_minutes=1380;")" twenty_three_hour_timer_allowed
expect_failure duration_above_24_hours \
 "INSERT INTO module001_timer_sessions(user_id,non_project_time_category_id,started_at_utc,stopped_at_utc,effective_stopped_at_utc,actual_elapsed_seconds,rounded_minutes,timer_status) VALUES ('10000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000008',NOW()-INTERVAL '25 hours',NOW(),NOW(),90000,1440,'STOPPED_DRAFT');" \
 "chk_module001_timer_actual_seconds"

psql_exec <<'SQL'
INSERT INTO project_intake_documents(
 project_intake_document_id,project_intake_request_id,project_id,document_type,
 document_category,original_file_name,stored_file_name,storage_path,
 engineering_visible,ai_timesheet_context_enabled,uploaded_by_user_id
) VALUES (
 '50000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000001','sow','sow','approved-sow.pdf',
 'approved-sow.pdf','/private/approved-sow.pdf',FALSE,FALSE,
 '10000000-0000-0000-0000-000000000001'
);
INSERT INTO pulse_ai_document_chunks(
 chunk_id,project_intake_document_id,project_id,engineering_visible,
 ai_timesheet_context_enabled,is_active
) VALUES (
 repeat('a',64),'50000000-0000-0000-0000-000000000001',
 '20000000-0000-0000-0000-000000000001',TRUE,FALSE,TRUE
);
UPDATE project_intake_documents
SET engineering_visible=TRUE
WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001';
SQL
assert_eq true "$(value "SELECT ai_timesheet_context_enabled::text FROM project_intake_documents WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001';")" visible_document_ai_context_enabled
assert_eq true "$(value "SELECT ai_timesheet_context_enabled::text FROM pulse_ai_document_chunks WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001';")" chunk_policy_propagated
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_document_processing_jobs WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001' AND requested_purpose='project_ai_generation_grounding';")" project_document_queued_once
assert_eq 100 "$(value "SELECT priority FROM pulse_ai_document_processing_jobs WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001';")" sow_priority_highest
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_document_processing_events WHERE event_code='PROJECT_AI_CONTEXT_AUTO_QUEUED' AND evidence_json->>'rawDocumentSentToExternalProvider'='false';")" private_queue_evidence_recorded

if psql_exec -f /workspace/database/rollback/057_module_001_multi_timer_document_grounded_ai_rollback.sql >/tmp/module001-057-rollback.log 2>&1; then
  echo "ASSERTION_FAILED guarded_rollback unexpectedly_succeeded" >&2
  exit 1
fi
grep -Fq 'rollback blocked' /tmp/module001-057-rollback.log || {
  cat /tmp/module001-057-rollback.log >&2
  exit 1
}
echo 'ASSERTION_PASSED guarded_rollback_after_operational_evidence'

echo 'MODULE001_057_POSTGRES_TEST=PASS timers=5 cap=24h documentGrounding=permission-scoped rollback=fail-closed'
