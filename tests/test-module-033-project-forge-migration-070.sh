#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-project-forge-070-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/070_module_033_project_forge.sql"
ROLLBACK="/workspace/database/rollback/070_module_033_project_forge_rollback.sql"
ASSERTIONS="/workspace/scripts/tests/070_module_033_project_forge_migration_test.sql"

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

expect_failure() {
  local label="$1" expected="$2"
  shift 2
  local log="/tmp/project-forge-070-${label}.log"
  if "$@" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label expected failure" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || {
    echo "ASSERTION_FAILED $label missing_expected=$expected" >&2
    cat "$log" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=blocked"
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
  permission_code VARCHAR(100) NOT NULL UNIQUE,
  permission_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  permission_description TEXT NOT NULL DEFAULT ''
);
CREATE TABLE app_role_permissions (
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id),
  app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(app_role_id, app_permission_id)
);
CREATE TABLE projects (
  project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_code TEXT NOT NULL UNIQUE,
  project_name TEXT NOT NULL
);
CREATE TABLE project_tasks (
  task_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
  task_code TEXT NOT NULL,
  task_name TEXT NOT NULL,
  UNIQUE(project_id, task_code)
);
CREATE TABLE project_assignments (
  project_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID NOT NULL REFERENCES projects(project_id),
  task_id UUID NOT NULL REFERENCES project_tasks(task_id),
  user_id UUID NOT NULL REFERENCES app_users(user_id)
);
CREATE TABLE app_feature_catalog (
  feature_code VARCHAR(100) PRIMARY KEY,
  feature_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  route_anchor TEXT NOT NULL,
  required_permission_code TEXT NOT NULL,
  feature_description TEXT NOT NULL DEFAULT '',
  display_order INTEGER NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE enterprise_notification_policies (
  enterprise_notification_policy_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  policy_code VARCHAR(160) NOT NULL UNIQUE,
  policy_name TEXT NOT NULL,
  category TEXT NOT NULL,
  source_module TEXT NOT NULL,
  event_code TEXT NOT NULL,
  trigger_mode TEXT NOT NULL,
  recipient_strategy TEXT NOT NULL,
  trigger_configuration JSONB NOT NULL DEFAULT '{}'::JSONB,
  recipient_configuration JSONB NOT NULL DEFAULT '{}'::JSONB,
  severity TEXT NOT NULL,
  delivery_boundary TEXT NOT NULL DEFAULT 'test_only',
  acknowledgement_required BOOLEAN NOT NULL DEFAULT FALSE,
  acknowledgement_escalation_minutes INTEGER NULL,
  subject_template TEXT NOT NULL,
  text_template TEXT NOT NULL,
  owner_module TEXT NOT NULL DEFAULT '065',
  producer_contract TEXT NOT NULL,
  source_state TEXT NOT NULL,
  enabled BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE enterprise_notification_events (
  enterprise_notification_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  policy_code VARCHAR(160) NOT NULL REFERENCES enterprise_notification_policies(policy_code)
);
CREATE TABLE enterprise_notification_policy_audit (
  enterprise_notification_policy_audit_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  enterprise_notification_policy_id UUID NOT NULL REFERENCES enterprise_notification_policies(enterprise_notification_policy_id)
);
CREATE TABLE ai_capability_routes (
  feature_code TEXT PRIMARY KEY,
  route_targets JSONB NOT NULL,
  external_context_policy TEXT NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_by UUID NULL
);
CREATE TABLE ai_capability_route_audit (
  audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  feature_code TEXT NOT NULL
);

INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Admin'),
 ('10000000-0000-0000-0000-000000000002','engineer@example.test','Engineer');
INSERT INTO projects(project_id,project_code,project_name) VALUES
 ('20000000-0000-0000-0000-000000000001','PF-070','Project Forge validation');
INSERT INTO app_roles(role_code,role_name)
SELECT role_code, replace(role_code, '_', ' ')
FROM unnest(ARRAY[
  'SUPER_ADMINISTRATOR','ADMINISTRATOR','PROJECT_MANAGER','PROJECT_MANAGEMENT',
  'PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD',
  'ENGINEERING','ENGINEER','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD'
]) role_code;
SQL

apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }
rollback_migration() { psql_exec -f "$ROLLBACK" >/dev/null; }

apply_migration
apply_migration
psql_exec -f "$ASSERTIONS"
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='070_module_033_project_forge';")" migration_registered_once
assert_eq 0 "$(value "SELECT COUNT(*) FROM project_forge_plans;")" migration_seeded_no_plans
assert_eq 0 "$(value "SELECT COUNT(*) FROM project_forge_plan_tasks;")" migration_seeded_no_tasks
assert_eq 4 "$(value "SELECT COUNT(*) FROM enterprise_notification_policies WHERE source_module='033';")" module065_policies

rollback_migration
assert_eq '' "$(value "SELECT to_regclass('public.project_forge_plans');")" empty_database_rollback_removed_tables
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='070_module_033_project_forge';")" rollback_removed_migration_evidence

apply_migration
psql_exec <<'SQL'
INSERT INTO project_forge_plans(
  plan_id,project_id,plan_name,objective,source_kind,created_by_user_id,updated_by_user_id
) VALUES (
  '30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001',
  'Validation plan','Validate Project Forge','manual',
  '10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'
);
INSERT INTO project_forge_plan_tasks(
  plan_task_id,plan_id,project_id,wbs_code,task_name,estimated_hours,
  reviewer_user_id,created_by_user_id,updated_by_user_id
) VALUES (
  '40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001','1','Validation task',8,
  '10000000-0000-0000-0000-000000000002',
  '10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'
);
INSERT INTO project_forge_plan_assignments(
  plan_id,plan_task_id,project_id,user_id,assignment_type,planned_hours,
  assigned_by_user_id
) VALUES (
  '30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002',
  'task_estimator',8,'10000000-0000-0000-0000-000000000001'
);
UPDATE project_forge_plan_tasks
SET estimated_hours=12,updated_by_user_id='10000000-0000-0000-0000-000000000002'
WHERE plan_task_id='40000000-0000-0000-0000-000000000001';
SQL

assert_eq 2 "$(value "SELECT revision_number FROM project_forge_plan_tasks WHERE plan_task_id='40000000-0000-0000-0000-000000000001';")" optimistic_revision_trigger
assert_eq 4 "$(value "SELECT COUNT(*) FROM project_forge_audit_events;")" automatic_audit_evidence
expect_failure audit_update_immutable 'Project Forge audit evidence is append-only' \
  psql_exec -qc "UPDATE project_forge_audit_events SET event_code='tampered';"
expect_failure guarded_rollback_with_operational_data 'rollback blocked' rollback_migration

echo 'MODULE_033_PROJECT_FORGE_MIGRATION_070=PASS'
