-- Extend Module 005 self-service roles to use the governed Module 038 Certify connection.
BEGIN;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission
  ON permission.permission_code = 'IMPORT_PROJECT_EXPENSE_CERTIFY'
WHERE upper(role.role_code) IN (
    'ENGINEER', 'ENGINEERING',
    'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD',
    'PROJECT_MANAGER', 'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD',
    'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'
)
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '044a_project_expense_self_certify_permission',
    'Allow Engineers, Engineering Leads, Project Management, and PM Leads to import their own project expenses from the governed Module 038 Certify connection',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description=EXCLUDED.description, applied_at=EXCLUDED.applied_at;

COMMIT;
