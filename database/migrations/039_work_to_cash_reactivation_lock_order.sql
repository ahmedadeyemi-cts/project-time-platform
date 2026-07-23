-- ProjectPulse Work-to-Cash invoice reactivation lock-order hardening.
-- Apply after 038_work_to_cash_lifecycle_and_audit.sql.
--
-- billing_invoice_lines triggers execute in trigger-name order. The readiness
-- package guard therefore acquires its advisory lock before the time-entry
-- guard. Invoice reactivation must acquire those lock classes in the same
-- order to prevent a concurrent replacement insert from forming a deadlock.

BEGIN;

DO $projectpulse039_prerequisite$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '038_work_to_cash_lifecycle_and_audit'
    )
    THEN
        RAISE EXCEPTION
            'Migration 039 requires 038_work_to_cash_lifecycle_and_audit first.';
    END IF;
END;
$projectpulse039_prerequisite$;

CREATE OR REPLACE FUNCTION projectpulse038_guard_invoice_reactivation()
RETURNS trigger
LANGUAGE plpgsql
AS $projectpulse039_reactivation$
DECLARE
    v_time_entry_id UUID;
    v_readiness_review_id UUID;
BEGIN
    IF lower(COALESCE(OLD.invoice_status, '')) = 'void'
       AND lower(COALESCE(NEW.invoice_status, '')) <> 'void'
    THEN
        -- Match billing_invoice_lines trigger order: readiness before time.
        FOR v_readiness_review_id IN
            SELECT DISTINCT target_line.billing_readiness_review_id
            FROM billing_invoice_lines target_line
            WHERE target_line.billing_invoice_id = NEW.billing_invoice_id
              AND target_line.billing_readiness_review_id IS NOT NULL
            ORDER BY target_line.billing_readiness_review_id
        LOOP
            PERFORM pg_advisory_xact_lock(
                hashtextextended(v_readiness_review_id::text, 38)
            );
        END LOOP;

        FOR v_time_entry_id IN
            SELECT DISTINCT target_line.time_entry_id
            FROM billing_invoice_lines target_line
            WHERE target_line.billing_invoice_id = NEW.billing_invoice_id
              AND target_line.time_entry_id IS NOT NULL
            ORDER BY target_line.time_entry_id
        LOOP
            PERFORM pg_advisory_xact_lock(
                hashtextextended(v_time_entry_id::text, 0)
            );
        END LOOP;

        IF EXISTS (
            SELECT 1
            FROM billing_invoice_lines target_line
            JOIN billing_invoice_lines other_line
              ON other_line.time_entry_id = target_line.time_entry_id
             AND other_line.billing_invoice_id <> target_line.billing_invoice_id
            JOIN billing_invoices other_invoice
              ON other_invoice.billing_invoice_id = other_line.billing_invoice_id
            WHERE target_line.billing_invoice_id = NEW.billing_invoice_id
              AND target_line.time_entry_id IS NOT NULL
              AND lower(COALESCE(other_invoice.invoice_status, '')) <> 'void'
        )
        THEN
            RAISE EXCEPTION
                'Invoice % cannot be reactivated because replacement invoice lines exist.',
                NEW.billing_invoice_id;
        END IF;

        IF EXISTS (
            SELECT 1
            FROM billing_invoice_lines target_line
            JOIN billing_invoice_lines other_line
              ON other_line.billing_readiness_review_id = target_line.billing_readiness_review_id
             AND other_line.billing_invoice_id <> target_line.billing_invoice_id
            JOIN billing_invoices other_invoice
              ON other_invoice.billing_invoice_id = other_line.billing_invoice_id
            WHERE target_line.billing_invoice_id = NEW.billing_invoice_id
              AND target_line.billing_readiness_review_id IS NOT NULL
              AND lower(COALESCE(other_invoice.invoice_status, '')) <> 'void'
        )
        THEN
            RAISE EXCEPTION
                'Invoice % cannot be reactivated because a replacement non-labor package line exists.',
                NEW.billing_invoice_id;
        END IF;
    END IF;

    RETURN NEW;
END;
$projectpulse039_reactivation$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '039_work_to_cash_reactivation_lock_order',
    'Align invoice reactivation advisory-lock order with replacement line triggers',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
