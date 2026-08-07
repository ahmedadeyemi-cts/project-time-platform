-- Guarded rollback for Module 001A Engineer Request Closeout migration 078.

BEGIN;

DO $projectpulse078_rollback_guard$
BEGIN
    IF to_regclass('public.module001a_engineer_task_closeouts') IS NOT NULL
       AND EXISTS (SELECT 1 FROM module001a_engineer_task_closeouts) THEN
        RAISE EXCEPTION 'Rollback refused: Module 001A closeout records exist.';
    END IF;
    IF to_regclass('public.module001a_engineer_task_closeout_events') IS NOT NULL
       AND EXISTS (SELECT 1 FROM module001a_engineer_task_closeout_events) THEN
        RAISE EXCEPTION 'Rollback refused: Module 001A immutable transition evidence exists.';
    END IF;
END;
$projectpulse078_rollback_guard$;

CREATE TEMP TABLE projectpulse078_permissions_to_remove(app_permission_id UUID PRIMARY KEY) ON COMMIT DROP;
CREATE TEMP TABLE projectpulse078_grants_to_remove(
    app_role_id UUID NOT NULL,
    app_permission_id UUID NOT NULL,
    PRIMARY KEY(app_role_id, app_permission_id)) ON COMMIT DROP;

INSERT INTO projectpulse078_permissions_to_remove
SELECT app_permission_id FROM module001a_078_permissions_created ON CONFLICT DO NOTHING;
INSERT INTO projectpulse078_grants_to_remove
SELECT app_role_id, app_permission_id FROM module001a_078_role_grants ON CONFLICT DO NOTHING;

DELETE FROM app_feature_catalog WHERE feature_code = 'ENGINEER_TASK_CLOSEOUT_001A';
DROP TABLE module001a_078_role_grants;
DROP TABLE module001a_078_permissions_created;

DELETE FROM app_role_permissions grant_row
USING projectpulse078_grants_to_remove evidence
WHERE grant_row.app_role_id = evidence.app_role_id
  AND grant_row.app_permission_id = evidence.app_permission_id;

DELETE FROM app_permissions permission
USING projectpulse078_permissions_to_remove evidence
WHERE permission.app_permission_id = evidence.app_permission_id
  AND NOT EXISTS (SELECT 1 FROM app_role_permissions remaining WHERE remaining.app_permission_id = permission.app_permission_id)
  AND NOT EXISTS (SELECT 1 FROM app_feature_catalog feature WHERE feature.required_permission_code = permission.permission_code);

DROP TRIGGER IF EXISTS trg_module001a_task_final_078 ON project_tasks;
DROP TRIGGER IF EXISTS trg_module001a_project_final_078 ON projects;
DROP TRIGGER IF EXISTS trg_module001a_time_guard_078 ON time_entries;
DROP TRIGGER IF EXISTS trg_module001a_events_immutable_078 ON module001a_engineer_task_closeout_events;
DROP TRIGGER IF EXISTS trg_module001a_closeout_touch_078 ON module001a_engineer_task_closeouts;

DROP FUNCTION IF EXISTS projectpulse078_finalize_task_closeout();
DROP FUNCTION IF EXISTS projectpulse078_finalize_project_closeouts();
DROP FUNCTION IF EXISTS projectpulse078_block_closed_assignment_time();
DROP FUNCTION IF EXISTS projectpulse078_immutable_closeout_event();
DROP FUNCTION IF EXISTS projectpulse078_touch_closeout();

DROP TABLE IF EXISTS module001a_engineer_task_closeout_events;
DROP TABLE IF EXISTS module001a_engineer_task_closeouts;

ALTER TABLE project_assignments
    DROP CONSTRAINT IF EXISTS chk_project_assignments_module001a_closeout_status;
ALTER TABLE project_assignments
    DROP COLUMN IF EXISTS module001a_closeout_updated_at,
    DROP COLUMN IF EXISTS module001a_closeout_status;

DELETE FROM schema_migrations WHERE migration_id = '078_module_001a_engineer_request_closeout';

COMMIT;
