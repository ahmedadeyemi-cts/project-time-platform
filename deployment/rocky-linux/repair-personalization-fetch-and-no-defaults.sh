#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
CSS_FILE="$REPO_DIR/src/frontend/project-time-web/src/timesheet.css"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
css_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/timesheet.css')
app = app_file.read_text()
css = css_file.read_text() if css_file.exists() else ''

# Ensure state objects exist.
if "const [openTasks, setOpenTasks]" not in app:
    app = app.replace(
        "  const [activitySource, setActivitySource] = useState('nonProject');",
        "  const [activitySource, setActivitySource] = useState('nonProject');\n  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });"
    )
if "const [remainingModules, setRemainingModules]" not in app:
    app = app.replace(
        "  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });",
        "  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });\n  const [remainingModules, setRemainingModules] = useState({ loading: true, data: null, error: null });"
    )
if "const [timesheetPreferences, setTimesheetPreferences]" not in app:
    app = app.replace(
        "  const [remainingModules, setRemainingModules] = useState({ loading: true, data: null, error: null });",
        "  const [remainingModules, setRemainingModules] = useState({ loading: true, data: null, error: null });\n  const [timesheetPreferences, setTimesheetPreferences] = useState({ loading: true, data: null, error: null });\n  const [companyHolidays, setCompanyHolidays] = useState({ loading: true, data: null, error: null });"
    )
if "const [hiddenRowsRevision, setHiddenRowsRevision]" not in app:
    app = app.replace(
        "  const [activitySource, setActivitySource] = useState('nonProject');",
        "  const [activitySource, setActivitySource] = useState('nonProject');\n  const [hiddenRowsRevision, setHiddenRowsRevision] = useState(0);"
    )

# Replace the main startup useEffect so every referenced result is defined.
new_load_effect = r'''  useEffect(() => {
    let cancelled = false;

    async function loadStatus() {
      setTimesheet({ loading: true, data: null, error: null });
      setTimesheetPreferences((current) => ({ ...current, loading: true, error: null }));
      setCompanyHolidays((current) => ({ ...current, loading: true, error: null }));
      setOpenTasks((current) => ({ ...current, loading: true, error: null }));
      setRemainingModules((current) => ({ ...current, loading: true, error: null }));

      try {
        const [
          healthResult,
          dbResult,
          schemaResult,
          timesheetResult,
          groupResult,
          locationsResult,
          policyResult,
          targetsResult,
          openTasksResult,
          preferencesResult,
          holidaysResult,
          projectIntakeResult,
          projectManagementResult,
          resourceCapacityResult,
          expenseSummaryResult,
          invoicingSummaryResult,
          executiveDashboardResult
        ] = await Promise.all([
          fetchJson('/health'),
          fetchJson('/api/db-health'),
          fetchJson('/api/schema/tables'),
          fetchJson(`/api/timesheets/week?weekStart=${selectedWeekStart}`),
          fetchJson('/api/work-location-groups'),
          fetchJson('/api/work-locations'),
          fetchJson('/api/utilization/policies'),
          fetchJson('/api/utilization/targets'),
          fetchJson(`/api/assignments/open-tasks?weekStart=${selectedWeekStart}`),
          fetchJson('/api/users/timesheet-preferences'),
          fetchJson(`/api/holidays?year=${selectedWeekStart.slice(0, 4)}`),
          fetchJson('/api/project-intake/summary'),
          fetchJson('/api/project-management/summary'),
          fetchJson(`/api/resource-scheduling/capacity?weekStart=${selectedWeekStart}`),
          fetchJson('/api/expenses/summary'),
          fetchJson('/api/invoicing/summary'),
          fetchJson('/api/reporting/executive-dashboard')
        ]);

        if (!cancelled) {
          setApiHealth({ loading: false, data: healthResult, error: null });
          setDbHealth({ loading: false, data: dbResult, error: null });
          setSchema({ loading: false, data: schemaResult, error: null });
          setTimesheet({ loading: false, data: timesheetResult, error: null });
          setLocationGroups({ loading: false, data: groupResult, error: null });
          setLocations({ loading: false, data: locationsResult, error: null });
          setUtilizationPolicies({ loading: false, data: policyResult, error: null });
          setUtilizationTargets({ loading: false, data: targetsResult, error: null });
          setOpenTasks({ loading: false, data: openTasksResult, error: null });
          setTimesheetPreferences({ loading: false, data: preferencesResult, error: null });
          setCompanyHolidays({ loading: false, data: holidaysResult, error: null });
          setRemainingModules({
            loading: false,
            error: null,
            data: {
              projectIntake: projectIntakeResult,
              projectManagement: projectManagementResult,
              resourceCapacity: resourceCapacityResult,
              expenses: expenseSummaryResult,
              invoicing: invoicingSummaryResult,
              executiveDashboard: executiveDashboardResult
            }
          });
        }
      } catch (error) {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : 'Unknown error';
          setApiHealth((current) => ({ ...current, loading: false, error: message }));
          setDbHealth((current) => ({ ...current, loading: false, error: message }));
          setSchema((current) => ({ ...current, loading: false, error: message }));
          setTimesheet((current) => ({ ...current, loading: false, error: message }));
          setLocationGroups((current) => ({ ...current, loading: false, error: message }));
          setLocations((current) => ({ ...current, loading: false, error: message }));
          setUtilizationPolicies((current) => ({ ...current, loading: false, error: message }));
          setUtilizationTargets((current) => ({ ...current, loading: false, error: message }));
          setOpenTasks((current) => ({ ...current, loading: false, error: message }));
          setTimesheetPreferences((current) => ({ ...current, loading: false, error: message }));
          setCompanyHolidays((current) => ({ ...current, loading: false, error: message }));
          setRemainingModules((current) => ({ ...current, loading: false, error: message }));
        }
      }
    }

    loadStatus();

    return () => {
      cancelled = true;
    };
  }, [selectedWeekStart]);'''

app, count = re.subn(
    r"  useEffect\(\(\) => \{\n    let cancelled = false;.*?\n  \}, \[selectedWeekStart\]\);",
    new_load_effect,
    app,
    count=1,
    flags=re.S,
)
if count == 0:
    raise SystemExit('ERROR: Could not replace main loadStatus useEffect.')

# Add local hidden-row helpers if missing.
helpers = r'''
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
    if (hiddenRows.delete(rowId)) saveHiddenRows(hiddenRows);
  }

'''
if "function getHiddenRowsKey" not in app:
    app = app.replace("  function getDayStatus(workDate) {", helpers + "  function getDayStatus(workDate) {", 1)

# Replace rehydration useEffect so there are no global defaults.
new_rehydrate = r'''  useEffect(() => {
    const categories = timesheet.data?.nonProjectCategories ?? [];
    const assignedOpenTasks = openTasks.data?.tasks ?? [];
    const savedEntries = timesheet.data?.entries ?? [];
    const hiddenRows = getHiddenRows(timesheet.data?.weekStart ?? selectedWeekStart);
    const daysForWeek = timesheet.data?.days ?? [];

    const userDefaultCodes = timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? [];
    const rowMap = new Map();

    // Hyper-personalized default rows: no global defaults. Rows are added only from the user's saved defaults,
    // saved time entries, manually selected tasks/categories, or auto-added holidays.
    categories
      .filter((category) => userDefaultCodes.includes(category.code) && !hiddenRows.has(`non-project-${category.code}`))
      .forEach((category) => rowMap.set(`non-project-${category.code}`, categoryToRow(category)));

    const holidaysForWeek = (companyHolidays.data?.holidays ?? []).filter((holiday) => daysForWeek.some((day) => day.date === holiday.holidayDate));
    const shouldAutoAddHolidays = timesheetPreferences.data?.autoAddHolidays !== false;
    const holidayCategory = categories.find((category) => category.code === 'HOLIDAY');
    if (shouldAutoAddHolidays && holidayCategory && holidaysForWeek.length > 0 && !hiddenRows.has('non-project-HOLIDAY')) {
      rowMap.set('non-project-HOLIDAY', categoryToRow(holidayCategory));
    }

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
        rowMap.set(rowId, matchingTask ? taskToRow(matchingTask) : {
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
    });

    const entryMap = {};

    if (shouldAutoAddHolidays && holidayCategory && rowMap.has('non-project-HOLIDAY')) {
      holidaysForWeek.forEach((holiday) => {
        const key = getEntryKey('non-project-HOLIDAY', holiday.holidayDate, 'normal');
        const alreadySaved = savedEntries.some((entry) => entry.workDate === holiday.holidayDate && entry.categoryCode === 'HOLIDAY');
        if (!alreadySaved) {
          entryMap[key] = {
            hours: (holiday.autoPopulateHours ?? 8).toString(),
            comment: holiday.holidayName ?? 'Company holiday',
            workLocationGroupId: locationGroups.data?.groups?.[0]?.id ?? '',
            workLocationId: locations.data?.locations?.[0]?.id ?? '',
            savedStatus: 'draft'
          };
        }
      });
    }

    savedEntries.forEach((entry) => {
      let rowId = null;
      if (entry.rowType === 'nonProject' && entry.categoryCode) rowId = `non-project-${entry.categoryCode}`;
      if (entry.rowType === 'projectTask' && entry.projectId && entry.taskId) rowId = `project-task-${entry.projectId}-${entry.taskId}`;
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
    const holidayDraftTotal = Object.values(entryMap).reduce((total, entry) => total + Number(entry.hours || 0), 0);
    setSubmissionStatus(statusToLabel(timesheet.data?.status, savedTotal || holidayDraftTotal));
    setSaveStatus(savedEntries.length > 0 ? `Loaded ${savedEntries.length} saved time entr${savedEntries.length === 1 ? 'y' : 'ies'}` : 'Not saved yet');
  }, [timesheet.data?.weekStart, timesheet.data?.timesheetId, timesheet.data?.status, timesheet.data?.entries?.length, openTasks.data?.count, timesheetPreferences.data?.defaultNonProjectCategoryCodes?.join(','), timesheetPreferences.data?.autoAddHolidays, companyHolidays.data?.count, hiddenRowsRevision]);'''

app, rcount = re.subn(
    r"  useEffect\(\(\) => \{\n    const categories = timesheet\.data\?\.nonProjectCategories \?\? \[\];.*?\n  \}, \[timesheet\.data\?\.weekStart, timesheet\.data\?\.timesheetId, timesheet\.data\?\.status.*?\]\);",
    new_rehydrate,
    app,
    count=1,
    flags=re.S,
)
if rcount == 0:
    raise SystemExit('ERROR: Could not replace rehydration useEffect.')

# Ensure no duplicate assignedOpenTasks declaration in main render section.
app = re.sub(
    r"  const categories = timesheet\.data\?\.nonProjectCategories \?\? \[\];\n(?:\s*const assignedOpenTasks = openTasks\.data\?\.tasks \?\? \[\];\n)*",
    "  const categories = timesheet.data?.nonProjectCategories ?? [];\n  const assignedOpenTasks = openTasks.data?.tasks ?? [];\n",
    app,
    count=1,
)

# Save week with empty layout should not re-add rows or complain.
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

# Remove row should persist hidden row preference.
app = app.replace(
"""    setActiveRows((current) => current.filter((row) => row.id !== rowId));
    setEntries((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith(`${rowId}|`))));""",
"""    hideRowForCurrentWeek(rowId);
    setActiveRows((current) => current.filter((row) => row.id !== rowId));
    setEntries((current) => Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith(`${rowId}|`))));"""
)

# Reset should restore user default layout by clearing hidden row preference.
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

if '.empty-activity-state strong' not in css:
    css += """

.empty-activity-state strong {
  display: block;
}
"""
css_file.write_text(css)
PY

if [ -d "$DIST_DIR" ]; then
  rm -rf "$DIST_DIR"
fi

echo "==> Personalization fetch/no-defaults repair applied"
echo "==> This resolves preferencesResult is not defined and removes global default rows."
