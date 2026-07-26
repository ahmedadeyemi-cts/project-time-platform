#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="b30fa97b06e700f2256b20690d650aafa1c28886"
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
echo "COMBINED_MODULE_MIGRATION_CHECKSUMS=VERIFIED"

read -r USERS_BEFORE PROJECTS_BEFORE ASSIGNMENTS_BEFORE TIMESHEETS_BEFORE ENTRIES_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM timesheets),
      (SELECT COUNT(*) FROM time_entries);" | tr '|' ' '
)"
[[ -n "${USERS_BEFORE:-}" ]] || fail "Required ProjectPulse operational tables are unavailable."

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
DO \$verify_combined_modules\$
DECLARE
  users_after bigint;
  projects_after bigint;
  assignments_after bigint;
  timesheets_after bigint;
  entries_after bigint;
  canonical_role_count bigint;
  module_count bigint;
  published_policy_count bigint;
  grant_count bigint;
  eligible_user_count bigint;
  operator_count bigint;
  assignment_target_count bigint;
  regular_task_count bigint;
  service_request_count bigint;
  non_project_count bigint;
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
    RAISE EXCEPTION 'Combined module preflight changed operational user, project, assignment, timesheet, or time-entry counts.';
  END IF;

  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id IN (
      '040_scoped_role_policy_versions',
      '043_ptc_time_steward_permissions',
      '044_project_expense_upload_certify_connection',
      '044a_project_expense_self_certify_permission')) <> 4 THEN
    RAISE EXCEPTION 'Required migrations 040, 043, 044, and 044a are not all registered.';
  END IF;

  SELECT COUNT(*) INTO canonical_role_count
  FROM app_roles r
  WHERE r.is_active=TRUE
    AND UPPER(r.role_code) IN (
      'ENGINEERING','PROJECT_MANAGEMENT','ENGINEERING_LEAD','PROJECT_MANAGEMENT_LEAD',
      'MANAGER','SALES','INSIDE_SALES','SOLUTION_ARCHITECT','EXECUTIVE',
      'PROJECT_TEAM_COORDINATOR','ACCOUNTING','SUPER_ADMINISTRATOR');
  IF canonical_role_count <> 12 THEN
    RAISE EXCEPTION 'Expected 12 active canonical roles; found %.', canonical_role_count;
  END IF;

  SELECT COUNT(*) INTO module_count FROM scoped_role_policy_modules WHERE is_active=TRUE;
  IF module_count <> 70 THEN
    RAISE EXCEPTION 'Expected 70 active scoped modules; found %.', module_count;
  END IF;

  SELECT COUNT(*) INTO published_policy_count
  FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED';
  IF published_policy_count <> 1 THEN
    RAISE EXCEPTION 'Expected exactly one published policy; found %.', published_policy_count;
  END IF;

  SELECT COUNT(*) INTO grant_count FROM scoped_role_policy_effective_grants;
  IF grant_count <= 0 THEN
    RAISE EXCEPTION 'The published permission matrix contains no effective grants.';
  END IF;

  SELECT COUNT(DISTINCT u.user_id) INTO eligible_user_count
  FROM app_users u
  JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
  JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
  WHERE u.is_active=TRUE
    AND UPPER(r.role_code) IN (
      'ENGINEERING','ENGINEER','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD',
      'PROJECT_MANAGEMENT','PROJECT_MANAGER','PROJECT_MANAGEMENT_LEAD',
      'PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD');
  IF eligible_user_count <= 0 THEN
    RAISE EXCEPTION 'No active eligible Engineering or Project Management users were found.';
  END IF;

  SELECT COUNT(DISTINCT u.user_id) INTO operator_count
  FROM app_users u
  JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
  JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
  WHERE u.is_active=TRUE
    AND UPPER(r.role_code) IN ('PROJECT_TEAM_COORDINATOR','SUPER_ADMINISTRATOR','ADMINISTRATOR');
  IF operator_count <= 0 THEN
    RAISE EXCEPTION 'No active Project Team Coordinator or Super Administrator operator exists.';
  END IF;

  SELECT COUNT(*) INTO assignment_target_count
  FROM project_assignments pa
  JOIN app_users u ON u.user_id=pa.user_id AND u.is_active=TRUE
  WHERE pa.effective_end_date IS NULL OR pa.effective_end_date>=CURRENT_DATE;
  IF assignment_target_count <= 0 THEN
    RAISE EXCEPTION 'No active project assignment targets exist for Module 001.';
  END IF;

  SELECT COUNT(*) INTO regular_task_count
  FROM project_tasks task
  WHERE task.is_active=TRUE
    AND NOT (
      LOWER(COALESCE(
        NULLIF(to_jsonb(task)->>'work_task_category',''),
        NULLIF(to_jsonb(task)->>'work_type',''),
        'project_task')) = 'service_request_task'
      OR COALESCE(NULLIF(to_jsonb(task)->>'service_request_number',''),'') <> ''
    );
  IF regular_task_count <= 0 THEN
    RAISE EXCEPTION 'No active regular project tasks exist for Module 001.';
  END IF;

  SELECT COUNT(*) INTO service_request_count
  FROM project_tasks task
  WHERE task.is_active=TRUE
    AND (
      LOWER(COALESCE(
        NULLIF(to_jsonb(task)->>'work_task_category',''),
        NULLIF(to_jsonb(task)->>'work_type',''),
        'project_task')) = 'service_request_task'
      OR COALESCE(NULLIF(to_jsonb(task)->>'service_request_number',''),'') <> ''
    );
  IF service_request_count <= 0 THEN
    RAISE EXCEPTION 'No active request or service-request tasks exist for Module 001.';
  END IF;

  SELECT COUNT(*) INTO non_project_count
  FROM non_project_time_categories WHERE is_active=TRUE;
  IF non_project_count <= 0 THEN
    RAISE EXCEPTION 'No active non-project time categories exist for Module 001.';
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
    RAISE EXCEPTION 'The governed default Certify profile is missing or automatic sync is unsafe.';
  END IF;

  IF (SELECT COUNT(*) FROM app_permissions WHERE permission_code IN (
      'VIEW_PROJECT_EXPENSE_UPLOAD','UPLOAD_PROJECT_EXPENSE_SELF',
      'UPLOAD_PROJECT_EXPENSE_ON_BEHALF','DELETE_PROJECT_EXPENSE_UPLOAD',
      'IMPORT_PROJECT_EXPENSE_CERTIFY','VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT',
      'MANAGE_CERTIFY_CONNECTION')) <> 7 THEN
    RAISE EXCEPTION 'Module 005/038 permissions are incomplete.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM app_roles r
    JOIN app_role_permissions rp USING(app_role_id)
    JOIN app_permissions p USING(app_permission_id)
    WHERE UPPER(r.role_code) IN ('ENGINEER','ENGINEERING')
      AND p.permission_code='IMPORT_PROJECT_EXPENSE_CERTIFY'
  ) THEN
    RAISE EXCEPTION 'Engineer Certify self-import permission is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM app_roles r
    JOIN app_role_permissions rp USING(app_role_id)
    JOIN app_permissions p USING(app_permission_id)
    WHERE UPPER(r.role_code) IN ('PROJECT_MANAGER','PROJECT_MANAGEMENT')
      AND p.permission_code='UPLOAD_PROJECT_EXPENSE_ON_BEHALF'
  ) THEN
    RAISE EXCEPTION 'Project Management on-behalf upload permission is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM app_roles r
    JOIN app_role_permissions rp USING(app_role_id)
    JOIN app_permissions p USING(app_permission_id)
    WHERE UPPER(r.role_code)='ACCOUNTING'
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
    RAISE EXCEPTION 'Module 005 or Module 038 catalog identity is incorrect.';
  END IF;

  RAISE NOTICE 'COMBINED_COUNTS roles=% modules=% grants=% eligibleUsers=% operators=% assignments=% regularTasks=% serviceRequests=% nonProject=%',
    canonical_role_count,module_count,grant_count,eligible_user_count,operator_count,
    assignment_target_count,regular_task_count,service_request_count,non_project_count;
END
\$verify_combined_modules\$;
SELECT 'COMBINED_MODULES_001_005_012_037_038_DATABASE=APPLIED_OR_VERIFIED';"
