CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS sow_ai_time_entry_scope_contexts (
    scope_context_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    scope_context_reference TEXT NOT NULL UNIQUE,
    handoff_reference TEXT NOT NULL DEFAULT '',
    intake_reference TEXT NOT NULL DEFAULT '',
    crm_record_reference TEXT NOT NULL DEFAULT '',
    customer_name TEXT NOT NULL,
    project_name TEXT NOT NULL,
    project_type TEXT NOT NULL DEFAULT 'Professional Services',
    assigned_project_manager TEXT NOT NULL DEFAULT '',
    assigned_engineering_team TEXT NOT NULL DEFAULT '',
    assigned_engineer TEXT NOT NULL DEFAULT '',
    signed_sow_file_name TEXT NOT NULL DEFAULT '',
    gsd_file_name TEXT NOT NULL DEFAULT '',
    sow_version_reference TEXT NOT NULL DEFAULT '',
    gsd_version_reference TEXT NOT NULL DEFAULT '',
    scope_summary TEXT NOT NULL DEFAULT '',
    included_scope_keywords TEXT NOT NULL DEFAULT '',
    excluded_scope_keywords TEXT NOT NULL DEFAULT '',
    scope_context_status TEXT NOT NULL DEFAULT 'preview_ready',
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sow_ai_time_entry_scope_documents (
    scope_document_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    scope_context_reference TEXT NOT NULL,
    document_type TEXT NOT NULL,
    document_name TEXT NOT NULL,
    document_version_reference TEXT NOT NULL DEFAULT '',
    canonical_document_area TEXT NOT NULL DEFAULT 'project_hours_sow_gsd_engineer_allocation',
    document_status TEXT NOT NULL DEFAULT 'available_for_scope_check',
    visible_to_assigned_engineers BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_project_manager BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_ptc BOOLEAN NOT NULL DEFAULT TRUE,
    visible_to_admin BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sow_ai_time_entry_ai_provider_readiness (
    ai_provider_readiness_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_key TEXT NOT NULL DEFAULT 'claude',
    provider_name TEXT NOT NULL DEFAULT 'Claude',
    consumer_key TEXT NOT NULL DEFAULT '028_sow_aware_ai_time_entry',
    provider_status TEXT NOT NULL DEFAULT 'server_side_configuration_required',
    secret_storage_policy TEXT NOT NULL DEFAULT 'no_repository_secrets',
    usage_policy TEXT NOT NULL DEFAULT 'draft_only_engineer_must_review',
    hallucination_control_policy TEXT NOT NULL DEFAULT 'must_compare_against_signed_sow_gsd_scope',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(provider_key, consumer_key)
);

CREATE TABLE IF NOT EXISTS sow_ai_time_entry_drafts (
    ai_time_entry_draft_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    draft_reference TEXT NOT NULL UNIQUE,
    scope_context_reference TEXT NOT NULL,
    project_name TEXT NOT NULL,
    task_name TEXT NOT NULL,
    engineer_email TEXT NOT NULL DEFAULT '',
    engineer_name TEXT NOT NULL DEFAULT '',
    work_date TEXT NOT NULL DEFAULT '',
    rough_work_description TEXT NOT NULL,
    ai_generated_time_entry TEXT NOT NULL DEFAULT '',
    ai_generated_customer_facing_summary TEXT NOT NULL DEFAULT '',
    recommended_hours NUMERIC(8,2),
    scope_alignment_status TEXT NOT NULL DEFAULT 'not_checked',
    scope_alignment_reason TEXT NOT NULL DEFAULT '',
    ai_provider_key TEXT NOT NULL DEFAULT 'claude',
    draft_status TEXT NOT NULL DEFAULT 'draft_preview',
    engineer_review_required BOOLEAN NOT NULL DEFAULT TRUE,
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sow_ai_time_entry_scope_checks (
    scope_check_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    draft_reference TEXT NOT NULL,
    scope_context_reference TEXT NOT NULL,
    scope_alignment_status TEXT NOT NULL,
    matched_scope_terms TEXT NOT NULL DEFAULT '',
    risk_terms TEXT NOT NULL DEFAULT '',
    out_of_scope_terms TEXT NOT NULL DEFAULT '',
    reasoning_summary TEXT NOT NULL DEFAULT '',
    requires_pm_review BOOLEAN NOT NULL DEFAULT FALSE,
    requires_ptc_review BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sow_ai_time_entry_acceptance_events (
    acceptance_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    draft_reference TEXT NOT NULL,
    scope_context_reference TEXT NOT NULL,
    acceptance_status TEXT NOT NULL DEFAULT 'preview_only',
    engineer_original_input TEXT NOT NULL DEFAULT '',
    ai_generated_output TEXT NOT NULL DEFAULT '',
    engineer_final_output TEXT NOT NULL DEFAULT '',
    accepted_hours NUMERIC(8,2),
    sow_version_reference TEXT NOT NULL DEFAULT '',
    gsd_version_reference TEXT NOT NULL DEFAULT '',
    actor TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sow_ai_time_entry_role_rules (
    role_rule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_key TEXT NOT NULL UNIQUE,
    role_name TEXT NOT NULL,
    visibility_scope TEXT NOT NULL,
    can_use_ai_time_entry BOOLEAN NOT NULL DEFAULT FALSE,
    can_view_own_ai_drafts BOOLEAN NOT NULL DEFAULT FALSE,
    can_view_team_ai_drafts BOOLEAN NOT NULL DEFAULT FALSE,
    can_view_all_ai_drafts BOOLEAN NOT NULL DEFAULT FALSE,
    can_override_scope_review BOOLEAN NOT NULL DEFAULT FALSE,
    can_manage_ai_provider BOOLEAN NOT NULL DEFAULT FALSE,
    notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS sow_ai_time_entry_readiness_reviews (
    readiness_review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    review_reference TEXT NOT NULL DEFAULT '028_sow_aware_ai_time_entry_readiness',
    signed_sow_context_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    gsd_context_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    assignment_context_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    engineer_own_time_rule_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    ai_provider_server_side_rule_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    engineer_review_required_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    audit_capture_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    scope_status_model_confirmed BOOLEAN NOT NULL DEFAULT FALSE,
    readiness_status TEXT NOT NULL DEFAULT 'pending',
    reviewer TEXT NOT NULL DEFAULT 'system',
    review_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO sow_ai_time_entry_ai_provider_readiness (
    provider_key,
    provider_name,
    consumer_key,
    provider_status,
    secret_storage_policy,
    usage_policy,
    hallucination_control_policy
)
VALUES (
    'claude',
    'Claude',
    '028_sow_aware_ai_time_entry',
    'server_side_configuration_required',
    'no_repository_secrets',
    'draft_only_engineer_must_review',
    'must_compare_against_signed_sow_gsd_scope'
)
ON CONFLICT (provider_key, consumer_key) DO UPDATE
SET
    provider_name = EXCLUDED.provider_name,
    provider_status = EXCLUDED.provider_status,
    secret_storage_policy = EXCLUDED.secret_storage_policy,
    usage_policy = EXCLUDED.usage_policy,
    hallucination_control_policy = EXCLUDED.hallucination_control_policy;

INSERT INTO sow_ai_time_entry_role_rules (
    rule_key,
    role_name,
    visibility_scope,
    can_use_ai_time_entry,
    can_view_own_ai_drafts,
    can_view_team_ai_drafts,
    can_view_all_ai_drafts,
    can_override_scope_review,
    can_manage_ai_provider,
    notes
)
VALUES
('028_engineer', 'Engineer', 'own_assigned_projects_and_own_time_only', TRUE, TRUE, FALSE, FALSE, FALSE, FALSE, 'Engineer can use AI time entry only for own assigned project/time drafts.'),
('028_project_manager', 'Project Management', 'assigned_projects_review_only', FALSE, FALSE, TRUE, FALSE, FALSE, FALSE, 'PM can review assigned project draft readiness and scope risk, but cannot submit engineer time.'),
('028_engineering_team_lead', 'Engineering Team Lead', 'team_scope_review', FALSE, FALSE, TRUE, FALSE, TRUE, FALSE, 'Engineering Team Lead can review team scope-risk patterns.'),
('028_ptc', 'Project Team Coordinator', 'operational_time_entry_visibility', FALSE, FALSE, TRUE, TRUE, TRUE, FALSE, 'PTC can review operational draft readiness and assignment workflow impact.'),
('028_admin', 'Administrator', 'system_wide_configuration_and_audit', FALSE, FALSE, TRUE, TRUE, TRUE, TRUE, 'Admin can view audit/configuration and manage provider readiness, but View-As remains read-only.'),
('028_executive', 'Executive', 'reporting_only', FALSE, FALSE, FALSE, FALSE, FALSE, FALSE, 'Executive receives high-level reporting only, not time-entry drafting.')
ON CONFLICT (rule_key) DO UPDATE
SET
    role_name = EXCLUDED.role_name,
    visibility_scope = EXCLUDED.visibility_scope,
    can_use_ai_time_entry = EXCLUDED.can_use_ai_time_entry,
    can_view_own_ai_drafts = EXCLUDED.can_view_own_ai_drafts,
    can_view_team_ai_drafts = EXCLUDED.can_view_team_ai_drafts,
    can_view_all_ai_drafts = EXCLUDED.can_view_all_ai_drafts,
    can_override_scope_review = EXCLUDED.can_override_scope_review,
    can_manage_ai_provider = EXCLUDED.can_manage_ai_provider,
    notes = EXCLUDED.notes;
