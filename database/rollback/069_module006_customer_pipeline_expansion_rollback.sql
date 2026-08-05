-- Project Health Dashboard migration 069 rollback
-- Reinstates the Migration 068 Toyota/Hyundai-only customer constraint.
-- The rollback refuses to proceed after additional-customer data has been saved.

BEGIN;

DO $projectpulse069_rollback_guard$
BEGIN
    IF to_regclass('public.module006_pipeline_records') IS NULL THEN
        RAISE EXCEPTION 'Migration 069 rollback requires module006_pipeline_records.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM module006_pipeline_records
        WHERE lower(btrim(customer)) NOT IN ('toyota', 'hyundai')
    ) THEN
        RAISE EXCEPTION 'Migration 069 rollback is blocked because additional-customer Module 006 records exist.';
    END IF;
END;
$projectpulse069_rollback_guard$;

LOCK TABLE module006_pipeline_records IN SHARE ROW EXCLUSIVE MODE;

DROP INDEX IF EXISTS ix_module006_pipeline_records_customer_name;

ALTER TABLE module006_pipeline_records
    DROP CONSTRAINT IF EXISTS ck_module006_pipeline_records_customer_name;
ALTER TABLE module006_pipeline_records
    DROP CONSTRAINT IF EXISTS module006_pipeline_records_customer_check;

ALTER TABLE module006_pipeline_records
    ADD CONSTRAINT module006_pipeline_records_customer_check
    CHECK (lower(customer) IN ('toyota', 'hyundai'));

DELETE FROM schema_migrations
WHERE migration_id = '069_module006_customer_pipeline_expansion';

COMMIT;
