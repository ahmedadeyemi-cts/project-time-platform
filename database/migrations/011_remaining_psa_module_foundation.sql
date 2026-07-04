-- Project Health Dashboard
-- Migration: 011_remaining_psa_module_foundation.sql
-- Purpose: Add foundation tables and seed data for remaining PSA sections.

BEGIN;

CREATE TABLE IF NOT EXISTS project_intake_requests (
    project_intake_request_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_number VARCHAR(50) NOT NULL UNIQUE,
    client_name VARCHAR(200) NOT NULL,
    opportunity_reference VARCHAR(100),
    request_title VARCHAR(255) NOT NULL,
    request_description TEXT,
    requested_by_user_id UUID REFERENCES app_users(user_id),
    assigned_pm_user_id UUID REFERENCES app_users(user_id),
    intake_status VARCHAR(50) NOT NULL DEFAULT 'new',
    priority VARCHAR(25) NOT NULL DEFAULT 'normal',
    target_start_date DATE,
    target_completion_date DATE,
    estimated_hours NUMERIC(10,2),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_templates (
    project_template_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    template_code VARCHAR(75) NOT NULL UNIQUE,
    template_name VARCHAR(255) NOT NULL,
    template_description TEXT,
    service_line VARCHAR(100),
    default_phase_count INTEGER NOT NULL DEFAULT 0,
    default_task_count INTEGER NOT NULL DEFAULT 0,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS project_milestones (
    project_milestone_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    milestone_name VARCHAR(255) NOT NULL,
    milestone_description TEXT,
    due_date DATE,
    milestone_status VARCHAR(50) NOT NULL DEFAULT 'not_started',
    display_order INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(project_id, milestone_name)
);

CREATE TABLE IF NOT EXISTS project_risks (
    project_risk_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE CASCADE,
    risk_title VARCHAR(255) NOT NULL,
    risk_description TEXT,
    probability VARCHAR(25) NOT NULL DEFAULT 'medium',
    impact VARCHAR(25) NOT NULL DEFAULT 'medium',
    risk_status VARCHAR(50) NOT NULL DEFAULT 'open',
    mitigation_plan TEXT,
    owner_user_id UUID REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS resource_capacity_plans (
    resource_capacity_plan_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    week_start_date DATE NOT NULL,
    available_hours NUMERIC(10,2) NOT NULL DEFAULT 40.00,
    assigned_hours NUMERIC(10,2) NOT NULL DEFAULT 0.00,
    planned_utilization_percent NUMERIC(7,2) NOT NULL DEFAULT 0.00,
    capacity_status VARCHAR(50) NOT NULL DEFAULT 'available',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, week_start_date)
);

CREATE TABLE IF NOT EXISTS expense_reports (
    expense_report_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    project_id UUID REFERENCES projects(project_id),
    report_number VARCHAR(50) NOT NULL UNIQUE,
    report_title VARCHAR(255) NOT NULL,
    report_status VARCHAR(50) NOT NULL DEFAULT 'draft',
    report_total NUMERIC(12,2) NOT NULL DEFAULT 0.00,
    submitted_at TIMESTAMPTZ,
    approved_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS expense_items (
    expense_item_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    expense_report_id UUID NOT NULL REFERENCES expense_reports(expense_report_id) ON DELETE CASCADE,
    expense_date DATE NOT NULL,
    expense_category VARCHAR(100) NOT NULL,
    merchant_name VARCHAR(255),
    amount NUMERIC(12,2) NOT NULL,
    description TEXT,
    reimbursable BOOLEAN NOT NULL DEFAULT TRUE,
    receipt_required BOOLEAN NOT NULL DEFAULT TRUE,
    receipt_attached BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS client_invoices (
    client_invoice_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_id UUID REFERENCES projects(project_id),
    invoice_number VARCHAR(50) NOT NULL UNIQUE,
    invoice_status VARCHAR(50) NOT NULL DEFAULT 'draft',
    billing_period_start DATE,
    billing_period_end DATE,
    labor_amount NUMERIC(12,2) NOT NULL DEFAULT 0.00,
    expense_amount NUMERIC(12,2) NOT NULL DEFAULT 0.00,
    invoice_total NUMERIC(12,2) NOT NULL DEFAULT 0.00,
    generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    approved_at TIMESTAMPTZ,
    exported_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS invoice_line_items (
    invoice_line_item_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    client_invoice_id UUID NOT NULL REFERENCES client_invoices(client_invoice_id) ON DELETE CASCADE,
    line_type VARCHAR(50) NOT NULL,
    line_description TEXT NOT NULL,
    quantity NUMERIC(12,2) NOT NULL DEFAULT 1.00,
    unit_rate NUMERIC(12,2) NOT NULL DEFAULT 0.00,
    line_total NUMERIC(12,2) NOT NULL DEFAULT 0.00,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS reporting_snapshots (
    reporting_snapshot_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    snapshot_date DATE NOT NULL,
    snapshot_type VARCHAR(75) NOT NULL,
    metric_name VARCHAR(150) NOT NULL,
    metric_value NUMERIC(14,2) NOT NULL DEFAULT 0.00,
    metric_unit VARCHAR(50),
    metric_context JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(snapshot_date, snapshot_type, metric_name)
);

-- Seed templates.
INSERT INTO project_templates (template_code, template_name, template_description, service_line, default_phase_count, default_task_count)
VALUES
    ('PSA-IMPLEMENTATION', 'PSA Platform Implementation', 'Standard internal PSA implementation plan covering intake, project management, scheduling, time, expense, invoicing, reporting, UAT, and go-live.', 'Professional Services', 7, 28),
    ('CLIENT-DELIVERY', 'Client Delivery Project', 'Reusable delivery template for client-facing implementation projects.', 'Professional Services', 5, 20),
    ('INTERNAL-AUTOMATION', 'Internal Automation Initiative', 'Internal workflow automation template with discovery, build, test, release, and support phases.', 'Operations', 5, 15)
ON CONFLICT (template_code) DO UPDATE
SET template_name = EXCLUDED.template_name,
    template_description = EXCLUDED.template_description,
    service_line = EXCLUDED.service_line,
    default_phase_count = EXCLUDED.default_phase_count,
    default_task_count = EXCLUDED.default_task_count,
    is_active = TRUE,
    updated_at = NOW();

-- Seed one intake request tied to the current PSA project.
INSERT INTO project_intake_requests (
    request_number,
    client_name,
    opportunity_reference,
    request_title,
    request_description,
    requested_by_user_id,
    assigned_pm_user_id,
    intake_status,
    priority,
    target_start_date,
    target_completion_date,
    estimated_hours
)
SELECT
    'INTAKE-2026-0001',
    'US Signal Internal',
    'USS-PSA-2026',
    'US Signal Professional Services Automation Platform',
    'Internal request to deliver the Project Health Dashboard PSA platform foundation and phased operational modules.',
    req.user_id,
    pm.user_id,
    'approved',
    'high',
    DATE '2026-06-21',
    DATE '2026-12-31',
    640.00
FROM app_users req
CROSS JOIN app_users pm
WHERE req.email = 'ahmed.adeyemi@ussignal.com'
  AND pm.email = 'matthew.lenoble@ussignal.com'
ON CONFLICT (request_number) DO UPDATE
SET client_name = EXCLUDED.client_name,
    opportunity_reference = EXCLUDED.opportunity_reference,
    request_title = EXCLUDED.request_title,
    request_description = EXCLUDED.request_description,
    requested_by_user_id = EXCLUDED.requested_by_user_id,
    assigned_pm_user_id = EXCLUDED.assigned_pm_user_id,
    intake_status = EXCLUDED.intake_status,
    priority = EXCLUDED.priority,
    target_start_date = EXCLUDED.target_start_date,
    target_completion_date = EXCLUDED.target_completion_date,
    estimated_hours = EXCLUDED.estimated_hours,
    updated_at = NOW();

-- Seed milestones for USS-PSA-2026.
INSERT INTO project_milestones (project_id, milestone_name, milestone_description, due_date, milestone_status, display_order)
SELECT p.project_id, seed.milestone_name, seed.milestone_description, seed.due_date, seed.milestone_status, seed.display_order
FROM projects p
CROSS JOIN (
    VALUES
        ('Foundation Complete', 'Infrastructure, database, API, frontend shell, and deployment foundation validated.', DATE '2026-08-01', 'in_progress', 10),
        ('Project Intake Ready', 'Project intake requests and template foundation ready for validation.', DATE '2026-08-29', 'not_started', 20),
        ('Project Management Ready', 'Milestones, risks, tasks, and workflow status views ready for validation.', DATE '2026-10-03', 'not_started', 30),
        ('Resource Scheduling Ready', 'Capacity and assignment visibility ready for validation.', DATE '2026-10-24', 'not_started', 40),
        ('Time and Expense Ready', 'Daily time workflow and expense capture ready for validation.', DATE '2026-11-21', 'in_progress', 50),
        ('Invoicing and Reporting Ready', 'Billing summaries, invoice staging, and executive dashboards ready for validation.', DATE '2026-12-12', 'not_started', 60),
        ('Go-Live Readiness', 'UAT, training, cutover checklist, and hypercare plan ready.', DATE '2026-12-31', 'not_started', 70)
) AS seed(milestone_name, milestone_description, due_date, milestone_status, display_order)
WHERE p.project_code = 'USS-PSA-2026'
ON CONFLICT (project_id, milestone_name) DO UPDATE
SET milestone_description = EXCLUDED.milestone_description,
    due_date = EXCLUDED.due_date,
    milestone_status = EXCLUDED.milestone_status,
    display_order = EXCLUDED.display_order,
    updated_at = NOW();

-- Seed risks.
INSERT INTO project_risks (project_id, risk_title, risk_description, probability, impact, risk_status, mitigation_plan, owner_user_id)
SELECT p.project_id, seed.risk_title, seed.risk_description, seed.probability, seed.impact, seed.risk_status, seed.mitigation_plan, owner.user_id
FROM projects p
CROSS JOIN app_users owner
CROSS JOIN (
    VALUES
        ('Approval workflow complexity', 'Daily submission, manager approval, PM validation, and accounting reconciliation may create edge cases if not tested by role.', 'medium', 'high', 'open', 'Validate one workflow at a time and keep full debug endpoints during development.'),
        ('Scope expansion', 'Remaining PSA modules may expand beyond the original minimum validation scope.', 'medium', 'medium', 'open', 'Use phased delivery gates and keep backlog items separated from validated functionality.'),
        ('Public validation exposure', 'Temporary public validation access must remain restricted to approved source IPs.', 'low', 'high', 'open', 'Expose only the frontend proxy and restrict source IP at OCI and OS firewall layers.')
) AS seed(risk_title, risk_description, probability, impact, risk_status, mitigation_plan)
WHERE p.project_code = 'USS-PSA-2026'
  AND owner.email = 'ahmed.adeyemi@ussignal.com'
  AND NOT EXISTS (
      SELECT 1 FROM project_risks existing
      WHERE existing.project_id = p.project_id
        AND existing.risk_title = seed.risk_title
  );

-- Seed capacity for Ahmed for validation weeks.
INSERT INTO resource_capacity_plans (user_id, week_start_date, available_hours, assigned_hours, planned_utilization_percent, capacity_status)
SELECT u.user_id, seed.week_start_date, 40.00, seed.assigned_hours, seed.planned_utilization_percent, seed.capacity_status
FROM app_users u
CROSS JOIN (
    VALUES
        (DATE '2026-06-21', 24.00, 60.00, 'available'),
        (DATE '2026-06-28', 32.00, 80.00, 'balanced'),
        (DATE '2026-07-05', 36.00, 90.00, 'near_capacity')
) AS seed(week_start_date, assigned_hours, planned_utilization_percent, capacity_status)
WHERE u.email = 'ahmed.adeyemi@ussignal.com'
ON CONFLICT (user_id, week_start_date) DO UPDATE
SET available_hours = EXCLUDED.available_hours,
    assigned_hours = EXCLUDED.assigned_hours,
    planned_utilization_percent = EXCLUDED.planned_utilization_percent,
    capacity_status = EXCLUDED.capacity_status,
    updated_at = NOW();

-- Seed expense report and item.
INSERT INTO expense_reports (user_id, project_id, report_number, report_title, report_status, report_total)
SELECT u.user_id, p.project_id, 'EXP-2026-0001', 'Project Health Dashboard validation expenses', 'draft', 0.00
FROM app_users u
CROSS JOIN projects p
WHERE u.email = 'ahmed.adeyemi@ussignal.com'
  AND p.project_code = 'USS-PSA-2026'
ON CONFLICT (report_number) DO UPDATE
SET user_id = EXCLUDED.user_id,
    project_id = EXCLUDED.project_id,
    report_title = EXCLUDED.report_title,
    report_status = EXCLUDED.report_status,
    updated_at = NOW();

-- Seed invoice staging.
INSERT INTO client_invoices (project_id, invoice_number, invoice_status, billing_period_start, billing_period_end, labor_amount, expense_amount, invoice_total)
SELECT p.project_id, 'INV-2026-0001', 'draft', DATE '2026-06-21', DATE '2026-06-27', 0.00, 0.00, 0.00
FROM projects p
WHERE p.project_code = 'USS-PSA-2026'
ON CONFLICT (invoice_number) DO UPDATE
SET project_id = EXCLUDED.project_id,
    invoice_status = EXCLUDED.invoice_status,
    billing_period_start = EXCLUDED.billing_period_start,
    billing_period_end = EXCLUDED.billing_period_end,
    labor_amount = EXCLUDED.labor_amount,
    expense_amount = EXCLUDED.expense_amount,
    invoice_total = EXCLUDED.invoice_total,
    updated_at = NOW();

-- Seed reporting snapshots.
INSERT INTO reporting_snapshots (snapshot_date, snapshot_type, metric_name, metric_value, metric_unit, metric_context)
VALUES
    (CURRENT_DATE, 'executive_dashboard', 'open_intake_requests', 1, 'count', '{"module":"project_intake"}'::jsonb),
    (CURRENT_DATE, 'executive_dashboard', 'active_projects', 1, 'count', '{"module":"project_management"}'::jsonb),
    (CURRENT_DATE, 'executive_dashboard', 'pending_manager_approvals', 0, 'count', '{"module":"time_approval"}'::jsonb),
    (CURRENT_DATE, 'executive_dashboard', 'draft_invoices', 1, 'count', '{"module":"invoicing"}'::jsonb)
ON CONFLICT (snapshot_date, snapshot_type, metric_name) DO UPDATE
SET metric_value = EXCLUDED.metric_value,
    metric_unit = EXCLUDED.metric_unit,
    metric_context = EXCLUDED.metric_context,
    created_at = NOW();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('011_remaining_psa_module_foundation', 'Add foundation tables and seed data for remaining PSA modules')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
