-- Project Time Platform
-- Rollback: 006_timesheet_persistence_location_columns_rollback.sql
-- Purpose: Remove work location fields added for saved draft and submitted weekly time entries.

BEGIN;

DROP INDEX IF EXISTS idx_time_entries_timesheet_work_date_type;
DROP INDEX IF EXISTS idx_time_entries_work_location;
DROP INDEX IF EXISTS idx_time_entries_work_location_group;

ALTER TABLE time_entries
    DROP COLUMN IF EXISTS work_location_id,
    DROP COLUMN IF EXISTS work_location_group_id;

DELETE FROM schema_migrations
WHERE migration_id = '006_timesheet_persistence_location_columns';

COMMIT;
