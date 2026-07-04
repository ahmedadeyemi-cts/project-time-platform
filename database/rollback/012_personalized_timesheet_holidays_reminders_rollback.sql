-- Project Health Dashboard
-- Rollback: 012_personalized_timesheet_holidays_reminders_rollback.sql

BEGIN;

DELETE FROM schema_migrations WHERE migration_id = '012_personalized_timesheet_holidays_reminders';

DROP TABLE IF EXISTS email_notification_outbox;
DROP TABLE IF EXISTS reminder_rules;
DROP TABLE IF EXISTS notification_group_members;
DROP TABLE IF EXISTS notification_groups;
DROP TABLE IF EXISTS company_holidays;
DROP TABLE IF EXISTS holiday_upload_batches;
DROP TABLE IF EXISTS user_timesheet_preferences;

COMMIT;
