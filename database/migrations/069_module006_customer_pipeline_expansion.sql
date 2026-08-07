-- Pulse migration 069
-- Expand Module 006 from the reviewed Toyota/Hyundai baseline to governed customer pipelines.
-- Existing Toyota and Hyundai records and append-only history remain unchanged.

BEGIN;

DO $projectpulse069_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.module006_pipeline_records') IS NULL THEN
        RAISE EXCEPTION 'Migration 069 requires schema_migrations and module006_pipeline_records.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '068_module006_standalone_pipeline_management'
    ) THEN
        RAISE EXCEPTION 'Migration 069 requires Migration 068.';
    END IF;
END;
$projectpulse069_prerequisites$;

LOCK TABLE module006_pipeline_records IN SHARE ROW EXCLUSIVE MODE;

-- Migration 068 intentionally began with a Toyota/Hyundai-only boundary. Replace
-- that constraint with a bounded, trimmed customer-name contract. The API also
-- performs the same validation before persistence.
ALTER TABLE module006_pipeline_records
    DROP CONSTRAINT IF EXISTS module006_pipeline_records_customer_check;
ALTER TABLE module006_pipeline_records
    DROP CONSTRAINT IF EXISTS ck_module006_pipeline_records_customer_name;

ALTER TABLE module006_pipeline_records
    ADD CONSTRAINT ck_module006_pipeline_records_customer_name
    CHECK (
        customer = btrim(customer)
        AND char_length(customer) BETWEEN 2 AND 120
        AND customer !~ '[[:cntrl:]]'
    );

CREATE INDEX IF NOT EXISTS ix_module006_pipeline_records_customer_name
    ON module006_pipeline_records (upper(customer), is_archived, updated_at DESC);

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '069_module006_customer_pipeline_expansion',
    'Allow governed Module 006 pipeline records for additional customers while preserving the reviewed Toyota and Hyundai baseline',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
