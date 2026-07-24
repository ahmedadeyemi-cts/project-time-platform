#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="19c7bee92e513b79ef83cc3b6ad3d2a781aa5b67"
RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."
command -v psql >/dev/null || fail "psql is required."
command -v sha256sum >/dev/null || fail "sha256sum is required."

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release marker is missing."
fi
[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] || fail "Unexpected release commit: $ACTUAL_RELEASE_COMMIT"

MIGRATION_ROOT="$RELEASE_ROOT/database/migrations"
MIGRATION="$MIGRATION_ROOT/040_scoped_role_policy_versions.sql"
CHECKSUM_MANIFEST="$MIGRATION_ROOT/SHA256SUMS"

[[ -f "$MIGRATION" ]] || fail "Migration 040 entry file is missing."
[[ -f "$CHECKSUM_MANIFEST" ]] || fail "Migration checksum manifest is missing."
[[ "$(grep -Ec '^[0-9a-f]{64}  040_scoped_role_policy_versions(/[^ ]+)?\.sql$' "$CHECKSUM_MANIFEST")" == "12" ]] ||
  fail "Migration checksum manifest must contain exactly 12 scoped RBAC SQL files."

(
  cd "$MIGRATION_ROOT"
  sha256sum --check --strict SHA256SUMS
) || fail "Migration checksum manifest validation failed."

echo "SCOPED_RBAC_MIGRATION_CHECKSUMS=VERIFIED"

read -r USERS_BEFORE ROLES_BEFORE ASSIGNMENTS_BEFORE ROLE_PERMISSIONS_BEFORE <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 --command="
    SELECT
      (SELECT COUNT(*) FROM app_users),
      (SELECT COUNT(*) FROM app_roles),
      (SELECT COUNT(*) FROM app_user_role_assignments),
      (SELECT COUNT(*) FROM app_role_permissions)
    WHERE EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='039_work_to_cash_reactivation_lock_order');" |
  tr '|' ' '
)"
[[ -n "${USERS_BEFORE:-}" ]] || fail "Migration 039 or legacy RBAC prerequisites are missing."

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION"

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --command="
DO \$verify_scoped_rbac\$
DECLARE
  users_after bigint;
  roles_after bigint;
  assignments_after bigint;
  role_permissions_after bigint;
BEGIN
  SELECT COUNT(*) INTO users_after FROM app_users;
  SELECT COUNT(*) INTO roles_after FROM app_roles;
  SELECT COUNT(*) INTO assignments_after FROM app_user_role_assignments;
  SELECT COUNT(*) INTO role_permissions_after FROM app_role_permissions;
  IF users_after <> ${USERS_BEFORE} OR roles_after <> ${ROLES_BEFORE}
     OR assignments_after <> ${ASSIGNMENTS_BEFORE}
     OR role_permissions_after <> ${ROLE_PERMISSIONS_BEFORE} THEN
    RAISE EXCEPTION 'Legacy RBAC counts changed during migration 040.';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='040_scoped_role_policy_versions') THEN
    RAISE EXCEPTION 'Migration 040 was not registered.';
  END IF;
  IF (SELECT COUNT(*) FROM scoped_role_policy_modules) <> 70 THEN
    RAISE EXCEPTION 'Scoped module catalog count is not 70.';
  END IF;
  IF (SELECT COUNT(*) FROM scoped_role_policy_versions WHERE policy_status='PUBLISHED') <> 1 THEN
    RAISE EXCEPTION 'Exactly one published scoped policy is required.';
  END IF;
  IF EXISTS (SELECT 1 FROM scoped_role_policy_effective_grants WHERE module_code='003' AND grant_effect='GRANT' AND action_code NOT IN ('MODULE_VIEW','UTILIZATION_VIEW')) THEN
    RAISE EXCEPTION 'Module 003 contains a write grant.';
  END IF;
  IF EXISTS (SELECT 1 FROM scoped_role_policy_effective_grants WHERE module_code='037' AND grant_effect='GRANT' AND action_code NOT IN ('MODULE_VIEW','MATRIX_VIEW','MATRIX_EXPORT','ACCESS_EXPLAIN')) THEN
    RAISE EXCEPTION 'Module 037 contains a write grant.';
  END IF;
  IF EXISTS (SELECT 1 FROM scoped_role_policy_effective_grants WHERE role_code IN ('PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD') AND action_code ILIKE '%PASSWORD%' AND grant_effect='GRANT') THEN
    RAISE EXCEPTION 'Project Management received password-reset approval access.';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM scoped_role_policy_effective_grants WHERE role_code='SUPER_ADMINISTRATOR' AND module_code='012' AND action_code='POLICY_PUBLISH' AND grant_effect='GRANT') THEN
    RAISE EXCEPTION 'Super Administrator policy-publish authority is missing.';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM scoped_role_policy_effective_grants WHERE role_code='PROJECT_TEAM_COORDINATOR' AND module_code='002' AND action_code='APPROVAL_DELEGATE_MANAGER' AND delegated_authority AND reason_required AND audit_required) THEN
    RAISE EXCEPTION 'PTC delegated approval authority is incomplete.';
  END IF;
END
\$verify_scoped_rbac\$;
SELECT 'SCOPED_RBAC_MIGRATION_040=APPLIED_AND_VERIFIED';"