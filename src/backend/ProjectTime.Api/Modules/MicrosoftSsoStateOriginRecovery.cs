using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Recovers the exact public callback URI that was persisted when Microsoft SSO
/// started. This is used only when the callback arrives without a trustworthy
/// proxy/public-origin signal. The state row is read without consuming it; the
/// existing callback handler remains the only atomic state consumer.
/// </summary>
internal static class MicrosoftSsoStateOriginRecovery
{
    internal const string CallbackPath = "/api/auth/sso/callback";

    internal static async Task<StateOriginResult> TryRecoverAsync(
        HttpContext context,
        string? state,
        CancellationToken cancellationToken = default)
    {
        var stateToken = (state ?? string.Empty).Trim();
        if (stateToken.Length is < 20 or > 512)
            return StateOriginResult.Fail("state_token_missing_or_invalid");

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            return StateOriginResult.Fail("sso_state_store_unavailable");

        string? redirectUri;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT redirect_uri
                FROM auth_sso_state
                WHERE state_token = @state_token
                  AND consumed_at IS NULL
                  AND expires_at > NOW()
                LIMIT 1;
                """, connection);
            command.Parameters.AddWithValue("state_token", stateToken);
            redirectUri = await command.ExecuteScalarAsync(cancellationToken) as string;
        }
        catch
        {
            return StateOriginResult.Fail("sso_state_store_unavailable");
        }

        if (string.IsNullOrWhiteSpace(redirectUri))
            return StateOriginResult.Fail("sso_state_not_found_or_expired");

        if (!TryValidateStoredRedirectUri(
                redirectUri,
                context,
                out var normalizedRedirectUri,
                out var publicOrigin,
                out var failureCode))
        {
            return StateOriginResult.Fail(failureCode);
        }

        return StateOriginResult.Success(
            publicOrigin,
            normalizedRedirectUri,
            "unconsumed_auth_sso_state_redirect_uri");
    }

    internal static bool TryValidateStoredRedirectUri(
        string? value,
        HttpContext context,
        out string normalizedRedirectUri,
        out Uri publicOrigin,
        out string failureCode)
    {
        normalizedRedirectUri = string.Empty;
        publicOrigin = null!;
        failureCode = string.Empty;

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed))
        {
            failureCode = "stored_redirect_uri_invalid";
            return false;
        }

        if (!parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            failureCode = "stored_redirect_uri_https_required";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(parsed.UserInfo))
        {
            failureCode = "stored_redirect_uri_user_info_rejected";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(parsed.Query)
            || !string.IsNullOrWhiteSpace(parsed.Fragment))
        {
            failureCode = "stored_redirect_uri_query_or_fragment_rejected";
            return false;
        }

        if (!parsed.AbsolutePath.Equals(CallbackPath, StringComparison.OrdinalIgnoreCase))
        {
            failureCode = "stored_redirect_uri_callback_path_mismatch";
            return false;
        }

        if (!parsed.IsDefaultPort && parsed.Port != 443)
        {
            failureCode = "stored_redirect_uri_port_rejected";
            return false;
        }

        var host = parsed.Host.Trim().TrimEnd('.');
        if (IsInternalAzureHost(host))
        {
            failureCode = "stored_redirect_uri_internal_host_rejected";
            return false;
        }

        var redirectEnvironment = EnvironmentFromApprovedHost(host);
        if (string.IsNullOrWhiteSpace(redirectEnvironment))
        {
            failureCode = "stored_redirect_uri_host_not_approved";
            return false;
        }

        var runtimeEnvironment = RuntimeEnvironment(context);
        if (string.IsNullOrWhiteSpace(runtimeEnvironment)
            || !redirectEnvironment.Equals(runtimeEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            failureCode = "stored_redirect_uri_environment_mismatch";
            return false;
        }

        normalizedRedirectUri = parsed.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.UriEscaped);
        publicOrigin = new Uri(parsed.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
        return true;
    }

    private static string RuntimeEnvironment(HttpContext context)
    {
        foreach (var name in new[]
                 {
                     "PROJECTPULSE_ENVIRONMENT",
                     "PROJECTPULSE_SSO_MODE",
                     "PROJECTPULSE_ENTRA_MODE"
                 })
        {
            var normalized = NormalizeEnvironment(Environment.GetEnvironmentVariable(name));
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        }

        foreach (var name in new[]
                 {
                     "PUBLIC_URL",
                     "PROJECTPULSE_PUBLIC_URL",
                     "PROJECTPULSE_WEB_URL",
                     "PROJECTPULSE_PUBLIC_BASE_URL"
                 })
        {
            if (Uri.TryCreate(Environment.GetEnvironmentVariable(name), UriKind.Absolute, out var configured))
            {
                var configuredEnvironment = EnvironmentFromApprovedHost(configured.Host);
                if (!string.IsNullOrWhiteSpace(configuredEnvironment)) return configuredEnvironment;
            }
        }

        return EnvironmentFromApprovedHost(context.Request.Host.Host);
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

    private static string EnvironmentFromApprovedHost(string? value)
    {
        var host = (value ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (host.EndsWith(".onenecklab.com", StringComparison.Ordinal)) return "test";
        if (host.EndsWith(".ussignal.com", StringComparison.Ordinal)
            && !host.Contains("-test.", StringComparison.Ordinal)
            && !host.Contains(".test.", StringComparison.Ordinal)
            && !host.StartsWith("test-", StringComparison.Ordinal))
            return "production";
        return string.Empty;
    }

    private static bool IsInternalAzureHost(string host) =>
        host.EndsWith(".azurecontainerapps.io", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
        || host.Contains("azurecontainerapps", StringComparison.OrdinalIgnoreCase);

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
            || string.IsNullOrWhiteSpace(password))
            return string.Empty;

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

    internal sealed record StateOriginResult(
        bool Recovered,
        Uri? PublicOrigin,
        string? RedirectUri,
        string Source,
        string FailureCode)
    {
        internal static StateOriginResult Success(Uri publicOrigin, string redirectUri, string source) =>
            new(true, publicOrigin, redirectUri, source, string.Empty);

        internal static StateOriginResult Fail(string failureCode) =>
            new(false, null, null, string.Empty, failureCode);
    }
}
