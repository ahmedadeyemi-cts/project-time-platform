-- Requires 060 and 060b. Apply before deploying the matching API/UI.
BEGIN;

-- One funding source per new project; separate from its customer billing model.
CREATE TABLE IF NOT EXISTS contract_project_funding (
    project_id UUID PRIMARY KEY REFERENCES projects(project_id),
    boh_contract_id UUID NOT NULL REFERENCES boh_contracts(boh_contract_id),
    drawdown_hourly_rate NUMERIC(14,2) NOT NULL CHECK (drawdown_hourly_rate > 0),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_contract_project_funding_contract
    ON contract_project_funding(boh_contract_id);

CREATE OR REPLACE FUNCTION contract_time_approval_bucket(entry_status TEXT, has_pm BOOLEAN)
RETURNS TEXT LANGUAGE SQL IMMUTABLE PARALLEL SAFE AS $$
    SELECT CASE
        WHEN entry_status = 'submitted' THEN 'pending'
        WHEN entry_status = 'manager_approved' AND has_pm THEN 'pending'
        WHEN entry_status IN ('pm_approved', 'accounting_ready', 'reconciled', 'locked')
            OR (entry_status = 'manager_approved' AND NOT has_pm) THEN 'approved'
        ELSE 'excluded'
    END;
$$;

-- Read canonical time status on every request. Approvals, returns, edits and
-- deletions take effect without a second client-side ledger write. An explicit
-- active ledger entry takes precedence over automatic project funding.
CREATE OR REPLACE VIEW vw_boh_authoritative_time_usage AS
WITH mapped AS (
    SELECT e.time_entry_id, e.project_id, e.user_id, e.work_date, e.hours,
        e.status, e.created_at AS entry_created_at,
        COALESCE(l.boh_contract_id, f.boh_contract_id) AS boh_contract_id,
        COALESCE(l.billing_rate, f.drawdown_hourly_rate, 0) AS billing_rate,
        p.project_manager_user_id IS NOT NULL AS has_pm
    FROM time_entries e
    LEFT JOIN projects p ON p.project_id = e.project_id
    LEFT JOIN boh_usage_ledger l ON l.time_entry_id = e.time_entry_id
        AND l.usage_status NOT IN ('reversed', 'voided')
    LEFT JOIN contract_project_funding f ON f.project_id = e.project_id
    WHERE l.boh_contract_id IS NOT NULL OR f.boh_contract_id IS NOT NULL
)
SELECT m.*, ROUND(m.hours * m.billing_rate, 2)::NUMERIC(14,2) AS amount,
    contract_time_approval_bucket(m.status, m.has_pm) AS approval_bucket
FROM mapped m;

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
    SELECT u.boh_contract_id,
        COALESCE(SUM(u.amount) FILTER (WHERE u.approval_bucket = 'pending'), 0)::NUMERIC(14,2) AS pending_amount,
        COALESCE(SUM(u.amount) FILTER (WHERE u.approval_bucket = 'approved'), 0)::NUMERIC(14,2) AS approved_amount
    FROM (
        SELECT boh_contract_id, amount, approval_bucket, entry_created_at
        FROM vw_boh_authoritative_time_usage
        UNION ALL
        SELECT boh_contract_id, COALESCE(NULLIF(usage_amount, 0), hours * billing_rate),
            CASE WHEN usage_status = 'submitted' THEN 'pending'
                 WHEN usage_status IN ('consumed', 'overage') THEN 'approved' ELSE 'excluded' END,
            created_at
        FROM boh_usage_ledger WHERE time_entry_id IS NULL
    ) u
    JOIN boh_contracts c ON c.boh_contract_id = u.boh_contract_id
    WHERE u.entry_created_at > COALESCE(c.import_snapshot_at, TIMESTAMPTZ '1970-01-01 00:00:00+00')
    GROUP BY u.boh_contract_id
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


-- Hours are operational live time. Imported currency snapshots remain separately
-- identified and are never presented as hours or silently discarded.
CREATE OR REPLACE VIEW vw_boh_contract_time_totals AS
SELECT c.boh_contract_id,
    COALESCE(SUM(u.hours) FILTER (WHERE u.approval_bucket = 'pending'), 0)::NUMERIC(14,2) AS pending_hours,
    COALESCE(SUM(u.hours) FILTER (WHERE u.approval_bucket = 'approved'), 0)::NUMERIC(14,2) AS approved_hours
FROM boh_contracts c
LEFT JOIN vw_boh_authoritative_time_usage u ON u.boh_contract_id = c.boh_contract_id
GROUP BY c.boh_contract_id;
COMMIT;
