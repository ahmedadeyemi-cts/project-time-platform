#!/usr/bin/env bash
set -Eeuo pipefail

RELEASE_ROOT="${1:-}"
EXPECTED_RELEASE_COMMIT="${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATION_NAME="080_celar_ai_internal_data_intelligence.sql"
MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"

fail() { echo "ERROR: $*" >&2; exit 1; }

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ "$EXPECTED_RELEASE_COMMIT" =~ ^[0-9a-f]{40}$ ]] || fail "MAIN_RELEASE_EXPECTED_RELEASE_COMMIT must be an exact commit."
[[ "$EXPECTED_DATABASE_NAME" =~ ^[A-Za-z_][A-Za-z0-9_]{0,62}$ ]] || fail "PROJECTPULSE_TEST_DATABASE_NAME must be an exact PostgreSQL identifier."
[[ "$MODE" == apply || "$MODE" == verify ]] || fail "MAIN_RELEASE_MIGRATION_MODE must be apply or verify."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."

[[ -n "${PGHOST:-}" && -n "${PGUSER:-}" && -n "${PGPASSWORD:-}" ]] || fail "The protected database connection is incomplete."
[[ "${PGDATABASE:-}" == "$EXPECTED_DATABASE_NAME" ]] || fail "PGDATABASE does not match the protected Test database."
[[ "${PGPORT:-}" =~ ^[0-9]{1,5}$ ]] || fail "PGPORT is invalid."

ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "The migration image release marker is incorrect."

mapfile -t ACTUAL_SQL_FILES < <(
  for path in "$MIGRATION_ROOT"/*.sql; do
    [[ -f "$path" ]] && basename "$path"
  done | LC_ALL=C sort
)
[[ "${ACTUAL_SQL_FILES[*]}" == "$MIGRATION_NAME" ]] || fail "The migration image must contain exactly migration 080."
[[ "$(wc -l < "$MIGRATION_ROOT/SHA256SUMS" | tr -d ' ')" == 1 ]] || fail "SHA256SUMS must contain exactly one entry."
(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum validation failed."

psql --no-psqlrc --set=ON_ERROR_STOP=1 --set=expected_database_name="$EXPECTED_DATABASE_NAME" <<'SQL'
BEGIN READ ONLY;
SET LOCAL search_path = public, pg_catalog;
SELECT set_config('projectpulse.release.expected_database', :'expected_database_name', true);
DO $identity$
BEGIN
  IF current_database() <> current_setting('projectpulse.release.expected_database')
     OR to_regclass('public.schema_migrations') IS NULL
     OR to_regclass('public.app_users') IS NULL
     OR to_regclass('public.projects') IS NULL
     OR to_regclass('public.project_tasks') IS NULL
     OR to_regclass('public.project_assignments') IS NULL THEN
    RAISE EXCEPTION 'The protected Test database identity is incorrect.';
  END IF;
END;
$identity$;
COMMIT;
SQL

if [[ "$MODE" == apply ]]; then
  psql --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
BEGIN READ ONLY;
SET LOCAL search_path = public, pg_catalog;
DO $private_runtime_guard$
BEGIN
  IF EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id = '081_celar_ai_private_runtime_activation'
  ) THEN
    RAISE EXCEPTION 'Private-runtime migration 081 is already present; the 080-only controller refuses to continue.';
  END IF;
END;
$private_runtime_guard$;
COMMIT;
SQL
  psql --no-psqlrc --set=ON_ERROR_STOP=1 -f "$MIGRATION_ROOT/$MIGRATION_NAME"
fi

psql --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
BEGIN READ ONLY;
SET LOCAL search_path = public, pg_catalog;
DO $contract$
DECLARE
  legacy_candidate_count INTEGER;
  seeded_alias_count INTEGER;
BEGIN
  IF (SELECT COUNT(*) FROM schema_migrations
      WHERE migration_id = '080_celar_ai_internal_data_intelligence') <> 1 THEN
    RAISE EXCEPTION 'Internal-data migration 080 is not registered exactly once.';
  END IF;
  IF EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id = '081_celar_ai_private_runtime_activation'
  ) THEN
    RAISE EXCEPTION 'Private-runtime migration 081 must remain absent from this release.';
  END IF;
  IF to_regclass('public.celar_ai_identity_aliases') IS NULL
     OR to_regclass('public.ux_celar_ai_identity_alias_user_value') IS NULL
     OR to_regclass('public.ix_celar_ai_identity_alias_verified_lookup') IS NULL
     OR to_regprocedure('public.projectpulse080_touch_identity_alias()') IS NULL THEN
    RAISE EXCEPTION 'Migration 080 identity-alias contract is incomplete.';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app')
     AND NOT has_table_privilege('ptp_app', 'celar_ai_identity_aliases', 'SELECT') THEN
    RAISE EXCEPTION 'The application role lacks read access to verified aliases.';
  END IF;

  SELECT COUNT(*) INTO legacy_candidate_count
  FROM app_users
  WHERE is_active = TRUE
    AND regexp_replace(lower(btrim(display_name)), '[^a-z0-9]+', '', 'g') = 'kevindamish';
  SELECT COUNT(*) INTO seeded_alias_count
  FROM celar_ai_identity_aliases
  WHERE alias_text = 'Kevin Damisch'
    AND is_verified = TRUE
    AND verification_source = 'migration_080_known_directory_correction'
    AND is_active = TRUE;
  IF (legacy_candidate_count = 1 AND seeded_alias_count <> 1)
     OR (legacy_candidate_count <> 1 AND seeded_alias_count <> 0) THEN
    RAISE EXCEPTION 'The guarded known-directory alias seed is inconsistent.';
  END IF;
END;
$contract$;
COMMIT;
SQL

echo "CELAR_AI_INTERNAL_DATA_MIGRATION_080_${MODE^^}=PASSED"
echo "PRIVATE_RUNTIME_MIGRATION_081=ABSENT"
