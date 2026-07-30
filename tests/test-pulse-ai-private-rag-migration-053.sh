#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-pulse-ai-053-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION_052="/workspace/database/migrations/052_document_intelligence_runtime.sql"
MIGRATION_053="/workspace/database/migrations/053_intelligence_answer_orchestration.sql"
ROLLBACK_053="/workspace/database/rollback/053_intelligence_answer_orchestration_rollback.sql"

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
  local log="/tmp/pulse-ai-053-${label}.log"
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
  ai_context_summary TEXT NULL,
  ai_context_last_processed_at TIMESTAMPTZ NULL,
  upload_source VARCHAR(60) NOT NULL DEFAULT 'manual_upload',
  uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Administrator'),
 ('10000000-0000-0000-0000-000000000002','ptc@example.test','Project Team Coordinator'),
 ('10000000-0000-0000-0000-000000000003','pm@example.test','Project Manager'),
 ('10000000-0000-0000-0000-000000000004','engineer@example.test','Engineer'),
 ('10000000-0000-0000-0000-000000000005','finance@example.test','Finance');
INSERT INTO clients(client_id,client_name) VALUES
 ('20000000-0000-0000-0000-000000000001','Customer Test');
INSERT INTO projects(project_id,client_id,project_code,project_name,project_manager_user_id) VALUES
 ('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','P-053','Pulse AI RAG Test','10000000-0000-0000-0000-000000000003');
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
 ('ENGINEER','Engineer'),
 ('FINANCE','Finance');
SQL

psql_exec -f "$MIGRATION_052" >/dev/null
psql_exec -f "$MIGRATION_053" >/dev/null
psql_exec -f "$MIGRATION_053" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='053_pulse_ai_private_rag_orchestration';")" migration_registered_once
assert_eq 4 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('pulse_ai_answer_runs','pulse_ai_answer_citations','pulse_ai_answer_feedback','pulse_ai_retrieval_events');")" orchestration_tables_created
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('ASK_PULSE_AI_HELP_SEARCH','USE_PULSE_AI_TIMESHEET_GROUNDING','USE_PULSE_AI_FLOWHIVE_PLANNING','VIEW_PULSE_AI_ANSWER_AUDIT','SUBMIT_PULSE_AI_FEEDBACK');")" permissions_created
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code IN ('PULSE_AI_PRIVATE_HELP_SEARCH','PULSE_AI_PRIVATE_TIMESHEET_GROUNDING','PULSE_AI_PRIVATE_FLOWHIVE_PLANNING');")" features_created
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SUPER_ADMINISTRATOR' AND p.permission_code IN ('ASK_PULSE_AI_HELP_SEARCH','USE_PULSE_AI_TIMESHEET_GROUNDING','USE_PULSE_AI_FLOWHIVE_PLANNING','VIEW_PULSE_AI_ANSWER_AUDIT','SUBMIT_PULSE_AI_FEEDBACK');")" super_admin_full_permissions
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEER' AND p.permission_code IN ('ASK_PULSE_AI_HELP_SEARCH','USE_PULSE_AI_TIMESHEET_GROUNDING','SUBMIT_PULSE_AI_FEEDBACK');")" engineer_permissions
assert_eq 2 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='FINANCE' AND p.permission_code IN ('ASK_PULSE_AI_HELP_SEARCH','SUBMIT_PULSE_AI_FEEDBACK');")" finance_permissions

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
  'succeeded','corr-053-doc'
);
INSERT INTO pulse_ai_document_versions (
  pulse_ai_document_version_id,project_intake_document_id,project_id,source_sha256,
  document_version,authority_status,classification,extraction_method,
  extraction_contract_version,page_count,section_count,chunk_count,character_count,
  estimated_token_count,index_status,processed_by_job_id
) VALUES (
  '70000000-0000-0000-0000-000000000001',
  '50000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',repeat('a',64),
  'approved-sow.pdf@2026-07-29T00:00:00Z','canonical',
  'restricted_internal_document','pdfpig_content_order','pipeline-v1',1,1,1,160,40,
  'embedding_ready','60000000-0000-0000-0000-000000000001'
);
UPDATE project_intake_documents
SET pulse_ai_active_version_id='70000000-0000-0000-0000-000000000001',
    pulse_ai_processing_status='ready'
WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001';
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
  'P-053','Pulse AI RAG Test','Customer Test','sow',
  'approved-sow.pdf@2026-07-29T00:00:00Z','restricted_internal_document',TRUE,TRUE,
  'organization_document_scope',0,'page:1','Scope',1,
  'Install, configure, validate, test, and document the approved project solution.',
  repeat('a',64),repeat('b',64),74,19,ARRAY[0.1,0.2,0.3]::DOUBLE PRECISION[],3,
  'private-embedding-test','ready','embedding_ready'
);
INSERT INTO pulse_ai_answer_runs (
  pulse_ai_answer_run_id,feature_code,purpose_code,answer_status,actual_user_id,
  effective_user_id,project_id,project_code,question_text,question_sha256,
  private_model_provider,private_model_name,prompt_contract_version,
  retrieval_contract_version,retrieval_mode,retrieved_chunk_count,cited_source_count,
  source_document_count,source_version_count,confidence_score,coverage_score,
  citation_coverage_score,answer_json,correlation_id,completed_at
) VALUES (
  '80000000-0000-0000-0000-000000000001','system_help_search','help_search','completed',
  '10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001','P-053','What is required by the SOW?',repeat('d',64),
  'private-pulse-ai','private-model-test','private-rag-prompt-v1','private-rag-retrieval-v1',
  'hybrid',1,1,1,1,0.95,1.0,1.0,
  '{"conclusion":"The SOW requires installation, configuration, validation, testing, and documentation.","citations":[1]}'::JSONB,
  'corr-053-answer',NOW()
);
INSERT INTO pulse_ai_answer_citations (
  pulse_ai_answer_run_id,chunk_id,project_intake_document_id,
  pulse_ai_document_version_id,project_id,source_type,source_module,
  document_category,document_version,original_file_name,citation_anchor,
  page_number,rank_order,lexical_score,semantic_score,combined_score,
  source_sha256,text_sha256,source_processed_at
) VALUES (
  '80000000-0000-0000-0000-000000000001',repeat('c',64),
  '50000000-0000-0000-0000-000000000001','70000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001','project_document','011','sow',
  'approved-sow.pdf@2026-07-29T00:00:00Z','approved-sow.pdf','page:1',1,1,
  0.8,0.9,0.86,repeat('a',64),repeat('b',64),NOW()
);
INSERT INTO pulse_ai_answer_feedback (
  pulse_ai_answer_run_id,actual_user_id,effective_user_id,feedback_type,
  feedback_reason,training_candidate
) VALUES (
  '80000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001','accepted','Accurate and complete.',FALSE
);
INSERT INTO pulse_ai_retrieval_events (
  pulse_ai_answer_run_id,actual_user_id,effective_user_id,project_id,
  feature_code,event_code,event_status,retrieval_mode,candidate_count,
  authorized_candidate_count,returned_chunk_count,correlation_id,evidence_json
) VALUES (
  '80000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001','system_help_search',
  'private_retrieval_completed','succeeded','hybrid',12,1,1,'corr-053-answer',
  '{"authorizationAppliedBeforeRanking":true,"rawChunkTextLogged":false}'::JSONB
);
SQL

assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_answer_runs WHERE answer_status='completed' AND retrieval_mode='hybrid';")" answer_run_recorded
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_answer_citations WHERE citation_anchor='page:1' AND rank_order=1;")" citation_recorded
assert_eq f "$(value "SELECT training_candidate FROM pulse_ai_answer_feedback LIMIT 1;")" feedback_not_training_by_default
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_retrieval_events WHERE event_code='private_retrieval_completed';")" retrieval_event_recorded
expect_sql_failure "UPDATE pulse_ai_retrieval_events SET event_status='failed';" 'Pulse AI retrieval event evidence is immutable.' immutable_retrieval_event_update
expect_sql_failure "DELETE FROM pulse_ai_retrieval_events;" 'Pulse AI retrieval event evidence is immutable.' immutable_retrieval_event_delete

psql_exec -f "$ROLLBACK_053" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_answer_runs')::text,'');")" rollback_removed_answer_runs
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_retrieval_events')::text,'');")" rollback_removed_retrieval_events
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='053_pulse_ai_private_rag_orchestration';")" rollback_removed_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='ASK_PULSE_AI_HELP_SEARCH';")" rollback_removed_permissions

psql_exec -f "$MIGRATION_053" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='053_pulse_ai_private_rag_orchestration';")" safe_reapply

echo 'PULSE_AI_PRIVATE_RAG_MIGRATION_053=PASS'
