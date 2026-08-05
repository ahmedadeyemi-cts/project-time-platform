-- Guarded rollback for Module 033 interactive migration 073.
-- Run only after the corresponding API/frontend version has been removed.

BEGIN;

DO $projectpulse073_rollback_guard$
BEGIN
    IF to_regclass('public.project_task_dependencies') IS NOT NULL
       AND EXISTS(SELECT 1 FROM project_task_dependencies) THEN
        RAISE EXCEPTION 'Rollback refused: canonical Project Forge dependencies exist.';
    END IF;
    IF EXISTS(
        SELECT 1 FROM project_forge_audit_events
        WHERE event_code IN (
            'CANONICAL_TASK_CREATED','TASK_DETAILS_UPDATED','TASK_WORKFLOW_UPDATED','TASK_SCHEDULE_UPDATED',
            'TASK_DECISION_UPDATED','TASK_ASSIGNEE_UPDATED','TASK_ARCHIVED','TASK_DEPENDENCY_CREATED',
            'TASK_DEPENDENCY_UPDATED','TASK_DEPENDENCY_DELETED','TASK_REVIEW_COMPLETED',
            'TASK_REVIEW_CHANGES_REQUESTED','TASK_UPDATED'
        ) OR event_code LIKE 'canonical_dependency_%'
    ) THEN
        RAISE EXCEPTION 'Rollback refused: interactive Project Forge audit evidence exists.';
    END IF;
    IF EXISTS(SELECT 1 FROM project_forge_plan_assignments WHERE reviewed_task_revision IS NOT NULL) THEN
        RAISE EXCEPTION 'Rollback refused: explicit task-review revision evidence exists.';
    END IF;
    IF EXISTS(
        SELECT 1 FROM project_forge_task_details
        WHERE parent_task_id IS NOT NULL OR duration_working_days<>0 OR display_order<>0
           OR constraint_type<>'ASAP' OR constraint_date IS NOT NULL OR blocked_reason<>''
    ) OR EXISTS(SELECT 1 FROM project_forge_plan_tasks WHERE blocked_reason<>'') THEN
        RAISE EXCEPTION 'Rollback refused: interactive Project Forge scheduling/workflow data exists.';
    END IF;
END;
$projectpulse073_rollback_guard$;

CREATE TEMP TABLE projectpulse073_permissions_to_remove(
    app_permission_id UUID PRIMARY KEY
) ON COMMIT DROP;
CREATE TEMP TABLE projectpulse073_role_grants_to_remove(
    app_role_id UUID NOT NULL,
    app_permission_id UUID NOT NULL,
    PRIMARY KEY(app_role_id,app_permission_id)
) ON COMMIT DROP;

INSERT INTO projectpulse073_permissions_to_remove(app_permission_id)
SELECT app_permission_id FROM project_forge_073_permissions_created
ON CONFLICT DO NOTHING;
INSERT INTO projectpulse073_role_grants_to_remove(app_role_id,app_permission_id)
SELECT app_role_id,app_permission_id FROM project_forge_073_role_grants
ON CONFLICT DO NOTHING;

-- Remove the evidence foreign keys before deleting catalog rows. The captured
-- transaction-local evidence still limits rollback to rows created by 073.
DROP TABLE project_forge_073_role_grants;
DROP TABLE project_forge_073_permissions_created;

DELETE FROM app_role_permissions grant_row
USING projectpulse073_role_grants_to_remove evidence
WHERE grant_row.app_role_id=evidence.app_role_id
  AND grant_row.app_permission_id=evidence.app_permission_id;

DELETE FROM app_permissions permission
USING projectpulse073_permissions_to_remove evidence
WHERE permission.app_permission_id=evidence.app_permission_id
  AND NOT EXISTS(SELECT 1 FROM app_role_permissions remaining WHERE remaining.app_permission_id=permission.app_permission_id)
  AND NOT EXISTS(SELECT 1 FROM app_feature_catalog feature WHERE feature.required_permission_code=permission.permission_code);

DROP TRIGGER IF EXISTS trg_project_forge_task_details_parent_073 ON project_forge_task_details;
DROP TRIGGER IF EXISTS trg_project_task_dependencies_audit_073 ON project_task_dependencies;
DROP TRIGGER IF EXISTS trg_project_task_dependencies_revision_073 ON project_task_dependencies;
DROP TRIGGER IF EXISTS trg_project_task_dependencies_validate_073 ON project_task_dependencies;
DROP TRIGGER IF EXISTS trg_project_assignments_revision_073 ON project_assignments;
DROP TRIGGER IF EXISTS trg_project_tasks_revision_073 ON project_tasks;

DROP FUNCTION IF EXISTS projectpulse073_working_day_duration(DATE,DATE);
DROP FUNCTION IF EXISTS projectpulse073_working_day_delta(DATE,DATE);
DROP FUNCTION IF EXISTS projectpulse073_add_working_days(DATE,INTEGER);
DROP FUNCTION IF EXISTS projectpulse073_is_working_day(DATE);
DROP FUNCTION IF EXISTS projectpulse073_record_dependency_audit();
DROP FUNCTION IF EXISTS projectpulse073_validate_parent_task();
DROP FUNCTION IF EXISTS projectpulse073_validate_canonical_dependency();

DROP TABLE IF EXISTS project_task_dependencies;
DROP INDEX IF EXISTS ix_project_forge_task_details_project_lane_order;
DROP INDEX IF EXISTS ux_project_assignments_primary_task;

ALTER TABLE project_forge_plan_assignments DROP COLUMN IF EXISTS reviewed_task_revision;
-- updated_at predates migration 073 and must remain intact.
ALTER TABLE project_assignments
    DROP COLUMN IF EXISTS is_primary_assignee,
    DROP COLUMN IF EXISTS revision_number,
    DROP COLUMN IF EXISTS updated_by_user_id;
ALTER TABLE project_forge_plan_tasks DROP COLUMN IF EXISTS blocked_reason;
ALTER TABLE project_forge_plan_tasks
    DROP CONSTRAINT IF EXISTS project_forge_plan_tasks_duration_working_days_check;
ALTER TABLE project_forge_plan_tasks
    ADD CONSTRAINT project_forge_plan_tasks_duration_working_days_check
    CHECK (duration_working_days IS NULL OR duration_working_days >= 0);
ALTER TABLE project_forge_task_dependencies
    DROP CONSTRAINT IF EXISTS project_forge_task_dependencies_lag_working_days_check;
ALTER TABLE project_forge_task_dependencies
    ADD CONSTRAINT project_forge_task_dependencies_lag_working_days_check
    CHECK (lag_working_days BETWEEN -3650 AND 3650);
ALTER TABLE project_forge_task_details
    DROP COLUMN IF EXISTS parent_task_id,
    DROP COLUMN IF EXISTS duration_working_days,
    DROP COLUMN IF EXISTS display_order,
    DROP COLUMN IF EXISTS constraint_type,
    DROP COLUMN IF EXISTS constraint_date,
    DROP COLUMN IF EXISTS blocked_reason;
ALTER TABLE project_tasks
    DROP COLUMN IF EXISTS revision_number,
    DROP COLUMN IF EXISTS updated_by_user_id;

DELETE FROM schema_migrations WHERE migration_id='073_module_033_project_forge_interactive';

COMMIT;
