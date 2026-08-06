#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-flowhive-074-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/074_module_066_project_flowhive_production.sql"
ROLLBACK="/workspace/database/rollback/074_module_066_project_flowhive_production_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() { docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"; }
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }
  echo "ASSERTION_PASSED $label=$actual"
}
expect_failure() {
  local log="/tmp/flowhive-074-$1.log" expected="$2"
  shift 2
  if "$@" >"$log" 2>&1; then echo "ASSERTION_FAILED expected_failure=$expected" >&2; exit 1; fi
  grep -Fq "$expected" "$log" || { cat "$log" >&2; exit 1; }
}

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
CREATE TABLE app_users(user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),email TEXT NOT NULL UNIQUE,display_name TEXT NOT NULL,is_active BOOLEAN NOT NULL DEFAULT TRUE);
CREATE TABLE app_roles(app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),role_code TEXT NOT NULL UNIQUE,role_name TEXT NOT NULL,is_active BOOLEAN NOT NULL DEFAULT TRUE);
CREATE TABLE app_permissions(app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),permission_code VARCHAR(100) NOT NULL UNIQUE,permission_name TEXT NOT NULL,module_code TEXT NOT NULL,permission_description TEXT NOT NULL DEFAULT '');
CREATE TABLE app_role_permissions(app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),app_role_id UUID NOT NULL REFERENCES app_roles,app_permission_id UUID NOT NULL REFERENCES app_permissions,created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),UNIQUE(app_role_id,app_permission_id));
CREATE TABLE app_feature_catalog(feature_code VARCHAR(100) PRIMARY KEY,feature_name TEXT NOT NULL,module_code TEXT NOT NULL,route_anchor TEXT NOT NULL,required_permission_code TEXT NOT NULL,feature_description TEXT NOT NULL DEFAULT '',display_order INTEGER NOT NULL DEFAULT 0,is_active BOOLEAN NOT NULL DEFAULT TRUE,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
CREATE TABLE projects(project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_code TEXT NOT NULL UNIQUE,project_name TEXT NOT NULL);
CREATE TABLE project_tasks(task_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_id UUID NOT NULL REFERENCES projects,task_code TEXT NOT NULL,task_name TEXT NOT NULL);
CREATE TABLE project_assignments(project_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),project_id UUID NOT NULL REFERENCES projects,task_id UUID NULL REFERENCES project_tasks,user_id UUID NOT NULL REFERENCES app_users);
CREATE TABLE ai_capability_routes(feature_code TEXT PRIMARY KEY,route_targets JSONB NOT NULL DEFAULT '[]',external_context_policy TEXT NOT NULL DEFAULT 'private_only',revision INTEGER NOT NULL DEFAULT 1,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),updated_by UUID NULL);
INSERT INTO app_users(user_id,email,display_name) VALUES('10000000-0000-0000-0000-000000000001','admin@example.test','Admin');
INSERT INTO projects(project_id,project_code,project_name) VALUES('20000000-0000-0000-0000-000000000001','FH-074','FlowHive migration validation');
INSERT INTO app_roles(role_code,role_name) SELECT code,replace(code,'_',' ') FROM unnest(ARRAY['SUPER_ADMINISTRATOR','PROJECT_MANAGER','ENGINEER']) code;
INSERT INTO app_permissions(permission_code,permission_name,module_code,permission_description)
VALUES('LEGACY_CELAR_LABEL','Pulse AI Private Search','011','Pulse AI visible compatibility label');
INSERT INTO app_feature_catalog(feature_code,feature_name,module_code,route_anchor,required_permission_code,feature_description)
VALUES('LEGACY_CELAR_FEATURE','Pulse AI Search','011','#celar-ai','LEGACY_CELAR_LABEL','Pulse AI visible feature description');
SQL

psql_exec -f "$MIGRATION" >/dev/null
first_applied="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='074_module_066_project_flowhive_production'")"
psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='074_module_066_project_flowhive_production'")" migration_registered_once
assert_eq "$first_applied" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='074_module_066_project_flowhive_production'")" migration_timestamp_immutable
assert_eq 4 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename LIKE 'project_flowhive_%' AND tablename NOT LIKE '%074%'")" production_tables
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_permissions WHERE module_code='066'")" flowhive_permissions
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_name ILIKE '%Pulse AI%' OR permission_description ILIKE '%Pulse AI%'")" retired_permission_labels_removed
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_name ILIKE '%Pulse AI%' OR feature_description ILIKE '%Pulse AI%'")" retired_feature_labels_removed

psql_exec <<'SQL'
INSERT INTO project_flowhive_plans(plan_id,project_id,plan_name,created_by_user_id,updated_by_user_id)
VALUES('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','Migration validation plan','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');
INSERT INTO project_flowhive_plan_versions(plan_id,project_id,version_number,plan_payload,schedule_payload,validation_payload,created_by_user_id)
VALUES('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001',1,'{}','{}','{}','10000000-0000-0000-0000-000000000001');
SQL
expect_failure immutable_version 'immutable' psql_exec -qc "UPDATE project_flowhive_plan_versions SET plan_payload='{\"changed\":true}' WHERE version_number=1"
expect_failure guarded_rollback 'Rollback refused: Project FlowHive versions exist.' psql_exec -f "$ROLLBACK"
assert_eq 1 "$(value "SELECT COUNT(*) FROM project_flowhive_plan_versions")" rollback_preserved_evidence

psql_exec -qc 'TRUNCATE project_flowhive_plan_versions,project_flowhive_plans CASCADE;'
psql_exec -f "$ROLLBACK" >/dev/null
assert_eq '' "$(value "SELECT to_regclass('public.project_flowhive_plans')")" clean_rollback_removed_tables
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='074_module_066_project_flowhive_production'")" clean_rollback_removed_ledger

echo 'MODULE_066_PROJECT_FLOWHIVE_MIGRATION_074=PASS'
