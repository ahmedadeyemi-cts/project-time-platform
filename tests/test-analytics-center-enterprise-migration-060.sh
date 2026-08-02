#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-analytics-060-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/060_analytics_center_enterprise_experience.sql"
ROLLBACK="/workspace/database/rollback/060_analytics_center_enterprise_experience_rollback.sql"

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
  local log="/tmp/analytics-060-${label}.log"
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
  job_title TEXT NOT NULL DEFAULT '',
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
CREATE TABLE enterprise_report_runs (
  enterprise_report_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);
CREATE TABLE enterprise_report_exports (
  enterprise_report_export_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  enterprise_report_run_id UUID NOT NULL REFERENCES enterprise_report_runs(enterprise_report_run_id) ON DELETE RESTRICT,
  export_format VARCHAR(12) NOT NULL CHECK (export_format IN ('csv','xlsx','json')),
  row_count INTEGER NOT NULL DEFAULT 0,
  content_sha256 VARCHAR(64) NOT NULL DEFAULT '',
  exported_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO app_users(user_id,email,display_name,job_title) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@ussignal.com','Administrator','Administrator'),
 ('10000000-0000-0000-0000-000000000002','engineer@ussignal.com','Engineer','Engineer'),
 ('10000000-0000-0000-0000-000000000003','pm@ussignal.com','Project Manager','Project Manager');
INSERT INTO app_roles(role_code,role_name) VALUES
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('ENGINEERING','Engineering'),
 ('PROJECT_MANAGEMENT','Project Management'),
 ('PROJECT_TEAM_COORDINATOR','Project Team Coordinator'),
 ('ACCOUNTING','Accounting');
INSERT INTO app_permissions(permission_code,permission_name,module_code) VALUES
 ('VIEW_ENTERPRISE_REPORTING','View Analytics','030'),
 ('RUN_ENTERPRISE_REPORTING','Run Analytics','030');
INSERT INTO app_role_permissions(app_role_id,app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role CROSS JOIN app_permissions permission
WHERE role.role_code IN ('ENGINEERING','PROJECT_MANAGEMENT','ACCOUNTING');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='060_analytics_center_enterprise_experience';")" migration_registered_once
assert_eq 5 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('analytics_report_schedules','analytics_report_schedule_recipients','analytics_report_schedule_runs','analytics_report_schedule_delivery_attempts','analytics_user_report_activity');")" tables_created
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_ANALYTICS_DASHBOARDS','VIEW_ANALYTICS_SCHEDULES','MANAGE_ANALYTICS_SCHEDULES','DELIVER_ANALYTICS_SCHEDULES');")" permissions_created
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_roles role JOIN app_role_permissions assignment USING(app_role_id) JOIN app_permissions permission USING(app_permission_id) WHERE role.role_code='ENGINEERING' AND permission.permission_code IN ('VIEW_ANALYTICS_DASHBOARDS','VIEW_ANALYTICS_SCHEDULES','MANAGE_ANALYTICS_SCHEDULES');")" engineer_schedule_permissions

psql_exec <<'SQL'
INSERT INTO enterprise_report_runs(enterprise_report_run_id)
VALUES ('20000000-0000-0000-0000-000000000001');
INSERT INTO enterprise_report_exports(
  enterprise_report_run_id,export_format,row_count,content_sha256
) VALUES (
  '20000000-0000-0000-0000-000000000001','pdf',1,
  'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
);
INSERT INTO analytics_report_schedules (
  analytics_report_schedule_id,owner_actual_user_id,owner_effective_user_id,
  schedule_name,report_code,cadence,local_time,timezone_name,export_format,
  delivery_boundary,enabled,next_run_at
) VALUES (
  '30000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'Weekly Project Financial','project_financial_health','weekly','08:00','America/New_York','pdf',
  'test_only',TRUE,NOW()
);
INSERT INTO analytics_report_schedule_recipients (
  analytics_report_schedule_id,recipient_user_id,recipient_name,recipient_email
) VALUES (
  '30000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000002',
  'Engineer','engineer@ussignal.com'
);
INSERT INTO analytics_report_schedule_runs (
  analytics_report_schedule_run_id,analytics_report_schedule_id,schedule_name,
  report_code,owner_actual_user_id,started_at,completed_at,run_status,
  recipient_count,queued_count
) VALUES (
  '40000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  'Weekly Project Financial','project_financial_health',
  '10000000-0000-0000-0000-000000000001',NOW(),NOW(),'queued',1,1
);
INSERT INTO analytics_report_schedule_delivery_attempts (
  analytics_report_schedule_run_id,enterprise_report_run_id,recipient_user_id,
  recipient_email,export_format,content_sha256,delivery_status
) VALUES (
  '40000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000002',
  'engineer@ussignal.com','pdf',
  'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
  'queued'
);
SQL

assert_eq 1 "$(value "SELECT COUNT(*) FROM enterprise_report_exports WHERE export_format='pdf';")" pdf_export_allowed
assert_eq 1 "$(value "SELECT COUNT(*) FROM analytics_report_schedule_recipients;")" recipient_created
expect_sql_failure "UPDATE analytics_report_schedule_runs SET run_status='failed';" 'Analytics schedule-run and delivery evidence is immutable.' immutable_schedule_run
expect_sql_failure "DELETE FROM analytics_report_schedule_delivery_attempts;" 'Analytics schedule-run and delivery evidence is immutable.' immutable_delivery_attempt

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.analytics_report_schedules')::text,'');")" rollback_removed_schedules
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.analytics_report_schedule_runs')::text,'');")" rollback_removed_schedule_runs
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='060_analytics_center_enterprise_experience';")" rollback_removed_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='VIEW_ANALYTICS_DASHBOARDS';")" rollback_removed_permissions
expect_sql_failure "INSERT INTO enterprise_report_exports(enterprise_report_run_id,export_format) VALUES ('20000000-0000-0000-0000-000000000001','pdf');" 'violates check constraint' rollback_restored_export_constraint

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='060_analytics_center_enterprise_experience';")" safe_reapply

echo 'ANALYTICS_CENTER_ENTERPRISE_MIGRATION_060=PASS'
