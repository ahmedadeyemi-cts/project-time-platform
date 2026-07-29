#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-notification-050-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/050_project_notification_routing_and_schedules.sql"
ROLLBACK="/workspace/database/rollback/050_project_notification_routing_and_schedules_rollback.sql"

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

expect_sql_failure() {
  local sql="$1" expected="$2" label="$3"
  local log="/tmp/project-notification-050-${label}.log"
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
  project_code TEXT NOT NULL,
  project_name TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'active',
  project_manager_user_id UUID NULL REFERENCES app_users(user_id),
  project_coordinator_user_id UUID NULL REFERENCES app_users(user_id),
  solution_architect_user_id UUID NULL REFERENCES app_users(user_id),
  account_executive_user_id UUID NULL REFERENCES app_users(user_id)
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
  PRIMARY KEY (app_role_id, app_permission_id)
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

INSERT INTO app_users (user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','pm@example.test','PM Test'),
 ('10000000-0000-0000-0000-000000000002','ptc@example.test','PTC Test'),
 ('10000000-0000-0000-0000-000000000003','admin@example.test','Admin Test'),
 ('10000000-0000-0000-0000-000000000004','engineer@example.test','Engineer Test'),
 ('10000000-0000-0000-0000-000000000005','sales@example.test','Sales Test'),
 ('10000000-0000-0000-0000-000000000006','sa@example.test','Solution Architect Test'),
 ('10000000-0000-0000-0000-000000000007','accounting@example.test','Accounting Test');

INSERT INTO projects (
  project_id,project_code,project_name,project_manager_user_id,
  project_coordinator_user_id,solution_architect_user_id,account_executive_user_id
) VALUES (
  '20000000-0000-0000-0000-000000000001','P-050','Notification Test Project',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000002',
  '10000000-0000-0000-0000-000000000006',
  '10000000-0000-0000-0000-000000000005'
);

INSERT INTO app_roles (role_code,role_name) VALUES
 ('ENGINEERING','Engineering'),
 ('ENGINEER','Engineer'),
 ('ENGINEERING_LEAD','Engineering Lead'),
 ('ENGINEERING_TEAM_LEAD','Engineering Team Lead'),
 ('PROJECT_MANAGEMENT','Project Management'),
 ('PROJECT_MANAGER','Project Manager'),
 ('PROJECT_MANAGEMENT_LEAD','Project Management Lead'),
 ('PROJECT_MANAGEMENT_TEAM_LEAD','Project Management Team Lead'),
 ('PM_TEAM_LEAD','PM Team Lead'),
 ('MANAGER','Manager'),
 ('SALES','Sales'),
 ('INSIDE_SALES','Inside Sales'),
 ('SOLUTION_ARCHITECT','Solution Architect'),
 ('EXECUTIVE','Executive'),
 ('PROJECT_TEAM_COORDINATOR','Project Team Coordinator'),
 ('ACCOUNTING','Accounting'),
 ('ACCOUNTING_BILLING','Accounting Billing'),
 ('BILLING','Billing'),
 ('FINANCE','Finance'),
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('ADMINISTRATOR','Administrator');
SQL

apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }

apply_migration
apply_migration

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='050_project_notification_routing_and_schedules';")" migration_registered_once
assert_eq 6 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('project_cost_alert_routing_rules','project_notification_schedules','project_notification_dispatches','project_notification_dispatch_recipients','project_notification_delivery_attempts','project_notification_configuration_audit');")" tables_created
assert_eq 8 "$(value "SELECT COUNT(*) FROM project_cost_alert_routing_rules;")" seeded_routing_rules
assert_eq 4 "$(value "SELECT COUNT(*) FROM project_notification_schedules;")" seeded_notification_schedules
assert_eq 8 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_COST_ALERT_ROUTING_RULES','MANAGE_COST_ALERT_ROUTING_RULES','VIEW_NOTIFICATION_SCHEDULES','MANAGE_NOTIFICATION_SCHEDULES','VIEW_NOTIFICATION_DELIVERY_MONITOR','MANAGE_NOTIFICATION_DELIVERY','VIEW_CLOSEOUT_NOTIFICATION_ROUTING','DELIVER_PROJECT_NOTIFICATIONS');")" permissions_created
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code IN ('COST_ALERT_ROUTING_RULES','PROJECT_NOTIFICATION_SCHEDULING','NOTIFICATION_DELIVERY_MONITOR','CLOSEOUT_NOTIFICATION_ROUTING');")" feature_catalog_entries
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='NOTIFICATION_DELIVERY_MONITOR' AND module_code='032' AND required_permission_code='VIEW_NOTIFICATION_DELIVERY_MONITOR';")" module_032_registered
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_TEAM_COORDINATOR' AND p.permission_code='MANAGE_NOTIFICATION_DELIVERY';")" ptc_manage_delivery
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGEMENT' AND p.permission_code='VIEW_NOTIFICATION_DELIVERY_MONITOR';")" pm_view_delivery
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEERING' AND p.permission_code='VIEW_NOTIFICATION_DELIVERY_MONITOR';")" engineering_view_delivery
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SALES' AND p.permission_code='VIEW_NOTIFICATION_DELIVERY_MONITOR';")" sales_view_delivery
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SOLUTION_ARCHITECT' AND p.permission_code='VIEW_COST_ALERT_ROUTING_RULES';")" solution_architect_view_rules

psql_exec <<'SQL'
INSERT INTO project_notification_dispatches (
  project_notification_dispatch_id,project_id,event_key,notification_type,
  source_module,subject,text_body,delivery_status
) VALUES (
  '30000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',
  'test:050','test_notice','032','Migration test','Test body','held'
);

INSERT INTO project_notification_dispatch_recipients (
  project_notification_dispatch_id,recipient_role,recipient_user_id,
  recipient_name,recipient_email,recipient_type,derivation_source
) VALUES (
  '30000000-0000-0000-0000-000000000001','project_manager',
  '10000000-0000-0000-0000-000000000001','PM Test','PM@EXAMPLE.TEST','to',
  'projects.project_manager_user_id'
);

INSERT INTO project_notification_delivery_attempts (
  project_notification_delivery_attempt_id,project_notification_dispatch_id,
  attempt_number,attempt_status,diagnostic_code,diagnostic_message
) VALUES (
  '40000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',1,'suppressed',
  'TEST_ONLY','No external mail sent during migration test.'
);

INSERT INTO project_notification_configuration_audit (
  project_notification_configuration_audit_id,entity_type,entity_id,
  action_code,change_reason
) VALUES (
  '50000000-0000-0000-0000-000000000001','dispatch',
  '30000000-0000-0000-0000-000000000001','TEST','Migration test evidence'
);
SQL

expect_sql_failure "INSERT INTO project_notification_dispatch_recipients (project_notification_dispatch_id,recipient_role,recipient_name,recipient_email,recipient_type,derivation_source) VALUES ('30000000-0000-0000-0000-000000000001','project_manager','Duplicate','pm@example.test','to','test');" 'duplicate key value violates unique constraint' recipient_case_insensitive_unique
expect_sql_failure "UPDATE project_notification_delivery_attempts SET diagnostic_code='MUTATED' WHERE project_notification_delivery_attempt_id='40000000-0000-0000-0000-000000000001';" 'immutable' delivery_attempt_evidence_immutable
expect_sql_failure "DELETE FROM project_notification_configuration_audit WHERE project_notification_configuration_audit_id='50000000-0000-0000-0000-000000000001';" 'immutable' configuration_audit_evidence_immutable

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.project_notification_schedules')::text,'');")" rollback_removed_schedules
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.project_notification_dispatches')::text,'');")" rollback_removed_dispatches
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='050_project_notification_routing_and_schedules';")" rollback_removed_migration_row
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='VIEW_NOTIFICATION_DELIVERY_MONITOR';")" rollback_removed_permissions
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='NOTIFICATION_DELIVERY_MONITOR';")" rollback_removed_module_032_feature

echo 'PROJECT_NOTIFICATION_MIGRATION_050=PASS'
