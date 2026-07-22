-- ProjectPulse migration 035 rollback.
-- Preserves Work Register audit history and intake data.

BEGIN;

DELETE FROM app_feature_catalog
WHERE feature_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D');

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D')
);

DELETE FROM app_permissions
WHERE permission_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D');

DELETE FROM schema_migrations
WHERE migration_id = '035_work_register_055c_055d_split';

COMMIT;
