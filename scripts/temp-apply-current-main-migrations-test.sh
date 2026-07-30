#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="289628ded2ec91ea0710d3cb7ee7cf16bca1f012"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
MODE="${PROJECTPULSE_CURRENT_MAIN_MIGRATION_MODE:-verify}"

MIGRATION_051A_FILE="051a_pending_approval_day_status_lifecycle.sql"
MIGRATION_052_FILE="052_document_intelligence_runtime.sql"
MIGRATION_053_FILE="053_intelligence_answer_orchestration.sql"
MIGRATION_051A_ID="051a_pending_approval_day_status_lifecycle"
MIGRATION_052_ID="052_pulse_ai_private_document_runtime"
MIGRATION_053_ID="053_pulse_ai_private_rag_orchestration"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "PROJECTPULSE_CURRENT_MAIN_MIGRATION_MODE must be apply or verify."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] ||
  fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"
for migration in "$MIGRATION_051A_FILE" "$MIGRATION_052_FILE" "$MIGRATION_053_FILE"; do
  [[ -f "$MIGRATION_ROOT/$migration" ]] || fail "Migration source is missing: $migration"
done
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "3" ]] ||
  fail "Migration checksum manifest must contain exactly three SQL files."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "PROJECTPULSE_MIGRATIONS_051A_052_053_CHECKSUM=VERIFIED"

read -r USERS_BEFORE PROJECTS_BEFORE ASSIGNMENTS_BEFORE ENTRIES_BEFORE DOCUMENTS_BEFORE DAY_STATUS_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM time_entries),
      (SELECT COUNT(*) FROM project_intake_documents),
      (SELECT COUNT(*) FROM timesheet_day_statuses);" | tr '|' ' '
)"
[[ -n "${USERS_BEFORE:-}" ]] || fail "Required ProjectPulse tables are unavailable."

migration_registered() {
  local migration_id="$1"
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT EXISTS (
      SELECT 1 FROM schema_migrations WHERE migration_id='$migration_id'
    );"
}

apply_or_verify() {
  local migration_id="$1" migration_file="$2" registered
  registered="$(migration_registered "$migration_id")"
  case "$registered:$MODE" in
    t:apply)
      echo "MIGRATION_${migration_id}=ALREADY_REGISTERED"
      ;;
    t:verify)
      echo "MIGRATION_${migration_id}=REGISTERED"
      ;;
    f:apply)
      echo "MIGRATION_${migration_id}=APPLYING"
      psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
        --file="$MIGRATION_ROOT/$migration_file"
      ;;
    f:verify)
      fail "Migration $migration_id is not registered; apply authorization is required."
      ;;
    *)
      fail "Unexpected migration registration state for $migration_id: $registered"
      ;;
  esac
}

apply_or_verify "$MIGRATION_051A_ID" "$MIGRATION_051A_FILE"
apply_or_verify "$MIGRATION_052_ID" "$MIGRATION_052_FILE"
apply_or_verify "$MIGRATION_053_ID" "$MIGRATION_053_FILE"

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
DO $projectpulse_current_main_verify$
DECLARE
  missing_tables text[];
  missing_columns text[];
  permission_count integer;
  feature_count integer;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='051a_pending_approval_day_status_lifecycle'
  ) THEN
    RAISE EXCEPTION 'Migration 051A is not registered.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='052_pulse_ai_private_document_runtime'
  ) THEN
    RAISE EXCEPTION 'Migration 052 is not registered.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='053_pulse_ai_private_rag_orchestration'
  ) THEN
    RAISE EXCEPTION 'Migration 053 is not registered.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname='chk_timesheet_day_status'
      AND pg_get_constraintdef(oid) LIKE '%pm_approved%'
      AND pg_get_constraintdef(oid) LIKE '%accounting_ready%'
  ) THEN
    RAISE EXCEPTION 'Migration 051A approval lifecycle constraint is missing or incomplete.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_indexes
    WHERE schemaname='public'
      AND indexname='ix_timesheet_day_statuses_pending_approval_stage'
  ) THEN
    RAISE EXCEPTION 'Migration 051A pending-approval index is missing.';
  END IF;

  SELECT array_agg(required_name)
  INTO missing_tables
  FROM unnest(ARRAY[
    'pulse_ai_document_processing_jobs',
    'pulse_ai_document_versions',
    'pulse_ai_document_sections',
    'pulse_ai_document_chunks',
    'pulse_ai_document_processing_events',
    'pulse_ai_answer_runs',
    'pulse_ai_answer_citations',
    'pulse_ai_answer_feedback',
    'pulse_ai_retrieval_events'
  ]) AS required_name
  WHERE to_regclass('public.' || required_name) IS NULL;

  IF missing_tables IS NOT NULL THEN
    RAISE EXCEPTION 'Missing Pulse AI runtime/RAG tables: %', missing_tables;
  END IF;

  SELECT array_agg(required_name)
  INTO missing_columns
  FROM unnest(ARRAY[
    'pulse_ai_processing_status',
    'pulse_ai_classification',
    'pulse_ai_document_revision',
    'pulse_ai_effective_at',
    'pulse_ai_superseded_by_document_id',
    'pulse_ai_active_version_id',
    'pulse_ai_processing_error_code',
    'pulse_ai_processing_updated_at'
  ]) AS required_name
  WHERE NOT EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema='public'
      AND table_name='project_intake_documents'
      AND column_name=required_name
  );

  IF missing_columns IS NOT NULL THEN
    RAISE EXCEPTION 'Missing Pulse AI document runtime columns: %', missing_columns;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_indexes
    WHERE schemaname='public'
      AND indexname='ix_pulse_ai_document_chunks_search'
  ) THEN
    RAISE EXCEPTION 'Pulse AI lexical search index is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_pulse_ai_052_processing_events_immutable'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Pulse AI processing evidence immutability trigger is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_pulse_ai_053_retrieval_events_immutable'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Pulse AI retrieval evidence immutability trigger is missing.';
  END IF;

  SELECT COUNT(*) INTO permission_count
  FROM app_permissions
  WHERE permission_code = ANY(ARRAY[
    'VIEW_PULSE_AI_DOCUMENT_RUNTIME',
    'QUEUE_PULSE_AI_DOCUMENT_PROCESSING',
    'CANCEL_PULSE_AI_DOCUMENT_PROCESSING',
    'RETRY_PULSE_AI_DOCUMENT_PROCESSING',
    'APPROVE_PULSE_AI_DOCUMENT_VERSION',
    'ASK_PULSE_AI_HELP_SEARCH',
    'USE_PULSE_AI_TIMESHEET_GROUNDING',
    'USE_PULSE_AI_FLOWHIVE_PLANNING',
    'VIEW_PULSE_AI_ANSWER_AUDIT',
    'SUBMIT_PULSE_AI_FEEDBACK'
  ]);
  IF permission_count <> 10 THEN
    RAISE EXCEPTION 'Expected 10 Pulse AI runtime/RAG permissions, found %.', permission_count;
  END IF;

  SELECT COUNT(*) INTO feature_count
  FROM app_feature_catalog
  WHERE feature_code = ANY(ARRAY[
    'PULSE_AI_PRIVATE_DOCUMENT_RUNTIME',
    'PULSE_AI_PRIVATE_HELP_SEARCH',
    'PULSE_AI_PRIVATE_TIMESHEET_GROUNDING',
    'PULSE_AI_PRIVATE_FLOWHIVE_PLANNING'
  ]);
  IF feature_count <> 4 THEN
    RAISE EXCEPTION 'Expected 4 Pulse AI runtime/RAG features, found %.', feature_count;
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema='public'
      AND table_name='pulse_ai_answer_feedback'
      AND column_name='training_candidate'
      AND column_default ILIKE '%false%'
  ) THEN
    RAISE EXCEPTION 'Pulse AI feedback must remain non-training by default.';
  END IF;
END
$projectpulse_current_main_verify$;
SQL

read -r USERS_AFTER PROJECTS_AFTER ASSIGNMENTS_AFTER ENTRIES_AFTER DOCUMENTS_AFTER DAY_STATUS_AFTER <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM time_entries),
      (SELECT COUNT(*) FROM project_intake_documents),
      (SELECT COUNT(*) FROM timesheet_day_statuses);" | tr '|' ' '
)"

[[ "$USERS_AFTER" == "$USERS_BEFORE" ]] || fail "Current-main migrations changed app_users row count."
[[ "$PROJECTS_AFTER" == "$PROJECTS_BEFORE" ]] || fail "Current-main migrations changed projects row count."
[[ "$ASSIGNMENTS_AFTER" == "$ASSIGNMENTS_BEFORE" ]] || fail "Current-main migrations changed project_assignments row count."
[[ "$ENTRIES_AFTER" == "$ENTRIES_BEFORE" ]] || fail "Current-main migrations changed time_entries row count."
[[ "$DOCUMENTS_AFTER" == "$DOCUMENTS_BEFORE" ]] || fail "Current-main migrations changed project_intake_documents row count."
[[ "$DAY_STATUS_AFTER" == "$DAY_STATUS_BEFORE" ]] || fail "Current-main migrations changed timesheet_day_statuses row count."

NEW_RUNTIME_ROWS="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
  SELECT json_build_object(
    'processingJobs', (SELECT COUNT(*) FROM pulse_ai_document_processing_jobs),
    'documentVersions', (SELECT COUNT(*) FROM pulse_ai_document_versions),
    'documentChunks', (SELECT COUNT(*) FROM pulse_ai_document_chunks),
    'answerRuns', (SELECT COUNT(*) FROM pulse_ai_answer_runs),
    'retrievalEvents', (SELECT COUNT(*) FROM pulse_ai_retrieval_events)
  );")"
echo "PROJECTPULSE_MIGRATIONS_051A_052_053_RUNTIME_ROWS=$NEW_RUNTIME_ROWS"
echo "PROJECTPULSE_MIGRATIONS_051A_052_053_OPERATIONAL_COUNTS=UNCHANGED"
echo "PROJECTPULSE_MIGRATIONS_051A_052_053_INVARIANTS=VERIFIED"
echo "PROJECTPULSE_VECTOR_INDEX_CREATED=NO"
echo "PROJECTPULSE_PRIVATE_MODEL_CONFIGURED=NO"
if [[ "$MODE" == apply ]]; then
  echo "PROJECTPULSE_MIGRATIONS_051A_052_053_RESULT=APPLIED_OR_ALREADY_PRESENT"
else
  echo "PROJECTPULSE_MIGRATIONS_051A_052_053_RESULT=VERIFY_ONLY_PASS"
fi
