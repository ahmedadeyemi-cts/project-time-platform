#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-project-forge-073-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
BASE_MIGRATION="/workspace/database/migrations/070_module_033_project_forge.sql"
MIGRATION="/workspace/database/migrations/073_module_033_project_forge_interactive.sql"
ROLLBACK="/workspace/database/rollback/073_module_033_project_forge_interactive_rollback.sql"
ASSERTIONS="/workspace/scripts/tests/073_module_033_project_forge_interactive_migration_test.sql"

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
  local log="/tmp/project-forge-073-${label}.log"
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
  UNIQUE(app_role_id,app_permission_id)
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
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(project_id,task_code)
);
CREATE TABLE project_assignments (
  project_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID NOT NULL REFERENCES projects(project_id),
  task_id UUID NOT NULL REFERENCES project_tasks(task_id),
  user_id UUID NOT NULL REFERENCES app_users(user_id),
  effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
  effective_end_date DATE NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE company_holidays (
  company_holiday_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  holiday_date DATE NOT NULL UNIQUE,
  holiday_name TEXT NOT NULL,
  is_floating_holiday BOOLEAN NOT NULL DEFAULT FALSE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
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
 ('10000000-0000-0000-0000-000000000002','engineer@example.test','Engineer'),
 ('10000000-0000-0000-0000-000000000003','engineer2@example.test','Engineer 2');
INSERT INTO projects(project_id,project_code,project_name) VALUES
 ('20000000-0000-0000-0000-000000000001','PF-073','Project Forge interactive validation');
INSERT INTO project_tasks(task_id,project_id,task_code,task_name) VALUES
 ('40000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','TASK-1','Existing canonical task');
INSERT INTO project_assignments(
  project_assignment_id,project_id,task_id,user_id,effective_start_date,updated_at
) VALUES (
  '50000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001',
  '40000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002',
  DATE '2026-01-01',TIMESTAMPTZ '2026-01-02 03:04:05+00'
);
INSERT INTO app_roles(role_code,role_name)
SELECT role_code,replace(role_code,'_',' ')
FROM unnest(ARRAY[
  'SUPER_ADMINISTRATOR','ADMINISTRATOR','PROJECT_MANAGER','PROJECT_MANAGEMENT',
  'PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD',
  'ENGINEERING','ENGINEER','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD',
  'SYSTEMS_ENGINEER','NETWORK_ENGINEER','ENTERPRISE_NETWORK_ENGINEER'
]) role_code;
SQL

apply_base() { psql_exec -f "$BASE_MIGRATION" >/dev/null; }
apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }
rollback_migration() { psql_exec -f "$ROLLBACK" >/dev/null; }

apply_base
apply_migration
migration_applied_at_before="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='073_module_033_project_forge_interactive';")"
apply_migration
psql_exec -f "$ASSERTIONS"

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='073_module_033_project_forge_interactive';")" migration_registered_once
assert_eq "$migration_applied_at_before" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='073_module_033_project_forge_interactive';")" migration_applied_at_is_immutable
assert_eq 0 "$(value 'SELECT COUNT(*) FROM project_forge_plans;')" migration_seeded_no_plans
assert_eq 0 "$(value 'SELECT COUNT(*) FROM project_forge_plan_tasks;')" migration_seeded_no_review_tasks
assert_eq 0 "$(value 'SELECT COUNT(*) FROM project_task_dependencies;')" migration_seeded_no_canonical_dependencies
assert_eq 1 "$(value "SELECT COUNT(*) FROM project_assignments WHERE is_primary_assignee=TRUE;")" primary_assignment_backfilled_once
assert_eq '2026-01-02 03:04:05+00' "$(value "SELECT updated_at FROM project_assignments WHERE project_assignment_id='50000000-0000-0000-0000-000000000001';")" preexisting_assignment_updated_at_preserved

rollback_migration
assert_eq '' "$(value "SELECT to_regclass('public.project_task_dependencies');")" empty_rollback_removed_canonical_dependencies
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='073_module_033_project_forge_interactive';")" empty_rollback_removed_migration_evidence
assert_eq 1 "$(value "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='project_assignments' AND column_name='updated_at';")" rollback_preserved_preexisting_updated_at_column
assert_eq '2026-01-02 03:04:05+00' "$(value "SELECT updated_at FROM project_assignments WHERE project_assignment_id='50000000-0000-0000-0000-000000000001';")" rollback_preserved_preexisting_updated_at_value
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_constraint WHERE conrelid='project_forge_plan_tasks'::regclass AND conname='project_forge_plan_tasks_duration_working_days_check' AND pg_get_constraintdef(oid) LIKE '%>= 0%' AND pg_get_constraintdef(oid) NOT LIKE '%3660%';")" rollback_restored_exact_070_duration_constraint
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_constraint WHERE conrelid='project_forge_task_dependencies'::regclass AND conname='project_forge_task_dependencies_lag_working_days_check' AND pg_get_constraintdef(oid) LIKE '%3650%';")" rollback_restored_070_lag_bound

psql_exec -qc 'ALTER TABLE project_tasks ADD COLUMN revision_number INTEGER NOT NULL DEFAULT 1;'
expect_failure ownership_preflight 'Migration 073 ownership preflight failed' apply_migration
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='073_module_033_project_forge_interactive';")" ownership_preflight_is_atomic
assert_eq '' "$(value "SELECT to_regclass('public.project_task_dependencies');")" ownership_preflight_created_no_canonical_dependency_table
psql_exec -qc 'ALTER TABLE project_tasks DROP COLUMN revision_number;'

apply_migration
psql_exec <<'SQL'
INSERT INTO company_holidays(holiday_date,holiday_name)
VALUES(DATE '2026-08-10','Validation holiday');

INSERT INTO project_tasks(task_id,project_id,task_code,task_name) VALUES
 ('40000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','TASK-2','Second canonical task'),
 ('40000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000001','TASK-3','Third canonical task');

INSERT INTO project_forge_plans(
  plan_id,project_id,plan_name,objective,source_kind,created_by_user_id,updated_by_user_id
) VALUES (
  '30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001',
  'Interactive validation plan','Validate interactive migration','manual',
  '10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'
);
INSERT INTO project_forge_plan_tasks(
  plan_task_id,plan_id,project_id,wbs_code,task_name,reviewer_user_id,created_by_user_id,updated_by_user_id
) VALUES (
  '41000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001','1','Review validation task',
  '10000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001'
);
INSERT INTO project_forge_plan_assignments(
  plan_id,plan_task_id,project_id,user_id,assignment_type,planned_hours,assigned_by_user_id
) VALUES (
  '30000000-0000-0000-0000-000000000001','41000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002',
  'task_estimator',8,'10000000-0000-0000-0000-000000000001'
);
INSERT INTO project_forge_task_details(
  task_id,project_id,created_by_user_id,updated_by_user_id
) VALUES
 ('40000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'),
 ('40000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'),
 ('40000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');

UPDATE project_tasks
SET task_name='Existing canonical task revised'
WHERE task_id='40000000-0000-0000-0000-000000000001';
UPDATE project_assignments
SET updated_by_user_id='10000000-0000-0000-0000-000000000001'
WHERE project_assignment_id='50000000-0000-0000-0000-000000000001';
SQL

assert_eq 2 "$(value "SELECT revision_number FROM project_tasks WHERE task_id='40000000-0000-0000-0000-000000000001';")" canonical_task_revision_trigger
assert_eq 2 "$(value "SELECT revision_number FROM project_assignments WHERE project_assignment_id='50000000-0000-0000-0000-000000000001';")" canonical_assignment_revision_trigger
assert_eq '2026-08-11' "$(value "SELECT projectpulse073_add_working_days(DATE '2026-08-07',1);")" working_day_add_skips_weekend_and_holiday
assert_eq 1 "$(value "SELECT projectpulse073_working_day_delta(DATE '2026-08-07',DATE '2026-08-11');")" working_day_delta_skips_weekend_and_holiday
assert_eq 2 "$(value "SELECT projectpulse073_working_day_duration(DATE '2026-08-07',DATE '2026-08-11');")" working_day_duration_skips_weekend_and_holiday

expect_failure duplicate_primary_assignee 'duplicate key value violates unique constraint' \
  psql_exec -qc "INSERT INTO project_assignments(project_id,task_id,user_id,effective_start_date,is_primary_assignee) VALUES('20000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000003',CURRENT_DATE,TRUE);"

psql_exec <<'SQL'
INSERT INTO project_task_dependencies(
  project_task_dependency_id,project_id,predecessor_task_id,successor_task_id,dependency_type,
  created_by_user_id,updated_by_user_id
) VALUES
 ('60000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000002','FS','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'),
 ('60000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000002','40000000-0000-0000-0000-000000000003','FS','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');
SQL

expect_failure dependency_cycle 'would create a cycle' \
  psql_exec -qc "INSERT INTO project_task_dependencies(project_id,predecessor_task_id,successor_task_id,dependency_type,created_by_user_id,updated_by_user_id) VALUES('20000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000003','40000000-0000-0000-0000-000000000001','FS','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');"
expect_failure guarded_rollback_dependency_evidence 'Rollback refused: canonical Project Forge dependencies exist.' rollback_migration
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='073_module_033_project_forge_interactive';")" failed_rollback_is_atomic
assert_eq 2 "$(value 'SELECT COUNT(*) FROM project_task_dependencies;')" failed_rollback_preserved_dependencies

psql_exec -qc 'DELETE FROM project_task_dependencies;'
expect_failure guarded_rollback_audit_evidence 'Rollback refused: interactive Project Forge audit evidence exists.' rollback_migration
# The append-only guard is disabled only inside this disposable migration-test
# database and only long enough to remove audit rows created by this test.
psql_exec <<'SQL'
ALTER TABLE project_forge_audit_events DISABLE TRIGGER trg_project_forge_audit_events_immutable;
DELETE FROM project_forge_audit_events
WHERE event_code LIKE 'canonical_dependency_%'
   OR event_code IN ('TASK_DEPENDENCY_CREATED','TASK_DEPENDENCY_UPDATED','TASK_DEPENDENCY_DELETED');
ALTER TABLE project_forge_audit_events ENABLE TRIGGER trg_project_forge_audit_events_immutable;
SQL
psql_exec -qc "INSERT INTO project_forge_audit_events(audit_event_id,project_id,event_code,entity_type,entity_id,event_metadata,correlation_id) VALUES('70000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','TASK_ASSIGNEE_UPDATED','canonical_task','40000000-0000-0000-0000-000000000001','{}','');"
expect_failure guarded_rollback_assignee_audit_evidence 'Rollback refused: interactive Project Forge audit evidence exists.' rollback_migration
psql_exec <<'SQL'
ALTER TABLE project_forge_audit_events DISABLE TRIGGER trg_project_forge_audit_events_immutable;
DELETE FROM project_forge_audit_events
WHERE audit_event_id='70000000-0000-0000-0000-000000000001';
ALTER TABLE project_forge_audit_events ENABLE TRIGGER trg_project_forge_audit_events_immutable;
SQL
psql_exec -qc "UPDATE project_forge_plan_assignments SET reviewed_task_revision=1 WHERE plan_task_id='41000000-0000-0000-0000-000000000001';"
expect_failure guarded_rollback_review_evidence 'Rollback refused: explicit task-review revision evidence exists.' rollback_migration

psql_exec -qc "UPDATE project_forge_plan_assignments SET reviewed_task_revision=NULL WHERE plan_task_id='41000000-0000-0000-0000-000000000001';"
psql_exec -qc "UPDATE project_forge_task_details SET duration_working_days=5 WHERE task_id='40000000-0000-0000-0000-000000000001';"
expect_failure guarded_rollback_schedule_evidence 'Rollback refused: interactive Project Forge scheduling/workflow data exists.' rollback_migration

psql_exec -qc "UPDATE project_forge_task_details SET duration_working_days=0 WHERE task_id='40000000-0000-0000-0000-000000000001';"
rollback_migration
assert_eq '' "$(value "SELECT to_regclass('public.project_task_dependencies');")" final_rollback_removed_canonical_dependencies
assert_eq 1 "$(value "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='project_assignments' AND column_name='updated_at';")" final_rollback_preserved_updated_at

echo 'MODULE_033_PROJECT_FORGE_INTERACTIVE_MIGRATION_073=PASS'
