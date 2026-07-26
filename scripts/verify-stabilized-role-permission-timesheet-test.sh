#!/usr/bin/env bash
set -Eeuo pipefail

DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
[[ -n "$DATABASE_URL" ]] || { echo 'ERROR: PROJECTPULSE_TEST_DATABASE_URL is not configured.' >&2; exit 1; }
command -v psql >/dev/null || { echo 'ERROR: psql is required.' >&2; exit 1; }

IFS='|' read -r ROLE_COUNT MODULE_COUNT PUBLISHED_COUNT GRANT_COUNT SUPER_ADMIN_COUNT ELIGIBLE_USER_COUNT ASSIGNMENT_TARGET_COUNT REGULAR_TASK_COUNT SERVICE_REQUEST_COUNT NON_PROJECT_COUNT MIGRATION_043_COUNT <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At -F '|' --set=ON_ERROR_STOP=1 --command="
    BEGIN READ ONLY;
    WITH canonical_roles(role_code) AS (
      VALUES
        ('ENGINEERING'),('PROJECT_MANAGEMENT'),('ENGINEERING_LEAD'),('PROJECT_MANAGEMENT_LEAD'),
        ('MANAGER'),('SALES'),('INSIDE_SALES'),('SOLUTION_ARCHITECT'),('EXECUTIVE'),
        ('PROJECT_TEAM_COORDINATOR'),('ACCOUNTING'),('SUPER_ADMINISTRATOR')
    ), eligible_aliases(role_code) AS (
      VALUES
        ('ENGINEERING'),('ENGINEER'),
        ('ENGINEERING_LEAD'),('ENGINEERING_TEAM_LEAD'),
        ('PROJECT_MANAGEMENT'),('PROJECT_MANAGER'),
        ('PROJECT_MANAGEMENT_LEAD'),('PROJECT_MANAGEMENT_TEAM_LEAD'),('PM_TEAM_LEAD')
    ), assignment_targets AS (
      SELECT pa.project_assignment_id,
             COALESCE(NULLIF(to_jsonb(pt)->>'work_task_category',''),
                      NULLIF(to_jsonb(pt)->>'work_type',''), 'project_task') AS work_category,
             COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number',''),'') AS service_request_number
      FROM project_assignments pa
      JOIN projects p ON p.project_id=pa.project_id
      JOIN project_tasks pt ON pt.task_id=pa.task_id AND pt.project_id=pa.project_id
      JOIN app_users u ON u.user_id=pa.user_id AND u.is_active=TRUE
      WHERE p.status IN ('active','on_hold')
        AND pt.is_active=TRUE
        AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= CURRENT_DATE - 14)
    )
    SELECT
      (SELECT COUNT(*) FROM app_roles r JOIN canonical_roles c ON c.role_code=UPPER(r.role_code) WHERE r.is_active=TRUE),
      (SELECT COUNT(*) FROM scoped_role_policy_modules WHERE is_active=TRUE),
      (SELECT COUNT(*) FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED'),
      (SELECT COUNT(*) FROM scoped_role_policy_effective_grants),
      (SELECT COUNT(DISTINCT u.user_id)
         FROM app_users u
         JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
         JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
        WHERE u.is_active=TRUE AND UPPER(r.role_code) IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR')),
      (SELECT COUNT(DISTINCT u.user_id)
         FROM app_users u
         JOIN app_user_role_assignments ura ON ura.user_id=u.user_id AND ura.is_active=TRUE
         JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
         JOIN eligible_aliases e ON e.role_code=UPPER(r.role_code)
        WHERE u.is_active=TRUE),
      (SELECT COUNT(*) FROM assignment_targets),
      (SELECT COUNT(*) FROM assignment_targets
        WHERE lower(work_category) <> 'service_request_task' AND service_request_number=''),
      (SELECT COUNT(*) FROM assignment_targets
        WHERE lower(work_category) = 'service_request_task' OR service_request_number<>''),
      (SELECT COUNT(*) FROM non_project_time_categories WHERE is_active=TRUE),
      (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='043_ptc_time_steward_permissions');
    ROLLBACK;"
)"

printf 'STABILIZED_ROLE_COUNT=%s\n' "$ROLE_COUNT"
printf 'STABILIZED_MODULE_COUNT=%s\n' "$MODULE_COUNT"
printf 'STABILIZED_PUBLISHED_POLICY_COUNT=%s\n' "$PUBLISHED_COUNT"
printf 'STABILIZED_EFFECTIVE_GRANT_COUNT=%s\n' "$GRANT_COUNT"
printf 'STABILIZED_SUPER_ADMIN_COUNT=%s\n' "$SUPER_ADMIN_COUNT"
printf 'STABILIZED_ELIGIBLE_USER_COUNT=%s\n' "$ELIGIBLE_USER_COUNT"
printf 'STABILIZED_ASSIGNMENT_TARGET_COUNT=%s\n' "$ASSIGNMENT_TARGET_COUNT"
printf 'STABILIZED_REGULAR_TASK_COUNT=%s\n' "$REGULAR_TASK_COUNT"
printf 'STABILIZED_SERVICE_REQUEST_TASK_COUNT=%s\n' "$SERVICE_REQUEST_COUNT"
printf 'STABILIZED_NON_PROJECT_COUNT=%s\n' "$NON_PROJECT_COUNT"
printf 'STABILIZED_MIGRATION_043_COUNT=%s\n' "$MIGRATION_043_COUNT"

[[ "$ROLE_COUNT" == 12 ]] || { echo "ERROR: Expected 12 active canonical roles; found $ROLE_COUNT." >&2; exit 1; }
[[ "$MODULE_COUNT" == 70 ]] || { echo "ERROR: Expected 70 active scoped modules; found $MODULE_COUNT." >&2; exit 1; }
[[ "$PUBLISHED_COUNT" == 1 ]] || { echo "ERROR: Expected exactly one published policy; found $PUBLISHED_COUNT." >&2; exit 1; }
(( GRANT_COUNT > 0 )) || { echo 'ERROR: No effective role-policy grants were found.' >&2; exit 1; }
(( SUPER_ADMIN_COUNT > 0 )) || { echo 'ERROR: No active Super Administrator assignment was found.' >&2; exit 1; }
(( ELIGIBLE_USER_COUNT > 0 )) || { echo 'ERROR: No active Engineer, Engineering Lead, Project Management, or Project Management Lead assignments were found.' >&2; exit 1; }
(( ASSIGNMENT_TARGET_COUNT > 0 )) || { echo 'ERROR: No active user-to-project-task assignments were found for timer targets.' >&2; exit 1; }
(( REGULAR_TASK_COUNT > 0 )) || { echo 'ERROR: No active Regular Task timer targets were found.' >&2; exit 1; }
(( SERVICE_REQUEST_COUNT > 0 )) || { echo 'ERROR: No active Request or Service Request timer targets were found.' >&2; exit 1; }
(( REGULAR_TASK_COUNT + SERVICE_REQUEST_COUNT == ASSIGNMENT_TARGET_COUNT )) || { echo 'ERROR: Timer assignment classification counts do not reconcile.' >&2; exit 1; }
(( NON_PROJECT_COUNT > 0 )) || { echo 'ERROR: No active non-project time categories were found.' >&2; exit 1; }
[[ "$MIGRATION_043_COUNT" == 1 ]] || { echo 'ERROR: Migration 043 is not registered.' >&2; exit 1; }

echo 'STABILIZED_ROLE_PERMISSION_TIMESHEET_DATA=PASS'
