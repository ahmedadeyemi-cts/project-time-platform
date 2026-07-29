#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-group5-051-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/051_financial_operations_reporting_recovery.sql"
ROLLBACK="/workspace/database/rollback/051_financial_operations_reporting_recovery_rollback.sql"

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
  local sql="$1" expected="$2" label="$3" log="/tmp/group5-051-${label}.log"
  if psql_exec -c "$sql" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || {
    echo "ASSERTION_FAILED $label missing_expected_error=$expected" >&2
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
  user_id UUID PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE clients (
  client_id UUID PRIMARY KEY,
  client_name TEXT NOT NULL
);
CREATE TABLE projects (
  project_id UUID PRIMARY KEY,
  client_id UUID NULL REFERENCES clients(client_id),
  project_code TEXT NOT NULL,
  project_name TEXT NOT NULL,
  project_manager_user_id UUID NULL REFERENCES app_users(user_id)
);
CREATE TABLE app_roles (
  app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  role_code TEXT NOT NULL UNIQUE,
  role_name TEXT NOT NULL,
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
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
  app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
  UNIQUE(app_role_id, app_permission_id)
);
CREATE TABLE app_feature_catalog (
  app_feature_catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  feature_code TEXT NOT NULL UNIQUE,
  feature_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  route_anchor TEXT,
  required_permission_code TEXT,
  feature_description TEXT,
  display_order INTEGER NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Administrator'),
 ('10000000-0000-0000-0000-000000000002','pm@example.test','Project Manager'),
 ('10000000-0000-0000-0000-000000000003','accounting@example.test','Accounting');
INSERT INTO clients(client_id,client_name) VALUES
 ('20000000-0000-0000-0000-000000000001','Customer Test');
INSERT INTO projects(project_id,client_id,project_code,project_name,project_manager_user_id) VALUES
 ('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','P-051','Financial Recovery Test','10000000-0000-0000-0000-000000000002');
INSERT INTO app_roles(role_code,role_name) VALUES
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('PROJECT_TEAM_COORDINATOR','Project Team Coordinator'),
 ('ACCOUNTING','Accounting'),
 ('PROJECT_MANAGEMENT','Project Management'),
 ('EXECUTIVE','Executive'),
 ('ENGINEERING','Engineering'),
 ('SALES','Sales'),
 ('SOLUTION_ARCHITECT','Solution Architect');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='051_financial_operations_reporting_recovery';")" migration_registered_once
assert_eq 3 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('financial_report_runs','financial_operations_work_items','financial_operations_actions');")" tables_created
assert_eq 10 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_FINANCIAL_REPORT_CENTER','RUN_FINANCIAL_REPORTS','EXPORT_FINANCIAL_REPORTS','VIEW_FINANCIAL_OPERATIONS_WORKBENCH','MANAGE_FINANCIAL_OPERATIONS_RECOVERY','RETRY_FINANCIAL_SOURCES','VIEW_ACCOUNTING_RECONCILIATION_RECOVERY','VIEW_PROJECT_CLOSEOUT_RECOVERY','VIEW_CLOSEOUT_NOTIFICATION_RECOVERY','VIEW_BILLING_RECOVERY');")" permissions_created
assert_eq 6 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code IN ('FINANCIAL_REPORT_CENTER','FINANCIAL_OPERATIONS_WORKBENCH','BILLING_READINESS_RECOVERY','PROJECT_CLOSEOUT_RECOVERY','CLOSEOUT_NOTIFICATION_RECOVERY','BILLING_RECOVERY');")" features_created
assert_eq 10 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING' AND p.permission_code LIKE '%FINANCIAL%' OR r.role_code='ACCOUNTING' AND p.permission_code IN ('RETRY_FINANCIAL_SOURCES','VIEW_ACCOUNTING_RECONCILIATION_RECOVERY','VIEW_PROJECT_CLOSEOUT_RECOVERY','VIEW_CLOSEOUT_NOTIFICATION_RECOVERY','VIEW_BILLING_RECOVERY');")" accounting_full_permissions
assert_eq 6 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGEMENT';")" pm_scoped_permissions
assert_eq 6 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='EXECUTIVE';")" executive_read_permissions
assert_eq 2 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEERING';")" engineering_report_permissions
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE module_code='038' OR permission_code LIKE '%CERTIFY%';")" certify_permissions_unchanged

psql_exec <<'SQL'
INSERT INTO financial_report_runs (
  report_code,report_name,actual_user_id,effective_user_id,result_status,row_count
) VALUES (
  'project_financial_health','Project Financial Health',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001','complete',1
);
INSERT INTO financial_operations_work_items (
  deduplication_key,project_id,module_code,item_type,source_key,priority,title
) VALUES (
  'project:30000000-0000-0000-0000-000000000001:test',
  '30000000-0000-0000-0000-000000000001','039','billing_readiness','billing_readiness_reviews','high','Billing package is not ready'
);
INSERT INTO financial_operations_actions (
  financial_operations_work_item_id,project_id,source_key,action_code,action_status,actor_user_id
) SELECT financial_operations_work_item_id,
  '30000000-0000-0000-0000-000000000001','billing_readiness_reviews','source_retry','succeeded',
  '10000000-0000-0000-0000-000000000001'
FROM financial_operations_work_items LIMIT 1;
SQL

expect_sql_failure "UPDATE financial_operations_actions SET action_status='failed';" 'Financial operations action evidence is immutable.' immutable_action_update
expect_sql_failure "DELETE FROM financial_operations_actions;" 'Financial operations action evidence is immutable.' immutable_action_delete

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.financial_report_runs')::text,'');")" rollback_removed_report_runs
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.financial_operations_work_items')::text,'');")" rollback_removed_work_items
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.financial_operations_actions')::text,'');")" rollback_removed_actions
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='051_financial_operations_reporting_recovery';")" rollback_removed_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='VIEW_FINANCIAL_REPORT_CENTER';")" rollback_removed_permissions

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='051_financial_operations_reporting_recovery';")" safe_reapply

echo 'GROUP_5_MIGRATION_051=PASS'
