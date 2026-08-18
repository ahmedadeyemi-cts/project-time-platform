#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-flowhive-sow-094-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION_CONTAINER="/workspace/database/migrations/094_flowhive_canonical_sow_authority.sql"
ROLLBACK_CONTAINER="/workspace/database/rollback/094_flowhive_canonical_sow_authority_rollback.sql"
MIGRATION_HOST="$ROOT/database/migrations/094_flowhive_canonical_sow_authority.sql"

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

docker run -d --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

# pg_isready can report that the server is accepting connections before the
# entrypoint has finished creating POSTGRES_DB. Wait for a real query against
# the exact target database so the test cannot race container initialization.
ready=false
for _ in $(seq 1 90); do
  if docker exec -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
      psql -Atqc 'SELECT 1;' -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
    ready=true
    break
  fi

  if ! docker ps --filter "name=$CONTAINER" --filter status=running \
      --format '{{.Names}}' | grep -qx "$CONTAINER"; then
    echo 'ASSERTION_FAILED postgres_container_stopped_before_database_ready' >&2
    docker logs "$CONTAINER" >&2 || true
    exit 1
  fi
  sleep 1
done

if [[ "$ready" != true ]]; then
  echo 'ASSERTION_FAILED target_postgres_database_not_ready' >&2
  docker logs "$CONTAINER" >&2 || true
  exit 1
fi
echo 'ASSERTION_PASSED target_postgres_database_ready=true'

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE schema_migrations (
    migration_id TEXT PRIMARY KEY,
    description TEXT NOT NULL DEFAULT '',
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO schema_migrations(migration_id, description)
VALUES
    ('079_coordinated_runtime_ai_document_rbac_repair', 'test prerequisite'),
    ('081_celar_ai_private_runtime_activation', 'test prerequisite'),
    ('086_module_066_flowhive_enterprise_pm', 'test prerequisite');

CREATE TABLE projects (
    project_id UUID PRIMARY KEY,
    project_code TEXT NOT NULL
);

CREATE TABLE work_register_documents (
    work_register_document_id UUID PRIMARY KEY,
    project_id UUID NOT NULL,
    document_type TEXT NOT NULL,
    upload_source TEXT NOT NULL,
    stored_file_path TEXT NOT NULL,
    status TEXT NOT NULL,
    effective_date DATE NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE project_intake_documents (
    project_intake_document_id UUID PRIMARY KEY,
    project_id UUID NULL,
    work_register_document_id UUID NULL,
    upload_source TEXT NOT NULL,
    document_type TEXT NOT NULL,
    document_category TEXT NOT NULL,
    engineering_visible BOOLEAN NOT NULL DEFAULT TRUE,
    pulse_ai_processing_status TEXT NOT NULL DEFAULT 'not_requested',
    pulse_ai_active_version_id UUID NULL,
    pulse_ai_effective_at TIMESTAMPTZ NULL,
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE pulse_ai_document_versions (
    pulse_ai_document_version_id UUID PRIMARY KEY,
    project_intake_document_id UUID NOT NULL,
    project_id UUID NULL,
    source_sha256 VARCHAR(64) NOT NULL,
    document_version VARCHAR(300) NOT NULL,
    authority_status VARCHAR(40) NOT NULL,
    index_status VARCHAR(40) NOT NULL
);
CREATE UNIQUE INDEX ux_test_one_canonical_project_version
    ON pulse_ai_document_versions(project_id, document_version)
    WHERE authority_status = 'canonical';

INSERT INTO projects(project_id, project_code)
VALUES ('94000000-0000-0000-0000-000000000001', 'TEST-094');

INSERT INTO work_register_documents(
    work_register_document_id, project_id, document_type, upload_source,
    stored_file_path, status, effective_date, created_at
)
VALUES
    ('94100000-0000-0000-0000-000000000001','94000000-0000-0000-0000-000000000001','SOW','local_file','work-register/sow-a-new.doc','active','2026-08-18','2026-08-18T10:00:00Z'),
    ('94100000-0000-0000-0000-000000000002','94000000-0000-0000-0000-000000000001','SOW','local_file','work-register/sow-a-old.docx','active','2026-08-17','2026-08-17T10:00:00Z'),
    ('94100000-0000-0000-0000-000000000003','94000000-0000-0000-0000-000000000001','SOW','local_file','work-register/sow-b.pdf','active','2026-08-16','2026-08-16T10:00:00Z'),
    ('94100000-0000-0000-0000-000000000004','94000000-0000-0000-0000-000000000001','SOW','local_file','work-register/revoked.pdf','active','2026-08-15','2026-08-15T10:00:00Z'),
    ('94100000-0000-0000-0000-000000000005','94000000-0000-0000-0000-000000000001','SOW','local_file','work-register/archived.pdf','archived','2026-08-14','2026-08-14T10:00:00Z');

INSERT INTO pulse_ai_document_versions(
    pulse_ai_document_version_id, project_intake_document_id, project_id,
    source_sha256, document_version, authority_status, index_status
)
VALUES
    ('94300000-0000-0000-0000-000000000001','94200000-0000-0000-0000-000000000001','94000000-0000-0000-0000-000000000001',repeat('a',64),'SOW-A','candidate','ready'),
    ('94300000-0000-0000-0000-000000000002','94200000-0000-0000-0000-000000000002','94000000-0000-0000-0000-000000000001',repeat('b',64),'SOW-A','candidate','ready'),
    ('94300000-0000-0000-0000-000000000003','94200000-0000-0000-0000-000000000003','94000000-0000-0000-0000-000000000001',repeat('c',64),'SOW-B','candidate','ready'),
    ('94300000-0000-0000-0000-000000000004','94200000-0000-0000-0000-000000000004','94000000-0000-0000-0000-000000000001',repeat('d',64),'SOW-REVOKED','revoked','ready'),
    ('94300000-0000-0000-0000-000000000005','94200000-0000-0000-0000-000000000005','94000000-0000-0000-0000-000000000001',repeat('e',64),'SOW-ARCHIVED','candidate','ready');

INSERT INTO project_intake_documents(
    project_intake_document_id, project_id, work_register_document_id,
    upload_source, document_type, document_category, engineering_visible,
    pulse_ai_processing_status, pulse_ai_active_version_id,
    pulse_ai_effective_at, uploaded_at, is_active
)
VALUES
    ('94200000-0000-0000-0000-000000000001','94000000-0000-0000-0000-000000000001','94100000-0000-0000-0000-000000000001','work_register_bridge','sow','sow',TRUE,'ready','94300000-0000-0000-0000-000000000001','2026-08-18T10:00:00Z','2026-08-18T10:00:00Z',TRUE),
    ('94200000-0000-0000-0000-000000000002','94000000-0000-0000-0000-000000000001','94100000-0000-0000-0000-000000000002','work_register_bridge','sow','sow',TRUE,'ready','94300000-0000-0000-0000-000000000002','2026-08-17T10:00:00Z','2026-08-17T10:00:00Z',TRUE),
    ('94200000-0000-0000-0000-000000000003','94000000-0000-0000-0000-000000000001','94100000-0000-0000-0000-000000000003','work_register_bridge','sow','sow',TRUE,'queued','94300000-0000-0000-0000-000000000003','2026-08-16T10:00:00Z','2026-08-16T10:00:00Z',TRUE),
    ('94200000-0000-0000-0000-000000000004','94000000-0000-0000-0000-000000000001','94100000-0000-0000-0000-000000000004','work_register_bridge','sow','sow',TRUE,'ready','94300000-0000-0000-0000-000000000004','2026-08-15T10:00:00Z','2026-08-15T10:00:00Z',TRUE),
    ('94200000-0000-0000-0000-000000000005','94000000-0000-0000-0000-000000000001','94100000-0000-0000-0000-000000000005','work_register_bridge','sow','sow',TRUE,'ready','94300000-0000-0000-0000-000000000005','2026-08-14T10:00:00Z','2026-08-14T10:00:00Z',TRUE);
SQL

psql_exec -f "$MIGRATION_CONTAINER" >/dev/null

assert_eq canonical "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000001';")" newest_equivalent_sow_becomes_canonical
assert_eq approved "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000002';")" duplicate_version_becomes_approved
assert_eq candidate "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000003';")" queued_document_not_promoted
assert_eq revoked "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000004';")" revoked_version_not_reactivated
assert_eq candidate "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000005';")" archived_source_not_promoted
assert_eq 2 "$(value "SELECT COUNT(*) FROM module094_flowhive_sow_authority_evidence;")" initial_promotion_evidence_count
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='094_flowhive_canonical_sow_authority';")" migration_registered

psql_exec -c "UPDATE project_intake_documents SET pulse_ai_processing_status='ready' WHERE project_intake_document_id='94200000-0000-0000-0000-000000000003';" >/dev/null
assert_eq canonical "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000003';")" ready_transition_trigger_promotes
assert_eq 3 "$(value "SELECT COUNT(*) FROM module094_flowhive_sow_authority_evidence;")" trigger_promotion_evidence_count

psql_exec -f "$MIGRATION_CONTAINER" >/dev/null
assert_eq 3 "$(value "SELECT COUNT(*) FROM module094_flowhive_sow_authority_evidence;")" migration_reapply_is_idempotent
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE tgname='trg_projectpulse094_reconcile_ready_work_register_sow' AND NOT tgisinternal;")" one_reconciliation_trigger_after_reapply

psql_exec -f "$ROLLBACK_CONTAINER" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE tgname='trg_projectpulse094_reconcile_ready_work_register_sow' AND NOT tgisinternal;")" rollback_removes_trigger
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='094_flowhive_canonical_sow_authority';")" rollback_unregisters_migration
assert_eq 3 "$(value "SELECT COUNT(*) FROM module094_flowhive_sow_authority_evidence;")" rollback_preserves_durable_evidence
assert_eq canonical "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000001';")" rollback_does_not_revoke_retained_authority

psql_exec <<'SQL'
INSERT INTO work_register_documents(
    work_register_document_id, project_id, document_type, upload_source,
    stored_file_path, status, effective_date, created_at
)
VALUES ('94100000-0000-0000-0000-000000000006','94000000-0000-0000-0000-000000000001','SOW','local_file','work-register/sow-c.pdf','active','2026-08-19','2026-08-19T10:00:00Z');
INSERT INTO pulse_ai_document_versions(
    pulse_ai_document_version_id, project_intake_document_id, project_id,
    source_sha256, document_version, authority_status, index_status
)
VALUES ('94300000-0000-0000-0000-000000000006','94200000-0000-0000-0000-000000000006','94000000-0000-0000-0000-000000000001',repeat('f',64),'SOW-C','candidate','ready');
INSERT INTO project_intake_documents(
    project_intake_document_id, project_id, work_register_document_id,
    upload_source, document_type, document_category, engineering_visible,
    pulse_ai_processing_status, pulse_ai_active_version_id,
    pulse_ai_effective_at, uploaded_at, is_active
)
VALUES ('94200000-0000-0000-0000-000000000006','94000000-0000-0000-0000-000000000001','94100000-0000-0000-0000-000000000006','work_register_bridge','sow','sow',TRUE,'queued','94300000-0000-0000-0000-000000000006','2026-08-19T10:00:00Z','2026-08-19T10:00:00Z',TRUE);
UPDATE project_intake_documents
SET pulse_ai_processing_status='ready'
WHERE project_intake_document_id='94200000-0000-0000-0000-000000000006';
SQL
assert_eq candidate "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000006';")" rollback_stops_future_automatic_promotion

psql_exec -f "$MIGRATION_CONTAINER" >/dev/null
assert_eq canonical "$(value "SELECT authority_status FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='94300000-0000-0000-0000-000000000006';")" safe_reapply_backfills_new_ready_sow
assert_eq 4 "$(value "SELECT COUNT(*) FROM module094_flowhive_sow_authority_evidence;")" safe_reapply_retains_and_extends_evidence

if grep -Eiq 'chunk_text|section_text|raw_document|document_text' "$MIGRATION_HOST"; then
  echo 'ASSERTION_FAILED migration_must_not_read_or_store_raw_document_text' >&2
  exit 1
fi

echo 'ASSERTION_PASSED migration_must_not_read_or_store_raw_document_text'
echo 'FLOWHIVE_CANONICAL_SOW_AUTHORITY_MIGRATION_094=PASS'
