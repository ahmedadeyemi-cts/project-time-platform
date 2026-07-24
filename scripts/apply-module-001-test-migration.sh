#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="8c4afed94bfb949ad158f029ebd498f6d930fcce"
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
MIGRATION="$MIGRATION_ROOT/041_module_001_timesheet_timer_and_task_association.sql"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"

[[ -f "$MIGRATION" ]] || fail "Migration 041 entry file is missing."
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(grep -Ec '^[0-9a-f]{64}  041_module_001_timesheet_timer_and_task_association\.sql$' "$CHECKSUM_MANIFEST")" == "1" ]] ||
  fail "Migration checksum manifest must contain exactly migration 041."

(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum manifest validation failed."

echo "MODULE001_MIGRATION_CHECKSUM=VERIFIED"

read -r TIMESHEETS_BEFORE ENTRIES_BEFORE ASSIGNMENTS_BEFORE TASKS_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM timesheets),
      (SELECT COUNT(*) FROM time_entries),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM project_tasks)
    WHERE EXISTS (
      SELECT 1 FROM schema_migrations
      WHERE migration_id='040_scoped_role_policy_versions'
    );" | tr '|' ' '
)"
[[ -n "${TIMESHEETS_BEFORE:-}" ]] || fail "Migration 040 prerequisite is missing."

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION"

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --command="
DO \$verify_module001_041\$
DECLARE
  timesheets_after bigint;
  entries_after bigint;
  assignments_after bigint;
  tasks_after bigint;
BEGIN
  SELECT COUNT(*) INTO timesheets_after FROM timesheets;
  SELECT COUNT(*) INTO entries_after FROM time_entries;
  SELECT COUNT(*) INTO assignments_after FROM project_assignments;
  SELECT COUNT(*) INTO tasks_after FROM project_tasks;

  IF timesheets_after <> ${TIMESHEETS_BEFORE}::bigint
     OR entries_after <> ${ENTRIES_BEFORE}::bigint
     OR assignments_after <> ${ASSIGNMENTS_BEFORE}::bigint
     OR tasks_after <> ${TASKS_BEFORE}::bigint THEN
    RAISE EXCEPTION 'Existing Timesheet, Time Entry, assignment, or task row counts changed during migration 041.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='041_module_001_timesheet_timer_and_task_association'
  ) THEN
    RAISE EXCEPTION 'Migration 041 was not registered.';
  END IF;

  IF to_regclass('public.module001_weekly_task_lines') IS NULL
     OR to_regclass('public.module001_timer_sessions') IS NULL
     OR to_regclass('public.module001_timer_daily_segments') IS NULL
     OR to_regclass('public.module001_timesheet_entry_associations') IS NULL
     OR to_regclass('public.module001_timer_audit_events') IS NULL THEN
    RAISE EXCEPTION 'One or more Module 001 tables are missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='timesheets' AND column_name='submitted_by_user_id'
  ) OR NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='timesheets' AND column_name='submission_reason'
  ) THEN
    RAISE EXCEPTION 'Timesheet submission attribution columns are missing.';
  END IF;

  IF to_regclass('public.ux_module001_one_running_timer_per_user') IS NULL THEN
    RAISE EXCEPTION 'One-running-timer unique index is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_module001_041_timer_audit_immutable' AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Immutable timer audit trigger is missing.';
  END IF;

  IF NOT has_table_privilege(current_user, 'module001_timer_sessions', 'SELECT,INSERT,UPDATE')
     OR NOT has_table_privilege(current_user, 'module001_timer_audit_events', 'SELECT,INSERT') THEN
    RAISE EXCEPTION 'The test API database principal cannot use the Module 001 tables.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='ptp_app')
     AND (
       NOT has_table_privilege('ptp_app', 'module001_timer_sessions', 'SELECT,INSERT,UPDATE')
       OR NOT has_table_privilege('ptp_app', 'module001_timer_audit_events', 'SELECT,INSERT')
     ) THEN
    RAISE EXCEPTION 'Module 001 grants for ptp_app are incomplete.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='projectpulse_app')
     AND (
       NOT has_table_privilege('projectpulse_app', 'module001_timer_sessions', 'SELECT,INSERT,UPDATE')
       OR NOT has_table_privilege('projectpulse_app', 'module001_timer_audit_events', 'SELECT,INSERT')
     ) THEN
    RAISE EXCEPTION 'Module 001 grants for projectpulse_app are incomplete.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM scoped_role_policy_modules
    WHERE module_code='001' AND module_name='Timesheet'
  ) THEN
    RAISE EXCEPTION 'Module 001 policy catalog name was not preserved as Timesheet.';
  END IF;
END
\$verify_module001_041\$;
SELECT 'MODULE001_MIGRATION_041=APPLIED_AND_VERIFIED';"
