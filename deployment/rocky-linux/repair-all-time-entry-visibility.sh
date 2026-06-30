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

repo = Path('/opt/project-time-platform/app/project-time-platform')
api_file = repo / 'src/backend/ProjectTime.Api/Program.cs'
app_file = repo / 'src/frontend/project-time-web/src/App.jsx'
api = api_file.read_text()
app = app_file.read_text()

api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.4.9"', api)
api = api.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('const string DevelopmentUserEmail = "engineer@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')
api = api.replace(
    'var comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();',
    'object comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();')

# Replace LoadSavedTimeEntriesAsync so every saved row returns enough metadata for the UI
# to rebuild the grid regardless of whether the row is non-project, project task,
# draft, submitted, declined, approved, accounting-ready, reconciled, or locked.
new_load_saved = r'''static async Task<List<object>> LoadSavedTimeEntriesAsync(NpgsqlConnection connection, Guid timesheetId)
{
    var entries = new List<object>();

    const string sql = """
        SELECT
            te.time_entry_id,
            te.work_date,
            te.time_type,
            te.hours,
            te.description,
            te.status,
            te.project_id,
            te.task_id,
            te.non_project_time_category_id,
            npt.category_code,
            npt.category_name,
            te.work_location_group_id,
            te.work_location_id,
            te.billable,
            p.project_code,
            p.project_name,
            pt.task_code,
            pt.task_name,
            c.client_name
        FROM time_entries te
        LEFT JOIN non_project_time_categories npt
            ON npt.non_project_time_category_id = te.non_project_time_category_id
        LEFT JOIN projects p
            ON p.project_id = te.project_id
        LEFT JOIN project_tasks pt
            ON pt.task_id = te.task_id
        LEFT JOIN clients c
            ON c.client_id = p.client_id
        WHERE te.timesheet_id = @timesheet_id
        ORDER BY te.work_date, te.time_type, COALESCE(npt.display_order, pt.display_order, 999), COALESCE(npt.category_name, pt.task_name, p.project_name);
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var projectId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6);
        var taskId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7);
        var categoryCode = reader.IsDBNull(9) ? null : reader.GetString(9);

        entries.Add(new
        {
            id = reader.GetGuid(0),
            rowType = projectId is not null && taskId is not null ? "projectTask" : "nonProject",
            workDate = reader.GetFieldValue<DateOnly>(1),
            timeType = reader.GetString(2),
            hours = reader.GetDecimal(3),
            description = reader.IsDBNull(4) ? null : reader.GetString(4),
            status = reader.GetString(5),
            projectId,
            taskId,
            nonProjectTimeCategoryId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
            categoryCode,
            categoryName = reader.IsDBNull(10) ? null : reader.GetString(10),
            workLocationGroupId = reader.IsDBNull(11) ? (Guid?)null : reader.GetGuid(11),
            workLocationId = reader.IsDBNull(12) ? (Guid?)null : reader.GetGuid(12),
            billable = reader.GetBoolean(13),
            projectCode = reader.IsDBNull(14) ? null : reader.GetString(14),
            projectName = reader.IsDBNull(15) ? null : reader.GetString(15),
            taskCode = reader.IsDBNull(16) ? null : reader.GetString(16),
            taskName = reader.IsDBNull(17) ? null : reader.GetString(17),
            clientName = reader.IsDBNull(18) ? null : reader.GetString(18)
        });
    }

    return entries;
}
'''

pattern = r'static async Task<List<object>> LoadSavedTimeEntriesAsync\(NpgsqlConnection connection, Guid timesheetId\)\s*\{.*?\n\}'
api, replaced = re.subn(pattern, new_load_saved, api, count=1, flags=re.S)
if replaced == 0:
    raise SystemExit('ERROR: Could not replace LoadSavedTimeEntriesAsync in Program.cs')

# Replace the frontend rehydration useEffect. This is the source of the current
# visibility issue: it previously rebuilt only non-project rows or only rows that
# matched the current Open Tasks list. This version rebuilds every saved row.
new_effect = r'''  useEffect(() => {
    const categories = timesheet.data?.nonProjectCategories ?? [];
    const assignedOpenTasks = openTasks.data?.tasks ?? [];
    const savedEntries = timesheet.data?.entries ?? [];

    if (categories.length === 0 && assignedOpenTasks.length === 0 && savedEntries.length === 0) return;

    const defaults = categories.filter((category) => ['ADMINISTRATIVE', 'PEER_SUPPORT'].includes(category.code));
    const fallback = categories.slice(0, 2);
    const savedCategoryCodes = new Set(savedEntries.map((entry) => entry.categoryCode).filter(Boolean));
    const savedCategories = categories.filter((category) => savedCategoryCodes.has(category.code));
    const rowMap = new Map();

    [...(defaults.length > 0 ? defaults : fallback), ...savedCategories].forEach((category) => {
      rowMap.set(`non-project-${category.code}`, categoryToRow(category));
    });

    savedEntries.forEach((entry) => {
      if (entry.rowType === 'nonProject' && entry.categoryCode && !rowMap.has(`non-project-${entry.categoryCode}`)) {
        rowMap.set(`non-project-${entry.categoryCode}`, {
          id: `non-project-${entry.categoryCode}`,
          type: 'nonProject',
          state: 'Saved',
          activity: entry.categoryName ?? entry.categoryCode,
          projectDescription: 'Non-project time',
          categoryCode: entry.categoryCode
        });
      }

      if (entry.rowType === 'projectTask' && entry.projectId && entry.taskId) {
        const matchingTask = assignedOpenTasks.find((task) => task.projectId === entry.projectId && task.taskId === entry.taskId);
        const rowId = `project-task-${entry.projectId}-${entry.taskId}`;

        if (matchingTask) {
          rowMap.set(rowId, taskToRow(matchingTask));
        } else {
          rowMap.set(rowId, {
            id: rowId,
            type: 'projectTask',
            state: 'Saved',
            activity: entry.taskName ?? entry.taskCode ?? 'Project task',
            projectDescription: entry.projectCode ? `${entry.projectCode} • ${entry.projectName ?? 'Project'}` : (entry.projectName ?? 'Project task'),
            projectId: entry.projectId,
            taskId: entry.taskId,
            taskCode: entry.taskCode ?? null,
            clientName: entry.clientName ?? null,
            projectManagerName: null
          });
        }
      }
    });

    const entryMap = {};
    savedEntries.forEach((entry) => {
      let rowId = null;

      if (entry.rowType === 'nonProject' && entry.categoryCode) {
        rowId = `non-project-${entry.categoryCode}`;
      }

      if (entry.rowType === 'projectTask' && entry.projectId && entry.taskId) {
        rowId = `project-task-${entry.projectId}-${entry.taskId}`;
      }

      if (!rowId) return;

      entryMap[getEntryKey(rowId, entry.workDate, entry.timeType)] = {
        hours: entry.hours?.toString() ?? '',
        comment: entry.description ?? '',
        workLocationGroupId: entry.workLocationGroupId ?? '',
        workLocationId: entry.workLocationId ?? '',
        savedStatus: entry.status ?? 'draft'
      };
    });

    setActiveRows([...rowMap.values()]);
    setEntries(entryMap);
    setSelectedCell(null);

    const savedTotal = savedEntries.reduce((total, entry) => total + Number(entry.hours || 0), 0);
    setSubmissionStatus(statusToLabel(timesheet.data?.status, savedTotal));
    setSaveStatus(savedEntries.length > 0 ? `Loaded ${savedEntries.length} saved time entr${savedEntries.length === 1 ? 'y' : 'ies'}` : 'Not saved yet');
  }, [timesheet.data?.weekStart, timesheet.data?.timesheetId, timesheet.data?.status, timesheet.data?.entries?.length, openTasks.data?.count]);'''

effect_pattern = r'''  useEffect\(\(\) => \{\n    const categories = timesheet\.data\?\.nonProjectCategories \?\? \[\];.*?\n  \}, \[timesheet\.data\?\.weekStart, timesheet\.data\?\.timesheetId, timesheet\.data\?\.status(?:, [^\]]+)?\]\);'''
app, effect_replaced = re.subn(effect_pattern, new_effect, app, count=1, flags=re.S)
if effect_replaced == 0:
    raise SystemExit('ERROR: Could not replace frontend rehydration useEffect')

# Ensure assignedOpenTasks exists in the main render scope exactly once after categories.
app = re.sub(r'const categories = timesheet\.data\?\.nonProjectCategories \?\? \[\];\n(?:\s*const assignedOpenTasks = openTasks\.data\?\.tasks \?\? \[\];\n)*',
             'const categories = timesheet.data?.nonProjectCategories ?? [];\n  const assignedOpenTasks = openTasks.data?.tasks ?? [];\n', app, count=1)

# Activity count should reflect the chosen source.
app = app.replace(
    "<span>{activitySource === 'nonProject' ? categories.length : 0}</span>",
    "<span>{activitySource === 'nonProject' ? categories.length : activitySource === 'openTasks' ? assignedOpenTasks.length : 0}</span>")

# Make saved/submitted/approved cells clickable so users can view read-only details.
app = app.replace('disabled={!dayIsEditable}', 'disabled={false}')

# Show read-only state after manager approval/other locked workflow statuses.
app = app.replace(
'''                {selectedDayStatus?.status === 'submitted' ? (
                  <button type="button" className="unlock-action" onClick={unlockSelectedDay} disabled={isSaving}>
                    Unlock this day
                  </button>
                ) : (
                  <button type="button" className="primary-action" onClick={submitSelectedDay} disabled={isSaving || getSelectedDayTotal() < 8}>
                    Submit this day
                  </button>
                )}''',
'''                {selectedDayStatus?.status === 'submitted' ? (
                  <button type="button" className="unlock-action" onClick={unlockSelectedDay} disabled={isSaving}>
                    Unlock this day
                  </button>
                ) : selectedDayStatus?.canEdit === false ? (
                  <button type="button" className="modal-close-button" disabled>
                    Read only
                  </button>
                ) : (
                  <button type="button" className="primary-action" onClick={submitSelectedDay} disabled={isSaving || getSelectedDayTotal() < 8}>
                    Submit this day
                  </button>
                )}''')

app = app.replace(
'''              {selectedDayStatus?.status === 'submitted' ? (
                <small>{selectedDayStatus.unlockMessage}</small>
              ) : (
                <small>Use Submit this day once the day reaches at least 8.00 hours. Closing this window automatically saves your draft.</small>
              )}''',
'''              {selectedDayStatus?.status === 'submitted' || selectedDayStatus?.canEdit === false ? (
                <small>{selectedDayStatus.unlockMessage}</small>
              ) : (
                <small>Use Submit this day once the day reaches at least 8.00 hours. Closing this window automatically saves your draft.</small>
              )}''')

# Remove consecutive duplicate Open Tasks declarations from previous patch attempts.
lines = app.splitlines()
cleaned = []
previous = None
for line in lines:
    stripped = line.strip()
    if stripped == 'const assignedOpenTasks = openTasks.data?.tasks ?? [];' and stripped == previous:
        continue
    cleaned.append(line)
    previous = stripped
app = '\n'.join(cleaned) + '\n'

api_file.write_text(api)
app_file.write_text(app)
PY

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist"
  rm -rf "$DIST_DIR"
fi

echo "==> All time-entry visibility repair applied"
echo "==> Expected API version after redeploy: 0.4.9"
echo "==> This repair keeps every saved time entry visible: non-project, project task, draft, submitted, declined, approved, accounting, reconciled, and locked."
