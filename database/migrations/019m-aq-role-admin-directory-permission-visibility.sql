-- 019M-AQ Role Administration Directory + Permission Visibility

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_ROLE_ADMIN_DIRECTORY', 'View Role Administration Directory', 'SECURITY', 'View role definitions, assigned users, and permissions grouped by module.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_ROLE_ADMIN_DIRECTORY'
WHERE r.role_code = 'ADMINISTRATOR'
ON CONFLICT DO NOTHING;
