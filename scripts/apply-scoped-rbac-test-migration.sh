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

MIGRATION="$RELEASE_ROOT/database/migrations/040_scoped_role_policy_versions.sql"
FILES=(
  "040_scoped_role_policy_versions.sql:5ed561de521f448f6f4f3db5179fc7b7dae457d4ed107541d731b0222f77a67c"
  "040_scoped_role_policy_versions/00_schema.sql:3b496f25334bac0a920cbe6e6e09267751fe6d415b613278331312c2d5e990ac"
  "040_scoped_role_policy_versions/10_workbook_cells.sql:b51c5f9e2481cb5161b754944e477a0fdcc88cf31d87a51ace527579f0812a74"
  "040_scoped_role_policy_versions/12_super_administrator_override.sql:5d7dfc8059d0fc94662ee563e6253aa9db9548e158a0653e09ffbc946ee89c76"
  "040_scoped_role_policy_versions/15_workbook_metadata.sql:f937f9065a4f2300a919690e3c416a8e458fae5328a897da4ee1986c8239c8ed"
  "040_scoped_role_policy_versions/20_standard_grants.sql:a2a0ecdee56261c7b5db5fb6717596cd8ef2f684b70ca3252f755c11618591cc"
  "040_scoped_role_policy_versions/30_time_entry.sql:26f260ab66e8062515465f0f955c27b0f67ac97ab250f3f6ff7e2079bb90e72e"
  "040_scoped_role_policy_versions/40_approval_inbox.sql:87a81fc76c251c043c5f2868881d0cfab234daf57ebb4c5fea5206b2e49db0f7"
  "040_scoped_role_policy_versions/50_utilization.sql:c8218c04b3c275284d41c9e16815d476a0011cba57c42804e19eb42ed5688209"
  "040_scoped_role_policy_versions/60_role_administration.sql:93ec7fa061c8595417c95ac8193a94c36f4a8b782cb05f5d1b2b4cfab278919c"
  "040_scoped_role_policy_versions/70_read_only_matrix.sql:6af8e45994893ead1a2e7c274ff39c6ee263a36c0cbee6f5d5bac5c266bff2d1"
  "040_scoped_role_policy_versions/80_finalize.sql:88019b66bd376b2840157464d545710691d983dedcc26fa35b4da63ce99dceec"
)
for item in "${FILES[@]}"; do
  path="${item%%:*}"; expected="${item##*:}"
  full="$RELEASE_ROOT/database/migrations/$path"
  [[ -f "$full" ]] || fail "Missing migration file: $path"
  actual="$(sha256sum "$full" | awk '{print $1}')"
  [[ "$actual" == "$expected" ]] || fail "Checksum mismatch: $path"
done

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
