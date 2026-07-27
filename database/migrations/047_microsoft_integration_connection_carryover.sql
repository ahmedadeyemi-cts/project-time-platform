-- ProjectPulse migration 047: make Module 065 the authoritative Microsoft Integration Connection.
-- Non-destructive carryover from Module 010 Azure/Entra settings and Module 067 mail settings.
BEGIN;

DO $projectpulse047_carryover$
DECLARE
    marker constant text := 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';
    azure_settings jsonb := '{}'::jsonb;
    legacy_mail jsonb := '{}'::jsonb;
    existing_document jsonb := NULL;
    existing_revision bigint := 0;
    environment_mode text;
    tenant_key text;
    tenant_name text;
    tenant_domain text;
    source_provider text;
    tenant_payload jsonb;
    inactive_tenant_payload jsonb;
    mail_payload jsonb;
    consolidated_payload jsonb;
    next_document jsonb;
    next_revision bigint;
    existing_notes text;
BEGIN
    IF to_regclass('public.azure_entra_settings') IS NULL THEN
        RAISE EXCEPTION 'Migration 019h must be applied before migration 047.';
    END IF;
    IF to_regclass('public.projectpulse_native_admin_documents') IS NULL
       OR to_regclass('public.projectpulse_native_admin_document_revisions') IS NULL THEN
        RAISE EXCEPTION 'Migration 032 must be applied before migration 047.';
    END IF;

    SELECT to_jsonb(settings)
    INTO azure_settings
    FROM azure_entra_settings settings
    ORDER BY settings.updated_at DESC NULLS LAST, settings.created_at DESC NULLS LAST
    LIMIT 1;
    azure_settings := COALESCE(azure_settings, '{}'::jsonb);

    SELECT COALESCE(document_json -> 'configuration', '{}'::jsonb)
    INTO legacy_mail
    FROM projectpulse_native_admin_documents
    WHERE module_number = '067'
      AND document_key = 'configuration'
    LIMIT 1;
    legacy_mail := COALESCE(legacy_mail, '{}'::jsonb);

    SELECT document_json, revision_number
    INTO existing_document, existing_revision
    FROM projectpulse_native_admin_documents
    WHERE module_number = '065'
      AND document_key = 'configuration'
    LIMIT 1;

    environment_mode := CASE
        WHEN lower(COALESCE(
            azure_settings ->> 'environment_mode',
            azure_settings ->> 'environmentMode',
            azure_settings ->> 'source_provider',
            azure_settings ->> 'sourceProvider',
            azure_settings ->> 'tenant_domain',
            azure_settings ->> 'tenantDomain',
            azure_settings ->> 'redirect_uri',
            azure_settings ->> 'redirectUri',
            '')) ~ '(prod|ussignal\.com)'
          AND lower(COALESCE(azure_settings ->> 'redirect_uri', azure_settings ->> 'redirectUri', '')) !~ '(test|onenecklab)'
            THEN 'production'
        ELSE 'test'
    END;

    tenant_key := CASE WHEN environment_mode = 'production' THEN 'ussignal' ELSE 'onenecklab' END;
    tenant_name := COALESCE(
        NULLIF(azure_settings ->> 'tenant_name', ''),
        NULLIF(azure_settings ->> 'tenantName', ''),
        CASE WHEN environment_mode = 'production' THEN 'US Signal Production' ELSE 'OneNeck Lab Test' END);
    tenant_domain := COALESCE(
        NULLIF(azure_settings ->> 'tenant_domain', ''),
        NULLIF(azure_settings ->> 'tenantDomain', ''),
        CASE WHEN environment_mode = 'production' THEN 'ussignal.com' ELSE 'onenecklab.com' END);
    source_provider := COALESCE(
        NULLIF(azure_settings ->> 'source_provider', ''),
        NULLIF(azure_settings ->> 'sourceProvider', ''),
        CASE WHEN environment_mode = 'production' THEN 'ENTRA_ID' ELSE 'ENTRA_ID_TEST' END);

    tenant_payload := jsonb_build_object(
        'key', tenant_key,
        'name', tenant_name,
        'environmentMode', environment_mode,
        'tenantDomain', tenant_domain,
        'tenantId', COALESCE(azure_settings ->> 'tenant_id', azure_settings ->> 'tenantId', ''),
        'sourceProvider', source_provider,
        'directorySyncEnabled', COALESCE((azure_settings ->> 'sync_enabled')::boolean, false),
        'syncFrequencyHours', COALESCE(NULLIF(azure_settings ->> 'sync_frequency_hours', '')::integer, 24),
        'defaultRoleCode', COALESCE(NULLIF(azure_settings ->> 'default_role_code', ''), NULLIF(azure_settings ->> 'defaultRoleCode', ''), 'ENGINEER'),
        'sso', jsonb_build_object(
            'connectionPurpose', 'sso_app_registration',
            'clientId', '',
            'authorityUrl', COALESCE(azure_settings ->> 'authority_url', azure_settings ->> 'authorityUrl', ''),
            'redirectUri', COALESCE(azure_settings ->> 'redirect_uri', azure_settings ->> 'redirectUri', ''),
            'allowedDomains', CASE WHEN environment_mode = 'production' THEN 'ussignal.com' ELSE 'onenecklab.com,onitdemo.com' END
        ),
        'services', jsonb_build_object(
            'connectionPurpose', 'microsoft_services_enterprise_application',
            'clientId', COALESCE(azure_settings ->> 'client_id', azure_settings ->> 'clientId', ''),
            'graphScopes', COALESCE(NULLIF(azure_settings ->> 'graph_scope', ''), NULLIF(azure_settings ->> 'graphScope', ''), 'User.Read.All Directory.Read.All')
        ),
        'legacyDirectorySettingsCarriedOver', true
    );

    inactive_tenant_payload := jsonb_build_object(
        'key', CASE WHEN environment_mode = 'production' THEN 'onenecklab' ELSE 'ussignal' END,
        'name', CASE WHEN environment_mode = 'production' THEN 'OneNeck Lab Test' ELSE 'US Signal Production' END,
        'environmentMode', CASE WHEN environment_mode = 'production' THEN 'test' ELSE 'production' END,
        'tenantDomain', CASE WHEN environment_mode = 'production' THEN 'onenecklab.com' ELSE 'ussignal.com' END,
        'tenantId', '',
        'sourceProvider', CASE WHEN environment_mode = 'production' THEN 'ENTRA_ID_TEST' ELSE 'ENTRA_ID' END,
        'directorySyncEnabled', false,
        'syncFrequencyHours', 24,
        'defaultRoleCode', 'ENGINEER',
        'sso', jsonb_build_object(
            'connectionPurpose', 'sso_app_registration',
            'clientId', '',
            'authorityUrl', '',
            'redirectUri', '',
            'allowedDomains', CASE WHEN environment_mode = 'production' THEN 'onenecklab.com,onitdemo.com' ELSE 'ussignal.com' END
        ),
        'services', jsonb_build_object(
            'connectionPurpose', 'microsoft_services_enterprise_application',
            'clientId', '',
            'graphScopes', 'User.Read.All Directory.Read.All'
        )
    );

    mail_payload := jsonb_build_object(
        'providerTarget', COALESCE(NULLIF(legacy_mail ->> 'providerTarget', ''), 'microsoft_graph'),
        'smtpHost', COALESCE(NULLIF(legacy_mail ->> 'smtpHost', ''), 'smtp.office365.com'),
        'smtpPort', CASE
            WHEN COALESCE(legacy_mail ->> 'smtpPort', '') ~ '^[0-9]+$' THEN (legacy_mail ->> 'smtpPort')::integer
            ELSE 587
        END,
        'senderName', COALESCE(legacy_mail ->> 'senderName', ''),
        'senderAddress', COALESCE(legacy_mail ->> 'senderAddress', ''),
        'replyToAddress', COALESCE(legacy_mail ->> 'replyToAddress', ''),
        'recipientBoundary', COALESCE(NULLIF(legacy_mail ->> 'recipientBoundary', ''), 'test_only'),
        'legacyModule067ConfigurationCarriedOver', legacy_mail <> '{}'::jsonb
    );

    consolidated_payload := jsonb_build_object(
        'activeTenantKey', tenant_key,
        'activeEnvironmentMode', environment_mode,
        'tenants', CASE
            WHEN environment_mode = 'test' THEN jsonb_build_array(tenant_payload, inactive_tenant_payload)
            ELSE jsonb_build_array(inactive_tenant_payload, tenant_payload)
        END,
        'mail', mail_payload,
        'connectionOwnership', jsonb_build_object(
            'module010DirectoryImport', 'services',
            'module057CalendarPresence', 'services',
            'module062IdentityProfile', 'services',
            'globalMailTransport', 'services',
            'interactiveSso', 'sso'
        ),
        'carryoverMigration', '047_microsoft_integration_connection_carryover'
    );

    existing_notes := COALESCE(existing_document -> 'configuration' ->> 'notes', '');
    IF existing_notes NOT LIKE marker || '%' THEN
        next_document := COALESCE(existing_document, '{}'::jsonb)
            || jsonb_build_object(
                'configuration',
                COALESCE(existing_document -> 'configuration', '{}'::jsonb)
                || jsonb_build_object(
                    'applicationId', COALESCE(NULLIF(existing_document -> 'configuration' ->> 'applicationId', ''), azure_settings ->> 'client_id', azure_settings ->> 'clientId', ''),
                    'tenantId', COALESCE(NULLIF(existing_document -> 'configuration' ->> 'tenantId', ''), azure_settings ->> 'tenant_id', azure_settings ->> 'tenantId', ''),
                    'ownerTeam', COALESCE(NULLIF(existing_document -> 'configuration' ->> 'ownerTeam', ''), 'Platform Administration'),
                    'notes', marker || consolidated_payload::text
                )
            );
        next_revision := COALESCE(existing_revision, 0) + 1;

        INSERT INTO projectpulse_native_admin_documents (
            module_number, document_key, document_json, revision_number, updated_by, updated_at
        ) VALUES (
            '065', 'configuration', next_document, next_revision, NULL, NOW()
        )
        ON CONFLICT (module_number, document_key)
        DO UPDATE SET
            document_json = EXCLUDED.document_json,
            revision_number = EXCLUDED.revision_number,
            updated_at = NOW();

        INSERT INTO projectpulse_native_admin_document_revisions (
            revision_id, module_number, document_key, revision_number, document_json,
            saved_by, saved_at, change_reason, restored_from_revision_id
        ) VALUES (
            gen_random_uuid(), '065', 'configuration', next_revision, next_document,
            NULL, NOW(), 'save', NULL
        )
        ON CONFLICT (module_number, document_key, revision_number) DO NOTHING;

        IF to_regclass('public.microsoft_integration_audit_events') IS NOT NULL THEN
            INSERT INTO microsoft_integration_audit_events (
                actor_user_id, actor_email, action_code, tenant_key, outcome_code,
                correlation_id, event_metadata, created_at
            ) VALUES (
                NULL,
                'migration@projectpulse.local',
                'LEGACY_CONFIGURATION_CARRIED_OVER',
                tenant_key,
                'success',
                'migration-047',
                jsonb_build_object(
                    'module010TenantMetadataCarriedOver', true,
                    'module067MailConfigurationCarriedOver', legacy_mail <> '{}'::jsonb,
                    'secretValuesRead', false,
                    'secretValuesChanged', false,
                    'sourceTablesDeleted', false
                ),
                NOW()
            );
        END IF;
    END IF;
END;
$projectpulse047_carryover$;

DO $projectpulse047_catalog$
BEGIN
    IF to_regclass('public.scoped_role_policy_modules') IS NOT NULL THEN
        UPDATE scoped_role_policy_modules
        SET module_name = 'Microsoft Integration Connection',
            current_state = 'Installed authoritative Microsoft connection center',
            permission_notes = 'Single interface for Test and Production SSO, Graph, Module 010 directory import, Module 057 calendar/presence, Module 062 identity/profile, and Microsoft 365/SMTP configuration.',
            is_active = TRUE
        WHERE module_code = '065';
    END IF;

    IF to_regclass('public.app_feature_catalog') IS NOT NULL THEN
        UPDATE app_feature_catalog
        SET feature_name = 'Microsoft Integration Connection',
            feature_description = 'Authoritative Test and Production Microsoft SSO, Graph, identity, directory, calendar, presence, and global mail connection configuration.',
            updated_at = NOW()
        WHERE route_anchor = '#entra-secret-administration'
           OR feature_code IN ('ENTRA_SECRET_ADMINISTRATION', 'MICROSOFT_INTEGRATION');
    END IF;
END;
$projectpulse047_catalog$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '047_microsoft_integration_connection_carryover',
    'Carry existing Module 010 Azure/Entra and Module 067 global mail configuration into authoritative Module 065 Microsoft Integration Connection without changing secrets or deleting legacy evidence',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
