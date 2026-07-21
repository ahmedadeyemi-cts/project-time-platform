using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class SecurityDiagnosticsOperations
{
    internal const int MaximumRequestBytes = 32 * 1024;

    internal static async Task<AccessOutcome> AuthorizeAsync(
        HttpContext context,
        string module,
        string[] viewRoles,
        string[] manageRoles,
        string viewPermission,
        string managePermission)
    {
        var userId = ActualUserId(context);
        if (userId is null)
        {
            return AccessOutcome.Fail(Results.Json(new
            {
                module,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return AccessOutcome.Fail(Results.Json(new
            {
                module,
                status = "authorization_dependency_unavailable",
                message = "Operational authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT
                    EXISTS (
                        SELECT 1
                        FROM app_user_role_assignments ura
                        JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                        LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
                        LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
                        WHERE ura.user_id = @user_id AND ura.is_active = TRUE
                          AND (upper(COALESCE(r.role_code, '')) = ANY(@view_roles)
                               OR upper(COALESCE(p.permission_code, '')) = ANY(@view_permissions))
                    ),
                    EXISTS (
                        SELECT 1
                        FROM app_user_role_assignments ura
                        JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                        LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
                        LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
                        WHERE ura.user_id = @user_id AND ura.is_active = TRUE
                          AND (upper(COALESCE(r.role_code, '')) = ANY(@manage_roles)
                               OR upper(COALESCE(p.permission_code, '')) = ANY(@manage_permissions))
                    );
                """, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);
            command.Parameters.AddWithValue("view_roles", viewRoles);
            command.Parameters.AddWithValue("manage_roles", manageRoles);
            command.Parameters.AddWithValue("view_permissions", new[] { viewPermission, managePermission, "SYSTEM_ADMINISTRATION", "MANAGE_ALL" });
            command.Parameters.AddWithValue("manage_permissions", new[] { managePermission, "MANAGE_ALL" });

            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            await reader.ReadAsync(context.RequestAborted);
            var canView = reader.GetBoolean(0);
            var canManage = reader.GetBoolean(1);

            if (!canView)
            {
                await connection.DisposeAsync();
                return AccessOutcome.Fail(Results.Json(new
                {
                    module,
                    status = module == "997" ? "security_access_required" : "diagnostic_access_required",
                    message = "Your account is not authorized for this operational center."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new AccessOutcome(connection, new AccessContext(userId.Value, canView, canManage), null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("SecurityDiagnosticsOperations")
                .LogWarning("Module {Module} authorization dependency unavailable ({ExceptionType}).", module, exception.GetType().Name);
            return AccessOutcome.Fail(Results.Json(new
            {
                module,
                status = "authorization_dependency_unavailable",
                message = "Operational authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    internal static IResult? RequireMutation(HttpContext context, string module, AccessContext access)
    {
        if (IsViewAs(context))
        {
            return Results.Json(new
            {
                module,
                status = "view_as_write_blocked",
                message = "View-As is read-only and never transfers operational authority."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!access.CanManage)
        {
            return Results.Json(new
            {
                module,
                status = "management_authority_required",
                message = "This action requires operational management authority."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    internal static async Task<BodyOutcome<T>> ReadBodyAsync<T>(HttpContext context, string module)
    {
        if (context.Request.ContentLength is > MaximumRequestBytes)
        {
            return BodyOutcome<T>.Fail(Results.Json(new
            {
                module,
                status = "request_too_large",
                message = $"Request bodies are limited to {MaximumRequestBytes} bytes."
            }, statusCode: StatusCodes.Status413PayloadTooLarge));
        }

        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(cancellationToken: context.RequestAborted);
            return value is null
                ? BodyOutcome<T>.Fail(Results.BadRequest(new { module, status = "invalid_request", message = "A valid JSON request body is required." }))
                : new BodyOutcome<T>(value, null);
        }
        catch (JsonException)
        {
            return BodyOutcome<T>.Fail(Results.BadRequest(new { module, status = "invalid_request", message = "The JSON request body is invalid." }));
        }
    }

    internal static async Task<bool> OperationalSchemaAvailableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.projectpulse_security_incidents') IS NOT NULL
               AND to_regclass('public.projectpulse_diagnostic_sessions') IS NOT NULL
               AND to_regclass('public.projectpulse_remediation_requests') IS NOT NULL;
            """, connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    internal static IResult SchemaUnavailable(string module) => Results.Json(new
    {
        module,
        status = "operational_schema_unavailable",
        migration = "033_security_diagnostics_native_operations",
        message = "The security and diagnostics database migration has not been applied."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    internal static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string module,
        string entityType,
        string entityId,
        string actionCode,
        Guid actorUserId,
        object evidence,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO projectpulse_module_audit_events
            (event_id, module_number, entity_type, entity_id, action_code, actor_user_id, evidence_json)
            VALUES (@event_id, @module_number, @entity_type, @entity_id, @action_code, @actor_user_id, CAST(@evidence AS jsonb));
            """, connection, transaction);
        command.Parameters.AddWithValue("event_id", Guid.NewGuid());
        command.Parameters.AddWithValue("module_number", module);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("action_code", actionCode);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(evidence));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static object AccessResponse(AccessContext access, HttpContext context, string classification) => new
    {
        classification,
        serverAuthorized = access.CanView,
        canManage = access.CanManage,
        authoritySource = "actual_projectpulse_session",
        viewAsTransfersAuthority = false,
        isViewAs = IsViewAs(context)
    };

    internal static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true;

    internal static string RuntimeEnvironment()
    {
        var value = (Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT")
                     ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                     ?? "runtime_managed").Trim().ToLowerInvariant();
        if (value.Contains("prod", StringComparison.Ordinal)) return "production";
        if (value.Contains("test", StringComparison.Ordinal) || value.Contains("qa", StringComparison.Ordinal) || value.Contains("uat", StringComparison.Ordinal)) return "test";
        if (value.Contains("dev", StringComparison.Ordinal)) return "development";
        if (value.Contains("local", StringComparison.Ordinal)) return "local";
        return "runtime_managed";
    }

    private static Guid? ActualUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
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
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 10
        }.ConnectionString;
    }

    internal sealed record AccessOutcome(NpgsqlConnection? Connection, AccessContext? Access, IResult? Failure)
    {
        internal static AccessOutcome Fail(IResult failure) => new(null, null, failure);
    }

    internal sealed record AccessContext(Guid UserId, bool CanView, bool CanManage);
    internal sealed record BodyOutcome<T>(T? Value, IResult? Failure)
    {
        internal static BodyOutcome<T> Fail(IResult failure) => new(default, failure);
    }
}
