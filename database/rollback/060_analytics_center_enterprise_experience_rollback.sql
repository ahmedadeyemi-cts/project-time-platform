-- Roll back Module 030 Analytics Center enterprise experience migration 060.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'VIEW_ANALYTICS_DASHBOARDS',
        'VIEW_ANALYTICS_SCHEDULES',
        'MANAGE_ANALYTICS_SCHEDULES',
        'DELIVER_ANALYTICS_SCHEDULES'
    )
);

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_ANALYTICS_DASHBOARDS',
    'VIEW_ANALYTICS_SCHEDULES',
    'MANAGE_ANALYTICS_SCHEDULES',
    'DELIVER_ANALYTICS_SCHEDULES'
);

DROP TRIGGER IF EXISTS trg_projectpulse060_schedule_delivery_immutable
    ON analytics_report_schedule_delivery_attempts;
DROP TRIGGER IF EXISTS trg_projectpulse060_schedule_runs_immutable
    ON analytics_report_schedule_runs;
DROP FUNCTION IF EXISTS projectpulse060_block_analytics_schedule_evidence_mutation();

DROP TABLE IF EXISTS analytics_report_schedule_delivery_attempts;
DROP TABLE IF EXISTS analytics_report_schedule_runs;
DROP TABLE IF EXISTS analytics_report_schedule_recipients;
DROP TABLE IF EXISTS analytics_report_schedules;
DROP TABLE IF EXISTS analytics_user_report_activity;

-- PDF is introduced by migration 060. Remove only PDF export evidence before
-- restoring the migration-055 csv/xlsx/json constraint. The referenced report
-- runs remain intact.
DELETE FROM enterprise_report_exports
WHERE export_format = 'pdf';

ALTER TABLE enterprise_report_exports
    DROP CONSTRAINT IF EXISTS enterprise_report_exports_export_format_check;
ALTER TABLE enterprise_report_exports
    ADD CONSTRAINT enterprise_report_exports_export_format_check
    CHECK (export_format IN ('csv', 'xlsx', 'json'));

DELETE FROM schema_migrations
WHERE migration_id = '060_analytics_center_enterprise_experience';

COMMIT;
