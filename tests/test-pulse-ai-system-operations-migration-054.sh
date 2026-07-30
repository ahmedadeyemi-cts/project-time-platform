#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-pulse-ai-054-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/054_pulse_ai_system_operations_copilot.sql"
ROLLBACK="/workspace/database/rollback/054_pulse_ai_system_operations_copilot_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}
expect_sql_failure() {
  local sql="$1"
  local expected="$2"
  local label="$3"
  local log="/tmp/pulse-ai-054-${label}.log"
  if psql_exec -c "$sql" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || {
    echo "ASSERTION_FAILED $label missing_expected_error=$expected" >&2
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
  role_code TEXT NOT NULL UNIQUE,
  role_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE app_permissions (
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code TEXT NOT NULL UNIQUE,
  permission_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  permission_description TEXT NOT NULL DEFAULT ''
);
CREATE TABLE app_role_permissions (
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
  app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
  UNIQUE(app_role_id, app_permission_id)
);
CREATE TABLE app_feature_catalog (
  app_feature_catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  feature_code TEXT NOT NULL UNIQUE,
  feature_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  route_anchor TEXT,
  required_permission_code TEXT,
  feature_description TEXT,
  display_order INTEGER NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Administrator'),
 ('10000000-0000-0000-0000-000000000002','analyst@example.test','Security Analyst'),
 ('10000000-0000-0000-0000-000000000003','pm@example.test','Project Manager');
INSERT INTO app_roles(role_code,role_name) VALUES
 ('SUPER_ADMINISTRATOR','Super Administrator'),
 ('ADMINISTRATOR','Administrator'),
 ('SYSTEM_ADMINISTRATOR','System Administrator'),
 ('SECURITY_ADMINISTRATOR','Security Administrator'),
 ('SECURITY_OPERATIONS','Security Operations'),
 ('SECURITY_ANALYST','Security Analyst'),
 ('PROJECT_MANAGER','Project Manager'),
 ('ENGINEERING_LEAD','Engineering Lead'),
 ('FINANCE','Finance');
SQL

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_pulse_ai_system_operations_copilot';")" migration_registered_once
assert_eq 3 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('pulse_ai_system_operations_investigations','pulse_ai_system_operations_evidence','pulse_ai_future_enhancement_plans');")" tables_created
assert_eq 6 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('ASK_PULSE_AI_SYSTEM_OPERATIONS','VIEW_PULSE_AI_SYSTEM_OPERATIONS','RETEST_PULSE_AI_SAFE_API','VIEW_PULSE_AI_OPERATIONS_HISTORY','EXPORT_PULSE_AI_OPERATIONS_EVIDENCE','PLAN_PULSE_AI_FUTURE_ENHANCEMENT');")" permissions_created
assert_eq 3 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code IN ('PULSE_AI_UNIFIED_LIVE_ANSWER','PULSE_AI_SYSTEM_OPERATIONS_COPILOT','PULSE_AI_FUTURE_ENHANCEMENT_PLANNER');")" features_created
assert_eq 6 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SUPER_ADMINISTRATOR' AND p.module_code='011' AND p.permission_code IN ('ASK_PULSE_AI_SYSTEM_OPERATIONS','VIEW_PULSE_AI_SYSTEM_OPERATIONS','RETEST_PULSE_AI_SAFE_API','VIEW_PULSE_AI_OPERATIONS_HISTORY','EXPORT_PULSE_AI_OPERATIONS_EVIDENCE','PLAN_PULSE_AI_FUTURE_ENHANCEMENT');")" super_admin_full_permissions
assert_eq 5 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='SECURITY_ANALYST' AND p.permission_code IN ('ASK_PULSE_AI_SYSTEM_OPERATIONS','VIEW_PULSE_AI_SYSTEM_OPERATIONS','VIEW_PULSE_AI_OPERATIONS_HISTORY','EXPORT_PULSE_AI_OPERATIONS_EVIDENCE','PLAN_PULSE_AI_FUTURE_ENHANCEMENT');")" analyst_read_permissions
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='PLAN_PULSE_AI_FUTURE_ENHANCEMENT';")" pm_future_plan_permission
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_roles r JOIN app_role_permissions rp USING(app_role_id) JOIN app_permissions p USING(app_permission_id) WHERE r.role_code='PROJECT_MANAGER' AND p.permission_code='VIEW_PULSE_AI_SYSTEM_OPERATIONS';")" pm_no_privileged_operations_permission

psql_exec <<'SQL'
INSERT INTO pulse_ai_system_operations_investigations (
  pulse_ai_system_operations_investigation_id,actual_user_id,effective_user_id,
  intent_code,investigation_status,sanitized_question,question_sha256,
  classification_json,correlation_id,release_sha
) VALUES (
  '20000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'api_inventory','completed','What APIs are running?',repeat('a',64),
  '{"intent":"api_inventory"}'::jsonb,'pulse-ops-test','release-test'
);
INSERT INTO pulse_ai_system_operations_evidence (
  pulse_ai_system_operations_evidence_id,
  pulse_ai_system_operations_investigation_id,
  rank_order,evidence_type,source_module,source_name,api_id,method,path,
  evidence_status,status_code,response_time_ms,error_code,correlation_id,
  observed_at,release_sha,evidence_json
) VALUES (
  '30000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',
  1,'api_inventory','013','System Health & API Diagnostics','api-test','GET',
  '/api/test','healthy',200,10.5,'','corr-test',NOW(),'release-test',
  '{"requestBodiesReturned":false}'::jsonb
);
INSERT INTO pulse_ai_future_enhancement_plans (
  pulse_ai_future_enhancement_plan_id,actual_user_id,effective_user_id,
  title,sanitized_request,request_sha256,affected_modules_json,plan_json
) VALUES (
  '40000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000003',
  '10000000-0000-0000-0000-000000000003',
  'Future API troubleshooting enhancement','Add API troubleshooting',repeat('b',64),
  '["011","013","998"]'::jsonb,'{"implementationPerformed":false}'::jsonb
);
SQL

assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_system_operations_investigations;")" investigation_inserted
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_system_operations_evidence;")" immutable_evidence_inserted
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_future_enhancement_plans;")" future_plan_inserted
expect_sql_failure "UPDATE pulse_ai_system_operations_evidence SET evidence_status='failed' WHERE pulse_ai_system_operations_evidence_id='30000000-0000-0000-0000-000000000001';" "Pulse AI system operations evidence is immutable." immutable_evidence_update
expect_sql_failure "DELETE FROM pulse_ai_system_operations_evidence WHERE pulse_ai_system_operations_evidence_id='30000000-0000-0000-0000-000000000001';" "Pulse AI system operations evidence is immutable." immutable_evidence_delete

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_pulse_ai_system_operations_copilot';")" migration_removed
assert_eq 0 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('pulse_ai_system_operations_investigations','pulse_ai_system_operations_evidence','pulse_ai_future_enhancement_plans');")" tables_removed
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code IN ('ASK_PULSE_AI_SYSTEM_OPERATIONS','VIEW_PULSE_AI_SYSTEM_OPERATIONS','RETEST_PULSE_AI_SAFE_API','VIEW_PULSE_AI_OPERATIONS_HISTORY','EXPORT_PULSE_AI_OPERATIONS_EVIDENCE','PLAN_PULSE_AI_FUTURE_ENHANCEMENT');")" permissions_removed
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code IN ('PULSE_AI_UNIFIED_LIVE_ANSWER','PULSE_AI_SYSTEM_OPERATIONS_COPILOT','PULSE_AI_FUTURE_ENHANCEMENT_PLANNER');")" features_removed

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='054_pulse_ai_system_operations_copilot';")" migration_reapplied
assert_eq 3 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('pulse_ai_system_operations_investigations','pulse_ai_system_operations_evidence','pulse_ai_future_enhancement_plans');")" tables_recreated

echo 'PULSE_AI_SYSTEM_OPERATIONS_MIGRATION_054=PASS'
