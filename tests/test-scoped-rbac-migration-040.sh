#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-rbac-040-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/040_scoped_role_policy_versions.sql"
ROLLBACK="/workspace/database/rollback/040_scoped_role_policy_versions_rollback.sql"

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

psql_exec() {
  docker exec -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}

value() {
  psql_exec -Atqc "$1" | tr -d '\r'
}

assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

assert_gt_zero() {
  local actual="$1" label="$2"
  [[ "$actual" =~ ^[0-9]+$ && "$actual" -gt 0 ]] || {
    echo "ASSERTION_FAILED $label expected=>0 actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

docker run --detach --rm \
  --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  if docker exec "$CONTAINER" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
    break
  fi
  [[ "$attempt" != 60 ]] || {
    echo "PostgreSQL did not become ready." >&2
    exit 1
  }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE schema_migrations (
    migration_id TEXT PRIMARY KEY,
    description TEXT NOT NULL DEFAULT '',
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO schema_migrations (migration_id, description)
VALUES ('039_work_to_cash_reactivation_lock_order', 'test prerequisite');

CREATE TABLE app_users (
    user_id UUID PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE app_roles (
    app_role_id UUID PRIMARY KEY,
    role_code TEXT NOT NULL UNIQUE,
    role_name TEXT NOT NULL,
    role_description TEXT NOT NULL DEFAULT '',
    display_order INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE app_permissions (
    app_permission_id UUID PRIMARY KEY,
    permission_code TEXT NOT NULL UNIQUE,
    permission_name TEXT NOT NULL,
    permission_description TEXT NOT NULL DEFAULT '',
    module_code TEXT NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE app_role_permissions (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id),
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    PRIMARY KEY (app_role_id, app_permission_id)
);

CREATE TABLE app_user_role_assignments (
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id),
    assigned_by_user_id UUID NULL REFERENCES app_users(user_id),
    assignment_reason TEXT NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, app_role_id)
);

INSERT INTO app_roles (
    app_role_id, role_code, role_name, role_description, display_order, is_active
)
VALUES
('10000000-0000-0000-0000-000000000001','ENGINEERING','Engineering','Engineering',10,TRUE),
('10000000-0000-0000-0000-000000000002','PROJECT_MANAGEMENT','Project Management','Project Management',20,TRUE),
('10000000-0000-0000-0000-000000000003','ENGINEERING_LEAD','Engineering Lead','Engineering Lead',30,TRUE),
('10000000-0000-0000-0000-000000000004','PROJECT_MANAGEMENT_LEAD','Project Management Lead','Project Management Lead',40,TRUE),
('10000000-0000-0000-0000-000000000005','MANAGER','Manager','Manager',50,TRUE),
('10000000-0000-0000-0000-000000000006','SALES','Sales','Sales',60,TRUE),
('10000000-0000-0000-0000-000000000007','INSIDE_SALES','Inside Sales','Inside Sales',70,TRUE),
('10000000-0000-0000-0000-000000000008','SOLUTION_ARCHITECT','Solution Architect','Solution Architect',80,TRUE),
('10000000-0000-0000-0000-000000000009','EXECUTIVE','Executive','Executive',90,TRUE),
('10000000-0000-0000-0000-000000000010','PROJECT_TEAM_COORDINATOR','Project Team Coordinator','Project Team Coordinator',100,TRUE),
('10000000-0000-0000-0000-000000000011','ACCOUNTING','Accounting','Accounting',110,TRUE),
('10000000-0000-0000-0000-000000000012','SUPER_ADMINISTRATOR','Super Administrator','Super Administrator',120,TRUE);

INSERT INTO app_users (user_id, email, display_name, is_active)
VALUES ('20000000-0000-0000-0000-000000000001','superadmin@example.test','Scoped RBAC Test Super Administrator',TRUE);

INSERT INTO app_user_role_assignments (
    user_id, app_role_id, assigned_by_user_id, assignment_reason, is_active
)
VALUES (
    '20000000-0000-0000-0000-000000000001',
    '10000000-0000-0000-0000-000000000012',
    '20000000-0000-0000-0000-000000000001',
    'Migration 040 test fixture',
    TRUE
);

INSERT INTO app_permissions (
    app_permission_id, permission_code, permission_name, permission_description, module_code, is_active
)
VALUES (
    '30000000-0000-0000-0000-000000000001',
    'LEGACY_TEST_PERMISSION',
    'Legacy test permission',
    'Must survive migration and rollback',
    'TEST',
    TRUE
);

INSERT INTO app_role_permissions (app_role_id, app_permission_id, is_active)
VALUES (
    '10000000-0000-0000-0000-000000000012',
    '30000000-0000-0000-0000-000000000001',
    TRUE
);
SQL

legacy_users_before="$(value 'SELECT COUNT(*) FROM app_users;')"
legacy_roles_before="$(value 'SELECT COUNT(*) FROM app_roles;')"
legacy_assignments_before="$(value 'SELECT COUNT(*) FROM app_user_role_assignments;')"
legacy_permissions_before="$(value 'SELECT COUNT(*) FROM app_role_permissions;')"

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='040_scoped_role_policy_versions';")" migration_registered_once
assert_eq 70 "$(value 'SELECT COUNT(*) FROM scoped_role_policy_modules;')" workbook_module_count
assert_eq 1 "$(value "SELECT COUNT(*) FROM scoped_role_policy_versions WHERE version_number=1 AND policy_status='PUBLISHED';")" baseline_policy_count
assert_eq 1 "$(value "SELECT COUNT(*) FROM scoped_role_policy_audit_events WHERE event_code='POLICY_BASELINE_PUBLISHED';")" baseline_audit_idempotent
assert_eq 0 "$(value "SELECT COUNT(*) FROM scoped_role_policy_grants WHERE role_code='ENGINEERING' AND module_code='007';")" not_set_preserves_legacy
assert_gt_zero "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE role_code='SUPER_ADMINISTRATOR' AND module_code IN ('005','006') AND grant_effect='GRANT';")" super_admin_005_006_grants
assert_eq 0 "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE role_code='SUPER_ADMINISTRATOR' AND module_code IN ('005','006') AND grant_effect='DENY';")" super_admin_005_006_denials
assert_gt_zero "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE role_code='SUPER_ADMINISTRATOR' AND module_code='001' AND action_code='TIME_CORRECT_ON_BEHALF' AND scope_code='ORGANIZATION' AND grant_effect='GRANT';")" super_admin_time_custom_authority
assert_gt_zero "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE role_code='SUPER_ADMINISTRATOR' AND module_code='001' AND action_code='TIME_DELETE_PERMANENT' AND grant_effect='DENY';")" super_admin_non_bypassable_time_deny
assert_eq 0 "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE module_code='003' AND grant_effect='GRANT' AND action_code NOT IN ('MODULE_VIEW','UTILIZATION_VIEW');")" utilization_read_only
assert_eq 0 "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE module_code='037' AND grant_effect='GRANT' AND action_code NOT IN ('MODULE_VIEW','MATRIX_VIEW','MATRIX_EXPORT','ACCESS_EXPLAIN');")" matrix_read_only
assert_eq 0 "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE role_code IN ('PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD') AND action_code ILIKE '%PASSWORD%';")" pm_password_reset_not_inherited
assert_gt_zero "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE role_code='PROJECT_TEAM_COORDINATOR' AND module_code='002' AND action_code IN ('APPROVAL_DELEGATE_MANAGER','APPROVAL_DELEGATE_PROJECT_MANAGER') AND delegated_authority=TRUE AND reason_required=TRUE AND audit_required=TRUE;")" ptc_delegated_approval_auditable
assert_gt_zero "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE role_code='SUPER_ADMINISTRATOR' AND module_code='012' AND action_code='POLICY_PUBLISH' AND grant_effect='GRANT';")" super_admin_policy_publish
assert_eq 0 "$(value "SELECT COUNT(*) FROM scoped_role_policy_effective_grants WHERE role_code <> 'SUPER_ADMINISTRATOR' AND module_code='012' AND action_code IN ('POLICY_PUBLISH','POLICY_RESTORE') AND grant_effect='GRANT';")" non_super_admin_policy_writes

assert_eq "$legacy_users_before" "$(value 'SELECT COUNT(*) FROM app_users;')" legacy_users_preserved_after_apply
assert_eq "$legacy_roles_before" "$(value 'SELECT COUNT(*) FROM app_roles;')" legacy_roles_preserved_after_apply
assert_eq "$legacy_assignments_before" "$(value 'SELECT COUNT(*) FROM app_user_role_assignments;')" legacy_assignments_preserved_after_apply
assert_eq "$legacy_permissions_before" "$(value 'SELECT COUNT(*) FROM app_role_permissions;')" legacy_permissions_preserved_after_apply

if psql_exec -c "UPDATE scoped_role_policy_audit_events SET reason='tamper';" >/dev/null 2>&1; then
  echo 'ASSERTION_FAILED immutable_policy_audit_update_was_allowed' >&2
  exit 1
fi
echo 'ASSERTION_PASSED immutable_policy_audit_update_blocked'

if psql_exec -c "UPDATE scoped_role_policy_grants SET source_notes='tamper' WHERE policy_version_id='04000000-0000-0000-0000-000000000001' LIMIT 1;" >/dev/null 2>&1; then
  echo 'ASSERTION_FAILED immutable_published_grant_update_was_allowed' >&2
  exit 1
fi
echo 'ASSERTION_PASSED immutable_published_grant_update_blocked'

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.scoped_role_policy_versions')::text,'');")" scoped_tables_removed_by_rollback
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='040_scoped_role_policy_versions';")" migration_unregistered_by_rollback
assert_eq "$legacy_users_before" "$(value 'SELECT COUNT(*) FROM app_users;')" legacy_users_preserved_after_rollback
assert_eq "$legacy_roles_before" "$(value 'SELECT COUNT(*) FROM app_roles;')" legacy_roles_preserved_after_rollback
assert_eq "$legacy_assignments_before" "$(value 'SELECT COUNT(*) FROM app_user_role_assignments;')" legacy_assignments_preserved_after_rollback
assert_eq "$legacy_permissions_before" "$(value 'SELECT COUNT(*) FROM app_role_permissions;')" legacy_permissions_preserved_after_rollback

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -c "
  INSERT INTO scoped_role_policy_versions (
      policy_version_id, version_number, policy_name, policy_status,
      source_name, source_sha256, policy_notes
  ) VALUES (
      '04000000-0000-0000-0000-000000000002', 2,
      'Rollback protection test', 'RETIRED',
      'test', 'test', 'Rollback must now fail closed'
  );
" >/dev/null

if psql_exec -f "$ROLLBACK" >/tmp/projectpulse-rbac-040-rollback.log 2>&1; then
  echo 'ASSERTION_FAILED rollback_with_version_2_was_allowed' >&2
  cat /tmp/projectpulse-rbac-040-rollback.log >&2
  exit 1
fi
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='040_scoped_role_policy_versions';")" blocked_rollback_keeps_migration
assert_eq 2 "$(value 'SELECT COUNT(*) FROM scoped_role_policy_versions;')" blocked_rollback_keeps_policy_versions

echo 'SCOPED_RBAC_MIGRATION_040_DATABASE_TEST=PASS'
