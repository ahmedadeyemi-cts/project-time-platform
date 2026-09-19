-- Correct the vendor identity without rewriting retained credentials or evidence.
BEGIN;
SELECT pg_advisory_xact_lock(260111);

INSERT INTO crm_integration_providers (
    provider_key, provider_name, provider_type, provider_status, auth_model,
    configuration_scope, secret_storage_policy, base_url, health_check_url,
    api_key_header, api_key_prefix, record_lookup_url_template, import_mapping_json,
    is_builtin, is_enabled, availability_status, supports_accounts,
    supports_opportunities, supports_quotes, supports_attachments, notes
) VALUES (
    'connectwise_sell', 'ConnectWise SELL', 'crm', 'native_configuration', 'api_key',
    'server_side_only', 'encrypted_write_only', 'https://sellapi.quosalsell.com',
    'https://sellapi.quosalsell.com/api/quotes?page=1&pageSize=1', 'Authorization', 'Basic',
    'https://sellapi.quosalsell.com/api/quotes/{recordId}',
    '{"projectNamePath":"name","quoteNumberPath":"quoteNumber","customerNamePath":"accountName","contractedAmountPath":"quoteTotal"}'::jsonb,
    TRUE, FALSE, 'not_configured', FALSE, FALSE, TRUE, FALSE,
    'ConnectWise SELL (CPQ). Add the access key, public key and private key in Module 026. The connection test verifies quote API access only; customer sync, labor-rate intake and document publishing need separate adapters.'
) ON CONFLICT (provider_key) DO NOTHING;

-- AES-GCM associated data contains the provider key. Never move or copy old
-- ciphertext, OAuth state, checks, customer links, or SOW submission records.
UPDATE crm_integration_providers
SET is_enabled = FALSE, is_builtin = FALSE, availability_status = 'disabled',
    updated_at = NOW()
WHERE provider_key = 'zendesk_sell' AND (is_enabled OR is_builtin OR availability_status <> 'disabled');

ALTER TABLE customer_directory_source_authority
    DROP CONSTRAINT IF EXISTS ck_customer_directory_source_authority_provider;
INSERT INTO customer_directory_source_authority_history (
    previous_source_mode, previous_provider_key, next_source_mode, next_provider_key
)
SELECT source_mode, provider_key, source_mode, 'connectwise_sell'
FROM customer_directory_source_authority
WHERE source_mode = 'sell' AND provider_key = 'zendesk_sell';
UPDATE customer_directory_source_authority
SET provider_key = 'connectwise_sell', updated_at = NOW()
WHERE source_mode = 'sell' AND provider_key = 'zendesk_sell';
ALTER TABLE customer_directory_source_authority
    ADD CONSTRAINT ck_customer_directory_source_authority_provider CHECK (
        (source_mode = 'sell' AND provider_key = 'connectwise_sell')
        OR (source_mode = 'crm' AND provider_key IS NOT NULL AND provider_key <> 'connectwise_sell')
        OR (source_mode = 'manual' AND provider_key IS NULL)
    );

-- Keep old destinations valid so immutable receipts retain their actual origin.
ALTER TABLE module025_sow_sell_submissions
    DROP CONSTRAINT IF EXISTS module025_sow_sell_submissions_destination_key_check;
ALTER TABLE module025_sow_sell_submissions
    ADD CONSTRAINT module025_sow_sell_submissions_destination_key_check
    CHECK (destination_key IN ('zendesk_sell', 'connectwise_sell'));

DO $$ BEGIN
    IF to_regclass('public.reporting_external_connection_catalog') IS NOT NULL THEN
        INSERT INTO reporting_external_connection_catalog
            (connection_key, connection_name, connection_type, provider_category, operational_owner)
        VALUES ('connectwise_sell', 'ConnectWise SELL', 'CRM', 'External quoting platform', 'Sales Operations')
        ON CONFLICT (connection_key) DO NOTHING;
        UPDATE reporting_external_connection_catalog
        SET connection_name = 'Retired sales connector'
        WHERE connection_key = 'zendesk_sell';
    END IF;
    IF to_regclass('public.reporting_data_domains') IS NOT NULL THEN
        UPDATE reporting_data_domains SET domain_description = replace(domain_description, 'Zendesk Sell', 'ConnectWise SELL')
        WHERE domain_description LIKE '%Zendesk Sell%';
    END IF;
    IF to_regclass('public.reporting_filter_catalog') IS NOT NULL THEN
        UPDATE reporting_filter_catalog SET role_scope_notes = replace(role_scope_notes, 'Zendesk Sell', 'ConnectWise SELL')
        WHERE role_scope_notes LIKE '%Zendesk Sell%';
    END IF;
    IF to_regclass('public.reporting_templates') IS NOT NULL THEN
        UPDATE reporting_templates SET description = replace(description, 'Zendesk Sell', 'ConnectWise SELL')
        WHERE description LIKE '%Zendesk Sell%';
    END IF;
END $$;
COMMIT;
