#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="24fb92d751726b1bab66c11d902c0b2571701b23"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
MODE="${GROUP4_MIGRATION_MODE:-verify}"
MIGRATION_FILE="050_project_notification_routing_and_schedules.sql"
MIGRATION_ID="050_project_notification_routing_and_schedules"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "GROUP4_MIGRATION_MODE must be apply or verify."
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
[[ -f "$MIGRATION_ROOT/$MIGRATION_FILE" ]] || fail "Migration 050 source is missing."
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "1" ]] ||
  fail "Migration checksum manifest must contain exactly one SQL file."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "GROUP4_MIGRATION_050_CHECKSUM=VERIFIED"

read -r USERS_BEFORE PROJECTS_BEFORE ASSIGNMENTS_BEFORE ENTRIES_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM time_entries);" | tr '|' ' '
)"
[[ -n "${USERS_BEFORE:-}" ]] || fail "Required ProjectPulse tables are unavailable."

REGISTERED="$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT EXISTS (
      SELECT 1 FROM schema_migrations WHERE migration_id='$MIGRATION_ID'
    );"
)"

case "$REGISTERED:$MODE" in
  t:apply)
    echo "GROUP4_MIGRATION_050=ALREADY_REGISTERED"
    ;;
  t:verify)
    echo "GROUP4_MIGRATION_050=REGISTERED"
    ;;
  f:apply)
    echo "GROUP4_MIGRATION_050=APPLYING"
    psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
      --file="$MIGRATION_ROOT/$MIGRATION_FILE"
    ;;
  f:verify)
    fail "Migration 050 is not registered; run the separately authorized apply Action first."
    ;;
  *)
    fail "Unexpected migration registration state: $REGISTERED"
    ;;
esac

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
  --set=users_before="$USERS_BEFORE" \
  --set=projects_before="$PROJECTS_BEFORE" \
  --set=assignments_before="$ASSIGNMENTS_BEFORE" \
  --set=entries_before="$ENTRIES_BEFORE" <<'SQL'
DO $group4_verify$
DECLARE
  missing_tables text[];
  permission_count integer;
  rule_count integer;
  schedule_count integer;
  existing_privileged_roles integer;
  complete_privileged_roles integer;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='050_project_notification_routing_and_schedules'
  ) THEN
    RAISE EXCEPTION 'Migration 050 is not registered.';
  END IF;

  SELECT array_agg(required_name)
  INTO missing_tables
  FROM unnest(ARRAY[
    'project_cost_alert_routing_rules',
    'project_notification_schedules',
    'project_notification_dispatches',
    'project_notification_dispatch_recipients',
    'project_notification_delivery_attempts',
    'project_notification_configuration_audit'
  ]) AS required_name
  WHERE to_regclass('public.' || required_name) IS NULL;

  IF missing_tables IS NOT NULL THEN
    RAISE EXCEPTION 'Missing Group 4 tables: %', missing_tables;
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
    RAISE EXCEPTION 'Expected 8 governed Group 4 routing rules, found %.', rule_count;
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
    RAISE EXCEPTION 'Expected 4 governed Group 4 schedules, found %.', schedule_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_indexes
    WHERE schemaname='public'
      AND indexname='ux_project_notification_dispatch_recipients_email'
  ) THEN
    RAISE EXCEPTION 'Case-insensitive recipient uniqueness index is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_projectpulse050_delivery_attempts_immutable'
      AND NOT tgisinternal
  ) OR NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_projectpulse050_configuration_audit_immutable'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Immutable Group 4 evidence triggers are incomplete.';
  END IF;

  SELECT COUNT(*) INTO existing_privileged_roles
  FROM app_roles
  WHERE is_active=TRUE
    AND upper(role_code) IN ('PROJECT_TEAM_COORDINATOR','SUPER_ADMINISTRATOR','ADMINISTRATOR');

  SELECT COUNT(*) INTO complete_privileged_roles
  FROM (
    SELECT role.app_role_id
    FROM app_roles role
    JOIN app_role_permissions role_permission
      ON role_permission.app_role_id=role.app_role_id
    JOIN app_permissions permission
      ON permission.app_permission_id=role_permission.app_permission_id
    WHERE role.is_active=TRUE
      AND upper(role.role_code) IN ('PROJECT_TEAM_COORDINATOR','SUPER_ADMINISTRATOR','ADMINISTRATOR')
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

  IF complete_privileged_roles <> existing_privileged_roles THEN
    RAISE EXCEPTION 'One or more privileged roles are missing the complete Group 4 permission set.';
  END IF;

  IF (SELECT COUNT(*) FROM app_users) <> :'users_before'::bigint
     OR (SELECT COUNT(*) FROM projects) <> :'projects_before'::bigint
     OR (SELECT COUNT(*) FROM project_assignments) <> :'assignments_before'::bigint
     OR (SELECT COUNT(*) FROM time_entries) <> :'entries_before'::bigint THEN
    RAISE EXCEPTION 'Migration 050 changed operational user, project, assignment, or time-entry counts.';
  END IF;
END
$group4_verify$;
SQL

echo "GROUP4_MIGRATION_050_INVARIANTS=VERIFIED"
if [[ "$MODE" == apply ]]; then
  echo "GROUP4_MIGRATION_050_RESULT=APPLIED_OR_ALREADY_PRESENT"
else
  echo "GROUP4_MIGRATION_050_RESULT=VERIFY_ONLY_PASS"
fi
