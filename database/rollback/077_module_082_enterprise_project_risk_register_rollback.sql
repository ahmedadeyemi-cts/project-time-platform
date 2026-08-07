-- Guarded rollback for Module 082. Risk, action, version, and audit evidence prevents removal.

BEGIN;

DO $pulse077_rollback_guard$
BEGIN
    IF to_regclass('public.project_risks') IS NOT NULL AND EXISTS(SELECT 1 FROM project_risks) THEN
        RAISE EXCEPTION 'Rollback refused: Module 082 risk records exist.';
    END IF;
    IF to_regclass('public.project_risk_actions') IS NOT NULL AND EXISTS(SELECT 1 FROM project_risk_actions) THEN
        RAISE EXCEPTION 'Rollback refused: Module 082 action records exist.';
    END IF;
    IF to_regclass('public.project_risk_versions') IS NOT NULL AND EXISTS(SELECT 1 FROM project_risk_versions) THEN
        RAISE EXCEPTION 'Rollback refused: Module 082 immutable versions exist.';
    END IF;
    IF to_regclass('public.project_risk_audit_events') IS NOT NULL AND EXISTS(SELECT 1 FROM project_risk_audit_events) THEN
        RAISE EXCEPTION 'Rollback refused: Module 082 audit evidence exists.';
    END IF;
END;
$pulse077_rollback_guard$;

CREATE TEMP TABLE pulse077_permissions_to_remove(app_permission_id UUID PRIMARY KEY) ON COMMIT DROP;
CREATE TEMP TABLE pulse077_grants_to_remove(app_role_id UUID,app_permission_id UUID,PRIMARY KEY(app_role_id,app_permission_id)) ON COMMIT DROP;
INSERT INTO pulse077_permissions_to_remove SELECT app_permission_id FROM project_risk_077_permissions_created ON CONFLICT DO NOTHING;
INSERT INTO pulse077_grants_to_remove SELECT app_role_id,app_permission_id FROM project_risk_077_role_grants ON CONFLICT DO NOTHING;

DELETE FROM app_feature_catalog WHERE feature_code='ENTERPRISE_PROJECT_RISK_REGISTER_082';
DROP TABLE project_risk_077_role_grants;
DROP TABLE project_risk_077_permissions_created;
DELETE FROM app_role_permissions grant_row USING pulse077_grants_to_remove evidence
WHERE grant_row.app_role_id=evidence.app_role_id AND grant_row.app_permission_id=evidence.app_permission_id;
DELETE FROM app_permissions permission USING pulse077_permissions_to_remove evidence
WHERE permission.app_permission_id=evidence.app_permission_id
  AND NOT EXISTS(SELECT 1 FROM app_role_permissions remaining WHERE remaining.app_permission_id=permission.app_permission_id)
  AND NOT EXISTS(SELECT 1 FROM app_feature_catalog feature WHERE feature.required_permission_code=permission.permission_code);

DROP TRIGGER IF EXISTS trg_project_risk_audit_immutable_077 ON project_risk_audit_events;
DROP TRIGGER IF EXISTS trg_project_risk_action_history_immutable_077 ON project_risk_action_history;
DROP TRIGGER IF EXISTS trg_project_risk_versions_immutable_077 ON project_risk_versions;
DROP TRIGGER IF EXISTS trg_project_risk_action_touch_077 ON project_risk_actions;
DROP TRIGGER IF EXISTS trg_project_risk_action_validate_077 ON project_risk_actions;
DROP TRIGGER IF EXISTS trg_project_risk_touch_077 ON project_risks;
DROP TRIGGER IF EXISTS trg_project_risk_owner_077 ON project_risks;
DROP TRIGGER IF EXISTS trg_project_risk_number_077 ON project_risks;
DROP FUNCTION IF EXISTS pulse077_immutable_evidence();
DROP FUNCTION IF EXISTS pulse077_validate_action();
DROP FUNCTION IF EXISTS pulse077_validate_owner();
DROP FUNCTION IF EXISTS pulse077_touch_action();
DROP FUNCTION IF EXISTS pulse077_touch_revision();
DROP FUNCTION IF EXISTS pulse077_next_risk_number();

DROP TABLE IF EXISTS project_risk_audit_events;
DROP TABLE IF EXISTS project_risk_action_history;
DROP TABLE IF EXISTS project_risk_actions;
DROP TABLE IF EXISTS project_risk_versions;
DROP TABLE IF EXISTS project_risks;
DROP TABLE IF EXISTS project_risk_counters;
DELETE FROM schema_migrations WHERE migration_id='077_module_082_enterprise_project_risk_register';

COMMIT;
