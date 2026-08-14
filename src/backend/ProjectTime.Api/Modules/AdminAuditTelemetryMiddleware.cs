using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// System-wide, best-effort operational audit telemetry. The middleware records
/// every authentication result, every state-changing API request, and every
/// failed/degraded API response without retaining passwords, bearer tokens,
/// request bodies, query strings, private AI prompts, or document contents.
/// </summary>
internal static class AdminAuditTelemetryMiddleware
{
    private const int MaximumLoginBodyBytes = 16_384;

    internal static WebApplication UseAdminAuditTelemetry(this WebApplication app)
    {
        app.Use(InvokeAsync);
        return app;
    }

    private static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var started = Stopwatch.GetTimestamp();
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method.ToUpperInvariant();
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers["X-ProjectPulse-Correlation-Id"] = correlationId;

        string loginIdentifier = string.Empty;
        if (IsLoginSubmission(path, method))
        {
            loginIdentifier = await ReadLoginIdentifierAsync(context);
        }

        Exception? unhandled = null;
        try
        {
            await next();
        }
        catch (Exception exception)
        {
            unhandled = exception;
            throw;
        }
        finally
        {
            var statusCode = unhandled is null
                ? context.Response.StatusCode
                : StatusCodes.Status500InternalServerError;
            var durationMs = Math.Max(0, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (ShouldRecord(path, method, statusCode, unhandled))
            {
                try
                {
                    await RecordAsync(
                        context,
                        path,
                        method,
                        loginIdentifier,
                        statusCode,
                        durationMs,
                        correlationId,
                        unhandled);
                }
                catch
                {
                    // Audit persistence is intentionally fail-open for the user
                    // request. Audit readiness itself is exposed in Module 008.
                }
            }
        }
    }

    private static bool ShouldRecord(
        string path,
        string method,
        int statusCode,
        Exception? unhandled)
    {
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsAuthenticationPath(path)) return true;
        if (unhandled is not null || statusCode >= 400) return true;
        return method is "POST" or "PUT" or "PATCH" or "DELETE";
    }

    private static async Task RecordAsync(
        HttpContext context,
        string path,
        string method,
        string loginIdentifier,
        int statusCode,
        double durationMs,
        string correlationId,
        Exception? unhandled)
    {
        var connectionString = AdminExperienceCommon.ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);

        var actor = await ResolveActorAsync(connection, context, loginIdentifier);
        var isAuth = IsAuthenticationPath(path);
        var category = isAuth
            ? "authentication"
            : statusCode >= 500
                ? "system"
                : statusCode >= 400
                    ? "security"
                    : "change";
        var status = unhandled is not null || statusCode >= 400
            ? "failure"
            : "success";
        var eventType = ResolveEventType(path, method, statusCode, unhandled);
        var module = ResolveModule(path);
        var environment = Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "unknown";
        var sourceRevision = Environment.GetEnvironmentVariable("PROJECTPULSE_SOURCE_REVISION")
            ?? Environment.GetEnvironmentVariable("ProjectPulseSourceRevision")
            ?? "unknown";
        var summary = BuildSummary(path, method, statusCode, unhandled);
        var details = new
        {
            method,
            path = SanitizedPath(path),
            statusCode,
            durationMs = Math.Round(durationMs, 2),
            category,
            eventType,
            environment,
            sourceRevision,
            isViewAs = AdminExperienceCommon.IsViewAs(context),
            effectiveUserId = ReadGuid(context, "ProjectPulseEffectiveUserId"),
            sessionProvider = ReadString(context, "ProjectPulseSessionProvider"),
            responseContentType = context.Response.ContentType ?? string.Empty,
            exceptionType = unhandled?.GetType().Name ?? string.Empty,
            exceptionCode = unhandled is null ? string.Empty : "unhandled_api_exception",
            redaction = "No request body, password, token, query string, private AI prompt, document content, or response body retained."
        };

        await AdminExperienceCommon.WriteAuditAsync(
            connection,
            null,
            category,
            status,
            eventType,
            actor.UserId,
            actor.Email,
            isAuth ? "authentication" : "api_route",
            SanitizedPath(path),
            isAuth ? actor.Username : SanitizedPath(path),
            module,
            "http_pipeline",
            string.Empty,
            summary,
            details,
            AdminExperienceCommon.ClientIp(context),
            correlationId,
            CancellationToken.None);

        if (isAuth && await AdminExperienceCommon.TableExistsAsync(
                connection,
                "auth_login_events",
                cancellationToken: CancellationToken.None))
        {
            await using var auth = new NpgsqlCommand("""
                INSERT INTO auth_login_events(
                    user_id,username,login_method,login_result,source_ip,user_agent,event_details,created_at)
                VALUES(
                    @user_id,@username,@method,@result,@ip,@agent,@details::jsonb,NOW());
                """, connection);
            auth.Parameters.AddWithValue("user_id", actor.UserId.HasValue ? actor.UserId.Value : DBNull.Value);
            auth.Parameters.AddWithValue("username", actor.Username);
            auth.Parameters.AddWithValue("method", ResolveLoginMethod(path));
            auth.Parameters.AddWithValue("result", ResolveLoginResult(path, statusCode, unhandled));
            auth.Parameters.AddWithValue("ip", AdminExperienceCommon.ClientIp(context));
            auth.Parameters.AddWithValue("agent", Truncate(context.Request.Headers.UserAgent.ToString(), 1000));
            auth.Parameters.AddWithValue("details", JsonSerializer.Serialize(new
            {
                path = SanitizedPath(path),
                statusCode,
                durationMs = Math.Round(durationMs, 2),
                correlationId,
                environment,
                sourceRevision,
                redaction = "Credentials and session tokens were not retained."
            }));
            await auth.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task<AuditActor> ResolveActorAsync(
        NpgsqlConnection connection,
        HttpContext context,
        string loginIdentifier)
    {
        var userId = AdminExperienceCommon.ActualUserId(context);
        var email = AdminExperienceCommon.ActualEmail(context);
        var username = loginIdentifier.Trim();

        if (!userId.HasValue)
        {
            var token = ReadSessionToken(context.Request);
            if (!string.IsNullOrWhiteSpace(token))
            {
                await using var session = new NpgsqlCommand("""
                    SELECT s.user_id,COALESCE(u.email,''),COALESCE(a.username,u.email,'')
                    FROM auth_sessions s
                    LEFT JOIN app_users u ON u.user_id=s.user_id
                    LEFT JOIN auth_local_accounts a ON a.user_id=s.user_id
                    WHERE s.session_token_hash=@hash
                    ORDER BY s.created_at DESC
                    LIMIT 1;
                    """, connection);
                session.Parameters.AddWithValue("hash", Sha256(token));
                await using var reader = await session.ExecuteReaderAsync(CancellationToken.None);
                if (await reader.ReadAsync(CancellationToken.None))
                {
                    userId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
                    email = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    if (username.Length == 0) username = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                }
            }
        }

        if (!userId.HasValue && username.Length > 0)
        {
            await using var user = new NpgsqlCommand("""
                SELECT u.user_id,COALESCE(u.email,''),COALESCE(a.username,u.email,'')
                FROM app_users u
                LEFT JOIN auth_local_accounts a ON a.user_id=u.user_id
                WHERE LOWER(COALESCE(a.username,u.email,''))=LOWER(@username)
                   OR LOWER(COALESCE(u.email,''))=LOWER(@username)
                LIMIT 1;
                """, connection);
            user.Parameters.AddWithValue("username", username);
            await using var reader = await user.ExecuteReaderAsync(CancellationToken.None);
            if (await reader.ReadAsync(CancellationToken.None))
            {
                userId = reader.GetGuid(0);
                email = reader.GetString(1);
                username = reader.GetString(2);
            }
        }

        if (username.Length == 0) username = email == "unknown" ? string.Empty : email;
        return new AuditActor(userId, email == "unknown" ? string.Empty : email, username);
    }

    private static async Task<string> ReadLoginIdentifierAsync(HttpContext context)
    {
        if (context.Request.ContentLength is > MaximumLoginBodyBytes) return string.Empty;
        if (!(context.Request.ContentType ?? string.Empty).Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        context.Request.EnableBuffering(bufferThreshold: MaximumLoginBodyBytes, bufferLimit: MaximumLoginBodyBytes);
        try
        {
            using var document = await JsonDocument.ParseAsync(
                context.Request.Body,
                new JsonDocumentOptions { AllowTrailingCommas = false, MaxDepth = 8 },
                context.RequestAborted);
            foreach (var key in new[] { "username", "email", "loginHint" })
            {
                if (document.RootElement.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return Truncate(value.GetString() ?? string.Empty, 255);
                }
            }
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            if (context.Request.Body.CanSeek) context.Request.Body.Position = 0;
        }
        return string.Empty;
    }

    private static string ResolveEventType(
        string path,
        string method,
        int statusCode,
        Exception? unhandled)
    {
        if (IsAuthenticationPath(path))
        {
            if (path.Contains("/local/login", StringComparison.OrdinalIgnoreCase)
                || path.Contains("/sso/callback", StringComparison.OrdinalIgnoreCase))
                return statusCode is >= 200 and < 400 && unhandled is null ? "login_succeeded" : "login_failed";
            if (path.Contains("/logout", StringComparison.OrdinalIgnoreCase))
                return statusCode is >= 200 and < 400 && unhandled is null ? "logout_succeeded" : "logout_failed";
            if (path.Contains("/extend", StringComparison.OrdinalIgnoreCase))
                return statusCode is >= 200 and < 400 && unhandled is null ? "session_extended" : "session_extension_failed";
            if (path.Contains("password-reset", StringComparison.OrdinalIgnoreCase))
                return statusCode is >= 200 and < 400 && unhandled is null ? "password_reset_activity" : "password_reset_failed";
            return statusCode is >= 200 and < 400 && unhandled is null ? "authentication_activity" : "authentication_failed";
        }
        if (unhandled is not null) return "api_request_exception";
        if (statusCode == StatusCodes.Status503ServiceUnavailable) return "dependency_unavailable";
        if (statusCode >= 500) return "api_request_failed";
        if (statusCode >= 400) return "api_request_denied";
        return method switch
        {
            "POST" => "record_created_or_action_started",
            "PUT" or "PATCH" => "record_updated",
            "DELETE" => "record_deleted_or_revoked",
            _ => "api_activity"
        };
    }

    private static string ResolveLoginResult(string path, int statusCode, Exception? unhandled)
    {
        if (unhandled is not null || statusCode >= 500) return "error";
        if (path.Contains("/logout", StringComparison.OrdinalIgnoreCase))
            return statusCode < 400 ? "logout_success" : "logout_failed";
        if (path.Contains("/extend", StringComparison.OrdinalIgnoreCase))
            return statusCode < 400 ? "session_extended" : "session_extension_failed";
        if (statusCode is >= 200 and < 400) return "success";
        if (statusCode == StatusCodes.Status423Locked) return "account_locked";
        if (statusCode == StatusCodes.Status403Forbidden) return "access_denied";
        if (statusCode == StatusCodes.Status429TooManyRequests) return "rate_limited";
        if (statusCode == StatusCodes.Status401Unauthorized) return "invalid_credentials_or_session";
        return "failed";
    }

    private static string ResolveLoginMethod(string path)
    {
        if (path.Contains("/sso/", StringComparison.OrdinalIgnoreCase)) return "entra_id";
        if (path.Contains("password-reset", StringComparison.OrdinalIgnoreCase)) return "password_reset";
        if (path.Contains("/session/", StringComparison.OrdinalIgnoreCase)) return "session";
        return "local";
    }

    private static string ResolveModule(string path)
    {
        var mappings = new (string Prefix, string Module)[]
        {
            ("/api/admin/audit-history", "008"),
            ("/api/auth", "009"),
            ("/api/projects/cost-alerts", "022"),
            ("/api/sow-gsd-planning", "025"),
            ("/api/project-management", "033"),
            ("/api/project-intake", "055D"),
            ("/api/billing", "039"),
            ("/api/project-flowhive", "066"),
            ("/api/celar-ai", "011")
        };
        return mappings.FirstOrDefault(mapping => path.StartsWith(mapping.Prefix, StringComparison.OrdinalIgnoreCase)).Module
            ?? "platform";
    }

    private static string BuildSummary(string path, string method, int statusCode, Exception? unhandled)
    {
        var route = SanitizedPath(path);
        if (unhandled is not null) return $"{method} {route} ended with an unhandled server error.";
        if (statusCode == StatusCodes.Status503ServiceUnavailable) return $"{method} {route} reported a temporarily unavailable dependency.";
        if (statusCode >= 500) return $"{method} {route} failed with HTTP {statusCode}.";
        if (statusCode >= 400) return $"{method} {route} was denied or rejected with HTTP {statusCode}.";
        return $"{method} {route} completed with HTTP {statusCode}.";
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? context.Request.Headers["X-Request-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(supplied)) return Truncate(supplied.Trim(), 120);
        return Guid.NewGuid().ToString("N");
    }

    private static string SanitizedPath(string path) => Truncate(path.Split('?', 2)[0], 500);

    private static string? ReadString(HttpContext context, string key) =>
        context.Items.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static Guid? ReadGuid(HttpContext context, string key) =>
        context.Items.TryGetValue(key, out var value) && Guid.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : null;

    private static string ReadSessionToken(HttpRequest request)
    {
        var direct = request.Headers["X-ProjectPulse-Session"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(direct)) return direct.Trim();
        var authorization = request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authorization)
            && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization[7..].Trim();
        }
        return string.Empty;
    }

    private static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum];

    private static bool IsAuthenticationPath(string path) =>
        path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase);

    private static bool IsLoginSubmission(string path, string method) =>
        method == "POST"
        && (path.Equals("/api/auth/local/login", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/sso/dev-login", StringComparison.OrdinalIgnoreCase));

    private sealed record AuditActor(Guid? UserId, string Email, string Username);
}
