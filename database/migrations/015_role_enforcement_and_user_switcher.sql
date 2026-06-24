-- Project Pulse
-- Migration: 015_role_enforcement_and_user_switcher.sql
-- Purpose: Seed development user-switcher accounts and permissions so role enforcement can be validated by role.

BEGIN;

-- Permission used only for the development/test user switcher. This keeps the feature visible in the catalog
-- while still making it clear that production identity should come from Microsoft Entra ID.
INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('SWITCH_DEVELOPMENT_USER', 'Switch development user', 'admin', 'Use the local development user switcher to validate role-specific access before Entra ID is wired in.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

-- Administrators can switch users during development validation.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = 'SWITCH_DEVELOPMENT_USER'
WHERE r.role_code = 'ADMINISTRATOR'
ON CONFLICT DO NOTHING;

-- Development role personas used by the front-end user switcher.
INSERT INTO app_users (email, display_name, job_title, department, is_active)
VALUES
    ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Platform Administrator', 'Project Time Platform', TRUE),
    ('engineer.demo@ussignal.com', 'Engineer Demo User', 'Collaboration Engineer', 'Engineering', TRUE),
    ('manager.demo@ussignal.com', 'Manager Demo User', 'Engineering Manager', 'Engineering', TRUE),
    ('matthew.lenoble@ussignal.com', 'Matthew Lenoble', 'Project Management', 'Project Management Office', TRUE),
    ('coordinator.demo@ussignal.com', 'Project Team Coordinator Demo User', 'Project and Team Coordinator', 'Operations', TRUE)
ON CONFLICT (email) DO UPDATE
SET display_name = EXCLUDED.display_name,
    job_title = EXCLUDED.job_title,
    department = EXCLUDED.department,
    is_active = TRUE,
    updated_at = NOW();

-- Reset only the seeded demo persona roles so the switcher remains predictable.
UPDATE app_user_role_assignments ura
SET is_active = FALSE,
    updated_at = NOW()
FROM app_users u
WHERE u.user_id = ura.user_id
  AND u.email IN (
      'engineer.demo@ussignal.com',
      'manager.demo@ussignal.com',
      'matthew.lenoble@ussignal.com',
      'coordinator.demo@ussignal.com'
  );

-- Keep Ahmed as Administrator.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Development user switcher seed assignment', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'ADMINISTRATOR'
WHERE u.email = 'ahmed.adeyemi@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

-- Engineer persona.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Development user switcher seed assignment', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'ENGINEER'
WHERE u.email = 'engineer.demo@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

-- Manager persona.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Development user switcher seed assignment', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'MANAGER'
WHERE u.email = 'manager.demo@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

-- Project Management persona. Migration 014 consolidates PMO/Project Manager into PROJECT_MANAGEMENT.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Development user switcher seed assignment', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'PROJECT_MANAGEMENT'
WHERE u.email = 'matthew.lenoble@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

-- Project and Team Coordinator persona.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Development user switcher seed assignment', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'PROJECT_TEAM_COORDINATOR'
WHERE u.email = 'coordinator.demo@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

INSERT INTO app_feature_catalog (feature_code, feature_name, module_code, route_anchor, required_permission_code, feature_description, display_order, is_active)
VALUES
    ('USER_SWITCHER', 'Development User Switcher', 'admin', '#dashboard', 'SWITCH_DEVELOPMENT_USER', 'Switch between seeded development personas to validate role enforcement across modules.', 5, TRUE)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('015_role_enforcement_and_user_switcher', 'Seed development user-switcher personas and switcher permission')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
