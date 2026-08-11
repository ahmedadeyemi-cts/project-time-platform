#!/usr/bin/env bash
set -Eeuo pipefail

: "${PGHOST:?PGHOST is required}"
: "${PGPORT:?PGPORT is required}"
: "${PGUSER:?PGUSER is required}"
: "${PGPASSWORD:?PGPASSWORD is required}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/084_module_076_celar_ai_defect_operations.sql"
ROLLBACK="$ROOT/database/rollback/084_module_076_celar_ai_defect_operations_rollback.sql"
EVIDENCE_DATABASE="celar_ops_084_evidence_${RANDOM}_${RANDOM}"
ROLLBACK_DATABASE="celar_ops_084_rollback_${RANDOM}_${RANDOM}"

cleanup_database() {
  local database="$1"
  PGDATABASE=postgres psql -v ON_ERROR_STOP=1 \
    -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='${database}' AND pid<>pg_backend_pid();" \
    >/dev/null 2>&1 || true
  PGDATABASE=postgres dropdb --if-exists "$database" >/dev/null 2>&1 || true
}

cleanup() {
  cleanup_database "$EVIDENCE_DATABASE"
  cleanup_database "$ROLLBACK_DATABASE"
}
trap cleanup EXIT

initialize_database() {
  local database="$1"
  PGDATABASE=postgres createdb "$database"
  PGDATABASE="$database" psql -v ON_ERROR_STOP=1 <<'SQL'
CREATE TABLE schema_migrations(
  migration_id TEXT PRIMARY KEY,
  applied_at TIMESTAMPTZ NOT NULL
);
CREATE TABLE app_users(
  user_id UUID PRIMARY KEY,
  display_name TEXT NOT NULL DEFAULT '',
  email TEXT NOT NULL UNIQUE,
  is_active BOOLEAN NOT NULL DEFAULT TRUE
);
INSERT INTO app_users(user_id,display_name,email,is_active)
VALUES('08400000-0000-0000-0000-000000000001','Ahmed Adeyemi','ahmed.adeyemi@ussignal.com',TRUE);
SQL
}

apply_migration() {
  local database="$1"
  PGDATABASE="$database" psql -v ON_ERROR_STOP=1 -f "$MIGRATION" >/dev/null
}

apply_rollback() {
  local database="$1"
  PGDATABASE="$database" psql -v ON_ERROR_STOP=1 -f "$ROLLBACK" >/dev/null
}

query() {
  local database="$1"
  local sql="$2"
  PGDATABASE="$database" psql -Atqc "$sql"
}

# Evidence database: prove forward migration, idempotency, immutable evidence,
# and guarded rollback refusal without weakening or bypassing append-only rules.
initialize_database "$EVIDENCE_DATABASE"
apply_migration "$EVIDENCE_DATABASE"
FIRST_APPLIED_AT="$(query "$EVIDENCE_DATABASE" "SELECT applied_at::text FROM schema_migrations WHERE migration_id='084_module_076_celar_ai_defect_operations';")"
apply_migration "$EVIDENCE_DATABASE"
SECOND_APPLIED_AT="$(query "$EVIDENCE_DATABASE" "SELECT applied_at::text FROM schema_migrations WHERE migration_id='084_module_076_celar_ai_defect_operations';")"
[[ "$FIRST_APPLIED_AT" == "$SECOND_APPLIED_AT" ]] || { echo 'MIGRATION_084_LEDGER_TIMESTAMP_STABILITY=FAILED'; exit 1; }
echo 'MIGRATION_084_LEDGER_TIMESTAMP_STABILITY=PASSED'

for relation in \
  module076_defects \
  module076_defect_comments \
  module076_defect_events \
  module076_defect_evidence \
  module076_intake_sessions \
  module076_incident_occurrences \
  module076_monitor_policies \
  module076_probe_results \
  module076_monitor_suppressions \
  module076_notification_outbox; do
  [[ "$(query "$EVIDENCE_DATABASE" "SELECT to_regclass('public.${relation}') IS NOT NULL;")" == 't' ]] || { echo "MIGRATION_084_RELATION_${relation}=FAILED"; exit 1; }
  echo "MIGRATION_084_RELATION_${relation}=PASSED"
done

[[ "$(query "$EVIDENCE_DATABASE" "SELECT COUNT(*) FROM module076_monitor_policies;")" == '15' ]] || { echo 'MIGRATION_084_POLICY_COUNT=FAILED'; exit 1; }
[[ "$(query "$EVIDENCE_DATABASE" "SELECT bool_and(machine_creation_enabled=FALSE) FROM module076_monitor_policies;")" == 't' ]] || { echo 'MIGRATION_084_POLICIES_OBSERVE_ONLY=FAILED'; exit 1; }
[[ "$(query "$EVIDENCE_DATABASE" "SELECT consecutive_failure_threshold=3 AND evaluation_window_seconds=300 FROM module076_monitor_policies WHERE policy_code='all_ai_targets';")" == 't' ]] || { echo 'MIGRATION_084_ALL_AI_THRESHOLD=FAILED'; exit 1; }
[[ "$(query "$EVIDENCE_DATABASE" "SELECT consecutive_failure_threshold=2 AND initial_priority='Critical' FROM module076_monitor_policies WHERE policy_code='github_actions';")" == 't' ]] || { echo 'MIGRATION_084_GITHUB_ACTIONS_THRESHOLD=FAILED'; exit 1; }
[[ "$(query "$EVIDENCE_DATABASE" "SELECT consecutive_success_threshold=3 AND recovery_stability_seconds=900 FROM module076_monitor_policies WHERE policy_code='private_inference';")" == 't' ]] || { echo 'MIGRATION_084_RECOVERY_POLICY=FAILED'; exit 1; }
echo 'MIGRATION_084_POLICY_BASELINE=PASSED'

PGDATABASE="$EVIDENCE_DATABASE" psql -v ON_ERROR_STOP=1 <<'SQL'
INSERT INTO module076_defects(
  defect_id,defect_number,title,description,category,priority,status,
  source_channel,environment,affected_system,
  actual_reporter_user_id,effective_reporter_user_id,
  reporter_display_name,reporter_email,
  assignee_user_id,assignee_display_name,assignee_email,
  machine_created,user_confirmed,idempotency_key,date_added)
VALUES(
  '08400000-0000-0000-0000-000000000010','DEF-2026-000001',
  'Synthetic Test defect','Validated migration 084 record.','Availability','High','Open',
  'availability_monitor','test','private_inference',
  NULL,NULL,'Governed monitoring service','',
  '08400000-0000-0000-0000-000000000001','Ahmed Adeyemi','ahmed.adeyemi@ussignal.com',
  TRUE,FALSE,'migration-084-test',NOW());
INSERT INTO module076_defect_evidence(
  evidence_id,defect_id,evidence_type,source_code,sanitized_summary,
  evidence_document,contains_secret,raw_private_content_stored,observed_at)
VALUES(
  '08400000-0000-0000-0000-000000000011',
  '08400000-0000-0000-0000-000000000010',
  'availability_probe','migration_test','No secret content.',
  '{}'::jsonb,FALSE,FALSE,NOW());
SQL

if PGDATABASE="$EVIDENCE_DATABASE" psql -v ON_ERROR_STOP=1 \
  -c "UPDATE module076_defect_evidence SET sanitized_summary='changed' WHERE evidence_id='08400000-0000-0000-0000-000000000011';" \
  >/dev/null 2>&1; then
  echo 'MIGRATION_084_APPEND_ONLY_EVIDENCE_UPDATE=FAILED'
  exit 1
fi
echo 'MIGRATION_084_APPEND_ONLY_EVIDENCE_UPDATE=PASSED'

if PGDATABASE="$EVIDENCE_DATABASE" psql -v ON_ERROR_STOP=1 \
  -c "DELETE FROM module076_defect_evidence WHERE evidence_id='08400000-0000-0000-0000-000000000011';" \
  >/dev/null 2>&1; then
  echo 'MIGRATION_084_APPEND_ONLY_EVIDENCE_DELETE=FAILED'
  exit 1
fi
echo 'MIGRATION_084_APPEND_ONLY_EVIDENCE_DELETE=PASSED'

if apply_rollback "$EVIDENCE_DATABASE" >/dev/null 2>&1; then
  echo 'MIGRATION_084_ROLLBACK_REFUSES_DURABLE_EVIDENCE=FAILED'
  exit 1
fi
echo 'MIGRATION_084_ROLLBACK_REFUSES_DURABLE_EVIDENCE=PASSED'

[[ "$(query "$EVIDENCE_DATABASE" "SELECT COUNT(*) FROM module076_defect_evidence WHERE evidence_id='08400000-0000-0000-0000-000000000011';")" == '1' ]] || { echo 'MIGRATION_084_EVIDENCE_PRESERVED_AFTER_REFUSED_ROLLBACK=FAILED'; exit 1; }
echo 'MIGRATION_084_EVIDENCE_PRESERVED_AFTER_REFUSED_ROLLBACK=PASSED'

# Clean rollback database: prove the reviewed rollback succeeds when no durable
# evidence exists. This is intentionally separate from the append-only database.
initialize_database "$ROLLBACK_DATABASE"
apply_migration "$ROLLBACK_DATABASE"
apply_rollback "$ROLLBACK_DATABASE"

[[ "$(query "$ROLLBACK_DATABASE" "SELECT to_regclass('public.module076_defects') IS NULL;")" == 't' ]] || { echo 'MIGRATION_084_CLEAN_ROLLBACK_TABLES=FAILED'; exit 1; }
[[ "$(query "$ROLLBACK_DATABASE" "SELECT NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='084_module_076_celar_ai_defect_operations');")" == 't' ]] || { echo 'MIGRATION_084_CLEAN_ROLLBACK_LEDGER=FAILED'; exit 1; }
echo 'MIGRATION_084_CLEAN_ROLLBACK=PASSED'

# Reapply and roll back once more to prove the disposable clean lifecycle can be
# repeated without relying on state from the evidence database.
apply_migration "$ROLLBACK_DATABASE"
[[ "$(query "$ROLLBACK_DATABASE" "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='084_module_076_celar_ai_defect_operations';")" == '1' ]] || { echo 'MIGRATION_084_REAPPLY_LEDGER=FAILED'; exit 1; }
apply_rollback "$ROLLBACK_DATABASE"
[[ "$(query "$ROLLBACK_DATABASE" "SELECT to_regclass('public.module076_defects') IS NULL;")" == 't' ]] || { echo 'MIGRATION_084_REPEAT_ROLLBACK=FAILED'; exit 1; }
echo 'MIGRATION_084_REPEAT_CLEAN_LIFECYCLE=PASSED'

echo 'MODULE_076_CELAR_AI_DEFECT_OPERATIONS_MIGRATION_084=PASS'
