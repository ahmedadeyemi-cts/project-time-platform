#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="e3d68ddcac51ba7f2eb46f71d7930daa2da38b58"
EXPECTED_MIGRATION_SHA256="5959dc299cb6fbd248db6a69bf0a21bc9f65340bd437a44b5cf5e388a22ae52c"
RELEASE_ROOT="${1:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
MIGRATION_FILE="079_coordinated_runtime_ai_document_rbac_repair.sql"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ "$EXPECTED_DATABASE_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] ||
  fail "PROJECTPULSE_TEST_DATABASE_NAME must be an exact PostgreSQL identifier."
[[ "$MODE" == apply || "$MODE" == verify ]] ||
  fail "MAIN_RELEASE_MIGRATION_MODE must be apply or verify."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."

[[ -n "${PGHOST:-}" ]] || fail "PGHOST is not configured."
[[ "${PGPORT:-}" =~ ^[0-9]{1,5}$ ]] || fail "PGPORT is not valid."
[[ "${PGDATABASE:-}" == "$EXPECTED_DATABASE_NAME" ]] ||
  fail "PGDATABASE does not match the protected Test database name."
[[ -n "${PGUSER:-}" ]] || fail "PGUSER is not configured."
[[ -n "${PGPASSWORD:-}" ]] || fail "PGPASSWORD is not configured."

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] ||
  fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

[[ -f "$MIGRATION_ROOT/$MIGRATION_FILE" ]] || fail "Migration 079 is missing."
[[ -f "$MIGRATION_ROOT/SHA256SUMS" ]] || fail "Migration checksum manifest is missing."
mapfile -t ACTUAL_SQL_FILES < <(
  for path in "$MIGRATION_ROOT"/*.sql; do
    [[ -f "$path" ]] && basename "$path"
  done | LC_ALL=C sort
)
[[ "${#ACTUAL_SQL_FILES[@]}" == 1 && "${ACTUAL_SQL_FILES[0]}" == "$MIGRATION_FILE" ]] ||
  fail "Migration image must contain exactly migration 079."
[[ "$(wc -l < "$MIGRATION_ROOT/SHA256SUMS" | tr -d ' ')" == 1 ]] ||
  fail "SHA256SUMS must contain exactly one entry."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."
[[ "$(sha256sum "$MIGRATION_ROOT/$MIGRATION_FILE" | awk '{print $1}')" == "$EXPECTED_MIGRATION_SHA256" ]] ||
  fail "Migration 079 source bytes do not match the reviewed release."
[[ "$(grep -c '^BEGIN;$' "$MIGRATION_ROOT/$MIGRATION_FILE")" == 1 ]] ||
  fail "Migration 079 must contain one top-level BEGIN."
[[ "$(grep -c '^COMMIT;$' "$MIGRATION_ROOT/$MIGRATION_FILE")" == 1 ]] ||
  fail "Migration 079 must contain one top-level COMMIT."
echo "COORDINATED_MIGRATION_079_SOURCE=VERIFIED"

psql --no-psqlrc --set=ON_ERROR_STOP=1 --set=expected_database_name="$EXPECTED_DATABASE_NAME" <<'SQL'
\set ON_ERROR_STOP on
BEGIN READ ONLY;
SET LOCAL search_path = public, pg_catalog;
SELECT set_config('projectpulse.release.expected_database', :'expected_database_name', true) AS value \gset release_database_
DO $database_identity$
BEGIN
  IF current_database() <> current_setting('projectpulse.release.expected_database') THEN
    RAISE EXCEPTION 'Connected database does not match the protected Test database identity.';
  END IF;
  IF to_regclass('public.projects') IS NULL
     OR to_regclass('public.schema_migrations') IS NULL
     OR to_regclass('public.project_intake_documents') IS NULL
     OR to_regclass('public.work_register_documents') IS NULL THEN
    RAISE EXCEPTION 'The protected Test database sentinel tables are unavailable.';
  END IF;
END
$database_identity$;
COMMIT;
\echo COORDINATED_MIGRATION_079_DATABASE_IDENTITY=VERIFIED
SQL

if [[ "$MODE" == apply ]]; then
  psql --no-psqlrc --set=ON_ERROR_STOP=1 -f "$MIGRATION_ROOT/$MIGRATION_FILE"
  echo "COORDINATED_MIGRATION_079_APPLY=COMPLETED"
else
  echo "COORDINATED_MIGRATION_079_APPLY=SKIPPED_VERIFY_MODE"
fi

psql --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
\set ON_ERROR_STOP on
BEGIN READ ONLY;
SET LOCAL search_path = public, pg_catalog;
DO $migration_079_contract$
BEGIN
  IF (SELECT COUNT(*) FROM schema_migrations
      WHERE migration_id='079_coordinated_runtime_ai_document_rbac_repair') <> 1 THEN
    RAISE EXCEPTION 'Migration 079 is missing or duplicated in schema_migrations.';
  END IF;
  IF to_regprocedure('public.projectpulse079_sync_work_register_document()') IS NULL
     OR to_regclass('public.module079_role_grants') IS NULL THEN
    RAISE EXCEPTION 'Migration 079 runtime objects are unavailable.';
  END IF;
  IF NOT EXISTS (
      SELECT 1 FROM information_schema.columns
      WHERE table_schema='public'
        AND table_name='project_intake_documents'
        AND column_name='work_register_document_id'
        AND data_type='uuid') THEN
    RAISE EXCEPTION 'The Work Register bridge owner column is unavailable.';
  END IF;
  IF (SELECT COUNT(*) FROM pg_trigger
      WHERE tgrelid='public.work_register_documents'::regclass
        AND NOT tgisinternal
        AND tgname IN (
          'trg_projectpulse079_sync_work_register_document',
          'trg_projectpulse079_archive_deleted_work_register_document')) <> 2 THEN
    RAISE EXCEPTION 'Migration 079 trigger contract is incomplete.';
  END IF;
  IF EXISTS (
      SELECT 1
      FROM work_register_documents source
      LEFT JOIN project_intake_documents bridge
        ON bridge.work_register_document_id=source.work_register_document_id
      WHERE COALESCE(source.upload_source, '')='local_file'
        AND COALESCE(source.stored_file_path, '')<>''
        AND bridge.project_intake_document_id IS NULL) THEN
    RAISE EXCEPTION 'A durable Work Register upload is missing its private-document bridge.';
  END IF;
  IF EXISTS (
      SELECT 1
      FROM project_intake_documents bridge
      JOIN work_register_documents source
        ON source.work_register_document_id=bridge.work_register_document_id
      WHERE bridge.upload_source='work_register_bridge'
        AND (COALESCE(source.upload_source, '')<>'local_file'
          OR COALESCE(source.stored_file_path, '')='')) THEN
    RAISE EXCEPTION 'A link-only Work Register document entered the private pipeline.';
  END IF;
  IF EXISTS (
      SELECT 1
      FROM module079_role_grants recorded
      LEFT JOIN app_role_permissions active
        ON active.app_role_id=recorded.app_role_id
       AND active.app_permission_id=recorded.app_permission_id
      WHERE active.app_role_permission_id IS NULL) THEN
    RAISE EXCEPTION 'A recorded Module 001A role grant is not active.';
  END IF;
END
$migration_079_contract$;
COMMIT;
\echo COORDINATED_MIGRATION_079_RUNTIME_CONTRACT=VERIFIED
SQL

echo "COORDINATED_MIGRATION_079_TEST_DATABASE=APPLIED_OR_VERIFIED"
