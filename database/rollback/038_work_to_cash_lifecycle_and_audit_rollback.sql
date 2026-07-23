-- Roll back ProjectPulse Work-to-Cash lifecycle persistence and unified audit.

BEGIN;

DROP TRIGGER IF EXISTS trg_projectpulse038_invoice_reactivation
    ON billing_invoices;
DROP TRIGGER IF EXISTS trg_projectpulse038_live_time_entry_line
    ON billing_invoice_lines;
DROP TRIGGER IF EXISTS trg_projectpulse038_invoice_audit
    ON billing_invoice_events;
DROP TRIGGER IF EXISTS trg_projectpulse038_work_register_audit
    ON work_register_change_history;
DROP TRIGGER IF EXISTS trg_projectpulse038_audit_immutable
    ON work_lifecycle_audit_events;

DROP FUNCTION IF EXISTS projectpulse038_capture_invoice_event();
DROP FUNCTION IF EXISTS projectpulse038_capture_work_register_change();
DROP FUNCTION IF EXISTS projectpulse038_reject_audit_mutation();
DROP FUNCTION IF EXISTS projectpulse038_guard_invoice_reactivation();
DROP FUNCTION IF EXISTS projectpulse038_guard_live_time_entry_line();

DROP INDEX IF EXISTS uq_billing_invoice_lines_invoice_time_entry;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM billing_invoice_lines
        WHERE time_entry_id IS NOT NULL
        GROUP BY time_entry_id
        HAVING COUNT(*) > 1
    )
    THEN
        RAISE EXCEPTION
            'Migration 038 cannot be rolled back after a voided time entry has been re-invoiced.';
    END IF;
END;
$$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_billing_invoice_lines_time_entry
    ON billing_invoice_lines(time_entry_id)
    WHERE time_entry_id IS NOT NULL;

DROP TABLE IF EXISTS work_lifecycle_audit_events;
DROP TABLE IF EXISTS work_closeout_records;
DROP TABLE IF EXISTS work_billing_readiness_reviews;

DELETE FROM schema_migrations
WHERE migration_id = '038_work_to_cash_lifecycle_and_audit';

COMMIT;
