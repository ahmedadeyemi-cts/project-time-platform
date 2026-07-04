CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS crm_integration_providers (
    provider_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_key TEXT NOT NULL UNIQUE,
    provider_name TEXT NOT NULL,
    provider_type TEXT NOT NULL,
    provider_status TEXT NOT NULL DEFAULT 'placeholder',
    auth_model TEXT NOT NULL DEFAULT 'external_secret_provider',
    configuration_scope TEXT NOT NULL DEFAULT 'server_side_only',
    secret_storage_policy TEXT NOT NULL DEFAULT 'no_repository_secrets',
    supports_accounts BOOLEAN NOT NULL DEFAULT TRUE,
    supports_opportunities BOOLEAN NOT NULL DEFAULT TRUE,
    supports_quotes BOOLEAN NOT NULL DEFAULT TRUE,
    supports_attachments BOOLEAN NOT NULL DEFAULT FALSE,
    notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS crm_integration_field_mappings (
    mapping_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_key TEXT NOT NULL,
    crm_object TEXT NOT NULL,
    crm_field TEXT NOT NULL,
    projectpulse_destination TEXT NOT NULL,
    workflow_target TEXT NOT NULL,
    mapping_purpose TEXT NOT NULL,
    required_for_promotion BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    display_order INTEGER NOT NULL DEFAULT 100,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE(provider_key, crm_object, crm_field, projectpulse_destination, workflow_target)
);

CREATE TABLE IF NOT EXISTS crm_integration_sync_preview_events (
    sync_preview_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_key TEXT NOT NULL,
    source_record_type TEXT NOT NULL,
    source_record_reference TEXT NOT NULL,
    source_record_name TEXT NOT NULL,
    customer_name TEXT NOT NULL,
    opportunity_or_deal_name TEXT NOT NULL,
    quote_reference TEXT NOT NULL DEFAULT '',
    sales_owner TEXT NOT NULL DEFAULT '',
    solution_architect TEXT NOT NULL DEFAULT '',
    estimated_hours NUMERIC(12,2),
    estimated_revenue NUMERIC(14,2),
    promotion_target TEXT NOT NULL DEFAULT 'preview_only',
    preview_payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    validation_status TEXT NOT NULL DEFAULT 'previewed',
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS crm_integration_promotion_events (
    promotion_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_key TEXT NOT NULL,
    source_record_type TEXT NOT NULL,
    source_record_reference TEXT NOT NULL,
    source_record_name TEXT NOT NULL,
    promotion_target TEXT NOT NULL,
    promotion_status TEXT NOT NULL DEFAULT 'preview_only',
    promoted_customer_name TEXT NOT NULL,
    promoted_project_name TEXT NOT NULL,
    promoted_quote_reference TEXT NOT NULL DEFAULT '',
    promoted_sales_owner TEXT NOT NULL DEFAULT '',
    promoted_solution_architect TEXT NOT NULL DEFAULT '',
    promoted_estimated_hours NUMERIC(12,2),
    promoted_estimated_revenue NUMERIC(14,2),
    audit_payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_by TEXT NOT NULL DEFAULT 'system',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS crm_integration_readiness_reviews (
    readiness_review_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    review_scope TEXT NOT NULL DEFAULT 'module_026_crm_integration_framework',
    provider_scope TEXT NOT NULL DEFAULT 'salesforce_zendesk_sell',
    field_mapping_reviewed BOOLEAN NOT NULL DEFAULT FALSE,
    security_reviewed BOOLEAN NOT NULL DEFAULT FALSE,
    no_repository_secret_reviewed BOOLEAN NOT NULL DEFAULT FALSE,
    sow_mapping_reviewed BOOLEAN NOT NULL DEFAULT FALSE,
    intake_mapping_reviewed BOOLEAN NOT NULL DEFAULT FALSE,
    sync_preview_reviewed BOOLEAN NOT NULL DEFAULT FALSE,
    promotion_audit_reviewed BOOLEAN NOT NULL DEFAULT FALSE,
    readiness_status TEXT NOT NULL DEFAULT 'pending',
    reviewer TEXT NOT NULL DEFAULT 'system',
    review_notes TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO crm_integration_providers (
    provider_key,
    provider_name,
    provider_type,
    provider_status,
    auth_model,
    configuration_scope,
    secret_storage_policy,
    supports_accounts,
    supports_opportunities,
    supports_quotes,
    supports_attachments,
    notes
)
VALUES
(
    'salesforce',
    'Salesforce',
    'crm_opportunity_platform',
    'placeholder',
    'oauth_server_side',
    'backend_configuration_only',
    'no_repository_secrets',
    TRUE,
    TRUE,
    TRUE,
    TRUE,
    'Framework placeholder for Salesforce Account, Opportunity, Quote, Owner, Solution Architect, service line, and attachment metadata.'
),
(
    'zendesk_sell',
    'Zendesk Sell',
    'crm_deal_platform',
    'placeholder',
    'api_or_oauth_server_side',
    'backend_configuration_only',
    'no_repository_secrets',
    TRUE,
    TRUE,
    TRUE,
    TRUE,
    'Framework placeholder for Zendesk Sell Company, Deal, Quote, Sales Owner, service line, and handoff metadata.'
)
ON CONFLICT (provider_key) DO UPDATE
SET
    provider_name = EXCLUDED.provider_name,
    provider_type = EXCLUDED.provider_type,
    provider_status = EXCLUDED.provider_status,
    auth_model = EXCLUDED.auth_model,
    configuration_scope = EXCLUDED.configuration_scope,
    secret_storage_policy = EXCLUDED.secret_storage_policy,
    supports_accounts = EXCLUDED.supports_accounts,
    supports_opportunities = EXCLUDED.supports_opportunities,
    supports_quotes = EXCLUDED.supports_quotes,
    supports_attachments = EXCLUDED.supports_attachments,
    notes = EXCLUDED.notes,
    updated_at = now();

INSERT INTO crm_integration_field_mappings (
    provider_key,
    crm_object,
    crm_field,
    projectpulse_destination,
    workflow_target,
    mapping_purpose,
    required_for_promotion,
    display_order
)
VALUES
('shared', 'Account/Company', 'Name', 'Project Health Dashboard Customer', 'customer_match', 'Creates or matches onboarded customer record.', TRUE, 10),
('shared', 'Opportunity/Deal', 'Name', 'SOW Project / Engagement Name', 'sow_generator', 'Prepopulates the SOW Generator project name.', TRUE, 20),
('shared', 'Opportunity/Deal', 'Record ID', 'CRM Reference', 'audit_traceability', 'Preserved for audit and traceability.', TRUE, 30),
('shared', 'Quote/Deal', 'Value', 'Estimated Revenue / Planned Cost Context', 'intake_foundation', 'Used for intake readiness and executive visibility.', FALSE, 40),
('shared', 'Opportunity/Deal', 'Owner', 'Sales Owner', 'handoff_notification', 'Used for handoff notification and audit history.', TRUE, 50),
('shared', 'Opportunity/Deal', 'Solution Architect', 'Solution Architect', 'sow_review', 'Owns SOW draft review and hallucination controls.', TRUE, 60),
('shared', 'Opportunity/Deal', 'Signed Stage / Close Stage', 'Signed SOW Handoff Trigger', 'signed_handoff', 'Module 027 uses this to trigger PTC and Executive notification.', TRUE, 70),
('shared', 'Products/Services', 'Quote Lines', 'Research-backed SOW Scope Seed', 'sow_research', 'Used by Module 025 to guide Claude process research and scope drafting.', TRUE, 80),
('shared', 'Products/Services', 'Estimated Service Hours', 'Project Hours / Resource Demand', 'resource_assignment', 'Feeds intake, PM assignment, and engineer allocation planning.', FALSE, 90),
('shared', 'Attachments', 'SOW/GSD Files', 'Canonical SOW / GSD Artifacts', 'project_documents', 'Signed SOW and GSD remain in canonical project document area.', TRUE, 100)
ON CONFLICT (provider_key, crm_object, crm_field, projectpulse_destination, workflow_target) DO UPDATE
SET
    mapping_purpose = EXCLUDED.mapping_purpose,
    required_for_promotion = EXCLUDED.required_for_promotion,
    is_active = TRUE,
    display_order = EXCLUDED.display_order,
    updated_at = now();
