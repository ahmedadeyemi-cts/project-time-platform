using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static readonly string[] PtcManagedRoleAliases =
    {
        "ENGINEERING", "ENGINEER",
        "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
        "PROJECT_MANAGEMENT", "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD"
    };

    public static WebApplication MapRuntimeDataCompatibilityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/runtime/role-policy/summary", SummaryAsync);
        app.MapGet("/api/runtime/role-policy/catalog", CatalogAsync);
        app.MapGet("/api/runtime/role-policy/versions", VersionsAsync);
        app.MapGet("/api/runtime/role-policy/matrix", MatrixAsync);
        app.MapGet("/api/runtime/role-policy/roles/{roleCode}", RoleDetailAsync);
        app.MapGet("/api/runtime/timesheet/steward/users", RuntimePtcUsersAsync);
        app.MapGet("/api/runtime/timesheet/steward/users/{targetUserId:guid}/workspace", RuntimePtcWorkspaceAsync);
        return app;
    }

    private static string CanonicalPtcManagedRole(string roleCode) => roleCode.Trim().ToUpperInvariant() switch
    {
        "ENGINEER" or "ENGINEERING" => "ENGINEERING",
        "ENGINEERING_TEAM_LEAD" or "ENGINEERING_LEAD" => "ENGINEERING_LEAD",
        "PROJECT_MANAGER" or "PROJECT_MANAGEMENT" => "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_TEAM_LEAD" or "PM_TEAM_LEAD" or "PROJECT_MANAGEMENT_LEAD" => "PROJECT_MANAGEMENT_LEAD",
        _ => roleCode.Trim().ToUpperInvariant()
    };

    private static string[] CanonicalPtcManagedRoles(string[] roleCodes) => roleCodes
        .Select(CanonicalPtcManagedRole)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value)
        .ToArray();

    private static async Task<bool> RuntimePtcManagedUserExistsAsync(NpgsqlConnection connection, Guid userId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_users u
                JOIN app_user_role_assignments ura
                  ON ura.user_id=u.user_id AND ura.is_active=TRUE
                JOIN app_roles r
                  ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
                WHERE u.user_id=@user_id
                  AND u.is_active=TRUE
                  AND UPPER(r.role_code)=ANY(@role_codes)
            );
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("role_codes", PtcManagedRoleAliases);
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<IResult> RuntimePtcUsersAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_VIEW_ON_BEHALF", null, null, false);
        if (access.Error is not null) return access.Error;

        var weekStart = PtcRequestedWeek(context);
        var weekEnd = weekStart.AddDays(6);
        var search = context.Request.Query["search"].FirstOrDefault()?.Trim() ?? string.Empty;
        var users = new List<object>();

        await using var command = new NpgsqlCommand("""
            WITH eligible_users AS (
                SELECT
                    u.user_id,
                    u.email,
                    COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                    ARRAY_AGG(DISTINCT UPPER(r.role_code)) AS raw_role_codes
                FROM app_users u
                JOIN app_user_role_assignments ura
                  ON ura.user_id=u.user_id AND ura.is_active=TRUE
                JOIN app_roles r
                  ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
                WHERE u.is_active=TRUE
                  AND UPPER(r.role_code)=ANY(@role_codes)
                  AND (
                    @search=''
                    OR u.email ILIKE '%' || @search || '%'
                    OR COALESCE(u.display_name, '') ILIKE '%' || @search || '%'
                  )
                GROUP BY u.user_id, u.email, u.display_name
            )
            SELECT
                eu.user_id,
                eu.email,
                eu.display_name,
                eu.raw_role_codes,
                t.timesheet_id,
                COALESCE(t.status, 'not_started'),
                COALESCE(SUM(te.hours), 0),
                COUNT(te.time_entry_id),
                MAX(COALESCE(te.updated_at, t.updated_at))
            FROM eligible_users eu
            LEFT JOIN timesheets t
              ON t.user_id=eu.user_id AND t.week_start_date=@week_start
            LEFT JOIN time_entries te
              ON te.timesheet_id=t.timesheet_id
             AND te.work_date BETWEEN @week_start AND @week_end
            GROUP BY eu.user_id, eu.email, eu.display_name, eu.raw_role_codes,
                     t.timesheet_id, t.status
            ORDER BY eu.display_name, eu.email
            LIMIT 500;
            """, connection);
        command.Parameters.AddWithValue("role_codes", PtcManagedRoleAliases);
        command.Parameters.AddWithValue("search", search);
        command.Parameters.AddWithValue("week_start", weekStart);
        command.Parameters.AddWithValue("week_end", weekEnd);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var roleCodes = CanonicalPtcManagedRoles(reader.GetFieldValue<string[]>(3));
            users.Add(new
            {
                userId = reader.GetGuid(0),
                email = reader.GetString(1),
                displayName = reader.GetString(2),
                roleCodes,
                roleNames = roleCodes.Select(RoleDisplayName).ToArray(),
                timesheetId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                status = reader.GetString(5),
                totalHours = reader.GetDecimal(6),
                entryCount = Convert.ToInt32(reader.GetInt64(7)),
                lastUpdatedAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(8)
            });
        }

        return Results.Ok(new
        {
            apiContractVersion = "runtime-data-2026-07-25",
            weekStart,
            weekEnd,
            eligibleRoleCodes = new[]
            {
                "ENGINEERING", "ENGINEERING_LEAD",
                "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD"
            },
            count = users.Count,
            canSubmitOnBehalf = false,
            users
        });
    }

    private static async Task<IResult> RuntimePtcWorkspaceAsync(Guid targetUserId, HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_VIEW_ON_BEHALF", targetUserId, null, false);
        if (access.Error is not null) return access.Error;
        if (!await RuntimePtcManagedUserExistsAsync(connection, targetUserId))
        {
            return Results.NotFound(new
            {
                status = "ptc_managed_user_not_found",
                message = "Select an active Engineer, Engineering Lead, Project Management, or Project Management Lead user."
            });
        }

        var weekStart = PtcRequestedWeek(context);
        var weekEnd = weekStart.AddDays(6);
        object? user = null;
        object? timesheet = null;
        var entries = new List<object>();
        var assignments = new List<object>();
        var nonProjectCategories = new List<object>();

        await using (var userCommand = new NpgsqlCommand("""
            SELECT u.user_id, u.email, COALESCE(NULLIF(u.display_name,''),u.email),
                   ARRAY_AGG(DISTINCT UPPER(r.role_code))
            FROM app_users u
            JOIN app_user_role_assignments ura
              ON ura.user_id=u.user_id AND ura.is_active=TRUE
            JOIN app_roles r
              ON r.app_role_id=ura.app_role_id AND r.is_active=TRUE
            WHERE u.user_id=@user_id AND u.is_active=TRUE
              AND UPPER(r.role_code)=ANY(@role_codes)
            GROUP BY u.user_id, u.email, u.display_name;
            """, connection))
        {
            userCommand.Parameters.AddWithValue("user_id", targetUserId);
            userCommand.Parameters.AddWithValue("role_codes", PtcManagedRoleAliases);
            await using var reader = await userCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var roleCodes = CanonicalPtcManagedRoles(reader.GetFieldValue<string[]>(3));
                user = new
                {
                    userId = reader.GetGuid(0),
                    email = reader.GetString(1),
                    displayName = reader.GetString(2),
                    roleCodes,
                    roleNames = roleCodes.Select(RoleDisplayName).ToArray()
                };
            }
        }

        await using (var timesheetCommand = new NpgsqlCommand("""
            SELECT timesheet_id, status, submitted_at, updated_at
            FROM timesheets
            WHERE user_id=@user_id AND week_start_date=@week_start;
            """, connection))
        {
            timesheetCommand.Parameters.AddWithValue("user_id", targetUserId);
            timesheetCommand.Parameters.AddWithValue("week_start", weekStart);
            await using var reader = await timesheetCommand.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                timesheet = new
                {
                    timesheetId = reader.GetGuid(0),
                    status = reader.GetString(1),
                    submittedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2),
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(3)
                };
            }
        }

        await using (var entryCommand = new NpgsqlCommand("""
            SELECT te.time_entry_id, te.timesheet_id, te.work_date, te.hours,
                   COALESCE(te.description,''), te.billable, te.status,
                   te.project_id, COALESCE(p.project_code,''), COALESCE(p.project_name,''),
                   te.task_id, COALESCE(pt.task_code,''), COALESCE(pt.task_name,''),
                   te.non_project_time_category_id,
                   COALESCE(npc.category_code,''), COALESCE(npc.category_name,''),
                   te.updated_at
            FROM time_entries te
            LEFT JOIN projects p ON p.project_id=te.project_id
            LEFT JOIN project_tasks pt ON pt.task_id=te.task_id
            LEFT JOIN non_project_time_categories npc
              ON npc.non_project_time_category_id=te.non_project_time_category_id
            WHERE te.user_id=@user_id
              AND te.work_date BETWEEN @week_start AND @week_end
            ORDER BY te.work_date, p.project_code, pt.task_code, npc.category_name, te.created_at;
            """, connection))
        {
            entryCommand.Parameters.AddWithValue("user_id", targetUserId);
            entryCommand.Parameters.AddWithValue("week_start", weekStart);
            entryCommand.Parameters.AddWithValue("week_end", weekEnd);
            await using var reader = await entryCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new
                {
                    timeEntryId = reader.GetGuid(0),
                    timesheetId = reader.GetGuid(1),
                    workDate = reader.GetFieldValue<DateOnly>(2),
                    hours = reader.GetDecimal(3),
                    description = reader.GetString(4),
                    billable = reader.GetBoolean(5),
                    status = reader.GetString(6),
                    projectId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
                    projectCode = reader.GetString(8),
                    projectName = reader.GetString(9),
                    taskId = reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10),
                    taskCode = reader.GetString(11),
                    taskName = reader.GetString(12),
                    nonProjectTimeCategoryId = reader.IsDBNull(13) ? (Guid?)null : reader.GetGuid(13),
                    nonProjectCategoryCode = reader.GetString(14),
                    nonProjectCategoryName = reader.GetString(15),
                    entryGroup = reader.IsDBNull(13) ? "PROJECT_OR_REQUEST" : "NON_PROJECT_TIME",
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(16)
                });
            }
        }

        await using (var assignmentCommand = new NpgsqlCommand("""
            SELECT pa.project_assignment_id,
                   COALESCE(c.client_name,''),
                   p.project_id, p.project_code, p.project_name,
                   pt.task_id, pt.task_code, pt.task_name, pt.billable,
                   COALESCE(NULLIF(to_jsonb(pt)->>'work_task_category',''),
                            NULLIF(to_jsonb(pt)->>'work_type',''), 'project_task'),
                   COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number',''),'')
            FROM project_assignments pa
            JOIN projects p ON p.project_id=pa.project_id
            JOIN project_tasks pt
              ON pt.task_id=pa.task_id AND pt.project_id=pa.project_id
            LEFT JOIN clients c ON c.client_id=p.client_id
            WHERE pa.user_id=@user_id
              AND pa.effective_start_date<=@week_end
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date>=@week_start)
              AND p.status IN ('active','on_hold')
              AND pt.is_active=TRUE
            ORDER BY p.project_code, pt.task_code;
            """, connection))
        {
            assignmentCommand.Parameters.AddWithValue("user_id", targetUserId);
            assignmentCommand.Parameters.AddWithValue("week_start", weekStart);
            assignmentCommand.Parameters.AddWithValue("week_end", weekEnd);
            await using var reader = await assignmentCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var workTaskCategory = reader.GetString(9).Trim().ToLowerInvariant();
                var serviceRequestNumber = reader.GetString(10).Trim();
                var serviceRequest = workTaskCategory == "service_request_task"
                    || !string.IsNullOrWhiteSpace(serviceRequestNumber);
                var selectionParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(serviceRequestNumber)) selectionParts.Add(serviceRequestNumber);
                if (!string.IsNullOrWhiteSpace(reader.GetString(1))) selectionParts.Add(reader.GetString(1));
                selectionParts.Add(reader.GetString(3));
                selectionParts.Add(reader.GetString(7));
                assignments.Add(new
                {
                    targetType = "assignment",
                    groupLabel = serviceRequest ? "Requests / Service Requests" : "Project Tasks",
                    assignmentId = reader.GetGuid(0),
                    customerName = reader.GetString(1),
                    projectId = reader.GetGuid(2),
                    projectCode = reader.GetString(3),
                    projectName = reader.GetString(4),
                    taskId = reader.GetGuid(5),
                    taskCode = reader.GetString(6),
                    taskName = reader.GetString(7),
                    billable = reader.GetBoolean(8),
                    workTaskCategory,
                    serviceRequestNumber,
                    selectionLabel = string.Join(" · ", selectionParts)
                });
            }
        }

        await using (var categoryCommand = new NpgsqlCommand("""
            SELECT non_project_time_category_id, category_code, category_name
            FROM non_project_time_categories
            WHERE is_active=TRUE
            ORDER BY display_order, category_name;
            """, connection))
        {
            await using var reader = await categoryCommand.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                nonProjectCategories.Add(new
                {
                    targetType = "category",
                    groupLabel = "Non-Project Time",
                    nonProjectTimeCategoryId = reader.GetGuid(0),
                    categoryCode = reader.GetString(1),
                    categoryName = reader.GetString(2),
                    selectionLabel = reader.GetString(2)
                });
            }
        }

        return Results.Ok(new
        {
            apiContractVersion = "runtime-data-2026-07-25",
            user,
            weekStart,
            weekEnd,
            timesheet,
            entries,
            assignments,
            nonProjectCategories,
            canSubmitOnBehalf = false,
            requiredCorrectionFlow = "UNSUBMIT_OR_REOPEN_THEN_EDIT_MOVE_REMOVE_THEN_USER_RESUBMITS"
        });
    }
}
