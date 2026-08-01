using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 001 production multi-timer contract.
///
/// This additive surface keeps the historic single-timer routes available for
/// compatibility while providing one authoritative server contract for up to
/// five simultaneous assigned-task or non-project timers. Every start, stop,
/// automatic stop, and discard remains permission checked and audit recorded.
/// </summary>
public static partial class ScopedRolePolicyModule
{
    private const int Module001MultiTimerMaximumActive = 5;
    private const int Module001MultiTimerCapSeconds = 86_400;
    private const int Module001MultiTimerMaximumRoundedMinutes = 1_440;
    private const long Module001MultiTimerAdvisorySeed = 57_001;
    private const string Module001MultiTimerMigration =
        "057_module_001_multi_timer_document_grounded_ai";

    public sealed record Module001MultiTimerStartTarget(
        Guid? AssignmentId,
        Guid? NonProjectTimeCategoryId,
        string? NonProjectCategoryCode);

    public sealed record Module001MultiTimerStartRequest(
        IReadOnlyList<Module001MultiTimerStartTarget>? Targets,
        string? TimeClassification,
        string? Description,
        string? TimeZoneId);

    public sealed record Module001MultiTimerStopItem(
        Guid TimerSessionId,
        string? Description,
        int? ExpectedRowVersion);

    public sealed record Module001MultiTimerStopAllRequest(
        IReadOnlyList<Module001MultiTimerStopItem>? Timers,
        string? Reason);

    public static WebApplication MapModule001MultiTimerEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/timesheet/timers/active-set",
            (Func<HttpContext, Task<IResult>>)Module001ActiveTimerSetAsync);
        app.MapGet(
            "/api/timesheet/timers/history-v2",
            (Func<HttpContext, Task<IResult>>)Module001TimerHistoryV2Async);
        app.MapPost(
            "/api/timesheet/timers/start-batch",
            (Func<Module001MultiTimerStartRequest, HttpContext, Task<IResult>>)Module001StartTimerBatchAsync);
        app.MapPost(
            "/api/timesheet/timers/v2/{timerSessionId:guid}/stop",
            (Func<Guid, Module001TimerStopRequest, HttpContext, Task<IResult>>)Module001StopTimerV2Async);
        app.MapPost(
            "/api/timesheet/timers/v2/stop-all",
            (Func<Module001MultiTimerStopAllRequest, HttpContext, Task<IResult>>)Module001StopAllTimersAsync);
        app.MapPost(
            "/api/timesheet/timers/v2/{timerSessionId:guid}/discard",
            (Func<Guid, Module001TimerDiscardRequest, HttpContext, Task<IResult>>)Module001DiscardTimerV2Async);
        return app;
    }

    private static async Task<IResult?> RequireModule001MultiTimerReadyAsync(
        NpgsqlConnection connection)
    {
        var baseReadiness = await RequireModule001TablesAsync(connection);
        if (baseReadiness is not null) return baseReadiness;

        await using var command = new NpgsqlCommand("""
            SELECT
                EXISTS (
                    SELECT 1
                    FROM schema_migrations
                    WHERE migration_id = @migration_id
                ),
                EXISTS (
                    SELECT 1
                    FROM pg_trigger
                    WHERE tgrelid = to_regclass('public.module001_timer_sessions')
                      AND tgname = 'trg_module001_057_running_timer_limit'
                      AND tgenabled <> 'D'
                      AND NOT tgisinternal
                ),
                to_regclass('public.ux_module001_running_assignment') IS NOT NULL,
                to_regclass('public.ux_module001_running_non_project') IS NOT NULL;
            """, connection);
        command.Parameters.AddWithValue("migration_id", Module001MultiTimerMigration);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        var ready = reader.GetBoolean(0)
            && reader.GetBoolean(1)
            && reader.GetBoolean(2)
            && reader.GetBoolean(3);
        if (ready) return null;

        return Results.Json(new
        {
            status = "module001_multi_timer_migration_required",
            migration = Module001MultiTimerMigration,
            message = "Apply migration 057 before using simultaneous Timesheet timers."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task AcquireModule001MultiTimerUserLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(hashtextextended(@user_id, @seed));
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId.ToString("D"));
        command.Parameters.AddWithValue("seed", Module001MultiTimerAdvisorySeed);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<Module001TimerRow>> LoadModule001RunningTimersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
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
              AND t.timer_status = 'RUNNING'
            ORDER BY t.started_at_utc, t.timer_session_id
            """;
        if (forUpdate) sql += " FOR UPDATE OF t";
        sql += ";";

        var timers = new List<Module001TimerRow>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            timers.Add(new Module001TimerRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5),
                reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
                reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
                reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
                reader.IsDBNull(13) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(13),
                reader.IsDBNull(14) ? (int?)null : reader.GetInt32(14),
                reader.IsDBNull(15) ? (int?)null : reader.GetInt32(15),
                reader.GetString(16),
                reader.GetString(17),
                reader.GetBoolean(18),
                reader.IsDBNull(19) ? (Guid?)null : reader.GetGuid(19),
                reader.GetInt32(20),
                reader.GetString(21),
                reader.GetString(22),
                reader.GetString(23),
                reader.GetString(24),
                reader.GetString(25),
                reader.GetString(26),
                reader.GetString(27)));
        }
        return timers;
    }

    private static async Task<List<Module001TimerRow>> LoadModule001TimerHistoryRowsAsync(
        NpgsqlConnection connection,
        Guid userId,
        DateOnly weekStart)
    {
        const string sql = """
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
              AND t.week_start_date = @week_start
            ORDER BY t.started_at_utc DESC, t.timer_session_id DESC
            LIMIT 250;
            """;

        var timers = new List<Module001TimerRow>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("week_start", weekStart);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            timers.Add(new Module001TimerRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetFieldValue<DateOnly>(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5),
                reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
                reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
                reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
                reader.IsDBNull(13) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(13),
                reader.IsDBNull(14) ? (int?)null : reader.GetInt32(14),
                reader.IsDBNull(15) ? (int?)null : reader.GetInt32(15),
                reader.GetString(16),
                reader.GetString(17),
                reader.GetBoolean(18),
                reader.IsDBNull(19) ? (Guid?)null : reader.GetGuid(19),
                reader.GetInt32(20),
                reader.GetString(21),
                reader.GetString(22),
                reader.GetString(23),
                reader.GetString(24),
                reader.GetString(25),
                reader.GetString(26),
                reader.GetString(27)));
        }
        return timers;
    }

    private static int Module001MultiTimerRoundedMinutes(int elapsedSeconds)
    {
        if (elapsedSeconds <= 0) return 0;
        var capped = Math.Min(Module001MultiTimerCapSeconds, elapsedSeconds);
        var quarterUnits = (capped + Module001QuarterSeconds - 1) / Module001QuarterSeconds;
        return Math.Min(Module001MultiTimerMaximumRoundedMinutes, quarterUnits * 15);
    }

    private static object Module001MultiTimerResponse(
        Module001TimerRow timer,
        DateTimeOffset nowUtc)
    {
        var capAt = timer.StartedAtUtc.AddSeconds(Module001MultiTimerCapSeconds);
        var effectiveNow = nowUtc < capAt ? nowUtc : capAt;
        var liveSeconds = timer.ActualElapsedSeconds
            ?? Math.Clamp(
                (int)Math.Floor((effectiveNow - timer.StartedAtUtc).TotalSeconds),
                0,
                Module001MultiTimerCapSeconds);

        return new
        {
            timerSessionId = timer.TimerSessionId,
            userId = timer.UserId,
            weekStartDate = timer.WeekStartDate,
            entryDate = timer.EntryDate,
            customerId = timer.CustomerId,
            customerName = timer.CustomerName,
            projectId = timer.ProjectId,
            projectCode = timer.ProjectCode,
            projectName = timer.ProjectName,
            taskId = timer.TaskId,
            taskCode = timer.TaskCode,
            taskName = timer.TaskName,
            assignmentId = timer.AssignmentId,
            nonProjectCategoryId = timer.NonProjectCategoryId,
            nonProjectCategoryCode = timer.NonProjectCategoryCode,
            nonProjectCategoryName = timer.NonProjectCategoryName,
            timeClassification = timer.TimeClassification,
            timeZoneId = timer.TimeZoneId,
            startedAtUtc = timer.StartedAtUtc,
            stoppedAtUtc = timer.StoppedAtUtc,
            effectiveStoppedAtUtc = timer.EffectiveStoppedAtUtc,
            actualElapsedSeconds = timer.ActualElapsedSeconds,
            liveElapsedSeconds = liveSeconds,
            roundedMinutes = timer.RoundedMinutes,
            description = timer.Description,
            timerStatus = timer.TimerStatus,
            autoStopped = timer.AutoStopped,
            resultingTimesheetEntryId = timer.ResultingTimesheetEntryId,
            rowVersion = timer.RowVersion,
            maximumDurationSeconds = Module001MultiTimerCapSeconds,
            maximumConcurrentTimers = Module001MultiTimerMaximumActive,
            descriptionComplete = !string.IsNullOrWhiteSpace(timer.Description),
            expired = timer.TimerStatus == "RUNNING" && nowUtc >= capAt
        };
    }
}
