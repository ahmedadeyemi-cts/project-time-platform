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
    is_active = TRUE;

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_ASSIGNED_PROJECT_SCOPE', 'View assigned project scope', 'projects', 'Engineer-level view of assigned projects, tasks, documents, and resource requests.'),
    ('VIEW_MANAGED_PROJECT_SCOPE', 'View managed project scope', 'projects', 'PM-level view of projects and intakes managed by the user.'),
    ('VIEW_TEAM_PROJECT_SCOPE', 'View team project scope', 'projects', 'Lead/manager-level view of team projects, resources, and assignments.'),
    ('VIEW_TEAM_UTILIZATION', 'View team utilization', 'utilization', 'View team and individual utilization for assigned team scope.'),
    ('VIEW_ORGANIZATION_UTILIZATION', 'View organization utilization', 'reporting', 'View utilization across the organization, teams, managers, and individuals.'),
    ('VIEW_ALL_MANAGER_RESOURCE_SCOPE', 'View all manager resource scope', 'reporting', 'Executive-level visibility across managers, teams, and resources.'),
    ('MANAGE_PROJECT_COORDINATION', 'Manage project coordination', 'projects', 'Coordinate intake, project operations, billing readiness, reporting, and assignments.'),
    ('MANAGE_ROLE_ASSIGNMENTS_LIMITED', 'Manage role assignments limited', 'security', 'Coordinator-level role assignment workflow without full administrator control.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

CREATE TABLE IF NOT EXISTS projectpulse_role_scope_rules (
    role_scope_rule_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    role_code varchar(120) NOT NULL UNIQUE,
    default_scope varchar(80) NOT NULL,
    can_view_assigned_self boolean NOT NULL DEFAULT false,
    can_view_managed_projects boolean NOT NULL DEFAULT false,
    can_view_team_scope boolean NOT NULL DEFAULT false,
    can_view_org_scope boolean NOT NULL DEFAULT false,
    can_approve_time boolean NOT NULL DEFAULT false,
    can_approve_password_reset boolean NOT NULL DEFAULT false,
    can_coordinate_billing_expense boolean NOT NULL DEFAULT false,
    can_manage_role_assignments_limited boolean NOT NULL DEFAULT false,
    notes text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

INSERT INTO projectpulse_role_scope_rules (
    role_code,
    default_scope,
    can_view_assigned_self,
    can_view_managed_projects,
    can_view_team_scope,
    can_view_org_scope,
    can_approve_time,
    can_approve_password_reset,
    can_coordinate_billing_expense,
    can_manage_role_assignments_limited,
    notes
)
VALUES
    ('ENGINEER', 'assigned_self', TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE,
     'Engineer sees only projects, tasks, documents, resource requests, and assignments tied directly to their user ID.'),
    ('PROJECT_MANAGEMENT', 'managed_projects', FALSE, TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE,
     'PM sees only projects they manage and intakes/resource requests assigned to them or tied to their projects.'),
    ('MANAGER', 'team_scope', FALSE, FALSE, TRUE, FALSE, TRUE, TRUE, FALSE, FALSE,
     'Manager sees reporting/team scope and keeps approval authority.'),
    ('ENGINEERING_TEAM_LEAD', 'team_scope_no_approval', FALSE, FALSE, TRUE, FALSE, FALSE, FALSE, FALSE, FALSE,
     'Engineering Lead sees assigned engineering team, team utilization, and individual utilization, without time/password approval.'),
    ('PROJECT_MANAGEMENT_TEAM_LEAD', 'pm_team_scope_no_approval', FALSE, TRUE, TRUE, FALSE, FALSE, FALSE, FALSE, FALSE,
     'PM Lead sees PM team workload and project/resource coordination, without time/password approval.'),
    ('PROJECT_TEAM_COORDINATOR', 'operations_broad_scope', FALSE, TRUE, TRUE, TRUE, FALSE, FALSE, TRUE, TRUE,
     'Project Team Coordinator has broad project operations scope including accounting, billing, expenses, reporting, role coordination, and assignment coordination.'),
    ('EXECUTIVE', 'organization_read_scope', FALSE, FALSE, TRUE, TRUE, FALSE, FALSE, FALSE, FALSE,
     'Executive sees all managers, teams, resources, utilization, and reporting at organization/team/manager/individual levels.'),
    ('ADMINISTRATOR', 'full_system_scope', TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE,
     'Administrator has full platform control.')
ON CONFLICT (role_code) DO UPDATE
SET default_scope = EXCLUDED.default_scope,
    can_view_assigned_self = EXCLUDED.can_view_assigned_self,
    can_view_managed_projects = EXCLUDED.can_view_managed_projects,
    can_view_team_scope = EXCLUDED.can_view_team_scope,
    can_view_org_scope = EXCLUDED.can_view_org_scope,
    can_approve_time = EXCLUDED.can_approve_time,
    can_approve_password_reset = EXCLUDED.can_approve_password_reset,
    can_coordinate_billing_expense = EXCLUDED.can_coordinate_billing_expense,
    can_manage_role_assignments_limited = EXCLUDED.can_manage_role_assignments_limited,
    notes = EXCLUDED.notes,
    updated_at = now();

CREATE TABLE IF NOT EXISTS projectpulse_team_scope_assignments (
    team_scope_assignment_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    scoped_user_id uuid NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    scope_type varchar(80) NOT NULL,
    team_name text NULL,
    department_name text NULL,
    manager_user_id uuid NULL REFERENCES app_users(user_id),
    is_active boolean NOT NULL DEFAULT true,
    notes text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_projectpulse_team_scope_type
        CHECK (scope_type IN ('engineering_team_lead', 'project_management_team_lead', 'manager_team', 'coordinator_scope', 'executive_scope'))
);

CREATE INDEX IF NOT EXISTS idx_projectpulse_team_scope_user
ON projectpulse_team_scope_assignments(scoped_user_id, is_active);

CREATE INDEX IF NOT EXISTS idx_projectpulse_team_scope_team
ON projectpulse_team_scope_assignments(scope_type, team_name, department_name, is_active);

-- Role permission mapping.

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
ON CONFLICT DO NOTHING;

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
ON CONFLICT DO NOTHING;

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
ON CONFLICT DO NOTHING;

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
ON CONFLICT DO NOTHING;

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
ON CONFLICT DO NOTHING;

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
ON CONFLICT DO NOTHING;

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
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON TRUE
WHERE r.role_code = 'ADMINISTRATOR'
ON CONFLICT DO NOTHING;

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    projectpulse_role_scope_rules,
    projectpulse_team_scope_assignments
TO "ptp_app";

COMMIT;
