#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-module001-041-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/041_module_001_timesheet_timer_and_task_association.sql"
ROLLBACK="/workspace/database/rollback/041_module_001_timesheet_timer_and_task_association_rollback.sql"

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
  local label="$1" sql="$2"
  if psql_exec -c "$sql" >/tmp/module001-041-expected-failure.log 2>&1; then
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
CREATE ROLE ptp_app;

CREATE TABLE schema_migrations (
  migration_id TEXT PRIMARY KEY,
  description TEXT NOT NULL DEFAULT '',
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO schema_migrations (migration_id, description)
VALUES ('040_scoped_role_policy_versions', 'test prerequisite');

CREATE TABLE app_users (
  user_id UUID PRIMARY KEY,
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
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
  status TEXT NOT NULL DEFAULT 'active',
  billable BOOLEAN NOT NULL DEFAULT TRUE,
  project_manager_user_id UUID NULL REFERENCES app_users(user_id)
);
CREATE TABLE project_tasks (
  task_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(project_id),
  task_code TEXT NOT NULL,
  task_name TEXT NOT NULL,
  task_description TEXT NULL,
  billable BOOLEAN NOT NULL DEFAULT TRUE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE project_assignments (
  project_assignment_id UUID PRIMARY KEY,
  project_id UUID NOT NULL REFERENCES projects(project_id),
  task_id UUID NOT NULL REFERENCES project_tasks(task_id),
  user_id UUID NOT NULL REFERENCES app_users(user_id),
  effective_start_date DATE NOT NULL,
  effective_end_date DATE NULL,
  assigned_hours NUMERIC(10,2) NOT NULL DEFAULT 0
);
CREATE TABLE non_project_time_categories (
  non_project_time_category_id UUID PRIMARY KEY,
  category_code TEXT NOT NULL UNIQUE,
  category_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE timesheets (
  timesheet_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES app_users(user_id),
  week_start_date DATE NOT NULL,
  week_end_date DATE NOT NULL,
  status TEXT NOT NULL DEFAULT 'draft',
  submitted_at TIMESTAMPTZ NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (user_id, week_start_date)
);
CREATE TABLE time_entries (
  time_entry_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  timesheet_id UUID NOT NULL REFERENCES timesheets(timesheet_id),
  user_id UUID NOT NULL REFERENCES app_users(user_id),
  project_id UUID NULL REFERENCES projects(project_id),
  task_id UUID NULL REFERENCES project_tasks(task_id),
  non_project_time_category_id UUID NULL REFERENCES non_project_time_categories(non_project_time_category_id),
  work_date DATE NOT NULL,
  time_type TEXT NOT NULL DEFAULT 'normal',
  hours NUMERIC(6,2) NOT NULL DEFAULT 0,
  description TEXT NULL,
  billable BOOLEAN NOT NULL DEFAULT FALSE,
  status TEXT NOT NULL DEFAULT 'draft',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE scoped_role_policy_modules (
  module_code TEXT PRIMARY KEY,
  module_name TEXT NOT NULL,
  permission_notes TEXT NOT NULL DEFAULT ''
);
INSERT INTO scoped_role_policy_modules (module_code, module_name)
VALUES ('001', 'Time Entry');

INSERT INTO app_users (user_id, email, display_name)
VALUES ('10000000-0000-0000-0000-000000000001', 'engineer@example.test', 'Engineer Test');
INSERT INTO clients (client_id, client_name)
VALUES ('20000000-0000-0000-0000-000000000001', 'Test Customer');
INSERT INTO projects (project_id, client_id, project_code, project_name)
VALUES ('30000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', 'P-001', 'Test Project');
INSERT INTO project_tasks (task_id, project_id, task_code, task_name)
VALUES ('40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', 'T-001', 'Test Task');
INSERT INTO project_assignments (
  project_assignment_id, project_id, task_id, user_id, effective_start_date, assigned_hours
) VALUES (
  '50000000-0000-0000-0000-000000000001',
  '30000000-0000-0000-0000-000000000001',
  '40000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '2026-01-01', 40
);
INSERT INTO non_project_time_categories (
  non_project_time_category_id, category_code, category_name
) VALUES (
  '60000000-0000-0000-0000-000000000001', 'ADMIN', 'Administrative'
);
SQL

legacy_users="$(value 'SELECT COUNT(*) FROM app_users;')"
legacy_projects="$(value 'SELECT COUNT(*) FROM projects;')"
legacy_tasks="$(value 'SELECT COUNT(*) FROM project_tasks;')"
legacy_assignments="$(value 'SELECT COUNT(*) FROM project_assignments;')"

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='041_module_001_timesheet_timer_and_task_association';")" migration_registered_once
assert_eq Timesheet "$(value "SELECT module_name FROM scoped_role_policy_modules WHERE module_code='001';")" module001_renamed
assert_eq 1 "$(value "SELECT COUNT(*) FROM information_schema.tables WHERE table_name='module001_timer_sessions';")" timer_table_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM information_schema.tables WHERE table_name='module001_weekly_task_lines';")" weekly_line_table_created
assert_eq "$legacy_users" "$(value 'SELECT COUNT(*) FROM app_users;')" legacy_users_preserved
assert_eq "$legacy_projects" "$(value 'SELECT COUNT(*) FROM projects;')" legacy_projects_preserved
assert_eq "$legacy_tasks" "$(value 'SELECT COUNT(*) FROM project_tasks;')" legacy_tasks_preserved
assert_eq "$legacy_assignments" "$(value 'SELECT COUNT(*) FROM project_assignments;')" legacy_assignments_preserved

psql_exec <<'SQL'
INSERT INTO module001_timer_sessions (
  timer_session_id, user_id, week_start_date, entry_date,
  non_project_time_category_id, time_classification, time_zone_id,
  started_at_utc, timer_status, created_by_user_id, updated_by_user_id
) VALUES (
  '70000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '2026-07-19', '2026-07-20',
  '60000000-0000-0000-0000-000000000001',
  'normal', 'UTC', '2026-07-20T12:00:00Z', 'RUNNING',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001'
);
SQL

expect_failure one_running_timer_per_user "INSERT INTO module001_timer_sessions (user_id, week_start_date, entry_date, non_project_time_category_id, started_at_utc, timer_status, created_by_user_id, updated_by_user_id) VALUES ('10000000-0000-0000-0000-000000000001','2026-07-19','2026-07-20','60000000-0000-0000-0000-000000000001',NOW(),'RUNNING','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');"
expect_failure duration_above_12_hours "INSERT INTO module001_timer_sessions (user_id, week_start_date, entry_date, non_project_time_category_id, started_at_utc, stopped_at_utc, effective_stopped_at_utc, actual_elapsed_seconds, rounded_minutes, timer_status, created_by_user_id, updated_by_user_id) VALUES ('10000000-0000-0000-0000-000000000001','2026-07-19','2026-07-20','60000000-0000-0000-0000-000000000001',NOW()-INTERVAL '13 hours',NOW(),NOW(),46800,720,'STOPPED_DRAFT','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');"
expect_failure non_quarter_rounded_minutes "INSERT INTO module001_timer_sessions (user_id, week_start_date, entry_date, non_project_time_category_id, started_at_utc, stopped_at_utc, effective_stopped_at_utc, actual_elapsed_seconds, rounded_minutes, timer_status, created_by_user_id, updated_by_user_id) VALUES ('10000000-0000-0000-0000-000000000001','2026-07-19','2026-07-20','60000000-0000-0000-0000-000000000001',NOW()-INTERVAL '1 hour',NOW(),NOW(),3600,61,'STOPPED_DRAFT','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');"

psql_exec -c "DELETE FROM module001_timer_sessions;" >/dev/null
psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.module001_timer_sessions')::text,'');")" rollback_removed_timer_table
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='041_module_001_timesheet_timer_and_task_association';")" rollback_unregistered_migration
assert_eq Time Entry "$(value "SELECT module_name FROM scoped_role_policy_modules WHERE module_code='001';")" rollback_restored_module_name
assert_eq "$legacy_assignments" "$(value 'SELECT COUNT(*) FROM project_assignments;')" rollback_preserved_assignments

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -c "INSERT INTO module001_weekly_task_lines (user_id, week_start_date, project_id, task_id, assignment_id, activity_type, created_by_user_id, updated_by_user_id) VALUES ('10000000-0000-0000-0000-000000000001','2026-07-19','30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001','PROJECT_TASK','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');" >/dev/null
if psql_exec -f "$ROLLBACK" >/tmp/module001-041-rollback-blocked.log 2>&1; then
  echo 'ASSERTION_FAILED rollback_with_operational_data_was_allowed' >&2
  exit 1
fi
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='041_module_001_timesheet_timer_and_task_association';")" blocked_rollback_keeps_migration
assert_eq 1 "$(value 'SELECT COUNT(*) FROM module001_weekly_task_lines;')" blocked_rollback_keeps_operational_data

echo 'MODULE_001_MIGRATION_041_DATABASE_TEST=PASS'
