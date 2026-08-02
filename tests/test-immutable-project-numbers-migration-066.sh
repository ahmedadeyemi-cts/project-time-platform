#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-project-number-066-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION="/workspace/database/migrations/066_immutable_project_numbers.sql"
ROLLBACK="/workspace/database/rollback/066_immutable_project_numbers_rollback.sql"

if ! command -v docker >/dev/null 2>&1; then
  for marker in     "projectpulse066_project_prefix"     "projectpulse066_issue_project_number"     "project_code_aliases"     "projectpulse_resolve_project_id"     "projectpulse066_guard_issued_project_number"     "projectpulse055d4d_commit_intake_package_legacy_066"     "066_immutable_project_numbers"; do
    grep -Fq "$marker" "$ROOT/database/migrations/066_immutable_project_numbers.sql" || {
      echo "STATIC_ASSERTION_FAILED missing=$marker" >&2
      exit 1
    }
  done
  grep -Fq "ALTER FUNCTION public.projectpulse055d4d_commit_intake_package_legacy_066" "$ROOT/database/rollback/066_immutable_project_numbers_rollback.sql"
  grep -Fq "DELETE FROM schema_migrations" "$ROOT/database/rollback/066_immutable_project_numbers_rollback.sql"
  echo 'IMMUTABLE_PROJECT_NUMBERS_MIGRATION_066=STATIC_PASS docker_unavailable'
  exit 0
fi

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
expect_sql_failure() {
  local sql="$1" expected="$2" label="$3" log="/tmp/projectpulse-066-${label}.log"
  if psql_exec -c "$sql" >"$log" 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" "$log" || { cat "$log" >&2; exit 1; }
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
  display_name TEXT NOT NULL
);
CREATE TABLE clients (
  client_id UUID PRIMARY KEY,
  client_name TEXT NOT NULL
);
CREATE TABLE projects (
  project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  client_id UUID REFERENCES clients(client_id),
  project_code TEXT NOT NULL UNIQUE,
  project_name TEXT NOT NULL,
  project_description TEXT,
  status TEXT NOT NULL DEFAULT 'active',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE work_register_intake_packages (
  work_register_intake_package_id UUID PRIMARY KEY,
  requested_work_type TEXT NOT NULL DEFAULT 'Project',
  customer_id UUID,
  project_name_hint TEXT,
  reviewed_json JSONB NOT NULL DEFAULT '{}'::jsonb
);
CREATE TABLE work_register_project_metadata (
  work_register_project_metadata_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id UUID NOT NULL UNIQUE REFERENCES projects(project_id),
  work_register_intake_package_id UUID REFERENCES work_register_intake_packages(work_register_intake_package_id),
  requested_work_type TEXT NOT NULL DEFAULT '',
  metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE TABLE work_register_intake_commits (
  work_register_intake_commit_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  work_register_intake_package_id UUID NOT NULL UNIQUE REFERENCES work_register_intake_packages(work_register_intake_package_id),
  project_id UUID NOT NULL REFERENCES projects(project_id),
  project_code TEXT NOT NULL,
  committed_by_user_id UUID REFERENCES app_users(user_id),
  commit_summary_json JSONB NOT NULL DEFAULT '{}'::jsonb
);

INSERT INTO app_users VALUES
 ('10000000-0000-0000-0000-000000000001','admin@example.test','Admin Test');
INSERT INTO clients VALUES
 ('20000000-0000-0000-0000-000000000001','Test Customer');
INSERT INTO work_register_intake_packages VALUES
 ('30000000-0000-0000-0000-000000000001','Project','20000000-0000-0000-0000-000000000001','Backfill Project','{}'),
 ('30000000-0000-0000-0000-000000000002','Service Request','20000000-0000-0000-0000-000000000001','New Service Request','{}');
INSERT INTO projects(project_id,client_id,project_code,project_name) VALUES
 ('40000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','WR-20260802-ABC123','Backfill Project');
INSERT INTO work_register_project_metadata(project_id,work_register_intake_package_id,requested_work_type)
VALUES ('40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','Project');
INSERT INTO work_register_intake_commits(work_register_intake_package_id,project_id,project_code,committed_by_user_id,commit_summary_json)
VALUES ('30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001','WR-20260802-ABC123','10000000-0000-0000-0000-000000000001','{"projectCode":"WR-20260802-ABC123"}');

CREATE OR REPLACE FUNCTION projectpulse055d4d_commit_intake_package(
  p_intake_package_id UUID,
  p_actor_user_id UUID
)
RETURNS JSONB
LANGUAGE plpgsql
AS $$
DECLARE
  existing RECORD;
  package RECORD;
  new_project_id UUID;
  old_code TEXT;
BEGIN
  SELECT * INTO existing FROM work_register_intake_commits
   WHERE work_register_intake_package_id=p_intake_package_id;
  IF FOUND THEN
    RETURN jsonb_build_object('status','already_committed','projectId',existing.project_id,'projectCode',existing.project_code,'message','This intake package was already committed.');
  END IF;
  SELECT * INTO package FROM work_register_intake_packages
   WHERE work_register_intake_package_id=p_intake_package_id;
  new_project_id := gen_random_uuid();
  old_code := 'WR-' || to_char(NOW(),'YYYYMMDD') || '-' || upper(substr(replace(p_intake_package_id::text,'-',''),1,6));
  INSERT INTO projects(project_id,client_id,project_code,project_name)
  VALUES(new_project_id,package.customer_id,old_code,package.project_name_hint);
  INSERT INTO work_register_project_metadata(project_id,work_register_intake_package_id,requested_work_type)
  VALUES(new_project_id,p_intake_package_id,package.requested_work_type);
  INSERT INTO work_register_intake_commits(work_register_intake_package_id,project_id,project_code,committed_by_user_id,commit_summary_json)
  VALUES(p_intake_package_id,new_project_id,old_code,p_actor_user_id,jsonb_build_object('projectCode',old_code));
  RETURN jsonb_build_object('status','committed','projectId',new_project_id,'projectCode',old_code,'message','Created Work Register project '||old_code||'.');
END;
$$;
SQL

apply_migration() { psql_exec -f "$MIGRATION" >/dev/null; }
apply_migration
apply_migration

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='066_immutable_project_numbers';")" migration_registered_once
assert_eq 'PRO-40000000' "$(value "SELECT project_code FROM projects WHERE project_id='40000000-0000-0000-0000-000000000001';")" project_backfilled
assert_eq 'WR-20260802-ABC123' "$(value "SELECT alias_code FROM project_code_aliases WHERE project_id='40000000-0000-0000-0000-000000000001';")" legacy_alias_preserved
assert_eq '40000000-0000-0000-0000-000000000001' "$(value "SELECT projectpulse_resolve_project_id('WR-20260802-ABC123');")" legacy_alias_resolves
assert_eq 'PRO-40000000' "$(value "SELECT project_code FROM work_register_intake_commits WHERE project_id='40000000-0000-0000-0000-000000000001';")" intake_commit_synchronized

NEW_RESULT="$(value "SELECT projectpulse055d4d_commit_intake_package('30000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001')::text;")"
[[ "$NEW_RESULT" == *'"projectNumberImmutable": true'* ]] || { echo "$NEW_RESULT" >&2; exit 1; }
NEW_CODE="$(value "SELECT project_code FROM projects WHERE project_name='New Service Request';")"
[[ "$NEW_CODE" =~ ^SR-[A-Z0-9]{8}$ ]] || { echo "ASSERTION_FAILED service_request_prefix code=$NEW_CODE" >&2; exit 1; }
echo "ASSERTION_PASSED service_request_prefix=$NEW_CODE"

expect_sql_failure "INSERT INTO projects(project_id,client_id,project_code,project_name) VALUES (gen_random_uuid(),'20000000-0000-0000-0000-000000000001','PRO-UNSAFE01','Unsafe Import');" 'governed Module 055D database workflow' direct_permanent_number_insert_blocked
expect_sql_failure "UPDATE projects SET project_code='PRO-FFFFFFFF' WHERE project_code='$NEW_CODE';" 'immutable' issued_number_immutable
expect_sql_failure "UPDATE projects SET project_code='PRO-12345678' WHERE project_id='40000000-0000-0000-0000-000000000001';" 'immutable' backfilled_number_immutable
expect_sql_failure "UPDATE project_code_aliases SET alias_code='WR-CHANGED' WHERE alias_code='WR-20260802-ABC123';" 'immutable' alias_immutable

psql_exec -f "$ROLLBACK" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='066_immutable_project_numbers';")" rollback_removed_registration
assert_eq 'WR-20260802-ABC123' "$(value "SELECT project_code FROM projects WHERE project_id='40000000-0000-0000-0000-000000000001';")" rollback_restored_legacy_code
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.project_code_aliases')::text,'');")" rollback_removed_alias_table
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_proc WHERE proname='projectpulse055d4d_commit_intake_package';")" rollback_restored_final_save

apply_migration
assert_eq 'PRO-40000000' "$(value "SELECT project_code FROM projects WHERE project_id='40000000-0000-0000-0000-000000000001';")" migration_reapplied
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='066_immutable_project_numbers';")" migration_reapplied_registration

echo 'IMMUTABLE_PROJECT_NUMBERS_MIGRATION_066=PASS'
