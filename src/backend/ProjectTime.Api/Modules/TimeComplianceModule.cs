using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;
using System.Runtime.InteropServices;

namespace ProjectTime.Api.Modules;

public static class TimeComplianceModule
{
    private const string DefaultScenario = "weekly_reminder";

    public static WebApplication MapTimeComplianceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/time-compliance/settings", GetSettingsAsync);
        app.MapGet("/api/time-compliance/preview", GetPreviewAsync);
        app.MapPost("/api/time-compliance/dry-run", CreateDryRunAsync);
        app.MapGet("/api/time-compliance/history", GetHistoryAsync);

        return app;
    }

    private static async Task<IResult> GetSettingsAsync()
    {
        var config = TimeComplianceDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var rules = await LoadReminderRulesAsync(connection);
        var coordinator = await LoadProjectTeamCoordinatorAsync(connection);
        var holidays = await LoadHolidayReminderWindowsAsync(connection);

        return Results.Ok(new
        {
            mode = "dry_run_only",
            permission = "VIEW_TIME_COMPLIANCE",
            futureManagePermission = "MANAGE_TIME_COMPLIANCE_NOTIFICATIONS",
            defaults = new
            {
                weeklyReminder = new
                {
                    ruleCode = "WEEKLY_ENGINEER_TIME_REMINDER",
                    defaultDay = "Monday",
                    defaultTimeCentral = "06:00",
                    dryRunRequired = true
                },
                weeklyEscalation = new
                {
                    ruleCode = "WEEKLY_ENGINEER_TIME_ESCALATION",
                    defaultDay = "Monday",
                    defaultTimeCentral = "08:00",
                    dryRunRequired = true
                },
                monthEnd = new
                {
                    ruleCode = "MONTH_END_PM_REMINDER",
                    selectedLastWeekday = "Friday",
                    allowedLastWeekdayOptions = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" }
                },
                holidayReminderOffsets = new[] { 7, 1 }
            },
            projectTeamCoordinator = coordinator,
            reminderRules = rules,
            holidayReminderWindows = holidays,
            guardrails = new[]
            {
                "Notification preview mode is enforced; this module does not send email.",
                "Preview must be reviewed before real-send functionality is introduced.",
                "Manager CC uses reporting_relationships first and app_users.manager_email as fallback.",
                "Project Team Coordinator is loaded only from trusted database records."
            }
        });
    }

    private static async Task<IResult> GetPreviewAsync(DateOnly? weekStart, string? scenario)
    {
        var config = TimeComplianceDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        var start = GetSundayForDate(weekStart ?? DateOnly.FromDateTime(DateTime.UtcNow));
        var normalizedScenario = string.IsNullOrWhiteSpace(scenario) ? DefaultScenario : scenario.Trim();

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var payload = await BuildPreviewPayloadAsync(connection, start, normalizedScenario);
        return Results.Ok(payload);
    }

    private static async Task<IResult> CreateDryRunAsync(TimeComplianceDryRunRequest request)
    {
        var config = TimeComplianceDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        var start = GetSundayForDate(request.WeekStart ?? DateOnly.FromDateTime(DateTime.UtcNow));
        var scenario = string.IsNullOrWhiteSpace(request.Scenario) ? DefaultScenario : request.Scenario.Trim();

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var payload = await BuildPreviewPayloadAsync(connection, start, scenario);

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var queued = 0;

            foreach (var item in payload.MissingSubmissions)
            {
                const string outboxSql = """
                    INSERT INTO notification_outbox (
                        notification_type,
                        recipient_email,
                        cc_email,
                        subject,
                        body,
                        status,
                        related_entity_type,
                        related_entity_id
                    )
                    VALUES (
                        @notification_type,
                        @recipient_email,
                        @cc_email,
                        @subject,
                        @body,
                        'dry_run',
                        'app_user',
                        @related_entity_id
                    );
                    """;

                await using var command = new NpgsqlCommand(outboxSql, connection, transaction);
                command.Parameters.AddWithValue("notification_type", $"time_compliance_{scenario}_dry_run");
                command.Parameters.AddWithValue("recipient_email", item.Email);
                var ccParameter = command.Parameters.Add("cc_email", NpgsqlDbType.Array | NpgsqlDbType.Text);
                ccParameter.Value = item.CcEmails.Length == 0 ? DBNull.Value : item.CcEmails;
                command.Parameters.AddWithValue("subject", item.Subject);
                command.Parameters.AddWithValue("body", item.Body);
                command.Parameters.AddWithValue("related_entity_id", item.UserId);

                await command.ExecuteNonQueryAsync();
                queued++;
            }

            const string auditSql = """
                INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id)
                VALUES (NULL, @action, 'time_compliance_preview', NULL);
                """;

            await using (var auditCommand = new NpgsqlCommand(auditSql, connection, transaction))
            {
                auditCommand.Parameters.AddWithValue("action", $"time_compliance_{scenario}_dry_run_created");
                await auditCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            return Results.Ok(new
            {
                status = "dry_run_created",
                previewOnly = true,
                queuedNotifications = queued,
                weekStart = payload.WeekStart,
                weekEnd = payload.WeekEnd,
                scenario,
                message = "Notification preview records were created. No email was sent.",
                preview = payload
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Results.Problem(
                title: "Failed to create notification preview records",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetHistoryAsync(int? limit)
    {
        var config = TimeComplianceDatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        var take = Math.Clamp(limit ?? 50, 1, 200);
        var outbox = new List<object>();
        var audits = new List<object>();

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        const string outboxSql = """
            SELECT
                notification_outbox_id,
                notification_type,
                recipient_email,
                cc_email,
                subject,
                status,
                related_entity_type,
                related_entity_id,
                created_at,
                sent_at,
                error_message
            FROM notification_outbox
            WHERE notification_type LIKE 'time_compliance_%'
            ORDER BY created_at DESC
            LIMIT @limit;
            """;

        await using (var command = new NpgsqlCommand(outboxSql, connection))
        {
            command.Parameters.AddWithValue("limit", take);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                outbox.Add(new
                {
                    id = reader.GetGuid(0),
                    notificationType = reader.GetString(1),
                    recipientEmail = reader.GetString(2),
                    ccEmails = reader.IsDBNull(3) ? Array.Empty<string>() : reader.GetFieldValue<string[]>(3),
                    subject = reader.GetString(4),
                    status = reader.GetString(5),
                    relatedEntityType = reader.IsDBNull(6) ? null : reader.GetString(6),
                    relatedEntityId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
                    createdAt = reader.GetFieldValue<DateTimeOffset>(8),
                    sentAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(9),
                    errorMessage = reader.IsDBNull(10) ? null : reader.GetString(10)
                });
            }
        }

        const string auditSql = """
            SELECT audit_log_id, action, entity_type, entity_id, created_at
            FROM audit_logs
            WHERE action LIKE 'time_compliance_%'
            ORDER BY created_at DESC
            LIMIT @limit;
            """;

        await using (var command = new NpgsqlCommand(auditSql, connection))
        {
            command.Parameters.AddWithValue("limit", take);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                audits.Add(new
                {
                    id = reader.GetGuid(0),
                    action = reader.GetString(1),
                    entityType = reader.GetString(2),
                    entityId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
                    createdAt = reader.GetFieldValue<DateTimeOffset>(4)
                });
            }
        }

        return Results.Ok(new
        {
            count = outbox.Count,
            dryRunNotifications = outbox,
            auditEvents = audits
        });
    }

    private static async Task<TimeCompliancePreviewPayload> BuildPreviewPayloadAsync(NpgsqlConnection connection, DateOnly weekStart, string scenario)
    {
        var weekEnd = weekStart.AddDays(6);
        var coordinator = await LoadProjectTeamCoordinatorAsync(connection);
        var missingSubmissions = new List<TimeCompliancePreviewItem>();

        const string sql = """
            WITH timesheet_totals AS (
                SELECT timesheet_id, SUM(hours) AS total_hours
                FROM time_entries
                GROUP BY timesheet_id
            )
            SELECT
                u.user_id,
                u.email,
                u.display_name,
                u.job_title,
                COALESCE(NULLIF(u.department_name, ''), NULLIF(u.department, '')) AS department_name,
                u.team_name,
                COALESCE(manager.email, NULLIF(u.manager_email, '')) AS manager_email,
                manager.display_name AS manager_name,
                ts.timesheet_id,
                ts.status,
                COALESCE(tt.total_hours, 0) AS total_hours
            FROM app_users u
            LEFT JOIN reporting_relationships rr
                ON rr.employee_user_id = u.user_id
               AND rr.effective_start_date <= @week_end
               AND (rr.effective_end_date IS NULL OR rr.effective_end_date >= @week_start)
            LEFT JOIN app_users manager
                ON manager.user_id = rr.manager_user_id
            LEFT JOIN timesheets ts
                ON ts.user_id = u.user_id
               AND ts.week_start_date = @week_start
            LEFT JOIN timesheet_totals tt
                ON tt.timesheet_id = ts.timesheet_id
            WHERE u.is_active = TRUE
              AND u.login_enabled = TRUE
              AND LOWER(COALESCE(u.job_title, '')) NOT LIKE '%manager%'
              AND (
                    LOWER(COALESCE(u.job_title, '')) LIKE '%engineer%'
                 OR LOWER(COALESCE(u.department_name, '')) LIKE '%engineering%'
                 OR LOWER(COALESCE(u.department, '')) LIKE '%engineering%'
                 OR LOWER(COALESCE(u.team_name, '')) LIKE '%systems%'
                 OR LOWER(COALESCE(u.team_name, '')) LIKE '%collaboration%'
                 OR LOWER(COALESCE(u.team_name, '')) LIKE '%network%'
              )
              AND (
                    ts.timesheet_id IS NULL
                 OR ts.status NOT IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked')
              )
            ORDER BY u.display_name, u.email;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("week_start", weekStart);
        command.Parameters.AddWithValue("week_end", weekEnd);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var userId = reader.GetGuid(0);
            var email = reader.GetString(1);
            var displayName = reader.GetString(2);
            var jobTitle = reader.IsDBNull(3) ? null : reader.GetString(3);
            var department = reader.IsDBNull(4) ? null : reader.GetString(4);
            var teamName = reader.IsDBNull(5) ? null : reader.GetString(5);
            var managerEmail = reader.IsDBNull(6) ? null : reader.GetString(6);
            var managerName = reader.IsDBNull(7) ? null : reader.GetString(7);
            var timesheetId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8);
            var timesheetStatus = reader.IsDBNull(9) ? "missing" : reader.GetString(9);
            var totalHours = reader.GetDecimal(10);

            var ccEmails = new List<string>();
            var gaps = new List<string>();

            if (!string.IsNullOrWhiteSpace(managerEmail))
            {
                ccEmails.Add(managerEmail);
            }
            else
            {
                gaps.Add("Manager CC missing: reporting_relationships and app_users.manager_email do not provide a manager.");
            }

            if (coordinator is not null)
            {
                ccEmails.Add(coordinator.Email);
            }
            else
            {
                gaps.Add("Project Team Coordinator CC missing: no trusted active coordinator record was found.");
            }

            var scenarioLabel = scenario.Replace('_', ' ');
            var subject = scenario.Contains("escalation", StringComparison.OrdinalIgnoreCase)
                ? $"Escalation: Missing Project Pulse time for week of {weekStart:yyyy-MM-dd}"
                : $"Reminder: Submit Project Pulse time for week of {weekStart:yyyy-MM-dd}";

            var body = $"""
                Notification preview only. No email was sent.

                Project Pulse shows missing or unsubmitted time for {displayName} for the week of {weekStart:yyyy-MM-dd} through {weekEnd:yyyy-MM-dd}.

                Current timesheet status: {timesheetStatus}
                Current recorded hours: {totalHours:0.00}

                Scenario: {scenarioLabel}
                Required next step: Review and submit time in Project Pulse.
                """;

            missingSubmissions.Add(new TimeCompliancePreviewItem(
                userId,
                email,
                displayName,
                jobTitle,
                department,
                teamName,
                managerEmail,
                managerName,
                timesheetId,
                timesheetStatus,
                totalHours,
                ccEmails.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                gaps.ToArray(),
                subject,
                body));
        }

        await reader.CloseAsync();

        var reminderRules = await LoadReminderRulesAsync(connection);
        var holidayWindows = await LoadHolidayReminderWindowsAsync(connection);

        return new TimeCompliancePreviewPayload(
            true,
            scenario,
            DateTimeOffset.UtcNow,
            weekStart,
            weekEnd,
            new
            {
                weeklyReminderDefault = "Monday 6:00 AM Central",
                weeklyEscalationDefault = "Monday 8:00 AM Central",
                monthEndOptions = new[] { "Last Monday", "Last Tuesday", "Last Wednesday", "Last Thursday", "Last Friday" },
                holidayReminderOffsets = new[] { "7 days before weekday holidays", "1 day before weekday holidays" }
            },
            coordinator,
            reminderRules,
            holidayWindows,
            missingSubmissions,
            new
            {
                missingSubmissionCount = missingSubmissions.Count,
                managerCcGapCount = missingSubmissions.Count(item => item.ComplianceGaps.Any(gap => gap.StartsWith("Manager CC", StringComparison.OrdinalIgnoreCase))),
                coordinatorCcConfigured = coordinator is not null,
                previewOnly = true
            });
    }

    private static async Task<IReadOnlyList<object>> LoadReminderRulesAsync(NpgsqlConnection connection)
    {
        var rules = new List<object>();

        const string sql = """
            SELECT rule_code, rule_name, recipient_group_code, rule_type, cadence_description, subject_template, is_active, updated_at
            FROM reminder_rules
            WHERE rule_code IN (
                'WEEKLY_ENGINEER_TIME_REMINDER',
                'WEEKLY_ENGINEER_TIME_ESCALATION',
                'MONTH_END_PM_REMINDER',
                'HOLIDAY_TIME_REMINDER_7_DAY',
                'HOLIDAY_TIME_REMINDER_1_DAY'
            )
            ORDER BY rule_code;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rules.Add(new
            {
                ruleCode = reader.GetString(0),
                ruleName = reader.GetString(1),
                recipientGroupCode = reader.GetString(2),
                ruleType = reader.GetString(3),
                cadenceDescription = reader.GetString(4),
                subjectTemplate = reader.GetString(5),
                isActive = reader.GetBoolean(6),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(7)
            });
        }

        return rules;
    }

    private static async Task<ProjectTeamCoordinator?> LoadProjectTeamCoordinatorAsync(NpgsqlConnection connection)
    {
        var configuredEmail = Environment.GetEnvironmentVariable("PROJECTPULSE_PROJECT_TEAM_COORDINATOR_EMAIL");

        var sql = string.IsNullOrWhiteSpace(configuredEmail)
            ? """
              SELECT email, display_name
              FROM app_users
              WHERE is_active = TRUE
                AND login_enabled = TRUE
                AND (
                    LOWER(COALESCE(job_title, '')) LIKE '%project team coordinator%'
                 OR LOWER(COALESCE(job_title, '')) LIKE '%coordinator%'
                 OR LOWER(COALESCE(display_name, '')) LIKE '%project team coordinator%'
                )
              ORDER BY
                CASE WHEN LOWER(COALESCE(job_title, '')) LIKE '%project team coordinator%' THEN 0 ELSE 1 END,
                display_name
              LIMIT 1;
              """
            : """
              SELECT email, display_name
              FROM app_users
              WHERE is_active = TRUE
                AND login_enabled = TRUE
                AND LOWER(email) = LOWER(@configured_email)
              LIMIT 1;
              """;

        await using var command = new NpgsqlCommand(sql, connection);

        if (!string.IsNullOrWhiteSpace(configuredEmail))
        {
            command.Parameters.AddWithValue("configured_email", configuredEmail);
        }

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new ProjectTeamCoordinator(reader.GetString(0), reader.GetString(1));
    }

    private static async Task<IReadOnlyList<object>> LoadHolidayReminderWindowsAsync(NpgsqlConnection connection)
    {
        var windows = new List<object>();

        const string sql = """
            SELECT company_holiday_id, holiday_date, holiday_name, holiday_type, auto_populate_hours
            FROM company_holidays
            WHERE is_active = TRUE
              AND is_floating_holiday = FALSE
              AND EXTRACT(ISODOW FROM holiday_date) BETWEEN 1 AND 5
              AND holiday_date >= CURRENT_DATE
            ORDER BY holiday_date
            LIMIT 30;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var holidayDate = reader.GetFieldValue<DateOnly>(1);

            windows.Add(new
            {
                id = reader.GetGuid(0),
                holidayDate,
                holidayName = reader.GetString(2),
                holidayType = reader.GetString(3),
                autoPopulateHours = reader.GetDecimal(4),
                sevenDayReminderDate = holidayDate.AddDays(-7),
                oneDayReminderDate = holidayDate.AddDays(-1),
                weekdayOnly = true
            });
        }

        return windows;
    }

    private static IResult? ValidateConfig(TimeComplianceDatabaseConfig config)
    {
        if (config.Missing.Count == 0)
        {
            return null;
        }

        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing
        });
    }

    private static DateOnly GetSundayForDate(DateOnly date)
    {
        var offset = (int)date.DayOfWeek;
        return date.AddDays(-offset);
    }
}

internal sealed record TimeComplianceDryRunRequest(DateOnly? WeekStart, string? Scenario);

internal sealed record ProjectTeamCoordinator(string Email, string DisplayName);

internal sealed record TimeCompliancePreviewItem(
    Guid UserId,
    string Email,
    string DisplayName,
    string? JobTitle,
    string? Department,
    string? TeamName,
    string? ManagerEmail,
    string? ManagerName,
    Guid? TimesheetId,
    string TimesheetStatus,
    decimal TotalHours,
    string[] CcEmails,
    string[] ComplianceGaps,
    string Subject,
    string Body);

internal sealed record TimeCompliancePreviewPayload(
    bool PreviewOnly,
    string Scenario,
    DateTimeOffset GeneratedAtUtc,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    object Settings,
    ProjectTeamCoordinator? ProjectTeamCoordinator,
    IReadOnlyList<object> ReminderRules,
    IReadOnlyList<object> HolidayReminderWindows,
    IReadOnlyList<TimeCompliancePreviewItem> MissingSubmissions,
    object Summary);

internal sealed record TimeComplianceDatabaseConfig(
    string? Host,
    string? Port,
    string? Database,
    string? Username,
    string? Password,
    IReadOnlyList<string> Missing)
{
    public string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = int.TryParse(Port, out var parsedPort) ? parsedPort : 5432,
                Database = Database,
                Username = Username,
                Password = Password,
                IncludeErrorDetail = false,
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = 5
            };

            return builder.ConnectionString;
        }
    }

    public static TimeComplianceDatabaseConfig FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(host)) missing.Add("PTP_DB_HOST");
        if (string.IsNullOrWhiteSpace(port)) missing.Add("PTP_DB_PORT");
        if (string.IsNullOrWhiteSpace(database)) missing.Add("PTP_DB_NAME");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("PTP_DB_USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("PTP_DB_PASSWORD");

        return new TimeComplianceDatabaseConfig(host, port, database, username, password, missing);
    }
}
