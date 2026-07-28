using System.Security.Cryptography;
using System.Text;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Resolves the externally visible ProjectPulse origin without trusting an
/// arbitrary browser-supplied host. Azure Container Apps terminates TLS before
/// the web container, so the web proxy can observe HTTP even though the public
/// request is HTTPS. This compatibility boundary accepts only approved
/// ProjectPulse environment hosts or explicitly configured public URLs.
/// </summary>
public static class ProjectPulsePublicOriginCompatibility
{
    internal const string PublicOriginItem = "ProjectPulsePublicOriginUri";
    internal const string PublicOriginSourceItem = "ProjectPulsePublicOriginSource";

    private static readonly string[] PublicUrlEnvironmentNames =
    [
        "PUBLIC_URL",
        "PROJECTPULSE_PUBLIC_URL",
        "PROJECTPULSE_WEB_URL",
        "PROJECTPULSE_PUBLIC_BASE_URL"
    ];

    public static WebApplication UseProjectPulsePublicOriginCompatibility(this WebApplication app)
    {
        EnsureCrmErpEncryptionKey();

        app.Use(async (context, next) =>
        {
            if (IsRelevantRequest(context)
                && TryResolveProxyOrConfiguredOrigin(context, out var publicOrigin, out var source))
            {
                context.Request.Scheme = publicOrigin.Scheme;
                context.Request.Host = HostString.FromUriComponent(publicOrigin.Authority);
                context.Items[PublicOriginItem] = publicOrigin;
                context.Items[PublicOriginSourceItem] = source;
            }

            await next();
        });

        return app;
    }

    internal static bool TryResolveProxyOrConfiguredOrigin(
        HttpContext context,
        out Uri publicOrigin,
        out string source)
    {
        if (context.Items.TryGetValue(PublicOriginItem, out var existing)
            && existing is Uri existingUri
            && TrustedHost(existingUri.Host, context))
        {
            publicOrigin = existingUri;
            source = context.Items.TryGetValue(PublicOriginSourceItem, out var existingSource)
                ? existingSource?.ToString() ?? "existing"
                : "existing";
            return true;
        }

        var request = context.Request;
        var hosts = ForwardedValues(request.Headers["X-Forwarded-Host"].ToString());
        var protocols = ForwardedValues(request.Headers["X-Forwarded-Proto"].ToString());
        for (var index = 0; index < hosts.Length; index++)
        {
            var candidateHost = hosts[index];
            var candidateProto = protocols.Length > index
                ? protocols[index]
                : protocols.FirstOrDefault() ?? string.Empty;
            if (!TryApprovedAuthority(candidateHost, context, out var approvedAuthority)) continue;
            foreach (var scheme in CandidateSchemes(candidateProto, approvedAuthority.Host))
            {
                if (TryOrigin($"{scheme}://{approvedAuthority.Authority}", context, out publicOrigin))
                {
                    source = "trusted_forwarded_origin";
                    return true;
                }
            }
        }

        foreach (var forwarded in ForwardedValues(request.Headers["Forwarded"].ToString()))
        {
            ReadForwardedHeader(forwarded, out var forwardedHost, out var forwardedProto);
            if (string.IsNullOrWhiteSpace(forwardedHost)
                || !TryApprovedAuthority(forwardedHost, context, out var approvedAuthority)) continue;
            foreach (var scheme in CandidateSchemes(forwardedProto, approvedAuthority.Host))
            {
                if (TryOrigin($"{scheme}://{approvedAuthority.Authority}", context, out publicOrigin))
                {
                    source = "trusted_forwarded_header";
                    return true;
                }
            }
        }

        foreach (var name in PublicUrlEnvironmentNames)
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (TryOrigin(configured, context, out publicOrigin))
            {
                source = $"configured:{name}";
                return true;
            }
        }

        if (TryApprovedAuthority(request.Host.Value, context, out var requestAuthority))
        {
            foreach (var scheme in CandidateSchemes(request.Scheme, requestAuthority.Host))
            {
                if (TryOrigin($"{scheme}://{requestAuthority.Authority}", context, out publicOrigin))
                {
                    source = "approved_request_host";
                    return true;
                }
            }
        }

        publicOrigin = null!;
        source = string.Empty;
        return false;
    }

    internal static bool TryBrowserOrigin(HttpContext context, out Uri publicOrigin, out string source)
    {
        var request = context.Request;
        foreach (var originHeader in ForwardedValues(request.Headers["Origin"].ToString()))
        {
            if (TryOrigin(originHeader, context, out publicOrigin))
            {
                source = "browser_origin";
                return true;
            }
        }

        foreach (var refererHeader in ForwardedValues(request.Headers["Referer"].ToString()))
        {
            if (Uri.TryCreate(refererHeader, UriKind.Absolute, out var referer)
                && TryOrigin(referer.GetLeftPart(UriPartial.Authority), context, out publicOrigin))
            {
                source = "browser_referer";
                return true;
            }
        }

        publicOrigin = null!;
        source = string.Empty;
        return false;
    }

    internal static bool TryOrigin(string? value, HttpContext context, out Uri origin)
    {
        origin = null!;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim().Trim('"'), UriKind.Absolute, out var parsed)
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

    internal static bool TrustedHost(string host, HttpContext context)
    {
        if (IsApprovedEnvironmentHost(host)) return true;

        foreach (var name in PublicUrlEnvironmentNames)
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri)
                && host.Equals(configuredUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var requestHost = context.Request.Host.Host;
        return IsApprovedEnvironmentHost(requestHost)
            && host.Equals(requestHost, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsApprovedEnvironmentHost(string? host)
    {
        var value = (host ?? string.Empty).Trim().TrimEnd('.');
        return value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || value.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".onenecklab.com", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith(".ussignal.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRelevantRequest(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        return path.StartsWith("/api/microsoft-integration/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/sso/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/admin/azure/users/preview", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/integrations/026/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/public/integrations/026/oauth/callback", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryApprovedAuthority(string value, HttpContext context, out Uri authority)
    {
        authority = null!;
        var text = (value ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!text.Contains("://", StringComparison.Ordinal)) text = $"https://{text}";
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            || !string.IsNullOrWhiteSpace(parsed.UserInfo)
            || !TrustedHost(parsed.Host, context))
        {
            return false;
        }

        authority = parsed;
        return true;
    }

    private static IEnumerable<string> CandidateSchemes(string? forwardedScheme, string host)
    {
        var local = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
        var normalized = ForwardedValues(forwardedScheme).FirstOrDefault() ?? string.Empty;

        if (normalized.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            yield return Uri.UriSchemeHttps;
        else if (local && normalized.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            yield return Uri.UriSchemeHttp;

        // Public ProjectPulse environments are HTTPS-only. This deliberately
        // upgrades the trusted public host when TLS was terminated upstream and
        // the inner web proxy observed HTTP.
        yield return local ? Uri.UriSchemeHttp : Uri.UriSchemeHttps;
    }

    private static bool ApprovedScheme(Uri origin) =>
        origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || (origin.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && (origin.IsLoopback
                || origin.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));

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

    private static string[] ForwardedValues(string? value) =>
        (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static void EnsureCrmErpEncryptionKey()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY")))
            return;

        var seed = First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_MICROSOFT_INTEGRATION_SECRET_KEY"),
            Environment.GetEnvironmentVariable("PTP_DB_PASSWORD"));
        if (string.IsNullOrWhiteSpace(seed)) return;

        var material = Encoding.UTF8.GetBytes($"ProjectPulse-CRM-ERP-Integration:{seed}");
        try
        {
            var key = SHA256.HashData(material);
            try
            {
                Environment.SetEnvironmentVariable(
                    "PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY",
                    Convert.ToBase64String(key));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
