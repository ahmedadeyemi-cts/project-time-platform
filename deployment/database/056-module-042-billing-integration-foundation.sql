-- MODULE 042 - Billing, Purchase Order, Invoice Ledger,
-- and External Integration Foundation
--
-- Sidecar-safe:
--   * Does not alter existing project, customer, user, time-entry,
--     task, or rate-card columns.
--   * Does not create fictional operational records.
--   * Seeds connector definitions only as NOT CONFIGURED.
--   * Stores secret references, never secret values.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS project_billing_profiles (
    project_id UUID PRIMARY KEY
        REFERENCES projects(project_id) ON DELETE CASCADE,

    purchase_order_required BOOLEAN NOT NULL DEFAULT FALSE,
    billing_contact_name TEXT NOT NULL DEFAULT '',
    billing_contact_email TEXT NOT NULL DEFAULT '',
    billing_instructions TEXT NOT NULL DEFAULT '',
    invoice_delivery_method TEXT NOT NULL DEFAULT 'manual',

    default_rate_card_id UUID NULL
        REFERENCES work_rate_cards(rate_card_id),

    created_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    updated_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_project_billing_profiles_delivery
        CHECK (
            invoice_delivery_method IN (
                'manual',
                'email',
                'certinia',
                'salesforce',
                'sell',
                'other'
            )
        )
);

CREATE TABLE IF NOT EXISTS project_purchase_orders (
    project_purchase_order_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    project_id UUID NOT NULL
        REFERENCES projects(project_id) ON DELETE CASCADE,

    po_number TEXT NOT NULL,
    po_status TEXT NOT NULL DEFAULT 'active',
    is_primary BOOLEAN NOT NULL DEFAULT FALSE,

    authorized_amount NUMERIC(14,2) NULL,
    effective_start_date DATE NULL,
    effective_end_date DATE NULL,

    customer_reference TEXT NOT NULL DEFAULT '',
    customer_notes TEXT NOT NULL DEFAULT '',
    internal_notes TEXT NOT NULL DEFAULT '',

    source_system TEXT NOT NULL DEFAULT 'work_register',
    external_source_id TEXT NOT NULL DEFAULT '',

    created_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    updated_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_project_purchase_orders_project_number
        UNIQUE(project_id, po_number),

    CONSTRAINT ck_project_purchase_orders_status
        CHECK (
            po_status IN (
                'draft',
                'active',
                'expired',
                'exhausted',
                'replaced',
                'cancelled'
            )
        ),

    CONSTRAINT ck_project_purchase_orders_amount
        CHECK (
            authorized_amount IS NULL
            OR authorized_amount >= 0
        ),

    CONSTRAINT ck_project_purchase_orders_dates
        CHECK (
            effective_end_date IS NULL
            OR effective_start_date IS NULL
            OR effective_end_date >= effective_start_date
        )
);

CREATE INDEX IF NOT EXISTS idx_project_purchase_orders_project
    ON project_purchase_orders(project_id);

CREATE INDEX IF NOT EXISTS idx_project_purchase_orders_status
    ON project_purchase_orders(project_id, po_status);

CREATE UNIQUE INDEX IF NOT EXISTS uq_project_purchase_orders_primary
    ON project_purchase_orders(project_id)
    WHERE is_primary = TRUE
      AND po_status IN ('draft', 'active');

CREATE TABLE IF NOT EXISTS billing_invoices (
    billing_invoice_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    invoice_number TEXT NULL,
    project_id UUID NOT NULL
        REFERENCES projects(project_id),

    client_id UUID NULL
        REFERENCES clients(client_id),

    invoice_type TEXT NOT NULL DEFAULT 'partial',
    invoice_status TEXT NOT NULL DEFAULT 'draft',

    billing_period_start DATE NULL,
    billing_period_end DATE NULL,
    invoice_date DATE NULL,
    due_date DATE NULL,

    customer_name_snapshot TEXT NOT NULL DEFAULT '',
    project_code_snapshot TEXT NOT NULL DEFAULT '',
    project_name_snapshot TEXT NOT NULL DEFAULT '',
    contract_type_snapshot TEXT NOT NULL DEFAULT '',

    project_manager_name_snapshot TEXT NOT NULL DEFAULT '',
    project_coordinator_name_snapshot TEXT NOT NULL DEFAULT '',

    purchase_order_id UUID NULL
        REFERENCES project_purchase_orders(project_purchase_order_id),

    purchase_order_number_snapshot TEXT NOT NULL DEFAULT '',
    purchase_order_amount_snapshot NUMERIC(14,2) NULL,

    certinia_id_snapshot TEXT NOT NULL DEFAULT '',
    salesforce_id_snapshot TEXT NOT NULL DEFAULT '',
    sell_quote_snapshot TEXT NOT NULL DEFAULT '',

    subtotal_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    adjustment_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_amount NUMERIC(14,2) NOT NULL DEFAULT 0,

    invoice_notes TEXT NOT NULL DEFAULT '',
    billing_instructions_snapshot TEXT NOT NULL DEFAULT '',

    created_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    finalized_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    finalized_at TIMESTAMPTZ NULL,

    immutable_snapshot_json JSONB NOT NULL DEFAULT '{}'::jsonb,

    CONSTRAINT ck_billing_invoices_type
        CHECK (invoice_type IN ('partial', 'final', 'credit', 'adjustment')),

    CONSTRAINT ck_billing_invoices_status
        CHECK (
            invoice_status IN (
                'draft',
                'blocked',
                'ready_for_pm',
                'ready_for_accounting',
                'approved',
                'finalized',
                'exported',
                'sent',
                'paid',
                'void'
            )
        ),

    CONSTRAINT ck_billing_invoices_period
        CHECK (
            billing_period_end IS NULL
            OR billing_period_start IS NULL
            OR billing_period_end >= billing_period_start
        )
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_billing_invoices_number
    ON billing_invoices(invoice_number)
    WHERE invoice_number IS NOT NULL
      AND BTRIM(invoice_number) <> '';

CREATE INDEX IF NOT EXISTS idx_billing_invoices_project_status
    ON billing_invoices(project_id, invoice_status);

CREATE INDEX IF NOT EXISTS idx_billing_invoices_client
    ON billing_invoices(client_id);

CREATE INDEX IF NOT EXISTS idx_billing_invoices_period
    ON billing_invoices(billing_period_start, billing_period_end);

CREATE TABLE IF NOT EXISTS billing_invoice_lines (
    billing_invoice_line_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    billing_invoice_id UUID NOT NULL
        REFERENCES billing_invoices(billing_invoice_id) ON DELETE CASCADE,

    line_number INTEGER NOT NULL,

    source_type TEXT NOT NULL DEFAULT 'time_entry',

    time_entry_id UUID NULL
        REFERENCES time_entries(time_entry_id),

    project_task_id UUID NULL
        REFERENCES project_tasks(project_task_id),

    resource_user_id UUID NULL
        REFERENCES app_users(user_id),

    work_date DATE NULL,

    resource_name_snapshot TEXT NOT NULL DEFAULT '',
    resource_email_snapshot TEXT NOT NULL DEFAULT '',

    task_code_snapshot TEXT NOT NULL DEFAULT '',
    task_name_snapshot TEXT NOT NULL DEFAULT '',

    customer_facing_description TEXT NOT NULL DEFAULT '',
    internal_description TEXT NOT NULL DEFAULT '',

    time_type TEXT NOT NULL DEFAULT 'normal',
    labor_category TEXT NOT NULL DEFAULT 'engineering',
    work_location TEXT NOT NULL DEFAULT '',

    approved_hours NUMERIC(10,2) NOT NULL DEFAULT 0,

    rate_card_id UUID NULL
        REFERENCES work_rate_cards(rate_card_id),

    rate_line_id UUID NULL
        REFERENCES work_rate_card_lines(rate_line_id),

    rate_code_snapshot TEXT NOT NULL DEFAULT '',
    rate_description_snapshot TEXT NOT NULL DEFAULT '',
    unit_rate NUMERIC(14,2) NOT NULL DEFAULT 0,

    line_amount NUMERIC(14,2) NOT NULL DEFAULT 0,

    manager_approval_snapshot TEXT NOT NULL DEFAULT '',
    project_approval_snapshot TEXT NOT NULL DEFAULT '',
    accounting_readiness_snapshot TEXT NOT NULL DEFAULT '',

    source_snapshot_json JSONB NOT NULL DEFAULT '{}'::jsonb,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_billing_invoice_lines_number
        UNIQUE(billing_invoice_id, line_number),

    CONSTRAINT ck_billing_invoice_lines_source
        CHECK (
            source_type IN (
                'time_entry',
                'fixed_price_milestone',
                'expense',
                'credit',
                'adjustment',
                'other'
            )
        ),

    CONSTRAINT ck_billing_invoice_lines_hours
        CHECK (approved_hours >= 0)
);

CREATE INDEX IF NOT EXISTS idx_billing_invoice_lines_invoice
    ON billing_invoice_lines(billing_invoice_id);

CREATE INDEX IF NOT EXISTS idx_billing_invoice_lines_time_entry
    ON billing_invoice_lines(time_entry_id);

CREATE INDEX IF NOT EXISTS idx_billing_invoice_lines_project_task
    ON billing_invoice_lines(project_task_id);

CREATE TABLE IF NOT EXISTS billing_invoice_events (
    billing_invoice_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    billing_invoice_id UUID NOT NULL
        REFERENCES billing_invoices(billing_invoice_id) ON DELETE CASCADE,

    event_type TEXT NOT NULL,
    prior_status TEXT NOT NULL DEFAULT '',
    new_status TEXT NOT NULL DEFAULT '',

    actor_user_id UUID NULL
        REFERENCES app_users(user_id),

    event_reason TEXT NOT NULL DEFAULT '',
    event_json JSONB NOT NULL DEFAULT '{}'::jsonb,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_billing_invoice_events_invoice
    ON billing_invoice_events(billing_invoice_id, created_at DESC);

CREATE TABLE IF NOT EXISTS external_integration_connections (
    external_integration_connection_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    system_code TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,

    environment_name TEXT NOT NULL DEFAULT 'test',
    connection_status TEXT NOT NULL DEFAULT 'not_configured',

    base_url TEXT NOT NULL DEFAULT '',
    api_version TEXT NOT NULL DEFAULT '',
    authentication_type TEXT NOT NULL DEFAULT 'to_confirm',

    client_id_reference TEXT NOT NULL DEFAULT '',
    secret_reference TEXT NOT NULL DEFAULT '',
    service_account_reference TEXT NOT NULL DEFAULT '',

    inbound_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    outbound_enabled BOOLEAN NOT NULL DEFAULT FALSE,

    capabilities_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    configuration_json JSONB NOT NULL DEFAULT '{}'::jsonb,

    last_connection_test_at TIMESTAMPTZ NULL,
    last_connection_test_status TEXT NOT NULL DEFAULT 'not_tested',
    last_connection_test_message TEXT NOT NULL DEFAULT '',

    last_successful_sync_at TIMESTAMPTZ NULL,
    last_error_at TIMESTAMPTZ NULL,
    last_error_message TEXT NOT NULL DEFAULT '',

    created_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    updated_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_external_integration_connection_status
        CHECK (
            connection_status IN (
                'not_configured',
                'configured',
                'testing',
                'connected',
                'degraded',
                'disabled',
                'error'
            )
        )
);

CREATE TABLE IF NOT EXISTS external_integration_field_mappings (
    external_integration_field_mapping_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    system_code TEXT NOT NULL
        REFERENCES external_integration_connections(system_code)
        ON DELETE CASCADE,

    sync_direction TEXT NOT NULL,
    local_entity TEXT NOT NULL,
    local_field TEXT NOT NULL,

    remote_object TEXT NOT NULL,
    remote_field TEXT NOT NULL,

    mapping_status TEXT NOT NULL DEFAULT 'draft',
    is_required BOOLEAN NOT NULL DEFAULT FALSE,
    is_external_id BOOLEAN NOT NULL DEFAULT FALSE,

    transform_key TEXT NOT NULL DEFAULT '',
    default_value TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_external_integration_field_mapping
        UNIQUE (
            system_code,
            sync_direction,
            local_entity,
            local_field,
            remote_object,
            remote_field
        ),

    CONSTRAINT ck_external_integration_sync_direction
        CHECK (
            sync_direction IN (
                'inbound',
                'outbound',
                'bidirectional'
            )
        ),

    CONSTRAINT ck_external_integration_mapping_status
        CHECK (
            mapping_status IN (
                'draft',
                'confirmed',
                'disabled'
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_external_integration_mappings_system
    ON external_integration_field_mappings(system_code);

CREATE TABLE IF NOT EXISTS external_integration_sync_runs (
    external_integration_sync_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    system_code TEXT NOT NULL
        REFERENCES external_integration_connections(system_code),

    sync_direction TEXT NOT NULL,
    sync_mode TEXT NOT NULL DEFAULT 'manual',

    correlation_id TEXT NOT NULL DEFAULT '',
    run_status TEXT NOT NULL DEFAULT 'queued',

    requested_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    records_read INTEGER NOT NULL DEFAULT 0,
    records_created INTEGER NOT NULL DEFAULT 0,
    records_updated INTEGER NOT NULL DEFAULT 0,
    records_skipped INTEGER NOT NULL DEFAULT 0,
    records_failed INTEGER NOT NULL DEFAULT 0,

    started_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,

    error_summary TEXT NOT NULL DEFAULT '',
    run_metadata_json JSONB NOT NULL DEFAULT '{}'::jsonb,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_external_integration_sync_run_status
        CHECK (
            run_status IN (
                'queued',
                'running',
                'succeeded',
                'partially_succeeded',
                'failed',
                'cancelled'
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_external_integration_sync_runs_system
    ON external_integration_sync_runs(system_code, created_at DESC);

CREATE TABLE IF NOT EXISTS external_integration_outbox (
    external_integration_outbox_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    system_code TEXT NOT NULL
        REFERENCES external_integration_connections(system_code),

    local_entity TEXT NOT NULL,
    local_entity_id UUID NULL,

    operation_type TEXT NOT NULL,
    idempotency_key TEXT NOT NULL UNIQUE,

    payload_json JSONB NOT NULL,
    delivery_status TEXT NOT NULL DEFAULT 'pending',

    attempt_count INTEGER NOT NULL DEFAULT 0,
    next_attempt_at TIMESTAMPTZ NULL,

    last_attempt_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,

    last_error TEXT NOT NULL DEFAULT '',

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_external_integration_outbox_operation
        CHECK (
            operation_type IN (
                'create',
                'update',
                'upsert',
                'delete',
                'status_update'
            )
        ),

    CONSTRAINT ck_external_integration_outbox_status
        CHECK (
            delivery_status IN (
                'pending',
                'processing',
                'succeeded',
                'failed',
                'dead_letter',
                'cancelled'
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_external_integration_outbox_delivery
    ON external_integration_outbox(
        system_code,
        delivery_status,
        next_attempt_at
    );

INSERT INTO external_integration_connections (
    system_code,
    display_name,
    environment_name,
    connection_status,
    authentication_type,
    inbound_enabled,
    outbound_enabled,
    capabilities_json
)
VALUES
(
    'SALESFORCE',
    'Salesforce',
    'test',
    'not_configured',
    'to_confirm',
    FALSE,
    FALSE,
    '{
        "customers": true,
        "opportunities": true,
        "quotes": true,
        "externalIds": true,
        "projects": false,
        "invoices": false
    }'::jsonb
),
(
    'CERTINIA',
    'Certinia',
    'test',
    'not_configured',
    'to_confirm',
    FALSE,
    FALSE,
    '{
        "projects": true,
        "billing": true,
        "invoices": true,
        "purchaseOrders": true,
        "externalIds": true
    }'::jsonb
),
(
    'SELL',
    'SELL',
    'test',
    'not_configured',
    'to_confirm',
    FALSE,
    FALSE,
    '{
        "customers": true,
        "deals": true,
        "quotes": true,
        "externalIds": true,
        "sync": true
    }'::jsonb
)
ON CONFLICT (system_code) DO UPDATE
SET
    display_name = EXCLUDED.display_name,
    capabilities_json = EXCLUDED.capabilities_json,
    updated_at = NOW();

COMMIT;
