-- Pulse
-- Migration: 087_activate_approved_local_logins.sql
-- Purpose:
--   Activate the approved local UAT/demo identities with their existing canonical
--   roles and rotate their local credentials without forcing a password change.
-- Application:
--   Run only through scripts/release-test/apply-087-approved-local-logins.sh.
--   The apply script injects one-time, unique salted PBKDF2-SHA256 hashes through
--   session-scoped settings. No plaintext credential or reusable hash is stored
--   in source control.

BEGIN;

DO $migration_087_prerequisites$
DECLARE
    missing_relations text[];
    missing_columns text[];
BEGIN
    SELECT array_agg(required_relation ORDER BY required_relation)
    INTO missing_relations
    FROM (
        VALUES
            ('app_users'),
            ('app_roles'),
            ('app_user_role_assignments'),
            ('auth_local_accounts'),
            ('auth_sessions'),
            ('schema_migrations')
    ) AS required(required_relation)
    WHERE to_regclass('public.' || required_relation) IS NULL;

    IF missing_relations IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 requires missing relation(s): %',
            array_to_string(missing_relations, ', ');
    END IF;

    SELECT array_agg(required_column ORDER BY required_column)
    INTO missing_columns
    FROM (
        VALUES
            ('app_users.email'),
            ('app_users.display_name'),
            ('app_users.is_active'),
            ('app_users.login_enabled'),
            ('app_users.manager_email'),
            ('app_users.updated_at'),
            ('app_roles.role_code'),
            ('app_roles.is_active'),
            ('app_user_role_assignments.user_id'),
            ('app_user_role_assignments.app_role_id'),
            ('app_user_role_assignments.assignment_reason'),
            ('app_user_role_assignments.is_active'),
            ('app_user_role_assignments.assigned_at'),
            ('app_user_role_assignments.updated_at'),
            ('auth_local_accounts.user_id'),
            ('auth_local_accounts.username'),
            ('auth_local_accounts.password_hash'),
            ('auth_local_accounts.password_hash_algorithm'),
            ('auth_local_accounts.password_set_at'),
            ('auth_local_accounts.password_hash_updated_at'),
            ('auth_local_accounts.last_password_change_at'),
            ('auth_local_accounts.must_change_password'),
            ('auth_local_accounts.failed_login_count'),
            ('auth_local_accounts.locked_until'),
            ('auth_local_accounts.is_active'),
            ('auth_local_accounts.updated_at'),
            ('auth_sessions.user_id'),
            ('auth_sessions.expires_at'),
            ('auth_sessions.revoked_at'),
            ('auth_sessions.revoked_reason')
    ) AS required(required_column)
    WHERE NOT EXISTS (
        SELECT 1
        FROM information_schema.columns column_info
        WHERE column_info.table_schema = 'public'
          AND column_info.table_name = split_part(required_column, '.', 1)
          AND column_info.column_name = split_part(required_column, '.', 2)
    );

    IF missing_columns IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 requires missing column(s): %',
            array_to_string(missing_columns, ', ');
    END IF;
END
$migration_087_prerequisites$;

CREATE TEMP TABLE migration_087_aliases (
    alias_email text PRIMARY KEY,
    canonical_email text NOT NULL UNIQUE,
    canonical_display_name text NOT NULL
) ON COMMIT DROP;

INSERT INTO migration_087_aliases (
    alias_email,
    canonical_email,
    canonical_display_name
)
VALUES
    ('header.schrock@ussignal.local', 'heather.schrock@ussignal.local', 'Heather Schrock'),
    ('kevin.damish@ussignal.local', 'kevin.damisch@ussignal.local', 'Kevin Damisch'),
    ('jason.mossier@ussignal.local', 'jason.mosier@ussignal.local', 'Jason Mosier');

DO $migration_087_alias_guard$
DECLARE
    duplicate_email text;
    conflicting_alias text;
BEGIN
    SELECT lower(user_row.email)
    INTO duplicate_email
    FROM app_users user_row
    WHERE lower(user_row.email) IN (
        SELECT alias_email FROM migration_087_aliases
        UNION
        SELECT canonical_email FROM migration_087_aliases
    )
    GROUP BY lower(user_row.email)
    HAVING count(*) > 1
    ORDER BY lower(user_row.email)
    LIMIT 1;

    IF duplicate_email IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 found more than one app_users row for case-insensitive email %.',
            duplicate_email;
    END IF;

    SELECT alias_row.alias_email
    INTO conflicting_alias
    FROM migration_087_aliases alias_row
    JOIN app_users alias_user
      ON lower(alias_user.email) = alias_row.alias_email
    JOIN app_users canonical_user
      ON lower(canonical_user.email) = alias_row.canonical_email
     AND canonical_user.user_id <> alias_user.user_id
    ORDER BY alias_row.alias_email
    LIMIT 1;

    IF conflicting_alias IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 will not merge the distinct alias identity % into an existing canonical identity.',
            conflicting_alias;
    END IF;
END
$migration_087_alias_guard$;

UPDATE app_users dependent_user
SET manager_email = alias_row.canonical_email,
    updated_at = NOW()
FROM migration_087_aliases alias_row
WHERE lower(COALESCE(dependent_user.manager_email, '')) = alias_row.alias_email;

UPDATE app_users alias_user
SET email = alias_row.canonical_email,
    display_name = alias_row.canonical_display_name,
    is_active = TRUE,
    login_enabled = TRUE,
    updated_at = NOW()
FROM migration_087_aliases alias_row
WHERE lower(alias_user.email) = alias_row.alias_email;

CREATE TEMP TABLE migration_087_targets (
    email text PRIMARY KEY,
    display_name text NOT NULL,
    role_code text NOT NULL,
    hash_setting text NOT NULL UNIQUE,
    password_hash text NOT NULL
) ON COMMIT DROP;

INSERT INTO migration_087_targets (
    email,
    display_name,
    role_code,
    hash_setting,
    password_hash
)
VALUES
    ('jeremy.holt@ussignal.local', 'Jeremy Holt', 'ENGINEERING', 'projectpulse.m087.hash.jeremy_holt', COALESCE(current_setting('projectpulse.m087.hash.jeremy_holt', TRUE), '')),
    ('darren.olson@ussignal.local', 'Darren Olson', 'EXECUTIVE', 'projectpulse.m087.hash.darren_olson', COALESCE(current_setting('projectpulse.m087.hash.darren_olson', TRUE), '')),
    ('demo.engineer@ussignal.local', 'Demo Engineer', 'ENGINEERING', 'projectpulse.m087.hash.demo_engineer', COALESCE(current_setting('projectpulse.m087.hash.demo_engineer', TRUE), '')),
    ('demo.manager@ussignal.local', 'Demo Manager', 'MANAGER', 'projectpulse.m087.hash.demo_manager', COALESCE(current_setting('projectpulse.m087.hash.demo_manager', TRUE), '')),
    ('heather.schrock@ussignal.local', 'Heather Schrock', 'PROJECT_MANAGEMENT', 'projectpulse.m087.hash.heather_schrock', COALESCE(current_setting('projectpulse.m087.hash.heather_schrock', TRUE), '')),
    ('jason.mosier@ussignal.local', 'Jason Mosier', 'ENGINEERING', 'projectpulse.m087.hash.jason_mosier', COALESCE(current_setting('projectpulse.m087.hash.jason_mosier', TRUE), '')),
    ('juli.cambron@ussignal.local', 'Juli Cambron', 'ACCOUNTING', 'projectpulse.m087.hash.juli_cambron', COALESCE(current_setting('projectpulse.m087.hash.juli_cambron', TRUE), '')),
    ('kevin.damisch@ussignal.local', 'Kevin Damisch', 'ENGINEERING', 'projectpulse.m087.hash.kevin_damisch', COALESCE(current_setting('projectpulse.m087.hash.kevin_damisch', TRUE), '')),
    ('project.team.coordinator@ussignal.local', 'Project Team Coordinator', 'PROJECT_TEAM_COORDINATOR', 'projectpulse.m087.hash.project_team_coordinator', COALESCE(current_setting('projectpulse.m087.hash.project_team_coordinator', TRUE), '')),
    ('steve.kopischke@ussignal.local', 'Steve Kopischke', 'PROJECT_MANAGEMENT', 'projectpulse.m087.hash.steve_kopischke', COALESCE(current_setting('projectpulse.m087.hash.steve_kopischke', TRUE), ''));

DO $migration_087_target_guard$
DECLARE
    missing_users text[];
    duplicate_target_email text;
    missing_roles text[];
    conflicting_username text;
    invalid_hash_setting text;
    unique_hash_count integer;
BEGIN
    SELECT target.hash_setting
    INTO invalid_hash_setting
    FROM migration_087_targets target
    WHERE target.password_hash !~ E'^PBKDF2-SHA256\\$210000\\$[A-Za-z0-9+/]{22}==\\$[A-Za-z0-9+/]{43}=$'
    ORDER BY target.hash_setting
    LIMIT 1;

    IF invalid_hash_setting IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 requires a valid session-injected PBKDF2 value for setting %.',
            invalid_hash_setting;
    END IF;

    SELECT count(DISTINCT target.password_hash)
    INTO unique_hash_count
    FROM migration_087_targets target;

    IF unique_hash_count <> 10 THEN
        RAISE EXCEPTION
            'Migration 087 requires 10 independently salted credential hashes but received %.',
            unique_hash_count;
    END IF;

    SELECT array_agg(target.email ORDER BY target.email)
    INTO missing_users
    FROM migration_087_targets target
    LEFT JOIN app_users user_row
      ON lower(user_row.email) = target.email
    WHERE user_row.user_id IS NULL;

    IF missing_users IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 requires the approved app_users row(s): %',
            array_to_string(missing_users, ', ');
    END IF;

    SELECT lower(user_row.email)
    INTO duplicate_target_email
    FROM app_users user_row
    JOIN migration_087_targets target
      ON lower(user_row.email) = target.email
    GROUP BY lower(user_row.email)
    HAVING count(*) > 1
    ORDER BY lower(user_row.email)
    LIMIT 1;

    IF duplicate_target_email IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 found duplicate case-insensitive app_users rows for %.',
            duplicate_target_email;
    END IF;

    SELECT array_agg(target.role_code ORDER BY target.role_code)
    INTO missing_roles
    FROM (
        SELECT DISTINCT role_code
        FROM migration_087_targets
    ) target
    LEFT JOIN app_roles role_row
      ON role_row.role_code = target.role_code
     AND role_row.is_active = TRUE
    WHERE role_row.app_role_id IS NULL;

    IF missing_roles IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 requires active canonical role(s): %',
            array_to_string(missing_roles, ', ');
    END IF;

    SELECT target.email
    INTO conflicting_username
    FROM migration_087_targets target
    JOIN auth_local_accounts local_account
      ON lower(local_account.username) = target.email
    JOIN app_users intended_user
      ON lower(intended_user.email) = target.email
    WHERE local_account.user_id <> intended_user.user_id
    ORDER BY target.email
    LIMIT 1;

    IF conflicting_username IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 found local username % assigned to a different user.',
            conflicting_username;
    END IF;
END
$migration_087_target_guard$;

UPDATE app_users user_row
SET display_name = target.display_name,
    is_active = TRUE,
    login_enabled = TRUE,
    updated_at = NOW()
FROM migration_087_targets target
WHERE lower(user_row.email) = target.email;

INSERT INTO app_user_role_assignments (
    user_id,
    app_role_id,
    assignment_reason,
    is_active,
    assigned_at,
    updated_at
)
SELECT
    user_row.user_id,
    role_row.app_role_id,
    'Migration 087 approved local login role activation',
    TRUE,
    NOW(),
    NOW()
FROM migration_087_targets target
JOIN app_users user_row
  ON lower(user_row.email) = target.email
JOIN app_roles role_row
  ON role_row.role_code = target.role_code
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET assignment_reason = EXCLUDED.assignment_reason,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO auth_local_accounts (
    user_id,
    username,
    password_hash,
    password_hash_algorithm,
    password_set_at,
    password_hash_updated_at,
    last_password_change_at,
    must_change_password,
    failed_login_count,
    locked_until,
    is_active,
    created_at,
    updated_at
)
SELECT
    user_row.user_id,
    target.email,
    target.password_hash,
    'PBKDF2-SHA256',
    NOW(),
    NOW(),
    NOW(),
    FALSE,
    0,
    NULL,
    TRUE,
    NOW(),
    NOW()
FROM migration_087_targets target
JOIN app_users user_row
  ON lower(user_row.email) = target.email
ON CONFLICT (user_id) DO UPDATE
SET username = EXCLUDED.username,
    password_hash = EXCLUDED.password_hash,
    password_hash_algorithm = EXCLUDED.password_hash_algorithm,
    password_set_at = EXCLUDED.password_set_at,
    password_hash_updated_at = EXCLUDED.password_hash_updated_at,
    last_password_change_at = EXCLUDED.last_password_change_at,
    must_change_password = FALSE,
    failed_login_count = 0,
    locked_until = NULL,
    is_active = TRUE,
    updated_at = NOW();

UPDATE auth_sessions session_row
SET revoked_at = NOW(),
    revoked_reason = 'migration_087_approved_local_login_activation'
FROM app_users user_row
JOIN migration_087_targets target
  ON lower(user_row.email) = target.email
WHERE session_row.user_id = user_row.user_id
  AND session_row.revoked_at IS NULL
  AND session_row.expires_at > NOW();

DO $migration_087_verification$
DECLARE
    active_user_count integer;
    active_account_count integer;
    expected_role_count integer;
    unique_hash_count integer;
    lingering_alias text;
BEGIN
    SELECT count(*)
    INTO active_user_count
    FROM migration_087_targets target
    JOIN app_users user_row
      ON lower(user_row.email) = target.email
    WHERE user_row.is_active = TRUE
      AND user_row.login_enabled = TRUE;

    IF active_user_count <> 10 THEN
        RAISE EXCEPTION
            'Migration 087 expected 10 active login-enabled users but verified %.',
            active_user_count;
    END IF;

    SELECT count(*), count(DISTINCT local_account.password_hash)
    INTO active_account_count, unique_hash_count
    FROM migration_087_targets target
    JOIN app_users user_row
      ON lower(user_row.email) = target.email
    JOIN auth_local_accounts local_account
      ON local_account.user_id = user_row.user_id
     AND lower(local_account.username) = target.email
    WHERE local_account.is_active = TRUE
      AND local_account.must_change_password = FALSE
      AND local_account.failed_login_count = 0
      AND local_account.locked_until IS NULL
      AND local_account.password_hash = target.password_hash
      AND local_account.password_hash_algorithm = 'PBKDF2-SHA256';

    IF active_account_count <> 10 OR unique_hash_count <> 10 THEN
        RAISE EXCEPTION
            'Migration 087 expected 10 active accounts with 10 unique hashes but verified % account(s) and % unique hash(es).',
            active_account_count,
            unique_hash_count;
    END IF;

    SELECT count(*)
    INTO expected_role_count
    FROM migration_087_targets target
    JOIN app_users user_row
      ON lower(user_row.email) = target.email
    JOIN app_roles role_row
      ON role_row.role_code = target.role_code
     AND role_row.is_active = TRUE
    JOIN app_user_role_assignments assignment
      ON assignment.user_id = user_row.user_id
     AND assignment.app_role_id = role_row.app_role_id
     AND assignment.is_active = TRUE;

    IF expected_role_count <> 10 THEN
        RAISE EXCEPTION
            'Migration 087 expected 10 active canonical role assignments but verified %.',
            expected_role_count;
    END IF;

    SELECT alias_row.alias_email
    INTO lingering_alias
    FROM migration_087_aliases alias_row
    JOIN app_users user_row
      ON lower(user_row.email) = alias_row.alias_email
    ORDER BY alias_row.alias_email
    LIMIT 1;

    IF lingering_alias IS NOT NULL THEN
        RAISE EXCEPTION
            'Migration 087 left the obsolete alias % active in app_users.',
            lingering_alias;
    END IF;
END
$migration_087_verification$;

INSERT INTO schema_migrations (
    migration_id,
    description,
    applied_at
)
VALUES (
    '087_activate_approved_local_logins',
    'Activate approved local logins with canonical roles, normalized aliases, session-injected PBKDF2 credentials, and no forced password change',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
