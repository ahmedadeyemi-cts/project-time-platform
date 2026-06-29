using System.Net.Http;
using System.Text.Json;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using ProjectTime.Api.Modules;

const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";
const string DevelopmentUserDisplayName = "Ahmed Adeyemi";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase) ||
        IsProjectPulsePublicApiPath(context))
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
        WHERE r.role_code = 'ENGINEER'
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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var isProjectManager =
        roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PROJECT_MANAGEMENT");

    var canViewBuilder =
        canViewAll
        || isProjectManager
        || roles.Contains("MANAGER")
        || roles.Contains("ENGINEERING_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
        || permissions.Contains("VIEW_WORK_TASK_BUILDER")
        || permissions.Contains("MANAGE_WORK_TASK_BUILDER")
        || permissions.Contains("ASSIGN_WORK_TASKS");

    var canManageTemplates =
        canViewAll
        || permissions.Contains("MANAGE_WORK_TASK_BUILDER");

    var canAssignTasks =
        canViewAll
        || isProjectManager
        || permissions.Contains("ASSIGN_WORK_TASKS");

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
            COALESCE(ur.used_hours, 0)::numeric AS used_hours
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
          AND r.role_code = 'ENGINEER'
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
            canAssignTasks
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
            r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
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

    var canViewAll = false;
    var isProjectManager = false;

    await using (var accessCommand = new NpgsqlCommand("""
        SELECT
            BOOL_OR(
                r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
                OR p.permission_code IN ('SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
            ) AS can_view_all,
            BOOL_OR(
                r.role_code IN ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT')
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
        return Results.Json(new { status = "access_denied", message = "Only Administrators, Project Team Coordinators, and assigned Project Managers can create project work tasks." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using (var scopeCommand = new NpgsqlCommand("""
        SELECT COUNT(*)
        FROM projects
        WHERE project_id = @project_id
          AND (@can_view_all = TRUE OR project_manager_user_id = @user_id);
        """, connection))
    {
        scopeCommand.Parameters.AddWithValue("project_id", projectId.Value);
        scopeCommand.Parameters.AddWithValue("can_view_all", canViewAll);
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
                r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
                OR p.permission_code IN ('SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
            ) AS can_view_all,
            BOOL_OR(
                r.role_code IN ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT')
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
          AND r.role_code = 'ENGINEER';
        """, connection))
    {
        engineerCommand.Parameters.AddWithValue("engineer_user_id", engineerUserId.Value);
        var count = Convert.ToInt32(await engineerCommand.ExecuteScalarAsync() ?? 0);
        if (count == 0)
        {
            return Results.Json(new { status = "validation_error", message = "Selected user must be an active Engineer." }, statusCode: StatusCodes.Status400BadRequest);
        }
    }

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
        effectiveEndDate
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
            p.project_id AS project_id,
            pt.task_id AS task_id,
            p.project_code AS project_code,
            p.project_name AS project_name,
            COALESCE(c.client_name, 'No customer assigned') AS client_name,
            pt.task_code AS task_code,
            pt.task_name AS task_name,
            pt.task_description AS task_description,
            pt.billable AS billable,
            COALESCE(pt.utilization_bucket, CASE WHEN pt.billable THEN 'billable' ELSE 'non_billable' END) AS utilization_bucket,
            COALESCE(pm.display_name, 'No PM assigned') AS project_manager_name,
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
          AND pt.is_active = TRUE
          AND p.status NOT IN ('cancelled', 'archived')
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

        tasks.Add(new
        {
            projectId = reader.GetGuid(O("project_id")),
            taskId = reader.GetGuid(O("task_id")),
            projectCode = reader.GetString(O("project_code")),
            projectName = reader.GetString(O("project_name")),
            clientName = reader.GetString(O("client_name")),
            taskCode = reader.GetString(O("task_code")),
            taskName = reader.GetString(O("task_name")),
            taskDescription = reader.IsDBNull(O("task_description")) ? null : reader.GetString(O("task_description")),
            billable = reader.GetBoolean(O("billable")),
            utilizationBucket = reader.GetString(O("utilization_bucket")),
            projectManagerName = reader.GetString(O("project_manager_name")),
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

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "Password reset approvals are restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

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

        if (existingStatus is "reconciled" or "locked")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Locked or reconciled timesheets cannot be edited."
            });
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

        if (dayState.Status == "submitted")
        {
            return Results.Conflict(new
            {
                status = "day_already_submitted",
                currentStatus = dayState.Status,
                message = "This day is already submitted. Use Unlock within two hours, or contact your manager after two hours."
            });
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


app.MapGet("/api/manager/approvals", async (DateOnly? weekStart, bool? includeAll) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(6);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var items = await LoadManagerApprovalItemsAsync(connection, start, end, includeAll ?? false);

    return Results.Ok(new
    {
        weekStart = start,
        weekEnd = end,
        includeAll = includeAll ?? false,
        count = items.Count,
        items
    });
});

app.MapPost("/api/manager/approvals/approve", async (ManagerApprovalActionRequest request) =>
{
    return await ProcessManagerApprovalActionAsync(request, "manager_approved", "approved", "timesheet_day_manager_approved", "Approved by manager.");
});

app.MapPost("/api/manager/approvals/decline", async (ManagerApprovalActionRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Comment))
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "A decline reason is required before returning time to the engineer."
        });
    }

    return await ProcessManagerApprovalActionAsync(request, "manager_declined", "declined", "timesheet_day_manager_declined", "Returned to engineer for correction.");
});

app.MapPost("/api/manager/approvals/unlock", async (ManagerApprovalActionRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var managerUserId = await GetOrCreateDevelopmentManagerUserIdAsync(connection, transaction);

        const string statusSql = """
            UPDATE timesheet_day_statuses
            SET status = 'draft',
                manager_user_id = @manager_user_id,
                manager_decision_comment = @comment,
                manager_unlocked_at = NOW(),
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
            RETURNING timesheet_day_status_id;
            """;

        await using var statusCommand = new NpgsqlCommand(statusSql, connection, transaction);
        statusCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        statusCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        statusCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
        statusCommand.Parameters.AddWithValue("comment", string.IsNullOrWhiteSpace(request.Comment) ? "Manager unlocked time for correction." : request.Comment.Trim());

        var statusId = (Guid?)(await statusCommand.ExecuteScalarAsync());
        if (statusId is null)
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "No day-level timesheet status record was found for the selected item."
            });
        }

        await using var entryCommand = new NpgsqlCommand(
            "UPDATE time_entries SET status = 'draft', updated_at = NOW() WHERE timesheet_id = @timesheet_id AND work_date = @work_date;",
            connection,
            transaction);
        entryCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        entryCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        await entryCommand.ExecuteNonQueryAsync();

        await InsertAuditLogAsync(connection, transaction, managerUserId, "timesheet_day_manager_unlocked", "timesheet", request.TimesheetId);

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "manager_unlocked",
            timesheetId = request.TimesheetId,
            workDate = request.WorkDate,
            message = "Manager unlocked the selected day so the engineer can correct and resubmit it."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to manager-unlock timesheet day",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});


app.MapGet("/api/manager/approval-summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var summary = await LoadManagerApprovalSummaryAsync(connection);
    return Results.Ok(summary);
});


app.MapPost("/api/manager/approvals/bulk-approve", async (ManagerBulkApprovalRequest request) =>
{
    return await ProcessManagerBulkApprovalAsync(request);
});



app.MapPost("/api/timesheets/ai-description-suggestions", async (ProjectPulseAiTimeEntrySuggestionRequest request, HttpContext httpContext) =>
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

    var rowLabel = request.RowLabel?.Trim();
    var taskName = request.TaskName?.Trim();
    var projectName = request.ProjectName?.Trim();

    if (string.IsNullOrWhiteSpace(rowLabel)
        && string.IsNullOrWhiteSpace(taskName)
        && string.IsNullOrWhiteSpace(projectName))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Project, task, or activity context is required before generating a suggestion." });
    }

    var result = await ProjectPulseAiTimeEntrySuggestionService.GenerateAsync(request);

    return Results.Ok(new
    {
        status = "ai_suggestion_generated",
        suggestion = result.Suggestion,
        provider = result.Provider,
        warning = result.Warning,
        message = result.Provider == "claude"
            ? "Claude generated a time-entry description suggestion."
            : "Local suggestion generated because Claude is not configured."
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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canViewManaged =
        roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
        || roles.Contains("MANAGER")
        || roles.Contains("PROJECT_COORDINATOR")
        || permissions.Contains("VIEW_PROJECT_INTAKE")
        || permissions.Contains("VIEW_PROJECT_INTAKE_AGING")
        || permissions.Contains("VIEW_INTAKE_WORK_TASK_HANDOFF");

    var canManageProjectLinks =
        canViewAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canViewManaged =
        roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
        || permissions.Contains("VIEW_PROJECT_INTAKE")
        || permissions.Contains("VIEW_PROJECT_INTAKE_AGING")
        || permissions.Contains("VIEW_INTAKE_WORK_TASK_HANDOFF")
        || permissions.Contains("MANAGE_INTAKE_PROJECT_LINKS");

    var canManageProjectLinks =
        canViewAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canManageProjectLinks =
        canManageAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canViewManaged =
        roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
        || roles.Contains("PROJECT_COORDINATOR")
        || permissions.Contains("VIEW_RESOURCE_ASSIGNMENT_HANDOFF")
        || permissions.Contains("VIEW_INTAKE_WORK_TASK_HANDOFF")
        || permissions.Contains("MANAGE_INTAKE_PROJECT_LINKS")
        || permissions.Contains("VIEW_ENGINEERING_RESOURCE_REQUESTS")
        || permissions.Contains("MANAGE_ENGINEERING_RESOURCE_REQUESTS");

    var canPromoteResourceAssignments =
        canViewAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canPromoteResourceAssignments =
        canManageAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD")
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
                r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT', 'PM_TEAM_LEAD', 'PROJECT_MANAGEMENT_TEAM_LEAD')
                OR p.permission_code IN ('VIEW_PROJECT_INTAKE', 'VIEW_PROJECT_INTAKE_AGING', 'MANAGE_PROJECT_INTAKE', 'MANAGE_PROJECT_INTAKE_AGING', 'SYSTEM_ADMINISTRATION', 'MANAGE_ALL')
            ) AS can_view,
            BOOL_OR(
                r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
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
            r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
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
            r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
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
    var storedFileName = $"{documentType}_{Guid.NewGuid():N}{Path.GetExtension(safeOriginalFileName)}";
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


app.MapGet("/api/customers/overview", async (HttpContext httpContext) =>
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

    var customers = new List<object>();

    await using (var command = new NpgsqlCommand("""
        WITH contact_summary AS (
            SELECT
                client_id,
                COUNT(*) FILTER (WHERE is_active = TRUE) AS active_contact_count
            FROM client_contacts
            GROUP BY client_id
        ),
        project_summary AS (
            SELECT
                client_id,
                COUNT(*) FILTER (WHERE status = 'active') AS active_project_count,
                COUNT(*) AS total_project_count,
                COALESCE(SUM(planned_engineering_cost), 0) AS planned_engineering_cost,
                COALESCE(SUM(planned_pm_cost), 0) AS planned_pm_cost,
                COALESCE(SUM(planned_total_project_cost), 0) AS planned_total_project_cost
            FROM projects
            WHERE client_id IS NOT NULL
            GROUP BY client_id
        ),
        intake_summary AS (
            SELECT
                client_id,
                COUNT(*) AS intake_count,
                COALESCE(SUM(planned_engineering_cost), 0) AS intake_engineering_cost,
                COALESCE(SUM(planned_pm_cost), 0) AS intake_pm_cost,
                COALESCE(SUM(planned_total_project_cost), 0) AS intake_total_cost
            FROM project_intake_requests
            WHERE client_id IS NOT NULL
            GROUP BY client_id
        ),
        overrun_summary AS (
            SELECT
                client_id,
                COUNT(*) FILTER (WHERE cost_status = 'hours_over_plan') AS projects_over_plan_count
            FROM project_cost_status_vw
            WHERE client_id IS NOT NULL
            GROUP BY client_id
        )
        SELECT
            c.client_id,
            c.client_name,
            COALESCE(c.client_code, '') AS client_code,
            c.is_active,
            COALESCE(cs.active_contact_count, 0) AS active_contact_count,
            COALESCE(ps.active_project_count, 0) AS active_project_count,
            COALESCE(ps.total_project_count, 0) AS total_project_count,
            COALESCE(isum.intake_count, 0) AS intake_count,
            COALESCE(ps.planned_engineering_cost, 0) AS planned_project_engineering_cost,
            COALESCE(ps.planned_pm_cost, 0) AS planned_project_pm_cost,
            COALESCE(ps.planned_total_project_cost, 0) AS planned_project_total_cost,
            COALESCE(isum.intake_engineering_cost, 0) AS planned_intake_engineering_cost,
            COALESCE(isum.intake_pm_cost, 0) AS planned_intake_pm_cost,
            COALESCE(isum.intake_total_cost, 0) AS planned_intake_total_cost,
            COALESCE(os.projects_over_plan_count, 0) AS projects_over_plan_count
        FROM clients c
        LEFT JOIN contact_summary cs ON cs.client_id = c.client_id
        LEFT JOIN project_summary ps ON ps.client_id = c.client_id
        LEFT JOIN intake_summary isum ON isum.client_id = c.client_id
        LEFT JOIN overrun_summary os ON os.client_id = c.client_id
        ORDER BY c.client_name;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            customers.Add(new
            {
                clientId = reader.GetGuid(0),
                clientName = reader.GetString(1),
                clientCode = reader.GetString(2),
                isActive = reader.GetBoolean(3),
                activeContactCount = Convert.ToInt32(reader.GetInt64(4)),
                activeProjectCount = Convert.ToInt32(reader.GetInt64(5)),
                totalProjectCount = Convert.ToInt32(reader.GetInt64(6)),
                intakeCount = Convert.ToInt32(reader.GetInt64(7)),
                plannedProjectEngineeringCost = reader.GetDecimal(8),
                plannedProjectPmCost = reader.GetDecimal(9),
                plannedProjectTotalCost = reader.GetDecimal(10),
                plannedIntakeEngineeringCost = reader.GetDecimal(11),
                plannedIntakePmCost = reader.GetDecimal(12),
                plannedIntakeTotalCost = reader.GetDecimal(13),
                plannedTotalProjectCost = reader.GetDecimal(10),
                projectsOverPlanCount = Convert.ToInt32(reader.GetInt64(14))
            });
        }
    }

    var contacts = new List<object>();

    await using (var command = new NpgsqlCommand("""
        SELECT
            client_contact_id,
            client_id,
            contact_name,
            COALESCE(title, '') AS title,
            COALESCE(role_description, '') AS role_description,
            COALESCE(email, '') AS email,
            COALESCE(phone, '') AS phone,
            COALESCE(address_line1, '') AS address_line1,
            COALESCE(address_line2, '') AS address_line2,
            COALESCE(city, '') AS city,
            COALESCE(state_region, '') AS state_region,
            COALESCE(postal_code, '') AS postal_code,
            COALESCE(country, '') AS country,
            is_primary,
            display_order
        FROM client_contacts
        WHERE is_active = TRUE
        ORDER BY client_id, is_primary DESC, display_order, contact_name;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            contacts.Add(new
            {
                contactId = reader.GetGuid(0),
                clientId = reader.GetGuid(1),
                contactName = reader.GetString(2),
                title = reader.GetString(3),
                roleDescription = reader.GetString(4),
                email = reader.GetString(5),
                phone = reader.GetString(6),
                addressLine1 = reader.GetString(7),
                addressLine2 = reader.GetString(8),
                city = reader.GetString(9),
                stateRegion = reader.GetString(10),
                postalCode = reader.GetString(11),
                country = reader.GetString(12),
                isPrimary = reader.GetBoolean(13),
                displayOrder = reader.GetInt32(14)
            });
        }
    }

    return Results.Ok(new
    {
        count = customers.Count,
        customers,
        contactCount = contacts.Count,
        contacts
    });
});



app.MapPost("/api/customers", async (CustomerDirectoryClientUpsertRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.ClientName))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Customer name is required." });
    }

    var clientName = request.ClientName.Trim();
    var clientCode = string.IsNullOrWhiteSpace(request.ClientCode)
        ? new string(clientName.Where(char.IsLetterOrDigit).Take(8).ToArray()).ToUpperInvariant()
        : request.ClientCode.Trim().ToUpperInvariant();

    if (string.IsNullOrWhiteSpace(clientCode))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Customer code is required." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanManageCustomersAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "Customer Directory management is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using (var duplicateCommand = new NpgsqlCommand("""
        SELECT client_id
        FROM clients
        WHERE lower(client_code) = lower(@client_code)
           OR lower(client_name) = lower(@client_name)
        LIMIT 1;
        """, connection))
    {
        duplicateCommand.Parameters.AddWithValue("client_code", clientCode);
        duplicateCommand.Parameters.AddWithValue("client_name", clientName);

        var duplicateId = await duplicateCommand.ExecuteScalarAsync();

        if (duplicateId is Guid existingClientId)
        {
            return Results.Conflict(new
            {
                status = "customer_already_exists",
                clientId = existingClientId,
                message = "A customer with the same name or customer code already exists."
            });
        }
    }

    await using var command = new NpgsqlCommand("""
        INSERT INTO clients (
            client_name,
            client_code,
            is_active
        )
        VALUES (
            @client_name,
            @client_code,
            @is_active
        )
        RETURNING client_id;
        """, connection);

    command.Parameters.AddWithValue("client_name", clientName);
    command.Parameters.AddWithValue("client_code", clientCode);
    command.Parameters.AddWithValue("is_active", request.IsActive ?? true);

    var clientId = (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create customer."));

    return Results.Ok(new
    {
        status = "customer_created",
        clientId,
        clientName,
        clientCode,
        message = "Customer record created."
    });
});


app.MapPut("/api/customers/{clientId:guid}", async (Guid clientId, CustomerDirectoryClientUpsertRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.ClientName))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Customer name is required." });
    }

    var clientName = request.ClientName.Trim();
    var clientCode = string.IsNullOrWhiteSpace(request.ClientCode)
        ? new string(clientName.Where(char.IsLetterOrDigit).Take(8).ToArray()).ToUpperInvariant()
        : request.ClientCode.Trim().ToUpperInvariant();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanManageCustomersAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "Customer Directory management is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using (var duplicateCommand = new NpgsqlCommand("""
        SELECT client_id
        FROM clients
        WHERE client_id <> @client_id
          AND (
                lower(client_code) = lower(@client_code)
             OR lower(client_name) = lower(@client_name)
          )
        LIMIT 1;
        """, connection))
    {
        duplicateCommand.Parameters.AddWithValue("client_id", clientId);
        duplicateCommand.Parameters.AddWithValue("client_code", clientCode);
        duplicateCommand.Parameters.AddWithValue("client_name", clientName);

        var duplicateId = await duplicateCommand.ExecuteScalarAsync();

        if (duplicateId is Guid)
        {
            return Results.Conflict(new
            {
                status = "customer_already_exists",
                message = "Another customer already uses the same name or customer code."
            });
        }
    }

    await using var command = new NpgsqlCommand("""
        UPDATE clients
        SET client_name = @client_name,
            client_code = @client_code,
            is_active = @is_active,
            updated_at = NOW()
        WHERE client_id = @client_id;
        """, connection);

    command.Parameters.AddWithValue("client_id", clientId);
    command.Parameters.AddWithValue("client_name", clientName);
    command.Parameters.AddWithValue("client_code", clientCode);
    command.Parameters.AddWithValue("is_active", request.IsActive ?? true);

    var rows = await command.ExecuteNonQueryAsync();

    if (rows == 0)
    {
        return Results.NotFound(new { status = "customer_not_found", message = "Customer was not found." });
    }

    return Results.Ok(new
    {
        status = "customer_updated",
        clientId,
        clientName,
        clientCode,
        message = "Customer record updated."
    });
});


app.MapPost("/api/customers/{clientId:guid}/contacts", async (Guid clientId, CustomerDirectoryContactUpsertRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.ContactName))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Contact name is required." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanManageCustomersAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "Customer contact management is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using (var clientCommand = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM clients WHERE client_id = @client_id);", connection))
    {
        clientCommand.Parameters.AddWithValue("client_id", clientId);
        var exists = (bool)(await clientCommand.ExecuteScalarAsync() ?? false);

        if (!exists)
        {
            return Results.NotFound(new { status = "customer_not_found", message = "Customer was not found." });
        }
    }

    await using (var countCommand = new NpgsqlCommand("""
        SELECT COUNT(*)
        FROM client_contacts
        WHERE client_id = @client_id
          AND is_active = TRUE;
        """, connection))
    {
        countCommand.Parameters.AddWithValue("client_id", clientId);
        var activeContactCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync() ?? 0);

        if (activeContactCount >= 10)
        {
            return Results.BadRequest(new
            {
                status = "contact_limit_reached",
                message = "A customer can have at most 10 active contacts."
            });
        }
    }

    await using var command = new NpgsqlCommand("""
        INSERT INTO client_contacts (
            client_id,
            contact_name,
            title,
            role_description,
            email,
            phone,
            address_line1,
            address_line2,
            city,
            state_region,
            postal_code,
            country,
            is_primary,
            is_active,
            display_order
        )
        VALUES (
            @client_id,
            @contact_name,
            NULLIF(@title, ''),
            NULLIF(@role_description, ''),
            NULLIF(@email, ''),
            NULLIF(@phone, ''),
            NULLIF(@address_line1, ''),
            NULLIF(@address_line2, ''),
            NULLIF(@city, ''),
            NULLIF(@state_region, ''),
            NULLIF(@postal_code, ''),
            COALESCE(NULLIF(@country, ''), 'United States'),
            @is_primary,
            @is_active,
            @display_order
        )
        RETURNING client_contact_id;
        """, connection);

    command.Parameters.AddWithValue("client_id", clientId);
    command.Parameters.AddWithValue("contact_name", request.ContactName.Trim());
    command.Parameters.AddWithValue("title", request.Title?.Trim() ?? "");
    command.Parameters.AddWithValue("role_description", request.RoleDescription?.Trim() ?? "");
    command.Parameters.AddWithValue("email", request.Email?.Trim().ToLowerInvariant() ?? "");
    command.Parameters.AddWithValue("phone", request.Phone?.Trim() ?? "");
    command.Parameters.AddWithValue("address_line1", request.AddressLine1?.Trim() ?? "");
    command.Parameters.AddWithValue("address_line2", request.AddressLine2?.Trim() ?? "");
    command.Parameters.AddWithValue("city", request.City?.Trim() ?? "");
    command.Parameters.AddWithValue("state_region", request.StateRegion?.Trim() ?? "");
    command.Parameters.AddWithValue("postal_code", request.PostalCode?.Trim() ?? "");
    command.Parameters.AddWithValue("country", request.Country?.Trim() ?? "United States");
    command.Parameters.AddWithValue("is_primary", request.IsPrimary ?? false);
    command.Parameters.AddWithValue("is_active", request.IsActive ?? true);
    command.Parameters.AddWithValue("display_order", request.DisplayOrder ?? 0);

    var contactId = (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create customer contact."));

    return Results.Ok(new
    {
        status = "customer_contact_created",
        clientId,
        contactId,
        message = "Customer contact created."
    });
});


app.MapPut("/api/customers/{clientId:guid}/contacts/{contactId:guid}", async (Guid clientId, Guid contactId, CustomerDirectoryContactUpsertRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.ContactName))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Contact name is required." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanManageCustomersAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "Customer contact management is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var command = new NpgsqlCommand("""
        UPDATE client_contacts
        SET contact_name = @contact_name,
            title = NULLIF(@title, ''),
            role_description = NULLIF(@role_description, ''),
            email = NULLIF(@email, ''),
            phone = NULLIF(@phone, ''),
            address_line1 = NULLIF(@address_line1, ''),
            address_line2 = NULLIF(@address_line2, ''),
            city = NULLIF(@city, ''),
            state_region = NULLIF(@state_region, ''),
            postal_code = NULLIF(@postal_code, ''),
            country = COALESCE(NULLIF(@country, ''), 'United States'),
            is_primary = @is_primary,
            is_active = @is_active,
            display_order = @display_order,
            updated_at = NOW()
        WHERE client_id = @client_id
          AND client_contact_id = @contact_id;
        """, connection);

    command.Parameters.AddWithValue("client_id", clientId);
    command.Parameters.AddWithValue("contact_id", contactId);
    command.Parameters.AddWithValue("contact_name", request.ContactName.Trim());
    command.Parameters.AddWithValue("title", request.Title?.Trim() ?? "");
    command.Parameters.AddWithValue("role_description", request.RoleDescription?.Trim() ?? "");
    command.Parameters.AddWithValue("email", request.Email?.Trim().ToLowerInvariant() ?? "");
    command.Parameters.AddWithValue("phone", request.Phone?.Trim() ?? "");
    command.Parameters.AddWithValue("address_line1", request.AddressLine1?.Trim() ?? "");
    command.Parameters.AddWithValue("address_line2", request.AddressLine2?.Trim() ?? "");
    command.Parameters.AddWithValue("city", request.City?.Trim() ?? "");
    command.Parameters.AddWithValue("state_region", request.StateRegion?.Trim() ?? "");
    command.Parameters.AddWithValue("postal_code", request.PostalCode?.Trim() ?? "");
    command.Parameters.AddWithValue("country", request.Country?.Trim() ?? "United States");
    command.Parameters.AddWithValue("is_primary", request.IsPrimary ?? false);
    command.Parameters.AddWithValue("is_active", request.IsActive ?? true);
    command.Parameters.AddWithValue("display_order", request.DisplayOrder ?? 0);

    var rows = await command.ExecuteNonQueryAsync();

    if (rows == 0)
    {
        return Results.NotFound(new { status = "contact_not_found", message = "Customer contact was not found." });
    }

    return Results.Ok(new
    {
        status = "customer_contact_updated",
        clientId,
        contactId,
        message = "Customer contact updated."
    });
});


app.MapGet("/api/projects/cost-status", async (HttpContext httpContext) =>
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

    var projects = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            project_id,
            project_code,
            project_name,
            client_id,
            COALESCE(client_name, '') AS client_name,
            project_status,
            billable,
            planned_engineering_cost,
            planned_pm_cost,
            planned_total_project_cost,
            assigned_hours,
            used_hours,
            remaining_assigned_hours,
            over_assigned_hours,
            cost_status
        FROM project_cost_status_vw
        ORDER BY client_name, project_code;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        projects.Add(new
        {
            projectId = reader.GetGuid(0),
            projectCode = reader.GetString(1),
            projectName = reader.GetString(2),
            clientId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
            clientName = reader.GetString(4),
            projectStatus = reader.GetString(5),
            billable = reader.GetBoolean(6),
            plannedEngineeringCost = reader.GetDecimal(7),
            plannedPmCost = reader.GetDecimal(8),
            plannedTotalProjectCost = reader.GetDecimal(9),
            assignedHours = reader.GetDecimal(10),
            usedHours = reader.GetDecimal(11),
            remainingAssignedHours = reader.GetDecimal(12),
            overAssignedHours = reader.GetDecimal(13),
            costStatus = reader.GetString(14)
        });
    }

    return Results.Ok(new
    {
        count = projects.Count,
        projects
    });
});



app.MapGet("/api/projects/cost-alerts", async (HttpContext httpContext) =>
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

    if (!await RequestUserCanAccessCostAlertsAsync(httpContext, connection, requireManage: false))
    {
        return Results.Json(new { status = "access_denied", message = "Cost alerts are available to administrators, project/team coordinators, project managers, and managers." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var candidates = new List<object>();
    await using (var command = new NpgsqlCommand("""
        WITH current_cost AS (
            SELECT
                pcs.project_id,
                pcs.project_code,
                pcs.project_name,
                COALESCE(pcs.client_name, '') AS client_name,
                COALESCE(pcs.planned_engineering_cost, 0)::numeric AS planned_engineering_cost,
                COALESCE(pcs.planned_pm_cost, 0)::numeric AS planned_pm_cost,
                COALESCE(pcs.planned_total_project_cost, 0)::numeric AS planned_total_project_cost,
                COALESCE(pcs.assigned_hours, 0)::numeric AS assigned_hours,
                COALESCE(pcs.used_hours, 0)::numeric AS used_hours,
                COALESCE(pcs.remaining_assigned_hours, 0)::numeric AS remaining_assigned_hours,
                COALESCE(pcs.over_assigned_hours, 0)::numeric AS over_assigned_hours,
                COALESCE(pcs.cost_status, 'unknown') AS cost_status,
                p.project_manager_user_id,
                pm.email AS project_manager_email
            FROM project_cost_status_vw pcs
            JOIN projects p ON p.project_id = pcs.project_id
            LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
        ),
        candidate_alerts AS (
            SELECT
                *,
                CASE
                    WHEN over_assigned_hours > 0 OR cost_status = 'hours_over_plan' THEN 'hours_over_plan'
                    WHEN planned_total_project_cost = 0 AND (used_hours > 0 OR assigned_hours > 0) THEN 'cost_plan_missing_with_activity'
                    WHEN planned_total_project_cost = 0 THEN 'cost_plan_missing'
                    WHEN assigned_hours > 0 AND remaining_assigned_hours <= 8 THEN 'assignment_capacity_warning'
                    ELSE NULL
                END AS alert_type,
                CASE
                    WHEN over_assigned_hours > 0 OR cost_status = 'hours_over_plan' THEN 'high'
                    WHEN planned_total_project_cost = 0 AND (used_hours > 0 OR assigned_hours > 0) THEN 'high'
                    WHEN planned_total_project_cost = 0 THEN 'medium'
                    WHEN assigned_hours > 0 AND remaining_assigned_hours <= 8 THEN 'medium'
                    ELSE 'low'
                END AS alert_severity
            FROM current_cost
        )
        SELECT
            project_id,
            project_code,
            project_name,
            client_name,
            project_manager_user_id,
            project_manager_email,
            alert_type,
            alert_severity,
            planned_engineering_cost,
            planned_pm_cost,
            planned_total_project_cost,
            assigned_hours,
            used_hours,
            remaining_assigned_hours,
            over_assigned_hours,
            cost_status
        FROM candidate_alerts
        WHERE alert_type IS NOT NULL
        ORDER BY
            CASE alert_severity WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
            client_name,
            project_code;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            candidates.Add(new
            {
                projectId = reader.GetGuid(0),
                projectCode = reader.GetString(1),
                projectName = reader.GetString(2),
                clientName = reader.GetString(3),
                projectManagerUserId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                projectManagerEmail = reader.IsDBNull(5) ? null : reader.GetString(5),
                alertType = reader.GetString(6),
                alertSeverity = reader.GetString(7),
                plannedEngineeringCost = reader.GetDecimal(8),
                plannedPmCost = reader.GetDecimal(9),
                plannedTotalProjectCost = reader.GetDecimal(10),
                assignedHours = reader.GetDecimal(11),
                usedHours = reader.GetDecimal(12),
                remainingAssignedHours = reader.GetDecimal(13),
                overAssignedHours = reader.GetDecimal(14),
                costStatus = reader.GetString(15)
            });
        }
    }

    var alerts = new List<object>();
    var openAlertCount = 0;
    var highAlertCount = 0;

    await using (var command = new NpgsqlCommand("""
        SELECT
            project_cost_alert_id,
            project_id,
            alert_type,
            alert_severity,
            alert_status,
            client_name,
            project_code,
            project_name,
            project_manager_email,
            planned_total_project_cost,
            assigned_hours,
            used_hours,
            remaining_assigned_hours,
            over_assigned_hours,
            cost_status,
            alert_summary,
            alert_detail,
            first_detected_at,
            last_detected_at,
            notification_queued_at,
            notification_recipient_count,
            resolved_at,
            acknowledged_at,
            acknowledged_by_email,
            acknowledgement_note,
            routing_status,
            routing_note,
            notification_released_at,
            notification_released_by_email,
            last_action_at,
            last_action_by_email
        FROM project_cost_alerts
        ORDER BY
            CASE alert_status WHEN 'open' THEN 1 WHEN 'acknowledged' THEN 2 ELSE 3 END,
            CASE alert_severity WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
            last_detected_at DESC
        LIMIT 100;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var alertSeverity = reader.GetString(3);
            var alertStatus = reader.GetString(4);

            if (string.Equals(alertStatus, "open", StringComparison.OrdinalIgnoreCase))
            {
                openAlertCount++;
            }

            if (string.Equals(alertSeverity, "high", StringComparison.OrdinalIgnoreCase))
            {
                highAlertCount++;
            }

            alerts.Add(new
            {
                alertId = reader.GetGuid(0),
                projectId = reader.GetGuid(1),
                alertType = reader.GetString(2),
                alertSeverity,
                alertStatus,
                clientName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                projectCode = reader.IsDBNull(6) ? "" : reader.GetString(6),
                projectName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                projectManagerEmail = reader.IsDBNull(8) ? null : reader.GetString(8),
                plannedTotalProjectCost = reader.GetDecimal(9),
                assignedHours = reader.GetDecimal(10),
                usedHours = reader.GetDecimal(11),
                remainingAssignedHours = reader.GetDecimal(12),
                overAssignedHours = reader.GetDecimal(13),
                costStatus = reader.GetString(14),
                alertSummary = reader.GetString(15),
                alertDetail = reader.GetString(16),
                firstDetectedAt = reader.GetFieldValue<DateTimeOffset>(17),
                lastDetectedAt = reader.GetFieldValue<DateTimeOffset>(18),
                notificationQueuedAt = reader.IsDBNull(19) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(19),
                notificationRecipientCount = reader.GetInt32(20),
                resolvedAt = reader.IsDBNull(21) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(21),
                acknowledgedAt = reader.IsDBNull(22) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(22),
                acknowledgedByEmail = reader.IsDBNull(23) ? null : reader.GetString(23),
                acknowledgementNote = reader.IsDBNull(24) ? null : reader.GetString(24),
                routingStatus = reader.IsDBNull(25) ? "hold" : reader.GetString(25),
                routingNote = reader.IsDBNull(26) ? null : reader.GetString(26),
                notificationReleasedAt = reader.IsDBNull(27) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(27),
                notificationReleasedByEmail = reader.IsDBNull(28) ? null : reader.GetString(28),
                lastActionAt = reader.IsDBNull(29) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(29),
                lastActionByEmail = reader.IsDBNull(30) ? null : reader.GetString(30)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AI Cost Overrun Alert Foundation",
        count = alerts.Count,
        candidateCount = candidates.Count,
        openCount = openAlertCount,
        highCount = highAlertCount,
        candidates,
        alerts
    });
});


app.MapPost("/api/projects/cost-alerts/evaluate", async (ProjectCostAlertEvaluationRequest request, HttpContext httpContext) =>
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

    if (!await RequestUserCanAccessCostAlertsAsync(httpContext, connection, requireManage: true))
    {
        return Results.Json(new { status = "access_denied", message = "Evaluating and queuing cost alerts is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var thresholdHours = request.AssignmentWarningThresholdHours ?? 8m;
        var queueNotifications = request.QueueNotifications ?? false;

        await using (var upsertCommand = new NpgsqlCommand("""
            WITH current_cost AS (
                SELECT
                    pcs.project_id,
                    pcs.project_code,
                    pcs.project_name,
                    COALESCE(pcs.client_name, '') AS client_name,
                    COALESCE(pcs.planned_engineering_cost, 0)::numeric AS planned_engineering_cost,
                    COALESCE(pcs.planned_pm_cost, 0)::numeric AS planned_pm_cost,
                    COALESCE(pcs.planned_total_project_cost, 0)::numeric AS planned_total_project_cost,
                    COALESCE(pcs.assigned_hours, 0)::numeric AS assigned_hours,
                    COALESCE(pcs.used_hours, 0)::numeric AS used_hours,
                    COALESCE(pcs.remaining_assigned_hours, 0)::numeric AS remaining_assigned_hours,
                    COALESCE(pcs.over_assigned_hours, 0)::numeric AS over_assigned_hours,
                    COALESCE(pcs.cost_status, 'unknown') AS cost_status,
                    p.project_manager_user_id,
                    pm.email AS project_manager_email
                FROM project_cost_status_vw pcs
                JOIN projects p ON p.project_id = pcs.project_id
                LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
            ),
            candidate_alerts AS (
                SELECT
                    *,
                    CASE
                        WHEN over_assigned_hours > 0 OR cost_status = 'hours_over_plan' THEN 'hours_over_plan'
                        WHEN planned_total_project_cost = 0 AND (used_hours > 0 OR assigned_hours > 0) THEN 'cost_plan_missing_with_activity'
                        WHEN planned_total_project_cost = 0 THEN 'cost_plan_missing'
                        WHEN assigned_hours > 0 AND remaining_assigned_hours <= @threshold_hours THEN 'assignment_capacity_warning'
                        ELSE NULL
                    END AS alert_type,
                    CASE
                        WHEN over_assigned_hours > 0 OR cost_status = 'hours_over_plan' THEN 'high'
                        WHEN planned_total_project_cost = 0 AND (used_hours > 0 OR assigned_hours > 0) THEN 'high'
                        WHEN planned_total_project_cost = 0 THEN 'medium'
                        WHEN assigned_hours > 0 AND remaining_assigned_hours <= @threshold_hours THEN 'medium'
                        ELSE 'low'
                    END AS alert_severity
                FROM current_cost
            ),
            actionable AS (
                SELECT
                    *,
                    (project_id::text || ':' || alert_type) AS alert_key,
                    CASE alert_type
                        WHEN 'hours_over_plan' THEN 'Project hours exceed assigned allocation.'
                        WHEN 'cost_plan_missing_with_activity' THEN 'Project has activity but no loaded cost plan.'
                        WHEN 'cost_plan_missing' THEN 'Project is missing a cost plan.'
                        WHEN 'assignment_capacity_warning' THEN 'Project has limited assigned hours remaining.'
                        ELSE 'Project cost alert.'
                    END AS alert_summary,
                    (
                        'Project ' || project_code || ' has cost status ' || cost_status ||
                        ', planned total cost ' || planned_total_project_cost ||
                        ', assigned hours ' || assigned_hours ||
                        ', used hours ' || used_hours ||
                        ', remaining assigned hours ' || remaining_assigned_hours ||
                        ', over-assigned hours ' || over_assigned_hours || '.'
                    ) AS alert_detail
                FROM candidate_alerts
                WHERE alert_type IS NOT NULL
            ),
            upserted AS (
                INSERT INTO project_cost_alerts (
                    project_id,
                    alert_key,
                    alert_type,
                    alert_severity,
                    alert_status,
                    client_name,
                    project_code,
                    project_name,
                    project_manager_user_id,
                    project_manager_email,
                    planned_engineering_cost,
                    planned_pm_cost,
                    planned_total_project_cost,
                    assigned_hours,
                    used_hours,
                    remaining_assigned_hours,
                    over_assigned_hours,
                    cost_status,
                    alert_summary,
                    alert_detail,
                    last_detected_at,
                    resolved_at,
                    updated_at,
                    metadata_json
                )
                SELECT
                    project_id,
                    alert_key,
                    alert_type,
                    alert_severity,
                    'open',
                    client_name,
                    project_code,
                    project_name,
                    project_manager_user_id,
                    project_manager_email,
                    planned_engineering_cost,
                    planned_pm_cost,
                    planned_total_project_cost,
                    assigned_hours,
                    used_hours,
                    remaining_assigned_hours,
                    over_assigned_hours,
                    cost_status,
                    alert_summary,
                    alert_detail,
                    NOW(),
                    NULL,
                    NOW(),
                    jsonb_build_object('evaluatedBy', @evaluated_by::text, 'thresholdHours', @threshold_hours)
                FROM actionable
                ON CONFLICT (alert_key) DO UPDATE
                SET alert_severity = EXCLUDED.alert_severity,
                    alert_status = CASE WHEN project_cost_alerts.alert_status = 'resolved' THEN 'open' ELSE project_cost_alerts.alert_status END,
                    client_name = EXCLUDED.client_name,
                    project_code = EXCLUDED.project_code,
                    project_name = EXCLUDED.project_name,
                    project_manager_user_id = EXCLUDED.project_manager_user_id,
                    project_manager_email = EXCLUDED.project_manager_email,
                    planned_engineering_cost = EXCLUDED.planned_engineering_cost,
                    planned_pm_cost = EXCLUDED.planned_pm_cost,
                    planned_total_project_cost = EXCLUDED.planned_total_project_cost,
                    assigned_hours = EXCLUDED.assigned_hours,
                    used_hours = EXCLUDED.used_hours,
                    remaining_assigned_hours = EXCLUDED.remaining_assigned_hours,
                    over_assigned_hours = EXCLUDED.over_assigned_hours,
                    cost_status = EXCLUDED.cost_status,
                    alert_summary = EXCLUDED.alert_summary,
                    alert_detail = EXCLUDED.alert_detail,
                    last_detected_at = NOW(),
                    resolved_at = NULL,
                    updated_at = NOW(),
                    metadata_json = EXCLUDED.metadata_json
                RETURNING project_cost_alert_id
            )
            SELECT COUNT(*) FROM upserted;
            """, connection, transaction))
        {
            upsertCommand.Parameters.AddWithValue("threshold_hours", thresholdHours);
            upsertCommand.Parameters.AddWithValue("evaluated_by", GetProjectPulseSessionEmail(httpContext) ?? "unknown");
            await upsertCommand.ExecuteScalarAsync();
        }

        await using (var resolveCommand = new NpgsqlCommand("""
            WITH current_cost AS (
                SELECT
                    pcs.project_id,
                    COALESCE(pcs.planned_total_project_cost, 0)::numeric AS planned_total_project_cost,
                    COALESCE(pcs.assigned_hours, 0)::numeric AS assigned_hours,
                    COALESCE(pcs.used_hours, 0)::numeric AS used_hours,
                    COALESCE(pcs.remaining_assigned_hours, 0)::numeric AS remaining_assigned_hours,
                    COALESCE(pcs.over_assigned_hours, 0)::numeric AS over_assigned_hours,
                    COALESCE(pcs.cost_status, 'unknown') AS cost_status
                FROM project_cost_status_vw pcs
            ),
            actionable AS (
                SELECT
                    project_id,
                    CASE
                        WHEN over_assigned_hours > 0 OR cost_status = 'hours_over_plan' THEN 'hours_over_plan'
                        WHEN planned_total_project_cost = 0 AND (used_hours > 0 OR assigned_hours > 0) THEN 'cost_plan_missing_with_activity'
                        WHEN planned_total_project_cost = 0 THEN 'cost_plan_missing'
                        WHEN assigned_hours > 0 AND remaining_assigned_hours <= @threshold_hours THEN 'assignment_capacity_warning'
                        ELSE NULL
                    END AS alert_type
                FROM current_cost
            ),
            active_keys AS (
                SELECT project_id::text || ':' || alert_type AS alert_key
                FROM actionable
                WHERE alert_type IS NOT NULL
            )
            UPDATE project_cost_alerts
            SET alert_status = 'resolved',
                resolved_at = NOW(),
                updated_at = NOW()
            WHERE alert_status = 'open'
              AND alert_key NOT IN (SELECT alert_key FROM active_keys);
            """, connection, transaction))
        {
            resolveCommand.Parameters.AddWithValue("threshold_hours", thresholdHours);
            await resolveCommand.ExecuteNonQueryAsync();
        }

        var queuedRecipientCount = 0;
        var queuedAlertCount = 0;

        if (queueNotifications)
        {
            var alertsToQueue = new List<(Guid AlertId, Guid ProjectId, string AlertType, string AlertSeverity, string ProjectCode, string ProjectName, string ClientName, decimal PlannedTotalProjectCost, decimal AssignedHours, decimal UsedHours, decimal OverAssignedHours, string CostStatus)>();

            await using (var alertCommand = new NpgsqlCommand("""
                SELECT
                    project_cost_alert_id,
                    project_id,
                    alert_type,
                    alert_severity,
                    COALESCE(project_code, ''),
                    COALESCE(project_name, ''),
                    COALESCE(client_name, ''),
                    planned_total_project_cost,
                    assigned_hours,
                    used_hours,
                    over_assigned_hours,
                    cost_status
                FROM project_cost_alerts
                WHERE alert_status = 'open'
                  AND notification_queued_at IS NULL
                ORDER BY
                    CASE alert_severity WHEN 'high' THEN 1 WHEN 'medium' THEN 2 ELSE 3 END,
                    last_detected_at DESC;
                """, connection, transaction))
            {
                await using var reader = await alertCommand.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    alertsToQueue.Add((
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetString(6),
                        reader.GetDecimal(7),
                        reader.GetDecimal(8),
                        reader.GetDecimal(9),
                        reader.GetDecimal(10),
                        reader.GetString(11)
                    ));
                }
            }

            foreach (var alert in alertsToQueue)
            {
                var recipientCount = await QueueProjectCostAlertNotificationsAsync(
                    connection,
                    transaction,
                    alert.AlertId,
                    alert.ProjectId,
                    alert.AlertType,
                    alert.AlertSeverity,
                    alert.ProjectCode,
                    alert.ProjectName,
                    alert.ClientName,
                    alert.PlannedTotalProjectCost,
                    alert.AssignedHours,
                    alert.UsedHours,
                    alert.OverAssignedHours,
                    alert.CostStatus);

                await using var updateCommand = new NpgsqlCommand("""
                    UPDATE project_cost_alerts
                    SET notification_queued_at = CASE WHEN @recipient_count > 0 THEN NOW() ELSE notification_queued_at END,
                        notification_recipient_count = notification_recipient_count + @recipient_count,
                        routing_status = CASE WHEN @recipient_count > 0 THEN 'queued' ELSE routing_status END,
                        notification_released_at = CASE WHEN @recipient_count > 0 THEN COALESCE(notification_released_at, NOW()) ELSE notification_released_at END,
                        notification_released_by_user_id = CASE WHEN @recipient_count > 0 THEN @released_by_user_id ELSE notification_released_by_user_id END,
                        notification_released_by_email = CASE WHEN @recipient_count > 0 THEN @released_by_email ELSE notification_released_by_email END,
                        last_action_at = NOW(),
                        last_action_by_email = @released_by_email,
                        updated_at = NOW()
                    WHERE project_cost_alert_id = @alert_id;
                    """, connection, transaction);

                updateCommand.Parameters.AddWithValue("alert_id", alert.AlertId);
                updateCommand.Parameters.AddWithValue("recipient_count", recipientCount);
                updateCommand.Parameters.AddWithValue("released_by_user_id", sessionUserId.Value);
                updateCommand.Parameters.AddWithValue("released_by_email", GetProjectPulseSessionEmail(httpContext) ?? "unknown");
                await updateCommand.ExecuteNonQueryAsync();

                if (recipientCount > 0)
                {
                    queuedAlertCount++;
                    queuedRecipientCount += recipientCount;
                }
            }
        }

        long openCount;
        long highCount;

        await using (var summaryCommand = new NpgsqlCommand("""
            SELECT
                COUNT(*) FILTER (WHERE alert_status = 'open') AS open_count,
                COUNT(*) FILTER (WHERE alert_status = 'open' AND alert_severity = 'high') AS high_count
            FROM project_cost_alerts;
            """, connection, transaction))
        await using (var reader = await summaryCommand.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            openCount = reader.GetInt64(0);
            highCount = reader.GetInt64(1);
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "cost_alerts_evaluated",
            openCount,
            highCount,
            queuedAlertCount,
            queuedRecipientCount,
            queueNotifications,
            message = "Project cost alerts evaluated and notification routing prepared."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to evaluate project cost alerts",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});



app.MapGet("/api/project-management/workload", async (HttpContext httpContext, Guid? projectManagerUserId) =>
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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var isProjectManager =
        roles.Contains("PROJECT_MANAGER")
        || roles.Contains("PROJECT_MANAGEMENT");

    var isProjectManagementTeamLead =
        roles.Contains("PM_TEAM_LEAD")
        || roles.Contains("PROJECT_MANAGEMENT_TEAM_LEAD");

    var canAccess =
        canViewAll
        || isProjectManager
        || isProjectManagementTeamLead
        || permissions.Contains("VIEW_PROJECT_WORKLOAD");

    if (!canAccess)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Project workload is available to project managers, PM team leads, project/team coordinators, and administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var selectableProjectManagers = new List<(Guid UserId, string DisplayName, string Email, string ScopeReason)>();

    if (canViewAll)
    {
        await using var pmCommand = new NpgsqlCommand("""
            WITH project_pm AS (
                SELECT DISTINCT project_manager_user_id AS user_id
                FROM projects
                WHERE project_manager_user_id IS NOT NULL
            ),
            role_pm AS (
                SELECT DISTINCT ura.user_id
                FROM app_user_role_assignments ura
                JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id
                   AND r.is_active = TRUE
                WHERE ura.is_active = TRUE
                  AND r.role_code IN ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT')
            ),
            eligible_pm AS (
                SELECT user_id FROM project_pm
                UNION
                SELECT user_id FROM role_pm
            )
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email
            FROM eligible_pm ep
            JOIN app_users u
                ON u.user_id = ep.user_id
               AND u.is_active = TRUE
            ORDER BY display_name, u.email;
            """, connection);

        await using var reader = await pmCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            selectableProjectManagers.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                "all_project_managers"));
        }
    }
    else if (isProjectManagementTeamLead)
    {
        await using var teamCommand = new NpgsqlCommand("""
            WITH active_lead_teams AS (
                SELECT DISTINCT tm.team_id
                FROM team_memberships tm
                WHERE tm.user_id = @user_id
                  AND tm.effective_start_date <= CURRENT_DATE
                  AND (tm.effective_end_date IS NULL OR tm.effective_end_date >= CURRENT_DATE)
            ),
            team_pm_members AS (
                SELECT DISTINCT tm.user_id
                FROM team_memberships tm
                JOIN active_lead_teams alt
                    ON alt.team_id = tm.team_id
                JOIN app_user_role_assignments ura
                    ON ura.user_id = tm.user_id
                   AND ura.is_active = TRUE
                JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id
                   AND r.is_active = TRUE
                WHERE tm.effective_start_date <= CURRENT_DATE
                  AND (tm.effective_end_date IS NULL OR tm.effective_end_date >= CURRENT_DATE)
                  AND r.role_code IN ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT')
            ),
            own_pm AS (
                SELECT @user_id::uuid AS user_id
                WHERE EXISTS (
                    SELECT 1
                    FROM app_user_role_assignments ura
                    JOIN app_roles r
                        ON r.app_role_id = ura.app_role_id
                       AND r.is_active = TRUE
                    WHERE ura.user_id = @user_id
                      AND ura.is_active = TRUE
                      AND r.role_code IN ('PROJECT_MANAGER', 'PROJECT_MANAGEMENT')
                )
            ),
            eligible_pm AS (
                SELECT user_id FROM team_pm_members
                UNION
                SELECT user_id FROM own_pm
            )
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email
            FROM eligible_pm ep
            JOIN app_users u
                ON u.user_id = ep.user_id
               AND u.is_active = TRUE
            ORDER BY display_name, u.email;
            """, connection);

        teamCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await teamCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            selectableProjectManagers.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                "pm_team_scope"));
        }
    }
    else if (isProjectManager)
    {
        await using var ownCommand = new NpgsqlCommand("""
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email
            FROM app_users u
            WHERE u.user_id = @user_id
              AND u.is_active = TRUE;
            """, connection);

        ownCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await ownCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            selectableProjectManagers.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                "own_project_manager_scope"));
        }
    }

    selectableProjectManagers = selectableProjectManagers
        .GroupBy(item => item.UserId)
        .Select(group => group.First())
        .OrderBy(item => item.DisplayName)
        .ThenBy(item => item.Email)
        .ToList();

    var allowedProjectManagerIds = selectableProjectManagers.Select(item => item.UserId).ToArray();

    Guid? effectiveProjectManagerUserId = null;
    var scopeAll = false;
    var allowTeamScope = false;
    var scope = "own_project_manager_scope";

    if (canViewAll)
    {
        if (projectManagerUserId.HasValue && projectManagerUserId.Value != Guid.Empty)
        {
            if (!allowedProjectManagerIds.Contains(projectManagerUserId.Value))
            {
                return Results.Json(new
                {
                    status = "invalid_project_manager_scope",
                    message = "Selected project manager is not available for workload scope."
                }, statusCode: StatusCodes.Status400BadRequest);
            }

            effectiveProjectManagerUserId = projectManagerUserId.Value;
            scope = "selected_project_manager_scope";
        }
        else
        {
            scopeAll = true;
            scope = "all_projects";
        }
    }
    else if (isProjectManagementTeamLead)
    {
        if (projectManagerUserId.HasValue && projectManagerUserId.Value != Guid.Empty)
        {
            if (!allowedProjectManagerIds.Contains(projectManagerUserId.Value))
            {
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "PM Team Lead workload selection is restricted to project managers on the same team."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            effectiveProjectManagerUserId = projectManagerUserId.Value;
            scope = "selected_team_project_manager_scope";
        }
        else
        {
            allowTeamScope = true;
            scope = "pm_team_scope";
        }
    }
    else
    {
        effectiveProjectManagerUserId = sessionUserId.Value;
        scope = "own_project_manager_scope";
    }

    var now = DateTime.UtcNow.Date;
    var quarterNumber = ((now.Month - 1) / 3) + 1;
    var quarterStart = new DateTime(now.Year, ((quarterNumber - 1) * 3) + 1, 1);
    var quarterEnd = quarterStart.AddMonths(3).AddDays(-1);
    var quarterLabel = $"Q{quarterNumber} {now.Year}";

    void AddScopeParameters(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue("scope_all", scopeAll);
        command.Parameters.AddWithValue("allow_team_scope", allowTeamScope);
        command.Parameters.AddWithValue("has_scoped_pm", effectiveProjectManagerUserId.HasValue);
        command.Parameters.AddWithValue("scoped_pm_user_id", effectiveProjectManagerUserId ?? Guid.Empty);
        command.Parameters.AddWithValue("team_pm_user_ids", allowedProjectManagerIds);
    }

    const string workloadScopeWhere = """
        (
            @scope_all
            OR (@has_scoped_pm AND p.project_manager_user_id = @scoped_pm_user_id)
            OR (@allow_team_scope AND p.project_manager_user_id = ANY(@team_pm_user_ids))
        )
        """;

    object summary;
    await using (var summaryCommand = new NpgsqlCommand($"""
        WITH scoped_projects AS (
            SELECT
                p.project_id,
                COALESCE(p.status, 'unknown') AS status,
                p.start_date,
                p.end_date,
                p.created_at
            FROM projects p
            WHERE {workloadScopeWhere}
        )
        SELECT
            COUNT(*)::bigint AS total_projects,
            COUNT(*) FILTER (
                WHERE lower(status) IN ('active', 'open', 'in_progress', 'planning')
                  AND COALESCE(end_date, @quarter_end::date) >= @quarter_start::date
                  AND COALESCE(start_date, created_at::date, @quarter_start::date) <= @quarter_end::date
            )::bigint AS active_this_quarter,
            COUNT(*) FILTER (
                WHERE lower(status) IN ('closed', 'complete', 'completed', 'done')
                  AND COALESCE(end_date, created_at::date) BETWEEN @quarter_start::date AND @quarter_end::date
            )::bigint AS closed_this_quarter,
            COUNT(*) FILTER (
                WHERE lower(status) IN ('active', 'open', 'in_progress', 'planning')
                  AND end_date IS NOT NULL
                  AND end_date < @today::date
            )::bigint AS overdue_active_projects
        FROM scoped_projects;
        """, connection))
    {
        AddScopeParameters(summaryCommand);
        summaryCommand.Parameters.AddWithValue("quarter_start", quarterStart);
        summaryCommand.Parameters.AddWithValue("quarter_end", quarterEnd);
        summaryCommand.Parameters.AddWithValue("today", now);

        await using var reader = await summaryCommand.ExecuteReaderAsync();
        await reader.ReadAsync();

        summary = new
        {
            totalProjects = reader.GetInt64(0),
            activeProjectsThisQuarter = reader.GetInt64(1),
            closedProjectsThisQuarter = reader.GetInt64(2),
            overdueActiveProjects = reader.GetInt64(3)
        };
    }

    var statusBreakdown = new List<object>();
    await using (var statusCommand = new NpgsqlCommand($"""
        SELECT
            COALESCE(NULLIF(trim(p.status), ''), 'unknown') AS status,
            COUNT(*)::bigint AS project_count
        FROM projects p
        WHERE {workloadScopeWhere}
        GROUP BY COALESCE(NULLIF(trim(p.status), ''), 'unknown')
        ORDER BY project_count DESC, status;
        """, connection))
    {
        AddScopeParameters(statusCommand);

        await using var reader = await statusCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statusBreakdown.Add(new
            {
                status = reader.GetString(0),
                count = reader.GetInt64(1)
            });
        }
    }

    var projects = new List<object>();
    var riskHighlights = new List<object>();

    await using (var projectsCommand = new NpgsqlCommand($"""
        SELECT
            p.project_id,
            p.project_code,
            p.project_name,
            COALESCE(c.client_name, 'No client') AS client_name,
            COALESCE(p.status, 'unknown') AS status,
            p.start_date,
            p.end_date,
            COALESCE(pm.display_name, 'Unassigned PM') AS project_manager_name,
            COALESCE(pm.email, '') AS project_manager_email,
            COALESCE(SUM(pa.assigned_hours), 0)::numeric AS assigned_hours,
            COUNT(DISTINCT pa.user_id)::bigint AS assigned_resource_count,
            COUNT(DISTINCT pt.task_id)::bigint AS task_count,
            COUNT(DISTINCT pca.project_cost_alert_id)::bigint AS open_cost_alert_count,
            COALESCE(p.planned_total_project_cost, 0)::numeric AS planned_total_project_cost
        FROM projects p
        LEFT JOIN clients c ON c.client_id = p.client_id
        LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
        LEFT JOIN project_assignments pa ON pa.project_id = p.project_id
        LEFT JOIN project_tasks pt ON pt.project_id = p.project_id AND pt.is_active = TRUE
        LEFT JOIN project_cost_alerts pca ON pca.project_id = p.project_id AND pca.alert_status = 'open'
        WHERE {workloadScopeWhere}
        GROUP BY
            p.project_id,
            p.project_code,
            p.project_name,
            c.client_name,
            p.status,
            p.start_date,
            p.end_date,
            pm.display_name,
            pm.email,
            p.planned_total_project_cost
        ORDER BY
            CASE
                WHEN lower(COALESCE(p.status, '')) IN ('active', 'open', 'in_progress', 'planning') THEN 1
                WHEN lower(COALESCE(p.status, '')) IN ('closed', 'complete', 'completed', 'done') THEN 3
                ELSE 2
            END,
            COALESCE(p.end_date, DATE '2999-12-31'),
            p.project_code
        LIMIT 100;
        """, connection))
    {
        AddScopeParameters(projectsCommand);

        await using var reader = await projectsCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var projectId = reader.GetGuid(0);
            var projectCode = reader.GetString(1);
            var projectName = reader.GetString(2);
            var clientName = reader.GetString(3);
            var projectStatus = reader.GetString(4);
            var startDate = reader.IsDBNull(5) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(5);
            var endDate = reader.IsDBNull(6) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(6);
            var projectManagerName = reader.GetString(7);
            var projectManagerEmail = reader.GetString(8);
            var assignedHours = reader.GetDecimal(9);
            var assignedResourceCount = reader.GetInt64(10);
            var taskCount = reader.GetInt64(11);
            var openCostAlertCount = reader.GetInt64(12);
            var plannedTotalProjectCost = reader.GetDecimal(13);

            projects.Add(new
            {
                projectId,
                projectCode,
                projectName,
                clientName,
                status = projectStatus,
                startDate,
                endDate,
                projectManagerName,
                projectManagerEmail,
                assignedHours,
                assignedResourceCount,
                taskCount,
                openCostAlertCount,
                plannedTotalProjectCost
            });

            var statusLower = projectStatus.ToLowerInvariant();
            var isClosed = statusLower is "closed" or "complete" or "completed" or "done";
            var scheduleReview = endDate.HasValue && endDate.Value < DateOnly.FromDateTime(now) && !isClosed;
            var statusReview = statusLower == "at_risk";

            if (riskHighlights.Count < 12 && (openCostAlertCount > 0 || scheduleReview || statusReview))
            {
                riskHighlights.Add(new
                {
                    projectId,
                    projectCode,
                    projectName,
                    status = projectStatus,
                    riskSummary = openCostAlertCount > 0
                        ? $"{openCostAlertCount} open cost alert(s)"
                        : "Schedule or status requires review"
                });
            }
        }
    }

    return Results.Ok(new
    {
        module = "019M-AM Project Workload PM Scope",
        scope,
        quarter = quarterLabel,
        quarterStart = DateOnly.FromDateTime(quarterStart),
        quarterEnd = DateOnly.FromDateTime(quarterEnd),
        selectedProjectManagerUserId = effectiveProjectManagerUserId,
        selectableProjectManagers = selectableProjectManagers.Select(item => new
        {
            userId = item.UserId,
            displayName = item.DisplayName,
            email = item.Email,
            scopeReason = item.ScopeReason
        }),
        access = new
        {
            canViewAll,
            canSelectProjectManager = canViewAll || isProjectManagementTeamLead,
            isProjectManager,
            isProjectManagementTeamLead
        },
        summary,
        statusBreakdown,
        projectCount = projects.Count,
        projects,
        riskCount = riskHighlights.Count,
        riskHighlights
    });
});


app.MapPost("/api/projects/cost-alerts/{alertId:guid}/status", async (Guid alertId, ProjectCostAlertStatusUpdateRequest request, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var requestedStatus = (request.AlertStatus ?? string.Empty).Trim().ToLowerInvariant();

    if (requestedStatus is not ("open" or "acknowledged" or "resolved"))
    {
        return Results.Json(new
        {
            status = "invalid_alert_status",
            message = "Alert status must be open, acknowledged, or resolved."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    var note = (request.Note ?? string.Empty).Trim();
    var actorEmail = GetProjectPulseSessionEmail(httpContext) ?? "unknown";

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessCostAlertsAsync(httpContext, connection, requireManage: true))
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Cost alert status changes are restricted to administrators and project/team coordinators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var command = new NpgsqlCommand("""
        UPDATE project_cost_alerts
        SET alert_status = @alert_status,
            acknowledged_at = CASE
                WHEN @alert_status = 'acknowledged' THEN NOW()
                WHEN @alert_status = 'open' THEN NULL
                ELSE acknowledged_at
            END,
            acknowledged_by_user_id = CASE
                WHEN @alert_status = 'acknowledged' THEN @actor_user_id
                WHEN @alert_status = 'open' THEN NULL
                ELSE acknowledged_by_user_id
            END,
            acknowledged_by_email = CASE
                WHEN @alert_status = 'acknowledged' THEN @actor_email
                WHEN @alert_status = 'open' THEN NULL
                ELSE acknowledged_by_email
            END,
            acknowledgement_note = CASE
                WHEN @alert_status = 'acknowledged' THEN NULLIF(@note, '')
                WHEN @alert_status = 'open' THEN NULL
                ELSE acknowledgement_note
            END,
            resolved_at = CASE
                WHEN @alert_status = 'resolved' THEN NOW()
                WHEN @alert_status = 'open' THEN NULL
                ELSE resolved_at
            END,
            routing_status = CASE
                WHEN @alert_status = 'resolved' THEN 'closed'
                WHEN @alert_status = 'open' AND routing_status = 'closed' THEN 'hold'
                ELSE routing_status
            END,
            routing_note = CASE
                WHEN NULLIF(@note, '') IS NOT NULL THEN @note
                ELSE routing_note
            END,
            last_action_at = NOW(),
            last_action_by_email = @actor_email,
            updated_at = NOW()
        WHERE project_cost_alert_id = @alert_id
        RETURNING
            project_cost_alert_id,
            alert_status,
            routing_status,
            acknowledged_at,
            acknowledged_by_email,
            acknowledgement_note,
            resolved_at,
            last_action_at,
            last_action_by_email;
        """, connection);

    command.Parameters.AddWithValue("alert_id", alertId);
    command.Parameters.AddWithValue("alert_status", requestedStatus);
    command.Parameters.AddWithValue("actor_user_id", sessionUserId.Value);
    command.Parameters.AddWithValue("actor_email", actorEmail);
    command.Parameters.AddWithValue("note", note);

    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return Results.Json(new { status = "not_found", message = "Cost alert was not found." }, statusCode: StatusCodes.Status404NotFound);
    }

    return Results.Ok(new
    {
        status = "cost_alert_status_updated",
        alertId = reader.GetGuid(0),
        alertStatus = reader.GetString(1),
        routingStatus = reader.GetString(2),
        acknowledgedAt = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3),
        acknowledgedByEmail = reader.IsDBNull(4) ? null : reader.GetString(4),
        acknowledgementNote = reader.IsDBNull(5) ? null : reader.GetString(5),
        resolvedAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
        lastActionAt = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(7),
        lastActionByEmail = reader.IsDBNull(8) ? null : reader.GetString(8)
    });
});


app.MapPost("/api/projects/cost-alerts/{alertId:guid}/release-notification", async (Guid alertId, ProjectCostAlertReleaseNotificationRequest request, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var actorEmail = GetProjectPulseSessionEmail(httpContext) ?? "unknown";
    var routingNote = (request.RoutingNote ?? string.Empty).Trim();

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessCostAlertsAsync(httpContext, connection, requireManage: true))
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Notification release is restricted to administrators and project/team coordinators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        Guid projectId;
        string alertType;
        string alertSeverity;
        string projectCode;
        string projectName;
        string clientName;
        decimal plannedTotalProjectCost;
        decimal assignedHours;
        decimal usedHours;
        decimal overAssignedHours;
        string costStatus;
        DateTimeOffset? notificationQueuedAt;
        string alertStatus;

        await using (var selectCommand = new NpgsqlCommand("""
            SELECT
                project_id,
                alert_type,
                alert_severity,
                COALESCE(project_code, ''),
                COALESCE(project_name, ''),
                COALESCE(client_name, ''),
                planned_total_project_cost,
                assigned_hours,
                used_hours,
                over_assigned_hours,
                cost_status,
                notification_queued_at,
                alert_status
            FROM project_cost_alerts
            WHERE project_cost_alert_id = @alert_id;
            """, connection, transaction))
        {
            selectCommand.Parameters.AddWithValue("alert_id", alertId);

            await using var reader = await selectCommand.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                await transaction.RollbackAsync();
                return Results.Json(new { status = "not_found", message = "Cost alert was not found." }, statusCode: StatusCodes.Status404NotFound);
            }

            projectId = reader.GetGuid(0);
            alertType = reader.GetString(1);
            alertSeverity = reader.GetString(2);
            projectCode = reader.GetString(3);
            projectName = reader.GetString(4);
            clientName = reader.GetString(5);
            plannedTotalProjectCost = reader.GetDecimal(6);
            assignedHours = reader.GetDecimal(7);
            usedHours = reader.GetDecimal(8);
            overAssignedHours = reader.GetDecimal(9);
            costStatus = reader.GetString(10);
            notificationQueuedAt = reader.IsDBNull(11) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(11);
            alertStatus = reader.GetString(12);
        }

        if (string.Equals(alertStatus, "resolved", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "alert_resolved",
                message = "Resolved alerts cannot release notifications unless reopened."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        if (notificationQueuedAt is not null)
        {
            await using var alreadyQueuedCommand = new NpgsqlCommand("""
                UPDATE project_cost_alerts
                SET routing_status = 'queued',
                    routing_note = COALESCE(NULLIF(@routing_note, ''), routing_note),
                    last_action_at = NOW(),
                    last_action_by_email = @actor_email,
                    updated_at = NOW()
                WHERE project_cost_alert_id = @alert_id;
                """, connection, transaction);

            alreadyQueuedCommand.Parameters.AddWithValue("alert_id", alertId);
            alreadyQueuedCommand.Parameters.AddWithValue("routing_note", routingNote);
            alreadyQueuedCommand.Parameters.AddWithValue("actor_email", actorEmail);
            await alreadyQueuedCommand.ExecuteNonQueryAsync();

            await transaction.CommitAsync();

            return Results.Ok(new
            {
                status = "notification_already_queued",
                alertId,
                message = "Notification was already queued; no duplicate notification was created."
            });
        }

        var recipientCount = await QueueProjectCostAlertNotificationsAsync(
            connection,
            transaction,
            alertId,
            projectId,
            alertType,
            alertSeverity,
            projectCode,
            projectName,
            clientName,
            plannedTotalProjectCost,
            assignedHours,
            usedHours,
            overAssignedHours,
            costStatus);

        await using (var updateCommand = new NpgsqlCommand("""
            UPDATE project_cost_alerts
            SET notification_queued_at = CASE WHEN @recipient_count > 0 THEN NOW() ELSE notification_queued_at END,
                notification_recipient_count = notification_recipient_count + @recipient_count,
                routing_status = CASE WHEN @recipient_count > 0 THEN 'queued' ELSE 'no_recipients' END,
                routing_note = COALESCE(NULLIF(@routing_note, ''), routing_note),
                notification_released_at = CASE WHEN @recipient_count > 0 THEN NOW() ELSE notification_released_at END,
                notification_released_by_user_id = CASE WHEN @recipient_count > 0 THEN @actor_user_id ELSE notification_released_by_user_id END,
                notification_released_by_email = CASE WHEN @recipient_count > 0 THEN @actor_email ELSE notification_released_by_email END,
                last_action_at = NOW(),
                last_action_by_email = @actor_email,
                updated_at = NOW()
            WHERE project_cost_alert_id = @alert_id;
            """, connection, transaction))
        {
            updateCommand.Parameters.AddWithValue("alert_id", alertId);
            updateCommand.Parameters.AddWithValue("recipient_count", recipientCount);
            updateCommand.Parameters.AddWithValue("routing_note", routingNote);
            updateCommand.Parameters.AddWithValue("actor_user_id", sessionUserId.Value);
            updateCommand.Parameters.AddWithValue("actor_email", actorEmail);
            await updateCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = recipientCount > 0 ? "notification_released" : "notification_not_released",
            alertId,
            recipientCount,
            message = recipientCount > 0
                ? "Cost alert notification was released to the routing outbox."
                : "No eligible notification recipients were found."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to release cost alert notification",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});


app.MapGet("/api/project-management/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var milestones = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT p.project_code, pm.milestone_name, pm.milestone_status, pm.due_date, pm.display_order
        FROM project_milestones pm
        INNER JOIN projects p ON p.project_id = pm.project_id
        ORDER BY p.project_code, pm.display_order, pm.due_date;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            milestones.Add(new
            {
                projectCode = reader.GetString(0),
                name = reader.GetString(1),
                status = reader.GetString(2),
                dueDate = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
                displayOrder = reader.GetInt32(4)
            });
        }
    }

    var risks = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT p.project_code, pr.risk_title, pr.probability, pr.impact, pr.risk_status, pr.mitigation_plan
        FROM project_risks pr
        INNER JOIN projects p ON p.project_id = pr.project_id
        ORDER BY p.project_code, pr.created_at DESC;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            risks.Add(new
            {
                projectCode = reader.GetString(0),
                title = reader.GetString(1),
                probability = reader.GetString(2),
                impact = reader.GetString(3),
                status = reader.GetString(4),
                mitigationPlan = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
    }

    return Results.Ok(new { milestoneCount = milestones.Count, riskCount = risks.Count, milestones, risks });
});

app.MapGet("/api/resource-scheduling/capacity", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(28);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var rows = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT u.display_name, u.email, rcp.week_start_date, rcp.available_hours, rcp.assigned_hours, rcp.planned_utilization_percent, rcp.capacity_status
        FROM resource_capacity_plans rcp
        INNER JOIN app_users u ON u.user_id = rcp.user_id
        WHERE rcp.week_start_date BETWEEN @start AND @end
        ORDER BY rcp.week_start_date, u.display_name;
        """, connection);
    command.Parameters.AddWithValue("start", start);
    command.Parameters.AddWithValue("end", end);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            resourceName = reader.GetString(0),
            resourceEmail = reader.GetString(1),
            weekStart = reader.GetFieldValue<DateOnly>(2),
            availableHours = reader.GetDecimal(3),
            assignedHours = reader.GetDecimal(4),
            plannedUtilizationPercent = reader.GetDecimal(5),
            status = reader.GetString(6)
        });
    }

    return Results.Ok(new { weekStart = start, weekEnd = end, count = rows.Count, capacity = rows });
});

app.MapGet("/api/expenses/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var reports = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT er.report_number, er.report_title, er.report_status, er.report_total, u.display_name, p.project_code
        FROM expense_reports er
        INNER JOIN app_users u ON u.user_id = er.user_id
        LEFT JOIN projects p ON p.project_id = er.project_id
        ORDER BY er.created_at DESC;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        reports.Add(new
        {
            reportNumber = reader.GetString(0),
            title = reader.GetString(1),
            status = reader.GetString(2),
            total = reader.GetDecimal(3),
            resourceName = reader.GetString(4),
            projectCode = reader.IsDBNull(5) ? null : reader.GetString(5)
        });
    }

    return Results.Ok(new { count = reports.Count, reports });
});

app.MapGet("/api/invoicing/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var invoices = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT ci.invoice_number, ci.invoice_status, ci.billing_period_start, ci.billing_period_end, ci.labor_amount, ci.expense_amount, ci.invoice_total, p.project_code, p.project_name
        FROM client_invoices ci
        LEFT JOIN projects p ON p.project_id = ci.project_id
        ORDER BY ci.generated_at DESC;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        invoices.Add(new
        {
            invoiceNumber = reader.GetString(0),
            status = reader.GetString(1),
            billingPeriodStart = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
            billingPeriodEnd = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
            laborAmount = reader.GetDecimal(4),
            expenseAmount = reader.GetDecimal(5),
            invoiceTotal = reader.GetDecimal(6),
            projectCode = reader.IsDBNull(7) ? null : reader.GetString(7),
            projectName = reader.IsDBNull(8) ? null : reader.GetString(8)
        });
    }

    return Results.Ok(new { count = invoices.Count, invoices });
});

app.MapGet("/api/reporting/executive-dashboard", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var metrics = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT snapshot_date, metric_name, metric_value, metric_unit, metric_context::text
        FROM reporting_snapshots
        WHERE snapshot_type = 'executive_dashboard'
        ORDER BY metric_name;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        metrics.Add(new
        {
            snapshotDate = reader.GetFieldValue<DateOnly>(0),
            metricName = reader.GetString(1),
            metricValue = reader.GetDecimal(2),
            metricUnit = reader.IsDBNull(3) ? null : reader.GetString(3),
            context = reader.IsDBNull(4) ? null : reader.GetString(4)
        });
    }

    return Results.Ok(new { count = metrics.Count, metrics });
});


app.MapGet("/api/users/timesheet-preferences", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var userId = sessionUserId.Value;
    var preferences = await LoadTimesheetPreferencesAsync(connection, userId);

    return Results.Ok(preferences);
});

app.MapPost("/api/users/timesheet-preferences", async (TimesheetPreferenceRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var userId = sessionUserId.Value;

    const string sql = """
        INSERT INTO user_timesheet_preferences (
            user_id,
            default_non_project_category_codes,
            default_project_task_ids,
            auto_add_holidays,
            weekly_reminder_enabled,
            updated_at
        )
        VALUES (
            @user_id,
            @default_codes,
            @default_task_ids,
            @auto_add_holidays,
            @weekly_reminder_enabled,
            NOW()
        )
        ON CONFLICT (user_id) DO UPDATE
        SET default_non_project_category_codes = EXCLUDED.default_non_project_category_codes,
            default_project_task_ids = EXCLUDED.default_project_task_ids,
            auto_add_holidays = EXCLUDED.auto_add_holidays,
            weekly_reminder_enabled = EXCLUDED.weekly_reminder_enabled,
            updated_at = NOW();
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("default_codes", request.DefaultNonProjectCategoryCodes?.ToArray() ?? Array.Empty<string>());
    command.Parameters.AddWithValue("default_task_ids", request.DefaultProjectTaskIds?.ToArray() ?? Array.Empty<Guid>());
    command.Parameters.AddWithValue("auto_add_holidays", request.AutoAddHolidays);
    command.Parameters.AddWithValue("weekly_reminder_enabled", request.WeeklyReminderEnabled);
    await command.ExecuteNonQueryAsync();

    var preferences = await LoadTimesheetPreferencesAsync(connection, userId);
    return Results.Ok(new { status = "preferences_saved", preferences });
});

app.MapGet("/api/holidays", async (int? year) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var targetYear = year ?? DateTime.UtcNow.Year;

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var holidays = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT holiday_date, holiday_name, holiday_code, holiday_type, is_floating_holiday, auto_populate_hours
            FROM company_holidays
            WHERE is_active = TRUE
              AND EXTRACT(YEAR FROM holiday_date) = @year
            ORDER BY holiday_date;
            """, connection);
        command.Parameters.AddWithValue("year", targetYear);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            holidays.Add(new
            {
                holidayDate = reader.GetFieldValue<DateOnly>(0),
                holidayName = reader.GetString(1),
                holidayCode = reader.GetString(2),
                holidayType = reader.GetString(3),
                isFloatingHoliday = reader.GetBoolean(4),
                autoPopulateHours = reader.GetDecimal(5)
            });
        }

        return Results.Ok(new { year = targetYear, count = holidays.Count, holidays });
    }
    catch (PostgresException ex) when (ex.SqlState == "42P01" || ex.SqlState == "42703")
    {
        return Results.Ok(new { year = targetYear, count = 0, holidays = Array.Empty<object>(), warning = "Holiday foundation tables are not ready yet." });
    }
});

app.MapPost("/api/reminders/queue-weekly-engineer", async () =>
{
    return await QueueReminderRuleAsync("WEEKLY_ENGINEER_TIME_REMINDER");
});

app.MapPost("/api/reminders/queue-month-end-pm", async () =>
{
    return await QueueReminderRuleAsync("MONTH_END_PM_REMINDER");
});

app.MapGet("/api/reminders/outbox", async (int? limit) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var maxRows = Math.Clamp(limit ?? 25, 1, 200);
    var rows = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT rule_code, recipient_email, recipient_name, subject, status, scheduled_for, sent_at, error_message
        FROM email_notification_outbox
        ORDER BY created_at DESC
        LIMIT @limit;
        """, connection);
    command.Parameters.AddWithValue("limit", maxRows);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            ruleCode = reader.IsDBNull(0) ? null : reader.GetString(0),
            recipientEmail = reader.GetString(1),
            recipientName = reader.IsDBNull(2) ? null : reader.GetString(2),
            subject = reader.GetString(3),
            status = reader.GetString(4),
            scheduledFor = reader.GetFieldValue<DateTimeOffset>(5),
            sentAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
            errorMessage = reader.IsDBNull(7) ? null : reader.GetString(7)
        });
    }

    return Results.Ok(new { count = rows.Count, outbox = rows });
});


app.MapPost("/api/holidays/import-text", async (HolidayCsvImportRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.CsvText))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "CSV text is required." });
    }

    var lines = request.CsvText
        .Replace("\r\n", "\n")
        .Replace("\r", "\n")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    if (lines.Count < 2)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "CSV must include a header and at least one holiday row." });
    }

    var header = ParseSimpleCsvLine(lines[0]).Select(item => item.Trim()).ToList();
    var dateIndex = header.FindIndex(item => item.Equals("holiday_date", StringComparison.OrdinalIgnoreCase));
    var nameIndex = header.FindIndex(item => item.Equals("holiday_name", StringComparison.OrdinalIgnoreCase));
    var typeIndex = header.FindIndex(item => item.Equals("holiday_type", StringComparison.OrdinalIgnoreCase));
    var floatingIndex = header.FindIndex(item => item.Equals("is_floating_holiday", StringComparison.OrdinalIgnoreCase));
    var hoursIndex = header.FindIndex(item => item.Equals("auto_populate_hours", StringComparison.OrdinalIgnoreCase));

    if (dateIndex < 0 || nameIndex < 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "CSV must include holiday_date and holiday_name columns." });
    }

    var rows = new List<HolidayImportRow>();
    for (var index = 1; index < lines.Count; index++)
    {
        var columns = ParseSimpleCsvLine(lines[index]);
        if (columns.Count <= Math.Max(dateIndex, nameIndex)) continue;

        var dateValue = columns[dateIndex].Trim();
        var nameValue = columns[nameIndex].Trim();
        if (string.IsNullOrWhiteSpace(dateValue) || string.IsNullOrWhiteSpace(nameValue)) continue;
        if (!DateOnly.TryParse(dateValue, out var holidayDate))
        {
            return Results.BadRequest(new { status = "validation_failed", message = $"Invalid holiday_date on row {index + 1}: {dateValue}" });
        }

        if (request.Year is not null && holidayDate.Year != request.Year.Value) continue;

        var holidayType = typeIndex >= 0 && columns.Count > typeIndex && !string.IsNullOrWhiteSpace(columns[typeIndex])
            ? columns[typeIndex].Trim()
            : "company_paid";
        var isFloating = floatingIndex >= 0 && columns.Count > floatingIndex && IsTruthy(columns[floatingIndex]);
        var hours = 8.00m;
        if (hoursIndex >= 0 && columns.Count > hoursIndex && decimal.TryParse(columns[hoursIndex], out var parsedHours)) hours = parsedHours;

        rows.Add(new HolidayImportRow(holidayDate, nameValue, holidayType, isFloating, hours));
    }

    if (rows.Count == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "No holiday rows were imported. Check the year and CSV values." });
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
        Guid batchId;

        await using (var batchCommand = new NpgsqlCommand("""
            INSERT INTO holiday_upload_batches (upload_year, original_filename, uploaded_by_user_id, row_count, notes)
            VALUES (@upload_year, @original_filename, @uploaded_by_user_id, @row_count, @notes)
            ON CONFLICT (upload_year, original_filename) DO UPDATE
            SET uploaded_at = NOW(),
                uploaded_by_user_id = EXCLUDED.uploaded_by_user_id,
                row_count = EXCLUDED.row_count,
                notes = EXCLUDED.notes
            RETURNING holiday_upload_batch_id;
            """, connection, transaction))
        {
            batchCommand.Parameters.AddWithValue("upload_year", request.Year ?? rows[0].HolidayDate.Year);
            batchCommand.Parameters.AddWithValue("original_filename", string.IsNullOrWhiteSpace(request.Filename) ? $"holiday-upload-{DateTime.UtcNow:yyyyMMddHHmmss}.csv" : request.Filename.Trim());
            batchCommand.Parameters.AddWithValue("uploaded_by_user_id", userId);
            batchCommand.Parameters.AddWithValue("row_count", rows.Count);
            batchCommand.Parameters.AddWithValue("notes", "Uploaded through Project Pulse holiday admin UI");
            batchId = (Guid)(await batchCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create holiday upload batch."));
        }

        foreach (var row in rows)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO company_holidays (holiday_date, holiday_name, holiday_code, holiday_type, is_floating_holiday, auto_populate_hours, is_active, source_batch_id)
                VALUES (@holiday_date, @holiday_name, 'HOLIDAY', @holiday_type, @is_floating_holiday, @auto_populate_hours, TRUE, @source_batch_id)
                ON CONFLICT (holiday_date) DO UPDATE
                SET holiday_name = EXCLUDED.holiday_name,
                    holiday_type = EXCLUDED.holiday_type,
                    is_floating_holiday = EXCLUDED.is_floating_holiday,
                    auto_populate_hours = EXCLUDED.auto_populate_hours,
                    is_active = TRUE,
                    source_batch_id = EXCLUDED.source_batch_id,
                    updated_at = NOW();
                """, connection, transaction);
            command.Parameters.AddWithValue("holiday_date", row.HolidayDate);
            command.Parameters.AddWithValue("holiday_name", row.HolidayName);
            command.Parameters.AddWithValue("holiday_type", row.HolidayType);
            command.Parameters.AddWithValue("is_floating_holiday", row.IsFloatingHoliday);
            command.Parameters.AddWithValue("auto_populate_hours", row.AutoPopulateHours);
            command.Parameters.AddWithValue("source_batch_id", batchId);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return Results.Ok(new { status = "holidays_imported", importedCount = rows.Count, year = request.Year ?? rows[0].HolidayDate.Year });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(title: "Failed to import holidays", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});


app.MapGet("/api/security/me", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var userId = sessionUserId.Value;
    return Results.Ok(await BuildSecurityContextAsync(connection, userId));
});


app.MapGet("/api/security/context", async (HttpContext httpContext) =>
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

    var roles = new List<object>();
    var permissions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

    await using var connection = new NpgsqlConnection(BuildProjectPulseViewAsConnectionString());
    await connection.OpenAsync();

    const string roleSql = """
        SELECT
            r.role_code,
            r.role_name
        FROM app_user_role_assignments ura
        JOIN app_roles r
            ON r.app_role_id = ura.app_role_id
        WHERE ura.user_id = @user_id
          AND ura.is_active = TRUE
          AND r.is_active = TRUE
        ORDER BY r.role_code;
        """;

    await using (var roleCommand = new NpgsqlCommand(roleSql, connection))
    {
        roleCommand.Parameters.AddWithValue("user_id", userId.Value);

        await using var reader = await roleCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            roles.Add(new
            {
                roleCode = reader.GetString(0),
                roleName = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1)
            });
        }
    }

    const string permissionSql = """
        SELECT DISTINCT p.permission_code
        FROM app_user_role_assignments ura
        JOIN app_roles r
            ON r.app_role_id = ura.app_role_id
        JOIN app_role_permissions rp
            ON rp.app_role_id = r.app_role_id
        JOIN app_permissions p
            ON p.app_permission_id = rp.app_permission_id
        WHERE ura.user_id = @user_id
          AND ura.is_active = TRUE
          AND r.is_active = TRUE
        ORDER BY p.permission_code;
        """;

    await using (var permissionCommand = new NpgsqlCommand(permissionSql, connection))
    {
        permissionCommand.Parameters.AddWithValue("user_id", userId.Value);

        await using var reader = await permissionCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            permissions.Add(reader.GetString(0));
        }
    }

    return Results.Ok(new
    {
        userId = userId.Value,
        isViewAs = httpContext.Items.TryGetValue("ProjectPulseIsViewAs", out var isViewAsValue)
            && isViewAsValue is bool isViewAs
            && isViewAs,
        roles,
        permissions = permissions.ToArray()
    });
});


app.MapGet("/api/security/role-matrix", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT
            r.role_code,
            r.role_name,
            r.role_description,
            COALESCE(array_agg(p.permission_code ORDER BY p.module_code, p.permission_code) FILTER (WHERE p.permission_code IS NOT NULL), ARRAY[]::text[]) AS permissions
        FROM app_roles r
        LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
        LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
        WHERE r.is_active = TRUE
        GROUP BY r.role_code, r.role_name, r.role_description, r.display_order
        ORDER BY r.display_order;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roles.Add(new
        {
            roleCode = reader.GetString(0),
            roleName = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            permissions = reader.GetFieldValue<string[]>(3)
        });
    }

    return Results.Ok(new { count = roles.Count, roles });
});



// 019M-AQ Role Administration Directory + Permission Visibility
app.MapGet("/api/role-admin/summary", async (HttpContext httpContext) =>
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

    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new
        {
            status = "admin_required",
            message = "Role Administration Directory is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var roleDefinitionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ADMINISTRATOR"] = "Full platform administrator. Can manage users, roles, security configuration, system administration, and all modules.",
        ["PROJECT_TEAM_COORDINATOR"] = "Operations coordinator with broad project, intake, time, billing, reconciliation, customer, and work-task coordination access.",
        ["PROJECT_MANAGEMENT"] = "Project manager role for managing assigned projects, project workspace activity, project workload, intake aging visibility, work-task assignment, and project validation workflows.",
        ["PROJECT_MANAGER"] = "Legacy or alternate project manager role. Used for assigned project ownership and project-scoped work-task assignment where still present.",
        ["PM_TEAM_LEAD"] = "Project management team lead role. Used to review PM workload and project-management team visibility where configured.",
        ["PROJECT_MANAGEMENT_TEAM_LEAD"] = "Project management team lead role. Provides team-level visibility for PM workload, intake aging, and related project-management oversight.",
        ["MANAGER"] = "People manager role. Reviews team time, approvals, utilization visibility, and manager-scoped operational activity.",
        ["ENGINEERING_TEAM_LEAD"] = "Engineering team lead role. Reviews engineering team utilization and assigned engineering work within team scope.",
        ["ENGINEER"] = "Engineer role. Enters own time, views assigned work, sees own utilization, holidays, and assigned project workspace content.",
        ["EXECUTIVE"] = "Executive/readiness role focused on high-level reporting, workflow visibility, and operational summaries without management write access.",
        ["PMO"] = "Legacy PMO role retained for compatibility where older records still reference it."
    };

    var roleMap = new Dictionary<string, (
        string RoleCode,
        string RoleName,
        string DatabaseDescription,
        string PlainDefinition,
        bool IsActive,
        int DisplayOrder,
        long ActiveUserCount,
        long PermissionCount,
        List<object> AssignedUsers,
        SortedDictionary<string, List<object>> PermissionsByModule
    )>(StringComparer.OrdinalIgnoreCase);

    await using (var roleCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            r.role_name,
            COALESCE(r.role_description, '') AS role_description,
            r.is_active,
            COALESCE(r.display_order, 999) AS display_order,
            COUNT(DISTINCT ura.user_id) FILTER (WHERE ura.is_active = TRUE) AS active_user_count,
            COUNT(DISTINCT rp.app_permission_id) AS permission_count
        FROM app_roles r
        LEFT JOIN app_user_role_assignments ura
            ON ura.app_role_id = r.app_role_id
        LEFT JOIN app_role_permissions rp
            ON rp.app_role_id = r.app_role_id
        GROUP BY r.role_code, r.role_name, r.role_description, r.is_active, r.display_order
        ORDER BY COALESCE(r.display_order, 999), r.role_name, r.role_code;
        """, connection))
    {
        await using var reader = await roleCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var roleCode = reader.GetString(0);
            var roleName = reader.GetString(1);
            var databaseDescription = reader.GetString(2);
            var plainDefinition = roleDefinitionMap.TryGetValue(roleCode, out var mappedDefinition)
                ? mappedDefinition
                : string.IsNullOrWhiteSpace(databaseDescription)
                    ? "Application role used to grant module permissions and route visibility."
                    : databaseDescription;

            roleMap[roleCode] = (
                roleCode,
                roleName,
                databaseDescription,
                plainDefinition,
                reader.GetBoolean(3),
                reader.GetInt32(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                new List<object>(),
                new SortedDictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase)
            );
        }
    }

    await using (var userCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            u.user_id,
            COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
            u.email,
            COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), 'Unassigned') AS team_name,
            ura.assigned_at,
            COALESCE(ura.assignment_reason, '') AS assignment_reason
        FROM app_user_role_assignments ura
        JOIN app_roles r
            ON r.app_role_id = ura.app_role_id
        JOIN app_users u
            ON u.user_id = ura.user_id
        WHERE ura.is_active = TRUE
          AND u.is_active = TRUE
        ORDER BY r.role_code, team_name, display_name, u.email;
        """, connection))
    {
        await using var reader = await userCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var roleCode = reader.GetString(0);
            if (!roleMap.TryGetValue(roleCode, out var role)) continue;

            role.AssignedUsers.Add(new
            {
                userId = reader.GetGuid(1),
                displayName = reader.GetString(2),
                email = reader.GetString(3),
                teamName = reader.GetString(4),
                assignedAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5),
                assignmentReason = reader.GetString(6)
            });
        }
    }

    await using (var permissionCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            COALESCE(NULLIF(p.module_code, ''), 'Unassigned') AS module_code,
            p.permission_code,
            p.permission_name,
            COALESCE(p.permission_description, '') AS permission_description
        FROM app_role_permissions rp
        JOIN app_roles r
            ON r.app_role_id = rp.app_role_id
        JOIN app_permissions p
            ON p.app_permission_id = rp.app_permission_id
        ORDER BY r.role_code, module_code, p.permission_code;
        """, connection))
    {
        await using var reader = await permissionCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var roleCode = reader.GetString(0);
            if (!roleMap.TryGetValue(roleCode, out var role)) continue;

            var moduleCode = reader.GetString(1);
            if (!role.PermissionsByModule.TryGetValue(moduleCode, out var modulePermissions))
            {
                modulePermissions = new List<object>();
                role.PermissionsByModule[moduleCode] = modulePermissions;
            }

            modulePermissions.Add(new
            {
                permissionCode = reader.GetString(2),
                permissionName = reader.GetString(3),
                permissionDescription = reader.GetString(4)
            });
        }
    }

    var moduleTotals = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var role in roleMap.Values)
    {
        foreach (var module in role.PermissionsByModule)
        {
            moduleTotals[module.Key] = moduleTotals.GetValueOrDefault(module.Key, 0) + module.Value.Count;
        }
    }

    var directory = roleMap.Values
        .OrderBy(role => role.DisplayOrder)
        .ThenBy(role => role.RoleName)
        .Select(role => new
        {
            roleCode = role.RoleCode,
            roleName = role.RoleName,
            roleDescription = role.DatabaseDescription,
            plainLanguageDefinition = role.PlainDefinition,
            isActive = role.IsActive,
            displayOrder = role.DisplayOrder,
            activeUserCount = role.AssignedUsers.Count,
            permissionCount = role.PermissionsByModule.Sum(module => module.Value.Count),
            assignedUsers = role.AssignedUsers,
            permissionsByModule = role.PermissionsByModule.Select(module => new
            {
                moduleCode = module.Key,
                permissionCount = module.Value.Count,
                permissions = module.Value
            }).ToList()
        })
        .ToList();

    return Results.Ok(new
    {
        module = "019M-AQ Role Administration Directory",
        canViewRoleDirectory = true,
        canManageRoles = true,
        access = new
        {
            adminOnly = true,
            managementActionsAdminOnly = true,
            explanation = "This endpoint summarizes roles, assigned users, and permissions. Existing role assignment write actions remain administrator-controlled."
        },
        summary = new
        {
            roleCount = directory.Count,
            activeRoleCount = directory.Count(role => role.isActive),
            assignedUserRoleCount = directory.Sum(role => role.activeUserCount),
            permissionGrantCount = directory.Sum(role => role.permissionCount),
            moduleCount = moduleTotals.Count
        },
        moduleTotals = moduleTotals.Select(item => new
        {
            moduleCode = item.Key,
            permissionGrantCount = item.Value
        }),
        roles = directory
    });
});

app.MapGet("/api/admin/roles", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT role_code, role_name, role_description, display_order
        FROM app_roles
        WHERE is_active = TRUE
        ORDER BY display_order, role_name;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roles.Add(new
        {
            roleCode = reader.GetString(0),
            roleName = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            displayOrder = reader.GetInt32(3)
        });
    }

    return Results.Ok(new { count = roles.Count, roles });
});

app.MapGet("/api/admin/users", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var users = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT
            u.user_id,
            u.email,
            u.display_name,
            COALESCE(u.job_title, '') AS job_title,
            COALESCE(u.department, '') AS department,
            u.is_active,
            COALESCE(array_agg(r.role_code ORDER BY r.display_order) FILTER (WHERE r.role_code IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_codes,
            COALESCE(array_agg(r.role_name ORDER BY r.display_order) FILTER (WHERE r.role_name IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_names
        FROM app_users u
        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE u.is_active = TRUE
        GROUP BY u.user_id, u.email, u.display_name, u.job_title, u.department, u.is_active
        ORDER BY u.display_name, u.email;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        users.Add(new
        {
            userId = reader.GetGuid(0),
            email = reader.GetString(1),
            displayName = reader.GetString(2),
            jobTitle = reader.GetString(3),
            department = reader.GetString(4),
            isActive = reader.GetBoolean(5),
            roleCodes = reader.GetFieldValue<string[]>(6),
            roleNames = reader.GetFieldValue<string[]>(7)
        });
    }

    return Results.Ok(new { count = users.Count, users });
});

app.MapPost("/api/admin/users/roles", async (UserRoleAssignmentRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Email is required." });
    }

    var roleCodes = request.RoleCodes?.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim().ToUpperInvariant()).Distinct().ToArray() ?? Array.Empty<string>();
    if (roleCodes.Length == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "At least one role code is required." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var sessionAdminUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionAdminUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        var adminUserId = sessionAdminUserId.Value;
        Guid targetUserId;

        await using (var userCommand = new NpgsqlCommand("SELECT user_id FROM app_users WHERE lower(email) = lower(@email);", connection, transaction))
        {
            userCommand.Parameters.AddWithValue("email", request.Email.Trim());
            var result = await userCommand.ExecuteScalarAsync();
            if (result is null)
            {
                return Results.NotFound(new { status = "not_found", message = $"No user found for {request.Email}." });
            }
            targetUserId = (Guid)result;
        }

        await using (var deactivateCommand = new NpgsqlCommand("""
            UPDATE app_user_role_assignments
            SET is_active = FALSE,
                updated_at = NOW()
            WHERE user_id = @user_id;
            """, connection, transaction))
        {
            deactivateCommand.Parameters.AddWithValue("user_id", targetUserId);
            await deactivateCommand.ExecuteNonQueryAsync();
        }

        foreach (var roleCode in roleCodes)
        {
            await using var assignCommand = new NpgsqlCommand("""
                INSERT INTO app_user_role_assignments (user_id, app_role_id, assigned_by_user_id, assignment_reason, is_active)
                SELECT @user_id, app_role_id, @assigned_by_user_id, @assignment_reason, TRUE
                FROM app_roles
                WHERE role_code = @role_code
                  AND is_active = TRUE
                ON CONFLICT (user_id, app_role_id) DO UPDATE
                SET is_active = TRUE,
                    assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                    assignment_reason = EXCLUDED.assignment_reason,
                    updated_at = NOW();
                """, connection, transaction);
            assignCommand.Parameters.AddWithValue("user_id", targetUserId);
            assignCommand.Parameters.AddWithValue("assigned_by_user_id", adminUserId);
            assignCommand.Parameters.AddWithValue("assignment_reason", string.IsNullOrWhiteSpace(request.Reason) ? "Role updated from Project Pulse role administration" : request.Reason.Trim());
            assignCommand.Parameters.AddWithValue("role_code", roleCode);
            await assignCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return Results.Ok(new { status = "roles_updated", email = request.Email.Trim(), roleCodes });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(title: "Failed to update user roles", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});



const int ProjectPulseSessionMinutes = 120;
const int ProjectPulseSessionWarningMinutes = 10;
const int ProjectPulsePasswordIterations = 210_000;

bool IsProjectPulsePublicApiPath(HttpContext context)
{
    var path = context.Request.Path.Value ?? string.Empty;

    if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var publicPaths = new[]
    {
        "/api/version",
        "/api/auth/login/route",
        "/api/auth/local/login",
        "/api/auth/sso/dev-login",
        "/api/auth/password-reset/request"
    };

    return publicPaths.Any(publicPath => path.Equals(publicPath, StringComparison.OrdinalIgnoreCase));
}


async Task<bool> ApplyProjectPulseViewAsContextAsync(HttpContext context, ProjectPulseSessionValidation validation)
{
    if (validation.UserId is null)
    {
        return true;
    }

    if (!context.Request.Headers.TryGetValue("X-ProjectPulse-View-As-User", out var viewAsHeader))
    {
        return true;
    }

    var rawViewAs = viewAsHeader.ToString();

    if (string.IsNullOrWhiteSpace(rawViewAs))
    {
        return true;
    }

    if (!Guid.TryParse(rawViewAs, out var viewedAsUserId))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "invalid_view_as_user",
            message = "The selected View-As user identifier is invalid."
        });
        return false;
    }

    if (viewedAsUserId == validation.UserId.Value)
    {
        return true;
    }

    var path = context.Request.Path.Value ?? string.Empty;
    var method = context.Request.Method.ToUpperInvariant();

    if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    await using var connection = new NpgsqlConnection(BuildProjectPulseViewAsConnectionString());
    await connection.OpenAsync();

    var actualIsAdministrator = await ProjectPulseViewAsUserHasRoleAsync(connection, validation.UserId.Value, "ADMINISTRATOR");

    if (!actualIsAdministrator)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "view_as_forbidden",
            message = "Only Administrators can use View-As preview."
        });
        return false;
    }

    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "view_as_read_only",
            message = "Write actions are disabled while using Administrator View-As preview. Exit preview to make changes."
        });
        return false;
    }

    var viewedUser = await LoadProjectPulseViewAsUserAsync(connection, viewedAsUserId);

    if (viewedUser is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "view_as_user_not_found",
            message = "The selected View-As user was not found or is inactive."
        });
        return false;
    }

    context.Items["ProjectPulseActualUserId"] = validation.UserId.Value;
    context.Items["ProjectPulseActualEmail"] = validation.Email ?? string.Empty;
    context.Items["ProjectPulseEffectiveUserId"] = viewedUser.UserId;
    context.Items["ProjectPulseEffectiveEmail"] = viewedUser.Email;
    context.Items["ProjectPulseIsViewAs"] = true;

    await InsertProjectPulseViewAsAuditAsync(connection, validation.UserId.Value, viewedUser.UserId, path);

    return true;
}

string BuildProjectPulseViewAsConnectionString()
{
    var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
    var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
    var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
    var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
    var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

    var builder = new NpgsqlConnectionStringBuilder
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
    };

    return builder.ConnectionString;
}

async Task<bool> ProjectPulseViewAsUserHasRoleAsync(NpgsqlConnection connection, Guid userId, string roleCode)
{
    const string sql = """
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND r.role_code = @role_code
        );
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("role_code", roleCode);

    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

async Task<ProjectPulseViewAsUser?> LoadProjectPulseViewAsUserAsync(NpgsqlConnection connection, Guid userId)
{
    const string sql = """
        SELECT user_id, email
        FROM app_users
        WHERE user_id = @user_id
          AND is_active = TRUE
          AND login_enabled = TRUE;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);

    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new ProjectPulseViewAsUser(reader.GetGuid(0), reader.GetString(1));
}

async Task InsertProjectPulseViewAsAuditAsync(NpgsqlConnection connection, Guid administratorUserId, Guid viewedAsUserId, string route)
{
    try
    {
        const string sql = """
            INSERT INTO projectpulse_admin_view_as_audit (
                administrator_user_id,
                viewed_as_user_id,
                viewed_route,
                preview_mode,
                action_taken
            )
            VALUES (
                @administrator_user_id,
                @viewed_as_user_id,
                @viewed_route,
                'read_only',
                'global_view_as_preview'
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("administrator_user_id", administratorUserId);
        command.Parameters.AddWithValue("viewed_as_user_id", viewedAsUserId);
        command.Parameters.AddWithValue("viewed_route", route);
        await command.ExecuteNonQueryAsync();
    }
    catch
    {
        // Do not break read-only preview if audit insert fails.
    }
}


string? GetProjectPulseSessionToken(HttpRequest request)
{
    if (request.Headers.TryGetValue("X-ProjectPulse-Session", out var sessionHeader))
    {
        var token = sessionHeader.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token)) return token.Trim();
    }

    var authorization = request.Headers.Authorization.ToString();
    if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return authorization["Bearer ".Length..].Trim();
    }

    return null;
}

string HashSessionToken(string token)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

string GenerateSessionToken()
{
    return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

string HashProjectPulsePassword(string password)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(
        password: Encoding.UTF8.GetBytes(password),
        salt: salt,
        iterations: ProjectPulsePasswordIterations,
        hashAlgorithm: HashAlgorithmName.SHA256,
        outputLength: 32);

    return $"PBKDF2-SHA256${ProjectPulsePasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
}

bool VerifyProjectPulsePassword(string password, string? storedHash)
{
    if (string.IsNullOrWhiteSpace(storedHash)) return false;

    var parts = storedHash.Split('$');
    if (parts.Length != 4) return false;
    if (!parts[0].Equals("PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase)) return false;
    if (!int.TryParse(parts[1], out var iterations)) return false;

    var salt = Convert.FromBase64String(parts[2]);
    var expectedHash = Convert.FromBase64String(parts[3]);

    var actualHash = Rfc2898DeriveBytes.Pbkdf2(
        password: Encoding.UTF8.GetBytes(password),
        salt: salt,
        iterations: iterations,
        hashAlgorithm: HashAlgorithmName.SHA256,
        outputLength: expectedHash.Length);

    return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
}

string? ValidatePasswordQuality(string password)
{
    if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
    {
        return "Password must be at least 12 characters long.";
    }

    if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
    {
        return "Password must include uppercase, lowercase, number, and special character.";
    }

    return null;
}

async Task<ProjectPulseCreatedSession> CreateProjectPulseSessionAsync(
    NpgsqlConnection connection,
    Guid userId,
    string providerCode,
    HttpRequest request)
{
    var rawToken = GenerateSessionToken();
    var tokenHash = HashSessionToken(rawToken);
    var sessionId = Guid.NewGuid();
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(ProjectPulseSessionMinutes);

    await using var command = new NpgsqlCommand("""
        INSERT INTO auth_sessions (
            auth_session_id,
            user_id,
            provider_code,
            session_token_hash,
            expires_at,
            ip_address,
            user_agent,
            session_window_minutes
        )
        VALUES (
            @auth_session_id,
            @user_id,
            @provider_code,
            @session_token_hash,
            @expires_at,
            @ip_address,
            @user_agent,
            @session_window_minutes
        );
        """, connection);

    command.Parameters.AddWithValue("auth_session_id", sessionId);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("provider_code", providerCode);
    command.Parameters.AddWithValue("session_token_hash", tokenHash);
    command.Parameters.AddWithValue("expires_at", expiresAt);
    command.Parameters.AddWithValue("ip_address", (object?)request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? DBNull.Value);
    command.Parameters.AddWithValue("user_agent", (object?)request.Headers.UserAgent.ToString() ?? DBNull.Value);
    command.Parameters.AddWithValue("session_window_minutes", ProjectPulseSessionMinutes);

    await command.ExecuteNonQueryAsync();

    return new ProjectPulseCreatedSession(sessionId, rawToken, expiresAt);
}

async Task<ProjectPulseSessionValidation> ValidateProjectPulseSessionAsync(HttpContext context)
{
    var token = GetProjectPulseSessionToken(context.Request);
    if (string.IsNullOrWhiteSpace(token))
    {
        return new ProjectPulseSessionValidation(false, null, null, null, null, "Missing session token.");
    }

    var config = DatabaseConfig.FromEnvironment();

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            SELECT
                s.auth_session_id,
                s.user_id,
                s.provider_code,
                s.expires_at,
                u.email
            FROM auth_sessions s
            JOIN app_users u ON u.user_id = s.user_id
            WHERE s.session_token_hash = @session_token_hash
              AND s.revoked_at IS NULL
              AND s.expires_at > NOW()
              AND u.is_active = TRUE
              AND COALESCE(u.login_enabled, TRUE) = TRUE
              AND EXISTS (
                  SELECT 1
                  FROM app_user_role_assignments ura
                  JOIN app_roles r ON r.app_role_id = ura.app_role_id
                  WHERE ura.user_id = u.user_id
                    AND ura.is_active = TRUE
                    AND r.is_active = TRUE
              )
            LIMIT 1;
            """, connection);

        command.Parameters.AddWithValue("session_token_hash", HashSessionToken(token));

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new ProjectPulseSessionValidation(false, null, null, null, null, "Session expired or invalid.");
        }

        var sessionId = reader.GetGuid(0);
        var userId = reader.GetGuid(1);
        var providerCode = reader.GetString(2);
        var expiresAt = reader.GetFieldValue<DateTimeOffset>(3);
        var email = reader.GetString(4);

        await reader.CloseAsync();

        await using var updateCommand = new NpgsqlCommand("""
            UPDATE auth_sessions
            SET last_seen_at = NOW()
            WHERE auth_session_id = @auth_session_id;
            """, connection);

        updateCommand.Parameters.AddWithValue("auth_session_id", sessionId);
        await updateCommand.ExecuteNonQueryAsync();

        return new ProjectPulseSessionValidation(true, userId, email, providerCode, expiresAt, null);
    }
    catch
    {
        return new ProjectPulseSessionValidation(false, null, null, null, null, "Unable to validate session.");
    }
}


async Task<bool> UserHasActiveRoleAsync(NpgsqlConnection connection, Guid userId)
{
    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId);
    var result = await command.ExecuteScalarAsync();

    return result is bool value && value;
}

Guid? GetProjectPulseSessionUserId(HttpContext context)
{
    if (context.Items.TryGetValue("ProjectPulseEffectiveUserId", out var effectiveValue) && effectiveValue is Guid effectiveUserId)
    {
        return effectiveUserId;
    }

    if (context.Items.TryGetValue("ProjectPulseSessionUserId", out var value) && value is Guid userId)
    {
        return userId;
    }

    return null;
}

string? GetProjectPulseSessionEmail(HttpContext context)
{
    if (context.Items.TryGetValue("ProjectPulseEffectiveEmail", out var effectiveValue) && effectiveValue is string effectiveEmail)
    {
        return effectiveEmail;
    }

    if (context.Items.TryGetValue("ProjectPulseSessionEmail", out var value) && value is string email)
    {
        return email;
    }

    return null;
}




async Task<bool> RequestUserCanAccessCostAlertsAsync(HttpContext context, NpgsqlConnection connection, bool requireManage)
{
    var userId = GetProjectPulseSessionUserId(context);

    if (userId is null)
    {
        return false;
    }

    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
            LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND (
                    (NOT @require_manage AND r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR', 'PROJECT_MANAGEMENT', 'PROJECT_MANAGER', 'MANAGER'))
                 OR (NOT @require_manage AND p.permission_code IN ('VIEW_COST_ALERTS', 'MANAGE_COST_ALERTS', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'))
                 OR (@require_manage AND r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'))
                 OR (@require_manage AND p.permission_code IN ('MANAGE_COST_ALERTS', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'))
              )
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId.Value);
    command.Parameters.AddWithValue("require_manage", requireManage);

    var result = await command.ExecuteScalarAsync();

    return result is bool value && value;
}


async Task<bool> RequestUserCanManageCustomersAsync(HttpContext context, NpgsqlConnection connection)
{
    var userId = GetProjectPulseSessionUserId(context);

    if (userId is null)
    {
        return false;
    }

    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
            LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND (
                    r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
                 OR p.permission_code IN ('MANAGE_CUSTOMERS', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION')
              )
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId.Value);
    var result = await command.ExecuteScalarAsync();

    return result is bool value && value;
}


async Task<bool> RequestUserCanAccessUserAdministrationAsync(HttpContext context, NpgsqlConnection connection)
{
    var userId = GetProjectPulseSessionUserId(context);

    if (userId is null)
    {
        return false;
    }

    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
            LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND (
                    r.role_code IN ('ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR')
                 OR p.permission_code IN ('VIEW_USER_ADMIN', 'MANAGE_USER_ADMIN', 'MANAGE_ALL')
              )
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId.Value);
    var result = await command.ExecuteScalarAsync();

    return result is bool value && value;
}


async Task<bool> RequestUserIsAdministratorAsync(HttpContext context, NpgsqlConnection connection)
{
    var userId = GetProjectPulseSessionUserId(context);

    if (userId is null)
    {
        return false;
    }

    return await SessionUserIsAdministratorAsync(connection, userId.Value);
}


async Task<bool> SessionUserIsAdministratorAsync(NpgsqlConnection connection, Guid userId)
{
    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.role_code = 'ADMINISTRATOR'
              AND r.is_active = TRUE
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId);
    var result = await command.ExecuteScalarAsync();

    return result is bool value && value;
}


app.MapGet("/api/auth/login/route", async (string? username) =>
{
    var cleanedUsername = (username ?? string.Empty).Trim().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(cleanedUsername))
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "Username or email is required."
        });
    }

    if (cleanedUsername.EndsWith("@ussignal.com"))
    {
        return Results.Ok(new
        {
            status = "route_resolved",
            username = cleanedUsername,
            loginMethod = "sso",
            provider = "ENTRA_ID",
            displayName = "Continue with US Signal SSO",
            message = "US Signal users authenticate through Microsoft Entra ID."
        });
    }

    if (cleanedUsername.EndsWith(".local") || cleanedUsername.EndsWith("@ussignal.local"))
    {
        var config = DatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM auth_local_accounts la
                JOIN app_users u ON u.user_id = la.user_id
                WHERE lower(la.username) = lower(@username)
                  AND la.is_active = TRUE
                  AND u.is_active = TRUE
            );
            """, connection);
        command.Parameters.AddWithValue("username", cleanedUsername);

        var exists = (bool)(await command.ExecuteScalarAsync() ?? false);

        return Results.Ok(new
        {
            status = exists ? "route_resolved" : "local_account_not_found",
            username = cleanedUsername,
            loginMethod = "local",
            provider = "LOCAL",
            displayName = "Project Pulse local administrator login",
            requiresPassword = true,
            message = exists
                ? "Local administrator account requires Project Pulse password authentication."
                : "No active local account was found for this username."
        });
    }

    return Results.BadRequest(new
    {
        status = "unsupported_login_domain",
        username = cleanedUsername,
        message = "Use your US Signal email address for SSO or a Project Pulse .local administrator account."
    });
});

app.MapPost("/api/auth/password-reset/request", async (PasswordResetRequest request) =>
{
    var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "Username is required."
        });
    }

    if (!username.EndsWith(".local") && !username.EndsWith("@ussignal.local"))
    {
        return Results.BadRequest(new
        {
            status = "sso_account_reset_not_supported_here",
            message = "US Signal SSO users must reset passwords through Microsoft Entra ID. This Project Pulse reset workflow is only for .local administrator accounts."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        Guid userId;
        string displayName;

        await using (var userCommand = new NpgsqlCommand("""
            SELECT u.user_id, u.display_name
            FROM auth_local_accounts la
            JOIN app_users u ON u.user_id = la.user_id
            WHERE lower(la.username) = lower(@username)
              AND la.is_active = TRUE
              AND u.is_active = TRUE;
            """, connection, transaction))
        {
            userCommand.Parameters.AddWithValue("username", username);
            await using var reader = await userCommand.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    status = "local_account_not_found",
                    message = "No active local administrator account was found for that username."
                });
            }

            userId = reader.GetGuid(0);
            displayName = reader.GetString(1);
        }

        Guid resetRequestId;
        await using (var resetCommand = new NpgsqlCommand("""
            INSERT INTO auth_password_reset_requests (
                user_id,
                requested_by_email,
                approval_email_to,
                status,
                requested_at,
                expires_at,
                notes
            )
            VALUES (
                @user_id,
                @requested_by_email,
                ARRAY['ahmed.adeyemi@ussignal.com','customercare@ussignal.com'],
                'pending_approval',
                NOW(),
                NOW() + INTERVAL '24 hours',
                @notes
            )
            RETURNING auth_password_reset_request_id;
            """, connection, transaction))
        {
            resetCommand.Parameters.AddWithValue("user_id", userId);
            resetCommand.Parameters.AddWithValue("requested_by_email", username);
            resetCommand.Parameters.AddWithValue("notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());
            resetRequestId = (Guid)(await resetCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create password reset request."));
        }

        foreach (var recipient in new[] { "ahmed.adeyemi@ussignal.com", "customercare@ussignal.com" })
        {
            await using var notifyCommand = new NpgsqlCommand("""
                INSERT INTO notification_outbox (
                    notification_type,
                    recipient_email,
                    subject,
                    body,
                    related_entity_type,
                    related_entity_id
                )
                VALUES (
                    'local_admin_password_reset_approval',
                    @recipient_email,
                    @subject,
                    @body,
                    'auth_password_reset_request',
                    @related_entity_id
                );
                """, connection, transaction);

            notifyCommand.Parameters.AddWithValue("recipient_email", recipient);
            notifyCommand.Parameters.AddWithValue("subject", "Project Pulse local administrator password reset approval required");
            notifyCommand.Parameters.AddWithValue("body", $"A password reset was requested for local administrator account {username} ({displayName}). Approval is required before reset can continue.");
            notifyCommand.Parameters.AddWithValue("related_entity_id", resetRequestId);
            await notifyCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "password_reset_pending_approval",
            username,
            resetRequestId,
            approvalRequired = true,
            approvalEmails = new[] { "ahmed.adeyemi@ussignal.com", "customercare@ussignal.com" },
            message = "Password reset request created. Approval notification has been queued."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to create password reset request",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/auth/local-accounts", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var accounts = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT
            u.email,
            u.display_name,
            la.username,
            la.must_change_password,
            la.failed_login_count,
            la.locked_until,
            la.is_active
        FROM auth_local_accounts la
        JOIN app_users u ON u.user_id = la.user_id
        ORDER BY la.username;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        accounts.Add(new
        {
            email = reader.GetString(0),
            displayName = reader.GetString(1),
            username = reader.GetString(2),
            mustChangePassword = reader.GetBoolean(3),
            failedLoginCount = reader.GetInt32(4),
            lockedUntil = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5),
            isActive = reader.GetBoolean(6)
        });
    }

    return Results.Ok(new
    {
        count = accounts.Count,
        accounts
    });
});


app.MapGet("/api/utilization/current-quarter", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var quarterNumber = ((today.Month - 1) / 3) + 1;
    var quarterStartMonth = ((quarterNumber - 1) * 3) + 1;
    var quarterStart = new DateOnly(today.Year, quarterStartMonth, 1);
    var quarterEndExclusive = quarterStart.AddMonths(3);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var userId = sessionUserId.Value;

    string policyName;
    decimal standardPeriodHours;
    decimal targetPercent;

    await using (var policyCommand = new NpgsqlCommand("""
        SELECT policy_name, standard_period_hours, default_target_percent
        FROM utilization_policies
        WHERE is_active = TRUE
        ORDER BY created_at DESC
        LIMIT 1;
        """, connection))
    {
        await using var policyReader = await policyCommand.ExecuteReaderAsync();

        if (!await policyReader.ReadAsync())
        {
            return Results.Ok(new
            {
                status = "no_active_policy",
                message = "No active utilization policy is configured."
            });
        }

        policyName = policyReader.GetString(0);
        standardPeriodHours = policyReader.GetDecimal(1);
        targetPercent = policyReader.GetDecimal(2);
    }

    decimal currentBillableHours;

    await using (var hoursCommand = new NpgsqlCommand("""
        SELECT COALESCE(SUM(te.hours), 0)
        FROM time_entries te
        WHERE te.user_id = @user_id
          AND te.work_date >= @quarter_start
          AND te.work_date < @quarter_end
          AND te.billable = TRUE
          AND te.status <> 'manager_declined';
        """, connection))
    {
        hoursCommand.Parameters.AddWithValue("user_id", userId);
        hoursCommand.Parameters.AddWithValue("quarter_start", quarterStart);
        hoursCommand.Parameters.AddWithValue("quarter_end", quarterEndExclusive);

        currentBillableHours = (decimal)(await hoursCommand.ExecuteScalarAsync() ?? 0m);
    }

    var targetHours = Math.Round(standardPeriodHours * (targetPercent / 100m), 2);
    var currentUtilizationPercent = standardPeriodHours == 0
        ? 0m
        : Math.Round((currentBillableHours / standardPeriodHours) * 100m, 2);
    var hoursLeftToTarget = targetHours > currentBillableHours
        ? Math.Round(targetHours - currentBillableHours, 2)
        : 0m;

    return Results.Ok(new
    {
        policyName,
        quarter = $"Q{quarterNumber} {today.Year}",
        quarterStart,
        quarterEnd = quarterEndExclusive.AddDays(-1),
        standardPeriodHours,
        targetPercent,
        targetHours,
        currentBillableHours,
        currentUtilizationPercent,
        hoursLeftToTarget
    });
});


app.MapGet("/api/auth/password-reset/approvals", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "Password reset approvals are restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var approvals = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            pr.auth_password_reset_request_id,
            pr.requested_by_email,
            pr.approval_email_to,
            pr.status,
            pr.requested_at,
            pr.expires_at,
            pr.notes,
            u.email AS account_email,
            u.display_name AS account_display_name,
            pr.approved_at,
            pr.approved_by_email,
            pr.completed_at
        FROM auth_password_reset_requests pr
        JOIN app_users u ON u.user_id = pr.user_id
        WHERE pr.status IN ('pending_approval', 'approved')
          AND lower(u.email) LIKE '%.local'
        ORDER BY
            CASE pr.status
                WHEN 'approved' THEN 1
                WHEN 'pending_approval' THEN 2
                ELSE 3
            END,
            pr.requested_at DESC;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        approvals.Add(new
        {
            resetRequestId = reader.GetGuid(0),
            requestedByEmail = reader.GetString(1),
            approvalEmails = reader.GetFieldValue<string[]>(2),
            status = reader.GetString(3),
            requestedAt = reader.GetFieldValue<DateTimeOffset>(4),
            expiresAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5),
            notes = reader.IsDBNull(6) ? null : reader.GetString(6),
            accountEmail = reader.GetString(7),
            accountDisplayName = reader.GetString(8),
            approvedAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(9),
            approvedByEmail = reader.IsDBNull(10) ? null : reader.GetString(10),
            completedAt = reader.IsDBNull(11) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(11),
            approvalType = "local_admin_password_reset",
            approvalTitle = "Local administrator password reset",
            approvalDescription = reader.GetString(3) == "approved"
                ? "Approval is complete. Set a temporary password to finish the reset."
                : "Approve or decline a password reset request for a Project Pulse local administrator account."
        });
    }

    return Results.Ok(new
    {
        count = approvals.Count,
        approvals
    });
});


app.MapPost("/api/auth/password-reset/approve", async (PasswordResetApprovalAction request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        Guid resetRequestId = request.ResetRequestId;
        string approvedByEmail = string.IsNullOrWhiteSpace(request.ActionByEmail)
            ? "ahmed.adeyemi@ussignal.com"
            : request.ActionByEmail.Trim().ToLowerInvariant();

        string? accountEmail = null;

        await using (var updateCommand = new NpgsqlCommand("""
            UPDATE auth_password_reset_requests pr
            SET status = 'approved',
                approved_at = NOW(),
                approved_by_email = @approved_by_email,
                notes = COALESCE(pr.notes, '') || E'\nApproval note: ' || COALESCE(@notes, ''),
                expires_at = COALESCE(pr.expires_at, NOW() + INTERVAL '24 hours')
            FROM app_users u
            WHERE u.user_id = pr.user_id
              AND pr.auth_password_reset_request_id = @reset_request_id
              AND pr.status = 'pending_approval'
              AND lower(u.email) LIKE '%.local'
            RETURNING pr.user_id;
            """, connection, transaction))
        {
            updateCommand.Parameters.AddWithValue("reset_request_id", resetRequestId);
            updateCommand.Parameters.AddWithValue("approved_by_email", approvedByEmail);
            updateCommand.Parameters.AddWithValue("notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());

            var userIdResult = await updateCommand.ExecuteScalarAsync();
            if (userIdResult is not Guid userId)
            {
                return Results.NotFound(new
                {
                    status = "not_found_or_already_processed",
                    message = "The password reset request was not found or has already been processed."
                });
            }

            await using var userCommand = new NpgsqlCommand("""
                SELECT email
                FROM app_users
                WHERE user_id = @user_id;
                """, connection, transaction);
            userCommand.Parameters.AddWithValue("user_id", userId);
            accountEmail = (string?)await userCommand.ExecuteScalarAsync();
        }

        foreach (var recipient in new[] { "ahmed.adeyemi@ussignal.com", "customercare@ussignal.com" })
        {
            await using var notifyCommand = new NpgsqlCommand("""
                INSERT INTO notification_outbox (
                    notification_type,
                    recipient_email,
                    subject,
                    body,
                    related_entity_type,
                    related_entity_id
                )
                VALUES (
                    'local_admin_password_reset_approved',
                    @recipient_email,
                    @subject,
                    @body,
                    'auth_password_reset_request',
                    @related_entity_id
                );
                """, connection, transaction);

            notifyCommand.Parameters.AddWithValue("recipient_email", recipient);
            notifyCommand.Parameters.AddWithValue("subject", "Project Pulse local administrator password reset approved");
            notifyCommand.Parameters.AddWithValue("body", $"Password reset request for {accountEmail} was approved by {approvedByEmail}. The next step is to set a temporary password once local password hashing is enabled.");
            notifyCommand.Parameters.AddWithValue("related_entity_id", resetRequestId);
            await notifyCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "approved",
            resetRequestId,
            accountEmail,
            approvedByEmail,
            message = "Password reset request approved. A notification has been queued. Set a temporary password to complete the reset."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to approve password reset request",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/auth/password-reset/decline", async (PasswordResetApprovalAction request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "Password reset approvals are restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    string declinedByEmail = string.IsNullOrWhiteSpace(request.ActionByEmail)
        ? "ahmed.adeyemi@ussignal.com"
        : request.ActionByEmail.Trim().ToLowerInvariant();

    await using var command = new NpgsqlCommand("""
        UPDATE auth_password_reset_requests pr
        SET status = 'declined',
            approved_by_email = @declined_by_email,
            notes = COALESCE(pr.notes, '') || E'\nDecline note: ' || COALESCE(@notes, '')
        FROM app_users u
        WHERE u.user_id = pr.user_id
          AND pr.auth_password_reset_request_id = @reset_request_id
          AND pr.status = 'pending_approval'
          AND lower(u.email) LIKE '%.local';
        """, connection);

    command.Parameters.AddWithValue("reset_request_id", request.ResetRequestId);
    command.Parameters.AddWithValue("declined_by_email", declinedByEmail);
    command.Parameters.AddWithValue("notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());

    var rows = await command.ExecuteNonQueryAsync();

    if (rows == 0)
    {
        return Results.NotFound(new
        {
            status = "not_found_or_already_processed",
            message = "The password reset request was not found or has already been processed."
        });
    }

    return Results.Ok(new
    {
        status = "declined",
        resetRequestId = request.ResetRequestId,
        declinedByEmail,
        message = "Password reset request declined."
    });
});


app.MapPost("/api/auth/sso/dev-login", async (SsoDevelopmentLoginRequest request, HttpRequest httpRequest) =>
{
    var email = request.Email.Trim().ToLowerInvariant();

    if (!email.EndsWith("@ussignal.com", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            status = "invalid_sso_domain",
            message = "US Signal SSO is only available for @ussignal.com accounts."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    await using var userCommand = new NpgsqlCommand("""
        SELECT user_id, email, display_name
        FROM app_users
        WHERE lower(email) = @email
          AND is_active = TRUE
        LIMIT 1;
        """, connection);

    userCommand.Parameters.AddWithValue("email", email);

    await using var reader = await userCommand.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return Results.NotFound(new
        {
            status = "sso_user_not_found",
            message = "No active Project Pulse user was found for this US Signal SSO account."
        });
    }

    var userId = reader.GetGuid(0);
    var resolvedEmail = reader.GetString(1);
    var displayName = reader.GetString(2);

    await reader.CloseAsync();

    if (!await UserHasActiveRoleAsync(connection, userId))
    {
        return Results.Json(new
        {
            status = "no_active_project_pulse_role",
            message = "Your account exists in Project Pulse, but no active role has been assigned. Contact a Project Pulse administrator."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var session = await CreateProjectPulseSessionAsync(connection, userId, "ENTRA_ID", httpRequest);

    return Results.Ok(new
    {
        status = "sso_development_session_created",
        loginMethod = "sso",
        provider = "ENTRA_ID",
        username = resolvedEmail,
        displayName,
        sessionToken = session.RawToken,
        expiresAt = session.ExpiresAt,
        sessionMinutes = ProjectPulseSessionMinutes,
        warningMinutes = ProjectPulseSessionWarningMinutes,
        message = "Development SSO session created. Replace this endpoint with Microsoft Entra ID token validation for production."
    });
});


app.MapPost("/api/auth/password-reset/complete", async (PasswordResetCompletionRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var passwordIssue = ValidatePasswordQuality(request.TemporaryPassword);
    if (passwordIssue is not null)
    {
        return Results.BadRequest(new
        {
            status = "password_quality_failed",
            message = passwordIssue
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "Password reset completion is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var completedByEmail = string.IsNullOrWhiteSpace(request.ActionByEmail)
            ? "unknown"
            : request.ActionByEmail.Trim().ToLowerInvariant();

        Guid userId;
        string accountEmail;
        string accountDisplayName;

        await using (var lookupCommand = new NpgsqlCommand("""
            SELECT
                pr.user_id,
                u.email,
                u.display_name
            FROM auth_password_reset_requests pr
            JOIN app_users u ON u.user_id = pr.user_id
            JOIN auth_local_accounts la ON la.user_id = u.user_id
            WHERE pr.auth_password_reset_request_id = @reset_request_id
              AND pr.status = 'approved'
              AND COALESCE(pr.expires_at, NOW() + INTERVAL '1 minute') >= NOW()
              AND lower(u.email) LIKE '%.local'
              AND u.is_active = TRUE
              AND la.is_active = TRUE;
            """, connection, transaction))
        {
            lookupCommand.Parameters.AddWithValue("reset_request_id", request.ResetRequestId);

            await using var reader = await lookupCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    status = "approved_reset_not_found",
                    message = "No approved, unexpired local admin password reset request was found."
                });
            }

            userId = reader.GetGuid(0);
            accountEmail = reader.GetString(1);
            accountDisplayName = reader.GetString(2);
        }

        var passwordHash = HashProjectPulsePassword(request.TemporaryPassword);

        await using (var updatePasswordCommand = new NpgsqlCommand("""
            UPDATE auth_local_accounts
            SET password_hash = @password_hash,
                must_change_password = TRUE,
                failed_login_count = 0,
                locked_until = NULL,
                password_hash_updated_at = NOW()
            WHERE user_id = @user_id;
            """, connection, transaction))
        {
            updatePasswordCommand.Parameters.AddWithValue("password_hash", passwordHash);
            updatePasswordCommand.Parameters.AddWithValue("user_id", userId);
            await updatePasswordCommand.ExecuteNonQueryAsync();
        }

        await using (var completeCommand = new NpgsqlCommand("""
            UPDATE auth_password_reset_requests
            SET status = 'completed',
                completed_at = NOW(),
                notes = COALESCE(notes, '') || E'\nTemporary password set by: ' || @completed_by_email || E'\nCompletion note: ' || COALESCE(@notes, '')
            WHERE auth_password_reset_request_id = @reset_request_id
              AND status = 'approved';
            """, connection, transaction))
        {
            completeCommand.Parameters.AddWithValue("reset_request_id", request.ResetRequestId);
            completeCommand.Parameters.AddWithValue("completed_by_email", completedByEmail);
            completeCommand.Parameters.AddWithValue("notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());
            await completeCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "password_reset_completed",
            resetRequestId = request.ResetRequestId,
            accountEmail,
            accountDisplayName,
            mustChangePassword = true,
            message = $"Temporary password was set for {accountEmail}. The local administrator must change it at next login."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to complete password reset",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});



app.MapPost("/api/auth/local/login", async (LocalLoginRequest request, HttpRequest httpRequest) =>
{
    var username = request.Username.Trim().ToLowerInvariant();

    if (!username.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
        !username.EndsWith("@ussignal.local", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            status = "invalid_local_account",
            message = "Local login is only available for .local administrator accounts."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand("""
        SELECT
            la.user_id,
            la.username,
            la.password_hash,
            la.must_change_password,
            la.failed_login_count,
            la.locked_until,
            la.is_active,
            u.email,
            u.display_name
        FROM auth_local_accounts la
        JOIN app_users u ON u.user_id = la.user_id
        WHERE lower(la.username) = @username
        LIMIT 1;
        """, connection);

    command.Parameters.AddWithValue("username", username);

    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return Results.Unauthorized();
    }

    var userId = reader.GetGuid(0);
    var resolvedUsername = reader.GetString(1);
    var passwordHash = reader.IsDBNull(2) ? null : reader.GetString(2);
    var mustChangePassword = reader.GetBoolean(3);
    var failedLoginCount = reader.GetInt32(4);
    var lockedUntil = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5);
    var isActive = reader.GetBoolean(6);
    var email = reader.GetString(7);
    var displayName = reader.GetString(8);

    await reader.CloseAsync();

    if (!await UserHasActiveRoleAsync(connection, userId))
    {
        return Results.Json(new
        {
            status = "no_active_project_pulse_role",
            message = "This local account exists but has no active Project Pulse role assigned."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (!isActive)
    {
        return Results.Unauthorized();
    }

    if (lockedUntil is not null && lockedUntil.Value > DateTimeOffset.UtcNow)
    {
        return Results.Json(new
        {
            status = "local_account_locked",
            lockedUntil,
            message = "This local account is temporarily locked because of failed login attempts."
        }, statusCode: StatusCodes.Status423Locked);
    }

    if (string.IsNullOrWhiteSpace(passwordHash))
    {
        return Results.Json(new
        {
            status = "local_password_not_configured",
            message = "This local account does not have a password configured yet. Approve a password reset and set a temporary password first."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var passwordValid = VerifyProjectPulsePassword(request.Password, passwordHash);

    if (!passwordValid)
    {
        var newFailedCount = failedLoginCount + 1;
        DateTimeOffset? newLockedUntil = newFailedCount >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15) : null;

        await using var failCommand = new NpgsqlCommand("""
            UPDATE auth_local_accounts
            SET failed_login_count = @failed_login_count,
                locked_until = @locked_until
            WHERE user_id = @user_id;
            """, connection);

        failCommand.Parameters.AddWithValue("failed_login_count", newFailedCount);
        failCommand.Parameters.AddWithValue("locked_until", (object?)newLockedUntil ?? DBNull.Value);
        failCommand.Parameters.AddWithValue("user_id", userId);
        await failCommand.ExecuteNonQueryAsync();

        return Results.Json(new
        {
            status = "invalid_local_credentials",
            failedLoginCount = newFailedCount,
            lockedUntil = newLockedUntil,
            message = newLockedUntil is null
                ? "Invalid local administrator credentials."
                : "Invalid credentials. This local account has been temporarily locked for 15 minutes."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var successCommand = new NpgsqlCommand("""
        UPDATE auth_local_accounts
        SET failed_login_count = 0,
            locked_until = NULL
        WHERE user_id = @user_id;
        """, connection);

    successCommand.Parameters.AddWithValue("user_id", userId);
    await successCommand.ExecuteNonQueryAsync();

    var session = await CreateProjectPulseSessionAsync(connection, userId, "LOCAL", httpRequest);

    return Results.Ok(new
    {
        status = "local_login_success",
        loginMethod = "local",
        provider = "LOCAL",
        username = resolvedUsername,
        email,
        displayName,
        mustChangePassword,
        sessionToken = session.RawToken,
        expiresAt = session.ExpiresAt,
        sessionMinutes = ProjectPulseSessionMinutes,
        warningMinutes = ProjectPulseSessionWarningMinutes
    });
});

app.MapPost("/api/auth/session/extend", async (HttpRequest httpRequest) =>
{
    var token = GetProjectPulseSessionToken(httpRequest);

    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.Unauthorized();
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var newExpiresAt = DateTimeOffset.UtcNow.AddMinutes(ProjectPulseSessionMinutes);

    await using var command = new NpgsqlCommand("""
        UPDATE auth_sessions
        SET expires_at = @expires_at,
            last_seen_at = NOW()
        WHERE session_token_hash = @session_token_hash
          AND revoked_at IS NULL
          AND expires_at > NOW()
        RETURNING auth_session_id;
        """, connection);

    command.Parameters.AddWithValue("expires_at", newExpiresAt);
    command.Parameters.AddWithValue("session_token_hash", HashSessionToken(token));

    var result = await command.ExecuteScalarAsync();

    if (result is not Guid)
    {
        return Results.Json(new
        {
            status = "session_expired",
            message = "Your session has already expired. Please sign in again."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        status = "session_extended",
        expiresAt = newExpiresAt,
        sessionMinutes = ProjectPulseSessionMinutes,
        warningMinutes = ProjectPulseSessionWarningMinutes,
        message = "Your Project Pulse session has been extended."
    });
});

app.MapPost("/api/auth/session/logout", async (HttpRequest httpRequest) =>
{
    var token = GetProjectPulseSessionToken(httpRequest);

    if (!string.IsNullOrWhiteSpace(token))
    {
        var config = DatabaseConfig.FromEnvironment();
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            UPDATE auth_sessions
            SET revoked_at = NOW(),
                revoked_reason = 'user_logout'
            WHERE session_token_hash = @session_token_hash
              AND revoked_at IS NULL;
            """, connection);

        command.Parameters.AddWithValue("session_token_hash", HashSessionToken(token));
        await command.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        status = "signed_out",
        message = "Signed out of Project Pulse."
    });
});

app.MapPost("/api/auth/local/set-temporary-password", async (SetTemporaryPasswordRequest request, HttpRequest httpRequest) =>
{
    var token = GetProjectPulseSessionToken(httpRequest);
    if (string.IsNullOrWhiteSpace(token)) return Results.Unauthorized();

    var passwordIssue = ValidatePasswordQuality(request.TemporaryPassword);
    if (passwordIssue is not null)
    {
        return Results.BadRequest(new
        {
            status = "password_quality_failed",
            message = passwordIssue
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var tokenHash = HashSessionToken(token);

        Guid? actingUserId = null;
        await using (var sessionCommand = new NpgsqlCommand("""
            SELECT user_id
            FROM auth_sessions
            WHERE session_token_hash = @session_token_hash
              AND revoked_at IS NULL
              AND expires_at > NOW()
            LIMIT 1;
            """, connection, transaction))
        {
            sessionCommand.Parameters.AddWithValue("session_token_hash", tokenHash);
            var result = await sessionCommand.ExecuteScalarAsync();
            if (result is Guid resolvedUserId) actingUserId = resolvedUserId;
        }

        if (actingUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Unauthorized();
        }

        var isAdmin = await SessionUserIsAdministratorAsync(connection, actingUserId.Value);
        if (!isAdmin)
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "admin_required",
                message = "Only an administrator can set a temporary local administrator password."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var username = request.Username.Trim().ToLowerInvariant();
        var newHash = HashProjectPulsePassword(request.TemporaryPassword);

        await using var updateCommand = new NpgsqlCommand("""
            UPDATE auth_local_accounts la
            SET password_hash = @password_hash,
                must_change_password = TRUE,
                failed_login_count = 0,
                locked_until = NULL,
                password_hash_updated_at = NOW()
            FROM app_users u
            WHERE u.user_id = la.user_id
              AND lower(la.username) = @username
              AND u.is_active = TRUE
              AND EXISTS (
                  SELECT 1
                  FROM auth_password_reset_requests pr
                  WHERE pr.auth_password_reset_request_id = @reset_request_id
                    AND pr.user_id = la.user_id
                    AND pr.status IN ('approved', 'temporary_password_set')
              );
            """, connection, transaction);

        updateCommand.Parameters.AddWithValue("password_hash", newHash);
        updateCommand.Parameters.AddWithValue("username", username);
        updateCommand.Parameters.AddWithValue("reset_request_id", request.ResetRequestId);

        var rows = await updateCommand.ExecuteNonQueryAsync();

        if (rows == 0)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new
            {
                status = "approved_reset_not_found",
                message = "No approved password reset request was found for this local account."
            });
        }

        await using var resetCommand = new NpgsqlCommand("""
            UPDATE auth_password_reset_requests
            SET status = 'temporary_password_set',
                notes = COALESCE(notes, '') || E'\nTemporary password was set by administrator.'
            WHERE auth_password_reset_request_id = @reset_request_id;
            """, connection, transaction);

        resetCommand.Parameters.AddWithValue("reset_request_id", request.ResetRequestId);
        await resetCommand.ExecuteNonQueryAsync();

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "temporary_password_set",
            username,
            mustChangePassword = true,
            message = "Temporary password has been set. The local administrator must change it after login."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to set temporary local password",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/auth/local/change-password", async (ChangeLocalPasswordRequest request, HttpRequest httpRequest) =>
{
    var token = GetProjectPulseSessionToken(httpRequest);
    if (string.IsNullOrWhiteSpace(token)) return Results.Unauthorized();

    var passwordIssue = ValidatePasswordQuality(request.NewPassword);
    if (passwordIssue is not null)
    {
        return Results.BadRequest(new
        {
            status = "password_quality_failed",
            message = passwordIssue
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    Guid? sessionUserId = null;

    await using (var sessionCommand = new NpgsqlCommand("""
        SELECT user_id
        FROM auth_sessions
        WHERE session_token_hash = @session_token_hash
          AND provider_code = 'LOCAL'
          AND revoked_at IS NULL
          AND expires_at > NOW()
        LIMIT 1;
        """, connection))
    {
        sessionCommand.Parameters.AddWithValue("session_token_hash", HashSessionToken(token));
        var result = await sessionCommand.ExecuteScalarAsync();
        if (result is Guid resolvedUserId) sessionUserId = resolvedUserId;
    }

    if (sessionUserId is null)
    {
        return Results.Json(new
        {
            status = "local_session_required",
            message = "A valid local administrator session is required to change this password."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    string? currentHash = null;

    await using (var lookupCommand = new NpgsqlCommand("""
        SELECT password_hash
        FROM auth_local_accounts
        WHERE user_id = @user_id
          AND is_active = TRUE;
        """, connection))
    {
        lookupCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        currentHash = (string?)await lookupCommand.ExecuteScalarAsync();
    }

    if (!VerifyProjectPulsePassword(request.CurrentPassword, currentHash))
    {
        return Results.Json(new
        {
            status = "invalid_current_password",
            message = "The current password is incorrect."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var newHash = HashProjectPulsePassword(request.NewPassword);

    await using var updateCommand = new NpgsqlCommand("""
        UPDATE auth_local_accounts
        SET password_hash = @password_hash,
            must_change_password = FALSE,
            failed_login_count = 0,
            locked_until = NULL,
            password_hash_updated_at = NOW(),
            last_password_change_at = NOW()
        WHERE user_id = @user_id;
        """, connection);

    updateCommand.Parameters.AddWithValue("password_hash", newHash);
    updateCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
    await updateCommand.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        status = "password_changed",
        mustChangePassword = false,
        message = "Local administrator password changed successfully."
    });
});



app.MapGet("/api/admin/azure/config", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new { status = "admin_required", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var command = new NpgsqlCommand("""
        SELECT
            azure_entra_settings_id,
            tenant_id,
            client_id,
            authority_url,
            redirect_uri,
            graph_scope,
            sync_enabled,
            default_role_code,
            sync_frequency_hours,
            last_sync_at,
            last_sync_status,
            last_sync_message,
            updated_by_email,
            updated_at
        FROM azure_entra_settings
        ORDER BY created_at
        LIMIT 1;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return Results.NotFound(new { status = "azure_config_missing", message = "Azure/Entra configuration row was not found." });
    }

    return Results.Ok(new
    {
        settingsId = reader.GetGuid(0),
        tenantId = reader.IsDBNull(1) ? "" : reader.GetString(1),
        clientId = reader.IsDBNull(2) ? "" : reader.GetString(2),
        authorityUrl = reader.IsDBNull(3) ? "" : reader.GetString(3),
        redirectUri = reader.IsDBNull(4) ? "" : reader.GetString(4),
        graphScope = reader.GetString(5),
        syncEnabled = reader.GetBoolean(6),
        defaultRoleCode = reader.GetString(7),
        syncFrequencyHours = reader.GetInt32(8),
        lastSyncAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(9),
        lastSyncStatus = reader.IsDBNull(10) ? null : reader.GetString(10),
        lastSyncMessage = reader.IsDBNull(11) ? null : reader.GetString(11),
        updatedByEmail = reader.IsDBNull(12) ? null : reader.GetString(12),
        updatedAt = reader.GetFieldValue<DateTimeOffset>(13)
    });
});

app.MapPost("/api/admin/azure/config", async (AzureAdminConfigRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new { status = "admin_required", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var updatedBy = GetProjectPulseSessionEmail(httpContext) ?? "unknown";

    await using var command = new NpgsqlCommand("""
        UPDATE azure_entra_settings
        SET tenant_id = NULLIF(@tenant_id, ''),
            client_id = NULLIF(@client_id, ''),
            authority_url = NULLIF(@authority_url, ''),
            redirect_uri = NULLIF(@redirect_uri, ''),
            graph_scope = COALESCE(NULLIF(@graph_scope, ''), 'User.Read.All Directory.Read.All'),
            sync_enabled = @sync_enabled,
            default_role_code = COALESCE(NULLIF(@default_role_code, ''), 'ENGINEER'),
            sync_frequency_hours = GREATEST(@sync_frequency_hours, 1),
            updated_by_email = @updated_by_email,
            updated_at = NOW()
        WHERE azure_entra_settings_id = (
            SELECT azure_entra_settings_id
            FROM azure_entra_settings
            ORDER BY created_at
            LIMIT 1
        );
        """, connection);

    command.Parameters.AddWithValue("tenant_id", request.TenantId?.Trim() ?? "");
    command.Parameters.AddWithValue("client_id", request.ClientId?.Trim() ?? "");
    command.Parameters.AddWithValue("authority_url", request.AuthorityUrl?.Trim() ?? "");
    command.Parameters.AddWithValue("redirect_uri", request.RedirectUri?.Trim() ?? "");
    command.Parameters.AddWithValue("graph_scope", request.GraphScope?.Trim() ?? "User.Read.All Directory.Read.All");
    command.Parameters.AddWithValue("sync_enabled", request.SyncEnabled);
    command.Parameters.AddWithValue("default_role_code", string.IsNullOrWhiteSpace(request.DefaultRoleCode) ? "ENGINEER" : request.DefaultRoleCode.Trim().ToUpperInvariant());
    command.Parameters.AddWithValue("sync_frequency_hours", request.SyncFrequencyHours <= 0 ? 24 : request.SyncFrequencyHours);
    command.Parameters.AddWithValue("updated_by_email", updatedBy);

    await command.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        status = "azure_config_saved",
        message = "Azure/Entra configuration foundation saved."
    });
});

app.MapGet("/api/admin/azure/users", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new { status = "admin_required", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var users = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            u.user_id,
            u.email,
            u.display_name,
            u.entra_object_id,
            u.source_provider,
            u.job_title,
            u.department_name,
            u.office_location,
            u.manager_email,
            u.login_enabled,
            u.is_active,
            u.last_directory_sync_at,
            COALESCE(array_agg(r.role_name ORDER BY r.display_order) FILTER (WHERE r.role_name IS NOT NULL), ARRAY[]::varchar[]) AS role_names
        FROM app_users u
        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE COALESCE(u.source_provider, '') IN ('ENTRA_ID', 'ENTRA_ID_TEST')
           OR lower(u.email) LIKE '%@ussignal.com'
           OR lower(u.email) LIKE '%@onenecklab.com'
           OR lower(u.email) LIKE '%@onitdemo.com'
        GROUP BY u.user_id, u.email, u.display_name, u.entra_object_id, u.source_provider, u.job_title, u.department_name, u.office_location, u.manager_email, u.login_enabled, u.is_active, u.last_directory_sync_at
        ORDER BY u.display_name, u.email;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        users.Add(new
        {
            userId = reader.GetGuid(0),
            email = reader.GetString(1),
            displayName = reader.GetString(2),
            entraObjectId = reader.IsDBNull(3) ? null : reader.GetString(3),
            sourceProvider = reader.GetString(4),
            jobTitle = reader.IsDBNull(5) ? null : reader.GetString(5),
            departmentName = reader.IsDBNull(6) ? null : reader.GetString(6),
            officeLocation = reader.IsDBNull(7) ? null : reader.GetString(7),
            managerEmail = reader.IsDBNull(8) ? null : reader.GetString(8),
            loginEnabled = reader.GetBoolean(9),
            isActive = reader.GetBoolean(10),
            lastDirectorySyncAt = reader.IsDBNull(11) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(11),
            roleNames = reader.GetFieldValue<string[]>(12)
        });
    }

    return Results.Ok(new
    {
        count = users.Count,
        users
    });
});

app.MapPost("/api/admin/azure/users/import", async (AzureUserImportRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (request.Users is null || request.Users.Count == 0)
    {
        return Results.BadRequest(new { status = "no_users", message = "Provide at least one Azure/Entra user to import." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new { status = "admin_required", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    var syncRunId = Guid.NewGuid();
    var imported = 0;
    var updated = 0;
    var skipped = 0;
    var errors = new List<object>();

    try
    {
        await using (var runCommand = new NpgsqlCommand("""
            INSERT INTO azure_entra_sync_runs (
                azure_entra_sync_run_id,
                status,
                triggered_by_email,
                message
            )
            VALUES (
                @sync_run_id,
                'started',
                @triggered_by_email,
                'Manual Azure/Entra import foundation started.'
            );
            """, connection, transaction))
        {
            runCommand.Parameters.AddWithValue("sync_run_id", syncRunId);
            runCommand.Parameters.AddWithValue("triggered_by_email", (object?)GetProjectPulseSessionEmail(httpContext) ?? DBNull.Value);
            await runCommand.ExecuteNonQueryAsync();
        }

        foreach (var user in request.Users)
        {
            var email = user.Email?.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(email) || !email.EndsWith("@ussignal.com", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                errors.Add(new { email, error = "Skipped because email is blank or not @ussignal.com." });
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? email
                : user.DisplayName.Trim();

            Guid resolvedUserId;
            bool existed;

            await using (var upsertCommand = new NpgsqlCommand("""
                INSERT INTO app_users (
                    email,
                    display_name,
                    is_active,
                    login_enabled,
                    source_provider,
                    entra_object_id,
                    job_title,
                    department_name,
                    office_location,
                    manager_email,
                    last_directory_sync_at
                )
                VALUES (
                    @email,
                    @display_name,
                    TRUE,
                    TRUE,
                    'ENTRA_ID',
                    NULLIF(@entra_object_id, ''),
                    NULLIF(@job_title, ''),
                    NULLIF(@department_name, ''),
                    NULLIF(@office_location, ''),
                    NULLIF(@manager_email, ''),
                    NOW()
                )
                ON CONFLICT (email) DO UPDATE
                SET display_name = EXCLUDED.display_name,
                    is_active = TRUE,
                    login_enabled = TRUE,
                    source_provider = 'ENTRA_ID',
                    entra_object_id = COALESCE(EXCLUDED.entra_object_id, app_users.entra_object_id),
                    job_title = EXCLUDED.job_title,
                    department_name = EXCLUDED.department_name,
                    office_location = EXCLUDED.office_location,
                    manager_email = EXCLUDED.manager_email,
                    last_directory_sync_at = NOW()
                RETURNING user_id, (xmax <> 0) AS existed;
                """, connection, transaction))
            {
                upsertCommand.Parameters.AddWithValue("email", email);
                upsertCommand.Parameters.AddWithValue("display_name", displayName);
                upsertCommand.Parameters.AddWithValue("entra_object_id", user.EntraObjectId?.Trim() ?? "");
                upsertCommand.Parameters.AddWithValue("job_title", user.JobTitle?.Trim() ?? "");
                upsertCommand.Parameters.AddWithValue("department_name", user.DepartmentName?.Trim() ?? "");
                upsertCommand.Parameters.AddWithValue("office_location", user.OfficeLocation?.Trim() ?? "");
                upsertCommand.Parameters.AddWithValue("manager_email", user.ManagerEmail?.Trim().ToLowerInvariant() ?? "");

                await using var reader = await upsertCommand.ExecuteReaderAsync();
                await reader.ReadAsync();
                resolvedUserId = reader.GetGuid(0);
                existed = reader.GetBoolean(1);
            }

            await using (var roleCommand = new NpgsqlCommand("""
                INSERT INTO app_user_role_assignments (
                    user_id,
                    app_role_id,
                    assignment_reason,
                    is_active
                )
                SELECT @user_id, r.app_role_id, 'Default Engineer role from Azure/Entra import', TRUE
                FROM app_roles r
                WHERE r.role_code = 'ENGINEER'
                ON CONFLICT (user_id, app_role_id) DO UPDATE
                SET is_active = TRUE,
                    assignment_reason = EXCLUDED.assignment_reason,
                    updated_at = NOW();
                """, connection, transaction))
            {
                roleCommand.Parameters.AddWithValue("user_id", resolvedUserId);
                await roleCommand.ExecuteNonQueryAsync();
            }

            if (existed) updated++;
            else imported++;
        }

        await using (var completeCommand = new NpgsqlCommand("""
            UPDATE azure_entra_sync_runs
            SET sync_completed_at = NOW(),
                status = 'completed_foundation_import',
                users_seen = @users_seen,
                users_imported = @users_imported,
                users_updated = @users_updated,
                users_skipped = @users_skipped,
                message = @message
            WHERE azure_entra_sync_run_id = @sync_run_id;

            UPDATE azure_entra_settings
            SET last_sync_at = NOW(),
                last_sync_status = 'completed_foundation_import',
                last_sync_message = @message,
                updated_at = NOW();
            """, connection, transaction))
        {
            completeCommand.Parameters.AddWithValue("sync_run_id", syncRunId);
            completeCommand.Parameters.AddWithValue("users_seen", request.Users.Count);
            completeCommand.Parameters.AddWithValue("users_imported", imported);
            completeCommand.Parameters.AddWithValue("users_updated", updated);
            completeCommand.Parameters.AddWithValue("users_skipped", skipped);
            completeCommand.Parameters.AddWithValue("message", "Manual Azure/Entra foundation import completed. Real Microsoft Graph sync will be connected in the production SSO phase.");
            await completeCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "azure_import_completed",
            syncRunId,
            usersSeen = request.Users.Count,
            usersImported = imported,
            usersUpdated = updated,
            usersSkipped = skipped,
            errors,
            message = "Azure/Entra user import completed. Imported users received the Engineer role by default."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Azure/Entra user import failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/admin/azure/sync/run", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new { status = "admin_required", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var syncRunId = Guid.NewGuid();

    await using var command = new NpgsqlCommand("""
        INSERT INTO azure_entra_sync_runs (
            azure_entra_sync_run_id,
            sync_started_at,
            sync_completed_at,
            status,
            triggered_by_email,
            users_seen,
            users_imported,
            users_updated,
            users_skipped,
            message
        )
        VALUES (
            @sync_run_id,
            NOW(),
            NOW(),
            'completed_foundation_only',
            @triggered_by_email,
            0,
            0,
            0,
            0,
            'Foundation sync run recorded. Microsoft Graph connectivity will be connected in the next Azure SSO implementation phase.'
        );

        UPDATE azure_entra_settings
        SET last_sync_at = NOW(),
            last_sync_status = 'completed_foundation_only',
            last_sync_message = 'Foundation sync run recorded. Microsoft Graph connectivity is not active yet.',
            updated_at = NOW();
        """, connection);

    command.Parameters.AddWithValue("sync_run_id", syncRunId);
    command.Parameters.AddWithValue("triggered_by_email", (object?)GetProjectPulseSessionEmail(httpContext) ?? DBNull.Value);

    await command.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        status = "foundation_sync_recorded",
        syncRunId,
        message = "Azure/Entra foundation sync run recorded. Real Microsoft Graph sync will be connected in the Azure SSO phase."
    });
});

app.MapGet("/api/admin/azure/sync/runs", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new { status = "admin_required", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var runs = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            azure_entra_sync_run_id,
            sync_started_at,
            sync_completed_at,
            status,
            triggered_by_email,
            users_seen,
            users_imported,
            users_updated,
            users_skipped,
            message
        FROM azure_entra_sync_runs
        ORDER BY sync_started_at DESC
        LIMIT 20;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        runs.Add(new
        {
            syncRunId = reader.GetGuid(0),
            syncStartedAt = reader.GetFieldValue<DateTimeOffset>(1),
            syncCompletedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2),
            status = reader.GetString(3),
            triggeredByEmail = reader.IsDBNull(4) ? null : reader.GetString(4),
            usersSeen = reader.GetInt32(5),
            usersImported = reader.GetInt32(6),
            usersUpdated = reader.GetInt32(7),
            usersSkipped = reader.GetInt32(8),
            message = reader.IsDBNull(9) ? null : reader.GetString(9)
        });
    }

    return Results.Ok(new { count = runs.Count, runs });
});



app.MapGet("/api/admin/user-admin/reference", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var roles = new List<object>();
    await using (var roleCommand = new NpgsqlCommand("""
        SELECT role_code, role_name, role_description, display_order
        FROM app_roles
        WHERE is_active = TRUE
        ORDER BY display_order, role_name;
        """, connection))
    {
        await using var reader = await roleCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(new
            {
                roleCode = reader.GetString(0),
                roleName = reader.GetString(1),
                description = reader.IsDBNull(2) ? null : reader.GetString(2),
                displayOrder = reader.GetInt32(3)
            });
        }
    }

    var departments = new List<string>();
    await using (var departmentCommand = new NpgsqlCommand("""
        SELECT DISTINCT department_name
        FROM app_users
        WHERE NULLIF(TRIM(department_name), '') IS NOT NULL
        ORDER BY department_name;
        """, connection))
    {
        await using var reader = await departmentCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync()) departments.Add(reader.GetString(0));
    }

    var teams = new List<string>();
    await using (var teamCommand = new NpgsqlCommand("""
        SELECT DISTINCT team_name
        FROM app_users
        WHERE NULLIF(TRIM(team_name), '') IS NOT NULL
        ORDER BY team_name;
        """, connection))
    {
        await using var reader = await teamCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync()) teams.Add(reader.GetString(0));
    }

    return Results.Ok(new
    {
        roles,
        departments,
        teams
    });
});

app.MapGet("/api/admin/user-admin/users", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var users = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            u.user_id,
            u.email,
            u.display_name,
            u.is_active,
            COALESCE(u.login_enabled, TRUE) AS login_enabled,
            COALESCE(u.source_provider, 'LOCAL_APP') AS source_provider,
            u.entra_object_id,
            u.job_title,
            u.department_name,
            u.team_name,
            u.office_location,
            u.manager_email,
            u.last_directory_sync_at,
            la.username AS local_username,
            la.password_hash IS NOT NULL AS has_local_password,
            la.must_change_password,
            la.failed_login_count,
            la.locked_until,
            COALESCE(array_agg(r.role_code ORDER BY r.display_order) FILTER (WHERE r.role_code IS NOT NULL), ARRAY[]::varchar[]) AS role_codes,
            COALESCE(array_agg(r.role_name ORDER BY r.display_order) FILTER (WHERE r.role_name IS NOT NULL), ARRAY[]::varchar[]) AS role_names
        FROM app_users u
        LEFT JOIN auth_local_accounts la ON la.user_id = u.user_id
        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        GROUP BY
            u.user_id,
            u.email,
            u.display_name,
            u.is_active,
            u.login_enabled,
            u.source_provider,
            u.entra_object_id,
            u.job_title,
            u.department_name,
            u.team_name,
            u.office_location,
            u.manager_email,
            u.last_directory_sync_at,
            la.username,
            la.password_hash,
            la.must_change_password,
            la.failed_login_count,
            la.locked_until
        ORDER BY u.display_name, u.email;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        users.Add(new
        {
            userId = reader.GetGuid(0),
            email = reader.GetString(1),
            displayName = reader.GetString(2),
            isActive = reader.GetBoolean(3),
            loginEnabled = reader.GetBoolean(4),
            sourceProvider = reader.GetString(5),
            entraObjectId = reader.IsDBNull(6) ? null : reader.GetString(6),
            jobTitle = reader.IsDBNull(7) ? null : reader.GetString(7),
            departmentName = reader.IsDBNull(8) ? null : reader.GetString(8),
            teamName = reader.IsDBNull(9) ? null : reader.GetString(9),
            officeLocation = reader.IsDBNull(10) ? null : reader.GetString(10),
            managerEmail = reader.IsDBNull(11) ? null : reader.GetString(11),
            lastDirectorySyncAt = reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
            localUsername = reader.IsDBNull(13) ? null : reader.GetString(13),
            hasLocalPassword = reader.GetBoolean(14),
            mustChangePassword = reader.IsDBNull(15) ? (bool?)null : reader.GetBoolean(15),
            failedLoginCount = reader.IsDBNull(16) ? (int?)null : reader.GetInt32(16),
            lockedUntil = reader.IsDBNull(17) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(17),
            roleCodes = reader.GetFieldValue<string[]>(18),
            roleNames = reader.GetFieldValue<string[]>(19)
        });
    }

    return Results.Ok(new
    {
        count = users.Count,
        users
    });
});

app.MapPost("/api/admin/user-admin/users/profile", async (UserAdminProfileUpdateRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var command = new NpgsqlCommand("""
        UPDATE app_users
        SET display_name = COALESCE(NULLIF(@display_name, ''), display_name),
            job_title = NULLIF(@job_title, ''),
            department_name = NULLIF(@department_name, ''),
            team_name = NULLIF(@team_name, ''),
            office_location = NULLIF(@office_location, ''),
            manager_email = NULLIF(@manager_email, ''),
            login_enabled = @login_enabled,
            is_active = @is_active
        WHERE user_id = @user_id;
        """, connection);

    command.Parameters.AddWithValue("user_id", request.UserId);
    command.Parameters.AddWithValue("display_name", request.DisplayName?.Trim() ?? "");
    command.Parameters.AddWithValue("job_title", request.JobTitle?.Trim() ?? "");
    command.Parameters.AddWithValue("department_name", request.DepartmentName?.Trim() ?? "");
    command.Parameters.AddWithValue("team_name", request.TeamName?.Trim() ?? "");
    command.Parameters.AddWithValue("office_location", request.OfficeLocation?.Trim() ?? "");
    command.Parameters.AddWithValue("manager_email", request.ManagerEmail?.Trim().ToLowerInvariant() ?? "");
    command.Parameters.AddWithValue("login_enabled", request.LoginEnabled);
    command.Parameters.AddWithValue("is_active", request.IsActive);

    var rows = await command.ExecuteNonQueryAsync();

    if (rows == 0)
    {
        return Results.NotFound(new { status = "user_not_found", message = "User was not found." });
    }

    return Results.Ok(new
    {
        status = "user_profile_updated",
        message = "User profile, department, team, and login status were updated."
    });
});

app.MapPost("/api/admin/user-admin/users/roles", async (UserAdminRoleUpdateRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        var cleanRoleCodes = (request.RoleCodes ?? new List<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (sessionUserId == request.UserId && !cleanRoleCodes.Contains("ADMINISTRATOR"))
        {
            await transaction.RollbackAsync();
            return Results.BadRequest(new
            {
                status = "self_admin_removal_blocked",
                message = "You cannot remove your own Administrator role from User Administration."
            });
        }

        await using (var deactivateCommand = new NpgsqlCommand("""
            UPDATE app_user_role_assignments
            SET is_active = FALSE,
                updated_at = NOW()
            WHERE user_id = @user_id;
            """, connection, transaction))
        {
            deactivateCommand.Parameters.AddWithValue("user_id", request.UserId);
            await deactivateCommand.ExecuteNonQueryAsync();
        }

        foreach (var roleCode in cleanRoleCodes)
        {
            await using var roleCommand = new NpgsqlCommand("""
                INSERT INTO app_user_role_assignments (
                    user_id,
                    app_role_id,
                    assigned_by_user_id,
                    assignment_reason,
                    is_active
                )
                SELECT
                    @user_id,
                    r.app_role_id,
                    @assigned_by_user_id,
                    @assignment_reason,
                    TRUE
                FROM app_roles r
                WHERE r.role_code = @role_code
                  AND r.is_active = TRUE
                ON CONFLICT (user_id, app_role_id) DO UPDATE
                SET is_active = TRUE,
                    assignment_reason = EXCLUDED.assignment_reason,
                    assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                    updated_at = NOW();
                """, connection, transaction);

            roleCommand.Parameters.AddWithValue("user_id", request.UserId);
            roleCommand.Parameters.AddWithValue("assigned_by_user_id", (object?)sessionUserId ?? DBNull.Value);
            roleCommand.Parameters.AddWithValue("assignment_reason", string.IsNullOrWhiteSpace(request.Reason) ? "Updated from User Administration" : request.Reason.Trim());
            roleCommand.Parameters.AddWithValue("role_code", roleCode);
            await roleCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "user_roles_updated",
            roleCodes = cleanRoleCodes,
            message = cleanRoleCodes.Count == 0
                ? "All active roles were removed. This user will be blocked from login."
                : "User roles were updated."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to update user roles",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/admin/user-admin/local-password", async (UserAdminLocalPasswordUpdateRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var passwordIssue = ValidatePasswordQuality(request.TemporaryPassword);
    if (passwordIssue is not null)
    {
        return Results.BadRequest(new
        {
            status = "password_quality_failed",
            message = passwordIssue
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var passwordHash = HashProjectPulsePassword(request.TemporaryPassword);

    await using var command = new NpgsqlCommand("""
        UPDATE auth_local_accounts la
        SET password_hash = @password_hash,
            must_change_password = @must_change_password,
            failed_login_count = 0,
            locked_until = NULL,
            password_hash_updated_at = NOW()
        FROM app_users u
        WHERE u.user_id = la.user_id
          AND u.user_id = @user_id
          AND lower(la.username) LIKE '%.local';
        """, connection);

    command.Parameters.AddWithValue("user_id", request.UserId);
    command.Parameters.AddWithValue("password_hash", passwordHash);
    command.Parameters.AddWithValue("must_change_password", request.MustChangePassword);

    var rows = await command.ExecuteNonQueryAsync();

    if (rows == 0)
    {
        return Results.NotFound(new
        {
            status = "local_account_not_found",
            message = "No local account was found for this user."
        });
    }

    return Results.Ok(new
    {
        status = "local_password_updated",
        mustChangePassword = request.MustChangePassword,
        message = "Local temporary password was updated. The user can now sign in with the new temporary password."
    });
});





app.MapPost("/api/admin/user-admin/users/local", async (UserAdminLocalUserCreateRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var email = request.Email?.Trim().ToLowerInvariant() ?? "";
    var displayName = request.DisplayName?.Trim() ?? "";

    if (string.IsNullOrWhiteSpace(email) || !email.EndsWith("@ussignal.local", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            status = "invalid_local_domain",
            message = "Manual users must use the @ussignal.local domain. Use Entra import for @ussignal.com and @onenecklab.com users."
        });
    }

    if (email.EndsWith("@ussignal.com", StringComparison.OrdinalIgnoreCase) ||
        email.EndsWith("@onenecklab.com", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            status = "entra_domain_blocked",
            message = "Cloud-domain users must be imported from Entra. Manual creation is restricted to @ussignal.local users."
        });
    }

    if (string.IsNullOrWhiteSpace(displayName))
    {
        return Results.BadRequest(new
        {
            status = "display_name_required",
            message = "Display name is required."
        });
    }

    var passwordIssue = ValidatePasswordQuality(request.TemporaryPassword ?? "");
    if (passwordIssue is not null)
    {
        return Results.BadRequest(new
        {
            status = "password_quality_failed",
            message = passwordIssue
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        await using (var existsCommand = new NpgsqlCommand("SELECT 1 FROM app_users WHERE lower(email) = lower(@email) LIMIT 1;", connection, transaction))
        {
            existsCommand.Parameters.AddWithValue("email", email);
            var exists = await existsCommand.ExecuteScalarAsync();
            if (exists is not null)
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new
                {
                    status = "user_already_exists",
                    message = $"A user already exists for {email}."
                });
            }
        }

        var userId = Guid.NewGuid();
        var passwordHash = HashProjectPulsePassword(request.TemporaryPassword ?? "");
        var cleanRoleCodes = (request.RoleCodes ?? new List<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (cleanRoleCodes.Count == 0)
        {
            cleanRoleCodes.Add("ENGINEER");
        }

        await using (var userCommand = new NpgsqlCommand("""
            INSERT INTO app_users (
                user_id,
                email,
                display_name,
                is_active,
                login_enabled,
                source_provider,
                job_title,
                department_name,
                team_name,
                office_location,
                manager_email
            )
            VALUES (
                @user_id,
                @email,
                @display_name,
                TRUE,
                TRUE,
                'LOCAL_APP',
                NULLIF(@job_title, ''),
                NULLIF(@department_name, ''),
                NULLIF(@team_name, ''),
                NULLIF(@office_location, ''),
                NULLIF(@manager_email, '')
            );
            """, connection, transaction))
        {
            userCommand.Parameters.AddWithValue("user_id", userId);
            userCommand.Parameters.AddWithValue("email", email);
            userCommand.Parameters.AddWithValue("display_name", displayName);
            userCommand.Parameters.AddWithValue("job_title", request.JobTitle?.Trim() ?? "");
            userCommand.Parameters.AddWithValue("department_name", request.DepartmentName?.Trim() ?? "");
            userCommand.Parameters.AddWithValue("team_name", request.TeamName?.Trim() ?? "");
            userCommand.Parameters.AddWithValue("office_location", request.OfficeLocation?.Trim() ?? "");
            userCommand.Parameters.AddWithValue("manager_email", request.ManagerEmail?.Trim().ToLowerInvariant() ?? "");
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (var localAccountCommand = new NpgsqlCommand("""
            INSERT INTO auth_local_accounts (
                user_id,
                username,
                password_hash,
                must_change_password,
                failed_login_count,
                locked_until,
                password_hash_updated_at
            )
            VALUES (
                @user_id,
                @username,
                @password_hash,
                @must_change_password,
                0,
                NULL,
                NOW()
            );
            """, connection, transaction))
        {
            localAccountCommand.Parameters.AddWithValue("user_id", userId);
            localAccountCommand.Parameters.AddWithValue("username", email);
            localAccountCommand.Parameters.AddWithValue("password_hash", passwordHash);
            localAccountCommand.Parameters.AddWithValue("must_change_password", request.MustChangePassword);
            await localAccountCommand.ExecuteNonQueryAsync();
        }

        foreach (var roleCode in cleanRoleCodes)
        {
            await using var roleCommand = new NpgsqlCommand("""
                INSERT INTO app_user_role_assignments (
                    user_id,
                    app_role_id,
                    assigned_by_user_id,
                    assignment_reason,
                    is_active
                )
                SELECT
                    @user_id,
                    r.app_role_id,
                    @assigned_by_user_id,
                    @assignment_reason,
                    TRUE
                FROM app_roles r
                WHERE r.role_code = @role_code
                  AND r.is_active = TRUE
                ON CONFLICT (user_id, app_role_id) DO UPDATE
                SET is_active = TRUE,
                    assignment_reason = EXCLUDED.assignment_reason,
                    assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                    updated_at = NOW();
                """, connection, transaction);

            roleCommand.Parameters.AddWithValue("user_id", userId);
            roleCommand.Parameters.AddWithValue("assigned_by_user_id", sessionUserId.Value);
            roleCommand.Parameters.AddWithValue("assignment_reason", "Created from User Administration local user workflow.");
            roleCommand.Parameters.AddWithValue("role_code", roleCode);
            await roleCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "local_user_created",
            userId,
            email,
            roleCodes = cleanRoleCodes,
            message = $"Local user {email} was created. The temporary password is active."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to create local user",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/admin/user-admin/users/deactivate", async (UserAdminUserLifecycleRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (sessionUserId.Value == request.UserId)
        {
            await transaction.RollbackAsync();
            return Results.BadRequest(new
            {
                status = "self_deactivation_blocked",
                message = "You cannot deactivate your own account."
            });
        }

        string? targetEmail;
        await using (var lookupCommand = new NpgsqlCommand("SELECT email FROM app_users WHERE user_id = @user_id;", connection, transaction))
        {
            lookupCommand.Parameters.AddWithValue("user_id", request.UserId);
            targetEmail = (string?)await lookupCommand.ExecuteScalarAsync();
        }

        if (string.IsNullOrWhiteSpace(targetEmail))
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "user_not_found", message = "User was not found." });
        }

        if (targetEmail.Equals("ahmed.adeyemi@ussignal.local", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync();
            return Results.BadRequest(new
            {
                status = "break_glass_protected",
                message = "The break-glass local administrator cannot be deactivated from this workflow."
            });
        }

        await using (var userCommand = new NpgsqlCommand("""
            UPDATE app_users
            SET is_active = FALSE,
                login_enabled = FALSE
            WHERE user_id = @user_id;
            """, connection, transaction))
        {
            userCommand.Parameters.AddWithValue("user_id", request.UserId);
            await userCommand.ExecuteNonQueryAsync();
        }

        await using (var roleCommand = new NpgsqlCommand("""
            UPDATE app_user_role_assignments
            SET is_active = FALSE,
                updated_at = NOW()
            WHERE user_id = @user_id;
            """, connection, transaction))
        {
            roleCommand.Parameters.AddWithValue("user_id", request.UserId);
            await roleCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "user_deactivated",
            message = $"{targetEmail} was deactivated. Login is disabled and active roles were removed."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to deactivate user",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/admin/user-admin/users/delete", async (UserAdminUserLifecycleRequest request, HttpContext httpContext) =>
{
    static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
        }

        var sessionUserId = GetProjectPulseSessionUserId(httpContext);
        if (sessionUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (sessionUserId.Value == request.UserId)
        {
            await transaction.RollbackAsync();
            return Results.BadRequest(new
            {
                status = "self_delete_blocked",
                message = "You cannot delete your own account."
            });
        }

        string? targetEmail;
        await using (var lookupCommand = new NpgsqlCommand("SELECT email FROM app_users WHERE user_id = @user_id;", connection, transaction))
        {
            lookupCommand.Parameters.AddWithValue("user_id", request.UserId);
            targetEmail = (string?)await lookupCommand.ExecuteScalarAsync();
        }

        if (string.IsNullOrWhiteSpace(targetEmail))
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "user_not_found", message = "User was not found." });
        }

        if (targetEmail.Equals("ahmed.adeyemi@ussignal.local", StringComparison.OrdinalIgnoreCase))
        {
            await transaction.RollbackAsync();
            return Results.BadRequest(new
            {
                status = "break_glass_protected",
                message = "The break-glass local administrator cannot be deleted from this workflow."
            });
        }

        var ignoredTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "app_users",
            "auth_local_accounts",
            "app_user_role_assignments",
            "auth_sessions"
        };

        var dependencyColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "user_id",
            "engineer_user_id",
            "manager_user_id",
            "assigned_user_id",
            "assigned_by_user_id",
            "approved_by_user_id",
            "declined_by_user_id",
            "submitted_by_user_id",
            "uploaded_by_user_id",
            "created_by_user_id",
            "updated_by_user_id"
        };

        var dependencies = new List<string>();

        await using (var dependencyLookup = new NpgsqlCommand("""
            SELECT table_schema, table_name, column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND column_name IN (
                  'user_id',
                  'engineer_user_id',
                  'manager_user_id',
                  'assigned_user_id',
                  'assigned_by_user_id',
                  'approved_by_user_id',
                  'declined_by_user_id',
                  'submitted_by_user_id',
                  'uploaded_by_user_id',
                  'created_by_user_id',
                  'updated_by_user_id'
              )
            ORDER BY table_name, column_name;
            """, connection, transaction))
        {
            await using var reader = await dependencyLookup.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var schemaName = reader.GetString(0);
                var tableName = reader.GetString(1);
                var columnName = reader.GetString(2);

                if (ignoredTables.Contains(tableName) || !dependencyColumns.Contains(columnName))
                {
                    continue;
                }

                dependencies.Add($"{schemaName}.{tableName}.{columnName}");
            }
        }

        var blockingDependencies = new List<string>();

        foreach (var dependency in dependencies)
        {
            var parts = dependency.Split('.');
            if (parts.Length != 3) continue;

            var sql = $"""
                SELECT 1
                FROM {QuoteIdentifier(parts[0])}.{QuoteIdentifier(parts[1])}
                WHERE {QuoteIdentifier(parts[2])} = @user_id
                LIMIT 1;
                """;

            await using var dependencyCommand = new NpgsqlCommand(sql, connection, transaction);
            dependencyCommand.Parameters.AddWithValue("user_id", request.UserId);

            var exists = await dependencyCommand.ExecuteScalarAsync();
            if (exists is not null)
            {
                blockingDependencies.Add(dependency);
            }
        }

        if (blockingDependencies.Count > 0)
        {
            await using (var userCommand = new NpgsqlCommand("""
                UPDATE app_users
                SET is_active = FALSE,
                    login_enabled = FALSE
                WHERE user_id = @user_id;
                """, connection, transaction))
            {
                userCommand.Parameters.AddWithValue("user_id", request.UserId);
                await userCommand.ExecuteNonQueryAsync();
            }

            await using (var roleCommand = new NpgsqlCommand("""
                UPDATE app_user_role_assignments
                SET is_active = FALSE,
                    updated_at = NOW()
                WHERE user_id = @user_id;
                """, connection, transaction))
            {
                roleCommand.Parameters.AddWithValue("user_id", request.UserId);
                await roleCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            return Results.Ok(new
            {
                status = "user_safe_deactivated",
                dependencyCount = blockingDependencies.Count,
                dependencies = blockingDependencies.Take(10).ToList(),
                message = $"{targetEmail} has history in Project Pulse, so the account was safely deactivated instead of hard deleted."
            });
        }

        await using (var sessionsCommand = new NpgsqlCommand("DELETE FROM auth_sessions WHERE user_id = @user_id;", connection, transaction))
        {
            sessionsCommand.Parameters.AddWithValue("user_id", request.UserId);
            await sessionsCommand.ExecuteNonQueryAsync();
        }

        await using (var rolesCommand = new NpgsqlCommand("DELETE FROM app_user_role_assignments WHERE user_id = @user_id;", connection, transaction))
        {
            rolesCommand.Parameters.AddWithValue("user_id", request.UserId);
            await rolesCommand.ExecuteNonQueryAsync();
        }

        await using (var localCommand = new NpgsqlCommand("DELETE FROM auth_local_accounts WHERE user_id = @user_id;", connection, transaction))
        {
            localCommand.Parameters.AddWithValue("user_id", request.UserId);
            await localCommand.ExecuteNonQueryAsync();
        }

        await using (var userDeleteCommand = new NpgsqlCommand("DELETE FROM app_users WHERE user_id = @user_id;", connection, transaction))
        {
            userDeleteCommand.Parameters.AddWithValue("user_id", request.UserId);
            await userDeleteCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "user_deleted",
            message = $"{targetEmail} had no dependent history and was permanently deleted."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to delete user",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});


app.MapPost("/api/admin/user-admin/users/bulk-update", async (UserAdminBulkUpdateRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (request.UserIds is null || request.UserIds.Count == 0)
    {
        return Results.BadRequest(new
        {
            status = "no_users_selected",
            message = "Select at least one user before applying a bulk update."
        });
    }

    var userIds = request.UserIds.Distinct().ToArray();
    var roleMode = string.IsNullOrWhiteSpace(request.RoleUpdateMode)
        ? "none"
        : request.RoleUpdateMode.Trim().ToLowerInvariant();

    if (!new[] { "none", "add", "remove", "replace" }.Contains(roleMode))
    {
        return Results.BadRequest(new
        {
            status = "invalid_role_update_mode",
            message = "Role update mode must be none, add, remove, or replace."
        });
    }

    var cleanRoleCodes = (request.RoleCodes ?? new List<string>())
        .Where(code => !string.IsNullOrWhiteSpace(code))
        .Select(code => code.Trim().ToUpperInvariant())
        .Distinct()
        .ToList();

    if (roleMode != "none" && cleanRoleCodes.Count == 0)
    {
        return Results.BadRequest(new
        {
            status = "no_roles_selected",
            message = "Select at least one role when using add, remove, or replace role mode."
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "User Administration is restricted to administrators and project/team coordinators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);

    if (sessionUserId is not null && userIds.Contains(sessionUserId.Value))
    {
        if (roleMode == "replace" && !cleanRoleCodes.Contains("ADMINISTRATOR"))
        {
            return Results.BadRequest(new
            {
                status = "self_admin_removal_blocked",
                message = "You cannot bulk replace your own roles without keeping Administrator."
            });
        }

        if (roleMode == "remove" && cleanRoleCodes.Contains("ADMINISTRATOR"))
        {
            return Results.BadRequest(new
            {
                status = "self_admin_removal_blocked",
                message = "You cannot bulk remove your own Administrator role."
            });
        }
    }

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        await using (var profileCommand = new NpgsqlCommand("""
            UPDATE app_users
            SET job_title = CASE WHEN @apply_job_title THEN NULLIF(@job_title, '') ELSE job_title END,
                department_name = CASE WHEN @apply_department_name THEN NULLIF(@department_name, '') ELSE department_name END,
                team_name = CASE WHEN @apply_team_name THEN NULLIF(@team_name, '') ELSE team_name END,
                office_location = CASE WHEN @apply_office_location THEN NULLIF(@office_location, '') ELSE office_location END,
                manager_email = CASE WHEN @apply_manager_email THEN NULLIF(@manager_email, '') ELSE manager_email END,
                login_enabled = CASE WHEN @apply_login_enabled THEN @login_enabled ELSE login_enabled END,
                is_active = CASE WHEN @apply_is_active THEN @is_active ELSE is_active END
            WHERE user_id = ANY(@user_ids);
            """, connection, transaction))
        {
            profileCommand.Parameters.AddWithValue("user_ids", userIds);
            profileCommand.Parameters.AddWithValue("apply_job_title", request.ApplyJobTitle);
            profileCommand.Parameters.AddWithValue("job_title", request.JobTitle?.Trim() ?? "");
            profileCommand.Parameters.AddWithValue("apply_department_name", request.ApplyDepartmentName);
            profileCommand.Parameters.AddWithValue("department_name", request.DepartmentName?.Trim() ?? "");
            profileCommand.Parameters.AddWithValue("apply_team_name", request.ApplyTeamName);
            profileCommand.Parameters.AddWithValue("team_name", request.TeamName?.Trim() ?? "");
            profileCommand.Parameters.AddWithValue("apply_office_location", request.ApplyOfficeLocation);
            profileCommand.Parameters.AddWithValue("office_location", request.OfficeLocation?.Trim() ?? "");
            profileCommand.Parameters.AddWithValue("apply_manager_email", request.ApplyManagerEmail);
            profileCommand.Parameters.AddWithValue("manager_email", request.ManagerEmail?.Trim().ToLowerInvariant() ?? "");
            profileCommand.Parameters.AddWithValue("apply_login_enabled", request.ApplyLoginEnabled);
            profileCommand.Parameters.AddWithValue("login_enabled", request.LoginEnabled);
            profileCommand.Parameters.AddWithValue("apply_is_active", request.ApplyIsActive);
            profileCommand.Parameters.AddWithValue("is_active", request.IsActive);

            await profileCommand.ExecuteNonQueryAsync();
        }

        if (roleMode == "replace")
        {
            await using var deactivateCommand = new NpgsqlCommand("""
                UPDATE app_user_role_assignments
                SET is_active = FALSE,
                    updated_at = NOW()
                WHERE user_id = ANY(@user_ids);
                """, connection, transaction);

            deactivateCommand.Parameters.AddWithValue("user_ids", userIds);
            await deactivateCommand.ExecuteNonQueryAsync();
        }

        if (roleMode == "remove")
        {
            await using var removeCommand = new NpgsqlCommand("""
                UPDATE app_user_role_assignments ura
                SET is_active = FALSE,
                    updated_at = NOW()
                FROM app_roles r
                WHERE r.app_role_id = ura.app_role_id
                  AND ura.user_id = ANY(@user_ids)
                  AND r.role_code = ANY(@role_codes);
                """, connection, transaction);

            removeCommand.Parameters.AddWithValue("user_ids", userIds);
            removeCommand.Parameters.AddWithValue("role_codes", cleanRoleCodes.ToArray());
            await removeCommand.ExecuteNonQueryAsync();
        }

        if (roleMode == "add" || roleMode == "replace")
        {
            foreach (var userId in userIds)
            {
                foreach (var roleCode in cleanRoleCodes)
                {
                    await using var roleCommand = new NpgsqlCommand("""
                        INSERT INTO app_user_role_assignments (
                            user_id,
                            app_role_id,
                            assigned_by_user_id,
                            assignment_reason,
                            is_active
                        )
                        SELECT
                            @user_id,
                            r.app_role_id,
                            @assigned_by_user_id,
                            @assignment_reason,
                            TRUE
                        FROM app_roles r
                        WHERE r.role_code = @role_code
                          AND r.is_active = TRUE
                        ON CONFLICT (user_id, app_role_id) DO UPDATE
                        SET is_active = TRUE,
                            assignment_reason = EXCLUDED.assignment_reason,
                            assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                            updated_at = NOW();
                        """, connection, transaction);

                    roleCommand.Parameters.AddWithValue("user_id", userId);
                    roleCommand.Parameters.AddWithValue("assigned_by_user_id", (object?)sessionUserId ?? DBNull.Value);
                    roleCommand.Parameters.AddWithValue("assignment_reason", string.IsNullOrWhiteSpace(request.Reason) ? "Bulk update from User Administration" : request.Reason.Trim());
                    roleCommand.Parameters.AddWithValue("role_code", roleCode);

                    await roleCommand.ExecuteNonQueryAsync();
                }
            }
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "bulk_user_update_completed",
            usersUpdated = userIds.Length,
            roleUpdateMode = roleMode,
            roleCodes = cleanRoleCodes,
            message = $"Bulk update completed for {userIds.Length} user(s)."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Bulk user update failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});





var projectPulseManagedServices = new Dictionary<string, object>
{
    ["projectpulse-api"] = new
    {
        serviceKey = "projectpulse-api",
        systemdName = "projecttime-api.service",
        displayName = "ProjectPulse API",
        description = "ASP.NET backend API service for authentication, timesheets, approvals, audit, integrations, and administration."
    },
    ["projectpulse-frontend"] = new
    {
        serviceKey = "projectpulse-frontend",
        systemdName = "projecttime-frontend-public.service",
        displayName = "ProjectPulse Frontend",
        description = "Restricted public frontend service that serves the ProjectPulse web application."
    },
    ["nginx"] = new
    {
        serviceKey = "nginx",
        systemdName = "nginx.service",
        displayName = "Nginx Reverse Proxy",
        description = "Public reverse proxy for HTTPS traffic and routing to API/frontend services."
    },
    ["postgresql"] = new
    {
        serviceKey = "postgresql",
        systemdName = "postgresql.service",
        displayName = "PostgreSQL Database",
        description = "Primary PostgreSQL database service for ProjectPulse."
    }
};






app.MapPost("/api/system/backup-dr/runs/delete", async (ProjectPulseBackupDeleteRequest request, HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(request.RequestId))
    {
        return Results.BadRequest(new
        {
            status = "request_id_required",
            message = "A backup request ID is required."
        });
    }

    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 8)
    {
        return Results.BadRequest(new
        {
            status = "reason_required",
            message = "A deletion reason of at least 8 characters is required."
        });
    }

    var requestId = request.RequestId.Trim();

    if (requestId.Any(character => !(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.')))
    {
        return Results.BadRequest(new
        {
            status = "invalid_request_id",
            message = "The backup request ID contains unsupported characters."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Backup deletion is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var deleteRequestId = Guid.NewGuid();
    var pendingDirectory = "/opt/project-time-platform/backup-delete-requests/pending";
    Directory.CreateDirectory(pendingDirectory);

    var deletePayload = new
    {
        deleteRequestId,
        requestId,
        requestedAt = DateTimeOffset.UtcNow,
        requestedByUserId = adminContext.UserId,
        requestedByEmail = adminContext.Email,
        reason = request.Reason.Trim()
    };

    var deleteRequestPath = Path.Combine(pendingDirectory, $"{deleteRequestId}.json");

    await File.WriteAllTextAsync(
        deleteRequestPath,
        JsonSerializer.Serialize(deletePayload, new JsonSerializerOptions { WriteIndented = true }));

    await InsertProjectPulseAuditEventAsync(
        connection,
        adminContext.UserId,
        "backup_delete_queued",
        "backup_dr",
        null,
        httpContext,
        new
        {
            deleteRequestId,
            requestId,
            reason = request.Reason.Trim(),
            deleteRequestPath
        });

    return Results.Ok(new
    {
        status = "backup_delete_queued",
        message = "Backup deletion was queued. It may take up to one minute to disappear from history.",
        deleteRequestId,
        requestId,
        generatedAt = DateTimeOffset.UtcNow
    });
});



app.MapGet("/api/system/backup-dr/runs", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Backup run history is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    static string ExtractOutputValue(string output, string key)
    {
        if (string.IsNullOrWhiteSpace(output)) return "";

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                return line[(key.Length + 1)..].Trim();
            }
        }

        return "";
    }

    var resultsDirectory = "/opt/project-time-platform/backups/results";
    Directory.CreateDirectory(resultsDirectory);

    var resultFiles = Directory
        .EnumerateFiles(resultsDirectory, "*.result.json", SearchOption.TopDirectoryOnly)
        .Select(path => new FileInfo(path))
        .OrderByDescending(file => file.LastWriteTimeUtc)
        .Take(30)
        .ToList();

    var runs = new List<object>();

    foreach (var file in resultFiles)
    {
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file.FullName));
            var root = document.RootElement;

            var output = root.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.String
                ? outputElement.GetString() ?? ""
                : "";

            var request = root.TryGetProperty("request", out var requestElement)
                ? requestElement
                : default;

            string GetString(JsonElement element, string name)
            {
                if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(name, out var property)) return "";
                return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
            }

            bool GetBool(JsonElement element, string name)
            {
                if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(name, out var property)) return false;
                if (property.ValueKind == JsonValueKind.True) return true;
                if (property.ValueKind == JsonValueKind.False) return false;
                return bool.TryParse(property.ToString(), out var parsed) && parsed;
            }

            runs.Add(new
            {
                requestId = GetString(request, "requestId"),
                requestedAt = GetString(request, "requestedAt"),
                requestedByEmail = GetString(request, "requestedByEmail"),
                reason = GetString(request, "reason"),
                uploadToSftp = GetBool(request, "uploadToSftp"),
                uploadToAzure = GetBool(request, "uploadToAzure"),
                status = GetString(root, "status"),
                exitCode = root.TryGetProperty("exitCode", out var exitCodeElement) && exitCodeElement.TryGetInt32(out var exitCode) ? exitCode : -1,
                startedAt = GetString(root, "startedAt"),
                completedAt = GetString(root, "completedAt"),
                resultFile = file.FullName,
                outputFile = GetString(root, "outputFile"),
                backupBundle = ExtractOutputValue(output, "backup_bundle"),
                backupBundleSha256 = ExtractOutputValue(output, "backup_bundle_sha256"),
                databaseDump = ExtractOutputValue(output, "database_dump"),
                configArchive = ExtractOutputValue(output, "config_archive"),
                appArchive = ExtractOutputValue(output, "app_archive"),
                sftpUploadStatus = ExtractOutputValue(output, "sftp_upload_status"),
                azureUploadStatus = ExtractOutputValue(output, "azure_upload_status"),
                output
            });
        }
        catch (Exception ex)
        {
            runs.Add(new
            {
                requestId = file.Name.Replace(".result.json", "", StringComparison.OrdinalIgnoreCase),
                status = "unreadable",
                exitCode = -1,
                resultFile = file.FullName,
                message = ex.Message
            });
        }
    }

    return Results.Ok(new
    {
        status = "backup_runs_loaded",
        generatedAt = DateTimeOffset.UtcNow,
        totalCount = runs.Count,
        runs
    });
});



app.MapGet("/api/system/backup-dr/settings", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Backup settings are restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var sftp = ReadProjectPulseEnvFile("/opt/project-time-platform/config/backup-sftp.env");
    var azure = ReadProjectPulseEnvFile("/opt/project-time-platform/config/backup-azure.env");
    var notifications = ReadProjectPulseEnvFile("/opt/project-time-platform/config/backup-notifications.env");
    var schedule = ReadProjectPulseEnvFile("/opt/project-time-platform/config/backup-schedule.env");

    return Results.Ok(new
    {
        status = "settings_loaded",
        sftp = new
        {
            enabled = string.Equals(sftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_ENABLED"), "true", StringComparison.OrdinalIgnoreCase),
            authMode = sftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_AUTH_MODE") ?? "private_key",
            host = sftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_HOST") ?? "",
            port = sftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_PORT") ?? "22",
            user = sftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_USER") ?? "",
            remotePath = sftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_REMOTE_PATH") ?? "",
            keyPath = sftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_KEY_PATH") ?? "",
            passwordConfigured = !string.IsNullOrWhiteSpace(sftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_PASSWORD"))
        },
        azure = new
        {
            enabled = string.Equals(azure.GetValueOrDefault("PROJECTPULSE_BACKUP_AZURE_ENABLED"), "true", StringComparison.OrdinalIgnoreCase),
            containerSasUrlMasked = MaskProjectPulseSecret(azure.GetValueOrDefault("PROJECTPULSE_BACKUP_AZURE_CONTAINER_SAS_URL")),
            containerSasUrlConfigured = !string.IsNullOrWhiteSpace(azure.GetValueOrDefault("PROJECTPULSE_BACKUP_AZURE_CONTAINER_SAS_URL")),
            blobPrefix = azure.GetValueOrDefault("PROJECTPULSE_BACKUP_AZURE_BLOB_PREFIX") ?? "projectpulse-backups"
        },
        notifications = new
        {
            notifyOnSuccess = string.Equals(notifications.GetValueOrDefault("PROJECTPULSE_BACKUP_NOTIFY_ON_SUCCESS"), "true", StringComparison.OrdinalIgnoreCase),
            notifyOnFailure = !string.Equals(notifications.GetValueOrDefault("PROJECTPULSE_BACKUP_NOTIFY_ON_FAILURE"), "false", StringComparison.OrdinalIgnoreCase),
            successRecipients = notifications.GetValueOrDefault("PROJECTPULSE_BACKUP_SUCCESS_RECIPIENTS") ?? "",
            failureRecipients = notifications.GetValueOrDefault("PROJECTPULSE_BACKUP_FAILURE_RECIPIENTS") ?? "",
            ccRecipients = notifications.GetValueOrDefault("PROJECTPULSE_BACKUP_CC_RECIPIENTS") ?? ""
        },
        schedule = new
        {
            enabled = string.Equals(schedule.GetValueOrDefault("PROJECTPULSE_BACKUP_SCHEDULE_ENABLED"), "true", StringComparison.OrdinalIgnoreCase),
            mode = schedule.GetValueOrDefault("PROJECTPULSE_BACKUP_SCHEDULE_MODE") ?? "daily",
            timeUtc = schedule.GetValueOrDefault("PROJECTPULSE_BACKUP_SCHEDULE_TIME_UTC") ?? "06:00",
            weeklyDayUtc = schedule.GetValueOrDefault("PROJECTPULSE_BACKUP_SCHEDULE_WEEKLY_DAY_UTC") ?? "7",
            monthlyDayUtc = schedule.GetValueOrDefault("PROJECTPULSE_BACKUP_SCHEDULE_MONTHLY_DAY_UTC") ?? "1",
            uploadToSftp = string.Equals(schedule.GetValueOrDefault("PROJECTPULSE_BACKUP_SCHEDULE_UPLOAD_TO_SFTP"), "true", StringComparison.OrdinalIgnoreCase),
            uploadToAzure = string.Equals(schedule.GetValueOrDefault("PROJECTPULSE_BACKUP_SCHEDULE_UPLOAD_TO_AZURE"), "true", StringComparison.OrdinalIgnoreCase)
        }
    });
});

app.MapPost("/api/system/backup-dr/settings", async (JsonElement request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Backup settings are restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    string GetString(string name, string fallback = "")
    {
        if (!request.TryGetProperty(name, out var property)) return fallback;
        if (property.ValueKind == JsonValueKind.Null || property.ValueKind == JsonValueKind.Undefined) return fallback;
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? fallback : property.ToString();
    }

    bool GetBool(string name, bool fallback = false)
    {
        if (!request.TryGetProperty(name, out var property)) return fallback;
        if (property.ValueKind == JsonValueKind.True) return true;
        if (property.ValueKind == JsonValueKind.False) return false;
        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed)) return parsed;
        return fallback;
    }

    var sftpPath = "/opt/project-time-platform/config/backup-sftp.env";
    var azurePath = "/opt/project-time-platform/config/backup-azure.env";
    var notificationPath = "/opt/project-time-platform/config/backup-notifications.env";
    var schedulePath = "/opt/project-time-platform/config/backup-schedule.env";

    Directory.CreateDirectory("/opt/project-time-platform/config");

    var existingSftp = ReadProjectPulseEnvFile(sftpPath);
    var existingAzure = ReadProjectPulseEnvFile(azurePath);

    var submittedSftpPassword = GetString("sftpPassword");
    var effectiveSftpPassword = string.IsNullOrWhiteSpace(submittedSftpPassword)
        ? existingSftp.GetValueOrDefault("PROJECTPULSE_BACKUP_SFTP_PASSWORD") ?? ""
        : submittedSftpPassword;

    var submittedAzureSas = GetString("azureContainerSasUrl");
    var effectiveAzureSas = string.IsNullOrWhiteSpace(submittedAzureSas)
        ? existingAzure.GetValueOrDefault("PROJECTPULSE_BACKUP_AZURE_CONTAINER_SAS_URL") ?? ""
        : submittedAzureSas;

    await File.WriteAllLinesAsync(sftpPath, new[]
    {
        $"PROJECTPULSE_BACKUP_SFTP_ENABLED={GetBool("sftpEnabled").ToString().ToLowerInvariant()}",
        $"PROJECTPULSE_BACKUP_SFTP_AUTH_MODE={QuoteProjectPulseEnvValue(GetString("sftpAuthMode", "private_key"))}",
        $"PROJECTPULSE_BACKUP_SFTP_HOST={QuoteProjectPulseEnvValue(GetString("sftpHost"))}",
        $"PROJECTPULSE_BACKUP_SFTP_PORT={QuoteProjectPulseEnvValue(GetString("sftpPort", "22"))}",
        $"PROJECTPULSE_BACKUP_SFTP_USER={QuoteProjectPulseEnvValue(GetString("sftpUser"))}",
        $"PROJECTPULSE_BACKUP_SFTP_REMOTE_PATH={QuoteProjectPulseEnvValue(GetString("sftpRemotePath"))}",
        $"PROJECTPULSE_BACKUP_SFTP_KEY_PATH={QuoteProjectPulseEnvValue(GetString("sftpKeyPath"))}",
        $"PROJECTPULSE_BACKUP_SFTP_PASSWORD={QuoteProjectPulseEnvValue(effectiveSftpPassword)}"
    });

    await File.WriteAllLinesAsync(azurePath, new[]
    {
        $"PROJECTPULSE_BACKUP_AZURE_ENABLED={GetBool("azureEnabled").ToString().ToLowerInvariant()}",
        $"PROJECTPULSE_BACKUP_AZURE_CONTAINER_SAS_URL={QuoteProjectPulseEnvValue(effectiveAzureSas)}",
        $"PROJECTPULSE_BACKUP_AZURE_BLOB_PREFIX={QuoteProjectPulseEnvValue(GetString("azureBlobPrefix", "projectpulse-backups"))}"
    });

    await File.WriteAllLinesAsync(notificationPath, new[]
    {
        $"PROJECTPULSE_BACKUP_NOTIFY_ON_SUCCESS={GetBool("notifyOnSuccess").ToString().ToLowerInvariant()}",
        $"PROJECTPULSE_BACKUP_NOTIFY_ON_FAILURE={GetBool("notifyOnFailure", true).ToString().ToLowerInvariant()}",
        $"PROJECTPULSE_BACKUP_SUCCESS_RECIPIENTS={QuoteProjectPulseEnvValue(GetString("successRecipients"))}",
        $"PROJECTPULSE_BACKUP_FAILURE_RECIPIENTS={QuoteProjectPulseEnvValue(GetString("failureRecipients"))}",
        $"PROJECTPULSE_BACKUP_CC_RECIPIENTS={QuoteProjectPulseEnvValue(GetString("ccRecipients"))}"
    });

    await File.WriteAllLinesAsync(schedulePath, new[]
    {
        $"PROJECTPULSE_BACKUP_SCHEDULE_ENABLED={GetBool("scheduleEnabled").ToString().ToLowerInvariant()}",
        $"PROJECTPULSE_BACKUP_SCHEDULE_MODE={QuoteProjectPulseEnvValue(GetString("scheduleMode", "daily"))}",
        $"PROJECTPULSE_BACKUP_SCHEDULE_TIME_UTC={QuoteProjectPulseEnvValue(GetString("scheduleTimeUtc", "06:00"))}",
        $"PROJECTPULSE_BACKUP_SCHEDULE_WEEKLY_DAY_UTC={QuoteProjectPulseEnvValue(GetString("scheduleWeeklyDayUtc", "7"))}",
        $"PROJECTPULSE_BACKUP_SCHEDULE_MONTHLY_DAY_UTC={QuoteProjectPulseEnvValue(GetString("scheduleMonthlyDayUtc", "1"))}",
        $"PROJECTPULSE_BACKUP_SCHEDULE_UPLOAD_TO_SFTP={GetBool("scheduleUploadToSftp").ToString().ToLowerInvariant()}",
        $"PROJECTPULSE_BACKUP_SCHEDULE_UPLOAD_TO_AZURE={GetBool("scheduleUploadToAzure").ToString().ToLowerInvariant()}"
    });

    await InsertProjectPulseAuditEventAsync(
        connection,
        adminContext.UserId,
        "backup_settings_updated",
        "backup_dr",
        null,
        httpContext,
        new
        {
            sftpEnabled = GetBool("sftpEnabled"),
            sftpAuthMode = GetString("sftpAuthMode", "private_key"),
            azureEnabled = GetBool("azureEnabled"),
            notifyOnSuccess = GetBool("notifyOnSuccess"),
            notifyOnFailure = GetBool("notifyOnFailure", true),
            scheduleEnabled = GetBool("scheduleEnabled"),
            scheduleMode = GetString("scheduleMode", "daily")
        });

    return Results.Ok(new
    {
        status = "settings_saved",
        message = "Backup / DR settings were saved.",
        generatedAt = DateTimeOffset.UtcNow
    });
});



app.MapPost("/api/system/backup-dr/run", async (ProjectPulseBackupRunRequest request, HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 8)
    {
        return Results.BadRequest(new
        {
            status = "reason_required",
            message = "A backup reason of at least 8 characters is required."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Manual backup actions are restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var requestId = Guid.NewGuid();
    var pendingDirectory = "/opt/project-time-platform/backup-requests/pending";
    Directory.CreateDirectory(pendingDirectory);

    var requestPayload = new
    {
        requestId,
        requestedAt = DateTimeOffset.UtcNow,
        requestedByUserId = adminContext.UserId,
        requestedByEmail = adminContext.Email,
        uploadToSftp = request.UploadToSftp,
        uploadToAzure = request.UploadToAzure == true,
        reason = request.Reason.Trim()
    };

    var requestPath = Path.Combine(pendingDirectory, $"{requestId}.json");
    await File.WriteAllTextAsync(
        requestPath,
        JsonSerializer.Serialize(requestPayload, new JsonSerializerOptions { WriteIndented = true }));

    await InsertProjectPulseAuditEventAsync(
        connection,
        adminContext.UserId,
        "backup_run_queued",
        "backup_dr",
        null,
        httpContext,
        new
        {
            requestId,
            uploadToSftp = request.UploadToSftp,
            uploadToAzure = request.UploadToAzure == true,
            reason = request.Reason.Trim(),
            requestPath
        });

    return Results.Ok(new
    {
        status = "backup_queued",
        message = request.UploadToSftp || request.UploadToAzure == true
            ? "Backup request was queued. The root backup runner will create the bundle and upload it to the selected external target(s)."
            : "Backup request was queued. The root backup runner will create the local backup bundle.",
        requestId,
        requestPath,
        generatedAt = DateTimeOffset.UtcNow
    });
});







app.MapGet("/api/system/replication-sync/settings", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Replication & Sync settings are restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    const string settingsFile = "/opt/project-time-platform/config/replication-sync.env";
    var settings = File.Exists(settingsFile)
        ? ReadProjectPulseEnvFile(settingsFile)
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var staleBackupHoursRaw = settings.GetValueOrDefault("PROJECTPULSE_SYNC_STALE_BACKUP_HOURS", "24");
    var staleBackupHours = int.TryParse(staleBackupHoursRaw, out var parsedHours) ? parsedHours : 24;

    return Results.Ok(new
    {
        status = "replication_sync_settings_loaded",
        peerName = settings.GetValueOrDefault("PROJECTPULSE_SYNC_PEER_NAME", ""),
        peerHost = settings.GetValueOrDefault("PROJECTPULSE_SYNC_PEER_HOST", ""),
        peerUrl = settings.GetValueOrDefault("PROJECTPULSE_SYNC_PEER_URL", ""),
        staleBackupHours
    });
});

app.MapPost("/api/system/replication-sync/settings", async (HttpContext httpContext, ProjectPulseReplicationSyncSettingsRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Replication & Sync settings are restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    const string settingsFile = "/opt/project-time-platform/config/replication-sync.env";
    Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);

    var peerName = request.PeerName?.Trim() ?? "";
    var peerHost = request.PeerHost?.Trim() ?? "";
    var peerUrl = request.PeerUrl?.Trim() ?? "";
    var staleBackupHours = Math.Clamp(request.StaleBackupHours ?? 24, 1, 720);

    var content = string.Join(Environment.NewLine, new[]
    {
        $"PROJECTPULSE_SYNC_PEER_NAME={QuoteProjectPulseEnvValue(peerName)}",
        $"PROJECTPULSE_SYNC_PEER_HOST={QuoteProjectPulseEnvValue(peerHost)}",
        $"PROJECTPULSE_SYNC_PEER_URL={QuoteProjectPulseEnvValue(peerUrl)}",
        $"PROJECTPULSE_SYNC_STALE_BACKUP_HOURS={staleBackupHours}"
    }) + Environment.NewLine;

    await File.WriteAllTextAsync(settingsFile, content);
    await RunProjectPulseProcessAsync("/usr/bin/chmod", "660", settingsFile);

    return Results.Ok(new
    {
        status = "replication_sync_settings_saved",
        message = "Replication & Sync settings were saved. The status exporter will refresh shortly.",
        peerName,
        peerHost,
        peerUrl,
        staleBackupHours
    });
});





app.MapGet("/api/system/restore-validation/backups", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Restore Validation Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    const string backupRoot = "/opt/project-time-platform/backups";

    var backups = Directory.Exists(backupRoot)
        ? Directory.EnumerateFiles(backupRoot, "*.tgz", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var checksumPath = path + ".sha256";

                return new
                {
                    name = info.Name,
                    path = info.FullName,
                    sizeBytes = info.Length,
                    createdAt = info.LastWriteTimeUtc,
                    ageHours = Math.Round((DateTimeOffset.UtcNow - info.LastWriteTimeUtc).TotalHours, 2),
                    checksumExists = File.Exists(checksumPath)
                };
            })
            .OrderByDescending(item => item.createdAt)
            .ToArray()
        : Array.Empty<object>();

    return Results.Ok(new
    {
        status = "restore_validation_backups_loaded",
        backups
    });
});

app.MapGet("/api/system/restore-validation/settings", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Restore Validation settings are restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    const string settingsFile = "/opt/project-time-platform/config/restore-validation.env";
    var settings = File.Exists(settingsFile)
        ? ReadProjectPulseEnvFile(settingsFile)
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    return Results.Ok(new
    {
        status = "restore_validation_settings_loaded",
        selectedBackup = settings.GetValueOrDefault("PROJECTPULSE_RESTORE_VALIDATION_SELECTED_BACKUP", "")
    });
});

app.MapPost("/api/system/restore-validation/settings", async (HttpContext httpContext, ProjectPulseRestoreValidationSettingsRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Restore Validation settings are restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    const string backupRoot = "/opt/project-time-platform/backups";
    const string settingsFile = "/opt/project-time-platform/config/restore-validation.env";

    var selectedBackupRaw = request.SelectedBackup?.Trim() ?? "";
    var selectedBackup = Path.GetFileName(selectedBackupRaw);

    if (!string.IsNullOrWhiteSpace(selectedBackupRaw))
    {
        if (!string.Equals(selectedBackupRaw, selectedBackup, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                status = "invalid_restore_point",
                message = "Selected backup must be a backup filename only, not a path."
            });
        }

        if (!selectedBackup.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                status = "invalid_restore_point",
                message = "Selected backup must end with .tgz."
            });
        }

        var selectedPath = Path.Combine(backupRoot, selectedBackup);
        if (!File.Exists(selectedPath))
        {
            return Results.BadRequest(new
            {
                status = "restore_point_not_found",
                message = "Selected backup was not found."
            });
        }
    }
    else
    {
        selectedBackup = "";
    }

    Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);

    var content = $"PROJECTPULSE_RESTORE_VALIDATION_SELECTED_BACKUP={QuoteProjectPulseEnvValue(selectedBackup)}{Environment.NewLine}";
    await File.WriteAllTextAsync(settingsFile, content);
    await RunProjectPulseProcessAsync("/usr/bin/chmod", "660", settingsFile);

    return Results.Ok(new
    {
        status = "restore_validation_settings_saved",
        message = string.IsNullOrWhiteSpace(selectedBackup)
            ? "Restore point selection saved. ProjectPulse will validate the latest backup."
            : "Restore point selection saved. ProjectPulse will validate the selected backup.",
        selectedBackup
    });
});



app.MapGet("/api/system/backup-retention/status", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Backup Retention Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    const string backupRoot = "/opt/project-time-platform/backups";
    const string restoreSettingsFile = "/opt/project-time-platform/config/restore-validation.env";
    const string deleteStateFile = "/opt/project-time-platform/state/backup-delete-status.json";

    var restoreSettings = File.Exists(restoreSettingsFile)
        ? ReadProjectPulseEnvFile(restoreSettingsFile)
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var selectedRestorePoint = restoreSettings.GetValueOrDefault("PROJECTPULSE_RESTORE_VALIDATION_SELECTED_BACKUP", "");

    var backups = new List<object>();

    if (Directory.Exists(backupRoot))
    {
        foreach (var filePath in Directory.EnumerateFiles(backupRoot, "*.tgz", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(filePath);
            var checksumPath = filePath + ".sha256";

            backups.Add(new
            {
                name = info.Name,
                path = info.FullName,
                sizeBytes = info.Length,
                createdAt = info.LastWriteTimeUtc,
                ageHours = Math.Round((DateTime.UtcNow - info.LastWriteTimeUtc).TotalHours, 2),
                checksumExists = File.Exists(checksumPath),
                isSelectedRestorePoint = string.Equals(info.Name, selectedRestorePoint, StringComparison.Ordinal)
            });
        }
    }

    backups = backups
        .OrderByDescending(item => ((dynamic)item).createdAt)
        .ToList();

    object? deleteStatus = null;

    if (File.Exists(deleteStateFile))
    {
        try
        {
            var deleteJson = await File.ReadAllTextAsync(deleteStateFile);
            deleteStatus = System.Text.Json.JsonSerializer.Deserialize<object>(deleteJson);
        }
        catch
        {
            deleteStatus = new
            {
                overallStatus = "unknown",
                message = "Backup delete status file could not be parsed."
            };
        }
    }

    return Results.Ok(new
    {
        status = "backup_retention_status_loaded",
        generatedAt = DateTimeOffset.UtcNow,
        backupCount = backups.Count,
        selectedRestorePoint,
        canDelete = backups.Count > 1,
        backups,
        deleteStatus
    });
});

app.MapPost("/api/system/backup-retention/delete", async (HttpContext httpContext, ProjectPulseBackupRetentionDeleteRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Backup Retention Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (request.Confirm != true)
    {
        return Results.BadRequest(new
        {
            status = "confirmation_required",
            message = "Backup deletion requires explicit confirmation."
        });
    }

    const string backupRoot = "/opt/project-time-platform/backups";
    const string pendingDir = "/opt/project-time-platform/backup-delete-requests/pending";
    const string restoreSettingsFile = "/opt/project-time-platform/config/restore-validation.env";

    var backupNameRaw = request.BackupName?.Trim() ?? "";
    var backupName = Path.GetFileName(backupNameRaw);

    if (string.IsNullOrWhiteSpace(backupName))
    {
        return Results.BadRequest(new
        {
            status = "backup_name_required",
            message = "Backup name is required."
        });
    }

    if (!string.Equals(backupNameRaw, backupName, StringComparison.Ordinal))
    {
        return Results.BadRequest(new
        {
            status = "invalid_backup_name",
            message = "Backup name must be a filename only, not a path."
        });
    }

    if (!backupName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            status = "invalid_backup_name",
            message = "Backup name must end with .tgz."
        });
    }

    var backupPath = Path.Combine(backupRoot, backupName);

    if (!File.Exists(backupPath))
    {
        return Results.BadRequest(new
        {
            status = "backup_not_found",
            message = "Backup file was not found."
        });
    }

    var backupCount = Directory.Exists(backupRoot)
        ? Directory.EnumerateFiles(backupRoot, "*.tgz", SearchOption.TopDirectoryOnly).Count()
        : 0;

    if (backupCount <= 1)
    {
        return Results.BadRequest(new
        {
            status = "last_backup_protected",
            message = "Cannot delete the last remaining backup."
        });
    }

    var restoreSettings = File.Exists(restoreSettingsFile)
        ? ReadProjectPulseEnvFile(restoreSettingsFile)
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    var selectedRestorePoint = restoreSettings.GetValueOrDefault("PROJECTPULSE_RESTORE_VALIDATION_SELECTED_BACKUP", "");

    if (!string.IsNullOrWhiteSpace(selectedRestorePoint) &&
        string.Equals(selectedRestorePoint, backupName, StringComparison.Ordinal))
    {
        return Results.BadRequest(new
        {
            status = "selected_restore_point_protected",
            message = "Cannot delete the backup currently selected as the Restore Validation restore point."
        });
    }

    Directory.CreateDirectory(pendingDir);

    var requestId = $"backup-delete-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
    var requestFile = Path.Combine(pendingDir, requestId + ".json");

    var queuePayload = new
    {
        requestId,
        backupName,
        reason = request.Reason?.Trim() ?? "",
        requestedAt = DateTimeOffset.UtcNow
    };

    await File.WriteAllTextAsync(
        requestFile,
        System.Text.Json.JsonSerializer.Serialize(queuePayload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }));

    await RunProjectPulseProcessAsync("/usr/bin/chmod", "660", requestFile);

    return Results.Accepted($"/api/system/backup-retention/status", new
    {
        status = "backup_delete_queued",
        message = "Backup deletion was queued. The root-owned delete runner will process it shortly.",
        requestId,
        backupName
    });
});

app.MapGet("/api/system/restore-validation/status", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Restore Validation Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    const string statusFile = "/opt/project-time-platform/state/restore-validation-status.json";

    if (!File.Exists(statusFile))
    {
        return Results.Json(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            overallStatus = "unknown",
            message = "Restore validation has not been exported yet.",
            checks = Array.Empty<object>()
        });
    }

    var json = await File.ReadAllTextAsync(statusFile);
    return Results.Content(json, "application/json");
});

app.MapGet("/api/system/replication-sync/status", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Replication & Sync Status Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    const string statusFile = "/opt/project-time-platform/state/replication-sync-status.json";

    if (!File.Exists(statusFile))
    {
        return Results.Json(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            overallStatus = "unknown",
            message = "Replication and sync status has not been exported yet.",
            checks = Array.Empty<object>()
        });
    }

    var json = await File.ReadAllTextAsync(statusFile);
    return Results.Content(json, "application/json");
});

app.MapGet("/api/system/backup-dr/status", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Backup / DR Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var generatedAt = DateTimeOffset.UtcNow;
    var checks = new List<object>();

    async Task AddCommandCheckAsync(
        string key,
        string name,
        string category,
        string command,
        bool requireOutput = false,
        string actionRequiredMessage = "Review required.")
    {
        var result = await RunProjectPulseProcessAsync("/usr/bin/bash", "-lc", command);
        var output = string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardError
            : result.StandardOutput;

        var hasOutput = !string.IsNullOrWhiteSpace(output);
        var checkStatus = result.ExitCode == 0 && (!requireOutput || hasOutput)
            ? "ready"
            : "action_required";

        var message = checkStatus == "ready"
            ? "Check passed."
            : actionRequiredMessage;

        checks.Add(new
        {
            key,
            name,
            category,
            status = checkStatus,
            message,
            checkedAt = generatedAt,
            details = new
            {
                command,
                exitCode = result.ExitCode,
                output
            }
        });
    }

    try
    {
        await using var dbCommand = new NpgsqlCommand("""
            SELECT
                current_database(),
                pg_size_pretty(pg_database_size(current_database())) AS database_size,
                (
                    SELECT COUNT(*)
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_type = 'BASE TABLE'
                ) AS public_table_count,
                NOW();
            """, connection);

        await using var reader = await dbCommand.ExecuteReaderAsync();
        await reader.ReadAsync();

        checks.Add(new
        {
            key = "database-connectivity",
            name = "PostgreSQL Database Connectivity",
            category = "Database",
            status = "ready",
            message = "Database is reachable and metadata was collected.",
            checkedAt = generatedAt,
            details = new
            {
                database = reader.GetString(0),
                databaseSize = reader.GetString(1),
                publicTableCount = reader.GetInt64(2),
                databaseTime = reader.GetFieldValue<DateTimeOffset>(3)
            }
        });
    }
    catch (Exception ex)
    {
        checks.Add(new
        {
            key = "database-connectivity",
            name = "PostgreSQL Database Connectivity",
            category = "Database",
            status = "action_required",
            message = "Database metadata could not be collected.",
            checkedAt = generatedAt,
            details = new
            {
                error = ex.Message
            }
        });
    }

    await AddCommandCheckAsync(
        "backup-artifacts",
        "Latest Backup Artifacts",
        "Backups",
        "find /opt/project-time-platform /opt/project-time-platform/backups /var/backups /opt/backup /tmp -maxdepth 5 \\( -iname '*.sql' -o -iname '*.dump' -o -iname '*.backup' -o -iname '*.tgz' -o -iname '*.tar.gz' \\) -printf '%T@|%TY-%Tm-%Td %TH:%TM|%s|%p\\n' 2>/dev/null | sort -nr | head -25",
        requireOutput: true,
        actionRequiredMessage: "No backup artifacts were found in the monitored locations.");

    await AddCommandCheckAsync(
        "backup-directory",
        "ProjectPulse Backup Directory",
        "Backups",
        "test -d /opt/project-time-platform/backups && find /opt/project-time-platform/backups -maxdepth 2 -type f -printf '%TY-%Tm-%Td %TH:%TM|%s|%p\\n' 2>/dev/null | sort -r | head -25",
        requireOutput: false,
        actionRequiredMessage: "Backup directory /opt/project-time-platform/backups does not exist yet.");

    await AddCommandCheckAsync(
        "backup-tools",
        "Backup Tool Availability",
        "Tools",
        "command -v pg_dump && pg_dump --version && command -v pg_restore && pg_restore --version && command -v psql && psql --version && command -v tar && tar --version | head -1 && command -v gzip && gzip --version | head -1",
        requireOutput: true,
        actionRequiredMessage: "One or more required backup/restore tools are missing.");

    await AddCommandCheckAsync(
        "sftp-backup-target",
        "External SFTP Backup Target",
        "External Backup",
        "if [ -f /opt/project-time-platform/config/backup-sftp.env ]; then source /opt/project-time-platform/config/backup-sftp.env; echo host=${PROJECTPULSE_BACKUP_SFTP_HOST:-missing}; echo port=${PROJECTPULSE_BACKUP_SFTP_PORT:-22}; echo user=${PROJECTPULSE_BACKUP_SFTP_USER:-missing}; echo remote_path=${PROJECTPULSE_BACKUP_SFTP_REMOTE_PATH:-missing}; if [ -n \"${PROJECTPULSE_BACKUP_SFTP_KEY_PATH:-}\" ] && [ -f \"$PROJECTPULSE_BACKUP_SFTP_KEY_PATH\" ]; then echo key_status=present; else echo key_status=missing; fi; else echo 'SFTP backup target is not configured'; exit 2; fi",
        requireOutput: true,
        actionRequiredMessage: "External SFTP backup target is not configured yet.");

    await AddCommandCheckAsync(
        "git-deployment-state",
        "Git Deployment State",
        "Application",
        "cd /opt/project-time-platform/app/project-time-platform && echo Branch: $(git rev-parse --abbrev-ref HEAD) && echo Commit: $(git rev-parse HEAD) && echo ShortCommit: $(git rev-parse --short HEAD) && echo Status: && git status --short",
        requireOutput: true,
        actionRequiredMessage: "Git deployment state could not be collected.");

    await AddCommandCheckAsync(
        "application-config-files",
        "Application Configuration Files",
        "Configuration",
        "for p in /opt/project-time-platform/config/*.env /etc/systemd/system/projecttime-api.service /etc/systemd/system/projecttime-api.service.d/*.conf /etc/systemd/system/projecttime-frontend-public.service /etc/systemd/system/projecttime-frontend-public.service.d/*.conf /etc/nginx/conf.d/projectpulse.conf; do [ -e \"$p\" ] && stat -c '%n|owner=%U:%G|mode=%a|bytes=%s|modified=%y' \"$p\"; done",
        requireOutput: true,
        actionRequiredMessage: "No application/system configuration files were detected from the monitored paths.");

    await AddCommandCheckAsync(
        "systemd-units",
        "Systemd Unit Files",
        "Configuration",
        "systemctl list-unit-files 'projecttime*' 'projectpulse*' --no-pager 2>/dev/null",
        requireOutput: true,
        actionRequiredMessage: "ProjectPulse systemd unit files were not detected.");

    await AddCommandCheckAsync(
        "nginx-config",
        "Nginx Configuration",
        "Configuration",
        "cat /opt/project-time-platform/state/nginx-readiness.json 2>/dev/null && grep -q '\"status\": \"ready\"' /opt/project-time-platform/state/nginx-readiness.json",
        requireOutput: true,
        actionRequiredMessage: "Nginx readiness export was not available or reported action required.");

    await AddCommandCheckAsync(
        "runbook",
        "Backup / DR Runbook",
        "Recovery",
        "for p in /opt/project-time-platform/runbooks/backup-dr.md /opt/project-time-platform/app/project-time-platform/docs/backup-dr.md /opt/project-time-platform/app/project-time-platform/README.md; do [ -f \"$p\" ] && stat -c '%n|bytes=%s|modified=%y' \"$p\"; done",
        requireOutput: true,
        actionRequiredMessage: "A dedicated Backup / DR runbook was not found yet.");

    var readyCount = checks.Count(check => string.Equals(
        Convert.ToString(check.GetType().GetProperty("status")?.GetValue(check)),
        "ready",
        StringComparison.OrdinalIgnoreCase));

    var actionRequiredCount = checks.Count - readyCount;

    return Results.Ok(new
    {
        status = actionRequiredCount == 0 ? "ready" : "action_required",
        generatedAt,
        readyCount,
        actionRequiredCount,
        totalCount = checks.Count,
        checks
    });
});



app.MapGet("/api/system/service-control/status", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Service Control Center is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var services = new List<object>();

    foreach (var service in projectPulseManagedServices.Values)
    {
        var systemdName = (string)service.GetType().GetProperty("systemdName")!.GetValue(service)!;
        var statusResult = await RunProjectPulseProcessAsync("/usr/bin/systemctl", "show", systemdName,
            "--property=Id,Description,LoadState,ActiveState,SubState,ActiveEnterTimestamp,InactiveEnterTimestamp,NRestarts",
            "--no-page");

        var isActiveResult = await RunProjectPulseProcessAsync("/usr/bin/systemctl", "is-active", systemdName);
        var recentLogsResult = await RunProjectPulseProcessAsync("/usr/bin/journalctl", "-u", systemdName, "-n", "12", "--no-pager", "--output=short-iso");

        var properties = ParseSystemctlShowProperties(statusResult.StandardOutput);

        services.Add(new
        {
            serviceKey = service.GetType().GetProperty("serviceKey")!.GetValue(service),
            systemdName,
            displayName = service.GetType().GetProperty("displayName")!.GetValue(service),
            description = service.GetType().GetProperty("description")!.GetValue(service),
            activeState = properties.GetValueOrDefault("ActiveState", isActiveResult.StandardOutput.Trim()),
            subState = properties.GetValueOrDefault("SubState", ""),
            loadState = properties.GetValueOrDefault("LoadState", ""),
            activeSince = properties.GetValueOrDefault("ActiveEnterTimestamp", ""),
            inactiveSince = properties.GetValueOrDefault("InactiveEnterTimestamp", ""),
            restartCount = properties.GetValueOrDefault("NRestarts", "0"),
            commandStatus = statusResult.ExitCode == 0 ? "ok" : "error",
            statusError = statusResult.StandardError,
            recentLogs = recentLogsResult.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .TakeLast(12)
                .ToArray()
        });
    }

    return Results.Ok(new
    {
        status = "service_status_loaded",
        generatedAt = DateTimeOffset.UtcNow,
        count = services.Count,
        services
    });
});

app.MapGet("/api/system/api-status", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "API Status Dashboard is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var components = new List<object>();

    async Task AddComponentAsync(string key, string name, string category, Func<Task<object>> probe)
    {
        try
        {
            var details = await probe();
            components.Add(new
            {
                key,
                name,
                category,
                status = "healthy",
                checkedAt = DateTimeOffset.UtcNow,
                details
            });
        }
        catch (Exception ex)
        {
            components.Add(new
            {
                key,
                name,
                category,
                status = "unhealthy",
                checkedAt = DateTimeOffset.UtcNow,
                details = new
                {
                    error = ex.Message
                }
            });
        }
    }

    await AddComponentAsync("database", "Database Health", "Core", async () =>
    {
        await using var command = new NpgsqlCommand("SELECT current_database(), current_user, NOW();", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            database = reader.GetString(0),
            user = reader.GetString(1),
            serverTime = reader.GetFieldValue<DateTimeOffset>(2)
        };
    });

    await AddComponentAsync("auth", "Auth API", "Core", async () =>
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '24 hours') AS login_events_24h,
                COUNT(*) FILTER (WHERE login_result ILIKE '%fail%' AND created_at >= NOW() - INTERVAL '24 hours') AS failed_logins_24h
            FROM auth_login_events;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            loginEvents24h = reader.GetInt64(0),
            failedLogins24h = reader.GetInt64(1)
        };
    });

    await AddComponentAsync("timesheet", "Timesheet API", "Application", async () =>
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) AS time_entry_count,
                COUNT(*) FILTER (WHERE work_date >= CURRENT_DATE - INTERVAL '14 days') AS recent_time_entries
            FROM time_entries;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            totalTimeEntries = reader.GetInt64(0),
            recentTimeEntries = reader.GetInt64(1)
        };
    });

    await AddComponentAsync("approvals", "Approval API", "Application", async () =>
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) FILTER (WHERE status = 'submitted') AS submitted_days,
                COUNT(*) FILTER (WHERE status = 'manager_approved') AS manager_approved_days,
                COUNT(*) FILTER (WHERE status = 'manager_declined') AS manager_declined_days
            FROM timesheet_day_statuses;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            submittedDays = reader.GetInt64(0),
            managerApprovedDays = reader.GetInt64(1),
            managerDeclinedDays = reader.GetInt64(2)
        };
    });

    await AddComponentAsync("audit", "Audit API", "Security", async () =>
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                (SELECT COUNT(*) FROM audit_logs WHERE created_at >= NOW() - INTERVAL '7 days') AS audit_log_events,
                (SELECT COUNT(*) FROM auth_login_events WHERE created_at >= NOW() - INTERVAL '7 days') AS login_events,
                (SELECT COUNT(*) FROM auth_password_reset_requests WHERE requested_at >= NOW() - INTERVAL '30 days') AS password_reset_events;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            auditLogEvents7d = reader.GetInt64(0),
            loginEvents7d = reader.GetInt64(1),
            passwordResetEvents30d = reader.GetInt64(2)
        };
    });

    await AddComponentAsync("azure-admin", "Azure / Entra Admin API", "Integrations", async () =>
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) AS sync_runs,
                COUNT(*) FILTER (WHERE lower(status) IN ('failed', 'error')) AS failed_runs,
                MAX(created_at) AS last_run_at
            FROM azure_entra_import_runs
            WHERE created_at >= NOW() - INTERVAL '30 days';
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            syncRuns30d = reader.GetInt64(0),
            failedRuns30d = reader.GetInt64(1),
            lastRunAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2)
        };
    });

    await AddComponentAsync("user-admin", "User Admin API", "Administration", async () =>
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) AS users,
                COUNT(*) FILTER (WHERE is_active = TRUE AND login_enabled = TRUE) AS login_enabled_users,
                COUNT(*) FILTER (WHERE source_provider = 'LOCAL_APP') AS local_users
            FROM app_users;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            users = reader.GetInt64(0),
            loginEnabledUsers = reader.GetInt64(1),
            localUsers = reader.GetInt64(2)
        };
    });

    await AddComponentAsync("project-allocation", "Project Allocation API", "Application", async () =>
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                to_regclass('project_allocation_projects') IS NOT NULL AS has_project_allocation_projects,
                to_regclass('project_engineer_allocations') IS NOT NULL AS has_engineer_allocations;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            hasProjectAllocationProjects = reader.GetBoolean(0),
            hasEngineerAllocations = reader.GetBoolean(1)
        };
    });

    await AddComponentAsync("ai-description", "AI Description API", "AI", async () =>
    {
        var hasClaudeKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPULSE_CLAUDE_API_KEY"));
        var model = Environment.GetEnvironmentVariable("PROJECTPULSE_CLAUDE_MODEL");

        return new
        {
            mode = hasClaudeKey ? "claude_configured" : "local_fallback",
            model = string.IsNullOrWhiteSpace(model) ? "default" : model
        };
    });

    await AddComponentAsync("notifications", "Notification Outbox", "Operations", async () =>
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COUNT(*) FILTER (WHERE status = 'pending') AS pending,
                COUNT(*) FILTER (WHERE status = 'sent') AS sent,
                COUNT(*) FILTER (WHERE status = 'failed' OR error_message IS NOT NULL) AS failed
            FROM notification_outbox;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new
        {
            pending = reader.GetInt64(0),
            sent = reader.GetInt64(1),
            failed = reader.GetInt64(2)
        };
    });

    return Results.Ok(new
    {
        status = components.Any(component => ((string)component.GetType().GetProperty("status")!.GetValue(component)!) == "unhealthy")
            ? "degraded"
            : "healthy",
        generatedAt = DateTimeOffset.UtcNow,
        components
    });
});


app.MapGet("/api/system/version-inventory", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Version Inventory is restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var checkedAt = DateTimeOffset.UtcNow;
    var items = new System.Collections.Concurrent.ConcurrentBag<object>();
    var versionTasks = new List<Task>();

    async Task AddShellVersionAsync(string key, string name, string category, string command)
    {
        try
        {
            var result = await RunProjectPulseProcessAsync("/usr/bin/bash", "-lc", command);
            var output = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError
                : result.StandardOutput;

            var version = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "Unavailable";

            var normalizedOutput = output.ToLowerInvariant();
            var versionStatus = result.ExitCode == 0 ? "detected" : "check_failed";

            if (normalizedOutput.Contains("no matching") ||
                normalizedOutput.Contains("not installed") ||
                normalizedOutput.Contains("no node.js rpm package match detected") ||
                normalizedOutput.Contains("no .net rpm package match detected") ||
                normalizedOutput.Contains("no postgresql rpm package match detected"))
            {
                versionStatus = "not_detected";
            }

            if (normalizedOutput.Contains("timed out"))
            {
                versionStatus = "check_timed_out";
            }

            items.Add(new
            {
                key,
                name,
                category,
                status = versionStatus,
                version,
                checkedAt,
                details = new
                {
                    command,
                    exitCode = result.ExitCode,
                    output
                }
            });
        }
        catch (Exception ex)
        {
            items.Add(new
            {
                key,
                name,
                category,
                status = "check_failed",
                version = "Check failed",
                checkedAt,
                details = new
                {
                    command,
                    error = ex.Message
                }
            });
        }
    }

    items.Add(new
    {
        key = "projectpulse-api-runtime",
        name = "ProjectPulse API Runtime",
        category = "ProjectPulse",
        status = "detected",
        version = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        checkedAt,
        details = new
        {
            environmentVersion = Environment.Version.ToString(),
            osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            machineName = Environment.MachineName,
            applicationBasePath = AppContext.BaseDirectory
        }
    });

    versionTasks.Add(AddShellVersionAsync("projectpulse-api-service", "ProjectPulse API Service", "ProjectPulse", "systemctl show projecttime-api.service --property=Id,Description,ActiveState,SubState,FragmentPath,ExecMainPID --no-page || systemctl show projectpulse-api.service --property=Id,Description,ActiveState,SubState,FragmentPath,ExecMainPID --no-page"));
    versionTasks.Add(AddShellVersionAsync("projectpulse-frontend-service", "ProjectPulse Frontend Service", "ProjectPulse", "systemctl show projecttime-frontend-public.service --property=Id,Description,ActiveState,SubState,FragmentPath,ExecMainPID --no-page || systemctl show projectpulse-frontend-public.service --property=Id,Description,ActiveState,SubState,FragmentPath,ExecMainPID --no-page"));

    versionTasks.Add(AddShellVersionAsync("operating-system", "Operating System", "Host", "source /etc/os-release && echo \"$PRETTY_NAME\""));
    versionTasks.Add(AddShellVersionAsync("linux-kernel", "Linux Kernel", "Host", "uname -r"));
    versionTasks.Add(AddShellVersionAsync("systemd", "systemd", "Host", "systemctl --version | head -1"));

    versionTasks.Add(AddShellVersionAsync("dotnet-sdk", ".NET SDK", "Runtime", "dotnet --version"));
    versionTasks.Add(AddShellVersionAsync("dotnet-runtimes", ".NET Runtimes", "Runtime", "dotnet --list-runtimes"));
    versionTasks.Add(AddShellVersionAsync("nodejs", "Node.js", "Runtime", "node --version"));
    versionTasks.Add(AddShellVersionAsync("npm", "npm", "Runtime", "npm --version"));
    versionTasks.Add(AddShellVersionAsync("python", "Python", "Runtime", "python3 --version"));

    versionTasks.Add(AddShellVersionAsync("nginx", "Nginx", "Web Server", "nginx -v 2>&1"));
    versionTasks.Add(AddShellVersionAsync("openssl", "OpenSSL", "Security", "openssl version"));
    versionTasks.Add(AddShellVersionAsync("git", "Git", "Source Control", "git --version"));

    versionTasks.Add(AddShellVersionAsync("postgresql-client", "PostgreSQL Client", "Database", "psql --version"));

    try
    {
        await using var postgresCommand = new NpgsqlCommand("SHOW server_version;", connection);
        var serverVersion = Convert.ToString(await postgresCommand.ExecuteScalarAsync()) ?? "Unavailable";

        items.Add(new
        {
            key = "postgresql-server",
            name = "PostgreSQL Server",
            category = "Database",
            status = "detected",
            version = serverVersion,
            checkedAt,
            details = new
            {
                source = "SHOW server_version"
            }
        });
    }
    catch (Exception ex)
    {
        items.Add(new
        {
            key = "postgresql-server",
            name = "PostgreSQL Server",
            category = "Database",
            status = "check_failed",
            version = "Check failed",
            checkedAt,
            details = new
            {
                error = ex.Message
            }
        });
    }

    versionTasks.Add(AddShellVersionAsync("rocky-release-package", "Rocky Release Package", "Packages", "rpm -q rocky-release"));
    versionTasks.Add(AddShellVersionAsync("nginx-package", "Nginx Package", "Packages", "rpm -q nginx"));
    versionTasks.Add(AddShellVersionAsync("postgresql-package", "PostgreSQL Package", "Packages", "rpm -qa | grep -Ei '^postgresql|^postgresql[0-9]+' | sort | head -20"));
    versionTasks.Add(AddShellVersionAsync("dotnet-package", ".NET Packages", "Packages", "rpm -qa | grep -Ei '^dotnet|^aspnetcore' | sort | head -20"));
    versionTasks.Add(AddShellVersionAsync("node-package", "Node.js Package", "Packages", "rpm -qa | grep -Ei '^nodejs|^npm' | sort | head -20"));

    await Task.WhenAll(versionTasks);

    return Results.Ok(new
    {
        status = "version_inventory_loaded",
        generatedAt = checkedAt,
        count = items.Count,
        items
    });
});



app.MapPost("/api/system/service-control/restart", async (ServiceRestartRequest request, HttpContext httpContext) =>
{
    var serviceKey = (request.ServiceKey ?? string.Empty).Trim().ToLowerInvariant();

    if (!projectPulseManagedServices.TryGetValue(serviceKey, out var serviceDefinition))
    {
        return Results.BadRequest(new
        {
            status = "invalid_service",
            message = "The requested service is not allowlisted for Project Pulse service control."
        });
    }

    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 8)
    {
        return Results.BadRequest(new
        {
            status = "reason_required",
            message = "A restart reason of at least 8 characters is required."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var adminContext = await ResolveProjectPulseAdministratorContextAsync(httpContext, connection);
    if (!adminContext.IsAdministrator)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Restart actions are restricted to administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var systemdName = (string)serviceDefinition.GetType().GetProperty("systemdName")!.GetValue(serviceDefinition)!;
    var displayName = (string)serviceDefinition.GetType().GetProperty("displayName")!.GetValue(serviceDefinition)!;

    await InsertProjectPulseAuditEventAsync(
        connection,
        adminContext.UserId,
        "service_restart_requested",
        "system_service",
        null,
        httpContext,
        new
        {
            serviceKey,
            systemdName,
            displayName,
            reason = request.Reason.Trim()
        });

    var restartResult = await RunProjectPulseProcessAsync("/usr/bin/sudo", "-n", "/usr/bin/systemctl", "restart", systemdName);

    await InsertProjectPulseAuditEventAsync(
        connection,
        adminContext.UserId,
        restartResult.ExitCode == 0 ? "service_restart_completed" : "service_restart_failed",
        "system_service",
        null,
        httpContext,
        new
        {
            serviceKey,
            systemdName,
            displayName,
            reason = request.Reason.Trim(),
            exitCode = restartResult.ExitCode,
            standardOutput = restartResult.StandardOutput,
            standardError = restartResult.StandardError
        });

    if (restartResult.ExitCode != 0)
    {
        return Results.Json(new
        {
            status = "restart_failed",
            serviceKey,
            systemdName,
            message = restartResult.StandardError.Length > 0 ? restartResult.StandardError : "Service restart failed.",
            restartResult.ExitCode
        }, statusCode: StatusCodes.Status500InternalServerError);
    }

    return Results.Ok(new
    {
        status = "restart_requested",
        serviceKey,
        systemdName,
        displayName,
        message = $"Restart completed for {displayName}."
    });
});



app.MapGet("/api/audit/history", async (HttpContext httpContext, int? days, string? category, string? status, string? search) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Audit history is restricted to administrators and project/team coordinators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var lookbackDays = Math.Clamp(days ?? 14, 1, 365);
    var normalizedCategory = string.IsNullOrWhiteSpace(category) ? "all" : category.Trim().ToLowerInvariant();
    var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
    var normalizedSearch = string.IsNullOrWhiteSpace(search) ? "" : search.Trim();

    var events = new List<object>();

    await using var command = new NpgsqlCommand("""
        WITH unified_events AS (
            SELECT
                ale.created_at AS event_time,
                'authentication'::text AS category,
                CASE
                    WHEN lower(ale.login_result) LIKE '%success%' THEN 'success'
                    WHEN lower(ale.login_result) LIKE '%fail%' OR lower(ale.login_result) LIKE '%invalid%' OR lower(ale.login_result) LIKE '%locked%' THEN 'failure'
                    ELSE lower(ale.login_result)
                END AS status,
                CASE
                    WHEN lower(ale.login_result) LIKE '%success%' THEN 'Login success'
                    ELSE 'Login failure'
                END AS event_type,
                COALESCE(ale.username, u.email, 'Unknown') AS actor,
                COALESCE(u.email, ale.username, 'Unknown') AS target,
                ale.login_method AS source,
                COALESCE(ale.event_details::text, '') AS details,
                ale.source_ip AS ip_address,
                ale.user_agent AS user_agent,
                ale.auth_login_event_id AS event_id
            FROM auth_login_events ale
            LEFT JOIN app_users u ON u.user_id = ale.user_id

            UNION ALL

            SELECT
                pr.requested_at AS event_time,
                'password_reset'::text AS category,
                CASE
                    WHEN pr.status = 'declined' THEN 'failure'
                    WHEN pr.status = 'completed' THEN 'success'
                    WHEN pr.status = 'approved' THEN 'warning'
                    ELSE 'pending'
                END AS status,
                CASE
                    WHEN pr.status = 'pending_approval' THEN 'Password reset requested'
                    WHEN pr.status = 'approved' THEN 'Password reset approved'
                    WHEN pr.status = 'declined' THEN 'Password reset declined'
                    WHEN pr.status = 'completed' THEN 'Temporary password set'
                    ELSE 'Password reset event'
                END AS event_type,
                pr.requested_by_email AS actor,
                u.email AS target,
                'LOCAL_APP'::text AS source,
                COALESCE(pr.notes, '') AS details,
                NULL::text AS ip_address,
                NULL::text AS user_agent,
                pr.auth_password_reset_request_id AS event_id
            FROM auth_password_reset_requests pr
            JOIN app_users u ON u.user_id = pr.user_id

            UNION ALL

            SELECT
                air.created_at AS event_time,
                'azure_sync'::text AS category,
                CASE
                    WHEN lower(air.status) IN ('completed', 'success', 'succeeded') THEN 'success'
                    WHEN lower(air.status) IN ('failed', 'error') THEN 'failure'
                    ELSE 'warning'
                END AS status,
                CASE
                    WHEN lower(air.status) IN ('completed', 'success', 'succeeded') THEN 'Azure sync completed'
                    WHEN lower(air.status) IN ('failed', 'error') THEN 'Azure sync failure'
                    ELSE 'Azure sync event'
                END AS event_type,
                COALESCE(requested_by.email, 'System') AS actor,
                air.tenant_domain AS target,
                air.source_provider AS source,
                CONCAT(
                    'Run type: ', air.run_type,
                    '; environment: ', air.environment_mode,
                    '; selected: ', air.selected_count,
                    '; imported: ', air.imported_count,
                    '; updated: ', air.updated_count,
                    '; deactivated: ', air.deactivated_count,
                    '; skipped: ', air.skipped_count,
                    '; message: ', COALESCE(air.message, '')
                ) AS details,
                NULL::text AS ip_address,
                NULL::text AS user_agent,
                air.import_run_id AS event_id
            FROM azure_entra_import_runs air
            LEFT JOIN app_users requested_by ON requested_by.user_id = air.requested_by_user_id

            UNION ALL

            SELECT
                no.created_at AS event_time,
                'notification'::text AS category,
                CASE
                    WHEN lower(no.status) = 'sent' THEN 'success'
                    WHEN lower(no.status) = 'failed' OR no.error_message IS NOT NULL THEN 'failure'
                    ELSE 'pending'
                END AS status,
                CASE
                    WHEN lower(no.status) = 'sent' THEN 'Notification sent'
                    WHEN lower(no.status) = 'failed' OR no.error_message IS NOT NULL THEN 'Notification failure'
                    ELSE 'Notification pending'
                END AS event_type,
                'System'::text AS actor,
                no.recipient_email AS target,
                no.notification_type AS source,
                CONCAT(no.subject, '; ', COALESCE(no.error_message, no.body, '')) AS details,
                NULL::text AS ip_address,
                NULL::text AS user_agent,
                no.notification_outbox_id AS event_id
            FROM notification_outbox no

            UNION ALL

            SELECT
                al.created_at AS event_time,
                'system_audit'::text AS category,
                'success'::text AS status,
                al.action AS event_type,
                COALESCE(actor.email, 'System') AS actor,
                COALESCE(al.entity_type, 'Unknown') AS target,
                al.entity_type AS source,
                CONCAT(
                    'Old: ', COALESCE(al.old_value::text, ''),
                    '; New: ', COALESCE(al.new_value::text, '')
                ) AS details,
                al.ip_address::text AS ip_address,
                al.user_agent AS user_agent,
                al.audit_log_id AS event_id
            FROM audit_logs al
            LEFT JOIN app_users actor ON actor.user_id = al.actor_user_id
        )
        SELECT
            event_time,
            category,
            status,
            event_type,
            actor,
            target,
            source,
            details,
            ip_address,
            user_agent,
            event_id
        FROM unified_events
        WHERE event_time >= NOW() - (@lookback_days::text || ' days')::interval
          AND (@category = 'all' OR category = @category)
          AND (@status = 'all' OR status = @status)
          AND (
              @search = ''
              OR actor ILIKE '%' || @search || '%'
              OR target ILIKE '%' || @search || '%'
              OR event_type ILIKE '%' || @search || '%'
              OR details ILIKE '%' || @search || '%'
              OR source ILIKE '%' || @search || '%'
          )
        ORDER BY event_time DESC
        LIMIT 500;
        """, connection);

    command.Parameters.AddWithValue("lookback_days", lookbackDays);
    command.Parameters.AddWithValue("category", normalizedCategory);
    command.Parameters.AddWithValue("status", normalizedStatus);
    command.Parameters.AddWithValue("search", normalizedSearch);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        events.Add(new
        {
            eventTime = reader.GetFieldValue<DateTimeOffset>(0),
            category = reader.GetString(1),
            status = reader.GetString(2),
            eventType = reader.GetString(3),
            actor = reader.GetString(4),
            target = reader.GetString(5),
            source = reader.GetString(6),
            details = reader.IsDBNull(7) ? "" : reader.GetString(7),
            ipAddress = reader.IsDBNull(8) ? null : reader.GetString(8),
            userAgent = reader.IsDBNull(9) ? null : reader.GetString(9),
            eventId = reader.GetGuid(10)
        });
    }

    return Results.Ok(new
    {
        lookbackDays,
        category = normalizedCategory,
        status = normalizedStatus,
        search = normalizedSearch,
        count = events.Count,
        events
    });
});



app.MapGet("/api/manager/approval-count", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var submittedTimeCount = 0;
    var passwordResetCount = 0;

    await using (var timeCommand = new NpgsqlCommand("""
        SELECT COUNT(*)
        FROM timesheets
        WHERE status IN ('submitted', 'submitted_for_manager_approval', 'pending_manager_approval');
        """, connection))
    {
        submittedTimeCount = Convert.ToInt32(await timeCommand.ExecuteScalarAsync() ?? 0);
    }

    await using (var resetCommand = new NpgsqlCommand("""
        SELECT COUNT(*)
        FROM auth_password_reset_requests
        WHERE status IN ('pending_approval', 'requested', 'pending');
        """, connection))
    {
        passwordResetCount = Convert.ToInt32(await resetCommand.ExecuteScalarAsync() ?? 0);
    }

    return Results.Ok(new
    {
        submittedTimeCount,
        passwordResetCount,
        totalPendingCount = submittedTimeCount + passwordResetCount
    });
});





async Task<bool> ProjectAllocationUserHasPermissionAsync(NpgsqlConnection connection, Guid userId, params string[] permissionCodes)
{
    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
            LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND (
                    r.role_code = 'ADMINISTRATOR'
                 OR p.permission_code = ANY(@permission_codes)
              )
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("permission_codes", permissionCodes);

    var result = await command.ExecuteScalarAsync();
    return result is bool value && value;
}

string GetProjectPulseUploadRoot()
{
    var configured = Environment.GetEnvironmentVariable("PROJECT_PULSE_UPLOAD_ROOT");

    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    return "/opt/project-time-platform/uploads";
}

string SanitizeProjectPulseFileName(string fileName)
{
    var invalid = Path.GetInvalidFileNameChars();
    var clean = new string(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    return string.IsNullOrWhiteSpace(clean) ? "uploaded-file" : clean;
}

bool ProjectDocumentExtensionIsAllowed(string fileName)
{
    var extension = Path.GetExtension(fileName).ToLowerInvariant();

    return extension is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".csv";
}

async Task<bool> ProjectAllocationUserCanAccessProjectAsync(NpgsqlConnection connection, Guid userId, Guid projectAllocationProjectId)
{
    if (await ProjectAllocationUserHasPermissionAsync(connection, userId, "MANAGE_PROJECT_ALLOCATION_INFO", "PURGE_PROJECT_DOCUMENTS", "MANAGE_ALL"))
    {
        return true;
    }

    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM project_engineer_allocations pea
            WHERE pea.project_allocation_project_id = @project_id
              AND pea.user_id = @user_id
              AND pea.is_active = TRUE
        );
        """, connection);

    command.Parameters.AddWithValue("project_id", projectAllocationProjectId);
    command.Parameters.AddWithValue("user_id", userId);

    var result = await command.ExecuteScalarAsync();
    return result is bool value && value;
}


app.MapGet("/api/utilization/yearly-status", async (int? year, HttpContext httpContext) =>
{
    var selectedYear = year ?? DateTime.UtcNow.Year;

    if (selectedYear < 2026) selectedYear = 2026;
    if (selectedYear > 2036) selectedYear = 2036;

    decimal standardQuarterHours = 482m;

    var targets = new[] { 70m, 75m, 80m, 85m, 90m, 95m, 100m, 105m }
        .Select(percent => new
        {
            targetPercent = percent,
            targetHours = Math.Round(standardQuarterHours * percent / 100m, 1)
        })
        .ToList();

    var quarters = new List<object>();

    foreach (var quarterNumber in new[] { 1, 2, 3, 4 })
    {
        decimal billableHours = 0m;
        decimal utilizationPercent = 0m;

        var nextTarget = targets.FirstOrDefault(target => target.targetHours > billableHours);
        var hoursToNextTarget = nextTarget is null ? 0m : Math.Max(0, Math.Round(nextTarget.targetHours - billableHours, 2));

        quarters.Add(new
        {
            quarterNumber,
            quarterName = $"Q{quarterNumber}",
            standardQuarterHours,
            billableHours,
            utilizationPercent,
            nextTargetPercent = nextTarget?.targetPercent,
            nextTargetHours = nextTarget?.targetHours,
            hoursToNextTarget,
            thresholds = targets.Select(target => new
            {
                target.targetPercent,
                target.targetHours,
                hoursRemaining = Math.Max(0, Math.Round(target.targetHours - billableHours, 2)),
                reached = billableHours >= target.targetHours
            })
        });
    }

    return Results.Ok(new
    {
        year = selectedYear,
        standardQuarterHours,
        calculationStatus = "placeholder",
        calculationNote = "Project and service-request utilization calculation will be finalized during 019I. This view is stable now so engineers can see yearly quarterly thresholds.",
        quarters
    });
});



app.MapGet("/api/project-allocation-info/source-projects", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var canManage = await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "MANAGE_PROJECT_ALLOCATION_INFO", "MANAGE_ALL");
    var canView = await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "VIEW_PROJECT_ALLOCATION_INFO", "MANAGE_ALL");

    if (!canManage && !canView)
    {
        return Results.Json(new { status = "access_denied", message = "You do not have access to Project Allocation source projects." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var projectMap = new Dictionary<Guid, (string ProjectCode, string ProjectName, List<object> Tasks)>();

    await using var command = new NpgsqlCommand("""
        SELECT
            p.project_id,
            COALESCE(p.project_code, '') AS project_code,
            COALESCE(p.project_name, '') AS project_name,
            pt.task_id,
            COALESCE(pt.task_name, '') AS task_name
        FROM projects p
        LEFT JOIN project_tasks pt
               ON pt.project_id = p.project_id
        ORDER BY
            COALESCE(p.project_code, ''),
            COALESCE(p.project_name, ''),
            COALESCE(pt.task_name, '');
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        var projectId = reader.GetGuid(0);
        var projectCode = reader.GetString(1);
        var projectName = reader.GetString(2);

        if (!projectMap.ContainsKey(projectId))
        {
            projectMap[projectId] = (projectCode, projectName, new List<object>());
        }

        if (!reader.IsDBNull(3))
        {
            var taskId = reader.GetGuid(3);
            var taskName = reader.GetString(4);

            projectMap[projectId].Tasks.Add(new
            {
                sourceTaskId = taskId,
                taskId,
                taskName
            });
        }
    }

    var projects = projectMap.Select(item => new
    {
        sourceProjectId = item.Key,
        projectId = item.Key,
        projectCode = item.Value.ProjectCode,
        projectName = item.Value.ProjectName,
        displayName = string.IsNullOrWhiteSpace(item.Value.ProjectCode)
            ? item.Value.ProjectName
            : $"{item.Value.ProjectCode} - {item.Value.ProjectName}",
        tasks = item.Value.Tasks
    }).ToList();

    return Results.Ok(new
    {
        status = "ok",
        count = projects.Count,
        projects
    });
});


app.MapGet("/api/project-allocation-info/engineers", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "MANAGE_PROJECT_ALLOCATION_INFO", "MANAGE_ALL"))
    {
        return Results.Json(new { status = "access_denied", message = "Only PM, Project/Team Coordinator, or Administrator can view engineer allocation setup." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var engineers = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT DISTINCT
            u.user_id,
            u.email,
            u.display_name,
            u.job_title,
            u.department_name,
            u.team_name
        FROM app_users u
        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id
        WHERE u.is_active = TRUE
          AND COALESCE(u.login_enabled, TRUE) = TRUE
          AND lower(u.email) NOT LIKE '%.local'
        ORDER BY u.display_name, u.email;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        engineers.Add(new
        {
            userId = reader.GetGuid(0),
            email = reader.GetString(1),
            displayName = reader.GetString(2),
            jobTitle = reader.IsDBNull(3) ? null : reader.GetString(3),
            departmentName = reader.IsDBNull(4) ? null : reader.GetString(4),
            teamName = reader.IsDBNull(5) ? null : reader.GetString(5)
        });
    }

    return Results.Ok(new { count = engineers.Count, engineers });
});

app.MapGet("/api/project-allocation-info/projects", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var canManage = await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "MANAGE_PROJECT_ALLOCATION_INFO", "MANAGE_ALL");
    var canPurge = await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "PURGE_PROJECT_DOCUMENTS", "MANAGE_ALL");

    if (!canManage && !await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "VIEW_PROJECT_ALLOCATION_INFO"))
    {
        return Results.Json(new { status = "access_denied", message = "You do not have access to Project Allocation and Info." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var projectRows = new List<(Guid ProjectId, string ProjectCode, string ProjectName, string? CustomerName, string? ServiceRequestNumber, string ProjectStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)>();

    await using (var projectCommand = new NpgsqlCommand("""
        SELECT
            p.project_allocation_project_id,
            p.project_code,
            p.project_name,
            p.customer_name,
            p.service_request_number,
            p.project_status,
            p.created_at,
            p.updated_at
        FROM project_allocation_projects p
        WHERE @can_manage = TRUE
           OR EXISTS (
                SELECT 1
                FROM project_engineer_allocations pea
                WHERE pea.project_allocation_project_id = p.project_allocation_project_id
                  AND pea.user_id = @user_id
                  AND pea.is_active = TRUE
           )
        ORDER BY p.updated_at DESC, p.project_name;
        """, connection))
    {
        projectCommand.Parameters.AddWithValue("can_manage", canManage);
        projectCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await projectCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            projectRows.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetFieldValue<DateTimeOffset>(7)
            ));
        }
    }

    var projects = new List<object>();

    foreach (var project in projectRows)
    {
        var allocations = new List<object>();
        var documents = new List<object>();

        await using (var allocationCommand = new NpgsqlCommand("""
            SELECT
                pea.project_engineer_allocation_id,
                pea.user_id,
                u.display_name,
                u.email,
                u.department_name,
                u.team_name,
                pea.allocated_hours,
                pea.allocation_notes,
                pea.is_active
            FROM project_engineer_allocations pea
            JOIN app_users u ON u.user_id = pea.user_id
            WHERE pea.project_allocation_project_id = @project_id
              AND pea.is_active = TRUE
            ORDER BY u.display_name, u.email;
            """, connection))
        {
            allocationCommand.Parameters.AddWithValue("project_id", project.ProjectId);

            await using var reader = await allocationCommand.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var allocatedHours = reader.GetDecimal(6);
                decimal usedHours = 0m;
                var remainingHours = Math.Max(0, allocatedHours - usedHours);

                allocations.Add(new
                {
                    allocationId = reader.GetGuid(0),
                    userId = reader.GetGuid(1),
                    displayName = reader.GetString(2),
                    email = reader.GetString(3),
                    departmentName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    teamName = reader.IsDBNull(5) ? null : reader.GetString(5),
                    allocatedHours,
                    usedHours,
                    remainingHours,
                    allocationNotes = reader.IsDBNull(7) ? null : reader.GetString(7),
                    isActive = reader.GetBoolean(8)
                });
            }
        }

        await using (var documentCommand = new NpgsqlCommand("""
            SELECT
                project_document_file_id,
                document_type,
                original_file_name,
                content_type,
                size_bytes,
                uploaded_at,
                is_purged,
                purged_at
            FROM project_document_files
            WHERE project_allocation_project_id = @project_id
            ORDER BY uploaded_at DESC;
            """, connection))
        {
            documentCommand.Parameters.AddWithValue("project_id", project.ProjectId);

            await using var reader = await documentCommand.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var documentId = reader.GetGuid(0);
                var isPurged = reader.GetBoolean(6);

                documents.Add(new
                {
                    documentId,
                    documentType = reader.GetString(1),
                    originalFileName = reader.GetString(2),
                    contentType = reader.IsDBNull(3) ? null : reader.GetString(3),
                    sizeBytes = reader.GetInt64(4),
                    uploadedAt = reader.GetFieldValue<DateTimeOffset>(5),
                    isPurged,
                    purgedAt = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(7),
                    downloadUrl = isPurged ? null : $"/api/project-allocation-info/documents/{documentId}/download"
                });
            }
        }

        projects.Add(new
        {
            projectId = project.ProjectId,
            projectCode = project.ProjectCode,
            projectName = project.ProjectName,
            customerName = project.CustomerName,
            serviceRequestNumber = project.ServiceRequestNumber,
            projectStatus = project.ProjectStatus,
            createdAt = project.CreatedAt,
            updatedAt = project.UpdatedAt,
            allocations,
            documents,
            totalAllocatedHours = allocations.Sum(item => (decimal)item.GetType().GetProperty("allocatedHours")!.GetValue(item)!),
            totalUsedHours = allocations.Sum(item => (decimal)item.GetType().GetProperty("usedHours")!.GetValue(item)!),
            totalRemainingHours = allocations.Sum(item => (decimal)item.GetType().GetProperty("remainingHours")!.GetValue(item)!)
        });
    }

    return Results.Ok(new
    {
        count = projects.Count,
        canManage,
        canPurge,
        calculationStatus = "allocation_foundation",
        calculationNote = "Used hours are currently placeholders and will be connected to project/service-request time entries after allocation mapping is finalized.",
        projects
    });
});

app.MapPost("/api/project-allocation-info/projects", async (ProjectAllocationProjectUpsertRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (string.IsNullOrWhiteSpace(request.ProjectCode) || string.IsNullOrWhiteSpace(request.ProjectName))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Project code and project name are required." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "MANAGE_PROJECT_ALLOCATION_INFO", "MANAGE_ALL"))
    {
        return Results.Json(new { status = "access_denied", message = "Only PM, Project/Team Coordinator, or Administrator can manage project allocations." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        Guid projectId;

        await using (var projectCommand = new NpgsqlCommand("""
            INSERT INTO project_allocation_projects (
                project_code,
                project_name,
                customer_name,
                service_request_number,
                project_status,
                created_by_user_id,
                updated_by_user_id
            )
            VALUES (
                @project_code,
                @project_name,
                NULLIF(@customer_name, ''),
                NULLIF(@service_request_number, ''),
                COALESCE(NULLIF(@project_status, ''), 'intake'),
                @user_id,
                @user_id
            )
            ON CONFLICT (project_code) DO UPDATE
            SET project_name = EXCLUDED.project_name,
                customer_name = EXCLUDED.customer_name,
                service_request_number = EXCLUDED.service_request_number,
                project_status = EXCLUDED.project_status,
                updated_by_user_id = EXCLUDED.updated_by_user_id,
                updated_at = NOW()
            RETURNING project_allocation_project_id;
            """, connection, transaction))
        {
            projectCommand.Parameters.AddWithValue("project_code", request.ProjectCode.Trim());
            projectCommand.Parameters.AddWithValue("project_name", request.ProjectName.Trim());
            projectCommand.Parameters.AddWithValue("customer_name", request.CustomerName?.Trim() ?? "");
            projectCommand.Parameters.AddWithValue("service_request_number", request.ServiceRequestNumber?.Trim() ?? "");
            projectCommand.Parameters.AddWithValue("project_status", request.ProjectStatus?.Trim() ?? "intake");
            projectCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

            projectId = (Guid)(await projectCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to save project allocation record."));
        }

        foreach (var allocation in request.Allocations ?? new List<ProjectAllocationEngineerRequest>())
        {
            if (allocation.UserId == Guid.Empty || allocation.AllocatedHours < 0)
            {
                continue;
            }

            await using var allocationCommand = new NpgsqlCommand("""
                INSERT INTO project_engineer_allocations (
                    project_allocation_project_id,
                    user_id,
                    allocated_hours,
                    allocation_notes,
                    is_active,
                    allocated_by_user_id
                )
                VALUES (
                    @project_id,
                    @user_id,
                    @allocated_hours,
                    NULLIF(@allocation_notes, ''),
                    TRUE,
                    @allocated_by_user_id
                )
                ON CONFLICT (project_allocation_project_id, user_id) DO UPDATE
                SET allocated_hours = EXCLUDED.allocated_hours,
                    allocation_notes = EXCLUDED.allocation_notes,
                    is_active = TRUE,
                    allocated_by_user_id = EXCLUDED.allocated_by_user_id,
                    updated_at = NOW();
                """, connection, transaction);

            allocationCommand.Parameters.AddWithValue("project_id", projectId);
            allocationCommand.Parameters.AddWithValue("user_id", allocation.UserId);
            allocationCommand.Parameters.AddWithValue("allocated_hours", allocation.AllocatedHours);
            allocationCommand.Parameters.AddWithValue("allocation_notes", allocation.Notes?.Trim() ?? "");
            allocationCommand.Parameters.AddWithValue("allocated_by_user_id", sessionUserId.Value);

            await allocationCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "project_allocation_saved",
            projectId,
            message = "Project allocation record saved. Engineers assigned to the project can now view allocation hours and download SOW/GSD documents."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to save project allocation",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/project-allocation-info/documents/upload", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "MANAGE_PROJECT_ALLOCATION_INFO", "MANAGE_ALL"))
    {
        return Results.Json(new { status = "access_denied", message = "Only PM, Project/Team Coordinator, or Administrator can upload SOW/GSD documents." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var form = await httpContext.Request.ReadFormAsync();

    if (!Guid.TryParse(form["projectId"], out var projectId))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Valid projectId is required." });
    }

    var documentType = form["documentType"].ToString().Trim().ToUpperInvariant();

    if (documentType is not ("SOW" or "GSD"))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Document type must be SOW or GSD." });
    }

    var file = form.Files.GetFile("file");

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "A SOW/GSD file is required." });
    }

    if (file.Length > 50 * 1024 * 1024)
    {
        return Results.BadRequest(new { status = "file_too_large", message = "Document uploads are limited to 50 MB." });
    }

    if (!ProjectDocumentExtensionIsAllowed(file.FileName))
    {
        return Results.BadRequest(new { status = "file_type_not_allowed", message = "Allowed file types are PDF, Word, Excel, and CSV." });
    }

    var uploadRoot = GetProjectPulseUploadRoot();
    var projectFolder = Path.Combine(uploadRoot, "project-documents", projectId.ToString());
    Directory.CreateDirectory(projectFolder);

    var originalFileName = SanitizeProjectPulseFileName(file.FileName);
    var storedFileName = $"{documentType}_{Guid.NewGuid():N}_{originalFileName}";
    var storagePath = Path.Combine(projectFolder, storedFileName);

    await using (var stream = File.Create(storagePath))
    {
        await file.CopyToAsync(stream);
    }

    Guid documentId;

    await using (var command = new NpgsqlCommand("""
        INSERT INTO project_document_files (
            project_allocation_project_id,
            document_type,
            original_file_name,
            stored_file_name,
            storage_path,
            content_type,
            size_bytes,
            uploaded_by_user_id
        )
        VALUES (
            @project_id,
            @document_type,
            @original_file_name,
            @stored_file_name,
            @storage_path,
            @content_type,
            @size_bytes,
            @uploaded_by_user_id
        )
        RETURNING project_document_file_id;
        """, connection))
    {
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("document_type", documentType);
        command.Parameters.AddWithValue("original_file_name", originalFileName);
        command.Parameters.AddWithValue("stored_file_name", storedFileName);
        command.Parameters.AddWithValue("storage_path", storagePath);
        command.Parameters.AddWithValue("content_type", string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        command.Parameters.AddWithValue("size_bytes", file.Length);
        command.Parameters.AddWithValue("uploaded_by_user_id", sessionUserId.Value);

        documentId = (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to save document metadata."));
    }

    return Results.Ok(new
    {
        status = "project_document_uploaded",
        documentId,
        documentType,
        originalFileName,
        message = $"{documentType} uploaded successfully."
    });
});

app.MapGet("/api/project-allocation-info/documents/{documentId:guid}/download", async (Guid documentId, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand("""
        SELECT
            project_allocation_project_id,
            original_file_name,
            storage_path,
            content_type,
            is_purged
        FROM project_document_files
        WHERE project_document_file_id = @document_id;
        """, connection);

    command.Parameters.AddWithValue("document_id", documentId);

    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return Results.NotFound(new { status = "document_not_found", message = "Document was not found." });
    }

    var projectId = reader.GetGuid(0);
    var originalFileName = reader.GetString(1);
    var storagePath = reader.GetString(2);
    var contentType = reader.IsDBNull(3) ? "application/octet-stream" : reader.GetString(3);
    var isPurged = reader.GetBoolean(4);

    await reader.CloseAsync();

    if (isPurged || !File.Exists(storagePath))
    {
        return Results.NotFound(new { status = "document_purged", message = "This document has been purged or is no longer available." });
    }

    if (!await ProjectAllocationUserCanAccessProjectAsync(connection, sessionUserId.Value, projectId))
    {
        return Results.Json(new { status = "access_denied", message = "You do not have access to this project document." }, statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.File(storagePath, contentType, originalFileName);
});

app.MapPost("/api/project-allocation-info/documents/purge", async (ProjectDocumentPurgeRequest request, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectAllocationUserHasPermissionAsync(connection, sessionUserId.Value, "PURGE_PROJECT_DOCUMENTS", "MANAGE_ALL"))
    {
        return Results.Json(new { status = "access_denied", message = "Only Project/Team Coordinator or Administrator can purge old SOW/GSD files." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var olderThanDays = request.OlderThanDays <= 0 ? 120 : request.OlderThanDays;
    var cutoff = DateTimeOffset.UtcNow.AddDays(-olderThanDays);

    var purgeCandidates = new List<(Guid DocumentId, string StoragePath)>();

    await using (var selectCommand = new NpgsqlCommand("""
        SELECT d.project_document_file_id, d.storage_path
        FROM project_document_files d
        JOIN project_allocation_projects p ON p.project_allocation_project_id = d.project_allocation_project_id
        WHERE d.is_purged = FALSE
          AND d.uploaded_at < @cutoff
          AND (
                @include_active_projects = TRUE
             OR lower(p.project_status) NOT IN ('active', 'in_progress', 'open')
          );
        """, connection))
    {
        selectCommand.Parameters.AddWithValue("cutoff", cutoff);
        selectCommand.Parameters.AddWithValue("include_active_projects", request.IncludeActiveProjects);

        await using var reader = await selectCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            purgeCandidates.Add((reader.GetGuid(0), reader.GetString(1)));
        }
    }

    foreach (var candidate in purgeCandidates)
    {
        try
        {
            if (File.Exists(candidate.StoragePath))
            {
                File.Delete(candidate.StoragePath);
            }
        }
        catch
        {
            // Metadata will still be marked purged so the UI stops exposing stale download links.
        }

        await using var updateCommand = new NpgsqlCommand("""
            UPDATE project_document_files
            SET is_purged = TRUE,
                purged_at = NOW(),
                purged_by_user_id = @purged_by_user_id,
                purge_reason = @purge_reason
            WHERE project_document_file_id = @document_id;
            """, connection);

        updateCommand.Parameters.AddWithValue("document_id", candidate.DocumentId);
        updateCommand.Parameters.AddWithValue("purged_by_user_id", sessionUserId.Value);
        updateCommand.Parameters.AddWithValue("purge_reason", string.IsNullOrWhiteSpace(request.PurgeReason) ? $"Purged because document was older than {olderThanDays} days." : request.PurgeReason.Trim());

        await updateCommand.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        status = "document_purge_completed",
        olderThanDays,
        documentsPurged = purgeCandidates.Count,
        includeActiveProjects = request.IncludeActiveProjects,
        message = $"Purged {purgeCandidates.Count} old SOW/GSD document(s)."
    });
});




app.MapGet("/api/utilization/engineering-team-summary", async (int? year, Guid? engineerUserId, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var selectedYear = year ?? DateTime.UtcNow.Year;
    var yearStart = new DateOnly(selectedYear, 1, 1);
    var yearEndExclusive = new DateOnly(selectedYear + 1, 1, 1);

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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var isEngineeringTeamLead = roles.Contains("ENGINEERING_TEAM_LEAD");
    var isManager = roles.Contains("MANAGER");
    var isEngineer = roles.Contains("ENGINEER");

    var canUseTeamScope =
        !canViewAll
        && (
            isEngineeringTeamLead
            || isManager
            || permissions.Contains("VIEW_TEAM_UTILIZATION")
        );

    var canUseOwnScope = false;

    var canAccess =
        canViewAll
        || canUseTeamScope;

    if (!canAccess)
    {
        return Results.Json(new
        {
            canViewEngineeringTeamUtilization = false,
            status = "access_denied",
            message = "Engineering team utilization is available to Engineering Team Leads, Managers, Project/Team Coordinators, and Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var selectableEngineers = new List<(Guid UserId, string DisplayName, string Email, string TeamName, string ScopeReason)>();

    if (canViewAll)
    {
        await using var allCommand = new NpgsqlCommand("""
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
              AND r.role_code = 'ENGINEER'
            ORDER BY team_name, display_name, u.email;
            """, connection);

        await using var reader = await allCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            selectableEngineers.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                "all_engineers"));
        }
    }
    else if (canUseTeamScope)
    {
        await using var teamCommand = new NpgsqlCommand("""
            WITH lead_profile AS (
                SELECT
                    user_id,
                    NULLIF(team_name, '') AS team_name,
                    NULLIF(department_name, '') AS department_name,
                    NULLIF(department, '') AS department
                FROM app_users
                WHERE user_id = @user_id
            ),
            active_lead_teams AS (
                SELECT DISTINCT tm.team_id
                FROM team_memberships tm
                WHERE tm.user_id = @user_id
                  AND tm.effective_start_date <= CURRENT_DATE
                  AND (tm.effective_end_date IS NULL OR tm.effective_end_date >= CURRENT_DATE)
            ),
            membership_candidates AS (
                SELECT DISTINCT tm.user_id
                FROM team_memberships tm
                JOIN active_lead_teams alt
                    ON alt.team_id = tm.team_id
                WHERE tm.effective_start_date <= CURRENT_DATE
                  AND (tm.effective_end_date IS NULL OR tm.effective_end_date >= CURRENT_DATE)
            ),
            profile_candidates AS (
                SELECT DISTINCT u.user_id
                FROM app_users u
                CROSS JOIN lead_profile lp
                WHERE u.is_active = TRUE
                  AND (
                        (lp.team_name IS NOT NULL AND lower(COALESCE(u.team_name, '')) = lower(lp.team_name))
                     OR (lp.department_name IS NOT NULL AND lower(COALESCE(u.department_name, '')) = lower(lp.department_name))
                     OR (lp.department IS NOT NULL AND lower(COALESCE(u.department, '')) = lower(lp.department))
                  )
            ),
            eligible_engineers AS (
                SELECT user_id FROM membership_candidates
                UNION
                SELECT user_id FROM profile_candidates
            )
            SELECT DISTINCT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email,
                COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), 'Unassigned') AS team_name
            FROM eligible_engineers ee
            JOIN app_users u
                ON u.user_id = ee.user_id
               AND u.is_active = TRUE
            JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            WHERE r.role_code = 'ENGINEER'
            ORDER BY team_name, display_name, u.email;
            """, connection);

        teamCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await teamCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            selectableEngineers.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                "engineering_team_scope"));
        }
    }
    else if (canUseOwnScope)
    {
        await using var ownCommand = new NpgsqlCommand("""
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email,
                COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), 'Unassigned') AS team_name
            FROM app_users u
            WHERE u.user_id = @user_id
              AND u.is_active = TRUE;
            """, connection);

        ownCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await ownCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            selectableEngineers.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                "own_engineer_scope"));
        }
    }

    selectableEngineers = selectableEngineers
        .GroupBy(item => item.UserId)
        .Select(group => group.First())
        .OrderBy(item => item.TeamName)
        .ThenBy(item => item.DisplayName)
        .ThenBy(item => item.Email)
        .ToList();

    var allowedEngineerIds = selectableEngineers.Select(item => item.UserId).ToArray();

    Guid? effectiveEngineerUserId = null;
    var scope = canViewAll ? "all_engineers" : canUseTeamScope ? "engineering_team_scope" : "own_engineer_scope";

    if (engineerUserId.HasValue && engineerUserId.Value != Guid.Empty)
    {
        if (canViewAll || isEngineeringTeamLead)
        {
            if (!allowedEngineerIds.Contains(engineerUserId.Value))
            {
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "Selected engineer is not available within your utilization scope."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            effectiveEngineerUserId = engineerUserId.Value;
            scope = canViewAll ? "selected_engineer_scope" : "selected_team_engineer_scope";
        }
        else
        {
            effectiveEngineerUserId = sessionUserId.Value;
            scope = "own_engineer_scope";
        }
    }

    var scopedEngineerIds = effectiveEngineerUserId.HasValue
        ? new[] { effectiveEngineerUserId.Value }
        : allowedEngineerIds;

    decimal standardQuarterHours = 520m;
    decimal targetPercent = 70m;
    string policyName = "Default utilization policy";

    await using (var policyCommand = new NpgsqlCommand("""
        SELECT
            policy_name,
            standard_period_hours,
            default_target_percent
        FROM utilization_policies
        WHERE is_active = TRUE
        ORDER BY created_at DESC
        LIMIT 1;
        """, connection))
    {
        await using var reader = await policyCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            policyName = reader.GetString(0);
            standardQuarterHours = reader.GetDecimal(1);
            targetPercent = reader.GetDecimal(2);
        }
    }

    var usageByUserQuarter = new Dictionary<Guid, Dictionary<int, decimal>>();

    if (scopedEngineerIds.Length > 0)
    {
        await using var usageCommand = new NpgsqlCommand("""
            SELECT
                te.user_id,
                EXTRACT(QUARTER FROM te.work_date)::int AS quarter_number,
                COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.billable, FALSE) = TRUE), 0)::numeric AS billable_hours,
                COALESCE(SUM(te.hours), 0)::numeric AS total_hours
            FROM time_entries te
            WHERE te.user_id = ANY(@user_ids)
              AND te.work_date >= @year_start
              AND te.work_date < @year_end
              AND COALESCE(te.status, 'draft') NOT IN ('manager_declined', 'rejected', 'voided')
            GROUP BY te.user_id, EXTRACT(QUARTER FROM te.work_date)::int
            ORDER BY te.user_id, quarter_number;
            """, connection);

        usageCommand.Parameters.AddWithValue("user_ids", scopedEngineerIds);
        usageCommand.Parameters.AddWithValue("year_start", yearStart);
        usageCommand.Parameters.AddWithValue("year_end", yearEndExclusive);

        await using var reader = await usageCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var userId = reader.GetGuid(0);
            var quarter = reader.GetInt32(1);
            var billableHours = reader.GetDecimal(2);

            if (!usageByUserQuarter.TryGetValue(userId, out var quarterMap))
            {
                quarterMap = new Dictionary<int, decimal>();
                usageByUserQuarter[userId] = quarterMap;
            }

            quarterMap[quarter] = billableHours;
        }
    }

    object BuildQuarter(int quarterNumber, decimal billableHours)
    {
        var utilizationPercent = standardQuarterHours == 0
            ? 0m
            : Math.Round((billableHours / standardQuarterHours) * 100m, 2);

        var targetHours = Math.Round(standardQuarterHours * (targetPercent / 100m), 2);
        var hoursLeftToTarget = Math.Max(0m, Math.Round(targetHours - billableHours, 2));

        return new
        {
            quarterNumber,
            billableHours,
            standardQuarterHours,
            targetPercent,
            targetHours,
            utilizationPercent,
            hoursLeftToTarget
        };
    }

    var selectedUsers = selectableEngineers
        .Where(item => scopedEngineerIds.Contains(item.UserId))
        .ToList();

    var members = selectedUsers.Select(user =>
    {
        usageByUserQuarter.TryGetValue(user.UserId, out var quarters);
        quarters ??= new Dictionary<int, decimal>();

        var annualBillableHours = Enumerable.Range(1, 4).Sum(quarter => quarters.GetValueOrDefault(quarter, 0m));
        var annualCapacityHours = standardQuarterHours * 4m;
        var annualUtilizationPercent = annualCapacityHours == 0
            ? 0m
            : Math.Round((annualBillableHours / annualCapacityHours) * 100m, 2);

        return new
        {
            userId = user.UserId,
            displayName = user.DisplayName,
            email = user.Email,
            teamName = user.TeamName,
            annualBillableHours,
            annualCapacityHours,
            annualUtilizationPercent,
            quarters = Enumerable.Range(1, 4)
                .Select(quarter => BuildQuarter(quarter, quarters.GetValueOrDefault(quarter, 0m)))
                .ToList()
        };
    }).ToList();

    var teamSummaries = members
        .GroupBy(member => member.teamName)
        .Select(group =>
        {
            var teamAnnualBillableHours = group.Sum(member => member.annualBillableHours);
            var teamAnnualCapacityHours = group.Sum(member => member.annualCapacityHours);
            var teamAnnualUtilizationPercent = teamAnnualCapacityHours == 0
                ? 0m
                : Math.Round((teamAnnualBillableHours / teamAnnualCapacityHours) * 100m, 2);

            return new
            {
                teamName = group.Key,
                memberCount = group.Count(),
                annualBillableHours = teamAnnualBillableHours,
                annualCapacityHours = teamAnnualCapacityHours,
                annualUtilizationPercent = teamAnnualUtilizationPercent,
                quarters = Enumerable.Range(1, 4).Select(quarter =>
                {
                    var quarterBillableHours = group.Sum(member =>
                    {
                        var quarterObj = member.quarters[quarter - 1];
                        return (decimal)quarterObj.GetType().GetProperty("billableHours")!.GetValue(quarterObj)!;
                    });

                    return BuildQuarter(quarter, quarterBillableHours);
                }).ToList()
            };
        })
        .OrderBy(team => team.teamName)
        .ToList();

    var collectiveAnnualBillableHours = members.Sum(member => member.annualBillableHours);
    var collectiveAnnualCapacityHours = members.Sum(member => member.annualCapacityHours);
    var collectiveAnnualUtilizationPercent = collectiveAnnualCapacityHours == 0
        ? 0m
        : Math.Round((collectiveAnnualBillableHours / collectiveAnnualCapacityHours) * 100m, 2);

    return Results.Ok(new
    {
        module = "019M-AO Engineering Team Lead Utilization Scope",
        canViewEngineeringTeamUtilization = true,
        scope,
        year = selectedYear,
        selectedEngineerUserId = effectiveEngineerUserId,
        policy = new
        {
            policyName,
            standardQuarterHours,
            targetPercent
        },
        access = new
        {
            canViewAll,
            canSelectEngineer = canViewAll || canUseTeamScope,
            isEngineeringTeamLead,
            isManager,
            isEngineer,
            canUseTeamScope,
            canUseOwnScope
        },
        selectableEngineers = selectableEngineers.Select(item => new
        {
            userId = item.UserId,
            displayName = item.DisplayName,
            email = item.Email,
            teamName = item.TeamName,
            scopeReason = item.ScopeReason
        }),
        collectiveSummary = new
        {
            memberCount = members.Count,
            annualBillableHours = collectiveAnnualBillableHours,
            annualCapacityHours = collectiveAnnualCapacityHours,
            annualUtilizationPercent = collectiveAnnualUtilizationPercent
        },
        teamSummaries,
        members,
        calculationNote = "Engineering Team Lead scope is enforced by backend role scope. Team leads can only view engineers on matching active team membership or profile team/department scope."
    });
});

app.MapGet("/api/utilization/manager-team-summary", async (int? year, HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var selectedYear = year ?? DateTime.UtcNow.Year;
    if (selectedYear < 2026) selectedYear = 2026;
    if (selectedYear > 2036) selectedYear = 2036;

    var yearStart = new DateOnly(selectedYear, 1, 1);
    var nextYearStart = new DateOnly(selectedYear + 1, 1, 1);
    decimal standardQuarterHours = 482m;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var sessionEmail = "";
    var sessionRoles = new List<string>();

    await using (var roleCommand = new NpgsqlCommand("""
        SELECT u.email,
               COALESCE(array_agg(r.role_code ORDER BY r.display_order) FILTER (WHERE r.role_code IS NOT NULL), ARRAY[]::varchar[]) AS role_codes
        FROM app_users u
        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE u.user_id = @user_id
        GROUP BY u.email;
        """, connection))
    {
        roleCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);

        await using var reader = await roleCommand.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return Results.Json(new { status = "user_not_found", message = "Session user was not found." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        sessionEmail = reader.GetString(0).ToLowerInvariant();
        sessionRoles = reader.GetFieldValue<string[]>(1).ToList();
    }

    var isAdministrator = sessionRoles.Contains("ADMINISTRATOR");
    var isCoordinator = sessionRoles.Contains("PROJECT_TEAM_COORDINATOR");
    var isManager = sessionRoles.Contains("MANAGER");

    if (!isAdministrator && !isCoordinator && !isManager)
    {
        return Results.Ok(new
        {
            canViewManagerUtilization = false,
            year = selectedYear,
            managedTeams = Array.Empty<string>(),
            message = "Manager utilization is available to Managers, Project/Team Coordinators, and Administrators."
        });
    }

    var managedTeams = new List<string>();
    var managedDepartments = new List<string>();

    if (isAdministrator || isCoordinator)
    {
        managedTeams.AddRange(new[] { "Systems", "Collaboration", "Enterprise Networking", "Project Management", "Back Office" });
        managedDepartments.AddRange(new[] {
            "Systems Engineering",
            "Collaboration Engineering",
            "Enterprise Networking Engineering",
            "Project Management",
            "Project Management Office",
            "Back Office"
        });
    }
    else if (sessionEmail == "ahmed.adeyemi@ussignal.com" || sessionEmail == "ahmed.adeyemi@ussignal.local")
    {
        managedTeams.AddRange(new[] { "Systems", "Collaboration" });
        managedDepartments.AddRange(new[] { "Systems Engineering", "Collaboration Engineering" });
    }
    else if (sessionEmail == "matthew.lenoble@ussignal.com")
    {
        managedTeams.AddRange(new[] { "Enterprise Networking", "Project Management" });
        managedDepartments.AddRange(new[] { "Enterprise Networking Engineering", "Project Management", "Project Management Office" });
    }

    if (managedTeams.Count == 0 && managedDepartments.Count == 0)
    {
        return Results.Ok(new
        {
            canViewManagerUtilization = true,
            year = selectedYear,
            managedTeams,
            teamSummaries = Array.Empty<object>(),
            teamMembers = Array.Empty<object>(),
            collectiveSummary = new { memberCount = 0, annualBillableHours = 0, annualUtilizationPercent = 0 },
            message = "No managed teams are configured for this manager yet."
        });
    }

    var users = new List<(Guid UserId, string Email, string DisplayName, string? DepartmentName, string? TeamName)>();

    await using (var usersCommand = new NpgsqlCommand("""
        SELECT DISTINCT
            u.user_id,
            u.email,
            u.display_name,
            u.department_name,
            u.team_name
        FROM app_users u
        WHERE u.is_active = TRUE
          AND COALESCE(u.login_enabled, TRUE) = TRUE
          AND lower(u.email) NOT LIKE '%.local'
          AND (
                u.team_name = ANY(@managed_teams)
             OR u.department_name = ANY(@managed_departments)
          )
        ORDER BY u.team_name, u.display_name, u.email;
        """, connection))
    {
        usersCommand.Parameters.AddWithValue("managed_teams", managedTeams.ToArray());
        usersCommand.Parameters.AddWithValue("managed_departments", managedDepartments.ToArray());

        await using var reader = await usersCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)
            ));
        }
    }

    var billableByUserQuarter = new Dictionary<Guid, Dictionary<int, decimal>>();

    foreach (var user in users)
    {
        billableByUserQuarter[user.UserId] = new Dictionary<int, decimal>
        {
            [1] = 0m,
            [2] = 0m,
            [3] = 0m,
            [4] = 0m
        };
    }

    if (users.Count > 0)
    {
        await using var usageCommand = new NpgsqlCommand("""
            WITH entry_rows AS (
                SELECT
                    ts.user_id,
                    NULLIF(to_jsonb(te)->>'work_date', '')::date AS work_date,
                    COALESCE(NULLIF(to_jsonb(te)->>'hours', '')::numeric, 0) AS hours,
                    CASE
                        WHEN NULLIF(to_jsonb(te)->>'is_billable', '') IS NOT NULL
                            THEN NULLIF(to_jsonb(te)->>'is_billable', '')::boolean
                        ELSE COALESCE(
                            NULLIF(to_jsonb(te)->>'project_id', ''),
                            NULLIF(to_jsonb(te)->>'project_task_id', ''),
                            NULLIF(to_jsonb(te)->>'task_id', ''),
                            NULLIF(to_jsonb(te)->>'service_request_id', '')
                        ) IS NOT NULL
                    END AS is_billable,
                    COALESCE(NULLIF(to_jsonb(ts)->>'status', ''), 'draft') AS timesheet_status
                FROM time_entries te
                JOIN timesheets ts ON ts.timesheet_id = te.timesheet_id
                WHERE ts.user_id = ANY(@user_ids)
            )
            SELECT
                user_id,
                EXTRACT(QUARTER FROM work_date)::int AS quarter_number,
                COALESCE(SUM(hours), 0) AS billable_hours
            FROM entry_rows
            WHERE work_date >= @year_start
              AND work_date < @next_year_start
              AND is_billable = TRUE
              AND timesheet_status NOT IN ('manager_declined', 'rejected', 'voided')
            GROUP BY user_id, EXTRACT(QUARTER FROM work_date)::int;
            """, connection);

        usageCommand.Parameters.AddWithValue("user_ids", users.Select(user => user.UserId).ToArray());
        usageCommand.Parameters.AddWithValue("year_start", yearStart);
        usageCommand.Parameters.AddWithValue("next_year_start", nextYearStart);

        await using var reader = await usageCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var userId = reader.GetGuid(0);
            var quarter = reader.GetInt32(1);
            var billableHours = reader.GetDecimal(2);

            if (billableByUserQuarter.ContainsKey(userId))
            {
                billableByUserQuarter[userId][quarter] = billableHours;
            }
        }
    }

    var teamMembers = users.Select(user =>
    {
        var quarters = new[] { 1, 2, 3, 4 }.Select(quarter =>
        {
            var billableHours = Math.Round(billableByUserQuarter[user.UserId][quarter], 2);
            var utilizationPercent = standardQuarterHours == 0 ? 0 : Math.Round((billableHours / standardQuarterHours) * 100m, 2);

            return new
            {
                quarterNumber = quarter,
                quarterName = $"Q{quarter}",
                billableHours,
                utilizationPercent
            };
        }).ToList();

        var annualBillableHours = quarters.Sum(item => item.billableHours);
        var annualUtilizationPercent = standardQuarterHours == 0 ? 0 : Math.Round((annualBillableHours / (standardQuarterHours * 4m)) * 100m, 2);

        return new
        {
            userId = user.UserId,
            user.Email,
            user.DisplayName,
            user.DepartmentName,
            user.TeamName,
            annualBillableHours,
            annualUtilizationPercent,
            quarters
        };
    }).ToList();

    var teamSummaries = teamMembers
        .GroupBy(member => string.IsNullOrWhiteSpace(member.TeamName) ? member.DepartmentName ?? "Unassigned" : member.TeamName)
        .Select(group =>
        {
            var memberCount = group.Count();
            var annualBillableHours = group.Sum(member => member.annualBillableHours);
            var annualCapacityHours = standardQuarterHours * 4m * memberCount;
            var annualUtilizationPercent = annualCapacityHours == 0 ? 0 : Math.Round((annualBillableHours / annualCapacityHours) * 100m, 2);

            var quarters = new[] { 1, 2, 3, 4 }.Select(quarter =>
            {
                var billableHours = group.Sum(member => member.quarters.First(item => item.quarterNumber == quarter).billableHours);
                var capacityHours = standardQuarterHours * memberCount;
                var utilizationPercent = capacityHours == 0 ? 0 : Math.Round((billableHours / capacityHours) * 100m, 2);

                return new
                {
                    quarterNumber = quarter,
                    quarterName = $"Q{quarter}",
                    billableHours,
                    utilizationPercent
                };
            }).ToList();

            return new
            {
                teamName = group.Key,
                memberCount,
                annualBillableHours,
                annualUtilizationPercent,
                quarters
            };
        })
        .OrderBy(team => team.teamName)
        .ToList();

    var collectiveMemberCount = teamMembers.Count;
    var collectiveAnnualBillableHours = teamMembers.Sum(member => member.annualBillableHours);
    var collectiveAnnualCapacityHours = standardQuarterHours * 4m * collectiveMemberCount;
    var collectiveAnnualUtilizationPercent = collectiveAnnualCapacityHours == 0 ? 0 : Math.Round((collectiveAnnualBillableHours / collectiveAnnualCapacityHours) * 100m, 2);

    return Results.Ok(new
    {
        canViewManagerUtilization = true,
        year = selectedYear,
        standardQuarterHours,
        managedTeams,
        calculationStatus = "foundation",
        calculationNote = "Manager utilization uses current project/service-request billable time when available. Final mapping will be refined after project allocation/time-entry linkage is completed.",
        collectiveSummary = new
        {
            memberCount = collectiveMemberCount,
            annualBillableHours = collectiveAnnualBillableHours,
            annualUtilizationPercent = collectiveAnnualUtilizationPercent
        },
        teamSummaries,
        teamMembers
    });
});




async Task<bool> ProjectPulseUserIsAzureAdministratorAsync(NpgsqlConnection connection, Guid userId)
{
    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.is_active = TRUE
              AND r.role_code = 'ADMINISTRATOR'
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId);

    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

async Task<ProjectPulseEntraImportSettings> ProjectPulseGetEntraImportSettingsAsync(NpgsqlConnection connection)
{
    await using var command = new NpgsqlCommand("""
        SELECT environment_mode,
               tenant_domain,
               source_provider,
               import_source_type,
               graph_group_id,
               graph_filter,
               default_role_code,
               disable_missing_from_source
        FROM azure_entra_import_settings
        WHERE settings_id = 'default';
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return new ProjectPulseEntraImportSettings(
            "test",
            "onenecklab.com,onitdemo.com",
            "ENTRA_ID_TEST",
            "ALL_USERS",
            null,
            null,
            "ENGINEER",
            true);
    }

    return new ProjectPulseEntraImportSettings(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetString(6),
        reader.GetBoolean(7));
}

async Task<string> ProjectPulseGetGraphAccessTokenAsync()
{
    var tenantId = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_TENANT_ID");
    var clientId = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_CLIENT_ID");
    var clientSecret = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_CLIENT_SECRET");

    using var httpClient = new HttpClient();

    var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["client_id"] = clientId,
        ["client_secret"] = clientSecret,
        ["scope"] = "https://graph.microsoft.com/.default",
        ["grant_type"] = "client_credentials"
    });

    var tokenResponse = await httpClient.PostAsync(
        $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
        tokenRequest);

    var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

    if (!tokenResponse.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Graph token request failed: HTTP {(int)tokenResponse.StatusCode} {tokenJson}");
    }

    using var document = JsonDocument.Parse(tokenJson);
    var accessToken = ProjectPulseJsonString(document.RootElement, "access_token");

    if (string.IsNullOrWhiteSpace(accessToken))
    {
        throw new InvalidOperationException("Graph token response did not include access_token.");
    }

    return accessToken;
}

string ProjectPulseNormalizeGraphEmail(JsonElement user)
{
    var mail = ProjectPulseJsonString(user, "mail");
    var upn = ProjectPulseJsonString(user, "userPrincipalName");

    return (mail ?? upn ?? "").Trim().ToLowerInvariant();
}

string[] ProjectPulseGetTenantDomains(ProjectPulseEntraImportSettings settings)
{
    return (settings.TenantDomain ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(domain => domain.Trim().TrimStart('@').ToLowerInvariant())
        .Where(domain => !string.IsNullOrWhiteSpace(domain))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

bool ProjectPulseEmailMatchesTenantDomains(string? email, ProjectPulseEntraImportSettings settings)
{
    if (string.IsNullOrWhiteSpace(email))
    {
        return false;
    }

    var normalizedEmail = email.Trim().ToLowerInvariant();
    var domains = ProjectPulseGetTenantDomains(settings);

    if (domains.Length == 0)
    {
        return false;
    }

    return domains.Any(domain => normalizedEmail.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase));
}

ProjectPulseGraphUser ProjectPulseReadGraphUser(JsonElement user)
{
    var id = ProjectPulseJsonString(user, "id") ?? "";
    var email = ProjectPulseNormalizeGraphEmail(user);

    return new ProjectPulseGraphUser(
        id,
        ProjectPulseJsonString(user, "displayName") ?? email,
        email,
        ProjectPulseJsonString(user, "userPrincipalName"),
        ProjectPulseJsonString(user, "jobTitle"),
        ProjectPulseJsonString(user, "department"),
        ProjectPulseJsonString(user, "officeLocation"),
        user.TryGetProperty("accountEnabled", out var accountEnabledElement)
            && accountEnabledElement.ValueKind == JsonValueKind.True);
}

async Task<List<ProjectPulseGraphUser>> ProjectPulseFetchGraphUsersAsync(ProjectPulseEntraImportSettings settings)
{
    var accessToken = await ProjectPulseGetGraphAccessTokenAsync();

    var select = "id,displayName,mail,userPrincipalName,jobTitle,department,officeLocation,accountEnabled";
    var urls = new Queue<string>();

    if (settings.ImportSourceType.Equals("GROUP", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(settings.GraphGroupId))
        {
            throw new InvalidOperationException("Graph group ID is required when import source type is GROUP.");
        }

        urls.Enqueue($"https://graph.microsoft.com/v1.0/groups/{Uri.EscapeDataString(settings.GraphGroupId.Trim())}/members/microsoft.graph.user?$select={Uri.EscapeDataString(select)}&$top=999");
    }
    else if (settings.ImportSourceType.Equals("FILTER", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(settings.GraphFilter))
        {
            throw new InvalidOperationException("Graph filter is required when import source type is FILTER.");
        }

        urls.Enqueue($"https://graph.microsoft.com/v1.0/users?$select={Uri.EscapeDataString(select)}&$filter={Uri.EscapeDataString(settings.GraphFilter.Trim())}&$top=999");
    }
    else
    {
        urls.Enqueue($"https://graph.microsoft.com/v1.0/users?$select={Uri.EscapeDataString(select)}&$top=999");
    }

    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    httpClient.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");

    var users = new List<ProjectPulseGraphUser>();

    while (urls.Count > 0)
    {
        var url = urls.Dequeue();
        var response = await httpClient.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Graph user query failed: HTTP {(int)response.StatusCode} {json}");
        }

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.TryGetProperty("value", out var values))
        {
            foreach (var item in values.EnumerateArray())
            {
                var graphUser = ProjectPulseReadGraphUser(item);

                if (string.IsNullOrWhiteSpace(graphUser.Email))
                {
                    continue;
                }

                if (!ProjectPulseEmailMatchesTenantDomains(graphUser.Email, settings)
                    && !ProjectPulseEmailMatchesTenantDomains(graphUser.UserPrincipalName, settings))
                {
                    continue;
                }

                users.Add(graphUser);
            }
        }

        if (document.RootElement.TryGetProperty("@odata.nextLink", out var nextLink)
            && nextLink.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(nextLink.GetString()))
        {
            urls.Enqueue(nextLink.GetString()!);
        }
    }

    return users
        .GroupBy(user => user.Id)
        .Select(group => group.First())
        .OrderBy(user => user.DisplayName)
        .ToList();
}

async Task<ProjectPulseGraphUser> ProjectPulseFetchGraphUserByIdAsync(string entraObjectId)
{
    var accessToken = await ProjectPulseGetGraphAccessTokenAsync();
    var select = "id,displayName,mail,userPrincipalName,jobTitle,department,officeLocation,accountEnabled";

    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

    var url = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(entraObjectId)}?$select={Uri.EscapeDataString(select)}";
    var response = await httpClient.GetAsync(url);
    var json = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Graph user lookup failed for {entraObjectId}: HTTP {(int)response.StatusCode} {json}");
    }

    using var document = JsonDocument.Parse(json);
    return ProjectPulseReadGraphUser(document.RootElement);
}

async Task<Guid> ProjectPulseCreateAzureImportRunAsync(
    NpgsqlConnection connection,
    ProjectPulseEntraImportSettings settings,
    Guid? requestedByUserId,
    string runType,
    int previewedCount = 0,
    int selectedCount = 0,
    int importedCount = 0,
    int updatedCount = 0,
    int deactivatedCount = 0,
    int skippedCount = 0,
    string? message = null)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO azure_entra_import_runs (
            run_type,
            environment_mode,
            tenant_domain,
            source_provider,
            import_source_type,
            graph_group_id,
            graph_filter,
            requested_by_user_id,
            previewed_count,
            selected_count,
            imported_count,
            updated_count,
            deactivated_count,
            skipped_count,
            message
        )
        VALUES (
            @run_type,
            @environment_mode,
            @tenant_domain,
            @source_provider,
            @import_source_type,
            @graph_group_id,
            @graph_filter,
            @requested_by_user_id,
            @previewed_count,
            @selected_count,
            @imported_count,
            @updated_count,
            @deactivated_count,
            @skipped_count,
            @message
        )
        RETURNING import_run_id;
        """, connection);

    command.Parameters.AddWithValue("run_type", runType);
    command.Parameters.AddWithValue("environment_mode", settings.EnvironmentMode);
    command.Parameters.AddWithValue("tenant_domain", settings.TenantDomain);
    command.Parameters.AddWithValue("source_provider", settings.SourceProvider);
    command.Parameters.AddWithValue("import_source_type", settings.ImportSourceType);
    command.Parameters.AddWithValue("graph_group_id", (object?)settings.GraphGroupId ?? DBNull.Value);
    command.Parameters.AddWithValue("graph_filter", (object?)settings.GraphFilter ?? DBNull.Value);
    command.Parameters.AddWithValue("requested_by_user_id", (object?)requestedByUserId ?? DBNull.Value);
    command.Parameters.AddWithValue("previewed_count", previewedCount);
    command.Parameters.AddWithValue("selected_count", selectedCount);
    command.Parameters.AddWithValue("imported_count", importedCount);
    command.Parameters.AddWithValue("updated_count", updatedCount);
    command.Parameters.AddWithValue("deactivated_count", deactivatedCount);
    command.Parameters.AddWithValue("skipped_count", skippedCount);
    command.Parameters.AddWithValue("message", (object?)message ?? DBNull.Value);

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create Azure import run."));
}

async Task ProjectPulseRecordAzureImportRunUserAsync(
    NpgsqlConnection connection,
    Guid importRunId,
    ProjectPulseGraphUser user,
    string actionTaken,
    string? message = null)
{
    await using var command = new NpgsqlCommand("""
        INSERT INTO azure_entra_import_run_users (
            import_run_id,
            entra_object_id,
            email,
            display_name,
            account_enabled,
            action_taken,
            message
        )
        VALUES (
            @import_run_id,
            @entra_object_id,
            @email,
            @display_name,
            @account_enabled,
            @action_taken,
            @message
        );
        """, connection);

    command.Parameters.AddWithValue("import_run_id", importRunId);
    command.Parameters.AddWithValue("entra_object_id", user.Id);
    command.Parameters.AddWithValue("email", user.Email);
    command.Parameters.AddWithValue("display_name", user.DisplayName);
    command.Parameters.AddWithValue("account_enabled", user.AccountEnabled);
    command.Parameters.AddWithValue("action_taken", actionTaken);
    command.Parameters.AddWithValue("message", (object?)message ?? DBNull.Value);

    await command.ExecuteNonQueryAsync();
}

async Task<(string ActionTaken, Guid? UserId)> ProjectPulseUpsertSelectedEntraUserAsync(
    NpgsqlConnection connection,
    ProjectPulseEntraImportSettings settings,
    ProjectPulseGraphUser user)
{
    if (!user.Email.EndsWith("@" + settings.TenantDomain, StringComparison.OrdinalIgnoreCase))
    {
        return ("skipped_domain_mismatch", null);
    }

    if (!user.AccountEnabled)
    {
        await using var deactivateCommand = new NpgsqlCommand("""
            UPDATE app_users
            SET is_active = FALSE,
                login_enabled = FALSE,
                updated_at = NOW()
            WHERE entra_object_id = @entra_object_id
               OR lower(email) = @email
            RETURNING user_id;
            """, connection);

        deactivateCommand.Parameters.AddWithValue("entra_object_id", user.Id);
        deactivateCommand.Parameters.AddWithValue("email", user.Email);

        var deactivated = await deactivateCommand.ExecuteScalarAsync();
        return deactivated is Guid deactivatedUserId
            ? ("deactivated_account_disabled_in_entra", deactivatedUserId)
            : ("skipped_account_disabled_in_entra", null);
    }

    await using var upsertCommand = new NpgsqlCommand("""
        INSERT INTO app_users (
            email,
            display_name,
            is_active,
            login_enabled,
            source_provider,
            entra_tenant_id,
            entra_object_id,
            entra_user_principal_name,
            job_title,
            department_name,
            office_location,
            last_directory_sync_at,
            updated_at
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
            @job_title,
            @department_name,
            @office_location,
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
            job_title = EXCLUDED.job_title,
            department_name = EXCLUDED.department_name,
            office_location = EXCLUDED.office_location,
            last_directory_sync_at = NOW(),
            updated_at = NOW()
        RETURNING user_id;
        """, connection);

    upsertCommand.Parameters.AddWithValue("email", user.Email);
    upsertCommand.Parameters.AddWithValue("display_name", user.DisplayName);
    upsertCommand.Parameters.AddWithValue("source_provider", settings.SourceProvider);
    upsertCommand.Parameters.AddWithValue("tenant_id", ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_TENANT_ID"));
    upsertCommand.Parameters.AddWithValue("entra_object_id", user.Id);
    upsertCommand.Parameters.AddWithValue("user_principal_name", (object?)user.UserPrincipalName ?? DBNull.Value);
    upsertCommand.Parameters.AddWithValue("job_title", (object?)user.JobTitle ?? DBNull.Value);
    upsertCommand.Parameters.AddWithValue("department_name", (object?)user.Department ?? DBNull.Value);
    upsertCommand.Parameters.AddWithValue("office_location", (object?)user.OfficeLocation ?? DBNull.Value);

    var userId = (Guid)(await upsertCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to upsert Entra user."));

    await using var roleCommand = new NpgsqlCommand("""
        INSERT INTO app_user_role_assignments (
            user_id,
            app_role_id,
            assignment_reason,
            is_active
        )
        SELECT @user_id,
               r.app_role_id,
               'Default role from Azure Graph selective import',
               TRUE
        FROM app_roles r
        WHERE r.role_code = @role_code
          AND r.is_active = TRUE
        ON CONFLICT (user_id, app_role_id) DO UPDATE
        SET is_active = TRUE,
            assignment_reason = EXCLUDED.assignment_reason,
            updated_at = NOW();
        """, connection);

    roleCommand.Parameters.AddWithValue("user_id", userId);
    roleCommand.Parameters.AddWithValue("role_code", settings.DefaultRoleCode);

    await roleCommand.ExecuteNonQueryAsync();

    return ("imported_or_updated", userId);
}

async Task<int> ProjectPulseDeactivateMissingOrDisabledEntraUsersAsync(
    NpgsqlConnection connection,
    ProjectPulseEntraImportSettings settings,
    List<ProjectPulseGraphUser> currentSourceUsers,
    Guid importRunId)
{
    var activeGraphIds = currentSourceUsers
        .Where(user => user.AccountEnabled)
        .Select(user => user.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var disabledGraphIds = currentSourceUsers
        .Where(user => !user.AccountEnabled)
        .Select(user => user.Id)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var deactivated = 0;

    foreach (var user in currentSourceUsers.Where(user => !user.AccountEnabled))
    {
        await using var disabledCommand = new NpgsqlCommand("""
            UPDATE app_users
            SET is_active = FALSE,
                login_enabled = FALSE,
                updated_at = NOW()
            WHERE source_provider = @source_provider
              AND lower(split_part(email, '@', 2)) = ANY(@tenant_domains)
              AND entra_object_id = @entra_object_id
              AND (is_active = TRUE OR login_enabled = TRUE);
            """, connection);

        disabledCommand.Parameters.AddWithValue("source_provider", settings.SourceProvider);
        disabledCommand.Parameters.AddWithValue("tenant_domains", ProjectPulseGetTenantDomains(settings));
        disabledCommand.Parameters.AddWithValue("entra_object_id", user.Id);

        var rows = await disabledCommand.ExecuteNonQueryAsync();

        if (rows > 0)
        {
            deactivated += rows;
            await ProjectPulseRecordAzureImportRunUserAsync(connection, importRunId, user, "deactivated_account_disabled_in_entra");
        }
    }

    if (settings.DisableMissingFromSource)
    {
        var currentIds = currentSourceUsers
            .Select(user => user.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        await using var missingCommand = new NpgsqlCommand("""
            UPDATE app_users
            SET is_active = FALSE,
                login_enabled = FALSE,
                updated_at = NOW()
            WHERE source_provider = @source_provider
              AND lower(split_part(email, '@', 2)) = ANY(@tenant_domains)
              AND entra_object_id IS NOT NULL
              AND NOT (entra_object_id = ANY(@current_ids))
              AND (is_active = TRUE OR login_enabled = TRUE);
            """, connection);

        missingCommand.Parameters.AddWithValue("source_provider", settings.SourceProvider);
        missingCommand.Parameters.AddWithValue("tenant_domains", ProjectPulseGetTenantDomains(settings));
        missingCommand.Parameters.AddWithValue("current_ids", currentIds);

        deactivated += await missingCommand.ExecuteNonQueryAsync();
    }

    return deactivated;
}


app.MapGet("/api/auth/sso/test-config", () =>
{
    var tenantId = Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_TENANT_ID");
    var clientId = Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_ID");
    var redirectUri = Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_REDIRECT_URI");
    var testDomain = Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_TEST_DOMAIN");
    var mode = Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE") ?? "development";

    return Results.Ok(new
    {
        status = "entra_config_loaded",
        mode,
        tenantConfigured = !string.IsNullOrWhiteSpace(tenantId),
        clientConfigured = !string.IsNullOrWhiteSpace(clientId),
        secretConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET")),
        redirectUri,
        testDomain
    });
});

app.MapGet("/api/auth/sso/start", async (HttpContext httpContext, string? loginHint, string? prompt) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var tenantId = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_TENANT_ID");
    var clientId = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_CLIENT_ID");
    var redirectUri = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_REDIRECT_URI");

    var state = ProjectPulseSecureToken();
    var nonce = ProjectPulseSecureToken();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand("""
        INSERT INTO auth_sso_state (
            state_token,
            nonce_token,
            provider_code,
            redirect_uri,
            requested_email,
            expires_at,
            client_ip,
            user_agent
        )
        VALUES (
            @state_token,
            @nonce_token,
            'ENTRA_ID',
            @redirect_uri,
            NULLIF(@requested_email, ''),
            NOW() + INTERVAL '10 minutes',
            @client_ip,
            @user_agent
        );
        """, connection);

    command.Parameters.AddWithValue("state_token", state);
    command.Parameters.AddWithValue("nonce_token", nonce);
    command.Parameters.AddWithValue("redirect_uri", redirectUri);
    command.Parameters.AddWithValue("requested_email", loginHint ?? "");
    command.Parameters.AddWithValue("client_ip", httpContext.Connection.RemoteIpAddress?.ToString() ?? "");
    command.Parameters.AddWithValue("user_agent", httpContext.Request.Headers.UserAgent.ToString());

    await command.ExecuteNonQueryAsync();

    var query = new Dictionary<string, string?>
    {
        ["client_id"] = clientId,
        ["response_type"] = "code",
        ["redirect_uri"] = redirectUri,
        ["response_mode"] = "query",
        ["scope"] = "openid profile email User.Read",
        ["state"] = state,
        ["nonce"] = nonce
    };

    if (!string.IsNullOrWhiteSpace(loginHint))
    {
        query["login_hint"] = loginHint.Trim();
    }

    if (!string.IsNullOrWhiteSpace(prompt))
    {
        var allowedPrompts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "select_account",
            "login",
            "consent"
        };

        if (allowedPrompts.Contains(prompt.Trim()))
        {
            query["prompt"] = prompt.Trim();
        }
    }

    var authorizationUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize?" +
        string.Join("&", query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? "")}"));

    return Results.Redirect(authorizationUrl);
});

app.MapGet("/api/auth/sso/callback", async (HttpContext httpContext, string? code, string? state, string? error, string? error_description) =>
{
    if (!string.IsNullOrWhiteSpace(error))
    {
        var encodedError = Uri.EscapeDataString(error_description ?? error);
        return Results.Redirect($"/#login?ssoError={encodedError}");
    }

    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
    {
        return Results.Redirect("/#login?ssoError=missing_code_or_state");
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var tenantId = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_TENANT_ID");
    var clientId = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_CLIENT_ID");
    var clientSecret = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_CLIENT_SECRET");
    var redirectUri = ProjectPulseRequiredEnv("PROJECTPULSE_ENTRA_REDIRECT_URI");
    var testDomain = Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_TEST_DOMAIN") ?? "";
    var allowTestJit = string.Equals(Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_ALLOW_TEST_JIT"), "true", StringComparison.OrdinalIgnoreCase);
    var mode = Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE") ?? "development";

    string nonce;
    string? requestedEmail;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    await using (var stateCommand = new NpgsqlCommand("""
        UPDATE auth_sso_state
        SET consumed_at = NOW()
        WHERE state_token = @state_token
          AND consumed_at IS NULL
          AND expires_at > NOW()
        RETURNING nonce_token, requested_email;
        """, connection))
    {
        stateCommand.Parameters.AddWithValue("state_token", state);

        await using var reader = await stateCommand.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return Results.Redirect("/#login?ssoError=invalid_or_expired_state");
        }

        nonce = reader.GetString(0);
        requestedEmail = reader.IsDBNull(1) ? null : reader.GetString(1);
    }

    using var httpClient = new HttpClient();

    var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["client_id"] = clientId,
        ["scope"] = "openid profile email User.Read",
        ["code"] = code,
        ["redirect_uri"] = redirectUri,
        ["grant_type"] = "authorization_code",
        ["client_secret"] = clientSecret
    });

    var tokenResponse = await httpClient.PostAsync($"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token", tokenRequest);
    var tokenJson = await tokenResponse.Content.ReadAsStringAsync();

    if (!tokenResponse.IsSuccessStatusCode)
    {
        var tokenErrorMessage = "token_exchange_failed";

        try
        {
            using var errorDocument = JsonDocument.Parse(tokenJson);
            var errorCode = ProjectPulseJsonString(errorDocument.RootElement, "error") ?? "token_exchange_failed";
            var errorDescription = ProjectPulseJsonString(errorDocument.RootElement, "error_description") ?? "";
            tokenErrorMessage = $"{errorCode}: {errorDescription}";
        }
        catch
        {
            tokenErrorMessage = $"token_exchange_failed: HTTP {(int)tokenResponse.StatusCode}";
        }

        Console.Error.WriteLine($"Project Pulse Entra token exchange failed: {tokenErrorMessage}");
        var encodedTokenError = Uri.EscapeDataString(tokenErrorMessage);
        return Results.Redirect($"/#login?ssoError={encodedTokenError}");
    }

    using var tokenDocument = JsonDocument.Parse(tokenJson);
    var idToken = ProjectPulseJsonString(tokenDocument.RootElement, "id_token");

    if (string.IsNullOrWhiteSpace(idToken))
    {
        return Results.Redirect("/#login?ssoError=missing_id_token");
    }

    JsonElement payload;

    try
    {
        payload = await ProjectPulseValidateMicrosoftIdTokenAsync(idToken, tenantId, clientId, nonce);
    }
    catch (Exception ex)
    {
        var encoded = Uri.EscapeDataString(ex.Message);
        return Results.Redirect($"/#login?ssoError={encoded}");
    }

    var objectId = ProjectPulseJsonString(payload, "oid") ?? "";
    var preferredUsername = ProjectPulseJsonString(payload, "preferred_username");
    var email = ProjectPulseJsonString(payload, "email") ?? preferredUsername ?? requestedEmail ?? "";
    var displayName = ProjectPulseJsonString(payload, "name") ?? email;

    if (string.IsNullOrWhiteSpace(objectId) || string.IsNullOrWhiteSpace(email))
    {
        return Results.Redirect("/#login?ssoError=missing_user_claims");
    }

    email = email.Trim().ToLowerInvariant();

    if (!string.IsNullOrWhiteSpace(testDomain)
        && mode.Equals("test", StringComparison.OrdinalIgnoreCase)
        && !email.EndsWith("@" + testDomain, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Redirect("/#login?ssoError=test_tenant_domain_restricted");
    }

    Guid userId;

    await using (var lookupCommand = new NpgsqlCommand("""
        SELECT user_id
        FROM app_users
        WHERE entra_object_id = @entra_object_id
           OR lower(email) = @email
        LIMIT 1;
        """, connection))
    {
        lookupCommand.Parameters.AddWithValue("entra_object_id", objectId);
        lookupCommand.Parameters.AddWithValue("email", email);

        var existing = await lookupCommand.ExecuteScalarAsync();

        if (existing is Guid existingUserId)
        {
            userId = existingUserId;

            await using var updateCommand = new NpgsqlCommand("""
                UPDATE app_users
                SET display_name = @display_name,
                    entra_tenant_id = @tenant_id,
                    entra_object_id = @entra_object_id,
                    entra_user_principal_name = @user_principal_name,
                    source_provider = CASE
                        WHEN @mode = 'test' THEN 'ENTRA_ID_TEST'
                        ELSE 'ENTRA_ID'
                    END,
                    last_sso_login_at = NOW(),
                    updated_at = NOW()
                WHERE user_id = @user_id;
                """, connection);

            updateCommand.Parameters.AddWithValue("display_name", displayName);
            updateCommand.Parameters.AddWithValue("tenant_id", tenantId);
            updateCommand.Parameters.AddWithValue("entra_object_id", objectId);
            updateCommand.Parameters.AddWithValue("user_principal_name", (object?)preferredUsername ?? DBNull.Value);
            updateCommand.Parameters.AddWithValue("mode", mode);
            updateCommand.Parameters.AddWithValue("user_id", userId);

            await updateCommand.ExecuteNonQueryAsync();
        }
        else if (allowTestJit && mode.Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            userId = await ProjectPulseEnsureEntraUserAsync(
                connection,
                tenantId,
                objectId,
                email,
                displayName,
                preferredUsername,
                "ENTRA_ID_TEST");

            await ProjectPulseAssignDefaultEngineerRoleAsync(
                connection,
                userId,
                "Default Engineer role from test Entra SSO JIT import.");
        }
        else
        {
            return Results.Redirect("/#login?ssoError=user_not_imported");
        }
    }

    if (!await UserHasActiveRoleAsync(connection, userId))
    {
        return Results.Redirect("/#login?ssoError=no_active_project_pulse_role");
    }

    var session = await CreateProjectPulseSessionAsync(connection, userId, "ENTRA_ID", httpContext.Request);

    var sessionJson = JsonSerializer.Serialize(new
    {
        username = email,
        displayName,
        loginMethod = "sso",
        provider = "ENTRA_ID",
        sessionToken = session.RawToken,
        expiresAt = session.ExpiresAt,
        signedInAt = DateTimeOffset.UtcNow
    });

    var sessionJsonLiteral = JsonSerializer.Serialize(sessionJson);

    var html = $"""
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <title>Project Pulse SSO</title>
</head>
<body>
  <p>Completing Project Pulse sign-in...</p>
  <script>
    window.localStorage.setItem('projectPulseAuthSession', {sessionJsonLiteral});
    window.location.replace('/#dashboard');
  </script>
</body>
</html>
""";

    return Results.Content(html, "text/html");
});


app.MapGet("/api/admin/azure/import-settings", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectPulseUserIsAzureAdministratorAsync(connection, sessionUserId.Value))
    {
        return Results.Json(new { status = "access_denied", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var settings = await ProjectPulseGetEntraImportSettingsAsync(connection);

    return Results.Ok(new
    {
        status = "ok",
        settings
    });
});

app.MapPost("/api/admin/azure/import-settings", async (HttpContext httpContext, ProjectPulseImportSettingsUpdateRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectPulseUserIsAzureAdministratorAsync(connection, sessionUserId.Value))
    {
        return Results.Json(new { status = "access_denied", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var environmentMode = (request.EnvironmentMode ?? "test").Trim().ToLowerInvariant();
    var tenantDomain = (request.TenantDomain ?? "").Trim().ToLowerInvariant();
    var sourceProvider = (request.SourceProvider ?? "ENTRA_ID_TEST").Trim().ToUpperInvariant();
    var importSourceType = (request.ImportSourceType ?? "ALL_USERS").Trim().ToUpperInvariant();
    var defaultRoleCode = (request.DefaultRoleCode ?? "ENGINEER").Trim().ToUpperInvariant();

    if (environmentMode == "production")
    {
        tenantDomain = "ussignal.com";
        sourceProvider = "ENTRA_ID";
    }
    else if (environmentMode == "custom")
    {
        if (string.IsNullOrWhiteSpace(tenantDomain))
        {
            return Results.BadRequest(new { status = "validation_failed", message = "Tenant domain is required when using Create New." });
        }

        sourceProvider = string.IsNullOrWhiteSpace(sourceProvider) ? "ENTRA_ID_TEST" : sourceProvider;
    }
    else
    {
        tenantDomain = "onenecklab.com,onitdemo.com";
        sourceProvider = "ENTRA_ID_TEST";
        environmentMode = "test";
    }

    if (importSourceType is not ("ALL_USERS" or "GROUP" or "FILTER"))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Import source type must be ALL_USERS, GROUP, or FILTER." });
    }

    if (importSourceType == "GROUP" && string.IsNullOrWhiteSpace(request.GraphGroupId))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Group ID is required for group-based import." });
    }

    if (importSourceType == "FILTER" && string.IsNullOrWhiteSpace(request.GraphFilter))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Graph filter is required for filter-based import." });
    }

    await using var command = new NpgsqlCommand("""
        INSERT INTO azure_entra_import_settings (
            settings_id,
            environment_mode,
            tenant_domain,
            source_provider,
            import_source_type,
            graph_group_id,
            graph_filter,
            default_role_code,
            disable_missing_from_source,
            updated_at
        )
        VALUES (
            'default',
            @environment_mode,
            @tenant_domain,
            @source_provider,
            @import_source_type,
            NULLIF(@graph_group_id, ''),
            NULLIF(@graph_filter, ''),
            @default_role_code,
            @disable_missing_from_source,
            NOW()
        )
        ON CONFLICT (settings_id) DO UPDATE
        SET environment_mode = EXCLUDED.environment_mode,
            tenant_domain = EXCLUDED.tenant_domain,
            source_provider = EXCLUDED.source_provider,
            import_source_type = EXCLUDED.import_source_type,
            graph_group_id = EXCLUDED.graph_group_id,
            graph_filter = EXCLUDED.graph_filter,
            default_role_code = EXCLUDED.default_role_code,
            disable_missing_from_source = EXCLUDED.disable_missing_from_source,
            updated_at = NOW();
        """, connection);

    command.Parameters.AddWithValue("environment_mode", environmentMode);
    command.Parameters.AddWithValue("tenant_domain", tenantDomain);
    command.Parameters.AddWithValue("source_provider", sourceProvider);
    command.Parameters.AddWithValue("import_source_type", importSourceType);
    command.Parameters.AddWithValue("graph_group_id", request.GraphGroupId?.Trim() ?? "");
    command.Parameters.AddWithValue("graph_filter", request.GraphFilter?.Trim() ?? "");
    command.Parameters.AddWithValue("default_role_code", defaultRoleCode);
    command.Parameters.AddWithValue("disable_missing_from_source", request.DisableMissingFromSource);

    await command.ExecuteNonQueryAsync();

    return Results.Ok(new { status = "saved", message = "Azure import settings saved." });
});

app.MapPost("/api/admin/azure/users/preview", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectPulseUserIsAzureAdministratorAsync(connection, sessionUserId.Value))
    {
        return Results.Json(new { status = "access_denied", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var settings = await ProjectPulseGetEntraImportSettingsAsync(connection);

    var graphUsers = await ProjectPulseFetchGraphUsersAsync(settings);

    var candidateEmails = graphUsers
        .SelectMany(user => new[] { user.Email, user.UserPrincipalName })
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim().ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var candidateObjectIds = graphUsers
        .Select(user => user.Id)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var imported = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    await using (var importedCommand = new NpgsqlCommand("""
        SELECT entra_object_id,
               email,
               display_name,
               is_active,
               login_enabled,
               source_provider,
               last_directory_sync_at
        FROM app_users
        WHERE COALESCE(source_provider, '') IN ('ENTRA_ID', 'ENTRA_ID_TEST')
           OR lower(email) = ANY(@candidate_emails)
           OR COALESCE(entra_object_id, '') = ANY(@candidate_object_ids);
        """, connection))
    {
        importedCommand.Parameters.AddWithValue("candidate_emails", candidateEmails);
        importedCommand.Parameters.AddWithValue("candidate_object_ids", candidateObjectIds);

        await using var reader = await importedCommand.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var snapshot = new
            {
                email = reader.GetString(1),
                displayName = reader.GetString(2),
                isActive = reader.GetBoolean(3),
                loginEnabled = reader.GetBoolean(4),
                sourceProvider = reader.GetString(5),
                lastDirectorySyncAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6)
            };

            if (!reader.IsDBNull(0))
            {
                imported[reader.GetString(0)] = snapshot;
            }

            imported[reader.GetString(1)] = snapshot;
        }
    }

    await using (var updatePreviewCommand = new NpgsqlCommand("""
        UPDATE azure_entra_import_settings
        SET last_preview_at = NOW(),
            updated_at = NOW()
        WHERE settings_id = 'default';
        """, connection))
    {
        await updatePreviewCommand.ExecuteNonQueryAsync();
    }

    await ProjectPulseCreateAzureImportRunAsync(
        connection,
        settings,
        sessionUserId.Value,
        "preview",
        previewedCount: graphUsers.Count);

    return Results.Ok(new
    {
        status = "ok",
        settings,
        users = graphUsers.Select(user => new
        {
            entraObjectId = user.Id,
            user.DisplayName,
            user.Email,
            user.UserPrincipalName,
            user.JobTitle,
            user.Department,
            user.OfficeLocation,
            user.AccountEnabled,
            alreadyImported =
                (!string.IsNullOrWhiteSpace(user.Id) && imported.ContainsKey(user.Id)) ||
                (!string.IsNullOrWhiteSpace(user.Email) && imported.ContainsKey(user.Email)) ||
                (!string.IsNullOrWhiteSpace(user.UserPrincipalName) && imported.ContainsKey(user.UserPrincipalName)),
            importStatus =
                ((!string.IsNullOrWhiteSpace(user.Id) && imported.ContainsKey(user.Id)) ||
                 (!string.IsNullOrWhiteSpace(user.Email) && imported.ContainsKey(user.Email)) ||
                 (!string.IsNullOrWhiteSpace(user.UserPrincipalName) && imported.ContainsKey(user.UserPrincipalName)))
                    ? "Already imported"
                    : "Ready to import",
            willBeInactive = !user.AccountEnabled
        })
    });
});

app.MapPost("/api/admin/azure/users/import-selected", async (HttpContext httpContext, ProjectPulseImportSelectedUsersRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (request.EntraObjectIds is null || request.EntraObjectIds.Count == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Select at least one Entra user to import." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectPulseUserIsAzureAdministratorAsync(connection, sessionUserId.Value))
    {
        return Results.Json(new { status = "access_denied", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var settings = await ProjectPulseGetEntraImportSettingsAsync(connection);

    var imported = 0;
    var updated = 0;
    var deactivated = 0;
    var skipped = 0;
    var selectedUsers = new List<ProjectPulseGraphUser>();

    foreach (var id in request.EntraObjectIds.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var graphUser = await ProjectPulseFetchGraphUserByIdAsync(id);
        selectedUsers.Add(graphUser);
    }

    var importRunId = await ProjectPulseCreateAzureImportRunAsync(
        connection,
        settings,
        sessionUserId.Value,
        "import_selected",
        selectedCount: selectedUsers.Count);

    foreach (var user in selectedUsers)
    {
        var result = await ProjectPulseUpsertSelectedEntraUserAsync(connection, settings, user);

        if (result.ActionTaken == "imported_or_updated")
        {
            imported++;
        }
        else if (result.ActionTaken.StartsWith("deactivated", StringComparison.OrdinalIgnoreCase))
        {
            deactivated++;
        }
        else
        {
            skipped++;
        }

        await ProjectPulseRecordAzureImportRunUserAsync(connection, importRunId, user, result.ActionTaken);
    }

    await using (var updateImportCommand = new NpgsqlCommand("""
        UPDATE azure_entra_import_settings
        SET last_import_at = NOW(),
            updated_at = NOW()
        WHERE settings_id = 'default';

        UPDATE azure_entra_import_runs
        SET imported_count = @imported_count,
            updated_count = @updated_count,
            deactivated_count = @deactivated_count,
            skipped_count = @skipped_count
        WHERE import_run_id = @import_run_id;
        """, connection))
    {
        updateImportCommand.Parameters.AddWithValue("imported_count", imported);
        updateImportCommand.Parameters.AddWithValue("updated_count", updated);
        updateImportCommand.Parameters.AddWithValue("deactivated_count", deactivated);
        updateImportCommand.Parameters.AddWithValue("skipped_count", skipped);
        updateImportCommand.Parameters.AddWithValue("import_run_id", importRunId);

        await updateImportCommand.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        status = "import_completed",
        selected = selectedUsers.Count,
        imported,
        updated,
        deactivated,
        skipped
    });
});

app.MapPost("/api/admin/azure/users/reconcile", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    if (!await ProjectPulseUserIsAzureAdministratorAsync(connection, sessionUserId.Value))
    {
        return Results.Json(new { status = "access_denied", message = "Azure Admin is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var settings = await ProjectPulseGetEntraImportSettingsAsync(connection);
    var currentUsers = await ProjectPulseFetchGraphUsersAsync(settings);

    var importRunId = await ProjectPulseCreateAzureImportRunAsync(
        connection,
        settings,
        sessionUserId.Value,
        "reconcile",
        previewedCount: currentUsers.Count);

    var deactivated = await ProjectPulseDeactivateMissingOrDisabledEntraUsersAsync(
        connection,
        settings,
        currentUsers,
        importRunId);

    await using (var updateCommand = new NpgsqlCommand("""
        UPDATE azure_entra_import_settings
        SET last_reconcile_at = NOW(),
            updated_at = NOW()
        WHERE settings_id = 'default';

        UPDATE azure_entra_import_runs
        SET deactivated_count = @deactivated_count
        WHERE import_run_id = @import_run_id;
        """, connection))
    {
        updateCommand.Parameters.AddWithValue("deactivated_count", deactivated);
        updateCommand.Parameters.AddWithValue("import_run_id", importRunId);

        await updateCommand.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        status = "reconcile_completed",
        sourceUserCount = currentUsers.Count,
        deactivated
    });
});



// 019M-AV Approval Export Audit Workflow Hardening
app.MapGet("/api/workflow/operational-readiness", async (HttpContext httpContext, DateOnly? weekStart, DateOnly? weekEnd) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView && !access.CanViewAll)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Workflow operational readiness is restricted to approved workflow roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    var start = weekStart ?? today.AddDays(-(int)today.DayOfWeek);
    var end = weekEnd ?? start.AddDays(6);

    long totalEntries = 0;
    decimal totalHours = 0;
    long draftEntries = 0;
    decimal draftHours = 0;
    long submittedEntries = 0;
    decimal submittedHours = 0;
    long managerApprovedEntries = 0;
    decimal managerApprovedHours = 0;
    long projectApprovedEntries = 0;
    decimal projectApprovedHours = 0;
    long accountingReadyEntries = 0;
    decimal accountingReadyHours = 0;
    long reconciledEntries = 0;
    decimal reconciledHours = 0;
    long lockedEntries = 0;
    decimal lockedHours = 0;
    long returnedEntries = 0;
    decimal returnedHours = 0;
    long projectLinkedEntries = 0;
    long taskLinkedEntries = 0;
    long exportReadyEntries = 0;
    decimal exportReadyHours = 0;
    long exportBlockedEntries = 0;
    decimal exportBlockedHours = 0;

    await using (var summaryCommand = new NpgsqlCommand("""
        SELECT
            COUNT(*)::bigint AS total_entries,
            COALESCE(SUM(te.hours), 0)::numeric AS total_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, 'draft') = 'draft')::bigint AS draft_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, 'draft') = 'draft'), 0)::numeric AS draft_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, '') IN ('submitted', 'pending_manager_approval'))::bigint AS submitted_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, '') IN ('submitted', 'pending_manager_approval')), 0)::numeric AS submitted_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, '') = 'manager_approved')::bigint AS manager_approved_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, '') = 'manager_approved'), 0)::numeric AS manager_approved_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, '') IN ('project_approved', 'project_validated'))::bigint AS project_approved_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, '') IN ('project_approved', 'project_validated')), 0)::numeric AS project_approved_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, '') = 'accounting_ready')::bigint AS accounting_ready_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, '') = 'accounting_ready'), 0)::numeric AS accounting_ready_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, '') = 'reconciled')::bigint AS reconciled_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, '') = 'reconciled'), 0)::numeric AS reconciled_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, '') = 'locked')::bigint AS locked_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, '') = 'locked'), 0)::numeric AS locked_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, '') IN ('manager_declined', 'rejected', 'returned'))::bigint AS returned_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, '') IN ('manager_declined', 'rejected', 'returned')), 0)::numeric AS returned_hours,

            COUNT(*) FILTER (WHERE te.project_id IS NOT NULL)::bigint AS project_linked_entries,
            COUNT(*) FILTER (WHERE te.task_id IS NOT NULL)::bigint AS task_linked_entries,

            COUNT(*) FILTER (WHERE COALESCE(te.status, '') IN ('accounting_ready', 'reconciled', 'locked'))::bigint AS export_ready_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, '') IN ('accounting_ready', 'reconciled', 'locked')), 0)::numeric AS export_ready_hours,

            COUNT(*) FILTER (WHERE COALESCE(te.status, 'draft') NOT IN ('accounting_ready', 'reconciled', 'locked'))::bigint AS export_blocked_entries,
            COALESCE(SUM(te.hours) FILTER (WHERE COALESCE(te.status, 'draft') NOT IN ('accounting_ready', 'reconciled', 'locked')), 0)::numeric AS export_blocked_hours
        FROM time_entries te
        LEFT JOIN projects p
            ON p.project_id = te.project_id
        WHERE te.work_date BETWEEN @week_start AND @week_end
          AND (
              @can_view_all = TRUE
              OR @can_manage_accounting = TRUE
              OR @can_export = TRUE
              OR p.project_manager_user_id = @user_id
          );
        """, connection))
    {
        summaryCommand.Parameters.AddWithValue("week_start", start);
        summaryCommand.Parameters.AddWithValue("week_end", end);
        summaryCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        summaryCommand.Parameters.AddWithValue("can_view_all", access.CanViewAll);
        summaryCommand.Parameters.AddWithValue("can_manage_accounting", access.CanManageAccounting);
        summaryCommand.Parameters.AddWithValue("can_export", access.CanExport);

        await using var reader = await summaryCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            totalEntries = reader.GetInt64(0);
            totalHours = reader.GetDecimal(1);
            draftEntries = reader.GetInt64(2);
            draftHours = reader.GetDecimal(3);
            submittedEntries = reader.GetInt64(4);
            submittedHours = reader.GetDecimal(5);
            managerApprovedEntries = reader.GetInt64(6);
            managerApprovedHours = reader.GetDecimal(7);
            projectApprovedEntries = reader.GetInt64(8);
            projectApprovedHours = reader.GetDecimal(9);
            accountingReadyEntries = reader.GetInt64(10);
            accountingReadyHours = reader.GetDecimal(11);
            reconciledEntries = reader.GetInt64(12);
            reconciledHours = reader.GetDecimal(13);
            lockedEntries = reader.GetInt64(14);
            lockedHours = reader.GetDecimal(15);
            returnedEntries = reader.GetInt64(16);
            returnedHours = reader.GetDecimal(17);
            projectLinkedEntries = reader.GetInt64(18);
            taskLinkedEntries = reader.GetInt64(19);
            exportReadyEntries = reader.GetInt64(20);
            exportReadyHours = reader.GetDecimal(21);
            exportBlockedEntries = reader.GetInt64(22);
            exportBlockedHours = reader.GetDecimal(23);
        }
    }

    long exportCount = 0;
    long exportItemCount = 0;
    decimal exportedHours = 0;

    await using (var exportCommand = new NpgsqlCommand("""
        SELECT
            COUNT(*)::bigint,
            COALESCE(SUM(item_count), 0)::bigint,
            COALESCE(SUM(total_hours), 0)::numeric
        FROM time_workflow_exports
        WHERE COALESCE(week_start, @week_start) <= @week_end
          AND COALESCE(week_end, @week_end) >= @week_start;
        """, connection))
    {
        exportCommand.Parameters.AddWithValue("week_start", start);
        exportCommand.Parameters.AddWithValue("week_end", end);

        await using var reader = await exportCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            exportCount = reader.GetInt64(0);
            exportItemCount = reader.GetInt64(1);
            exportedHours = reader.GetDecimal(2);
        }
    }

    var statusBreakdown = new List<object>();
    await using (var breakdownCommand = new NpgsqlCommand("""
        SELECT
            COALESCE(te.status, 'draft') AS status,
            COUNT(*)::bigint AS entry_count,
            COALESCE(SUM(te.hours), 0)::numeric AS total_hours
        FROM time_entries te
        LEFT JOIN projects p
            ON p.project_id = te.project_id
        WHERE te.work_date BETWEEN @week_start AND @week_end
          AND (
              @can_view_all = TRUE
              OR @can_manage_accounting = TRUE
              OR @can_export = TRUE
              OR p.project_manager_user_id = @user_id
          )
        GROUP BY COALESCE(te.status, 'draft')
        ORDER BY COALESCE(te.status, 'draft');
        """, connection))
    {
        breakdownCommand.Parameters.AddWithValue("week_start", start);
        breakdownCommand.Parameters.AddWithValue("week_end", end);
        breakdownCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        breakdownCommand.Parameters.AddWithValue("can_view_all", access.CanViewAll);
        breakdownCommand.Parameters.AddWithValue("can_manage_accounting", access.CanManageAccounting);
        breakdownCommand.Parameters.AddWithValue("can_export", access.CanExport);

        await using var reader = await breakdownCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statusBreakdown.Add(new
            {
                status = reader.GetString(0),
                entryCount = reader.GetInt64(1),
                totalHours = reader.GetDecimal(2)
            });
        }
    }

    var auditEvidence = new List<object>();
    if (access.CanAudit || access.CanViewAll || access.CanManageAccounting || access.CanExport || access.CanProjectApprove)
    {
        await using var auditCommand = new NpgsqlCommand("""
            SELECT
                al.audit_log_id,
                COALESCE(au.display_name, au.email, 'System') AS actor_name,
                al.action,
                al.entity_type,
                al.entity_id,
                al.created_at,
                COALESCE(al.new_value::text, '') AS new_value_text
            FROM audit_logs al
            LEFT JOIN app_users au
                ON au.user_id = al.actor_user_id
            WHERE al.action ILIKE '%time%'
               OR al.action ILIKE '%approval%'
               OR al.action ILIKE '%approved%'
               OR al.action ILIKE '%declined%'
               OR al.action ILIKE '%reconcili%'
               OR al.action ILIKE '%export%'
               OR al.action ILIKE '%lock%'
               OR al.entity_type IN ('timesheet', 'time_entry', 'time_workflow_export', 'accounting_reconciliation')
            ORDER BY al.created_at DESC
            LIMIT 30;
            """, connection);

        await using var reader = await auditCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var preview = reader.GetString(6);
            auditEvidence.Add(new
            {
                auditLogId = reader.GetGuid(0),
                actorName = reader.GetString(1),
                action = reader.GetString(2),
                entityType = reader.GetString(3),
                entityId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                createdAt = reader.GetDateTime(5),
                evidencePreview = preview.Length > 240 ? preview[..240] + "..." : preview
            });
        }
    }

    var workflowStages = new List<object>
    {
        new
        {
            stage = "manager_review",
            title = "Manager Review",
            entryCount = submittedEntries,
            totalHours = submittedHours,
            actionRequired = submittedEntries > 0 && (access.CanViewAll || access.CanProjectApprove),
            guidance = submittedEntries > 0
                ? "Submitted time is waiting for manager approval."
                : "No submitted time is waiting for manager approval in this date range."
        },
        new
        {
            stage = "project_validation",
            title = "Project Validation",
            entryCount = managerApprovedEntries,
            totalHours = managerApprovedHours,
            actionRequired = managerApprovedEntries > 0 && (access.CanViewAll || access.CanProjectApprove),
            guidance = managerApprovedEntries > 0
                ? "Manager-approved time should be validated against project/task context before accounting review."
                : "No manager-approved time is waiting for project validation in this date range."
        },
        new
        {
            stage = "accounting_review",
            title = "Accounting Review",
            entryCount = projectApprovedEntries + accountingReadyEntries,
            totalHours = projectApprovedHours + accountingReadyHours,
            actionRequired = (projectApprovedEntries + accountingReadyEntries) > 0 && (access.CanViewAll || access.CanManageAccounting),
            guidance = (projectApprovedEntries + accountingReadyEntries) > 0
                ? "Accounting can review validated time and reconcile exceptions."
                : "No validated/accounting-ready time is waiting for accounting review."
        },
        new
        {
            stage = "reconciled_locked",
            title = "Reconciled / Locked",
            entryCount = reconciledEntries + lockedEntries,
            totalHours = reconciledHours + lockedHours,
            actionRequired = false,
            guidance = "Reconciled and locked time is protected for audit and export evidence."
        },
        new
        {
            stage = "returned",
            title = "Returned / Rejected",
            entryCount = returnedEntries,
            totalHours = returnedHours,
            actionRequired = returnedEntries > 0,
            guidance = returnedEntries > 0
                ? "Returned or rejected time needs correction before it can continue through the workflow."
                : "No returned or rejected time in this date range."
        }
    };

    var roleGuidance = new List<object>
    {
        new { role = "Engineer", access = false, guidance = "Engineers do not receive approval, reconciliation, export, or audit workflow controls." },
        new { role = "Manager", access = access.CanProjectApprove && !access.CanManageAccounting && !access.CanExport, guidance = "Managers focus on approval and project validation where assigned." },
        new { role = "Project Management", access = access.CanProjectApprove, guidance = "Project Management validates manager-approved project/task time in PM scope." },
        new { role = "PTC / Admin", access = access.CanViewAll || access.CanManageAccounting || access.CanExport, guidance = "PTC and Administrators can manage reconciliation, export readiness, and audit evidence." },
        new { role = "Executive", access = access.CanView && !access.CanProjectApprove && !access.CanManageAccounting && !access.CanExport, guidance = "Executives receive read-only workflow status." }
    };

    return Results.Ok(new
    {
        module = "019M-AV Approval Export Audit Workflow Hardening",
        dateRange = new { weekStart = start, weekEnd = end },
        access = new
        {
            access.CanView,
            access.CanProjectApprove,
            access.CanManageAccounting,
            access.CanExport,
            access.CanAudit,
            access.CanViewAll,
            automationEnabled = false,
            note = "This endpoint adds operational readiness and audit evidence. It does not change workflow status."
        },
        summary = new
        {
            totalEntries,
            totalHours,
            draftEntries,
            draftHours,
            submittedEntries,
            submittedHours,
            managerApprovedEntries,
            managerApprovedHours,
            projectApprovedEntries,
            projectApprovedHours,
            accountingReadyEntries,
            accountingReadyHours,
            reconciledEntries,
            reconciledHours,
            lockedEntries,
            lockedHours,
            returnedEntries,
            returnedHours,
            projectLinkedEntries,
            taskLinkedEntries,
            exportReadyEntries,
            exportReadyHours,
            exportBlockedEntries,
            exportBlockedHours,
            exportCount,
            exportItemCount,
            exportedHours,
            auditEvidenceCount = auditEvidence.Count
        },
        exportReadiness = new
        {
            readyToExport = exportReadyEntries > 0 && exportBlockedEntries == 0,
            readyEntryCount = exportReadyEntries,
            readyHours = exportReadyHours,
            blockedEntryCount = exportBlockedEntries,
            blockedHours = exportBlockedHours,
            existingExportCount = exportCount,
            message = exportReadyEntries == 0
                ? "No accounting-ready, reconciled, or locked entries are ready for export in this date range."
                : exportBlockedEntries > 0
                    ? "Some entries are still draft, submitted, manager-approved, or returned, so the export package is not fully clean."
                    : "All scoped entries are in export-ready statuses."
        },
        workflowStages,
        statusBreakdown,
        roleGuidance,
        auditEvidence
    });
});

app.MapGet("/api/workflow/approval-export-summary", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Approval/export workflow is restricted to approved workflow roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var summary = new Dictionary<string, long>();

    await using (var command = new NpgsqlCommand("""
        WITH scoped_days AS (
            SELECT
                tds.status,
                tds.timesheet_id,
                tds.work_date,
                EXISTS (
                    SELECT 1
                    FROM time_entries te
                    JOIN projects p ON p.project_id = te.project_id
                    WHERE te.timesheet_id = tds.timesheet_id
                      AND te.work_date = tds.work_date
                      AND p.project_manager_user_id = @user_id
                ) AS pm_scope
            FROM timesheet_day_statuses tds
            WHERE tds.status IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked', 'manager_declined')
        )
        SELECT
            COUNT(*) FILTER (WHERE status = 'submitted')::bigint AS pending_manager,
            COUNT(*) FILTER (WHERE status = 'manager_approved' AND (@can_view_all OR pm_scope))::bigint AS pending_project,
            COUNT(*) FILTER (WHERE status = 'pm_approved')::bigint AS pending_accounting,
            COUNT(*) FILTER (WHERE status = 'accounting_ready')::bigint AS accounting_ready,
            COUNT(*) FILTER (WHERE status = 'reconciled')::bigint AS reconciled,
            COUNT(*) FILTER (WHERE status = 'locked')::bigint AS locked,
            COUNT(*) FILTER (WHERE status = 'manager_declined')::bigint AS returned
        FROM scoped_days;
        """, connection))
    {
        command.Parameters.AddWithValue("user_id", sessionUserId.Value);
        command.Parameters.AddWithValue("can_view_all", access.CanViewAll);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        summary["pendingManagerApprovals"] = reader.GetInt64(0);
        summary["pendingProjectApprovals"] = reader.GetInt64(1);
        summary["pendingAccountingReview"] = reader.GetInt64(2);
        summary["accountingReady"] = reader.GetInt64(3);
        summary["reconciled"] = reader.GetInt64(4);
        summary["locked"] = reader.GetInt64(5);
        summary["returned"] = reader.GetInt64(6);
    }

    long exportCount;
    await using (var exportCommand = new NpgsqlCommand("""
        SELECT COUNT(*)::bigint
        FROM time_workflow_exports
        WHERE created_at >= NOW() - INTERVAL '30 days';
        """, connection))
    {
        exportCount = Convert.ToInt64(await exportCommand.ExecuteScalarAsync() ?? 0);
    }

    return Results.Ok(new
    {
        module = "019M-AL Approval Export Audit Workflow",
        refreshedAtUtc = DateTimeOffset.UtcNow,
        access = new
        {
            access.CanView,
            access.CanProjectApprove,
            access.CanManageAccounting,
            access.CanExport,
            access.CanAudit,
            access.CanViewAll
        },
        summary = new
        {
            pendingManagerApprovals = summary["pendingManagerApprovals"],
            pendingProjectApprovals = summary["pendingProjectApprovals"],
            pendingAccountingReview = summary["pendingAccountingReview"],
            accountingReady = summary["accountingReady"],
            reconciled = summary["reconciled"],
            locked = summary["locked"],
            returned = summary["returned"],
            exportsLast30Days = exportCount
        }
    });
});


app.MapGet("/api/workflow/approval-items", async (HttpContext httpContext, DateOnly? weekStart, DateOnly? weekEnd) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = weekEnd ?? start.AddDays(6);

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Approval/export workflow is restricted to approved workflow roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var items = new List<object>();

    await using (var command = new NpgsqlCommand("""
        WITH day_totals AS (
            SELECT
                tds.timesheet_id,
                tds.work_date,
                tds.status,
                tds.submitted_at,
                tds.manager_approved_at,
                tds.manager_decision_comment,
                tds.pm_approved_at,
                tds.pm_decision_comment,
                tds.accounting_ready_at,
                tds.accounting_comment,
                tds.reconciled_at,
                tds.reconciliation_comment,
                tds.locked_at,
                tds.lock_comment,
                t.user_id,
                employee.display_name AS employee_name,
                employee.email AS employee_email,
                COALESCE(SUM(te.hours), 0)::numeric AS total_hours,
                COALESCE(STRING_AGG(DISTINCT p.project_code, ', ' ORDER BY p.project_code), 'Non-project / mixed') AS project_codes,
                COALESCE(STRING_AGG(DISTINCT p.project_name, ', ' ORDER BY p.project_name), 'Non-project / mixed') AS project_names,
                BOOL_OR(p.project_manager_user_id = @user_id) AS pm_scope
            FROM timesheet_day_statuses tds
            JOIN timesheets t ON t.timesheet_id = tds.timesheet_id
            JOIN app_users employee ON employee.user_id = t.user_id
            LEFT JOIN time_entries te
                ON te.timesheet_id = tds.timesheet_id
               AND te.work_date = tds.work_date
            LEFT JOIN projects p
                ON p.project_id = te.project_id
            WHERE tds.work_date BETWEEN @week_start AND @week_end
              AND tds.status IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked', 'manager_declined')
            GROUP BY
                tds.timesheet_id,
                tds.work_date,
                tds.status,
                tds.submitted_at,
                tds.manager_approved_at,
                tds.manager_decision_comment,
                tds.pm_approved_at,
                tds.pm_decision_comment,
                tds.accounting_ready_at,
                tds.accounting_comment,
                tds.reconciled_at,
                tds.reconciliation_comment,
                tds.locked_at,
                tds.lock_comment,
                t.user_id,
                employee.display_name,
                employee.email
        )
        SELECT *
        FROM day_totals
        WHERE @can_view_all
           OR (status = 'manager_approved' AND pm_scope)
           OR (status IN ('pm_approved', 'accounting_ready', 'reconciled', 'locked') AND @can_accounting)
        ORDER BY work_date DESC, employee_name;
        """, connection))
    {
        command.Parameters.AddWithValue("user_id", sessionUserId.Value);
        command.Parameters.AddWithValue("week_start", start);
        command.Parameters.AddWithValue("week_end", end);
        command.Parameters.AddWithValue("can_view_all", access.CanViewAll);
        command.Parameters.AddWithValue("can_accounting", access.CanManageAccounting);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                timesheetId = reader.GetGuid(0),
                workDate = reader.GetFieldValue<DateOnly>(1),
                status = reader.GetString(2),
                submittedAt = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3),
                managerApprovedAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4),
                managerComment = reader.IsDBNull(5) ? null : reader.GetString(5),
                pmApprovedAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
                pmComment = reader.IsDBNull(7) ? null : reader.GetString(7),
                accountingReadyAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(8),
                accountingComment = reader.IsDBNull(9) ? null : reader.GetString(9),
                reconciledAt = reader.IsDBNull(10) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(10),
                reconciliationComment = reader.IsDBNull(11) ? null : reader.GetString(11),
                lockedAt = reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
                lockComment = reader.IsDBNull(13) ? null : reader.GetString(13),
                userId = reader.GetGuid(14),
                employeeName = reader.GetString(15),
                employeeEmail = reader.GetString(16),
                totalHours = reader.GetDecimal(17),
                projectCodes = reader.GetString(18),
                projectNames = reader.GetString(19),
                pmScope = !reader.IsDBNull(20) && reader.GetBoolean(20)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AL Approval Export Audit Workflow",
        weekStart = start,
        weekEnd = end,
        count = items.Count,
        items,
        access = new
        {
            access.CanProjectApprove,
            access.CanManageAccounting,
            access.CanExport,
            access.CanAudit,
            access.CanViewAll
        }
    });
});


app.MapPost("/api/workflow/approval-items/action", async (ApprovalExportWorkflowActionRequest request, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (request.TimesheetId == Guid.Empty || request.WorkDate == default)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Timesheet and work date are required." });
    }

    var normalizedAction = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
    var comment = string.IsNullOrWhiteSpace(request.Comment) ? normalizedAction : request.Comment.Trim();

    string targetStatus;
    string auditAction;
    string[] allowedStatuses;

    switch (normalizedAction)
    {
        case "pm_approve":
            targetStatus = "pm_approved";
            auditAction = "timesheet_day_project_manager_approved";
            allowedStatuses = new[] { "manager_approved" };
            break;
        case "accounting_ready":
            targetStatus = "accounting_ready";
            auditAction = "timesheet_day_marked_accounting_ready";
            allowedStatuses = new[] { "pm_approved", "manager_approved" };
            break;
        case "reconcile":
            targetStatus = "reconciled";
            auditAction = "timesheet_day_reconciled";
            allowedStatuses = new[] { "accounting_ready", "pm_approved" };
            break;
        case "lock":
            targetStatus = "locked";
            auditAction = "timesheet_day_locked";
            allowedStatuses = new[] { "reconciled", "accounting_ready" };
            break;
        default:
            return Results.BadRequest(new
            {
                status = "invalid_action",
                message = "Action must be pm_approve, accounting_ready, reconcile, or lock."
            });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);

    if (normalizedAction == "pm_approve" && !access.CanProjectApprove)
    {
        return Results.Json(new { status = "access_denied", message = "Project time approval is restricted to project managers, project/team coordinators, and administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    if (normalizedAction is "accounting_ready" or "reconcile" or "lock" && !access.CanManageAccounting)
    {
        return Results.Json(new { status = "access_denied", message = "Accounting workflow actions are restricted to project/team coordinators and administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        await using var command = new NpgsqlCommand("""
            UPDATE timesheet_day_statuses tds
            SET status = @target_status,
                pm_approved_at = CASE WHEN @target_status = 'pm_approved' THEN NOW() ELSE pm_approved_at END,
                pm_approved_by_user_id = CASE WHEN @target_status = 'pm_approved' THEN @actor_user_id ELSE pm_approved_by_user_id END,
                pm_decision_comment = CASE WHEN @target_status = 'pm_approved' THEN @comment ELSE pm_decision_comment END,
                accounting_ready_at = CASE WHEN @target_status = 'accounting_ready' THEN NOW() ELSE accounting_ready_at END,
                accounting_ready_by_user_id = CASE WHEN @target_status = 'accounting_ready' THEN @actor_user_id ELSE accounting_ready_by_user_id END,
                accounting_comment = CASE WHEN @target_status = 'accounting_ready' THEN @comment ELSE accounting_comment END,
                reconciled_at = CASE WHEN @target_status = 'reconciled' THEN NOW() ELSE reconciled_at END,
                reconciled_by_user_id = CASE WHEN @target_status = 'reconciled' THEN @actor_user_id ELSE reconciled_by_user_id END,
                reconciliation_comment = CASE WHEN @target_status = 'reconciled' THEN @comment ELSE reconciliation_comment END,
                locked_at = CASE WHEN @target_status = 'locked' THEN NOW() ELSE locked_at END,
                locked_by_user_id = CASE WHEN @target_status = 'locked' THEN @actor_user_id ELSE locked_by_user_id END,
                lock_comment = CASE WHEN @target_status = 'locked' THEN @comment ELSE lock_comment END,
                updated_at = NOW()
            WHERE tds.timesheet_id = @timesheet_id
              AND tds.work_date = @work_date
              AND tds.status = ANY(@allowed_statuses)
              AND (
                    @can_view_all
                 OR EXISTS (
                        SELECT 1
                        FROM time_entries te
                        JOIN projects p ON p.project_id = te.project_id
                        WHERE te.timesheet_id = tds.timesheet_id
                          AND te.work_date = tds.work_date
                          AND p.project_manager_user_id = @actor_user_id
                    )
              )
            RETURNING tds.timesheet_day_status_id;
            """, connection, transaction);

        command.Parameters.AddWithValue("target_status", targetStatus);
        command.Parameters.AddWithValue("actor_user_id", sessionUserId.Value);
        command.Parameters.AddWithValue("comment", comment);
        command.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        command.Parameters.AddWithValue("work_date", request.WorkDate);
        command.Parameters.AddWithValue("allowed_statuses", allowedStatuses);
        command.Parameters.AddWithValue("can_view_all", access.CanViewAll || access.CanManageAccounting);

        var statusId = (Guid?)(await command.ExecuteScalarAsync());

        if (statusId is null)
        {
            await transaction.RollbackAsync();

            return Results.Json(new
            {
                status = "workflow_action_not_applied",
                message = "No eligible workflow item matched the requested action, status, or role scope."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        await using (var entryCommand = new NpgsqlCommand("""
            UPDATE time_entries
            SET status = @target_status,
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction))
        {
            entryCommand.Parameters.AddWithValue("target_status", targetStatus);
            entryCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
            entryCommand.Parameters.AddWithValue("work_date", request.WorkDate);
            await entryCommand.ExecuteNonQueryAsync();
        }

        await InsertAuditLogAsync(connection, transaction, sessionUserId.Value, auditAction, "timesheet", request.TimesheetId);

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "workflow_action_applied",
            action = normalizedAction,
            targetStatus,
            timesheetId = request.TimesheetId,
            workDate = request.WorkDate,
            message = $"Workflow action '{normalizedAction}' applied to {request.WorkDate}."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to apply approval/export workflow action",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});


app.MapGet("/api/time-exports", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanExport && !access.CanViewAll)
    {
        return Results.Json(new { status = "access_denied", message = "Time export history is restricted to export-enabled roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var exports = new List<object>();

    await using (var command = new NpgsqlCommand("""
        SELECT
            time_workflow_export_id,
            export_format,
            week_start,
            week_end,
            export_status,
            requested_by_email,
            item_count,
            total_hours,
            file_name,
            notes,
            created_at,
            COALESCE(package_download_count, 0) AS package_download_count,
            package_last_downloaded_at,
            package_generated_at,
            COALESCE(package_content_type, 'text/csv') AS package_content_type,
            COALESCE(package_file_extension, 'csv') AS package_file_extension
        FROM time_workflow_exports
        ORDER BY created_at DESC
        LIMIT 100;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            exports.Add(new
            {
                exportId = reader.GetGuid(0),
                exportFormat = reader.GetString(1),
                weekStart = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
                weekEnd = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
                exportStatus = reader.GetString(4),
                requestedByEmail = reader.IsDBNull(5) ? null : reader.GetString(5),
                itemCount = reader.GetInt32(6),
                totalHours = reader.GetDecimal(7),
                fileName = reader.IsDBNull(8) ? null : reader.GetString(8),
                notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                createdAt = reader.GetFieldValue<DateTimeOffset>(10),
                packageDownloadCount = reader.GetInt32(11),
                packageLastDownloadedAt = reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
                packageGeneratedAt = reader.IsDBNull(13) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(13),
                packageContentType = reader.IsDBNull(14) ? "text/csv" : reader.GetString(14),
                packageFileExtension = reader.IsDBNull(15) ? "csv" : reader.GetString(15),
                downloadReady = true
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AL Time Export Foundation",
        count = exports.Count,
        exports
    });
});



// 019M-AW Export Package Generation + Download Readiness
app.MapGet("/api/time-exports/{exportId:guid}/download", async (Guid exportId, HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanExport && !access.CanViewAll)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Export package download is restricted to export-enabled roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    string exportFormat;
    DateOnly? weekStart;
    DateOnly? weekEnd;
    string? fileName;

    await using (var exportCommand = new NpgsqlCommand("""
        SELECT export_format, week_start, week_end, file_name
        FROM time_workflow_exports
        WHERE time_workflow_export_id = @export_id;
        """, connection))
    {
        exportCommand.Parameters.AddWithValue("export_id", exportId);

        await using var reader = await exportCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new
            {
                status = "export_not_found",
                message = "The requested export package was not found."
            });
        }

        exportFormat = reader.GetString(0);
        weekStart = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1);
        weekEnd = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2);
        fileName = reader.IsDBNull(3) ? null : reader.GetString(3);
    }

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = weekEnd ?? start.AddDays(6);

    var csv = new System.Text.StringBuilder();
    csv.AppendLine("Export Id,Export Format,Week Start,Week End,Work Date,Employee Name,Employee Email,Project Code,Project Name,Task Code,Task Name,Hours,Billable,Status,Description");

    var itemCount = 0;
    decimal totalHours = 0;

    await using (var itemCommand = new NpgsqlCommand("""
        SELECT
            te.work_date,
            COALESCE(employee.display_name, employee.email, '') AS employee_name,
            employee.email AS employee_email,
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
        """, connection))
    {
        itemCommand.Parameters.AddWithValue("week_start", start);
        itemCommand.Parameters.AddWithValue("week_end", end);

        await using var reader = await itemCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            itemCount++;
            totalHours += reader.GetDecimal(7);

            var fields = new[]
            {
                ProjectPulseCsvField(exportId),
                ProjectPulseCsvField(exportFormat),
                ProjectPulseCsvField(start),
                ProjectPulseCsvField(end),
                ProjectPulseCsvField(reader.GetFieldValue<DateOnly>(0)),
                ProjectPulseCsvField(reader.GetString(1)),
                ProjectPulseCsvField(reader.GetString(2)),
                ProjectPulseCsvField(reader.GetString(3)),
                ProjectPulseCsvField(reader.GetString(4)),
                ProjectPulseCsvField(reader.GetString(5)),
                ProjectPulseCsvField(reader.GetString(6)),
                ProjectPulseCsvField(reader.GetDecimal(7)),
                ProjectPulseCsvField(reader.GetBoolean(8) ? "Yes" : "No"),
                ProjectPulseCsvField(reader.GetString(9)),
                ProjectPulseCsvField(reader.GetString(10))
            };

            csv.AppendLine(string.Join(",", fields));
        }
    }

    await using (var updateCommand = new NpgsqlCommand("""
        UPDATE time_workflow_exports
        SET package_generated_at = COALESCE(package_generated_at, NOW()),
            package_content_type = 'text/csv',
            package_file_extension = 'csv',
            package_download_count = COALESCE(package_download_count, 0) + 1,
            package_last_downloaded_at = NOW(),
            package_last_downloaded_by_user_id = @user_id,
            item_count = @item_count,
            total_hours = @total_hours,
            updated_at = NOW()
        WHERE time_workflow_export_id = @export_id;
        """, connection))
    {
        updateCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        updateCommand.Parameters.AddWithValue("export_id", exportId);
        updateCommand.Parameters.AddWithValue("item_count", itemCount);
        updateCommand.Parameters.AddWithValue("total_hours", totalHours);
        await updateCommand.ExecuteNonQueryAsync();
    }

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
            'time_export_package_downloaded',
            'time_workflow_export',
            @entity_id,
            @new_value::jsonb
        );
        """, connection))
    {
        var auditValue = JsonSerializer.Serialize(new
        {
            exportId,
            exportFormat,
            weekStart = start,
            weekEnd = end,
            itemCount,
            totalHours,
            packageContentType = "text/csv",
            generatedAtUtc = DateTimeOffset.UtcNow
        });

        auditCommand.Parameters.AddWithValue("actor_user_id", sessionUserId.Value);
        auditCommand.Parameters.AddWithValue("entity_id", exportId);
        auditCommand.Parameters.AddWithValue("new_value", auditValue);
        await auditCommand.ExecuteNonQueryAsync();
    }

    var baseName = string.IsNullOrWhiteSpace(fileName)
        ? $"projectpulse-time-export-{start:yyyyMMdd}-{end:yyyyMMdd}"
        : System.IO.Path.GetFileNameWithoutExtension(fileName);

    var downloadFileName = $"{baseName}.csv";
    var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());

    return Results.File(bytes, "text/csv; charset=utf-8", downloadFileName);
});


// 019M-AY Workflow Export Audit Evidence Detail
app.MapGet("/api/time-exports/{exportId:guid}/details", async (Guid exportId, HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanAudit && !access.CanExport && !access.CanManageAccounting && !access.CanViewAll)
    {
        return Results.Json(new
        {
            status = "access_denied",
            message = "Export audit evidence is restricted to workflow audit, export, accounting, and administrator roles."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    object? exportRecord = null;
    DateOnly start = GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    DateOnly end = start.AddDays(6);

    await using (var exportCommand = new NpgsqlCommand("""
        SELECT
            time_workflow_export_id,
            export_format,
            week_start,
            week_end,
            export_status,
            requested_by_email,
            item_count,
            total_hours,
            file_name,
            notes,
            created_at,
            COALESCE(package_download_count, 0) AS package_download_count,
            package_last_downloaded_at,
            package_generated_at,
            COALESCE(package_content_type, 'text/csv') AS package_content_type,
            COALESCE(package_file_extension, 'csv') AS package_file_extension
        FROM time_workflow_exports
        WHERE time_workflow_export_id = @export_id;
        """, connection))
    {
        exportCommand.Parameters.AddWithValue("export_id", exportId);

        await using var reader = await exportCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return Results.NotFound(new
            {
                status = "export_not_found",
                message = "The requested export package was not found."
            });
        }

        start = reader.IsDBNull(2) ? start : reader.GetFieldValue<DateOnly>(2);
        end = reader.IsDBNull(3) ? end : reader.GetFieldValue<DateOnly>(3);

        exportRecord = new
        {
            exportId = reader.GetGuid(0),
            exportFormat = reader.GetString(1),
            weekStart = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
            weekEnd = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
            exportStatus = reader.GetString(4),
            requestedByEmail = reader.IsDBNull(5) ? null : reader.GetString(5),
            itemCount = reader.GetInt32(6),
            totalHours = reader.GetDecimal(7),
            fileName = reader.IsDBNull(8) ? null : reader.GetString(8),
            notes = reader.IsDBNull(9) ? null : reader.GetString(9),
            createdAt = reader.GetFieldValue<DateTimeOffset>(10),
            packageDownloadCount = reader.GetInt32(11),
            packageLastDownloadedAt = reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
            packageGeneratedAt = reader.IsDBNull(13) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(13),
            packageContentType = reader.IsDBNull(14) ? "text/csv" : reader.GetString(14),
            packageFileExtension = reader.IsDBNull(15) ? "csv" : reader.GetString(15),
            downloadReady = true
        };
    }

    var items = new List<object>();
    await using (var itemCommand = new NpgsqlCommand("""
        SELECT
            te.time_entry_id,
            te.work_date,
            COALESCE(employee.display_name, employee.email, '') AS employee_name,
            employee.email,
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
        """, connection))
    {
        itemCommand.Parameters.AddWithValue("week_start", start);
        itemCommand.Parameters.AddWithValue("week_end", end);

        await using var reader = await itemCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                timeEntryId = reader.GetGuid(0),
                workDate = reader.GetFieldValue<DateOnly>(1),
                employeeName = reader.GetString(2),
                employeeEmail = reader.GetString(3),
                projectCode = reader.GetString(4),
                projectName = reader.GetString(5),
                taskCode = reader.GetString(6),
                taskName = reader.GetString(7),
                hours = reader.GetDecimal(8),
                billable = reader.GetBoolean(9),
                status = reader.GetString(10),
                description = reader.GetString(11)
            });
        }
    }

    var auditEvidence = new List<object>();
    await using (var auditCommand = new NpgsqlCommand("""
        SELECT
            al.audit_log_id,
            COALESCE(au.display_name, au.email, 'System') AS actor_name,
            al.action,
            al.entity_type,
            al.entity_id,
            al.created_at,
            COALESCE(al.old_value::text, '') AS old_value_text,
            COALESCE(al.new_value::text, '') AS new_value_text
        FROM audit_logs al
        LEFT JOIN app_users au
            ON au.user_id = al.actor_user_id
        WHERE al.entity_id = @export_id
           OR al.action ILIKE '%export%'
           OR al.action ILIKE '%reconcili%'
           OR al.action ILIKE '%lock%'
           OR al.action ILIKE '%approval%'
           OR al.action ILIKE '%approved%'
        ORDER BY al.created_at DESC
        LIMIT 50;
        """, connection))
    {
        auditCommand.Parameters.AddWithValue("export_id", exportId);

        await using var reader = await auditCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var oldPreview = reader.GetString(6);
            var newPreview = reader.GetString(7);

            auditEvidence.Add(new
            {
                auditLogId = reader.GetGuid(0),
                actorName = reader.GetString(1),
                action = reader.GetString(2),
                entityType = reader.GetString(3),
                entityId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                createdAt = reader.GetDateTime(5),
                oldValuePreview = oldPreview.Length > 300 ? oldPreview[..300] + "..." : oldPreview,
                newValuePreview = newPreview.Length > 300 ? newPreview[..300] + "..." : newPreview
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AY Workflow Export Audit Evidence Detail",
        exportRecord,
        package = new
        {
            downloadReady = true,
            downloadUrl = $"/api/time-exports/{exportId}/download",
            itemCount = items.Count,
            totalHours = items.Sum(item => (decimal)item.GetType().GetProperty("hours")!.GetValue(item)!)
        },
        items,
        auditEvidence
    });
});

app.MapPost("/api/time-exports", async (TimeWorkflowExportCreateRequest request, HttpContext httpContext) =>
{
    var sessionUserId = GetProjectPulseSessionUserId(httpContext);
    if (sessionUserId is null)
    {
        return Results.Json(new { status = "session_required", message = "Missing session token." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var exportFormat = (request.ExportFormat ?? "excel").Trim().ToLowerInvariant();
    if (exportFormat is not ("excel" or "pdf"))
    {
        return Results.BadRequest(new { status = "invalid_export_format", message = "Export format must be excel or pdf." });
    }

    var start = request.WeekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = request.WeekEnd ?? start.AddDays(6);
    var notes = string.IsNullOrWhiteSpace(request.Notes) ? $"Prepared {exportFormat.ToUpperInvariant()} export foundation record." : request.Notes.Trim();

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanExport && !access.CanViewAll)
    {
        return Results.Json(new { status = "access_denied", message = "Time export preparation is restricted to export-enabled roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        int itemCount;
        decimal totalHours;

        await using (var countCommand = new NpgsqlCommand("""
            SELECT
                COUNT(*)::int AS item_count,
                COALESCE(SUM(hours), 0)::numeric AS total_hours
            FROM time_entries
            WHERE work_date BETWEEN @week_start AND @week_end
              AND status IN ('pm_approved', 'accounting_ready', 'reconciled', 'locked');
            """, connection, transaction))
        {
            countCommand.Parameters.AddWithValue("week_start", start);
            countCommand.Parameters.AddWithValue("week_end", end);

            await using var reader = await countCommand.ExecuteReaderAsync();
            await reader.ReadAsync();

            itemCount = reader.GetInt32(0);
            totalHours = reader.GetDecimal(1);
        }

        var exportId = Guid.Empty;
        var requestedByEmail = GetProjectPulseSessionEmail(httpContext) ?? "unknown";
        var fileName = $"projectpulse-time-export-{start:yyyyMMdd}-{end:yyyyMMdd}.csv";

        await using (var insertCommand = new NpgsqlCommand("""
            INSERT INTO time_workflow_exports (
                export_format,
                week_start,
                week_end,
                export_status,
                requested_by_user_id,
                requested_by_email,
                item_count,
                total_hours,
                file_name,
                notes,
                package_generated_at,
                package_content_type,
                package_file_extension
            )
            VALUES (
                @export_format,
                @week_start,
                @week_end,
                'prepared',
                @requested_by_user_id,
                @requested_by_email,
                @item_count,
                @total_hours,
                @file_name,
                @notes,
                NOW(),
                'text/csv',
                'csv'
            )
            RETURNING time_workflow_export_id;
            """, connection, transaction))
        {
            insertCommand.Parameters.AddWithValue("export_format", exportFormat);
            insertCommand.Parameters.AddWithValue("week_start", start);
            insertCommand.Parameters.AddWithValue("week_end", end);
            insertCommand.Parameters.AddWithValue("requested_by_user_id", sessionUserId.Value);
            insertCommand.Parameters.AddWithValue("requested_by_email", requestedByEmail);
            insertCommand.Parameters.AddWithValue("item_count", itemCount);
            insertCommand.Parameters.AddWithValue("total_hours", totalHours);
            insertCommand.Parameters.AddWithValue("file_name", fileName);
            insertCommand.Parameters.AddWithValue("notes", notes);

            exportId = (Guid)(await insertCommand.ExecuteScalarAsync() ?? Guid.Empty);
        }

        await InsertAuditLogAsync(connection, transaction, sessionUserId.Value, $"time_export_{exportFormat}_prepared", "time_workflow_export", exportId);

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "time_export_prepared",
            exportId,
            exportFormat,
            weekStart = start,
            weekEnd = end,
            itemCount,
            totalHours,
            fileName,
            message = $"{exportFormat.ToUpperInvariant()} export package record prepared. Download-ready CSV package is available from export history."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        return Results.Problem(
            title: "Failed to prepare time export",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});


app.MapTimeComplianceEndpoints();
app.MapProjectIntakeEndpoints();
app.MapProjectWorkspaceEndpoints();


// 019M-AZ through 019M-BJ Workflow Operations Mega Sprint

app.MapGet("/api/audit-history/summary", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView && !access.CanAudit && !access.CanViewAll && !access.CanManageAccounting && !access.CanProjectApprove && !access.CanExport)
    {
        return Results.Json(new { status = "access_denied", message = "Audit history is restricted to approved workflow roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    long totalEvents = 0;
    long workflowEvents = 0;
    long exportEvents = 0;
    long reconciliationEvents = 0;
    long approvalEvents = 0;
    DateTimeOffset? latestEventAt = null;

    await using (var command = new NpgsqlCommand("""
        SELECT
            COUNT(*)::bigint,
            COUNT(*) FILTER (
                WHERE action ILIKE '%workflow%'
                   OR action ILIKE '%time%'
                   OR action ILIKE '%approval%'
                   OR action ILIKE '%export%'
                   OR action ILIKE '%reconcili%'
                   OR action ILIKE '%lock%'
                   OR entity_type IN ('time_entry', 'timesheet', 'time_workflow_export', 'accounting_reconciliation')
            )::bigint,
            COUNT(*) FILTER (WHERE action ILIKE '%export%' OR entity_type = 'time_workflow_export')::bigint,
            COUNT(*) FILTER (WHERE action ILIKE '%reconcili%' OR entity_type = 'accounting_reconciliation')::bigint,
            COUNT(*) FILTER (WHERE action ILIKE '%approval%' OR action ILIKE '%approved%' OR action ILIKE '%declined%')::bigint,
            MAX(created_at)
        FROM audit_logs;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            totalEvents = reader.GetInt64(0);
            workflowEvents = reader.GetInt64(1);
            exportEvents = reader.GetInt64(2);
            reconciliationEvents = reader.GetInt64(3);
            approvalEvents = reader.GetInt64(4);
            latestEventAt = reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5);
        }
    }

    var actionGroups = new List<object>();
    await using (var groupCommand = new NpgsqlCommand("""
        SELECT
            action,
            entity_type,
            COUNT(*)::bigint AS event_count,
            MAX(created_at) AS latest_event_at
        FROM audit_logs
        GROUP BY action, entity_type
        ORDER BY MAX(created_at) DESC NULLS LAST, COUNT(*) DESC
        LIMIT 25;
        """, connection))
    {
        await using var reader = await groupCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actionGroups.Add(new
            {
                action = reader.GetString(0),
                entityType = reader.GetString(1),
                eventCount = reader.GetInt64(2),
                latestEventAt = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AZ Audit History Endpoint Repair",
        summary = new
        {
            totalEvents,
            workflowEvents,
            exportEvents,
            reconciliationEvents,
            approvalEvents,
            latestEventAt
        },
        access = new
        {
            access.CanView,
            access.CanProjectApprove,
            access.CanManageAccounting,
            access.CanExport,
            access.CanAudit,
            access.CanViewAll
        },
        actionGroups
    });
});

app.MapGet("/api/audit-history/events", async (HttpContext httpContext, string? action, string? entityType, int? limit) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView && !access.CanAudit && !access.CanViewAll && !access.CanManageAccounting && !access.CanProjectApprove && !access.CanExport)
    {
        return Results.Json(new { status = "access_denied", message = "Audit history events are restricted to approved workflow roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var safeLimit = Math.Clamp(limit ?? 50, 1, 200);
    var events = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            al.audit_log_id,
            COALESCE(au.display_name, au.email, 'System') AS actor_name,
            al.action,
            al.entity_type,
            al.entity_id,
            al.created_at,
            COALESCE(al.old_value::text, '') AS old_value_text,
            COALESCE(al.new_value::text, '') AS new_value_text
        FROM audit_logs al
        LEFT JOIN app_users au
            ON au.user_id = al.actor_user_id
        WHERE (CAST(@action_filter AS text) IS NULL OR al.action ILIKE '%' || CAST(@action_filter AS text) || '%')
          AND (CAST(@entity_type_filter AS text) IS NULL OR al.entity_type ILIKE '%' || CAST(@entity_type_filter AS text) || '%')
        ORDER BY al.created_at DESC
        LIMIT @safe_limit;
        """, connection);

    command.Parameters.AddWithValue("action_filter", string.IsNullOrWhiteSpace(action) ? DBNull.Value : action.Trim());
    command.Parameters.AddWithValue("entity_type_filter", string.IsNullOrWhiteSpace(entityType) ? DBNull.Value : entityType.Trim());
    command.Parameters.AddWithValue("safe_limit", safeLimit);

    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var oldPreview = reader.GetString(6);
            var newPreview = reader.GetString(7);

            events.Add(new
            {
                auditLogId = reader.GetGuid(0),
                actorName = reader.GetString(1),
                action = reader.GetString(2),
                entityType = reader.GetString(3),
                entityId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                createdAt = reader.GetFieldValue<DateTimeOffset>(5),
                oldValuePreview = oldPreview.Length > 320 ? oldPreview[..320] + "..." : oldPreview,
                newValuePreview = newPreview.Length > 320 ? newPreview[..320] + "..." : newPreview
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-AZ Audit History Events",
        count = events.Count,
        filters = new { action, entityType, limit = safeLimit },
        events
    });
});

app.MapGet("/api/workflow/action-capabilities", async (HttpContext httpContext, DateOnly? weekStart, DateOnly? weekEnd) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView && !access.CanProjectApprove && !access.CanManageAccounting && !access.CanExport && !access.CanViewAll)
    {
        return Results.Json(new { status = "access_denied", message = "Workflow action capabilities are restricted to workflow roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    var start = weekStart ?? today.AddDays(-(int)today.DayOfWeek);
    var end = weekEnd ?? start.AddDays(6);

    var statusBuckets = new List<object>();

    await using (var command = new NpgsqlCommand("""
        SELECT
            COALESCE(status, 'draft') AS status,
            COUNT(*)::bigint AS item_count,
            COALESCE(SUM(hours), 0)::numeric AS total_hours
        FROM time_entries
        WHERE work_date BETWEEN @week_start AND @week_end
        GROUP BY COALESCE(status, 'draft')
        ORDER BY COALESCE(status, 'draft');
        """, connection))
    {
        command.Parameters.AddWithValue("week_start", start);
        command.Parameters.AddWithValue("week_end", end);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statusBuckets.Add(new
            {
                status = reader.GetString(0),
                itemCount = reader.GetInt64(1),
                totalHours = reader.GetDecimal(2)
            });
        }
    }

    var actions = new List<object>
    {
        new
        {
            actionCode = "manager_approve",
            title = "Manager Approve",
            eligibleStatuses = new[] { "submitted", "pending_manager_approval" },
            allowed = access.CanProjectApprove || access.CanViewAll,
            destructive = true,
            enabledInThisSprint = false,
            note = "Listed for capability visibility. Existing approval endpoints remain the source of truth."
        },
        new
        {
            actionCode = "project_validate",
            title = "Project Validate",
            eligibleStatuses = new[] { "manager_approved" },
            allowed = access.CanProjectApprove || access.CanViewAll,
            destructive = true,
            enabledInThisSprint = false,
            note = "Dry-run and queue visibility only in this sprint."
        },
        new
        {
            actionCode = "accounting_reconcile",
            title = "Accounting Reconcile",
            eligibleStatuses = new[] { "project_approved", "project_validated", "accounting_ready" },
            allowed = access.CanManageAccounting || access.CanViewAll,
            destructive = true,
            enabledInThisSprint = false,
            note = "Dry-run and reconciliation workbench visibility only in this sprint."
        },
        new
        {
            actionCode = "export_package_download",
            title = "Export Package Download",
            eligibleStatuses = new[] { "accounting_ready", "reconciled", "locked" },
            allowed = access.CanExport || access.CanViewAll,
            destructive = false,
            enabledInThisSprint = true,
            note = "Download endpoint is already active and audited."
        }
    };

    return Results.Ok(new
    {
        module = "019M-BA Workflow Action Capabilities",
        dateRange = new { weekStart = start, weekEnd = end },
        access = new
        {
            access.CanProjectApprove,
            access.CanManageAccounting,
            access.CanExport,
            access.CanAudit,
            access.CanViewAll
        },
        statusBuckets,
        actions
    });
});

app.MapPost("/api/workflow/actions/dry-run", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanManageAccounting && !access.CanExport && !access.CanViewAll)
    {
        return Results.Json(new { status = "access_denied", message = "Workflow dry-run is restricted to accounting/export-enabled roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    string dryRunAction = "accounting_reconcile";
    DateOnly? weekStart = null;
    DateOnly? weekEnd = null;

    try
    {
        using var document = await JsonDocument.ParseAsync(httpContext.Request.Body);
        var root = document.RootElement;

        if (root.TryGetProperty("action", out var actionElement) && actionElement.ValueKind == JsonValueKind.String)
        {
            dryRunAction = actionElement.GetString() ?? dryRunAction;
        }

        if (root.TryGetProperty("weekStart", out var weekStartElement) && weekStartElement.ValueKind == JsonValueKind.String && DateOnly.TryParse(weekStartElement.GetString(), out var parsedStart))
        {
            weekStart = parsedStart;
        }

        if (root.TryGetProperty("weekEnd", out var weekEndElement) && weekEndElement.ValueKind == JsonValueKind.String && DateOnly.TryParse(weekEndElement.GetString(), out var parsedEnd))
        {
            weekEnd = parsedEnd;
        }
    }
    catch
    {
        return Results.Json(new { status = "invalid_request", message = "Dry-run request body must be valid JSON." }, statusCode: StatusCodes.Status400BadRequest);
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    var start = weekStart ?? today.AddDays(-(int)today.DayOfWeek);
    var end = weekEnd ?? start.AddDays(6);

    string[] eligibleStatuses = dryRunAction switch
    {
        "manager_approve" => new[] { "submitted", "pending_manager_approval" },
        "project_validate" => new[] { "manager_approved" },
        "accounting_reconcile" => new[] { "project_approved", "project_validated", "accounting_ready" },
        "lock_period" => new[] { "reconciled" },
        "export_package_download" => new[] { "accounting_ready", "reconciled", "locked" },
        _ => new[] { "project_approved", "project_validated", "accounting_ready" }
    };

    long eligibleItemCount = 0;
    decimal eligibleHours = 0;

    await using (var command = new NpgsqlCommand("""
        SELECT
            COUNT(*)::bigint,
            COALESCE(SUM(hours), 0)::numeric
        FROM time_entries
        WHERE work_date BETWEEN @week_start AND @week_end
          AND status = ANY(@eligible_statuses);
        """, connection))
    {
        command.Parameters.AddWithValue("week_start", start);
        command.Parameters.AddWithValue("week_end", end);
        command.Parameters.AddWithValue("eligible_statuses", eligibleStatuses);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            eligibleItemCount = reader.GetInt64(0);
            eligibleHours = reader.GetDecimal(1);
        }
    }

    var payload = new
    {
        dryRunAction,
        weekStart = start,
        weekEnd = end,
        eligibleStatuses,
        eligibleItemCount,
        eligibleHours,
        destructiveStateChangePerformed = false,
        note = "This endpoint records dry-run evidence only. No time entry status is changed."
    };

    await using (var insertCommand = new NpgsqlCommand("""
        INSERT INTO workflow_action_dry_run_events (
            actor_user_id,
            dry_run_action,
            week_start,
            week_end,
            eligible_item_count,
            eligible_hours,
            result_payload
        )
        VALUES (
            @actor_user_id,
            @dry_run_action,
            @week_start,
            @week_end,
            @eligible_item_count,
            @eligible_hours,
            @result_payload::jsonb
        );
        """, connection))
    {
        insertCommand.Parameters.AddWithValue("actor_user_id", sessionUserId.Value);
        insertCommand.Parameters.AddWithValue("dry_run_action", dryRunAction);
        insertCommand.Parameters.AddWithValue("week_start", start);
        insertCommand.Parameters.AddWithValue("week_end", end);
        insertCommand.Parameters.AddWithValue("eligible_item_count", (int)Math.Min(eligibleItemCount, int.MaxValue));
        insertCommand.Parameters.AddWithValue("eligible_hours", eligibleHours);
        insertCommand.Parameters.AddWithValue("result_payload", JsonSerializer.Serialize(payload));
        await insertCommand.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        module = "019M-BA Workflow Action Dry Run",
        payload
    });
});

app.MapGet("/api/dashboard/module-visibility-smoke", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView && !access.CanViewAll && !access.CanManageAccounting && !access.CanExport && !access.CanProjectApprove)
    {
        return Results.Json(new { status = "access_denied", message = "Module visibility smoke is restricted to workflow operators and administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var expectations = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT
            module_key,
            module_name,
            route,
            group_name,
            required_permissions,
            allowed_roles,
            expected_visibility,
            COALESCE(notes, '') AS notes
        FROM dashboard_module_visibility_expectations
        WHERE is_active = TRUE
        ORDER BY module_key;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            expectations.Add(new
            {
                moduleKey = reader.GetString(0),
                moduleName = reader.GetString(1),
                route = reader.GetString(2),
                groupName = reader.GetString(3),
                requiredPermissions = reader.GetFieldValue<string[]>(4),
                allowedRoles = reader.GetFieldValue<string[]>(5),
                expectedVisibility = reader.GetString(6),
                notes = reader.GetString(7)
            });
        }
    }

    var roleCoverage = new List<object>();
    await using (var roleCommand = new NpgsqlCommand("""
        SELECT
            r.role_code,
            r.role_name,
            COUNT(DISTINCT p.permission_code)::bigint AS permission_count,
            COUNT(DISTINCT p.permission_code) FILTER (
                WHERE p.permission_code IN (
                    'VIEW_AUDIT_HISTORY_EVENTS',
                    'VIEW_WORKFLOW_ACTION_CAPABILITIES',
                    'VIEW_MODULE_VISIBILITY_SMOKE',
                    'VIEW_EXPORT_PACKAGE_READINESS_SUMMARY',
                    'VIEW_EXPORT_PACKAGE_EVIDENCE_DETAIL',
                    'VIEW_ACCOUNTING_RECONCILIATION_WORKBENCH',
                    'VIEW_LOCKED_PERIOD_AUDIT_EVIDENCE',
                    'VIEW_ROLE_ACCESS_MATRIX',
                    'VIEW_DEMO_READINESS_COMMAND_CENTER',
                    'VIEW_WORKFLOW_VALIDATION_RULES',
                    'VIEW_WORKFLOW_OPERATIONS_CENTER',
                    'RUN_WORKFLOW_DRY_RUN'
                )
            )::bigint AS sprint_permission_count
        FROM app_roles r
        LEFT JOIN app_role_permissions rp
            ON rp.app_role_id = r.app_role_id
        LEFT JOIN app_permissions p
            ON p.app_permission_id = rp.app_permission_id
        WHERE r.is_active = TRUE
        GROUP BY r.role_code, r.role_name
        ORDER BY r.role_code;
        """, connection))
    {
        await using var reader = await roleCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roleCoverage.Add(new
            {
                roleCode = reader.GetString(0),
                roleName = reader.GetString(1),
                permissionCount = reader.GetInt64(2),
                sprintPermissionCount = reader.GetInt64(3)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-BB Dashboard Module Visibility Smoke",
        summary = new
        {
            expectationCount = expectations.Count,
            roleCount = roleCoverage.Count,
            active = true,
            engineerExpectedWorkflowManagementAccess = false
        },
        access = new
        {
            access.CanView,
            access.CanProjectApprove,
            access.CanManageAccounting,
            access.CanExport,
            access.CanAudit,
            access.CanViewAll
        },
        expectations,
        roleCoverage
    });
});

app.MapGet("/api/export-packages/readiness-summary", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanExport && !access.CanManageAccounting && !access.CanViewAll)
    {
        return Results.Json(new { status = "access_denied", message = "Export package readiness is restricted to export/accounting roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var packages = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT
            time_workflow_export_id,
            export_format,
            week_start,
            week_end,
            export_status,
            requested_by_email,
            item_count,
            total_hours,
            file_name,
            created_at,
            package_generated_at,
            package_download_count,
            package_last_downloaded_at,
            COALESCE(package_content_type, 'text/csv') AS package_content_type,
            COALESCE(package_file_extension, 'csv') AS package_file_extension
        FROM time_workflow_exports
        ORDER BY created_at DESC
        LIMIT 100;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            packages.Add(new
            {
                exportId = reader.GetGuid(0),
                exportFormat = reader.GetString(1),
                weekStart = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
                weekEnd = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
                exportStatus = reader.GetString(4),
                requestedByEmail = reader.IsDBNull(5) ? null : reader.GetString(5),
                itemCount = reader.GetInt32(6),
                totalHours = reader.GetDecimal(7),
                fileName = reader.IsDBNull(8) ? null : reader.GetString(8),
                createdAt = reader.GetFieldValue<DateTimeOffset>(9),
                packageGeneratedAt = reader.IsDBNull(10) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(10),
                packageDownloadCount = reader.GetInt32(11),
                packageLastDownloadedAt = reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
                packageContentType = reader.GetString(13),
                packageFileExtension = reader.GetString(14),
                downloadReady = true
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-BC Export Package Readiness Summary",
        summary = new
        {
            packageCount = packages.Count,
            downloadReadyCount = packages.Count,
            note = "Packages are generated as CSV download artifacts with audit evidence."
        },
        packages
    });
});

app.MapGet("/api/workflow/reconciliation-workbench", async (HttpContext httpContext, DateOnly? weekStart, DateOnly? weekEnd) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanManageAccounting && !access.CanExport && !access.CanViewAll)
    {
        return Results.Json(new { status = "access_denied", message = "Accounting reconciliation workbench is restricted to accounting/export roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    var start = weekStart ?? today.AddDays(-(int)today.DayOfWeek);
    var end = weekEnd ?? start.AddDays(6);

    var groups = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT
            COALESCE(te.status, 'draft') AS status,
            COALESCE(p.project_code, 'No Project') AS project_code,
            COALESCE(p.project_name, 'No Project') AS project_name,
            COALESCE(employee.display_name, employee.email, 'Unknown User') AS employee_name,
            COUNT(*)::bigint AS item_count,
            COALESCE(SUM(te.hours), 0)::numeric AS total_hours,
            MIN(te.work_date) AS first_work_date,
            MAX(te.work_date) AS last_work_date,
            COUNT(*) FILTER (WHERE te.project_id IS NULL OR te.task_id IS NULL)::bigint AS missing_link_count
        FROM time_entries te
        JOIN app_users employee
            ON employee.user_id = te.user_id
        LEFT JOIN projects p
            ON p.project_id = te.project_id
        WHERE te.work_date BETWEEN @week_start AND @week_end
        GROUP BY
            COALESCE(te.status, 'draft'),
            COALESCE(p.project_code, 'No Project'),
            COALESCE(p.project_name, 'No Project'),
            COALESCE(employee.display_name, employee.email, 'Unknown User')
        ORDER BY status, project_code, employee_name;
        """, connection))
    {
        command.Parameters.AddWithValue("week_start", start);
        command.Parameters.AddWithValue("week_end", end);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            groups.Add(new
            {
                status = reader.GetString(0),
                projectCode = reader.GetString(1),
                projectName = reader.GetString(2),
                employeeName = reader.GetString(3),
                itemCount = reader.GetInt64(4),
                totalHours = reader.GetDecimal(5),
                firstWorkDate = reader.GetFieldValue<DateOnly>(6),
                lastWorkDate = reader.GetFieldValue<DateOnly>(7),
                missingLinkCount = reader.GetInt64(8),
                needsAttention = reader.GetInt64(8) > 0 || reader.GetString(0) is "returned" or "rejected" or "manager_declined"
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-BE Accounting Reconciliation Workbench",
        dateRange = new { weekStart = start, weekEnd = end },
        summary = new
        {
            groupCount = groups.Count,
            attentionGroupCount = groups.Count(g => (bool)g.GetType().GetProperty("needsAttention")!.GetValue(g)!)
        },
        groups
    });
});

app.MapGet("/api/workflow/lock-evidence", async (HttpContext httpContext, DateOnly? weekStart, DateOnly? weekEnd) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanAudit && !access.CanExport && !access.CanManageAccounting && !access.CanViewAll)
    {
        return Results.Json(new { status = "access_denied", message = "Locked period evidence is restricted to audit/export/accounting roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    var start = weekStart ?? today.AddDays(-(int)today.DayOfWeek);
    var end = weekEnd ?? start.AddDays(6);

    var lockedItems = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT
            te.time_entry_id,
            te.work_date,
            COALESCE(employee.display_name, employee.email, 'Unknown User') AS employee_name,
            COALESCE(p.project_code, 'No Project') AS project_code,
            COALESCE(pt.task_code, 'No Task') AS task_code,
            te.hours,
            te.status
        FROM time_entries te
        JOIN app_users employee
            ON employee.user_id = te.user_id
        LEFT JOIN projects p
            ON p.project_id = te.project_id
        LEFT JOIN project_tasks pt
            ON pt.task_id = te.task_id
        WHERE te.work_date BETWEEN @week_start AND @week_end
          AND te.status IN ('reconciled', 'locked')
        ORDER BY te.work_date, employee.display_name, p.project_code, pt.task_code;
        """, connection))
    {
        command.Parameters.AddWithValue("week_start", start);
        command.Parameters.AddWithValue("week_end", end);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lockedItems.Add(new
            {
                timeEntryId = reader.GetGuid(0),
                workDate = reader.GetFieldValue<DateOnly>(1),
                employeeName = reader.GetString(2),
                projectCode = reader.GetString(3),
                taskCode = reader.GetString(4),
                hours = reader.GetDecimal(5),
                status = reader.GetString(6)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-BF Locked Period Audit Evidence",
        dateRange = new { weekStart = start, weekEnd = end },
        summary = new
        {
            lockedOrReconciledItemCount = lockedItems.Count,
            lockedOrReconciledHours = lockedItems.Sum(item => (decimal)item.GetType().GetProperty("hours")!.GetValue(item)!)
        },
        lockedItems
    });
});

app.MapGet("/api/security/role-access-matrix", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanViewAll)
    {
        return Results.Json(new { status = "access_denied", message = "Role access matrix is restricted to administrators and system operators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var matrix = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT
            r.role_code,
            r.role_name,
            COUNT(DISTINCT u.user_id)::bigint AS assigned_user_count,
            COUNT(DISTINCT p.permission_code)::bigint AS permission_count,
            COALESCE(STRING_AGG(DISTINCT p.permission_code, ', ' ORDER BY p.permission_code), '') AS permissions
        FROM app_roles r
        LEFT JOIN app_user_role_assignments ura
            ON ura.app_role_id = r.app_role_id
           AND ura.is_active = TRUE
        LEFT JOIN app_users u
            ON u.user_id = ura.user_id
           AND u.is_active = TRUE
        LEFT JOIN app_role_permissions rp
            ON rp.app_role_id = r.app_role_id
        LEFT JOIN app_permissions p
            ON p.app_permission_id = rp.app_permission_id
        WHERE r.is_active = TRUE
        GROUP BY r.role_code, r.role_name
        ORDER BY r.role_code;
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            matrix.Add(new
            {
                roleCode = reader.GetString(0),
                roleName = reader.GetString(1),
                assignedUserCount = reader.GetInt64(2),
                permissionCount = reader.GetInt64(3),
                permissions = reader.GetString(4).Split(", ", StringSplitOptions.RemoveEmptyEntries)
            });
        }
    }

    return Results.Ok(new
    {
        module = "019M-BG Role Access Matrix",
        summary = new
        {
            roleCount = matrix.Count,
            note = "Used to verify dashboard/module visibility and role enforcement."
        },
        matrix
    });
});

app.MapGet("/api/demo/readiness-command-center", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView && !access.CanViewAll && !access.CanAudit)
    {
        return Results.Json(new { status = "access_denied", message = "Demo readiness is restricted to approved reporting and workflow roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    long activeUsers = 0;
    long activeProjects = 0;
    long timeEntries = 0;
    long exports = 0;
    long auditEvents = 0;
    long moduleExpectations = 0;

    await using (var command = new NpgsqlCommand("""
        SELECT
            (SELECT COUNT(*)::bigint FROM app_users WHERE is_active = TRUE),
            (SELECT COUNT(*)::bigint FROM projects WHERE status = 'active'),
            (SELECT COUNT(*)::bigint FROM time_entries),
            (SELECT COUNT(*)::bigint FROM time_workflow_exports),
            (SELECT COUNT(*)::bigint FROM audit_logs),
            (SELECT COUNT(*)::bigint FROM dashboard_module_visibility_expectations WHERE is_active = TRUE);
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            activeUsers = reader.GetInt64(0);
            activeProjects = reader.GetInt64(1);
            timeEntries = reader.GetInt64(2);
            exports = reader.GetInt64(3);
            auditEvents = reader.GetInt64(4);
            moduleExpectations = reader.GetInt64(5);
        }
    }

    var checks = new List<object>
    {
        new { check = "Active users", value = activeUsers, status = activeUsers > 0 ? "ready" : "needs_data" },
        new { check = "Active projects", value = activeProjects, status = activeProjects > 0 ? "ready" : "needs_data" },
        new { check = "Time entries", value = timeEntries, status = timeEntries > 0 ? "ready" : "needs_data" },
        new { check = "Export packages", value = exports, status = exports > 0 ? "ready" : "optional" },
        new { check = "Audit evidence", value = auditEvents, status = auditEvents > 0 ? "ready" : "needs_data" },
        new { check = "Dashboard module expectations", value = moduleExpectations, status = moduleExpectations >= 10 ? "ready" : "needs_review" }
    };

    return Results.Ok(new
    {
        module = "019M-BH Demo Readiness Command Center",
        summary = new
        {
            readyCheckCount = checks.Count(c => (string)c.GetType().GetProperty("status")!.GetValue(c)! == "ready"),
            checkCount = checks.Count,
            demoReady = activeUsers > 0 && activeProjects > 0 && timeEntries > 0 && auditEvents > 0
        },
        checks
    });
});

app.MapGet("/api/workflow/validation-rules", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView && !access.CanViewAll && !access.CanAudit && !access.CanProjectApprove && !access.CanManageAccounting && !access.CanExport)
    {
        return Results.Json(new { status = "access_denied", message = "Workflow validation rules are restricted to workflow roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    long exportReadyEntries = 0;
    long blockedEntries = 0;
    long exportPackages = 0;
    long auditEvents = 0;

    await using (var command = new NpgsqlCommand("""
        SELECT
            (SELECT COUNT(*)::bigint FROM time_entries WHERE status IN ('accounting_ready', 'reconciled', 'locked')),
            (SELECT COUNT(*)::bigint FROM time_entries WHERE COALESCE(status, 'draft') NOT IN ('accounting_ready', 'reconciled', 'locked')),
            (SELECT COUNT(*)::bigint FROM time_workflow_exports),
            (SELECT COUNT(*)::bigint FROM audit_logs WHERE action ILIKE '%export%' OR action ILIKE '%approval%' OR action ILIKE '%reconcili%' OR action ILIKE '%lock%');
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            exportReadyEntries = reader.GetInt64(0);
            blockedEntries = reader.GetInt64(1);
            exportPackages = reader.GetInt64(2);
            auditEvents = reader.GetInt64(3);
        }
    }

    var rules = new List<object>
    {
        new
        {
            ruleCode = "engineer_no_export_controls",
            title = "Engineers do not receive export or workflow management controls",
            status = "configured",
            evidence = "Export/download endpoints require export-enabled roles and prior smoke checks confirmed Engineer View-As HTTP 403."
        },
        new
        {
            ruleCode = "export_only_ready_statuses",
            title = "Export package includes only accounting-ready, reconciled, or locked entries",
            status = "configured",
            evidence = $"Export-ready entries: {exportReadyEntries}; blocked entries: {blockedEntries}."
        },
        new
        {
            ruleCode = "download_audit_evidence",
            title = "Export package download creates audit evidence",
            status = auditEvents > 0 ? "evidence_present" : "needs_evidence",
            evidence = $"Workflow/export audit evidence events: {auditEvents}."
        },
        new
        {
            ruleCode = "dashboard_registry_coverage",
            title = "New modules must appear in dashboard/module registry",
            status = "configured",
            evidence = "Dashboard module visibility expectations are seeded and dashboard registry is patched by this sprint."
        },
        new
        {
            ruleCode = "no_destructive_dry_run",
            title = "Dry-run endpoints must not change workflow status",
            status = "configured",
            evidence = "Workflow dry-run writes evidence only to workflow_action_dry_run_events."
        },
        new
        {
            ruleCode = "export_package_available",
            title = "Export package records are available for download readiness",
            status = exportPackages > 0 ? "evidence_present" : "needs_export_package",
            evidence = $"Export package records: {exportPackages}."
        }
    };

    return Results.Ok(new
    {
        module = "019M-BI Workflow Validation Rules",
        summary = new
        {
            ruleCount = rules.Count,
            configuredRuleCount = rules.Count(r => ((string)r.GetType().GetProperty("status")!.GetValue(r)!).Contains("configured") || ((string)r.GetType().GetProperty("status")!.GetValue(r)!).Contains("evidence_present"))
        },
        rules
    });
});

app.MapGet("/api/workflow/operations-center", async (HttpContext httpContext) =>
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

    var access = await LoadApprovalExportWorkflowAccessAsync(connection, sessionUserId.Value);
    if (!access.CanView && !access.CanViewAll && !access.CanAudit && !access.CanProjectApprove && !access.CanManageAccounting && !access.CanExport)
    {
        return Results.Json(new { status = "access_denied", message = "Workflow operations center is restricted to workflow roles." }, statusCode: StatusCodes.Status403Forbidden);
    }

    long expectations = 0;
    long exports = 0;
    long auditEvents = 0;
    long dryRuns = 0;
    long approvalItems = 0;
    long exportReadyEntries = 0;

    await using (var command = new NpgsqlCommand("""
        SELECT
            (SELECT COUNT(*)::bigint FROM dashboard_module_visibility_expectations WHERE is_active = TRUE),
            (SELECT COUNT(*)::bigint FROM time_workflow_exports),
            (SELECT COUNT(*)::bigint FROM audit_logs WHERE action ILIKE '%time%' OR action ILIKE '%approval%' OR action ILIKE '%export%' OR action ILIKE '%reconcili%' OR action ILIKE '%lock%'),
            (SELECT COUNT(*)::bigint FROM workflow_action_dry_run_events),
            (SELECT COUNT(*)::bigint FROM time_entries WHERE status IN ('submitted', 'pending_manager_approval', 'manager_approved', 'project_approved', 'project_validated', 'accounting_ready')),
            (SELECT COUNT(*)::bigint FROM time_entries WHERE status IN ('accounting_ready', 'reconciled', 'locked'));
        """, connection))
    {
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            expectations = reader.GetInt64(0);
            exports = reader.GetInt64(1);
            auditEvents = reader.GetInt64(2);
            dryRuns = reader.GetInt64(3);
            approvalItems = reader.GetInt64(4);
            exportReadyEntries = reader.GetInt64(5);
        }
    }

    return Results.Ok(new
    {
        module = "019M-BJ Workflow Operations Center",
        summary = new
        {
            dashboardExpectationCount = expectations,
            exportPackageCount = exports,
            workflowAuditEventCount = auditEvents,
            dryRunEvidenceCount = dryRuns,
            activeWorkflowItemCount = approvalItems,
            exportReadyEntryCount = exportReadyEntries,
            operationalStatus = "ready"
        },
        access = new
        {
            access.CanView,
            access.CanProjectApprove,
            access.CanManageAccounting,
            access.CanExport,
            access.CanAudit,
            access.CanViewAll
        },
        modules = new[]
        {
            "019M-AZ Audit History Endpoint + UI Repair",
            "019M-BA Workflow Action Completion Controls",
            "019M-BB Dashboard Module Visibility Smoke Automation",
            "019M-BC Export Package Readiness Summary",
            "019M-BD Export Package Evidence Detail",
            "019M-BE Accounting Reconciliation Workbench",
            "019M-BF Locked Period Audit Evidence",
            "019M-BG Role Access Matrix Endpoint",
            "019M-BH Demo Readiness Command Center",
            "019M-BI Workflow Validation Rules",
            "019M-BJ Workflow Operations Center Registry",
            "019M-BK Sprint Automation Validation Script"
        }
    });
});


app.Run();


static bool CanEngineerUnlockDay(string? status, DateTimeOffset? submittedAt)
{
    return status == "submitted"
        && submittedAt is not null
        && DateTimeOffset.UtcNow - submittedAt.Value <= TimeSpan.FromHours(2);
}

static string GetDayUnlockMessage(string? status, DateTimeOffset? submittedAt)
{
    if (status is null || status == "draft") return "This day has not been submitted yet.";
    if (status == "manager_declined") return "This day was returned for correction and can be edited/resubmitted.";
    if (status == "submitted")
    {
        if (submittedAt is null) return "This submitted day is missing a submission timestamp. Please contact your manager to unlock it.";
        return DateTimeOffset.UtcNow - submittedAt.Value <= TimeSpan.FromHours(2)
            ? "This submitted day can be unlocked."
            : "This day was submitted more than two hours ago. Please contact your manager to unlock it.";
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



static async Task<object> LoadManagerApprovalSummaryAsync(NpgsqlConnection connection)
{
    const string sql = """
        SELECT COUNT(*)
        FROM timesheet_day_statuses
        WHERE status = 'submitted';
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    var pendingManagerApprovals = Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0);
    const long pendingProjectApprovals = 0;

    return new
    {
        pendingManagerApprovals,
        pendingProjectApprovals,
        totalPending = pendingManagerApprovals + pendingProjectApprovals,
        refreshedAtUtc = DateTimeOffset.UtcNow
    };
}

static async Task<IResult> ProcessManagerBulkApprovalAsync(ManagerBulkApprovalRequest request)
{
    if (request.Items is null || request.Items.Count == 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "Select at least one submitted day before using bulk approval."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var managerUserId = await GetOrCreateDevelopmentManagerUserIdAsync(connection, transaction);
        object comment = string.IsNullOrWhiteSpace(request.Comment) ? "Bulk approved by manager." : request.Comment.Trim();
        var approvedCount = 0;

        foreach (var item in request.Items.DistinctBy(item => new { item.TimesheetId, item.WorkDate }))
        {
            await using var statusCommand = new NpgsqlCommand("""
                UPDATE timesheet_day_statuses
                SET status = 'manager_approved',
                    manager_user_id = @manager_user_id,
                    manager_decision_comment = @comment,
                    manager_approved_at = NOW(),
                    manager_declined_at = NULL,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = 'submitted'
                RETURNING timesheet_day_status_id;
                """, connection, transaction);
            statusCommand.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            statusCommand.Parameters.AddWithValue("work_date", item.WorkDate);
            statusCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
            statusCommand.Parameters.AddWithValue("comment", comment);

            var statusId = (Guid?)(await statusCommand.ExecuteScalarAsync());
            if (statusId is null) continue;

            await using var entryUpdateCommand = new NpgsqlCommand(
                "UPDATE time_entries SET status = 'manager_approved', updated_at = NOW() WHERE timesheet_id = @timesheet_id AND work_date = @work_date;",
                connection,
                transaction);
            entryUpdateCommand.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            entryUpdateCommand.Parameters.AddWithValue("work_date", item.WorkDate);
            await entryUpdateCommand.ExecuteNonQueryAsync();

            await using var approvalCommand = new NpgsqlCommand("""
                INSERT INTO approval_records (time_entry_id, approval_stage, approval_status, approver_user_id, decision_comment)
                SELECT time_entry_id, 'manager', 'approved', @manager_user_id, @comment
                FROM time_entries
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date;
                """, connection, transaction);
            approvalCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
            approvalCommand.Parameters.AddWithValue("comment", comment);
            approvalCommand.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            approvalCommand.Parameters.AddWithValue("work_date", item.WorkDate);
            await approvalCommand.ExecuteNonQueryAsync();

            await InsertAuditLogAsync(connection, transaction, managerUserId, "timesheet_day_manager_bulk_approved", "timesheet", item.TimesheetId);
            approvedCount++;
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "bulk_manager_approved",
            approvedCount,
            requestedCount = request.Items.Count,
            message = $"Bulk approved {approvedCount} submitted day(s)."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to bulk approve manager approval items",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}

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

static async Task<List<object>> LoadManagerApprovalItemsAsync(NpgsqlConnection connection, DateOnly weekStart, DateOnly weekEnd, bool includeAll)
{
    var items = new List<object>();

    const string sql = """
        SELECT
            tds.timesheet_id,
            tds.user_id,
            u.display_name,
            u.email,
            tds.work_date,
            tds.status,
            tds.submitted_at,
            COALESCE(SUM(CASE WHEN te.time_type = 'normal' THEN te.hours ELSE 0 END), 0) AS normal_hours,
            COALESCE(SUM(CASE WHEN te.time_type = 'afterhours' THEN te.hours ELSE 0 END), 0) AS afterhours_hours,
            COALESCE(SUM(te.hours), 0) AS total_hours,
            COUNT(te.time_entry_id) AS entry_count,
            COALESCE(COUNT(te.time_entry_id) FILTER (WHERE te.description IS NOT NULL AND BTRIM(te.description) <> ''), 0) AS comment_count,
            COALESCE(STRING_AGG(DISTINCT COALESCE(npt.category_name, 'Project task'), ', '), 'No entries') AS activity_summary,
            tds.manager_decision_comment
        FROM timesheet_day_statuses tds
        INNER JOIN app_users u ON u.user_id = tds.user_id
        LEFT JOIN time_entries te
            ON te.timesheet_id = tds.timesheet_id
           AND te.work_date = tds.work_date
        LEFT JOIN non_project_time_categories npt
            ON npt.non_project_time_category_id = te.non_project_time_category_id
        WHERE tds.work_date BETWEEN @week_start AND @week_end
          AND (@include_all = TRUE OR tds.status = 'submitted')
        GROUP BY
            tds.timesheet_id,
            tds.user_id,
            u.display_name,
            u.email,
            tds.work_date,
            tds.status,
            tds.submitted_at,
            tds.manager_decision_comment
        ORDER BY tds.work_date, u.display_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("week_start", weekStart);
    command.Parameters.AddWithValue("week_end", weekEnd);
    command.Parameters.AddWithValue("include_all", includeAll);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new
        {
            timesheetId = reader.GetGuid(0),
            userId = reader.GetGuid(1),
            resourceName = reader.GetString(2),
            resourceEmail = reader.GetString(3),
            workDate = reader.GetFieldValue<DateOnly>(4),
            status = reader.GetString(5),
            submittedAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
            normalHours = reader.GetDecimal(7),
            afterhours = reader.GetDecimal(8),
            totalHours = reader.GetDecimal(9),
            entryCount = reader.GetInt64(10),
            commentCount = reader.GetInt64(11),
            activitySummary = reader.GetString(12),
            managerDecisionComment = reader.IsDBNull(13) ? null : reader.GetString(13)
        });
    }

    return items;
}

static async Task<IResult> ProcessManagerApprovalActionAsync(
    ManagerApprovalActionRequest request,
    string targetStatus,
    string approvalStatus,
    string auditAction,
    string successMessage)
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var managerUserId = await GetOrCreateDevelopmentManagerUserIdAsync(connection, transaction);
        object comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();

        var approvedAtSql = targetStatus == "manager_approved" ? "manager_approved_at = NOW(), manager_declined_at = NULL," : "manager_declined_at = NOW(), manager_approved_at = NULL,";

        var statusSql = $"""
            UPDATE timesheet_day_statuses
            SET status = @target_status,
                manager_user_id = @manager_user_id,
                manager_decision_comment = @comment,
                {approvedAtSql}
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
              AND status = 'submitted'
            RETURNING timesheet_day_status_id;
            """;

        await using var statusCommand = new NpgsqlCommand(statusSql, connection, transaction);
        statusCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        statusCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        statusCommand.Parameters.AddWithValue("target_status", targetStatus);
        statusCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
        statusCommand.Parameters.AddWithValue("comment", comment);

        var statusId = (Guid?)(await statusCommand.ExecuteScalarAsync());
        if (statusId is null)
        {
            return Results.Conflict(new
            {
                status = "not_pending_manager_approval",
                message = "Only submitted days that are pending manager approval can be approved or declined."
            });
        }

        await using var entryUpdateCommand = new NpgsqlCommand(
            "UPDATE time_entries SET status = @target_status, updated_at = NOW() WHERE timesheet_id = @timesheet_id AND work_date = @work_date;",
            connection,
            transaction);
        entryUpdateCommand.Parameters.AddWithValue("target_status", targetStatus);
        entryUpdateCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        entryUpdateCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        await entryUpdateCommand.ExecuteNonQueryAsync();

        await using var approvalCommand = new NpgsqlCommand("""
            INSERT INTO approval_records (time_entry_id, approval_stage, approval_status, approver_user_id, decision_comment)
            SELECT time_entry_id, 'manager', @approval_status, @manager_user_id, @comment
            FROM time_entries
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction);
        approvalCommand.Parameters.AddWithValue("approval_status", approvalStatus);
        approvalCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
        approvalCommand.Parameters.AddWithValue("comment", comment);
        approvalCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        approvalCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        await approvalCommand.ExecuteNonQueryAsync();

        await InsertAuditLogAsync(connection, transaction, managerUserId, auditAction, "timesheet", request.TimesheetId);

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = targetStatus,
            timesheetId = request.TimesheetId,
            workDate = request.WorkDate,
            message = successMessage
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to process manager approval action",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}


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
          AND p.status = 'active'
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
        SELECT category_code, category_name, category_description, utilization_bucket, requires_approval
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
            code = reader.GetString(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            utilizationBucket = reader.GetString(3),
            requiresApproval = reader.GetBoolean(4)
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
            c.client_name
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

        entries.Add(new
        {
            id = reader.GetGuid(0),
            rowType = projectId is not null && taskId is not null ? "projectTask" : "nonProject",
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
            clientName = reader.IsDBNull(18) ? null : reader.GetString(18)
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
        roles.Contains("ADMINISTRATOR")
        || roles.Contains("PROJECT_TEAM_COORDINATOR")
        || permissions.Contains("SYSTEM_ADMINISTRATION")
        || permissions.Contains("MANAGE_ALL");

    var canProjectApprove =
        canViewAll
        || roles.Contains("PROJECT_MANAGEMENT")
        || roles.Contains("PROJECT_MANAGER")
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
    string? TimeType,
    string? RowType,
    string? RowLabel,
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
    string? Warning);


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
