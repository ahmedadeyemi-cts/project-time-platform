-- Guarded rollback for ProjectPulse migration 094.
-- Refuses to remove collaboration authority once operational collaborator or
-- immutable audit evidence exists.

BEGIN;

DO $projectpulse094_rollback_guard$
BEGIN
    IF to_regclass('public.project_planning_collaborators') IS NOT NULL
       AND EXISTS (SELECT 1 FROM project_planning_collaborators) THEN
        RAISE EXCEPTION 'Rollback 094 refused: project-planning collaborator records exist.';
    END IF;

    IF to_regclass('public.project_planning_collaboration_audit_events') IS NOT NULL
       AND EXISTS (SELECT 1 FROM project_planning_collaboration_audit_events) THEN
        RAISE EXCEPTION 'Rollback 094 refused: immutable project-planning collaboration evidence exists.';
    END IF;
END;
$projectpulse094_rollback_guard$;

DELETE FROM app_role_permissions assignment
USING project_planning_094_role_grants tracked
WHERE assignment.app_role_id = tracked.app_role_id
  AND assignment.app_permission_id = tracked.app_permission_id;

DELETE FROM app_permissions permission
USING project_planning_094_permissions_created tracked
WHERE permission.app_permission_id = tracked.app_permission_id;

DROP TRIGGER IF EXISTS trg_project_planning_collaborator_audit_094
    ON project_planning_collaborators;
DROP TRIGGER IF EXISTS trg_project_planning_collaborator_touch_094
    ON project_planning_collaborators;

DROP FUNCTION IF EXISTS projectpulse094_record_collaborator_audit();
DROP FUNCTION IF EXISTS projectpulse094_touch_collaborator();
DROP FUNCTION IF EXISTS projectpulse094_can_administer_planner(UUID, UUID);
DROP FUNCTION IF EXISTS projectpulse094_can_edit_planner(UUID, UUID);
DROP FUNCTION IF EXISTS projectpulse094_can_view_project(UUID, UUID);
DROP FUNCTION IF EXISTS projectpulse094_project_scope_reason(UUID, UUID);
DROP FUNCTION IF EXISTS projectpulse094_has_role(UUID, TEXT[]);
DROP FUNCTION IF EXISTS projectpulse094_json_uuid(JSONB, TEXT);

DROP TABLE IF EXISTS project_planning_094_migration_evidence;
DROP TABLE IF EXISTS project_planning_094_role_grants;
DROP TABLE IF EXISTS project_planning_094_permissions_created;
DROP TABLE IF EXISTS project_planning_collaboration_audit_events;
DROP TABLE IF EXISTS project_planning_collaborators;

COMMIT;
