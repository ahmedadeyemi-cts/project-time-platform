-- Roll back only the permission-catalog additions made by migration 036.
-- Application authorization must be rolled back with its matching source commit.

BEGIN;

DELETE FROM app_role_permissions role_permission
USING app_roles role, app_permissions permission
WHERE role_permission.app_role_id = role.app_role_id
  AND role_permission.app_permission_id = permission.app_permission_id
  AND upper(role.role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR')
  AND permission.permission_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D');

UPDATE app_permissions
SET permission_description = CASE permission_code
    WHEN 'EDIT_WORK_REGISTER_055C' THEN
        'Manage existing projects, tasks, assignments, documents, purchase orders, lifecycle fields, closeout entry points, and billing requests with audit history.'
    WHEN 'CREATE_WORK_REGISTER_055D' THEN
        'Create new projects from controlled GSD or SELL intake with durable Work Register audit history.'
    ELSE permission_description
END
WHERE permission_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D');

DELETE FROM schema_migrations
WHERE migration_id = '036_work_register_role_scope_and_closeout_handoff';

COMMIT;
