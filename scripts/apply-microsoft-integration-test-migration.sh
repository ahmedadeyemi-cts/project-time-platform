#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="13a7b2bddd76026421f833841daf79340c973e18"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"

fail() { echo "ERROR: $*" >&2; exit 1; }
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
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
MIGRATION="045_microsoft_integration_consolidation.sql"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"
[[ -f "$MIGRATION_ROOT/$MIGRATION" ]] || fail "Required migration is missing: $MIGRATION"
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "1" ]] || fail "Migration checksum manifest must contain exactly one SQL file."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "MICROSOFT_INTEGRATION_MIGRATION_CHECKSUM=VERIFIED"

read -r USERS_BEFORE ROLES_BEFORE DOCUMENTS_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM app_roles),
      (SELECT COUNT(*) FROM projectpulse_native_admin_documents);" | tr '|' ' '
)"
[[ -n "${USERS_BEFORE:-}" ]] || fail "Required ProjectPulse operational tables are unavailable."

SECRET_ROWS_BEFORE=0
AUDIT_ROWS_BEFORE=0
if [[ "$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT to_regclass('public.microsoft_integration_client_secrets') IS NOT NULL;")" == "t" ]]; then
  SECRET_ROWS_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM microsoft_integration_client_secrets;")"
fi
if [[ "$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT to_regclass('public.microsoft_integration_audit_events') IS NOT NULL;")" == "t" ]]; then
  AUDIT_ROWS_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM microsoft_integration_audit_events;")"
fi
[[ "$SECRET_ROWS_BEFORE" =~ ^[0-9]+$ && "$AUDIT_ROWS_BEFORE" =~ ^[0-9]+$ ]] || fail "Existing Microsoft Integration evidence counts are invalid."
echo "MICROSOFT_INTEGRATION_EVIDENCE_BASELINE secrets=$SECRET_ROWS_BEFORE audit=$AUDIT_ROWS_BEFORE"

REGISTERED="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='045_microsoft_integration_consolidation');")"
if [[ "$REGISTERED" == "t" ]]; then
  echo "VERIFY_ALREADY_REGISTERED=045_microsoft_integration_consolidation"
else
  echo "APPLY=$MIGRATION"
  psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION_ROOT/$MIGRATION"
fi

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --command="
DO \$verify_microsoft_integration\$
DECLARE
  users_after bigint;
  roles_after bigint;
  documents_after bigint;
  secret_rows_after bigint;
  audit_rows_after bigint;
  aliases_count bigint;
BEGIN
  SELECT COUNT(*) INTO users_after FROM app_users;
  SELECT COUNT(*) INTO roles_after FROM app_roles;
  SELECT COUNT(*) INTO documents_after FROM projectpulse_native_admin_documents;

  IF users_after <> ${USERS_BEFORE}
     OR roles_after <> ${ROLES_BEFORE}
     OR documents_after <> ${DOCUMENTS_BEFORE} THEN
    RAISE EXCEPTION 'Migration 045 changed operational user, role, or native-document counts.';
  END IF;

  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='045_microsoft_integration_consolidation') <> 1 THEN
    RAISE EXCEPTION 'Migration 045 is not registered exactly once.';
  END IF;

  IF to_regclass('public.microsoft_integration_client_secrets') IS NULL
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
      AND module_name='Microsoft Integration'
      AND route_scope='entra-secret-administration'
      AND is_active=TRUE
  ) THEN
    RAISE EXCEPTION 'Module 065 Microsoft Integration catalog state is incomplete.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM scoped_role_policy_modules
    WHERE module_code='067'
      AND is_active=FALSE
  ) THEN
    RAISE EXCEPTION 'Module 067 was not retired non-destructively.';
  END IF;

  SELECT COUNT(*) INTO secret_rows_after FROM microsoft_integration_client_secrets;
  SELECT COUNT(*) INTO audit_rows_after FROM microsoft_integration_audit_events;
  IF secret_rows_after <> ${SECRET_ROWS_BEFORE}
     OR audit_rows_after <> ${AUDIT_ROWS_BEFORE} THEN
    RAISE EXCEPTION 'Migration 045 changed existing Microsoft Integration secret or audit evidence counts.';
  END IF;
END
\$verify_microsoft_integration\$;
SELECT 'MICROSOFT_INTEGRATION_DATABASE=APPLIED_OR_VERIFIED';"
