-- 030R-canonical-role-model-cleanup.sql
-- Purpose:
--   Canonicalize Project Health Dashboard roles into the approved production role model.
--   This migration is idempotent and safe to re-run.
--
-- Canonical roles:
--   ENGINEERING
--   PROJECT_MANAGEMENT
--   ENGINEERING_LEAD
--   PROJECT_MANAGEMENT_LEAD
--   MANAGER
--   SALES
--   INSIDE_SALES
--   SOLUTION_ARCHITECT
--   EXECUTIVE
--   PROJECT_TEAM_COORDINATOR
--   ACCOUNTING
--   SUPER_ADMINISTRATOR

BEGIN;

CREATE TEMP TABLE canonical_roles (
  role_code text PRIMARY KEY,
  role_name text NOT NULL,
  role_description text NOT NULL,
  display_order integer NOT NULL
) ON COMMIT DROP;

INSERT INTO canonical_roles(role_code, role_name, role_description, display_order)
VALUES
  ('ENGINEERING', 'Engineering', 'Assigned engineers. Own time, assigned projects, assigned tasks, assigned engineering documents, own utilization, and holidays view-only.', 10),
  ('PROJECT_MANAGEMENT', 'Project Management', 'Project managers. Managed project scope, project workload, assigned project reporting, and task assignment to engineers on managed projects.', 20),
  ('ENGINEERING_LEAD', 'Engineering Lead', 'Engineering team leads. Engineering access plus team/individual utilization and engineering/team reporting.', 30),
  ('PROJECT_MANAGEMENT_LEAD', 'Project Management Lead', 'Project management team leads. PM access plus PM team workload, project reporting, project closeout, and engineer task assignment for managed projects.', 40),
  ('MANAGER', 'Manager', 'People managers. Time approval for engineers, team reporting, engineering reports by team, team utilization, and approval inbox.', 50),
  ('SALES', 'Sales', 'Sales users. Customer-scoped reporting for their customers and sales-owned intake/handoff visibility.', 60),
  ('INSIDE_SALES', 'Inside Sales', 'Inside Sales users. All-customer reporting and quote association visibility from SELL and Salesforce.', 70),
  ('SOLUTION_ARCHITECT', 'Solution Architect', 'Solution Architects. Project status, reporting, cost overrun visibility, and SOW/GSD creation and review.', 80),
  ('EXECUTIVE', 'Executive', 'Executive users. High-level dashboard, reports, utilization, and organization-wide read-only performance visibility.', 90),
  ('PROJECT_TEAM_COORDINATOR', 'Project Team Coordinator', 'Project Team Coordinators. Broad project operations scope including intake, assignment coordination, billing/accounting coordination, reporting, workflow, export, and audit readiness.', 100),
  ('ACCOUNTING', 'Accounting', 'Accounting users. Accounting, reporting, export, reconciliation, invoicing, and billing visibility.', 110),
  ('SUPER_ADMINISTRATOR', 'Super Administrator', 'Full platform administrator with complete system, role, security, configuration, View-As, and module administration.', 120);

INSERT INTO app_roles (
  role_code,
  role_name,
  role_description,
  is_system_role,
  is_active,
  display_order,
  created_at,
  updated_at
)
SELECT
  role_code,
  role_name,
  role_description,
  TRUE,
  TRUE,
  display_order,
  NOW(),
  NOW()
FROM canonical_roles cr
WHERE NOT EXISTS (
  SELECT 1
  FROM app_roles ar
  WHERE ar.role_code = cr.role_code
);

UPDATE app_roles ar
SET
  role_name = cr.role_name,
  role_description = cr.role_description,
  is_system_role = TRUE,
  is_active = TRUE,
  display_order = cr.display_order,
  updated_at = NOW()
FROM canonical_roles cr
WHERE ar.role_code = cr.role_code;

CREATE TEMP TABLE permission_sources (
  target_role_code text NOT NULL,
  source_role_code text NOT NULL
) ON COMMIT DROP;

INSERT INTO permission_sources(target_role_code, source_role_code)
VALUES
  ('ENGINEERING', 'ENGINEER'),
  ('ENGINEERING_LEAD', 'ENGINEER'),
  ('ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD'),
  ('PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT'),
  ('PROJECT_MANAGEMENT', 'PROJECT_MANAGER'),
  ('PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT'),
  ('PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGER'),
  ('PROJECT_MANAGEMENT_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD'),
  ('MANAGER', 'MANAGER'),
  ('PROJECT_TEAM_COORDINATOR', 'PROJECT_TEAM_COORDINATOR'),
  ('ACCOUNTING', 'ACCOUNTING'),
  ('EXECUTIVE', 'EXECUTIVE'),
  ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR');

CREATE TEMP TABLE manual_permissions (
  target_role_code text NOT NULL,
  permission_code text NOT NULL
) ON COMMIT DROP;

INSERT INTO manual_permissions(target_role_code, permission_code)
VALUES
  ('SALES', 'VIEW_DASHBOARD'),
  ('SALES', 'VIEW_REPORTS'),
  ('SALES', 'VIEW_CUSTOMERS'),
  ('SALES', 'VIEW_PROJECT_INTAKE'),
  ('SALES', 'VIEW_PROJECT_WORKSPACE'),

  ('INSIDE_SALES', 'VIEW_DASHBOARD'),
  ('INSIDE_SALES', 'VIEW_REPORTS'),
  ('INSIDE_SALES', 'VIEW_CUSTOMERS'),
  ('INSIDE_SALES', 'VIEW_PROJECT_INTAKE'),
  ('INSIDE_SALES', 'VIEW_PROJECT_WORKSPACE'),
  ('INSIDE_SALES', 'VIEW_RESOURCE_ASSIGNMENT_HANDOFF'),

  ('SOLUTION_ARCHITECT', 'VIEW_DASHBOARD'),
  ('SOLUTION_ARCHITECT', 'VIEW_REPORTS'),
  ('SOLUTION_ARCHITECT', 'VIEW_CUSTOMERS'),
  ('SOLUTION_ARCHITECT', 'VIEW_PROJECT_INTAKE'),
  ('SOLUTION_ARCHITECT', 'VIEW_PROJECT_WORKSPACE'),
  ('SOLUTION_ARCHITECT', 'VIEW_COST_ALERTS'),
  ('SOLUTION_ARCHITECT', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'),
  ('SOLUTION_ARCHITECT', 'VIEW_INTAKE_WORK_TASK_HANDOFF'),
  ('SOLUTION_ARCHITECT', 'VIEW_RESOURCE_ASSIGNMENT_HANDOFF'),
  ('SOLUTION_ARCHITECT', 'MANAGE_PROJECT_DOCUMENTS'),
  ('SOLUTION_ARCHITECT', 'MANAGE_PROJECT_INTAKE');

CREATE TEMP TABLE planned_permissions AS
SELECT DISTINCT
  ps.target_role_code,
  p.permission_code
FROM permission_sources ps
JOIN app_roles source_role
  ON source_role.role_code = ps.source_role_code
JOIN app_role_permissions rp
  ON rp.app_role_id = source_role.app_role_id
JOIN app_permissions p
  ON p.app_permission_id = rp.app_permission_id
UNION
SELECT DISTINCT
  mp.target_role_code,
  mp.permission_code
FROM manual_permissions mp;

INSERT INTO app_role_permissions (
  app_role_id,
  app_permission_id
)
SELECT DISTINCT
  target_role.app_role_id,
  p.app_permission_id
FROM planned_permissions pp
JOIN app_roles target_role
  ON target_role.role_code = pp.target_role_code
JOIN app_permissions p
  ON p.permission_code = pp.permission_code
WHERE NOT EXISTS (
  SELECT 1
  FROM app_role_permissions existing
  WHERE existing.app_role_id = target_role.app_role_id
    AND existing.app_permission_id = p.app_permission_id
);

CREATE TEMP TABLE role_assignment_map (
  old_role_code text NOT NULL,
  target_role_code text
) ON COMMIT DROP;

INSERT INTO role_assignment_map(old_role_code, target_role_code)
VALUES
  ('ENGINEER', 'ENGINEERING'),
  ('ENGINEERING_TEAM_LEAD', 'ENGINEERING_LEAD'),
  ('PROJECT_MANAGEMENT', 'PROJECT_MANAGEMENT'),
  ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT'),
  ('PROJECT_MANAGEMENT_TEAM_LEAD', 'PROJECT_MANAGEMENT_LEAD'),
  ('MANAGER', 'MANAGER'),
  ('PROJECT_TEAM_COORDINATOR', 'PROJECT_TEAM_COORDINATOR'),
  ('ACCOUNTING', 'ACCOUNTING'),
  ('ADMINISTRATOR', 'SUPER_ADMINISTRATOR'),
  ('EXECUTIVE', 'EXECUTIVE'),
  ('PMO', NULL);

INSERT INTO app_user_role_assignments (
  user_id,
  app_role_id,
  assignment_reason,
  is_active,
  assigned_at,
  updated_at
)
SELECT DISTINCT
  ura.user_id,
  target_role.app_role_id,
  '030R canonical role assignment from ' || old_role.role_code,
  TRUE,
  NOW(),
  NOW()
FROM app_user_role_assignments ura
JOIN app_roles old_role
  ON old_role.app_role_id = ura.app_role_id
JOIN role_assignment_map ram
  ON ram.old_role_code = old_role.role_code
JOIN app_roles target_role
  ON target_role.role_code = ram.target_role_code
JOIN app_users u
  ON u.user_id = ura.user_id
WHERE ura.is_active = TRUE
  AND u.is_active = TRUE
  AND ram.target_role_code IS NOT NULL
  AND NOT EXISTS (
    SELECT 1
    FROM app_user_role_assignments existing
    WHERE existing.user_id = ura.user_id
      AND existing.app_role_id = target_role.app_role_id
      AND existing.is_active = TRUE
  );

INSERT INTO app_user_role_assignments (
  user_id,
  app_role_id,
  assignment_reason,
  is_active,
  assigned_at,
  updated_at
)
SELECT
  u.user_id,
  r.app_role_id,
  '030R explicit Project Team Coordinator assignment',
  TRUE,
  NOW(),
  NOW()
FROM app_users u
JOIN app_roles r
  ON r.role_code = 'PROJECT_TEAM_COORDINATOR'
WHERE lower(u.email) = 'project.team.coordinator@ussignal.local'
  AND u.is_active = TRUE
  AND NOT EXISTS (
    SELECT 1
    FROM app_user_role_assignments existing
    WHERE existing.user_id = u.user_id
      AND existing.app_role_id = r.app_role_id
      AND existing.is_active = TRUE
  );

UPDATE app_users
SET
  is_active = FALSE,
  login_enabled = FALSE,
  updated_at = NOW()
WHERE lower(email) IN (
  'demo.engineer@ussignal.local',
  'demo.manager@ussignal.local'
);

CREATE TEMP TABLE role_array_map (
  old_role_code text PRIMARY KEY,
  target_role_code text
) ON COMMIT DROP;

INSERT INTO role_array_map(old_role_code, target_role_code)
VALUES
  ('ENGINEER', 'ENGINEERING'),
  ('ENGINEERING_TEAM_LEAD', 'ENGINEERING_LEAD'),
  ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT'),
  ('PROJECT_MANAGEMENT_TEAM_LEAD', 'PROJECT_MANAGEMENT_LEAD'),
  ('ADMINISTRATOR', 'SUPER_ADMINISTRATOR'),
  ('PMO', NULL);

UPDATE dashboard_module_visibility_expectations d
SET
  allowed_roles = ARRAY(
    SELECT DISTINCT COALESCE(ram.target_role_code, role_value)
    FROM unnest(d.allowed_roles) AS role_value
    LEFT JOIN role_array_map ram
      ON ram.old_role_code = role_value
    WHERE COALESCE(ram.target_role_code, role_value) IS NOT NULL
    ORDER BY 1
  ),
  updated_at = NOW()
WHERE d.allowed_roles && ARRAY[
  'ENGINEER',
  'ENGINEERING_TEAM_LEAD',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'ADMINISTRATOR',
  'PMO'
];

UPDATE route_permission_contracts r
SET
  allowed_roles = ARRAY(
    SELECT DISTINCT COALESCE(ram.target_role_code, role_value)
    FROM unnest(r.allowed_roles) AS role_value
    LEFT JOIN role_array_map ram
      ON ram.old_role_code = role_value
    WHERE COALESCE(ram.target_role_code, role_value) IS NOT NULL
    ORDER BY 1
  ),
  restricted_roles = ARRAY(
    SELECT DISTINCT COALESCE(ram.target_role_code, role_value)
    FROM unnest(r.restricted_roles) AS role_value
    LEFT JOIN role_array_map ram
      ON ram.old_role_code = role_value
    WHERE COALESCE(ram.target_role_code, role_value) IS NOT NULL
    ORDER BY 1
  ),
  updated_at = NOW()
WHERE r.allowed_roles && ARRAY[
  'ENGINEER',
  'ENGINEERING_TEAM_LEAD',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'ADMINISTRATOR',
  'PMO'
]
OR r.restricted_roles && ARRAY[
  'ENGINEER',
  'ENGINEERING_TEAM_LEAD',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'ADMINISTRATOR',
  'PMO'
];

CREATE TEMP TABLE canonical_scope_rules (
  role_code text PRIMARY KEY,
  default_scope text NOT NULL,
  can_view_assigned_self boolean NOT NULL,
  can_view_managed_projects boolean NOT NULL,
  can_view_team_scope boolean NOT NULL,
  can_view_org_scope boolean NOT NULL,
  can_approve_time boolean NOT NULL,
  can_approve_password_reset boolean NOT NULL,
  can_coordinate_billing_expense boolean NOT NULL,
  can_manage_role_assignments_limited boolean NOT NULL,
  notes text NOT NULL
) ON COMMIT DROP;

INSERT INTO canonical_scope_rules
VALUES
  ('ENGINEERING', 'assigned_self', TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, 'Engineering sees own time and assigned project/workspace scope.'),
  ('PROJECT_MANAGEMENT', 'managed_projects', FALSE, TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, 'Project Management sees managed project scope and can manage project work within assignment boundaries.'),
  ('ENGINEERING_LEAD', 'team_scope_no_approval', TRUE, FALSE, TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, 'Engineering Lead has Engineering access plus team utilization and engineering reports.'),
  ('PROJECT_MANAGEMENT_LEAD', 'pm_team_scope_no_approval', FALSE, TRUE, TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, 'Project Management Lead sees PM team workload, managed projects, reports, and project closeout/task assignment controls.'),
  ('MANAGER', 'team_scope', FALSE, FALSE, TRUE, FALSE, TRUE, FALSE, FALSE, FALSE, 'Manager approves team time and runs team engineering reports.'),
  ('SALES', 'customer_owned_scope', FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, 'Sales sees reports for their customers.'),
  ('INSIDE_SALES', 'all_customer_sales_scope', FALSE, FALSE, FALSE, TRUE, FALSE, FALSE, FALSE, FALSE, 'Inside Sales sees all-customer reporting and quote association visibility.'),
  ('SOLUTION_ARCHITECT', 'scope_review_scope', FALSE, TRUE, FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, 'Solution Architect sees project status, cost overrun visibility, and SOW/GSD review/create context.'),
  ('EXECUTIVE', 'organization_read_scope', FALSE, FALSE, TRUE, TRUE, FALSE, FALSE, FALSE, FALSE, 'Executive sees high-level organization reporting and utilization.'),
  ('PROJECT_TEAM_COORDINATOR', 'operations_broad_scope', FALSE, TRUE, TRUE, TRUE, FALSE, FALSE, TRUE, TRUE, 'Project Team Coordinator has broad project operations, billing, reporting, workflow, export, and assignment coordination scope.'),
  ('ACCOUNTING', 'accounting_reporting_scope', FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, TRUE, FALSE, 'Accounting sees accounting, reporting, reconciliation, invoicing, and export scope.'),
  ('SUPER_ADMINISTRATOR', 'full_system_scope', TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, TRUE, 'Super Administrator has full platform control.');

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
  notes,
  created_at,
  updated_at
)
SELECT
  csr.role_code,
  csr.default_scope,
  csr.can_view_assigned_self,
  csr.can_view_managed_projects,
  csr.can_view_team_scope,
  csr.can_view_org_scope,
  csr.can_approve_time,
  csr.can_approve_password_reset,
  csr.can_coordinate_billing_expense,
  csr.can_manage_role_assignments_limited,
  csr.notes,
  NOW(),
  NOW()
FROM canonical_scope_rules csr
WHERE NOT EXISTS (
  SELECT 1
  FROM projectpulse_role_scope_rules existing
  WHERE existing.role_code = csr.role_code
);

UPDATE projectpulse_role_scope_rules prs
SET
  default_scope = csr.default_scope,
  can_view_assigned_self = csr.can_view_assigned_self,
  can_view_managed_projects = csr.can_view_managed_projects,
  can_view_team_scope = csr.can_view_team_scope,
  can_view_org_scope = csr.can_view_org_scope,
  can_approve_time = csr.can_approve_time,
  can_approve_password_reset = csr.can_approve_password_reset,
  can_coordinate_billing_expense = csr.can_coordinate_billing_expense,
  can_manage_role_assignments_limited = csr.can_manage_role_assignments_limited,
  notes = csr.notes,
  updated_at = NOW()
FROM canonical_scope_rules csr
WHERE prs.role_code = csr.role_code;

UPDATE app_user_role_assignments ura
SET
  is_active = FALSE,
  updated_at = NOW()
FROM app_roles old_role
WHERE ura.app_role_id = old_role.app_role_id
  AND ura.is_active = TRUE
  AND old_role.role_code IN (
    'ENGINEER',
    'ENGINEERING_TEAM_LEAD',
    'PROJECT_MANAGER',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'ADMINISTRATOR',
    'PMO'
  )
  AND (
    old_role.role_code = 'PMO'
    OR EXISTS (
      SELECT 1
      FROM app_user_role_assignments new_ura
      JOIN app_roles new_role
        ON new_role.app_role_id = new_ura.app_role_id
      WHERE new_ura.user_id = ura.user_id
        AND new_ura.is_active = TRUE
        AND new_role.is_active = TRUE
        AND new_role.role_code = CASE old_role.role_code
          WHEN 'ENGINEER' THEN 'ENGINEERING'
          WHEN 'ENGINEERING_TEAM_LEAD' THEN 'ENGINEERING_LEAD'
          WHEN 'PROJECT_MANAGER' THEN 'PROJECT_MANAGEMENT'
          WHEN 'PROJECT_MANAGEMENT_TEAM_LEAD' THEN 'PROJECT_MANAGEMENT_LEAD'
          WHEN 'ADMINISTRATOR' THEN 'SUPER_ADMINISTRATOR'
          ELSE NULL
        END
    )
  );

UPDATE app_roles
SET
  is_active = FALSE,
  role_description =
    COALESCE(NULLIF(role_description, ''), role_name)
    || CASE
         WHEN role_description ILIKE '%Retired by canonical role cleanup%' THEN ''
         ELSE ' Retired by canonical role cleanup after migration to the approved 12-role model.'
       END,
  updated_at = NOW()
WHERE role_code IN (
  'ENGINEER',
  'ENGINEERING_TEAM_LEAD',
  'PROJECT_MANAGER',
  'PROJECT_MANAGEMENT_TEAM_LEAD',
  'ADMINISTRATOR',
  'PMO'
);

COMMIT;
