using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Central fail-closed security boundary for legacy and cross-module routes that predate
/// endpoint-local authorization. Endpoint handlers should still perform their own checks;
/// this middleware provides a second, consistent authorization and input-safety boundary.
/// </summary>
public static class SecurityHardeningModule
{
    private const string SsoStateCookie = "ProjectPulseSsoState";
    private const string DefaultBreakGlassAccount = "ahmed.adeyemi@ussignal.local";
    private const long MaximumInspectedJsonBytes = 10 * 1024 * 1024;

    private static readonly Regex WorkRegisterDetailsPath = new(
        @"^/api/work-register/projects/(?<id>[0-9a-fA-F-]{36})/details$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WorkRegisterDocumentDownloadPath = new(
        @"^/api/work-register/projects/documents/(?<id>[0-9a-fA-F-]{36})/download$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IntakeDocumentDownloadPath = new(
        @"^/api/project-intake/documents/(?<id>[0-9a-fA-F-]{36})/download$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IntakeRequestMutationPath = new(
        @"^/api/project-intake/(?:requests/)?(?<id>[0-9a-fA-F-]{36})/(?:documents|supporting-documents/upload|post-intake|project-link)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SafeDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sow",
        "gsd",
        "quote",
        "proposal",
        "order_form",
        "architecture",
        "supporting_document",
        "other"
    };

    public static WebApplication UseProjectPulseSecurityHardening(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            await InvokeAsync(context, next);
        });

        return app;
    }

    private static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        RegisterSecurityHeaders(context);

        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method.ToUpperInvariant();

        if (await TryHandleGenericLocalLoginRouteAsync(context, path, method))
        {
            return;
        }

        if (path.Equals("/api/auth/local/login", StringComparison.OrdinalIgnoreCase)
            && method == HttpMethods.Post)
        {
            await InvokeWithGenericLocalLoginFailureAsync(context, next);
            return;
        }

        if (path.Equals("/api/auth/password-reset/request", StringComparison.OrdinalIgnoreCase)
            && method == HttpMethods.Post)
        {
            await InvokeWithGenericPasswordResetResponseAsync(context, next);
            return;
        }

        if (path.Equals("/api/auth/sso/start", StringComparison.OrdinalIgnoreCase)
            && method == HttpMethods.Get)
        {
            RegisterSsoStateCookieOnRedirect(context);
            await next();
            return;
        }

        if (path.Equals("/api/auth/sso/callback", StringComparison.OrdinalIgnoreCase)
            && method == HttpMethods.Get)
        {
            if (!ValidateAndConsumeSsoStateCookie(context))
            {
                context.Response.Redirect("/#login?ssoError=invalid_browser_state");
                return;
            }

            await next();
            return;
        }

        if (IsUnsafeMethod(method)
            && context.Items.TryGetValue("ProjectPulseIsViewAs", out var viewAsValue)
            && viewAsValue is true)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "view_as_read_only",
                "Write actions are disabled while using Administrator View-As preview. Exit preview to make changes.");
            return;
        }

        if (context.Request.HasFormContentType
            && IsDocumentUploadPath(path)
            && !await ValidateDocumentUploadFormAsync(context))
        {
            return;
        }

        var sensitivePayloadRequired =
            IsRoleMutationPath(path, method)
            || IsBreakGlassPasswordPath(path, method);

        JsonDocument? inspectedPayload = null;

        if (sensitivePayloadRequired && !IsJsonRequest(context.Request))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "json_body_required",
                "This security-sensitive action requires an application/json request body.");
            return;
        }

        if (IsUnsafeMethod(method) && IsJsonRequest(context.Request))
        {
            if (context.Request.ContentLength is long contentLength
                && contentLength > MaximumInspectedJsonBytes)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    "request_body_too_large",
                    "The request body exceeds the security inspection limit.");
                return;
            }

            var inspection = await InspectJsonBodyAsync(context);

            if (inspection.TooLarge)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    "request_body_too_large",
                    "The request body exceeds the security inspection limit.");
                return;
            }

            if (inspection.Malformed)
            {
                if (sensitivePayloadRequired)
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status400BadRequest,
                        "invalid_json_body",
                        "A valid JSON request body is required for this security-sensitive action.");
                    return;
                }
            }
            else
            {
                inspectedPayload = inspection.Document;

                if (inspectedPayload is not null
                    && !await ValidateJsonSafetyAsync(context, path, inspectedPayload.RootElement))
                {
                    inspectedPayload.Dispose();
                    return;
                }
            }
        }

        if (sensitivePayloadRequired && inspectedPayload is null)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "security_payload_unavailable",
                "The request body could not be inspected and the action was denied.");
            return;
        }

        NpgsqlConnection? authorizationConnection = null;

        try
        {
            var actorUserId = ResolveActualUserId(context);
            var policy = RequiredPolicy(path, method);

            AccessContext? access = null;

            if (policy != SecurityPolicy.None
                || WorkRegisterDetailsPath.IsMatch(path)
                || WorkRegisterDocumentDownloadPath.IsMatch(path)
                || IntakeDocumentDownloadPath.IsMatch(path)
                || IsRoleMutationPath(path, method)
                || IsBreakGlassPasswordPath(path, method))
            {
                if (actorUserId is null)
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status401Unauthorized,
                        "session_required",
                        "A valid Project Pulse session is required.");
                    return;
                }

                authorizationConnection = await OpenConnectionAsync();
                access = await LoadAccessContextAsync(authorizationConnection, actorUserId.Value);

                if (!PolicyAllows(policy, access))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status403Forbidden,
                        "access_denied",
                        PolicyDeniedMessage(policy));
                    return;
                }

                if (IsRoleMutationPath(path, method)
                    && inspectedPayload is not null
                    && !await ValidateRoleMutationAsync(
                        context,
                        authorizationConnection,
                        actorUserId.Value,
                        access,
                        inspectedPayload.RootElement))
                {
                    return;
                }

                if (IsBreakGlassPasswordPath(path, method)
                    && inspectedPayload is not null
                    && !await ValidateBreakGlassPasswordMutationAsync(
                        context,
                        authorizationConnection,
                        inspectedPayload.RootElement))
                {
                    return;
                }

                if (!await ValidateObjectScopeAsync(context, authorizationConnection, access, actorUserId.Value, path))
                {
                    return;
                }
            }

            await next();
        }
        catch (InvalidOperationException ex)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "security_configuration_unavailable",
                "Security authorization could not be evaluated. The action was denied.");
        }
        finally
        {
            if (authorizationConnection is not null)
            {
                await authorizationConnection.DisposeAsync();
            }

            inspectedPayload?.Dispose();
        }
    }

    private static void RegisterSecurityHeaders(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
            context.Response.Headers.TryAdd("Referrer-Policy", "same-origin");
            context.Response.Headers.TryAdd(
                "Content-Security-Policy",
                "object-src 'none'; base-uri 'self'; frame-ancestors 'self'");
            context.Response.Headers.TryAdd(
                "Permissions-Policy",
                "camera=(), microphone=(), geolocation=()");
            return Task.CompletedTask;
        });
    }

    private static bool IsUnsafeMethod(string method)
    {
        return !string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(method, HttpMethods.Head, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(method, HttpMethods.Options, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJsonRequest(HttpRequest request)
    {
        return request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static Guid? ResolveActualUserId(HttpContext context)
    {
        foreach (var key in new[]
                 {
                     "ProjectPulseActualUserId",
                     "ProjectPulseSessionUserId"
                 })
        {
            if (!context.Items.TryGetValue(key, out var raw) || raw is null)
            {
                continue;
            }

            if (raw is Guid guid)
            {
                return guid;
            }

            if (Guid.TryParse(raw.ToString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static async Task<bool> TryHandleGenericLocalLoginRouteAsync(
        HttpContext context,
        string path,
        string method)
    {
        if (!path.Equals("/api/auth/login/route", StringComparison.OrdinalIgnoreCase)
            || method != HttpMethods.Get)
        {
            return false;
        }

        var username = context.Request.Query["username"].FirstOrDefault()?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!IsLocalUsername(username))
        {
            return false;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "route_resolved",
            username,
            loginMethod = "local",
            provider = "LOCAL",
            displayName = "Project Pulse local administrator login",
            requiresPassword = true,
            message = "Local administrator credentials are required."
        });

        return true;
    }

    private static async Task InvokeWithGenericLocalLoginFailureAsync(
        HttpContext context,
        Func<Task> next)
    {
        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next();

            if (context.Response.StatusCode >= 400)
            {
                context.Response.Body = originalBody;
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    status = "invalid_local_credentials",
                    message = "Invalid local administrator credentials."
                });
                return;
            }

            buffer.Position = 0;
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static async Task InvokeWithGenericPasswordResetResponseAsync(
        HttpContext context,
        Func<Task> next)
    {
        var username = await TryReadJsonStringAsync(context, "username");

        if (!IsLocalUsername(username))
        {
            await next();
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next();

            if (context.Response.StatusCode >= 500)
            {
                buffer.Position = 0;
                context.Response.Body = originalBody;
                await buffer.CopyToAsync(originalBody);
                return;
            }

            context.Response.Body = originalBody;
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsJsonAsync(new
            {
                status = "password_reset_request_received",
                message = "If the local account is eligible, a password-reset approval request has been queued."
            });
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsLocalUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var normalized = username.Trim();
        return normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
               || normalized.EndsWith("@ussignal.local", StringComparison.OrdinalIgnoreCase);
    }

    private static void RegisterSsoStateCookieOnRedirect(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var location = context.Response.Headers.Location.ToString();

            if (!Uri.TryCreate(location, UriKind.Absolute, out var redirectUri))
            {
                return Task.CompletedTask;
            }

            var query = QueryHelpers.ParseQuery(redirectUri.Query);
            var state = query.TryGetValue("state", out var stateValues)
                ? stateValues.FirstOrDefault()
                : null;

            if (string.IsNullOrWhiteSpace(state))
            {
                return Task.CompletedTask;
            }

            context.Response.Cookies.Append(
                SsoStateCookie,
                state,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    MaxAge = TimeSpan.FromMinutes(10),
                    Path = "/api/auth/sso/callback"
                });

            return Task.CompletedTask;
        });
    }

    private static bool ValidateAndConsumeSsoStateCookie(HttpContext context)
    {
        var state = context.Request.Query["state"].FirstOrDefault();
        var cookieState = context.Request.Cookies[SsoStateCookie];

        context.Response.Cookies.Delete(
            SsoStateCookie,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/api/auth/sso/callback"
            });

        if (string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(cookieState))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(cookieState);
        var actual = Encoding.UTF8.GetBytes(state);

        return expected.Length == actual.Length
               && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static async Task<JsonBodyInspection> InspectJsonBodyAsync(HttpContext context)
    {
        try
        {
            context.Request.EnableBuffering(
                bufferThreshold: 64 * 1024,
                bufferLimit: MaximumInspectedJsonBytes);
            context.Request.Body.Position = 0;
            var document = await JsonDocument.ParseAsync(context.Request.Body);
            context.Request.Body.Position = 0;
            return new JsonBodyInspection(document, TooLarge: false, Malformed: false);
        }
        catch (IOException)
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            return new JsonBodyInspection(null, TooLarge: true, Malformed: false);
        }
        catch (JsonException)
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            return new JsonBodyInspection(null, TooLarge: false, Malformed: true);
        }
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        value = default;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static async Task<string> TryReadJsonStringAsync(HttpContext context, string propertyName)
    {
        var inspection = await InspectJsonBodyAsync(context);
        var document = inspection.Document;

        if (document is null)
        {
            return string.Empty;
        }

        using (document)
        {
            return TryGetPropertyIgnoreCase(document.RootElement, propertyName, out var property)
                   && property.ValueKind == JsonValueKind.String
                ? property.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }
    }

    private static async Task<bool> ValidateJsonSafetyAsync(
        HttpContext context,
        string path,
        JsonElement root)
    {
        foreach (var property in EnumerateProperties(root))
        {
            var name = property.Name;
            var value = property.Value;

            if (value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = value.GetString() ?? string.Empty;

            if (name.Equals("subject", StringComparison.OrdinalIgnoreCase)
                || name.Equals("from", StringComparison.OrdinalIgnoreCase)
                || name.Equals("to", StringComparison.OrdinalIgnoreCase)
                || name.Equals("cc", StringComparison.OrdinalIgnoreCase)
                || name.Equals("bcc", StringComparison.OrdinalIgnoreCase))
            {
                if (text.Contains('\r') || text.Contains('\n'))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status400BadRequest,
                        "invalid_email_header",
                        "Email header values cannot contain carriage returns or line feeds.");
                    return false;
                }
            }

            if (name.Equals("actionUrl", StringComparison.OrdinalIgnoreCase)
                || name.Equals("action_url", StringComparison.OrdinalIgnoreCase)
                || (name.Equals("documentReference", StringComparison.OrdinalIgnoreCase)
                    && path.StartsWith("/api/work-register/", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(text) && !IsSafeActionUrl(text))
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status400BadRequest,
                        "unsafe_url_rejected",
                        "Only same-origin relative routes or explicit HTTPS destinations are allowed.");
                    return false;
                }
            }
        }

        return true;
    }

    private static IEnumerable<JsonProperty> EnumerateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property;

                foreach (var nested in EnumerateProperties(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateProperties(item))
                {
                    yield return nested;
                }
            }
        }
    }

    internal static bool IsSafeActionUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var candidate = value.Trim();

        if (candidate.StartsWith("/", StringComparison.Ordinal)
            && !candidate.StartsWith("//", StringComparison.Ordinal))
        {
            return !candidate.Contains('\\')
                   && !candidate.Any(char.IsControl);
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
               && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static bool IsDocumentUploadPath(string path)
    {
        return path.Contains("/project-intake/", StringComparison.OrdinalIgnoreCase)
               && path.Contains("document", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ValidateDocumentUploadFormAsync(HttpContext context)
    {
        IFormCollection form;

        try
        {
            form = await context.Request.ReadFormAsync();
        }
        catch (InvalidDataException)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "invalid_multipart_form",
                "The uploaded multipart form is invalid.");
            return false;
        }

        var documentType = form["documentType"].FirstOrDefault()?.Trim() ?? "other";

        if (!SafeDocumentTypes.Contains(documentType)
            || !Regex.IsMatch(documentType, @"^[a-z0-9_]{1,40}$", RegexOptions.CultureInvariant))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status400BadRequest,
                "invalid_document_type",
                "The document type is not supported.");
            return false;
        }

        foreach (var file in form.Files)
        {
            if (!string.Equals(file.FileName, Path.GetFileName(file.FileName), StringComparison.Ordinal)
                || file.FileName.Contains('/')
                || file.FileName.Contains('\\')
                || file.FileName.Contains('\0'))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "invalid_file_name",
                    "The uploaded file name is invalid.");
                return false;
            }
        }

        return true;
    }

    private static SecurityPolicy RequiredPolicy(string path, string method)
    {
        if (path.Equals("/api/admin/users", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/admin/users/roles", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/auth/local-accounts", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("/api/auth/password-reset/", StringComparison.OrdinalIgnoreCase)
                && !path.Equals("/api/auth/password-reset/request", StringComparison.OrdinalIgnoreCase))
            || (path.StartsWith("/api/admin/user-admin/", StringComparison.OrdinalIgnoreCase)
                && IsUnsafeMethod(method))
            || IsDiagnosticPath(path))
        {
            return SecurityPolicy.Administrator;
        }

        if (path.StartsWith("/api/reports/030/", StringComparison.OrdinalIgnoreCase))
        {
            return SecurityPolicy.Reporting;
        }

        if (path.StartsWith("/api/time-compliance/", StringComparison.OrdinalIgnoreCase))
        {
            return SecurityPolicy.TimeCompliance;
        }

        if (path.Equals("/api/holidays/import-text", StringComparison.OrdinalIgnoreCase))
        {
            return SecurityPolicy.HolidayAdministration;
        }

        if (path.StartsWith("/api/project-intake/", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Contains("/assign", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/project-intake/resource-assignment-promotions", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/project-link", StringComparison.OrdinalIgnoreCase))
            {
                return SecurityPolicy.ProjectAssignment;
            }

            if (path.Equals("/api/project-intake/overview", StringComparison.OrdinalIgnoreCase)
                || IsUnsafeMethod(method))
            {
                return SecurityPolicy.ProjectIntake;
            }
        }

        return SecurityPolicy.None;
    }

    private static bool IsDiagnosticPath(string path)
    {
        return path.Equals("/api/production-data-readiness", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/production/data-readiness", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/db-config-check", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/db-health", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/schema/tables", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PolicyAllows(SecurityPolicy policy, AccessContext access)
    {
        return policy switch
        {
            SecurityPolicy.None => true,
            SecurityPolicy.Administrator => access.IsAdministrator,
            SecurityPolicy.Reporting => access.CanViewReporting,
            SecurityPolicy.TimeCompliance => access.CanViewTimeCompliance,
            SecurityPolicy.HolidayAdministration => access.CanManageHolidays,
            SecurityPolicy.ProjectIntake => access.CanUseProjectIntake,
            SecurityPolicy.ProjectAssignment => access.CanManageProjectAssignments,
            _ => false
        };
    }

    private static string PolicyDeniedMessage(SecurityPolicy policy)
    {
        return policy switch
        {
            SecurityPolicy.Administrator => "This action is restricted to Administrators and Super Administrators.",
            SecurityPolicy.Reporting => "Reporting access is not granted to the current user.",
            SecurityPolicy.TimeCompliance => "Time-compliance access is not granted to the current user.",
            SecurityPolicy.HolidayAdministration => "Holiday imports are restricted to authorized administrators and coordinators.",
            SecurityPolicy.ProjectIntake => "Project Intake access is not granted to the current user.",
            SecurityPolicy.ProjectAssignment => "Resource assignment is restricted to authorized project coordinators and administrators.",
            _ => "Access is denied."
        };
    }

    private static bool IsRoleMutationPath(string path, string method)
    {
        if (!IsUnsafeMethod(method))
        {
            return false;
        }

        return path.Equals("/api/admin/users/roles", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/admin/user-admin/users/roles", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/admin/user-admin/users/bulk-update", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/admin/user-admin/users/local", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBreakGlassPasswordPath(string path, string method)
    {
        return method == HttpMethods.Post
               && path.Equals("/api/admin/user-admin/local-password", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ValidateRoleMutationAsync(
        HttpContext context,
        NpgsqlConnection connection,
        Guid actorUserId,
        AccessContext access,
        JsonElement payload)
    {
        var roleCodes = ReadRoleCodes(payload);

        if (roleCodes.Count > 0)
        {
            var knownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using (var command = new NpgsqlCommand("""
                SELECT role_code
                FROM app_roles
                WHERE is_active = TRUE
                  AND role_code = ANY(@role_codes);
                """, connection))
            {
                command.Parameters.AddWithValue("role_codes", roleCodes.ToArray());

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    knownCodes.Add(reader.GetString(0));
                }
            }

            var unknownCodes = roleCodes.Where(code => !knownCodes.Contains(code)).ToArray();

            if (unknownCodes.Length > 0)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "unknown_role_code",
                    $"Unknown or inactive role code(s): {string.Join(", ", unknownCodes)}.");
                return false;
            }
        }

        var targetUserIds = ReadTargetUserIds(payload);
        var targetEmail = ReadString(payload, "email");

        if (!access.IsSuperAdministrator
            && await TargetsExistingSuperAdministratorAsync(
                connection,
                targetUserIds,
                targetEmail))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "super_administrator_target_protected",
                "Only a Super Administrator can change roles for an existing Super Administrator.");
            return false;
        }

        if (roleCodes.Contains("SUPER_ADMINISTRATOR") && !access.IsSuperAdministrator)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "super_administrator_assignment_forbidden",
                "Only a Super Administrator can grant or preserve the Super Administrator role.");
            return false;
        }

        var targetUserId = ReadGuid(payload, "userId");
        if (targetUserId == actorUserId
            && roleCodes.Contains("SUPER_ADMINISTRATOR")
            && !access.IsSuperAdministrator)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "self_elevation_forbidden",
                "Self-elevation to Super Administrator is forbidden.");
            return false;
        }

        return true;
    }

    private static async Task<bool> TargetsExistingSuperAdministratorAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<Guid> targetUserIds,
        string targetEmail)
    {
        if (targetUserIds.Count == 0 && string.IsNullOrWhiteSpace(targetEmail))
        {
            return false;
        }

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_users u
                JOIN app_user_role_assignments ura
                  ON ura.user_id = u.user_id
                 AND ura.is_active = TRUE
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                WHERE r.role_code = 'SUPER_ADMINISTRATOR'
                  AND (
                        (cardinality(@target_user_ids) > 0 AND u.user_id = ANY(@target_user_ids))
                     OR (NULLIF(@target_email, '') IS NOT NULL AND lower(u.email) = lower(@target_email))
                  )
            );
            """, connection);

        command.Parameters.AddWithValue(
            "target_user_ids",
            NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid,
            targetUserIds.ToArray());
        command.Parameters.AddWithValue("target_email", targetEmail ?? string.Empty);

        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static HashSet<string> ReadRoleCodes(JsonElement payload)
    {
        var roleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetPropertyIgnoreCase(payload, "roleCodes", out var roleElement)
            || roleElement.ValueKind != JsonValueKind.Array)
        {
            return roleCodes;
        }

        foreach (var item in roleElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var code = item.GetString()?.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(code))
            {
                roleCodes.Add(code);
            }
        }

        return roleCodes;
    }

    private static HashSet<Guid> ReadTargetUserIds(JsonElement payload)
    {
        var userIds = new HashSet<Guid>();
        var singleUserId = ReadGuid(payload, "userId");

        if (singleUserId is not null)
        {
            userIds.Add(singleUserId.Value);
        }

        if (TryGetPropertyIgnoreCase(payload, "userIds", out var userIdsElement)
            && userIdsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in userIdsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String
                    && Guid.TryParse(item.GetString(), out var parsed))
                {
                    userIds.Add(parsed);
                }
            }
        }

        return userIds;
    }

    private static Guid? ReadGuid(JsonElement payload, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(payload, propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ReadString(JsonElement payload, string propertyName)
    {
        return TryGetPropertyIgnoreCase(payload, propertyName, out var element)
               && element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static async Task<bool> ValidateBreakGlassPasswordMutationAsync(
        HttpContext context,
        NpgsqlConnection connection,
        JsonElement payload)
    {
        var targetUserId = ReadGuid(payload, "userId");

        if (targetUserId is null)
        {
            return true;
        }

        await using var command = new NpgsqlCommand("""
            SELECT email
            FROM app_users
            WHERE user_id = @user_id
            LIMIT 1;
            """, connection);

        command.Parameters.AddWithValue("user_id", targetUserId.Value);
        var email = (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty;
        var breakGlass = Environment.GetEnvironmentVariable("PROJECTPULSE_BREAK_GLASS_ACCOUNT")
                         ?? DefaultBreakGlassAccount;

        if (!email.Equals(breakGlass, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await WriteErrorAsync(
            context,
            StatusCodes.Status403Forbidden,
            "break_glass_password_protected",
            "The break-glass account password can only be changed through the approved offline recovery procedure.");
        return false;
    }

    private static async Task<bool> ValidateObjectScopeAsync(
        HttpContext context,
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        string path)
    {
        var detailsMatch = WorkRegisterDetailsPath.Match(path);

        if (detailsMatch.Success
            && Guid.TryParse(detailsMatch.Groups["id"].Value, out var projectId)
            && !await CanAccessProjectAsync(connection, access, actorUserId, projectId))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "project_access_denied",
                "You do not have access to this project.");
            return false;
        }

        var documentMatch = WorkRegisterDocumentDownloadPath.Match(path);

        if (documentMatch.Success
            && Guid.TryParse(documentMatch.Groups["id"].Value, out var documentId)
            && !await CanAccessWorkRegisterDocumentAsync(
                connection,
                access,
                actorUserId,
                documentId))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "document_access_denied",
                "The requested project document was not found or is outside your document visibility scope.");
            return false;
        }

        var intakeMutationMatch = IntakeRequestMutationPath.Match(path);

        if (intakeMutationMatch.Success
            && Guid.TryParse(intakeMutationMatch.Groups["id"].Value, out var intakeRequestId)
            && !await CanAccessIntakeRequestAsync(
                connection,
                access,
                actorUserId,
                intakeRequestId))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "intake_access_denied",
                "The requested Project Intake record was not found or is outside your role scope.");
            return false;
        }

        var intakeMatch = IntakeDocumentDownloadPath.Match(path);

        if (intakeMatch.Success
            && Guid.TryParse(intakeMatch.Groups["id"].Value, out var intakeDocumentId)
            && !await CanAccessIntakeDocumentAsync(
                connection,
                access,
                actorUserId,
                intakeDocumentId))
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "document_access_denied",
                "The requested intake document was not found or is outside your role scope.");
            return false;
        }

        return true;
    }

    private static async Task<bool> CanAccessProjectAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        Guid projectId)
    {
        if (access.HasOrganizationProjectScope)
        {
            return true;
        }

        return await IsProjectManagerAsync(connection, actorUserId, projectId)
               || await IsAssignedToProjectAsync(connection, actorUserId, projectId);
    }

    private static async Task<bool> CanAccessWorkRegisterDocumentAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        Guid documentId)
    {
        Guid projectId;
        string visibility;

        await using (var command = new NpgsqlCommand("""
            SELECT
                project_id,
                lower(COALESCE(NULLIF(visibility, ''), 'project_team'))
            FROM work_register_documents
            WHERE work_register_document_id = @document_id
              AND status = 'active'
            LIMIT 1;
            """, connection))
        {
            command.Parameters.AddWithValue("document_id", documentId);

            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return false;
            }

            projectId = reader.GetGuid(0);
            visibility = reader.GetString(1);
        }

        // The most restricted document class is available only to PTC/admin
        // organization-wide document managers. Project Managers and assigned
        // engineers never inherit this visibility through project membership.
        if (visibility == "ptc_admin_only")
        {
            return access.CanManageAllWorkRegisterDocuments;
        }

        if (access.CanManageAllWorkRegisterDocuments)
        {
            return true;
        }

        var isProjectManager = await IsProjectManagerAsync(connection, actorUserId, projectId);

        if (isProjectManager
            && visibility is "project_team" or "pm_ptc_admin" or "engineering_team")
        {
            return true;
        }

        var isAssigned = await IsAssignedToProjectAsync(connection, actorUserId, projectId);

        return isAssigned
               && visibility is "project_team" or "engineering_team";
    }

    private static async Task<bool> IsProjectManagerAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        Guid projectId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM projects
                WHERE project_id = @project_id
                  AND project_manager_user_id = @user_id
            );
            """, connection);

        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", actorUserId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> IsAssignedToProjectAsync(
        NpgsqlConnection connection,
        Guid actorUserId,
        Guid projectId)
    {
        if (await TableExistsAsync(connection, "project_assignments"))
        {
            await using var projectAssignmentCommand = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM project_assignments
                    WHERE project_id = @project_id
                      AND user_id = @user_id
                );
                """, connection);

            projectAssignmentCommand.Parameters.AddWithValue("project_id", projectId);
            projectAssignmentCommand.Parameters.AddWithValue("user_id", actorUserId);

            if (Convert.ToBoolean(await projectAssignmentCommand.ExecuteScalarAsync() ?? false))
            {
                return true;
            }
        }

        if (await TableExistsAsync(connection, "work_register_task_assignment_history"))
        {
            await using var assignmentCommand = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM work_register_task_assignment_history
                    WHERE project_id = @project_id
                      AND assigned_user_id = @user_id
                      AND assignment_status = 'active'
                      AND effective_end_date IS NULL
                );
                """, connection);

            assignmentCommand.Parameters.AddWithValue("project_id", projectId);
            assignmentCommand.Parameters.AddWithValue("user_id", actorUserId);

            if (Convert.ToBoolean(await assignmentCommand.ExecuteScalarAsync() ?? false))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> CanAccessIntakeRequestAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        Guid requestId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM project_intake_requests r
                WHERE r.project_intake_request_id = @request_id
                  AND (
                        r.requested_by_user_id = @user_id
                     OR r.assigned_pm_user_id = @user_id
                     OR r.account_executive_user_id = @user_id
                     OR r.solution_architect_user_id = @user_id
                  )
            );
            """, connection);

        command.Parameters.AddWithValue("request_id", requestId);
        command.Parameters.AddWithValue("user_id", actorUserId);

        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> CanAccessIntakeDocumentAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid actorUserId,
        Guid documentId)
    {
        if (access.HasOrganizationIntakeScope)
        {
            return true;
        }

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM project_intake_documents d
                JOIN project_intake_requests r
                  ON r.project_intake_request_id = d.project_intake_request_id
                WHERE d.project_intake_document_id = @document_id
                  AND COALESCE(d.is_active, TRUE) = TRUE
                  AND COALESCE(d.upload_source, '') <> 'celar_ai_chat_attachment'
                  AND (
                        @has_organization_scope = TRUE
                     OR r.requested_by_user_id = @user_id
                     OR r.assigned_pm_user_id = @user_id
                     OR r.account_executive_user_id = @user_id
                     OR r.solution_architect_user_id = @user_id
                     OR (
                            COALESCE(d.engineering_visible, FALSE) = TRUE
                        AND d.project_id IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM project_assignments pa
                            WHERE pa.project_id = d.project_id
                              AND pa.user_id = @user_id
                        )
                     )
                  )
            );
            """, connection);

        command.Parameters.AddWithValue("document_id", documentId);
        command.Parameters.AddWithValue("user_id", actorUserId);
        command.Parameters.AddWithValue("has_organization_scope", access.HasOrganizationIntakeScope);

        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string tableName)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass(@table_name) IS NOT NULL;",
            connection);

        command.Parameters.AddWithValue("table_name", $"public.{tableName}");
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Security authorization could not be evaluated because database configuration is incomplete.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(
                Environment.GetEnvironmentVariable("PTP_DB_PORT"),
                out var port)
                ? port
                : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 10
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<AccessContext> LoadAccessContextAsync(
        NpgsqlConnection connection,
        Guid userId)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var command = new NpgsqlCommand("""
            SELECT
                r.role_code,
                COALESCE(p.permission_code, '')
            FROM app_user_role_assignments ura
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
             AND r.is_active = TRUE
            LEFT JOIN app_role_permissions rp
              ON rp.app_role_id = r.app_role_id
            LEFT JOIN app_permissions p
              ON p.app_permission_id = rp.app_permission_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE;
            """, connection);

        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));

            if (!reader.IsDBNull(1))
            {
                var permission = reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(permission))
                {
                    permissions.Add(permission);
                }
            }
        }

        return new AccessContext(roles, permissions);
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string status,
        string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new
        {
            status,
            message
        });
    }

    private sealed record JsonBodyInspection(
        JsonDocument? Document,
        bool TooLarge,
        bool Malformed);

    private enum SecurityPolicy
    {
        None,
        Administrator,
        Reporting,
        TimeCompliance,
        HolidayAdministration,
        ProjectIntake,
        ProjectAssignment
    }

    private sealed record AccessContext(
        HashSet<string> Roles,
        HashSet<string> Permissions)
    {
        public bool IsSuperAdministrator => Roles.Contains("SUPER_ADMINISTRATOR");

        public bool IsAdministrator =>
            IsSuperAdministrator
            || Roles.Contains("ADMINISTRATOR");

        public bool CanViewReporting =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Roles.Contains("EXECUTIVE")
            || Roles.Contains("ACCOUNTING")
            || Roles.Contains("FINANCE")
            || Roles.Contains("PROJECT_MANAGEMENT")
            || Roles.Contains("PROJECT_MANAGER")
            || Permissions.Overlaps(new[]
            {
                "VIEW_REPORTING",
                "VIEW_REPORTS",
                "MANAGE_REPORTING",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        // SECURITY_20260729_TIME_COMPLIANCE_ORG_SCOPE
        public bool CanViewTimeCompliance =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Permissions.Overlaps(new[]
            {
                "VIEW_TIME_COMPLIANCE",
                "MANAGE_TIME_COMPLIANCE",
                "MANAGE_TIME_COMPLIANCE_NOTIFICATIONS",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        public bool CanManageHolidays =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Permissions.Overlaps(new[]
            {
                "MANAGE_HOLIDAYS",
                "MANAGE_TIMESHEET_CONFIGURATION",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        public bool CanUseProjectIntake =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Roles.Contains("PROJECT_MANAGEMENT")
            || Roles.Contains("PROJECT_MANAGER")
            || Roles.Contains("SALES")
            || Roles.Contains("SOLUTION_ARCHITECT")
            || Permissions.Overlaps(new[]
            {
                "VIEW_PROJECT_INTAKE",
                "MANAGE_PROJECT_INTAKE",
                "MANAGE_PROJECT_DOCUMENTS",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        public bool CanManageProjectAssignments =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Permissions.Overlaps(new[]
            {
                "MANAGE_PROJECT_ASSIGNMENTS",
                "MANAGE_PROJECT_COORDINATION",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        public bool HasOrganizationIntakeScope =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Permissions.Overlaps(new[]
            {
                "MANAGE_PROJECT_INTAKE",
                "MANAGE_PROJECT_DOCUMENTS",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        public bool CanManageAllWorkRegisterDocuments =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Permissions.Overlaps(new[]
            {
                "MANAGE_WORK_REGISTER",
                "MANAGE_PROJECT_DOCUMENTS",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        public bool HasOrganizationProjectScope =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Roles.Contains("EXECUTIVE")
            || Permissions.Overlaps(new[]
            {
                "VIEW_ALL_PROJECTS",
                "MANAGE_WORK_REGISTER",
                "MANAGE_PROJECT_ASSIGNMENTS",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });
    }

    // SECURITY_20260729_FOLLOWUP_COMPLETE
}
