#!/usr/bin/env bash
set -Eeuo pipefail

RELEASE_ROOT="${1:-}"
EXPECTED_RELEASE_COMMIT="${MAIN_RELEASE_EXPECTED_RELEASE_COMMIT:-}"
EXPECTED_DATABASE_NAME="${PROJECTPULSE_TEST_DATABASE_NAME:-}"
MODE="${MAIN_RELEASE_MIGRATION_MODE:-verify}"
MIGRATION_NAME="083_module_083_autonomous_control_plane.sql"
MIGRATION_ID="083_module_083_autonomous_control_plane"
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
[[ "${ACTUAL_SQL_FILES[*]}" == "$MIGRATION_NAME" ]] || fail "The migration image must contain exactly migration 083."
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
     OR to_regclass('public.app_feature_catalog') IS NULL
     OR to_regclass('public.full_future_loop_items') IS NULL
     OR to_regclass('public.full_future_loop_events') IS NULL
     OR to_regclass('public.full_future_loop_artifacts') IS NULL THEN
    RAISE EXCEPTION 'The protected Test database identity or Module 083 migration 082 prerequisites are incorrect.';
  END IF;
END;
$identity$;
COMMIT;
SQL

APPLIED="$(
  psql --no-psqlrc --set=ON_ERROR_STOP=1 -Atqc \
    "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='$MIGRATION_ID')::text;"
)"
[[ "$APPLIED" == true || "$APPLIED" == false ]] || fail "Could not determine migration 083 ledger state."

if [[ "$MODE" == apply ]]; then
  if [[ "$APPLIED" == false ]]; then
    psql --no-psqlrc --set=ON_ERROR_STOP=1 -f "$MIGRATION_ROOT/$MIGRATION_NAME"
    echo "MODULE_083_MIGRATION_083_ACTION=APPLIED"
  else
    echo "MODULE_083_MIGRATION_083_ACTION=ALREADY_APPLIED"
  fi
fi

psql --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
BEGIN READ ONLY;
SET LOCAL search_path = public, pg_catalog;
DO $contract$
DECLARE
  orchestration_table_count INTEGER;
  adapter_count INTEGER;
  disabled_adapter_count INTEGER;
  permission_count INTEGER;
  feature_count INTEGER;
  immutable_trigger_count INTEGER;
  super_admin_grant_count INTEGER;
BEGIN
  IF (SELECT COUNT(*) FROM schema_migrations
      WHERE migration_id='083_module_083_autonomous_control_plane') <> 1 THEN
    RAISE EXCEPTION 'Migration 083 is not registered exactly once.';
  END IF;

  SELECT COUNT(*) INTO orchestration_table_count
  FROM pg_tables
  WHERE schemaname='public'
    AND tablename IN (
      'full_future_loop_automation_policies',
      'full_future_loop_automation_state',
      'full_future_loop_automation_adapters',
      'full_future_loop_automation_runs',
      'full_future_loop_automation_steps',
      'full_future_loop_automation_approvals',
      'full_future_loop_release_manifests',
      'full_future_loop_automation_evidence',
      'full_future_loop_outbox'
    );
  IF orchestration_table_count <> 9 THEN
    RAISE EXCEPTION 'Module 083 orchestration tables are incomplete.';
  END IF;

  IF (SELECT COUNT(*) FROM full_future_loop_automation_policies
      WHERE policy_version='enterprise-default-v1') <> 1 THEN
    RAISE EXCEPTION 'Module 083 baseline policy is missing.';
  END IF;

  IF NOT EXISTS (
      SELECT 1 FROM full_future_loop_automation_state
      WHERE state_id=1
        AND automation_enabled=FALSE
        AND global_kill_switch=TRUE
        AND dry_run_only=TRUE
        AND revision_number>=1) THEN
    RAISE EXCEPTION 'Module 083 runtime is not fail-closed and dry-run-only.';
  END IF;

  SELECT COUNT(*), COUNT(*) FILTER (WHERE adapter_mode='disabled' AND is_ready=FALSE)
  INTO adapter_count, disabled_adapter_count
  FROM full_future_loop_automation_adapters;
  IF adapter_count <> 7 OR disabled_adapter_count <> 7 THEN
    RAISE EXCEPTION 'Module 083 provider-neutral adapters are not fully disabled.';
  END IF;

  IF EXISTS (
      SELECT 1 FROM full_future_loop_automation_adapters
      WHERE adapter_mode NOT IN ('disabled','dry_run') OR is_ready=TRUE) THEN
    RAISE EXCEPTION 'Module 083 external adapter boundary is violated.';
  END IF;

  IF EXISTS (SELECT 1 FROM full_future_loop_automation_runs WHERE dry_run IS DISTINCT FROM TRUE)
     OR EXISTS (SELECT 1 FROM full_future_loop_release_manifests WHERE is_read_only IS DISTINCT FROM TRUE) THEN
    RAISE EXCEPTION 'Module 083 dry-run or immutable-manifest boundary is violated.';
  END IF;

  SELECT COUNT(*) INTO permission_count
  FROM app_permissions
  WHERE module_code='083'
    AND permission_code LIKE '%FULL_FUTURE_LOOP_AUTOMATION_083';
  IF permission_count <> 4 THEN
    RAISE EXCEPTION 'Module 083 autonomous permissions are incomplete.';
  END IF;

  SELECT COUNT(*) INTO feature_count
  FROM app_feature_catalog
  WHERE feature_code='FULL_FUTURE_LOOP_AUTOMATION_083'
    AND route_anchor='#full-future-loop'
    AND is_active=TRUE;
  IF feature_count <> 1 THEN
    RAISE EXCEPTION 'Module 083 feature registration is incomplete.';
  END IF;

  SELECT COUNT(*) INTO immutable_trigger_count
  FROM pg_trigger
  WHERE NOT tgisinternal
    AND tgname IN (
      'trg_full_future_loop_automation_policies_immutable_083',
      'trg_full_future_loop_release_manifests_immutable_083',
      'trg_full_future_loop_automation_evidence_immutable_083'
    );
  IF immutable_trigger_count <> 3 THEN
    RAISE EXCEPTION 'Module 083 append-only triggers are incomplete.';
  END IF;

  SELECT COUNT(*) INTO super_admin_grant_count
  FROM app_role_permissions grant_row
  JOIN app_roles role ON role.app_role_id=grant_row.app_role_id
  JOIN app_permissions permission ON permission.app_permission_id=grant_row.app_permission_id
  WHERE upper(role.role_code)='SUPER_ADMINISTRATOR'
    AND role.is_active=TRUE
    AND permission.permission_code LIKE '%FULL_FUTURE_LOOP_AUTOMATION_083';
  IF EXISTS (
      SELECT 1 FROM app_roles
      WHERE upper(role_code)='SUPER_ADMINISTRATOR' AND is_active=TRUE)
     AND super_admin_grant_count <> 4 THEN
    RAISE EXCEPTION 'The active Super Administrator role lacks Module 083 autonomous permissions.';
  END IF;
END;
$contract$;
COMMIT;
SQL

echo "MODULE_083_AUTONOMOUS_CONTROL_PLANE_MIGRATION_083_${MODE^^}=PASSED"
echo "MODULE_083_AUTONOMY_DATABASE_BOUNDARY=DRY_RUN_ONLY"
echo "MODULE_083_EXTERNAL_EXECUTION=DISABLED"
