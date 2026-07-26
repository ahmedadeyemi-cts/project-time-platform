#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="3b68790b7fa320d96b96158ab7414bad335bc767"
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
  "044_project_expense_upload_certify_connection.sql"
  "044a_project_expense_self_certify_permission.sql"
)
MIGRATION_IDS=(
  "044_project_expense_upload_certify_connection"
  "044a_project_expense_self_certify_permission"
)

for migration in "${MIGRATIONS[@]}"; do
  [[ -f "$MIGRATION_ROOT/$migration" ]] || fail "Required migration is missing: $migration"
done
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "2" ]] ||
  fail "Migration checksum manifest must contain exactly 2 SQL files."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "MODULE_005_EXPENSE_MIGRATION_CHECKSUMS=VERIFIED"

read -r USERS_BEFORE PROJECTS_BEFORE ASSIGNMENTS_BEFORE TIMESHEETS_BEFORE ENTRIES_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM timesheets),
      (SELECT COUNT(*) FROM time_entries);" | tr '|' ' '
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
DO \$verify_module_005_expense\$
DECLARE
  users_after bigint;
  projects_after bigint;
  assignments_after bigint;
  timesheets_after bigint;
  entries_after bigint;
BEGIN
  SELECT COUNT(*) INTO users_after FROM app_users;
  SELECT COUNT(*) INTO projects_after FROM projects;
  SELECT COUNT(*) INTO assignments_after FROM project_assignments;
  SELECT COUNT(*) INTO timesheets_after FROM timesheets;
  SELECT COUNT(*) INTO entries_after FROM time_entries;

  IF users_after <> ${USERS_BEFORE}
     OR projects_after <> ${PROJECTS_BEFORE}
     OR assignments_after <> ${ASSIGNMENTS_BEFORE}
     OR timesheets_after <> ${TIMESHEETS_BEFORE}
     OR entries_after <> ${ENTRIES_BEFORE} THEN
    RAISE EXCEPTION 'Module 005 migrations changed operational user, project, assignment, timesheet, or time-entry counts.';
  END IF;

  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id IN (
      '044_project_expense_upload_certify_connection',
      '044a_project_expense_self_certify_permission')) <> 2 THEN
    RAISE EXCEPTION 'Required Module 005 migrations are not both registered.';
  END IF;

  IF to_regclass('public.project_expense_uploads') IS NULL
     OR to_regclass('public.project_expense_lines') IS NULL
     OR to_regclass('public.project_expense_events') IS NULL
     OR to_regclass('public.project_expense_mail_outbox') IS NULL
     OR to_regclass('public.certify_connection_profiles') IS NULL
     OR to_regclass('public.certify_expense_import_runs') IS NULL THEN
    RAISE EXCEPTION 'One or more Module 005/038 tables are missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM certify_connection_profiles
    WHERE profile_name='default'
      AND base_url='https://api.certify.com/v1/'
      AND automatic_sync_enabled=FALSE
  ) THEN
    RAISE EXCEPTION 'The governed default Certify connection profile is missing or unsafe.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM app_permissions WHERE permission_code='VIEW_PROJECT_EXPENSE_UPLOAD'
  ) OR NOT EXISTS (
    SELECT 1 FROM app_permissions WHERE permission_code='UPLOAD_PROJECT_EXPENSE_ON_BEHALF'
  ) OR NOT EXISTS (
    SELECT 1 FROM app_permissions WHERE permission_code='MANAGE_CERTIFY_CONNECTION'
  ) THEN
    RAISE EXCEPTION 'Module 005/038 permissions are incomplete.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM app_roles r
    JOIN app_role_permissions rp USING(app_role_id)
    JOIN app_permissions p USING(app_permission_id)
    WHERE upper(r.role_code) IN ('ENGINEER','ENGINEERING')
      AND p.permission_code='IMPORT_PROJECT_EXPENSE_CERTIFY'
  ) THEN
    RAISE EXCEPTION 'Engineer Certify self-import permission is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM app_roles r
    JOIN app_role_permissions rp USING(app_role_id)
    JOIN app_permissions p USING(app_permission_id)
    WHERE upper(r.role_code) IN ('PROJECT_MANAGER','PROJECT_MANAGEMENT')
      AND p.permission_code='UPLOAD_PROJECT_EXPENSE_ON_BEHALF'
  ) THEN
    RAISE EXCEPTION 'Project Management on-behalf upload permission is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM app_roles r
    JOIN app_role_permissions rp USING(app_role_id)
    JOIN app_permissions p USING(app_permission_id)
    WHERE upper(r.role_code)='ACCOUNTING'
      AND p.permission_code='MANAGE_CERTIFY_CONNECTION'
  ) THEN
    RAISE EXCEPTION 'Accounting Certify connection permission is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM scoped_role_policy_modules
    WHERE module_code='005' AND module_name='Project Expense Upload'
  ) OR NOT EXISTS (
    SELECT 1 FROM scoped_role_policy_modules
    WHERE module_code='038' AND module_name='Certify Connection & Sync Center'
  ) THEN
    RAISE EXCEPTION 'Module 005 or 038 catalog identity was not updated.';
  END IF;
END
\$verify_module_005_expense\$;
SELECT 'MODULE_005_PROJECT_EXPENSE_MIGRATIONS=APPLIED_OR_VERIFIED';"
