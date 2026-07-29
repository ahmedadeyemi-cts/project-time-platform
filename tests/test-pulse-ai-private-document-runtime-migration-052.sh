#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="pulse-ai-runtime-052-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/052_pulse_ai_private_document_runtime.sql"
ROLLBACK="/workspace/database/rollback/052_pulse_ai_private_document_runtime_rollback.sql"

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
expect_sql_failure() {
  local sql="$1" expected="$2" label="$3"
  local log="/tmp/pulse-ai-052-${label}.log"
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
CREATE TABLE app_users (
  user_id UUID PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE clients (
  client_id UUID PRIMARY KEY,
  client_name TEXT NOT NULL
);
CREATE TABLE projects (
  project_id UUID PRIMARY KEY,
  client_id UUID NULL REFERENCES clients(client_id),
  project_code TEXT NOT NULL,
  project_name TEXT NOT NULL,
  project_manager_user_id UUID NULL REFERENCES app_users(user_id)
);
CREATE TABLE project_intake_requests (
  project_intake_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID NULL REFERENCES projects(project_id)
);
CREATE TABLE project_intake_documents (
  project_intake_document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_intake_request_id UUID NULL REFERENCES project_intake_requests(project_intake_request_id),
  project_id UUID NULL REFERENCES projects(project_id),
  original_file_name TEXT NOT NULL,
  stored_file_name TEXT NOT NULL,
  storage_path TEXT NOT NULL,
  document_type TEXT NOT NULL DEFAULT 'other',
  document_category TEXT NOT NULL DEFAULT 'other',
  engineering_visible BOOLEAN NOT NULL DEFAULT TRUE,
  ai_timesheet_context_enabled BOOLEAN NOT NULL DEFAULT FALSE,
  extraction_status TEXT NOT NULL DEFAULT 'not_started',
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE app_roles (
  app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  role_code TEXT NOT NULL UNIQUE,
  role_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE app_permissions (
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code TEXT NOT NULL UNIQUE,
  permission_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  permission_description TEXT NOT NULL DEFAULT ''
);
CREATE TABLE app_role_permissions (
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
  app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
  UNIQUE(app_role_id, app_permission_id)
);
CREATE TABLE app_feature_catalog (
  app_feature_catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  feature_code TEXT NOT NULL UNIQUE,
  feature_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  route_anchor TEXT,
  required_permission_code TEXT,
  feature_description TEXT,
  display_order INTEGER NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Administrator'),
 ('10000000-0000-0000-0000-000000000002','pm@example.test','Project Manager'),
 ('10000000-0000-0000-0000-000000000003','engineer@example.test','Engineer');
INSERT INTO clients(client_id,client_name) VALUES
 ('20000000-0000-0000-0000-000000000001','Private AI Test Customer');
INSERT INTO projects(project_id,client_id,project_code,project_name,project_manager_user_id) VALUES
 ('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','P-AI-052','Private AI Runtime','10000000-0000-0000-0000-000000000002');
INSERT INTO project_intake_requests(project_intake_request_id,project_id) VALUES
 ('40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001');
INSERT INTO project_intake_documents(
 project_intake_document_id,project_intake_request_id,project_id,original_file_name,stored_file_name,storage_path,document_type,document_category
) VALUES (
 '50000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',
 'approved-sow.pdf','approved-sow.pdf','/private/approved-sow.pdf','sow','sow'
);
INSERT INTO app_roles(role_code,role_name) VALUES
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('ADMINISTRATOR','Administrator'),
 ('PROJECT_TEAM_COORDINATOR','Project Team Coordinator'),
 ('PROJECT_MANAGEMENT','Project Management'),
 ('ENGINEERING','Engineering'),
 ('SOLUTION_ARCHITECT','Solution Architect'),
 ('EXECUTIVE','Executive');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='052_pulse_ai_private_document_runtime';")" migration_registered_once
assert_eq 10 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename LIKE 'pulse_ai_document_%';")" runtime_tables_created
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code LIKE '%PULSE_AI_DOCUMENT%';")" permissions_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='PULSE_AI_PRIVATE_DOCUMENT_RUNTIME';")" feature_created
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SUPER_ADMINISTRATOR' AND p.permission_code LIKE '%PULSE_AI_DOCUMENT%';")" super_admin_full_permissions
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGEMENT' AND p.permission_code LIKE '%PULSE_AI_DOCUMENT%';")" pm_scoped_permissions
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEERING' AND p.permission_code LIKE '%PULSE_AI_DOCUMENT%';")" engineer_view_permission
assert_eq 2 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='EXECUTIVE' AND p.permission_code LIKE '%PULSE_AI_DOCUMENT%';")" executive_read_permissions
assert_eq 7 "$(value "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='project_intake_documents' AND column_name LIKE 'pulse_ai_%';")" document_runtime_columns

psql_exec <<'SQL'
INSERT INTO pulse_ai_document_versions (
 pulse_ai_document_version_id,project_intake_document_id,project_id,source_sha256,
 original_file_name,document_category,document_version_label,version_state,canonical_for_category
) VALUES (
 '60000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',
 repeat('a',64),'approved-sow.pdf','sow','SOW-v1','approved',TRUE
);
INSERT INTO pulse_ai_document_scan_results (
 pulse_ai_document_version_id,scanner_code,source_sha256,scan_status
) VALUES (
 '60000000-0000-0000-0000-000000000001','clamav',repeat('a',64),'clean'
);
INSERT INTO pulse_ai_document_processing_jobs (
 pulse_ai_document_processing_job_id,pulse_ai_document_version_id,project_intake_document_id,project_id,
 requested_by_user_id,actual_user_id,effective_user_id,source_sha256,job_state
) VALUES (
 '70000000-0000-0000-0000-000000000001','60000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001',
 '30000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001',
 '10000000-0000-0000-0000-000000000001',repeat('a',64),'queued'
);
INSERT INTO pulse_ai_document_processing_events (
 pulse_ai_document_processing_job_id,pulse_ai_document_version_id,project_intake_document_id,
 event_code,event_status,actor_user_id
) VALUES (
 '70000000-0000-0000-0000-000000000001','60000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001',
 'job_queued','requested','10000000-0000-0000-0000-000000000001'
);
SQL

expect_sql_failure "UPDATE pulse_ai_document_scan_results SET scan_status='error';" 'Pulse AI private processing evidence is immutable.' immutable_scan_update
expect_sql_failure "DELETE FROM pulse_ai_document_processing_events;" 'Pulse AI private processing evidence is immutable.' immutable_event_delete
expect_sql_failure "INSERT INTO pulse_ai_document_versions(project_intake_document_id,project_id,source_sha256,original_file_name,document_category,document_version_label,version_state,canonical_for_category) VALUES ('50000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',repeat('b',64),'other-sow.pdf','sow','SOW-v2','approved',TRUE);" 'duplicate key value violates unique constraint' canonical_version_unique
expect_sql_failure "INSERT INTO pulse_ai_document_processing_jobs(pulse_ai_document_version_id,project_intake_document_id,project_id,source_sha256,job_state) VALUES ('60000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',repeat('a',64),'queued');" 'duplicate key value violates unique constraint' one_active_job_per_version

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_document_versions')::text,'');")" rollback_removed_versions
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_document_processing_jobs')::text,'');")" rollback_removed_jobs
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='052_pulse_ai_private_document_runtime';")" rollback_removed_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='VIEW_PULSE_AI_DOCUMENT_RUNTIME';")" rollback_removed_permissions
assert_eq 0 "$(value "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='project_intake_documents' AND column_name LIKE 'pulse_ai_%';")" rollback_removed_columns

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='052_pulse_ai_private_document_runtime';")" safe_reapply

echo 'PULSE_AI_PRIVATE_DOCUMENT_RUNTIME_MIGRATION_052=PASS'
