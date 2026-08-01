using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<IResult> Module001StopTimerV2Async(
        Guid timerSessionId,
        Module001TimerStopRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001MultiTimerReadyAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        await AcquireModule001MultiTimerUserLockAsync(connection, transaction, actor.EffectiveUserId);
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
                message = "That running timer was not found. Refresh the timer workspace."
            });
        }
        if (request.ExpectedRowVersion.HasValue
            && request.ExpectedRowVersion.Value != timer.RowVersion)
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "timer_version_conflict",
                message = "The timer changed on another device. Refresh before stopping it."
            });
        }

        try
        {
            var finalized = await FinalizeModule001MultiTimerAsync(
                connection,
                transaction,
                actor,
                timer,
                DateTimeOffset.UtcNow,
                request.Description ?? timer.Description,
                request.Reason ?? "Timer stopped individually from Module 001 Timesheet.");
            await transaction.CommitAsync();
            return Results.Ok(new
            {
                status = finalized.TimerStatus,
                contractVersion = "module001-multi-timer-v2",
                message = string.IsNullOrWhiteSpace(finalized.Description)
                    ? "Timer stopped and draft time recorded. Add a description before submission."
                    : "Timer stopped and draft time recorded.",
                refreshTimesheet = true,
                timer = Module001MultiTimerResponse(finalized, DateTimeOffset.UtcNow)
            });
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync();
            return Results.BadRequest(new
            {
                status = "timer_conversion_blocked",
                message = exception.Message
            });
        }
    }

    private static async Task<IResult> Module001StopAllTimersAsync(
        Module001MultiTimerStopAllRequest request,
        HttpContext context)
    {
        var requestedItems = request.Timers?.ToArray() ?? [];
        if (requestedItems.Length is < 1 or > Module001MultiTimerMaximumActive
            || requestedItems.Select(item => item.TimerSessionId).Distinct().Count() != requestedItems.Length)
        {
            return Results.BadRequest(new
            {
                status = "stop_all_timer_set_invalid",
                message = "Stop All requires one unique request item for every visible running timer."
            });
        }

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001MultiTimerReadyAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        await AcquireModule001MultiTimerUserLockAsync(connection, transaction, actor.EffectiveUserId);
        var running = await LoadModule001RunningTimersAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            true);
        var runningIds = running.Select(timer => timer.TimerSessionId).ToHashSet();
        var requestedIds = requestedItems.Select(item => item.TimerSessionId).ToHashSet();
        if (!runningIds.SetEquals(requestedIds))
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "stop_all_timer_set_changed",
                message = "The running timer set changed on another device. Refresh before using Stop All.",
                activeTimers = running.Select(timer => Module001MultiTimerResponse(timer, DateTimeOffset.UtcNow))
            });
        }

        var requestById = requestedItems.ToDictionary(item => item.TimerSessionId);
        foreach (var timer in running)
        {
            var item = requestById[timer.TimerSessionId];
            if (item.ExpectedRowVersion.HasValue
                && item.ExpectedRowVersion.Value != timer.RowVersion)
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new
                {
                    status = "timer_version_conflict",
                    message = "At least one timer changed on another device. No timer was stopped. Refresh and try again."
                });
            }
        }

        var stoppedAtUtc = DateTimeOffset.UtcNow;
        var finalized = new List<Module001TimerRow>();
        try
        {
            foreach (var timer in running)
            {
                var item = requestById[timer.TimerSessionId];
                finalized.Add(await FinalizeModule001MultiTimerAsync(
                    connection,
                    transaction,
                    actor,
                    timer,
                    stoppedAtUtc,
                    item.Description ?? timer.Description,
                    request.Reason ?? "All visible timers stopped together from Module 001 Timesheet."));
            }
            await transaction.CommitAsync();
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync();
            return Results.BadRequest(new
            {
                status = "stop_all_conversion_blocked",
                message = $"No timer was stopped. {exception.Message}"
            });
        }

        return Results.Ok(new
        {
            status = "STOPPED_DRAFT",
            contractVersion = "module001-multi-timer-v2",
            message = $"All {finalized.Count} running timers stopped and their draft time entries were recorded.",
            atomic = true,
            refreshTimesheet = true,
            stoppedAtUtc,
            timers = finalized.Select(timer => Module001MultiTimerResponse(timer, DateTimeOffset.UtcNow))
        });
    }

    private static async Task<IResult> Module001DiscardTimerV2Async(
        Guid timerSessionId,
        Module001TimerDiscardRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequireModule001MultiTimerReadyAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        await using var transaction = await connection.BeginTransactionAsync();
        await AcquireModule001MultiTimerUserLockAsync(connection, transaction, actor.EffectiveUserId);
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
                message = "That running timer was not found. Refresh the timer workspace."
            });
        }
        if (request.ExpectedRowVersion.HasValue
            && request.ExpectedRowVersion.Value != timer.RowVersion)
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "timer_version_conflict",
                message = "The timer changed on another device. Refresh before discarding it."
            });
        }

        var discardedAt = DateTimeOffset.UtcNow;
        var effectiveStop = discardedAt < timer.StartedAtUtc.AddSeconds(Module001MultiTimerCapSeconds)
            ? discardedAt
            : timer.StartedAtUtc.AddSeconds(Module001MultiTimerCapSeconds);
        await using (var update = new NpgsqlCommand("""
            UPDATE module001_timer_sessions
            SET stopped_at_utc = @discarded_at,
                effective_stopped_at_utc = @effective_stop,
                timer_status = 'DISCARDED',
                updated_by_user_id = @user_id
            WHERE timer_session_id = @timer_session_id;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("discarded_at", discardedAt);
            update.Parameters.AddWithValue("effective_stop", effectiveStop);
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
            new { timerStatus = "DISCARDED", discardedAt, effectiveStop },
            new
            {
                contractVersion = "module001-multi-timer-v2",
                maximumDurationSeconds = Module001MultiTimerCapSeconds,
                noTimesheetEntryCreated = true
            });
        await transaction.CommitAsync();
        return Results.Ok(new
        {
            status = "DISCARDED",
            contractVersion = "module001-multi-timer-v2",
            message = "Timer discarded. No Timesheet time was created."
        });
    }
}
