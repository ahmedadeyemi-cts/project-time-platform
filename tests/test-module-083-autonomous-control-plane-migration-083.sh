#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-module083-autonomy-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/083_module_083_autonomous_control_plane.sql"
ROLLBACK="/workspace/database/rollback/083_module_083_autonomous_control_plane_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() { docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" psql --no-psqlrc -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"; }
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }
  echo "ASSERTION_PASSED $label=$actual"
}
expect_failure() {
  local label="$1" expected="$2" log="/tmp/module083-autonomy-$1.log"
  shift 2
  if "$@" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label expected failure containing: $expected" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || { cat "$log" >&2; exit 1; }
  echo "ASSERTION_PASSED $label"
}

docker run --detach --rm --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  postgres:16-alpine >/dev/null

for attempt in $(seq 1 60); do
  psql_exec -Atqc 'SELECT 1' >/dev/null 2>&1 && break
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations(
  migration_id TEXT PRIMARY KEY,
  description TEXT NOT NULL DEFAULT '',
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE app_users(
  user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE app_roles(
  app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  role_code TEXT NOT NULL UNIQUE,
  role_name TEXT NOT NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
CREATE TABLE app_permissions(
  app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  permission_code VARCHAR(100) NOT NULL UNIQUE,
  permission_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  permission_description TEXT NOT NULL DEFAULT ''
);
CREATE TABLE app_role_permissions(
  app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  app_role_id UUID NOT NULL REFERENCES app_roles,
  app_permission_id UUID NOT NULL REFERENCES app_permissions,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(app_role_id,app_permission_id)
);
CREATE TABLE app_feature_catalog(
  feature_code VARCHAR(100) PRIMARY KEY,
  feature_name TEXT NOT NULL,
  module_code TEXT NOT NULL,
  route_anchor TEXT NOT NULL,
  required_permission_code TEXT NOT NULL,
  feature_description TEXT NOT NULL DEFAULT '',
  display_order INTEGER NOT NULL DEFAULT 0,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE full_future_loop_items(
  loop_id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);
CREATE TABLE full_future_loop_events(
  event_id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);
CREATE TABLE full_future_loop_artifacts(
  artifact_id UUID PRIMARY KEY DEFAULT gen_random_uuid()
);

INSERT INTO app_users(user_id,email,display_name)
VALUES
  ('10000000-0000-0000-0000-000000000001','requester@example.test','Requester'),
  ('10000000-0000-0000-0000-000000000002','approver@example.test','Approver');
INSERT INTO app_roles(role_code,role_name)
SELECT code,replace(code,'_',' ')
FROM unnest(ARRAY[
  'SUPER_ADMINISTRATOR','ADMINISTRATOR','SYSTEM_ADMINISTRATOR','RELEASE_MANAGER',
  'MANAGER','ENGINEERING_MANAGER','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD',
  'PROJECT_MANAGER','PROJECT_MANAGEMENT','ENGINEER','SUPPORT','EXECUTIVE'
]) code;
INSERT INTO full_future_loop_items(loop_id)
VALUES('20000000-0000-0000-0000-000000000001');
SQL

psql_exec -f "$MIGRATION" >/dev/null
first_applied="$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='083_module_083_autonomous_control_plane'")"
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='083_module_083_autonomous_control_plane'")" migration_registered_once
assert_eq "$first_applied" "$(value "SELECT applied_at FROM schema_migrations WHERE migration_id='083_module_083_autonomous_control_plane'")" migration_timestamp_immutable
assert_eq 9 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename IN ('full_future_loop_automation_policies','full_future_loop_automation_state','full_future_loop_automation_adapters','full_future_loop_automation_runs','full_future_loop_automation_steps','full_future_loop_automation_approvals','full_future_loop_release_manifests','full_future_loop_automation_evidence','full_future_loop_outbox')")" orchestration_tables
assert_eq 1 "$(value "SELECT COUNT(*) FROM full_future_loop_automation_policies WHERE policy_version='enterprise-default-v1'")" baseline_policy
assert_eq 'f|t|t|1' "$(value "SELECT automation_enabled::text||'|'||global_kill_switch::text||'|'||dry_run_only::text||'|'||revision_number::text FROM full_future_loop_automation_state WHERE state_id=1")" fail_closed_runtime
assert_eq 7 "$(value "SELECT COUNT(*) FROM full_future_loop_automation_adapters")" provider_neutral_adapters
assert_eq 7 "$(value "SELECT COUNT(*) FROM full_future_loop_automation_adapters WHERE adapter_mode='disabled' AND is_ready=FALSE")" adapters_disabled
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_permissions WHERE module_code='083' AND permission_code LIKE '%FULL_FUTURE_LOOP_AUTOMATION_083'")" automation_permissions
assert_eq 4 "$(value "SELECT COUNT(*) FROM app_role_permissions grant_row JOIN app_roles role ON role.app_role_id=grant_row.app_role_id JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id WHERE role.role_code='SUPER_ADMINISTRATOR' AND permission.permission_code LIKE '%FULL_FUTURE_LOOP_AUTOMATION_083'")" super_admin_grants
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='FULL_FUTURE_LOOP_AUTOMATION_083' AND route_anchor='#full-future-loop' AND is_active=TRUE")" feature_catalog_registration
assert_eq 3 "$(value "SELECT COUNT(*) FROM pg_trigger WHERE NOT tgisinternal AND tgname IN ('trg_full_future_loop_automation_policies_immutable_083','trg_full_future_loop_release_manifests_immutable_083','trg_full_future_loop_automation_evidence_immutable_083')")" immutable_triggers

expect_failure immutable_policy 'append-only' psql_exec -qc "UPDATE full_future_loop_automation_policies SET policy_version='changed' WHERE policy_version='enterprise-default-v1'"
expect_failure active_adapter_rejected 'violates check constraint' psql_exec -qc "UPDATE full_future_loop_automation_adapters SET adapter_mode='active' WHERE adapter_code='github'"
expect_failure non_dry_run_rejected 'violates check constraint' psql_exec -qc "INSERT INTO full_future_loop_automation_runs(run_id,idempotency_key,correlation_id,requested_operation,target_environment,repository,source_commit,risk_class,change_type,policy_version_id,disposition,decision_code,run_status,dry_run,attempt_count,maximum_attempts,request_snapshot,decision_snapshot,requested_by_user_id,requested_at,completed_at) VALUES(gen_random_uuid(),'invalid-non-dry-run',gen_random_uuid(),'observe','test','ahmedadeyemi-cts/project-time-platform',repeat('a',40),'normal','application','08300000-0000-0000-0000-000000000001','blocked','invalid','blocked',FALSE,1,3,'{}','{}','10000000-0000-0000-0000-000000000001',NOW(),NOW())"

psql_exec <<'SQL'
INSERT INTO full_future_loop_automation_runs(
  run_id,loop_id,idempotency_key,correlation_id,requested_operation,target_environment,
  repository,source_commit,risk_class,change_type,policy_version_id,disposition,
  decision_code,run_status,dry_run,attempt_count,maximum_attempts,deadline_at,
  request_snapshot,decision_snapshot,requested_by_user_id,requested_at,completed_at)
VALUES(
  '30000000-0000-0000-0000-000000000001',
  '20000000-0000-0000-0000-000000000001',
  'module083-migration-guard-test',
  '40000000-0000-0000-0000-000000000001',
  'observe','test','ahmedadeyemi-cts/project-time-platform',repeat('b',40),
  'normal','application','08300000-0000-0000-0000-000000000001',
  'blocked','global_kill_switch_active','blocked',TRUE,1,3,NOW()+INTERVAL '2 hours',
  '{}','{}','10000000-0000-0000-0000-000000000001',NOW(),NOW());
SQL

expect_failure guarded_rollback 'Rollback refused: Module 083 autonomous run evidence exists.' psql_exec -f "$ROLLBACK"
assert_eq 1 "$(value "SELECT COUNT(*) FROM full_future_loop_automation_runs WHERE run_id='30000000-0000-0000-0000-000000000001'")" guarded_rollback_preserved_run

psql_exec -qc "DELETE FROM full_future_loop_automation_runs WHERE run_id='30000000-0000-0000-0000-000000000001'"
psql_exec -f "$ROLLBACK" >/dev/null

assert_eq '' "$(value "SELECT to_regclass('public.full_future_loop_automation_runs')")" clean_rollback_removed_runs
assert_eq '' "$(value "SELECT to_regclass('public.full_future_loop_automation_state')")" clean_rollback_removed_state
assert_eq '' "$(value "SELECT to_regclass('public.full_future_loop_release_manifests')")" clean_rollback_removed_manifests
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='083_module_083_autonomous_control_plane'")" clean_rollback_removed_ledger
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_feature_catalog WHERE feature_code='FULL_FUTURE_LOOP_AUTOMATION_083'")" clean_rollback_removed_feature
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code LIKE '%FULL_FUTURE_LOOP_AUTOMATION_083'")" clean_rollback_removed_permissions

echo 'MODULE_083_AUTONOMOUS_CONTROL_PLANE_MIGRATION_083=PASS'
echo 'MODULE_083_AUTONOMY_DATABASE_BOUNDARY=DRY_RUN_ONLY'
echo 'MODULE_083_AUTONOMY_ROLLBACK=GUARDED_AND_CLEAN'
