using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class ApprovalCenterModule
{
    private static readonly HashSet<string> TimeApprovalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "MANAGER",
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT"
    };

    private static readonly HashSet<string> PasswordResetRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "MANAGER"
    };

    public static WebApplication MapApprovalCenterEndpoints(this WebApplication app)
    {
        app.MapGet("/api/approval-center/access", async (HttpContext context) =>
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();

            var accessResult = await RequireAccessAsync(context, connection);
            if (accessResult.Error is not null) return accessResult.Error;

            return Results.Ok(ToAccessPayload(accessResult.Access!));
        });

        app.MapGet("/api/manager/approvals", async (
            DateOnly? weekStart,
            bool? includeAll,
            bool? allDates,
            string? search,
            HttpContext context) =>
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();

            var accessResult = await RequireAccessAsync(context, connection, requireTime: true);
            if (accessResult.Error is not null) return accessResult.Error;
            var access = accessResult.Access!;

            var requestedAllDates = allDates == true && access.CanViewAllTimeApprovals;
            var start = requestedAllDates
                ? new DateOnly(2000, 1, 1)
                : weekStart ?? Sunday(DateOnly.FromDateTime(DateTime.UtcNow));
            var end = requestedAllDates ? new DateOnly(2100, 12, 31) : start.AddDays(6);

            var items = await LoadTimeApprovalsAsync(
                connection,
                access,
                start,
                end,
                includeAll == true,
                search);

            return Results.Ok(new
            {
                module = "002",
                status = "approval_inbox_loaded",
                weekStart = requestedAllDates ? (DateOnly?)null : start,
                weekEnd = requestedAllDates ? (DateOnly?)null : end,
                allDates = requestedAllDates,
                includeAll = includeAll == true,
                count = items.Count,
                actionableCount = items.Count(item => item.Status == "submitted"),
                access = ToAccessPayload(access),
                items
            });
        });

        app.MapGet("/api/manager/approval-summary", async (HttpContext context) =>
            await BuildSummaryResultAsync(context));

        app.MapGet("/api/manager/approval-count", async (HttpContext context) =>
            await BuildSummaryResultAsync(context));

        app.MapPost("/api/manager/approvals/approve", async (
            ApprovalActionRequest request,
            HttpContext context) =>
            await ProcessTimeActionAsync(
                request,
                context,
                targetStatus: "manager_approved",
                approvalStatus: "approved",
                auditAction: "timesheet_day_approval_center_approved",
                successMessage: "Submitted time was approved.",
                sendRejectionEmail: false));

        app.MapPost("/api/manager/approvals/decline", async (
            ApprovalActionRequest request,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Comment))
            {
                return Results.BadRequest(new
                {
                    status = "validation_failed",
                    message = "A specific rejection reason is required before returning time to the engineer."
                });
            }

            return await ProcessTimeActionAsync(
                request,
                context,
                targetStatus: "manager_declined",
                approvalStatus: "declined",
                auditAction: "timesheet_day_approval_center_rejected",
                successMessage: "Submitted time was returned to the engineer for correction.",
                sendRejectionEmail: true);
        });

        app.MapPost("/api/manager/approvals/resolve-stale", async (
            ApprovalActionRequest request,
            HttpContext context) =>
        {
            if (string.IsNullOrWhiteSpace(request.Comment))
            {
                return Results.BadRequest(new
                {
                    status = "validation_failed",
                    message = "A stale-item resolution reason is required."
                });
            }

            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            var accessResult = await RequireAccessAsync(context, connection, requireTime: true);
            if (accessResult.Error is not null) return accessResult.Error;

            if (!accessResult.Access!.CanResolveStaleApprovals)
            {
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "Only Administrators and Super Administrators may resolve stale approval items."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var ageDays = await GetSubmittedAgeDaysAsync(connection, request.TimesheetId, request.WorkDate);
            if (ageDays is null)
            {
                return Results.NotFound(new { status = "not_found", message = "The selected submitted approval was not found." });
            }

            if (ageDays.Value < 7)
            {
                return Results.Conflict(new
                {
                    status = "not_stale",
                    ageDays,
                    message = "An approval must be at least seven days old before it can be resolved as stale."
                });
            }

            return await ProcessTimeActionAsync(
                request,
                context,
                targetStatus: "manager_declined",
                approvalStatus: "declined",
                auditAction: "timesheet_day_stale_approval_resolved",
                successMessage: "The stale approval was resolved and returned to the engineer with an audit record.",
                sendRejectionEmail: true);
        });

        app.MapPost("/api/manager/approvals/unlock", async (
            ApprovalActionRequest request,
            HttpContext context) =>
            await UnlockTimeAsync(request, context));

        app.MapPost("/api/manager/approvals/bulk-approve", async (
            BulkApprovalRequest request,
            HttpContext context) =>
        {
            if (request.Items is null || request.Items.Count == 0)
            {
                return Results.BadRequest(new { status = "validation_failed", message = "Select at least one submitted day." });
            }

            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            var accessResult = await RequireAccessAsync(context, connection, requireTime: true);
            if (accessResult.Error is not null) return accessResult.Error;

            if (!accessResult.Access!.CanViewAllTimeApprovals)
            {
                return Results.Json(new
                {
                    status = "access_denied",
                    message = "Bulk approval is restricted to Project Team Coordinators, Administrators, and Super Administrators."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var approved = 0;
            var failures = new List<object>();
            foreach (var item in request.Items.DistinctBy(item => new { item.TimesheetId, item.WorkDate }))
            {
                var result = await ProcessTimeActionCoreAsync(
                    connection,
                    accessResult.Access,
                    new ApprovalActionRequest(item.TimesheetId, item.WorkDate, request.Comment ?? "Bulk approved from Approval Center."),
                    "manager_approved",
                    "approved",
                    "timesheet_day_approval_center_bulk_approved",
                    false);

                if (result.Success) approved++;
                else failures.Add(new { item.TimesheetId, item.WorkDate, result.Status, result.Message });
            }

            return Results.Ok(new
            {
                status = failures.Count == 0 ? "bulk_approved" : "bulk_approved_with_exceptions",
                approvedCount = approved,
                failedCount = failures.Count,
                failures,
                message = $"Approved {approved} submitted day(s); {failures.Count} item(s) were not changed."
            });
        });

        return app;
    }

    private static async Task<IResult> BuildSummaryResultAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        var accessResult = await RequireAccessAsync(context, connection);
        if (accessResult.Error is not null) return accessResult.Error;
        var access = accessResult.Access!;

        var submittedTimePending = access.CanViewTimeApprovals
            ? await CountTimeApprovalsAsync(connection, access)
            : 0;

        var localResetPendingApproval = 0;
        var localResetReadyForTempPassword = 0;

        if (access.CanViewPasswordResetApprovals)
        {
            await using var resetCommand = new NpgsqlCommand("""
                SELECT
                    COUNT(*) FILTER (WHERE pr.status = 'pending_approval'),
                    COUNT(*) FILTER (WHERE pr.status = 'approved')
                FROM auth_password_reset_requests pr
                JOIN app_users u ON u.user_id = pr.user_id
                WHERE pr.status IN ('pending_approval', 'approved')
                  AND lower(u.email) LIKE '%.local';
                """, connection);

            await using var resetReader = await resetCommand.ExecuteReaderAsync();
            if (await resetReader.ReadAsync())
            {
                localResetPendingApproval = Convert.ToInt32(resetReader.GetInt64(0));
                localResetReadyForTempPassword = Convert.ToInt32(resetReader.GetInt64(1));
            }
        }

        var actionableTotal = submittedTimePending + localResetPendingApproval + localResetReadyForTempPassword;

        return Results.Ok(new
        {
            module = "002",
            status = "approval_summary_loaded",
            submittedTimePending,
            submittedTimeCount = submittedTimePending,
            pendingManagerApprovals = submittedTimePending,
            pendingProjectApprovals = 0,
            localResetPendingApproval,
            localResetReadyForTempPassword,
            passwordResetCount = localResetPendingApproval + localResetReadyForTempPassword,
            actionableTotal,
            totalPending = actionableTotal,
            totalPendingCount = actionableTotal,
            access = ToAccessPayload(access),
            refreshedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static async Task<List<TimeApprovalItem>> LoadTimeApprovalsAsync(
        NpgsqlConnection connection,
        ApprovalAccess access,
        DateOnly start,
        DateOnly end,
        bool includeAll,
        string? search)
    {
        var items = new List<TimeApprovalItem>();
        var normalizedSearch = search?.Trim() ?? string.Empty;

        await using var command = new NpgsqlCommand("""
            SELECT
                tds.timesheet_id,
                tds.user_id,
                COALESCE(u.display_name, u.email, 'Unknown resource'),
                COALESCE(u.email, ''),
                tds.work_date,
                tds.status,
                tds.submitted_at,
                COALESCE(SUM(CASE WHEN te.time_type = 'normal' THEN te.hours ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN te.time_type = 'afterhours' THEN te.hours ELSE 0 END), 0),
                COALESCE(SUM(te.hours), 0),
                COUNT(te.time_entry_id),
                COUNT(te.time_entry_id) FILTER (WHERE COALESCE(BTRIM(te.description), '') <> ''),
                COALESCE(STRING_AGG(DISTINCT COALESCE(npt.category_name, NULLIF(to_jsonb(p)->>'project_name', ''), 'Project time'), ', '), 'No entries'),
                tds.manager_decision_comment,
                GREATEST(0, COALESCE(EXTRACT(DAY FROM NOW() - tds.submitted_at)::int, 0)),
                COALESCE((
                    SELECT jsonb_agg(
                        jsonb_build_object(
                            'timeEntryId', detail.time_entry_id,
                            'timeType', detail.time_type,
                            'hours', detail.hours,
                            'description', COALESCE(detail.description, ''),
                            'projectId', detail.project_id,
                            'projectCode', COALESCE(to_jsonb(detail_project)->>'project_code', ''),
                            'projectName', COALESCE(to_jsonb(detail_project)->>'project_name', ''),
                            'taskId', COALESCE(to_jsonb(detail)->>'project_task_id', ''),
                            'categoryCode', COALESCE(detail_category.category_code, ''),
                            'categoryName', COALESCE(detail_category.category_name, '')
                        )
                        ORDER BY detail.created_at, detail.time_entry_id
                    )
                    FROM time_entries detail
                    LEFT JOIN projects detail_project ON detail_project.project_id = detail.project_id
                    LEFT JOIN non_project_time_categories detail_category
                      ON detail_category.non_project_time_category_id = detail.non_project_time_category_id
                    WHERE detail.timesheet_id = tds.timesheet_id
                      AND detail.work_date = tds.work_date
                ), '[]'::jsonb)::text
            FROM timesheet_day_statuses tds
            JOIN app_users u ON u.user_id = tds.user_id
            LEFT JOIN time_entries te
              ON te.timesheet_id = tds.timesheet_id
             AND te.work_date = tds.work_date
            LEFT JOIN projects p ON p.project_id = te.project_id
            LEFT JOIN non_project_time_categories npt
              ON npt.non_project_time_category_id = te.non_project_time_category_id
            WHERE tds.work_date BETWEEN @date_from AND @date_to
              AND tds.user_id <> @actor_user_id
              AND (
                    (@include_all = FALSE AND tds.status = 'submitted')
                 OR (@include_all = TRUE AND tds.status IN ('submitted', 'manager_approved', 'manager_declined'))
              )
              AND (
                    @can_view_all = TRUE
                 OR (@is_manager = TRUE AND lower(COALESCE(u.manager_email, '')) = lower(@actor_email))
                 OR (
                        @is_project_manager = TRUE
                    AND EXISTS (
                        SELECT 1
                        FROM time_entries scope_entry
                        JOIN projects scope_project ON scope_project.project_id = scope_entry.project_id
                        WHERE scope_entry.timesheet_id = tds.timesheet_id
                          AND scope_entry.work_date = tds.work_date
                          AND scope_project.project_manager_user_id = @actor_user_id
                    )
                 )
              )
              AND (
                    @search = ''
                 OR COALESCE(u.display_name, '') ILIKE '%' || @search || '%'
                 OR COALESCE(u.email, '') ILIKE '%' || @search || '%'
                 OR COALESCE(to_jsonb(p)->>'project_code', '') ILIKE '%' || @search || '%'
                 OR COALESCE(to_jsonb(p)->>'project_name', '') ILIKE '%' || @search || '%'
                 OR COALESCE(npt.category_name, '') ILIKE '%' || @search || '%'
              )
            GROUP BY
                tds.timesheet_id,
                tds.user_id,
                u.display_name,
                u.email,
                tds.work_date,
                tds.status,
                tds.submitted_at,
                tds.manager_decision_comment
            ORDER BY
                CASE WHEN tds.status = 'submitted' THEN 0 ELSE 1 END,
                tds.submitted_at NULLS LAST,
                tds.work_date,
                u.display_name;
            """, connection);

        AddScopeParameters(command, access);
        command.Parameters.AddWithValue("date_from", start);
        command.Parameters.AddWithValue("date_to", end);
        command.Parameters.AddWithValue("include_all", includeAll);
        command.Parameters.AddWithValue("search", normalizedSearch);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new TimeApprovalItem(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateOnly>(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetInt32(14),
                JsonDocument.Parse(reader.GetString(15)).RootElement.Clone()));
        }

        return items;
    }

    private static async Task<int> CountTimeApprovalsAsync(NpgsqlConnection connection, ApprovalAccess access)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM timesheet_day_statuses tds
            JOIN app_users u ON u.user_id = tds.user_id
            WHERE tds.status = 'submitted'
              AND tds.user_id <> @actor_user_id
              AND (
                    @can_view_all = TRUE
                 OR (@is_manager = TRUE AND lower(COALESCE(u.manager_email, '')) = lower(@actor_email))
                 OR (
                        @is_project_manager = TRUE
                    AND EXISTS (
                        SELECT 1
                        FROM time_entries scope_entry
                        JOIN projects scope_project ON scope_project.project_id = scope_entry.project_id
                        WHERE scope_entry.timesheet_id = tds.timesheet_id
                          AND scope_entry.work_date = tds.work_date
                          AND scope_project.project_manager_user_id = @actor_user_id
                    )
                 )
              );
            """, connection);

        AddScopeParameters(command, access);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<IResult> ProcessTimeActionAsync(
        ApprovalActionRequest request,
        HttpContext context,
        string targetStatus,
        string approvalStatus,
        string auditAction,
        string successMessage,
        bool sendRejectionEmail)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();

        var accessResult = await RequireAccessAsync(context, connection, requireTime: true);
        if (accessResult.Error is not null) return accessResult.Error;

        var result = await ProcessTimeActionCoreAsync(
            connection,
            accessResult.Access!,
            request,
            targetStatus,
            approvalStatus,
            auditAction,
            sendRejectionEmail);

        if (!result.Success)
        {
            return Results.Json(new { status = result.Status, message = result.Message }, statusCode: result.StatusCode);
        }

        return Results.Ok(new
        {
            status = targetStatus,
            request.TimesheetId,
            request.WorkDate,
            message = successMessage,
            emailNotificationStatus = sendRejectionEmail ? "queued_global_smtp" : "not_required",
            actionableCountRefreshRequired = true
        });
    }

    private static async Task<ActionResult> ProcessTimeActionCoreAsync(
        NpgsqlConnection connection,
        ApprovalAccess access,
        ApprovalActionRequest request,
        string targetStatus,
        string approvalStatus,
        string auditAction,
        bool sendRejectionEmail)
    {
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var target = await LoadTargetAsync(connection, transaction, request.TimesheetId, request.WorkDate);
            if (target is null)
            {
                await transaction.RollbackAsync();
                return ActionResult.Fail("not_found", "The selected approval item was not found.", 404);
            }

            if (!await CanAccessTargetAsync(connection, transaction, access, target.UserId, target.ManagerEmail, request.TimesheetId, request.WorkDate))
            {
                await transaction.RollbackAsync();
                return ActionResult.Fail("access_denied", "The selected time is outside your approval scope.", 403);
            }

            if (string.Equals(targetStatus, "manager_declined", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(request.Comment))
            {
                await transaction.RollbackAsync();
                return ActionResult.Fail("validation_failed", "A specific rejection reason is required.", 400);
            }

            var entries = await LoadEntryDetailsAsync(connection, transaction, request.TimesheetId, request.WorkDate);
            var comment = string.IsNullOrWhiteSpace(request.Comment) ? "Approved from Approval Center." : request.Comment.Trim();
            var timestampColumn = targetStatus == "manager_approved" ? "manager_approved_at" : "manager_declined_at";
            var oppositeColumn = targetStatus == "manager_approved" ? "manager_declined_at" : "manager_approved_at";

            await using (var statusCommand = new NpgsqlCommand($"""
                UPDATE timesheet_day_statuses
                SET status = @target_status,
                    manager_user_id = @actor_user_id,
                    manager_decision_comment = @comment,
                    {timestampColumn} = NOW(),
                    {oppositeColumn} = NULL,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = 'submitted'
                RETURNING timesheet_day_status_id;
                """, connection, transaction))
            {
                statusCommand.Parameters.AddWithValue("target_status", targetStatus);
                statusCommand.Parameters.AddWithValue("actor_user_id", access.UserId);
                statusCommand.Parameters.AddWithValue("comment", comment);
                statusCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
                statusCommand.Parameters.AddWithValue("work_date", request.WorkDate);

                if (await statusCommand.ExecuteScalarAsync() is not Guid)
                {
                    await transaction.RollbackAsync();
                    return ActionResult.Fail("not_pending_approval", "Only submitted time awaiting approval can be changed.", 409);
                }
            }

            await using (var entryCommand = new NpgsqlCommand("""
                UPDATE time_entries
                SET status = @target_status,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = 'submitted';
                """, connection, transaction))
            {
                entryCommand.Parameters.AddWithValue("target_status", targetStatus);
                entryCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
                entryCommand.Parameters.AddWithValue("work_date", request.WorkDate);
                await entryCommand.ExecuteNonQueryAsync();
            }

            await using (var approvalCommand = new NpgsqlCommand("""
                INSERT INTO approval_records (
                    time_entry_id,
                    approval_stage,
                    approval_status,
                    approver_user_id,
                    decision_comment
                )
                SELECT
                    time_entry_id,
                    'manager',
                    @approval_status,
                    @actor_user_id,
                    @comment
                FROM time_entries
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date;
                """, connection, transaction))
            {
                approvalCommand.Parameters.AddWithValue("approval_status", approvalStatus);
                approvalCommand.Parameters.AddWithValue("actor_user_id", access.UserId);
                approvalCommand.Parameters.AddWithValue("comment", comment);
                approvalCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
                approvalCommand.Parameters.AddWithValue("work_date", request.WorkDate);
                await approvalCommand.ExecuteNonQueryAsync();
            }

            await InsertAuditAsync(connection, transaction, access.UserId, auditAction, request.TimesheetId);

            if (sendRejectionEmail)
            {
                await QueueRejectionEmailAsync(connection, transaction, access, target, request, entries, comment);
            }

            await transaction.CommitAsync();
            return ActionResult.Ok();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ActionResult.Fail("approval_action_failed", ex.Message, 500);
        }
    }

    private static async Task<IResult> UnlockTimeAsync(ApprovalActionRequest request, HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var accessResult = await RequireAccessAsync(context, connection, requireTime: true);
        if (accessResult.Error is not null) return accessResult.Error;
        var access = accessResult.Access!;

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var target = await LoadTargetAsync(connection, transaction, request.TimesheetId, request.WorkDate);
            if (target is null)
            {
                await transaction.RollbackAsync();
                return Results.NotFound(new { status = "not_found", message = "The selected approval item was not found." });
            }

            if (!await CanAccessTargetAsync(connection, transaction, access, target.UserId, target.ManagerEmail, request.TimesheetId, request.WorkDate))
            {
                await transaction.RollbackAsync();
                return Results.Json(new { status = "access_denied", message = "The selected time is outside your approval scope." }, statusCode: 403);
            }

            await using var statusCommand = new NpgsqlCommand("""
                UPDATE timesheet_day_statuses
                SET status = 'draft',
                    manager_user_id = @actor_user_id,
                    manager_decision_comment = @comment,
                    manager_unlocked_at = NOW(),
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status IN ('submitted', 'manager_approved', 'manager_declined')
                RETURNING timesheet_day_status_id;
                """, connection, transaction);

            statusCommand.Parameters.AddWithValue("actor_user_id", access.UserId);
            statusCommand.Parameters.AddWithValue("comment", string.IsNullOrWhiteSpace(request.Comment) ? "Unlocked from Approval Center." : request.Comment.Trim());
            statusCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
            statusCommand.Parameters.AddWithValue("work_date", request.WorkDate);

            if (await statusCommand.ExecuteScalarAsync() is not Guid)
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new { status = "unlock_not_allowed", message = "The selected day cannot be unlocked from its current status." });
            }

            await using var entryCommand = new NpgsqlCommand("""
                UPDATE time_entries
                SET status = 'draft', updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date;
                """, connection, transaction);
            entryCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
            entryCommand.Parameters.AddWithValue("work_date", request.WorkDate);
            await entryCommand.ExecuteNonQueryAsync();

            await InsertAuditAsync(connection, transaction, access.UserId, "timesheet_day_approval_center_unlocked", request.TimesheetId);
            await transaction.CommitAsync();

            return Results.Ok(new
            {
                status = "manager_unlocked",
                request.TimesheetId,
                request.WorkDate,
                message = "The day was unlocked for correction.",
                actionableCountRefreshRequired = true
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Results.Problem(title: "Failed to unlock time", detail: ex.Message, statusCode: 500);
        }
    }

    private static async Task<TargetUser?> LoadTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timesheetId,
        DateOnly workDate)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                u.user_id,
                COALESCE(u.email, ''),
                COALESCE(u.display_name, u.email, 'Engineer'),
                COALESCE(u.manager_email, ''),
                tds.status,
                tds.submitted_at
            FROM timesheet_day_statuses tds
            JOIN app_users u ON u.user_id = tds.user_id
            WHERE tds.timesheet_id = @timesheet_id
              AND tds.work_date = @work_date;
            """, connection, transaction);

        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        command.Parameters.AddWithValue("work_date", workDate);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new TargetUser(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }

    private static async Task<List<EntryDetail>> LoadEntryDetailsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timesheetId,
        DateOnly workDate)
    {
        var entries = new List<EntryDetail>();
        await using var command = new NpgsqlCommand("""
            SELECT
                te.time_entry_id,
                COALESCE(te.time_type, 'normal'),
                te.hours,
                COALESCE(te.description, ''),
                COALESCE(to_jsonb(p)->>'project_code', ''),
                COALESCE(to_jsonb(p)->>'project_name', ''),
                COALESCE(npt.category_code, ''),
                COALESCE(npt.category_name, ''),
                COALESCE(to_jsonb(te)->>'project_task_id', '')
            FROM time_entries te
            LEFT JOIN projects p ON p.project_id = te.project_id
            LEFT JOIN non_project_time_categories npt
              ON npt.non_project_time_category_id = te.non_project_time_category_id
            WHERE te.timesheet_id = @timesheet_id
              AND te.work_date = @work_date
            ORDER BY te.created_at, te.time_entry_id;
            """, connection, transaction);

        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        command.Parameters.AddWithValue("work_date", workDate);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new EntryDetail(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8)));
        }

        return entries;
    }

    private static async Task QueueRejectionEmailAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalAccess reviewer,
        TargetUser target,
        ApprovalActionRequest request,
        IReadOnlyList<EntryDetail> entries,
        string reason)
    {
        var subject = $"ProjectPulse: Time returned for correction — {request.WorkDate:MMMM d, yyyy}";
        var body = new StringBuilder()
            .AppendLine($"Hello {target.DisplayName},")
            .AppendLine()
            .AppendLine("One or more submitted time entries were returned for correction in ProjectPulse.")
            .AppendLine()
            .AppendLine($"Reviewed by: {reviewer.DisplayName} — {reviewer.PrimaryRoleLabel}")
            .AppendLine($"Work date: {request.WorkDate:MMMM d, yyyy}")
            .AppendLine($"Timesheet ID: {request.TimesheetId}")
            .AppendLine()
            .AppendLine("Rejected entries:");

        if (entries.Count == 0)
        {
            body.AppendLine("• No detailed time-entry rows were available; the submitted day was returned in full.");
        }
        else
        {
            foreach (var entry in entries)
            {
                var workLabel = !string.IsNullOrWhiteSpace(entry.ProjectCode)
                    ? $"{entry.ProjectCode} {entry.ProjectName}".Trim()
                    : !string.IsNullOrWhiteSpace(entry.CategoryName)
                        ? $"{entry.CategoryCode} {entry.CategoryName}".Trim()
                        : "Time entry";

                body.AppendLine($"• {workLabel}")
                    .AppendLine($"  Entry ID: {entry.TimeEntryId}")
                    .AppendLine($"  Task ID: {(string.IsNullOrWhiteSpace(entry.TaskId) ? "Not assigned" : entry.TaskId)}")
                    .AppendLine($"  Type: {entry.TimeType}")
                    .AppendLine($"  Hours: {entry.Hours:N2}")
                    .AppendLine($"  Submitted description: {(string.IsNullOrWhiteSpace(entry.Description) ? "No description provided" : entry.Description)}");
            }
        }

        body.AppendLine()
            .AppendLine("Reason:")
            .AppendLine(reason)
            .AppendLine()
            .AppendLine("Required action:")
            .AppendLine("Open ProjectPulse, correct the returned time, and resubmit it for approval.")
            .AppendLine()
            .AppendLine("ProjectPulse: https://phd-west-test.onenecklab.com/#timesheet");

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
                'time_entry_rejection_global_smtp',
                @recipient_email,
                @subject,
                @body,
                'timesheet',
                @related_entity_id
            );
            """, connection, transaction))
        {
            notifyCommand.Parameters.AddWithValue("recipient_email", target.Email);
            notifyCommand.Parameters.AddWithValue("subject", subject);
            notifyCommand.Parameters.AddWithValue("body", body.ToString());
            notifyCommand.Parameters.AddWithValue("related_entity_id", request.TimesheetId);
            await notifyCommand.ExecuteNonQueryAsync();
        }

        await using var emailCommand = new NpgsqlCommand("""
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
                'TIME_ENTRY_REJECTION',
                @recipient_email,
                @recipient_name,
                @subject,
                @body,
                'queued',
                NOW()
            );
            """, connection, transaction);

        emailCommand.Parameters.AddWithValue("recipient_email", target.Email);
        emailCommand.Parameters.AddWithValue("recipient_name", target.DisplayName);
        emailCommand.Parameters.AddWithValue("subject", subject);
        emailCommand.Parameters.AddWithValue("body", body.ToString());
        await emailCommand.ExecuteNonQueryAsync();
    }

    private static async Task<bool> CanAccessTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApprovalAccess access,
        Guid targetUserId,
        string targetManagerEmail,
        Guid timesheetId,
        DateOnly workDate)
    {
        if (targetUserId == access.UserId) return false;
        if (access.CanViewAllTimeApprovals) return true;

        if (access.IsManager
            && !string.IsNullOrWhiteSpace(access.Email)
            && string.Equals(targetManagerEmail, access.Email, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!access.IsProjectManager) return false;

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM time_entries te
                JOIN projects p ON p.project_id = te.project_id
                WHERE te.timesheet_id = @timesheet_id
                  AND te.work_date = @work_date
                  AND p.project_manager_user_id = @actor_user_id
            );
            """, connection, transaction);

        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        command.Parameters.AddWithValue("work_date", workDate);
        command.Parameters.AddWithValue("actor_user_id", access.UserId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int?> GetSubmittedAgeDaysAsync(
        NpgsqlConnection connection,
        Guid timesheetId,
        DateOnly workDate)
    {
        await using var command = new NpgsqlCommand("""
            SELECT GREATEST(0, EXTRACT(DAY FROM NOW() - submitted_at)::int)
            FROM timesheet_day_statuses
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
              AND status = 'submitted';
            """, connection);

        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        command.Parameters.AddWithValue("work_date", workDate);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        string action,
        Guid timesheetId)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id)
            VALUES (@actor_user_id, @action, 'timesheet', @timesheet_id);
            """, connection, transaction);

        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("timesheet_id", timesheetId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(ApprovalAccess? Access, IResult? Error)> RequireAccessAsync(
        HttpContext context,
        NpgsqlConnection connection,
        bool requireTime = false)
    {
        var userId = SessionUserId(context);
        if (userId is null)
        {
            return (null, Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var access = await ResolveAccessAsync(connection, userId.Value);
        if (access is null || (!access.CanViewTimeApprovals && !access.CanViewPasswordResetApprovals))
        {
            return (null, Results.Json(new
            {
                status = "access_denied",
                message = "Approval Center access is restricted to Managers, Project Managers, Project Team Coordinators, Administrators, and Super Administrators."
            }, statusCode: StatusCodes.Status403Forbidden));
        }

        if (requireTime && !access.CanViewTimeApprovals)
        {
            return (null, Results.Json(new
            {
                status = "access_denied",
                message = "Time approval access is not available for this role."
            }, statusCode: StatusCodes.Status403Forbidden));
        }

        return (access, null);
    }

    private static async Task<ApprovalAccess?> ResolveAccessAsync(NpgsqlConnection connection, Guid userId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                COALESCE(u.email, ''),
                COALESCE(u.display_name, u.email, 'ProjectPulse user'),
                COALESCE(array_agg(DISTINCT r.role_code) FILTER (WHERE r.role_code IS NOT NULL), ARRAY[]::text[])
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
              ON ura.user_id = u.user_id
             AND ura.is_active = TRUE
            LEFT JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
             AND r.is_active = TRUE
            WHERE u.user_id = @user_id
              AND u.is_active = TRUE
            GROUP BY u.user_id, u.email, u.display_name;
            """, connection);

        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var email = reader.GetString(0);
        var displayName = reader.GetString(1);
        var roles = reader.GetFieldValue<string[]>(2)
            .Select(role => role.Trim().ToUpperInvariant())
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isSuperAdmin = roleSet.Contains("SUPER_ADMINISTRATOR");
        var isAdmin = roleSet.Contains("ADMINISTRATOR");
        var isCoordinator = roleSet.Contains("PROJECT_TEAM_COORDINATOR");
        var isManager = roleSet.Contains("MANAGER");
        var isProjectManager = roleSet.Contains("PROJECT_MANAGER") || roleSet.Contains("PROJECT_MANAGEMENT");
        var canViewAll = isSuperAdmin || isAdmin || isCoordinator;
        var canViewTime = roles.Any(role => TimeApprovalRoles.Contains(role));
        var canViewPasswordReset = roles.Any(role => PasswordResetRoles.Contains(role));

        var scope = canViewAll
            ? "organization"
            : isManager && isProjectManager
                ? "team_and_managed_projects"
                : isManager
                    ? "direct_reports"
                    : isProjectManager
                        ? "managed_projects"
                        : "none";

        var scopeLabel = scope switch
        {
            "organization" => "All organization approvals",
            "team_and_managed_projects" => "Direct reports and managed projects",
            "direct_reports" => "My direct reports",
            "managed_projects" => "My managed projects",
            _ => "No approval scope"
        };

        var primaryRoleLabel = isSuperAdmin
            ? "Super Administrator"
            : isAdmin
                ? "Administrator"
                : isCoordinator
                    ? "Project Team Coordinator"
                    : isManager
                        ? "Manager"
                        : "Project Manager";

        return new ApprovalAccess(
            userId,
            email,
            displayName,
            roles,
            canViewTime,
            canViewPasswordReset,
            canViewAll,
            isManager,
            isProjectManager,
            isSuperAdmin || isAdmin,
            scope,
            scopeLabel,
            primaryRoleLabel);
    }

    private static object ToAccessPayload(ApprovalAccess access) => new
    {
        access.UserId,
        access.Email,
        access.DisplayName,
        roleCodes = access.Roles,
        access.CanViewTimeApprovals,
        access.CanViewPasswordResetApprovals,
        access.CanViewAllTimeApprovals,
        access.CanResolveStaleApprovals,
        access.Scope,
        access.ScopeLabel,
        access.PrimaryRoleLabel
    };

    private static void AddScopeParameters(NpgsqlCommand command, ApprovalAccess access)
    {
        command.Parameters.AddWithValue("actor_user_id", access.UserId);
        command.Parameters.AddWithValue("actor_email", access.Email);
        command.Parameters.AddWithValue("can_view_all", access.CanViewAllTimeApprovals);
        command.Parameters.AddWithValue("is_manager", access.IsManager);
        command.Parameters.AddWithValue("is_project_manager", access.IsProjectManager);
    }

    private static Guid? SessionUserId(HttpContext context)
    {
        foreach (var key in new[]
        {
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId",
            "ProjectPulseActualUserId"
        })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid guid) return guid;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    private static DateOnly Sunday(DateOnly date) => date.AddDays(-(int)date.DayOfWeek);

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

    private sealed record ApprovalActionRequest(Guid TimesheetId, DateOnly WorkDate, string? Comment);
    private sealed record BulkApprovalRequest(List<ApprovalActionRequest>? Items, string? Comment);

    private sealed record ApprovalAccess(
        Guid UserId,
        string Email,
        string DisplayName,
        string[] Roles,
        bool CanViewTimeApprovals,
        bool CanViewPasswordResetApprovals,
        bool CanViewAllTimeApprovals,
        bool IsManager,
        bool IsProjectManager,
        bool CanResolveStaleApprovals,
        string Scope,
        string ScopeLabel,
        string PrimaryRoleLabel);

    private sealed record TimeApprovalItem(
        Guid TimesheetId,
        Guid UserId,
        string ResourceName,
        string ResourceEmail,
        DateOnly WorkDate,
        string Status,
        DateTimeOffset? SubmittedAt,
        decimal NormalHours,
        decimal Afterhours,
        decimal TotalHours,
        long EntryCount,
        long CommentCount,
        string ActivitySummary,
        string? ManagerDecisionComment,
        int AgeDays,
        JsonElement Entries);

    private sealed record TargetUser(
        Guid UserId,
        string Email,
        string DisplayName,
        string ManagerEmail,
        string Status,
        DateTimeOffset? SubmittedAt);

    private sealed record EntryDetail(
        Guid TimeEntryId,
        string TimeType,
        decimal Hours,
        string Description,
        string ProjectCode,
        string ProjectName,
        string CategoryCode,
        string CategoryName,
        string TaskId);

    private sealed record ActionResult(bool Success, string Status, string Message, int StatusCode)
    {
        public static ActionResult Ok() => new(true, "ok", "", 200);
        public static ActionResult Fail(string status, string message, int statusCode) => new(false, status, message, statusCode);
    }
}
