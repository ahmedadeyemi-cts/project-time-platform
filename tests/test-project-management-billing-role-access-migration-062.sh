#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-role-repair-062-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/062_project_management_billing_role_access_repair.sql"
ROLLBACK="/workspace/database/rollback/062_project_management_billing_role_access_repair_rollback.sql"

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
  local log="/tmp/projectpulse-062-${label}.log"
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
  role_description TEXT,
  is_system_role BOOLEAN NOT NULL DEFAULT TRUE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  display_order INTEGER NOT NULL DEFAULT 0,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE app_permissions (
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code VARCHAR(100) NOT NULL UNIQUE,
  permission_name VARCHAR(200) NOT NULL,
  module_code VARCHAR(75) NOT NULL,
  permission_description TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE app_role_permissions (
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
  app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (app_role_id, app_permission_id)
);

CREATE TABLE projectpulse_role_scope_rules (
  role_code TEXT PRIMARY KEY,
  default_scope TEXT NOT NULL,
  can_view_assigned_self BOOLEAN NOT NULL DEFAULT FALSE,
  can_view_managed_projects BOOLEAN NOT NULL DEFAULT FALSE,
  can_view_team_scope BOOLEAN NOT NULL DEFAULT FALSE,
  can_view_org_scope BOOLEAN NOT NULL DEFAULT FALSE,
  can_approve_time BOOLEAN NOT NULL DEFAULT FALSE,
  can_approve_password_reset BOOLEAN NOT NULL DEFAULT FALSE,
  can_coordinate_billing_expense BOOLEAN NOT NULL DEFAULT FALSE,
  can_manage_role_assignments_limited BOOLEAN NOT NULL DEFAULT FALSE,
  notes TEXT NOT NULL DEFAULT '',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO app_roles (role_code, role_name, display_order) VALUES
  ('PROJECT_MANAGER', 'Project Manager', 10),
  ('PROJECT_MANAGEMENT', 'Project Management', 20),
  ('PROJECT_MANAGEMENT_LEAD', 'Project Management Lead', 30),
  ('PROJECT_MANAGEMENT_TEAM_LEAD', 'Project Management Team Lead', 31),
  ('PM_TEAM_LEAD', 'PM Team Lead', 32),
  ('BILLING', 'Billing', 40),
  ('ACCOUNTING_BILLING', 'Accounting Billing', 50),
  ('FINANCE', 'Finance', 60),
  ('ACCOUNTING', 'Accounting', 70);

INSERT INTO app_permissions (permission_code, permission_name, module_code) VALUES
  ('VIEW_TIME_ENTRY', 'View time entry', '001'),
  ('EDIT_OWN_TIME', 'Edit own time', '001'),
  ('SUBMIT_OWN_TIME', 'Submit own time', '001'),
  ('VIEW_APPROVAL_INBOX', 'View approval inbox', '002'),
  ('APPROVE_TIME', 'Approve time', '002'),
  ('REJECT_TIME', 'Reject time', '002'),
  ('PROJECT_TIME_APPROVAL', 'Project time approval', '002'),
  ('VIEW_HOLIDAYS', 'View holidays', '004'),
  ('VIEW_CALENDAR', 'View calendar', '004'),
  ('VIEW_EXPENSES', 'View expenses', '005'),
  ('MANAGE_EXPENSES', 'Manage expenses', '005'),
  ('VIEW_PROJECT_WORKSPACE', 'View project workspace', '019'),
  ('VIEW_REPORTS', 'View reports', '030'),
  ('VIEW_AUDIT_TRAIL', 'View audit trail', '008');

-- Preserve one pre-existing PM grant to verify rollback does not remove it.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'VIEW_TIME_ENTRY'
WHERE role.role_code = 'PROJECT_MANAGEMENT';

-- Billing-family and Accounting begin with Audit History access.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'VIEW_AUDIT_TRAIL'
WHERE role.role_code IN ('BILLING','ACCOUNTING_BILLING','FINANCE','ACCOUNTING');

INSERT INTO projectpulse_role_scope_rules (
  role_code, default_scope, can_view_assigned_self,
  can_view_managed_projects, can_approve_time, notes
) VALUES
  ('PROJECT_MANAGEMENT', 'managed_projects', FALSE, TRUE, FALSE, 'original PM scope'),
  ('PROJECT_MANAGEMENT_LEAD', 'managed_team', FALSE, TRUE, FALSE, 'original PM lead scope');
SQL

apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }
apply_migration
apply_migration

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='062_project_management_billing_role_access_repair';")" migration_registered_once
assert_eq 2 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_QUALIFICATIONS_069','MANAGE_OWN_QUALIFICATIONS_069');")" qualification_permissions_created
assert_eq 75 "$(value "SELECT COUNT(*) FROM app_role_permissions relationship JOIN app_roles role ON role.app_role_id=relationship.app_role_id JOIN app_permissions permission ON permission.app_permission_id=relationship.app_permission_id WHERE role.role_code IN ('PROJECT_MANAGER','PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD') AND permission.permission_code IN ('VIEW_TIME_ENTRY','EDIT_OWN_TIME','SUBMIT_OWN_TIME','VIEW_APPROVAL_INBOX','APPROVE_TIME','REJECT_TIME','PROJECT_TIME_APPROVAL','VIEW_HOLIDAYS','VIEW_CALENDAR','VIEW_EXPENSES','MANAGE_EXPENSES','VIEW_PROJECT_WORKSPACE','VIEW_REPORTS','VIEW_QUALIFICATIONS_069','MANAGE_OWN_QUALIFICATIONS_069');")" pm_required_permissions_complete
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_role_permissions relationship JOIN app_roles role ON role.app_role_id=relationship.app_role_id JOIN app_permissions permission ON permission.app_permission_id=relationship.app_permission_id WHERE role.role_code IN ('BILLING','ACCOUNTING_BILLING','FINANCE') AND permission.permission_code='VIEW_AUDIT_TRAIL';")" billing_audit_removed
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_role_permissions relationship JOIN app_roles role ON role.app_role_id=relationship.app_role_id JOIN app_permissions permission ON permission.app_permission_id=relationship.app_permission_id WHERE role.role_code='ACCOUNTING' AND permission.permission_code='VIEW_AUDIT_TRAIL';")" accounting_audit_preserved
assert_eq 2 "$(value "SELECT COUNT(*) FROM projectpulse_role_scope_rules WHERE role_code IN ('PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD') AND can_view_assigned_self=TRUE AND can_approve_time=TRUE;")" pm_scope_corrected

expect_sql_failure \
  "DELETE FROM role_access_repair_062_permission_grants;" \
  "migration 062 evidence is immutable" \
  grant_evidence_immutable

psql_exec -f "$ROLLBACK" >/dev/null

assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='062_project_management_billing_role_access_repair';")" migration_removed_on_rollback
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_role_permissions relationship JOIN app_roles role ON role.app_role_id=relationship.app_role_id JOIN app_permissions permission ON permission.app_permission_id=relationship.app_permission_id WHERE role.role_code IN ('BILLING','ACCOUNTING_BILLING','FINANCE') AND permission.permission_code='VIEW_AUDIT_TRAIL';")" billing_audit_restored
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_role_permissions relationship JOIN app_roles role ON role.app_role_id=relationship.app_role_id JOIN app_permissions permission ON permission.app_permission_id=relationship.app_permission_id WHERE role.role_code='PROJECT_MANAGEMENT' AND permission.permission_code='VIEW_TIME_ENTRY';")" preexisting_pm_grant_preserved
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_role_permissions relationship JOIN app_roles role ON role.app_role_id=relationship.app_role_id JOIN app_permissions permission ON permission.app_permission_id=relationship.app_permission_id WHERE role.role_code='PROJECT_MANAGEMENT' AND permission.permission_code='MANAGE_OWN_QUALIFICATIONS_069';")" migration_pm_grant_removed
assert_eq 'false|false|original PM scope' "$(value "SELECT can_view_assigned_self::text || '|' || can_approve_time::text || '|' || notes FROM projectpulse_role_scope_rules WHERE role_code='PROJECT_MANAGEMENT';")" pm_scope_restored

apply_migration
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='062_project_management_billing_role_access_repair';")" migration_reapplied
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_role_permissions relationship JOIN app_roles role ON role.app_role_id=relationship.app_role_id JOIN app_permissions permission ON permission.app_permission_id=relationship.app_permission_id WHERE role.role_code IN ('BILLING','ACCOUNTING_BILLING','FINANCE') AND permission.permission_code='VIEW_AUDIT_TRAIL';")" billing_audit_removed_after_reapply

echo 'PROJECT_MANAGEMENT_BILLING_ROLE_ACCESS_MIGRATION_062=PASS'
