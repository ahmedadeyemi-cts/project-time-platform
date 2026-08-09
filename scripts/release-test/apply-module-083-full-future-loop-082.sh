#!/usr/bin/env bash
set -Eeuo pipefail

RELEASE_ROOT="${1:-}"
EXPECTED_RELEASE_COMMIT="${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATION_NAME="082_module_083_full_future_loop.sql"
MIGRATION_ID="082_module_083_full_future_loop"
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
[[ "${ACTUAL_SQL_FILES[*]}" == "$MIGRATION_NAME" ]] || fail "The migration image must contain exactly migration 082 for Module 083."
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
     OR to_regclass('public.app_roles') IS NULL
     OR to_regclass('public.app_permissions') IS NULL
     OR to_regclass('public.app_role_permissions') IS NULL
     OR to_regclass('public.app_feature_catalog') IS NULL THEN
    RAISE EXCEPTION 'The protected Test database identity or canonical Module 083 prerequisites are incorrect.';
  END IF;
END;
$identity$;
COMMIT;
SQL

if [[ "$MODE" == apply ]]; then
  psql --no-psqlrc --set=ON_ERROR_STOP=1 -f "$MIGRATION_ROOT/$MIGRATION_NAME"
fi

psql --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
BEGIN READ ONLY;
SET LOCAL search_path = public, pg_catalog;
DO $contract$
DECLARE
  permission_count INTEGER;
  feature_count INTEGER;
  super_admin_grant_count INTEGER;
  administrator_grant_count INTEGER;
  release_manager_grant_count INTEGER;
BEGIN
  IF (SELECT COUNT(*) FROM schema_migrations
      WHERE migration_id = '082_module_083_full_future_loop') <> 1 THEN
    RAISE EXCEPTION 'Module 083 migration 082 is not registered exactly once.';
  END IF;

  IF to_regclass('public.full_future_loop_items') IS NULL
     OR to_regclass('public.full_future_loop_events') IS NULL
     OR to_regclass('public.full_future_loop_artifacts') IS NULL
     OR to_regclass('public.full_future_loop_082_permissions_created') IS NULL
     OR to_regclass('public.full_future_loop_082_role_grants') IS NULL
     OR to_regclass('public.full_future_loop_items_loop_number_seq') IS NULL THEN
    RAISE EXCEPTION 'Module 083 persistence objects are incomplete.';
  END IF;

  IF to_regprocedure('public.pulse082_touch_full_future_loop_item()') IS NULL
     OR to_regprocedure('public.pulse082_immutable_full_future_loop_evidence()') IS NULL THEN
    RAISE EXCEPTION 'Module 083 lifecycle and immutability functions are incomplete.';
  END IF;

  IF NOT EXISTS (
      SELECT 1 FROM pg_trigger
      WHERE tgrelid = 'public.full_future_loop_items'::regclass
        AND tgname = 'trg_full_future_loop_item_touch_082'
        AND NOT tgisinternal)
     OR NOT EXISTS (
      SELECT 1 FROM pg_trigger
      WHERE tgrelid = 'public.full_future_loop_events'::regclass
        AND tgname = 'trg_full_future_loop_events_immutable_082'
        AND NOT tgisinternal)
     OR NOT EXISTS (
      SELECT 1 FROM pg_trigger
      WHERE tgrelid = 'public.full_future_loop_artifacts'::regclass
        AND tgname = 'trg_full_future_loop_artifacts_immutable_082'
        AND NOT tgisinternal) THEN
    RAISE EXCEPTION 'Module 083 required triggers are incomplete.';
  END IF;

  IF EXISTS (SELECT 1 FROM full_future_loop_items WHERE environment <> 'sandbox')
     OR EXISTS (SELECT 1 FROM full_future_loop_artifacts WHERE is_read_only IS DISTINCT FROM TRUE) THEN
    RAISE EXCEPTION 'Module 083 sandbox or read-only evidence boundary is violated.';
  END IF;

  SELECT COUNT(*) INTO permission_count
  FROM app_permissions
  WHERE permission_code IN (
    'VIEW_FULL_FUTURE_LOOP_083',
    'RUN_FULL_FUTURE_LOOP_SANDBOX_083',
    'MANAGE_FULL_FUTURE_LOOP_083',
    'VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'
  ) AND module_code = '083';
  IF permission_count <> 4 THEN
    RAISE EXCEPTION 'Module 083 RBAC permissions are incomplete.';
  END IF;

  SELECT COUNT(*) INTO feature_count
  FROM app_feature_catalog
  WHERE feature_code = 'FULL_FUTURE_LOOP_083'
    AND module_code = '083'
    AND route_anchor = '#full-future-loop'
    AND required_permission_code = 'VIEW_FULL_FUTURE_LOOP_083'
    AND is_active = TRUE;
  IF feature_count <> 1 THEN
    RAISE EXCEPTION 'Module 083 feature-catalog registration is incomplete.';
  END IF;

  SELECT COUNT(*) INTO super_admin_grant_count
  FROM app_role_permissions arp
  JOIN app_roles r ON r.app_role_id = arp.app_role_id
  JOIN app_permissions p ON p.app_permission_id = arp.app_permission_id
  WHERE upper(r.role_code) = 'SUPER_ADMINISTRATOR'
    AND r.is_active = TRUE
    AND p.permission_code IN (
      'VIEW_FULL_FUTURE_LOOP_083',
      'RUN_FULL_FUTURE_LOOP_SANDBOX_083',
      'MANAGE_FULL_FUTURE_LOOP_083',
      'VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'
    );
  IF EXISTS (SELECT 1 FROM app_roles WHERE upper(role_code) = 'SUPER_ADMINISTRATOR' AND is_active = TRUE)
     AND super_admin_grant_count <> 4 THEN
    RAISE EXCEPTION 'The active Super Administrator role does not have the complete Module 083 permission set.';
  END IF;

  SELECT COUNT(*) INTO administrator_grant_count
  FROM app_role_permissions arp
  JOIN app_roles r ON r.app_role_id = arp.app_role_id
  JOIN app_permissions p ON p.app_permission_id = arp.app_permission_id
  WHERE upper(r.role_code) = 'ADMINISTRATOR'
    AND r.is_active = TRUE
    AND p.permission_code IN (
      'VIEW_FULL_FUTURE_LOOP_083',
      'RUN_FULL_FUTURE_LOOP_SANDBOX_083',
      'MANAGE_FULL_FUTURE_LOOP_083',
      'VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'
    );
  IF EXISTS (SELECT 1 FROM app_roles WHERE upper(role_code) = 'ADMINISTRATOR' AND is_active = TRUE)
     AND administrator_grant_count <> 4 THEN
    RAISE EXCEPTION 'The active Administrator role does not have the complete Module 083 permission set.';
  END IF;

  SELECT COUNT(*) INTO release_manager_grant_count
  FROM app_role_permissions arp
  JOIN app_roles r ON r.app_role_id = arp.app_role_id
  JOIN app_permissions p ON p.app_permission_id = arp.app_permission_id
  WHERE upper(r.role_code) = 'RELEASE_MANAGER'
    AND r.is_active = TRUE
    AND p.permission_code IN (
      'VIEW_FULL_FUTURE_LOOP_083',
      'RUN_FULL_FUTURE_LOOP_SANDBOX_083',
      'MANAGE_FULL_FUTURE_LOOP_083',
      'VIEW_FULL_FUTURE_LOOP_EVIDENCE_083'
    );
  IF EXISTS (SELECT 1 FROM app_roles WHERE upper(role_code) = 'RELEASE_MANAGER' AND is_active = TRUE)
     AND release_manager_grant_count <> 4 THEN
    RAISE EXCEPTION 'The active Release Manager role does not have the complete Module 083 permission set.';
  END IF;
END;
$contract$;
COMMIT;
SQL

echo "MODULE_083_FULL_FUTURE_LOOP_MIGRATION_082_${MODE^^}=PASSED"
echo "MODULE_083_ENVIRONMENT_BOUNDARY=SANDBOX_ONLY"
echo "MODULE_083_EVIDENCE_MODE=APPEND_ONLY"