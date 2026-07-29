-- Roll back ProjectPulse Group 5 migration 051.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'VIEW_FINANCIAL_REPORT_CENTER',
        'RUN_FINANCIAL_REPORTS',
        'EXPORT_FINANCIAL_REPORTS',
        'VIEW_FINANCIAL_OPERATIONS_WORKBENCH',
        'MANAGE_FINANCIAL_OPERATIONS_RECOVERY',
        'RETRY_FINANCIAL_SOURCES',
        'VIEW_ACCOUNTING_RECONCILIATION_RECOVERY',
        'VIEW_PROJECT_CLOSEOUT_RECOVERY',
        'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY',
        'VIEW_BILLING_RECOVERY'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code IN (
    'FINANCIAL_REPORT_CENTER',
    'FINANCIAL_OPERATIONS_WORKBENCH',
    'BILLING_READINESS_RECOVERY',
    'PROJECT_CLOSEOUT_RECOVERY',
    'CLOSEOUT_NOTIFICATION_RECOVERY',
    'BILLING_RECOVERY'
);

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_FINANCIAL_REPORT_CENTER',
    'RUN_FINANCIAL_REPORTS',
    'EXPORT_FINANCIAL_REPORTS',
    'VIEW_FINANCIAL_OPERATIONS_WORKBENCH',
    'MANAGE_FINANCIAL_OPERATIONS_RECOVERY',
    'RETRY_FINANCIAL_SOURCES',
    'VIEW_ACCOUNTING_RECONCILIATION_RECOVERY',
    'VIEW_PROJECT_CLOSEOUT_RECOVERY',
    'VIEW_CLOSEOUT_NOTIFICATION_RECOVERY',
    'VIEW_BILLING_RECOVERY'
);

DROP TRIGGER IF EXISTS trg_projectpulse051_financial_actions_immutable
    ON financial_operations_actions;
DROP FUNCTION IF EXISTS projectpulse051_block_financial_action_mutation();

DROP TABLE IF EXISTS financial_operations_actions;
DROP TABLE IF EXISTS financial_operations_work_items;
DROP TABLE IF EXISTS financial_report_runs;

DELETE FROM schema_migrations
WHERE migration_id = '051_financial_operations_reporting_recovery';

COMMIT;
