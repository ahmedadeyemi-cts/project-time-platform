CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS signed_sow_handoff_packages (
    handoff_package_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    handoff_reference TEXT NOT NULL UNIQUE,
    intake_reference TEXT NOT NULL DEFAULT '',
    crm_source TEXT NOT NULL DEFAULT '',
    crm_record_reference TEXT NOT NULL DEFAULT '',
    customer_name TEXT NOT NULL,
    project_name TEXT NOT NULL,
    project_type TEXT NOT NULL DEFAULT 'Professional Services',
    sales_owner TEXT NOT NULL DEFAULT '',
    solution_architect TEXT NOT NULL DEFAULT '',
    project_team_coordinator TEXT NOT NULL DEFAULT '',
    estimated_hours NUMERIC(12,2),
    estimated_revenue NUMERIC(14,2),
    signed_sow_file_name TEXT NOT NULL DEFAULT '',
    gsd_file_name TEXT NOT NULL DEFAULT '',
    scope_summary TEXT NOT NULL DEFAULT '',
    handoff_status TEXT NOT NULL DEFAULT 'draft',
    signed_sow_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    gsd_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    scope_locked BOOLEAN NOT NULL DEFAULT FALSE,
    delivery_package_ready BOOLEAN NOT NULL DEFAULT FALSE,
    ptc_notified BOOLEAN NOT NULL DEFAULT FALSE,
    executive_notified BOOLEAN NOT NULL DEFAULT FALSE,
    assignment_ready BOOLEAN NOT NULL DEFAULT FALSE,
    assignment_triggered BOOLEAN NOT NULL DEFAULT FALSE,
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS signed_sow_handoff_artifacts (
    artifact_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    handoff_reference TEXT NOT NULL,
    artifact_type TEXT NOT NULL,
    artifact_name TEXT NOT NULL,
    artifact_status TEXT NOT NULL DEFAULT 'required',
    canonical_document_area TEXT NOT NULL DEFAULT 'project_hours_sow_gsd_engineer_allocation',
    visible_to_ptc BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_executive BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_pm BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_engineers BOOLEAN NOT NULL DEFAULT TRUE,
    uploaded_by TEXT NOT NULL DEFAULT '',
    uploaded_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS signed_sow_handoff_notification_templates (
    notification_template_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    template_key TEXT NOT NULL UNIQUE,
    notification_stage TEXT NOT NULL,
    recipient_scope TEXT NOT NULL,
    subject_template TEXT NOT NULL,
    body_template TEXT NOT NULL,
    shared_email_provider_required BOOLEAN NOT NULL DEFAULT TRUE,
    recipient_safety_required BOOLEAN NOT NULL DEFAULT TRUE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS signed_sow_handoff_notification_events (
    notification_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    handoff_reference TEXT NOT NULL,
    notification_stage TEXT NOT NULL,
    recipient_scope TEXT NOT NULL,
    notification_status TEXT NOT NULL DEFAULT 'preview_only',
    subject_preview TEXT NOT NULL DEFAULT '',
    body_preview TEXT NOT NULL DEFAULT '',
    shared_email_provider_status TEXT NOT NULL DEFAULT 'not_sent',
    recipient_safety_status TEXT NOT NULL DEFAULT 'not_checked',
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS signed_sow_assignment_previews (
    assignment_preview_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    handoff_reference TEXT NOT NULL,
    customer_name TEXT NOT NULL,
    project_name TEXT NOT NULL,
    project_type TEXT NOT NULL,
    assigned_project_manager TEXT NOT NULL DEFAULT '',
    assigned_engineering_team TEXT NOT NULL DEFAULT '',
    primary_engineer TEXT NOT NULL DEFAULT '',
    secondary_engineer TEXT NOT NULL DEFAULT '',
    backup_engineer TEXT NOT NULL DEFAULT '',
    planned_hours NUMERIC(12,2),
    target_start_date TEXT NOT NULL DEFAULT '',
    assignment_status TEXT NOT NULL DEFAULT 'preview_only',
    assignment_notes TEXT NOT NULL DEFAULT '',
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS signed_sow_assignment_events (
    assignment_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    handoff_reference TEXT NOT NULL,
    assignment_event_type TEXT NOT NULL,
    assignment_event_status TEXT NOT NULL DEFAULT 'preview_only',
    assigned_project_manager TEXT NOT NULL DEFAULT '',
    assigned_engineering_team TEXT NOT NULL DEFAULT '',
    primary_engineer TEXT NOT NULL DEFAULT '',
    secondary_engineer TEXT NOT NULL DEFAULT '',
    event_payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    actor TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS signed_sow_handoff_readiness_reviews (
    readiness_review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    handoff_reference TEXT NOT NULL,
    signed_sow_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    gsd_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    scope_locked BOOLEAN NOT NULL DEFAULT FALSE,
    canonical_docs_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    ptc_notification_ready BOOLEAN NOT NULL DEFAULT FALSE,
    executive_notification_ready BOOLEAN NOT NULL DEFAULT FALSE,
    assignment_package_ready BOOLEAN NOT NULL DEFAULT FALSE,
    pm_assignment_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    engineer_assignment_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    shared_email_provider_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    recipient_safety_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    audit_reviewed BOOLEAN NOT NULL DEFAULT FALSE,
    readiness_status TEXT NOT NULL DEFAULT 'pending',
    reviewer TEXT NOT NULL DEFAULT 'system',
    review_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS signed_sow_handoff_visibility_rules (
    visibility_rule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_key TEXT NOT NULL UNIQUE,
    role_name TEXT NOT NULL,
    visibility_scope TEXT NOT NULL,
    can_mark_signed BOOLEAN NOT NULL DEFAULT FALSE,
    can_upload_artifacts BOOLEAN NOT NULL DEFAULT FALSE,
    can_notify_ptc_executive BOOLEAN NOT NULL DEFAULT FALSE,
    can_prepare_assignment BOOLEAN NOT NULL DEFAULT FALSE,
    can_trigger_assignment BOOLEAN NOT NULL DEFAULT FALSE,
    can_view_handoff BOOLEAN NOT NULL DEFAULT TRUE,
    notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO signed_sow_handoff_notification_templates (
    template_key,
    notification_stage,
    recipient_scope,
    subject_template,
    body_template,
    shared_email_provider_required,
    recipient_safety_required
)
VALUES
(
    '027_ptc_executive_signed_handoff',
    'signed_handoff_ready',
    'ptc_executive',
    'Signed SOW/GSD Ready - {{customer_name}} / {{project_name}}',
    'The signed SOW and GSD package is ready for PTC validation and PM/Engineer assignment. Customer: {{customer_name}}. Project: {{project_name}}. Sales Owner: {{sales_owner}}. Solution Architect: {{solution_architect}}.',
    TRUE,
    TRUE
),
(
    '027_pm_engineer_assignment',
    'assignment_triggered',
    'assigned_pm_engineers',
    'Project Assignment Ready - {{customer_name}} / {{project_name}}',
    'You have been assigned to the project delivery package. Review the signed SOW and GSD in ProjectPulse Project Workspace / Engineering Documents before starting delivery work.',
    TRUE,
    TRUE
)
ON CONFLICT (template_key) DO UPDATE
SET
    notification_stage = EXCLUDED.notification_stage,
    recipient_scope = EXCLUDED.recipient_scope,
    subject_template = EXCLUDED.subject_template,
    body_template = EXCLUDED.body_template,
    shared_email_provider_required = EXCLUDED.shared_email_provider_required,
    recipient_safety_required = EXCLUDED.recipient_safety_required,
    is_active = TRUE;

INSERT INTO signed_sow_handoff_visibility_rules (
    rule_key,
    role_name,
    visibility_scope,
    can_mark_signed,
    can_upload_artifacts,
    can_notify_ptc_executive,
    can_prepare_assignment,
    can_trigger_assignment,
    can_view_handoff,
    notes
)
VALUES
('027_sales', 'Sales', 'sales_owned_handoff', TRUE, TRUE, TRUE, FALSE, FALSE, TRUE, 'Sales can mark SOW signed and prepare the handoff package.'),
('027_solution_architect', 'Solution Architect', 'assigned_solution_design', TRUE, TRUE, TRUE, FALSE, FALSE, TRUE, 'Solution Architect validates scope and signed artifacts.'),
('027_ptc', 'Project Team Coordinator', 'all_handoff_operations', FALSE, TRUE, TRUE, TRUE, TRUE, TRUE, 'PTC validates handoff and triggers PM/Engineer assignment.'),
('027_executive', 'Executive', 'handoff_status_reporting', FALSE, FALSE, FALSE, FALSE, FALSE, TRUE, 'Executive receives signed handoff visibility.'),
('027_project_manager', 'Project Management', 'assigned_project_package', FALSE, FALSE, FALSE, FALSE, FALSE, TRUE, 'PM receives package visibility after assignment.'),
('027_engineer', 'Engineer', 'assigned_engineering_package', FALSE, FALSE, FALSE, FALSE, FALSE, TRUE, 'Engineers receive signed SOW/GSD visibility after assignment.')
ON CONFLICT (rule_key) DO UPDATE
SET
    role_name = EXCLUDED.role_name,
    visibility_scope = EXCLUDED.visibility_scope,
    can_mark_signed = EXCLUDED.can_mark_signed,
    can_upload_artifacts = EXCLUDED.can_upload_artifacts,
    can_notify_ptc_executive = EXCLUDED.can_notify_ptc_executive,
    can_prepare_assignment = EXCLUDED.can_prepare_assignment,
    can_trigger_assignment = EXCLUDED.can_trigger_assignment,
    can_view_handoff = EXCLUDED.can_view_handoff,
    notes = EXCLUDED.notes;
