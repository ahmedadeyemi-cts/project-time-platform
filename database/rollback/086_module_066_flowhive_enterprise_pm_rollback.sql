-- Guarded rollback for ProjectPulse migration 086.
-- Refuses to remove enterprise PM data after any working copy, control, RAID,
-- status report, or customer share has been created.

BEGIN;

DO $projectpulse086_rollback_guard$
DECLARE
    populated BOOLEAN;
BEGIN
    SELECT
        EXISTS(SELECT 1 FROM project_flowhive_working_copies)
        OR EXISTS(SELECT 1 FROM project_flowhive_project_controls)
        OR EXISTS(SELECT 1 FROM project_flowhive_raid_items)
        OR EXISTS(SELECT 1 FROM project_flowhive_status_reports)
        OR EXISTS(SELECT 1 FROM project_flowhive_customer_shares)
        OR EXISTS(SELECT 1 FROM project_flowhive_share_access_events)
    INTO populated;

    IF populated THEN
        RAISE EXCEPTION 'Rollback refused: Project FlowHive enterprise PM records exist.';
    END IF;
END;
$projectpulse086_rollback_guard$;

DROP TRIGGER IF EXISTS trg_projectpulse086_classify_flowhive_document ON project_intake_documents;
DROP FUNCTION IF EXISTS projectpulse086_classify_flowhive_document();

DROP TRIGGER IF EXISTS trg_project_flowhive_status_report_immutable_086 ON project_flowhive_status_reports;
DROP FUNCTION IF EXISTS projectpulse086_immutable_status_report();
DROP TRIGGER IF EXISTS trg_project_flowhive_raid_touch_086 ON project_flowhive_raid_items;
DROP TRIGGER IF EXISTS trg_project_flowhive_controls_touch_086 ON project_flowhive_project_controls;
DROP FUNCTION IF EXISTS projectpulse086_touch_controls();
DROP TRIGGER IF EXISTS trg_project_flowhive_working_copy_touch_086 ON project_flowhive_working_copies;
DROP FUNCTION IF EXISTS projectpulse086_touch_working_copy();

DROP TABLE IF EXISTS project_flowhive_share_access_events;
DROP TABLE IF EXISTS project_flowhive_customer_shares;
DROP TABLE IF EXISTS project_flowhive_status_reports;
DROP TABLE IF EXISTS project_flowhive_raid_items;
DROP TABLE IF EXISTS project_flowhive_project_controls;
DROP TABLE IF EXISTS project_flowhive_working_copies;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id FROM app_permissions
    WHERE permission_code IN (
        'MANAGE_FLOWHIVE_PM_WORKSPACE_066',
        'CREATE_FLOWHIVE_CUSTOMER_SHARE_066',
        'VIEW_FLOWHIVE_FINANCIALS_066'
    )
);
DELETE FROM app_permissions
WHERE permission_code IN (
    'MANAGE_FLOWHIVE_PM_WORKSPACE_066',
    'CREATE_FLOWHIVE_CUSTOMER_SHARE_066',
    'VIEW_FLOWHIVE_FINANCIALS_066'
);
DELETE FROM schema_migrations WHERE migration_id='086_module_066_flowhive_enterprise_pm';

COMMIT;
