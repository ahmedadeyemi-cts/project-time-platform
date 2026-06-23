#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

# Align local development identity with seeded project assignments.
api = api.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('const string DevelopmentUserEmail = "engineer@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('const string DevelopmentUserEmail = "developer@projectpulse.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace("manager@ussignal.local", "ahmed.adeyemi@ussignal.com")
api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.4.5"', api)

# Repair old compile issue if it reappears.
api = api.replace(
    'var comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();',
    'object comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();')

# Make week draft save day-aware. It should preserve protected/submitted/approved days,
# but still save open project-task and non-project entries for draft/declined days.
old_save = '''        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be edited."
            });
        }

        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, start);
        await ReplaceTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "draft");'''
new_save = '''        if (existingStatus is "reconciled" or "locked")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Locked or reconciled timesheets cannot be edited."
            });
        }

        var timesheetId = await UpsertDraftShellForEditableSaveAsync(connection, transaction, userId, start);
        await ReplaceEditableTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "draft");'''
api = api.replace(old_save, new_save)

helpers = r'''
static async Task<Guid> UpsertDraftShellForEditableSaveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        INSERT INTO timesheets (user_id, week_start_date, week_end_date, status, submitted_at)
        VALUES (@user_id, @week_start_date, @week_end_date, 'draft', NULL)
        ON CONFLICT (user_id, week_start_date) DO UPDATE
        SET week_end_date = EXCLUDED.week_end_date,
            status = CASE
                WHEN timesheets.status IN ('draft', 'manager_declined') THEN 'draft'
                ELSE timesheets.status
            END,
            submitted_at = CASE
                WHEN timesheets.status IN ('draft', 'manager_declined') THEN NULL
                ELSE timesheets.submitted_at
            END,
            updated_at = NOW()
        RETURNING timesheet_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);
    command.Parameters.AddWithValue("week_end_date", weekStart.AddDays(6));

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create draft timesheet shell."));
}

static async Task ReplaceEditableTimeEntriesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    var protectedDates = new HashSet<DateOnly>();

    await using (var protectedCommand = new NpgsqlCommand("""
        SELECT work_date
        FROM timesheet_day_statuses
        WHERE timesheet_id = @timesheet_id
          AND status IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked');
        """, connection, transaction))
    {
        protectedCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await using var reader = await protectedCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            protectedDates.Add(reader.GetFieldValue<DateOnly>(0));
        }
    }

    await using (var deleteCommand = new NpgsqlCommand("""
        DELETE FROM time_entries
        WHERE timesheet_id = @timesheet_id
          AND NOT EXISTS (
              SELECT 1
              FROM timesheet_day_statuses tds
              WHERE tds.timesheet_id = time_entries.timesheet_id
                AND tds.work_date = time_entries.work_date
                AND tds.status IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked')
          );
        """, connection, transaction))
    {
        deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    var editableEntries = entries
        .Where(entry => entry.Hours > 0)
        .Where(entry => !protectedDates.Contains(entry.WorkDate))
        .ToList();

    if (editableEntries.Count > 0)
    {
        await InsertTimeEntriesWithoutDeletingAsync(connection, transaction, timesheetId, userId, editableEntries, status);
    }
}

static async Task InsertTimeEntriesWithoutDeletingAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    foreach (var entry in entries.Where(item => item.Hours > 0))
    {
        Guid? nonProjectCategoryId = null;
        var billable = entry.ProjectId is not null && entry.TaskId is not null;

        if (!string.IsNullOrWhiteSpace(entry.CategoryCode))
        {
            nonProjectCategoryId = await GetNonProjectCategoryIdAsync(connection, transaction, entry.CategoryCode);
            billable = false;
        }

        const string sql = """
            INSERT INTO time_entries (
                timesheet_id,
                user_id,
                project_id,
                task_id,
                non_project_time_category_id,
                time_type,
                work_date,
                hours,
                description,
                billable,
                status,
                work_location_group_id,
                work_location_id
            )
            VALUES (
                @timesheet_id,
                @user_id,
                @project_id,
                @task_id,
                @non_project_time_category_id,
                @time_type,
                @work_date,
                @hours,
                @description,
                @billable,
                @status,
                @work_location_group_id,
                @work_location_id
            );
            """;

        await using var insertCommand = new NpgsqlCommand(sql, connection, transaction);
        insertCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        insertCommand.Parameters.AddWithValue("user_id", userId);
        insertCommand.Parameters.AddWithValue("project_id", (object?)entry.ProjectId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("task_id", (object?)entry.TaskId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("non_project_time_category_id", (object?)nonProjectCategoryId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("time_type", entry.TimeType);
        insertCommand.Parameters.AddWithValue("work_date", entry.WorkDate);
        insertCommand.Parameters.AddWithValue("hours", entry.Hours);
        insertCommand.Parameters.AddWithValue("description", string.IsNullOrWhiteSpace(entry.Description) ? DBNull.Value : entry.Description.Trim());
        insertCommand.Parameters.AddWithValue("billable", billable);
        insertCommand.Parameters.AddWithValue("status", status);
        insertCommand.Parameters.AddWithValue("work_location_group_id", (object?)entry.WorkLocationGroupId ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("work_location_id", (object?)entry.WorkLocationId ?? DBNull.Value);

        await insertCommand.ExecuteNonQueryAsync();
    }
}

'''

if 'static async Task<Guid> UpsertDraftShellForEditableSaveAsync' not in api:
    api = api.replace('static async Task<Guid> UpsertDraftTimesheetAsync', helpers + 'static async Task<Guid> UpsertDraftTimesheetAsync', 1)

# Add a debug endpoint so validation can show exactly what the API persisted for the current week.
debug_endpoint = r'''
app.MapGet("/api/debug/time-entries", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(6);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var rows = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            t.week_start_date,
            t.status AS timesheet_status,
            te.work_date,
            te.time_type,
            te.hours,
            te.status AS entry_status,
            COALESCE(npt.category_name, pt.task_name, 'Unknown') AS activity,
            p.project_code,
            p.project_name,
            te.description
        FROM timesheets t
        LEFT JOIN time_entries te ON te.timesheet_id = t.timesheet_id
        LEFT JOIN non_project_time_categories npt ON npt.non_project_time_category_id = te.non_project_time_category_id
        LEFT JOIN project_tasks pt ON pt.task_id = te.task_id
        LEFT JOIN projects p ON p.project_id = te.project_id
        WHERE t.user_id = @user_id
          AND t.week_start_date = @week_start
        ORDER BY te.work_date, te.time_type, activity;
        """, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start", start);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            weekStart = reader.GetFieldValue<DateOnly>(0),
            timesheetStatus = reader.GetString(1),
            workDate = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
            timeType = reader.IsDBNull(3) ? null : reader.GetString(3),
            hours = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4),
            entryStatus = reader.IsDBNull(5) ? null : reader.GetString(5),
            activity = reader.IsDBNull(6) ? null : reader.GetString(6),
            projectCode = reader.IsDBNull(7) ? null : reader.GetString(7),
            projectName = reader.IsDBNull(8) ? null : reader.GetString(8),
            description = reader.IsDBNull(9) ? null : reader.GetString(9)
        });
    }

    return Results.Ok(new { weekStart = start, weekEnd = end, count = rows.Count, rows });
});

'''

if 'app.MapGet("/api/debug/time-entries"' not in api:
    api = api.replace('\napp.Run();', '\n' + debug_endpoint + 'app.Run();', 1)

api_file.write_text(api)

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

# Remove consecutive duplicate declarations created by reapplying patches.
lines = app.splitlines()
needle = 'const assignedOpenTasks = openTasks.data?.tasks ?? [];'
cleaned = []
previous_was_needle = False
for line in lines:
    stripped = line.strip()
    if stripped == needle and previous_was_needle:
        continue
    cleaned.append(line)
    previous_was_needle = stripped == needle
app = '\n'.join(cleaned) + '\n'

# Ensure saved project-task rows rehydrate after both timesheet and open tasks finish loading.
app = app.replace(
    "}, [timesheet.data?.weekStart, timesheet.data?.timesheetId, timesheet.data?.status]);",
    "}, [timesheet.data?.weekStart, timesheet.data?.timesheetId, timesheet.data?.status, openTasks.data?.count]);")

app_file.write_text(app)
PY

if [ -d "$DIST_DIR" ]; then
  rm -rf "$DIST_DIR"
fi

echo "==> Project task save/submit repair applied"
echo "==> Expected API version after redeploy: 0.4.5"
