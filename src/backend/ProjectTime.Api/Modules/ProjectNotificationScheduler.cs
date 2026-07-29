using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Bounded in-process scheduler for Group 4. A PostgreSQL advisory lock ensures
/// that only one API replica evaluates due schedules at a time. Migration absence,
/// database outages, and Module 065 delivery locks are all fail-closed.
/// </summary>
internal static class ProjectNotificationScheduler
{
    private static int _started;

    internal static void Start(WebApplication application)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;

        application.Lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(() => RunLoopAsync(application.Lifetime.ApplicationStopping));
        });
    }

    private static async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var initialDelaySeconds = ReadBoundedInt(
            "PROJECTPULSE_NOTIFICATION_SCHEDULER_INITIAL_DELAY_SECONDS",
            20,
            5,
            600);
        var intervalSeconds = ReadBoundedInt(
            "PROJECTPULSE_NOTIFICATION_SCHEDULER_INTERVAL_SECONDS",
            300,
            30,
            3600);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(initialDelaySeconds), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(cancellationToken);
            }
            catch
            {
                // Scheduler exceptions must not terminate the API process. Source-
                // specific diagnostics remain available from Module 032 and the
                // next bounded interval retries safely.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal static async Task<ProjectNotificationProcessingService.ScheduleRunSummary?> RunOnceAsync(
        CancellationToken cancellationToken)
    {
        var connectionString = ProjectNotificationRepository.ConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await ProjectNotificationRepository.MigrationReadyAsync(
                connection,
                cancellationToken))
        {
            return null;
        }

        var acquired = await ProjectNotificationRepository.TryAcquireSchedulerLockAsync(
            connection,
            cancellationToken);
        if (!acquired) return null;

        try
        {
            return await ProjectNotificationProcessingService.RunDueSchedulesAsync(
                connection,
                null,
                null,
                cancellationToken);
        }
        finally
        {
            try
            {
                await ProjectNotificationRepository.ReleaseSchedulerLockAsync(
                    connection,
                    cancellationToken);
            }
            catch
            {
                // PostgreSQL releases advisory locks automatically when the
                // connection closes; an explicit-unlock failure is non-fatal.
            }
        }
    }

    internal static DateTimeOffset CalculateNextRun(
        string scheduleType,
        int? dayOfWeek,
        TimeOnly localTime,
        string timezoneName,
        int? daysBeforeMonthEnd,
        DateTimeOffset fromUtc)
    {
        var timezone = ResolveTimezone(timezoneName);
        var localNow = TimeZoneInfo.ConvertTime(fromUtc, timezone);
        DateTime localTarget;

        if (scheduleType == "month_end_reminder")
        {
            localTarget = MonthEndTarget(
                localNow.DateTime,
                localTime,
                daysBeforeMonthEnd ?? 0);
            if (localTarget <= localNow.DateTime)
            {
                var nextMonth = localNow.DateTime.AddMonths(1);
                localTarget = MonthEndTarget(
                    nextMonth,
                    localTime,
                    daysBeforeMonthEnd ?? 0);
            }
        }
        else if (scheduleType == "escalation")
        {
            localTarget = localNow.Date.AddDays(1).Add(localTime.ToTimeSpan());
        }
        else
        {
            var targetDay = (DayOfWeek)Math.Clamp(dayOfWeek ?? 1, 0, 6);
            var daysAhead = ((int)targetDay - (int)localNow.DayOfWeek + 7) % 7;
            localTarget = localNow.Date
                .AddDays(daysAhead)
                .Add(localTime.ToTimeSpan());
            if (localTarget <= localNow.DateTime)
                localTarget = localTarget.AddDays(7);
        }

        var unspecified = DateTime.SpecifyKind(localTarget, DateTimeKind.Unspecified);
        return new DateTimeOffset(
            unspecified,
            timezone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    internal static bool IsQuietHours(
        ProjectNotificationSchedule schedule,
        DateTimeOffset utcNow)
    {
        if (!schedule.QuietHoursStart.HasValue || !schedule.QuietHoursEnd.HasValue)
            return false;

        var timezone = ResolveTimezone(schedule.TimezoneName);
        var localTime = TimeOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(utcNow, timezone).DateTime);
        var start = schedule.QuietHoursStart.Value;
        var end = schedule.QuietHoursEnd.Value;

        return start <= end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }

    internal static DateTimeOffset EndOfQuietHours(
        ProjectNotificationSchedule schedule,
        DateTimeOffset utcNow)
    {
        if (!schedule.QuietHoursEnd.HasValue) return utcNow;
        var timezone = ResolveTimezone(schedule.TimezoneName);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, timezone);
        var localTarget = localNow.Date.Add(schedule.QuietHoursEnd.Value.ToTimeSpan());
        if (localTarget <= localNow.DateTime) localTarget = localTarget.AddDays(1);
        var unspecified = DateTime.SpecifyKind(localTarget, DateTimeKind.Unspecified);
        return new DateTimeOffset(
            unspecified,
            timezone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    private static DateTime MonthEndTarget(
        DateTime basis,
        TimeOnly time,
        int daysBeforeMonthEnd)
    {
        var lastDay = new DateTime(
            basis.Year,
            basis.Month,
            DateTime.DaysInMonth(basis.Year, basis.Month));
        return lastDay
            .AddDays(-Math.Clamp(daysBeforeMonthEnd, 0, 31))
            .Date
            .Add(time.ToTimeSpan());
    }

    private static TimeZoneInfo ResolveTimezone(string timezoneName)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneName);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static int ReadBoundedInt(
        string name,
        int fallback,
        int minimum,
        int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
}
