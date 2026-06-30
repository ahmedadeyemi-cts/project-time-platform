BEGIN;

ALTER TABLE app_users
ADD COLUMN IF NOT EXISTS team_name TEXT NULL;

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_USER_ADMIN', 'View User Administration', 'admin', 'View user administration, roles, departments, teams, and local account status.'),
    ('MANAGE_USER_ADMIN', 'Manage User Administration', 'admin', 'Update user profile metadata, roles, departments, teams, login status, and local temporary passwords.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN ('VIEW_USER_ADMIN', 'MANAGE_USER_ADMIN')
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog (
    feature_code,
    feature_name,
    module_code,
    route_anchor,
    required_permission_code,
    feature_description,
    display_order,
    is_active
)
VALUES (
    'USER_ADMINISTRATION',
    'User Administration',
    'admin',
    '#user-admin',
    'VIEW_USER_ADMIN',
    'Manage users, local passwords, roles, teams, departments, and login access.',
    155,
    TRUE
)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '019h1_user_administration',
    'User Administration page for roles, teams, departments, and local password management',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
