BEGIN;

ALTER TABLE project_allocation_projects
ADD COLUMN IF NOT EXISTS source_project_id UUID NULL;

ALTER TABLE project_allocation_projects
ADD COLUMN IF NOT EXISTS source_task_id UUID NULL;

ALTER TABLE project_allocation_projects
ADD COLUMN IF NOT EXISTS source_mapping_notes TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_project_allocation_projects_source_project
ON project_allocation_projects(source_project_id)
WHERE source_project_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_time_entries_project_user_status_date
ON time_entries(project_id, user_id, status, work_date)
WHERE project_id IS NOT NULL;

CREATE OR REPLACE VIEW project_allocation_used_hours_vw AS
SELECT
    p.project_allocation_project_id,
    pea.user_id,
    COALESCE(SUM(te.hours), 0)::numeric(10,2) AS used_hours
FROM project_allocation_projects p
JOIN project_engineer_allocations pea
  ON pea.project_allocation_project_id = p.project_allocation_project_id
 AND pea.is_active = TRUE
LEFT JOIN time_entries te
  ON te.user_id = pea.user_id
 AND te.project_id = p.source_project_id
 AND (
        p.source_task_id IS NULL
     OR te.task_id = p.source_task_id
 )
 AND te.billable = TRUE
 AND te.status IN (
        'submitted',
        'manager_approved',
        'approved',
        'project_approved',
        'accounting_approved'
 )
GROUP BY p.project_allocation_project_id, pea.user_id;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '019k_project_allocation_used_hours',
    'Wire project allocation used hours to billable time entries using source project and task mapping',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
