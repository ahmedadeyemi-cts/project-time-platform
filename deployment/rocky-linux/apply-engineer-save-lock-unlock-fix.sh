#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
CSS_FILE="$REPO_DIR/src/frontend/project-time-web/src/timesheet.css"

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

# Keep the API version moving forward for validation.
api = re.sub(r'version = "0\.4\.[0-9]+"', 'version = "0.4.2"', api)
api = re.sub(r'version = "0\.3\.[0-9]+"', 'version = "0.4.2"', api)

# Repair the earlier C# conditional compile issue if it exists.
api = api.replace(
    'var comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();',
    'object comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();')

# A draft save must not be blocked just because one or more days in the week have already been submitted/approved.
api = api.replace(
'''        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be edited."
            });
        }

        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, start);
        await ReplaceTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "draft");
''',
'''        if (existingStatus is "reconciled" or "locked")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Locked or reconciled timesheets cannot be edited."
            });
        }

        var timesheetId = await UpsertDraftShellForEditableSaveAsync(connection, transaction, userId, start);
        await ReplaceEditableTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "draft");
''')

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
        await ReplaceTimeEntriesForEditableDaysAsync(connection, transaction, timesheetId, userId, editableEntries, status);
    }
}

static async Task ReplaceTimeEntriesForEditableDaysAsync(
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

api_file.write_text(api)

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

new_get_day_status = r'''  function getDayStatus(workDate) {
    const apiDayStatus = timesheet.data?.dayStatuses?.find((dayStatus) => dayStatus.workDate === workDate);
    const savedEntryStatus = (timesheet.data?.entries ?? [])
      .filter((entry) => entry.workDate === workDate)
      .map((entry) => entry.status)
      .find((entryStatus) => entryStatus && entryStatus !== 'draft');

    const status = apiDayStatus?.status ?? savedEntryStatus ?? 'draft';
    const editableStatuses = ['draft', 'manager_declined'];
    const canEdit = editableStatuses.includes(status);
    const canUnlock = status === 'submitted' && (apiDayStatus?.canUnlock ?? Boolean(timesheet.data?.canUnlock ?? true));

    let unlockMessage = 'This day is open for time entry.';
    if (status === 'submitted') {
      unlockMessage = 'This submitted day is locked. Use Unlock if it is within the allowed correction window, or contact your manager.';
    } else if (status === 'manager_declined') {
      unlockMessage = apiDayStatus?.managerDecisionComment ?? 'This day was returned by the manager and can be corrected/resubmitted.';
    } else if (status === 'manager_approved') {
      unlockMessage = 'This day has been approved by the manager and can no longer be edited by the engineer.';
    } else if (['pm_approved', 'accounting_ready', 'reconciled', 'locked'].includes(status)) {
      unlockMessage = 'This day has moved forward in the approval workflow and can no longer be edited by the engineer.';
    }

    return {
      ...apiDayStatus,
      workDate,
      status,
      canEdit,
      canUnlock,
      unlockMessage
    };
  }

'''

app = re.sub(r'  function getDayStatus\(workDate\) \{.*?  function isDayEditable\(workDate\)', new_get_day_status + '  function isDayEditable(workDate)', app, count=1, flags=re.S)

app = app.replace(
'''  async function submitSelectedDay() {
    if (!selectedCell || isSaving) return;

    const dayTotal = getDayTotal(selectedCell.date);
''',
'''  async function submitSelectedDay() {
    if (!selectedCell || isSaving) return;

    if (!isDayEditable(selectedCell.date)) {
      setSaveStatus('This day is locked and cannot be submitted or edited by the engineer.');
      return;
    }

    const dayTotal = getDayTotal(selectedCell.date);
''')

# Submitted/approved cells must stay clickable so the modal can show read-only status or unlock.
app = app.replace('disabled={!dayIsEditable}', 'disabled={false}')
app = app.replace('disabled={!isDayEditable(day.date)}', 'disabled={false}')

old_actions = r'''                {selectedDayStatus?.status === 'submitted' ? (
                  <button type="button" className="unlock-action" onClick={unlockSelectedDay} disabled={isSaving}>
                    Unlock this day
                  </button>
                ) : (
                  <button type="button" className="primary-action" onClick={submitSelectedDay} disabled={isSaving || getSelectedDayTotal() < 8}>
                    Submit this day
                  </button>
                )}'''
new_actions = r'''                {selectedDayStatus?.status === 'submitted' ? (
                  <button type="button" className="unlock-action" onClick={unlockSelectedDay} disabled={isSaving || !selectedDayStatus?.canUnlock}>
                    Unlock this day
                  </button>
                ) : isDayEditable(selectedCell.date) ? (
                  <button type="button" className="primary-action" onClick={submitSelectedDay} disabled={isSaving || getSelectedDayTotal() < 8}>
                    Submit this day
                  </button>
                ) : (
                  <span className="read-only-pill">Read only</span>
                )}'''
app = app.replace(old_actions, new_actions)

old_note = r'''              {selectedDayStatus?.status === 'submitted' ? (
                <small>{selectedDayStatus.unlockMessage}</small>
              ) : (
                <small>Use Submit this day once the day reaches at least 8.00 hours. Closing this window automatically saves your draft.</small>
              )}'''
new_note = r'''              {selectedDayStatus?.status === 'submitted' || !isDayEditable(selectedCell.date) ? (
                <small>{selectedDayStatus.unlockMessage}</small>
              ) : (
                <small>Use Submit this day once the day reaches at least 8.00 hours. Closing this window automatically saves your draft.</small>
              )}'''
app = app.replace(old_note, new_note)

app_file.write_text(app)

css_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/timesheet.css')
if css_file.exists():
    css = css_file.read_text()
    if '.read-only-pill' not in css:
        css += r'''

.read-only-pill {
  display: inline-flex;
  align-items: center;
  border: 1px solid var(--border);
  border-radius: 999px;
  padding: 11px 15px;
  background: var(--surface-strong);
  color: var(--muted);
  font-size: 0.86rem;
  font-weight: 900;
}
'''
        css_file.write_text(css)
PY

echo "==> Engineer save, locking, and unlock visibility fix applied"
echo "==> Expected API version after redeploy: 0.4.2"
