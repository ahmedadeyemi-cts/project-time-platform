-- 019M-AR Project Intake to Work Task Builder Handoff Readiness

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_INTAKE_WORK_TASK_HANDOFF', 'View Intake Work Task Handoff', 'PROJECT_INTAKE', 'View readiness between project intake, project records, work tasks, assignments, timesheets, and utilization.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_INTAKE_WORK_TASK_HANDOFF'
WHERE r.role_code IN (
    'ADMINISTRATOR',
    'PROJECT_TEAM_COORDINATOR',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGER',
    'PM_TEAM_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'MANAGER',
    'PROJECT_COORDINATOR'
)
ON CONFLICT DO NOTHING;
