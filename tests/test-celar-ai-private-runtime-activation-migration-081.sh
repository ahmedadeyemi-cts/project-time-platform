#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-private-runtime-081-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/081_celar_ai_private_runtime_activation.sql"
ROLLBACK="/workspace/database/rollback/081_celar_ai_private_runtime_activation_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() { docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"; }
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }
  echo "ASSERTION_PASSED $label=$actual"
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
  ('079_coordinated_runtime_ai_document_rbac_repair'),
  ('080_celar_ai_internal_data_intelligence');

CREATE TABLE app_users(
  user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  entra_object_id TEXT UNIQUE,
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  employee_number TEXT,
  job_title TEXT,
  department TEXT,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE app_roles(
  app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  role_code TEXT NOT NULL UNIQUE,
  role_name TEXT NOT NULL,
  role_description TEXT,
  is_system_role BOOLEAN NOT NULL DEFAULT TRUE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  display_order INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE app_permissions(
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code TEXT NOT NULL UNIQUE,
  permission_name TEXT NOT NULL,
  module_code TEXT NOT NULL DEFAULT '011',
  permission_description TEXT
);
CREATE TABLE app_role_permissions(
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles,
  app_permission_id UUID NOT NULL REFERENCES app_permissions,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(app_role_id,app_permission_id)
);
CREATE TABLE app_user_role_assignments(
  app_user_role_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES app_users,
  app_role_id UUID NOT NULL REFERENCES app_roles,
  assigned_by_user_id UUID NULL REFERENCES app_users,
  assignment_reason TEXT,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(user_id,app_role_id)
);
INSERT INTO app_permissions(permission_code,permission_name) VALUES
  ('QUEUE_PULSE_AI_DOCUMENT_PROCESSING','Queue private documents'),
  ('MANAGE_ALL','Manage everything');

CREATE TABLE work_register_documents(
  work_register_document_id UUID PRIMARY KEY,
  document_name TEXT NOT NULL,
  original_file_name TEXT,
  stored_file_path TEXT,
  upload_source TEXT NOT NULL DEFAULT 'local_file'
);
CREATE TABLE project_intake_documents(
  project_intake_document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  work_register_document_id UUID NULL REFERENCES work_register_documents,
  upload_source TEXT NOT NULL,
  original_file_name TEXT NOT NULL,
  extraction_status TEXT NOT NULL DEFAULT 'not_started',
  pulse_ai_processing_status TEXT NOT NULL DEFAULT 'not_requested',
  pulse_ai_active_version_id UUID NULL,
  pulse_ai_processing_error_code TEXT NULL,
  pulse_ai_processing_updated_at TIMESTAMPTZ NULL
);

INSERT INTO work_register_documents VALUES
  ('10000000-0000-0000-0000-000000000001','Approved SOW','Approved SOW','/private/uploads/customer-sow.PDF','local_file'),
  ('10000000-0000-0000-0000-000000000002','Unproven file','Unproven file','/private/uploads/blob.bin','local_file');
INSERT INTO project_intake_documents(
  work_register_document_id,upload_source,original_file_name,
  extraction_status,pulse_ai_processing_status,pulse_ai_active_version_id,pulse_ai_processing_error_code
) VALUES
  ('10000000-0000-0000-0000-000000000001','work_register_bridge','Approved SOW','ready','ready','20000000-0000-0000-0000-000000000001','old_error'),
  ('10000000-0000-0000-0000-000000000002','work_register_bridge','Unproven file','not_started','not_requested',NULL,NULL);
SQL

psql_exec -f "$MIGRATION" >/dev/null
first_applied="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='081_celar_ai_private_runtime_activation'")"
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='081_celar_ai_private_runtime_activation'")" migration_registered_once
assert_eq "$first_applied" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='081_celar_ai_private_runtime_activation'")" migration_timestamp_immutable
assert_eq 'Approved SOW.pdf|not_started|not_requested||' "$(value "SELECT original_file_name||'|'||extraction_status||'|'||pulse_ai_processing_status||'|'||COALESCE(pulse_ai_active_version_id::text,'')||'|'||COALESCE(pulse_ai_processing_error_code,'') FROM project_intake_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'")" supported_extension_repaired_and_requeued
assert_eq 'Unproven file' "$(value "SELECT original_file_name FROM project_intake_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000002'")" unsupported_extension_not_invented
assert_eq 'Approved SOW.pdf' "$(value "SELECT projectpulse081_supported_file_name('Approved SOW.pdf','/private/uploads/customer-sow.docx')")" proven_original_extension_preserved
assert_eq 'Architecture.docx' "$(value "SELECT projectpulse081_supported_file_name('Architecture','/private/uploads/architecture.DOCX')")" supported_stored_extension_derived
assert_eq 'true|true|true' "$(value "SELECT service_user.is_active||'|'||service_role.is_active||'|'||assignment.is_active FROM app_users service_user JOIN app_user_role_assignments assignment ON assignment.user_id=service_user.user_id JOIN app_roles service_role ON service_role.app_role_id=assignment.app_role_id WHERE service_user.user_id='08100000-0000-0000-0000-000000000001'")" service_identity_active
assert_eq 'QUEUE_PULSE_AI_DOCUMENT_PROCESSING' "$(value "SELECT string_agg(permission.permission_code,',' ORDER BY permission.permission_code) FROM app_roles role JOIN app_role_permissions grant_row ON grant_row.app_role_id=role.app_role_id JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id WHERE role.role_code='CELAR_AI_DOCUMENT_SERVICE'")" service_role_least_privilege

psql_exec <<'SQL'
CREATE TABLE pulse_ai_document_processing_jobs(requested_by_user_id UUID NOT NULL);
INSERT INTO pulse_ai_document_processing_jobs(requested_by_user_id)
VALUES ('08100000-0000-0000-0000-000000000001');
SQL

rollback_log="$(mktemp)"
if psql_exec -f "$ROLLBACK" >"$rollback_log" 2>&1; then
  echo 'ASSERTION_FAILED guarded_rollback accepted owned processing evidence' >&2
  exit 1
fi
grep -Fq 'Rollback 081 refused: the Celar AI document service identity owns processing evidence.' "$rollback_log"
rm -f "$rollback_log"
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='081_celar_ai_private_runtime_activation'")" guarded_rollback_preserved_ledger
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_users WHERE user_id='08100000-0000-0000-0000-000000000001'")" guarded_rollback_preserved_service_user

psql_exec -c "DELETE FROM pulse_ai_document_processing_jobs WHERE requested_by_user_id='08100000-0000-0000-0000-000000000001'" >/dev/null
psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_users WHERE user_id='08100000-0000-0000-0000-000000000001'")" rollback_removed_created_service_user
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles WHERE role_code='CELAR_AI_DOCUMENT_SERVICE'")" rollback_removed_created_service_role
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='081_celar_ai_private_runtime_activation'")" rollback_removed_ledger

echo 'CELAR_AI_PRIVATE_RUNTIME_ACTIVATION_MIGRATION_081=PASS'
