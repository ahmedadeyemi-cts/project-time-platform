#!/usr/bin/env bash
set -euo pipefail

APP_ROOT="/opt/project-time-platform"
REPO_DIR="$APP_ROOT/app/project-time-platform"
ENV_FILE="$APP_ROOT/config/postgres.env"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: Missing $ENV_FILE"
  exit 1
fi

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

# shellcheck disable=SC1090
source "$ENV_FILE"

echo "==> Ensuring personalization/holiday/reminder foundation tables exist"
PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h "$PTP_DB_HOST" \
  -p "$PTP_DB_PORT" \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;

CREATE TABLE IF NOT EXISTS user_timesheet_preferences (
    user_timesheet_preference_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    default_non_project_category_codes TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
    default_project_task_ids UUID[] NOT NULL DEFAULT ARRAY[]::UUID[],
    auto_add_holidays BOOLEAN NOT NULL DEFAULT TRUE,
    weekly_reminder_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    reminder_day_of_week INTEGER NOT NULL DEFAULT 5,
    reminder_local_time TIME NOT NULL DEFAULT TIME '09:00',
    timezone_name VARCHAR(100) NOT NULL DEFAULT 'America/Los_Angeles',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id)
);

CREATE TABLE IF NOT EXISTS holiday_upload_batches (
    holiday_upload_batch_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    upload_year INTEGER NOT NULL,
    original_filename VARCHAR(255),
    uploaded_by_user_id UUID REFERENCES app_users(user_id),
    uploaded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    row_count INTEGER NOT NULL DEFAULT 0,
    notes TEXT,
    UNIQUE(upload_year, original_filename)
);

CREATE TABLE IF NOT EXISTS company_holidays (
    company_holiday_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    holiday_date DATE NOT NULL UNIQUE,
    holiday_name VARCHAR(255) NOT NULL,
    holiday_code VARCHAR(75) NOT NULL DEFAULT 'HOLIDAY',
    holiday_type VARCHAR(50) NOT NULL DEFAULT 'company_paid',
    is_floating_holiday BOOLEAN NOT NULL DEFAULT FALSE,
    auto_populate_hours NUMERIC(5,2) NOT NULL DEFAULT 8.00,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    source_batch_id UUID REFERENCES holiday_upload_batches(holiday_upload_batch_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS notification_groups (
    notification_group_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    group_code VARCHAR(75) NOT NULL UNIQUE,
    group_name VARCHAR(255) NOT NULL,
    group_description TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS notification_group_members (
    notification_group_member_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_group_id UUID NOT NULL REFERENCES notification_groups(notification_group_id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(notification_group_id, user_id)
);

CREATE TABLE IF NOT EXISTS reminder_rules (
    reminder_rule_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_code VARCHAR(100) NOT NULL UNIQUE,
    rule_name VARCHAR(255) NOT NULL,
    recipient_group_code VARCHAR(75) NOT NULL,
    rule_type VARCHAR(50) NOT NULL,
    cadence_description TEXT NOT NULL,
    subject_template TEXT NOT NULL,
    body_template TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS email_notification_outbox (
    email_notification_outbox_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_code VARCHAR(100),
    recipient_email VARCHAR(255) NOT NULL,
    recipient_name VARCHAR(255),
    subject TEXT NOT NULL,
    body TEXT NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'queued',
    scheduled_for TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sent_at TIMESTAMPTZ,
    error_message TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO notification_groups (group_code, group_name, group_description)
VALUES
    ('ENGINEERS', 'Engineers', 'Resources required to enter and submit weekly time.'),
    ('PROJECT_MANAGEMENT', 'Project Management Team', 'Project managers and delivery leaders who need month-end reminders.')
ON CONFLICT (group_code) DO UPDATE
SET group_name = EXCLUDED.group_name,
    group_description = EXCLUDED.group_description,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO user_timesheet_preferences (user_id, default_non_project_category_codes, default_project_task_ids, auto_add_holidays, weekly_reminder_enabled)
SELECT u.user_id, ARRAY[]::TEXT[], ARRAY[]::UUID[], TRUE, TRUE
FROM app_users u
WHERE u.email = 'ahmed.adeyemi@ussignal.com'
ON CONFLICT (user_id) DO UPDATE
SET auto_add_holidays = TRUE,
    weekly_reminder_enabled = TRUE,
    updated_at = NOW();

INSERT INTO reminder_rules (rule_code, rule_name, recipient_group_code, rule_type, cadence_description, subject_template, body_template)
VALUES
    ('WEEKLY_ENGINEER_TIME_REMINDER', 'Weekly engineer time reminder', 'ENGINEERS', 'weekly', 'Every Friday at 9:00 AM by default.', 'Reminder: Submit your weekly time in Project Health Dashboard', 'All resources are required to submit 40 hours of time each week. Use Vacation for PTO. Use Holiday only for company-paid holidays and floating holidays.'),
    ('MONTH_END_PM_REMINDER', 'Month-end project management reminder', 'PROJECT_MANAGEMENT', 'month_end_last_friday', 'Runs on the last Friday of each month.', 'Month End Reminder: Project Health Dashboard review', 'This is a Month End reminder for the Project Management team. Please review project time, approvals, billing readiness, expenses, and reporting items in Project Health Dashboard.')
ON CONFLICT (rule_code) DO UPDATE
SET rule_name = EXCLUDED.rule_name,
    recipient_group_code = EXCLUDED.recipient_group_code,
    rule_type = EXCLUDED.rule_type,
    cadence_description = EXCLUDED.cadence_description,
    subject_template = EXCLUDED.subject_template,
    body_template = EXCLUDED.body_template,
    is_active = TRUE,
    updated_at = NOW();

INSERT INTO schema_migrations (migration_id, description)
VALUES ('012_personalized_timesheet_holidays_reminders_repair', 'Repair foundation tables for personalization holidays and reminders')
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
SQL

echo "==> Patching holidays endpoint to return an empty list instead of HTTP 500 if the foundation is not ready"
python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()
api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.4"', api)

new_endpoint = r'''app.MapGet("/api/holidays", async (int? year) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var targetYear = year ?? DateTime.UtcNow.Year;

    try
    {
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
    }
    catch (PostgresException ex) when (ex.SqlState == "42P01" || ex.SqlState == "42703")
    {
        return Results.Ok(new { year = targetYear, count = 0, holidays = Array.Empty<object>(), warning = "Holiday foundation tables are not ready yet." });
    }
});'''

pattern = r'app\.MapGet\("/api/holidays", async \(int\? year\) =>\s*\{.*?\n\}\);'
api, count = re.subn(pattern, new_endpoint, api, count=1, flags=re.S)
if count == 0:
    api = api.replace('\napp.Run();', '\n' + new_endpoint + '\napp.Run();', 1)

api_file.write_text(api)
PY

if [ -d "$DIST_DIR" ]; then
  rm -rf "$DIST_DIR"
fi

echo "==> Holidays endpoint 500 repair applied"
echo "==> Expected API version after redeploy: 0.5.4"
