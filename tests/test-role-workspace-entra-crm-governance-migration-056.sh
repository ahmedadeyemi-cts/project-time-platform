#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-governance-056-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/056_role_workspace_entra_crm_governance.sql"
ROLLBACK="/workspace/database/rollback/056_role_workspace_entra_crm_governance_rollback.sql"

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
  local log="/tmp/projectpulse-056-${label}.log"
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

CREATE TABLE app_users (
  user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  login_enabled BOOLEAN NOT NULL DEFAULT TRUE
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

CREATE TABLE app_user_role_assignments (
  app_user_role_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
  assigned_by_user_id UUID NULL REFERENCES app_users(user_id),
  assignment_reason TEXT,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (user_id, app_role_id)
);

CREATE TABLE app_feature_catalog (
  app_feature_catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  feature_code VARCHAR(100) NOT NULL UNIQUE,
  feature_name VARCHAR(200) NOT NULL,
  module_code VARCHAR(75) NOT NULL,
  route_anchor VARCHAR(100),
  required_permission_code VARCHAR(100) NULL REFERENCES app_permissions(permission_code),
  feature_description TEXT,
  display_order INTEGER NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE crm_integration_providers (
  provider_key VARCHAR(60) PRIMARY KEY,
  provider_name VARCHAR(200) NOT NULL,
  auth_model VARCHAR(40) NOT NULL DEFAULT 'oauth2',
  is_enabled BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE crm_integration_credentials (
  provider_key VARCHAR(60) NOT NULL REFERENCES crm_integration_providers(provider_key) ON DELETE CASCADE,
  credential_kind VARCHAR(80) NOT NULL,
  encrypted_value TEXT NOT NULL,
  expires_at TIMESTAMPTZ NULL,
  rotated_by UUID NOT NULL REFERENCES app_users(user_id),
  rotated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (provider_key, credential_kind)
);

CREATE TABLE project_notification_dispatches (
  project_notification_dispatch_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  event_key VARCHAR(260) NOT NULL UNIQUE,
  delivery_status VARCHAR(40) NOT NULL DEFAULT 'held'
);

CREATE TABLE project_notification_dispatch_recipients (
  project_notification_dispatch_recipient_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_notification_dispatch_id UUID NOT NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE CASCADE,
  recipient_email TEXT NOT NULL
);

INSERT INTO app_users (user_id, email, display_name) VALUES
  ('10000000-0000-0000-0000-000000000001', 'admin@example.test', 'Admin Test'),
  ('10000000-0000-0000-0000-000000000002', 'ptc@example.test', 'PTC Test'),
  ('10000000-0000-0000-0000-000000000003', 'pm@example.test', 'PM Test'),
  ('10000000-0000-0000-0000-000000000004', 'accounting@example.test', 'Accounting Test'),
  ('10000000-0000-0000-0000-000000000005', 'billing@example.test', 'Billing Test'),
  ('10000000-0000-0000-0000-000000000006', 'sales@example.test', 'Sales Test');

INSERT INTO app_roles (role_code, role_name) VALUES
  ('SUPER_ADMINISTRATOR', 'Super Administrator'),
  ('ADMINISTRATOR', 'Administrator'),
  ('INTEGRATION_ADMINISTRATOR', 'Integration Administrator'),
  ('PROJECT_TEAM_COORDINATOR', 'Project Team Coordinator'),
  ('PROJECT_MANAGER', 'Project Manager'),
  ('PROJECT_MANAGEMENT', 'Project Management'),
  ('PROJECT_MANAGEMENT_LEAD', 'Project Management Lead'),
  ('PROJECT_MANAGEMENT_TEAM_LEAD', 'Project Management Team Lead'),
  ('PM_TEAM_LEAD', 'PM Team Lead'),
  ('ACCOUNTING', 'Accounting'),
  ('ACCOUNTING_BILLING', 'Accounting Billing'),
  ('BILLING', 'Billing'),
  ('FINANCE', 'Finance'),
  ('SALES', 'Sales'),
  ('INSIDE_SALES', 'Inside Sales'),
  ('RESALE', 'Resale'),
  ('ACCOUNT_EXECUTIVE', 'Account Executive'),
  ('ACCOUNT_EXECUTIVES', 'Account Executives'),
  ('SALES_MANAGER', 'Sales Manager');

INSERT INTO app_permissions (permission_code, permission_name, module_code) VALUES
  ('MANAGE_ALL', 'Manage all', 'admin'),
  ('VIEW_TIME_ENTRY', 'View time entry', 'time'),
  ('EDIT_OWN_TIME', 'Edit own time', 'time'),
  ('SUBMIT_OWN_TIME', 'Submit own time', 'time'),
  ('VIEW_APPROVAL_INBOX', 'View approval inbox', 'approval'),
  ('APPROVE_TIME', 'Approve time', 'approval'),
  ('REJECT_TIME', 'Reject time', 'approval'),
  ('PROJECT_TIME_APPROVAL', 'Project approval', 'projects'),
  ('VIEW_OWN_UTILIZATION', 'View utilization', 'utilization'),
  ('VIEW_PROJECT_WORKSPACE', 'View project workspace', '019'),
  ('VIEW_PROJECT_WORKLOAD', 'View project workload', '018'),
  ('VIEW_PROJECT_INTAKE', 'View project intake', '020'),
  ('MANAGE_PROJECT_INTAKE', 'Manage project intake', '020'),
  ('VIEW_RESOURCE_SCHEDULING', 'View resource scheduling', 'resources'),
  ('MANAGE_RESOURCE_SCHEDULING', 'Manage resource scheduling', 'resources'),
  ('VIEW_CUSTOMERS', 'View customers', '021'),
  ('VIEW_REPORTS', 'View reports', '030'),
  ('VIEW_ACCOUNT_RECONCILIATION', 'View reconciliation', '007'),
  ('MANAGE_ACCOUNT_RECONCILIATION', 'Manage reconciliation', '007'),
  ('VIEW_AUDIT_TRAIL', 'View audit', '008'),
  ('VIEW_BILLING_READINESS', 'View billing readiness', '039'),
  ('VIEW_OPPORTUNITIES', 'View opportunities', '063'),
  ('VIEW_INTEGRATIONS_026', 'View CRM integrations', '026'),
  ('MANAGE_INTEGRATIONS_026', 'Manage CRM integrations', '026');

INSERT INTO app_feature_catalog (
  feature_code, feature_name, module_code, route_anchor,
  required_permission_code, feature_description, display_order, is_active
) VALUES
  ('PROJECT_WORKLOAD', 'Project Workload', '018', '#project-workload', 'VIEW_PROJECT_WORKLOAD', '', 1, TRUE),
  ('PROJECT_WORKSPACE', 'Project Workspace', '019', '#project-workspace', 'VIEW_PROJECT_WORKSPACE', '', 2, TRUE),
  ('PROJECT_INTAKE', 'Project Intake', '020', '#project-intake', 'VIEW_PROJECT_INTAKE', '', 3, TRUE),
  ('CUSTOMERS', 'Customers', '021', '#customer-directory', 'VIEW_CUSTOMERS', '', 4, TRUE),
  ('REPORTS', 'Reports', '030', '#reporting', 'VIEW_REPORTS', '', 5, TRUE),
  ('BILLING_READINESS', 'Billing Readiness', '039', '#billing-readiness', 'VIEW_BILLING_READINESS', '', 6, TRUE),
  ('OPPORTUNITIES', 'Opportunities', '063', '#opportunities', 'VIEW_OPPORTUNITIES', '', 7, TRUE),
  ('CRM_INTEGRATIONS', 'CRM Integrations', '026', '#crm-integration', 'VIEW_INTEGRATIONS_026', '', 8, TRUE);

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'MANAGE_ALL'
WHERE role.role_code = 'SUPER_ADMINISTRATOR';

-- Seed access that migration 056 must remove from non-engineering business roles.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
  'VIEW_TIME_ENTRY', 'VIEW_APPROVAL_INBOX', 'VIEW_OWN_UTILIZATION', 'VIEW_PROJECT_WORKSPACE'
)
WHERE role.role_code IN ('ACCOUNTING', 'BILLING', 'SALES', 'INSIDE_SALES')
ON CONFLICT DO NOTHING;

INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason)
SELECT '10000000-0000-0000-0000-000000000002', app_role_id, 'Migration 056 test PTC'
FROM app_roles WHERE role_code = 'PROJECT_TEAM_COORDINATOR';

INSERT INTO crm_integration_providers (provider_key, provider_name)
VALUES ('salesforce', 'Salesforce');
SQL

apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }
apply_migration
apply_migration

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='056_role_workspace_entra_crm_governance';")" migration_registered_once
assert_eq 9 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('entra_secret_expiration_profile_versions','entra_secret_expiration_state','entra_secret_expiration_recipients','entra_secret_expiration_acknowledgements','entra_secret_expiration_reminder_claims','entra_secret_expiration_reminder_events','entra_secret_expiration_audit_events','crm_integration_token_refresh_events','role_workspace_permission_changes_056');")" governance_tables_created
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_ENTRA_SECRET_EXPIRATION','MANAGE_ENTRA_SECRET_EXPIRATION','ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION');")" expiration_permissions_created
assert_eq 2 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code IN ('ENTRA_SECRET_EXPIRATION_GOVERNANCE','CRM_ERP_OAUTH_PERSISTENCE');")" feature_catalog_registered

assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='APPROVE_TIME';")" project_manager_approval_granted
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='VIEW_PROJECT_WORKLOAD';")" project_manager_workspace_granted
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING' AND p.permission_code='VIEW_BILLING_READINESS';")" accounting_billing_workspace_granted
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING' AND p.permission_code IN ('VIEW_TIME_ENTRY','VIEW_APPROVAL_INBOX','VIEW_OWN_UTILIZATION','VIEW_PROJECT_WORKSPACE');")" accounting_engineering_access_removed
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SALES' AND p.permission_code='VIEW_INTEGRATIONS_026';")" sales_crm_status_granted
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SALES' AND p.permission_code IN ('VIEW_TIME_ENTRY','VIEW_APPROVAL_INBOX','VIEW_OWN_UTILIZATION','VIEW_PROJECT_WORKSPACE');")" sales_engineering_access_removed
assert_eq 2 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_TEAM_COORDINATOR' AND p.permission_code IN ('VIEW_ENTRA_SECRET_EXPIRATION','ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION');")" ptc_acknowledgement_access
assert_eq "$(value "SELECT COUNT(*) FROM app_permissions;")" "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) WHERE r.role_code='SUPER_ADMINISTRATOR';")" super_administrator_full_control

psql_exec <<'SQL'
INSERT INTO entra_secret_expiration_profile_versions (
  profile_id, generation, application_name, environment_name, secret_label,
  secret_version, expires_at, change_reason, created_by_user_id
) VALUES (
  '20000000-0000-0000-0000-000000000001', 1,
  'ProjectPulse Microsoft Integration', 'test', 'Microsoft Entra application client secret',
  'test-version-1', NOW() + INTERVAL '20 days', 'Migration test profile',
  '10000000-0000-0000-0000-000000000001'
);

INSERT INTO entra_secret_expiration_state (
  singleton_key, active_profile_id, updated_by_user_id
) VALUES (
  TRUE,
  '20000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001'
);

INSERT INTO entra_secret_expiration_recipients (
  profile_id, user_id, display_name, email, role_code
) VALUES (
  '20000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000002',
  'PTC Test', 'ptc@example.test', 'PROJECT_TEAM_COORDINATOR'
);

INSERT INTO entra_secret_expiration_acknowledgements (
  profile_id, user_id, acknowledged_by_actual_user_id, acknowledgement_statement
) VALUES (
  '20000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000002',
  '10000000-0000-0000-0000-000000000002',
  'Migration test acknowledgement'
);

INSERT INTO crm_integration_token_refresh_events (
  provider_key, refresh_trigger, refresh_status, diagnostic_code, event_metadata
) VALUES (
  'salesforce', 'background', 'oauth_token_refreshed', '',
  '{"accessTokenReturned":false,"refreshTokenReturned":false,"clientSecretReturned":false}'::jsonb
);
SQL

expect_sql_failure "UPDATE entra_secret_expiration_profile_versions SET secret_version='mutated' WHERE profile_id='20000000-0000-0000-0000-000000000001';" 'immutable' expiration_profile_immutable
expect_sql_failure "DELETE FROM entra_secret_expiration_acknowledgements WHERE profile_id='20000000-0000-0000-0000-000000000001';" 'immutable' acknowledgement_immutable
expect_sql_failure "UPDATE crm_integration_token_refresh_events SET refresh_status='mutated';" 'immutable' oauth_refresh_evidence_immutable

psql_exec -f "$ROLLBACK" >/dev/null

assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.entra_secret_expiration_profile_versions')::text,'');")" rollback_removed_expiration_tables
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.crm_integration_token_refresh_events')::text,'');")" rollback_removed_oauth_evidence
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='056_role_workspace_entra_crm_governance';")" rollback_removed_migration_row
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('VIEW_ENTRA_SECRET_EXPIRATION','MANAGE_ENTRA_SECRET_EXPIRATION','ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION');")" rollback_removed_permissions
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='ACCOUNTING' AND p.permission_code='VIEW_TIME_ENTRY';")" rollback_restored_removed_access
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='APPROVE_TIME';")" rollback_removed_migration_grant

echo 'ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_MIGRATION_056=PASS'
