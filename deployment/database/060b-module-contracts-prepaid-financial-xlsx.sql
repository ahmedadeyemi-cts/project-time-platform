-- Module 060B — Prepaid financial balance, XLSX exchange, credits, notes,
-- and Work Register funding foundation.
--
-- Safety:
--   * Extends the existing Module 060 sidecar tables.
--   * Does not create customers, users, Account Executives, or coordinators.
--   * Does not alter Entra or global SMTP credentials.
--   * Does not mutate existing time-entry rows.
--   * Adds an auditable financial usage API foundation for later Work Register UI wiring.

BEGIN;

ALTER TABLE boh_contracts
    ADD COLUMN IF NOT EXISTS balance_unit TEXT NOT NULL DEFAULT 'currency',
    ADD COLUMN IF NOT EXISTS fixed_fee_item TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS latest_time_text TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS billing_date DATE NULL,
    ADD COLUMN IF NOT EXISTS fixed_fee_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS imported_pending_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS imported_approved_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_expenses NUMERIC(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS manual_adjustments NUMERIC(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS import_source_key TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS import_snapshot_at TIMESTAMPTZ NULL;

DO $$
BEGIN
    ALTER TABLE boh_contracts
        ADD CONSTRAINT ck_boh_contract_balance_unit
        CHECK (balance_unit IN ('currency', 'hours'));
EXCEPTION
    WHEN duplicate_object THEN NULL;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_boh_contracts_import_source_key
    ON boh_contracts(import_source_key)
    WHERE import_source_key <> '';

ALTER TABLE boh_contract_adjustments
    ADD COLUMN IF NOT EXISTS amount NUMERIC(14,2) NULL,
    ADD COLUMN IF NOT EXISTS awarded_on DATE NULL,
    ADD COLUMN IF NOT EXISTS source_reference TEXT NOT NULL DEFAULT '';

UPDATE boh_contract_adjustments
SET amount = hours
WHERE amount IS NULL;

UPDATE boh_contract_adjustments
SET awarded_on = created_at::DATE
WHERE awarded_on IS NULL;

CREATE INDEX IF NOT EXISTS idx_boh_adjustments_source_reference
    ON boh_contract_adjustments(source_reference)
    WHERE source_reference <> '';

ALTER TABLE boh_contract_notes
    ADD COLUMN IF NOT EXISTS note_category TEXT NOT NULL DEFAULT 'general',
    ADD COLUMN IF NOT EXISTS source_reference TEXT NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS idx_boh_notes_source_reference
    ON boh_contract_notes(source_reference)
    WHERE source_reference <> '';

ALTER TABLE boh_usage_ledger
    ADD COLUMN IF NOT EXISTS billing_rate NUMERIC(14,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS usage_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS financial_status TEXT NOT NULL DEFAULT '';

CREATE TABLE IF NOT EXISTS boh_balance_import_batches (
    boh_balance_import_batch_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_filename TEXT NOT NULL,
    source_sha256 TEXT NOT NULL,
    worksheet_name TEXT NOT NULL DEFAULT '',
    import_status TEXT NOT NULL DEFAULT 'preview',
    is_active BOOLEAN NOT NULL DEFAULT FALSE,
    uploaded_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
    confirmed_by_user_id UUID NULL REFERENCES app_users(user_id),
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    confirmed_at TIMESTAMPTZ NULL,
    total_rows INTEGER NOT NULL DEFAULT 0,
    valid_rows INTEGER NOT NULL DEFAULT 0,
    invalid_rows INTEGER NOT NULL DEFAULT 0,
    new_rows INTEGER NOT NULL DEFAULT 0,
    changed_rows INTEGER NOT NULL DEFAULT 0,
    duplicate_rows INTEGER NOT NULL DEFAULT 0,
    header_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    validation_summary_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    CONSTRAINT ck_boh_import_batch_status
        CHECK (
            import_status IN (
                'preview',
                'confirmed',
                'superseded',
                'rejected'
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_boh_import_batches_status
    ON boh_balance_import_batches(import_status, uploaded_at DESC);

CREATE UNIQUE INDEX IF NOT EXISTS uq_boh_import_active_batch
    ON boh_balance_import_batches(is_active)
    WHERE is_active = TRUE;

CREATE TABLE IF NOT EXISTS boh_balance_import_rows (
    boh_balance_import_row_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    boh_balance_import_batch_id UUID NOT NULL
        REFERENCES boh_balance_import_batches(boh_balance_import_batch_id)
        ON DELETE CASCADE,
    source_row_number INTEGER NOT NULL,
    source_key TEXT NOT NULL,
    row_status TEXT NOT NULL,
    change_type TEXT NOT NULL,
    validation_messages_json JSONB NOT NULL DEFAULT '[]'::JSONB,

    account_executive_text TEXT NOT NULL DEFAULT '',
    customer_text TEXT NOT NULL DEFAULT '',
    engagement_name TEXT NOT NULL DEFAULT '',
    contract_manager_text TEXT NOT NULL DEFAULT '',
    po_quote TEXT NOT NULL DEFAULT '',
    contract_start_date DATE NULL,
    contract_end_date DATE NULL,
    fixed_fee_item TEXT NOT NULL DEFAULT '',
    latest_time_text TEXT NOT NULL DEFAULT '',
    billing_date DATE NULL,
    fixed_fee_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    credit_awarded NUMERIC(14,2) NOT NULL DEFAULT 0,
    credit_awarded_on DATE NULL,
    credit_awarded_by_text TEXT NOT NULL DEFAULT '',
    pending_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    approved_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_hours_amount NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_expenses NUMERIC(14,2) NOT NULL DEFAULT 0,
    adjustments NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_used NUMERIC(14,2) NOT NULL DEFAULT 0,
    remaining_balance NUMERIC(14,2) NOT NULL DEFAULT 0,
    balance_percent NUMERIC(12,6) NULL,
    certinia_id TEXT NOT NULL DEFAULT '',
    sell_quote TEXT NOT NULL DEFAULT '',
    salesforce_id TEXT NOT NULL DEFAULT '',
    notes TEXT NOT NULL DEFAULT '',

    matched_account_executive_user_id UUID NULL
        REFERENCES app_users(user_id),
    matched_client_id UUID NULL
        REFERENCES clients(client_id),
    matched_project_team_coordinator_user_id UUID NULL
        REFERENCES app_users(user_id),
    matched_credit_awarded_by_user_id UUID NULL
        REFERENCES app_users(user_id),
    matched_boh_contract_id UUID NULL
        REFERENCES boh_contracts(boh_contract_id),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_boh_import_row
        UNIQUE (boh_balance_import_batch_id, source_row_number),
    CONSTRAINT ck_boh_import_row_status
        CHECK (row_status IN ('valid', 'invalid', 'skipped')),
    CONSTRAINT ck_boh_import_change_type
        CHECK (
            change_type IN (
                'new',
                'changed',
                'duplicate',
                'skipped'
            )
        )
);

CREATE INDEX IF NOT EXISTS idx_boh_import_rows_batch_status
    ON boh_balance_import_rows(
        boh_balance_import_batch_id,
        row_status,
        source_row_number
    );

ALTER TABLE boh_contracts
    ADD COLUMN IF NOT EXISTS import_batch_id UUID NULL
        REFERENCES boh_balance_import_batches(boh_balance_import_batch_id);

CREATE OR REPLACE VIEW vw_boh_prepaid_balance_rows AS
WITH credit_totals AS (
    SELECT
        boh_contract_id,
        COALESCE(
            SUM(
                CASE
                    WHEN adjustment_type = 'credit_awarded'
                        THEN COALESCE(amount, hours)
                    WHEN adjustment_type = 'credit_reversal'
                        THEN -COALESCE(amount, hours)
                    ELSE 0
                END
            ),
            0
        )::NUMERIC(14,2) AS credit_awarded
    FROM boh_contract_adjustments
    GROUP BY boh_contract_id
),
manual_corrections AS (
    SELECT
        boh_contract_id,
        COALESCE(
            SUM(
                CASE
                    WHEN adjustment_type = 'manual_correction'
                        THEN COALESCE(amount, hours)
                    ELSE 0
                END
            ),
            0
        )::NUMERIC(14,2) AS manual_correction_amount
    FROM boh_contract_adjustments
    GROUP BY boh_contract_id
),
latest_credit AS (
    SELECT DISTINCT ON (a.boh_contract_id)
        a.boh_contract_id,
        a.awarded_on,
        a.created_by_user_id,
        COALESCE(NULLIF(u.display_name, ''), u.email) AS awarded_by_name
    FROM boh_contract_adjustments a
    JOIN app_users u
        ON u.user_id = a.created_by_user_id
    WHERE a.adjustment_type = 'credit_awarded'
    ORDER BY
        a.boh_contract_id,
        COALESCE(a.awarded_on, a.created_at::DATE) DESC,
        a.created_at DESC
),
live_usage AS (
    SELECT
        l.boh_contract_id,
        COALESCE(
            SUM(
                CASE
                    WHEN l.usage_status IN ('entered', 'submitted')
                        THEN COALESCE(
                            NULLIF(l.usage_amount, 0),
                            l.hours * l.billing_rate
                        )
                    ELSE 0
                END
            ),
            0
        )::NUMERIC(14,2) AS pending_amount,
        COALESCE(
            SUM(
                CASE
                    WHEN l.usage_status IN ('consumed', 'overage')
                        THEN COALESCE(
                            NULLIF(l.usage_amount, 0),
                            l.hours * l.billing_rate
                        )
                    ELSE 0
                END
            ),
            0
        )::NUMERIC(14,2) AS approved_amount
    FROM boh_usage_ledger l
    JOIN boh_contracts c
        ON c.boh_contract_id = l.boh_contract_id
    WHERE l.created_at > COALESCE(
        c.import_snapshot_at,
        TIMESTAMPTZ '1970-01-01 00:00:00+00'
    )
      AND l.usage_status NOT IN (
          'rejected',
          'declined',
          'voided',
          'reversed'
      )
    GROUP BY l.boh_contract_id
),
note_summary AS (
    SELECT
        n.boh_contract_id,
        COUNT(*)::INTEGER AS note_count,
        (
            ARRAY_AGG(
                n.note_text
                ORDER BY n.created_at DESC
            )
        )[1] AS latest_note
    FROM boh_contract_notes n
    GROUP BY n.boh_contract_id
),
base AS (
    SELECT
        c.boh_contract_id,
        c.client_id,
        cl.client_name AS customer_name,
        COALESCE(
            NULLIF(
                CONCAT_WS(
                    ', ',
                    NULLIF(cc.address_line1, ''),
                    NULLIF(cc.address_line2, ''),
                    NULLIF(cc.city, ''),
                    NULLIF(cc.postal_code, '')
                ),
                ''
            ),
            ''
        ) AS customer_address,
        c.contract_name AS engagement_name,
        c.contract_status,
        c.primary_account_executive_user_id,
        COALESCE(NULLIF(ae.display_name, ''), ae.email)
            AS account_executive_name,
        ae.email AS account_executive_email,
        c.project_team_coordinator_user_id,
        COALESCE(NULLIF(ptc.display_name, ''), ptc.email)
            AS contract_manager_name,
        ptc.email AS contract_manager_email,
        c.purchase_order_reference AS po_quote,
        c.start_date AS contract_start_date,
        c.effective_expiration_date AS contract_end_date,
        c.fixed_fee_item,
        c.latest_time_text,
        c.billing_date,
        c.fixed_fee_amount,
        COALESCE(ct.credit_awarded, 0)::NUMERIC(14,2)
            AS credit_awarded,
        lc.awarded_on AS latest_credit_awarded_on,
        COALESCE(lc.awarded_by_name, '') AS latest_credit_awarded_by,
        (
            c.imported_pending_amount
            + COALESCE(lu.pending_amount, 0)
        )::NUMERIC(14,2) AS pending_amount,
        (
            c.imported_approved_amount
            + COALESCE(lu.approved_amount, 0)
        )::NUMERIC(14,2) AS approved_amount,
        c.total_expenses,
        (
            c.manual_adjustments
            + COALESCE(mc.manual_correction_amount, 0)
        )::NUMERIC(14,2) AS adjustments,
        c.certinia_id,
        c.sell_quote,
        c.salesforce_id,
        c.balance_unit,
        COALESCE(ns.note_count, 0) AS note_count,
        COALESCE(ns.latest_note, '') AS latest_note,
        c.import_batch_id,
        c.import_snapshot_at,
        c.updated_at
    FROM boh_contracts c
    JOIN clients cl
        ON cl.client_id = c.client_id
    JOIN app_users ae
        ON ae.user_id = c.primary_account_executive_user_id
    JOIN app_users ptc
        ON ptc.user_id = c.project_team_coordinator_user_id
    LEFT JOIN LATERAL (
        SELECT
            address_line1,
            address_line2,
            city,
            postal_code
        FROM client_contacts
        WHERE client_id = cl.client_id
        ORDER BY
            is_primary DESC,
            display_order,
            created_at
        LIMIT 1
    ) cc ON TRUE
    LEFT JOIN credit_totals ct
        ON ct.boh_contract_id = c.boh_contract_id
    LEFT JOIN manual_corrections mc
        ON mc.boh_contract_id = c.boh_contract_id
    LEFT JOIN latest_credit lc
        ON lc.boh_contract_id = c.boh_contract_id
    LEFT JOIN live_usage lu
        ON lu.boh_contract_id = c.boh_contract_id
    LEFT JOIN note_summary ns
        ON ns.boh_contract_id = c.boh_contract_id
)
SELECT
    b.*,
    (b.pending_amount + b.approved_amount)::NUMERIC(14,2)
        AS total_hours_amount,
    (
        b.pending_amount
        + b.approved_amount
        + b.total_expenses
    )::NUMERIC(14,2) AS total_used,
    (
        b.fixed_fee_amount
        + b.credit_awarded
        + b.adjustments
    )::NUMERIC(14,2) AS total_available,
    (
        b.fixed_fee_amount
        + b.credit_awarded
        + b.adjustments
        - b.pending_amount
        - b.approved_amount
        - b.total_expenses
    )::NUMERIC(14,2) AS remaining_balance,
    CASE
        WHEN (
            b.fixed_fee_amount
            + b.credit_awarded
            + b.adjustments
        ) = 0
            THEN NULL
        ELSE (
            b.fixed_fee_amount
            + b.credit_awarded
            + b.adjustments
            - b.pending_amount
            - b.approved_amount
            - b.total_expenses
        ) / (
            b.fixed_fee_amount
            + b.credit_awarded
            + b.adjustments
        )
    END::NUMERIC(12,6) AS balance_percent
FROM base b;

COMMIT;
