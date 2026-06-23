-- Project Time Platform
-- Migration: 006_timesheet_persistence_location_columns.sql
-- Purpose: Add work location fields needed for saved draft and submitted weekly time entries.

BEGIN;

ALTER TABLE time_entries
    ADD COLUMN IF NOT EXISTS work_location_group_id UUID REFERENCES work_location_groups(work_location_group_id),
    ADD COLUMN IF NOT EXISTS work_location_id UUID REFERENCES work_locations(work_location_id);

CREATE INDEX IF NOT EXISTS idx_time_entries_work_location_group
    ON time_entries(work_location_group_id);

CREATE INDEX IF NOT EXISTS idx_time_entries_work_location
    ON time_entries(work_location_id);

CREATE INDEX IF NOT EXISTS idx_time_entries_timesheet_work_date_type
    ON time_entries(timesheet_id, work_date, time_type);

INSERT INTO schema_migrations (migration_id, description)
VALUES ('006_timesheet_persistence_location_columns', 'Add work location columns required for saved draft and submitted weekly time entries')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
