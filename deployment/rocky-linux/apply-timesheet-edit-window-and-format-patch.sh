#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"

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

repo = Path('/opt/project-time-platform/app/project-time-platform')
api_file = repo / 'src/backend/ProjectTime.Api/Program.cs'
app_file = repo / 'src/frontend/project-time-web/src/App.jsx'

api = api_file.read_text()

api = api.replace('version = "0.3.0"', 'version = "0.3.1"')

api = api.replace(
'''        var existingStatus = await GetTimesheetStatusAsync(connection, transaction, userId, start);

        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be edited."
            });
        }
''',
'''        var editState = await GetTimesheetEditStateAsync(connection, transaction, userId, start);

        if (!CanEditTimesheet(editState, start))
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = editState?.Status,
                message = "Timesheets can be edited while they are draft, manager-declined, still in the current week, or within one hour of submission."
            });
        }
''')

api = api.replace(
'''        var existingStatus = await GetTimesheetStatusAsync(connection, transaction, userId, start);

        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_submittable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be submitted."
            });
        }
''',
'''        var editState = await GetTimesheetEditStateAsync(connection, transaction, userId, start);

        if (!CanEditTimesheet(editState, start))
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_submittable",
                currentStatus = editState?.Status,
                message = "Timesheets can be submitted while they are draft, manager-declined, still in the current week, or within one hour of submission."
            });
        }
''')

api = api.replace(
'''        submittedAt = timesheet?.SubmittedAt,
        weekStart = start,
''',
'''        submittedAt = timesheet?.SubmittedAt,
        canEdit = CanEditTimesheet(timesheet is null ? null : new TimesheetEditState(timesheet.Status, timesheet.SubmittedAt), start),
        editPolicy = "Editable while draft, manager-declined, still in the current week, or within one hour of submission.",
        weekStart = start,
''')

insert_before = '''static async Task<string?> GetTimesheetStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
'''
helper = '''static bool CanEditTimesheet(TimesheetEditState? editState, DateOnly weekStart)
{
    if (editState is null) return true;
    if (editState.Status is "draft" or "manager_declined") return true;
    if (editState.Status != "submitted") return false;

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var isCurrentWeek = today >= weekStart && today <= weekStart.AddDays(6);
    var submittedWithinOneHour = editState.SubmittedAt is not null
        && DateTimeOffset.UtcNow - editState.SubmittedAt.Value <= TimeSpan.FromHours(1);

    return isCurrentWeek || submittedWithinOneHour;
}

static async Task<TimesheetEditState?> GetTimesheetEditStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        SELECT status, submitted_at
        FROM timesheets
        WHERE user_id = @user_id
          AND week_start_date = @week_start_date;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new TimesheetEditState(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
}

'''
if 'static bool CanEditTimesheet(' not in api:
    api = api.replace(insert_before, helper + insert_before)

api = api.replace(
'''internal sealed record TimesheetHeader(Guid? TimesheetId, string Status, DateTimeOffset? SubmittedAt);
''',
'''internal sealed record TimesheetHeader(Guid? TimesheetId, string Status, DateTimeOffset? SubmittedAt);

internal sealed record TimesheetEditState(string Status, DateTimeOffset? SubmittedAt);
''')

api_file.write_text(api)

app = app_file.read_text()

app = app.replace(
'''function formatNumber(value) {
  return Number(value || 0).toFixed(2);
}
''',
'''function formatNumber(value) {
  return Number(value || 0).toFixed(2);
}

function formatHoursValue(value) {
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) ? parsed.toFixed(2) : '0.00';
}
''')

app = app.replace(
'''  const isTimesheetEditable = ['draft', 'manager_declined'].includes(currentTimesheetStatus);
''',
'''  const isTimesheetEditable = timesheet.data?.canEdit ?? ['draft', 'manager_declined'].includes(currentTimesheetStatus);
''')

app = app.replace(
'''                              title={`${type.label}: ${entry.hours || '0.00'} hours`}
''',
'''                              title={`${type.label}: ${formatHoursValue(entry.hours)} hours`}
''')

app = app.replace(
'''                              {entry.hours || '0.00'}
''',
'''                              {formatHoursValue(entry.hours)}
''')

app_file.write_text(app)
PY

echo "==> Timesheet edit-window and time-format source patch applied"
echo "==> Validate with: git diff -- src/backend/ProjectTime.Api/Program.cs src/frontend/project-time-web/src/App.jsx"
