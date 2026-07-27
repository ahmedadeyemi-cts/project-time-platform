#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="c38edbb63f50bf736092e3f71c581eead5bdb13a"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
VERIFY_SQL=""

fail() { echo "ERROR: $*" >&2; exit 1; }
cleanup() {
  if [[ -n "$VERIFY_SQL" && -f "$VERIFY_SQL" ]]; then
    rm -f "$VERIFY_SQL"
  fi
}
trap cleanup EXIT INT TERM

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."
command -v mktemp >/dev/null || fail "mktemp is required."

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
MIGRATIONS=(
  "045_microsoft_integration_consolidation.sql"
  "046_microsoft_sso_connection_profiles.sql"
  "047_microsoft_integration_connection_carryover.sql"
)
MIGRATION_IDS=(
  "045_microsoft_integration_consolidation"
  "046_microsoft_sso_connection_profiles"
  "047_microsoft_integration_connection_carryover"
)
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"
for migration in "${MIGRATIONS[@]}"; do
  [[ -f "$MIGRATION_ROOT/$migration" ]] || fail "Required migration is missing: $migration"
done
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "3" ]] || fail "Migration checksum manifest must contain exactly three SQL files."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "MICROSOFT_INTEGRATION_MIGRATION_CHECKSUMS=VERIFIED"

table_exists() {
  local table="$1"
  [[ "$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT to_regclass('public.$table') IS NOT NULL;")" == "t" ]]
}

count_if_table() {
  local table="$1"
  if table_exists "$table"; then
    psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM $table;"
  else
    printf '0\n'
  fi
}

source_hash() {
  local expression="$1"
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT md5(COALESCE(($expression)::text, ''));"
}

read -r USERS_BEFORE ROLES_BEFORE DOCUMENTS_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM app_roles),
      (SELECT COUNT(*) FROM projectpulse_native_admin_documents);" | tr '|' ' '
)"
[[ -n "${USERS_BEFORE:-}" ]] || fail "Required ProjectPulse operational tables are unavailable."

GRAPH_SECRET_ROWS_BEFORE="$(count_if_table microsoft_integration_client_secrets)"
SSO_SECRET_ROWS_BEFORE="$(count_if_table microsoft_integration_sso_client_secrets)"
AUDIT_ROWS_BEFORE="$(count_if_table microsoft_integration_audit_events)"
if table_exists microsoft_integration_audit_events; then
  CARRYOVER_AUDIT_ROWS_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM microsoft_integration_audit_events WHERE action_code='LEGACY_CONFIGURATION_CARRIED_OVER';")"
else
  CARRYOVER_AUDIT_ROWS_BEFORE=0
fi
MODULE010_ROWS_BEFORE="$(count_if_table azure_entra_settings)"
MODULE067_DOCUMENTS_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM projectpulse_native_admin_documents WHERE module_number='067' AND document_key='configuration';")"
MODULE010_SOURCE_HASH_BEFORE="$(source_hash "SELECT to_jsonb(settings) FROM azure_entra_settings settings ORDER BY settings.updated_at DESC NULLS LAST, settings.created_at DESC NULLS LAST LIMIT 1")"
MODULE067_SOURCE_HASH_BEFORE="$(source_hash "SELECT document_json FROM projectpulse_native_admin_documents WHERE module_number='067' AND document_key='configuration' LIMIT 1")"
MODULE065_MARKER_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
  SELECT EXISTS (
    SELECT 1 FROM projectpulse_native_admin_documents
    WHERE module_number='065'
      AND document_key='configuration'
      AND COALESCE(document_json->'configuration'->>'notes','') LIKE 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:%'
  );")"

for value in "$USERS_BEFORE" "$ROLES_BEFORE" "$DOCUMENTS_BEFORE" "$GRAPH_SECRET_ROWS_BEFORE" "$SSO_SECRET_ROWS_BEFORE" "$AUDIT_ROWS_BEFORE" "$CARRYOVER_AUDIT_ROWS_BEFORE" "$MODULE010_ROWS_BEFORE" "$MODULE067_DOCUMENTS_BEFORE"; do
  [[ "$value" =~ ^[0-9]+$ ]] || fail "Existing Microsoft Integration baseline values are invalid."
done
[[ "$MODULE010_SOURCE_HASH_BEFORE" =~ ^[0-9a-f]{32}$ ]] || fail "Module 010 source hash is invalid."
[[ "$MODULE067_SOURCE_HASH_BEFORE" =~ ^[0-9a-f]{32}$ ]] || fail "Module 067 source hash is invalid."
[[ "$MODULE065_MARKER_BEFORE" == "t" || "$MODULE065_MARKER_BEFORE" == "f" ]] || fail "Module 065 marker state is invalid."

echo "MICROSOFT_INTEGRATION_EVIDENCE_BASELINE graphSecrets=$GRAPH_SECRET_ROWS_BEFORE ssoSecrets=$SSO_SECRET_ROWS_BEFORE audit=$AUDIT_ROWS_BEFORE carryoverAudit=$CARRYOVER_AUDIT_ROWS_BEFORE module010Rows=$MODULE010_ROWS_BEFORE module067Documents=$MODULE067_DOCUMENTS_BEFORE module065Marker=$MODULE065_MARKER_BEFORE"

for index in "${!MIGRATIONS[@]}"; do
  migration="${MIGRATIONS[$index]}"
  migration_id="${MIGRATION_IDS[$index]}"
  registered="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='$migration_id');")"
  if [[ "$registered" == "t" ]]; then
    echo "VERIFY_ALREADY_REGISTERED=$migration_id"
  else
    echo "APPLY=$migration"
    psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION_ROOT/$migration"
  fi
done

EXPECTED_AUDIT_ROWS_AFTER="$AUDIT_ROWS_BEFORE"
EXPECTED_CARRYOVER_AUDIT_ROWS_AFTER="$CARRYOVER_AUDIT_ROWS_BEFORE"
if [[ "$MODULE065_MARKER_BEFORE" != "t" ]]; then
  EXPECTED_AUDIT_ROWS_AFTER=$((AUDIT_ROWS_BEFORE + 1))
  EXPECTED_CARRYOVER_AUDIT_ROWS_AFTER=$((CARRYOVER_AUDIT_ROWS_BEFORE + 1))
fi
MAX_DOCUMENTS_AFTER=$((DOCUMENTS_BEFORE + 1))

VERIFY_SQL="$(mktemp)"
cat > "$VERIFY_SQL" <<SQL
CREATE TEMP TABLE projectpulse_microsoft_verification_baseline AS
SELECT
  ${USERS_BEFORE}::bigint AS users_before,
  ${ROLES_BEFORE}::bigint AS roles_before,
  ${DOCUMENTS_BEFORE}::bigint AS documents_before,
  ${MAX_DOCUMENTS_AFTER}::bigint AS max_documents_after,
  ${GRAPH_SECRET_ROWS_BEFORE}::bigint AS graph_secret_rows_before,
  ${SSO_SECRET_ROWS_BEFORE}::bigint AS sso_secret_rows_before,
  ${EXPECTED_AUDIT_ROWS_AFTER}::bigint AS expected_audit_rows_after,
  ${EXPECTED_CARRYOVER_AUDIT_ROWS_AFTER}::bigint AS expected_carryover_audit_rows_after,
  ${MODULE010_ROWS_BEFORE}::bigint AS module010_rows_before,
  ${MODULE067_DOCUMENTS_BEFORE}::bigint AS module067_documents_before,
  '${MODULE010_SOURCE_HASH_BEFORE}'::text AS module010_source_hash_before,
  '${MODULE067_SOURCE_HASH_BEFORE}'::text AS module067_source_hash_before;
SQL

cat >> "$VERIFY_SQL" <<'SQL'
DO $verify_microsoft_connection_carryover$
DECLARE
  baseline record;
  users_after bigint;
  roles_after bigint;
  documents_after bigint;
  graph_secret_rows_after bigint;
  sso_secret_rows_after bigint;
  audit_rows_after bigint;
  carryover_audit_rows_after bigint;
  aliases_count bigint;
  module010_rows_after bigint;
  module067_documents_after bigint;
  module010_source_hash_after text;
  module067_source_hash_after text;
  notes text;
  consolidated jsonb;
  active_tenant jsonb;
  source_settings jsonb;
  legacy_mail jsonb;
BEGIN
  SELECT * INTO baseline FROM projectpulse_microsoft_verification_baseline LIMIT 1;

  SELECT COUNT(*) INTO users_after FROM app_users;
  SELECT COUNT(*) INTO roles_after FROM app_roles;
  SELECT COUNT(*) INTO documents_after FROM projectpulse_native_admin_documents;
  IF users_after <> baseline.users_before OR roles_after <> baseline.roles_before THEN
    RAISE EXCEPTION 'Microsoft Integration migrations changed operational user or role counts.';
  END IF;
  IF documents_after < baseline.documents_before OR documents_after > baseline.max_documents_after THEN
    RAISE EXCEPTION 'Migration 047 changed the native-document count outside the permitted Module 065 carryover range.';
  END IF;

  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id IN (
      '045_microsoft_integration_consolidation',
      '046_microsoft_sso_connection_profiles',
      '047_microsoft_integration_connection_carryover')) <> 3 THEN
    RAISE EXCEPTION 'Migrations 045, 046, and 047 are not all registered exactly once.';
  END IF;

  IF to_regclass('public.microsoft_integration_client_secrets') IS NULL
     OR to_regclass('public.microsoft_integration_sso_client_secrets') IS NULL
     OR to_regclass('public.microsoft_integration_audit_events') IS NULL
     OR to_regclass('public.microsoft_integration_permission_aliases') IS NULL THEN
    RAISE EXCEPTION 'One or more Microsoft Integration tables are missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_projectpulse045_microsoft_integration_audit_immutable'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Microsoft Integration immutable audit trigger is missing.';
  END IF;

  SELECT COUNT(*) INTO aliases_count
  FROM microsoft_integration_permission_aliases
  WHERE legacy_module_code='067'
    AND active_module_code='065'
    AND active_route_scope='entra-secret-administration';
  IF aliases_count <> 4 THEN
    RAISE EXCEPTION 'Expected four Module 067 permission aliases; found %.', aliases_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM scoped_role_policy_modules
    WHERE module_code='065'
      AND module_name='Microsoft Integration Connection'
      AND route_scope='entra-secret-administration'
      AND is_active=TRUE
  ) THEN
    RAISE EXCEPTION 'Module 065 Microsoft Integration Connection catalog state is incomplete.';
  END IF;
  IF NOT EXISTS (
    SELECT 1 FROM scoped_role_policy_modules
    WHERE module_code='067' AND is_active=FALSE
  ) THEN
    RAISE EXCEPTION 'Module 067 was not retired non-destructively.';
  END IF;

  SELECT COUNT(*) INTO graph_secret_rows_after FROM microsoft_integration_client_secrets;
  SELECT COUNT(*) INTO sso_secret_rows_after FROM microsoft_integration_sso_client_secrets;
  SELECT COUNT(*) INTO audit_rows_after FROM microsoft_integration_audit_events;
  SELECT COUNT(*) INTO carryover_audit_rows_after
  FROM microsoft_integration_audit_events
  WHERE action_code='LEGACY_CONFIGURATION_CARRIED_OVER';
  IF graph_secret_rows_after <> baseline.graph_secret_rows_before
     OR sso_secret_rows_after <> baseline.sso_secret_rows_before THEN
    RAISE EXCEPTION 'Migration 047 changed existing Graph or SSO secret evidence counts.';
  END IF;
  IF audit_rows_after <> baseline.expected_audit_rows_after
     OR carryover_audit_rows_after <> baseline.expected_carryover_audit_rows_after THEN
    RAISE EXCEPTION 'Migration 047 did not create exactly the expected sanitized carryover audit evidence.';
  END IF;
  IF EXISTS (
    SELECT 1 FROM microsoft_integration_audit_events
    WHERE action_code='LEGACY_CONFIGURATION_CARRIED_OVER'
      AND (
        COALESCE(event_metadata->>'secretValuesRead','true') <> 'false'
        OR COALESCE(event_metadata->>'secretValuesChanged','true') <> 'false'
        OR COALESCE(event_metadata->>'sourceTablesDeleted','true') <> 'false'
      )
  ) THEN
    RAISE EXCEPTION 'Migration 047 carryover evidence is not sanitized.';
  END IF;

  SELECT COUNT(*) INTO module010_rows_after FROM azure_entra_settings;
  SELECT COUNT(*) INTO module067_documents_after
  FROM projectpulse_native_admin_documents
  WHERE module_number='067' AND document_key='configuration';
  SELECT md5(COALESCE((
    SELECT to_jsonb(settings)::text
    FROM azure_entra_settings settings
    ORDER BY settings.updated_at DESC NULLS LAST, settings.created_at DESC NULLS LAST
    LIMIT 1
  ), '')) INTO module010_source_hash_after;
  SELECT md5(COALESCE((
    SELECT document_json::text
    FROM projectpulse_native_admin_documents
    WHERE module_number='067' AND document_key='configuration'
    LIMIT 1
  ), '')) INTO module067_source_hash_after;
  IF module010_rows_after <> baseline.module010_rows_before
     OR module067_documents_after <> baseline.module067_documents_before
     OR module010_source_hash_after <> baseline.module010_source_hash_before
     OR module067_source_hash_after <> baseline.module067_source_hash_before THEN
    RAISE EXCEPTION 'Migration 047 changed or removed the Module 010 or Module 067 source configuration.';
  END IF;

  SELECT document_json->'configuration'->>'notes'
  INTO notes
  FROM projectpulse_native_admin_documents
  WHERE module_number='065' AND document_key='configuration';
  IF notes IS NULL OR notes NOT LIKE 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:%' THEN
    RAISE EXCEPTION 'Module 065 consolidated configuration marker is missing.';
  END IF;
  consolidated := substring(notes from length('PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:') + 1)::jsonb;
  SELECT tenant INTO active_tenant
  FROM jsonb_array_elements(COALESCE(consolidated->'tenants','[]'::jsonb)) tenant
  WHERE tenant->>'key' = consolidated->>'activeTenantKey'
  LIMIT 1;
  IF active_tenant IS NULL THEN
    RAISE EXCEPTION 'Module 065 active tenant profile is missing.';
  END IF;

  SELECT to_jsonb(settings) INTO source_settings
  FROM azure_entra_settings settings
  ORDER BY settings.updated_at DESC NULLS LAST, settings.created_at DESC NULLS LAST
  LIMIT 1;
  IF source_settings IS NOT NULL THEN
    IF COALESCE(source_settings->>'tenant_id','') <> ''
       AND active_tenant->>'tenantId' <> source_settings->>'tenant_id' THEN
      RAISE EXCEPTION 'Module 010 tenant ID was not carried into Module 065.';
    END IF;
    IF COALESCE(source_settings->>'client_id','') <> ''
       AND active_tenant->'services'->>'clientId' <> source_settings->>'client_id' THEN
      RAISE EXCEPTION 'Module 010 services client ID was not carried into Module 065.';
    END IF;
    IF COALESCE(source_settings->>'redirect_uri','') <> ''
       AND active_tenant->'sso'->>'redirectUri' <> source_settings->>'redirect_uri' THEN
      RAISE EXCEPTION 'Module 010 redirect URI was not carried into Module 065.';
    END IF;
  END IF;

  SELECT document_json->'configuration' INTO legacy_mail
  FROM projectpulse_native_admin_documents
  WHERE module_number='067' AND document_key='configuration'
  LIMIT 1;
  IF legacy_mail IS NOT NULL THEN
    IF COALESCE(legacy_mail->>'senderAddress','') <> ''
       AND consolidated->'mail'->>'senderAddress' <> legacy_mail->>'senderAddress' THEN
      RAISE EXCEPTION 'Module 067 sender address was not carried into Module 065.';
    END IF;
    IF COALESCE(legacy_mail->>'smtpHost','') <> ''
       AND consolidated->'mail'->>'smtpHost' <> legacy_mail->>'smtpHost' THEN
      RAISE EXCEPTION 'Module 067 SMTP host was not carried into Module 065.';
    END IF;
  END IF;

  IF consolidated->'connectionOwnership'->>'module010DirectoryImport' <> 'services'
     OR consolidated->'connectionOwnership'->>'module057CalendarPresence' <> 'services'
     OR consolidated->'connectionOwnership'->>'module062IdentityProfile' <> 'services'
     OR consolidated->'connectionOwnership'->>'globalMailTransport' <> 'services'
     OR consolidated->'connectionOwnership'->>'interactiveSso' <> 'sso' THEN
    RAISE EXCEPTION 'Module 065 Microsoft connection ownership metadata is incomplete.';
  END IF;
END
$verify_microsoft_connection_carryover$;
SELECT 'MICROSOFT_INTEGRATION_CONNECTION_DATABASE=APPLIED_OR_VERIFIED';
SQL

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$VERIFY_SQL"
