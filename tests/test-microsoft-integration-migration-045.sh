#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-microsoft-045-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/045_microsoft_integration_consolidation.sql"
ROLLBACK="/workspace/database/rollback/045_microsoft_integration_consolidation_rollback.sql"

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
expect_file_failure() {
  local expected="$1" label="$2" log="/tmp/microsoft-integration-045-${label}.log"
  if psql_exec -f "$ROLLBACK" >"$log" 2>&1; then
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
CREATE TABLE scoped_role_policy_modules (
  module_code TEXT PRIMARY KEY,
  module_name TEXT NOT NULL,
  route_scope TEXT NOT NULL DEFAULT '',
  current_state TEXT NOT NULL DEFAULT '',
  permission_notes TEXT NOT NULL DEFAULT '',
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE projectpulse_native_admin_documents (
  module_number TEXT NOT NULL,
  document_key TEXT NOT NULL,
  document_json JSONB NOT NULL DEFAULT '{}'::jsonb,
  PRIMARY KEY (module_number, document_key)
);
INSERT INTO scoped_role_policy_modules (
  module_code,module_name,route_scope,current_state,permission_notes,is_active
) VALUES
  ('065','Entra Secret Administration','entra-secret-administration','Installed fail-closed','legacy 065',TRUE),
  ('067','Global Mail Configuration Center','global-mail-configuration','Installed read-only','legacy 067',TRUE);
INSERT INTO projectpulse_native_admin_documents (module_number,document_key,document_json)
VALUES ('067','configuration','{"senderAddress":"preserved@example.test"}'::jsonb);
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='045_microsoft_integration_consolidation';")" migration_registered_once
assert_eq 3 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('microsoft_integration_client_secrets','microsoft_integration_audit_events','microsoft_integration_permission_aliases');")" integration_tables_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE tgname='trg_projectpulse045_microsoft_integration_audit_immutable' AND NOT tgisinternal;")" immutable_audit_trigger_created
assert_eq 4 "$(value "SELECT COUNT(*) FROM microsoft_integration_permission_aliases WHERE legacy_module_code='067' AND active_module_code='065';")" permission_aliases_created
assert_eq 'Microsoft Integration|entra-secret-administration|t' "$(value "SELECT module_name || '|' || route_scope || '|' || is_active::text FROM scoped_role_policy_modules WHERE module_code='065';")" module_065_consolidated
assert_eq 'f' "$(value "SELECT is_active::text FROM scoped_role_policy_modules WHERE module_code='067';")" module_067_retired
assert_eq 1 "$(value "SELECT COUNT(*) FROM projectpulse_native_admin_documents WHERE module_number='067' AND document_key='configuration';")" legacy_configuration_preserved

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='045_microsoft_integration_consolidation';")" safe_rollback_removed_registration
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.microsoft_integration_client_secrets')::text,'');")" safe_rollback_removed_secret_table
assert_eq 'Entra Secret Administration|t' "$(value "SELECT module_name || '|' || is_active::text FROM scoped_role_policy_modules WHERE module_code='065';")" safe_rollback_restored_module_065
assert_eq 'Global Mail Configuration Center|t' "$(value "SELECT module_name || '|' || is_active::text FROM scoped_role_policy_modules WHERE module_code='067';")" safe_rollback_restored_module_067
assert_eq 1 "$(value "SELECT COUNT(*) FROM projectpulse_native_admin_documents WHERE module_number='067' AND document_key='configuration';")" rollback_preserved_legacy_configuration

psql_exec -f "$MIGRATION" >/dev/null
psql_exec <<'SQL'
INSERT INTO microsoft_integration_client_secrets (
  tenant_key,ciphertext,nonce,authentication_tag,fingerprint_sha256,encryption_key_source
) VALUES (
  'tenant-test',decode('00','hex'),decode('000000000000000000000000','hex'),decode('00000000000000000000000000000000','hex'),repeat('a',64),'test'
);
SQL
expect_file_failure 'Rollback blocked: Microsoft Integration client-secret metadata exists.' secret_metadata_blocks_rollback
psql_exec -c "DELETE FROM microsoft_integration_client_secrets;" >/dev/null
psql_exec <<'SQL'
INSERT INTO microsoft_integration_audit_events (
  actor_email,action_code,tenant_key,outcome_code,correlation_id,event_metadata
) VALUES ('admin@example.test','TEST','tenant-test','success','migration-test','{}'::jsonb);
SQL
expect_file_failure 'Rollback blocked: immutable Microsoft Integration audit evidence exists.' audit_evidence_blocks_rollback

echo 'MICROSOFT_INTEGRATION_MIGRATION_045_TEST=PASS'
