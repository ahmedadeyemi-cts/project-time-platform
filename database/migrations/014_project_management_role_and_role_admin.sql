-- Project Pulse
-- Migration: 014_project_management_role_and_role_admin.sql
-- Purpose: Consolidate PMO and PM/Project Manager into one Project Management role.

BEGIN;

-- Create the unified Project Management role.
INSERT INTO app_roles (role_code, role_name, role_description, display_order)
VALUES (
    'PROJECT_MANAGEMENT',
    'Project Management',
    'Project Management role with time entry, utilization, project intake, resource scheduling, expense management, project approval, time rejection, and engineer/project association access.',
    40
)
ON CONFLICT (role_code) DO UPDATE
SET role_name = EXCLUDED.role_name,
    role_description = EXCLUDED.role_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Copy permissions from both older PMO and PROJECT_MANAGER roles into PROJECT_MANAGEMENT.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT target_role.app_role_id, rp.app_permission_id
FROM app_roles source_role
INNER JOIN app_role_permissions rp ON rp.app_role_id = source_role.app_role_id
CROSS JOIN app_roles target_role
WHERE source_role.role_code IN ('PMO', 'PROJECT_MANAGER')
  AND target_role.role_code = 'PROJECT_MANAGEMENT'
ON CONFLICT DO NOTHING;

-- Ensure Project Management has the explicit permission set requested.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = ANY(ARRAY[
    'VIEW_DASHBOARD',
    'VIEW_TIME_ENTRY',
    'EDIT_OWN_TIME',
    'SUBMIT_OWN_TIME',
    'VIEW_OWN_UTILIZATION',
    'VIEW_HOLIDAYS',
    'VIEW_CALENDAR',
    'MANAGE_PERSONAL_PREFERENCES',
    'RECEIVE_TIME_REMINDERS',
    'VIEW_PROJECT_INTAKE',
    'MANAGE_PROJECT_INTAKE',
    'VIEW_RESOURCE_SCHEDULING',
    'MANAGE_RESOURCE_SCHEDULING',
    'VIEW_EXPENSES',
    'MANAGE_EXPENSES',
    'PROJECT_TIME_APPROVAL',
    'REJECT_TIME',
    'MANAGE_PROJECT_ASSIGNMENTS',
    'VIEW_REPORTS'
])
WHERE r.role_code = 'PROJECT_MANAGEMENT'
ON CONFLICT DO NOTHING;

-- Move active PMO and PROJECT_MANAGER user assignments to PROJECT_MANAGEMENT.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assigned_by_user_id, assignment_reason, is_active)
SELECT DISTINCT ura.user_id,
       target_role.app_role_id,
       ura.assigned_by_user_id,
       'Migrated from PMO/PROJECT_MANAGER to unified Project Management role',
       TRUE
FROM app_user_role_assignments ura
INNER JOIN app_roles source_role ON source_role.app_role_id = ura.app_role_id
CROSS JOIN app_roles target_role
WHERE source_role.role_code IN ('PMO', 'PROJECT_MANAGER')
  AND target_role.role_code = 'PROJECT_MANAGEMENT'
  AND ura.is_active = TRUE
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

-- Keep the old role records for audit/history, but deactivate them so they are not assignable going forward.
UPDATE app_roles
SET is_active = FALSE,
    role_description = COALESCE(role_description, '') || ' Deprecated: replaced by PROJECT_MANAGEMENT.',
    updated_at = NOW()
WHERE role_code IN ('PMO', 'PROJECT_MANAGER');

-- Ensure Matthew has the unified role in the development seed.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Development seed assignment for Project Management role', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'PROJECT_MANAGEMENT'
WHERE u.email = 'matthew.lenoble@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

-- Update feature descriptions/names that referred to PM/Project Manager language.
UPDATE app_feature_catalog
SET feature_description = REPLACE(feature_description, 'PM', 'Project Management'),
    updated_at = NOW()
WHERE feature_description ILIKE '%PM%';

INSERT INTO schema_migrations (migration_id, description)
VALUES ('014_project_management_role_and_role_admin', 'Consolidate PMO and Project Manager into unified Project Management role')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
