#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-planning-authority-096-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION_CONTAINER="/workspace/database/migrations/096_project_planning_document_authority.sql"
ROLLBACK_CONTAINER="/workspace/database/rollback/096_project_planning_document_authority_rollback.sql"
CUMULATIVE_MIGRATION_RUNNER="$ROOT/scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh"

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
  "$ROOT/database/migrations/096_project_planning_document_authority.sql" \
  "$ROOT/database/rollback/096_project_planning_document_authority_rollback.sql" \
  "$CUMULATIVE_MIGRATION_RUNNER"; do
  test -s "$required" || { echo "ASSERTION_FAILED missing_artifact=$required" >&2; exit 1; }
done

# The governed Protected-Test deployment path must remain cumulative through the
# release migrations that extend Module Management, customer-source authority,
# and Module 025. This static contract prevents a future release from silently
# dropping 098/098/099 while retaining the older 096/097 migration test.
for cumulative_migration in \
  '098_module_management_owner_storage_reconciliation.sql' \
  '098_customer_directory_source_authority.sql' \
  '099_module025_sow_gsd_workspace.sql' \
  'MIGRATION_098_OWNER_STORAGE=APPLIED_AND_VERIFIED' \
  'MIGRATION_098_CUSTOMER_SOURCE=APPLIED_AND_VERIFIED' \
  'MIGRATION_099_MODULE025_SOW_GSD=APPLIED_AND_VERIFIED'; do
  grep -Fq "$cumulative_migration" "$CUMULATIVE_MIGRATION_RUNNER" || {
    echo "ASSERTION_FAILED governed_cumulative_migration_contract=$cumulative_migration" >&2
    exit 1
  }
done
echo 'ASSERTION_PASSED governed_cumulative_migration_contract=096-097-098-098-099'

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
echo 'ASSERTION_PASSED target_postgres_database_ready=true'

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations (
  migration_id text PRIMARY KEY,
  description text NOT NULL,
  applied_at timestamptz NOT NULL DEFAULT now()
);
SQL

psql_exec -f "$MIGRATION_CONTAINER" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='096_project_planning_document_authority';")" migration_registered
assert_eq "Durable project-document authority shared by Module 055C, FlowHive, and Project Forge." "$(value "SELECT description FROM schema_migrations WHERE migration_id='096_project_planning_document_authority';")" migration_description_recorded
assert_eq project_planning_document_authority "$(value "SELECT to_regclass('public.project_planning_document_authority');")" authority_table_created
assert_eq current_project_planning_document_authority "$(value "SELECT to_regclass('public.current_project_planning_document_authority');")" current_authority_view_created

psql_exec <<'SQL'
SELECT projectpulse_reconcile_project_planning_document_authority(
  '96000000-0000-0000-0000-000000000001',
  '96100000-0000-0000-0000-000000000001',
  '96200000-0000-0000-0000-000000000001',
  'statement_of_work','module_055c_work_register',
  '96300000-0000-0000-0000-000000000001',
  'SOW-v1.doc','1.0',repeat('a',64),'ready','ready','ready','{"kind":"sow-v1"}'::jsonb
);
SELECT projectpulse_reconcile_project_planning_document_authority(
  '96000000-0000-0000-0000-000000000001',
  '96100000-0000-0000-0000-000000000002',
  '96200000-0000-0000-0000-000000000002',
  'statement_of_work','module_055c_work_register',
  '96300000-0000-0000-0000-000000000002',
  'SOW-v2.doc','2.0',repeat('b',64),'ready','ready','ready','{"kind":"sow-v2"}'::jsonb
);
SELECT projectpulse_reconcile_project_planning_document_authority(
  '96000000-0000-0000-0000-000000000001',
  '96100000-0000-0000-0000-000000000003',
  '96200000-0000-0000-0000-000000000003',
  'gsd','module_055c_work_register',
  '96300000-0000-0000-0000-000000000003',
  'GSD.docx','1.0',repeat('c',64),'ready','ready','ready','{}'::jsonb
);
SELECT projectpulse_reconcile_project_planning_document_authority(
  '96000000-0000-0000-0000-000000000001',
  '96100000-0000-0000-0000-000000000004',
  '96200000-0000-0000-0000-000000000004',
  'architecture','module_055c_work_register',
  '96300000-0000-0000-0000-000000000004',
  'Architecture.pdf','1.0',repeat('d',64),'ready','ready','ready','{}'::jsonb
);
SELECT projectpulse_reconcile_project_planning_document_authority(
  '96000000-0000-0000-0000-000000000001',
  '96100000-0000-0000-0000-000000000005',
  '96200000-0000-0000-0000-000000000005',
  'validation','module_055c_work_register',
  '96300000-0000-0000-0000-000000000005',
  'Validation.pdf','1.0',repeat('e',64),'ready','ready','ready','{}'::jsonb
);
SQL

assert_eq 1 "$(value "SELECT COUNT(*) FROM current_project_planning_document_authority WHERE project_id='96000000-0000-0000-0000-000000000001' AND document_role='statement_of_work';")" one_current_sow
assert_eq SOW-v2.doc "$(value "SELECT source_file_name FROM current_project_planning_document_authority WHERE project_id='96000000-0000-0000-0000-000000000001' AND document_role='statement_of_work';")" newest_sow_current
assert_eq 1 "$(value "SELECT COUNT(*) FROM project_planning_document_authority WHERE document_id='96100000-0000-0000-0000-000000000001' AND NOT is_current AND superseded_at IS NOT NULL;")" prior_sow_superseded
assert_eq 1 "$(value "SELECT COUNT(*) FROM current_project_planning_document_authority WHERE project_id='96000000-0000-0000-0000-000000000001' AND document_role='gsd';")" one_current_gsd
assert_eq 2 "$(value "SELECT COUNT(*) FROM current_project_planning_document_authority WHERE project_id='96000000-0000-0000-0000-000000000001' AND document_role IN ('architecture','validation');")" multiple_supporting_roles_current

# A newer version of the same supporting document supersedes only that document.
psql_exec <<'SQL'
SELECT projectpulse_reconcile_project_planning_document_authority(
  '96000000-0000-0000-0000-000000000001',
  '96100000-0000-0000-0000-000000000004',
  '96200000-0000-0000-0000-000000000006',
  'architecture','module_055c_work_register',
  '96300000-0000-0000-0000-000000000004',
  'Architecture.pdf','2.0',repeat('f',64),'ready','ready','ready','{}'::jsonb
);
SQL
assert_eq 1 "$(value "SELECT COUNT(*) FROM current_project_planning_document_authority WHERE document_id='96100000-0000-0000-0000-000000000004';")" one_current_architecture_version
assert_eq 2.0 "$(value "SELECT source_version FROM current_project_planning_document_authority WHERE document_id='96100000-0000-0000-0000-000000000004';")" newest_architecture_version_current
assert_eq 1 "$(value "SELECT COUNT(*) FROM current_project_planning_document_authority WHERE document_id='96100000-0000-0000-0000-000000000005';")" independent_validation_remains_current

# Reapply is idempotent and preserves one migration registration.
psql_exec -f "$MIGRATION_CONTAINER" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='096_project_planning_document_authority';")" migration_reapply_idempotent
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_proc WHERE proname='projectpulse_reconcile_project_planning_document_authority';")" one_reconcile_function

psql_exec -f "$ROLLBACK_CONTAINER" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='096_project_planning_document_authority';")" rollback_unregisters_migration
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.project_planning_document_authority')::text,'');")" rollback_removes_authority_table
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.current_project_planning_document_authority')::text,'');")" rollback_removes_current_view
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_proc WHERE proname='projectpulse_reconcile_project_planning_document_authority';")" rollback_removes_reconcile_function

echo 'PROJECT_PLANNING_DOCUMENT_AUTHORITY_MIGRATION_096=PASS'
