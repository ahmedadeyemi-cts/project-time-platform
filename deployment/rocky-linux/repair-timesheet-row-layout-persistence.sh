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

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

# Add a small local layout preference state. This is intentionally frontend-side
# so a user can remove default empty rows for a week even when there are no time
# entries to send to the backend yet.
if "const [hiddenRowsRevision, setHiddenRowsRevision]" not in app:
    app = app.replace(
        "  const [activitySource, setActivitySource] = useState('nonProject');",
        "  const [activitySource, setActivitySource] = useState('nonProject');\n  const [hiddenRowsRevision, setHiddenRowsRevision] = useState(0);"
    )

helper = r'''
  function getHiddenRowsKey(weekStart = selectedWeekStart) {
    return `projectPulseHiddenRows:${weekStart}`;
  }

  function getHiddenRows(weekStart = selectedWeekStart) {
    try {
      return new Set(JSON.parse(window.localStorage.getItem(getHiddenRowsKey(weekStart)) ?? '[]'));
    } catch {
      return new Set();
    }
  }

  function saveHiddenRows(hiddenRows, weekStart = selectedWeekStart) {
    window.localStorage.setItem(getHiddenRowsKey(weekStart), JSON.stringify([...hiddenRows]));
    setHiddenRowsRevision((value) => value + 1);
  }

  function hideRowForCurrentWeek(rowId) {
    const hiddenRows = getHiddenRows();
    hiddenRows.add(rowId);
    saveHiddenRows(hiddenRows);
  }

  function unhideRowForCurrentWeek(rowId) {
    const hiddenRows = getHiddenRows();
    if (hiddenRows.delete(rowId)) {
      saveHiddenRows(hiddenRows);
    }
  }

'''

if "function getHiddenRowsKey" not in app:
    anchor = "  function getDayStatus(workDate) {"
    if anchor not in app:
        raise SystemExit('ERROR: Could not find getDayStatus anchor.')
    app = app.replace(anchor, helper + anchor, 1)

# In the rehydration useEffect, read hidden rows and exclude hidden default rows.
app = app.replace(
    "    const savedEntries = timesheet.data?.entries ?? [];\n\n    if (categories.length === 0",
    "    const savedEntries = timesheet.data?.entries ?? [];\n    const hiddenRows = getHiddenRows(timesheet.data?.weekStart ?? selectedWeekStart);\n\n    if (categories.length === 0"
)

app = app.replace(
    "    const defaults = categories.filter((category) => ['ADMINISTRATIVE', 'PEER_SUPPORT'].includes(category.code));",
    "    const defaults = categories.filter((category) => ['ADMINISTRATIVE', 'PEER_SUPPORT'].includes(category.code) && !hiddenRows.has(`non-project-${category.code}`));"
)

app = app.replace(
    "    const fallback = categories.slice(0, 2);",
    "    const fallback = categories.slice(0, 2).filter((category) => !hiddenRows.has(`non-project-${category.code}`));"
)

# Make the effect re-run if hidden row preferences change in-session.
app = app.replace(
    "openTasks.data?.count]);",
    "openTasks.data?.count, hiddenRowsRevision]);"
)

# When a user adds a row back from the activity panel, unhide it for that week.
app = app.replace(
"""    const row = categoryToRow(category);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');""",
"""    const row = categoryToRow(category);
    unhideRowForCurrentWeek(row.id);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');"""
)

app = app.replace(
"""    const row = taskToRow(task);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');""",
"""    const row = taskToRow(task);
    unhideRowForCurrentWeek(row.id);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));
    setSaveStatus('Unsaved changes');"""
)

# When a row is removed, remember that preference for the current week so it does
# not reappear on refresh simply because it is a default row with no saved time.
app = app.replace(
"""    setActiveRows((current) => current.filter((row) => row.id !== rowId));
    setEntries((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith(`${rowId}|`))));""",
"""    hideRowForCurrentWeek(rowId);
    setActiveRows((current) => current.filter((row) => row.id !== rowId));
    setEntries((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith(`${rowId}|`))));"""
)

# Save week should not show an error when the user's intent is saving an empty
# layout after removing rows. It should persist the local row-layout preference.
app = app.replace(
"""    if (grandTotal <= 0) {
      setSubmissionStatus('Add time before submitting.');
      return;
    }""",
"""    if (grandTotal <= 0) {
      setSubmissionStatus('Draft');
      setSaveStatus('Layout saved. No time entries for this week yet.');
      return;
    }"""
)

# Reset should clear row layout preferences for the current week and restore default rows.
app = app.replace(
"""    setEntries({});
    setSelectedCell(null);
    setSubmissionStatus('Draft');
    setSaveStatus('Unsaved changes');""",
"""    saveHiddenRows(new Set());
    setEntries({});
    setSelectedCell(null);
    setSubmissionStatus('Draft');
    setSaveStatus('Layout reset');"""
)

app_file.write_text(app)
PY

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist"
  rm -rf "$DIST_DIR"
fi

echo "==> Timesheet row layout persistence repair applied"
echo "==> Removed empty default rows now stay removed after refresh for the selected week."
