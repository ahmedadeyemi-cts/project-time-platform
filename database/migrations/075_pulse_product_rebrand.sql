-- Pulse migration 075
-- Changes future invoice identities to the Pulse brand while preserving every
-- immutable invoice number issued under the legacy prefix.

BEGIN;

DO $pulse075_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.billing_invoices') IS NULL
       OR to_regclass('public.project_billing_profiles') IS NULL
       OR to_regclass('public.billing_invoice_series_seq') IS NULL THEN
        RAISE EXCEPTION 'Migration 075 requires the billing integration foundation.';
    END IF;
END;
$pulse075_prerequisites$;

ALTER TABLE billing_invoices
    DROP CONSTRAINT IF EXISTS ck_billing_invoices_number_format;

ALTER TABLE billing_invoices
    ADD CONSTRAINT ck_billing_invoices_number_format
    CHECK (invoice_number ~ '^(PHD|PULSE)-[0-9]{6,}-[1-9][0-9]*$');

CREATE OR REPLACE FUNCTION reserve_project_invoice_number(
    p_project_id UUID
)
RETURNS TABLE (
    reserved_series_number BIGINT,
    reserved_installment_number INTEGER,
    reserved_invoice_number TEXT
)
LANGUAGE plpgsql
VOLATILE
SET search_path = public
AS $pulse075_reserve$
DECLARE
    v_series_number BIGINT;
    v_installment_number INTEGER;
BEGIN
    IF p_project_id IS NULL THEN
        RAISE EXCEPTION 'Project ID is required.';
    END IF;

    INSERT INTO project_billing_profiles (
        project_id,
        invoice_series_number
    )
    VALUES (
        p_project_id,
        nextval('billing_invoice_series_seq')
    )
    ON CONFLICT (project_id) DO NOTHING;

    SELECT profile.invoice_series_number
    INTO v_series_number
    FROM project_billing_profiles AS profile
    WHERE profile.project_id = p_project_id
    FOR UPDATE;

    IF v_series_number IS NULL THEN
        UPDATE project_billing_profiles
        SET invoice_series_number = nextval('billing_invoice_series_seq'),
            updated_at = NOW()
        WHERE project_id = p_project_id
          AND invoice_series_number IS NULL
        RETURNING invoice_series_number
        INTO v_series_number;
    END IF;

    IF v_series_number IS NULL THEN
        SELECT profile.invoice_series_number
        INTO v_series_number
        FROM project_billing_profiles AS profile
        WHERE profile.project_id = p_project_id;
    END IF;

    IF v_series_number IS NULL THEN
        RAISE EXCEPTION 'Unable to allocate an invoice series for project %.', p_project_id;
    END IF;

    SELECT COALESCE(MAX(invoice.invoice_installment_number), 0) + 1
    INTO v_installment_number
    FROM billing_invoices AS invoice
    WHERE invoice.project_id = p_project_id;

    RETURN QUERY
    SELECT
        v_series_number,
        v_installment_number,
        'PULSE-'
        || LPAD(v_series_number::TEXT, 6, '0')
        || '-'
        || v_installment_number::TEXT;
END;
$pulse075_reserve$;

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '075_pulse_product_rebrand',
    'Rebrand future immutable invoice identities to Pulse while retaining legacy invoice compatibility.',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
