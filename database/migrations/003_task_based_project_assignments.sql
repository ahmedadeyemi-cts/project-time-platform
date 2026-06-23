-- Project Time Platform
-- Migration: 003_task_based_project_assignments.sql
-- Purpose: Require project assignments and project time entries to use project tasks.

BEGIN;

ALTER TABLE project_assignments
    ALTER COLUMN task_id SET NOT NULL;

DO $$
BEGIN
    ALTER TABLE project_assignments
        ADD CONSTRAINT uq_project_task_user_assignment_effective
        UNIQUE (project_id, task_id, user_id, effective_start_date);
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

ALTER TABLE time_entries
    DROP CONSTRAINT IF EXISTS chk_time_entry_project_or_non_project;

ALTER TABLE time_entries
    DROP CONSTRAINT IF EXISTS chk_time_entry_task_requires_project;

DO $$
BEGIN
    ALTER TABLE time_entries
        ADD CONSTRAINT chk_time_entry_project_task_or_non_project
        CHECK (
            (project_id IS NOT NULL AND task_id IS NOT NULL AND non_project_time_category_id IS NULL)
            OR
            (project_id IS NULL AND task_id IS NULL AND non_project_time_category_id IS NOT NULL)
        );
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

CREATE INDEX IF NOT EXISTS idx_project_assignments_task_user_dates
    ON project_assignments(task_id, user_id, effective_start_date, effective_end_date);

CREATE INDEX IF NOT EXISTS idx_time_entries_project_task_user_date
    ON time_entries(project_id, task_id, user_id, work_date);

INSERT INTO schema_migrations (migration_id, description)
VALUES ('003_task_based_project_assignments', 'Require project assignments and project time entries to use project tasks')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
