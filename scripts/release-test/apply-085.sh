#!/usr/bin/env bash
set -Eeuo pipefail

RELEASE_ROOT="${1:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATION_FILE="085_module_019_document_access_storage_repair.sql"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ "${ASPNETCORE_ENVIRONMENT:-${PROJECTPULSE_ENVIRONMENT:-}}" =~ ^([Tt][Ee][Ss][Tt])$ ]] ||
  fail "Migration 085 application is restricted to Test by this release script."
[[ "$EXPECTED_DATABASE_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] ||
  fail "PROJECTPULSE_TEST_DATABASE_NAME must be an exact PostgreSQL identifier."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "MAIN_RELEASE_MIGRATION_MODE must be apply or verify."
[[ "${PGDATABASE:-}" == "$EXPECTED_DATABASE_NAME" ]] || fail "PGDATABASE does not match protected Test."
[[ -n "${PGHOST:-}" && -n "${PGUSER:-}" && -n "${PGPASSWORD:-}" ]] || fail "PostgreSQL connection variables are incomplete."
command -v psql >/dev/null || fail "psql is required."
[[ -f "$RELEASE_ROOT/database/migrations/$MIGRATION_FILE" ]] || fail "Migration 085 is missing."

psql --no-psqlrc --set=ON_ERROR_STOP=1 --set=expected_database_name="$EXPECTED_DATABASE_NAME" <<'SQL'
\set ON_ERROR_STOP on
BEGIN READ ONLY;
SELECT set_config('projectpulse.release.expected_database', :'expected_database_name', true);
DO $identity$
BEGIN
  IF current_database() <> current_setting('projectpulse.release.expected_database') THEN
    RAISE EXCEPTION 'Connected database is not protected Test.';
  END IF;
  IF to_regclass('public.project_intake_documents') IS NULL
     OR to_regclass('public.work_register_documents') IS NULL THEN
    RAISE EXCEPTION 'Document sentinel tables are unavailable.';
  END IF;
END;
$identity$;
COMMIT;
SQL

if [[ "$MODE" == apply ]]; then
  psql --no-psqlrc --set=ON_ERROR_STOP=1 -f "$RELEASE_ROOT/database/migrations/$MIGRATION_FILE"
  echo "MODULE019_MIGRATION_085_APPLY=COMPLETED"
else
  echo "MODULE019_MIGRATION_085_APPLY=SKIPPED_VERIFY_MODE"
fi

psql --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
\set ON_ERROR_STOP on
BEGIN READ ONLY;
DO $contract$
BEGIN
  IF (SELECT COUNT(*) FROM schema_migrations
      WHERE migration_id = '085_module_019_document_access_storage_repair') <> 1 THEN
    RAISE EXCEPTION 'Migration 085 is missing or duplicated.';
  END IF;
  IF to_regprocedure('public.projectpulse085_normalize_upload_path(text)') IS NULL THEN
    RAISE EXCEPTION 'Migration 085 path normalizer is unavailable.';
  END IF;
  IF (SELECT COUNT(*) FROM pg_trigger
      WHERE NOT tgisinternal
        AND tgname IN (
          'trg_projectpulse085_normalize_work_register_upload_path',
          'trg_projectpulse085_normalize_intake_upload_path')) <> 2 THEN
    RAISE EXCEPTION 'Migration 085 normalization triggers are incomplete.';
  END IF;
END;
$contract$;
COMMIT;
SQL

echo "MODULE019_MIGRATION_085_TEST_DATABASE=APPLIED_OR_VERIFIED"
