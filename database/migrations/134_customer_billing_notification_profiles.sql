-- Module 021 / 042 customer-specific invoice workflow notification profile.
BEGIN;

CREATE TABLE IF NOT EXISTS customer_billing_notification_profiles (
    client_id UUID PRIMARY KEY REFERENCES clients(client_id) ON DELETE CASCADE,
    invoice_generated_action_required_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    workflow_notes TEXT NOT NULL DEFAULT '',
    version BIGINT NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_by_user_id UUID NULL REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_customer_billing_notification_workflow_notes
        CHECK (char_length(workflow_notes) <= 4000)
);

CREATE TABLE IF NOT EXISTS customer_billing_notification_profile_audit (
    customer_billing_notification_profile_audit_id UUID PRIMARY KEY,
    client_id UUID NOT NULL REFERENCES clients(client_id) ON DELETE CASCADE,
    action_code TEXT NOT NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id),
    prior_state JSONB NOT NULL DEFAULT '{}'::jsonb,
    new_state JSONB NOT NULL DEFAULT '{}'::jsonb,
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_customer_billing_notification_profile_audit_client
    ON customer_billing_notification_profile_audit(client_id, occurred_at DESC);

INSERT INTO enterprise_notification_policies (
    policy_code, policy_name, category, source_module, event_code,
    trigger_mode, recipient_strategy, trigger_configuration,
    recipient_configuration, severity, acknowledgement_required,
    acknowledgement_escalation_minutes, subject_template, text_template,
    producer_contract, source_state
)
VALUES (
    'CUSTOMER_INVOICE_WORKFLOW_ACTION_REQUIRED',
    'Customer invoice workflow action required',
    'financial',
    '042',
    'customer_invoice_workflow_action_required',
    'event',
    'billing_project_team',
    '{"customerProfileRequired":true}'::jsonb,
    '{"to":["billing","finance","accounting"],"cc":["project_manager","ptc"],"channels":["email","teams"]}'::jsonb,
    'informational',
    FALSE,
    NULL,
    'ProjectPulse: Customer invoice workflow action required — {{projectCode}}',
    'Invoice {{invoiceNumber}} was generated for {{customerName}} / {{projectCode}} {{projectName}}. This customer has a Customer Directory billing notification profile enabled. Complete the required downstream billing workflow and record the resulting handoff or reconciliation in Pulse.',
    'module_021_042_customer_billing_profile_v1',
    'native_bridge'
)
ON CONFLICT (policy_code) DO UPDATE
SET policy_name = EXCLUDED.policy_name,
    category = EXCLUDED.category,
    source_module = EXCLUDED.source_module,
    event_code = EXCLUDED.event_code,
    trigger_mode = EXCLUDED.trigger_mode,
    recipient_strategy = EXCLUDED.recipient_strategy,
    trigger_configuration = EXCLUDED.trigger_configuration,
    recipient_configuration = EXCLUDED.recipient_configuration,
    subject_template = EXCLUDED.subject_template,
    text_template = EXCLUDED.text_template,
    producer_contract = EXCLUDED.producer_contract,
    source_state = EXCLUDED.source_state,
    updated_at = NOW();

DO $grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        GRANT SELECT, INSERT, UPDATE ON customer_billing_notification_profiles TO ptp_app;
        GRANT SELECT, INSERT ON customer_billing_notification_profile_audit TO ptp_app;
    END IF;
END $grant$;

INSERT INTO schema_migrations(migration_id, description)
VALUES (
    '134_customer_billing_notification_profiles',
    'Pulse-owned per-customer invoice workflow notification flags with audited Email and Teams enterprise delivery'
)
ON CONFLICT DO NOTHING;

COMMIT;
