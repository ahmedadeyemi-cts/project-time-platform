-- Guarded rollback for Project FlowHive production migration 074.
-- Refuses to remove customer/project planning evidence after first use.

BEGIN;

DO $projectpulse074_rollback_guard$
BEGIN
    IF to_regclass('public.project_flowhive_plan_versions') IS NOT NULL
       AND EXISTS(SELECT 1 FROM project_flowhive_plan_versions) THEN
        RAISE EXCEPTION 'Rollback refused: Project FlowHive versions exist.';
    END IF;
    IF to_regclass('public.project_flowhive_plan_reviews') IS NOT NULL
       AND EXISTS(SELECT 1 FROM project_flowhive_plan_reviews) THEN
        RAISE EXCEPTION 'Rollback refused: Project FlowHive review evidence exists.';
    END IF;
    IF to_regclass('public.project_flowhive_audit_events') IS NOT NULL
       AND EXISTS(SELECT 1 FROM project_flowhive_audit_events) THEN
        RAISE EXCEPTION 'Rollback refused: Project FlowHive audit evidence exists.';
    END IF;
END;
$projectpulse074_rollback_guard$;

CREATE TEMP TABLE projectpulse074_permissions_to_remove(app_permission_id UUID PRIMARY KEY) ON COMMIT DROP;
CREATE TEMP TABLE projectpulse074_grants_to_remove(
    app_role_id UUID NOT NULL,
    app_permission_id UUID NOT NULL,
    PRIMARY KEY(app_role_id, app_permission_id)) ON COMMIT DROP;

INSERT INTO projectpulse074_permissions_to_remove
SELECT app_permission_id FROM project_flowhive_074_permissions_created ON CONFLICT DO NOTHING;
INSERT INTO projectpulse074_grants_to_remove
SELECT app_role_id, app_permission_id FROM project_flowhive_074_role_grants ON CONFLICT DO NOTHING;

DELETE FROM app_feature_catalog WHERE feature_code='PROJECT_FLOWHIVE_PRODUCTION';
DROP TABLE project_flowhive_074_role_grants;
DROP TABLE project_flowhive_074_permissions_created;

DELETE FROM app_role_permissions grant_row
USING projectpulse074_grants_to_remove evidence
WHERE grant_row.app_role_id=evidence.app_role_id
  AND grant_row.app_permission_id=evidence.app_permission_id;

DELETE FROM app_permissions permission
USING projectpulse074_permissions_to_remove evidence
WHERE permission.app_permission_id=evidence.app_permission_id
  AND NOT EXISTS(SELECT 1 FROM app_role_permissions remaining WHERE remaining.app_permission_id=permission.app_permission_id)
  AND NOT EXISTS(SELECT 1 FROM app_feature_catalog feature WHERE feature.required_permission_code=permission.permission_code);

DROP TRIGGER IF EXISTS trg_project_flowhive_audit_immutable_074 ON project_flowhive_audit_events;
DROP TRIGGER IF EXISTS trg_project_flowhive_reviews_immutable_074 ON project_flowhive_plan_reviews;
DROP TRIGGER IF EXISTS trg_project_flowhive_versions_immutable_074 ON project_flowhive_plan_versions;
DROP TRIGGER IF EXISTS trg_project_flowhive_plan_touch_074 ON project_flowhive_plans;
DROP FUNCTION IF EXISTS projectpulse074_immutable_flowhive_evidence();
DROP FUNCTION IF EXISTS projectpulse074_touch_flowhive_plan();

DROP TABLE IF EXISTS project_flowhive_audit_events;
DROP TABLE IF EXISTS project_flowhive_plan_reviews;
DROP TABLE IF EXISTS project_flowhive_plan_versions;
DROP TABLE IF EXISTS project_flowhive_plans;

DELETE FROM schema_migrations WHERE migration_id='074_module_066_project_flowhive_production';

COMMIT;
