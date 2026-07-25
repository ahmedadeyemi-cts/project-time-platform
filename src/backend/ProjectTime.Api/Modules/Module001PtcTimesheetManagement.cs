using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    public sealed record PtcReasonRequest(string? Reason);

    public sealed record PtcTimeEntryEditRequest(
        Guid TargetUserId,
        decimal? Hours,
        string? Description,
        bool? Billable,
        string? Reason);

    public sealed record PtcTimeEntryMoveRequest(
        Guid TargetUserId,
        Guid AssignmentId,
        string? Reason);

    public sealed record PtcTimeEntryRemoveRequest(
        Guid TargetUserId,
        string? Reason);

    public sealed record PtcTaskCreateRequest(
        Guid TargetUserId,
        Guid ProjectId,
        string? TaskCode,
        string? TaskName,
        string? TaskDescription,
        bool Billable,
        string? Reason);

    public static WebApplication MapModule001PtcTimesheetManagementEndpoints(this WebApplication app)
    {
        app.MapGet("/api/timesheet/ptc/users", PtcUsersAsync);
        app.MapGet("/api/timesheet/ptc/users/{targetUserId:guid}/entries", PtcUserEntriesAsync);
        app.MapPost("/api/timesheet/ptc/users/{targetUserId:guid}/weeks/{weekStart}/unsubmit", PtcUnsubmitWeekAsync);
        app.MapPatch("/api/timesheet/ptc/entries/{timeEntryId:guid}", PtcEditEntryAsync);
        app.MapPost("/api/timesheet/ptc/entries/{timeEntryId:guid}/move", PtcMoveEntryAsync);
        app.MapPost("/api/timesheet/ptc/entries/{timeEntryId:guid}/remove", PtcRemoveEntryAsync);
        app.MapPost("/api/timesheet/ptc/tasks", PtcCreateTaskAsync);
        return app;
    }

    private static async Task<IResult?> RequirePtcTimeStewardTablesAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.scoped_time_management_events') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM schema_migrations
                   WHERE migration_id = '043_ptc_time_steward_permissions'
               );
            """, connection);
        if (Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false)) return null;

        return Results.Json(new
        {
            status = "ptc_time_steward_migration_required",
            migration = "043_ptc_time_steward_permissions",
            message = "Apply migration 043 before using the Project Team Coordinator time-steward workspace."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<(ActorContext? Actor, ScopedAuthorizationDecision? Decision, IResult? Error)>
        RequirePtcTimeStewardAccessAsync(
            HttpContext context,
            NpgsqlConnection connection,
            string actionCode,
            Guid? targetUserId,
            Guid? projectId,
            bool isWrite)
    {
        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return (null, null, SessionRequired());
        if (actor.IsViewAs && isWrite)
        {
            return (actor, null, Results.Json(new
            {
                status = "view_as_read_only",
                message = "Time-steward changes are disabled while using Administrator View-As. Select users inside the PTC workspace instead."
            }, statusCode: StatusCodes.Status403Forbidden));
        }

        var decision = await ScopedAuthorizationEvaluator.EvaluateAsync(
            connection,
            actor,
            "001",
            actionCode,
            targetUserId,
            projectId,
            null,
            isWrite);

        if (!decision.Allowed)
        {
            return (actor, decision, Results.Json(new
            {
                status = "scoped_access_denied",
                moduleCode = "001",
                actionCode,
                decision.ScopeCode,
                decision.ExplicitDeny,
                decision.IsViewAs,
                message = decision.Explanation
            }, statusCode: StatusCodes.Status403Forbidden));
        }

        return (actor, decision, null);
    }

    private static bool TryPtcWeekStart(string? raw, out DateOnly weekStart)
    {
        if (!DateOnly.TryParse(raw, out weekStart)) return false;
        return weekStart.DayOfWeek == DayOfWeek.Sunday;
    }

    private static DateOnly PtcRequestedWeek(HttpContext context)
    {
        var raw = context.Request.Query["weekStart"].FirstOrDefault();
        return TryPtcWeekStart(raw, out var parsed)
            ? parsed
            : Module001WeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static IResult ReasonRequired(string action) => Results.BadRequest(new
    {
        status = "reason_required",
        message = $"Enter a reason before you {action}. The reason is retained in immutable audit evidence."
    });

    private static async Task<bool> ActiveUserExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM app_users WHERE user_id=@user_id AND is_active=TRUE);",
            connection,
            transaction);
        command.Parameters.AddWithValue("user_id", userId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task InsertPtcTimeAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string actionCode,
        ActorContext actor,
        Guid targetUserId,
        Guid? timesheetId,
        Guid? timeEntryId,
        Guid? projectId,
        Guid? taskId,
        string reason,
        object originalValues,
        object revisedValues,
        object? metadata = null)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO scoped_time_management_events (
                action_code, actor_user_id, target_user_id,
                timesheet_id, time_entry_id, project_id, task_id,
                reason, original_values, revised_values, event_metadata
            ) VALUES (
                @action_code, @actor_user_id, @target_user_id,
                @timesheet_id, @time_entry_id, @project_id, @task_id,
                @reason, @original_values::jsonb, @revised_values::jsonb,
                @event_metadata::jsonb
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("action_code", actionCode);
        command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("target_user_id", targetUserId);
        AddNullableGuid(command, "timesheet_id", timesheetId);
        AddNullableGuid(command, "time_entry_id", timeEntryId);
        AddNullableGuid(command, "project_id", projectId);
        AddNullableGuid(command, "task_id", taskId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("original_values", JsonSerializer.Serialize(originalValues));
        command.Parameters.AddWithValue("revised_values", JsonSerializer.Serialize(revisedValues));
        command.Parameters.AddWithValue("event_metadata", JsonSerializer.Serialize(metadata ?? new
        {
            actor.ActualUserId,
            actor.EffectiveUserId,
            actor.RoleCodes,
            actor.IsViewAs,
            immutableAudit = true,
            submissionOnBehalf = false
        }));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IResult> PtcUsersAsync(HttpContext context)
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
            SELECT
                u.user_id,
                u.email,
                COALESCE(NULLIF(u.display_name, ''), u.email),
                t.timesheet_id,
                COALESCE(t.status, 'not_started'),
                COALESCE(SUM(te.hours), 0),
                COUNT(te.time_entry_id),
                MAX(COALESCE(te.updated_at, t.updated_at, u.updated_at))
            FROM app_users u
            LEFT JOIN timesheets t
              ON t.user_id = u.user_id
             AND t.week_start_date = @week_start
            LEFT JOIN time_entries te
              ON te.timesheet_id = t.timesheet_id
             AND te.work_date BETWEEN @week_start AND @week_end
            WHERE u.is_active = TRUE
              AND (
                  @search = ''
                  OR u.email ILIKE '%' || @search || '%'
                  OR COALESCE(u.display_name, '') ILIKE '%' || @search || '%'
              )
            GROUP BY u.user_id, u.email, u.display_name, t.timesheet_id, t.status
            ORDER BY COALESCE(NULLIF(u.display_name, ''), u.email), u.email
            LIMIT 500;
            """, connection);
        command.Parameters.AddWithValue("week_start", weekStart);
        command.Parameters.AddWithValue("week_end", weekEnd);
        command.Parameters.AddWithValue("search", search);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new
            {
                userId = reader.GetGuid(0),
                email = reader.GetString(1),
                displayName = reader.GetString(2),
                timesheetId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
                status = reader.GetString(4),
                totalHours = reader.GetDecimal(5),
                entryCount = Convert.ToInt32(reader.GetInt64(6)),
                lastUpdatedAt = reader.IsDBNull(7) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(7)
            });
        }

        return Results.Ok(new
        {
            weekStart,
            weekEnd,
            count = users.Count,
            canSubmitOnBehalf = false,
            workspaceMode = "PTC_TIME_STEWARD",
            users
        });
    }

    private static async Task<IResult> PtcUserEntriesAsync(Guid targetUserId, HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_VIEW_ON_BEHALF", targetUserId, null, false);
        if (access.Error is not null) return access.Error;
        if (!await ActiveUserExistsAsync(connection, null, targetUserId))
            return Results.NotFound(new { status = "user_not_found", message = "The selected active user was not found." });

        var weekStart = PtcRequestedWeek(context);
        var weekEnd = weekStart.AddDays(6);
        object? user = null;
        await using (var userCommand = new NpgsqlCommand("""
            SELECT user_id, email, COALESCE(NULLIF(display_name, ''), email)
            FROM app_users
            WHERE user_id=@user_id AND is_active=TRUE;
            """, connection))
        {
            userCommand.Parameters.AddWithValue("user_id", targetUserId);
            await using var userReader = await userCommand.ExecuteReaderAsync();
            if (await userReader.ReadAsync())
            {
                user = new
                {
                    userId = userReader.GetGuid(0),
                    email = userReader.GetString(1),
                    displayName = userReader.GetString(2)
                };
            }
        }

        object? timesheet = null;
        await using (var timesheetCommand = new NpgsqlCommand("""
            SELECT timesheet_id, status, submitted_at, updated_at
            FROM timesheets
            WHERE user_id=@user_id AND week_start_date=@week_start;
            """, connection))
        {
            timesheetCommand.Parameters.AddWithValue("user_id", targetUserId);
            timesheetCommand.Parameters.AddWithValue("week_start", weekStart);
            await using var timesheetReader = await timesheetCommand.ExecuteReaderAsync();
            if (await timesheetReader.ReadAsync())
            {
                timesheet = new
                {
                    timesheetId = timesheetReader.GetGuid(0),
                    status = timesheetReader.GetString(1),
                    submittedAt = timesheetReader.IsDBNull(2) ? (DateTimeOffset?)null : timesheetReader.GetFieldValue<DateTimeOffset>(2),
                    updatedAt = timesheetReader.GetFieldValue<DateTimeOffset>(3)
                };
            }
        }

        var entries = new List<object>();
        await using (var entryCommand = new NpgsqlCommand("""
            SELECT
                te.time_entry_id, te.timesheet_id, te.work_date, te.hours,
                COALESCE(te.description, ''), te.billable, te.status,
                te.project_id, COALESCE(p.project_code, ''), COALESCE(p.project_name, ''),
                te.task_id, COALESCE(pt.task_code, ''), COALESCE(pt.task_name, ''),
                te.updated_at
            FROM time_entries te
            LEFT JOIN projects p ON p.project_id = te.project_id
            LEFT JOIN project_tasks pt ON pt.task_id = te.task_id
            WHERE te.user_id=@user_id
              AND te.work_date BETWEEN @week_start AND @week_end
            ORDER BY te.work_date, p.project_code, pt.task_code, te.created_at;
            """, connection))
        {
            entryCommand.Parameters.AddWithValue("user_id", targetUserId);
            entryCommand.Parameters.AddWithValue("week_start", weekStart);
            entryCommand.Parameters.AddWithValue("week_end", weekEnd);
            await using var entryReader = await entryCommand.ExecuteReaderAsync();
            while (await entryReader.ReadAsync())
            {
                entries.Add(new
                {
                    timeEntryId = entryReader.GetGuid(0),
                    timesheetId = entryReader.GetGuid(1),
                    workDate = entryReader.GetFieldValue<DateOnly>(2),
                    hours = entryReader.GetDecimal(3),
                    description = entryReader.GetString(4),
                    billable = entryReader.GetBoolean(5),
                    status = entryReader.GetString(6),
                    projectId = entryReader.GetGuid(7),
                    projectCode = entryReader.GetString(8),
                    projectName = entryReader.GetString(9),
                    taskId = entryReader.IsDBNull(10) ? (Guid?)null : entryReader.GetGuid(10),
                    taskCode = entryReader.GetString(11),
                    taskName = entryReader.GetString(12),
                    updatedAt = entryReader.GetFieldValue<DateTimeOffset>(13)
                });
            }
        }

        var assignments = new List<object>();
        await using (var assignmentCommand = new NpgsqlCommand("""
            SELECT
                pa.project_assignment_id, pa.project_id,
                p.project_code, p.project_name,
                pa.task_id, pt.task_code, pt.task_name,
                pt.billable
            FROM project_assignments pa
            JOIN projects p ON p.project_id=pa.project_id
            JOIN project_tasks pt ON pt.task_id=pa.task_id AND pt.project_id=pa.project_id
            WHERE pa.user_id=@user_id
              AND pa.effective_start_date <= @week_end
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start)
              AND pt.is_active=TRUE
              AND p.status IN ('active','on_hold')
            ORDER BY p.project_code, pt.task_code;
            """, connection))
        {
            assignmentCommand.Parameters.AddWithValue("user_id", targetUserId);
            assignmentCommand.Parameters.AddWithValue("week_start", weekStart);
            assignmentCommand.Parameters.AddWithValue("week_end", weekEnd);
            await using var assignmentReader = await assignmentCommand.ExecuteReaderAsync();
            while (await assignmentReader.ReadAsync())
            {
                assignments.Add(new
                {
                    assignmentId = assignmentReader.GetGuid(0),
                    projectId = assignmentReader.GetGuid(1),
                    projectCode = assignmentReader.GetString(2),
                    projectName = assignmentReader.GetString(3),
                    taskId = assignmentReader.GetGuid(4),
                    taskCode = assignmentReader.GetString(5),
                    taskName = assignmentReader.GetString(6),
                    billable = assignmentReader.GetBoolean(7)
                });
            }
        }

        return Results.Ok(new
        {
            user,
            weekStart,
            weekEnd,
            timesheet,
            entries,
            assignments,
            canSubmitOnBehalf = false,
            requiredCorrectionFlow = "UNSUBMIT_OR_REOPEN_THEN_EDIT_MOVE_REMOVE_THEN_USER_RESUBMITS"
        });
    }

    private static async Task<IResult> PtcUnsubmitWeekAsync(
        Guid targetUserId,
        string weekStart,
        PtcReasonRequest request,
        HttpContext context)
    {
        if (!TryPtcWeekStart(weekStart, out var parsedWeekStart))
            return Results.BadRequest(new { status = "invalid_week_start", message = "WeekStart must be a Sunday in YYYY-MM-DD format." });
        if (string.IsNullOrWhiteSpace(request.Reason)) return ReasonRequired("return the selected user's time to draft");

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_UNSUBMIT", targetUserId, null, true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        Guid? timesheetId = null;
        string? previousStatus = null;
        await using (var select = new NpgsqlCommand("""
            SELECT timesheet_id, status
            FROM timesheets
            WHERE user_id=@user_id AND week_start_date=@week_start
            FOR UPDATE;
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("user_id", targetUserId);
            select.Parameters.AddWithValue("week_start", parsedWeekStart);
            await using var reader = await select.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                timesheetId = reader.GetGuid(0);
                previousStatus = reader.GetString(1);
            }
        }

        if (timesheetId is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "timesheet_not_found", message = "The selected user does not have a timesheet for this week." });
        }
        if (previousStatus == "draft")
        {
            await transaction.RollbackAsync();
            return Results.Ok(new { status = "already_draft", timesheetId, canSubmitOnBehalf = false });
        }
        if (previousStatus == "reconciled")
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new { status = "reconciled_time_locked", message = "Reconciled time cannot be returned to draft through the operational PTC workflow." });
        }

        await using (var updateTimesheet = new NpgsqlCommand("""
            UPDATE timesheets
            SET status='draft', submitted_at=NULL, updated_at=NOW()
            WHERE timesheet_id=@timesheet_id;

            UPDATE timesheet_day_statuses
            SET status='draft', manager_decision_comment=@reason, updated_at=NOW()
            WHERE timesheet_id=@timesheet_id;

            UPDATE time_entries
            SET status='draft', updated_at=NOW()
            WHERE timesheet_id=@timesheet_id;
            """, connection, transaction))
        {
            updateTimesheet.Parameters.AddWithValue("timesheet_id", timesheetId.Value);
            updateTimesheet.Parameters.AddWithValue("reason", request.Reason.Trim());
            await updateTimesheet.ExecuteNonQueryAsync();
        }

        await InsertPtcTimeAuditAsync(
            connection, transaction, "TIME_UNSUBMIT", actor, targetUserId,
            timesheetId, null, null, null, request.Reason.Trim(),
            new { status = previousStatus, parsedWeekStart },
            new { status = "draft", submittedAt = (DateTimeOffset?)null },
            new { userMustResubmit = true, submissionOnBehalf = false });
        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "returned_to_draft",
            timesheetId,
            previousStatus,
            newStatus = "draft",
            userMustResubmit = true,
            canSubmitOnBehalf = false
        });
    }

    private sealed record PtcEntrySnapshot(
        Guid TimeEntryId,
        Guid TimesheetId,
        Guid UserId,
        DateOnly WorkDate,
        decimal Hours,
        string Description,
        bool Billable,
        string Status,
        Guid ProjectId,
        Guid? TaskId);

    private static async Task<PtcEntrySnapshot?> LoadPtcEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timeEntryId,
        Guid targetUserId,
        bool forUpdate)
    {
        var sql = """
            SELECT time_entry_id, timesheet_id, user_id, work_date, hours,
                   COALESCE(description, ''), billable, status, project_id, task_id
            FROM time_entries
            WHERE time_entry_id=@time_entry_id AND user_id=@user_id
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("time_entry_id", timeEntryId);
        command.Parameters.AddWithValue("user_id", targetUserId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new PtcEntrySnapshot(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetFieldValue<DateOnly>(3), reader.GetDecimal(4), reader.GetString(5),
            reader.GetBoolean(6), reader.GetString(7), reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9));
    }

    private static bool PtcEntryIsEditable(PtcEntrySnapshot entry) =>
        entry.Status is "draft" or "manager_declined" or "pm_declined";

    private static async Task<IResult> PtcEditEntryAsync(
        Guid timeEntryId,
        PtcTimeEntryEditRequest request,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return ReasonRequired("correct the selected time entry");
        if (request.Hours.HasValue && (request.Hours <= 0 || request.Hours > 24))
            return Results.BadRequest(new { status = "invalid_hours", message = "Hours must be greater than 0 and no more than 24." });

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_CORRECT_ON_BEHALF", request.TargetUserId, null, true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        var original = await LoadPtcEntryAsync(connection, transaction, timeEntryId, request.TargetUserId, true);
        if (original is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "time_entry_not_found" });
        }
        if (!PtcEntryIsEditable(original))
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new { status = "unsubmit_required", message = "Return the selected user's week to draft before editing this entry." });
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE time_entries
            SET hours=COALESCE(@hours, hours),
                description=COALESCE(@description, description),
                billable=COALESCE(@billable, billable),
                status='draft',
                updated_at=NOW()
            WHERE time_entry_id=@time_entry_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("hours", (object?)request.Hours ?? DBNull.Value);
            update.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);
            update.Parameters.AddWithValue("billable", (object?)request.Billable ?? DBNull.Value);
            update.Parameters.AddWithValue("time_entry_id", timeEntryId);
            await update.ExecuteNonQueryAsync();
        }

        var revised = await LoadPtcEntryAsync(connection, transaction, timeEntryId, request.TargetUserId, false);
        await InsertPtcTimeAuditAsync(
            connection, transaction, "TIME_CORRECT_ON_BEHALF", actor, request.TargetUserId,
            original.TimesheetId, timeEntryId, original.ProjectId, original.TaskId,
            request.Reason.Trim(), original, revised ?? original,
            new { userMustResubmit = true, submissionOnBehalf = false });
        await transaction.CommitAsync();
        return Results.Ok(new { status = "updated", entry = revised, userMustResubmit = true });
    }

    private sealed record PtcAssignmentTarget(
        Guid AssignmentId,
        Guid ProjectId,
        Guid TaskId,
        bool Billable,
        string ProjectCode,
        string ProjectName,
        string TaskCode,
        string TaskName);

    private static async Task<PtcAssignmentTarget?> LoadPtcAssignmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid assignmentId,
        Guid targetUserId,
        DateOnly workDate)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pa.project_assignment_id, pa.project_id, pa.task_id,
                   pt.billable, p.project_code, p.project_name,
                   pt.task_code, pt.task_name
            FROM project_assignments pa
            JOIN projects p ON p.project_id=pa.project_id
            JOIN project_tasks pt ON pt.task_id=pa.task_id AND pt.project_id=pa.project_id
            WHERE pa.project_assignment_id=@assignment_id
              AND pa.user_id=@user_id
              AND pa.effective_start_date <= @work_date
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @work_date)
              AND pt.is_active=TRUE
              AND p.status IN ('active','on_hold');
            """, connection, transaction);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("user_id", targetUserId);
        command.Parameters.AddWithValue("work_date", workDate);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new PtcAssignmentTarget(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetBoolean(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7));
    }

    private static async Task<IResult> PtcMoveEntryAsync(
        Guid timeEntryId,
        PtcTimeEntryMoveRequest request,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return ReasonRequired("move the selected time entry to another task");
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;

        await using var transaction = await connection.BeginTransactionAsync();
        var original = await LoadPtcEntryAsync(connection, transaction, timeEntryId, request.TargetUserId, true);
        if (original is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "time_entry_not_found" });
        }
        if (!PtcEntryIsEditable(original))
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new { status = "unsubmit_required", message = "Return the selected user's week to draft before moving this entry." });
        }

        var target = await LoadPtcAssignmentAsync(
            connection, transaction, request.AssignmentId, request.TargetUserId, original.WorkDate);
        if (target is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "assignment_not_found", message = "The selected task is not actively assigned to this user." });
        }

        var access = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_REASSIGN", request.TargetUserId, target.ProjectId, true);
        if (access.Error is not null)
        {
            await transaction.RollbackAsync();
            return access.Error;
        }
        var actor = access.Actor!;

        await using (var update = new NpgsqlCommand("""
            UPDATE time_entries
            SET project_id=@project_id,
                task_id=@task_id,
                non_project_time_category_id=NULL,
                billable=@billable,
                status='draft',
                updated_at=NOW()
            WHERE time_entry_id=@time_entry_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("project_id", target.ProjectId);
            update.Parameters.AddWithValue("task_id", target.TaskId);
            update.Parameters.AddWithValue("billable", target.Billable);
            update.Parameters.AddWithValue("time_entry_id", timeEntryId);
            await update.ExecuteNonQueryAsync();
        }

        await using (var association = new NpgsqlCommand("""
            INSERT INTO module001_timesheet_entry_associations (
                time_entry_id, project_id, task_id, assignment_id,
                association_source, created_by_user_id, updated_by_user_id
            ) VALUES (
                @time_entry_id, @project_id, @task_id, @assignment_id,
                'PTC_TIME_STEWARD', @actor_user_id, @actor_user_id
            )
            ON CONFLICT (time_entry_id)
            DO UPDATE SET project_id=EXCLUDED.project_id,
                          task_id=EXCLUDED.task_id,
                          assignment_id=EXCLUDED.assignment_id,
                          non_project_time_category_id=NULL,
                          association_source='PTC_TIME_STEWARD',
                          updated_by_user_id=EXCLUDED.updated_by_user_id;
            """, connection, transaction))
        {
            association.Parameters.AddWithValue("time_entry_id", timeEntryId);
            association.Parameters.AddWithValue("project_id", target.ProjectId);
            association.Parameters.AddWithValue("task_id", target.TaskId);
            association.Parameters.AddWithValue("assignment_id", target.AssignmentId);
            association.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
            await association.ExecuteNonQueryAsync();
        }

        var revised = await LoadPtcEntryAsync(connection, transaction, timeEntryId, request.TargetUserId, false);
        await InsertPtcTimeAuditAsync(
            connection, transaction, "TIME_REASSIGN", actor, request.TargetUserId,
            original.TimesheetId, timeEntryId, target.ProjectId, target.TaskId,
            request.Reason.Trim(), original,
            new { revised, target.AssignmentId, target.ProjectCode, target.ProjectName, target.TaskCode, target.TaskName },
            new { userMustResubmit = true, submissionOnBehalf = false });
        await transaction.CommitAsync();
        return Results.Ok(new { status = "moved", entry = revised, assignment = target, userMustResubmit = true });
    }

    private static async Task<IResult> PtcRemoveEntryAsync(
        Guid timeEntryId,
        PtcTimeEntryRemoveRequest request,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return ReasonRequired("remove the selected draft entry");
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_DELETE_ON_BEHALF", request.TargetUserId, null, true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        var original = await LoadPtcEntryAsync(connection, transaction, timeEntryId, request.TargetUserId, true);
        if (original is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "time_entry_not_found" });
        }
        if (!PtcEntryIsEditable(original))
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new { status = "unsubmit_required", message = "Return the selected user's week to draft before removing this entry." });
        }

        await using (var timerGenerated = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM module001_timer_daily_segments
                WHERE resulting_timesheet_entry_id=@time_entry_id
            );
            """, connection, transaction))
        {
            timerGenerated.Parameters.AddWithValue("time_entry_id", timeEntryId);
            if (Convert.ToBoolean(await timerGenerated.ExecuteScalarAsync() ?? false))
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new
                {
                    status = "timer_audit_preservation_required",
                    message = "Timer-generated entries cannot be removed. Correct the hours or move the entry instead so raw timer evidence remains linked."
                });
            }
        }

        await InsertPtcTimeAuditAsync(
            connection, transaction, "TIME_DELETE_ON_BEHALF", actor, request.TargetUserId,
            original.TimesheetId, timeEntryId, original.ProjectId, original.TaskId,
            request.Reason.Trim(), original,
            new { removed = true, retainedInImmutableAudit = true },
            new { userMustResubmit = true, permanentAuditDeletion = false });

        await using (var delete = new NpgsqlCommand(
            "DELETE FROM time_entries WHERE time_entry_id=@time_entry_id;",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("time_entry_id", timeEntryId);
            await delete.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        return Results.Ok(new { status = "removed", timeEntryId, retainedInImmutableAudit = true, userMustResubmit = true });
    }

    private static async Task<IResult> PtcCreateTaskAsync(PtcTaskCreateRequest request, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return ReasonRequired("create and assign a replacement task");
        if (string.IsNullOrWhiteSpace(request.TaskCode) || string.IsNullOrWhiteSpace(request.TaskName))
            return Results.BadRequest(new { status = "task_details_required", message = "Task code and task name are required." });

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;
        var createAccess = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_TASK_CREATE", request.TargetUserId, request.ProjectId, true);
        if (createAccess.Error is not null) return createAccess.Error;
        var assignAccess = await RequirePtcTimeStewardAccessAsync(
            context, connection, "TIME_TASK_ASSIGN", request.TargetUserId, request.ProjectId, true);
        if (assignAccess.Error is not null) return assignAccess.Error;
        var actor = createAccess.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        if (!await ActiveUserExistsAsync(connection, transaction, request.TargetUserId))
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "user_not_found" });
        }

        string? projectStatus = null;
        await using (var project = new NpgsqlCommand(
            "SELECT status FROM projects WHERE project_id=@project_id FOR UPDATE;",
            connection,
            transaction))
        {
            project.Parameters.AddWithValue("project_id", request.ProjectId);
            projectStatus = Convert.ToString(await project.ExecuteScalarAsync());
        }
        if (projectStatus is not ("active" or "on_hold"))
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new { status = "project_not_active", message = "Replacement tasks can be created only for an active or on-hold project." });
        }

        Guid taskId;
        await using (var insertTask = new NpgsqlCommand("""
            INSERT INTO project_tasks (
                project_id, task_code, task_name, task_description,
                billable, is_active
            ) VALUES (
                @project_id, @task_code, @task_name, @task_description,
                @billable, TRUE
            )
            ON CONFLICT (project_id, task_code)
            DO UPDATE SET task_name=EXCLUDED.task_name,
                          task_description=EXCLUDED.task_description,
                          billable=EXCLUDED.billable,
                          is_active=TRUE,
                          updated_at=NOW()
            RETURNING task_id;
            """, connection, transaction))
        {
            insertTask.Parameters.AddWithValue("project_id", request.ProjectId);
            insertTask.Parameters.AddWithValue("task_code", request.TaskCode.Trim());
            insertTask.Parameters.AddWithValue("task_name", request.TaskName.Trim());
            insertTask.Parameters.AddWithValue("task_description", request.TaskDescription?.Trim() ?? string.Empty);
            insertTask.Parameters.AddWithValue("billable", request.Billable);
            taskId = (Guid)(await insertTask.ExecuteScalarAsync() ?? throw new InvalidOperationException("Task creation did not return an identifier."));
        }

        Guid assignmentId;
        await using (var findAssignment = new NpgsqlCommand("""
            SELECT project_assignment_id
            FROM project_assignments
            WHERE project_id=@project_id AND task_id=@task_id AND user_id=@user_id
              AND (effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE)
            ORDER BY effective_start_date DESC
            LIMIT 1;
            """, connection, transaction))
        {
            findAssignment.Parameters.AddWithValue("project_id", request.ProjectId);
            findAssignment.Parameters.AddWithValue("task_id", taskId);
            findAssignment.Parameters.AddWithValue("user_id", request.TargetUserId);
            var existing = await findAssignment.ExecuteScalarAsync();
            if (existing is Guid foundAssignmentId)
            {
                assignmentId = foundAssignmentId;
            }
            else
            {
                await using var insertAssignment = new NpgsqlCommand("""
                    INSERT INTO project_assignments (
                        project_id, task_id, user_id, assigned_by_user_id,
                        effective_start_date
                    ) VALUES (
                        @project_id, @task_id, @user_id, @actor_user_id,
                        CURRENT_DATE
                    )
                    RETURNING project_assignment_id;
                    """, connection, transaction);
                insertAssignment.Parameters.AddWithValue("project_id", request.ProjectId);
                insertAssignment.Parameters.AddWithValue("task_id", taskId);
                insertAssignment.Parameters.AddWithValue("user_id", request.TargetUserId);
                insertAssignment.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                assignmentId = (Guid)(await insertAssignment.ExecuteScalarAsync() ?? throw new InvalidOperationException("Task assignment did not return an identifier."));
            }
        }

        await InsertPtcTimeAuditAsync(
            connection, transaction, "TIME_TASK_CREATE", actor, request.TargetUserId,
            null, null, request.ProjectId, taskId, request.Reason.Trim(),
            new { taskExisted = false },
            new { taskId, assignmentId, request.ProjectId, taskCode = request.TaskCode.Trim(), taskName = request.TaskName.Trim(), request.Billable },
            new { taskAssignedToSelectedUser = true, submissionOnBehalf = false });
        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "task_ready",
            taskId,
            assignmentId,
            request.ProjectId,
            request.TargetUserId,
            message = "The replacement task is active and assigned to the selected user. It can now receive the time entry."
        });
    }
}
