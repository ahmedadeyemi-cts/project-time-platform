-- Project Health Dashboard
-- Rollback: 007_timesheet_day_submission_status_rollback.sql

BEGIN;

DROP INDEX IF EXISTS idx_timesheet_day_status_timesheet_status;
DROP INDEX IF EXISTS idx_timesheet_day_status_user_date;
DROP TABLE IF EXISTS timesheet_day_statuses;

DELETE FROM schema_migrations
WHERE migration_id = '007_timesheet_day_submission_status';

COMMIT;
