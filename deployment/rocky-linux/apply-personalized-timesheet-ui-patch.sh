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

if [ ! -f "$CSS_FILE" ]; then
  echo "ERROR: Missing $CSS_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path

repo = Path('/opt/project-time-platform/app/project-time-platform')
app_file = repo / 'src/frontend/project-time-web/src/App.jsx'
css_file = repo / 'src/frontend/project-time-web/src/timesheet.css'
app = app_file.read_text()
css = css_file.read_text()

# Add state for preferences and holidays.
if "const [timesheetPreferences" not in app:
    app = app.replace(
        "  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });",
        "  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });\n  const [timesheetPreferences, setTimesheetPreferences] = useState({ loading: true, data: null, error: null });\n  const [companyHolidays, setCompanyHolidays] = useState({ loading: true, data: null, error: null });"
    )

# Add reminder helper near statusToLabel/function helpers.
if "function getVacationHolidayReminder" not in app:
    helper = r'''
function getVacationHolidayReminder(row) {
  if (!row || !['VACATION', 'HOLIDAY'].includes(row.categoryCode)) return null;

  return 'Reminder: The code "Vacation" should be used for PTO. "Holiday" should be used only for company-paid holidays and your floating holiday. If you are taking PTO and a time entry deadline is approaching, your time should be submitted before you take your time off. All resources are required to submit 40 hours of time each week.';
}

'''
    app = app.replace('function statusToLabel(status, totalHours) {', helper + 'function statusToLabel(status, totalHours) {', 1)

# Extend startup fetches for preferences and holidays.
if "preferencesResult" not in app:
    app = app.replace(
        "fetchJson(`/api/assignments/open-tasks?weekStart=${selectedWeekStart}`)",
        "fetchJson(`/api/assignments/open-tasks?weekStart=${selectedWeekStart}`),\n          fetchJson('/api/users/timesheet-preferences'),\n          fetchJson(`/api/holidays?year=${selectedWeekStart.slice(0, 4)}`)"
    )
    app = app.replace(
        "fetchJson('/api/reporting/executive-dashboard')",
        "fetchJson('/api/reporting/executive-dashboard')"
    )
    app = app.replace(
        "const [healthResult, dbResult, schemaResult, timesheetResult, groupResult, locationsResult, policyResult, targetsResult, openTasksResult] = await Promise.all([",
        "const [healthResult, dbResult, schemaResult, timesheetResult, groupResult, locationsResult, policyResult, targetsResult, openTasksResult, preferencesResult, holidaysResult] = await Promise.all(["
    )
    app = app.replace(
        "setOpenTasks({ loading: false, data: openTasksResult, error: null });",
        "setOpenTasks({ loading: false, data: openTasksResult, error: null });\n          setTimesheetPreferences({ loading: false, data: preferencesResult, error: null });\n          setCompanyHolidays({ loading: false, data: holidaysResult, error: null });"
    )
    app = app.replace(
        "setOpenTasks((current) => ({ ...current, loading: false, error: message }));",
        "setOpenTasks((current) => ({ ...current, loading: false, error: message }));\n          setTimesheetPreferences((current) => ({ ...current, loading: false, error: message }));\n          setCompanyHolidays((current) => ({ ...current, loading: false, error: message }));"
    )

# Patch rehydration logic so no global defaults are inserted. Defaults only come from user's preferences.
app = app.replace(
    "const defaults = categories.filter((category) => ['ADMINISTRATIVE', 'PEER_SUPPORT'].includes(category.code) && !hiddenRows.has(`non-project-${category.code}`));",
    "const userDefaultCodes = timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? [];\n    const defaults = categories.filter((category) => userDefaultCodes.includes(category.code) && !hiddenRows.has(`non-project-${category.code}`));"
)

# Remove old fallback defaults. If user did not set defaults, there should be no preloaded rows.
app = app.replace(
    "const fallback = categories.slice(0, 2).filter((category) => !hiddenRows.has(`non-project-${category.code}`));",
    "const fallback = [];"
)

# Auto-add company holiday rows to the visible draft grid when preference allows.
if "const holidaysForWeek" not in app:
    app = app.replace(
        "    savedEntries.forEach((entry) => {\n      if (entry.rowType === 'nonProject' && entry.categoryCode && !rowMap.has(`non-project-${entry.categoryCode}`)) {",
        "    const holidaysForWeek = (companyHolidays.data?.holidays ?? []).filter((holiday) => days.some((day) => day.date === holiday.holidayDate));\n    const shouldAutoAddHolidays = timesheetPreferences.data?.autoAddHolidays !== false;\n    const holidayCategory = categories.find((category) => category.code === 'HOLIDAY');\n    if (shouldAutoAddHolidays && holidayCategory && holidaysForWeek.length > 0 && !hiddenRows.has('non-project-HOLIDAY')) {\n      rowMap.set('non-project-HOLIDAY', categoryToRow(holidayCategory));\n    }\n\n    savedEntries.forEach((entry) => {\n      if (entry.rowType === 'nonProject' && entry.categoryCode && !rowMap.has(`non-project-${entry.categoryCode}`)) {"
    )

if "holidayEntryKey" not in app:
    app = app.replace(
        "    savedEntries.forEach((entry) => {\n      let rowId = null;",
        "    if (shouldAutoAddHolidays && holidayCategory && rowMap.has('non-project-HOLIDAY')) {\n      holidaysForWeek.forEach((holiday) => {\n        const holidayEntryKey = getEntryKey('non-project-HOLIDAY', holiday.holidayDate, 'normal');\n        const alreadySaved = savedEntries.some((entry) => entry.workDate === holiday.holidayDate && entry.categoryCode === 'HOLIDAY');\n        if (!alreadySaved && !entryMap[holidayEntryKey]) {\n          entryMap[holidayEntryKey] = {\n            hours: (holiday.autoPopulateHours ?? 8).toString(),\n            comment: holiday.holidayName ?? 'Company holiday',\n            workLocationGroupId: locationGroups.data?.groups?.[0]?.id ?? '',\n            workLocationId: locations.data?.locations?.[0]?.id ?? '',\n            savedStatus: 'draft'\n          };\n        }\n      });\n    }\n\n    savedEntries.forEach((entry) => {\n      let rowId = null;"
    )

# Add current preference and holiday states to effect dependencies.
app = app.replace(
    "openTasks.data?.count, hiddenRowsRevision]);",
    "openTasks.data?.count, timesheetPreferences.data?.defaultNonProjectCategoryCodes?.join(','), timesheetPreferences.data?.autoAddHolidays, companyHolidays.data?.count, hiddenRowsRevision]);"
)

# Add action buttons in activity cards for setting/removing personal defaults.
if "async function savePersonalDefaults" not in app:
    functions = r'''
  async function savePersonalDefaults(defaultCodes) {
    const currentPreferences = timesheetPreferences.data ?? {};
    const result = await postJson('/api/users/timesheet-preferences', {
      defaultNonProjectCategoryCodes: defaultCodes,
      defaultProjectTaskIds: currentPreferences.defaultProjectTaskIds ?? [],
      autoAddHolidays: currentPreferences.autoAddHolidays !== false,
      weeklyReminderEnabled: currentPreferences.weeklyReminderEnabled !== false
    });
    setTimesheetPreferences({ loading: false, data: result.preferences, error: null });
    setSaveStatus('Personal defaults saved');
  }

  async function toggleCategoryDefault(categoryCode) {
    const currentCodes = new Set(timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []);
    if (currentCodes.has(categoryCode)) currentCodes.delete(categoryCode);
    else currentCodes.add(categoryCode);
    await savePersonalDefaults([...currentCodes]);
  }

'''
    app = app.replace('  function addCategory(category) {', functions + '  function addCategory(category) {', 1)

# Insert reminder note in modal for Vacation/Holiday.
if "getVacationHolidayReminder(selectedRow)" not in app:
    app = app.replace(
        "              <div className=\"detail-form modal-detail-form\">",
        "              {getVacationHolidayReminder(selectedRow) ? (\n                <div className=\"policy-reminder\">{getVacationHolidayReminder(selectedRow)}</div>\n              ) : null}\n\n              <div className=\"detail-form modal-detail-form\">"
    )

# Add default toggle button into category cards in a conservative way.
if "Personal default" not in app:
    app = app.replace(
        "<small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>",
        "<small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>\n                         <span className=\"default-toggle\" onClick={(event) => { event.stopPropagation(); void toggleCategoryDefault(category.code); }}>\n                           {(timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []).includes(category.code) ? 'Personal default' : 'Set as default'}\n                         </span>"
    )

if '.policy-reminder' not in css:
    css += r'''

.policy-reminder {
  border: 1px solid rgba(0, 87, 146, 0.25);
  background: rgba(0, 87, 146, 0.08);
  color: var(--text-color);
  border-radius: 0.85rem;
  padding: 0.85rem 1rem;
  margin: 1rem 0;
  line-height: 1.45;
  font-weight: 700;
}

.default-toggle {
  display: inline-flex;
  width: fit-content;
  margin-top: 0.4rem;
  border-radius: 999px;
  padding: 0.25rem 0.6rem;
  background: rgba(0, 87, 146, 0.08);
  color: var(--brand-blue);
  font-weight: 800;
  cursor: pointer;
}
'''

app_file.write_text(app)
css_file.write_text(css)
PY

if [ -d "$DIST_DIR" ]; then
  echo "==> Removing stale frontend dist"
  rm -rf "$DIST_DIR"
fi

echo "==> Personalized timesheet UI patch applied"
echo "==> There are no global default rows. Users can set their own defaults from the activity cards. Vacation/Holiday display policy reminders. Holidays can auto-populate 8 hours when loaded."
