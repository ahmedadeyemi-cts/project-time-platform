-- Project Time Platform
-- Rollback: 002_non_project_time_and_hour_types_rollback.sql
-- Purpose: Roll back non-project time category support and normal/afterhours time entry support.

BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM time_entries
        WHERE project_id IS NULL
           OR non_project_time_category_id IS NOT NULL
           OR time_type <> 'normal'
    ) THEN
        RAISE EXCEPTION 'Rollback blocked: time_entries contains non-project or afterhours entries.';
    END IF;
END $$;

ALTER TABLE time_entries DROP CONSTRAINT IF EXISTS chk_time_entry_project_or_non_project;
ALTER TABLE time_entries DROP CONSTRAINT IF EXISTS chk_time_entry_task_requires_project;
ALTER TABLE time_entries DROP CONSTRAINT IF EXISTS chk_time_entry_time_type;

DROP INDEX IF EXISTS idx_time_entries_user_work_date_time_type;
DROP INDEX IF EXISTS idx_time_entries_time_type;
DROP INDEX IF EXISTS idx_time_entries_non_project_category;
DROP INDEX IF EXISTS idx_non_project_time_categories_code;
DROP INDEX IF EXISTS idx_non_project_time_categories_active;

ALTER TABLE time_entries DROP COLUMN IF EXISTS non_project_time_category_id;
ALTER TABLE time_entries DROP COLUMN IF EXISTS time_type;
ALTER TABLE time_entries ALTER COLUMN project_id SET NOT NULL;

DROP TABLE IF EXISTS non_project_time_categories;

DELETE FROM schema_migrations
WHERE migration_id = '002_non_project_time_and_hour_types';

COMMIT;
