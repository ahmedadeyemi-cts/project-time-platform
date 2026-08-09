using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Projects the Module 065 Microsoft services profile into the existing Graph,
/// Module 010 preview, calendar, identity, and Microsoft 365 runtime contracts.
/// Secret values remain in the established encrypted/environment stores and are
/// never accepted from or returned to the browser by this endpoint.
/// </summary>
public static class MicrosoftServicesRuntimeCompatibility
{
    private const string ApplyPath = "/api/microsoft-integration/services-apply-profile";
    private const string ConfigurationMarker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";

    private static readonly string[] RequiredDirectoryPermissions =
    {
        "Directory.Read.All",
        "User.Read.All"
    };

    private static readonly HashSet<string> WritePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_ENTRA_SECRET",
        "MANAGE_AZURE_AD",
        "MANAGE_AZURE_SYNC",
        "MANAGE_GLOBAL_MAIL_CONFIGURATION",
        "MANAGE_GLOBAL_MAIL"
    };

    public static WebApplication MapMicrosoftServicesRuntimeProfileEndpoints(this WebApplication app)
    {
        app.MapPost(ApplyPath, (Func<HttpContext, Task<IResult>>)ApplyProfileAsync);
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(HydrateStoredProfileAsync));
        return app;
    }

    private static async Task<IResult> ApplyProfileAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (IsViewAs(context))
        {
            return Results.Json(new
            {
                module = "065",
                status = "view_as_read_only",
                message = "Exit Administrator View-As before applying Microsoft services configuration."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        ServicesRuntimeProfileRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ServicesRuntimeProfileRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid Microsoft services runtime profile is required.");
        }

        var normalized = Normalize(request);
        if (normalized.Failure is not null) return normalized.Failure;
        var profile = normalized.Profile!;
        var runtimeEnvironment = RuntimeEnvironmentMode(context.Request.Host.Host);
        var runtimeActivated = ApplyEnvironmentProfile(profile, runtimeEnvironment);
        var secretAvailable = ServicesSecret(profile.EnvironmentMode, profile.TenantKey) is { Length: > 0 };
        var missingDirectoryPermissions = RequiredDirectoryPermissions
            .Where(required => !profile.GraphScopes.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return Results.Ok(new
        {
            module = "065",
            status = runtimeActivated
                ? "microsoft_services_runtime_profile_applied"
                : "microsoft_services_profile_saved_for_other_environment",
            profile.EnvironmentMode,
            runtimeEnvironment,
            runtimeActivated,
            connectionPurpose = "microsoft_services_enterprise_application",
            module010PreviewSource = "module_065_services_profile",
            profile.TenantKey,
            profile.TenantId,
            profile.ClientId,
            profile.AuthorityUrl,
            graphScopes = profile.GraphScopes,
            directoryPermissionsReady = missingDirectoryPermissions.Length == 0,
            missingDirectoryPermissions,
            mailSendPermissionDeclared = profile.GraphScopes.Contains("Mail.Send", StringComparer.OrdinalIgnoreCase),
            senderMailboxConfigured = !string.IsNullOrWhiteSpace(profile.SenderMailbox),
            servicesSecretAvailable = secretAvailable,
            secretValuesRead = false,
            secretValuesReturned = false,
            ssoConnectionChanged = false,
            message = runtimeActivated
                ? "Module 065 is now the active Microsoft services source for Module 010 preview, calendar, identity, and Microsoft 365 runtime metadata."
                : $"The {DisplayEnvironment(profile.EnvironmentMode)} services profile was preserved, but this API is running in the {DisplayEnvironment(runtimeEnvironment)} environment."
        });
    }

    private static NormalizedProfile Normalize(ServicesRuntimeProfileRequest? request)
    {
        var environmentMode = NormalizeEnvironment(request?.EnvironmentMode);
        if (string.IsNullOrWhiteSpace(environmentMode))
            return new(null, InvalidRequest("Environment must be Test or Production."));

        var tenantKey = NormalizeTenantKey(request?.TenantKey);
        if (string.IsNullOrWhiteSpace(tenantKey))
            return new(null, InvalidRequest("A stable tenant key is required."));
        if (!Guid.TryParse(request?.TenantId, out var tenantId))
            return new(null, InvalidRequest("The Microsoft services tenant ID must be a GUID."));
        if (!Guid.TryParse(request?.ClientId, out var clientId))
            return new(null, InvalidRequest("The Microsoft services application/client ID must be a GUID."));

        var graphScopes = NormalizeGraphScopes(request?.GraphScopes);
        var missing = RequiredDirectoryPermissions
            .Where(required => !graphScopes.Contains(required, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missing.Length > 0)
        {
            return new(null, Results.Json(new
            {
                module = "065",
                status = "directory_application_permissions_required",
                missingPermissions = missing,
                message = "Module 010 preview requires Directory.Read.All and User.Read.All application permissions with tenant admin consent."
            }, statusCode: StatusCodes.Status409Conflict));
        }

        var senderMailbox = (request?.SenderMailbox ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(senderMailbox) && !IsEmail(senderMailbox))
            return new(null, InvalidRequest("The Microsoft 365 sender mailbox must be a valid email address."));

        return new(new(
            environmentMode,
            tenantKey,
            tenantId.ToString("D"),
            clientId.ToString("D"),
            $"https://login.microsoftonline.com/{tenantId:D}",
            graphScopes,
            senderMailbox), null);
    }

    private static bool ApplyEnvironmentProfile(ServicesRuntimeProfile profile, string runtimeEnvironment)
    {
        var production = profile.EnvironmentMode == "production";
        var prefix = production ? "PROJECTPULSE_ENTRA_PRODUCTION_" : "PROJECTPULSE_ENTRA_TEST_";
        Environment.SetEnvironmentVariable(prefix + "TENANT_ID", profile.TenantId);
        Environment.SetEnvironmentVariable(prefix + "CLIENT_ID", profile.ClientId);
        Environment.SetEnvironmentVariable(prefix + "AUTHORITY", profile.AuthorityUrl);
        Environment.SetEnvironmentVariable(prefix + "GRAPH_SCOPE", string.Join(' ', profile.GraphScopes));

        var tenantToken = SanitizeEnvironmentToken(profile.TenantKey);
        Environment.SetEnvironmentVariable($"PROJECTPULSE_MICROSOFT_TENANT_{tenantToken}_TENANT_ID", profile.TenantId);
        Environment.SetEnvironmentVariable($"PROJECTPULSE_MICROSOFT_TENANT_{tenantToken}_CLIENT_ID", profile.ClientId);

        if (!string.Equals(runtimeEnvironment, profile.EnvironmentMode, StringComparison.OrdinalIgnoreCase))
            return false;

        Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE", profile.EnvironmentMode);
        Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_TENANT_ID", profile.TenantId);
        Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_ID", profile.ClientId);
        Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_AUTHORITY", profile.AuthorityUrl);
        Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_GRAPH_SCOPE", string.Join(' ', profile.GraphScopes));
        Environment.SetEnvironmentVariable("PROJECTPULSE_M365_TENANT_ID", profile.TenantId);
        Environment.SetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_ID", profile.ClientId);
        SetIfPresent("PROJECTPULSE_M365_SENDER_MAILBOX", profile.SenderMailbox);

        var secret = ServicesSecret(profile.EnvironmentMode, profile.TenantKey);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET", secret);
            Environment.SetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET", secret);
        }
        return true;
    }

    private static string ServicesSecret(string environmentMode, string tenantKey)
    {
        var tenantName = $"PROJECTPULSE_MICROSOFT_TENANT_{SanitizeEnvironmentToken(tenantKey)}_CLIENT_SECRET";
        var modeName = environmentMode == "production"
            ? "PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET"
            : "PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET";
        return First(
            Environment.GetEnvironmentVariable(tenantName),
            Environment.GetEnvironmentVariable(modeName));
    }

    private static async Task HydrateStoredProfileAsync()
    {
        foreach (var delay in new[] { 900, 2200, 4200 })
        {
            try
            {
                await Task.Delay(delay);
                var profile = await ReadStoredProfileAsync();
                if (profile is null) continue;
                ApplyEnvironmentProfile(profile, RuntimeEnvironmentMode(null));
                return;
            }
            catch
            {
                // Existing environment configuration remains authoritative until
                // a complete stored Module 065 services profile is available.
            }
        }
    }

    private static async Task<ServicesRuntimeProfile?> ReadStoredProfileAsync()
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
        if (!TryProperty(document.RootElement, "configuration", out var configuration)) return null;
        var notes = JsonString(configuration, "notes");
        if (!notes.StartsWith(ConfigurationMarker, StringComparison.Ordinal)) return null;
        using var stored = JsonDocument.Parse(notes[ConfigurationMarker.Length..]);
        var root = stored.RootElement;
        var activeKey = JsonString(root, "activeTenantKey");
        var activeMode = NormalizeEnvironment(JsonString(root, "activeEnvironmentMode"));
        if (!TryProperty(root, "tenants", out var tenants) || tenants.ValueKind != JsonValueKind.Array) return null;

        foreach (var tenant in tenants.EnumerateArray())
        {
            var tenantKey = First(JsonString(tenant, "key"), JsonString(tenant, "tenantKey"));
            var environmentMode = NormalizeEnvironment(JsonString(tenant, "environmentMode"));
            if ((!string.IsNullOrWhiteSpace(activeKey) && !tenantKey.Equals(activeKey, StringComparison.OrdinalIgnoreCase))
                && (!string.IsNullOrWhiteSpace(activeMode) && environmentMode != activeMode)) continue;
            if (!TryProperty(tenant, "services", out var services)) services = default;
            if (!Guid.TryParse(JsonString(tenant, "tenantId"), out var tenantId)) return null;
            if (!Guid.TryParse(First(JsonString(services, "clientId"), JsonString(tenant, "clientId")), out var clientId)) return null;
            var scopes = NormalizeGraphScopes(First(JsonString(services, "graphScopes"), JsonString(tenant, "graphScopes")));
            var senderMailbox = string.Empty;
            if (TryProperty(root, "mail", out var mail)) senderMailbox = JsonString(mail, "senderAddress");
            return new(
                environmentMode,
                NormalizeTenantKey(tenantKey),
                tenantId.ToString("D"),
                clientId.ToString("D"),
                $"https://login.microsoftonline.com/{tenantId:D}",
                scopes,
                senderMailbox);
        }
        return null;
    }

    private static async Task<AccessResult> ResolveAccessAsync(HttpContext context)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return new(null, Results.Json(new
            {
                status = "session_required",
                message = "A valid Pulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(r.role_code, ''), COALESCE(p.permission_code, '')
                FROM app_user_role_assignments ura
                JOIN app_roles r ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
                LEFT JOIN app_role_permissions rp ON rp.app_role_id=r.app_role_id
                LEFT JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id
                WHERE ura.user_id=@user_id AND ura.is_active=TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0))) roles.Add(reader.GetString(0));
                if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1))) permissions.Add(reader.GetString(1));
            }

            var administrator = ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, roles);
            if (!administrator && !permissions.Any(WritePermissions.Contains))
            {
                return new(null, Results.Json(new
                {
                    module = "065",
                    status = "microsoft_integration_manage_access_required",
                    message = "Manage Microsoft Integration authority is required."
                }, statusCode: StatusCodes.Status403Forbidden));
            }
            return new(new(userId.Value), null);
        }
        catch
        {
            return new(null, Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static string RuntimeEnvironmentMode(string? host)
    {
        foreach (var name in new[] { "PROJECTPULSE_ENVIRONMENT", "PROJECTPULSE_ENTRA_MODE", "ASPNETCORE_ENVIRONMENT" })
        {
            var value = NormalizeEnvironment(Environment.GetEnvironmentVariable(name));
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        var normalizedHost = (host ?? string.Empty).ToLowerInvariant();
        if (normalizedHost.Contains("-test.") || normalizedHost.Contains("localhost") || normalizedHost.Contains("127.0.0.1")) return "test";
        if (normalizedHost.Contains("-prod.") || normalizedHost.Contains("ussignal")) return "production";
        return string.Empty;
    }

    private static string[] NormalizeGraphScopes(string? value) =>
        (value ?? string.Empty)
            .Split(new[] { ' ', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();

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

    private static string NormalizeTenantKey(string? value)
    {
        var normalized = new string((value ?? string.Empty).Trim().ToLowerInvariant()
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(80)
            .ToArray());
        return normalized;
    }

    private static string SanitizeEnvironmentToken(string value) =>
        new((value ?? string.Empty).ToUpperInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')
            .ToArray());

    private static bool IsEmail(string value) =>
        value.Contains('@', StringComparison.Ordinal) && value.Length <= 320;

    private static void SetIfPresent(string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Environment.SetEnvironmentVariable(name, value.Trim());
    }

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
        if (!TryProperty(element, name, out var value) || value.ValueKind != JsonValueKind.String) return string.Empty;
        return value.GetString()?.Trim() ?? string.Empty;
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string DisplayEnvironment(string? environmentMode) =>
        environmentMode == "production" ? "Production" : environmentMode == "test" ? "Test" : "unknown";

    private static IResult InvalidRequest(string message) => Results.BadRequest(new
    {
        module = "065",
        status = "invalid_request",
        message
    });

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
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is bool isViewAs && isViewAs;

    private sealed record AccessResult(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(Guid UserId);
    private sealed record NormalizedProfile(ServicesRuntimeProfile? Profile, IResult? Failure);
    private sealed record ServicesRuntimeProfile(
        string EnvironmentMode,
        string TenantKey,
        string TenantId,
        string ClientId,
        string AuthorityUrl,
        string[] GraphScopes,
        string SenderMailbox);
    private sealed record ServicesRuntimeProfileRequest(
        string EnvironmentMode,
        string TenantKey,
        string TenantId,
        string ClientId,
        string GraphScopes,
        string SenderMailbox);
}
