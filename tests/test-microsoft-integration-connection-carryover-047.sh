#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-microsoft-carryover-047-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/047_microsoft_integration_connection_carryover.sql"
ROLLBACK="/workspace/database/rollback/047_microsoft_integration_connection_carryover_rollback.sql"

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
  description TEXT NOT NULL,
  applied_at TIMESTAMPTZ NOT NULL
);
CREATE TABLE app_users (user_id UUID PRIMARY KEY DEFAULT gen_random_uuid());
CREATE TABLE azure_entra_settings (
  azure_entra_settings_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id TEXT NULL,
  client_id TEXT NULL,
  authority_url TEXT NULL,
  redirect_uri TEXT NULL,
  graph_scope TEXT NOT NULL DEFAULT 'User.Read.All Directory.Read.All',
  sync_enabled BOOLEAN NOT NULL DEFAULT FALSE,
  default_role_code TEXT NOT NULL DEFAULT 'ENGINEER',
  sync_frequency_hours INTEGER NOT NULL DEFAULT 24,
  last_sync_at TIMESTAMPTZ NULL,
  last_sync_status TEXT NULL,
  last_sync_message TEXT NULL,
  updated_by_email TEXT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE projectpulse_native_admin_documents (
  module_number VARCHAR(3) NOT NULL,
  document_key VARCHAR(100) NOT NULL,
  document_json JSONB NOT NULL,
  revision_number BIGINT NOT NULL DEFAULT 0,
  updated_by UUID NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (module_number, document_key)
);
CREATE TABLE projectpulse_native_admin_document_revisions (
  revision_id UUID PRIMARY KEY,
  module_number VARCHAR(3) NOT NULL,
  document_key VARCHAR(100) NOT NULL,
  revision_number BIGINT NOT NULL,
  document_json JSONB NOT NULL,
  saved_by UUID NULL,
  saved_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  change_reason VARCHAR(50) NOT NULL,
  restored_from_revision_id UUID NULL,
  UNIQUE (module_number, document_key, revision_number)
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
CREATE TABLE microsoft_integration_sso_client_secrets (
  environment_mode TEXT PRIMARY KEY,
  tenant_key TEXT NOT NULL,
  ciphertext BYTEA NOT NULL,
  nonce BYTEA NOT NULL,
  authentication_tag BYTEA NOT NULL,
  fingerprint_sha256 TEXT NOT NULL,
  encryption_key_source TEXT NOT NULL,
  updated_by_user_id UUID NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE microsoft_integration_audit_events (
  microsoft_integration_audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  actor_user_id UUID NULL,
  actor_email TEXT NOT NULL DEFAULT '',
  action_code TEXT NOT NULL,
  tenant_key TEXT NOT NULL DEFAULT '',
  outcome_code TEXT NOT NULL,
  correlation_id TEXT NOT NULL DEFAULT '',
  event_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE scoped_role_policy_modules (
  module_code TEXT PRIMARY KEY,
  module_name TEXT NOT NULL,
  current_state TEXT NOT NULL DEFAULT '',
  permission_notes TEXT NOT NULL DEFAULT '',
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);

INSERT INTO azure_entra_settings (
  tenant_id, client_id, authority_url, redirect_uri, graph_scope,
  sync_enabled, default_role_code, sync_frequency_hours,
  last_sync_status, last_sync_message, updated_by_email
) VALUES (
  'eee4e6be-f544-4bab-be52-e6fa617ab524',
  'f30f7ad8-663d-447c-a8e6-ac908b738167',
  'https://login.microsoftonline.com/eee4e6be-f544-4bab-be52-e6fa617ab524',
  'https://projectpulse-test.onenecklab.com/auth/callback',
  'User.Read.All Directory.Read.All',
  FALSE,
  'ENGINEERING',
  24,
  'ready',
  'Existing Module 010 configuration',
  'admin@ussignal.com'
);
INSERT INTO projectpulse_native_admin_documents (
  module_number, document_key, document_json, revision_number
) VALUES
  ('065', 'configuration', '{"configuration":{"applicationId":"","tenantId":"","ownerTeam":"Platform Administration","notes":"legacy Module 065 metadata"}}', 2),
  ('067', 'configuration', '{"configuration":{"providerTarget":"microsoft_graph","smtpHost":"smtp.office365.com","smtpPort":587,"senderName":"ProjectPulse","senderAddress":"projectpulse-test@onenecklab.com","replyToAddress":"support@ussignal.com","recipientBoundary":"test_only"}}', 4);
INSERT INTO microsoft_integration_client_secrets (
  tenant_key, ciphertext, nonce, authentication_tag, fingerprint_sha256, encryption_key_source
) VALUES (
  'onenecklab', decode('01','hex'), decode('000000000000000000000000','hex'), decode('00000000000000000000000000000000','hex'), repeat('a',64), 'existing-graph-secret'
);
INSERT INTO microsoft_integration_sso_client_secrets (
  environment_mode, tenant_key, ciphertext, nonce, authentication_tag, fingerprint_sha256, encryption_key_source
) VALUES (
  'test', 'onenecklab', decode('02','hex'), decode('000000000000000000000000','hex'), decode('00000000000000000000000000000000','hex'), repeat('b',64), 'existing-sso-secret'
);
INSERT INTO scoped_role_policy_modules (module_code, module_name)
VALUES ('065', 'Microsoft Integration');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='047_microsoft_integration_connection_carryover';")" migration_registered_once
assert_eq 3 "$(value "SELECT revision_number FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration';")" carryover_created_one_revision
assert_eq 1 "$(value "SELECT COUNT(*) FROM projectpulse_native_admin_document_revisions WHERE module_number='065' AND document_key='configuration' AND revision_number=3;")" carryover_revision_recorded
assert_eq 'eee4e6be-f544-4bab-be52-e6fa617ab524' "$(value "SELECT document_json->'configuration'->>'tenantId' FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration';")" tenant_id_carried_over
assert_eq 'f30f7ad8-663d-447c-a8e6-ac908b738167' "$(value "SELECT (substring(document_json->'configuration'->>'notes' from length('PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:') + 1)::jsonb)->'tenants'->0->'services'->>'clientId' FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration';")" services_client_id_carried_over
assert_eq 'https://projectpulse-test.onenecklab.com/auth/callback' "$(value "SELECT (substring(document_json->'configuration'->>'notes' from length('PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:') + 1)::jsonb)->'tenants'->0->'sso'->>'redirectUri' FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration';")" redirect_uri_carried_over
assert_eq 'projectpulse-test@onenecklab.com' "$(value "SELECT (substring(document_json->'configuration'->>'notes' from length('PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:') + 1)::jsonb)->'mail'->>'senderAddress' FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration';")" mail_sender_carried_over
assert_eq 'services' "$(value "SELECT (substring(document_json->'configuration'->>'notes' from length('PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:') + 1)::jsonb)->'connectionOwnership'->>'module062IdentityProfile' FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration';")" module062_uses_services_connection
assert_eq 1 "$(value "SELECT COUNT(*) FROM azure_entra_settings WHERE tenant_id='eee4e6be-f544-4bab-be52-e6fa617ab524';")" module010_source_preserved
assert_eq 1 "$(value "SELECT COUNT(*) FROM projectpulse_native_admin_documents WHERE module_number='067' AND document_key='configuration';")" module067_source_preserved
assert_eq 1 "$(value "SELECT COUNT(*) FROM microsoft_integration_client_secrets WHERE tenant_key='onenecklab' AND encryption_key_source='existing-graph-secret';")" graph_secret_preserved
assert_eq 1 "$(value "SELECT COUNT(*) FROM microsoft_integration_sso_client_secrets WHERE environment_mode='test' AND encryption_key_source='existing-sso-secret';")" sso_secret_preserved
assert_eq 1 "$(value "SELECT COUNT(*) FROM microsoft_integration_audit_events WHERE action_code='LEGACY_CONFIGURATION_CARRIED_OVER' AND event_metadata->>'secretValuesChanged'='false';")" sanitized_carryover_audit_recorded
assert_eq 'Microsoft Integration Connection' "$(value "SELECT module_name FROM scoped_role_policy_modules WHERE module_code='065';")" module_catalog_renamed

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='047_microsoft_integration_connection_carryover';")" rollback_removed_registration
assert_eq 1 "$(value "SELECT COUNT(*) FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration' AND document_json->'configuration'->>'notes' LIKE 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:%';")" rollback_preserved_consolidated_document
assert_eq 1 "$(value "SELECT COUNT(*) FROM microsoft_integration_client_secrets WHERE tenant_key='onenecklab';")" rollback_preserved_graph_secret
assert_eq 1 "$(value "SELECT COUNT(*) FROM microsoft_integration_sso_client_secrets WHERE environment_mode='test';")" rollback_preserved_sso_secret

echo 'MICROSOFT_INTEGRATION_CONNECTION_CARRYOVER_047_TEST=PASS'
