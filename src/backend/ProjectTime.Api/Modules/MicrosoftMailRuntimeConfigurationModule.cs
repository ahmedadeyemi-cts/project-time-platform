using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Applies environment-specific non-secret Microsoft mail metadata stored by
/// Module 065 to the running API. The configured provider is preserved even
/// when Test-only or Locked recipient boundaries deliberately keep live delivery
/// outbox-only. Test and Production credentials remain isolated and are never returned.
/// </summary>
public static class MicrosoftMailRuntimeConfigurationModule
{
    private const string ModuleNumber = "065";
    private const string RuntimePath = "/api/microsoft-integration/mail-runtime";
    private const string ConfigurationMarker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";

    private static readonly HashSet<string> WritePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_ENTRA_SECRET",
        "MANAGE_GLOBAL_MAIL_CONFIGURATION",
        "MANAGE_GLOBAL_MAIL"
    };

    public static WebApplication MapMicrosoftMailRuntimeConfigurationEndpoints(this WebApplication app)
    {
        app.MapPut(RuntimePath, (Func<HttpContext, Task<IResult>>)ApplyRuntimeAsync);
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(HydrateRuntimeAsync));
        return app;
    }

    internal static async Task<RuntimeResult?> ApplyStoredEnvironmentAsync(string environmentMode)
    {
        var normalizedMode = MicrosoftEnvironmentRuntimeResolver.Normalize(environmentMode);
        if (string.IsNullOrWhiteSpace(normalizedMode)) return null;
        var configuration = await ReadStoredConfigurationAsync(normalizedMode);
        return configuration is null ? null : ApplyRuntime(configuration);
    }

    private static async Task<IResult> ApplyRuntimeAsync(HttpContext context)
    {
        var accessFailure = await AuthorizeAsync(context);
        if (accessFailure is not null) return accessFailure;
        if (IsViewAs(context))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "view_as_read_only",
                message = "Exit Administrator View-As before applying Microsoft mail configuration."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        MailRuntimeRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<MailRuntimeRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid Microsoft mail runtime configuration is required.");
        }

        var normalized = Normalize(request);
        if (normalized.Failure is not null) return normalized.Failure;
        var configuration = normalized.Configuration!;
        var runtimeEnvironment = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if (!string.IsNullOrWhiteSpace(runtimeEnvironment)
            && !configuration.EnvironmentMode.Equals(runtimeEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "mail_profile_saved_for_other_environment",
                environmentMode = configuration.EnvironmentMode,
                runtimeEnvironment,
                configuredProvider = configuration.ProviderTarget,
                metadataApplied = false,
                liveDeliveryEnabled = false,
                secretValuesRead = false,
                secretValuesReturned = false,
                message = $"The {MicrosoftEnvironmentRuntimeResolver.Display(configuration.EnvironmentMode)} mail profile was preserved and will activate only in its matching environment."
            });
        }

        var result = ApplyRuntime(configuration);
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = result.ConfiguredReady ? "mail_runtime_ready" : "mail_runtime_configuration_pending",
            environmentMode = configuration.EnvironmentMode,
            runtimeEnvironment,
            configuredProvider = result.ConfiguredProvider,
            activeDeliveryProvider = result.ModuleProvider,
            sharedProvider = result.SharedProvider,
            recipientBoundary = configuration.RecipientBoundary,
            senderMailbox = configuration.SenderAddress,
            metadataApplied = true,
            liveDeliveryEnabled = result.LiveDeliveryEnabled,
            graphCapableDeliveryReady = result.GraphCapableDeliveryReady,
            sharedDeliveryReady = result.SharedDeliveryReady,
            servicesSecretAvailable = result.ServicesSecretAvailable,
            smtpCredentialAvailable = result.SmtpCredentialAvailable,
            configuredTransportReady = result.ConfiguredReady,
            runtimeReady = result.ConfiguredReady,
            secretValuesRead = false,
            secretValuesReturned = false,
            message = result.Message
        });
    }

    private static async Task HydrateRuntimeAsync()
    {
        foreach (var delay in new[] { 500, 1500, 3000 })
        {
            try
            {
                await Task.Delay(delay);
                var environmentMode = MicrosoftEnvironmentRuntimeResolver.Resolve();
                if (string.IsNullOrWhiteSpace(environmentMode)) continue;
                var configuration = await ReadStoredConfigurationAsync(environmentMode);
                if (configuration is null) continue;
                ApplyRuntime(configuration);
                return;
            }
            catch
            {
                // Existing Container App environment configuration remains unchanged.
            }
        }
    }

    private static RuntimeResult ApplyRuntime(MailRuntimeConfiguration configuration)
    {
        var servicesSecret = ServicesSecret(configuration.EnvironmentMode);
        var smtpCredential = ResolveSmtpCredential(configuration.EnvironmentMode);
        var servicesSecretAvailable = !string.IsNullOrWhiteSpace(servicesSecret);
        var smtpCredentialAvailable = !string.IsNullOrWhiteSpace(smtpCredential.Username)
            && !string.IsNullOrWhiteSpace(smtpCredential.Password);
        var graphReady = Guid.TryParse(configuration.TenantId, out _)
            && Guid.TryParse(configuration.ClientId, out _)
            && servicesSecretAvailable
            && IsEmail(configuration.SenderAddress);
        var smtpReady = configuration.SmtpHost.Equals("smtp.office365.com", StringComparison.OrdinalIgnoreCase)
            && configuration.SmtpPort is > 0 and <= 65535
            && IsEmail(configuration.SenderAddress)
            && smtpCredentialAvailable;
        var configuredReady = configuration.ProviderTarget switch
        {
            "microsoft_graph" => graphReady,
            "smtp_relay" => smtpReady,
            "locked" => true,
            _ => false
        };

        var liveDeliveryEnabled = configuration.RecipientBoundary == "production_governed"
            && configuration.ProviderTarget != "locked"
            && configuredReady;
        var moduleProvider = liveDeliveryEnabled
            ? configuration.ProviderTarget switch
            {
                "microsoft_graph" => "microsoft_graph",
                "smtp_relay" => "exchange_online_smtp",
                _ => "locked"
            }
            : "locked";
        var sharedProvider = liveDeliveryEnabled && configuration.ProviderTarget == "smtp_relay"
            ? "smtp"
            : "outbox_only";

        var environmentPrefix = configuration.EnvironmentMode == "production"
            ? "PROJECTPULSE_PRODUCTION_MAIL_"
            : "PROJECTPULSE_TEST_MAIL_";
        Environment.SetEnvironmentVariable(environmentPrefix + "PROVIDER", configuration.ProviderTarget);
        Environment.SetEnvironmentVariable(environmentPrefix + "RECIPIENT_BOUNDARY", configuration.RecipientBoundary);
        Environment.SetEnvironmentVariable(environmentPrefix + "SENDER", configuration.SenderAddress);
        Environment.SetEnvironmentVariable(environmentPrefix + "SMTP_HOST", configuration.SmtpHost);
        Environment.SetEnvironmentVariable(environmentPrefix + "SMTP_PORT", configuration.SmtpPort.ToString());

        Environment.SetEnvironmentVariable("PROJECTPULSE_MAIL_CONFIGURED_ENVIRONMENT", configuration.EnvironmentMode);
        Environment.SetEnvironmentVariable("PROJECTPULSE_MAIL_CONFIGURED_PROVIDER", configuration.ProviderTarget);
        Environment.SetEnvironmentVariable("PROJECTPULSE_MAIL_PROVIDER", moduleProvider);
        Environment.SetEnvironmentVariable("PROJECTPULSE_EMAIL_PROVIDER", sharedProvider);
        Environment.SetEnvironmentVariable("PROJECTPULSE_MAIL_RECIPIENT_BOUNDARY", configuration.RecipientBoundary);
        SetIfPresent("PROJECTPULSE_M365_TENANT_ID", configuration.TenantId);
        SetIfPresent("PROJECTPULSE_M365_CLIENT_ID", configuration.ClientId);
        SetIfPresent("PROJECTPULSE_M365_SENDER_MAILBOX", configuration.SenderAddress);
        SetIfPresent("PROJECTPULSE_SMTP_HOST", configuration.SmtpHost);
        SetIfPresent("PROJECTPULSE_SMTP_PORT", configuration.SmtpPort.ToString());
        SetIfPresent("PROJECTPULSE_SMTP_FROM", configuration.SenderAddress);
        Environment.SetEnvironmentVariable(
            "PROJECTPULSE_M365_CLIENT_SECRET",
            servicesSecretAvailable ? servicesSecret : null);

        var boundaryMessage = configuration.RecipientBoundary switch
        {
            "test_only" => "The Test-only boundary keeps live delivery disabled while preserving the configured transport for readiness testing.",
            "locked" => "The recipient boundary is locked; no live message can be sent.",
            _ when liveDeliveryEnabled => "The configured provider is eligible for governed live delivery.",
            _ => "Live delivery remains disabled until the configured provider passes readiness requirements."
        };
        var providerMessage = configuration.ProviderTarget switch
        {
            "microsoft_graph" when graphReady => "Microsoft Graph application authentication metadata and sender configuration are ready.",
            "microsoft_graph" => "Microsoft Graph requires a tenant GUID, services client GUID, environment-specific services secret, and sender mailbox.",
            "smtp_relay" when smtpReady => "Microsoft 365 SMTP metadata and the environment-specific credential pair are ready.",
            "smtp_relay" => "Microsoft 365 SMTP requires smtp.office365.com, a valid port and sender, and the environment-specific SMTP username/password pair.",
            _ => "No live Microsoft mail provider is selected."
        };

        return new(
            configuration.ProviderTarget,
            moduleProvider,
            sharedProvider,
            liveDeliveryEnabled,
            graphReady,
            smtpReady,
            servicesSecretAvailable,
            smtpCredentialAvailable,
            configuredReady,
            $"{providerMessage} {boundaryMessage}");
    }

    private static NormalizedRequest Normalize(MailRuntimeRequest? request)
    {
        var environmentMode = MicrosoftEnvironmentRuntimeResolver.Normalize(request?.EnvironmentMode);
        var provider = (request?.ProviderTarget ?? "locked").Trim().ToLowerInvariant();
        var boundary = (request?.RecipientBoundary ?? "test_only").Trim().ToLowerInvariant();
        var tenantId = (request?.TenantId ?? string.Empty).Trim();
        var clientId = (request?.ClientId ?? string.Empty).Trim();
        var senderAddress = (request?.SenderAddress ?? string.Empty).Trim().ToLowerInvariant();
        var replyToAddress = (request?.ReplyToAddress ?? string.Empty).Trim().ToLowerInvariant();
        var smtpHost = First((request?.SmtpHost ?? string.Empty).Trim().ToLowerInvariant(), "smtp.office365.com");
        var smtpPort = request?.SmtpPort ?? 587;

        if (string.IsNullOrWhiteSpace(environmentMode)) return new(null, InvalidRequest("Environment must be Test or Production."));
        if (provider is not ("microsoft_graph" or "smtp_relay" or "locked")) return new(null, InvalidRequest("Provider must be Microsoft Graph, Microsoft 365 SMTP relay, or Locked."));
        if (boundary is not ("test_only" or "production_governed" or "locked")) return new(null, InvalidRequest("Recipient boundary must be Test only, Production governed, or Locked."));
        if (!string.IsNullOrWhiteSpace(senderAddress) && !IsEmail(senderAddress)) return new(null, InvalidRequest("Sender mailbox must be a valid email address."));
        if (!string.IsNullOrWhiteSpace(replyToAddress) && !IsEmail(replyToAddress)) return new(null, InvalidRequest("Reply-to address must be a valid email address."));
        if (!string.IsNullOrWhiteSpace(tenantId) && !Guid.TryParse(tenantId, out _)) return new(null, InvalidRequest("Microsoft tenant ID must be a GUID."));
        if (!string.IsNullOrWhiteSpace(clientId) && !Guid.TryParse(clientId, out _)) return new(null, InvalidRequest("Microsoft services application/client ID must be a GUID."));
        if (smtpPort is <= 0 or > 65535) return new(null, InvalidRequest("SMTP port must be between 1 and 65535."));

        return new(new(
            environmentMode,
            provider,
            tenantId,
            clientId,
            smtpHost,
            smtpPort,
            senderAddress,
            (request?.SenderName ?? string.Empty).Trim(),
            replyToAddress,
            boundary), null);
    }

    private static async Task<MailRuntimeConfiguration?> ReadStoredConfigurationAsync(string environmentMode)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT document_json::text
            FROM projectpulse_native_admin_documents
            WHERE module_number='065' AND document_key='configuration'
            LIMIT 1;
            """, connection);
        var raw = Convert.ToString(await command.ExecuteScalarAsync());
        if (string.IsNullOrWhiteSpace(raw)) return null;

        using var document = JsonDocument.Parse(raw);
        if (!TryProperty(document.RootElement, "configuration", out var documentConfiguration)) return null;
        var notes = JsonString(documentConfiguration, "notes");
        if (!notes.StartsWith(ConfigurationMarker, StringComparison.Ordinal)) return null;
        using var configurationDocument = JsonDocument.Parse(notes[ConfigurationMarker.Length..]);
        var root = configurationDocument.RootElement;
        if (!TryProperty(root, "tenants", out var tenants) || tenants.ValueKind != JsonValueKind.Array) return null;

        JsonElement? selectedTenant = null;
        foreach (var tenant in tenants.EnumerateArray())
        {
            var mode = MicrosoftEnvironmentRuntimeResolver.Normalize(JsonString(tenant, "environmentMode"));
            if (mode.Equals(environmentMode, StringComparison.OrdinalIgnoreCase))
            {
                selectedTenant = tenant.Clone();
                break;
            }
        }
        if (selectedTenant is null) return null;
        if (!TryProperty(selectedTenant.Value, "services", out var services)) services = default;
        if (!TryProperty(selectedTenant.Value, "mail", out var mail)
            && !TryProperty(root, "mail", out mail)) mail = default;

        var normalized = Normalize(new(
            environmentMode,
            JsonString(mail, "providerTarget"),
            JsonString(selectedTenant.Value, "tenantId"),
            JsonString(services, "clientId"),
            JsonString(mail, "smtpHost"),
            JsonInt(mail, "smtpPort") ?? 587,
            JsonString(mail, "senderName"),
            JsonString(mail, "senderAddress"),
            JsonString(mail, "replyToAddress"),
            JsonString(mail, "recipientBoundary")));
        return normalized.Configuration;
    }

    private static async Task<IResult?> AuthorizeAsync(HttpContext context)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null) return Results.Json(new { status = "session_required", message = "A valid ProjectPulse session is required." }, statusCode: StatusCodes.Status401Unauthorized);
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return Results.Json(new { status = "authorization_dependency_unavailable", message = "Microsoft Integration authorization is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(role.role_code,''), COALESCE(permission.permission_code,'')
                FROM app_user_role_assignments assignment
                JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
                LEFT JOIN app_role_permissions role_permission ON role_permission.app_role_id=role.app_role_id
                LEFT JOIN app_permissions permission ON permission.app_permission_id=role_permission.app_permission_id
                WHERE assignment.user_id=@user_id AND assignment.is_active=TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                if (!reader.IsDBNull(0)) roles.Add(reader.GetString(0));
                if (!reader.IsDBNull(1)) permissions.Add(reader.GetString(1));
            }
            var administrator = ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, roles);
            if (!administrator && !permissions.Any(WritePermissions.Contains))
            {
                return Results.Json(new { module = ModuleNumber, status = "microsoft_integration_manage_access_required", message = "Manage Microsoft Integration or global-mail authority is required." }, statusCode: StatusCodes.Status403Forbidden);
            }
            return null;
        }
        catch
        {
            return Results.Json(new { status = "authorization_dependency_unavailable", message = "Microsoft Integration authorization is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string ServicesSecret(string environmentMode)
    {
        var activeMode = MicrosoftEnvironmentRuntimeResolver.Normalize(Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"));
        if (environmentMode == "production")
        {
            return First(
                Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_MICROSOFT_TENANT_USSIGNAL_CLIENT_SECRET"),
                activeMode == "production" ? Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET") : string.Empty,
                activeMode == "production" ? Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET") : string.Empty);
        }
        return First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_MICROSOFT_TENANT_ONENECKLAB_CLIENT_SECRET"),
            activeMode == "test" ? Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET") : string.Empty,
            activeMode == "test" ? Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET") : string.Empty);
    }

    private static SmtpCredential ResolveSmtpCredential(string environmentMode)
    {
        var activeMode = MicrosoftEnvironmentRuntimeResolver.Normalize(Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"));
        var prefix = environmentMode == "production" ? "PROJECTPULSE_PRODUCTION_SMTP_" : "PROJECTPULSE_TEST_SMTP_";
        return new(
            First(Environment.GetEnvironmentVariable(prefix + "USERNAME"), activeMode == environmentMode ? Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME") : string.Empty, activeMode == environmentMode ? Environment.GetEnvironmentVariable("SMTP_USERNAME") : string.Empty),
            First(Environment.GetEnvironmentVariable(prefix + "PASSWORD"), activeMode == environmentMode ? Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD") : string.Empty, activeMode == environmentMode ? Environment.GetEnvironmentVariable("SMTP_PASSWORD") : string.Empty));
    }

    private static string BuildConnectionString()
    {
        foreach (var name in new[] { "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse", "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING", "PROJECTTIME_DATABASE_CONNECTION" })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return string.Empty;
        return new NpgsqlConnectionStringBuilder { Host = host, Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432, Database = database, Username = username, Password = password, IncludeErrorDetail = false, Pooling = true, MaxPoolSize = 5 }.ConnectionString;
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

    private static bool IsViewAs(HttpContext context) => context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is bool isViewAs && isViewAs;
    private static bool IsEmail(string value) { try { return new System.Net.Mail.MailAddress(value).Address == value; } catch { return false; } }
    private static void SetIfPresent(string name, string value) { if (!string.IsNullOrWhiteSpace(value)) Environment.SetEnvironmentVariable(name, value); }
    private static IResult InvalidRequest(string message) => Results.BadRequest(new { module = ModuleNumber, status = "invalid_request", message });
    private static string First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
            }
        }
        value = default;
        return false;
    }

    private static string JsonString(JsonElement element, string name) => TryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
    private static int? JsonInt(JsonElement element, string name) => TryProperty(element, name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private sealed record NormalizedRequest(MailRuntimeConfiguration? Configuration, IResult? Failure);
    private sealed record MailRuntimeRequest(string? EnvironmentMode, string? ProviderTarget, string? TenantId, string? ClientId, string? SmtpHost, int? SmtpPort, string? SenderName, string? SenderAddress, string? ReplyToAddress, string? RecipientBoundary);
    private sealed record MailRuntimeConfiguration(string EnvironmentMode, string ProviderTarget, string TenantId, string ClientId, string SmtpHost, int SmtpPort, string SenderAddress, string SenderName, string ReplyToAddress, string RecipientBoundary);
    private sealed record SmtpCredential(string Username, string Password);
    internal sealed record RuntimeResult(string ConfiguredProvider, string ModuleProvider, string SharedProvider, bool LiveDeliveryEnabled, bool GraphCapableDeliveryReady, bool SharedDeliveryReady, bool ServicesSecretAvailable, bool SmtpCredentialAvailable, bool ConfiguredReady, string Message);
}
