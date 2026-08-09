using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Fail-closed compatibility boundary for Modules 010/065/067.
/// It protects governed directory-import roles, separates read and write authority,
/// and hydrates every configured encrypted tenant secret after restart.
/// </summary>
public static class MicrosoftIntegrationSecurityCompatibility
{
    private const string ImportPath = "/api/microsoft-integration/directory-users/import-selected";
    private const string SecretPath = "/api/microsoft-integration/client-secret";
    private const string TestConnectionPath = "/api/microsoft-integration/test-connection";

    private static readonly HashSet<string> ImportPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_AZURE_AD",
        "MANAGE_AZURE_SYNC"
    };

    private static readonly HashSet<string> WritePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_ENTRA_SECRET",
        "MANAGE_GLOBAL_MAIL_CONFIGURATION",
        "MANAGE_GLOBAL_MAIL"
    };

    private static readonly HashSet<string> AllowedGovernedImportRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ENGINEER",
        "ENGINEERING",
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "SALES",
        "INSIDE_SALES",
        "SOLUTION_ARCHITECT"
    };

    public static WebApplication UseMicrosoftIntegrationSecurityCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (HttpMethods.IsPost(context.Request.Method)
                && path.Equals(ImportPath, StringComparison.OrdinalIgnoreCase))
            {
                var access = await ReadAccessAsync(context);
                if (access.Failure is not null)
                {
                    await access.Failure.ExecuteAsync(context);
                    return;
                }

                if (IsViewAs(context))
                {
                    await Results.Json(new
                    {
                        module = "010",
                        status = "view_as_read_only",
                        message = "Exit Administrator View-As before importing Entra users."
                    }, statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
                    return;
                }

                if (!access.Context!.Administrator
                    && !access.Context.Permissions.Any(ImportPermissions.Contains))
                {
                    await Results.Json(new
                    {
                        module = "010",
                        status = "azure_directory_import_access_required",
                        message = "Administrator or delegated Azure/Entra synchronization access is required."
                    }, statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
                    return;
                }

                var roleFailure = await EnforceGovernedImportRoleAsync(context, access.Context.ConnectionString);
                if (roleFailure is not null)
                {
                    await roleFailure.ExecuteAsync(context);
                    return;
                }

                await next();
                return;
            }

            if ((HttpMethods.IsPut(context.Request.Method) && path.Equals(SecretPath, StringComparison.OrdinalIgnoreCase))
                || (HttpMethods.IsPost(context.Request.Method) && path.Equals(TestConnectionPath, StringComparison.OrdinalIgnoreCase)))
            {
                var access = await ReadAccessAsync(context);
                if (access.Failure is not null)
                {
                    await access.Failure.ExecuteAsync(context);
                    return;
                }

                if (IsViewAs(context))
                {
                    await Results.Json(new
                    {
                        module = "065",
                        status = "view_as_read_only",
                        message = "Exit Administrator View-As before changing or testing Microsoft Integration credentials."
                    }, statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
                    return;
                }

                if (!access.Context!.Administrator
                    && !access.Context.Permissions.Any(WritePermissions.Contains))
                {
                    await Results.Json(new
                    {
                        module = "065",
                        status = "microsoft_integration_manage_access_required",
                        message = "Manage Microsoft Integration or secret-administration authority is required. View-only legacy mail permissions cannot change credentials or run privileged connection tests."
                    }, statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
                    return;
                }
            }

            await next();
        });

        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(HydrateEveryConfiguredTenantSecretAsync));
        return app;
    }

    private static async Task<IResult?> EnforceGovernedImportRoleAsync(HttpContext context, string connectionString)
    {
        context.Request.EnableBuffering();
        JsonObject payload;
        try
        {
            using var reader = new StreamReader(
                context.Request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var raw = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
            payload = JsonNode.Parse(raw) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return Results.BadRequest(new
            {
                module = "010",
                status = "invalid_request",
                message = "A valid selected-user import payload is required."
            });
        }

        var explicitRole = FirstString(payload, "defaultRoleCode", "default_role_code", "roleCode", "role_code");
        if (!string.IsNullOrWhiteSpace(explicitRole)
            && !explicitRole.Equals("ENGINEERING", StringComparison.OrdinalIgnoreCase)
            && !explicitRole.Equals("ENGINEER", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                module = "010",
                status = "client_selected_import_role_not_allowed",
                message = "The import role is governed by Microsoft Integration settings and cannot be selected by the request."
            });
        }

        var governedRole = NormalizeRole(await ReadGovernedImportRoleAsync(connectionString, context.RequestAborted));
        if (!AllowedGovernedImportRoles.Contains(governedRole))
        {
            return Results.Json(new
            {
                module = "010",
                status = "governed_import_role_not_allowed",
                governedRole,
                message = "The configured import role is privileged or unsupported. Select an approved non-administrative import role in Module 065 before importing users."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        foreach (var key in new[] { "defaultRoleCode", "default_role_code", "roleCode", "role_code" })
            payload.Remove(key);
        payload["defaultRoleCode"] = governedRole;

        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        context.Request.Body = new MemoryStream(bytes, writable: false);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/json";
        return null;
    }

    private static async Task<string> ReadGovernedImportRoleAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(
                    NULLIF(to_jsonb(settings)->>'defaultRoleCode', ''),
                    NULLIF(to_jsonb(settings)->>'default_role_code', ''),
                    'ENGINEERING')
                FROM azure_entra_settings settings
                LIMIT 1;
                """, connection);
            return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "ENGINEERING";
        }
        catch
        {
            return "ENGINEERING";
        }
    }

    private static async Task<BoundaryAccessResult> ReadAccessAsync(HttpContext context)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return new(null, Results.Json(new
            {
                status = "session_required",
                message = "A valid Pulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(role.role_code, ''), COALESCE(permission.permission_code, '')
                FROM app_user_role_assignments assignment
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                 AND role.is_active = TRUE
                LEFT JOIN app_role_permissions role_permission
                  ON role_permission.app_role_id = role.app_role_id
                LEFT JOIN app_permissions permission
                  ON permission.app_permission_id = role_permission.app_permission_id
                WHERE assignment.user_id = @user_id
                  AND assignment.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                var role = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var permission = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(role)) roles.Add(role);
                if (!string.IsNullOrWhiteSpace(permission)) permissions.Add(permission);
            }

            var administrator = ProjectPulseActualSessionAuthority.HasPermanentAdministratorAuthority(context, roles);
            return new(new BoundaryAccessContext(connectionString, administrator, permissions), null);
        }
        catch
        {
            return new(null, Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static async Task HydrateEveryConfiguredTenantSecretAsync()
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        byte[]? key = null;
        try
        {
            key = ResolveEncryptionKey();
            if (key is null) return;

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var activeTenantKey = await ReadActiveTenantKeyAsync(connection);
            await using var command = new NpgsqlCommand("""
                SELECT tenant_key, ciphertext, nonce, authentication_tag
                FROM microsoft_integration_client_secrets
                ORDER BY tenant_key;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tenantKey = reader.GetString(0);
                var ciphertext = (byte[])reader[1];
                var nonce = (byte[])reader[2];
                var tag = (byte[])reader[3];
                var plaintext = new byte[ciphertext.Length];
                try
                {
                    using var aes = new AesGcm(key, tag.Length);
                    aes.Decrypt(
                        nonce,
                        ciphertext,
                        tag,
                        plaintext,
                        Encoding.UTF8.GetBytes($"ProjectPulse:065:{tenantKey}"));
                    ApplyHydratedSecret(tenantKey, Encoding.UTF8.GetString(plaintext), activeTenantKey);
                }
                catch
                {
                    // One invalid tenant credential must not prevent other configured tenants from hydrating.
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }
        catch
        {
            // Startup remains fail-closed and preserves existing environment-based credentials.
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task<string> ReadActiveTenantKeyAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(
                    NULLIF(to_jsonb(settings)->>'activeTenantKey', ''),
                    NULLIF(to_jsonb(settings)->>'active_tenant_key', ''),
                    'default')
                FROM azure_entra_settings settings
                LIMIT 1;
                """, connection);
            return Convert.ToString(await command.ExecuteScalarAsync()) ?? "default";
        }
        catch
        {
            return "default";
        }
    }

    private static void ApplyHydratedSecret(string tenantKey, string secret, string activeTenantKey)
    {
        if (string.IsNullOrWhiteSpace(secret)) return;
        var specificName = $"PROJECTPULSE_MICROSOFT_TENANT_{SanitizeEnvironmentToken(tenantKey)}_CLIENT_SECRET";
        Environment.SetEnvironmentVariable(specificName, secret);

        if (tenantKey.Equals(activeTenantKey, StringComparison.OrdinalIgnoreCase)
            || tenantKey.Equals("default", StringComparison.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET", secret);

        if (tenantKey.Contains("ussignal", StringComparison.OrdinalIgnoreCase)
            || tenantKey.Contains("prod", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET", secret);
            Environment.SetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET", secret);
        }
        else
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET", secret);
        }
    }

    private static byte[]? ResolveEncryptionKey()
    {
        var configured = Environment.GetEnvironmentVariable("PROJECTPULSE_MICROSOFT_INTEGRATION_SECRET_KEY");
        if (string.IsNullOrWhiteSpace(configured))
            configured = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(configured)) return null;

        try
        {
            var decoded = Convert.FromBase64String(configured);
            if (decoded.Length == 32) return decoded;
            CryptographicOperations.ZeroMemory(decoded);
        }
        catch
        {
            // Non-base64 values are stretched with SHA-256.
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes($"ProjectPulse-Microsoft-Integration:{configured}"));
    }

    private static string NormalizeRole(string roleCode)
    {
        var normalized = (roleCode ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "" => "ENGINEER",
            "ENGINEERING" => "ENGINEER",
            _ => normalized
        };
    }

    private static string FirstString(JsonObject payload, params string[] names)
    {
        foreach (var name in names)
        {
            if (!payload.TryGetPropertyValue(name, out var value) || value is null) continue;
            var text = value.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return string.Empty;
    }

    private static string SanitizeEnvironmentToken(string value)
    {
        var normalized = new string((value ?? string.Empty)
            .ToUpperInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "DEFAULT" : normalized;
    }

    private static Guid? ActualSessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

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
            MaxPoolSize = 10
        }.ConnectionString;
    }

    private sealed record BoundaryAccessResult(BoundaryAccessContext? Context, IResult? Failure);
    private sealed record BoundaryAccessContext(
        string ConnectionString,
        bool Administrator,
        HashSet<string> Permissions);
}
