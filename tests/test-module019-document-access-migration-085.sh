#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-module019-085-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/085_module_019_document_access_storage_repair.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql --no-psqlrc -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
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

command -v docker >/dev/null || { echo 'docker is required' >&2; exit 1; }

docker run --detach --rm --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  psql_exec -Atqc 'SELECT 1' >/dev/null 2>&1 && break
  [[ "$attempt" != 60 ]] || {
    docker logs "$CONTAINER" >&2 || true
    exit 1
  }
  sleep 1
done

psql_exec <<'SQL'
CREATE TABLE schema_migrations(
  migration_id TEXT PRIMARY KEY,
  description TEXT NOT NULL DEFAULT '',
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO schema_migrations(migration_id)
VALUES ('079_coordinated_runtime_ai_document_rbac_repair');

CREATE TABLE project_intake_documents(
  project_intake_document_id UUID PRIMARY KEY,
  project_intake_request_id UUID NULL,
  project_id UUID NULL,
  storage_path TEXT NOT NULL,
  uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE work_register_documents(
  work_register_document_id UUID PRIMARY KEY,
  stored_file_path TEXT NULL
);

CREATE TABLE project_assignments(
  project_assignment_id UUID PRIMARY KEY,
  project_id UUID NOT NULL,
  user_id UUID NOT NULL,
  effective_start_date DATE NOT NULL,
  effective_end_date DATE NULL
);

CREATE TABLE engineering_resource_requests(
  engineering_resource_request_id UUID PRIMARY KEY,
  project_id UUID NULL,
  project_intake_request_id UUID NULL,
  request_status TEXT NOT NULL DEFAULT 'requested'
);

CREATE TABLE engineering_resource_request_assignments(
  engineering_resource_request_assignment_id UUID PRIMARY KEY,
  engineering_resource_request_id UUID NOT NULL,
  user_id UUID NOT NULL
);

-- Reproduce the protected-Test schema behavior that exposed the original
-- migration defect: an UPDATE queues a deferred trigger event on the intake
-- document table. Any CREATE INDEX attempted afterward in the same transaction
-- would fail with "pending trigger events".
CREATE OR REPLACE FUNCTION test_module019_deferred_intake_event()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $test_module019_deferred_intake_event_body$
BEGIN
  RETURN NEW;
END;
$test_module019_deferred_intake_event_body$;

CREATE CONSTRAINT TRIGGER trg_test_module019_deferred_intake_event
AFTER UPDATE ON project_intake_documents
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION test_module019_deferred_intake_event();

INSERT INTO work_register_documents(work_register_document_id, stored_file_path) VALUES
  ('10000000-0000-0000-0000-000000000001', '/opt/project-time-platform/uploads/work-register/20000000/doc-a.pdf'),
  ('10000000-0000-0000-0000-000000000002', 'uploads/project-intake/30000000/doc-b.pdf'),
  ('10000000-0000-0000-0000-000000000003', '../unsafe/doc-c.pdf');

INSERT INTO project_intake_documents(
  project_intake_document_id, project_intake_request_id, project_id, storage_path
) VALUES
  ('40000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', NULL,
   '/opt/projectpulse/uploads/project-intake/50000000/doc-d.pdf'),
  ('40000000-0000-0000-0000-000000000002', NULL, '60000000-0000-0000-0000-000000000001',
   'project-intake/60000000/doc-e.pdf'),
  ('40000000-0000-0000-0000-000000000003', NULL, '60000000-0000-0000-0000-000000000002',
   '/outside/no-upload-marker/doc-f.pdf');
SQL

psql_exec -f "$MIGRATION" >/dev/null
first_applied="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='085_module_019_document_access_storage_repair'")"
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='085_module_019_document_access_storage_repair'")" migration_registered_once
assert_eq "$first_applied" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='085_module_019_document_access_storage_repair'")" migration_timestamp_immutable

assert_eq 'work-register/20000000/doc-a.pdf' "$(value "SELECT stored_file_path FROM work_register_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000001'")" legacy_work_register_path_normalized
assert_eq 'project-intake/30000000/doc-b.pdf' "$(value "SELECT stored_file_path FROM work_register_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000002'")" uploads_prefix_removed
assert_eq '../unsafe/doc-c.pdf' "$(value "SELECT stored_file_path FROM work_register_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000003'")" unsafe_relative_path_not_redirected
assert_eq 'project-intake/50000000/doc-d.pdf' "$(value "SELECT storage_path FROM project_intake_documents WHERE project_intake_document_id='40000000-0000-0000-0000-000000000001'")" legacy_intake_path_normalized
assert_eq 'project-intake/60000000/doc-e.pdf' "$(value "SELECT storage_path FROM project_intake_documents WHERE project_intake_document_id='40000000-0000-0000-0000-000000000002'")" canonical_path_preserved
assert_eq '/outside/no-upload-marker/doc-f.pdf' "$(value "SELECT storage_path FROM project_intake_documents WHERE project_intake_document_id='40000000-0000-0000-0000-000000000003'")" unknown_absolute_path_not_redirected

psql_exec <<'SQL'
INSERT INTO work_register_documents(work_register_document_id, stored_file_path)
VALUES ('10000000-0000-0000-0000-000000000004', E'C:\\legacy\\uploads\\work-register\\70000000\\doc-g.pdf');

INSERT INTO project_intake_documents(
  project_intake_document_id, project_intake_request_id, project_id, storage_path
) VALUES (
  '40000000-0000-0000-0000-000000000004',
  '50000000-0000-0000-0000-000000000004',
  NULL,
  E'uploads\\project-intake\\50000000\\doc-h.pdf'
);
SQL

assert_eq 'work-register/70000000/doc-g.pdf' "$(value "SELECT stored_file_path FROM work_register_documents WHERE work_register_document_id='10000000-0000-0000-0000-000000000004'")" future_055c_insert_normalized
assert_eq 'project-intake/50000000/doc-h.pdf' "$(value "SELECT storage_path FROM project_intake_documents WHERE project_intake_document_id='40000000-0000-0000-0000-000000000004'")" future_055d_insert_normalized

assert_eq 2 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE NOT tgisinternal AND tgname IN ('trg_projectpulse085_normalize_work_register_upload_path','trg_projectpulse085_normalize_intake_upload_path')")" normalization_triggers_active
assert_eq 5 "$(value "SELECT COUNT(*) FROM pg_indexes WHERE schemaname='public' AND indexname LIKE 'ix_piw085_%'")" authorization_indexes_created
assert_eq 'https://example.invalid/file.pdf' "$(value "SELECT projectpulse085_normalize_upload_path('https://example.invalid/file.pdf')")" url_not_redirected
assert_eq '../outside.pdf' "$(value "SELECT projectpulse085_normalize_upload_path('../outside.pdf')")" traversal_not_redirected

echo 'MODULE019_DOCUMENT_ACCESS_MIGRATION_085=PASS'
