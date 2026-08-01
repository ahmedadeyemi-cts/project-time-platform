#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-superadmin-061-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/061_super_administrator_permanent_full_control.sql"
ROLLBACK="/workspace/database/rollback/061_super_administrator_permanent_full_control_rollback.sql"

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
  local log="/tmp/projectpulse-061-${label}.log"
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
  user_id UUID PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
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

INSERT INTO app_users (user_id, email, display_name) VALUES
  ('10000000-0000-0000-0000-000000000001', 'legacy-admin@example.test', 'Legacy Admin'),
  ('10000000-0000-0000-0000-000000000002', 'canonical-admin@example.test', 'Canonical Admin');

INSERT INTO app_roles (role_code, role_name, display_order) VALUES
  ('ADMINISTRATOR', 'Administrator', 60),
  ('SUPER_ADMINISTRATOR', 'Super Administrator', 120);

INSERT INTO app_permissions (permission_code, permission_name, module_code) VALUES
  ('MANAGE_ALL', 'Manage all', 'admin'),
  ('MANAGE_INTEGRATIONS_026', 'Manage CRM integrations', '026'),
  ('MANAGE_ENTRA_SECRET', 'Manage Entra secret', '065'),
  ('MODULE_ACCESS', 'Open module', 'shared');

INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason)
SELECT '10000000-0000-0000-0000-000000000001', app_role_id, 'Legacy assignment'
FROM app_roles WHERE role_code = 'ADMINISTRATOR';

INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason)
SELECT '10000000-0000-0000-0000-000000000002', app_role_id, 'Pre-existing canonical assignment'
FROM app_roles WHERE role_code = 'SUPER_ADMINISTRATOR';

-- One relationship predates migration 061 and must survive rollback.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'MANAGE_ALL'
WHERE role.role_code = 'SUPER_ADMINISTRATOR';
SQL

apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }
apply_migration
apply_migration

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='061_super_administrator_permanent_full_control';")" migration_registered_once
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_user_role_assignments a JOIN app_roles r ON r.app_role_id=a.app_role_id WHERE a.user_id='10000000-0000-0000-0000-000000000001' AND r.role_code='SUPER_ADMINISTRATOR' AND a.is_active=TRUE;")" legacy_admin_reconciled
assert_eq 8 "$(value "SELECT COUNT(*) FROM app_role_permissions rp JOIN app_roles r ON r.app_role_id=rp.app_role_id WHERE r.role_code IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR');")" every_admin_permission_explicit
assert_eq 1 "$(value "SELECT COUNT(*) FROM role_access_repair_061_assignment_changes;")" assignment_evidence_count
assert_eq 7 "$(value "SELECT COUNT(*) FROM role_access_repair_061_permission_changes;")" permission_evidence_count

expect_sql_failure \
  "DELETE FROM role_access_repair_061_permission_changes;" \
  "ProjectPulse migration 061 evidence is immutable" \
  immutable_permission_evidence

psql_exec <<'SQL'
INSERT INTO app_permissions (permission_code, permission_name, module_code)
VALUES ('FUTURE_MODULE_CONFIGURE', 'Configure a future module', '900');
SQL
apply_migration

assert_eq 10 "$(value "SELECT COUNT(*) FROM app_role_permissions rp JOIN app_roles r ON r.app_role_id=rp.app_role_id WHERE r.role_code IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR');")" later_permission_reconciled
assert_eq 9 "$(value "SELECT COUNT(*) FROM role_access_repair_061_permission_changes;")" later_permission_evidence_recorded

psql_exec -f "$ROLLBACK" >/dev/null

assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='061_super_administrator_permanent_full_control';")" rollback_removed_registration
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_user_role_assignments a JOIN app_roles r ON r.app_role_id=a.app_role_id WHERE a.user_id='10000000-0000-0000-0000-000000000001' AND r.role_code='SUPER_ADMINISTRATOR';")" rollback_removed_inserted_canonical_assignment
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_user_role_assignments a JOIN app_roles r ON r.app_role_id=a.app_role_id WHERE a.user_id='10000000-0000-0000-0000-000000000002' AND r.role_code='SUPER_ADMINISTRATOR' AND a.is_active=TRUE;")" rollback_preserved_preexisting_canonical_assignment
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_role_permissions rp JOIN app_roles r ON r.app_role_id=rp.app_role_id JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id WHERE r.role_code='SUPER_ADMINISTRATOR' AND p.permission_code='MANAGE_ALL';")" rollback_preserved_preexisting_permission
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_role_permissions rp JOIN app_roles r ON r.app_role_id=rp.app_role_id JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id WHERE r.role_code='ADMINISTRATOR';")" rollback_removed_only_migration_permissions

apply_migration
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='061_super_administrator_permanent_full_control';")" migration_reapplied
assert_eq 10 "$(value "SELECT COUNT(*) FROM app_role_permissions rp JOIN app_roles r ON r.app_role_id=rp.app_role_id WHERE r.role_code IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR');")" reapplied_permissions_complete

echo 'SUPER_ADMINISTRATOR_PERMANENT_FULL_CONTROL_MIGRATION_061=PASS'
