#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-module025-migration-110-${GITHUB_RUN_ID:-local}-$$"
DB_USER='projectpulse'
DB_NAME='projectpulse'
DB_PASSWORD='projectpulse-test-only'

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT
psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || { echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2; exit 1; }
  echo "ASSERTION_PASSED $label=$actual"
}

docker run -d --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" -e POSTGRES_PASSWORD="$DB_PASSWORD" -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" postgres:16-alpine >/dev/null
for attempt in $(seq 1 90); do
  if psql_exec -Atqc 'SELECT 1;' >/dev/null 2>&1; then break; fi
  [[ "$attempt" != 90 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec -f /workspace/database/migrations/001_initial_schema.sql >/dev/null
psql_exec -f /workspace/database/migrations/099_module025_sow_gsd_workspace.sql >/dev/null
psql_exec -f /workspace/database/migrations/106_module025_sow_sell_register.sql >/dev/null
psql_exec -f /workspace/database/migrations/109_module025_project_name.sql >/dev/null
psql_exec -f /workspace/database/migrations/110_module025_ungenerated_draft_delete.sql >/dev/null
psql_exec -f /workspace/database/migrations/110_module025_ungenerated_draft_delete.sql >/dev/null

assert_eq 1 "$(value "SELECT count(*) FROM schema_migrations WHERE migration_id='110_module025_ungenerated_draft_delete';")" migration_registered_once

psql_exec <<'SQL'
INSERT INTO app_users(user_id,email,display_name)
VALUES ('11000000-0000-0000-0000-000000000001','module025-delete@example.invalid','Module 025 Delete Test');

INSERT INTO module025_sow_gsd_engagements(
    engagement_id,owner_user_id,owner_display_name,customer_name,customer_entry_mode,project_name)
VALUES (
    '11000000-0000-0000-0000-000000000002',
    '11000000-0000-0000-0000-000000000001',
    'Module 025 Delete Test','Synthetic customer','manual','Draft delete test');

INSERT INTO module025_sow_gsd_phases(engagement_id,phase_code,sort_order)
VALUES
 ('11000000-0000-0000-0000-000000000002','plan',1),
 ('11000000-0000-0000-0000-000000000002','design',2),
 ('11000000-0000-0000-0000-000000000002','implement',3),
 ('11000000-0000-0000-0000-000000000002','validate',4),
 ('11000000-0000-0000-0000-000000000002','release',5);

INSERT INTO module025_sow_gsd_events(
    engagement_id,event_type,actor_user_id,engagement_revision,summary,evidence_json)
VALUES (
    '11000000-0000-0000-0000-000000000002','created',
    '11000000-0000-0000-0000-000000000001',1,'Synthetic draft created','{}');
SQL

psql_exec <<'SQL'
DO $$
DECLARE blocked boolean := false;
BEGIN
  BEGIN
    DELETE FROM module025_sow_gsd_engagements
    WHERE engagement_id='11000000-0000-0000-0000-000000000002';
  EXCEPTION WHEN OTHERS THEN blocked := true;
  END;
  IF NOT blocked THEN RAISE EXCEPTION 'draft delete without guarded session flag was not rejected'; END IF;
END $$;
SQL
assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_gsd_engagements WHERE engagement_id='11000000-0000-0000-0000-000000000002';")" normal_delete_remains_blocked

psql_exec <<'SQL'
BEGIN;
SELECT set_config('projectpulse.module025_allow_draft_delete','on',true);
DELETE FROM module025_sow_gsd_events
WHERE engagement_id='11000000-0000-0000-0000-000000000002';
DELETE FROM module025_sow_gsd_engagements
WHERE engagement_id='11000000-0000-0000-0000-000000000002'
  AND status='draft' AND is_active=TRUE AND last_generated_at IS NULL;
COMMIT;
SQL
assert_eq 0 "$(value "SELECT count(*) FROM module025_sow_gsd_engagements WHERE engagement_id='11000000-0000-0000-0000-000000000002';")" ungenerated_draft_can_be_guardedly_deleted

psql_exec <<'SQL'
INSERT INTO module025_sow_gsd_engagements(
    engagement_id,owner_user_id,owner_display_name,customer_name,customer_entry_mode,project_name,status,last_generated_at)
VALUES (
    '11000000-0000-0000-0000-000000000003',
    '11000000-0000-0000-0000-000000000001',
    'Module 025 Delete Test','Synthetic customer','manual','Generated record',
    'review_ready',now());

INSERT INTO module025_sow_gsd_phases(engagement_id,phase_code,sort_order,objective)
VALUES
 ('11000000-0000-0000-0000-000000000003','plan',1,'Generated planning objective'),
 ('11000000-0000-0000-0000-000000000003','design',2,'Generated design objective'),
 ('11000000-0000-0000-0000-000000000003','implement',3,'Generated implementation objective'),
 ('11000000-0000-0000-0000-000000000003','validate',4,'Generated validation objective'),
 ('11000000-0000-0000-0000-000000000003','release',5,'Generated release objective');

INSERT INTO module025_sow_gsd_events(
    engagement_id,event_type,actor_user_id,engagement_revision,summary,evidence_json)
VALUES (
    '11000000-0000-0000-0000-000000000003','ai_generation_completed',
    '11000000-0000-0000-0000-000000000001',1,'Synthetic generation complete',
    '{"generationId":"11000000-0000-0000-0000-000000000099"}');
SQL

assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_gsd_generation_snapshots WHERE engagement_id='11000000-0000-0000-0000-000000000003';")" generated_snapshot_exists

psql_exec <<'SQL'
DO $$
DECLARE blocked boolean := false;
BEGIN
  PERFORM set_config('projectpulse.module025_allow_draft_delete','on',true);
  BEGIN
    DELETE FROM module025_sow_gsd_events
    WHERE engagement_id='11000000-0000-0000-0000-000000000003';
  EXCEPTION WHEN OTHERS THEN blocked := true;
  END;
  IF NOT blocked THEN RAISE EXCEPTION 'generated evidence delete was not rejected'; END IF;
END $$;
SQL
assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_gsd_engagements WHERE engagement_id='11000000-0000-0000-0000-000000000003';")" generated_record_remains_immutable
assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_gsd_generation_snapshots WHERE engagement_id='11000000-0000-0000-0000-000000000003';")" generated_evidence_remains_immutable

echo 'MODULE025_DRAFT_DELETE_MIGRATION_110=PASS'
