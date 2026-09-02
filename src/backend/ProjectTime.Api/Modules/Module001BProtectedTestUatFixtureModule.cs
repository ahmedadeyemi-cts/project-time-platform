using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Disposable live-UAT fixture support for Module 001B.
///
/// These routes are deliberately fail-closed behind an explicit Protected-Test runtime
/// enable flag and remain behind the normal Project Team Coordinator / Super Administrator
/// steward boundary. They create a single submitted entry in an isolated future week,
/// allow the real Module 001B reallocation endpoint to mutate it, and then remove the
/// disposable records. No current employee Timesheet week is used as UAT data.
/// </summary>
public static partial class ScopedRolePolicyModule
{
    private const string Module001BProtectedTestUatEnabledVariable =
        "PROJECTPULSE_MODULE001B_PROTECTED_TEST_UAT_ENABLED";
    private const string Module001BProtectedTestFixtureDescription =
        "PROTECTED_TEST_UAT_MODULE001B_REALLOCATION";
    private const string Module001BProtectedTestTargetEmail =
        "demo.engineer@ussignal.local";

    private static readonly DateOnly Module001BProtectedTestWeekStart =
        new(2099, 12, 27);

    public static WebApplication MapModule001BProtectedTestUatEndpoints(this WebApplication app)
    {
        app.MapPost(
            "/api/runtime/timesheet/steward/001b/protected-test-uat/fixture",
            (Func<HttpContext, Task<IResult>>)Module001BPrepareProtectedTestUatFixtureAsync);
        app.MapDelete(
            "/api/runtime/timesheet/steward/001b/protected-test-uat/fixture/{timeEntryId:guid}",
            (Func<Guid, HttpContext, Task<IResult>>)Module001BCleanupProtectedTestUatFixtureAsync);
        return app;
    }

    private static bool Module001BIsProtectedTestRequest(HttpContext context)
        => string.Equals(
            Environment.GetEnvironmentVariable(Module001BProtectedTestUatEnabledVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static IResult Module001BProtectedTestOnly()
        => Results.NotFound(new
        {
            status = "protected_test_uat_route_unavailable",
            message = "This disposable UAT route is available only while the governed Protected-Test UAT gate is enabled."
        });

    private static async Task<Guid?> Module001BProtectedTestTargetUserIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT user_id
            FROM app_users
            WHERE LOWER(email) = LOWER(@email)
              AND is_active = TRUE
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("email", Module001BProtectedTestTargetEmail);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    private static async Task Module001BDeleteProtectedTestFixtureAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid targetUserId,
        Guid? timeEntryId,
        CancellationToken cancellationToken)
    {
        var entryPredicate = timeEntryId.HasValue
            ? "te.time_entry_id = @time_entry_id"
            : "te.user_id = @user_id AND te.work_date BETWEEN @week_start AND @week_end AND te.description = @description";

        var ids = new List<Guid>();
        await using (var select = new NpgsqlCommand($"""
            SELECT te.time_entry_id
            FROM time_entries te
            WHERE {entryPredicate};
            """, connection, transaction))
        {
            if (timeEntryId.HasValue)
                select.Parameters.AddWithValue("time_entry_id", timeEntryId.Value);
            else
            {
                select.Parameters.AddWithValue("user_id", targetUserId);
                select.Parameters.AddWithValue("week_start", Module001BProtectedTestWeekStart);
                select.Parameters.AddWithValue("week_end", Module001BProtectedTestWeekStart.AddDays(6));
                select.Parameters.AddWithValue("description", Module001BProtectedTestFixtureDescription);
            }

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        }

        foreach (var id in ids)
        {
            // The entry-association foreign key is ON DELETE CASCADE. Delete only
            // the disposable entry here; immutable steward events are deliberately
            // retained and continue to anchor the otherwise-empty Timesheet row.
            await using (var entry = new NpgsqlCommand("""
                DELETE FROM time_entries
                WHERE time_entry_id = @time_entry_id
                  AND user_id = @user_id
                  AND work_date BETWEEN @week_start AND @week_end
                  AND description = @description;
                """, connection, transaction))
            {
                entry.Parameters.AddWithValue("time_entry_id", id);
                entry.Parameters.AddWithValue("user_id", targetUserId);
                entry.Parameters.AddWithValue("week_start", Module001BProtectedTestWeekStart);
                entry.Parameters.AddWithValue("week_end", Module001BProtectedTestWeekStart.AddDays(6));
                entry.Parameters.AddWithValue("description", Module001BProtectedTestFixtureDescription);
                await entry.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using var timesheet = new NpgsqlCommand("""
            DELETE FROM timesheets t
            WHERE t.user_id = @user_id
              AND t.week_start_date = @week_start
              AND NOT EXISTS (
                  SELECT 1
                  FROM time_entries te
                  WHERE te.timesheet_id = t.timesheet_id
              )
              AND NOT EXISTS (
                  SELECT 1
                  FROM scoped_time_management_events audit
                  WHERE audit.timesheet_id = t.timesheet_id
              );
            """, connection, transaction);
        timesheet.Parameters.AddWithValue("user_id", targetUserId);
        timesheet.Parameters.AddWithValue("week_start", Module001BProtectedTestWeekStart);
        await timesheet.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IResult> Module001BPrepareProtectedTestUatFixtureAsync(
        HttpContext context)
    {
        if (!Module001BIsProtectedTestRequest(context))
            return Module001BProtectedTestOnly();

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;

        var targetUserId = await Module001BProtectedTestTargetUserIdAsync(
            connection,
            null,
            context.RequestAborted);
        if (!targetUserId.HasValue
            || !await RuntimePtcManagedUserExistsAsync(connection, targetUserId.Value))
        {
            return Results.Json(new
            {
                status = "protected_test_fixture_user_unavailable",
                targetEmail = Module001BProtectedTestTargetEmail
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var access = await RequirePtcTimeStewardAccessAsync(
            context,
            connection,
            "TIME_REASSIGN",
            targetUserId.Value,
            null,
            true);
        if (access.Error is not null) return access.Error;

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            // A prior interrupted UAT run may have left only this isolated fixture.
            await Module001BDeleteProtectedTestFixtureAsync(
                connection,
                transaction,
                targetUserId.Value,
                null,
                context.RequestAborted);

            await using (var occupied = new NpgsqlCommand("""
                SELECT COUNT(*)
                FROM time_entries
                WHERE user_id = @user_id
                  AND work_date BETWEEN @week_start AND @week_end;
                """, connection, transaction))
            {
                occupied.Parameters.AddWithValue("user_id", targetUserId.Value);
                occupied.Parameters.AddWithValue("week_start", Module001BProtectedTestWeekStart);
                occupied.Parameters.AddWithValue("week_end", Module001BProtectedTestWeekStart.AddDays(6));
                var count = Convert.ToInt64(await occupied.ExecuteScalarAsync(context.RequestAborted) ?? 0L);
                if (count != 0)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Conflict(new
                    {
                        status = "protected_test_fixture_week_occupied",
                        weekStart = Module001BProtectedTestWeekStart,
                        message = "The isolated Module 001B UAT week contains non-fixture data and was not modified."
                    });
                }
            }

            Guid sourceProjectId;
            Guid sourceTaskId;
            bool sourceBillable;
            await using (var source = new NpgsqlCommand("""
                SELECT p.project_id,
                       pt.task_id,
                       COALESCE(pt.billable, p.billable, TRUE)
                FROM projects p
                JOIN project_tasks pt
                  ON pt.project_id = p.project_id
                 AND pt.is_active = TRUE
                WHERE p.status IN ('active', 'on_hold')
                  AND NOT EXISTS (
                      SELECT 1
                      FROM module001a_engineer_task_closeouts closeout
                      WHERE closeout.engineer_user_id = @target_user_id
                        AND closeout.project_id = p.project_id
                        AND closeout.task_id = pt.task_id
                        AND closeout.closeout_status IN ('engineer_closed', 'ptc_final_closed')
                  )
                ORDER BY p.project_code, pt.task_code
                LIMIT 1;
                """, connection, transaction))
            {
                source.Parameters.AddWithValue("target_user_id", targetUserId.Value);
                await using var reader = await source.ExecuteReaderAsync(context.RequestAborted);
                if (!await reader.ReadAsync(context.RequestAborted))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Json(new { status = "protected_test_source_task_unavailable" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                sourceProjectId = reader.GetGuid(0);
                sourceTaskId = reader.GetGuid(1);
                sourceBillable = reader.GetBoolean(2);
            }

            Guid destinationCategoryId;
            await using (var destination = new NpgsqlCommand("""
                SELECT non_project_time_category_id
                FROM non_project_time_categories
                WHERE is_active = TRUE
                ORDER BY display_order, category_name
                LIMIT 1;
                """, connection, transaction))
            {
                var value = await destination.ExecuteScalarAsync(context.RequestAborted);
                if (value is not Guid categoryId)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Json(new { status = "protected_test_non_project_category_unavailable" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                destinationCategoryId = categoryId;
            }

            Guid timesheetId;
            await using (var timesheet = new NpgsqlCommand("""
                INSERT INTO timesheets (
                    user_id, week_start_date, week_end_date, status, submitted_at
                ) VALUES (
                    @user_id, @week_start, @week_end, 'submitted', NOW()
                )
                ON CONFLICT (user_id, week_start_date)
                DO UPDATE SET week_end_date = EXCLUDED.week_end_date,
                              status = 'submitted',
                              submitted_at = NOW(),
                              updated_at = NOW()
                RETURNING timesheet_id;
                """, connection, transaction))
            {
                timesheet.Parameters.AddWithValue("user_id", targetUserId.Value);
                timesheet.Parameters.AddWithValue("week_start", Module001BProtectedTestWeekStart);
                timesheet.Parameters.AddWithValue("week_end", Module001BProtectedTestWeekStart.AddDays(6));
                timesheetId = (Guid)(await timesheet.ExecuteScalarAsync(context.RequestAborted)
                    ?? throw new InvalidOperationException("Unable to create the Protected-Test UAT Timesheet fixture."));
            }

            var workDate = Module001BProtectedTestWeekStart.AddDays(1);
            const decimal hours = 1.25m;
            Guid timeEntryId;
            await using (var entry = new NpgsqlCommand("""
                INSERT INTO time_entries (
                    timesheet_id, user_id, project_id, task_id,
                    non_project_time_category_id, work_date, time_type,
                    hours, description, billable, status
                ) VALUES (
                    @timesheet_id, @user_id, @project_id, @task_id,
                    NULL, @work_date, 'normal',
                    @hours, @description, @billable, 'submitted'
                )
                RETURNING time_entry_id;
                """, connection, transaction))
            {
                entry.Parameters.AddWithValue("timesheet_id", timesheetId);
                entry.Parameters.AddWithValue("user_id", targetUserId.Value);
                entry.Parameters.AddWithValue("project_id", sourceProjectId);
                entry.Parameters.AddWithValue("task_id", sourceTaskId);
                entry.Parameters.AddWithValue("work_date", workDate);
                entry.Parameters.AddWithValue("hours", hours);
                entry.Parameters.AddWithValue("description", Module001BProtectedTestFixtureDescription);
                entry.Parameters.AddWithValue("billable", sourceBillable);
                timeEntryId = (Guid)(await entry.ExecuteScalarAsync(context.RequestAborted)
                    ?? throw new InvalidOperationException("Unable to create the Protected-Test UAT time-entry fixture."));
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Json(new
            {
                status = "protected_test_fixture_ready",
                module = "001B",
                sourceCommit = Environment.GetEnvironmentVariable("PROJECTPULSE_SOURCE_COMMIT") ?? string.Empty,
                targetUserId,
                targetEmail = Module001BProtectedTestTargetEmail,
                weekStart = Module001BProtectedTestWeekStart,
                workDate,
                timesheetId,
                timeEntryId,
                expectedHours = hours,
                expectedStatus = "submitted",
                sourceProjectId,
                sourceTaskId,
                destinationType = "non_project",
                nonProjectTimeCategoryId = destinationCategoryId,
                disposable = true
            }, statusCode: StatusCodes.Status201Created);
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<IResult> Module001BCleanupProtectedTestUatFixtureAsync(
        Guid timeEntryId,
        HttpContext context)
    {
        if (!Module001BIsProtectedTestRequest(context))
            return Module001BProtectedTestOnly();

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;

        Guid targetUserId;
        await using (var fixture = new NpgsqlCommand("""
            SELECT user_id
            FROM time_entries
            WHERE time_entry_id = @time_entry_id
              AND work_date BETWEEN @week_start AND @week_end
              AND description = @description;
            """, connection))
        {
            fixture.Parameters.AddWithValue("time_entry_id", timeEntryId);
            fixture.Parameters.AddWithValue("week_start", Module001BProtectedTestWeekStart);
            fixture.Parameters.AddWithValue("week_end", Module001BProtectedTestWeekStart.AddDays(6));
            fixture.Parameters.AddWithValue("description", Module001BProtectedTestFixtureDescription);
            var value = await fixture.ExecuteScalarAsync(context.RequestAborted);
            if (value is not Guid id)
                return Results.NotFound(new { status = "protected_test_fixture_not_found" });
            targetUserId = id;
        }

        var access = await RequirePtcTimeStewardAccessAsync(
            context,
            connection,
            "TIME_REASSIGN",
            targetUserId,
            null,
            true);
        if (access.Error is not null) return access.Error;

        var auditVerified = false;
        await using (var audit = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM scoped_time_management_events
                WHERE time_entry_id = @time_entry_id
                  AND action_code = 'TIME_REASSIGN'
                  AND COALESCE(event_metadata->>'module', '') = '001B'
                  AND COALESCE((event_metadata->>'submissionStatePreserved')::boolean, FALSE) = TRUE
                  AND COALESCE((event_metadata->>'userMustResubmit')::boolean, TRUE) = FALSE
            );
            """, connection))
        {
            audit.Parameters.AddWithValue("time_entry_id", timeEntryId);
            auditVerified = Convert.ToBoolean(await audit.ExecuteScalarAsync(context.RequestAborted) ?? false);
        }

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await Module001BDeleteProtectedTestFixtureAsync(
                connection,
                transaction,
                targetUserId,
                timeEntryId,
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }

        return Results.Ok(new
        {
            status = "protected_test_fixture_removed",
            module = "001B",
            timeEntryId,
            auditVerified,
            currentEmployeeWeekMutation = false
        });
    }
}
