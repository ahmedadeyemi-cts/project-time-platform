#!/usr/bin/env bash
set -Eeuo pipefail

DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"

fail() {
  echo "DIRECT_RUNTIME_DATA_VALIDATION=FAIL: $*" >&2
  exit 1
}

[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
command -v psql >/dev/null || fail "psql is required."

read -r ROLE_COUNT MODULE_COUNT PUBLISHED_COUNT ELIGIBLE_USER_COUNT GRANT_COUNT SUPER_ADMIN_COUNT MIGRATION_043 <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --field-separator=' ' --command="
    SELECT
      (SELECT COUNT(*)
       FROM app_roles
       WHERE is_active=TRUE
         AND role_code IN (
           'ENGINEERING','PROJECT_MANAGEMENT','ENGINEERING_LEAD','PROJECT_MANAGEMENT_LEAD',
           'MANAGER','SALES','INSIDE_SALES','SOLUTION_ARCHITECT','EXECUTIVE',
           'PROJECT_TEAM_COORDINATOR','ACCOUNTING','SUPER_ADMINISTRATOR'
         )),
      (SELECT COUNT(*) FROM scoped_role_policy_modules WHERE is_active=TRUE),
      (SELECT COUNT(*) FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED'),
      (SELECT COUNT(DISTINCT u.user_id)
       FROM app_users u
       JOIN app_user_role_assignments ura
         ON ura.user_id=u.user_id AND ura.is_active=TRUE
       JOIN app_roles r
         ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
       WHERE u.is_active=TRUE
         AND upper(r.role_code) IN (
           'ENGINEERING','ENGINEER',
           'ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD',
           'PROJECT_MANAGEMENT','PROJECT_MANAGER',
           'PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD'
         )),
      (SELECT COUNT(*) FROM scoped_role_policy_effective_grants),
      (SELECT COUNT(DISTINCT u.user_id)
       FROM app_users u
       JOIN app_user_role_assignments ura
         ON ura.user_id=u.user_id AND ura.is_active=TRUE
       JOIN app_roles r
         ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
       WHERE u.is_active=TRUE
         AND upper(r.role_code) IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR')),
      (SELECT CASE WHEN EXISTS (
         SELECT 1 FROM schema_migrations
         WHERE migration_id='043_ptc_time_steward_permissions'
       ) THEN 1 ELSE 0 END);"
)"

[[ "$ROLE_COUNT" == "12" ]] || fail "Expected 12 active canonical roles; found $ROLE_COUNT."
[[ "$MODULE_COUNT" == "70" ]] || fail "Expected 70 active scoped modules; found $MODULE_COUNT."
[[ "$PUBLISHED_COUNT" == "1" ]] || fail "Expected exactly one published role-policy version; found $PUBLISHED_COUNT."
[[ "$ELIGIBLE_USER_COUNT" =~ ^[0-9]+$ && "$ELIGIBLE_USER_COUNT" -ge 1 ]] || fail "Expected at least one eligible Engineer/Lead/PM user; found $ELIGIBLE_USER_COUNT."
[[ "$GRANT_COUNT" =~ ^[0-9]+$ && "$GRANT_COUNT" -ge 1 ]] || fail "Expected effective role-policy grants; found $GRANT_COUNT."
[[ "$SUPER_ADMIN_COUNT" =~ ^[0-9]+$ && "$SUPER_ADMIN_COUNT" -ge 1 ]] || fail "Expected at least one active Super Administrator; found $SUPER_ADMIN_COUNT."
[[ "$MIGRATION_043" == "1" ]] || fail "Migration 043 is not registered."

printf 'DIRECT_RUNTIME_DATA_ROLE_COUNT=%s\n' "$ROLE_COUNT"
printf 'DIRECT_RUNTIME_DATA_MODULE_COUNT=%s\n' "$MODULE_COUNT"
printf 'DIRECT_RUNTIME_DATA_PUBLISHED_POLICY_COUNT=%s\n' "$PUBLISHED_COUNT"
printf 'DIRECT_RUNTIME_DATA_ELIGIBLE_USER_COUNT=%s\n' "$ELIGIBLE_USER_COUNT"
printf 'DIRECT_RUNTIME_DATA_EFFECTIVE_GRANT_COUNT=%s\n' "$GRANT_COUNT"
printf 'DIRECT_RUNTIME_DATA_SUPER_ADMIN_COUNT=%s\n' "$SUPER_ADMIN_COUNT"
printf 'DIRECT_RUNTIME_DATA_MIGRATION_043=REGISTERED\n'
printf 'DIRECT_RUNTIME_DATA_VALIDATION=PASS\n'
