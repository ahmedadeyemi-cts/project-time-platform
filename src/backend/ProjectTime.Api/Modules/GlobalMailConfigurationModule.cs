namespace ProjectTime.Api.Modules;

/// <summary>
/// Compatibility registration point retained for the former Module 067.
/// Module 065 owns Microsoft Integration; Module 010 owns Entra directory-user import.
/// Program.cs continues to call this existing method once, so no broad startup edit is required.
/// </summary>
public static class GlobalMailConfigurationModule
{
    private const string MicrosoftSsoRuntimePrefix = "/api/microsoft-integration/sso-";

    public static WebApplication MapGlobalMailConfigurationEndpoints(this WebApplication app)
    {
        app.UseMicrosoftIntegrationSecurityCompatibility();
        app.UseMicrosoftPublicSsoOriginCompatibility();
        app.UseModuleAvailabilityReadContinuityCompatibility();
        app.UseMicrosoftSsoRuntimeCompatibility();
        app.UseMicrosoftSmtpCredentialProjectionCompatibility();
        MicrosoftIntegrationModule.MapEndpoints(app);
        app.MapMicrosoftSsoConnectionProfileEndpoints();
        app.MapMicrosoftSsoRuntimeProfileEndpoints();
        app.MapMicrosoftServicesRuntimeProfileEndpoints();
        app.MapMicrosoftMailRuntimeConfigurationEndpoints();
        app.MapMicrosoftMailTransportTestEndpoints();
        AzureDirectoryImportModule.MapEndpoints(app);
        // Additive registration only: Module 021 consumes the authoritative
        // Module 026 SELL connection without changing Microsoft Integration.
        app.MapCustomerDirectorySellSyncEndpoints();
        return app;
    }

    private static WebApplication UseMicrosoftPublicSsoOriginCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith(MicrosoftSsoRuntimePrefix, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (!TryResolvePublicOrigin(context, out var publicOrigin, out var failure))
            {
                await Results.BadRequest(new
                {
                    module = "065",
                    status = "invalid_forwarded_public_origin",
                    message = failure
                }).ExecuteAsync(context);
                return;
            }

            context.Request.Scheme = publicOrigin.Scheme;
            context.Request.Host = HostString.FromUriComponent(publicOrigin.Authority);
            context.Items["ProjectPulsePublicOrigin"] = publicOrigin.GetLeftPart(UriPartial.Authority);
            await next();
        });
        return app;
    }

    private static bool TryResolvePublicOrigin(
        HttpContext context,
        out Uri publicOrigin,
        out string failure)
    {
        var request = context.Request;
        var forwardedHost = FirstForwardedValue(request.Headers["X-Forwarded-Host"].ToString());
        var forwardedProto = FirstForwardedValue(request.Headers["X-Forwarded-Proto"].ToString());
        if (string.IsNullOrWhiteSpace(forwardedHost)
            && string.IsNullOrWhiteSpace(forwardedProto))
        {
            ReadForwardedHeader(
                FirstForwardedValue(request.Headers["Forwarded"].ToString()),
                out forwardedHost,
                out forwardedProto);
        }

        if (!string.IsNullOrWhiteSpace(forwardedHost)
            || !string.IsNullOrWhiteSpace(forwardedProto))
        {
            var scheme = First(forwardedProto, request.Scheme);
            var authority = First(forwardedHost, request.Host.Value);
            if (!TryOrigin($"{scheme}://{authority}", context, out publicOrigin))
            {
                failure = "The forwarded ProjectPulse public origin is invalid or outside the approved environment domains.";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        var originHeader = FirstForwardedValue(request.Headers["Origin"].ToString());
        if (!string.IsNullOrWhiteSpace(originHeader)
            && TryOrigin(originHeader, context, out publicOrigin))
        {
            failure = string.Empty;
            return true;
        }

        var refererHeader = FirstForwardedValue(request.Headers["Referer"].ToString());
        if (!string.IsNullOrWhiteSpace(refererHeader)
            && Uri.TryCreate(refererHeader, UriKind.Absolute, out var referer)
            && TryOrigin(referer.GetLeftPart(UriPartial.Authority), context, out publicOrigin))
        {
            failure = string.Empty;
            return true;
        }

        if (TryOrigin($"{request.Scheme}://{request.Host}", context, out publicOrigin))
        {
            failure = string.Empty;
            return true;
        }

        publicOrigin = null!;
        failure = "ProjectPulse could not determine a trusted public origin for the SSO callback.";
        return false;
    }

    private static bool TryOrigin(string value, HttpContext context, out Uri origin)
    {
        origin = null!;
        if (!Uri.TryCreate(value.Trim().Trim('"'), UriKind.Absolute, out var parsed)
            || !string.IsNullOrWhiteSpace(parsed.UserInfo)
            || parsed.AbsolutePath != "/"
            || !string.IsNullOrWhiteSpace(parsed.Query)
            || !string.IsNullOrWhiteSpace(parsed.Fragment)
            || !ApprovedScheme(parsed)
            || !TrustedHost(parsed.Host, context))
        {
            return false;
        }

        origin = parsed;
        return true;
    }

    private static bool ApprovedScheme(Uri origin) =>
        origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || (origin.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && (origin.IsLoopback
                || origin.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));

    private static bool TrustedHost(string host, HttpContext context)
    {
        if (host.Equals(context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".onenecklab.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".ussignal.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var name in new[] { "PUBLIC_URL", "PROJECTPULSE_PUBLIC_URL", "PROJECTPULSE_WEB_URL" })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri)
                && host.Equals(configuredUri.Host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void ReadForwardedHeader(string value, out string host, out string proto)
    {
        host = string.Empty;
        proto = string.Empty;
        foreach (var part in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2) continue;
            if (pair[0].Equals("host", StringComparison.OrdinalIgnoreCase)) host = pair[1].Trim('"');
            if (pair[0].Equals("proto", StringComparison.OrdinalIgnoreCase)) proto = pair[1].Trim('"');
        }
    }

    private static string FirstForwardedValue(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
