using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<Module001TimerRow> FinalizeModule001TimerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActorContext actor,
        Module001TimerRow timer,
        DateTimeOffset requestedStopUtc,
        string description,
        string reason)
    {
        var capAt = timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds);
        var autoStopped = requestedStopUtc >= capAt;
        var effectiveStop = requestedStopUtc < capAt ? requestedStopUtc : capAt;
        var actualSeconds = Math.Clamp(
            (int)Math.Floor((effectiveStop - timer.StartedAtUtc).TotalSeconds),
            0,
            Module001TimerCapSeconds);
        var roundedMinutes = Module001RoundedMinutes(actualSeconds);
        var timeZone = Module001TimeZone(timer.TimeZoneId);
        var segments = Module001BuildSegments(
            timer.StartedAtUtc,
            effectiveStop,
            timeZone,
            roundedMinutes);

        var target = timer.AssignmentId is Guid assignmentId
            ? await LoadModule001AssignmentTargetAsync(
                connection,
                transaction,
                assignmentId,
                actor.EffectiveUserId,
                timer.EntryDate)
            : timer.NonProjectCategoryId is Guid categoryId
                ? await LoadModule001NonProjectTargetAsync(
                    connection,
                    transaction,
                    categoryId)
                : null;

        if (target is null)
        {
            throw new InvalidOperationException(
                "The timer task or non-project activity is no longer authorized.");
        }

        Guid? firstEntryId = null;
        foreach (var segment in segments)
        {
            if (segment.RoundedMinutes <= 0) continue;

            var entryId = await Module001UpsertTimerEntryAsync(
                connection,
                transaction,
                actor,
                timer.TimerSessionId,
                target,
                timer.TimeClassification,
                segment.LocalDate,
                segment.RoundedMinutes,
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
            connection,
            transaction,
            actor.EffectiveUserId,
            timer.WeekStartDate,
            target,
            "TIMER");

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
            AddNullableGuid(update, "time_entry_id", firstEntryId);
            update.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            update.Parameters.AddWithValue("timer_session_id", timer.TimerSessionId);
            await update.ExecuteNonQueryAsync();
        }

        await InsertModule001TimerAuditAsync(
            connection,
            transaction,
            timer.TimerSessionId,
            actor.ActualUserId,
            autoStopped ? "TIMER_AUTO_STOPPED" : "TIMER_STOPPED",
            reason,
            new
            {
                timerStatus = timer.TimerStatus,
                startedAtUtc = timer.StartedAtUtc,
                rowVersion = timer.RowVersion
            },
            new
            {
                timerStatus = finalStatus,
                effectiveStoppedAtUtc = effectiveStop,
                actualElapsedSeconds = actualSeconds,
                roundedMinutes,
                resultingTimesheetEntryId = firstEntryId
            },
            new
            {
                timeZoneId = timer.TimeZoneId,
                segments,
                descriptionComplete = !string.IsNullOrWhiteSpace(description)
            });

        return (await LoadModule001TimerAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            timer.TimerSessionId,
            false,
            false))!;
    }

    private static async Task<Module001TimerRow?> AutoStopModule001TimerAsync(
        NpgsqlConnection connection,
        ActorContext actor)
    {
        if (actor.IsViewAs)
        {
            return await LoadModule001TimerAsync(
                connection,
                null,
                actor.EffectiveUserId,
                null,
                true,
                false);
        }

        await using var transaction = await connection.BeginTransactionAsync();
        var timer = await LoadModule001TimerAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            null,
            true,
            true);

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
            connection,
            transaction,
            actor,
            timer,
            timer.StartedAtUtc.AddSeconds(Module001TimerCapSeconds),
            timer.Description,
            "Automatically stopped at the 12-hour maximum.");
        await transaction.CommitAsync();
        return finalized;
    }
}
