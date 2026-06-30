#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

# 1) Replace the saved-entry rehydration effect so it restores BOTH non-project
# rows and project-task rows after refresh, submit, and approval.
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
      if (entry.rowType === 'projectTask' && entry.projectId && entry.taskId) {
        const matchingTask = assignedOpenTasks.find((task) => task.projectId === entry.projectId && task.taskId === entry.taskId);
        const rowId = `project-task-${entry.projectId}-${entry.taskId}`;

        if (matchingTask) {
          rowMap.set(rowId, taskToRow(matchingTask));
        } else {
          rowMap.set(rowId, {
            id: rowId,
            type: 'projectTask',
            state: 'Draft',
            activity: entry.taskName ?? entry.taskCode ?? 'Project task',
            projectDescription: entry.projectCode ? `${entry.projectCode} • ${entry.projectName ?? 'Project'}` : 'Project task',
            projectId: entry.projectId,
            taskId: entry.taskId,
            taskCode: entry.taskCode ?? null,
            clientName: null,
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
    setSaveStatus(savedEntries.length > 0 ? 'Loaded saved entries' : 'Not saved yet');
  }, [timesheet.data?.weekStart, timesheet.data?.timesheetId, timesheet.data?.status, timesheet.data?.entries?.length, openTasks.data?.count]);'''

pattern = r'''  useEffect\(\(\) => \{\n    const categories = timesheet\.data\?\.nonProjectCategories \?\? \[\];.*?\n  \}, \[timesheet\.data\?\.weekStart, timesheet\.data\?\.timesheetId, timesheet\.data\?\.status(?:, [^\]]+)?\]\);'''
app, count = re.subn(pattern, new_effect, app, count=1, flags=re.S)
if count == 0:
    raise SystemExit('ERROR: Could not replace the timesheet rehydration useEffect. App.jsx structure did not match expected pattern.')

# 2) Ensure the activity count shows Open Tasks when selected.
app = app.replace(
    "<span>{activitySource === 'nonProject' ? categories.length : 0}</span>",
    "<span>{activitySource === 'nonProject' ? categories.length : activitySource === 'openTasks' ? assignedOpenTasks.length : 0}</span>")

# 3) Allow submitted/approved cells to open as read-only instead of disabling the button.
# The inputs inside the modal remain disabled when the day is not editable.
app = app.replace('disabled={!dayIsEditable}', 'disabled={false}')

# 4) In the modal actions, show read-only for manager-approved/locked days instead of Submit.
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
                )}'''
app = app.replace(old_actions, new_actions)

# 5) Make the lower modal message explain approved/read-only states.
old_message = r'''              {selectedDayStatus?.status === 'submitted' ? (
                <small>{selectedDayStatus.unlockMessage}</small>
              ) : (
                <small>Use Submit this day once the day reaches at least 8.00 hours. Closing this window automatically saves your draft.</small>
              )}'''
new_message = r'''              {selectedDayStatus?.status === 'submitted' || selectedDayStatus?.canEdit === false ? (
                <small>{selectedDayStatus.unlockMessage}</small>
              ) : (
                <small>Use Submit this day once the day reaches at least 8.00 hours. Closing this window automatically saves your draft.</small>
              )}'''
app = app.replace(old_message, new_message)

app_file.write_text(app)
PY

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist"
  rm -rf "$DIST_DIR"
fi

echo "==> Approved project-task visibility repair applied"
echo "==> Rebuild the frontend and restart projecttime-frontend-public.service next."
