-- Roll back ProjectPulse Work-to-Cash lifecycle persistence and unified audit.

BEGIN;

DROP TRIGGER IF EXISTS trg_projectpulse038_invoice_audit
    ON billing_invoice_events;
DROP TRIGGER IF EXISTS trg_projectpulse038_work_register_audit
    ON work_register_change_history;
DROP TRIGGER IF EXISTS trg_projectpulse038_audit_immutable
    ON work_lifecycle_audit_events;

DROP FUNCTION IF EXISTS projectpulse038_capture_invoice_event();
DROP FUNCTION IF EXISTS projectpulse038_capture_work_register_change();
DROP FUNCTION IF EXISTS projectpulse038_reject_audit_mutation();

DROP TABLE IF EXISTS work_lifecycle_audit_events;
DROP TABLE IF EXISTS work_closeout_records;
DROP TABLE IF EXISTS work_billing_readiness_reviews;

DELETE FROM schema_migrations
WHERE migration_id = '038_work_to_cash_lifecycle_and_audit';

COMMIT;
