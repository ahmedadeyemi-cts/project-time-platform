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

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    projectpulse_role_scope_rules,
    projectpulse_team_scope_assignments
TO "ptp_app";

COMMIT;
