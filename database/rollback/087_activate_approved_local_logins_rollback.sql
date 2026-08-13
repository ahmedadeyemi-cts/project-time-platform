-- Pulse
-- Rollback: 087_activate_approved_local_logins_rollback.sql
-- Security posture:
--   Prior credential material cannot be restored safely. This rollback revokes
--   sessions, removes Migration 087 credentials, and disables local login while
--   preserving canonical identities and role history.

BEGIN;

DO $rollback_087_guard$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '087_activate_approved_local_logins'
    ) THEN
        RAISE EXCEPTION
            'Rollback 087 refused because migration 087_activate_approved_local_logins is not recorded.';
    END IF;

    IF to_regclass('public.app_users') IS NULL
       OR to_regclass('public.auth_local_accounts') IS NULL
       OR to_regclass('public.auth_sessions') IS NULL THEN
        RAISE EXCEPTION
            'Rollback 087 requires app_users, auth_local_accounts, and auth_sessions.';
    END IF;
END
$rollback_087_guard$;

CREATE TEMP TABLE rollback_087_targets (
    email text PRIMARY KEY
) ON COMMIT DROP;

INSERT INTO rollback_087_targets (email)
VALUES
    ('jeremy.holt@ussignal.local'),
    ('darren.olson@ussignal.local'),
    ('demo.engineer@ussignal.local'),
    ('demo.manager@ussignal.local'),
    ('heather.schrock@ussignal.local'),
    ('jason.mosier@ussignal.local'),
    ('juli.cambron@ussignal.local'),
    ('kevin.damisch@ussignal.local'),
    ('project.team.coordinator@ussignal.local'),
    ('steve.kopischke@ussignal.local');

UPDATE auth_sessions session_row
SET revoked_at = NOW(),
    revoked_reason = 'rollback_087_local_login_disabled'
FROM app_users user_row
JOIN rollback_087_targets target
  ON lower(user_row.email) = target.email
WHERE session_row.user_id = user_row.user_id
  AND session_row.revoked_at IS NULL;

UPDATE auth_local_accounts local_account
SET password_hash = NULL,
    password_hash_algorithm = 'PBKDF2-SHA256',
    password_set_at = NULL,
    password_hash_updated_at = NULL,
    last_password_change_at = NULL,
    must_change_password = TRUE,
    failed_login_count = 0,
    locked_until = NULL,
    is_active = FALSE,
    updated_at = NOW()
FROM app_users user_row
JOIN rollback_087_targets target
  ON lower(user_row.email) = target.email
WHERE local_account.user_id = user_row.user_id;

DO $rollback_087_verification$
DECLARE
    unsafe_account_count integer;
BEGIN
    SELECT count(*)
    INTO unsafe_account_count
    FROM rollback_087_targets target
    JOIN app_users user_row
      ON lower(user_row.email) = target.email
    JOIN auth_local_accounts local_account
      ON local_account.user_id = user_row.user_id
    WHERE local_account.password_hash IS NOT NULL
       OR local_account.is_active = TRUE
       OR local_account.must_change_password = FALSE
       OR local_account.locked_until IS NOT NULL
       OR local_account.failed_login_count <> 0;

    IF unsafe_account_count <> 0 THEN
        RAISE EXCEPTION
            'Rollback 087 failed to disable % local account(s) safely.',
            unsafe_account_count;
    END IF;
END
$rollback_087_verification$;

DELETE FROM schema_migrations
WHERE migration_id = '087_activate_approved_local_logins';

COMMIT;
