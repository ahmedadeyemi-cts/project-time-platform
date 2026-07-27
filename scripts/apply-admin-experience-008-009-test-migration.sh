#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="55ff9c3a07535ae7c7e2469cf69cdb075c51d1b3"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"

fail() { echo "ERROR: $*" >&2; exit 1; }
mask_value() { local value="$1"; value="${value//%/%25}"; printf '::add-mask::%s\n' "$value"; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."
mask_value "$DATABASE_URL"

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
MIGRATION="048_admin_audit_and_manager_team_scope.sql"
MIGRATION_ID="048_admin_audit_and_manager_team_scope"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"
[[ -f "$MIGRATION_ROOT/$MIGRATION" ]] || fail "Required migration is missing: $MIGRATION"
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "1" ]] || fail "Migration checksum manifest must contain exactly one SQL file."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "ADMIN_EXPERIENCE_MIGRATION_CHECKSUM=VERIFIED"

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

hash_or_empty() {
  local sql="$1"
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT md5(COALESCE(($sql)::text, ''));"
}

USERS_BEFORE="$(count_if_table app_users)"
ROLES_BEFORE="$(count_if_table app_roles)"
USERS_HASH_BEFORE="$(hash_or_empty "SELECT jsonb_agg(to_jsonb(snapshot)) FROM (SELECT user_id, email, display_name, team_name, manager_email, is_active FROM app_users ORDER BY user_id) snapshot")"
MODULE010_ROWS_BEFORE="$(count_if_table azure_entra_settings)"
MODULE010_HASH_BEFORE="$(hash_or_empty "SELECT jsonb_agg(to_jsonb(settings)) FROM (SELECT * FROM azure_entra_settings ORDER BY updated_at DESC NULLS LAST, created_at DESC NULLS LAST) settings")"
MODULE065_HASH_BEFORE="$(hash_or_empty "SELECT document_json FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration' LIMIT 1")"
MODULE067_HASH_BEFORE="$(hash_or_empty "SELECT document_json FROM projectpulse_native_admin_documents WHERE module_number='067' AND document_key='configuration' LIMIT 1")"
GRAPH_SECRET_ROWS_BEFORE="$(count_if_table microsoft_integration_client_secrets)"
SSO_SECRET_ROWS_BEFORE="$(count_if_table microsoft_integration_sso_client_secrets)"
MICROSOFT_AUDIT_ROWS_BEFORE="$(count_if_table microsoft_integration_audit_events)"
ADMIN_AUDIT_ROWS_BEFORE="$(count_if_table projectpulse_system_audit_events)"
MANAGER_ASSIGNMENT_ROWS_BEFORE="$(count_if_table user_admin_manager_team_assignments)"

for value in \
  "$USERS_BEFORE" "$ROLES_BEFORE" "$MODULE010_ROWS_BEFORE" \
  "$GRAPH_SECRET_ROWS_BEFORE" "$SSO_SECRET_ROWS_BEFORE" \
  "$MICROSOFT_AUDIT_ROWS_BEFORE" "$ADMIN_AUDIT_ROWS_BEFORE" \
  "$MANAGER_ASSIGNMENT_ROWS_BEFORE"; do
  [[ "$value" =~ ^[0-9]+$ ]] || fail "A database evidence count is invalid."
done
for value in "$USERS_HASH_BEFORE" "$MODULE010_HASH_BEFORE" "$MODULE065_HASH_BEFORE" "$MODULE067_HASH_BEFORE"; do
  [[ "$value" =~ ^[0-9a-f]{32}$ ]] || fail "A database evidence hash is invalid."
done

echo "ADMIN_EXPERIENCE_EVIDENCE_BASELINE users=$USERS_BEFORE roles=$ROLES_BEFORE module010Rows=$MODULE010_ROWS_BEFORE graphSecrets=$GRAPH_SECRET_ROWS_BEFORE ssoSecrets=$SSO_SECRET_ROWS_BEFORE microsoftAudit=$MICROSOFT_AUDIT_ROWS_BEFORE adminAudit=$ADMIN_AUDIT_ROWS_BEFORE managerAssignments=$MANAGER_ASSIGNMENT_ROWS_BEFORE"

REGISTERED="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='$MIGRATION_ID');")"
case "$REGISTERED" in
  t)
    echo "ADMIN_EXPERIENCE_MIGRATION=ALREADY_REGISTERED"
    ;;
  f)
    echo "ADMIN_EXPERIENCE_MIGRATION=APPLYING"
    psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION_ROOT/$MIGRATION"
    ;;
  *)
    fail "Migration registration state is invalid."
    ;;
esac

REGISTRATION_COUNT="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM schema_migrations WHERE migration_id='$MIGRATION_ID';")"
[[ "$REGISTRATION_COUNT" == "1" ]] || fail "Migration 048 is not registered exactly once."

table_exists projectpulse_system_audit_events || fail "Immutable administrative audit table is missing."
table_exists user_admin_manager_team_assignments || fail "Manager-to-team assignment table is missing."

IMMUTABLE_TRIGGER="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname='trg_projectpulse048_system_audit_immutable' AND NOT tgisinternal);")"
UNIQUE_MANAGER_INDEX="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT to_regclass('public.ux_user_admin_one_active_manager_per_team') IS NOT NULL;")"
AUDIT_TIME_INDEX="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT to_regclass('public.ix_projectpulse_system_audit_event_time') IS NOT NULL;")"
[[ "$IMMUTABLE_TRIGGER" == "t" ]] || fail "Immutable audit trigger is missing."
[[ "$UNIQUE_MANAGER_INDEX" == "t" ]] || fail "One-active-manager-per-team index is missing."
[[ "$AUDIT_TIME_INDEX" == "t" ]] || fail "Administrative audit timeline index is missing."

USERS_AFTER="$(count_if_table app_users)"
ROLES_AFTER="$(count_if_table app_roles)"
USERS_HASH_AFTER="$(hash_or_empty "SELECT jsonb_agg(to_jsonb(snapshot)) FROM (SELECT user_id, email, display_name, team_name, manager_email, is_active FROM app_users ORDER BY user_id) snapshot")"
MODULE010_ROWS_AFTER="$(count_if_table azure_entra_settings)"
MODULE010_HASH_AFTER="$(hash_or_empty "SELECT jsonb_agg(to_jsonb(settings)) FROM (SELECT * FROM azure_entra_settings ORDER BY updated_at DESC NULLS LAST, created_at DESC NULLS LAST) settings")"
MODULE065_HASH_AFTER="$(hash_or_empty "SELECT document_json FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration' LIMIT 1")"
MODULE067_HASH_AFTER="$(hash_or_empty "SELECT document_json FROM projectpulse_native_admin_documents WHERE module_number='067' AND document_key='configuration' LIMIT 1")"
GRAPH_SECRET_ROWS_AFTER="$(count_if_table microsoft_integration_client_secrets)"
SSO_SECRET_ROWS_AFTER="$(count_if_table microsoft_integration_sso_client_secrets)"
MICROSOFT_AUDIT_ROWS_AFTER="$(count_if_table microsoft_integration_audit_events)"
ADMIN_AUDIT_ROWS_AFTER="$(count_if_table projectpulse_system_audit_events)"
MANAGER_ASSIGNMENT_ROWS_AFTER="$(count_if_table user_admin_manager_team_assignments)"

[[ "$USERS_AFTER" == "$USERS_BEFORE" && "$ROLES_AFTER" == "$ROLES_BEFORE" ]] || fail "Migration 048 changed operational user or role counts."
[[ "$USERS_HASH_AFTER" == "$USERS_HASH_BEFORE" ]] || fail "Migration 048 changed existing user profile, team, or manager data."
[[ "$MODULE010_ROWS_AFTER" == "$MODULE010_ROWS_BEFORE" && "$MODULE010_HASH_AFTER" == "$MODULE010_HASH_BEFORE" ]] || fail "Migration 048 changed Module 010 configuration."
[[ "$MODULE065_HASH_AFTER" == "$MODULE065_HASH_BEFORE" && "$MODULE067_HASH_AFTER" == "$MODULE067_HASH_BEFORE" ]] || fail "Migration 048 changed Module 065 or legacy Module 067 configuration."
[[ "$GRAPH_SECRET_ROWS_AFTER" == "$GRAPH_SECRET_ROWS_BEFORE" && "$SSO_SECRET_ROWS_AFTER" == "$SSO_SECRET_ROWS_BEFORE" ]] || fail "Migration 048 changed Microsoft Integration secret evidence."
[[ "$MICROSOFT_AUDIT_ROWS_AFTER" == "$MICROSOFT_AUDIT_ROWS_BEFORE" ]] || fail "Migration 048 changed Microsoft Integration audit evidence."
[[ "$ADMIN_AUDIT_ROWS_AFTER" == "$ADMIN_AUDIT_ROWS_BEFORE" ]] || fail "Migration 048 fabricated administrative audit events."
[[ "$MANAGER_ASSIGNMENT_ROWS_AFTER" == "$MANAGER_ASSIGNMENT_ROWS_BEFORE" ]] || fail "Migration 048 fabricated manager-to-team assignments."

for role in ptp_app projectpulse_app; do
  ROLE_EXISTS="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='$role');")"
  if [[ "$ROLE_EXISTS" == "t" ]]; then
    AUDIT_PRIVILEGE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT has_table_privilege('$role', 'projectpulse_system_audit_events', 'SELECT,INSERT');")"
    ASSIGNMENT_PRIVILEGE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT has_table_privilege('$role', 'user_admin_manager_team_assignments', 'SELECT,INSERT,UPDATE');")"
    [[ "$AUDIT_PRIVILEGE" == "t" && "$ASSIGNMENT_PRIVILEGE" == "t" ]] || fail "Runtime table grants are incomplete for $role."
  fi
done

echo "ADMIN_EXPERIENCE_MIGRATION_REGISTRATION=VERIFIED"
echo "ADMIN_EXPERIENCE_IMMUTABLE_AUDIT=VERIFIED"
echo "ADMIN_EXPERIENCE_MANAGER_TEAM_SCOPE=VERIFIED"
echo "ADMIN_EXPERIENCE_EXISTING_DATA_PRESERVATION=VERIFIED"
echo "ADMIN_EXPERIENCE_MIGRATION_048=APPLIED_OR_VERIFIED"
