#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-reporting-054-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/054_enterprise_reporting_center.sql"
ROLLBACK="/workspace/database/rollback/054_enterprise_reporting_center_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}
expect_sql_failure() {
  local sql="$1"
  local expected="$2"
  local label="$3"
  local log="/tmp/reporting-054-${label}.log"
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
 ('10000000-0000-0000-0000-000000000002','engineer@example.test','Engineer'),
 ('10000000-0000-0000-0000-000000000003','pm@example.test','Project Manager');
INSERT INTO app_roles(role_code,role_name) VALUES
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('PROJECT_TEAM_COORDINATOR','Project Team Coordinator'),
 ('ACCOUNTING','Accounting'),
 ('EXECUTIVE','Executive'),
 ('PROJECT_MANAGER','Project Manager'),
 ('ENGINEER','Engineer'),
 ('MANAGER','Manager'),
 ('SALES','Sales'),
 ('SOLUTION_ARCHITECT','Solution Architect');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_enterprise_reporting_center';")" migration_registered_once
assert_eq 3 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('enterprise_report_runs','enterprise_report_saved_views','enterprise_report_exports');")" reporting_tables_created
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_ENTERPRISE_REPORTING','RUN_ENTERPRISE_REPORTING','EXPORT_ENTERPRISE_REPORTING','MANAGE_ENTERPRISE_REPORTING');")" reporting_permissions_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='ENTERPRISE_REPORTING_CENTER';")" reporting_feature_created
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING';")" accounting_full_reporting_permissions
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEER';")" engineer_scoped_reporting_permissions
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER';")" pm_scoped_reporting_permissions

psql_exec <<'SQL'
INSERT INTO enterprise_report_runs (
  enterprise_report_run_id, report_code, report_name,
  actual_user_id, effective_user_id, result_status, row_count,
  result_json
) VALUES (
  '20000000-0000-0000-0000-000000000001',
  'project_portfolio', 'Project Portfolio',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'complete', 1, '[{"projectCode":"P-1"}]'::jsonb
);
INSERT INTO enterprise_report_exports (
  enterprise_report_run_id, actor_user_id, export_format,
  row_count, content_sha256
) VALUES (
  '20000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'xlsx', 1, repeat('a', 64)
);
INSERT INTO enterprise_report_saved_views (
  enterprise_report_saved_view_id, view_name, report_code,
  owner_user_id, filters_json, is_default
) VALUES (
  '30000000-0000-0000-0000-000000000001',
  'My projects', 'project_portfolio',
  '10000000-0000-0000-0000-000000000003',
  '{"projectManagerUserId":"10000000-0000-0000-0000-000000000003"}'::jsonb,
  TRUE
);
UPDATE enterprise_report_saved_views
SET view_name='My active projects', version=version+1
WHERE enterprise_report_saved_view_id='30000000-0000-0000-0000-000000000001';
SQL

assert_eq 2 "$(value "SELECT version FROM enterprise_report_saved_views WHERE enterprise_report_saved_view_id='30000000-0000-0000-0000-000000000001';")" saved_views_remain_editable
expect_sql_failure "UPDATE enterprise_report_runs SET row_count=2;" 'Enterprise report run and export evidence is immutable.' immutable_run_update
expect_sql_failure "DELETE FROM enterprise_report_exports;" 'Enterprise report run and export evidence is immutable.' immutable_export_delete

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.enterprise_report_runs')::text,'');")" rollback_removed_runs
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.enterprise_report_saved_views')::text,'');")" rollback_removed_saved_views
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.enterprise_report_exports')::text,'');")" rollback_removed_exports
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_enterprise_reporting_center';")" rollback_removed_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='VIEW_ENTERPRISE_REPORTING';")" rollback_removed_permissions

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_enterprise_reporting_center';")" safe_reapply

echo 'ENTERPRISE_REPORTING_MIGRATION_054=PASS'
