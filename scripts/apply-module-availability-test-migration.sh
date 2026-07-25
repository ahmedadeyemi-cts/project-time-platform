#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="b64f495c743c30176977d05435f838259ead2d9e"
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
MIGRATION="$MIGRATION_ROOT/042_module_availability_controls.sql"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"

[[ -f "$MIGRATION" ]] || fail "Migration 042 entry file is missing."
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(grep -Ec '^[0-9a-f]{64}  042_module_availability_controls\.sql$' "$CHECKSUM_MANIFEST")" == "1" ]] ||
  fail "Migration checksum manifest must contain exactly migration 042."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum manifest validation failed."
echo "MODULE_AVAILABILITY_MIGRATION_CHECKSUM=VERIFIED"

PREREQUISITE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
  SELECT COUNT(*)
  FROM schema_migrations
  WHERE migration_id='041_module_001_timesheet_timer_and_task_association';")"
[[ "$PREREQUISITE" == "1" ]] || fail "Migration 041 prerequisite is missing."

STATE_EXISTS_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT CASE WHEN to_regclass('public.projectpulse_module_availability') IS NULL THEN 0 ELSE 1 END;")"
AUDIT_EXISTS_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT CASE WHEN to_regclass('public.projectpulse_module_availability_audit') IS NULL THEN 0 ELSE 1 END;")"
STATE_BEFORE=0
AUDIT_BEFORE=0
DISABLED_BEFORE=0
if [[ "$STATE_EXISTS_BEFORE" == "1" ]]; then
  STATE_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM projectpulse_module_availability;")"
  DISABLED_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM projectpulse_module_availability WHERE is_enabled=FALSE;")"
fi
if [[ "$AUDIT_EXISTS_BEFORE" == "1" ]]; then
  AUDIT_BEFORE="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT COUNT(*) FROM projectpulse_module_availability_audit;")"
fi

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION"

read -r STATE_AFTER AUDIT_AFTER DISABLED_AFTER <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM projectpulse_module_availability),
      (SELECT COUNT(*) FROM projectpulse_module_availability_audit),
      (SELECT COUNT(*) FROM projectpulse_module_availability WHERE is_enabled=FALSE);" | tr '|' ' '
)"

[[ "$STATE_AFTER" == "$STATE_BEFORE" ]] || fail "Migration 042 changed module availability rows."
[[ "$AUDIT_AFTER" == "$AUDIT_BEFORE" ]] || fail "Migration 042 changed module availability audit rows."
[[ "$DISABLED_AFTER" == "$DISABLED_BEFORE" ]] || fail "Migration 042 changed the number of disabled modules."

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --command="
DO \$verify_module_availability_042\$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='042_module_availability_controls'
  ) THEN
    RAISE EXCEPTION 'Migration 042 was not registered.';
  END IF;

  IF to_regclass('public.projectpulse_module_availability') IS NULL
     OR to_regclass('public.projectpulse_module_availability_audit') IS NULL THEN
    RAISE EXCEPTION 'One or more module availability tables are missing.';
  END IF;

  IF NOT has_table_privilege(current_user, 'projectpulse_module_availability', 'SELECT,INSERT,UPDATE')
     OR NOT has_table_privilege(current_user, 'projectpulse_module_availability_audit', 'SELECT,INSERT') THEN
    RAISE EXCEPTION 'The test API database principal cannot use module availability storage.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='ptp_app')
     AND (
       NOT has_table_privilege('ptp_app', 'projectpulse_module_availability', 'SELECT,INSERT,UPDATE')
       OR NOT has_table_privilege('ptp_app', 'projectpulse_module_availability_audit', 'SELECT,INSERT')
     ) THEN
    RAISE EXCEPTION 'Module availability grants for ptp_app are incomplete.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='projectpulse_app')
     AND (
       NOT has_table_privilege('projectpulse_app', 'projectpulse_module_availability', 'SELECT,INSERT,UPDATE')
       OR NOT has_table_privilege('projectpulse_app', 'projectpulse_module_availability_audit', 'SELECT,INSERT')
     ) THEN
    RAISE EXCEPTION 'Module availability grants for projectpulse_app are incomplete.';
  END IF;
END
\$verify_module_availability_042\$;
SELECT 'MODULE_AVAILABILITY_MIGRATION_042=APPLIED_AND_VERIFIED';"
