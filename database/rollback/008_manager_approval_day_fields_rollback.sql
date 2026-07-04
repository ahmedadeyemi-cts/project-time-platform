-- Project Health Dashboard
-- Rollback: 008_manager_approval_day_fields_rollback.sql

BEGIN;

DROP INDEX IF EXISTS idx_timesheet_day_status_manager_user;

ALTER TABLE timesheet_day_statuses
    DROP COLUMN IF EXISTS manager_unlocked_at,
    DROP COLUMN IF EXISTS manager_declined_at,
    DROP COLUMN IF EXISTS manager_approved_at,
    DROP COLUMN IF EXISTS manager_decision_comment,
    DROP COLUMN IF EXISTS manager_user_id;

DELETE FROM schema_migrations
WHERE migration_id = '008_manager_approval_day_fields';

COMMIT;
