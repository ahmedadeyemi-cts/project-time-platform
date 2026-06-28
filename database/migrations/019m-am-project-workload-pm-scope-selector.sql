-- 019M-AM Project Workload PM Scope + PM Team Lead Selector

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_PROJECT_WORKLOAD'
WHERE r.role_code IN ('PM_TEAM_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD')
ON CONFLICT DO NOTHING;
