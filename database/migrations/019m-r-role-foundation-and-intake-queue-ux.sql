-- 019M-R Role Foundation + Intake Queue/Search UX
-- Adds lead/executive role foundation and role-scope permissions for future backend enforcement.

BEGIN;

INSERT INTO app_roles (role_code, role_name, is_active)
VALUES
    ('ENGINEER', 'Engineer', TRUE),
    ('MANAGER', 'Manager', TRUE),
    ('PROJECT_MANAGEMENT', 'Project Management', TRUE),
    ('ENGINEERING_TEAM_LEAD', 'Engineering Team Lead', TRUE),
    ('PROJECT_MANAGEMENT_TEAM_LEAD', 'Project Management Team Lead', TRUE),
    ('PROJECT_TEAM_COORDINATOR', 'Project Team Coordinator', TRUE),
    ('ADMINISTRATOR', 'Administrator', TRUE),
    ('EXECUTIVE', 'Executive', TRUE)
ON CONFLICT (role_code) DO UPDATE
SET role_name = EXCLUDED.role_name,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_ASSIGNED_PROJECT_SCOPE', 'View assigned project scope', 'projects', 'View only projects, tasks, documents, and assignments directly assigned to the user.'),
    ('VIEW_MANAGED_PROJECT_SCOPE', 'View managed project scope', 'projects', 'View only projects where the user is the assigned project manager.'),
    ('VIEW_TEAM_PROJECT_SCOPE', 'View team project scope', 'projects', 'View projects and assignments for users on the lead or manager team.'),
    ('VIEW_TEAM_UTILIZATION', 'View team utilization', 'utilization', 'View utilization for team members and individual users within the team scope.'),
    ('VIEW_ORGANIZATION_UTILIZATION', 'View organization utilization', 'reporting', 'View organization-wide utilization by organization, team, manager, and individual.'),
    ('VIEW_ALL_MANAGER_RESOURCE_SCOPE', 'View all manager resource scope', 'reporting', 'View all managers, resources, and team structures for executive reporting.'),
    ('MANAGE_PROJECT_COORDINATION', 'Manage project coordination', 'projects', 'Coordinate intake, assignments, billing readiness, reporting, and operational project workflows.'),
    ('MANAGE_ROLE_ASSIGNMENTS_LIMITED', 'Manage role assignments limited', 'security', 'Manage role assignment workflows without full administrator system control.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

-- Administrator: full platform control where permissions exist.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON TRUE
WHERE r.role_code = 'ADMINISTRATOR'
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

-- Engineer: own assigned project scope only.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_TIME_ENTRY',
    'VIEW_ASSIGNED_PROJECT_SCOPE',
    'VIEW_PROJECT_WORKSPACE',
    'VIEW_ENGINEERING_PROJECT_DOCUMENTS'
)
WHERE r.role_code = 'ENGINEER'
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

-- Project Management: managed projects/intakes only.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_PROJECT_INTAKE',
    'MANAGE_PROJECT_INTAKE',
    'VIEW_ENGINEERING_RESOURCE_REQUESTS',
    'MANAGE_ENGINEERING_RESOURCE_REQUESTS',
    'VIEW_PROJECT_WORKSPACE',
    'VIEW_MANAGED_PROJECT_SCOPE',
    'VIEW_REPORTS'
)
WHERE r.role_code = 'PROJECT_MANAGEMENT'
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

-- Engineering Team Lead: team visibility, assignments, utilization. No time-entry approval and no password-reset approval.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_PROJECT_WORKSPACE',
    'VIEW_ENGINEERING_PROJECT_DOCUMENTS',
    'VIEW_RESOURCE_SCHEDULING',
    'VIEW_ENGINEERING_RESOURCE_REQUESTS',
    'MANAGE_ENGINEERING_RESOURCE_REQUESTS',
    'VIEW_TEAM_PROJECT_SCOPE',
    'VIEW_TEAM_UTILIZATION',
    'VIEW_REPORTS'
)
WHERE r.role_code = 'ENGINEERING_TEAM_LEAD'
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

-- Project Management Team Lead: PM team/project visibility and coordination. No time-entry approval and no password-reset approval.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_PROJECT_INTAKE',
    'MANAGE_PROJECT_INTAKE',
    'VIEW_ENGINEERING_RESOURCE_REQUESTS',
    'MANAGE_ENGINEERING_RESOURCE_REQUESTS',
    'VIEW_PROJECT_WORKSPACE',
    'VIEW_TEAM_PROJECT_SCOPE',
    'VIEW_TEAM_UTILIZATION',
    'VIEW_REPORTS'
)
WHERE r.role_code = 'PROJECT_MANAGEMENT_TEAM_LEAD'
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

-- Manager: team/manager visibility plus approvals where existing permissions already allow it.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_PROJECT_WORKSPACE',
    'VIEW_RESOURCE_SCHEDULING',
    'VIEW_ENGINEERING_RESOURCE_REQUESTS',
    'VIEW_TEAM_PROJECT_SCOPE',
    'VIEW_TEAM_UTILIZATION',
    'VIEW_REPORTS',
    'VIEW_AUDIT_TRAIL'
)
WHERE r.role_code = 'MANAGER'
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

-- Project Team Coordinator: broader operational coordination across PM, accounting, billing, expenses, reports, and role coordination.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_PROJECT_INTAKE',
    'MANAGE_PROJECT_INTAKE',
    'VIEW_ENGINEERING_RESOURCE_REQUESTS',
    'MANAGE_ENGINEERING_RESOURCE_REQUESTS',
    'VIEW_PROJECT_WORKSPACE',
    'VIEW_RESOURCE_SCHEDULING',
    'MANAGE_RESOURCE_SCHEDULING',
    'VIEW_EXPENSES',
    'MANAGE_EXPENSES',
    'VIEW_REPORTS',
    'VIEW_EXECUTIVE_REPORTING',
    'VIEW_AUDIT_TRAIL',
    'MANAGE_PROJECT_COORDINATION',
    'MANAGE_ROLE_ASSIGNMENTS_LIMITED',
    'VIEW_TEAM_UTILIZATION',
    'VIEW_ORGANIZATION_UTILIZATION'
)
WHERE r.role_code = 'PROJECT_TEAM_COORDINATOR'
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

-- Executive: view all managers/resources/teams and utilization/reporting, no operational write authority by default.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code IN (
    'VIEW_PROJECT_WORKSPACE',
    'VIEW_RESOURCE_SCHEDULING',
    'VIEW_REPORTS',
    'VIEW_EXECUTIVE_REPORTING',
    'VIEW_ORGANIZATION_UTILIZATION',
    'VIEW_ALL_MANAGER_RESOURCE_SCOPE'
)
WHERE r.role_code = 'EXECUTIVE'
  AND NOT EXISTS (
      SELECT 1
      FROM app_role_permissions existing
      WHERE existing.app_role_id = r.app_role_id
        AND existing.app_permission_id = p.app_permission_id
  );

DO $$
BEGIN
    IF to_regclass('public.schema_migrations') IS NOT NULL THEN
        INSERT INTO schema_migrations (migration_id, description)
        VALUES ('019m_r_role_foundation_and_intake_queue_ux', 'Add lead/executive role foundation and intake queue UX support')
        ON CONFLICT (migration_id) DO UPDATE
        SET description = EXCLUDED.description,
            applied_at = NOW();
    END IF;
END $$;

COMMIT;
