#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-document-bridge-079-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/079_coordinated_runtime_ai_document_rbac_repair.sql"
ROLLBACK="/workspace/database/rollback/079_coordinated_runtime_ai_document_rbac_repair_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() { docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"; }
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }
  echo "ASSERTION_PASSED $label=$actual"
}
expect_failure() {
  local log="/tmp/document-bridge-079-$1.log" expected="$2"
  shift 2
  if "$@" >"$log" 2>&1; then echo "ASSERTION_FAILED expected_failure=$expected" >&2; exit 1; fi
  grep -Fq "$expected" "$log" || { cat "$log" >&2; exit 1; }
  echo "ASSERTION_PASSED expected_failure=$1"
}

docker run --detach --rm --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" -e POSTGRES_PASSWORD="$DB_PASSWORD" -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" postgres:16-alpine >/dev/null
for attempt in $(seq 1 60); do
  psql_exec -Atqc 'SELECT 1' >/dev/null 2>&1 && break
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT NOT NULL DEFAULT '',applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
INSERT INTO schema_migrations(migration_id) VALUES
  ('052_pulse_ai_private_document_runtime'),
  ('057_module_001_multi_timer_document_grounded_ai'),
  ('072_celar_ai_conversation_attachments'),
  ('078_module_001a_engineer_request_closeout');

CREATE TABLE app_roles(
  app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  role_code TEXT NOT NULL UNIQUE,
  role_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE app_permissions(
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code TEXT NOT NULL UNIQUE,
  permission_name TEXT NOT NULL
);
CREATE TABLE app_role_permissions(
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles,
  app_permission_id UUID NOT NULL REFERENCES app_permissions,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(app_role_id,app_permission_id)
);
INSERT INTO app_roles(role_code,role_name) VALUES
  ('ENGINEERING_LEAD','Engineering Lead'),
  ('ENGINEERING_TEAM_LEAD','Engineering Team Lead'),
  ('ENGINEERING_MANAGER','Engineering Manager');
INSERT INTO app_permissions(permission_code,permission_name) VALUES
  ('VIEW_ENGINEER_TASK_CLOSEOUT_001A','View engineer task closeout'),
  ('MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A','Manage own engineer task closeout');

CREATE TABLE project_intake_documents(
  project_intake_document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_intake_request_id UUID NULL,
  project_id UUID NULL,
  document_type TEXT NOT NULL DEFAULT 'supporting',
  document_category TEXT NOT NULL DEFAULT 'supporting',
  document_status TEXT NOT NULL DEFAULT 'active',
  original_file_name TEXT NOT NULL,
  stored_file_name TEXT NOT NULL,
  storage_path TEXT NOT NULL,
  content_type TEXT NULL,
  size_bytes BIGINT NOT NULL DEFAULT 0,
  uploaded_by_user_id UUID NULL,
  upload_source TEXT NOT NULL,
  extraction_status TEXT NOT NULL DEFAULT 'not_started',
  engineering_visible BOOLEAN NOT NULL DEFAULT FALSE,
  ai_timesheet_context_enabled BOOLEAN NOT NULL DEFAULT FALSE,
  source_system TEXT NOT NULL DEFAULT '',
  external_reference_id TEXT NOT NULL DEFAULT '',
  pulse_ai_document_revision TEXT NOT NULL DEFAULT '',
  pulse_ai_effective_at TIMESTAMPTZ NULL,
  pulse_ai_processing_status TEXT NOT NULL DEFAULT 'not_requested',
  pulse_ai_active_version_id UUID NULL,
  pulse_ai_processing_updated_at TIMESTAMPTZ NULL,
  uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  CONSTRAINT ck_project_intake_documents_origin_owner CHECK (
    project_intake_request_id IS NOT NULL
    OR (upload_source='celar_ai_chat_attachment' AND uploaded_by_user_id IS NOT NULL)
  )
);

CREATE TABLE work_register_documents(
  work_register_document_id UUID PRIMARY KEY,
  project_id UUID NOT NULL,
  document_name TEXT NOT NULL,
  document_type TEXT NOT NULL,
  version_label TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'active',
  visibility TEXT NOT NULL DEFAULT 'project_team',
  effective_date DATE NULL,
  upload_source TEXT NOT NULL DEFAULT 'reference_link',
  original_file_name TEXT NULL,
  stored_file_path TEXT NULL,
  content_type TEXT NULL,
  file_size_bytes BIGINT NULL,
  created_by_user_id UUID NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO work_register_documents(
  work_register_document_id,project_id,document_name,document_type,version_label,status,
  visibility,effective_date,upload_source,original_file_name,stored_file_path,content_type,file_size_bytes,created_by_user_id)
VALUES
  ('10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','Approved SOW','Statement of Work','v1','active',
   'engineering_team','2026-08-07','local_file','approved-sow.pdf','/private/uploads/approved-sow.pdf','application/pdf',4096,'30000000-0000-0000-0000-000000000001'),
  ('10000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','Reference only','Technical Document','v1','active',
   'project_team','2026-08-07','reference_link',NULL,NULL,NULL,NULL,'30000000-0000-0000-0000-000000000001');
SQL

psql_exec -f "$MIGRATION" >/dev/null
first_applied="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='079_coordinated_runtime_ai_document_rbac_repair'")"
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='079_coordinated_runtime_ai_document_rbac_repair'")" migration_registered_once
assert_eq "$first_applied" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='079_coordinated_runtime_ai_document_rbac_repair'")" migration_timestamp_immutable
assert_eq 1 "$(value "SELECT COUNT(*) FROM project_intake_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'")" local_file_bridged_once
assert_eq 0 "$(value "SELECT COUNT(*) FROM project_intake_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000002'")" link_only_not_ingested
assert_eq 'sow|work_register_bridge|true|true|active|not_started|not_requested' "$(value "SELECT document_category||'|'||upload_source||'|'||engineering_visible||'|'||ai_timesheet_context_enabled||'|'||document_status||'|'||extraction_status||'|'||pulse_ai_processing_status FROM project_intake_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'")" sow_private_pipeline_contract
assert_eq 6 "$(value "SELECT COUNT(*) FROM module079_role_grants")" role_grants_recorded

psql_exec -qc "UPDATE project_intake_documents SET extraction_status='ready',pulse_ai_processing_status='ready',pulse_ai_active_version_id='40000000-0000-0000-0000-000000000001' WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'"
psql_exec -qc "UPDATE work_register_documents SET stored_file_path='/private/uploads/approved-sow-v2.pdf',version_label='v2' WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'"
assert_eq 'not_started|not_requested|' "$(value "SELECT extraction_status||'|'||pulse_ai_processing_status||'|'||COALESCE(pulse_ai_active_version_id::text,'') FROM project_intake_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'")" changed_file_requeued

psql_exec -qc "UPDATE work_register_documents SET status='archived' WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'"
assert_eq 'archived|false' "$(value "SELECT document_status||'|'||is_active FROM project_intake_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'")" archived_source_retired
psql_exec -qc "DELETE FROM work_register_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'"
assert_eq 'archived|false' "$(value "SELECT document_status||'|'||is_active FROM project_intake_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'")" deleted_source_preserves_archived_evidence

expect_failure guarded_rollback 'Rollback 079 refused' psql_exec -f "$ROLLBACK"
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='079_coordinated_runtime_ai_document_rbac_repair'")" guarded_rollback_preserves_migration

psql_exec -qc "DELETE FROM project_intake_documents WHERE upload_source='work_register_bridge' OR work_register_document_id IS NOT NULL"
psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='project_intake_documents' AND column_name='work_register_document_id'")" clean_rollback_removed_owner_column
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_role_permissions")" clean_rollback_removed_recorded_grants
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='079_coordinated_runtime_ai_document_rbac_repair'")" clean_rollback_removed_ledger

echo 'COORDINATED_DOCUMENT_BRIDGE_MIGRATION_079=PASS'
