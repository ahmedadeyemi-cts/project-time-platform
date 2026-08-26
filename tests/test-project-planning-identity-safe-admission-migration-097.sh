#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-identity-safe-097-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/097_project_planning_identity_safe_admission.sql"
ROLLBACK="/workspace/database/rollback/097_project_planning_identity_safe_admission_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -X -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
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

for required in \
  "$ROOT/database/migrations/097_project_planning_identity_safe_admission.sql" \
  "$ROOT/database/rollback/097_project_planning_identity_safe_admission_rollback.sql"; do
  [[ -s "$required" ]] || { echo "ASSERTION_FAILED missing=$required" >&2; exit 1; }
done

docker run -d --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

ready=false
for _ in $(seq 1 90); do
  if docker exec -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
      psql -Atqc 'SELECT 1;' -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 1
done
[[ "$ready" == true ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations (
  migration_id text PRIMARY KEY,
  description text NOT NULL,
  applied_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO schema_migrations(migration_id, description) VALUES
('057_module_001_multi_timer_document_grounded_ai','test prerequisite'),
('096_project_planning_document_authority','test prerequisite');

CREATE TABLE project_intake_documents (
  project_intake_document_id uuid PRIMARY KEY,
  pulse_ai_processing_status text NOT NULL DEFAULT 'not_requested',
  pulse_ai_processing_error_code text NOT NULL DEFAULT '',
  pulse_ai_processing_updated_at timestamptz
);
CREATE TABLE pulse_ai_document_processing_jobs (
  pulse_ai_document_processing_job_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_intake_document_id uuid NOT NULL REFERENCES project_intake_documents(project_intake_document_id),
  requested_purpose text NOT NULL,
  actual_user_id uuid,
  effective_user_id uuid,
  job_status text NOT NULL,
  completed_at timestamptz,
  cancellation_requested boolean NOT NULL DEFAULT false,
  lease_owner text NOT NULL DEFAULT '',
  lease_token uuid,
  lease_heartbeat_at timestamptz,
  lease_expires_at timestamptz,
  diagnostic_code text NOT NULL DEFAULT '',
  diagnostic_message text NOT NULL DEFAULT '',
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE OR REPLACE FUNCTION module001_057_queue_project_ai_document()
RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NEW; END; $$;
CREATE TRIGGER trg_module001_057_queue_project_ai_document_insert
AFTER INSERT ON project_intake_documents
FOR EACH ROW EXECUTE FUNCTION module001_057_queue_project_ai_document();
CREATE TRIGGER trg_module001_057_queue_project_ai_document_update
AFTER UPDATE ON project_intake_documents
FOR EACH ROW EXECUTE FUNCTION module001_057_queue_project_ai_document();

INSERT INTO project_intake_documents(project_intake_document_id, pulse_ai_processing_status)
VALUES('97000000-0000-0000-0000-000000000001','queued');
INSERT INTO pulse_ai_document_processing_jobs(
  project_intake_document_id, requested_purpose, actual_user_id, effective_user_id,
  job_status, lease_owner, lease_token, lease_expires_at)
VALUES(
  '97000000-0000-0000-0000-000000000001', 'project_ai_generation_grounding',
  NULL, NULL, 'queued', 'legacy-worker', gen_random_uuid(), now()+interval '5 minutes');
SQL

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission';")" migration_registered
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE tgrelid='project_intake_documents'::regclass AND tgname IN ('trg_module001_057_queue_project_ai_document_insert','trg_module001_057_queue_project_ai_document_update') AND NOT tgisinternal;")" legacy_queue_triggers_retired
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_proc WHERE proname='module001_057_queue_project_ai_document';")" legacy_queue_function_retired
assert_eq failed "$(value "SELECT job_status FROM pulse_ai_document_processing_jobs WHERE project_intake_document_id='97000000-0000-0000-0000-000000000001';")" identityless_active_job_terminalized
assert_eq legacy_identityless_queue_retired "$(value "SELECT diagnostic_code FROM pulse_ai_document_processing_jobs WHERE project_intake_document_id='97000000-0000-0000-0000-000000000001';")" retirement_diagnostic_recorded
assert_eq failed "$(value "SELECT pulse_ai_processing_status FROM project_intake_documents WHERE project_intake_document_id='97000000-0000-0000-0000-000000000001';")" document_recoverable_failed_state

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission';")" migration_reapply_idempotent

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission';")" rollback_unregisters_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE tgrelid='project_intake_documents'::regclass AND tgname LIKE 'trg_module001_057_queue_project_ai_document%' AND NOT tgisinternal;")" rollback_does_not_restore_unsafe_trigger

echo 'PROJECT_PLANNING_IDENTITY_SAFE_ADMISSION_MIGRATION_097=PASS'
