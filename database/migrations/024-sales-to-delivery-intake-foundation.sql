CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS sales_delivery_intake_packages (
    intake_package_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    intake_reference TEXT NOT NULL UNIQUE,
    intake_status TEXT NOT NULL DEFAULT 'draft',
    source_system TEXT NOT NULL DEFAULT 'manual',
    source_record_reference TEXT NOT NULL DEFAULT '',
    customer_name TEXT NOT NULL,
    project_name TEXT NOT NULL,
    project_type TEXT NOT NULL DEFAULT 'Professional Services',
    sales_owner TEXT NOT NULL DEFAULT '',
    solution_architect TEXT NOT NULL DEFAULT '',
    project_team_coordinator TEXT NOT NULL DEFAULT '',
    estimated_hours NUMERIC(12,2),
    estimated_revenue NUMERIC(14,2),
    high_level_scope TEXT NOT NULL DEFAULT '',
    signed_sow_required BOOLEAN NOT NULL DEFAULT TRUE,
    gsd_required BOOLEAN NOT NULL DEFAULT TRUE,
    signed_sow_received BOOLEAN NOT NULL DEFAULT FALSE,
    gsd_received BOOLEAN NOT NULL DEFAULT FALSE,
    intake_ready_for_assignment BOOLEAN NOT NULL DEFAULT FALSE,
    readiness_status TEXT NOT NULL DEFAULT 'pending_artifacts',
    readiness_notes TEXT NOT NULL DEFAULT '',
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sales_delivery_intake_artifacts (
    artifact_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    intake_package_id UUID REFERENCES sales_delivery_intake_packages(intake_package_id) ON DELETE CASCADE,
    intake_reference TEXT NOT NULL,
    artifact_type TEXT NOT NULL,
    artifact_name TEXT NOT NULL,
    artifact_status TEXT NOT NULL DEFAULT 'required',
    canonical_document_area TEXT NOT NULL DEFAULT 'project_hours_sow_gsd_engineer_allocation',
    visible_to_pm BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_engineers BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_leads BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_ptc BOOLEAN NOT NULL DEFAULT TRUE,
    uploaded_by TEXT NOT NULL DEFAULT '',
    uploaded_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sales_delivery_intake_readiness_reviews (
    readiness_review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    intake_reference TEXT NOT NULL,
    signed_sow_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    gsd_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    customer_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    scope_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    estimate_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    sales_owner_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    solution_architect_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    ptc_assignment_ready BOOLEAN NOT NULL DEFAULT FALSE,
    executive_visibility_ready BOOLEAN NOT NULL DEFAULT FALSE,
    readiness_status TEXT NOT NULL DEFAULT 'pending',
    reviewer TEXT NOT NULL DEFAULT 'system',
    review_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sales_delivery_intake_assignment_previews (
    assignment_preview_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    intake_reference TEXT NOT NULL,
    customer_name TEXT NOT NULL,
    project_name TEXT NOT NULL,
    project_type TEXT NOT NULL,
    recommended_pm TEXT NOT NULL DEFAULT '',
    recommended_engineer_team TEXT NOT NULL DEFAULT '',
    estimated_hours NUMERIC(12,2),
    assignment_status TEXT NOT NULL DEFAULT 'preview_only',
    handoff_email_subject TEXT NOT NULL DEFAULT '',
    handoff_email_preview TEXT NOT NULL DEFAULT '',
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sales_delivery_intake_activity_events (
    activity_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    intake_reference TEXT NOT NULL,
    event_type TEXT NOT NULL,
    event_summary TEXT NOT NULL,
    event_payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    actor TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sales_delivery_intake_visibility_rules (
    visibility_rule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_key TEXT NOT NULL UNIQUE,
    role_name TEXT NOT NULL,
    visibility_scope TEXT NOT NULL,
    can_create_intake BOOLEAN NOT NULL DEFAULT FALSE,
    can_update_intake BOOLEAN NOT NULL DEFAULT FALSE,
    can_upload_required_artifacts BOOLEAN NOT NULL DEFAULT FALSE,
    can_validate_readiness BOOLEAN NOT NULL DEFAULT FALSE,
    can_prepare_assignment BOOLEAN NOT NULL DEFAULT FALSE,
    can_view_artifacts BOOLEAN NOT NULL DEFAULT TRUE,
    notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO sales_delivery_intake_visibility_rules (
    rule_key,
    role_name,
    visibility_scope,
    can_create_intake,
    can_update_intake,
    can_upload_required_artifacts,
    can_validate_readiness,
    can_prepare_assignment,
    can_view_artifacts,
    notes
)
VALUES
('024_sales', 'Sales', 'sales_owned_intake', TRUE, TRUE, TRUE, FALSE, FALSE, TRUE, 'Sales can create intake and attach signed SOW/GSD package artifacts.'),
('024_solution_architect', 'Solution Architect', 'assigned_solution_design', TRUE, TRUE, TRUE, TRUE, FALSE, TRUE, 'Solution Architect reviews scope, GSD, and SOW readiness.'),
('024_ptc', 'Project Team Coordinator', 'all_intake_operations', FALSE, TRUE, TRUE, TRUE, TRUE, TRUE, 'PTC validates readiness and prepares PM/Engineer assignment.'),
('024_executive', 'Executive', 'intake_status_reporting', FALSE, FALSE, FALSE, FALSE, FALSE, TRUE, 'Executive receives visibility into signed handoff readiness.'),
('024_project_manager', 'Project Management', 'assigned_after_handoff', FALSE, FALSE, FALSE, FALSE, FALSE, TRUE, 'PM receives package visibility after assignment.'),
('024_engineer', 'Engineer', 'assigned_after_handoff', FALSE, FALSE, FALSE, FALSE, FALSE, TRUE, 'Engineers receive SOW/GSD visibility after assignment.')
ON CONFLICT (rule_key) DO UPDATE
SET
    role_name = EXCLUDED.role_name,
    visibility_scope = EXCLUDED.visibility_scope,
    can_create_intake = EXCLUDED.can_create_intake,
    can_update_intake = EXCLUDED.can_update_intake,
    can_upload_required_artifacts = EXCLUDED.can_upload_required_artifacts,
    can_validate_readiness = EXCLUDED.can_validate_readiness,
    can_prepare_assignment = EXCLUDED.can_prepare_assignment,
    can_view_artifacts = EXCLUDED.can_view_artifacts,
    notes = EXCLUDED.notes;
