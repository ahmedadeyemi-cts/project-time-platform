#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-expense-044-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION_044="/workspace/database/migrations/044_project_expense_upload_certify_connection.sql"
MIGRATION_044A="/workspace/database/migrations/044a_project_expense_self_certify_permission.sql"
ROLLBACK_044A="/workspace/database/rollback/044a_project_expense_self_certify_permission_rollback.sql"
ROLLBACK_044="/workspace/database/rollback/044_project_expense_upload_certify_connection_rollback.sql"

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
expect_file_failure() {
  local file="$1"
  local expected="$2"
  local label="$3"
  local log="/tmp/project-expense-044-${label}.log"
  if psql_exec -f "$file" >"$log" 2>&1; then
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
expect_sql_failure() {
  local sql="$1"
  local expected="$2"
  local label="$3"
  local log="/tmp/project-expense-044-${label}.log"
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
  contract_type TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'active',
  project_manager_user_id UUID NULL REFERENCES app_users(user_id)
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
CREATE TABLE scoped_role_policy_modules (
  module_code TEXT PRIMARY KEY,
  module_name TEXT NOT NULL,
  current_state TEXT NOT NULL DEFAULT '',
  permission_notes TEXT NOT NULL DEFAULT ''
);

INSERT INTO app_users (user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','engineer@example.test','Engineer Test'),
 ('10000000-0000-0000-0000-000000000002','pm@example.test','PM Test'),
 ('10000000-0000-0000-0000-000000000003','admin@example.test','Admin Test'),
 ('10000000-0000-0000-0000-000000000004','accounting@example.test','Accounting Test');
INSERT INTO clients (client_id,client_name)
VALUES ('20000000-0000-0000-0000-000000000001','Customer Test');
INSERT INTO projects (
  project_id,client_id,project_code,project_name,contract_type,project_manager_user_id
) VALUES (
  '30000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',
  'P-005','Expense Test Project','Time and Materials',
  '10000000-0000-0000-0000-000000000002'
);
INSERT INTO app_roles (role_code,role_name) VALUES
 ('ENGINEERING','Engineering'),
 ('ENGINEERING_LEAD','Engineering Lead'),
 ('PROJECT_MANAGEMENT','Project Management'),
 ('PROJECT_MANAGEMENT_LEAD','Project Management Lead'),
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('ACCOUNTING','Accounting');
INSERT INTO app_permissions (
  permission_code,permission_name,module_code,permission_description
) VALUES (
  'VIEW_PROJECT_ALLOCATION_INFO','View Project Allocation and Info','005','Legacy prerequisite'
);
INSERT INTO app_feature_catalog (
  feature_code,feature_name,module_code,route_anchor,required_permission_code,feature_description,display_order
) VALUES (
  'PROJECT_ALLOCATION_INFO','Project Allocation and Info','projects','#project-allocation-info',
  'VIEW_PROJECT_ALLOCATION_INFO','Legacy project allocation information',75
);
INSERT INTO scoped_role_policy_modules (module_code,module_name,current_state,permission_notes) VALUES
 ('005','Project Allocation and Info','Installed legacy behavior',''),
 ('038','Certify Integration Center','Installed','');
SQL

apply_all() {
  psql_exec -f "$MIGRATION_044" >/dev/null
  psql_exec -f "$MIGRATION_044A" >/dev/null
}

apply_all
apply_all
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='044_project_expense_upload_certify_connection';")" migration_044_registered_once
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='044a_project_expense_self_certify_permission';")" migration_044a_registered_once
assert_eq 6 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('project_expense_uploads','project_expense_lines','project_expense_events','project_expense_mail_outbox','certify_connection_profiles','certify_expense_import_runs');")" expense_tables_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_views WHERE schemaname='public' AND viewname='project_expense_current_summary';")" current_summary_view_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM certify_connection_profiles WHERE profile_name='default' AND base_url='https://api.certify.com/v1/' AND automatic_sync_enabled=FALSE;")" certify_default_profile
assert_eq 7 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_PROJECT_EXPENSE_UPLOAD','UPLOAD_PROJECT_EXPENSE_SELF','UPLOAD_PROJECT_EXPENSE_ON_BEHALF','DELETE_PROJECT_EXPENSE_UPLOAD','IMPORT_PROJECT_EXPENSE_CERTIFY','VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT','MANAGE_CERTIFY_CONNECTION');")" permissions_created
assert_eq 'Project Expense Upload' "$(value "SELECT module_name FROM scoped_role_policy_modules WHERE module_code='005';")" module_005_renamed
assert_eq 'Certify Connection & Sync Center' "$(value "SELECT module_name FROM scoped_role_policy_modules WHERE module_code='038';")" module_038_renamed
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='PROJECT_EXPENSE_UPLOAD' AND required_permission_code='VIEW_PROJECT_EXPENSE_UPLOAD';")" expense_feature_registered
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEERING' AND p.permission_code='IMPORT_PROJECT_EXPENSE_CERTIFY';")" engineer_certify_import_granted
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGEMENT' AND p.permission_code='UPLOAD_PROJECT_EXPENSE_ON_BEHALF';")" pm_on_behalf_granted
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING' AND p.permission_code='MANAGE_CERTIFY_CONNECTION';")" accounting_certify_management_granted

psql_exec -f "$ROLLBACK_044A" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ENGINEERING' AND p.permission_code='IMPORT_PROJECT_EXPENSE_CERTIFY';")" engineer_certify_grant_removed_by_044a_rollback
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGEMENT' AND p.permission_code='IMPORT_PROJECT_EXPENSE_CERTIFY';")" pm_certify_grant_preserved_until_044_rollback
psql_exec -f "$ROLLBACK_044" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.project_expense_uploads')::text,'');")" safe_rollback_removed_upload_table
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id IN ('044_project_expense_upload_certify_connection','044a_project_expense_self_certify_permission');")" safe_rollback_removed_migration_rows
assert_eq 'Project Allocation and Info' "$(value "SELECT module_name FROM scoped_role_policy_modules WHERE module_code='005';")" safe_rollback_restored_module_005
assert_eq 'Certify Integration Center' "$(value "SELECT module_name FROM scoped_role_policy_modules WHERE module_code='038';")" safe_rollback_restored_module_038

apply_all
psql_exec <<'SQL'
INSERT INTO certify_expense_import_runs (
  certify_expense_import_run_id,certify_connection_profile_id,project_id,
  expense_owner_user_id,initiated_by_user_id,import_status,certify_report_id
) SELECT
  '40000000-0000-0000-0000-000000000001',certify_connection_profile_id,
  '30000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000002','started','CERTIFY-TEST'
FROM certify_connection_profiles WHERE profile_name='default';
SQL
expect_file_failure "$ROLLBACK_044" 'Rollback 044 is blocked because Certify import audit records exist.' certify_import_blocks_rollback
psql_exec -c "DELETE FROM certify_expense_import_runs;" >/dev/null

psql_exec <<'SQL'
INSERT INTO project_expense_uploads (
  project_expense_upload_id,project_id,customer_name,project_code,project_name,
  expense_owner_user_id,uploaded_by_user_id,source_mode,source_format,source_sha256,
  total_amount,reimbursable_amount,contract_type_snapshot,billing_treatment,version_number
) VALUES (
  '50000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001','Customer Test','P-005','Expense Test Project',
  '10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002',
  'excel_csv','category_summary','test-sha',2377.26,2377.26,'Time and Materials','pass_through_invoice',1
);
INSERT INTO project_expense_lines (
  project_expense_upload_id,line_number,employee_name,expense_category,amount,reimbursable_amount
) VALUES (
  '50000000-0000-0000-0000-000000000001',1,'Engineer Test','SP-Cust Pass Through - Airfare',500,500
);
INSERT INTO project_expense_events (
  project_expense_upload_id,project_id,event_code,actor_user_id,target_user_id,reason
) VALUES (
  '50000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001','UPLOAD_CREATED',
  '10000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001','test'
);
SQL
expect_sql_failure "UPDATE project_expense_events SET reason='changed';" 'Project expense audit events are immutable.' expense_event_is_immutable
expect_file_failure "$ROLLBACK_044" 'Rollback 044 is blocked because project expense upload records exist.' expense_upload_blocks_rollback

assert_eq 2377.26 "$(value "SELECT total_amount FROM project_expense_current_summary WHERE project_expense_upload_id='50000000-0000-0000-0000-000000000001';")" current_summary_total
assert_eq pass_through_invoice "$(value "SELECT billing_treatment FROM project_expense_current_summary WHERE project_expense_upload_id='50000000-0000-0000-0000-000000000001';")" current_summary_billing_treatment

echo 'PROJECT_EXPENSE_MIGRATION_044_TEST=PASS apply=true idempotent=true safeRollback=true guardedRollback=true immutableAudit=true'
