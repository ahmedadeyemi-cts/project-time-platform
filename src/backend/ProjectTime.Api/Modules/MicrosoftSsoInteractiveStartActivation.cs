using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Ensures the environment-specific Module 065 SSO profile is projected into
/// the legacy interactive sign-in handler before /api/auth/sso/start executes.
/// This is required when a Test Container App uses ASPNETCORE_ENVIRONMENT=Production.
/// </summary>
public static class MicrosoftSsoInteractiveStartActivation
{
    private const string StartPath = "/api/auth/sso/start";
    private const string CallbackPath = "/api/auth/sso/callback";
    private const string ConfigurationMarker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";

    public static WebApplication UseMicrosoftSsoInteractiveStartActivation(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method)
                || !context.Request.Path.Equals(StartPath, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            try
            {
                var activation = await EnsureActiveProfileAsync(context);
                if (activation is not null)
                {
                    await activation.ExecuteAsync(context);
                    return;
                }

                await next();
            }
            catch (Exception exception)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MicrosoftSsoInteractiveStartActivation")
                    .LogWarning(
                        "Interactive Microsoft SSO start failed ({ExceptionType}); correlation {CorrelationId}.",
                        exception.GetType().Name,
                        context.TraceIdentifier);

                if (!context.Response.HasStarted)
                {
                    await Results.Json(new
                    {
                        module = "065",
                        status = "sso_interactive_start_failed",
                        correlationId = context.TraceIdentifier,
                        message = "ProjectPulse could not start Microsoft sign-in. Verify the active Module 065 Test or Production SSO profile and try again."
                    }, statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
                }
            }
        });

        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            foreach (var delay in new[] { 600, 1600, 3200 })
            {
                try
                {
                    await Task.Delay(delay);
                    var mode = MicrosoftEnvironmentRuntimeResolver.Resolve();
                    if (string.IsNullOrWhiteSpace(mode)) continue;
                    var profile = await ReadStoredProfileAsync(mode);
                    if (profile is null) continue;
                    Apply(profile);
                    return;
                }
                catch
                {
                    // The first interactive request performs the same guarded hydration.
                }
            }
        }));

        return app;
    }

    private static async Task<IResult?> EnsureActiveProfileAsync(HttpContext context)
    {
        var environmentMode = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if (string.IsNullOrWhiteSpace(environmentMode))
        {
            return Results.Json(new
            {
                module = "065",
                status = "microsoft_environment_unresolved",
                correlationId = context.TraceIdentifier,
                message = "ProjectPulse could not determine whether this is the Test or Production Microsoft environment."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var profile = await ReadStoredProfileAsync(environmentMode);
        if (profile is null)
        {
            return Results.Json(new
            {
                module = "065",
                status = "sso_profile_not_configured",
                environmentMode,
                correlationId = context.TraceIdentifier,
                message = $"Complete and save the {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} SSO connection in Module 065 before signing in with Microsoft."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var expectedRedirect = ExpectedRedirect(context);
        if (!profile.RedirectUri.Equals(expectedRedirect, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new
            {
                module = "065",
                status = "sso_redirect_host_mismatch",
                environmentMode,
                configuredRedirectUri = profile.RedirectUri,
                expectedRedirectUri = expectedRedirect,
                correlationId = context.TraceIdentifier,
                message = "The Module 065 redirect URI must exactly match this ProjectPulse environment and the Entra App Registration redirect URI."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var secret = ActiveSecret(environmentMode);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Results.Json(new
            {
                module = "065",
                status = "sso_client_secret_missing",
                environmentMode,
                correlationId = context.TraceIdentifier,
                message = $"Save the write-only {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} SSO client secret in Module 065 before signing in with Microsoft."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        Apply(profile, secret);
        context.Items["ProjectPulseSsoEnvironment"] = environmentMode;
        context.Items["ProjectPulseSsoProfileHydrated"] = true;
        return null;
    }

    private static void Apply(SsoProfile profile, string? suppliedSecret = null)
    {
        var prefix = profile.EnvironmentMode == "production"
            ? "PROJECTPULSE_ENTRA_PRODUCTION_SSO_"
            : "PROJECTPULSE_ENTRA_TEST_SSO_";
        Environment.SetEnvironmentVariable(prefix + "TENANT_ID", profile.TenantId);
        Environment.SetEnvironmentVariable(prefix + "CLIENT_ID", profile.ClientId);
        Environment.SetEnvironmentVariable(prefix + "AUTHORITY", profile.AuthorityUrl);
        Environment.SetEnvironmentVariable(prefix + "REDIRECT_URI", profile.RedirectUri);
        Environment.SetEnvironmentVariable(prefix + "ALLOWED_DOMAINS", profile.AllowedDomains);

        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_MODE", profile.EnvironmentMode);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_TENANT_ID", profile.TenantId);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_CLIENT_ID", profile.ClientId);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_AUTHORITY", profile.AuthorityUrl);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_REDIRECT_URI", profile.RedirectUri);
        Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_ALLOWED_DOMAINS", profile.AllowedDomains);

        var secret = string.IsNullOrWhiteSpace(suppliedSecret)
            ? ActiveSecret(profile.EnvironmentMode)
            : suppliedSecret;
        if (!string.IsNullOrWhiteSpace(secret))
            Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_CLIENT_SECRET", secret);
    }

    private static async Task<SsoProfile?> ReadStoredProfileAsync(string environmentMode)
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
        if (!TryProperty(stored.RootElement, "tenants", out var tenants)
            || tenants.ValueKind != JsonValueKind.Array) return null;

        foreach (var tenant in tenants.EnumerateArray())
        {
            var mode = MicrosoftEnvironmentRuntimeResolver.Normalize(JsonString(tenant, "environmentMode"));
            if (!mode.Equals(environmentMode, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Guid.TryParse(JsonString(tenant, "tenantId"), out var tenantId)) return null;
            if (!TryProperty(tenant, "sso", out var sso)) return null;
            if (!Guid.TryParse(First(JsonString(sso, "clientId"), JsonString(tenant, "ssoClientId")), out var clientId)) return null;
            var redirectUri = First(JsonString(sso, "redirectUri"), JsonString(tenant, "redirectUri"));
            var domains = First(JsonString(sso, "allowedDomains"), JsonString(tenant, "ssoAllowedDomains"));
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect)
                || !redirect.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !redirect.AbsolutePath.TrimEnd('/').Equals(CallbackPath, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(domains)) return null;

            return new(
                mode,
                tenantId.ToString("D"),
                clientId.ToString("D"),
                $"https://login.microsoftonline.com/{tenantId:D}",
                redirect.AbsoluteUri.TrimEnd('/'),
                string.Join(',', domains
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => value.ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        return null;
    }

    private static string ExpectedRedirect(HttpContext context)
    {
        if (context.Items.TryGetValue(ProjectPulsePublicOriginCompatibility.PublicOriginItem, out var originValue)
            && originValue is Uri publicOrigin)
            return $"{publicOrigin.GetLeftPart(UriPartial.Authority)}{CallbackPath}";
        return $"{context.Request.Scheme}://{context.Request.Host}{CallbackPath}";
    }

    private static string ActiveSecret(string environmentMode)
    {
        var environmentName = environmentMode == "production"
            ? "PROJECTPULSE_ENTRA_PRODUCTION_SSO_CLIENT_SECRET"
            : "PROJECTPULSE_ENTRA_TEST_SSO_CLIENT_SECRET";
        return First(
            Environment.GetEnvironmentVariable(environmentName),
            MicrosoftEnvironmentRuntimeResolver.Normalize(Environment.GetEnvironmentVariable("PROJECTPULSE_SSO_MODE")) == environmentMode
                ? Environment.GetEnvironmentVariable("PROJECTPULSE_SSO_CLIENT_SECRET")
                : string.Empty);
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
            MaxPoolSize = 5
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

    private sealed record SsoProfile(
        string EnvironmentMode,
        string TenantId,
        string ClientId,
        string AuthorityUrl,
        string RedirectUri,
        string AllowedDomains);
}
