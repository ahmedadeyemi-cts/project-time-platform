using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class ProjectNotificationQuietHoursService
{
    internal static async Task<IResult> RunDueAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => actor.CanManageSchedules);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        try
        {
            var result = await RunDueSchedulesAsync(
                connection,
                access.Actor!.ActualUserId,
                context,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = "023",
                status = "due_notification_schedules_processed",
                deferredForQuietHours = result.DeferredForQuietHours,
                summary = result.Summary,
                message = result.DeferredForQuietHours > 0
                    ? "Due schedules outside quiet hours were evaluated. Quiet-hours schedules were deferred without sending mail. Module 065 remained the only delivery authority."
                    : "Due schedules were evaluated. Module 065 remained the only delivery authority."
            });
        }
        catch (Exception exception)
        {
            return ProjectNotificationRepository.SourceFailure(
                "023",
                "due_notification_schedule_processing",
                exception,
                "Due notification schedules could not be processed. Retry after the source is restored."
            );
        }
    }

    internal static async Task<QuietHoursRunResult> RunDueSchedulesAsync(
        NpgsqlConnection connection,
        Guid? actorUserId,
        HttpContext? context,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var schedules = await ProjectNotificationRepository.LoadSchedulesAsync(
            connection,
            cancellationToken);
        var deferred = 0;

        foreach (var schedule in schedules.Where(schedule =>
                     schedule.Enabled
                     && (!schedule.NextRunAt.HasValue || schedule.NextRunAt.Value <= now)
                     && ProjectNotificationScheduler.IsQuietHours(schedule, now)))
        {
            await ProjectNotificationRepository.UpdateScheduleRunStateAsync(
                connection,
                schedule.ScheduleId,
                false,
                "deferred_for_quiet_hours",
                ProjectNotificationScheduler.EndOfQuietHours(schedule, now),
                cancellationToken);
            deferred++;
        }

        var summary = await ProjectNotificationProcessingService.RunDueSchedulesAsync(
            connection,
            actorUserId,
            context,
            cancellationToken);
        return new(summary, deferred);
    }

    internal sealed record QuietHoursRunResult(
        ProjectNotificationProcessingService.ScheduleRunSummary Summary,
        int DeferredForQuietHours);
}
