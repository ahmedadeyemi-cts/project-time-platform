using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Runtime compatibility boundary that wires the separately stored SSO profile
/// into the existing authentication handlers without changing Graph/services
/// credentials. It also prevents request-controlled SSO discovery URLs.
/// </summary>
public static class MicrosoftSsoRuntimeCompatibility
{
    private const string TestPath = "/api/microsoft-integration/sso-test";
    private const string ApplyPath = "/api/microsoft-integration/sso-apply-profile";
    private const string CallbackPath = "/api/auth/sso/callback";

    private static readonly HashSet<string> WritePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_ENTRA_SECRET",
        "MANAGE_GLOBAL_MAIL_CONFIGURATION",
        "MANAGE_GLOBAL_MAIL"
    };

    public static WebApplication UseMicrosoftSsoRuntimeCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsPost(context.Request.Method)
                || !context.Request.Path.Equals(TestPath, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            context.Request.EnableBuffering();
            JsonObject payload;
            try
            {
                using var reader = new StreamReader(
                    context.Request.Body,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                var raw = await reader.ReadToEndAsync(context.RequestAborted);
                context.Request.Body.Position = 0;
                payload = JsonNode.Parse(raw) as JsonObject ?? new JsonObject();
            }
            catch
            {
                await Results.BadRequest(new
                {
                    module = "065",
                    status = "invalid_sso_test_request",
                    message = "A valid SSO configuration-test request is required."
                }).ExecuteAsync(context);
                return;
            }

            var tenantId = FirstString(payload, "tenantId", "tenant_id");
            if (!Guid.TryParse(tenantId, out var tenantGuid))
            {
                await Results.BadRequest(new
                {
                    module = "065",
                    status = "invalid_sso_tenant_id",
                    message = "The SSO tenant ID must be a Microsoft Entra tenant GUID."
                }).ExecuteAsync(context);
                return;
            }

            var clientId = FirstString(payload, "clientId", "client_id", "ssoClientId", "sso_client_id");
            if (!Guid.TryParse(clientId, out var clientGuid))
            {
                await Results.BadRequest(new
                {
                    module = "065",
                    status = "invalid_sso_client_id",
                    message = "The SSO application/client ID must be a GUID."
                }).ExecuteAsync(context);
                return;
            }

            var redirectValue = FirstString(payload, "redirectUri", "redirect_uri");
            if (!TryRedirectUri(redirectValue, out var redirectUri))
            {
                await Results.BadRequest(new
                {
                    module = "065",
                    status = "invalid_sso_redirect_uri",
                    expectedPath = CallbackPath,
                    message = $"The SSO redirect URI must use HTTPS and end with {CallbackPath}."
                }).ExecuteAsync(context);
                return;
            }

            payload["tenantId"] = tenantGuid.ToString("D");
            payload["clientId"] = clientGuid.ToString("D");
            payload["authorityUrl"] = MicrosoftAuthority(tenantGuid);
            payload["redirectUri"] = redirectUri;
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
            context.Request.Body = new MemoryStream(bytes, writable: false);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = "application/json";

            await next();
        });
        return app;
    }

    public static WebApplication MapMicrosoftSsoRuntimeProfileEndpoints(this WebApplication app)
    {
        app.MapPost(ApplyPath, (Func<HttpContext, Task<IResult>>)ApplyProfileAsync);
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
                message = "Exit Administrator View-As before applying SSO configuration."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        SsoRuntimeProfileRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SsoRuntimeProfileRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid SSO runtime profile is required.");
        }

        var environmentMode = NormalizeEnvironment(request?.EnvironmentMode);
        if (string.IsNullOrWhiteSpace(environmentMode))
            return InvalidRequest("Environment must be Test or Production.");
        if (!Guid.TryParse(request?.TenantId, out var tenantId))
            return InvalidRequest("The SSO tenant ID must be a Microsoft Entra tenant GUID.");
        if (!Guid.TryParse(request?.ClientId, out var clientId))
            return InvalidRequest("The SSO application/client ID must be a GUID.");
        if (!TryRedirectUri(request?.RedirectUri, out var redirectUri))
            return InvalidRequest($"The redirect URI must use HTTPS and end with {CallbackPath}.");

        var allowedDomains = NormalizeDomains(request?.AllowedDomains);
        if (allowedDomains.Length == 0)
            return InvalidRequest("At least one allowed SSO domain is required.");

        var profile = new RuntimeProfile(
            environmentMode,
            tenantId.ToString("D"),
            clientId.ToString("D"),
            MicrosoftAuthority(tenantId),
            redirectUri,
            string.Join(',', allowedDomains));

        var runtimeEnvironment = RuntimeEnvironmentMode(context.Request.Host.Host);
        if (runtimeEnvironment == profile.EnvironmentMode
            && !RedirectMatchesCurrentHost(profile.RedirectUri, context))
        {
            return Results.Json(new
            {
                module = "065",
                status = "sso_redirect_host_mismatch",
                configuredRedirectUri = profile.RedirectUri,
                expectedRedirectUri = ExpectedRedirectUri(context),
                message = "The active SSO redirect URI must exactly match this ProjectPulse environment and the Entra App Registration redirect URI."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var runtimeActivated = ApplyEnvironmentProfile(profile, runtimeEnvironment);
        var secretAvailable = !string.IsNullOrWhiteSpace(ActiveSsoSecret(profile.EnvironmentMode));

        return Results.Ok(new
        {
            module = "065",
            status = runtimeActivated
                ? "sso_runtime_profile_applied"
                : "sso_profile_saved_for_other_environment",
            profile.EnvironmentMode,
            runtimeEnvironment,
            connectionPurpose = "sso_app_registration",
            runtimeActivated,
            profile.TenantId,
            profile.ClientId,
            profile.AuthorityUrl,
            profile.RedirectUri,
            allowedDomains,
            secretAvailable,
            servicesConnectionChanged = false,
            graphEnvironmentChanged = false,
            secretReturned = false,
            message = runtimeActivated
                ? "The Module 065 SSO profile is active for the running authentication flow without changing Microsoft services credentials."
                : $"The {DisplayEnvironment(profile.EnvironmentMode)} SSO profile was preserved, but this API is running in the {DisplayEnvironment(runtimeEnvironment)} environment."
        });
    }

    private static bool ApplyEnvironmentProfile(RuntimeProfile profile, string runtimeEnvironment)
    {
        var prefix = profile.EnvironmentMode == "production"
            ? "PROJECTPULSE_ENTRA_PRODUCTION_SSO_"
            : "PROJECTPULSE_ENTRA_TEST_SSO_";
        Environment.SetEnvironmentVariable(prefix + "TENANT_ID", profile.TenantId);
        Environment.SetEnvironmentVariable(prefix + "CLIENT_ID", profile.ClientId);
        Environment.SetEnvironmentVariable(prefix + "AUTHORITY", profile.AuthorityUrl);
        Environment.SetEnvironmentVariable(prefix + "REDIRECT_URI", profile.RedirectUri);
        Environment.SetEnvironmentVariable(prefix + "ALLOWED_DOMAINS", profile.AllowedDomains);

        if (!string.Equals(runtimeEnvironment, profile.EnvironmentMode, StringComparison.OrdinalIgnoreCase))
            return false;

        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_MODE", profile.EnvironmentMode);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_TENANT_ID", profile.TenantId);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_CLIENT_ID", profile.ClientId);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_AUTHORITY", profile.AuthorityUrl);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_REDIRECT_URI", profile.RedirectUri);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_ALLOWED_DOMAINS", profile.AllowedDomains);

        var secret = ActiveSsoSecret(profile.EnvironmentMode);
        if (!string.IsNullOrWhiteSpace(secret))
            Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_CLIENT_SECRET", secret);
        return true;
    }

    private static string ActiveSsoSecret(string environmentMode)
    {
        var environmentName = environmentMode == "production"
            ? "PROJECTPULSE_ENTRA_PRODUCTION_SSO_CLIENT_SECRET"
            : "PROJECTPULSE_ENTRA_TEST_SSO_CLIENT_SECRET";
        return First(
            Environment.GetEnvironmentVariable(environmentName),
            NormalizeEnvironment(Environment.GetEnvironmentVariable("PROJECTPULSE_SSO_MODE")) == environmentMode
                ? Environment.GetEnvironmentVariable("PROJECTPULSE_SSO_CLIENT_SECRET")
                : string.Empty);
    }

    private static string RuntimeEnvironmentMode(string? host)
    {
        foreach (var name in new[] { "PROJECTPULSE_ENVIRONMENT", "PROJECTPULSE_SSO_MODE", "PROJECTPULSE_ENTRA_MODE", "ASPNETCORE_ENVIRONMENT" })
        {
            var value = NormalizeEnvironment(Environment.GetEnvironmentVariable(name));
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        var normalizedHost = (host ?? string.Empty).ToLowerInvariant();
        if (normalizedHost.Contains("-test.") || normalizedHost.Contains("localhost") || normalizedHost.Contains("127.0.0.1")) return "test";
        if (normalizedHost.Contains("-prod.") || normalizedHost.Contains("ussignal")) return "production";
        return string.Empty;
    }

    private static bool RedirectMatchesCurrentHost(string redirectUri, HttpContext context)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) return false;
        return uri.Host.Equals(context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Scheme.Equals(context.Request.Scheme, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.TrimEnd('/').Equals(CallbackPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExpectedRedirectUri(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host}{CallbackPath}";

    private static async Task<AccessResult> ResolveAccessAsync(HttpContext context)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return new(null, Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
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
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
                LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
                WHERE ura.user_id = @user_id
                  AND ura.is_active = TRUE;
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

    private static string[] NormalizeDomains(string? value) =>
        (value ?? string.Empty)
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(domain => domain.ToLowerInvariant())
            .Where(domain => domain.Length <= 253
                && domain.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

    private static bool TryRedirectUri(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)) return false;
        var local = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !(local && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (!uri.AbsolutePath.TrimEnd('/').Equals(CallbackPath, StringComparison.OrdinalIgnoreCase))
            return false;
        normalized = uri.AbsoluteUri.TrimEnd('/');
        return true;
    }

    private static string MicrosoftAuthority(Guid tenantId) =>
        $"https://login.microsoftonline.com/{tenantId:D}";

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

    private static string FirstString(JsonObject payload, params string[] names)
    {
        foreach (var name in names)
        {
            if (payload.TryGetPropertyValue(name, out var node)
                && node is JsonValue value
                && value.TryGetValue<string>(out var text)
                && !string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }
        return string.Empty;
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
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private sealed record AccessResult(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(Guid UserId);
    private sealed record RuntimeProfile(
        string EnvironmentMode,
        string TenantId,
        string ClientId,
        string AuthorityUrl,
        string RedirectUri,
        string AllowedDomains);
    private sealed record SsoRuntimeProfileRequest(
        string EnvironmentMode,
        string TenantId,
        string ClientId,
        string RedirectUri,
        string AllowedDomains);
}
