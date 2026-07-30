#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-governance-056a-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/056a_role_workspace_permission_scope_guard.sql"
ROLLBACK="/workspace/database/rollback/056a_role_workspace_permission_scope_guard_rollback.sql"

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
  local log="/tmp/projectpulse-056a-${label}.log"
  if psql_exec -c "$sql" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fqi "$expected" "$log" || {
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

CREATE TABLE app_roles (
  app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  role_code VARCHAR(75) NOT NULL UNIQUE,
  role_name VARCHAR(150) NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE app_permissions (
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code VARCHAR(100) NOT NULL UNIQUE,
  permission_name VARCHAR(200) NOT NULL,
  module_code VARCHAR(75) NOT NULL
);

CREATE TABLE app_role_permissions (
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
  app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
  UNIQUE (app_role_id, app_permission_id)
);

CREATE TABLE role_workspace_permission_changes_056 (
  role_code VARCHAR(75) NOT NULL,
  permission_code VARCHAR(100) NOT NULL,
  change_kind VARCHAR(20) NOT NULL CHECK (change_kind IN ('granted', 'removed')),
  recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (role_code, permission_code, change_kind)
);

INSERT INTO schema_migrations (migration_id, description)
VALUES ('056_role_workspace_entra_crm_governance', 'Test migration 056 baseline');

INSERT INTO app_roles (role_code, role_name) VALUES
  ('PROJECT_MANAGER', 'Project Manager'),
  ('ACCOUNTING', 'Accounting'),
  ('BILLING', 'Billing'),
  ('SALES', 'Sales'),
  ('INSIDE_SALES', 'Inside Sales');

INSERT INTO app_permissions (permission_code, permission_name, module_code) VALUES
  ('VIEW_PROJECT_WORKLOAD', 'View project workload', '018'),
  ('APPROVE_TIME', 'Approve time', '002'),
  ('VIEW_BILLING_READINESS', 'View billing readiness', '039'),
  ('MANAGE_ACCOUNT_RECONCILIATION', 'Manage account reconciliation', '007'),
  ('VIEW_INTEGRATIONS_026', 'View integrations', '026'),
  ('MANAGE_INTEGRATIONS_026', 'Manage integrations', '026'),
  ('SYSTEM_ADMINISTRATION', 'System administration', 'admin'),
  ('MANAGE_CUSTOMERS', 'Manage customers', '021');

-- Simulate the dynamic grants inserted and tracked by migration 056.
INSERT INTO role_workspace_permission_changes_056 (role_code, permission_code, change_kind) VALUES
  ('PROJECT_MANAGER', 'VIEW_PROJECT_WORKLOAD', 'granted'),
  ('PROJECT_MANAGER', 'APPROVE_TIME', 'granted'),
  ('PROJECT_MANAGER', 'MANAGE_INTEGRATIONS_026', 'granted'),
  ('ACCOUNTING', 'VIEW_BILLING_READINESS', 'granted'),
  ('ACCOUNTING', 'MANAGE_ACCOUNT_RECONCILIATION', 'granted'),
  ('ACCOUNTING', 'SYSTEM_ADMINISTRATION', 'granted'),
  ('BILLING', 'VIEW_BILLING_READINESS', 'granted'),
  ('BILLING', 'MANAGE_INTEGRATIONS_026', 'granted'),
  ('SALES', 'VIEW_INTEGRATIONS_026', 'granted'),
  ('SALES', 'MANAGE_INTEGRATIONS_026', 'granted'),
  ('INSIDE_SALES', 'VIEW_INTEGRATIONS_026', 'granted'),
  ('INSIDE_SALES', 'MANAGE_INTEGRATIONS_026', 'granted');

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM role_workspace_permission_changes_056 change
JOIN app_roles role ON role.role_code = change.role_code
JOIN app_permissions permission ON permission.permission_code = change.permission_code
WHERE change.change_kind = 'granted';

-- Pre-existing untracked authority must not be removed by 056A.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'MANAGE_CUSTOMERS'
WHERE role.role_code = 'SALES';
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='056a_role_workspace_permission_scope_guard';")" migration_registered_once
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename='role_workspace_permission_scope_changes_056a';")" scope_evidence_created
assert_eq 5 "$(value "SELECT COUNT(*) FROM role_workspace_permission_scope_changes_056a;")" unsafe_new_grants_recorded

assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='VIEW_PROJECT_WORKLOAD';")" pm_view_preserved
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='APPROVE_TIME';")" pm_approval_preserved
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='MANAGE_INTEGRATIONS_026';")" pm_unrelated_integration_management_removed
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING' AND p.permission_code='MANAGE_ACCOUNT_RECONCILIATION';")" accounting_reconciliation_preserved
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING' AND p.permission_code='SYSTEM_ADMINISTRATION';")" accounting_system_admin_removed
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SALES' AND p.permission_code='VIEW_INTEGRATIONS_026';")" sales_integration_view_preserved
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code IN ('SALES','INSIDE_SALES','BILLING') AND p.permission_code='MANAGE_INTEGRATIONS_026';")" business_integration_management_removed
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SALES' AND p.permission_code='MANAGE_CUSTOMERS';")" untracked_preexisting_permission_preserved

expect_sql_failure "DELETE FROM role_workspace_permission_scope_changes_056a;" 'immutable' scope_evidence_immutable

psql_exec -f "$ROLLBACK" >/dev/null

assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.role_workspace_permission_scope_changes_056a')::text,'');")" rollback_removed_scope_evidence
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='056a_role_workspace_permission_scope_guard';")" rollback_removed_migration_row
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SALES' AND p.permission_code='MANAGE_INTEGRATIONS_026';")" rollback_restored_removed_056_grant
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING' AND p.permission_code='SYSTEM_ADMINISTRATION';")" rollback_restored_accounting_056_grant

echo 'ROLE_WORKSPACE_PERMISSION_SCOPE_MIGRATION_056A=PASS'
