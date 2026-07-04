CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS reporting_period_presets (
    period_preset_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    preset_key TEXT NOT NULL UNIQUE,
    preset_name TEXT NOT NULL,
    date_basis_options TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_data_domains (
    data_domain_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    domain_key TEXT NOT NULL UNIQUE,
    domain_name TEXT NOT NULL,
    domain_description TEXT NOT NULL DEFAULT '',
    primary_audience TEXT NOT NULL DEFAULT '',
    operational_owner TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_filter_catalog (
    filter_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    filter_key TEXT NOT NULL UNIQUE,
    filter_name TEXT NOT NULL,
    filter_group TEXT NOT NULL,
    applies_to_domains TEXT NOT NULL DEFAULT '',
    data_type TEXT NOT NULL DEFAULT 'text',
    supported_operators TEXT NOT NULL DEFAULT 'equals,contains,in',
    role_scope_notes TEXT NOT NULL DEFAULT '',
    sort_order INT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_output_column_catalog (
    output_column_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    column_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    report_domain TEXT NOT NULL DEFAULT '',
    source_hint TEXT NOT NULL DEFAULT '',
    sample_invoice_column BOOLEAN NOT NULL DEFAULT FALSE,
    accounting_column BOOLEAN NOT NULL DEFAULT FALSE,
    time_entry_column BOOLEAN NOT NULL DEFAULT FALSE,
    system_health_column BOOLEAN NOT NULL DEFAULT FALSE,
    external_connection_column BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_templates (
    template_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    template_key TEXT NOT NULL UNIQUE,
    template_name TEXT NOT NULL,
    category TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    default_period TEXT NOT NULL DEFAULT 'date_range',
    default_grouping TEXT NOT NULL DEFAULT '',
    default_filters JSONB NOT NULL DEFAULT '{}'::jsonb,
    output_columns TEXT NOT NULL DEFAULT '',
    export_formats TEXT NOT NULL DEFAULT 'preview,csv,xlsx,pdf',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_saved_report_definitions (
    saved_report_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    report_reference TEXT NOT NULL UNIQUE,
    report_name TEXT NOT NULL,
    report_type TEXT NOT NULL,
    audience TEXT NOT NULL DEFAULT '',
    owner_role TEXT NOT NULL DEFAULT '',
    criteria_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    selected_columns TEXT NOT NULL DEFAULT '',
    cadence TEXT NOT NULL DEFAULT 'on_demand',
    export_format TEXT NOT NULL DEFAULT 'preview',
    readiness_status TEXT NOT NULL DEFAULT 'draft',
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_execution_events (
    execution_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    execution_reference TEXT NOT NULL UNIQUE,
    report_reference TEXT NOT NULL DEFAULT '',
    report_type TEXT NOT NULL DEFAULT '',
    requested_by TEXT NOT NULL DEFAULT '',
    requested_role TEXT NOT NULL DEFAULT '',
    criteria_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    result_status TEXT NOT NULL DEFAULT 'preview_generated',
    row_count INT NOT NULL DEFAULT 0,
    total_hours NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    exception_count INT NOT NULL DEFAULT 0,
    generated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_invoice_schema_columns (
    invoice_schema_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    column_order INT NOT NULL,
    column_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    required_for_invoice BOOLEAN NOT NULL DEFAULT FALSE,
    data_type TEXT NOT NULL DEFAULT 'text',
    validation_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_external_connection_catalog (
    connection_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    connection_key TEXT NOT NULL UNIQUE,
    connection_name TEXT NOT NULL,
    connection_type TEXT NOT NULL,
    provider_category TEXT NOT NULL,
    operational_owner TEXT NOT NULL DEFAULT '',
    status_report_required BOOLEAN NOT NULL DEFAULT TRUE,
    last_check_status TEXT NOT NULL DEFAULT 'not_checked',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_api_status_catalog (
    api_status_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    api_key TEXT NOT NULL UNIQUE,
    api_name TEXT NOT NULL,
    api_path TEXT NOT NULL DEFAULT '',
    owning_module TEXT NOT NULL DEFAULT '',
    status_report_required BOOLEAN NOT NULL DEFAULT TRUE,
    expected_success_code TEXT NOT NULL DEFAULT '200',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_system_health_catalog (
    system_health_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    component_key TEXT NOT NULL UNIQUE,
    component_name TEXT NOT NULL,
    component_type TEXT NOT NULL,
    health_dimension TEXT NOT NULL,
    reporting_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_role_visibility_rules (
    role_visibility_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_key TEXT NOT NULL UNIQUE,
    role_name TEXT NOT NULL,
    allowed_report_categories TEXT NOT NULL DEFAULT '',
    restricted_report_categories TEXT NOT NULL DEFAULT '',
    export_allowed BOOLEAN NOT NULL DEFAULT FALSE,
    accounting_visibility BOOLEAN NOT NULL DEFAULT FALSE,
    system_health_visibility BOOLEAN NOT NULL DEFAULT FALSE,
    external_connection_visibility BOOLEAN NOT NULL DEFAULT FALSE,
    criteria_scope_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS reporting_readiness_reviews (
    readiness_review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    review_reference TEXT NOT NULL DEFAULT '030_reporting_accounting_invoicing_analytics',
    criteria_builder_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    time_reporting_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    accounting_invoicing_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    customer_project_pm_engineer_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    team_organization_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    system_api_external_auth_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    export_center_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    role_visibility_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    closeout_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    readiness_status TEXT NOT NULL DEFAULT 'pending',
    reviewer TEXT NOT NULL DEFAULT 'system',
    review_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO reporting_period_presets (preset_key, preset_name, date_basis_options, description)
VALUES
('daily', 'Daily', 'entry_date,submitted_date,approved_date,invoice_date,exported_date', 'Single-day reporting for operational and accounting review.'),
('weekly', 'Weekly', 'entry_date,submitted_date,approved_date,invoice_date,exported_date', 'Weekly time, approval, invoice, and utilization reporting.'),
('monthly', 'Monthly', 'entry_date,submitted_date,approved_date,invoice_date,exported_date', 'Monthly operational, billing, and customer reporting.'),
('date_range', 'Custom Date Range', 'entry_date,submitted_date,approved_date,invoice_date,exported_date', 'Custom from/to date range for any report type.'),
('year_to_date', 'Year to Date', 'entry_date,submitted_date,approved_date,invoice_date,exported_date', 'YTD reporting across time, invoice, customer, project, and system activity.'),
('fiscal_period', 'Fiscal Period', 'entry_date,submitted_date,approved_date,invoice_date,exported_date', 'Fiscal period reporting for accounting and leadership.')
ON CONFLICT (preset_key) DO UPDATE
SET preset_name = EXCLUDED.preset_name,
    date_basis_options = EXCLUDED.date_basis_options,
    description = EXCLUDED.description;

INSERT INTO reporting_data_domains (domain_key, domain_name, domain_description, primary_audience, operational_owner)
VALUES
('time_entries', 'Time Entry Reporting', 'All time entered, submitted, approved, rejected, returned, missing, billable, non-billable, AI-assisted, and SOW-aligned time.', 'Engineer, PM, Manager, PTC, Accounting, Executive', 'PTC'),
('accounting_invoicing', 'Accounting and Invoicing Reporting', 'Invoice-ready time, uninvoiced time, CP invoice number, PO/quote, rate, amount, work code, work location, billing exceptions.', 'Accounting, PTC, Executive', 'Accounting'),
('customer', 'Customer Reporting', 'Customer, customer group, engagement, project, contract type, invoice status, billable hours, open billing exposure.', 'PTC, Accounting, Executive, Sales', 'PTC'),
('project', 'Project Reporting', 'Project status, PM, customer, SOW/GSD, handoff, assignment, project workload, time, and billing readiness.', 'PM, PTC, Executive', 'PM'),
('pm', 'PM Reporting', 'PM assignment, project workload, PM validation backlog, returned/rejected time, billing readiness, handoff gaps.', 'PM, PM Team Lead, PTC, Executive', 'PM Team Lead'),
('engineer', 'Engineer Reporting', 'Engineer, selected engineers, utilization, submitted time, missing time, billable/non-billable, project allocation, AI-assisted entries.', 'Engineer, Manager, Engineering Team Lead, PTC', 'Engineering Team Lead'),
('team_organization', 'Team and Organization Reporting', 'Team, department, organization-wide utilization, workload, billable/non-billable rollups, approval backlog.', 'Manager, Team Lead, PTC, Executive', 'Leadership'),
('workflow_audit', 'Workflow / Approval / Audit Reporting', 'Approval backlog, View-As audit, handoff audit, assignment audit, notification audit, UAT evidence audit.', 'PTC, Admin, Executive', 'Admin'),
('system_stability', 'System Stability Reporting', 'Frontend, API, database, nginx, service, validation, uptime, and production readiness indicators.', 'Admin, Executive', 'Admin'),
('api_status', 'API Status Reporting', 'Authentication, navigation, dashboard, notifications, email provider, recipient safety, readiness, CRM, SOW/AI APIs.', 'Admin, Executive', 'Admin'),
('external_connections', 'External Connection Reporting', 'CRM, Salesforce, Zendesk Sell, Claude, Azure, SSO/Auth, Brevo, recipient safety, and future connectors.', 'Admin, PTC, Executive', 'Admin'),
('authentication_security', 'Authentication / Security Reporting', 'SSO, session_required events, role checks, View-As, forbidden writes, admin/system access readiness.', 'Admin, Executive', 'Admin'),
('ai_sow_scope', 'AI / SOW Scope Reporting', 'AI draft generation, SOW/GSD alignment, engineer acceptance, scope status, audit trail.', 'Engineer, PM, PTC, Admin', 'PTC'),
('notifications', 'Notification Reporting', 'Production notifications, acknowledgments, email provider delivery, recipient safety.', 'Admin, PTC, Executive', 'Admin'),
('uat', 'UAT Validation Reporting', 'Role validation, workflow scenarios, evidence capture, readiness, approval/export/audit checks.', 'Admin, PTC, Executive', 'Admin'),
('report_library', 'Report Library', 'Saved report definitions, cadence, audience, ownership, export format, and readiness.', 'All scoped roles', 'PTC')
ON CONFLICT (domain_key) DO UPDATE
SET domain_name = EXCLUDED.domain_name,
    domain_description = EXCLUDED.domain_description,
    primary_audience = EXCLUDED.primary_audience,
    operational_owner = EXCLUDED.operational_owner;

INSERT INTO reporting_filter_catalog (filter_key, filter_name, filter_group, applies_to_domains, data_type, supported_operators, role_scope_notes, sort_order)
VALUES
('report_type', 'Report Type', 'Core Criteria', 'all', 'list', 'equals', 'Every report must have a type.', 10),
('date_basis', 'Date Basis', 'Date Criteria', 'all', 'list', 'equals', 'Entry, submitted, approved, exported, or invoice date.', 20),
('period_preset', 'Period Preset', 'Date Criteria', 'all', 'list', 'equals', 'Daily, weekly, monthly, date range, YTD, fiscal period.', 30),
('start_date', 'Start Date', 'Date Criteria', 'all', 'date', 'greater_than_or_equal', 'Required for date range.', 40),
('end_date', 'End Date', 'Date Criteria', 'all', 'date', 'less_than_or_equal', 'Required for date range.', 50),
('customer', 'Customer', 'Customer Criteria', 'customer,project,time_entries,accounting_invoicing', 'text', 'equals,contains,in', 'Scoped by role/customer access.', 60),
('project', 'Project', 'Project Criteria', 'project,time_entries,accounting_invoicing,pm,engineer', 'text', 'equals,contains,in', 'PM sees assigned projects only.', 70),
('pm', 'Project Manager', 'People Criteria', 'pm,project,time_entries,accounting_invoicing', 'text', 'equals,contains,in', 'PM report can be scoped to one or more PMs.', 80),
('engineer', 'Engineer', 'People Criteria', 'engineer,time_entries,project,team_organization', 'text', 'equals,contains,in', 'Engineer sees own records only unless elevated role.', 90),
('engineer_set', 'Selected Engineers', 'People Criteria', 'engineer,time_entries,team_organization', 'text', 'in', 'Manager/team lead/PTC/Admin scoped selection.', 100),
('team', 'Team', 'Organization Criteria', 'team_organization,engineer,time_entries', 'text', 'equals,in', 'Manager/team lead scoped by team.', 110),
('organization', 'Organization', 'Organization Criteria', 'team_organization,customer,project,accounting_invoicing', 'text', 'equals,in', 'Executive/PTC/Admin organization-wide reports.', 120),
('contract_type', 'Contract Type', 'Accounting Criteria', 'accounting_invoicing,customer,project', 'list', 'equals,in', 'T&M, fixed fee, service request, managed service, project.', 130),
('time_entry_status', 'Time Entry Status', 'Time Criteria', 'time_entries,accounting_invoicing,workflow_audit', 'list', 'equals,in', 'Draft, submitted, approved, rejected, returned, exported, invoiced.', 140),
('approval_status', 'Approval Status', 'Workflow Criteria', 'workflow_audit,time_entries,accounting_invoicing', 'list', 'equals,in', 'Manager/PM approval status.', 150),
('invoice_status', 'Invoice Status', 'Accounting Criteria', 'accounting_invoicing,customer,project', 'list', 'equals,in', 'Invoice-ready, invoiced, not invoiced, exception, credit.', 160),
('work_code', 'Work Code', 'Accounting Criteria', 'accounting_invoicing,time_entries', 'text', 'equals,in', 'Consulting, configuration, project management, support.', 170),
('work_location', 'Work Location', 'Accounting Criteria', 'accounting_invoicing,time_entries', 'text', 'equals,in', 'Default work location, undefined, remote, onsite.', 180),
('external_connection', 'External Connection', 'Connection Criteria', 'external_connections,system_stability', 'list', 'equals,in', 'Salesforce, Zendesk Sell, Claude, Azure, Brevo, SSO/Auth.', 190),
('api_area', 'API Area', 'System Criteria', 'api_status,system_stability,authentication_security', 'list', 'equals,in', 'API endpoint group or module API.', 200),
('auth_event', 'Authentication Event', 'Security Criteria', 'authentication_security,workflow_audit', 'list', 'equals,in', 'SSO login, session_required, forbidden, View-As.', 210),
('system_component', 'System Component', 'System Criteria', 'system_stability,api_status', 'list', 'equals,in', 'Frontend, API, database, nginx, background service.', 220),
('export_format', 'Export Format', 'Output Criteria', 'all', 'list', 'equals', 'Preview, CSV, XLSX, PDF, invoice extract.', 230),
('group_by', 'Group By', 'Output Criteria', 'all', 'list', 'equals', 'Customer, project, PM, engineer, team, organization, invoice, status.', 240),
('include_exceptions', 'Include Exceptions', 'Output Criteria', 'all', 'boolean', 'equals', 'Include missing values, rejected time, billing exceptions, API failures.', 250)
ON CONFLICT (filter_key) DO UPDATE
SET filter_name = EXCLUDED.filter_name,
    filter_group = EXCLUDED.filter_group,
    applies_to_domains = EXCLUDED.applies_to_domains,
    data_type = EXCLUDED.data_type,
    supported_operators = EXCLUDED.supported_operators,
    role_scope_notes = EXCLUDED.role_scope_notes,
    sort_order = EXCLUDED.sort_order;

INSERT INTO reporting_invoice_schema_columns (column_order, column_key, display_name, required_for_invoice, data_type, validation_notes)
VALUES
(1, 'engagement_manager', 'Engagement Manager', TRUE, 'text', 'Maps to delivery/accounting owner.'),
(2, 'customer', 'Customer', TRUE, 'text', 'Customer name used for billing grouping.'),
(3, 'engagement', 'Engagement', TRUE, 'text', 'Engagement or project description.'),
(4, 'contract_type', 'Contract Type', TRUE, 'text', 'T&M, fixed fee, service request, managed service, project.'),
(5, 'po_quote', 'PO / Quote', TRUE, 'text', 'PO, quote, or service request identifier.'),
(6, 'invoicing_instructions', 'Invoicing Instructions', FALSE, 'text', 'Special billing instructions.'),
(7, 'cp_invoice_number', 'CP Invoice Number', TRUE, 'text', 'Invoice number such as CP-2026-00816.'),
(8, 'invoice_date', 'Invoice Date', TRUE, 'date', 'Invoice date for accounting reporting.'),
(9, 'category', 'Category', TRUE, 'text', 'Regular Time, OverTime, travel, adjustment, credit.'),
(10, 'item_description', 'Item Description', TRUE, 'text', 'Description from approved time or billing item.'),
(11, 'quantity_hours_entered', 'Quantity / Hours Entered', TRUE, 'numeric', 'Approved billable hours or invoice quantity.'),
(12, 'rate', 'Rate', TRUE, 'currency', 'Billing rate.'),
(13, 'amount', 'Amount', TRUE, 'currency', 'Quantity multiplied by rate; supports credits.'),
(14, 'work_code', 'Work Code', TRUE, 'text', 'Consulting, configuration, support, PM.'),
(15, 'work_location', 'Work Location', FALSE, 'text', 'Default work location, undefined, onsite, remote.'),
(16, 'total_invoiced_amount', 'Total Invoiced Amount', TRUE, 'currency', 'Invoice total rollup.')
ON CONFLICT (column_key) DO UPDATE
SET column_order = EXCLUDED.column_order,
    display_name = EXCLUDED.display_name,
    required_for_invoice = EXCLUDED.required_for_invoice,
    data_type = EXCLUDED.data_type,
    validation_notes = EXCLUDED.validation_notes;

INSERT INTO reporting_output_column_catalog (
    column_key,
    display_name,
    report_domain,
    source_hint,
    sample_invoice_column,
    accounting_column,
    time_entry_column,
    system_health_column,
    external_connection_column
)
SELECT column_key, display_name, 'accounting_invoicing', 'sample_invoice_schema', TRUE, TRUE, FALSE, FALSE, FALSE
FROM reporting_invoice_schema_columns
ON CONFLICT (column_key) DO UPDATE
SET display_name = EXCLUDED.display_name,
    report_domain = EXCLUDED.report_domain,
    source_hint = EXCLUDED.source_hint,
    sample_invoice_column = EXCLUDED.sample_invoice_column,
    accounting_column = EXCLUDED.accounting_column;

INSERT INTO reporting_output_column_catalog (
    column_key,
    display_name,
    report_domain,
    source_hint,
    accounting_column,
    time_entry_column,
    system_health_column,
    external_connection_column
)
VALUES
('time_entry_id', 'Time Entry ID', 'time_entries', 'time entry record', FALSE, TRUE, FALSE, FALSE),
('entry_date', 'Entry Date', 'time_entries', 'time entry record', FALSE, TRUE, FALSE, FALSE),
('submitted_date', 'Submitted Date', 'time_entries', 'time entry workflow', FALSE, TRUE, FALSE, FALSE),
('approved_date', 'Approved Date', 'time_entries', 'approval workflow', FALSE, TRUE, FALSE, FALSE),
('exported_date', 'Exported Date', 'accounting_invoicing', 'export workflow', TRUE, TRUE, FALSE, FALSE),
('engineer_name', 'Engineer Name', 'time_entries', 'user/team records', FALSE, TRUE, FALSE, FALSE),
('pm_name', 'PM Name', 'project', 'project assignment', FALSE, TRUE, FALSE, FALSE),
('team_name', 'Team Name', 'team_organization', 'team records', FALSE, TRUE, FALSE, FALSE),
('organization_name', 'Organization', 'team_organization', 'organization records', FALSE, TRUE, FALSE, FALSE),
('billable_status', 'Billable Status', 'time_entries', 'time entry billing flag', TRUE, TRUE, FALSE, FALSE),
('approval_status', 'Approval Status', 'workflow_audit', 'approval workflow', FALSE, TRUE, FALSE, FALSE),
('invoice_status', 'Invoice Status', 'accounting_invoicing', 'invoice workflow', TRUE, TRUE, FALSE, FALSE),
('ai_assisted', 'AI Assisted', 'ai_sow_scope', 'AI draft audit', FALSE, TRUE, FALSE, FALSE),
('sow_alignment_status', 'SOW Alignment Status', 'ai_sow_scope', 'SOW/GSD scope checker', FALSE, TRUE, FALSE, FALSE),
('api_name', 'API Name', 'api_status', 'API catalog', FALSE, FALSE, TRUE, FALSE),
('api_path', 'API Path', 'api_status', 'API catalog', FALSE, FALSE, TRUE, FALSE),
('http_status', 'HTTP Status', 'api_status', 'API response', FALSE, FALSE, TRUE, FALSE),
('component_name', 'Component Name', 'system_stability', 'system health catalog', FALSE, FALSE, TRUE, FALSE),
('component_status', 'Component Status', 'system_stability', 'service status', FALSE, FALSE, TRUE, FALSE),
('connection_name', 'Connection Name', 'external_connections', 'external connection catalog', FALSE, FALSE, FALSE, TRUE),
('connection_status', 'Connection Status', 'external_connections', 'connector readiness', FALSE, FALSE, FALSE, TRUE),
('last_check', 'Last Check', 'external_connections', 'connector readiness', FALSE, FALSE, FALSE, TRUE),
('auth_event_type', 'Authentication Event Type', 'authentication_security', 'auth/session audit', FALSE, FALSE, TRUE, FALSE),
('actor_role', 'Actor Role', 'workflow_audit', 'role/access audit', FALSE, FALSE, TRUE, FALSE),
('view_as_target', 'View-As Target', 'workflow_audit', 'View-As audit', FALSE, FALSE, TRUE, FALSE),
('forbidden_write_result', 'Forbidden Write Result', 'workflow_audit', 'View-As enforcement', FALSE, FALSE, TRUE, FALSE)
ON CONFLICT (column_key) DO UPDATE
SET display_name = EXCLUDED.display_name,
    report_domain = EXCLUDED.report_domain,
    source_hint = EXCLUDED.source_hint,
    accounting_column = EXCLUDED.accounting_column,
    time_entry_column = EXCLUDED.time_entry_column,
    system_health_column = EXCLUDED.system_health_column,
    external_connection_column = EXCLUDED.external_connection_column;

INSERT INTO reporting_templates (
    template_key,
    template_name,
    category,
    description,
    default_period,
    default_grouping,
    output_columns
)
VALUES
('time_entry_detail', 'Time Entry Detail Report', 'Time Entries', 'All time entered with status, project, customer, engineer, PM, billable, AI, and approval fields.', 'date_range', 'engineer,project,status', 'time_entry_id,entry_date,customer,project,pm_name,engineer_name,quantity_hours_entered,billable_status,approval_status,invoice_status,ai_assisted,sow_alignment_status'),
('accounting_invoice_detail', 'Accounting Invoice Detail Report', 'Accounting / Invoicing', 'Invoice-style report matching accounting export fields and billing exception review.', 'monthly', 'customer,invoice,project', 'engagement_manager,customer,engagement,contract_type,po_quote,invoicing_instructions,cp_invoice_number,invoice_date,category,item_description,quantity_hours_entered,rate,amount,work_code,work_location,total_invoiced_amount'),
('customer_billing_summary', 'Customer Billing Summary Report', 'Customer', 'Customer-level billable hours, invoiced amount, open billing exposure, exceptions, and project rollup.', 'monthly', 'customer', 'customer,contract_type,quantity_hours_entered,amount,total_invoiced_amount,invoice_status,approval_status'),
('pm_project_workload', 'PM Project Workload Report', 'Project Management', 'PM project workload, PM validation backlog, returned/rejected time, signed handoff gaps, and billing readiness.', 'weekly', 'pm,project', 'pm_name,project,customer,quantity_hours_entered,approval_status,invoice_status'),
('engineer_utilization_detail', 'Engineer Utilization Detail Report', 'Engineer', 'Engineer or selected engineer report for utilization, submitted time, missing time, billable/non-billable, allocation, AI-assisted entries.', 'weekly', 'engineer,team', 'engineer_name,team_name,entry_date,project,quantity_hours_entered,billable_status,approval_status,ai_assisted,sow_alignment_status'),
('team_organization_rollup', 'Team / Organization Rollup Report', 'Team / Organization', 'Team and organization-wide utilization, billable/non-billable totals, workload distribution, and approval backlog.', 'monthly', 'team,organization', 'team_name,organization_name,quantity_hours_entered,billable_status,approval_status,invoice_status'),
('workflow_audit_report', 'Workflow / Approval / Audit Report', 'Audit', 'Approval status, manager approval backlog, PM validation backlog, View-As audit, export readiness, handoff audit, notification audit.', 'date_range', 'workflow,status', 'actor_role,view_as_target,approval_status,forbidden_write_result,exported_date'),
('system_stability_report', 'System Stability Report', 'System Stability', 'Frontend, API, database, nginx, service status, error placeholders, uptime, and readiness checks.', 'daily', 'component,status', 'component_name,component_status,api_name,http_status,last_check'),
('api_status_report', 'API Status Report', 'API Status', 'Authentication, navigation, dashboard, notification, email, recipient safety, readiness, CRM, SOW/AI provider API status.', 'daily', 'api_area,status', 'api_name,api_path,http_status,component_status,last_check'),
('external_connection_report', 'External Connection Report', 'External Connections', 'CRM, Salesforce, Zendesk Sell, Claude, Azure, Brevo, SSO/Auth, recipient safety, and future connector readiness.', 'daily', 'connection,status', 'connection_name,connection_status,last_check,component_status'),
('authentication_security_report', 'Authentication / Security Report', 'Authentication / Security', 'SSO login activity, session_required events, role access checks, View-As activity, forbidden writes, admin/system readiness.', 'date_range', 'auth_event,role', 'auth_event_type,actor_role,view_as_target,forbidden_write_result,last_check'),
('executive_reporting_summary', 'Executive Reporting Summary', 'Executive', 'Leadership view across time, billing, projects, exceptions, system health, APIs, and external connections.', 'month_to_date', 'organization', 'organization_name,total_invoiced_amount,quantity_hours_entered,approval_status,invoice_status,component_status')
ON CONFLICT (template_key) DO UPDATE
SET template_name = EXCLUDED.template_name,
    category = EXCLUDED.category,
    description = EXCLUDED.description,
    default_period = EXCLUDED.default_period,
    default_grouping = EXCLUDED.default_grouping,
    output_columns = EXCLUDED.output_columns;

INSERT INTO reporting_external_connection_catalog (
    connection_key,
    connection_name,
    connection_type,
    provider_category,
    operational_owner
)
VALUES
('crm_generic', 'CRM Integration Framework', 'CRM', 'Sales-to-delivery source', 'PTC'),
('salesforce', 'Salesforce', 'CRM', 'External sales platform', 'Sales Operations'),
('zendesk_sell', 'Zendesk Sell', 'CRM', 'External sales platform', 'Sales Operations'),
('claude', 'Claude / Anthropic', 'AI Provider', 'SOW and time-entry drafting', 'Admin'),
('azure', 'Azure / Microsoft Identity', 'Cloud / Identity', 'Authentication and future integrations', 'Admin'),
('brevo', 'Brevo Email Provider', 'Email Provider', 'Shared notification delivery', 'Admin'),
('recipient_safety', 'Recipient Safety Gate', 'Email Safety', 'Email recipient policy gate', 'Admin'),
('sso_auth', 'SSO / Authentication', 'Authentication', 'User login and session provider', 'Admin'),
('nginx_public_frontend', 'Nginx Public Frontend Proxy', 'Infrastructure', 'Public frontend routing', 'Admin'),
('postgresql', 'PostgreSQL Project Health Dashboard Database', 'Database', 'Primary application database', 'Admin')
ON CONFLICT (connection_key) DO UPDATE
SET connection_name = EXCLUDED.connection_name,
    connection_type = EXCLUDED.connection_type,
    provider_category = EXCLUDED.provider_category,
    operational_owner = EXCLUDED.operational_owner;

INSERT INTO reporting_api_status_catalog (
    api_key,
    api_name,
    api_path,
    owning_module,
    expected_success_code
)
VALUES
('auth_dev_login', 'Authentication Dev Login API', '/api/auth/sso/dev-login', 'Authentication', '200'),
('navigation_registry_integrity', 'Navigation Registry Integrity API', '/api/navigation/registry-integrity', 'Navigation', '200'),
('dashboard_module_visibility', 'Dashboard Module Visibility Smoke API', '/api/dashboard/module-visibility-smoke', 'Dashboard', '200'),
('production_readiness', 'Production Readiness Command Center API', '/api/production/readiness-command-center', 'Production', '200'),
('production_notifications', 'Production Notifications Summary API', '/api/production/notifications/summary', '022', '200'),
('email_provider_summary', 'Shared Email Provider Summary API', '/api/system/email-provider/summary', '019M-CK', '200'),
('recipient_safety_summary', 'Recipient Safety Summary API', '/api/system/email-provider/recipient-safety/summary', '020J', '200'),
('time_compliance_email', 'Time Compliance Email Notifications API', '/api/time-compliance/email-notifications/summary', '019M-CJ', '200'),
('production_ops_ack', 'Production Operations Acknowledgments API', '/api/production/operations-acknowledgments/summary', '019M-CI', '200'),
('timesheet_ai_suggestion', 'Timesheet AI Suggestion API', '/api/timesheets/ai-description-suggestions', '025A', '200')
ON CONFLICT (api_key) DO UPDATE
SET api_name = EXCLUDED.api_name,
    api_path = EXCLUDED.api_path,
    owning_module = EXCLUDED.owning_module,
    expected_success_code = EXCLUDED.expected_success_code;

INSERT INTO reporting_system_health_catalog (
    component_key,
    component_name,
    component_type,
    health_dimension,
    reporting_notes
)
VALUES
('frontend_service', 'projecttime-frontend-public.service', 'service', 'availability', 'Frontend service active/inactive reporting.'),
('api_service', 'projecttime-api.service', 'service', 'availability', 'API service active/inactive reporting.'),
('nginx', 'nginx.service', 'service', 'availability', 'Nginx syntax, reload, and active status.'),
('postgresql', 'postgresql.service', 'service', 'availability', 'Database availability and migration readiness.'),
('published_frontend', 'Published Frontend Build', 'artifact', 'deployment', 'Published frontend index marker and size validation.'),
('runtime_frontend', 'Runtime Frontend Build', 'artifact', 'deployment', 'Runtime frontend index marker and size validation.'),
('navigation_registry', 'Navigation Registry', 'api', 'integrity', 'Registry integrity endpoint validation.'),
('dashboard_visibility', 'Dashboard Module Visibility', 'api', 'integrity', 'Module visibility smoke endpoint validation.'),
('email_provider', 'Shared Email Provider', 'integration', 'readiness', 'Brevo/shared provider readiness.'),
('recipient_safety', 'Recipient Safety Gate', 'integration', 'readiness', 'Recipient safety gate readiness.'),
('ai_provider', 'Claude AI Provider Readiness', 'integration', 'readiness', 'Server-side AI provider readiness for SOW and time-entry use cases.')
ON CONFLICT (component_key) DO UPDATE
SET component_name = EXCLUDED.component_name,
    component_type = EXCLUDED.component_type,
    health_dimension = EXCLUDED.health_dimension,
    reporting_notes = EXCLUDED.reporting_notes;

INSERT INTO reporting_role_visibility_rules (
    role_key,
    role_name,
    allowed_report_categories,
    restricted_report_categories,
    export_allowed,
    accounting_visibility,
    system_health_visibility,
    external_connection_visibility,
    criteria_scope_notes
)
VALUES
('engineer', 'Engineer', 'Own time, own utilization, assigned project, own AI time-entry audit', 'Global accounting, organization-wide, system health, external connections', FALSE, FALSE, FALSE, FALSE, 'Engineer criteria limited to own entries and assigned projects.'),
('project_management', 'Project Management', 'Assigned project, PM workload, assigned project time validation', 'Global engineer time, full accounting, system health, external connections', FALSE, FALSE, FALSE, FALSE, 'PM criteria limited to assigned projects.'),
('manager', 'Manager', 'Team report, team utilization, team approval backlog', 'Global organization unless assigned, full accounting, system health', TRUE, FALSE, FALSE, FALSE, 'Manager criteria limited to team scope.'),
('engineering_team_lead', 'Engineering Team Lead', 'Team engineers, team utilization, assigned work reports', 'Accounting export, admin system health', FALSE, FALSE, FALSE, FALSE, 'Team lead criteria limited to team engineers.'),
('pm_team_lead', 'PM Team Lead', 'PM team workload, managed projects, PM validation reporting', 'Engineering global time, accounting export, admin system health', FALSE, FALSE, FALSE, FALSE, 'PM lead criteria limited to PM team.'),
('ptc', 'PTC', 'Time, accounting, invoicing, customer, project, workflow, export, audit, handoff, assignment', 'Admin-only secrets/password reset', TRUE, TRUE, TRUE, TRUE, 'PTC has broad operational reporting scope.'),
('administrator', 'Administrator', 'All reporting categories', 'None except View-As write protection remains enforced', TRUE, TRUE, TRUE, TRUE, 'Admin has full report visibility and View-As remains read-only.'),
('executive', 'Executive', 'Executive summary, organization, customer, project, billing summary, system health summary', 'Operational writes, detailed restricted personnel records unless permitted', TRUE, TRUE, TRUE, TRUE, 'Executive sees high-level reporting and drill-down where permitted.'),
('accounting', 'Accounting', 'Accounting, invoicing, export readiness, customer billing, project billing', 'Admin-only system control', TRUE, TRUE, FALSE, FALSE, 'Accounting criteria focused on billing/export.'),
('sales', 'Sales', 'Customer, CRM, intake, SOW handoff reporting', 'Engineer time detail unless assigned, accounting export, system health', FALSE, FALSE, FALSE, FALSE, 'Sales criteria focused on customer/intake/SOW pipeline.'),
('solution_architect', 'Solution Architect', 'SOW/GSD scope, customer, project, handoff, AI scope alignment', 'Accounting export, admin system health', FALSE, FALSE, FALSE, FALSE, 'SA criteria focused on scope and handoff context.'),
('project_coordinator', 'Project Coordinator', 'Document workflow, post-intake, signed-date aging, handoff support', 'Approval/export/admin controls', FALSE, FALSE, FALSE, FALSE, 'Coordinator criteria focused on documents and workflow aging.')
ON CONFLICT (role_key) DO UPDATE
SET role_name = EXCLUDED.role_name,
    allowed_report_categories = EXCLUDED.allowed_report_categories,
    restricted_report_categories = EXCLUDED.restricted_report_categories,
    export_allowed = EXCLUDED.export_allowed,
    accounting_visibility = EXCLUDED.accounting_visibility,
    system_health_visibility = EXCLUDED.system_health_visibility,
    external_connection_visibility = EXCLUDED.external_connection_visibility,
    criteria_scope_notes = EXCLUDED.criteria_scope_notes;
