#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-microsoft-sso-046-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/046_microsoft_sso_connection_profiles.sql"
ROLLBACK="/workspace/database/rollback/046_microsoft_sso_connection_profiles_rollback.sql"

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
  local expected="$1"
  local label="$2"
  local log="/tmp/microsoft-sso-046-${label}.log"
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
CREATE TABLE schema_migrations (
  migration_id TEXT PRIMARY KEY,
  description TEXT NOT NULL DEFAULT '',
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE microsoft_integration_client_secrets (
  tenant_key TEXT PRIMARY KEY,
  ciphertext BYTEA NOT NULL,
  nonce BYTEA NOT NULL,
  authentication_tag BYTEA NOT NULL,
  fingerprint_sha256 TEXT NOT NULL,
  encryption_key_source TEXT NOT NULL,
  updated_by_user_id UUID NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO microsoft_integration_client_secrets (
  tenant_key,ciphertext,nonce,authentication_tag,fingerprint_sha256,encryption_key_source
) VALUES (
  'onenecklab',decode('01','hex'),decode('000000000000000000000000','hex'),decode('00000000000000000000000000000000','hex'),repeat('a',64),'existing-graph-service'
);
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='046_microsoft_sso_connection_profiles';")" migration_registered_once
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename='microsoft_integration_sso_client_secrets';")" sso_secret_table_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM microsoft_integration_client_secrets WHERE tenant_key='onenecklab' AND encryption_key_source='existing-graph-service';")" graph_service_secret_preserved

psql_exec <<'SQL'
INSERT INTO microsoft_integration_sso_client_secrets (
  environment_mode,tenant_key,ciphertext,nonce,authentication_tag,fingerprint_sha256,encryption_key_source
) VALUES
  ('test','onenecklab',decode('02','hex'),decode('000000000000000000000000','hex'),decode('00000000000000000000000000000000','hex'),repeat('b',64),'test-sso'),
  ('production','ussignal',decode('03','hex'),decode('000000000000000000000000','hex'),decode('00000000000000000000000000000000','hex'),repeat('c',64),'production-sso');
SQL
assert_eq 2 "$(value "SELECT COUNT(*) FROM microsoft_integration_sso_client_secrets;")" test_and_production_sso_secrets_supported
expect_file_failure 'Rollback blocked: Microsoft SSO App Registration secret metadata exists.' sso_secret_blocks_rollback

psql_exec -c 'DELETE FROM microsoft_integration_sso_client_secrets;' >/dev/null
psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='046_microsoft_sso_connection_profiles';")" safe_rollback_removed_registration
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.microsoft_integration_sso_client_secrets')::text,'');")" safe_rollback_removed_sso_table
assert_eq 1 "$(value "SELECT COUNT(*) FROM microsoft_integration_client_secrets WHERE tenant_key='onenecklab';")" safe_rollback_preserved_graph_service_secret

echo 'MICROSOFT_SSO_CONNECTION_MIGRATION_046_TEST=PASS'
