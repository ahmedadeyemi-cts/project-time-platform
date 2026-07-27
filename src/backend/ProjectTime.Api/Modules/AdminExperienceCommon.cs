using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class AdminExperienceCommon
{
    internal sealed record AccessContext(
        Guid UserId,
        string Email,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions,
        string ConnectionString);

    internal sealed record AccessResult(
        AccessContext? Context,
        IResult? Failure);

    internal static async Task<AccessResult> AuthorizeAsync(
        HttpContext context,
        bool allowAuditViewer = false)
    {
        var userId = ActualUserId(context);
        if (userId is null)
        {
            return new(null, Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Administrative authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT
                    COALESCE(r.role_code, ''),
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
            command.Parameters.AddWithValue("user_id", userId.Value);

            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0)))
                {
                    roles.Add(reader.GetString(0));
                }

                if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1)))
                {
                    permissions.Add(reader.GetString(1));
                }
            }

            var allowed = roles.Contains("SUPER_ADMINISTRATOR")
                || roles.Contains("ADMINISTRATOR")
                || permissions.Contains("SYSTEM_ADMINISTRATION")
                || permissions.Contains("MANAGE_ALL")
                || (allowAuditViewer && permissions.Contains("VIEW_AUDIT_TRAIL"));

            if (!allowed)
            {
                return new(null, Results.Json(new
                {
                    status = "administrator_access_required",
                    message = allowAuditViewer
                        ? "Administrator or Audit Trail access is required."
                        : "Administrator access is required."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new(new(
                userId.Value,
                ActualEmail(context),
                roles,
                permissions,
                connectionString), null);
        }
        catch
        {
            return new(null, Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Administrative authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    internal static Guid? ActualUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var raw)) continue;
            if (raw is Guid userId) return userId;
            if (Guid.TryParse(raw?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    internal static string ActualEmail(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualEmail", "ProjectPulseSessionEmail" })
        {
            if (!context.Items.TryGetValue(key, out var raw)) continue;
            var value = raw?.ToString()?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        return "unknown";
    }

    internal static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var raw)
        && raw is bool value
        && value;

    internal static string? ConnectionString()
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
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port)
                ? port
                : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10,
            Timeout = 5,
            CommandTimeout = 15
        }.ConnectionString;
    }

    internal static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string tableName,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.' || @table_name) IS NOT NULL;",
            connection,
            transaction);
        command.Parameters.AddWithValue("table_name", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task<bool> WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string category,
        string status,
        string eventType,
        Guid? actorUserId,
        string actorEmail,
        string targetType,
        string targetId,
        string targetLabel,
        string sourceModule,
        string sourceTable,
        string sourceRecordId,
        string summary,
        object details,
        string ipAddress,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!await TableExistsAsync(
                connection,
                "projectpulse_system_audit_events",
                transaction,
                cancellationToken))
        {
            return false;
        }

        await using var command = new NpgsqlCommand("""
            INSERT INTO projectpulse_system_audit_events (
                event_time,
                category,
                status,
                event_type,
                actor_user_id,
                actor_email,
                target_type,
                target_id,
                target_label,
                source_module,
                source_table,
                source_record_id,
                summary,
                event_details,
                ip_address,
                correlation_id,
                is_immutable,
                created_at
            )
            VALUES (
                NOW(),
                @category,
                @status,
                @event_type,
                @actor_user_id,
                @actor_email,
                @target_type,
                @target_id,
                @target_label,
                @source_module,
                @source_table,
                @source_record_id,
                @summary,
                @event_details::jsonb,
                @ip_address,
                @correlation_id,
                TRUE,
                NOW()
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("category", category);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue(
            "actor_user_id",
            actorUserId.HasValue ? actorUserId.Value : DBNull.Value);
        command.Parameters.AddWithValue("actor_email", actorEmail ?? string.Empty);
        command.Parameters.AddWithValue("target_type", targetType ?? string.Empty);
        command.Parameters.AddWithValue("target_id", targetId ?? string.Empty);
        command.Parameters.AddWithValue("target_label", targetLabel ?? string.Empty);
        command.Parameters.AddWithValue("source_module", sourceModule ?? string.Empty);
        command.Parameters.AddWithValue("source_table", sourceTable ?? string.Empty);
        command.Parameters.AddWithValue("source_record_id", sourceRecordId ?? string.Empty);
        command.Parameters.AddWithValue("summary", summary ?? string.Empty);
        command.Parameters.AddWithValue("event_details", JsonSerializer.Serialize(details));
        command.Parameters.AddWithValue("ip_address", ipAddress ?? string.Empty);
        command.Parameters.AddWithValue("correlation_id", correlationId ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    internal static string ClientIp(HttpContext context)
    {
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }
}
