-- ProjectPulse migration 034 rollback.
-- Destructive: removes saved integration credentials, OAuth state, and status history.
-- Run only through an approved rollback window after a verified backup.

BEGIN;

DELETE FROM app_feature_catalog
WHERE feature_code = 'CRM_ERP_INTEGRATIONS_026';

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN ('VIEW_INTEGRATIONS_026', 'MANAGE_INTEGRATIONS_026')
);

DELETE FROM app_permissions
WHERE permission_code IN ('VIEW_INTEGRATIONS_026', 'MANAGE_INTEGRATIONS_026');

DROP TABLE IF EXISTS crm_integration_connection_checks;
DROP TABLE IF EXISTS crm_integration_oauth_states;
DROP TABLE IF EXISTS crm_integration_credentials;

DELETE FROM crm_integration_providers
WHERE provider_key IN ('certinia', 'servicenow')
  AND is_builtin = TRUE;

UPDATE crm_integration_providers
SET provider_name = CASE provider_key
        WHEN 'salesforce' THEN 'Salesforce'
        WHEN 'zendesk_sell' THEN 'Zendesk Sell'
        ELSE provider_name
    END,
    provider_type = CASE provider_key
        WHEN 'salesforce' THEN 'crm_opportunity_platform'
        WHEN 'zendesk_sell' THEN 'crm_deal_platform'
        ELSE provider_type
    END,
    provider_status = 'placeholder',
    auth_model = CASE provider_key
        WHEN 'salesforce' THEN 'oauth_server_side'
        WHEN 'zendesk_sell' THEN 'api_or_oauth_server_side'
        ELSE auth_model
    END,
    configuration_scope = 'backend_configuration_only',
    secret_storage_policy = 'no_repository_secrets',
    updated_at = NOW()
WHERE provider_key IN ('salesforce', 'zendesk_sell');

ALTER TABLE crm_integration_providers
    DROP CONSTRAINT IF EXISTS crm_integration_providers_availability_status_check,
    DROP CONSTRAINT IF EXISTS crm_integration_providers_auth_model_check,
    DROP COLUMN IF EXISTS base_url,
    DROP COLUMN IF EXISTS health_check_url,
    DROP COLUMN IF EXISTS oauth_authorization_url,
    DROP COLUMN IF EXISTS oauth_token_url,
    DROP COLUMN IF EXISTS oauth_client_id,
    DROP COLUMN IF EXISTS oauth_scopes,
    DROP COLUMN IF EXISTS api_key_header,
    DROP COLUMN IF EXISTS api_key_prefix,
    DROP COLUMN IF EXISTS is_builtin,
    DROP COLUMN IF EXISTS is_enabled,
    DROP COLUMN IF EXISTS availability_status,
    DROP COLUMN IF EXISTS last_checked_at,
    DROP COLUMN IF EXISTS last_available_at,
    DROP COLUMN IF EXISTS last_status_code,
    DROP COLUMN IF EXISTS last_error_code,
    DROP COLUMN IF EXISTS created_by,
    DROP COLUMN IF EXISTS updated_by;

DELETE FROM schema_migrations
WHERE migration_id = '034_module_026_crm_erp_integrations';

COMMIT;
