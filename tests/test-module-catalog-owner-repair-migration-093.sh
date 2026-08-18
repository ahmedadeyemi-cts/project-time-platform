#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-owner-catalog-093-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/093_assigned_work_canonical_visibility_repair.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() { docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"; }
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}

docker run --detach --rm --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  psql_exec -Atqc 'SELECT 1' >/dev/null 2>&1 && break
  [[ "$attempt" != 60 ]] || {
    docker logs "$CONTAINER" >&2 || true
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

CREATE TABLE app_users (
    user_id UUID PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE projects (
    project_id UUID PRIMARY KEY,
    status TEXT NOT NULL DEFAULT 'active'
);

CREATE TABLE project_tasks (
    task_id UUID PRIMARY KEY,
    project_id UUID NOT NULL REFERENCES projects(project_id),
    task_code TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE project_assignments (
    project_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id),
    task_id UUID NOT NULL REFERENCES project_tasks(task_id),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    assigned_by_user_id UUID NULL REFERENCES app_users(user_id),
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE NULL,
    allocation_percent NUMERIC(7,2) NULL,
    UNIQUE (project_id, task_id, user_id, effective_start_date)
);

CREATE TABLE work_register_task_assignment_history (
    work_register_task_assignment_history_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id),
    task_id_text TEXT NOT NULL,
    assigned_user_id UUID NULL REFERENCES app_users(user_id),
    changed_by_user_id UUID NULL REFERENCES app_users(user_id),
    assignment_status TEXT NOT NULL DEFAULT 'active',
    effective_start_date DATE NULL,
    effective_end_date DATE NULL,
    allocated_hours NUMERIC(10,2) NULL,
    allocation_percent NUMERIC(7,2) NULL,
    change_reason TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE scoped_role_policy_modules (
    module_code TEXT PRIMARY KEY,
    module_name TEXT NOT NULL,
    route_scope TEXT NOT NULL,
    current_state TEXT NOT NULL,
    permission_notes TEXT NOT NULL DEFAULT '',
    source_url TEXT NOT NULL DEFAULT '',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    owner_user_id UUID NULL,
    owner_revision_number INTEGER NOT NULL DEFAULT 0,
    owner_updated_at TIMESTAMPTZ NULL,
    owner_updated_by_user_id UUID NULL
);

INSERT INTO app_users (user_id, email, display_name, is_active)
VALUES
    ('10000000-0000-0000-0000-000000000001', 'default.owner@example.test', 'Default Owner', TRUE),
    ('10000000-0000-0000-0000-000000000002', 'existing.owner@example.test', 'Existing Owner', TRUE);

INSERT INTO scoped_role_policy_modules (
    module_code,
    module_name,
    route_scope,
    current_state,
    permission_notes,
    source_url,
    is_active,
    owner_user_id,
    owner_revision_number,
    owner_updated_at,
    owner_updated_by_user_id
)
VALUES
    ('001', 'Timesheet', 'timesheet', 'Installed', '', 'fixture', TRUE,
     '10000000-0000-0000-0000-000000000001', 4, NOW(), '10000000-0000-0000-0000-000000000001'),
    ('002', 'Approval Inbox', 'manager-approval', 'Installed', '', 'fixture', TRUE,
     '10000000-0000-0000-0000-000000000001', 4, NOW(), '10000000-0000-0000-0000-000000000001'),
    ('033', 'Legacy Forge Label', 'legacy-forge', 'Legacy', 'legacy', 'fixture', FALSE,
     '10000000-0000-0000-0000-000000000002', 7, NOW(), '10000000-0000-0000-0000-000000000002');
SQL

psql_exec -f "$MIGRATION" >/dev/null

assert_eq 3 "$(value "SELECT COUNT(*) FROM scoped_role_policy_modules WHERE module_code IN ('031','032','033') AND is_active=TRUE")" owner_repair_target_count
assert_eq 3 "$(value "SELECT COUNT(*) FROM scoped_role_policy_modules WHERE (module_code,module_name,route_scope) IN (('031','Financial Operations Workbench','financial-operations-workbench'),('032','Notification Delivery Monitor','notification-delivery-monitor'),('033','Project Forge','project-forge'))")" canonical_catalog_metadata
assert_eq 2 "$(value "SELECT COUNT(*) FROM scoped_role_policy_modules WHERE module_code IN ('031','032') AND owner_user_id='10000000-0000-0000-0000-000000000001' AND owner_revision_number=1")" inserted_modules_inherit_default_owner
assert_eq '10000000-0000-0000-0000-000000000002|7' "$(value "SELECT owner_user_id::TEXT || '|' || owner_revision_number::TEXT FROM scoped_role_policy_modules WHERE module_code='033'")" preexisting_owner_preserved
assert_eq 2 "$(value "SELECT COUNT(*) FROM module_catalog_reconciliation_093_owner_repair_evidence WHERE was_present=FALSE")" missing_rows_recorded
assert_eq 1 "$(value "SELECT COUNT(*) FROM module_catalog_reconciliation_093_owner_repair_evidence WHERE module_code='033' AND was_present=TRUE")" preexisting_row_recorded

psql_exec -qc "UPDATE scoped_role_policy_modules SET owner_user_id='10000000-0000-0000-0000-000000000002', owner_revision_number=2, owner_updated_at=NOW(), owner_updated_by_user_id='10000000-0000-0000-0000-000000000002' WHERE module_code='031'"
psql_exec -f "$MIGRATION" >/dev/null

assert_eq '10000000-0000-0000-0000-000000000002|2' "$(value "SELECT owner_user_id::TEXT || '|' || owner_revision_number::TEXT FROM scoped_role_policy_modules WHERE module_code='031'")" rerun_preserves_changed_owner
assert_eq '10000000-0000-0000-0000-000000000001|1' "$(value "SELECT repaired_owner_user_id::TEXT || '|' || repaired_owner_revision_number::TEXT FROM module_catalog_reconciliation_093_owner_repair_evidence WHERE module_code='031'")" repair_evidence_preserved
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='093_assigned_work_canonical_visibility_repair'")" migration_registered_once
assert_eq 3 "$(value "SELECT COUNT(*) FROM module_catalog_reconciliation_093_owner_repair_evidence")" repair_evidence_exact_scope

echo 'MODULE_CATALOG_OWNER_REPAIR_MIGRATION_093=PASS'
