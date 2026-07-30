-- Roll back Module 030 Enterprise Reporting Center migration 054.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id FROM app_permissions
    WHERE permission_code IN (
        'VIEW_ENTERPRISE_REPORTING',
        'RUN_ENTERPRISE_REPORTING',
        'EXPORT_ENTERPRISE_REPORTING',
        'MANAGE_ENTERPRISE_REPORTING'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code = 'ENTERPRISE_REPORTING_CENTER';

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_ENTERPRISE_REPORTING',
    'RUN_ENTERPRISE_REPORTING',
    'EXPORT_ENTERPRISE_REPORTING',
    'MANAGE_ENTERPRISE_REPORTING'
);

DROP TRIGGER IF EXISTS trg_projectpulse054_report_exports_immutable ON enterprise_report_exports;
DROP TRIGGER IF EXISTS trg_projectpulse054_report_runs_immutable ON enterprise_report_runs;
DROP FUNCTION IF EXISTS projectpulse054_block_enterprise_report_evidence_mutation();

DROP TABLE IF EXISTS enterprise_report_exports;
DROP TABLE IF EXISTS enterprise_report_saved_views;
DROP TABLE IF EXISTS enterprise_report_runs;

DELETE FROM schema_migrations
WHERE migration_id = '054_enterprise_reporting_center';

COMMIT;
