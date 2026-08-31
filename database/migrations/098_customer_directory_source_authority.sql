-- ProjectPulse Modules 021, 026, 039, and 042
-- Establish a single customer-source authority so customer-facing modules can use
-- SELL, another configured Module 026 CRM/ERP provider, or locally managed customers.
-- SELL remains the default for backward compatibility until an authorized user changes it.

BEGIN;

DO $$
BEGIN
    IF to_regclass('public.crm_integration_providers') IS NULL THEN
        RAISE EXCEPTION 'Migration 034 must be applied before migration 098.';
    END IF;
    IF to_regclass('public.customer_directory_source_links') IS NULL
       OR to_regclass('public.customer_directory_sync_runs') IS NULL THEN
        RAISE EXCEPTION 'Migration 049 must be applied before migration 098.';
    END IF;
END $$;

-- Custom provider keys are not constrained to 50 characters. Keep source lineage
-- lossless when Module 021 uses a non-SELL Module 026 provider.
ALTER TABLE customer_directory_source_links
    ALTER COLUMN source_system TYPE VARCHAR(120);

ALTER TABLE customer_directory_sync_runs
    ALTER COLUMN source_system TYPE VARCHAR(120);

CREATE TABLE IF NOT EXISTS customer_directory_source_authority (
    customer_source_authority_id SMALLINT PRIMARY KEY DEFAULT 1,
    source_mode TEXT NOT NULL DEFAULT 'sell',
    provider_key TEXT REFERENCES crm_integration_providers(provider_key),
    updated_by UUID REFERENCES app_users(user_id),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_customer_directory_source_authority_singleton
        CHECK (customer_source_authority_id = 1),
    CONSTRAINT ck_customer_directory_source_authority_mode
        CHECK (source_mode IN ('sell', 'crm', 'manual')),
    CONSTRAINT ck_customer_directory_source_authority_provider
        CHECK (
            (source_mode = 'sell' AND provider_key = 'zendesk_sell')
            OR (source_mode = 'crm' AND provider_key IS NOT NULL AND provider_key <> 'zendesk_sell')
            OR (source_mode = 'manual' AND provider_key IS NULL)
        )
);

CREATE TABLE IF NOT EXISTS customer_directory_source_authority_history (
    customer_source_authority_history_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    previous_source_mode TEXT NOT NULL,
    previous_provider_key TEXT,
    next_source_mode TEXT NOT NULL,
    next_provider_key TEXT,
    changed_by UUID REFERENCES app_users(user_id),
    changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_customer_directory_source_authority_history_modes
        CHECK (
            previous_source_mode IN ('sell', 'crm', 'manual')
            AND next_source_mode IN ('sell', 'crm', 'manual')
        )
);

INSERT INTO customer_directory_source_authority (
    customer_source_authority_id,
    source_mode,
    provider_key,
    updated_at
)
VALUES (1, 'sell', 'zendesk_sell', NOW())
ON CONFLICT (customer_source_authority_id) DO NOTHING;

CREATE INDEX IF NOT EXISTS ix_customer_directory_source_authority_history_changed_at
    ON customer_directory_source_authority_history(changed_at DESC);

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '098_customer_directory_source_authority',
    'Configurable Module 021 customer source authority for SELL, Module 026 CRM/ERP providers, or manual customer management with downstream source-aware readiness',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

DO $grant_runtime_role$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON TABLE customer_directory_source_authority TO "ptp_app"';
        EXECUTE 'GRANT SELECT, INSERT ON TABLE customer_directory_source_authority_history TO "ptp_app"';
        RAISE NOTICE 'Migration 098 granted optional compatibility privileges to ptp_app.';
    ELSE
        RAISE NOTICE 'Migration 098 skipped optional ptp_app grants because the role is not installed; the current migration role owns the tables.';
    END IF;
END
$grant_runtime_role$;

COMMIT;
