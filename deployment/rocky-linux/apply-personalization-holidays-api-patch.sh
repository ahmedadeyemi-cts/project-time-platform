#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.3"', api)
api = api.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')

endpoints = r'''
app.MapGet("/api/users/timesheet-preferences", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var preferences = await LoadTimesheetPreferencesAsync(connection, userId);

    return Results.Ok(preferences);
});

app.MapPost("/api/users/timesheet-preferences", async (TimesheetPreferenceRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);

    const string sql = """
        INSERT INTO user_timesheet_preferences (
            user_id,
            default_non_project_category_codes,
            default_project_task_ids,
            auto_add_holidays,
            weekly_reminder_enabled,
            updated_at
        )
        VALUES (
            @user_id,
            @default_codes,
            @default_task_ids,
            @auto_add_holidays,
            @weekly_reminder_enabled,
            NOW()
        )
        ON CONFLICT (user_id) DO UPDATE
        SET default_non_project_category_codes = EXCLUDED.default_non_project_category_codes,
            default_project_task_ids = EXCLUDED.default_project_task_ids,
            auto_add_holidays = EXCLUDED.auto_add_holidays,
            weekly_reminder_enabled = EXCLUDED.weekly_reminder_enabled,
            updated_at = NOW();
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("default_codes", request.DefaultNonProjectCategoryCodes?.ToArray() ?? Array.Empty<string>());
    command.Parameters.AddWithValue("default_task_ids", request.DefaultProjectTaskIds?.ToArray() ?? Array.Empty<Guid>());
    command.Parameters.AddWithValue("auto_add_holidays", request.AutoAddHolidays);
    command.Parameters.AddWithValue("weekly_reminder_enabled", request.WeeklyReminderEnabled);
    await command.ExecuteNonQueryAsync();

    var preferences = await LoadTimesheetPreferencesAsync(connection, userId);
    return Results.Ok(new { status = "preferences_saved", preferences });
});

app.MapGet("/api/holidays", async (int? year) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var targetYear = year ?? DateTime.UtcNow.Year;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var holidays = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT holiday_date, holiday_name, holiday_code, holiday_type, is_floating_holiday, auto_populate_hours
        FROM company_holidays
        WHERE is_active = TRUE
          AND EXTRACT(YEAR FROM holiday_date) = @year
        ORDER BY holiday_date;
        """, connection);
    command.Parameters.AddWithValue("year", targetYear);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        holidays.Add(new
        {
            holidayDate = reader.GetFieldValue<DateOnly>(0),
            holidayName = reader.GetString(1),
            holidayCode = reader.GetString(2),
            holidayType = reader.GetString(3),
            isFloatingHoliday = reader.GetBoolean(4),
            autoPopulateHours = reader.GetDecimal(5)
        });
    }

    return Results.Ok(new { year = targetYear, count = holidays.Count, holidays });
});

app.MapPost("/api/reminders/queue-weekly-engineer", async () =>
{
    return await QueueReminderRuleAsync("WEEKLY_ENGINEER_TIME_REMINDER");
});

app.MapPost("/api/reminders/queue-month-end-pm", async () =>
{
    return await QueueReminderRuleAsync("MONTH_END_PM_REMINDER");
});

app.MapGet("/api/reminders/outbox", async (int? limit) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var maxRows = Math.Clamp(limit ?? 25, 1, 200);
    var rows = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT rule_code, recipient_email, recipient_name, subject, status, scheduled_for, sent_at, error_message
        FROM email_notification_outbox
        ORDER BY created_at DESC
        LIMIT @limit;
        """, connection);
    command.Parameters.AddWithValue("limit", maxRows);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            ruleCode = reader.IsDBNull(0) ? null : reader.GetString(0),
            recipientEmail = reader.GetString(1),
            recipientName = reader.IsDBNull(2) ? null : reader.GetString(2),
            subject = reader.GetString(3),
            status = reader.GetString(4),
            scheduledFor = reader.GetFieldValue<DateTimeOffset>(5),
            sentAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
            errorMessage = reader.IsDBNull(7) ? null : reader.GetString(7)
        });
    }

    return Results.Ok(new { count = rows.Count, outbox = rows });
});

'''

if 'app.MapGet("/api/users/timesheet-preferences"' not in api:
    api = api.replace('\napp.Run();', '\n' + endpoints + 'app.Run();', 1)

helpers = r'''
static async Task<object> LoadTimesheetPreferencesAsync(NpgsqlConnection connection, Guid userId)
{
    const string sql = """
        INSERT INTO user_timesheet_preferences (user_id)
        VALUES (@user_id)
        ON CONFLICT (user_id) DO NOTHING;

        SELECT default_non_project_category_codes,
               default_project_task_ids,
               auto_add_holidays,
               weekly_reminder_enabled,
               reminder_day_of_week,
               reminder_local_time,
               timezone_name
        FROM user_timesheet_preferences
        WHERE user_id = @user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);

    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();

    return new
    {
        defaultNonProjectCategoryCodes = reader.GetFieldValue<string[]>(0),
        defaultProjectTaskIds = reader.GetFieldValue<Guid[]>(1),
        autoAddHolidays = reader.GetBoolean(2),
        weeklyReminderEnabled = reader.GetBoolean(3),
        reminderDayOfWeek = reader.GetInt32(4),
        reminderLocalTime = reader.GetFieldValue<TimeOnly>(5).ToString("HH:mm"),
        timezoneName = reader.GetString(6)
    };
}

static async Task<IResult> QueueReminderRuleAsync(string ruleCode)
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        INSERT INTO email_notification_outbox (rule_code, recipient_email, recipient_name, subject, body, status, scheduled_for)
        SELECT
            rr.rule_code,
            u.email,
            u.display_name,
            rr.subject_template,
            REPLACE(rr.body_template, '{{display_name}}', u.display_name),
            'queued',
            NOW()
        FROM reminder_rules rr
        INNER JOIN notification_groups ng ON ng.group_code = rr.recipient_group_code
        INNER JOIN notification_group_members ngm ON ngm.notification_group_id = ng.notification_group_id AND ngm.is_active = TRUE
        INNER JOIN app_users u ON u.user_id = ngm.user_id AND u.is_active = TRUE
        WHERE rr.rule_code = @rule_code
          AND rr.is_active = TRUE
          AND ng.is_active = TRUE;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("rule_code", ruleCode);
    var inserted = await command.ExecuteNonQueryAsync();

    return Results.Ok(new { status = "queued", ruleCode, queuedCount = inserted });
}

'''

if 'static async Task<object> LoadTimesheetPreferencesAsync' not in api:
    api = api.replace('static IResult? ValidateConfig(DatabaseConfig config)', helpers + 'static IResult? ValidateConfig(DatabaseConfig config)', 1)

if 'internal sealed record TimesheetPreferenceRequest' not in api:
    api = api.replace('internal sealed record TimesheetSaveRequest', 'internal sealed record TimesheetPreferenceRequest(List<string>? DefaultNonProjectCategoryCodes, List<Guid>? DefaultProjectTaskIds, bool AutoAddHolidays, bool WeeklyReminderEnabled);\n\ninternal sealed record TimesheetSaveRequest', 1)

api_file.write_text(api)
PY

echo "==> Personalization, holiday, and reminder API patch applied"
echo "==> Expected API version after redeploy: 0.5.3"
