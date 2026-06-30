-- 019M-AK Project Manager Workload Dashboard

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES (
    'VIEW_PROJECT_WORKLOAD',
    'View Project Workload',
    'PROJECT_WORKLOAD',
    'View project-manager workload dashboard including active projects, closed projects, project status, and PM-owned workload risks.'
)
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_PROJECT_WORKLOAD'
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER')
ON CONFLICT DO NOTHING;
