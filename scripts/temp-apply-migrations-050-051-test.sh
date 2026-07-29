#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="5d269021b1f8df471a0e1d1b654e17e0c7b1576c"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
MODE="${PROJECTPULSE_CUMULATIVE_MIGRATION_MODE:-verify}"
MIGRATION_050="050_project_notification_routing_and_schedules.sql"
MIGRATION_051="051_financial_operations_reporting_recovery.sql"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "PROJECTPULSE_CUMULATIVE_MIGRATION_MODE must be apply or verify."
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
for migration in "$MIGRATION_050" "$MIGRATION_051"; do
  [[ -f "$MIGRATION_ROOT/$migration" ]] || fail "Migration source is missing: $migration"
done
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "2" ]] ||
  fail "Migration checksum manifest must contain exactly two SQL files."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "PROJECTPULSE_MIGRATIONS_050_051_CHECKSUM=VERIFIED"

read -r USERS_BEFORE PROJECTS_BEFORE ASSIGNMENTS_BEFORE ENTRIES_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM time_entries);" | tr '|' ' '
)"
[[ -n "${USERS_BEFORE:-}" ]] || fail "Required ProjectPulse tables are unavailable."

migration_registered() {
  local migration_id="$1"
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT EXISTS (
      SELECT 1 FROM schema_migrations WHERE migration_id='$migration_id'
    );"
}

apply_or_verify() {
  local migration_id="$1" migration_file="$2" registered
  registered="$(migration_registered "$migration_id")"
  case "$registered:$MODE" in
    t:apply)
      echo "MIGRATION_${migration_id}=ALREADY_REGISTERED"
      ;;
    t:verify)
      echo "MIGRATION_${migration_id}=REGISTERED"
      ;;
    f:apply)
      echo "MIGRATION_${migration_id}=APPLYING"
      psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
        --file="$MIGRATION_ROOT/$migration_file"
      ;;
    f:verify)
      fail "Migration $migration_id is not registered; apply authorization is required."
      ;;
    *)
      fail "Unexpected migration registration state for $migration_id: $registered"
      ;;
  esac
}

apply_or_verify "050_project_notification_routing_and_schedules" "$MIGRATION_050"
apply_or_verify "051_financial_operations_reporting_recovery" "$MIGRATION_051"

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
DO $projectpulse_verify$
DECLARE
  missing_tables text[];
  permission_count integer;
  feature_count integer;
  rule_count integer;
  schedule_count integer;
  existing_roles integer;
  complete_roles integer;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='050_project_notification_routing_and_schedules'
  ) THEN
    RAISE EXCEPTION 'Migration 050 is not registered.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='051_financial_operations_reporting_recovery'
  ) THEN
    RAISE EXCEPTION 'Migration 051 is not registered.';
  END IF;

  SELECT array_agg(required_name)
  INTO missing_tables
  FROM unnest(ARRAY[
    'project_cost_alert_routing_rules',
    'project_notification_schedules',
    'project_notification_dispatches',
    'project_notification_dispatch_recipients',
    'project_notification_delivery_attempts',
    'project_notification_configuration_audit',
    'financial_report_runs',
    'financial_operations_work_items',
    'financial_operations_actions'
  ]) AS required_name
  WHERE to_regclass('public.' || required_name) IS NULL;

  IF missing_tables IS NOT NULL THEN
    RAISE EXCEPTION 'Missing cumulative release tables: %', missing_tables;
  END IF;

  SELECT COUNT(*) INTO permission_count
  FROM app_permissions
  WHERE permission_code = ANY(ARRAY[
    'VIEW_COST_ALERT_ROUTING_RULES',
    'MANAGE_COST_ALERT_ROUTING_RULES',
    'VIEW_NOTIFICATION_SCHEDULES',
    'MANAGE_NOTIFICATION_SCHEDULES',
    'VIEW_NOTIFICATION_DELIVERY_MONITOR',
    'MANAGE_NOTIFICATION_DELIVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_ROUTING',
    'DELIVER_PROJECT_NOTIFICATIONS'
  ]);
  IF permission_count <> 8 THEN
    RAISE EXCEPTION 'Expected 8 Group 4 permissions, found %.', permission_count;
  END IF;

  SELECT COUNT(*) INTO permission_count
  FROM app_permissions
  WHERE permission_code = ANY(ARRAY[
    'VIEW_FINANCIAL_REPORT_CENTER',
    'RUN_FINANCIAL_REPORTS',
    'EXPORT_FINANCIAL_REPORTS',
    'VIEW_FINANCIAL_OPERATIONS_WORKBENCH',
    'MANAGE_FINANCIAL_OPERATIONS_RECOVERY',
    'RETRY_FINANCIAL_SOURCES',
    'VIEW_ACCOUNTING_RECONCILIATION_RECOVERY',
    'VIEW_PROJECT_CLOSEOUT_RECOVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY',
    'VIEW_BILLING_RECOVERY'
  ]);
  IF permission_count <> 10 THEN
    RAISE EXCEPTION 'Expected 10 Group 5 permissions, found %.', permission_count;
  END IF;

  SELECT COUNT(*) INTO feature_count
  FROM app_feature_catalog
  WHERE feature_code = ANY(ARRAY[
    'FINANCIAL_REPORT_CENTER',
    'FINANCIAL_OPERATIONS_WORKBENCH',
    'BILLING_READINESS_RECOVERY',
    'PROJECT_CLOSEOUT_RECOVERY',
    'CLOSEOUT_NOTIFICATION_RECOVERY',
    'BILLING_RECOVERY'
  ]);
  IF feature_count <> 6 THEN
    RAISE EXCEPTION 'Expected 6 Group 5 feature-catalog entries, found %.', feature_count;
  END IF;

  SELECT COUNT(*) INTO rule_count
  FROM project_cost_alert_routing_rules
  WHERE rule_code = ANY(ARRAY[
    'HOURS_USED_APPROACHING',
    'LABOR_BUDGET_APPROACHING',
    'EXPENSE_BUDGET_APPROACHING',
    'FORECAST_OVER_BUDGET',
    'PROJECT_APPROACHING_BUDGET',
    'PROJECT_OVER_BUDGET',
    'MISSING_FINANCIAL_INFORMATION',
    'PROJECT_DATA_REFRESH_FAILED'
  ]);
  IF rule_count <> 8 THEN
    RAISE EXCEPTION 'Expected 8 Group 4 routing rules, found %.', rule_count;
  END IF;

  SELECT COUNT(*) INTO schedule_count
  FROM project_notification_schedules
  WHERE schedule_code = ANY(ARRAY[
    'COST_ALERT_WEEKDAY_EVALUATION',
    'WEEKLY_PROJECT_REMINDER',
    'MONDAY_PROJECT_ESCALATION',
    'MONTH_END_FINANCIAL_REMINDER'
  ]);
  IF schedule_count <> 4 THEN
    RAISE EXCEPTION 'Expected 4 Group 4 schedules, found %.', schedule_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_indexes
    WHERE schemaname='public'
      AND indexname='ux_project_notification_dispatch_recipients_email'
  ) THEN
    RAISE EXCEPTION 'Group 4 recipient uniqueness index is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_projectpulse050_delivery_attempts_immutable'
      AND NOT tgisinternal
  ) OR NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_projectpulse050_configuration_audit_immutable'
      AND NOT tgisinternal
  ) OR NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_projectpulse051_financial_actions_immutable'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Cumulative immutable evidence triggers are incomplete.';
  END IF;

  SELECT COUNT(*) INTO existing_roles
  FROM app_roles
  WHERE is_active=TRUE
    AND upper(role_code) IN ('PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR', 'ADMINISTRATOR');

  SELECT COUNT(*) INTO complete_roles
  FROM (
    SELECT role.app_role_id
    FROM app_roles role
    JOIN app_role_permissions role_permission
      ON role_permission.app_role_id=role.app_role_id
    JOIN app_permissions permission
      ON permission.app_permission_id=role_permission.app_permission_id
    WHERE role.is_active=TRUE
      AND upper(role.role_code) IN ('PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR', 'ADMINISTRATOR')
      AND permission.permission_code = ANY(ARRAY[
        'VIEW_COST_ALERT_ROUTING_RULES',
        'MANAGE_COST_ALERT_ROUTING_RULES',
        'VIEW_NOTIFICATION_SCHEDULES',
        'MANAGE_NOTIFICATION_SCHEDULES',
        'VIEW_NOTIFICATION_DELIVERY_MONITOR',
        'MANAGE_NOTIFICATION_DELIVERY',
        'VIEW_CLOSEOUT_NOTIFICATION_ROUTING',
        'DELIVER_PROJECT_NOTIFICATIONS'
      ])
    GROUP BY role.app_role_id
    HAVING COUNT(DISTINCT permission.permission_code)=8
  ) complete;

  IF complete_roles <> existing_roles THEN
    RAISE EXCEPTION 'One or more privileged roles are missing the complete Group 4 permission set.';
  END IF;

  SELECT COUNT(*) INTO existing_roles
  FROM app_roles
  WHERE is_active=TRUE
    AND upper(role_code) IN (
      'ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE',
      'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'
    );

  SELECT COUNT(*) INTO complete_roles
  FROM (
    SELECT role.app_role_id
    FROM app_roles role
    JOIN app_role_permissions role_permission
      ON role_permission.app_role_id=role.app_role_id
    JOIN app_permissions permission
      ON permission.app_permission_id=role_permission.app_permission_id
    WHERE role.is_active=TRUE
      AND upper(role.role_code) IN (
        'ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE',
        'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'
      )
      AND permission.permission_code = ANY(ARRAY[
        'VIEW_FINANCIAL_REPORT_CENTER',
        'RUN_FINANCIAL_REPORTS',
        'EXPORT_FINANCIAL_REPORTS',
        'VIEW_FINANCIAL_OPERATIONS_WORKBENCH',
        'MANAGE_FINANCIAL_OPERATIONS_RECOVERY',
        'RETRY_FINANCIAL_SOURCES',
        'VIEW_ACCOUNTING_RECONCILIATION_RECOVERY',
        'VIEW_PROJECT_CLOSEOUT_RECOVERY',
        'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY',
        'VIEW_BILLING_RECOVERY'
      ])
    GROUP BY role.app_role_id
    HAVING COUNT(DISTINCT permission.permission_code)=10
  ) complete;

  IF complete_roles <> existing_roles THEN
    RAISE EXCEPTION 'One or more privileged roles are missing the complete Group 5 permission set.';
  END IF;
END
$projectpulse_verify$;
SQL

read -r USERS_AFTER PROJECTS_AFTER ASSIGNMENTS_AFTER ENTRIES_AFTER <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM time_entries);" | tr '|' ' '
)"

[[ "$USERS_AFTER" == "$USERS_BEFORE" ]] || fail "Cumulative migrations changed app_users row count."
[[ "$PROJECTS_AFTER" == "$PROJECTS_BEFORE" ]] || fail "Cumulative migrations changed projects row count."
[[ "$ASSIGNMENTS_AFTER" == "$ASSIGNMENTS_BEFORE" ]] || fail "Cumulative migrations changed project_assignments row count."
[[ "$ENTRIES_AFTER" == "$ENTRIES_BEFORE" ]] || fail "Cumulative migrations changed time_entries row count."

echo "PROJECTPULSE_MIGRATIONS_050_051_OPERATIONAL_COUNTS=UNCHANGED"
echo "PROJECTPULSE_MIGRATIONS_050_051_INVARIANTS=VERIFIED"
if [[ "$MODE" == apply ]]; then
  echo "PROJECTPULSE_MIGRATIONS_050_051_RESULT=APPLIED_OR_ALREADY_PRESENT"
else
  echo "PROJECTPULSE_MIGRATIONS_050_051_RESULT=VERIFY_ONLY_PASS"
fi
