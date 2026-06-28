-- 019M-AH Customer Directory Management UI permissions

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_CUSTOMERS', 'View Customer Directory', 'CUSTOMERS', 'View customer directory, customer contacts, and customer project/intake summaries.'),
    ('MANAGE_CUSTOMERS', 'Manage Customer Directory', 'CUSTOMERS', 'Create and update customer records and customer contacts.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code IN ('VIEW_CUSTOMERS', 'MANAGE_CUSTOMERS')
WHERE r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p
    ON p.permission_code = 'VIEW_CUSTOMERS'
WHERE r.role_code IN ('PROJECT_MANAGEMENT', 'MANAGER')
ON CONFLICT DO NOTHING;

GRANT SELECT, INSERT, UPDATE ON TABLE clients TO "ptp_app";
GRANT SELECT, INSERT, UPDATE ON TABLE client_contacts TO "ptp_app";
