-- Roll back Module 030 Analytics Center migration 055.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'VIEW_ENTERPRISE_REPORTING',
        'RUN_ENTERPRISE_REPORTING',
        'EXPORT_ENTERPRISE_REPORTING',
        'MANAGE_ENTERPRISE_REPORTING'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code = 'ANALYTICS_CENTER';

UPDATE app_feature_catalog
SET is_active = TRUE,
    updated_at = NOW()
WHERE feature_code = 'FINANCIAL_REPORT_CENTER';

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_ENTERPRISE_REPORTING',
    'RUN_ENTERPRISE_REPORTING',
    'EXPORT_ENTERPRISE_REPORTING',
    'MANAGE_ENTERPRISE_REPORTING'
);

DROP TRIGGER IF EXISTS trg_projectpulse055_analytics_runs_immutable ON enterprise_report_runs;
DROP TRIGGER IF EXISTS trg_projectpulse055_analytics_exports_immutable ON enterprise_report_exports;
DROP FUNCTION IF EXISTS projectpulse055_block_analytics_evidence_mutation();

DROP TABLE IF EXISTS enterprise_report_exports;
DROP TABLE IF EXISTS enterprise_report_saved_views;
DROP TABLE IF EXISTS enterprise_report_runs;

DELETE FROM schema_migrations
WHERE migration_id = '055_analytics_center';

COMMIT;
