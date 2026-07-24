using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<Module001Target?> LoadModule001AssignmentTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid assignmentId,
        Guid userId,
        DateOnly effectiveDate)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pa.project_assignment_id,
                   c.client_id,
                   COALESCE(c.client_name, ''),
                   p.project_id,
                   p.project_code,
                   p.project_name,
                   pt.task_id,
                   pt.task_code,
                   pt.task_name,
                   COALESCE(pt.billable, p.billable, TRUE)
            FROM project_assignments pa
            JOIN projects p ON p.project_id = pa.project_id
            JOIN project_tasks pt
              ON pt.task_id = pa.task_id
             AND pt.project_id = pa.project_id
            LEFT JOIN clients c ON c.client_id = p.client_id
            WHERE pa.project_assignment_id = @assignment_id
              AND pa.user_id = @user_id
              AND pa.effective_start_date <= @effective_date
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @effective_date)
              AND p.status IN ('active','on_hold')
              AND pt.is_active = TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("effective_date", effectiveDate);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new Module001Target(
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetGuid(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetGuid(0),
            null,
            string.Empty,
            string.Empty,
            reader.GetBoolean(9));
    }

    private static async Task<Module001Target?> LoadModule001NonProjectTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid categoryId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT non_project_time_category_id, category_code, category_name
            FROM non_project_time_categories
            WHERE non_project_time_category_id = @category_id
              AND is_active = TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("category_id", categoryId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new Module001Target(
            null,
            string.Empty,
            null,
            string.Empty,
            string.Empty,
            null,
            string.Empty,
            string.Empty,
            null,
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            false);
    }

    private static async Task UpsertModule001WeeklyLineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateOnly weekStart,
        Module001Target target,
        string source)
    {
        if (target.AssignmentId is Guid assignmentId)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO module001_weekly_task_lines (
                    user_id, week_start_date, customer_id, project_id, task_id,
                    assignment_id, activity_type, line_source,
                    created_by_user_id, updated_by_user_id
                ) VALUES (
                    @user_id, @week_start, @customer_id, @project_id, @task_id,
                    @assignment_id, 'PROJECT_TASK', @line_source,
                    @user_id, @user_id
                )
                ON CONFLICT (user_id, week_start_date, assignment_id)
                    WHERE assignment_id IS NOT NULL AND is_active = TRUE
                DO UPDATE SET is_active = TRUE,
                              line_source = EXCLUDED.line_source,
                              updated_by_user_id = EXCLUDED.updated_by_user_id;
                """, connection, transaction);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("week_start", weekStart);
            AddNullableGuid(command, "customer_id", target.CustomerId);
            command.Parameters.AddWithValue("project_id", target.ProjectId!.Value);
            command.Parameters.AddWithValue("task_id", target.TaskId!.Value);
            command.Parameters.AddWithValue("assignment_id", assignmentId);
            command.Parameters.AddWithValue("line_source", source);
            await command.ExecuteNonQueryAsync();
            return;
        }

        await using var nonProject = new NpgsqlCommand("""
            INSERT INTO module001_weekly_task_lines (
                user_id, week_start_date, non_project_time_category_id,
                activity_type, line_source, created_by_user_id, updated_by_user_id
            ) VALUES (
                @user_id, @week_start, @category_id,
                'NON_PROJECT', @line_source, @user_id, @user_id
            )
            ON CONFLICT (user_id, week_start_date, non_project_time_category_id)
                WHERE non_project_time_category_id IS NOT NULL AND is_active = TRUE
            DO UPDATE SET is_active = TRUE,
                          line_source = EXCLUDED.line_source,
                          updated_by_user_id = EXCLUDED.updated_by_user_id;
            """, connection, transaction);
        nonProject.Parameters.AddWithValue("user_id", userId);
        nonProject.Parameters.AddWithValue("week_start", weekStart);
        nonProject.Parameters.AddWithValue("category_id", target.NonProjectCategoryId!.Value);
        nonProject.Parameters.AddWithValue("line_source", source);
        await nonProject.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> Module001GetOrCreateTimesheetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        DateOnly weekStart)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO timesheets (user_id, week_start_date, week_end_date, status)
            VALUES (@user_id, @week_start, @week_end, 'draft')
            ON CONFLICT (user_id, week_start_date)
            DO UPDATE SET updated_at = NOW()
            RETURNING timesheet_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("week_start", weekStart);
        command.Parameters.AddWithValue("week_end", weekStart.AddDays(6));
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Unable to create or load the Timesheet week."));
    }

    private static async Task<Module001TimerRow?> LoadModule001TimerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        Guid? timerSessionId,
        bool runningOnly,
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
            """;
        if (timerSessionId.HasValue) sql += " AND t.timer_session_id = @timer_session_id";
        if (runningOnly) sql += " AND t.timer_status = 'RUNNING'";
        sql += " ORDER BY t.started_at_utc DESC LIMIT 1";
        if (forUpdate) sql += " FOR UPDATE";
        sql += ";";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        if (timerSessionId.HasValue)
        {
            command.Parameters.AddWithValue("timer_session_id", timerSessionId.Value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new Module001TimerRow(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetFieldValue<DateOnly>(2),
            reader.GetFieldValue<DateOnly>(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
            reader.IsDBNull(14) ? null : reader.GetInt32(14),
            reader.IsDBNull(15) ? null : reader.GetInt32(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetBoolean(18),
            reader.IsDBNull(19) ? null : reader.GetGuid(19),
            reader.GetInt32(20),
            reader.GetString(21),
            reader.GetString(22),
            reader.GetString(23),
            reader.GetString(24),
            reader.GetString(25),
            reader.GetString(26),
            reader.GetString(27));
    }

    private static async Task InsertModule001TimerAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timerSessionId,
        Guid actorUserId,
        string eventCode,
        string reason,
        object previousState,
        object newState,
        object metadata)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module001_timer_audit_events (
                timer_session_id, actor_user_id, event_code, reason,
                previous_state, new_state, event_metadata
            ) VALUES (
                @timer_session_id, @actor_user_id, @event_code, @reason,
                @previous_state, @new_state, @event_metadata
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("timer_session_id", timerSessionId);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("event_code", eventCode);
        command.Parameters.AddWithValue("reason", reason ?? string.Empty);
        command.Parameters.Add("previous_state", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(previousState);
        command.Parameters.Add("new_state", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(newState);
        command.Parameters.Add("event_metadata", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(metadata);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertModule001PlatformAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        string action,
        string entityType,
        Guid? entityId,
        object oldValue,
        object newValue)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO audit_logs (
                actor_user_id, action, entity_type, entity_id,
                old_value, new_value, created_at
            ) VALUES (
                @actor_user_id, @action, @entity_type, @entity_id,
                @old_value, @new_value, NOW()
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("entity_type", entityType);
        AddNullableGuid(command, "entity_id", entityId);
        command.Parameters.Add("old_value", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(oldValue);
        command.Parameters.Add("new_value", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(newValue);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> Module001UpsertTimerEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActorContext actor,
        Guid timerSessionId,
        Module001Target target,
        string classification,
        DateOnly workDate,
        int roundedMinutes,
        string description)
    {
        var weekStart = Module001WeekStart(workDate);
        var timesheetId = await Module001GetOrCreateTimesheetAsync(
            connection,
            transaction,
            actor.EffectiveUserId,
            weekStart);

        await using (var editable = new NpgsqlCommand("""
            SELECT COALESCE((
                SELECT status
                FROM timesheet_day_statuses
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
            ), 'draft');
            """, connection, transaction))
        {
            editable.Parameters.AddWithValue("timesheet_id", timesheetId);
            editable.Parameters.AddWithValue("work_date", workDate);
            var status = Convert.ToString(await editable.ExecuteScalarAsync()) ?? "draft";
            if (status is not ("draft" or "manager_declined"))
            {
                throw new InvalidOperationException(
                    $"{workDate:yyyy-MM-dd} is {status} and cannot receive timer-generated draft time.");
            }
        }

        var additionalHours = roundedMinutes / 60m;
        await using (var limit = new NpgsqlCommand("""
            SELECT COALESCE(SUM(hours), 0)
            FROM time_entries
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction))
        {
            limit.Parameters.AddWithValue("timesheet_id", timesheetId);
            limit.Parameters.AddWithValue("work_date", workDate);
            var current = Convert.ToDecimal(await limit.ExecuteScalarAsync() ?? 0m);
            if (current + additionalHours > 24m)
            {
                throw new InvalidOperationException(
                    $"Timer conversion would exceed 24.00 hours on {workDate:yyyy-MM-dd}.");
            }
        }

        Guid? existingId = null;
        await using (var existing = new NpgsqlCommand("""
            SELECT time_entry_id
            FROM time_entries
            WHERE timesheet_id = @timesheet_id
              AND user_id = @user_id
              AND work_date = @work_date
              AND time_type = @time_type
              AND status IN ('draft','manager_declined')
              AND (
                    (@project_id IS NOT NULL AND project_id = @project_id AND task_id = @task_id)
                 OR (@project_id IS NULL AND non_project_time_category_id = @category_id)
              )
            ORDER BY created_at
            LIMIT 1
            FOR UPDATE;
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue("timesheet_id", timesheetId);
            existing.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            existing.Parameters.AddWithValue("work_date", workDate);
            existing.Parameters.AddWithValue("time_type", classification);
            AddNullableGuid(existing, "project_id", target.ProjectId);
            AddNullableGuid(existing, "task_id", target.TaskId);
            AddNullableGuid(existing, "category_id", target.NonProjectCategoryId);
            var value = await existing.ExecuteScalarAsync();
            if (value is Guid id) existingId = id;
        }

        Guid timeEntryId;
        if (existingId is Guid id)
        {
            await using var update = new NpgsqlCommand("""
                UPDATE time_entries
                SET hours = hours + @additional_hours,
                    description = CASE
                        WHEN NULLIF(BTRIM(@description), '') IS NULL THEN description
                        WHEN NULLIF(BTRIM(description), '') IS NULL THEN BTRIM(@description)
                        WHEN POSITION(BTRIM(@description) IN description) > 0 THEN description
                        ELSE description || E'\n' || BTRIM(@description)
                    END,
                    status = 'draft',
                    updated_at = NOW()
                WHERE time_entry_id = @time_entry_id;
                """, connection, transaction);
            update.Parameters.AddWithValue("additional_hours", additionalHours);
            update.Parameters.AddWithValue("description", description ?? string.Empty);
            update.Parameters.AddWithValue("time_entry_id", id);
            await update.ExecuteNonQueryAsync();
            timeEntryId = id;
        }
        else
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO time_entries (
                    timesheet_id, user_id, project_id, task_id,
                    non_project_time_category_id, work_date, time_type,
                    hours, description, billable, status
                ) VALUES (
                    @timesheet_id, @user_id, @project_id, @task_id,
                    @category_id, @work_date, @time_type,
                    @hours, NULLIF(BTRIM(@description), ''), @billable, 'draft'
                )
                RETURNING time_entry_id;
                """, connection, transaction);
            insert.Parameters.AddWithValue("timesheet_id", timesheetId);
            insert.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            AddNullableGuid(insert, "project_id", target.ProjectId);
            AddNullableGuid(insert, "task_id", target.TaskId);
            AddNullableGuid(insert, "category_id", target.NonProjectCategoryId);
            insert.Parameters.AddWithValue("work_date", workDate);
            insert.Parameters.AddWithValue("time_type", classification);
            insert.Parameters.AddWithValue("hours", additionalHours);
            insert.Parameters.AddWithValue("description", description ?? string.Empty);
            insert.Parameters.AddWithValue("billable", target.Billable);
            timeEntryId = (Guid)(await insert.ExecuteScalarAsync()
                ?? throw new InvalidOperationException(
                    "Unable to create the timer-generated Timesheet entry."));
        }

        await using (var association = new NpgsqlCommand("""
            INSERT INTO module001_timesheet_entry_associations (
                time_entry_id, customer_id, project_id, task_id, assignment_id,
                non_project_time_category_id, source_timer_session_id,
                association_source, created_by_user_id, updated_by_user_id
            ) VALUES (
                @time_entry_id, @customer_id, @project_id, @task_id, @assignment_id,
                @category_id, @timer_session_id,
                'TIMER', @user_id, @user_id
            )
            ON CONFLICT (time_entry_id)
            DO UPDATE SET customer_id = EXCLUDED.customer_id,
                          project_id = EXCLUDED.project_id,
                          task_id = EXCLUDED.task_id,
                          assignment_id = EXCLUDED.assignment_id,
                          non_project_time_category_id = EXCLUDED.non_project_time_category_id,
                          source_timer_session_id = EXCLUDED.source_timer_session_id,
                          association_source = 'TIMER',
                          updated_by_user_id = EXCLUDED.updated_by_user_id;
            """, connection, transaction))
        {
            association.Parameters.AddWithValue("time_entry_id", timeEntryId);
            AddNullableGuid(association, "customer_id", target.CustomerId);
            AddNullableGuid(association, "project_id", target.ProjectId);
            AddNullableGuid(association, "task_id", target.TaskId);
            AddNullableGuid(association, "assignment_id", target.AssignmentId);
            AddNullableGuid(association, "category_id", target.NonProjectCategoryId);
            association.Parameters.AddWithValue("timer_session_id", timerSessionId);
            association.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
            await association.ExecuteNonQueryAsync();
        }

        return timeEntryId;
    }
}
