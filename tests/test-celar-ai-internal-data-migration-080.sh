#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-celar-internal-080-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/080_celar_ai_internal_data_intelligence.sql"
ROLLBACK="/workspace/database/rollback/080_celar_ai_internal_data_intelligence_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() { docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"; }
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() { local expected="$1" actual="$2" label="$3"; [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }; echo "ASSERTION_PASSED $label=$actual"; }
expect_failure() { local label="$1" expected="$2" log="/tmp/celar-internal-080-$1.log"; shift 2; if "$@" >"$log" 2>&1; then echo "ASSERTION_FAILED expected_failure=$label" >&2; exit 1; fi; grep -Fq "$expected" "$log" || { cat "$log" >&2; exit 1; }; echo "ASSERTION_PASSED $label=REJECTED"; }

docker run --detach --rm --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" -e POSTGRES_PASSWORD="$DB_PASSWORD" -e POSTGRES_DB="$DB_NAME" \
  -p 127.0.0.1::5432 \
  -v "$ROOT:/workspace:ro" postgres:16-alpine >/dev/null
for attempt in $(seq 1 60); do
  psql_exec -Atqc 'SELECT 1' >/dev/null 2>&1 && break
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE ROLE ptp_app NOLOGIN;
CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT NOT NULL DEFAULT '',applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE app_users(
 user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),email TEXT NOT NULL UNIQUE,display_name TEXT NOT NULL,
 team_name TEXT,department_name TEXT,department TEXT,is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE projects(
 project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_code TEXT NOT NULL UNIQUE,project_name TEXT NOT NULL,
 project_manager_user_id UUID REFERENCES app_users,status TEXT NOT NULL DEFAULT 'active'
);
CREATE TABLE project_tasks(
 task_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_id UUID NOT NULL REFERENCES projects,
 task_code TEXT NOT NULL,task_name TEXT NOT NULL,is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE project_assignments(
 project_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_id UUID NOT NULL REFERENCES projects,
 task_id UUID REFERENCES project_tasks,user_id UUID NOT NULL REFERENCES app_users,
 effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,effective_end_date DATE,
 assigned_hours NUMERIC(12,2) NOT NULL DEFAULT 0,module001a_closeout_status TEXT NOT NULL DEFAULT 'active'
);
CREATE TABLE reporting_relationships(
 reporting_relationship_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),employee_user_id UUID NOT NULL REFERENCES app_users,
 manager_user_id UUID REFERENCES app_users,team_lead_user_id UUID REFERENCES app_users,
 effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,effective_end_date DATE
);
CREATE TABLE projectpulse_team_scope_assignments(
 team_scope_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),scoped_user_id UUID NOT NULL REFERENCES app_users,
 team_name TEXT,department_name TEXT,manager_user_id UUID REFERENCES app_users,is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE engineering_resource_requests(
 engineering_resource_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_id UUID REFERENCES projects,
 request_status TEXT NOT NULL DEFAULT 'requested',target_start_date DATE,target_end_date DATE
);
CREATE TABLE engineering_resource_request_assignments(
 engineering_resource_request_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
 engineering_resource_request_id UUID NOT NULL REFERENCES engineering_resource_requests,user_id UUID NOT NULL REFERENCES app_users,
 assignment_status TEXT NOT NULL DEFAULT 'proposed'
);
CREATE TABLE work_register_task_assignment_history(
 work_register_task_assignment_history_id UUID PRIMARY KEY,project_id UUID NOT NULL,task_id_text TEXT NOT NULL,
 task_name_snapshot TEXT NOT NULL DEFAULT '',assigned_user_id UUID,allocated_hours NUMERIC(12,2),
 assignment_status TEXT NOT NULL DEFAULT 'active',effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,effective_end_date DATE
);
INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Admin User'),
 ('10000000-0000-0000-0000-000000000002','kevin.damish@example.test','Kevin Damish');
SQL

psql_exec -f "$MIGRATION" >/dev/null
first_applied="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='080_celar_ai_internal_data_intelligence'")"
psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='080_celar_ai_internal_data_intelligence'")" migration_registered_once
assert_eq "$first_applied" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='080_celar_ai_internal_data_intelligence'")" migration_timestamp_idempotent
assert_eq t "$(value "SELECT has_table_privilege('ptp_app','celar_ai_identity_aliases','SELECT')")" runtime_role_select_granted
assert_eq ix_celar_ai_current_roster_person_project "$(value "SELECT to_regclass('public.ix_celar_ai_current_roster_person_project')")" roster_lookup_index_created

assert_eq 1 "$(value "SELECT COUNT(*) FROM celar_ai_identity_aliases WHERE alias_text='Kevin Damisch' AND is_verified=TRUE AND verification_source='migration_080_known_directory_correction' AND is_active=TRUE")" known_verified_alias_seeded

psql_exec <<'SQL'
INSERT INTO projects(project_id,project_code,project_name,project_manager_user_id,status) VALUES
 ('20000000-0000-0000-0000-000000000001','P-A','Canonical and roster project','10000000-0000-0000-0000-000000000001','active'),
 ('20000000-0000-0000-0000-000000000002','P-B','Roster-only project','10000000-0000-0000-0000-000000000001','active'),
 ('20000000-0000-0000-0000-000000000003','P-C','Resource-request project','10000000-0000-0000-0000-000000000001','active'),
 ('20000000-0000-0000-0000-000000000004','P-D','Kevin managed project','10000000-0000-0000-0000-000000000002','active'),
 ('20000000-0000-0000-0000-000000000005','P-E','Closed project','10000000-0000-0000-0000-000000000001','completed');
INSERT INTO project_tasks(task_id,project_id,task_code,task_name,is_active) VALUES
 ('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','TASK-A','Mirrored task',TRUE),
 ('30000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000002','TASK-B','Roster task',TRUE),
 ('30000000-0000-0000-0000-000000000005','20000000-0000-0000-0000-000000000005','TASK-E','Closed project task',TRUE);
INSERT INTO project_assignments(project_assignment_id,project_id,task_id,user_id,effective_start_date,assigned_hours,module001a_closeout_status) VALUES
 ('40000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000002','2026-01-01',4,'active'),
 ('40000000-0000-0000-0000-000000000005','20000000-0000-0000-0000-000000000005','30000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000002','2026-01-01',2,'active');
INSERT INTO work_register_task_assignment_history(work_register_task_assignment_history_id,project_id,task_id_text,task_name_snapshot,assigned_user_id,allocated_hours,assignment_status,effective_start_date) VALUES
 ('50000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','Mirrored task','10000000-0000-0000-0000-000000000002',8,'active','2026-01-01'),
 ('50000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002','Roster task','10000000-0000-0000-0000-000000000002',16,'active','2026-01-01'),
 ('50000000-0000-0000-0000-000000000005','20000000-0000-0000-0000-000000000005','30000000-0000-0000-0000-000000000005','Closed project task','10000000-0000-0000-0000-000000000002',2,'active','2026-01-01');
INSERT INTO engineering_resource_requests(engineering_resource_request_id,project_id,request_status,target_start_date) VALUES
 ('60000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000003','assigned','2026-01-01');
INSERT INTO engineering_resource_request_assignments(engineering_resource_request_assignment_id,engineering_resource_request_id,user_id,assignment_status) VALUES
 ('70000000-0000-0000-0000-000000000003','60000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000002','assigned');
SQL

HOST_PORT="$(docker port "$CONTAINER" 5432/tcp | head -n 1)"
HOST_PORT="${HOST_PORT##*:}"
CELAR_AI_TEST_CONNECTION_STRING="Host=127.0.0.1;Port=$HOST_PORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD" \
  dotnet run --project "$ROOT/tests/CelarAiInternalDataTests/CelarAiInternalDataTests.csproj" --configuration Release -p:ProjectPulseSourceRevision=1111111111111111111111111111111111111111

expect_failure normalized_duplicate 'duplicate key value' psql_exec -qc "INSERT INTO celar_ai_identity_aliases(user_id,alias_text) VALUES('10000000-0000-0000-0000-000000000002',' Kevin-Damisch ')"
expect_failure invalid_verification 'chk_celar_ai_identity_alias_verification' psql_exec -qc "INSERT INTO celar_ai_identity_aliases(user_id,alias_text,is_verified) VALUES('10000000-0000-0000-0000-000000000002','K. Damisch',TRUE)"
psql_exec -qc "INSERT INTO celar_ai_identity_aliases(user_id,alias_text,alias_type,created_by_user_id) VALUES('10000000-0000-0000-0000-000000000002','K Damish','preferred_name','10000000-0000-0000-0000-000000000001')"
expect_failure guarded_rollback 'Rollback refused' psql_exec -f "$ROLLBACK"
assert_eq 2 "$(value "SELECT COUNT(*) FROM celar_ai_identity_aliases")" guarded_rollback_preserved_aliases

psql_exec -qc 'TRUNCATE celar_ai_identity_aliases;'
psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT to_regclass('public.celar_ai_identity_aliases')")" clean_rollback_removed_alias_table
assert_eq '' "$(value "SELECT to_regclass('public.ix_celar_ai_current_roster_person_project')")" clean_rollback_removed_roster_index
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='080_celar_ai_internal_data_intelligence'")" clean_rollback_removed_ledger
echo 'CELAR_AI_INTERNAL_DATA_MIGRATION_080=PASS'
