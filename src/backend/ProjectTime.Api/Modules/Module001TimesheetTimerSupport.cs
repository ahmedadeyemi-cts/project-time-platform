using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private const int Module001TimerCapSeconds = 12 * 60 * 60;
    private const int Module001QuarterSeconds = 15 * 60;

    private sealed record Module001Target(
        Guid? CustomerId,
        string CustomerName,
        Guid? ProjectId,
        string ProjectCode,
        string ProjectName,
        Guid? TaskId,
        string TaskCode,
        string TaskName,
        Guid? AssignmentId,
        Guid? NonProjectCategoryId,
        string NonProjectCategoryCode,
        string NonProjectCategoryName,
        bool Billable);

    private sealed record Module001TimerRow(
        Guid TimerSessionId,
        Guid UserId,
        DateOnly WeekStartDate,
        DateOnly EntryDate,
        Guid? CustomerId,
        Guid? ProjectId,
        Guid? TaskId,
        Guid? AssignmentId,
        Guid? NonProjectCategoryId,
        string TimeClassification,
        string TimeZoneId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? StoppedAtUtc,
        DateTimeOffset? EffectiveStoppedAtUtc,
        int? ActualElapsedSeconds,
        int? RoundedMinutes,
        string Description,
        string TimerStatus,
        bool AutoStopped,
        Guid? ResultingTimesheetEntryId,
        int RowVersion,
        string CustomerName,
        string ProjectCode,
        string ProjectName,
        string TaskCode,
        string TaskName,
        string NonProjectCategoryCode,
        string NonProjectCategoryName);

    private sealed record Module001TimerSegment(
        DateOnly LocalDate,
        int ActualSeconds,
        int RoundedMinutes);

    private sealed record Module001SubmissionValidation(
        bool Valid,
        Guid? TimesheetId,
        DateOnly WeekStart,
        DateOnly WeekEnd,
        decimal TotalHours,
        int EntryCount,
        List<string> Errors,
        List<object> IncompleteEntries,
        bool RunningTimer);

    private static async Task<IResult?> RequireModule001TablesAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.module001_timer_sessions') IS NOT NULL
               AND to_regclass('public.module001_weekly_task_lines') IS NOT NULL
               AND to_regclass('public.module001_timesheet_entry_associations') IS NOT NULL;
            """, connection);
        if (Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false)) return null;
        return Results.Json(new
        {
            status = "module001_migration_required",
            migration = "041_module_001_timesheet_timer_and_task_association",
            message = "Apply migration 041 before using the enhanced Timesheet timer and task APIs."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<(ActorContext? Actor, ScopedAuthorizationDecision? Decision, IResult? Error)>
        RequireModule001AccessAsync(
            HttpContext context,
            NpgsqlConnection connection,
            string actionCode,
            bool isWrite,
            Guid? projectId = null)
    {
        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return (null, null, SessionRequired());

        var decision = await ScopedAuthorizationEvaluator.EvaluateAsync(
            connection,
            actor,
            "001",
            actionCode,
            actor.EffectiveUserId,
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

    private static DateOnly Module001WeekStart(DateOnly date) =>
        date.AddDays(-(int)date.DayOfWeek);

    private static bool TryModule001WeekStart(string? value, out DateOnly weekStart)
    {
        if (!DateOnly.TryParse(value, out weekStart)) return false;
        return weekStart.DayOfWeek == DayOfWeek.Sunday;
    }

    private static TimeZoneInfo Module001TimeZone(string? timeZoneId)
    {
        var normalized = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        try { return TimeZoneInfo.FindSystemTimeZoneById(normalized); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static DateTimeOffset Module001UtcAtLocalMidnight(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local)) local = local.AddMinutes(30);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static List<Module001TimerSegment> Module001BuildSegments(
        DateTimeOffset startedAtUtc,
        DateTimeOffset stoppedAtUtc,
        TimeZoneInfo timeZone,
        int roundedMinutes)
    {
        var raw = new List<(DateOnly Date, int Seconds)>();
        var cursor = startedAtUtc;
        while (cursor < stoppedAtUtc)
        {
            var local = TimeZoneInfo.ConvertTime(cursor, timeZone);
            var date = DateOnly.FromDateTime(local.DateTime);
            var nextMidnightUtc = Module001UtcAtLocalMidnight(date.AddDays(1), timeZone);
            if (nextMidnightUtc <= cursor) nextMidnightUtc = cursor.AddHours(1);
            var segmentEnd = nextMidnightUtc < stoppedAtUtc ? nextMidnightUtc : stoppedAtUtc;
            var seconds = Math.Max(0, (int)Math.Floor((segmentEnd - cursor).TotalSeconds));
            var index = raw.FindIndex(item => item.Date == date);
            if (index >= 0) raw[index] = (date, raw[index].Seconds + seconds);
            else raw.Add((date, seconds));
            cursor = segmentEnd;
        }

        if (raw.Count == 0)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startedAtUtc, timeZone).DateTime);
            raw.Add((localDate, 0));
        }

        var totalSeconds = Math.Max(1, raw.Sum(item => item.Seconds));
        var totalQuarterUnits = roundedMinutes / 15;
        var allocations = raw.Select((item, index) =>
        {
            var exact = totalQuarterUnits * (double)item.Seconds / totalSeconds;
            var floor = (int)Math.Floor(exact);
            return new { index, item.Date, item.Seconds, floor, remainder = exact - floor };
        }).ToArray();
        var remaining = totalQuarterUnits - allocations.Sum(item => item.floor);
        var extraIndexes = allocations
            .OrderByDescending(item => item.remainder)
            .ThenBy(item => item.index)
            .Take(Math.Max(0, remaining))
            .Select(item => item.index)
            .ToHashSet();

        return allocations
            .Select(item => new Module001TimerSegment(
                item.Date,
                item.Seconds,
                (item.floor + (extraIndexes.Contains(item.index) ? 1 : 0)) * 15))
            .ToList();
    }

    private static int Module001RoundedMinutes(int elapsedSeconds)
    {
        if (elapsedSeconds <= 0) return 0;
        var capped = Math.Min(Module001TimerCapSeconds, elapsedSeconds);
        var quarterUnits = (capped + Module001QuarterSeconds - 1) / Module001QuarterSeconds;
        return Math.Min(720, quarterUnits * 15);
    }

    private static async Task<Module001Target?> LoadModule001AssignmentTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid assignmentId,
        Guid userId,
        DateOnly effectiveDate)
    {
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
                   COALESCE(pt.billable, p.billable, TRUE)
            FROM project_assignments pa
            JOIN projects p ON p.project_id = pa.project_id
            JOIN project_tasks pt ON pt.task_id = pa.task_id AND pt.project_id = pa.project_id
            LEFT JOIN clients c ON c.client_id = p.client_id
            WHERE pa.project_assignment_id = @assignment_id
              AND pa.user_id = @user_id
              AND pa.effective_start_date <= @effective_date
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @effective_date)
              AND p.status IN ('active','on_hold')
              AND pt.is_active = TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("effective_date", effectiveDate);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Module001Target(
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetGuid(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetGuid(0),
            null,
            string.Empty,
            string.Empty,
            reader.GetBoolean(9));
    }

    private static async Task<Module001Target?> LoadModule001NonProjectTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid categoryId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT non_project_time_category_id, category_code, category_name
            FROM non_project_time_categories
            WHERE non_project_time_category_id = @category_id
              AND is_active = TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("category_id", categoryId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Module001Target(
            null, string.Empty, null, string.Empty, string.Empty, null,
            string.Empty, string.Empty, null,
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), false);
    }

    private static async Task UpsertModule001WeeklyLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateOnly weekStart,
        Module001Target target,
        string source)
    {
        if (target.AssignmentId is not null)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO module001_weekly_task_lines (
                    user_id, week_start_date, customer_id, project_id, task_id,
                    assignment_id, activity_type, line_source,
                    created_by_user_id, updated_by_user_id
                ) VALUES (
                    @user_id, @week_start, @customer_id, @project_id, @task_id,
                    @assignment_id, 'PROJECT_TASK', @line_source,
                    @user_id, @user_id
                )
                ON CONFLICT (user_id, week_start_date, assignment_id)
                    WHERE assignment_id IS NOT NULL AND is_active = TRUE
                DO UPDATE SET is_active = TRUE, line_source = EXCLUDED.line_source,
                              updated_by_user_id = EXCLUDED.updated_by_user_id;
                """, connection, transaction);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("week_start", weekStart);
            command.Parameters.AddWithValue("customer_id", (object?)target.CustomerId ?? DBNull.Value);
            command.Parameters.AddWithValue("project_id", target.ProjectId!.Value);
            command.Parameters.AddWithValue("task_id", target.TaskId!.Value);
            command.Parameters.AddWithValue("assignment_id", target.AssignmentId.Value);
            command.Parameters.AddWithValue("line_source", source);
            await command.ExecuteNonQueryAsync();
            return;
        }

        await using var nonProject = new NpgsqlCommand("""
            INSERT INTO module001_weekly_task_lines (
                user_id, week_start_date, non_project_time_category_id,
                activity_type, line_source, created_by_user_id, updated_by_user_id
            ) VALUES (
                @user_id, @week_start, @category_id,
                'NON_PROJECT', @line_source, @user_id, @user_id
            )
            ON CONFLICT (user_id, week_start_date, non_project_time_category_id)
                WHERE non_project_time_category_id IS NOT NULL AND is_active = TRUE
            DO UPDATE SET is_active = TRUE, line_source = EXCLUDED.line_source,
                          updated_by_user_id = EXCLUDED.updated_by_user_id;
            """, connection, transaction);
        nonProject.Parameters.AddWithValue("user_id", userId);
        nonProject.Parameters.AddWithValue("week_start", weekStart);
        nonProject.Parameters.AddWithValue("category_id", target.NonProjectCategoryId!.Value);
        nonProject.Parameters.AddWithValue("line_source", source);
        await nonProject.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> Module001GetOrCreateTimesheetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateOnly weekStart)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO timesheets (user_id, week_start_date, week_end_date, status)
            VALUES (@user_id, @week_start, @week_end, 'draft')
            ON CONFLICT (user_id, week_start_date)
            DO UPDATE SET updated_at = NOW()
            RETURNING timesheet_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("week_start", weekStart);
        command.Parameters.AddWithValue("week_end", weekStart.AddDays(6));
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Unable to create or load the Timesheet week."));
    }

    private static async Task<Guid> Module001UpsertTimerEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActorContext actor,
        Guid timerSessionId,
        Module001Target target,
        string classification,
        DateOnly workDate,
        int roundedMinutes,
        string description)
    {
        var weekStart = Module001WeekStart(workDate);
        var timesheetId = await Module001GetOrCreateTimesheetAsync(
            connection, transaction, actor.EffectiveUserId, weekStart);

        await using (var editable = new NpgsqlCommand("""
            SELECT COALESCE((
                SELECT status FROM timesheet_day_statuses
                WHERE timesheet_id = @timesheet_id AND work_date = @work_date
            ), 'draft');
            """, connection, transaction))
        {
            editable.Parameters.AddWithValue("timesheet_id", timesheetId);
            editable.Parameters.AddWithValue("work_date", workDate);
            var status = Convert.ToString(await editable.ExecuteScalarAsync()) ?? "draft";
            if (status is not ("draft" or "manager_declined"))
            {
                throw new InvalidOperationException($"{workDate:yyyy-MM-dd} is {status} and cannot receive timer-generated draft time.");
            }
        }

        var additionalHours = roundedMinutes / 60m;
        await using (var limit = new NpgsqlCommand("""
            SELECT COALESCE(SUM(hours), 0)
            FROM time_entries
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction))
        {
            limit.Parameters.AddWithValue("timesheet_id", timesheetId);
            limit.Parameters.AddWithValue("work_date", workDate);
            var current = Convert.ToDecimal(await limit.ExecuteScalarAsync() ?? 0m);
            if (current + additionalHours > 24m)
            {
                throw new InvalidOperationException($"Timer conversion would exceed 24.00 hours on {workDate:yyyy-MM-dd}.");
            }
        }

        Guid? existingId;
        await using (var existing = new NpgsqlCommand("""
            SELECT time_entry_id
            FROM time_entries
            WHERE timesheet_id = @timesheet_id
              AND user_id = @user_id
              AND work_date = @work_date
              AND time_type = @time_type
              AND status IN ('draft','manager_declined')
              AND (
                    (@project_id IS NOT NULL AND project_id = @project_id AND task_id = @task_id)
                 OR (@project_id IS NULL AND non_project_time_category_id = @category_id)
              )
            ORDER BY created_at
            LIMIT 1
            FOR UPDATE;
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue("timesheet_id", timesheetId);
            existing.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            existing.Parameters.AddWithValue("work_date", workDate);
            existing.Parameters.AddWithValue("time_type", classification);
            existing.Parameters.AddWithValue("project_id", (object?)target.ProjectId ?? DBNull.Value);
            existing.Parameters.AddWithValue("task_id", (object?)target.TaskId ?? DBNull.Value);
            existing.Parameters.AddWithValue("category_id", (object?)target.NonProjectCategoryId ?? DBNull.Value);
            existingId = await existing.ExecuteScalarAsync() as Guid?;
        }

        Guid timeEntryId;
        if (existingId is Guid id)
        {
            await using var update = new NpgsqlCommand("""
                UPDATE time_entries
                SET hours = hours + @additional_hours,
                    description = CASE
                        WHEN NULLIF(BTRIM(@description), '') IS NULL THEN description
                        WHEN NULLIF(BTRIM(description), '') IS NULL THEN BTRIM(@description)
                        WHEN POSITION(BTRIM(@description) IN description) > 0 THEN description
                        ELSE description || E'\n' || BTRIM(@description)
                    END,
                    status = 'draft',
                    updated_at = NOW()
                WHERE time_entry_id = @time_entry_id;
                """, connection, transaction);
            update.Parameters.AddWithValue("additional_hours", additionalHours);
            update.Parameters.AddWithValue("description", description ?? string.Empty);
            update.Parameters.AddWithValue("time_entry_id", id);
            await update.ExecuteNonQueryAsync();
            timeEntryId = id;
        }
        else
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO time_entries (
                    timesheet_id, user_id, project_id, task_id,
                    non_project_time_category_id, work_date, time_type,
                    hours, description, billable, status
                ) VALUES (
                    @timesheet_id, @user_id, @project_id, @task_id,
                    @category_id, @work_date, @time_type,
                    @hours, NULLIF(BTRIM(@description), ''), @billable, 'draft'
                )
                RETURNING time_entry_id;
                """, connection, transaction);
            insert.Parameters.AddWithValue("timesheet_id", timesheetId);
            insert.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            insert.Parameters.AddWithValue("project_id", (object?)target.ProjectId ?? DBNull.Value);
            insert.Parameters.AddWithValue("task_id", (object?)target.TaskId ?? DBNull.Value);
            insert.Parameters.AddWithValue("category_id", (object?)target.NonProjectCategoryId ?? DBNull.Value);
            insert.Parameters.AddWithValue("work_date", workDate);
            insert.Parameters.AddWithValue("time_type", classification);
            insert.Parameters.AddWithValue("hours", additionalHours);
            insert.Parameters.AddWithValue("description", description ?? string.Empty);
            insert.Parameters.AddWithValue("billable", target.Billable);
            timeEntryId = (Guid)(await insert.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Unable to create the timer-generated Timesheet entry."));
        }

        await using (var association = new NpgsqlCommand("""
            INSERT INTO module001_timesheet_entry_associations (
                time_entry_id, customer_id, project_id, task_id, assignment_id,
                non_project_time_category_id, source_timer_session_id,
                association_source, created_by_user_id, updated_by_user_id
            ) VALUES (
                @time_entry_id, @customer_id, @project_id, @task_id, @assignment_id,
                @category_id, @timer_session_id,
                'TIMER', @user_id, @user_id
            )
            ON CONFLICT (time_entry_id)
            DO UPDATE SET customer_id = EXCLUDED.customer_id,
                          project_id = EXCLUDED.project_id,
                          task_id = EXCLUDED.task_id,
                          assignment_id = EXCLUDED.assignment_id,
                          non_project_time_category_id = EXCLUDED.non_project_time_category_id,
                          source_timer_session_id = EXCLUDED.source_timer_session_id,
                          association_source = 'TIMER',
                          updated_by_user_id = EXCLUDED.updated_by_user_id;
            """, connection, transaction))
        {
            association.Parameters.AddWithValue("time_entry_id", timeEntryId);
            association.Parameters.AddWithValue("customer_id", (object?)target.CustomerId ?? DBNull.Value);
            association.Parameters.AddWithValue("project_id", (object?)target.ProjectId ?? DBNull.Value);
            association.Parameters.AddWithValue("task_id", (object?)target.TaskId ?? DBNull.Value);
            association.Parameters.AddWithValue("assignment_id", (object?)target.AssignmentId ?? DBNull.Value);
            association.Parameters.AddWithValue("category_id", (object?)target.NonProjectCategoryId ?? DBNull.Value);
            association.Parameters.AddWithValue("timer_session_id", timerSessionId);
            association.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            await association.ExecuteNonQueryAsync();
        }

        return timeEntryId;
    }

    private static async Task InsertModule001TimerAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timerSessionId,
        Guid actorUserId,
        string eventCode,
        string reason,
        object previousState,
        object newState,
        object metadata)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module001_timer_audit_events (
                timer_session_id, actor_user_id, event_code, reason,
                previous_state, new_state, event_metadata
            ) VALUES (
                @timer_session_id, @actor_user_id, @event_code, @reason,
                @previous_state, @new_state, @event_metadata
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("timer_session_id", timerSessionId);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("event_code", eventCode);
        command.Parameters.AddWithValue("reason", reason ?? string.Empty);
        command.Parameters.Add("previous_state", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(previousState);
        command.Parameters.Add("new_state", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(newState);
        command.Parameters.Add("event_metadata", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(metadata);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertModule001PlatformAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        object oldValue,
        object newValue)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO audit_logs (
                actor_user_id, action, entity_type, entity_id,
                old_value, new_value, created_at
            ) VALUES (
                @actor_user_id, @action, @entity_type, @entity_id,
                @old_value, @new_value, NOW()
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", (object?)entityId ?? DBNull.Value);
        command.Parameters.Add("old_value", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(oldValue);
        command.Parameters.Add("new_value", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(newValue);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Module001TimerRow?> LoadModule001TimerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        Guid? timerSessionId,
        bool runningOnly,
        bool forUpdate)
    {
        var sql = """
            SELECT t.timer_session_id, t.user_id, t.week_start_date, t.entry_date,
                   t.customer_id, t.project_id, t.task_id, t.assignment_id,
                   t.non_project_time_category_id, t.time_classification,
                   t.time_zone_id, t.started_at_utc, t.stopped_at_utc,
                   t.effective_stopped_at_utc, t.actual_elapsed_seconds,
                   t.rounded_minutes, COALESCE(t.description, ''), t.timer_status,
                   t.auto_stopped, t.resulting_timesheet_entry_id, t.row_version,
                   COALESCE(c.client_name, ''), COALESCE(p.project_code, ''),
                   COALESCE(p.project_name, ''), COALESCE(pt.task_code, ''),
                   COALESCE(pt.task_name, ''), COALESCE(npc.category_code, ''),
                   COALESCE(npc.category_name, '')
            FROM module001_timer_sessions t
            LEFT JOIN clients c ON c.client_id = t.customer_id
            LEFT JOIN projects p ON p.project_id = t.project_id
            LEFT JOIN project_tasks pt ON pt.task_id = t.task_id
            LEFT JOIN non_project_time_categories npc
              ON npc.non_project_time_category_id = t.non_project_time_category_id
            WHERE t.user_id = @user_id
            """;
        if (timerSessionId is not null) sql += " AND t.timer_session_id = @timer_session_id";
        if (runningOnly) sql += " AND t.timer_status = 'RUNNING'";
        sql += " ORDER BY t.started_at_utc DESC LIMIT 1";
        if (forUpdate) sql += " FOR UPDATE";
        sql += ";";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        if (timerSessionId is not null) command.Parameters.AddWithValue("timer_session_id", timerSessionId.Value);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Module001TimerRow(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateOnly>(2), reader.GetFieldValue<DateOnly>(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8), reader.GetString(9), reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11), reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13), reader.IsDBNull(14) ? null : reader.GetInt32(14),
            reader.IsDBNull(15) ? null : reader.GetInt32(15), reader.GetString(16), reader.GetString(17), reader.GetBoolean(18),
            reader.IsDBNull(19) ? null : reader.GetGuid(19), reader.GetInt32(20), reader.GetString(21), reader.GetString(22),
            reader.GetString(23), reader.GetString(24), reader.GetString(25), reader.GetString(26), reader.GetString(27));
    }

    private static object Module001TimerResponse(Module001TimerRow timer, DateTimeOffset nowUtc)
    {
        var effectiveNow = nowUtc < timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds)
            ? nowUtc
            : timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds);
        var liveSeconds = timer.ActualElapsedSeconds
            ?? Math.Clamp((int)Math.Floor((effectiveNow - timer.StartedAtUtc).TotalSeconds), 0, Module001TimerCapSeconds);
        return new
        {
            timer.timerSessionId,
            timer.userId,
            timer.weekStartDate,
            timer.entryDate,
            timer.customerId,
            timer.customerName,
            timer.projectId,
            timer.projectCode,
            timer.projectName,
            timer.taskId,
            timer.taskCode,
            timer.taskName,
            timer.assignmentId,
            timer.nonProjectCategoryId,
            timer.nonProjectCategoryCode,
            timer.nonProjectCategoryName,
            timer.timeClassification,
            timer.timeZoneId,
            timer.startedAtUtc,
            timer.stoppedAtUtc,
            timer.effectiveStoppedAtUtc,
            actualElapsedSeconds = timer.ActualElapsedSeconds,
            liveElapsedSeconds = liveSeconds,
            timer.roundedMinutes,
            timer.description,
            timer.timerStatus,
            timer.autoStopped,
            timer.resultingTimesheetEntryId,
            timer.rowVersion,
            maximumDurationSeconds = Module001TimerCapSeconds,
            descriptionComplete = !string.IsNullOrWhiteSpace(timer.Description),
            expired = timer.TimerStatus == "RUNNING" && nowUtc >= timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds)
        };
    }

    private static async Task<Module001TimerRow> FinalizeModule001TimerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActorContext actor,
        Module001TimerRow timer,
        DateTimeOffset requestedStopUtc,
        string description,
        string reason)
    {
        var cap = timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds);
        var autoStopped = requestedStopUtc >= cap;
        var effectiveStop = requestedStopUtc < cap ? requestedStopUtc : cap;
        var actualSeconds = Math.Clamp(
            (int)Math.Floor((effectiveStop - timer.StartedAtUtc).TotalSeconds),
            0,
            Module001TimerCapSeconds);
        var roundedMinutes = Module001RoundedMinutes(actualSeconds);
        var timeZone = Module001TimeZone(timer.TimeZoneId);
        var segments = Module001BuildSegments(timer.StartedAtUtc, effectiveStop, timeZone, roundedMinutes);
        var target = timer.AssignmentId is Guid assignmentId
            ? await LoadModule001AssignmentTargetAsync(connection, transaction, assignmentId, actor.EffectiveUserId, timer.EntryDate)
            : timer.NonProjectCategoryId is Guid categoryId
                ? await LoadModule001NonProjectTargetAsync(connection, transaction, categoryId)
                : null;
        if (target is null) throw new InvalidOperationException("The timer task or non-project activity is no longer authorized.");

        Guid? firstEntryId = null;
        foreach (var segment in segments)
        {
            if (segment.RoundedMinutes <= 0) continue;
            var entryId = await Module001UpsertTimerEntryAsync(
                connection, transaction, actor, timer.TimerSessionId, target,
                timer.TimeClassification, segment.LocalDate, segment.RoundedMinutes,
                description);
            firstEntryId ??= entryId;

            await using var segmentCommand = new NpgsqlCommand("""
                INSERT INTO module001_timer_daily_segments (
                    timer_session_id, local_entry_date, actual_elapsed_seconds,
                    allocated_rounded_minutes, resulting_timesheet_entry_id
                ) VALUES (
                    @timer_session_id, @local_date, @actual_seconds,
                    @rounded_minutes, @time_entry_id
                )
                ON CONFLICT (timer_session_id, local_entry_date)
                DO UPDATE SET actual_elapsed_seconds = EXCLUDED.actual_elapsed_seconds,
                              allocated_rounded_minutes = EXCLUDED.allocated_rounded_minutes,
                              resulting_timesheet_entry_id = EXCLUDED.resulting_timesheet_entry_id;
                """, connection, transaction);
            segmentCommand.Parameters.AddWithValue("timer_session_id", timer.TimerSessionId);
            segmentCommand.Parameters.AddWithValue("local_date", segment.LocalDate);
            segmentCommand.Parameters.AddWithValue("actual_seconds", segment.ActualSeconds);
            segmentCommand.Parameters.AddWithValue("rounded_minutes", segment.RoundedMinutes);
            segmentCommand.Parameters.AddWithValue("time_entry_id", entryId);
            await segmentCommand.ExecuteNonQueryAsync();
        }

        await UpsertModule001WeeklyLineAsync(
            connection, transaction, actor.EffectiveUserId,
            timer.WeekStartDate, target, "TIMER");

        var finalStatus = autoStopped ? "AUTO_STOPPED" : "STOPPED_DRAFT";
        await using (var update = new NpgsqlCommand("""
            UPDATE module001_timer_sessions
            SET stopped_at_utc = @requested_stop,
                effective_stopped_at_utc = @effective_stop,
                actual_elapsed_seconds = @actual_seconds,
                rounded_minutes = @rounded_minutes,
                description = NULLIF(BTRIM(@description), ''),
                timer_status = @timer_status,
                auto_stopped = @auto_stopped,
                resulting_timesheet_entry_id = @time_entry_id,
                updated_by_user_id = @user_id
            WHERE timer_session_id = @timer_session_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("requested_stop", requestedStopUtc);
            update.Parameters.AddWithValue("effective_stop", effectiveStop);
            update.Parameters.AddWithValue("actual_seconds", actualSeconds);
            update.Parameters.AddWithValue("rounded_minutes", roundedMinutes);
            update.Parameters.AddWithValue("description", description ?? string.Empty);
            update.Parameters.AddWithValue("timer_status", finalStatus);
            update.Parameters.AddWithValue("auto_stopped", autoStopped);
            update.Parameters.AddWithValue("time_entry_id", (object?)firstEntryId ?? DBNull.Value);
            update.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            update.Parameters.AddWithValue("timer_session_id", timer.TimerSessionId);
            await update.ExecuteNonQueryAsync();
        }

        await InsertModule001TimerAuditAsync(
            connection, transaction, timer.TimerSessionId, actor.ActualUserId,
            autoStopped ? "TIMER_AUTO_STOPPED" : "TIMER_STOPPED", reason,
            new { timer.timerStatus, timer.startedAtUtc, timer.rowVersion },
            new { timerStatus = finalStatus, effectiveStop, actualSeconds, roundedMinutes, resultingTimesheetEntryId = firstEntryId },
            new { timer.timeZoneId, segments, descriptionComplete = !string.IsNullOrWhiteSpace(description) });

        return (await LoadModule001TimerAsync(
            connection, transaction, actor.EffectiveUserId,
            timer.TimerSessionId, false, false))!;
    }

    private static async Task<Module001TimerRow?> AutoStopModule001TimerAsync(
        NpgsqlConnection connection,
        ActorContext actor)
    {
        if (actor.IsViewAs) return await LoadModule001TimerAsync(
            connection, null, actor.EffectiveUserId, null, true, false);

        await using var transaction = await connection.BeginTransactionAsync();
        var timer = await LoadModule001TimerAsync(
            connection, transaction, actor.EffectiveUserId, null, true, true);
        if (timer is null)
        {
            await transaction.CommitAsync();
            return null;
        }
        if (DateTimeOffset.UtcNow < timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds))
        {
            await transaction.CommitAsync();
            return timer;
        }

        var finalized = await FinalizeModule001TimerAsync(
            connection, transaction, actor, timer,
            timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds),
            timer.Description,
            "Automatically stopped at the 12-hour maximum.");
        await transaction.CommitAsync();
        return finalized;
    }

    private static async Task<Module001SubmissionValidation> ValidateModule001WeekAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ActorContext actor,
        DateOnly weekStart)
    {
        var errors = new List<string>();
        var incomplete = new List<object>();
        Guid? timesheetId = null;
        await using (var timesheet = new NpgsqlCommand("""
            SELECT timesheet_id
            FROM timesheets
            WHERE user_id = @user_id AND week_start_date = @week_start;
            """, connection, transaction))
        {
            timesheet.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            timesheet.Parameters.AddWithValue("week_start", weekStart);
            timesheetId = await timesheet.ExecuteScalarAsync() as Guid?;
        }

        var runningTimer = false;
        await using (var running = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM module001_timer_sessions
                WHERE user_id = @user_id AND timer_status = 'RUNNING'
            );
            """, connection, transaction))
        {
            running.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            runningTimer = Convert.ToBoolean(await running.ExecuteScalarAsync() ?? false);
        }
        if (runningTimer) errors.Add("Stop or discard the active timer before submitting the Timesheet week.");
        if (timesheetId is null)
        {
            errors.Add("Save at least one Timesheet entry before submitting the week.");
            return new Module001SubmissionValidation(false, null, weekStart, weekStart.AddDays(6), 0m, 0, errors, incomplete, runningTimer);
        }

        var totalHours = 0m;
        var entryCount = 0;
        var dayTotals = new Dictionary<DateOnly, decimal>();
        await using (var entries = new NpgsqlCommand("""
            SELECT te.time_entry_id, te.work_date, te.hours, COALESCE(te.description, ''),
                   te.project_id, te.task_id, te.non_project_time_category_id,
                   COALESCE(p.project_code, ''), COALESCE(p.project_name, ''),
                   COALESCE(pt.task_name, ''),
                   EXISTS (
                       SELECT 1 FROM project_assignments pa
                       WHERE pa.user_id = @user_id
                         AND pa.project_id = te.project_id
                         AND pa.task_id = te.task_id
                         AND pa.effective_start_date <= te.work_date
                         AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= te.work_date)
                   ) AS assignment_valid
            FROM time_entries te
            LEFT JOIN projects p ON p.project_id = te.project_id
            LEFT JOIN project_tasks pt ON pt.task_id = te.task_id
            WHERE te.timesheet_id = @timesheet_id
              AND te.hours > 0
            ORDER BY te.work_date, p.project_code, pt.task_name;
            """, connection, transaction))
        {
            entries.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            entries.Parameters.AddWithValue("timesheet_id", timesheetId.Value);
            await using var reader = await entries.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entryCount++;
                var id = reader.GetGuid(0);
                var date = reader.GetFieldValue<DateOnly>(1);
                var hours = reader.GetDecimal(2);
                var description = reader.GetString(3);
                var projectId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4);
                var taskId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
                var categoryId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6);
                var projectCode = reader.GetString(7);
                var projectName = reader.GetString(8);
                var taskName = reader.GetString(9);
                var assignmentValid = reader.GetBoolean(10);
                totalHours += hours;
                dayTotals[date] = dayTotals.GetValueOrDefault(date) + hours;

                var reasons = new List<string>();
                if (string.IsNullOrWhiteSpace(description)) reasons.Add("meaningful work description is required");
                if (projectId is not null && (taskId is null || !assignmentValid)) reasons.Add("valid assigned project task is required");
                if (projectId is null && categoryId is null) reasons.Add("project task or authorized non-project activity is required");
                if (reasons.Count > 0)
                {
                    incomplete.Add(new
                    {
                        timeEntryId = id,
                        workDate = date,
                        projectCode,
                        projectName,
                        taskName,
                        hours,
                        reasons
                    });
                }
            }
        }

        if (entryCount == 0) errors.Add("The Timesheet week contains no positive-hour entries.");
        foreach (var day in dayTotals)
        {
            if (day.Value < 8m) errors.Add($"{day.Key:yyyy-MM-dd} has {day.Value:0.00} hours; the existing daily submission policy requires at least 8.00 hours.");
            if (day.Value > 24m) errors.Add($"{day.Key:yyyy-MM-dd} exceeds the 24.00-hour daily limit.");
        }
        if (incomplete.Count > 0) errors.Add($"{incomplete.Count} positive-hour entry or entries require description or task-association correction.");

        return new Module001SubmissionValidation(
            errors.Count == 0,
            timesheetId,
            weekStart,
            weekStart.AddDays(6),
            totalHours,
            entryCount,
            errors,
            incomplete,
            runningTimer);
    }
}
