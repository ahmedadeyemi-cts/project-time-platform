using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<HashSet<string>> LoadCodeSetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task<int> NextVersionNumberAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COALESCE(MAX(version_number), 0) + 1 FROM scoped_role_policy_versions;",
            connection,
            transaction);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 1);
    }

    private static async Task AdvisoryLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended('projectpulse-scoped-rbac-policy', 40));",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarIntAsync(
        NpgsqlConnection connection,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<bool> ScopedPolicyTablesExistAsync(
        NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.scoped_role_policy_versions') IS NOT NULL " +
            "AND to_regclass('public.scoped_role_policy_grants') IS NOT NULL;",
            connection);
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<IResult?> RequirePolicyTablesAsync(
        NpgsqlConnection connection)
    {
        if (await ScopedPolicyTablesExistAsync(connection)) return null;
        return Results.Json(new
        {
            status = "scoped_rbac_migration_required",
            migration = "040_scoped_role_policy_versions",
            message = "Apply migration 040 before using scoped role policy APIs."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid ProjectPulse session is required."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static Guid? ReadGuid(object? value)
    {
        if (value is Guid guid) return guid;
        return Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    public static string CanonicalRole(string? roleCode)
    {
        var normalized = (roleCode ?? string.Empty).Trim().ToUpperInvariant();
        return RoleAliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : normalized;
    }

    private static string[] AliasesFor(string canonicalRoleCode)
    {
        var canonical = CanonicalRole(canonicalRoleCode);
        return RoleAliases
            .Where(pair => string.Equals(pair.Value, canonical, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key.ToUpperInvariant())
            .Append(canonical)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string RoleDisplayName(string roleCode) =>
        roleCode.Replace('_', ' ').ToLowerInvariant() switch
        {
            "project team coordinator" => "Project Team Coordinator",
            "project management" => "Project Management",
            "project management lead" => "Project Management Lead",
            "engineering lead" => "Engineering Lead",
            "inside sales" => "Inside Sales",
            "solution architect" => "Solution Architect",
            "super administrator" => "Super Administrator",
            var value => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value)
        };

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
        throw new InvalidOperationException("ProjectPulse database connection is not configured.");
    }

    public sealed record ActorContext(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string Email,
        string[] RoleCodes,
        bool IsViewAs,
        bool IsSuperAdministrator);

    internal sealed record RoleSummaryRow(
        string RoleCode,
        string RoleName,
        string Description,
        bool IsActive,
        int ActiveUserCount);

    internal sealed record ModuleSummaryRow(
        string ModuleCode,
        string ModuleName,
        string RouteScope,
        string CurrentState,
        string PermissionNotes,
        string SourceUrl);

    internal sealed record AssignedUserRow(
        Guid UserId,
        string Email,
        string DisplayName,
        bool IsActive);

    internal sealed record PolicyGrantRow(
        string RoleCode,
        string ModuleCode,
        string ModuleName,
        string RouteScope,
        string ActionCode,
        string ScopeCode,
        string GrantEffect,
        JsonElement Conditions,
        bool DelegatedAuthority,
        bool ReasonRequired,
        bool AuditRequired,
        string SourceDesignation,
        string SourceNotes,
        int VersionNumber,
        string LastModifiedBy,
        DateTimeOffset? LastModifiedAt);

    internal sealed record PolicyVersionRow(
        Guid PolicyVersionId,
        int VersionNumber,
        string PolicyName,
        string PolicyStatus,
        string SourceName,
        string SourceSha256,
        string PolicyNotes,
        Guid? RestoredFromPolicyVersionId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? PublishedAt,
        DateTimeOffset? RetiredAt);

    public sealed record PolicyGrantInput(
        string ActionCode,
        string ScopeCode,
        string Effect,
        JsonElement? Conditions,
        bool DelegatedAuthority,
        bool ReasonRequired,
        bool AuditRequired,
        bool IsActive = true);

    public sealed record PolicyModuleChange(
        string RoleCode,
        string ModuleCode,
        List<PolicyGrantInput>? Grants,
        string? Notes);

    public sealed record PolicyPublishRequest(
        int? BaseVersionNumber,
        string? Reason,
        List<PolicyModuleChange>? Changes);

    public sealed record PolicyRestoreRequest(string? Reason);

    public sealed record ScopedApprovalDecisionRequest(
        Guid TimesheetId,
        DateOnly WorkDate,
        string? RequiredStage,
        string? Decision,
        string? OriginalResponsibleRole,
        Guid? OriginalResponsibleUserId,
        string? Reason);

    public sealed record ScopedTimeCorrectionRequest(
        Guid? TimeEntryId,
        Guid TimesheetId,
        DateOnly WorkDate,
        Guid TargetUserId,
        Guid? ProjectId,
        string? TaskId,
        decimal? Hours,
        string? Description,
        string? Reason);

    public sealed record PolicyValidationResult(
        bool Valid,
        List<string> Errors,
        List<string> Warnings,
        int ActiveSuperAdministratorCount);
}
