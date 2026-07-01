CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS uat_role_validation_matrix (
    role_validation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_key TEXT NOT NULL UNIQUE,
    role_name TEXT NOT NULL,
    dashboard_scope TEXT NOT NULL,
    navigation_scope TEXT NOT NULL,
    allowed_modules TEXT NOT NULL DEFAULT '',
    restricted_modules TEXT NOT NULL DEFAULT '',
    write_controls TEXT NOT NULL DEFAULT '',
    validation_status TEXT NOT NULL DEFAULT 'pending',
    validation_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS uat_workflow_validation_scenarios (
    scenario_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    scenario_key TEXT NOT NULL UNIQUE,
    scenario_name TEXT NOT NULL,
    workflow_area TEXT NOT NULL,
    scenario_description TEXT NOT NULL,
    expected_result TEXT NOT NULL,
    modules_in_scope TEXT NOT NULL DEFAULT '',
    validation_status TEXT NOT NULL DEFAULT 'pending',
    evidence_required BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS uat_view_as_enforcement_tests (
    view_as_test_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    test_key TEXT NOT NULL UNIQUE,
    test_name TEXT NOT NULL,
    actor_role TEXT NOT NULL,
    target_role TEXT NOT NULL,
    expected_access TEXT NOT NULL,
    expected_write_result TEXT NOT NULL DEFAULT 'read_only_or_forbidden',
    validation_status TEXT NOT NULL DEFAULT 'pending',
    audit_required BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS uat_module_access_checks (
    module_access_check_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    check_key TEXT NOT NULL UNIQUE,
    module_number TEXT NOT NULL,
    module_name TEXT NOT NULL,
    route_hash TEXT NOT NULL,
    dashboard_card_required BOOLEAN NOT NULL DEFAULT TRUE,
    standalone_route_required BOOLEAN NOT NULL DEFAULT TRUE,
    registry_validation_required BOOLEAN NOT NULL DEFAULT TRUE,
    validation_status TEXT NOT NULL DEFAULT 'pending',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS uat_evidence_capture_events (
    evidence_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    evidence_reference TEXT NOT NULL UNIQUE,
    scenario_key TEXT NOT NULL DEFAULT '',
    role_key TEXT NOT NULL DEFAULT '',
    module_number TEXT NOT NULL DEFAULT '',
    evidence_type TEXT NOT NULL DEFAULT 'manual_observation',
    evidence_summary TEXT NOT NULL DEFAULT '',
    evidence_status TEXT NOT NULL DEFAULT 'captured_preview',
    captured_by TEXT NOT NULL DEFAULT 'system',
    captured_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS uat_approval_export_audit_checks (
    audit_check_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    check_key TEXT NOT NULL UNIQUE,
    check_name TEXT NOT NULL,
    workflow_area TEXT NOT NULL,
    expected_control TEXT NOT NULL,
    validation_status TEXT NOT NULL DEFAULT 'pending',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS uat_readiness_reviews (
    readiness_review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    review_reference TEXT NOT NULL DEFAULT '029_uat_role_workflow_validation',
    role_matrix_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    workflow_scenarios_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    view_as_enforcement_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    dashboard_navigation_registry_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    module_access_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    approval_export_audit_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    evidence_capture_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    closeout_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    readiness_status TEXT NOT NULL DEFAULT 'pending',
    reviewer TEXT NOT NULL DEFAULT 'system',
    review_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO uat_role_validation_matrix (
    role_key,
    role_name,
    dashboard_scope,
    navigation_scope,
    allowed_modules,
    restricted_modules,
    write_controls,
    validation_notes
)
VALUES
('engineer', 'Engineer', 'Own time, assigned work, holidays view-only, project workspace', 'Timesheet, Utilization, Holidays, Project Workspace', 'Timesheet, Utilization, Holidays, Project Workspace, assigned SOW/GSD, own AI time entry', 'Project Info, Allocation, workflow/export/reconciliation, Work Task Builder, management panels', 'Own time only; cannot approve/export/write through View-As', 'Engineer own-time-only and assigned-project visibility must pass.'),
('project_management', 'Project Management', 'Assigned projects, Project Workload, PM validation', 'Project Workload, Project Workspace, assigned project validation', 'Assigned project packages, PM validation, assigned SOW/GSD', 'Project Info, Allocation, Utilization, global engineer time, export/reconciliation', 'Can validate assigned project time only; cannot submit engineer time', 'PM assigned-project-only controls must pass.'),
('manager', 'Manager', 'Team approvals, team utilization, holidays management, customer directory view', 'Approvals, Team Utilization, Holidays, Customer Directory', 'Team approval/rejection, team utilization, holidays upload/manage', 'Global admin, password reset, unrelated teams', 'Approve/reject team scope only', 'Manager team-scope approval controls must pass.'),
('engineering_team_lead', 'Engineering Team Lead', 'Team engineers, assigned work, team/individual utilization', 'Team utilization, Project Workspace, assigned work', 'Team visibility and coaching review', 'Approval/password reset/export/reconciliation', 'No approval/password reset', 'Engineering Team Lead scoped visibility must pass.'),
('pm_team_lead', 'PM Team Lead', 'PMs on team, PM workload, managed projects', 'PM workload, managed project workspace', 'PM team workload and managed project status', 'Engineer approval/password reset/export/reconciliation', 'No approval/password reset', 'PM Team Lead scoped visibility must pass.'),
('ptc', 'Project Team Coordinator', 'Broad operations, accounting/billing/reconciliation, intake, handoff, assignment', 'Operations, Intake, Handoff, Reconciliation, Export, Workflow', 'Time records, holidays, customers, cost alerts, workflow/export/accounting, handoff assignment', 'System admin secrets/password reset unless admin', 'Operational workflow controls only', 'PTC operational workflow controls must pass.'),
('administrator', 'Administrator', 'Full system access with View-As read-only preview', 'All modules', 'All modules and system administration', 'None except View-As write protection', 'View-As write attempts must be forbidden/audited', 'Admin full access and View-As write protection must pass.'),
('executive', 'Executive', 'High-level reporting and workflow status', 'Executive reports, workflow status, readiness', 'Reporting, high-level workflow status, signed handoff visibility', 'Operational writes, approvals, exports unless explicitly assigned', 'Reporting-only controls', 'Executive reporting-only visibility must pass.'),
('accounting', 'Accounting', 'Reconciliation/export operational readiness', 'Accounting, Reconciliation, Export', 'Reconciliation/export visibility and workflow readiness', 'Admin/full system control', 'Accounting workflow only', 'Accounting reconciliation/export scope must pass.'),
('sales', 'Sales', 'Sales-owned intake and signed SOW handoff prep', 'Sales Intake, SOW Generator, Handoff', 'CRM/intake context, SOW handoff readiness', 'PM/Engineer assignment trigger unless PTC/Admin', 'Sales-owned intake/handoff only', 'Sales workflow visibility must pass.'),
('solution_architect', 'Solution Architect', 'SOW review, scope validation, technical handoff', 'SOW Generator, Intake, Handoff', 'SOW/GSD review, technical scope validation, handoff support', 'Assignment trigger unless PTC/Admin', 'Scope validation only', 'SA scope workflow visibility must pass.'),
('project_coordinator', 'Project Coordinator', 'Post-intake editing, document uploads, signed-date aging workflow', 'Intake, Documents, Aging, Handoff support', 'Post-intake editing, document upload, aging workflow', 'Approvals/export/admin', 'Coordinator workflow only', 'Project Coordinator post-intake controls must pass.')
ON CONFLICT (role_key) DO UPDATE
SET
    role_name = EXCLUDED.role_name,
    dashboard_scope = EXCLUDED.dashboard_scope,
    navigation_scope = EXCLUDED.navigation_scope,
    allowed_modules = EXCLUDED.allowed_modules,
    restricted_modules = EXCLUDED.restricted_modules,
    write_controls = EXCLUDED.write_controls,
    validation_notes = EXCLUDED.validation_notes,
    updated_at = now();

INSERT INTO uat_workflow_validation_scenarios (
    scenario_key,
    scenario_name,
    workflow_area,
    scenario_description,
    expected_result,
    modules_in_scope
)
VALUES
('029_engineer_own_time', 'Engineer Own-Time Validation', 'Timesheet', 'Engineer can draft and manage only own time entries and assigned project context.', 'Engineer sees own time only and cannot view global user time records.', '028, Timesheet'),
('029_pm_assigned_project', 'PM Assigned-Project Validation', 'Project Management', 'PM can see assigned project workload and validate assigned project time.', 'PM cannot access unrelated projects or global engineer time records.', '027, 028'),
('029_manager_team_approval', 'Manager Team Approval Validation', 'Approval', 'Manager can approve/reject team scope only.', 'Manager cannot approve unrelated team entries.', 'Approvals'),
('029_admin_view_as_read_only', 'Admin View-As Read-Only Validation', 'View-As', 'Admin can preview role experience but cannot submit/approve/write as selected user.', 'View-As write attempts are forbidden and audited.', 'All'),
('029_ptc_handoff_assignment', 'PTC Handoff Assignment Validation', 'Operations', 'PTC validates signed SOW/GSD handoff and prepares PM/Engineer assignment.', 'PTC can operate handoff/assignment workflow without admin-only access.', '024,025,026,027'),
('029_executive_reporting', 'Executive Reporting Validation', 'Reporting', 'Executive can view high-level workflow status and readiness without operational controls.', 'Executive sees reporting-only status.', '024,027,029'),
('029_accounting_export', 'Accounting Reconciliation/Export Validation', 'Accounting', 'Accounting can see reconciliation/export readiness without full admin access.', 'Accounting has scoped export/reconciliation visibility only.', 'Export, Reconciliation'),
('029_sales_sa_intake_sow', 'Sales and SA Intake/SOW Validation', 'Sales-to-Delivery', 'Sales and SA can move from CRM/intake to reviewed SOW and signed handoff readiness.', 'Sales/SA visibility aligns to intake/SOW/handoff responsibilities.', '024,025,026,027'),
('029_project_coordinator_documents', 'Project Coordinator Document Workflow Validation', 'Documents', 'Project Coordinator can support post-intake editing, uploads, and signed-date aging workflow.', 'Project Coordinator has document workflow visibility without approval/export/admin access.', '024,027'),
('029_module_chain_024_028', 'Module Chain Continuity Validation', 'Platform Workflow', 'Validate dashboard and standalone routes for modules 024 through 028.', 'Each module card and route is visible without page bleed-through.', '024,025,026,027,028')
ON CONFLICT (scenario_key) DO UPDATE
SET
    scenario_name = EXCLUDED.scenario_name,
    workflow_area = EXCLUDED.workflow_area,
    scenario_description = EXCLUDED.scenario_description,
    expected_result = EXCLUDED.expected_result,
    modules_in_scope = EXCLUDED.modules_in_scope;

INSERT INTO uat_view_as_enforcement_tests (
    test_key,
    test_name,
    actor_role,
    target_role,
    expected_access,
    expected_write_result
)
VALUES
('029_admin_view_as_engineer_write_block', 'Admin View-As Engineer Write Block', 'Administrator', 'Engineer', 'Read-only preview of Engineer experience', 'POST/approval/submit/write returns forbidden'),
('029_admin_view_as_pm_write_block', 'Admin View-As PM Write Block', 'Administrator', 'Project Management', 'Read-only preview of PM experience', 'Validation/write actions forbidden'),
('029_admin_view_as_manager_approval_block', 'Admin View-As Manager Approval Block', 'Administrator', 'Manager', 'Read-only preview of Manager experience', 'Approve/reject as viewed user forbidden'),
('029_ptc_view_as_engineer_block', 'PTC View-As Engineer Write Block', 'PTC', 'Engineer', 'No impersonated write', 'Write forbidden'),
('029_manager_out_of_team_block', 'Manager Out-of-Team Approval Block', 'Manager', 'Engineer outside team', 'No out-of-team approval access', 'Approval forbidden')
ON CONFLICT (test_key) DO UPDATE
SET
    test_name = EXCLUDED.test_name,
    actor_role = EXCLUDED.actor_role,
    target_role = EXCLUDED.target_role,
    expected_access = EXCLUDED.expected_access,
    expected_write_result = EXCLUDED.expected_write_result;

INSERT INTO uat_module_access_checks (
    check_key,
    module_number,
    module_name,
    route_hash,
    dashboard_card_required,
    standalone_route_required,
    registry_validation_required
)
VALUES
('029_module_024_sales_intake', '024', 'Sales-to-Delivery Intake Foundation', '#sales-intake', TRUE, TRUE, TRUE),
('029_module_025_sow_generator', '025', 'SOW Generator + Claude Review Workflow', '#sow-generator', TRUE, TRUE, TRUE),
('029_module_026_crm_integration', '026', 'CRM Integration Framework', '#crm-integration', TRUE, TRUE, TRUE),
('029_module_027_signed_handoff', '027', 'Signed SOW Handoff + Assignment Trigger', '#signed-handoff', TRUE, TRUE, TRUE),
('029_module_028_ai_time_entry', '028', 'SOW-Aware AI Time Entry Generator', '#ai-time-entry', TRUE, TRUE, TRUE),
('029_module_029_uat_validation', '029', 'User Acceptance / Role + Workflow Validation Center', '#uat-validation', TRUE, TRUE, TRUE)
ON CONFLICT (check_key) DO UPDATE
SET
    module_number = EXCLUDED.module_number,
    module_name = EXCLUDED.module_name,
    route_hash = EXCLUDED.route_hash,
    dashboard_card_required = EXCLUDED.dashboard_card_required,
    standalone_route_required = EXCLUDED.standalone_route_required,
    registry_validation_required = EXCLUDED.registry_validation_required;

INSERT INTO uat_approval_export_audit_checks (
    check_key,
    check_name,
    workflow_area,
    expected_control
)
VALUES
('029_approval_scope_controls', 'Approval Scope Controls', 'Approval', 'Managers approve/reject team scope only; PM validates assigned project time only.'),
('029_export_role_controls', 'Export Role Controls', 'Export', 'PTC/Admin/Accounting export visibility follows operational role scope.'),
('029_audit_view_as_controls', 'View-As Audit Controls', 'Audit', 'View-As read-only preview and forbidden write attempts are auditable.'),
('029_notification_safety_controls', 'Notification Safety Controls', 'Notifications', 'Shared email provider and recipient safety checks remain validated.'),
('029_ai_audit_controls', 'AI Time Entry Audit Controls', 'AI / Timesheet', 'AI drafts capture original input, SOW/GSD version, final accepted entry, actor, and timestamp.')
ON CONFLICT (check_key) DO UPDATE
SET
    check_name = EXCLUDED.check_name,
    workflow_area = EXCLUDED.workflow_area,
    expected_control = EXCLUDED.expected_control;
