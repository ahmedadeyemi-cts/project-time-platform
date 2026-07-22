-- ProjectPulse Modules 055C and 055D authorization correction
-- Aligns permission metadata with assigned-PM scope, broad PTC/admin authority,
-- and the governed Module 040 closeout handoff.

BEGIN;

UPDATE app_permissions
SET permission_description = CASE permission_code
    WHEN 'EDIT_WORK_REGISTER_055C' THEN
        'Edit assigned projects as Project Manager, or edit every project as Project Team Coordinator, Administrator, or Super Administrator. All writes remain audited and View-As remains read-only.'
    WHEN 'CREATE_WORK_REGISTER_055D' THEN
        'Create new projects from controlled GSD or SELL intake as Project Team Coordinator, Administrator, or Super Administrator. View-As remains read-only.'
    ELSE permission_description
END
WHERE permission_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D');

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission
  ON permission.permission_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D')
WHERE upper(role.role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR')
ON CONFLICT DO NOTHING;

UPDATE app_feature_catalog
SET feature_description = CASE feature_code
    WHEN 'EDIT_WORK_REGISTER_055C' THEN
        'Manage assigned projects as PM or every project as PTC/Administrator, with audited changes and a selected-project handoff to Module 040 closeout.'
    WHEN 'CREATE_WORK_REGISTER_055D' THEN
        'Create a new project from GSD or SELL as PTC/Administrator; SELL remains authoritative for project name and Actual Rate / Pricing / Rate Review.'
    ELSE feature_description
END,
updated_at = NOW()
WHERE feature_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D');

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '036_work_register_role_scope_and_closeout_handoff',
    'Scope PM edits to assigned projects, grant PTC/admin broad 055C/055D authority, and document the Module 040 closeout handoff',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
