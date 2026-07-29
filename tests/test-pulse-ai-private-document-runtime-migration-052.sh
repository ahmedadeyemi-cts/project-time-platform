#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-pulse-ai-052-${GITHUB_RUN_ID:-local}-$$"
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
CREATE TABLE project_intake_requests (
  project_intake_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);
CREATE TABLE project_intake_documents (
  project_intake_document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_intake_request_id UUID NOT NULL REFERENCES project_intake_requests(project_intake_request_id) ON DELETE CASCADE,
  project_id UUID NULL REFERENCES projects(project_id),
  document_type VARCHAR(80) NOT NULL DEFAULT 'other',
  document_category VARCHAR(80) NOT NULL DEFAULT 'other',
  original_file_name TEXT NOT NULL,
  stored_file_name TEXT NOT NULL,
  storage_path TEXT NOT NULL,
  content_type TEXT NULL,
  size_bytes BIGINT NOT NULL DEFAULT 0,
  engineering_visible BOOLEAN NOT NULL DEFAULT TRUE,
  ai_timesheet_context_enabled BOOLEAN NOT NULL DEFAULT FALSE,
  extraction_status VARCHAR(60) NOT NULL DEFAULT 'not_started',
  uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);

INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Administrator'),
 ('10000000-0000-0000-0000-000000000002','ptc@example.test','Project Team Coordinator'),
 ('10000000-0000-0000-0000-000000000003','pm@example.test','Project Manager'),
 ('10000000-0000-0000-0000-000000000004','lead@example.test','Engineering Lead');
INSERT INTO clients(client_id,client_name) VALUES
 ('20000000-0000-0000-0000-000000000001','Customer Test');
INSERT INTO projects(project_id,client_id,project_code,project_name,project_manager_user_id) VALUES
 ('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','P-052','Pulse AI Private Runtime Test','10000000-0000-0000-0000-000000000003');
INSERT INTO project_intake_requests(project_intake_request_id) VALUES
 ('40000000-0000-0000-0000-000000000001');
INSERT INTO project_intake_documents(
  project_intake_document_id,project_intake_request_id,project_id,document_type,
  document_category,original_file_name,stored_file_name,storage_path,
  engineering_visible,ai_timesheet_context_enabled
) VALUES (
  '50000000-0000-0000-0000-000000000001',
  '40000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  'sow','sow','approved-sow.pdf','approved-sow.pdf','/private/approved-sow.pdf',TRUE,TRUE
);
INSERT INTO app_roles(role_code,role_name) VALUES
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('ADMINISTRATOR','Administrator'),
 ('PROJECT_TEAM_COORDINATOR','Project Team Coordinator'),
 ('PROJECT_MANAGEMENT','Project Management'),
 ('ENGINEERING_LEAD','Engineering Lead');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='052_pulse_ai_private_document_runtime';")" migration_registered_once
assert_eq 5 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('pulse_ai_document_processing_jobs','pulse_ai_document_versions','pulse_ai_document_sections','pulse_ai_document_chunks','pulse_ai_document_processing_events');")" tables_created
assert_eq 8 "$(value "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='project_intake_documents' AND column_name IN ('pulse_ai_processing_status','pulse_ai_classification','pulse_ai_document_revision','pulse_ai_effective_at','pulse_ai_superseded_by_document_id','pulse_ai_active_version_id','pulse_ai_processing_error_code','pulse_ai_processing_updated_at');")" document_runtime_columns_created
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_PULSE_AI_DOCUMENT_RUNTIME','QUEUE_PULSE_AI_DOCUMENT_PROCESSING','CANCEL_PULSE_AI_DOCUMENT_PROCESSING','RETRY_PULSE_AI_DOCUMENT_PROCESSING','APPROVE_PULSE_AI_DOCUMENT_VERSION');")" permissions_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='PULSE_AI_PRIVATE_DOCUMENT_RUNTIME';")" feature_created
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SUPER_ADMINISTRATOR' AND p.module_code='011';")" super_admin_full_permissions
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_TEAM_COORDINATOR' AND p.module_code='011';")" ptc_full_permissions
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGEMENT' AND p.permission_code='VIEW_PULSE_AI_DOCUMENT_RUNTIME';")" pm_view_permission
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEERING_LEAD' AND p.permission_code='VIEW_PULSE_AI_DOCUMENT_RUNTIME';")" engineering_lead_view_permission
assert_eq restricted_internal_document "$(value "SELECT pulse_ai_classification FROM project_intake_documents WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001';")" sow_classified_restricted

psql_exec <<'SQL'
INSERT INTO pulse_ai_document_processing_jobs (
  pulse_ai_document_processing_job_id,project_intake_document_id,project_id,
  actual_user_id,effective_user_id,requested_by_user_id,job_status,correlation_id
) VALUES (
  '60000000-0000-0000-0000-000000000001',
  '50000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'queued','corr-052-test'
);
INSERT INTO pulse_ai_document_versions (
  pulse_ai_document_version_id,project_intake_document_id,project_id,source_sha256,
  document_version,authority_status,classification,extraction_method,
  extraction_contract_version,page_count,section_count,chunk_count,character_count,
  estimated_token_count,index_status,processed_by_job_id
) VALUES (
  '70000000-0000-0000-0000-000000000001',
  '50000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  repeat('a',64),'approved-sow.pdf@2026-07-29T00:00:00Z','canonical',
  'restricted_internal_document','pdfpig_content_order','pipeline-v1',1,1,1,120,30,
  'embedding_ready','60000000-0000-0000-0000-000000000001'
);
UPDATE project_intake_documents
SET pulse_ai_active_version_id='70000000-0000-0000-0000-000000000001',
    pulse_ai_processing_status='ready'
WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001';
INSERT INTO pulse_ai_document_sections (
  pulse_ai_document_version_id,project_intake_document_id,section_index,
  citation_anchor,section_title,page_number,section_text,character_count,text_sha256
) VALUES (
  '70000000-0000-0000-0000-000000000001',
  '50000000-0000-0000-0000-000000000001',0,'page:1','Scope',1,
  'Install, configure, validate, and document the approved project solution.',66,repeat('b',64)
);
INSERT INTO pulse_ai_document_chunks (
  chunk_id,pulse_ai_document_version_id,project_intake_document_id,project_id,
  project_code,project_name,customer_name,document_category,document_version,
  classification,engineering_visible,ai_timesheet_context_enabled,access_scope,
  chunk_index,citation_anchor,section_title,page_number,chunk_text,source_sha256,
  text_sha256,character_count,estimated_token_count,embedding,embedding_dimension,
  embedding_model,embedding_status,index_status
) VALUES (
  repeat('c',64),'70000000-0000-0000-0000-000000000001',
  '50000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',
  'P-052','Pulse AI Private Runtime Test','Customer Test','sow',
  'approved-sow.pdf@2026-07-29T00:00:00Z','restricted_internal_document',TRUE,TRUE,
  'organization_document_scope',0,'page:1','Scope',1,
  'Install, configure, validate, and document the approved project solution.',
  repeat('a',64),repeat('b',64),66,17,ARRAY[0.1,0.2,0.3]::DOUBLE PRECISION[],3,
  'private-embedding-test','ready','embedding_ready'
);
INSERT INTO pulse_ai_document_processing_events (
  pulse_ai_document_processing_job_id,project_intake_document_id,project_id,
  actual_user_id,effective_user_id,event_code,event_status,correlation_id,evidence_json
) VALUES (
  '60000000-0000-0000-0000-000000000001',
  '50000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'document_indexed','succeeded','corr-052-test','{"rawTextLogged":false}'::JSONB
);
SQL

assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_document_chunks WHERE search_vector @@ plainto_tsquery('english','configure validate');")" lexical_search_vector_ready
assert_eq 3 "$(value "SELECT embedding_dimension FROM pulse_ai_document_chunks WHERE chunk_id=repeat('c',64);")" embedding_dimension_preserved
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_document_processing_events WHERE event_code='document_indexed';")" immutable_event_recorded
expect_sql_failure "INSERT INTO pulse_ai_document_processing_jobs(project_intake_document_id,job_status) VALUES ('50000000-0000-0000-0000-000000000001','queued');" 'duplicate key value violates unique constraint' one_active_job_per_document
expect_sql_failure "UPDATE pulse_ai_document_processing_events SET event_status='failed';" 'Pulse AI document processing event evidence is immutable.' immutable_event_update
expect_sql_failure "DELETE FROM pulse_ai_document_processing_events;" 'Pulse AI document processing event evidence is immutable.' immutable_event_delete

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_document_processing_jobs')::text,'');")" rollback_removed_jobs
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_document_versions')::text,'');")" rollback_removed_versions
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_document_chunks')::text,'');")" rollback_removed_chunks
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='052_pulse_ai_private_document_runtime';")" rollback_removed_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='VIEW_PULSE_AI_DOCUMENT_RUNTIME';")" rollback_removed_permissions
assert_eq 0 "$(value "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='project_intake_documents' AND column_name='pulse_ai_processing_status';")" rollback_removed_document_columns

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='052_pulse_ai_private_document_runtime';")" safe_reapply

echo 'PULSE_AI_PRIVATE_DOCUMENT_RUNTIME_MIGRATION_052=PASS'
