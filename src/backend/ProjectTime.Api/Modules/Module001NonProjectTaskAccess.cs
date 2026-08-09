using System.Text.RegularExpressions;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class Module001NonProjectTaskModule
{
    private static async Task<NonProjectTaskAccess?> LoadAccessAsync(
        NpgsqlConnection connection,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var actualUserId = ContextGuid(context, "ProjectPulseActualUserId")
            ?? ContextGuid(context, "ProjectPulseSessionUserId")
            ?? ContextGuid(context, "ProjectPulseEffectiveUserId");
        if (!actualUserId.HasValue) return null;

        var effectiveUserId = ContextGuid(context, "ProjectPulseEffectiveUserId")
            ?? actualUserId.Value;
        var isViewAs = ProjectPulseActualSessionAuthority.IsViewAs(context)
            || effectiveUserId != actualUserId.Value;

        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(
                ARRAY_AGG(DISTINCT UPPER(role_row.role_code))
                    FILTER (WHERE role_row.role_code IS NOT NULL),
                ARRAY[]::text[]
            )
            FROM app_users user_row
            LEFT JOIN app_user_role_assignments assignment
              ON assignment.user_id = user_row.user_id
             AND assignment.is_active = TRUE
            LEFT JOIN app_roles role_row
              ON role_row.app_role_id = assignment.app_role_id
             AND role_row.is_active = TRUE
            WHERE user_row.user_id = @user_id
              AND user_row.is_active = TRUE
            GROUP BY user_row.user_id;
            """, connection);
        command.Parameters.AddWithValue("user_id", effectiveUserId);

        var roleValue = await command.ExecuteScalarAsync(cancellationToken);
        if (roleValue is not string[] roleCodes) return null;

        return new NonProjectTaskAccess(
            actualUserId.Value,
            effectiveUserId,
            roleCodes
                .Select(role => role.Trim().ToUpperInvariant())
                .Where(role => role.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            isViewAs,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers["User-Agent"].ToString());
    }

    private static string NormalizeCode(string? value)
    {
        var normalized = Regex.Replace(
            Clean(value).ToUpperInvariant().Replace(' ', '_'),
            "[^A-Z0-9._-]+",
            "_");
        return normalized.Trim('_', '-', '.');
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static Guid? ContextGuid(HttpContext context, string key)
    {
        if (!context.Items.TryGetValue(key, out var value)) return null;
        if (value is Guid guid) return guid;
        return Guid.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    private static string ConnectionString()
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
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        throw new InvalidOperationException("Pulse database connection is not configured.");
    }

    private sealed record NonProjectTaskRequest(
        string? TaskCode,
        string? TaskName,
        string? TaskDescription,
        string? UtilizationClassification,
        bool? RequiresApproval,
        int? DisplayOrder,
        string? Reason);

    private sealed record NonProjectTaskAccess(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string[] RoleCodes,
        bool IsViewAs,
        string IpAddress,
        string UserAgent);

    private sealed record ExistingCategory(
        Guid CategoryId,
        string Code,
        string Name,
        string Description,
        string Classification,
        bool RequiresApproval,
        bool IsActive,
        int DisplayOrder);
}
