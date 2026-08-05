#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-enterprise-notification-064-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/064_module_065_enterprise_notification_orchestration.sql"
ROLLBACK="/workspace/database/rollback/064_module_065_enterprise_notification_orchestration_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}

value() { psql_exec -Atqc "$1" | tr -d '\r'; }

assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

assert_ge() {
  local minimum="$1" actual="$2" label="$3"
  (( actual >= minimum )) || {
    echo "ASSERTION_FAILED $label minimum=$minimum actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

expect_sql_failure() {
  local sql="$1" expected="$2" label="$3"
  local log="/tmp/projectpulse-enterprise-notification-064-${label}.log"
  if psql_exec -c "$sql" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || {
    echo "ASSERTION_FAILED $label missing_expected=$expected" >&2
    cat "$log" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label"
}

docker run --detach --rm \
  --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  if psql_exec -Atqc 'SELECT 1;' >/dev/null 2>&1; then break; fi
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE schema_migrations (
  migration_id TEXT PRIMARY KEY,
  description TEXT NOT NULL DEFAULT '',
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE app_users (
  user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE projects (
  project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_code TEXT NOT NULL DEFAULT '',
  project_name TEXT NOT NULL DEFAULT ''
);

CREATE TABLE app_roles (
  app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  role_code TEXT NOT NULL UNIQUE,
  role_name TEXT NOT NULL DEFAULT '',
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE app_permissions (
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code TEXT NOT NULL UNIQUE,
  permission_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  permission_description TEXT NOT NULL DEFAULT ''
);

CREATE TABLE app_role_permissions (
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
  app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
  PRIMARY KEY(app_role_id, app_permission_id)
);

CREATE TABLE app_feature_catalog (
  feature_code TEXT PRIMARY KEY,
  feature_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  route_anchor TEXT NOT NULL DEFAULT '',
  required_permission_code TEXT NOT NULL DEFAULT '',
  feature_description TEXT NOT NULL DEFAULT '',
  display_order INTEGER NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE project_notification_dispatches (
  project_notification_dispatch_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  event_key TEXT NOT NULL UNIQUE,
  notification_type TEXT NOT NULL,
  delivery_status TEXT NOT NULL DEFAULT 'queued',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE project_notification_dispatch_recipients (
  project_notification_dispatch_recipient_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_notification_dispatch_id UUID NOT NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE CASCADE,
  recipient_email TEXT NOT NULL
);
CREATE TABLE project_notification_delivery_attempts (
  project_notification_delivery_attempt_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_notification_dispatch_id UUID NOT NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE CASCADE,
  attempt_status TEXT NOT NULL
);

-- Existing Module 032 delivery evidence must remain untouched by Module 065 rollback.
INSERT INTO project_notification_dispatches (
  project_notification_dispatch_id,event_key,notification_type,delivery_status
) VALUES (
  '09000000-0000-0000-0000-000000000001',
  'preexisting:module032:dispatch','project_assignment_changed','sent'
);

INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Admin Test'),
 ('10000000-0000-0000-0000-000000000002','ptc@example.test','PTC Test'),
 ('10000000-0000-0000-0000-000000000003','engineer@example.test','Engineer Test');

INSERT INTO projects(project_id,project_code,project_name) VALUES
 ('20000000-0000-0000-0000-000000000001','P-064','Enterprise Notification Test');

INSERT INTO app_roles(role_code,role_name) VALUES
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('ADMINISTRATOR','Administrator'),
 ('PROJECT_TEAM_COORDINATOR','Project Team Coordinator'),
 ('ENGINEERING','Engineering');

-- This permission predates migration 064 and must survive rollback.
INSERT INTO app_permissions(
  app_permission_id,permission_code,permission_name,module_code,permission_description
) VALUES (
  '30000000-0000-0000-0000-000000000001',
  'VIEW_ENTERPRISE_NOTIFICATIONS_065',
  'Preexisting view permission',
  '065',
  'Created before migration 064.'
);
SQL

apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }

apply_migration
apply_migration

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='064_module_065_enterprise_notification_orchestration';")" migration_registered_once
assert_eq 8 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('enterprise_notification_policies','enterprise_notification_events','enterprise_notification_event_history','enterprise_notification_acknowledgements','enterprise_notification_source_checkpoints','enterprise_notification_run_history','enterprise_notification_policy_audit','enterprise_notification_064_permissions_created');")" core_tables_created
assert_ge 45 "$(value "SELECT COUNT(*) FROM enterprise_notification_policies;")" complete_policy_inventory
assert_eq 1 "$(value "SELECT COUNT(*) FROM enterprise_notification_policies WHERE policy_code='TIME_APPROVAL_OVERDUE_3_DAYS' AND trigger_configuration->>'ageDays'='3';")" three_day_approval_reminder
assert_eq 1 "$(value "SELECT COUNT(*) FROM enterprise_notification_policies WHERE policy_code='MONDAY_TIME_COMPLIANCE' AND trigger_configuration->>'timezone'='America/Chicago' AND trigger_configuration->>'localTime'='08:00';")" monday_chicago_schedule
assert_eq 1 "$(value "SELECT COUNT(*) FROM enterprise_notification_policies WHERE policy_code='QUALIFICATION_EXPIRING' AND trigger_configuration->'offsetDays'='[90, 60, 30, 14, 7, 1, 0]'::jsonb;")" certification_offsets
assert_eq 1 "$(value "SELECT COUNT(*) FROM enterprise_notification_inventory WHERE policy_code='SECURITY_EVENT' AND delivery_authority='module_065' AND direct_smtp_authorized=FALSE AND direct_brevo_authorized=FALSE;")" exclusive_module065_delivery
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_ENTERPRISE_NOTIFICATIONS_065','MANAGE_ENTERPRISE_NOTIFICATIONS_065','RUN_ENTERPRISE_NOTIFICATIONS_065');")" permission_catalog_complete
assert_eq 2 "$(value "SELECT COUNT(*) FROM enterprise_notification_064_permissions_created;")" only_new_permissions_recorded
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles role JOIN app_role_permissions relationship USING(app_role_id) JOIN app_permissions permission USING(app_permission_id) WHERE role.role_code='PROJECT_TEAM_COORDINATOR' AND permission.permission_code='RUN_ENTERPRISE_NOTIFICATIONS_065';")" ptc_run_authority
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles role JOIN app_role_permissions relationship USING(app_role_id) JOIN app_permissions permission USING(app_permission_id) WHERE role.role_code='ENGINEERING' AND permission.permission_code='MANAGE_ENTERPRISE_NOTIFICATIONS_065';")" engineering_manage_denied

# Reapplication must not overwrite administrator policy choices.
psql_exec -c "UPDATE enterprise_notification_policies SET enabled=FALSE, delivery_boundary='locked' WHERE policy_code='SECURITY_EVENT';" >/dev/null
apply_migration
assert_eq 'false|locked' "$(value "SELECT enabled::text || '|' || delivery_boundary FROM enterprise_notification_policies WHERE policy_code='SECURITY_EVENT';")" reapply_preserved_policy_configuration

psql_exec <<'SQL'
INSERT INTO enterprise_notification_events (
  enterprise_notification_event_id,policy_code,source_module,source_event_id,
  idempotency_key,entity_type,entity_id,subject_user_id,occurred_at,payload,
  ingestion_source,event_status
) VALUES (
  '40000000-0000-0000-0000-000000000001',
  'TIME_SUBMISSION_CONFIRMATION','001','test-event-1','test:064:event-1',
  'timesheet','50000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000003',NOW(),
  '{"workDate":"2026-08-01"}'::jsonb,'authoritative_scanner','pending'
);

INSERT INTO enterprise_notification_event_history (
  enterprise_notification_event_id,history_code,event_status,diagnostic_code,
  history_metadata,correlation_id
) VALUES (
  '40000000-0000-0000-0000-000000000001','EVENT_ACCEPTED','pending','',
  '{"test":true}'::jsonb,'migration-064-test'
);

INSERT INTO enterprise_notification_acknowledgements (
  enterprise_notification_event_id,user_id,acknowledged_by_actual_user_id,
  acknowledgement_statement
) VALUES (
  '40000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000003',
  '10000000-0000-0000-0000-000000000003',
  'Migration test acknowledgement'
);
SQL

expect_sql_failure "UPDATE enterprise_notification_event_history SET diagnostic_code='MUTATED' WHERE enterprise_notification_event_id='40000000-0000-0000-0000-000000000001';" 'immutable' event_history_immutable
expect_sql_failure "DELETE FROM enterprise_notification_acknowledgements WHERE enterprise_notification_event_id='40000000-0000-0000-0000-000000000001';" 'immutable' acknowledgement_immutable
expect_sql_failure "INSERT INTO enterprise_notification_events (policy_code,source_module,source_event_id,idempotency_key,occurred_at,ingestion_source) VALUES ('TIME_SUBMISSION_CONFIRMATION','001','test-event-2','test:064:event-1',NOW(),'authoritative_scanner');" 'duplicate key value violates unique constraint' event_idempotency_enforced

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.enterprise_notification_events')::text,'');")" rollback_removed_event_store
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.enterprise_notification_policies')::text,'');")" rollback_removed_policy_store
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='064_module_065_enterprise_notification_orchestration';")" rollback_removed_registration
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='VIEW_ENTERPRISE_NOTIFICATIONS_065' AND permission_name='Preexisting view permission';")" rollback_preserved_preexisting_permission
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('MANAGE_ENTERPRISE_NOTIFICATIONS_065','RUN_ENTERPRISE_NOTIFICATIONS_065');")" rollback_removed_only_created_permissions
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='ENTERPRISE_NOTIFICATION_ORCHESTRATION';")" rollback_removed_created_feature
assert_eq 1 "$(value "SELECT COUNT(*) FROM project_notification_dispatches WHERE event_key IS NOT NULL;")" rollback_preserved_existing_dispatch_table

apply_migration
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='064_module_065_enterprise_notification_orchestration';")" migration_reapplied
assert_ge 45 "$(value "SELECT COUNT(*) FROM enterprise_notification_policies;")" policy_inventory_reapplied

echo 'MODULE_065_ENTERPRISE_NOTIFICATION_ORCHESTRATION_MIGRATION_064=PASS'
