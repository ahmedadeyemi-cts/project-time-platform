-- Rollback Module 005 Project Expense Upload and Module 038 Certify connection foundation.
-- Fails closed if operational expense uploads or Certify import runs exist.

BEGIN;

DO $projectpulse044_rollback_guard$
BEGIN
    IF to_regclass('public.project_expense_uploads') IS NOT NULL
       AND EXISTS (SELECT 1 FROM project_expense_uploads) THEN
        RAISE EXCEPTION 'Rollback 044 is blocked because project expense upload records exist.';
    END IF;

    IF to_regclass('public.certify_expense_import_runs') IS NOT NULL
       AND EXISTS (SELECT 1 FROM certify_expense_import_runs) THEN
        RAISE EXCEPTION 'Rollback 044 is blocked because Certify import audit records exist.';
    END IF;
END;
$projectpulse044_rollback_guard$;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'VIEW_PROJECT_EXPENSE_UPLOAD',
        'UPLOAD_PROJECT_EXPENSE_SELF',
        'UPLOAD_PROJECT_EXPENSE_ON_BEHALF',
        'DELETE_PROJECT_EXPENSE_UPLOAD',
        'IMPORT_PROJECT_EXPENSE_CERTIFY',
        'VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT',
        'MANAGE_CERTIFY_CONNECTION'
    )
);

DELETE FROM app_permissions
WHERE permission_code IN (
    'VIEW_PROJECT_EXPENSE_UPLOAD',
    'UPLOAD_PROJECT_EXPENSE_SELF',
    'UPLOAD_PROJECT_EXPENSE_ON_BEHALF',
    'DELETE_PROJECT_EXPENSE_UPLOAD',
    'IMPORT_PROJECT_EXPENSE_CERTIFY',
    'VIEW_PROJECT_EXPENSE_INVOICE_CONTEXT',
    'MANAGE_CERTIFY_CONNECTION'
);

DELETE FROM app_feature_catalog
WHERE feature_code = 'PROJECT_EXPENSE_UPLOAD';

UPDATE app_feature_catalog
SET feature_name = 'Project Allocation and Info',
    module_code = 'projects',
    required_permission_code = 'VIEW_PROJECT_ALLOCATION_INFO',
    feature_description = 'View project allocations, engineer hours, SOW/GSD downloads, and project information.',
    updated_at = NOW()
WHERE feature_code = 'PROJECT_ALLOCATION_INFO';

UPDATE scoped_role_policy_modules
SET module_name = 'Project Allocation and Info',
    current_state = 'Installed legacy behavior',
    permission_notes = ''
WHERE module_code = '005';

UPDATE scoped_role_policy_modules
SET module_name = 'Certify Integration Center',
    permission_notes = ''
WHERE module_code = '038';

DROP VIEW IF EXISTS project_expense_current_summary;
DROP TABLE IF EXISTS certify_expense_import_runs;
DROP TABLE IF EXISTS certify_connection_profiles;
DROP TABLE IF EXISTS project_expense_mail_outbox;
DROP TRIGGER IF EXISTS trg_projectpulse044_expense_events_immutable ON project_expense_events;
DROP FUNCTION IF EXISTS projectpulse044_block_expense_event_mutation();
DROP TABLE IF EXISTS project_expense_events;
DROP TABLE IF EXISTS project_expense_lines;
DROP TABLE IF EXISTS project_expense_uploads;

DELETE FROM schema_migrations
WHERE migration_id = '044_project_expense_upload_certify_connection';

COMMIT;
