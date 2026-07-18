-- Module 060 — Contracts / Block of Hours foundation
--
-- Sidecar-safe:
--   * Creates new Module 060 tables and views.
--   * Does not insert sample customers, contracts, users, hours, or balances.
--   * Does not alter global SMTP credentials.
--   * Does not alter Entra configuration.
--   * Does not yet alter existing project/time-entry tables.
--
-- The migration will be applied in a later controlled database step.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS boh_contracts (
    boh_contract_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    client_id UUID NOT NULL
        REFERENCES clients(client_id),

    contract_name TEXT NOT NULL,
    contract_status TEXT NOT NULL DEFAULT 'active',

    primary_account_executive_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    project_team_coordinator_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    purchased_hours NUMERIC(12,2) NOT NULL DEFAULT 0,

    start_date DATE NOT NULL,
    original_expiration_date DATE NOT NULL,
    effective_expiration_date DATE NOT NULL,

    eligible_tm BOOLEAN NOT NULL DEFAULT TRUE,
    eligible_service_request BOOLEAN NOT NULL DEFAULT TRUE,
    eligible_fixed_price BOOLEAN NOT NULL DEFAULT TRUE,
    eligible_iqs BOOLEAN NOT NULL DEFAULT TRUE,

    certinia_id TEXT NOT NULL DEFAULT '',
    sell_quote TEXT NOT NULL DEFAULT '',
    salesforce_id TEXT NOT NULL DEFAULT '',
    purchase_order_reference TEXT NOT NULL DEFAULT '',

    internal_summary TEXT NOT NULL DEFAULT '',

    created_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    updated_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_boh_contract_status
        CHECK (
            contract_status IN (
                'draft',
                'active',
                'low_balance',
                'expiring',
                'expired',
                'exhausted',
                'cancelled',
                'closed'
            )
        ),

    CONSTRAINT ck_boh_contract_purchased_hours
        CHECK (purchased_hours >= 0),

    CONSTRAINT ck_boh_contract_dates
        CHECK (
            original_expiration_date >= start_date
            AND effective_expiration_date >= start_date
        )
);

CREATE INDEX IF NOT EXISTS idx_boh_contracts_client
    ON boh_contracts(client_id);

CREATE INDEX IF NOT EXISTS idx_boh_contracts_ae
    ON boh_contracts(primary_account_executive_user_id);

CREATE INDEX IF NOT EXISTS idx_boh_contracts_coordinator
    ON boh_contracts(project_team_coordinator_user_id);

CREATE INDEX IF NOT EXISTS idx_boh_contracts_status_expiration
    ON boh_contracts(contract_status, effective_expiration_date);

CREATE TABLE IF NOT EXISTS boh_contract_secondary_sales (
    boh_contract_id UUID NOT NULL
        REFERENCES boh_contracts(boh_contract_id) ON DELETE CASCADE,

    user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    created_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    PRIMARY KEY (boh_contract_id, user_id)
);

CREATE TABLE IF NOT EXISTS boh_contract_adjustments (
    boh_contract_adjustment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    boh_contract_id UUID NOT NULL
        REFERENCES boh_contracts(boh_contract_id) ON DELETE CASCADE,

    adjustment_type TEXT NOT NULL,
    hours NUMERIC(12,2) NOT NULL,
    reason TEXT NOT NULL,

    customer_satisfaction_reference TEXT NOT NULL DEFAULT '',

    reverses_adjustment_id UUID NULL
        REFERENCES boh_contract_adjustments(boh_contract_adjustment_id),

    created_by_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_boh_adjustment_type
        CHECK (
            adjustment_type IN (
                'credit_awarded',
                'credit_reversal',
                'manual_correction'
            )
        ),

    CONSTRAINT ck_boh_adjustment_hours
        CHECK (hours > 0)
);

CREATE INDEX IF NOT EXISTS idx_boh_adjustments_contract
    ON boh_contract_adjustments(boh_contract_id, created_at DESC);

CREATE TABLE IF NOT EXISTS boh_contract_extensions (
    boh_contract_extension_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    boh_contract_id UUID NOT NULL
        REFERENCES boh_contracts(boh_contract_id) ON DELETE CASCADE,

    previous_expiration_date DATE NOT NULL,
    new_expiration_date DATE NOT NULL,
    reason TEXT NOT NULL,

    created_by_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_boh_extension_dates
        CHECK (new_expiration_date > previous_expiration_date)
);

CREATE INDEX IF NOT EXISTS idx_boh_extensions_contract
    ON boh_contract_extensions(boh_contract_id, created_at DESC);

CREATE TABLE IF NOT EXISTS boh_contract_notes (
    boh_contract_note_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    boh_contract_id UUID NOT NULL
        REFERENCES boh_contracts(boh_contract_id) ON DELETE CASCADE,

    note_text TEXT NOT NULL,

    created_by_user_id UUID NOT NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_boh_note_text
        CHECK (length(trim(note_text)) > 0)
);

CREATE INDEX IF NOT EXISTS idx_boh_notes_contract
    ON boh_contract_notes(boh_contract_id, created_at DESC);

CREATE TABLE IF NOT EXISTS boh_contract_work_links (
    boh_contract_work_link_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    boh_contract_id UUID NOT NULL
        REFERENCES boh_contracts(boh_contract_id),

    project_id UUID NULL
        REFERENCES projects(project_id),

    project_intake_request_id UUID NULL
        REFERENCES project_intake_requests(project_intake_request_id),

    task_id UUID NULL,
    billing_classification TEXT NOT NULL DEFAULT '',

    use_block_of_hours BOOLEAN NOT NULL DEFAULT TRUE,

    created_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_boh_work_link_target
        CHECK (
            project_id IS NOT NULL
            OR project_intake_request_id IS NOT NULL
            OR task_id IS NOT NULL
        )
);

CREATE INDEX IF NOT EXISTS idx_boh_work_links_contract
    ON boh_contract_work_links(boh_contract_id);

CREATE INDEX IF NOT EXISTS idx_boh_work_links_project
    ON boh_contract_work_links(project_id);

CREATE INDEX IF NOT EXISTS idx_boh_work_links_intake
    ON boh_contract_work_links(project_intake_request_id);

CREATE TABLE IF NOT EXISTS boh_usage_ledger (
    boh_usage_ledger_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    boh_contract_id UUID NOT NULL
        REFERENCES boh_contracts(boh_contract_id),

    time_entry_id UUID NULL,
    project_id UUID NULL
        REFERENCES projects(project_id),

    task_id UUID NULL,
    user_id UUID NULL
        REFERENCES app_users(user_id),

    work_date DATE NOT NULL,
    hours NUMERIC(12,2) NOT NULL,

    usage_status TEXT NOT NULL,
    billing_classification TEXT NOT NULL DEFAULT '',
    is_overage BOOLEAN NOT NULL DEFAULT FALSE,

    source_status TEXT NOT NULL DEFAULT '',
    source_reference TEXT NOT NULL DEFAULT '',

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_boh_usage_hours
        CHECK (hours > 0),

    CONSTRAINT ck_boh_usage_status
        CHECK (
            usage_status IN (
                'entered',
                'submitted',
                'consumed',
                'rejected',
                'declined',
                'voided',
                'reversed',
                'overage'
            )
        )
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_boh_usage_time_entry_active
    ON boh_usage_ledger(time_entry_id)
    WHERE time_entry_id IS NOT NULL
      AND usage_status NOT IN ('reversed', 'voided');

CREATE INDEX IF NOT EXISTS idx_boh_usage_contract_status
    ON boh_usage_ledger(boh_contract_id, usage_status, work_date);

CREATE TABLE IF NOT EXISTS boh_email_schedule (
    schedule_key TEXT PRIMARY KEY DEFAULT 'weekly-balance',

    is_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    weekday_iso INTEGER NOT NULL DEFAULT 1,
    send_time TIME NOT NULL DEFAULT TIME '08:00',
    time_zone TEXT NOT NULL DEFAULT 'America/Chicago',

    subject_template TEXT NOT NULL
        DEFAULT 'Weekly Block of Hours Balance Summary',

    body_introduction TEXT NOT NULL DEFAULT '',

    include_expired BOOLEAN NOT NULL DEFAULT FALSE,
    low_balance_threshold_percent NUMERIC(5,2)
        NOT NULL DEFAULT 25,

    expiration_warning_days INTEGER NOT NULL DEFAULT 90,
    retention_months INTEGER NOT NULL DEFAULT 24,

    updated_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_boh_schedule_weekday
        CHECK (weekday_iso BETWEEN 1 AND 7),

    CONSTRAINT ck_boh_schedule_low_balance
        CHECK (
            low_balance_threshold_percent >= 0
            AND low_balance_threshold_percent <= 100
        ),

    CONSTRAINT ck_boh_schedule_warning_days
        CHECK (expiration_warning_days >= 0),

    CONSTRAINT ck_boh_schedule_retention
        CHECK (retention_months BETWEEN 1 AND 120)
);

INSERT INTO boh_email_schedule (schedule_key)
VALUES ('weekly-balance')
ON CONFLICT (schedule_key) DO NOTHING;

CREATE TABLE IF NOT EXISTS boh_email_runs (
    boh_email_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    trigger_type TEXT NOT NULL,
    run_status TEXT NOT NULL DEFAULT 'queued',

    data_cutoff_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    workbook_filename TEXT NOT NULL DEFAULT '',
    workbook_sha256 TEXT NOT NULL DEFAULT '',
    workbook_storage_reference TEXT NOT NULL DEFAULT '',

    account_executive_count INTEGER NOT NULL DEFAULT 0,
    contract_count INTEGER NOT NULL DEFAULT 0,

    to_recipients_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    cc_recipients_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    excluded_recipients_json JSONB NOT NULL DEFAULT '[]'::jsonb,

    generation_result TEXT NOT NULL DEFAULT '',
    smtp_result TEXT NOT NULL DEFAULT '',
    error_details TEXT NOT NULL DEFAULT '',

    requested_by_user_id UUID NULL
        REFERENCES app_users(user_id),

    started_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_boh_email_trigger
        CHECK (
            trigger_type IN (
                'scheduled',
                'send_now',
                'test',
                'preview'
            )
        ),

    CONSTRAINT ck_boh_email_status
        CHECK (
            run_status IN (
                'queued',
                'generating',
                'sending',
                'succeeded',
                'partially_succeeded',
                'failed',
                'cancelled'
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_boh_email_runs_created
    ON boh_email_runs(created_at DESC);

CREATE OR REPLACE VIEW vw_boh_contract_balances AS
WITH adjustment_totals AS (
    SELECT
        boh_contract_id,
        COALESCE(SUM(
            CASE
                WHEN adjustment_type IN (
                    'credit_awarded',
                    'manual_correction'
                )
                    THEN hours
                WHEN adjustment_type = 'credit_reversal'
                    THEN -hours
                ELSE 0
            END
        ), 0)::NUMERIC(12,2) AS credit_awarded
    FROM boh_contract_adjustments
    GROUP BY boh_contract_id
),
usage_totals AS (
    SELECT
        boh_contract_id,
        COALESCE(SUM(
            CASE WHEN usage_status = 'entered'
                THEN hours ELSE 0 END
        ), 0)::NUMERIC(12,2) AS entered_hours,

        COALESCE(SUM(
            CASE WHEN usage_status = 'submitted'
                THEN hours ELSE 0 END
        ), 0)::NUMERIC(12,2) AS submitted_hours,

        COALESCE(SUM(
            CASE WHEN usage_status = 'consumed'
                THEN hours ELSE 0 END
        ), 0)::NUMERIC(12,2) AS consumed_hours,

        COALESCE(SUM(
            CASE WHEN usage_status = 'overage'
                THEN hours ELSE 0 END
        ), 0)::NUMERIC(12,2) AS overage_hours
    FROM boh_usage_ledger
    GROUP BY boh_contract_id
)
SELECT
    c.boh_contract_id,
    c.client_id,
    c.contract_name,
    c.contract_status,
    c.primary_account_executive_user_id,
    c.project_team_coordinator_user_id,
    c.purchased_hours,
    COALESCE(a.credit_awarded, 0)::NUMERIC(12,2)
        AS credit_awarded,
    (
        c.purchased_hours
        + COALESCE(a.credit_awarded, 0)
    )::NUMERIC(12,2) AS total_available_hours,
    COALESCE(u.entered_hours, 0)::NUMERIC(12,2)
        AS entered_hours,
    COALESCE(u.submitted_hours, 0)::NUMERIC(12,2)
        AS submitted_hours,
    COALESCE(u.consumed_hours, 0)::NUMERIC(12,2)
        AS consumed_hours,
    COALESCE(u.overage_hours, 0)::NUMERIC(12,2)
        AS overage_hours,
    (
        c.purchased_hours
        + COALESCE(a.credit_awarded, 0)
        - COALESCE(u.consumed_hours, 0)
    )::NUMERIC(12,2) AS remaining_balance,
    (
        c.purchased_hours
        + COALESCE(a.credit_awarded, 0)
        - COALESCE(u.entered_hours, 0)
        - COALESCE(u.submitted_hours, 0)
        - COALESCE(u.consumed_hours, 0)
    )::NUMERIC(12,2) AS projected_remaining,
    c.start_date,
    c.original_expiration_date,
    c.effective_expiration_date,
    c.certinia_id,
    c.sell_quote,
    c.salesforce_id,
    c.purchase_order_reference,
    c.created_at,
    c.updated_at
FROM boh_contracts c
LEFT JOIN adjustment_totals a
    ON a.boh_contract_id = c.boh_contract_id
LEFT JOIN usage_totals u
    ON u.boh_contract_id = c.boh_contract_id;

COMMIT;
