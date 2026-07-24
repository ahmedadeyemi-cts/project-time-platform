using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<Module001SubmissionValidation> ValidateModule001WeekAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ActorContext actor,
        DateOnly weekStart)
    {
        var errors = new List<string>();
        var incompleteEntries = new List<object>();
        Guid? timesheetId = null;

        await using (var timesheet = new NpgsqlCommand("""
            SELECT timesheet_id
            FROM timesheets
            WHERE user_id = @user_id
              AND week_start_date = @week_start;
            """, connection, transaction))
        {
            timesheet.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            timesheet.Parameters.AddWithValue("week_start", weekStart);
            var value = await timesheet.ExecuteScalarAsync();
            if (value is Guid id) timesheetId = id;
        }

        var runningTimer = false;
        await using (var running = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM module001_timer_sessions
                WHERE user_id = @user_id
                  AND timer_status = 'RUNNING'
            );
            """, connection, transaction))
        {
            running.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            runningTimer = Convert.ToBoolean(await running.ExecuteScalarAsync() ?? false);
        }
        if (runningTimer)
        {
            errors.Add("Stop or discard the active timer before submitting the Timesheet week.");
        }

        if (timesheetId is null)
        {
            errors.Add("Save at least one Timesheet entry before submitting the week.");
            return new Module001SubmissionValidation(
                false,
                null,
                weekStart,
                weekStart.AddDays(6),
                0m,
                0,
                errors,
                incompleteEntries,
                runningTimer);
        }

        var totalHours = 0m;
        var entryCount = 0;
        var dayTotals = new Dictionary<DateOnly, decimal>();

        await using (var entries = new NpgsqlCommand("""
            SELECT te.time_entry_id,
                   te.work_date,
                   te.hours,
                   COALESCE(te.description, ''),
                   te.project_id,
                   te.task_id,
                   te.non_project_time_category_id,
                   COALESCE(p.project_code, ''),
                   COALESCE(p.project_name, ''),
                   COALESCE(pt.task_name, ''),
                   EXISTS (
                       SELECT 1
                       FROM project_assignments pa
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
                var timeEntryId = reader.GetGuid(0);
                var workDate = reader.GetFieldValue<DateOnly>(1);
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
                dayTotals[workDate] = dayTotals.GetValueOrDefault(workDate) + hours;

                var reasons = new List<string>();
                if (string.IsNullOrWhiteSpace(description))
                {
                    reasons.Add("meaningful work description is required");
                }
                if (projectId is not null && (taskId is null || !assignmentValid))
                {
                    reasons.Add("valid assigned project task is required");
                }
                if (projectId is null && categoryId is null)
                {
                    reasons.Add("project task or authorized non-project activity is required");
                }

                if (reasons.Count > 0)
                {
                    incompleteEntries.Add(new
                    {
                        timeEntryId,
                        workDate,
                        projectCode,
                        projectName,
                        taskName,
                        hours,
                        reasons
                    });
                }
            }
        }

        if (entryCount == 0)
        {
            errors.Add("The Timesheet week contains no positive-hour entries.");
        }

        foreach (var day in dayTotals.OrderBy(item => item.Key))
        {
            if (day.Value < 8m)
            {
                errors.Add(
                    $"{day.Key:yyyy-MM-dd} has {day.Value:0.00} hours; the existing daily submission policy requires at least 8.00 hours.");
            }
            if (day.Value > 24m)
            {
                errors.Add($"{day.Key:yyyy-MM-dd} exceeds the 24.00-hour daily limit.");
            }
        }

        if (incompleteEntries.Count > 0)
        {
            errors.Add(
                $"{incompleteEntries.Count} positive-hour entry or entries require description or task-association correction.");
        }

        return new Module001SubmissionValidation(
            errors.Count == 0,
            timesheetId,
            weekStart,
            weekStart.AddDays(6),
            totalHours,
            entryCount,
            errors,
            incompleteEntries,
            runningTimer);
    }

    private static async Task SubmitModule001WeekAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActorContext actor,
        Module001SubmissionValidation validation,
        string reason)
    {
        if (!validation.Valid || validation.TimesheetId is not Guid timesheetId)
        {
            throw new InvalidOperationException("A valid Timesheet week is required before submission.");
        }

        await using (var timesheet = new NpgsqlCommand("""
            UPDATE timesheets
            SET status = 'submitted',
                submitted_at = NOW(),
                submitted_by_user_id = @user_id,
                submission_reason = NULLIF(BTRIM(@reason), ''),
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND user_id = @user_id
              AND status IN ('draft','manager_declined');
            """, connection, transaction))
        {
            timesheet.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            timesheet.Parameters.AddWithValue("reason", reason ?? string.Empty);
            timesheet.Parameters.AddWithValue("timesheet_id", timesheetId);
            if (await timesheet.ExecuteNonQueryAsync() != 1)
            {
                throw new InvalidOperationException(
                    "The Timesheet week is no longer editable or was already submitted.");
            }
        }

        await using (var entries = new NpgsqlCommand("""
            UPDATE time_entries
            SET status = 'submitted',
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND user_id = @user_id
              AND hours > 0
              AND status IN ('draft','manager_declined');
            """, connection, transaction))
        {
            entries.Parameters.AddWithValue("timesheet_id", timesheetId);
            entries.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            await entries.ExecuteNonQueryAsync();
        }

        await using (var dayStatuses = new NpgsqlCommand("""
            INSERT INTO timesheet_day_statuses (
                timesheet_id, user_id, work_date, status, submitted_at
            )
            SELECT DISTINCT
                @timesheet_id,
                @user_id,
                te.work_date,
                'submitted',
                NOW()
            FROM time_entries te
            WHERE te.timesheet_id = @timesheet_id
              AND te.user_id = @user_id
              AND te.hours > 0
            ON CONFLICT (timesheet_id, work_date)
            DO UPDATE SET status = 'submitted',
                          submitted_at = NOW(),
                          unlocked_at = NULL,
                          unlocked_by_user_id = NULL,
                          updated_at = NOW();
            """, connection, transaction))
        {
            dayStatuses.Parameters.AddWithValue("timesheet_id", timesheetId);
            dayStatuses.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            await dayStatuses.ExecuteNonQueryAsync();
        }

        await InsertModule001PlatformAuditAsync(
            connection,
            transaction,
            actor.ActualUserId,
            "TIMESHEET_SUBMITTED",
            "timesheet",
            timesheetId,
            new { status = "draft", validation.WeekStart },
            new
            {
                status = "submitted",
                validation.WeekStart,
                validation.WeekEnd,
                validation.TotalHours,
                validation.EntryCount,
                reason
            });
    }
}
