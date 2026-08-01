using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static string Module001MultiTimerTargetKey(Module001Target target) =>
        target.AssignmentId is Guid assignmentId
            ? $"assignment:{assignmentId:D}"
            : target.NonProjectCategoryId is Guid categoryId
                ? $"category:{categoryId:D}"
                : string.Empty;

    private static async Task<Module001Target?> ResolveModule001MultiTimerTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Module001MultiTimerStartTarget request,
        Guid userId,
        DateOnly effectiveDate)
    {
        var code = (request.NonProjectCategoryCode ?? string.Empty).Trim().ToUpperInvariant();
        var supplied = (request.AssignmentId.HasValue ? 1 : 0)
            + (request.NonProjectTimeCategoryId.HasValue ? 1 : 0)
            + (!string.IsNullOrWhiteSpace(code) ? 1 : 0);
        if (supplied != 1) return null;

        if (request.AssignmentId is Guid assignmentId)
        {
            return await LoadModule001AssignmentTargetAsync(
                connection,
                transaction,
                assignmentId,
                userId,
                effectiveDate);
        }

        if (request.NonProjectTimeCategoryId is Guid categoryId)
        {
            return await LoadModule001NonProjectTargetAsync(connection, transaction, categoryId);
        }

        if (code.Length is < 1 or > 100
            || code.Any(character => !char.IsLetterOrDigit(character) && character is not ('_' or '-')))
        {
            return null;
        }

        await using var command = new NpgsqlCommand("""
            SELECT non_project_time_category_id
            FROM non_project_time_categories
            WHERE UPPER(category_code) = @category_code
              AND is_active = TRUE
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("category_code", code);
        var value = await command.ExecuteScalarAsync();
        return value is Guid resolvedCategoryId
            ? await LoadModule001NonProjectTargetAsync(connection, transaction, resolvedCategoryId)
            : null;
    }

    private static async Task<IResult> Module001StartTimerBatchAsync(
        Module001MultiTimerStartRequest request,
        HttpContext context)
    {
        var requestedTargets = request.Targets?.Where(target => target is not null).ToArray() ?? [];
        if (requestedTargets.Length is < 1 or > Module001MultiTimerMaximumActive)
        {
            return Results.BadRequest(new
            {
                status = "multi_timer_target_count_invalid",
                message = $"Select between 1 and {Module001MultiTimerMaximumActive} authorized activities."
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
        var readiness = await RequireModule001MultiTimerReadyAsync(connection);
        if (readiness is not null) return readiness;
        var access = await RequireModule001AccessAsync(context, connection, "TIME_EDIT_OWN", true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        var autoStop = await AutoStopModule001TimerSetAsync(connection, actor);
        if (autoStop.Warnings.Count > 0)
        {
            return Results.Conflict(new
            {
                status = "expired_timer_conversion_blocked",
                message = string.Join(" ", autoStop.Warnings),
                activeTimers = autoStop.Active.Select(timer =>
                    Module001MultiTimerResponse(timer, DateTimeOffset.UtcNow))
            });
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var timeZone = Module001TimeZone(request.TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startedAtUtc, timeZone).DateTime);
        var weekStart = Module001WeekStart(localDate);
        var resolved = new List<Module001Target>();

        foreach (var targetRequest in requestedTargets)
        {
            var target = await ResolveModule001MultiTimerTargetAsync(
                connection,
                null,
                targetRequest,
                actor.EffectiveUserId,
                localDate);
            if (target is null)
            {
                return Results.BadRequest(new
                {
                    status = "timer_target_not_authorized",
                    message = "One selected task or non-project activity is unavailable or not authorized. Refresh the list and try again."
                });
            }

            var projectAccess = await RequireModule001AccessAsync(
                context,
                connection,
                "TIME_EDIT_OWN",
                true,
                target.ProjectId);
            if (projectAccess.Error is not null) return projectAccess.Error;
            resolved.Add(target);
        }

        var requestedKeys = resolved.Select(Module001MultiTimerTargetKey).ToArray();
        if (requestedKeys.Any(string.IsNullOrWhiteSpace)
            || requestedKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requestedKeys.Length)
        {
            return Results.BadRequest(new
            {
                status = "duplicate_timer_target",
                message = "Each selected activity may appear only once in a multi-timer start request."
            });
        }

        await using var transaction = await connection.BeginTransactionAsync();
        await AcquireModule001MultiTimerUserLockAsync(
            connection,
            transaction,
            actor.EffectiveUserId);
        var existing = await LoadModule001RunningTimersAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            true);
        var existingKeys = existing.Select(timer => timer.AssignmentId is Guid assignmentId
            ? $"assignment:{assignmentId:D}"
            : timer.NonProjectCategoryId is Guid categoryId
                ? $"category:{categoryId:D}"
                : string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var duplicate = requestedKeys.FirstOrDefault(existingKeys.Contains);
        if (!string.IsNullOrWhiteSpace(duplicate))
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "timer_target_already_running",
                message = "One selected activity already has a running timer.",
                maximumConcurrentTimers = Module001MultiTimerMaximumActive,
                activeTimers = existing.Select(timer => Module001MultiTimerResponse(timer, DateTimeOffset.UtcNow))
            });
        }

        if (existing.Count + resolved.Count > Module001MultiTimerMaximumActive)
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "multi_timer_limit_reached",
                message = $"A maximum of {Module001MultiTimerMaximumActive} timers may run at once. Stop one or more active timers and try again.",
                maximumConcurrentTimers = Module001MultiTimerMaximumActive,
                activeTimers = existing.Select(timer => Module001MultiTimerResponse(timer, DateTimeOffset.UtcNow))
            });
        }

        var timerIds = new List<Guid>();
        try
        {
            foreach (var target in resolved)
            {
                await UpsertModule001WeeklyLineAsync(
                    connection,
                    transaction,
                    actor.EffectiveUserId,
                    weekStart,
                    target,
                    "TIMER");

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
                var timerSessionId = (Guid)(await insert.ExecuteScalarAsync()
                    ?? throw new InvalidOperationException("Unable to start one of the selected timers."));
                timerIds.Add(timerSessionId);

                await InsertModule001TimerAuditAsync(
                    connection,
                    transaction,
                    timerSessionId,
                    actor.ActualUserId,
                    "TIMER_STARTED",
                    "Started from the Module 001 simultaneous timer workspace.",
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
                    new
                    {
                        contractVersion = "module001-multi-timer-v2",
                        batchSize = resolved.Count,
                        timeZoneId = timeZone.Id,
                        descriptionComplete = !string.IsNullOrWhiteSpace(request.Description)
                    });
            }

            await transaction.CommitAsync();
        }
        catch (PostgresException exception)
            when (exception.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation)
        {
            await transaction.RollbackAsync();
            return Results.Conflict(new
            {
                status = "multi_timer_concurrency_conflict",
                message = "The active timer set changed on another device. Refresh the timer workspace and try again."
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        var timers = new List<object>();
        var responseAt = DateTimeOffset.UtcNow;
        foreach (var timerId in timerIds)
        {
            var timer = await LoadModule001TimerAsync(
                connection,
                null,
                actor.EffectiveUserId,
                timerId,
                false,
                false);
            if (timer is not null) timers.Add(Module001MultiTimerResponse(timer, responseAt));
        }

        return Results.Json(new
        {
            status = "running",
            contractVersion = "module001-multi-timer-v2",
            message = $"{timers.Count} timer{(timers.Count == 1 ? string.Empty : "s")} started from one authoritative server timestamp.",
            startedAtUtc,
            maximumConcurrentTimers = Module001MultiTimerMaximumActive,
            timers
        }, statusCode: StatusCodes.Status201Created);
    }
}
