using System.Security.Claims;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Resolves non-transferable authority from the actual authenticated ProjectPulse
/// session. Super Administrator authority is permanent and organization-wide in
/// an administrator's own session, but it is never transferred into View-As.
///
/// The resolver accepts the canonical SUPER_ADMINISTRATOR code and retained
/// compatibility aliases, excluding ADMINISTRATOR. It resolves the signed-in identity by stable user ID
/// and never changes that identity based on email or external-identity aliases.
/// </summary>
internal static class ProjectPulseActualSessionAuthority
{
    private static readonly string[] SuperAdministratorRoleCodes =
    [
        "SUPER_ADMINISTRATOR",
        "SUPERADMINISTRATOR",
        "GLOBAL_ADMINISTRATOR",
        "GLOBALADMINISTRATOR"
    ];

    private sealed record AdministratorResolution(
        Guid UserId,
        string RoleCode,
        string AuthoritySource);

    internal static bool IsAdministratorRoleCode(string? roleCode)
    {
        var canonical = System.Text.RegularExpressions.Regex.Replace(
            (roleCode ?? string.Empty).Trim().ToUpperInvariant(),
            "[^A-Z0-9]+",
            "_").Trim('_');
        return SuperAdministratorRoleCodes.Contains(
            canonical,
            StringComparer.OrdinalIgnoreCase);
    }

    internal static bool HasPermanentAdministratorAuthority(
        HttpContext context,
        IEnumerable<string> roleCodes)
    {
        if (IsViewAs(context)) return false;

        return roleCodes.Any(IsAdministratorRoleCode);
    }

    internal static bool IsViewAs(HttpContext context)
    {
        if (context.Items.TryGetValue("ProjectPulseIsViewAs", out var flag) && flag is true)
            return true;
        if (context.Request.Headers.TryGetValue("X-ProjectPulse-View-As-User", out var header)
            && !string.IsNullOrWhiteSpace(header.ToString()))
        {
            return true;
        }

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

        var sessionUserId = ReadUserId(
            context,
            "ProjectPulseSessionUserId",
            "ProjectPulseActualUserId");
        if (!sessionUserId.HasValue) return false;

        var ownsConnection = existingConnection is null;
        var connectionString = ownsConnection ? BuildConnectionString() : string.Empty;
        if (ownsConnection && string.IsNullOrWhiteSpace(connectionString)) return false;

        await using var ownedConnection = ownsConnection
            ? new NpgsqlConnection(connectionString)
            : null;
        var connection = existingConnection ?? ownedConnection!;
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        // Authority is bound only to the validated session's stable user ID.
        // Mutable email addresses and external-link aliases cannot transfer it.
        var resolution = await ResolveByUserIdAsync(connection, transaction, sessionUserId, cancellationToken);

        var resolved = resolution?.UserId;
        if (resolved is not Guid administratorUserId || administratorUserId == Guid.Empty)
            return false;
        if (resolution is null || !IsAdministratorRoleCode(resolution.RoleCode))
            return false;

        // Record verified authority without changing the session identity.
        context.Items["ProjectPulsePermanentFullControl"] = true;
        context.Items["ProjectPulseAuthorizationSource"] = "actual_session_super_administrator";
        context.Items["ProjectPulseIdentityResolutionSource"] = resolution.AuthoritySource;
        context.Items["ProjectPulseActualRoleCodes"] = new[] { resolution.RoleCode };
        return true;
    }

    private static async Task<AdministratorResolution?> ResolveByUserIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty) return null;

        await using var command = new NpgsqlCommand("""
            SELECT app_user.user_id, role.role_code
            FROM app_users app_user
            JOIN app_user_role_assignments assignment
              ON assignment.user_id = app_user.user_id
             AND assignment.is_active = TRUE
            JOIN app_roles role
              ON role.app_role_id = assignment.app_role_id
             AND role.is_active = TRUE
            WHERE app_user.user_id = @user_id
              AND app_user.is_active = TRUE
              AND trim(both '_' from regexp_replace(
                    upper(btrim(COALESCE(role.role_code, ''))),
                    '[^A-Z0-9]+',
                    '_',
                    'g')) = ANY(@admin_role_codes)
            ORDER BY role.role_code;
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId.Value);
        AddAdministratorRoleCodes(command);
        return await ReadAdministratorResolutionAsync(
            command,
            "actual_session_user_id",
            cancellationToken);
    }

    private static async Task<AdministratorResolution?> ReadAdministratorResolutionAsync(
        NpgsqlCommand command,
        string authoritySource,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var userId = reader.GetGuid(0);
            var roleCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (userId != Guid.Empty && IsAdministratorRoleCode(roleCode))
            {
                return new AdministratorResolution(
                    userId,
                    roleCode,
                    authoritySource);
            }
        }

        return null;
    }

    private static void AddAdministratorRoleCodes(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue(
            "admin_role_codes",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            SuperAdministratorRoleCodes);
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

    internal static string ReadActualEmail(HttpContext context)
    {
        foreach (var key in new[]
                 {
                     "ProjectPulseActualEmail",
                     "ProjectPulseSessionEmail",
                     "ProjectPulseUserEmail"
                 })
        {
            if (!context.Items.TryGetValue(key, out var raw)) continue;
            var value = raw?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) return value.ToLowerInvariant();
        }

        foreach (var claimType in new[]
                 {
                     ClaimTypes.Email,
                     "email",
                     "preferred_username",
                     "upn",
                     ClaimTypes.Name
                 })
        {
            var value = context.User?.Claims
                .FirstOrDefault(claim => string.Equals(
                    claim.Type,
                    claimType,
                    StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && value.Contains('@'))
                return value.ToLowerInvariant();
        }

        return string.Empty;
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
