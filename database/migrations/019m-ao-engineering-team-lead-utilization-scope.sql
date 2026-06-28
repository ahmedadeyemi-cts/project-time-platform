-- 019M-AO Engineering Team Lead Utilization Scope + Engineer Selector

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_TEAM_UTILIZATION', 'View Team Utilization', 'utilization', 'View team-scoped utilization for assigned or managed team members.'),
    ('VIEW_INDIVIDUAL_UTILIZATION', 'View Individual Utilization', 'utilization', 'View individual utilization records when role scope allows.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code IN ('VIEW_TEAM_UTILIZATION', 'VIEW_INDIVIDUAL_UTILIZATION')
WHERE r.role_code IN ('ENGINEERING_TEAM_LEAD', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_OWN_UTILIZATION'
WHERE r.role_code = 'ENGINEER'
ON CONFLICT DO NOTHING;
