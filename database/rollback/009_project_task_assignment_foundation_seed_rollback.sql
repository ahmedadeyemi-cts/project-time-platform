-- Project Pulse
-- Rollback: 009_project_task_assignment_foundation_seed_rollback.sql

BEGIN;

DELETE FROM project_assignments pa
USING projects p
WHERE pa.project_id = p.project_id
  AND p.project_code = 'USS-PSA-2026';

DELETE FROM project_tasks pt
USING projects p
WHERE pt.project_id = p.project_id
  AND p.project_code = 'USS-PSA-2026';

DELETE FROM projects
WHERE project_code = 'USS-PSA-2026';

DELETE FROM clients
WHERE client_code = 'USS'
  AND client_name = 'US Signal Internal';

DELETE FROM schema_migrations
WHERE migration_id = '009_project_task_assignment_foundation_seed';

COMMIT;
