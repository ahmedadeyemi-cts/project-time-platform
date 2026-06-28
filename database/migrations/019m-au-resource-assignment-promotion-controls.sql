-- 019M-AU Manual Resource Assignment to Project Task Promotion Controls

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('MANAGE_RESOURCE_ASSIGNMENT_PROMOTION', 'Manage Resource Assignment Promotion', 'PROJECT_INTAKE', 'Manually promote engineering resource request assignments into project task assignments without automatic conversion.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'MANAGE_RESOURCE_ASSIGNMENT_PROMOTION'
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGER',
    'PM_TEAM_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD'
)
ON CONFLICT DO NOTHING;
