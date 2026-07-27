using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Makes the Module 065 Microsoft 365 / SMTP configuration authoritative for
/// the running mail delivery process without returning or moving secret values.
/// Metadata is stored in the existing Module 065 native document. Existing
/// Test/Production services secrets remain in their current environment or
/// encrypted Microsoft Integration stores.
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

    private static async Task<IResult> ApplyRuntimeAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access is not null) return access;
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
        var result = ApplyRuntime(normalized.Configuration!);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = result.Ready ? "mail_runtime_ready" : "mail_runtime_configuration_pending",
            provider = result.Provider,
            environmentMode = normalized.Configuration!.EnvironmentMode,
            senderMailbox = normalized.Configuration.SenderAddress,
            metadataApplied = true,
            secretValuesRead = false,
            secretValuesReturned = false,
            servicesSecretAvailable = result.ServicesSecretAvailable,
            smtpCredentialAvailable = result.SmtpCredentialAvailable,
            runtimeReady = result.Ready,
            message = result.Message
        });
    }

    private static async Task HydrateRuntimeAsync()
    {
        // Existing Microsoft secret hydration is also registered at startup.
        // A short bounded retry lets that process populate the environment first.
        foreach (var delay in new[] { 500, 1500, 3000 })
        {
            try
            {
                await Task.Delay(delay);
                var configuration = await ReadStoredConfigurationAsync();
                if (configuration is null) continue;
                var result = ApplyRuntime(configuration);
                if (result.Ready || result.ServicesSecretAvailable || result.SmtpCredentialAvailable) return;
            }
            catch
            {
                // Existing Container App environment configuration remains unchanged.
            }
        }
    }

    private static RuntimeResult ApplyRuntime(MailRuntimeConfiguration configuration)
    {
        var provider = configuration.ProviderTarget switch
        {
            "microsoft_graph" => "microsoft_graph",
            "smtp_relay" => "exchange_online_smtp",
            "locked" => "locked",
            _ => string.Empty
        };

        SetIfPresent("PROJECTPULSE_MAIL_PROVIDER", provider);
        SetIfPresent("PROJECTPULSE_EMAIL_PROVIDER", provider);
        SetIfPresent("PROJECTPULSE_M365_TENANT_ID", configuration.TenantId);
        SetIfPresent("PROJECTPULSE_M365_CLIENT_ID", configuration.ClientId);
        SetIfPresent("PROJECTPULSE_M365_SENDER_MAILBOX", configuration.SenderAddress);
        SetIfPresent("PROJECTPULSE_SMTP_HOST", configuration.SmtpHost);
        SetIfPresent("PROJECTPULSE_SMTP_PORT", configuration.SmtpPort.ToString());
        SetIfPresent("PROJECTPULSE_SMTP_FROM", configuration.SenderAddress);

        var servicesSecret = ServicesSecret(configuration.EnvironmentMode);
        if (!string.IsNullOrWhiteSpace(servicesSecret))
            Environment.SetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET", servicesSecret);

        var smtpUser = First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USERNAME"),
            Environment.GetEnvironmentVariable("SMTP_USERNAME"));
        var smtpPassword = First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD"),
            Environment.GetEnvironmentVariable("SMTP_PASSWORD"));
        var smtpCredentialAvailable = !string.IsNullOrWhiteSpace(smtpUser)
            && !string.IsNullOrWhiteSpace(smtpPassword);
        var servicesSecretAvailable = !string.IsNullOrWhiteSpace(servicesSecret);

        if (provider == "microsoft_graph")
        {
            var ready = Guid.TryParse(configuration.TenantId, out _)
                && Guid.TryParse(configuration.ClientId, out _)
                && servicesSecretAvailable
                && IsEmail(configuration.SenderAddress);
            return new(
                provider,
                ready,
                servicesSecretAvailable,
                smtpCredentialAvailable,
                ready
                    ? "Microsoft Graph mail runtime is ready. The existing services application and sender mailbox will be used."
                    : "Microsoft Graph mail still requires a valid tenant ID, services client ID, services secret, and sender mailbox.");
        }

        if (provider == "exchange_online_smtp")
        {
            var ready = !string.IsNullOrWhiteSpace(configuration.SmtpHost)
                && configuration.SmtpPort is > 0 and <= 65535
                && IsEmail(configuration.SenderAddress)
                && smtpCredentialAvailable;
            return new(
                provider,
                ready,
                servicesSecretAvailable,
                smtpCredentialAvailable,
                ready
                    ? "Microsoft 365 SMTP runtime is ready."
                    : "SMTP relay metadata was applied, but SMTP username/password must remain configured in the approved Container App secret environment.");
        }

        return new(
            provider,
            false,
            servicesSecretAvailable,
            smtpCredentialAvailable,
            "Microsoft mail delivery is locked or no supported provider is selected.");
    }

    private static NormalizedRequest Normalize(MailRuntimeRequest? request)
    {
        var environmentMode = NormalizeEnvironment(request?.EnvironmentMode);
        var provider = (request?.ProviderTarget ?? string.Empty).Trim().ToLowerInvariant();
        var tenantId = (request?.TenantId ?? string.Empty).Trim();
        var clientId = (request?.ClientId ?? string.Empty).Trim();
        var senderAddress = (request?.SenderAddress ?? string.Empty).Trim().ToLowerInvariant();
        var smtpHost = (request?.SmtpHost ?? string.Empty).Trim().ToLowerInvariant();
        var smtpPort = request?.SmtpPort ?? 587;

        if (string.IsNullOrWhiteSpace(environmentMode))
            return new(null, InvalidRequest("Environment must be Test or Production."));
        if (provider is not ("microsoft_graph" or "smtp_relay" or "locked"))
            return new(null, InvalidRequest("Provider must be Microsoft Graph, Microsoft 365 SMTP relay, or Locked."));
        if (provider != "locked" && !IsEmail(senderAddress))
            return new(null, InvalidRequest("A valid sender mailbox is required."));
        if (provider == "microsoft_graph"
            && (!Guid.TryParse(tenantId, out _) || !Guid.TryParse(clientId, out _)))
            return new(null, InvalidRequest("Microsoft Graph mail requires GUID tenant and services application/client IDs."));
        if (provider == "smtp_relay" && (string.IsNullOrWhiteSpace(smtpHost) || smtpPort is <= 0 or > 65535))
            return new(null, InvalidRequest("SMTP relay requires a valid host and port."));

        return new(new(
            environmentMode,
            provider,
            tenantId,
            clientId,
            smtpHost,
            smtpPort,
            senderAddress,
            (request?.SenderName ?? string.Empty).Trim(),
            (request?.ReplyToAddress ?? string.Empty).Trim().ToLowerInvariant(),
            (request?.RecipientBoundary ?? "test_only").Trim().ToLowerInvariant()), null);
    }

    private static async Task<MailRuntimeConfiguration?> ReadStoredConfigurationAsync()
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
        var environmentMode = NormalizeEnvironment(JsonString(root, "activeEnvironmentMode"));
        var activeTenantKey = JsonString(root, "activeTenantKey");
        if (!TryProperty(root, "tenants", out var tenants) || tenants.ValueKind != JsonValueKind.Array) return null;

        JsonElement? activeTenant = null;
        foreach (var tenant in tenants.EnumerateArray())
        {
            var key = JsonString(tenant, "key");
            var mode = NormalizeEnvironment(JsonString(tenant, "environmentMode"));
            if ((!string.IsNullOrWhiteSpace(activeTenantKey) && key.Equals(activeTenantKey, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(environmentMode) && mode == environmentMode))
            {
                activeTenant = tenant.Clone();
                environmentMode = mode;
                break;
            }
        }
        if (activeTenant is null) return null;
        if (!TryProperty(root, "mail", out var mail)) mail = default;
        if (!TryProperty(activeTenant.Value, "services", out var services)) services = default;

        var normalized = Normalize(new(
            environmentMode,
            JsonString(mail, "providerTarget"),
            JsonString(activeTenant.Value, "tenantId"),
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
        if (userId is null)
        {
            return Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

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
            var administrator = roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR");
            if (!administrator && !permissions.Any(WritePermissions.Contains))
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "microsoft_integration_manage_access_required",
                    message = "Manage Microsoft Integration or global-mail authority is required."
                }, statusCode: StatusCodes.Status403Forbidden);
            }
            return null;
        }
        catch
        {
            return Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static string ServicesSecret(string environmentMode)
    {
        var production = environmentMode == "production";
        return First(
            Environment.GetEnvironmentVariable(production
                ? "PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET"
                : "PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET"));
    }

    private static void SetIfPresent(string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Environment.SetEnvironmentVariable(name, value);
    }

    private static string NormalizeEnvironment(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "test" or "development" or "dev" or "onenecklab" => "test",
            "production" or "prod" or "ussignal" => "production",
            _ => string.Empty
        };
    }

    private static bool IsEmail(string value)
    {
        try { return new System.Net.Mail.MailAddress(value).Address == value; }
        catch { return false; }
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

    private static string BuildConnectionString()
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
            || string.IsNullOrWhiteSpace(password)) return string.Empty;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10
        }.ConnectionString;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string JsonString(JsonElement element, string name)
    {
        if (!TryProperty(element, name, out var value) || value.ValueKind != JsonValueKind.String)
            return string.Empty;
        return value.GetString()?.Trim() ?? string.Empty;
    }

    private static int? JsonInt(JsonElement element, string name)
    {
        if (!TryProperty(element, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), out number) ? number : null;
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static IResult InvalidRequest(string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_request",
        message
    });

    private sealed record MailRuntimeRequest(
        string EnvironmentMode,
        string ProviderTarget,
        string TenantId,
        string ClientId,
        string SmtpHost,
        int SmtpPort,
        string SenderName,
        string SenderAddress,
        string ReplyToAddress,
        string RecipientBoundary);

    private sealed record MailRuntimeConfiguration(
        string EnvironmentMode,
        string ProviderTarget,
        string TenantId,
        string ClientId,
        string SmtpHost,
        int SmtpPort,
        string SenderAddress,
        string SenderName,
        string ReplyToAddress,
        string RecipientBoundary);

    private sealed record NormalizedRequest(MailRuntimeConfiguration? Configuration, IResult? Failure);
    private sealed record RuntimeResult(
        string Provider,
        bool Ready,
        bool ServicesSecretAvailable,
        bool SmtpCredentialAvailable,
        string Message);
}
