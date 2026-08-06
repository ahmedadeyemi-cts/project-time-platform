#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="64c3168778957f39203c4a17377418e0a8f1ed23"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ "$EXPECTED_DATABASE_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] ||
  fail "PROJECTPULSE_TEST_DATABASE_NAME must be an exact PostgreSQL identifier."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "MAIN_RELEASE_MIGRATION_MODE must be apply or verify."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."

PSQL_TARGET=()
if [[ -n "$DATABASE_URL" ]]; then
  PSQL_TARGET=("$DATABASE_URL")
else
  [[ -n "${PGHOST:-}" ]] || fail "PGHOST is not configured."
  [[ "${PGPORT:-}" =~ ^[0-9]{1,5}$ ]] || fail "PGPORT is not valid."
  [[ "${PGDATABASE:-}" == "$EXPECTED_DATABASE_NAME" ]] || fail "PGDATABASE does not match the protected Test database name."
  [[ -n "${PGUSER:-}" ]] || fail "PGUSER is not configured."
  [[ -n "${PGPASSWORD:-}" ]] || fail "PGPASSWORD is not configured."
fi

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] ||
  fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

FILES=(
  071_ai_runtime_production_hardening.sql
  072_celar_ai_conversation_attachments.sql
  073_module_033_project_forge_interactive.sql
)
HASHES=(
  108f898a0f5e7f76833d42741ba1caa9a0f1ff427db17728cf5a5008a91ee6e7
  b1a898d72916027f02f7ea5578facd7d9149436b2db9e11dca833d74a7cecf1f
  e4280b1f020f9aaed4376da4d6e687706ba9805e70bc2adb8cb23ffdb30ec4c6
)
[[ -f "$MIGRATION_ROOT/SHA256SUMS" ]] || fail "Migration checksum manifest is missing."
mapfile -t ACTUAL_FILES < <(
  for path in "$MIGRATION_ROOT"/*.sql; do
    [[ -f "$path" ]] && basename "$path"
  done | LC_ALL=C sort
)
diff -u <(printf '%s\n' "${FILES[@]}" | LC_ALL=C sort) <(printf '%s\n' "${ACTUAL_FILES[@]}") ||
  fail "Migration image must contain exactly migrations 071, 072, and 073."
[[ "$(wc -l < "$MIGRATION_ROOT/SHA256SUMS" | tr -d ' ')" == "3" ]] ||
  fail "SHA256SUMS must contain exactly three entries."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."

for index in 0 1 2; do
  file="${FILES[$index]}"
  actual="$(sha256sum "$MIGRATION_ROOT/$file" | awk '{print $1}')"
  [[ "$actual" == "${HASHES[$index]}" ]] || fail "Unexpected source bytes for $file."
  [[ "$(grep -c '^BEGIN;$' "$MIGRATION_ROOT/$file")" == "1" ]] ||
    fail "$file must contain one top-level BEGIN."
  [[ "$(grep -c '^COMMIT;$' "$MIGRATION_ROOT/$file")" == "1" ]] ||
    fail "$file must contain one top-level COMMIT."
done
echo "MAIN_RELEASE_MIGRATION_SOURCE=VERIFIED"

BODY_ROOT="$(mktemp -d)"
cleanup() {
  local status=$?
  rm -rf "$BODY_ROOT"
  unset DATABASE_URL PGPASSWORD
  exit "$status"
}
trap cleanup EXIT INT TERM

for file in "${FILES[@]}"; do
  sed -e '/^BEGIN;$/d' -e '/^COMMIT;$/d' "$MIGRATION_ROOT/$file" > "$BODY_ROOT/$file"
done

APPLY_BOOL=false
[[ "$MODE" == apply ]] && APPLY_BOOL=true

psql "${PSQL_TARGET[@]}" \
  --no-psqlrc \
  --set=ON_ERROR_STOP=1 \
  --set=release_apply="$APPLY_BOOL" \
  --set=expected_database_name="$EXPECTED_DATABASE_NAME" \
  --set=body071="$BODY_ROOT/${FILES[0]}" \
  --set=body072="$BODY_ROOT/${FILES[1]}" \
  --set=body073="$BODY_ROOT/${FILES[2]}" <<'SQL'
\set ON_ERROR_STOP on
BEGIN;
SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '20min';
SELECT set_config('projectpulse.release.expected_database', :'expected_database_name', true) AS value \gset release_database_

DO $release_database_identity$
BEGIN
  IF current_database() <> current_setting('projectpulse.release.expected_database') THEN
    RAISE EXCEPTION 'Connected database does not match the protected Test database identity.';
  END IF;
  IF to_regclass('public.projects') IS NULL THEN
    RAISE EXCEPTION 'The protected Test database sentinel table is unavailable.';
  END IF;
END
$release_database_identity$;
\echo DATABASE_IDENTITY=TEST_SENTINEL_VERIFIED

SELECT pg_advisory_xact_lock(71072073);

DO $release_prerequisites$
DECLARE
  required_id text;
BEGIN
  IF to_regclass('public.schema_migrations') IS NULL THEN
    RAISE EXCEPTION 'The canonical schema migration ledger is unavailable.';
  END IF;
  FOREACH required_id IN ARRAY ARRAY[
    '052_pulse_ai_private_document_runtime',
    '053_pulse_ai_private_rag_orchestration',
    '054_pulse_ai_system_intelligence_conversations',
    '061_celar_ai_capability_routing',
    '070_module_033_project_forge'
  ] LOOP
    IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id=required_id) <> 1 THEN
      RAISE EXCEPTION 'Required prerequisite migration is missing or duplicated: %', required_id;
    END IF;
  END LOOP;
  IF EXISTS(
    SELECT 1 FROM schema_migrations
    WHERE migration_id='074_module_066_project_flowhive_production'
  ) THEN
    RAISE EXCEPTION 'FlowHive Migration 074 is outside this exact release and must not already be applied.';
  END IF;
END
$release_prerequisites$;

\if :release_apply
  SELECT
    COUNT(*) = 3 AND COUNT(DISTINCT migration_id) = 3 AS complete,
    COUNT(*) <> 0 AND NOT (COUNT(*) = 3 AND COUNT(DISTINCT migration_id) = 3) AS inconsistent
  FROM schema_migrations
  WHERE migration_id IN (
    '071_ai_runtime_production_hardening',
    '072_celar_ai_conversation_attachments',
    '073_module_033_project_forge_interactive'
  )
  \gset release_target_
  \if :release_target_inconsistent
    \echo ERROR: Refusing partial, duplicate, or mixed 071-073 ledger state; explicit operator recovery is required.
    \quit 3
  \endif
  \if :release_target_complete
    \echo MAIN_RELEASE_TARGET_LEDGER=COMPLETE_RECONCILING
  \else
    \echo MAIN_RELEASE_TARGET_LEDGER=ABSENT_APPLYING
  \endif
\endif

CREATE TEMP TABLE release_business_counts AS
SELECT
  (SELECT COUNT(*) FROM app_users) AS app_users,
  (SELECT COUNT(*) FROM projects) AS projects,
  (SELECT COUNT(*) FROM project_assignments) AS project_assignments,
  (SELECT COUNT(*) FROM time_entries) AS time_entries,
  (SELECT COUNT(*) FROM project_tasks) AS project_tasks;

SELECT EXISTS(
  SELECT 1 FROM schema_migrations
  WHERE migration_id='071_ai_runtime_production_hardening'
) AS present \gset m071_
\if :m071_present
  \echo MAIN_RELEASE_071=ALREADY_PRESENT_VERIFYING
\else
  \if :release_apply
    \echo MAIN_RELEASE_071=APPLYING
    \i :body071
  \else
    \echo ERROR: Migration 071 is absent in verify mode.
    \quit 3
  \endif
\endif

SELECT EXISTS(
  SELECT 1 FROM schema_migrations
  WHERE migration_id='072_celar_ai_conversation_attachments'
) AS present \gset m072_
\if :m072_present
  \echo MAIN_RELEASE_072=ALREADY_PRESENT_VERIFYING
\else
  \if :release_apply
    \echo MAIN_RELEASE_072=APPLYING
    \i :body072
  \else
    \echo ERROR: Migration 072 is absent in verify mode.
    \quit 3
  \endif
\endif

SELECT EXISTS(
  SELECT 1 FROM schema_migrations
  WHERE migration_id='073_module_033_project_forge_interactive'
) AS present \gset m073_
\if :m073_present
  \echo MAIN_RELEASE_073=ALREADY_PRESENT_VERIFYING
\else
  \if :release_apply
    \echo MAIN_RELEASE_073=APPLYING
    \i :body073
  \else
    \echo ERROR: Migration 073 is absent in verify mode.
    \quit 3
  \endif
\endif

DO $release_postconditions$
DECLARE
  missing text[];
  before_counts record;
BEGIN
  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='071_ai_runtime_production_hardening') <> 1
     OR NOT EXISTS(
       SELECT 1 FROM schema_migrations
       WHERE migration_id='071_ai_runtime_production_hardening'
         AND applied_at IS NOT NULL
         AND description='Migration-owned Module 064 schemas, version-aware encryption key IDs, shared probe evidence, and fenced private-document worker leases'
     ) THEN
    RAISE EXCEPTION 'Migration 071 ledger evidence is incomplete.';
  END IF;
  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='072_celar_ai_conversation_attachments') <> 1
     OR NOT EXISTS(
       SELECT 1 FROM schema_migrations
       WHERE migration_id='072_celar_ai_conversation_attachments'
         AND applied_at IS NOT NULL
         AND description='Module 011 private conversation attachments using the hardened Celar AI document runtime'
     ) THEN
    RAISE EXCEPTION 'Migration 072 ledger evidence is incomplete.';
  END IF;
  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id='073_module_033_project_forge_interactive') <> 1
     OR NOT EXISTS(
       SELECT 1 FROM schema_migrations
       WHERE migration_id='073_module_033_project_forge_interactive'
         AND applied_at IS NOT NULL
         AND description='Add Module 033 interactive task revisions, canonical dependencies, primary assignments, review revision evidence, RBAC, and holiday-aware schedule helpers'
     ) THEN
    RAISE EXCEPTION 'Migration 073 ledger evidence is incomplete.';
  END IF;

  SELECT array_agg(name) INTO missing
  FROM unnest(ARRAY[
    'ai_provider_secrets',
    'ai_provider_settings',
    'ai_provider_probe_evidence',
    'pulse_ai_conversation_attachments',
    'pulse_ai_conversation_attachment_purge_audit',
    'project_task_dependencies'
  ]) AS name
  WHERE to_regclass('public.' || name) IS NULL;
  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'Release tables are missing: %', missing;
  END IF;

  IF NOT EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='ai_provider_secrets' AND column_name='encryption_key_id'
  ) OR NOT EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='pulse_ai_document_processing_jobs' AND column_name='lease_token'
  ) OR NOT EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='pulse_ai_document_processing_jobs' AND column_name='lease_generation'
  ) OR NOT EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='pulse_ai_document_processing_jobs' AND column_name='lease_heartbeat_at'
  ) OR to_regclass('public.ix_pulse_ai_document_jobs_lease_fence') IS NULL THEN
    RAISE EXCEPTION 'Migration 071 source/key/lease fencing is incomplete.';
  END IF;

  IF NOT EXISTS(
    SELECT 1 FROM pg_constraint
    WHERE conname='ck_project_intake_documents_origin_owner'
      AND conrelid='public.project_intake_documents'::regclass
  ) OR (
    SELECT COUNT(*) FROM pg_trigger
    WHERE tgname IN (
      'trg_pulse_ai_072_attachment_updated_at',
      'trg_pulse_ai_072_conversation_owner_immutable',
      'trg_pulse_ai_072_chat_document_delete_guard',
      'trg_pulse_ai_072_purged_answer_immutable',
      'trg_pulse_ai_072_purged_answer_feedback_guard',
      'trg_pulse_ai_072_document_attachment_ownership',
      'trg_pulse_ai_072_attachment_document_ownership'
    ) AND NOT tgisinternal AND tgenabled <> 'D'
  ) <> 7 THEN
    RAISE EXCEPTION 'Migration 072 ownership, purge, or immutability controls are incomplete.';
  END IF;
  IF NOT EXISTS(
    SELECT 1 FROM app_permissions
    WHERE permission_code='ATTACH_CELAR_AI_CHAT_DOCUMENTS' AND module_code='011'
  ) OR NOT EXISTS(
    SELECT 1 FROM app_feature_catalog
    WHERE feature_code='CELAR_AI_CHAT_ATTACHMENTS'
      AND required_permission_code='ATTACH_CELAR_AI_CHAT_DOCUMENTS'
      AND is_active=TRUE
  ) THEN
    RAISE EXCEPTION 'Migration 072 permission or feature evidence is incomplete.';
  END IF;
  IF EXISTS (
    WITH ask_roles AS (
      SELECT arp.app_role_id
      FROM app_role_permissions arp
      JOIN app_permissions p ON p.app_permission_id=arp.app_permission_id
      WHERE p.permission_code='ASK_PULSE_AI_SYSTEM_INTELLIGENCE'
    ), attachment_roles AS (
      SELECT arp.app_role_id
      FROM app_role_permissions arp
      JOIN app_permissions p ON p.app_permission_id=arp.app_permission_id
      WHERE p.permission_code='ATTACH_CELAR_AI_CHAT_DOCUMENTS'
    ), delta AS (
      (SELECT app_role_id FROM ask_roles EXCEPT SELECT app_role_id FROM attachment_roles)
      UNION ALL
      (SELECT app_role_id FROM attachment_roles EXCEPT SELECT app_role_id FROM ask_roles)
    )
    SELECT 1 FROM delta
  ) THEN
    RAISE EXCEPTION 'Migration 072 attachment role set differs from the Ask Celar AI role set.';
  END IF;

  IF NOT EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='project_tasks' AND column_name='revision_number'
  ) OR NOT EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='project_assignments' AND column_name='is_primary_assignee'
  ) OR NOT EXISTS(
    SELECT 1 FROM information_schema.columns
    WHERE table_schema='public' AND table_name='project_forge_plan_assignments' AND column_name='reviewed_task_revision'
  ) OR (
    SELECT COUNT(*) FROM pg_trigger
    WHERE tgname IN (
      'trg_project_tasks_revision_073',
      'trg_project_assignments_revision_073',
      'trg_project_task_dependencies_validate_073',
      'trg_project_task_dependencies_revision_073',
      'trg_project_task_dependencies_audit_073',
      'trg_project_forge_task_details_parent_073'
    ) AND NOT tgisinternal AND tgenabled <> 'D'
  ) <> 6 OR NOT EXISTS(
    SELECT 1 FROM app_permissions
    WHERE permission_code='UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033' AND module_code='033'
  ) THEN
    RAISE EXCEPTION 'Migration 073 interactive Project Forge controls are incomplete.';
  END IF;

  SELECT * INTO before_counts FROM release_business_counts;
  IF before_counts.app_users <> (SELECT COUNT(*) FROM app_users)
     OR before_counts.projects <> (SELECT COUNT(*) FROM projects)
     OR before_counts.project_assignments <> (SELECT COUNT(*) FROM project_assignments)
     OR before_counts.time_entries <> (SELECT COUNT(*) FROM time_entries)
     OR before_counts.project_tasks <> (SELECT COUNT(*) FROM project_tasks) THEN
    RAISE EXCEPTION 'Migrations 071-073 changed protected business-row counts.';
  END IF;
END
$release_postconditions$;

COMMIT;
SQL

echo "MAIN_RELEASE_MIGRATIONS_071_073=$([ "$MODE" = apply ] && echo APPLIED_OR_VERIFIED || echo VERIFY_ONLY_PASS)"
