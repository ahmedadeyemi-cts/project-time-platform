using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private const int Module001TimerCapSeconds = 43_200;
    private const int Module001QuarterSeconds = 900;

    public sealed record Module001TimerStartRequest(
        Guid? AssignmentId,
        Guid? NonProjectTimeCategoryId,
        string? TimeClassification,
        string? Description,
        string? TimeZoneId);

    public sealed record Module001TimerStopRequest(
        string? Description,
        string? Reason,
        int? ExpectedRowVersion);

    public sealed record Module001TimerDiscardRequest(
        string? Reason,
        int? ExpectedRowVersion);

    public sealed record Module001WeeklyTaskLineRequest(DateOnly WeekStart);

    public sealed record Module001EntryAssociationRequest(
        Guid AssignmentId,
        string? Reason);

    public sealed record Module001WeekSubmissionRequest(
        bool Confirmed,
        string? Reason);

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
               AND to_regclass('public.module001_timesheet_entry_associations') IS NOT NULL
               AND to_regclass('public.module001_timer_audit_events') IS NOT NULL;
            """, connection);
        if (Convert.ToBoolean(await command.ExecuteScalarAsync() ?? false)) return null;

        return Results.Json(new
        {
            status = "module001_migration_required",
            migration = "041_module_001_timesheet_timer_and_task_association",
            message = "Apply migration 041 before using the enhanced Timesheet APIs."
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
        var candidate = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static DateTimeOffset Module001UtcAtLocalMidnight(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(local)) local = local.AddMinutes(30);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }

    private static int Module001RoundedMinutes(int elapsedSeconds)
    {
        if (elapsedSeconds <= 0) return 0;
        var capped = Math.Min(Module001TimerCapSeconds, elapsedSeconds);
        var quarterUnits = (capped + Module001QuarterSeconds - 1) / Module001QuarterSeconds;
        return Math.Min(720, quarterUnits * 15);
    }

    private static List<Module001TimerSegment> Module001BuildSegments(
        DateTimeOffset startedAtUtc,
        DateTimeOffset stoppedAtUtc,
        TimeZoneInfo timeZone,
        int roundedMinutes)
    {
        var rawSegments = new List<(DateOnly Date, int Seconds)>();
        var cursor = startedAtUtc;

        while (cursor < stoppedAtUtc)
        {
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(cursor, timeZone).DateTime);
            var nextMidnightUtc = Module001UtcAtLocalMidnight(localDate.AddDays(1), timeZone);
            if (nextMidnightUtc <= cursor) nextMidnightUtc = cursor.AddHours(1);
            var segmentEnd = nextMidnightUtc < stoppedAtUtc ? nextMidnightUtc : stoppedAtUtc;
            var seconds = Math.Max(0, (int)Math.Floor((segmentEnd - cursor).TotalSeconds));

            var existingIndex = rawSegments.FindIndex(item => item.Date == localDate);
            if (existingIndex >= 0)
            {
                rawSegments[existingIndex] = (localDate, rawSegments[existingIndex].Seconds + seconds);
            }
            else
            {
                rawSegments.Add((localDate, seconds));
            }
            cursor = segmentEnd;
        }

        if (rawSegments.Count == 0)
        {
            rawSegments.Add((
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startedAtUtc, timeZone).DateTime),
                0));
        }

        var totalSeconds = Math.Max(1, rawSegments.Sum(item => item.Seconds));
        var totalUnits = roundedMinutes / 15;
        var allocations = rawSegments.Select((item, index) =>
        {
            var exactUnits = totalUnits * (double)item.Seconds / totalSeconds;
            var baseUnits = (int)Math.Floor(exactUnits);
            return new
            {
                Index = index,
                item.Date,
                item.Seconds,
                BaseUnits = baseUnits,
                Remainder = exactUnits - baseUnits
            };
        }).ToArray();

        var remainingUnits = totalUnits - allocations.Sum(item => item.BaseUnits);
        var extraUnitIndexes = allocations
            .OrderByDescending(item => item.Remainder)
            .ThenBy(item => item.Index)
            .Take(Math.Max(0, remainingUnits))
            .Select(item => item.Index)
            .ToHashSet();

        return allocations.Select(item => new Module001TimerSegment(
            item.Date,
            item.Seconds,
            (item.BaseUnits + (extraUnitIndexes.Contains(item.Index) ? 1 : 0)) * 15)).ToList();
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static object Module001TimerResponse(Module001TimerRow timer, DateTimeOffset nowUtc)
    {
        var capAt = timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds);
        var effectiveNow = nowUtc < capAt ? nowUtc : capAt;
        var liveSeconds = timer.ActualElapsedSeconds
            ?? Math.Clamp(
                (int)Math.Floor((effectiveNow - timer.StartedAtUtc).TotalSeconds),
                0,
                Module001TimerCapSeconds);

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
            maximumDurationSeconds = Module001TimerCapSeconds,
            descriptionComplete = !string.IsNullOrWhiteSpace(timer.Description),
            expired = timer.TimerStatus == "RUNNING" && nowUtc >= capAt
        };
    }
}
