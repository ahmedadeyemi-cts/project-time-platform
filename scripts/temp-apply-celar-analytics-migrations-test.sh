#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="1722b6476845e23ab5d6fc63b630420dcbf9a97c"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
MODE="${PROJECTPULSE_CELAR_ANALYTICS_MIGRATION_MODE:-verify}"

MIGRATION_054_FILE="054_pulse_ai_system_intelligence_conversations.sql"
MIGRATION_055_FILE="055_analytics_center.sql"
MIGRATION_054_ID="054_pulse_ai_system_intelligence_conversations"
MIGRATION_055_ID="055_analytics_center"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
[[ "$MODE" == apply || "$MODE" == verify ]] ||
  fail "PROJECTPULSE_CELAR_ANALYTICS_MIGRATION_MODE must be apply or verify."
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
for migration in "$MIGRATION_054_FILE" "$MIGRATION_055_FILE"; do
  [[ -f "$MIGRATION_ROOT/$migration" ]] || fail "Migration source is missing: $migration"
done
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(wc -l < "$CHECKSUM_MANIFEST" | tr -d ' ')" == "2" ]] ||
  fail "Migration checksum manifest must contain exactly two SQL files."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
echo "PROJECTPULSE_MIGRATIONS_054_055_CHECKSUM=VERIFIED"

for prerequisite in \
  051a_pending_approval_day_status_lifecycle \
  052_pulse_ai_private_document_runtime \
  053_pulse_ai_private_rag_orchestration; do
  registered="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT EXISTS (
      SELECT 1 FROM schema_migrations WHERE migration_id='$prerequisite'
    );")"
  [[ "$registered" == t ]] || fail "Required prerequisite migration is not registered: $prerequisite"
done
echo "PROJECTPULSE_MIGRATIONS_054_055_PREREQUISITES=VERIFIED"

read -r USERS_BEFORE PROJECTS_BEFORE ASSIGNMENTS_BEFORE ENTRIES_BEFORE DOCUMENTS_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM time_entries),
      (SELECT COUNT(*) FROM project_intake_documents);" | tr '|' ' '
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

apply_or_verify "$MIGRATION_054_ID" "$MIGRATION_054_FILE"
apply_or_verify "$MIGRATION_055_ID" "$MIGRATION_055_FILE"

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
DO $projectpulse_celar_analytics_verify$
DECLARE
  missing_tables text[];
  missing_indexes text[];
  permission_count integer;
  feature_count integer;
  analytics_active boolean;
  legacy_financial_active boolean;
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='054_pulse_ai_system_intelligence_conversations'
  ) THEN
    RAISE EXCEPTION 'Migration 054 is not registered.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id='055_analytics_center'
  ) THEN
    RAISE EXCEPTION 'Migration 055 is not registered.';
  END IF;

  SELECT array_agg(required_name)
  INTO missing_tables
  FROM unnest(ARRAY[
    'pulse_ai_conversations',
    'pulse_ai_conversation_messages',
    'pulse_ai_system_inquiry_runs',
    'pulse_ai_system_tool_events',
    'enterprise_report_runs',
    'enterprise_report_saved_views',
    'enterprise_report_exports'
  ]) AS required_name
  WHERE to_regclass('public.' || required_name) IS NULL;

  IF missing_tables IS NOT NULL THEN
    RAISE EXCEPTION 'Missing Celar AI or Analytics tables: %', missing_tables;
  END IF;

  SELECT array_agg(required_name)
  INTO missing_indexes
  FROM unnest(ARRAY[
    'ix_pulse_ai_conversations_user',
    'ix_pulse_ai_conversation_messages_conversation',
    'ux_pulse_ai_system_inquiry_runs_correlation',
    'ix_pulse_ai_system_tool_events_run',
    'ux_enterprise_report_saved_views_owner_name',
    'ux_enterprise_report_saved_views_default',
    'ix_enterprise_report_exports_run'
  ]) AS required_name
  WHERE NOT EXISTS (
    SELECT 1
    FROM pg_indexes
    WHERE schemaname='public'
      AND indexname=required_name
  );

  IF missing_indexes IS NOT NULL THEN
    RAISE EXCEPTION 'Missing Celar AI or Analytics indexes: %', missing_indexes;
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_pulse_ai_054_tool_events_immutable'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Celar AI tool-event immutability trigger is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_pulse_ai_054_message_insert'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Celar AI durable-conversation message trigger is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_projectpulse055_analytics_runs_immutable'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Analytics run immutability trigger is missing.';
  END IF;

  IF NOT EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname='trg_projectpulse055_analytics_exports_immutable'
      AND NOT tgisinternal
  ) THEN
    RAISE EXCEPTION 'Analytics export immutability trigger is missing.';
  END IF;

  SELECT COUNT(*) INTO permission_count
  FROM app_permissions
  WHERE permission_code = ANY(ARRAY[
    'ASK_PULSE_AI_SYSTEM_INTELLIGENCE',
    'VIEW_PULSE_AI_API_INVENTORY',
    'USE_PULSE_AI_SYSTEM_TROUBLESHOOTING',
    'USE_PULSE_AI_ENHANCEMENT_ADVISOR',
    'VIEW_PULSE_AI_CONVERSATION_HISTORY',
    'RETEST_PULSE_AI_SAFE_API',
    'VIEW_PULSE_AI_SYSTEM_AUDIT'
  ]);
  IF permission_count <> 7 THEN
    RAISE EXCEPTION 'Expected 7 Celar AI system-intelligence permissions, found %.', permission_count;
  END IF;

  SELECT COUNT(*) INTO permission_count
  FROM app_permissions
  WHERE permission_code = ANY(ARRAY[
    'VIEW_ENTERPRISE_REPORTING',
    'RUN_ENTERPRISE_REPORTING',
    'EXPORT_ENTERPRISE_REPORTING',
    'MANAGE_ENTERPRISE_REPORTING'
  ]);
  IF permission_count <> 4 THEN
    RAISE EXCEPTION 'Expected 4 Analytics Center permissions, found %.', permission_count;
  END IF;

  SELECT COUNT(*) INTO feature_count
  FROM app_feature_catalog
  WHERE feature_code = ANY(ARRAY[
    'PULSE_AI_SYSTEM_INTELLIGENCE',
    'PULSE_AI_API_DISCOVERY',
    'PULSE_AI_SYSTEM_TROUBLESHOOTING',
    'PULSE_AI_ENHANCEMENT_ADVISOR',
    'PULSE_AI_CONVERSATIONS'
  ])
    AND is_active = TRUE;
  IF feature_count <> 5 THEN
    RAISE EXCEPTION 'Expected 5 active Celar AI system-intelligence features, found %.', feature_count;
  END IF;

  SELECT COALESCE(bool_or(is_active), FALSE)
  INTO analytics_active
  FROM app_feature_catalog
  WHERE feature_code='ANALYTICS_CENTER';
  IF analytics_active IS NOT TRUE THEN
    RAISE EXCEPTION 'Analytics Center feature is not active.';
  END IF;

  SELECT COALESCE(bool_or(is_active), FALSE)
  INTO legacy_financial_active
  FROM app_feature_catalog
  WHERE feature_code='FINANCIAL_REPORT_CENTER';
  IF legacy_financial_active IS TRUE THEN
    RAISE EXCEPTION 'Legacy Financial Report Center remains active.';
  END IF;
END
$projectpulse_celar_analytics_verify$;
SQL

read -r USERS_AFTER PROJECTS_AFTER ASSIGNMENTS_AFTER ENTRIES_AFTER DOCUMENTS_AFTER <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM projects),
      (SELECT COUNT(*) FROM project_assignments),
      (SELECT COUNT(*) FROM time_entries),
      (SELECT COUNT(*) FROM project_intake_documents);" | tr '|' ' '
)"

[[ "$USERS_AFTER" == "$USERS_BEFORE" ]] || fail "Migrations 054/055 changed app_users row count."
[[ "$PROJECTS_AFTER" == "$PROJECTS_BEFORE" ]] || fail "Migrations 054/055 changed projects row count."
[[ "$ASSIGNMENTS_AFTER" == "$ASSIGNMENTS_BEFORE" ]] || fail "Migrations 054/055 changed project_assignments row count."
[[ "$ENTRIES_AFTER" == "$ENTRIES_BEFORE" ]] || fail "Migrations 054/055 changed time_entries row count."
[[ "$DOCUMENTS_AFTER" == "$DOCUMENTS_BEFORE" ]] || fail "Migrations 054/055 changed project_intake_documents row count."

NEW_TABLE_ROWS="$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
  SELECT json_build_object(
    'conversations', (SELECT COUNT(*) FROM pulse_ai_conversations),
    'conversationMessages', (SELECT COUNT(*) FROM pulse_ai_conversation_messages),
    'systemInquiryRuns', (SELECT COUNT(*) FROM pulse_ai_system_inquiry_runs),
    'systemToolEvents', (SELECT COUNT(*) FROM pulse_ai_system_tool_events),
    'analyticsRuns', (SELECT COUNT(*) FROM enterprise_report_runs),
    'analyticsSavedViews', (SELECT COUNT(*) FROM enterprise_report_saved_views),
    'analyticsExports', (SELECT COUNT(*) FROM enterprise_report_exports)
  );")"

echo "PROJECTPULSE_MIGRATIONS_054_055_TABLE_ROWS=$NEW_TABLE_ROWS"
echo "PROJECTPULSE_MIGRATIONS_054_055_OPERATIONAL_COUNTS=UNCHANGED"
echo "PROJECTPULSE_MIGRATIONS_054_055_INVARIANTS=VERIFIED"
echo "PROJECTPULSE_PROVIDER_CREDENTIALS_CHANGED=NO"
echo "PROJECTPULSE_PRIVATE_MODEL_CONFIGURED=NO"
if [[ "$MODE" == apply ]]; then
  echo "PROJECTPULSE_MIGRATIONS_054_055_RESULT=APPLIED_OR_ALREADY_PRESENT"
else
  echo "PROJECTPULSE_MIGRATIONS_054_055_RESULT=VERIFY_ONLY_PASS"
fi
