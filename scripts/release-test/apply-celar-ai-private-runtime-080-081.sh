#!/usr/bin/env bash
set -Eeuo pipefail

RELEASE_ROOT="${1:-}"
EXPECTED_RELEASE_COMMIT="${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
MIGRATIONS=(
  080_celar_ai_internal_data_intelligence.sql
  081_celar_ai_private_runtime_activation.sql
)

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
[[ "${ACTUAL_SQL_FILES[*]}" == "${MIGRATIONS[*]}" ]] || fail "The migration image must contain exactly migrations 080 and 081."
[[ "$(wc -l < "$MIGRATION_ROOT/SHA256SUMS" | tr -d ' ')" == 2 ]] || fail "SHA256SUMS must contain two entries."
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
     OR to_regclass('public.project_intake_documents') IS NULL
     OR to_regclass('public.work_register_documents') IS NULL THEN
    RAISE EXCEPTION 'The protected Test database identity is incorrect.';
  END IF;
END;
$identity$;
COMMIT;
SQL

if [[ "$MODE" == apply ]]; then
  for migration in "${MIGRATIONS[@]}"; do
    psql --no-psqlrc --set=ON_ERROR_STOP=1 -f "$MIGRATION_ROOT/$migration"
  done
fi

psql --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
BEGIN READ ONLY;
SET LOCAL search_path = public, pg_catalog;
DO $contract$
BEGIN
  IF (SELECT COUNT(*) FROM schema_migrations WHERE migration_id IN (
        '080_celar_ai_internal_data_intelligence',
        '081_celar_ai_private_runtime_activation'
      )) <> 2 THEN
    RAISE EXCEPTION 'Celar AI migrations 080 and 081 are not both applied.';
  END IF;
  IF to_regprocedure('public.projectpulse081_supported_file_name(text,text)') IS NULL
     OR to_regprocedure('public.projectpulse081_repair_work_register_bridge_name()') IS NULL THEN
    RAISE EXCEPTION 'The private-runtime document-admission functions are unavailable.';
  END IF;
  IF NOT EXISTS (
      SELECT 1
      FROM app_users service_user
      JOIN app_user_role_assignments assignment
        ON assignment.user_id = service_user.user_id AND assignment.is_active = TRUE
      JOIN app_roles role
        ON role.app_role_id = assignment.app_role_id AND role.is_active = TRUE
      JOIN app_role_permissions role_permission
        ON role_permission.app_role_id = role.app_role_id
      JOIN app_permissions permission
        ON permission.app_permission_id = role_permission.app_permission_id
      WHERE service_user.user_id = '08100000-0000-0000-0000-000000000001'::UUID
        AND service_user.is_active = TRUE
        AND role.role_code = 'CELAR_AI_DOCUMENT_SERVICE'
        AND permission.permission_code = 'QUEUE_PULSE_AI_DOCUMENT_PROCESSING'
  ) THEN
    RAISE EXCEPTION 'The least-privilege document service identity is not authorized.';
  END IF;
  IF EXISTS (
      SELECT 1
      FROM app_roles role
      JOIN app_role_permissions role_permission ON role_permission.app_role_id = role.app_role_id
      JOIN app_permissions permission ON permission.app_permission_id = role_permission.app_permission_id
      WHERE role.role_code = 'CELAR_AI_DOCUMENT_SERVICE'
        AND permission.permission_code <> 'QUEUE_PULSE_AI_DOCUMENT_PROCESSING'
  ) THEN
    RAISE EXCEPTION 'The document service role has permissions outside its least-privilege boundary.';
  END IF;
  IF EXISTS (
      SELECT 1
      FROM project_intake_documents bridge
      JOIN work_register_documents source
        ON source.work_register_document_id = bridge.work_register_document_id
      WHERE bridge.upload_source = 'work_register_bridge'
        AND COALESCE(source.upload_source, '') = 'local_file'
        AND lower(regexp_replace(source.stored_file_path, '^.*/', ''))
            ~ '\.(pdf|docx|xlsx|pptx|txt|csv|json|xml|html|htm|md)$'
        AND lower(bridge.original_file_name)
            !~ '\.(pdf|docx|xlsx|pptx|txt|csv|json|xml|html|htm|md)$'
  ) THEN
    RAISE EXCEPTION 'A bridge document still lacks its proven supported filename extension.';
  END IF;
END;
$contract$;
COMMIT;
SQL

echo "CELAR_AI_PRIVATE_RUNTIME_MIGRATIONS_080_081_${MODE^^}=PASSED"
