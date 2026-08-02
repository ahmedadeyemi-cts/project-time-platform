using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 026 administration experience compatibility.
///
/// The historical Module 026 handlers remain authoritative for encrypted
/// credentials, OAuth, provider tests, validation, audit evidence, and SSRF
/// controls. This partial supplies the current dynamic-RBAC decision and
/// returns editable built-in templates when the provider table has not yet
/// been seeded in a restored or partially migrated environment.
/// </summary>
public static partial class CrmErpIntegrationModule
{
    private static readonly BuiltinProviderTemplate[] BuiltinProviderTemplates =
    [
        new(
            "zendesk_sell",
            "SELL (Zendesk Sell)",
            "crm",
            "api_key",
            "https://api.getbase.com",
            "https://api.getbase.com/v2/contacts?per_page=1",
            "https://api.getbase.com/oauth2/authorize",
            "https://api.getbase.com/oauth2/token",
            string.Empty,
            "read profile",
            "Authorization",
            "Bearer",
            "https://api.getbase.com/v2/deals/{recordId}",
            "{\"projectNamePath\":\"data.name\",\"quoteNumberPath\":\"data.id\",\"customerNamePath\":\"data.organization_name\",\"contractedAmountPath\":\"data.value\",\"rateLinesPath\":\"data.custom_fields.pricing_rate_review\",\"rateCodePath\":\"sku\",\"descriptionPath\":\"description\",\"unitRatePath\":\"unit_rate\",\"laborCategoryPath\":\"labor_category\",\"timeTypePath\":\"time_type\",\"unitTypePath\":\"unit_type\",\"billablePath\":\"billable\"}",
            "Authoritative customer, organization, deal, quote, and pricing source for ProjectPulse."),
        new(
            "salesforce",
            "Salesforce",
            "crm",
            "oauth2",
            "https://login.salesforce.com",
            "https://login.salesforce.com/services/oauth2/userinfo",
            "https://login.salesforce.com/services/oauth2/authorize",
            "https://login.salesforce.com/services/oauth2/token",
            string.Empty,
            "api refresh_token",
            "Authorization",
            "Bearer",
            string.Empty,
            "{}",
            "CRM account, contact, opportunity, and pipeline integration using a Salesforce Connected App."),
        new(
            "servicenow",
            "ServiceNow",
            "itsm_erp",
            "oauth2",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            "Authorization",
            "Bearer",
            string.Empty,
            "{}",
            "ITSM customer, request, incident, change, and service-delivery integration for an approved instance."),
        new(
            "certinia",
            "Certinia",
            "erp_psa",
            "oauth2",
            "https://login.salesforce.com",
            "https://login.salesforce.com/services/oauth2/userinfo",
            "https://login.salesforce.com/services/oauth2/authorize",
            "https://login.salesforce.com/services/oauth2/token",
            string.Empty,
            "api refresh_token",
            "Authorization",
            "Bearer",
            string.Empty,
            "{}",
            "ERP/PSA project, billing, resource, and financial integration through the Salesforce platform.")
    ];

    private static async Task<IResult> ListProvidersAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted))
            return SchemaUnavailable();

        var providers = new List<ProviderAdministrationRow>();
        await using (var command = new NpgsqlCommand("""
            SELECT
                p.provider_key,
                p.provider_name,
                p.provider_type,
                p.auth_model,
                p.base_url,
                p.health_check_url,
                p.oauth_authorization_url,
                p.oauth_token_url,
                p.oauth_client_id,
                p.oauth_scopes,
                p.api_key_header,
                p.api_key_prefix,
                p.record_lookup_url_template,
                p.import_mapping_json::text,
                p.is_builtin,
                p.is_enabled,
                p.availability_status,
                p.last_checked_at,
                p.last_available_at,
                p.last_status_code,
                p.last_error_code,
                p.notes,
                EXISTS (
                    SELECT 1
                    FROM crm_integration_credentials c
                    WHERE c.provider_key = p.provider_key
                      AND c.credential_kind = CASE
                          WHEN p.auth_model = 'api_key' THEN 'api_key'
                          ELSE 'oauth_client_secret'
                      END
                ) AS credential_configured,
                EXISTS (
                    SELECT 1
                    FROM crm_integration_credentials c
                    WHERE c.provider_key = p.provider_key
                      AND c.credential_kind = 'oauth_token'
                ) AS oauth_connected
            FROM crm_integration_providers p
            ORDER BY
                CASE p.provider_key
                    WHEN 'zendesk_sell' THEN 10
                    WHEN 'salesforce' THEN 20
                    WHEN 'servicenow' THEN 30
                    WHEN 'certinia' THEN 40
                    ELSE 100
                END,
                lower(p.provider_name),
                p.provider_key;
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(context.RequestAborted))
        {
            while (await reader.ReadAsync(context.RequestAborted))
            {
                providers.Add(new ProviderAdministrationRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetBoolean(14),
                    true,
                    reader.GetBoolean(15),
                    reader.GetString(16),
                    reader.IsDBNull(17) ? null : reader.GetDateTime(17),
                    reader.IsDBNull(18) ? null : reader.GetDateTime(18),
                    reader.IsDBNull(19) ? null : reader.GetInt32(19),
                    reader.GetString(20),
                    reader.GetString(21),
                    reader.GetBoolean(22),
                    reader.GetBoolean(23),
                    false));
            }
        }

        var persistedKeys = providers
            .Select(provider => provider.ProviderKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var template in BuiltinProviderTemplates)
        {
            if (persistedKeys.Contains(template.ProviderKey)) continue;
            providers.Add(template.ToAdministrationRow());
        }

        providers = providers
            .OrderBy(provider => BuiltinOrder(provider.ProviderKey))
            .ThenBy(provider => provider.ProviderName)
            .ToList();

        var authority = await ResolveManageAuthorityAsync(context);
        var persistedCount = providers.Count(provider => provider.IsPersisted);
        var configuredCount = providers.Count(provider =>
            provider.CredentialConfigured || provider.OauthConnected);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "providers_loaded",
            contractVersion = "module-026-administration-v2-2026-07-28",
            generatedAt = DateTimeOffset.UtcNow,
            access = new
            {
                canView = true,
                canManage = authority.Allowed,
                manageAuthoritySource = authority.Source,
                manageMessage = authority.Message,
                requiredPermission = "MANAGE_INTEGRATIONS_026",
                dynamicAction = "MODULE_CONFIGURE",
                isViewAs = IsViewAs(context),
                viewAsTransfersMutationAuthority = false
            },
            initialization = new
            {
                builtInTemplateCount = BuiltinProviderTemplates.Length,
                persistedProviderCount = persistedCount,
                virtualTemplateCount = providers.Count(provider => !provider.IsPersisted),
                firstSaveCreatesProvider = true,
                migrationSeedRequiredForDisplay = false
            },
            summary = new
            {
                registered = providers.Count,
                persisted = persistedCount,
                configured = configuredCount,
                available = providers.Count(provider =>
                    string.Equals(
                        provider.AvailabilityStatus,
                        "available",
                        StringComparison.OrdinalIgnoreCase))
            },
            security = new
            {
                credentialsAreWriteOnly = true,
                encryptedStoreRequired = true,
                httpsEndpointsRequired = true,
                connectionTestsAreExplicit = true,
                secretsReturned = false
            },
            providers
        });
    }

    private static async Task<IResult?> AuthorizeManageAsync(HttpContext context)
    {
        var authority = await ResolveManageAuthorityAsync(context);
        if (authority.Allowed) return null;

        return Results.Json(new
        {
            module = ModuleNumber,
            code = "MANAGE_INTEGRATIONS_026_REQUIRED",
            message = authority.Message,
            authoritySource = authority.Source,
            requiredPermission = "MANAGE_INTEGRATIONS_026",
            dynamicAction = "MODULE_CONFIGURE",
            isViewAs = IsViewAs(context)
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task<bool> HasManageAuthorityAsync(HttpContext context) =>
        (await ResolveManageAuthorityAsync(context)).Allowed;

    private static async Task<ManageAuthority> ResolveManageAuthorityPolicyFirstAsync(
        HttpContext context)
    {
        if (IsViewAs(context))
        {
            return new ManageAuthority(
                false,
                "view_as_read_only",
                "Exit Administrator View-As before changing CRM or ERP connector configuration.");
        }

        // Permanent actual-session Super Administrator authority is evaluated
        // before any published policy. A stale or incomplete policy matrix may
        // narrow ordinary roles, but it cannot downgrade the authenticated
        // Super Administrator's own session. View-As was rejected above and
        // never inherits this bypass.
        if (await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(context))
        {
            return new ManageAuthority(
                true,
                "actual_session_super_administrator",
                "Your actual Super Administrator session has permanent Full Control of Module 026.");
        }

        var dynamicDecision = await ScopedRolePolicyModule.EvaluateCurrentActorAsync(
            context,
            ModuleNumber,
            "MODULE_CONFIGURE",
            isWrite: true);

        if (dynamicDecision is not null && !dynamicDecision.LegacyFallback)
        {
            return new ManageAuthority(
                dynamicDecision.Allowed,
                "published_role_policy",
                dynamicDecision.Explanation);
        }

        if (await HasManageAuthorityLegacyAsync(context))
        {
            return new ManageAuthority(
                true,
                "legacy_role_or_permission",
                "Your actual ProjectPulse session can manage Module 026 integrations.");
        }

        return new ManageAuthority(
            false,
            dynamicDecision is null
                ? "legacy_role_or_permission_required"
                : "published_policy_legacy_fallback",
            "Your actual session needs Super Administrator, Administrator, Integration Administrator, or the MANAGE_INTEGRATIONS_026 permission.");
    }

    private static int BuiltinOrder(string providerKey) =>
        providerKey.ToLowerInvariant() switch
        {
            "zendesk_sell" => 10,
            "salesforce" => 20,
            "servicenow" => 30,
            "certinia" => 40,
            _ => 100
        };

    private sealed record ManageAuthority(
        bool Allowed,
        string Source,
        string Message);

    private sealed record ProviderAdministrationRow(
        string ProviderKey,
        string ProviderName,
        string ProviderType,
        string AuthModel,
        string BaseUrl,
        string HealthCheckUrl,
        string OauthAuthorizationUrl,
        string OauthTokenUrl,
        string OauthClientId,
        string OauthScopes,
        string ApiKeyHeader,
        string ApiKeyPrefix,
        string RecordLookupUrlTemplate,
        string ImportMappingJson,
        bool IsBuiltin,
        bool IsPersisted,
        bool IsEnabled,
        string AvailabilityStatus,
        DateTime? LastCheckedAt,
        DateTime? LastAvailableAt,
        int? LastStatusCode,
        string LastErrorCode,
        string Notes,
        bool CredentialConfigured,
        bool OauthConnected,
        bool SecretValueReturned);

    private sealed record BuiltinProviderTemplate(
        string ProviderKey,
        string ProviderName,
        string ProviderType,
        string AuthModel,
        string BaseUrl,
        string HealthCheckUrl,
        string OauthAuthorizationUrl,
        string OauthTokenUrl,
        string OauthClientId,
        string OauthScopes,
        string ApiKeyHeader,
        string ApiKeyPrefix,
        string RecordLookupUrlTemplate,
        string ImportMappingJson,
        string Notes)
    {
        public ProviderAdministrationRow ToAdministrationRow() =>
            new(
                ProviderKey,
                ProviderName,
                ProviderType,
                AuthModel,
                BaseUrl,
                HealthCheckUrl,
                OauthAuthorizationUrl,
                OauthTokenUrl,
                OauthClientId,
                OauthScopes,
                ApiKeyHeader,
                ApiKeyPrefix,
                RecordLookupUrlTemplate,
                ImportMappingJson,
                true,
                false,
                false,
                "not_configured",
                null,
                null,
                null,
                string.Empty,
                Notes,
                false,
                false,
                false);
    }
}
