using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    public static WebApplication MapModule001TimesheetEnhancementEndpoints(this WebApplication app)
    {
        app.MapGet("/api/timesheet/work-queue", Module001WorkQueueAsync);
        app.MapGet("/api/timesheet/weekly-lines", Module001WeeklyLinesAsync);
        app.MapPost("/api/timesheet/work-queue/{assignmentId:guid}/add", Module001AddWorkQueueTaskAsync);
        app.MapGet("/api/timesheet/timers/active", Module001ActiveTimerAsync);
        app.MapGet("/api/timesheet/timers/history", Module001TimerHistoryAsync);
        app.MapPost("/api/timesheet/timers/start", Module001StartTimerAsync);
        app.MapPost("/api/timesheet/timers/{timerSessionId:guid}/stop", Module001StopTimerAsync);
        app.MapPost("/api/timesheet/timers/{timerSessionId:guid}/discard", Module001DiscardTimerAsync);
        app.MapPost("/api/timesheet/entries/{timeEntryId:guid}/association", Module001ChangeEntryAssociationAsync);
        app.MapDelete("/api/timesheet/entries/{timeEntryId:guid}", Module001DeleteDraftEntryAsync);
        app.MapPost("/api/timesheet/weeks/{weekStart}/validate-submission", Module001ValidateWeekAsync);
        app.MapPost("/api/timesheet/weeks/{weekStart}/submit", Module001SubmitWeekAsync);
        return app;
    }

    private static DateOnly Module001RequestedWeek(HttpContext context)
    {
        var raw = context.Request.Query["weekStart"].FirstOrDefault();
        return TryModule001WeekStart(raw, out var parsed)
            ? parsed
            : Module001WeekStart(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private static async Task<IResult> Module001WorkQueueAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_VIEW", false);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;
        var weekStart = Module001RequestedWeek(context);
        var weekEnd = weekStart.AddDays(6);

        var tasks = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT pa.project_assignment_id,
                   c.client_id,
                   COALESCE(c.client_name, ''),
                   p.project_id,
                   p.project_code,
                   p.project_name,
                   pt.task_id,
                   pt.task_code,
                   pt.task_name,
                   COALESCE(pt.task_description, ''),
                   COALESCE(NULLIF(to_jsonb(pt)->>'work_type', ''), 'Project task'),
                   engineer.user_id,
                   engineer.display_name,
                   COALESCE(pm.display_name, pm.email, ''),
                   NULLIF(to_jsonb(pt)->>'due_date', ''),
                   COALESCE(NULLIF(to_jsonb(pt)->>'status', ''), CASE WHEN pt.is_active THEN 'active' ELSE 'inactive' END),
                   COALESCE(pa.assigned_hours, 0),
                   COALESCE(used.total_hours, 0),
                   COALESCE(week_used.week_hours, 0),
                   GREATEST(COALESCE(pa.assigned_hours, 0) - COALESCE(used.total_hours, 0), 0),
                   EXISTS (
                       SELECT 1
                       FROM module001_weekly_task_lines line
                       WHERE line.user_id = pa.user_id
                         AND line.week_start_date = @week_start
                         AND line.assignment_id = pa.project_assignment_id
                         AND line.is_active = TRUE
                   ) AS added_this_week
            FROM project_assignments pa
            JOIN app_users engineer ON engineer.user_id = pa.user_id
            JOIN projects p ON p.project_id = pa.project_id
            JOIN project_tasks pt
              ON pt.task_id = pa.task_id
             AND pt.project_id = pa.project_id
            LEFT JOIN clients c ON c.client_id = p.client_id
            LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
            LEFT JOIN LATERAL (
                SELECT SUM(te.hours) AS total_hours
                FROM time_entries te
                WHERE te.user_id = pa.user_id
                  AND te.project_id = pa.project_id
                  AND te.task_id = pa.task_id
                  AND te.status <> 'manager_declined'
            ) used ON TRUE
            LEFT JOIN LATERAL (
                SELECT SUM(te.hours) AS week_hours
                FROM time_entries te
                WHERE te.user_id = pa.user_id
                  AND te.project_id = pa.project_id
                  AND te.task_id = pa.task_id
                  AND te.work_date BETWEEN @week_start AND @week_end
            ) week_used ON TRUE
            WHERE pa.user_id = @user_id
              AND pa.effective_start_date <= @week_end
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start)
              AND p.status IN ('active','on_hold')
              AND pt.is_active = TRUE
            ORDER BY COALESCE(NULLIF(to_jsonb(pt)->>'due_date', ''), '9999-12-31'),
                     p.project_code,
                     pt.task_code;
            """, connection);
        command.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("week_start", weekStart);
        command.Parameters.AddWithValue("week_end", weekEnd);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tasks.Add(new
            {
                assignmentId = reader.GetGuid(0),
                customerId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                customerName = reader.GetString(2),
                projectId = reader.GetGuid(3),
                projectCode = reader.GetString(4),
                projectName = reader.GetString(5),
                taskId = reader.GetGuid(6),
                taskCode = reader.GetString(7),
                taskName = reader.GetString(8),
                taskDescription = reader.GetString(9),
                workType = reader.GetString(10),
                assignedEngineerId = reader.GetGuid(11),
                assignedEngineerName = reader.GetString(12),
                projectManagerName = reader.GetString(13),
                dueDate = reader.IsDBNull(14) ? null : reader.GetString(14),
                taskStatus = reader.GetString(15),
                assignedHours = reader.GetDecimal(16),
                usedHours = reader.GetDecimal(17),
                weekHours = reader.GetDecimal(18),
                remainingHours = reader.GetDecimal(19),
                addedThisWeek = reader.GetBoolean(20),
                openTaskHref = "#project-workspace"
            });
        }

        return Results.Ok(new
        {
            weekStart,
            weekEnd,
            count = tasks.Count,
            authoritativeSource = "project_assignments",
            deduplicationKey = "user_id + week_start_date + assignment_id",
            tasks
        });
    }

    private static async Task<IResult> Module001WeeklyLinesAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_VIEW", false);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;
        var weekStart = Module001RequestedWeek(context);

        var lines = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT line.weekly_task_line_id,
                   line.activity_type,
                   line.line_source,
                   line.assignment_id,
                   line.non_project_time_category_id,
                   COALESCE(c.client_name, ''),
                   COALESCE(p.project_code, ''),
                   COALESCE(p.project_name, ''),
                   COALESCE(pt.task_code, ''),
                   COALESCE(pt.task_name, ''),
                   COALESCE(npc.category_code, ''),
                   COALESCE(npc.category_name, '')
            FROM module001_weekly_task_lines line
            LEFT JOIN clients c ON c.client_id = line.customer_id
            LEFT JOIN projects p ON p.project_id = line.project_id
            LEFT JOIN project_tasks pt ON pt.task_id = line.task_id
            LEFT JOIN non_project_time_categories npc
              ON npc.non_project_time_category_id = line.non_project_time_category_id
            WHERE line.user_id = @user_id
              AND line.week_start_date = @week_start
              AND line.is_active = TRUE
            ORDER BY line.created_at;
            """, connection);
        command.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("week_start", weekStart);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(new
            {
                weeklyTaskLineId = reader.GetGuid(0),
                activityType = reader.GetString(1),
                lineSource = reader.GetString(2),
                assignmentId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
                nonProjectCategoryId = reader.IsDBNull(4) ? null : reader.GetGuid(4),
                customerName = reader.GetString(5),
                projectCode = reader.GetString(6),
                projectName = reader.GetString(7),
                taskCode = reader.GetString(8),
                taskName = reader.GetString(9),
                categoryCode = reader.GetString(10),
                categoryName = reader.GetString(11)
            });
        }

        return Results.Ok(new { weekStart, count = lines.Count, lines });
    }

    private static async Task<IResult> Module001AddWorkQueueTaskAsync(
        Guid assignmentId,
        Module001WeeklyTaskLineRequest request,
        HttpContext context)
    {
        if (request.WeekStart.DayOfWeek != DayOfWeek.Sunday)
        {
            return Results.BadRequest(new { status = "invalid_week_start", message = "WeekStart must be a Sunday." });
        }

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        var target = await LoadModule001AssignmentTargetAsync(
            connection,
            null,
            assignmentId,
            actor.EffectiveUserId,
            request.WeekStart);
        if (target is null)
        {
            return Results.NotFound(new
            {
                status = "assignment_not_found",
                message = "The task is not actively assigned to the authenticated user."
            });
        }

        var projectAccess = await RequireModule001AccessAsync(
            context,
            connection,
            "TIME_EDIT_OWN",
            true,
            target.ProjectId);
        if (projectAccess.Error is not null) return projectAccess.Error;

        await using var transaction = await connection.BeginTransactionAsync();
        await UpsertModule001WeeklyLineAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            request.WeekStart,
            target,
            "WORK_QUEUE");
        await InsertModule001PlatformAuditAsync(
            connection,
            transaction,
            actor.ActualUserId,
            "TASK_ASSOCIATED",
            "project_assignment",
            assignmentId,
            new { },
            new { request.WeekStart, target.ProjectId, target.TaskId, assignmentId });
        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "added",
            message = "Assigned task added to the Timesheet week.",
            request.WeekStart,
            assignmentId,
            target.ProjectId,
            target.TaskId
        });
    }

    private static async Task<IResult> Module001ActiveTimerAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_VIEW", false);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;
        var timer = await AutoStopModule001TimerAsync(connection, actor);
        var active = timer is not null && timer.TimerStatus == "RUNNING"
            ? Module001TimerResponse(timer, DateTimeOffset.UtcNow)
            : null;
        var autoStopped = timer is not null && timer.TimerStatus == "AUTO_STOPPED"
            ? Module001TimerResponse(timer, DateTimeOffset.UtcNow)
            : null;
        return Results.Ok(new { activeTimer = active, autoStoppedTimer = autoStopped });
    }

    private static async Task<IResult> Module001TimerHistoryAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_VIEW", false);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;
        await AutoStopModule001TimerAsync(connection, actor);
        var weekStart = Module001RequestedWeek(context);

        var ids = new List<Guid>();
        await using (var command = new NpgsqlCommand("""
            SELECT timer_session_id
            FROM module001_timer_sessions
            WHERE user_id = @user_id
              AND week_start_date = @week_start
            ORDER BY started_at_utc DESC
            LIMIT 100;
            """, connection))
        {
            command.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            command.Parameters.AddWithValue("week_start", weekStart);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        }

        var timers = new List<object>();
        foreach (var id in ids)
        {
            var timer = await LoadModule001TimerAsync(
                connection,
                null,
                actor.EffectiveUserId,
                id,
                false,
                false);
            if (timer is not null) timers.Add(Module001TimerResponse(timer, DateTimeOffset.UtcNow));
        }
        return Results.Ok(new { weekStart, count = timers.Count, timers });
    }

    private static async Task<IResult> Module001StartTimerAsync(
        Module001TimerStartRequest request,
        HttpContext context)
    {
        var hasAssignment = request.AssignmentId.HasValue;
        var hasCategory = request.NonProjectTimeCategoryId.HasValue;
        if (hasAssignment == hasCategory)
        {
            return Results.BadRequest(new
            {
                status = "timer_target_required",
                message = "Select exactly one assigned project task or authorized non-project activity."
            });
        }

        var classification = (request.TimeClassification ?? "normal").Trim().ToLowerInvariant();
        if (classification is not ("normal" or "afterhours"))
        {
            return Results.BadRequest(new
            {
                status = "invalid_time_classification",
                message = "TimeClassification must be normal or afterhours."
            });
        }

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;
        await AutoStopModule001TimerAsync(connection, actor);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var timeZone = Module001TimeZone(request.TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startedAtUtc, timeZone).DateTime);
        var weekStart = Module001WeekStart(localDate);
        var target = request.AssignmentId is Guid assignmentId
            ? await LoadModule001AssignmentTargetAsync(connection, null, assignmentId, actor.EffectiveUserId, localDate)
            : request.NonProjectTimeCategoryId is Guid categoryId
                ? await LoadModule001NonProjectTargetAsync(connection, null, categoryId)
                : null;
        if (target is null)
        {
            return Results.NotFound(new
            {
                status = "timer_target_not_authorized",
                message = "The selected task or non-project activity is unavailable to the authenticated user."
            });
        }

        var projectAccess = await RequireModule001AccessAsync(
            context,
            connection,
            "TIME_EDIT_OWN",
            true,
            target.ProjectId);
        if (projectAccess.Error is not null) return projectAccess.Error;

        await using var transaction = await connection.BeginTransactionAsync();
        var existing = await LoadModule001TimerAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            null,
            true,
            true);
        if (existing is not null)
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "timer_already_running",
                message = "Stop or discard the active timer before starting another.",
                activeTimer = Module001TimerResponse(existing, DateTimeOffset.UtcNow)
            });
        }

        await UpsertModule001WeeklyLineAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            weekStart,
            target,
            "TIMER");

        Guid timerSessionId;
        try
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO module001_timer_sessions (
                    user_id, week_start_date, entry_date, customer_id,
                    project_id, task_id, assignment_id,
                    non_project_time_category_id, time_classification,
                    time_zone_id, started_at_utc, description,
                    timer_status, created_by_user_id, updated_by_user_id
                ) VALUES (
                    @user_id, @week_start, @entry_date, @customer_id,
                    @project_id, @task_id, @assignment_id,
                    @category_id, @classification,
                    @time_zone_id, @started_at_utc, NULLIF(BTRIM(@description), ''),
                    'RUNNING', @user_id, @user_id
                )
                RETURNING timer_session_id;
                """, connection, transaction);
            insert.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            insert.Parameters.AddWithValue("week_start", weekStart);
            insert.Parameters.AddWithValue("entry_date", localDate);
            AddNullableGuid(insert, "customer_id", target.CustomerId);
            AddNullableGuid(insert, "project_id", target.ProjectId);
            AddNullableGuid(insert, "task_id", target.TaskId);
            AddNullableGuid(insert, "assignment_id", target.AssignmentId);
            AddNullableGuid(insert, "category_id", target.NonProjectCategoryId);
            insert.Parameters.AddWithValue("classification", classification);
            insert.Parameters.AddWithValue("time_zone_id", timeZone.Id);
            insert.Parameters.AddWithValue("started_at_utc", startedAtUtc);
            insert.Parameters.AddWithValue("description", request.Description ?? string.Empty);
            timerSessionId = (Guid)(await insert.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Unable to start the timer."));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "timer_already_running",
                message = "Only one running timer is permitted per authenticated user."
            });
        }

        await InsertModule001TimerAuditAsync(
            connection,
            transaction,
            timerSessionId,
            actor.ActualUserId,
            "TIMER_STARTED",
            string.Empty,
            new { },
            new
            {
                timerSessionId,
                startedAtUtc,
                weekStart,
                entryDate = localDate,
                target.ProjectId,
                target.TaskId,
                target.AssignmentId,
                target.NonProjectCategoryId,
                classification
            },
            new { timeZoneId = timeZone.Id, descriptionComplete = !string.IsNullOrWhiteSpace(request.Description) });
        await transaction.CommitAsync();

        var timer = await LoadModule001TimerAsync(
            connection,
            null,
            actor.EffectiveUserId,
            timerSessionId,
            false,
            false);
        return Results.Json(
            new { status = "running", timer = Module001TimerResponse(timer!, DateTimeOffset.UtcNow) },
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Module001StopTimerAsync(
        Guid timerSessionId,
        Module001TimerStopRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        var timer = await LoadModule001TimerAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            timerSessionId,
            true,
            true);
        if (timer is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new
            {
                status = "running_timer_not_found",
                message = "No running timer with that identifier belongs to the authenticated user."
            });
        }
        if (request.ExpectedRowVersion.HasValue && request.ExpectedRowVersion.Value != timer.RowVersion)
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "timer_version_conflict",
                message = "The timer changed on another device. Refresh before stopping it."
            });
        }

        var description = request.Description ?? timer.Description;
        Module001TimerRow finalized;
        try
        {
            finalized = await FinalizeModule001TimerAsync(
                connection,
                transaction,
                actor,
                timer,
                DateTimeOffset.UtcNow,
                description,
                request.Reason ?? "Timer stopped by the authenticated user.");
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync();
            return Results.BadRequest(new { status = "timer_conversion_blocked", message = exception.Message });
        }

        await transaction.CommitAsync();
        return Results.Ok(new
        {
            status = finalized.TimerStatus,
            message = string.IsNullOrWhiteSpace(finalized.Description)
                ? "Timer stopped and draft time recorded. Add a description before submission."
                : "Timer stopped and draft time recorded.",
            refreshTimesheet = true,
            timer = Module001TimerResponse(finalized, DateTimeOffset.UtcNow)
        });
    }

    private static async Task<IResult> Module001DiscardTimerAsync(
        Guid timerSessionId,
        Module001TimerDiscardRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        var timer = await LoadModule001TimerAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            timerSessionId,
            true,
            true);
        if (timer is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "running_timer_not_found" });
        }
        if (request.ExpectedRowVersion.HasValue && request.ExpectedRowVersion.Value != timer.RowVersion)
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new { status = "timer_version_conflict" });
        }

        var discardedAt = DateTimeOffset.UtcNow;
        await using (var update = new NpgsqlCommand("""
            UPDATE module001_timer_sessions
            SET stopped_at_utc = @discarded_at,
                effective_stopped_at_utc = LEAST(@discarded_at, started_at_utc + INTERVAL '12 hours'),
                timer_status = 'DISCARDED',
                updated_by_user_id = @user_id
            WHERE timer_session_id = @timer_session_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("discarded_at", discardedAt);
            update.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            update.Parameters.AddWithValue("timer_session_id", timerSessionId);
            await update.ExecuteNonQueryAsync();
        }

        await InsertModule001TimerAuditAsync(
            connection,
            transaction,
            timerSessionId,
            actor.ActualUserId,
            "TIMER_DISCARDED",
            request.Reason ?? "Timer discarded after user confirmation.",
            new { timerStatus = timer.TimerStatus, timer.StartedAtUtc, timer.RowVersion },
            new { timerStatus = "DISCARDED", discardedAt },
            new { noTimesheetEntryCreated = true });
        await transaction.CommitAsync();
        return Results.Ok(new { status = "DISCARDED", message = "Timer discarded. No Timesheet time was created." });
    }

    private static async Task<IResult> Module001ChangeEntryAssociationAsync(
        Guid timeEntryId,
        Module001EntryAssociationRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        DateOnly workDate;
        Guid? oldProjectId;
        Guid? oldTaskId;
        await using (var entry = new NpgsqlCommand("""
            SELECT work_date, project_id, task_id
            FROM time_entries
            WHERE time_entry_id = @time_entry_id
              AND user_id = @user_id
              AND status IN ('draft','manager_declined')
            FOR UPDATE;
            """, connection, transaction))
        {
            entry.Parameters.AddWithValue("time_entry_id", timeEntryId);
            entry.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            await using var reader = await entry.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                await transaction.RollbackAsync();
                return Results.NotFound(new
                {
                    status = "editable_entry_not_found",
                    message = "Only the authenticated user's unsubmitted draft entry can change task association."
                });
            }
            workDate = reader.GetFieldValue<DateOnly>(0);
            oldProjectId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            oldTaskId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        }

        var target = await LoadModule001AssignmentTargetAsync(
            connection,
            transaction,
            request.AssignmentId,
            actor.EffectiveUserId,
            workDate);
        if (target is null)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "assignment_not_found" });
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE time_entries
            SET project_id = @project_id,
                task_id = @task_id,
                non_project_time_category_id = NULL,
                billable = @billable,
                updated_at = NOW()
            WHERE time_entry_id = @time_entry_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("project_id", target.ProjectId!.Value);
            update.Parameters.AddWithValue("task_id", target.TaskId!.Value);
            update.Parameters.AddWithValue("billable", target.Billable);
            update.Parameters.AddWithValue("time_entry_id", timeEntryId);
            await update.ExecuteNonQueryAsync();
        }

        await using (var association = new NpgsqlCommand("""
            INSERT INTO module001_timesheet_entry_associations (
                time_entry_id, customer_id, project_id, task_id, assignment_id,
                association_source, created_by_user_id, updated_by_user_id
            ) VALUES (
                @time_entry_id, @customer_id, @project_id, @task_id, @assignment_id,
                'CALENDAR', @user_id, @user_id
            )
            ON CONFLICT (time_entry_id)
            DO UPDATE SET customer_id = EXCLUDED.customer_id,
                          project_id = EXCLUDED.project_id,
                          task_id = EXCLUDED.task_id,
                          assignment_id = EXCLUDED.assignment_id,
                          non_project_time_category_id = NULL,
                          association_source = 'CALENDAR',
                          updated_by_user_id = EXCLUDED.updated_by_user_id;
            """, connection, transaction))
        {
            association.Parameters.AddWithValue("time_entry_id", timeEntryId);
            AddNullableGuid(association, "customer_id", target.CustomerId);
            association.Parameters.AddWithValue("project_id", target.ProjectId!.Value);
            association.Parameters.AddWithValue("task_id", target.TaskId!.Value);
            association.Parameters.AddWithValue("assignment_id", target.AssignmentId!.Value);
            association.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            await association.ExecuteNonQueryAsync();
        }

        await InsertModule001PlatformAuditAsync(
            connection,
            transaction,
            actor.ActualUserId,
            "TASK_CHANGED",
            "time_entry",
            timeEntryId,
            new { oldProjectId, oldTaskId },
            new
            {
                target.ProjectId,
                target.TaskId,
                target.AssignmentId,
                reason = request.Reason ?? string.Empty
            });
        await transaction.CommitAsync();
        return Results.Ok(new { status = "updated", refreshTimesheet = true, timeEntryId });
    }

    private static async Task<IResult> Module001DeleteDraftEntryAsync(Guid timeEntryId, HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        await using (var timerGenerated = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM module001_timer_daily_segments
                WHERE resulting_timesheet_entry_id = @time_entry_id
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
                    message = "Timer-generated entries cannot be permanently removed because raw-duration audit evidence must be preserved."
                });
            }
        }

        await using var delete = new NpgsqlCommand("""
            DELETE FROM time_entries
            WHERE time_entry_id = @time_entry_id
              AND user_id = @user_id
              AND status IN ('draft','manager_declined')
            RETURNING time_entry_id;
            """, connection, transaction);
        delete.Parameters.AddWithValue("time_entry_id", timeEntryId);
        delete.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
        if (await delete.ExecuteScalarAsync() is not Guid)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new { status = "editable_entry_not_found" });
        }

        await InsertModule001PlatformAuditAsync(
            connection,
            transaction,
            actor.ActualUserId,
            "DRAFT_ENTRY_REMOVED",
            "time_entry",
            timeEntryId,
            new { status = "draft" },
            new { removed = true });
        await transaction.CommitAsync();
        return Results.Ok(new { status = "removed", refreshTimesheet = true, timeEntryId });
    }

    private static async Task<IResult> Module001ValidateWeekAsync(string weekStart, HttpContext context)
    {
        if (!TryModule001WeekStart(weekStart, out var parsedWeekStart))
        {
            return Results.BadRequest(new { status = "invalid_week_start", message = "WeekStart must be a Sunday in YYYY-MM-DD format." });
        }

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_SUBMIT", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;
        await AutoStopModule001TimerAsync(connection, actor);

        var validation = await ValidateModule001WeekAsync(connection, null, actor, parsedWeekStart);
        if (!validation.Valid)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await InsertModule001PlatformAuditAsync(
                connection,
                transaction,
                actor.ActualUserId,
                "SUBMISSION_VALIDATION_FAILED",
                "timesheet",
                validation.TimesheetId,
                new { parsedWeekStart },
                new { validation.Errors, validation.IncompleteEntries, validation.RunningTimer });
            await transaction.CommitAsync();
        }
        return Results.Ok(validation);
    }

    private static async Task<IResult> Module001SubmitWeekAsync(
        string weekStart,
        Module001WeekSubmissionRequest request,
        HttpContext context)
    {
        if (!TryModule001WeekStart(weekStart, out var parsedWeekStart))
        {
            return Results.BadRequest(new { status = "invalid_week_start", message = "WeekStart must be a Sunday in YYYY-MM-DD format." });
        }
        if (!request.Confirmed)
        {
            return Results.BadRequest(new
            {
                status = "explicit_confirmation_required",
                message = "Review the Timesheet summary and confirm submission."
            });
        }

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001TablesAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_SUBMIT", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;
        await AutoStopModule001TimerAsync(connection, actor);

        await using var transaction = await connection.BeginTransactionAsync();
        var validation = await ValidateModule001WeekAsync(connection, transaction, actor, parsedWeekStart);
        if (!validation.Valid)
        {
            await InsertModule001PlatformAuditAsync(
                connection,
                transaction,
                actor.ActualUserId,
                "SUBMISSION_VALIDATION_FAILED",
                "timesheet",
                validation.TimesheetId,
                new { parsedWeekStart },
                new { validation.Errors, validation.IncompleteEntries, validation.RunningTimer });
            await transaction.CommitAsync();
            return Results.BadRequest(validation);
        }

        await SubmitModule001WeekAsync(
            connection,
            transaction,
            actor,
            validation,
            request.Reason ?? "Submitted from Module 001 Timesheet.");
        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "submitted",
            message = "Timesheet week submitted to Module 002 Approval Inbox.",
            validation.WeekStart,
            validation.WeekEnd,
            validation.TotalHours,
            validation.EntryCount,
            module002Handoff = true
        });
    }
}
