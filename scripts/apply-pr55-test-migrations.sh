#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_RELEASE_COMMIT="5b4debe8218560de357f37e567f38aa497482d69"
EXPECTED_034_SHA256="275c2f3f5ad56d80f303327baeb665506bc41014d52af8a2b7082c6e451974b9"
EXPECTED_035_SHA256="87c6fcea07a25b829ca58c62c18992c9f01d8477a48b55f70aa1c710807b180d"
EXPECTED_036_SHA256="b8f9dab7d7465ce06af2ee287867759ee718f6b7d1fc96d4b8629e65b58d80f3"

RELEASE_ROOT="${1:-}"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-}"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

[[ -n "$RELEASE_ROOT" ]] || fail "Usage: $0 <checked-out-release-root>"
[[ -n "$DATABASE_URL" ]] || fail "PROJECTPULSE_TEST_DATABASE_URL is not configured."

require_command psql
require_command sha256sum
require_command awk
require_command grep

if [[ -d "$RELEASE_ROOT/.git" ]]; then
  require_command git
  ACTUAL_RELEASE_COMMIT="$(git -C "$RELEASE_ROOT" rev-parse HEAD)"
elif [[ -f "$RELEASE_ROOT/.projectpulse-release-commit" ]]; then
  ACTUAL_RELEASE_COMMIT="$(tr -d '\r\n' < "$RELEASE_ROOT/.projectpulse-release-commit")"
else
  fail "Release root has neither Git metadata nor a pinned release marker: $RELEASE_ROOT"
fi

[[ "$ACTUAL_RELEASE_COMMIT" == "$EXPECTED_RELEASE_COMMIT" ]] ||
  fail "Migration source must be release $EXPECTED_RELEASE_COMMIT, not $ACTUAL_RELEASE_COMMIT."

MIGRATION_034="$RELEASE_ROOT/database/migrations/034_module_026_crm_erp_integrations.sql"
MIGRATION_035="$RELEASE_ROOT/database/migrations/035_work_register_055c_055d_split.sql"
MIGRATION_036="$RELEASE_ROOT/database/migrations/036_work_register_role_scope_and_closeout_handoff.sql"

[[ -f "$MIGRATION_034" ]] || fail "Missing migration: $MIGRATION_034"
[[ -f "$MIGRATION_035" ]] || fail "Missing migration: $MIGRATION_035"
[[ -f "$MIGRATION_036" ]] || fail "Missing migration: $MIGRATION_036"

ACTUAL_034_SHA256="$(sha256sum "$MIGRATION_034" | awk '{print $1}')"
ACTUAL_035_SHA256="$(sha256sum "$MIGRATION_035" | awk '{print $1}')"
ACTUAL_036_SHA256="$(sha256sum "$MIGRATION_036" | awk '{print $1}')"

[[ "$ACTUAL_034_SHA256" == "$EXPECTED_034_SHA256" ]] ||
  fail "Migration 034 checksum does not match the reviewed release."
[[ "$ACTUAL_035_SHA256" == "$EXPECTED_035_SHA256" ]] ||
  fail "Migration 035 checksum does not match the reviewed release."
[[ "$ACTUAL_036_SHA256" == "$EXPECTED_036_SHA256" ]] ||
  fail "Migration 036 checksum does not match the reviewed release."

validate_transaction_boundary() {
  local migration_file="$1"
  local begin_count commit_count

  begin_count="$(grep -Ec '^[[:space:]]*BEGIN;[[:space:]]*$' "$migration_file" || true)"
  commit_count="$(grep -Ec '^[[:space:]]*COMMIT;[[:space:]]*$' "$migration_file" || true)"

  [[ "$begin_count" == "1" ]] || fail "$migration_file must contain exactly one top-level BEGIN statement."
  [[ "$commit_count" == "1" ]] || fail "$migration_file must contain exactly one top-level COMMIT statement."
}

validate_transaction_boundary "$MIGRATION_034"
validate_transaction_boundary "$MIGRATION_035"
validate_transaction_boundary "$MIGRATION_036"

echo "PR55_DATABASE_PREFLIGHT=STARTED"
psql "$DATABASE_URL" \
  --no-psqlrc \
  --set=ON_ERROR_STOP=1 \
  --command="
    DO \$pr55_preflight\$
    BEGIN
      IF to_regclass('public.schema_migrations') IS NULL
         OR to_regclass('public.app_users') IS NULL
         OR to_regclass('public.app_roles') IS NULL
         OR to_regclass('public.app_permissions') IS NULL
         OR to_regclass('public.app_role_permissions') IS NULL
         OR to_regclass('public.app_feature_catalog') IS NULL
         OR to_regclass('public.crm_integration_providers') IS NULL
         OR to_regclass('public.work_register_intake_packages') IS NULL
         OR to_regclass('public.projectpulse_module_audit_events') IS NULL THEN
        RAISE EXCEPTION 'One or more PR #55 prerequisite tables are missing.';
      END IF;
    END
    \$pr55_preflight\$;
    SELECT 'PR55_DATABASE_PREREQUISITES=READY';"

BUNDLE="$(mktemp)"
trap 'rm -f "$BUNDLE"' EXIT

append_migration_body() {
  local migration_file="$1"

  awk '
    /^[[:space:]]*BEGIN;[[:space:]]*$/ { next }
    /^[[:space:]]*COMMIT;[[:space:]]*$/ { next }
    { print }
  ' "$migration_file" >> "$BUNDLE"
}

cat > "$BUNDLE" <<'SQL'
\set ON_ERROR_STOP on
BEGIN;
SQL

append_migration_body "$MIGRATION_034"
append_migration_body "$MIGRATION_035"
append_migration_body "$MIGRATION_036"

cat >> "$BUNDLE" <<'SQL'

DO $pr55_verify$
DECLARE
    audit_constraint_definition TEXT;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '034_module_026_crm_erp_integrations'
    ) THEN
        RAISE EXCEPTION 'Migration 034 did not register in schema_migrations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '035_work_register_055c_055d_split'
    ) THEN
        RAISE EXCEPTION 'Migration 035 did not register in schema_migrations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '036_work_register_role_scope_and_closeout_handoff'
    ) THEN
        RAISE EXCEPTION 'Migration 036 did not register in schema_migrations.';
    END IF;

    IF to_regclass('public.crm_integration_credentials') IS NULL
       OR to_regclass('public.crm_integration_oauth_states') IS NULL
       OR to_regclass('public.crm_integration_connection_checks') IS NULL THEN
        RAISE EXCEPTION 'Migration 034 integration tables are incomplete.';
    END IF;

    SELECT pg_get_constraintdef(oid)
    INTO audit_constraint_definition
    FROM pg_constraint
    WHERE conrelid = 'projectpulse_module_audit_events'::regclass
      AND conname = 'ck_projectpulse_module_audit_module';

    IF audit_constraint_definition IS NULL
       OR position('026' IN audit_constraint_definition) = 0 THEN
        RAISE EXCEPTION 'The shared module audit constraint does not permit Module 026.';
    END IF;

    IF to_regclass('public.work_register_change_history') IS NULL THEN
        RAISE EXCEPTION 'Migration 035 work-register audit table is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'work_register_intake_packages'
          AND column_name = 'source_mode'
          AND is_nullable = 'NO'
          AND column_default IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'Migration 035 source_mode contract is incomplete.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'work_register_change_history'::regclass
          AND confrelid = 'app_users'::regclass
          AND contype = 'f'
    ) THEN
        RAISE EXCEPTION 'Migration 035 changed-by user foreign key is missing.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('SUPER_ADMINISTRATOR'),
            ('ADMINISTRATOR')
        ) AS required_role(role_code)
        WHERE NOT EXISTS (
            SELECT 1
            FROM app_roles role
            WHERE upper(role.role_code) = required_role.role_code
        )
    ) THEN
        RAISE EXCEPTION 'Migration 036 requires the Administrator and Super Administrator roles.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('EDIT_WORK_REGISTER_055C'),
            ('CREATE_WORK_REGISTER_055D')
        ) AS required_permission(permission_code)
        CROSS JOIN (VALUES
            ('SUPER_ADMINISTRATOR'),
            ('ADMINISTRATOR')
        ) AS required_role(role_code)
        WHERE NOT EXISTS (
            SELECT 1
            FROM app_role_permissions role_permission
            JOIN app_roles role
              ON role.app_role_id = role_permission.app_role_id
            JOIN app_permissions permission
              ON permission.app_permission_id = role_permission.app_permission_id
            WHERE upper(role.role_code) = required_role.role_code
              AND permission.permission_code = required_permission.permission_code
        )
    ) THEN
        RAISE EXCEPTION 'Migration 036 administrator Work Register grants are incomplete.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM (VALUES
            ('EDIT_WORK_REGISTER_055C'),
            ('CREATE_WORK_REGISTER_055D')
        ) AS required_feature(feature_code)
        WHERE NOT EXISTS (
            SELECT 1
            FROM app_feature_catalog feature
            WHERE feature.feature_code = required_feature.feature_code
              AND feature.updated_at IS NOT NULL
        )
    ) THEN
        RAISE EXCEPTION 'Migration 036 Work Register feature metadata is incomplete.';
    END IF;
END
$pr55_verify$;

COMMIT;

SELECT 'MIGRATION_034_APPLIED=YES'
WHERE EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id = '034_module_026_crm_erp_integrations'
);

SELECT 'MIGRATION_035_APPLIED=YES'
WHERE EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id = '035_work_register_055c_055d_split'
);

SELECT 'MIGRATION_036_APPLIED=YES'
WHERE EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id = '036_work_register_role_scope_and_closeout_handoff'
);
SQL

echo "PR55_MIGRATION_TRANSACTION=STARTED"
psql "$DATABASE_URL" \
  --no-psqlrc \
  --set=ON_ERROR_STOP=1 \
  --file="$BUNDLE"
echo "PR55_MIGRATION_TRANSACTION=COMMITTED"
