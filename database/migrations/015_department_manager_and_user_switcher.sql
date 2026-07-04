-- Project Health Dashboard
-- Migration: 015_department_manager_and_user_switcher.sql
-- Purpose: Add department-manager structure and seed users for role validation.

BEGIN;

CREATE TABLE IF NOT EXISTS app_departments (
    app_department_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    department_code VARCHAR(75) NOT NULL UNIQUE,
    department_name VARCHAR(200) NOT NULL,
    department_description TEXT,
    manager_user_id UUID REFERENCES app_users(user_id),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE app_users
    ADD COLUMN IF NOT EXISTS app_department_id UUID REFERENCES app_departments(app_department_id),
    ADD COLUMN IF NOT EXISTS manager_user_id UUID REFERENCES app_users(user_id);

CREATE INDEX IF NOT EXISTS idx_app_users_app_department_id ON app_users(app_department_id);
CREATE INDEX IF NOT EXISTS idx_app_users_manager_user_id ON app_users(manager_user_id);
CREATE INDEX IF NOT EXISTS idx_app_departments_manager_user_id ON app_departments(manager_user_id);

-- Remove old local-only development account from active Role Admin testing.
DELETE FROM app_user_role_assignments
WHERE user_id IN (
    SELECT user_id FROM app_users WHERE email = 'ahmed.adeyemi@ussignal.local'
);

DELETE FROM app_users
WHERE email = 'ahmed.adeyemi@ussignal.local';

-- Ensure named managers exist.
INSERT INTO app_users (email, display_name, job_title, department, is_active)
VALUES
    ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Department Manager / Administrator', 'Systems / Collaboration', TRUE),
    ('matthew.lenoble@ussignal.com', 'Matthew LeNoble', 'Department Manager / Project Management', 'Enterprise Networking / Project Management Office', TRUE)
ON CONFLICT (email) DO UPDATE
SET display_name = EXCLUDED.display_name,
    job_title = EXCLUDED.job_title,
    department = EXCLUDED.department,
    is_active = TRUE,
    updated_at = NOW();

-- Official Project Health Dashboard departments.
INSERT INTO app_departments (department_code, department_name, department_description, manager_user_id, display_order, is_active)
VALUES
    ('ENTERPRISE_NETWORKING', 'Enterprise Networking', 'Enterprise Networking delivery and engineering team.', (SELECT user_id FROM app_users WHERE email = 'matthew.lenoble@ussignal.com'), 10, TRUE),
    ('PROJECT_MANAGEMENT_OFFICE', 'Project Management Office', 'Project management, intake, scheduling, and delivery coordination office.', (SELECT user_id FROM app_users WHERE email = 'matthew.lenoble@ussignal.com'), 20, TRUE),
    ('SYSTEMS', 'Systems', 'Systems engineering delivery team.', (SELECT user_id FROM app_users WHERE email = 'ahmed.adeyemi@ussignal.com'), 30, TRUE),
    ('COLLABORATION', 'Collaboration', 'Collaboration engineering delivery team.', (SELECT user_id FROM app_users WHERE email = 'ahmed.adeyemi@ussignal.com'), 40, TRUE)
ON CONFLICT (department_code) DO UPDATE
SET department_name = EXCLUDED.department_name,
    department_description = EXCLUDED.department_description,
    manager_user_id = EXCLUDED.manager_user_id,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Seed role-testing users.
INSERT INTO app_users (email, display_name, job_title, department, app_department_id, manager_user_id, is_active)
VALUES
    ('network.engineer@ussignal.com', 'Enterprise Networking Engineer', 'Engineer', 'Enterprise Networking', (SELECT app_department_id FROM app_departments WHERE department_code = 'ENTERPRISE_NETWORKING'), (SELECT user_id FROM app_users WHERE email = 'matthew.lenoble@ussignal.com'), TRUE),
    ('systems.engineer@ussignal.com', 'Systems Engineer', 'Engineer', 'Systems', (SELECT app_department_id FROM app_departments WHERE department_code = 'SYSTEMS'), (SELECT user_id FROM app_users WHERE email = 'ahmed.adeyemi@ussignal.com'), TRUE),
    ('collaboration.engineer@ussignal.com', 'Collaboration Engineer', 'Engineer', 'Collaboration', (SELECT app_department_id FROM app_departments WHERE department_code = 'COLLABORATION'), (SELECT user_id FROM app_users WHERE email = 'ahmed.adeyemi@ussignal.com'), TRUE),
    ('ptc.coordinator@ussignal.com', 'Project and Team Coordinator', 'Project and Team Coordinator', 'Project Management Office', (SELECT app_department_id FROM app_departments WHERE department_code = 'PROJECT_MANAGEMENT_OFFICE'), (SELECT user_id FROM app_users WHERE email = 'matthew.lenoble@ussignal.com'), TRUE)
ON CONFLICT (email) DO UPDATE
SET display_name = EXCLUDED.display_name,
    job_title = EXCLUDED.job_title,
    department = EXCLUDED.department,
    app_department_id = EXCLUDED.app_department_id,
    manager_user_id = EXCLUDED.manager_user_id,
    is_active = TRUE,
    updated_at = NOW();

UPDATE app_users
SET app_department_id = (SELECT app_department_id FROM app_departments WHERE department_code = 'SYSTEMS'),
    department = 'Systems / Collaboration',
    job_title = 'Department Manager / Administrator',
    updated_at = NOW()
WHERE email = 'ahmed.adeyemi@ussignal.com';

UPDATE app_users
SET app_department_id = (SELECT app_department_id FROM app_departments WHERE department_code = 'PROJECT_MANAGEMENT_OFFICE'),
    department = 'Enterprise Networking / Project Management Office',
    job_title = 'Department Manager / Project Management',
    updated_at = NOW()
WHERE email = 'matthew.lenoble@ussignal.com';

-- Assign roles.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Role enforcement seed', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'ADMINISTRATOR'
WHERE u.email = 'ahmed.adeyemi@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Department manager and project management seed', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code IN ('MANAGER', 'PROJECT_MANAGEMENT')
WHERE u.email = 'matthew.lenoble@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Engineer role seed', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'ENGINEER'
WHERE u.email IN ('network.engineer@ussignal.com', 'systems.engineer@ussignal.com', 'collaboration.engineer@ussignal.com')
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Coordinator role seed', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'PROJECT_TEAM_COORDINATOR'
WHERE u.email = 'ptc.coordinator@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('015_department_manager_and_user_switcher', 'Add department manager structure and role validation seed users')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
