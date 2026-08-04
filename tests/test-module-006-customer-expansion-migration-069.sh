#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-module006-069-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
MIGRATION_068="/workspace/database/migrations/068_module006_standalone_pipeline_management.sql"
MIGRATION_069="/workspace/database/migrations/069_module006_customer_pipeline_expansion.sql"
ROLLBACK_069="/workspace/database/rollback/069_module006_customer_pipeline_expansion_rollback.sql"

if ! command -v docker >/dev/null 2>&1; then
  for marker in \
    "069_module006_customer_pipeline_expansion" \
    "ck_module006_pipeline_records_customer_name" \
    "char_length(customer) BETWEEN 2 AND 120" \
    "ix_module006_pipeline_records_customer_name"; do
    grep -Fq "$marker" "$ROOT/database/migrations/069_module006_customer_pipeline_expansion.sql" || {
      echo "STATIC_ASSERTION_FAILED missing=$marker" >&2
      exit 1
    }
  done
  grep -Fq "additional-customer Module 006 records exist" \
    "$ROOT/database/rollback/069_module006_customer_pipeline_expansion_rollback.sql"
  grep -Fq "lower(btrim(customer)) NOT IN ('toyota', 'hyundai')" \
    "$ROOT/database/rollback/069_module006_customer_pipeline_expansion_rollback.sql"
  echo 'MODULE_006_CUSTOMER_EXPANSION_MIGRATION_069=STATIC_PASS docker_unavailable'
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
  local sql="$1" expected="$2" label="$3"
  local log="/tmp/projectpulse-module006-069-${label}.log"
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

expect_file_failure() {
  local file="$1" expected="$2" label="$3"
  local log="/tmp/projectpulse-module006-069-${label}.log"
  if psql_exec -f "$file" >"$log" 2>&1; then
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
  display_name TEXT NOT NULL
);
INSERT INTO app_users(user_id,email,display_name) VALUES
 ('10000000-0000-0000-0000-000000000001','module006@example.test','Module 006 Test');
SQL

psql_exec -f "$MIGRATION_068" >/dev/null
psql_exec <<'SQL'
INSERT INTO module006_pipeline_records(
  module006_pipeline_record_id,source_project_code,customer,project_name,
  created_by_user_id,updated_by_user_id
) VALUES (
  '20000000-0000-0000-0000-000000000001','P.TOYOTA01','Toyota','Baseline Toyota Project',
  '10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'
);
SQL

assert_eq 1 "$(value 'SELECT COUNT(*) FROM module006_pipeline_records;')" baseline_record_count

psql_exec -f "$MIGRATION_069" >/dev/null
psql_exec -f "$MIGRATION_069" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='069_module006_customer_pipeline_expansion';")" migration_registered_once
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_constraint WHERE conrelid='module006_pipeline_records'::regclass AND conname='ck_module006_pipeline_records_customer_name';")" governed_customer_constraint
assert_eq ix_module006_pipeline_records_customer_name "$(value "SELECT to_regclass('public.ix_module006_pipeline_records_customer_name')::text;")" customer_index
assert_eq 1 "$(value 'SELECT COUNT(*) FROM module006_pipeline_records;')" apply_preserved_business_rows

psql_exec <<'SQL'
INSERT INTO module006_pipeline_records(
  module006_pipeline_record_id,source_project_code,customer,project_name,
  created_by_user_id,updated_by_user_id
) VALUES (
  '20000000-0000-0000-0000-000000000002','P.ACME001','Acme Industries','Additional Customer Project',
  '10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'
);
SQL
assert_eq 1 "$(value "SELECT COUNT(*) FROM module006_pipeline_records WHERE customer='Acme Industries';")" additional_customer_allowed

expect_sql_failure \
  "INSERT INTO module006_pipeline_records(source_project_code,customer,project_name,created_by_user_id,updated_by_user_id) VALUES ('P.INVALID1','A','Short Customer','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');" \
  'ck_module006_pipeline_records_customer_name' customer_too_short_rejected
expect_sql_failure \
  "INSERT INTO module006_pipeline_records(source_project_code,customer,project_name,created_by_user_id,updated_by_user_id) VALUES ('P.INVALID2',' Acme','Padded Customer','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');" \
  'ck_module006_pipeline_records_customer_name' padded_customer_rejected
expect_sql_failure \
  "INSERT INTO module006_pipeline_records(source_project_code,customer,project_name,created_by_user_id,updated_by_user_id) VALUES ('P.INVALID3',repeat('X',121),'Long Customer','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');" \
  'ck_module006_pipeline_records_customer_name' customer_too_long_rejected
expect_sql_failure \
  "INSERT INTO module006_pipeline_records(source_project_code,customer,project_name,created_by_user_id,updated_by_user_id) VALUES ('P.INVALID4','Bad' || chr(10) || 'Name','Control Customer','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');" \
  'ck_module006_pipeline_records_customer_name' control_character_rejected

expect_file_failure "$ROLLBACK_069" \
  'Migration 069 rollback is blocked because additional-customer Module 006 records exist.' \
  additional_customer_blocks_rollback
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='069_module006_customer_pipeline_expansion';")" failed_rollback_preserved_registration

psql_exec -c "DELETE FROM module006_pipeline_records WHERE customer='Acme Industries';" >/dev/null
psql_exec -f "$ROLLBACK_069" >/dev/null

assert_eq 0 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='069_module006_customer_pipeline_expansion';")" rollback_removed_registration
assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.ix_module006_pipeline_records_customer_name')::text,'');")" rollback_removed_customer_index
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_constraint WHERE conrelid='module006_pipeline_records'::regclass AND conname='module006_pipeline_records_customer_check';")" rollback_restored_toyota_hyundai_constraint
assert_eq 1 "$(value 'SELECT COUNT(*) FROM module006_pipeline_records;')" rollback_preserved_business_rows
expect_sql_failure \
  "INSERT INTO module006_pipeline_records(source_project_code,customer,project_name,created_by_user_id,updated_by_user_id) VALUES ('P.ACME002','Acme Industries','Blocked After Rollback','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001');" \
  'module006_pipeline_records_customer_check' rollback_restricted_additional_customer

psql_exec -f "$MIGRATION_069" >/dev/null
psql_exec <<'SQL'
INSERT INTO module006_pipeline_records(
  module006_pipeline_record_id,source_project_code,customer,project_name,
  created_by_user_id,updated_by_user_id
) VALUES (
  '20000000-0000-0000-0000-000000000003','P.ACME003','Acme Industries','Reapplied Customer Project',
  '10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001'
);
SQL
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='069_module006_customer_pipeline_expansion';")" migration_reapplied_once
assert_eq 1 "$(value "SELECT COUNT(*) FROM module006_pipeline_records WHERE source_project_code='P.ACME003';")" reapply_allows_additional_customer

echo 'MODULE_006_CUSTOMER_EXPANSION_MIGRATION_069=PASS prerequisite=068 idempotent=true validation=bounded rollback=guarded reapply=true'
