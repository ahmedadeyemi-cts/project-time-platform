#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/066_immutable_business_project_numbers.sql"
ROLLBACK="$ROOT/database/rollback/066_immutable_business_project_numbers_rollback.sql"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-${TEST_DATABASE_URL:-}}"

test -s "$MIGRATION"
test -s "$ROLLBACK"

for marker in \
  'PRO' 'SR' 'IQS' 'INT' 'PRES' \
  'legacy_project_code' \
  'project_business_identifier_aliases' \
  'projectpulse_resolve_project_identifier' \
  'projectpulse066_guard_project_number_immutability' \
  '066_immutable_business_project_numbers'; do
  grep -Fq "$marker" "$MIGRATION" || { echo "MIGRATION066_CONTRACT_MISSING=$marker" >&2; exit 1; }
done

grep -Fq 'UPDATE projects' "$ROLLBACK"
grep -Fq 'DROP TABLE IF EXISTS project_business_identifier_aliases' "$ROLLBACK"

if [[ -z "$DATABASE_URL" ]]; then
  echo 'MIGRATION066_SOURCE_CONTRACT=PASS'
  echo 'MIGRATION066_DATABASE_EXECUTION=SKIPPED_NO_TEST_DATABASE_URL'
  exit 0
fi

command -v psql >/dev/null

psql "$DATABASE_URL" -v ON_ERROR_STOP=1 <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE IF NOT EXISTS schema_migrations (
  migration_id text PRIMARY KEY,
  description text NOT NULL DEFAULT '',
  applied_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS clients (client_id uuid PRIMARY KEY DEFAULT gen_random_uuid());
CREATE TABLE IF NOT EXISTS app_users (user_id uuid PRIMARY KEY DEFAULT gen_random_uuid());
CREATE TABLE IF NOT EXISTS projects (
  project_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  client_id uuid REFERENCES clients(client_id),
  project_code varchar(100) NOT NULL UNIQUE,
  project_name varchar(255) NOT NULL,
  project_description text,
  project_manager_user_id uuid REFERENCES app_users(user_id),
  status varchar(50) NOT NULL DEFAULT 'active',
  start_date date,
  end_date date,
  billable boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS work_register_intake_packages (
  work_register_intake_package_id uuid PRIMARY KEY DEFAULT gen_random_uuid()
);
CREATE TABLE IF NOT EXISTS work_register_project_metadata (
  work_register_project_metadata_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  project_id uuid NOT NULL UNIQUE REFERENCES projects(project_id),
  work_register_intake_package_id uuid REFERENCES work_register_intake_packages(work_register_intake_package_id),
  requested_work_type text NOT NULL DEFAULT '',
  contract_type text NOT NULL DEFAULT '',
  gsd_template_family text NOT NULL DEFAULT 'standard',
  sow_signed_date date,
  intake_reason text NOT NULL DEFAULT '',
  project_list_price numeric NOT NULL DEFAULT 0,
  pm_hours numeric NOT NULL DEFAULT 0,
  engineering_hours numeric NOT NULL DEFAULT 0,
  travel_hours numeric NOT NULL DEFAULT 0,
  metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
  created_by_user_id uuid REFERENCES app_users(user_id),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS work_register_intake_commits (
  work_register_intake_commit_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  work_register_intake_package_id uuid NOT NULL UNIQUE REFERENCES work_register_intake_packages(work_register_intake_package_id),
  project_id uuid NOT NULL REFERENCES projects(project_id),
  project_code text NOT NULL,
  committed_by_user_id uuid REFERENCES app_users(user_id),
  committed_at timestamptz NOT NULL DEFAULT now(),
  commit_summary_json jsonb NOT NULL DEFAULT '{}'::jsonb
);
CREATE OR REPLACE FUNCTION projectpulse055d4d_commit_intake_package(
  p_intake_package_id uuid,
  p_actor_user_id uuid)
RETURNS jsonb
LANGUAGE sql
AS $$
  SELECT jsonb_build_object(
    'status', 'already_committed',
    'projectId', '11111111-1111-1111-1111-111111111111',
    'projectCode', 'WR-20260802-ABC123');
$$;
TRUNCATE work_register_intake_commits, work_register_project_metadata, work_register_intake_packages, projects CASCADE;
WITH inserted_project AS (
  INSERT INTO projects (project_id, project_code, project_name, project_description)
  VALUES ('11111111-1111-1111-1111-111111111111', 'WR-20260802-ABC123', 'Legacy service request', 'Created from Work Register intake. Work type: Service Request. Contract type: TM.')
  RETURNING project_id
), inserted_package AS (
  INSERT INTO work_register_intake_packages (work_register_intake_package_id)
  VALUES ('22222222-2222-2222-2222-222222222222') RETURNING work_register_intake_package_id
)
INSERT INTO work_register_project_metadata (
  project_id, work_register_intake_package_id, requested_work_type)
SELECT inserted_project.project_id, inserted_package.work_register_intake_package_id, 'Service Request'
FROM inserted_project, inserted_package;
SQL

psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f "$MIGRATION"
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f "$MIGRATION"

actual="$(psql "$DATABASE_URL" -Atqc "SELECT project_code || '|' || legacy_project_code FROM projects WHERE project_id='11111111-1111-1111-1111-111111111111';")"
[[ "$actual" =~ ^SR-[A-F0-9]{8}\|WR-20260802-ABC123$ ]]

resolved="$(psql "$DATABASE_URL" -Atqc "SELECT projectpulse_resolve_project_identifier('WR-20260802-ABC123');")"
[[ "$resolved" == '11111111-1111-1111-1111-111111111111' ]]

commit_response="$(psql "$DATABASE_URL" -Atqc "SELECT projectpulse055d4d_commit_intake_package('22222222-2222-2222-2222-222222222222', NULL)::text;")"
[[ "$commit_response" == *'"projectCode": "SR-'* ]]
[[ "$commit_response" == *'"legacyProjectCode": "WR-20260802-ABC123"'* ]]
[[ "$commit_response" == *'"projectNumberImmutable": true'* ]]

if psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -c "UPDATE projects SET project_code='PRO-00000000' WHERE project_id='11111111-1111-1111-1111-111111111111';" >/dev/null 2>&1; then
  echo 'MIGRATION066_IMMUTABILITY_FAILED' >&2
  exit 1
fi

psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f "$ROLLBACK"
legacy="$(psql "$DATABASE_URL" -Atqc "SELECT project_code FROM projects WHERE project_id='11111111-1111-1111-1111-111111111111';")"
[[ "$legacy" == 'WR-20260802-ABC123' ]]
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f "$MIGRATION"

printf 'MIGRATION066_APPLY_REPEAT_ROLLBACK_REAPPLY=PASS\n'
