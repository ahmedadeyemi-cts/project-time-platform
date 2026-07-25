#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-ptc-043-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/043_ptc_time_steward_permissions.sql"
ROLLBACK="/workspace/database/rollback/043_ptc_time_steward_permissions_rollback.sql"

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
  local label="$1" command="$2"
  if bash -lc "$command" >/tmp/ptc-043-expected-failure.log 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
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
INSERT INTO schema_migrations (migration_id, description) VALUES
  ('040_scoped_role_policy_versions', 'test prerequisite'),
  ('041_module_001_timesheet_timer_and_task_association', 'test prerequisite'),
  ('042_module_availability_controls', 'test prerequisite');

CREATE TABLE app_users (
  user_id UUID PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE projects (
  project_id UUID PRIMARY KEY,
  project_code TEXT NOT NULL,
  project_name TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'active'
);
CREATE TABLE project_tasks (
  task_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(project_id),
  task_code TEXT NOT NULL,
  task_name TEXT NOT NULL,
  task_description TEXT NULL,
  billable BOOLEAN NOT NULL DEFAULT TRUE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (project_id, task_code)
);
CREATE TABLE project_assignments (
  project_assignment_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(project_id),
  task_id UUID NOT NULL REFERENCES project_tasks(task_id),
  user_id UUID NOT NULL REFERENCES app_users(user_id),
  assigned_by_user_id UUID NULL REFERENCES app_users(user_id),
  effective_start_date DATE NOT NULL,
  effective_end_date DATE NULL
);
CREATE TABLE timesheets (
  timesheet_id UUID PRIMARY KEY,
  user_id UUID NOT NULL REFERENCES app_users(user_id),
  week_start_date DATE NOT NULL,
  week_end_date DATE NOT NULL,
  status TEXT NOT NULL DEFAULT 'draft',
  submitted_at TIMESTAMPTZ NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE time_entries (
  time_entry_id UUID PRIMARY KEY,
  timesheet_id UUID NOT NULL REFERENCES timesheets(timesheet_id),
  user_id UUID NOT NULL REFERENCES app_users(user_id),
  project_id UUID NOT NULL REFERENCES projects(project_id),
  task_id UUID NULL REFERENCES project_tasks(task_id),
  work_date DATE NOT NULL,
  hours NUMERIC(6,2) NOT NULL,
  description TEXT NULL,
  billable BOOLEAN NOT NULL DEFAULT TRUE,
  status TEXT NOT NULL DEFAULT 'draft',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE scoped_role_policy_modules (
  module_code TEXT PRIMARY KEY,
  module_name TEXT NOT NULL,
  route_scope TEXT NOT NULL,
  current_state TEXT NOT NULL,
  permission_notes TEXT NOT NULL DEFAULT '',
  source_url TEXT NOT NULL DEFAULT '',
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE scoped_role_policy_actions (
  action_code TEXT PRIMARY KEY,
  action_description TEXT NOT NULL,
  is_non_bypassable BOOLEAN NOT NULL DEFAULT FALSE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE scoped_role_policy_scopes (
  scope_code TEXT PRIMARY KEY,
  scope_description TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE scoped_role_policy_versions (
  policy_version_id UUID PRIMARY KEY,
  version_number INTEGER NOT NULL UNIQUE,
  policy_name TEXT NOT NULL,
  policy_status TEXT NOT NULL CHECK (policy_status IN ('DRAFT','PUBLISHED','RETIRED')),
  source_name TEXT NOT NULL,
  source_sha256 TEXT NOT NULL,
  policy_notes TEXT NOT NULL DEFAULT '',
  created_by_user_id UUID NULL REFERENCES app_users(user_id),
  published_by_user_id UUID NULL REFERENCES app_users(user_id),
  restored_from_policy_version_id UUID NULL REFERENCES scoped_role_policy_versions(policy_version_id),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  published_at TIMESTAMPTZ NULL,
  retired_at TIMESTAMPTZ NULL
);
CREATE UNIQUE INDEX ux_scoped_role_policy_one_published
  ON scoped_role_policy_versions ((policy_status)) WHERE policy_status='PUBLISHED';
CREATE TABLE scoped_role_policy_grants (
  scoped_role_policy_grant_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  policy_version_id UUID NOT NULL REFERENCES scoped_role_policy_versions(policy_version_id),
  role_code TEXT NOT NULL,
  module_code TEXT NOT NULL REFERENCES scoped_role_policy_modules(module_code),
  action_code TEXT NOT NULL REFERENCES scoped_role_policy_actions(action_code),
  scope_code TEXT NOT NULL REFERENCES scoped_role_policy_scopes(scope_code),
  grant_effect TEXT NOT NULL CHECK (grant_effect IN ('GRANT','DENY')),
  conditions JSONB NOT NULL DEFAULT '{}'::jsonb,
  delegated_authority BOOLEAN NOT NULL DEFAULT FALSE,
  reason_required BOOLEAN NOT NULL DEFAULT FALSE,
  audit_required BOOLEAN NOT NULL DEFAULT TRUE,
  source_designation TEXT NOT NULL,
  source_notes TEXT NOT NULL DEFAULT '',
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  UNIQUE (policy_version_id, role_code, module_code, action_code, scope_code, grant_effect)
);

CREATE OR REPLACE FUNCTION projectpulse040_block_immutable_audit_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'Scoped RBAC audit evidence is immutable.';
END;
$$;
CREATE OR REPLACE FUNCTION projectpulse040_block_published_grant_mutation()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_status TEXT;
BEGIN
  SELECT policy_status INTO v_status
  FROM scoped_role_policy_versions
  WHERE policy_version_id=COALESCE(OLD.policy_version_id, NEW.policy_version_id);
  IF v_status IN ('PUBLISHED','RETIRED') THEN
    RAISE EXCEPTION 'Published or retired scoped policy grants are immutable.';
  END IF;
  RETURN COALESCE(NEW, OLD);
END;
$$;
CREATE TRIGGER trg_projectpulse040_published_grants_immutable
BEFORE UPDATE OR DELETE ON scoped_role_policy_grants
FOR EACH ROW EXECUTE FUNCTION projectpulse040_block_published_grant_mutation();

CREATE TABLE module001_timesheet_entry_associations (
  time_entry_id UUID PRIMARY KEY REFERENCES time_entries(time_entry_id) ON DELETE CASCADE,
  project_id UUID NULL REFERENCES projects(project_id),
  task_id UUID NULL REFERENCES project_tasks(task_id),
  assignment_id UUID NULL REFERENCES project_assignments(project_assignment_id),
  non_project_time_category_id UUID NULL,
  association_source VARCHAR(50) NOT NULL DEFAULT 'EXISTING_ENTRY',
  created_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
  updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
  CONSTRAINT chk_module001_association_source CHECK (
    association_source IN ('EXISTING_ENTRY','WORK_QUEUE','TIMER','CALENDAR')
  )
);

INSERT INTO app_users (user_id, email, display_name) VALUES
  ('10000000-0000-0000-0000-000000000001','ptc@example.test','PTC Test'),
  ('10000000-0000-0000-0000-000000000002','engineer@example.test','Engineer Test');
INSERT INTO projects (project_id, project_code, project_name)
VALUES ('20000000-0000-0000-0000-000000000001','P-001','Test Project');
INSERT INTO project_tasks (task_id, project_id, task_code, task_name)
VALUES ('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','T-001','Test Task');
INSERT INTO project_assignments (
  project_assignment_id, project_id, task_id, user_id, assigned_by_user_id, effective_start_date
) VALUES (
  '40000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000002',
  '10000000-0000-0000-0000-000000000001',
  '2026-01-01'
);
INSERT INTO timesheets (timesheet_id, user_id, week_start_date, week_end_date)
VALUES ('50000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002','2026-07-19','2026-07-25');
INSERT INTO time_entries (
  time_entry_id, timesheet_id, user_id, project_id, task_id, work_date, hours, description
) VALUES (
  '60000000-0000-0000-0000-000000000001',
  '50000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000002',
  '20000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  '2026-07-20', 8, 'Test entry'
);
INSERT INTO scoped_role_policy_modules
  (module_code,module_name,route_scope,current_state)
VALUES ('001','Timesheet','timesheet','Installed');
INSERT INTO scoped_role_policy_scopes
  (scope_code,scope_description)
VALUES ('ORGANIZATION','Organization-wide scope.');
INSERT INTO scoped_role_policy_actions
  (action_code,action_description,is_non_bypassable)
VALUES
  ('MODULE_VIEW','View module',FALSE),
  ('TIME_VIEW','View time',FALSE),
  ('TIME_REOPEN','Reopen time',FALSE),
  ('TIME_CORRECT_ON_BEHALF','Correct time',FALSE),
  ('TIME_REASSIGN','Move time',FALSE),
  ('TIME_SUBMIT','Submit time',FALSE),
  ('TIME_DELETE_PERMANENT','Permanent delete',TRUE),
  ('USER_IMPERSONATE','Impersonate user',TRUE),
  ('SYSTEM_CONFIGURE','Configure system',TRUE),
  ('AUDIT_VIEW','View audit',FALSE),
  ('AUDIT_RECORD','Record audit',FALSE);
INSERT INTO scoped_role_policy_versions (
  policy_version_id,version_number,policy_name,policy_status,source_name,source_sha256,published_at
) VALUES (
  '70000000-0000-0000-0000-000000000001',1,'Baseline','PUBLISHED','baseline','baseline',NOW()
);
INSERT INTO scoped_role_policy_grants (
  policy_version_id,role_code,module_code,action_code,scope_code,grant_effect,
  source_designation,source_notes
) VALUES (
  '70000000-0000-0000-0000-000000000001','ENGINEERING','001','TIME_VIEW','ORGANIZATION','GRANT','View','baseline'
);
SQL

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='043_ptc_time_steward_permissions';")" migration_registered
assert_eq 2 "$(value 'SELECT COUNT(*) FROM scoped_role_policy_versions;')" immutable_policy_version_created
assert_eq 2 "$(value "SELECT version_number FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED';")" new_policy_published
assert_eq RETIRED "$(value "SELECT policy_status FROM scoped_role_policy_versions WHERE version_number=1;")" prior_policy_retired
assert_eq 5 "$(value "SELECT COUNT(*) FROM scoped_role_policy_actions WHERE action_code IN ('TIME_VIEW_ON_BEHALF','TIME_UNSUBMIT','TIME_DELETE_ON_BEHALF','TIME_TASK_CREATE','TIME_TASK_ASSIGN');")" new_actions_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM scoped_role_policy_grants g JOIN scoped_role_policy_versions v USING(policy_version_id) WHERE v.policy_status='PUBLISHED' AND role_code='PROJECT_TEAM_COORDINATOR' AND module_code='001' AND action_code='TIME_SUBMIT' AND grant_effect='DENY';")" submit_on_behalf_denied
assert_eq 0 "$(value "SELECT COUNT(*) FROM scoped_role_policy_grants g JOIN scoped_role_policy_versions v USING(policy_version_id) WHERE v.policy_status='PUBLISHED' AND role_code='PROJECT_TEAM_COORDINATOR' AND module_code='001' AND action_code='TIME_SUBMIT' AND grant_effect='GRANT';")" submit_on_behalf_not_granted
assert_eq 1 "$(value "SELECT COUNT(*) FROM scoped_role_policy_grants g JOIN scoped_role_policy_versions v USING(policy_version_id) WHERE v.policy_status='PUBLISHED' AND role_code='PROJECT_TEAM_COORDINATOR' AND action_code='TIME_DELETE_PERMANENT' AND grant_effect='DENY';")" permanent_delete_denied
assert_eq 7 "$(value "SELECT COUNT(*) FROM scoped_role_policy_grants g JOIN scoped_role_policy_versions v USING(policy_version_id) WHERE v.policy_status='PUBLISHED' AND role_code='PROJECT_TEAM_COORDINATOR' AND action_code IN ('TIME_VIEW_ON_BEHALF','TIME_UNSUBMIT','TIME_CORRECT_ON_BEHALF','TIME_REASSIGN','TIME_DELETE_ON_BEHALF','TIME_TASK_CREATE','TIME_TASK_ASSIGN') AND grant_effect='GRANT';")" operational_actions_granted

psql_exec -c "INSERT INTO module001_timesheet_entry_associations (time_entry_id,project_id,task_id,assignment_id,association_source,created_by_user_id,updated_by_user_id) VALUES ('60000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','PTC_TIME_STEWARD','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');" >/dev/null
assert_eq PTC_TIME_STEWARD "$(value "SELECT association_source FROM module001_timesheet_entry_associations WHERE time_entry_id='60000000-0000-0000-0000-000000000001';")" ptc_association_source_allowed

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 2 "$(value 'SELECT COUNT(*) FROM scoped_role_policy_versions;')" migration_idempotent

psql_exec -c "INSERT INTO scoped_time_management_events (action_code,actor_user_id,target_user_id,timesheet_id,time_entry_id,project_id,task_id,reason) VALUES ('TIME_CORRECT_ON_BEHALF','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002','50000000-0000-0000-0000-000000000001','60000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','Test correction');" >/dev/null
if psql_exec -c "UPDATE scoped_time_management_events SET reason='tampered';" >/tmp/ptc-043-audit-update.log 2>&1; then
  echo 'ASSERTION_FAILED immutable_audit_update_succeeded' >&2
  exit 1
fi
echo 'ASSERTION_PASSED immutable_audit_update_blocked'
if psql_exec -f "$ROLLBACK" >/tmp/ptc-043-operational-rollback.log 2>&1; then
  echo 'ASSERTION_FAILED operational_rollback_succeeded' >&2
  exit 1
fi
echo 'ASSERTION_PASSED operational_rollback_blocked'

psql_exec -c 'TRUNCATE TABLE scoped_time_management_events;' >/dev/null
psql_exec -c "DELETE FROM module001_timesheet_entry_associations WHERE time_entry_id='60000000-0000-0000-0000-000000000001';" >/dev/null
psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 1 "$(value "SELECT version_number FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED';")" prior_policy_restored
assert_eq 1 "$(value 'SELECT COUNT(*) FROM scoped_role_policy_versions;')" migration_policy_removed
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='043_ptc_time_steward_permissions';")" migration_unregistered
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.scoped_time_management_events')::text,'');")" audit_table_removed
assert_eq 0 "$(value "SELECT COUNT(*) FROM scoped_role_policy_actions WHERE action_code IN ('TIME_VIEW_ON_BEHALF','TIME_UNSUBMIT','TIME_DELETE_ON_BEHALF','TIME_TASK_CREATE','TIME_TASK_ASSIGN');")" new_actions_removed

if psql_exec -c "INSERT INTO module001_timesheet_entry_associations (time_entry_id,project_id,task_id,assignment_id,association_source,created_by_user_id,updated_by_user_id) VALUES ('60000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','PTC_TIME_STEWARD','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');" >/tmp/ptc-043-association-rollback.log 2>&1; then
  echo 'ASSERTION_FAILED rollback_association_constraint_not_restored' >&2
  exit 1
fi
echo 'ASSERTION_PASSED rollback_association_constraint_restored'

echo 'PTC_TIME_STEWARD_MIGRATION_043_TEST=PASS'
