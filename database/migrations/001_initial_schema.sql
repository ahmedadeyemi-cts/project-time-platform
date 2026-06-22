-- Project Time Platform
-- Migration: 001_initial_schema.sql
-- Purpose: Create the initial PostgreSQL schema for users, roles, projects, time entry, approvals, accounting, utilization, notifications, and audit logs.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS schema_migrations (
    migration_id VARCHAR(100) PRIMARY KEY,
    description TEXT NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS app_users (
    user_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    entra_object_id VARCHAR(100) UNIQUE,
    email VARCHAR(255) NOT NULL UNIQUE,
    display_name VARCHAR(255) NOT NULL,
    employee_number VARCHAR(100),
    job_title VARCHAR(255),
    department VARCHAR(255),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS roles (
    role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_name VARCHAR(100) NOT NULL UNIQUE,
    role_description TEXT,
    is_system_role BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS user_roles (
    user_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    role_id UUID NOT NULL REFERENCES roles(role_id) ON DELETE CASCADE,
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE,
    assigned_by_user_id UUID REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_user_role_effective UNIQUE (user_id, role_id, effective_start_date),
    CONSTRAINT chk_user_role_dates CHECK (effective_end_date IS NULL OR effective_end_date >= effective_start_date)
);

CREATE TABLE IF NOT EXISTS teams (
    team_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    team_name VARCHAR(255) NOT NULL UNIQUE,
    team_description TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS team_memberships (
    team_membership_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    team_id UUID NOT NULL REFERENCES teams(team_id),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_team_membership_effective UNIQUE (team_id, user_id, effective_start_date),
    CONSTRAINT chk_team_membership_dates CHECK (effective_end_date IS NULL OR effective_end_date >= effective_start_date)
);

CREATE TABLE IF NOT EXISTS reporting_relationships (
    reporting_relationship_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_user_id UUID NOT NULL REFERENCES app_users(user_id),
    manager_user_id UUID REFERENCES app_users(user_id),
    team_lead_user_id UUID REFERENCES app_users(user_id),
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_reporting_relationship_effective UNIQUE (employee_user_id, effective_start_date),
    CONSTRAINT chk_reporting_relationship_dates CHECK (effective_end_date IS NULL OR effective_end_date >= effective_start_date),
    CONSTRAINT chk_reporting_relationship_not_self CHECK (
        employee_user_id <> COALESCE(manager_user_id, '00000000-0000-0000-0000-000000000000'::uuid)
        AND employee_user_id <> COALESCE(team_lead_user_id, '00000000-0000-0000-0000-000000000000'::uuid)
    )
);

CREATE TABLE IF NOT EXISTS clients (
    client_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    client_name VARCHAR(255) NOT NULL UNIQUE,
    client_code VARCHAR(100) UNIQUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS projects (
    project_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    client_id UUID REFERENCES clients(client_id),
    project_code VARCHAR(100) NOT NULL UNIQUE,
    project_name VARCHAR(255) NOT NULL,
    project_description TEXT,
    project_manager_user_id UUID REFERENCES app_users(user_id),
    status VARCHAR(50) NOT NULL DEFAULT 'active',
    start_date DATE,
    end_date DATE,
    billable BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_project_status CHECK (status IN ('draft', 'active', 'on_hold', 'completed', 'cancelled')),
    CONSTRAINT chk_project_dates CHECK (end_date IS NULL OR start_date IS NULL OR end_date >= start_date)
);

CREATE TABLE IF NOT EXISTS project_tasks (
    task_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    task_code VARCHAR(100) NOT NULL,
    task_name VARCHAR(255) NOT NULL,
    task_description TEXT,
    billable BOOLEAN NOT NULL DEFAULT TRUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_project_task_code UNIQUE (project_id, task_code)
);

CREATE TABLE IF NOT EXISTS project_assignments (
    project_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    task_id UUID REFERENCES project_tasks(task_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    assigned_by_user_id UUID REFERENCES app_users(user_id),
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE,
    allocation_percent NUMERIC(5,2),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_project_assignment_dates CHECK (effective_end_date IS NULL OR effective_end_date >= effective_start_date),
    CONSTRAINT chk_project_assignment_allocation CHECK (allocation_percent IS NULL OR allocation_percent BETWEEN 0 AND 100)
);

CREATE TABLE IF NOT EXISTS accounting_periods (
    accounting_period_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    period_name VARCHAR(50) NOT NULL UNIQUE,
    period_start_date DATE NOT NULL,
    period_end_date DATE NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'open',
    locked_by_user_id UUID REFERENCES app_users(user_id),
    locked_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_accounting_period_dates CHECK (period_end_date >= period_start_date),
    CONSTRAINT chk_accounting_period_status CHECK (status IN ('open', 'approval_in_progress', 'reconciliation_in_progress', 'locked', 'reopened'))
);

CREATE TABLE IF NOT EXISTS timesheets (
    timesheet_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    accounting_period_id UUID REFERENCES accounting_periods(accounting_period_id),
    week_start_date DATE NOT NULL,
    week_end_date DATE NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'draft',
    submitted_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_user_timesheet_week UNIQUE (user_id, week_start_date),
    CONSTRAINT chk_timesheet_dates CHECK (week_end_date >= week_start_date),
    CONSTRAINT chk_timesheet_status CHECK (status IN ('draft', 'submitted', 'manager_approved', 'manager_declined', 'pm_approved', 'pm_declined', 'accounting_ready', 'reconciled', 'locked'))
);

CREATE TABLE IF NOT EXISTS time_entries (
    time_entry_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    timesheet_id UUID NOT NULL REFERENCES timesheets(timesheet_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    project_id UUID NOT NULL REFERENCES projects(project_id),
    task_id UUID REFERENCES project_tasks(task_id),
    work_date DATE NOT NULL,
    hours NUMERIC(5,2) NOT NULL,
    description TEXT,
    billable BOOLEAN NOT NULL DEFAULT TRUE,
    status VARCHAR(50) NOT NULL DEFAULT 'draft',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_time_entry_hours CHECK (hours > 0 AND hours <= 24),
    CONSTRAINT chk_time_entry_status CHECK (status IN ('draft', 'submitted', 'manager_approved', 'manager_declined', 'pm_approved', 'pm_declined', 'accounting_ready', 'reconciled', 'locked'))
);

CREATE TABLE IF NOT EXISTS approval_records (
    approval_record_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    time_entry_id UUID NOT NULL REFERENCES time_entries(time_entry_id) ON DELETE CASCADE,
    approval_stage VARCHAR(50) NOT NULL,
    approval_status VARCHAR(50) NOT NULL,
    approver_user_id UUID NOT NULL REFERENCES app_users(user_id),
    decision_comment TEXT,
    decided_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_approval_stage CHECK (approval_stage IN ('manager', 'project_manager', 'accounting', 'override')),
    CONSTRAINT chk_approval_status CHECK (approval_status IN ('approved', 'declined', 'returned', 'reopened'))
);

CREATE TABLE IF NOT EXISTS accounting_reconciliations (
    reconciliation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    accounting_period_id UUID NOT NULL REFERENCES accounting_periods(accounting_period_id),
    project_id UUID REFERENCES projects(project_id),
    time_entry_id UUID REFERENCES time_entries(time_entry_id),
    reconciliation_status VARCHAR(50) NOT NULL DEFAULT 'pending',
    reconciled_by_user_id UUID REFERENCES app_users(user_id),
    reconciled_at TIMESTAMPTZ,
    reconciliation_notes TEXT,
    external_reference VARCHAR(255),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_reconciliation_status CHECK (reconciliation_status IN ('pending', 'reconciled', 'exception', 'excluded'))
);

CREATE TABLE IF NOT EXISTS utilization_snapshots (
    utilization_snapshot_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    period_type VARCHAR(50) NOT NULL,
    period_start_date DATE NOT NULL,
    period_end_date DATE NOT NULL,
    target_percent NUMERIC(5,2) NOT NULL DEFAULT 70.00,
    billable_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    total_available_hours NUMERIC(8,2) NOT NULL DEFAULT 0,
    utilization_percent NUMERIC(6,2) NOT NULL DEFAULT 0,
    next_target_percent NUMERIC(5,2),
    hours_needed_for_target NUMERIC(8,2),
    calculation_basis VARCHAR(50) NOT NULL DEFAULT 'approved',
    snapshot_created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_utilization_period_type CHECK (period_type IN ('monthly', 'quarterly')),
    CONSTRAINT chk_utilization_dates CHECK (period_end_date >= period_start_date),
    CONSTRAINT chk_utilization_basis CHECK (calculation_basis IN ('submitted', 'approved', 'reconciled'))
);

CREATE TABLE IF NOT EXISTS notification_preferences (
    notification_preference_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    notification_type VARCHAR(100) NOT NULL,
    email_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    in_app_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    opt_out_allowed BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_notification_preference UNIQUE (user_id, notification_type)
);

CREATE TABLE IF NOT EXISTS notification_log (
    notification_log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES app_users(user_id),
    notification_type VARCHAR(100) NOT NULL,
    recipient_email VARCHAR(255),
    subject VARCHAR(500),
    delivery_status VARCHAR(50) NOT NULL DEFAULT 'pending',
    sent_at TIMESTAMPTZ,
    error_message TEXT,
    related_entity_type VARCHAR(100),
    related_entity_id UUID,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_notification_delivery_status CHECK (delivery_status IN ('pending', 'sent', 'failed', 'suppressed'))
);

CREATE TABLE IF NOT EXISTS audit_logs (
    audit_log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actor_user_id UUID REFERENCES app_users(user_id),
    action VARCHAR(255) NOT NULL,
    entity_type VARCHAR(100) NOT NULL,
    entity_id UUID,
    old_value JSONB,
    new_value JSONB,
    ip_address INET,
    user_agent TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_app_users_updated_at BEFORE UPDATE ON app_users FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_roles_updated_at BEFORE UPDATE ON roles FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_teams_updated_at BEFORE UPDATE ON teams FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_clients_updated_at BEFORE UPDATE ON clients FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_projects_updated_at BEFORE UPDATE ON projects FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_project_tasks_updated_at BEFORE UPDATE ON project_tasks FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_accounting_periods_updated_at BEFORE UPDATE ON accounting_periods FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_timesheets_updated_at BEFORE UPDATE ON timesheets FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_time_entries_updated_at BEFORE UPDATE ON time_entries FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_accounting_reconciliations_updated_at BEFORE UPDATE ON accounting_reconciliations FOR EACH ROW EXECUTE FUNCTION set_updated_at();
CREATE TRIGGER trg_notification_preferences_updated_at BEFORE UPDATE ON notification_preferences FOR EACH ROW EXECUTE FUNCTION set_updated_at();

CREATE INDEX IF NOT EXISTS idx_app_users_email ON app_users(email);
CREATE INDEX IF NOT EXISTS idx_app_users_entra_object_id ON app_users(entra_object_id);
CREATE INDEX IF NOT EXISTS idx_user_roles_user_id ON user_roles(user_id);
CREATE INDEX IF NOT EXISTS idx_team_memberships_user_id ON team_memberships(user_id);
CREATE INDEX IF NOT EXISTS idx_reporting_relationships_employee ON reporting_relationships(employee_user_id);
CREATE INDEX IF NOT EXISTS idx_reporting_relationships_manager ON reporting_relationships(manager_user_id);
CREATE INDEX IF NOT EXISTS idx_projects_pm ON projects(project_manager_user_id);
CREATE INDEX IF NOT EXISTS idx_project_tasks_project ON project_tasks(project_id);
CREATE INDEX IF NOT EXISTS idx_project_assignments_user ON project_assignments(user_id);
CREATE INDEX IF NOT EXISTS idx_project_assignments_project ON project_assignments(project_id);
CREATE INDEX IF NOT EXISTS idx_timesheets_user_week ON timesheets(user_id, week_start_date);
CREATE INDEX IF NOT EXISTS idx_time_entries_user_date ON time_entries(user_id, work_date);
CREATE INDEX IF NOT EXISTS idx_time_entries_project ON time_entries(project_id);
CREATE INDEX IF NOT EXISTS idx_approval_records_time_entry ON approval_records(time_entry_id);
CREATE INDEX IF NOT EXISTS idx_accounting_reconciliations_period ON accounting_reconciliations(accounting_period_id);
CREATE INDEX IF NOT EXISTS idx_utilization_snapshots_user_period ON utilization_snapshots(user_id, period_type, period_start_date, period_end_date);
CREATE INDEX IF NOT EXISTS idx_notification_log_user ON notification_log(user_id);
CREATE INDEX IF NOT EXISTS idx_audit_logs_entity ON audit_logs(entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_audit_logs_actor ON audit_logs(actor_user_id);

INSERT INTO roles (role_name, role_description, is_system_role)
VALUES
    ('Engineer', 'Can enter time and view personal utilization.', TRUE),
    ('Team Lead', 'Can view assigned team members without approval authority.', TRUE),
    ('Manager', 'Can approve or decline direct report time.', TRUE),
    ('Project Manager', 'Can manage projects/tasks and approve project/task time for billing readiness.', TRUE),
    ('Accounting', 'Can reconcile approved time and support period close.', TRUE),
    ('Organizational Admin', 'Can view and manage organization-wide operational data.', TRUE),
    ('System Admin', 'Can configure platform settings, identity, and roles.', TRUE),
    ('Super Admin', 'Emergency full access with audit trail.', TRUE)
ON CONFLICT (role_name) DO NOTHING;

INSERT INTO schema_migrations (migration_id, description)
VALUES ('001_initial_schema', 'Initial schema for Project Time Platform')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
