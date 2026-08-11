using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class AnalyticsCenterScheduleRepository
{
    internal const string MigrationId = "060_analytics_center_enterprise_experience";
    private const long SchedulerAdvisoryLock = 30060030L;

    internal static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = ProjectFinancialTruthModule.FinancialOperationsConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Pulse database configuration is unavailable.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    internal static async Task<bool> MigrationReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM schema_migrations WHERE migration_id = @migration_id
            );
            """, connection);
        command.Parameters.AddWithValue("migration_id", MigrationId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    internal static async Task<bool> TryAcquireSchedulerLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(@lock_id);",
            connection);
        command.Parameters.AddWithValue("lock_id", SchedulerAdvisoryLock);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    internal static async Task ReleaseSchedulerLockAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_unlock(@lock_id);",
            connection);
        command.Parameters.AddWithValue("lock_id", SchedulerAdvisoryLock);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    internal static async Task<AnalyticsSchedule[]> LoadSchedulesAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        var schedules = new List<AnalyticsSchedule>();
        await using (var command = new NpgsqlCommand("""
            SELECT analytics_report_schedule_id,
                   owner_actual_user_id,
                   owner_effective_user_id,
                   schedule_name,
                   report_code,
                   criteria_json::text,
                   cadence,
                   day_of_week,
                   day_of_month,
                   month_of_year,
                   local_time,
                   timezone_name,
                   export_format,
                   delivery_boundary,
                   email_subject,
                   email_message,
                   enabled,
                   next_run_at,
                   last_started_at,
                   last_completed_at,
                   last_status,
                   version,
                   created_at,
                   updated_at
            FROM analytics_report_schedules
            WHERE (@broad OR owner_actual_user_id = @actual_user_id)
              AND (@include_disabled OR enabled = TRUE)
            ORDER BY enabled DESC, next_run_at NULLS LAST, schedule_name;
            """, connection))
        {
            command.Parameters.AddWithValue("broad", actor.Broad);
            command.Parameters.AddWithValue("actual_user_id", actor.ActualUserId);
            command.Parameters.AddWithValue("include_disabled", includeDisabled);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                schedules.Add(ReadSchedule(reader, Array.Empty<AnalyticsScheduleRecipient>()));
        }

        if (schedules.Count == 0) return Array.Empty<AnalyticsSchedule>();
        var recipients = await LoadRecipientsAsync(
            connection,
            schedules.Select(schedule => schedule.ScheduleId).ToArray(),
            cancellationToken);
        return schedules.Select(schedule => schedule with
        {
            Recipients = recipients.TryGetValue(schedule.ScheduleId, out var rows)
                ? rows.ToArray()
                : Array.Empty<AnalyticsScheduleRecipient>()
        }).ToArray();
    }

    internal static async Task<AnalyticsSchedule?> LoadScheduleAsync(
        NpgsqlConnection connection,
        Guid scheduleId,
        FinancialOperationsActor? actor,
        CancellationToken cancellationToken)
    {
        AnalyticsSchedule? schedule = null;
        await using (var command = new NpgsqlCommand("""
            SELECT analytics_report_schedule_id,
                   owner_actual_user_id,
                   owner_effective_user_id,
                   schedule_name,
                   report_code,
                   criteria_json::text,
                   cadence,
                   day_of_week,
                   day_of_month,
                   month_of_year,
                   local_time,
                   timezone_name,
                   export_format,
                   delivery_boundary,
                   email_subject,
                   email_message,
                   enabled,
                   next_run_at,
                   last_started_at,
                   last_completed_at,
                   last_status,
                   version,
                   created_at,
                   updated_at
            FROM analytics_report_schedules
            WHERE analytics_report_schedule_id = @schedule_id
              AND (@ignore_scope OR @broad OR owner_actual_user_id = @actual_user_id)
            LIMIT 1;
            """, connection))
        {
            command.Parameters.AddWithValue("schedule_id", scheduleId);
            command.Parameters.AddWithValue("ignore_scope", actor is null);
            command.Parameters.AddWithValue("broad", actor?.Broad ?? false);
            command.Parameters.AddWithValue("actual_user_id", actor?.ActualUserId ?? Guid.Empty);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                schedule = ReadSchedule(reader, Array.Empty<AnalyticsScheduleRecipient>());
        }
        if (schedule is null) return null;
        var recipients = await LoadRecipientsAsync(connection, [scheduleId], cancellationToken);
        return schedule with
        {
            Recipients = recipients.TryGetValue(scheduleId, out var rows)
                ? rows.ToArray()
                : Array.Empty<AnalyticsScheduleRecipient>()
        };
    }

    internal static async Task<AnalyticsSchedule[]> LoadDueSchedulesAsync(
        NpgsqlConnection connection,
        DateTimeOffset utcNow,
        int limit,
        CancellationToken cancellationToken)
    {
        var schedules = new List<AnalyticsSchedule>();
        await using (var command = new NpgsqlCommand("""
            SELECT analytics_report_schedule_id,
                   owner_actual_user_id,
                   owner_effective_user_id,
                   schedule_name,
                   report_code,
                   criteria_json::text,
                   cadence,
                   day_of_week,
                   day_of_month,
                   month_of_year,
                   local_time,
                   timezone_name,
                   export_format,
                   delivery_boundary,
                   email_subject,
                   email_message,
                   enabled,
                   next_run_at,
                   last_started_at,
                   last_completed_at,
                   last_status,
                   version,
                   created_at,
                   updated_at
            FROM analytics_report_schedules
            WHERE enabled = TRUE
              AND next_run_at IS NOT NULL
              AND next_run_at <= @utc_now
            ORDER BY next_run_at
            LIMIT @limit;
            """, connection))
        {
            command.Parameters.AddWithValue("utc_now", utcNow);
            command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 100));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                schedules.Add(ReadSchedule(reader, Array.Empty<AnalyticsScheduleRecipient>()));
        }
        if (schedules.Count == 0) return Array.Empty<AnalyticsSchedule>();
        var recipients = await LoadRecipientsAsync(
            connection,
            schedules.Select(schedule => schedule.ScheduleId).ToArray(),
            cancellationToken);
        return schedules.Select(schedule => schedule with
        {
            Recipients = recipients.TryGetValue(schedule.ScheduleId, out var rows)
                ? rows.ToArray()
                : Array.Empty<AnalyticsScheduleRecipient>()
        }).ToArray();
    }

    internal static async Task<Guid> SaveScheduleAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        AnalyticsScheduleUpsertRequest request,
        AnalyticsExperienceRequest criteria,
        AnalyticsScheduleRecipientRequest[] recipients,
        DateTimeOffset? nextRunAt,
        CancellationToken cancellationToken)
    {
        var scheduleId = request.ScheduleId.GetValueOrDefault(Guid.NewGuid());
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (request.ScheduleId.HasValue)
            {
                await using var ownership = new NpgsqlCommand("""
                    SELECT EXISTS (
                        SELECT 1
                        FROM analytics_report_schedules
                        WHERE analytics_report_schedule_id = @schedule_id
                          AND (@broad OR owner_actual_user_id = @actual_user_id)
                    );
                    """, connection, transaction);
                ownership.Parameters.AddWithValue("schedule_id", scheduleId);
                ownership.Parameters.AddWithValue("broad", actor.Broad);
                ownership.Parameters.AddWithValue("actual_user_id", actor.ActualUserId);
                if (!Convert.ToBoolean(await ownership.ExecuteScalarAsync(cancellationToken) ?? false))
                    throw new UnauthorizedAccessException("The schedule was not found in the current user's scope.");
            }

            await using (var command = new NpgsqlCommand("""
                INSERT INTO analytics_report_schedules (
                    analytics_report_schedule_id,
                    owner_actual_user_id,
                    owner_effective_user_id,
                    schedule_name,
                    report_code,
                    criteria_json,
                    cadence,
                    day_of_week,
                    day_of_month,
                    month_of_year,
                    local_time,
                    timezone_name,
                    export_format,
                    delivery_boundary,
                    email_subject,
                    email_message,
                    enabled,
                    next_run_at,
                    last_status,
                    version,
                    created_at,
                    updated_at
                )
                VALUES (
                    @schedule_id,
                    @owner_actual_user_id,
                    @owner_effective_user_id,
                    @schedule_name,
                    @report_code,
                    @criteria_json::jsonb,
                    @cadence,
                    @day_of_week,
                    @day_of_month,
                    @month_of_year,
                    @local_time,
                    @timezone_name,
                    @export_format,
                    @delivery_boundary,
                    @email_subject,
                    @email_message,
                    @enabled,
                    @next_run_at,
                    'not_run',
                    1,
                    NOW(),
                    NOW()
                )
                ON CONFLICT (analytics_report_schedule_id)
                DO UPDATE SET
                    schedule_name = EXCLUDED.schedule_name,
                    report_code = EXCLUDED.report_code,
                    criteria_json = EXCLUDED.criteria_json,
                    cadence = EXCLUDED.cadence,
                    day_of_week = EXCLUDED.day_of_week,
                    day_of_month = EXCLUDED.day_of_month,
                    month_of_year = EXCLUDED.month_of_year,
                    local_time = EXCLUDED.local_time,
                    timezone_name = EXCLUDED.timezone_name,
                    export_format = EXCLUDED.export_format,
                    delivery_boundary = EXCLUDED.delivery_boundary,
                    email_subject = EXCLUDED.email_subject,
                    email_message = EXCLUDED.email_message,
                    enabled = EXCLUDED.enabled,
                    next_run_at = EXCLUDED.next_run_at,
                    version = analytics_report_schedules.version + 1,
                    updated_at = NOW();
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("schedule_id", scheduleId);
                command.Parameters.AddWithValue("owner_actual_user_id", actor.ActualUserId);
                command.Parameters.AddWithValue("owner_effective_user_id", actor.EffectiveUserId);
                command.Parameters.AddWithValue("schedule_name", Clean(request.ScheduleName, 180, "Analytics schedule"));
                command.Parameters.AddWithValue("report_code", Clean(request.ReportCode, 120, criteria.ReportCode ?? string.Empty));
                command.Parameters.AddWithValue("criteria_json", JsonSerializer.Serialize(criteria));
                command.Parameters.AddWithValue("cadence", NormalizeCadence(request.Cadence));
                AddNullable(command, "day_of_week", NpgsqlDbType.Smallint, request.DayOfWeek);
                AddNullable(command, "day_of_month", NpgsqlDbType.Smallint, request.DayOfMonth);
                AddNullable(command, "month_of_year", NpgsqlDbType.Smallint, request.MonthOfYear);
                command.Parameters.AddWithValue("local_time", request.LocalTime ?? new TimeOnly(8, 0));
                command.Parameters.AddWithValue("timezone_name", Clean(request.TimezoneName, 100, "America/New_York"));
                command.Parameters.AddWithValue("export_format", NormalizeExportFormat(request.ExportFormat));
                command.Parameters.AddWithValue("delivery_boundary", NormalizeDeliveryBoundary(request.DeliveryBoundary));
                command.Parameters.AddWithValue("email_subject", Clean(request.EmailSubject, 500, string.Empty));
                command.Parameters.AddWithValue("email_message", Clean(request.EmailMessage, 10000, string.Empty));
                command.Parameters.AddWithValue("enabled", request.Enabled ?? true);
                AddNullable(command, "next_run_at", NpgsqlDbType.TimestampTz, nextRunAt);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteRecipients = new NpgsqlCommand("""
                DELETE FROM analytics_report_schedule_recipients
                WHERE analytics_report_schedule_id = @schedule_id;
                """, connection, transaction))
            {
                deleteRecipients.Parameters.AddWithValue("schedule_id", scheduleId);
                await deleteRecipients.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var recipient in recipients)
            {
                await using var insertRecipient = new NpgsqlCommand("""
                    INSERT INTO analytics_report_schedule_recipients (
                        analytics_report_schedule_recipient_id,
                        analytics_report_schedule_id,
                        recipient_user_id,
                        recipient_name,
                        recipient_email,
                        recipient_type,
                        created_at
                    )
                    VALUES (
                        gen_random_uuid(),
                        @schedule_id,
                        @recipient_user_id,
                        @recipient_name,
                        @recipient_email,
                        @recipient_type,
                        NOW()
                    );
                    """, connection, transaction);
                insertRecipient.Parameters.AddWithValue("schedule_id", scheduleId);
                AddNullable(insertRecipient, "recipient_user_id", NpgsqlDbType.Uuid, recipient.UserId);
                insertRecipient.Parameters.AddWithValue("recipient_name", Clean(recipient.DisplayName, 240, string.Empty));
                insertRecipient.Parameters.AddWithValue("recipient_email", Clean(recipient.Email, 320, string.Empty).ToLowerInvariant());
                insertRecipient.Parameters.AddWithValue("recipient_type", NormalizeRecipientType(recipient.RecipientType));
                await insertRecipient.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return scheduleId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    internal static async Task<bool> DeleteScheduleAsync(
        NpgsqlConnection connection,
        Guid scheduleId,
        FinancialOperationsActor actor,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            DELETE FROM analytics_report_schedules
            WHERE analytics_report_schedule_id = @schedule_id
              AND (@broad OR owner_actual_user_id = @actual_user_id);
            """, connection);
        command.Parameters.AddWithValue("schedule_id", scheduleId);
        command.Parameters.AddWithValue("broad", actor.Broad);
        command.Parameters.AddWithValue("actual_user_id", actor.ActualUserId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    internal static async Task UpdateScheduleAfterRunAsync(
        NpgsqlConnection connection,
        Guid scheduleId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        DateTimeOffset? nextRunAt,
        string status,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE analytics_report_schedules
            SET last_started_at = @started_at,
                last_completed_at = @completed_at,
                last_status = @status,
                next_run_at = @next_run_at,
                updated_at = NOW()
            WHERE analytics_report_schedule_id = @schedule_id;
            """, connection);
        command.Parameters.AddWithValue("schedule_id", scheduleId);
        command.Parameters.AddWithValue("started_at", startedAt);
        command.Parameters.AddWithValue("completed_at", completedAt);
        command.Parameters.AddWithValue("status", Clean(status, 40, "failed"));
        AddNullable(command, "next_run_at", NpgsqlDbType.TimestampTz, nextRunAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<Guid> InsertScheduleRunAsync(
        NpgsqlConnection connection,
        AnalyticsSchedule schedule,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        int recipientCount,
        int sentCount,
        int queuedCount,
        int failedCount,
        string diagnosticCode,
        string diagnosticMessage,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO analytics_report_schedule_runs (
                analytics_report_schedule_run_id,
                analytics_report_schedule_id,
                schedule_name,
                report_code,
                owner_actual_user_id,
                started_at,
                completed_at,
                run_status,
                recipient_count,
                sent_count,
                queued_count,
                failed_count,
                diagnostic_code,
                diagnostic_message,
                created_at
            )
            VALUES (
                @run_id,
                @schedule_id,
                @schedule_name,
                @report_code,
                @owner_actual_user_id,
                @started_at,
                @completed_at,
                @run_status,
                @recipient_count,
                @sent_count,
                @queued_count,
                @failed_count,
                @diagnostic_code,
                @diagnostic_message,
                NOW()
            );
            """, connection);
        command.Parameters.AddWithValue("run_id", id);
        command.Parameters.AddWithValue("schedule_id", schedule.ScheduleId);
        command.Parameters.AddWithValue("schedule_name", schedule.ScheduleName);
        command.Parameters.AddWithValue("report_code", schedule.ReportCode);
        command.Parameters.AddWithValue("owner_actual_user_id", schedule.OwnerActualUserId);
        command.Parameters.AddWithValue("started_at", startedAt);
        command.Parameters.AddWithValue("completed_at", completedAt);
        command.Parameters.AddWithValue("run_status", Clean(status, 40, "failed"));
        command.Parameters.AddWithValue("recipient_count", recipientCount);
        command.Parameters.AddWithValue("sent_count", sentCount);
        command.Parameters.AddWithValue("queued_count", queuedCount);
        command.Parameters.AddWithValue("failed_count", failedCount);
        command.Parameters.AddWithValue("diagnostic_code", Clean(diagnosticCode, 120, string.Empty));
        command.Parameters.AddWithValue("diagnostic_message", Clean(diagnosticMessage, 10000, string.Empty));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    internal static async Task InsertDeliveryEvidenceAsync(
        NpgsqlConnection connection,
        Guid scheduleRunId,
        Guid? reportRunId,
        AnalyticsScheduleRecipient recipient,
        string exportFormat,
        string contentSha256,
        Module065MailDeliveryResult delivery,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO analytics_report_schedule_delivery_attempts (
                analytics_report_schedule_delivery_attempt_id,
                analytics_report_schedule_run_id,
                enterprise_report_run_id,
                recipient_user_id,
                recipient_email,
                export_format,
                content_sha256,
                delivery_status,
                provider_source,
                provider_message_id,
                diagnostic_code,
                diagnostic_message,
                created_at
            )
            VALUES (
                gen_random_uuid(),
                @schedule_run_id,
                @report_run_id,
                @recipient_user_id,
                @recipient_email,
                @export_format,
                @content_sha256,
                @delivery_status,
                @provider_source,
                @provider_message_id,
                @diagnostic_code,
                @diagnostic_message,
                NOW()
            );
            """, connection);
        command.Parameters.AddWithValue("schedule_run_id", scheduleRunId);
        AddNullable(command, "report_run_id", NpgsqlDbType.Uuid, reportRunId);
        AddNullable(command, "recipient_user_id", NpgsqlDbType.Uuid, recipient.UserId);
        command.Parameters.AddWithValue("recipient_email", recipient.Email);
        command.Parameters.AddWithValue("export_format", NormalizeExportFormat(exportFormat));
        command.Parameters.AddWithValue("content_sha256", contentSha256 ?? string.Empty);
        command.Parameters.AddWithValue("delivery_status", NormalizeDeliveryStatus(delivery.Status));
        command.Parameters.AddWithValue("provider_source", Clean(delivery.Provider, 40, "module_065"));
        command.Parameters.AddWithValue("provider_message_id", Clean(delivery.ProviderMessageId, 4000, string.Empty));
        command.Parameters.AddWithValue("diagnostic_code", Clean(delivery.DiagnosticCode, 120, string.Empty));
        command.Parameters.AddWithValue("diagnostic_message", Clean(delivery.Message, 10000, string.Empty));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<AnalyticsScheduleRun[]> LoadScheduleRunsAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<AnalyticsScheduleRun>();
        await using var command = new NpgsqlCommand("""
            SELECT analytics_report_schedule_run_id,
                   analytics_report_schedule_id,
                   schedule_name,
                   report_code,
                   owner_actual_user_id,
                   started_at,
                   completed_at,
                   run_status,
                   recipient_count,
                   sent_count,
                   queued_count,
                   failed_count,
                   diagnostic_code,
                   diagnostic_message,
                   created_at
            FROM analytics_report_schedule_runs
            WHERE @broad OR owner_actual_user_id = @actual_user_id
            ORDER BY created_at DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("broad", actor.Broad);
        command.Parameters.AddWithValue("actual_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AnalyticsScheduleRun(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                ReadDateTimeOffset(reader, 5),
                ReadDateTimeOffset(reader, 6),
                reader.GetString(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetString(12),
                reader.GetString(13),
                ReadDateTimeOffset(reader, 14)));
        }
        return rows.ToArray();
    }

    internal static async Task<AnalyticsScheduleDeliveryEvidence[]> LoadDeliveryEvidenceAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<AnalyticsScheduleDeliveryEvidence>();
        await using var command = new NpgsqlCommand("""
            SELECT attempt.analytics_report_schedule_delivery_attempt_id,
                   attempt.analytics_report_schedule_run_id,
                   attempt.enterprise_report_run_id,
                   attempt.recipient_user_id,
                   attempt.recipient_email,
                   attempt.export_format,
                   attempt.content_sha256,
                   attempt.delivery_status,
                   attempt.provider_source,
                   attempt.provider_message_id,
                   attempt.diagnostic_code,
                   attempt.diagnostic_message,
                   attempt.created_at
            FROM analytics_report_schedule_delivery_attempts attempt
            JOIN analytics_report_schedule_runs run
              ON run.analytics_report_schedule_run_id = attempt.analytics_report_schedule_run_id
            WHERE @broad OR run.owner_actual_user_id = @actual_user_id
            ORDER BY attempt.created_at DESC
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("broad", actor.Broad);
        command.Parameters.AddWithValue("actual_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AnalyticsScheduleDeliveryEvidence(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                ReadDateTimeOffset(reader, 12)));
        }
        return rows.ToArray();
    }

    internal static async Task<AnalyticsRecipientOption[]> LoadRecipientOptionsAsync(
        NpgsqlConnection connection,
        FinancialOperationsActor actor,
        bool canDeliverMultiple,
        string search,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = new List<AnalyticsRecipientOption>();
        await using var command = new NpgsqlCommand("""
            SELECT user_id,
                   COALESCE(display_name, email, ''),
                   COALESCE(email, ''),
                   COALESCE(job_title, '')
            FROM app_users
            WHERE is_active = TRUE
              AND COALESCE(email, '') <> ''
              AND (
                    @multiple
                    OR user_id = @effective_user_id
              )
              AND (
                    @search = ''
                    OR COALESCE(display_name, '') ILIKE '%' || @search || '%'
                    OR COALESCE(email, '') ILIKE '%' || @search || '%'
                    OR COALESCE(job_title, '') ILIKE '%' || @search || '%'
              )
            ORDER BY COALESCE(display_name, email)
            LIMIT @limit;
            """, connection);
        command.Parameters.AddWithValue("multiple", canDeliverMultiple);
        command.Parameters.AddWithValue("effective_user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("search", Clean(search, 120, string.Empty));
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AnalyticsRecipientOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                "active_projectpulse_user",
                !canDeliverMultiple));
        }
        return rows.ToArray();
    }

    internal static async Task UpsertActivityAsync(
        NpgsqlConnection connection,
        Guid userId,
        string reportCode,
        bool incrementView,
        bool? favorite,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO analytics_user_report_activity (
                analytics_user_report_activity_id,
                user_id,
                report_code,
                is_favorite,
                view_count,
                last_viewed_at,
                created_at,
                updated_at
            )
            VALUES (
                gen_random_uuid(),
                @user_id,
                @report_code,
                COALESCE(@favorite, FALSE),
                CASE WHEN @increment_view THEN 1 ELSE 0 END,
                CASE WHEN @increment_view THEN NOW() ELSE NULL END,
                NOW(),
                NOW()
            )
            ON CONFLICT (user_id, report_code)
            DO UPDATE SET
                is_favorite = COALESCE(@favorite, analytics_user_report_activity.is_favorite),
                view_count = analytics_user_report_activity.view_count
                    + CASE WHEN @increment_view THEN 1 ELSE 0 END,
                last_viewed_at = CASE WHEN @increment_view THEN NOW()
                    ELSE analytics_user_report_activity.last_viewed_at END,
                updated_at = NOW();
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("report_code", Clean(reportCode, 120, string.Empty));
        command.Parameters.AddWithValue("increment_view", incrementView);
        command.Parameters.Add("favorite", NpgsqlDbType.Boolean).Value = favorite.HasValue
            ? favorite.Value
            : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<Dictionary<string, (bool Favorite, int ViewCount, DateTimeOffset? LastViewedAt)>>
        LoadActivityAsync(
            NpgsqlConnection connection,
            Guid userId,
            CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, (bool, int, DateTimeOffset?)>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT report_code, is_favorite, view_count, last_viewed_at
            FROM analytics_user_report_activity
            WHERE user_id = @user_id
            ORDER BY is_favorite DESC, last_viewed_at DESC NULLS LAST;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = (
                reader.GetBoolean(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : ReadDateTimeOffset(reader, 3));
        }
        return result;
    }

    private static async Task<Dictionary<Guid, List<AnalyticsScheduleRecipient>>> LoadRecipientsAsync(
        NpgsqlConnection connection,
        Guid[] scheduleIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, List<AnalyticsScheduleRecipient>>();
        if (scheduleIds.Length == 0) return result;
        await using var command = new NpgsqlCommand("""
            SELECT analytics_report_schedule_recipient_id,
                   analytics_report_schedule_id,
                   recipient_user_id,
                   recipient_name,
                   recipient_email,
                   recipient_type
            FROM analytics_report_schedule_recipients
            WHERE analytics_report_schedule_id = ANY(@schedule_ids)
            ORDER BY analytics_report_schedule_id, recipient_type, recipient_name, recipient_email;
            """, connection);
        command.Parameters.Add(new NpgsqlParameter(
            "schedule_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = scheduleIds
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new AnalyticsScheduleRecipient(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5));
            if (!result.TryGetValue(row.ScheduleId, out var list))
            {
                list = new List<AnalyticsScheduleRecipient>();
                result[row.ScheduleId] = list;
            }
            list.Add(row);
        }
        return result;
    }

    private static AnalyticsSchedule ReadSchedule(
        NpgsqlDataReader reader,
        AnalyticsScheduleRecipient[] recipients) => new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            ParseJson(reader.GetString(5)),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt16(7),
            reader.IsDBNull(8) ? null : reader.GetInt16(8),
            reader.IsDBNull(9) ? null : reader.GetInt16(9),
            ReadTimeOnly(reader, 10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetBoolean(16),
            reader.IsDBNull(17) ? null : ReadDateTimeOffset(reader, 17),
            reader.IsDBNull(18) ? null : ReadDateTimeOffset(reader, 18),
            reader.IsDBNull(19) ? null : ReadDateTimeOffset(reader, 19),
            reader.GetString(20),
            reader.GetInt32(21),
            ReadDateTimeOffset(reader, 22),
            ReadDateTimeOffset(reader, 23),
            recipients);

    private static JsonElement ParseJson(string value)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
        return document.RootElement.Clone();
    }

    private static TimeOnly ReadTimeOnly(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            TimeOnly time => time,
            TimeSpan span => TimeOnly.FromTimeSpan(span),
            _ => TimeOnly.Parse(value.ToString() ?? "08:00")
        };
    }

    private static DateTimeOffset ReadDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        object? value) => command.Parameters.Add(name, type).Value = value ?? DBNull.Value;

    internal static string NormalizeCadence(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "daily" => "daily",
            "weekdays" or "business_days" => "weekdays",
            "monthly" => "monthly",
            "quarterly" => "quarterly",
            "yearly" or "annual" => "yearly",
            _ => "weekly"
        };

    internal static string NormalizeExportFormat(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() == "xlsx" ? "xlsx" : "pdf";

    internal static string NormalizeDeliveryBoundary(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "production_governed" => "production_governed",
            "locked" => "locked",
            _ => "test_only"
        };

    private static string NormalizeRecipientType(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cc" => "cc",
            "bcc" => "bcc",
            _ => "to"
        };

    private static string NormalizeDeliveryStatus(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sent" => "sent",
            "queued" => "queued",
            "suppressed" => "suppressed",
            _ => "failed"
        };

    private static string Clean(string? value, int maximum, string fallback)
    {
        var clean = (value ?? string.Empty).Replace('\0', ' ').Trim();
        if (string.IsNullOrWhiteSpace(clean)) clean = fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }
}
