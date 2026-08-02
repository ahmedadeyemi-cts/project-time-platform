using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Bounded recurring-report scheduler. A PostgreSQL advisory lock ensures one API
/// replica evaluates due schedules. Migration absence, database outages, owner
/// authorization changes, export failures, and Module 065 delivery locks fail closed.
/// </summary>
internal static class AnalyticsCenterScheduler
{
    private static int _started;

    internal static void Start(WebApplication application)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        application.Lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(() => RunLoopAsync(
                application.Services,
                application.Lifetime.ApplicationStopping));
        });
    }

    private static async Task RunLoopAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var initialDelay = Bounded(
            "PROJECTPULSE_ANALYTICS_SCHEDULER_INITIAL_DELAY_SECONDS",
            30,
            5,
            600);
        var interval = Bounded(
            "PROJECTPULSE_ANALYTICS_SCHEDULER_INTERVAL_SECONDS",
            300,
            30,
            3600);
        try { await Task.Delay(TimeSpan.FromSeconds(initialDelay), cancellationToken); }
        catch (OperationCanceledException) { return; }

        while (!cancellationToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(services, cancellationToken); }
            catch
            {
                // A failed scheduler cycle must not terminate the API. Immutable
                // per-recipient evidence and the next bounded cycle provide recovery.
            }
            try { await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal static async Task<object> RunOnceAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        await using var connection = await AnalyticsCenterScheduleRepository.OpenAsync(cancellationToken);
        if (!await AnalyticsCenterScheduleRepository.MigrationReadyAsync(connection, cancellationToken))
            return new { status = "migration_required", processed = 0 };
        if (!await AnalyticsCenterScheduleRepository.TryAcquireSchedulerLockAsync(connection, cancellationToken))
            return new { status = "another_replica_active", processed = 0 };

        try
        {
            var due = await AnalyticsCenterScheduleRepository.LoadDueSchedulesAsync(
                connection,
                DateTimeOffset.UtcNow,
                25,
                cancellationToken);
            var summaries = new List<AnalyticsScheduleExecutionSummary>();
            foreach (var schedule in due)
            {
                cancellationToken.ThrowIfCancellationRequested();
                summaries.Add(await AnalyticsCenterScheduleService.ExecuteAsync(
                    schedule,
                    services,
                    cancellationToken));
            }
            return new
            {
                status = "complete",
                processed = summaries.Count,
                complete = summaries.Count(summary => summary.Status == "complete"),
                partial = summaries.Count(summary => summary.Status == "partial"),
                queued = summaries.Count(summary => summary.Status == "queued"),
                failed = summaries.Count(summary => summary.Status == "failed"),
                summaries
            };
        }
        finally
        {
            try
            {
                await AnalyticsCenterScheduleRepository.ReleaseSchedulerLockAsync(
                    connection,
                    cancellationToken);
            }
            catch
            {
                // PostgreSQL automatically releases advisory locks when the
                // connection closes.
            }
        }
    }

    internal static DateTimeOffset CalculateNextRun(
        string cadence,
        int? dayOfWeek,
        int? dayOfMonth,
        int? monthOfYear,
        TimeOnly localTime,
        string timezoneName,
        DateTimeOffset fromUtc)
    {
        var timezone = ResolveTimezone(timezoneName);
        var localNow = TimeZoneInfo.ConvertTime(fromUtc, timezone);
        var normalized = AnalyticsCenterScheduleRepository.NormalizeCadence(cadence);
        DateTime localTarget;

        if (normalized == "daily")
        {
            localTarget = localNow.Date.Add(localTime.ToTimeSpan());
            if (localTarget <= localNow.DateTime) localTarget = localTarget.AddDays(1);
        }
        else if (normalized == "weekdays")
        {
            localTarget = localNow.Date.Add(localTime.ToTimeSpan());
            if (localTarget <= localNow.DateTime) localTarget = localTarget.AddDays(1);
            while (localTarget.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                localTarget = localTarget.AddDays(1);
        }
        else if (normalized == "weekly")
        {
            var targetDay = (DayOfWeek)Math.Clamp(dayOfWeek ?? 1, 0, 6);
            var ahead = ((int)targetDay - (int)localNow.DayOfWeek + 7) % 7;
            localTarget = localNow.Date.AddDays(ahead).Add(localTime.ToTimeSpan());
            if (localTarget <= localNow.DateTime) localTarget = localTarget.AddDays(7);
        }
        else if (normalized == "monthly")
        {
            localTarget = MonthlyTarget(localNow.DateTime, dayOfMonth ?? 1, localTime);
            if (localTarget <= localNow.DateTime)
                localTarget = MonthlyTarget(localNow.DateTime.AddMonths(1), dayOfMonth ?? 1, localTime);
        }
        else if (normalized == "quarterly")
        {
            var quarterMonth = ((localNow.Month - 1) / 3) * 3 + 1;
            localTarget = MonthlyTarget(
                new DateTime(localNow.Year, quarterMonth, 1),
                dayOfMonth ?? 1,
                localTime);
            while (localTarget <= localNow.DateTime)
                localTarget = MonthlyTarget(localTarget.AddMonths(3), dayOfMonth ?? 1, localTime);
        }
        else
        {
            var month = Math.Clamp(monthOfYear ?? 1, 1, 12);
            localTarget = MonthlyTarget(
                new DateTime(localNow.Year, month, 1),
                dayOfMonth ?? 1,
                localTime);
            if (localTarget <= localNow.DateTime)
                localTarget = MonthlyTarget(
                    new DateTime(localNow.Year + 1, month, 1),
                    dayOfMonth ?? 1,
                    localTime);
        }

        var unspecified = DateTime.SpecifyKind(localTarget, DateTimeKind.Unspecified);
        return new DateTimeOffset(
            unspecified,
            timezone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    private static DateTime MonthlyTarget(
        DateTime basis,
        int requestedDay,
        TimeOnly localTime)
    {
        var day = Math.Min(
            Math.Max(1, requestedDay),
            DateTime.DaysInMonth(basis.Year, basis.Month));
        return new DateTime(basis.Year, basis.Month, day)
            .Add(localTime.ToTimeSpan());
    }

    private static TimeZoneInfo ResolveTimezone(string timezoneName)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezoneName); }
        catch { return TimeZoneInfo.Utc; }
    }

    private static int Bounded(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
}
