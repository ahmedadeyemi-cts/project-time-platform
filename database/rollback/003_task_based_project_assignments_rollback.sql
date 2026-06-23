-- Project Time Platform
-- Rollback: 003_task_based_project_assignments_rollback.sql
-- Purpose: Roll back task-based assignment enforcement.

BEGIN;

ALTER TABLE time_entries
    DROP CONSTRAINT IF EXISTS chk_time_entry_project_task_or_non_project;

DROP INDEX IF EXISTS idx_time_entries_project_task_user_date;
DROP INDEX IF EXISTS idx_project_assignments_task_user_dates;

ALTER TABLE project_assignments
    DROP CONSTRAINT IF EXISTS uq_project_task_user_assignment_effective;

ALTER TABLE project_assignments
    ALTER COLUMN task_id DROP NOT NULL;

DO $$
BEGIN
    ALTER TABLE time_entries
        ADD CONSTRAINT chk_time_entry_project_or_non_project
        CHECK (
            (project_id IS NOT NULL AND non_project_time_category_id IS NULL)
            OR
            (project_id IS NULL AND non_project_time_category_id IS NOT NULL)
        );
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

DO $$
BEGIN
    ALTER TABLE time_entries
        ADD CONSTRAINT chk_time_entry_task_requires_project
        CHECK (task_id IS NULL OR project_id IS NOT NULL);
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

DELETE FROM schema_migrations
WHERE migration_id = '003_task_based_project_assignments';

COMMIT;
