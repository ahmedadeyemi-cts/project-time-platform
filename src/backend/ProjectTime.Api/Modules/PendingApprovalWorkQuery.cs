using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class PendingApprovalWorkModule
{
    private static async Task<List<PendingApprovalItem>> LoadPendingItemsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        PendingApprovalAccess access,
        string? stage,
        DateOnly? weekStart,
        CancellationToken cancellationToken)
    {
        var statusFilter = stage switch
        {
            "manager" => "submitted",
            "pm" => "manager_approved",
            "ptc" => "pm_approved",
            _ => string.Empty
        };

        var weekEnd = weekStart?.AddDays(6);
        var items = new List<PendingApprovalItem>();

        await using var command = new NpgsqlCommand("""
            WITH pending_days AS (
                SELECT
                    tds.timesheet_id,
                    tds.user_id,
                    tds.work_date,
                    tds.status,
                    COALESCE(
                        CASE
                            WHEN tds.status = 'submitted' THEN tds.submitted_at
                            WHEN tds.status = 'manager_approved'
                                THEN NULLIF(to_jsonb(tds)->>'manager_approved_at', '')::timestamptz
                            WHEN tds.status = 'pm_approved'
                                THEN NULLIF(to_jsonb(tds)->>'pm_approved_at', '')::timestamptz
                            ELSE NULL
                        END,
                        tds.updated_at,
                        tds.created_at
                    ) AS pending_at
                FROM timesheet_day_statuses tds
                WHERE tds.status IN ('submitted', 'manager_approved', 'pm_approved')
                  AND (@status_filter = '' OR tds.status = @status_filter)
                  AND (
                        @week_start IS NULL
                        OR tds.work_date BETWEEN @week_start AND @week_end
                  )
            )
            SELECT
                pending.timesheet_id,
                pending.user_id,
                timesheet.week_start_date,
                timesheet.week_end_date,
                pending.work_date,
                pending.status,
                pending.pending_at,
                COALESCE(NULLIF(submitter.display_name, ''), submitter.email, 'Unknown resource'),
                COALESCE(submitter.email, ''),
                COALESCE(SUM(entry.hours), 0),
                COUNT(entry.time_entry_id),
                COALESCE(
                    STRING_AGG(DISTINCT NULLIF(project.project_code, ''), ', ')
                        FILTER (WHERE NULLIF(project.project_code, '') IS NOT NULL),
                    ''
                ),
                COALESCE(
                    STRING_AGG(DISTINCT NULLIF(project.project_name, ''), ', ')
                        FILTER (WHERE NULLIF(project.project_name, '') IS NOT NULL),
                    ''
                )
            FROM pending_days pending
            JOIN timesheets timesheet ON timesheet.timesheet_id = pending.timesheet_id
            JOIN app_users submitter ON submitter.user_id = pending.user_id
            LEFT JOIN time_entries entry
              ON entry.timesheet_id = pending.timesheet_id
             AND entry.work_date = pending.work_date
            LEFT JOIN projects project ON project.project_id = entry.project_id
            WHERE pending.user_id <> @effective_user_id
              AND (
                    (
                        pending.status = 'submitted'
                        AND @can_manager_approve
                        AND (
                              @organization_scope
                              OR (
                                   @is_manager
                                   AND lower(COALESCE(submitter.manager_email, '')) = lower(@actor_email)
                              )
                        )
                    )
                    OR
                    (
                        pending.status = 'manager_approved'
                        AND @can_project_approve
                        AND (
                              @organization_scope
                              OR (
                                   @is_project_manager
                                   AND EXISTS (
                                       SELECT 1
                                       FROM time_entries project_scope_entry
                                       JOIN projects project_scope
                                         ON project_scope.project_id = project_scope_entry.project_id
                                       WHERE project_scope_entry.timesheet_id = pending.timesheet_id
                                         AND project_scope_entry.work_date = pending.work_date
                                         AND project_scope.project_manager_user_id = @effective_user_id
                                   )
                              )
                        )
                    )
                    OR
                    (
                        pending.status = 'pm_approved'
                        AND @can_ptc_final_approve
                        AND @organization_scope
                    )
              )
            GROUP BY
                pending.timesheet_id,
                pending.user_id,
                timesheet.week_start_date,
                timesheet.week_end_date,
                pending.work_date,
                pending.status,
                pending.pending_at,
                submitter.display_name,
                submitter.email
            ORDER BY pending.work_date, pending.pending_at, submitter.display_name
            LIMIT 5000;
            """, connection, transaction);

        command.Parameters.AddWithValue("status_filter", statusFilter);
        command.Parameters.Add("week_start", NpgsqlDbType.Date).Value =
            weekStart.HasValue ? weekStart.Value : DBNull.Value;
        command.Parameters.Add("week_end", NpgsqlDbType.Date).Value =
            weekEnd.HasValue ? weekEnd.Value : DBNull.Value;
        command.Parameters.AddWithValue("effective_user_id", access.EffectiveUserId);
        command.Parameters.AddWithValue("actor_email", access.Email);
        command.Parameters.AddWithValue("organization_scope", access.OrganizationScope);
        command.Parameters.AddWithValue("is_manager", access.IsManager);
        command.Parameters.AddWithValue("is_project_manager", access.IsProjectManager);
        command.Parameters.AddWithValue("can_manager_approve", access.CanManagerApprove);
        command.Parameters.AddWithValue("can_project_approve", access.CanProjectApprove);
        command.Parameters.AddWithValue("can_ptc_final_approve", access.CanPtcFinalApprove);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemStage = reader.GetString(5) switch
            {
                "submitted" => "manager",
                "manager_approved" => "pm",
                "pm_approved" => "ptc",
                _ => "unknown"
            };

            items.Add(new PendingApprovalItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.GetFieldValue<DateOnly>(4),
                itemStage,
                StageLabel(itemStage),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetDecimal(9),
                reader.GetInt64(10),
                reader.GetString(11),
                reader.GetString(12)));
        }

        return items;
    }

    private static async Task<PendingApprovalAccess?> LoadAccessAsync(
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
            SELECT
                COALESCE(user_row.email, ''),
                COALESCE(NULLIF(user_row.display_name, ''), user_row.email, 'ProjectPulse user'),
                COALESCE(
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
            GROUP BY user_row.user_id, user_row.email, user_row.display_name;
            """, connection);
        command.Parameters.AddWithValue("user_id", effectiveUserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var email = reader.GetString(0);
        var displayName = reader.GetString(1);
        var roleCodes = reader.GetFieldValue<string[]>(2)
            .Select(role => role.Trim().ToUpperInvariant())
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var roles = roleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var organizationScope = roles.Any(OrganizationApprovalRoles.Contains);
        var isManager = roles.Any(ManagerApprovalRoles.Contains);
        var isProjectManager = roles.Any(ProjectApprovalRoles.Contains);
        var isPtc = roles.Contains("PROJECT_TEAM_COORDINATOR");
        var canManagerApprove = organizationScope || isManager;
        var canProjectApprove = organizationScope || isProjectManager;
        var canPtcFinalApprove = organizationScope;

        var scopeLabel = organizationScope
            ? "All organization approvals"
            : isManager && isProjectManager
                ? "Direct reports and managed projects"
                : isManager
                    ? "My direct reports"
                    : isProjectManager
                        ? "My managed projects"
                        : "No approval scope";

        var primaryRoleCode = roleCodes.FirstOrDefault(role => role == "SUPER_ADMINISTRATOR")
            ?? roleCodes.FirstOrDefault(role => role == "ADMINISTRATOR")
            ?? roleCodes.FirstOrDefault(role => role == "PROJECT_TEAM_COORDINATOR")
            ?? roleCodes.FirstOrDefault(ProjectApprovalRoles.Contains)
            ?? roleCodes.FirstOrDefault(ManagerApprovalRoles.Contains)
            ?? roleCodes.FirstOrDefault()
            ?? "UNKNOWN";

        return new PendingApprovalAccess(
            actualUserId.Value,
            effectiveUserId,
            email,
            displayName,
            roleCodes,
            primaryRoleCode,
            isViewAs,
            organizationScope,
            isManager,
            isProjectManager,
            isPtc,
            canManagerApprove,
            canProjectApprove,
            canPtcFinalApprove,
            scopeLabel,
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            context.Request.Headers["User-Agent"].ToString());
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT to_regclass('public.' || @table_name) IS NOT NULL;",
            connection,
            transaction);
        command.Parameters.AddWithValue("table_name", tableName);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static bool CanCompleteStage(PendingApprovalAccess access, string stage) => stage switch
    {
        "manager" => access.CanManagerApprove,
        "pm" => access.CanProjectApprove,
        "ptc" => access.CanPtcFinalApprove,
        _ => false
    };

    private static string BuildSystemApprovalReason(
        PendingApprovalAccess access,
        string stage,
        DateOnly weekStart) =>
        $"{StageLabel(stage)} bulk approval completed by {access.DisplayName} for week {weekStart:yyyy-MM-dd}. No user-entered approval comment was required.";

    private static string? NormalizeStage(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "manager" or "manager_review" => "manager",
        "pm" or "project_manager" or "project-manager" => "pm",
        "ptc" or "ptc_final" or "ptc-final" => "ptc",
        _ => null
    };

    private static string StageLabel(string stage) => stage switch
    {
        "manager" => "Manager review",
        "pm" => "PM review",
        "ptc" => "PTC final review",
        _ => "Approval review"
    };

    private static int StageOrder(string stage) => stage switch
    {
        "manager" => 0,
        "pm" => 1,
        "ptc" => 2,
        _ => 9
    };

    private static string ApprovalKey(Guid timesheetId, DateOnly workDate) =>
        $"{timesheetId:D}|{workDate:yyyy-MM-dd}";

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

        throw new InvalidOperationException("ProjectPulse database connection is not configured.");
    }

    private sealed record BulkCompleteRequest(
        string? Stage,
        DateOnly? WeekStart,
        List<BulkApprovalItem>? Items);

    private sealed record BulkApprovalItem(Guid TimesheetId, DateOnly WorkDate);

    private sealed record PendingApprovalAccess(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string Email,
        string DisplayName,
        string[] RoleCodes,
        string PrimaryRoleCode,
        bool IsViewAs,
        bool OrganizationScope,
        bool IsManager,
        bool IsProjectManager,
        bool IsPtc,
        bool CanManagerApprove,
        bool CanProjectApprove,
        bool CanPtcFinalApprove,
        string ScopeLabel,
        string IpAddress,
        string UserAgent)
    {
        public bool CanViewAnyApprovalWork =>
            CanManagerApprove || CanProjectApprove || CanPtcFinalApprove;
    }

    private sealed record PendingApprovalItem(
        Guid TimesheetId,
        Guid UserId,
        DateOnly WeekStart,
        DateOnly WeekEnd,
        DateOnly WorkDate,
        string Stage,
        string StageLabel,
        string Status,
        DateTimeOffset PendingAt,
        string ResourceName,
        string ResourceEmail,
        decimal TotalHours,
        long EntryCount,
        string ProjectCodes,
        string ProjectNames);
}
