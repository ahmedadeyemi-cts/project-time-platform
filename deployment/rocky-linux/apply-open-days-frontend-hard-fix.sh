#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

# Add day-level edit helpers. This works even if the backend only returns saved entries
# and has not yet returned a full dayStatuses array.
old = """  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isTimesheetEditable = timesheet.data?.canEdit ?? ['draft', 'manager_declined'].includes(currentTimesheetStatus);
"""
new = """  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isTimesheetEditable = timesheet.data?.canEdit ?? ['draft', 'manager_declined'].includes(currentTimesheetStatus);
  const isAnyDayEditable = days.length === 0 || days.some((day) => isDayEditable(day.date));
"""
if old in app and 'const isAnyDayEditable =' not in app:
    app = app.replace(old, new, 1)

if 'function getDayStatus(workDate)' not in app:
    insert_before = """  const databaseSummary = useMemo(() => {
"""
    helper = """  function getDayStatus(workDate) {
    const apiDayStatus = timesheet.data?.dayStatuses?.find((dayStatus) => dayStatus.workDate === workDate);
    if (apiDayStatus) return apiDayStatus;

    const submittedEntryExists = (timesheet.data?.entries ?? []).some(
      (entry) => entry.workDate === workDate && entry.status === 'submitted'
    );

    return {
      workDate,
      status: submittedEntryExists ? 'submitted' : 'draft',
      canEdit: !submittedEntryExists,
      canUnlock: submittedEntryExists && Boolean(timesheet.data?.canUnlock),
      unlockMessage: submittedEntryExists
        ? 'This submitted day is locked. Use Unlock if it is within the allowed correction window, or contact your manager.'
        : 'This day is open for time entry.'
    };
  }

  function isDayEditable(workDate) {
    return getDayStatus(workDate).canEdit !== false;
  }

"""
    if insert_before not in app:
        raise SystemExit('ERROR: Could not find insertion point for day-level edit helpers.')
    app = app.replace(insert_before, helper + insert_before, 1)

# Replace function-level edit guards.
app = re.sub(
    r"function updateEntry\((.*?)\) \{\n\s*if \(!isTimesheetEditable\) return;",
    r"function updateEntry(\1) {\n    if (!isDayEditable(date)) return;",
    app,
    count=1,
    flags=re.S,
)

app = re.sub(
    r"function addCategory\((.*?)\) \{\n\s*if \(!isTimesheetEditable\) return;",
    r"function addCategory(\1) {\n    if (!isAnyDayEditable) return;",
    app,
    count=1,
    flags=re.S,
)

app = re.sub(
    r"function removeRow\((.*?)\) \{\n\s*if \(!isTimesheetEditable\) return;",
    r"function removeRow(\1) {\n    if (!isAnyDayEditable) return;",
    app,
    count=1,
    flags=re.S,
)

app = re.sub(
    r"function saveDraft\(\) \{\n\s*if \(!isTimesheetEditable \|\| isSaving\) return;",
    "function saveDraft() {\n    if (!isAnyDayEditable || isSaving) return;",
    app,
    count=1,
)

app = re.sub(
    r"function resetTimesheet\(\) \{\n\s*if \(!isTimesheetEditable\) return;",
    "function resetTimesheet() {\n    if (!isAnyDayEditable) return;",
    app,
    count=1,
)

# Allow selecting cells even when the week header is submitted; the day-level lock decides if fields are editable.
app = re.sub(
    r"function openEntryDetails\(rowId, date, type\) \{\n\s*if \(!isTimesheetEditable\) return;\n\s*setSelectedCell\(\{ rowId, date, type \}\);\n\s*\}",
    "function openEntryDetails(rowId, date, type) {\n    setSelectedCell({ rowId, date, type });\n  }",
    app,
    count=1,
)

# Toolbar and activity cards should use open-day editability, not whole-week editability.
app = app.replace("disabled={!isTimesheetEditable || isSaving}>Reset", "disabled={!isAnyDayEditable || isSaving}>Reset")
app = app.replace("disabled={!isTimesheetEditable || isSaving}>Save draft", "disabled={!isAnyDayEditable || isSaving}>Save draft")
app = app.replace("disabled={alreadyAdded || !isTimesheetEditable}", "disabled={alreadyAdded || !isAnyDayEditable}")

# Time cells should lock only when that specific day is submitted.
app = app.replace("disabled={!isTimesheetEditable}", "disabled={!isDayEditable(day.date)}")
app = app.replace("disabled={!isTimesheetEditable && isDayEditable(day.date)}", "disabled={!isDayEditable(day.date)}")

# If modal fields exist, lock them only by selected day.
app = app.replace("disabled={!isTimesheetEditable}", "disabled={selectedCell ? !isDayEditable(selectedCell.date) : true}")

# Add a visible note when the week status says submitted but only specific days should be locked.
needle = """          <span>Total: <strong>{formatNumber(grandTotal)}</strong></span>
        </div>
"""
replacement = """          <span>Total: <strong>{formatNumber(grandTotal)}</strong></span>
          {currentTimesheetStatus === 'submitted' ? (
            <span className="unlock-message">Submitted days are locked individually. Open days remain editable.</span>
          ) : null}
        </div>
"""
if needle in app and 'Submitted days are locked individually' not in app:
    app = app.replace(needle, replacement, 1)

app_file.write_text(app)
PY

echo "==> Open-day frontend hard fix applied"
echo "==> Validate source changes with:"
echo "grep -n \"isAnyDayEditable\|isDayEditable\|Submitted days are locked individually\" src/frontend/project-time-web/src/App.jsx"
