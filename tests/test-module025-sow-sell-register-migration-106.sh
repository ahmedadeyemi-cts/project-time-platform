#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-module025-migration-106-${GITHUB_RUN_ID:-local}-$$"
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
psql_exec -f /workspace/database/migrations/106_module025_sow_sell_register.sql >/dev/null

assert_eq 1 "$(value "SELECT count(*) FROM schema_migrations WHERE migration_id='106_module025_sow_sell_register';")" migration_registered_once
assert_eq 1 "$(value "SELECT count(*) FROM pg_trigger WHERE tgname='module025_generation_snapshot' AND NOT tgisinternal;")" generation_snapshot_trigger_is_unique
assert_eq 1 "$(value "SELECT count(*) FROM pg_trigger WHERE tgname='module025_sell_receipt_validation' AND NOT tgisinternal;")" sell_receipt_trigger_is_unique

psql_exec <<'SQL'
INSERT INTO app_users(user_id,email,display_name)
VALUES ('10600000-0000-0000-0000-000000000001','module025-migration@example.invalid','Module 025 Migration Test');
INSERT INTO module025_sow_gsd_engagements(engagement_id,owner_user_id,owner_display_name,customer_name,customer_entry_mode)
VALUES ('10600000-0000-0000-0000-000000000002','10600000-0000-0000-0000-000000000001','Module 025 Migration Test','Synthetic customer','manual');
INSERT INTO module025_sow_gsd_phases(engagement_id,phase_code,sort_order,objective)
VALUES ('10600000-0000-0000-0000-000000000002','plan',1,'Synthetic retained planning objective');
INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,summary,evidence_json)
VALUES ('10600000-0000-0000-0000-000000000002','ai_generation_completed','10600000-0000-0000-0000-000000000001',1,'Synthetic generation completed','{"generationId":"synthetic-106"}');
INSERT INTO module025_sow_gsd_versions(version_id,engagement_id,version_number,source_revision,content_sha256,source_json,sow_content,gsd_content,actor_user_id)
VALUES ('10600000-0000-0000-0000-000000000003','10600000-0000-0000-0000-000000000002',1,2,repeat('a',64),'{"synthetic":true}',decode(repeat('ab',32),'hex'),decode(repeat('cd',32),'hex'),'10600000-0000-0000-0000-000000000001');
SQL

assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_gsd_generation_snapshots WHERE event_id=(SELECT event_id FROM module025_sow_gsd_events WHERE event_type='ai_generation_completed');")" generation_snapshot_captured
assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_gsd_versions;")" retained_version_created

psql_exec <<'SQL'
INSERT INTO module025_sow_gsd_artifact_issuance(version_id,artifact_kind,actor_user_id)
VALUES ('10600000-0000-0000-0000-000000000003','sow','10600000-0000-0000-0000-000000000001'),('10600000-0000-0000-0000-000000000003','gsd','10600000-0000-0000-0000-000000000001')
ON CONFLICT (version_id,artifact_kind) DO NOTHING;
INSERT INTO module025_sow_gsd_artifact_issuance(version_id,artifact_kind,actor_user_id)
VALUES ('10600000-0000-0000-0000-000000000003','sow','10600000-0000-0000-0000-000000000001'),('10600000-0000-0000-0000-000000000003','gsd','10600000-0000-0000-0000-000000000001')
ON CONFLICT (version_id,artifact_kind) DO NOTHING;
INSERT INTO module025_sow_sell_submissions(submission_id,engagement_id,version_id,destination_key,runtime_environment,actor_user_id,recipients_json)
VALUES ('10600000-0000-0000-0000-000000000004','10600000-0000-0000-0000-000000000002','10600000-0000-0000-0000-000000000003','zendesk_sell','test','10600000-0000-0000-0000-000000000001','[{"role":"solution_architect"},{"role":"account_executive"},{"role":"inside_sales"}]');
INSERT INTO module025_sow_sell_dispatch(submission_id,engagement_id,sell_status)
VALUES ('10600000-0000-0000-0000-000000000004','10600000-0000-0000-0000-000000000002','blocked');
SELECT sow_sha256,gsd_sha256 FROM module025_sow_gsd_versions WHERE version_id='10600000-0000-0000-0000-000000000003'\gset version_
INSERT INTO module025_sow_sell_receipts(submission_id,sell_record_id,sow_document_id,gsd_document_id,sow_sha256,gsd_sha256,provider_receipt_id)
VALUES ('10600000-0000-0000-0000-000000000004','synthetic-sell-106','synthetic-sow-106','synthetic-gsd-106',:'version_sow_sha256',:'version_gsd_sha256','synthetic-provider-106');
SQL

assert_eq 2 "$(value "SELECT count(*) FROM module025_sow_gsd_artifact_issuance;")" repeated_downloads_one_first_issuance_each
assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_sell_submissions;")" one_submission
assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_sell_links;")" one_destination_link
assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_sell_receipts;")" exact_receipt_attested

psql_exec <<'SQL'
DO $$
DECLARE blocked boolean := false;
BEGIN
  BEGIN UPDATE module025_sow_gsd_versions SET source_json='{"mutated":true}' WHERE version_id='10600000-0000-0000-0000-000000000003'; EXCEPTION WHEN OTHERS THEN blocked := true; END;
  IF NOT blocked THEN RAISE EXCEPTION 'retained version update was not rejected'; END IF;
END $$;
DO $$
DECLARE blocked boolean := false;
BEGIN
  BEGIN DELETE FROM module025_sow_sell_receipts WHERE submission_id='10600000-0000-0000-0000-000000000004'; EXCEPTION WHEN OTHERS THEN blocked := true; END;
  IF NOT blocked THEN RAISE EXCEPTION 'receipt delete was not rejected'; END IF;
END $$;
DO $$
DECLARE blocked boolean := false;
BEGIN
  BEGIN
    INSERT INTO module025_sow_sell_receipts(submission_id,sell_record_id,sow_document_id,gsd_document_id,sow_sha256,gsd_sha256,provider_receipt_id)
    VALUES ('10600000-0000-0000-0000-000000000004','wrong-sell-106','wrong-sow-106','wrong-gsd-106',repeat('0',64),repeat('0',64),'wrong-provider-106');
  EXCEPTION WHEN OTHERS THEN blocked := true;
  END;
  IF NOT blocked THEN RAISE EXCEPTION 'mismatched receipt was not rejected'; END IF;
END $$;
SQL
assert_eq 1 "$(value "SELECT count(*) FROM module025_sow_sell_receipts;")" rejected_mutations_preserve_receipt
assert_eq 1 "$(value "SELECT count(*) FROM schema_migrations WHERE migration_id='106_module025_sow_sell_register';")" migration_reapply_is_idempotent
echo 'MODULE025_SOW_SELL_REGISTER_MIGRATION_106=PASS'
