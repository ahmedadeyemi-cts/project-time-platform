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

app.UseProjectPulseSecurityHardening();
app.UseWorkRegisterAuthorization();




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

    while (start.DayOfWeek != DayOfWeek.Monday)
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
            COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), '') AS service_request_number,
            pt.billable AS billable,
            COALESCE(pt.utilization_bucket, CASE WHEN pt.billable THEN 'billable' ELSE 'non_billable' END) AS utilization_bucket,
            COALESCE(pm.display_name, 'No PM assigned') AS project_manager_name,
            COALESCE(NULLIF(p.work_type, ''), 'Project') AS work_type,
            CASE
                WHEN lower(COALESCE(NULLIF(p.work_type, ''), 'Project')) IN ('project', 'iqs')
                    THEN 'regular'
                ELSE 'requests'
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
        var isServiceRequest = string.Equals(
                workTaskCategory.Trim(),
                "service_request_task",
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(serviceRequestNumber);

        tasks.Add(new
        {
            assignmentId = reader.GetGuid(O("assignment_id")),
            projectId = reader.GetGuid(O("project_id")),
            taskId = reader.GetGuid(O("task_id")),
            projectCode = reader.GetString(O("project_code")),
            projectName = reader.GetString(O("project_name")),
            clientName = reader.GetString(O("client_name")),
            taskCode = reader.GetString(O("task_code")),
            taskName = reader.GetString(O("task_name")),
            taskDescription = reader.IsDBNull(O("task_description")) ? null : reader.GetString(O("task_description")),
            rowType = isServiceRequest ? "service_request" : "projectTask",
            workTaskCategory,
            serviceRequestNumber,
            billable = reader.GetBoolean(O("billable")),
            utilizationBucket = reader.GetString(O("utilization_bucket")),
            projectManagerName = reader.GetString(O("project_manager_name")),
            workType = reader.GetString(O("work_type")),
            timeEntrySection = reader.GetString(O("time_entry_section")),
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
                "The governed local template generated a time-entry description suggestion.",
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
                STRING_AGG(DISTINCT ps.project_code || ' Â· ' || ps.project_name, '; ' ORDER BY ps.project_code || ' Â· ' || ps.project_name) AS project_labels,
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
                STRING_AGG(DISTINCT ps.project_code || ' Â· ' || ps.project_name, '; ' ORDER BY ps.project_code || ' Â· ' || ps.project_name) AS project_labels,
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
            message = $"Project link confirmed: {requestNumber} â†’ {projectCode}."
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
                STRING_AGG(DISTINCT pt.task_code || ' Â· ' || pt.task_name, '; ' ORDER BY pt.task_code || ' Â· ' || pt.task_name) AS task_labels
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
            message = "Project intake aging is available to Project Coordinators, PMs, PTC, and Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    static string? CleanNullableString(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetString(ordinal);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    var items = new List<object>();
    var missingSignedDate = 0;
    var reminder7Day = 0;
    var reminder14Day = 0;
    var escalation21Day = 0;
    var onTrack = 0;

    await using (var command = new NpgsqlCommand("""
        WITH resource_counts AS (
            SELECT
                project_intake_request_id,
                COUNT(*)::bigint AS resource_request_count,
                MIN(created_at)::text AS first_resource_request_at
            FROM engineering_resource_requests
            WHERE project_intake_request_id IS NOT NULL
            GROUP BY project_intake_request_id
        ),
        document_counts AS (
            SELECT
                project_intake_request_id,
                COUNT(*)::bigint AS active_document_count,
                COUNT(*)::bigint AS total_document_count,
                NULL::text AS last_document_uploaded_at
            FROM project_intake_documents
            GROUP BY project_intake_request_id
        ),
        history_counts AS (
            SELECT
                project_intake_request_id,
                COUNT(*)::bigint AS change_count,
                MAX(created_at)::text AS last_change_at
            FROM project_intake_change_history
            GROUP BY project_intake_request_id
        ),
        aging_base AS (
            SELECT
                pir.project_intake_request_id,
                COALESCE(pir.request_number, '') AS request_number,
                COALESCE(pir.client_name, '') AS client_name,
                COALESCE(pir.request_title, '') AS request_title,
                COALESCE(pir.intake_status, 'new') AS intake_status,
                COALESCE(pir.priority, 'normal') AS priority,
                pir.project_signed_date,
                COALESCE(GREATEST(0, CURRENT_DATE - pir.project_signed_date), 0)::integer AS signed_age_days,
                pir.created_at::text AS created_at_text,
                pir.updated_at::text AS updated_at_text,
                pir.triage_started_at::text AS triage_started_at_text,
                COALESCE(pir.resource_request_started_at::text, rc.first_resource_request_at) AS resource_request_started_at_text,
                pir.pm_assignment_started_at::text AS pm_assignment_started_at_text,
                pir.assigned_pm_user_id,
                COALESCE(pm.display_name, '') AS assigned_pm_name,
                COALESCE(pm.email, '') AS assigned_pm_email,
                COALESCE(rc.resource_request_count, 0)::bigint AS resource_request_count,
                COALESCE(dc.active_document_count, 0)::bigint AS active_document_count,
                COALESCE(dc.total_document_count, 0)::bigint AS total_document_count,
                dc.last_document_uploaded_at,
                COALESCE(hc.change_count, 0)::bigint AS change_count,
                hc.last_change_at,
                pir.last_post_intake_edit_note,
                pir.aging_notification_stage,
                pir.aging_notification_last_evaluated_at::text AS aging_notification_last_evaluated_at_text,
                pir.aging_notification_last_message,
                (
                    pir.triage_started_at IS NOT NULL
                    OR LOWER(COALESCE(pir.intake_status, 'new')) IN ('triage', 'requested', 'resource_requested', 'assigned', 'active', 'approved')
                ) AS triage_started_flag
            FROM project_intake_requests pir
            LEFT JOIN app_users pm
                ON pm.user_id = pir.assigned_pm_user_id
            LEFT JOIN resource_counts rc
                ON rc.project_intake_request_id = pir.project_intake_request_id
            LEFT JOIN document_counts dc
                ON dc.project_intake_request_id = pir.project_intake_request_id
            LEFT JOIN history_counts hc
                ON hc.project_intake_request_id = pir.project_intake_request_id
        )
        SELECT
            project_intake_request_id,
            request_number,
            client_name,
            request_title,
            intake_status,
            priority,
            COALESCE(project_signed_date::text, '') AS project_signed_date_text,
            signed_age_days,
            CASE
                WHEN project_signed_date IS NULL THEN 'missing_signed_date'
                WHEN signed_age_days >= 21 AND (triage_started_flag = FALSE OR resource_request_count = 0 OR assigned_pm_user_id IS NULL) THEN 'escalation_21_day'
                WHEN signed_age_days >= 14 AND (resource_request_count = 0 OR assigned_pm_user_id IS NULL) THEN 'reminder_14_day'
                WHEN signed_age_days >= 7 AND triage_started_flag = FALSE THEN 'reminder_7_day'
                ELSE 'on_track'
            END AS aging_stage,
            CASE
                WHEN project_signed_date IS NULL THEN 'Signed date is not recorded yet.'
                WHEN signed_age_days >= 21 AND (triage_started_flag = FALSE OR resource_request_count = 0 OR assigned_pm_user_id IS NULL) THEN '21+ days since signed date with incomplete movement. Escalate to PTC and assigned PM/manager if known.'
                WHEN signed_age_days >= 14 AND (resource_request_count = 0 OR assigned_pm_user_id IS NULL) THEN '14+ days since signed date with no resource request or PM assignment. Notify Project Coordinator and PTC.'
                WHEN signed_age_days >= 7 AND triage_started_flag = FALSE THEN '7+ days since signed date with no triage movement. Notify Project Coordinator.'
                ELSE 'Signed-date aging is currently on track.'
            END AS aging_message,
            created_at_text,
            updated_at_text,
            COALESCE(triage_started_at_text, '') AS triage_started_at_text,
            COALESCE(resource_request_started_at_text, '') AS resource_request_started_at_text,
            COALESCE(pm_assignment_started_at_text, '') AS pm_assignment_started_at_text,
            assigned_pm_user_id,
            assigned_pm_name,
            assigned_pm_email,
            resource_request_count,
            active_document_count,
            total_document_count,
            COALESCE(last_document_uploaded_at, '') AS last_document_uploaded_at,
            change_count,
            COALESCE(last_change_at, '') AS last_change_at,
            COALESCE(last_post_intake_edit_note, '') AS last_post_intake_edit_note,
            COALESCE(aging_notification_stage, '') AS aging_notification_stage,
            COALESCE(aging_notification_last_evaluated_at_text, '') AS aging_notification_last_evaluated_at_text,
            COALESCE(aging_notification_last_message, '') AS aging_notification_last_message
        FROM aging_base
        ORDER BY
            COALESCE(project_signed_date, created_at_text::date) ASC,
            created_at_text DESC
        LIMIT 120;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var agingStage = reader.GetString(8);

            switch (agingStage)
            {
                case "missing_signed_date":
                    missingSignedDate++;
                    break;
                case "reminder_7_day":
                    reminder7Day++;
                    break;
                case "reminder_14_day":
                    reminder14Day++;
                    break;
                case "escalation_21_day":
                    escalation21Day++;
                    break;
                default:
                    onTrack++;
                    break;
            }

            items.Add(new
            {
                intakeId = reader.GetGuid(0),
                requestNumber = reader.GetString(1),
                clientName = reader.GetString(2),
                requestTitle = reader.GetString(3),
                intakeStatus = reader.GetString(4),
                priority = reader.GetString(5),
                projectSignedDate = CleanNullableString(reader, 6),
                signedAgeDays = Convert.ToInt32(reader.GetValue(7)),
                agingStage,
                agingMessage = reader.GetString(9),
                createdAt = CleanNullableString(reader, 10),
                updatedAt = CleanNullableString(reader, 11),
                triageStartedAt = CleanNullableString(reader, 12),
                resourceRequestStartedAt = CleanNullableString(reader, 13),
                pmAssignmentStartedAt = CleanNullableString(reader, 14),
                assignedPmUserId = reader.IsDBNull(15) ? (Guid?)null : reader.GetGuid(15),
                assignedPmName = reader.GetString(16),
                assignedPmEmail = reader.GetString(17),
                resourceRequestCount = reader.GetInt64(18),
                activeDocumentCount = reader.GetInt64(19),
                totalDocumentCount = reader.GetInt64(20),
                lastDocumentUploadedAt = CleanNullableString(reader, 21),
                changeCount = reader.GetInt64(22),
                lastChangeAt = CleanNullableString(reader, 23),
                lastPostIntakeEditNote = CleanNullableString(reader, 24),
                lastNotificationStage = CleanNullableString(reader, 25),
                lastNotificationEvaluatedAt = CleanNullableString(reader, 26),
                lastNotificationMessage = CleanNullableString(reader, 27)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AN Project Intake Aging",
        canManage,
        summary = new
        {
            totalIntakes = items.Count,
            missingSignedDate,
            reminder7Day,
            reminder14Day,
            escalation21Day,
            onTrack
        },
        items
    });
});

app.MapPut("/api/project-intake/{intakeId:guid}/post-intake", async (Guid intakeId, JsonElement payload, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    string? GetString(string name)
    {
        if (!payload.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        return value.GetString()?.Trim();
    }

    DateOnly? GetDate(string name)
    {
        var raw = GetString(name);
        return DateOnly.TryParse(raw, out var parsed) ? parsed : null;
    }

    var requestTitle = GetString("requestTitle");
    var requestDescription = GetString("requestDescription");
    var intakeStatus = GetString("intakeStatus");
    var priority = GetString("priority");
    var projectSignedDate = GetDate("projectSignedDate");
    var targetStartDate = GetDate("targetStartDate");
    var targetCompletionDate = GetDate("targetCompletionDate");
    var updateNote = GetString("updateNote");

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    bool canManage;
    await using (var accessCommand = new NpgsqlCommand("""
        SELECT BOOL_OR(
            r.role_code IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
            OR p.permission_code IN ('MANAGE_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE_AGING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
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
        canManage = (bool?)await accessCommand.ExecuteScalarAsync() == true;
    }

    if (!canManage)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Post-intake edits are restricted to Project Coordinators, PTC, and Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    string? previousSnapshot;
    await using (var previousCommand = new NpgsqlCommand("""
        SELECT to_jsonb(pir)::text
        FROM project_intake_requests pir
        WHERE pir.project_intake_request_id = @intake_id;
        """, connection, transaction))
    {
        previousCommand.Parameters.AddWithValue("intake_id", intakeId);
        previousSnapshot = (string?)await previousCommand.ExecuteScalarAsync();
    }

    if (previousSnapshot is null)
    {
        await transaction.RollbackAsync();
        return Results.NotFound(new { status = "intake_not_found", message = "Project intake request was not found." });
    }

    await using (var updateCommand = new NpgsqlCommand("""
        UPDATE project_intake_requests
        SET
            request_title = CASE WHEN @request_title <> '' THEN @request_title ELSE request_title END,
            request_description = CASE WHEN @request_description <> '' THEN @request_description ELSE request_description END,
            intake_status = CASE WHEN @intake_status <> '' THEN @intake_status ELSE intake_status END,
            priority = CASE WHEN @priority <> '' THEN @priority ELSE priority END,
            project_signed_date = COALESCE(@project_signed_date, project_signed_date),
            signed_date_recorded_at = CASE WHEN @project_signed_date IS NULL THEN signed_date_recorded_at ELSE NOW() END,
            signed_date_recorded_by_user_id = CASE WHEN @project_signed_date IS NULL THEN signed_date_recorded_by_user_id ELSE @updated_by_user_id END,
            target_start_date = COALESCE(@target_start_date, target_start_date),
            target_completion_date = COALESCE(@target_completion_date, target_completion_date),
            triage_started_at = CASE
                WHEN triage_started_at IS NULL AND LOWER(COALESCE(NULLIF(@intake_status, ''), intake_status)) IN ('triage', 'requested', 'resource_requested', 'assigned', 'active')
                THEN NOW()
                ELSE triage_started_at
            END,
            resource_request_started_at = CASE
                WHEN resource_request_started_at IS NULL AND LOWER(COALESCE(NULLIF(@intake_status, ''), intake_status)) IN ('resource_requested', 'assigned', 'active')
                THEN NOW()
                ELSE resource_request_started_at
            END,
            pm_assignment_started_at = CASE
                WHEN pm_assignment_started_at IS NULL AND assigned_pm_user_id IS NOT NULL
                THEN NOW()
                ELSE pm_assignment_started_at
            END,
            post_intake_edit_count = post_intake_edit_count + 1,
            last_post_intake_edit_at = NOW(),
            last_post_intake_edit_by_user_id = @updated_by_user_id,
            last_post_intake_edit_note = NULLIF(@update_note, ''),
            updated_at = NOW()
        WHERE project_intake_request_id = @intake_id;
        """, connection, transaction))
    {
        updateCommand.Parameters.AddWithValue("intake_id", intakeId);
        updateCommand.Parameters.AddWithValue("updated_by_user_id", sessionUserId.Value);
        updateCommand.Parameters.AddWithValue("request_title", requestTitle ?? "");
        updateCommand.Parameters.AddWithValue("request_description", requestDescription ?? "");
        updateCommand.Parameters.AddWithValue("intake_status", intakeStatus ?? "");
        updateCommand.Parameters.AddWithValue("priority", priority ?? "");
        updateCommand.Parameters.AddWithValue("project_signed_date", (object?)projectSignedDate ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("target_start_date", (object?)targetStartDate ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("target_completion_date", (object?)targetCompletionDate ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("update_note", updateNote ?? "Post-intake update recorded.");

        await updateCommand.ExecuteNonQueryAsync();
    }

    string? newSnapshot;
    await using (var newCommand = new NpgsqlCommand("""
        SELECT to_jsonb(pir)::text
        FROM project_intake_requests pir
        WHERE pir.project_intake_request_id = @intake_id;
        """, connection, transaction))
    {
        newCommand.Parameters.AddWithValue("intake_id", intakeId);
        newSnapshot = (string?)await newCommand.ExecuteScalarAsync();
    }

    await using (var historyCommand = new NpgsqlCommand("""
        INSERT INTO project_intake_change_history (
            project_intake_request_id,
            changed_by_user_id,
            change_type,
            change_summary,
            previous_snapshot,
            new_snapshot
        )
        VALUES (
            @intake_id,
            @changed_by_user_id,
            'post_intake_update',
            @change_summary,
            @previous_snapshot::jsonb,
            @new_snapshot::jsonb
        );
        """, connection, transaction))
    {
        historyCommand.Parameters.AddWithValue("intake_id", intakeId);
        historyCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        historyCommand.Parameters.AddWithValue("change_summary", string.IsNullOrWhiteSpace(updateNote) ? "Post-intake update recorded." : updateNote);
        historyCommand.Parameters.AddWithValue("previous_snapshot", previousSnapshot);
        historyCommand.Parameters.AddWithValue("new_snapshot", newSnapshot ?? previousSnapshot);
        await historyCommand.ExecuteNonQueryAsync();
    }

    await InsertAuditLogAsync(connection, transaction, sessionUserId.Value, "project_intake_post_intake_updated", "project_intake", intakeId);

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "post_intake_updated",
        intakeId,
        message = "Post-intake information was updated and audit history was recorded."
    });
});

app.MapPost("/api/project-intake/{intakeId:guid}/supporting-documents/upload", async (Guid intakeId, HttpContext httpContext) =>
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

    bool canManage;
    await using (var accessCommand = new NpgsqlCommand("""
        SELECT BOOL_OR(
            r.role_code IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
            OR p.permission_code IN ('MANAGE_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE_AGING', 'MANAGE_PROJECT_DOCUMENTS', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
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
        canManage = (bool?)await accessCommand.ExecuteScalarAsync() == true;
    }

    if (!canManage)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Post-intake document upload is restricted to Project Coordinators, PTC, and Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var form = await httpContext.Request.ReadFormAsync();
    var file = form.Files.GetFile("file");

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "A supporting document file is required." });
    }

    if (file.Length > 50 * 1024 * 1024)
    {
        return Results.BadRequest(new { status = "file_too_large", message = "Document uploads are limited to 50 MB." });
    }

    if (!ProjectDocumentExtensionIsAllowed(file.FileName))
    {
        return Results.BadRequest(new { status = "file_type_not_allowed", message = "Allowed file types are PDF, Word, Excel, and CSV." });
    }

    var documentType = form["documentType"].ToString().Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(documentType)) documentType = "supporting_document";

    var replaceExisting = string.Equals(form["replaceExisting"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
    var engineeringVisible = !string.Equals(form["engineeringVisible"].ToString(), "false", StringComparison.OrdinalIgnoreCase);
    var aiTimesheetContextEnabled =
        string.Equals(form["aiTimesheetContextEnabled"].ToString(), "true", StringComparison.OrdinalIgnoreCase)
        || documentType is "sow" or "gsd";

    var safeOriginalFileName = Path.GetFileName(file.FileName);
    var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(safeOriginalFileName)}"; // SECURITY_20260729_SAFE_DOCUMENT_PATH_COMPONENT
    var uploadRoot = GetProjectPulseUploadRoot();
    var requestFolder = Path.Combine(uploadRoot, "project-intake", intakeId.ToString("N"));
    Directory.CreateDirectory(requestFolder);
    var storedPath = Path.Combine(requestFolder, storedFileName);

    await using (var stream = File.Create(storedPath))
    {
        await file.CopyToAsync(stream);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    await using (var existsCommand = new NpgsqlCommand("""
        SELECT 1
        FROM project_intake_requests
        WHERE project_intake_request_id = @intake_id;
        """, connection, transaction))
    {
        existsCommand.Parameters.AddWithValue("intake_id", intakeId);
        if (await existsCommand.ExecuteScalarAsync() is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "intake_not_found", message = "Project intake request was not found." });
        }
    }

    if (replaceExisting)
    {
        await using var replaceCommand = new NpgsqlCommand("""
            UPDATE project_intake_documents
            SET is_active = FALSE,
                document_status = 'replaced'
            WHERE project_intake_request_id = @intake_id
              AND LOWER(document_type) = LOWER(@document_type)
              AND is_active = TRUE;
            """, connection, transaction);

        replaceCommand.Parameters.AddWithValue("intake_id", intakeId);
        replaceCommand.Parameters.AddWithValue("document_type", documentType);
        await replaceCommand.ExecuteNonQueryAsync();
    }

    Guid documentId;
    await using (var insertCommand = new NpgsqlCommand("""
        INSERT INTO project_intake_documents (
            project_intake_request_id,
            document_type,
            document_category,
            original_file_name,
            stored_file_name,
            storage_path,
            content_type,
            size_bytes,
            uploaded_by_user_id,
            upload_source,
            engineering_visible,
            ai_timesheet_context_enabled,
            extraction_status,
            document_status,
            is_active
        )
        VALUES (
            @intake_id,
            @document_type,
            @document_category,
            @original_file_name,
            @stored_file_name,
            @storage_path,
            @content_type,
            @size_bytes,
            @uploaded_by_user_id,
            'post_intake_upload',
            @engineering_visible,
            @ai_timesheet_context_enabled,
            'not_started',
            'active',
            TRUE
        )
        RETURNING project_intake_document_id;
        """, connection, transaction))
    {
        insertCommand.Parameters.AddWithValue("intake_id", intakeId);
        insertCommand.Parameters.AddWithValue("document_type", documentType);
        insertCommand.Parameters.AddWithValue("document_category", documentType is "sow" or "gsd" ? documentType : "supporting_document");
        insertCommand.Parameters.AddWithValue("original_file_name", safeOriginalFileName);
        insertCommand.Parameters.AddWithValue("stored_file_name", storedFileName);
        insertCommand.Parameters.AddWithValue("storage_path", storedPath);
        insertCommand.Parameters.AddWithValue("content_type", string.IsNullOrWhiteSpace(file.ContentType) ? DBNull.Value : file.ContentType);
        insertCommand.Parameters.AddWithValue("size_bytes", file.Length);
        insertCommand.Parameters.AddWithValue("uploaded_by_user_id", sessionUserId.Value);
        insertCommand.Parameters.AddWithValue("engineering_visible", engineeringVisible);
        insertCommand.Parameters.AddWithValue("ai_timesheet_context_enabled", aiTimesheetContextEnabled);

        documentId = (Guid)(await insertCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to save supporting document."));
    }

    await using (var updateCommand = new NpgsqlCommand("""
        UPDATE project_intake_requests
        SET source_document_received = TRUE,
            post_intake_edit_count = post_intake_edit_count + 1,
            last_post_intake_edit_at = NOW(),
            last_post_intake_edit_by_user_id = @updated_by_user_id,
            last_post_intake_edit_note = @note,
            updated_at = NOW()
        WHERE project_intake_request_id = @intake_id;
        """, connection, transaction))
    {
        updateCommand.Parameters.AddWithValue("intake_id", intakeId);
        updateCommand.Parameters.AddWithValue("updated_by_user_id", sessionUserId.Value);
        updateCommand.Parameters.AddWithValue("note", $"{documentType} document uploaded after initial intake.");
        await updateCommand.ExecuteNonQueryAsync();
    }

    await using (var historyCommand = new NpgsqlCommand("""
        INSERT INTO project_intake_change_history (
            project_intake_request_id,
            changed_by_user_id,
            change_type,
            change_summary,
            new_snapshot
        )
        VALUES (
            @intake_id,
            @changed_by_user_id,
            'post_intake_document_uploaded',
            @change_summary,
            jsonb_build_object(
                'documentId', @document_id,
                'documentType', @document_type,
                'originalFileName', @original_file_name,
                'replaceExisting', @replace_existing
            )
        );
        """, connection, transaction))
    {
        historyCommand.Parameters.AddWithValue("intake_id", intakeId);
        historyCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        historyCommand.Parameters.AddWithValue("change_summary", $"{documentType} document uploaded after initial intake.");
        historyCommand.Parameters.AddWithValue("document_id", documentId);
        historyCommand.Parameters.AddWithValue("document_type", documentType);
        historyCommand.Parameters.AddWithValue("original_file_name", safeOriginalFileName);
        historyCommand.Parameters.AddWithValue("replace_existing", replaceExisting);
        await historyCommand.ExecuteNonQueryAsync();
    }

    await InsertAuditLogAsync(connection, transaction, sessionUserId.Value, "project_intake_post_intake_document_uploaded", "project_intake", intakeId);

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "post_intake_document_uploaded",
        intakeId,
        documentId,
        documentType,
        originalFileName = safeOriginalFileName,
        replaceExisting,
        message = "Supporting document uploaded and audit history was recorded."
    });
});

app.MapGet("/api/project-intake/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var requests = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT request_number, client_name, request_title, intake_status, priority, target_start_date, target_completion_date, estimated_hours
        FROM project_intake_requests
        ORDER BY created_at DESC;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            requests.Add(new
            {
                requestNumber = reader.GetString(0),
                clientName = reader.GetString(1),
                title = reader.GetString(2),
                status = reader.GetString(3),
                priority = reader.GetString(4),
                targetStartDate = reader.IsDBNull(5) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(5),
                targetCompletionDate = reader.IsDBNull(6) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(6),
                estimatedHours = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7)
            });
        }
    }

    var templates = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT template_code, template_name, service_line, default_phase_count, default_task_count
        FROM project_templates
        WHERE is_active = TRUE
        ORDER BY template_name;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            templates.Add(new
            {
                templateCode = reader.GetString(0),
                templateName = reader.GetString(1),
                serviceLine = reader.IsDBNull(2) ? null : reader.GetString(2),
                defaultPhaseCount = reader.GetInt32(3),
                defaultTaskCount = reader.GetInt32(4)
            });
        }
    }

    return Results.Ok(new { count = requests.Count, requests, templates });
});














/* 055D_2A_GSD_XLSX_EXTRACTION_REVIEW_API_START */
app.MapGet("/api/work-register/intake/packages/recent", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055D2A_intake_recent_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var dbConfig = DatabaseConfig.FromEnvironment();
    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    var packages = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            p.work_register_intake_package_id,
            p.intake_status,
            p.requested_work_type,
            COALESCE(p.contract_type, 'Fixed Price'),
            p.customer_id,
            p.source_mode,
            p.customer_hint,
            p.project_name_hint,
            p.extraction_status,
            COALESCE(p.review_status, 'not_started'),
            p.created_at,
            COUNT(d.work_register_intake_document_id) AS document_count
        FROM work_register_intake_packages p
        LEFT JOIN work_register_intake_documents d
          ON d.work_register_intake_package_id = p.work_register_intake_package_id
        GROUP BY
            p.work_register_intake_package_id,
            p.intake_status,
            p.requested_work_type,
            p.contract_type,
            p.customer_id,
            p.source_mode,
            p.customer_hint,
            p.project_name_hint,
            p.extraction_status,
            p.review_status,
            p.created_at
        ORDER BY p.created_at DESC
        LIMIT 50;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        packages.Add(new
        {
            intakePackageId = reader.GetGuid(0),
            intakeStatus = reader.GetString(1),
            requestedWorkType = reader.GetString(2),
            contractType = reader.GetString(3),
            customerId = reader.IsDBNull(4) ? "" : reader.GetGuid(4).ToString(),
            sourceMode = reader.GetString(5),
            customerHint = reader.GetString(6),
            projectNameHint = reader.GetString(7),
            extractionStatus = reader.GetString(8),
            reviewStatus = reader.GetString(9),
            createdAt = reader.GetDateTime(10),
            documentCount = reader.GetInt64(11)
        });
    }

    return Results.Ok(new { status = "intake_packages_loaded", packages });
});

app.MapGet("/api/work-register/intake/packages/{intakePackageId:guid}/review", async (Guid intakePackageId, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055D2A_intake_review_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var dbConfig = DatabaseConfig.FromEnvironment();
    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    object? package = null;

    await using (var packageCommand = new NpgsqlCommand("""
        SELECT
            work_register_intake_package_id,
            intake_status,
            requested_work_type,
            COALESCE(contract_type, 'Fixed Price'),
            customer_id,
            source_mode,
            customer_hint,
            project_name_hint,
            notes,
            extraction_status,
            COALESCE(extracted_json, '{}'::jsonb)::text,
            COALESCE(review_status, 'not_started'),
            COALESCE(reviewed_json, '{}'::jsonb)::text,
            created_at
        FROM work_register_intake_packages
        WHERE work_register_intake_package_id = @intake_package_id
        LIMIT 1;
        """, connection))
    {
        packageCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);

        await using var reader = await packageCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            package = new
            {
                intakePackageId = reader.GetGuid(0),
                intakeStatus = reader.GetString(1),
                requestedWorkType = reader.GetString(2),
                contractType = reader.GetString(3),
                customerId = reader.IsDBNull(4) ? "" : reader.GetGuid(4).ToString(),
                sourceMode = reader.GetString(5),
                customerHint = reader.GetString(6),
                projectNameHint = reader.GetString(7),
                notes = reader.GetString(8),
                extractionStatus = reader.GetString(9),
                extractedJson = reader.GetString(10),
                reviewStatus = reader.GetString(11),
                reviewedJson = reader.GetString(12),
                createdAt = reader.GetDateTime(13)
            };
        }
    }

    if (package is null)
    {
        return Results.NotFound(new { status = "intake_package_not_found", message = "Intake package was not found." });
    }

    var documents = new List<object>();

    await using (var documentCommand = new NpgsqlCommand("""
        SELECT
            work_register_intake_document_id,
            document_type,
            original_file_name,
            content_type,
            file_size_bytes,
            created_at
        FROM work_register_intake_documents
        WHERE work_register_intake_package_id = @intake_package_id
        ORDER BY
            CASE document_type
              WHEN 'GSD' THEN 1
              WHEN 'SOW' THEN 2
              ELSE 3
            END,
            created_at;
        """, connection))
    {
        documentCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);

        await using var reader = await documentCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            documents.Add(new
            {
                documentId = reader.GetGuid(0),
                documentType = reader.GetString(1),
                originalFileName = reader.GetString(2),
                contentType = reader.GetString(3),
                fileSizeBytes = reader.GetInt64(4),
                createdAt = reader.GetDateTime(5)
            });
        }
    }

    return Results.Ok(new { status = "intake_review_loaded", package, documents });
});

app.MapPost("/api/work-register/intake/packages/{intakePackageId:guid}/extract", async (Guid intakePackageId, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055D2A_intake_extract_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var dbConfig = DatabaseConfig.FromEnvironment();
    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    string requestedWorkType = "";
    string contractType = "";
    string customerHint = "";
    string projectNameHint = "";
    string customerIdText = "";
    string sowSignedDate = "";
    string estimatedEndDate = "";

    await using (var packageCommand = new NpgsqlCommand("""
        SELECT requested_work_type,
               COALESCE(contract_type, 'Fixed Price'),
               customer_hint,
               project_name_hint,
               customer_id,
               COALESCE(extracted_json->>'sowSignedDate', ''),
               COALESCE(extracted_json->>'estimatedEndDate', '')
        FROM work_register_intake_packages
        WHERE work_register_intake_package_id = @intake_package_id
        LIMIT 1;
        """, connection))
    {
        packageCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);

        await using var reader = await packageCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new { status = "intake_package_not_found", message = "Intake package was not found." });
        }

        requestedWorkType = reader.GetString(0);
        contractType = reader.GetString(1);
        customerHint = reader.GetString(2);
        projectNameHint = reader.GetString(3);
        customerIdText = reader.IsDBNull(4) ? "" : reader.GetGuid(4).ToString();
        sowSignedDate = reader.GetString(5);
        estimatedEndDate = reader.GetString(6);
    }

    var documents = new List<(Guid DocumentId, string DocumentType, string OriginalFileName, string StoredFilePath, string ContentType, long FileSizeBytes)>();

    await using (var documentCommand = new NpgsqlCommand("""
        SELECT
            work_register_intake_document_id,
            document_type,
            original_file_name,
            stored_file_path,
            content_type,
            file_size_bytes
        FROM work_register_intake_documents
        WHERE work_register_intake_package_id = @intake_package_id
        ORDER BY
            CASE document_type
              WHEN 'GSD' THEN 1
              WHEN 'SOW' THEN 2
              ELSE 3
            END,
            created_at;
        """, connection))
    {
        documentCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);

        await using var reader = await documentCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            documents.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5)));
        }
    }

    static string CleanCell(string? value)
    {
        return (value ?? "").Replace("\r", "\n").Replace("\t", " ").Trim();
    }

    static string NormalizeLabel(string value)
    {
        return new string((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    static decimal? TryDecimal(string value)
    {
        var clean = (value ?? "").Replace("$", "").Replace(",", "").Trim();
        if (decimal.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        var match = System.Text.RegularExpressions.Regex.Match(clean, @"(?<!\d)(?<number>\d{1,8}(?:\.\d{1,2})?)(?!\d)");
        if (match.Success && decimal.TryParse(match.Groups["number"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedMatch))
        {
            return parsedMatch;
        }

        return null;
    }

    static int ColumnLettersToNumber(string letters)
    {
        var result = 0;
        foreach (var character in letters.ToUpperInvariant())
        {
            if (character < 'A' || character > 'Z') continue;
            result = result * 26 + (character - 'A' + 1);
        }
        return result;
    }

    static string CellValue(System.Xml.Linq.XElement cell, System.Collections.Generic.IReadOnlyList<string> sharedStrings)
    {
        var ns = cell.Name.Namespace;
        var type = cell.Attribute("t")?.Value ?? "";
        var value = cell.Element(ns + "v")?.Value ?? "";

        if (type == "s" && int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
        {
            return CleanCell(sharedStrings[sharedIndex]);
        }

        if (type == "inlineStr")
        {
            return CleanCell(string.Concat(cell.Descendants(ns + "t").Select(t => t.Value)));
        }

        return CleanCell(value);
    }

    static Dictionary<string, Dictionary<(int Row, int Col), string>> ReadXlsxSheets(string filePath)
    {
        var sheets = new Dictionary<string, Dictionary<(int Row, int Col), string>>(StringComparer.OrdinalIgnoreCase);

        using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);

        var sharedStrings = new List<string>();
        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is not null)
        {
            using var sharedStream = sharedStringsEntry.Open();
            var sharedDoc = System.Xml.Linq.XDocument.Load(sharedStream);
            var ns = sharedDoc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;
            foreach (var si in sharedDoc.Descendants(ns + "si"))
            {
                sharedStrings.Add(CleanCell(string.Concat(si.Descendants(ns + "t").Select(t => t.Value))));
            }
        }

        var workbookEntry = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidOperationException("workbook.xml not found.");
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidOperationException("workbook relationships not found.");

        System.Xml.Linq.XDocument workbookDoc;
        using (var workbookStream = workbookEntry.Open())
        {
            workbookDoc = System.Xml.Linq.XDocument.Load(workbookStream);
        }

        System.Xml.Linq.XDocument relsDoc;
        using (var relsStream = relsEntry.Open())
        {
            relsDoc = System.Xml.Linq.XDocument.Load(relsStream);
        }

        var relsNs = relsDoc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;
        var relMap = relsDoc.Descendants(relsNs + "Relationship")
            .Where(rel => rel.Attribute("Id") is not null && rel.Attribute("Target") is not null)
            .ToDictionary(rel => rel.Attribute("Id")!.Value, rel => rel.Attribute("Target")!.Value);

        var workbookNs = workbookDoc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;
        var relationshipNs = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");

        foreach (var sheet in workbookDoc.Descendants(workbookNs + "sheet"))
        {
            var sheetName = sheet.Attribute("name")?.Value ?? "";
            var relId = sheet.Attribute(relationshipNs + "id")?.Value ?? "";
            if (string.IsNullOrWhiteSpace(sheetName) || string.IsNullOrWhiteSpace(relId) || !relMap.TryGetValue(relId, out var target))
            {
                continue;
            }

            target = target.Replace("\\", "/").TrimStart('/');
            var sheetPath = target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target;
            var sheetEntry = archive.GetEntry(sheetPath);
            if (sheetEntry is null) continue;

            var cells = new Dictionary<(int Row, int Col), string>();
            using var sheetStream = sheetEntry.Open();
            var sheetDoc = System.Xml.Linq.XDocument.Load(sheetStream);
            var sheetNs = sheetDoc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;

            foreach (var cell in sheetDoc.Descendants(sheetNs + "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? "";
                var letters = new string(reference.TakeWhile(char.IsLetter).ToArray());
                var digits = new string(reference.SkipWhile(char.IsLetter).TakeWhile(char.IsDigit).ToArray());

                if (!int.TryParse(digits, out var row) || string.IsNullOrWhiteSpace(letters)) continue;

                var col = ColumnLettersToNumber(letters);
                var value = CellValue(cell, sharedStrings);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    cells[(row, col)] = value;
                }
            }

            sheets[sheetName] = cells;
        }

        return sheets;
    }

    static string FindLabelValue(Dictionary<(int Row, int Col), string> sheet, params string[] labels)
    {
        var normalizedLabels = labels.Select(NormalizeLabel).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in sheet.OrderBy(kvp => kvp.Key.Row).ThenBy(kvp => kvp.Key.Col))
        {
            if (!normalizedLabels.Contains(NormalizeLabel(cell.Value))) continue;

            for (var col = cell.Key.Col + 1; col <= cell.Key.Col + 5; col++)
            {
                if (sheet.TryGetValue((cell.Key.Row, col), out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return "";
    }

    





    /* 055D_2I_REPAIRED_TOTALS_ROLLUP_SKU_MAPPING_START */
    static string NormalizeContractTypeForIntake(string value)
    {
        var normalized = NormalizeLabel(value);

        if (normalized is "tm" or "tandm" or "timematerial" or "timematerials"
            or "timeandmaterial" or "timeandmaterials" or "timeampmaterial" or "timeampmaterials")
        {
            return "TM";
        }

        if (normalized is "fp" or "fixedprice")
        {
            return "FP";
        }

        if (normalized.Contains("presale")) return "Presales";
        if (normalized.Contains("internal")) return "Internal";

        return string.IsNullOrWhiteSpace(value) ? "FP" : value.Trim();
    }

    static string GetCell(Dictionary<(int Row, int Col), string> sheet, int row, int col)
    {
        return sheet.TryGetValue((row, col), out var value) ? CleanCell(value) : "";
    }

    static decimal NumberOrZero(string value)
    {
        return TryDecimal(value) ?? 0m;
    }

    static decimal FirstNumberInRange(Dictionary<(int Row, int Col), string> sheet, int row, int startCol, int endCol)
    {
        for (var col = startCol; col <= endCol; col++)
        {
            var number = TryDecimal(GetCell(sheet, row, col));
            if (number is not null)
            {
                return number.Value;
            }
        }

        return 0m;
    }

    static bool LooksLikeToyotaHyundaiGsd(Dictionary<(int Row, int Col), string> summary)
    {
        var client = FindLabelValue(summary, "Client Name", "Customer", "Customer Name");
        var project = FindLabelValue(summary, "Project Name", "Work Name");
        var combined = NormalizeLabel(client + " " + project);

        return combined.Contains("toyota")
            || combined.Contains("tmna")
            || combined.Contains("tmma")
            || combined.Contains("lexus")
            || combined.Contains("hyundai")
            || combined.Contains("haea")
            || combined.Contains("hma")
            || combined.Contains("hatci")
            || combined.Contains("kia")
            || combined.Contains("kus");
    }

    static bool IsPmRole(string role)
    {
        var normalized = NormalizeLabel(role);

        return normalized.Contains("projectmgr")
            || normalized.Contains("projectmanager")
            || normalized.Contains("pmgr")
            || normalized.Contains("pcoord")
            || normalized.Contains("projectcoord")
            || normalized.Contains("psched")
            || normalized.Contains("projectsched");
    }

    static bool IsTravelRole(string role)
    {
        var normalized = NormalizeLabel(role);
        return normalized.Contains("travel");
    }

    static string RoleFamily(string role)
    {
        var normalized = NormalizeLabel(role);

        if (normalized.Contains("badevarch") || normalized.Contains("analyst") || normalized.Contains("architect"))
        {
            return "analystdevarch";
        }

        if (normalized.Contains("senior") || normalized.Contains("sme"))
        {
            return "sme";
        }

        if (normalized.Contains("consult"))
        {
            return "consult";
        }

        if (normalized.Contains("assoc"))
        {
            return "assoc";
        }

        if (IsPmRole(role))
        {
            if (normalized.Contains("coord") || normalized.Contains("pcoord")) return "projectcoord";
            if (normalized.Contains("sched") || normalized.Contains("psched")) return "projectsched";
            return "projectmanager";
        }

        if (normalized.Contains("travel"))
        {
            return "travel";
        }

        return normalized;
    }

    static bool SkuMatchesRole(string sku, string description, string role, bool overtime)
    {
        var combined = NormalizeLabel(sku + " " + description);
        var family = RoleFamily(role);

        if (overtime && !combined.Contains("afterhours"))
        {
            return false;
        }

        if (!overtime && combined.Contains("afterhours"))
        {
            return false;
        }

        return family switch
        {
            "analystdevarch" => combined.Contains("analystdevarch") || combined.Contains("badevarch"),
            "sme" => combined.Contains("sme"),
            "consult" => combined.Contains("consult"),
            "assoc" => combined.Contains("assoc"),
            "projectmanager" => combined.Contains("projectmanager") || combined.Contains("projectmgr"),
            "projectcoord" => combined.Contains("projectcoord"),
            "projectsched" => combined.Contains("projectsched") || combined.Contains("projectsched"),
            "travel" => combined.Contains("travel"),
            _ => combined.Contains(family)
        };
    }

    static (string Sku, string Description, decimal CatalogRate, decimal CatalogHours) FindSellSkuMatch(
        Dictionary<string, Dictionary<(int Row, int Col), string>> workbook,
        string role,
        bool overtime,
        string contractType)
    {
        if (!workbook.TryGetValue("SELL SKUs", out var sell))
        {
            return ("", "", 0m, 0m);
        }

        var normalizedContract = NormalizeContractTypeForIntake(contractType);
        var activeSection = "";

        for (var row = 1; row <= 450; row++)
        {
            var rowText = string.Join(" ", Enumerable.Range(1, 14).Select(col => GetCell(sell, row, col))).Trim();
            var rowNormalized = NormalizeLabel(rowText);

            if (rowNormalized.Contains("timematerial") || rowNormalized.Contains("timeandmaterial") || rowNormalized.Contains("timeampmaterial") || rowNormalized.Contains("tandm"))
            {
                activeSection = "TM";
            }

            if (rowNormalized.Contains("fixedprice"))
            {
                activeSection = "FP";
            }

            var skuCol = 0;
            var sku = "";

            for (var col = 1; col <= 14; col++)
            {
                var candidate = GetCell(sell, row, col);
                if (candidate.StartsWith("ON-", StringComparison.OrdinalIgnoreCase))
                {
                    skuCol = col;
                    sku = candidate;
                    break;
                }
            }

            if (skuCol == 0)
            {
                continue;
            }

            if ((normalizedContract == "TM" && activeSection == "FP") || (normalizedContract == "FP" && activeSection == "TM"))
            {
                continue;
            }

            var description = "";
            for (var col = skuCol + 1; col <= skuCol + 5; col++)
            {
                description = GetCell(sell, row, col);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    break;
                }
            }

            if (!SkuMatchesRole(sku, description, role, overtime))
            {
                continue;
            }

            var leftNumbers = new List<(int Col, decimal Value)>();
            for (var col = skuCol - 1; col >= 1; col--)
            {
                var number = TryDecimal(GetCell(sell, row, col));
                if (number is not null)
                {
                    leftNumbers.Add((col, number.Value));
                }
            }

            var catalogHours = leftNumbers.Count >= 1 ? leftNumbers[0].Value : 0m;
            var catalogRate = leftNumbers.Count >= 2 ? leftNumbers[1].Value : 0m;

            return (sku, string.IsNullOrWhiteSpace(description) ? sku : description, catalogRate, catalogHours);
        }

        return ("", "", 0m, 0m);
    }

    static (string Sku, string Description) FindExpenseSkuMatch(
        Dictionary<string, Dictionary<(int Row, int Col), string>> workbook,
        string expenseType,
        string contractType)
    {
        if (!workbook.TryGetValue("SELL SKUs", out var sell))
        {
            return ("", "");
        }

        var normalizedContract = NormalizeContractTypeForIntake(contractType);
        var activeSection = "";
        var wanted = NormalizeLabel(expenseType);

        for (var row = 1; row <= 450; row++)
        {
            var rowText = string.Join(" ", Enumerable.Range(1, 14).Select(col => GetCell(sell, row, col))).Trim();
            var rowNormalized = NormalizeLabel(rowText);

            if (rowNormalized.Contains("timematerial") || rowNormalized.Contains("timeandmaterial") || rowNormalized.Contains("timeampmaterial") || rowNormalized.Contains("tandm"))
            {
                activeSection = "TM";
            }

            if (rowNormalized.Contains("fixedprice"))
            {
                activeSection = "FP";
            }

            var skuCol = 0;
            var sku = "";

            for (var col = 1; col <= 14; col++)
            {
                var candidate = GetCell(sell, row, col);
                if (candidate.StartsWith("ON-", StringComparison.OrdinalIgnoreCase))
                {
                    skuCol = col;
                    sku = candidate;
                    break;
                }
            }

            if (skuCol == 0)
            {
                continue;
            }

            if ((normalizedContract == "TM" && activeSection == "FP") || (normalizedContract == "FP" && activeSection == "TM"))
            {
                continue;
            }

            var description = "";
            for (var col = skuCol + 1; col <= skuCol + 5; col++)
            {
                description = GetCell(sell, row, col);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    break;
                }
            }

            var combined = NormalizeLabel(sku + " " + description);

            if (wanted.Contains("travel") && (combined.Contains("perdiem") || combined.Contains("travel")))
            {
                return (sku, string.IsNullOrWhiteSpace(description) ? sku : description);
            }

            if (wanted.Contains("material") && combined.Contains("material"))
            {
                return (sku, string.IsNullOrWhiteSpace(description) ? sku : description);
            }

            if (wanted.Contains("shipping") && combined.Contains("shipping"))
            {
                return (sku, string.IsNullOrWhiteSpace(description) ? sku : description);
            }
        }

        return ("", "");
    }

    static (int HeaderRow, int RoleCol, int RegHoursCol, int OtHoursCol, int RegTotalCol, int OtTotalCol, int OverallTotalCol) FindResourceTotalsHeader(
        Dictionary<(int Row, int Col), string> sheet)
    {
        for (var row = 1; row <= 80; row++)
        {
            var roleCol = 0;
            var regHoursCol = 0;
            var otHoursCol = 0;
            var regTotalCol = 0;
            var otTotalCol = 0;
            var overallTotalCol = 0;

            for (var col = 1; col <= 14; col++)
            {
                var normalized = NormalizeLabel(GetCell(sheet, row, col));

                if (normalized.Contains("resourcerole")) roleCol = col;
                if (normalized.Contains("regularhours")) regHoursCol = col;
                if (normalized.Contains("overtimehours")) otHoursCol = col;
                if (normalized.Contains("regulartotals")) regTotalCol = col;
                if (normalized.Contains("overtimetotals")) otTotalCol = col;
                if (normalized.Contains("overalltotals")) overallTotalCol = col;
            }

            if (roleCol > 0 && regHoursCol > 0 && otHoursCol > 0 && regTotalCol > 0)
            {
                return (row, roleCol, regHoursCol, otHoursCol, regTotalCol, otTotalCol, overallTotalCol);
            }
        }

        return (0, 0, 0, 0, 0, 0, 0);
    }

    static List<object> ExtractTotalsSheetPricingRows(
        Dictionary<string, Dictionary<(int Row, int Col), string>> workbook,
        string contractType)
    {
        var rows = new List<object>();

        if (!workbook.TryGetValue("Totals Sheet", out var sheet))
        {
            return rows;
        }

        var header = FindResourceTotalsHeader(sheet);
        if (header.HeaderRow == 0)
        {
            return rows;
        }

        for (var row = header.HeaderRow + 1; row <= header.HeaderRow + 40; row++)
        {
            var rowText = string.Join(" ", Enumerable.Range(1, 14).Select(col => GetCell(sheet, row, col))).Trim();
            var normalized = NormalizeLabel(rowText);

            if (normalized.Contains("phasebreakouttotals") || normalized.Contains("milestonebreakouttotals") || normalized.Contains("invoicingbreakouttotals"))
            {
                break;
            }

            var role = GetCell(sheet, row, header.RoleCol);

            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            var normalizedRole = NormalizeLabel(role);
            if (normalizedRole.Contains("total") || normalizedRole.Contains("resourcerole"))
            {
                continue;
            }

            var regularHours = NumberOrZero(GetCell(sheet, row, header.RegHoursCol));
            var overtimeHours = NumberOrZero(GetCell(sheet, row, header.OtHoursCol));

            var regularTotalEnd = header.OtTotalCol > 0 ? header.OtTotalCol - 1 : header.RegTotalCol + 2;
            var overtimeTotalEnd = header.OverallTotalCol > 0 ? header.OverallTotalCol - 1 : header.OtTotalCol + 2;

            var regularTotal = FirstNumberInRange(sheet, row, header.RegTotalCol, Math.Max(header.RegTotalCol, regularTotalEnd));
            var overtimeTotal = header.OtTotalCol > 0
                ? FirstNumberInRange(sheet, row, header.OtTotalCol, Math.Max(header.OtTotalCol, overtimeTotalEnd))
                : 0m;

            if (regularHours > 0m && regularTotal > 0m)
            {
                var skuMatch = FindSellSkuMatch(workbook, role, overtime: false, contractType);
                var derivedRate = regularTotal / regularHours;

                rows.Add(new
                {
                    include = true,
                    source = "Totals Sheet Resource Totals",
                    pricingUse = "Authoritative GSD pricing rollup",
                    contractType = NormalizeContractTypeForIntake(contractType),
                    row,
                    sku = string.IsNullOrWhiteSpace(skuMatch.Sku) ? $"GSD-{RoleFamily(role).ToUpperInvariant()}" : skuMatch.Sku,
                    description = $"{role} - Regular / Standard",
                    rate = Math.Round(derivedRate, 2),
                    hours = regularHours,
                    extendedAmount = regularTotal,
                    billable = true
                });
            }

            if (overtimeHours > 0m && overtimeTotal > 0m)
            {
                var skuMatch = FindSellSkuMatch(workbook, role, overtime: true, contractType);
                var derivedRate = overtimeTotal / overtimeHours;

                rows.Add(new
                {
                    include = true,
                    source = "Totals Sheet Resource Totals",
                    pricingUse = "Authoritative GSD pricing rollup",
                    contractType = NormalizeContractTypeForIntake(contractType),
                    row,
                    sku = string.IsNullOrWhiteSpace(skuMatch.Sku) ? $"GSD-{RoleFamily(role).ToUpperInvariant()}-OT" : skuMatch.Sku,
                    description = $"{role} - Overtime / Afterhours",
                    rate = Math.Round(derivedRate, 2),
                    hours = overtimeHours,
                    extendedAmount = overtimeTotal,
                    billable = true
                });
            }
        }

        for (var row = 1; row <= 140; row++)
        {
            var label = "";
            var labelCol = 0;

            for (var col = 1; col <= 6; col++)
            {
                var candidate = GetCell(sheet, row, col);
                var normalizedCandidate = NormalizeLabel(candidate);

                if (normalizedCandidate is "travelexpense" or "materialexpense" or "shippingexpense")
                {
                    label = candidate;
                    labelCol = col;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var amount = FirstNumberInRange(sheet, row, labelCol + 1, labelCol + 5);
            if (amount <= 0m)
            {
                continue;
            }

            var expenseSku = FindExpenseSkuMatch(workbook, label, contractType);
            var sku = !string.IsNullOrWhiteSpace(expenseSku.Sku)
                ? expenseSku.Sku
                : $"GSD-{NormalizeLabel(label).ToUpperInvariant()}";

            rows.Add(new
            {
                include = true,
                source = "Totals Sheet Overall Totals",
                pricingUse = "Authoritative GSD expense rollup",
                contractType = NormalizeContractTypeForIntake(contractType),
                row,
                sku,
                description = label,
                rate = amount,
                hours = 1m,
                extendedAmount = amount,
                billable = true
            });
        }

        return rows;
    }

    static List<object> ExtractSellSkuRateCatalogRows(
        Dictionary<string, Dictionary<(int Row, int Col), string>> workbook,
        string contractType)
    {
        var rows = new List<object>();

        if (!workbook.TryGetValue("SELL SKUs", out var sell))
        {
            return rows;
        }

        var normalizedContract = NormalizeContractTypeForIntake(contractType);
        var activeSection = "";

        for (var row = 1; row <= 450; row++)
        {
            var rowText = string.Join(" ", Enumerable.Range(1, 14).Select(col => GetCell(sell, row, col))).Trim();
            var rowNormalized = NormalizeLabel(rowText);

            if (rowNormalized.Contains("timematerial") || rowNormalized.Contains("timeandmaterial") || rowNormalized.Contains("timeampmaterial") || rowNormalized.Contains("tandm"))
            {
                activeSection = "TM";
            }

            if (rowNormalized.Contains("fixedprice"))
            {
                activeSection = "FP";
            }

            var skuCol = 0;
            var sku = "";

            for (var col = 1; col <= 14; col++)
            {
                var candidate = GetCell(sell, row, col);
                if (candidate.StartsWith("ON-", StringComparison.OrdinalIgnoreCase))
                {
                    skuCol = col;
                    sku = candidate;
                    break;
                }
            }

            if (skuCol == 0)
            {
                continue;
            }

            if ((normalizedContract == "TM" && activeSection == "FP") || (normalizedContract == "FP" && activeSection == "TM"))
            {
                continue;
            }

            var leftNumbers = new List<(int Col, decimal Value)>();
            for (var col = skuCol - 1; col >= 1; col--)
            {
                var number = TryDecimal(GetCell(sell, row, col));
                if (number is not null)
                {
                    leftNumbers.Add((col, number.Value));
                }
            }

            var hours = leftNumbers.Count >= 1 ? leftNumbers[0].Value : 0m;
            var rate = leftNumbers.Count >= 2 ? leftNumbers[1].Value : 0m;

            if (hours > 0m)
            {
                continue;
            }

            var description = "";
            for (var col = skuCol + 1; col <= skuCol + 5; col++)
            {
                description = GetCell(sell, row, col);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    break;
                }
            }

            if (rate <= 0m)
            {
                continue;
            }

            rows.Add(new
            {
                include = false,
                source = "SELL SKUs",
                pricingUse = "Available rate catalog",
                contractType = normalizedContract,
                priceSection = activeSection,
                row,
                sku,
                description = string.IsNullOrWhiteSpace(description) ? sku : description,
                rate,
                hours = 0m,
                extendedAmount = 0m,
                billable = true
            });
        }

        return rows.Take(25).ToList();
    }

    
    /* 055D_4D_STANDARD_SELL_SKU_PRICING_START */
    static List<object> ExtractStandardSellSkuPricingRows(
        Dictionary<string, Dictionary<(int Row, int Col), string>> workbook,
        string contractType)
    {
        var rows = new List<object>();

        if (!workbook.TryGetValue("SELL SKUs", out var sell))
        {
            return rows;
        }

        var normalizedContract = NormalizeContractTypeForIntake(contractType);
        var activeSection = "";

        for (var row = 1; row <= 450; row++)
        {
            var rowText = string.Join(" ", Enumerable.Range(1, 14).Select(col => GetCell(sell, row, col))).Trim();
            var rowNormalized = NormalizeLabel(rowText);

            if (rowNormalized.Contains("timematerial") || rowNormalized.Contains("timeandmaterial") || rowNormalized.Contains("timeampmaterial") || rowNormalized.Contains("tandm"))
            {
                activeSection = "TM";
            }

            if (rowNormalized.Contains("fixedprice"))
            {
                activeSection = "FP";
            }

            var skuCol = 0;
            var sku = "";

            for (var col = 1; col <= 14; col++)
            {
                var candidate = GetCell(sell, row, col);
                if (candidate.StartsWith("ON-", StringComparison.OrdinalIgnoreCase))
                {
                    skuCol = col;
                    sku = candidate;
                    break;
                }
            }

            if (skuCol == 0)
            {
                continue;
            }

            if ((normalizedContract == "TM" && activeSection == "FP") || (normalizedContract == "FP" && activeSection == "TM"))
            {
                continue;
            }

            var leftNumbers = new List<(int Col, decimal Value)>();

            for (var col = skuCol - 1; col >= 1; col--)
            {
                var number = TryDecimal(GetCell(sell, row, col));
                if (number is not null)
                {
                    leftNumbers.Add((col, number.Value));
                }
            }

            var hours = leftNumbers.Count >= 1 ? leftNumbers[0].Value : 0m;
            var rate = leftNumbers.Count >= 2 ? leftNumbers[1].Value : 0m;

            if (hours <= 0m || rate <= 0m)
            {
                continue;
            }

            var description = "";

            for (var col = skuCol + 1; col <= skuCol + 5; col++)
            {
                description = GetCell(sell, row, col);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    break;
                }
            }

            rows.Add(new
            {
                include = true,
                source = "SELL SKUs",
                pricingUse = "Standard GSD priced SKU",
                contractType = normalizedContract,
                priceSection = activeSection,
                row,
                sku,
                description = string.IsNullOrWhiteSpace(description) ? sku : description,
                rate,
                hours,
                extendedAmount = rate * hours,
                billable = true
            });
        }

        return rows;
    }

    static List<object> ExtractRates(
        Dictionary<string, Dictionary<(int Row, int Col), string>> workbook,
        string contractType,
        Dictionary<(int Row, int Col), string> summary)
    {
        if (LooksLikeToyotaHyundaiGsd(summary))
        {
            var pricingRows = ExtractTotalsSheetPricingRows(workbook, contractType);

            if (pricingRows.Count > 0)
            {
                var catalogRows = ExtractSellSkuRateCatalogRows(workbook, contractType);
                return pricingRows.Concat(catalogRows).ToList();
            }
        }

        var standardPricedRows = ExtractStandardSellSkuPricingRows(workbook, contractType);

        if (standardPricedRows.Count > 0)
        {
            var catalogRows = ExtractSellSkuRateCatalogRows(workbook, contractType);
            return standardPricedRows.Concat(catalogRows).ToList();
        }

        return ExtractSellSkuRateCatalogRows(workbook, contractType);
    }
    /* 055D_4D_STANDARD_SELL_SKU_PRICING_END */

    static List<object> ExtractTasksFromTotalsSheetResourceRows(
        Dictionary<string, Dictionary<(int Row, int Col), string>> workbook)
    {
        var tasks = new List<object>();

        if (!workbook.TryGetValue("Totals Sheet", out var sheet))
        {
            return tasks;
        }

        var header = FindResourceTotalsHeader(sheet);
        if (header.HeaderRow == 0)
        {
            return tasks;
        }

        for (var row = header.HeaderRow + 1; row <= header.HeaderRow + 40; row++)
        {
            var rowText = string.Join(" ", Enumerable.Range(1, 14).Select(col => GetCell(sheet, row, col))).Trim();
            var normalized = NormalizeLabel(rowText);

            if (normalized.Contains("phasebreakouttotals") || normalized.Contains("milestonebreakouttotals") || normalized.Contains("invoicingbreakouttotals"))
            {
                break;
            }

            var role = GetCell(sheet, row, header.RoleCol);

            if (string.IsNullOrWhiteSpace(role))
            {
                continue;
            }

            var normalizedRole = NormalizeLabel(role);
            if (normalizedRole.Contains("total") || normalizedRole.Contains("resourcerole") || IsTravelRole(role))
            {
                continue;
            }

            var regularHours = NumberOrZero(GetCell(sheet, row, header.RegHoursCol));
            var overtimeHours = NumberOrZero(GetCell(sheet, row, header.OtHoursCol));

            var regularTotalEnd = header.OtTotalCol > 0 ? header.OtTotalCol - 1 : header.RegTotalCol + 2;
            var overtimeTotalEnd = header.OverallTotalCol > 0 ? header.OverallTotalCol - 1 : header.OtTotalCol + 2;

            var regularTotal = FirstNumberInRange(sheet, row, header.RegTotalCol, Math.Max(header.RegTotalCol, regularTotalEnd));
            var overtimeTotal = header.OtTotalCol > 0
                ? FirstNumberInRange(sheet, row, header.OtTotalCol, Math.Max(header.OtTotalCol, overtimeTotalEnd))
                : 0m;

            var taskFamily = IsPmRole(role) ? "Project Management" : "Engineering";

            if (regularHours > 0m && regularTotal > 0m)
            {
                tasks.Add(new
                {
                    include = true,
                    source = "Totals Sheet Resource Totals",
                    phase = taskFamily,
                    taskName = $"{taskFamily} - {role}",
                    engineeringRole = $"{role} - Regular / Standard",
                    regularHours,
                    overtimeHours = 0m,
                    reserveHours = 0m,
                    pmHours = IsPmRole(role) ? regularHours : 0m,
                    pmReserveHours = 0m,
                    travelHours = 0m,
                    totalHours = regularHours,
                    laborListPrice = regularTotal,
                    billable = true,
                    utilizationEligible = true,
                    engineers = new List<object>()
                });
            }

            if (overtimeHours > 0m && overtimeTotal > 0m)
            {
                tasks.Add(new
                {
                    include = true,
                    source = "Totals Sheet Resource Totals",
                    phase = taskFamily,
                    taskName = $"{taskFamily} - {role} Afterhours",
                    engineeringRole = $"{role} - Overtime / Afterhours",
                    regularHours = 0m,
                    overtimeHours,
                    reserveHours = 0m,
                    pmHours = 0m,
                    pmReserveHours = 0m,
                    travelHours = 0m,
                    totalHours = overtimeHours,
                    laborListPrice = overtimeTotal,
                    billable = true,
                    utilizationEligible = true,
                    engineers = new List<object>()
                });
            }
        }

        return tasks;
    }

    
    /* 055D_4C_STANDARD_GSD_PHASE_TASK_REPAIR_START */
    static decimal FindNumberNearLabel(Dictionary<(int Row, int Col), string> sheet, int maxRows, params string[] labels)
    {
        var normalizedLabels = labels.Select(NormalizeLabel).ToList();

        for (var row = 1; row <= maxRows; row++)
        {
            for (var col = 1; col <= 14; col++)
            {
                var label = NormalizeLabel(GetCell(sheet, row, col));

                if (!normalizedLabels.Contains(label))
                {
                    continue;
                }

                for (var offset = 1; offset <= 6; offset++)
                {
                    var number = TryDecimal(GetCell(sheet, row, col + offset));
                    if (number is not null)
                    {
                        return number.Value;
                    }
                }
            }
        }

        return 0m;
    }

    static List<object> ExtractStandardGsdPhaseTasksFromTotalsSheet(Dictionary<string, Dictionary<(int Row, int Col), string>> workbook)
    {
        var tasks = new List<object>();

        if (!workbook.TryGetValue("Totals Sheet", out var totals))
        {
            return tasks;
        }

        var phaseHeaderRow = 0;
        var phaseCol = 0;
        var hoursCol = 0;
        var otCol = 0;
        var laborCol = 0;

        for (var row = 1; row <= 120; row++)
        {
            for (var col = 1; col <= 12; col++)
            {
                var label = NormalizeLabel(GetCell(totals, row, col));

                if (label != "phasebreakouttotals")
                {
                    continue;
                }

                phaseHeaderRow = row;
                phaseCol = col;

                for (var scanCol = col + 1; scanCol <= col + 6; scanCol++)
                {
                    var header = NormalizeLabel(GetCell(totals, row, scanCol));

                    if (header == "hours")
                    {
                        hoursCol = scanCol;
                    }
                    else if (header == "othours")
                    {
                        otCol = scanCol;
                    }
                    else if (header.Contains("laborandtravel"))
                    {
                        laborCol = scanCol;
                    }
                }

                break;
            }

            if (phaseHeaderRow > 0)
            {
                break;
            }
        }

        if (phaseHeaderRow == 0 || phaseCol == 0 || hoursCol == 0)
        {
            return tasks;
        }

        var validPhases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PLAN",
            "DESIGN",
            "IMPLEMENT",
            "VALIDATE",
            "RELEASE"
        };

        for (var row = phaseHeaderRow + 1; row <= phaseHeaderRow + 15; row++)
        {
            var phase = GetCell(totals, row, phaseCol).Trim();
            var normalizedPhase = NormalizeLabel(phase);

            if (normalizedPhase.Contains("totalprojectlabor"))
            {
                break;
            }

            if (!validPhases.Contains(phase))
            {
                continue;
            }

            var hours = NumberOrZero(GetCell(totals, row, hoursCol));
            var otHours = otCol > 0 ? NumberOrZero(GetCell(totals, row, otCol)) : 0m;
            var labor = laborCol > 0 ? FirstNumberInRange(totals, row, laborCol, laborCol + 2) : 0m;

            if (hours == 0m && otHours == 0m && labor == 0m)
            {
                continue;
            }

            tasks.Add(new
            {
                include = true,
                source = "Totals Sheet - Phase Breakout Totals",
                phase,
                taskName = phase,
                engineeringRole = "Standard GSD engineering phase total",
                regularHours = hours,
                overtimeHours = otHours,
                reserveHours = 0m,
                pmHours = 0m,
                pmReserveHours = 0m,
                travelHours = 0m,
                totalHours = hours + otHours,
                laborListPrice = labor,
                billable = true,
                utilizationEligible = true,
                engineers = new List<object>()
            });
        }

        if (workbook.TryGetValue("Summary", out var summary))
        {
            var pmHours = FindNumberNearLabel(summary, 50, "Total Project Oversight Hours", "Proj Oversight Hours");
            var projectOversightAmount = FindNumberNearLabel(totals, 80, "PROJECT OVERSIGHT");

            if (pmHours > 0m || projectOversightAmount > 0m)
            {
                tasks.Add(new
                {
                    include = true,
                    source = "Summary / Totals Sheet - Project Oversight",
                    phase = "PROJECT OVERSIGHT",
                    taskName = "Project Oversight",
                    engineeringRole = "PM / Project Oversight",
                    regularHours = 0m,
                    overtimeHours = 0m,
                    reserveHours = 0m,
                    pmHours,
                    pmReserveHours = 0m,
                    travelHours = 0m,
                    totalHours = pmHours,
                    laborListPrice = projectOversightAmount,
                    billable = true,
                    utilizationEligible = true,
                    engineers = new List<object>()
                });
            }
        }

        return tasks;
    }


    static List<object> ExtractConsolidatedTasks(Dictionary<string, Dictionary<(int Row, int Col), string>> workbook, Dictionary<(int Row, int Col), string> summary)
    {
        if (LooksLikeToyotaHyundaiGsd(summary))
        {
            var toyotaHyundaiTasks = ExtractTasksFromTotalsSheetResourceRows(workbook);

            if (toyotaHyundaiTasks.Count > 0)
            {
                return toyotaHyundaiTasks;
            }
        }

        var standardTasks = ExtractStandardGsdPhaseTasksFromTotalsSheet(workbook);

        if (standardTasks.Count > 0)
        {
            return standardTasks;
        }

        var fallbackTasks = ExtractTasksFromTotalsSheetResourceRows(workbook);

        if (fallbackTasks.Count > 0)
        {
            return fallbackTasks;
        }

        return new List<object>();
    }
    /* 055D_4C_STANDARD_GSD_PHASE_TASK_REPAIR_END */

    static List<object> ExtractPhaseTotals(Dictionary<string, Dictionary<(int Row, int Col), string>> workbook)
    {
        var totals = new List<object>();

        if (!workbook.TryGetValue("Totals Sheet", out var sheet))
        {
            return totals;
        }

        for (var row = 1; row <= 180; row++)
        {
            var rowText = string.Join(" ", Enumerable.Range(1, 10).Select(col => GetCell(sheet, row, col))).Trim();
            var normalized = NormalizeLabel(rowText);

            if (normalized.Contains("totalprojectlabor")
                || normalized.Contains("overalltotals")
                || normalized.Contains("travelexpense")
                || normalized.Contains("materialexpense")
                || normalized.Contains("shippingexpense")
                || normalized.Contains("totalproject"))
            {
                totals.Add(new
                {
                    source = "Totals Sheet",
                    row,
                    label = rowText,
                    amount = FirstNumberInRange(sheet, row, 1, 10)
                });
            }
        }

        return totals;
    }
    /* 055D_2I_REPAIRED_TOTALS_ROLLUP_SKU_MAPPING_END */






    var parserNotes = new List<string>();
    Dictionary<string, object>? extractedData = null;

    foreach (var document in documents)
    {
        if (!System.IO.File.Exists(document.StoredFilePath))
        {
            parserNotes.Add($"{document.DocumentType} file not found on disk: {document.OriginalFileName}");
            continue;
        }

        var extension = System.IO.Path.GetExtension(document.StoredFilePath).ToLowerInvariant();
        if (extension != ".xlsx" && extension != ".xlsm")
        {
            parserNotes.Add($"{document.DocumentType} file '{document.OriginalFileName}' is not an XLSX GSD. It remains attached for review.");
            continue;
        }

        try
        {
            var workbook = ReadXlsxSheets(document.StoredFilePath);
            workbook.TryGetValue("Summary", out var summary);
            summary ??= new Dictionary<(int Row, int Col), string>();

            var rawContractType = FindLabelValue(summary, "Contract Type");
            var normalizedContractType = NormalizeContractTypeForIntake(string.IsNullOrWhiteSpace(rawContractType) ? contractType : rawContractType);

            extractedData = new Dictionary<string, object>
            {
                ["extractionStatus"] = "extracted_needs_review",
                ["parserVersion"] = "055D.2A_xlsx_gsd_parser",
                ["requestedWorkType"] = requestedWorkType,
                ["contractType"] = normalizedContractType,
                ["customerId"] = customerIdText,
                ["customerName"] = string.IsNullOrWhiteSpace(FindLabelValue(summary, "Client Name", "Customer", "Customer Name")) ? customerHint : FindLabelValue(summary, "Client Name", "Customer", "Customer Name"),
                ["projectName"] = string.IsNullOrWhiteSpace(FindLabelValue(summary, "Project Name", "Work Name")) ? projectNameHint : FindLabelValue(summary, "Project Name", "Work Name"),
                ["sowSignedDate"] = sowSignedDate,
                ["estimatedEndDate"] = estimatedEndDate,
                ["accountExecutiveName"] = FindLabelValue(summary, "AE", "Account Executive"),
                ["solutionArchitectName"] = FindLabelValue(summary, "SA", "Solution Architect"),
                ["insideSalesName"] = FindLabelValue(summary, "SAA", "Inside Sales"),
                ["pmHours"] = FindLabelValue(summary, "Total Project Oversight Hours", "Proj Oversight Hours"),
                ["engineeringHours"] = FindLabelValue(summary, "Total Engineering Hours"),
                ["totalProjectHours"] = FindLabelValue(summary, "Total Project Hours"),
                ["travelHours"] = FindLabelValue(summary, "Travel Hours"),
                ["projectListPrice"] = FindLabelValue(summary, "Project List Price"),
                ["workLocation"] = FindLabelValue(summary, "Work Location"),
                ["rates"] = ExtractRates(workbook, normalizedContractType, summary),
                ["tasks"] = ExtractConsolidatedTasks(workbook, summary),
                ["phaseTotals"] = ExtractPhaseTotals(workbook),
                ["documents"] = documents.Select(d => new { d.DocumentId, d.DocumentType, d.OriginalFileName, d.ContentType, d.FileSizeBytes }).ToList(),
                ["parserNotes"] = parserNotes.Concat(new[] { "Extracted from XLSX GSD workbook. Review customer mapping, people mapping, rates, tasks, and hours before committing to Work Register." }).ToList()
            };

            break;
        }
        catch (Exception ex)
        {
            parserNotes.Add($"{document.DocumentType} XLSX parser failed for {document.OriginalFileName}: {ex.Message}");
        }
    }

    extractedData ??= new Dictionary<string, object>
    {
        ["extractionStatus"] = "needs_manual_review",
        ["parserVersion"] = "055D.2A_xlsx_gsd_parser",
        ["requestedWorkType"] = requestedWorkType,
        ["contractType"] = NormalizeContractTypeForIntake(contractType),
        ["customerId"] = customerIdText,
        ["customerName"] = customerHint,
        ["projectName"] = projectNameHint,
        ["sowSignedDate"] = sowSignedDate,
        ["estimatedEndDate"] = estimatedEndDate,
        ["accountExecutiveName"] = "",
        ["solutionArchitectName"] = "",
        ["insideSalesName"] = "",
        ["pmHours"] = "",
        ["engineeringHours"] = "",
        ["totalProjectHours"] = "",
        ["travelHours"] = "",
        ["projectListPrice"] = "",
        ["workLocation"] = "",
        ["rates"] = new List<object>(),
        ["tasks"] = new List<object>(),
        ["phaseTotals"] = new List<object>(),
        ["documents"] = documents.Select(d => new { d.DocumentId, d.DocumentType, d.OriginalFileName, d.ContentType, d.FileSizeBytes }).ToList(),
        ["parserNotes"] = parserNotes.Count > 0 ? parserNotes : new List<string> { "No XLSX GSD was found. Enter/review mapping manually." }
    };

    var extractionStatus = Convert.ToString(extractedData["extractionStatus"]) ?? "needs_manual_review";
    var extractedJson = JsonSerializer.Serialize(extractedData);

    await using var transaction = await connection.BeginTransactionAsync();

    await using (var updateCommand = new NpgsqlCommand("""
        UPDATE work_register_intake_packages
        SET extraction_status = @extraction_status,
            extracted_json = CAST(@extracted_json AS jsonb),
            review_status = 'needs_review',
            reviewed_json = '{}'::jsonb,
            reviewed_by_user_id = NULL,
            reviewed_at = NULL,
            updated_at = NOW()
        WHERE work_register_intake_package_id = @intake_package_id;
        /* 055D_2C_RESET_REVIEW_ON_EXTRACT */
        """, connection, transaction))
    {
        updateCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        updateCommand.Parameters.AddWithValue("extraction_status", extractionStatus);
        updateCommand.Parameters.AddWithValue("extracted_json", extractedJson);
        await updateCommand.ExecuteNonQueryAsync();
    }

    await using (var historyCommand = new NpgsqlCommand("""
        INSERT INTO work_register_intake_history (
            work_register_intake_history_id,
            work_register_intake_package_id,
            action,
            summary,
            changed_by_user_id,
            payload_json
        )
        VALUES (
            @history_id,
            @intake_package_id,
            'gsd_xlsx_extraction_run',
            @summary,
            @changed_by_user_id,
            CAST(@payload_json AS jsonb)
        );
        """, connection, transaction))
    {
        historyCommand.Parameters.AddWithValue("history_id", Guid.NewGuid());
        historyCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        historyCommand.Parameters.AddWithValue("summary", $"XLSX extraction completed with status {extractionStatus}.");
        historyCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        historyCommand.Parameters.AddWithValue("payload_json", extractedJson);
        await historyCommand.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "intake_extraction_completed",
        intakePackageId,
        extractionStatus,
        extractedData,
        message = extractionStatus == "extracted_needs_review"
            ? "XLSX GSD extraction completed. Review and correct the mapping before committing to Work Register."
            : "Extraction needs manual review. Enter mapping manually before committing to Work Register."
    });
});

app.MapPost("/api/work-register/intake/packages/{intakePackageId:guid}/review/save", async (Guid intakePackageId, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055D2A_intake_review_save_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body);
    var root = document.RootElement;
    var reviewedDataJson = root.TryGetProperty("reviewedData", out var reviewedData) ? reviewedData.GetRawText() : root.GetRawText();

    var dbConfig = DatabaseConfig.FromEnvironment();
    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    string? savedReviewedDataJson = null;
    await using (var updateCommand = new NpgsqlCommand("""
        UPDATE work_register_intake_packages
        SET review_status = 'reviewed',
            reviewed_json = CASE
                WHEN source_mode = 'sell_import' THEN
                    CAST(@reviewed_json AS jsonb)
                    || jsonb_build_object(
                        'sourceMode', 'sell_import',
                        'sourceSystem', 'SELL',
                        'sourceRecordId', extracted_json->'sourceRecordId',
                        'sourceFieldsLocked', jsonb_build_array('projectName', 'rates'),
                        'projectName', extracted_json->'projectName',
                        'projectListPrice', extracted_json->'projectListPrice',
                        'rates', extracted_json->'rates',
                        'sellQuoteNumber', extracted_json->'sellQuoteNumber'
                    )
                ELSE CAST(@reviewed_json AS jsonb)
            END,
            reviewed_by_user_id = @reviewed_by_user_id,
            reviewed_at = NOW(),
            updated_at = NOW()
        WHERE work_register_intake_package_id = @intake_package_id
        RETURNING reviewed_json::text;
        """, connection, transaction))
    {
        updateCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        updateCommand.Parameters.AddWithValue("reviewed_json", reviewedDataJson);
        updateCommand.Parameters.AddWithValue("reviewed_by_user_id", sessionUserId.Value);
        savedReviewedDataJson = Convert.ToString(await updateCommand.ExecuteScalarAsync());
    }

    if (string.IsNullOrWhiteSpace(savedReviewedDataJson))
    {
        await transaction.RollbackAsync();
        return Results.NotFound(new { status = "intake_package_not_found", message = "Intake package was not found." });
    }

    await using (var historyCommand = new NpgsqlCommand("""
        INSERT INTO work_register_intake_history (
            work_register_intake_history_id,
            work_register_intake_package_id,
            action,
            summary,
            changed_by_user_id,
            payload_json
        )
        VALUES (
            @history_id,
            @intake_package_id,
            'intake_review_mapping_saved',
            'Project Team Coordinator saved reviewed intake mapping.',
            @changed_by_user_id,
            CAST(@payload_json AS jsonb)
        );
        """, connection, transaction))
    {
        historyCommand.Parameters.AddWithValue("history_id", Guid.NewGuid());
        historyCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        historyCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        historyCommand.Parameters.AddWithValue("payload_json", savedReviewedDataJson);
        await historyCommand.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "intake_review_mapping_saved",
        intakePackageId,
        reviewStatus = "reviewed",
        message = "Reviewed intake mapping saved. Next step is committing the reviewed package to Work Register."
    });
});
/* 055D_2A_GSD_XLSX_EXTRACTION_REVIEW_API_END */



/* 055D_1_INTAKE_WIZARD_GSD_SOW_API_START */


/* 055D_4J_TEMP_CLOUD_EMAIL_NOTIFICATION_START */
static string ProjectPulse055D4JEnv(params string[] names)
{
    foreach (var name in names)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return "";
}

static async Task ProjectPulse055D4JNotifyProjectTeamCoordinatorsAsync(NpgsqlConnection connection, Guid intakePackageId, string commitJsonText, Guid actorUserId)
{
    using var parsed = System.Text.Json.JsonDocument.Parse(commitJsonText);

    if (!parsed.RootElement.TryGetProperty("projectId", out var projectIdProperty)
        || !Guid.TryParse(projectIdProperty.GetString(), out var projectId))
    {
        return;
    }

    var projectCode = parsed.RootElement.TryGetProperty("projectCode", out var projectCodeProperty)
        ? projectCodeProperty.GetString() ?? ""
        : "";

    var projectName = parsed.RootElement.TryGetProperty("projectName", out var projectNameProperty)
        ? projectNameProperty.GetString() ?? ""
        : "";

    var placeholderRows = new List<(string Role, string Name, string Email)>();

    await using (var placeholderCommand = new NpgsqlCommand("""
        SELECT stakeholder_role,
               display_name_snapshot,
               email_snapshot
        FROM work_register_project_stakeholders
        WHERE project_id = @project_id
          AND lower(email_snapshot) LIKE '%@ussignal.cloud'
        ORDER BY stakeholder_role, display_name_snapshot;
        """, connection))
    {
        placeholderCommand.Parameters.AddWithValue("project_id", projectId);

        await using var reader = await placeholderCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            placeholderRows.Add((
                reader.IsDBNull(0) ? "" : reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2)
            ));
        }
    }

    if (placeholderRows.Count == 0)
    {
        return;
    }

    var recipients = new List<string>();

    await using (var recipientCommand = new NpgsqlCommand("""
        WITH role_recipients AS (
            SELECT DISTINCT u.email
            FROM app_users u
            JOIN app_user_role_assignments ura
              ON ura.user_id = u.user_id
             AND ura.is_active = TRUE
            JOIN app_roles ar
              ON ar.app_role_id = ura.app_role_id
             AND ar.is_active = TRUE
            WHERE u.is_active = TRUE
              AND COALESCE(u.login_enabled, TRUE) = TRUE
              AND lower(coalesce(u.email, '')) LIKE '%@ussignal.com'
              AND (
                    lower(ar.role_name) LIKE '%project team coordinator%'
                 OR lower(ar.role_name) LIKE '%project management%'
                 OR lower(ar.role_name) LIKE '%pmo%'
                 OR replace(lower(ar.role_code), '_', ' ') LIKE '%project team coordinator%'
                 OR replace(lower(ar.role_code), '_', ' ') LIKE '%project management%'
                 OR replace(lower(ar.role_code), '_', ' ') LIKE '%pmo%'
              )
        ),
        profile_recipients AS (
            SELECT DISTINCT u.email
            FROM app_users u
            WHERE u.is_active = TRUE
              AND COALESCE(u.login_enabled, TRUE) = TRUE
              AND lower(coalesce(u.email, '')) LIKE '%@ussignal.com'
              AND (
                    lower(coalesce(u.job_title, '')) LIKE '%project team coordinator%'
                 OR lower(coalesce(u.department, '')) LIKE '%project management%'
                 OR lower(coalesce(u.department_name, '')) LIKE '%project management%'
                 OR lower(coalesce(u.team_name, '')) LIKE '%project management%'
                 OR lower(coalesce(u.team_name, '')) LIKE '%pmo%'
              )
        )
        SELECT DISTINCT email
        FROM (
            SELECT email FROM role_recipients
            UNION ALL
            SELECT email FROM profile_recipients
        ) r
        WHERE email IS NOT NULL AND btrim(email) <> ''
        ORDER BY email;
        """, connection))
    {
        await using var reader = await recipientCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            recipients.Add(reader.GetString(0));
        }
    }

    var subject = $"ProjectPulse action required: Add temporary stakeholder(s) to Microsoft Entra for {projectCode}";
    var stakeholderLines = string.Join(Environment.NewLine, placeholderRows.Select(row =>
        $"- {row.Role}: {row.Name} ({row.Email})"
    ));

    var body =
$@"ProjectPulse created one or more non-login temporary stakeholder placeholder accounts during Work Register intake final save.

Project: {projectCode} - {projectName}

Temporary stakeholder(s):
{stakeholderLines}

Action required:
Please add or sync these users through Microsoft Entra using their official @ussignal.com accounts. After Entra sync, ProjectPulse can link future records to the official account instead of the temporary @ussignal.cloud placeholder.

Security note:
The @ussignal.cloud placeholder accounts are created with login_enabled = false and are only for tracking/reporting until the official Entra account exists.";

    var recipientCsv = string.Join(",", recipients);

    Guid notificationId;

    await using (var insertCommand = new NpgsqlCommand("""
        INSERT INTO work_register_temp_cloud_user_notifications (
            project_id,
            work_register_intake_package_id,
            project_code,
            project_name,
            stakeholder_role,
            stakeholder_display_name,
            stakeholder_email,
            notification_recipients,
            notification_subject,
            notification_body,
            notification_status,
            created_by_user_id
        )
        VALUES (
            @project_id,
            @intake_package_id,
            @project_code,
            @project_name,
            @stakeholder_role,
            @stakeholder_display_name,
            @stakeholder_email,
            @recipients,
            @subject,
            @body,
            @status,
            @actor_user_id
        )
        RETURNING work_register_temp_cloud_user_notification_id;
        """, connection))
    {
        insertCommand.Parameters.AddWithValue("project_id", projectId);
        insertCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        insertCommand.Parameters.AddWithValue("project_code", projectCode);
        insertCommand.Parameters.AddWithValue("project_name", projectName);
        insertCommand.Parameters.AddWithValue("stakeholder_role", string.Join("; ", placeholderRows.Select(r => r.Role).Distinct()));
        insertCommand.Parameters.AddWithValue("stakeholder_display_name", string.Join("; ", placeholderRows.Select(r => r.Name).Distinct()));
        insertCommand.Parameters.AddWithValue("stakeholder_email", string.Join("; ", placeholderRows.Select(r => r.Email).Distinct()));
        insertCommand.Parameters.AddWithValue("recipients", recipientCsv);
        insertCommand.Parameters.AddWithValue("subject", subject);
        insertCommand.Parameters.AddWithValue("body", body);
        insertCommand.Parameters.AddWithValue("status", recipients.Count == 0 ? "pending_no_project_team_coordinator_recipient" : "pending");
        insertCommand.Parameters.AddWithValue("actor_user_id", actorUserId);

        notificationId = (Guid)(await insertCommand.ExecuteScalarAsync() ?? Guid.Empty);
    }

    if (recipients.Count == 0)
    {
        return;
    }

    var smtpHost = ProjectPulse055D4JEnv("PTP_SMTP_HOST", "SMTP_HOST");
    var smtpFrom = ProjectPulse055D4JEnv("PTP_SMTP_FROM", "SMTP_FROM", "EMAIL_FROM");
    var smtpPortText = ProjectPulse055D4JEnv("PTP_SMTP_PORT", "SMTP_PORT");
    var smtpUser = ProjectPulse055D4JEnv("PTP_SMTP_USER", "SMTP_USER");
    var smtpPassword = ProjectPulse055D4JEnv("PTP_SMTP_PASSWORD", "SMTP_PASSWORD");
    var smtpEnableSslText = ProjectPulse055D4JEnv("PTP_SMTP_ENABLE_SSL", "SMTP_ENABLE_SSL");

    if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(smtpFrom))
    {
        await using var noSmtpCommand = new NpgsqlCommand("""
            UPDATE work_register_temp_cloud_user_notifications
            SET notification_status = 'pending_no_smtp_configuration',
                notification_error = 'SMTP host/from environment variables are not configured.'
            WHERE work_register_temp_cloud_user_notification_id = @notification_id;
            """, connection);

        noSmtpCommand.Parameters.AddWithValue("notification_id", notificationId);
        await noSmtpCommand.ExecuteNonQueryAsync();
        return;
    }

    try
    {
        var smtpPort = int.TryParse(smtpPortText, out var parsedPort) ? parsedPort : 25;
        var enableSsl = bool.TryParse(smtpEnableSslText, out var parsedSsl) && parsedSsl;

        using var message = new System.Net.Mail.MailMessage();
        message.From = new System.Net.Mail.MailAddress(smtpFrom);
        foreach (var recipient in recipients)
        {
            message.To.Add(recipient);
        }

        message.Subject = subject;
        message.Body = body;
        message.IsBodyHtml = false;

        using var client = new System.Net.Mail.SmtpClient(smtpHost, smtpPort)
        {
            EnableSsl = enableSsl
        };

        if (!string.IsNullOrWhiteSpace(smtpUser))
        {
            client.Credentials = new System.Net.NetworkCredential(smtpUser, smtpPassword);
        }

        await client.SendMailAsync(message);

        await using var sentCommand = new NpgsqlCommand("""
            UPDATE work_register_temp_cloud_user_notifications
            SET notification_status = 'sent',
                sent_at = NOW()
            WHERE work_register_temp_cloud_user_notification_id = @notification_id;
            """, connection);

        sentCommand.Parameters.AddWithValue("notification_id", notificationId);
        await sentCommand.ExecuteNonQueryAsync();
    }
    catch (Exception ex)
    {
        await using var errorCommand = new NpgsqlCommand("""
            UPDATE work_register_temp_cloud_user_notifications
            SET notification_status = 'pending_email_send_failed',
                notification_error = @error
            WHERE work_register_temp_cloud_user_notification_id = @notification_id;
            """, connection);

        errorCommand.Parameters.AddWithValue("notification_id", notificationId);
        errorCommand.Parameters.AddWithValue("error", ex.Message);
        await errorCommand.ExecuteNonQueryAsync();
    }
}
/* 055D_4J_TEMP_CLOUD_EMAIL_NOTIFICATION_END */

/* 055D_4C_FINAL_SAVE_ENDPOINT_START */
static string ProjectPulse055D4CSafeFolderName(string value)
{
    var cleaned = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"[^A-Za-z0-9._ -]+", " ").Trim();
    cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
    return string.IsNullOrWhiteSpace(cleaned) ? "Unknown" : cleaned;
}

static async Task ProjectPulse055D4CCopyIntakeDocumentsToCustomerFolderAsync(NpgsqlConnection connection, Guid intakePackageId, string commitJsonText)
{
    using var parsed = System.Text.Json.JsonDocument.Parse(commitJsonText);

    if (!parsed.RootElement.TryGetProperty("projectId", out var projectIdProperty)
        || !Guid.TryParse(projectIdProperty.GetString(), out var projectId))
    {
        return;
    }

    var projectCode = parsed.RootElement.TryGetProperty("projectCode", out var projectCodeProperty)
        ? ProjectPulse055D4CSafeFolderName(projectCodeProperty.GetString() ?? "")
        : projectId.ToString();

    var customerName = "Unknown Customer";

    await using (var customerCommand = new NpgsqlCommand("""
        SELECT COALESCE(
            NULLIF(reviewed_json->>'customerName', ''),
            NULLIF(customer_hint, ''),
            'Unknown Customer'
        )
        FROM work_register_intake_packages
        WHERE work_register_intake_package_id = @intake_package_id;
        """, connection))
    {
        customerCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        customerName = Convert.ToString(await customerCommand.ExecuteScalarAsync()) ?? "Unknown Customer";
    }

    var targetFolder = System.IO.Path.Combine(
        "/opt/project-time-platform/app/customer-documents",
        ProjectPulse055D4CSafeFolderName(customerName),
        projectCode
    );

    System.IO.Directory.CreateDirectory(targetFolder);

    var docs = new List<(Guid Id, string StoredPath, string OriginalName)>();

    await using (var docsCommand = new NpgsqlCommand("""
        SELECT work_register_project_document_id,
               stored_file_path,
               original_file_name
        FROM work_register_project_documents
        WHERE project_id = @project_id
          AND work_register_intake_package_id = @intake_package_id;
        """, connection))
    {
        docsCommand.Parameters.AddWithValue("project_id", projectId);
        docsCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);

        await using var reader = await docsCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            docs.Add((
                reader.GetGuid(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "document" : reader.GetString(2)
            ));
        }
    }

    foreach (var doc in docs)
    {
        try
        {
            var sourcePath = doc.StoredPath;

            if (!System.IO.Path.IsPathRooted(sourcePath))
            {
                sourcePath = System.IO.Path.Combine("/opt/project-time-platform/app", sourcePath.TrimStart('/'));
            }

            if (!System.IO.File.Exists(sourcePath))
            {
                await using var missingCommand = new NpgsqlCommand("""
                    UPDATE work_register_project_documents
                    SET customer_folder_path = @folder,
                        document_routing_status = 'source_file_missing'
                    WHERE work_register_project_document_id = @document_id;
                    """, connection);

                missingCommand.Parameters.AddWithValue("folder", targetFolder);
                missingCommand.Parameters.AddWithValue("document_id", doc.Id);
                await missingCommand.ExecuteNonQueryAsync();
                continue;
            }

            var safeName = ProjectPulse055D4CSafeFolderName(doc.OriginalName);
            var targetPath = System.IO.Path.Combine(targetFolder, safeName);

            if (System.IO.File.Exists(targetPath))
            {
                var extension = System.IO.Path.GetExtension(safeName);
                var baseName = System.IO.Path.GetFileNameWithoutExtension(safeName);
                targetPath = System.IO.Path.Combine(targetFolder, $"{baseName}-{doc.Id:N}{extension}");
            }

            System.IO.File.Copy(sourcePath, targetPath, overwrite: false);

            await using var updateCommand = new NpgsqlCommand("""
                UPDATE work_register_project_documents
                SET customer_folder_path = @folder,
                    stored_file_path = @target_path,
                    copied_to_customer_folder_at = NOW(),
                    document_routing_status = 'copied_to_customer_folder'
                WHERE work_register_project_document_id = @document_id;
                """, connection);

            updateCommand.Parameters.AddWithValue("folder", targetFolder);
            updateCommand.Parameters.AddWithValue("target_path", targetPath);
            updateCommand.Parameters.AddWithValue("document_id", doc.Id);
            await updateCommand.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            await using var errorCommand = new NpgsqlCommand("""
                UPDATE work_register_project_documents
                SET customer_folder_path = @folder,
                    document_routing_status = @status
                WHERE work_register_project_document_id = @document_id;
                """, connection);

            errorCommand.Parameters.AddWithValue("folder", targetFolder);
            errorCommand.Parameters.AddWithValue("status", "copy_failed: " + ex.Message);
            errorCommand.Parameters.AddWithValue("document_id", doc.Id);
            await errorCommand.ExecuteNonQueryAsync();
        }
    }
}


// 055D_5K1_SAFE_POST_COMMIT_IDENTIFIER_APPLY_ENDPOINT
app.MapPost("/api/work-register/intake/packages/{intakePackageId:guid}/billing-identifiers/apply", async (Guid intakePackageId, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);

    if (sessionUserId is null)
    {
        return Results.Json(new
        {
            status = "session_required",
            message = "Missing ProjectPulse session token."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    System.Text.Json.JsonElement payload = default;

    try
    {
        payload = await System.Text.Json.JsonSerializer.DeserializeAsync<System.Text.Json.JsonElement>(httpContext.Request.Body);
    }
    catch
    {
        payload = default;
    }

    var projectIdText = ProjectPulse055D5K1JsonString(payload, "projectId", "project_id", "workId", "work_id", "workRegisterId", "work_register_id");
    var projectName = ProjectPulse055D5K1JsonString(payload, "projectName", "project_name", "workName", "work_name");
    var customerIdText = ProjectPulse055D5K1JsonString(payload, "customerId", "customer_id", "clientId", "client_id");

    var sellQuoteNumber = ProjectPulse055D5K1JsonString(payload, "sellQuoteNumber", "sell_quote_number", "sellQuote", "sell_quote");
    var salesforceIdNumber = ProjectPulse055D5K1JsonString(payload, "salesforceIdNumber", "salesforce_id_number", "salesforceId", "salesforce_id");
    var certiniaIdNumber = ProjectPulse055D5K1JsonString(payload, "certiniaIdNumber", "certinia_id_number", "certiniaId", "certinia_id");
    var sowSignedDate = ProjectPulse055D5K1JsonString(payload, "sowSignedDate", "sow_signed_date", "signedDate", "signed_date");

    if (string.IsNullOrWhiteSpace(sellQuoteNumber)
        && string.IsNullOrWhiteSpace(salesforceIdNumber)
        && string.IsNullOrWhiteSpace(certiniaIdNumber)
        && string.IsNullOrWhiteSpace(sowSignedDate))
    {
        return Results.Json(new
        {
            status = "skipped",
            message = "No identifiers or SOW signed date were supplied."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var validation = ValidateConfig(config);
    if (validation is not null) return validation;

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using (var accessCommand = new NpgsqlCommand("SELECT projectpulse055d7_can_complete_intake(@user_id);", connection))
        {
            accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
            var canApply = await accessCommand.ExecuteScalarAsync();

            if (canApply is not bool allowed || !allowed)
            {
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "Final Work Register fields can only be changed by PTC, Project Management, PMO, Project Manager, Administrator, or Super Administrator users."
                }, statusCode: StatusCodes.Status403Forbidden);
            }
        }

        Guid projectId;

        if (!Guid.TryParse(projectIdText, out projectId))
        {
            var normalizedCustomerId = Guid.TryParse(customerIdText, out var parsedCustomerId)
                ? parsedCustomerId.ToString()
                : "";

            await using var findCommand = new NpgsqlCommand(@"
                select p.project_id
                  from projects p
                 where (@project_name = '' or lower(trim(p.project_name)) = lower(trim(@project_name)))
                   and (@customer_id = '' or p.client_id = @customer_id::uuid)
                 order by p.created_at desc nulls last, p.updated_at desc nulls last
                 limit 1;", connection);

            findCommand.Parameters.AddWithValue("project_name", projectName ?? "");
            findCommand.Parameters.AddWithValue("customer_id", normalizedCustomerId);

            var found = await findCommand.ExecuteScalarAsync();

            if (found is Guid foundGuid)
            {
                projectId = foundGuid;
            }
            else if (!Guid.TryParse(Convert.ToString(found), out projectId))
            {
                return Results.Json(new
                {
                    status = "not_found",
                    message = "The Work Register project was created, but the apply endpoint could not find it to save identifiers.",
                    projectName,
                    customerId = normalizedCustomerId
                }, statusCode: StatusCodes.Status404NotFound);
            }
        }

        await using (var updateProjectCommand = new NpgsqlCommand(@"
            update projects
               set sell_quote_number = coalesce(nullif(@sell_quote_number, ''), sell_quote_number),
                   salesforce_id_number = coalesce(nullif(@salesforce_id_number, ''), salesforce_id_number),
                   certinia_id_number = coalesce(nullif(@certinia_id_number, ''), certinia_id_number),
                   sow_signed_date = coalesce(nullif(@sow_signed_date, '')::date, sow_signed_date),
                   updated_at = now()
             where project_id = @project_id;", connection))
        {
            updateProjectCommand.Parameters.AddWithValue("project_id", projectId);
            updateProjectCommand.Parameters.AddWithValue("sell_quote_number", sellQuoteNumber ?? "");
            updateProjectCommand.Parameters.AddWithValue("salesforce_id_number", salesforceIdNumber ?? "");
            updateProjectCommand.Parameters.AddWithValue("certinia_id_number", certiniaIdNumber ?? "");
            updateProjectCommand.Parameters.AddWithValue("sow_signed_date", sowSignedDate ?? "");
            await updateProjectCommand.ExecuteNonQueryAsync();
        }

        try
        {
            await using var updateMetadataCommand = new NpgsqlCommand(@"
                update work_register_project_metadata
                   set sell_quote_number = coalesce(nullif(@sell_quote_number, ''), sell_quote_number),
                       salesforce_id_number = coalesce(nullif(@salesforce_id_number, ''), salesforce_id_number),
                       certinia_id_number = coalesce(nullif(@certinia_id_number, ''), certinia_id_number),
                       updated_at = now()
                 where project_id = @project_id;", connection);

            updateMetadataCommand.Parameters.AddWithValue("project_id", projectId);
            updateMetadataCommand.Parameters.AddWithValue("sell_quote_number", sellQuoteNumber ?? "");
            updateMetadataCommand.Parameters.AddWithValue("salesforce_id_number", salesforceIdNumber ?? "");
            updateMetadataCommand.Parameters.AddWithValue("certinia_id_number", certiniaIdNumber ?? "");
            await updateMetadataCommand.ExecuteNonQueryAsync();
        }
        catch
        {
            // Metadata table update is best-effort. The projects table is the source of truth.
        }

        return Results.Json(new
        {
            status = "ok",
            message = "Billing identifiers applied to Work Register project.",
            projectId,
            sellQuoteNumber,
            salesforceIdNumber,
            certiniaIdNumber,
            sowSignedDate
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "error",
            message = "Unable to apply billing identifiers to the created Work Register project.",
            detail = ex.Message
        }, statusCode: StatusCodes.Status500InternalServerError);
    }
});



// 055D_5L2_APPLY_V3_ENDPOINT
app.MapPost("/api/work-register/intake/packages/{intakePackageId:guid}/billing-identifiers/apply-v3", async (Guid intakePackageId, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);

    if (sessionUserId is null)
    {
        return Results.Json(new
        {
            status = "session_required",
            message = "Missing ProjectPulse session token."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    System.Text.Json.JsonElement payload = default;

    try
    {
        payload = await System.Text.Json.JsonSerializer.DeserializeAsync<System.Text.Json.JsonElement>(httpContext.Request.Body);
    }
    catch
    {
        payload = default;
    }

    var projectIdText = ProjectPulse055D5L2JsonString(payload, "projectId", "project_id", "workId", "work_id", "workRegisterId", "work_register_id");
    var projectName = ProjectPulse055D5L2JsonString(payload, "projectName", "project_name", "workName", "work_name");
    var customerIdText = ProjectPulse055D5L2JsonString(payload, "customerId", "customer_id", "clientId", "client_id");

    var contractType = ProjectPulse055D5L2JsonString(payload, "contractType", "contract_type");
    var sellQuoteNumber = ProjectPulse055D5L2JsonString(payload, "sellQuoteNumber", "sell_quote_number", "sellQuote", "sell_quote");
    var salesforceIdNumber = ProjectPulse055D5L2JsonString(payload, "salesforceIdNumber", "salesforce_id_number", "salesforceId", "salesforce_id");
    var certiniaIdNumber = ProjectPulse055D5L2JsonString(payload, "certiniaIdNumber", "certinia_id_number", "certiniaId", "certinia_id");
    var sowSignedDate = ProjectPulse055D5L2JsonString(payload, "sowSignedDate", "sow_signed_date", "signedDate", "signed_date");

    var config = DatabaseConfig.FromEnvironment();
    var validation = ValidateConfig(config);
    if (validation is not null) return validation;

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using (var accessCommand = new NpgsqlCommand("SELECT projectpulse055d7_can_complete_intake(@user_id);", connection))
        {
            accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
            var canApply = await accessCommand.ExecuteScalarAsync();

            if (canApply is not bool allowed || !allowed)
            {
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "Final Work Register fields can only be changed by PTC, Project Management, PMO, Project Manager, Administrator, or Super Administrator users."
                }, statusCode: StatusCodes.Status403Forbidden);
            }
        }

        Guid projectId;

        if (!Guid.TryParse(projectIdText, out projectId))
        {
            var normalizedCustomerId = Guid.TryParse(customerIdText, out var parsedCustomerId)
                ? parsedCustomerId.ToString()
                : "";

            await using var findCommand = new NpgsqlCommand(@"
                select p.project_id
                  from projects p
                 where (@project_name = '' or lower(trim(p.project_name)) = lower(trim(@project_name)))
                   and (@customer_id = '' or p.client_id = @customer_id::uuid)
                 order by p.created_at desc nulls last, p.updated_at desc nulls last
                 limit 1;", connection);

            findCommand.Parameters.AddWithValue("project_name", projectName ?? "");
            findCommand.Parameters.AddWithValue("customer_id", normalizedCustomerId);

            var found = await findCommand.ExecuteScalarAsync();

            if (found is Guid foundGuid)
            {
                projectId = foundGuid;
            }
            else if (!Guid.TryParse(Convert.ToString(found), out projectId))
            {
                return Results.Json(new
                {
                    status = "not_found",
                    message = "Project was created, but apply-v3 could not find it.",
                    projectName,
                    customerId = normalizedCustomerId
                }, statusCode: StatusCodes.Status404NotFound);
            }
        }

        await using (var updateProjectCommand = new NpgsqlCommand(@"
            update projects
               set contract_type = coalesce(nullif(@contract_type, ''), contract_type),
                   sell_quote_number = coalesce(nullif(@sell_quote_number, ''), sell_quote_number),
                   salesforce_id_number = coalesce(nullif(@salesforce_id_number, ''), salesforce_id_number),
                   certinia_id_number = coalesce(nullif(@certinia_id_number, ''), certinia_id_number),
                   sow_signed_date = coalesce(nullif(@sow_signed_date, '')::date, sow_signed_date),
                   updated_at = now()
             where project_id = @project_id;", connection))
        {
            updateProjectCommand.Parameters.AddWithValue("project_id", projectId);
            updateProjectCommand.Parameters.AddWithValue("contract_type", contractType ?? "");
            updateProjectCommand.Parameters.AddWithValue("sell_quote_number", sellQuoteNumber ?? "");
            updateProjectCommand.Parameters.AddWithValue("salesforce_id_number", salesforceIdNumber ?? "");
            updateProjectCommand.Parameters.AddWithValue("certinia_id_number", certiniaIdNumber ?? "");
            updateProjectCommand.Parameters.AddWithValue("sow_signed_date", sowSignedDate ?? "");
            await updateProjectCommand.ExecuteNonQueryAsync();
        }

        try
        {
            await using var updateMetadataCommand = new NpgsqlCommand(@"
                update work_register_project_metadata
                   set sell_quote_number = coalesce(nullif(@sell_quote_number, ''), sell_quote_number),
                       salesforce_id_number = coalesce(nullif(@salesforce_id_number, ''), salesforce_id_number),
                       certinia_id_number = coalesce(nullif(@certinia_id_number, ''), certinia_id_number),
                       updated_at = now()
                 where project_id = @project_id;", connection);

            updateMetadataCommand.Parameters.AddWithValue("project_id", projectId);
            updateMetadataCommand.Parameters.AddWithValue("sell_quote_number", sellQuoteNumber ?? "");
            updateMetadataCommand.Parameters.AddWithValue("salesforce_id_number", salesforceIdNumber ?? "");
            updateMetadataCommand.Parameters.AddWithValue("certinia_id_number", certiniaIdNumber ?? "");
            await updateMetadataCommand.ExecuteNonQueryAsync();
        }
        catch
        {
            // Metadata table update is best-effort.
        }

        return Results.Json(new
        {
            status = "ok",
            message = "Final Create Work fields applied.",
            projectId,
            contractType,
            sellQuoteNumber,
            salesforceIdNumber,
            certiniaIdNumber,
            sowSignedDate
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "error",
            message = "Unable to apply final Create Work fields.",
            detail = ex.Message
        }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/work-register/intake/packages/{intakePackageId:guid}/commit", async (Guid intakePackageId, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);

    if (sessionUserId is null)
    {
        return Results.Json(new
        {
            status = "session_required",
            message = "Missing ProjectPulse session token."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var validation = ValidateConfig(config);
    if (validation is not null) return validation;

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        if (!await ProjectTime.Api.Modules.WorkRegisterAuthorization.HasCreateAuthorityAsync(
                connection, httpContext, cancellationToken: httpContext.RequestAborted))
        {
            return Results.Json(new
            {
                status = "access_denied",
                message = "Only a Project Team Coordinator, Administrator, or Super Administrator can create a Work Register record."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        await using (var dateValidationCommand = new NpgsqlCommand("""
            SELECT
                NULLIF(btrim(reviewed_json->>'estimatedEndDate'), ''),
                CURRENT_DATE,
                EXISTS (
                    SELECT 1
                    FROM work_register_intake_commits committed
                    WHERE committed.work_register_intake_package_id =
                          work_register_intake_packages.work_register_intake_package_id
                )
            FROM work_register_intake_packages
            WHERE work_register_intake_package_id = @intake_package_id
            LIMIT 1;
            """, connection))
        {
            dateValidationCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);

            await using var dateReader = await dateValidationCommand.ExecuteReaderAsync(httpContext.RequestAborted);
            if (await dateReader.ReadAsync(httpContext.RequestAborted) && !dateReader.GetBoolean(2))
            {
                var estimatedEndDateText = dateReader.IsDBNull(0) ? "" : dateReader.GetString(0);
                var projectStartDate = dateReader.GetFieldValue<DateOnly>(1);

                if (!string.IsNullOrWhiteSpace(estimatedEndDateText))
                {
                    if (!DateOnly.TryParseExact(
                            estimatedEndDateText,
                            "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out var estimatedEndDate))
                    {
                        return Results.BadRequest(new
                        {
                            status = "validation_error",
                            message = "Estimated end date must use YYYY-MM-DD."
                        });
                    }

                    if (estimatedEndDate < projectStartDate)
                    {
                        return Results.BadRequest(new
                        {
                            status = "validation_error",
                            message = "Estimated end date cannot be before the project creation date."
                        });
                    }
                }
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(httpContext.RequestAborted);
        await using var command = new NpgsqlCommand("""
            SELECT projectpulse055d4d_commit_intake_package(@intake_package_id, @actor_user_id)::text;
            """, connection, transaction);

        command.Parameters.AddWithValue("intake_package_id", intakePackageId);
        command.Parameters.AddWithValue("actor_user_id", sessionUserId.Value);

        var jsonText = Convert.ToString(await command.ExecuteScalarAsync()) ?? "{\"status\":\"error\",\"message\":\"No response from commit function.\"}";

        using var parsed = System.Text.Json.JsonDocument.Parse(jsonText);
        var status = parsed.RootElement.TryGetProperty("status", out var statusProperty)
            ? statusProperty.GetString()
            : "";

        if (status is "database_error" or "validation_error" or "not_found")
        {
            await transaction.RollbackAsync(httpContext.RequestAborted);
            return Results.Content(jsonText, "application/json", statusCode: StatusCodes.Status400BadRequest);
        }

        if (status is "committed" or "already_committed")
        {
            if (!parsed.RootElement.TryGetProperty("projectId", out var projectIdProperty)
                || !Guid.TryParse(projectIdProperty.GetString(), out var createdProjectId))
            {
                throw new InvalidOperationException("The committed Work Register response did not include a project ID.");
            }

            await using var auditCommand = new NpgsqlCommand("""
                    INSERT INTO work_register_change_history (
                        work_register_change_history_id, source_table, work_id, action,
                        change_summary, changed_fields_csv, changed_by_user_id,
                        old_value_json, new_value_json, changed_at)
                    SELECT
                        gen_random_uuid(), 'projects', @project_id, 'work_register_created',
                        'Authorized 055D user created Work Register from ' ||
                            CASE WHEN source_mode = 'sell_import' THEN 'SELL' ELSE 'GSD' END ||
                            ' intake package.',
                        'Project Name,Customer,Source,SELL Quote,Pricing / Rate Review',
                        @actor, NULL,
                        jsonb_build_object(
                            'intakePackageId', work_register_intake_package_id,
                            'sourceMode', source_mode,
                            'projectName', COALESCE(reviewed_json->'projectName', extracted_json->'projectName'),
                            'customerId', customer_id,
                            'sellQuoteNumber', COALESCE(reviewed_json->'sellQuoteNumber', extracted_json->'sellQuoteNumber'),
                            'rates', COALESCE(reviewed_json->'rates', extracted_json->'rates', '[]'::jsonb)
                        ),
                        NOW()
                    FROM work_register_intake_packages
                    WHERE work_register_intake_package_id = @intake_package_id
                      AND NOT EXISTS (
                          SELECT 1 FROM work_register_change_history
                          WHERE source_table = 'projects'
                            AND work_id = @project_id
                            AND action = 'work_register_created'
                      );
                    """, connection, transaction);
            auditCommand.Parameters.AddWithValue("project_id", createdProjectId);
            auditCommand.Parameters.AddWithValue("actor", sessionUserId.Value);
            auditCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
            await auditCommand.ExecuteNonQueryAsync(httpContext.RequestAborted);

            await using var auditGuardCommand = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM work_register_change_history
                    WHERE source_table = 'projects'
                      AND work_id = @project_id
                      AND action = 'work_register_created'
                );
                """, connection, transaction);
            auditGuardCommand.Parameters.AddWithValue("project_id", createdProjectId);
            var auditRecorded = Convert.ToBoolean(await auditGuardCommand.ExecuteScalarAsync(httpContext.RequestAborted));
            if (!auditRecorded)
            {
                throw new InvalidOperationException("Work Register creation audit evidence was not recorded.");
            }
        }

        await transaction.CommitAsync(httpContext.RequestAborted);

        if (status is "committed" or "already_committed")
        {
            await ProjectPulse055D4CCopyIntakeDocumentsToCustomerFolderAsync(connection, intakePackageId, jsonText);
            await ProjectPulse055D4JNotifyProjectTeamCoordinatorsAsync(connection, intakePackageId, jsonText, sessionUserId.Value);
        }

        return Results.Content(jsonText, "application/json");
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "server_exception",
            message = ex.Message,
            exceptionType = ex.GetType().FullName
        }, statusCode: StatusCodes.Status500InternalServerError);
    }
});
/* 055D_4C_FINAL_SAVE_ENDPOINT_END */

app.MapPost("/api/work-register/intake/packages/upload", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055D1_intake_upload_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!httpContext.Request.HasFormContentType)
    {
        return Results.BadRequest(new { status = "invalid_upload", message = "Intake package must be sent as multipart/form-data." });
    }

    var form = await httpContext.Request.ReadFormAsync();

    string ReadFormString(string name, string fallback = "")
    {
        return form.TryGetValue(name, out var values) ? (values.ToString() ?? fallback).Trim() : fallback;
    }

    bool ReadFormBool(string name, bool fallback = false)
    {
        var value = ReadFormString(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    string SafeFileName(string value)
    {
        var fileName = System.IO.Path.GetFileName(value ?? "uploaded-document");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "uploaded-document";
        }

        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        fileName = fileName.Replace(" ", "_").Trim();

        if (fileName.Length > 180)
        {
            var extension = System.IO.Path.GetExtension(fileName);
            var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            fileName = baseName[..Math.Min(baseName.Length, 140)] + extension;
        }

        return fileName;
    }

    string FileNameWithoutExtension(string value)
    {
        var clean = SafeFileName(value);
        var without = System.IO.Path.GetFileNameWithoutExtension(clean);
        return string.IsNullOrWhiteSpace(without) ? clean : without.Replace("_", " ");
    }

    Guid? ReadFormGuid(string name)
    {
        var value = ReadFormString(name);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    var requestedWorkType = ReadFormString("requestedWorkType", "Project");
    var contractType = ReadFormString("contractType", "Fixed Price");

    // 055D_5A_BILLING_IDENTIFIER_UPLOAD_FIELDS
    var sellQuoteNumber = ProjectPulseFormValue(form, "sellQuoteNumber", "sell_quote_number", "quoteNumber", "quote_number");
    var salesforceIdNumber = ProjectPulseFormValue(form, "salesforceIdNumber", "salesforce_id_number", "salesforceId", "salesforce_id", "opportunityId", "opportunity_id");
    var certiniaIdNumber = ProjectPulseFormValue(form, "certiniaIdNumber", "certinia_id_number", "certiniaId", "certinia_id", "certiniaProjectId", "certinia_project_id");
    var customerId = ReadFormGuid("customerId");
    var customerHint = ReadFormString("customerName", ReadFormString("customerHint"));
    var projectNameHint = ReadFormString("projectNameHint");
    var notes = ReadFormString("notes");
    var reason = ReadFormString("reason");
    var sowSignedDateText = ReadFormString("sowSignedDate");
    var estimatedEndDateText = ReadFormString("estimatedEndDate");
    /* 055D_2A_INTAKE_UPLOAD_CUSTOMER_CONTRACT_PATCH */
    var skipGsd = ReadFormBool("skipGsd", false);
    var skipSow = ReadFormBool("skipSow", false);

    var gsdFile = form.Files.GetFile("gsdFile");
    var sowFile = form.Files.GetFile("sowFile");
    var approvalFile = form.Files.GetFile("approvalFile");

    var isProjectLike = string.Equals(requestedWorkType, "Project", StringComparison.OrdinalIgnoreCase)
        || string.Equals(requestedWorkType, "IQS", StringComparison.OrdinalIgnoreCase);

    if (customerId is null)
    {
        return Results.BadRequest(new { status = "customer_required", message = "Select a customer from the Customer Directory before creating an intake package. If the customer does not exist, onboard the customer first." });
    }

    if (string.IsNullOrWhiteSpace(reason))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Intake reason is required for audit history." });
    }

    DateOnly? sowSignedDate = null;
    if (!string.IsNullOrWhiteSpace(sowSignedDateText))
    {
        if (!DateOnly.TryParseExact(
                sowSignedDateText,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedSowSignedDate))
        {
            return Results.BadRequest(new { status = "validation_failed", message = "SOW signed date must use YYYY-MM-DD." });
        }
        sowSignedDate = parsedSowSignedDate;
    }

    DateOnly? estimatedEndDate = null;
    if (!string.IsNullOrWhiteSpace(estimatedEndDateText))
    {
        if (!DateOnly.TryParseExact(
                estimatedEndDateText,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedEstimatedEndDate))
        {
            return Results.BadRequest(new { status = "validation_failed", message = "Estimated end date must use YYYY-MM-DD." });
        }
        if (parsedEstimatedEndDate < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Results.BadRequest(new { status = "validation_failed", message = "Estimated end date cannot be before the project creation date." });
        }
        estimatedEndDate = parsedEstimatedEndDate;
    }

    if (isProjectLike && !skipGsd && (gsdFile is null || gsdFile.Length <= 0))
    {
        return Results.BadRequest(new { status = "gsd_required", message = "GSD upload is expected for Project/IQS intake. Check 'No GSD available' only when this must be created manually." });
    }

    if (isProjectLike && !skipSow && (sowFile is null || sowFile.Length <= 0))
    {
        return Results.BadRequest(new { status = "sow_required", message = "SOW upload is expected for Project/IQS intake. Check 'No SOW available' only when this work type does not require it yet." });
    }

    foreach (var uploadedFile in new[] { gsdFile, sowFile, approvalFile })
    {
        if (uploadedFile is not null && uploadedFile.Length > 50L * 1024L * 1024L)
        {
            return Results.BadRequest(new { status = "file_too_large", message = $"File {uploadedFile.FileName} exceeds the 50 MB upload limit." });
        }
    }

    var dbConfig = DatabaseConfig.FromEnvironment();

    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    var canCreateIntake = await ProjectTime.Api.Modules.WorkRegisterAuthorization.HasCreateAuthorityAsync(
        connection, httpContext, cancellationToken: httpContext.RequestAborted);

    if (!canCreateIntake)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Only a Project Team Coordinator, Administrator, or Super Administrator can create intake packages."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var intakePackageId = Guid.NewGuid();
    var uploadRoot = GetProjectPulseUploadRoot();
    var intakeFolder = System.IO.Path.Combine(uploadRoot, "work-register-intake", intakePackageId.ToString("N"));
    System.IO.Directory.CreateDirectory(intakeFolder);

    async Task<(Guid DocumentId, string DocumentType, string OriginalFileName, string StoredFilePath, string ContentType, long FileSizeBytes)?> SaveIntakeFileAsync(IFormFile? file, string documentType)
    {
        if (file is null || file.Length <= 0)
        {
            return null;
        }

        var documentId = Guid.NewGuid();
        var originalFileName = SafeFileName(file.FileName);
        var storedFileName = $"{documentId:N}_{originalFileName}";
        var storedFilePath = System.IO.Path.Combine(intakeFolder, storedFileName);

        await using (var stream = System.IO.File.Create(storedFilePath))
        {
            await file.CopyToAsync(stream);
        }

        return (
            documentId,
            documentType,
            originalFileName,
            storedFilePath,
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            file.Length
        );
    }

    var savedDocuments = new List<(Guid DocumentId, string DocumentType, string OriginalFileName, string StoredFilePath, string ContentType, long FileSizeBytes)>();

    var savedGsd = await SaveIntakeFileAsync(gsdFile, "GSD");
    if (savedGsd is not null) savedDocuments.Add(savedGsd.Value);

    var savedSow = await SaveIntakeFileAsync(sowFile, "SOW");
    if (savedSow is not null) savedDocuments.Add(savedSow.Value);

    var savedApproval = await SaveIntakeFileAsync(approvalFile, "Customer Approval");
    if (savedApproval is not null) savedDocuments.Add(savedApproval.Value);

    if (string.IsNullOrWhiteSpace(projectNameHint) && savedGsd is not null)
    {
        projectNameHint = FileNameWithoutExtension(savedGsd.Value.OriginalFileName);
    }

    if (string.IsNullOrWhiteSpace(projectNameHint) && savedSow is not null)
    {
        projectNameHint = FileNameWithoutExtension(savedSow.Value.OriginalFileName);
    }

    var extractionPayload = JsonSerializer.Serialize(new
    {
        extractionStatus = "pending_parser",
        parserStage = "055D.1_upload_only",
        message = "GSD/SOW files are stored. 055D.2 will extract customer, project name, AE, SA, SAA, rates, tasks, and hours.",
        requestedWorkType,
        contractType,
        customerId,
        customerHint,
        projectNameHint,
        sowSignedDate,
        estimatedEndDate,
        uploadedDocuments = savedDocuments.Select(document => new
        {
            document.DocumentId,
            document.DocumentType,
            document.OriginalFileName,
            document.ContentType,
            document.FileSizeBytes
        }).ToList()
    });

    await using var transaction = await connection.BeginTransactionAsync();

    await using (var packageCommand = new NpgsqlCommand("""
        INSERT INTO work_register_intake_packages (
            work_register_intake_package_id,
            intake_status,
            requested_work_type,
            contract_type,
            sell_quote_number,
            salesforce_id_number,
            certinia_id_number,
            customer_id,
            source_mode,
            customer_hint,
            project_name_hint,
            notes,
            extraction_status,
            extracted_json,
            created_by_user_id,
            updated_at
        )
        VALUES (
            @intake_package_id,
            'uploaded',
            @requested_work_type,
            @contract_type,
            @sell_quote_number,
            @salesforce_id_number,
            @certinia_id_number,
            @customer_id,
            'gsd_sow_upload',
            @customer_hint,
            @project_name_hint,
            @notes,
            'pending_parser',
            CAST(@extracted_json AS jsonb),
            @created_by_user_id,
            NOW()
        );
        /* 055D_2D_REPAIRED_INTAKE_PACKAGE_INSERT */
        """, connection, transaction))
    {
        packageCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        packageCommand.Parameters.AddWithValue("requested_work_type", requestedWorkType);
        packageCommand.Parameters.AddWithValue("contract_type", string.IsNullOrWhiteSpace(contractType) ? "Fixed Price" : contractType);
        packageCommand.Parameters.AddWithValue("sell_quote_number", sellQuoteNumber ?? string.Empty);
        packageCommand.Parameters.AddWithValue("salesforce_id_number", salesforceIdNumber ?? string.Empty);
        packageCommand.Parameters.AddWithValue("certinia_id_number", certiniaIdNumber ?? string.Empty);
        packageCommand.Parameters.Add("customer_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value = customerId is null ? DBNull.Value : customerId.Value;
        packageCommand.Parameters.AddWithValue("customer_hint", customerHint);
        packageCommand.Parameters.AddWithValue("project_name_hint", projectNameHint);
        packageCommand.Parameters.AddWithValue("notes", notes);
        packageCommand.Parameters.AddWithValue("extracted_json", extractionPayload);
        packageCommand.Parameters.AddWithValue("created_by_user_id", sessionUserId.Value);
        await packageCommand.ExecuteNonQueryAsync();
    }

    foreach (var savedDocument in savedDocuments)
    {
        await using var documentCommand = new NpgsqlCommand("""
            INSERT INTO work_register_intake_documents (
                work_register_intake_document_id,
                work_register_intake_package_id,
                document_type,
                original_file_name,
                stored_file_path,
                content_type,
                file_size_bytes,
                uploaded_by_user_id
            )
            VALUES (
                @document_id,
                @intake_package_id,
                @document_type,
                @original_file_name,
                @stored_file_path,
                @content_type,
                @file_size_bytes,
                @uploaded_by_user_id
            );
            """, connection, transaction);

        documentCommand.Parameters.AddWithValue("document_id", savedDocument.DocumentId);
        documentCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        documentCommand.Parameters.AddWithValue("document_type", savedDocument.DocumentType);
        documentCommand.Parameters.AddWithValue("original_file_name", savedDocument.OriginalFileName);
        documentCommand.Parameters.AddWithValue("stored_file_path", savedDocument.StoredFilePath);
        documentCommand.Parameters.AddWithValue("content_type", savedDocument.ContentType);
        documentCommand.Parameters.AddWithValue("file_size_bytes", savedDocument.FileSizeBytes);
        documentCommand.Parameters.AddWithValue("uploaded_by_user_id", sessionUserId.Value);
        await documentCommand.ExecuteNonQueryAsync();
    }

    var historyPayload = JsonSerializer.Serialize(new
    {
        intakePackageId,
        requestedWorkType,
        customerHint,
        projectNameHint,
        notes,
        reason,
        documents = savedDocuments.Select(document => new
        {
            document.DocumentId,
            document.DocumentType,
            document.OriginalFileName,
            document.FileSizeBytes
        }).ToList()
    });

    await using (var historyCommand = new NpgsqlCommand("""
        INSERT INTO work_register_intake_history (
            work_register_intake_history_id,
            work_register_intake_package_id,
            action,
            summary,
            changed_by_user_id,
            payload_json
        )
        VALUES (
            @history_id,
            @intake_package_id,
            'intake_documents_uploaded',
            @summary,
            @changed_by_user_id,
            CAST(@payload_json AS jsonb)
        );
        """, connection, transaction))
    {
        historyCommand.Parameters.AddWithValue("history_id", Guid.NewGuid());
        historyCommand.Parameters.AddWithValue("intake_package_id", intakePackageId);
        historyCommand.Parameters.AddWithValue("summary", $"Initial intake package uploaded with {savedDocuments.Count} document(s).");
        historyCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        historyCommand.Parameters.AddWithValue("payload_json", historyPayload);
        await historyCommand.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "intake_package_uploaded",
        intakePackageId,
        requestedWorkType,
        contractType,
        customerId,
        customerHint,
        projectNameHint,
        extractionStatus = "pending_parser",
        uploadedDocumentCount = savedDocuments.Count,
        documents = savedDocuments.Select(document => new
        {
            documentId = document.DocumentId,
            documentType = document.DocumentType,
            originalFileName = document.OriginalFileName,
            fileSizeBytes = document.FileSizeBytes
        }).ToList(),
        message = "Initial intake package uploaded. GSD/SOW extraction will be handled in the next parser step."
    });
}).DisableAntiforgery();
/* 055D_1_INTAKE_WIZARD_GSD_SOW_API_END */


/* 055C_10_LOCAL_DOCUMENT_UPLOAD_API_START */
app.MapPost("/api/work-register/projects/documents/upload", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055C10_document_upload_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!httpContext.Request.HasFormContentType)
    {
        return Results.BadRequest(new { status = "invalid_upload", message = "Upload must be sent as multipart/form-data." });
    }

    var form = await httpContext.Request.ReadFormAsync();
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

    string ReadFormString(string name, string fallback = "")
    {
        return form.TryGetValue(name, out var values) ? (values.ToString() ?? fallback).Trim() : fallback;
    }

    Guid? ReadFormGuid(string name)
    {
        var value = ReadFormString(name);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    DateTime? ReadFormDate(string name)
    {
        var value = ReadFormString(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : null;
    }

    string SafeFileName(string value)
    {
        var fileName = System.IO.Path.GetFileName(value ?? "uploaded-document");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "uploaded-document";
        }

        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        fileName = fileName.Replace(" ", "_").Trim();

        if (fileName.Length > 180)
        {
            var extension = System.IO.Path.GetExtension(fileName);
            var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
            fileName = baseName[..Math.Min(baseName.Length, 140)] + extension;
        }

        return fileName;
    }

    var projectId = ReadFormGuid("projectId");
    var documentName = ReadFormString("documentName");
    var documentType = ReadFormString("documentType", "Other");
    var versionLabel = ReadFormString("versionLabel");
    var visibility = ReadFormString("visibility", "project_team");
    var effectiveDate = ReadFormDate("effectiveDate");
    var notes = ReadFormString("notes");
    var reason = ReadFormString("reason");

    if (projectId is null)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Project ID is required." });
    }

    if (file is null || file.Length <= 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Choose a local file to upload." });
    }

    if (file.Length > 50L * 1024L * 1024L)
    {
        return Results.BadRequest(new { status = "file_too_large", message = "Document uploads are limited to 50 MB." });
    }

    var originalFileName = SafeFileName(file.FileName);
    if (string.IsNullOrWhiteSpace(documentName))
    {
        documentName = originalFileName;
    }

    if (string.IsNullOrWhiteSpace(reason))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Reason is required for document upload audit history." });
    }

    var dbConfig = DatabaseConfig.FromEnvironment();

    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    var canEditWorkRegister = false;
    await using (var accessCommand = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND r.role_code IN (
                  'SUPER_ADMINISTRATOR',
                  'ADMINISTRATOR',
                  'PROJECT_TEAM_COORDINATOR',
                  'PROJECT_MANAGER',
                  'PROJECT_MANAGEMENT',
                  'PROJECT_MANAGEMENT_LEAD',
                  'PROJECT_MANAGEMENT_TEAM_LEAD',
                  'PM_TEAM_LEAD'
              )
        );
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        canEditWorkRegister = Convert.ToBoolean(await accessCommand.ExecuteScalarAsync() ?? false);
    }

    if (!canEditWorkRegister)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Only the assigned Project Manager can upload documents for this project. Project Team Coordinators, Administrators, and Super Administrators can upload documents for every project."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var workRegisterDocumentId = Guid.NewGuid();
    var uploadRoot = GetProjectPulseUploadRoot();
    var projectFolder = System.IO.Path.Combine(uploadRoot, "work-register-documents", projectId.Value.ToString("N"));
    System.IO.Directory.CreateDirectory(projectFolder);

    var storedFileName = $"{workRegisterDocumentId:N}_{originalFileName}";
    var storedFilePath = System.IO.Path.Combine(projectFolder, storedFileName);
    var downloadReference = $"/api/work-register/projects/documents/{workRegisterDocumentId}/download";

    await using (var fileStream = System.IO.File.Create(storedFilePath))
    {
        await file.CopyToAsync(fileStream);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    var newSnapshot = JsonSerializer.Serialize(new
    {
        projectId = projectId.Value,
        workRegisterDocumentId,
        documentName,
        documentType,
        versionLabel,
        visibility,
        effectiveDate,
        notes,
        reason,
        uploadSource = "local_file",
        originalFileName,
        storedFilePath,
        file.ContentType,
        file.Length,
        downloadReference
    });

    await using (var insertCommand = new NpgsqlCommand("""
        INSERT INTO work_register_documents (
            work_register_document_id,
            project_id,
            document_name,
            document_type,
            document_reference,
            version_label,
            status,
            visibility,
            effective_date,
            notes,
            created_by_user_id,
            upload_source,
            original_file_name,
            stored_file_path,
            content_type,
            file_size_bytes
        )
        VALUES (
            @document_id,
            @project_id,
            @document_name,
            @document_type,
            @document_reference,
            @version_label,
            'active',
            @visibility,
            @effective_date,
            @notes,
            @created_by_user_id,
            'local_file',
            @original_file_name,
            @stored_file_path,
            @content_type,
            @file_size_bytes
        );
        """, connection, transaction))
    {
        insertCommand.Parameters.AddWithValue("document_id", workRegisterDocumentId);
        insertCommand.Parameters.AddWithValue("project_id", projectId.Value);
        insertCommand.Parameters.AddWithValue("document_name", documentName);
        insertCommand.Parameters.AddWithValue("document_type", documentType);
        insertCommand.Parameters.AddWithValue("document_reference", downloadReference);
        insertCommand.Parameters.AddWithValue("version_label", versionLabel);
        insertCommand.Parameters.AddWithValue("visibility", visibility);
        insertCommand.Parameters.Add("effective_date", NpgsqlTypes.NpgsqlDbType.Date).Value = effectiveDate is null ? DBNull.Value : effectiveDate.Value;
        insertCommand.Parameters.AddWithValue("notes", notes);
        insertCommand.Parameters.AddWithValue("created_by_user_id", sessionUserId.Value);
        insertCommand.Parameters.AddWithValue("original_file_name", originalFileName);
        insertCommand.Parameters.AddWithValue("stored_file_path", storedFilePath);
        insertCommand.Parameters.AddWithValue("content_type", string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        insertCommand.Parameters.AddWithValue("file_size_bytes", file.Length);
        await insertCommand.ExecuteNonQueryAsync();
    }

    await using (var auditCommand = new NpgsqlCommand("""
        INSERT INTO work_register_change_history (
            work_register_change_history_id,
            source_table,
            work_id,
            action,
            change_summary,
            changed_fields_csv,
            changed_by_user_id,
            old_value_json,
            new_value_json
        )
        VALUES (
            @history_id,
            'projects',
            @work_id,
            'document_uploaded',
            @change_summary,
            @changed_fields_csv,
            @changed_by_user_id,
            '{}'::jsonb,
            CAST(@new_value_json AS jsonb)
        );
        """, connection, transaction))
    {
        auditCommand.Parameters.AddWithValue("history_id", Guid.NewGuid());
        auditCommand.Parameters.AddWithValue("work_id", projectId.Value);
        auditCommand.Parameters.AddWithValue("change_summary", $"{documentType}: {documentName} uploaded");
        auditCommand.Parameters.AddWithValue("changed_fields_csv", "Local File Upload, Document Name, Document Type, Version, Visibility, File Size");
        auditCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        auditCommand.Parameters.AddWithValue("new_value_json", newSnapshot);
        await auditCommand.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "document_uploaded",
        projectId = projectId.Value,
        workRegisterDocumentId,
        documentName,
        originalFileName,
        fileSizeBytes = file.Length,
        downloadReference,
        message = "Local document uploaded and audit history saved."
    });
}).DisableAntiforgery();

app.MapGet("/api/work-register/projects/documents/{documentId:guid}/download", async (Guid documentId, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055C10_document_download_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var dbConfig = DatabaseConfig.FromEnvironment();

    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand("""
        SELECT
            COALESCE(stored_file_path, ''),
            COALESCE(original_file_name, document_name, 'document'),
            COALESCE(content_type, 'application/octet-stream')
        FROM work_register_documents
        WHERE work_register_document_id = @document_id
          AND status = 'active'
        LIMIT 1;
        """, connection);

    command.Parameters.AddWithValue("document_id", documentId);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.NotFound(new { status = "document_not_found", message = "Document was not found or is archived." });
    }

    var storedFilePath = reader.GetString(0);
    var originalFileName = reader.GetString(1);
    var contentType = reader.GetString(2);

    if (string.IsNullOrWhiteSpace(storedFilePath) || !System.IO.File.Exists(storedFilePath))
    {
        return Results.NotFound(new { status = "stored_file_not_found", message = "The stored document file could not be found on the server." });
    }

    return Results.File(
        path: storedFilePath,
        contentType: string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
        fileDownloadName: string.IsNullOrWhiteSpace(originalFileName) ? "document" : originalFileName,
        enableRangeProcessing: true
    );
});
/* 055C_10_LOCAL_DOCUMENT_UPLOAD_API_END */


/* 055C_9_DOCUMENT_MANAGEMENT_API_START */
app.MapPost("/api/work-register/projects/documents/save", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055C9_document_save_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body);
    var root = document.RootElement;

    string ReadString(string name, string fallback = "")
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? (value.GetString() ?? fallback).Trim()
            : fallback;
    }

    Guid? ReadGuid(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    DateTime? ReadNullableDate(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsed))
        {
            return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        }

        return null;
    }

    var projectId = ReadGuid("projectId");
    var documentName = ReadString("documentName");
    var documentType = ReadString("documentType", "Other");
    var documentReference = ReadString("documentReference");
    var versionLabel = ReadString("versionLabel");
    var visibility = ReadString("visibility", "project_team");
    var relatedChangeOrderId = ReadGuid("relatedChangeOrderId");
    var effectiveDate = ReadNullableDate("effectiveDate");
    var notes = ReadString("notes");
    var reason = ReadString("reason");

    if (projectId is null)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Project ID is required." });
    }

    if (string.IsNullOrWhiteSpace(documentName))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Document name is required." });
    }

    if (string.IsNullOrWhiteSpace(documentType))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Document type is required." });
    }

    if (string.IsNullOrWhiteSpace(reason))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Reason is required for document audit history." });
    }

    var dbConfig = DatabaseConfig.FromEnvironment();

    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    var canEditWorkRegister = false;
    await using (var accessCommand = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND r.role_code IN (
                  'SUPER_ADMINISTRATOR',
                  'ADMINISTRATOR',
                  'PROJECT_TEAM_COORDINATOR',
                  'PROJECT_MANAGER',
                  'PROJECT_MANAGEMENT',
                  'PROJECT_MANAGEMENT_LEAD',
                  'PROJECT_MANAGEMENT_TEAM_LEAD',
                  'PM_TEAM_LEAD'
              )
        );
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        canEditWorkRegister = Convert.ToBoolean(await accessCommand.ExecuteScalarAsync() ?? false);
    }

    if (!canEditWorkRegister)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Only the assigned Project Manager can manage documents for this project. Project Team Coordinators, Administrators, and Super Administrators can manage documents for every project."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    var workRegisterDocumentId = Guid.NewGuid();

    var newSnapshot = JsonSerializer.Serialize(new
    {
        projectId = projectId.Value,
        workRegisterDocumentId,
        documentName,
        documentType,
        documentReference,
        versionLabel,
        visibility,
        relatedChangeOrderId,
        effectiveDate,
        notes,
        reason
    });

    await using (var insertCommand = new NpgsqlCommand("""
        INSERT INTO work_register_documents (
            work_register_document_id,
            project_id,
            document_name,
            document_type,
            document_reference,
            version_label,
            status,
            visibility,
            related_change_order_id,
            effective_date,
            notes,
            created_by_user_id
        )
        VALUES (
            @document_id,
            @project_id,
            @document_name,
            @document_type,
            @document_reference,
            @version_label,
            'active',
            @visibility,
            @related_change_order_id,
            @effective_date,
            @notes,
            @created_by_user_id
        );
        """, connection, transaction))
    {
        insertCommand.Parameters.AddWithValue("document_id", workRegisterDocumentId);
        insertCommand.Parameters.AddWithValue("project_id", projectId.Value);
        insertCommand.Parameters.AddWithValue("document_name", documentName);
        insertCommand.Parameters.AddWithValue("document_type", documentType);
        insertCommand.Parameters.AddWithValue("document_reference", documentReference);
        insertCommand.Parameters.AddWithValue("version_label", versionLabel);
        insertCommand.Parameters.AddWithValue("visibility", visibility);
        insertCommand.Parameters.Add("related_change_order_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value = relatedChangeOrderId is null ? DBNull.Value : relatedChangeOrderId.Value;
        insertCommand.Parameters.Add("effective_date", NpgsqlTypes.NpgsqlDbType.Date).Value = effectiveDate is null ? DBNull.Value : effectiveDate.Value;
        insertCommand.Parameters.AddWithValue("notes", notes);
        insertCommand.Parameters.AddWithValue("created_by_user_id", sessionUserId.Value);
        await insertCommand.ExecuteNonQueryAsync();
    }

    await using (var auditCommand = new NpgsqlCommand("""
        INSERT INTO work_register_change_history (
            work_register_change_history_id,
            source_table,
            work_id,
            action,
            change_summary,
            changed_fields_csv,
            changed_by_user_id,
            old_value_json,
            new_value_json
        )
        VALUES (
            @history_id,
            'projects',
            @work_id,
            'document_registered',
            @change_summary,
            @changed_fields_csv,
            @changed_by_user_id,
            '{}'::jsonb,
            CAST(@new_value_json AS jsonb)
        );
        """, connection, transaction))
    {
        auditCommand.Parameters.AddWithValue("history_id", Guid.NewGuid());
        auditCommand.Parameters.AddWithValue("work_id", projectId.Value);
        auditCommand.Parameters.AddWithValue("change_summary", $"{documentType}: {documentName}");
        auditCommand.Parameters.AddWithValue("changed_fields_csv", "Document Name, Document Type, Reference, Version, Visibility, Notes");
        auditCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        auditCommand.Parameters.AddWithValue("new_value_json", newSnapshot);
        await auditCommand.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "document_registered",
        projectId = projectId.Value,
        workRegisterDocumentId,
        message = "Document registered and audit history saved."
    });
});

app.MapPost("/api/work-register/projects/documents/archive", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055C9_document_archive_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body);
    var root = document.RootElement;

    string ReadString(string name, string fallback = "")
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? (value.GetString() ?? fallback).Trim()
            : fallback;
    }

    Guid? ReadGuid(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    var projectId = ReadGuid("projectId");
    var documentId = ReadGuid("documentId");
    var reason = ReadString("reason");

    if (projectId is null || documentId is null)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Project ID and document ID are required." });
    }

    if (string.IsNullOrWhiteSpace(reason))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Archive reason is required." });
    }

    var dbConfig = DatabaseConfig.FromEnvironment();

    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    var canEditWorkRegister = false;
    await using (var accessCommand = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND r.role_code IN (
                  'SUPER_ADMINISTRATOR',
                  'ADMINISTRATOR',
                  'PROJECT_TEAM_COORDINATOR',
                  'PROJECT_MANAGER',
                  'PROJECT_MANAGEMENT',
                  'PROJECT_MANAGEMENT_LEAD',
                  'PROJECT_MANAGEMENT_TEAM_LEAD',
                  'PM_TEAM_LEAD'
              )
        );
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        canEditWorkRegister = Convert.ToBoolean(await accessCommand.ExecuteScalarAsync() ?? false);
    }

    if (!canEditWorkRegister)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Only the assigned Project Manager can archive documents for this project. Project Team Coordinators, Administrators, and Super Administrators can archive documents for every project."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    var oldSnapshot = "{}";
    await using (var oldCommand = new NpgsqlCommand("""
        SELECT COALESCE(to_jsonb(d), '{}'::jsonb)::text
        FROM work_register_documents d
        WHERE d.work_register_document_id = @document_id
          AND d.project_id = @project_id
        LIMIT 1;
        """, connection, transaction))
    {
        oldCommand.Parameters.AddWithValue("document_id", documentId.Value);
        oldCommand.Parameters.AddWithValue("project_id", projectId.Value);
        oldSnapshot = Convert.ToString(await oldCommand.ExecuteScalarAsync() ?? "{}") ?? "{}";
    }

    await using (var updateCommand = new NpgsqlCommand("""
        UPDATE work_register_documents
        SET status = 'archived',
            archived_by_user_id = @archived_by_user_id,
            archived_at = NOW(),
            archive_reason = @archive_reason
        WHERE work_register_document_id = @document_id
          AND project_id = @project_id
          AND status = 'active';
        """, connection, transaction))
    {
        updateCommand.Parameters.AddWithValue("document_id", documentId.Value);
        updateCommand.Parameters.AddWithValue("project_id", projectId.Value);
        updateCommand.Parameters.AddWithValue("archived_by_user_id", sessionUserId.Value);
        updateCommand.Parameters.AddWithValue("archive_reason", reason);

        var affected = await updateCommand.ExecuteNonQueryAsync();
        if (affected == 0)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "document_not_found_or_already_archived", message = "The document was not found or is already archived." });
        }
    }

    var newSnapshot = JsonSerializer.Serialize(new
    {
        projectId = projectId.Value,
        documentId = documentId.Value,
        status = "archived",
        reason
    });

    await using (var auditCommand = new NpgsqlCommand("""
        INSERT INTO work_register_change_history (
            work_register_change_history_id,
            source_table,
            work_id,
            action,
            change_summary,
            changed_fields_csv,
            changed_by_user_id,
            old_value_json,
            new_value_json
        )
        VALUES (
            @history_id,
            'projects',
            @work_id,
            'document_archived',
            @change_summary,
            @changed_fields_csv,
            @changed_by_user_id,
            CAST(@old_value_json AS jsonb),
            CAST(@new_value_json AS jsonb)
        );
        """, connection, transaction))
    {
        auditCommand.Parameters.AddWithValue("history_id", Guid.NewGuid());
        auditCommand.Parameters.AddWithValue("work_id", projectId.Value);
        auditCommand.Parameters.AddWithValue("change_summary", reason);
        auditCommand.Parameters.AddWithValue("changed_fields_csv", "Document Status, Archive Reason");
        auditCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        auditCommand.Parameters.AddWithValue("old_value_json", oldSnapshot);
        auditCommand.Parameters.AddWithValue("new_value_json", newSnapshot);
        await auditCommand.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "document_archived",
        projectId = projectId.Value,
        documentId = documentId.Value,
        message = "Document archived and audit history saved."
    });
});
/* 055C_9_DOCUMENT_MANAGEMENT_API_END */


/* 055C_8_CHANGE_ORDER_COSTING_API_START */
app.MapPost("/api/work-register/projects/change-orders/save", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055C8_change_order_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body);
    var root = document.RootElement;

    string ReadString(string name, string fallback = "")
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? (value.GetString() ?? fallback).Trim()
            : fallback;
    }

    Guid? ReadGuid(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    DateTime ReadDate(string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return DateTime.UtcNow.Date;
        }

        if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsed))
        {
            return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        }

        return DateTime.UtcNow.Date;
    }

    static decimal ReadDecimalFrom(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return 0m;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsedNumber))
        {
            return parsedNumber;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsedString))
        {
            return parsedString;
        }

        return 0m;
    }

    static bool ReadBoolFrom(JsonElement parent, string name, bool fallback)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    static string ReadStringFrom(JsonElement parent, string name, string fallback = "")
    {
        return parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? (value.GetString() ?? fallback).Trim()
            : fallback;
    }

    var projectId = ReadGuid("projectId");
    var changeOrderNumber = ReadString("changeOrderNumber");
    var title = ReadString("title", "Change Order");
    var statusValue = ReadString("status", "approved");
    var changeOrderDate = ReadDate("changeOrderDate");
    var approvalReference = ReadString("approvalReference");
    var reason = ReadString("reason");

    if (projectId is null)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Project ID is required." });
    }

    if (string.IsNullOrWhiteSpace(title))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Change order title is required." });
    }

    if (string.IsNullOrWhiteSpace(reason))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Reason is required for change order audit history." });
    }

    if (!root.TryGetProperty("lines", out var linesElement) || linesElement.ValueKind != JsonValueKind.Array)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Change order cost lines are required." });
    }

    var lines = new List<(string LineType, string Description, decimal Quantity, decimal UnitRate, decimal Amount, bool Billable, bool UtilizationEligible)>();

    foreach (var line in linesElement.EnumerateArray())
    {
        var lineType = ReadStringFrom(line, "lineType", "other");
        var description = ReadStringFrom(line, "description", lineType);
        var quantity = ReadDecimalFrom(line, "quantity");
        var unitRate = ReadDecimalFrom(line, "unitRate");
        var amount = ReadDecimalFrom(line, "amount");

        if (amount <= 0m && quantity > 0m && unitRate > 0m)
        {
            amount = Math.Round(quantity * unitRate, 2);
        }

        if (amount <= 0m)
        {
            continue;
        }

        lines.Add((
            lineType,
            description,
            quantity,
            unitRate,
            amount,
            ReadBoolFrom(line, "billable", true),
            ReadBoolFrom(line, "utilizationEligible", true)
        ));
    }

    if (lines.Count == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "At least one change order cost line must have an amount greater than zero." });
    }

    if (lines.Count > 50)
    {
        return Results.BadRequest(new { status = "maximum_lines_exceeded", message = "A change order can contain a maximum of 50 cost lines." });
    }

    var totalAmount = lines.Sum(line => line.Amount);

    var dbConfig = DatabaseConfig.FromEnvironment();

    await using var connection = new NpgsqlConnection(dbConfig.ConnectionString);
    await connection.OpenAsync();

    var canEditWorkRegister = false;
    await using (var accessCommand = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND r.role_code IN (
                  'SUPER_ADMINISTRATOR',
                  'ADMINISTRATOR',
                  'PROJECT_TEAM_COORDINATOR',
                  'PROJECT_MANAGER',
                  'PROJECT_MANAGEMENT',
                  'PROJECT_MANAGEMENT_LEAD',
                  'PROJECT_MANAGEMENT_TEAM_LEAD',
                  'PM_TEAM_LEAD'
              )
        );
        """, connection))
    {
        accessCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        canEditWorkRegister = Convert.ToBoolean(await accessCommand.ExecuteScalarAsync() ?? false);
    }

    if (!canEditWorkRegister)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Only the assigned Project Manager can add change orders for this project. Project Team Coordinators, Administrators, and Super Administrators can add change orders for every project."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    var changeOrderId = Guid.NewGuid();

    var newSnapshot = JsonSerializer.Serialize(new
    {
        projectId = projectId.Value,
        changeOrderNumber,
        title,
        status = statusValue,
        changeOrderDate,
        approvalReference,
        reason,
        totalAmount,
        lines = lines.Select(line => new
        {
            line.LineType,
            line.Description,
            line.Quantity,
            line.UnitRate,
            line.Amount,
            line.Billable,
            line.UtilizationEligible
        }).ToList()
    });

    await using (var orderCommand = new NpgsqlCommand("""
        INSERT INTO work_register_change_orders (
            work_register_change_order_id,
            project_id,
            change_order_number,
            title,
            status,
            change_order_date,
            approval_reference,
            reason,
            total_amount,
            created_by_user_id
        )
        VALUES (
            @change_order_id,
            @project_id,
            @change_order_number,
            @title,
            @status,
            @change_order_date,
            @approval_reference,
            @reason,
            @total_amount,
            @created_by_user_id
        );
        """, connection, transaction))
    {
        orderCommand.Parameters.AddWithValue("change_order_id", changeOrderId);
        orderCommand.Parameters.AddWithValue("project_id", projectId.Value);
        orderCommand.Parameters.AddWithValue("change_order_number", changeOrderNumber);
        orderCommand.Parameters.AddWithValue("title", title);
        orderCommand.Parameters.AddWithValue("status", statusValue);
        orderCommand.Parameters.Add("change_order_date", NpgsqlTypes.NpgsqlDbType.Date).Value = changeOrderDate;
        orderCommand.Parameters.AddWithValue("approval_reference", approvalReference);
        orderCommand.Parameters.AddWithValue("reason", reason);
        orderCommand.Parameters.AddWithValue("total_amount", totalAmount);
        orderCommand.Parameters.AddWithValue("created_by_user_id", sessionUserId.Value);
        await orderCommand.ExecuteNonQueryAsync();
    }

    foreach (var line in lines)
    {
        await using var lineCommand = new NpgsqlCommand("""
            INSERT INTO work_register_change_order_lines (
                work_register_change_order_line_id,
                work_register_change_order_id,
                project_id,
                line_type,
                description,
                quantity,
                unit_rate,
                amount,
                billable,
                utilization_eligible
            )
            VALUES (
                @line_id,
                @change_order_id,
                @project_id,
                @line_type,
                @description,
                @quantity,
                @unit_rate,
                @amount,
                @billable,
                @utilization_eligible
            );
            """, connection, transaction);

        lineCommand.Parameters.AddWithValue("line_id", Guid.NewGuid());
        lineCommand.Parameters.AddWithValue("change_order_id", changeOrderId);
        lineCommand.Parameters.AddWithValue("project_id", projectId.Value);
        lineCommand.Parameters.AddWithValue("line_type", line.LineType);
        lineCommand.Parameters.AddWithValue("description", line.Description);
        lineCommand.Parameters.AddWithValue("quantity", line.Quantity);
        lineCommand.Parameters.AddWithValue("unit_rate", line.UnitRate);
        lineCommand.Parameters.AddWithValue("amount", line.Amount);
        lineCommand.Parameters.AddWithValue("billable", line.Billable);
        lineCommand.Parameters.AddWithValue("utilization_eligible", line.UtilizationEligible);
        await lineCommand.ExecuteNonQueryAsync();
    }

    await using (var auditCommand = new NpgsqlCommand("""
        INSERT INTO work_register_change_history (
            work_register_change_history_id,
            source_table,
            work_id,
            action,
            change_summary,
            changed_fields_csv,
            changed_by_user_id,
            old_value_json,
            new_value_json
        )
        VALUES (
            @history_id,
            'projects',
            @work_id,
            'change_order_added',
            @change_summary,
            @changed_fields_csv,
            @changed_by_user_id,
            '{}'::jsonb,
            CAST(@new_value_json AS jsonb)
        );
        """, connection, transaction))
    {
        auditCommand.Parameters.AddWithValue("history_id", Guid.NewGuid());
        auditCommand.Parameters.AddWithValue("work_id", projectId.Value);
        auditCommand.Parameters.AddWithValue("change_summary", $"{title} - {totalAmount:C}");
        auditCommand.Parameters.AddWithValue("changed_fields_csv", "Change Order, PM Cost, Engineering Cost, Travel, Materials, Total Amount");
        auditCommand.Parameters.AddWithValue("changed_by_user_id", sessionUserId.Value);
        auditCommand.Parameters.AddWithValue("new_value_json", newSnapshot);
        await auditCommand.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();

    return Results.Ok(new
    {
        status = "change_order_saved",
        projectId = projectId.Value,
        changeOrderId,
        totalAmount,
        message = $"Change order saved for {totalAmount:C}."
    });
});
/* 055C_8_CHANGE_ORDER_COSTING_API_END */


/* 055C_7_MULTI_ENGINEER_ROSTER_API_START */
app.MapPost("/api/work-register/tasks/assignments/roster/save", async (HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token.", guard = "055C7_multi_engineer_roster_session_required" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    using var document = await JsonDocument.ParseAsync(httpContext.Request.Body);
    var root = document.RootElement;

    string ReadString(string name, string fallback = "")
    {
        return root.TryGetProperty(name, ou×O6ß½›Ê×¬¢h­µçY\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[]Y]	‰ˆXXØÙ\ÜËØ[‘^Ü	‰ˆXXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™È	‰ˆXXØÙ\ÜËØ[•šY]Ð[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH“ØÚÙY\š[Ù]šY[˜ÙH\È™\ÝšXÝYÈ]Y]Ù^ÜØXØÛÝ[[™È›Û\ËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆÙ^HH]SÛ›K‘œ›ÛQ]U[YJ]U[YK•]Ó›ÝË‘]JNÂˆ˜\ˆÝ\HÙYZÔÝ\ÏÈÙ^KY^\ÊJ[
]Ù^K‘^SÙ•ÙYZÊNÂˆ˜\ˆ[™HÙYZÑ[™ÏÈÝ\Y^\ÊŠNÂ‚ˆ˜\ˆØÚÙY][\ÈH™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆK[YWÙ[žWÚYˆKÛÜš×Ù]KˆÓÐSTÐÑJ[\ÞYYK™\Ü^WÛ˜[YK[\ÞYYK™[XZ[	Õ[šÛ›ÝÛˆ\Ù\‰ÊHTÈ[\ÞYYWÛ˜[YKˆÓÐSTÐÑJœ›Ú™XÝØÛÙK	Ó›È›Ú™XÝ	ÊHTÈ›Ú™XÝØÛÙKˆÓÐSTÐÑJ\Ú×ØÛÙK	Ó›È\ÚÉÊHTÈ\Ú×ØÛÙKˆKšÝ\œËˆKœÝ]\Âˆ”“ÓH[YWÙ[šY\ÈBˆ“ÒSˆ\Ý\Ù\œÈ[\ÞYYBˆÓˆ[\ÞYYK\Ù\—ÚYHK\Ù\—ÚYˆQ•“ÒSˆ›Ú™XÝÈˆÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚYˆQ•“ÒSˆ›Ú™XÝÝ\ÚÜÈˆÓˆ\Ú×ÚYHK\Ú×ÚYˆÒT‘HKÛÜš×Ù]H‘UÑQSˆÙYZ×ÜÝ\S‘ÙYZ×Ù[™ˆS‘KœÝ]\ÈSˆ
	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊBˆÔ‘Tˆ–HKÛÜš×Ù]K[\ÞYYK™\Ü^WÛ˜[YKœ›Ú™XÝØÛÙK\Ú×ØÛÙNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹Ý\
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹[™
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆØÚÙY][\ËY
™]ÂˆÂˆ[YQ[žRYH™XY\‹‘Ù]ÝZY

KˆÛÜšÑ]HH™XY\‹‘Ù]šY[˜[YO]SÛ›OŠJKˆ[\ÞYYS˜[YHH™XY\‹‘Ù]Ýš[™ÊŠKˆ›Ú™XÝÛÙHH™XY\‹‘Ù]Ýš[™ÊÊKˆ\ÚÐÛÙHH™XY\‹‘Ù]Ýš[™Ê
KˆÝ\œÈH™XY\‹‘Ù]XÚ[X[
JKˆÝ]\ÈH™XY\‹‘Ù]Ýš[™ÊŠBˆJNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP‘ˆØÚÙY\š[Ù]Y]]šY[˜ÙH‹ˆ]T˜[™ÙHH™]ÈÈÙYZÔÝ\HÝ\ÙYZÑ[™H[™KˆÝ[[X\žHH™]ÂˆÂˆØÚÙYÜ”™XÛÛ˜Ú[Y][PÛÝ[HØÚÙY][\ËÛÝ[ˆØÚÙYÜ”™XÛÛ˜Ú[YÝ\œÈHØÚÙY][\Ë”Ý[J][HOˆ
XÚ[X[
Z][K‘Ù]\J
K‘Ù]›Ü\JšÝ\œÈŠHK‘Ù]˜[YJ][JHJBˆKˆØÚÙY][\ÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÜÙXÝ\š]KÜ›ÛKXXØÙ\ÜË[X]š^‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[•šY]Ð[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH”›ÛHXØÙ\ÜÈX]š^\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™Þ\Ý[HÜ\˜]ÜœËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆX]š^H™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ‹œ›ÛWØÛÙKˆ‹œ›ÛWÛ˜[YKˆÓÕS•
TÕSÕK\Ù\—ÚY
NŽ˜šYÚ[TÈ\ÜÚYÛ™YÝ\Ù\—ØÛÝ[ˆÓÕS•
TÕSÕœ\›Z\ÜÚ[Û—ØÛÙJNŽ˜šYÚ[TÈ\›Z\ÜÚ[Û—ØÛÝ[ˆÓÐSTÐÑJÕ’S‘×ÐQÑÊTÕSÕœ\›Z\ÜÚ[Û—ØÛÙK	Ë	ÈÔ‘Tˆ–Hœ\›Z\ÜÚ[Û—ØÛÙJK	ÉÊHTÈ\›Z\ÜÚ[ÛœÂˆ”“ÓH\Ü›Û\È‚ˆQ•“ÒSˆ\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜BˆÓˆ\˜K˜\Ü›ÛWÚYH‹˜\Ü›ÛWÚYˆS‘\˜Kš\×ØXÝ]™HH•QBˆQ•“ÒSˆ\Ý\Ù\œÈBˆÓˆK\Ù\—ÚYH\˜K\Ù\—ÚYˆS‘Kš\×ØXÝ]™HH•QBˆQ•“ÒSˆ\Ü›ÛWÜ\›Z\ÜÚ[ÛœÈœˆÓˆœ˜\Ü›ÛWÚYH‹˜\Ü›ÛWÚYˆQ•“ÒSˆ\Ü\›Z\ÜÚ[ÛœÈˆÓˆ˜\Ü\›Z\ÜÚ[Û—ÚYHœ˜\Ü\›Z\ÜÚ[Û—ÚYˆÒT‘H‹š\×ØXÝ]™HH•QBˆÔ“ÕT–H‹œ›ÛWØÛÙK‹œ›ÛWÛ˜[YBˆÔ‘Tˆ–H‹œ›ÛWØÛÙNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆX]š^Y
™]ÂˆÂˆ›ÛPÛÙHH™XY\‹‘Ù]Ýš[™Ê
Kˆ›ÛS˜[YHH™XY\‹‘Ù]Ýš[™ÊJKˆ\ÜÚYÛ™Y\Ù\ÛÝ[H™XY\‹‘Ù][
ŠKˆ\›Z\ÜÚ[ÛÛÝ[H™XY\‹‘Ù][
ÊKˆ\›Z\ÜÚ[ÛœÈH™XY\‹‘Ù]Ýš[™Ê
K”Ü]
‹‹Ýš[™ÔÜ]Ü[ÛœË”™[[Ý™Q[\Q[šY\ÊBˆJNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP‘È›ÛHXØÙ\ÜÈX]š^‹ˆÝ[[X\žHH™]ÂˆÂˆ›ÛPÛÝ[HX]š^ÛÝ[ˆ›ÝHH•\ÙYÈ™\šYžH\Ú›Ø\™Û[Ù[Hš\ÚXš[]H[™›ÛH[™›Ü˜Ù[Y[ˆ‚ˆKˆX]š^ˆJNÂŸJNÂ‚‚‹ËÈŒPÈ™[[Ý™YYØXÞH[[È™XY[™\ÜÈÛÛ[X[™XÙ[\ˆ›Ý]HY\ˆ›ÙXÝ[Ûˆ›Ý]HÝ[™\™^˜][Û‹‚‚˜\“X\Ù]
‹Ø\KÝÛÜšÙ›ÝËÝ˜[Y][Û‹\[\È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[•šY]È	‰ˆXXØÙ\ÜËØ[•šY]Ð[	‰ˆXXØÙ\ÜËØ[]Y]	‰ˆXXØÙ\ÜËØ[”›Ú™XÝ\›Ý™H	‰ˆXXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™È	‰ˆXXØÙ\ÜËØ[‘^Ü
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH•ÛÜšÙ›ÝÈ˜[Y][Ûˆ[\È\™H™\ÝšXÝYÈÛÜšÙ›ÝÈ›Û\ËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆÛ™È^Ü™XYQ[šY\ÈHÂˆÛ™È›ØÚÙY[šY\ÈHÂˆÛ™È^ÜXÚØYÙ\ÈHÂˆÛ™È]Y]]™[ÈHÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÙ[šY\ÈÒT‘HÝ]\ÈSˆ
	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊJKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÙ[šY\ÈÒT‘HÓÐSTÐÑJÝ]\Ë	Ù˜Y	ÊH“ÕSˆ
	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊJKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH]Y]ÛÙÜÈÒT‘HXÝ[ÛˆSRÑH	ÉY^Ü	IÈÔˆXÝ[ÛˆSRÑH	ÉX\›Ý˜[	IÈÔˆXÝ[ÛˆSRÑH	É\™XÛÛ˜Ú[IIÈÔˆXÝ[ÛˆSRÑH	É[ØÚÉIÊNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ^Ü™XYQ[šY\ÈH™XY\‹‘Ù][

NÂˆ›ØÚÙY[šY\ÈH™XY\‹‘Ù][
JNÂˆ^ÜXÚØYÙ\ÈH™XY\‹‘Ù][
ŠNÂˆ]Y]]™[ÈH™XY\‹‘Ù][
ÊNÂˆBˆB‚ˆ˜\ˆ[\ÈH™]È\ÝØš™XÝ‚ˆÂˆ™]ÂˆÂˆ[PÛÙHH™[™Ú[™Y\—Û›×Ù^ÜØÛÛ›ÛÈ‹ˆ]HH‘[™Ú[™Y\œÈÈ›Ý™XÙZ]™H^ÜÜˆÛÜšÙ›ÝÈX[˜YÙ[Y[ÛÛ›ÛÈ‹ˆÝ]\ÈH˜ÛÛ™šYÝ\™Y‹ˆ]šY[˜ÙHH‘^ÜÙÝÛ›ØY[™Ú[È™\]Z\™H^ÜY[˜X›Y›Û\È[™š[ÜˆÛ[ÚÙHÚXÚÜÈÛÛ™š\›YY[™Ú[™Y\ˆšY]ËP\ÈËˆ‚ˆKˆ™]ÂˆÂˆ[PÛÙHH™^ÜÛÛ›WÜ™XYWÜÝ]\Ù\È‹ˆ]HH‘^ÜXÚØYÙH[˜ÛY\ÈÛ›HXØÛÝ[[™Ë\™XYK™XÛÛ˜Ú[YÜˆØÚÙY[šY\È‹ˆÝ]\ÈH˜ÛÛ™šYÝ\™Y‹ˆ]šY[˜ÙHH	‘^Ü\™XYH[šY\ÎˆÙ^Ü™XYQ[šY\ßNÈ›ØÚÙY[šY\ÎˆØ›ØÚÙY[šY\ßKˆ‚ˆKˆ™]ÂˆÂˆ[PÛÙHH™ÝÛ›ØYØ]Y]Ù]šY[˜ÙH‹ˆ]HH‘^ÜXÚØYÙHÝÛ›ØYÜ™X]\È]Y]]šY[˜ÙH‹ˆÝ]\ÈH]Y]]™[ÈˆÈ™]šY[˜ÙWÜ™\Ù[ˆˆ›™YY×Ù]šY[˜ÙH‹ˆ]šY[˜ÙHH	•ÛÜšÙ›ÝËÙ^Ü]Y]]šY[˜ÙH]™[ÎˆØ]Y]]™[ßKˆ‚ˆKˆ™]ÂˆÂˆ[PÛÙHH™\Ú›Ø\™Ü™YÚ\ÝžWØÛÝ™\˜YÙH‹ˆ]HH“™]È[Ù[\È]\Ý\X\ˆ[ˆ\Ú›Ø\™Û[Ù[H™YÚ\ÝžH‹ˆÝ]\ÈH˜ÛÛ™šYÝ\™Y‹ˆ]šY[˜ÙHH‘\Ú›Ø\™[Ù[Hš\ÚXš[]H^XÝ][ÛœÈ\™HÙYYY[™\Ú›Ø\™™YÚ\ÝžH\È]ÚYžH\ÈÜš[ˆ‚ˆKˆ™]ÂˆÂˆ[PÛÙHHÛÜšÙ›Ý×Ü™Y›YÚÛ›Û—Ù\ÝXÝ]™H‹ˆ]HH•ÛÜšÙ›ÝÈ™Y›YÚ˜[Y][Ûˆ]\Ý›ÝÚ[™ÙHÛÜšÙ›ÝÈÝ]\È‹ˆÝ]\ÈH˜ÛÛ™šYÝ\™Y‹ˆ]šY[˜ÙHH•ÛÜšÙ›ÝÈ™Y›YÚ˜[Y][ÛˆÜš]\È]šY[˜ÙHÛ›H[™Ù\È›ÝÚ[™ÙH[YH[žHÝ]\Ëˆ‚ˆKˆ™]ÂˆÂˆ[PÛÙHH™^ÜÜXÚØYÙWØ]˜Z[X›H‹ˆ]HH‘^ÜXÚØYÙH™XÛÜ™È\™H]˜Z[X›H›ÜˆÝÛ›ØY™XY[™\ÜÈ‹ˆÝ]\ÈH^ÜXÚØYÙ\ÈˆÈ™]šY[˜ÙWÜ™\Ù[ˆˆ›™YY×Ù^ÜÜXÚØYÙH‹ˆ]šY[˜ÙHH	‘^ÜXÚØYÙH™XÛÜ™ÎˆÙ^ÜXÚØYÙ\ßKˆ‚ˆBˆNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP’HÛÜšÙ›ÝÈ˜[Y][Ûˆ[\È‹ˆÝ[[X\žHH™]ÂˆÂˆ[PÛÝ[H[\ËÛÝ[ˆÛÛ™šYÝ\™Y[PÛÝ[H[\ËÛÝ[
ˆOˆ

Ýš[™Ê\‹‘Ù]\J
K‘Ù]›Ü\JœÝ]\ÈŠHK‘Ù]˜[YJŠHJKÛÛZ[œÊ˜ÛÛ™šYÝ\™YŠH

Ýš[™Ê\‹‘Ù]\J
K‘Ù]›Ü\JœÝ]\ÈŠHK‘Ù]˜[YJŠHJKÛÛZ[œÊ™]šY[˜ÙWÜ™\Ù[ŠJBˆKˆ[\ÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÝÛÜšÙ›ÝËÛÜ\˜][ÛœËXÙ[\ˆ‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[•šY]È	‰ˆXXØÙ\ÜËØ[•šY]Ð[	‰ˆXXØÙ\ÜËØ[]Y]	‰ˆXXØÙ\ÜËØ[”›Ú™XÝ\›Ý™H	‰ˆXXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™È	‰ˆXXØÙ\ÜËØ[‘^Ü
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH•ÛÜšÙ›ÝÈÜ\˜][ÛœÈÙ[\ˆ\È™\ÝšXÝYÈÛÜšÙ›ÝÈ›Û\ËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆÛ™È^XÝ][ÛœÈHÂˆÛ™È^ÜÈHÂˆÛ™È]Y]]™[ÈHÂˆÛ™ÈžT[œÈHÂˆÛ™È\›Ý˜[][\ÈHÂˆÛ™È^Ü™XYQ[šY\ÈHÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH\Ú›Ø\™Û[Ù[WÝš\ÚXš[]WÙ^XÝ][ÛœÈÒT‘H\×ØXÝ]™HH•QJKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH]Y]ÛÙÜÈÒT‘HXÝ[ÛˆSRÑH	É][YIIÈÔˆXÝ[ÛˆSRÑH	ÉX\›Ý˜[	IÈÔˆXÝ[ÛˆSRÑH	ÉY^Ü	IÈÔˆXÝ[ÛˆSRÑH	É\™XÛÛ˜Ú[IIÈÔˆXÝ[ÛˆSRÑH	É[ØÚÉIÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓHÛÜšÙ›Ý×Ü™Y›YÚÝ˜[Y][Û—Ù]™[ÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÙ[šY\ÈÒT‘HÝ]\ÈSˆ
	ÜÝX›Z]Y	Ë	Ü[™[™×ÛX[˜YÙ\—Ø\›Ý˜[	Ë	ÛX[˜YÙ\—Ø\›Ý™Y	Ë	Ü›Ú™XÝØ\›Ý™Y	Ë	Ü›Ú™XÝÝ˜[Y]Y	Ë	ØXØÛÝ[[™×Ü™XYIÊJKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÙ[šY\ÈÒT‘HÝ]\ÈSˆ
	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊJNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ^XÝ][ÛœÈH™XY\‹‘Ù][

NÂˆ^ÜÈH™XY\‹‘Ù][
JNÂˆ]Y]]™[ÈH™XY\‹‘Ù][
ŠNÂˆžT[œÈH™XY\‹‘Ù][
ÊNÂˆ\›Ý˜[][\ÈH™XY\‹‘Ù][

NÂˆ^Ü™XYQ[šY\ÈH™XY\‹‘Ù][
JNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP’ˆÛÜšÙ›ÝÈÜ\˜][ÛœÈÙ[\ˆ‹ˆÝ[[X\žHH™]ÂˆÂˆ\Ú›Ø\™^XÝ][ÛÛÝ[H^XÝ][ÛœËˆ^ÜXÚØYÙPÛÝ[H^ÜËˆÛÜšÙ›ÝÐ]Y]]™[ÛÝ[H]Y]]™[Ëˆ™Y›YÚ]šY[˜ÙPÛÝ[HžT[œËˆXÝ]™UÛÜšÙ›ÝÒ][PÛÝ[H\›Ý˜[][\Ëˆ^Ü™XYQ[žPÛÝ[H^Ü™XYQ[šY\ËˆÜ\˜][Û˜[Ý]\ÈHœ™XYH‚ˆKˆXØÙ\ÜÈH™]ÂˆÂˆXØÙ\ÜËØ[•šY]ËˆXØÙ\ÜËØ[”›Ú™XÝ\›Ý™KˆXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™ËˆXØÙ\ÜËØ[‘^ÜˆXØÙ\ÜËØ[]Y]ˆXØÙ\ÜËØ[•šY]Ð[ˆKˆ[Ù[\ÈH™]Ö×BˆÂˆŒNSKPVˆ]Y]\ÝÜžH[™Ú[
ÈRH™\Z\ˆ‹ˆŒNSKPHÛÜšÙ›ÝÈ™Y›YÚ˜[Y][ÛˆÛÛ›ÛÈ‹ˆŒNSKPˆ\Ú›Ø\™[Ù[Hš\ÚXš[]H˜[Y][Ûˆ‹ˆŒNSKPÈ^ÜXÚØYÙH™XY[™\ÜÈÝ[[X\žH‹ˆŒNSKP‘^ÜXÚØYÙH]šY[˜ÙH]Z[‹ˆŒNSKP‘HXØÛÝ[[™È™XÛÛ˜Ú[X][ÛˆÛÜšØ™[˜Ú‹ˆŒNSKP‘ˆØÚÙY\š[Ù]Y]]šY[˜ÙH‹ˆŒNSKP‘È›ÛHXØÙ\ÜÈX]š^[™Ú[‹ˆŒNSKP’›ÙXÝ[Ûˆ™XY[™\ÜÈÛÛ[X[™Ù[\ˆ‹ˆŒNSKP’HÛÜšÙ›ÝÈ˜[Y][Ûˆ[\È‹ˆŒNSKP’ˆÛÜšÙ›ÝÈÜ\˜][ÛœÈÙ[\ˆ™YÚ\ÝžH‹ˆŒNSKP’È›ÙXÝ[Ûˆ˜[Y][ÛˆØÜš\‚ˆBˆJNÂŸJNÂ‚‚‚‹ËÈNSKP“›ÝYÚNSKP•H›ÙXÝ[Ûˆ\™[š[™ÈÜš[‚˜\“X\Ù]
‹Ø\KÝÛÜšÙ›ÝËÜ™Y›YÚ]˜[Y][Ûˆ‹\Þ[˜È
ÛÛ^ÛÛ^]SÛ›OÈÙYZÔÝ\]SÛ›OÈÙYZÑ[™
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[•šY]È	‰ˆXXØÙ\ÜËØ[”›Ú™XÝ\›Ý™H	‰ˆXXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™È	‰ˆXXØÙ\ÜËØ[‘^Ü	‰ˆXXØÙ\ÜËØ[•šY]Ð[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH•ÛÜšÙ›ÝÈ™Y›YÚ˜[Y][Ûˆ\È™\ÝšXÝYÈÛÜšÙ›ÝÈ›Û\ËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆÙ^HH]SÛ›K‘œ›ÛQ]U[YJ]U[YK•]Ó›ÝË‘]JNÂˆ˜\ˆÝ\HÙYZÔÝ\ÏÈÙ^KY^\ÊJ[
]Ù^K‘^SÙ•ÙYZÊNÂˆ˜\ˆ[™HÙYZÑ[™ÏÈÝ\Y^\ÊŠNÂ‚ˆÛ™ÈÝ[[šY\ÈHÂˆÛ™ÈZ\ÜÚ[™Ô›Ú™XÝÜ•\ÚÈHÂˆÛ™È^Ü™XYQ[šY\ÈHÂˆÛ™È›ØÚÙY[šY\ÈHÂˆXÚ[X[Ý[Ý\œÈHÂˆXÚ[X[^Ü™XYRÝ\œÈHÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÓÕS•

ŠNŽ˜šYÚ[ˆÓÐSTÐÑJÕSJÝ\œÊK
NŽ›[Y\šXËˆÓÕS•

ŠH’STˆ
ÒT‘H›Ú™XÝÚYTÈ•SÔˆ\Ú×ÚYTÈ•S
NŽ˜šYÚ[ˆÓÕS•

ŠH’STˆ
ÒT‘HÝ]\ÈSˆ
	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊJNŽ˜šYÚ[ˆÓÐSTÐÑJÕSJÝ\œÊH’STˆ
ÒT‘HÝ]\ÈSˆ
	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊJK
NŽ›[Y\šXËˆÓÕS•

ŠH’STˆ
ÒT‘HÓÐSTÐÑJÝ]\Ë	Ù˜Y	ÊH“ÕSˆ
	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊJNŽ˜šYÚ[ˆ”“ÓH[YWÙ[šY\ÂˆÒT‘HÛÜš×Ù]H‘UÑQSˆÙYZ×ÜÝ\S‘ÙYZ×Ù[™Âˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹Ý\
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹[™
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÝ[[šY\ÈH™XY\‹‘Ù][

NÂˆÝ[Ý\œÈH™XY\‹‘Ù]XÚ[X[
JNÂˆZ\ÜÚ[™Ô›Ú™XÝÜ•\ÚÈH™XY\‹‘Ù][
ŠNÂˆ^Ü™XYQ[šY\ÈH™XY\‹‘Ù][
ÊNÂˆ^Ü™XYRÝ\œÈH™XY\‹‘Ù]XÚ[X[

NÂˆ›ØÚÙY[šY\ÈH™XY\‹‘Ù][
JNÂˆBˆB‚ˆ˜\ˆÝ]\ÐXÚÙ]ÈH™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆXÚÙ]ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÓÐSTÐÑJÝ]\Ë	Ù˜Y	ÊHTÈÝ]\ËˆÓÕS•

ŠNŽ˜šYÚ[TÈ][WØÛÝ[ˆÓÐSTÐÑJÕSJÝ\œÊK
NŽ›[Y\šXÈTÈÝ[ÚÝ\œËˆÓÕS•

ŠH’STˆ
ÒT‘H›Ú™XÝÚYTÈ•SÔˆ\Ú×ÚYTÈ•S
NŽ˜šYÚ[TÈZ\ÜÚ[™×Û[š×ØÛÝ[ˆ”“ÓH[YWÙ[šY\ÂˆÒT‘HÛÜš×Ù]H‘UÑQSˆÙYZ×ÜÝ\S‘ÙYZ×Ù[™ˆÔ“ÕT–HÓÐSTÐÑJÝ]\Ë	Ù˜Y	ÊBˆÔ‘Tˆ–HÓÐSTÐÑJÝ]\Ë	Ù˜Y	ÊNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆXÚÙ]ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹Ý\
NÂˆXÚÙ]ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹[™
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]XÚÙ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÝ]\ÐXÚÙ]ËY
™]ÂˆÂˆÝ]\ÈH™XY\‹‘Ù]Ýš[™Ê
Kˆ][PÛÝ[H™XY\‹‘Ù][
JKˆÝ[Ý\œÈH™XY\‹‘Ù]XÚ[X[
ŠKˆZ\ÜÚ[™Ó[šÐÛÝ[H™XY\‹‘Ù][
ÊBˆJNÂˆBˆB‚ˆ˜\ˆ\ÜÝY\ÈH™]È\ÝØš™XÝŠ
NÂˆYˆ
Ý[[šY\ÈOH
BˆÂˆ\ÜÝY\ËY
™]ÂˆÂˆÙ]™\š]HHØ\›š[™È‹ˆ[PÛÙHH››×Ý[YWÙ[šY\È‹ˆY\ÜØYÙHH“›È[YH[šY\È^\Ý›ÜˆHÙ[XÝY\š[Ùˆ‹ˆ][PÛÝ[HˆJNÂˆB‚ˆYˆ
Z\ÜÚ[™Ô›Ú™XÝÜ•\ÚÈˆ
BˆÂˆ\ÜÝY\ËY
™]ÂˆÂˆÙ]™\š]HHØ\›š[™È‹ˆ[PÛÙHH›Z\ÜÚ[™×Ü›Ú™XÝÛÜ—Ý\Ú×Û[šÈ‹ˆY\ÜØYÙHH”ÛÛYH[YH[šY\È\™HZ\ÜÚ[™È›Ú™XÝÜˆ\ÚÈ[šÜÈ[™ÚÝ[™HÛÜœ™XÝY™Y›Ü™H›ÙXÝ[Ûˆ^Üˆ‹ˆ][PÛÝ[HZ\ÜÚ[™Ô›Ú™XÝÜ•\ÚÂˆJNÂˆB‚ˆYˆ
›ØÚÙY[šY\Èˆ
BˆÂˆ\ÜÝY\ËY
™]ÂˆÂˆÙ]™\š]HHš[™›È‹ˆ[PÛÙHH™[šY\×Û›ÝÙ^ÜÜ™XYH‹ˆY\ÜØYÙHH”ÛÛYH[šY\È\™H›ÝY]XØÛÝ[[™Ë\™XYK™XÛÛ˜Ú[YÜˆØÚÙYˆ‹ˆ][PÛÝ[H›ØÚÙY[šY\ÂˆJNÂˆB‚ˆ˜\ˆXÝ[ÛœÈH™]È\ÝØš™XÝ‚ˆÂˆ™]ÂˆÂˆXÝ[ÛÛÙHH›X[˜YÙ\—Ø\›Ý˜[Ü™]šY]È‹ˆ]HH“X[˜YÙ\ˆ\›Ý˜[™]šY]È‹ˆ[ÝÙYHXØÙ\ÜËØ[”›Ú™XÝ\›Ý™HXØÙ\ÜËØ[•šY]Ð[ˆ\ÝXÝ]™TÝ]PÚ[™ÙHH˜[ÙKˆ›ÙXÝ[Û”Ý]\ÈHœ™Y›YÚÛÛ›H‹ˆ›ÝHH”™]šY]ÜÈ[YÚXš[]H[™›ØÚÙ\œÈ™Y›Ü™H^\Ý[™È\›Ý˜[XÝ[ÛœÈ\™H\ÙYˆ‚ˆKˆ™]ÂˆÂˆXÝ[ÛÛÙHHœ›Ú™XÝÝ˜[Y][Û—Ü™]šY]È‹ˆ]HH”›Ú™XÝ˜[Y][Ûˆ™]šY]È‹ˆ[ÝÙYHXØÙ\ÜËØ[”›Ú™XÝ\›Ý™HXØÙ\ÜËØ[•šY]Ð[ˆ\ÝXÝ]™TÝ]PÚ[™ÙHH˜[ÙKˆ›ÙXÝ[Û”Ý]\ÈHœ™Y›YÚÛÛ›H‹ˆ›ÝHH•˜[Y]\ÈÚ]\ˆX[˜YÙ\‹X\›Ý™Y[YH\È™XYH›Üˆ›Ú™XÝ˜[Y][Û‹ˆ‚ˆKˆ™]ÂˆÂˆXÝ[ÛÛÙHH˜XØÛÝ[[™×Ü™XÛÛ˜Ú[X][Û—Ü™]šY]È‹ˆ]HHXØÛÝ[[™È™XÛÛ˜Ú[X][Ûˆ™]šY]È‹ˆ[ÝÙYHXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™ÈXØÙ\ÜËØ[•šY]Ð[ˆ\ÝXÝ]™TÝ]PÚ[™ÙHH˜[ÙKˆ›ÙXÝ[Û”Ý]\ÈHœ™Y›YÚÛÛ›H‹ˆ›ÝHH•˜[Y]\ÈXØÛÝ[[™È]Y]YH™XY[™\ÜÈÚ]Ý]Ú[™Ú[™ÈÝ]\Ëˆ‚ˆKˆ™]ÂˆÂˆXÝ[ÛÛÙHH™^ÜÜXÚØYÙWÜ™]šY]È‹ˆ]HH‘^ÜXÚØYÙH™]šY]È‹ˆ[ÝÙYHXØÙ\ÜËØ[‘^ÜXØÙ\ÜËØ[•šY]Ð[ˆ\ÝXÝ]™TÝ]PÚ[™ÙHH˜[ÙKˆ›ÙXÝ[Û”Ý]\ÈH˜XÝ]™H‹ˆ›ÝHH•˜[Y]\ÈXÚØYÙH™XY[™\ÜÈ™Y›Ü™H^ÜXÚØYÙHÝÛ›ØYˆ‚ˆBˆNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP“HÛÜšÙ›ÝÈ™Y›YÚ˜[Y][Ûˆ‹ˆ]T˜[™ÙHH™]ÈÈÙYZÔÝ\HÝ\ÙYZÑ[™H[™KˆÝ[[X\žHH™]ÂˆÂˆÝ[[šY\ËˆÝ[Ý\œËˆ^Ü™XYQ[šY\Ëˆ^Ü™XYRÝ\œËˆ›ØÚÙY[šY\ËˆZ\ÜÚ[™Ô›Ú™XÝÜ•\ÚËˆ\ÜÝYPÛÝ[H\ÜÝY\ËÛÝ[ˆ›ÙXÝ[Û”™XYQ›Ü‘^ÜH^Ü™XYQ[šY\Èˆ	‰ˆZ\ÜÚ[™Ô›Ú™XÝÜ•\ÚÈOHˆKˆXØÙ\ÜÈH™]ÂˆÂˆXØÙ\ÜËØ[•šY]ËˆXØÙ\ÜËØ[”›Ú™XÝ\›Ý™KˆXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™ËˆXØÙ\ÜËØ[‘^ÜˆXØÙ\ÜËØ[]Y]ˆXØÙ\ÜËØ[•šY]Ð[ˆKˆÝ]\ÐXÚÙ]Ëˆ\ÜÝY\ËˆXÝ[ÛœÂˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÝÛÜšÙ›ÝËÜ™Y›YÚ]˜[Y][Û‹Ü[ˆ‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™È	‰ˆXXØÙ\ÜËØ[‘^Ü	‰ˆXXØÙ\ÜËØ[•šY]Ð[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH•ÛÜšÙ›ÝÈ™Y›YÚ˜[Y][Ûˆ[ˆ\È™\ÝšXÝYÈXØÛÝ[[™ËÙ^ÜY[˜X›Y›ÙXÝ[Ûˆ›Û\ËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆÝš[™È™Y›YÚXÝ[ÛˆH˜XØÛÝ[[™×Ü™XÛÛ˜Ú[X][Û—Ü™]šY]ÈŽÂˆ]SÛ›OÈÙYZÔÝ\H[Âˆ]SÛ›OÈÙYZÑ[™H[Â‚ˆžBˆÂˆ\Ú[™È˜\ˆØÝ[Y[H]ØZ]œÛÛ‘ØÝ[Y[”\œÙP\Þ[˜ÊÛÛ^”™\]Y\Ý›ÙJNÂˆ˜\ˆ›ÛÝHØÝ[Y[”›ÛÝ[[Y[Â‚ˆYˆ
›ÛÝ•žQÙ]›Ü\J˜XÝ[Ûˆ‹Ý]˜\ˆXÝ[Û‘[[Y[
H	‰ˆXÝ[Û‘[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™”Ýš[™ÊBˆÂˆ™Y›YÚXÝ[ÛˆHXÝ[Û‘[[Y[‘Ù]Ýš[™Ê
HÏÈ™Y›YÚXÝ[ÛŽÂˆB‚ˆYˆ
›ÛÝ•žQÙ]›Ü\JÙYZÔÝ\‹Ý]˜\ˆÙYZÔÝ\[[Y[
H	‰ˆÙYZÔÝ\[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™”Ýš[™È	‰ˆ]SÛ›K•žT\œÙJÙYZÔÝ\[[Y[‘Ù]Ýš[™Ê
KÝ]˜\ˆ\œÙYÝ\
JBˆÂˆÙYZÔÝ\H\œÙYÝ\ÂˆB‚ˆYˆ
›ÛÝ•žQÙ]›Ü\JÙYZÑ[™‹Ý]˜\ˆÙYZÑ[™[[Y[
H	‰ˆÙYZÑ[™[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™”Ýš[™È	‰ˆ]SÛ›K•žT\œÙJÙYZÑ[™[[Y[‘Ù]Ýš[™Ê
KÝ]˜\ˆ\œÙY[™
JBˆÂˆÙYZÑ[™H\œÙY[™ÂˆBˆBˆØ]ÚˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHš[˜[YÜ™\]Y\Ý‹Y\ÜØYÙHH”™Y›YÚ˜[Y][Ûˆ™\]Y\Ý›ÙH]\Ý™H˜[Y”ÓÓ‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆ˜\ˆÙ^HH]SÛ›K‘œ›ÛQ]U[YJ]U[YK•]Ó›ÝË‘]JNÂˆ˜\ˆÝ\HÙYZÔÝ\ÏÈÙ^KY^\ÊJ[
]Ù^K‘^SÙ•ÙYZÊNÂˆ˜\ˆ[™HÙYZÑ[™ÏÈÝ\Y^\ÊŠNÂ‚ˆÝš[™Ö×H[YÚX›TÝ]\Ù\ÈH™Y›YÚXÝ[ÛˆÝÚ]ÚˆÂˆ›X[˜YÙ\—Ø\›Ý˜[Ü™]šY]ÈˆOˆ™]Ö×HÈœÝX›Z]Y‹œ[™[™×ÛX[˜YÙ\—Ø\›Ý˜[ˆKˆœ›Ú™XÝÝ˜[Y][Û—Ü™]šY]ÈˆOˆ™]Ö×HÈ›X[˜YÙ\—Ø\›Ý™YˆKˆ˜XØÛÝ[[™×Ü™XÛÛ˜Ú[X][Û—Ü™]šY]ÈˆOˆ™]Ö×HÈœ›Ú™XÝØ\›Ý™Y‹œ›Ú™XÝÝ˜[Y]Y‹˜XØÛÝ[[™×Ü™XYHˆKˆœ\š[ÙÛØÚ×Ü™]šY]ÈˆOˆ™]Ö×HÈœ™XÛÛ˜Ú[YˆKˆ™^ÜÜXÚØYÙWÜ™]šY]ÈˆOˆ™]Ö×HÈ˜XØÛÝ[[™×Ü™XYH‹œ™XÛÛ˜Ú[Y‹›ØÚÙYˆKˆÈOˆ™]Ö×HÈœ›Ú™XÝØ\›Ý™Y‹œ›Ú™XÝÝ˜[Y]Y‹˜XØÛÝ[[™×Ü™XYHˆBˆNÂ‚ˆÛ™È[YÚX›R][PÛÝ[HÂˆÛ™È›ØÚÙY][PÛÝ[HÂˆÛ™È\ÜÝYPÛÝ[HÂˆXÚ[X[[YÚX›RÝ\œÈHÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÓÕS•

ŠH’STˆ
ÒT‘HÝ]\ÈHS–J[YÚX›WÜÝ]\Ù\ÊJNŽ˜šYÚ[ˆÓÐSTÐÑJÕSJÝ\œÊH’STˆ
ÒT‘HÝ]\ÈHS–J[YÚX›WÜÝ]\Ù\ÊJK
NŽ›[Y\šXËˆÓÕS•

ŠH’STˆ
ÒT‘HÓÐSTÐÑJÝ]\Ë	Ù˜Y	ÊHˆS
[YÚX›WÜÝ]\Ù\ÊJNŽ˜šYÚ[ˆÓÕS•

ŠH’STˆ
ÒT‘H›Ú™XÝÚYTÈ•SÔˆ\Ú×ÚYTÈ•S
NŽ˜šYÚ[ˆ”“ÓH[YWÙ[šY\ÂˆÒT‘HÛÜš×Ù]H‘UÑQSˆÙYZ×ÜÝ\S‘ÙYZ×Ù[™Âˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹Ý\
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹[™
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[YÚX›WÜÝ]\Ù\È‹[YÚX›TÝ]\Ù\ÊNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ[YÚX›R][PÛÝ[H™XY\‹‘Ù][

NÂˆ[YÚX›RÝ\œÈH™XY\‹‘Ù]XÚ[X[
JNÂˆ›ØÚÙY][PÛÝ[H™XY\‹‘Ù][
ŠNÂˆ\ÜÝYPÛÝ[H™XY\‹‘Ù][
ÊNÂˆBˆB‚ˆ˜\ˆ^[ØYH™]ÂˆÂˆ™Y›YÚXÝ[Û‹ˆÙYZÔÝ\HÝ\ˆÙYZÑ[™H[™ˆ[YÚX›TÝ]\Ù\Ëˆ[YÚX›R][PÛÝ[ˆ[YÚX›RÝ\œËˆ›ØÚÙY][PÛÝ[ˆ\ÜÝYPÛÝ[ˆ\ÝXÝ]™TÝ]PÚ[™ÙT\™›Ü›YYH˜[ÙKˆ›ÙXÝ[Û”ØY™]PÛÛ›ÛHYKˆ›ÝHH•\È›ÙXÝ[Ûˆ™Y›YÚ˜[Y][Ûˆ™XÛÜ™È]šY[˜ÙHÛ›Kˆ›È[YH[žHÝ]\È\ÈÚ[™ÙYˆ‚ˆNÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ[œÙ\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•ÈÛÜšÙ›Ý×Ü™Y›YÚÝ˜[Y][Û—Ù]™[È
ˆXÝÜ—Ý\Ù\—ÚYˆ™Y›YÚØXÝ[Û‹ˆÙYZ×ÜÝ\ˆÙYZ×Ù[™ˆ[YÚX›WÚ][WØÛÝ[ˆ[YÚX›WÚÝ\œËˆ›ØÚÙYÚ][WØÛÝ[ˆ\ÜÝYWØÛÝ[ˆ™\Ý[Ü^[ØYˆ
BˆSQTÈ
ˆXÝÜ—Ý\Ù\—ÚYˆ™Y›YÚØXÝ[Û‹ˆÙYZ×ÜÝ\ˆÙYZ×Ù[™ˆ[YÚX›WÚ][WØÛÝ[ˆ[YÚX›WÚÝ\œËˆ›ØÚÙYÚ][WØÛÝ[ˆ\ÜÝYWØÛÝ[ˆ™\Ý[Ü^[ØYŽšœÛÛ˜‚ˆ
NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝÜ—Ý\Ù\—ÚY‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™Y›YÚØXÝ[Ûˆ‹™Y›YÚXÝ[ÛŠNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹Ý\
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹[™
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[YÚX›WÚ][WØÛÝ[‹
[
SX]“Z[Š[YÚX›R][PÛÝ[[“X^˜[YJJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[YÚX›WÚÝ\œÈ‹[YÚX›RÝ\œÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜›ØÚÙYÚ][WØÛÝ[‹
[
SX]“Z[Š›ØÚÙY][PÛÝ[[“X^˜[YJJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJš\ÜÝYWØÛÝ[‹
[
SX]“Z[Š\ÜÝYPÛÝ[[“X^˜[YJJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™\Ý[Ü^[ØY‹œÛÛ”Ù\šX[^™\‹”Ù\šX[^™J^[ØY
JNÂˆ]ØZ][œÙ\ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP“HÛÜšÙ›ÝÈ™Y›YÚ˜[Y][Ûˆ[ˆ‹ˆ^[ØYˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÝÛÜšÙ›ÝËÜ™Y›YÚY]™[È‹\Þ[˜È
ÛÛ^ÛÛ^[È[Z]
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™È	‰ˆXXØÙ\ÜËØ[‘^Ü	‰ˆXXØÙ\ÜËØ[•šY]Ð[	‰ˆXXØÙ\ÜËØ[]Y]
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH•ÛÜšÙ›ÝÈ™Y›YÚ]šY[˜ÙH\È™\ÝšXÝYÈ›ÙXÝ[ÛˆÛÜšÙ›ÝÈÜ\˜]ÜœËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆØY™S[Z]HX]Û[\
[Z]ÏÈKKL
NÂˆ˜\ˆ]™[ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆKÛÜšÙ›Ý×Ü™Y›YÚÝ˜[Y][Û—Ù]™[ÚYˆÓÐSTÐÑJK™\Ü^WÛ˜[YKK™[XZ[	ÔÞ\Ý[IÊHTÈXÝÜ—Û˜[YKˆKœ™Y›YÚØXÝ[Û‹ˆKÙYZ×ÜÝ\ˆKÙYZ×Ù[™ˆK™[YÚX›WÚ][WØÛÝ[ˆK™[YÚX›WÚÝ\œËˆK˜›ØÚÙYÚ][WØÛÝ[ˆKš\ÜÝYWØÛÝ[ˆK˜Ü™X]YØ]ˆ”“ÓHÛÜšÙ›Ý×Ü™Y›YÚÝ˜[Y][Û—Ù]™[ÈBˆQ•“ÒSˆ\Ý\Ù\œÈBˆÓˆK\Ù\—ÚYHK˜XÝÜ—Ý\Ù\—ÚYˆÔ‘Tˆ–HK˜Ü™X]YØ]TÐÂˆSRUØY™WÛ[Z]Âˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœØY™WÛ[Z]‹ØY™S[Z]
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
JBˆÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ]™[ËY
™]ÂˆÂˆ™Y›YÚ]™[YH™XY\‹‘Ù]ÝZY

KˆXÝÜ“˜[YHH™XY\‹‘Ù]Ýš[™ÊJKˆ™Y›YÚXÝ[ÛˆH™XY\‹‘Ù]Ýš[™ÊŠKˆÙYZÔÝ\H™XY\‹’\Ñ“[
ÊHÈ
]SÛ›OÊ[[ˆ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠÊKˆÙYZÑ[™H™XY\‹’\Ñ“[

HÈ
]SÛ›OÊ[[ˆ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠ
Kˆ[YÚX›R][PÛÝ[H™XY\‹‘Ù][ÌŠJKˆ[YÚX›RÝ\œÈH™XY\‹‘Ù]XÚ[X[
ŠKˆ›ØÚÙY][PÛÝ[H™XY\‹‘Ù][ÌŠÊKˆ\ÜÝYPÛÝ[H™XY\‹‘Ù][ÌŠ
KˆÜ™X]Y]H™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠJBˆJNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP“HÛÜšÙ›ÝÈ™Y›YÚ]šY[˜ÙH‹ˆÛÝ[H]™[ËÛÝ[ˆ]™[ÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÜ›ÙXÝ[Û‹Ü™XY[™\ÜËXÛÛ[X[™XÙ[\ˆ‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[•šY]È	‰ˆXXØÙ\ÜËØ[•šY]Ð[	‰ˆXXØÙ\ÜËØ[]Y]
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH”›ÙXÝ[Ûˆ™XY[™\ÜÈ\È™\ÝšXÝYÈ\›Ý™Y™\Ü[™È[™ÛÜšÙ›ÝÈ›Û\ËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆÛ™ÈXÝ]™U\Ù\œÈHÂˆÛ™ÈXÝ]™T›Ú™XÝÈHÂˆÛ™È[YQ[šY\ÈHÂˆÛ™È^ÜÈHÂˆÛ™È]Y]]™[ÈHÂˆÛ™È›Ý]PÛÛ˜XÝÈHÂˆÛ™È[Ù[Q^XÝ][ÛœÈHÂˆÛ™È™Y›YÚ]™[ÈHÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH\Ý\Ù\œÈÒT‘H\×ØXÝ]™HH•QJKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH›Ú™XÝÈÒT‘HÝ]\ÈH	ØXÝ]™IÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÙ[šY\ÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH]Y]ÛÙÜÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH›Ý]WÜ\›Z\ÜÚ[Û—ØÛÛ˜XÝÈÒT‘HÛÛ˜XÝÜÝ]\ÈH	ØXÝ]™IÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH\Ú›Ø\™Û[Ù[WÝš\ÚXš[]WÙ^XÝ][ÛœÈÒT‘H\×ØXÝ]™HH•QJKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓHÛÜšÙ›Ý×Ü™Y›YÚÝ˜[Y][Û—Ù]™[ÊNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆXÝ]™U\Ù\œÈH™XY\‹‘Ù][

NÂˆXÝ]™T›Ú™XÝÈH™XY\‹‘Ù][
JNÂˆ[YQ[šY\ÈH™XY\‹‘Ù][
ŠNÂˆ^ÜÈH™XY\‹‘Ù][
ÊNÂˆ]Y]]™[ÈH™XY\‹‘Ù][

NÂˆ›Ý]PÛÛ˜XÝÈH™XY\‹‘Ù][
JNÂˆ[Ù[Q^XÝ][ÛœÈH™XY\‹‘Ù][
ŠNÂˆ™Y›YÚ]™[ÈH™XY\‹‘Ù][
ÊNÂˆBˆB‚ˆ˜\ˆÚXÚÜÈH™]È\ÝØš™XÝ‚ˆÂˆ™]ÈÈÚXÚÈHXÝ]™H\Ù\œÈ‹˜[YHHXÝ]™U\Ù\œËÝ]\ÈHXÝ]™U\Ù\œÈˆÈœ™XYHˆˆ›™YY×Ù]HˆKˆ™]ÈÈÚXÚÈHXÝ]™H›Ú™XÝÈ‹˜[YHHXÝ]™T›Ú™XÝËÝ]\ÈHXÝ]™T›Ú™XÝÈˆÈœ™XYHˆˆ›™YY×Ù]HˆKˆ™]ÈÈÚXÚÈH•[YH[šY\È‹˜[YHH[YQ[šY\ËÝ]\ÈH[YQ[šY\ÈˆÈœ™XYHˆˆ›™YY×Ù]HˆKˆ™]ÈÈÚXÚÈH‘^ÜXÚØYÙ\È‹˜[YHH^ÜËÝ]\ÈH^ÜÈˆÈœ™XYHˆˆ›Ü[Û˜[ˆKˆ™]ÈÈÚXÚÈH]Y]]šY[˜ÙH‹˜[YHH]Y]]™[ËÝ]\ÈH]Y]]™[ÈˆÈœ™XYHˆˆ›™YY×Ù]HˆKˆ™]ÈÈÚXÚÈH”›Ý]H\›Z\ÜÚ[ÛˆÛÛ˜XÝÈ‹˜[YHH›Ý]PÛÛ˜XÝËÝ]\ÈH›Ý]PÛÛ˜XÝÈˆÈœ™XYHˆˆ›™YY×ØÛÛ˜XÝÈˆKˆ™]ÈÈÚXÚÈH‘\Ú›Ø\™[Ù[H™YÚ\ÝžH‹˜[YHH[Ù[Q^XÝ][ÛœËÝ]\ÈH[Ù[Q^XÝ][ÛœÈHLÈœ™XYHˆˆ›™YY×Ü™]šY]ÈˆKˆ™]ÈÈÚXÚÈH•ÛÜšÙ›ÝÈ™Y›YÚ]šY[˜ÙH‹˜[YHH™Y›YÚ]™[ËÝ]\ÈH™Y›YÚ]™[ÈˆÈœ™XYHˆˆœ[™[™×Ùš\œÝÜ[ˆˆBˆNÂ‚ˆ˜\ˆ™XYPÚXÚÐÛÝ[HÚXÚÜËÛÝ[
ÚXÚÈOˆ
Ýš[™ÊXÚXÚË‘Ù]\J
K‘Ù]›Ü\JœÝ]\ÈŠHK‘Ù]˜[YJÚXÚÊHHOHœ™XYHŠNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP“ˆ›ÙXÝ[Ûˆ™XY[™\ÜÈÛÛ[X[™Ù[\ˆ‹ˆÝ[[X\žHH™]ÂˆÂˆ™XYPÚXÚÐÛÝ[ˆÚXÚÐÛÝ[HÚXÚÜËÛÝ[ˆ›ÙXÝ[Û”™XYHHXÝ]™U\Ù\œÈˆ	‰ˆXÝ]™T›Ú™XÝÈˆ	‰ˆ[YQ[šY\Èˆ	‰ˆ]Y]]™[Èˆ	‰ˆ›Ý]PÛÛ˜XÝÈˆˆKˆÚXÚÜÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÜÙXÝ\š]KÜ›Ý]K\\›Z\ÜÚ[Û‹XÛÛ˜XÝÈ‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[•šY]Ð[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH”›Ý]H\›Z\ÜÚ[ÛˆÛÛ˜XÝÈ\™H™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›ÙXÝ[Ûˆ]›Ü›HÜ\˜]ÜœËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆÛÛ˜XÝÈH™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ›Ý]WÚÙ^Kˆ›Ý]WÜ]ˆ[Ù[WÛ˜[YKˆ[Ù[WÙÜ›Ý\ˆ™\]Z\™YÜ\›Z\ÜÚ[ÛœËˆ[ÝÙYÜ›Û\Ëˆ™\ÝšXÝYÜ›Û\ËˆÛÛ˜XÝÜÝ]\Ëˆ›ÙXÝ[Û—ÙÝX\™˜Z[ˆ\]YØ]ˆ”“ÓH›Ý]WÜ\›Z\ÜÚ[Û—ØÛÛ˜XÝÂˆÔ‘Tˆ–H›Ý]WÚÙ^K[Ù[WÙÜ›Ý\[Ù[WÛ˜[YNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÛÛ˜XÝËY
™]ÂˆÂˆ›Ý]RÙ^HH™XY\‹‘Ù]Ýš[™Ê
Kˆ›Ý]T]H™XY\‹‘Ù]Ýš[™ÊJKˆ[Ù[S˜[YHH™XY\‹‘Ù]Ýš[™ÊŠKˆ[Ù[QÜ›Ý\H™XY\‹‘Ù]Ýš[™ÊÊKˆ™\]Z\™Y\›Z\ÜÚ[ÛœÈH™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠ
Kˆ[ÝÙY›Û\ÈH™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠJKˆ™\ÝšXÝY›Û\ÈH™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠŠKˆÛÛ˜XÝÝ]\ÈH™XY\‹‘Ù]Ýš[™ÊÊKˆ›ÙXÝ[Û‘ÝX\™˜Z[H™XY\‹‘Ù]Ýš[™Ê
Kˆ\]Y]H™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠJBˆJNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP”ˆ›Ý]H\›Z\ÜÚ[ÛˆÛÛ˜XÝÈ‹ˆÝ[[X\žHH™]ÂˆÂˆÛÛ˜XÝÛÝ[HÛÛ˜XÝËÛÝ[ˆXÝ]™PÛÛ˜XÝÛÝ[HÛÛ˜XÝËÛÝ[
ÛÛ˜XÝOˆ
Ýš[™ÊXÛÛ˜XÝ‘Ù]\J
K‘Ù]›Ü\J˜ÛÛ˜XÝÝ]\ÈŠHK‘Ù]˜[YJÛÛ˜XÝ
HHOH˜XÝ]™HŠKˆ[™Ú[™Y\”™\ÝšXÝYÛÛ˜XÝÛÝ[HÛÛ˜XÝËÛÝ[
ÛÛ˜XÝOˆ

Ýš[™Ö×JXÛÛ˜XÝ‘Ù]\J
K‘Ù]›Ü\Jœ™\ÝšXÝY›Û\ÈŠHK‘Ù]˜[YJÛÛ˜XÝ
HJKÛÛZ[œÊ‘S‘ÒS‘QTˆŠJBˆKˆÛÛ˜XÝÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÛ˜]šYØ][Û‹Ü™YÚ\ÝžKZ[YÜš]H‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[•šY]Ð[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH“˜]šYØ][Ûˆ™YÚ\ÝžH[YÜš]H\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›ÙXÝ[Ûˆ]›Ü›HÜ\˜]ÜœËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆÛ™È^XÝ][ÛœÈHÂˆÛ™ÈÛÛ˜XÝÈHÂˆÛ™ÈÛÜšÙ›ÝÓ[Ù[\ÈHÂˆÛ™È\Ú›Ø\™[Ù[\ÈHÂˆÛ™ÈÙXÝ\š]S[Ù[\ÈHÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH\Ú›Ø\™Û[Ù[WÝš\ÚXš[]WÙ^XÝ][ÛœÈÒT‘H\×ØXÝ]™HH•QJKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH›Ý]WÜ\›Z\ÜÚ[Û—ØÛÛ˜XÝÈÒT‘HÛÛ˜XÝÜÝ]\ÈH	ØXÝ]™IÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH\Ú›Ø\™Û[Ù[WÝš\ÚXš[]WÙ^XÝ][ÛœÈÒT‘H\×ØXÝ]™HH•QHS‘›Ý]HH	ÝÛÜšÙ›ÝÉÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH\Ú›Ø\™Û[Ù[WÝš\ÚXš[]WÙ^XÝ][ÛœÈÒT‘H\×ØXÝ]™HH•QHS‘›Ý]HH	Ù\Ú›Ø\™	ÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH\Ú›Ø\™Û[Ù[WÝš\ÚXš[]WÙ^XÝ][ÛœÈÒT‘H\×ØXÝ]™HH•QHS‘Ü›Ý\Û˜[YHH	ÔÙXÝ\š]IÊNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ^XÝ][ÛœÈH™XY\‹‘Ù][

NÂˆÛÛ˜XÝÈH™XY\‹‘Ù][
JNÂˆÛÜšÙ›ÝÓ[Ù[\ÈH™XY\‹‘Ù][
ŠNÂˆ\Ú›Ø\™[Ù[\ÈH™XY\‹‘Ù][
ÊNÂˆÙXÝ\š]S[Ù[\ÈH™XY\‹‘Ù][

NÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP•˜]šYØ][Ûˆ™YÚ\ÝžH[YÜš]HÝX\™‹ˆÝ[[X\žHH™]ÂˆÂˆ\Ú›Ø\™[Ù[Q^XÝ][ÛÛÝ[H^XÝ][ÛœËˆ›Ý]T\›Z\ÜÚ[ÛÛÛ˜XÝÛÝ[HÛÛ˜XÝËˆÛÜšÙ›ÝÓ[Ù[PÛÝ[HÛÜšÙ›ÝÓ[Ù[\Ëˆ\Ú›Ø\™›Ý]S[Ù[PÛÝ[H\Ú›Ø\™[Ù[\ËˆÙXÝ\š]S[Ù[PÛÝ[HÙXÝ\š]S[Ù[\Ëˆ™YÚ\ÝžTÝ]\ÈH^XÝ][ÛœÈˆ	‰ˆÛÛ˜XÝÈˆÈœ™XYHˆˆ›™YY×Ü™]šY]È‚ˆKˆÝX\™˜Z[ÈH™]Ö×BˆÂˆ“™]È›ÙXÝ[Ûˆ[Ù[\È]\Ý™H™\™\Ù[Y[ˆ\Ú›Ø\™[Ù[Hš\ÚXš[]H^XÝ][ÛœËˆ‹ˆ”™\ÝšXÝY›ÙXÝ[Ûˆ›Ý]\È]\Ý]™H›Ý]H\›Z\ÜÚ[ÛˆÛÛ˜XÝËˆ‹ˆ‘[™Ú[™Y\‹[Û›H\Ù\œÈ]\Ý™[XZ[ˆ^ÛYYœ›ÛHÛÜšÙ›ÝËÙ^ÜØXØÛÝ[[™ËÜ›ÛK[X]š^ÛÛ›ÛËˆ‹ˆ•šY]ËP\È]\Ý™[XZ[ˆ™XY[Û›H›ÜˆÜš]HÜ\˜][ÛœËˆ‚ˆBˆJNÂŸJNÂ‚‚˜\“X\Ù]
‹Ø\KÙ^Ü\XÚØYÙ\ËÙ]šY[˜ÙK\Ý[[X\žH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[‘^Ü	‰ˆXXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™È	‰ˆXXØÙ\ÜËØ[•šY]Ð[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH”›ÙXÝ[Ûˆ^Ü]šY[˜ÙH\È™\ÝšXÝYÈ^ÜØXØÛÝ[[™È›Û\ËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆÊˆMWÑVÔ•ÑU’QSÑWÔÕSSPT–WÔÕT•
‹Âˆ˜\ˆXÚØYÙ\ÈH™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆK[YWÝÛÜšÙ›Ý×Ù^ÜÚYˆK™^ÜÙ›Ü›X]ˆKÙYZ×ÜÝ\ˆKÙYZ×Ù[™ˆK™^ÜÜÝ]\ËˆKš][WØÛÝ[ˆKÝ[ÚÝ\œËˆK™š[WÛ˜[YKˆK˜Ü™X]YØ]ˆKœXÚØYÙWÙÙ[™\˜]YØ]ˆÓÐSTÐÑJKœXÚØYÙWÙÝÛ›ØYØÛÝ[
HTÈXÚØYÙWÙÝÛ›ØYØÛÝ[ˆKœXÚØYÙWÛ\ÝÙÝÛ›ØYYØ]ˆÓÐSTÐÑJKœXÚØYÙWØÛÛ[Ý\K	Ý^ØÜÝ‰ÊHTÈXÚØYÙWØÛÛ[Ý\KˆÓÐSTÐÑJKœXÚØYÙWÙš[WÙ^[œÚ[Û‹	ØÜÝ‰ÊHTÈXÚØYÙWÙš[WÙ^[œÚ[Û‹ˆÓÐSTÐÑJ
ˆÑSPÕÓÕS•

ŠNŽ˜šYÚ[ˆ”“ÓH]Y]ÛÙÜÈ[ˆÒT‘H[™[]WÚYHK[YWÝÛÜšÙ›Ý×Ù^ÜÚYˆS‘
[˜XÝ[ÛˆSRÑH	ÉY^Ü	IÈÔˆ[˜XÝ[ÛˆSRÑH	ÉYÝÛ›ØY	IÊBˆ
K
HTÈ]Y]Ù]™[ØÛÝ[ˆÓÐSTÐÑJKœXÚØYÙWÜÚLM‹	ÉÊHTÈXÚØYÙWÜÚLM‹ˆÓÐSTÐÑJKœXÚØYÙWÜÛ˜\ÚÝÚ][WØÛÝ[
NŽš[TÈXÚØYÙWÜÛ˜\ÚÝÚ][WØÛÝ[ˆÓÐSTÐÑJ
ˆÑSPÕÓÕS•

ŠNŽš[ˆ”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÚ][\ÈBˆÒT‘HK[YWÝÛÜšÙ›Ý×Ù^ÜÚYHK[YWÝÛÜšÙ›Ý×Ù^ÜÚYˆ
K
HTÈÛ˜\ÚÝÚ][WØÛÝ[ˆÓÐSTÐÑJ
ˆÑSPÕÕSJKšÝ\œÊNŽ›[Y\šXÂˆ”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÚ][\ÈBˆÒT‘HK[YWÝÛÜšÙ›Ý×Ù^ÜÚYHK[YWÝÛÜšÙ›Ý×Ù^ÜÚYˆ
K
NŽ›[Y\šXÈTÈÛ˜\ÚÝÝÝ[ÚÝ\œËˆÐTÑBˆÒSˆVTÕÈ
ˆÑSPÕBˆ”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÚ][\ÈBˆÒT‘HK[YWÝÛÜšÙ›Ý×Ù^ÜÚYHK[YWÝÛÜšÙ›Ý×Ù^ÜÚYˆ
HSˆ	ÜÛ˜\ÚÝ	ÂˆSÑH	ÛYØXÞWÛ]™WÙ˜[˜XÚÉÂˆS‘TÈÛ˜\ÚÝÜÛÝ\˜ÙBˆ”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÈBˆQ•“ÒSˆ[YWÝÛÜšÙ›Ý×Ù^ÜÛY]Y]HBˆÓˆK[YWÝÛÜšÙ›Ý×Ù^ÜÚYHK[YWÝÛÜšÙ›Ý×Ù^ÜÚYˆÔ‘Tˆ–HK˜Ü™X]YØ]TÐÂˆSRULÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ˜\ˆ]Y]]™[ÛÝ[H™XY\‹‘Ù][
M
NÂˆ˜\ˆXÚØYÙQÝÛ›ØYÛÝ[H™XY\‹‘Ù][ÌŠL
NÂˆ˜\ˆXÚØYÙTÚLMˆH™XY\‹‘Ù]Ýš[™ÊMJNÂˆ˜\ˆÛ˜\ÚÝ][PÛÝ[H™XY\‹‘Ù][ÌŠMÊNÂ‚ˆXÚØYÙ\ËY
™]ÂˆÂˆ^ÜYH™XY\‹‘Ù]ÝZY

Kˆ^Ü›Ü›X]H™XY\‹‘Ù]Ýš[™ÊJKˆÙYZÔÝ\H™XY\‹’\Ñ“[
ŠHÈ
]SÛ›OÊ[[ˆ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠŠKˆÙYZÑ[™H™XY\‹’\Ñ“[
ÊHÈ
]SÛ›OÊ[[ˆ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠÊKˆ^ÜÝ]\ÈH™XY\‹‘Ù]Ýš[™Ê
Kˆ][PÛÝ[H™XY\‹‘Ù][ÌŠJKˆÝ[Ý\œÈH™XY\‹‘Ù]XÚ[X[
ŠKˆš[S˜[YHH™XY\‹’\Ñ“[
ÊHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊÊKˆÜ™X]Y]H™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]Š
KˆXÚØYÙQÙ[™\˜]Y]H™XY\‹’\Ñ“[
JHÈ
]U[YSÙ™œÙ]Ê[[ˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠJKˆXÚØYÙQÝÛ›ØYÛÝ[ˆXÚØYÙS\ÝÝÛ›ØYY]H™XY\‹’\Ñ“[
LJHÈ
]U[YSÙ™œÙ]Ê[[ˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠLJKˆXÚØYÙPÛÛ[\HH™XY\‹‘Ù]Ýš[™ÊLŠKˆXÚØYÙQš[Q^[œÚ[ÛˆH™XY\‹‘Ù]Ýš[™ÊLÊKˆ]Y]]™[ÛÝ[ˆXÚØYÙTÚLM‹ˆXÚØYÙTÛ˜\ÚÝ][PÛÝ[H™XY\‹‘Ù][ÌŠMŠKˆÛ˜\ÚÝ][PÛÝ[ˆÛ˜\ÚÝÝ[Ý\œÈH™XY\‹‘Ù]XÚ[X[
N
KˆÛ˜\ÚÝÛÝ\˜ÙHH™XY\‹‘Ù]Ýš[™ÊNJKˆÚXÚÜÝ[P]˜Z[X›HH\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÚØYÙTÚLMŠKˆÛ˜\ÚÝ˜XÚÙYHÛ˜\ÚÝ][PÛÝ[ˆˆ›ÙXÝ[Û‘]šY[˜ÙT™XYHH]Y]]™[ÛÝ[ˆXÚØYÙQÝÛ›ØYÛÝ[ˆÛ˜\ÚÝ][PÛÝ[ˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÚØYÙTÚLMŠBˆJNÂˆBˆB‚ˆ˜\ˆ]šY[˜ÙT™XYPÛÝ[HXÚØYÙ\ËÛÝ[
XÚØYÙHOˆ
›ÛÛ
\XÚØYÙK‘Ù]\J
K‘Ù]›Ü\Jœ›ÙXÝ[Û‘]šY[˜ÙT™XYHŠHK‘Ù]˜[YJXÚØYÙJHJNÂˆ˜\ˆÝÛ›ØYYXÚØYÙPÛÝ[HXÚØYÙ\ËÛÝ[
XÚØYÙHOˆ
[
\XÚØYÙK‘Ù]\J
K‘Ù]›Ü\JœXÚØYÙQÝÛ›ØYÛÝ[ŠHK‘Ù]˜[YJXÚØYÙJHHˆ
NÂˆ˜\ˆÛ˜\ÚÝ˜XÚÙYXÚØYÙPÛÝ[HXÚØYÙ\ËÛÝ[
XÚØYÙHOˆ
›ÛÛ
\XÚØYÙK‘Ù]\J
K‘Ù]›Ü\JœÛ˜\ÚÝ˜XÚÙYŠHK‘Ù]˜[YJXÚØYÙJHJNÂˆ˜\ˆÚXÚÜÝ[P]˜Z[X›PÛÝ[HXÚØYÙ\ËÛÝ[
XÚØYÙHOˆ
›ÛÛ
\XÚØYÙK‘Ù]\J
K‘Ù]›Ü\J˜ÚXÚÜÝ[P]˜Z[X›HŠHK‘Ù]˜[YJXÚØYÙJHJNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒMH›ÙXÝ[Ûˆ^Ü]šY[˜ÙHš\ÚXš[]H‹ˆÝ[[X\žHH™]ÂˆÂˆXÚØYÙPÛÝ[HXÚØYÙ\ËÛÝ[ˆ]šY[˜ÙT™XYPÛÝ[ˆÝÛ›ØYYXÚØYÙPÛÝ[ˆÛ˜\ÚÝ˜XÚÙYXÚØYÙPÛÝ[ˆÚXÚÜÝ[P]˜Z[X›PÛÝ[ˆKˆXÚØYÙ\ÂˆJNÂˆÊˆMWÑVÔ•ÑU’QSÑWÔÕSSPT–WÑS‘
‹ÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÝÛÜšÙ›ÝËÛÜ\˜][ÛœË]ZKY]H‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹Y\ÜØYÙHH“Z\ÜÚ[™ÈÙ\ÜÚ[ÛˆÚÙ[‹ˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆXØÙ\ÜÈH]ØZ]ØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆYˆ
XXØÙ\ÜËØ[•šY]È	‰ˆXXØÙ\ÜËØ[”›Ú™XÝ\›Ý™H	‰ˆXXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™È	‰ˆXXØÙ\ÜËØ[‘^Ü	‰ˆXXØÙ\ÜËØ[]Y]	‰ˆXXØÙ\ÜËØ[•šY]Ð[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÈÈÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹Y\ÜØYÙHH•ÛÜšÙ›ÝÈÜ\˜][ÛœÈRH]H\È™\ÝšXÝYÈÛÜšÙ›ÝÈ›Û\ËˆˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆÛ™È]Y]]™[ÈHÂˆÛ™È^ÜXÚØYÙ\ÈHÂˆÛ™È™Y›YÚ]™[ÈHÂˆÛ™È›Ý]PÛÛ˜XÝÈHÂˆÛ™È^Ü™XYQ[šY\ÈHÂˆÛ™ÈXØÛÝ[[™Ô]Y]YR][\ÈHÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH]Y]ÛÙÜÈÒT‘HXÝ[ÛˆSRÑH	É][YIIÈÔˆXÝ[ÛˆSRÑH	ÉX\›Ý˜[	IÈÔˆXÝ[ÛˆSRÑH	ÉY^Ü	IÈÔˆXÝ[ÛˆSRÑH	É\™XÛÛ˜Ú[IIÈÔˆXÝ[ÛˆSRÑH	É[ØÚÉIÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓHÛÜšÙ›Ý×Ü™Y›YÚÝ˜[Y][Û—Ù]™[ÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH›Ý]WÜ\›Z\ÜÚ[Û—ØÛÛ˜XÝÈÒT‘HÛÛ˜XÝÜÝ]\ÈH	ØXÝ]™IÊKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÙ[šY\ÈÒT‘HÝ]\ÈSˆ
	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊJKˆ
ÑSPÕÓÕS•

ŠNŽ˜šYÚ[”“ÓH[YWÙ[šY\ÈÒT‘HÝ]\ÈSˆ
	Ü›Ú™XÝØ\›Ý™Y	Ë	Ü›Ú™XÝÝ˜[Y]Y	Ë	ØXØÛÝ[[™×Ü™XYIÊJNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ]Y]]™[ÈH™XY\‹‘Ù][

NÂˆ^ÜXÚØYÙ\ÈH™XY\‹‘Ù][
JNÂˆ™Y›YÚ]™[ÈH™XY\‹‘Ù][
ŠNÂˆ›Ý]PÛÛ˜XÝÈH™XY\‹‘Ù][
ÊNÂˆ^Ü™XYQ[šY\ÈH™XY\‹‘Ù][

NÂˆXØÛÝ[[™Ô]Y]YR][\ÈH™XY\‹‘Ù][
JNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKP“ÈÛÜšÙ›ÝÈÜ\˜][ÛœÈRKÑ]H›Ý[™][Ûˆ‹ˆÝ[[X\žHH™]ÂˆÂˆ]Y]]™[Ëˆ^ÜXÚØYÙ\Ëˆ™Y›YÚ]™[Ëˆ›Ý]PÛÛ˜XÝËˆ^Ü™XYQ[šY\ËˆXØÛÝ[[™Ô]Y]YR][\Ëˆ›ÙXÝ[Û“Ü\˜][ÛœÔÝ]\ÈHœ™XYH‚ˆKˆXØÙ\ÜÈH™]ÂˆÂˆXØÙ\ÜËØ[•šY]ËˆXØÙ\ÜËØ[”›Ú™XÝ\›Ý™KˆXØÙ\ÜËØ[“X[˜YÙPXØÛÝ[[™ËˆXØÙ\ÜËØ[‘^ÜˆXØÙ\ÜËØ[]Y]ˆXØÙ\ÜËØ[•šY]Ð[ˆKˆ›ÙXÝ[Û”[™[ÈH™]Ö×BˆÂˆ™]ÈÈ[™[Ù^HHœ™Y›YÚÝ˜[Y][Ûˆ‹]HH•ÛÜšÙ›ÝÈ™Y›YÚ˜[Y][Ûˆ‹[™Ú[H‹Ø\KÝÛÜšÙ›ÝËÜ™Y›YÚ]˜[Y][ÛˆˆKˆ™]ÈÈ[™[Ù^HHœ›ÙXÝ[Û—Ü™XY[™\ÜÈ‹]HH”›ÙXÝ[Ûˆ™XY[™\ÜÈÛÛ[X[™Ù[\ˆ‹[™Ú[H‹Ø\KÜ›ÙXÝ[Û‹Ü™XY[™\ÜËXÛÛ[X[™XÙ[\ˆˆKˆ™]ÈÈ[™[Ù^HH™^ÜÙ]šY[˜ÙH‹]HH”›ÙXÝ[Ûˆ^Ü]šY[˜ÙH‹[™Ú[H‹Ø\KÙ^Ü\XÚØYÙ\ËÙ]šY[˜ÙK\Ý[[X\žHˆKˆ™]ÈÈ[™[Ù^HHœ›Ý]WØÛÛ˜XÝÈ‹]HH”›Ý]H\›Z\ÜÚ[ÛˆÛÛ˜XÝÈ‹[™Ú[H‹Ø\KÜÙXÝ\š]KÜ›Ý]K\\›Z\ÜÚ[Û‹XÛÛ˜XÝÈˆKˆ™]ÈÈ[™[Ù^HHœ™YÚ\ÝžWÚ[YÜš]H‹]HH“˜]šYØ][Ûˆ™YÚ\ÝžH[YÜš]H‹[™Ú[H‹Ø\KÛ˜]šYØ][Û‹Ü™YÚ\ÝžKZ[YÜš]HˆBˆBˆJNÂŸJNÂ‚‚‚‹ËÈNSKPÒH›ÙXÝ[ÛˆÜ\˜][ÛœÈXÚÛ›ÝÛYÛY[È
ÈÚYÛ‹SÙ™ˆ]šY[˜ÙHHÕT•˜\“X\Ù]
‹Ø\KÜ›ÙXÝ[Û‹ÛÜ\˜][ÛœËXXÚÛ›ÝÛYÛY[ËÜÝ[[X\žH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH”›ÙXÝ[ÛˆÜ\˜][ÛœÈXÚÛ›ÝÛYÛY[È\™H™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆ›Ý]Qš[\ˆHÛÛ^”™\]Y\Ý”]Y\žVÈœ›Ý]RÙ^H—K‘š\œÝÜ‘Y˜][

NÂ‚ˆ˜\ˆXÚÛ›ÝÛYÛY[ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ›ÙXÝ[Û—ÛÜ\˜][Ûœ×ØXÚÛ›ÝÛYÛY[ÚYˆ›Ý]WÚÙ^KˆÜ\˜][Û—ÚÙ^KˆÜ\˜][Û—Ý]KˆXÚÛ›ÝÛYÛY[ÜÝ]\ËˆXÚÛ›ÝÛYÛY[Û›ÝKˆXÚÛ›ÝÛYÙYØžWÙ[XZ[ˆXÚÛ›ÝÛYÙYØ]ˆ]šY[˜ÙWÜÛ˜\ÚÝˆ”“ÓH›ÙXÝ[Û—ÛÜ\˜][Ûœ×ØXÚÛ›ÝÛYÛY[ÂˆÒT‘H\×ØXÝ]™HHYBˆS‘
›Ý]WÚÙ^HTÈ•SÔˆ›Ý]WÚÙ^HH›Ý]WÚÙ^JBˆÔ‘Tˆ–HXÚÛ›ÝÛYÙYØ]TÐÂˆSRULÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆÛÛ[X[™”\˜[Y]\œËY
™]ÈœÜÜ[\˜[Y]\Šœ›Ý]WÚÙ^H‹œÜÜ[\\Ë“œÜÜ[•\K•^
BˆÂˆ˜[YHHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ›Ý]Qš[\ŠHÈ“[•˜[YHˆ›Ý]Qš[\‹•š[J
BˆJNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆXÚÛ›ÝÛYÛY[ËY
™]ÂˆÂˆXÚÛ›ÝÛYÛY[YH™XY\‹‘Ù]ÝZY

Kˆ›Ý]RÙ^HH™XY\‹‘Ù]Ýš[™ÊJKˆÜ\˜][Û’Ù^HH™XY\‹‘Ù]Ýš[™ÊŠKˆÜ\˜][Û•]HH™XY\‹‘Ù]Ýš[™ÊÊKˆXÚÛ›ÝÛYÛY[Ý]\ÈH™XY\‹‘Ù]Ýš[™Ê
KˆXÚÛ›ÝÛYÛY[›ÝHH™XY\‹’\Ñ“[
JHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊJKˆXÚÛ›ÝÛYÙYžQ[XZ[H™XY\‹’\Ñ“[
ŠHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊŠKˆXÚÛ›ÝÛYÙY]H™XY\‹‘Ù]]U[YJÊKˆ]šY[˜ÙTÛ˜\ÚÝH™XY\‹’\Ñ“[

HÈžßHˆˆ™XY\‹‘Ù]˜[YJ
OË•ÔÝš[™Ê
HÏÈžßH‚ˆJNÂˆBˆB‚ˆÛ™ÈÝ[XÚÛ›ÝÛYÛY[ÈHÂˆÛ™È\Ú›Ø\™XÚÛ›ÝÛYÛY[ÈHÂˆÛ™ÈÛÜšÙ›ÝÐXÚÛ›ÝÛYÛY[ÈHÂˆÛ™È›ÛPYZ[XÚÛ›ÝÛYÛY[ÈHÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÝ[[X\žPÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÓÕS•

ŠH’STˆ
ÒT‘H\×ØXÝ]™HHYJHTÈÝ[ØXÚÛ›ÝÛYÛY[ËˆÓÕS•

ŠH’STˆ
ÒT‘H\×ØXÝ]™HHYHS‘›Ý]WÚÙ^HH	Ù\Ú›Ø\™	ÊHTÈ\Ú›Ø\™ØXÚÛ›ÝÛYÛY[ËˆÓÕS•

ŠH’STˆ
ÒT‘H\×ØXÝ]™HHYHS‘›Ý]WÚÙ^HH	ÝÛÜšÙ›ÝÉÊHTÈÛÜšÙ›Ý×ØXÚÛ›ÝÛYÛY[ËˆÓÕS•

ŠH’STˆ
ÒT‘H\×ØXÝ]™HHYHS‘›Ý]WÚÙ^HH	Ü›ÛKXYZ[‰ÊHTÈ›ÛWØYZ[—ØXÚÛ›ÝÛYÛY[Âˆ”“ÓH›ÙXÝ[Û—ÛÜ\˜][Ûœ×ØXÚÛ›ÝÛYÛY[ÎÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]Ý[[X\žPÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÝ[XÚÛ›ÝÛYÛY[ÈH™XY\‹‘Ù][

NÂˆ\Ú›Ø\™XÚÛ›ÝÛYÛY[ÈH™XY\‹‘Ù][
JNÂˆÛÜšÙ›ÝÐXÚÛ›ÝÛYÛY[ÈH™XY\‹‘Ù][
ŠNÂˆ›ÛPYZ[XÚÛ›ÝÛYÛY[ÈH™XY\‹‘Ù][
ÊNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKPÒH›ÙXÝ[ÛˆÜ\˜][ÛœÈXÚÛ›ÝÛYÛY[È
ÈÚYÛ‹SÙ™ˆ]šY[˜ÙH‹ˆÝ[[X\žHH™]ÂˆÂˆÝ[XÚÛ›ÝÛYÛY[Ëˆ\Ú›Ø\™XÚÛ›ÝÛYÛY[ËˆÛÜšÙ›ÝÐXÚÛ›ÝÛYÛY[Ëˆ›ÛPYZ[XÚÛ›ÝÛYÛY[Ëˆ›Ý]Qš[\ˆHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ›Ý]Qš[\ŠHÈ˜[ˆˆ›Ý]Qš[\‚ˆKˆXÚÛ›ÝÛYÛY[ÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÜ›ÙXÝ[Û‹ÛÜ\˜][ÛœËXXÚÛ›ÝÛYÛY[ËÙ]™[È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH”›ÙXÝ[ÛˆÜ\˜][ÛœÈXÚÛ›ÝÛYÛY[]šY[˜ÙH\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆ[Z]HNÂˆYˆ
[•žT\œÙJÛÛ^”™\]Y\Ý”]Y\žVÈ›[Z]—K‘š\œÝÜ‘Y˜][

KÝ]˜\ˆ\œÙY[Z]
JBˆÂˆ[Z]HX]Û[\
\œÙY[Z]KL
NÂˆB‚ˆ˜\ˆ]™[ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ›ÙXÝ[Û—ÛÜ\˜][Ûœ×ØXÚÛ›ÝÛYÛY[ÚYˆ›Ý]WÚÙ^KˆÜ\˜][Û—ÚÙ^KˆÜ\˜][Û—Ý]KˆXÚÛ›ÝÛYÛY[ÜÝ]\ËˆXÚÛ›ÝÛYÛY[Û›ÝKˆXÚÛ›ÝÛYÙYØžWÙ[XZ[ˆXÚÛ›ÝÛYÙYØ]ˆ]šY[˜ÙWÜÛ˜\ÚÝˆ”“ÓH›ÙXÝ[Û—ÛÜ\˜][Ûœ×ØXÚÛ›ÝÛYÛY[ÂˆÒT‘H\×ØXÝ]™HHYBˆÔ‘Tˆ–HXÚÛ›ÝÛYÙYØ]TÐÂˆSRU[Z]Âˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›[Z]‹[Z]
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ]™[ËY
™]ÂˆÂˆXÚÛ›ÝÛYÛY[YH™XY\‹‘Ù]ÝZY

Kˆ›Ý]RÙ^HH™XY\‹‘Ù]Ýš[™ÊJKˆÜ\˜][Û’Ù^HH™XY\‹‘Ù]Ýš[™ÊŠKˆÜ\˜][Û•]HH™XY\‹‘Ù]Ýš[™ÊÊKˆXÚÛ›ÝÛYÛY[Ý]\ÈH™XY\‹‘Ù]Ýš[™Ê
KˆXÚÛ›ÝÛYÛY[›ÝHH™XY\‹’\Ñ“[
JHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊJKˆXÚÛ›ÝÛYÙYžQ[XZ[H™XY\‹’\Ñ“[
ŠHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊŠKˆXÚÛ›ÝÛYÙY]H™XY\‹‘Ù]]U[YJÊKˆ]šY[˜ÙTÛ˜\ÚÝH™XY\‹’\Ñ“[

HÈžßHˆˆ™XY\‹‘Ù]˜[YJ
OË•ÔÝš[™Ê
HÏÈžßH‚ˆJNÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKPÒH›ÙXÝ[ÛˆÜ\˜][ÛœÈXÚÛ›ÝÛYÛY[]™[È‹ˆÛÝ[H]™[ËÛÝ[ˆ]™[ÂˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÜ›ÙXÝ[Û‹ÛÜ\˜][ÛœËXXÚÛ›ÝÛYÛY[È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH”›ÙXÝ[ÛˆÜ\˜][ÛœÈXÚÛ›ÝÛYÛY[\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ\Ú[™È˜\ˆØÝ[Y[H]ØZ]œÛÛ‘ØÝ[Y[”\œÙP\Þ[˜ÊÛÛ^”™\]Y\Ý›ÙJNÂˆ˜\ˆ›ÛÝHØÝ[Y[”›ÛÝ[[Y[Â‚ˆ˜\ˆ›Ý]RÙ^HH›ÛÝ•žQÙ]›Ü\Jœ›Ý]RÙ^H‹Ý]˜\ˆ›Ý]Q[[Y[
HÈ›Ý]Q[[Y[‘Ù]Ýš[™Ê
Hˆ[Âˆ˜\ˆÜ\˜][Û’Ù^HH›ÛÝ•žQÙ]›Ü\J›Ü\˜][Û’Ù^H‹Ý]˜\ˆÜ\˜][Û‘[[Y[
HÈÜ\˜][Û‘[[Y[‘Ù]Ýš[™Ê
Hˆ[Âˆ˜\ˆÜ\˜][Û•]HH›ÛÝ•žQÙ]›Ü\J›Ü\˜][Û•]H‹Ý]˜\ˆ]Q[[Y[
HÈ]Q[[Y[‘Ù]Ýš[™Ê
Hˆ[Âˆ˜\ˆXÚÛ›ÝÛYÛY[›ÝHH›ÛÝ•žQÙ]›Ü\J˜XÚÛ›ÝÛYÛY[›ÝH‹Ý]˜\ˆ›ÝQ[[Y[
HÈ›ÝQ[[Y[‘Ù]Ýš[™Ê
Hˆ[Âˆ˜\ˆ]šY[˜ÙTÛ˜\ÚÝH›ÛÝ•žQÙ]›Ü\J™]šY[˜ÙTÛ˜\ÚÝ‹Ý]˜\ˆ]šY[˜ÙQ[[Y[
HÈ]šY[˜ÙQ[[Y[‘Ù]˜]Õ^

HˆžßHŽÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ›Ý]RÙ^JHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJÜ\˜][Û’Ù^JHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJÜ\˜][Û•]JJBˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈHš[˜[YÜ™\]Y\Ý‹ˆY\ÜØYÙHHœ›Ý]RÙ^KÜ\˜][Û’Ù^K[™Ü\˜][Û•]H\™H™\]Z\™Yˆ‚ˆJNÂˆB‚ˆ˜\ˆXÝÜ•\Ù\’YH]ØZ]™\ÛÛ™TÙ\ÜÚ[Û•\Ù\’Y›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆ˜\ˆXÝÜ‘[XZ[HˆŽÂ‚ˆYˆ
XÝÜ•\Ù\’Y\È›Ý[
BˆÂˆ]ØZ]\Ú[™È˜\ˆ\Ù\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ[XZ[ˆ”“ÓH\Ý\Ù\œÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ\Ù\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹XÝÜ•\Ù\’Y•˜[YJNÂˆXÝÜ‘[XZ[H
]ØZ]\Ù\ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
JOË•ÔÝš[™Ê
HÏÈˆŽÂˆB‚ˆÝZYXÚÛ›ÝÛYÛY[YÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È›ÙXÝ[Û—ÛÜ\˜][Ûœ×ØXÚÛ›ÝÛYÛY[È
ˆ›Ý]WÚÙ^KˆÜ\˜][Û—ÚÙ^KˆÜ\˜][Û—Ý]KˆXÚÛ›ÝÛYÛY[ÜÝ]\ËˆXÚÛ›ÝÛYÛY[Û›ÝKˆXÚÛ›ÝÛYÙYØžWÝ\Ù\—ÚYˆXÚÛ›ÝÛYÙYØžWÙ[XZ[ˆ]šY[˜ÙWÜÛ˜\ÚÝˆ
BˆSQTÈ
ˆ›Ý]WÚÙ^KˆÜ\˜][Û—ÚÙ^KˆÜ\˜][Û—Ý]Kˆ	ØXÚÛ›ÝÛYÙY	ËˆXÚÛ›ÝÛYÛY[Û›ÝKˆXÚÛ›ÝÛYÙYØžWÝ\Ù\—ÚYˆXÚÛ›ÝÛYÙYØžWÙ[XZ[ˆ]šY[˜ÙWÜÛ˜\ÚÝŽšœÛÛ˜‚ˆ
Bˆ‘UT“’S‘È›ÙXÝ[Û—ÛÜ\˜][Ûœ×ØXÚÛ›ÝÛYÛY[ÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›Ý]WÚÙ^H‹›Ý]RÙ^K•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›Ü\˜][Û—ÚÙ^H‹Ü\˜][Û’Ù^K•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›Ü\˜][Û—Ý]H‹Ü\˜][Û•]K•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÚÛ›ÝÛYÛY[Û›ÝH‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÚÛ›ÝÛYÛY[›ÝJHÈ“[•˜[YHˆXÚÛ›ÝÛYÛY[›ÝK•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÚÛ›ÝÛYÙYØžWÝ\Ù\—ÚY‹XÝÜ•\Ù\’Y\È[È“[•˜[YHˆXÝÜ•\Ù\’Y•˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÚÛ›ÝÛYÙYØžWÙ[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÝÜ‘[XZ[
HÈ“[•˜[YHˆXÝÜ‘[XZ[
NÂˆÛÛ[X[™”\˜[Y]\œËY
™]ÈœÜÜ[\˜[Y]\Š™]šY[˜ÙWÜÛ˜\ÚÝ‹œÜÜ[\\Ë“œÜÜ[•\K’œÛÛ˜ŠBˆÂˆ˜[YHHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ]šY[˜ÙTÛ˜\ÚÝ
HÈžßHˆˆ]šY[˜ÙTÛ˜\ÚÝˆJNÂ‚ˆXÚÛ›ÝÛYÛY[YH
ÝZY
J]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈÝZY‘[\JNÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKPÒH›ÙXÝ[ÛˆÜ\˜][ÛœÈXÚÛ›ÝÛYÛY[È
ÈÚYÛ‹SÙ™ˆ]šY[˜ÙH‹ˆÝ]\ÈH˜XÚÛ›ÝÛYÙY‹ˆXÚÛ›ÝÛYÛY[Yˆ›Ý]RÙ^KˆÜ\˜][Û’Ù^KˆÜ\˜][Û•]KˆXÚÛ›ÝÛYÙYžQ[XZ[HXÝÜ‘[XZ[ˆY\ÜØYÙHH”›ÙXÝ[ÛˆÜ\˜][ÛˆXÚÛ›ÝÛYÛY[Ø\È™XÛÜ™YÚ]]šY[˜ÙKˆ‚ˆJNÂŸJNÂ‚œÝ]XÈ\Þ[˜È\ÚÏÝZYÏˆ™\ÛÛ™TÙ\ÜÚ[Û•\Ù\’Y›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[\Þ[˜ÊÛÛ^ÛÛ^œÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[ÛŠBžÂˆ˜\ˆÚÙ[ˆHÛÛ^”™\]Y\Ý’XY\œÖÈ–T›Ú™XÝ[ÙKTÙ\ÜÚ[Ûˆ—K‘š\œÝÜ‘Y˜][

NÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÚÙ[ŠJBˆÂˆ™]\›ˆ[ÂˆB‚ˆÝ]XÈÝš[™È][ÝRY[YšY\‘›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[
Ýš[™ÈY[YšY\ŠBˆÂˆ™]\›ˆ—ˆˆ
ÈY[YšY\‹”™\XÙJ—ˆ‹——ˆŠH
È—ˆŽÂˆB‚ˆÝ]XÈ\Þ[˜È\ÚÏ\ÚÙ]Ýš[™ÏˆÙ]›ÙXÝ[ÛXÚÛ›ÝÛYÛY[ÛÛ[[œÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÚÝ\ÛÛ›™XÝ[Û‹Ýš[™ÈX›S˜[YJBˆÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[[œÐÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕÛÛ[[—Û˜[YBˆ”“ÓH[™›Ü›X][Û—ÜØÚ[XK˜ÛÛ[[œÂˆÒT‘HX›WÜØÚ[XHH	ÜX›XÉÂˆS‘X›WÛ˜[YHHX›WÛ˜[YNÂˆˆˆ‹ÛÚÝ\ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[[œÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJX›WÛ˜[YH‹X›S˜[YJNÂ‚ˆ˜\ˆÛÛ[[œÈH™]È\ÚÙ]Ýš[™ÏŠÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[[œÐÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÛÛ[[œËY
™XY\‹‘Ù]Ýš[™Ê
JNÂˆB‚ˆ™]\›ˆÛÛ[[œÎÂˆB‚ˆÝ]XÈÝš[™ÏÈXÚÔ›ÙXÝ[ÛXÚÛ›ÝÛYÛY[ÛÛ[[Š\ÚÙ]Ýš[™ÏˆÛÛ[[œË\˜[\ÈÝš[™Ö×HØ[™Y]\ÊBˆÂˆ™]\›ˆØ[™Y]\Ë‘š\œÝÜ‘Y˜][
ÛÛ[[œËÛÛZ[œÊNÂˆB‚ˆÝš[™ÏÈÙ\ÜÚ[Û•X›NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆX›PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕX›WÛ˜[YBˆ”“ÓH[™›Ü›X][Û—ÜØÚ[XK˜ÛÛ[[œÂˆÒT‘HX›WÜØÚ[XHH	ÜX›XÉÂˆÔ“ÕT–HX›WÛ˜[YBˆU’S‘Âˆ›ÛÛÛÜŠÛÛ[[—Û˜[YHSˆ
	ÜÙ\ÜÚ[Û—ÝÚÙ[‰Ë	ÝÚÙ[‰ÊJBˆS‘›ÛÛÛÜŠÛÛ[[—Û˜[YHSˆ
	Ý\Ù\—ÚY	Ë	Ø\Ý\Ù\—ÚY	ÊJBˆÔ‘Tˆ–BˆÐTÑBˆÒSˆX›WÛ˜[YHH	Ø]]ÜÙ\ÜÚ[ÛœÉÈSˆˆÒSˆX›WÛ˜[YHH	Ý\Ù\—ÜÙ\ÜÚ[ÛœÉÈSˆBˆÒSˆX›WÛ˜[YHH	ÜÙ\ÜÚ[ÛœÉÈSˆ‚ˆÒSˆX›WÛ˜[YHSRÑH	É\Ù\ÜÚ[Û‰IÈSˆÂˆSÑHˆS‘ˆX›WÛ˜[YBˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆÙ\ÜÚ[Û•X›HH
]ØZ]X›PÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
JOË•ÔÝš[™Ê
NÂˆB‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÙ\ÜÚ[Û•X›JJBˆÂˆ™]\›ˆ[ÂˆB‚ˆ˜\ˆÛÛ[[œÈH]ØZ]Ù]›ÙXÝ[ÛXÚÛ›ÝÛYÛY[ÛÛ[[œÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Ù\ÜÚ[Û•X›JNÂˆ˜\ˆÚÙ[ÛÛ[[ˆHXÚÔ›ÙXÝ[ÛXÚÛ›ÝÛYÛY[ÛÛ[[ŠÛÛ[[œËœÙ\ÜÚ[Û—ÝÚÙ[ˆ‹ÚÙ[ˆŠNÂˆ˜\ˆ\Ù\’YÛÛ[[ˆHXÚÔ›ÙXÝ[ÛXÚÛ›ÝÛYÛY[ÛÛ[[ŠÛÛ[[œË\Ù\—ÚY‹˜\Ý\Ù\—ÚYŠNÂˆ˜\ˆ^\™\ÐÛÛ[[ˆHXÚÔ›ÙXÝ[ÛXÚÛ›ÝÛYÛY[ÛÛ[[ŠÛÛ[[œË™^\™\×Ø]‹™^\™\×Ý]È‹™^\™\×ÛÛˆŠNÂˆ˜\ˆ™]›ÚÙYÛÛ[[ˆHXÚÔ›ÙXÝ[ÛXÚÛ›ÝÛYÛY[ÛÛ[[ŠÛÛ[[œËœ™]›ÚÙYØ]‹œ™]›ÚÙYÝ]ÈŠNÂˆ˜\ˆXÝ]™PÛÛ[[ˆHXÚÔ›ÙXÝ[ÛXÚÛ›ÝÛYÛY[ÛÛ[[ŠÛÛ[[œËš\×ØXÝ]™H‹˜XÝ]™HŠNÂ‚ˆYˆ
ÚÙ[ÛÛ[[ˆ\È[\Ù\’YÛÛ[[ˆ\È[
BˆÂˆ™]\›ˆ[ÂˆB‚ˆ˜\ˆÚ\™T\ÈH™]È\ÝÝš[™Ï‚ˆÂˆ	žÔ][ÝRY[YšY\‘›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[
ÚÙ[ÛÛ[[Š_HHÙ\ÜÚ[Û—ÝÚÙ[ˆ‚ˆNÂ‚ˆYˆ
^\™\ÐÛÛ[[ˆ\È›Ý[
BˆÂˆÚ\™T\ËY
	ŠÔ][ÝRY[YšY\‘›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[
^\™\ÐÛÛ[[Š_HTÈ•SÔˆÔ][ÝRY[YšY\‘›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[
^\™\ÐÛÛ[[Š_Hˆ›ÝÊ
JHŠNÂˆB‚ˆYˆ
™]›ÚÙYÛÛ[[ˆ\È›Ý[
BˆÂˆÚ\™T\ËY
	žÔ][ÝRY[YšY\‘›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[
™]›ÚÙYÛÛ[[Š_HTÈ•SŠNÂˆB‚ˆYˆ
XÝ]™PÛÛ[[ˆ\È›Ý[
BˆÂˆÚ\™T\ËY
	ÓÐSTÐÑJÔ][ÝRY[YšY\‘›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[
XÝ]™PÛÛ[[Š_K•QJHH•QHŠNÂˆB‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
	ˆˆ‚ˆÑSPÕÔ][ÝRY[YšY\‘›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[
\Ù\’YÛÛ[[Š_Bˆ”“ÓHÔ][ÝRY[YšY\‘›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[
Ù\ÜÚ[Û•X›J_BˆÒT‘HÜÝš[™Ë’›Ú[ŠˆS‘‹Ú\™T\Ê_BˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÙ\ÜÚ[Û—ÝÚÙ[ˆ‹ÚÙ[‹•š[J
JNÂ‚ˆ˜\ˆ˜[YHH]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂˆYˆ
˜[YH\È[˜[YHOH“[•˜[YJBˆÂˆ™]\›ˆ[ÂˆB‚ˆ™]\›ˆ˜[YH\ÈÝZY\Ù\’YÈ\Ù\’YˆÝZY”\œÙJ˜[YK•ÔÝš[™Ê
HJNÂŸB‚‹ËÈNSKPÒH›ÙXÝ[ÛˆÜ\˜][ÛœÈXÚÛ›ÝÛYÛY[È
ÈÚYÛ‹SÙ™ˆ]šY[˜ÙHHS‘‚‚‹ËÈNSKPÒˆ[YHÛÛ\X[˜ÙH]]ÛX]XÈ[™Ú[™Y\ˆ[XZ[›ÝYšXØ][ÛœÈHÕT•˜\“X\Ù]
‹Ø\KÝ[YKXÛÛ\X[˜ÙKÙ[XZ[[›ÝYšXØ][ÛœËÜÝ[[X\žH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH•[YHÛÛ\X[˜ÙH[XZ[›ÝYšXØ][ÛˆÜ\˜][ÛœÈ\™H™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆØÚY[\ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆØÚY[PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆØÚY[WÚÙ^KˆØÚY[WÛ˜[YKˆØÙ[˜\š[Ëˆ™XÚ\Y[ÙÜ›Ý\ØÛÙKˆÙ[™Ù^KˆÙ[™Ý[YWÛØØ[ˆ[Y^›Û™WÛ˜[YKˆ\×ØXÝ]™Kˆ™\]Z\™\×Ü™]šY]×Ø™Y›Ü™WÜÙ[™ˆ\ÝÜ[—Ø]ˆ™^Ü[—Ú[ˆ”“ÓH[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—ÜØÚY[WØÛÛ›ÛÂˆÔ‘Tˆ–HØÚY[WÚÙ^NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ØÚY[PÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆØÚY[\ËY
™]ÂˆÂˆØÚY[RÙ^HH™XY\‹‘Ù]Ýš[™Ê
KˆØÚY[S˜[YHH™XY\‹‘Ù]Ýš[™ÊJKˆØÙ[˜\š[ÈH™XY\‹‘Ù]Ýš[™ÊŠKˆ™XÚ\Y[Ü›Ý\ÛÙHH™XY\‹‘Ù]Ýš[™ÊÊKˆÙ[™^HH™XY\‹‘Ù]Ýš[™Ê
KˆÙ[™[YSØØ[H™XY\‹‘Ù]Ýš[™ÊJKˆ[Y^›Û™S˜[YHH™XY\‹‘Ù]Ýš[™ÊŠKˆ\ÐXÝ]™HH™XY\‹‘Ù]›ÛÛX[ŠÊKˆ™\]Z\™\Ô™]šY]Ð™Y›Ü™TÙ[™H™XY\‹‘Ù]›ÛÛX[Š
Kˆ\Ý[]H™XY\‹’\Ñ“[
JHÈ
]U[YOÊ[[ˆ™XY\‹‘Ù]]U[YJJKˆ™^[’[H™XY\‹’\Ñ“[
L
HÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊL
BˆJNÂˆBˆB‚ˆÛ™È[ÛÝ[HÂˆÛ™È]Y]YYÛÝ[HÂˆÛ™ÈÙ[ÛÝ[HÂˆÛ™È˜Z[YÛÝ[HÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÝ[[X\žPÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÓÕS•

ŠHTÈ[—ØÛÝ[ˆÓÐSTÐÑJÕSJ]Y]YYØÛÝ[
K
HTÈ]Y]YYØÛÝ[ˆÓÐSTÐÑJÕSJÙ[ØÛÝ[
K
HTÈÙ[ØÛÝ[ˆÓÐSTÐÑJÕSJ˜Z[YØÛÝ[
K
HTÈ˜Z[YØÛÝ[ˆ”“ÓH[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[œÎÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]Ý[[X\žPÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ[ÛÝ[H™XY\‹‘Ù][

NÂˆ]Y]YYÛÝ[H™XY\‹‘Ù][
JNÂˆÙ[ÛÝ[H™XY\‹‘Ù][
ŠNÂˆ˜Z[YÛÝ[H™XY\‹‘Ù][
ÊNÂˆBˆB‚ˆ˜\ˆ™XÙ[[œÈH™]È\ÝØš™XÝŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ[œÐÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[—ÚYˆ[—Ý\KˆØÙ[˜\š[Ëˆ[]™\žWÛ[ÙKˆÙYZ×ÜÝ\ˆÙYZ×Ù[™ˆ™\]Y\ÝYØžWÙ[XZ[ˆ[—ÜÝ]\ËˆÙ[™\˜]YØÛÝ[ˆ]Y]YYØÛÝ[ˆÙ[ØÛÝ[ˆ˜Z[YØÛÝ[ˆÚÚ\YØÛÝ[ˆÝ\YØ]ˆÛÛ\]YØ]ˆ[—ÛY\ÜØYÙBˆ”“ÓH[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[œÂˆÔ‘Tˆ–HÝ\YØ]TÐÂˆSRULÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ][œÐÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™XÙ[[œËY
™]ÂˆÂˆ[’YH™XY\‹‘Ù]ÝZY

Kˆ[•\HH™XY\‹‘Ù]Ýš[™ÊJKˆØÙ[˜\š[ÈH™XY\‹‘Ù]Ýš[™ÊŠKˆ[]™\žS[ÙHH™XY\‹‘Ù]Ýš[™ÊÊKˆÙYZÔÝ\H™XY\‹’\Ñ“[

HÈ
]SÛ›OÊ[[ˆ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠ
KˆÙYZÑ[™H™XY\‹’\Ñ“[
JHÈ
]SÛ›OÊ[[ˆ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠJKˆ™\]Y\ÝYžQ[XZ[H™XY\‹’\Ñ“[
ŠHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊŠKˆ[”Ý]\ÈH™XY\‹‘Ù]Ýš[™ÊÊKˆÙ[™\˜]YÛÝ[H™XY\‹‘Ù][ÌŠ
Kˆ]Y]YYÛÝ[H™XY\‹‘Ù][ÌŠJKˆÙ[ÛÝ[H™XY\‹‘Ù][ÌŠL
Kˆ˜Z[YÛÝ[H™XY\‹‘Ù][ÌŠLJKˆÚÚ\YÛÝ[H™XY\‹‘Ù][ÌŠLŠKˆÝ\Y]H™XY\‹‘Ù]]U[YJLÊKˆÛÛ\]Y]H™XY\‹’\Ñ“[
M
HÈ
]U[YOÊ[[ˆ™XY\‹‘Ù]]U[YJM
Kˆ[“Y\ÜØYÙHH™XY\‹’\Ñ“[
MJHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊMJBˆJNÂˆBˆB‚ˆ˜\ˆ[XZ[›ÝšY\ˆHÙ]›Ú™XÝ[ÙTÚ\™Y[XZ[›ÝšY\”[[YJ
NÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKPÒˆ[YHÛÛ\X[˜ÙH]]ÛX]XÈ[™Ú[™Y\ˆ[XZ[›ÝYšXØ][ÛœÈ‹ˆÝ[[X\žHH™]ÂˆÂˆ[ÛÝ[ˆ]Y]YYÛÝ[ˆÙ[ÛÝ[ˆ˜Z[YÛÝ[ˆØÚY[PÛÝ[HØÚY[\ËÛÝ[ˆXÝ]™TØÚY[PÛÝ[HØÚY[\ËÛÝ[
ÈOˆ
›ÛÛ
\Ë‘Ù]\J
K‘Ù]›Ü\Jš\ÐXÝ]™HŠHK‘Ù]˜[YJÊHJKˆ›ÝšY\ˆH[XZ[›ÝšY\‹”›ÝšY\‹ˆÙ[™\‘[XZ[H[XZ[›ÝšY\‹”Ù[™\‘[XZ[ˆÙ[™\“˜[YHH[XZ[›ÝšY\‹”Ù[™\“˜[YKˆœ™]›Ð\PÛÛ™šYÝ\™YH[XZ[›ÝšY\‹œ™]›Ð\PÛÛ™šYÝ\™YˆÙ[™XZ[]˜Z[X›HH[XZ[›ÝšY\‹”Ù[™XZ[]˜Z[X›KˆÛ]ÛÛ™šYÝ\™YH[XZ[›ÝšY\‹”Û]ÛÛ™šYÝ\™Yˆ›ØÚÓØØ[™XÚ\Y[ÈH[XZ[›ÝšY\‹›ØÚÓØØ[™XÚ\Y[Ëˆ™Y™\œ™Y[]™\žS[ÙHH[XZ[›ÝšY\‹”™Y™\œ™Y[]™\žS[ÙKˆ[]™\žT™XY[™\ÜÈH[XZ[›ÝšY\‹‘[]™\žT™XY[™\ÜÂˆKˆØÚY[\Ëˆ™XÙ[[œÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÝ[YKXÛÛ\X[˜ÙKÙ[XZ[[›ÝYšXØ][ÛœËÙ]™[È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH•[YHÛÛ\X[˜ÙH[XZ[›ÝYšXØ][Ûˆ]™[È\™H™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆ[Z]HNÂˆYˆ
[•žT\œÙJÛÛ^”™\]Y\Ý”]Y\žVÈ›[Z]—K‘š\œÝÜ‘Y˜][

KÝ]˜\ˆ\œÙY[Z]
JBˆÂˆ[Z]HX]Û[\
\œÙY[Z]KL
NÂˆB‚ˆ˜\ˆ]™[ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆK[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ù[]™\žWÙ]™[ÚYˆK[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[—ÚYˆKœ™XÚ\Y[Ù[XZ[ˆKœ™XÚ\Y[Ù\Ü^WÛ˜[YKˆK›X[˜YÙ\—Ù[XZ[ˆK˜Ø×Ù[XZ[ËˆKœÝXš™XÝˆK™[]™\žWÜÝ]\ËˆK™[]™\žWÛ[ÙKˆKœÙ[Ø]ˆK™˜Z[YØ]ˆK™˜Z[\™WÛY\ÜØYÙKˆK˜Ü™X]YØ]ˆ”“ÓH[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ù[]™\žWÙ]™[ÈBˆÔ‘Tˆ–HK˜Ü™X]YØ]TÐÂˆSRU[Z]Âˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›[Z]‹[Z]
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ]™[ËY
™]ÂˆÂˆ[]™\žQ]™[YH™XY\‹‘Ù]ÝZY

Kˆ[’YH™XY\‹‘Ù]ÝZY
JKˆ™XÚ\Y[[XZ[H™XY\‹‘Ù]Ýš[™ÊŠKˆ™XÚ\Y[\Ü^S˜[YHH™XY\‹’\Ñ“[
ÊHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊÊKˆX[˜YÙ\‘[XZ[H™XY\‹’\Ñ“[

HÈˆˆˆ™XY\‹‘Ù]Ýš[™Ê
KˆØÑ[XZ[ÈH™XY\‹’\Ñ“[
JHÈ\œ˜^K‘[\OÝš[™ÏŠ
Hˆ™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠJKˆÝXš™XÝH™XY\‹‘Ù]Ýš[™ÊŠKˆ[]™\žTÝ]\ÈH™XY\‹‘Ù]Ýš[™ÊÊKˆ[]™\žS[ÙHH™XY\‹‘Ù]Ýš[™Ê
KˆÙ[]H™XY\‹’\Ñ“[
JHÈ
]U[YOÊ[[ˆ™XY\‹‘Ù]]U[YJJKˆ˜Z[Y]H™XY\‹’\Ñ“[
L
HÈ
]U[YOÊ[[ˆ™XY\‹‘Ù]]U[YJL
Kˆ˜Z[\™SY\ÜØYÙHH™XY\‹’\Ñ“[
LJHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊLJKˆÜ™X]Y]H™XY\‹‘Ù]]U[YJLŠBˆJNÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKPÒˆ[YHÛÛ\X[˜ÙH[XZ[›ÝYšXØ][Ûˆ[]™\žH]™[È‹ˆÛÝ[H]™[ËÛÝ[ˆ]™[ÂˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÝ[YKXÛÛ\X[˜ÙKÙ[XZ[[›ÝYšXØ][ÛœËÜÙ[™‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH•[YHÛÛ\X[˜ÙH[XZ[›ÝYšXØ][ÛˆÙ[™\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ\Ú[™È˜\ˆØÝ[Y[H]ØZ]œÛÛ‘ØÝ[Y[”\œÙP\Þ[˜ÊÛÛ^”™\]Y\Ý›ÙJNÂˆ˜\ˆ›ÛÝHØÝ[Y[”›ÛÝ[[Y[Â‚ˆ˜\ˆØÙ[˜\š[ÈH›ÛÝ•žQÙ]›Ü\JœØÙ[˜\š[È‹Ý]˜\ˆØÙ[˜\š[Ñ[[Y[
BˆÈØÙ[˜\š[Ñ[[Y[‘Ù]Ýš[™Ê
BˆˆÙYZÛWÜ™[Z[™\ˆŽÂ‚ˆØÙ[˜\š[ÈHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJØÙ[˜\š[ÊHÈÙYZÛWÜ™[Z[™\ˆˆˆØÙ[˜\š[Ë•š[J
NÂ‚ˆ˜\ˆ[]™\žS[ÙHH›ÛÝ•žQÙ]›Ü\J™[]™\žS[ÙH‹Ý]˜\ˆ[ÙQ[[Y[
BˆÈ[ÙQ[[Y[‘Ù]Ýš[™Ê
Bˆˆ›Ý]›ÞÛÛ›HŽÂ‚ˆ[]™\žS[ÙHHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[]™\žS[ÙJHÈ›Ý]›ÞÛÛ›Hˆˆ[]™\žS[ÙK•š[J
NÂ‚ˆ˜\ˆÚ\™Y[XZ[›ÝšY\ˆHÙ]›Ú™XÝ[ÙTÚ\™Y[XZ[›ÝšY\”[[YJ
NÂˆYˆ
[]™\žS[ÙK‘\]X[Êœ›ÝšY\ˆ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ[]™\žS[ÙK‘\]X[Ê˜]]È‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ[]™\žS[ÙK‘\]X[Ê™Y˜][‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆ[]™\žS[ÙHHÚ\™Y[XZ[›ÝšY\‹”™Y™\œ™Y[]™\žS[ÙNÂˆB‚ˆYˆ
Y[]™\žS[ÙK‘\]X[Ê›Ý]›ÞÛÛ›H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆ˜\ˆ™XÚ\Y[ØY™]QØ]HH]ØZ]›Ú™XÝ[ÙT™XÚ\Y[ØY™]QØ]P[ÝÜÔÙ[™\Þ[˜ÊˆÛÛ›™XÝ[Û‹ˆ•SQWÐÓÓTPSÑWÑS‘ÒS‘QT—Ó“ÕQ’PÐUSÓ”È‹ˆØÙ[˜\š[Âˆ
NÂ‚ˆYˆ
\™XÚ\Y[ØY™]QØ]K[ÝÙY
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœ™XÚ\Y[ÜØY™]WÜ™]šY]×Ü™\]Z\™Y‹ˆ[Ù[HHŒŒˆÚ\™Y[XZ[™XÚ\Y[ØY™]H™]šY]È‹ˆY\ÜØYÙHH™XÚ\Y[ØY™]QØ]K”™X\ÛÛ‹ˆØÙ[˜\š[Ëˆ[]™\žS[ÙKˆ™]šY]ÒYH™XÚ\Y[ØY™]QØ]K”™]šY]ÒYˆ™^Ý\H”[ˆØ\KÜÞ\Ý[KÙ[XZ[\›ÝšY\‹Ü™XÚ\Y[\ØY™]KÜ[‹\™]šY]È[™\›Ý™HHÛX[ˆ™]šY]È™Y›Ü™H™X[›ÝšY\ˆÙ[™ˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍPÛÛ™›XÝ
NÂˆBˆB‚ˆ˜\ˆ[•\HH›ÛÝ•žQÙ]›Ü\Jœ[•\H‹Ý]˜\ˆ[•\Q[[Y[
BˆÈ[•\Q[[Y[‘Ù]Ýš[™Ê
Bˆˆ›X[X[ŽÂ‚ˆ[•\HHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[•\JHÈ›X[X[ˆˆ[•\K•š[J
NÂ‚ˆ]SÛ›OÈÙYZÔÝ\H[Âˆ]SÛ›OÈÙYZÑ[™H[Â‚ˆYˆ
›ÛÝ•žQÙ]›Ü\JÙYZÔÝ\‹Ý]˜\ˆÙYZÔÝ\[[Y[
Bˆ	‰ˆ]SÛ›K•žT\œÙJÙYZÔÝ\[[Y[‘Ù]Ýš[™Ê
KÝ]˜\ˆ\œÙYÙYZÔÝ\
JBˆÂˆÙYZÔÝ\H\œÙYÙYZÔÝ\ÂˆÙYZÑ[™H\œÙYÙYZÔÝ\Y^\ÊŠNÂˆB‚ˆ˜\ˆXÝÜ•\Ù\’YH]ØZ]™\ÛÛ™TÙ\ÜÚ[Û•\Ù\’Y›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆ˜\ˆXÝÜ‘[XZ[HˆŽÂ‚ˆYˆ
XÝÜ•\Ù\’Y\È›Ý[
BˆÂˆ]ØZ]\Ú[™È˜\ˆ\Ù\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ[XZ[ˆ”“ÓH\Ý\Ù\œÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ\Ù\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹XÝÜ•\Ù\’Y•˜[YJNÂˆXÝÜ‘[XZ[H
]ØZ]\Ù\ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
JOË•ÔÝš[™Ê
HÏÈˆŽÂˆB‚ˆ˜\ˆ™]šY]Õ\›H‹Ø\KÝ[YKXÛÛ\X[˜ÙKÜ™]šY]ÏÜØÙ[˜\š[ÏHˆ
È\šK‘\ØØ\Q]TÝš[™ÊØÙ[˜\š[ÊNÂˆYˆ
ÙYZÔÝ\\È›Ý[
BˆÂˆ™]šY]Õ\›
ÏH‰ÙYZÔÝ\Hˆ
È\šK‘\ØØ\Q]TÝš[™ÊÙYZÔÝ\•˜[YK•ÔÝš[™Êž^^^KSSKYŠJNÂˆB‚ˆÊˆÑPÕT’UWÌŒŒÌŽWÑ’VQÒS•T“SÐTWÓÔ’QÒSˆ
‹Âˆ˜\ˆ[\›˜[\P˜\ÙU\›H[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÒS•T“SÐTWÐTÑWÕT“ŠBˆÏÈš‹ËÌLËŒŒŒNLŽÂ‚ˆYˆ
U\šK•žPÜ™X]J[\›˜[\P˜\ÙU\›\šRÚ[™XœÛÛ]KÝ]˜\ˆ[\›˜[\P˜\ÙU\šJBˆ
[\›˜[\P˜\ÙU\šK”ØÚ[YHOH\šK•\šTØÚ[YR	‰ˆ[\›˜[\P˜\ÙU\šK”ØÚ[YHOH\šK•\šTØÚ[YRÊBˆ\Ýš[™Ë’\Ó[Ü‘[\J[\›˜[\P˜\ÙU\šK•\Ù\’[™›ÊJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHš[\›˜[Ø\WÛÜšYÚ[—Ú[˜[Y‹ˆY\ÜØYÙHH•HÛÛ™šYÝ\™Y[\›˜[THÜšYÚ[ˆ\È[˜[Yˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍLÔÙ\šXÙU[˜]˜Z[X›JNÂˆB‚ˆ\Ú[™È˜\ˆÛY[H™]ÈÛY[

NÂˆÛY[‘Y˜][™\]Y\ÝXY\œËY
–T›Ú™XÝ[ÙKTÙ\ÜÚ[Ûˆ‹ÛÛ^”™\]Y\Ý’XY\œÖÈ–T›Ú™XÝ[ÙKTÙ\ÜÚ[Ûˆ—K‘š\œÝÜ‘Y˜][

HÏÈˆŠNÂ‚ˆ˜\ˆ™]šY]ÒœÛÛˆH]ØZ]ÛY[‘Ù]Ýš[™Ð\Þ[˜Ê™]È\šJ[\›˜[\P˜\ÙU\šK™]šY]Õ\›
JNÂˆ\Ú[™È˜\ˆ™]šY]ÑØÝ[Y[HœÛÛ‘ØÝ[Y[”\œÙJ™]šY]ÒœÛÛŠNÂˆ˜\ˆ™]šY]Ô›ÛÝH™]šY]ÑØÝ[Y[”›ÛÝ[[Y[Â‚ˆ˜\ˆZ\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœÈH™]šY]Ô›ÛÝ•žQÙ]›Ü\J›Z\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœÈ‹Ý]˜\ˆZ\ÜÚ[™Ñ[[Y[
Bˆ	‰ˆZ\ÜÚ[™Ñ[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™\œ˜^BˆÈZ\ÜÚ[™Ñ[[Y[‘[[Y\˜]P\œ˜^J
K•Ó\Ý

Bˆˆ™]È\ÝœÛÛ‘[[Y[Š
NÂ‚ˆÝZY[’YÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ[ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[œÈ
ˆ[—Ý\KˆØÙ[˜\š[Ëˆ[]™\žWÛ[ÙKˆÙYZ×ÜÝ\ˆÙYZ×Ù[™ˆ™\]Y\ÝYØžWÝ\Ù\—ÚYˆ™\]Y\ÝYØžWÙ[XZ[ˆ[—ÜÝ]\ËˆÙ[™\˜]YØÛÝ[ˆ™]šY]×ÜÛ˜\ÚÝˆ
BˆSQTÈ
ˆ[—Ý\KˆØÙ[˜\š[Ëˆ[]™\žWÛ[ÙKˆÙYZ×ÜÝ\ˆÙYZ×Ù[™ˆ™\]Y\ÝYØžWÝ\Ù\—ÚYˆ™\]Y\ÝYØžWÙ[XZ[ˆ	Ü[›š[™ÉËˆÙ[™\˜]YØÛÝ[ˆ™]šY]×ÜÛ˜\ÚÝŽšœÛÛ˜‚ˆ
Bˆ‘UT“’S‘È[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[—ÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ[—Ý\H‹[•\JNÂˆ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœØÙ[˜\š[È‹ØÙ[˜\š[ÊNÂˆ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]™\žWÛ[ÙH‹[]™\žS[ÙJNÂˆ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹ÙYZÔÝ\\È[È“[•˜[YHˆÙYZÔÝ\•˜[YJNÂˆ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹ÙYZÑ[™\È[È“[•˜[YHˆÙYZÑ[™•˜[YJNÂˆ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™\]Y\ÝYØžWÝ\Ù\—ÚY‹XÝÜ•\Ù\’Y\È[È“[•˜[YHˆXÝÜ•\Ù\’Y•˜[YJNÂˆ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™\]Y\ÝYØžWÙ[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÝÜ‘[XZ[
HÈ“[•˜[YHˆXÝÜ‘[XZ[
NÂˆ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™Ù[™\˜]YØÛÝ[‹Z\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœËÛÝ[
NÂˆ[ÛÛ[X[™”\˜[Y]\œËY
™]ÈœÜÜ[\˜[Y]\Šœ™]šY]×ÜÛ˜\ÚÝ‹œÜÜ[\\Ë“œÜÜ[•\K’œÛÛ˜ŠBˆÂˆ˜[YHH™]šY]ÒœÛÛ‚ˆJNÂ‚ˆ[’YH
ÝZY
J]ØZ][ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈÝZY‘[\JNÂˆB‚ˆ˜\ˆ]Y]YYÛÝ[HÂˆ˜\ˆÚÚ\YÛÝ[HÂˆ˜\ˆÙ[ÛÝ[HÂˆ˜\ˆ˜Z[YÛÝ[HÂ‚ˆ›Ü™XXÚ
˜\ˆÝX›Z\ÜÚ[Ûˆ[ˆZ\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœÊBˆÂˆ˜\ˆ™XÚ\Y[[XZ[HÝX›Z\ÜÚ[Û‹•žQÙ]›Ü\J™[XZ[‹Ý]˜\ˆ[XZ[[[Y[
HÈ[XZ[[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆ™XÚ\Y[˜[YHHÝX›Z\ÜÚ[Û‹•žQÙ]›Ü\J™\Ü^S˜[YH‹Ý]˜\ˆ˜[YQ[[Y[
HÈ˜[YQ[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆX[˜YÙ\‘[XZ[HÝX›Z\ÜÚ[Û‹•žQÙ]›Ü\J›X[˜YÙ\‘[XZ[‹Ý]˜\ˆX[˜YÙ\‘[[Y[
HÈX[˜YÙ\‘[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆÝXš™XÝHÝX›Z\ÜÚ[Û‹•žQÙ]›Ü\JœÝXš™XÝ‹Ý]˜\ˆÝXš™XÝ[[Y[
HÈÝXš™XÝ[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆ”›Ú™XÝ[ÙH[YHÛÛ\X[˜ÙH™[Z[™\ˆŽÂˆ˜\ˆ›ÙHHÝX›Z\ÜÚ[Û‹•žQÙ]›Ü\J˜›ÙH‹Ý]˜\ˆ›ÙQ[[Y[
HÈ›ÙQ[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂ‚ˆ˜\ˆØÑ[XZ[ÈH™]È\ÝÝš[™ÏŠ
NÂˆYˆ
ÝX›Z\ÜÚ[Û‹•žQÙ]›Ü\J˜ØÑ[XZ[È‹Ý]˜\ˆØÑ[[Y[
H	‰ˆØÑ[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™\œ˜^JBˆÂˆ›Ü™XXÚ
˜\ˆØÈ[ˆØÑ[[Y[‘[[Y\˜]P\œ˜^J
JBˆÂˆ˜\ˆØÕ˜[YHHØË‘Ù]Ýš[™Ê
NÂˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJØÕ˜[YJJBˆÂˆØÑ[XZ[ËY
ØÕ˜[YK•š[J
JNÂˆBˆBˆB‚ˆÝZYÈ\Ù\’YH[ÂˆYˆ
ÝX›Z\ÜÚ[Û‹•žQÙ]›Ü\J\Ù\’Y‹Ý]˜\ˆ\Ù\’Y[[Y[
Bˆ	‰ˆÝZY•žT\œÙJ\Ù\’Y[[Y[‘Ù]Ýš[™Ê
KÝ]˜\ˆ\œÙY\Ù\’Y
JBˆÂˆ\Ù\’YH\œÙY\Ù\’YÂˆB‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[[XZ[
JBˆÂˆÚÚ\YÛÝ[
ÊÎÂˆÛÛ[YNÂˆB‚ˆ˜\ˆÝ]\ÈH[]™\žS[ÙK‘\]X[Ê›Ý]›ÞÛÛ›H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJHÈ›Ý]›ÞÛÛ›Hˆˆœ]Y]YYŽÂˆ˜\ˆ˜Z[\™SY\ÜØYÙHHˆŽÂ‚ˆYˆ
[]™\žS[ÙK‘\]X[Ê›Ý]›ÞÛÛ›H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆ]Y]YYÛÝ[
ÊÎÂˆBˆ[ÙBˆÂˆ˜\ˆ[]™\žT™\Ý[H]ØZ]Ù[™›Ú™XÝ[ÙQ[XZ[›ÝYÚÚ\™Y›ÝšY\\Þ[˜Êˆ[]™\žS[ÙKˆ™XÚ\Y[[XZ[ˆ™XÚ\Y[˜[YKˆØÑ[XZ[ËˆÝXš™XÝˆ›ÙBˆ
NÂ‚ˆÝ]\ÈH[]™\žT™\Ý[”Ý]\ÎÂˆ˜Z[\™SY\ÜØYÙHH[]™\žT™\Ý[‘˜Z[\™SY\ÜØYÙNÂ‚ˆYˆ
Ý]\Ë‘\]X[ÊœÙ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆÙ[ÛÝ[
ÊÎÂˆBˆ[ÙHYˆ
Ý]\Ë‘\]X[Ê™˜Z[Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆ˜Z[YÛÝ[
ÊÎÂˆBˆ[ÙHYˆ
Ý]\Ë‘\]X[ÊœÚÚ\Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆÚÚ\YÛÝ[
ÊÎÂˆBˆ[ÙBˆÂˆ]Y]YYÛÝ[
ÊÎÂˆBˆB‚ˆ]ØZ]\Ú[™È˜\ˆ]™[ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ù[]™\žWÙ]™[È
ˆ[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[—ÚYˆ™XÚ\Y[Ý\Ù\—ÚYˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Ù\Ü^WÛ˜[YKˆX[˜YÙ\—Ù[XZ[ˆØ×Ù[XZ[ËˆÝXš™XÝˆ›ÙKˆ[]™\žWÜÝ]\Ëˆ[]™\žWÛ[ÙKˆÙ[Ø]ˆ˜Z[YØ]ˆ˜Z[\™WÛY\ÜØYÙBˆ
BˆSQTÈ
ˆ[—ÚYˆ™XÚ\Y[Ý\Ù\—ÚYˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Ù\Ü^WÛ˜[YKˆX[˜YÙ\—Ù[XZ[ˆØ×Ù[XZ[ËˆÝXš™XÝˆ›ÙKˆ[]™\žWÜÝ]\Ëˆ[]™\žWÛ[ÙKˆÐTÑHÒSˆ[]™\žWÜÝ]\ÈH	ÜÙ[	ÈSˆ›ÝÊ
HSÑH•SS‘ˆÐTÑHÒSˆ[]™\žWÜÝ]\ÈH	Ù˜Z[Y	ÈSˆ›ÝÊ
HSÑH•SS‘ˆ˜Z[\™WÛY\ÜØYÙBˆ
NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ[—ÚY‹[’Y
NÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ý\Ù\—ÚY‹\Ù\’Y\È[È“[•˜[YHˆ\Ù\’Y•˜[YJNÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ù[XZ[‹™XÚ\Y[[XZ[•š[J
JNÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ù\Ü^WÛ˜[YH‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[˜[YJHÈ“[•˜[YHˆ™XÚ\Y[˜[YK•š[J
JNÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›X[˜YÙ\—Ù[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJX[˜YÙ\‘[XZ[
HÈ“[•˜[YHˆX[˜YÙ\‘[XZ[•š[J
JNÂˆ]™[ÛÛ[X[™”\˜[Y]\œËY
™]ÈœÜÜ[\˜[Y]\Š˜Ø×Ù[XZ[È‹œÜÜ[\\Ë“œÜÜ[•\K\œ˜^HœÜÜ[\\Ë“œÜÜ[•\K•^
BˆÂˆ˜[YHHØÑ[XZ[Ë•Ð\œ˜^J
BˆJNÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÝXš™XÝ‹ÝXš™XÝ
NÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜›ÙH‹›ÙJNÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]™\žWÜÝ]\È‹Ý]\ÊNÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]™\žWÛ[ÙH‹[]™\žS[ÙJNÂˆ]™[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™˜Z[\™WÛY\ÜØYÙH‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜Z[\™SY\ÜØYÙJHÈ“[•˜[YHˆ˜Z[\™SY\ÜØYÙJNÂ‚ˆ]ØZ]]™[ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ]ØZ]\Ú[™È
˜\ˆ\]T[ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆTUH[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[œÂˆÑUˆ[—ÜÝ]\ÈHÐTÑHÒSˆ˜Z[YØÛÝ[ˆSˆ	ØÛÛ\]YÝÚ]Ù\œ›ÜœÉÈSÑH	ØÛÛ\]Y	ÈS‘ˆ]Y]YYØÛÝ[H]Y]YYØÛÝ[ˆÙ[ØÛÝ[HÙ[ØÛÝ[ˆ˜Z[YØÛÝ[H˜Z[YØÛÝ[ˆÚÚ\YØÛÝ[HÚÚ\YØÛÝ[ˆÛÛ\]YØ]H›ÝÊ
Kˆ[—ÛY\ÜØYÙHH[—ÛY\ÜØYÙBˆÒT‘H[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ü[—ÚYH[—ÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ\]T[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ[—ÚY‹[’Y
NÂˆ\]T[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ]Y]YYØÛÝ[‹]Y]YYÛÝ[
NÂˆ\]T[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÙ[ØÛÝ[‹Ù[ÛÝ[
NÂˆ\]T[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™˜Z[YØÛÝ[‹˜Z[YÛÝ[
NÂˆ\]T[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÚÚ\YØÛÝ[‹ÚÚ\YÛÝ[
NÂˆ\]T[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ[—ÛY\ÜØYÙH‹ˆ[]™\žS[ÙK‘\]X[Ê›Ý]›ÞÛÛ›H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÈ“›ÝYšXØ][Ûˆ[ˆ™XÛÜ™Y[ˆÝ]›Þ[Û›H[ÙKˆ›È[XZ[Ø\ÈÙ[ˆ‚ˆˆ	]]ÛX]XÈ[™Ú[™Y\ˆ›ÝYšXØ][ÛˆÙ[™][\Y›ÝYÚÚ\™Y›Ú™XÝ[ÙH[XZ[›ÝšY\ŽˆÙ[]™\žS[Ù_KˆŠNÂˆ]ØZ]\]T[ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKPÒˆ[YHÛÛ\X[˜ÙH]]ÛX]XÈ[™Ú[™Y\ˆ[XZ[›ÝYšXØ][ÛœÈ‹ˆÝ]\ÈH˜ÛÛ\]Y‹ˆ[’YˆØÙ[˜\š[Ëˆ[]™\žS[ÙKˆÙ[™\˜]YÛÝ[HZ\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœËÛÝ[ˆ]Y]YYÛÝ[ˆÙ[ÛÝ[ˆ˜Z[YÛÝ[ˆÚÚ\YÛÝ[ˆY\ÜØYÙHH[]™\žS[ÙK‘\]X[Ê›Ý]›ÞÛÛ›H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÈ]]ÛX]XÈ[™Ú[™Y\ˆ›ÝYšXØ][Ûˆ[ˆØ\È™XÛÜ™Y[ˆÝ]›Þ[Û›H[ÙKˆ›È[XZ[Ø\ÈÙ[ˆ‚ˆˆ	]]ÛX]XÈ[™Ú[™Y\ˆ›ÝYšXØ][ÛˆÙ[™ÛÛ\]Y›ÝYÚÚ\™Y›Ú™XÝ[ÙH[XZ[›ÝšY\ŽˆÙ[]™\žS[Ù_Kˆ‚ˆJNÂŸJNÂ‹ËÈNSKPÒˆ[YHÛÛ\X[˜ÙH]]ÛX]XÈ[™Ú[™Y\ˆ[XZ[›ÝYšXØ][ÛœÈHS‘‚‚‹ËÈNSKPÒÈÚ\™Y›Ú™XÝ[ÙH[XZ[›ÝšY\ˆHÕT•˜\“X\Ù]
‹Ø\KÜÞ\Ý[KÙ[XZ[\›ÝšY\‹ÜÝ[[X\žH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH”Ú\™Y[XZ[›ÝšY\ˆÙ][™ÜÈ\™H™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆ›ÝšY\ˆHÙ]›Ú™XÝ[ÙTÚ\™Y[XZ[›ÝšY\”[[YJ
NÂˆ˜\ˆÛÛœÝ[Y\œÈH™]È\ÝØš™XÝŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛœÝ[Y\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÛÛœÝ[Y\—ÚÙ^KˆÛÛœÝ[Y\—Û˜[YKˆÛÛœÝ[Y\—Ù\ØÜš\[Û‹ˆÝÛš[™×Ü›Ý]Kˆ™\]Z\™YÜ\›Z\ÜÚ[ÛœËˆ^XÝYÙ[]™\žWÛ[Ù\Ëˆ\×ØXÝ]™Kˆ\]YØ]ˆ”“ÓHÞ\Ý[WÙ[XZ[Ü›ÝšY\—ØÛÛœÝ[Y\œÂˆÔ‘Tˆ–HÛÛœÝ[Y\—ÚÙ^NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛœÝ[Y\ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÛÛœÝ[Y\œËY
™]ÂˆÂˆÛÛœÝ[Y\’Ù^HH™XY\‹‘Ù]Ýš[™Ê
KˆÛÛœÝ[Y\“˜[YHH™XY\‹‘Ù]Ýš[™ÊJKˆÛÛœÝ[Y\‘\ØÜš\[ÛˆH™XY\‹‘Ù]Ýš[™ÊŠKˆÝÛš[™Ô›Ý]HH™XY\‹’\Ñ“[
ÊHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊÊKˆ™\]Z\™Y\›Z\ÜÚ[ÛœÈH™XY\‹’\Ñ“[

HÈ\œ˜^K‘[\OÝš[™ÏŠ
Hˆ™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠ
Kˆ^XÝY[]™\žS[Ù\ÈH™XY\‹’\Ñ“[
JHÈ\œ˜^K‘[\OÝš[™ÏŠ
Hˆ™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠJKˆ\ÐXÝ]™HH™XY\‹‘Ù]›ÛÛX[ŠŠKˆ\]Y]H™XY\‹‘Ù]]U[YJÊBˆJNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKPÒÈÚ\™Y›Ú™XÝ[ÙH[XZ[›ÝšY\ˆ‹ˆÝ[[X\žHH™]ÂˆÂˆ›ÝšY\ˆH›ÝšY\‹”›ÝšY\‹ˆÙ[™\‘[XZ[H›ÝšY\‹”Ù[™\‘[XZ[ˆÙ[™\“˜[YHH›ÝšY\‹”Ù[™\“˜[YKˆœ™]›Ð\PÛÛ™šYÝ\™YH›ÝšY\‹œ™]›Ð\PÛÛ™šYÝ\™YˆÙ[™XZ[]˜Z[X›HH›ÝšY\‹”Ù[™XZ[]˜Z[X›KˆÛ]ÛÛ™šYÝ\™YH›ÝšY\‹”Û]ÛÛ™šYÝ\™Yˆ›ØÚÓØØ[™XÚ\Y[ÈH›ÝšY\‹›ØÚÓØØ[™XÚ\Y[Ëˆ™Y™\œ™Y[]™\žS[ÙHH›ÝšY\‹”™Y™\œ™Y[]™\žS[ÙKˆ[]™\žT™XY[™\ÜÈH›ÝšY\‹‘[]™\žT™XY[™\ÜËˆÛÛœÝ[Y\ÛÝ[HÛÛœÝ[Y\œËÛÝ[ˆXÝ]™PÛÛœÝ[Y\ÛÝ[HÛÛœÝ[Y\œËÛÝ[
ÈOˆ
›ÛÛ
XË‘Ù]\J
K‘Ù]›Ü\Jš\ÐXÝ]™HŠHK‘Ù]˜[YJÊHJBˆKˆÛÛœÝ[Y\œÂˆJNÂŸJNÂ‚œÝ]XÈ
ˆÝš[™È›ÝšY\‹ˆÝš[™ÈÙ[™\‘[XZ[ˆÝš[™ÈÙ[™\“˜[YKˆ›ÛÛœ™]›Ð\PÛÛ™šYÝ\™Yˆ›ÛÛÙ[™XZ[]˜Z[X›Kˆ›ÛÛÛ]ÛÛ™šYÝ\™Yˆ›ÛÛ›ØÚÓØØ[™XÚ\Y[ËˆÝš[™È™Y™\œ™Y[]™\žS[ÙKˆÝš[™È[]™\žT™XY[™\ÜÂŠHÙ]›Ú™XÝ[ÙTÚ\™Y[XZ[›ÝšY\”[[YJ
BžÂˆ˜\ˆÛÛ™šYÝ\™Y›ÝšY\ˆH
[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÑSPRSÔ“Õ’QTˆŠHÏÈ›Ý]›ÞÛÛ›HŠK•š[J
K•ÓÝÙ\’[˜\šX[

NÂ‚ˆ˜\ˆœ™]›Ð\RÙ^HH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐ”‘U“×ÐTWÒÑVHŠHÏÈˆŽÂˆ˜\ˆœ™]›ÔÙ[™\‘[XZ[Bˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐ”‘U“×ÔÑS‘T—ÑSPRSŠHÏÂˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÑSPRSÑQUSÔÑS‘T—ÑSPRSŠHÏÂˆˆŽÂ‚ˆ˜\ˆœ™]›ÔÙ[™\“˜[YHBˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐ”‘U“×ÔÑS‘T—ÓSQHŠHÏÂˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÑSPRSÑQUSÔÑS‘T—ÓSQHŠHÏÂˆ”›Ú™XÝ[ÙHŽÂ‚ˆ˜\ˆÙ[™XZ[]˜Z[X›HHš[K‘^\ÝÊ‹Ý\Ü‹ÜØš[‹ÜÙ[™XZ[ŠHš[K‘^\ÝÊ‹Ý\Ü‹ÛX‹ÜÙ[™XZ[ŠNÂ‚ˆ˜\ˆÛ]ÛÛ™šYÝ\™YBˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÓUÒÔÕŠJHˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÓUÒÔÕŠJNÂ‚ˆ˜\ˆœ™]›Ð\PÛÛ™šYÝ\™YBˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJœ™]›Ð\RÙ^JH	‰‚ˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJœ™]›ÔÙ[™\‘[XZ[
NÂ‚ˆYˆ
ÛÛ™šYÝ\™Y›ÝšY\ˆOH˜œ™]›ÈˆÛÛ™šYÝ\™Y›ÝšY\ˆOH˜œ™]›×Ø\HŠBˆÂˆÛÛ™šYÝ\™Y›ÝšY\ˆH˜œ™]›×Ø\HŽÂˆB‚ˆÝš[™È™Y™\œ™Y[]™\žS[ÙNÂˆYˆ
ÛÛ™šYÝ\™Y›ÝšY\ˆOH˜œ™]›×Ø\Hˆ	‰ˆœ™]›Ð\PÛÛ™šYÝ\™Y
BˆÂˆ™Y™\œ™Y[]™\žS[ÙHH˜œ™]›×Ø\HŽÂˆBˆ[ÙHYˆ
ÛÛ™šYÝ\™Y›ÝšY\ˆOHœÙ[™XZ[ˆ	‰ˆÙ[™XZ[]˜Z[X›JBˆÂˆ™Y™\œ™Y[]™\žS[ÙHHœÙ[™XZ[ŽÂˆBˆ[ÙHYˆ
ÛÛ™šYÝ\™Y›ÝšY\ˆOHœÛ]ˆ	‰ˆÛ]ÛÛ™šYÝ\™Y
BˆÂˆ™Y™\œ™Y[]™\žS[ÙHHœÛ]ŽÂˆBˆ[ÙHYˆ
œ™]›Ð\PÛÛ™šYÝ\™Y
BˆÂˆ™Y™\œ™Y[]™\žS[ÙHH˜œ™]›×Ø\HŽÂˆÛÛ™šYÝ\™Y›ÝšY\ˆH˜œ™]›×Ø\HŽÂˆBˆ[ÙHYˆ
Ù[™XZ[]˜Z[X›JBˆÂˆ™Y™\œ™Y[]™\žS[ÙHHœÙ[™XZ[ŽÂˆBˆ[ÙBˆÂˆ™Y™\œ™Y[]™\žS[ÙHH›Ý]›ÞÛÛ›HŽÂˆB‚ˆ˜\ˆ›ØÚÓØØ[™XÚ\Y[ÈH\Ýš[™Ë‘\]X[Êˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÑSPRSÐ“ÐÒ×ÓÐÐSÔ‘PÒTQS•ÈŠKˆ™˜[ÙH‹ˆÝš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙBˆ
NÂ‚ˆ™]\›ˆ
ˆÛÛ™šYÝ\™Y›ÝšY\‹ˆœ™]›ÔÙ[™\‘[XZ[ˆœ™]›ÔÙ[™\“˜[YKˆœ™]›Ð\PÛÛ™šYÝ\™YˆÙ[™XZ[]˜Z[X›KˆÛ]ÛÛ™šYÝ\™Yˆ›ØÚÓØØ[™XÚ\Y[Ëˆ™Y™\œ™Y[]™\žS[ÙKˆ™Y™\œ™Y[]™\žS[ÙHOH›Ý]›ÞÛÛ›HˆÈ›Ý]›ÞÛÛ›Hˆˆœ™XYH‚ˆ
NÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙQ[XZ[™XÚ\Y[ÚÝ[™TÚÚ\Y
Ýš[™È[XZ[
BžÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[XZ[
JBˆÂˆ™]\›ˆYNÂˆB‚ˆ˜\ˆ›ÝšY\ˆHÙ]›Ú™XÝ[ÙTÚ\™Y[XZ[›ÝšY\”[[YJ
NÂ‚ˆYˆ
\›ÝšY\‹›ØÚÓØØ[™XÚ\Y[ÊBˆÂˆ™]\›ˆ˜[ÙNÂˆB‚ˆ™]\›ˆ[XZ[‘[™ÕÚ]
‹›ØØ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ
Ýš[™ÈÝ]\ËÝš[™È˜Z[\™SY\ÜØYÙJOˆÙ[™›Ú™XÝ[ÙQ[XZ[›ÝYÚÚ\™Y›ÝšY\\Þ[˜ÊˆÝš[™È[]™\žS[ÙKˆÝš[™È™XÚ\Y[[XZ[ˆÝš[™È™XÚ\Y[˜[YKˆT™XYÛ›PÛÛXÝ[ÛÝš[™ÏˆØÑ[XZ[ËˆÝš[™ÈÝXš™XÝˆÝš[™È›ÙJBžÂˆ˜\ˆ›ÝšY\ˆHÙ]›Ú™XÝ[ÙTÚ\™Y[XZ[›ÝšY\”[[YJ
NÂ‚ˆ˜\ˆ™\ÛÛ™Y[ÙHHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[]™\žS[ÙJBˆÈ›ÝšY\‹”™Y™\œ™Y[]™\žS[ÙBˆˆ[]™\žS[ÙK•š[J
K•ÓÝÙ\’[˜\šX[

NÂ‚ˆYˆ
™\ÛÛ™Y[ÙHOHœ›ÝšY\ˆˆ™\ÛÛ™Y[ÙHOH˜]]Èˆ™\ÛÛ™Y[ÙHOH™Y˜][ŠBˆÂˆ™\ÛÛ™Y[ÙHH›ÝšY\‹”™Y™\œ™Y[]™\žS[ÙNÂˆB‚ˆYˆ
™\ÛÛ™Y[ÙHOH›Ý]›ÞÛÛ›HŠBˆÂˆ™]\›ˆ
›Ý]›ÞÛÛ›H‹ˆŠNÂˆB‚ˆYˆ
›Ú™XÝ[ÙQ[XZ[™XÚ\Y[ÚÝ[™TÚÚ\Y
™XÚ\Y[[XZ[
JBˆÂˆ™]\›ˆ
œÚÚ\Y‹”ÚÚ\Y›Û‹\›Ý]X›HÜˆ[\H™XÚ\Y[™XØ]\ÙHHÚ\™Y[XZ[›ÝšY\ˆ\ÈÛÛ™šYÝ\™YÈ›ØÚÈØØ[Ý\Ý™XÚ\Y[ËˆŠNÂˆB‚ˆ˜\ˆš[\™YØÑ[XZ[ÈHØÑ[XZ[Âˆ•Ú\™JØÈOˆT›Ú™XÝ[ÙQ[XZ[™XÚ\Y[ÚÝ[™TÚÚ\Y
ØÊJBˆ”Ù[XÝ
ØÈOˆØË•š[J
JBˆ•Ú\™JØÈOˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJØÊJBˆ‘\Ý[˜Ý
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ•Ð\œ˜^J
NÂ‚ˆYˆ
™\ÛÛ™Y[ÙHOH˜œ™]›×Ø\HŠBˆÂˆYˆ
\›ÝšY\‹œ™]›Ð\PÛÛ™šYÝ\™Y
BˆÂˆ™]\›ˆ
™˜Z[Y‹œ™]›ÈTH›ÝšY\ˆ\ÈÙ[XÝY]“Ò‘PÕSÑWÐ”‘U“×ÐTWÒÑVHÜˆÙ[™\ˆ[XZ[\È›ÝÛÛ™šYÝ\™YˆŠNÂˆB‚ˆžBˆÂˆ˜\ˆœ™]›Ð\RÙ^HH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐ”‘U“×ÐTWÒÑVHŠHÏÈˆŽÂ‚ˆ˜\ˆ[XZ[›ÙHH›ÙK”™\XÙJˆ“›ÝYšXØ][Ûˆ™]šY]ÈÛ›Kˆ›È[XZ[Ø\ÈÙ[ˆ‹ˆ]]ÛX]XÈ›Ú™XÝ[ÙH[YKXÛÛ\X[˜ÙH›ÝYšXØ][Û‹ˆ‚ˆ
NÂ‚ˆ˜\ˆœ™]›Ô^[ØYH™]ÂˆÂˆÙ[™\ˆH™]ÂˆÂˆ˜[YHH›ÝšY\‹”Ù[™\“˜[YKˆ[XZ[H›ÝšY\‹”Ù[™\‘[XZ[ˆKˆÈH™]Ö×BˆÂˆ™]ÂˆÂˆ[XZ[H™XÚ\Y[[XZ[•š[J
Kˆ˜[YHHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[˜[YJHÈ™XÚ\Y[[XZ[•š[J
Hˆ™XÚ\Y[˜[YK•š[J
BˆBˆKˆØÈHš[\™YØÑ[XZ[Ë”Ù[XÝ
ØÈOˆ™]ÈÈ[XZ[HØÈJK•Ð\œ˜^J
KˆÝXš™XÝˆ^ÛÛ[H[XZ[›ÙBˆNÂ‚ˆ\Ú[™È˜\ˆœ™]›ÐÛY[H™]ÈÛY[

NÂˆ\Ú[™È˜\ˆœ™]›Ô™\]Y\ÝH™]È™\]Y\ÝY\ÜØYÙJY]Ù”ÜÝšÎ‹ËØ\K˜œ™]›Ë˜ÛÛKÝŒËÜÛ]Ù[XZ[ŠNÂˆœ™]›Ô™\]Y\Ý’XY\œËY
˜XØÙ\‹˜\XØ][Û‹ÚœÛÛˆŠNÂˆœ™]›Ô™\]Y\Ý’XY\œËY
˜\KZÙ^H‹œ™]›Ð\RÙ^JNÂˆœ™]›Ô™\]Y\ÝÛÛ[H™]ÈÝš[™ÐÛÛ[
ˆœÛÛ”Ù\šX[^™\‹”Ù\šX[^™Jœ™]›Ô^[ØY
Kˆ[˜ÛÙ[™Ë•UŽˆ˜\XØ][Û‹ÚœÛÛˆ‚ˆ
NÂ‚ˆ\Ú[™È˜\ˆœ™]›Ô™\ÜÛœÙHH]ØZ]œ™]›ÐÛY[”Ù[™\Þ[˜Êœ™]›Ô™\]Y\Ý
NÂˆ˜\ˆœ™]›Ô™\ÜÛœÙP›ÙHH]ØZ]œ™]›Ô™\ÜÛœÙKÛÛ[”™XY\ÔÝš[™Ð\Þ[˜Ê
NÂ‚ˆYˆ
œ™]›Ô™\ÜÛœÙK’\ÔÝXØÙ\ÜÔÝ]\ÐÛÙJBˆÂˆ™]\›ˆ
œÙ[‹ˆŠNÂˆB‚ˆ™]\›ˆ
™˜Z[Y‹	œ™]›ÈTHÊ[
Xœ™]›Ô™\ÜÛœÙK”Ý]\ÐÛÙ_NˆØœ™]›Ô™\ÜÛœÙP›Ù_HŠNÂˆBˆØ]Ú
^Ù\[Ûˆ^
BˆÂˆ™]\›ˆ
™˜Z[Y‹^“Y\ÜØYÙJNÂˆBˆB‚ˆYˆ
™\ÛÛ™Y[ÙHOHœÛ]ŠBˆÂˆ™]\›ˆ
™˜Z[Y‹”ÓU[]™\žH[ÙH\È™\Ù\™Y›ÜˆH]\™H›ÝšY\ˆY\\‹ˆÛÛ™šYÝ\™Hœ™]›ÈTH\ÈHÚ\™Y›ÝšY\ˆ›ÜˆÝ\œ™[›ÙXÝ[Ûˆ[XZ[[]™\žKˆŠNÂˆB‚ˆ™]\›ˆ
›Ý]›ÞÛÛ›H‹ˆŠNÂŸB‹ËÈNSKPÒÈÚ\™Y›Ú™XÝ[ÙH[XZ[›ÝšY\ˆHS‘‚‚‹ËÈNSKPÓÚ\™Y[XZ[›ÝšY\ˆ\Ý\›™\ÜÈHÕT•˜\“X\Ù]
‹Ø\KÜÞ\Ý[KÙ[XZ[\›ÝšY\‹Ý\ÝY]™[È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH”Ú\™Y[XZ[›ÝšY\ˆ\Ý]™[È\™H™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆ[Z]HNÂˆYˆ
[•žT\œÙJÛÛ^”™\]Y\Ý”]Y\žVÈ›[Z]—K‘š\œÝÜ‘Y˜][

KÝ]˜\ˆ\œÙY[Z]
JBˆÂˆ[Z]HX]Û[\
\œÙY[Z]KL
NÂˆB‚ˆ˜\ˆ]™[ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÞ\Ý[WÙ[XZ[Ü›ÝšY\—Ý\ÝÙ]™[ÚYˆ›ÝšY\‹ˆ[]™\žWÛ[ÙKˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Ù\Ü^WÛ˜[YKˆÝXš™XÝˆ[]™\žWÜÝ]\Ëˆ˜Z[\™WÛY\ÜØYÙKˆ™\]Y\ÝYØžWÙ[XZ[ˆÜ™X]YØ]ˆ”“ÓHÞ\Ý[WÙ[XZ[Ü›ÝšY\—Ý\ÝÙ]™[ÂˆÔ‘Tˆ–HÜ™X]YØ]TÐÂˆSRU[Z]Âˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›[Z]‹[Z]
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ]™[ËY
™]ÂˆÂˆ\Ý]™[YH™XY\‹‘Ù]ÝZY

Kˆ›ÝšY\ˆH™XY\‹‘Ù]Ýš[™ÊJKˆ[]™\žS[ÙHH™XY\‹‘Ù]Ýš[™ÊŠKˆ™XÚ\Y[[XZ[H™XY\‹‘Ù]Ýš[™ÊÊKˆ™XÚ\Y[\Ü^S˜[YHH™XY\‹’\Ñ“[

HÈˆˆˆ™XY\‹‘Ù]Ýš[™Ê
KˆÝXš™XÝH™XY\‹‘Ù]Ýš[™ÊJKˆ[]™\žTÝ]\ÈH™XY\‹‘Ù]Ýš[™ÊŠKˆ˜Z[\™SY\ÜØYÙHH™XY\‹’\Ñ“[
ÊHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊÊKˆ™\]Y\ÝYžQ[XZ[H™XY\‹’\Ñ“[

HÈˆˆˆ™XY\‹‘Ù]Ýš[™Ê
KˆÜ™X]Y]H™XY\‹‘Ù]]U[YJJBˆJNÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒNSKPÓÚ\™Y[XZ[›ÝšY\ˆ\Ý\›™\ÜÈ‹ˆÛÝ[H]™[ËÛÝ[ˆ]™[ÂˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÜÞ\Ý[KÙ[XZ[\›ÝšY\‹Ý\Ý\Ù[™‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH”Ú\™Y[XZ[›ÝšY\ˆ\ÝÙ[™\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆYˆ
ÛÛ^”™\]Y\Ý’XY\œËÛÛZ[œÒÙ^J–T›Ú™XÝ[ÙKUšY]ËP\ËU\Ù\ˆŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHšY]×Ø\×Ü™XYÛÛ›H‹ˆY\ÜØYÙHH•Üš]HXÝ[ÛœÈ\™H\ØX›YÚ[H\Ú[™ÈYZ[š\Ý˜]ÜˆšY]ËP\È™]šY]Ëˆ^]™]šY]ÈÈÙ[™\Ý[XZ[ˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ\Ú[™È˜\ˆØÝ[Y[H]ØZ]œÛÛ‘ØÝ[Y[”\œÙP\Þ[˜ÊÛÛ^”™\]Y\Ý›ÙJNÂˆ˜\ˆ›ÛÝHØÝ[Y[”›ÛÝ[[Y[Â‚ˆ˜\ˆ™XÚ\Y[[XZ[H›ÛÝ•žQÙ]›Ü\Jœ™XÚ\Y[[XZ[‹Ý]˜\ˆ™XÚ\Y[[[Y[
BˆÈ™XÚ\Y[[[Y[‘Ù]Ýš[™Ê
HÏÈˆ‚ˆˆˆŽÂ‚ˆ˜\ˆ™XÚ\Y[˜[YHH›ÛÝ•žQÙ]›Ü\Jœ™XÚ\Y[˜[YH‹Ý]˜\ˆ˜[YQ[[Y[
BˆÈ˜[YQ[[Y[‘Ù]Ýš[™Ê
HÏÈˆ‚ˆˆˆŽÂ‚ˆ˜\ˆ[]™\žS[ÙHH›ÛÝ•žQÙ]›Ü\J™[]™\žS[ÙH‹Ý]˜\ˆ[ÙQ[[Y[
BˆÈ[ÙQ[[Y[‘Ù]Ýš[™Ê
HÏÈœ›ÝšY\ˆ‚ˆˆœ›ÝšY\ˆŽÂ‚ˆ˜\ˆÛÛ™š\›X][ÛˆH›ÛÝ•žQÙ]›Ü\J˜ÛÛ™š\›X][Ûˆ‹Ý]˜\ˆÛÛ™š\›Q[[Y[
BˆÈÛÛ™š\›Q[[Y[‘Ù]Ýš[™Ê
HÏÈˆ‚ˆˆˆŽÂ‚ˆ™XÚ\Y[[XZ[H™XÚ\Y[[XZ[•š[J
NÂˆ™XÚ\Y[˜[YHH™XÚ\Y[˜[YK•š[J
NÂˆ[]™\žS[ÙHHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[]™\žS[ÙJHÈœ›ÝšY\ˆˆˆ[]™\žS[ÙK•š[J
NÂ‚ˆYˆ
\Ýš[™Ë‘\]X[ÊÛÛ™š\›X][Û‹”ÑS‘Ô“Õ’QT—ÕTÕ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜ÛÛ™š\›X][Û—Ü™\]Z\™Y‹ˆY\ÜØYÙHH”Ù]ÛÛ™š\›X][ÛˆÈÑS‘Ô“Õ’QT—ÕTÕÈÙ[™Û™HÛÛ›ÛY›ÝšY\ˆ\Ý[XZ[ˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[[XZ[
H\™XÚ\Y[[XZ[ÛÛZ[œÊ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHš[˜[YÜ™XÚ\Y[‹ˆY\ÜØYÙHHH˜[Y™XÚ\Y[[XZ[\È™\]Z\™Y›ÜˆH›ÝšY\ˆ\Ý[XZ[ˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆYˆ
›Ú™XÝ[ÙQ[XZ[™XÚ\Y[ÚÝ[™TÚÚ\Y
™XÚ\Y[[XZ[
JBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœ™XÚ\Y[Ø›ØÚÙY‹ˆY\ÜØYÙHH•HÛÛ™šYÝ\™YÚ\™Y›ÝšY\ˆ›ØÚÜÈ›ØØ[Üˆ[\H™XÚ\Y[Ëˆ\ÙHH™X[›Ý]X›H™XÚ\Y[›ÜˆHÛÛ›ÛY\Ýˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆ˜\ˆ›ÝšY\ˆHÙ]›Ú™XÝ[ÙTÚ\™Y[XZ[›ÝšY\”[[YJ
NÂˆ˜\ˆXÝÜ•\Ù\’YH]ØZ]™\ÛÛ™TÙ\ÜÚ[Û•\Ù\’Y›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆ˜\ˆXÝÜ‘[XZ[HˆŽÂ‚ˆYˆ
XÝÜ•\Ù\’Y\È›Ý[
BˆÂˆ]ØZ]\Ú[™È˜\ˆ\Ù\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ[XZ[ˆ”“ÓH\Ý\Ù\œÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ\Ù\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹XÝÜ•\Ù\’Y•˜[YJNÂˆXÝÜ‘[XZ[H
]ØZ]\Ù\ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
JOË•ÔÝš[™Ê
HÏÈˆŽÂˆB‚ˆ˜\ˆÝXš™XÝH	”›Ú™XÝ[ÙH[XZ[›ÝšY\ˆ\ÝHÑ]U[YSÙ™œÙ]•]Ó›ÝÎž^^^KSSKY›[_HUÈŽÂˆ˜\ˆ›ÙHH	ˆˆ‚”›Ú™XÝ[ÙHÚ\™Y[XZ[›ÝšY\ˆ\Ý‚”›ÝšY\ŽˆÜ›ÝšY\‹”›ÝšY\ŸB”™Y™\œ™Y[]™\žH[ÙNˆÜ›ÝšY\‹”™Y™\œ™Y[]™\žS[Ù_B”Ù[™\ŽˆÜ›ÝšY\‹”Ù[™\“˜[Y_HÜ›ÝšY\‹”Ù[™\‘[XZ[O‚”™XÚ\Y[ˆÜ™XÚ\Y[[XZ[B”™\]Y\ÝYžNˆØXÝÜ‘[XZ[B‘Ù[™\˜]YUÎˆÑ]U[YSÙ™œÙ]•]Ó›ÝÎ“ßB‚•\È\ÈHÛÛ›ÛYÚ[™ÛK\™XÚ\Y[\Ý[XZ[ˆ]ÛÛ™š\›\È]›Ú™XÝ[ÙHØ[ˆÙ[™›ÝYÚHÚ\™YÛØ˜[[XZ[›ÝšY\ˆÛÛ™šYÝ\˜][Û‹‚ˆˆˆŽÂ‚ˆ˜\ˆ™\Ý[H]ØZ]Ù[™›Ú™XÝ[ÙQ[XZ[›ÝYÚÚ\™Y›ÝšY\\Þ[˜Êˆ[]™\žS[ÙKˆ™XÚ\Y[[XZ[ˆ™XÚ\Y[˜[YKˆ\œ˜^K‘[\OÝš[™ÏŠ
KˆÝXš™XÝˆ›ÙBˆ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ[œÙ\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•ÈÞ\Ý[WÙ[XZ[Ü›ÝšY\—Ý\ÝÙ]™[È
ˆ›ÝšY\‹ˆ[]™\žWÛ[ÙKˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Ù\Ü^WÛ˜[YKˆÝXš™XÝˆ[]™\žWÜÝ]\Ëˆ˜Z[\™WÛY\ÜØYÙKˆ™\]Y\ÝYØžWÝ\Ù\—ÚYˆ™\]Y\ÝYØžWÙ[XZ[ˆ
BˆSQTÈ
ˆ›ÝšY\‹ˆ[]™\žWÛ[ÙKˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Ù\Ü^WÛ˜[YKˆÝXš™XÝˆ[]™\žWÜÝ]\Ëˆ˜Z[\™WÛY\ÜØYÙKˆ™\]Y\ÝYØžWÝ\Ù\—ÚYˆ™\]Y\ÝYØžWÙ[XZ[ˆ
NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›ÝšY\ˆ‹›ÝšY\‹”›ÝšY\ŠNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]™\žWÛ[ÙH‹[]™\žS[ÙJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ù[XZ[‹™XÚ\Y[[XZ[
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ù\Ü^WÛ˜[YH‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[˜[YJHÈ“[•˜[YHˆ™XÚ\Y[˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÝXš™XÝ‹ÝXš™XÝ
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]™\žWÜÝ]\È‹™\Ý[”Ý]\ÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™˜Z[\™WÛY\ÜØYÙH‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™\Ý[‘˜Z[\™SY\ÜØYÙJHÈ“[•˜[YHˆ™\Ý[‘˜Z[\™SY\ÜØYÙJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™\]Y\ÝYØžWÝ\Ù\—ÚY‹XÝÜ•\Ù\’Y\È[È“[•˜[YHˆXÝÜ•\Ù\’Y•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™\]Y\ÝYØžWÙ[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÝÜ‘[XZ[
HÈ“[•˜[YHˆXÝÜ‘[XZ[
NÂ‚ˆ]ØZ][œÙ\ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ˜\ˆÝ]\ÐÛÙHH™\Ý[”Ý]\Ë‘\]X[ÊœÙ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ™\Ý[”Ý]\Ë‘\]X[Ê›Ý]›ÞÛÛ›H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÈÝ]\ÐÛÙ\Ë”Ý]\ÌŒÒÂˆˆÝ]\ÐÛÙ\Ë”Ý]\ÍL˜YØ]]Ø^NÂ‚ˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆ[Ù[HHŒNSKPÓÚ\™Y[XZ[›ÝšY\ˆ\Ý\›™\ÜÈ‹ˆÝ]\ÈH™\Ý[”Ý]\Ëˆ›ÝšY\ˆH›ÝšY\‹”›ÝšY\‹ˆ™Y™\œ™Y[]™\žS[ÙHH›ÝšY\‹”™Y™\œ™Y[]™\žS[ÙKˆ[]™\žS[ÙKˆ™XÚ\Y[[XZ[ˆÝXš™XÝˆ˜Z[\™SY\ÜØYÙHH™\Ý[‘˜Z[\™SY\ÜØYÙKˆY\ÜØYÙHH™\Ý[”Ý]\Ë‘\]X[ÊœÙ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÈÛÛ›ÛY›ÝšY\ˆ\Ý[XZ[Ø\ÈÙ[ˆ‚ˆˆ™\Ý[”Ý]\Ë‘\]X[Ê›Ý]›ÞÛÛ›H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÈÛÛ›ÛY›ÝšY\ˆ\ÝØ\È™XÛÜ™Y[ˆÝ]›Þ[Û›H[ÙKˆ›È[XZ[Ø\ÈÙ[ˆ‚ˆˆÛÛ›ÛY›ÝšY\ˆ\ÝY›ÝÙ[™ÝXØÙ\ÜÙ[Kˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙJNÂŸJNÂ‹ËÈNSKPÓÚ\™Y[XZ[›ÝšY\ˆ\Ý\›™\ÜÈHS‘‚‚‹ËÈŒˆÚ\™Y[XZ[™XÚ\Y[ØY™]H™]šY]ÈHÕT•˜\“X\Ù]
‹Ø\KÜÞ\Ý[KÙ[XZ[\›ÝšY\‹Ü™XÚ\Y[\ØY™]KÜÝ[[X\žH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH‘[XZ[™XÚ\Y[ØY™]H™]šY]È\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆ[\ÈH™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆ[PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ[WØÛÙK[WÛ˜[YK[WÙ\ØÜš\[Û‹š\Ú×Û]™[›ØÚÜ×ÜÙ[™\×ØXÝ]™Bˆ”“ÓHÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ[\ÂˆÔ‘Tˆ–H›ØÚÜ×ÜÙ[™TÐËš\Ú×Û]™[TÐË[WØÛÙNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ][PÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ[\ËY
™]ÂˆÂˆ[PÛÙHH™XY\‹‘Ù]Ýš[™Ê
Kˆ[S˜[YHH™XY\‹‘Ù]Ýš[™ÊJKˆ[Q\ØÜš\[ÛˆH™XY\‹‘Ù]Ýš[™ÊŠKˆš\ÚÓ]™[H™XY\‹‘Ù]Ýš[™ÊÊKˆ›ØÚÜÔÙ[™H™XY\‹‘Ù]›ÛÛX[Š
Kˆ\ÐXÝ]™HH™XY\‹‘Ù]›ÛÛX[ŠJBˆJNÂˆBˆB‚ˆØš™XÝÈ]\Ý™]šY]ÈH[Âˆ]ØZ]\Ú[™È
˜\ˆ]\ÝÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×ÚYˆÛÛœÝ[Y\—ÚÙ^KˆØÙ[˜\š[Ëˆ[]™\žWÛ[ÙKˆ›ÝšY\‹ˆ™]šY]×ÜÝ]\ËˆÝ[Ü™XÚ\Y[ØÛÝ[ˆ›ØÚÙYØÛÝ[ˆØ\›š[™×ØÛÝ[ˆÛX\—ØÛÝ[ˆÙ[™\˜]YØžWÙ[XZ[ˆÙ[™\˜]YØ]ˆ\›Ý™YØžWÙ[XZ[ˆ\›Ý™YØ]ˆ^\™\×Ø]ˆ™]šY]×ÛY\ÜØYÙBˆ”“ÓHÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]ÜÂˆÔ‘Tˆ–HÙ[™\˜]YØ]TÐÂˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]]\ÝÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ]\Ý™]šY]ÈH™]ÂˆÂˆ™]šY]ÒYH™XY\‹‘Ù]ÝZY

KˆÛÛœÝ[Y\’Ù^HH™XY\‹‘Ù]Ýš[™ÊJKˆØÙ[˜\š[ÈH™XY\‹‘Ù]Ýš[™ÊŠKˆ[]™\žS[ÙHH™XY\‹‘Ù]Ýš[™ÊÊKˆ›ÝšY\ˆH™XY\‹‘Ù]Ýš[™Ê
Kˆ™]šY]ÔÝ]\ÈH™XY\‹‘Ù]Ýš[™ÊJKˆÝ[™XÚ\Y[ÛÝ[H™XY\‹‘Ù][ÌŠŠKˆ›ØÚÙYÛÝ[H™XY\‹‘Ù][ÌŠÊKˆØ\›š[™ÐÛÝ[H™XY\‹‘Ù][ÌŠ
KˆÛX\ÛÝ[H™XY\‹‘Ù][ÌŠJKˆÙ[™\˜]YžQ[XZ[H™XY\‹’\Ñ“[
L
HÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊL
KˆÙ[™\˜]Y]H™XY\‹‘Ù]]U[YJLJKˆ\›Ý™YžQ[XZ[H™XY\‹’\Ñ“[
LŠHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊLŠKˆ\›Ý™Y]H™XY\‹’\Ñ“[
LÊHÈ
]U[YOÊ[[ˆ™XY\‹‘Ù]]U[YJLÊKˆ^\™\Ð]H™XY\‹’\Ñ“[
M
HÈ
]U[YOÊ[[ˆ™XY\‹‘Ù]]U[YJM
Kˆ™]šY]ÓY\ÜØYÙHH™XY\‹’\Ñ“[
MJHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊMJBˆNÂˆBˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒˆÚ\™Y[XZ[™XÚ\Y[ØY™]H™]šY]È‹ˆÝ[[X\žHH™]ÂˆÂˆ[PÛÝ[H[\ËÛÝ[ˆXÝ]™T[PÛÝ[H[\ËÛÝ[
ˆOˆ
›ÛÛ
\‹‘Ù]\J
K‘Ù]›Ü\Jš\ÐXÝ]™HŠHK‘Ù]˜[YJŠHJKˆ]\Ý™]šY]ÂˆKˆ[\ÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÜÞ\Ý[KÙ[XZ[\›ÝšY\‹Ü™XÚ\Y[\ØY™]KÜ™]šY]ËZ][\È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH‘[XZ[™XÚ\Y[ØY™]H™]šY]È][\È\™H™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆÝZYÈ™]šY]ÒYH[Âˆ˜\ˆ™]šY]ÒY^HÛÛ^”™\]Y\Ý”]Y\žVÈœ™]šY]ÒY—K‘š\œÝÜ‘Y˜][

NÂˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™]šY]ÒY^
H	‰ˆÝZY•žT\œÙJ™]šY]ÒY^Ý]˜\ˆ\œÙY™]šY]ÒY
JBˆÂˆ™]šY]ÒYH\œÙY™]šY]ÒYÂˆB‚ˆYˆ
™]šY]ÒY\È[
BˆÂˆ]ØZ]\Ú[™È˜\ˆ]\ÝÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×ÚYˆ”“ÓHÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]ÜÂˆÔ‘Tˆ–HÙ[™\˜]YØ]TÐÂˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ˜\ˆ]\Ý˜[YHH]ØZ]]\ÝÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂˆYˆ
]\Ý˜[YH\ÈÝZY]\ÝÝZY
BˆÂˆ™]šY]ÒYH]\ÝÝZYÂˆBˆB‚ˆYˆ
™]šY]ÒY\È[
BˆÂˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒˆÚ\™Y[XZ[™XÚ\Y[ØY™]H™]šY]È][\È‹ˆÛÝ[Hˆ™]šY]ÒYH
ÝZYÊ[[ˆ][\ÈH\œ˜^K‘[\OØš™XÝŠ
BˆJNÂˆB‚ˆ˜\ˆ[Z]HLÂˆYˆ
[•žT\œÙJÛÛ^”™\]Y\Ý”]Y\žVÈ›[Z]—K‘š\œÝÜ‘Y˜][

KÝ]˜\ˆ\œÙY[Z]
JBˆÂˆ[Z]HX]Û[\
\œÙY[Z]KL
NÂˆB‚ˆ˜\ˆ][\ÈH™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È˜\ˆ][PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×Ú][WÚYˆ™]šY]×ÚYˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Ù\Ü^WÛ˜[YKˆ™XÚ\Y[ÚÚ[™ˆX[˜YÙ\—Ù[XZ[ˆØ×Ù[XZ[Ëˆš\Ú×ØÛÙ\Ëˆš\Ú×Û]™[ˆØY™]WÜÝ]\Ëˆ›ØÚ×ÜÙ[™ˆ]Z[ËˆÜ™X]YØ]ˆ”“ÓHÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×Ú][\ÂˆÒT‘H™]šY]×ÚYH™]šY]×ÚYˆÔ‘Tˆ–H›ØÚ×ÜÙ[™TÐËš\Ú×Û]™[TÐË™XÚ\Y[Ù[XZ[ˆSRU[Z]Âˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™]šY]×ÚY‹™]šY]ÒY•˜[YJNÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›[Z]‹[Z]
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]][PÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ][\ËY
™]ÂˆÂˆ™]šY]Ò][RYH™XY\‹‘Ù]ÝZY

Kˆ™]šY]ÒYH™XY\‹‘Ù]ÝZY
JKˆ™XÚ\Y[[XZ[H™XY\‹‘Ù]Ýš[™ÊŠKˆ™XÚ\Y[\Ü^S˜[YHH™XY\‹’\Ñ“[
ÊHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊÊKˆ™XÚ\Y[Ú[™H™XY\‹‘Ù]Ýš[™Ê
KˆX[˜YÙ\‘[XZ[H™XY\‹’\Ñ“[
JHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊJKˆØÑ[XZ[ÈH™XY\‹’\Ñ“[
ŠHÈ\œ˜^K‘[\OÝš[™ÏŠ
Hˆ™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠŠKˆš\ÚÐÛÙ\ÈH™XY\‹’\Ñ“[
ÊHÈ\œ˜^K‘[\OÝš[™ÏŠ
Hˆ™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠÊKˆš\ÚÓ]™[H™XY\‹‘Ù]Ýš[™Ê
KˆØY™]TÝ]\ÈH™XY\‹‘Ù]Ýš[™ÊJKˆ›ØÚÔÙ[™H™XY\‹‘Ù]›ÛÛX[ŠL
Kˆ]Z[ÈH™XY\‹‘Ù]Ýš[™ÊLJKˆÜ™X]Y]H™XY\‹‘Ù]]U[YJLŠBˆJNÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒˆÚ\™Y[XZ[™XÚ\Y[ØY™]H™]šY]È][\È‹ˆÛÝ[H][\ËÛÝ[ˆ™]šY]ÒYˆ][\ÂˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÜÞ\Ý[KÙ[XZ[\›ÝšY\‹Ü™XÚ\Y[\ØY™]KÜ[‹\™]šY]È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH‘[XZ[™XÚ\Y[ØY™]H™]šY]ÈÙ[™\˜][Ûˆ\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆYˆ
ÛÛ^”™\]Y\Ý’XY\œËÛÛZ[œÒÙ^J–T›Ú™XÝ[ÙKUšY]ËP\ËU\Ù\ˆŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHšY]×Ø\×Ü™XYÛÛ›H‹ˆY\ÜØYÙHH•Üš]HXÝ[ÛœÈ\™H\ØX›YÚ[H\Ú[™ÈYZ[š\Ý˜]ÜˆšY]ËP\È™]šY]Ëˆ^]™]šY]ÈÈ[ˆ™XÚ\Y[ØY™]H™]šY]Ëˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ\Ú[™È˜\ˆ™\]Y\ÝØÝ[Y[H]ØZ]œÛÛ‘ØÝ[Y[”\œÙP\Þ[˜ÊÛÛ^”™\]Y\Ý›ÙJNÂˆ˜\ˆ™\]Y\Ý›ÛÝH™\]Y\ÝØÝ[Y[”›ÛÝ[[Y[Â‚ˆ˜\ˆÛÛœÝ[Y\’Ù^HH™\]Y\Ý›ÛÝ•žQÙ]›Ü\J˜ÛÛœÝ[Y\’Ù^H‹Ý]˜\ˆÛÛœÝ[Y\‘[[Y[
BˆÈÛÛœÝ[Y\‘[[Y[‘Ù]Ýš[™Ê
HÏÈ•SQWÐÓÓTPSÑWÑS‘ÒS‘QT—Ó“ÕQ’PÐUSÓ”È‚ˆˆ•SQWÐÓÓTPSÑWÑS‘ÒS‘QT—Ó“ÕQ’PÐUSÓ”ÈŽÂ‚ˆ˜\ˆØÙ[˜\š[ÈH™\]Y\Ý›ÛÝ•žQÙ]›Ü\JœØÙ[˜\š[È‹Ý]˜\ˆØÙ[˜\š[Ñ[[Y[
BˆÈØÙ[˜\š[Ñ[[Y[‘Ù]Ýš[™Ê
HÏÈÙYZÛWÜ™[Z[™\ˆ‚ˆˆÙYZÛWÜ™[Z[™\ˆŽÂ‚ˆ˜\ˆ[]™\žS[ÙHH™\]Y\Ý›ÛÝ•žQÙ]›Ü\J™[]™\žS[ÙH‹Ý]˜\ˆ[ÙQ[[Y[
BˆÈ[ÙQ[[Y[‘Ù]Ýš[™Ê
HÏÈœ›ÝšY\ˆ‚ˆˆœ›ÝšY\ˆŽÂ‚ˆÛÛœÝ[Y\’Ù^HHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJÛÛœÝ[Y\’Ù^JHÈ•SQWÐÓÓTPSÑWÑS‘ÒS‘QT—Ó“ÕQ’PÐUSÓ”ÈˆˆÛÛœÝ[Y\’Ù^K•š[J
NÂˆØÙ[˜\š[ÈHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJØÙ[˜\š[ÊHÈÙYZÛWÜ™[Z[™\ˆˆˆØÙ[˜\š[Ë•š[J
NÂˆ[]™\žS[ÙHHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[]™\žS[ÙJHÈœ›ÝšY\ˆˆˆ[]™\žS[ÙK•š[J
NÂ‚ˆYˆ
\Ýš[™Ë‘\]X[ÊÛÛœÝ[Y\’Ù^K•SQWÐÓÓTPSÑWÑS‘ÒS‘QT—Ó“ÕQ’PÐUSÓ”È‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH[œÝ\ÜYØÛÛœÝ[Y\ˆ‹ˆY\ÜØYÙHH”™XÚ\Y[ØY™]H™]šY]ÈÝ\œ™[HÝ\ÜÈ[YHÛÛ\X[˜ÙH[™Ú[™Y\ˆ›ÝYšXØ][ÛœËˆY][Û˜[ÛÛœÝ[Y\œÈ\™H™YÚ\Ý\™Y›Üˆ]\™H›ÛÝ]ˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆ˜\ˆ›ÝšY\ˆHÙ]›Ú™XÝ[ÙTÚ\™Y[XZ[›ÝšY\”[[YJ
NÂˆ˜\ˆXÝÜ•\Ù\’YH]ØZ]™\ÛÛ™TÙ\ÜÚ[Û•\Ù\’Y›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆ˜\ˆXÝÜ‘[XZ[HˆŽÂ‚ˆYˆ
XÝÜ•\Ù\’Y\È›Ý[
BˆÂˆ]ØZ]\Ú[™È˜\ˆ\Ù\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ[XZ[ˆ”“ÓH\Ý\Ù\œÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ\Ù\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹XÝÜ•\Ù\’Y•˜[YJNÂˆXÝÜ‘[XZ[H
]ØZ]\Ù\ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
JOË•ÔÝš[™Ê
HÏÈˆŽÂˆB‚ˆ˜\ˆÙ\ÜÚ[Û•ÚÙ[ˆHÛÛ^”™\]Y\Ý’XY\œË•žQÙ]˜[YJ–T›Ú™XÝ[ÙKTÙ\ÜÚ[Ûˆ‹Ý]˜\ˆXY\•˜[YJBˆÈXY\•˜[YK•ÔÝš[™Ê
BˆˆˆŽÂ‚ˆ\Ú[™È˜\ˆÛY[H™]ÈÛY[

NÂˆÛY[‘Y˜][™\]Y\ÝXY\œËY
–T›Ú™XÝ[ÙKTÙ\ÜÚ[Ûˆ‹Ù\ÜÚ[Û•ÚÙ[ŠNÂ‚ˆ˜\ˆ™]šY]Õ\›H	š‹ËÌLËŒŒŒNLØ\KÝ[YKXÛÛ\X[˜ÙKÜ™]šY]ÏÜØÙ[˜\š[Ï^Õ\šK‘\ØØ\Q]TÝš[™ÊØÙ[˜\š[Ê_HŽÂˆ\Ú[™È˜\ˆ™]šY]Ô™\ÜÛœÙHH]ØZ]ÛY[‘Ù]\Þ[˜Ê™]šY]Õ\›
NÂˆ˜\ˆ™]šY]ÐÛÛ[H]ØZ]™]šY]Ô™\ÜÛœÙKÛÛ[”™XY\ÔÝš[™Ð\Þ[˜Ê
NÂ‚ˆYˆ
\™]šY]Ô™\ÜÛœÙK’\ÔÝXØÙ\ÜÔÝ]\ÐÛÙJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœ™]šY]×Ù˜Z[Y‹ˆY\ÜØYÙHH	ÛÝ[›ÝÙ[™\˜]H[YHÛÛ\X[˜ÙH™]šY]È™Y›Ü™H™XÚ\Y[ØY™]H™]šY]ËˆÊ[
\™]šY]Ô™\ÜÛœÙK”Ý]\ÐÛÙ_H‹ˆ™]šY]ÐÛÛ[ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍL˜YØ]]Ø^JNÂˆB‚ˆ\Ú[™È˜\ˆ™]šY]ÑØÝ[Y[HœÛÛ‘ØÝ[Y[”\œÙJ™]šY]ÐÛÛ[
NÂˆ˜\ˆ™]šY]Ô›ÛÝH™]šY]ÑØÝ[Y[”›ÛÝ[[Y[Â‚ˆYˆ
\™]šY]Ô›ÛÝ•žQÙ]›Ü\J›Z\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœÈ‹Ý]˜\ˆZ\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœÊHZ\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœË•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™\œ˜^JBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœ™]šY]×ÛZ\ÜÚ[™×Ü™XÚ\Y[È‹ˆY\ÜØYÙHH•[YHÛÛ\X[˜ÙH™]šY]ÈY›Ý[˜ÛYHZ\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœÈ™XÚ\Y[]Kˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍL[\›˜[Ù\™\‘\œ›ÜŠNÂˆB‚ˆ˜\ˆØ[™Y]\ÈH™]È\Ý
Ýš[™È[XZ[Ýš[™È˜[YKÝš[™ÈX[˜YÙ\‘[XZ[Ýš[™Ö×HØÑ[XZ[Ë\ÝÝš[™Ïˆš\ÚÐÛÙ\Ë\ÝÝš[™Ïˆ]Z[Ë›ÛÛ›ØÚÔÙ[™Ýš[™ÈÛÝ\˜ÙRœÛÛŠOŠ
NÂ‚ˆ›Ü™XXÚ
˜\ˆ][H[ˆZ\ÜÚ[™ÔÝX›Z\ÜÚ[ÛœË‘[[Y\˜]P\œ˜^J
JBˆÂˆ˜\ˆ[XZ[H][K•žQÙ]›Ü\J™[XZ[‹Ý]˜\ˆ[XZ[[[Y[
HÈ[XZ[[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆ˜[YHH][K•žQÙ]›Ü\J™\Ü^S˜[YH‹Ý]˜\ˆ˜[YQ[[Y[
HÈ˜[YQ[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆX[˜YÙ\‘[XZ[H][K•žQÙ]›Ü\J›X[˜YÙ\‘[XZ[‹Ý]˜\ˆX[˜YÙ\‘[XZ[[[Y[
HÈX[˜YÙ\‘[XZ[[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂ‚ˆ˜\ˆØÑ[XZ[ÈH™]È\ÝÝš[™ÏŠ
NÂˆYˆ
][K•žQÙ]›Ü\J˜ØÑ[XZ[È‹Ý]˜\ˆØÑ[[Y[
H	‰ˆØÑ[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™\œ˜^JBˆÂˆ›Ü™XXÚ
˜\ˆØÈ[ˆØÑ[[Y[‘[[Y\˜]P\œ˜^J
JBˆÂˆ˜\ˆØÕ^HØË‘Ù]Ýš[™Ê
HÏÈˆŽÂˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJØÕ^
JBˆÂˆØÑ[XZ[ËY
ØÕ^•š[J
JNÂˆBˆBˆB‚ˆ˜\ˆš\ÚÐÛÙ\ÈH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ]Z[ÈH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ›ØÚÔÙ[™H˜[ÙNÂ‚ˆ˜\ˆ›Ü›X[^™Y[XZ[H[XZ[•š[J
K•ÓÝÙ\’[˜\šX[

NÂˆ˜\ˆ›Ü›X[^™Y˜[YHH˜[YK•š[J
K•ÓÝÙ\’[˜\šX[

NÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[XZ[
HY[XZ[ÛÛZ[œÊ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆÂˆš\ÚÐÛÙ\ËY
‘STWÓÔ—ÒS•SQÔ‘PÒTQS•ŠNÂˆ]Z[ËY
”™XÚ\Y[[XZ[\È[\HÜˆ[˜[YˆŠNÂˆ›ØÚÔÙ[™HYNÂˆB‚ˆYˆ
[XZ[‘[™ÕÚ]
‹›ØØ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆš\ÚÐÛÙ\ËY
“ÐÐSÓÔ—ÕTÕÑÓPRSˆŠNÂˆ]Z[ËY
”™XÚ\Y[[XZ[\È›ØØ[[™Ø[››Ý™H›Ý]YžHHÚ\™Y›ÝšY\‹ˆŠNÂˆ›ØÚÔÙ[™HYNÂˆB‚ˆYˆ
›Ü›X[^™Y[XZ[ÛÛZ[œÊ™[[È‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ›Ü›X[^™Y[XZ[ÛÛZ[œÊ\Ý‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ›Ü›X[^™Y˜[YKÛÛZ[œÊÝš[™ËÛÛ˜Ø]
™H‹›[ÈŠKÝš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ›Ü›X[^™Y˜[YKÛÛZ[œÊ\Ý‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆš\ÚÐÛÙ\ËY
““Ó—Ô“ÑPÕSÓ—ÓÔ—ÕTÕÕTÑTˆŠNÂˆ]Z[ËY
”™XÚ\Y[\X\œÈÈ™HH›Û‹\›ÙXÝ[Û‹Ý\Ý\Ù\‹ˆŠNÂˆ›ØÚÔÙ[™HYNÂˆB‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJX[˜YÙ\‘[XZ[
JBˆÂˆš\ÚÐÛÙ\ËY
“RTÔÒS‘×ÓPSQÑT—ÑSPRSŠNÂˆ]Z[ËY
“X[˜YÙ\ˆ[XZ[\ÈZ\ÜÚ[™ËˆŠNÂˆBˆ[ÙHYˆ
X[˜YÙ\‘[XZ[‘[™ÕÚ]
‹›ØØ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆš\ÚÐÛÙ\ËY
““Ó—Ô“ÕUP“WÓPSQÑT—ÓÔ—ÐÐÈŠNÂˆ]Z[ËY
“X[˜YÙ\ˆ[XZ[\È›ØØ[[™Ø[››Ý™XÙZ]™HÐËÙ\ØØ[][Û‹ˆŠNÂˆ›ØÚÔÙ[™HYNÂˆB‚ˆ›Ü™XXÚ
˜\ˆØÑ[XZ[[ˆØÑ[XZ[ÊBˆÂˆYˆ
ØÑ[XZ[‘[™ÕÚ]
‹›ØØ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆš\ÚÐÛÙ\ËY
““Ó—Ô“ÕUP“WÓPSQÑT—ÓÔ—ÐÐÈŠNÂˆ]Z[ËY
	ÐÈ[XZ[\È›Û‹\›Ý]X›NˆØØÑ[XZ[HŠNÂˆ›ØÚÔÙ[™HYNÂˆBˆB‚ˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[XZ[
Bˆ	‰ˆ[XZ[ÛÛZ[œÊ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
Bˆ	‰ˆY[XZ[‘[™ÕÚ]
\ÜÚYÛ˜[˜ÛÛH‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ	‰ˆY[XZ[‘[™ÕÚ]
Û™[™XÚË˜ÛÛH‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ	‰ˆY[XZ[‘[™ÕÚ]
Û™[™XÚÛX‹˜ÛÛH‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ	‰ˆY[XZ[‘[™ÕÚ]
‹›ØØ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆš\ÚÐÛÙ\ËY
‘VT“SÑÓPRS—Ô‘U’QUÈŠNÂˆ]Z[ËY
”™XÚ\Y[\ÈÝ]ÚYH^XÝYTÈÚYÛ˜[ÈÛ™S™XÚÈÛXZ[œËˆŠNÂˆB‚ˆØ[™Y]\ËY

[XZ[•š[J
K˜[YK•š[J
KX[˜YÙ\‘[XZ[•š[J
KØÑ[XZ[Ë•Ð\œ˜^J
Kš\ÚÐÛÙ\Ë]Z[Ë›ØÚÔÙ[™][K‘Ù]˜]Õ^

JJNÂˆB‚ˆ˜\ˆ\XØ]Q[XZ[Ù]HØ[™Y]\Âˆ•Ú\™JÈOˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJË‘[XZ[
JBˆ‘Ü›Ý\žJÈOˆË‘[XZ[•š[J
K•ÓÝÙ\’[˜\šX[

JBˆ•Ú\™JÈOˆËÛÝ[

HˆJBˆ”Ù[XÝ
ÈOˆË’Ù^JBˆ•Ò\ÚÙ]
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂ‚ˆ˜\ˆ\XØ]S˜[YTÙ]HØ[™Y]\Âˆ•Ú\™JÈOˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJË“˜[YJJBˆ‘Ü›Ý\žJÈOˆË“˜[YK•š[J
K•ÓÝÙ\’[˜\šX[

JBˆ•Ú\™JÈOˆËÛÝ[

HˆJBˆ”Ù[XÝ
ÈOˆË’Ù^JBˆ•Ò\ÚÙ]
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂ‚ˆ›Üˆ
˜\ˆHHÈHØ[™Y]\ËÛÝ[ÈJÊÊBˆÂˆ˜\ˆØ[™Y]HHØ[™Y]\ÖÚWNÂ‚ˆYˆ
\XØ]Q[XZ[Ù]ÛÛZ[œÊØ[™Y]K‘[XZ[•š[J
K•ÓÝÙ\’[˜\šX[

JJBˆÂˆØ[™Y]K”š\ÚÐÛÙ\ËY
‘TPÐUWÑSPRSŠNÂˆØ[™Y]K‘]Z[ËY
”™XÚ\Y[[XZ[\X\œÈ[Ü™H[ˆÛ˜ÙH[ˆHÙ[™\˜]YÙ[™\ÝˆŠNÂˆØ[™Y]K›ØÚÔÙ[™HYNÂˆB‚ˆYˆ
\XØ]S˜[YTÙ]ÛÛZ[œÊØ[™Y]K“˜[YK•š[J
K•ÓÝÙ\’[˜\šX[

JJBˆÂˆØ[™Y]K”š\ÚÐÛÙ\ËY
‘TPÐUWÑTÔVWÓSQHŠNÂˆØ[™Y]K‘]Z[ËY
”™XÚ\Y[\Ü^H˜[YH\X\œÈ[Ü™H[ˆÛ˜ÙH[ˆHÙ[™\˜]YÙ[™\ÝˆŠNÂˆØ[™Y]K›ØÚÔÙ[™HYNÂˆB‚ˆØ[™Y]\ÖÚWHHØ[™Y]NÂˆB‚ˆ˜\ˆ›ØÚÙYÛÝ[HØ[™Y]\ËÛÝ[
ÈOˆË›ØÚÔÙ[™
NÂˆ˜\ˆØ\›š[™ÐÛÝ[HØ[™Y]\ËÛÝ[
ÈOˆXË›ØÚÔÙ[™	‰ˆË”š\ÚÐÛÙ\ËÛÝ[ˆ
NÂˆ˜\ˆÛX\ÛÝ[HØ[™Y]\ËÛÝ[
ÈOˆXË›ØÚÔÙ[™	‰ˆË”š\ÚÐÛÙ\ËÛÝ[OH
NÂˆ˜\ˆ™]šY]ÔÝ]\ÈH›ØÚÙYÛÝ[ˆÈ˜›ØÚÙYˆˆØ\›š[™ÐÛÝ[ˆÈœ™]šY]×Ü™\]Z\™Yˆˆœ™XYWÙ›Ü—Ø\›Ý˜[ŽÂˆ˜\ˆ™]šY]ÓY\ÜØYÙHH›ØÚÙYÛÝ[ˆˆÈ”™XÚ\Y[ØY™]H™]šY]È›Ý[™›ØÚÚ[™È™XÚ\Y[š\ÚÜËˆ™X[›ÝšY\ˆÙ[™\È›ØÚÙY[[™XÚ\Y[È\™HÛÜœ™XÝY[™™]šY]ÙYYØZ[‹ˆ‚ˆˆØ\›š[™ÐÛÝ[ˆˆÈ”™XÚ\Y[ØY™]H™]šY]È›Ý[™Ø\›š[™ÜËˆ\›Ý˜[\È[ÝÙYY\ˆYZ[š\Ý˜]Ü‹ÔÈ™]šY]Ëˆ‚ˆˆ”™XÚ\Y[ØY™]H™]šY]È›Ý[™›È™XÚ\Y[š\ÚÜËˆŽÂ‚ˆÝZY™]šY]ÒYÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ™]šY]ÐÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•ÈÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]ÜÈ
ˆÛÛœÝ[Y\—ÚÙ^KˆØÙ[˜\š[Ëˆ[]™\žWÛ[ÙKˆ›ÝšY\‹ˆ™]šY]×ÜÝ]\ËˆÝ[Ü™XÚ\Y[ØÛÝ[ˆ›ØÚÙYØÛÝ[ˆØ\›š[™×ØÛÝ[ˆÛX\—ØÛÝ[ˆÙ[™\˜]YØžWÝ\Ù\—ÚYˆÙ[™\˜]YØžWÙ[XZ[ˆ^\™\×Ø]ˆ™]šY]×ÛY\ÜØYÙBˆ
BˆSQTÈ
ˆÛÛœÝ[Y\—ÚÙ^KˆØÙ[˜\š[Ëˆ[]™\žWÛ[ÙKˆ›ÝšY\‹ˆ™]šY]×ÜÝ]\ËˆÝ[Ü™XÚ\Y[ØÛÝ[ˆ›ØÚÙYØÛÝ[ˆØ\›š[™×ØÛÝ[ˆÛX\—ØÛÝ[ˆÙ[™\˜]YØžWÝ\Ù\—ÚYˆÙ[™\˜]YØžWÙ[XZ[ˆ›ÝÊ
H
È[\˜[	ÌLˆÝ\œÉËˆ™]šY]×ÛY\ÜØYÙBˆ
Bˆ‘UT“’S‘ÈÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×ÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜ÛÛœÝ[Y\—ÚÙ^H‹ÛÛœÝ[Y\’Ù^JNÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœØÙ[˜\š[È‹ØÙ[˜\š[ÊNÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]™\žWÛ[ÙH‹[]™\žS[ÙJNÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›ÝšY\ˆ‹›ÝšY\‹”›ÝšY\ŠNÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™]šY]×ÜÝ]\È‹™]šY]ÔÝ]\ÊNÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÝ[Ü™XÚ\Y[ØÛÝ[‹Ø[™Y]\ËÛÝ[
NÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜›ØÚÙYØÛÝ[‹›ØÚÙYÛÝ[
NÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJØ\›š[™×ØÛÝ[‹Ø\›š[™ÐÛÝ[
NÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜ÛX\—ØÛÝ[‹ÛX\ÛÝ[
NÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™Ù[™\˜]YØžWÝ\Ù\—ÚY‹XÝÜ•\Ù\’Y\È[È
Øš™XÝ
Q“[•˜[YHˆXÝÜ•\Ù\’Y•˜[YJNÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™Ù[™\˜]YØžWÙ[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÝÜ‘[XZ[
HÈ
Øš™XÝ
Q“[•˜[YHˆXÝÜ‘[XZ[
NÂˆ™]šY]ÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™]šY]×ÛY\ÜØYÙH‹™]šY]ÓY\ÜØYÙJNÂ‚ˆ™]šY]ÒYH
ÝZY
J]ØZ]™]šY]ÐÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈÝZY‘[\JNÂˆB‚ˆ›Ü™XXÚ
˜\ˆØ[™Y]H[ˆØ[™Y]\ÊBˆÂˆ˜\ˆš\ÚÓ]™[HØ[™Y]K›ØÚÔÙ[™ˆÈšYÚ‚ˆˆØ[™Y]K”š\ÚÐÛÙ\ËÛÝ[ˆˆÈ›YY][H‚ˆˆ˜ÛX\ˆŽÂ‚ˆ˜\ˆØY™]TÝ]\ÈHØ[™Y]K›ØÚÔÙ[™ˆÈ˜›ØÚÙY‚ˆˆØ[™Y]K”š\ÚÐÛÙ\ËÛÝ[ˆˆÈØ\›š[™È‚ˆˆ˜ÛX\ˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆ][PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•ÈÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×Ú][\È
ˆ™]šY]×ÚYˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Ù\Ü^WÛ˜[YKˆ™XÚ\Y[ÚÚ[™ˆX[˜YÙ\—Ù[XZ[ˆØ×Ù[XZ[Ëˆš\Ú×ØÛÙ\Ëˆš\Ú×Û]™[ˆØY™]WÜÝ]\Ëˆ›ØÚ×ÜÙ[™ˆ]Z[ËˆÛÝ\˜ÙWÜ^[ØYˆ
BˆSQTÈ
ˆ™]šY]×ÚYˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Ù\Ü^WÛ˜[YKˆ	Üš[X\žIËˆX[˜YÙ\—Ù[XZ[ˆØ×Ù[XZ[Ëˆš\Ú×ØÛÙ\Ëˆš\Ú×Û]™[ˆØY™]WÜÝ]\Ëˆ›ØÚ×ÜÙ[™ˆ]Z[ËˆÛÝ\˜ÙWÜ^[ØYˆ
NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™]šY]×ÚY‹™]šY]ÒY
NÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ù[XZ[‹Ø[™Y]K‘[XZ[
NÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ù\Ü^WÛ˜[YH‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJØ[™Y]K“˜[YJHÈ
Øš™XÝ
Q“[•˜[YHˆØ[™Y]K“˜[YJNÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›X[˜YÙ\—Ù[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJØ[™Y]K“X[˜YÙ\‘[XZ[
HÈ
Øš™XÝ
Q“[•˜[YHˆØ[™Y]K“X[˜YÙ\‘[XZ[
NÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜Ø×Ù[XZ[È‹Ø[™Y]KØÑ[XZ[ÊNÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœš\Ú×ØÛÙ\È‹Ø[™Y]K”š\ÚÐÛÙ\Ë‘\Ý[˜Ý
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJK•Ð\œ˜^J
JNÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœš\Ú×Û]™[‹š\ÚÓ]™[
NÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœØY™]WÜÝ]\È‹ØY™]TÝ]\ÊNÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜›ØÚ×ÜÙ[™‹Ø[™Y]K›ØÚÔÙ[™
NÂˆ][PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™]Z[È‹Ýš[™Ë’›Ú[Šˆ‹Ø[™Y]K‘]Z[Ë‘\Ý[˜Ý

JJNÂˆ][PÛÛ[X[™”\˜[Y]\œËY
™]ÈœÜÜ[“œÜÜ[\˜[Y]\ŠœÛÝ\˜ÙWÜ^[ØY‹œÜÜ[\\Ë“œÜÜ[•\K’œÛÛ˜ŠHÈ˜[YHHØ[™Y]K”ÛÝ\˜ÙRœÛÛˆJNÂ‚ˆ]ØZ]][PÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒˆÚ\™Y[XZ[™XÚ\Y[ØY™]H™]šY]È‹ˆÝ]\ÈH™]šY]ÔÝ]\Ëˆ™]šY]ÒYˆÛÛœÝ[Y\’Ù^KˆØÙ[˜\š[Ëˆ[]™\žS[ÙKˆ›ÝšY\ˆH›ÝšY\‹”›ÝšY\‹ˆÝ[[X\žHH™]ÂˆÂˆÝ[™XÚ\Y[ÛÝ[HØ[™Y]\ËÛÝ[ˆ›ØÚÙYÛÝ[ˆØ\›š[™ÐÛÝ[ˆÛX\ÛÝ[ˆØ[\›Ý™HH›ØÚÙYÛÝ[OHˆØ[”Ù[™™X[›ÝšY\‘[XZ[H˜[ÙKˆ™]šY]ÓY\ÜØYÙBˆBˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÜÞ\Ý[KÙ[XZ[\›ÝšY\‹Ü™XÚ\Y[\ØY™]KØ\›Ý™K\™]šY]È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHH‘[XZ[™XÚ\Y[ØY™]H™]šY]È\›Ý˜[\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›Ú™XÝÝX[HÛÛÜ™[˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆYˆ
ÛÛ^”™\]Y\Ý’XY\œËÛÛZ[œÒÙ^J–T›Ú™XÝ[ÙKUšY]ËP\ËU\Ù\ˆŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHšY]×Ø\×Ü™XYÛÛ›H‹ˆY\ÜØYÙHH•Üš]HXÝ[ÛœÈ\™H\ØX›YÚ[H\Ú[™ÈYZ[š\Ý˜]ÜˆšY]ËP\È™]šY]Ëˆ^]™]šY]ÈÈ\›Ý™H™XÚ\Y[ØY™]H™]šY]Ëˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ\Ú[™È˜\ˆØÝ[Y[H]ØZ]œÛÛ‘ØÝ[Y[”\œÙP\Þ[˜ÊÛÛ^”™\]Y\Ý›ÙJNÂˆ˜\ˆ›ÛÝHØÝ[Y[”›ÛÝ[[Y[Â‚ˆ˜\ˆÛÛ™š\›X][ÛˆH›ÛÝ•žQÙ]›Ü\J˜ÛÛ™š\›X][Ûˆ‹Ý]˜\ˆÛÛ™š\›X][Û‘[[Y[
BˆÈÛÛ™š\›X][Û‘[[Y[‘Ù]Ýš[™Ê
HÏÈˆ‚ˆˆˆŽÂ‚ˆYˆ
\Ýš[™Ë‘\]X[ÊÛÛ™š\›X][Û‹T“Õ‘WÔ‘PÒTQS•ÔÐQ‘UWÔ‘U’QUÈ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜ÛÛ™š\›X][Û—Ü™\]Z\™Y‹ˆY\ÜØYÙHH”Ù]ÛÛ™š\›X][ÛˆÈT“Õ‘WÔ‘PÒTQS•ÔÐQ‘UWÔ‘U’QUÈÈ\›Ý™HH™XÚ\Y[ØY™]H™]šY]Ëˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆÝZYÈ™]šY]ÒYH[ÂˆYˆ
›ÛÝ•žQÙ]›Ü\Jœ™]šY]ÒY‹Ý]˜\ˆ™]šY]ÒY[[Y[
Bˆ	‰ˆÝZY•žT\œÙJ™]šY]ÒY[[Y[‘Ù]Ýš[™Ê
KÝ]˜\ˆ\œÙY™]šY]ÒY
JBˆÂˆ™]šY]ÒYH\œÙY™]šY]ÒYÂˆB‚ˆYˆ
™]šY]ÒY\È[
BˆÂˆ]ØZ]\Ú[™È˜\ˆ]\ÝÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×ÚYˆ”“ÓHÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]ÜÂˆÔ‘Tˆ–HÙ[™\˜]YØ]TÐÂˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ˜\ˆ]\Ý˜[YHH]ØZ]]\ÝÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂˆYˆ
]\Ý˜[YH\ÈÝZY]\ÝÝZY
BˆÂˆ™]šY]ÒYH]\ÝÝZYÂˆBˆB‚ˆYˆ
™]šY]ÒY\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœ™]šY]×Û›ÝÙ›Ý[™‹ˆY\ÜØYÙHH“›È™XÚ\Y[ØY™]H™]šY]ÈØ\È›Ý[™È\›Ý™Kˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í›Ý›Ý[™
NÂˆB‚ˆ]ØZ]\Ú[™È
˜\ˆ›ØÚÐÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ›ØÚÙYØÛÝ[ˆØ\›š[™×ØÛÝ[ˆÝ[Ü™XÚ\Y[ØÛÝ[ˆ™]šY]×ÜÝ]\Âˆ”“ÓHÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]ÜÂˆÒT‘HÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×ÚYH™]šY]×ÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ›ØÚÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™]šY]×ÚY‹™]šY]ÒY•˜[YJNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]›ØÚÐÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
X]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœ™]šY]×Û›ÝÙ›Ý[™‹ˆY\ÜØYÙHH”™XÚ\Y[ØY™]H™]šY]ÈØ\È›Ý›Ý[™ˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í›Ý›Ý[™
NÂˆB‚ˆ˜\ˆ›ØÚÙYÛÝ[H™XY\‹‘Ù][ÌŠ
NÂˆ˜\ˆØ\›š[™ÐÛÝ[H™XY\‹‘Ù][ÌŠJNÂˆ˜\ˆÝ[ÛÝ[H™XY\‹‘Ù][ÌŠŠNÂ‚ˆYˆ
›ØÚÙYÛÝ[ˆ
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜›ØÚÙYÜ™XÚ\Y[×Ü™\Ù[‹ˆY\ÜØYÙHH”™XÚ\Y[ØY™]H™]šY]ÈØ[››Ý™H\›Ý™YÚ[H›ØÚÙY™XÚ\Y[È\™H™\Ù[ˆ‹ˆ™]šY]ÒYˆ›ØÚÙYÛÝ[ˆØ\›š[™ÐÛÝ[ˆÝ[™XÚ\Y[ÛÝ[HÝ[ÛÝ[ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍPÛÛ™›XÝ
NÂˆBˆB‚ˆ˜\ˆXÝÜ•\Ù\’YH]ØZ]™\ÛÛ™TÙ\ÜÚ[Û•\Ù\’Y›Ü”›ÙXÝ[ÛXÚÛ›ÝÛYÛY[\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆ˜\ˆXÝÜ‘[XZ[HˆŽÂ‚ˆYˆ
XÝÜ•\Ù\’Y\È›Ý[
BˆÂˆ]ØZ]\Ú[™È˜\ˆ\Ù\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ[XZ[ˆ”“ÓH\Ý\Ù\œÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ\Ù\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹XÝÜ•\Ù\’Y•˜[YJNÂˆXÝÜ‘[XZ[H
]ØZ]\Ù\ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
JOË•ÔÝš[™Ê
HÏÈˆŽÂˆB‚ˆ]ØZ]\Ú[™È˜\ˆ\›Ý™PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆTUHÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]ÜÂˆÑUˆ™]šY]×ÜÝ]\ÈH	Ø\›Ý™Y	Ëˆ\›Ý™YØžWÝ\Ù\—ÚYH\›Ý™YØžWÝ\Ù\—ÚYˆ\›Ý™YØžWÙ[XZ[H\›Ý™YØžWÙ[XZ[ˆ\›Ý™YØ]H›ÝÊ
Kˆ^\™\×Ø]H›ÝÊ
H
È[\˜[	ÌLˆÝ\œÉËˆ™]šY]×ÛY\ÜØYÙHH	Ô™XÚ\Y[ØY™]H™]šY]È\›Ý™Y›ÜˆÛÛ›ÛY›ÝšY\ˆÙ[™Ú[™ÝË‰ÂˆÒT‘HÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×ÚYH™]šY]×ÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ\›Ý™PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™]šY]×ÚY‹™]šY]ÒY•˜[YJNÂˆ\›Ý™PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜\›Ý™YØžWÝ\Ù\—ÚY‹XÝÜ•\Ù\’Y\È[È
Øš™XÝ
Q“[•˜[YHˆXÝÜ•\Ù\’Y•˜[YJNÂˆ\›Ý™PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜\›Ý™YØžWÙ[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÝÜ‘[XZ[
HÈ
Øš™XÝ
Q“[•˜[YHˆXÝÜ‘[XZ[
NÂ‚ˆ]ØZ]\›Ý™PÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒˆÚ\™Y[XZ[™XÚ\Y[ØY™]H™]šY]È‹ˆÝ]\ÈH˜\›Ý™Y‹ˆ™]šY]ÒYˆ\›Ý™YžQ[XZ[HXÝÜ‘[XZ[ˆ^\™\Ò[’Ý\œÈHL‹ˆY\ÜØYÙHH”™XÚ\Y[ØY™]H™]šY]È\›Ý™Yˆ™X[›ÝšY\ˆÙ[™\È[ÝÙYÛ›HÚ[H\È™]šY]È™[XZ[œÈ˜[Yˆ‚ˆJNÂŸJNÂ‚œÝ]XÈ\Þ[˜È\ÚÏ
›ÛÛ[ÝÙYÝš[™È™X\ÛÛ‹ÝZYÈ™]šY]ÒY
Oˆ›Ú™XÝ[ÙT™XÚ\Y[ØY™]QØ]P[ÝÜÔÙ[™\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆÝš[™ÈÛÛœÝ[Y\’Ù^KˆÝš[™ÈØÙ[˜\š[ÊBžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]×ÚYˆ”“ÓHÞ\Ý[WÙ[XZ[Ü™XÚ\Y[ÜØY™]WÜ™]šY]ÜÂˆÒT‘HÛÛœÝ[Y\—ÚÙ^HHÛÛœÝ[Y\—ÚÙ^BˆS‘ØÙ[˜\š[ÈHØÙ[˜\š[ÂˆS‘™]šY]×ÜÝ]\ÈH	Ø\›Ý™Y	ÂˆS‘›ØÚÙYØÛÝ[HˆS‘\›Ý™YØ]TÈ“Õ•SˆS‘^\™\×Ø]ˆ›ÝÊ
BˆÔ‘Tˆ–H\›Ý™YØ]TÐÂˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜ÛÛœÝ[Y\—ÚÙ^H‹ÛÛœÝ[Y\’Ù^JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœØÙ[˜\š[È‹ØÙ[˜\š[ÊNÂ‚ˆ˜\ˆ˜[YHH]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂˆYˆ
˜[YH\ÈÝZY™]šY]ÒY
BˆÂˆ™]\›ˆ
YK\›Ý™Y™XÚ\Y[ØY™]H™]šY]È\ÈXÝ]™Kˆ‹™]šY]ÒY
NÂˆB‚ˆ™]\›ˆ
˜[ÙK”™X[›ÝšY\ˆÙ[™™\]Z\™\È[ˆ\›Ý™Y™XÚ\Y[ØY™]H™]šY]ÈÚ]™\›È›ØÚÙY™XÚ\Y[Ëˆ‹[
NÂŸB‹ËÈŒˆÚ\™Y[XZ[™XÚ\Y[ØY™]H™]šY]ÈHS‘‚‚‹ËÈŒH›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆÙ[\ˆ›Ý[™][ÛˆHÕT•˜\“X\Ù]
‹Ø\KÜ›ÙXÝ[Û‹Û›ÝYšXØ][ÛœËÜÝ[[X\žH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆY™™XÝ]™U\Ù\’YH]ØZ]›Ú™XÝ[ÙT™\ÛÛ™QY™™XÝ]™T›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\’Y\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆYˆ
Y™™XÝ]™U\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹ˆY\ÜØYÙHHH›Ú™XÝ[ÙHÙ\ÜÚ[Ûˆ\È™\]Z\™YÈšY]È›ÙXÝ[Ûˆ›ÝYšXØ][ÛœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆ\Ù\ÛÛ^H]ØZ]›Ú™XÝ[ÙQÙ]›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\ÛÛ^\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YJNÂˆ˜\ˆ›ÝYšXØ][ÛœÈH]ØZ]›Ú™XÝ[ÙSØYš\ÚX›T›ÙXÝ[Û“›ÝYšXØ][ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YK\Ù\ÛÛ^”›ÛPÛÙ\ËJNÂ‚ˆ˜\ˆXÝ]™PÛÝ[H›ÝYšXØ][ÛœËÛÝ[Âˆ˜\ˆ[˜XÚÛ›ÝÛYÙYÛÝ[H›ÝYšXØ][ÛœËÛÝ[
][HOˆ][K•žQÙ]˜[YJ˜XÚÛ›ÝÛYÙY‹Ý]˜\ˆ˜[YJH	‰ˆ˜[YH\È›ÛÛXÚÛ›ÝÛYÙY	‰ˆXÚÛ›ÝÛYÙYOH˜[ÙJNÂˆ˜\ˆÜš]XØ[ÛÝ[H›ÝYšXØ][ÛœËÛÝ[
][HOˆ][K•žQÙ]˜[YJœÙ]™\š]H‹Ý]˜\ˆ˜[YJH	‰ˆÝš[™Ë‘\]X[Ê˜[YOË•ÔÝš[™Ê
K˜Üš]XØ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJNÂˆ˜\ˆØ\›š[™ÐÛÝ[H›ÝYšXØ][ÛœËÛÝ[
][HOˆ][K•žQÙ]˜[YJœÙ]™\š]H‹Ý]˜\ˆ˜[YJH	‰ˆÝš[™Ë‘\]X[Ê˜[YOË•ÔÝš[™Ê
KØ\›š[™È‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒH›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆÙ[\ˆ›Ý[™][Ûˆ‹ˆÝ[[X\žHH™]ÂˆÂˆY™™XÝ]™U\Ù\’Yˆ\Ù\‘[XZ[H\Ù\ÛÛ^‘[XZ[ˆ\Ù\‘\Ü^S˜[YHH\Ù\ÛÛ^‘\Ü^S˜[YKˆ›ÛPÛÙ\ÈH\Ù\ÛÛ^”›ÛPÛÙ\Ëˆš\ÚX›S›ÝYšXØ][ÛÛÝ[HXÝ]™PÛÝ[ˆ[˜XÚÛ›ÝÛYÙYÛÝ[ˆÜš]XØ[ÛÝ[ˆØ\›š[™ÐÛÝ[ˆKˆ]\Ý›ÝYšXØ][ÛœÈH›ÝYšXØ][ÛœË•ZÙJJK•Ð\œ˜^J
BˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÜ›ÙXÝ[Û‹Û›ÝYšXØ][ÛœÈ‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆY™™XÝ]™U\Ù\’YH]ØZ]›Ú™XÝ[ÙT™\ÛÛ™QY™™XÝ]™T›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\’Y\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆYˆ
Y™™XÝ]™U\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹ˆY\ÜØYÙHHH›Ú™XÝ[ÙHÙ\ÜÚ[Ûˆ\È™\]Z\™YÈšY]È›ÙXÝ[Ûˆ›ÝYšXØ][ÛœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆ[Z]HLÂˆYˆ
[•žT\œÙJÛÛ^”™\]Y\Ý”]Y\žVÈ›[Z]—K‘š\œÝÜ‘Y˜][

KÝ]˜\ˆ\œÙY[Z]
JBˆÂˆ[Z]HX]Û[\
\œÙY[Z]KŒ
NÂˆB‚ˆ˜\ˆ\Ù\ÛÛ^H]ØZ]›Ú™XÝ[ÙQÙ]›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\ÛÛ^\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YJNÂˆ˜\ˆ›ÝYšXØ][ÛœÈH]ØZ]›Ú™XÝ[ÙSØYš\ÚX›T›ÙXÝ[Û“›ÝYšXØ][ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YK\Ù\ÛÛ^”›ÛPÛÙ\Ë[Z]
NÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒH›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆÙ[\ˆ‹ˆÛÝ[H›ÝYšXØ][ÛœËÛÝ[ˆY™™XÝ]™U\Ù\’Yˆ›ÛPÛÙ\ÈH\Ù\ÛÛ^”›ÛPÛÙ\Ëˆ›ÝYšXØ][ÛœÂˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÜ›ÙXÝ[Û‹Û›ÝYšXØ][ÛœËÜÞ\Ý[H‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜XØÙ\Ü×Ù[šYY‹ˆY\ÜØYÙHHÜ™X][™È›ÙXÝ[Ûˆ›ÝYšXØ][ÛœÈ\È™\ÝšXÝYÈYZ[š\Ý˜]ÜœÈ[™›ÙXÝ[ÛˆÜ\˜]ÜœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆYˆ
ÛÛ^”™\]Y\Ý’XY\œËÛÛZ[œÒÙ^J–T›Ú™XÝ[ÙKUšY]ËP\ËU\Ù\ˆŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHšY]×Ø\×Ü™XYÛÛ›H‹ˆY\ÜØYÙHH•Üš]HXÝ[ÛœÈ\™H\ØX›YÚ[H\Ú[™ÈYZ[š\Ý˜]ÜˆšY]ËP\È™]šY]Ëˆ^]™]šY]ÈÈÜ™X]H›ÙXÝ[Ûˆ›ÝYšXØ][ÛœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ\Ú[™È˜\ˆØÝ[Y[H]ØZ]œÛÛ‘ØÝ[Y[”\œÙP\Þ[˜ÊÛÛ^”™\]Y\Ý›ÙJNÂˆ˜\ˆ›ÛÝHØÝ[Y[”›ÛÝ[[Y[Â‚ˆ˜\ˆ[Ù[RÙ^HH›ÛÝ•žQÙ]›Ü\J›[Ù[RÙ^H‹Ý]˜\ˆ[Ù[Q[[Y[
HÈ[Ù[Q[[Y[‘Ù]Ýš[™Ê
HÏÈŒŒHˆˆŒŒHŽÂˆ˜\ˆÙ]™\š]HH›ÛÝ•žQÙ]›Ü\JœÙ]™\š]H‹Ý]˜\ˆÙ]™\š]Q[[Y[
HÈÙ]™\š]Q[[Y[‘Ù]Ýš[™Ê
HÏÈš[™›Èˆˆš[™›ÈŽÂˆ˜\ˆ]HH›ÛÝ•žQÙ]›Ü\J]H‹Ý]˜\ˆ]Q[[Y[
HÈ]Q[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆ›ÙHH›ÛÝ•žQÙ]›Ü\J˜›ÙH‹Ý]˜\ˆ›ÙQ[[Y[
HÈ›ÙQ[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆÛÝ\˜ÙT›Ý]HH›ÛÝ•žQÙ]›Ü\JœÛÝ\˜ÙT›Ý]H‹Ý]˜\ˆ›Ý]Q[[Y[
HÈ›Ý]Q[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆXÝ[Û•\›H›ÛÝ•žQÙ]›Ü\J˜XÝ[Û•\›‹Ý]˜\ˆXÝ[Û‘[[Y[
HÈXÝ[Û‘[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂ‚ˆÊˆÑPÕT’UWÌŒŒÌŽWÓ“ÕQ’PÐUSÓ—ÕT“ÐSÕÓTÕ
‹ÂˆYˆ
TÙXÝ\š]R\™[š[™Ó[Ù[K’\ÔØY™PXÝ[Û•\›
XÝ[Û•\›
JBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH[œØY™WÝ\›Ü™Z™XÝY‹ˆY\ÜØYÙHH“Û›HØ[YK[ÜšYÚ[ˆ™[]]™H›Ý]\ÈÜˆ^XÚ]È\Ý[˜][ÛœÈ\™H[ÝÙYˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆ˜\ˆ\™Ù]›ÛPÛÙ\ÈH™]È\ÝÝš[™ÏŠ
NÂˆYˆ
›ÛÝ•žQÙ]›Ü\J\™Ù]›ÛPÛÙ\È‹Ý]˜\ˆ›Û\Ñ[[Y[
H	‰ˆ›Û\Ñ[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™\œ˜^JBˆÂˆ›Ü™XXÚ
˜\ˆ›ÛH[ˆ›Û\Ñ[[Y[‘[[Y\˜]P\œ˜^J
JBˆÂˆ˜\ˆ›ÛPÛÙHH›ÛK‘Ù]Ýš[™Ê
HÏÈˆŽÂˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ›ÛPÛÙJJBˆÂˆ\™Ù]›ÛPÛÙ\ËY
›ÛPÛÙK•š[J
K•Õ\\’[˜\šX[

JNÂˆBˆBˆB‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ]JHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ›ÙJJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜[Y][Û—Ù˜Z[Y‹ˆY\ÜØYÙHH]H[™›ÙH\™H™\]Z\™Yˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆ˜\ˆXÝÜ•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆ˜\ˆXÝÜ‘[XZ[HˆŽÂ‚ˆYˆ
XÝÜ•\Ù\’Y\È›Ý[
BˆÂˆ]ØZ]\Ú[™È˜\ˆ\Ù\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ[XZ[ˆ”“ÓH\Ý\Ù\œÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆ\Ù\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹XÝÜ•\Ù\’Y•˜[YJNÂˆXÝÜ‘[XZ[H
]ØZ]\Ù\ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
JOË•ÔÝš[™Ê
HÏÈˆŽÂˆB‚ˆ˜\ˆ›ÝYšXØ][Û’Ù^HH›ÛÝ•žQÙ]›Ü\J››ÝYšXØ][Û’Ù^H‹Ý]˜\ˆÙ^Q[[Y[
BˆÈÙ^Q[[Y[‘Ù]Ýš[™Ê
HÏÈˆ‚ˆˆˆŽÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ›ÝYšXØ][Û’Ù^JJBˆÂˆ›ÝYšXØ][Û’Ù^HH	”“ÑS“ÕPÑK^Ñ]U[YSÙ™œÙ]•]Ó›ÝÎž^^^SSY[\ÜÙ™™ŸHŽÂˆB‚ˆÝZY›ÝYšXØ][Û’YÂˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È›ÙXÝ[Û—Û›ÝYšXØ][Û—Ù]™[È
ˆ›ÝYšXØ][Û—ÚÙ^Kˆ[Ù[WÚÙ^KˆÙ]™\š]Kˆ]Kˆ›ÙKˆ\™Ù]Ü›ÛWØÛÙ\ËˆÛÝ\˜ÙWÜ›Ý]KˆÛÝ\˜ÙWÙ[]WÝ\KˆXÝ[Û—Ý\›ˆÜ™X]YØžWÝ\Ù\—ÚYˆÜ™X]YØžWÙ[XZ[ˆ
BˆSQTÈ
ˆ›ÝYšXØ][Û—ÚÙ^Kˆ[Ù[WÚÙ^KˆÙ]™\š]Kˆ]Kˆ›ÙKˆ\™Ù]Ü›ÛWØÛÙ\ËˆÛÝ\˜ÙWÜ›Ý]Kˆ	ÛX[X[ÜÞ\Ý[WÛ›ÝXÙIËˆXÝ[Û—Ý\›ˆÜ™X]YØžWÝ\Ù\—ÚYˆÜ™X]YØžWÙ[XZ[ˆ
Bˆ‘UT“’S‘È›ÙXÝ[Û—Û›ÝYšXØ][Û—Ù]™[ÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ››ÝYšXØ][Û—ÚÙ^H‹›ÝYšXØ][Û’Ù^JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›[Ù[WÚÙ^H‹[Ù[RÙ^K•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÙ]™\š]H‹Ù]™\š]K•š[J
K•ÓÝÙ\’[˜\šX[

JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ]H‹]K•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜›ÙH‹›ÙK•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\™Ù]Ü›ÛWØÛÙ\È‹\™Ù]›ÛPÛÙ\Ë‘\Ý[˜Ý
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJK•Ð\œ˜^J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÛÝ\˜ÙWÜ›Ý]H‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÛÝ\˜ÙT›Ý]JHÈ
Øš™XÝ
Q“[•˜[YHˆÛÝ\˜ÙT›Ý]K•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝ[Û—Ý\›‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÝ[Û•\›
HÈ
Øš™XÝ
Q“[•˜[YHˆXÝ[Û•\›•š[J
JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜Ü™X]YØžWÝ\Ù\—ÚY‹XÝÜ•\Ù\’Y\È[È
Øš™XÝ
Q“[•˜[YHˆXÝÜ•\Ù\’Y•˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜Ü™X]YØžWÙ[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJXÝÜ‘[XZ[
HÈ
Øš™XÝ
Q“[•˜[YHˆXÝÜ‘[XZ[
NÂ‚ˆ›ÝYšXØ][Û’YH
ÝZY
J]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈÝZY‘[\JNÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒH›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆÙ[\ˆ›Ý[™][Ûˆ‹ˆÝ]\ÈH˜Ü™X]Y‹ˆ›ÝYšXØ][Û’Yˆ›ÝYšXØ][Û’Ù^KˆY\ÜØYÙHH”›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆØ\ÈÜ™X]Yˆ›È[XZ[Ø\ÈÙ[ˆ‚ˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÜ›ÙXÝ[Û‹Û›ÝYšXØ][ÛœËØXÚÛ›ÝÛYÙH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
ÛÛ^”™\]Y\Ý’XY\œËÛÛZ[œÒÙ^J–T›Ú™XÝ[ÙKUšY]ËP\ËU\Ù\ˆŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHšY]×Ø\×Ü™XYÛÛ›H‹ˆY\ÜØYÙHH•Üš]HXÝ[ÛœÈ\™H\ØX›YÚ[H\Ú[™ÈYZ[š\Ý˜]ÜˆšY]ËP\È™]šY]Ëˆ^]™]šY]ÈÈXÚÛ›ÝÛYÙH›ÝYšXØ][ÛœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆ˜\ˆY™™XÝ]™U\Ù\’YH]ØZ]›Ú™XÝ[ÙT™\ÛÛ™QY™™XÝ]™T›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\’Y\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆYˆ
Y™™XÝ]™U\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹ˆY\ÜØYÙHHH›Ú™XÝ[ÙHÙ\ÜÚ[Ûˆ\È™\]Z\™YÈXÚÛ›ÝÛYÙH›ÙXÝ[Ûˆ›ÝYšXØ][ÛœËˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ\Ú[™È˜\ˆØÝ[Y[H]ØZ]œÛÛ‘ØÝ[Y[”\œÙP\Þ[˜ÊÛÛ^”™\]Y\Ý›ÙJNÂˆ˜\ˆ›ÛÝHØÝ[Y[”›ÛÝ[[Y[Â‚ˆ˜\ˆ›ÝYšXØ][Û’Y^H›ÛÝ•žQÙ]›Ü\J››ÝYšXØ][Û’Y‹Ý]˜\ˆY[[Y[
HÈY[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆYˆ
QÝZY•žT\œÙJ›ÝYšXØ][Û’Y^Ý]˜\ˆ›ÝYšXØ][Û’Y
JBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH˜[Y][Û—Ù˜Z[Y‹ˆY\ÜØYÙHH››ÝYšXØ][Û’Y\È™\]Z\™Yˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í˜Y™\]Y\Ý
NÂˆB‚ˆ˜\ˆ›ÝHH›ÛÝ•žQÙ]›Ü\J˜XÚÛ›ÝÛYÛY[›ÝH‹Ý]˜\ˆ›ÝQ[[Y[
HÈ›ÝQ[[Y[‘Ù]Ýš[™Ê
HÏÈˆˆˆˆŽÂˆ˜\ˆ\Ù\ÛÛ^H]ØZ]›Ú™XÝ[ÙQÙ]›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\ÛÛ^\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YJNÂˆ˜\ˆš\ÚX›S›ÝYšXØ][ÛœÈH]ØZ]›Ú™XÝ[ÙSØYš\ÚX›T›ÙXÝ[Û“›ÝYšXØ][ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YK\Ù\ÛÛ^”›ÛPÛÙ\ËŒ
NÂ‚ˆ˜\ˆØ[”ÙYS›ÝYšXØ][ÛˆHš\ÚX›S›ÝYšXØ][ÛœË[žJ][HO‚ˆ][K•žQÙ]˜[YJ››ÝYšXØ][Û’Y‹Ý]˜\ˆ˜[YJBˆ	‰ˆ˜[YH\ÈÝZYYˆ	‰ˆYOH›ÝYšXØ][Û’Yˆ
NÂ‚ˆYˆ
XØ[”ÙYS›ÝYšXØ][ÛŠBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH››ÝÙ›Ý[™ÛÜ—Û›ÝÝš\ÚX›H‹ˆY\ÜØYÙHH•H™\]Y\ÝY›ÝYšXØ][ÛˆØ\È›Ý›Ý[™Üˆ\È›Ýš\ÚX›HÈHÝ\œ™[\Ù\‹ˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\Í›Ý›Ý[™
NÂˆB‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È›ÙXÝ[Û—Û›ÝYšXØ][Û—ØXÚÛ›ÝÛYÛY[È
ˆ›ÝYšXØ][Û—ÚYˆXÚÛ›ÝÛYÙYØžWÝ\Ù\—ÚYˆXÚÛ›ÝÛYÙYØžWÙ[XZ[ˆXÚÛ›ÝÛYÛY[Û›ÝBˆ
BˆSQTÈ
ˆ›ÝYšXØ][Û—ÚYˆXÚÛ›ÝÛYÙYØžWÝ\Ù\—ÚYˆXÚÛ›ÝÛYÙYØžWÙ[XZ[ˆXÚÛ›ÝÛYÛY[Û›ÝBˆ
BˆÓˆÓÓ‘“PÕ
›ÝYšXØ][Û—ÚYXÚÛ›ÝÛYÙYØžWÝ\Ù\—ÚY
BˆÈTUHÑUˆXÚÛ›ÝÛYÙYØ]H›ÝÊ
KˆXÚÛ›ÝÛYÛY[Û›ÝHHVÓQQ˜XÚÛ›ÝÛYÛY[Û›ÝNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ››ÝYšXØ][Û—ÚY‹›ÝYšXØ][Û’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÚÛ›ÝÛYÙYØžWÝ\Ù\—ÚY‹Y™™XÝ]™U\Ù\’Y•˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÚÛ›ÝÛYÙYØžWÙ[XZ[‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ\Ù\ÛÛ^‘[XZ[
HÈ
Øš™XÝ
Q“[•˜[YHˆ\Ù\ÛÛ^‘[XZ[
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÚÛ›ÝÛYÛY[Û›ÝH‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ›ÝJHÈ
Øš™XÝ
Q“[•˜[YHˆ›ÝJNÂ‚ˆ]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒH›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆÙ[\ˆ›Ý[™][Ûˆ‹ˆÝ]\ÈH˜XÚÛ›ÝÛYÙY‹ˆ›ÝYšXØ][Û’YˆXÚÛ›ÝÛYÙYžQ[XZ[H\Ù\ÛÛ^‘[XZ[ˆY\ÜØYÙHH”›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆØ\ÈXÚÛ›ÝÛYÙYˆ‚ˆJNÂŸJNÂ‚•\ÚÏÝZYÏˆ›Ú™XÝ[ÙT™\ÛÛ™QY™™XÝ]™T›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\’Y\Þ[˜ÊÛÛ^ÛÛ^œÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[ÛŠBžÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆ™]\›ˆ\ÚË‘œ›ÛT™\Ý[
Ù\ÜÚ[Û•\Ù\’Y
NÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ
Ýš[™È[XZ[Ýš[™È\Ü^S˜[YKÝš[™Ö×H›ÛPÛÙ\ÊOˆ›Ú™XÝ[ÙQÙ]›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\ÛÛ^\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆÝZY\Ù\’Y
BžÂˆ˜\ˆ[XZ[HˆŽÂˆ˜\ˆ\Ü^S˜[YHHˆŽÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ\Ù\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆÓÐSTÐÑJ[XZ[	ÉÊKˆÓÐSTÐÑJ\Ü^WÛ˜[YK[XZ[	ÉÊBˆ”“ÓH\Ý\Ù\œÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ\Ù\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]\Ù\ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ[XZ[H™XY\‹‘Ù]Ýš[™Ê
NÂˆ\Ü^S˜[YHH™XY\‹‘Ù]Ýš[™ÊJNÂˆBˆB‚ˆ˜\ˆ›ÛPÛÙ\ÈH™]È\ÝÝš[™ÏŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆ›ÛPÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕTÕSÕ‹œ›ÛWØÛÙBˆ”“ÓH\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜Bˆ“ÒSˆ\Ü›Û\È‚ˆÓˆ‹˜\Ü›ÛWÚYH\˜K˜\Ü›ÛWÚYˆS‘‹š\×ØXÝ]™HHYBˆÒT‘H\˜K\Ù\—ÚYH\Ù\—ÚYˆS‘\˜Kš\×ØXÝ]™HHYBˆÔ‘Tˆ–H‹œ›ÛWØÛÙNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ›ÛPÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]›ÛPÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ›ÛPÛÙ\ËY
™XY\‹‘Ù]Ýš[™Ê
JNÂˆBˆB‚ˆ™]\›ˆ
[XZ[\Ü^S˜[YK›ÛPÛÙ\Ë•Ð\œ˜^J
JNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ\ÝXÝ[Û˜\žOÝš[™ËØš™XÝÏˆ›Ú™XÝ[ÙSØYš\ÚX›T›ÙXÝ[Û“›ÝYšXØ][ÛœÐ\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆÝZY\Ù\’YˆÝš[™Ö×H›ÛPÛÙ\Ëˆ[[Z]
BžÂˆ˜\ˆ\Ù\”›ÛTÙ]H›ÛPÛÙ\Ë•Ò\ÚÙ]
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂˆ˜\ˆ™\Ý[ÈH™]È\ÝXÝ[Û˜\žOÝš[™ËØš™XÝÏŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ‹œ›ÙXÝ[Û—Û›ÝYšXØ][Û—Ù]™[ÚYˆ‹››ÝYšXØ][Û—ÚÙ^Kˆ‹›[Ù[WÚÙ^Kˆ‹œÙ]™\š]Kˆ‹]Kˆ‹˜›ÙKˆ‹\™Ù]Ý\Ù\—ÚYˆ‹\™Ù]Ü›ÛWØÛÙ\ËˆÓÐSTÐÑJ‹œÛÝ\˜ÙWÜ›Ý]K	ÉÊKˆÓÐSTÐÑJ‹˜XÝ[Û—Ý\›	ÉÊKˆ‹˜Ü™X]YØ]ˆVTÕÈ
ˆÑSPÕBˆ”“ÓH›ÙXÝ[Û—Û›ÝYšXØ][Û—ØXÚÛ›ÝÛYÛY[ÈBˆÒT‘HK››ÝYšXØ][Û—ÚYH‹œ›ÙXÝ[Û—Û›ÝYšXØ][Û—Ù]™[ÚYˆS‘K˜XÚÛ›ÝÛYÙYØžWÝ\Ù\—ÚYH\Ù\—ÚYˆ
HTÈXÚÛ›ÝÛYÙYˆ”“ÓH›ÙXÝ[Û—Û›ÝYšXØ][Û—Ù]™[È‚ˆÒT‘H‹š\×ØXÝ]™HHYBˆS‘
‹™^\™\×Ø]TÈ•SÔˆ‹™^\™\×Ø]ˆ›ÝÊ
JBˆS‘
‹\™Ù]Ý\Ù\—ÚYTÈ•SÔˆ‹\™Ù]Ý\Ù\—ÚYH\Ù\—ÚY
BˆÔ‘Tˆ–H‹˜Ü™X]YØ]TÐÂˆSRU[Z]Âˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›[Z]‹X]Û[\
[Z]KL
JNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ˜\ˆ\™Ù]\Ù\’YH™XY\‹’\Ñ“[
ŠHÈ
ÝZYÊ[[ˆ™XY\‹‘Ù]ÝZY
ŠNÂˆ˜\ˆ\™Ù]›ÛPÛÙ\ÈH™XY\‹’\Ñ“[
ÊHÈ\œ˜^K‘[\OÝš[™ÏŠ
Hˆ™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠÊNÂ‚ˆ˜\ˆ›ÛUš\ÚX›HH\™Ù]›ÛPÛÙ\Ë“[™ÝOH\™Ù]›ÛPÛÙ\Ë[žJ›ÛHOˆ\Ù\”›ÛTÙ]ÛÛZ[œÊ›ÛJJNÂˆYˆ
\›ÛUš\ÚX›JBˆÂˆÛÛ[YNÂˆB‚ˆ™\Ý[ËY
™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝÏ‚ˆÂˆÈ››ÝYšXØ][Û’Y—HH™XY\‹‘Ù]ÝZY

KˆÈ››ÝYšXØ][Û’Ù^H—HH™XY\‹‘Ù]Ýš[™ÊJKˆÈ›[Ù[RÙ^H—HH™XY\‹‘Ù]Ýš[™ÊŠKˆÈœÙ]™\š]H—HH™XY\‹‘Ù]Ýš[™ÊÊKˆÈ]H—HH™XY\‹‘Ù]Ýš[™Ê
KˆÈ˜›ÙH—HH™XY\‹‘Ù]Ýš[™ÊJKˆÈ\™Ù]\Ù\’Y—HH\™Ù]\Ù\’YˆÈ\™Ù]›ÛPÛÙ\È—HH\™Ù]›ÛPÛÙ\ËˆÈœÛÝ\˜ÙT›Ý]H—HH™XY\‹‘Ù]Ýš[™Ê
KˆÈ˜XÝ[Û•\›—HH™XY\‹‘Ù]Ýš[™ÊJKˆÈ˜Ü™X]Y]—HH™XY\‹‘Ù]]U[YJL
KˆÈ˜XÚÛ›ÝÛYÙY—HH™XY\‹‘Ù]›ÛÛX[ŠLJBˆJNÂˆB‚ˆ™]\›ˆ™\Ý[ÎÂŸB‹ËÈŒH›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆÙ[\ˆ›Ý[™][ÛˆHS‘‚‹ËÈŒÈ›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜Ù\È
È›Ý][™È[\ÈHÕT•˜\“X\Ù]
‹Ø\KÜ›ÙXÝ[Û‹Û›ÝYšXØ][ÛœËÜ™Y™\™[˜Ù\ËÜÝ[[X\žH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆY™™XÝ]™U\Ù\’YH]ØZ]›Ú™XÝ[ÙT™\ÛÛ™QY™™XÝ]™T›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\’Y\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆYˆ
Y™™XÝ]™U\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹ˆY\ÜØYÙHHH›Ú™XÝ[ÙHÙ\ÜÚ[Ûˆ\È™\]Z\™YÈšY]È›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜Ù\Ëˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆ\Ù\ÛÛ^H]ØZ]›Ú™XÝ[ÙQÙ]›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\ÛÛ^\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YJNÂˆ˜\ˆ›ÛPÛÙ\ÈH\Ù\ÛÛ^”›ÛPÛÙ\ÈÏÈ\œ˜^K‘[\OÝš[™ÏŠ
NÂ‚ˆ˜\ˆ›Ý][™Ô[\ÈH]ØZ]›Ú™XÝ[ÙLŒÓØY›Ý][™Ô[\Ð\Þ[˜ÊÛÛ›™XÝ[Û‹›ÛPÛÙ\ÊNÂˆ˜\ˆ™Y™\™[˜Ù\ÈH]ØZ]›Ú™XÝ[ÙLŒÓØY\Ù\”™Y™\™[˜Ù\Ð\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YJNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒÈ›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜Ù\È
È›Ý][™È[\È‹ˆÝ[[X\žHH™]ÂˆÂˆY™™XÝ]™U\Ù\’Yˆ\Ù\‘[XZ[H\Ù\ÛÛ^‘[XZ[ˆ\Ù\‘\Ü^S˜[YHH\Ù\ÛÛ^‘\Ü^S˜[YKˆ›ÛPÛÙ\Ëˆ›Ý][™Ô[PÛÝ[H›Ý][™Ô[\ËÛÝ[ˆ™Y™\™[˜ÙPÛÝ[H™Y™\™[˜Ù\ËÛÝ[ˆ]]Y™Y™\™[˜ÙPÛÝ[H™Y™\™[˜Ù\ËÛÝ[
][HOˆ][K“]]Y[[]È\È›Ý[	‰ˆ][K“]]Y[[]Èˆ]U[YSÙ™œÙ]•]Ó›ÝÊKˆ[\\ØX›Y™Y™\™[˜ÙPÛÝ[H™Y™\™[˜Ù\ËÛÝ[
][HOˆZ][K’[\[˜X›Y
Kˆ[XZ[[˜X›Y™Y™\™[˜ÙPÛÝ[H™Y™\™[˜Ù\ËÛÝ[
][HOˆ][K‘[XZ[[˜X›Y
Kˆ[XZ[[]™\žTÛXÞHH‘[XZ[[]™\žH™[XZ[œÈ\ØX›Y›ÜˆŒËˆ]\™H[XZ[[]™\žH]\Ý\ÙHHÚ\™Y›ÝšY\ˆØY™]HØ]Kˆ‹ˆ™Y™\™[˜ÙTØÛÜHH\Ù\—Û[Ù[WÜÙ]™\š]H‚ˆKˆ›Ý][™Ô[\Ëˆ™Y™\™[˜Ù\ÂˆJNÂŸJNÂ‚˜\“X\Ù]
‹Ø\KÜ›ÙXÝ[Û‹Û›ÝYšXØ][ÛœËÜ›Ý][™Ë\[\È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆY™™XÝ]™U\Ù\’YH]ØZ]›Ú™XÝ[ÙT™\ÛÛ™QY™™XÝ]™T›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\’Y\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠNÂˆYˆ
Y™™XÝ]™U\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹ˆY\ÜØYÙHHH›Ú™XÝ[ÙHÙ\ÜÚ[Ûˆ\È™\]Z\™YÈšY]È›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ›Ý][™È[\Ëˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆ˜\ˆ\Ù\ÛÛ^H]ØZ]›Ú™XÝ[ÙQÙ]›ÙXÝ[Û“›ÝYšXØ][Û•\Ù\ÛÛ^\Þ[˜ÊÛÛ›™XÝ[Û‹Y™™XÝ]™U\Ù\’Y•˜[YJNÂˆ˜\ˆ›ÛPÛÙ\ÈH\Ù\ÛÛ^”›ÛPÛÙ\ÈÏÈ\œ˜^K‘[\OÝš[™ÏŠ
NÂˆ˜\ˆ›Ý][™Ô[\ÈH]ØZ]›Ú™XÝ[ÙLŒÓØY›Ý][™Ô[\Ð\Þ[˜ÊÛÛ›™XÝ[Û‹›ÛPÛÙ\ÊNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒÈ›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜Ù\È
È›Ý][™È[\È‹ˆÛÝ[H›Ý][™Ô[\ËÛÝ[ˆY™™XÝ]™U\Ù\’Yˆ›ÛPÛÙ\Ëˆ›Ý][™Ô[\ÂˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÜ›ÙXÝ[Û‹Û›ÝYšXØ][ÛœËÜ™Y™\™[˜Ù\È‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÙ\ÜÚ[Û•\Ù\’YHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•\Ù\’Y
ÛÛ^
NÂˆYˆ
Ù\ÜÚ[Û•\Ù\’Y\È[
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈHœÙ\ÜÚ[Û—Ü™\]Z\™Y‹ˆY\ÜØYÙHHH›Ú™XÝ[ÙHÙ\ÜÚ[Ûˆ\È™\]Z\™YÈ\]H›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜Ù\Ëˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍU[˜]]Üš^™Y
NÂˆB‚ˆXÝ[Û˜\žOÝš[™ËœÛÛ‘[[Y[È^[ØYÂˆžBˆÂˆ^[ØYH]ØZ]œÛÛ”Ù\šX[^™\‹‘\Ù\šX[^™P\Þ[˜ÏXÝ[Û˜\žOÝš[™ËœÛÛ‘[[Y[ŠÛÛ^”™\]Y\Ý›ÙJNÂˆBˆØ]Ú
œÛÛ‘^Ù\[Ûˆ^
BˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈHš[˜[YÚœÛÛˆ‹ˆY\ÜØYÙHH”™Y™\™[˜ÙH^[ØY]\Ý™H˜[Y”ÓÓ‹ˆ‹ˆ]Z[H^“Y\ÜØYÙBˆJNÂˆB‚ˆYˆ
^[ØY\È[
BˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈH˜[Y][Û—Ù˜Z[Y‹ˆY\ÜØYÙHH”™Y™\™[˜ÙH^[ØY\È™\]Z\™Yˆ‚ˆJNÂˆB‚ˆ˜\ˆ[Ù[RÙ^HH›Ú™XÝ[ÙLŒÒœÛÛ”Ýš[™Ê^[ØY›[Ù[RÙ^HŠK•š[J
K•Õ\\’[˜\šX[

NÂˆ˜\ˆÙ]™\š]HH›Ú™XÝ[ÙLŒÒœÛÛ”Ýš[™Ê^[ØYœÙ]™\š]H‹š[™›ÈŠK•š[J
K•ÓÝÙ\’[˜\šX[

NÂˆ˜\ˆ[\[˜X›YH›Ú™XÝ[ÙLŒÒœÛÛ›ÛÛ
^[ØYš[\[˜X›Y‹YJNÂˆ˜\ˆ]]Y[[]ÈH›Ú™XÝ[ÙLŒÒœÛÛ‘]U[YSÙ™œÙ]
^[ØY›]]Y[[]ÈŠNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[Ù[RÙ^JJBˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈH˜[Y][Û—Ù˜Z[Y‹ˆY\ÜØYÙHH›[Ù[RÙ^H\È™\]Z\™Yˆ‚ˆJNÂˆB‚ˆYˆ
T›Ú™XÝ[ÙLŒÕ˜[YÙ]™\š]JÙ]™\š]JJBˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈH˜[Y][Û—Ù˜Z[Y‹ˆY\ÜØYÙHHœÙ]™\š]H]\Ý™HÛ™HÙˆ[™›ËØ\›š[™ËÜš]XØ[ÝXØÙ\ÜËÜˆ\œ›Ü‹ˆ‚ˆJNÂˆB‚ˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È›ÙXÝ[Û—Û›ÝYšXØ][Û—Ý\Ù\—Ü™Y™\™[˜Ù\È
ˆ\Ù\—ÚYˆ[Ù[WÚÙ^KˆÙ]™\š]Kˆ[—Ø\Ù[˜X›Yˆ[XZ[Ù[˜X›Yˆ]]YÝ[[ˆ\]YØžWÝ\Ù\—ÚYˆÜ™X]YØ]ˆ\]YØ]ˆ
BˆSQTÈ
ˆ\Ù\—ÚYˆ[Ù[WÚÙ^KˆÙ]™\š]Kˆ[—Ø\Ù[˜X›YˆSÑKˆ]]YÝ[[ˆ\]YØžWÝ\Ù\—ÚYˆ“ÕÊ
Kˆ“ÕÊ
Bˆ
BˆÓˆÓÓ‘“PÕ
\Ù\—ÚY[Ù[WÚÙ^KÙ]™\š]JBˆÈTUHÑUˆ[—Ø\Ù[˜X›YHVÓQQš[—Ø\Ù[˜X›Yˆ[XZ[Ù[˜X›YHSÑKˆ]]YÝ[[HVÓQQ›]]YÝ[[ˆ\]YØžWÝ\Ù\—ÚYHVÓQQ\]YØžWÝ\Ù\—ÚYˆ\]YØ]H“ÕÊ
Bˆ‘UT“’S‘È›ÙXÝ[Û—Û›ÝYšXØ][Û—Ý\Ù\—Ü™Y™\™[˜ÙWÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›[Ù[WÚÙ^H‹[Ù[RÙ^JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÙ]™\š]H‹Ù]™\š]JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJš[—Ø\Ù[˜X›Y‹[\[˜X›Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›]]YÝ[[‹
Øš™XÝÊ[]]Y[[]ÈÏÈ“[•˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\]YØžWÝ\Ù\—ÚY‹Ù\ÜÚ[Û•\Ù\’Y•˜[YJNÂ‚ˆ˜\ˆ™Y™\™[˜ÙRYH
ÝZY
J]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈÝZY‘[\JNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒÈ›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜Ù\È
È›Ý][™È[\È‹ˆÝ]\ÈHœ™Y™\™[˜ÙWÜØ]™Y‹ˆ™Y™\™[˜ÙRYˆ\Ù\’YHÙ\ÜÚ[Û•\Ù\’Y•˜[YKˆ[Ù[RÙ^KˆÙ]™\š]Kˆ[\[˜X›Yˆ[XZ[[˜X›YH˜[ÙKˆ]]Y[[]ËˆY\ÜØYÙHH”›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜ÙHØ\ÈØ]™Yˆ[XZ[™[XZ[œÈ\ØX›Y›ÜˆŒËˆ‚ˆJNÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KÜ›ÙXÝ[Û‹Û›ÝYšXØ][ÛœËÜ›Ý][™Ë\[\ËÝÙÙÛH‹\Þ[˜È
ÛÛ^ÛÛ^
HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆYˆ
X]ØZ]™\]Y\Ý\Ù\Ø[XØÙ\ÜÕ\Ù\YZ[š\Ý˜][Û\Þ[˜ÊÛÛ^ÛÛ›™XÝ[ÛŠJBˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆÝ]\ÈH™›Ü˜šY[ˆ‹ˆY\ÜØYÙHH“Û›HYZ[š\Ý˜]ÜœÈÜˆ]]Üš^™Y›ÙXÝ[ÛˆÜ\˜]ÜœÈØ[ˆ\]H›ÝYšXØ][Ûˆ›Ý][™È[\Ëˆ‚ˆKÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍÑ›Ü˜šY[ŠNÂˆB‚ˆXÝ[Û˜\žOÝš[™ËœÛÛ‘[[Y[È^[ØYÂˆžBˆÂˆ^[ØYH]ØZ]œÛÛ”Ù\šX[^™\‹‘\Ù\šX[^™P\Þ[˜ÏXÝ[Û˜\žOÝš[™ËœÛÛ‘[[Y[ŠÛÛ^”™\]Y\Ý›ÙJNÂˆBˆØ]Ú
œÛÛ‘^Ù\[Ûˆ^
BˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈHš[˜[YÚœÛÛˆ‹ˆY\ÜØYÙHH”›Ý][™È[H^[ØY]\Ý™H˜[Y”ÓÓ‹ˆ‹ˆ]Z[H^“Y\ÜØYÙBˆJNÂˆB‚ˆYˆ
^[ØY\È[
BˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈH˜[Y][Û—Ù˜Z[Y‹ˆY\ÜØYÙHH”›Ý][™È[H^[ØY\È™\]Z\™Yˆ‚ˆJNÂˆB‚ˆ˜\ˆ[RÙ^HH›Ú™XÝ[ÙLŒÒœÛÛ”Ýš[™Ê^[ØYœ[RÙ^HŠK•š[J
NÂˆ˜\ˆ\ÐXÝ]™HH›Ú™XÝ[ÙLŒÒœÛÛ›ÛÛ
^[ØYš\ÐXÝ]™H‹YJNÂˆ˜\ˆY˜][[\[˜X›YH›Ú™XÝ[ÙLŒÒœÛÛ“[X›P›ÛÛ
^[ØY™Y˜][[\[˜X›YŠNÂˆ˜\ˆ[ÝÕ\Ù\“ÜÝ]H›Ú™XÝ[ÙLŒÒœÛÛ“[X›P›ÛÛ
^[ØY˜[ÝÕ\Ù\“ÜÝ]ŠNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[RÙ^JJBˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈH˜[Y][Û—Ù˜Z[Y‹ˆY\ÜØYÙHHœ[RÙ^H\È™\]Z\™Yˆ‚ˆJNÂˆB‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆTUH›ÙXÝ[Û—Û›ÝYšXØ][Û—Ü›Ý][™×Ü[\ÂˆÑUˆ\×ØXÝ]™HH\×ØXÝ]™KˆY˜][Ú[—Ø\Ù[˜X›YHÓÐSTÐÑJY˜][Ú[—Ø\Ù[˜X›YY˜][Ú[—Ø\Ù[˜X›Y
Kˆ[Ý×Ý\Ù\—ÛÜÛÝ]HÓÐSTÐÑJ[Ý×Ý\Ù\—ÛÜÛÝ][Ý×Ý\Ù\—ÛÜÛÝ]
Kˆ[Ý×Ù[XZ[Ù[]™\žHHSÑKˆ\]YØ]H“ÕÊ
BˆÒT‘H[WÚÙ^HH[WÚÙ^Bˆ‘UT“’S‘È›ÙXÝ[Û—Û›ÝYšXØ][Û—Ü›Ý][™×Ü[WÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ[WÚÙ^H‹[RÙ^JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJš\×ØXÝ]™H‹\ÐXÝ]™JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™Y˜][Ú[—Ø\Ù[˜X›Y‹
Øš™XÝÊYY˜][[\[˜X›YÏÈ“[•˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜[Ý×Ý\Ù\—ÛÜÛÝ]‹
Øš™XÝÊX[ÝÕ\Ù\“ÜÝ]ÏÈ“[•˜[YJNÂ‚ˆ˜\ˆ™\Ý[H]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂˆYˆ
™\Ý[\È[
BˆÂˆ™]\›ˆ™\Ý[Ë“›Ý›Ý[™
™]ÂˆÂˆÝ]\ÈH››ÝÙ›Ý[™‹ˆY\ÜØYÙHH	“›È›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ›Ý][™È[HØ\È›Ý[™›ÜˆÜ[RÙ^_Kˆ‚ˆJNÂˆB‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆ[Ù[HHŒŒÈ›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜Ù\È
È›Ý][™È[\È‹ˆÝ]\ÈHœ›Ý][™×Ü[WÝ\]Y‹ˆ›Ý][™Ô[RYH
ÝZY
\™\Ý[ˆ[RÙ^Kˆ\ÐXÝ]™KˆY˜][[\[˜X›Yˆ[ÝÕ\Ù\“ÜÝ]ˆ[ÝÑ[XZ[[]™\žHH˜[ÙKˆY\ÜØYÙHH”›Ý][™È[HØ\È\]Yˆ[XZ[[]™\žH™[XZ[œÈ\ØX›Y›ÜˆŒËˆ‚ˆJNÂŸJNÂ‚œÝ]XÈ\Þ[˜È\ÚÏ\Ý›Ú™XÝ[ÙLŒÔ›Ý][™Ô[Oˆ›Ú™XÝ[ÙLŒÓØY›Ý][™Ô[\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™Ö×H›ÛPÛÙ\ÊBžÂˆ˜\ˆ[\ÈH™]È\Ý›Ú™XÝ[ÙLŒÔ›Ý][™Ô[OŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ›ÙXÝ[Û—Û›ÝYšXØ][Û—Ü›Ý][™×Ü[WÚYˆ[WÚÙ^Kˆ[Ù[WÚÙ^KˆÙ]™\š]Kˆ\™Ù]Ü›ÛWØÛÙ\ËˆY˜][Ú[—Ø\Ù[˜X›Yˆ[Ý×Ý\Ù\—ÛÜÛÝ]ˆ[Ý×Ù[XZ[Ù[]™\žKˆ\×ØXÝ]™Kˆ[WÙ\ØÜš\[Û‹ˆÜ™X]YØ]ˆ\]YØ]ˆ”“ÓH›ÙXÝ[Û—Û›ÝYšXØ][Û—Ü›Ý][™×Ü[\ÂˆÒT‘H\×ØXÝ]™HH•QBˆS‘
ˆØ\™[˜[]J\™Ù]Ü›ÛWØÛÙ\ÊHHˆÔˆ\™Ù]Ü›ÛWØÛÙ\È	‰ˆ›ÛWØÛÙ\Âˆ
BˆÔ‘Tˆ–H[Ù[WÚÙ^KÙ]™\š]K[WÚÙ^NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›ÛWØÛÙ\È‹›ÛPÛÙ\ÈÏÈ\œ˜^K‘[\OÝš[™ÏŠ
JNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ[\ËY
™]È›Ú™XÝ[ÙLŒÔ›Ý][™Ô[Jˆ™XY\‹‘Ù]ÝZY

Kˆ™XY\‹‘Ù]Ýš[™ÊJKˆ™XY\‹‘Ù]Ýš[™ÊŠKˆ™XY\‹‘Ù]Ýš[™ÊÊKˆ™XY\‹’\Ñ“[

HÈ\œ˜^K‘[\OÝš[™ÏŠ
Hˆ™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠ
Kˆ™XY\‹‘Ù]›ÛÛX[ŠJKˆ™XY\‹‘Ù]›ÛÛX[ŠŠKˆ™XY\‹‘Ù]›ÛÛX[ŠÊKˆ™XY\‹‘Ù]›ÛÛX[Š
Kˆ™XY\‹’\Ñ“[
JHÈÝš[™Ë‘[\Hˆ™XY\‹‘Ù]Ýš[™ÊJKˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠL
Kˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠLJBˆ
JNÂˆB‚ˆ™]\›ˆ[\ÎÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ\Ý›Ú™XÝ[ÙLŒÕ\Ù\”™Y™\™[˜ÙOˆ›Ú™XÝ[ÙLŒÓØY\Ù\”™Y™\™[˜Ù\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y
BžÂˆ˜\ˆ™Y™\™[˜Ù\ÈH™]È\Ý›Ú™XÝ[ÙLŒÕ\Ù\”™Y™\™[˜ÙOŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ›ÙXÝ[Û—Û›ÝYšXØ][Û—Ý\Ù\—Ü™Y™\™[˜ÙWÚYˆ[Ù[WÚÙ^KˆÙ]™\š]Kˆ[—Ø\Ù[˜X›Yˆ[XZ[Ù[˜X›Yˆ]]YÝ[[ˆ\]YØ]ˆ”“ÓH›ÙXÝ[Û—Û›ÝYšXØ][Û—Ý\Ù\—Ü™Y™\™[˜Ù\ÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆÔ‘Tˆ–H[Ù[WÚÙ^KÙ]™\š]NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™Y™\™[˜Ù\ËY
™]È›Ú™XÝ[ÙLŒÕ\Ù\”™Y™\™[˜ÙJˆ™XY\‹‘Ù]ÝZY

Kˆ™XY\‹‘Ù]Ýš[™ÊJKˆ™XY\‹‘Ù]Ýš[™ÊŠKˆ™XY\‹‘Ù]›ÛÛX[ŠÊKˆ™XY\‹‘Ù]›ÛÛX[Š
Kˆ™XY\‹’\Ñ“[
JHÈ[ˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠJKˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠŠBˆ
JNÂˆB‚ˆ™]\›ˆ™Y™\™[˜Ù\ÎÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLŒÒœÛÛ”Ýš[™ÊXÝ[Û˜\žOÝš[™ËœÛÛ‘[[Y[ˆ^[ØYÝš[™ÈÙ^KÝš[™È˜[˜XÚÈHˆŠBžÂˆYˆ
\^[ØY•žQÙ]˜[YJÙ^KÝ]˜\ˆ[[Y[
JBˆÂˆ™]\›ˆ˜[˜XÚÎÂˆB‚ˆ™]\›ˆ[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™”Ýš[™ÈÈ[[Y[‘Ù]Ýš[™Ê
HÏÈ˜[˜XÚÈˆ˜[˜XÚÎÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLŒÒœÛÛ›ÛÛ
XÝ[Û˜\žOÝš[™ËœÛÛ‘[[Y[ˆ^[ØYÝš[™ÈÙ^K›ÛÛ˜[˜XÚÊBžÂˆYˆ
\^[ØY•žQÙ]˜[YJÙ^KÝ]˜\ˆ[[Y[
JBˆÂˆ™]\›ˆ˜[˜XÚÎÂˆB‚ˆYˆ
[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™•YJH™]\›ˆYNÂˆYˆ
[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™‘˜[ÙJH™]\›ˆ˜[ÙNÂ‚ˆ™]\›ˆ˜[˜XÚÎÂŸB‚œÝ]XÈ›ÛÛÈ›Ú™XÝ[ÙLŒÒœÛÛ“[X›P›ÛÛ
XÝ[Û˜\žOÝš[™ËœÛÛ‘[[Y[ˆ^[ØYÝš[™ÈÙ^JBžÂˆYˆ
\^[ØY•žQÙ]˜[YJÙ^KÝ]˜\ˆ[[Y[
JBˆÂˆ™]\›ˆ[ÂˆB‚ˆYˆ
[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™•YJH™]\›ˆYNÂˆYˆ
[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™‘˜[ÙJH™]\›ˆ˜[ÙNÂ‚ˆ™]\›ˆ[ÂŸB‚œÝ]XÈ]U[YSÙ™œÙ]È›Ú™XÝ[ÙLŒÒœÛÛ‘]U[YSÙ™œÙ]
XÝ[Û˜\žOÝš[™ËœÛÛ‘[[Y[ˆ^[ØYÝš[™ÈÙ^JBžÂˆYˆ
\^[ØY•žQÙ]˜[YJÙ^KÝ]˜\ˆ[[Y[
H[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™”Ýš[™ÊBˆÂˆ™]\›ˆ[ÂˆB‚ˆ˜\ˆ˜[YHH[[Y[‘Ù]Ýš[™Ê
NÂˆ™]\›ˆ]U[YSÙ™œÙ]•žT\œÙJ˜[YKÝ]˜\ˆ\œÙY
HÈ\œÙY•Õ[š]™\œØ[[YJ
Hˆ[ÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLŒÕ˜[YÙ]™\š]JÝš[™ÈÙ]™\š]JBžÂˆ™]\›ˆÝš[™Ë‘\]X[ÊÙ]™\š]Kš[™›È‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÙ]™\š]KØ\›š[™È‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÙ]™\š]K˜Üš]XØ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÙ]™\š]KœÝXØÙ\ÜÈ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÙ]™\š]K™\œ›Üˆ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂŸB‚‹ËÈŒÈ›ÙXÝ[Ûˆ›ÝYšXØ][Ûˆ™Y™\™[˜Ù\È
È›Ý][™È[\ÈHS‘‚‚‚‚‚‚‚‚‚‚‚‚‚‚‚‹ËÈÌÔ‘TÔ•ÐTWÒ”ÓÓ—ÔÐQ‘WÔÕT•˜\“X\ÜÝ
‹Ø\KÜ™\ÜËÌÌÜ™]šY]È‹\Þ[˜È
™\]Y\Ý™\]Y\Ý
HO‚žÂˆËÈÌÔ‘TÔ•ÐTWÑUPTÑPÓÓ‘’Q×Ñ”“ÓWÑS•—Ñ’VˆËÈÌÔ‘TÔ•ÐTWÔÓÕTÑWÕP“WÐS‘ÓQTÔÐQÑWÑ’Vˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™ÐÛÛ™šYÔ™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™ÐÛÛ™šYÔ™\Ý[\È›Ý[
BˆÂˆ™]\›ˆZ\ÜÚ[™ÐÛÛ™šYÔ™\Ý[ÂˆB‚‚ˆœÛÛ‘ØÝ[Y[ÈØÝ[Y[H[Â‚ˆžBˆÂˆÝš[™È˜]Ð›ÙHH]ØZ]™]ÈÝ™X[T™XY\Š™\]Y\Ý›ÙJK”™XYÑ[™\Þ[˜Ê
NÂ‚ˆØÝ[Y[HÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜]Ð›ÙJBˆÈœÛÛ‘ØÝ[Y[”\œÙJžßHŠBˆˆœÛÛ‘ØÝ[Y[”\œÙJ˜]Ð›ÙJNÂ‚ˆœÛÛ‘[[Y[›ÛÝHØÝ[Y[”›ÛÝ[[Y[Â‚ˆÝš[™È™\Ü\HH›Ú™XÝ[ÙLÌØY™T™XYÝš[™Ê›ÛÝœ™\Ü\H‹\H‹œ™\Ü˜[YHŠNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™\Ü\JJBˆÂˆ™\Ü\HHXØÛÝ[[™È[›ÚXÙH]Z[™\ÜŽÂˆB‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂˆ˜\ˆ™\Ý[H]ØZ]›Ú™XÝ[ÙLÌZ[ØY™Q]X˜\ÙT™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\K›ÛÝ
NÂ‚ˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™\Ý[
NÂˆBˆØ]Ú
^Ù\[Ûˆ^
BˆÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™]ÂˆÂˆ]X˜\ÙP˜XÚÙYHYKˆ\œ›ÜˆHYKˆ™\Ü\HHŒÌ™\Ü™]šY]È‹ˆØ]YÛÜžHH™\œ›Üˆ‹ˆÛÝ\˜ÙUX›HHˆ‹ˆÛÛ[[œÈH™]Ö×HÈ‘\œ›ÜˆˆKˆ›ÝÜÈH\œ˜^K‘[\OØš™XÝÖ×OŠ
Kˆ›ÝÐÛÝ[HˆY\ÜØYÙHH”™\ÜÙ[™\˜][Ûˆ˜Z[Yˆ›È™\Ü›ÝÜÈÙ\™H™]\›™Yˆ‹ˆ^Ù\[Û•\HH^‘Ù]\J
K‘[˜[YKˆ]Z[H^“Y\ÜØYÙBˆJNÂˆBˆš[˜[BˆÂˆØÝ[Y[Ë‘\ÜÜÙJ
NÂˆBŸJNÂ‹ËÈÌÔ‘TÔ•ÐTWÒ”ÓÓ—ÔÐQ‘WÑS‘‚‹ËÈÌWÔ‘PQP“WÔ‘TÔ•S‘×Ñ’ST—ÓÔSÓ”×ÔÕT•˜\“X\Ù]
‹Ø\KÜ™\ÜËÌÌÙš[\‹[Ü[ÛœÈ‹\Þ[˜È

HO‚žÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™ÐÛÛ™šYÔ™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™ÐÛÛ™šYÔ™\Ý[\È›Ý[
BˆÂˆ™]\›ˆZ\ÜÚ[™ÐÛÛ™šYÔ™\Ý[ÂˆB‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆ™\Ý[H]ØZ]›Ú™XÝ[ÙLÌØY™XYX›Qš[\“Ü[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[ÛŠNÂˆ™]\›ˆ™\Ý[Ë’œÛÛŠ™\Ý[
NÂŸJNÂ‹ËÈÌWÔ‘PQP“WÔ‘TÔ•S‘×Ñ’ST—ÓÔSÓ”×ÑS‘‚‚‹ËÈÌÔ‘TÔ•ÐTWÒ”ÓÓ—ÔÐQ‘WÒST”×ÔÕT•‚œÝ]XÈ\Þ[˜È\ÚÏœÜÜ[ÛÛ›™XÝ[Ûˆ›Ú™XÝ[ÙLÌÜ[ÛÛ›™XÝ[Û\Þ[˜ÊTÙ\šXÙT›ÝšY\ˆÙ\šXÙ\ÊBžÂˆ˜\ˆ]TÛÝ\˜ÙHHÙ\šXÙ\Ë‘Ù]Ù\šXÙJ\[ÙŠœÜÜ[]TÛÝ\˜ÙJJH\ÈœÜÜ[]TÛÝ\˜ÙNÂ‚ˆYˆ
]TÛÝ\˜ÙH\È›Ý[
BˆÂˆ™]\›ˆ]ØZ]]TÛÝ\˜ÙK“Ü[ÛÛ›™XÝ[Û\Þ[˜Ê
NÂˆB‚ˆ˜\ˆÛÛ™šYÝ\˜][ÛˆHÙ\šXÙ\Ë‘Ù]Ù\šXÙJ\[ÙŠZXÜ›ÜÛÙ‘^[œÚ[ÛœËÛÛ™šYÝ\˜][Û‹’PÛÛ™šYÝ\˜][ÛŠJH\ÈZXÜ›ÜÛÙ‘^[œÚ[ÛœËÛÛ™šYÝ\˜][Û‹’PÛÛ™šYÝ\˜][ÛŽÂ‚ˆ˜\ˆØ[™Y]\ÈH™]Ö×BˆÂˆÛÛ™šYÝ\˜][ÛÖÈÛÛ›™XÝ[Û”Ýš[™ÜÎ”›Ú™XÝ[ÙH—KˆÛÛ™šYÝ\˜][ÛÖÈÛÛ›™XÝ[Û”Ýš[™ÜÎ‘Y˜][ÛÛ›™XÝ[Ûˆ—KˆÛÛ™šYÝ\˜][ÛÖÈÛÛ›™XÝ[Û”Ýš[™ÜÎ”›Ú™XÝ[YH—KˆÛÛ™šYÝ\˜][ÛÖÈ”“Ò‘PÕSÑWÐÓÓ“‘PÕSÓ—ÔÕ’S‘È—KˆÛÛ™šYÝ\˜][ÛÖÈ”“Ò‘PÕSQWÑUPTÑWÐÓÓ“‘PÕSÓˆ—KˆÛÛ™šYÝ\˜][ÛÖÈ‘UPTÑWÕT“—Kˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÛÛ›™XÝ[Û”Ýš[™Ü××Ô›Ú™XÝ[ÙHŠKˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÛÛ›™XÝ[Û”Ýš[™Ü××ÑY˜][ÛÛ›™XÝ[ÛˆŠKˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÛÛ›™XÝ[Û”Ýš[™Ü××Ô›Ú™XÝ[YHŠKˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐÓÓ“‘PÕSÓ—ÔÕ’S‘ÈŠKˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSQWÑUPTÑWÐÓÓ“‘PÕSÓˆŠKˆ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J‘UPTÑWÕT“ŠBˆNÂ‚ˆÝš[™ÏÈÛÛ›™XÝ[Û”Ýš[™ÈHØ[™Y]\Ë‘š\œÝÜ‘Y˜][
˜[YHOˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜[YJJNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÛÛ›™XÝ[Û”Ýš[™ÊJBˆÂˆ›ÝÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠ“›È]X˜\ÙHÛÛ›™XÝ[ÛˆÝš[™ÈÜˆœÜÜ[]TÛÝ\˜ÙHØ\È]˜Z[X›HÈHÌ™\Ü[™ÈTKˆŠNÂˆB‚ˆ˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ™]\›ˆÛÛ›™XÝ[ÛŽÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLÌØY™T™XYÝš[™ÊœÛÛ‘[[Y[›ÛÝ\˜[\ÈÝš[™Ö×H˜[Y\ÊBžÂˆYˆ
›ÛÝ•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™“Øš™XÝ
BˆÂˆ™]\›ˆÝš[™Ë‘[\NÂˆB‚ˆ›Ü™XXÚ
˜\ˆ˜[YH[ˆ˜[Y\ÊBˆÂˆYˆ
›ÛÝ•žQÙ]›Ü\J˜[YKÝ]˜\ˆ[[Y[
JBˆÂˆYˆ
[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™”Ýš[™ÊBˆÂˆ™]\›ˆ[[Y[‘Ù]Ýš[™Ê
HÏÈÝš[™Ë‘[\NÂˆB‚ˆYˆ
[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™“[X™\ˆ[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™•YH[[Y[•˜[YRÚ[™OHœÛÛ•˜[YRÚ[™‘˜[ÙJBˆÂˆ™]\›ˆ[[Y[•ÔÝš[™Ê
NÂˆBˆBˆB‚ˆ™]\›ˆÝš[™Ë‘[\NÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLÌØY™PØ]YÛÜžJÝš[™È™\Ü\JBžÂˆÝš[™È˜[YHH
™\Ü\HÏÈÝš[™Ë‘[\JK•ÓÝÙ\’[˜\šX[

NÂ‚ˆYˆ

˜[YKÛÛZ[œÊ	›HŠH˜[YKÛÛZ[œÊ[YH[™X]\šX[ŠJH	‰ˆ˜[YKÛÛZ[œÊœØ[\ÈŠJH™]\›ˆWÜØ[\ÈŽÂˆYˆ
˜[YKÛÛZ[œÊœ›Ú™XÝÝ]\ÈŠH
˜[YKÛÛZ[œÊ˜š[YŠH	‰ˆ˜[YKÛÛZ[œÊœ™[XZ[š[™ÈŠJJH™]\›ˆœ›Ú™XÝÜÝ]\ÈŽÂˆYˆ
˜[YKÛÛZ[œÊ˜Ù\YžHŠH˜[YKÛÛZ[œÊ™^[œÙHŠJH™]\›ˆ˜Ù\YžHŽÂˆYˆ
˜[YKÛÛZ[œÊ][^˜][ÛˆŠH	‰ˆ
˜[YKÛÛZ[œÊ›Ý™\ˆŠH˜[YKÛÛZ[œÊ[™\ˆŠJJH™]\›ˆ][^˜][Û—ÛÝ™\—Ý[™\ˆŽÂˆYˆ
˜[YKÛÛZ[œÊ›Ý™\ˆŠH	‰ˆ˜[YKÛÛZ[œÊ[™\ˆŠH	‰ˆ˜[YKÛÛZ[œÊ™[™Ú[™Y\ˆŠH	‰ˆ
˜[YKÛÛZ[œÊœ›Ú™XÝŠH˜[YKÛÛZ[œÊ˜YÙ]ŠJJH™]\›ˆ™[™Ú[™Y\—ÛÝ™\—Ý[™\ˆŽÂˆYˆ

˜[YKÛÛZ[œÊ˜XØ][ÛˆŠH˜[YKÛÛZ[œÊœÈŠJH	‰ˆ˜[YKÛÛZ[œÊ™[™Ú[™Y\ˆŠJH™]\›ˆœ×Ý\ÙYŽÂˆYˆ
˜[YKÛÛZ[œÊ˜š[X›HŠH	‰ˆ˜[YKÛÛZ[œÊ››ÛˆŠJH™]\›ˆ˜š[X›WÛ›Û˜š[X›HŽÂˆYˆ
˜[YKÛÛZ[œÊ[˜š[YŠH˜[YKÛÛZ[œÊš[›ÚXÙH™XY[™\ÜÈŠJH™]\›ˆš[›ÚXÙWÜ™XY[™\ÜÈŽÂˆYˆ
˜[YKÛÛZ[œÊ˜\›Ý˜[›Ý[™XÚÈŠJH™]\›ˆ˜\›Ý˜[Ø›Ý[™XÚÈŽÂˆYˆ
˜[YKÛÛZ[œÊ›Z\ÜÚ[™È[YHŠH˜[YKÛÛZ[œÊ›]H[Y\ÚY]ŠJH™]\›ˆ›Z\ÜÚ[™×Ý[YHŽÂˆYˆ
˜[YKÛÛZ[œÊ›X\™Ú[ˆŠJH™]\›ˆœ›Ú™XÝÛX\™Ú[ˆŽÂˆYˆ
˜[YKÛÛZ[œÊœ˜]HŠH	‰ˆ˜[YKÛÛZ[œÊ™^Ù\[ÛˆŠJH™]\›ˆœ˜]WÙ^Ù\[ÛˆŽÂˆYˆ
˜[YKÛÛZ[œÊœ›Ùš]Xš[]HŠJH™]\›ˆ˜Ý\ÝÛY\—Ü›Ùš]Xš[]HŽÂˆYˆ
˜[YKÛÛZ[œÊ˜ÛÜÙ[Ý]ŠJH™]\›ˆ˜ÛÜÙ[Ý]Ü™XY[™\ÜÈŽÂˆYˆ
˜[YKÛÛZ[œÊš[™Ù™ˆŠJH™]\›ˆš[™Ù™—Ü]X[]HŽÂ‚ˆYˆ
˜[YKÛÛZ[œÊ˜XØÛÝ[[™ÈŠH˜[YKÛÛZ[œÊš[›ÚXÙHŠJH™]\›ˆ˜XØÛÝ[[™ÈŽÂˆYˆ
˜[YKÛÛZ[œÊ[YH[žHŠJH™]\›ˆ[YHŽÂˆYˆ
˜[YKÛÛZ[œÊ˜Ý\ÝÛY\ˆŠJH™]\›ˆ˜Ý\ÝÛY\ˆŽÂˆYˆ
˜[YKÛÛZ[œÊœ›Ú™XÝ™\ÜŠJH™]\›ˆœ›Ú™XÝŽÂˆYˆ
˜[YKÛÛZ[œÊœHŠJH™]\›ˆœHŽÂˆYˆ
˜[YKÛÛZ[œÊœÙ[XÝY[™Ú[™Y\ˆŠH˜[YKÛÛZ[œÊ™[™Ú[™Y\ˆŠJH™]\›ˆ™[™Ú[™Y\ˆŽÂˆYˆ
˜[YKÛÛZ[œÊX[HŠH˜[YKÛÛZ[œÊ›Ü™Ø[š^˜][ÛˆŠJH™]\›ˆX[HŽÂˆYˆ
˜[YKÛÛZ[œÊÛÜšÙ›ÝÈŠH˜[YKÛÛZ[œÊ˜\›Ý˜[ŠH˜[YKÛÛZ[œÊ˜]Y]ŠJH™]\›ˆ˜]Y]ŽÂˆYˆ
˜[YKÛÛZ[œÊœÞ\Ý[HŠJH™]\›ˆœÞ\Ý[HŽÂˆYˆ
˜[YKÛÛZ[œÊ˜\HŠJH™]\›ˆ˜\HŽÂˆYˆ
˜[YKÛÛZ[œÊ™^\›˜[ŠJH™]\›ˆ™^\›˜[ŽÂˆYˆ
˜[YKÛÛZ[œÊ˜]][XØ][ÛˆŠH˜[YKÛÛZ[œÊœÙXÝ\š]HŠJH™]\›ˆ˜]]ŽÂˆYˆ
˜[YKÛÛZ[œÊ˜ZHŠH˜[YKÛÛZ[œÊœÛÝÈŠJH™]\›ˆ˜ZHŽÂˆYˆ
˜[YKÛÛZ[œÊ››ÝYšXØ][ÛˆŠJH™]\›ˆ››ÝYšXØ][ÛˆŽÂˆYˆ
˜[YKÛÛZ[œÊX]ŠJH™]\›ˆX]ŽÂˆYˆ
˜[YKÛÛZ[œÊ›Xœ˜\žHŠJH™]\›ˆ›Xœ˜\žHŽÂˆYˆ
˜[YKÛÛZ[œÊ™^XÝ]]™HŠJH™]\›ˆ™^XÝ]]™HŽÂ‚ˆ™]\›ˆ˜XØÛÝ[[™ÈŽÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[ØY™Q]X˜\ÙT™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KœÛÛ‘[[Y[Üš]\šXJBžÂˆÝš[™ÈØ]YÛÜžHH›Ú™XÝ[ÙLÌØY™PØ]YÛÜžJ™\Ü\JNÂ‚ˆ˜\ˆ™XYX›T™\ÜH]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›R›Ú[™Y™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆYˆ
™XYX›T™\Ü\È›Ý[
BˆÂˆ™]\›ˆ™XYX›T™\ÜÂˆB‚ˆ˜\ˆX›HH]ØZ]›Ú™XÝ[ÙLÌš[™ØY™T™\ÜX›P\Þ[˜ÊÛÛ›™XÝ[Û‹Ø]YÛÜžJNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJX›K”ØÚ[XJHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJX›K“˜[YJJBˆÂˆ™]\›ˆ™]ÂˆÂˆ]X˜\ÙP˜XÚÙYHYKˆ™\Ü\KˆØ]YÛÜžKˆÛÝ\˜ÙUX›HHˆ‹ˆÛÛ[[œÈH™]Ö×HÈ“Y\ÜØYÙHˆKˆ›ÝÜÈH\œ˜^K‘[\OØš™XÝÖ×OŠ
Kˆ›ÝÐÛÝ[HˆY\ÜØYÙHH	“›È]X˜\ÙHX›H\ÈÝ\œ™[HÛÛ™šYÝ\™Y›Üˆ™\ÜØ]YÛÜžH	ÞØØ]YÛÜž_IËˆ‚ˆNÂˆB‚ˆ˜\ˆÛÛ[[œÈH]ØZ]›Ú™XÝ[ÙLÌÙ]ØY™PÛÛ[[œÐ\Þ[˜ÊÛÛ›™XÝ[Û‹X›K”ØÚ[XKX›K“˜[YJNÂ‚ˆYˆ
ÛÛ[[œËÛÝ[OH
BˆÂˆ™]\›ˆ™]ÂˆÂˆ]X˜\ÙP˜XÚÙYHYKˆ™\Ü\KˆØ]YÛÜžKˆÛÝ\˜ÙUX›HH	žÝX›K”ØÚ[X_KžÝX›K“˜[Y_H‹ˆÛÛ[[œÈH™]Ö×HÈ“Y\ÜØYÙHˆKˆ›ÝÜÈH\œ˜^K‘[\OØš™XÝÖ×OŠ
Kˆ›ÝÐÛÝ[HˆY\ÜØYÙHH	•HÙ[XÝY™\ÜÛÝ\˜ÙH	ÞÝX›K”ØÚ[X_KžÝX›K“˜[Y_IÈY›Ý^ÜÙH™\ÜX›HÛÛ[[œÈ›ÝYÚH]X˜\ÙHÛÛ›™XÝ[Û‹ˆ‚ˆNÂˆB‚ˆ˜\ˆÙ[XÝYÛÛ[[œÈH›Ú™XÝ[ÙLÌÙ[XÝØY™PÛÛ[[œÊØ]YÛÜžKÛÛ[[œÊNÂˆ˜\ˆÚ\™HH›Ú™XÝ[ÙLÌZ[ØY™UÚ\™PÛ]\ÙJÜš]\šXKÙ[XÝYÛÛ[[œÊNÂ‚ˆÝš[™ÈÜ™\žHHÙ[XÝYÛÛ[[œË[žJÈOˆÝš[™Ë‘\]X[ÊË“˜[YK˜Ü™X]YØ]‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÈˆÔ‘Tˆ–Hˆ
È›Ú™XÝ[ÙLÌ][ÝJ˜Ü™X]YØ]ŠH
ÈˆTÐÈ•SÈTÕ‚ˆˆˆŽÂ‚ˆÝš[™ÈÜ[Bˆ”ÑSPÕˆ
ÈÝš[™Ë’›Ú[Š‹‹Ù[XÝYÛÛ[[œË”Ù[XÝ
ÈOˆ›Ú™XÝ[ÙLÌ][ÝJË“˜[YJJJH
Âˆˆ”“ÓHˆ
È›Ú™XÝ[ÙLÌ][ÝJX›K”ØÚ[XJH
È‹ˆˆ
È›Ú™XÝ[ÙLÌ][ÝJX›K“˜[YJH
ÂˆÚ\™K”Ü[
ÂˆÜ™\žH
ÂˆˆSRULÈŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂ‚ˆ›Ü™XXÚ
˜\ˆ\˜[Y]\ˆ[ˆÚ\™K”\˜[Y]\œÊBˆÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\˜[Y]\‹’Ù^K\˜[Y]\‹•˜[YJNÂˆB‚ˆ˜\ˆ›ÝÜÈH™]È\ÝØš™XÝÖ×OŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
JBˆÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ˜\ˆ›ÝÈH™]ÈØš™XÝÖÜÙ[XÝYÛÛ[[œËÛÝ[NÂ‚ˆ›Üˆ
[HHÈHÙ[XÝYÛÛ[[œËÛÝ[ÈJÊÊBˆÂˆ›ÝÖÚWHH™XY\‹’\Ñ“[
JHÈ[ˆ›Ú™XÝ[ÙLÌØY™Q›Ü›X]˜[YJ™XY\‹‘Ù]˜[YJJJNÂˆB‚ˆ›ÝÜËY
›ÝÊNÂˆBˆB‚ˆÝš[™ÈY\ÜØYÙHH›ÝÜËÛÝ[OHˆÈ	“›È]X˜\ÙH›ÝÜÈX]ÚYHÙ[XÝYÜš]\šXHœ›ÛH	ÞÝX›K”ØÚ[X_KžÝX›K“˜[Y_IËˆ‚ˆˆ	‘]X˜\ÙKX˜XÚÙY™\ÜÙ[™\˜]Yœ›ÛH	ÞÝX›K”ØÚ[X_KžÝX›K“˜[Y_IËˆ›ÝÜÈÚÝÛˆ\™HXÝX[]X˜\ÙH›ÝÜËˆŽÂ‚ˆ™]\›ˆ™]ÂˆÂˆ]X˜\ÙP˜XÚÙYHYKˆ™XYX›HH˜[ÙKˆ™\Ü\KˆØ]YÛÜžKˆÛÝ\˜ÙUX›HH	žÝX›K”ØÚ[X_KžÝX›K“˜[Y_H‹ˆÛÛ[[œÈHÙ[XÝYÛÛ[[œË”Ù[XÝ
ÈOˆ›Ú™XÝ[ÙLÌ\Ü^JË“˜[YJJK•Ð\œ˜^J
KˆÛÛ[[’Ù^\ÈHÙ[XÝYÛÛ[[œË”Ù[XÝ
ÈOˆË“˜[YJK•Ð\œ˜^J
Kˆ›ÝÜËˆ›ÝÐÛÝ[H›ÝÜËÛÝ[ˆY\ÜØYÙBˆNÂŸB‚‚‹ËÈÌWÔ‘PQP“WÔ‘TÔ•S‘×Ò“ÒS‘QÒST”×ÔÕT•‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝÏˆ›Ú™XÝ[ÙLÌZ[™XYX›R›Ú[™Y™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆYˆ
Ø]YÛÜžH\È˜XØÛÝ[[™ÈˆÜˆ[YHˆÜˆ™[™Ú[™Y\ˆŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›U[YT™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\Èœ›Ú™XÝˆÜˆœHŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›T›Ú™XÝ™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È˜Ý\ÝÛY\ˆŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›PÝ\ÝÛY\”™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\ÈX[HŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›UX[T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\ÈWÜØ[\ÈŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[TØ[\Ô™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\Èœ›Ú™XÝÜÝ]\ÈŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[›Ú™XÝÝ]\Ðš[Y™[XZ[š[™Ô™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È˜Ù\YžHŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[Ù\YžT™XYQ^[œÙT™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È™[™Ú[™Y\—ÛÝ™\—Ý[™\ˆŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[[™Ú[™Y\”›Ú™XÝÝ™\•[™\”™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È][^˜][Û—ÛÝ™\—Ý[™\ˆŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[][^˜][Û“Ý™\•[™\”™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\Èœ×Ý\ÙYŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[Õ\ÙY™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È˜š[X›WÛ›Û˜š[X›HŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[š[X›S›Ûš[X›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\Èš[›ÚXÙWÜ™XY[™\ÜÈŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[[›ÚXÙT™XY[™\ÜÔ™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È˜\›Ý˜[Ø›Ý[™XÚÈŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[\›Ý˜[›Ý[™XÚÔ™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È›Z\ÜÚ[™×Ý[YHŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[Z\ÜÚ[™Õ[YT™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\Èœ›Ú™XÝÛX\™Ú[ˆŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[›Ú™XÝX\™Ú[”™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\Èœ˜]WÙ^Ù\[ÛˆŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[˜]Q^Ù\[Û”™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È˜Ý\ÝÛY\—Ü›Ùš]Xš[]HŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[Ý\ÝÛY\”›Ùš]Xš[]T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\È˜ÛÜÙ[Ý]Ü™XY[™\ÜÈŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[ÛÜÙ[Ý]™XY[™\ÜÔ™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆYˆ
Ø]YÛÜžH\Èš[™Ù™—Ü]X[]HŠBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[[™Ù™”]X[]T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂˆB‚ˆ™]\›ˆ[ÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[™XYX›U[YT™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈHØ]YÛÜžHOH˜XØÛÝ[[™È‚ˆÈ™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
™[™ØYÙ[Y[ÛX[˜YÙ\ˆ‹‘[™ØYÙ[Y[X[˜YÙ\ˆŠKˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
™[™ØYÙ[Y[Ü›Ú™XÝ‹‘[™ØYÙ[Y[È›Ú™XÝŠKˆ
˜ÛÛ˜XÝÝ\H‹ÛÛ˜XÝ\HŠKˆ
œ×Ü][ÝH‹”ÈÈ][ÝHŠKˆ
š[›ÚXÚ[™×Ú[œÝXÝ[ÛœÈ‹’[›ÚXÚ[™È[œÝXÝ[ÛœÈŠKˆ
˜ÜÚ[›ÚXÙWÛ[X™\ˆ‹Ô[›ÚXÙH[X™\ˆŠKˆ
š[›ÚXÙWÙ]H‹’[›ÚXÙH]HŠKˆ
˜Ø]YÛÜžH‹Ø]YÛÜžHŠKˆ
š][WÙ\ØÜš\[Ûˆ‹’][H\ØÜš\[ÛˆŠKˆ
œ]X[]WÚÝ\œ×Ù[\™Y‹”]X[]HÈÝ\œÈ[\™YŠKˆ
œ˜]H‹”˜]HŠKˆ
˜[[Ý[‹[[Ý[ŠKˆ
ÛÜš×ØÛÙH‹•ÛÜšÈÛÙHŠKˆ
ÛÜš×ÛØØ][Ûˆ‹•ÛÜšÈØØ][ÛˆŠKˆ
˜š[X›WÜÝ]\È‹š[X›HÝ]\ÈŠKˆ
š[›ÚXÙWÜÝ]\È‹’[›ÚXÙHÝ]\ÈŠKˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠBˆBˆˆ™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
™[™Ú[™Y\—Ù[XZ[‹‘[™Ú[™Y\ˆ[XZ[ŠKˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
œ›Ú™XÝØÛÙH‹”›Ú™XÝÛÙHŠKˆ
œ›Ú™XÝÛ˜[YH‹”›Ú™XÝ˜[YHŠKˆ
œ›Ú™XÝÛX[˜YÙ\ˆ‹”›Ú™XÝX[˜YÙ\ˆŠKˆ
X[H‹•X[HŠKˆ
ÙYZ×ÜÝ\Ù]H‹•ÙYZÈÝ\]HŠKˆ
[Y\ÚY]ÜÝ]\È‹•[Y\ÚY]Ý]\ÈŠKˆ
ÛÜš×Ù]H‹•ÛÜšÈ]HŠKˆ
šÝ\œÈ‹’Ý\œÈŠKˆ
˜š[X›WÜÝ]\È‹š[X›HÝ]\ÈŠKˆ
˜Ø]YÛÜžH‹Ø]YÛÜžHŠKˆ
ÛÜš×Ú][H‹•ÛÜšÈ][HŠKˆ
ÛÜš×ØÛÙH‹•ÛÜšÈÛÙHŠKˆ
ÛÜš×ÛØØ][Û—ÙÜ›Ý\‹•ÛÜšÈØØ][ÛˆÜ›Ý\ŠKˆ
ÛÜš×ÛØØ][Ûˆ‹•ÛÜšÈØØ][ÛˆŠKˆ
[YWÙ[žWÜÝ]\È‹•[YH[žHÝ]\ÈŠKˆ
[YWÝ\H‹•[YH\HŠKˆ
™\ØÜš\[Ûˆ‹‘\ØÜš\[ÛˆŠBˆNÂ‚ˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂ‚ˆ›Ú™XÝ[ÙLÌY]T˜[™ÙJÜš]\šXKÚ\™K\˜[Y]\œËKÛÜš×Ù]HŠNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË˜Ý\ÝÛY\ˆ‹™]Ö×HÈ˜Ë˜ÛY[Û˜[YH‹˜Ë˜ÛY[ØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœ›Ú™XÝ‹™]Ö×HÈœœ›Ú™XÝÛ˜[YH‹œœ›Ú™XÝØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœH‹™]Ö×HÈœK™\Ü^WÛ˜[YH‹œK™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË™[™Ú[™Y\ˆ‹™]Ö×HÈK™\Ü^WÛ˜[YH‹K™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœÙ[XÝY[™Ú[™Y\œÈ‹™]Ö×HÈK™\Ü^WÛ˜[YH‹K™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËX[H‹™]Ö×HÈKX[WÛ˜[YH‹KX[WÛ˜[YH‹K™\\Y[‹K™\\Y[Û˜[YHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË›Ü™Ø[š^˜][Ûˆ‹™]Ö×HÈK™\\Y[‹K™\\Y[Û˜[YHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË[YQ[žTÝ]\È‹™]Ö×HÈKœÝ]\È‹ËœÝ]\ÈˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË˜\›Ý˜[Ý]\È‹™]Ö×HÈKœÝ]\È‹ËœÝ]\ÈˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËš[›ÚXÙTÝ]\È‹™]Ö×HÈ˜ÚKš[›ÚXÙWÜÝ]\ÈˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËÛÜšÐÛÙH‹™]Ö×HÈœ\Ú×ØÛÙH‹›œË˜Ø]YÛÜžWØÛÙH‹œÛÜš×Ý\Ú×ØØ]YÛÜžH‹œ˜š[[™×ØÛ\ÜÚYšXØ][ÛˆˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËÛÜšÓØØ][Ûˆ‹™]Ö×HÈÛË™Ü›Ý\Û˜[YH‹Û›ØØ][Û—Û˜[YH‹Û˜Ú]H‹ÛœÝ]WÜ™YÚ[ÛˆˆJNÂ‚ˆ›Ú™XÝ[ÙLÌYš[X›Qš[\ŠÜš]\šXKÚ\™KÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHŠNÂˆ›Ú™XÝ[ÙLÌYÛÛ˜XÝ\Qš[\ŠÜš]\šXKÚ\™JNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™ØYÙ[Y[ÛX[˜YÙ\‹ˆÓÐSTÐÑJË˜ÛY[Û˜[YK	ÉÊHTÈÝ\ÝÛY\‹ˆÓÐSTÐÑJ•SQŠÓÓÐUÕÔÊ	ÈH	Ëœ›Ú™XÝØÛÙKœ›Ú™XÝÛ˜[YJK	ÈH	ÊK	Ó›Û‹\›Ú™XÝ[YIÊHTÈ[™ØYÙ[Y[Ü›Ú™XÝˆÐTÑBˆÒSˆKœ›Ú™XÝÚYTÈ•SSˆ	Ó›Û‹T›Ú™XÝ[YIÂˆÒSˆÓÐSTÐÑJ˜š[X›KK˜š[X›KSÑJHSˆ	Ðš[X›H›Ú™XÝ	ÂˆSÑH	Ó›Û‹Pš[X›H›Ú™XÝ	ÂˆS‘TÈÛÛ˜XÝÝ\Kˆ	ÉÈTÈ×Ü][ÝKˆ	ÉÈTÈ[›ÚXÚ[™×Ú[œÝXÝ[ÛœËˆÓÐSTÐÑJÚKš[›ÚXÙWÛ[X™\‹	ÉÊHTÈÜÚ[›ÚXÙWÛ[X™\‹ˆÓÐSTÐÑJÚK™Ù[™\˜]YØ]Ž™]KKÛÜš×Ù]JHTÈ[›ÚXÙWÙ]KˆÓÐSTÐÑJœË˜Ø]YÛÜžWÛ˜[YKÛÜš×Ý\Ú×ØØ]YÛÜžK˜š[[™×ØÛ\ÜÚYšXØ][Û‹	Ô›Ú™XÝÛÜšÉÊHTÈØ]YÛÜžKˆÓÐSTÐÑJ•SQŠK™\ØÜš\[Û‹	ÉÊK\Ú×Û˜[YKœË˜Ø]YÛÜžWÛ˜[YK	ÉÊHTÈ][WÙ\ØÜš\[Û‹ˆKšÝ\œÈTÈ]X[]WÚÝ\œ×Ù[\™Yˆ•SŽ›[Y\šXÈTÈ˜]Kˆ•SŽ›[Y\šXÈTÈ[[Ý[ˆÓÐSTÐÑJ\Ú×ØÛÙKœË˜Ø]YÛÜžWØÛÙK	ÉÊHTÈÛÜš×ØÛÙKˆÓÐSTÐÑJÛ›ØØ][Û—Û˜[YKÛË™Ü›Ý\Û˜[YK	ÉÊHTÈÛÜš×ÛØØ][Û‹ˆÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHSˆ	Ðš[X›IÈSÑH	Ó›Û‹Xš[X›IÈS‘TÈš[X›WÜÝ]\ËˆÓÐSTÐÑJÚKš[›ÚXÙWÜÝ]\Ë	Ó›Ý[›ÚXÙY	ÊHTÈ[›ÚXÙWÜÝ]\ËˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆÓÐSTÐÑJK™[XZ[	ÉÊHTÈ[™Ú[™Y\—Ù[XZ[ˆÓÐSTÐÑJœ›Ú™XÝØÛÙK	ÉÊHTÈ›Ú™XÝØÛÙKˆÓÐSTÐÑJœ›Ú™XÝÛ˜[YK	ÉÊHTÈ›Ú™XÝÛ˜[YKˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ›Ú™XÝÛX[˜YÙ\‹ˆÓÐSTÐÑJKX[WÛ˜[YK•SQŠKX[WÛ˜[YK	ÉÊK	ÉÊHTÈX[KˆËÙYZ×ÜÝ\Ù]KˆÓÐSTÐÑJËœÝ]\Ë	ÉÊHTÈ[Y\ÚY]ÜÝ]\ËˆKÛÜš×Ù]KˆKšÝ\œËˆÓÐSTÐÑJœË˜Ø]YÛÜžWÛ˜[YK\Ú×Û˜[YK	Ô›Ú™XÝÛÜšÉÊHTÈÛÜš×Ú][KˆÓÐSTÐÑJÛË™Ü›Ý\Û˜[YK	ÉÊHTÈÛÜš×ÛØØ][Û—ÙÜ›Ý\ˆÓÐSTÐÑJKœÝ]\Ë	ÉÊHTÈ[YWÙ[žWÜÝ]\ËˆÓÐSTÐÑJK[YWÝ\K	ÉÊHTÈ[YWÝ\KˆK™\ØÜš\[Û‚‘”“ÓH[YWÙ[šY\ÈB“Q•“ÒSˆ[Y\ÚY]ÈÂˆÓˆË[Y\ÚY]ÚYHK[Y\ÚY]ÚY“Q•“ÒSˆ\Ý\Ù\œÈBˆÓˆK\Ù\—ÚYHK\Ù\—ÚY“Q•“ÒSˆ›Ú™XÝÈˆÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚY“Q•“ÒSˆÛY[ÈÂˆÓˆË˜ÛY[ÚYH˜ÛY[ÚY“Q•“ÒSˆ\Ý\Ù\œÈBˆÓˆK\Ù\—ÚYHœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚY“Q•“ÒSˆ›Ú™XÝÝ\ÚÜÈˆÓˆ\Ú×ÚYHK\Ú×ÚY“Q•“ÒSˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÈœÂˆÓˆœË››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYHK››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚY“Q•“ÒSˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÈÛÂˆÓˆÛËÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYHKÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚY“Q•“ÒSˆÛÜš×ÛØØ][ÛœÈÛˆÓˆÛÛÜš×ÛØØ][Û—ÚYHKÛÜš×ÛØØ][Û—ÚY“Q•“ÒSˆX[WÛY[X™\œÚ\ÈY[BˆÓˆY[K\Ù\—ÚYHK\Ù\—ÚYˆS‘
Y[K™Y™™XÝ]™WÙ[™Ù]HTÈ•SÔˆY[K™Y™™XÝ]™WÙ[™Ù]HHKÛÜš×Ù]JB“Q•“ÒSˆX[\ÈBˆÓˆKX[WÚYHY[KX[WÚY“Q•“ÒSˆÛY[Ú[›ÚXÙ\ÈÚBˆÓˆÚKœ›Ú™XÝÚYHœ›Ú™XÝÚYˆS‘KÛÜš×Ù]H‘UÑQSˆÚK˜š[[™×Ü\š[ÙÜÝ\S‘ÚK˜š[[™×Ü\š[ÙÙ[™ˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™JH
È‚“Ô‘Tˆ–HKÛÜš×Ù]HTÐÈ•SÈTÕÝ\ÝÛY\‹[™ØYÙ[Y[Ü›Ú™XÝ[™Ú[™Y\‚“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË[YWÙ[šY\È
È™XYX›H›Ú[œÈ‹ÛÛ[[œËÜ[\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[™XYX›T›Ú™XÝ™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
œ›Ú™XÝØÛÙH‹”›Ú™XÝÛÙHŠKˆ
œ›Ú™XÝÛ˜[YH‹”›Ú™XÝ˜[YHŠKˆ
œ›Ú™XÝÛX[˜YÙ\ˆ‹”›Ú™XÝX[˜YÙ\ˆŠKˆ
œ›Ú™XÝÜÝ]\È‹”›Ú™XÝÝ]\ÈŠKˆ
˜š[X›WÜÝ]\È‹š[X›HÝ]\ÈŠKˆ
œÝ\Ù]H‹”Ý\]HŠKˆ
™[™Ù]H‹‘[™]HŠKˆ
œ[›™YÙ[™Ú[™Y\š[™×ØÛÜÝ‹”[›™Y[™Ú[™Y\š[™ÈÛÜÝŠKˆ
œ[›™YÜWØÛÜÝ‹”[›™YHÛÜÝŠKˆ
œ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ‹”[›™YÝ[›Ú™XÝÛÜÝŠKˆ
[YWÙ[žWØÛÝ[‹•[YH[žHÛÝ[ŠKˆ
Ý[ÚÝ\œÈ‹•Ý[Ý\œÈŠKˆ
˜š[X›WÚÝ\œÈ‹š[X›HÝ\œÈŠKˆ
››Û—Øš[X›WÚÝ\œÈ‹“›Û‹Pš[X›HÝ\œÈŠBˆNÂ‚ˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂ‚ˆ›Ú™XÝ[ÙLÌY]T˜[™ÙJÜš]\šXKÚ\™K\˜[Y]\œËÓÐSTÐÑJœÝ\Ù]K˜Ü™X]YØ]Ž™]JHŠNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË˜Ý\ÝÛY\ˆ‹™]Ö×HÈ˜Ë˜ÛY[Û˜[YH‹˜Ë˜ÛY[ØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœ›Ú™XÝ‹™]Ö×HÈœœ›Ú™XÝÛ˜[YH‹œœ›Ú™XÝØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœH‹™]Ö×HÈœK™\Ü^WÛ˜[YH‹œK™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌYš[X›Qš[\ŠÜš]\šXKÚ\™KÓÐSTÐÑJ˜š[X›KSÑJHŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJË˜ÛY[Û˜[YK	ÉÊHTÈÝ\ÝÛY\‹ˆÓÐSTÐÑJœ›Ú™XÝØÛÙK	ÉÊHTÈ›Ú™XÝØÛÙKˆÓÐSTÐÑJœ›Ú™XÝÛ˜[YK	ÉÊHTÈ›Ú™XÝÛ˜[YKˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ›Ú™XÝÛX[˜YÙ\‹ˆÓÐSTÐÑJœÝ]\Ë	ÉÊHTÈ›Ú™XÝÜÝ]\ËˆÐTÑHÒSˆÓÐSTÐÑJ˜š[X›KSÑJHSˆ	Ðš[X›IÈSÑH	Ó›Û‹Xš[X›IÈS‘TÈš[X›WÜÝ]\ËˆœÝ\Ù]Kˆ™[™Ù]Kˆœ[›™YÙ[™Ú[™Y\š[™×ØÛÜÝˆœ[›™YÜWØÛÜÝˆœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝˆÓÕS•
TÕSÕK[YWÙ[žWÚY
NŽš[YÙ\ˆTÈ[YWÙ[žWØÛÝ[ˆÓÐSTÐÑJÕSJKšÝ\œÊK
HTÈÝ[ÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HTÈš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆSÑHKšÝ\œÈS‘
K
HTÈ›Û—Øš[X›WÚÝ\œÂ‘”“ÓH›Ú™XÝÈ“Q•“ÒSˆÛY[ÈÂˆÓˆË˜ÛY[ÚYH˜ÛY[ÚY“Q•“ÒSˆ\Ý\Ù\œÈBˆÓˆK\Ù\—ÚYHœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚY“Q•“ÒSˆ[YWÙ[šY\ÈBˆÓˆKœ›Ú™XÝÚYHœ›Ú™XÝÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™JH
È‚‘Ô“ÕT–HË˜ÛY[Û˜[YKœ›Ú™XÝØÛÙKœ›Ú™XÝÛ˜[YKK™\Ü^WÛ˜[YKœÝ]\Ë˜š[X›KœÝ\Ù]K™[™Ù]Kœ[›™YÙ[™Ú[™Y\š[™×ØÛÜÝœ[›™YÜWØÛÜÝœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ“Ô‘Tˆ–HË˜ÛY[Û˜[YKœ›Ú™XÝÛ˜[YB“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XËœ›Ú™XÝÈ
È™XYX›H›Ú[œÈ‹ÛÛ[[œËÜ[\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[™XYX›PÝ\ÝÛY\”™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
˜Ý\ÝÛY\—ØÛÙH‹Ý\ÝÛY\ˆÛÙHŠKˆ
˜XÝ]™WÜ›Ú™XÝÈ‹XÝ]™H›Ú™XÝÈŠKˆ
œ›Ú™XÝØÛÝ[‹”›Ú™XÝÛÝ[ŠKˆ
[YWÙ[žWØÛÝ[‹•[YH[žHÛÝ[ŠKˆ
Ý[ÚÝ\œÈ‹•Ý[Ý\œÈŠKˆ
˜š[X›WÚÝ\œÈ‹š[X›HÝ\œÈŠKˆ
››Û—Øš[X›WÚÝ\œÈ‹“›Û‹Pš[X›HÝ\œÈŠKˆ
š[›ÚXÙWÝÝ[‹’[›ÚXÙHÝ[ŠBˆNÂ‚ˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂ‚ˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË˜Ý\ÝÛY\ˆ‹™]Ö×HÈ˜Ë˜ÛY[Û˜[YH‹˜Ë˜ÛY[ØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœ›Ú™XÝ‹™]Ö×HÈœœ›Ú™XÝÛ˜[YH‹œœ›Ú™XÝØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌYš[X›Qš[\ŠÜš]\šXKÚ\™KÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJË˜ÛY[Û˜[YK	ÉÊHTÈÝ\ÝÛY\‹ˆÓÐSTÐÑJË˜ÛY[ØÛÙK	ÉÊHTÈÝ\ÝÛY\—ØÛÙKˆÓÕS•
TÕSÕœ›Ú™XÝÚY
H’STˆ
ÒT‘HœÝ]\ÈH	ØXÝ]™IÊNŽš[YÙ\ˆTÈXÝ]™WÜ›Ú™XÝËˆÓÕS•
TÕSÕœ›Ú™XÝÚY
NŽš[YÙ\ˆTÈ›Ú™XÝØÛÝ[ˆÓÕS•
TÕSÕK[YWÙ[žWÚY
NŽš[YÙ\ˆTÈ[YWÙ[žWØÛÝ[ˆÓÐSTÐÑJÕSJKšÝ\œÊK
HTÈÝ[ÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HTÈš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆSÑHKšÝ\œÈS‘
K
HTÈ›Û—Øš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJTÕSÕÚKš[›ÚXÙWÝÝ[
K
HTÈ[›ÚXÙWÝÝ[‘”“ÓHÛY[ÈÂ“Q•“ÒSˆ›Ú™XÝÈˆÓˆ˜ÛY[ÚYHË˜ÛY[ÚY“Q•“ÒSˆ[YWÙ[šY\ÈBˆÓˆKœ›Ú™XÝÚYHœ›Ú™XÝÚY“Q•“ÒSˆÛY[Ú[›ÚXÙ\ÈÚBˆÓˆÚKœ›Ú™XÝÚYHœ›Ú™XÝÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™JH
È‚‘Ô“ÕT–HË˜ÛY[Û˜[YKË˜ÛY[ØÛÙB“Ô‘Tˆ–HË˜ÛY[Û˜[YB“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË˜ÛY[È
È™XYX›H›Ú[œÈ‹ÛÛ[[œËÜ[\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[™XYX›UX[T™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
X[H‹•X[HŠKˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
™[™Ú[™Y\—Ù[XZ[‹‘[™Ú[™Y\ˆ[XZ[ŠKˆ
š›Ø—Ý]H‹’›Øˆ]HŠKˆ
™\\Y[‹‘\\Y[ŠKˆ
Ý[ÚÝ\œÈ‹•Ý[Ý\œÈŠKˆ
˜š[X›WÚÝ\œÈ‹š[X›HÝ\œÈŠKˆ
››Û—Øš[X›WÚÝ\œÈ‹“›Û‹Pš[X›HÝ\œÈŠKˆ
[YWÙ[žWØÛÝ[‹•[YH[žHÛÝ[ŠBˆNÂ‚ˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂ‚ˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËX[H‹™]Ö×HÈKX[WÛ˜[YH‹KX[WÛ˜[YHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË™[™Ú[™Y\ˆ‹™]Ö×HÈK™\Ü^WÛ˜[YH‹K™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË›Ü™Ø[š^˜][Ûˆ‹™]Ö×HÈK™\\Y[‹K™\\Y[Û˜[YHˆJNÂˆ›Ú™XÝ[ÙLÌYš[X›Qš[\ŠÜš]\šXKÚ\™KÓÐSTÐÑJK˜š[X›KSÑJHŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJKX[WÛ˜[YK•SQŠKX[WÛ˜[YK	ÉÊK	ÉÊHTÈX[KˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆÓÐSTÐÑJK™[XZ[	ÉÊHTÈ[™Ú[™Y\—Ù[XZ[ˆÓÐSTÐÑJKš›Ø—Ý]K	ÉÊHTÈ›Ø—Ý]KˆÓÐSTÐÑJK™\\Y[K™\\Y[Û˜[YK	ÉÊHTÈ\\Y[ˆÓÐSTÐÑJÕSJKšÝ\œÊK
HTÈÝ[ÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HTÈš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›KSÑJHSˆSÑHKšÝ\œÈS‘
K
HTÈ›Û—Øš[X›WÚÝ\œËˆÓÕS•
TÕSÕK[YWÙ[žWÚY
NŽš[YÙ\ˆTÈ[YWÙ[žWØÛÝ[‘”“ÓH\Ý\Ù\œÈB“Q•“ÒSˆX[WÛY[X™\œÚ\ÈY[BˆÓˆY[K\Ù\—ÚYHK\Ù\—ÚY“Q•“ÒSˆX[\ÈBˆÓˆKX[WÚYHY[KX[WÚY“Q•“ÒSˆ[YWÙ[šY\ÈBˆÓˆK\Ù\—ÚYHK\Ù\—ÚY•ÒT‘HKš\×ØXÝ]™HH•QBˆˆ
È
Ú\™KÛÝ[OHÈˆˆˆˆS‘ˆ
ÈÝš[™Ë’›Ú[ŠˆS‘‹Ú\™JJH
È‚‘Ô“ÕT–HKX[WÛ˜[YKKX[WÛ˜[YKK™\Ü^WÛ˜[YKK™[XZ[Kš›Ø—Ý]KK™\\Y[K™\\Y[Û˜[YB“Ô‘Tˆ–HX[K[™Ú[™Y\‚“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XËX[\È
È™XYX›H›Ú[œÈ‹ÛÛ[[œËÜ[\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆÝš[™È™\Ü\KˆÝš[™ÈØ]YÛÜžKˆÝš[™ÈÛÝ\˜ÙUX›Kˆ\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
OˆÛÛ[[œËˆÝš[™ÈÜ[ˆXÝ[Û˜\žOÝš[™ËØš™XÝˆ\˜[Y]\œÊBžÂˆ˜\ˆ›ÝÜÈH™]È\ÝØš™XÝÖ×OŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂ‚ˆ›Ü™XXÚ
˜\ˆ\˜[Y]\ˆ[ˆ\˜[Y]\œÊBˆÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\˜[Y]\‹’Ù^K\˜[Y]\‹•˜[YJNÂˆB‚ˆ]ØZ]\Ú[™È
˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
JBˆÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ˜\ˆ›ÝÈH™]ÈØš™XÝÖØÛÛ[[œËÛÝ[NÂ‚ˆ›Üˆ
[HHÈHÛÛ[[œËÛÝ[ÈJÊÊBˆÂˆ›ÝÖÚWHH™XY\‹’\Ñ“[
JHÈ[ˆ›Ú™XÝ[ÙLÌØY™Q›Ü›X]˜[YJ™XY\‹‘Ù]˜[YJJJNÂˆB‚ˆ›ÝÜËY
›ÝÊNÂˆBˆB‚ˆÝš[™ÈY\ÜØYÙHH›ÝÜËÛÝ[OHˆÈ	“›È]X˜\ÙH›ÝÜÈX]ÚYHÙ[XÝYÜš]\šXHœ›ÛH	ÞÜÛÝ\˜ÙUX›_IËˆ‚ˆˆ	”™XYX›H]X˜\ÙKX˜XÚÙY™\ÜÙ[™\˜]Yœ›ÛH	ÞÜÛÝ\˜ÙUX›_IËˆ˜[Y\È[™X™[È\™HÚÝÛˆ[œÝXYÙˆ[\›˜[QÈÚ\™H›Ú[œÈ\™H]˜Z[X›KˆŽÂ‚ˆ™]\›ˆ™]ÂˆÂˆ]X˜\ÙP˜XÚÙYHYKˆ™XYX›HHYKˆ™\Ü\KˆØ]YÛÜžKˆÛÝ\˜ÙUX›KˆÛÛ[[œÈHÛÛ[[œË”Ù[XÝ
ÈOˆË“X™[
K•Ð\œ˜^J
KˆÛÛ[[’Ù^\ÈHÛÛ[[œË”Ù[XÝ
ÈOˆË’Ù^JK•Ð\œ˜^J
Kˆ›ÝÜËˆ›ÝÐÛÝ[H›ÝÜËÛÝ[ˆY\ÜØYÙBˆNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌØY™XYX›Qš[\“Ü[ÛœÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[ÛŠBžÂˆ˜\ˆÝ\ÝÛY\œÈH]ØZ]›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕÛY[Û˜[YB‘”“ÓHÛY[Â•ÒT‘H\×ØXÝ]™HH•QBˆS‘•SQŠ’SJÛY[Û˜[YJK	ÉÊHTÈ“Õ•S“Ô‘Tˆ–HÛY[Û˜[YNÈ‹[Ý\ÝÛY\œÈŠNÂ‚ˆ˜\ˆ›Ú™XÝÈH]ØZ]›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕÓÓÐUÕÔÊ	ÈH	Ë•SQŠ›Ú™XÝØÛÙK	ÉÊK•SQŠ›Ú™XÝÛ˜[YK	ÉÊJHTÈX™[‘”“ÓH›Ú™XÝÂ•ÒT‘H•SQŠ’SJ›Ú™XÝÛ˜[YJK	ÉÊHTÈ“Õ•S“Ô‘Tˆ–HX™[È‹[›Ú™XÝÈŠNÂ‚ˆ˜\ˆ\ÈH]ØZ]›Ú™XÝ[ÙLÌØY\œÛÛ“Ü[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕK™\Ü^WÛ˜[YKK™[XZ[‘”“ÓH›Ú™XÝÈ’“ÒSˆ\Ý\Ù\œÈBˆÓˆK\Ù\—ÚYHœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚY•ÒT‘HKš\×ØXÝ]™HH•QBˆS‘•SQŠ’SJK™\Ü^WÛ˜[YJK	ÉÊHTÈ“Õ•SˆS‘•SQŠ’SJK™[XZ[
K	ÉÊHTÈ“Õ•S“Ô‘Tˆ–HK™\Ü^WÛ˜[YKK™[XZ[È‹[\ÈŠNÂ‚ˆ˜\ˆ[™Ú[™Y\œÈH]ØZ]›Ú™XÝ[ÙLÌØY\œÛÛ“Ü[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕK™\Ü^WÛ˜[YKK™[XZ[‘”“ÓH\Ý\Ù\œÈB’“ÒSˆ\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜BˆÓˆ\˜K\Ù\—ÚYHK\Ù\—ÚYˆS‘\˜Kš\×ØXÝ]™HH•QB’“ÒSˆ\Ü›Û\È‚ˆÓˆ‹˜\Ü›ÛWÚYH\˜K˜\Ü›ÛWÚYˆS‘‹š\×ØXÝ]™HH•QB•ÒT‘HKš\×ØXÝ]™HH•QBˆS‘K›ÙÚ[—Ù[˜X›YH•QBˆS‘‹œ›ÛWØÛÙHSˆ
	ÑS‘ÒS‘QT’S‘ÉË	ÑS‘ÒS‘QT’S‘×ÓPQ	Ë	ÓPSQÑT‰Ë	Ô“Ò‘PÕÕPSWÐÓÓÔ‘SUÔ‰Ë	ÔÕTT—ÐQRS’TÕUÔ‰ÊBˆS‘•SQŠ’SJK™\Ü^WÛ˜[YJK	ÉÊHTÈ“Õ•SˆS‘•SQŠ’SJK™[XZ[
K	ÉÊHTÈ“Õ•S“Ô‘Tˆ–HK™\Ü^WÛ˜[YKK™[XZ[È‹[[™Ú[™Y\œÈŠNÂ‚ˆ˜\ˆX[\ÈH]ØZ]›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕX[WÛ˜[YB‘”“ÓHX[\Â•ÒT‘H\×ØXÝ]™HH•QBˆS‘•SQŠ’SJX[WÛ˜[YJK	ÉÊHTÈ“Õ•S“Ô‘Tˆ–HX[WÛ˜[YNÈ‹[X[\ÈŠNÂ‚ˆ˜\ˆÜ™Ø[š^˜][ÛœÈH]ØZ]›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕÓÐSTÐÑJ•SQŠ\\Y[	ÉÊK•SQŠ\\Y[Û˜[YK	ÉÊJHTÈX™[‘”“ÓH\Ý\Ù\œÂ•ÒT‘H\×ØXÝ]™HH•QBˆS‘ÙÚ[—Ù[˜X›YH•QBˆS‘ÓÐSTÐÑJ•SQŠ\\Y[	ÉÊK•SQŠ\\Y[Û˜[YK	ÉÊJHTÈ“Õ•S“Ô‘Tˆ–HX™[È‹[Ü™Ø[š^˜][ÛœÈŠNÂ‚ˆ˜\ˆÛÛ˜XÝ\\ÈH]ØZ]›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕX™[‘”“ÓH
ˆÑSPÕ	Õ	“IÈTÈX™[ˆS’SÓˆSÑSPÕ	Õ[YH[™X]\šX[	ÂˆS’SÓˆSÑSPÕ	Ñš^YšY	ÂˆS’SÓˆSÑSPÕ	Ñš^Y™YIÂˆS’SÓˆSÑSPÕ	ÔÙ\šXÙH™\]Y\Ý	ÂˆS’SÓˆSÑSPÕ	ÓX[˜YÙYÙ\šXÙIÂˆS’SÓˆSÑSPÕ	Ô›Ú™XÝ	ÂˆS’SÓˆSÑSPÕ	Ó›Û‹T›Ú™XÝ[YIÂˆS’SÓˆSˆÑSPÕÐTÑBˆÒSˆKœ›Ú™XÝÚYTÈ•SSˆ	Ó›Û‹T›Ú™XÝ[YIÂˆÒSˆÓÐSTÐÑJ˜š[X›KK˜š[X›KSÑJHSˆ	Õ	“IÂˆSÑH	Ó›Û‹Pš[X›IÂˆS‘TÈX™[ˆ”“ÓH[YWÙ[šY\ÈBˆQ•“ÒSˆ›Ú™XÝÈˆÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚYŠHÛÝ\˜ÙB•ÒT‘H•SQŠ’SJX™[
K	ÉÊHTÈ“Õ•S“Ô‘Tˆ–HX™[È‹[ÛÛ˜XÝ\\ÈŠNÂ‚ˆ˜\ˆš[X›SÜ[ÛœÈH]ØZ]›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕÐTÑHÒSˆÓÐSTÐÑJš[X›KSÑJHSˆ	Ðš[X›IÈSÑH	Ó›Û‹Xš[X›IÈS‘TÈX™[‘”“ÓH[YWÙ[šY\Â“Ô‘Tˆ–HX™[È‹[š[X›HÝ]\Ù\ÈŠNÂ‚ˆ˜\ˆÛÜšÓØØ][ÛœÈH]ØZ]›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕÓÐSTÐÑJÛ›ØØ][Û—Û˜[YKÛË™Ü›Ý\Û˜[YJHTÈX™[‘”“ÓH[YWÙ[šY\ÈB“Q•“ÒSˆÛÜš×ÛØØ][ÛœÈÛˆÓˆÛÛÜš×ÛØØ][Û—ÚYHKÛÜš×ÛØØ][Û—ÚY“Q•“ÒSˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÈÛÂˆÓˆÛËÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYHKÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚY•ÒT‘HÓÐSTÐÑJÛ›ØØ][Û—Û˜[YKÛË™Ü›Ý\Û˜[YJHTÈ“Õ•S“Ô‘Tˆ–HX™[È‹[ÛÜšÈØØ][ÛœÈŠNÂ‚ˆ˜\ˆÛÜšÐÛÙ\ÈH]ØZ]›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊÛÛ›™XÝ[Û‹‚”ÑSPÕTÕSÕÓÐSTÐÑJ\Ú×ØÛÙKœË˜Ø]YÛÜžWØÛÙJHTÈX™[‘”“ÓH[YWÙ[šY\ÈB“Q•“ÒSˆ›Ú™XÝÝ\ÚÜÈˆÓˆ\Ú×ÚYHK\Ú×ÚY“Q•“ÒSˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÈœÂˆÓˆœË››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYHK››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚY•ÒT‘HÓÐSTÐÑJ\Ú×ØÛÙKœË˜Ø]YÛÜžWØÛÙJHTÈ“Õ•S“Ô‘Tˆ–HX™[È‹[ÛÜšÈÛÙ\ÈŠNÂ‚ˆ™]\›ˆ™]ÂˆÂˆ]X˜\ÙP˜XÚÙYHYKˆÝ\ÝÛY\œËˆ›Ú™XÝËˆ\Ëˆ[™Ú[™Y\œËˆÙ[XÝY[™Ú[™Y\œÈH[™Ú[™Y\œËˆX[\ËˆÜ™Ø[š^˜][ÛœËˆÛÛ˜XÝ\\Ëˆš[X›SÜ[ÛœËˆÛÜšÓØØ][ÛœËˆÛÜšÐÛÙ\ÂˆNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏÝš[™Ö×Oˆ›Ú™XÝ[ÙLÌØYÝš[™ÓÜ[ÛœÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™ÈÜ[Ýš[™È[X™[
BžÂˆ˜\ˆ˜[Y\ÈH™]È\ÝÝš[™ÏˆÈ[X™[NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂ‚ˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆYˆ
\™XY\‹’\Ñ“[

JBˆÂˆÝš[™È˜[YHH™XY\‹‘Ù]Ýš[™Ê
K•š[J
NÂˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜[YJH	‰ˆ]˜[Y\ËÛÛZ[œÊ˜[YKÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆ˜[Y\ËY
˜[YJNÂˆBˆBˆB‚ˆ™]\›ˆ˜[Y\Ë•Ð\œ˜^J
NÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝ×Oˆ›Ú™XÝ[ÙLÌØY\œÛÛ“Ü[ÛœÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™ÈÜ[Ýš[™È[X™[
BžÂˆ˜\ˆ˜[Y\ÈH™]È\ÝØš™XÝ‚ˆÂˆ™]ÈÈX™[H[X™[˜[YHH[X™[BˆNÂ‚ˆ˜\ˆÙY[ˆH™]È\ÚÙ]Ýš[™ÏŠÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJHÈ[X™[NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂ‚ˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÝš[™È\Ü^S˜[YHH™XY\‹’\Ñ“[

HÈˆˆˆ™XY\‹‘Ù]Ýš[™Ê
K•š[J
NÂˆÝš[™È[XZ[H™XY\‹’\Ñ“[
JHÈˆˆˆ™XY\‹‘Ù]Ýš[™ÊJK•š[J
NÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ\Ü^S˜[YJHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[XZ[
JBˆÂˆÛÛ[YNÂˆB‚ˆYˆ
ÙY[‹Y
[XZ[
JBˆÂˆ˜[Y\ËY
™]ÂˆÂˆX™[H	žÙ\Ü^S˜[Y_HÙ[XZ[Oˆ‹ˆ˜[YHH[XZ[ˆJNÂˆBˆB‚ˆ™]\›ˆ˜[Y\Ë•Ð\œ˜^J
NÂŸB‚‚œÝ]XÈ›ÚY›Ú™XÝ[ÙLÌY]T˜[™ÙJœÛÛ‘[[Y[Üš]\šXK\ÝÝš[™ÏˆÚ\™KXÝ[Û˜\žOÝš[™ËØš™XÝˆ\˜[Y]\œËÝš[™ÈÜ[^™\ÜÚ[ÛŠBžÂˆÝš[™ÈÝ\]HH›Ú™XÝ[ÙLÌØY™T™XYÝš[™ÊÜš]\šXKœÝ\]HŠNÂˆÝš[™È[™]HH›Ú™XÝ[ÙLÌØY™T™XYÝš[™ÊÜš]\šXK™[™]HŠNÂ‚ˆYˆ
]U[YK•žT\œÙJÝ\]KÝ]˜\ˆ\œÙYÝ\
JBˆÂˆÝš[™È\˜[Y]\“˜[YHHœˆ
È\˜[Y]\œËÛÝ[Âˆ\˜[Y]\œÖÜ\˜[Y]\“˜[YWHH\œÙYÝ\‘]NÂˆÚ\™KY
Ü[^™\ÜÚ[Ûˆ
ÈˆHˆ
È\˜[Y]\“˜[YJNÂˆB‚ˆYˆ
]U[YK•žT\œÙJ[™]KÝ]˜\ˆ\œÙY[™
JBˆÂˆÝš[™È\˜[Y]\“˜[YHHœˆ
È\˜[Y]\œËÛÝ[Âˆ\˜[Y]\œÖÜ\˜[Y]\“˜[YWHH\œÙY[™‘]NÂˆÚ\™KY
Ü[^™\ÜÚ[Ûˆ
ÈˆHˆ
È\˜[Y]\“˜[YJNÂˆBŸB‚œÝ]XÈ›ÚY›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠœÛÛ‘[[Y[Üš]\šXK\ÝÝš[™ÏˆÚ\™KXÝ[Û˜\žOÝš[™ËØš™XÝˆ\˜[Y]\œËÝš[™ÈšY[˜[YKÝš[™Ö×HÜ[^™\ÜÚ[ÛœÊBžÂˆÝš[™È˜[YHH›Ú™XÝ[ÙLÌØY™T™XYÝš[™ÊÜš]\šXKšY[˜[YJNÂ‚ˆYˆ
›Ú™XÝ[ÙLÌ\Ð[˜[YJ˜[YJJBˆÂˆ™]\›ŽÂˆB‚ˆ˜\ˆ\›\ÈH˜[YK”Ü]
	Ë	ËÝš[™ÔÜ]Ü[ÛœË”™[[Ý™Q[\Q[šY\ÈÝš[™ÔÜ]Ü[ÛœË•š[Q[šY\ÊBˆ•Ú\™J\›HOˆT›Ú™XÝ[ÙLÌ\Ð[˜[YJ\›JJBˆ•ZÙJLŠBˆ•Ó\Ý

NÂ‚ˆYˆ
\›\ËÛÝ[OH
BˆÂˆ™]\›ŽÂˆB‚ˆ˜\ˆ\›QÜ›Ý\ÈH™]È\ÝÝš[™ÏŠ
NÂ‚ˆ›Ü™XXÚ
˜\ˆ\›H[ˆ\›\ÊBˆÂˆÝš[™È\˜[Y]\“˜[YHHœˆ
È\˜[Y]\œËÛÝ[Âˆ\˜[Y]\œÖÜ\˜[Y]\“˜[YWHH‰Hˆ
È\›K•ÓÝÙ\’[˜\šX[

H
È‰HŽÂ‚ˆ\›QÜ›Ý\ËY
Šˆ
ÈÝš[™Ë’›Ú[ŠˆÔˆ‹Ü[^™\ÜÚ[ÛœË”Ù[XÝ
^™\ÜÚ[ÛˆOˆ“ÕÑTŠÓÐSTÐÑJˆ
È^™\ÜÚ[Ûˆ
ÈŽŽ^	ÉÊJHRÑHˆ
È\˜[Y]\“˜[YJJH
ÈŠHŠNÂˆB‚ˆÚ\™KY
Šˆ
ÈÝš[™Ë’›Ú[ŠˆÔˆ‹\›QÜ›Ý\ÊH
ÈŠHŠNÂŸB‚œÝ]XÈ›ÚY›Ú™XÝ[ÙLÌYš[X›Qš[\ŠœÛÛ‘[[Y[Üš]\šXK\ÝÝš[™ÏˆÚ\™KÝš[™ÈÜ[^™\ÜÚ[ÛŠBžÂˆÝš[™È˜[YHH›Ú™XÝ[ÙLÌØY™T™XYÝš[™ÊÜš]\šXK˜š[X›TÝ]\È‹˜š[X›H‹˜š[X›Qš[\ˆŠK•ÓÝÙ\’[˜\šX[

NÂ‚ˆYˆ
›Ú™XÝ[ÙLÌ\Ð[˜[YJ˜[YJJBˆÂˆ™]\›ŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ››ÛˆŠJBˆÂˆÚ\™KY
Ü[^™\ÜÚ[Ûˆ
ÈˆHSÑHŠNÂˆ™]\›ŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ˜š[X›HŠJBˆÂˆÚ\™KY
Ü[^™\ÜÚ[Ûˆ
ÈˆH•QHŠNÂˆBŸB‚œÝ]XÈ›ÚY›Ú™XÝ[ÙLÌYÛÛ˜XÝ\Qš[\ŠœÛÛ‘[[Y[Üš]\šXK\ÝÝš[™ÏˆÚ\™JBžÂˆÝš[™È˜[YHH›Ú™XÝ[ÙLÌØY™T™XYÝš[™ÊÜš]\šXK˜ÛÛ˜XÝ\HŠK•ÓÝÙ\’[˜\šX[

NÂ‚ˆYˆ
›Ú™XÝ[ÙLÌ\Ð[˜[YJ˜[YJJBˆÂˆ™]\›ŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ››Û‹\›Ú™XÝŠJBˆÂˆÚ\™KY
Kœ›Ú™XÝÚYTÈ•SŠNÂˆ™]\›ŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ››Û‹Xš[X›HŠJBˆÂˆÚ\™KY
Kœ›Ú™XÝÚYTÈ“Õ•SS‘ÓÐSTÐÑJ˜š[X›KK˜š[X›KSÑJHHSÑHŠNÂˆ™]\›ŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ˜š[X›HŠJBˆÂˆÚ\™KY
Kœ›Ú™XÝÚYTÈ“Õ•SS‘ÓÐSTÐÑJ˜š[X›KK˜š[X›KSÑJHH•QHŠNÂˆBŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLÌ\Ð[˜[YJÝš[™ÏÈ˜[YJBžÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜[YJJBˆÂˆ™]\›ˆYNÂˆB‚ˆÝš[™È›Ü›X[^™YH˜[YK•š[J
K•ÓÝÙ\’[˜\šX[

NÂˆ™]\›ˆ›Ü›X[^™YOH˜[‚ˆ›Ü›X[^™Y”Ý\ÕÚ]
˜[ŠBˆ›Ü›X[^™YÛÛZ[œÊ˜[ØÛÜYŠBˆ›Ü›X[^™YÛÛZ[œÊ››ÝÙ[XÝYŠBˆ›Ü›X[^™YÛÛZ[œÊ››Û™HÙ[XÝYŠNÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLÌÚ\™TÜ[
\ÝÝš[™ÏˆÚ\™JBžÂˆ™]\›ˆÚ\™KÛÝ[OHÈˆˆˆ•ÒT‘Hˆ
ÈÝš[™Ë’›Ú[ŠˆS‘‹Ú\™JNÂŸB‚‚‹ËÈÌ—ÒS•“ÒPÑWÑVÔ•Ð•TÒS‘TÔ×Ô‘TÔ•×ÔÕT•‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLÌÛÛ˜XÝ\Q^™\ÜÚ[ÛŠ
BžÂˆ™]\›ˆÐTÑBˆÒSˆKœ›Ú™XÝÚYTÈ•SSˆ	Ó›Û‹T›Ú™XÝ[YIÂˆÒSˆÕÑTŠÓÐSTÐÑJ˜š[[™×ØÛ\ÜÚYšXØ][Û‹ÛÜš×Ý\Ú×ØØ]YÛÜžK\Ú×ØÛÙK	ÉÊJHRÑH	ÉYš^Y	IÈSˆ	Ñš^YšY	ÂˆÒSˆÕÑTŠÓÐSTÐÑJ˜š[[™×ØÛ\ÜÚYšXØ][Û‹ÛÜš×Ý\Ú×ØØ]YÛÜžK\Ú×ØÛÙK	ÉÊJHRÑH	É\Ù\šXÙIIÈSˆ	ÔÙ\šXÙH™\]Y\Ý	ÂˆÒSˆÕÑTŠÓÐSTÐÑJ˜š[[™×ØÛ\ÜÚYšXØ][Û‹ÛÜš×Ý\Ú×ØØ]YÛÜžK\Ú×ØÛÙK	ÉÊJHRÑH	É[X[˜YÙY	IÈSˆ	ÓX[˜YÙYÙ\šXÙIÂˆÒSˆÓÐSTÐÑJ˜š[X›KK˜š[X›K˜š[X›KSÑJHSˆ	Õ	“IÂˆSÑH	Ó›Û‹Pš[X›IÂˆS‘ŽÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[TØ[\Ô™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
œ›Ú™XÝØÛÙH‹”›Ú™XÝÛÙHŠKˆ
œ›Ú™XÝÛ˜[YH‹”›Ú™XÝ˜[YHŠKˆ
œØ[\×ØÛÛ˜XÝÝ\H‹ÛÛ˜XÝ\HŠKˆ
œ›Ú™XÝÛX[˜YÙ\ˆ‹”›Ú™XÝX[˜YÙ\ˆŠKˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
ÛÜš×Ù]H‹•ÛÜšÈ]HŠKˆ
šÝ\œÈ‹’Ý\œÈŠKˆ
˜š[X›WÜÝ]\È‹š[X›HÝ]\ÈŠKˆ
ÛÜš×ØÛÙH‹•ÛÜšÈÛÙHŠKˆ
ÛÜš×Ú][H‹•ÛÜšÈ][HŠKˆ
™\ØÜš\[Ûˆ‹‘\ØÜš\[ÛˆŠBˆNÂ‚ˆ˜\ˆÚ\™HH›Ú™XÝ[ÙLÌÛÛ[[Û\Ú[™\ÜÕÚ\™JÜš]\šXKKÛÜš×Ù]HŠNÂˆÚ\™K”Ü[Y
ÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHH•QHŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJË˜ÛY[Û˜[YK	ÉÊHTÈÝ\ÝÛY\‹ˆÓÐSTÐÑJœ›Ú™XÝØÛÙK	ÉÊHTÈ›Ú™XÝØÛÙKˆÓÐSTÐÑJœ›Ú™XÝÛ˜[YK	ÉÊHTÈ›Ú™XÝÛ˜[YKˆˆ
È›Ú™XÝ[ÙLÌÛÛ˜XÝ\Q^™\ÜÚ[ÛŠ
H
ÈˆTÈØ[\×ØÛÛ˜XÝÝ\KˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ›Ú™XÝÛX[˜YÙ\‹ˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆKÛÜš×Ù]KˆKšÝ\œËˆÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHSˆ	Ðš[X›IÈSÑH	Ó›Û‹Xš[X›IÈS‘TÈš[X›WÜÝ]\ËˆÓÐSTÐÑJ\Ú×ØÛÙKœË˜Ø]YÛÜžWØÛÙK	ÉÊHTÈÛÜš×ØÛÙKˆÓÐSTÐÑJ\Ú×Û˜[YKœË˜Ø]YÛÜžWÛ˜[YK	ÉÊHTÈÛÜš×Ú][KˆÓÐSTÐÑJK™\ØÜš\[Û‹	ÉÊHTÈ\ØÜš\[Û‚‘”“ÓH[YWÙ[šY\ÈB“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHK\Ù\—ÚY“Q•“ÒSˆ›Ú™XÝÈÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚY“Q•“ÒSˆÛY[ÈÈÓˆË˜ÛY[ÚYH˜ÛY[ÚY“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚY“Q•“ÒSˆ›Ú™XÝÝ\ÚÜÈÓˆ\Ú×ÚYHK\Ú×ÚY“Q•“ÒSˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÈœÈÓˆœË››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYHK››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™K”Ü[
H
È‚“Ô‘Tˆ–HÝ\ÝÛY\‹›Ú™XÝÛ˜[YKKÛÜš×Ù]HTÐÂ“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË[YWÙ[šY\È
È	“HØ[\È›Ú[œÈ‹ÛÛ[[œËÜ[Ú\™K”\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[›Ú™XÝÝ]\Ðš[Y™[XZ[š[™Ô™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
œ›Ú™XÝØÛÙH‹”›Ú™XÝÛÙHŠKˆ
œ›Ú™XÝÛ˜[YH‹”›Ú™XÝ˜[YHŠKˆ
œ›Ú™XÝÛX[˜YÙ\ˆ‹”›Ú™XÝX[˜YÙ\ˆŠKˆ
œ›Ú™XÝÜÝ]\È‹”›Ú™XÝÝ]\ÈŠKˆ
œ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ‹”[›™YÝ[›Ú™XÝÛÜÝŠKˆ
˜š[YØÛÜÝ‹š[YÛÜÝŠKˆ
›X›Ü—Ø[[Ý[‹“X›Üˆ[[Ý[ŠKˆ
™^[œÙWØ[[Ý[‹‘^[œÙH[[Ý[ŠKˆ
œ™[XZ[š[™×Ø˜[[˜ÙH‹”™[XZ[š[™È˜[[˜ÙHŠKˆ
Ý[ÚÝ\œÈ‹•Ý[Ý\œÈŠKˆ
˜š[X›WÚÝ\œÈ‹š[X›HÝ\œÈŠKˆ
››Û—Øš[X›WÚÝ\œÈ‹“›Û‹Pš[X›HÝ\œÈŠBˆNÂ‚ˆ˜\ˆÚ\™HH›Ú™XÝ[ÙLÌÛÛ[[Û”›Ú™XÝÚ\™JÜš]\šXJNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJË˜ÛY[Û˜[YK	ÉÊHTÈÝ\ÝÛY\‹ˆÓÐSTÐÑJœ›Ú™XÝØÛÙK	ÉÊHTÈ›Ú™XÝØÛÙKˆÓÐSTÐÑJœ›Ú™XÝÛ˜[YK	ÉÊHTÈ›Ú™XÝÛ˜[YKˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ›Ú™XÝÛX[˜YÙ\‹ˆÓÐSTÐÑJœÝ]\Ë	ÉÊHTÈ›Ú™XÝÜÝ]\ËˆÓÐSTÐÑJœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ
HTÈ[›™YÝÝ[Ü›Ú™XÝØÛÜÝˆÓÐSTÐÑJÕSJTÕSÕÚKš[›ÚXÙWÝÝ[
K
HTÈš[YØÛÜÝˆÓÐSTÐÑJÕSJTÕSÕÚK›X›Ü—Ø[[Ý[
K
HTÈX›Ü—Ø[[Ý[ˆÓÐSTÐÑJÕSJTÕSÕÚK™^[œÙWØ[[Ý[
K
HTÈ^[œÙWØ[[Ý[ˆÓÐSTÐÑJœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ
HHÓÐSTÐÑJÕSJTÕSÕÚKš[›ÚXÙWÝÝ[
K
HTÈ™[XZ[š[™×Ø˜[[˜ÙKˆÓÐSTÐÑJÕSJKšÝ\œÊK
HTÈÝ[ÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HTÈš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆSÑHKšÝ\œÈS‘
K
HTÈ›Û—Øš[X›WÚÝ\œÂ‘”“ÓH›Ú™XÝÈ“Q•“ÒSˆÛY[ÈÈÓˆË˜ÛY[ÚYH˜ÛY[ÚY“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚY“Q•“ÒSˆÛY[Ú[›ÚXÙ\ÈÚHÓˆÚKœ›Ú™XÝÚYHœ›Ú™XÝÚY“Q•“ÒSˆ[YWÙ[šY\ÈHÓˆKœ›Ú™XÝÚYHœ›Ú™XÝÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™K”Ü[
H
È‚‘Ô“ÕT–HË˜ÛY[Û˜[YKœ›Ú™XÝØÛÙKœ›Ú™XÝÛ˜[YKK™\Ü^WÛ˜[YKœÝ]\Ëœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ“Ô‘Tˆ–HÝ\ÝÛY\‹›Ú™XÝÛ˜[YB“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XËœ›Ú™XÝÈ
È[›ÚXÙ\È
È[YH‹ÛÛ[[œËÜ[Ú\™K”\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[Ù\YžT™XYQ^[œÙT™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
œ›Ú™XÝØÛÙH‹”›Ú™XÝÛÙHŠKˆ
œ›Ú™XÝÛ˜[YH‹”›Ú™XÝ˜[YHŠKˆ
œ›Ú™XÝÛX[˜YÙ\ˆ‹”›Ú™XÝX[˜YÙ\ˆŠKˆ
™^[œÙWÜ™\ÜÛ[X™\ˆ‹‘^[œÙH™\Ü[X™\ˆŠKˆ
™^[œÙWÜ™\ÜÝ]H‹‘^[œÙH™\Ü]HŠKˆ
™^[œÙWÜÝ]\È‹‘^[œÙHÝ]\ÈŠKˆ
™^[œÙWÝÝ[‹‘^[œÙHÝ[ŠKˆ
œÝX›Z]YØ]‹”ÝX›Z]Y]ŠKˆ
˜\›Ý™YØ]‹\›Ý™Y]ŠKˆ
˜Ù\YžWÜÝ]\È‹Ù\YžH[YÜ˜][ÛˆÝ]\ÈŠBˆNÂ‚ˆ˜\ˆÚ\™HH›Ú™XÝ[ÙLÌÛÛ[[Û”›Ú™XÝÚ\™JÜš]\šXJNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆÓÐSTÐÑJË˜ÛY[Û˜[YK	ÉÊHTÈÝ\ÝÛY\‹ˆÓÐSTÐÑJœ›Ú™XÝØÛÙK	ÉÊHTÈ›Ú™XÝØÛÙKˆÓÐSTÐÑJœ›Ú™XÝÛ˜[YK	ÉÊHTÈ›Ú™XÝÛ˜[YKˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ›Ú™XÝÛX[˜YÙ\‹ˆÓÐSTÐÑJ\‹œ™\ÜÛ[X™\‹	ÉÊHTÈ^[œÙWÜ™\ÜÛ[X™\‹ˆÓÐSTÐÑJ\‹œ™\ÜÝ]K	ÉÊHTÈ^[œÙWÜ™\ÜÝ]KˆÓÐSTÐÑJ\‹œ™\ÜÜÝ]\Ë	ÉÊHTÈ^[œÙWÜÝ]\ËˆÓÐSTÐÑJ\‹œ™\ÜÝÝ[
HTÈ^[œÙWÝÝ[ˆ\‹œÝX›Z]YØ]ˆ\‹˜\›Ý™YØ]ˆ	Ô[™[™È[Ù[HÌHÙ\YžH[YÜ˜][ÛˆX\[™ÉÈTÈÙ\YžWÜÝ]\Â‘”“ÓH^[œÙWÜ™\ÜÈ\‚“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYH\‹\Ù\—ÚY“Q•“ÒSˆ›Ú™XÝÈÓˆœ›Ú™XÝÚYH\‹œ›Ú™XÝÚY“Q•“ÒSˆÛY[ÈÈÓˆË˜ÛY[ÚYH˜ÛY[ÚY“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™K”Ü[
H
È‚“Ô‘Tˆ–H\‹˜Ü™X]YØ]TÐÂ“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË™^[œÙWÜ™\ÜÈ
È›Ú™XÝ›Ú[œÈ‹ÛÛ[[œËÜ[Ú\™K”\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[[™Ú[™Y\”›Ú™XÝÝ™\•[™\”™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
œ›Ú™XÝØÛÙH‹”›Ú™XÝÛÙHŠKˆ
œ›Ú™XÝÛ˜[YH‹”›Ú™XÝ˜[YHŠKˆ
œ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ‹”[›™YÝ[›Ú™XÝÛÜÝŠKˆ
šÝ\œ×Ù[\™Y‹’Ý\œÈ[\™YŠKˆ
˜š[YØÛÜÝ‹š[YÛÜÝŠKˆ
œ™[XZ[š[™×Ø˜[[˜ÙH‹”™[XZ[š[™È˜[[˜ÙHŠKˆ
˜YÙ]ÜÜÚ][Ûˆ‹YÙ]ÜÚ][ÛˆŠBˆNÂ‚ˆ˜\ˆÚ\™HH›Ú™XÝ[ÙLÌÛÛ[[Û\Ú[™\ÜÕÚ\™JÜš]\šXKKÛÜš×Ù]HŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆÓÐSTÐÑJË˜ÛY[Û˜[YK	ÉÊHTÈÝ\ÝÛY\‹ˆÓÐSTÐÑJœ›Ú™XÝØÛÙK	ÉÊHTÈ›Ú™XÝØÛÙKˆÓÐSTÐÑJœ›Ú™XÝÛ˜[YK	ÉÊHTÈ›Ú™XÝÛ˜[YKˆÓÐSTÐÑJœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ
HTÈ[›™YÝÝ[Ü›Ú™XÝØÛÜÝˆÓÐSTÐÑJÕSJKšÝ\œÊK
HTÈÝ\œ×Ù[\™YˆÓÐSTÐÑJÕSJTÕSÕÚKš[›ÚXÙWÝÝ[
K
HTÈš[YØÛÜÝˆÓÐSTÐÑJœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ
HHÓÐSTÐÑJÕSJTÕSÕÚKš[›ÚXÙWÝÝ[
K
HTÈ™[XZ[š[™×Ø˜[[˜ÙKˆÐTÑBˆÒSˆÓÐSTÐÑJÕSJTÕSÕÚKš[›ÚXÙWÝÝ[
K
HˆÓÐSTÐÑJœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ
HSˆ	ÓÝ™\ˆYÙ]	ÂˆÒSˆÓÐSTÐÑJÕSJTÕSÕÚKš[›ÚXÙWÝÝ[
K
HÓÐSTÐÑJœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ
HSˆ	Õ[™\ˆYÙ]	ÂˆSÑH	Ð]YÙ]	ÂˆS‘TÈYÙ]ÜÜÚ][Û‚‘”“ÓH[YWÙ[šY\ÈB“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHK\Ù\—ÚY“Q•“ÒSˆ›Ú™XÝÈÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚY“Q•“ÒSˆÛY[ÈÈÓˆË˜ÛY[ÚYH˜ÛY[ÚY“Q•“ÒSˆÛY[Ú[›ÚXÙ\ÈÚHÓˆÚKœ›Ú™XÝÚYHœ›Ú™XÝÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™K”Ü[
H
È‚‘Ô“ÕT–HK™\Ü^WÛ˜[YKË˜ÛY[Û˜[YKœ›Ú™XÝØÛÙKœ›Ú™XÝÛ˜[YKœ[›™YÝÝ[Ü›Ú™XÝØÛÜÝ“Ô‘Tˆ–HYÙ]ÜÜÚ][Û‹Ý\ÝÛY\‹›Ú™XÝÛ˜[YK[™Ú[™Y\‚“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË[YWÙ[šY\È
È›Ú™XÝYÙ]›Ú[œÈ‹ÛÛ[[œËÜ[Ú\™K”\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[][^˜][Û“Ý™\•[™\”™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆËÈÌ×ÕUSVUSÓ—ÕSQWÑS•–WÑSPÒÂˆËÈ\š]™H][^˜][Ûˆ\™XÝHœ›ÛH[YWÙ[šY\ÈÛÈH™\Ü™[XZ[œÈ\ÙY[]™[ˆÚ[‚ˆËÈ][^˜][Û—ÝÙYZÛWÜÝ[[X\šY\È\È›ÝY]™Y[ˆÜ[]YžHHØÚY[Y›Û\›Ø‹‚ˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
™[™Ú[™Y\—Ù[XZ[‹‘[™Ú[™Y\ˆ[XZ[ŠKˆ
X[H‹•X[HŠKˆ
œ\š[ÙÜÝ\Ù]H‹”\š[ÙÝ\]HŠKˆ
œ\š[ÙÙ[™Ù]H‹”\š[Ù[™]HŠKˆ
˜š[X›WÚÝ\œÈ‹š[X›HÝ\œÈŠKˆ
››Û—Øš[X›WÚÝ\œÈ‹“›Û‹Pš[X›HÝ\œÈŠKˆ
œ×ÚÝ\œÈ‹”ÈÝ\œÈŠKˆ
Ý[ÚÝ\œÈ‹•Ý[Ý\œÈŠKˆ
œÝ[™\™Ü\š[ÙÚÝ\œÈ‹”Ý[™\™\š[ÙÝ\œÈŠKˆ
][^˜][Û—Ü\˜Ù[‹•][^˜][Ûˆ\˜Ù[ŠKˆ
][^˜][Û—ÜÜÚ][Ûˆ‹•][^˜][ÛˆÜÚ][ÛˆŠBˆNÂ‚ˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂ‚ˆ›Ú™XÝ[ÙLÌY]T˜[™ÙJÜš]\šXKÚ\™K\˜[Y]\œËKÛÜš×Ù]HŠNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË™[™Ú[™Y\ˆ‹™]Ö×HÈK™\Ü^WÛ˜[YH‹K™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœÙ[XÝY[™Ú[™Y\œÈ‹™]Ö×HÈK™\Ü^WÛ˜[YH‹K™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËX[H‹™]Ö×HÈKX[WÛ˜[YH‹KX[WÛ˜[YH‹K™\\Y[‹K™\\Y[Û˜[YHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË›Ü™Ø[š^˜][Ûˆ‹™]Ö×HÈK™\\Y[‹K™\\Y[Û˜[YHˆJNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆÓÐSTÐÑJK™[XZ[	ÉÊHTÈ[™Ú[™Y\—Ù[XZ[ˆÓÐSTÐÑJKX[WÛ˜[YK•SQŠKX[WÛ˜[YK	ÉÊK	ÉÊHTÈX[KˆRSŠKÛÜš×Ù]JHTÈ\š[ÙÜÝ\Ù]KˆPV
KÛÜš×Ù]JHTÈ\š[ÙÙ[™Ù]KˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HTÈš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHSˆSÑHKšÝ\œÈS‘
K
HTÈ›Û—Øš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑBˆÒSˆÕÑTŠÓÐSTÐÑJœË][^˜][Û—ØXÚÙ]œË˜Ø]YÛÜžWØÛÙKœË˜Ø]YÛÜžWÛ˜[YK	ÉÊJHSˆ
	ÜÉË	Ý˜XØ][Û‰Ë	ÚÛY^IÊBˆÔˆÕÑTŠÓÐSTÐÑJœË˜Ø]YÛÜžWØÛÙKœË˜Ø]YÛÜžWÛ˜[YK	ÉÊJHRÑH	É]˜XØ][Û‰IÂˆÔˆÕÑTŠÓÐSTÐÑJœË˜Ø]YÛÜžWØÛÙKœË˜Ø]YÛÜžWÛ˜[YK	ÉÊJHRÑH	ÉZÛY^IIÂˆÔˆÕÑTŠÓÐSTÐÑJœË˜Ø]YÛÜžWØÛÙKœË˜Ø]YÛÜžWÛ˜[YK	ÉÊJHRÑH	É\ÉIÂˆSˆKšÝ\œÈSÑHS‘
K
HTÈ×ÚÝ\œËˆÓÐSTÐÑJÕSJKšÝ\œÊK
HTÈÝ[ÚÝ\œËˆŽ›[Y\šXÈTÈÝ[™\™Ü\š[ÙÚÝ\œËˆÐTÑBˆÒSˆŽ›[Y\šXÈHSˆˆSÑH“ÕS‘

ÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HÈŽ›[Y\šXÊH
ˆLŠBˆS‘TÈ][^˜][Û—Ü\˜Ù[ˆÐTÑBˆÒSˆ“ÕS‘

ÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HÈŽ›[Y\šXÊH
ˆLŠHHLSˆ	ÓÝ™\ˆ\™Ù]	ÂˆÒSˆ“ÕS‘

ÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HÈŽ›[Y\šXÊH
ˆLŠHHHSˆ	Ó™X\ˆ\™Ù]	ÂˆSÑH	Õ[™\ˆ\™Ù]	ÂˆS‘TÈ][^˜][Û—ÜÜÚ][Û‚‘”“ÓH[YWÙ[šY\ÈB“Q•“ÒSˆ\Ý\Ù\œÈBˆÓˆK\Ù\—ÚYHK\Ù\—ÚY“Q•“ÒSˆ›Ú™XÝÈˆÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚY“Q•“ÒSˆ›Ú™XÝÝ\ÚÜÈˆÓˆ\Ú×ÚYHK\Ú×ÚY“Q•“ÒSˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÈœÂˆÓˆœË››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYHK››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚY“Q•“ÒSˆX[WÛY[X™\œÚ\ÈY[BˆÓˆY[K\Ù\—ÚYHK\Ù\—ÚYˆS‘
Y[K™Y™™XÝ]™WÙ[™Ù]HTÈ•SÔˆY[K™Y™™XÝ]™WÙ[™Ù]HHKÛÜš×Ù]JB“Q•“ÒSˆX[\ÈBˆÓˆKX[WÚYHY[KX[WÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™JH
È‚‘Ô“ÕT–HK™\Ü^WÛ˜[YKK™[XZ[KX[WÛ˜[YKKX[WÛ˜[YB“Ô‘Tˆ–H[™Ú[™Y\‹\š[ÙÜÝ\Ù]HTÐÂ“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË[YWÙ[šY\È
È][^˜][Ûˆ˜[˜XÚÈ›Ú[œÈ‹ÛÛ[[œËÜ[\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[Õ\ÙY™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
™[™Ú[™Y\—Ù[XZ[‹‘[™Ú[™Y\ˆ[XZ[ŠKˆ
œ×ØØ]YÛÜžH‹”ÈÈ˜XØ][ÛˆØ]YÛÜžHŠKˆ
ÛÜš×Ù]H‹‘]HŠKˆ
šÝ\œÈ‹’Ý\œÈ\ÙYŠKˆ
[Y\ÚY]ÜÝ]\È‹•[Y\ÚY]Ý]\ÈŠKˆ
[YWÙ[žWÜÝ]\È‹•[YH[žHÝ]\ÈŠBˆNÂ‚ˆ˜\ˆÚ\™HH›Ú™XÝ[ÙLÌÛÛ[[Û\Ú[™\ÜÕÚ\™JÜš]\šXKKÛÜš×Ù]HŠNÂˆÚ\™K”Ü[Y
ŠÕÑTŠÓÐSTÐÑJœË˜Ø]YÛÜžWØÛÙKœË˜Ø]YÛÜžWÛ˜[YKœË][^˜][Û—ØXÚÙ]	ÉÊJHRÑH	É]˜XØ][Û‰IÈÔˆÕÑTŠÓÐSTÐÑJœË˜Ø]YÛÜžWØÛÙKœË˜Ø]YÛÜžWÛ˜[YKœË][^˜][Û—ØXÚÙ]	ÉÊJHRÑH	É\ÉIÈÔˆÕÑTŠÓÐSTÐÑJœË˜Ø]YÛÜžWØÛÙKœË˜Ø]YÛÜžWÛ˜[YKœË][^˜][Û—ØXÚÙ]	ÉÊJHRÑH	ÉZÛY^IIÊHŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆÓÐSTÐÑJK™[XZ[	ÉÊHTÈ[™Ú[™Y\—Ù[XZ[ˆÓÐSTÐÑJœË˜Ø]YÛÜžWÛ˜[YK	ÉÊHTÈ×ØØ]YÛÜžKˆKÛÜš×Ù]KˆKšÝ\œËˆÓÐSTÐÑJËœÝ]\Ë	ÉÊHTÈ[Y\ÚY]ÜÝ]\ËˆÓÐSTÐÑJKœÝ]\Ë	ÉÊHTÈ[YWÙ[žWÜÝ]\Â‘”“ÓH[YWÙ[šY\ÈB“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHK\Ù\—ÚY“Q•“ÒSˆ[Y\ÚY]ÈÈÓˆË[Y\ÚY]ÚYHK[Y\ÚY]ÚY“Q•“ÒSˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÈœÈÓˆœË››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYHK››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™K”Ü[
H
È‚“Ô‘Tˆ–H[™Ú[™Y\‹KÛÜš×Ù]HTÐÂ“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË[YWÙ[šY\È
ÈÈØ]YÛÜžH›Ú[œÈ‹ÛÛ[[œËÜ[Ú\™K”\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[š[X›S›Ûš[X›T™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
˜Ý\ÝÛY\ˆ‹Ý\ÝÛY\ˆŠKˆ
œ›Ú™XÝÛ˜[YH‹”›Ú™XÝŠKˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
X[H‹•X[HŠKˆ
˜š[X›WÚÝ\œÈ‹š[X›HÝ\œÈŠKˆ
››Û—Øš[X›WÚÝ\œÈ‹“›Û‹Pš[X›HÝ\œÈŠKˆ
Ý[ÚÝ\œÈ‹•Ý[Ý\œÈŠKˆ
˜š[X›WÜ\˜Ù[‹š[X›H\˜Ù[ŠBˆNÂ‚ˆ˜\ˆÚ\™HH›Ú™XÝ[ÙLÌÛÛ[[Û\Ú[™\ÜÕÚ\™JÜš]\šXKKÛÜš×Ù]HŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJË˜ÛY[Û˜[YK	Ó›ÈÝ\ÝÛY\‰ÊHTÈÝ\ÝÛY\‹ˆÓÐSTÐÑJœ›Ú™XÝÛ˜[YK	Ó›Û‹\›Ú™XÝ[YIÊHTÈ›Ú™XÝÛ˜[YKˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆÓÐSTÐÑJKX[WÛ˜[YK•SQŠKX[WÛ˜[YK	ÉÊK	ÉÊHTÈX[KˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
K
HTÈš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆSÑHKšÝ\œÈS‘
K
HTÈ›Û—Øš[X›WÚÝ\œËˆÓÐSTÐÑJÕSJKšÝ\œÊK
HTÈÝ[ÚÝ\œËˆÐTÑHÒSˆÓÐSTÐÑJÕSJKšÝ\œÊK
HHSˆSÑH“ÕS‘

ÕSJÐTÑHÒSˆÓÐSTÐÑJK˜š[X›K˜š[X›KSÑJHSˆKšÝ\œÈSÑHS‘
HÈÕSJKšÝ\œÊJH
ˆLŠHS‘TÈš[X›WÜ\˜Ù[‘”“ÓH[YWÙ[šY\ÈB“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHK\Ù\—ÚY“Q•“ÒSˆ›Ú™XÝÈÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚY“Q•“ÒSˆÛY[ÈÈÓˆË˜ÛY[ÚYH˜ÛY[ÚY“Q•“ÒSˆX[WÛY[X™\œÚ\ÈY[HÓˆY[K\Ù\—ÚYHK\Ù\—ÚY“Q•“ÒSˆX[\ÈHÓˆKX[WÚYHY[KX[WÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™K”Ü[
H
È‚‘Ô“ÕT–HË˜ÛY[Û˜[YKœ›Ú™XÝÛ˜[YKK™\Ü^WÛ˜[YKKX[WÛ˜[YKKX[WÛ˜[YB“Ô‘Tˆ–HÝ\ÝÛY\‹›Ú™XÝÛ˜[YK[™Ú[™Y\‚“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË[YWÙ[šY\È
Èš[X›H›Û\‹ÛÛ[[œËÜ[Ú\™K”\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[[›ÚXÙT™XY[™\ÜÔ™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›U[YT™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\K˜XØÛÝ[[™È‹Üš]\šXJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[\›Ý˜[›Ý[™XÚÔ™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
ÛÜš×Ù]H‹•ÛÜšÈ]HŠKˆ
™^WÜÝ]\È‹‘^HÝ]\ÈŠKˆ
›X[˜YÙ\ˆ‹“X[˜YÙ\ˆŠKˆ
œÝX›Z]YØ]‹”ÝX›Z]Y]ŠKˆ
›X[˜YÙ\—Ø\›Ý™YØ]‹“X[˜YÙ\ˆ\›Ý™Y]ŠKˆ
œWØ\›Ý™YØ]‹”H\›Ý™Y]ŠKˆ
˜XØÛÝ[[™×Ü™XYWØ]‹XØÛÝ[[™È™XYH]ŠKˆ
˜›Ý[™XÚÈ‹›Ý[™XÚÈŠBˆNÂ‚ˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂˆ›Ú™XÝ[ÙLÌY]T˜[™ÙJÜš]\šXKÚ\™K\˜[Y]\œËËÛÜš×Ù]HŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆËÛÜš×Ù]KˆÓÐSTÐÑJËœÝ]\Ë	ÉÊHTÈ^WÜÝ]\ËˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈX[˜YÙ\‹ˆËœÝX›Z]YØ]ˆË›X[˜YÙ\—Ø\›Ý™YØ]ˆËœWØ\›Ý™YØ]ˆË˜XØÛÝ[[™×Ü™XYWØ]ˆÐTÑBˆÒSˆËœÝX›Z]YØ]TÈ•SSˆ	Ó›ÝÝX›Z]Y	ÂˆÒSˆË›X[˜YÙ\—Ø\›Ý™YØ]TÈ•SSˆ	ÓX[˜YÙ\ˆ\›Ý˜[[™[™ÉÂˆÒSˆËœWØ\›Ý™YØ]TÈ•SSˆ	ÔH\›Ý˜[[™[™ÉÂˆÒSˆË˜XØÛÝ[[™×Ü™XYWØ]TÈ•SSˆ	ÐXØÛÝ[[™È™XY[™\ÜÈ[™[™ÉÂˆSÑH	Ô™XYIÂˆS‘TÈ›Ý[™XÚÂ‘”“ÓH[Y\ÚY]Ù^WÜÝ]\Ù\ÈÂ“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHË\Ù\—ÚY“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHË›X[˜YÙ\—Ý\Ù\—ÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™JH
È‚“Ô‘Tˆ–HËÛÜš×Ù]HTÐË[™Ú[™Y\‚“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË[Y\ÚY]Ù^WÜÝ]\Ù\È
È\Ù\œÈ‹ÛÛ[[œËÜ[\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[Z\ÜÚ[™Õ[YT™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™ÈÙ^KÝš[™ÈX™[
O‚ˆÂˆ
™[™Ú[™Y\ˆ‹‘[™Ú[™Y\ˆŠKˆ
™[™Ú[™Y\—Ù[XZ[‹‘[™Ú[™Y\ˆ[XZ[ŠKˆ
ÙYZ×ÜÝ\Ù]H‹•ÙYZÈÝ\]HŠKˆ
ÙYZ×Ù[™Ù]H‹•ÙYZÈ[™]HŠKˆ
[Y\ÚY]ÜÝ]\È‹•[Y\ÚY]Ý]\ÈŠKˆ
Ý[ÚÝ\œÈ‹•Ý[Ý\œÈŠKˆ
›Z\ÜÚ[™×ÚÝ\œÈ‹“Z\ÜÚ[™ÈÝ\œÈŠBˆNÂ‚ˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂˆ›Ú™XÝ[ÙLÌY]T˜[™ÙJÜš]\šXKÚ\™K\˜[Y]\œËËÙYZ×ÜÝ\Ù]HŠNÂ‚ˆÝš[™ÈÜ[H‚”ÑSPÕˆÓÐSTÐÑJK™\Ü^WÛ˜[YK	ÉÊHTÈ[™Ú[™Y\‹ˆÓÐSTÐÑJK™[XZ[	ÉÊHTÈ[™Ú[™Y\—Ù[XZ[ˆËÙYZ×ÜÝ\Ù]KˆËÙYZ×Ù[™Ù]KˆÓÐSTÐÑJËœÝ]\Ë	ÉÊHTÈ[Y\ÚY]ÜÝ]\ËˆÓÐSTÐÑJÕSJKšÝ\œÊK
HTÈÝ[ÚÝ\œËˆÔ‘PUTÕ
HÓÐSTÐÑJÕSJKšÝ\œÊK
K
HTÈZ\ÜÚ[™×ÚÝ\œÂ‘”“ÓH[Y\ÚY]ÈÂ“Q•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHË\Ù\—ÚY“Q•“ÒSˆ[YWÙ[šY\ÈHÓˆK[Y\ÚY]ÚYHË[Y\ÚY]ÚYˆˆ
È›Ú™XÝ[ÙLÌÚ\™TÜ[
Ú\™JH
È‚‘Ô“ÕT–HK™\Ü^WÛ˜[YKK™[XZ[ËÙYZ×ÜÝ\Ù]KËÙYZ×Ù[™Ù]KËœÝ]\Â’U’S‘ÈÓÐSTÐÑJÕSJKšÝ\œÊK
H“Ô‘Tˆ–HËÙYZ×ÜÝ\Ù]HTÐË[™Ú[™Y\‚“SRULÈŽÂ‚ˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌ^XÝ]T™XYX›T™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKœX›XË[Y\ÚY]È
È[YWÙ[šY\È‹ÛÛ[[œËÜ[\˜[Y]\œÊNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[›Ú™XÝX\™Ú[”™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[›Ú™XÝÝ]\Ðš[Y™[XZ[š[™Ô™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[˜]Q^Ù\[Û”™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆ™\ÜH]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›U[YT™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\K˜XØÛÝ[[™È‹Üš]\šXJNÂˆ™]\›ˆ™\ÜÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[Ý\ÝÛY\”›Ùš]Xš[]T™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›PÝ\ÝÛY\”™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[ÛÜÙ[Ý]™XY[™\ÜÔ™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[›Ú™XÝÝ]\Ðš[Y™[XZ[š[™Ô™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆ›Ú™XÝ[ÙLÌZ[[™Ù™”]X[]T™\Ü\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™È™\Ü\KÝš[™ÈØ]YÛÜžKœÛÛ‘[[Y[Üš]\šXJBžÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLÌZ[™XYX›T›Ú™XÝ™\Ü\Þ[˜ÊÛÛ›™XÝ[Û‹™\Ü\KØ]YÛÜžKÜš]\šXJNÂŸB‚œÝ]XÈ
\ÝÝš[™ÏˆÜ[XÝ[Û˜\žOÝš[™ËØš™XÝˆ\˜[Y]\œÊH›Ú™XÝ[ÙLÌÛÛ[[Û\Ú[™\ÜÕÚ\™JœÛÛ‘[[Y[Üš]\šXKÝš[™È]Q^™\ÜÚ[ÛŠBžÂˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂ‚ˆ›Ú™XÝ[ÙLÌY]T˜[™ÙJÜš]\šXKÚ\™K\˜[Y]\œË]Q^™\ÜÚ[ÛŠNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË˜Ý\ÝÛY\ˆ‹™]Ö×HÈ˜Ë˜ÛY[Û˜[YH‹˜Ë˜ÛY[ØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœ›Ú™XÝ‹™]Ö×HÈœœ›Ú™XÝÛ˜[YH‹œœ›Ú™XÝØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœH‹™]Ö×HÈœK™\Ü^WÛ˜[YH‹œK™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË™[™Ú[™Y\ˆ‹™]Ö×HÈK™\Ü^WÛ˜[YH‹K™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœÙ[XÝY[™Ú[™Y\œÈ‹™]Ö×HÈK™\Ü^WÛ˜[YH‹K™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËX[H‹™]Ö×HÈKX[WÛ˜[YH‹KX[WÛ˜[YH‹K™\\Y[‹K™\\Y[Û˜[YHˆJNÂˆ›Ú™XÝ[ÙLÌYš[X›Qš[\ŠÜš]\šXKÚ\™KÓÐSTÐÑJK˜š[X›K˜š[X›K˜š[X›KSÑJHŠNÂˆ›Ú™XÝ[ÙLÌYÛÛ˜XÝ\Qš[\ŠÜš]\šXKÚ\™JNÂ‚ˆ™]\›ˆ
Ú\™K\˜[Y]\œÊNÂŸB‚œÝ]XÈ
\ÝÝš[™ÏˆÜ[XÝ[Û˜\žOÝš[™ËØš™XÝˆ\˜[Y]\œÊH›Ú™XÝ[ÙLÌÛÛ[[Û”›Ú™XÝÚ\™JœÛÛ‘[[Y[Üš]\šXJBžÂˆ˜\ˆÚ\™HH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂ‚ˆ›Ú™XÝ[ÙLÌY]T˜[™ÙJÜš]\šXKÚ\™K\˜[Y]\œËÓÐSTÐÑJœÝ\Ù]K˜Ü™X]YØ]Ž™]JHŠNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œË˜Ý\ÝÛY\ˆ‹™]Ö×HÈ˜Ë˜ÛY[Û˜[YH‹˜Ë˜ÛY[ØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœ›Ú™XÝ‹™]Ö×HÈœœ›Ú™XÝÛ˜[YH‹œœ›Ú™XÝØÛÙHˆJNÂˆ›Ú™XÝ[ÙLÌY™XYX›U^š[\ŠÜš]\šXKÚ\™K\˜[Y]\œËœH‹™]Ö×HÈœK™\Ü^WÛ˜[YH‹œK™[XZ[ˆJNÂˆ›Ú™XÝ[ÙLÌYš[X›Qš[\ŠÜš]\šXKÚ\™KÓÐSTÐÑJ˜š[X›KSÑJHŠNÂ‚ˆ™]\›ˆ
Ú\™K\˜[Y]\œÊNÂŸB‚‹ËÈÌ—ÒS•“ÒPÑWÑVÔ•Ð•TÒS‘TÔ×Ô‘TÔ•×ÑS‘‚‹ËÈÌWÔ‘PQP“WÔ‘TÔ•S‘×Ò“ÒS‘QÒST”×ÑS‘‚œÝ]XÈØš™XÝ›Ú™XÝ[ÙLÌØY™Q›Ü›X]˜[YJØš™XÝ˜[YJBžÂˆYˆ
˜[YH\È]U[YH]U[YJBˆÂˆ™]\›ˆ]U[YK•ÔÝš[™Êž^^^KSSKY›[NœÜÈŠNÂˆB‚ˆYˆ
˜[YH\È]U[YSÙ™œÙ]]U[YSÙ™œÙ]
BˆÂˆ™]\›ˆ]U[YSÙ™œÙ]•ÔÝš[™Êž^^^KSSKY›[NœÜÈžžˆŠNÂˆB‚ˆYˆ
˜[YH\È›ÛÛ›ÛÛ˜[YJBˆÂˆ™]\›ˆ›ÛÛ˜[YHÈ–Y\Èˆˆ“›ÈŽÂˆB‚ˆ™]\›ˆ˜[YNÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLÌ\Ü^JÝš[™ÈÛÛ[[“˜[YJBžÂˆÝš[™ÈÛX[™YHÛÛ[[“˜[YK”™\XÙJ—È‹ˆŠK”™\XÙJ‹H‹ˆŠK•š[J
NÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÛX[™Y
JBˆÂˆ™]\›ˆÛÛ[[“˜[YNÂˆB‚ˆ™]\›ˆÝš[™Ë’›Ú[Šˆ‹ÛX[™Y”Ü]
	È	ËÝš[™ÔÜ]Ü[ÛœË”™[[Ý™Q[\Q[šY\ÊBˆ”Ù[XÝ
\Oˆ\“[™ÝOHÈ\ˆÚ\‹•Õ\\’[˜\šX[
\ÌJH
È\”ÝXœÝš[™ÊJJJNÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLÌ][ÝJÝš[™È˜[YJBžÂˆ™]\›ˆ—ˆˆ
È˜[YK”™\XÙJ—ˆ‹——ˆŠH
È—ˆŽÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ
Ýš[™ÈØÚ[XKÝš[™È˜[YJOˆ›Ú™XÝ[ÙLÌš[™ØY™T™\ÜX›P\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™ÈØ]YÛÜžJBžÂˆ˜\ˆX›\ÈH™]È\Ý
Ýš[™ÈØÚ[XKÝš[™È˜[YKÛ™È\Ý[X]JOŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
‚”ÑSPÕˆØÚ[X[˜[YKˆ™[˜[YKˆÓÐSTÐÑJ—Û]™WÝ\
NŽ˜šYÚ[TÈ›Ý×Ù\Ý[X]B‘”“ÓH×ÜÝ]Ý\Ù\—ÝX›\Â“Ô‘Tˆ–HØÚ[X[˜[YK™[˜[YNÈ‹ÛÛ›™XÝ[ÛŠJBˆ]ØZ]\Ú[™È
˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
JBˆÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆX›\ËY

™XY\‹‘Ù]Ýš[™Ê
K™XY\‹‘Ù]Ýš[™ÊJK™XY\‹‘Ù][
ŠJJNÂˆBˆB‚ˆÝš[™Ö×H™Y™\œ™YH›Ú™XÝ[ÙLÌ™Y™\œ™YØY™UÚÙ[œÊØ]YÛÜžJNÂˆÝš[™Ö×H[ÝÙY™\Ü[™ÕX›\ÈH›Ú™XÝ[ÙLÌ[ÝÙY™\Ü[™ÕX›\ÊØ]YÛÜžJNÂ‚ˆ˜\ˆØÛÜ™YHX›\Âˆ”Ù[XÝ
Oˆ™]ÂˆÂˆX›HHˆØÛÜ™HH›Ú™XÝ[ÙLÌØÛÜ™TØY™UX›JØ]YÛÜžK“˜[YK™Y™\œ™Y[ÝÙY™\Ü[™ÕX›\Ë‘\Ý[X]JBˆJBˆ•Ú\™JOˆ”ØÛÜ™Hˆ
Bˆ“Ü™\žQ\ØÙ[™[™ÊOˆ”ØÛÜ™JBˆ•[žQ\ØÙ[™[™ÊOˆ•X›K‘\Ý[X]JBˆ•[žJOˆ•X›K“˜[YJBˆ•Ó\Ý

NÂ‚ˆYˆ
ØÛÜ™YÛÝ[OH
BˆÂˆ™]\›ˆ
ˆ‹ˆŠNÂˆB‚ˆ™]\›ˆ
ØÛÜ™YÌK•X›K”ØÚ[XKØÛÜ™YÌK•X›K“˜[YJNÂŸB‚œÝ]XÈÝš[™Ö×H›Ú™XÝ[ÙLÌ™Y™\œ™YØY™UÚÙ[œÊÝš[™ÈØ]YÛÜžJBžÂˆ™]\›ˆØ]YÛÜžHÝÚ]ÚˆÂˆ˜XØÛÝ[[™ÈˆOˆ™]Ö×HÈš[›ÚXÙH‹˜š[[™È‹™^Ü‹[YWÙ[žH‹[YWÙ[šY\È‹[Y\ÚY]ˆKˆ[YHˆOˆ™]Ö×HÈ[YWÙ[žH‹[YWÙ[šY\È‹[Y\ÚY]‹[YHˆKˆ˜Ý\ÝÛY\ˆˆOˆ™]Ö×HÈ˜Ý\ÝÛY\ˆ‹˜Ý\ÝÛY\œÈˆKˆœ›Ú™XÝˆOˆ™]Ö×HÈœ›Ú™XÝ‹œ›Ú™XÝÈˆKˆœHˆOˆ™]Ö×HÈœ›Ú™XÝ‹˜\ÜÚYÛ›Y[‹[YWÙ[žH‹[YWÙ[šY\È‹\Ù\ˆ‹\Ù\œÈˆKˆ™[™Ú[™Y\ˆˆOˆ™]Ö×HÈ[YWÙ[žH‹[YWÙ[šY\È‹[Y\ÚY]‹\Ù\ˆ‹\Ù\œÈ‹˜\ÜÚYÛ›Y[ˆKˆX[HˆOˆ™]Ö×HÈX[H‹X[\È‹\Ù\ˆ‹\Ù\œÈ‹[YWÙ[žH‹[YWÙ[šY\ÈˆKˆ˜]Y]ˆOˆ™]Ö×HÈ˜]Y]‹™]™[‹™]™[È‹˜\›Ý˜[‹™^ÜˆKˆœÞ\Ý[HˆOˆ™]Ö×HÈœ™\Ü[™×ÜÞ\Ý[WÚX[ØØ][ÙÈˆKˆ˜\HˆOˆ™]Ö×HÈœ™\Ü[™×Ø\WÜÝ]\×ØØ][ÙÈˆKˆ™^\›˜[ˆOˆ™]Ö×HÈœ™\Ü[™×Ù^\›˜[ØÛÛ›™XÝ[Û—ØØ][ÙÈˆKˆ˜]]ˆOˆ™]Ö×HÈœÙ\ÜÚ[Ûˆ‹˜]]‹›ÙÚ[ˆ‹šY]×Ø\È‹˜]Y]ˆKˆ˜ZHˆOˆ™]Ö×HÈœÛÝ×ØZWÝ[YWÙ[žWÙ˜YÈ‹œÛÝ×ØZWÝ[YWÙ[žWÜØÛÜWØÚXÚÜÈ‹œÛÝ×ØZWÝ[YWÙ[žWØZWÜ›ÝšY\—Ü™XY[™\ÜÈˆKˆ››ÝYšXØ][ÛˆˆOˆ™]Ö×HÈœ›ÙXÝ[Û—Û›ÝYšXØ][Û—Ù]™[È‹[YWØÛÛ\X[˜ÙWÛ›ÝYšXØ][Û—Ù[]™\žWÙ]™[È‹œÞ\Ý[WÙ[XZ[Ü›ÝšY\—Ý\ÝÙ]™[ÈˆKˆX]ˆOˆ™]Ö×HÈX]Ù]šY[˜ÙWØØ\\™WÙ]™[È‹X]ÝÛÜšÙ›Ý×Ý˜[Y][Û—ÜØÙ[˜\š[ÜÈ‹X]Ü›ÛWÝ˜[Y][Û—ÛX]š^ˆKˆ›Xœ˜\žHˆOˆ™]Ö×HÈœ™\Ü[™×ÜØ]™YÜ™\ÜÙYš[š][ÛœÈ‹œ™\Ü[™×Ý[\]\ÈˆKˆ™^XÝ]]™HˆOˆ™]Ö×HÈœ™\Ü[™×Ù^XÝ][Û—Ù]™[È‹œ™\Ü[™×Ù]WÙÛXZ[œÈ‹œ›Ú™XÝ‹[YWÙ[žH‹š[›ÚXÙHˆKˆÈOˆ™]Ö×HÈ[YWÙ[žH‹œ›Ú™XÝ‹˜Ý\ÝÛY\ˆˆBˆNÂŸB‚œÝ]XÈÝš[™Ö×H›Ú™XÝ[ÙLÌ[ÝÙY™\Ü[™ÕX›\ÊÝš[™ÈØ]YÛÜžJBžÂˆ™]\›ˆØ]YÛÜžHÝÚ]ÚˆÂˆœÞ\Ý[HˆOˆ™]Ö×HÈœ™\Ü[™×ÜÞ\Ý[WÚX[ØØ][ÙÈˆKˆ˜\HˆOˆ™]Ö×HÈœ™\Ü[™×Ø\WÜÝ]\×ØØ][ÙÈˆKˆ™^\›˜[ˆOˆ™]Ö×HÈœ™\Ü[™×Ù^\›˜[ØÛÛ›™XÝ[Û—ØØ][ÙÈˆKˆ›Xœ˜\žHˆOˆ™]Ö×HÈœ™\Ü[™×ÜØ]™YÜ™\ÜÙYš[š][ÛœÈ‹œ™\Ü[™×Ý[\]\ÈˆKˆ™^XÝ]]™HˆOˆ™]Ö×HÈœ™\Ü[™×Ù^XÝ][Û—Ù]™[È‹œ™\Ü[™×Ù]WÙÛXZ[œÈˆKˆÈOˆ\œ˜^K‘[\OÝš[™ÏŠ
BˆNÂŸB‚œÝ]XÈ[›Ú™XÝ[ÙLÌØÛÜ™TØY™UX›JÝš[™ÈØ]YÛÜžKÝš[™ÈX›S˜[YKÝš[™Ö×H™Y™\œ™YÚÙ[œËÝš[™Ö×H[ÝÙY™\Ü[™ÕX›\ËÛ™È›ÝÑ\Ý[X]JBžÂˆÝš[™È˜[YHHX›S˜[YK•ÓÝÙ\’[˜\šX[

NÂˆ[ØÛÜ™HHÂ‚ˆYˆ
˜[YK”Ý\ÕÚ]
œ™\Ü[™×ÈŠH	‰ˆX[ÝÙY™\Ü[™ÕX›\ËÛÛZ[œÊ˜[YJJBˆÂˆ™]\›ˆÂˆB‚ˆ›Ü™XXÚ
˜\ˆÚÙ[ˆ[ˆ™Y™\œ™YÚÙ[œÊBˆÂˆÝš[™ÈÝÙ\™YÚÙ[ˆHÚÙ[‹•ÓÝÙ\’[˜\šX[

NÂ‚ˆYˆ
˜[YHOHÝÙ\™YÚÙ[ŠBˆÂˆØÛÜ™H
ÏHLÂˆBˆ[ÙHYˆ
˜[YKÛÛZ[œÊÝÙ\™YÚÙ[ŠJBˆÂˆØÛÜ™H
ÏHÍNÂˆBˆB‚ˆYˆ
›ÝÑ\Ý[X]Hˆ
BˆÂˆØÛÜ™H
ÏHMNÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ˜\˜Ú]™HŠH˜[YKÛÛZ[œÊ˜˜XÚÝ\ŠH˜[YKÛÛZ[œÊ\ÝŠJBˆÂˆØÛÜ™HOHÌÂˆB‚ˆ™]\›ˆØÛÜ™NÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ\Ý
Ýš[™È˜[YKÝš[™È]U\JOˆ›Ú™XÝ[ÙLÌÙ]ØY™PÛÛ[[œÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™ÈØÚ[XKÝš[™ÈX›JBžÂˆËÈÌÔ‘TÔ•ÐTWÑÑUÐÓÓSS”×Ô×ÐÐUSÑ×Ñ’VˆËÈ\ÙH×ØØ][ÙÈš\œÝ[ˆ[™›Ü›X][Û—ÜØÚ[XH\ÈH˜[˜XÚË‚ˆ˜\ˆÛÛ[[œÈH™]È\Ý
Ýš[™È˜[YKÝš[™È]U\JOŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
‚”ÑSPÕˆK˜]˜[YHTÈÛÛ[[—Û˜[YKˆ×ØØ][ÙË™›Ü›X]Ý\JK˜]\YK˜]\[Ù
HTÈ]WÝ\B‘”“ÓH×ØØ][ÙËœ×Ø]šX]HB’“ÒSˆ×ØØ][ÙËœ×ØÛ\ÜÈÂˆÓˆË›ÚYHK˜]™[Y’“ÒSˆ×ØØ][ÙËœ×Û˜[Y\ÜXÙH‚ˆÓˆ‹›ÚYHËœ™[˜[Y\ÜXÙB•ÒT‘H‹›œÜ˜[YHHØÚ[XBˆS‘Ëœ™[˜[YHHX›BˆS‘K˜][HˆˆS‘“ÕK˜]\Ù›ÜY“Ô‘Tˆ–HK˜][NÈ‹ÛÛ›™XÝ[ÛŠJBˆÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœØÚ[XH‹ØÚ[XJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJX›H‹X›JNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂ‚ˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÛÛ[[œËY

™XY\‹‘Ù]Ýš[™Ê
K™XY\‹‘Ù]Ýš[™ÊJJJNÂˆBˆB‚ˆYˆ
ÛÛ[[œËÛÝ[ˆ
BˆÂˆ™]\›ˆÛÛ[[œÎÂˆB‚ˆ]ØZ]\Ú[™È
˜\ˆ˜[˜XÚÐÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
‚”ÑSPÕÛÛ[[—Û˜[YK]WÝ\B‘”“ÓH[™›Ü›X][Û—ÜØÚ[XK˜ÛÛ[[œÂ•ÒT‘HX›WÜØÚ[XHHØÚ[XBˆS‘X›WÛ˜[YHHX›B“Ô‘Tˆ–HÜ™[˜[ÜÜÚ][ÛŽÈ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ˜[˜XÚÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœØÚ[XH‹ØÚ[XJNÂˆ˜[˜XÚÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJX›H‹X›JNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ˜[˜XÚÔ™XY\ˆH]ØZ]˜[˜XÚÐÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂ‚ˆÚ[H
]ØZ]˜[˜XÚÔ™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÛÛ[[œËY

˜[˜XÚÔ™XY\‹‘Ù]Ýš[™Ê
K˜[˜XÚÔ™XY\‹‘Ù]Ýš[™ÊJJJNÂˆBˆB‚ˆ™]\›ˆÛÛ[[œÎÂŸB‚œÝ]XÈ\Ý
Ýš[™È˜[YKÝš[™È]U\JOˆ›Ú™XÝ[ÙLÌÙ[XÝØY™PÛÛ[[œÊÝš[™ÈØ]YÛÜžK\Ý
Ýš[™È˜[YKÝš[™È]U\JOˆÛÛ[[œÊBžÂˆÝš[™Ö×H™Y™\œ™YHØ]YÛÜžHÝÚ]ÚˆÂˆ˜XØÛÝ[[™ÈˆOˆ™]Ö×HÈš[›ÚXÙH‹˜Ý\ÝÛY\ˆ‹œ›Ú™XÝ‹™[™ØYÙ[Y[‹˜ÛÛ˜XÝ‹œÈ‹œ][ÝH‹™\ØÜš\[Ûˆ‹šÝ\œÈ‹œ]X[]H‹œ˜]H‹˜[[Ý[‹ÛÜšÈ‹›ØØ][Ûˆ‹œÝ]\È‹™]H‹˜Ü™X]Y‹˜\›Ý™Y‹™^ÜYˆKˆ[YHˆOˆ™]Ö×HÈ[YH‹™[žH‹™]H‹˜Ý\ÝÛY\ˆ‹œ›Ú™XÝ‹™[™Ú[™Y\ˆ‹\Ù\ˆ‹šÝ\œÈ‹œÝ]\È‹˜š[X›H‹™\ØÜš\[Ûˆ‹››Ý\È‹˜\›Ý™Y‹œÝX›Z]Y‹˜Ü™X]YˆKˆ˜Ý\ÝÛY\ˆˆOˆ™]Ö×HÈ˜Ý\ÝÛY\ˆ‹›˜[YH‹œÝ]\È‹˜Ü™X]Y‹\]YˆKˆœ›Ú™XÝˆOˆ™]Ö×HÈœ›Ú™XÝ‹˜Ý\ÝÛY\ˆ‹œH‹›X[˜YÙ\ˆ‹œÝ]\È‹œÛÝÈ‹™ÜÙ‹˜\ÜÚYÛ›Y[‹˜Ü™X]Y‹\]YˆKˆœHˆOˆ™]Ö×HÈœH‹œ›Ú™XÝÛX[˜YÙ\ˆ‹›X[˜YÙ\ˆ‹œ›Ú™XÝ‹˜Ý\ÝÛY\ˆ‹œÝ]\È‹˜Ü™X]Y‹\]YˆKˆ™[™Ú[™Y\ˆˆOˆ™]Ö×HÈ™[™Ú[™Y\ˆ‹\Ù\ˆ‹™\Ü^H‹™[XZ[‹X[H‹œ›Ú™XÝ‹šÝ\œÈ‹œÝ]\È‹˜Ü™X]Y‹\]YˆKˆX[HˆOˆ™]Ö×HÈX[H‹\Ù\ˆ‹™[™Ú[™Y\ˆ‹›X[˜YÙ\ˆ‹šÝ\œÈ‹œÝ]\È‹˜Ü™X]Y‹\]YˆKˆ˜]Y]ˆOˆ™]Ö×HÈ™]™[‹˜]Y]‹˜XÝÜˆ‹œ›ÛH‹˜XÝ[Ûˆ‹œÝ]\È‹˜Ü™X]Y‹[Y\Ý[\‹››Ý\ÈˆKˆœÞ\Ý[HˆOˆ™]Ö×HÈ˜ÛÛ\Û™[‹šX[‹œÝ]\È‹››Ý\È‹˜Ü™X]YˆKˆ˜\HˆOˆ™]Ö×HÈ˜\H‹œ]‹›[Ù[H‹œÝ]\È‹œÝXØÙ\ÜÈ‹˜Ü™X]YˆKˆ™^\›˜[ˆOˆ™]Ö×HÈ˜ÛÛ›™XÝ[Ûˆ‹œ›ÝšY\ˆ‹\H‹›ÝÛ™\ˆ‹œÝ]\È‹˜Ü™X]YˆKˆ˜]]ˆOˆ™]Ö×HÈ˜]]‹œÙ\ÜÚ[Ûˆ‹\Ù\ˆ‹œ›ÛH‹œÝ]\È‹˜Ü™X]YˆKˆ˜ZHˆOˆ™]Ö×HÈ˜ZH‹œÛÝÈ‹œØÛÜH‹™˜Y‹™[™Ú[™Y\ˆ‹œ›Ú™XÝ‹œÝ]\È‹˜Ü™X]YˆKˆ››ÝYšXØ][ÛˆˆOˆ™]Ö×HÈ››ÝYšXØ][Ûˆ‹œ›ÝšY\ˆ‹œ™XÚ\Y[‹œÝ]\È‹˜Ü™X]YˆKˆX]ˆOˆ™]Ö×HÈœØÙ[˜\š[È‹œ›ÛH‹ÛÜšÙ›ÝÈ‹™]šY[˜ÙH‹œÝ]\È‹˜Ü™X]YˆKˆ›Xœ˜\žHˆOˆ™]Ö×HÈœ™\Ü‹[\]H‹˜Üš]\šXH‹›ÝÛ™\ˆ‹˜ØY[˜ÙH‹™›Ü›X]‹œÝ]\È‹˜Ü™X]YˆKˆ™^XÝ]]™HˆOˆ™]Ö×HÈ™ÛXZ[ˆ‹›˜[YH‹™\ØÜš\[Ûˆ‹˜]YY[˜ÙH‹›ÝÛ™\ˆ‹˜Ü™X]YˆKˆÈOˆ™]Ö×HÈšY‹›˜[YH‹œÝ]\È‹˜Ü™X]YˆBˆNÂ‚ˆ˜\ˆÙ[XÝYHÛÛ[[œÂˆ”Ù[XÝ
ÈOˆ™]ÂˆÂˆÛÛ[[ˆHËˆØÛÜ™HH™Y™\œ™Y[žJOˆË“˜[YKÛÛZ[œÊÝš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJHÈŒˆˆJBˆ“Ü™\žQ\ØÙ[™[™ÊOˆ”ØÛÜ™JBˆ•[žJOˆÛÛ[[œË‘š[™[™^
ÈOˆË“˜[YHOHÛÛ[[‹“˜[YJJBˆ”Ù[XÝ
OˆÛÛ[[ŠBˆ•ZÙJ
Bˆ•Ó\Ý

NÂ‚ˆYˆ
Ù[XÝYÛÝ[OH
BˆÂˆÙ[XÝYHÛÛ[[œË•ZÙJ
K•Ó\Ý

NÂˆB‚ˆ™]\›ˆÙ[XÝYÂŸB‚œÝ]XÈ
Ýš[™ÈÜ[XÝ[Û˜\žOÝš[™ËØš™XÝˆ\˜[Y]\œÊH›Ú™XÝ[ÙLÌZ[ØY™UÚ\™PÛ]\ÙJœÛÛ‘[[Y[Üš]\šXK\Ý
Ýš[™È˜[YKÝš[™È]U\JOˆÛÛ[[œÊBžÂˆ˜\ˆÚ\™T\ÈH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆ\˜[Y]\œÈH™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝŠ
NÂ‚ˆ›ÚYY^š[\ŠÝš[™ÈšY[˜[YKÝš[™Ö×HÛÛ[[•ÚÙ[œÊBˆÂˆÝš[™È˜[YHH›Ú™XÝ[ÙLÌØY™T™XYÝš[™ÊÜš]\šXKšY[˜[YJNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜[YJHÝš[™Ë‘\]X[Ê˜[YK[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÂˆ™]\›ŽÂˆB‚ˆ˜\ˆX]Ú[™ÐÛÛ[[œÈHÛÛ[[œÂˆ•Ú\™JÈOˆÛÛ[[•ÚÙ[œË[žJÚÙ[ˆOˆË“˜[YKÛÛZ[œÊÚÙ[‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJJBˆ•ZÙJ
Bˆ•Ó\Ý

NÂ‚ˆYˆ
X]Ú[™ÐÛÛ[[œËÛÝ[OH
BˆÂˆ™]\›ŽÂˆB‚ˆÝš[™È\˜[Y]\“˜[YHHœˆ
È\˜[Y]\œËÛÝ[Âˆ\˜[Y]\œÖÜ\˜[Y]\“˜[YWHH‰Hˆ
È˜[YH
È‰HŽÂ‚ˆÚ\™T\ËY
Šˆ
ÈÝš[™Ë’›Ú[ŠˆÔˆ‹X]Ú[™ÐÛÛ[[œË”Ù[XÝ
ÈOˆ›Ú™XÝ[ÙLÌ][ÝJË“˜[YJH
ÈŽŽ^SRÑHˆ
È\˜[Y]\“˜[YJJH
ÈŠHŠNÂˆB‚ˆY^š[\Š˜Ý\ÝÛY\ˆ‹™]Ö×HÈ˜Ý\ÝÛY\ˆ‹˜ÛY[ˆJNÂˆY^š[\Šœ›Ú™XÝ‹™]Ö×HÈœ›Ú™XÝ‹™[™ØYÙ[Y[ˆJNÂˆY^š[\ŠœH‹™]Ö×HÈœH‹œ›Ú™XÝÛX[˜YÙ\ˆ‹›X[˜YÙ\ˆˆJNÂˆY^š[\Š™[™Ú[™Y\ˆ‹™]Ö×HÈ™[™Ú[™Y\ˆ‹\Ù\ˆ‹™[\ÞYYH‹™\Ü^H‹™[XZ[ˆJNÂˆY^š[\ŠX[H‹™]Ö×HÈX[H‹™\\Y[ˆJNÂˆY^š[\Š˜ÛÛ˜XÝ\H‹™]Ö×HÈ˜ÛÛ˜XÝˆJNÂˆY^š[\Š[YQ[žTÝ]\È‹™]Ö×HÈœÝ]\ÈˆJNÂˆY^š[\Š˜\›Ý˜[Ý]\È‹™]Ö×HÈ˜\›Ý˜[‹œÝ]\ÈˆJNÂˆY^š[\Šš[›ÚXÙTÝ]\È‹™]Ö×HÈš[›ÚXÙH‹œÝ]\ÈˆJNÂˆY^š[\ŠÛÜšÐÛÙH‹™]Ö×HÈÛÜš×ØÛÙH‹ÛÜšÈˆJNÂˆY^š[\ŠÛÜšÓØØ][Ûˆ‹™]Ö×HÈ›ØØ][ÛˆˆJNÂ‚ˆ™]\›ˆ
Ú\™T\ËÛÝ[OHÈˆˆˆˆÒT‘Hˆ
ÈÝš[™Ë’›Ú[ŠˆS‘‹Ú\™T\ÊK\˜[Y]\œÊNÂŸB‚‹ËÈÌÔ‘TÔ•ÐTWÒ”ÓÓ—ÔÐQ‘WÒST”×ÑS‘‹ÊˆÎWÐÑT•Q–WÔPÑRÓT—ÑS‘ÒS•×ÔÕT•
‹Â˜\ˆ›Ú™XÝ[ÙLÎPÙ\YžT™\]Z\™YÙ^\ÈH™]Ö×BžÂˆÑT•Q–WÐTÑWÕT“‹ˆÑT•Q–WÐUUÓSÑH‹ˆÑT•Q–WÐÓÓTS–WÒQ‚ŸNÂ‚˜›ÛÛ›Ú™XÝ[ÙLÎPÙ\YžR\Õ˜[YJÝš[™ÈÙ^JBžÂˆ™]\›ˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÙ^JJNÂŸB‚›Øš™XÝ›Ú™XÝ[ÙLÎPÙ\YžPÛÛ™šYÔÛ˜\ÚÝ

BžÂˆ˜\ˆZ\ÜÚ[™ÒÙ^\ÈH›Ú™XÝ[ÙLÎPÙ\YžT™\]Z\™YÙ^\Âˆ•Ú\™JÙ^HOˆT›Ú™XÝ[ÙLÎPÙ\YžR\Õ˜[YJÙ^JJBˆ•Ð\œ˜^J
NÂ‚ˆ˜\ˆ]][ÙHH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÑT•Q–WÐUUÓSÑHŠHÏÈœXÙZÛ\ˆŽÂˆ˜\ˆžT[“Û›HHÝš[™Ë‘\]X[Ê[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÑT•Q–WÑ–WÔ•S—ÓÓ“HŠHÏÈYH‹YH‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂ‚ˆ™]\›ˆ™]ÂˆÂˆÝ]\ÈHœXÙZÛ\ˆ‹ˆÛÛ›™XÝÜˆHÙ\YžH‹ˆÛÛ™šYÝ\™YHZ\ÜÚ[™ÒÙ^\Ë“[™ÝOHˆØ[”[“]™TÞ[˜ÈH˜[ÙKˆžT[“Û›Kˆ]][ÙKˆZ\ÜÚ[™ÐÛÛ™šYÒÙ^\ÈHZ\ÜÚ[™ÒÙ^\ËˆØY™PÛÛ™šYÈH™]ÂˆÂˆ˜\ÙU\›ÛÛ™šYÝ\™YH›Ú™XÝ[ÙLÎPÙ\YžR\Õ˜[YJÑT•Q–WÐTÑWÕT“ŠKˆ\RÙ^PÛÛ™šYÝ\™YH›Ú™XÝ[ÙLÎPÙ\YžR\Õ˜[YJÑT•Q–WÐTWÒÑVHŠKˆÛY[YÛÛ™šYÝ\™YH›Ú™XÝ[ÙLÎPÙ\YžR\Õ˜[YJÑT•Q–WÐÓQS•ÒQŠKˆÛY[ÙXÜ™]ÛÛ™šYÝ\™YH›Ú™XÝ[ÙLÎPÙ\YžR\Õ˜[YJÑT•Q–WÐÓQS•ÔÑPÔ‘UŠKˆÛÛ\[žRYÛÛ™šYÝ\™YH›Ú™XÝ[ÙLÎPÙ\YžR\Õ˜[YJÑT•Q–WÐÓÓTS–WÒQŠKˆÚÙ[•\›ÛÛ™šYÝ\™YH›Ú™XÝ[ÙLÎPÙ\YžR\Õ˜[YJÑT•Q–WÕÒÑS—ÕT“ŠKˆÞ[˜ÓÛÚØ˜XÚÑ^\ÈH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÑT•Q–WÔÖS×ÓÓÒÐPÒ×ÑVTÈŠHÏÈH‹ˆ\›Ý™YÛ›HH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÑT•Q–WÔÖS×ÐT“Õ‘QÓÓ“HŠHÏÈYH‹ˆ[˜ÛYT™XÙZ\ÈH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÑT•Q–WÔÖS×ÒSÓQWÔ‘PÑRTÈŠHÏÈYH‹ˆ›Ú™XÝÛÙQšY[H[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÑT•Q–WÔ“Ò‘PÕÐÓÑWÑ’QSŠHÏÈ”›Ú™XÝÛÙH‹ˆÝ\ÝÛY\‘šY[H[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÑT•Q–WÐÕTÕÓQT—Ñ’QSŠHÏÈÝ\ÝÛY\ˆ‹ˆš[X›Q›YÑšY[H[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›JÑT•Q–WÐ’SP“WÑ“Q×Ñ’QSŠHÏÈš[X›H‚ˆKˆY\ÜØYÙHHÙ\YžHXÙZÛ\œÈ\™H™XYKˆ]™HÞ[˜È™[XZ[œÈ\ØX›Y[[™X[Ù\™\‹\ÚYHÜ™Y[X[È[™ÛÛ›™XÝÜˆÙÚXÈ\™H[\[Y[Yˆ‹ˆÙ[™\˜]Y]HÞ\Ý[K‘]U[YSÙ™œÙ]•]Ó›ÝÂˆNÂŸB‚˜\ˆ›Ú™XÝ[ÙLÎTXÙZÛ\‘^[œÙ\ÈH™]Ö×BžÂˆ™]ÂˆÂˆÙ\YžT™\ÜYHÑT•Q–KTPÑRÓT‹LH‹ˆ[\ÞYYQ[XZ[H™[\ÞYYP^[\K˜ÛÛH‹ˆÝ\ÝÛY\“˜[YHHÝ\ÝÛY\ˆX\[™È[™[™È‹ˆ›Ú™XÝÛÙHH”“Ò‘PÕPÓÑKTS‘S‘È‹ˆ™\ÜÝ]\ÈH\›Ý™YXÙZÛ\ˆ‹ˆ^[œÙPØ]YÛÜžHH•˜]™[‹ˆ[[Ý[HŒKˆÝ\œ™[˜ÞHH•TÑ‹ˆš[X›HHYKˆX\[™ÔÝ]\ÈH”XÙZÛ\ˆÛ›H‹ˆš[[™ÔÝ]\ÈH“›Ý™XYH›Üˆš[[™È‚ˆKˆ™]ÂˆÂˆÙ\YžT™\ÜYHÑT•Q–KTPÑRÓT‹Lˆ‹ˆ[\ÞYYQ[XZ[H˜ÛÛœÝ[[^[\K˜ÛÛH‹ˆÝ\ÝÛY\“˜[YHHÝ\ÝÛY\ˆX\[™È[™[™È‹ˆ›Ú™XÝÛÙHH”“Ò‘PÕPÓÑKTS‘S‘È‹ˆ™\ÜÝ]\ÈH\›Ý™YXÙZÛ\ˆ‹ˆ^[œÙPØ]YÛÜžHH“YX[È‹ˆ[[Ý[HŒKˆÝ\œ™[˜ÞHH•TÑ‹ˆš[X›HHYKˆX\[™ÔÝ]\ÈH”XÙZÛ\ˆÛ›H‹ˆš[[™ÔÝ]\ÈH“›Ý™XYH›Üˆš[[™È‚ˆBŸNÂ‚˜\ˆ›Ú™XÝ[ÙLÎTXÙZÛ\‘^Ù\[ÛœÈH™]Ö×BžÂˆ™]ÂˆÂˆ^Ù\[ÛÛÙHH“RTÔÒS‘×Ô“Ò‘PÕÓPTS‘È‹ˆÙ]™\š]HH’YÚ‹ˆY\ÜØYÙHHÙ\YžH›Ú™XÝØÝ\ÝÛY\ˆšY[]\ÝX\ÈH[ÙH›Ú™XÝ™Y›Ü™HH^[œÙHØ[ˆ™Hš[Yˆ‹ˆ™\ÛÛ][ÛˆHÛÛ™š\›HHÙ\YžHÝ\ÝÛHšY[]ÝÜ™\È›Ú™XÝÛÙKÝ\ÝÛY\‹›Øˆ[X™\‹ÜˆÛÜÝÙ[\‹ˆ‚ˆKˆ™]ÂˆÂˆ^Ù\[ÛÛÙHH“RTÔÒS‘×ÑSTÖQQWÓPTS‘È‹ˆÙ]™\š]HH“YY][H‹ˆY\ÜØYÙHHÙ\YžH[\ÞYYHY[]H]\ÝX\È[ˆXÝ]™H[ÙH\Ù\‹ˆ‹ˆ™\ÛÛ][ÛˆH“X\Ù\YžH[\ÞYYH[XZ[Üˆ[\ÞYYHQÈH[ÙH\Ù\ˆ\™XÝÜžKˆ‚ˆKˆ™]ÂˆÂˆ^Ù\[ÛÛÙHH“RTÔÒS‘×ÐÐUQÓÔ–WÓPTS‘È‹ˆÙ]™\š]HH“YY][H‹ˆY\ÜØYÙHHÙ\YžH^[œÙHØ]YÛÜžH]\ÝX\ÈH[ÙHš[[™ËØXØÛÝ[[™ÈØ]YÛÜžKˆ‹ˆ™\ÛÛ][ÛˆHÜ™X]HØ]YÛÜžHX\[™È›Üˆš[X›K™Z[X\œØX›K›Û‹Xš[X›K[™™XÙZ\\™\]Z\™YØ]YÛÜšY\Ëˆ‚ˆBŸNÂ‚˜\“X\Ù]
‹Ø\KØÙ\YžKØÛÛ™šYË\XÙZÛ\ˆ‹

HOˆ›Ú™XÝ[ÙLÎPÙ\YžPÛÛ™šYÔÛ˜\ÚÝ

JNÂ‚˜\“X\Ù]
‹Ø\KØÙ\YžKÜÞ[˜ËÜÝ]\È‹

HOˆ™]ÂžÂˆÝ]\ÈHœXÙZÛ\—Ü™XYH‹ˆÛÛ›™XÝÜˆHÙ\YžH‹ˆØ[”[“]™TÞ[˜ÈH˜[ÙKˆ\ÝÞ[˜ÔÝ]\ÈH“›Ý[ˆ‹ˆ\ÝÞ[˜Ð]H
Ýš[™ÏÊ[[ˆ\Ý™]šY]Ð]H
Ýš[™ÏÊ[[ˆÝYÙY^[œÙPÛÝ[H›Ú™XÝ[ÙLÎTXÙZÛ\‘^[œÙ\Ë“[™Ýˆ^Ù\[ÛÛÝ[H›Ú™XÝ[ÙLÎTXÙZÛ\‘^Ù\[ÛœË“[™ÝˆY\ÜØYÙHH”XÙZÛ\ˆÞ[˜È[™Ú[È\™H]˜Z[X›Kˆ]™HÙ\YžHTHØ[È\™H›Ý[˜X›YY]ˆ‹ˆÛÛ™šYÈH›Ú™XÝ[ÙLÎPÙ\YžPÛÛ™šYÔÛ˜\ÚÝ

KˆÙ[™\˜]Y]HÞ\Ý[K‘]U[YSÙ™œÙ]•]Ó›ÝÂŸJNÂ‚˜\“X\Ù]
‹Ø\KØÙ\YžKÙ^[œÙ\ËÜÝYÙY‹

HOˆ™]ÂžÂˆÝ]\ÈHœXÙZÛ\ˆ‹ˆÛÝ[H›Ú™XÝ[ÙLÎTXÙZÛ\‘^[œÙ\Ë“[™ÝˆÝYÙY^[œÙ\ÈH›Ú™XÝ[ÙLÎTXÙZÛ\‘^[œÙ\ËˆY\ÜØYÙHH•\ÙH\™HXÙZÛ\ˆ™XÛÜ™ÈÚÝÚ[™ÈH]\™HÝYÙY^[œÙHÚ\Kˆ^H\™H›Ý[\ÜYœ›ÛHÙ\YžKˆ‹ˆÙ[™\˜]Y]HÞ\Ý[K‘]U[YSÙ™œÙ]•]Ó›ÝÂŸJNÂ‚˜\“X\Ù]
‹Ø\KØÙ\YžKÙ^Ù\[ÛœÈ‹

HOˆ™]ÂžÂˆÝ]\ÈHœXÙZÛ\ˆ‹ˆÛÝ[H›Ú™XÝ[ÙLÎTXÙZÛ\‘^Ù\[ÛœË“[™Ýˆ^Ù\[ÛœÈH›Ú™XÝ[ÙLÎTXÙZÛ\‘^Ù\[ÛœËˆY\ÜØYÙHH•\ÙH\™HXÙZÛ\ˆ^Ù\[Ûˆ\\È^XÝY\š[™È]\™HÙ\YžH^[œÙH[\Üˆ‹ˆÙ[™\˜]Y]HÞ\Ý[K‘]U[YSÙ™œÙ]•]Ó›ÝÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KØÙ\YžKÝ\ÝXÛÛ›™XÝ[Ûˆ‹

HOˆ™]ÂžÂˆÝ]\ÈHœXÙZÛ\—ØÛÛ›™XÝ[Û—Ý\Ý‹ˆÛÛ›™XÝÜˆHÙ\YžH‹ˆÝXØÙ\ÜÈH˜[ÙKˆØ[”[“]™TÞ[˜ÈH˜[ÙKˆY\ÜØYÙHHÛÛ›™XÝ[Ûˆ\ÝXÙZÛ\ˆÛÛ\]Yˆ™X[\Ý™\]Z\™\ÈÙ\YžHTHÜ™Y[X[È[™˜XÚÙ[™ÛÛ›™XÝÜˆ[\[Y[][Û‹ˆ‹ˆÛÛ™šYÈH›Ú™XÝ[ÙLÎPÙ\YžPÛÛ™šYÔÛ˜\ÚÝ

KˆÙ[™\˜]Y]HÞ\Ý[K‘]U[YSÙ™œÙ]•]Ó›ÝÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KØÙ\YžKÜÞ[˜ËÜ™]šY]È‹

HOˆ™]ÂžÂˆÝ]\ÈHœXÙZÛ\—Ü™]šY]×Ü™XYH‹ˆÛÛ›™XÝÜˆHÙ\YžH‹ˆ[ÙHH™žWÜ[ˆ‹ˆØ[”[“]™TÞ[˜ÈH˜[ÙKˆÝYÙY^[œÙPÛÝ[H›Ú™XÝ[ÙLÎTXÙZÛ\‘^[œÙ\Ë“[™Ýˆ^Ù\[ÛÛÝ[H›Ú™XÝ[ÙLÎTXÙZÛ\‘^Ù\[ÛœË“[™ÝˆÝYÙY^[œÙ\ÈH›Ú™XÝ[ÙLÎTXÙZÛ\‘^[œÙ\Ëˆ^Ù\[ÛœÈH›Ú™XÝ[ÙLÎTXÙZÛ\‘^Ù\[ÛœËˆY\ÜØYÙHH”™]šY]ÈXÙZÛ\ˆÙ[™\˜]Yˆ™X[™]šY]ÈÚ[Ø[Ù\YžKÝYÙH\›Ý™Y^[œÙ\Ë[™›YÈX\[™È^Ù\[ÛœËˆ‹ˆÙ[™\˜]Y]HÞ\Ý[K‘]U[YSÙ™œÙ]•]Ó›ÝÂŸJNÂ‚˜\“X\ÜÝ
‹Ø\KØÙ\YžKÜÞ[˜ËÜ[ˆ‹

HOˆ™]ÂžÂˆÝ]\ÈHœXÙZÛ\—Ü[—Ø›ØÚÙY‹ˆÛÛ›™XÝÜˆHÙ\YžH‹ˆ[ÙHH™žWÜ[—ÛÛ›H‹ˆØ[”[“]™TÞ[˜ÈH˜[ÙKˆÝYÙY^[œÙPÛÝ[H›Ú™XÝ[ÙLÎTXÙZÛ\‘^[œÙ\Ë“[™Ýˆ^Ù\[ÛÛÝ[H›Ú™XÝ[ÙLÎTXÙZÛ\‘^Ù\[ÛœË“[™ÝˆÝYÙY^[œÙ\ÈH›Ú™XÝ[ÙLÎTXÙZÛ\‘^[œÙ\Ëˆ^Ù\[ÛœÈH›Ú™XÝ[ÙLÎTXÙZÛ\‘^Ù\[ÛœËˆY\ÜØYÙHH“]™HÙ\YžHÞ[˜È\È[[[Û˜[H›ØÚÙYˆY™X[Ü™Y[X[ËÝYÚ[™ÈX›\Ë[™ÛÛ›™XÝÜˆÙÚXÈ™Y›Ü™H[˜X›[™È[\ÜËˆ‹ˆÙ[™\˜]Y]HÞ\Ý[K‘]U[YSÙ™œÙ]•]Ó›ÝÂŸJNÂ‹ÊˆÎWÐÑT•Q–WÔPÑRÓT—ÑS‘ÒS•×ÑS‘
‹Â‚‚‚‚‚‚‚‚‚‚‹ÊˆSWÐÓÔÑSÕUÑSPRSÐUQUÒST”×ÔÕT•
‹ÂœÝ]XÈÝš[™È›Ú™XÝ[ÙLSRœÛÛ”Ýš[™ÊÞ\Ý[K•^’œÛÛ‹’œÛÛ‘[[Y[›ÛÝÝš[™È›Ü\S˜[YJBžÂˆYˆ
›ÛÝ•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“Øš™XÝ
H™]\›ˆÝš[™Ë‘[\NÂˆYˆ
\›ÛÝ•žQÙ]›Ü\J›Ü\S˜[YKÝ]˜\ˆ˜[YJJH™]\›ˆÝš[™Ë‘[\NÂ‚ˆYˆ
˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™”Ýš[™ÊBˆÂˆ™]\›ˆ˜[YK‘Ù]Ýš[™Ê
HÏÈÝš[™Ë‘[\NÂˆB‚ˆYˆ
˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“[˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™•[™Yš[™Y
BˆÂˆ™]\›ˆÝš[™Ë‘[\NÂˆB‚ˆ™]\›ˆ˜[YK•ÔÝš[™Ê
HÏÈÝš[™Ë‘[\NÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLSRœÛÛ›ÛÛ
Þ\Ý[K•^’œÛÛ‹’œÛÛ‘[[Y[›ÛÝÝš[™È›Ü\S˜[YJBžÂˆYˆ
›ÛÝ•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“Øš™XÝ
H™]\›ˆ˜[ÙNÂˆYˆ
\›ÛÝ•žQÙ]›Ü\J›Ü\S˜[YKÝ]˜\ˆ˜[YJJH™]\›ˆ˜[ÙNÂ‚ˆYˆ
˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™•YJH™]\›ˆYNÂˆYˆ
˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™‘˜[ÙJH™]\›ˆ˜[ÙNÂ‚ˆYˆ
˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™”Ýš[™Âˆ	‰ˆ›ÛÛ•žT\œÙJ˜[YK‘Ù]Ýš[™Ê
KÝ]˜\ˆ\œÙY
JBˆÂˆ™]\›ˆ\œÙYÂˆB‚ˆ™]\›ˆ˜[ÙNÂŸB‚œÝ]XÈ[›Ú™XÝ[ÙLSRœÛÛ’[
Þ\Ý[K•^’œÛÛ‹’œÛÛ‘[[Y[›ÛÝÝš[™È›Ü\S˜[YJBžÂˆYˆ
›ÛÝ•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“Øš™XÝ
H™]\›ˆÂˆYˆ
\›ÛÝ•žQÙ]›Ü\J›Ü\S˜[YKÝ]˜\ˆ˜[YJJH™]\›ˆÂ‚ˆYˆ
˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“[X™\‚ˆ	‰ˆ˜[YK•žQÙ][ÌŠÝ]˜\ˆ\œÙY[X™\ŠJBˆÂˆ™]\›ˆ\œÙY[X™\ŽÂˆB‚ˆYˆ
˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™”Ýš[™Âˆ	‰ˆ[•žT\œÙJ˜[YK‘Ù]Ýš[™Ê
KÝ]˜\ˆ\œÙYÝš[™ÊJBˆÂˆ™]\›ˆ\œÙYÝš[™ÎÂˆB‚ˆ™]\›ˆÂŸB‚œÝ]XÈÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\ÝØš™XÝˆ›Ú™XÝ[ÙLSRœÛÛ”™XÚ\Y[ÊÞ\Ý[K•^’œÛÛ‹’œÛÛ‘[[Y[›ÛÝÝš[™È›Ü\S˜[YJBžÂˆ˜\ˆ™XÚ\Y[ÈH™]ÈÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\ÝØš™XÝŠ
NÂ‚ˆYˆ
›ÛÝ•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“Øš™XÝ
H™]\›ˆ™XÚ\Y[ÎÂˆYˆ
\›ÛÝ•žQÙ]›Ü\J›Ü\S˜[YKÝ]˜\ˆ\œ˜^Q[[Y[
JH™]\›ˆ™XÚ\Y[ÎÂˆYˆ
\œ˜^Q[[Y[•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™\œ˜^JH™]\›ˆ™XÚ\Y[ÎÂ‚ˆ›Ü™XXÚ
˜\ˆ][H[ˆ\œ˜^Q[[Y[‘[[Y\˜]P\œ˜^J
JBˆÂˆYˆ
][K•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“Øš™XÝ
HÛÛ[YNÂ‚ˆ™XÚ\Y[ËY
™]ÂˆÂˆ›ÛHH›Ú™XÝ[ÙLSRœÛÛ”Ýš[™Ê][K”›ÛHŠH\ÈÈ[™ÝˆˆH›ÛU\\‚ˆÈ›ÛU\\‚ˆˆ›Ú™XÝ[ÙLSRœÛÛ”Ýš[™Ê][Kœ›ÛHŠKˆ˜[YHH›Ú™XÝ[ÙLSRœÛÛ”Ýš[™Ê][K“˜[YHŠH\ÈÈ[™ÝˆˆH˜[YU\\‚ˆÈ˜[YU\\‚ˆˆ›Ú™XÝ[ÙLSRœÛÛ”Ýš[™Ê][K›˜[YHŠKˆ[XZ[H›Ú™XÝ[ÙLSRœÛÛ”Ýš[™Ê][K‘[XZ[ŠH\ÈÈ[™ÝˆˆH[XZ[\\‚ˆÈ[XZ[\\‚ˆˆ›Ú™XÝ[ÙLSRœÛÛ”Ýš[™Ê][K™[XZ[ŠBˆJNÂˆB‚ˆ™]\›ˆ™XÚ\Y[ÎÂŸB‹ÊˆSWÐÓÔÑSÕUÑSPRSÐUQUÒST”×ÑS‘
‹Â‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLPQÙ]œÛÛ”Ýš[™ÊÞ\Ý[K•^’œÛÛ‹’œÛÛ‘[[Y[›ÛÝÝš[™È›Ü\S˜[YJBžÂˆYˆ
›ÛÝ•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“Øš™XÝ
H™]\›ˆÝš[™Ë‘[\NÂ‚ˆYˆ
\›ÛÝ•žQÙ]›Ü\J›Ü\S˜[YKÝ]˜\ˆ˜[YJJH™]\›ˆÝš[™Ë‘[\NÂ‚ˆYˆ
˜[YK•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™”Ýš[™ÊBˆÂˆ™]\›ˆ˜[YK‘Ù]Ýš[™Ê
HÏÈÝš[™Ë‘[\NÂˆB‚ˆ™]\›ˆ˜[YK•ÔÝš[™Ê
HÏÈÝš[™Ë‘[\NÂŸB‚œÝ]XÈÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
Oˆ›Ú™XÝ[ÙLPQ^˜XÝ™XÚ\Y[ÊÞ\Ý[K•^’œÛÛ‹’œÛÛ‘[[Y[›ÛÝ
BžÂˆ™]\›ˆ›Ú™XÝ[ÙLPQ^˜XÝ™XÚ\Y[\œ˜^J›ÛÝœ™XÚ\Y[È‹”›Ú™XÝX[HŠNÂŸB‚œÝ]XÈÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
Oˆ›Ú™XÝ[ÙLPQ^˜XÝØÔ™XÚ\Y[ÊÞ\Ý[K•^’œÛÛ‹’œÛÛ‘[[Y[›ÛÝ
BžÂˆ™]\›ˆ›Ú™XÝ[ÙLPQ^˜XÝ™XÚ\Y[\œ˜^J›ÛÝ˜ØÔ™XÚ\Y[È‹ÐÈŠNÂŸB‚œÝ]XÈÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
Oˆ›Ú™XÝ[ÙLPQ^˜XÝ™XÚ\Y[\œ˜^JÞ\Ý[K•^’œÛÛ‹’œÛÛ‘[[Y[›ÛÝÝš[™È›Ü\S˜[YKÝš[™ÈY˜][›ÛJBžÂˆ˜\ˆ™XÚ\Y[ÈH™]ÈÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
OŠ
NÂ‚ˆYˆ
›ÛÝ•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“Øš™XÝ
H™]\›ˆ™XÚ\Y[ÎÂˆYˆ
\›ÛÝ•žQÙ]›Ü\J›Ü\S˜[YKÝ]˜\ˆ™XÚ\Y[Ñ[[Y[
JH™]\›ˆ™XÚ\Y[ÎÂˆYˆ
™XÚ\Y[Ñ[[Y[•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™\œ˜^JH™]\›ˆ™XÚ\Y[ÎÂ‚ˆ›Ü™XXÚ
˜\ˆ][H[ˆ™XÚ\Y[Ñ[[Y[‘[[Y\˜]P\œ˜^J
JBˆÂˆYˆ
][K•˜[YRÚ[™OHÞ\Ý[K•^’œÛÛ‹’œÛÛ•˜[YRÚ[™“Øš™XÝ
HÛÛ[YNÂ‚ˆ˜\ˆ›ÛHH›Ú™XÝ[ÙLPQÙ]œÛÛ”Ýš[™Ê][Kœ›ÛHŠNÂˆ˜\ˆ˜[YHH›Ú™XÝ[ÙLPQÙ]œÛÛ”Ýš[™Ê][K›˜[YHŠNÂˆ˜\ˆ[XZ[H›Ú™XÝ[ÙLPQÙ]œÛÛ”Ýš[™Ê][K™[XZ[ŠNÂ‚ˆ™XÚ\Y[ËY

ˆ›ÛNˆÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ›ÛJHÈY˜][›ÛHˆ›ÛKˆ˜[YNˆÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜[YJHÈ[XZ[ˆ˜[YKˆ[XZ[ˆ[XZ[
JNÂˆB‚ˆ™]\›ˆ™XÚ\Y[ÎÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLPR\Ñ[XZ[
Ýš[™ÏÈ˜[YJBžÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜[YJJH™]\›ˆ˜[ÙNÂ‚ˆ™]\›ˆÞ\Ý[K•^”™YÝ[\‘^™\ÜÚ[ÛœË”™YÙ^’\ÓX]Ú
ˆ˜[YK•š[J
Kˆ—–×—ÐJÐ×—ÐJ×–×—ÐJÉ‹ˆÞ\Ý[K•^”™YÝ[\‘^™\ÜÚ[ÛœË”™YÙ^Ü[ÛœËÝ[\™R[˜\šX[
NÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLPTØY™Qš[T\
Ýš[™È˜[YJBžÂˆ˜\ˆ›Ü›X[^™YHÞ\Ý[K•^”™YÝ[\‘^™\ÜÚ[ÛœË”™YÙ^”™\XÙJ˜[YHÏÈÝš[™Ë‘[\K–×KV˜K^ŒNWË‹WJÈ‹‹HŠK•š[J	ËIÊNÂˆ™]\›ˆÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ›Ü›X[^™Y
HÈœ›Ú™XÝˆˆ›Ü›X[^™YÂŸB‚‚‹ÊˆP—Ð”‘U“×ÐTWÒST”×ÔÕT•
‹ÂœÝ]XÈ\Þ[˜ÈÞ\Ý[K•™XY[™Ë•\ÚÜË•\ÚÏ
›ÛÛÙ[Ýš[™ÈÝ]\ËÝš[™È]Z[Ýš[™ÏÈÝ]›Þ]
Oˆ›Ú™XÝ[ÙLP”Ù[™œ™]›Ð\Q[XZ[\Þ[˜ÊˆÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
Oˆ™XÚ\Y[ËˆÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
OˆØÔ™XÚ\Y[ËˆÝš[™ÈÝXš™XÝˆÝš[™È›ÙKˆÝš[™È›Ú™XÝÛÙKˆÝš[™ÈÝ\ÝÛY\“˜[YKˆÝš[™Èœ™]›Ð\RÙ^JBžÂˆ˜\ˆ\U\›HÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐ”‘U“×ÐTWÕT“ŠNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ\U\›
JBˆÂˆ\U\›HšÎ‹ËØ\K˜œ™]›Ë˜ÛÛKÝŒËÜÛ]Ù[XZ[ŽÂˆB‚ˆ˜\ˆÙ[™\‘[XZ[HÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐ”‘U“×ÔÑS‘T—ÑSPRSŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÓUÑ”“ÓHŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÓUÑ”“ÓHŠBˆÏÈœ›Ú™XÝZX[Y\Ú›Ø\™ØØ[ÜÝŽÂ‚ˆ˜\ˆÙ[™\“˜[YHHÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐ”‘U“×ÔÑS‘T—ÓSQHŠBˆÏÈ”[ÙHŽÂ‚ˆÊˆSÐ”‘U“×ÓÓRUÑSTWÐÐ×ÔÕT•
‹Âˆ˜\ˆœ™]›ÕÔ™XÚ\Y[ÈH™]ÈÞ\Ý[K•^’œÛÛ‹“›Ù\Ë’œÛÛ\œ˜^J
NÂ‚ˆ›Ü™XXÚ
˜\ˆ™XÚ\Y[[ˆ™XÚ\Y[Ë•Ú\™J™XÚ\Y[Oˆ›Ú™XÝ[ÙLPR\Ñ[XZ[
™XÚ\Y[‘[XZ[
JJBˆÂˆœ™]›ÕÔ™XÚ\Y[ËY
™]ÈÞ\Ý[K•^’œÛÛ‹“›Ù\Ë’œÛÛ“Øš™XÝˆÂˆÈ™[XZ[—HH™XÚ\Y[‘[XZ[ˆÈ›˜[YH—HHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[“˜[YJHÈ™XÚ\Y[‘[XZ[ˆ™XÚ\Y[“˜[YBˆJNÂˆB‚ˆ˜\ˆ^[ØYH™]ÈÞ\Ý[K•^’œÛÛ‹“›Ù\Ë’œÛÛ“Øš™XÝˆÂˆÈœÙ[™\ˆ—HH™]ÈÞ\Ý[K•^’œÛÛ‹“›Ù\Ë’œÛÛ“Øš™XÝˆÂˆÈ™[XZ[—HHÙ[™\‘[XZ[ˆÈ›˜[YH—HHÙ[™\“˜[YBˆKˆÈÈ—HHœ™]›ÕÔ™XÚ\Y[ËˆÈœÝXš™XÝ—HHÝXš™XÝˆÈ^ÛÛ[—HH›ÙBˆNÂ‚ˆ˜\ˆœ™]›ÐØÔ™XÚ\Y[ÈH™]ÈÞ\Ý[K•^’œÛÛ‹“›Ù\Ë’œÛÛ\œ˜^J
NÂ‚ˆ›Ü™XXÚ
˜\ˆ™XÚ\Y[[ˆØÔ™XÚ\Y[Ë•Ú\™J™XÚ\Y[Oˆ›Ú™XÝ[ÙLPR\Ñ[XZ[
™XÚ\Y[‘[XZ[
JJBˆÂˆœ™]›ÐØÔ™XÚ\Y[ËY
™]ÈÞ\Ý[K•^’œÛÛ‹“›Ù\Ë’œÛÛ“Øš™XÝˆÂˆÈ™[XZ[—HH™XÚ\Y[‘[XZ[ˆÈ›˜[YH—HHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[“˜[YJHÈ™XÚ\Y[‘[XZ[ˆ™XÚ\Y[“˜[YBˆJNÂˆB‚ˆYˆ
œ™]›ÐØÔ™XÚ\Y[ËÛÝ[ˆ
BˆÂˆ^[ØYÈ˜ØÈ—HHœ™]›ÐØÔ™XÚ\Y[ÎÂˆBˆÊˆSÐ”‘U“×ÓÓRUÑSTWÐÐ×ÑS‘
‹Â‚ˆYˆ
œ™]›ÕÔ™XÚ\Y[ËÛÝ[OH
BˆÂˆ™]\›ˆ
˜[ÙK››×Ü™XÚ\Y[È‹“›È[XZ[\™XYH™XÚ\Y[ÈÙ\™H]˜Z[X›H›Üˆœ™]›ÈTH[]™\žKˆ‹[
NÂˆB‚ˆ˜\ˆœÛÛˆHÞ\Ý[K•^’œÛÛ‹’œÛÛ”Ù\šX[^™\‹”Ù\šX[^™J^[ØY
NÂ‚ˆ\Ú[™È˜\ˆÛY[H™]ÈÞ\Ý[K“™]’’ÛY[

NÂˆ\Ú[™È˜\ˆ™\]Y\ÝH™]ÈÞ\Ý[K“™]’’™\]Y\ÝY\ÜØYÙJÞ\Ý[K“™]’’Y]Ù”ÜÝ\U\›
NÂ‚ˆ™\]Y\Ý’XY\œË•žPYÚ]Ý]˜[Y][ÛŠ˜\KZÙ^H‹œ™]›Ð\RÙ^JNÂˆ™\]Y\Ý’XY\œËXØÙ\Y
™]ÈÞ\Ý[K“™]’’XY\œË“YYXU\UÚ]]X[]RXY\•˜[YJ˜\XØ][Û‹ÚœÛÛˆŠJNÂˆ™\]Y\ÝÛÛ[H™]ÈÞ\Ý[K“™]’”Ýš[™ÐÛÛ[
œÛÛ‹Þ\Ý[K•^‘[˜ÛÙ[™Ë•UŽ˜\XØ][Û‹ÚœÛÛˆŠNÂ‚ˆžBˆÂˆ\Ú[™È˜\ˆ™\ÜÛœÙHH]ØZ]ÛY[”Ù[™\Þ[˜Ê™\]Y\Ý
NÂˆ˜\ˆ™\ÜÛœÙU^H]ØZ]™\ÜÛœÙKÛÛ[”™XY\ÔÝš[™Ð\Þ[˜Ê
NÂ‚ˆYˆ
™\ÜÛœÙK’\ÔÝXØÙ\ÜÔÝ]\ÐÛÙJBˆÂˆ™]\›ˆ
YKœÙ[Øœ™]›×Ø\H‹	]]ÛX]XÈÛÜÙ[Ý][XZ[Ù[›ÝYÚœ™]›ÈTKˆ™\ÜÛœÙNˆÜ™\ÜÛœÙU^H‹[
NÂˆB‚ˆ˜\ˆ˜[˜XÚÈH]ØZ]›Ú™XÝ[ÙLPUÜš]SÝ]›Þ[XZ[\Þ[˜Ê™XÚ\Y[ËØÔ™XÚ\Y[ËÝXš™XÝ›ÙK›Ú™XÝÛÙK˜œ™]›ËX\KY˜Z[YŠNÂˆ™]\›ˆ
˜[ÙKœ]Y]YYØœ™]›×Ø\WÙ˜Z[Y‹	œ™]›ÈTH™]\›™YÊ[
\™\ÜÛœÙK”Ý]\ÐÛÙ_NˆÜ™\ÜÛœÙU^H‹˜[˜XÚÊNÂˆBˆØ]Ú
Þ\Ý[K‘^Ù\[Ûˆ^
BˆÂˆ˜\ˆ˜[˜XÚÈH]ØZ]›Ú™XÝ[ÙLPUÜš]SÝ]›Þ[XZ[\Þ[˜Ê™XÚ\Y[ËØÔ™XÚ\Y[ËÝXš™XÝ›ÙK›Ú™XÝÛÙK˜œ™]›ËX\KY^Ù\[ÛˆŠNÂˆ™]\›ˆ
˜[ÙKœ]Y]YYØœ™]›×Ø\WÙ^Ù\[Ûˆ‹	œ™]›ÈTH[]™\žH˜Z[Y[™HY\ÜØYÙHØ\ÈÜš][ˆÈÝ]›ÞˆÙ^“Y\ÜØYÙ_H‹˜[˜XÚÊNÂˆBŸB‹ÊˆP—Ð”‘U“×ÐTWÒST”×ÑS‘
‹Â‚œÝ]XÈ\Þ[˜ÈÞ\Ý[K•™XY[™Ë•\ÚÜË•\ÚÏ
›ÛÛÙ[Ýš[™ÈÝ]\ËÝš[™È]Z[Ýš[™ÏÈÝ]›Þ]
Oˆ›Ú™XÝ[ÙLPTÙ[™ÛÜÙ[Ý][XZ[\Þ[˜ÊˆÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
Oˆ™XÚ\Y[ËˆÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
OˆØÔ™XÚ\Y[ËˆÝš[™ÈÝXš™XÝˆÝš[™È›ÙKˆÝš[™È›Ú™XÝÛÙKˆÝš[™ÈÝ\ÝÛY\“˜[YJBžÂˆÊˆP—Ð”‘U“×ÐTWÑSPRSÑSU‘T–WÔÕT•
‹Âˆ˜\ˆœ™]›Ð\RÙ^HHÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÐ”‘U“×ÐTWÒÑVHŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”‘U“×ÐTWÒÑVHŠNÂ‚ˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJœ™]›Ð\RÙ^JJBˆÂˆ™]\›ˆ]ØZ]›Ú™XÝ[ÙLP”Ù[™œ™]›Ð\Q[XZ[\Þ[˜Ê™XÚ\Y[ËØÔ™XÚ\Y[ËÝXš™XÝ›ÙK›Ú™XÝÛÙKÝ\ÝÛY\“˜[YKœ™]›Ð\RÙ^JNÂˆBˆÊˆP—Ð”‘U“×ÐTWÑSPRSÑSU‘T–WÑS‘
‹Â‚ˆ˜\ˆÛ]ÜÝHÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÓUÒÔÕŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÓUÒÔÕŠNÂ‚ˆ˜\ˆÛ]œ›ÛHHÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÓUÑ”“ÓHŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÓUÑ”“ÓHŠBˆÏÈœ›Ú™XÝZX[Y\Ú›Ø\™ØØ[ÜÝŽÂ‚ˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÛ]ÜÝ
JBˆÂˆ˜\ˆÛ]Ü^HÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÓUÔÔ•ŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÓUÔÔ•ŠBˆÏÈŒHŽÂ‚ˆ˜\ˆÛ]ÜH[•žT\œÙJÛ]Ü^Ý]˜\ˆ\œÙYÜ
HÈ\œÙYÜˆNÂˆ˜\ˆÛ]\Ù\ˆHÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÓUÕTÑTˆŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÓUÕTÑTˆŠNÂˆ˜\ˆÛ]\ÜÝÛÜ™HÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÓUÔTÔÕÓÔ‘ŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÓUÔTÔÕÓÔ‘ŠNÂˆ˜\ˆÛ]ÜÛ^HÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÓUÔÔÓŠBˆÏÈÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÓUÔÔÓŠBˆÏÈ™˜[ÙHŽÂ‚ˆ\Ú[™È˜\ˆY\ÜØYÙHH™]ÈÞ\Ý[K“™]“XZ[“XZ[Y\ÜØYÙBˆÂˆœ›ÛHH™]ÈÞ\Ý[K“™]“XZ[“XZ[Y™\ÜÊÛ]œ›ÛJKˆÝXš™XÝHÝXš™XÝˆ›ÙHH›ÙKˆ\Ð›ÙR[H˜[ÙBˆNÂ‚ˆ›Ü™XXÚ
˜\ˆ™XÚ\Y[[ˆ™XÚ\Y[ÊBˆÂˆY\ÜØYÙK•ËY
™]ÈÞ\Ý[K“™]“XZ[“XZ[Y™\ÜÊ™XÚ\Y[‘[XZ[Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[“˜[YJHÈ™XÚ\Y[‘[XZ[ˆ™XÚ\Y[“˜[YJJNÂˆB‚ˆ›Ü™XXÚ
˜\ˆ™XÚ\Y[[ˆØÔ™XÚ\Y[ÊBˆÂˆY\ÜØYÙKÐËY
™]ÈÞ\Ý[K“™]“XZ[“XZ[Y™\ÜÊ™XÚ\Y[‘[XZ[Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XÚ\Y[“˜[YJHÈ™XÚ\Y[‘[XZ[ˆ™XÚ\Y[“˜[YJJNÂˆB‚ˆ\Ú[™È˜\ˆÛ]H™]ÈÞ\Ý[K“™]“XZ[”Û]ÛY[
Û]ÜÝÛ]Ü
BˆÂˆ[˜X›TÜÛHÝš[™Ë‘\]X[ÊÛ]ÜÛ^YH‹Þ\Ý[K”Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÛ]ÜÛ^ŒH‹Þ\Ý[K”Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÛ]ÜÛ^žY\È‹Þ\Ý[K”Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆNÂ‚ˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÛ]\Ù\ŠJBˆÂˆÛ]Ü™Y[X[ÈH™]ÈÞ\Ý[K“™]“™]ÛÜšÐÜ™Y[X[
Û]\Ù\‹Û]\ÜÝÛÜ™ÏÈÝš[™Ë‘[\JNÂˆB‚ˆžBˆÂˆ]ØZ]Û]”Ù[™XZ[\Þ[˜ÊY\ÜØYÙJNÂˆ™]\›ˆ
YKœÙ[‹]]ÛX]XÈÛÜÙ[Ý][XZ[Ù[›ÝYÚÛÛ™šYÝ\™YÓUˆ‹[
NÂˆBˆØ]Ú
Þ\Ý[K‘^Ù\[Ûˆ^
BˆÂˆ˜\ˆ˜[˜XÚÈH]ØZ]›Ú™XÝ[ÙLPUÜš]SÝ]›Þ[XZ[\Þ[˜Ê™XÚ\Y[ËØÔ™XÚ\Y[ËÝXš™XÝ›ÙK›Ú™XÝÛÙKœÛ]Y˜Z[YŠNÂˆ™]\›ˆ
˜[ÙKœ]Y]YYÜÛ]Ù˜Z[Y‹	”ÓUÙ[™˜Z[Y[™HY\ÜØYÙHØ\ÈÜš][ˆÈÝ]›ÞˆÙ^“Y\ÜØYÙ_H‹˜[˜XÚÊNÂˆBˆB‚ˆ˜\ˆÙ[™XZ[]HÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÔÑS‘PRSÔUŠHÏÈ‹Ý\Ü‹ÜØš[‹ÜÙ[™XZ[ŽÂ‚ˆYˆ
Þ\Ý[K’SË‘š[K‘^\ÝÊÙ[™XZ[]
JBˆÂˆ˜\ˆ˜]Ñ[XZ[H›Ú™XÝ[ÙLPPZ[˜]Ñ[XZ[
™XÚ\Y[ËØÔ™XÚ\Y[ËÛ]œ›ÛKÝXš™XÝ›ÙJNÂ‚ˆžBˆÂˆ˜\ˆ›ØÙ\ÜÒ[™›ÈH™]ÈÞ\Ý[K‘XYÛ›ÜÝXÜË”›ØÙ\ÜÔÝ\[™›ÂˆÂˆš[S˜[YHHÙ[™XZ[]ˆ\™Ý[Y[ÈH‹][ÚH‹ˆ™Y\™XÝÝ[™\™[œ]HYKˆ™Y\™XÝÝ[™\™\œ›ÜˆHYKˆ\ÙTÚ[^XÝ]HH˜[ÙBˆNÂ‚ˆ\Ú[™È˜\ˆ›ØÙ\ÜÈHÞ\Ý[K‘XYÛ›ÜÝXÜË”›ØÙ\ÜË”Ý\
›ØÙ\ÜÒ[™›ÊNÂ‚ˆYˆ
›ØÙ\ÜÈ\È›Ý[
BˆÂˆ]ØZ]›ØÙ\ÜË”Ý[™\™[œ]•Üš]P\Þ[˜Ê˜]Ñ[XZ[
NÂˆ›ØÙ\ÜË”Ý[™\™[œ]ÛÜÙJ
NÂˆ˜\ˆ\œ›ÜˆH]ØZ]›ØÙ\ÜË”Ý[™\™\œ›Ü‹”™XYÑ[™\Þ[˜Ê
NÂˆ]ØZ]›ØÙ\ÜË•ØZ]›Ü‘^]\Þ[˜Ê
NÂ‚ˆYˆ
›ØÙ\ÜË‘^]ÛÙHOH
BˆÂˆ™]\›ˆ
YKœÙ[‹]]ÛX]XÈÛÜÙ[Ý][XZ[Ù[›ÝYÚØØ[Ù[™XZ[ˆ‹[
NÂˆB‚ˆ˜\ˆ˜[˜XÚÈH]ØZ]›Ú™XÝ[ÙLPUÜš]SÝ]›Þ[XZ[\Þ[˜Ê™XÚ\Y[ËØÔ™XÚ\Y[ËÝXš™XÝ›ÙK›Ú™XÝÛÙKœÙ[™XZ[Y˜Z[YŠNÂˆ™]\›ˆ
˜[ÙKœ]Y]YYÜÙ[™XZ[Ù˜Z[Y‹	œÙ[™XZ[^]YÚ]ÛÙHÜ›ØÙ\ÜË‘^]ÛÙ_NˆÙ\œ›ÜŸH‹˜[˜XÚÊNÂˆBˆBˆØ]Ú
Þ\Ý[K‘^Ù\[Ûˆ^
BˆÂˆ˜\ˆ˜[˜XÚÈH]ØZ]›Ú™XÝ[ÙLPUÜš]SÝ]›Þ[XZ[\Þ[˜Ê™XÚ\Y[ËØÔ™XÚ\Y[ËÝXš™XÝ›ÙK›Ú™XÝÛÙKœÙ[™XZ[Y^Ù\[ÛˆŠNÂˆ™]\›ˆ
˜[ÙKœ]Y]YYÜÙ[™XZ[Ù^Ù\[Ûˆ‹	œÙ[™XZ[˜Z[Y[™HY\ÜØYÙHØ\ÈÜš][ˆÈÝ]›ÞˆÙ^“Y\ÜØYÙ_H‹˜[˜XÚÊNÂˆBˆB‚ˆ˜\ˆÝ]›Þ]H]ØZ]›Ú™XÝ[ÙLPUÜš]SÝ]›Þ[XZ[\Þ[˜Ê™XÚ\Y[ËØÔ™XÚ\Y[ËÝXš™XÝ›ÙK›Ú™XÝÛÙK›Z\ÜÚ[™Ë[XZ[\ˆŠNÂˆ™]\›ˆ
˜[ÙKœ]Y]YYÛ›ÝÜÙ[ÛZ\ÜÚ[™×ÛXZ[\ˆ‹“›ÈÓUÜÝÜˆÙ[™XZ[š[˜\žH\ÈÛÛ™šYÝ\™YˆY\ÜØYÙHØ\ÈÜš][ˆÈHÛÜÙ[Ý][XZ[Ý]›Þˆ‹Ý]›Þ]
NÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLPTØ[š]^™RXY\•˜[YJÝš[™ÏÈ˜[YJBžÂˆ™]\›ˆÞ\Ý[K•^”™YÝ[\‘^™\ÜÚ[ÛœË”™YÙ^ˆ”™\XÙJ˜[YHÏÈÝš[™Ë‘[\K–×——JÈ‹ˆŠBˆ•š[J
NÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLPPZ[˜]Ñ[XZ[
ˆÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
Oˆ™XÚ\Y[ËˆÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
OˆØÔ™XÚ\Y[ËˆÝš[™Èœ›ÛKˆÝš[™ÈÝXš™XÝˆÝš[™È›ÙJBžÂˆ˜\ˆZ[\ˆH™]ÈÞ\Ý[K•^”Ýš[™ÐZ[\Š
NÂ‚ˆZ[\‹\[™[™J	‘œ›ÛNˆÔ›Ú™XÝ[ÙLPTØ[š]^™RXY\•˜[YJœ›ÛJ_HŠNÂˆZ[\‹\[™[™J	•ÎˆÜÝš[™Ë’›Ú[Š‹‹™XÚ\Y[Ë”Ù[XÝ
™XÚ\Y[Oˆ›Ú™XÝ[ÙLPTØ[š]^™RXY\•˜[YJ™XÚ\Y[‘[XZ[
JJ_HŠNÂ‚ˆYˆ
ØÔ™XÚ\Y[ËÛÝ[ˆ
BˆÂˆZ[\‹\[™[™J	ØÎˆÜÝš[™Ë’›Ú[Š‹‹ØÔ™XÚ\Y[Ë”Ù[XÝ
™XÚ\Y[Oˆ›Ú™XÝ[ÙLPTØ[š]^™RXY\•˜[YJ™XÚ\Y[‘[XZ[
JJ_HŠNÂˆB‚ˆZ[\‹\[™[™J	”ÝXš™XÝˆÔ›Ú™XÝ[ÙLPTØ[š]^™RXY\•˜[YJÝXš™XÝ
_HŠNÂˆZ[\‹\[™[™J“RSQKU™\œÚ[ÛŽˆKŒŠNÂˆZ[\‹\[™[™JÛÛ[U\Nˆ^ÜZ[ŽÈÚ\œÙ]]]‹NŠNÂˆZ[\‹\[™[™J
NÂˆZ[\‹\[™[™J›ÙJNÂ‚ˆ™]\›ˆZ[\‹•ÔÝš[™Ê
NÂŸB‚œÝ]XÈ\Þ[˜ÈÞ\Ý[K•™XY[™Ë•\ÚÜË•\ÚÏÝš[™Ïˆ›Ú™XÝ[ÙLPUÜš]SÝ]›Þ[XZ[\Þ[˜ÊˆÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
Oˆ™XÚ\Y[ËˆÞ\Ý[KÛÛXÝ[ÛœË‘Ù[™\šXË“\Ý
Ýš[™È›ÛKÝš[™È˜[YKÝš[™È[XZ[
OˆØÔ™XÚ\Y[ËˆÝš[™ÈÝXš™XÝˆÝš[™È›ÙKˆÝš[™È›Ú™XÝÛÙKˆÝš[™È™X\ÛÛŠBžÂˆ˜\ˆ]T›ÛÝHÞ\Ý[K‘[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”“Ò‘PÕSÑWÑUWÑTˆŠNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ]T›ÛÝ
JBˆÂˆ]T›ÛÝHÞ\Ý[K’SË”]ÛÛXš[™JÞ\Ý[K’SË‘\™XÝÜžK‘Ù]Ý\œ™[\™XÝÜžJ
K™]HŠNÂˆB‚ˆ˜\ˆÝ]›Þ\ˆHÞ\Ý[K’SË”]ÛÛXš[™J]T›ÛÝœ›Ú™XÝXÛÜÙ[Ý]Y[XZ[[Ý]›ÞŠNÂˆÞ\Ý[K’SË‘\™XÝÜžKÜ™X]Q\™XÝÜžJÝ]›Þ\ŠNÂ‚ˆ˜\ˆÝ]›Þ]HÞ\Ý[K’SË”]ÛÛXš[™JˆÝ]›Þ\‹ˆ	žÔÞ\Ý[K‘]U[YSÙ™œÙ]•]Ó›ÝÎž^^^SSY[\ÜÙ™™ŸK^Ô›Ú™XÝ[ÙLPTØY™Qš[T\
›Ú™XÝÛÙJ_K^Ô›Ú™XÝ[ÙLPTØY™Qš[T\
™X\ÛÛŠ_K™[[ŠNÂ‚ˆ]ØZ]Þ\Ý[K’SË‘š[K•Üš]P[^\Þ[˜ÊˆÝ]›Þ]ˆ›Ú™XÝ[ÙLPPZ[˜]Ñ[XZ[
™XÚ\Y[ËØÔ™XÚ\Y[Ëœ›Ú™XÝZX[Y\Ú›Ø\™ØØ[ÜÝ‹ÝXš™XÝ›ÙJJNÂ‚ˆ™]\›ˆÝ]›Þ]ÂŸB‚‹Êˆ—ÓU‘WÐ’SS‘×ÐTWÓPTÔÕT•
‹Â”›Ú™XÝ[YK\K“[Ù[\Ë’[›ÚXÙPš[[™Ó[Ù[K“X\[›ÚXÙPš[[™Ñ[™Ú[Ê\
NÂ˜\“X\ÛÜšÓY™XÞXÛQ[™Ú[Ê
NÂ‹Êˆ—ÓU‘WÐ’SS‘×ÐTWÓPTÑS‘
‹Â‚‹ÊˆÓÔ’×Ô‘QÒTÕT—Ô×ÑS‘ÒS•ÓPTÔÕT•
‹Â”›Ú™XÝ[YK\K“[Ù[\Ë•ÛÜšÔ™YÚ\Ý\”\˜Ú\ÙSÜ™\“[Ù[K“X\ÛÜšÔ™YÚ\Ý\”\˜Ú\ÙSÜ™\‘[™Ú[Ê\
NÂ‹ÊˆÓÔ’×Ô‘QÒTÕT—Ô×ÑS‘ÒS•ÓPTÑS‘
‹Â‚‹ÊˆMQÔÑSÒSTÔ•ÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\ÛÜšÔ™YÚ\Ý\”Ù[[\Ü[™Ú[Ê
NÂ‹ÊˆMQÔÑSÒSTÔ•ÑS‘ÒS•ÓPTÑS‘
‹Â‚”›Ú™XÝ[YK\K“[Ù[\Ë’Y[]T›Ùš[S[Ù[K“X\Y[]T›Ùš[Q[™Ú[Ê\
NÂ‚‹ÊˆSÑSWÌ—ÐÔ“WÑT”ÒS•QÔUSÓ—ÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\Ü›Q\œ[YÜ˜][Û‘[™Ú[Ê
NÂ‹ÊˆSÑSWÌ—ÐÔ“WÑT”ÒS•QÔUSÓ—ÑS‘ÒS•ÓPTÑS‘
‹Â‚‹ÊˆSÑSWÌÔÒT‘QÐRWÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\ZT›ÝšY\ÛÛ™šYÝ\˜][Û‘[™Ú[Ê
NÂ‹ÊˆSÑSWÌÔÒT‘QÐRWÑS‘ÒS•ÓPTÑS‘
‹Â‚‹ÊˆSÑSWÌWÑS•WÔÑPÔ‘UÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\[˜TÙXÜ™]YZ[š\Ý˜][Û‘[™Ú[Ê
NÂ‹ÊˆSÑSWÌWÑS•WÔÑPÔ‘UÑS‘ÒS•ÓPTÑS‘
‹Â‚˜\“X\›Ú™XÝ›Ü™ÙQ[™Ú[Ê
NÂ‚‹ÊˆSÑSWÌLWÔ“Ò‘PÕÑ“ÕÒU‘WÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\›Ú™XÝ›ÝÒ]™Q[™Ú[Ê
NÂ‹ÊˆSÑSWÌLWÔ“Ò‘PÕÑ“ÕÒU‘WÑS‘ÒS•ÓPTÑS‘
‹Â‚‹ÊˆSÑST×Ì×ÌÍÔ‘SPTÑWÕRS—ÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\ÛØ˜[XZ[ÛÛ™šYÝ\˜][Û‘[™Ú[Ê
NÂ˜\“X\Þ\Ý[P\˜Ú]XÝ\™Q[™Ú[Ê
NÂ˜\“X\]X[YšXØ][ÛœÐÙ\YšXØ][Û‘[™Ú[Ê
NÂ˜\“X\Ø\XÚ]T\[[™Q›Ü™XØ\Ý[™Ú[Ê
NÂ˜\“X\ÛØ[ØÚY[[™Ñ[™Ú[Ê
NÂ˜\“X\Û™P\ÜÚ\Ý›Ý][™Ñ\™XÝÜžQ[™Ú[Ê
NÂ˜\“X\Ø[\ÐÛÝ™\˜YÙP[YÛ›Y[[™Ú[Ê
NÂ˜\“X\Ù[U™[™Ü‘\™XÝÜžQ[™Ú[Ê
NÂ˜\“X\[Ù[LÍ˜]]™PYZ[š\Ý˜][Û‘[™Ú[Ê
NÂ‹ÊˆSÑST×Ì×ÌÍÔ‘SPTÑWÕRS—ÑS‘ÒS•ÓPTÑS‘
‹Â‚‹ÊˆSÑSWÌÍ—ÑQ‘PÕÕPÒÑT—ÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\Y™XÝ˜XÚÙ\‘[™Ú[Ê
NÂ‹ÊˆSÑSWÌÍ—ÑQ‘PÕÕPÒÑT—ÑS‘ÒS•ÓPTÑS‘
‹Â‚‹ÊˆSÑST×ÌÍWÌÔ•S•SQWÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\[YÜ˜][Û‘]™[Ø]]Ø^Q[™Ú[Ê
NÂ˜\“X\™[X\ÙQ\Þ[Y[ÛÛ›Û[™Ú[Ê
NÂ˜\“X\ØœÙ\˜Xš[]TÛÒX[[™Ú[Ê
NÂ˜\“X\]QÛÝ™\›˜[˜ÙT™][[Û‘[™Ú[Ê
NÂ˜\“X\Ý\ÝÛY\‘[]™\žPXØÙ\[˜ÙQ[™Ú[Ê
NÂ‹ÊˆSÑST×ÌÍWÌÔ•S•SQWÑS‘ÒS•ÓPTÑS‘
‹Â‚‹ÊˆSÑSWÎNNÔÖTÕSWÑPQÓ“ÔÕP×ÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\Þ\Ý[QXYÛ›ÜÝXÔ™[YYX][Û‘[™Ú[Ê
NÂ‹ÊˆSÑSWÎNNÔÖTÕSWÑPQÓ“ÔÕP×ÑS‘ÒS•ÓPTÑS‘
‹Â‚”›Ú™XÝ[YK\K“[Ù[\ËØ[[™\Ø\XÚ]S[Ù[K“X\Ø[[™\Ø\XÚ]Q[™Ú[Ê\
NÂ‚‹ÊˆSÑSWÎNM×ÔÑPÕT’UWÓÔTUSÓ”×ÑS‘ÒS•ÓPTÔÕT•
‹Â˜\“X\ÙXÝ\š]SÜ\˜][ÛœÔ™\ÜÛœÙQ[™Ú[Ê
NÂ‹ÊˆSÑSWÎNM×ÔÑPÕT’UWÓÔTUSÓ”×ÑS‘ÒS•ÓPTÑS‘
‹Â‚”›Ú™XÝ[YK\K“[Ù[\ËÚPÙ\[[™S[Ù[K“X\ÚPÙ\[[™Q[™Ú[Ê\
NÂ‚˜\“X\Ù\[šXPš[[™Ñ[™Ú[Ê
NÂ˜\“X\Ù\ÜÚ[Û’[[YÙ[˜ÙQ[™Ú[Ê
NÂ˜\“X\Ù[[˜›Ý[™Û˜\ÚÝ[™Ú[Ê
NÂ˜\“X\Ù[ÛÛ[Y\˜ÚX[™XY[Ù[[™Ú[Ê
NÂ‚˜\“X\ÛÛ˜XÝÑ[™Ú[Ê
NÂ˜\“X\ÛÛ˜XÝÔ™\ZY[Ù[J
NÂ˜\“X\ÛÛ˜XÝÔ™\ZYX[˜YÙ[Y[[Ù[J
NÂ‚”›Ú™XÝ[YK\K“[Ù[\Ë“ÜÜ[š]Y\Ó[Ù[K“X\ÜÜ[š]Q[™Ú[Ê\
NÂ‚˜\”[Š
NÂ‚‚‚‚œÝ]XÈ›ÛÛØ[‘[™Ú[™Y\•[›ØÚÑ^JÝš[™ÏÈÝ]\Ë]U[YSÙ™œÙ]ÈÝX›Z]Y]
BžÂˆ™]\›ˆÝ]\ÈOHœÝX›Z]Y‚ˆ	‰ˆÝX›Z]Y]\È›Ý[ˆ	‰ˆ]U[YSÙ™œÙ]•]Ó›ÝÈHÝX›Z]Y]•˜[YHH[YTÜ[‹‘œ›ÛRÝ\œÊJNÂŸB‚œÝ]XÈÝš[™ÈÙ]^U[›ØÚÓY\ÜØYÙJÝš[™ÏÈÝ]\Ë]U[YSÙ™œÙ]ÈÝX›Z]Y]
BžÂˆYˆ
Ý]\È\È[Ý]\ÈOH™˜YŠH™]\›ˆ•\È^H\È›Ý™Y[ˆÝX›Z]YY]ˆŽÂˆYˆ
Ý]\ÈOH›X[˜YÙ\—ÙXÛ[™YŠH™]\›ˆ•\È^HØ\È™]\›™Y›ÜˆÛÜœ™XÝ[Ûˆ[™Ø[ˆ™HY]YÜ™\ÝX›Z]YˆŽÂˆYˆ
Ý]\ÈOHœÝX›Z]YŠBˆÂˆYˆ
ÝX›Z]Y]\È[
H™]\›ˆ•\ÈÝX›Z]Y^H\ÈZ\ÜÚ[™ÈHÝX›Z\ÜÚ[Ûˆ[Y\Ý[\ˆX\ÙHÛÛXÝ[Ý\ˆX[˜YÙ\ˆÈ[›ØÚÈ]ˆŽÂˆ™]\›ˆ]U[YSÙ™œÙ]•]Ó›ÝÈHÝX›Z]Y]•˜[YHH[YTÜ[‹‘œ›ÛRÝ\œÊJBˆÈ•\ÈÝX›Z]Y^HØ[ˆ™H[›ØÚÙYˆ‚ˆˆ•\È^HØ\ÈÝX›Z]Y[Ü™H[ˆÛ™HÝ\ˆYÛËˆX\ÙHÛÛXÝ[Ý\ˆX[˜YÙ\ˆÈ[›ØÚÈ]ˆŽÂˆBˆYˆ
Ý]\ÈOH›X[˜YÙ\—Ø\›Ý™YŠH™]\›ˆ•\È^H\È™Y[ˆX[˜YÙ\‹X\›Ý™Y[™\È™XY[Û›H›ÜˆH[™Ú[™Y\‹ˆŽÂˆYˆ
Ý]\ÈOHœWØ\›Ý™YŠH™]\›ˆ•\È^H\È™Y[ˆKX\›Ý™Y[™\È™XY[Û›H›ÜˆH[™Ú[™Y\‹ˆŽÂˆYˆ
Ý]\ÈOH˜XØÛÝ[[™×Ü™XYHŠH™]\›ˆ•\È^H\È™XYH›ÜˆXØÛÝ[[™È™]šY]È[™\È™XY[Û›H›ÜˆH[™Ú[™Y\‹ˆŽÂˆYˆ
Ý]\ÈOHœ™XÛÛ˜Ú[YŠH™]\›ˆ•\È^H\È™Y[ˆ™XÛÛ˜Ú[Y[™\ÈØÚÙYˆŽÂˆYˆ
Ý]\ÈOH›ØÚÙYŠH™]\›ˆ•\È^H\ÈØÚÙYˆŽÂ‚ˆ™]\›ˆ•\È^H\È›ÝY]X›H[ˆ]ÈÝ\œ™[ÛÜšÙ›ÝÈÝ]KˆŽÂŸB‚‚‚œÝ]XÈÝš[™È›Ú™XÝ[ÙPÜÝ‘šY[
Øš™XÝÈ˜[YJBžÂˆ˜\ˆ^H˜[YHÝÚ]ÚˆÂˆ[OˆÝš[™Ë‘[\Kˆ]SÛ›HOˆ•ÔÝš[™Êž^^^KSSKYŠKˆ]U[YSÙ™œÙ]ÈOˆË•ÔÝš[™Ê“ÈŠKˆXÚ[X[Oˆ•ÔÝš[™ÊŒˆÈÈ‹Þ\Ý[K‘ÛØ˜[^˜][Û‹Ý[\™R[™›Ë’[˜\šX[Ý[\™JKˆÝX›HOˆ•ÔÝš[™ÊŒˆÈÈ‹Þ\Ý[K‘ÛØ˜[^˜][Û‹Ý[\™R[™›Ë’[˜\šX[Ý[\™JKˆ›Ø]ˆOˆ‹•ÔÝš[™ÊŒˆÈÈ‹Þ\Ý[K‘ÛØ˜[^˜][Û‹Ý[\™R[™›Ë’[˜\šX[Ý[\™JKˆÈOˆ˜[YK•ÔÝš[™Ê
HÏÈÝš[™Ë‘[\BˆNÂ‚ˆ^H^”™\XÙJ—ˆ‹ˆŠK”™\XÙJ—ˆ‹ˆŠK”™\XÙJ—ˆ‹——ˆŠNÂ‚ˆÊˆÑPÕT’UWÌŒŒÌŽWÐÔÕ—Ñ“Ô“USWÓ‘UUSVUSÓˆ
‹Âˆ˜\ˆ›Ü›][PØ[™Y]HH^•š[TÝ\

NÂˆYˆ
›Ü›][PØ[™Y]K”Ý\ÕÚ]
H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
Bˆ›Ü›][PØ[™Y]K”Ý\ÕÚ]
ŠÈ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
Bˆ›Ü›][PØ[™Y]K”Ý\ÕÚ]
‹H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
Bˆ›Ü›][PØ[™Y]K”Ý\ÕÚ]
‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆÂˆ^H‰Èˆ
È^ÂˆB‚ˆ™]\›ˆ	—žÝ^WˆŽÂŸB‚‚œÝ]XÈT™XYÛ›S\ÝÝš[™Ïˆ˜[Y]Q^TÝX›Z]™\]Y\Ý
[Y\ÚY]^TÝX›Z]™\]Y\Ý™\]Y\Ý
BžÂˆ˜\ˆ\œ›ÜœÈH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆÙYZÔÝ\HÙ]Ý[™^Q›Ü‘]J™\]Y\Ý•ÙYZÔÝ\
NÂˆ˜\ˆÙYZÑ[™HÙYZÔÝ\Y^\ÊŠNÂ‚ˆYˆ
™\]Y\Ý•ÛÜšÑ]HÙYZÔÝ\™\]Y\Ý•ÛÜšÑ]HˆÙYZÑ[™
BˆÂˆ\œ›ÜœËY
	•ÛÜšÈ]HÜ™\]Y\Ý•ÛÜšÑ]_H\ÈÝ]ÚYHHÙ[XÝYÙYZÈÝÙYZÔÝ\H›ÝYÚÝÙYZÑ[™KˆŠNÂˆB‚ˆYˆ
™\]Y\Ý‘[šY\È\È[™\]Y\Ý‘[šY\ËÛÝ[OH
BˆÂˆ\œ›ÜœËY
]X\ÝÛ™H[YH[žH\È™\]Z\™Y›ÜˆHÙ[XÝY^KˆŠNÂˆ™]\›ˆ\œ›ÜœÎÂˆB‚ˆ˜\ˆZ[UÝ[H™\]Y\Ý‘[šY\Âˆ•Ú\™J[žHOˆ[žK•ÛÜšÑ]HOH™\]Y\Ý•ÛÜšÑ]JBˆ”Ý[J[žHOˆ[žK’Ý\œÊNÂ‚ˆYˆ
Z[UÝ[ŒJBˆÂˆ\œ›ÜœËY
	HZ[š[][HÙˆŒÝ\œÈ\È™\]Z\™Y™Y›Ü™HÝX›Z][™ÈÜ™\]Y\Ý•ÛÜšÑ]_KˆÝ\œ™[Ý[\ÈÙZ[UÝ[ŒŒHÝ\œËˆŠNÂˆB‚ˆ›Ü™XXÚ
˜\ˆ[žH[ˆ™\]Y\Ý‘[šY\ÊBˆÂˆYˆ
[žK•ÛÜšÑ]HOH™\]Y\Ý•ÛÜšÑ]JBˆÂˆ\œ›ÜœËY
	‘[žH]HÙ[žK•ÛÜšÑ]_HÙ\È›ÝX]ÚÙ[XÝYÝX›Z]]HÜ™\]Y\Ý•ÛÜšÑ]_KˆŠNÂˆB‚ˆYˆ
[žK•[YU\H\È›Ý
››Ü›X[ˆÜˆ˜Y\šÝ\œÈŠJBˆÂˆ\œ›ÜœËY
	’[˜[Y[YH\H	ÞÙ[žK•[YU\_IËˆ^XÝY›Ü›X[ÜˆY\šÝ\œËˆŠNÂˆB‚ˆYˆ
[žK’Ý\œÈ[žK’Ý\œÈˆ
BˆÂˆ\œ›ÜœËY
	’Ý\œÈ›ÜˆÙ[žK•ÛÜšÑ]_H]\Ý™H™]ÙY[ˆ[™ˆŠNÂˆB‚ˆYˆ
[žK’Ý\œÈˆ	‰ˆÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žKØ]YÛÜžPÛÙJH	‰ˆ
[žK”›Ú™XÝY\È[[žK•\ÚÒY\È[
JBˆÂˆ\œ›ÜœËY
	‘[žH›ÜˆÙ[žK•ÛÜšÑ]_H]\ÝY[YžHZ]\ˆH›Û‹\›Ú™XÝØ]YÛÜžHÜˆH›Ú™XÝ\ÚËˆŠNÂˆBˆB‚ˆ™]\›ˆ\œ›ÜœÎÂŸB‚‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆØY[Y\ÚY]™Y™\™[˜Ù\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È\Ù\—Ý[Y\ÚY]Ü™Y™\™[˜Ù\È
\Ù\—ÚY
BˆSQTÈ
\Ù\—ÚY
BˆÓˆÓÓ‘“PÕ
\Ù\—ÚY
HÈ“ÕS‘ÎÂ‚ˆÑSPÕY˜][Û›Û—Ü›Ú™XÝØØ]YÛÜžWØÛÙ\ËˆY˜][Ü›Ú™XÝÝ\Ú×ÚYËˆ]]×ØYÚÛY^\ËˆÙYZÛWÜ™[Z[™\—Ù[˜X›Yˆ™[Z[™\—Ù^WÛÙ—ÝÙYZËˆ™[Z[™\—ÛØØ[Ý[YKˆ[Y^›Û™WÛ˜[YBˆ”“ÓH\Ù\—Ý[Y\ÚY]Ü™Y™\™[˜Ù\ÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆ]ØZ]™XY\‹”™XY\Þ[˜Ê
NÂ‚ˆ™]\›ˆ™]ÂˆÂˆY˜][›Û”›Ú™XÝØ]YÛÜžPÛÙ\ÈH™XY\‹‘Ù]šY[˜[YOÝš[™Ö×OŠ
KˆY˜][›Ú™XÝ\ÚÒYÈH™XY\‹‘Ù]šY[˜[YOÝZY×OŠJKˆ]]ÐYÛY^\ÈH™XY\‹‘Ù]›ÛÛX[ŠŠKˆÙYZÛT™[Z[™\‘[˜X›YH™XY\‹‘Ù]›ÛÛX[ŠÊKˆ™[Z[™\‘^SÙ•ÙYZÈH™XY\‹‘Ù][ÌŠ
Kˆ™[Z[™\“ØØ[[YHH™XY\‹‘Ù]šY[˜[YO[YSÛ›OŠJK•ÔÝš[™Ê’›[HŠKˆ[Y^›Û™S˜[YHH™XY\‹‘Ù]Ýš[™ÊŠBˆNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏT™\Ý[ˆ]Y]YT™[Z[™\”[P\Þ[˜ÊÝš[™È[PÛÙJBžÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂˆ˜\ˆZ\ÜÚ[™Ô™\Ý[H˜[Y]PÛÛ™šYÊÛÛ™šYÊNÂˆYˆ
Z\ÜÚ[™Ô™\Ý[\È›Ý[
H™]\›ˆZ\ÜÚ[™Ô™\Ý[Â‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È[XZ[Û›ÝYšXØ][Û—ÛÝ]›Þ
[WØÛÙK™XÚ\Y[Ù[XZ[™XÚ\Y[Û˜[YKÝXš™XÝ›ÙKÝ]\ËØÚY[YÙ›ÜŠBˆÑSPÕˆœ‹œ[WØÛÙKˆK™[XZ[ˆK™\Ü^WÛ˜[YKˆœ‹œÝXš™XÝÝ[\]Kˆ‘TPÑJœ‹˜›ÙWÝ[\]K	ÞÞÙ\Ü^WÛ˜[Y__IËK™\Ü^WÛ˜[YJKˆ	Ü]Y]YY	Ëˆ“ÕÊ
Bˆ”“ÓH™[Z[™\—Ü[\Èœ‚ˆS“‘Tˆ“ÒSˆ›ÝYšXØ][Û—ÙÜ›Ý\È™ÈÓˆ™Ë™Ü›Ý\ØÛÙHHœ‹œ™XÚ\Y[ÙÜ›Ý\ØÛÙBˆS“‘Tˆ“ÒSˆ›ÝYšXØ][Û—ÙÜ›Ý\ÛY[X™\œÈ™ÛHÓˆ™ÛK››ÝYšXØ][Û—ÙÜ›Ý\ÚYH™Ë››ÝYšXØ][Û—ÙÜ›Ý\ÚYS‘™ÛKš\×ØXÝ]™HH•QBˆS“‘Tˆ“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYH™ÛK\Ù\—ÚYS‘Kš\×ØXÝ]™HH•QBˆÒT‘Hœ‹œ[WØÛÙHH[WØÛÙBˆS‘œ‹š\×ØXÝ]™HH•QBˆS‘™Ëš\×ØXÝ]™HH•QNÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ[WØÛÙH‹[PÛÙJNÂˆ˜\ˆ[œÙ\YH]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÈÈÝ]\ÈHœ]Y]YY‹[PÛÙK]Y]YYÛÝ[H[œÙ\YJNÂŸB‚‚œÝ]XÈ\ÝÝš[™Ïˆ\œÙTÚ[\PÜÝ“[™JÝš[™È[™JBžÂˆ˜\ˆ˜[Y\ÈH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆÝ\œ™[H™]ÈÞ\Ý[K•^”Ýš[™ÐZ[\Š
NÂˆ˜\ˆ[”][Ý\ÈH˜[ÙNÂ‚ˆ›Üˆ
˜\ˆHHÈH[™K“[™ÝÈJÊÊBˆÂˆ˜\ˆÚH[™VÚWNÂˆYˆ
ÚOH	È‰ÊBˆÂˆYˆ
[”][Ý\È	‰ˆH
ÈH[™K“[™Ý	‰ˆ[™VÚH
ÈWHOH	È‰ÊBˆÂˆÝ\œ™[\[™
	È‰ÊNÂˆJÊÎÂˆBˆ[ÙBˆÂˆ[”][Ý\ÈHZ[”][Ý\ÎÂˆBˆBˆ[ÙHYˆ
ÚOH	Ë	È	‰ˆZ[”][Ý\ÊBˆÂˆ˜[Y\ËY
Ý\œ™[•ÔÝš[™Ê
JNÂˆÝ\œ™[ÛX\Š
NÂˆBˆ[ÙBˆÂˆÝ\œ™[\[™
Ú
NÂˆBˆB‚ˆ˜[Y\ËY
Ý\œ™[•ÔÝš[™Ê
JNÂˆ™]\›ˆ˜[Y\ÎÂŸB‚œÝ]XÈ›ÛÛ\Õ]JÝš[™ÏÈ˜[YJBžÂˆ™]\›ˆ˜[YH\È›Ý[	‰ˆ™]Ö×HÈYH‹ŒH‹žY\È‹žHˆKÛÛZ[œÊ˜[YK•š[J
K•ÓÝÙ\’[˜\šX[

JNÂŸB‚‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆZ[ÙXÝ\š]PÛÛ^\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y
BžÂˆÝš[™ÏÈ[XZ[H[ÂˆÝš[™ÏÈ\Ü^S˜[YHH[Â‚ˆ]ØZ]\Ú[™È
˜\ˆ\Ù\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
”ÑSPÕ[XZ[\Ü^WÛ˜[YH”“ÓH\Ý\Ù\œÈÒT‘H\Ù\—ÚYH\Ù\—ÚYÈ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ\Ù\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]\Ù\ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ[XZ[H™XY\‹‘Ù]Ýš[™Ê
NÂˆ\Ü^S˜[YHH™XY\‹‘Ù]Ýš[™ÊJNÂˆBˆB‚ˆ˜\ˆ›Û\ÈH™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆ›ÛPÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ‹œ›ÛWØÛÙK‹œ›ÛWÛ˜[YK‹œ›ÛWÙ\ØÜš\[Û‚ˆ”“ÓH\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜BˆS“‘Tˆ“ÒSˆ\Ü›Û\ÈˆÓˆ‹˜\Ü›ÛWÚYH\˜K˜\Ü›ÛWÚYˆÒT‘H\˜K\Ù\—ÚYH\Ù\—ÚYˆS‘\˜Kš\×ØXÝ]™HH•QBˆS‘‹š\×ØXÝ]™HH•QBˆÔ‘Tˆ–H‹™\Ü^WÛÜ™\ŽÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ›ÛPÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]›ÛPÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ›Û\ËY
™]ÂˆÂˆ›ÛPÛÙHH™XY\‹‘Ù]Ýš[™Ê
Kˆ›ÛS˜[YHH™XY\‹‘Ù]Ýš[™ÊJKˆ\ØÜš\[ÛˆH™XY\‹’\Ñ“[
ŠHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊŠBˆJNÂˆBˆB‚ˆ˜\ˆ\›Z\ÜÚ[ÛœÈH™]È\ÝÝš[™ÏŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆ\›Z\ÜÚ[ÛÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕTÕSÕœ\›Z\ÜÚ[Û—ØÛÙBˆ”“ÓH\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜BˆS“‘Tˆ“ÒSˆ\Ü›Û\ÈˆÓˆ‹˜\Ü›ÛWÚYH\˜K˜\Ü›ÛWÚYˆS“‘Tˆ“ÒSˆ\Ü›ÛWÜ\›Z\ÜÚ[ÛœÈœÓˆœ˜\Ü›ÛWÚYH‹˜\Ü›ÛWÚYˆS“‘Tˆ“ÒSˆ\Ü\›Z\ÜÚ[ÛœÈÓˆ˜\Ü\›Z\ÜÚ[Û—ÚYHœ˜\Ü\›Z\ÜÚ[Û—ÚYˆÒT‘H\˜K\Ù\—ÚYH\Ù\—ÚYˆS‘\˜Kš\×ØXÝ]™HH•QBˆS‘‹š\×ØXÝ]™HH•QBˆÔ‘Tˆ–Hœ\›Z\ÜÚ[Û—ØÛÙNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ\›Z\ÜÚ[ÛÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]\›Z\ÜÚ[ÛÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JH\›Z\ÜÚ[ÛœËY
™XY\‹‘Ù]Ýš[™Ê
JNÂˆB‚ˆ˜\ˆ™X]\™\ÈH™]È\ÝØš™XÝŠ
NÂˆ]ØZ]\Ú[™È
˜\ˆ™X]\™PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ™X]\™WØÛÙK™X]\™WÛ˜[YK[Ù[WØÛÙK›Ý]WØ[˜ÚÜ‹™\]Z\™YÜ\›Z\ÜÚ[Û—ØÛÙK™X]\™WÙ\ØÜš\[Û‚ˆ”“ÓH\Ù™X]\™WØØ][ÙÂˆÒT‘H\×ØXÝ]™HH•QBˆS‘
™\]Z\™YÜ\›Z\ÜÚ[Û—ØÛÙHTÈ•SÔˆ™\]Z\™YÜ\›Z\ÜÚ[Û—ØÛÙHHS–J\›Z\ÜÚ[ÛœÊJBˆÔ‘Tˆ–H\Ü^WÛÜ™\ŽÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ™X]\™PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ\›Z\ÜÚ[ÛœÈ‹\›Z\ÜÚ[ÛœË•Ð\œ˜^J
JNÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]™X]\™PÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™X]\™\ËY
™]ÂˆÂˆ™X]\™PÛÙHH™XY\‹‘Ù]Ýš[™Ê
Kˆ™X]\™S˜[YHH™XY\‹‘Ù]Ýš[™ÊJKˆ[Ù[PÛÙHH™XY\‹‘Ù]Ýš[™ÊŠKˆ›Ý]P[˜ÚÜˆH™XY\‹’\Ñ“[
ÊHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊÊKˆ™\]Z\™Y\›Z\ÜÚ[ÛÛÙHH™XY\‹’\Ñ“[

HÈ[ˆ™XY\‹‘Ù]Ýš[™Ê
Kˆ\ØÜš\[ÛˆH™XY\‹’\Ñ“[
JHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊJBˆJNÂˆBˆB‚ˆ™]\›ˆ™]ÂˆÂˆ\Ù\’Yˆ[XZ[ˆ\Ü^S˜[YKˆ›Û\Ëˆ\›Z\ÜÚ[ÛœËˆ™X]\™\ËˆØ[ˆH™]ÂˆÂˆšY]Õ[YQ[žHH\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×ÕSQWÑS•–HŠKˆY]ÝÛ•[YHH\›Z\ÜÚ[ÛœËÛÛZ[œÊ‘QUÓÕÓ—ÕSQHŠKˆ\›Ý™U[YHH\›Z\ÜÚ[ÛœËÛÛZ[œÊT“Õ‘WÕSQHŠKˆ™Z™XÝ[YHH\›Z\ÜÚ[ÛœËÛÛZ[œÊ”‘R‘PÕÕSQHŠKˆX[˜YÙRÛY^\ÈH\›Z\ÜÚ[ÛœËÛÛZ[œÊ“PSQÑWÒÓQVTÈŠKˆšY]ÒÛY^\ÈH\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×ÒÓQVTÈŠKˆšY]Ô›Ú™XÝ[ZÙHH\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×Ô“Ò‘PÕÒS•RÑHŠKˆšY]Ô™\ÛÝ\˜ÙTØÚY[[™ÈH\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×Ô‘TÓÕTÑWÔÐÒQSS‘ÈŠKˆšY]Ñ^[œÙ\ÈH\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×ÑVS”ÑTÈŠKˆšY]Ñ^XÝ]]™T™\Ü[™ÈH\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×ÑVPÕUU‘WÔ‘TÔ•S‘ÈŠKˆšY]Ð]Y]˜Z[H\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×ÐUQUÕRSŠKˆ^Ü[YTˆH\›Z\ÜÚ[ÛœËÛÛZ[œÊ‘VÔ•ÕSQWÔˆŠKˆ^Ü[YQ^Ù[H\›Z\ÜÚ[ÛœËÛÛZ[œÊ‘VÔ•ÕSQWÑVÑSŠKˆÞ\Ý[PYZ[š\Ý˜][ÛˆH\›Z\ÜÚ[ÛœËÛÛZ[œÊ”ÖTÕSWÐQRS’TÕUSÓˆŠKˆX[˜YÙP[H\›Z\ÜÚ[ÛœËÛÛZ[œÊ“PSQÑWÐSŠBˆBˆNÂŸB‚œÝ]XÈT™\Ý[È˜[Y]PÛÛ™šYÊ]X˜\ÙPÛÛ™šYÈÛÛ™šYÊBžÂˆYˆ
ÛÛ™šYË“Z\ÜÚ[™ËÛÝ[OH
BˆÂˆ™]\›ˆ[ÂˆB‚ˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈH˜ÛÛ™šYÝ\˜][Û—ÛZ\ÜÚ[™È‹ˆZ\ÜÚ[™ÈHÛÛ™šYË“Z\ÜÚ[™ÂˆJNÂŸB‚œÝ]XÈ]SÛ›HÙ]Ý[™^Q›Ü‘]J]SÛ›H]JBžÂˆ˜\ˆÙ™œÙ]H
[
Y]K‘^SÙ•ÙYZÎÂˆ™]\›ˆ]KY^\Ê[Ù™œÙ]
NÂŸB‚œÝ]XÈT™XYÛ›S\ÝÝš[™Ïˆ˜[Y]U[Y\ÚY]™\]Y\Ý
[Y\ÚY]Ø]™T™\]Y\Ý™\]Y\Ý
BžÂˆ˜\ˆ\œ›ÜœÈH™]È\ÝÝš[™ÏŠ
NÂˆ˜\ˆÝ\HÙ]Ý[™^Q›Ü‘]J™\]Y\Ý•ÙYZÔÝ\
NÂˆ˜\ˆ[™HÝ\Y^\ÊŠNÂ‚ˆYˆ
™\]Y\Ý‘[šY\È\È[
BˆÂˆ\œ›ÜœËY
‘[šY\ÈÛÛXÝ[Ûˆ\È™\]Z\™YˆŠNÂˆ™]\›ˆ\œ›ÜœÎÂˆB‚ˆ›Ü™XXÚ
˜\ˆ[žH[ˆ™\]Y\Ý‘[šY\ÊBˆÂˆYˆ
[žK•ÛÜšÑ]HÝ\[žK•ÛÜšÑ]Hˆ[™
BˆÂˆ\œ›ÜœËY
	‘[žH]HÙ[žK•ÛÜšÑ]_H\ÈÝ]ÚYHHÙ[XÝYÙYZÈÜÝ\H›ÝYÚÙ[™KˆŠNÂˆB‚ˆYˆ
[žK•[YU\H\È›Ý
››Ü›X[ˆÜˆ˜Y\šÝ\œÈŠJBˆÂˆ\œ›ÜœËY
	’[˜[Y[YH\H	ÞÙ[žK•[YU\_IËˆ^XÝY›Ü›X[ÜˆY\šÝ\œËˆŠNÂˆB‚ˆYˆ
[žK’Ý\œÈ[žK’Ý\œÈˆ
BˆÂˆ\œ›ÜœËY
	’Ý\œÈ›ÜˆÙ[žK•ÛÜšÑ]_H]\Ý™H™]ÙY[ˆ[™ˆŠNÂˆB‚ˆYˆ
[žK’Ý\œÈˆ	‰ˆÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žKØ]YÛÜžPÛÙJH	‰ˆ
[žK”›Ú™XÝY\È[[žK•\ÚÒY\È[
JBˆÂˆ\œ›ÜœËY
	‘[žH›ÜˆÙ[žK•ÛÜšÑ]_H]\ÝY[YžHZ]\ˆH›Û‹\›Ú™XÝØ]YÛÜžHÜˆH›Ú™XÝ\ÚËˆŠNÂˆBˆB‚ˆ™]\›ˆ\œ›ÜœÎÂŸB‚‚‚‹ÊˆMWÐUQUÑVÔ•Õ’TÒP’SUWÒST”×ÔÕT•
‹ÂœÝ]XÈÝš[™È›Ú™XÝ[ÙLMU[˜Ø]JÝš[™ÏÈ˜[YK[X^[™Ý
BžÂˆ˜\ˆ^H˜[YHÏÈÝš[™Ë‘[\NÂˆYˆ
^“[™ÝHX^[™Ý
BˆÂˆ™]\›ˆ^ÂˆB‚ˆ™]\›ˆ^Ë‹›X^[™ÝH
È‹‹‹ˆŽÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLMP]Y]Ø]YÛÜžJÝš[™ÈXÝ[Û‹Ýš[™È[]U\JBžÂˆ˜\ˆ˜[YHH	žØXÝ[ÛŸHÙ[]U\_H‹•ÓÝÙ\’[˜\šX[

NÂ‚ˆYˆ
˜[YKÛÛZ[œÊ™^ÜŠH˜[YKÛÛZ[œÊ™ÝÛ›ØYŠH˜[YKÛÛZ[œÊœXÚØYÙHŠJBˆÂˆ™]\›ˆ™^ÜÜXÚØYÙHŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊœ™XÛÛ˜Ú[HŠJBˆÂˆ™]\›ˆœ™XÛÛ˜Ú[X][ÛˆŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ›ØÚÈŠJBˆÂˆ™]\›ˆ›ØÚÈŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ™XÛ[ˆŠH˜[YKÛÛZ[œÊœ™Z™XÝŠH˜[YKÛÛZ[œÊœ™]\›ˆŠJBˆÂˆ™]\›ˆ™XÛ[™WÜ™]\›ˆŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ˜\›Ý˜[ŠH˜[YKÛÛZ[œÊ˜\›Ý™YŠH˜[YKÛÛZ[œÊ›X[˜YÙ\ˆŠH˜[YKÛÛZ[œÊœWÈŠJBˆÂˆ™]\›ˆ˜\›Ý˜[ŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ[YHŠH˜[YKÛÛZ[œÊ[Y\ÚY]ŠJBˆÂˆ™]\›ˆ[YWÙ[žHŽÂˆB‚ˆ™]\›ˆ™Ù[™\˜[Ø]Y]ŽÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLMP]Y]ÛÝ\˜ÙS[Ù[JÝš[™ÈXÝ[Û‹Ýš[™È[]U\JBžÂˆ˜\ˆ˜[YHH	žØXÝ[ÛŸHÙ[]U\_H‹•ÓÝÙ\’[˜\šX[

NÂ‚ˆYˆ
˜[YKÛÛZ[œÊ[YWÝÛÜšÙ›Ý×Ù^ÜŠH˜[YKÛÛZ[œÊ™^ÜŠH˜[YKÛÛZ[œÊ™ÝÛ›ØYŠJBˆÂˆ™]\›ˆ\›Ý˜[È^ÜÈ]Y]ŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊ[Y\ÚY]ŠH˜[YKÛÛZ[œÊ[YWÙ[žHŠJBˆÂˆ™]\›ˆ•[YH[žHÈ\›Ý˜[ÛÜšÙ›ÝÈŽÂˆB‚ˆYˆ
˜[YKÛÛZ[œÊœ›Ú™XÝŠJBˆÂˆ™]\›ˆ”›Ú™XÝÛÜšÙ›ÝÈŽÂˆB‚ˆ™]\›ˆ]Y]\ÝÜžHŽÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLMPZ[]Y]]šY[˜ÙT™]šY]ÊÝš[™ÈXÝ[Û‹Ýš[™È[]U\KÝš[™ÏÈÛ˜[YKÝš[™ÏÈ™]Õ˜[YJBžÂˆ˜\ˆØ]YÛÜžHH›Ú™XÝ[ÙLMP]Y]Ø]YÛÜžJXÝ[Û‹[]U\JNÂˆ˜\ˆ^[ØYHÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ™]Õ˜[YJHÈÛ˜[YHÏÈÝš[™Ë‘[\Hˆ™]Õ˜[YHÏÈÝš[™Ë‘[\NÂ‚ˆ˜\ˆ™Yš^HØ]YÛÜžHÝÚ]ÚˆÂˆ™^ÜÜXÚØYÙHˆOˆ‘^ÜXÚØYÙH]šY[˜ÙH‹ˆœ™XÛÛ˜Ú[X][ÛˆˆOˆ”™XÛÛ˜Ú[X][Ûˆ]šY[˜ÙH‹ˆ›ØÚÈˆOˆ“ØÚÈ]šY[˜ÙH‹ˆ™XÛ[™WÜ™]\›ˆˆOˆ‘XÛ[™KÜ™]\›ˆ]šY[˜ÙH‹ˆ˜\›Ý˜[ˆOˆ\›Ý˜[]šY[˜ÙH‹ˆ[YWÙ[žHˆOˆ•[YKY[žH]šY[˜ÙH‹ˆÈOˆ]Y]]šY[˜ÙH‚ˆNÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ^[ØY
JBˆÂˆ™]\›ˆ	žÜ™Yš^NˆØXÝ[ÛŸH™XÛÜ™Y›ÜˆÙ[]U\_KˆŽÂˆB‚ˆ™]\›ˆ	žÜ™Yš^NˆÔ›Ú™XÝ[ÙLMU[˜Ø]J^[ØYÍŒ
_HŽÂŸB‹ÊˆMWÐUQUÑVÔ•Õ’TÒP’SUWÒST”×ÑS‘
‹Â‚‹ÊˆMÑVÔ•ÔÓTÒÕÒST”×ÔÕT•
‹ÂœÝ]XÈ\Þ[˜È\ÚÏ\ÝXÝ[Û˜\žOÝš[™ËØš™XÝÏˆ›Ú™XÝ[ÙLMØY^ÜÛ˜\ÚÝ][\Ð\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[ÛÈ˜[œØXÝ[Û‹ˆÝZY^ÜY
BžÂˆ˜\ˆ][\ÈH™]È\ÝXÝ[Û˜\žOÝš[™ËØš™XÝÏŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ[YWÙ[žWÚYˆÛÜš×Ù]Kˆ[\ÞYYWÛ˜[YKˆ[\ÞYYWÙ[XZ[ˆ›Ú™XÝØÛÙKˆ›Ú™XÝÛ˜[YKˆ\Ú×ØÛÙKˆ\Ú×Û˜[YKˆÝ\œËˆš[X›KˆÝ]\Ëˆ\ØÜš\[Û‚ˆ”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÚ][\ÂˆÒT‘H[YWÝÛÜšÙ›Ý×Ù^ÜÚYH^ÜÚYˆÔ‘Tˆ–HÛÜš×Ù]K[\ÞYYWÛ˜[YK›Ú™XÝØÛÙK\Ú×ØÛÙK[YWÙ[žWÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆYˆ
˜[œØXÝ[Ûˆ\È›Ý[
BˆÂˆÛÛ[X[™•˜[œØXÝ[ÛˆH˜[œØXÝ[ÛŽÂˆB‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™^ÜÚY‹^ÜY
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ][\ËY
™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝÏ‚ˆÂˆÈ[YQ[žRY—HH™XY\‹’\Ñ“[

HÈ[ˆ™XY\‹‘Ù]ÝZY

KˆÈÛÜšÑ]H—HH™XY\‹‘Ù]šY[˜[YO]SÛ›OŠJKˆÈ™[\ÞYYS˜[YH—HH™XY\‹‘Ù]Ýš[™ÊŠKˆÈ™[\ÞYYQ[XZ[—HH™XY\‹‘Ù]Ýš[™ÊÊKˆÈœ›Ú™XÝÛÙH—HH™XY\‹‘Ù]Ýš[™Ê
KˆÈœ›Ú™XÝ˜[YH—HH™XY\‹‘Ù]Ýš[™ÊJKˆÈ\ÚÐÛÙH—HH™XY\‹‘Ù]Ýš[™ÊŠKˆÈ\ÚÓ˜[YH—HH™XY\‹‘Ù]Ýš[™ÊÊKˆÈšÝ\œÈ—HH™XY\‹‘Ù]XÚ[X[

KˆÈ˜š[X›H—HH™XY\‹‘Ù]›ÛÛX[ŠJKˆÈœÝ]\È—HH™XY\‹‘Ù]Ýš[™ÊL
KˆÈ™\ØÜš\[Ûˆ—HH™XY\‹‘Ù]Ýš[™ÊLJBˆJNÂˆB‚ˆ™]\›ˆ][\ÎÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ\ÝXÝ[Û˜\žOÝš[™ËØš™XÝÏˆ›Ú™XÝ[ÙLMØYYØXÞS]™Q^Ü][\Ð\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[ÛÈ˜[œØXÝ[Û‹ˆ]SÛ›HÝ\ˆ]SÛ›H[™
BžÂˆ˜\ˆ][\ÈH™]È\ÝXÝ[Û˜\žOÝš[™ËØš™XÝÏŠ
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆK[YWÙ[žWÚYˆKÛÜš×Ù]KˆÓÐSTÐÑJ[\ÞYYK™\Ü^WÛ˜[YK[\ÞYYK™[XZ[	ÉÊHTÈ[\ÞYYWÛ˜[YKˆÓÐSTÐÑJ[\ÞYYK™[XZ[	ÉÊHTÈ[\ÞYYWÙ[XZ[ˆÓÐSTÐÑJœ›Ú™XÝØÛÙK	ÉÊHTÈ›Ú™XÝØÛÙKˆÓÐSTÐÑJœ›Ú™XÝÛ˜[YK	ÉÊHTÈ›Ú™XÝÛ˜[YKˆÓÐSTÐÑJ\Ú×ØÛÙK	ÉÊHTÈ\Ú×ØÛÙKˆÓÐSTÐÑJ\Ú×Û˜[YK	ÉÊHTÈ\Ú×Û˜[YKˆKšÝ\œËˆK˜š[X›KˆKœÝ]\ËˆÓÐSTÐÑJK™\ØÜš\[Û‹	ÉÊHTÈ\ØÜš\[Û‚ˆ”“ÓH[YWÙ[šY\ÈBˆ“ÒSˆ\Ý\Ù\œÈ[\ÞYYBˆÓˆ[\ÞYYK\Ù\—ÚYHK\Ù\—ÚYˆQ•“ÒSˆ›Ú™XÝÈˆÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚYˆQ•“ÒSˆ›Ú™XÝÝ\ÚÜÈˆÓˆ\Ú×ÚYHK\Ú×ÚYˆÒT‘HKÛÜš×Ù]H‘UÑQSˆÙYZ×ÜÝ\S‘ÙYZ×Ù[™ˆS‘KœÝ]\ÈSˆ
	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊBˆÔ‘Tˆ–HKÛÜš×Ù]K[\ÞYYK™\Ü^WÛ˜[YKœ›Ú™XÝØÛÙK\Ú×ØÛÙNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆYˆ
˜[œØXÝ[Ûˆ\È›Ý[
BˆÂˆÛÛ[X[™•˜[œØXÝ[ÛˆH˜[œØXÝ[ÛŽÂˆB‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹Ý\
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹[™
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ][\ËY
™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝÏ‚ˆÂˆÈ[YQ[žRY—HH™XY\‹‘Ù]ÝZY

KˆÈÛÜšÑ]H—HH™XY\‹‘Ù]šY[˜[YO]SÛ›OŠJKˆÈ™[\ÞYYS˜[YH—HH™XY\‹‘Ù]Ýš[™ÊŠKˆÈ™[\ÞYYQ[XZ[—HH™XY\‹‘Ù]Ýš[™ÊÊKˆÈœ›Ú™XÝÛÙH—HH™XY\‹‘Ù]Ýš[™Ê
KˆÈœ›Ú™XÝ˜[YH—HH™XY\‹‘Ù]Ýš[™ÊJKˆÈ\ÚÐÛÙH—HH™XY\‹‘Ù]Ýš[™ÊŠKˆÈ\ÚÓ˜[YH—HH™XY\‹‘Ù]Ýš[™ÊÊKˆÈšÝ\œÈ—HH™XY\‹‘Ù]XÚ[X[

KˆÈ˜š[X›H—HH™XY\‹‘Ù]›ÛÛX[ŠJKˆÈœÝ]\È—HH™XY\‹‘Ù]Ýš[™ÊL
KˆÈ™\ØÜš\[Ûˆ—HH™XY\‹‘Ù]Ýš[™ÊLJBˆJNÂˆB‚ˆ™]\›ˆ][\ÎÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLMZ[^ÜÜÝŠˆÝZY^ÜYˆÝš[™È^Ü›Ü›X]ˆ]SÛ›HÝ\ˆ]SÛ›H[™ˆT™XYÛ›S\ÝXÝ[Û˜\žOÝš[™ËØš™XÝÏˆ][\ÊBžÂˆ˜\ˆÜÝˆH™]ÈÞ\Ý[K•^”Ýš[™ÐZ[\Š
NÂˆÜÝ‹\[™[™J‘^ÜY^Ü›Ü›X]ÙYZÈÝ\ÙYZÈ[™ÛÜšÈ]K[\ÞYYH˜[YK[\ÞYYH[XZ[›Ú™XÝÛÙK›Ú™XÝ˜[YK\ÚÈÛÙK\ÚÈ˜[YKÝ\œËš[X›KÝ]\Ë\ØÜš\[ÛˆŠNÂ‚ˆ›Ü™XXÚ
˜\ˆ][H[ˆ][\ÊBˆÂˆ˜\ˆšY[ÈH™]Ö×BˆÂˆ›Ú™XÝ[ÙPÜÝ‘šY[
^ÜY
Kˆ›Ú™XÝ[ÙPÜÝ‘šY[
^Ü›Ü›X]
Kˆ›Ú™XÝ[ÙPÜÝ‘šY[
Ý\
Kˆ›Ú™XÝ[ÙPÜÝ‘šY[
[™
Kˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLM]SÛ›J][KÛÜšÑ]HŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMÝš[™Ê][K™[\ÞYYS˜[YHŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMÝš[™Ê][K™[\ÞYYQ[XZ[ŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMÝš[™Ê][Kœ›Ú™XÝÛÙHŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMÝš[™Ê][Kœ›Ú™XÝ˜[YHŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMÝš[™Ê][K\ÚÐÛÙHŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMÝš[™Ê][K\ÚÓ˜[YHŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMXÚ[X[
][KšÝ\œÈŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLM›ÛÛ
][K˜š[X›HŠHÈ–Y\Èˆˆ“›ÈŠKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMÝš[™Ê][KœÝ]\ÈŠJKˆ›Ú™XÝ[ÙPÜÝ‘šY[
›Ú™XÝ[ÙLMÝš[™Ê][K™\ØÜš\[ÛˆŠJBˆNÂ‚ˆÜÝ‹\[™[™JÝš[™Ë’›Ú[Š‹‹šY[ÊJNÂˆB‚ˆ™]\›ˆÜÝ‹•ÔÝš[™Ê
NÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLMÛÛ\]TÚLMŠÝš[™È˜[YJBžÂˆ˜\ˆž]\ÈHÞ\Ý[K•^‘[˜ÛÙ[™Ë•UŽ‘Ù]ž]\Ê˜[YJNÂˆ˜\ˆ\ÚHÞ\Ý[K”ÙXÝ\š]KÜž\ÙÜ˜\K”ÒLM‹’\Ú]Jž]\ÊNÂˆ™]\›ˆÛÛ™\•Ò^Ýš[™Ê\Ú
K•ÓÝÙ\’[˜\šX[

NÂŸB‚œÝ]XÈØš™XÝ›Ú™XÝ[ÙLM^ÜÛ˜\ÚÝ][QÊXÝ[Û˜\žOÝš[™ËØš™XÝÏˆ][JBžÂˆ™]\›ˆ™]ÂˆÂˆ[YQ[žRYH›Ú™XÝ[ÙLM[X›J][K[YQ[žRYŠKˆÛÜšÑ]HH›Ú™XÝ[ÙLM]SÛ›J][KÛÜšÑ]HŠKˆ[\ÞYYS˜[YHH›Ú™XÝ[ÙLMÝš[™Ê][K™[\ÞYYS˜[YHŠKˆ[\ÞYYQ[XZ[H›Ú™XÝ[ÙLMÝš[™Ê][K™[\ÞYYQ[XZ[ŠKˆ›Ú™XÝÛÙHH›Ú™XÝ[ÙLMÝš[™Ê][Kœ›Ú™XÝÛÙHŠKˆ›Ú™XÝ˜[YHH›Ú™XÝ[ÙLMÝš[™Ê][Kœ›Ú™XÝ˜[YHŠKˆ\ÚÐÛÙHH›Ú™XÝ[ÙLMÝš[™Ê][K\ÚÐÛÙHŠKˆ\ÚÓ˜[YHH›Ú™XÝ[ÙLMÝš[™Ê][K\ÚÓ˜[YHŠKˆÝ\œÈH›Ú™XÝ[ÙLMXÚ[X[
][KšÝ\œÈŠKˆš[X›HH›Ú™XÝ[ÙLM›ÛÛ
][K˜š[X›HŠKˆÝ]\ÈH›Ú™XÝ[ÙLMÝš[™Ê][KœÝ]\ÈŠKˆ\ØÜš\[ÛˆH›Ú™XÝ[ÙLMÝš[™Ê][K™\ØÜš\[ÛˆŠBˆNÂŸB‚œÝ]XÈØš™XÝÈ›Ú™XÝ[ÙLM[X›JXÝ[Û˜\žOÝš[™ËØš™XÝÏˆ][KÝš[™ÈÙ^JBžÂˆ™]\›ˆ][K•žQÙ]˜[YJÙ^KÝ]˜\ˆ˜[YJHÈ˜[YHˆ[ÂŸB‚œÝ]XÈÝš[™È›Ú™XÝ[ÙLMÝš[™ÊXÝ[Û˜\žOÝš[™ËØš™XÝÏˆ][KÝš[™ÈÙ^JBžÂˆYˆ
Z][K•žQÙ]˜[YJÙ^KÝ]˜\ˆ˜[YJH˜[YH\È[˜[YH\È“[
BˆÂˆ™]\›ˆÝš[™Ë‘[\NÂˆB‚ˆ™]\›ˆÛÛ™\•ÔÝš[™Ê˜[YJHÏÈÝš[™Ë‘[\NÂŸB‚œÝ]XÈXÚ[X[›Ú™XÝ[ÙLMXÚ[X[
XÝ[Û˜\žOÝš[™ËØš™XÝÏˆ][KÝš[™ÈÙ^JBžÂˆYˆ
Z][K•žQÙ]˜[YJÙ^KÝ]˜\ˆ˜[YJH˜[YH\È[˜[YH\È“[
BˆÂˆ™]\›ˆNÂˆB‚ˆ™]\›ˆ˜[YH\ÈXÚ[X[XÚ[X[˜[YHÈXÚ[X[˜[YHˆÛÛ™\•ÑXÚ[X[
˜[YJNÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLM›ÛÛ
XÝ[Û˜\žOÝš[™ËØš™XÝÏˆ][KÝš[™ÈÙ^JBžÂˆYˆ
Z][K•žQÙ]˜[YJÙ^KÝ]˜\ˆ˜[YJH˜[YH\È[˜[YH\È“[
BˆÂˆ™]\›ˆ˜[ÙNÂˆB‚ˆ™]\›ˆ˜[YH\È›ÛÛ›ÛÛ˜[YHÈ›ÛÛ˜[YHˆÛÛ™\•Ð›ÛÛX[Š˜[YJNÂŸB‚œÝ]XÈ]SÛ›H›Ú™XÝ[ÙLM]SÛ›JXÝ[Û˜\žOÝš[™ËØš™XÝÏˆ][KÝš[™ÈÙ^JBžÂˆYˆ
Z][K•žQÙ]˜[YJÙ^KÝ]˜\ˆ˜[YJH˜[YH\È[˜[YH\È“[
BˆÂˆ™]\›ˆ]SÛ›K‘œ›ÛQ]U[YJ]U[YK•]Ó›ÝË‘]JNÂˆB‚ˆ™]\›ˆ˜[YH\È]SÛ›H]SÛ›U˜[YBˆÈ]SÛ›U˜[YBˆˆ]SÛ›K‘œ›ÛQ]U[YJÛÛ™\•Ñ]U[YJ˜[YJJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ
Ýš[™ÏÈXÚØYÙTÚLM‹[XÚØYÙTÛ˜\ÚÝ][PÛÝ[
Oˆ›Ú™XÝ[ÙLMØY^ÜY]Y]P\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[ÛÈ˜[œØXÝ[Û‹ˆÝZY^ÜY
BžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕXÚØYÙWÜÚLM‹ÓÐSTÐÑJXÚØYÙWÜÛ˜\ÚÝÚ][WØÛÝ[
Bˆ”“ÓH[YWÝÛÜšÙ›Ý×Ù^ÜÛY]Y]BˆÒT‘H[YWÝÛÜšÙ›Ý×Ù^ÜÚYH^ÜÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆYˆ
˜[œØXÝ[Ûˆ\È›Ý[
BˆÂˆÛÛ[X[™•˜[œØXÝ[ÛˆH˜[œØXÝ[ÛŽÂˆB‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™^ÜÚY‹^ÜY
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
X]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™]\›ˆ
[
NÂˆB‚ˆ™]\›ˆ
ˆ™XY\‹’\Ñ“[

HÈ[ˆ™XY\‹‘Ù]Ýš[™Ê
Kˆ™XY\‹‘Ù][ÌŠJJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏÝš[™ÏÏˆ›Ú™XÝ[ÙLMÙ]ÝÜ™YXÚØYÙTÚP\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[ÛÈ˜[œØXÝ[Û‹ˆÝZY^ÜY
BžÂˆ˜\ˆY]Y]HH]ØZ]›Ú™XÝ[ÙLMØY^ÜY]Y]P\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹^ÜY
NÂˆ™]\›ˆY]Y]K”XÚØYÙTÚLMŽÂŸB‚œÝ]XÈ\Þ[˜È\ÚÈ›Ú™XÝ[ÙLM\Ù\^ÜY]Y]P\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆÝZY^ÜYˆÝš[™ÈXÚØYÙTÚLM‹ˆÝš[™ÈXÚØYÙTÛ˜\ÚÝˆ[][PÛÝ[
BžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È[YWÝÛÜšÙ›Ý×Ù^ÜÛY]Y]H
ˆ[YWÝÛÜšÙ›Ý×Ù^ÜÚYˆXÚØYÙWÜÚLM‹ˆXÚØYÙWÜÛ˜\ÚÝˆXÚØYÙWÜÛ˜\ÚÝÚ][WØÛÝ[ˆ\]YØ]ˆ
BˆSQTÈ
ˆ^ÜÚYˆXÚØYÙWÜÚLM‹ˆXÚØYÙWÜÛ˜\ÚÝŽšœÛÛ˜‹ˆ][WØÛÝ[ˆ“ÕÊ
Bˆ
BˆÓˆÓÓ‘“PÕ
[YWÝÛÜšÙ›Ý×Ù^ÜÚY
HÈTUBˆÑUXÚØYÙWÜÚLMˆHÓÐSTÐÑJ[YWÝÛÜšÙ›Ý×Ù^ÜÛY]Y]KœXÚØYÙWÜÚLM‹VÓQQœXÚØYÙWÜÚLMŠKˆXÚØYÙWÜÛ˜\ÚÝHÓÐSTÐÑJ[YWÝÛÜšÙ›Ý×Ù^ÜÛY]Y]KœXÚØYÙWÜÛ˜\ÚÝVÓQQœXÚØYÙWÜÛ˜\ÚÝ
KˆXÚØYÙWÜÛ˜\ÚÝÚ][WØÛÝ[HÐTÑBˆÒSˆ[YWÝÛÜšÙ›Ý×Ù^ÜÛY]Y]KœXÚØYÙWÜÛ˜\ÚÝÚ][WØÛÝ[HSˆVÓQQœXÚØYÙWÜÛ˜\ÚÝÚ][WØÛÝ[ˆSÑH[YWÝÛÜšÙ›Ý×Ù^ÜÛY]Y]KœXÚØYÙWÜÛ˜\ÚÝÚ][WØÛÝ[ˆS‘ˆ\]YØ]H“ÕÊ
NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™^ÜÚY‹^ÜY
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœXÚØYÙWÜÚLMˆ‹XÚØYÙTÚLMŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœXÚØYÙWÜÛ˜\ÚÝ‹XÚØYÙTÛ˜\ÚÝ
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJš][WØÛÝ[‹][PÛÝ[
NÂˆ]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂŸB‹ÊˆMÑVÔ•ÔÓTÒÕÒST”×ÑS‘
‹Â‚‹ÊˆM×ÕÓÔ’Ñ“Õ×ÒSSUUP’SUWÒST”×ÔÕT•
‹ÂœÝ]XÈ\Þ[˜È\ÚÏÝš[™ÏÏˆ›Ú™XÝ[ÙLMÑÙ][Y\ÚY]^TÝ]\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[ÛÈ˜[œØXÝ[Û‹ÝZY[Y\ÚY]Y]SÛ›HÛÜšÑ]JBžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕÝ]\Âˆ”“ÓH[Y\ÚY]Ù^WÜÝ]\Ù\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘ÛÜš×Ù]HHÛÜš×Ù]NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆYˆ
˜[œØXÝ[Ûˆ\È›Ý[
BˆÂˆÛÛ[X[™•˜[œØXÝ[ÛˆH˜[œØXÝ[ÛŽÂˆB‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛÜšÑ]JNÂ‚ˆ˜\ˆ˜[YHH]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂˆ™]\›ˆ˜[YH\ÈÝš[™ÎÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLMÕÛÜšÙ›ÝÕ˜[œÚ][Û[ÝÙY
Ýš[™È›Ü›X[^™YXÝ[Û‹Ýš[™ÏÈÝ\œ™[Ý]\ÊBžÂˆ˜\ˆÝ]\ÈH
Ý\œ™[Ý]\ÈÏÈÝš[™Ë‘[\JK•š[J
K•ÓÝÙ\’[˜\šX[

NÂ‚ˆ™]\›ˆ›Ü›X[^™YXÝ[ÛˆÝÚ]ÚˆÂˆœWØ\›Ý™HˆOˆÝ]\ÈOH›X[˜YÙ\—Ø\›Ý™Y‹ˆœWÜ™Z™XÝˆOˆÝ]\ÈOH›X[˜YÙ\—Ø\›Ý™Y‹ˆ˜XØÛÝ[[™×Ü™XYHˆOˆÝ]\È\È›X[˜YÙ\—Ø\›Ý™YˆÜˆœWØ\›Ý™Y‹ˆœ™XÛÛ˜Ú[HˆOˆÝ]\È\È˜XØÛÝ[[™×Ü™XYHˆÜˆœWØ\›Ý™Y‹ˆ›ØÚÈˆOˆÝ]\È\È˜XØÛÝ[[™×Ü™XYHˆÜˆœ™XÛÛ˜Ú[Y‹ˆÈOˆ˜[ÙBˆNÂŸB‹ÊˆM×ÕÓÔ’Ñ“Õ×ÒSSUUP’SUWÒST”×ÑS‘
‹Â‚‹ÊˆM—ÐT“ÕSÐUUÔ’UWÒST”×ÔÕT•
‹Â‚œÝ]XÈ\Þ[˜È\ÚÏÝZYÏˆ›Ú™XÝ[ÙLM‘Ù][Y\ÚY]ÝÛ™\•\Ù\’Y\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[ÛÈ˜[œØXÝ[Û‹ÝZY[Y\ÚY]Y
BžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ\Ù\—ÚYˆ”“ÓH[Y\ÚY]ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆYˆ
˜[œØXÝ[Ûˆ\È›Ý[
BˆÂˆÛÛ[X[™•˜[œØXÝ[ÛˆH˜[œØXÝ[ÛŽÂˆB‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂ‚ˆ˜\ˆ˜[YHH]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂˆ™]\›ˆ˜[YH\ÈÝZY\Ù\’YÈ\Ù\’Yˆ[ÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ›ÛÛˆ›Ú™XÝ[ÙLM•ÛÜšÙ›ÝÑ^R\Ô›Ú™XÝX[˜YÙ\”ØÛÜP\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZYXÝÜ•\Ù\’YÝZY[Y\ÚY]Y]SÛ›HÛÜšÑ]JBžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕVTÕÈ
ˆÑSPÕBˆ”“ÓH[YWÙ[šY\ÈBˆ“ÒSˆ›Ú™XÝÈˆÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚYˆÒT‘HK[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘KÛÜš×Ù]HHÛÜš×Ù]BˆS‘œ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚYHXÝÜ—Ý\Ù\—ÚYˆ
NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝÜ—Ý\Ù\—ÚY‹XÝÜ•\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛÜšÑ]JNÂ‚ˆ™]\›ˆÛÛ™\•Ð›ÛÛX[Š]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ˜[ÙJNÂŸB‹ÊˆM—ÐT“ÕSÐUUÔ’UWÒST”×ÑS‘
‹Â‚œÝ]XÈ\Þ[˜È\ÚÏÝZYˆÙ]ÜÜ™X]Q]™[ÜY[X[˜YÙ\•\Ù\’Y\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[ÛÈ˜[œØXÝ[ÛˆH[
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È\Ý\Ù\œÈ
[XZ[\Ü^WÛ˜[YK›Ø—Ý]K\\Y[\×ØXÝ]™JBˆSQTÈ
	ØZYY˜Y^Y[ZP\ÜÚYÛ˜[›ØØ[	Ë	ÐZYYY^Y[ZIË	Ñ]™[ÜY[X[˜YÙ\‰Ë	Ô›Ú™XÝ[ÙIË•QJBˆÓˆÓÓ‘“PÕ
[XZ[
HÈTUBˆÑU\Ü^WÛ˜[YHHVÓQQ™\Ü^WÛ˜[YKˆ\]YØ]H“ÕÊ
Bˆ‘UT“’S‘È\Ù\—ÚYÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆ™]\›ˆ
ÝZY
J]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ›ÝÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠ•[˜X›HÈÜ™X]H]™[ÜY[X[˜YÙ\ˆ\Ù\‹ˆŠJNÂŸB‚‹ÊˆLÒÐTÔÒQÓ“QS•ÐÓÔÕÑÕPT‘RSÒST—ÔÕT•
‹ÂœÝ]XÈXÚ[X[›Ú™XÝ[ÙLLÒÙ]Y˜][[™Ú[™Y\š[™ÒÝ\›PÛÜÝ

BžÂˆ˜\ˆÛÛ™šYÝ\™Y˜[YHH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”ÑQUSÑS‘ÒS‘QT’S‘×ÒÕT“WÐÓÔÕŠNÂ‚ˆYˆ
XÚ[X[•žT\œÙJÛÛ™šYÝ\™Y˜[YKÝ]˜\ˆ\œÙY˜[YJH	‰ˆ\œÙY˜[YHˆJBˆÂˆ™]\›ˆ\œÙY˜[YNÂˆB‚ˆ™]\›ˆMLNÂŸB‹ÊˆLÒÐTÔÒQÓ“QS•ÐÓÔÕÑÕPT‘RSÒST—ÑS‘
‹Â‚œÝ]XÈ\Þ[˜È\ÚÏ\ÝØš™XÝˆØYÜ[\ÜÚYÛ™Y›Ú™XÝ\ÚÜÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y]SÛ›HÙYZÔÝ\]SÛ›HÙYZÑ[™
BžÂˆ˜\ˆ\ÚÜÈH™]È\ÝØš™XÝŠ
NÂ‚ˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆÑSPÕTÕSÕˆKœ›Ú™XÝØ\ÜÚYÛ›Y[ÚYˆœ›Ú™XÝÚYˆœ›Ú™XÝØÛÙKˆœ›Ú™XÝÛ˜[YKˆË˜ÛY[Û˜[YKˆË˜ÛY[ØÛÙKˆ\Ú×ÚYˆ\Ú×ØÛÙKˆ\Ú×Û˜[YKˆ\Ú×Ù\ØÜš\[Û‹ˆÓÐSTÐÑJK˜[ØØ][Û—Ü\˜Ù[
HTÈ[ØØ][Û—Ü\˜Ù[ˆK™Y™™XÝ]™WÜÝ\Ù]KˆK™Y™™XÝ]™WÙ[™Ù]Kˆœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚYˆK™\Ü^WÛ˜[YHTÈ›Ú™XÝÛX[˜YÙ\—Û˜[YBˆ”“ÓH›Ú™XÝØ\ÜÚYÛ›Y[ÈBˆS“‘Tˆ“ÒSˆ›Ú™XÝÈÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚYˆS“‘Tˆ“ÒSˆ›Ú™XÝÝ\ÚÜÈÓˆ\Ú×ÚYHK\Ú×ÚYˆQ•“ÒSˆÛY[ÈÈÓˆË˜ÛY[ÚYH˜ÛY[ÚYˆQ•“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚYˆÒT‘HK\Ù\—ÚYH\Ù\—ÚYˆS‘K™Y™™XÝ]™WÜÝ\Ù]HHÙYZ×Ù[™ˆS‘
K™Y™™XÝ]™WÙ[™Ù]HTÈ•SÔˆK™Y™™XÝ]™WÙ[™Ù]HHÙYZ×ÜÝ\
BˆÊˆLÑ×ÒQWÐÓÔÑQÔ“Ò‘PÕ×Ñ”“ÓWÓÔS—ÕTÒÔÈ
‹ÂˆS‘ÝÙ\ŠÓÐSTÐÑJœÝ]\Ë	ØXÝ]™IÊJHH	ØXÝ]™IÂˆS‘š\×ØXÝ]™HH•QBˆÔ‘Tˆ–Hœ›Ú™XÝØÛÙK\Ú×ØÛÙNÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹ÙYZÔÝ\
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹ÙYZÑ[™
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ\ÚÜËY
™]ÂˆÂˆ\ÜÚYÛ›Y[YH™XY\‹‘Ù]ÝZY

Kˆ›Ú™XÝYH™XY\‹‘Ù]ÝZY
JKˆ›Ú™XÝÛÙHH™XY\‹‘Ù]Ýš[™ÊŠKˆ›Ú™XÝ˜[YHH™XY\‹‘Ù]Ýš[™ÊÊKˆÛY[˜[YHH™XY\‹’\Ñ“[

HÈ[ˆ™XY\‹‘Ù]Ýš[™Ê
KˆÛY[ÛÙHH™XY\‹’\Ñ“[
JHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊJKˆ\ÚÒYH™XY\‹‘Ù]ÝZY
ŠKˆ\ÚÐÛÙHH™XY\‹‘Ù]Ýš[™ÊÊKˆ\ÚÓ˜[YHH™XY\‹‘Ù]Ýš[™Ê
Kˆ\ÚÑ\ØÜš\[ÛˆH™XY\‹’\Ñ“[
JHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊJKˆ[ØØ][Û”\˜Ù[H™XY\‹‘Ù]XÚ[X[
L
KˆY™™XÝ]™TÝ\]HH™XY\‹‘Ù]šY[˜[YO]SÛ›OŠLJKˆY™™XÝ]™Q[™]HH™XY\‹’\Ñ“[
LŠHÈ
]SÛ›OÊ[[ˆ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠLŠKˆ›Ú™XÝX[˜YÙ\•\Ù\’YH™XY\‹’\Ñ“[
LÊHÈ
ÝZYÊ[[ˆ™XY\‹‘Ù]ÝZY
LÊKˆ›Ú™XÝX[˜YÙ\“˜[YHH™XY\‹’\Ñ“[
M
HÈ[ˆ™XY\‹‘Ù]Ýš[™ÊM
BˆJNÂˆB‚ˆ™]\›ˆ\ÚÜÎÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏÝZYˆÙ]ÜÜ™X]Q]™[ÜY[\Ù\’Y\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[ÛÈ˜[œØXÝ[ÛˆH[
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È\Ý\Ù\œÈ
[XZ[\Ü^WÛ˜[YK›Ø—Ý]K\\Y[\×ØXÝ]™JBˆSQTÈ
	ØZYY˜Y^Y[ZP\ÜÚYÛ˜[›ØØ[	Ë	ÐZYYY^Y[ZIË	Ñ]™[ÜY[[™Ú[™Y\‰Ë	Ô›Ù™\ÜÚ[Û˜[Ù\šXÙ\ÉË•QJBˆÓˆÓÓ‘“PÕ
[XZ[
HÈTUBˆÑU\Ü^WÛ˜[YHHVÓQQ™\Ü^WÛ˜[YKˆ›Ø—Ý]HHVÓQQš›Ø—Ý]Kˆ\\Y[HVÓQQ™\\Y[ˆ\×ØXÝ]™HH•QKˆ\]YØ]H“ÕÊ
Bˆ‘UT“’S‘È\Ù\—ÚYÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆ™]\›ˆ
ÝZY
J]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ›ÝÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠ•[˜X›HÈÜ™X]H]™[ÜY[\Ù\‹ˆŠJNÂŸB‚‚‚‹ÊˆLP—ÕSQWÑS•–WÐÔ’UPÐSÔ‘TRT—ÔÕT•
‹ÂœÝ]XÈ›ÛÛ›Ú™XÝ[ÙLLP’\Ò[[]]X›U[Y\ÚY]^TÝ]\ÊÝš[™ÏÈÝ]\ÊBžÂˆ™]\›ˆÝš[™Ë‘\]X[ÊÝ]\ËœÝX›Z]Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ë›X[˜YÙ\—Ø\›Ý™Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\ËœWØ\›Ý™Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ë˜XØÛÝ[[™×Ü™XYH‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ëœ™XÛÛ˜Ú[Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ë›ØÚÙY‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLLP’\ÑY]X›U[Y\ÚY]^TÝ]\ÊÝš[™ÏÈÝ]\ÊBžÂˆ™]\›ˆÝš[™Ë’\Ó[Ü•Ú]TÜXÙJÝ]\ÊBˆÝš[™Ë‘\]X[ÊÝ]\Ë™˜Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ë›X[˜YÙ\—ÙXÛ[™Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂŸB‚œÝ]XÈ›ÛÛ›Ú™XÝ[ÙLLP’\Ò[[]]X›U[Y\ÚY]ÙYZÔÝ]\ÊÝš[™ÏÈÝ]\ÊBžÂˆ™]\›ˆÝš[™Ë‘\]X[ÊÝ]\ËœÝX›Z]Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ë›X[˜YÙ\—Ø\›Ý™Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\ËœWØ\›Ý™Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ë˜XØÛÝ[[™×Ü™XYH‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ëœ™XÛÛ˜Ú[Y‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆÝš[™Ë‘\]X[ÊÝ]\Ë›ØÚÙY‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏÝZYÏˆ›Ú™XÝ[ÙLLP‘Ù]ÛY^PØ]YÛÜžRY\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[ÛŠBžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ”“ÓH›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÂˆÒT‘H\×ØXÝ]™HH•QBˆS‘
ˆTTŠÓÐSTÐÑJØ]YÛÜžWØÛÙK	ÉÊJHH	ÒÓQVIÂˆÔˆTTŠÓÐSTÐÑJØ]YÛÜžWÛ˜[YK	ÉÊJHH	ÒÓQVIÂˆÔˆÕÑTŠÓÐSTÐÑJØ]YÛÜžWÛ˜[YK	ÉÊJHRÑH	ÉZÛY^IIÂˆ
BˆÔ‘Tˆ–BˆÐTÑHÒSˆTTŠÓÐSTÐÑJØ]YÛÜžWØÛÙK	ÉÊJHH	ÒÓQVIÈSˆSÑHHS‘ˆØ]YÛÜžWÛ˜[YBˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂ‚ˆ˜\ˆ™\Ý[H]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂ‚ˆ™]\›ˆ™\Ý[\ÈÝZYÛY^PØ]YÛÜžRYÈÛY^PØ]YÛÜžRYˆ[ÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ›ÛÛˆ›Ú™XÝ[ÙLLP•\Ù\’\ÒÛY^P]]ÔÝX›Z][YÚX›P\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y
BžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕVTÕÈ
ˆÑSPÕBˆ”“ÓH\Ý\Ù\œÈBˆ“ÒSˆ\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜BˆÓˆ\˜K\Ù\—ÚYHK\Ù\—ÚYˆS‘\˜Kš\×ØXÝ]™HH•QBˆ“ÒSˆ\Ü›Û\È‚ˆÓˆ‹˜\Ü›ÛWÚYH\˜K˜\Ü›ÛWÚYˆS‘‹š\×ØXÝ]™HH•QBˆÒT‘HK\Ù\—ÚYH\Ù\—ÚYˆS‘Kš\×ØXÝ]™HH•QBˆS‘‹œ›ÛWØÛÙHSˆ
ˆ	ÑS‘ÒS‘QT‰Ëˆ	ÑS‘ÒS‘QT’S‘ÉËˆ	Ô“Ò‘PÕÓPSQÑT‰Ëˆ	Ô“Ò‘PÕÓPSQÑSQS•	Ëˆ	ÔWÕPSWÓPQ	Ëˆ	Ô“Ò‘PÕÓPSQÑSQS•ÓPQ	Âˆ
Bˆ
NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂ‚ˆ™]\›ˆÛÛ™\•Ð›ÛÛX[Š]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ˜[ÙJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ[ˆ›Ú™XÝ[ÙLLP]]ÔÝX›Z][YÚX›RÛY^\Ñ›Ü•ÙYZÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y]SÛ›HÙYZÔÝ\
BžÂˆYˆ
X]ØZ]›Ú™XÝ[ÙLLP•\Ù\’\ÒÛY^P]]ÔÝX›Z][YÚX›P\Þ[˜ÊÛÛ›™XÝ[Û‹\Ù\’Y
JBˆÂˆ™]\›ˆÂˆB‚ˆ˜\ˆÙYZÑ[™HÙYZÔÝ\Y^\ÊŠNÂˆ˜\ˆÝX›Z]YÛÝ[HÂ‚ˆ]ØZ]\Ú[™È˜\ˆ˜[œØXÝ[ÛˆH]ØZ]ÛÛ›™XÝ[Û‹™YÚ[•˜[œØXÝ[Û\Þ[˜Ê
NÂ‚ˆžBˆÂˆ˜\ˆ[Y\ÚY]YH]ØZ]\Ù\˜Y[Y\ÚY]\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹\Ù\’YÙYZÔÝ\
NÂˆ˜\ˆÛY^PØ]YÛÜžRYH]ØZ]›Ú™XÝ[ÙLLP‘Ù]ÛY^PØ]YÛÜžRY\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂ‚ˆYˆ
ÛY^PØ]YÛÜžRY\È[
BˆÂˆ]ØZ]˜[œØXÝ[Û‹ÛÛ[Z]\Þ[˜Ê
NÂˆ™]\›ˆÂˆB‚ˆ˜\ˆÛY^\ÈH™]È\Ý
]SÛ›HÛY^Q]KÝš[™ÈÛY^S˜[YKXÚ[X[Ý\œÊOŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆÛY^PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕÛY^WÙ]KÛY^WÛ˜[YK]]×ÜÜ[]WÚÝ\œÂˆ”“ÓHÛÛ\[žWÚÛY^\ÂˆÒT‘H\×ØXÝ]™HH•QBˆS‘\×Ù›Ø][™×ÚÛY^HHSÑBˆS‘ÛY^WÙ]H‘UÑQSˆÙYZ×ÜÝ\S‘ÙYZ×Ù[™ˆS‘VPÕ
TÓÑÕÈ”“ÓHÛY^WÙ]JH‘UÑQSˆHS‘BˆÔ‘Tˆ–HÛY^WÙ]NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆÛY^PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\‹ÙYZÔÝ\
NÂˆÛY^PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™‹ÙYZÑ[™
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛY^PÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂ‚ˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÛY^\ËY

ˆ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠ
Kˆ™XY\‹‘Ù]Ýš[™ÊJKˆ™XY\‹‘Ù]XÚ[X[
ŠBˆ
JNÂˆBˆB‚ˆ›Ü™XXÚ
˜\ˆÛY^H[ˆÛY^\ÊBˆÂˆ˜\ˆ^TÝ]HH]ØZ]Ù][Y\ÚY]^TÝ]\Ð\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹[Y\ÚY]YÛY^K’ÛY^Q]JNÂ‚ˆYˆ
T›Ú™XÝ[ÙLLP’\ÑY]X›U[Y\ÚY]^TÝ]\Ê^TÝ]K”Ý]\ÊJBˆÂˆÛÛ[YNÂˆB‚ˆ˜\ˆ\Ó›Û’ÛY^SX[X[[YHH˜[ÙNÂ‚ˆ]ØZ]\Ú[™È
˜\ˆX[X[ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕVTÕÈ
ˆÑSPÕBˆ”“ÓH[YWÙ[šY\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘\Ù\—ÚYH\Ù\—ÚYˆS‘ÛÜš×Ù]HHÛÜš×Ù]BˆS‘
ˆ›Ú™XÝÚYTÈ“Õ•SˆÔˆ\Ú×ÚYTÈ“Õ•SˆÔˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYTÈTÕSÕ”“ÓHÛY^WØØ]YÛÜžWÚYˆ
BˆS‘ÓÐSTÐÑJÝ]\Ë	Ù˜Y	ÊH“ÕSˆ
	ÛX[˜YÙ\—ÙXÛ[™Y	Ë	Ü™Z™XÝY	Ë	Ý›ÚYY	ÊBˆ
NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆX[X[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆX[X[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆX[X[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛY^K’ÛY^Q]JNÂˆX[X[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJšÛY^WØØ]YÛÜžWÚY‹ÛY^PØ]YÛÜžRY•˜[YJNÂ‚ˆ\Ó›Û’ÛY^SX[X[[YHHÛÛ™\•Ð›ÛÛX[Š]ØZ]X[X[ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ˜[ÙJNÂˆB‚ˆYˆ
\Ó›Û’ÛY^SX[X[[YJBˆÂˆÛÛ[YNÂˆB‚ˆ]ØZ]\Ú[™È
˜\ˆ[]PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆSUH”“ÓH[YWÙ[šY\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘\Ù\—ÚYH\Ù\—ÚYˆS‘ÛÜš×Ù]HHÛÜš×Ù]BˆS‘›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYHÛY^WØØ]YÛÜžWÚYˆS‘ÓÐSTÐÑJÝ]\Ë	Ù˜Y	ÊHSˆ
	Ù˜Y	Ë	ÛX[˜YÙ\—ÙXÛ[™Y	ÊNÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ[]PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ[]PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ[]PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛY^K’ÛY^Q]JNÂˆ[]PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJšÛY^WØØ]YÛÜžWÚY‹ÛY^PØ]YÛÜžRY•˜[YJNÂ‚ˆ]ØZ][]PÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ]ØZ]\Ú[™È
˜\ˆ[œÙ\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È[YWÙ[šY\È
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆÛÜš×Ù]KˆÝ\œËˆ[YWÝ\Kˆ\ØÜš\[Û‹ˆÝ]\ËˆÜ™X]YØ]ˆ\]YØ]ˆ
BˆSQTÈ
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆÛY^WØØ]YÛÜžWÚYˆÛÜš×Ù]KˆÝ\œËˆ	Û›Ü›X[	Ëˆ\ØÜš\[Û‹ˆ	ÜÝX›Z]Y	Ëˆ“ÕÊ
Kˆ“ÕÊ
Bˆ
NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJšÛY^WØØ]YÛÜžWÚY‹ÛY^PØ]YÛÜžRY•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛY^K’ÛY^Q]JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJšÝ\œÈ‹ÛY^K’Ý\œÈHÈŒHˆÛY^K’Ý\œÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™\ØÜš\[Ûˆ‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÛY^K’ÛY^S˜[YJHÈÛÛ\[žHÛY^HˆˆÛY^K’ÛY^S˜[YJNÂ‚ˆ]ØZ][œÙ\ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ]ØZ]X\šÕ[Y\ÚY]^TÝX›Z]Y\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹[Y\ÚY]Y\Ù\’YÛY^K’ÛY^Q]JNÂˆ]ØZ][œÙ\]Y]ÙÐ\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹\Ù\’Y[Y\ÚY]ÚÛY^WØ]]×ÜÝX›Z]Y‹[Y\ÚY]‹[Y\ÚY]Y
NÂ‚ˆÝX›Z]YÛÝ[
ÊÎÂˆB‚ˆ]ØZ]˜[œØXÝ[Û‹ÛÛ[Z]\Þ[˜Ê
NÂ‚ˆ™]\›ˆÝX›Z]YÛÝ[ÂˆBˆØ]ÚˆÂˆ]ØZ]˜[œØXÝ[Û‹”›Û˜XÚÐ\Þ[˜Ê
NÂˆ›ÝÎÂˆBŸB‹ÊˆLP—ÕSQWÑS•–WÐÔ’UPÐSÔ‘TRT—ÑS‘
‹Â‚œÝ]XÈ\Þ[˜È\ÚÏÝš[™ÏÏˆÙ][Y\ÚY]Ý]\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ÝZY\Ù\’Y]SÛ›HÙYZÔÝ\
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆÑSPÕÝ]\Âˆ”“ÓH[Y\ÚY]ÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆS‘ÙYZ×ÜÝ\Ù]HHÙYZ×ÜÝ\Ù]NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\Ù]H‹ÙYZÔÝ\
NÂ‚ˆ™]\›ˆ
Ýš[™ÏÊX]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
NÂŸB‚‚œÝ]XÈ\Þ[˜È\ÚÏÝZYˆ\Ù\˜YÚ[›Ü‘Y]X›TØ]™P\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ÝZY\Ù\’Y]SÛ›HÙYZÔÝ\
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È[Y\ÚY]È
\Ù\—ÚYÙYZ×ÜÝ\Ù]KÙYZ×Ù[™Ù]KÝ]\ËÝX›Z]YØ]
BˆSQTÈ
\Ù\—ÚYÙYZ×ÜÝ\Ù]KÙYZ×Ù[™Ù]K	Ù˜Y	Ë•S
BˆÓˆÓÓ‘“PÕ
\Ù\—ÚYÙYZ×ÜÝ\Ù]JHÈTUBˆÑUÙYZ×Ù[™Ù]HHVÓQQÙYZ×Ù[™Ù]KˆÝ]\ÈHÐTÑBˆÒSˆ[Y\ÚY]ËœÝ]\ÈSˆ
	Ù˜Y	Ë	ÛX[˜YÙ\—ÙXÛ[™Y	ÊHSˆ	Ù˜Y	ÂˆSÑH[Y\ÚY]ËœÝ]\ÂˆS‘ˆÝX›Z]YØ]HÐTÑBˆÒSˆ[Y\ÚY]ËœÝ]\ÈSˆ
	Ù˜Y	Ë	ÛX[˜YÙ\—ÙXÛ[™Y	ÊHSˆ•SˆSÑH[Y\ÚY]ËœÝX›Z]YØ]ˆS‘ˆ\]YØ]H“ÕÊ
Bˆ‘UT“’S‘È[Y\ÚY]ÚYÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\Ù]H‹ÙYZÔÝ\
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™Ù]H‹ÙYZÔÝ\Y^\ÊŠJNÂ‚ˆ™]\›ˆ
ÝZY
J]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ›ÝÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠ•[˜X›HÈÜ™X]H˜Y[Y\ÚY]Ú[ˆŠJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÈ™\XÙQY]X›U[YQ[šY\Ð\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆÝZY[Y\ÚY]YˆÝZY\Ù\’YˆT™XYÛ›S\Ý[Y\ÚY][žT™\]Y\Ýˆ[šY\ËˆÝš[™ÈÝ]\ÊBžÂˆ˜\ˆ›ÝXÝY]\ÈH™]È\ÚÙ]]SÛ›OŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ›ÝXÝYÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕÛÜš×Ù]Bˆ”“ÓH[Y\ÚY]Ù^WÜÝ]\Ù\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘Ý]\ÈSˆ
	ÜÝX›Z]Y	Ë	ÛX[˜YÙ\—Ø\›Ý™Y	Ë	ÜWØ\›Ý™Y	Ë	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊNÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ›ÝXÝYÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]›ÝXÝYÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ›ÝXÝY]\ËY
™XY\‹‘Ù]šY[˜[YO]SÛ›OŠ
JNÂˆBˆB‚ˆ]ØZ]\Ú[™È
˜\ˆ[]PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆSUH”“ÓH[YWÙ[šY\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘“ÕVTÕÈ
ˆÑSPÕBˆ”“ÓH[Y\ÚY]Ù^WÜÝ]\Ù\ÈÂˆÒT‘HË[Y\ÚY]ÚYH[YWÙ[šY\Ë[Y\ÚY]ÚYˆS‘ËÛÜš×Ù]HH[YWÙ[šY\ËÛÜš×Ù]BˆS‘ËœÝ]\ÈSˆ
	ÜÝX›Z]Y	Ë	ÛX[˜YÙ\—Ø\›Ý™Y	Ë	ÜWØ\›Ý™Y	Ë	ØXØÛÝ[[™×Ü™XYIË	Ü™XÛÛ˜Ú[Y	Ë	ÛØÚÙY	ÊBˆ
NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ[]PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ]ØZ][]PÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ˜\ˆY]X›Q[šY\ÈH[šY\Âˆ•Ú\™J[žHOˆ[žK’Ý\œÈˆ
Bˆ•Ú\™J[žHOˆ\›ÝXÝY]\ËÛÛZ[œÊ[žK•ÛÜšÑ]JJBˆ•Ó\Ý

NÂ‚ˆYˆ
Y]X›Q[šY\ËÛÝ[ˆ
BˆÂˆ]ØZ]™\XÙU[YQ[šY\Ñ›Ü‘Y]X›Q^\Ð\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹[Y\ÚY]Y\Ù\’YY]X›Q[šY\ËÝ]\ÊNÂˆBŸB‚œÝ]XÈ\Þ[˜È\ÚÈ™\XÙU[YQ[šY\Ñ›Ü‘Y]X›Q^\Ð\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆÝZY[Y\ÚY]YˆÝZY\Ù\’YˆT™XYÛ›S\Ý[Y\ÚY][žT™\]Y\Ýˆ[šY\ËˆÝš[™ÈÝ]\ÊBžÂˆ›Ü™XXÚ
˜\ˆ[žH[ˆ[šY\Ë•Ú\™J][HOˆ][K’Ý\œÈˆ
JBˆÂˆÝZYÈ›Û”›Ú™XÝØ]YÛÜžRYH[Âˆ˜\ˆš[X›HH[žK”›Ú™XÝY\È›Ý[	‰ˆ[žK•\ÚÒY\È›Ý[Â‚ˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žKØ]YÛÜžPÛÙJJBˆÂˆ›Û”›Ú™XÝØ]YÛÜžRYH]ØZ]Ù]›Û”›Ú™XÝØ]YÛÜžRY\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹[žKØ]YÛÜžPÛÙJNÂˆš[X›HH˜[ÙNÂˆB‚ˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È[YWÙ[šY\È
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Ú™XÝÚYˆ\Ú×ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ[YWÝ\KˆÛÜš×Ù]KˆÝ\œËˆ\ØÜš\[Û‹ˆš[X›KˆÝ]\ËˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆÛÜš×ÛØØ][Û—ÚYˆ
BˆSQTÈ
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Ú™XÝÚYˆ\Ú×ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ[YWÝ\KˆÛÜš×Ù]KˆÝ\œËˆ\ØÜš\[Û‹ˆš[X›KˆÝ]\ËˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆÛÜš×ÛØØ][Û—ÚYˆ
NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆ[œÙ\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›Ú™XÝÚY‹
Øš™XÝÊY[žK”›Ú™XÝYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ú×ÚY‹
Øš™XÝÊY[žK•\ÚÒYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚY‹
Øš™XÝÊ[›Û”›Ú™XÝØ]YÛÜžRYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[YWÝ\H‹[žK•[YU\JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹[žK•ÛÜšÑ]JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJšÝ\œÈ‹[žK’Ý\œÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™\ØÜš\[Ûˆ‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žK‘\ØÜš\[ÛŠHÈ“[•˜[YHˆ[žK‘\ØÜš\[Û‹•š[J
JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜š[X›H‹š[X›JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÝ]\È‹Ý]\ÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚY‹
Øš™XÝÊY[žK•ÛÜšÓØØ][Û‘Ü›Ý\YÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×ÛØØ][Û—ÚY‹
Øš™XÝÊY[žK•ÛÜšÓØØ][Û’YÏÈ“[•˜[YJNÂ‚ˆ]ØZ][œÙ\ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆBŸB‚œÝ]XÈ\Þ[˜È\ÚÏÝZYˆ\Ù\˜Y[Y\ÚY]\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ÝZY\Ù\’Y]SÛ›HÙYZÔÝ\
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È[Y\ÚY]È
\Ù\—ÚYÙYZ×ÜÝ\Ù]KÙYZ×Ù[™Ù]KÝ]\ËÝX›Z]YØ]
BˆSQTÈ
\Ù\—ÚYÙYZ×ÜÝ\Ù]KÙYZ×Ù[™Ù]K	Ù˜Y	Ë•S
BˆÓˆÓÓ‘“PÕ
\Ù\—ÚYÙYZ×ÜÝ\Ù]JHÈTUBˆÑUÙYZ×Ù[™Ù]HHVÓQQÙYZ×Ù[™Ù]KˆÝ]\ÈH	Ù˜Y	ËˆÝX›Z]YØ]H•Sˆ\]YØ]H“ÕÊ
Bˆ‘UT“’S‘È[Y\ÚY]ÚYÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\Ù]H‹ÙYZÔÝ\
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×Ù[™Ù]H‹ÙYZÔÝ\Y^\ÊŠJNÂ‚ˆ™]\›ˆ
ÝZY
J]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ›ÝÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠ•[˜X›HÈÜ™X]H˜Y[Y\ÚY]ˆŠJNÂŸB‚‚œÝ]XÈ\Þ[˜È\ÚÏ^TÝ]\Ô™XÛÜ™ˆÙ][Y\ÚY]^TÝ]\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ÝZY[Y\ÚY]Y]SÛ›HÛÜšÑ]JBžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆÑSPÕÝ]\ËÝX›Z]YØ]ˆ”“ÓH[Y\ÚY]Ù^WÜÝ]\Ù\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘ÛÜš×Ù]HHÛÜš×Ù]NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛÜšÑ]JNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
X]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™]\›ˆ™]È^TÝ]\Ô™XÛÜ™
™˜Y‹[
NÂˆB‚ˆ™]\›ˆ™]È^TÝ]\Ô™XÛÜ™
ˆ™XY\‹‘Ù]Ýš[™Ê
Kˆ™XY\‹’\Ñ“[
JHÈ[ˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠJJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÈ™\XÙQ^U[YQ[šY\Ð\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆÝZY[Y\ÚY]YˆÝZY\Ù\’Yˆ]SÛ›HÛÜšÑ]KˆT™XYÛ›S\Ý[Y\ÚY][žT™\]Y\Ýˆ[šY\ËˆÝš[™ÈÝ]\ÊBžÂˆ]ØZ]\Ú[™È
˜\ˆ[]PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
‘SUH”“ÓH[YWÙ[šY\ÈÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYS‘ÛÜš×Ù]HHÛÜš×Ù]NÈ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ[]PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ[]PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛÜšÑ]JNÂˆ]ØZ][]PÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ›Ü™XXÚ
˜\ˆ[žH[ˆ[šY\Ë•Ú\™J][HOˆ][K•ÛÜšÑ]HOHÛÜšÑ]H	‰ˆ][K’Ý\œÈˆ
JBˆÂˆÝZYÈ›Û”›Ú™XÝØ]YÛÜžRYH[Âˆ˜\ˆš[X›HH[žK”›Ú™XÝY\È›Ý[	‰ˆ[žK•\ÚÒY\È›Ý[Â‚ˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žKØ]YÛÜžPÛÙJJBˆÂˆ›Û”›Ú™XÝØ]YÛÜžRYH]ØZ]Ù]›Û”›Ú™XÝØ]YÛÜžRY\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹[žKØ]YÛÜžPÛÙJNÂˆš[X›HH˜[ÙNÂˆB‚ˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È[YWÙ[šY\È
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Ú™XÝÚYˆ\Ú×ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ[YWÝ\KˆÛÜš×Ù]KˆÝ\œËˆ\ØÜš\[Û‹ˆš[X›KˆÝ]\ËˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆÛÜš×ÛØØ][Û—ÚYˆ
BˆSQTÈ
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Ú™XÝÚYˆ\Ú×ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ[YWÝ\KˆÛÜš×Ù]KˆÝ\œËˆ\ØÜš\[Û‹ˆš[X›KˆÝ]\ËˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆÛÜš×ÛØØ][Û—ÚYˆ
NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆ[œÙ\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›Ú™XÝÚY‹
Øš™XÝÊY[žK”›Ú™XÝYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ú×ÚY‹
Øš™XÝÊY[žK•\ÚÒYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚY‹
Øš™XÝÊ[›Û”›Ú™XÝØ]YÛÜžRYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[YWÝ\H‹[žK•[YU\JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹[žK•ÛÜšÑ]JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJšÝ\œÈ‹[žK’Ý\œÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™\ØÜš\[Ûˆ‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žK‘\ØÜš\[ÛŠHÈ“[•˜[YHˆ[žK‘\ØÜš\[Û‹•š[J
JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜š[X›H‹š[X›JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÝ]\È‹Ý]\ÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚY‹
Øš™XÝÊY[žK•ÛÜšÓØØ][Û‘Ü›Ý\YÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×ÛØØ][Û—ÚY‹
Øš™XÝÊY[žK•ÛÜšÓØØ][Û’YÏÈ“[•˜[YJNÂ‚ˆ]ØZ][œÙ\ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆBŸB‚œÝ]XÈ\Þ[˜È\ÚÈX\šÕ[Y\ÚY]^TÝX›Z]Y\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ÝZY[Y\ÚY]YÝZY\Ù\’Y]SÛ›HÛÜšÑ]JBžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È[Y\ÚY]Ù^WÜÝ]\Ù\È
[Y\ÚY]ÚY\Ù\—ÚYÛÜš×Ù]KÝ]\ËÝX›Z]YØ]
BˆSQTÈ
[Y\ÚY]ÚY\Ù\—ÚYÛÜš×Ù]K	ÜÝX›Z]Y	Ë“ÕÊ
JBˆÓˆÓÓ‘“PÕ
[Y\ÚY]ÚYÛÜš×Ù]JHÈTUBˆÑUÝ]\ÈH	ÜÝX›Z]Y	ËˆÝX›Z]YØ]H“ÕÊ
Kˆ\]YØ]H“ÕÊ
NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛÜšÑ]JNÂˆ]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂŸB‚œÝ]XÈ\Þ[˜È\ÚÈ[›ØÚÕ[Y\ÚY]^P\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ÝZY[Y\ÚY]YÝZY\Ù\’Y]SÛ›HÛÜšÑ]JBžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆTUH[Y\ÚY]Ù^WÜÝ]\Ù\ÂˆÑUÝ]\ÈH	Ù˜Y	Ëˆ[›ØÚÙYØ]H“ÕÊ
Kˆ[›ØÚÙYØžWÝ\Ù\—ÚYH\Ù\—ÚYˆ\]YØ]H“ÕÊ
BˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘ÛÜš×Ù]HHÛÜš×Ù]BˆS‘Ý]\ÈH	ÜÝX›Z]Y	ÎÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛÜšÑ]JNÂˆ]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ[žPÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆ•TUH[YWÙ[šY\ÈÑUÝ]\ÈH	Ù˜Y	Ë\]YØ]H“ÕÊ
HÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYS‘ÛÜš×Ù]HHÛÜš×Ù]NÈ‹ˆÛÛ›™XÝ[Û‹ˆ˜[œØXÝ[ÛŠNÂˆ[žPÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ[žPÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹ÛÜšÑ]JNÂˆ]ØZ][žPÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂŸB‚œÝ]XÈ\Þ[˜È\ÚÈ™\XÙU[YQ[šY\Ð\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆÝZY[Y\ÚY]YˆÝZY\Ù\’YˆT™XYÛ›S\Ý[Y\ÚY][žT™\]Y\Ýˆ[šY\ËˆÝš[™ÈÝ]\ÊBžÂˆ]ØZ]\Ú[™È
˜\ˆ[]PÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆSUH”“ÓH[YWÙ[šY\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘ÛÜš×Ù]H“ÕSˆ
ˆÑSPÕÛÜš×Ù]Bˆ”“ÓH[Y\ÚY]Ù^WÜÝ]\Ù\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆS‘Ý]\ÈH	ÜÝX›Z]Y	Âˆ
NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ[]PÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ]ØZ][]PÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ›Ü™XXÚ
˜\ˆ[žH[ˆ[šY\Ë•Ú\™J][HOˆ][K’Ý\œÈˆ
JBˆÂˆÝZYÈ›Û”›Ú™XÝØ]YÛÜžRYH[Âˆ˜\ˆš[X›HH[žK”›Ú™XÝY\È›Ý[	‰ˆ[žK•\ÚÒY\È›Ý[Â‚ˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žKØ]YÛÜžPÛÙJJBˆÂˆ›Û”›Ú™XÝØ]YÛÜžRYH]ØZ]Ù]›Û”›Ú™XÝØ]YÛÜžRY\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹[žKØ]YÛÜžPÛÙJNÂˆš[X›HH˜[ÙNÂˆB‚ˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È[YWÙ[šY\È
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Ú™XÝÚYˆ\Ú×ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ[YWÝ\KˆÛÜš×Ù]KˆÝ\œËˆ\ØÜš\[Û‹ˆš[X›KˆÝ]\ËˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆÛÜš×ÛØØ][Û—ÚYˆ
BˆSQTÈ
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Ú™XÝÚYˆ\Ú×ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ[YWÝ\KˆÛÜš×Ù]KˆÝ\œËˆ\ØÜš\[Û‹ˆš[X›KˆÝ]\ËˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆÛÜš×ÛØØ][Û—ÚYˆ
NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆ[œÙ\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›Ú™XÝÚY‹
Øš™XÝÊY[žK”›Ú™XÝYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ú×ÚY‹
Øš™XÝÊY[žK•\ÚÒYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚY‹
Øš™XÝÊ[›Û”›Ú™XÝØ]YÛÜžRYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[YWÝ\H‹[žK•[YU\JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹[žK•ÛÜšÑ]JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJšÝ\œÈ‹[žK’Ý\œÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™\ØÜš\[Ûˆ‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žK‘\ØÜš\[ÛŠHÈ“[•˜[YHˆ[žK‘\ØÜš\[Û‹•š[J
JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜š[X›H‹š[X›JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÝ]\È‹Ý]\ÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚY‹
Øš™XÝÊY[žK•ÛÜšÓØØ][Û‘Ü›Ý\YÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×ÛØØ][Û—ÚY‹
Øš™XÝÊY[žK•ÛÜšÓØØ][Û’YÏÈ“[•˜[YJNÂ‚ˆ]ØZ][œÙ\ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆBŸB‚‚œÝ]XÈ\Þ[˜È\ÚÈ[œÙ\[YQ[šY\ÕÚ]Ý][][™Ð\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆÝZY[Y\ÚY]YˆÝZY\Ù\’YˆT™XYÛ›S\Ý[Y\ÚY][žT™\]Y\Ýˆ[šY\ËˆÝš[™ÈÝ]\ÊBžÂˆ›Ü™XXÚ
˜\ˆ[žH[ˆ[šY\Ë•Ú\™J][HOˆ][K’Ý\œÈˆ
JBˆÂˆÝZYÈ›Û”›Ú™XÝØ]YÛÜžRYH[Âˆ˜\ˆš[X›HH[žK”›Ú™XÝY\È›Ý[	‰ˆ[žK•\ÚÒY\È›Ý[Â‚ˆYˆ
\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žKØ]YÛÜžPÛÙJJBˆÂˆ›Û”›Ú™XÝØ]YÛÜžRYH]ØZ]Ù]›Û”›Ú™XÝØ]YÛÜžRY\Þ[˜ÊÛÛ›™XÝ[Û‹˜[œØXÝ[Û‹[žKØ]YÛÜžPÛÙJNÂˆš[X›HH˜[ÙNÂˆB‚ˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È[YWÙ[šY\È
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Ú™XÝÚYˆ\Ú×ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ[YWÝ\KˆÛÜš×Ù]KˆÝ\œËˆ\ØÜš\[Û‹ˆš[X›KˆÝ]\ËˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆÛÜš×ÛØØ][Û—ÚYˆ
BˆSQTÈ
ˆ[Y\ÚY]ÚYˆ\Ù\—ÚYˆ›Ú™XÝÚYˆ\Ú×ÚYˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ[YWÝ\KˆÛÜš×Ù]KˆÝ\œËˆ\ØÜš\[Û‹ˆš[X›KˆÝ]\ËˆÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆÛÜš×ÛØØ][Û—ÚYˆ
NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆ[œÙ\ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›Ú™XÝÚY‹
Øš™XÝÊY[žK”›Ú™XÝYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ú×ÚY‹
Øš™XÝÊY[žK•\ÚÒYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚY‹
Øš™XÝÊ[›Û”›Ú™XÝØ]YÛÜžRYÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[YWÝ\H‹[žK•[YU\JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×Ù]H‹[žK•ÛÜšÑ]JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJšÝ\œÈ‹[žK’Ý\œÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™\ØÜš\[Ûˆ‹Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[žK‘\ØÜš\[ÛŠHÈ“[•˜[YHˆ[žK‘\ØÜš\[Û‹•š[J
JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜š[X›H‹š[X›JNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÝ]\È‹Ý]\ÊNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚY‹
Øš™XÝÊY[žK•ÛÜšÓØØ][Û‘Ü›Ý\YÏÈ“[•˜[YJNÂˆ[œÙ\ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÛÜš×ÛØØ][Û—ÚY‹
Øš™XÝÊY[žK•ÛÜšÓØØ][Û’YÏÈ“[•˜[YJNÂ‚ˆ]ØZ][œÙ\ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆBŸB‚œÝ]XÈ\Þ[˜È\ÚÏÝZYˆÙ]›Û”›Ú™XÝØ]YÛÜžRY\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹Ýš[™ÈØ]YÛÜžPÛÙJBžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆÑSPÕ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆ”“ÓH›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÂˆÒT‘HØ]YÛÜžWØÛÙHHØ]YÛÜžWØÛÙBˆS‘\×ØXÝ]™HH•QNÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜Ø]YÛÜžWØÛÙH‹Ø]YÛÜžPÛÙJNÂ‚ˆ™]\›ˆ
ÝZY
J]ØZ]ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ›ÝÈ™]È[˜[YÜ\˜][Û‘^Ù\[ÛŠ	•[šÛ›ÝÛˆÜˆ[˜XÝ]™H›Û‹\›Ú™XÝ[YHØ]YÛÜžNˆØØ]YÛÜžPÛÙ_HŠJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÈX\šÕ[Y\ÚY]ÝX›Z]Y\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ÝZY[Y\ÚY]Y
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆTUH[Y\ÚY]ÂˆÑUÝ]\ÈH	ÜÝX›Z]Y	ËˆÝX›Z]YØ]H“ÕÊ
Kˆ\]YØ]H“ÕÊ
BˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂˆ]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂŸB‚œÝ]XÈ\Þ[˜È\ÚÈ[œÙ\]Y]ÙÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹œÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ÝZYXÝÜ•\Ù\’YÝš[™ÈXÝ[Û‹Ýš[™È[]U\KÝZY[]RY
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆS”ÑT•S•È]Y]ÛÙÜÈ
XÝÜ—Ý\Ù\—ÚYXÝ[Û‹[]WÝ\K[]WÚY
BˆSQTÈ
XÝÜ—Ý\Ù\—ÚYXÝ[Û‹[]WÝ\K[]WÚY
NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝÜ—Ý\Ù\—ÚY‹XÝÜ•\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝ[Ûˆ‹XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]WÝ\H‹[]U\JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]WÚY‹[]RY
NÂˆ]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏT™XYÛ›S\ÝØš™XÝˆØY›Û”›Ú™XÝØ]YÛÜšY\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[ÛŠBžÂˆ˜\ˆØ]YÛÜšY\ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆÑSPÕˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆØ]YÛÜžWØÛÙKˆØ]YÛÜžWÛ˜[YKˆØ]YÛÜžWÙ\ØÜš\[Û‹ˆ][^˜][Û—ØÛ\ÜÚYšXØ][Û‹ˆ][^˜][Û—ØXÚÙ]ˆ™\]Z\™\×Ø\›Ý˜[ˆ\×ØXÝ]™Kˆ\Ü^WÛÜ™\‚ˆ”“ÓH›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÂˆÒT‘H\×ØXÝ]™HH•QBˆÔ‘Tˆ–H\Ü^WÛÜ™\‹Ø]YÛÜžWÛ˜[YNÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂ‚ˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆØ]YÛÜšY\ËY
™]ÂˆÂˆYH™XY\‹‘Ù]ÝZY

KˆÛÙHH™XY\‹‘Ù]Ýš[™ÊJKˆ˜[YHH™XY\‹‘Ù]Ýš[™ÊŠKˆ\ØÜš\[ÛˆH™XY\‹’\Ñ“[
ÊHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊÊKˆ][^˜][ÛÛ\ÜÚYšXØ][ÛˆH™XY\‹‘Ù]Ýš[™Ê
Kˆ][^˜][ÛXÚÙ]H™XY\‹‘Ù]Ýš[™ÊJKˆ™\]Z\™\Ð\›Ý˜[H™XY\‹‘Ù]›ÛÛX[ŠŠKˆ\ÐXÝ]™HH™XY\‹‘Ù]›ÛÛX[ŠÊKˆ\Ü^SÜ™\ˆH™XY\‹‘Ù][ÌŠ
BˆJNÂˆB‚ˆ™]\›ˆØ]YÛÜšY\ÎÂŸB‚‚‚‚‚œÝ]XÈ\Þ[˜È\ÚÏT™\Ý[ˆZ[›Ú™XÝ[ÙT›ÙXÝ[Û‘]T™XY[™\ÜÔ™\Ý[\Þ[˜Ê
BžÂˆ˜\ˆÛÛ™šYÈH]X˜\ÙPÛÛ™šYË‘œ›ÛQ[š\›Û›Y[

NÂ‚ˆYˆ
ÛÛ™šYË“Z\ÜÚ[™ËÛÝ[ˆ
BˆÂˆ™]\›ˆ™\Ý[Ë˜Y™\]Y\Ý
™]ÂˆÂˆÝ]\ÈH˜ÛÛ™šYÝ\˜][Û—ÛZ\ÜÚ[™È‹ˆZ\ÜÚ[™ÈHÛÛ™šYË“Z\ÜÚ[™ËˆÙ[™\˜]Y]]ÈH]U[YSÙ™œÙ]•]Ó›ÝÂˆJNÂˆB‚ˆžBˆÂˆ]ØZ]\Ú[™È˜\ˆÛÛ›™XÝ[ÛˆH™]ÈœÜÜ[ÛÛ›™XÝ[ÛŠÛÛ™šYËÛÛ›™XÝ[Û”Ýš[™ÊNÂˆ]ØZ]ÛÛ›™XÝ[Û‹“Ü[\Þ[˜Ê
NÂ‚ˆ˜\ˆÚXÚÜÈH™]È\ÝXÝ[Û˜\žOÝš[™ËØš™XÝÏŠ
NÂ‚ˆ\Þ[˜È\ÚÈYÛÝ[ÚXÚÐ\Þ[˜ÊˆÝš[™ÈÙ^KˆÝš[™ÈX™[ˆÝš[™ÈX›S˜[YKˆ[™XYSZ[š[][KˆÝš[™È\œÜÙKˆÝš[™ÈÙXœYÙPÚXÚÊBˆÂˆ˜\ˆÛÝ[H]ØZ]]Y\žT›Ú™XÝ[ÙQ]T™XY[™\ÜÐÛÝ[\Þ[˜ÊÛÛ›™XÝ[Û‹X›S˜[YJNÂˆ˜\ˆX›Q^\ÝÈHÛÝ[’\Õ˜[YNÂˆ˜\ˆÝ]\ÈH]X›Q^\ÝÂˆÈ›Z\ÜÚ[™×ÝX›H‚ˆˆÛÝ[•˜[YHH™XYSZ[š[][BˆÈœ™XYH‚ˆˆ›™YY×Ù]HŽÂ‚ˆÚXÚÜËY
™]ÈXÝ[Û˜\žOÝš[™ËØš™XÝÏ‚ˆÂˆÈšÙ^H—HHÙ^KˆÈ›X™[—HHX™[ˆÈX›S˜[YH—HHX›S˜[YKˆÈ˜ÛÝ[—HHÛÝ[ÏÈˆÈœ™XYSZ[š[][H—HH™XYSZ[š[][KˆÈX›Q^\ÝÈ—HHX›Q^\ÝËˆÈœÝ]\È—HHÝ]\ËˆÈœ\œÜÙH—HH\œÜÙKˆÈÙXœYÙPÚXÚÈ—HHÙXœYÙPÚXÚÂˆJNÂˆB‚ˆ˜\ˆ]P\™X\ÈH™]È
Ýš[™ÈÙ^KÝš[™ÈX™[Ýš[™ÈX›S˜[YK[™XYSZ[š[][KÝš[™È\œÜÙKÝš[™ÈÙXœYÙPÚXÚÊV×BˆÂˆ
\Ù\œÈ‹•\Ù\œÈ‹˜\Ý\Ù\œÈ‹KÛÛ™š\›\È™X[\Ù\œÈ^\Ý›ÜˆÙÚ[‹›ÛH\ÜÚYÛ›Y[\›Ý˜[Ë[™ÛÜšÙ›ÝÈÝÛ™\œÚ\ˆ‹“Ü[ˆ\Ù\ˆYZ[š\Ý˜][ÛˆÜˆ›ÛHYZ[š\Ý˜][Ûˆ[™ÛÛ™š\›H\Ù\œÈ\™H™\Ù[ˆŠKˆ
œ›Û\È‹”›Û\È‹˜\Ü›Û\È‹KÛÛ™š\›\È\XØ][Ûˆ›Û\È^\Ý›Üˆ›ÛKX˜\ÙYXØÙ\ÜÈ[™\Ú›Ø\™Û[Ù[Hš\ÚXš[]Kˆ‹“Ü[ˆ›ÛHÈÙXÝ\š]HYZ[š\Ý˜][Ûˆ[™ÛÛ™š\›H›Û\È[™\›Z\ÜÚ[ÛœÈ\™Hš\ÚX›KˆŠKˆ
˜Ý\ÝÛY\œÈ‹Ý\ÝÛY\œÈ‹˜ÛY[È‹KÛÛ™š\›\ÈÝ\ÝÛY\‹ØXØÛÝ[]H^\ÝÈ›Üˆ›Ú™XÝ[ZÙK[ØØ][Û‹š[[™Ë[™™\Ü[™Ëˆ‹“Ü[ˆÝ\ÝÛY\ˆ\™XÝÜžH[™ÛÛ™š\›HÝ\ÝÛY\ˆ™XÛÜ™ÈÜˆHÛX\ˆ[\HÝ]H\X\œËˆŠKˆ
œ›Ú™XÝÈ‹”›Ú™XÝÈ‹œ›Ú™XÝÈ‹KÛÛ™š\›\È›Ú™XÝ™XÛÜ™È^\Ý›Üˆ[Y\ÚY]Ë›Ú™XÝÛÜšÜÜXÙK™\ÛÝ\˜ÙH\ÜÚYÛ›Y[[™ÛÜšÛØY™\Ü[™Ëˆ‹“Ü[ˆ›Ú™XÝÛÜšÜÜXÙHÜˆ™\ÛÝ\˜ÙH\ÜÚYÛ›Y[[™ÛÛ™š\›H›Ú™XÝ]H\Èš\ÚX›KˆŠKˆ
œ›Ú™XÝÝ\ÚÜÈ‹”›Ú™XÝ\ÚÜÈ‹œ›Ú™XÝÝ\ÚÜÈ‹KÛÛ™š\›\È\ÚË[]™[ÛÜšÈ\È]˜Z[X›H›Üˆ[YH[žK\ÜÚYÛ›Y[\›Ý˜[Ë[™^ÜËˆ‹“Ü[ˆ›Ú™XÝÛÜšÜÜXÙH[™ÛÛ™š\›H›Ú™XÝ\ÚÜÈ\™H]˜Z[X›HÜˆ[\HÝ]H\È[™\œÝ[™X›KˆŠKˆ
[Y\ÚY]È‹•[Y\ÚY]È‹[Y\ÚY]È‹KÛÛ™š\›\È[Y\ÚY]XY\œÈ^\Ý›ÜˆÙYZÛH[YH[žH[™X[˜YÙ\ˆ\›Ý˜[ÛÜšÙ›ÝÜËˆ‹“Ü[ˆ[Y\ÚY]ÜˆX[˜YÙ\ˆ\›Ý˜[È[™ÛÛ™š\›H[YHÛÜšÙ›ÝÈ]HØYËˆŠKˆ
[YWÙ[šY\È‹•[YH[šY\È‹[YWÙ[šY\È‹KÛÛ™š\›\ÈÝX›Z]YÜˆ˜Y[YH]H^\ÝÈ›Üˆ\›Ý˜[Ë^ÜË][^˜][Û‹[™]Y]]šY[˜ÙKˆ‹“Ü[ˆÛÜšÙ›ÝÈÜˆX[˜YÙ\ˆ\›Ý˜[È[™ÛÛ™š\›H[YH]H\X\œÈÚ[ˆ^XÝYˆŠKˆ
›X[˜YÙ\—Ø\›Ý˜[È‹“X[˜YÙ\ˆ\›Ý˜[]šY[˜ÙH‹›X[˜YÙ\—Ø\›Ý˜[ØXÝ[ÛœÈ‹KÛÛ™š\›\È\›Ý˜[XÚ\Ú[Ûˆ]šY[˜ÙH^\ÝÈÜˆØ[ˆ™H˜XÚÙY›Üˆ]Y][™^Ü™XY[™\ÜËˆ‹“Ü[ˆX[˜YÙ\ˆ\›Ý˜[È[™ÛÛ™š\›H\›Ý˜[ÛÜšÙ›ÝÈÝ]H\È[™\œÝ[™X›KˆŠKˆ
™^ÜÈ‹‘^ÜXÚØYÙ\È‹[YWÙ^ÜÜXÚØYÙ\È‹KÛÛ™š\›\È^ÜXÚØYÙH]šY[˜ÙH^\ÝÈ›ÜˆXØÛÝ[[™È[™\š[ÙXÛÜÙHÛÜšÙ›ÝÜËˆ‹“Ü[ˆ\›Ý˜[È^ÜÈ]Y]ÛÜšÙ›ÝÜÈ[™ÛÛ™š\›H^Ü™XY[™\ÜÈ\Èš\ÚX›KˆŠKˆ
˜]Y]Ù]™[È‹]Y]]™[È‹˜]Y]Ù]™[È‹KÛÛ™š\›\ÈÞ\Ý[HXÝ[ÛœÈ\™H™Z[™ÈÙÙÙY›ÜˆXØÛÝ[Xš[]H[™›ÝX›\ÚÛÝ[™Ëˆ‹“Ü[ˆ]Y]\ÝÜžH[™ÛÛ™š\›H]Y]™XÛÜ™ÈÜˆHÛX\ˆ[\HÝ]H\X\œËˆŠKˆ
››ÝYšXØ][Û—Ù]™[È‹“›ÝYšXØ][Ûˆ]™[È‹››ÝYšXØ][Û—Ù]™[È‹KÛÛ™š\›\È›ÝYšXØ][Ûˆ]šY[˜ÙH\È]˜Z[X›H›Üˆ[YHÛÛ\X[˜ÙH[™Ü\˜][Û˜[Y\ÜØYÚ[™Ëˆ‹“Ü[ˆ›ÝYšXØ][Û‹\™[]YYÙ\È[™ÛÛ™š\›H]™[È\™Hš\ÚX›HY\ˆ›ÝYšXØ][ÛˆXÝ]š]KˆŠBˆNÂ‚ˆ›Ü™XXÚ
˜\ˆ\™XH[ˆ]P\™X\ÊBˆÂˆ]ØZ]YÛÝ[ÚXÚÐ\Þ[˜Êˆ\™XK’Ù^Kˆ\™XK“X™[ˆ\™XK•X›S˜[YKˆ\™XK”™XYSZ[š[][Kˆ\™XK”\œÜÙKˆ\™XK•ÙXœYÙPÚXÚÊNÂˆB‚ˆ˜\ˆ™XYPÛÝ[HÚXÚÜËÛÝ[
ÚXÚÈOˆÝš[™Ë‘\]X[ÊÛÛ™\•ÔÝš[™ÊÚXÚÖÈœÝ]\È—JKœ™XYH‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJNÂˆ˜\ˆ™YYÑ]PÛÝ[HÚXÚÜËÛÝ[
ÚXÚÈOˆÝš[™Ë‘\]X[ÊÛÛ™\•ÔÝš[™ÊÚXÚÖÈœÝ]\È—JK›™YY×Ù]H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJNÂˆ˜\ˆZ\ÜÚ[™ÕX›PÛÝ[HÚXÚÜËÛÝ[
ÚXÚÈOˆÝš[™Ë‘\]X[ÊÛÛ™\•ÔÝš[™ÊÚXÚÖÈœÝ]\È—JK›Z\ÜÚ[™×ÝX›H‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJNÂ‚ˆ™]\›ˆ™\Ý[Ë“ÚÊ™]ÂˆÂˆÝ]\ÈHZ\ÜÚ[™ÕX›PÛÝ[OH	‰ˆ™YYÑ]PÛÝ[OHÈœ™XYHˆˆ›™YY×Ù]WÜ™]šY]È‹ˆÙ[™\˜]Y]]ÈH]U[YSÙ™œÙ]•]Ó›ÝËˆ›Ý]HH‹Ø\KÜ›ÙXÝ[Û‹Y]K\™XY[™\ÜÈ‹ˆš[X\žT›Ý]HH‹Ø\KÜ›ÙXÝ[Û‹Ù]K\™XY[™\ÜÈ‹ˆÝ[[X\žHH™]ÂˆÂˆÚXÚÐÛÝ[HÚXÚÜËÛÝ[ˆ™XYPÛÝ[ˆ™YYÑ]PÛÝ[ˆZ\ÜÚ[™ÕX›PÛÝ[ˆ›ÙXÝ[Û‘]T™XYHHZ\ÜÚ[™ÕX›PÛÝ[OH	‰ˆ™YYÑ]PÛÝ[OHˆKˆÚXÚÜÂˆJNÂˆBˆØ]Ú
^Ù\[Ûˆ^
BˆÂˆ™]\›ˆ™\Ý[Ë”›Ø›[Jˆ]Nˆ”›ÙXÝ[Ûˆ]H™XY[™\ÜÈ˜Z[Y‹ˆ]Z[ˆ^“Y\ÜØYÙKˆÝ]\ÐÛÙNˆÝ]\ÐÛÙ\Ë”Ý]\ÍL[\›˜[Ù\™\‘\œ›ÜŠNÂˆBŸB‚‚œÝ]XÈ\Þ[˜È\ÚÏÛ™ÏÏˆ]Y\žT›Ú™XÝ[ÙQ]T™XY[™\ÜÐÛÝ[\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹Ýš[™ÈX›S˜[YJBžÂˆ]ØZ]\Ú[™È
˜\ˆ^\ÝÐÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕVTÕÈ
ˆÑSPÕBˆ”“ÓH[™›Ü›X][Û—ÜØÚ[XKX›\ÂˆÒT‘HX›WÜØÚ[XHH	ÜX›XÉÂˆS‘X›WÛ˜[YHHX›WÛ˜[YBˆ
NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠJBˆÂˆ^\ÝÐÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJX›WÛ˜[YH‹X›S˜[YJNÂˆ˜\ˆ^\ÝÈH
›ÛÛ
J]ØZ]^\ÝÐÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ˜[ÙJNÂ‚ˆYˆ
Y^\ÝÊBˆÂˆ™]\›ˆ[ÂˆBˆB‚ˆ˜\ˆØY™UX›S˜[YHHX›S˜[YK”™\XÙJ—ˆ‹——ˆŠNÂˆ]ØZ]\Ú[™È˜\ˆÛÝ[ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
	”ÑSPÕÓÕS•

ŠH”“ÓHžÜØY™UX›S˜[Y_WŽÈ‹ÛÛ›™XÝ[ÛŠNÂˆ™]\›ˆÛÛ™\•Ò[
]ØZ]ÛÝ[ÛÛ[X[™‘^XÝ]TØØ[\\Þ[˜Ê
HÏÈ
NÂŸB‚‚œÝ]XÈ\Þ[˜È\ÚÏØš™XÝˆZ[[Y\ÚY]ÙYZÔ^[ØY\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y]SÛ›HÝ\
BžÂˆ˜\ˆ^\ÈH[[Y\˜X›K”˜[™ÙJÊBˆ”Ù[XÝ
Ù™œÙ]OˆÝ\Y^\ÊÙ™œÙ]
JBˆ”Ù[XÝ
]HOˆ™]ÂˆÂˆ]Kˆ^S˜[YHH]K‘^SÙ•ÙYZË•ÔÝš[™Ê
Kˆ›Ü›X[Ý\œÈHŒKˆY\šÝ\œÈHŒBˆJBˆ•Ó\Ý

NÂ‚ˆ˜\ˆØ]YÛÜšY\ÈH]ØZ]ØY[Y\ÚY]›Û”›Ú™XÝØ]YÛÜšY\Ð\Þ[˜ÊÛÛ›™XÝ[ÛŠNÂˆ˜\ˆ[Y\ÚY]H]ØZ]ØY[Y\ÚY]XY\\Þ[˜ÊÛÛ›™XÝ[Û‹\Ù\’YÝ\
NÂˆ˜\ˆ[šY\ÈH[Y\ÚY]Ë•[Y\ÚY]Y\È[ˆÈ™]È\ÝØš™XÝŠ
Bˆˆ]ØZ]ØYØ]™Y[YQ[šY\Ð\Þ[˜ÊÛÛ›™XÝ[Û‹[Y\ÚY]•[Y\ÚY]Y•˜[YJNÂˆ˜\ˆ^TÝ]\Ù\ÈH]ØZ]ØY^TÝ]\Ù\Ð\Þ[˜ÊÛÛ›™XÝ[Û‹[Y\ÚY]Ë•[Y\ÚY]YÝ\
NÂ‚ˆ™]\›ˆ™]ÂˆÂˆ[Y\ÚY]YH[Y\ÚY]Ë•[Y\ÚY]YˆÝ]\ÈH[Y\ÚY]Ë”Ý]\ÈÏÈ™˜Y‹ˆÝX›Z]Y]H[Y\ÚY]Ë”ÝX›Z]Y]ˆ^TÝ]\Ù\ËˆÙYZÔÝ\HÝ\ˆÙYZÑ[™HÝ\Y^\ÊŠKˆ^\Ëˆ[YU\\ÈH™]Ö×HÈ››Ü›X[‹˜Y\šÝ\œÈˆKˆ›Û”›Ú™XÝØ]YÛÜšY\ÈHØ]YÛÜšY\Ëˆ[šY\Ëˆ›ÝHH•ÙYZÛHÚ[›ÝÈ[˜ÛY\ÈØ]™Y˜Y[™ÝX›Z]Y[YH[žH^[ØYËˆ‚ˆNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏT™XYÛ›S\ÝØš™XÝˆØY[Y\ÚY]›Û”›Ú™XÝØ]YÛÜšY\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[ÛŠBžÂˆ˜\ˆØ]YÛÜšY\ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆÛÛœÝÝš[™ÈØ]YÛÜžTÜ[Hˆˆ‚ˆÑSPÕˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆØ]YÛÜžWØÛÙKˆØ]YÛÜžWÛ˜[YKˆØ]YÛÜžWÙ\ØÜš\[Û‹ˆ][^˜][Û—ØXÚÙ]ˆ™\]Z\™\×Ø\›Ý˜[ˆ”“ÓH›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÂˆÒT‘H\×ØXÝ]™HH•QBˆÔ‘Tˆ–H\Ü^WÛÜ™\‹Ø]YÛÜžWÛ˜[YNÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ø]YÛÜžTÜ[ÛÛ›™XÝ[ÛŠNÂˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂ‚ˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆØ]YÛÜšY\ËY
™]ÂˆÂˆØ]YÛÜžRYH™XY\‹‘Ù]ÝZY

KˆÛÙHH™XY\‹‘Ù]Ýš[™ÊJKˆ˜[YHH™XY\‹‘Ù]Ýš[™ÊŠKˆ\ØÜš\[ÛˆH™XY\‹’\Ñ“[
ÊHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊÊKˆ][^˜][ÛXÚÙ]H™XY\‹‘Ù]Ýš[™Ê
Kˆ™\]Z\™\Ð\›Ý˜[H™XY\‹‘Ù]›ÛÛX[ŠJBˆJNÂˆB‚ˆ™]\›ˆØ]YÛÜšY\ÎÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ[Y\ÚY]XY\ÏˆØY[Y\ÚY]XY\\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y]SÛ›HÙYZÔÝ\
BžÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆÑSPÕ[Y\ÚY]ÚYÝ]\ËÝX›Z]YØ]ˆ”“ÓH[Y\ÚY]ÂˆÒT‘H\Ù\—ÚYH\Ù\—ÚYˆS‘ÙYZ×ÜÝ\Ù]HHÙYZ×ÜÝ\Ù]NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJÙYZ×ÜÝ\Ù]H‹ÙYZÔÝ\
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
X]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™]\›ˆ[ÂˆB‚ˆ™]\›ˆ™]È[Y\ÚY]XY\Šˆ™XY\‹‘Ù]ÝZY

Kˆ™XY\‹‘Ù]Ýš[™ÊJKˆ™XY\‹’\Ñ“[
ŠHÈ[ˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠŠJNÂŸB‚‚œÝ]XÈ\Þ[˜È\ÚÏ\ÝØš™XÝˆØY^TÝ]\Ù\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZYÈ[Y\ÚY]Y]SÛ›HÙYZÔÝ\
BžÂˆ˜\ˆÝ]\ÐžQ]HH™]ÈXÝ[Û˜\žO]SÛ›K^TÝ]\Ô™XÛÜ™Š
NÂ‚ˆYˆ
[Y\ÚY]Y\È›Ý[
BˆÂˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆÑSPÕÛÜš×Ù]KÝ]\ËÝX›Z]YØ]ˆ”“ÓH[Y\ÚY]Ù^WÜÝ]\Ù\ÂˆÒT‘H[Y\ÚY]ÚYH[Y\ÚY]ÚYˆÔ‘Tˆ–HÛÜš×Ù]NÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y•˜[YJNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆÝ]\ÐžQ]VÜ™XY\‹‘Ù]šY[˜[YO]SÛ›OŠ
WHH™]È^TÝ]\Ô™XÛÜ™
ˆ™XY\‹‘Ù]Ýš[™ÊJKˆ™XY\‹’\Ñ“[
ŠHÈ[ˆ™XY\‹‘Ù]šY[˜[YO]U[YSÙ™œÙ]ŠŠJNÂˆBˆB‚ˆ™]\›ˆ[[Y\˜X›K”˜[™ÙJÊBˆ”Ù[XÝ
Ù™œÙ]OˆÙYZÔÝ\Y^\ÊÙ™œÙ]
JBˆ”Ù[XÝ
]HO‚ˆÂˆÝ]\ÐžQ]K•žQÙ]˜[YJ]KÝ]˜\ˆ™XÛÜ™
NÂˆ˜\ˆÝ]\ÈH™XÛÜ™Ë”Ý]\ÈÏÈ™˜YŽÂˆ˜\ˆÝX›Z]Y]H™XÛÜ™Ë”ÝX›Z]Y]Â‚ˆ™]\›ˆ
Øš™XÝ
[™]ÂˆÂˆÛÜšÑ]HH]KˆÝ]\ËˆÝX›Z]Y]ˆØ[‘Y]HÝ]\È\È™˜YˆÜˆ›X[˜YÙ\—ÙXÛ[™Y‹ˆØ[•[›ØÚÈHØ[‘[™Ú[™Y\•[›ØÚÑ^JÝ]\ËÝX›Z]Y]
Kˆ[›ØÚÓY\ÜØYÙHHÙ]^U[›ØÚÓY\ÜØYÙJÝ]\ËÝX›Z]Y]
BˆNÂˆJBˆ•Ó\Ý

NÂŸB‚‚œÝ]XÈ\Þ[˜È\ÚÏ\ÝØš™XÝˆØYØ]™Y[YQ[šY\Ð\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY[Y\ÚY]Y
BžÂˆ˜\ˆ[šY\ÈH™]È\ÝØš™XÝŠ
NÂ‚ˆÛÛœÝÝš[™ÈÜ[Hˆˆ‚ˆÑSPÕˆK[YWÙ[žWÚYˆKÛÜš×Ù]KˆK[YWÝ\KˆKšÝ\œËˆK™\ØÜš\[Û‹ˆKœÝ]\ËˆKœ›Ú™XÝÚYˆK\Ú×ÚYˆK››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆœ˜Ø]YÛÜžWØÛÙKˆœ˜Ø]YÛÜžWÛ˜[YKˆKÛÜš×ÛØØ][Û—ÙÜ›Ý\ÚYˆKÛÜš×ÛØØ][Û—ÚYˆK˜š[X›Kˆœ›Ú™XÝØÛÙKˆœ›Ú™XÝÛ˜[YKˆ\Ú×ØÛÙKˆ\Ú×Û˜[YKˆË˜ÛY[Û˜[YKˆÓÐSTÐÑJˆ•SQŠ×ÚœÛÛ˜Š
KO‰ÝÛÜš×Ý\Ú×ØØ]YÛÜžIË	ÉÊKˆ•SQŠ×ÚœÛÛ˜Š
KO‰ÝÛÜš×Ý\IË	ÉÊKˆ	Ü›Ú™XÝÝ\ÚÉÂˆ
HTÈÛÜš×Ý\Ú×ØØ]YÛÜžKˆÓÐSTÐÑJ•SQŠ×ÚœÛÛ˜Š
KO‰ÜÙ\šXÙWÜ™\]Y\ÝÛ[X™\‰Ë	ÉÊK	ÉÊHTÈÙ\šXÙWÜ™\]Y\ÝÛ[X™\‚ˆ”“ÓH[YWÙ[šY\ÈBˆQ•“ÒSˆ›Û—Ü›Ú™XÝÝ[YWØØ]YÛÜšY\ÈœˆÓˆœ››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYHK››Û—Ü›Ú™XÝÝ[YWØØ]YÛÜžWÚYˆQ•“ÒSˆ›Ú™XÝÈˆÓˆœ›Ú™XÝÚYHKœ›Ú™XÝÚYˆQ•“ÒSˆ›Ú™XÝÝ\ÚÜÈˆÓˆ\Ú×ÚYHK\Ú×ÚYˆQ•“ÒSˆÛY[ÈÂˆÓˆË˜ÛY[ÚYH˜ÛY[ÚYˆÒT‘HK[Y\ÚY]ÚYH[Y\ÚY]ÚYˆÔ‘Tˆ–HKÛÜš×Ù]KK[YWÝ\KÓÐSTÐÑJœ™\Ü^WÛÜ™\‹NNJKÓÐSTÐÑJœ˜Ø]YÛÜžWÛ˜[YK\Ú×Û˜[YKœ›Ú™XÝÛ˜[YJNÂˆˆˆŽÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
Ü[ÛÛ›™XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ[Y\ÚY]ÚY‹[Y\ÚY]Y
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ˜\ˆ›Ú™XÝYH™XY\‹’\Ñ“[
ŠHÈ
ÝZYÊ[[ˆ™XY\‹‘Ù]ÝZY
ŠNÂˆ˜\ˆ\ÚÒYH™XY\‹’\Ñ“[
ÊHÈ
ÝZYÊ[[ˆ™XY\‹‘Ù]ÝZY
ÊNÂˆ˜\ˆØ]YÛÜžPÛÙHH™XY\‹’\Ñ“[
JHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊJNÂˆ˜\ˆÛÜšÕ\ÚÐØ]YÛÜžHH™XY\‹‘Ù]Ýš[™ÊNJNÂˆ˜\ˆÙ\šXÙT™\]Y\Ý[X™\ˆH™XY\‹‘Ù]Ýš[™ÊŒ
NÂˆ˜\ˆ\ÔÙ\šXÙT™\]Y\ÝH›Ú™XÝY\È›Ý[ˆ	‰ˆ\ÚÒY\È›Ý[ˆ	‰ˆ
Ýš[™Ë‘\]X[ÊˆÛÜšÕ\ÚÐØ]YÛÜžK•š[J
KˆœÙ\šXÙWÜ™\]Y\ÝÝ\ÚÈ‹ˆÝš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÙ\šXÙT™\]Y\Ý[X™\ŠJNÂ‚ˆ[šY\ËY
™]ÂˆÂˆYH™XY\‹‘Ù]ÝZY

Kˆ[YQ[žRYH™XY\‹‘Ù]ÝZY

Kˆ›ÝÕ\HH\ÔÙ\šXÙT™\]Y\ÝˆÈœÙ\šXÙWÜ™\]Y\Ý‚ˆˆ›Ú™XÝY\È›Ý[	‰ˆ\ÚÒY\È›Ý[Èœ›Ú™XÝ\ÚÈˆˆ››Û”›Ú™XÝ‹ˆÛÜšÑ]HH™XY\‹‘Ù]šY[˜[YO]SÛ›OŠJKˆ[YU\HH™XY\‹‘Ù]Ýš[™ÊŠKˆÝ\œÈH™XY\‹‘Ù]XÚ[X[
ÊKˆ\ØÜš\[ÛˆH™XY\‹’\Ñ“[

HÈ[ˆ™XY\‹‘Ù]Ýš[™Ê
KˆÝ]\ÈH™XY\‹‘Ù]Ýš[™ÊJKˆ›Ú™XÝYˆ\ÚÒYˆ›Û”›Ú™XÝ[YPØ]YÛÜžRYH™XY\‹’\Ñ“[

HÈ
ÝZYÊ[[ˆ™XY\‹‘Ù]ÝZY

KˆØ]YÛÜžPÛÙKˆØ]YÛÜžS˜[YHH™XY\‹’\Ñ“[
L
HÈ[ˆ™XY\‹‘Ù]Ýš[™ÊL
KˆÛÜšÓØØ][Û‘Ü›Ý\YH™XY\‹’\Ñ“[
LJHÈ
ÝZYÊ[[ˆ™XY\‹‘Ù]ÝZY
LJKˆÛÜšÓØØ][Û’YH™XY\‹’\Ñ“[
LŠHÈ
ÝZYÊ[[ˆ™XY\‹‘Ù]ÝZY
LŠKˆš[X›HH™XY\‹‘Ù]›ÛÛX[ŠLÊKˆ›Ú™XÝÛÙHH™XY\‹’\Ñ“[
M
HÈ[ˆ™XY\‹‘Ù]Ýš[™ÊM
Kˆ›Ú™XÝ˜[YHH™XY\‹’\Ñ“[
MJHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊMJKˆ\ÚÐÛÙHH™XY\‹’\Ñ“[
MŠHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊMŠKˆ\ÚÓ˜[YHH™XY\‹’\Ñ“[
MÊHÈ[ˆ™XY\‹‘Ù]Ýš[™ÊMÊKˆÛY[˜[YHH™XY\‹’\Ñ“[
N
HÈ[ˆ™XY\‹‘Ù]Ýš[™ÊN
KˆÛÜšÕ\ÚÐØ]YÛÜžKˆÙ\šXÙT™\]Y\Ý[X™\‚ˆJNÂˆB‚ˆ™]\›ˆ[šY\ÎÂŸB‚‚‚‚‚‚‚œÝ]XÈXÝ[Û˜\žOÝš[™ËÝš[™Ïˆ™XY›Ú™XÝ[ÙQ[‘š[JÝš[™È]
BžÂˆ˜\ˆ˜[Y\ÈH™]ÈXÝ[Û˜\žOÝš[™ËÝš[™ÏŠÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂ‚ˆYˆ
Qš[K‘^\ÝÊ]
JH™]\›ˆ˜[Y\ÎÂ‚ˆ›Ü™XXÚ
˜\ˆ˜]Ó[™H[ˆš[K”™XY[[™\Ê]
JBˆÂˆ˜\ˆ[™HH˜]Ó[™K•š[J
NÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ[™JH[™K”Ý\ÕÚ]
ˆÈ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
JHÛÛ[YNÂ‚ˆ˜\ˆ[™^H[™K’[™^ÙŠ	ÏIÊNÂˆYˆ
[™^H
HÛÛ[YNÂ‚ˆ˜\ˆÙ^HH[™VË‹š[™^K•š[J
NÂˆ˜\ˆ˜[YHH[™VÊ[™^
ÈJK‹—K•š[J
NÂ‚ˆYˆ
˜[YK“[™ÝHˆ	‰ˆ˜[YK”Ý\ÕÚ]
‰È‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
H	‰ˆ˜[YK‘[™ÕÚ]
‰È‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆÂˆ˜[YHH˜[YVÌK‹—ŒWK”™\XÙJ‰×‰×‰È‹‰ÈŠNÂˆBˆ[ÙHYˆ
˜[YK“[™ÝHˆ	‰ˆ˜[YK”Ý\ÕÚ]
—ˆ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
H	‰ˆ˜[YK‘[™ÕÚ]
—ˆ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[
JBˆÂˆ˜[YHH˜[YVÌK‹—ŒWK”™\XÙJ—ˆ‹—ˆŠNÂˆB‚ˆ˜[Y\ÖÚÙ^WHH˜[YNÂˆB‚ˆ™]\›ˆ˜[Y\ÎÂŸB‚œÝ]XÈÝš[™È][ÝT›Ú™XÝ[ÙQ[•˜[YJÝš[™ÏÈ˜[YJBžÂˆ™]\›ˆ‰Èˆ
È
˜[YHÏÈˆŠK”™\XÙJ‰È‹‰×‰×‰ÈŠH
È‰ÈŽÂŸB‚œÝ]XÈÝš[™ÈX\ÚÔ›Ú™XÝ[ÙTÙXÜ™]
Ýš[™ÏÈ˜[YJBžÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ˜[YJJH™]\›ˆˆŽÂ‚ˆ˜\ˆš[[YYH˜[YK•š[J
NÂˆYˆ
š[[YY“[™ÝH
H™]\›ˆ˜ÛÛ™šYÝ\™YŽÂ‚ˆ™]\›ˆ	žÝš[[YYË‹_K‹‹žÝš[[YY×‹—_HŽÂŸB‚œÝ]XÈXÝ[Û˜\žOÝš[™ËÝš[™Ïˆ\œÙTÞ\Ý[XÝÚÝÔ›Ü\Y\ÊÝš[™ÈÝ]]
BžÂˆ™]\›ˆÝ]]ˆ”Ü]
	×‰ËÝš[™ÔÜ]Ü[ÛœË”™[[Ý™Q[\Q[šY\ÈÝš[™ÔÜ]Ü[ÛœË•š[Q[šY\ÊBˆ”Ù[XÝ
[™HOˆ[™K”Ü]
	ÏIËŠJBˆ•Ú\™J\ÈOˆ\Ë“[™ÝOHŠBˆ•ÑXÝ[Û˜\žJ\ÈOˆ\ÖÌK\ÈOˆ\ÖÌWJNÂŸB‚œÝ]XÈ\Þ[˜È\ÚÏ›Ú™XÝ[ÙT›ØÙ\ÜÔ™\Ý[ˆ[”›Ú™XÝ[ÙT›ØÙ\ÜÐ\Þ[˜ÊÝš[™Èš[S˜[YK\˜[\ÈÝš[™Ö×H\™Ý[Y[ÊBžÂˆ\Ú[™È˜\ˆ›ØÙ\ÜÈH™]ÈÞ\Ý[K‘XYÛ›ÜÝXÜË”›ØÙ\ÜÊ
NÂ‚ˆ›ØÙ\ÜË”Ý\[™›Ë‘š[S˜[YHHš[S˜[YNÂˆ›ØÙ\ÜË”Ý\[™›Ë”™Y\™XÝÝ[™\™Ý]]HYNÂˆ›ØÙ\ÜË”Ý\[™›Ë”™Y\™XÝÝ[™\™\œ›ÜˆHYNÂˆ›ØÙ\ÜË”Ý\[™›Ë•\ÙTÚ[^XÝ]HH˜[ÙNÂˆ›ØÙ\ÜË”Ý\[™›ËÜ™X]S›ÕÚ[™ÝÈHYNÂ‚ˆ›Ü™XXÚ
˜\ˆ\™Ý[Y[[ˆ\™Ý[Y[ÊBˆÂˆ›ØÙ\ÜË”Ý\[™›Ë\™Ý[Y[\ÝY
\™Ý[Y[
NÂˆB‚ˆžBˆÂˆ›ØÙ\ÜË”Ý\

NÂ‚ˆ˜\ˆÝ[™\™Ý]]\ÚÈH›ØÙ\ÜË”Ý[™\™Ý]]”™XYÑ[™\Þ[˜Ê
NÂˆ˜\ˆÝ[™\™\œ›Ü•\ÚÈH›ØÙ\ÜË”Ý[™\™\œ›Ü‹”™XYÑ[™\Þ[˜Ê
NÂ‚ˆ˜\ˆ[Y[Ý]H\™Ý[Y[Ë[žJ\™Ý[Y[Oˆ\™Ý[Y[ÛÛZ[œÊœ›Ú™XÝ[ÙKX˜XÚÝ\œÚ‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÈ[YTÜ[‹‘œ›ÛSZ[]\ÊL
Bˆˆ\™Ý[Y[Ë[žJ\™Ý[Y[OˆÝš[™Ë‘\]X[Ê\™Ý[Y[œ™\Ý\‹Ýš[™ÐÛÛ\\š\ÛÛ‹“Ü™[˜[YÛ›Ü™PØ\ÙJJBˆÈ[YTÜ[‹‘œ›ÛTÙXÛÛ™ÊŒ
Bˆˆ[YTÜ[‹‘œ›ÛTÙXÛÛ™ÊŒ
NÂ‚ˆžBˆÂˆ]ØZ]›ØÙ\ÜË•ØZ]›Ü‘^]\Þ[˜Ê
K•ØZ]\Þ[˜Ê[Y[Ý]
NÂˆBˆØ]Ú
[Y[Ý]^Ù\[ÛŠBˆÂˆžBˆÂˆ›ØÙ\ÜË’Ú[
[\™T›ØÙ\ÜÕ™YNˆYJNÂˆBˆØ]ÚˆÂˆËÈ™\ÝYY™›ÜÛX[\Û›K‚ˆB‚ˆ˜\ˆ[YYÝ]Ý]]HÝš[™Ë‘[\NÂˆ˜\ˆ[YYÝ]\œ›ÜˆHÝš[™Ë‘[\NÂ‚ˆžBˆÂˆ[YYÝ]Ý]]H]ØZ]Ý[™\™Ý]]\ÚË•ØZ]\Þ[˜Ê[YTÜ[‹‘œ›ÛTÙXÛÛ™ÊJJNÂˆBˆØ]ÚˆÂˆËÈYÛ›Ü™H[˜ÛÛ\]HÝ]]Y\ˆ[Y[Ý]‚ˆB‚ˆžBˆÂˆ[YYÝ]\œ›ÜˆH]ØZ]Ý[™\™\œ›Ü•\ÚË•ØZ]\Þ[˜Ê[YTÜ[‹‘œ›ÛTÙXÛÛ™ÊJJNÂˆBˆØ]ÚˆÂˆËÈYÛ›Ü™H[˜ÛÛ\]H\œ›ÜˆY\ˆ[Y[Ý]‚ˆB‚ˆ™]\›ˆ™]È›Ú™XÝ[ÙT›ØÙ\ÜÔ™\Ý[
ˆLˆ[YYÝ]Ý]]•š[J
KˆÝš[™Ë’\Ó[Ü•Ú]TÜXÙJ[YYÝ]\œ›ÜŠBˆÈ	[YYÝ]Y\ˆÝ[Y[Ý]•Ý[ÙXÛÛ™ÎŒHÙXÛÛ™È‚ˆˆ[YYÝ]\œ›Ü‹•š[J
JNÂˆB‚ˆ™]\›ˆ™]È›Ú™XÝ[ÙT›ØÙ\ÜÔ™\Ý[
ˆ›ØÙ\ÜË‘^]ÛÙKˆ
]ØZ]Ý[™\™Ý]]\ÚÊK•š[J
Kˆ
]ØZ]Ý[™\™\œ›Ü•\ÚÊK•š[J
JNÂˆBˆØ]Ú
^Ù\[Ûˆ^
BˆÂˆ™]\›ˆ™]È›Ú™XÝ[ÙT›ØÙ\ÜÔ™\Ý[
LËÝš[™Ë‘[\K^“Y\ÜØYÙJNÂˆBŸB‚˜\Þ[˜È\ÚÏ›Ú™XÝ[ÙPYZ[š\Ý˜]ÜÛÛ^ˆ™\ÛÛ™T›Ú™XÝ[ÙPYZ[š\Ý˜]ÜÛÛ^\Þ[˜ÊÛÛ^ÛÛ^œÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[ÛŠBžÂˆ˜\ˆÚÙ[ˆHÙ]›Ú™XÝ[ÙTÙ\ÜÚ[Û•ÚÙ[ŠÛÛ^”™\]Y\Ý
NÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÚÙ[ŠJBˆÂˆ™]\›ˆ™]È›Ú™XÝ[ÙPYZ[š\Ý˜]ÜÛÛ^
˜[ÙK[[
NÂˆB‚ˆ˜\ˆÚÙ[’\ÚH\ÚÙ\ÜÚ[Û•ÚÙ[ŠÚÙ[ŠNÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕË\Ù\—ÚYK™[XZ[ˆ”“ÓH]]ÜÙ\ÜÚ[ÛœÈÂˆ“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHË\Ù\—ÚYˆÒT‘HËœÙ\ÜÚ[Û—ÝÚÙ[—Ú\ÚHÙ\ÜÚ[Û—ÝÚÙ[—Ú\ÚˆS‘Ëœ™]›ÚÙYØ]TÈ•SˆS‘Ë™^\™\×Ø]ˆ“ÕÊ
BˆS‘Kš\×ØXÝ]™HH•QBˆS‘K›ÙÚ[—Ù[˜X›YH•QBˆSRUNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÙ\ÜÚ[Û—ÝÚÙ[—Ú\Ú‹ÚÙ[’\Ú
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆYˆ
X]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™]\›ˆ™]È›Ú™XÝ[ÙPYZ[š\Ý˜]ÜÛÛ^
˜[ÙK[[
NÂˆB‚ˆ˜\ˆ\Ù\’YH™XY\‹‘Ù]ÝZY

NÂˆ˜\ˆ[XZ[H™XY\‹‘Ù]Ýš[™ÊJNÂ‚ˆ]ØZ]™XY\‹ÛÜÙP\Þ[˜Ê
NÂ‚ˆ˜\ˆ\ÐYZ[š\Ý˜]ÜˆH]ØZ]Ù\ÜÚ[Û•\Ù\’\ÐYZ[š\Ý˜]Ü\Þ[˜ÊÛÛ›™XÝ[Û‹\Ù\’Y
NÂ‚ˆ™]\›ˆ™]È›Ú™XÝ[ÙPYZ[š\Ý˜]ÜÛÛ^
\ÐYZ[š\Ý˜]Ü‹\Ù\’Y[XZ[
NÂŸB‚˜\Þ[˜È\ÚÏXÝ[Û˜\žOÝZYÝš[™Ö×OˆØY›Ú™XÝ[ÙPXÝ]™T›ÛPÛÙ\Ð\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆQ[[Y\˜X›OÝZYˆ\Ù\’YÊBžÂˆ˜\ˆ›Ü›X[^™Y\Ù\’YÈH\Ù\’YË‘\Ý[˜Ý

K•Ð\œ˜^J
NÂˆ˜\ˆ›Û\ÐžU\Ù\ˆH›Ü›X[^™Y\Ù\’YË•ÑXÝ[Û˜\žJ\Ù\’YOˆ\Ù\’YÈOˆ™]È\ÝÝš[™ÏŠ
JNÂ‚ˆYˆ
›Ü›X[^™Y\Ù\’YË“[™ÝOH
BˆÂˆ™]\›ˆ™]ÈXÝ[Û˜\žOÝZYÝš[™Ö×OŠ
NÂˆB‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕ\˜K\Ù\—ÚY‹œ›ÛWØÛÙBˆ”“ÓH\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜Bˆ“ÒSˆ\Ü›Û\ÈˆÓˆ‹˜\Ü›ÛWÚYH\˜K˜\Ü›ÛWÚYS‘‹š\×ØXÝ]™HH•QBˆÒT‘H\˜K\Ù\—ÚYHS–J\Ù\—ÚYÊBˆS‘\˜Kš\×ØXÝ]™HH•QBˆÔ‘Tˆ–H\˜K\Ù\—ÚY‹™\Ü^WÛÜ™\‹‹œ›ÛWØÛÙNÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚYÈ‹›Ü›X[^™Y\Ù\’YÊNÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ›Û\ÐžU\Ù\–Ü™XY\‹‘Ù]ÝZY

WKY
™XY\‹‘Ù]Ýš[™ÊJJNÂˆB‚ˆ™]\›ˆ›Û\ÐžU\Ù\‹•ÑXÝ[Û˜\žJˆZ\ˆOˆZ\‹’Ù^KˆZ\ˆOˆZ\‹•˜[YK‘\Ý[˜Ý
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ“Ü™\žJ›ÛPÛÙHOˆ›ÛPÛÙKÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ•Ð\œ˜^J
JNÂŸB‚˜\Þ[˜È\ÚÈ[œÙ\›Ú™XÝ[ÙT›ÛP]Y]\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆÝZYXÝÜ•\Ù\’YˆÝZY\™Ù]\Ù\’YˆÝš[™ÈXÝ[Û‹ˆÝš[™È™X\ÛÛ‹ˆQ[[Y\˜X›OÝš[™ÏˆÛ›ÛPÛÙ\ËˆQ[[Y\˜X›OÝš[™Ïˆ™]Ô›ÛPÛÙ\ËˆÛÛ^ÛÛ^
BžÂˆ˜\ˆÛ˜[YHHœÛÛ”Ù\šX[^™\‹”Ù\šX[^™J™]ÂˆÂˆ›ÛPÛÙ\ÈHÛ›ÛPÛÙ\Ë‘\Ý[˜Ý
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ“Ü™\žJ›ÛPÛÙHOˆ›ÛPÛÙKÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJK•Ð\œ˜^J
BˆJNÂˆ˜\ˆ™]Õ˜[YHHœÛÛ”Ù\šX[^™\‹”Ù\šX[^™J™]ÂˆÂˆ›ÛPÛÙ\ÈH™]Ô›ÛPÛÙ\Ë‘\Ý[˜Ý
Ýš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJBˆ“Ü™\žJ›ÛPÛÙHOˆ›ÛPÛÙKÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJK•Ð\œ˜^J
Kˆ™X\ÛÛ‹ˆ›Ý]HHÛÛ^”™\]Y\Ý”]•˜[YHÏÈÝš[™Ë‘[\BˆJNÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È]Y]ÛÙÜÈ
ˆXÝÜ—Ý\Ù\—ÚYXÝ[Û‹[]WÝ\K[]WÚYˆÛÝ˜[YK™]×Ý˜[YK\ØY™\ÜË\Ù\—ØYÙ[ˆ
BˆSQTÈ
ˆXÝÜ—Ý\Ù\—ÚYXÝ[Û‹	Ø\Ý\Ù\—Ü›Û\ÉË[]WÚYˆÐTÕ
ÛÝ˜[YHTÈœÛÛ˜ŠKÐTÕ
™]×Ý˜[YHTÈœÛÛ˜ŠKˆ•SQŠ\ØY™\ÜË	ÉÊNŽš[™]\Ù\—ØYÙ[ˆ
NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝÜ—Ý\Ù\—ÚY‹XÝÜ•\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝ[Ûˆ‹XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]WÚY‹\™Ù]\Ù\’Y
NÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›ÛÝ˜[YH‹Û˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›™]×Ý˜[YH‹™]Õ˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJš\ØY™\ÜÈ‹ÛÛ^ÛÛ›™XÝ[Û‹”™[[ÝR\Y™\ÜÏË•ÔÝš[™Ê
HÏÈÝš[™Ë‘[\JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ØYÙ[‹ÛÛ^”™\]Y\Ý’XY\œË•\Ù\YÙ[•ÔÝš[™Ê
JNÂˆ]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂŸB‚‹ËÈÑPÕT’UWÌŒŒÌŽWÕS”ÐPÕSÓSÔ“ÓWÐUQUÒST”Â˜\Þ[˜È\ÚÈ[œÙ\›Ú™XÝ[ÙP]Y]]™[\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆÝZYÈXÝÜ•\Ù\’YˆÝš[™ÈXÝ[Û‹ˆÝš[™È[]U\KˆÝZYÈ[]RYˆÛÛ^ÛÛ^ˆØš™XÝ™]Õ˜[YJBžÂˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È]Y]ÛÙÜÈ
ˆXÝÜ—Ý\Ù\—ÚYˆXÝ[Û‹ˆ[]WÝ\Kˆ[]WÚYˆ™]×Ý˜[YKˆ\ØY™\ÜËˆ\Ù\—ØYÙ[ˆ
BˆSQTÈ
ˆXÝÜ—Ý\Ù\—ÚYˆXÝ[Û‹ˆ[]WÝ\Kˆ[]WÚYˆÐTÕ
™]×Ý˜[YHTÈœÛÛ˜ŠKˆ•SQŠ\ØY™\ÜË	ÉÊNŽš[™]ˆ\Ù\—ØYÙ[ˆ
NÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝÜ—Ý\Ù\—ÚY‹XÝÜ•\Ù\’Y\È[È“[•˜[YHˆXÝÜ•\Ù\’Y•˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜XÝ[Ûˆ‹XÝ[ÛŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]WÝ\H‹[]U\JNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ™[]WÚY‹[]RY\È[È“[•˜[YHˆ[]RY•˜[YJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ›™]×Ý˜[YH‹œÛÛ”Ù\šX[^™\‹”Ù\šX[^™J™]Õ˜[YJJNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJš\ØY™\ÜÈ‹ÛÛ^ÛÛ›™XÝ[Û‹”™[[ÝR\Y™\ÜÏË•ÔÝš[™Ê
HÏÈˆŠNÂˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ØYÙ[‹ÛÛ^”™\]Y\Ý’XY\œË•\Ù\YÙ[•ÔÝš[™Ê
JNÂ‚ˆ]ØZ]ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂŸB‚‚‚‚œÝ]XÈ\Þ[˜È\ÚÏ[ˆ]Y]YT›Ú™XÝÛÜÝ[\›ÝYšXØ][ÛœÐ\Þ[˜ÊˆœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ˆœÜÜ[˜[œØXÝ[Ûˆ˜[œØXÝ[Û‹ˆÝZY[\YˆÝZY›Ú™XÝYˆÝš[™È[\\KˆÝš[™È[\Ù]™\š]KˆÝš[™È›Ú™XÝÛÙKˆÝš[™È›Ú™XÝ˜[YKˆÝš[™ÈÛY[˜[YKˆXÚ[X[[›™YÝ[›Ú™XÝÛÜÝˆXÚ[X[\ÜÚYÛ™YÝ\œËˆXÚ[X[\ÙYÝ\œËˆXÚ[X[Ý™\\ÜÚYÛ™YÝ\œËˆÝš[™ÈÛÜÝÝ]\ÊBžÂˆ˜\ˆ™XÚ\Y[ÈH™]È\Ý
Ýš[™È[XZ[Ýš[™È˜[YKÝš[™È›ÛJOŠ
NÂ‚ˆ]ØZ]\Ú[™È
˜\ˆ™XÚ\Y[ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÒU›Ú™XÝØÛÛ^TÈ
ˆÑSPÕœ›Ú™XÝÚYœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚYˆ”“ÓH›Ú™XÝÈˆÒT‘Hœ›Ú™XÝÚYH›Ú™XÝÚYˆ
KˆWÜ™XÚ\Y[ÈTÈ
ˆÑSPÕTÕSÕˆK™[XZ[ˆK™\Ü^WÛ˜[YKˆ	Ô›Ú™XÝX[˜YÙ\‰ÎŽ^TÈ™XÚ\Y[Ü›ÛBˆ”“ÓH›Ú™XÝØÛÛ^Âˆ“ÒSˆ\Ý\Ù\œÈHÓˆK\Ù\—ÚYHËœ›Ú™XÝÛX[˜YÙ\—Ý\Ù\—ÚYˆÒT‘HKš\×ØXÝ]™HH•QBˆS‘ÓÐSTÐÑJK™[XZ[	ÉÊHˆ	ÉÂˆ
KˆX[˜YÙ\—Ü™XÚ\Y[ÈTÈ
ˆÑSPÕTÕSÕˆX[˜YÙ\‹™[XZ[ˆX[˜YÙ\‹™\Ü^WÛ˜[YKˆ	Ô™\ÛÝ\˜ÙHX[˜YÙ\‰ÎŽ^TÈ™XÚ\Y[Ü›ÛBˆ”“ÓH›Ú™XÝØ\ÜÚYÛ›Y[ÈBˆ“ÒSˆ\Ý\Ù\œÈ[™Ú[™Y\ˆÓˆ[™Ú[™Y\‹\Ù\—ÚYHK\Ù\—ÚYˆ“ÒSˆ\Ý\Ù\œÈX[˜YÙ\ˆÓˆÝÙ\ŠX[˜YÙ\‹™[XZ[
HHÝÙ\Š[™Ú[™Y\‹›X[˜YÙ\—Ù[XZ[
BˆÒT‘HKœ›Ú™XÝÚYH›Ú™XÝÚYˆS‘X[˜YÙ\‹š\×ØXÝ]™HH•QBˆS‘ÓÐSTÐÑJX[˜YÙ\‹™[XZ[	ÉÊHˆ	ÉÂˆ
Kˆ×Ü™XÚ\Y[ÈTÈ
ˆÑSPÕTÕSÕˆK™[XZ[ˆK™\Ü^WÛ˜[YKˆ	Ô›Ú™XÝX[HÛÛÜ™[˜]Ü‰ÎŽ^TÈ™XÚ\Y[Ü›ÛBˆ”“ÓH\Ý\Ù\œÈBˆ“ÒSˆ\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜HÓˆ\˜K\Ù\—ÚYHK\Ù\—ÚYS‘\˜Kš\×ØXÝ]™HH•QBˆ“ÒSˆ\Ü›Û\ÈˆÓˆ‹˜\Ü›ÛWÚYH\˜K˜\Ü›ÛWÚYˆÒT‘H‹œ›ÛWØÛÙHH	Ô“Ò‘PÕÕPSWÐÓÓÔ‘SUÔ‰ÂˆS‘‹š\×ØXÝ]™HH•QBˆS‘Kš\×ØXÝ]™HH•QBˆS‘ÓÐSTÐÑJK™[XZ[	ÉÊHˆ	ÉÂˆ
BˆÑSPÕTÕSÕÓˆ
ÝÙ\Š[XZ[
JBˆ[XZ[ˆ\Ü^WÛ˜[YKˆ™XÚ\Y[Ü›ÛBˆ”“ÓH
ˆÑSPÕ
ˆ”“ÓHWÜ™XÚ\Y[ÂˆS’SÓˆSˆÑSPÕ
ˆ”“ÓHX[˜YÙ\—Ü™XÚ\Y[ÂˆS’SÓˆSˆÑSPÕ
ˆ”“ÓH×Ü™XÚ\Y[Âˆ
H™XÚ\Y[ÂˆÔ‘Tˆ–HÝÙ\Š[XZ[
K™XÚ\Y[Ü›ÛNÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ™XÚ\Y[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ›Ú™XÝÚY‹›Ú™XÝY
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]™XÚ\Y[ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂ‚ˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ™XÚ\Y[ËY

ˆ™XY\‹‘Ù]Ýš[™Ê
Kˆ™XY\‹’\Ñ“[
JHÈ™XY\‹‘Ù]Ýš[™Ê
Hˆ™XY\‹‘Ù]Ýš[™ÊJKˆ™XY\‹‘Ù]Ýš[™ÊŠBˆ
JNÂˆBˆB‚ˆYˆ
™XÚ\Y[ËÛÝ[OH
BˆÂˆ™]\›ˆÂˆB‚ˆ˜\ˆÝXš™XÝH	”›Ú™XÝ[ÙHÛÜÝ[\ˆÜ›Ú™XÝÛÙ_HHØ[\Ù]™\š]K•Õ\\’[˜\šX[

_HŽÂˆ˜\ˆ›ÙHH	ˆˆ‚”›Ú™XÝ[ÙH]XÝYH›Ú™XÝÛÜÝÜ™XY[™\ÜÈ[\‚‚”›Ú™XÝˆÜ›Ú™XÝÛÙ_HHÜ›Ú™XÝ˜[Y_BÝ\ÝÛY\ŽˆØÛY[˜[Y_B[\\NˆØ[\\_B”Ù]™\š]NˆØ[\Ù]™\š]_BÛÜÝÝ]\ÎˆØÛÜÝÝ]\ßB‚”[›™YÝ[›Ú™XÝÛÜÝˆÜ[›™YÝ[›Ú™XÝÛÜÝßB\ÜÚYÛ™YÝ\œÎˆØ\ÜÚYÛ™YÝ\œÎ“ŒŸB•\ÙYÝ\œÎˆÝ\ÙYÝ\œÎ“ŒŸB“Ý™\ˆ\ÜÚYÛ™YÝ\œÎˆÛÝ™\\ÜÚYÛ™YÝ\œÎ“ŒŸB‚”X\ÙH™]šY]È›Ú™XÝ\ÜÚYÛ›Y[[YH\ØYÙK[™ÛÜÝ[ˆ™XY[™\ÜÈ[ˆ›Ú™XÝ[ÙK‚ˆˆˆŽÂ‚ˆ›Ü™XXÚ
˜\ˆ™XÚ\Y[[ˆ™XÚ\Y[ÊBˆÂˆ]ØZ]\Ú[™È
˜\ˆ›ÝYžPÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È›ÝYšXØ][Û—ÛÝ]›Þ
ˆ›ÝYšXØ][Û—Ý\Kˆ™XÚ\Y[Ù[XZ[ˆÝXš™XÝˆ›ÙKˆ™[]YÙ[]WÝ\Kˆ™[]YÙ[]WÚYˆ
BˆSQTÈ
ˆ	Ü›Ú™XÝØÛÜÝØ[\	Ëˆ™XÚ\Y[Ù[XZ[ˆÝXš™XÝˆ›ÙKˆ	Ü›Ú™XÝØÛÜÝØ[\	Ëˆ™[]YÙ[]WÚYˆ
NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ›ÝYžPÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ù[XZ[‹™XÚ\Y[‘[XZ[
NÂˆ›ÝYžPÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÝXš™XÝ‹ÝXš™XÝ
NÂˆ›ÝYžPÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜›ÙH‹›ÙJNÂˆ›ÝYžPÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™[]YÙ[]WÚY‹[\Y
NÂˆ]ØZ]›ÝYžPÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆB‚ˆ]ØZ]\Ú[™È
˜\ˆ[XZ[ÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆS”ÑT•S•È[XZ[Û›ÝYšXØ][Û—ÛÝ]›Þ
ˆ[WØÛÙKˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Û˜[YKˆÝXš™XÝˆ›ÙKˆÝ]\ËˆØÚY[YÙ›Ü‚ˆ
BˆSQTÈ
ˆ	Ô“Ò‘PÕÐÓÔÕÐST•	Ëˆ™XÚ\Y[Ù[XZ[ˆ™XÚ\Y[Û˜[YKˆÝXš™XÝˆ›ÙKˆ	Ü]Y]YY	Ëˆ“ÕÊ
Bˆ
NÂˆˆˆ‹ÛÛ›™XÝ[Û‹˜[œØXÝ[ÛŠJBˆÂˆ[XZ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Ù[XZ[‹™XÚ\Y[‘[XZ[
NÂˆ[XZ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœ™XÚ\Y[Û˜[YH‹™XÚ\Y[“˜[YJNÂˆ[XZ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJœÝXš™XÝ‹ÝXš™XÝ
NÂˆ[XZ[ÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ˜›ÙH‹›ÙJNÂˆ]ØZ][XZ[ÛÛ[X[™‘^XÝ]S›Û”]Y\žP\Þ[˜Ê
NÂˆBˆB‚ˆ™]\›ˆ™XÚ\Y[ËÛÝ[ÂŸB‚‚‚˜\Þ[˜È\ÚÏ\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÏˆØY\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÐ\Þ[˜ÊœÜÜ[ÛÛ›™XÝ[ÛˆÛÛ›™XÝ[Û‹ÝZY\Ù\’Y
BžÂˆ˜\ˆ\›Z\ÜÚ[ÛœÈH™]È\ÚÙ]Ýš[™ÏŠÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂˆ˜\ˆ›Û\ÈH™]È\ÚÙ]Ýš[™ÏŠÝš[™ÐÛÛ\\™\‹“Ü™[˜[YÛ›Ü™PØ\ÙJNÂ‚ˆ]ØZ]\Ú[™È˜\ˆÛÛ[X[™H™]ÈœÜÜ[ÛÛ[X[™
ˆˆ‚ˆÑSPÕˆ‹œ›ÛWØÛÙKˆÓÐSTÐÑJœ\›Z\ÜÚ[Û—ØÛÙK	ÉÊHTÈ\›Z\ÜÚ[Û—ØÛÙBˆ”“ÓH\Ý\Ù\—Ü›ÛWØ\ÜÚYÛ›Y[È\˜Bˆ“ÒSˆ\Ü›Û\È‚ˆÓˆ‹˜\Ü›ÛWÚYH\˜K˜\Ü›ÛWÚYˆS‘‹š\×ØXÝ]™HH•QBˆQ•“ÒSˆ\Ü›ÛWÜ\›Z\ÜÚ[ÛœÈœˆÓˆœ˜\Ü›ÛWÚYH‹˜\Ü›ÛWÚYˆQ•“ÒSˆ\Ü\›Z\ÜÚ[ÛœÈˆÓˆ˜\Ü\›Z\ÜÚ[Û—ÚYHœ˜\Ü\›Z\ÜÚ[Û—ÚYˆÒT‘H\˜K\Ù\—ÚYH\Ù\—ÚYˆS‘\˜Kš\×ØXÝ]™HH•QNÂˆˆˆ‹ÛÛ›™XÝ[ÛŠNÂ‚ˆÛÛ[X[™”\˜[Y]\œËYÚ]˜[YJ\Ù\—ÚY‹\Ù\’Y
NÂ‚ˆ]ØZ]\Ú[™È˜\ˆ™XY\ˆH]ØZ]ÛÛ[X[™‘^XÝ]T™XY\\Þ[˜Ê
NÂˆÚ[H
]ØZ]™XY\‹”™XY\Þ[˜Ê
JBˆÂˆ›Û\ËY
™XY\‹‘Ù]Ýš[™Ê
JNÂ‚ˆYˆ
\™XY\‹’\Ñ“[
JH	‰ˆ\Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ™XY\‹‘Ù]Ýš[™ÊJJJBˆÂˆ\›Z\ÜÚ[ÛœËY
™XY\‹‘Ù]Ýš[™ÊJJNÂˆBˆB‚ˆ˜\ˆØ[•šY]Ð[Bˆ
›Û\ËÛÛZ[œÊ”ÕTT—ÐQRS’TÕUÔˆŠH›Û\ËÛÛZ[œÊQRS’TÕUÔˆŠJBˆ›Û\ËÛÛZ[œÊ”“Ò‘PÕÕPSWÐÓÓÔ‘SUÔˆŠBˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ”ÖTÕSWÐQRS’TÕUSÓˆŠBˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ“PSQÑWÐSŠNÂ‚ˆ˜\ˆØ[”›Ú™XÝ\›Ý™HBˆØ[•šY]Ð[ˆ›Û\ËÛÛZ[œÊ”“Ò‘PÕÓPSQÑSQS•ŠBˆ
›Û\ËÛÛZ[œÊ”“Ò‘PÕÓPSQÑSQS•ŠH›Û\ËÛÛZ[œÊ”“Ò‘PÕÓPSQÑTˆŠJBˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ”“Ò‘PÕÕSQWÐT“ÕSŠNÂ‚ˆ˜\ˆØ[“X[˜YÙPXØÛÝ[[™ÈBˆØ[•šY]Ð[ˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ“PSQÑWÐPÐÓÕS•Ô‘PÓÓÒSPUSÓˆŠNÂ‚ˆ˜\ˆØ[‘^ÜBˆØ[•šY]Ð[ˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ‘VÔ•ÕSQWÑVÑSŠBˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ‘VÔ•ÕSQWÔˆŠNÂ‚ˆ˜\ˆØ[]Y]BˆØ[•šY]Ð[ˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×ÐUQUÕRSŠNÂ‚ˆ˜\ˆØ[•šY]ÈBˆØ[•šY]Ð[ˆØ[”›Ú™XÝ\›Ý™BˆØ[“X[˜YÙPXØÛÝ[[™ÂˆØ[‘^ÜˆØ[]Y]ˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×ÐT“ÕSÕÓÔ’Ñ“ÕÈŠBˆ\›Z\ÜÚ[ÛœËÛÛZ[œÊ•’QU×ÐPÐÓÕS•Ô‘PÓÓÒSPUSÓˆŠNÂ‚ˆ™]\›ˆ™]È\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÊˆØ[•šY]ÎˆØ[•šY]ËˆØ[”›Ú™XÝ\›Ý™NˆØ[”›Ú™XÝ\›Ý™KˆØ[“X[˜YÙPXØÛÝ[[™ÎˆØ[“X[˜YÙPXØÛÝ[[™ËˆØ[‘^ÜˆØ[‘^ÜˆØ[]Y]ˆØ[]Y]ˆØ[•šY]Ð[ˆØ[•šY]Ð[
NÂŸB‚‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ÙP˜XÚÝ\[]T™\]Y\Ý
Ýš[™È™\]Y\ÝYÝš[™ÏÈ™X\ÛÛŠNÂš[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ÙP˜XÚÝ\[”™\]Y\Ý
›ÛÛ\ØYÔÙ›ÛÛÈ\ØYÐ^\™KÝš[™ÏÈ™X\ÛÛŠNÂš[\›˜[ÙX[Y™XÛÜ™Ù\šXÙT™\Ý\™\]Y\Ý
Ýš[™ÈÙ\šXÙRÙ^KÝš[™È™X\ÛÛŠNÂš[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ÙT›ØÙ\ÜÔ™\Ý[
[^]ÛÙKÝš[™ÈÝ[™\™Ý]]Ýš[™ÈÝ[™\™\œ›ÜŠNÂš[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ÙPYZ[š\Ý˜]ÜÛÛ^
›ÛÛ\ÐYZ[š\Ý˜]Ü‹ÝZYÈ\Ù\’YÝš[™ÏÈ[XZ[
NÂ‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ØØ][Û”›Ú™XÝ\Ù\™\]Y\Ý
ˆÝš[™È›Ú™XÝÛÙKˆÝš[™È›Ú™XÝ˜[YKˆÝš[™ÏÈÝ\ÝÛY\“˜[YKˆÝš[™ÏÈÙ\šXÙT™\]Y\Ý[X™\‹ˆÝš[™ÏÈ›Ú™XÝÝ]\Ëˆ\Ý›Ú™XÝ[ØØ][Û‘[™Ú[™Y\”™\]Y\ÝÈ[ØØ][ÛœÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ØØ][Û‘[™Ú[™Y\”™\]Y\Ý
ˆÝZY\Ù\’YˆXÚ[X[[ØØ]YÝ\œËˆÝš[™ÏÈ›Ý\ÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝØÝ[Y[\™ÙT™\]Y\Ý
ˆ[Û\•[‘^\Ëˆ›ÛÛ[˜ÛYPXÝ]™T›Ú™XÝËˆÝš[™ÏÈ\™ÙT™X\ÛÛŠNÂ‚‚š[\›˜[ÙX[Y™XÛÜ™[Y\ÚY]^TÝX›Z]™\]Y\Ý
]SÛ›HÙYZÔÝ\]SÛ›HÛÜšÑ]K\Ý[Y\ÚY][žT™\]Y\Ýˆ[šY\ÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™[Y\ÚY]^U[›ØÚÔ™\]Y\Ý
]SÛ›HÙYZÔÝ\]SÛ›HÛÜšÑ]JNÂ‚š[\›˜[ÙX[Y™XÛÜ™X[˜YÙ\[Ð\›Ý˜[™\]Y\Ý
\ÝX[˜YÙ\\›Ý˜[XÝ[Û”™\]Y\Ýˆ][\ËÝš[™ÏÈÛÛ[Y[
NÂ‚š[\›˜[ÙX[Y™XÛÜ™X[˜YÙ\\›Ý˜[XÝ[Û”™\]Y\Ý
ÝZY[Y\ÚY]Y]SÛ›HÛÜšÑ]KÝš[™ÏÈÛÛ[Y[
NÂ‚š[\›˜[ÙX[Y™XÛÜ™[Y\ÚY]™Y™\™[˜ÙT™\]Y\Ý
\ÝÝš[™ÏÈY˜][›Û”›Ú™XÝØ]YÛÜžPÛÙ\Ë\ÝÝZYÈY˜][›Ú™XÝ\ÚÒYË›ÛÛ]]ÐYÛY^\Ë›ÛÛÙYZÛT™[Z[™\‘[˜X›Y
NÂ‚š[\›˜[ÙX[Y™XÛÜ™ÛY^PÜÝ’[\Ü™\]Y\Ý
[ÈYX\‹Ýš[™ÏÈš[[˜[YKÝš[™ÈÜÝ•^
NÂš[\›˜[ÙX[Y™XÛÜ™ÛY^R[\Ü›ÝÊ]SÛ›HÛY^Q]KÝš[™ÈÛY^S˜[YKÝš[™ÈÛY^U\K›ÛÛ\Ñ›Ø][™ÒÛY^KXÚ[X[]]ÔÜ[]RÝ\œÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™\Ù\”›ÛP\ÜÚYÛ›Y[™\]Y\Ý
Ýš[™È[XZ[\ÝÝš[™ÏÈ›ÛPÛÙ\ËÝš[™ÏÈ™X\ÛÛŠNÂ‚‚‚‚‚š[\›˜[ÙX[Y™XÛÜ™\Ù\YZ[[Õ\]T™\]Y\Ý
ˆ\ÝÝZYÈ\Ù\’YËˆ›ÛÛ\R›Ø•]KˆÝš[™ÏÈ›Ø•]Kˆ›ÛÛ\Q\\Y[˜[YKˆÝš[™ÏÈ\\Y[˜[YKˆ›ÛÛ\UX[S˜[YKˆÝš[™ÏÈX[S˜[YKˆ›ÛÛ\SÙ™šXÙSØØ][Û‹ˆÝš[™ÏÈÙ™šXÙSØØ][Û‹ˆ›ÛÛ\SX[˜YÙ\‘[XZ[ˆÝš[™ÏÈX[˜YÙ\‘[XZ[ˆ›ÛÛ\SÙÚ[‘[˜X›Yˆ›ÛÛÙÚ[‘[˜X›Yˆ›ÛÛ\R\ÐXÝ]™Kˆ›ÛÛ\ÐXÝ]™KˆÝš[™ÏÈ›ÛU\]S[ÙKˆ\ÝÝš[™ÏÈ›ÛPÛÙ\ËˆÝš[™ÏÈ™X\ÛÛŠNÂ‚‚š[\›˜[ÙX[Y™XÛÜ™\Ù\YZ[”›Ùš[U\]T™\]Y\Ý
ˆÝZY\Ù\’YˆÝš[™ÏÈ[XZ[ˆÝš[™ÏÈ\Ü^S˜[YKˆÝš[™ÏÈ›Ø•]KˆÝš[™ÏÈ\\Y[˜[YKˆÝš[™ÏÈX[S˜[YKˆÝš[™ÏÈÙ™šXÙSØØ][Û‹ˆÝš[™ÏÈX[˜YÙ\‘[XZ[ˆ›ÛÛÙÚ[‘[˜X›Yˆ›ÛÛ\ÐXÝ]™JNÂ‚š[\›˜[ÙX[Y™XÛÜ™\Ù\YZ[”›ÛU\]T™\]Y\Ý
ˆÝZY\Ù\’Yˆ\ÝÝš[™ÏÈ›ÛPÛÙ\ËˆÝš[™ÏÈ™X\ÛÛŠNÂ‚š[\›˜[ÙX[Y™XÛÜ™\Ù\YZ[“ØØ[\ÜÝÛÜ™\]T™\]Y\Ý
ˆÝZY\Ù\’YˆÝš[™È[\Ü˜\žT\ÜÝÛÜ™ˆ›ÛÛ]\ÝÚ[™ÙT\ÜÝÛÜ™ˆÝš[™ÏÈ›Ý\ÊNÂ‚‚š[\›˜[ÙX[Y™XÛÜ™^\™PYZ[ÛÛ™šYÔ™\]Y\Ý
ˆÝš[™ÏÈ[˜[YˆÝš[™ÏÈÛY[YˆÝš[™ÏÈ]]Üš]U\›ˆÝš[™ÏÈ™Y\™XÝ\šKˆÝš[™ÏÈÜ˜\ØÛÜKˆ›ÛÛÞ[˜Ñ[˜X›YˆÝš[™ÏÈY˜][›ÛPÛÙKˆ[Þ[˜Ñœ™\]Y[˜ÞRÝ\œÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™^\™U\Ù\’[\Ü™\]Y\Ý
\Ý^\™U\Ù\’[\Ü›ÝÏÈ\Ù\œÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™^\™U\Ù\’[\Ü›ÝÊˆÝš[™ÏÈ[XZ[ˆÝš[™ÏÈ\Ü^S˜[YKˆÝš[™ÏÈ[˜SØš™XÝYˆÝš[™ÏÈ›Ø•]KˆÝš[™ÏÈ\\Y[˜[YKˆÝš[™ÏÈÙ™šXÙSØØ][Û‹ˆÝš[™ÏÈX[˜YÙ\‘[XZ[
NÂ‚‚š[\›˜[ÙX[Y™XÛÜ™ØØ[ÙÚ[”™\]Y\Ý
Ýš[™È\Ù\›˜[YKÝš[™È\ÜÝÛÜ™
NÂš[\›˜[ÙX[Y™XÛÜ™ÜÛÑ]™[ÜY[ÙÚ[”™\]Y\Ý
Ýš[™È[XZ[
NÂš[\›˜[ÙX[Y™XÛÜ™Ù][\Ü˜\žT\ÜÝÛÜ™™\]Y\Ý
ÝZY™\Ù]™\]Y\ÝYÝš[™È\Ù\›˜[YKÝš[™È[\Ü˜\žT\ÜÝÛÜ™
NÂš[\›˜[ÙX[Y™XÛÜ™Ú[™ÙSØØ[\ÜÝÛÜ™™\]Y\Ý
Ýš[™ÈÝ\œ™[\ÜÝÛÜ™Ýš[™È™]Ô\ÜÝÛÜ™
NÂš[\›˜[ÙX[Yœ™XÛÜ™›Ú™XÝ[ÙQ[˜R[\ÜÙ][™ÜÊˆÝš[™È[š\›Û›Y[[ÙKˆÝš[™È[˜[ÛXZ[‹ˆÝš[™ÈÛÝ\˜ÙT›ÝšY\‹ˆÝš[™È[\ÜÛÝ\˜ÙU\KˆÝš[™ÏÈÜ˜\Ü›Ý\YˆÝš[™ÏÈÜ˜\š[\‹ˆÝš[™ÈY˜][›ÛPÛÙKˆ›ÛÛ\ØX›SZ\ÜÚ[™Ñœ›ÛTÛÝ\˜ÙJNÂ‚œ™XÛÜ™›Ú™XÝ[ÙQÜ˜\\Ù\ŠˆÝš[™ÈYˆÝš[™È\Ü^S˜[YKˆÝš[™È[XZ[ˆÝš[™ÏÈ\Ù\”š[˜Ú\[˜[YKˆÝš[™ÏÈ›Ø•]KˆÝš[™ÏÈ\\Y[ˆÝš[™ÏÈÙ™šXÙSØØ][Û‹ˆ›ÛÛXØÛÝ[[˜X›Y
NÂ‚œ™XÛÜ™›Ú™XÝ[ÙR[\ÜÙ[XÝY\Ù\œÔ™\]Y\Ý
ˆ\ÝÝš[™Ïˆ[˜SØš™XÝYÊNÂ‚œ™XÛÜ™›Ú™XÝ[ÙR[\ÜÙ][™ÜÕ\]T™\]Y\Ý
ˆÝš[™È[š\›Û›Y[[ÙKˆÝš[™È[˜[ÛXZ[‹ˆÝš[™ÈÛÝ\˜ÙT›ÝšY\‹ˆÝš[™È[\ÜÛÝ\˜ÙU\KˆÝš[™ÏÈÜ˜\Ü›Ý\YˆÝš[™ÏÈÜ˜\š[\‹ˆÝš[™ÈY˜][›ÛPÛÙKˆ›ÛÛ\ØX›SZ\ÜÚ[™Ñœ›ÛTÛÝ\˜ÙJNÂ‚‚œ™XÛÜ™›Ú™XÝ[ÙPÜ™X]YÙ\ÜÚ[ÛŠÝZYÙ\ÜÚ[Û’YÝš[™È˜]ÕÚÙ[‹]U[YSÙ™œÙ]^\™\Ð]
NÂœ™XÛÜ™›Ú™XÝ[ÙUšY]Ð\Õ\Ù\ŠÝZY\Ù\’YÝš[™È[XZ[
NÂ‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ÙTÙ\ÜÚ[Û•˜[Y][ÛŠ›ÛÛ\Õ˜[YÝZYÈ\Ù\’YÝš[™ÏÈ[XZ[Ýš[™ÏÈ›ÝšY\ÛÙK]U[YSÙ™œÙ]È^\™\Ð]Ýš[™ÏÈY\ÜØYÙJNÂ‚š[\›˜[ÙX[Y™XÛÜ™\ÜÝÛÜ™™\Ù]ÛÛ\][Û”™\]Y\Ý
ÝZY™\Ù]™\]Y\ÝYÝš[™È[\Ü˜\žT\ÜÝÛÜ™Ýš[™ÏÈXÝ[ÛžQ[XZ[Ýš[™ÏÈ›Ý\ÊNÂ‚‚‚š[\›˜[ÙX[Y™XÛÜ™\ÜÝÛÜ™™\Ù]\›Ý˜[XÝ[ÛŠÝZY™\Ù]™\]Y\ÝYÝš[™ÏÈXÝ[ÛžQ[XZ[Ýš[™ÏÈ›Ý\ÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™\ÜÝÛÜ™™\Ù]™\]Y\Ý
Ýš[™È\Ù\›˜[YKÝš[™ÏÈ›Ý\ÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™Õ[YQ[žPÛÜœ™XÝ[Û”™\]Y\Ý
ˆÝZY[YQ[žRYˆÝZY\™Ù]›Ú™XÝYˆÝZY\™Ù]\ÚÒYˆÝš[™ÏÈÜ\˜][Û‹ˆXÚ[X[ÈÜ]Ý\œËˆÝš[™ÏÈ™X\ÛÛŠNÂ‚š[\›˜[ÙX[Y™XÛÜ™[Y\ÚY]Ø]™T™\]Y\Ý
]SÛ›HÙYZÔÝ\\Ý[Y\ÚY][žT™\]Y\Ýˆ[šY\ÊNÂ‚š[\›˜[ÙX[Y™XÛÜ™[Y\ÚY][žT™\]Y\Ý
ˆÝš[™È›ÝÕ\KˆÝš[™ÏÈØ]YÛÜžPÛÙKˆ]SÛ›HÛÜšÑ]KˆÝš[™È[YU\KˆXÚ[X[Ý\œËˆÝš[™ÏÈ\ØÜš\[Û‹ˆÝZYÈÛÜšÓØØ][Û‘Ü›Ý\YˆÝZYÈÛÜšÓØØ][Û’YˆÝZYÈ›Ú™XÝYˆÝZYÈ\ÚÒY
NÂ‚š[\›˜[ÙX[Y™XÛÜ™[Y\ÚY]XY\ŠÝZYÈ[Y\ÚY]YÝš[™ÈÝ]\Ë]U[YSÙ™œÙ]ÈÝX›Z]Y]
NÂ‚š[\›˜[ÙX[Y™XÛÜ™^TÝ]\Ô™XÛÜ™
Ýš[™ÈÝ]\Ë]U[YSÙ™œÙ]ÈÝX›Z]Y]
NÂ‚š[\›˜[ÙX[Y™XÛÜ™]X˜\ÙPÛÛ™šYÊˆÝš[™ÏÈÜÝˆÝš[™ÏÈÜˆÝš[™ÏÈ]X˜\ÙKˆÝš[™ÏÈ\Ù\›˜[YKˆÝš[™ÏÈ\ÜÝÛÜ™ˆT™XYÛ›S\ÝÝš[™ÏˆZ\ÜÚ[™ÊBžÂˆX›XÈÝš[™ÈÛÛ›™XÝ[Û”Ýš[™ÂˆÂˆÙ]ˆÂˆ˜\ˆZ[\ˆH™]ÈœÜÜ[ÛÛ›™XÝ[Û”Ýš[™ÐZ[\‚ˆÂˆÜÝHÜÝˆÜH[•žT\œÙJÜÝ]˜\ˆ\œÙYÜ
HÈ\œÙYÜˆMÌ‹ˆ]X˜\ÙHH]X˜\ÙKˆ\Ù\›˜[YHH\Ù\›˜[YKˆ\ÜÝÛÜ™H\ÜÝÛÜ™ˆ[˜ÛYQ\œ›Ü‘]Z[H˜[ÙKˆÛÛ[™ÈHYKˆZ[”ÛÛÚ^™HHˆX^ÛÛÚ^™HHBˆNÂ‚ˆ™]\›ˆZ[\‹ÛÛ›™XÝ[Û”Ýš[™ÎÂˆBˆB‚ˆX›XÈÝ]XÈ]X˜\ÙPÛÛ™šYÈœ›ÛQ[š\›Û›Y[

BˆÂˆ˜\ˆÜÝH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”Ñ—ÒÔÕŠNÂˆ˜\ˆÜH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”Ñ—ÔÔ•ŠNÂˆ˜\ˆ]X˜\ÙHH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”Ñ—ÓSQHŠNÂˆ˜\ˆ\Ù\›˜[YHH[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”Ñ—ÕTÑTˆŠNÂˆ˜\ˆ\ÜÝÛÜ™H[š\›Û›Y[‘Ù][š\›Û›Y[˜\šXX›J”Ñ—ÔTÔÕÓÔ‘ŠNÂ‚ˆ˜\ˆZ\ÜÚ[™ÈH™]È\ÝÝš[™ÏŠ
NÂ‚ˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÜÝ
JHZ\ÜÚ[™ËY
”Ñ—ÒÔÕŠNÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJÜ
JHZ\ÜÚ[™ËY
”Ñ—ÔÔ•ŠNÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ]X˜\ÙJJHZ\ÜÚ[™ËY
”Ñ—ÓSQHŠNÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ\Ù\›˜[YJJHZ\ÜÚ[™ËY
”Ñ—ÕTÑTˆŠNÂˆYˆ
Ýš[™Ë’\Ó[Ü•Ú]TÜXÙJ\ÜÝÛÜ™
JHZ\ÜÚ[™ËY
”Ñ—ÔTÔÕÓÔ‘ŠNÂ‚ˆ™]\›ˆ™]È]X˜\ÙPÛÛ™šYÊÜÝÜ]X˜\ÙK\Ù\›˜[YK\ÜÝÛÜ™Z\ÜÚ[™ÊNÂˆBŸBœ™XÛÜ™\Ù\YZ[“ØØ[\Ù\Ü™X]T™\]Y\Ý
ˆÝš[™È[XZ[ˆÝš[™È\Ü^S˜[YKˆÝš[™ÏÈ[\Ü˜\žT\ÜÝÛÜ™ˆ›ÛÛ]\ÝÚ[™ÙT\ÜÝÛÜ™ˆÝš[™ÏÈ›Ø•]KˆÝš[™ÏÈ\\Y[˜[YKˆÝš[™ÏÈX[S˜[YKˆÝš[™ÏÈÙ™šXÙSØØ][Û‹ˆÝš[™ÏÈX[˜YÙ\‘[XZ[ˆ\ÝÝš[™ÏÈ›ÛPÛÙ\ÊNÂ‚œ™XÛÜ™\Ù\YZ[•\Ù\“Y™XÞXÛT™\]Y\Ý
ˆÝZY\Ù\’YˆÝš[™ÏÈ™X\ÛÛŠNÂ‚‚œ™XÛÜ™›Ú™XÝ[ÙPZU[YQ[žTÝYÙÙ\Ý[Û”™\]Y\Ý
ˆ]SÛ›HÛÜšÑ]KˆÝZYÈ[YQ[žRYˆÝZYÈ\ÜÚYÛ›Y[YˆÝZYÈ›Ú™XÝYˆÝZYÈ\ÚÒYˆÝZYÈ›Û”›Ú™XÝ[YPØ]YÛÜžRYˆÝš[™ÏÈ[YU\KˆÝš[™ÏÈ›ÝÕ\KˆÝš[™ÏÈ›ÝÓX™[ˆÝš[™ÏÈÝ\ÝÛY\“˜[YKˆÝš[™ÏÈ›Ú™XÝ˜[YKˆÝš[™ÏÈ›Ú™XÝÛÙKˆÝš[™ÏÈ\ÚÓ˜[YKˆÝš[™ÏÈ\ÚÐÛÙKˆÝš[™ÏÈØ]YÛÜžPÛÙKˆXÚ[X[ÈÝ\œËˆÝš[™ÏÈÝ\œ™[\ØÜš\[ÛŠNÂ‚œ™XÛÜ™›Ú™XÝ[ÙPZU[YQ[žTÝYÙÙ\Ý[Û”™\Ý[
ˆÝš[™ÈÝYÙÙ\Ý[Û‹ˆÝš[™È›ÝšY\‹ˆÝš[™ÏÈØ\›š[™ËˆT™XYÛ›S\Ý›Ú™XÝ[ÙPZU\™Ù]XÚ\Ú[ÛÈ\™Ù]XÚ\Ú[ÛœÈH[
NÂ‚‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ÙT™\XØ][Û”Þ[˜ÔÙ][™ÜÔ™\]Y\Ý
ˆÝš[™ÏÈY\“˜[YKˆÝš[™ÏÈY\’ÜÝˆÝš[™ÏÈY\•\›ˆ[ÈÝ[P˜XÚÝ\Ý\œÊNÂ‚‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ÙT™\ÝÜ™U˜[Y][Û”Ù][™ÜÔ™\]Y\Ý
Ýš[™ÏÈÙ[XÝY˜XÚÝ\
NÂ‚‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝ[ÙP˜XÚÝ\™][[Û‘[]T™\]Y\Ý
ˆÝš[™ÏÈ˜XÚÝ\˜[YKˆÝš[™ÏÈ™X\ÛÛ‹ˆ›ÛÛÈÛÛ™š\›JNÂ‚‚‚š[\›˜[ÙX[Y™XÛÜ™Ý\ÝÛY\‘\™XÝÜžPÛY[\Ù\™\]Y\Ý
ˆÝš[™ÈÛY[˜[YKˆÝš[™ÏÈÛY[ÛÙKˆ›ÛÛÈ\ÐXÝ]™JNÂ‚š[\›˜[ÙX[Y™XÛÜ™Ý\ÝÛY\‘\™XÝÜžPÛÛXÝ\Ù\™\]Y\Ý
ˆÝš[™ÈÛÛXÝ˜[YKˆÝš[™ÏÈ]KˆÝš[™ÏÈ›ÛQ\ØÜš\[Û‹ˆÝš[™ÏÈ[XZ[ˆÝš[™ÏÈÛ™KˆÝš[™ÏÈY™\ÜÓ[™LKˆÝš[™ÏÈY™\ÜÓ[™L‹ˆÝš[™ÏÈÚ]KˆÝš[™ÏÈÝ]T™YÚ[Û‹ˆÝš[™ÏÈÜÝ[ÛÙKˆÝš[™ÏÈÛÝ[žKˆ›ÛÛÈ\Ôš[X\žKˆ›ÛÛÈ\ÐXÝ]™Kˆ[È\Ü^SÜ™\ŠNÂ‚‚‚‚‚‚‚š[\›˜[ÙX[Y™XÛÜ™\›Ý˜[^ÜÛÜšÙ›ÝÐXØÙ\ÜÊˆ›ÛÛØ[•šY]Ëˆ›ÛÛØ[”›Ú™XÝ\›Ý™Kˆ›ÛÛØ[“X[˜YÙPXØÛÝ[[™Ëˆ›ÛÛØ[‘^Üˆ›ÛÛØ[]Y]ˆ›ÛÛØ[•šY]Ð[
NÂ‚‚š[\›˜[ÙX[Y™XÛÜ™\›Ý˜[^ÜÛÜšÙ›ÝÐXÝ[Û”™\]Y\Ý
ˆÝZY[Y\ÚY]Yˆ]SÛ›HÛÜšÑ]KˆÝš[™ÏÈXÝ[Û‹ˆÝš[™ÏÈÛÛ[Y[
NÂ‚‚š[\›˜[ÙX[Y™XÛÜ™[YUÛÜšÙ›ÝÑ^ÜÜ™X]T™\]Y\Ý
ˆÝš[™ÏÈ^Ü›Ü›X]ˆ]SÛ›OÈÙYZÔÝ\ˆ]SÛ›OÈÙYZÑ[™ˆÝš[™ÏÈ›Ý\ÊNÂ‚‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝÛÜÝ[\Ý]\Õ\]T™\]Y\Ý
ˆÝš[™ÏÈ[\Ý]\ËˆÝš[™ÏÈ›ÝJNÂ‚‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝÛÜÝ[\™[X\ÙS›ÝYšXØ][Û”™\]Y\Ý
ˆÝš[™ÏÈ›Ý][™Ó›ÝJNÂ‚‚š[\›˜[ÙX[Y™XÛÜ™›Ú™XÝÛÜÝ[\]˜[X][Û”™\]Y\Ý
ˆ›ÛÛÈ]Y]YS›ÝYšXØ][ÛœËˆXÚ[X[È\ÜÚYÛ›Y[Ø\›š[™Õ™\ÚÛÝ\œÊNÂ‚‹ËÈŒÈ›ÙXÝ[Ûˆ›ÝYšXØ][ÛˆÈ™XÛÜ™Âœ™XÛÜ™›Ú™XÝ[ÙLŒÔ›Ý][™Ô[JˆÝZY›Ý][™Ô[RYˆÝš[™È[RÙ^KˆÝš[™È[Ù[RÙ^KˆÝš[™ÈÙ]™\š]KˆÝš[™Ö×H\™Ù]›ÛPÛÙ\Ëˆ›ÛÛY˜][[\[˜X›Yˆ›ÛÛ[ÝÕ\Ù\“ÜÝ]ˆ›ÛÛ[ÝÑ[XZ[[]™\žKˆ›ÛÛ\ÐXÝ]™KˆÝš[™È[Q\ØÜš\[Û‹ˆ]U[YSÙ™œÙ]Ü™X]Y]ˆ]U[YSÙ™œÙ]\]Y]
NÂ‚œ™XÛÜ™›Ú™XÝ[ÙLŒÕ\Ù\”™Y™\™[˜ÙJˆÝZY™Y™\™[˜ÙRYˆÝš[™È[Ù[RÙ^KˆÝš[™ÈÙ]™\š]Kˆ›ÛÛ[\[˜X›Yˆ›ÛÛ[XZ[[˜X›Yˆ]U[YSÙ™œÙ]È]]Y[[]Ëˆ]U[YSÙ™œÙ]\]Y]
NÂ‚‚‹ËÈÌÔ“ÓWÐÓPS•TÔTÑL—ÐÓÓTUP’SUB‹ËÈØ[›ÛšXØ[›Û\È\™H›ÝÈS‘ÒS‘QT’S‘ËS‘ÒS‘QT’S‘×ÓPQ“Ò‘PÕÓPSQÑSQS•ÓPQ[™ÕTT—ÐQRS’TÕUÔ‹‚‹ËÈYØXÞH›ÛHÛÙ\È™[XZ[ˆ[\Ü˜\š[H™XÛÙÛš^™Y[[\ÙHÈ›ÛH™]\™[Y[\ÈÛÛ\]K‚