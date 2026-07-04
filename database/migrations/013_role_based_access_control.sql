-- Project Health Dashboard
-- Migration: 013_role_based_access_control.sql
-- Purpose: Role-based access control foundation for role-specific pages, modules, and workflow capabilities.

BEGIN;

CREATE TABLE IF NOT EXISTS app_roles (
    app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_code VARCHAR(75) NOT NULL UNIQUE,
    role_name VARCHAR(150) NOT NULL,
    role_description TEXT,
    is_system_role BOOLEAN NOT NULL DEFAULT TRUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS app_permissions (
    app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    permission_name VARCHAR(200) NOT NULL,
    module_code VARCHAR(75) NOT NULL,
    permission_description TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS app_role_permissions (
    app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(app_role_id, app_permission_id)
);

CREATE TABLE IF NOT EXISTS app_user_role_assignments (
    app_user_role_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE CASCADE,
    assigned_by_user_id UUID REFERENCES app_users(user_id),
    assignment_reason TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, app_role_id)
);

CREATE TABLE IF NOT EXISTS app_feature_catalog (
    app_feature_catalog_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    feature_code VARCHAR(100) NOT NULL UNIQUE,
    feature_name VARCHAR(200) NOT NULL,
    module_code VARCHAR(75) NOT NULL,
    route_anchor VARCHAR(100),
    required_permission_code VARCHAR(100) REFERENCES app_permissions(permission_code),
    feature_description TEXT,
    display_order INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO app_roles (role_code, role_name, role_description, display_order)
VALUES
    ('ENGINEER', 'Engineer', 'Primarily enters time, views individual utilization, views holidays/calendar, manages personal preferences, and receives time submission reminders.', 10),
    ('PMO', 'PMO', 'Operational support role with time entry, individual utilization, read-only holidays, scheduling visibility, and personal preferences.', 20),
    ('MANAGER', 'Manager', 'Reviews and approves/rejects time, views team and individual utilization, views reports, and manages holiday calendar data.', 30),
    ('PROJECT_MANAGER', 'PM / Project Manager', 'Manages project intake, project/resource scheduling, expenses, project approvals, time rejection, and engineer/project associations.', 40),
    ('PROJECT_TEAM_COORDINATOR', 'Project and Team Coordinator', 'Coordinator/admin operations role with manager and PM visibility, reconciliation, audit, reporting, export, and historical time correction.', 50),
    ('ADMINISTRATOR', 'Administrator', 'Full system access across all modules, administration, configuration, reporting, and workflow actions.', 60)
ON CONFLICT (role_code) DO UPDATE
SET role_name = EXCLUDED.role_name,
    role_description = EXCLUDED.role_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_DASHBOARD', 'View dashboard', 'dashboard', 'Access the main Project Health Dashboard dashboard.'),
    ('VIEW_TIME_ENTRY', 'View time entry', 'time', 'View the time entry workspace.'),
    ('EDIT_OWN_TIME', 'Edit own time', 'time', 'Create and edit own draft/correction time entries.'),
    ('SUBMIT_OWN_TIME', 'Submit own time', 'time', 'Submit own time for approval.'),
    ('VIEW_OWN_UTILIZATION', 'View own utilization', 'utilization', 'View individual utilization metrics.'),
    ('VIEW_TEAM_UTILIZATION', 'View team utilization', 'utilization', 'View team utilization metrics.'),
    ('VIEW_INDIVIDUAL_UTILIZATION', 'View individual utilization by resource', 'utilization', 'Run utilization views for specific individuals.'),
    ('VIEW_HOLIDAYS', 'View holidays', 'calendar', 'View uploaded company holidays.'),
    ('MANAGE_HOLIDAYS', 'Manage holidays', 'calendar', 'Upload and maintain annual company holidays.'),
    ('VIEW_CALENDAR', 'View calendar schedule', 'calendar', 'View personal or team calendar/schedule.'),
    ('MANAGE_PERSONAL_PREFERENCES', 'Manage personal preferences', 'preferences', 'Set personalized time entry defaults and reminders.'),
    ('RECEIVE_TIME_REMINDERS', 'Receive time reminders', 'notifications', 'Receive weekly time submission reminders.'),
    ('APPROVE_TIME', 'Approve time', 'approval', 'Approve submitted time entries.'),
    ('REJECT_TIME', 'Reject time', 'approval', 'Reject/return submitted time entries.'),
    ('UNLOCK_TIME', 'Unlock time', 'approval', 'Unlock submitted time when permitted.'),
    ('EDIT_HISTORICAL_TIME', 'Edit historical time', 'time', 'Change older/locked time entries when authorized.'),
    ('VIEW_APPROVAL_INBOX', 'View approval inbox', 'approval', 'Access manager/project approval queue.'),
    ('VIEW_REPORTS', 'View reports', 'reports', 'View operational reports.'),
    ('VIEW_PROJECT_INTAKE', 'View project intake', 'projects', 'View project intake requests.'),
    ('MANAGE_PROJECT_INTAKE', 'Manage project intake', 'projects', 'Create, update, approve, or route project intake requests.'),
    ('VIEW_RESOURCE_SCHEDULING', 'View resource scheduling', 'resources', 'View resource schedules and assignments.'),
    ('MANAGE_RESOURCE_SCHEDULING', 'Manage resource scheduling', 'resources', 'Assign resources and manage capacity.'),
    ('VIEW_EXPENSES', 'View expense management', 'expenses', 'View expense reports.'),
    ('MANAGE_EXPENSES', 'Manage expense management', 'expenses', 'Create, review, approve, or reject expense reports.'),
    ('PROJECT_TIME_APPROVAL', 'Project time approval', 'projects', 'Validate project/task time allocation accuracy.'),
    ('MANAGE_PROJECT_ASSIGNMENTS', 'Manage project assignments', 'projects', 'Add and associate engineers to projects and service requests.'),
    ('VIEW_ACCOUNT_RECONCILIATION', 'View account reconciliation', 'accounting', 'View accounting reconciliation module.'),
    ('MANAGE_ACCOUNT_RECONCILIATION', 'Manage account reconciliation', 'accounting', 'Perform accounting reconciliation actions.'),
    ('VIEW_EXECUTIVE_REPORTING', 'View executive reporting', 'reporting', 'View executive reporting dashboards.'),
    ('VIEW_AUDIT_TRAIL', 'View audit trail', 'audit', 'View audit trail and workflow history.'),
    ('EXPORT_TIME_PDF', 'Export time to PDF', 'exports', 'Export time entries to PDF.'),
    ('EXPORT_TIME_EXCEL', 'Export time to Excel', 'exports', 'Export time entries to Excel.'),
    ('SYSTEM_ADMINISTRATION', 'System administration', 'admin', 'Access administrative configuration and user management.'),
    ('MANAGE_ALL', 'Manage all', 'admin', 'Full administrative access across all modules.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

-- Engineer / PMO permissions.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = ANY(ARRAY[
    'VIEW_DASHBOARD','VIEW_TIME_ENTRY','EDIT_OWN_TIME','SUBMIT_OWN_TIME','VIEW_OWN_UTILIZATION','VIEW_HOLIDAYS','VIEW_CALENDAR','MANAGE_PERSONAL_PREFERENCES','RECEIVE_TIME_REMINDERS'
])
WHERE r.role_code IN ('ENGINEER','PMO')
ON CONFLICT DO NOTHING;

-- Manager permissions.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = ANY(ARRAY[
    'VIEW_DASHBOARD','VIEW_TIME_ENTRY','EDIT_OWN_TIME','SUBMIT_OWN_TIME','VIEW_OWN_UTILIZATION','VIEW_HOLIDAYS','VIEW_CALENDAR','MANAGE_PERSONAL_PREFERENCES','RECEIVE_TIME_REMINDERS',
    'VIEW_APPROVAL_INBOX','APPROVE_TIME','REJECT_TIME','UNLOCK_TIME','VIEW_REPORTS','VIEW_TEAM_UTILIZATION','VIEW_INDIVIDUAL_UTILIZATION','MANAGE_HOLIDAYS'
])
WHERE r.role_code = 'MANAGER'
ON CONFLICT DO NOTHING;

-- Project Manager permissions.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = ANY(ARRAY[
    'VIEW_DASHBOARD','VIEW_TIME_ENTRY','EDIT_OWN_TIME','SUBMIT_OWN_TIME','VIEW_OWN_UTILIZATION','VIEW_HOLIDAYS','VIEW_CALENDAR','MANAGE_PERSONAL_PREFERENCES','RECEIVE_TIME_REMINDERS',
    'VIEW_PROJECT_INTAKE','MANAGE_PROJECT_INTAKE','VIEW_RESOURCE_SCHEDULING','MANAGE_RESOURCE_SCHEDULING','VIEW_EXPENSES','MANAGE_EXPENSES','PROJECT_TIME_APPROVAL','REJECT_TIME','MANAGE_PROJECT_ASSIGNMENTS','VIEW_REPORTS'
])
WHERE r.role_code = 'PROJECT_MANAGER'
ON CONFLICT DO NOTHING;

-- Project and Team Coordinator permissions.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = ANY(ARRAY[
    'VIEW_DASHBOARD','VIEW_TIME_ENTRY','EDIT_OWN_TIME','SUBMIT_OWN_TIME','VIEW_OWN_UTILIZATION','VIEW_HOLIDAYS','VIEW_CALENDAR','MANAGE_PERSONAL_PREFERENCES','RECEIVE_TIME_REMINDERS',
    'VIEW_APPROVAL_INBOX','APPROVE_TIME','REJECT_TIME','UNLOCK_TIME','VIEW_REPORTS','VIEW_TEAM_UTILIZATION','VIEW_INDIVIDUAL_UTILIZATION','MANAGE_HOLIDAYS',
    'VIEW_PROJECT_INTAKE','MANAGE_PROJECT_INTAKE','VIEW_RESOURCE_SCHEDULING','MANAGE_RESOURCE_SCHEDULING','VIEW_EXPENSES','MANAGE_EXPENSES','PROJECT_TIME_APPROVAL','MANAGE_PROJECT_ASSIGNMENTS',
    'VIEW_ACCOUNT_RECONCILIATION','MANAGE_ACCOUNT_RECONCILIATION','VIEW_EXECUTIVE_REPORTING','VIEW_AUDIT_TRAIL','EDIT_HISTORICAL_TIME','EXPORT_TIME_PDF','EXPORT_TIME_EXCEL','SYSTEM_ADMINISTRATION'
])
WHERE r.role_code = 'PROJECT_TEAM_COORDINATOR'
ON CONFLICT DO NOTHING;

-- Administrator gets everything.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
CROSS JOIN app_permissions p
WHERE r.role_code = 'ADMINISTRATOR'
ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog (feature_code, feature_name, module_code, route_anchor, required_permission_code, feature_description, display_order)
VALUES
    ('TIME_ENTRY', 'Time Entry', 'time', '#timesheet', 'VIEW_TIME_ENTRY', 'Weekly time entry and daily submission workflow.', 10),
    ('UTILIZATION_PERSONAL', 'My Utilization', 'utilization', '#utilization', 'VIEW_OWN_UTILIZATION', 'Individual utilization metrics.', 20),
    ('UTILIZATION_TEAM', 'Team Utilization', 'utilization', '#utilization', 'VIEW_TEAM_UTILIZATION', 'Team utilization and individual resource views.', 30),
    ('HOLIDAY_CALENDAR', 'Holiday Calendar', 'calendar', '#holiday-admin', 'VIEW_HOLIDAYS', 'Read-only uploaded holiday calendar.', 40),
    ('HOLIDAY_ADMIN', 'Holiday Administration', 'calendar', '#holiday-admin', 'MANAGE_HOLIDAYS', 'Upload and manage company holiday calendar.', 50),
    ('APPROVAL_INBOX', 'Approval Inbox', 'approval', '#manager-approval', 'VIEW_APPROVAL_INBOX', 'Approve or reject submitted time.', 60),
    ('PROJECT_INTAKE', 'Project Intake', 'projects', '#psa-modules', 'VIEW_PROJECT_INTAKE', 'Project request intake and templates.', 70),
    ('RESOURCE_SCHEDULING', 'Resource Scheduling', 'resources', '#psa-modules', 'VIEW_RESOURCE_SCHEDULING', 'Capacity and resource assignment views.', 80),
    ('EXPENSE_MANAGEMENT', 'Expense Management', 'expenses', '#psa-modules', 'VIEW_EXPENSES', 'Expense reporting and approval workflow.', 90),
    ('PROJECT_APPROVAL', 'Project Approval', 'projects', '#workflow', 'PROJECT_TIME_APPROVAL', 'Validate project/task allocation accuracy.', 100),
    ('ACCOUNT_RECONCILIATION', 'Account Reconciliation', 'accounting', '#workflow', 'VIEW_ACCOUNT_RECONCILIATION', 'Accounting reconciliation workflow.', 110),
    ('EXECUTIVE_REPORTING', 'Executive Reporting', 'reporting', '#psa-modules', 'VIEW_EXECUTIVE_REPORTING', 'Executive reports and dashboards.', 120),
    ('AUDIT_TRAIL', 'Audit Trail', 'audit', '#workflow', 'VIEW_AUDIT_TRAIL', 'View workflow and system audit history.', 130),
    ('EXPORTS', 'Exports', 'exports', '#workflow', 'EXPORT_TIME_EXCEL', 'Export time entries to PDF and Excel.', 140),
    ('ADMINISTRATION', 'Administration', 'admin', '#workflow', 'SYSTEM_ADMINISTRATION', 'System configuration and user administration.', 150)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Development assignments. Ahmed is seeded as Administrator so the current build can still validate all modules.
INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Development seed assignment', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'ADMINISTRATOR'
WHERE u.email = 'ahmed.adeyemi@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

INSERT INTO app_user_role_assignments (user_id, app_role_id, assignment_reason, is_active)
SELECT u.user_id, r.app_role_id, 'Development seed assignment', TRUE
FROM app_users u
JOIN app_roles r ON r.role_code = 'PROJECT_MANAGER'
WHERE u.email = 'matthew.lenoble@ussignal.com'
ON CONFLICT (user_id, app_role_id) DO UPDATE
SET is_active = TRUE,
    assignment_reason = EXCLUDED.assignment_reason,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('013_role_based_access_control', 'Add role-based access control foundation for role-specific pages and module permissions')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
