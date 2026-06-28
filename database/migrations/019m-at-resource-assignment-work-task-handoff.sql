-- 019M-AT Resource Request Assignment to Work Task Assignment Handoff

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_RESOURCE_ASSIGNMENT_HANDOFF', 'View Resource Assignment Handoff', 'PROJECT_INTAKE', 'View readiness between engineering resource request assignments, project tasks, project assignments, timesheets, and utilization.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_RESOURCE_ASSIGNMENT_HANDOFF'
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGER',
    'PM_TEAM_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'PROJECT_COORDINATOR'
)
ON CONFLICT DO NOTHING;
