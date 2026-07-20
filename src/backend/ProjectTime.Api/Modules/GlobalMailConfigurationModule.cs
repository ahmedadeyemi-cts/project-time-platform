using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 067 provides an administrator-only, non-secret view of the shared
/// ProjectPulse mail configuration and Microsoft 365 migration readiness.
/// Provider activation, secret rotation, and test delivery remain locked until
/// the corresponding Azure, Entra, and deployment changes are authorized.
/// </summary>
public static class GlobalMailConfigurationModule
{
    private const string ModuleNumber = "067";
    private const string ContractVersion = "2026-07-19.1";
    private const string ImplementationBaseline =
        "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";

    public static WebApplication MapGlobalMailConfigurationEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/global-mail/configuration",
            (Func<HttpContext, Task<IResult>>)GetConfigurationAsync);

        app.MapGet(
            "/api/global-mail/health",
            (Func<HttpContext, Task<IResult>>)GetHealthAsync);

        return app;
    }

    private static async Task<IResult> GetConfigurationAsync(HttpContext context)
    {
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;

        var snapshot = BuildSnapshot();

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Global Mail Configuration Center",
            status = "configuration_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            observedAt = DateTimeOffset.UtcNow,
            environment = RuntimeEnvironment(),
            access = new
            {
                classification = "administrators_only",
                authoritySource = "actual_projectpulse_session",
                viewAsTransfersAuthority = false,
                isViewAs = IsViewAs(context)
            },
            configuration = snapshot.Configuration,
            secretMetadata = snapshot.SecretMetadata,
            consumerRegistry = ConsumerRegistry(),
            migration = snapshot.Migration,
            controls = new
            {
                secretValuesReturned = false,
                secretRotationEnabled = false,
                providerActivationEnabled = false,
                testDeliveryEnabled = false,
                configurationMutationEnabled = false,
                reason = "Azure, Entra, secret-store, database, and deployment changes require separate authorization."
            },
            guardrails = Guardrails()
        });
    }

    private static async Task<IResult> GetHealthAsync(HttpContext context)
    {
        var authorization = await AuthorizeAdministratorAsync(context);
        if (authorization is not null) return authorization;

        var snapshot = BuildSnapshot();
        var checks = HealthChecks(snapshot);
        var blocking = checks.Count(check => check.Required && check.State != "ready");

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "mail_health_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            observationMode = "configuration_only_no_provider_call",
            overallState = blocking == 0
                ? "ready_for_controlled_connectivity_validation"
                : "configuration_incomplete",
            blockingCheckCount = blocking,
            checks,
            providerRequestAttempted = false,
            messageSent = false,
            note = "Module 067 does not contact Microsoft 365 or any legacy provider during a read-only health request."
        });
    }

    private static MailSnapshot BuildSnapshot()
    {
        var configuredProvider = NormalizeProvider(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MAIL_PROVIDER")
            ?? Environment.GetEnvironmentVariable("PROJECTPULSE_EMAIL_PROVIDER"));

        var tenant = IdentifierMetadata(
            "tenant_id",
            Environment.GetEnvironmentVariable("PROJECTPULSE_M365_TENANT_ID")
            ?? Environment.GetEnvironmentVariable("AZURE_TENANT_ID"));

        var client = IdentifierMetadata(
            "client_id",
            Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_ID")
            ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID"));

        var clientSecret = SecretMetadata(
            "client_secret",
            "PROJECTPULSE_M365_CLIENT_SECRET",
            "AZURE_CLIENT_SECRET");

        var certificate = SecretMetadata(
            "certificate",
            "PROJECTPULSE_M365_CERTIFICATE",
            "PROJECTPULSE_M365_CERTIFICATE_PASSWORD");

        var legacyBrevo = SecretMetadata(
            "legacy_brevo_api_key",
            "PROJECTPULSE_BREVO_API_KEY",
            "BREVO_API_KEY");

        var managedIdentity = IsTrue("PROJECTPULSE_M365_USE_MANAGED_IDENTITY");
        var credentialReady = managedIdentity || clientSecret.Configured || certificate.Configured;
        var senderMailbox = NonSecretSetting(
            "PROJECTPULSE_M365_SENDER_MAILBOX",
            "PROJECTPULSE_SMTP_FROM",
            "SMTP_FROM");
        var replyTo = NonSecretSetting("PROJECTPULSE_M365_REPLY_TO", "PROJECTPULSE_REPLY_TO");
        var microsoftProviderSelected = configuredProvider is
            "microsoft_graph" or "exchange_online_smtp";

        var authenticationMode = managedIdentity
            ? "managed_identity"
            : certificate.Configured
                ? "certificate"
                : clientSecret.Configured
                    ? "client_secret"
                    : "not_configured";

        var legacyState = legacyBrevo.Configured
            ? (configuredProvider == "brevo_api" ? "active_legacy_provider" : "configured_not_selected")
            : "not_configured";

        var migrationState = microsoftProviderSelected
            && tenant.Configured
            && client.Configured
            && credentialReady
            && senderMailbox.Configured
            && !legacyBrevo.Configured
                ? "ready_for_controlled_connectivity_validation"
                : "blocked_pending_configuration_and_authorization";

        return new MailSnapshot(
            new MailConfiguration(
                configuredProvider,
                "microsoft_graph",
                "exchange_online_smtp",
                "https://graph.microsoft.com/v1.0",
                "smtp.office365.com",
                587,
                authenticationMode,
                tenant,
                client,
                senderMailbox,
                replyTo,
                BoundedInt("PROJECTPULSE_MAIL_TIMEOUT_SECONDS", 30, 5, 120),
                BoundedInt("PROJECTPULSE_MAIL_RETRY_LIMIT", 3, 0, 8),
                NonSecretSetting("PROJECTPULSE_MAIL_RECIPIENT_ENVIRONMENT"),
                legacyState,
                false),
            new[] { clientSecret, certificate, legacyBrevo },
            new MailMigration(
                migrationState,
                microsoftProviderSelected,
                tenant.Configured,
                client.Configured,
                credentialReady,
                senderMailbox.Configured,
                legacyBrevo.Configured,
                legacyBrevo.Configured,
                true,
                false,
                "authorized_change_only"));
    }

    private static MailHealthCheck[] HealthChecks(MailSnapshot snapshot)
    {
        var configuration = snapshot.Configuration;
        var migration = snapshot.Migration;

        return
        [
            Check("target_provider", "Microsoft 365 provider selected", migration.MicrosoftProviderSelected, true),
            Check("tenant", "Tenant identifier configured", migration.TenantConfigured, true),
            Check("client", "Application/client identifier configured", migration.ClientConfigured, true),
            Check("credential", "Approved non-password credential configured", migration.CredentialConfigured, true),
            Check("sender", "Governed sender mailbox configured", migration.SenderConfigured, true),
            Check("legacy_provider", "Legacy Brevo credential removed", !migration.LegacyBrevoConfigured, true),
            Check(
                "recipient_environment",
                "Test and production recipient boundary declared",
                configuration.RecipientEnvironment.Configured,
                true),
            new(
                "connectivity_validation",
                "Microsoft 365 connectivity and Send As validation",
                "locked",
                true,
                "Requires separately authorized provider connectivity and test-delivery execution."),
            new(
                "domain_readiness",
                "SPF, DKIM, DMARC, and accepted-domain evidence",
                "not_observed",
                true,
                "Domain evidence is owned outside this read-only source package.")
        ];
    }

    private static MailHealthCheck Check(
        string id,
        string name,
        bool ready,
        bool required) =>
        new(
            id,
            name,
            ready ? "ready" : "missing",
            required,
            ready ? "Configuration metadata is present." : "Required configuration metadata is absent.");

    private static object[] ConsumerRegistry() =>
    [
        new { id = "time_compliance", owner = "Module 023", state = "existing_shared_provider_consumer", purpose = "reminders and escalation" },
        new { id = "signed_handoff", owner = "Module 027", state = "provider_migration_required", purpose = "assignment-complete communication" },
        new { id = "closeout_email", owner = "Module 041", state = "provider_migration_required", purpose = "customer closeout communication" },
        new { id = "contracts", owner = "Module 060", state = "provider_migration_required", purpose = "contract notices" },
        new { id = "operational_alerts", owner = "Module 013", state = "planned", purpose = "critical service notifications" }
    ];

    private static string[] Guardrails() =>
    [
        "Module 067 exposes GET endpoints only and does not send email.",
        "Actual-session administrator authority is required; View-As never grants access.",
        "Secret values are never returned. Only presence, a short SHA-256 fingerprint, and source-name metadata are exposed.",
        "No provider connectivity, DNS discovery, tenant mutation, secret rotation, or activation occurs in this package.",
        "Brevo remains observable for migration evidence but is not treated as the approved target provider.",
        "Test and production recipient boundaries, idempotency, outbox, retry, and audit controls must pass before cutover.",
        "Module 059 and all installed routes remain preserved."
    ];

    private static async Task<IResult?> AuthorizeAdministratorAsync(HttpContext context)
    {
        var actualUserId = ActualSessionUserId(context);
        if (actualUserId is null)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Global Mail authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM app_user_role_assignments ura
                    JOIN app_roles r
                      ON r.app_role_id = ura.app_role_id
                     AND r.is_active = TRUE
                    LEFT JOIN app_role_permissions rp
                      ON rp.app_role_id = r.app_role_id
                    LEFT JOIN app_permissions p
                      ON p.app_permission_id = rp.app_permission_id
                    WHERE ura.user_id = @user_id
                      AND ura.is_active = TRUE
                      AND (
                          upper(COALESCE(r.role_code, '')) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR')
                          OR upper(COALESCE(p.permission_code, '')) IN ('SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
                      )
                );
                """, connection);
            command.Parameters.AddWithValue("user_id", actualUserId.Value);

            if (!Convert.ToBoolean(await command.ExecuteScalarAsync()))
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "administrator_access_required",
                    message = "Global Mail Configuration is restricted to authorized administrators."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            return null;
        }
        catch (Exception exception)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("GlobalMailConfigurationModule");
            logger.LogWarning(
                "Module 067 authorization dependency unavailable ({ExceptionType}).",
                exception.GetType().Name);

            return Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Global Mail authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static Guid? ActualSessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private static string NormalizeProvider(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "microsoft_graph" or "graph" or "m365_graph" => "microsoft_graph",
            "exchange_online_smtp" or "exchange" or "m365_smtp" => "exchange_online_smtp",
            "brevo" or "brevo_api" => "brevo_api",
            "sendmail" => "sendmail",
            "smtp" => "legacy_smtp",
            "outbox" or "outbox_only" => "outbox_only",
            _ => "outbox_only"
        };
    }

    private static IdentifierState IdentifierMetadata(string name, string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return new IdentifierState(
            name,
            normalized.Length > 0,
            normalized.Length > 0 ? $"ending_{normalized[^Math.Min(4, normalized.Length)..]}" : "not_configured");
    }

    private static SecretState SecretMetadata(string name, params string[] environmentNames)
    {
        foreach (var environmentName in environmentNames)
        {
            var value = Environment.GetEnvironmentVariable(environmentName);
            if (string.IsNullOrWhiteSpace(value)) continue;

            var fingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..12];
            return new SecretState(name, true, environmentName, fingerprint);
        }

        return new SecretState(name, false, "not_configured", "not_configured");
    }

    private static SettingState NonSecretSetting(params string[] environmentNames)
    {
        foreach (var environmentName in environmentNames)
        {
            var value = Environment.GetEnvironmentVariable(environmentName)?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return new SettingState(true, environmentName, value);
            }
        }

        return new SettingState(false, "not_configured", "not_configured");
    }

    private static bool IsTrue(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is not null
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static int BoundedInt(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    private static string RuntimeEnvironment()
    {
        var value = (Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT")
                     ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                     ?? "unknown").Trim().ToLowerInvariant();
        if (value.Contains("prod", StringComparison.Ordinal)) return "production";
        if (value.Contains("test", StringComparison.Ordinal)
            || value.Contains("qa", StringComparison.Ordinal)
            || value.Contains("uat", StringComparison.Ordinal)) return "test";
        if (value.Contains("dev", StringComparison.Ordinal)) return "development";
        if (value.Contains("local", StringComparison.Ordinal)) return "local";
        return "runtime_managed";
    }

    private sealed record MailSnapshot(
        MailConfiguration Configuration,
        SecretState[] SecretMetadata,
        MailMigration Migration);

    private sealed record MailConfiguration(
        string ActiveProvider,
        string ApprovedTargetProvider,
        string ApprovedAlternateProvider,
        string GraphEndpoint,
        string ExchangeOnlineHost,
        int ExchangeOnlinePort,
        string AuthenticationMode,
        IdentifierState Tenant,
        IdentifierState Client,
        SettingState SenderMailbox,
        SettingState ReplyTo,
        int TimeoutSeconds,
        int RetryLimit,
        SettingState RecipientEnvironment,
        string LegacyProviderState,
        bool PlaintextPasswordAuthenticationAllowed);

    private sealed record MailMigration(
        string State,
        bool MicrosoftProviderSelected,
        bool TenantConfigured,
        bool ClientConfigured,
        bool CredentialConfigured,
        bool SenderConfigured,
        bool LegacyBrevoConfigured,
        bool BrevoDisablementRequired,
        bool SharedConsumerMigrationRequired,
        bool LiveCutoverAuthorized,
        string RollbackMode);

    private sealed record IdentifierState(string Name, bool Configured, string MaskedValue);
    private sealed record SettingState(bool Configured, string Source, string Value);
    private sealed record SecretState(string Name, bool Configured, string Source, string Fingerprint);
    private sealed record MailHealthCheck(string Id, string Name, string State, bool Required, string Evidence);
}
