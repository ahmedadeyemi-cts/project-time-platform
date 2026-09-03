Warning: truncated output (original token count: 403319)
... 564700 bytes omitted ...

using System.Net.Http;
using System.Text.Json;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";
const string DevelopmentUserDisplayName = "Ahmed Adeyemi";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();
builder.Services.AddProjectPulseAi();
builder.Services.AddHostedService<Module025SowGsdGenerationWorker>();
builder.Services
    .AddHttpClient("Module026", client => client.Timeout = TimeSpan.FromSeconds(12))
    .ConfigurePrimaryHttpMessageHandler(CrmErpIntegrationModule.CreateSecureHttpHandler);

var app = builder.Build();
// This must remain the first middleware. Candidate revisions are closed before
// authentication, View-As, module middleware, or any application endpoint can run.
app.UseProjectPulseAiCandidateRequestFence();

/* 050_CRITICAL_LAUNCH_BLOCKER_PRODUCTION_GUARD_START */
var projectPulse050BlockedDevRouteTokens = new[]
{
    "dev-login",
    "development-login",
    "development/session",
    "dev/session",
    "debug-login",
    "mint-session",
    "impersonate",
    "bypass-auth"
};

var projectPulse050SessionExemptPrefixes = new[]
{
    "/health",
    "/api/auth/",
    "/api/public/",
    "/api/bootstrap/",
    "/api/app-config",
    "/api/config"
};

var projectPulse050SessionRequiredPrefixes = new[]
{
    "/api/admin/",
    "/api/accounting/",
    "/api/approval",
    "/api/approvals",
    "/api/manager/",
    "/api/profile/",
    "/api/project-closeout/",
    "/api/security/",
    "/api/time",
    "/api/timesheet",
    "/api/timesheets",
    "/api/utilization",
    "/api/workflow",
    "/api/projects",
    "/api/project"
};

app.Use(async (httpContext, next) =>
{
    var requestPath = httpContext.Request.Path.Value ?? string.Empty;
    var normalizedPath = requestPath.ToLowerInvariant();

    if (normalizedPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        && projectPulse050BlockedDevRouteTokens.Any(token => normalizedPath.Contains(token, StringComparison.OrdinalIgnoreCase)))
    {
        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsJsonAsync(new
        {
            status = "not_found",
            message = "Route is not available in this environment.",
            guard = "050_dev_auth_shortcut_blocked"
        });
        return;
    }

    var isApiRoute = normalizedPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    var isExempt = projectPulse050SessionExemptPrefixes.Any(prefix =>
        normalizedPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    var isProtectedCriticalRoute = projectPulse050SessionRequiredPrefixes.Any(prefix =>
        normalizedPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
        || normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /* 050C_RELAX_DASHBOARD_READ_ROUTE_SESSION_GUARD */
    var isUnsafeApiMethod =
        HttpMethods.IsPost(httpContext.Request.Method)
        || HttpMethods.IsPut(httpContext.Request.Method)
        || HttpMethods.IsPatch(httpContext.Request.Method)
        || HttpMethods.IsDelete(httpContext.Request.Method);

    if (isApiRoute && !isExempt && isProtectedCriticalRoute && isUnsafeApiMethod)
    {
        /* 051E_WRITE_GUARD_DIRECT_SESSION_VALIDATION_START */
        var sessionUserId = GetProjectPulseSessionUserId(httpContext);

        if (sessionUserId is null)
        {
            var validation = await ValidateProjectPulseSessionAsync(httpContext);

            if (!validation.IsValid || validation.UserId is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    status = "session_required",
                    message = validation.Message ?? "Missing session token.",
                    guard = "051E_critical_write_route_session_validation_failed"
                });
                return;
            }

            httpContext.Items["ProjectPulseSessionUserId"] = validation.UserId.Value;
            httpContext.Items["ProjectPulseSessionEmail"] = validation.Email ?? string.Empty;
            httpContext.Items["ProjectPulseSessionProvider"] = validation.ProviderCode ?? string.Empty;

            if (validation.ExpiresAt is not null)
            {
                httpContext.Items["ProjectPulseSessionExpiresAt"] = validation.ExpiresAt.Value;
            }
        }
        /* 051E_WRITE_GUARD_DIRECT_SESSION_VALIDATION_END */
    }

    await next();
});
/* 050_CRITICAL_LAUNCH_BLOCKER_PRODUCTION_GUARD_END */


app.Use(async (context, next) =>
{
    /* MODULES_071_072_PUBLIC_READ_SESSION_BYPASS */
    var isExplicitPublicReadApi =
        HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path.StartsWithSegments(
            "/api/public",
            StringComparison.OrdinalIgnoreCase);

    if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase) ||
        isExplicitPublicReadApi ||
        IsProjectPulsePublicApiPath(context) ||
        CertiniaBillingModule.IsValidIntegrationRequest(context))
    {
        await next();
        return;
    }

    if (ProjectPulseIsPublicAuthEndpoint(context.Request.Path.Value))
    {
        await next();
        return;
    }

    var validation = await ValidateProjectPulseSessionAsync(context);

    if (!validation.IsValid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "session_required",
            message = validation.Message ?? "Your Project Pulse session is missing or expired. Please sign in again."
        });
        return;
    }

    context.Items["ProjectPulseSessionUserId"] = validation.UserId;
    context.Items["ProjectPulseSessionEmail"] = validation.Email;
    context.Items["ProjectPulseSessionProvider"] = validation.ProviderCode;
    context.Items["ProjectPulseSessionExpiresAt"] = validation.ExpiresAt;

    if (!await ApplyProjectPulseViewAsContextAsync(context, validation))
    {
        return;
    }

    await next();
});

app.UseAdminAuditTelemetry();
app.UseProjectPulseSecurityHardening();
app.UseWorkRegisterAuthorization();
app.UseMiddleware<ProjectTime.Api.Modules.CelarAiTransientFailureMiddleware>();




static string ProjectPulseFormValue(IFormCollection form, params string[] keys)
{
    foreach (var key in keys)
    {
        if (form.TryGetValue(key, out var value))
        {
            var textValue = value.ToString();
            if (!string.IsNullOrWhiteSpace(textValue))
            {
                return textValue.Trim();
            }
        }
    }

    return string.Empty;
}


// 055D_5K1_SAFE_IDENTIFIER_APPLY_HELPER
static string ProjectPulse055D5K1JsonString(System.Text.Json.JsonElement source, params string[] keys)
{
    if (source.ValueKind != System.Text.Json.JsonValueKind.Object)
    {
        return "";
    }

    foreach (var key in keys)
    {
        if (source.TryGetProperty(key, out var value))
        {
            if (value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return value.GetString()?.Trim() ?? "";
            }

            if (value.ValueKind == System.Text.Json.JsonValueKind.Number ||
                value.ValueKind == System.Text.Json.JsonValueKind.True ||
                value.ValueKind == System.Text.Json.JsonValueKind.False)
            {
                return value.ToString().Trim();
            }
        }
    }

    return "";
}



// 055D_5L2_APPLY_V3_JSON_HELPER
static string ProjectPulse055D5L2JsonString(System.Text.Json.JsonElement source, params string[] keys)
{
    if (source.ValueKind != System.Text.Json.JsonValueKind.Object)
    {
        return "";
    }

    foreach (var key in keys)
    {
        if (source.TryGetProperty(key, out var value))
        {
            if (value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return value.GetString()?.Trim() ?? "";
            }

            if (value.ValueKind == System.Text.Json.JsonValueKind.Number ||
                value.ValueKind == System.Text.Json.JsonValueKind.True ||
                value.ValueKind == System.Text.Json.JsonValueKind.False)
            {
                return value.ToString().Trim();
            }
        }
    }

    return "";
}

app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Project Time Platform API",
    timestampUtc = DateTimeOffset.UtcNow
}));


static string ProjectPulseRequiredEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing required environment variable: {name}");
    }

    return value;
}

static string ProjectPulseBase64UrlEncode(byte[] input)
{
    return Convert.ToBase64String(input)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

static byte[] ProjectPulseBase64UrlDecode(string input)
{
    var padded = input.Replace('-', '+').Replace('_', '/');

    switch (padded.Length % 4)
    {
        case 2:
            padded += "==";
            break;
        case 3:
            padded += "=";
            break;
    }

    return Convert.FromBase64String(padded);
}

static string ProjectPulseSecureToken(int byteLength = 32)
{
    return ProjectPulseBase64UrlEncode(RandomNumberGenerator.GetBytes(byteLength));
}


/* 043B_PROFILE_IMAGE_PERSISTENCE_HELPERS_START */
static async Task ProjectPulse043BEnsureProfileColumnsAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand("""
        ALTER TABLE app_users
        ADD COLUMN IF NOT EXISTS profile_photo_data_url TEXT;

        ALTER TABLE app_users
        ADD COLUMN IF NOT EXISTS profile_photo_updated_at TIMESTAMPTZ;
        """, connection);

    await command.ExecuteNonQueryAsync();
}

static string? ProjectPulse043BJsonString(System.Text.Json.JsonElement element, string propertyName)
{
    if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

    return element.TryGetProperty(propertyName, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
        ? value.GetString()
        : null;
}

static (bool Valid, string Message) ProjectPulse043BValidateProfilePhotoDataUrl(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return (true, "Profile picture removal is valid.");
    }

    if (!value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
    {
        return (false, "Profile picture must be uploaded as an image data URL.");
    }

    var commaIndex = value.IndexOf(',');

    if (commaIndex <= 0)
    {
        return (false, "Profile picture data URL is missing the base64 payload.");
    }

    var metadata = value[..commaIndex].ToLowerInvariant();
    var base64Payload = value[(commaIndex + 1)..];

    var allowedPrefixes = new[]
    {
        "data:image/png;base64",
        "data:image/jpeg;base64",
        "data:image/jpg;base64",
        "data:image/webp;base64",
        "data:image/gif;base64"
    };

    if (!allowedPrefixes.Any(prefix => metadata == prefix))
    {
        return (false, "Profile picture must be PNG, JPG, JPEG, WEBP, or GIF.");
    }

    try
    {
        var bytes = Convert.FromBase64String(base64Payload);

        if (bytes.Length > 2 * 1024 * 1024)
        {
            return (false, "Profile picture must be smaller than 2 MB.");
        }

        if (bytes.Length == 0)
        {
            return (false, "Profile picture payload is empty.");
        }
    }
    catch
    {
        return (false, "Profile picture payload is not valid base64.");
    }

    return (true, "Profile picture is valid.");
}
/* 043B_PROFILE_IMAGE_PERSISTENCE_HELPERS_END */

static string? ProjectPulseJsonString(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
}

static long? ProjectPulseJsonLong(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return null;
    }

    if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
    {
        return value;
    }

    return null;
}

static JsonElement ProjectPulseDecodeJwtPayload(string jwt)
{
    var parts = jwt.Split('.');

    if (parts.Length != 3)
    {
        throw new InvalidOperationException("Invalid JWT format.");
    }

    var payloadBytes = ProjectPulseBase64UrlDecode(parts[1]);
    using var document = JsonDocument.Parse(payloadBytes);

    return document.RootElement.Clone();
}

static async Task<JsonElement> ProjectPulseValidateMicrosoftIdTokenAsync(
    string idToken,
    string tenantId,
    string clientId,
    string expectedNonce)
{
    var parts = idToken.Split('.');

    if (parts.Length != 3)
    {
        throw new InvalidOperationException("Invalid ID token format.");
    }

    var headerJson = Encoding.UTF8.GetString(ProjectPulseBase64UrlDecode(parts[0]));
    using var headerDocument = JsonDocument.Parse(headerJson);

    var kid = ProjectPulseJsonString(headerDocument.RootElement, "kid");
    var alg = ProjectPulseJsonString(headerDocument.RootElement, "alg");

    if (alg != "RS256" || string.IsNullOrWhiteSpace(kid))
    {
        throw new InvalidOperationException("Unsupported ID token signature algorithm.");
    }

    using var httpClient = new HttpClient();

    var metadataUrl = $"https://login.microsoftonline.com/{tenantId}/v2.0/.well-known/openid-configuration";
    var metadataJson = await httpClient.GetStringAsync(metadataUrl);

    using var metadataDocument = JsonDocument.Parse(metadataJson);

    var jwksUri = ProjectPulseJsonString(metadataDocument.RootElement, "jwks_uri")
        ?? throw new InvalidOperationException("Missing jwks_uri from OpenID configuration.");

    var jwksJson = await httpClient.GetStringAsync(jwksUri);

    using var jwksDocument = JsonDocument.Parse(jwksJson);

    JsonElement? signingKey = null;

    foreach (var key in jwksDocument.RootElement.GetProperty("keys").EnumerateArray())
    {
        if (ProjectPulseJsonString(key, "kid") == kid)
        {
            signingKey = key.Clone();
            break;
        }
    }

    if (signingKey is null)
    {
        throw new InvalidOperationException("Unable to find Microsoft signing key.");
    }

    var modulus = ProjectPulseBase64UrlDecode(ProjectPulseJsonString(signingKey.Value, "n") ?? throw new InvalidOperationException("Missing RSA modulus."));
    var exponent = ProjectPulseBase64UrlDecode(ProjectPulseJsonString(signingKey.Value, "e") ?? throw new InvalidOperationException("Missing RSA exponent."));

    using var rsa = RSA.Create();
    rsa.ImportParameters(new RSAParameters
    {
        Modulus = modulus,
        Exponent = exponent
    });

    var signedData = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
    var signature = ProjectPulseBase64UrlDecode(parts[2]);

    var signatureValid = rsa.VerifyData(
        signedData,
        signature,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);

    if (!signatureValid)
    {
        throw new InvalidOperationException("Invalid ID token signature.");
    }

    var payload = ProjectPulseDecodeJwtPayload(idToken);

    var issuer = ProjectPulseJsonString(payload, "iss") ?? "";
    var expectedIssuer = $"https://login.microsoftonline.com/{tenantId}/v2.0";

    if (!string.Equals(issuer.TrimEnd('/'), expectedIssuer.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("ID token issuer did not match expected tenant.");
    }

    var audience = ProjectPulseJsonString(payload, "aud") ?? "";

    if (!string.Equals(audience, clientId, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("ID token audience did not match client ID.");
    }

    var nonce = ProjectPulseJsonString(payload, "nonce") ?? "";

    if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("ID token nonce did not match.");
    }

    var expiresAt = ProjectPulseJsonLong(payload, "exp") ?? 0;

    if (expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    {
        throw new InvalidOperationException("ID token has expired.");
    }

    return payload;
}

async Task<Guid> ProjectPulseEnsureEntraUserAsync(
    NpgsqlConnection connection,
    string tenantId,
    string objectId,
    string email,
    string displayName,
    string? userPrincipalName,
    string sourceProvider)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO app_users (
            email,
            display_name,
            is_active,
            login_enabled,
            source_provider,
            entra_tenant_id,
            entra_object_id,
            entra_user_principal_name,
            last_sso_login_at,
            last_directory_sync_at
        )
        VALUES (
            @email,
            @display_name,
            TRUE,
            TRUE,
            @source_provider,
            @tenant_id,
            @entra_object_id,
            @user_principal_name,
            NOW(),
            NOW()
        )
        ON CONFLICT (email) DO UPDATE
        SET display_name = EXCLUDED.display_name,
            is_active = TRUE,
            login_enabled = TRUE,
            source_provider = EXCLUDED.source_provider,
            entra_tenant_id = EXCLUDED.entra_tenant_id,
            entra_object_id = EXCLUDED.entra_object_id,
            entra_user_principal_name = EXCLUDED.entra_user_principal_name,
            last_sso_login_at = NOW(),
            updated_at = NOW()
        RETURNING user_id;
        """, connection);

    command.Parameters.AddWithValue("email", email);
    command.Parameters.AddWithValue("display_name", displayName);
    command.Parameters.AddWithValue("source_provider", sourceProvider);
    command.Parameters.AddWithValue("tenant_id", tenantId);
    command.Parameters.AddWithValue("entra_object_id", objectId);
    command.Parameters.AddWithValue("user_principal_name", (object?)userPrincipalName ?? DBNull.Value);

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to upsert Entra user."));
}

async Task ProjectPulseAssignDefaultEngineerRoleAsync(NpgsqlConnection connection, Guid userId, string reason)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO app_user_role_assignments (
            user_id,
            app_role_id,
            assignment_reason,
            is_active
        )
        SELECT
            @user_id,
            r.app_role_id,
            @reason,
            TRUE
        FROM app_roles r
        WHERE r.role_code IN ('ENGINEERING', 'ENGINEER')
          AND r.is_active = TRUE
        ON CONFLICT (user_id, app_role_id) DO UPDATE
        SET is_active = TRUE,
            assignment_reason = EXCLUDED.assignment_reason,
            updated_at = NOW();
        """, connection);

    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("reason", reason);

    await command.ExecuteNonQueryAsync();
}



static bool ProjectPulseIsPublicAuthEndpoint(string? requestPath)
{
    if (string.IsNullOrWhiteSpace(requestPath))
    {
        return false;
    }

    return requestPath.StartsWith("/api/version", StringComparison.OrdinalIgnoreCase)
        || requestPath.StartsWith("/api/auth/login/route", StringComparison.OrdinalIgnoreCase)
        || requestPath.StartsWith("/api/auth/local/login", StringComparison.OrdinalIgnoreCase)
        || requestPath.StartsWith("/api/auth/password-reset/request", StringComparison.OrdinalIgnoreCase)
        || requestPath.StartsWith("/api/auth/sso/start", StringComparison.OrdinalIgnoreCase)
        || requestPath.StartsWith("/api/auth/sso/callback", StringComparison.OrdinalIgnoreCase)
        || requestPath.StartsWith("/api/auth/sso/test-config", StringComparison.OrdinalIgnoreCase);
}




app.MapGet("/api/production-data-readiness", BuildProjectPulseProductionDataReadinessResultAsync);

app.MapGet("/api/production/data-readiness", async () =>
{
    var config = DatabaseConfig.FromEnvironment();

    if (config.Missing.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing,
            generatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var checks = new List<object>();

        async Task AddCountCheckAsync(
            string key,
            string label,
            string tableName,
            int readyMinimum,
            string purpose,
            string webpageCheck)
        {
            var count = await QueryProjectPulseDataReadinessCountAsync(connection, tableName);
            var tableExists = count.HasValue;
            var status = !tableExists
                ? "missing_table"
                : count.Value >= readyMinimum
                    ? "ready"
                    : "needs_data";

            checks.Add(new
            {
                key,
                label,
                tableName,
                count = count ?? 0,
                readyMinimum,
                tableExists,
                status,
                purpose,
                webpageCheck
            });
        }

        await AddCountCheckAsync(
            "users",
            "Users",
            "app_users",
            1,
            "Confirms real users exist for login, role assignment, approvals, and workflow ownership.",
            "Open User Administration or Role Administration and confirm users are present.");

        await AddCountCheckAsync(
            "roles",
            "Roles",
            "app_roles",
            1,
            "Confirms application roles exist for role-based access and dashboard/module visibility.",
            "Open Role / Security Administration and confirm roles and permissions are visible.");

        await AddCountCheckAsync(
            "customers",
            "Customers",
            "clients",
            1,
            "Confirms customer/account data exists for project intake, allocation, billing, and reporting.",
            "Open Customer Directory and confirm customer records or a clear empty state appears.");

        await AddCountCheckAsync(
            "projects",
            "Projects",
            "projects",
            1,
            "Confirms project records exist for timesheets, project workspace, resource assignment, and workload reporting.",
            "Open Project Workspace or Resource Assignment and confirm project data is visible.");

        await AddCountCheckAsync(
            "project_tasks",
            "Project Tasks",
            "project_tasks",
            1,
            "Confirms task-level work is available for time entry, assignment, approvals, and exports.",
            "Open Project Workspace and confirm project tasks are available or empty state is understandable.");

        await AddCountCheckAsync(
            "timesheets",
            "Timesheets",
            "timesheets",
            1,
            "Confirms timesheet headers exist for weekly time entry and manager approval workflows.",
            "Open Timesheet or Manager Approvals and confirm time workflow data loads.");

        await AddCountCheckAsync(
            "time_entries",
            "Time Entries",
            "time_entries",
            1,
            "Confirms submitted or draft time data exists for approvals, exports, utilization, and audit evidence.",
            "Open Workflow or Manager Approvals and confirm time data appears when expected.");

        await AddCountCheckAsync(
            "manager_approvals",
            "Manager Approval Evidence",
            "manager_approval_actions",
            1,
            "Confirms approval decision evidence exists or can be tracked for audit and export readiness.",
            "Open Manager Approvals and confirm approval workflow state is understandable.");

        await AddCountCheckAsync(
            "exports",
            "Export Packages",
            "time_export_packages",
            1,
            "Confirms export package evidence exists for accounting and period-close workflows.",
            "Open Approval / Export / Audit Workflows and confirm export readiness is visible.");

        await AddCountCheckAsync(
            "audit_events",
            "Audit Events",
            "audit_events",
            1,
            "Confirms system actions are being logged for accountability and troubleshooting.",
            "Open Audit History and confirm audit records or a clear empty state appears.");

        await AddCountCheckAsync(
            "notification_events",
            "Notification Events",
            "notification_events",
            1,
            "Confirms notification evidence is available for time compliance and operational messaging.",
            "Open notification-related pages and confirm events are visible after notification activity.");

        var checkObjects = checks
            .Select(check => (dynamic)check)
            .ToList();

        var readyCount = checkObjects.Count(check => check.status == "ready");
        var needsDataCount = checkObjects.Count(check => check.status == "needs_data");
        var missingTableCount = checkObjects.Count(check => check.status == "missing_table");

        return Results.Ok(new
        {
            status = missingTableCount == 0 && needsDataCount == 0 ? "ready" : "needs_data_review",
            generatedAtUtc = DateTimeOffset.UtcNow,
            summary = new
            {
                checkCount = checks.Count,
                readyCount,
                needsDataCount,
                missingTableCount,
                productionDataReady = missingTableCount == 0 && needsDataCount == 0
            },
            checks
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Production data readiness failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/version", () => Results.Ok(new
{
    application = "Project Time Platform",
    component = "ProjectTime.Api",
    version = "0.9.0",
    framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/db-config-check", () =>
{
    var config = DatabaseConfig.FromEnvironment();
    return Results.Ok(new
    {
        configured = config.Missing.Count == 0,
        missing = config.Missing,
        database = config.Database,
        user = config.Username,
        host = config.Host,
        port = config.Port,
        passwordConfigured = !string.IsNullOrWhiteSpace(config.Password)
    });
});

app.MapGet("/api/db-health", async () =>
{
    var config = DatabaseConfig.FromEnvironment();

    if (config.Missing.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing
        });
    }

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT current_database(), current_user, now();", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return Results.Ok(new
        {
            status = "database_connected",
            database = reader.GetString(0),
            user = reader.GetString(1),
            timestamp = reader.GetDateTime(2)
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Database connection failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/schema/tables", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var tables = new List<string>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT table_name
        FROM information_schema.tables
        WHERE table_schema = 'public'
        ORDER BY table_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        tables.Add(reader.GetString(0));
    }

    return Results.Ok(new
    {
        count = tables.Count,
        tables
    });
});



// 019M-AP Work Task Builder / Task Classification Foundation
app.MapGet("/api/work-tasks/summary", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            COALESCE(p.permission_code, '') AS permission_code
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await accessCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));

            if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                permissions.Add(reader.GetString(1));
            }
        }
    }

    var canViewAll =
        (roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR"))
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var isProjectManager =
        (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || roles.Contains("PROJECT_MANAGEMENT");

    var canViewBuilder =
        canViewAll
        || isProjectManager
        || roles.Contains("MANAGER")
        || (roles.Contains("ENGINEERING_LEAD") || roles.Contains("ENGINEERING_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || permissions.Contains("VIEW_WORK_TASK_BUILDER")
        || permissions.Contains("MANAGE_WORK_TASK_BUILDER")
        || permissions.Contains("ASSIGN_WORK_TASKS");

    var canManageTemplates =
        canViewAll
        || permissions.Contains("MANAGE_WORK_TASK_BUILDER");

    /* 053E_PM_ASSIGNMENT_ONLY_ACCESS_FLAGS_START */
    var canCreateProjectTasks =
        canViewAll
        || permissions.Contains("MANAGE_WORK_TASK_BUILDER")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canAssignWorkTasks =
        canViewAll
        || isProjectManager
        || permissions.Contains("ASSIGN_WORK_TASKS");

    var canAssignTasks = canAssignWorkTasks;
    var assignmentDefaultHourlyCost = ProjectPulse053HGetDefaultEngineeringHourlyCost();
    /* 053E_PM_ASSIGNMENT_ONLY_ACCESS_FLAGS_END */

    if (!canViewBuilder)
    {
        return Results.Json(new
        {
            canViewWorkTaskBuilder = false,
            status = "access_denied",
            message = "Work Task Builder is available to Project Team Coordinators, Project Managers, Managers, Team Leads, and Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var templates = new List<object>();
    await using (var templateCommand = new NpgsqlCommand("""
        SELECT
            work_task_template_id,
            template_code,
            template_name,
            COALESCE(template_description, '') AS template_description,
            task_category,
            billing_classification,
            utilization_classification,
            utilization_bucket,
            default_billable,
            default_requires_approval,
            is_active,
            display_order
        FROM work_task_templates
        ORDER BY is_active DESC, display_order, template_name;
        """, connection))
    {
        await using var reader = await templateCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            templates.Add(new
            {
                templateId = reader.GetGuid(0),
                templateCode = reader.GetString(1),
                templateName = reader.GetString(2),
                templateDescription = reader.GetString(3),
                taskCategory = reader.GetString(4),
                billingClassification = reader.GetString(5),
                utilizationClassification = reader.GetString(6),
                utilizationBucket = reader.GetString(7),
                defaultBillable = reader.GetBoolean(8),
                defaultRequiresApproval = reader.GetBoolean(9),
                isActive = reader.GetBoolean(10),
                displayOrder = reader.GetInt32(11)
            });
        }
    }

    var projects = new List<object>();
    await using (var projectCommand = new NpgsqlCommand("""
        WITH assignment_rollup AS (
            SELECT
                pa.project_id,
                pa.task_id,
                COUNT(DISTINCT pa.user_id)::bigint AS assigned_engineer_count,
                COALESCE(SUM(pa.assigned_hours), 0)::numeric AS assigned_hours
            FROM project_assignments pa
            WHERE pa.effective_start_date <= CURRENT_DATE
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= CURRENT_DATE)
            GROUP BY pa.project_id, pa.task_id
        ),
        used_rollup AS (
            SELECT
                te.project_id,
                te.task_id,
                COALESCE(SUM(te.hours), 0)::numeric AS used_hours
            FROM time_entries te
            WHERE te.project_id IS NOT NULL
              AND te.task_id IS NOT NULL
              AND COALESCE(te.status, 'draft') NOT IN ('manager_declined', 'rejected', 'voided')
            GROUP BY te.project_id, te.task_id
        )
        SELECT
            p.project_id,
            p.project_code,
            p.project_name,
            COALESCE(c.client_name, '') AS client_name,
            p.status,
            p.billable AS project_billable,
            p.project_manager_user_id,
            COALESCE(pm.display_name, '') AS project_manager_name,
            pt.task_id,
            pt.task_code,
            pt.task_name,
            COALESCE(pt.task_description, '') AS task_description,
            pt.billable,
            pt.utilization_bucket,
            pt.utilization_requires_approval,
            pt.is_active,
            COALESCE(pt.work_task_category, 'project_task') AS work_task_category,
            COALESCE(pt.billing_classification, CASE WHEN pt.billable THEN 'billable' ELSE 'non_billable' END) AS billing_classification,
            COALESCE(pt.utilization_classification, CASE WHEN pt.billable THEN 'billable_utilization' ELSE 'non_billable_utilization' END) AS utilization_classification,
            COALESCE(pt.service_request_number, '') AS service_request_number,
            COALESCE(ar.assigned_engineer_count, 0)::bigint AS assigned_engineer_count,
            COALESCE(ar.assigned_hours, 0)::numeric AS assigned_hours,
            COALESCE(ur.used_hours, 0)::numeric AS used_hours,
            /* 053H_WORK_TASK_SUMMARY_COST_FIELDS_START */
            COALESCE(p.planned_engineering_cost, 0)::numeric AS planned_engineering_cost,
            COALESCE(p.planned_pm_cost, 0)::numeric AS planned_pm_cost,
            COALESCE(p.planned_total_project_cost, 0)::numeric AS planned_total_project_cost
            /* 053H_WORK_TASK_SUMMARY_COST_FIELDS_END */
        FROM projects p
        LEFT JOIN clients c
            ON c.client_id = p.client_id
        LEFT JOIN app_users pm
            ON pm.user_id = p.project_manager_user_id
        LEFT JOIN project_tasks pt
            ON pt.project_id = p.project_id
        LEFT JOIN assignment_rollup ar
            ON ar.project_id = p.project_id
           AND ar.task_id = pt.task_id
        LEFT JOIN used_rollup ur
            ON ur.project_id = p.project_id
           AND ur.task_id = pt.task_id
        WHERE p.status <> 'archived'
          AND (@can_view_all = TRUE OR p.project_manager_user_id = @session_user_id)
        ORDER BY p.project_name, pt.task_code NULLS LAST, pt.task_name NULLS LAST;
        """, connection))
    {
        projectCommand.Parameters.AddWithValue("can_view_all", canViewAll);
        projectCommand.Parameters.AddWithValue("session_user_id", sessionUserId.Value);

        var projectMap = new Dictionary<Guid, dynamic>();

        await using var reader = await projectCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var projectId = reader.GetGuid(0);
            if (!projectMap.TryGetValue(projectId, out var projectObj))
            {
                projectObj = new
                {
                    projectId,
                    projectCode = reader.GetString(1),
                    projectName = reader.GetString(2),
                    clientName = reader.GetString(3),
                    status = reader.GetString(4),
                    projectBillable = reader.GetBoolean(5),
                    projectManagerUserId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
                    projectManagerName = reader.GetString(7),
                    /* 053H_WORK_TASK_SUMMARY_COST_PAYLOAD_START */
                    plannedEngineeringCost = reader.GetDecimal(23),
                    plannedPmCost = reader.GetDecimal(24),
                    plannedTotalProjectCost = reader.GetDecimal(25),
                    assignmentBudget = reader.GetDecimal(23) > 0m ? reader.GetDecimal(23) : reader.GetDecimal(25),
                    assignmentDefaultHourlyCost,
                    /* 053H_WORK_TASK_SUMMARY_COST_PAYLOAD_END */
                    tasks = new List<object>()
                };

                projectMap[projectId] = projectObj;
            }

            if (!reader.IsDBNull(8))
            {
                var assignedHours = reader.GetDecimal(21);
                var usedHours = reader.GetDecimal(22);

                projectObj.tasks.Add(new
                {
                    taskId = reader.GetGuid(8),
                    taskCode = reader.GetString(9),
                    taskName = reader.GetString(10),
                    taskDescription = reader.GetString(11),
                    billable = reader.GetBoolean(12),
                    utilizationBucket = reader.GetString(13),
                    utilizationRequiresApproval = reader.GetBoolean(14),
                    isActive = reader.GetBoolean(15),
                    workTaskCategory = reader.GetString(16),
                    billingClassification = reader.GetString(17),
                    utilizationClassification = reader.GetString(18),
                    serviceRequestNumber = reader.GetString(19),
                    assignedEngineerCount = reader.GetInt64(20),
                    assignedHours,
                    usedHours,
                    remainingHours = Math.Max(0m, assignedHours - usedHours)
                });
            }
        }

        projects.AddRange(projectMap.Values);
    }

    var engineers = new List<object>();
    await using (var engineerCommand = new NpgsqlCommand("""
        SELECT DISTINCT
            u.user_id,
            COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
            u.email,
            COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), 'Unassigned') AS team_name
        FROM app_users u
        JOIN app_user_role_assignments ura
            ON ura.user_id = u.user_id
           AND ura.is_active = TRUE
        JOIN app_roles r
            ON r.app_role_id = ura.app_role_id
           AND r.is_active = TRUE
        WHERE u.is_active = TRUE
          AND r.role_code IN ('ENGINEERING', 'ENGINEER')
        ORDER BY team_name, display_name, u.email;
        """, connection))
    {
        await using var reader = await engineerCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            engineers.Add(new
            {
                userId = reader.GetGuid(0),
                displayName = reader.GetString(1),
                email = reader.GetString(2),
                teamName = reader.GetString(3)
            });
        }
    }

    var nonProjectCategories = new List<object>();
    await using (var categoryCommand = new NpgsqlCommand("""
        SELECT
            non_project_time_category_id,
            category_code,
            category_name,
            COALESCE(category_description, '') AS category_description,
            utilization_classification,
            utilization_bucket,
            requires_approval,
            is_active,
            display_order
        FROM non_project_time_categories
        ORDER BY display_order, category_name;
        """, connection))
    {
        await using var reader = await categoryCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nonProjectCategories.Add(new
            {
                categoryId = reader.GetGuid(0),
                categoryCode = reader.GetString(1),
                categoryName = reader.GetString(2),
                categoryDescription = reader.GetString(3),
                utilizationClassification = reader.GetString(4),
                utilizationBucket = reader.GetString(5),
                requiresApproval = reader.GetBoolean(6),
                isActive = reader.GetBoolean(7),
                displayOrder = reader.GetInt32(8)
            });
        }
    }

    var totalProjectTasks = projects.Sum(project =>
    {
        var tasksProperty = project.GetType().GetProperty("tasks");
        var tasks = tasksProperty?.GetValue(project) as List<object>;
        return tasks?.Count ?? 0;
    });

    return Results.Ok(new
    {
        module = "019M-AP Work Task Builder",
        canViewWorkTaskBuilder = true,
        access = new
        {
            canViewAll,
            isProjectManager,
            canManageTemplates,
            canCreateProjectTasks,
            canAssignWorkTasks,
            canAssignTasks,
            assignmentDefaultHourlyCost
        },
        classifications = new
        {
            taskCategories = new[]
            {
                new { value = "open_task", label = "Open Tasks", description = "General work items not yet tied to a specific delivery task." },
                new { value = "project_task", label = "Project Tasks", description = "Tasks tied to an approved project and project plan." },
                new { value = "service_request_task", label = "Service Request Tasks", description = "Customer service request work that needs project/task tracking." },
                new { value = "non_project_task", label = "Non-Project Tasks", description = "Internal operational work outside a customer project." }
            },
            billingClassifications = new[]
            {
                new { value = "billable", label = "Billable" },
                new { value = "non_billable", label = "Non-billable" }
            },
            utilizationClassifications = new[]
            {
                new { value = "billable_utilization", label = "Billable utilization eligible", bucket = "billable" },
                new { value = "non_billable_utilization", label = "Non-billable utilization eligible", bucket = "non_billable" },
                new { value = "non_billable_non_utilization", label = "Non-billable non-utilization eligible", bucket = "excluded" }
            }
        },
        summary = new
        {
            templateCount = templates.Count,
            projectCount = projects.Count,
            projectTaskCount = totalProjectTasks,
            engineerCount = engineers.Count,
            nonProjectCategoryCount = nonProjectCategories.Count
        },
        templates,
        projects,
        engineers,
        nonProjectCategories
    });
});

app.MapPost("/api/work-tasks/templates", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var canManage = false;
    await using (var accessCommand = new NpgsqlCommand("""
        SELECT BOOL_OR(
            r.role_code IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
            OR p.permission_code IN ('MANAGE_WORK_TASK_BUILDER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
        )
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        var result = await accessCommand.ExecuteScalarAsync();
        canManage = result is bool value && value;
    }

    if (!canManage)
    {
        return Results.Json(new { status = "access_denied", message = "Only Administrators and Project Team Coordinators can manage work task templates." }, statusCode: StatusCodes.Status403Forbidden);
    }

    using var document = await System.Text.Json.JsonDocument.ParseAsync(httpContext.Request.Body);
    var root = document.RootElement;

    string ReadString(string name, string fallback = "")
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return fallback;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    var templateName = ReadString("templateName");
    if (string.IsNullOrWhiteSpace(templateName))
    {
        return Results.Json(new { status = "validation_error", message = "Template name is required." }, statusCode: StatusCodes.Status400BadRequest);
    }

    var templateCode = ReadString("templateCode");
    if (string.IsNullOrWhiteSpace(templateCode))
    {
        templateCode = System.Text.RegularExpressions.Regex.Replace(templateName.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
    }

    if (templateCode.Length > 80) templateCode = templateCode[..80];

    var taskCategory = ReadString("taskCategory", "project_task");
    var billingClassification = ReadString("billingClassification", "billable");
    var utilizationClassification = ReadString("utilizationClassification", billingClassification == "billable" ? "billable_utilization" : "non_billable_utilization");

    if (!new[] { "open_task", "project_task", "service_request_task", "non_project_task" }.Contains(taskCategory))
    {
        taskCategory = "project_task";
    }

    if (!new[] { "billable", "non_billable" }.Contains(billingClassification))
    {
        billingClassification = "billable";
    }

    if (!new[] { "billable_utilization", "non_billable_utilization", "non_billable_non_utilization" }.Contains(utilizationClassification))
    {
        utilizationClassification = billingClassification == "billable" ? "billable_utilization" : "non_billable_utilization";
    }

    var utilizationBucket = utilizationClassification switch
    {
        "billable_utilization" => "billable",
        "non_billable_utilization" => "non_billable",
        _ => "excluded"
    };

    await using var command = new NpgsqlCommand("""
        INSERT INTO work_task_templates (
            template_code,
            template_name,
            template_description,
            task_category,
            billing_classification,
            utilization_classification,
            utilization_bucket,
            default_billable,
            default_requires_approval,
            created_by_user_id,
            updated_by_user_id
        )
        VALUES (
            @template_code,
            @template_name,
            @template_description,
            @task_category,
            @billing_classification,
            @utilization_classification,
            @utilization_bucket,
            @default_billable,
            TRUE,
            @user_id,
            @user_id
        )
        ON CONFLICT (template_code) DO UPDATE
        SET template_name = EXCLUDED.template_name,
            template_description = EXCLUDED.template_description,
            task_category = EXCLUDED.task_category,
            billing_classification = EXCLUDED.billing_classification,
            utilization_classification = EXCLUDED.utilization_classification,
            utilization_bucket = EXCLUDED.utilization_bucket,
            default_billable = EXCLUDED.default_billable,
            updated_by_user_id = EXCLUDED.updated_by_user_id,
            updated_at = NOW()
        RETURNING work_task_template_id;
        """, connection);

    command.Parameters.AddWithValue("template_code", templateCode);
    command.Parameters.AddWithValue("template_name", templateName);
    command.Parameters.AddWithValue("template_description", ReadString("templateDescription"));
    command.Parameters.AddWithValue("task_category", taskCategory);
    command.Parameters.AddWithValue("billing_classification", billingClassification);
    command.Parameters.AddWithValue("utilization_classification", utilizationClassification);
    command.Parameters.AddWithValue("utilization_bucket", utilizationBucket);
    command.Parameters.AddWithValue("default_billable", billingClassification == "billable");
    command.Parameters.AddWithValue("user_id", sessionUserId.Value);

    var templateId = (Guid)(await command.ExecuteScalarAsync() ?? Guid.Empty);

    return Results.Ok(new
    {
        status = "work_task_template_saved",
        templateId,
        templateCode,
        templateName,
        taskCategory,
        billingClassification,
        utilizationClassification,
        utilizationBucket
    });
});

app.MapPost("/api/work-tasks/project-tasks", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    using var document = await System.Text.Json.JsonDocument.ParseAsync(httpContext.Request.Body);
    var root = document.RootElement;

    string ReadString(string name, string fallback = "")
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return fallback;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    Guid? ReadGuid(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
        return Guid.TryParse(value.GetString(), out var guid) ? guid : null;
    }

    var projectId = ReadGuid("projectId");
    var taskName = ReadString("taskName");

    if (projectId is null || string.IsNullOrWhiteSpace(taskName))
    {
        return Results.Json(new { status = "validation_error", message = "Project and task name are required." }, statusCode: StatusCodes.Status400BadRequest);
    }

    var taskCategory = ReadString("taskCategory", "project_task");
    var billingClassification = ReadString("billingClassification", "billable");
    var utilizationClassification = ReadString("utilizationClassification", billingClassification == "billable" ? "billable_utilization" : "non_billable_utilization");

    if (!new[] { "open_task", "project_task", "service_request_task", "non_project_task" }.Contains(taskCategory))
    {
        taskCategory = "project_task";
    }

    if (!new[] { "billable", "non_billable" }.Contains(billingClassification))
    {
        billingClassification = "billable";
    }

    if (!new[] { "billable_utilization", "non_billable_utilization", "non_billable_non_utilization" }.Contains(utilizationClassification))
    {
        utilizationClassification = billingClassification == "billable" ? "billable_utilization" : "non_billable_utilization";
    }

    var utilizationBucket = utilizationClassification switch
    {
        "billable_utilization" => "billable",
        "non_billable_utilization" => "non_billable",
        _ => "excluded"
    };

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    /* 053D_RESTRICT_PROJECT_TASK_CREATION_TO_PTC_START */
    var canManageProjectTaskCreation = false;

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT COALESCE(BOOL_OR(
            r.role_code IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
            OR p.permission_code IN ('MANAGE_WORK_TASK_BUILDER', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
        ), FALSE) AS can_manage_project_task_creation
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        var result = await accessCommand.ExecuteScalarAsync();
        canManageProjectTaskCreation = result is bool value && value;
    }

    if (!canManageProjectTaskCreation)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Project task creation is restricted to Project Team Coordinators and Administrators. Project Managers may assign engineers only to existing PTC-created tasks."
        }, statusCode: StatusCodes.Status403Forbidden);
    }
    /* 053D_RESTRICT_PROJECT_TASK_CREATION_TO_PTC_END */

    await using (var scopeCommand = new NpgsqlCommand("""
        SELECT COUNT(*)
        FROM projects
        WHERE project_id = @project_id
          AND (@can_view_all = TRUE OR project_manager_user_id = @user_id);
        """, connection))
    {
        scopeCommand.Parameters.AddWithValue("project_id", projectId.Value);
        scopeCommand.Parameters.AddWithValue("can_view_all", canManageProjectTaskCreation);
        scopeCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        var count = Convert.ToInt32(await scopeCommand.ExecuteScalarAsync() ?? 0);
        if (count == 0)
        {
            return Results.Json(new { status = "access_denied", message = "The selected project is not within your work task assignment scope." }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    var taskCode = ReadString("taskCode");
    if (string.IsNullOrWhiteSpace(taskCode))
    {
        var baseCode = System.Text.RegularExpressions.Regex.Replace(taskName.ToUpperInvariant(), "[^A-Z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(baseCode)) baseCode = "WORK-TASK";
        if (baseCode.Length > 40) baseCode = baseCode[..40].Trim('-');
        taskCode = $"{baseCode}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    }

    if (taskCode.Length > 80) taskCode = taskCode[..80];

    await using var command = new NpgsqlCommand("""
        INSERT INTO project_tasks (
            project_id,
            task_code,
            task_name,
            task_description,
            billable,
            utilization_bucket,
            utilization_requires_approval,
            work_task_category,
            billing_classification,
            utilization_classification,
            service_request_number,
            work_task_notes,
            is_active
        )
        VALUES (
            @project_id,
            @task_code,
            @task_name,
            @task_description,
            @billable,
            @utilization_bucket,
            TRUE,
            @work_task_category,
            @billing_classification,
            @utilization_classification,
            @service_request_number,
            @work_task_notes,
            TRUE
        )
        RETURNING task_id;
        """, connection);

    command.Parameters.AddWithValue("project_id", projectId.Value);
    command.Parameters.AddWithValue("task_code", taskCode);
    command.Parameters.AddWithValue("task_name", taskName);
    command.Parameters.AddWithValue("task_description", ReadString("taskDescription"));
    command.Parameters.AddWithValue("billable", billingClassification == "billable");
    command.Parameters.AddWithValue("utilization_bucket", utilizationBucket);
    command.Parameters.AddWithValue("work_task_category", taskCategory);
    command.Parameters.AddWithValue("billing_classification", billingClassification);
    command.Parameters.AddWithValue("utilization_classification", utilizationClassification);
    command.Parameters.AddWithValue("service_request_number", ReadString("serviceRequestNumber"));
    command.Parameters.AddWithValue("work_task_notes", ReadString("workTaskNotes"));

    var taskId = (Guid)(await command.ExecuteScalarAsync() ?? Guid.Empty);

    return Results.Ok(new
    {
        status = "project_work_task_created",
        projectId,
        taskId,
        taskCode,
        taskName,
        taskCategory,
        billingClassification,
        utilizationClassification,
        utilizationBucket
    });
});

app.MapPost("/api/work-tasks/assignments", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    using var document = await System.Text.Json.JsonDocument.ParseAsync(httpContext.Request.Body);
    var root = document.RootElement;

    string ReadString(string name, string fallback = "")
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return fallback;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    Guid? ReadGuid(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
        return Guid.TryParse(value.GetString(), out var guid) ? guid : null;
    }

    decimal ReadDecimal(string name, decimal fallback)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return fallback;
        return value.TryGetDecimal(out var number) ? number : fallback;
    }

    DateOnly ReadDate(string name, DateOnly fallback)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return fallback;
        return DateOnly.TryParse(value.GetString(), out var date) ? date : fallback;
    }

    DateOnly? ReadNullableDate(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
        return DateOnly.TryParse(value.GetString(), out var date) ? date : null;
    }

    var projectId = ReadGuid("projectId");
    var taskId = ReadGuid("taskId");
    var engineerUserId = ReadGuid("engineerUserId");
    var assignedHours = ReadDecimal("assignedHours", 0m);
    var allocationPercent = ReadDecimal("allocationPercent", 0m);
    var effectiveStartDate = ReadDate("effectiveStartDate", DateOnly.FromDateTime(DateTime.UtcNow.Date));
    var effectiveEndDate = ReadNullableDate("effectiveEndDate");

    if (projectId is null || taskId is null || engineerUserId is null)
    {
        return Results.Json(new { status = "validation_error", message = "Project, task, and engineer are required." }, statusCode: StatusCodes.Status400BadRequest);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var canViewAll = false;
    var isProjectManager = false;

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            BOOL_OR(
                r.role_code IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
                OR p.permission_code IN ('SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
            ) AS can_view_all,
            BOOL_OR(
                r.role_code IN ('PROJECT_MANAGEMENT', 'PROJECT_MANAGER')
                OR p.permission_code = 'ASSIGN_WORK_TASKS'
            ) AS is_project_manager
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        await using var reader = await accessCommand.ExecuteReaderAsync();
        await reader.ReadAsync();
        canViewAll = !reader.IsDBNull(0) && reader.GetBoolean(0);
        isProjectManager = !reader.IsDBNull(1) && reader.GetBoolean(1);
    }

    if (!canViewAll && !isProjectManager)
    {
        return Results.Json(new { status = "access_denied", message = "Only Administrators, Project Team Coordinators, and assigned Project Managers can assign work tasks." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using (var scopeCommand = new NpgsqlCommand("""
        SELECT COUNT(*)
        FROM projects p
        JOIN project_tasks pt
            ON pt.project_id = p.project_id
           AND pt.task_id = @task_id
        WHERE p.project_id = @project_id
          AND (@can_view_all = TRUE OR p.project_manager_user_id = @user_id);
        """, connection))
    {
        scopeCommand.Parameters.AddWithValue("project_id", projectId.Value);
        scopeCommand.Parameters.AddWithValue("task_id", taskId.Value);
        scopeCommand.Parameters.AddWithValue("can_view_all", canViewAll);
        scopeCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        var count = Convert.ToInt32(await scopeCommand.ExecuteScalarAsync() ?? 0);
        if (count == 0)
        {
            return Results.Json(new { status = "access_denied", message = "The selected project/task is not within your assignment scope." }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    /* 053E_ASSIGNMENT_ROUTE_BLOCK_CLOSED_PROJECTS_START */
    await using (var projectStateCommand = new NpgsqlCommand("""
        SELECT
            COALESCE(p.status, '') AS project_status,
            COALESCE(pt.is_active, FALSE) AS task_is_active
        FROM projects p
        JOIN project_tasks pt
            ON pt.project_id = p.project_id
           AND pt.task_id = @task_id
        WHERE p.project_id = @project_id;
        """, connection))
    {
        projectStateCommand.Parameters.AddWithValue("project_id", projectId.Value);
        projectStateCommand.Parameters.AddWithValue("task_id", taskId.Value);

        await using var reader = await projectStateCommand.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return Results.Json(new
            {
                status = "validation_error",
                message = "The selected project/task could not be found."
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        var projectStatus = reader.IsDBNull(0) ? "" : reader.GetString(0);
        var taskIsActive = !reader.IsDBNull(1) && reader.GetBoolean(1);
        var projectStatusLower = projectStatus.Trim().ToLowerInvariant();

        if (projectStatusLower is "closed" or "complete" or "completed" or "done" or "archived" or "cancelled" or "canceled")
        {
            return Results.Json(new
            {
                status = "project_closed",
                message = "This project is closed or archived. Future engineer assignments cannot be added to closed projects."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        if (!taskIsActive)
        {
            return Results.Json(new
            {
                status = "task_inactive",
                message = "The selected task is inactive and cannot receive new engineer assignments."
            }, statusCode: StatusCodes.Status409Conflict);
        }
    }
    /* 053E_ASSIGNMENT_ROUTE_BLOCK_CLOSED_PROJECTS_END */

    await using (var engineerCommand = new NpgsqlCommand("""
        SELECT COUNT(*)
        FROM app_users u
        JOIN app_user_role_assignments ura
            ON ura.user_id = u.user_id
           AND ura.is_active = TRUE
        JOIN app_roles r
            ON r.app_role_id = ura.app_role_id
           AND r.is_active = TRUE
        WHERE u.user_id = @engineer_user_id
          AND u.is_active = TRUE
          AND r.role_code IN ('ENGINEERING', 'ENGINEER');
        """, connection))
    {
        engineerCommand.Parameters.AddWithValue("engineer_user_id", engineerUserId.Value);
        var count = Convert.ToInt32(await engineerCommand.ExecuteScalarAsync() ?? 0);
        if (count == 0)
        {
            return Results.Json(new { status = "validation_error", message = "Selected user must be an active Engineer." }, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /* 053H_ASSIGNMENT_COST_GUARDRAIL_START */
    var defaultEngineeringHourlyCost = ProjectPulse053HGetDefaultEngineeringHourlyCost();
    var proposedAssignmentHours = Math.Max(assignedHours, 0m);
    var proposedAssignmentCost = proposedAssignmentHours * defaultEngineeringHourlyCost;
    decimal plannedEngineeringCost;
    decimal plannedTotalProjectCost;
    decimal existingAssignedHours;

    await using (var costGuardrailCommand = new NpgsqlCommand("""
        SELECT
            COALESCE(p.planned_engineering_cost, 0)::numeric AS planned_engineering_cost,
            COALESCE(p.planned_total_project_cost, 0)::numeric AS planned_total_project_cost,
            COALESCE(SUM(COALESCE(pa.assigned_hours, 0)), 0)::numeric AS existing_assigned_hours
        FROM projects p
        LEFT JOIN project_assignments pa
            ON pa.project_id = p.project_id
        WHERE p.project_id = @project_id
        GROUP BY p.project_id, p.planned_engineering_cost, p.planned_total_project_cost;
        """, connection))
    {
        costGuardrailCommand.Parameters.AddWithValue("project_id", projectId.Value);

        await using var costReader = await costGuardrailCommand.ExecuteReaderAsync();

        if (!await costReader.ReadAsync())
        {
            return Results.Json(new
            {
                status = "validation_error",
                message = "The selected project could not be found for assignment cost validation."
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        plannedEngineeringCost = costReader.GetDecimal(0);
        plannedTotalProjectCost = costReader.GetDecimal(1);
        existingAssignedHours = costReader.GetDecimal(2);
    }

    var assignmentBudget = plannedEngineeringCost > 0m ? plannedEngineeringCost : plannedTotalProjectCost;
    var existingEstimatedAssignmentCost = existingAssignedHours * defaultEngineeringHourlyCost;
    var projectedEstimatedAssignmentCost = existingEstimatedAssignmentCost + proposedAssignmentCost;
    var remainingAssignmentBudget = assignmentBudget - existingEstimatedAssignmentCost;

    if (proposedAssignmentCost > 0m && assignmentBudget <= 0m)
    {
        return Results.Json(new
        {
            status = "assignment_cost_plan_missing",
            message = "This project does not have a planned engineering or total project cost loaded. Add the cost allocation before assigning additional engineer hours.",
            projectId,
            plannedEngineeringCost,
            plannedTotalProjectCost,
            existingAssignedHours,
            defaultEngineeringHourlyCost,
            proposedAssignmentHours,
            proposedAssignmentCost
        }, statusCode: StatusCodes.Status409Conflict);
    }

    if (proposedAssignmentCost > 0m && projectedEstimatedAssignmentCost > assignmentBudget)
    {
        return Results.Json(new
        {
            status = "assignment_cost_exceeds_budget",
            message = "This assignment would exceed the project cost allocation. Reduce assigned hours or update the project allocation before assigning the engineer.",
            projectId,
            assignmentBudget,
            plannedEngineeringCost,
            plannedTotalProjectCost,
            existingAssignedHours,
            defaultEngineeringHourlyCost,
            existingEstimatedAssignmentCost,
            proposedAssignmentHours,
            proposedAssignmentCost,
            projectedEstimatedAssignmentCost,
            remainingAssignmentBudget
        }, statusCode: StatusCodes.Status409Conflict);
    }
    /* 053H_ASSIGNMENT_COST_GUARDRAIL_END */

    await using var command = new NpgsqlCommand("""
        INSERT INTO project_assignments (
            project_id,
            task_id,
            user_id,
            assigned_by_user_id,
            effective_start_date,
            effective_end_date,
            allocation_percent,
            assigned_hours,
            assignment_source,
            assignment_notes,
            updated_at
        )
        VALUES (
            @project_id,
            @task_id,
            @engineer_user_id,
            @assigned_by_user_id,
            @effective_start_date,
            @effective_end_date,
            NULLIF(@allocation_percent, 0),
            @assigned_hours,
            'work_task_builder',
            @assignment_notes,
            NOW()
        )
        RETURNING project_assignment_id;
        """, connection);

    command.Parameters.AddWithValue("project_id", projectId.Value);
    command.Parameters.AddWithValue("task_id", taskId.Value);
    command.Parameters.AddWithValue("engineer_user_id", engineerUserId.Value);
    command.Parameters.AddWithValue("assigned_by_user_id", sessionUserId.Value);
    command.Parameters.AddWithValue("effective_start_date", effectiveStartDate);
    command.Parameters.AddWithValue("effective_end_date", effectiveEndDate.HasValue ? effectiveEndDate.Value : DBNull.Value);
    command.Parameters.AddWithValue("allocation_percent", allocationPercent);
    command.Parameters.AddWithValue("assigned_hours", assignedHours);
    command.Parameters.AddWithValue("assignment_notes", ReadString("assignmentNotes"));

    var assignmentId = (Guid)(await command.ExecuteScalarAsync() ?? Guid.Empty);

    return Results.Ok(new
    {
        status = "work_task_assigned",
        assignmentId,
        projectId,
        taskId,
        engineerUserId,
        assignedHours,
        allocationPercent,
        effectiveStartDate,
        effectiveEndDate,
        costGuardrail = new
        {
            assignmentBudget,
            plannedEngineeringCost,
            plannedTotalProjectCost,
            existingAssignedHours,
            defaultEngineeringHourlyCost,
            existingEstimatedAssignmentCost,
            proposedAssignmentHours,
            proposedAssignmentCost,
            projectedEstimatedAssignmentCost,
            remainingAssignmentBudget = assignmentBudget - projectedEstimatedAssignmentCost
        }
    });
});

app.MapGet("/api/assignments/available-tasks", async (DateOnly? weekStart, HttpContext httpContext) =>
{
    var userId = GetProjectPulseSessionUserId(httpContext);

    if (userId is null)
    {
        return Results.Json(new
        {
            status = "session_required",
            message = "A valid ProjectPulse session is required."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var start = weekStart ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);

    while (start.DayOfWeek != DayOfWeek.Sunday)
    {
        start = start.AddDays(-1);
    }

    var end = start.AddDays(6);

    var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
    var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
    var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
    var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
    var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

    var connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = host,
        Port = int.TryParse(port, out var parsedPort) ? parsedPort : 5432,
        Database = database,
        Username = username,
        Password = password,
        IncludeErrorDetail = false,
        Pooling = true,
        MinPoolSize = 0,
        MaxPoolSize = 5
    }.ConnectionString;

    var tasks = new List<object>();

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();

    const string sql = """
        WITH used_time AS (
            SELECT
                user_id,
                project_id,
                task_id,
                SUM(hours)::numeric AS used_hours
            FROM time_entries
            WHERE user_id = @user_id
              AND project_id IS NOT NULL
              AND task_id IS NOT NULL
              AND status NOT IN ('voided', 'rejected')
            GROUP BY user_id, project_id, task_id
        ),
        resource_alloc AS (
            SELECT
                err.project_id,
                erra.user_id,
                SUM(erra.allocated_hours)::numeric
                    / NULLIF(COUNT(DISTINCT pa2.project_assignment_id), 0)::numeric AS allocated_hours_per_task
            FROM engineering_resource_requests err
            JOIN engineering_resource_request_assignments erra
                ON erra.engineering_resource_request_id = err.engineering_resource_request_id
            LEFT JOIN project_assignments pa2
                ON pa2.project_id = err.project_id
               AND pa2.user_id = erra.user_id
            WHERE err.project_id IS NOT NULL
            GROUP BY err.project_id, erra.user_id
        )
        SELECT
            pa.project_assignment_id AS assignment_id,
            p.project_id AS project_id,
            pt.task_id AS task_id,
            p.project_code AS project_code,
            p.project_name AS project_name,
            COALESCE(c.client_name, 'No customer assigned') AS client_name,
            pt.task_code AS task_code,
            pt.task_name AS task_name,
            pt.task_description AS task_description,
            COALESCE(
                NULLIF(to_jsonb(pt)->>'work_task_category', ''),
                NULLIF(to_jsonb(pt)->>'work_type', ''),
                'project_task'
            ) AS work_task_category,
            COALESCE(
                NULLIF(to_jsonb(pt)->>'service_request_number', ''),
                CASE WHEN p.project_code ~* '^SR-' THEN p.project_code ELSE '' END
            ) AS service_request_number,
            pt.billable AS billable,
            COALESCE(pt.utilization_bucket, CASE WHEN pt.billable THEN 'billable' ELSE 'non_billable' END) AS utilization_bucket,
            COALESCE(pm.display_name, 'No PM assigned') AS project_manager_name,
            COALESCE(NULLIF(p.work_type, ''), 'Project') AS work_type,
            CASE
                WHEN p.project_code ~* '^(SR|PRES|INT)-'
                  OR regexp_replace(lower(COALESCE(p.work_type, '')), '[^a-z0-9]+', '', 'g') IN (
                      'servicerequest', 'sr', 'presales', 'presale', 'pres',
                      'internal', 'internalproject', 'internaltask'
                  )
                  OR NULLIF(to_jsonb(pt)->>'service_request_number', '') IS NOT NULL
                    THEN 'requests'
                ELSE 'regular'
            END AS time_entry_section,
            COALESCE(NULLIF(pa.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric AS assigned_hours,
            COALESCE(used_time.used_hours, 0)::numeric AS used_hours,
            GREATEST(
                COALESCE(NULLIF(pa.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric
                - COALESCE(used_time.used_hours, 0)::numeric,
                0
            )::numeric AS remaining_hours,
            (
                COALESCE(used_time.used_hours, 0)::numeric >
                COALESCE(NULLIF(pa.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric
                AND COALESCE(NULLIF(pa.assigned_hours, 0), resource_alloc.allocated_hours_per_task, 0)::numeric > 0
            ) AS is_over_allocated
        FROM project_assignments pa
        JOIN projects p ON p.project_id = pa.project_id
        JOIN project_tasks pt ON pt.task_id = pa.task_id
        LEFT JOIN clients c ON c.client_id = p.client_id
        LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
        LEFT JOIN used_time
            ON used_time.user_id = pa.user_id
           AND used_time.project_id = pa.project_id
           AND used_time.task_id = pa.task_id
        LEFT JOIN resource_alloc
            ON resource_alloc.project_id = pa.project_id
           AND resource_alloc.user_id = pa.user_id
        WHERE pa.user_id = @user_id
          AND pa.effective_start_date <= @week_end
          AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start)
          /* 001A_ENGINEER_CLOSEOUT_BILLING_LOCK */
          AND COALESCE(NULLIF(to_jsonb(pa)->>'module001a_closeout_status', ''), 'active') = 'active'
          AND pt.is_active = TRUE
          /* 053G_HIDE_CLOSED_PROJECTS_FROM_AVAILABLE_TASKS */
          AND lower(COALESCE(p.status, 'active')) NOT IN ('closed', 'complete', 'completed', 'done', 'cancelled', 'canceled', 'archived')
        ORDER BY c.client_name, p.project_code, pt.task_code, pt.task_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId.Value);
    command.Parameters.AddWithValue("week_start", start);
    command.Parameters.AddWithValue("week_end", end);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        int O(string name) => reader.GetOrdinal(name);
        var workTaskCategory = reader.GetString(O("work_task_category"));
        var serviceRequestNumber = reader.GetString(O("service_request_number"));
        var projectCode = reader.GetString(O("project_code"));
        var timeEntrySection = reader.GetString(O("time_entry_section"));
        var isRequestFamily = string.Equals(
            timeEntrySection,
            "requests",
            StringComparison.OrdinalIgnoreCase);

        tasks.Add(new
        {
            assignmentId = reader.GetGuid(O("assignment_id")),
            projectId = reader.GetGuid(O("project_id")),
            taskId = reader.GetGuid(O("task_id")),
            projectCode,
            projectName = reader.GetString(O("project_name")),
            clientName = reader.GetString(O("client_name")),
            taskCode = reader.GetString(O("task_code")),
            taskName = reader.GetString(O("task_name")),
            taskDescription = reader.IsDBNull(O("task_description")) ? null : reader.GetString(O("task_description")),
            rowType = isRequestFamily ? "service_request" : "projectTask",
            workTaskCategory,
            requestNumber = isRequestFamily ? projectCode : string.Empty,
            serviceRequestNumber,
            billable = reader.GetBoolean(O("billable")),
            utilizationBucket = reader.GetString(O("utilization_bucket")),
            projectManagerName = reader.GetString(O("project_manager_name")),
            workType = reader.GetString(O("work_type")),
            timeEntrySection,
            assignedHours = reader.GetDecimal(O("assigned_hours")),
            usedHours = reader.GetDecimal(O("used_hours")),
            remainingHours = reader.GetDecimal(O("remaining_hours")),
            isOverAllocated = reader.GetBoolean(O("is_over_allocated"))
        });
    }

    return Results.Ok(new
    {
        weekStart = start,
        weekEnd = end,
        count = tasks.Count,
        authoritativeSource = "project_assignments",
        activityClassification = "durable_project_code_and_work_type",
        tasks
    });
});


app.MapGet("/api/non-project-time-categories", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var categories = await LoadNonProjectCategoriesAsync(connection);

    return Results.Ok(new
    {
        count = categories.Count,
        categories
    });
});

app.MapGet("/api/work-location-groups", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var groups = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT work_location_group_id, group_code, group_name, group_description, is_active, display_order
        FROM work_location_groups
        WHERE is_active = TRUE
        ORDER BY display_order, group_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        groups.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            description = reader.IsDBNull(3) ? null : reader.GetString(3),
            isActive = reader.GetBoolean(4),
            displayOrder = reader.GetInt32(5)
        });
    }

    return Results.Ok(new
    {
        count = groups.Count,
        groups
    });
});

app.MapGet("/api/work-locations", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var locations = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            wl.work_location_id,
            wl.location_code,
            wl.location_name,
            wl.city,
            wl.state_region,
            wl.country,
            wl.time_zone,
            wlg.work_location_group_id,
            wlg.group_code,
            wlg.group_name,
            wl.display_order
        FROM work_locations wl
        LEFT JOIN work_location_groups wlg ON wlg.work_location_group_id = wl.work_location_group_id
        WHERE wl.is_active = TRUE
        ORDER BY wl.display_order, wl.location_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        locations.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            city = reader.IsDBNull(3) ? null : reader.GetString(3),
            stateRegion = reader.IsDBNull(4) ? null : reader.GetString(4),
            country = reader.GetString(5),
            timeZone = reader.IsDBNull(6) ? null : reader.GetString(6),
            groupId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
            groupCode = reader.IsDBNull(8) ? null : reader.GetString(8),
            groupName = reader.IsDBNull(9) ? null : reader.GetString(9),
            displayOrder = reader.GetInt32(10)
        });
    }

    return Results.Ok(new
    {
        count = locations.Count,
        locations
    });
});

app.MapGet("/api/utilization/policies", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var policies = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            utilization_policy_id,
            policy_name,
            period_type,
            standard_period_hours,
            default_target_percent,
            presales_training_requires_approval,
            is_active
        FROM utilization_policies
        ORDER BY is_active DESC, policy_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        policies.Add(new
        {
            id = reader.GetGuid(0),
            name = reader.GetString(1),
            periodType = reader.GetString(2),
            standardPeriodHours = reader.GetDecimal(3),
            defaultTargetPercent = reader.GetDecimal(4),
            presalesTrainingRequiresApproval = reader.GetBoolean(5),
            isActive = reader.GetBoolean(6)
        });
    }

    return Results.Ok(new
    {
        count = policies.Count,
        policies
    });
});

app.MapGet("/api/utilization/targets", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var targets = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            up.policy_name,
            upt.target_percent,
            upt.target_hours,
            upt.display_order
        FROM utilization_policy_targets upt
        INNER JOIN utilization_policies up ON up.utilization_policy_id = upt.utilization_policy_id
        WHERE up.is_active = TRUE
        ORDER BY upt.display_order, upt.target_percent;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        targets.Add(new
        {
            policyName = reader.GetString(0),
            targetPercent = reader.GetDecimal(1),
            targetHours = reader.GetDecimal(2),
            displayOrder = reader.GetInt32(3)
        });
    }

    return Results.Ok(new
    {
        count = targets.Count,
        targets
    });
});

app.MapGet("/api/timesheets/week", async (DateOnly? weekStart, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var userId = sessionUserId.Value;
    /* 051B_HOLIDAY_AUTO_SUBMIT_ON_WEEK_LOAD */
    await ProjectPulse051BAutoSubmitEligibleHolidaysForWeekAsync(connection, userId, start);

    var payload = await BuildTimesheetWeekPayloadAsync(connection, userId, start);

    return Results.Ok(payload);
});

app.MapPost("/api/timesheets/week/draft", async (TimesheetSaveRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var validationErrors = ValidateTimesheetRequest(request).ToList();
    validationErrors.AddRange(ProjectPulseTimeEntryDescriptionValidation.GetMissingDescriptionErrors(request.Entries));
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            errors = validationErrors
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    /* FIX-20260717-001_TIMESHEET_SAVE_AUTHORIZATION
       Draft saves are authorized by the authenticated user's own session and immutable-status checks.
       User-administration permission is unrelated to entering or saving personal time. */

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var userId = sessionUserId.Value;
        var start = GetSundayForDate(request.WeekStart);
        var existingStatus = await GetTimesheetStatusAsync(connection, transaction, userId, start);

        /* 051B_WEEK_DRAFT_IMMUTABLE_STATUS_GUARD */
        if (ProjectPulse051BIsImmutableTimesheetWeekStatus(existingStatus))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Submitted, approved, accounting-ready, reconciled, or locked timesheets cannot be edited by the engineer."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var timesheetId = await UpsertDraftShellForEditableSaveAsync(connection, transaction, userId, start);
        await ReplaceEditableTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "draft");
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_draft_saved", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, start);

        return Results.Ok(new
        {
            status = "draft_saved",
            timesheetId,
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to save draft timesheet",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/timesheets/week/submit", async (TimesheetSaveRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var validationErrors = ValidateTimesheetRequest(request).ToList();
    validationErrors.AddRange(ProjectPulseTimeEntryDescriptionValidation.GetMissingDescriptionErrors(request.Entries));
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            errors = validationErrors
        });
    }

    var positiveEntryCount = request.Entries.Count(entry => entry.Hours > 0);
    if (positiveEntryCount == 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            errors = new[] { "At least one time entry with hours greater than zero is required before submission." }
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var userId = sessionUserId.Value;
        var start = GetSundayForDate(request.WeekStart);
        var existingStatus = await GetTimesheetStatusAsync(connection, transaction, userId, start);

        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_submittable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be submitted."
            });
        }

        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, start);
        await ReplaceTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "submitted");
        await MarkTimesheetSubmittedAsync(connection, transaction, timesheetId);
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_submitted", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, start);

        return Results.Ok(new
        {
            status = "submitted_for_manager_approval",
            timesheetId,
            submittedEntryCount = positiveEntryCount,
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to submit timesheet",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/timesheets/day/submit", async (TimesheetDaySubmitRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var validationErrors = ValidateDaySubmitRequest(request).ToList();
    validationErrors.AddRange(ProjectPulseTimeEntryDescriptionValidation.GetMissingDescriptionErrors(request.Entries));
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            errors = validationErrors
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var userId = sessionUserId.Value;
        var weekStart = GetSundayForDate(request.WeekStart);
        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, weekStart);
        var dayState = await GetTimesheetDayStatusAsync(connection, transaction, timesheetId, request.WorkDate);

        /* 051B_DAY_SUBMIT_IMMUTABLE_STATUS_GUARD */
        if (ProjectPulse051BIsImmutableTimesheetDayStatus(dayState.Status))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "day_not_editable",
                currentStatus = dayState.Status,
                message = dayState.Status == "submitted"
                    ? "This day is already submitted. Use Unlock within two hours, or contact your manager after two hours."
                    : "This day has already moved into approval, accounting, reconciliation, or lock status and cannot be rewritten by the engineer."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        await ReplaceDayTimeEntriesAsync(connection, transaction, timesheetId, userId, request.WorkDate, request.Entries, "submitted");
        await MarkTimesheetDaySubmittedAsync(connection, transaction, timesheetId, userId, request.WorkDate);
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_day_submitted", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, weekStart);

        return Results.Ok(new
        {
            status = "day_submitted",
            timesheetId,
            workDate = request.WorkDate,
            message = $"{request.WorkDate} submitted successfully.",
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to submit timesheet day",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/timesheets/day/unlock", async (TimesheetDayUnlockRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var userId = sessionUserId.Value;
        var weekStart = GetSundayForDate(request.WeekStart);
        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, weekStart);
        var dayState = await GetTimesheetDayStatusAsync(connection, transaction, timesheetId, request.WorkDate);

        if (!CanEngineerUnlockDay(dayState.Status, dayState.SubmittedAt))
        {
            return Results.Conflict(new
            {
                status = "day_unlock_denied",
                currentStatus = dayState.Status,
                message = GetDayUnlockMessage(dayState.Status, dayState.SubmittedAt)
            });
        }

        await UnlockTimesheetDayAsync(connection, transaction, timesheetId, userId, request.WorkDate);
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_day_engineer_unlocked", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, weekStart);

        return Results.Ok(new
        {
            status = "day_unlocked",
            timesheetId,
            workDate = request.WorkDate,
            message = "Day unlocked. Make your correction, then submit the day again.",
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to unlock timesheet day",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});


/* MODULE_002_APPROVAL_CENTER_ENDPOINTS_START */
app.MapApprovalCenterEndpoints();
/* MODULE_002_APPROVAL_CENTER_ENDPOINTS_END */

app.MapPost("/api/timesheets/ai-description-suggestions", async (
    ProjectPulseAiTimeEntrySuggestionRequest request,
    HttpContext httpContext,
    ProjectPulseAiTimesheetContextResolver contextResolver,
    ProjectPulseAiTimeEntrySuggestionService aiService,
    CancellationToken cancellationToken) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (request.WorkDate == default)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Work date is required." });
    }

    var roughNote = request.CurrentDescription ?? string.Empty;
    if (roughNote.Length > 4_000)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "The rough work note cannot exceed 4,000 characters. Keep only the factual details needed for this time entry."
        });
    }

    var roughNoteCharacters = roughNote.Count(character => !char.IsWhiteSpace(character));
    var roughNoteFactualCharacters = roughNote.Count(char.IsLetterOrDigit);
    if (roughNoteCharacters < 12 || roughNoteFactualCharacters < 8)
    {
        return Results.BadRequest(new
        {
            status = "more_detail_required",
            message = "Add a brief factual note about the work performed before generating a customer-facing description."
        });
    }

    var contextResolution = await contextResolver.ResolveAsync(
        sessionUserId.Value,
        request,
        cancellationToken);
    if (!contextResolution.Succeeded || contextResolution.Request is null)
    {
        return Results.Json(
            new
            {
                status = contextResolution.Status,
                message = contextResolution.Message
            },
            statusCode: contextResolution.StatusCode);
    }

    var result = await aiService.GenerateAsync(contextResolution.Request, cancellationToken);

    return Results.Ok(new
    {
        status = string.IsNullOrWhiteSpace(result.Suggestion)
            ? "ai_suggestion_refused"
            : "ai_suggestion_generated",
        suggestion = result.Suggestion,
        provider = result.Provider,
        warning = result.Warning,
        targetDecisions = result.TargetDecisions ?? [],
        contextSource = contextResolution.ContextSource,
        message = result.Provider switch
        {
            ProjectPulseAiProviders.Claude when !string.IsNullOrWhiteSpace(result.Suggestion) =>
                "Claude generated a time-entry description suggestion.",
            ProjectPulseAiProviders.OpenAi when !string.IsNullOrWhiteSpace(result.Suggestion) =>
                "OpenAI generated a time-entry description suggestion.",
            CelarAiCapabilityTargets.CelarAi when !string.IsNullOrWhiteSpace(result.Suggestion) =>
                "Celar AI generated a privately grounded time-entry description suggestion.",
            ProjectPulseAiProviders.Local =>
                "No configured AI target completed this request. No template was presented as an AI suggestion.",
            _ => "The selected provider declined this request under its safety controls."
        }
    });
});


app.MapGet("/api/assignments/open-tasks", async (DateOnly? weekStart, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(6);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var userId = sessionUserId.Value;
    var tasks = await LoadOpenAssignedProjectTasksAsync(connection, userId, start, end);

    return Results.Ok(new
    {
        weekStart = start,
        weekEnd = end,
        count = tasks.Count,
        tasks
    });
});


app.MapGet("/api/debug/time-entries", async (DateOnly? weekStart, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(6);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var userId = sessionUserId.Value;
    var rows = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            t.week_start_date,
            t.status AS timesheet_status,
            te.work_date,
            te.time_type,
            te.hours,
            te.status AS entry_status,
            COALESCE(npt.category_name, pt.task_name, 'Unknown') AS activity,
            p.project_code,
            p.project_name,
            te.description
        FROM timesheets t
        LEFT JOIN time_entries te ON te.timesheet_id = t.timesheet_id
        LEFT JOIN non_project_time_categories npt ON npt.non_project_time_category_id = te.non_project_time_category_id
        LEFT JOIN project_tasks pt ON pt.task_id = te.task_id
        LEFT JOIN projects p ON p.project_id = te.project_id
        WHERE t.user_id = @user_id
          AND t.week_start_date = @week_start
        ORDER BY te.work_date, te.time_type, activity;
        """, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start", start);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            weekStart = reader.GetFieldValue<DateOnly>(0),
            timesheetStatus = reader.GetString(1),
            workDate = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
            timeType = reader.IsDBNull(3) ? null : reader.GetString(3),
            hours = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4),
            entryStatus = reader.IsDBNull(5) ? null : reader.GetString(5),
            activity = reader.IsDBNull(6) ? null : reader.GetString(6),
            projectCode = reader.IsDBNull(7) ? null : reader.GetString(7),
            projectName = reader.IsDBNull(8) ? null : reader.GetString(8),
            description = reader.IsDBNull(9) ? null : reader.GetString(9)
        });
    }

    return Results.Ok(new { weekStart = start, weekEnd = end, count = rows.Count, rows });
});




// 019M-AR Project Intake to Work Task Builder Handoff Readiness
app.MapGet("/api/project-intake/work-task-handoff", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            COALESCE(p.permission_code, '') AS permission_code
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await accessCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));

            if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                permissions.Add(reader.GetString(1));
            }
        }
    }

    var canViewAll =
        (roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR"))
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canViewManaged =
        roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PM_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || roles.Contains("MANAGER")
        || roles.Contains("PROJECT_COORDINATOR")
        || permissions.Contains("VIEW_PROJECT_INTAKE")
        || permissions.Contains("VIEW_PROJECT_INTAKE_AGING")
        || permissions.Contains("VIEW_INTAKE_WORK_TASK_HANDOFF");

    var canManageProjectLinks =
        canViewAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PM_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || permissions.Contains("MANAGE_INTAKE_PROJECT_LINKS")
        || permissions.Contains("MANAGE_PROJECT_INTAKE")
        || permissions.Contains("MANAGE_ALL")
        || permissions.Contains("SYSTEM_ADMINISTRATION");

    if (!canViewAll && !canViewManaged)
    {
        return Results.Json(new
        {
            canViewIntakeWorkTaskHandoff = false,
            status = "access_denied",
            message = "Intake to Work Task handoff readiness is available to PTC, Administrators, Project Managers, PM Team Leads, and approved management roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var intakes = new List<object>();

    await using (var command = new NpgsqlCommand("""
        WITH direct_links AS (
            SELECT DISTINCT project_intake_request_id, project_id
            FROM project_intake_project_links
            WHERE project_intake_request_id IS NOT NULL
              AND project_id IS NOT NULL
              AND COALESCE(is_active, TRUE) = TRUE
              AND link_status = 'confirmed'

            UNION

            SELECT DISTINCT project_intake_request_id, project_id
            FROM engineering_resource_requests
            WHERE project_intake_request_id IS NOT NULL
              AND project_id IS NOT NULL

            UNION

            SELECT DISTINCT project_intake_request_id, project_id
            FROM project_intake_documents
            WHERE project_intake_request_id IS NOT NULL
              AND project_id IS NOT NULL
              AND COALESCE(is_active, TRUE) = TRUE
        ),
        candidate_links AS (
            SELECT DISTINCT
                pir.project_intake_request_id,
                p.project_id
            FROM project_intake_requests pir
            JOIN projects p
                ON (
                    (pir.client_id IS NOT NULL AND p.client_id = pir.client_id)
                    OR (
                        pir.assigned_pm_user_id IS NOT NULL
                        AND p.project_manager_user_id = pir.assigned_pm_user_id
                    )
                    OR (
                        NULLIF(TRIM(pir.request_title), '') IS NOT NULL
                        AND LOWER(p.project_name) = LOWER(pir.request_title)
                    )
                )
            WHERE NOT EXISTS (
                SELECT 1
                FROM direct_links dl
                WHERE dl.project_intake_request_id = pir.project_intake_request_id
                  AND dl.project_id = p.project_id
            )
        ),
        task_rollup AS (
            SELECT
                project_id,
                COUNT(DISTINCT task_id)::bigint AS task_count,
                COUNT(DISTINCT task_id) FILTER (WHERE COALESCE(is_active, TRUE) = TRUE)::bigint AS active_task_count
            FROM project_tasks
            GROUP BY project_id
        ),
        assignment_rollup AS (
            SELECT
                project_id,
                COUNT(DISTINCT project_assignment_id)::bigint AS assignment_count,
                COUNT(DISTINCT user_id)::bigint AS assigned_engineer_count,
                COALESCE(SUM(assigned_hours), 0)::numeric AS assigned_hours
            FROM project_assignments
            GROUP BY project_id
        ),
        time_rollup AS (
            SELECT
                project_id,
                COUNT(DISTINCT time_entry_id)::bigint AS time_entry_count,
                COALESCE(SUM(hours), 0)::numeric AS used_hours
            FROM time_entries
            WHERE project_id IS NOT NULL
              AND COALESCE(status, 'draft') NOT IN ('manager_declined', 'rejected', 'voided')
            GROUP BY project_id
        ),
        project_stats AS (
            SELECT
                p.project_id,
                p.project_code,
                p.project_name,
                p.status,
                COALESCE(tr.task_count, 0) AS task_count,
                COALESCE(tr.active_task_count, 0) AS active_task_count,
                COALESCE(ar.assignment_count, 0) AS assignment_count,
                COALESCE(ar.assigned_engineer_count, 0) AS assigned_engineer_count,
                COALESCE(ar.assigned_hours, 0) AS assigned_hours,
                COALESCE(tir.time_entry_count, 0) AS time_entry_count,
                COALESCE(tir.used_hours, 0) AS used_hours
            FROM projects p
            LEFT JOIN task_rollup tr
                ON tr.project_id = p.project_id
            LEFT JOIN assignment_rollup ar
                ON ar.project_id = p.project_id
            LEFT JOIN time_rollup tir
                ON tir.project_id = p.project_id
        ),
        direct_aggregate AS (
            SELECT
                dl.project_intake_request_id,
                COUNT(DISTINCT ps.project_id)::bigint AS project_count,
                STRING_AGG(DISTINCT ps.project_code || ' · ' || ps.project_name, '; ' ORDER BY ps.project_code || ' · ' || ps.project_name) AS project_labels,
                COALESCE(SUM(ps.task_count), 0)::bigint AS task_count,
                COALESCE(SUM(ps.active_task_count), 0)::bigint AS active_task_count,
                COALESCE(SUM(ps.assignment_count), 0)::bigint AS assignment_count,
                COALESCE(SUM(ps.assigned_engineer_count), 0)::bigint AS assigned_engineer_count,
                COALESCE(SUM(ps.assigned_hours), 0)::numeric AS assigned_hours,
                COALESCE(SUM(ps.time_entry_count), 0)::bigint AS time_entry_count,
                COALESCE(SUM(ps.used_hours), 0)::numeric AS used_hours
            FROM direct_links dl
            JOIN project_stats ps
                ON ps.project_id = dl.project_id
            GROUP BY dl.project_intake_request_id
        ),
        candidate_aggregate AS (
            SELECT
                cl.project_intake_request_id,
                COUNT(DISTINCT ps.project_id)::bigint AS project_count,
                STRING_AGG(DISTINCT ps.project_code || ' · ' || ps.project_name, '; ' ORDER BY ps.project_code || ' · ' || ps.project_name) AS project_labels,
                COALESCE(SUM(ps.task_count), 0)::bigint AS task_count,
                COALESCE(SUM(ps.active_task_count), 0)::bigint AS active_task_count,
                COALESCE(SUM(ps.assignment_count), 0)::bigint AS assignment_count,
                COALESCE(SUM(ps.assigned_engineer_count), 0)::bigint AS assigned_engineer_count,
                COALESCE(SUM(ps.assigned_hours), 0)::numeric AS assigned_hours,
                COALESCE(SUM(ps.time_entry_count), 0)::bigint AS time_entry_count,
                COALESCE(SUM(ps.used_hours), 0)::numeric AS used_hours
            FROM candidate_links cl
            JOIN project_stats ps
                ON ps.project_id = cl.project_id
            GROUP BY cl.project_intake_request_id
        )
        SELECT
            pir.project_intake_request_id,
            pir.request_number,
            COALESCE(pir.client_name, '') AS client_name,
            pir.request_title,
            COALESCE(pir.intake_status, 'new') AS intake_status,
            COALESCE(pir.priority, 'normal') AS priority,
            COALESCE(pir.project_signed_date::text, '') AS project_signed_date_text,
            COALESCE(pm.display_name, '') AS assigned_pm_name,
            COALESCE(da.project_count, 0)::bigint AS directly_linked_project_count,
            COALESCE(ca.project_count, 0)::bigint AS candidate_project_count,
            COALESCE(da.project_labels, ca.project_labels, '') AS project_labels,
            CASE WHEN COALESCE(da.project_count, 0) > 0 THEN COALESCE(da.task_count, 0) ELSE COALESCE(ca.task_count, 0) END::bigint AS task_count,
            CASE WHEN COALESCE(da.project_count, 0) > 0 THEN COALESCE(da.active_task_count, 0) ELSE COALESCE(ca.active_task_count, 0) END::bigint AS active_task_count,
            CASE WHEN COALESCE(da.project_count, 0) > 0 THEN COALESCE(da.assignment_count, 0) ELSE COALESCE(ca.assignment_count, 0) END::bigint AS assignment_count,
            CASE WHEN COALESCE(da.project_count, 0) > 0 THEN COALESCE(da.assigned_engineer_count, 0) ELSE COALESCE(ca.assigned_engineer_count, 0) END::bigint AS assigned_engineer_count,
            CASE WHEN COALESCE(da.project_count, 0) > 0 THEN COALESCE(da.assigned_hours, 0) ELSE COALESCE(ca.assigned_hours, 0) END::numeric AS assigned_hours,
            CASE WHEN COALESCE(da.project_count, 0) > 0 THEN COALESCE(da.time_entry_count, 0) ELSE COALESCE(ca.time_entry_count, 0) END::bigint AS time_entry_count,
            CASE WHEN COALESCE(da.project_count, 0) > 0 THEN COALESCE(da.used_hours, 0) ELSE COALESCE(ca.used_hours, 0) END::numeric AS used_hours
        FROM project_intake_requests pir
        LEFT JOIN app_users pm
            ON pm.user_id = pir.assigned_pm_user_id
        LEFT JOIN direct_aggregate da
            ON da.project_intake_request_id = pir.project_intake_request_id
        LEFT JOIN candidate_aggregate ca
            ON ca.project_intake_request_id = pir.project_intake_request_id
        WHERE @can_view_all = TRUE
           OR pir.assigned_pm_user_id = @user_id
           OR EXISTS (
                SELECT 1
                FROM direct_links dl
                JOIN projects p
                    ON p.project_id = dl.project_id
                WHERE dl.project_intake_request_id = pir.project_intake_request_id
                  AND p.project_manager_user_id = @user_id
           )
        ORDER BY
            CASE WHEN pir.project_signed_date IS NULL THEN 0 ELSE 1 END,
            pir.project_signed_date DESC NULLS LAST,
            pir.created_at DESC NULLS LAST;
        """, connection))
    {
        command.Parameters.AddWithValue("can_view_all", canViewAll);
        command.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var signedDateText = reader.GetString(6);
            var directProjectCount = reader.GetInt64(8);
            var candidateProjectCount = reader.GetInt64(9);
            var taskCount = reader.GetInt64(11);
            var assignmentCount = reader.GetInt64(13);
            var timeEntryCount = reader.GetInt64(16);
            var status = reader.GetString(4);

            string readinessStage;
            string readinessMessage;

            if (string.IsNullOrWhiteSpace(signedDateText))
            {
                readinessStage = "signed_date_needed";
                readinessMessage = "Signed project date is missing. The intake is not ready for handoff tracking.";
            }
            else if (!new[] { "approved", "triage", "resource_requested", "assigned", "active" }.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                readinessStage = "intake_not_ready";
                readinessMessage = "Intake exists, but it has not moved into an approved/active handoff status.";
            }
            else if (directProjectCount == 0 && candidateProjectCount == 0)
            {
                readinessStage = "project_link_needed";
                readinessMessage = "No project is linked or strongly matched yet. Next step is to connect the intake to a project record.";
            }
            else if (directProjectCount == 0 && candidateProjectCount > 0)
            {
                readinessStage = "project_link_confirmation_needed";
                readinessMessage = "A possible project match exists, but a direct intake-to-project link should be confirmed.";
            }
            else if (taskCount == 0)
            {
                readinessStage = "work_tasks_needed";
                readinessMessage = "Project is linked, but work tasks have not been created yet.";
            }
            else if (assignmentCount == 0)
            {
                readinessStage = "engineer_assignments_needed";
                readinessMessage = "Work tasks exist, but engineers have not been assigned yet.";
            }
            else if (timeEntryCount == 0)
            {
                readinessStage = "timesheet_usage_pending";
                readinessMessage = "Engineers are assigned, but no timesheet usage has been recorded yet.";
            }
            else
            {
                readinessStage = "utilization_flow_ready";
                readinessMessage = "Intake, project, work tasks, assignments, timesheet activity, and utilization readiness are connected.";
            }

            intakes.Add(new
            {
                intakeId = reader.GetGuid(0),
                requestNumber = reader.GetString(1),
                clientName = reader.GetString(2),
                requestTitle = reader.GetString(3),
                intakeStatus = status,
                priority = reader.GetString(5),
                projectSignedDate = signedDateText,
                assignedPmName = reader.GetString(7),
                directlyLinkedProjectCount = directProjectCount,
                candidateProjectCount,
                projectLabels = reader.GetString(10),
                taskCount,
                activeTaskCount = reader.GetInt64(12),
                assignmentCount,
                assignedEngineerCount = reader.GetInt64(14),
                assignedHours = reader.GetDecimal(15),
                timeEntryCount,
                usedHours = reader.GetDecimal(17),
                readinessStage,
                readinessMessage
            });
        }
    }

    var projects = new List<object>();

    await using (var projectCommand = new NpgsqlCommand("""
        WITH task_rollup AS (
            SELECT
                project_id,
                COUNT(DISTINCT task_id)::bigint AS task_count
            FROM project_tasks
            GROUP BY project_id
        ),
        assignment_rollup AS (
            SELECT
                project_id,
                COUNT(DISTINCT project_assignment_id)::bigint AS assignment_count,
                COUNT(DISTINCT user_id)::bigint AS assigned_engineer_count,
                COALESCE(SUM(assigned_hours), 0)::numeric AS assigned_hours
            FROM project_assignments
            GROUP BY project_id
        ),
        time_rollup AS (
            SELECT
                project_id,
                COUNT(DISTINCT time_entry_id)::bigint AS time_entry_count,
                COALESCE(SUM(hours), 0)::numeric AS used_hours
            FROM time_entries
            WHERE project_id IS NOT NULL
              AND COALESCE(status, 'draft') NOT IN ('manager_declined', 'rejected', 'voided')
            GROUP BY project_id
        )
        SELECT
            p.project_id,
            p.project_code,
            p.project_name,
            COALESCE(c.client_name, '') AS client_name,
            p.status,
            COALESCE(pm.display_name, '') AS project_manager_name,
            COALESCE(tr.task_count, 0)::bigint AS task_count,
            COALESCE(ar.assignment_count, 0)::bigint AS assignment_count,
            COALESCE(ar.assigned_engineer_count, 0)::bigint AS assigned_engineer_count,
            COALESCE(ar.assigned_hours, 0)::numeric AS assigned_hours,
            COALESCE(tir.time_entry_count, 0)::bigint AS time_entry_count,
            COALESCE(tir.used_hours, 0)::numeric AS used_hours
        FROM projects p
        LEFT JOIN clients c
            ON c.client_id = p.client_id
        LEFT JOIN app_users pm
            ON pm.user_id = p.project_manager_user_id
        LEFT JOIN task_rollup tr
            ON tr.project_id = p.project_id
        LEFT JOIN assignment_rollup ar
            ON ar.project_id = p.project_id
        LEFT JOIN time_rollup tir
            ON tir.project_id = p.project_id
        WHERE p.status <> 'archived'
          AND (@can_view_all = TRUE OR p.project_manager_user_id = @user_id)
        ORDER BY p.project_name, p.project_code;
        """, connection))
    {
        projectCommand.Parameters.AddWithValue("can_view_all", canViewAll);
        projectCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await projectCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            projects.Add(new
            {
                projectId = reader.GetGuid(0),
                projectCode = reader.GetString(1),
                projectName = reader.GetString(2),
                clientName = reader.GetString(3),
                status = reader.GetString(4),
                projectManagerName = reader.GetString(5),
                taskCount = reader.GetInt64(6),
                assignmentCount = reader.GetInt64(7),
                assignedEngineerCount = reader.GetInt64(8),
                assignedHours = reader.GetDecimal(9),
                timeEntryCount = reader.GetInt64(10),
                usedHours = reader.GetDecimal(11)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AR Project Intake to Work Task Builder Handoff",
        canViewIntakeWorkTaskHandoff = true,
        access = new
        {
            canViewAll,
            canViewManaged,
            automationEnabled = false,
            canManageProjectLinks,
            note = "This release exposes handoff readiness. It does not automatically convert intake records into projects or tasks yet."
        },
        lifecycle = new[]
        {
            new { step = 1, title = "Project Intake", description = "A request is created manually, uploaded, or sourced from a future integration." },
            new { step = 2, title = "Signed / Approved Intake", description = "Signed date, PM assignment, status, and supporting documents indicate readiness to move forward." },
            new { step = 3, title = "Project Record", description = "The intake should connect to a project record that owns delivery, customer, PM, and cost context." },
            new { step = 4, title = "Work Task Builder", description = "The project receives classified project, service request, open, or non-project work tasks." },
            new { step = 5, title = "Engineer Assignment", description = "Engineers are assigned to work tasks with hours and effective dates." },
            new { step = 6, title = "Timesheet and Utilization", description = "Assigned tasks become available for time entry and feed utilization readiness." }
        },
        summary = new
        {
            intakeCount = intakes.Count,
            signedIntakeCount = intakes.Count(item => !string.IsNullOrWhiteSpace(Convert.ToString(item.GetType().GetProperty("projectSignedDate")?.GetValue(item)))),
            directProjectLinkedIntakeCount = intakes.Count(item => Convert.ToInt64(item.GetType().GetProperty("directlyLinkedProjectCount")?.GetValue(item) ?? 0) > 0),
            candidateProjectMatchedIntakeCount = intakes.Count(item => Convert.ToInt64(item.GetType().GetProperty("candidateProjectCount")?.GetValue(item) ?? 0) > 0),
            taskReadyIntakeCount = intakes.Count(item => Convert.ToInt64(item.GetType().GetProperty("taskCount")?.GetValue(item) ?? 0) > 0),
            assignmentReadyIntakeCount = intakes.Count(item => Convert.ToInt64(item.GetType().GetProperty("assignmentCount")?.GetValue(item) ?? 0) > 0),
            timesheetActivityIntakeCount = intakes.Count(item => Convert.ToInt64(item.GetType().GetProperty("timeEntryCount")?.GetValue(item) ?? 0) > 0),
            projectCount = projects.Count
        },
        intakes,
        projects
    });
});


// 019M-AS Intake Project Link Confirmation + Resource Assignment Handoff
app.MapGet("/api/project-intake/project-link-options", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            COALESCE(p.permission_code, '') AS permission_code
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await accessCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));

            if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                permissions.Add(reader.GetString(1));
            }
        }
    }

    var canViewAll =
        (roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR"))
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canViewManaged =
        roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PM_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || permissions.Contains("VIEW_PROJECT_INTAKE")
        || permissions.Contains("VIEW_PROJECT_INTAKE_AGING")
        || permissions.Contains("VIEW_INTAKE_WORK_TASK_HANDOFF")
        || permissions.Contains("MANAGE_INTAKE_PROJECT_LINKS");

    var canManageProjectLinks =
        canViewAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PM_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || permissions.Contains("MANAGE_INTAKE_PROJECT_LINKS")
        || permissions.Contains("MANAGE_PROJECT_INTAKE");

    if (!canViewAll && !canViewManaged)
    {
        return Results.Json(new
        {
            canViewProjectLinkOptions = false,
            status = "access_denied",
            message = "Project link options are restricted to intake and project handoff roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var intakes = new List<Dictionary<string, object?>>();
    var intakeIndex = new Dictionary<Guid, Dictionary<string, object?>>();

    await using (var command = new NpgsqlCommand("""
        WITH active_link AS (
            SELECT DISTINCT ON (project_intake_request_id)
                project_intake_request_id,
                project_id,
                confirmation_note,
                confirmed_at
            FROM project_intake_project_links
            WHERE is_active = TRUE
              AND link_status = 'confirmed'
            ORDER BY project_intake_request_id, confirmed_at DESC
        )
        SELECT
            pir.project_intake_request_id,
            pir.request_number,
            COALESCE(pir.client_name, '') AS client_name,
            pir.request_title,
            COALESCE(pir.intake_status, 'new') AS intake_status,
            COALESCE(pir.priority, 'normal') AS priority,
            COALESCE(pir.project_signed_date::text, '') AS project_signed_date_text,
            COALESCE(pm.display_name, '') AS assigned_pm_name,
            al.project_id AS confirmed_project_id,
            COALESCE(cp.project_code, '') AS confirmed_project_code,
            COALESCE(cp.project_name, '') AS confirmed_project_name,
            COALESCE(al.confirmation_note, '') AS confirmation_note
        FROM project_intake_requests pir
        LEFT JOIN app_users pm
            ON pm.user_id = pir.assigned_pm_user_id
        LEFT JOIN active_link al
            ON al.project_intake_request_id = pir.project_intake_request_id
        LEFT JOIN projects cp
            ON cp.project_id = al.project_id
        WHERE @can_view_all = TRUE
           OR pir.assigned_pm_user_id = @user_id
        ORDER BY pir.created_at DESC NULLS LAST;
        """, connection))
    {
        command.Parameters.AddWithValue("can_view_all", canViewAll);
        command.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var intakeId = reader.GetGuid(0);
            var item = new Dictionary<string, object?>
            {
                ["intakeId"] = intakeId,
                ["requestNumber"] = reader.GetString(1),
                ["clientName"] = reader.GetString(2),
                ["requestTitle"] = reader.GetString(3),
                ["intakeStatus"] = reader.GetString(4),
                ["priority"] = reader.GetString(5),
                ["projectSignedDate"] = reader.GetString(6),
                ["assignedPmName"] = reader.GetString(7),
                ["confirmedProjectId"] = reader.IsDBNull(8) ? null : reader.GetGuid(8),
                ["confirmedProjectCode"] = reader.GetString(9),
                ["confirmedProjectName"] = reader.GetString(10),
                ["confirmationNote"] = reader.GetString(11),
                ["candidateProjects"] = new List<object>()
            };

            intakes.Add(item);
            intakeIndex[intakeId] = item;
        }
    }

    var projects = new List<object>();

    await using (var projectCommand = new NpgsqlCommand("""
        SELECT
            p.project_id,
            p.project_code,
            p.project_name,
            COALESCE(c.client_name, '') AS client_name,
            COALESCE(pm.display_name, '') AS project_manager_name,
            p.project_manager_user_id
        FROM projects p
        LEFT JOIN clients c
            ON c.client_id = p.client_id
        LEFT JOIN app_users pm
            ON pm.user_id = p.project_manager_user_id
        WHERE COALESCE(p.status, 'active') <> 'archived'
          AND (@can_view_all = TRUE OR p.project_manager_user_id = @user_id)
        ORDER BY c.client_name, p.project_name, p.project_code;
        """, connection))
    {
        projectCommand.Parameters.AddWithValue("can_view_all", canViewAll);
        projectCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await projectCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            projects.Add(new
            {
                projectId = reader.GetGuid(0),
                projectCode = reader.GetString(1),
                projectName = reader.GetString(2),
                clientName = reader.GetString(3),
                projectManagerName = reader.GetString(4),
                projectManagerUserId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5)
            });
        }
    }

    await using (var candidateCommand = new NpgsqlCommand("""
        SELECT DISTINCT
            pir.project_intake_request_id,
            p.project_id,
            p.project_code,
            p.project_name,
            COALESCE(c.client_name, '') AS client_name,
            COALESCE(pm.display_name, '') AS project_manager_name
        FROM project_intake_requests pir
        JOIN projects p
            ON (
                (pir.client_id IS NOT NULL AND p.client_id = pir.client_id)
                OR (pir.assigned_pm_user_id IS NOT NULL AND p.project_manager_user_id = pir.assigned_pm_user_id)
                OR (
                    NULLIF(TRIM(pir.request_title), '') IS NOT NULL
                    AND LOWER(p.project_name) = LOWER(pir.request_title)
                )
            )
        LEFT JOIN clients c
            ON c.client_id = p.client_id
        LEFT JOIN app_users pm
            ON pm.user_id = p.project_manager_user_id
        WHERE COALESCE(p.status, 'active') <> 'archived'
          AND (@can_view_all = TRUE OR pir.assigned_pm_user_id = @user_id OR p.project_manager_user_id = @user_id)
        ORDER BY pir.project_intake_request_id, p.project_code, p.project_name;
        """, connection))
    {
        candidateCommand.Parameters.AddWithValue("can_view_all", canViewAll);
        candidateCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await candidateCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var intakeId = reader.GetGuid(0);
            if (!intakeIndex.TryGetValue(intakeId, out var intake)) continue;

            var candidates = (List<object>)intake["candidateProjects"]!;
            candidates.Add(new
            {
                projectId = reader.GetGuid(1),
                projectCode = reader.GetString(2),
                projectName = reader.GetString(3),
                clientName = reader.GetString(4),
                projectManagerName = reader.GetString(5)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AS Intake Project Link Confirmation",
        canViewProjectLinkOptions = true,
        canManageProjectLinks,
        access = new
        {
            canViewAll,
            canViewManaged,
            canManageProjectLinks,
            automationEnabled = false,
            note = "Project links are manually confirmed. This does not automatically create projects or work tasks."
        },
        summary = new
        {
            intakeCount = intakes.Count,
            confirmedLinkCount = intakes.Count(item => item.TryGetValue("confirmedProjectId", out var value) && value is Guid),
            candidateMatchCount = intakes.Sum(item => ((List<object>)item["candidateProjects"]!).Count),
            projectOptionCount = projects.Count
        },
        intakes,
        projects
    });
});

app.MapPost("/api/project-intake/{intakeId:guid}/project-link", async (Guid intakeId, JsonElement payload, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!payload.TryGetProperty("projectId", out var projectElement)
        || projectElement.ValueKind != JsonValueKind.String
        || !Guid.TryParse(projectElement.GetString(), out var projectId)
        || projectId == Guid.Empty)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "A valid projectId is required to confirm an intake project link."
        });
    }

    var confirmationNote = payload.TryGetProperty("confirmationNote", out var noteElement) && noteElement.ValueKind == JsonValueKind.String
        ? noteElement.GetString()?.Trim()
        : null;

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            COALESCE(p.permission_code, '') AS permission_code
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await accessCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));

            if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                permissions.Add(reader.GetString(1));
            }
        }
    }

    var canManageAll =
        (roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR"))
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canManageProjectLinks =
        canManageAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PM_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || permissions.Contains("MANAGE_INTAKE_PROJECT_LINKS")
        || permissions.Contains("MANAGE_PROJECT_INTAKE");

    if (!canManageProjectLinks)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Project link confirmation is restricted to Administrators, PTC, and Project Management roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    string requestNumber;
    string requestTitle;
    Guid? intakePmUserId;
    string projectCode;
    string projectName;
    Guid? projectManagerUserId;

    await using (var readCommand = new NpgsqlCommand("""
        SELECT
            pir.request_number,
            pir.request_title,
            pir.assigned_pm_user_id,
            p.project_code,
            p.project_name,
            p.project_manager_user_id
        FROM project_intake_requests pir
        CROSS JOIN projects p
        WHERE pir.project_intake_request_id = @intake_id
          AND p.project_id = @project_id;
        """, connection))
    {
        readCommand.Parameters.AddWithValue("intake_id", intakeId);
        readCommand.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await readCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "The selected intake or project was not found."
            });
        }

        requestNumber = reader.GetString(0);
        requestTitle = reader.GetString(1);
        intakePmUserId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        projectCode = reader.GetString(3);
        projectName = reader.GetString(4);
        projectManagerUserId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
    }

    if (!canManageAll
        && intakePmUserId != sessionUserId.Value
        && projectManagerUserId != sessionUserId.Value)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Project Managers can confirm links only for intakes or projects assigned to them."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        await using (var supersedeCommand = new NpgsqlCommand("""
            UPDATE project_intake_project_links
            SET is_active = FALSE,
                link_status = 'superseded',
                updated_at = NOW()
            WHERE project_intake_request_id = @intake_id
              AND project_id <> @project_id
              AND is_active = TRUE;
            """, connection, transaction))
        {
            supersedeCommand.Parameters.AddWithValue("intake_id", intakeId);
            supersedeCommand.Parameters.AddWithValue("project_id", projectId);
            await supersedeCommand.ExecuteNonQueryAsync();
        }

        Guid linkId;
        await using (var linkCommand = new NpgsqlCommand("""
            INSERT INTO project_intake_project_links (
                project_intake_request_id,
                project_id,
                link_status,
                link_source,
                confirmation_note,
                confirmed_by_user_id,
                confirmed_at,
                is_active,
                updated_at
            )
            VALUES (
                @intake_id,
                @project_id,
                'confirmed',
                'manual_confirmation',
                NULLIF(@confirmation_note, ''),
                @confirmed_by_user_id,
                NOW(),
                TRUE,
                NOW()
            )
            ON CONFLICT (project_intake_request_id, project_id) DO UPDATE
            SET link_status = 'confirmed',
                link_source = 'manual_confirmation',
                confirmation_note = NULLIF(EXCLUDED.confirmation_note, ''),
                confirmed_by_user_id = EXCLUDED.confirmed_by_user_id,
                confirmed_at = NOW(),
                is_active = TRUE,
                updated_at = NOW()
            RETURNING project_intake_project_link_id;
            """, connection, transaction))
        {
            linkCommand.Parameters.AddWithValue("intake_id", intakeId);
            linkCommand.Parameters.AddWithValue("project_id", projectId);
            linkCommand.Parameters.AddWithValue("confirmation_note", confirmationNote ?? "");
            linkCommand.Parameters.AddWithValue("confirmed_by_user_id", sessionUserId.Value);
            linkId = (Guid)(await linkCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to confirm intake project link."));
        }

        await using (var resourceCommand = new NpgsqlCommand("""
            UPDATE engineering_resource_requests
            SET project_id = @project_id,
                updated_at = NOW()
            WHERE project_intake_request_id = @intake_id
              AND project_id IS NULL;
            """, connection, transaction))
        {
            resourceCommand.Parameters.AddWithValue("intake_id", intakeId);
            resourceCommand.Parameters.AddWithValue("project_id", projectId);
            await resourceCommand.ExecuteNonQueryAsync();
        }

        await using (var documentCommand = new NpgsqlCommand("""
            UPDATE project_intake_documents
            SET project_id = @project_id
            WHERE project_intake_request_id = @intake_id
              AND project_id IS NULL
              AND COALESCE(is_active, TRUE) = TRUE;
            """, connection, transaction))
        {
            documentCommand.Parameters.AddWithValue("intake_id", intakeId);
            documentCommand.Parameters.AddWithValue("project_id", projectId);
            await documentCommand.ExecuteNonQueryAsync();
        }

        await using (var intakeCommand = new NpgsqlCommand("""
            UPDATE project_intake_requests
            SET updated_at = NOW(),
                triage_started_at = COALESCE(triage_started_at, NOW()),
                last_post_intake_edit_at = NOW(),
                last_post_intake_edit_by_user_id = @user_id,
                last_post_intake_edit_note = @note
            WHERE project_intake_request_id = @intake_id;
            """, connection, transaction))
        {
            intakeCommand.Parameters.AddWithValue("intake_id", intakeId);
            intakeCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
            intakeCommand.Parameters.AddWithValue("note", $"Project link confirmed to {projectCode}.");
            await intakeCommand.ExecuteNonQueryAsync();
        }

        var auditJson = JsonSerializer.Serialize(new
        {
            intakeId,
            requestNumber,
            requestTitle,
            projectId,
            projectCode,
            projectName,
            confirmationNote
        });

        await using (var auditCommand = new NpgsqlCommand("""
            INSERT INTO audit_logs (
                actor_user_id,
                action,
                entity_type,
                entity_id,
                new_value
            )
            VALUES (
                @actor_user_id,
                'project_intake_project_link_confirmed',
                'project_intake_request',
                @entity_id,
                @new_value::jsonb
            );
            """, connection, transaction))
        {
            auditCommand.Parameters.AddWithValue("actor_user_id", sessionUserId.Value);
            auditCommand.Parameters.AddWithValue("entity_id", intakeId);
            auditCommand.Parameters.AddWithValue("new_value", auditJson);
            await auditCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        /* 053I_PROJECT_LINK_AE_SA_SYNC_START */
        await using (var projectPulse053IOwnerSyncCommand = new NpgsqlCommand("""
            UPDATE projects p
            SET account_executive_user_id = COALESCE(pir.account_executive_user_id, p.account_executive_user_id),
                solution_architect_user_id = COALESCE(pir.solution_architect_user_id, p.solution_architect_user_id),
                updated_at = NOW()
            FROM project_intake_requests pir
            WHERE pir.project_intake_request_id = @intake_id
              AND p.project_id = @project_id;
            """, connection))
        {
            projectPulse053IOwnerSyncCommand.Parameters.AddWithValue("intake_id", intakeId);
            projectPulse053IOwnerSyncCommand.Parameters.AddWithValue("project_id", projectId);
            await projectPulse053IOwnerSyncCommand.ExecuteNonQueryAsync();
        }
        /* 053I_PROJECT_LINK_AE_SA_SYNC_END */



        return Results.Ok(new
        {
            status = "project_link_confirmed",
            module = "019M-AS Intake Project Link Confirmation",
            intakeId,
            projectId,
            linkId,
            requestNumber,
            projectCode,
            projectName,
            message = $"Project link confirmed: {requestNumber} → {projectCode}."
        });
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
});


// 019M-AT Resource Request Assignment to Work Task Assignment Handoff
app.MapGet("/api/project-intake/resource-assignment-handoff", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            COALESCE(p.permission_code, '') AS permission_code
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await accessCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));

            if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                permissions.Add(reader.GetString(1));
            }
        }
    }

    var canViewAll =
        (roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR"))
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canViewManaged =
        roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PM_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || roles.Contains("PROJECT_COORDINATOR")
        || permissions.Contains("VIEW_RESOURCE_ASSIGNMENT_HANDOFF")
        || permissions.Contains("VIEW_INTAKE_WORK_TASK_HANDOFF")
        || permissions.Contains("MANAGE_INTAKE_PROJECT_LINKS")
        || permissions.Contains("VIEW_ENGINEERING_RESOURCE_REQUESTS")
        || permissions.Contains("MANAGE_ENGINEERING_RESOURCE_REQUESTS");

    var canPromoteResourceAssignments =
        canViewAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PM_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || permissions.Contains("MANAGE_RESOURCE_ASSIGNMENT_PROMOTION")
        || permissions.Contains("MANAGE_ENGINEERING_RESOURCE_REQUESTS")
        || permissions.Contains("ASSIGN_WORK_TASKS")
        || permissions.Contains("MANAGE_PROJECT_INTAKE");

    if (!canViewAll && !canViewManaged)
    {
        return Results.Json(new
        {
            canViewResourceAssignmentHandoff = false,
            status = "access_denied",
            message = "Resource assignment handoff readiness is available to Administrators, PTC, Project Management, and approved intake handoff roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var requests = new List<Dictionary<string, object?>>();
    var requestIndex = new Dictionary<Guid, Dictionary<string, object?>>();

    await using (var command = new NpgsqlCommand("""
        WITH task_rollup AS (
            SELECT
                project_id,
                COUNT(DISTINCT task_id)::bigint AS task_count,
                COUNT(DISTINCT task_id) FILTER (WHERE COALESCE(is_active, TRUE) = TRUE)::bigint AS active_task_count
            FROM project_tasks
            GROUP BY project_id
        ),
        resource_assignment_rollup AS (
            SELECT
                engineering_resource_request_id,
                COUNT(DISTINCT engineering_resource_request_assignment_id)::bigint AS resource_assignment_count,
                COUNT(DISTINCT user_id)::bigint AS assigned_engineer_count,
                COALESCE(SUM(allocated_hours), 0)::numeric AS allocated_hours
            FROM engineering_resource_request_assignments
            GROUP BY engineering_resource_request_id
        ),
        project_assignment_rollup AS (
            SELECT
                err.engineering_resource_request_id,
                COUNT(DISTINCT pa.project_assignment_id)::bigint AS project_assignment_count,
                COALESCE(SUM(pa.assigned_hours), 0)::numeric AS project_assigned_hours
            FROM engineering_resource_requests err
            JOIN engineering_resource_request_assignments erra
                ON erra.engineering_resource_request_id = err.engineering_resource_request_id
            LEFT JOIN project_assignments pa
                ON pa.project_id = err.project_id
               AND pa.user_id = erra.user_id
            GROUP BY err.engineering_resource_request_id
        ),
        time_rollup AS (
            SELECT
                err.engineering_resource_request_id,
                COUNT(DISTINCT te.time_entry_id)::bigint AS time_entry_count,
                COALESCE(SUM(te.hours), 0)::numeric AS used_hours
            FROM engineering_resource_requests err
            JOIN engineering_resource_request_assignments erra
                ON erra.engineering_resource_request_id = err.engineering_resource_request_id
            LEFT JOIN time_entries te
                ON te.project_id = err.project_id
               AND te.user_id = erra.user_id
               AND COALESCE(te.status, 'draft') NOT IN ('manager_declined', 'rejected', 'voided')
            GROUP BY err.engineering_resource_request_id
        )
        SELECT
            err.engineering_resource_request_id,
            err.request_number,
            err.project_intake_request_id,
            COALESCE(pir.request_number, '') AS intake_number,
            COALESCE(pir.request_title, '') AS intake_title,
            err.project_id,
            COALESCE(p.project_code, '') AS project_code,
            COALESCE(p.project_name, '') AS project_name,
            err.requested_function,
            COALESCE(err.skill_requirements, '') AS skill_requirements,
            err.requested_hours,
            COALESCE(err.request_status, 'requested') AS request_status,
            COALESCE(pm.display_name, '') AS assigned_pm_name,
            COALESCE(err.target_start_date::text, '') AS target_start_date_text,
            COALESCE(err.target_end_date::text, '') AS target_end_date_text,
            COALESCE(tr.task_count, 0)::bigint AS task_count,
            COALESCE(tr.active_task_count, 0)::bigint AS active_task_count,
            COALESCE(rar.resource_assignment_count, 0)::bigint AS resource_assignment_count,
            COALESCE(rar.assigned_engineer_count, 0)::bigint AS assigned_engineer_count,
            COALESCE(rar.allocated_hours, 0)::numeric AS allocated_hours,
            COALESCE(par.project_assignment_count, 0)::bigint AS project_assignment_count,
            COALESCE(par.project_assigned_hours, 0)::numeric AS project_assigned_hours,
            COALESCE(tir.time_entry_count, 0)::bigint AS time_entry_count,
            COALESCE(tir.used_hours, 0)::numeric AS used_hours
        FROM engineering_resource_requests err
        LEFT JOIN project_intake_requests pir
            ON pir.project_intake_request_id = err.project_intake_request_id
        LEFT JOIN projects p
            ON p.project_id = err.project_id
        LEFT JOIN app_users pm
            ON pm.user_id = err.assigned_pm_user_id
        LEFT JOIN task_rollup tr
            ON tr.project_id = err.project_id
        LEFT JOIN resource_assignment_rollup rar
            ON rar.engineering_resource_request_id = err.engineering_resource_request_id
        LEFT JOIN project_assignment_rollup par
            ON par.engineering_resource_request_id = err.engineering_resource_request_id
        LEFT JOIN time_rollup tir
            ON tir.engineering_resource_request_id = err.engineering_resource_request_id
        WHERE @can_view_all = TRUE
           OR err.assigned_pm_user_id = @user_id
           OR p.project_manager_user_id = @user_id
        ORDER BY err.created_at DESC NULLS LAST;
        """, connection))
    {
        command.Parameters.AddWithValue("can_view_all", canViewAll);
        command.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var requestId = reader.GetGuid(0);
            var projectId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
            var taskCount = reader.GetInt64(15);
            var resourceAssignmentCount = reader.GetInt64(17);
            var allocatedHours = reader.GetDecimal(19);
            var projectAssignmentCount = reader.GetInt64(20);
            var projectAssignedHours = reader.GetDecimal(21);
            var timeEntryCount = reader.GetInt64(22);

            string readinessStage;
            string readinessMessage;

            if (projectId is null)
            {
                readinessStage = "project_link_needed";
                readinessMessage = "Resource request is not linked to a project yet.";
            }
            else if (taskCount == 0)
            {
                readinessStage = "work_tasks_needed";
                readinessMessage = "Resource request is linked to a project, but the project does not have work tasks yet.";
            }
            else if (resourceAssignmentCount == 0)
            {
                readinessStage = "resource_assignment_needed";
                readinessMessage = "Project and work tasks exist, but engineers have not been assigned to the resource request yet.";
            }
            else if (projectAssignmentCount == 0)
            {
                readinessStage = "project_task_assignment_needed";
                readinessMessage = "Engineers are assigned to the resource request, but they are not yet assigned to project tasks.";
            }
            else if (projectAssignedHours < allocatedHours)
            {
                readinessStage = "assignment_hours_gap";
                readinessMessage = "Project task assignments exist, but assigned task hours are below the resource request allocation.";
            }
            else if (timeEntryCount == 0)
            {
                readinessStage = "timesheet_usage_pending";
                readinessMessage = "Resource and project task assignments are ready, but no timesheet activity has been recorded yet.";
            }
            else
            {
                readinessStage = "assignment_flow_ready";
                readinessMessage = "Resource request assignment, project task assignment, timesheet activity, and utilization readiness are connected.";
            }

            var item = new Dictionary<string, object?>
            {
                ["resourceRequestId"] = requestId,
                ["requestNumber"] = reader.GetString(1),
                ["intakeId"] = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                ["intakeNumber"] = reader.GetString(3),
                ["intakeTitle"] = reader.GetString(4),
                ["projectId"] = projectId,
                ["projectCode"] = reader.GetString(6),
                ["projectName"] = reader.GetString(7),
                ["requestedFunction"] = reader.GetString(8),
                ["skillRequirements"] = reader.GetString(9),
                ["requestedHours"] = reader.GetDecimal(10),
                ["requestStatus"] = reader.GetString(11),
                ["assignedPmName"] = reader.GetString(12),
                ["targetStartDate"] = reader.GetString(13),
                ["targetEndDate"] = reader.GetString(14),
                ["taskCount"] = taskCount,
                ["activeTaskCount"] = reader.GetInt64(16),
                ["resourceAssignmentCount"] = resourceAssignmentCount,
                ["assignedEngineerCount"] = reader.GetInt64(18),
                ["allocatedHours"] = allocatedHours,
                ["projectAssignmentCount"] = projectAssignmentCount,
                ["projectAssignedHours"] = projectAssignedHours,
                ["timeEntryCount"] = timeEntryCount,
                ["usedHours"] = reader.GetDecimal(23),
                ["readinessStage"] = readinessStage,
                ["readinessMessage"] = readinessMessage,
                ["assignments"] = new List<object>(),
                ["projectTasks"] = new List<object>()
            };

            requests.Add(item);
            requestIndex[requestId] = item;
        }
    }

    await using (var assignmentCommand = new NpgsqlCommand("""
        SELECT
            erra.engineering_resource_request_id,
            erra.engineering_resource_request_assignment_id,
            erra.user_id,
            COALESCE(u.display_name, '') AS engineer_name,
            COALESCE(u.email, '') AS engineer_email,
            COALESCE(erra.assignment_status, 'assigned') AS assignment_status,
            erra.allocated_hours,
            COALESCE(erra.allocation_percent, 0)::numeric AS allocation_percent,
            COALESCE(erra.assignment_notes, '') AS assignment_notes,
            COALESCE(par.project_assignment_count, 0)::bigint AS project_assignment_count,
            COALESCE(par.project_assigned_hours, 0)::numeric AS project_assigned_hours,
            COALESCE(par.task_labels, '') AS task_labels
        FROM engineering_resource_request_assignments erra
        JOIN engineering_resource_requests err
            ON err.engineering_resource_request_id = erra.engineering_resource_request_id
        LEFT JOIN app_users u
            ON u.user_id = erra.user_id
        LEFT JOIN LATERAL (
            SELECT
                COUNT(DISTINCT pa.project_assignment_id)::bigint AS project_assignment_count,
                COALESCE(SUM(pa.assigned_hours), 0)::numeric AS project_assigned_hours,
                STRING_AGG(DISTINCT pt.task_code || ' · ' || pt.task_name, '; ' ORDER BY pt.task_code || ' · ' || pt.task_name) AS task_labels
            FROM project_assignments pa
            JOIN project_tasks pt
                ON pt.task_id = pa.task_id
            WHERE pa.project_id = err.project_id
              AND pa.user_id = erra.user_id
        ) par ON TRUE
        LEFT JOIN projects p
            ON p.project_id = err.project_id
        WHERE @can_view_all = TRUE
           OR err.assigned_pm_user_id = @user_id
           OR p.project_manager_user_id = @user_id
        ORDER BY erra.assigned_at DESC NULLS LAST;
        """, connection))
    {
        assignmentCommand.Parameters.AddWithValue("can_view_all", canViewAll);
        assignmentCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await assignmentCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var requestId = reader.GetGuid(0);
            if (!requestIndex.TryGetValue(requestId, out var request)) continue;

            var assignments = (List<object>)request["assignments"]!;
            assignments.Add(new
            {
                resourceAssignmentId = reader.GetGuid(1),
                engineerUserId = reader.GetGuid(2),
                engineerName = reader.GetString(3),
                engineerEmail = reader.GetString(4),
                assignmentStatus = reader.GetString(5),
                allocatedHours = reader.GetDecimal(6),
                allocationPercent = reader.GetDecimal(7),
                assignmentNotes = reader.GetString(8),
                projectAssignmentCount = reader.GetInt64(9),
                projectAssignedHours = reader.GetDecimal(10),
                taskLabels = reader.GetString(11)
            });
        }
    }

    await using (var taskCommand = new NpgsqlCommand("""
        SELECT
            err.engineering_resource_request_id,
            pt.task_id,
            pt.task_code,
            pt.task_name,
            pt.work_task_category,
            pt.billing_classification,
            pt.utilization_classification,
            COALESCE(pa_roll.assignment_count, 0)::bigint AS assignment_count,
            COALESCE(pa_roll.assigned_hours, 0)::numeric AS assigned_hours
        FROM engineering_resource_requests err
        JOIN project_tasks pt
            ON pt.project_id = err.project_id
        LEFT JOIN LATERAL (
            SELECT
                COUNT(DISTINCT pa.project_assignment_id)::bigint AS assignment_count,
                COALESCE(SUM(pa.assigned_hours), 0)::numeric AS assigned_hours
            FROM project_assignments pa
            WHERE pa.task_id = pt.task_id
        ) pa_roll ON TRUE
        LEFT JOIN projects p
            ON p.project_id = err.project_id
        WHERE @can_view_all = TRUE
           OR err.assigned_pm_user_id = @user_id
           OR p.project_manager_user_id = @user_id
        ORDER BY err.engineering_resource_request_id, pt.task_code, pt.task_name;
        """, connection))
    {
        taskCommand.Parameters.AddWithValue("can_view_all", canViewAll);
        taskCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await taskCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var requestId = reader.GetGuid(0);
            if (!requestIndex.TryGetValue(requestId, out var request)) continue;

            var tasks = (List<object>)request["projectTasks"]!;
            tasks.Add(new
            {
                taskId = reader.GetGuid(1),
                taskCode = reader.GetString(2),
                taskName = reader.GetString(3),
                workTaskCategory = reader.GetString(4),
                billingClassification = reader.GetString(5),
                utilizationClassification = reader.GetString(6),
                assignmentCount = reader.GetInt64(7),
                assignedHours = reader.GetDecimal(8)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AT Resource Request Assignment to Work Task Assignment Handoff",
        canViewResourceAssignmentHandoff = true,
        access = new
        {
            canViewAll,
            canViewManaged,
            automationEnabled = false,
            canPromoteResourceAssignments,
            note = "This release exposes resource-assignment-to-project-task readiness. Promotion remains manual and requires explicit management action."
        },
        summary = new
        {
            resourceRequestCount = requests.Count,
            projectLinkedRequestCount = requests.Count(item => item["projectId"] is Guid),
            workTaskReadyRequestCount = requests.Count(item => Convert.ToInt64(item["taskCount"] ?? 0) > 0),
            resourceAssignmentReadyRequestCount = requests.Count(item => Convert.ToInt64(item["resourceAssignmentCount"] ?? 0) > 0),
            projectTaskAssignmentReadyRequestCount = requests.Count(item => Convert.ToInt64(item["projectAssignmentCount"] ?? 0) > 0),
            assignmentFlowReadyRequestCount = requests.Count(item => string.Equals(Convert.ToString(item["readinessStage"]), "assignment_flow_ready", StringComparison.OrdinalIgnoreCase)),
            gapRequestCount = requests.Count(item => !string.Equals(Convert.ToString(item["readinessStage"]), "assignment_flow_ready", StringComparison.OrdinalIgnoreCase))
        },
        lifecycle = new[]
        {
            new { step = 1, title = "Engineering Resource Request", description = "PM or PTC requests engineering capacity for a linked intake or project." },
            new { step = 2, title = "Resource Assignment", description = "One or more engineers are assigned to the resource request with allocated hours." },
            new { step = 3, title = "Project Work Tasks", description = "The linked project must have classified work tasks available for assignment." },
            new { step = 4, title = "Project Task Assignment", description = "Resource assignments must be translated into task-level assignments so engineers can select the correct work." },
            new { step = 5, title = "Timesheet and Utilization", description = "Task assignments become available for timesheets and feed utilization readiness." }
        },
        requests
    });
});


// 019M-AU Manual Resource Assignment to Project Task Promotion Controls
app.MapPost("/api/project-intake/resource-assignment-promotions", async (JsonElement payload, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    Guid? ReadGuid(string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return Guid.TryParse(value.GetString(), out var guid) ? guid : null;
    }

    string ReadString(string name, string fallback = "")
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return fallback;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    var resourceRequestId = ReadGuid("resourceRequestId");
    var resourceAssignmentId = ReadGuid("resourceAssignmentId");
    var promotionNote = ReadString("promotionNote", "Manual promotion from resource request assignment to project task assignment.");

    if (resourceRequestId is null || resourceAssignmentId is null)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "resourceRequestId and resourceAssignmentId are required."
        });
    }

    if (!payload.TryGetProperty("taskAssignments", out var taskAssignmentsElement)
        || taskAssignmentsElement.ValueKind != JsonValueKind.Array
        || taskAssignmentsElement.GetArrayLength() == 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "At least one task assignment is required."
        });
    }

    var taskAssignments = new List<(Guid TaskId, decimal AssignedHours, decimal AllocationPercent, DateOnly EffectiveStartDate, DateOnly? EffectiveEndDate)>();

    foreach (var item in taskAssignmentsElement.EnumerateArray())
    {
        if (!item.TryGetProperty("taskId", out var taskElement)
            || taskElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(taskElement.GetString(), out var taskId)
            || taskId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Each task assignment requires a valid taskId."
            });
        }

        var assignedHours = 0m;
        if (item.TryGetProperty("assignedHours", out var hoursElement) && hoursElement.ValueKind != JsonValueKind.Null)
        {
            hoursElement.TryGetDecimal(out assignedHours);
        }

        if (assignedHours <= 0)
        {
            return Results.BadRequest(new
            {
                status = "validation_failed",
                message = "Each task assignment requires assignedHours greater than zero."
            });
        }

        var allocationPercent = 0m;
        if (item.TryGetProperty("allocationPercent", out var allocationElement) && allocationElement.ValueKind != JsonValueKind.Null)
        {
            allocationElement.TryGetDecimal(out allocationPercent);
        }

        var effectiveStartDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (item.TryGetProperty("effectiveStartDate", out var startElement)
            && startElement.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(startElement.GetString(), out var parsedStart))
        {
            effectiveStartDate = parsedStart;
        }

        DateOnly? effectiveEndDate = null;
        if (item.TryGetProperty("effectiveEndDate", out var endElement)
            && endElement.ValueKind == JsonValueKind.String
            && DateOnly.TryParse(endElement.GetString(), out var parsedEnd))
        {
            effectiveEndDate = parsedEnd;
        }

        taskAssignments.Add((taskId, assignedHours, allocationPercent, effectiveStartDate, effectiveEndDate));
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            COALESCE(p.permission_code, '') AS permission_code
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await accessCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));

            if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                permissions.Add(reader.GetString(1));
            }
        }
    }

    var canManageAll =
        (roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR"))
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canPromoteResourceAssignments =
        canManageAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PM_TEAM_LEAD"))
        || (roles.Contains("PROJECT_MANAGEMENT_LEAD") || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD"))
        || permissions.Contains("MANAGE_RESOURCE_ASSIGNMENT_PROMOTION")
        || permissions.Contains("MANAGE_ENGINEERING_RESOURCE_REQUESTS")
        || permissions.Contains("ASSIGN_WORK_TASKS")
        || permissions.Contains("MANAGE_PROJECT_INTAKE");

    if (!canPromoteResourceAssignments)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Resource assignment promotion is restricted to Administrators, PTC, and Project Management roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    Guid projectId;
    Guid engineerUserId;
    Guid? assignedPmUserId;
    Guid? projectManagerUserId;
    string requestNumber;
    string projectCode;
    string projectName;
    string engineerName;
    decimal allocatedHours;

    await using (var contextCommand = new NpgsqlCommand("""
        SELECT
            err.project_id,
            err.request_number,
            err.assigned_pm_user_id,
            p.project_manager_user_id,
            p.project_code,
            p.project_name,
            erra.user_id,
            COALESCE(u.display_name, u.email, '') AS engineer_name,
            erra.allocated_hours
        FROM engineering_resource_requests err
        JOIN engineering_resource_request_assignments erra
            ON erra.engineering_resource_request_id = err.engineering_resource_request_id
        LEFT JOIN projects p
            ON p.project_id = err.project_id
        LEFT JOIN app_users u
            ON u.user_id = erra.user_id
        WHERE err.engineering_resource_request_id = @resource_request_id
          AND erra.engineering_resource_request_assignment_id = @resource_assignment_id;
        """, connection))
    {
        contextCommand.Parameters.AddWithValue("resource_request_id", resourceRequestId.Value);
        contextCommand.Parameters.AddWithValue("resource_assignment_id", resourceAssignmentId.Value);

        await using var reader = await contextCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "The selected resource request assignment was not found."
            });
        }

        if (reader.IsDBNull(0))
        {
            return Results.BadRequest(new
            {
                status = "project_link_needed",
                message = "The selected resource request must be linked to a project before promotion."
            });
        }

        projectId = reader.GetGuid(0);
        requestNumber = reader.GetString(1);
        assignedPmUserId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        projectManagerUserId = reader.IsDBNull(3) ? null : reader.GetGuid(3);
        projectCode = reader.GetString(4);
        projectName = reader.GetString(5);
        engineerUserId = reader.GetGuid(6);
        engineerName = reader.GetString(7);
        allocatedHours = reader.GetDecimal(8);
    }

    if (!canManageAll
        && assignedPmUserId != sessionUserId.Value
        && projectManagerUserId != sessionUserId.Value)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Project Managers can promote only resource assignments in their PM scope."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var createdCount = 0;
    var updatedCount = 0;
    var skippedCount = 0;
    var taskResults = new List<object>();

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        foreach (var taskAssignment in taskAssignments)
        {
            string taskCode;
            string taskName;

            await using (var taskCommand = new NpgsqlCommand("""
                SELECT task_code, task_name
                FROM project_tasks
                WHERE task_id = @task_id
                  AND project_id = @project_id
                  AND COALESCE(is_active, TRUE) = TRUE;
                """, connection, transaction))
            {
                taskCommand.Parameters.AddWithValue("task_id", taskAssignment.TaskId);
                taskCommand.Parameters.AddWithValue("project_id", projectId);

                await using var reader = await taskCommand.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                {
                    throw new InvalidOperationException("A selected task is not active or does not belong to the linked project.");
                }

                taskCode = reader.GetString(0);
                taskName = reader.GetString(1);
            }

            var existingAssignments = new List<Guid>();

            await using (var existingCommand = new NpgsqlCommand("""
                SELECT project_assignment_id
                FROM project_assignments
                WHERE project_id = @project_id
                  AND task_id = @task_id
                  AND user_id = @engineer_user_id
                ORDER BY created_at DESC NULLS LAST;
                """, connection, transaction))
            {
                existingCommand.Parameters.AddWithValue("project_id", projectId);
                existingCommand.Parameters.AddWithValue("task_id", taskAssignment.TaskId);
                existingCommand.Parameters.AddWithValue("engineer_user_id", engineerUserId);

                await using var reader = await existingCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingAssignments.Add(reader.GetGuid(0));
                }
            }

            var note = $"Resource request promotion from {requestNumber}. {promotionNote}".Trim();

            if (existingAssignments.Count > 1)
            {
                skippedCount++;
                taskResults.Add(new
                {
                    taskId = taskAssignment.TaskId,
                    taskCode,
                    taskName,
                    status = "skipped_duplicate_risk",
                    message = "Multiple existing assignments were found for this engineer/task. Review manually before changing."
                });
                continue;
            }

            if (existingAssignments.Count == 1)
            {
                await using var updateCommand = new NpgsqlCommand("""
                    UPDATE project_assignments
                    SET assigned_by_user_id = @assigned_by_user_id,
                        effective_start_date = @effective_start_date,
                        effective_end_date = @effective_end_date,
                        allocation_percent = NULLIF(@allocation_percent, 0),
                        assigned_hours = @assigned_hours,
                        assignment_source = 'resource_request_promotion',
                        assignment_notes = CASE
                            WHEN NULLIF(TRIM(COALESCE(assignment_notes, '')), '') IS NULL THEN @assignment_notes
                            ELSE assignment_notes || E'\n' || @assignment_notes
                        END,
                        updated_at = NOW()
                    WHERE project_assignment_id = @project_assignment_id;
                    """, connection, transaction);

                updateCommand.Parameters.AddWithValue("project_assignment_id", existingAssignments[0]);
                updateCommand.Parameters.AddWithValue("assigned_by_user_id", sessionUserId.Value);
                updateCommand.Parameters.AddWithValue("effective_start_date", taskAssignment.EffectiveStartDate);
                updateCommand.Parameters.AddWithValue("effective_end_date", taskAssignment.EffectiveEndDate.HasValue ? taskAssignment.EffectiveEndDate.Value : DBNull.Value);
                updateCommand.Parameters.AddWithValue("allocation_percent", taskAssignment.AllocationPercent);
                updateCommand.Parameters.AddWithValue("assigned_hours", taskAssignment.AssignedHours);
                updateCommand.Parameters.AddWithValue("assignment_notes", note);

                await updateCommand.ExecuteNonQueryAsync();
                updatedCount++;

                taskResults.Add(new
                {
                    taskId = taskAssignment.TaskId,
                    taskCode,
                    taskName,
                    projectAssignmentId = existingAssignments[0],
                    status = "updated",
                    assignedHours = taskAssignment.AssignedHours
                });
            }
            else
            {
                Guid projectAssignmentId;

                await using (var insertCommand = new NpgsqlCommand("""
                    INSERT INTO project_assignments (
                        project_id,
                        task_id,
                        user_id,
                        assigned_by_user_id,
                        effective_start_date,
                        effective_end_date,
                        allocation_percent,
                        assigned_hours,
                        assignment_source,
                        assignment_notes,
                        updated_at
                    )
                    VALUES (
                        @project_id,
                        @task_id,
                        @engineer_user_id,
                        @assigned_by_user_id,
                        @effective_start_date,
                        @effective_end_date,
                        NULLIF(@allocation_percent, 0),
                        @assigned_hours,
                        'resource_request_promotion',
                        @assignment_notes,
                        NOW()
                    )
                    RETURNING project_assignment_id;
                    """, connection, transaction))
                {
                    insertCommand.Parameters.AddWithValue("project_id", projectId);
                    insertCommand.Parameters.AddWithValue("task_id", taskAssignment.TaskId);
                    insertCommand.Parameters.AddWithValue("engineer_user_id", engineerUserId);
                    insertCommand.Parameters.AddWithValue("assigned_by_user_id", sessionUserId.Value);
                    insertCommand.Parameters.AddWithValue("effective_start_date", taskAssignment.EffectiveStartDate);
                    insertCommand.Parameters.AddWithValue("effective_end_date", taskAssignment.EffectiveEndDate.HasValue ? taskAssignment.EffectiveEndDate.Value : DBNull.Value);
                    insertCommand.Parameters.AddWithValue("allocation_percent", taskAssignment.AllocationPercent);
                    insertCommand.Parameters.AddWithValue("assigned_hours", taskAssignment.AssignedHours);
                    insertCommand.Parameters.AddWithValue("assignment_notes", note);

                    projectAssignmentId = (Guid)(await insertCommand.ExecuteScalarAsync() ?? Guid.Empty);
                }

                createdCount++;

                taskResults.Add(new
                {
                    taskId = taskAssignment.TaskId,
                    taskCode,
                    taskName,
                    projectAssignmentId,
                    status = "created",
                    assignedHours = taskAssignment.AssignedHours
                });
            }
        }

        var auditJson = JsonSerializer.Serialize(new
        {
            resourceRequestId,
            resourceAssignmentId,
            requestNumber,
            projectId,
            projectCode,
            projectName,
            engineerUserId,
            engineerName,
            allocatedHours,
            promotionNote,
            createdCount,
            updatedCount,
            skippedCount,
            taskResults
        });

        await using (var auditCommand = new NpgsqlCommand("""
            INSERT INTO audit_logs (
                actor_user_id,
                action,
                entity_type,
                entity_id,
                new_value
            )
            VALUES (
                @actor_user_id,
                'resource_assignment_promoted_to_project_tasks',
                'engineering_resource_request',
                @entity_id,
                @new_value::jsonb
            );
            """, connection, transaction))
        {
            auditCommand.Parameters.AddWithValue("actor_user_id", sessionUserId.Value);
            auditCommand.Parameters.AddWithValue("entity_id", resourceRequestId.Value);
            auditCommand.Parameters.AddWithValue("new_value", auditJson);
            await auditCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "resource_assignment_promotion_complete",
            module = "019M-AU Manual Resource Assignment to Project Task Promotion Controls",
            resourceRequestId,
            resourceAssignmentId,
            requestNumber,
            projectId,
            projectCode,
            projectName,
            engineerUserId,
            engineerName,
            createdCount,
            updatedCount,
            skippedCount,
            taskResults,
            message = $"Promotion completed for {requestNumber}: {createdCount} created, {updatedCount} updated, {skippedCount} skipped."
        });
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
});

app.MapGet("/api/project-intake/aging-summary", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    bool canView;
    bool canManage;

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            BOOL_OR(
                r.role_code IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT_LEAD', 'PM_TEAM_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD')
                OR p.permission_code IN ('VIEW_PROJECT_INTAKE', 'VIEW_PROJECT_INTAKE_AGING', 'MANAGE_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE_AGING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
            ) AS can_view,
            BOOL_OR(
                r.role_code IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
                OR p.permission_code IN ('MANAGE_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE_AGING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
            ) AS can_manage
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
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        await using var reader = await accessCommand.ExecuteReaderAsync();
        await reader.ReadAsync();

        canView = !reader.IsDBNull(0) && reader.GetBoolean(0);
        canManage = !reader.IsDBNull(1) && reader.GetBoolean(1);
    }

    if (!canView)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Project intake aging is available to Project Coordina…162152 tokens truncated…_location_groups wlg
  ON wlg.work_location_group_id = te.work_location_group_id
LEFT JOIN work_locations wl
  ON wl.work_location_id = te.work_location_id
LEFT JOIN team_memberships tmem
  ON tmem.user_id = te.user_id
 AND (tmem.effective_end_date IS NULL OR tmem.effective_end_date >= te.work_date)
LEFT JOIN teams tm
  ON tm.team_id = tmem.team_id
LEFT JOIN client_invoices ci
  ON ci.project_id = p.project_id
 AND te.work_date BETWEEN ci.billing_period_start AND ci.billing_period_end
" + ProjectPulse030WhereSql(where) + @"
ORDER BY te.work_date DESC NULLS LAST, customer, engagement_project, engineer
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.time_entries + readable joins", columns, sql, parameters);
}

static async Task<object> ProjectPulse030BuildReadableProjectReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("customer", "Customer"),
        ("project_code", "Project Code"),
        ("project_name", "Project Name"),
        ("project_manager", "Project Manager"),
        ("project_status", "Project Status"),
        ("billable_status", "Billable Status"),
        ("start_date", "Start Date"),
        ("end_date", "End Date"),
        ("planned_engineering_cost", "Planned Engineering Cost"),
        ("planned_pm_cost", "Planned PM Cost"),
        ("planned_total_project_cost", "Planned Total Project Cost"),
        ("time_entry_count", "Time Entry Count"),
        ("total_hours", "Total Hours"),
        ("billable_hours", "Billable Hours"),
        ("non_billable_hours", "Non-Billable Hours")
    };

    var where = new List<string>();
    var parameters = new Dictionary<string, object>();

    ProjectPulse030AddDateRange(criteria, where, parameters, "COALESCE(p.start_date, p.created_at::date)");
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "customer", new[] { "c.client_name", "c.client_code" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "project", new[] { "p.project_name", "p.project_code" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "pm", new[] { "pm.display_name", "pm.email" });
    ProjectPulse030AddBillableFilter(criteria, where, "COALESCE(p.billable, FALSE)");

    string sql = @"
SELECT
    COALESCE(c.client_name, '') AS customer,
    COALESCE(p.project_code, '') AS project_code,
    COALESCE(p.project_name, '') AS project_name,
    COALESCE(pm.display_name, '') AS project_manager,
    COALESCE(p.status, '') AS project_status,
    CASE WHEN COALESCE(p.billable, FALSE) THEN 'Billable' ELSE 'Non-billable' END AS billable_status,
    p.start_date,
    p.end_date,
    p.planned_engineering_cost,
    p.planned_pm_cost,
    p.planned_total_project_cost,
    COUNT(DISTINCT te.time_entry_id)::integer AS time_entry_count,
    COALESCE(SUM(te.hours), 0) AS total_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN te.hours ELSE 0 END), 0) AS billable_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN 0 ELSE te.hours END), 0) AS non_billable_hours
FROM projects p
LEFT JOIN clients c
  ON c.client_id = p.client_id
LEFT JOIN app_users pm
  ON pm.user_id = p.project_manager_user_id
LEFT JOIN time_entries te
  ON te.project_id = p.project_id
" + ProjectPulse030WhereSql(where) + @"
GROUP BY c.client_name, p.project_code, p.project_name, pm.display_name, p.status, p.billable, p.start_date, p.end_date, p.planned_engineering_cost, p.planned_pm_cost, p.planned_total_project_cost
ORDER BY c.client_name, p.project_name
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.projects + readable joins", columns, sql, parameters);
}

static async Task<object> ProjectPulse030BuildReadableCustomerReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("customer", "Customer"),
        ("customer_code", "Customer Code"),
        ("active_projects", "Active Projects"),
        ("project_count", "Project Count"),
        ("time_entry_count", "Time Entry Count"),
        ("total_hours", "Total Hours"),
        ("billable_hours", "Billable Hours"),
        ("non_billable_hours", "Non-Billable Hours"),
        ("invoice_total", "Invoice Total")
    };

    var where = new List<string>();
    var parameters = new Dictionary<string, object>();

    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "customer", new[] { "c.client_name", "c.client_code" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "project", new[] { "p.project_name", "p.project_code" });
    ProjectPulse030AddBillableFilter(criteria, where, "COALESCE(te.billable, p.billable, FALSE)");

    string sql = @"
SELECT
    COALESCE(c.client_name, '') AS customer,
    COALESCE(c.client_code, '') AS customer_code,
    COUNT(DISTINCT p.project_id) FILTER (WHERE p.status = 'active')::integer AS active_projects,
    COUNT(DISTINCT p.project_id)::integer AS project_count,
    COUNT(DISTINCT te.time_entry_id)::integer AS time_entry_count,
    COALESCE(SUM(te.hours), 0) AS total_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN te.hours ELSE 0 END), 0) AS billable_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN 0 ELSE te.hours END), 0) AS non_billable_hours,
    COALESCE(SUM(DISTINCT ci.invoice_total), 0) AS invoice_total
FROM clients c
LEFT JOIN projects p
  ON p.client_id = c.client_id
LEFT JOIN time_entries te
  ON te.project_id = p.project_id
LEFT JOIN client_invoices ci
  ON ci.project_id = p.project_id
" + ProjectPulse030WhereSql(where) + @"
GROUP BY c.client_name, c.client_code
ORDER BY c.client_name
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.clients + readable joins", columns, sql, parameters);
}

static async Task<object> ProjectPulse030BuildReadableTeamReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("team", "Team"),
        ("engineer", "Engineer"),
        ("engineer_email", "Engineer Email"),
        ("job_title", "Job Title"),
        ("department", "Department"),
        ("total_hours", "Total Hours"),
        ("billable_hours", "Billable Hours"),
        ("non_billable_hours", "Non-Billable Hours"),
        ("time_entry_count", "Time Entry Count")
    };

    var where = new List<string>();
    var parameters = new Dictionary<string, object>();

    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "team", new[] { "tm.team_name", "u.team_name" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "engineer", new[] { "u.display_name", "u.email" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "organization", new[] { "u.department", "u.department_name" });
    ProjectPulse030AddBillableFilter(criteria, where, "COALESCE(te.billable, FALSE)");

    string sql = @"
SELECT
    COALESCE(tm.team_name, NULLIF(u.team_name, ''), '') AS team,
    COALESCE(u.display_name, '') AS engineer,
    COALESCE(u.email, '') AS engineer_email,
    COALESCE(u.job_title, '') AS job_title,
    COALESCE(u.department, u.department_name, '') AS department,
    COALESCE(SUM(te.hours), 0) AS total_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, FALSE) THEN te.hours ELSE 0 END), 0) AS billable_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, FALSE) THEN 0 ELSE te.hours END), 0) AS non_billable_hours,
    COUNT(DISTINCT te.time_entry_id)::integer AS time_entry_count
FROM app_users u
LEFT JOIN team_memberships tmem
  ON tmem.user_id = u.user_id
LEFT JOIN teams tm
  ON tm.team_id = tmem.team_id
LEFT JOIN time_entries te
  ON te.user_id = u.user_id
WHERE u.is_active = TRUE
" + (where.Count == 0 ? "" : " AND " + string.Join(" AND ", where)) + @"
GROUP BY tm.team_name, u.team_name, u.display_name, u.email, u.job_title, u.department, u.department_name
ORDER BY team, engineer
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.teams + readable joins", columns, sql, parameters);
}

static async Task<object> ProjectPulse030ExecuteReadableReportAsync(
    NpgsqlConnection connection,
    string reportType,
    string category,
    string sourceTable,
    List<(string Key, string Label)> columns,
    string sql,
    Dictionary<string, object> parameters)
{
    var rows = new List<object?[]>();

    await using var command = new NpgsqlCommand(sql, connection);

    foreach (var parameter in parameters)
    {
        command.Parameters.AddWithValue(parameter.Key, parameter.Value);
    }

    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var row = new object?[columns.Count];

            for (int i = 0; i < columns.Count; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : ProjectPulse030SafeFormatValue(reader.GetValue(i));
            }

            rows.Add(row);
        }
    }

    string message = rows.Count == 0
        ? $"No database rows matched the selected criteria from '{sourceTable}'."
        : $"Readable database-backed report generated from '{sourceTable}'. Names and labels are shown instead of internal IDs where joins are available.";

    return new
    {
        databaseBacked = true,
        readable = true,
        reportType,
        category,
        sourceTable,
        columns = columns.Select(c => c.Label).ToArray(),
        columnKeys = columns.Select(c => c.Key).ToArray(),
        rows,
        rowCount = rows.Count,
        message
    };
}

static async Task<object> ProjectPulse030LoadReadableFilterOptionsAsync(NpgsqlConnection connection)
{
    var customers = await ProjectPulse030LoadStringOptionsAsync(connection, @"
SELECT DISTINCT client_name
FROM clients
WHERE is_active = TRUE
  AND NULLIF(TRIM(client_name), '') IS NOT NULL
ORDER BY client_name;", "All customers");

    var projects = await ProjectPulse030LoadStringOptionsAsync(connection, @"
SELECT DISTINCT CONCAT_WS(' - ', NULLIF(project_code, ''), NULLIF(project_name, '')) AS label
FROM projects
WHERE NULLIF(TRIM(project_name), '') IS NOT NULL
ORDER BY label;", "All projects");

    var pms = await ProjectPulse030LoadPersonOptionsAsync(connection, @"
SELECT DISTINCT u.display_name, u.email
FROM projects p
JOIN app_users u
  ON u.user_id = p.project_manager_user_id
WHERE u.is_active = TRUE
  AND NULLIF(TRIM(u.display_name), '') IS NOT NULL
  AND NULLIF(TRIM(u.email), '') IS NOT NULL
ORDER BY u.display_name, u.email;", "All PMs");

    var engineers = await ProjectPulse030LoadPersonOptionsAsync(connection, @"
SELECT DISTINCT u.display_name, u.email
FROM app_users u
JOIN app_user_role_assignments ura
  ON ura.user_id = u.user_id
 AND ura.is_active = TRUE
JOIN app_roles r
  ON r.app_role_id = ura.app_role_id
 AND r.is_active = TRUE
WHERE u.is_active = TRUE
  AND u.login_enabled = TRUE
  AND r.role_code IN ('ENGINEERING', 'ENGINEERING_LEAD', 'MANAGER', 'PROJECT_TEAM_COORDINATOR', 'SUPER_ADMINISTRATOR')
  AND NULLIF(TRIM(u.display_name), '') IS NOT NULL
  AND NULLIF(TRIM(u.email), '') IS NOT NULL
ORDER BY u.display_name, u.email;", "All engineers");

    var teams = await ProjectPulse030LoadStringOptionsAsync(connection, @"
SELECT DISTINCT team_name
FROM teams
WHERE is_active = TRUE
  AND NULLIF(TRIM(team_name), '') IS NOT NULL
ORDER BY team_name;", "All teams");

    var organizations = await ProjectPulse030LoadStringOptionsAsync(connection, @"
SELECT DISTINCT COALESCE(NULLIF(department, ''), NULLIF(department_name, '')) AS label
FROM app_users
WHERE is_active = TRUE
  AND login_enabled = TRUE
  AND COALESCE(NULLIF(department, ''), NULLIF(department_name, '')) IS NOT NULL
ORDER BY label;", "All organizations");

    var contractTypes = await ProjectPulse030LoadStringOptionsAsync(connection, @"
SELECT DISTINCT label
FROM (
    SELECT 'T&M' AS label
    UNION ALL SELECT 'Time and Material'
    UNION ALL SELECT 'Fixed Bid'
    UNION ALL SELECT 'Fixed Fee'
    UNION ALL SELECT 'Service Request'
    UNION ALL SELECT 'Managed Service'
    UNION ALL SELECT 'Project'
    UNION ALL SELECT 'Non-Project Time'
    UNION ALL
    SELECT CASE
        WHEN te.project_id IS NULL THEN 'Non-Project Time'
        WHEN COALESCE(p.billable, te.billable, FALSE) THEN 'T&M'
        ELSE 'Non-Billable'
    END AS label
    FROM time_entries te
    LEFT JOIN projects p
      ON p.project_id = te.project_id
) source
WHERE NULLIF(TRIM(label), '') IS NOT NULL
ORDER BY label;", "All contract types");

    var billableOptions = await ProjectPulse030LoadStringOptionsAsync(connection, @"
SELECT DISTINCT CASE WHEN COALESCE(billable, FALSE) THEN 'Billable' ELSE 'Non-billable' END AS label
FROM time_entries
ORDER BY label;", "All billable statuses");

    var workLocations = await ProjectPulse030LoadStringOptionsAsync(connection, @"
SELECT DISTINCT COALESCE(wl.location_name, wlg.group_name) AS label
FROM time_entries te
LEFT JOIN work_locations wl
  ON wl.work_location_id = te.work_location_id
LEFT JOIN work_location_groups wlg
  ON wlg.work_location_group_id = te.work_location_group_id
WHERE COALESCE(wl.location_name, wlg.group_name) IS NOT NULL
ORDER BY label;", "All work locations");

    var workCodes = await ProjectPulse030LoadStringOptionsAsync(connection, @"
SELECT DISTINCT COALESCE(pt.task_code, npc.category_code) AS label
FROM time_entries te
LEFT JOIN project_tasks pt
  ON pt.task_id = te.task_id
LEFT JOIN non_project_time_categories npc
  ON npc.non_project_time_category_id = te.non_project_time_category_id
WHERE COALESCE(pt.task_code, npc.category_code) IS NOT NULL
ORDER BY label;", "All work codes");

    return new
    {
        databaseBacked = true,
        customers,
        projects,
        pms,
        engineers,
        selectedEngineers = engineers,
        teams,
        organizations,
        contractTypes,
        billableOptions,
        workLocations,
        workCodes
    };
}

static async Task<string[]> ProjectPulse030LoadStringOptionsAsync(NpgsqlConnection connection, string sql, string allLabel)
{
    var values = new List<string> { allLabel };

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        if (!reader.IsDBNull(0))
        {
            string value = reader.GetString(0).Trim();
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value);
            }
        }
    }

    return values.ToArray();
}

static async Task<object[]> ProjectPulse030LoadPersonOptionsAsync(NpgsqlConnection connection, string sql, string allLabel)
{
    var values = new List<object>
    {
        new { label = allLabel, value = allLabel }
    };

    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { allLabel };

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        string displayName = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
        string email = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email))
        {
            continue;
        }

        if (seen.Add(email))
        {
            values.Add(new
            {
                label = $"{displayName} <{email}>",
                value = email
            });
        }
    }

    return values.ToArray();
}


static void ProjectPulse030AddDateRange(JsonElement criteria, List<string> where, Dictionary<string, object> parameters, string sqlExpression)
{
    string startDate = ProjectPulse030SafeReadString(criteria, "startDate");
    string endDate = ProjectPulse030SafeReadString(criteria, "endDate");

    if (DateTime.TryParse(startDate, out var parsedStart))
    {
        string parameterName = "p" + parameters.Count;
        parameters[parameterName] = parsedStart.Date;
        where.Add(sqlExpression + " >= @" + parameterName);
    }

    if (DateTime.TryParse(endDate, out var parsedEnd))
    {
        string parameterName = "p" + parameters.Count;
        parameters[parameterName] = parsedEnd.Date;
        where.Add(sqlExpression + " <= @" + parameterName);
    }
}

static void ProjectPulse030AddReadableTextFilter(JsonElement criteria, List<string> where, Dictionary<string, object> parameters, string fieldName, string[] sqlExpressions)
{
    string value = ProjectPulse030SafeReadString(criteria, fieldName);

    if (ProjectPulse030IsAllValue(value))
    {
        return;
    }

    var terms = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(term => !ProjectPulse030IsAllValue(term))
        .Take(12)
        .ToList();

    if (terms.Count == 0)
    {
        return;
    }

    var termGroups = new List<string>();

    foreach (var term in terms)
    {
        string parameterName = "p" + parameters.Count;
        parameters[parameterName] = "%" + term.ToLowerInvariant() + "%";

        termGroups.Add("(" + string.Join(" OR ", sqlExpressions.Select(expression => "LOWER(COALESCE(" + expression + "::text, '')) LIKE @" + parameterName)) + ")");
    }

    where.Add("(" + string.Join(" OR ", termGroups) + ")");
}

static void ProjectPulse030AddBillableFilter(JsonElement criteria, List<string> where, string sqlExpression)
{
    string value = ProjectPulse030SafeReadString(criteria, "billableStatus", "billable", "billableFilter").ToLowerInvariant();

    if (ProjectPulse030IsAllValue(value))
    {
        return;
    }

    if (value.Contains("non"))
    {
        where.Add(sqlExpression + " = FALSE");
        return;
    }

    if (value.Contains("billable"))
    {
        where.Add(sqlExpression + " = TRUE");
    }
}

static void ProjectPulse030AddContractTypeFilter(JsonElement criteria, List<string> where)
{
    string value = ProjectPulse030SafeReadString(criteria, "contractType").ToLowerInvariant();

    if (ProjectPulse030IsAllValue(value))
    {
        return;
    }

    if (value.Contains("non-project"))
    {
        where.Add("te.project_id IS NULL");
        return;
    }

    if (value.Contains("non-billable"))
    {
        where.Add("te.project_id IS NOT NULL AND COALESCE(p.billable, te.billable, FALSE) = FALSE");
        return;
    }

    if (value.Contains("billable"))
    {
        where.Add("te.project_id IS NOT NULL AND COALESCE(p.billable, te.billable, FALSE) = TRUE");
    }
}

static bool ProjectPulse030IsAllValue(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    string normalized = value.Trim().ToLowerInvariant();
    return normalized == "all"
        || normalized.StartsWith("all ")
        || normalized.Contains("all scoped")
        || normalized.Contains("not selected")
        || normalized.Contains("none selected");
}

static string ProjectPulse030WhereSql(List<string> where)
{
    return where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);
}


// 030B_INVOICE_EXPORT_BUSINESS_REPORTS_START

static string ProjectPulse030ContractTypeExpression()
{
    return @"CASE
        WHEN te.project_id IS NULL THEN 'Non-Project Time'
        WHEN LOWER(COALESCE(pt.billing_classification, pt.work_task_category, pt.task_code, '')) LIKE '%fixed%' THEN 'Fixed Bid'
        WHEN LOWER(COALESCE(pt.billing_classification, pt.work_task_category, pt.task_code, '')) LIKE '%service%' THEN 'Service Request'
        WHEN LOWER(COALESCE(pt.billing_classification, pt.work_task_category, pt.task_code, '')) LIKE '%managed%' THEN 'Managed Service'
        WHEN COALESCE(p.billable, te.billable, pt.billable, FALSE) THEN 'T&M'
        ELSE 'Non-Billable'
    END";
}

static async Task<object> ProjectPulse030BuildTmSalesReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("customer", "Customer"),
        ("project_code", "Project Code"),
        ("project_name", "Project Name"),
        ("sales_contract_type", "Contract Type"),
        ("project_manager", "Project Manager"),
        ("engineer", "Engineer"),
        ("work_date", "Work Date"),
        ("hours", "Hours"),
        ("billable_status", "Billable Status"),
        ("work_code", "Work Code"),
        ("work_item", "Work Item"),
        ("description", "Description")
    };

    var where = ProjectPulse030CommonBusinessWhere(criteria, "te.work_date");
    where.Sql.Add("COALESCE(te.billable, pt.billable, p.billable, FALSE) = TRUE");

    string sql = @"
SELECT
    COALESCE(c.client_name, '') AS customer,
    COALESCE(p.project_code, '') AS project_code,
    COALESCE(p.project_name, '') AS project_name,
    " + ProjectPulse030ContractTypeExpression() + @" AS sales_contract_type,
    COALESCE(pm.display_name, '') AS project_manager,
    COALESCE(u.display_name, '') AS engineer,
    te.work_date,
    te.hours,
    CASE WHEN COALESCE(te.billable, pt.billable, p.billable, FALSE) THEN 'Billable' ELSE 'Non-billable' END AS billable_status,
    COALESCE(pt.task_code, npc.category_code, '') AS work_code,
    COALESCE(pt.task_name, npc.category_name, '') AS work_item,
    COALESCE(te.description, '') AS description
FROM time_entries te
LEFT JOIN app_users u ON u.user_id = te.user_id
LEFT JOIN projects p ON p.project_id = te.project_id
LEFT JOIN clients c ON c.client_id = p.client_id
LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
LEFT JOIN project_tasks pt ON pt.task_id = te.task_id
LEFT JOIN non_project_time_categories npc ON npc.non_project_time_category_id = te.non_project_time_category_id
" + ProjectPulse030WhereSql(where.Sql) + @"
ORDER BY customer, project_name, te.work_date DESC
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.time_entries + T&M sales joins", columns, sql, where.Parameters);
}

static async Task<object> ProjectPulse030BuildProjectStatusBilledRemainingReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("customer", "Customer"),
        ("project_code", "Project Code"),
        ("project_name", "Project Name"),
        ("project_manager", "Project Manager"),
        ("project_status", "Project Status"),
        ("planned_total_project_cost", "Planned Total Project Cost"),
        ("billed_cost", "Billed Cost"),
        ("labor_amount", "Labor Amount"),
        ("expense_amount", "Expense Amount"),
        ("remaining_balance", "Remaining Balance"),
        ("total_hours", "Total Hours"),
        ("billable_hours", "Billable Hours"),
        ("non_billable_hours", "Non-Billable Hours")
    };

    var where = ProjectPulse030CommonProjectWhere(criteria);

    string sql = @"
SELECT
    COALESCE(c.client_name, '') AS customer,
    COALESCE(p.project_code, '') AS project_code,
    COALESCE(p.project_name, '') AS project_name,
    COALESCE(pm.display_name, '') AS project_manager,
    COALESCE(p.status, '') AS project_status,
    COALESCE(p.planned_total_project_cost, 0) AS planned_total_project_cost,
    COALESCE(SUM(DISTINCT ci.invoice_total), 0) AS billed_cost,
    COALESCE(SUM(DISTINCT ci.labor_amount), 0) AS labor_amount,
    COALESCE(SUM(DISTINCT ci.expense_amount), 0) AS expense_amount,
    COALESCE(p.planned_total_project_cost, 0) - COALESCE(SUM(DISTINCT ci.invoice_total), 0) AS remaining_balance,
    COALESCE(SUM(te.hours), 0) AS total_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN te.hours ELSE 0 END), 0) AS billable_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN 0 ELSE te.hours END), 0) AS non_billable_hours
FROM projects p
LEFT JOIN clients c ON c.client_id = p.client_id
LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
LEFT JOIN client_invoices ci ON ci.project_id = p.project_id
LEFT JOIN time_entries te ON te.project_id = p.project_id
" + ProjectPulse030WhereSql(where.Sql) + @"
GROUP BY c.client_name, p.project_code, p.project_name, pm.display_name, p.status, p.planned_total_project_cost
ORDER BY customer, project_name
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.projects + invoices + time", columns, sql, where.Parameters);
}

static async Task<object> ProjectPulse030BuildCertifyReadyExpenseReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("engineer", "Engineer"),
        ("customer", "Customer"),
        ("project_code", "Project Code"),
        ("project_name", "Project Name"),
        ("project_manager", "Project Manager"),
        ("expense_report_number", "Expense Report Number"),
        ("expense_report_title", "Expense Report Title"),
        ("expense_status", "Expense Status"),
        ("expense_total", "Expense Total"),
        ("submitted_at", "Submitted At"),
        ("approved_at", "Approved At"),
        ("certify_status", "Certify Integration Status")
    };

    var where = ProjectPulse030CommonProjectWhere(criteria);

    string sql = @"
SELECT
    COALESCE(u.display_name, '') AS engineer,
    COALESCE(c.client_name, '') AS customer,
    COALESCE(p.project_code, '') AS project_code,
    COALESCE(p.project_name, '') AS project_name,
    COALESCE(pm.display_name, '') AS project_manager,
    COALESCE(er.report_number, '') AS expense_report_number,
    COALESCE(er.report_title, '') AS expense_report_title,
    COALESCE(er.report_status, '') AS expense_status,
    COALESCE(er.report_total, 0) AS expense_total,
    er.submitted_at,
    er.approved_at,
    'Pending Module 031 Certify integration mapping' AS certify_status
FROM expense_reports er
LEFT JOIN app_users u ON u.user_id = er.user_id
LEFT JOIN projects p ON p.project_id = er.project_id
LEFT JOIN clients c ON c.client_id = p.client_id
LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
" + ProjectPulse030WhereSql(where.Sql) + @"
ORDER BY er.created_at DESC
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.expense_reports + project joins", columns, sql, where.Parameters);
}

static async Task<object> ProjectPulse030BuildEngineerProjectOverUnderReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("engineer", "Engineer"),
        ("customer", "Customer"),
        ("project_code", "Project Code"),
        ("project_name", "Project Name"),
        ("planned_total_project_cost", "Planned Total Project Cost"),
        ("hours_entered", "Hours Entered"),
        ("billed_cost", "Billed Cost"),
        ("remaining_balance", "Remaining Balance"),
        ("budget_position", "Budget Position")
    };

    var where = ProjectPulse030CommonBusinessWhere(criteria, "te.work_date");

    string sql = @"
SELECT
    COALESCE(u.display_name, '') AS engineer,
    COALESCE(c.client_name, '') AS customer,
    COALESCE(p.project_code, '') AS project_code,
    COALESCE(p.project_name, '') AS project_name,
    COALESCE(p.planned_total_project_cost, 0) AS planned_total_project_cost,
    COALESCE(SUM(te.hours), 0) AS hours_entered,
    COALESCE(SUM(DISTINCT ci.invoice_total), 0) AS billed_cost,
    COALESCE(p.planned_total_project_cost, 0) - COALESCE(SUM(DISTINCT ci.invoice_total), 0) AS remaining_balance,
    CASE
        WHEN COALESCE(SUM(DISTINCT ci.invoice_total), 0) > COALESCE(p.planned_total_project_cost, 0) THEN 'Over budget'
        WHEN COALESCE(SUM(DISTINCT ci.invoice_total), 0) < COALESCE(p.planned_total_project_cost, 0) THEN 'Under budget'
        ELSE 'At budget'
    END AS budget_position
FROM time_entries te
LEFT JOIN app_users u ON u.user_id = te.user_id
LEFT JOIN projects p ON p.project_id = te.project_id
LEFT JOIN clients c ON c.client_id = p.client_id
LEFT JOIN client_invoices ci ON ci.project_id = p.project_id
" + ProjectPulse030WhereSql(where.Sql) + @"
GROUP BY u.display_name, c.client_name, p.project_code, p.project_name, p.planned_total_project_cost
ORDER BY budget_position, customer, project_name, engineer
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.time_entries + project budget joins", columns, sql, where.Parameters);
}

static async Task<object> ProjectPulse030BuildUtilizationOverUnderReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    // 030C_UTILIZATION_TIME_ENTRY_FALLBACK
    // Derive utilization directly from time_entries so the report remains useful even when
    // utilization_weekly_summaries has not yet been populated by a scheduled rollup job.
    var columns = new List<(string Key, string Label)>
    {
        ("engineer", "Engineer"),
        ("engineer_email", "Engineer Email"),
        ("team", "Team"),
        ("period_start_date", "Period Start Date"),
        ("period_end_date", "Period End Date"),
        ("billable_hours", "Billable Hours"),
        ("non_billable_hours", "Non-Billable Hours"),
        ("pto_hours", "PTO Hours"),
        ("total_hours", "Total Hours"),
        ("standard_period_hours", "Standard Period Hours"),
        ("utilization_percent", "Utilization Percent"),
        ("utilization_position", "Utilization Position")
    };

    var where = new List<string>();
    var parameters = new Dictionary<string, object>();

    ProjectPulse030AddDateRange(criteria, where, parameters, "te.work_date");
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "engineer", new[] { "u.display_name", "u.email" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "selectedEngineers", new[] { "u.display_name", "u.email" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "team", new[] { "tm.team_name", "u.team_name", "u.department", "u.department_name" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "organization", new[] { "u.department", "u.department_name" });

    string sql = @"
SELECT
    COALESCE(u.display_name, '') AS engineer,
    COALESCE(u.email, '') AS engineer_email,
    COALESCE(tm.team_name, NULLIF(u.team_name, ''), '') AS team,
    MIN(te.work_date) AS period_start_date,
    MAX(te.work_date) AS period_end_date,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, pt.billable, p.billable, FALSE) THEN te.hours ELSE 0 END), 0) AS billable_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, pt.billable, p.billable, FALSE) THEN 0 ELSE te.hours END), 0) AS non_billable_hours,
    COALESCE(SUM(CASE
        WHEN LOWER(COALESCE(npc.utilization_bucket, npc.category_code, npc.category_name, '')) IN ('pto', 'vacation', 'holiday')
          OR LOWER(COALESCE(npc.category_code, npc.category_name, '')) LIKE '%vacation%'
          OR LOWER(COALESCE(npc.category_code, npc.category_name, '')) LIKE '%holiday%'
          OR LOWER(COALESCE(npc.category_code, npc.category_name, '')) LIKE '%pto%'
        THEN te.hours ELSE 0 END), 0) AS pto_hours,
    COALESCE(SUM(te.hours), 0) AS total_hours,
    40::numeric AS standard_period_hours,
    CASE
        WHEN 40::numeric = 0 THEN 0
        ELSE ROUND((COALESCE(SUM(CASE WHEN COALESCE(te.billable, pt.billable, p.billable, FALSE) THEN te.hours ELSE 0 END), 0) / 40::numeric) * 100, 2)
    END AS utilization_percent,
    CASE
        WHEN ROUND((COALESCE(SUM(CASE WHEN COALESCE(te.billable, pt.billable, p.billable, FALSE) THEN te.hours ELSE 0 END), 0) / 40::numeric) * 100, 2) >= 100 THEN 'Over target'
        WHEN ROUND((COALESCE(SUM(CASE WHEN COALESCE(te.billable, pt.billable, p.billable, FALSE) THEN te.hours ELSE 0 END), 0) / 40::numeric) * 100, 2) >= 85 THEN 'Near target'
        ELSE 'Under target'
    END AS utilization_position
FROM time_entries te
LEFT JOIN app_users u
  ON u.user_id = te.user_id
LEFT JOIN projects p
  ON p.project_id = te.project_id
LEFT JOIN project_tasks pt
  ON pt.task_id = te.task_id
LEFT JOIN non_project_time_categories npc
  ON npc.non_project_time_category_id = te.non_project_time_category_id
LEFT JOIN team_memberships tmem
  ON tmem.user_id = te.user_id
 AND (tmem.effective_end_date IS NULL OR tmem.effective_end_date >= te.work_date)
LEFT JOIN teams tm
  ON tm.team_id = tmem.team_id
" + ProjectPulse030WhereSql(where) + @"
GROUP BY u.display_name, u.email, tm.team_name, u.team_name
ORDER BY engineer, period_start_date DESC
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.time_entries + utilization fallback joins", columns, sql, parameters);
}

static async Task<object> ProjectPulse030BuildPtoUsedReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("engineer", "Engineer"),
        ("engineer_email", "Engineer Email"),
        ("pto_category", "PTO / Vacation Category"),
        ("work_date", "Date"),
        ("hours", "Hours Used"),
        ("timesheet_status", "Timesheet Status"),
        ("time_entry_status", "Time Entry Status")
    };

    var where = ProjectPulse030CommonBusinessWhere(criteria, "te.work_date");
    where.Sql.Add("(LOWER(COALESCE(npc.category_code, npc.category_name, npc.utilization_bucket, '')) LIKE '%vacation%' OR LOWER(COALESCE(npc.category_code, npc.category_name, npc.utilization_bucket, '')) LIKE '%pto%' OR LOWER(COALESCE(npc.category_code, npc.category_name, npc.utilization_bucket, '')) LIKE '%holiday%')");

    string sql = @"
SELECT
    COALESCE(u.display_name, '') AS engineer,
    COALESCE(u.email, '') AS engineer_email,
    COALESCE(npc.category_name, '') AS pto_category,
    te.work_date,
    te.hours,
    COALESCE(ts.status, '') AS timesheet_status,
    COALESCE(te.status, '') AS time_entry_status
FROM time_entries te
LEFT JOIN app_users u ON u.user_id = te.user_id
LEFT JOIN timesheets ts ON ts.timesheet_id = te.timesheet_id
LEFT JOIN non_project_time_categories npc ON npc.non_project_time_category_id = te.non_project_time_category_id
" + ProjectPulse030WhereSql(where.Sql) + @"
ORDER BY engineer, te.work_date DESC
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.time_entries + PTO category joins", columns, sql, where.Parameters);
}

static async Task<object> ProjectPulse030BuildBillableNonBillableReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("customer", "Customer"),
        ("project_name", "Project"),
        ("engineer", "Engineer"),
        ("team", "Team"),
        ("billable_hours", "Billable Hours"),
        ("non_billable_hours", "Non-Billable Hours"),
        ("total_hours", "Total Hours"),
        ("billable_percent", "Billable Percent")
    };

    var where = ProjectPulse030CommonBusinessWhere(criteria, "te.work_date");

    string sql = @"
SELECT
    COALESCE(c.client_name, 'No customer') AS customer,
    COALESCE(p.project_name, 'Non-project time') AS project_name,
    COALESCE(u.display_name, '') AS engineer,
    COALESCE(tm.team_name, NULLIF(u.team_name, ''), '') AS team,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN te.hours ELSE 0 END), 0) AS billable_hours,
    COALESCE(SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN 0 ELSE te.hours END), 0) AS non_billable_hours,
    COALESCE(SUM(te.hours), 0) AS total_hours,
    CASE WHEN COALESCE(SUM(te.hours), 0) = 0 THEN 0 ELSE ROUND((SUM(CASE WHEN COALESCE(te.billable, p.billable, FALSE) THEN te.hours ELSE 0 END) / SUM(te.hours)) * 100, 2) END AS billable_percent
FROM time_entries te
LEFT JOIN app_users u ON u.user_id = te.user_id
LEFT JOIN projects p ON p.project_id = te.project_id
LEFT JOIN clients c ON c.client_id = p.client_id
LEFT JOIN team_memberships tmem ON tmem.user_id = u.user_id
LEFT JOIN teams tm ON tm.team_id = tmem.team_id
" + ProjectPulse030WhereSql(where.Sql) + @"
GROUP BY c.client_name, p.project_name, u.display_name, tm.team_name, u.team_name
ORDER BY customer, project_name, engineer
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.time_entries + billable rollup", columns, sql, where.Parameters);
}

static async Task<object> ProjectPulse030BuildInvoiceReadinessReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    return await ProjectPulse030BuildReadableTimeReportAsync(connection, reportType, "accounting", criteria);
}

static async Task<object> ProjectPulse030BuildApprovalBottleneckReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("engineer", "Engineer"),
        ("work_date", "Work Date"),
        ("day_status", "Day Status"),
        ("manager", "Manager"),
        ("submitted_at", "Submitted At"),
        ("manager_approved_at", "Manager Approved At"),
        ("pm_approved_at", "PM Approved At"),
        ("accounting_ready_at", "Accounting Ready At"),
        ("bottleneck", "Bottleneck")
    };

    var where = new List<string>();
    var parameters = new Dictionary<string, object>();
    ProjectPulse030AddDateRange(criteria, where, parameters, "tds.work_date");

    string sql = @"
SELECT
    COALESCE(u.display_name, '') AS engineer,
    tds.work_date,
    COALESCE(tds.status, '') AS day_status,
    COALESCE(m.display_name, '') AS manager,
    tds.submitted_at,
    tds.manager_approved_at,
    tds.pm_approved_at,
    tds.accounting_ready_at,
    CASE
        WHEN tds.submitted_at IS NULL THEN 'Not submitted'
        WHEN tds.manager_approved_at IS NULL THEN 'Manager approval pending'
        WHEN tds.pm_approved_at IS NULL THEN 'PM approval pending'
        WHEN tds.accounting_ready_at IS NULL THEN 'Accounting readiness pending'
        ELSE 'Ready'
    END AS bottleneck
FROM timesheet_day_statuses tds
LEFT JOIN app_users u ON u.user_id = tds.user_id
LEFT JOIN app_users m ON m.user_id = tds.manager_user_id
" + ProjectPulse030WhereSql(where) + @"
ORDER BY tds.work_date DESC, engineer
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.timesheet_day_statuses + users", columns, sql, parameters);
}

static async Task<object> ProjectPulse030BuildMissingTimeReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var columns = new List<(string Key, string Label)>
    {
        ("engineer", "Engineer"),
        ("engineer_email", "Engineer Email"),
        ("week_start_date", "Week Start Date"),
        ("week_end_date", "Week End Date"),
        ("timesheet_status", "Timesheet Status"),
        ("total_hours", "Total Hours"),
        ("missing_hours", "Missing Hours")
    };

    var where = new List<string>();
    var parameters = new Dictionary<string, object>();
    ProjectPulse030AddDateRange(criteria, where, parameters, "ts.week_start_date");

    string sql = @"
SELECT
    COALESCE(u.display_name, '') AS engineer,
    COALESCE(u.email, '') AS engineer_email,
    ts.week_start_date,
    ts.week_end_date,
    COALESCE(ts.status, '') AS timesheet_status,
    COALESCE(SUM(te.hours), 0) AS total_hours,
    GREATEST(40 - COALESCE(SUM(te.hours), 0), 0) AS missing_hours
FROM timesheets ts
LEFT JOIN app_users u ON u.user_id = ts.user_id
LEFT JOIN time_entries te ON te.timesheet_id = ts.timesheet_id
" + ProjectPulse030WhereSql(where) + @"
GROUP BY u.display_name, u.email, ts.week_start_date, ts.week_end_date, ts.status
HAVING COALESCE(SUM(te.hours), 0) < 40
ORDER BY ts.week_start_date DESC, engineer
LIMIT 250;";

    return await ProjectPulse030ExecuteReadableReportAsync(connection, reportType, category, "public.timesheets + time_entries", columns, sql, parameters);
}

static async Task<object> ProjectPulse030BuildProjectMarginReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    return await ProjectPulse030BuildProjectStatusBilledRemainingReportAsync(connection, reportType, category, criteria);
}

static async Task<object> ProjectPulse030BuildRateExceptionReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    var report = await ProjectPulse030BuildReadableTimeReportAsync(connection, reportType, "accounting", criteria);
    return report;
}

static async Task<object> ProjectPulse030BuildCustomerProfitabilityReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    return await ProjectPulse030BuildReadableCustomerReportAsync(connection, reportType, category, criteria);
}

static async Task<object> ProjectPulse030BuildCloseoutReadinessReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    return await ProjectPulse030BuildProjectStatusBilledRemainingReportAsync(connection, reportType, category, criteria);
}

static async Task<object> ProjectPulse030BuildHandoffQualityReportAsync(NpgsqlConnection connection, string reportType, string category, JsonElement criteria)
{
    return await ProjectPulse030BuildReadableProjectReportAsync(connection, reportType, category, criteria);
}

static (List<string> Sql, Dictionary<string, object> Parameters) ProjectPulse030CommonBusinessWhere(JsonElement criteria, string dateExpression)
{
    var where = new List<string>();
    var parameters = new Dictionary<string, object>();

    ProjectPulse030AddDateRange(criteria, where, parameters, dateExpression);
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "customer", new[] { "c.client_name", "c.client_code" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "project", new[] { "p.project_name", "p.project_code" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "pm", new[] { "pm.display_name", "pm.email" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "engineer", new[] { "u.display_name", "u.email" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "selectedEngineers", new[] { "u.display_name", "u.email" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "team", new[] { "tm.team_name", "u.team_name", "u.department", "u.department_name" });
    ProjectPulse030AddBillableFilter(criteria, where, "COALESCE(te.billable, pt.billable, p.billable, FALSE)");
    ProjectPulse030AddContractTypeFilter(criteria, where);

    return (where, parameters);
}

static (List<string> Sql, Dictionary<string, object> Parameters) ProjectPulse030CommonProjectWhere(JsonElement criteria)
{
    var where = new List<string>();
    var parameters = new Dictionary<string, object>();

    ProjectPulse030AddDateRange(criteria, where, parameters, "COALESCE(p.start_date, p.created_at::date)");
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "customer", new[] { "c.client_name", "c.client_code" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "project", new[] { "p.project_name", "p.project_code" });
    ProjectPulse030AddReadableTextFilter(criteria, where, parameters, "pm", new[] { "pm.display_name", "pm.email" });
    ProjectPulse030AddBillableFilter(criteria, where, "COALESCE(p.billable, FALSE)");

    return (where, parameters);
}

// 030B_INVOICE_EXPORT_BUSINESS_REPORTS_END

// 030A_READABLE_REPORTING_JOINED_HELPERS_END

static object ProjectPulse030SafeFormatValue(object value)
{
    if (value is DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    if (value is DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss zzz");
    }

    if (value is bool boolValue)
    {
        return boolValue ? "Yes" : "No";
    }

    return value;
}

static string ProjectPulse030Display(string columnName)
{
    string cleaned = columnName.Replace("_", " ").Replace("-", " ").Trim();

    if (string.IsNullOrWhiteSpace(cleaned))
    {
        return columnName;
    }

    return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part.Substring(1)));
}

static string ProjectPulse030Quote(string value)
{
    return "\"" + value.Replace("\"", "\"\"") + "\"";
}

static async Task<(string Schema, string Name)> ProjectPulse030FindSafeReportTableAsync(NpgsqlConnection connection, string category)
{
    var tables = new List<(string Schema, string Name, long Estimate)>();

    await using (var command = new NpgsqlCommand(@"
SELECT
    schemaname,
    relname,
    COALESCE(n_live_tup, 0)::bigint AS row_estimate
FROM pg_stat_user_tables
ORDER BY schemaname, relname;", connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            tables.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }
    }

    string[] preferred = ProjectPulse030PreferredSafeTokens(category);
    string[] allowedReportingTables = ProjectPulse030AllowedReportingTables(category);

    var scored = tables
        .Select(t => new
        {
            Table = t,
            Score = ProjectPulse030ScoreSafeTable(category, t.Name, preferred, allowedReportingTables, t.Estimate)
        })
        .Where(x => x.Score > 0)
        .OrderByDescending(x => x.Score)
        .ThenByDescending(x => x.Table.Estimate)
        .ThenBy(x => x.Table.Name)
        .ToList();

    if (scored.Count == 0)
    {
        return ("", "");
    }

    return (scored[0].Table.Schema, scored[0].Table.Name);
}

static string[] ProjectPulse030PreferredSafeTokens(string category)
{
    return category switch
    {
        "accounting" => new[] { "invoice", "billing", "export", "time_entry", "time_entries", "timesheet" },
        "time" => new[] { "time_entry", "time_entries", "timesheet", "time" },
        "customer" => new[] { "customer", "customers" },
        "project" => new[] { "project", "projects" },
        "pm" => new[] { "project", "assignment", "time_entry", "time_entries", "user", "users" },
        "engineer" => new[] { "time_entry", "time_entries", "timesheet", "user", "users", "assignment" },
        "team" => new[] { "team", "teams", "user", "users", "time_entry", "time_entries" },
        "audit" => new[] { "audit", "event", "events", "approval", "export" },
        "system" => new[] { "reporting_system_health_catalog" },
        "api" => new[] { "reporting_api_status_catalog" },
        "external" => new[] { "reporting_external_connection_catalog" },
        "auth" => new[] { "session", "auth", "login", "view_as", "audit" },
        "ai" => new[] { "sow_ai_time_entry_drafts", "sow_ai_time_entry_scope_checks", "sow_ai_time_entry_ai_provider_readiness" },
        "notification" => new[] { "production_notification_events", "time_compliance_notification_delivery_events", "system_email_provider_test_events" },
        "uat" => new[] { "uat_evidence_capture_events", "uat_workflow_validation_scenarios", "uat_role_validation_matrix" },
        "library" => new[] { "reporting_saved_report_definitions", "reporting_templates" },
        "executive" => new[] { "reporting_execution_events", "reporting_data_domains", "project", "time_entry", "invoice" },
        _ => new[] { "time_entry", "project", "customer" }
    };
}

static string[] ProjectPulse030AllowedReportingTables(string category)
{
    return category switch
    {
        "system" => new[] { "reporting_system_health_catalog" },
        "api" => new[] { "reporting_api_status_catalog" },
        "external" => new[] { "reporting_external_connection_catalog" },
        "library" => new[] { "reporting_saved_report_definitions", "reporting_templates" },
        "executive" => new[] { "reporting_execution_events", "reporting_data_domains" },
        _ => Array.Empty<string>()
    };
}

static int ProjectPulse030ScoreSafeTable(string category, string tableName, string[] preferredTokens, string[] allowedReportingTables, long rowEstimate)
{
    string name = tableName.ToLowerInvariant();
    int score = 0;

    if (name.StartsWith("reporting_") && !allowedReportingTables.Contains(name))
    {
        return 0;
    }

    foreach (var token in preferredTokens)
    {
        string loweredToken = token.ToLowerInvariant();

        if (name == loweredToken)
        {
            score += 100;
        }
        else if (name.Contains(loweredToken))
        {
            score += 35;
        }
    }

    if (rowEstimate > 0)
    {
        score += 15;
    }

    if (name.Contains("archive") || name.Contains("backup") || name.Contains("test"))
    {
        score -= 30;
    }

    return score;
}

static async Task<List<(string Name, string DataType)>> ProjectPulse030GetSafeColumnsAsync(NpgsqlConnection connection, string schema, string table)
{
    // 030_REPORT_API_GET_COLUMNS_PG_CATALOG_FIX
    // Use pg_catalog first, then information_schema as a fallback.
    var columns = new List<(string Name, string DataType)>();

    await using (var command = new NpgsqlCommand(@"
SELECT
    a.attname AS column_name,
    pg_catalog.format_type(a.atttypid, a.atttypmod) AS data_type
FROM pg_catalog.pg_attribute a
JOIN pg_catalog.pg_class c
  ON c.oid = a.attrelid
JOIN pg_catalog.pg_namespace n
  ON n.oid = c.relnamespace
WHERE n.nspname = @schema
  AND c.relname = @table
  AND a.attnum > 0
  AND NOT a.attisdropped
ORDER BY a.attnum;", connection))
    {
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns.Add((reader.GetString(0), reader.GetString(1)));
        }
    }

    if (columns.Count > 0)
    {
        return columns;
    }

    await using (var fallbackCommand = new NpgsqlCommand(@"
SELECT column_name, data_type
FROM information_schema.columns
WHERE table_schema = @schema
  AND table_name = @table
ORDER BY ordinal_position;", connection))
    {
        fallbackCommand.Parameters.AddWithValue("schema", schema);
        fallbackCommand.Parameters.AddWithValue("table", table);

        await using var fallbackReader = await fallbackCommand.ExecuteReaderAsync();

        while (await fallbackReader.ReadAsync())
        {
            columns.Add((fallbackReader.GetString(0), fallbackReader.GetString(1)));
        }
    }

    return columns;
}

static List<(string Name, string DataType)> ProjectPulse030SelectSafeColumns(string category, List<(string Name, string DataType)> columns)
{
    string[] preferred = category switch
    {
        "accounting" => new[] { "invoice", "customer", "project", "engagement", "contract", "po", "quote", "description", "hours", "quantity", "rate", "amount", "work", "location", "status", "date", "created", "approved", "exported" },
        "time" => new[] { "time", "entry", "date", "customer", "project", "engineer", "user", "hours", "status", "billable", "description", "notes", "approved", "submitted", "created" },
        "customer" => new[] { "customer", "name", "status", "created", "updated" },
        "project" => new[] { "project", "customer", "pm", "manager", "status", "sow", "gsd", "assignment", "created", "updated" },
        "pm" => new[] { "pm", "project_manager", "manager", "project", "customer", "status", "created", "updated" },
        "engineer" => new[] { "engineer", "user", "display", "email", "team", "project", "hours", "status", "created", "updated" },
        "team" => new[] { "team", "user", "engineer", "manager", "hours", "status", "created", "updated" },
        "audit" => new[] { "event", "audit", "actor", "role", "action", "status", "created", "timestamp", "notes" },
        "system" => new[] { "component", "health", "status", "notes", "created" },
        "api" => new[] { "api", "path", "module", "status", "success", "created" },
        "external" => new[] { "connection", "provider", "type", "owner", "status", "created" },
        "auth" => new[] { "auth", "session", "user", "role", "status", "created" },
        "ai" => new[] { "ai", "sow", "scope", "draft", "engineer", "project", "status", "created" },
        "notification" => new[] { "notification", "provider", "recipient", "status", "created" },
        "uat" => new[] { "scenario", "role", "workflow", "evidence", "status", "created" },
        "library" => new[] { "report", "template", "criteria", "owner", "cadence", "format", "status", "created" },
        "executive" => new[] { "domain", "name", "description", "audience", "owner", "created" },
        _ => new[] { "id", "name", "status", "created" }
    };

    var selected = columns
        .Select(c => new
        {
            Column = c,
            Score = preferred.Any(p => c.Name.Contains(p, StringComparison.OrdinalIgnoreCase)) ? 20 : 0
        })
        .OrderByDescending(x => x.Score)
        .ThenBy(x => columns.FindIndex(c => c.Name == x.Column.Name))
        .Select(x => x.Column)
        .Take(24)
        .ToList();

    if (selected.Count == 0)
    {
        selected = columns.Take(24).ToList();
    }

    return selected;
}

static (string Sql, Dictionary<string, object> Parameters) ProjectPulse030BuildSafeWhereClause(JsonElement criteria, List<(string Name, string DataType)> columns)
{
    var whereParts = new List<string>();
    var parameters = new Dictionary<string, object>();

    void AddTextFilter(string fieldName, string[] columnTokens)
    {
        string value = ProjectPulse030SafeReadString(criteria, fieldName);

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var matchingColumns = columns
            .Where(c => columnTokens.Any(token => c.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Take(4)
            .ToList();

        if (matchingColumns.Count == 0)
        {
            return;
        }

        string parameterName = "p" + parameters.Count;
        parameters[parameterName] = "%" + value + "%";

        whereParts.Add("(" + string.Join(" OR ", matchingColumns.Select(c => ProjectPulse030Quote(c.Name) + "::text ILIKE @" + parameterName)) + ")");
    }

    AddTextFilter("customer", new[] { "customer", "client" });
    AddTextFilter("project", new[] { "project", "engagement" });
    AddTextFilter("pm", new[] { "pm", "project_manager", "manager" });
    AddTextFilter("engineer", new[] { "engineer", "user", "employee", "display", "email" });
    AddTextFilter("team", new[] { "team", "department" });
    AddTextFilter("contractType", new[] { "contract" });
    AddTextFilter("timeEntryStatus", new[] { "status" });
    AddTextFilter("approvalStatus", new[] { "approval", "status" });
    AddTextFilter("invoiceStatus", new[] { "invoice", "status" });
    AddTextFilter("workCode", new[] { "work_code", "work" });
    AddTextFilter("workLocation", new[] { "location" });

    return (whereParts.Count == 0 ? "" : " WHERE " + string.Join(" AND ", whereParts), parameters);
}

// 030_REPORT_API_JSON_SAFE_HELPERS_END
/* 038A_CERTIFY_PLACEHOLDER_ENDPOINTS_START */
var projectPulse038ACertifyRequiredKeys = new[]
{
    "CERTIFY_BASE_URL",
    "CERTIFY_AUTH_MODE",
    "CERTIFY_COMPANY_ID"
};

bool ProjectPulse038ACertifyHasValue(string key)
{
    return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key));
}

object ProjectPulse038ACertifyConfigSnapshot()
{
    var missingKeys = projectPulse038ACertifyRequiredKeys
        .Where(key => !ProjectPulse038ACertifyHasValue(key))
        .ToArray();

    var authMode = Environment.GetEnvironmentVariable("CERTIFY_AUTH_MODE") ?? "placeholder";
    var dryRunOnly = string.Equals(Environment.GetEnvironmentVariable("CERTIFY_DRY_RUN_ONLY") ?? "true", "true", StringComparison.OrdinalIgnoreCase);

    return new
    {
        status = "placeholder",
        connector = "Certify",
        configured = missingKeys.Length == 0,
        canRunLiveSync = false,
        dryRunOnly,
        authMode,
        missingConfigKeys = missingKeys,
        safeConfig = new
        {
            baseUrlConfigured = ProjectPulse038ACertifyHasValue("CERTIFY_BASE_URL"),
            apiKeyConfigured = ProjectPulse038ACertifyHasValue("CERTIFY_API_KEY"),
            clientIdConfigured = ProjectPulse038ACertifyHasValue("CERTIFY_CLIENT_ID"),
            clientSecretConfigured = ProjectPulse038ACertifyHasValue("CERTIFY_CLIENT_SECRET"),
            companyIdConfigured = ProjectPulse038ACertifyHasValue("CERTIFY_COMPANY_ID"),
            tokenUrlConfigured = ProjectPulse038ACertifyHasValue("CERTIFY_TOKEN_URL"),
            syncLookbackDays = Environment.GetEnvironmentVariable("CERTIFY_SYNC_LOOKBACK_DAYS") ?? "45",
            approvedOnly = Environment.GetEnvironmentVariable("CERTIFY_SYNC_APPROVED_ONLY") ?? "true",
            includeReceipts = Environment.GetEnvironmentVariable("CERTIFY_SYNC_INCLUDE_RECEIPTS") ?? "true",
            projectCodeField = Environment.GetEnvironmentVariable("CERTIFY_PROJECT_CODE_FIELD") ?? "ProjectCode",
            customerField = Environment.GetEnvironmentVariable("CERTIFY_CUSTOMER_FIELD") ?? "Customer",
            billableFlagField = Environment.GetEnvironmentVariable("CERTIFY_BILLABLE_FLAG_FIELD") ?? "Billable"
        },
        message = "Certify placeholders are ready. Live sync remains disabled until real server-side credentials and connector logic are implemented.",
        generatedAt = System.DateTimeOffset.UtcNow
    };
}

var projectPulse038APlaceholderExpenses = new[]
{
    new
    {
        certifyReportId = "CERTIFY-PLACEHOLDER-001",
        employeeEmail = "employee@example.com",
        customerName = "Customer mapping pending",
        projectCode = "PROJECT-CODE-PENDING",
        reportStatus = "Approved placeholder",
        expenseCategory = "Travel",
        amount = 0.00m,
        currency = "USD",
        billable = true,
        mappingStatus = "Placeholder only",
        billingStatus = "Not ready for billing"
    },
    new
    {
        certifyReportId = "CERTIFY-PLACEHOLDER-002",
        employeeEmail = "consultant@example.com",
        customerName = "Customer mapping pending",
        projectCode = "PROJECT-CODE-PENDING",
        reportStatus = "Approved placeholder",
        expenseCategory = "Meals",
        amount = 0.00m,
        currency = "USD",
        billable = true,
        mappingStatus = "Placeholder only",
        billingStatus = "Not ready for billing"
    }
};

var projectPulse038APlaceholderExceptions = new[]
{
    new
    {
        exceptionCode = "MISSING_PROJECT_MAPPING",
        severity = "High",
        message = "Certify project/customer field must map to a Pulse project before the expense can be billed.",
        resolution = "Confirm the Certify custom field that stores project code, customer, job number, or cost center."
    },
    new
    {
        exceptionCode = "MISSING_EMPLOYEE_MAPPING",
        severity = "Medium",
        message = "Certify employee identity must map to an active Pulse user.",
        resolution = "Map Certify employee email or employee ID to the Pulse user directory."
    },
    new
    {
        exceptionCode = "MISSING_CATEGORY_MAPPING",
        severity = "Medium",
        message = "Certify expense category must map to a Pulse billing/accounting category.",
        resolution = "Create category mapping for billable, reimbursable, non-billable, and receipt-required categories."
    }
};

app.MapGet("/api/certify/config-placeholder", () => ProjectPulse038ACertifyConfigSnapshot());

app.MapGet("/api/certify/sync/status", () => new
{
    status = "placeholder_ready",
    connector = "Certify",
    canRunLiveSync = false,
    lastSyncStatus = "Not run",
    lastSyncAt = (string?)null,
    lastPreviewAt = (string?)null,
    stagedExpenseCount = projectPulse038APlaceholderExpenses.Length,
    exceptionCount = projectPulse038APlaceholderExceptions.Length,
    message = "Placeholder sync endpoints are available. Live Certify API calls are not enabled yet.",
    config = ProjectPulse038ACertifyConfigSnapshot(),
    generatedAt = System.DateTimeOffset.UtcNow
});

app.MapGet("/api/certify/expenses/staged", () => new
{
    status = "placeholder",
    count = projectPulse038APlaceholderExpenses.Length,
    stagedExpenses = projectPulse038APlaceholderExpenses,
    message = "These are placeholder records showing the future staged expense shape. They are not imported from Certify.",
    generatedAt = System.DateTimeOffset.UtcNow
});

app.MapGet("/api/certify/exceptions", () => new
{
    status = "placeholder",
    count = projectPulse038APlaceholderExceptions.Length,
    exceptions = projectPulse038APlaceholderExceptions,
    message = "These are placeholder exception types expected during future Certify expense import.",
    generatedAt = System.DateTimeOffset.UtcNow
});

app.MapPost("/api/certify/test-connection", () => new
{
    status = "placeholder_connection_test",
    connector = "Certify",
    success = false,
    canRunLiveSync = false,
    message = "Connection test placeholder completed. Real test requires Certify API credentials and backend connector implementation.",
    config = ProjectPulse038ACertifyConfigSnapshot(),
    generatedAt = System.DateTimeOffset.UtcNow
});

app.MapPost("/api/certify/sync/preview", () => new
{
    status = "placeholder_preview_ready",
    connector = "Certify",
    mode = "dry_run",
    canRunLiveSync = false,
    stagedExpenseCount = projectPulse038APlaceholderExpenses.Length,
    exceptionCount = projectPulse038APlaceholderExceptions.Length,
    stagedExpenses = projectPulse038APlaceholderExpenses,
    exceptions = projectPulse038APlaceholderExceptions,
    message = "Preview placeholder generated. Real preview will call Certify, stage approved expenses, and flag mapping exceptions.",
    generatedAt = System.DateTimeOffset.UtcNow
});

app.MapPost("/api/certify/sync/run", () => new
{
    status = "placeholder_run_blocked",
    connector = "Certify",
    mode = "dry_run_only",
    canRunLiveSync = false,
    stagedExpenseCount = projectPulse038APlaceholderExpenses.Length,
    exceptionCount = projectPulse038APlaceholderExceptions.Length,
    stagedExpenses = projectPulse038APlaceholderExpenses,
    exceptions = projectPulse038APlaceholderExceptions,
    message = "Live Certify sync is intentionally blocked. Add real credentials, staging tables, and connector logic before enabling imports.",
    generatedAt = System.DateTimeOffset.UtcNow
});
/* 038A_CERTIFY_PLACEHOLDER_ENDPOINTS_END */










/* 041M_CLOSEOUT_EMAIL_AUDIT_HELPERS_START */
static string ProjectPulse041MJsonString(System.Text.Json.JsonElement root, string propertyName)
{
    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return string.Empty;
    if (!root.TryGetProperty(propertyName, out var value)) return string.Empty;

    if (value.ValueKind == System.Text.Json.JsonValueKind.String)
    {
        return value.GetString() ?? string.Empty;
    }

    if (value.ValueKind == System.Text.Json.JsonValueKind.Null || value.ValueKind == System.Text.Json.JsonValueKind.Undefined)
    {
        return string.Empty;
    }

    return value.ToString() ?? string.Empty;
}

static bool ProjectPulse041MJsonBool(System.Text.Json.JsonElement root, string propertyName)
{
    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
    if (!root.TryGetProperty(propertyName, out var value)) return false;

    if (value.ValueKind == System.Text.Json.JsonValueKind.True) return true;
    if (value.ValueKind == System.Text.Json.JsonValueKind.False) return false;

    if (value.ValueKind == System.Text.Json.JsonValueKind.String
        && bool.TryParse(value.GetString(), out var parsed))
    {
        return parsed;
    }

    return false;
}

static int ProjectPulse041MJsonInt(System.Text.Json.JsonElement root, string propertyName)
{
    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return 0;
    if (!root.TryGetProperty(propertyName, out var value)) return 0;

    if (value.ValueKind == System.Text.Json.JsonValueKind.Number
        && value.TryGetInt32(out var parsedNumber))
    {
        return parsedNumber;
    }

    if (value.ValueKind == System.Text.Json.JsonValueKind.String
        && int.TryParse(value.GetString(), out var parsedString))
    {
        return parsedString;
    }

    return 0;
}

static System.Collections.Generic.List<object> ProjectPulse041MJsonRecipients(System.Text.Json.JsonElement root, string propertyName)
{
    var recipients = new System.Collections.Generic.List<object>();

    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return recipients;
    if (!root.TryGetProperty(propertyName, out var arrayElement)) return recipients;
    if (arrayElement.ValueKind != System.Text.Json.JsonValueKind.Array) return recipients;

    foreach (var item in arrayElement.EnumerateArray())
    {
        if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

        recipients.Add(new
        {
            role = ProjectPulse041MJsonString(item, "Role") is { Length: > 0 } roleUpper
                ? roleUpper
                : ProjectPulse041MJsonString(item, "role"),
            name = ProjectPulse041MJsonString(item, "Name") is { Length: > 0 } nameUpper
                ? nameUpper
                : ProjectPulse041MJsonString(item, "name"),
            email = ProjectPulse041MJsonString(item, "Email") is { Length: > 0 } emailUpper
                ? emailUpper
                : ProjectPulse041MJsonString(item, "email")
        });
    }

    return recipients;
}
/* 041M_CLOSEOUT_EMAIL_AUDIT_HELPERS_END */

static string ProjectPulse041AGetJsonString(System.Text.Json.JsonElement root, string propertyName)
{
    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return string.Empty;

    if (!root.TryGetProperty(propertyName, out var value)) return string.Empty;

    if (value.ValueKind == System.Text.Json.JsonValueKind.String)
    {
        return value.GetString() ?? string.Empty;
    }

    return value.ToString() ?? string.Empty;
}

static System.Collections.Generic.List<(string Role, string Name, string Email)> ProjectPulse041AExtractRecipients(System.Text.Json.JsonElement root)
{
    return ProjectPulse041AExtractRecipientArray(root, "recipients", "Project Team");
}

static System.Collections.Generic.List<(string Role, string Name, string Email)> ProjectPulse041AExtractCcRecipients(System.Text.Json.JsonElement root)
{
    return ProjectPulse041AExtractRecipientArray(root, "ccRecipients", "CC");
}

static System.Collections.Generic.List<(string Role, string Name, string Email)> ProjectPulse041AExtractRecipientArray(System.Text.Json.JsonElement root, string propertyName, string defaultRole)
{
    var recipients = new System.Collections.Generic.List<(string Role, string Name, string Email)>();

    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return recipients;
    if (!root.TryGetProperty(propertyName, out var recipientsElement)) return recipients;
    if (recipientsElement.ValueKind != System.Text.Json.JsonValueKind.Array) return recipients;

    foreach (var item in recipientsElement.EnumerateArray())
    {
        if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

        var role = ProjectPulse041AGetJsonString(item, "role");
        var name = ProjectPulse041AGetJsonString(item, "name");
        var email = ProjectPulse041AGetJsonString(item, "email");

        recipients.Add((
            Role: string.IsNullOrWhiteSpace(role) ? defaultRole : role,
            Name: string.IsNullOrWhiteSpace(name) ? email : name,
            Email: email));
    }

    return recipients;
}

static bool ProjectPulse041AIsEmail(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return false;

    return System.Text.RegularExpressions.Regex.IsMatch(
        value.Trim(),
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

static string ProjectPulse041ASafeFilePart(string value)
{
    var normalized = System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, @"[^A-Za-z0-9_.-]+", "-").Trim('-');
    return string.IsNullOrWhiteSpace(normalized) ? "project" : normalized;
}


/* 041B_BREVO_API_HELPERS_START */
static async System.Threading.Tasks.Task<(bool Sent, string Status, string Detail, string? OutboxPath)> ProjectPulse041BSendBrevoApiEmailAsync(
    System.Collections.Generic.List<(string Role, string Name, string Email)> recipients,
    System.Collections.Generic.List<(string Role, string Name, string Email)> ccRecipients,
    string subject,
    string body,
    string projectCode,
    string customerName,
    string brevoApiKey)
{
    var apiUrl = System.Environment.GetEnvironmentVariable("PROJECTPULSE_BREVO_API_URL");

    if (string.IsNullOrWhiteSpace(apiUrl))
    {
        apiUrl = "https://api.brevo.com/v3/smtp/email";
    }

    var senderEmail = System.Environment.GetEnvironmentVariable("PROJECTPULSE_BREVO_SENDER_EMAIL")
        ?? System.Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_FROM")
        ?? System.Environment.GetEnvironmentVariable("SMTP_FROM")
        ?? "project-health-dashboard@localhost";

    var senderName = System.Environment.GetEnvironmentVariable("PROJECTPULSE_BREVO_SENDER_NAME")
        ?? "Pulse";

    /* 041L_BREVO_OMIT_EMPTY_CC_START */
    var brevoToRecipients = new System.Text.Json.Nodes.JsonArray();

    foreach (var recipient in recipients.Where(recipient => ProjectPulse041AIsEmail(recipient.Email)))
    {
        brevoToRecipients.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["email"] = recipient.Email,
            ["name"] = string.IsNullOrWhiteSpace(recipient.Name) ? recipient.Email : recipient.Name
        });
    }

    var payload = new System.Text.Json.Nodes.JsonObject
    {
        ["sender"] = new System.Text.Json.Nodes.JsonObject
        {
            ["email"] = senderEmail,
            ["name"] = senderName
        },
        ["to"] = brevoToRecipients,
        ["subject"] = subject,
        ["textContent"] = body
    };

    var brevoCcRecipients = new System.Text.Json.Nodes.JsonArray();

    foreach (var recipient in ccRecipients.Where(recipient => ProjectPulse041AIsEmail(recipient.Email)))
    {
        brevoCcRecipients.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["email"] = recipient.Email,
            ["name"] = string.IsNullOrWhiteSpace(recipient.Name) ? recipient.Email : recipient.Name
        });
    }

    if (brevoCcRecipients.Count > 0)
    {
        payload["cc"] = brevoCcRecipients;
    }
    /* 041L_BREVO_OMIT_EMPTY_CC_END */

    if (brevoToRecipients.Count == 0)
    {
        return (false, "no_recipients", "No email-ready recipients were available for Brevo API delivery.", null);
    }

    var json = System.Text.Json.JsonSerializer.Serialize(payload);

    using var httpClient = new System.Net.Http.HttpClient();
    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, apiUrl);

    request.Headers.TryAddWithoutValidation("api-key", brevoApiKey);
    request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    request.Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

    try
    {
        using var response = await httpClient.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            return (true, "sent_brevo_api", $"Automatic closeout email sent through Brevo API. Response: {responseText}", null);
        }

        var fallback = await ProjectPulse041AWriteOutboxEmailAsync(recipients, ccRecipients, subject, body, projectCode, "brevo-api-failed");
        return (false, "queued_brevo_api_failed", $"Brevo API returned HTTP {(int)response.StatusCode}: {responseText}", fallback);
    }
    catch (System.Exception ex)
    {
        var fallback = await ProjectPulse041AWriteOutboxEmailAsync(recipients, ccRecipients, subject, body, projectCode, "brevo-api-exception");
        return (false, "queued_brevo_api_exception", $"Brevo API delivery failed and the message was written to outbox: {ex.Message}", fallback);
    }
}
/* 041B_BREVO_API_HELPERS_END */

static async System.Threading.Tasks.Task<(bool Sent, string Status, string Detail, string? OutboxPath)> ProjectPulse041ASendCloseoutEmailAsync(
    System.Collections.Generic.List<(string Role, string Name, string Email)> recipients,
    System.Collections.Generic.List<(string Role, string Name, string Email)> ccRecipients,
    string subject,
    string body,
    string projectCode,
    string customerName)
{
    /* 041B_BREVO_API_EMAIL_DELIVERY_START */
    var brevoApiKey = System.Environment.GetEnvironmentVariable("PROJECTPULSE_BREVO_API_KEY")
        ?? System.Environment.GetEnvironmentVariable("BREVO_API_KEY");

    if (!string.IsNullOrWhiteSpace(brevoApiKey))
    {
        return await ProjectPulse041BSendBrevoApiEmailAsync(recipients, ccRecipients, subject, body, projectCode, customerName, brevoApiKey);
    }
    /* 041B_BREVO_API_EMAIL_DELIVERY_END */

    var smtpHost = System.Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_HOST")
        ?? System.Environment.GetEnvironmentVariable("SMTP_HOST");

    var smtpFrom = System.Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_FROM")
        ?? System.Environment.GetEnvironmentVariable("SMTP_FROM")
        ?? "project-health-dashboard@localhost";

    if (!string.IsNullOrWhiteSpace(smtpHost))
    {
        var smtpPortText = System.Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PORT")
            ?? System.Environment.GetEnvironmentVariable("SMTP_PORT")
            ?? "25";

        var smtpPort = int.TryParse(smtpPortText, out var parsedPort) ? parsedPort : 25;
        var smtpUser = System.Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_USER")
            ?? System.Environment.GetEnvironmentVariable("SMTP_USER");
        var smtpPassword = System.Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_PASSWORD")
            ?? System.Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        var smtpSslText = System.Environment.GetEnvironmentVariable("PROJECTPULSE_SMTP_SSL")
            ?? System.Environment.GetEnvironmentVariable("SMTP_SSL")
            ?? "false";

        using var message = new System.Net.Mail.MailMessage
        {
            From = new System.Net.Mail.MailAddress(smtpFrom),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };

        foreach (var recipient in recipients)
        {
            message.To.Add(new System.Net.Mail.MailAddress(recipient.Email, string.IsNullOrWhiteSpace(recipient.Name) ? recipient.Email : recipient.Name));
        }

        foreach (var recipient in ccRecipients)
        {
            message.CC.Add(new System.Net.Mail.MailAddress(recipient.Email, string.IsNullOrWhiteSpace(recipient.Name) ? recipient.Email : recipient.Name));
        }

        using var smtp = new System.Net.Mail.SmtpClient(smtpHost, smtpPort)
        {
            EnableSsl = string.Equals(smtpSslText, "true", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(smtpSslText, "1", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(smtpSslText, "yes", System.StringComparison.OrdinalIgnoreCase)
        };

        if (!string.IsNullOrWhiteSpace(smtpUser))
        {
            smtp.Credentials = new System.Net.NetworkCredential(smtpUser, smtpPassword ?? string.Empty);
        }

        try
        {
            await smtp.SendMailAsync(message);
            return (true, "sent", "Automatic closeout email sent through configured SMTP.", null);
        }
        catch (System.Exception ex)
        {
            var fallback = await ProjectPulse041AWriteOutboxEmailAsync(recipients, ccRecipients, subject, body, projectCode, "smtp-failed");
            return (false, "queued_smtp_failed", $"SMTP send failed and the message was written to outbox: {ex.Message}", fallback);
        }
    }

    var sendmailPath = System.Environment.GetEnvironmentVariable("PROJECTPULSE_SENDMAIL_PATH") ?? "/usr/sbin/sendmail";

    if (System.IO.File.Exists(sendmailPath))
    {
        var rawEmail = ProjectPulse041ABuildRawEmail(recipients, ccRecipients, smtpFrom, subject, body);

        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sendmailPath,
                Arguments = "-t -oi",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = System.Diagnostics.Process.Start(processInfo);

            if (process is not null)
            {
                await process.StandardInput.WriteAsync(rawEmail);
                process.StandardInput.Close();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    return (true, "sent", "Automatic closeout email sent through local sendmail.", null);
                }

                var fallback = await ProjectPulse041AWriteOutboxEmailAsync(recipients, ccRecipients, subject, body, projectCode, "sendmail-failed");
                return (false, "queued_sendmail_failed", $"sendmail exited with code {process.ExitCode}: {error}", fallback);
            }
        }
        catch (System.Exception ex)
        {
            var fallback = await ProjectPulse041AWriteOutboxEmailAsync(recipients, ccRecipients, subject, body, projectCode, "sendmail-exception");
            return (false, "queued_sendmail_exception", $"sendmail failed and the message was written to outbox: {ex.Message}", fallback);
        }
    }

    var outboxPath = await ProjectPulse041AWriteOutboxEmailAsync(recipients, ccRecipients, subject, body, projectCode, "missing-mailer");
    return (false, "queued_not_sent_missing_mailer", "No SMTP host or sendmail binary is configured. Message was written to the closeout email outbox.", outboxPath);
}

static string ProjectPulse041ASanitizeHeaderValue(string? value)
{
    return System.Text.RegularExpressions.Regex
        .Replace(value ?? string.Empty, @"[\r\n]+", " ")
        .Trim();
}

static string ProjectPulse041ABuildRawEmail(
    System.Collections.Generic.List<(string Role, string Name, string Email)> recipients,
    System.Collections.Generic.List<(string Role, string Name, string Email)> ccRecipients,
    string from,
    string subject,
    string body)
{
    var builder = new System.Text.StringBuilder();

    builder.AppendLine($"From: {ProjectPulse041ASanitizeHeaderValue(from)}");
    builder.AppendLine($"To: {string.Join(", ", recipients.Select(recipient => ProjectPulse041ASanitizeHeaderValue(recipient.Email)))}");

    if (ccRecipients.Count > 0)
    {
        builder.AppendLine($"Cc: {string.Join(", ", ccRecipients.Select(recipient => ProjectPulse041ASanitizeHeaderValue(recipient.Email)))}");
    }

    builder.AppendLine($"Subject: {ProjectPulse041ASanitizeHeaderValue(subject)}");
    builder.AppendLine("MIME-Version: 1.0");
    builder.AppendLine("Content-Type: text/plain; charset=utf-8");
    builder.AppendLine();
    builder.AppendLine(body);

    return builder.ToString();
}

static async System.Threading.Tasks.Task<string> ProjectPulse041AWriteOutboxEmailAsync(
    System.Collections.Generic.List<(string Role, string Name, string Email)> recipients,
    System.Collections.Generic.List<(string Role, string Name, string Email)> ccRecipients,
    string subject,
    string body,
    string projectCode,
    string reason)
{
    var dataRoot = System.Environment.GetEnvironmentVariable("PROJECTPULSE_DATA_DIR");

    if (string.IsNullOrWhiteSpace(dataRoot))
    {
        dataRoot = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "data");
    }

    var outboxDir = System.IO.Path.Combine(dataRoot, "project-closeout-email-outbox");
    System.IO.Directory.CreateDirectory(outboxDir);

    var outboxPath = System.IO.Path.Combine(
        outboxDir,
        $"{System.DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{ProjectPulse041ASafeFilePart(projectCode)}-{ProjectPulse041ASafeFilePart(reason)}.eml");

    await System.IO.File.WriteAllTextAsync(
        outboxPath,
        ProjectPulse041ABuildRawEmail(recipients, ccRecipients, "project-health-dashboard@localhost", subject, body));

    return outboxPath;
}

/* 042_LIVE_BILLING_API_MAP_START */
ProjectTime.Api.Modules.InvoiceBillingModule.MapInvoiceBillingEndpoints(app);
app.MapWorkLifecycleEndpoints();
/* 042_LIVE_BILLING_API_MAP_END */

/* WORK_REGISTER_PO_ENDPOINT_MAP_START */
ProjectTime.Api.Modules.WorkRegisterPurchaseOrderModule.MapWorkRegisterPurchaseOrderEndpoints(app);
/* WORK_REGISTER_PO_ENDPOINT_MAP_END */

/* 055D_SELL_IMPORT_ENDPOINT_MAP_START */
app.MapWorkRegisterSellImportEndpoints();
/* 055D_SELL_IMPORT_ENDPOINT_MAP_END */

ProjectTime.Api.Modules.IdentityProfileModule.MapIdentityProfileEndpoints(app);

/* MODULE_026_CRM_ERP_INTEGRATION_ENDPOINT_MAP_START */
app.MapCrmErpIntegrationEndpoints();
/* MODULE_026_CRM_ERP_INTEGRATION_ENDPOINT_MAP_END */

/* MODULE_064_SHARED_AI_ENDPOINT_MAP_START */
app.MapAiProviderConfigurationEndpoints();
/* MODULE_064_SHARED_AI_ENDPOINT_MAP_END */

/* MODULE_065_ENTRA_SECRET_ENDPOINT_MAP_START */
app.MapEntraSecretAdministrationEndpoints();
/* MODULE_065_ENTRA_SECRET_ENDPOINT_MAP_END */

app.MapProjectForgeEndpoints();

/* MODULE_066A1_PROJECT_FLOWHIVE_ENDPOINT_MAP_START */
app.MapProjectFlowHiveEndpoints();
/* MODULE_066A1_PROJECT_FLOWHIVE_ENDPOINT_MAP_END */

/* MODULES_067_074_RELEASE_TRAIN_ENDPOINT_MAP_START */
app.MapGlobalMailConfigurationEndpoints();
app.MapSystemArchitectureEndpoints();
app.MapQualificationsCertificationEndpoints();
app.MapCapacityPipelineForecastEndpoints();
app.MapOnCallSchedulingEndpoints();
app.MapOneAssistRoutingDirectoryEndpoints();
app.MapSalesCoverageAlignmentEndpoints();
app.MapOemVendorDirectoryEndpoints();
app.MapModule064074NativeAdministrationEndpoints();
/* MODULES_067_074_RELEASE_TRAIN_ENDPOINT_MAP_END */

/* MODULE_076_DEFECT_TRACKER_ENDPOINT_MAP_START */
app.MapDefectTrackerEndpoints();
/* MODULE_076_DEFECT_TRACKER_ENDPOINT_MAP_END */

/* MODULES_075_080_RUNTIME_ENDPOINT_MAP_START */
app.MapIntegrationEventGatewayEndpoints();
app.MapReleaseDeploymentControlEndpoints();
app.MapObservabilitySloHealthEndpoints();
app.MapDataGovernanceRetentionEndpoints();
app.MapCustomerDeliveryAcceptanceEndpoints();
/* MODULES_075_080_RUNTIME_ENDPOINT_MAP_END */

/* MODULE_998_SYSTEM_DIAGNOSTIC_ENDPOINT_MAP_START */
app.MapSystemDiagnosticRemediationEndpoints();
/* MODULE_998_SYSTEM_DIAGNOSTIC_ENDPOINT_MAP_END */

ProjectTime.Api.Modules.CalendarCapacityModule.MapCalendarCapacityEndpoints(app);

/* MODULE_997_SECURITY_OPERATIONS_ENDPOINT_MAP_START */
app.MapSecurityOperationsResponseEndpoints();
/* MODULE_997_SECURITY_OPERATIONS_ENDPOINT_MAP_END */

ProjectTime.Api.Modules.CiCdPipelineModule.MapCiCdPipelineEndpoints(app);

app.MapCertiniaBillingEndpoints();
app.MapSessionIntelligenceEndpoints();
app.MapSellInboundSnapshotEndpoints();
app.MapSellCommercialReadModelEndpoints();

app.MapContractsEndpoints();
app.MapContractsPrepaidModule();
app.MapContractsPrepaidManagementModule();

ProjectTime.Api.Modules.OpportunitiesModule.MapOpportunityEndpoints(app);

/* MODULE_001A_ENGINEER_REQUEST_CLOSEOUT_ENDPOINT_MAP_START */
app.MapModule001AEngineerTaskCloseoutEndpoints();
/* MODULE_001A_ENGINEER_REQUEST_CLOSEOUT_ENDPOINT_MAP_END */
app.MapLabEquipmentTrackerEndpoints();
app.MapProjectRiskRegisterEndpoints();

app.Run();




static bool CanEngineerUnlockDay(string? status, DateTimeOffset? submittedAt)
{
    return status == "submitted"
        && submittedAt is not null
        && DateTimeOffset.UtcNow - submittedAt.Value <= TimeSpan.FromHours(1);
}

static string GetDayUnlockMessage(string? status, DateTimeOffset? submittedAt)
{
    if (status is null || status == "draft") return "This day has not been submitted yet.";
    if (status == "manager_declined") return "This day was returned for correction and can be edited/resubmitted.";
    if (status == "submitted")
    {
        if (submittedAt is null) return "This submitted day is missing a submission timestamp. Please contact your manager to unlock it.";
        return DateTimeOffset.UtcNow - submittedAt.Value <= TimeSpan.FromHours(1)
            ? "This submitted day can be unlocked."
            : "This day was submitted more than one hour ago. Please contact your manager to unlock it.";
    }
    if (status == "manager_approved") return "This day has been manager-approved and is read-only for the engineer.";
    if (status == "pm_approved") return "This day has been PM-approved and is read-only for the engineer.";
    if (status == "accounting_ready") return "This day is ready for accounting review and is read-only for the engineer.";
    if (status == "reconciled") return "This day has been reconciled and is locked.";
    if (status == "locked") return "This day is locked.";

    return "This day is not editable in its current workflow state.";
}



static string ProjectPulseCsvField(object? value)
{
    var text = value switch
    {
        null => string.Empty,
        DateOnly d => d.ToString("yyyy-MM-dd"),
        DateTimeOffset dto => dto.ToString("O"),
        decimal d => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    text = text.Replace("\r", " ").Replace("\n", " ").Replace("\"", "\"\"");

    /* SECURITY_20260729_CSV_FORMULA_NEUTRALIZATION */
    var formulaCandidate = text.TrimStart();
    if (formulaCandidate.StartsWith("=", StringComparison.Ordinal)
        || formulaCandidate.StartsWith("+", StringComparison.Ordinal)
        || formulaCandidate.StartsWith("-", StringComparison.Ordinal)
        || formulaCandidate.StartsWith("@", StringComparison.Ordinal))
    {
        text = "'" + text;
    }

    return $"\"{text}\"";
}


static IReadOnlyList<string> ValidateDaySubmitRequest(TimesheetDaySubmitRequest request)
{
    var errors = new List<string>();
    var weekStart = GetSundayForDate(request.WeekStart);
    var weekEnd = weekStart.AddDays(6);

    if (request.WorkDate < weekStart || request.WorkDate > weekEnd)
    {
        errors.Add($"Work date {request.WorkDate} is outside the selected week {weekStart} through {weekEnd}.");
    }

    if (request.Entries is null || request.Entries.Count == 0)
    {
        errors.Add("At least one time entry is required for the selected day.");
        return errors;
    }

    var dailyTotal = request.Entries
        .Where(entry => entry.WorkDate == request.WorkDate)
        .Sum(entry => entry.Hours);

    if (dailyTotal < 8.00m)
    {
        errors.Add($"A minimum of 8.00 hours is required before submitting {request.WorkDate}. Current total is {dailyTotal:0.00} hours.");
    }

    foreach (var entry in request.Entries)
    {
        if (entry.WorkDate != request.WorkDate)
        {
            errors.Add($"Entry date {entry.WorkDate} does not match selected submit date {request.WorkDate}.");
        }

        if (entry.TimeType is not ("normal" or "afterhours"))
        {
            errors.Add($"Invalid time type '{entry.TimeType}'. Expected normal or afterhours.");
        }

        if (entry.Hours < 0 || entry.Hours > 24)
        {
            errors.Add($"Hours for {entry.WorkDate} must be between 0 and 24.");
        }

        if (entry.Hours > 0 && string.IsNullOrWhiteSpace(entry.CategoryCode) && (entry.ProjectId is null || entry.TaskId is null))
        {
            errors.Add($"Entry for {entry.WorkDate} must identify either a non-project category or a project task.");
        }
    }

    return errors;
}


static async Task<object> LoadTimesheetPreferencesAsync(NpgsqlConnection connection, Guid userId)
{
    const string sql = """
        INSERT INTO user_timesheet_preferences (user_id)
        VALUES (@user_id)
        ON CONFLICT (user_id) DO NOTHING;

        SELECT default_non_project_category_codes,
               default_project_task_ids,
               auto_add_holidays,
               weekly_reminder_enabled,
               reminder_day_of_week,
               reminder_local_time,
               timezone_name
        FROM user_timesheet_preferences
        WHERE user_id = @user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);

    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();

    return new
    {
        defaultNonProjectCategoryCodes = reader.GetFieldValue<string[]>(0),
        defaultProjectTaskIds = reader.GetFieldValue<Guid[]>(1),
        autoAddHolidays = reader.GetBoolean(2),
        weeklyReminderEnabled = reader.GetBoolean(3),
        reminderDayOfWeek = reader.GetInt32(4),
        reminderLocalTime = reader.GetFieldValue<TimeOnly>(5).ToString("HH:mm"),
        timezoneName = reader.GetString(6)
    };
}

static async Task<IResult> QueueReminderRuleAsync(string ruleCode)
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        INSERT INTO email_notification_outbox (rule_code, recipient_email, recipient_name, subject, body, status, scheduled_for)
        SELECT
            rr.rule_code,
            u.email,
            u.display_name,
            rr.subject_template,
            REPLACE(rr.body_template, '{{display_name}}', u.display_name),
            'queued',
            NOW()
        FROM reminder_rules rr
        INNER JOIN notification_groups ng ON ng.group_code = rr.recipient_group_code
        INNER JOIN notification_group_members ngm ON ngm.notification_group_id = ng.notification_group_id AND ngm.is_active = TRUE
        INNER JOIN app_users u ON u.user_id = ngm.user_id AND u.is_active = TRUE
        WHERE rr.rule_code = @rule_code
          AND rr.is_active = TRUE
          AND ng.is_active = TRUE;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("rule_code", ruleCode);
    var inserted = await command.ExecuteNonQueryAsync();

    return Results.Ok(new { status = "queued", ruleCode, queuedCount = inserted });
}


static List<string> ParseSimpleCsvLine(string line)
{
    var values = new List<string>();
    var current = new System.Text.StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
        var ch = line[i];
        if (ch == '"')
        {
            if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else
            {
                inQuotes = !inQuotes;
            }
        }
        else if (ch == ',' && !inQuotes)
        {
            values.Add(current.ToString());
            current.Clear();
        }
        else
        {
            current.Append(ch);
        }
    }

    values.Add(current.ToString());
    return values;
}

static bool IsTruthy(string? value)
{
    return value is not null && new[] { "true", "1", "yes", "y" }.Contains(value.Trim().ToLowerInvariant());
}


static async Task<object> BuildSecurityContextAsync(NpgsqlConnection connection, Guid userId)
{
    string? email = null;
    string? displayName = null;

    await using (var userCommand = new NpgsqlCommand("SELECT email, display_name FROM app_users WHERE user_id = @user_id;", connection))
    {
        userCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await userCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            email = reader.GetString(0);
            displayName = reader.GetString(1);
        }
    }

    var roles = new List<object>();
    await using (var roleCommand = new NpgsqlCommand("""
        SELECT r.role_code, r.role_name, r.role_description
        FROM app_user_role_assignments ura
        INNER JOIN app_roles r ON r.app_role_id = ura.app_role_id
        WHERE ura.user_id = @user_id
          AND ura.is_active = TRUE
          AND r.is_active = TRUE
        ORDER BY r.display_order;
        """, connection))
    {
        roleCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await roleCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(new
            {
                roleCode = reader.GetString(0),
                roleName = reader.GetString(1),
                description = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }
    }

    var permissions = new List<string>();
    await using (var permissionCommand = new NpgsqlCommand("""
        SELECT DISTINCT p.permission_code
        FROM app_user_role_assignments ura
        INNER JOIN app_roles r ON r.app_role_id = ura.app_role_id
        INNER JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
        INNER JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
        WHERE ura.user_id = @user_id
          AND ura.is_active = TRUE
          AND r.is_active = TRUE
        ORDER BY p.permission_code;
        """, connection))
    {
        permissionCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await permissionCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync()) permissions.Add(reader.GetString(0));
    }

    var features = new List<object>();
    await using (var featureCommand = new NpgsqlCommand("""
        SELECT feature_code, feature_name, module_code, route_anchor, required_permission_code, feature_description
        FROM app_feature_catalog
        WHERE is_active = TRUE
          AND (required_permission_code IS NULL OR required_permission_code = ANY(@permissions))
        ORDER BY display_order;
        """, connection))
    {
        featureCommand.Parameters.AddWithValue("permissions", permissions.ToArray());
        await using var reader = await featureCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            features.Add(new
            {
                featureCode = reader.GetString(0),
                featureName = reader.GetString(1),
                moduleCode = reader.GetString(2),
                routeAnchor = reader.IsDBNull(3) ? null : reader.GetString(3),
                requiredPermissionCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                description = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
    }

    return new
    {
        userId,
        email,
        displayName,
        roles,
        permissions,
        features,
        can = new
        {
            viewTimeEntry = permissions.Contains("VIEW_TIME_ENTRY"),
            editOwnTime = permissions.Contains("EDIT_OWN_TIME"),
            approveTime = permissions.Contains("APPROVE_TIME"),
            rejectTime = permissions.Contains("REJECT_TIME"),
            manageHolidays = permissions.Contains("MANAGE_HOLIDAYS"),
            viewHolidays = permissions.Contains("VIEW_HOLIDAYS"),
            viewProjectIntake = permissions.Contains("VIEW_PROJECT_INTAKE"),
            viewResourceScheduling = permissions.Contains("VIEW_RESOURCE_SCHEDULING"),
            viewExpenses = permissions.Contains("VIEW_EXPENSES"),
            viewExecutiveReporting = permissions.Contains("VIEW_EXECUTIVE_REPORTING"),
            viewAuditTrail = permissions.Contains("VIEW_AUDIT_TRAIL"),
            exportTimePdf = permissions.Contains("EXPORT_TIME_PDF"),
            exportTimeExcel = permissions.Contains("EXPORT_TIME_EXCEL"),
            systemAdministration = permissions.Contains("SYSTEM_ADMINISTRATION"),
            manageAll = permissions.Contains("MANAGE_ALL")
        }
    };
}

static IResult? ValidateConfig(DatabaseConfig config)
{
    if (config.Missing.Count == 0)
    {
        return null;
    }

    return Results.BadRequest(new
    {
        status = "configuration_missing",
        missing = config.Missing
    });
}

static DateOnly GetSundayForDate(DateOnly date)
{
    var offset = (int)date.DayOfWeek;
    return date.AddDays(-offset);
}

static IReadOnlyList<string> ValidateTimesheetRequest(TimesheetSaveRequest request)
{
    var errors = new List<string>();
    var start = GetSundayForDate(request.WeekStart);
    var end = start.AddDays(6);

    if (request.Entries is null)
    {
        errors.Add("Entries collection is required.");
        return errors;
    }

    foreach (var entry in request.Entries)
    {
        if (entry.WorkDate < start || entry.WorkDate > end)
        {
            errors.Add($"Entry date {entry.WorkDate} is outside the selected week {start} through {end}.");
        }

        if (entry.TimeType is not ("normal" or "afterhours"))
        {
            errors.Add($"Invalid time type '{entry.TimeType}'. Expected normal or afterhours.");
        }

        if (entry.Hours < 0 || entry.Hours > 24)
        {
            errors.Add($"Hours for {entry.WorkDate} must be between 0 and 24.");
        }

        if (entry.Hours > 0 && string.IsNullOrWhiteSpace(entry.CategoryCode) && (entry.ProjectId is null || entry.TaskId is null))
        {
            errors.Add($"Entry for {entry.WorkDate} must identify either a non-project category or a project task.");
        }
    }

    return errors;
}



/* 054E_AUDIT_EXPORT_VISIBILITY_HELPERS_START */
static string ProjectPulse054ETruncate(string? value, int maxLength)
{
    var text = value ?? string.Empty;
    if (text.Length <= maxLength)
    {
        return text;
    }

    return text[..maxLength] + "...";
}

static string ProjectPulse054EAuditCategory(string action, string entityType)
{
    var value = $"{action} {entityType}".ToLowerInvariant();

    if (value.Contains("export") || value.Contains("download") || value.Contains("package"))
    {
        return "export_package";
    }

    if (value.Contains("reconcili"))
    {
        return "reconciliation";
    }

    if (value.Contains("lock"))
    {
        return "lock";
    }

    if (value.Contains("declin") || value.Contains("reject") || value.Contains("return"))
    {
        return "decline_return";
    }

    if (value.Contains("approval") || value.Contains("approved") || value.Contains("manager") || value.Contains("pm_"))
    {
        return "approval";
    }

    if (value.Contains("time") || value.Contains("timesheet"))
    {
        return "time_entry";
    }

    return "general_audit";
}

static string ProjectPulse054EAuditSourceModule(string action, string entityType)
{
    var value = $"{action} {entityType}".ToLowerInvariant();

    if (value.Contains("time_workflow_export") || value.Contains("export") || value.Contains("download"))
    {
        return "Approval / Export / Audit";
    }

    if (value.Contains("timesheet") || value.Contains("time_entry"))
    {
        return "Time Entry / Approval Workflow";
    }

    if (value.Contains("project"))
    {
        return "Project Workflow";
    }

    return "Audit History";
}

static string ProjectPulse054EBuildAuditEvidencePreview(string action, string entityType, string? oldValue, string? newValue)
{
    var category = ProjectPulse054EAuditCategory(action, entityType);
    var payload = string.IsNullOrWhiteSpace(newValue) ? oldValue ?? string.Empty : newValue ?? string.Empty;

    var prefix = category switch
    {
        "export_package" => "Export package evidence",
        "reconciliation" => "Reconciliation evidence",
        "lock" => "Lock evidence",
        "decline_return" => "Decline/return evidence",
        "approval" => "Approval evidence",
        "time_entry" => "Time-entry evidence",
        _ => "Audit evidence"
    };

    if (string.IsNullOrWhiteSpace(payload))
    {
        return $"{prefix}: {action} recorded for {entityType}.";
    }

    return $"{prefix}: {ProjectPulse054ETruncate(payload, 360)}";
}
/* 054E_AUDIT_EXPORT_VISIBILITY_HELPERS_END */

/* 054D_EXPORT_SNAPSHOT_HELPERS_START */
static async Task<List<Dictionary<string, object?>>> ProjectPulse054DLoadExportSnapshotItemsAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction? transaction,
    Guid exportId)
{
    var items = new List<Dictionary<string, object?>>();

    await using var command = new NpgsqlCommand("""
        SELECT
            time_entry_id,
            work_date,
            employee_name,
            employee_email,
            project_code,
            project_name,
            task_code,
            task_name,
            hours,
            billable,
            status,
            description
        FROM time_workflow_export_items
        WHERE time_workflow_export_id = @export_id
        ORDER BY work_date, employee_name, project_code, task_code, time_entry_id;
        """, connection);

    if (transaction is not null)
    {
        command.Transaction = transaction;
    }

    command.Parameters.AddWithValue("export_id", exportId);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new Dictionary<string, object?>
        {
            ["timeEntryId"] = reader.IsDBNull(0) ? null : reader.GetGuid(0),
            ["workDate"] = reader.GetFieldValue<DateOnly>(1),
            ["employeeName"] = reader.GetString(2),
            ["employeeEmail"] = reader.GetString(3),
            ["projectCode"] = reader.GetString(4),
            ["projectName"] = reader.GetString(5),
            ["taskCode"] = reader.GetString(6),
            ["taskName"] = reader.GetString(7),
            ["hours"] = reader.GetDecimal(8),
            ["billable"] = reader.GetBoolean(9),
            ["status"] = reader.GetString(10),
            ["description"] = reader.GetString(11)
        });
    }

    return items;
}

static async Task<List<Dictionary<string, object?>>> ProjectPulse054DLoadLegacyLiveExportItemsAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction? transaction,
    DateOnly start,
    DateOnly end)
{
    var items = new List<Dictionary<string, object?>>();

    await using var command = new NpgsqlCommand("""
        SELECT
            te.time_entry_id,
            te.work_date,
            COALESCE(employee.display_name, employee.email, '') AS employee_name,
            COALESCE(employee.email, '') AS employee_email,
            COALESCE(p.project_code, '') AS project_code,
            COALESCE(p.project_name, '') AS project_name,
            COALESCE(pt.task_code, '') AS task_code,
            COALESCE(pt.task_name, '') AS task_name,
            te.hours,
            te.billable,
            te.status,
            COALESCE(te.description, '') AS description
        FROM time_entries te
        JOIN app_users employee
            ON employee.user_id = te.user_id
        LEFT JOIN projects p
            ON p.project_id = te.project_id
        LEFT JOIN project_tasks pt
            ON pt.task_id = te.task_id
        WHERE te.work_date BETWEEN @week_start AND @week_end
          AND te.status IN ('accounting_ready', 'reconciled', 'locked')
        ORDER BY te.work_date, employee.display_name, p.project_code, pt.task_code;
        """, connection);

    if (transaction is not null)
    {
        command.Transaction = transaction;
    }

    command.Parameters.AddWithValue("week_start", start);
    command.Parameters.AddWithValue("week_end", end);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new Dictionary<string, object?>
        {
            ["timeEntryId"] = reader.GetGuid(0),
            ["workDate"] = reader.GetFieldValue<DateOnly>(1),
            ["employeeName"] = reader.GetString(2),
            ["employeeEmail"] = reader.GetString(3),
            ["projectCode"] = reader.GetString(4),
            ["projectName"] = reader.GetString(5),
            ["taskCode"] = reader.GetString(6),
            ["taskName"] = reader.GetString(7),
            ["hours"] = reader.GetDecimal(8),
            ["billable"] = reader.GetBoolean(9),
            ["status"] = reader.GetString(10),
            ["description"] = reader.GetString(11)
        });
    }

    return items;
}

static string ProjectPulse054DBuildExportCsv(
    Guid exportId,
    string exportFormat,
    DateOnly start,
    DateOnly end,
    IReadOnlyList<Dictionary<string, object?>> items)
{
    var csv = new System.Text.StringBuilder();
    csv.AppendLine("Export Id,Export Format,Week Start,Week End,Work Date,Employee Name,Employee Email,Project Code,Project Name,Task Code,Task Name,Hours,Billable,Status,Description");

    foreach (var item in items)
    {
        var fields = new[]
        {
            ProjectPulseCsvField(exportId),
            ProjectPulseCsvField(exportFormat),
            ProjectPulseCsvField(start),
            ProjectPulseCsvField(end),
            ProjectPulseCsvField(ProjectPulse054DDateOnly(item, "workDate")),
            ProjectPulseCsvField(ProjectPulse054DString(item, "employeeName")),
            ProjectPulseCsvField(ProjectPulse054DString(item, "employeeEmail")),
            ProjectPulseCsvField(ProjectPulse054DString(item, "projectCode")),
            ProjectPulseCsvField(ProjectPulse054DString(item, "projectName")),
            ProjectPulseCsvField(ProjectPulse054DString(item, "taskCode")),
            ProjectPulseCsvField(ProjectPulse054DString(item, "taskName")),
            ProjectPulseCsvField(ProjectPulse054DDecimal(item, "hours")),
            ProjectPulseCsvField(ProjectPulse054DBool(item, "billable") ? "Yes" : "No"),
            ProjectPulseCsvField(ProjectPulse054DString(item, "status")),
            ProjectPulseCsvField(ProjectPulse054DString(item, "description"))
        };

        csv.AppendLine(string.Join(",", fields));
    }

    return csv.ToString();
}

static string ProjectPulse054DComputeSha256(string value)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(value);
    var hash = System.Security.Cryptography.SHA256.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static object ProjectPulse054DExportSnapshotItemDto(Dictionary<string, object?> item)
{
    return new
    {
        timeEntryId = ProjectPulse054DNullable(item, "timeEntryId"),
        workDate = ProjectPulse054DDateOnly(item, "workDate"),
        employeeName = ProjectPulse054DString(item, "employeeName"),
        employeeEmail = ProjectPulse054DString(item, "employeeEmail"),
        projectCode = ProjectPulse054DString(item, "projectCode"),
        projectName = ProjectPulse054DString(item, "projectName"),
        taskCode = ProjectPulse054DString(item, "taskCode"),
        taskName = ProjectPulse054DString(item, "taskName"),
        hours = ProjectPulse054DDecimal(item, "hours"),
        billable = ProjectPulse054DBool(item, "billable"),
        status = ProjectPulse054DString(item, "status"),
        description = ProjectPulse054DString(item, "description")
    };
}

static object? ProjectPulse054DNullable(Dictionary<string, object?> item, string key)
{
    return item.TryGetValue(key, out var value) ? value : null;
}

static string ProjectPulse054DString(Dictionary<string, object?> item, string key)
{
    if (!item.TryGetValue(key, out var value) || value is null || value is DBNull)
    {
        return string.Empty;
    }

    return Convert.ToString(value) ?? string.Empty;
}

static decimal ProjectPulse054DDecimal(Dictionary<string, object?> item, string key)
{
    if (!item.TryGetValue(key, out var value) || value is null || value is DBNull)
    {
        return 0m;
    }

    return value is decimal decimalValue ? decimalValue : Convert.ToDecimal(value);
}

static bool ProjectPulse054DBool(Dictionary<string, object?> item, string key)
{
    if (!item.TryGetValue(key, out var value) || value is null || value is DBNull)
    {
        return false;
    }

    return value is bool boolValue ? boolValue : Convert.ToBoolean(value);
}

static DateOnly ProjectPulse054DDateOnly(Dictionary<string, object?> item, string key)
{
    if (!item.TryGetValue(key, out var value) || value is null || value is DBNull)
    {
        return DateOnly.FromDateTime(DateTime.UtcNow.Date);
    }

    return value is DateOnly dateOnlyValue
        ? dateOnlyValue
        : DateOnly.FromDateTime(Convert.ToDateTime(value));
}

static async Task<(string? PackageSha256, int PackageSnapshotItemCount)> ProjectPulse054DLoadExportMetadataAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction? transaction,
    Guid exportId)
{
    await using var command = new NpgsqlCommand("""
        SELECT package_sha256, COALESCE(package_snapshot_item_count, 0)
        FROM time_workflow_export_metadata
        WHERE time_workflow_export_id = @export_id;
        """, connection);

    if (transaction is not null)
    {
        command.Transaction = transaction;
    }

    command.Parameters.AddWithValue("export_id", exportId);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return (null, 0);
    }

    return (
        reader.IsDBNull(0) ? null : reader.GetString(0),
        reader.GetInt32(1));
}

static async Task<string?> ProjectPulse054DGetStoredPackageShaAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction? transaction,
    Guid exportId)
{
    var metadata = await ProjectPulse054DLoadExportMetadataAsync(connection, transaction, exportId);
    return metadata.PackageSha256;
}

static async Task ProjectPulse054DUpsertExportMetadataAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid exportId,
    string packageSha256,
    string packageSnapshot,
    int itemCount)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO time_workflow_export_metadata (
            time_workflow_export_id,
            package_sha256,
            package_snapshot,
            package_snapshot_item_count,
            updated_at
        )
        VALUES (
            @export_id,
            @package_sha256,
            @package_snapshot::jsonb,
            @item_count,
            NOW()
        )
        ON CONFLICT (time_workflow_export_id) DO UPDATE
        SET package_sha256 = COALESCE(time_workflow_export_metadata.package_sha256, EXCLUDED.package_sha256),
            package_snapshot = COALESCE(time_workflow_export_metadata.package_snapshot, EXCLUDED.package_snapshot),
            package_snapshot_item_count = CASE
                WHEN time_workflow_export_metadata.package_snapshot_item_count = 0 THEN EXCLUDED.package_snapshot_item_count
                ELSE time_workflow_export_metadata.package_snapshot_item_count
            END,
            updated_at = NOW();
        """, connection, transaction);

    command.Parameters.AddWithValue("export_id", exportId);
    command.Parameters.AddWithValue("package_sha256", packageSha256);
    command.Parameters.AddWithValue("package_snapshot", packageSnapshot);
    command.Parameters.AddWithValue("item_count", itemCount);
    await command.ExecuteNonQueryAsync();
}
/* 054D_EXPORT_SNAPSHOT_HELPERS_END */

/* 054C_WORKFLOW_IMMUTABILITY_HELPERS_START */
static async Task<string?> ProjectPulse054CGetTimesheetDayStatusAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid timesheetId, DateOnly workDate)
{
    await using var command = new NpgsqlCommand("""
        SELECT status
        FROM timesheet_day_statuses
        WHERE timesheet_id = @timesheet_id
          AND work_date = @work_date;
        """, connection);

    if (transaction is not null)
    {
        command.Transaction = transaction;
    }

    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    command.Parameters.AddWithValue("work_date", workDate);

    var value = await command.ExecuteScalarAsync();
    return value as string;
}

static bool ProjectPulse054CWorkflowTransitionAllowed(string normalizedAction, string? currentStatus)
{
    var status = (currentStatus ?? string.Empty).Trim().ToLowerInvariant();

    return normalizedAction switch
    {
        "pm_approve" => status == "manager_approved",
        "pm_reject" => status == "manager_approved",
        "accounting_ready" => status is "manager_approved" or "pm_approved",
        "reconcile" => status is "accounting_ready" or "pm_approved",
        "lock" => status is "accounting_ready" or "reconciled",
        _ => false
    };
}
/* 054C_WORKFLOW_IMMUTABILITY_HELPERS_END */

/* 054B_APPROVAL_AUTHORITY_HELPERS_START */

static async Task<Guid?> ProjectPulse054BGetTimesheetOwnerUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid timesheetId)
{
    await using var command = new NpgsqlCommand("""
        SELECT user_id
        FROM timesheets
        WHERE timesheet_id = @timesheet_id;
        """, connection);

    if (transaction is not null)
    {
        command.Transaction = transaction;
    }

    command.Parameters.AddWithValue("timesheet_id", timesheetId);

    var value = await command.ExecuteScalarAsync();
    return value is Guid userId ? userId : null;
}

static async Task<bool> ProjectPulse054BWorkflowDayHasProjectManagerScopeAsync(NpgsqlConnection connection, Guid actorUserId, Guid timesheetId, DateOnly workDate)
{
    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM time_entries te
            JOIN projects p
              ON p.project_id = te.project_id
            WHERE te.timesheet_id = @timesheet_id
              AND te.work_date = @work_date
              AND p.project_manager_user_id = @actor_user_id
        );
        """, connection);

    command.Parameters.AddWithValue("actor_user_id", actorUserId);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    command.Parameters.AddWithValue("work_date", workDate);

    return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
}
/* 054B_APPROVAL_AUTHORITY_HELPERS_END */

static async Task<Guid> GetOrCreateDevelopmentManagerUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
{
    const string sql = """
        INSERT INTO app_users (email, display_name, job_title, department, is_active)
        VALUES ('ahmed.adeyemi@ussignal.local', 'Ahmed Adeyemi', 'Development Manager', 'Project Pulse', TRUE)
        ON CONFLICT (email) DO UPDATE
        SET display_name = EXCLUDED.display_name,
            updated_at = NOW()
        RETURNING user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create development manager user."));
}

/* 053H_ASSIGNMENT_COST_GUARDRAIL_HELPER_START */
static decimal ProjectPulse053HGetDefaultEngineeringHourlyCost()
{
    var configuredValue = Environment.GetEnvironmentVariable("PTP_DEFAULT_ENGINEERING_HOURLY_COST");

    if (decimal.TryParse(configuredValue, out var parsedValue) && parsedValue > 0m)
    {
        return parsedValue;
    }

    return 150m;
}
/* 053H_ASSIGNMENT_COST_GUARDRAIL_HELPER_END */

static async Task<List<object>> LoadOpenAssignedProjectTasksAsync(NpgsqlConnection connection, Guid userId, DateOnly weekStart, DateOnly weekEnd)
{
    var tasks = new List<object>();

    const string sql = """
        SELECT DISTINCT
            pa.project_assignment_id,
            p.project_id,
            p.project_code,
            p.project_name,
            c.client_name,
            c.client_code,
            pt.task_id,
            pt.task_code,
            pt.task_name,
            pt.task_description,
            COALESCE(pa.allocation_percent, 0) AS allocation_percent,
            pa.effective_start_date,
            pa.effective_end_date,
            p.project_manager_user_id,
            pm.display_name AS project_manager_name
        FROM project_assignments pa
        INNER JOIN projects p ON p.project_id = pa.project_id
        INNER JOIN project_tasks pt ON pt.task_id = pa.task_id
        LEFT JOIN clients c ON c.client_id = p.client_id
        LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
        WHERE pa.user_id = @user_id
          AND pa.effective_start_date <= @week_end
          AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start)
          /* 053G_HIDE_CLOSED_PROJECTS_FROM_OPEN_TASKS */
          AND lower(COALESCE(p.status, 'active')) = 'active'
          AND pt.is_active = TRUE
        ORDER BY p.project_code, pt.task_code;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start", weekStart);
    command.Parameters.AddWithValue("week_end", weekEnd);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        tasks.Add(new
        {
            assignmentId = reader.GetGuid(0),
            projectId = reader.GetGuid(1),
            projectCode = reader.GetString(2),
            projectName = reader.GetString(3),
            clientName = reader.IsDBNull(4) ? null : reader.GetString(4),
            clientCode = reader.IsDBNull(5) ? null : reader.GetString(5),
            taskId = reader.GetGuid(6),
            taskCode = reader.GetString(7),
            taskName = reader.GetString(8),
            taskDescription = reader.IsDBNull(9) ? null : reader.GetString(9),
            allocationPercent = reader.GetDecimal(10),
            effectiveStartDate = reader.GetFieldValue<DateOnly>(11),
            effectiveEndDate = reader.IsDBNull(12) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(12),
            projectManagerUserId = reader.IsDBNull(13) ? (Guid?)null : reader.GetGuid(13),
            projectManagerName = reader.IsDBNull(14) ? null : reader.GetString(14)
        });
    }

    return tasks;
}

static async Task<Guid> GetOrCreateDevelopmentUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
{
    const string sql = """
        INSERT INTO app_users (email, display_name, job_title, department, is_active)
        VALUES ('ahmed.adeyemi@ussignal.local', 'Ahmed Adeyemi', 'Development Engineer', 'Professional Services', TRUE)
        ON CONFLICT (email) DO UPDATE
        SET display_name = EXCLUDED.display_name,
            job_title = EXCLUDED.job_title,
            department = EXCLUDED.department,
            is_active = TRUE,
            updated_at = NOW()
        RETURNING user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create development user."));
}



/* 051B_TIME_ENTRY_CRITICAL_REPAIR_START */
static bool ProjectPulse051BIsImmutableTimesheetDayStatus(string? status)
{
    return string.Equals(status, "submitted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "manager_approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "pm_approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "accounting_ready", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "reconciled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "locked", StringComparison.OrdinalIgnoreCase);
}

static bool ProjectPulse051BIsEditableTimesheetDayStatus(string? status)
{
    return string.IsNullOrWhiteSpace(status)
        || string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "manager_declined", StringComparison.OrdinalIgnoreCase);
}

static bool ProjectPulse051BIsImmutableTimesheetWeekStatus(string? status)
{
    return string.Equals(status, "submitted", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "manager_approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "pm_approved", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "accounting_ready", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "reconciled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "locked", StringComparison.OrdinalIgnoreCase);
}

static async Task<Guid?> ProjectPulse051BGetHolidayCategoryIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
{
    await using var command = new NpgsqlCommand("""
        SELECT non_project_time_category_id
        FROM non_project_time_categories
        WHERE is_active = TRUE
          AND (
                UPPER(COALESCE(category_code, '')) = 'HOLIDAY'
             OR UPPER(COALESCE(category_name, '')) = 'HOLIDAY'
             OR LOWER(COALESCE(category_name, '')) LIKE '%holiday%'
          )
        ORDER BY
            CASE WHEN UPPER(COALESCE(category_code, '')) = 'HOLIDAY' THEN 0 ELSE 1 END,
            category_name
        LIMIT 1;
        """, connection, transaction);

    var result = await command.ExecuteScalarAsync();

    return result is Guid holidayCategoryId ? holidayCategoryId : null;
}

static async Task<bool> ProjectPulse051BUserIsHolidayAutoSubmitEligibleAsync(NpgsqlConnection connection, Guid userId)
{
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
            WHERE u.user_id = @user_id
              AND u.is_active = TRUE
              AND r.role_code IN (
                    'ENGINEER',
                    'ENGINEERING',
                    'PROJECT_MANAGER',
                    'PROJECT_MANAGEMENT',
                    'PM_TEAM_LEAD',
                    'PROJECT_MANAGEMENT_LEAD'
              )
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId);

    return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
}

static async Task<int> ProjectPulse051BAutoSubmitEligibleHolidaysForWeekAsync(NpgsqlConnection connection, Guid userId, DateOnly weekStart)
{
    if (!await ProjectPulse051BUserIsHolidayAutoSubmitEligibleAsync(connection, userId))
    {
        return 0;
    }

    var weekEnd = weekStart.AddDays(6);
    var submittedCount = 0;

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, weekStart);
        var holidayCategoryId = await ProjectPulse051BGetHolidayCategoryIdAsync(connection, transaction);

        if (holidayCategoryId is null)
        {
            await transaction.CommitAsync();
            return 0;
        }

        var holidays = new List<(DateOnly HolidayDate, string HolidayName, decimal Hours)>();

        await using (var holidayCommand = new NpgsqlCommand("""
            SELECT holiday_date, holiday_name, auto_populate_hours
            FROM company_holidays
            WHERE is_active = TRUE
              AND is_floating_holiday = FALSE
              AND holiday_date BETWEEN @week_start AND @week_end
              AND EXTRACT(ISODOW FROM holiday_date) BETWEEN 1 AND 5
            ORDER BY holiday_date;
            """, connection, transaction))
        {
            holidayCommand.Parameters.AddWithValue("week_start", weekStart);
            holidayCommand.Parameters.AddWithValue("week_end", weekEnd);

            await using var reader = await holidayCommand.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                holidays.Add((
                    reader.GetFieldValue<DateOnly>(0),
                    reader.GetString(1),
                    reader.GetDecimal(2)
                ));
            }
        }

        foreach (var holiday in holidays)
        {
            var dayState = await GetTimesheetDayStatusAsync(connection, transaction, timesheetId, holiday.HolidayDate);

            if (!ProjectPulse051BIsEditableTimesheetDayStatus(dayState.Status))
            {
                continue;
            }

            var hasNonHolidayManualTime = false;

            await using (var manualCommand = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM time_entries
                    WHERE timesheet_id = @timesheet_id
                      AND user_id = @user_id
                      AND work_date = @work_date
                      AND (
                            project_id IS NOT NULL
                         OR task_id IS NOT NULL
                         OR non_project_time_category_id IS DISTINCT FROM @holiday_category_id
                      )
                      AND COALESCE(status, 'draft') NOT IN ('manager_declined', 'rejected', 'voided')
                );
                """, connection, transaction))
            {
                manualCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
                manualCommand.Parameters.AddWithValue("user_id", userId);
                manualCommand.Parameters.AddWithValue("work_date", holiday.HolidayDate);
                manualCommand.Parameters.AddWithValue("holiday_category_id", holidayCategoryId.Value);

                hasNonHolidayManualTime = Convert.ToBoolean(await manualCommand.ExecuteScalarAsync() ?? false);
            }

            if (hasNonHolidayManualTime)
            {
                continue;
            }

            await using (var deleteCommand = new NpgsqlCommand("""
                DELETE FROM time_entries
                WHERE timesheet_id = @timesheet_id
                  AND user_id = @user_id
                  AND work_date = @work_date
                  AND non_project_time_category_id = @holiday_category_id
                  AND COALESCE(status, 'draft') IN ('draft', 'manager_declined');
                """, connection, transaction))
            {
                deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
                deleteCommand.Parameters.AddWithValue("user_id", userId);
                deleteCommand.Parameters.AddWithValue("work_date", holiday.HolidayDate);
                deleteCommand.Parameters.AddWithValue("holiday_category_id", holidayCategoryId.Value);

                await deleteCommand.ExecuteNonQueryAsync();
            }

            await using (var insertCommand = new NpgsqlCommand("""
                INSERT INTO time_entries (
                    timesheet_id,
                    user_id,
                    non_project_time_category_id,
                    work_date,
                    hours,
                    time_type,
                    description,
                    status,
                    created_at,
                    updated_at
                )
                VALUES (
                    @timesheet_id,
                    @user_id,
                    @holiday_category_id,
                    @work_date,
                    @hours,
                    'normal',
                    @description,
                    'submitted',
                    NOW(),
                    NOW()
                );
                """, connection, transaction))
            {
                insertCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
                insertCommand.Parameters.AddWithValue("user_id", userId);
                insertCommand.Parameters.AddWithValue("holiday_category_id", holidayCategoryId.Value);
                insertCommand.Parameters.AddWithValue("work_date", holiday.HolidayDate);
                insertCommand.Parameters.AddWithValue("hours", holiday.Hours <= 0 ? 8.00m : holiday.Hours);
                insertCommand.Parameters.AddWithValue("description", string.IsNullOrWhiteSpace(holiday.HolidayName) ? "Company holiday" : holiday.HolidayName);

                await insertCommand.ExecuteNonQueryAsync();
            }

            await MarkTimesheetDaySubmittedAsync(connection, transaction, timesheetId, userId, holiday.HolidayDate);
            await InsertAuditLogAsync(connection, transaction, userId, "timesheet_holiday_auto_submitted", "timesheet", timesheetId);

            submittedCount++;
        }

        await transaction.CommitAsync();

        return submittedCount;
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }
}
/* 051B_TIME_ENTRY_CRITICAL_REPAIR_END */

static async Task<string?> GetTimesheetStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        SELECT status
        FROM timesheets
        WHERE user_id = @user_id
          AND week_start_date = @week_start_date;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);

    return (string?)await command.ExecuteScalarAsync();
}


static async Task<Guid> UpsertDraftShellForEditableSaveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        INSERT INTO timesheets (user_id, week_start_date, week_end_date, status, submitted_at)
        VALUES (@user_id, @week_start_date, @week_end_date, 'draft', NULL)
        ON CONFLICT (user_id, week_start_date) DO UPDATE
        SET week_end_date = EXCLUDED.week_end_date,
            status = CASE
                WHEN timesheets.status IN ('draft', 'manager_declined') THEN 'draft'
                ELSE timesheets.status
            END,
            submitted_at = CASE
                WHEN timesheets.status IN ('draft', 'manager_declined') THEN NULL
                ELSE timesheets.submitted_at
            END,
            updated_at = NOW()
        RETURNING timesheet_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);
    command.Parameters.AddWithValue("week_end_date", weekStart.AddDays(6));

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create draft timesheet shell."));
}

static async Task ReplaceEditableTimeEntriesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    var protectedDates = new HashSet<DateOnly>();

    await using (var protectedCommand = new NpgsqlCommand("""
        SELECT work_date
        FROM timesheet_day_statuses
        WHERE timesheet_id = @timesheet_id
          AND status IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked');
        """, connection, transaction))
    {
        protectedCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await using var reader = await protectedCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            protectedDates.Add(reader.GetFieldValue<DateOnly>(0));
        }
    }

    await using (var deleteCommand = new NpgsqlCommand("""
        DELETE FROM time_entries
        WHERE timesheet_id = @timesheet_id
          AND NOT EXISTS (
              SELECT 1
              FROM timesheet_day_statuses tds
              WHERE tds.timesheet_id = time_entries.timesheet_id
                AND tds.work_date = time_entries.work_date
                AND tds.status IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked')
          );
        """, connection, transaction))
    {
        deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    var editableEntries = entries
        .Where(entry => entry.Hours > 0)
        .Where(entry => !protectedDates.Contains(entry.WorkDate))
        .ToList();

    if (editableEntries.Count > 0)
    {
        await ReplaceTimeEntriesForEditableDaysAsync(connection, transaction, timesheetId, userId, editableEntries, status);
    }
}

static async Task ReplaceTimeEntriesForEditableDaysAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    foreach (var entry in entries.Where(item => item.Hours > 0))
    {
        Guid? nonProjectCategoryId = null;
        var billable = entry.ProjectId is not null && entry.TaskId is not null;

        if (!string.IsNullOrWhiteSpace(entry.CategoryCode))
        {
            nonProjectCategoryId = await GetNonProjectCategoryIdAsync(connection, transaction, entry.CategoryCode);
            billable = false;
        }

        const string sql = """
            INSERT INTO time_entries (
                timesheet_id,
                user_id,
                project_id,
                task_id,
                non_project_time_category_id,
                time_type,
                work_date,
                hours,
                description,
                billable,
                status,
                work_location_group_id,
                work_location_id
            )
            VALUES (
                @timesheet_id,
                @user_id,
                @project_id,
                @task_id,
                @non_project_time_category_id,
                @time_type,
                @work_date,
                @hours,
                @description,
                @billable,
                @status,
                @work_location_group_id,
                @work_location_id
            );
            """;

        await using var insertCommand = new NpgsqlCommand(sql, connection, transaction);
        insertCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        insertCommand.Parameters.AddWithValue("user_id", userId);
        insertCommand.Parameters.AddWithValue("project_id", (object?)entry.ProjectId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("task_id", (object?)entry.TaskId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("non_project_time_category_id", (object?)nonProjectCategoryId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("time_type", entry.TimeType);
        insertCommand.Parameters.AddWithValue("work_date", entry.WorkDate);
        insertCommand.Parameters.AddWithValue("hours", entry.Hours);
        insertCommand.Parameters.AddWithValue("description", string.IsNullOrWhiteSpace(entry.Description) ? DBNull.Value : entry.Description.Trim());
        insertCommand.Parameters.AddWithValue("billable", billable);
        insertCommand.Parameters.AddWithValue("status", status);
        insertCommand.Parameters.AddWithValue("work_location_group_id", (object?)entry.WorkLocationGroupId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("work_location_id", (object?)entry.WorkLocationId ?? DBNull.Value);

        await insertCommand.ExecuteNonQueryAsync();
    }
}

static async Task<Guid> UpsertDraftTimesheetAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        INSERT INTO timesheets (user_id, week_start_date, week_end_date, status, submitted_at)
        VALUES (@user_id, @week_start_date, @week_end_date, 'draft', NULL)
        ON CONFLICT (user_id, week_start_date) DO UPDATE
        SET week_end_date = EXCLUDED.week_end_date,
            status = 'draft',
            submitted_at = NULL,
            updated_at = NOW()
        RETURNING timesheet_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);
    command.Parameters.AddWithValue("week_end_date", weekStart.AddDays(6));

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create draft timesheet."));
}


static async Task<DayStatusRecord> GetTimesheetDayStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid timesheetId, DateOnly workDate)
{
    const string sql = """
        SELECT status, submitted_at
        FROM timesheet_day_statuses
        WHERE timesheet_id = @timesheet_id
          AND work_date = @work_date;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    command.Parameters.AddWithValue("work_date", workDate);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return new DayStatusRecord("draft", null);
    }

    return new DayStatusRecord(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
}

static async Task ReplaceDayTimeEntriesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    DateOnly workDate,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    await using (var deleteCommand = new NpgsqlCommand("DELETE FROM time_entries WHERE timesheet_id = @timesheet_id AND work_date = @work_date;", connection, transaction))
    {
        deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        deleteCommand.Parameters.AddWithValue("work_date", workDate);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    foreach (var entry in entries.Where(item => item.WorkDate == workDate && item.Hours > 0))
    {
        Guid? nonProjectCategoryId = null;
        var billable = entry.ProjectId is not null && entry.TaskId is not null;

        if (!string.IsNullOrWhiteSpace(entry.CategoryCode))
        {
            nonProjectCategoryId = await GetNonProjectCategoryIdAsync(connection, transaction, entry.CategoryCode);
            billable = false;
        }

        const string sql = """
            INSERT INTO time_entries (
                timesheet_id,
                user_id,
                project_id,
                task_id,
                non_project_time_category_id,
                time_type,
                work_date,
                hours,
                description,
                billable,
                status,
                work_location_group_id,
                work_location_id
            )
            VALUES (
                @timesheet_id,
                @user_id,
                @project_id,
                @task_id,
                @non_project_time_category_id,
                @time_type,
                @work_date,
                @hours,
                @description,
                @billable,
                @status,
                @work_location_group_id,
                @work_location_id
            );
            """;

        await using var insertCommand = new NpgsqlCommand(sql, connection, transaction);
        insertCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        insertCommand.Parameters.AddWithValue("user_id", userId);
        insertCommand.Parameters.AddWithValue("project_id", (object?)entry.ProjectId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("task_id", (object?)entry.TaskId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("non_project_time_category_id", (object?)nonProjectCategoryId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("time_type", entry.TimeType);
        insertCommand.Parameters.AddWithValue("work_date", entry.WorkDate);
        insertCommand.Parameters.AddWithValue("hours", entry.Hours);
        insertCommand.Parameters.AddWithValue("description", string.IsNullOrWhiteSpace(entry.Description) ? DBNull.Value : entry.Description.Trim());
        insertCommand.Parameters.AddWithValue("billable", billable);
        insertCommand.Parameters.AddWithValue("status", status);
        insertCommand.Parameters.AddWithValue("work_location_group_id", (object?)entry.WorkLocationGroupId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("work_location_id", (object?)entry.WorkLocationId ?? DBNull.Value);

        await insertCommand.ExecuteNonQueryAsync();
    }
}

static async Task MarkTimesheetDaySubmittedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid timesheetId, Guid userId, DateOnly workDate)
{
    const string sql = """
        INSERT INTO timesheet_day_statuses (timesheet_id, user_id, work_date, status, submitted_at)
        VALUES (@timesheet_id, @user_id, @work_date, 'submitted', NOW())
        ON CONFLICT (timesheet_id, work_date) DO UPDATE
        SET status = 'submitted',
            submitted_at = NOW(),
            updated_at = NOW();
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("work_date", workDate);
    await command.ExecuteNonQueryAsync();
}

static async Task UnlockTimesheetDayAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid timesheetId, Guid userId, DateOnly workDate)
{
    const string sql = """
        UPDATE timesheet_day_statuses
        SET status = 'draft',
            unlocked_at = NOW(),
            unlocked_by_user_id = @user_id,
            updated_at = NOW()
        WHERE timesheet_id = @timesheet_id
          AND work_date = @work_date
          AND status = 'submitted';
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("work_date", workDate);
    await command.ExecuteNonQueryAsync();

    await using var entryCommand = new NpgsqlCommand(
        "UPDATE time_entries SET status = 'draft', updated_at = NOW() WHERE timesheet_id = @timesheet_id AND work_date = @work_date;",
        connection,
        transaction);
    entryCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
    entryCommand.Parameters.AddWithValue("work_date", workDate);
    await entryCommand.ExecuteNonQueryAsync();
}

static async Task ReplaceTimeEntriesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    await using (var deleteCommand = new NpgsqlCommand("""
        DELETE FROM time_entries
        WHERE timesheet_id = @timesheet_id
          AND work_date NOT IN (
              SELECT work_date
              FROM timesheet_day_statuses
              WHERE timesheet_id = @timesheet_id
                AND status = 'submitted'
          );
        """, connection, transaction))
    {
        deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    foreach (var entry in entries.Where(item => item.Hours > 0))
    {
        Guid? nonProjectCategoryId = null;
        var billable = entry.ProjectId is not null && entry.TaskId is not null;

        if (!string.IsNullOrWhiteSpace(entry.CategoryCode))
        {
            nonProjectCategoryId = await GetNonProjectCategoryIdAsync(connection, transaction, entry.CategoryCode);
            billable = false;
        }

        const string sql = """
            INSERT INTO time_entries (
                timesheet_id,
                user_id,
                project_id,
                task_id,
                non_project_time_category_id,
                time_type,
                work_date,
                hours,
                description,
                billable,
                status,
                work_location_group_id,
                work_location_id
            )
            VALUES (
                @timesheet_id,
                @user_id,
                @project_id,
                @task_id,
                @non_project_time_category_id,
                @time_type,
                @work_date,
                @hours,
                @description,
                @billable,
                @status,
                @work_location_group_id,
                @work_location_id
            );
            """;

        await using var insertCommand = new NpgsqlCommand(sql, connection, transaction);
        insertCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        insertCommand.Parameters.AddWithValue("user_id", userId);
        insertCommand.Parameters.AddWithValue("project_id", (object?)entry.ProjectId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("task_id", (object?)entry.TaskId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("non_project_time_category_id", (object?)nonProjectCategoryId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("time_type", entry.TimeType);
        insertCommand.Parameters.AddWithValue("work_date", entry.WorkDate);
        insertCommand.Parameters.AddWithValue("hours", entry.Hours);
        insertCommand.Parameters.AddWithValue("description", string.IsNullOrWhiteSpace(entry.Description) ? DBNull.Value : entry.Description.Trim());
        insertCommand.Parameters.AddWithValue("billable", billable);
        insertCommand.Parameters.AddWithValue("status", status);
        insertCommand.Parameters.AddWithValue("work_location_group_id", (object?)entry.WorkLocationGroupId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("work_location_id", (object?)entry.WorkLocationId ?? DBNull.Value);

        await insertCommand.ExecuteNonQueryAsync();
    }
}


static async Task InsertTimeEntriesWithoutDeletingAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    foreach (var entry in entries.Where(item => item.Hours > 0))
    {
        Guid? nonProjectCategoryId = null;
        var billable = entry.ProjectId is not null && entry.TaskId is not null;

        if (!string.IsNullOrWhiteSpace(entry.CategoryCode))
        {
            nonProjectCategoryId = await GetNonProjectCategoryIdAsync(connection, transaction, entry.CategoryCode);
            billable = false;
        }

        const string sql = """
            INSERT INTO time_entries (
                timesheet_id,
                user_id,
                project_id,
                task_id,
                non_project_time_category_id,
                time_type,
                work_date,
                hours,
                description,
                billable,
                status,
                work_location_group_id,
                work_location_id
            )
            VALUES (
                @timesheet_id,
                @user_id,
                @project_id,
                @task_id,
                @non_project_time_category_id,
                @time_type,
                @work_date,
                @hours,
                @description,
                @billable,
                @status,
                @work_location_group_id,
                @work_location_id
            );
            """;

        await using var insertCommand = new NpgsqlCommand(sql, connection, transaction);
        insertCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        insertCommand.Parameters.AddWithValue("user_id", userId);
        insertCommand.Parameters.AddWithValue("project_id", (object?)entry.ProjectId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("task_id", (object?)entry.TaskId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("non_project_time_category_id", (object?)nonProjectCategoryId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("time_type", entry.TimeType);
        insertCommand.Parameters.AddWithValue("work_date", entry.WorkDate);
        insertCommand.Parameters.AddWithValue("hours", entry.Hours);
        insertCommand.Parameters.AddWithValue("description", string.IsNullOrWhiteSpace(entry.Description) ? DBNull.Value : entry.Description.Trim());
        insertCommand.Parameters.AddWithValue("billable", billable);
        insertCommand.Parameters.AddWithValue("status", status);
        insertCommand.Parameters.AddWithValue("work_location_group_id", (object?)entry.WorkLocationGroupId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("work_location_id", (object?)entry.WorkLocationId ?? DBNull.Value);

        await insertCommand.ExecuteNonQueryAsync();
    }
}

static async Task<Guid> GetNonProjectCategoryIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string categoryCode)
{
    const string sql = """
        SELECT non_project_time_category_id
        FROM non_project_time_categories
        WHERE category_code = @category_code
          AND is_active = TRUE;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("category_code", categoryCode);

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"Unknown or inactive non-project time category: {categoryCode}"));
}

static async Task MarkTimesheetSubmittedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid timesheetId)
{
    const string sql = """
        UPDATE timesheets
        SET status = 'submitted',
            submitted_at = NOW(),
            updated_at = NOW()
        WHERE timesheet_id = @timesheet_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    await command.ExecuteNonQueryAsync();
}

static async Task InsertAuditLogAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorUserId, string action, string entityType, Guid entityId)
{
    const string sql = """
        INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id)
        VALUES (@actor_user_id, @action, @entity_type, @entity_id);
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("actor_user_id", actorUserId);
    command.Parameters.AddWithValue("action", action);
    command.Parameters.AddWithValue("entity_type", entityType);
    command.Parameters.AddWithValue("entity_id", entityId);
    await command.ExecuteNonQueryAsync();
}

static async Task<IReadOnlyList<object>> LoadNonProjectCategoriesAsync(NpgsqlConnection connection)
{
    var categories = new List<object>();

    const string sql = """
        SELECT
            non_project_time_category_id,
            category_code,
            category_name,
            category_description,
            utilization_classification,
            utilization_bucket,
            requires_approval,
            is_active,
            display_order
        FROM non_project_time_categories
        WHERE is_active = TRUE
        ORDER BY display_order, category_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        categories.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            description = reader.IsDBNull(3) ? null : reader.GetString(3),
            utilizationClassification = reader.GetString(4),
            utilizationBucket = reader.GetString(5),
            requiresApproval = reader.GetBoolean(6),
            isActive = reader.GetBoolean(7),
            displayOrder = reader.GetInt32(8)
        });
    }

    return categories;
}





static async Task<IResult> BuildProjectPulseProductionDataReadinessResultAsync()
{
    var config = DatabaseConfig.FromEnvironment();

    if (config.Missing.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing,
            generatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var checks = new List<Dictionary<string, object?>>();

        async Task AddCountCheckAsync(
            string key,
            string label,
            string tableName,
            int readyMinimum,
            string purpose,
            string webpageCheck)
        {
            var count = await QueryProjectPulseDataReadinessCountAsync(connection, tableName);
            var tableExists = count.HasValue;
            var status = !tableExists
                ? "missing_table"
                : count.Value >= readyMinimum
                    ? "ready"
                    : "needs_data";

            checks.Add(new Dictionary<string, object?>
            {
                ["key"] = key,
                ["label"] = label,
                ["tableName"] = tableName,
                ["count"] = count ?? 0,
                ["readyMinimum"] = readyMinimum,
                ["tableExists"] = tableExists,
                ["status"] = status,
                ["purpose"] = purpose,
                ["webpageCheck"] = webpageCheck
            });
        }

        var dataAreas = new (string Key, string Label, string TableName, int ReadyMinimum, string Purpose, string WebpageCheck)[]
        {
            ("users", "Users", "app_users", 1, "Confirms real users exist for login, role assignment, approvals, and workflow ownership.", "Open User Administration or Role Administration and confirm users are present."),
            ("roles", "Roles", "app_roles", 1, "Confirms application roles exist for role-based access and dashboard/module visibility.", "Open Role / Security Administration and confirm roles and permissions are visible."),
            ("customers", "Customers", "clients", 1, "Confirms customer/account data exists for project intake, allocation, billing, and reporting.", "Open Customer Directory and confirm customer records or a clear empty state appears."),
            ("projects", "Projects", "projects", 1, "Confirms project records exist for timesheets, project workspace, resource assignment, and workload reporting.", "Open Project Workspace or Resource Assignment and confirm project data is visible."),
            ("project_tasks", "Project Tasks", "project_tasks", 1, "Confirms task-level work is available for time entry, assignment, approvals, and exports.", "Open Project Workspace and confirm project tasks are available or empty state is understandable."),
            ("timesheets", "Timesheets", "timesheets", 1, "Confirms timesheet headers exist for weekly time entry and manager approval workflows.", "Open Timesheet or Manager Approvals and confirm time workflow data loads."),
            ("time_entries", "Time Entries", "time_entries", 1, "Confirms submitted or draft time data exists for approvals, exports, utilization, and audit evidence.", "Open Workflow or Manager Approvals and confirm time data appears when expected."),
            ("manager_approvals", "Manager Approval Evidence", "manager_approval_actions", 1, "Confirms approval decision evidence exists or can be tracked for audit and export readiness.", "Open Manager Approvals and confirm approval workflow state is understandable."),
            ("exports", "Export Packages", "time_export_packages", 1, "Confirms export package evidence exists for accounting and period-close workflows.", "Open Approval / Export / Audit Workflows and confirm export readiness is visible."),
            ("audit_events", "Audit Events", "audit_events", 1, "Confirms system actions are being logged for accountability and troubleshooting.", "Open Audit History and confirm audit records or a clear empty state appears."),
            ("notification_events", "Notification Events", "notification_events", 1, "Confirms notification evidence is available for time compliance and operational messaging.", "Open notification-related pages and confirm events are visible after notification activity.")
        };

        foreach (var area in dataAreas)
        {
            await AddCountCheckAsync(
                area.Key,
                area.Label,
                area.TableName,
                area.ReadyMinimum,
                area.Purpose,
                area.WebpageCheck);
        }

        var readyCount = checks.Count(check => string.Equals(Convert.ToString(check["status"]), "ready", StringComparison.OrdinalIgnoreCase));
        var needsDataCount = checks.Count(check => string.Equals(Convert.ToString(check["status"]), "needs_data", StringComparison.OrdinalIgnoreCase));
        var missingTableCount = checks.Count(check => string.Equals(Convert.ToString(check["status"]), "missing_table", StringComparison.OrdinalIgnoreCase));

        return Results.Ok(new
        {
            status = missingTableCount == 0 && needsDataCount == 0 ? "ready" : "needs_data_review",
            generatedAtUtc = DateTimeOffset.UtcNow,
            route = "/api/production-data-readiness",
            primaryRoute = "/api/production/data-readiness",
            summary = new
            {
                checkCount = checks.Count,
                readyCount,
                needsDataCount,
                missingTableCount,
                productionDataReady = missingTableCount == 0 && needsDataCount == 0
            },
            checks
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Production data readiness failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}


static async Task<long?> QueryProjectPulseDataReadinessCountAsync(NpgsqlConnection connection, string tableName)
{
    await using (var existsCommand = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name = @table_name
        );
        """, connection))
    {
        existsCommand.Parameters.AddWithValue("table_name", tableName);
        var exists = (bool)(await existsCommand.ExecuteScalarAsync() ?? false);

        if (!exists)
        {
            return null;
        }
    }

    var safeTableName = tableName.Replace("\"", "\"\"");
    await using var countCommand = new NpgsqlCommand($"SELECT COUNT(*) FROM \"{safeTableName}\";", connection);
    return Convert.ToInt64(await countCommand.ExecuteScalarAsync() ?? 0);
}


static async Task<object> BuildTimesheetWeekPayloadAsync(NpgsqlConnection connection, Guid userId, DateOnly start)
{
    var days = Enumerable.Range(0, 7)
        .Select(offset => start.AddDays(offset))
        .Select(date => new
        {
            date,
            dayName = date.DayOfWeek.ToString(),
            normalHours = 0.00m,
            afterhours = 0.00m
        })
        .ToList();

    var categories = await LoadTimesheetNonProjectCategoriesAsync(connection);
    var timesheet = await LoadTimesheetHeaderAsync(connection, userId, start);
    var entries = timesheet?.TimesheetId is null
        ? new List<object>()
        : await LoadSavedTimeEntriesAsync(connection, timesheet.TimesheetId.Value);
    var dayStatuses = await LoadDayStatusesAsync(connection, timesheet?.TimesheetId, start);

    return new
    {
        timesheetId = timesheet?.TimesheetId,
        status = timesheet?.Status ?? "draft",
        submittedAt = timesheet?.SubmittedAt,
        dayStatuses,
        weekStart = start,
        weekEnd = start.AddDays(6),
        days,
        timeTypes = new[] { "normal", "afterhours" },
        nonProjectCategories = categories,
        entries,
        note = "Weekly shell now includes saved draft and submitted time entry payloads."
    };
}

static async Task<IReadOnlyList<object>> LoadTimesheetNonProjectCategoriesAsync(NpgsqlConnection connection)
{
    var categories = new List<object>();

    const string categorySql = """
        SELECT
            non_project_time_category_id,
            category_code,
            category_name,
            category_description,
            utilization_bucket,
            requires_approval
        FROM non_project_time_categories
        WHERE is_active = TRUE
        ORDER BY display_order, category_name;
        """;

    await using var command = new NpgsqlCommand(categorySql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        categories.Add(new
        {
            categoryId = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            description = reader.IsDBNull(3) ? null : reader.GetString(3),
            utilizationBucket = reader.GetString(4),
            requiresApproval = reader.GetBoolean(5)
        });
    }

    return categories;
}

static async Task<TimesheetHeader?> LoadTimesheetHeaderAsync(NpgsqlConnection connection, Guid userId, DateOnly weekStart)
{
    const string sql = """
        SELECT timesheet_id, status, submitted_at
        FROM timesheets
        WHERE user_id = @user_id
          AND week_start_date = @week_start_date;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new TimesheetHeader(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
}


static async Task<List<object>> LoadDayStatusesAsync(NpgsqlConnection connection, Guid? timesheetId, DateOnly weekStart)
{
    var statusByDate = new Dictionary<DateOnly, DayStatusRecord>();

    if (timesheetId is not null)
    {
        const string sql = """
            SELECT work_date, status, submitted_at
            FROM timesheet_day_statuses
            WHERE timesheet_id = @timesheet_id
            ORDER BY work_date;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("timesheet_id", timesheetId.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statusByDate[reader.GetFieldValue<DateOnly>(0)] = new DayStatusRecord(
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
        }
    }

    return Enumerable.Range(0, 7)
        .Select(offset => weekStart.AddDays(offset))
        .Select(date =>
        {
            statusByDate.TryGetValue(date, out var record);
            var status = record?.Status ?? "draft";
            var submittedAt = record?.SubmittedAt;

            return (object)new
            {
                workDate = date,
                status,
                submittedAt,
                canEdit = status is "draft" or "manager_declined",
                canUnlock = CanEngineerUnlockDay(status, submittedAt),
                unlockMessage = GetDayUnlockMessage(status, submittedAt)
            };
        })
        .ToList();
}


static async Task<List<object>> LoadSavedTimeEntriesAsync(NpgsqlConnection connection, Guid timesheetId)
{
    var entries = new List<object>();

    const string sql = """
        SELECT
            te.time_entry_id,
            te.work_date,
            te.time_type,
            te.hours,
            te.description,
            te.status,
            te.project_id,
            te.task_id,
            te.non_project_time_category_id,
            npt.category_code,
            npt.category_name,
            te.work_location_group_id,
            te.work_location_id,
            te.billable,
            p.project_code,
            p.project_name,
            pt.task_code,
            pt.task_name,
            c.client_name,
            COALESCE(
                NULLIF(to_jsonb(pt)->>'work_task_category', ''),
                NULLIF(to_jsonb(pt)->>'work_type', ''),
                'project_task'
            ) AS work_task_category,
            COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), '') AS service_request_number
        FROM time_entries te
        LEFT JOIN non_project_time_categories npt
            ON npt.non_project_time_category_id = te.non_project_time_category_id
        LEFT JOIN projects p
            ON p.project_id = te.project_id
        LEFT JOIN project_tasks pt
            ON pt.task_id = te.task_id
        LEFT JOIN clients c
            ON c.client_id = p.client_id
        WHERE te.timesheet_id = @timesheet_id
        ORDER BY te.work_date, te.time_type, COALESCE(npt.display_order, 999), COALESCE(npt.category_name, pt.task_name, p.project_name);
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var projectId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6);
        var taskId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7);
        var categoryCode = reader.IsDBNull(9) ? null : reader.GetString(9);
        var workTaskCategory = reader.GetString(19);
        var serviceRequestNumber = reader.GetString(20);
        var isServiceRequest = projectId is not null
            && taskId is not null
            && (string.Equals(
                    workTaskCategory.Trim(),
                    "service_request_task",
                    StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(serviceRequestNumber));

        entries.Add(new
        {
            id = reader.GetGuid(0),
            timeEntryId = reader.GetGuid(0),
            rowType = isServiceRequest
                ? "service_request"
                : projectId is not null && taskId is not null ? "projectTask" : "nonProject",
            workDate = reader.GetFieldValue<DateOnly>(1),
            timeType = reader.GetString(2),
            hours = reader.GetDecimal(3),
            description = reader.IsDBNull(4) ? null : reader.GetString(4),
            status = reader.GetString(5),
            projectId,
            taskId,
            nonProjectTimeCategoryId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
            categoryCode,
            categoryName = reader.IsDBNull(10) ? null : reader.GetString(10),
            workLocationGroupId = reader.IsDBNull(11) ? (Guid?)null : reader.GetGuid(11),
            workLocationId = reader.IsDBNull(12) ? (Guid?)null : reader.GetGuid(12),
            billable = reader.GetBoolean(13),
            projectCode = reader.IsDBNull(14) ? null : reader.GetString(14),
            projectName = reader.IsDBNull(15) ? null : reader.GetString(15),
            taskCode = reader.IsDBNull(16) ? null : reader.GetString(16),
            taskName = reader.IsDBNull(17) ? null : reader.GetString(17),
            clientName = reader.IsDBNull(18) ? null : reader.GetString(18),
            workTaskCategory,
            serviceRequestNumber
        });
    }

    return entries;
}







static Dictionary<string, string> ReadProjectPulseEnvFile(string path)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (!File.Exists(path)) return values;

    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();

        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;

        var index = line.IndexOf('=');
        if (index <= 0) continue;

        var key = line[..index].Trim();
        var value = line[(index + 1)..].Trim();

        if (value.Length >= 2 && value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))
        {
            value = value[1..^1].Replace("'\"'\"'", "'");
        }
        else if (value.Length >= 2 && value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
        {
            value = value[1..^1].Replace("\\\"", "\"");
        }

        values[key] = value;
    }

    return values;
}

static string QuoteProjectPulseEnvValue(string? value)
{
    return "'" + (value ?? "").Replace("'", "'\"'\"'") + "'";
}

static string MaskProjectPulseSecret(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return "";

    var trimmed = value.Trim();
    if (trimmed.Length <= 8) return "configured";

    return $"{trimmed[..4]}...{trimmed[^4..]}";
}

static Dictionary<string, string> ParseSystemctlShowProperties(string output)
{
    return output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => line.Split('=', 2))
        .Where(parts => parts.Length == 2)
        .ToDictionary(parts => parts[0], parts => parts[1]);
}

static async Task<ProjectPulseProcessResult> RunProjectPulseProcessAsync(string fileName, params string[] arguments)
{
    using var process = new System.Diagnostics.Process();

    process.StartInfo.FileName = fileName;
    process.StartInfo.RedirectStandardOutput = true;
    process.StartInfo.RedirectStandardError = true;
    process.StartInfo.UseShellExecute = false;
    process.StartInfo.CreateNoWindow = true;

    foreach (var argument in arguments)
    {
        process.StartInfo.ArgumentList.Add(argument);
    }

    try
    {
        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        var timeout = arguments.Any(argument => argument.Contains("projectpulse-backup.sh", StringComparison.OrdinalIgnoreCase))
            ? TimeSpan.FromMinutes(10)
            : arguments.Any(argument => string.Equals(argument, "restart", StringComparison.OrdinalIgnoreCase))
                ? TimeSpan.FromSeconds(60)
                : TimeSpan.FromSeconds(20);

        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            var timedOutOutput = string.Empty;
            var timedOutError = string.Empty;

            try
            {
                timedOutOutput = await standardOutputTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // Ignore incomplete output after timeout.
            }

            try
            {
                timedOutError = await standardErrorTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // Ignore incomplete error after timeout.
            }

            return new ProjectPulseProcessResult(
                124,
                timedOutOutput.Trim(),
                string.IsNullOrWhiteSpace(timedOutError)
                    ? $"timed out after {timeout.TotalSeconds:0} seconds"
                    : timedOutError.Trim());
        }

        return new ProjectPulseProcessResult(
            process.ExitCode,
            (await standardOutputTask).Trim(),
            (await standardErrorTask).Trim());
    }
    catch (Exception ex)
    {
        return new ProjectPulseProcessResult(127, string.Empty, ex.Message);
    }
}

async Task<ProjectPulseAdministratorContext> ResolveProjectPulseAdministratorContextAsync(HttpContext httpContext, NpgsqlConnection connection)
{
    var token = GetProjectPulseSessionToken(httpContext.Request);
    if (string.IsNullOrWhiteSpace(token))
    {
        return new ProjectPulseAdministratorContext(false, null, null);
    }

    var tokenHash = HashSessionToken(token);

    await using var command = new NpgsqlCommand("""
        SELECT s.user_id, u.email
        FROM auth_sessions s
        JOIN app_users u ON u.user_id = s.user_id
        WHERE s.session_token_hash = @session_token_hash
          AND s.revoked_at IS NULL
          AND s.expires_at > NOW()
          AND u.is_active = TRUE
          AND u.login_enabled = TRUE
        LIMIT 1;
        """, connection);

    command.Parameters.AddWithValue("session_token_hash", tokenHash);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return new ProjectPulseAdministratorContext(false, null, null);
    }

    var userId = reader.GetGuid(0);
    var email = reader.GetString(1);

    await reader.CloseAsync();

    var isAdministrator = await SessionUserIsAdministratorAsync(connection, userId);

    return new ProjectPulseAdministratorContext(isAdministrator, userId, email);
}

async Task<Dictionary<Guid, string[]>> LoadProjectPulseActiveRoleCodesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    IEnumerable<Guid> userIds)
{
    var normalizedUserIds = userIds.Distinct().ToArray();
    var rolesByUser = normalizedUserIds.ToDictionary(userId => userId, _ => new List<string>());

    if (normalizedUserIds.Length == 0)
    {
        return new Dictionary<Guid, string[]>();
    }

    await using var command = new NpgsqlCommand("""
        SELECT ura.user_id, r.role_code
        FROM app_user_role_assignments ura
        JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE ura.user_id = ANY(@user_ids)
          AND ura.is_active = TRUE
        ORDER BY ura.user_id, r.display_order, r.role_code;
        """, connection, transaction);
    command.Parameters.AddWithValue("user_ids", normalizedUserIds);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rolesByUser[reader.GetGuid(0)].Add(reader.GetString(1));
    }

    return rolesByUser.ToDictionary(
        pair => pair.Key,
        pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(roleCode => roleCode, StringComparer.OrdinalIgnoreCase)
            .ToArray());
}

async Task InsertProjectPulseRoleAuditAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid actorUserId,
    Guid targetUserId,
    string action,
    string reason,
    IEnumerable<string> oldRoleCodes,
    IEnumerable<string> newRoleCodes,
    HttpContext httpContext)
{
    var oldValue = JsonSerializer.Serialize(new
    {
        roleCodes = oldRoleCodes.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(roleCode => roleCode, StringComparer.OrdinalIgnoreCase).ToArray()
    });
    var newValue = JsonSerializer.Serialize(new
    {
        roleCodes = newRoleCodes.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(roleCode => roleCode, StringComparer.OrdinalIgnoreCase).ToArray(),
        reason,
        route = httpContext.Request.Path.Value ?? string.Empty
    });

    await using var command = new NpgsqlCommand("""
        INSERT INTO audit_logs (
            actor_user_id, action, entity_type, entity_id,
            old_value, new_value, ip_address, user_agent
        )
        VALUES (
            @actor_user_id, @action, 'app_user_roles', @entity_id,
            CAST(@old_value AS jsonb), CAST(@new_value AS jsonb),
            NULLIF(@ip_address, '')::inet, @user_agent
        );
        """, connection, transaction);
    command.Parameters.AddWithValue("actor_user_id", actorUserId);
    command.Parameters.AddWithValue("action", action);
    command.Parameters.AddWithValue("entity_id", targetUserId);
    command.Parameters.AddWithValue("old_value", oldValue);
    command.Parameters.AddWithValue("new_value", newValue);
    command.Parameters.AddWithValue("ip_address", httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
    command.Parameters.AddWithValue("user_agent", httpContext.Request.Headers.UserAgent.ToString());
    await command.ExecuteNonQueryAsync();
}

// SECURITY_20260729_TRANSACTIONAL_ROLE_AUDIT_HELPERS
async Task InsertProjectPulseAuditEventAsync(
    NpgsqlConnection connection,
    Guid? actorUserId,
    string action,
    string entityType,
    Guid? entityId,
    HttpContext httpContext,
    object newValue)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO audit_logs (
            actor_user_id,
            action,
            entity_type,
            entity_id,
            new_value,
            ip_address,
            user_agent
        )
        VALUES (
            @actor_user_id,
            @action,
            @entity_type,
            @entity_id,
            CAST(@new_value AS jsonb),
            NULLIF(@ip_address, '')::inet,
            @user_agent
        );
        """, connection);

    command.Parameters.AddWithValue("actor_user_id", actorUserId is null ? DBNull.Value : actorUserId.Value);
    command.Parameters.AddWithValue("action", action);
    command.Parameters.AddWithValue("entity_type", entityType);
    command.Parameters.AddWithValue("entity_id", entityId is null ? DBNull.Value : entityId.Value);
    command.Parameters.AddWithValue("new_value", JsonSerializer.Serialize(newValue));
    command.Parameters.AddWithValue("ip_address", httpContext.Connection.RemoteIpAddress?.ToString() ?? "");
    command.Parameters.AddWithValue("user_agent", httpContext.Request.Headers.UserAgent.ToString());

    await command.ExecuteNonQueryAsync();
}




static async Task<int> QueueProjectCostAlertNotificationsAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid alertId,
    Guid projectId,
    string alertType,
    string alertSeverity,
    string projectCode,
    string projectName,
    string clientName,
    decimal plannedTotalProjectCost,
    decimal assignedHours,
    decimal usedHours,
    decimal overAssignedHours,
    string costStatus)
{
    var recipients = new List<(string Email, string Name, string Role)>();

    await using (var recipientCommand = new NpgsqlCommand("""
        WITH project_context AS (
            SELECT p.project_id, p.project_manager_user_id
            FROM projects p
            WHERE p.project_id = @project_id
        ),
        pm_recipients AS (
            SELECT DISTINCT
                pm.email,
                pm.display_name,
                'Project Manager'::text AS recipient_role
            FROM project_context pc
            JOIN app_users pm ON pm.user_id = pc.project_manager_user_id
            WHERE pm.is_active = TRUE
              AND COALESCE(pm.email, '') <> ''
        ),
        manager_recipients AS (
            SELECT DISTINCT
                manager.email,
                manager.display_name,
                'Resource Manager'::text AS recipient_role
            FROM project_assignments pa
            JOIN app_users engineer ON engineer.user_id = pa.user_id
            JOIN app_users manager ON lower(manager.email) = lower(engineer.manager_email)
            WHERE pa.project_id = @project_id
              AND manager.is_active = TRUE
              AND COALESCE(manager.email, '') <> ''
        ),
        ptc_recipients AS (
            SELECT DISTINCT
                u.email,
                u.display_name,
                'Project Team Coordinator'::text AS recipient_role
            FROM app_users u
            JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE r.role_code = 'PROJECT_TEAM_COORDINATOR'
              AND r.is_active = TRUE
              AND u.is_active = TRUE
              AND COALESCE(u.email, '') <> ''
        )
        SELECT DISTINCT ON (lower(email))
            email,
            display_name,
            recipient_role
        FROM (
            SELECT * FROM pm_recipients
            UNION ALL
            SELECT * FROM manager_recipients
            UNION ALL
            SELECT * FROM ptc_recipients
        ) recipients
        ORDER BY lower(email), recipient_role;
        """, connection, transaction))
    {
        recipientCommand.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await recipientCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            recipients.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                reader.GetString(2)
            ));
        }
    }

    if (recipients.Count == 0)
    {
        return 0;
    }

    var subject = $"Project Pulse Cost Alert: {projectCode} - {alertSeverity.ToUpperInvariant()}";
    var body = $"""
Project Pulse detected a project cost/readiness alert.

Project: {projectCode} - {projectName}
Customer: {clientName}
Alert Type: {alertType}
Severity: {alertSeverity}
Cost Status: {costStatus}

Planned Total Project Cost: {plannedTotalProjectCost:C}
Assigned Hours: {assignedHours:N2}
Used Hours: {usedHours:N2}
Over Assigned Hours: {overAssignedHours:N2}

Please review project assignment, time usage, and cost plan readiness in Project Pulse.
""";

    foreach (var recipient in recipients)
    {
        await using (var notifyCommand = new NpgsqlCommand("""
            INSERT INTO notification_outbox (
                notification_type,
                recipient_email,
                subject,
                body,
                related_entity_type,
                related_entity_id
            )
            VALUES (
                'project_cost_alert',
                @recipient_email,
                @subject,
                @body,
                'project_cost_alert',
                @related_entity_id
            );
            """, connection, transaction))
        {
            notifyCommand.Parameters.AddWithValue("recipient_email", recipient.Email);
            notifyCommand.Parameters.AddWithValue("subject", subject);
            notifyCommand.Parameters.AddWithValue("body", body);
            notifyCommand.Parameters.AddWithValue("related_entity_id", alertId);
            await notifyCommand.ExecuteNonQueryAsync();
        }

        await using (var emailCommand = new NpgsqlCommand("""
            INSERT INTO email_notification_outbox (
                rule_code,
                recipient_email,
                recipient_name,
                subject,
                body,
                status,
                scheduled_for
            )
            VALUES (
                'PROJECT_COST_ALERT',
                @recipient_email,
                @recipient_name,
                @subject,
                @body,
                'queued',
                NOW()
            );
            """, connection, transaction))
        {
            emailCommand.Parameters.AddWithValue("recipient_email", recipient.Email);
            emailCommand.Parameters.AddWithValue("recipient_name", recipient.Name);
            emailCommand.Parameters.AddWithValue("subject", subject);
            emailCommand.Parameters.AddWithValue("body", body);
            await emailCommand.ExecuteNonQueryAsync();
        }
    }

    return recipients.Count;
}



async Task<ApprovalExportWorkflowAccess> LoadApprovalExportWorkflowAccessAsync(NpgsqlConnection connection, Guid userId)
{
    var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    await using var command = new NpgsqlCommand("""
        SELECT
            r.role_code,
            COALESCE(p.permission_code, '') AS permission_code
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

        if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
        {
            permissions.Add(reader.GetString(1));
        }
    }

    var canViewAll =
        (roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR"))
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canProjectApprove =
        canViewAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || (roles.Contains("PROJECT_MANAGEMENT") || roles.Contains("PROJECT_MANAGER"))
        || permissions.Contains("PROJECT_TIME_APPROVAL");

    var canManageAccounting =
        canViewAll
        || permissions.Contains("MANAGE_ACCOUNT_RECONCILIATION");

    var canExport =
        canViewAll
        || permissions.Contains("EXPORT_TIME_EXCEL")
        || permissions.Contains("EXPORT_TIME_PDF");

    var canAudit =
        canViewAll
        || permissions.Contains("VIEW_AUDIT_TRAIL");

    var canView =
        canViewAll
        || canProjectApprove
        || canManageAccounting
        || canExport
        || canAudit
        || permissions.Contains("VIEW_APPROVAL_WORKFLOW")
        || permissions.Contains("VIEW_ACCOUNT_RECONCILIATION");

    return new ApprovalExportWorkflowAccess(
        CanView: canView,
        CanProjectApprove: canProjectApprove,
        CanManageAccounting: canManageAccounting,
        CanExport: canExport,
        CanAudit: canAudit,
        CanViewAll: canViewAll);
}


internal sealed record ProjectPulseBackupDeleteRequest(string RequestId, string? Reason);
internal sealed record ProjectPulseBackupRunRequest(bool UploadToSftp, bool? UploadToAzure, string? Reason);
internal sealed record ServiceRestartRequest(string ServiceKey, string Reason);
internal sealed record ProjectPulseProcessResult(int ExitCode, string StandardOutput, string StandardError);
internal sealed record ProjectPulseAdministratorContext(bool IsAdministrator, Guid? UserId, string? Email);

internal sealed record ProjectAllocationProjectUpsertRequest(
    string ProjectCode,
    string ProjectName,
    string? CustomerName,
    string? ServiceRequestNumber,
    string? ProjectStatus,
    List<ProjectAllocationEngineerRequest>? Allocations);

internal sealed record ProjectAllocationEngineerRequest(
    Guid UserId,
    decimal AllocatedHours,
    string? Notes);

internal sealed record ProjectDocumentPurgeRequest(
    int OlderThanDays,
    bool IncludeActiveProjects,
    string? PurgeReason);


internal sealed record TimesheetDaySubmitRequest(DateOnly WeekStart, DateOnly WorkDate, List<TimesheetEntryRequest> Entries);

internal sealed record TimesheetDayUnlockRequest(DateOnly WeekStart, DateOnly WorkDate);

internal sealed record ManagerBulkApprovalRequest(List<ManagerApprovalActionRequest> Items, string? Comment);

internal sealed record ManagerApprovalActionRequest(Guid TimesheetId, DateOnly WorkDate, string? Comment);

internal sealed record TimesheetPreferenceRequest(List<string>? DefaultNonProjectCategoryCodes, List<Guid>? DefaultProjectTaskIds, bool AutoAddHolidays, bool WeeklyReminderEnabled);

internal sealed record HolidayCsvImportRequest(int? Year, string? Filename, string CsvText);
internal sealed record HolidayImportRow(DateOnly HolidayDate, string HolidayName, string HolidayType, bool IsFloatingHoliday, decimal AutoPopulateHours);

internal sealed record UserRoleAssignmentRequest(string Email, List<string>? RoleCodes, string? Reason);





internal sealed record UserAdminBulkUpdateRequest(
    List<Guid>? UserIds,
    bool ApplyJobTitle,
    string? JobTitle,
    bool ApplyDepartmentName,
    string? DepartmentName,
    bool ApplyTeamName,
    string? TeamName,
    bool ApplyOfficeLocation,
    string? OfficeLocation,
    bool ApplyManagerEmail,
    string? ManagerEmail,
    bool ApplyLoginEnabled,
    bool LoginEnabled,
    bool ApplyIsActive,
    bool IsActive,
    string? RoleUpdateMode,
    List<string>? RoleCodes,
    string? Reason);


internal sealed record UserAdminProfileUpdateRequest(
    Guid UserId,
    string? Email,
    string? DisplayName,
    string? JobTitle,
    string? DepartmentName,
    string? TeamName,
    string? OfficeLocation,
    string? ManagerEmail,
    bool LoginEnabled,
    bool IsActive);

internal sealed record UserAdminRoleUpdateRequest(
    Guid UserId,
    List<string>? RoleCodes,
    string? Reason);

internal sealed record UserAdminLocalPasswordUpdateRequest(
    Guid UserId,
    string TemporaryPassword,
    bool MustChangePassword,
    string? Notes);


internal sealed record AzureAdminConfigRequest(
    string? TenantId,
    string? ClientId,
    string? AuthorityUrl,
    string? RedirectUri,
    string? GraphScope,
    bool SyncEnabled,
    string? DefaultRoleCode,
    int SyncFrequencyHours);

internal sealed record AzureUserImportRequest(List<AzureUserImportRow>? Users);

internal sealed record AzureUserImportRow(
    string? Email,
    string? DisplayName,
    string? EntraObjectId,
    string? JobTitle,
    string? DepartmentName,
    string? OfficeLocation,
    string? ManagerEmail);


internal sealed record LocalLoginRequest(string Username, string Password);
internal sealed record SsoDevelopmentLoginRequest(string Email);
internal sealed record SetTemporaryPasswordRequest(Guid ResetRequestId, string Username, string TemporaryPassword);
internal sealed record ChangeLocalPasswordRequest(string CurrentPassword, string NewPassword);
internal sealed
record ProjectPulseEntraImportSettings(
    string EnvironmentMode,
    string TenantDomain,
    string SourceProvider,
    string ImportSourceType,
    string? GraphGroupId,
    string? GraphFilter,
    string DefaultRoleCode,
    bool DisableMissingFromSource);

record ProjectPulseGraphUser(
    string Id,
    string DisplayName,
    string Email,
    string? UserPrincipalName,
    string? JobTitle,
    string? Department,
    string? OfficeLocation,
    bool AccountEnabled);

record ProjectPulseImportSelectedUsersRequest(
    List<string> EntraObjectIds);

record ProjectPulseImportSettingsUpdateRequest(
    string EnvironmentMode,
    string TenantDomain,
    string SourceProvider,
    string ImportSourceType,
    string? GraphGroupId,
    string? GraphFilter,
    string DefaultRoleCode,
    bool DisableMissingFromSource);


record ProjectPulseCreatedSession(Guid SessionId, string RawToken, DateTimeOffset ExpiresAt);
record ProjectPulseViewAsUser(Guid UserId, string Email);

internal sealed record ProjectPulseSessionValidation(bool IsValid, Guid? UserId, string? Email, string? ProviderCode, DateTimeOffset? ExpiresAt, string? Message);

internal sealed record PasswordResetCompletionRequest(Guid ResetRequestId, string TemporaryPassword, string? ActionByEmail, string? Notes);



internal sealed record PasswordResetApprovalAction(Guid ResetRequestId, string? ActionByEmail, string? Notes);

internal sealed record PasswordResetRequest(string Username, string? Notes);

internal sealed record PtcTimeEntryCorrectionRequest(
    Guid TimeEntryId,
    Guid TargetProjectId,
    Guid TargetTaskId,
    string? Operation,
    decimal? SplitHours,
    string? Reason);

internal sealed record TimesheetSaveRequest(DateOnly WeekStart, List<TimesheetEntryRequest> Entries);

internal sealed record TimesheetEntryRequest(
    string RowType,
    string? CategoryCode,
    DateOnly WorkDate,
    string TimeType,
    decimal Hours,
    string? Description,
    Guid? WorkLocationGroupId,
    Guid? WorkLocationId,
    Guid? ProjectId,
    Guid? TaskId);

internal sealed record TimesheetHeader(Guid? TimesheetId, string Status, DateTimeOffset? SubmittedAt);

internal sealed record DayStatusRecord(string Status, DateTimeOffset? SubmittedAt);

internal sealed record DatabaseConfig(
    string? Host,
    string? Port,
    string? Database,
    string? Username,
    string? Password,
    IReadOnlyList<string> Missing)
{
    public string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = int.TryParse(Port, out var parsedPort) ? parsedPort : 5432,
                Database = Database,
                Username = Username,
                Password = Password,
                IncludeErrorDetail = false,
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = 5
            };

            return builder.ConnectionString;
        }
    }

    public static DatabaseConfig FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(host)) missing.Add("PTP_DB_HOST");
        if (string.IsNullOrWhiteSpace(port)) missing.Add("PTP_DB_PORT");
        if (string.IsNullOrWhiteSpace(database)) missing.Add("PTP_DB_NAME");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("PTP_DB_USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("PTP_DB_PASSWORD");

        return new DatabaseConfig(host, port, database, username, password, missing);
    }
}
record UserAdminLocalUserCreateRequest(
    string Email,
    string DisplayName,
    string? TemporaryPassword,
    bool MustChangePassword,
    string? JobTitle,
    string? DepartmentName,
    string? TeamName,
    string? OfficeLocation,
    string? ManagerEmail,
    List<string>? RoleCodes);

record UserAdminUserLifecycleRequest(
    Guid UserId,
    string? Reason);


record ProjectPulseAiTimeEntrySuggestionRequest(
    DateOnly WorkDate,
    Guid? TimeEntryId,
    Guid? AssignmentId,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? NonProjectTimeCategoryId,
    string? TimeType,
    string? RowType,
    string? RowLabel,
    string? CustomerName,
    string? ProjectName,
    string? ProjectCode,
    string? TaskName,
    string? TaskCode,
    string? CategoryCode,
    decimal? Hours,
    string? CurrentDescription);

record ProjectPulseAiTimeEntrySuggestionResult(
    string Suggestion,
    string Provider,
    string? Warning,
    IReadOnlyList<ProjectPulseAiTargetDecision>? TargetDecisions = null);


internal sealed record ProjectPulseReplicationSyncSettingsRequest(
    string? PeerName,
    string? PeerHost,
    string? PeerUrl,
    int? StaleBackupHours);


internal sealed record ProjectPulseRestoreValidationSettingsRequest(string? SelectedBackup);


internal sealed record ProjectPulseBackupRetentionDeleteRequest(
    string? BackupName,
    string? Reason,
    bool? Confirm);



internal sealed record CustomerDirectoryClientUpsertRequest(
    string ClientName,
    string? ClientCode,
    bool? IsActive);

internal sealed record CustomerDirectoryContactUpsertRequest(
    string ContactName,
    string? Title,
    string? RoleDescription,
    string? Email,
    string? Phone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? Country,
    bool? IsPrimary,
    bool? IsActive,
    int? DisplayOrder);







internal sealed record ApprovalExportWorkflowAccess(
    bool CanView,
    bool CanProjectApprove,
    bool CanManageAccounting,
    bool CanExport,
    bool CanAudit,
    bool CanViewAll);


internal sealed record ApprovalExportWorkflowActionRequest(
    Guid TimesheetId,
    DateOnly WorkDate,
    string? Action,
    string? Comment);


internal sealed record TimeWorkflowExportCreateRequest(
    string? ExportFormat,
    DateOnly? WeekStart,
    DateOnly? WeekEnd,
    string? Notes);


internal sealed record ProjectCostAlertStatusUpdateRequest(
    string? AlertStatus,
    string? Note);


internal sealed record ProjectCostAlertReleaseNotificationRequest(
    string? RoutingNote);


internal sealed record ProjectCostAlertEvaluationRequest(
    bool? QueueNotifications,
    decimal? AssignmentWarningThresholdHours);

// 022C Production Notification DTO Records
record ProjectPulse022CRoutingRule(
    Guid RoutingRuleId,
    string RuleKey,
    string ModuleKey,
    string Severity,
    string[] TargetRoleCodes,
    bool DefaultInAppEnabled,
    bool AllowUserOptOut,
    bool AllowEmailDelivery,
    bool IsActive,
    string RuleDescription,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

record ProjectPulse022CUserPreference(
    Guid PreferenceId,
    string ModuleKey,
    string Severity,
    bool InAppEnabled,
    bool EmailEnabled,
    DateTimeOffset? MutedUntilUtc,
    DateTimeOffset UpdatedAt);


// 030_ROLE_CLEANUP_PHASE2_COMPATIBILITY
// Canonical roles are now ENGINEERING, ENGINEERING_LEAD, PROJECT_MANAGEMENT_LEAD, and SUPER_ADMINISTRATOR.
// Legacy role codes remain temporarily recognized until Phase 3 role retirement is complete.
