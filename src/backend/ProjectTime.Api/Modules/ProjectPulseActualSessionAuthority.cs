using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Resolves non-transferable authority from the actual authenticated ProjectPulse
/// session. Super Administrator authority is permanent and organization-wide in
/// an administrator's own session, but it is never transferred into View-As.
/// </summary>
internal static class ProjectPulseActualSessionAuthority
{
    private static readonly string[] SuperAdministratorRoleCodes =
    [
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR"
    ];

    internal static bool IsViewAs(HttpContext context)
    {
        if (context.Items.TryGetValue("ProjectPulseIsViewAs", out var flag) && flag is true)
            return true;
        if (context.Request.Headers.ContainsKey("X-ProjectPulse-View-As-User"))
            return true;

        var actual = ReadUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = ReadUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        return actual.HasValue && effective.HasValue && actual.Value != effective.Value;
    }

    internal static async Task<bool> IsSuperAdministratorAsync(
        HttpContext context,
        NpgsqlConnection? existingConnection = null,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        if (IsViewAs(context)) return false;
        var actualUserId = ReadUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        if (!actualUserId.HasValue) return false;

        var ownsConnection = existingConnection is null;
        await using var ownedConnection = ownsConnection
            ? new NpgsqlConnection(BuildConnectionString())
            : null;
        var connection = existingConnection ?? ownedConnection!;
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_user_role_assignments assignment
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                 AND role.is_active = TRUE
                JOIN app_users app_user
                  ON app_user.user_id = assignment.user_id
                 AND app_user.is_active = TRUE
                WHERE assignment.user_id = @user_id
                  AND assignment.is_active = TRUE
                  AND upper(COALESCE(role.role_code, '')) = ANY(@role_codes)
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", actualUserId.Value);
        command.Parameters.AddWithValue("role_codes", SuperAdministratorRoleCodes);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    internal static Guid? ReadUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var raw)) continue;
            if (raw is Guid id && id != Guid.Empty) return id;
            if (Guid.TryParse(raw?.ToString(), out var parsed) && parsed != Guid.Empty) return parsed;
        }
        return null;
    }

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
        {
            return string.Empty;
        }

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
}
