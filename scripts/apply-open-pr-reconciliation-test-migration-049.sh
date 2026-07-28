#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="b24ea3db7d0c839d03804975e87b7929dba8c7f6"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
MIGRATION_FILE="049_module_021_sell_customer_sync.sql"
MIGRATION_ID="049_module_021_sell_customer_sync"

fail() { echo "ERROR: $*" >&2; exit 1; }
scalar() {
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="$1"
}
relation_exists() {
  [[ "$(scalar "SELECT to_regclass('public.$1') IS NOT NULL;")" == "t" ]]
}
relation_count() {
  scalar "SELECT COUNT(*) FROM $1;"
}

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] ||
  fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"
[[ -f "$MIGRATION_ROOT/$MIGRATION_FILE" ]] || fail "Migration 049 source is missing."
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "1" ]] ||
  fail "Migration checksum manifest must contain exactly one SQL file."
[[ "$(awk '{print $2}' "$CHECKSUM_MANIFEST")" == "$MIGRATION_FILE" ]] ||
  fail "Migration checksum manifest contains an unexpected file."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration 049 checksum validation failed."
echo "OPEN_PR_RECONCILIATION_MIGRATION_049_CHECKSUM=VERIFIED"

for required_table in schema_migrations app_users clients client_contacts projects crm_integration_providers; do
  relation_exists "$required_table" || fail "Required ProjectPulse table is unavailable: $required_table"
done

read -r USERS_BEFORE CLIENTS_BEFORE CONTACTS_BEFORE PROJECTS_BEFORE PROVIDERS_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At -F ' ' --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM clients),
      (SELECT COUNT(*) FROM client_contacts),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM crm_integration_providers);"
)"

LINK_TABLE_EXISTED=false
RUN_TABLE_EXISTED=false
LINKS_BEFORE=0
RUNS_BEFORE=0
if relation_exists customer_directory_source_links; then
  LINK_TABLE_EXISTED=true
  LINKS_BEFORE="$(relation_count customer_directory_source_links)"
fi
if relation_exists customer_directory_sync_runs; then
  RUN_TABLE_EXISTED=true
  RUNS_BEFORE="$(relation_count customer_directory_sync_runs)"
fi

REGISTERED_BEFORE="$(scalar "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='$MIGRATION_ID');")"
if [[ "$REGISTERED_BEFORE" == "t" ]]; then
  echo "OPEN_PR_RECONCILIATION_MIGRATION_049=ALREADY_REGISTERED"
else
  echo "OPEN_PR_RECONCILIATION_MIGRATION_049=APPLYING"
  psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION_ROOT/$MIGRATION_FILE"
fi

[[ "$(scalar "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='$MIGRATION_ID';")" == "1" ]] ||
  fail "Migration 049 registration is missing or duplicated."
relation_exists customer_directory_source_links || fail "customer_directory_source_links was not created."
relation_exists customer_directory_sync_runs || fail "customer_directory_sync_runs was not created."

for required_index in \
  ux_customer_directory_source_links_client \
  ix_customer_directory_source_links_sync \
  ix_customer_directory_sync_runs_provider; do
  relation_exists "$required_index" || fail "Required migration 049 index is missing: $required_index"
done

[[ "$(scalar "SELECT COUNT(*) FROM pg_constraint WHERE conname IN ('ck_customer_directory_sync_runs_status','ck_customer_directory_sync_runs_counts');")" == "2" ]] ||
  fail "Migration 049 check constraints are incomplete."
[[ "$(scalar "SELECT COUNT(*) FROM crm_integration_providers WHERE provider_key='zendesk_sell';")" == "1" ]] ||
  fail "The authoritative Module 026 SELL provider is unavailable."
[[ "$(scalar "SELECT has_table_privilege('ptp_app','customer_directory_source_links','SELECT,INSERT,UPDATE');")" == "t" ]] ||
  fail "ptp_app lacks the required source-link privileges."
[[ "$(scalar "SELECT has_table_privilege('ptp_app','customer_directory_sync_runs','SELECT,INSERT,UPDATE');")" == "t" ]] ||
  fail "ptp_app lacks the required sync-run privileges."

read -r USERS_AFTER CLIENTS_AFTER CONTACTS_AFTER PROJECTS_AFTER PROVIDERS_AFTER <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At -F ' ' --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM clients),
      (SELECT COUNT(*) FROM client_contacts),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM crm_integration_providers);"
)"
[[ "$USERS_AFTER" == "$USERS_BEFORE" ]] || fail "Migration 049 changed app_users row count."
[[ "$CLIENTS_AFTER" == "$CLIENTS_BEFORE" ]] || fail "Migration 049 changed clients row count."
[[ "$CONTACTS_AFTER" == "$CONTACTS_BEFORE" ]] || fail "Migration 049 changed client_contacts row count."
[[ "$PROJECTS_AFTER" == "$PROJECTS_BEFORE" ]] || fail "Migration 049 changed projects row count."
[[ "$PROVIDERS_AFTER" == "$PROVIDERS_BEFORE" ]] || fail "Migration 049 changed CRM provider row count."

LINKS_AFTER="$(relation_count customer_directory_source_links)"
RUNS_AFTER="$(relation_count customer_directory_sync_runs)"
if [[ "$LINK_TABLE_EXISTED" == true ]]; then
  [[ "$LINKS_AFTER" == "$LINKS_BEFORE" ]] || fail "Migration verification changed source-link evidence."
else
  [[ "$LINKS_AFTER" == "0" ]] || fail "New source-link table was not empty after migration."
fi
if [[ "$RUN_TABLE_EXISTED" == true ]]; then
  [[ "$RUNS_AFTER" == "$RUNS_BEFORE" ]] || fail "Migration verification changed synchronization evidence."
else
  [[ "$RUNS_AFTER" == "0" ]] || fail "New synchronization-run table was not empty after migration."
fi

[[ "$(scalar "SELECT NOT EXISTS (SELECT 1 FROM customer_directory_sync_runs WHERE evidence_json ?| ARRAY['clientSecret','apiKey','accessToken','refreshToken']);")" == "t" ]] ||
  fail "Synchronization evidence contains a forbidden credential-like field."

echo "OPEN_PR_RECONCILIATION_OPERATIONAL_COUNTS=PRESERVED"
echo "MODULE_021_026_SELL_SYNC_MIGRATION_049=APPLIED_OR_VERIFIED"
