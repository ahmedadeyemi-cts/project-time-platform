#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/048_admin_audit_and_manager_team_scope.sql"
ROLLBACK="$ROOT/database/rollback/048_admin_audit_and_manager_team_scope_rollback.sql"
DATABASE_URL="${PROJECTPULSE_TEST_DATABASE_URL:-postgresql://postgres:postgres@127.0.0.1:5432/projectpulse_test}"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ -f "$MIGRATION" ]] || fail "Migration 048 source is missing."
[[ -f "$ROLLBACK" ]] || fail "Migration 048 rollback source is missing."
command -v psql >/dev/null || fail "psql is required."

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;

DROP TABLE IF EXISTS user_admin_manager_team_assignments CASCADE;
DROP TABLE IF EXISTS projectpulse_system_audit_events CASCADE;
DROP FUNCTION IF EXISTS projectpulse048_block_system_audit_mutation() CASCADE;

CREATE TABLE IF NOT EXISTS schema_migrations (
    migration_id VARCHAR(100) PRIMARY KEY,
    description TEXT NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
DELETE FROM schema_migrations
WHERE migration_id = '048_admin_audit_and_manager_team_scope';

CREATE TABLE IF NOT EXISTS app_users (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    team_name TEXT,
    manager_email TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
TRUNCATE TABLE app_users CASCADE;
SQL

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION"
psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$MIGRATION"

eval "$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 <<'SQL'
SELECT 'MIGRATION_COUNT=' || quote_literal(COUNT(*))
FROM schema_migrations
WHERE migration_id = '048_admin_audit_and_manager_team_scope';
SELECT 'AUDIT_TABLE=' || quote_literal(to_regclass('public.projectpulse_system_audit_events') IS NOT NULL);
SELECT 'ASSIGNMENT_TABLE=' || quote_literal(to_regclass('public.user_admin_manager_team_assignments') IS NOT NULL);
SELECT 'IMMUTABLE_TRIGGER=' || quote_literal(EXISTS (
    SELECT 1 FROM pg_trigger
    WHERE tgname = 'trg_projectpulse048_system_audit_immutable'
      AND NOT tgisinternal
));
SELECT 'UNIQUE_MANAGER_INDEX=' || quote_literal(to_regclass('public.ux_user_admin_one_active_manager_per_team') IS NOT NULL);
SQL
)"

[[ "$MIGRATION_COUNT" == 1 ]] || fail "Migration 048 registration is not idempotent."
[[ "$AUDIT_TABLE" == true ]] || fail "Unified audit table was not created."
[[ "$ASSIGNMENT_TABLE" == true ]] || fail "Manager team assignment table was not created."
[[ "$IMMUTABLE_TRIGGER" == true ]] || fail "Immutable audit trigger was not created."
[[ "$UNIQUE_MANAGER_INDEX" == true ]] || fail "One-active-manager-per-team index was not created."

read -r MANAGER_ONE MANAGER_TWO ACTOR_ID <<<"$(
  psql "$DATABASE_URL" --no-psqlrc -At -F ' ' --set=ON_ERROR_STOP=1 <<'SQL'
WITH inserted AS (
    INSERT INTO app_users (email, display_name, team_name)
    VALUES
        ('manager.one@ussignal.local', 'Manager One', 'Collaboration Engineering'),
        ('manager.two@ussignal.local', 'Manager Two', 'Systems Engineering'),
        ('admin.actor@ussignal.local', 'Admin Actor', 'Administration')
    RETURNING user_id, email
)
SELECT
    MAX(user_id::text) FILTER (WHERE email = 'manager.one@ussignal.local'),
    MAX(user_id::text) FILTER (WHERE email = 'manager.two@ussignal.local'),
    MAX(user_id::text) FILTER (WHERE email = 'admin.actor@ussignal.local')
FROM inserted;
SQL
)"

[[ "$MANAGER_ONE" =~ ^[0-9a-f-]{36}$ ]] || fail "Manager one seed failed."
[[ "$MANAGER_TWO" =~ ^[0-9a-f-]{36}$ ]] || fail "Manager two seed failed."
[[ "$ACTOR_ID" =~ ^[0-9a-f-]{36}$ ]] || fail "Actor seed failed."

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
  --set=manager_one="$MANAGER_ONE" \
  --set=actor_id="$ACTOR_ID" <<'SQL'
INSERT INTO user_admin_manager_team_assignments (
    manager_user_id,
    manager_email,
    team_name,
    is_active,
    assigned_by_user_id,
    assignment_reason
)
VALUES (
    :'manager_one'::uuid,
    'manager.one@ussignal.local',
    'Collaboration Engineering',
    TRUE,
    :'actor_id'::uuid,
    'Migration regression test'
);

INSERT INTO projectpulse_system_audit_events (
    category,
    status,
    event_type,
    actor_user_id,
    actor_email,
    target_type,
    target_label,
    source_module,
    summary,
    event_details,
    correlation_id
)
VALUES (
    'user_administration',
    'success',
    'MANAGER_TEAM_SCOPE_UPDATED',
    :'actor_id'::uuid,
    'admin.actor@ussignal.local',
    'team',
    'Collaboration Engineering',
    '009',
    'Migration regression audit evidence.',
    '{"sanitized":true}'::jsonb,
    'migration-048-test'
);
SQL

if psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
  --set=manager_two="$MANAGER_TWO" \
  --set=actor_id="$ACTOR_ID" <<'SQL'
INSERT INTO user_admin_manager_team_assignments (
    manager_user_id,
    manager_email,
    team_name,
    is_active,
    assigned_by_user_id,
    assignment_reason
)
VALUES (
    :'manager_two'::uuid,
    'manager.two@ussignal.local',
    'Collaboration Engineering',
    TRUE,
    :'actor_id'::uuid,
    'This must conflict with the active manager'
);
SQL
then
  fail "Migration 048 allowed two active managers for one team."
fi

echo "MIGRATION_048_ONE_ACTIVE_MANAGER_PER_TEAM=PASSED"

if psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
  --command="UPDATE projectpulse_system_audit_events SET summary='mutated';"
then
  fail "Immutable audit evidence allowed UPDATE."
fi

if psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 \
  --command="DELETE FROM projectpulse_system_audit_events;"
then
  fail "Immutable audit evidence allowed DELETE."
fi

echo "MIGRATION_048_IMMUTABLE_AUDIT=PASSED"

if psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$ROLLBACK"
then
  fail "Rollback succeeded despite operational evidence."
fi

echo "MIGRATION_048_ROLLBACK_GUARD=PASSED"

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 <<'SQL'
TRUNCATE TABLE projectpulse_system_audit_events;
TRUNCATE TABLE user_admin_manager_team_assignments;
SQL

psql "$DATABASE_URL" --no-psqlrc --set=ON_ERROR_STOP=1 --file="$ROLLBACK"

eval "$(psql "$DATABASE_URL" --no-psqlrc -At --set=ON_ERROR_STOP=1 <<'SQL'
SELECT 'AUDIT_REMOVED=' || quote_literal(to_regclass('public.projectpulse_system_audit_events') IS NULL);
SELECT 'ASSIGNMENT_REMOVED=' || quote_literal(to_regclass('public.user_admin_manager_team_assignments') IS NULL);
SELECT 'REGISTRATION_REMOVED=' || quote_literal(NOT EXISTS (
    SELECT 1 FROM schema_migrations
    WHERE migration_id = '048_admin_audit_and_manager_team_scope'
));
SQL
)"

[[ "$AUDIT_REMOVED" == true ]] || fail "Rollback did not remove the empty audit table."
[[ "$ASSIGNMENT_REMOVED" == true ]] || fail "Rollback did not remove the empty assignment table."
[[ "$REGISTRATION_REMOVED" == true ]] || fail "Rollback did not remove migration registration."

echo "MIGRATION_048_APPLY_IDEMPOTENCE_ROLLBACK=PASSED"
