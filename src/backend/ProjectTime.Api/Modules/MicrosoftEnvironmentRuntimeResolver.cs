namespace ProjectTime.Api.Modules;

/// <summary>
/// Resolves the Pulse Microsoft environment from an explicit Microsoft
/// override or the trusted public host before considering generic application
/// and ASP.NET runtime modes. Azure Container Apps normally runs the Test
/// application with ASPNETCORE_ENVIRONMENT=Production, so that generic value
/// cannot decide whether the OneNeck Lab or US Signal Microsoft profile is active.
/// </summary>
public static class MicrosoftEnvironmentRuntimeResolver
{
    private const string ApplicationEnvironmentVariable = "PROJECTPULSE_" + "ENVIRONMENT";

    public static WebApplication UseMicrosoftEnvironmentRuntimeCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var relevant = path.StartsWith("/api/microsoft-integration/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/auth/sso/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/admin/azure/", StringComparison.OrdinalIgnoreCase);

            if (relevant)
            {
                var mode = Resolve(context);
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    Environment.SetEnvironmentVariable(ApplicationEnvironmentVariable, mode);
                    Environment.SetEnvironmentVariable("PROJECTPULSE_MICROSOFT_ENVIRONMENT", mode);
                    context.Items["ProjectPulseMicrosoftEnvironment"] = mode;
                }
            }

            await next();
        });
        return app;
    }

    public static string Resolve(HttpContext? context = null, string? host = null)
    {
        var explicitMicrosoftMode = Normalize(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MICROSOFT_ENVIRONMENT"));
        if (!string.IsNullOrWhiteSpace(explicitMicrosoftMode)) return explicitMicrosoftMode;

        var trustedHost = ResolveHost(context, host);
        var hostMode = FromHost(trustedHost);
        if (!string.IsNullOrWhiteSpace(hostMode)) return hostMode;

        // Background workers do not have an HttpContext. Resolve the same
        // approved public host from deployment configuration before considering
        // generic application/runtime modes.
        foreach (var name in new[]
                 {
                     "PROJECTPULSE_PUBLIC_URL",
                     "PROJECTPULSE_PUBLIC_BASE_URL",
                     "PROJECTPULSE_WEB_URL",
                     "PUBLIC_URL"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri)) continue;
            var configuredMode = FromHost(uri.Host);
            if (!string.IsNullOrWhiteSpace(configuredMode)) return configuredMode;
        }

        foreach (var name in new[]
                 {
                     "PROJECTPULSE_ENVIRONMENT",
                     "PROJECTPULSE_SSO_MODE",
                     "PROJECTPULSE_ENTRA_MODE"
                 })
        {
            var applicationMode = Normalize(Environment.GetEnvironmentVariable(name));
            if (!string.IsNullOrWhiteSpace(applicationMode)) return applicationMode;
        }

        foreach (var name in new[] { "DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT" })
        {
            var frameworkMode = Normalize(Environment.GetEnvironmentVariable(name));
            if (!string.IsNullOrWhiteSpace(frameworkMode)) return frameworkMode;
        }

        return string.Empty;
    }

    public static string FromHost(string? host)
    {
        var value = (host ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        if (value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || value.Contains("-test.", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".onenecklab.com", StringComparison.OrdinalIgnoreCase))
            return "test";

        if (value.Contains("-prod.", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".ussignal.com", StringComparison.OrdinalIgnoreCase))
            return "production";

        return string.Empty;
    }

    public static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "test" or "testing" or "qa" or "uat" or "development" or "dev" or "onenecklab" => "test",
            "production" or "prod" or "ussignal" => "production",
            _ => string.Empty
        };
    }

    public static string Display(string? value) =>
        Normalize(value) == "production" ? "Production" : Normalize(value) == "test" ? "Test" : "Unknown";

    private static string ResolveHost(HttpContext? context, string? host)
    {
        if (context?.Items.TryGetValue(ProjectPulsePublicOriginCompatibility.PublicOriginItem, out var originValue) == true
            && originValue is Uri publicOrigin)
            return publicOrigin.Host;

        if (context is not null
            && ProjectPulsePublicOriginCompatibility.TryResolveProxyOrConfiguredOrigin(
                context,
                out var resolvedOrigin,
                out _))
            return resolvedOrigin.Host;

        if (!string.IsNullOrWhiteSpace(host)) return host.Trim();
        return context?.Request.Host.Host ?? string.Empty;
    }
}
