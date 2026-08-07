#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-governance-081-082-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION_081="/workspace/database/migrations/076_module_081_lab_equipment_tracker.sql"
MIGRATION_082="/workspace/database/migrations/077_module_082_enterprise_project_risk_register.sql"
ROLLBACK_082="/workspace/database/rollback/077_module_082_enterprise_project_risk_register_rollback.sql"
ROLLBACK_081="/workspace/database/rollback/076_module_081_lab_equipment_tracker_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() { docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"; }
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() { local expected="$1" actual="$2" label="$3"; [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }; echo "ASSERTION_PASSED $label=$actual"; }
expect_failure() { local label="$1" expected="$2" log="/tmp/governance-$1.log"; shift 2; if "$@" >"$log" 2>&1; then echo "ASSERTION_FAILED expected_failure=$label" >&2; exit 1; fi; grep -Fq "$expected" "$log" || { cat "$log" >&2; exit 1; }; echo "ASSERTION_PASSED $label=REJECTED"; }

docker run --detach --rm --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" -e POSTGRES_PASSWORD="$DB_PASSWORD" -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" postgres:16-alpine >/dev/null
for attempt in $(seq 1 60); do
  psql_exec -Atqc 'SELECT 1' >/dev/null 2>&1 && break
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT NOT NULL DEFAULT '',applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE app_users(user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),email TEXT NOT NULL UNIQUE,display_name TEXT NOT NULL,team_name TEXT NOT NULL DEFAULT '',is_active BOOLEAN NOT NULL DEFAULT TRUE);
CREATE TABLE app_roles(app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),role_code TEXT NOT NULL UNIQUE,role_name TEXT NOT NULL,is_active BOOLEAN NOT NULL DEFAULT TRUE);
CREATE TABLE app_permissions(app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),permission_code VARCHAR(100) NOT NULL UNIQUE,permission_name TEXT NOT NULL,module_code TEXT NOT NULL,permission_description TEXT NOT NULL DEFAULT '');
CREATE TABLE app_role_permissions(app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),app_role_id UUID NOT NULL REFERENCES app_roles,app_permission_id UUID NOT NULL REFERENCES app_permissions,created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),UNIQUE(app_role_id,app_permission_id));
CREATE TABLE app_feature_catalog(feature_code VARCHAR(100) PRIMARY KEY,feature_name TEXT NOT NULL,module_code TEXT NOT NULL,route_anchor TEXT NOT NULL,required_permission_code TEXT NOT NULL,feature_description TEXT NOT NULL DEFAULT '',display_order INTEGER NOT NULL DEFAULT 0,is_active BOOLEAN NOT NULL DEFAULT TRUE,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE projects(project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_code TEXT NOT NULL UNIQUE,project_name TEXT NOT NULL,project_manager_user_id UUID NULL REFERENCES app_users);
CREATE TABLE project_assignments(project_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_id UUID NOT NULL REFERENCES projects,user_id UUID NOT NULL REFERENCES app_users,effective_end_date DATE NULL);
INSERT INTO app_users(user_id,email,display_name,team_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Admin','Lab Engineering'),
 ('10000000-0000-0000-0000-000000000002','pm@example.test','Project Manager','Project Management'),
 ('10000000-0000-0000-0000-000000000003','engineer@example.test','Engineer','Lab Engineering');
INSERT INTO projects(project_id,project_code,project_name,project_manager_user_id)
VALUES('20000000-0000-0000-0000-000000000001','RISK-082','Governance validation','10000000-0000-0000-0000-000000000002');
INSERT INTO project_assignments(project_id,user_id) VALUES('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000003');
INSERT INTO app_roles(role_code,role_name) SELECT code,replace(code,'_',' ') FROM unnest(ARRAY[
 'SUPER_ADMINISTRATOR','ADMINISTRATOR','PROJECT_TEAM_COORDINATOR','MANAGER','ENGINEERING_MANAGER','ENGINEERING_TEAM_LEAD','ENGINEER','ENGINEERING','SYSTEMS_ENGINEER','NETWORK_ENGINEER','PROJECT_MANAGER','PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD','PROJECT_MANAGEMENT_TEAM_LEAD','PM_TEAM_LEAD','SOLUTION_ARCHITECT','ACCOUNT_EXECUTIVE','EXECUTIVE','ACCOUNTING','SALES']) code;
SQL

psql_exec -f "$MIGRATION_081" >/dev/null
psql_exec -f "$MIGRATION_082" >/dev/null
first_081="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='076_module_081_lab_equipment_tracker'")"
first_082="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='077_module_082_enterprise_project_risk_register'")"
psql_exec -f "$MIGRATION_081" >/dev/null
psql_exec -f "$MIGRATION_082" >/dev/null
assert_eq "$first_081" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='076_module_081_lab_equipment_tracker'")" module_081_idempotent_ledger
assert_eq "$first_082" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='077_module_082_enterprise_project_risk_register'")" module_082_idempotent_ledger
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_permissions WHERE module_code='081'")" module_081_permissions
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_permissions WHERE module_code='082'")" module_082_permissions

psql_exec <<'SQL'
INSERT INTO lab_equipment(equipment_id,managing_team,equipment_name,equipment_type,lab_location,pod,rack,rack_unit_start,rack_unit_height,created_by_user_id,updated_by_user_id)
VALUES('30000000-0000-0000-0000-000000000001','Lab Engineering','Core switch','switch','GRR1','Pod A','R01',20,2,'10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');
INSERT INTO lab_ip_allocations(managing_team,lab_location,pod,network_zone,address_family,network_cidr,ip_address,prefix_length,allocation_status,created_by_user_id,updated_by_user_id)
VALUES('Lab Engineering','GRR1','Pod A','management',4,'10.20.30.0/24','10.20.30.10',24,'available','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');
INSERT INTO project_risks(risk_id,risk_number,project_id,project_code_snapshot,project_name_snapshot,risk_title,cause_statement,uncertain_event_statement,impact_statement,risk_type,category,date_identified,identified_by_user_id,risk_owner_user_id,probability_score,schedule_impact_score,response_strategy,next_review_date,created_by_user_id,updated_by_user_id)
VALUES('40000000-0000-0000-0000-000000000001',0,'20000000-0000-0000-0000-000000000001','RISK-082','Governance validation','Validation risk','Because a dependency can fail','An uncertain outage may occur','Delivery could be delayed','threat','Delivery',CURRENT_DATE,'10000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000003',4,4,'mitigate',CURRENT_DATE+30,'10000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000002');
INSERT INTO project_risk_versions(risk_id,project_id,version_number,risk_snapshot,change_reason,created_by_user_id)
SELECT risk_id,project_id,1,to_jsonb(risk),'Initial validation','10000000-0000-0000-0000-000000000002' FROM project_risks risk;
SQL
assert_eq 1 "$(value "SELECT risk_number FROM project_risks WHERE risk_id='40000000-0000-0000-0000-000000000001'")" project_risk_number_assigned
assert_eq 16 "$(value "SELECT inherent_exposure FROM project_risks WHERE risk_id='40000000-0000-0000-0000-000000000001'")" project_risk_exposure_generated
expect_failure rack_overlap 'Rack-unit placement conflicts' psql_exec -qc "INSERT INTO lab_equipment(managing_team,equipment_name,equipment_type,lab_location,pod,rack,rack_unit_start,rack_unit_height,created_by_user_id,updated_by_user_id) VALUES('Lab Engineering','Overlap device','server','GRR1','Pod A','R01',21,1,'10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001')"
expect_failure duplicate_ip 'duplicate key value' psql_exec -qc "INSERT INTO lab_ip_allocations(managing_team,lab_location,pod,network_zone,address_family,network_cidr,ip_address,prefix_length,allocation_status,created_by_user_id,updated_by_user_id) VALUES('Lab Engineering','GRR1','Pod A','management',4,'10.20.30.0/24','10.20.30.10',24,'available','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001')"
expect_failure immutable_version 'immutable' psql_exec -qc "UPDATE project_risk_versions SET risk_snapshot='{\"changed\":true}' WHERE version_number=1"
expect_failure guarded_risk_rollback 'Rollback refused' psql_exec -f "$ROLLBACK_082"
expect_failure guarded_lab_rollback 'Rollback refused' psql_exec -f "$ROLLBACK_081"

psql_exec -qc 'TRUNCATE project_risk_audit_events,project_risk_action_history,project_risk_actions,project_risk_versions,project_risks,project_risk_counters,lab_equipment_audit_events,lab_import_rows,lab_rack_reservations,lab_cable_connections,lab_ip_allocations,lab_equipment,lab_import_batches CASCADE;'
psql_exec -f "$ROLLBACK_082" >/dev/null
psql_exec -f "$ROLLBACK_081" >/dev/null
assert_eq '' "$(value "SELECT to_regclass('public.project_risks')")" module_082_clean_rollback
assert_eq '' "$(value "SELECT to_regclass('public.lab_equipment')")" module_081_clean_rollback
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id IN ('076_module_081_lab_equipment_tracker','077_module_082_enterprise_project_risk_register')")" migration_ledgers_removed
echo 'MODULES_081_082_ENTERPRISE_GOVERNANCE_MIGRATIONS=PASS'
