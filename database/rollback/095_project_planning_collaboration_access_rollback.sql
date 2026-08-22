-- Guarded rollback for ProjectPulse migration 095.
-- Refuses to erase configured project collaborators or append-only evidence.

BEGIN;
DO $projectpulse095_flowhive_run_guard$
BEGIN
    IF to_regclass('public.project_flowhive_ai_planner_runs') IS NOT NULL
       AND EXISTS(SELECT 1 FROM project_flowhive_ai_planner_runs) THEN
        RAISE EXCEPTION 'Rollback 095 refused: durable FlowHive AI Planner operation evidence exists.';
    END IF;
END;
$projectpulse095_flowhive_run_guard$;


DO $projectpulse095_rollback_guard$
BEGIN
    IF to_regclass('public.project_planning_collaborators') IS NOT NULL
       AND EXISTS(SELECT 1 FROM project_planning_collaborators) THEN
        RAISE EXCEPTION 'Rollback 095 refused: project planning collaborator assignments exist.';
    END IF;

    IF to_regclass('public.project_planning_collaboration_audit_events') IS NOT NULL
       AND EXISTS(SELECT 1 FROM project_planning_collaboration_audit_events) THEN
        RAISE EXCEPTION 'Rollback 095 refused: immutable project planning collaboration audit evidence exists.';
    END IF;
END;
$projectpulse095_rollback_guard$;

DELETE FROM app_role_permissions grant_row
USING project_planning_095_role_grants evidence
WHERE grant_row.app_role_id=evidence.app_role_id
  AND grant_row.app_permission_id=evidence.app_permission_id;

DELETE FROM app_permissions permission
USING project_planning_095_permissions_created evidence
WHERE permission.app_permission_id=evidence.app_permission_id
  AND permission.permission_code=evidence.permission_code
  AND NOT EXISTS(
      SELECT 1 FROM app_role_permissions remaining
      WHERE remaining.app_permission_id=permission.app_permission_id
  );

DROP TRIGGER IF EXISTS trg_project_planning_collaboration_audit_immutable_095
    ON project_planning_collaboration_audit_events;
DROP TRIGGER IF EXISTS trg_project_planning_collaborator_audit_095
    ON project_planning_collaborators;
DROP TRIGGER IF EXISTS trg_project_planning_collaborator_touch_095
    ON project_planning_collaborators;

DROP FUNCTION IF EXISTS projectpulse095_block_collaboration_audit_mutation();
DROP FUNCTION IF EXISTS projectpulse095_audit_planning_collaborator();
DROP FUNCTION IF EXISTS projectpulse095_touch_planning_collaborator();

DROP TABLE IF EXISTS project_planning_collaboration_audit_events;
DROP TABLE IF EXISTS project_flowhive_ai_planner_runs;
DROP TABLE IF EXISTS project_planning_collaborators;
DROP TABLE IF EXISTS project_planning_095_role_grants;
DROP TABLE IF EXISTS project_planning_095_permissions_created;

DELETE FROM schema_migrations
WHERE migration_id='095_project_planning_collaboration_access';

COMMIT;
