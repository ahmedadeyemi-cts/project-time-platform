#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="d874b1a5e03c77ab48e174020b98b6678c6eabc9"
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
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] ||
  fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"
MIGRATIONS=(
  "040_scoped_role_policy_versions.sql"
  "041_module_001_timesheet_timer_and_task_association.sql"
  "042_module_availability_controls.sql"
  "043_ptc_time_steward_permissions.sql"
)
MIGRATION_IDS=(
  "040_scoped_role_policy_versions"
  "041_module_001_timesheet_timer_and_task_association"
  "042_module_availability_controls"
  "043_ptc_time_steward_permissions"
)

for migration in "${MIGRATIONS[@]}"; do
  [[ -f "$MIGRATION_ROOT/$migration" ]] || fail "Required migration is missing: $migration"
done
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "15" ]] ||
  fail "Migration checksum manifest must contain exactly 15 SQL files."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."

echo "INTUITIVE_PERMISSIONS_PTC_MIGRATION_CHECKSUMS=VERIFIED"

read -r USERS_BEFORE TIMESHEETS_BEFORE ENTRIES_BEFORE TASKS_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM timesheets),
      (SELECT COUNT(*) FROM time_entries),
      (SELECT COUNT(*) FROM project_tasks);" | tr '|' ' '
)"
[[ -n "${USERS_BEFORE:-}" ]] || fail "Required ProjectPulse tables are unavailable."

for index in "${!MIGRATIONS[@]}"; do
  migration="${MIGRATIONS[$index]}"
  migration_id="${MIGRATION_IDS[$index]}"
  registered="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='$migration_id');")"
  if [[ "$registered" == "t" ]]; then
    echo "VERIFY_ALREADY_REGISTERED=$migration_id"
    continue
  fi
  echo "APPLY=$migration"
  psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION_ROOT/$migration"
done

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --command="
DO \$verify_intuitive_permissions_ptc\$
DECLARE
  users_after bigint;
  timesheets_after bigint;
  entries_after bigint;
  tasks_after bigint;
BEGIN
  SELECT COUNT(*) INTO users_after FROM app_users;
  SELECT COUNT(*) INTO timesheets_after FROM timesheets;
  SELECT COUNT(*) INTO entries_after FROM time_entries;
  SELECT COUNT(*) INTO tasks_after FROM project_tasks;

  IF users_after <> ${USERS_BEFORE}
     OR timesheets_after <> ${TIMESHEETS_BEFORE}
     OR entries_after <> ${ENTRIES_BEFORE}
     OR tasks_after <> ${TASKS_BEFORE} THEN
    RAISE EXCEPTION 'Migrations 040-043 changed operational user, timesheet, entry, or task counts.';
  END IF;

  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id IN (
      '040_scoped_role_policy_versions',
      '041_module_001_timesheet_timer_and_task_association',
      '042_module_availability_controls',
      '043_ptc_time_steward_permissions')) <> 4 THEN
    RAISE EXCEPTION 'Required migrations 040-043 are not all registered.';
  END IF;

  IF (SELECT COUNT(*) FROM scoped_role_policy_modules WHERE is_active) <> 70 THEN
    RAISE EXCEPTION 'The active scoped module catalog count is not 70.';
  END IF;

  IF (SELECT COUNT(DISTINCT role_code) FROM scoped_role_policy_effective_grants) <> 12 THEN
    RAISE EXCEPTION 'The effective canonical role count is not 12.';
  END IF;

  IF (SELECT COUNT(*) FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED') <> 1 THEN
    RAISE EXCEPTION 'Exactly one published policy version is required.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM scoped_role_policy_versions
    WHERE policy_status='PUBLISHED'
      AND source_name='043_ptc_time_steward_permissions'
  ) THEN
    RAISE EXCEPTION 'Migration 043 is not the published role-policy version.';
  END IF;

  IF to_regclass('public.scoped_time_management_events') IS NULL THEN
    RAISE EXCEPTION 'The immutable PTC time-management audit table is missing.';
  END IF;

  IF (SELECT COUNT(DISTINCT action_code)
      FROM scoped_role_policy_effective_grants
      WHERE role_code='PROJECT_TEAM_COORDINATOR'
        AND module_code='001'
        AND grant_effect='GRANT'
        AND action_code IN (
          'MODULE_VIEW','TIME_VIEW','TIME_VIEW_ON_BEHALF','TIME_UNSUBMIT',
          'TIME_REOPEN','TIME_CORRECT_ON_BEHALF','TIME_REASSIGN',
          'TIME_DELETE_ON_BEHALF','TIME_TASK_CREATE','TIME_TASK_ASSIGN',
          'AUDIT_VIEW','AUDIT_RECORD')) <> 12 THEN
    RAISE EXCEPTION 'The PTC time-steward grant set is incomplete.';
  END IF;

  IF (SELECT COUNT(DISTINCT action_code)
      FROM scoped_role_policy_effective_grants
      WHERE role_code='PROJECT_TEAM_COORDINATOR'
        AND module_code='001'
        AND grant_effect='DENY'
        AND action_code IN ('TIME_SUBMIT','TIME_DELETE_PERMANENT','USER_IMPERSONATE','SYSTEM_CONFIGURE')) <> 4 THEN
    RAISE EXCEPTION 'The PTC protected denial set is incomplete.';
  END IF;

  IF EXISTS (
    SELECT 1 FROM scoped_role_policy_effective_grants
    WHERE role_code='PROJECT_TEAM_COORDINATOR'
      AND module_code='001'
      AND grant_effect='GRANT'
      AND action_code IN ('TIME_SUBMIT','TIME_DELETE_PERMANENT','USER_IMPERSONATE','SYSTEM_CONFIGURE')
  ) THEN
    RAISE EXCEPTION 'A protected PTC action was granted.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname='chk_module001_association_source'
      AND pg_get_constraintdef(oid) LIKE '%PTC_TIME_STEWARD%'
  ) THEN
    RAISE EXCEPTION 'The PTC time-steward association source is not allowed.';
  END IF;
END
\$verify_intuitive_permissions_ptc\$;
SELECT 'INTUITIVE_PERMISSIONS_PTC_MIGRATIONS=APPLIED_OR_VERIFIED';"
