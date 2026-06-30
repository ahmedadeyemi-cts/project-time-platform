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

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
app = app_file.read_text()

if 'function getDayStatus(workDate)' not in app:
    raise SystemExit('ERROR: Daily submission functions were not found in App.jsx. Run apply-daily-submission-policy-patch.sh before this fix script.')

# Add an app-level helper flag that says at least one day is still editable.
if 'const isAnyDayEditable =' not in app:
    app = app.replace(
"""  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isTimesheetEditable = timesheet.data?.canEdit ?? ['draft', 'manager_declined'].includes(currentTimesheetStatus);
  const canRequestUnlock = currentTimesheetStatus === 'submitted';
""",
"""  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isTimesheetEditable = timesheet.data?.canEdit ?? ['draft', 'manager_declined'].includes(currentTimesheetStatus);
  const isAnyDayEditable = days.length === 0 || days.some((day) => getDayStatus(day.date).canEdit !== false);
  const canRequestUnlock = currentTimesheetStatus === 'submitted';
""")

# Saving a draft should be allowed when any day in the week remains open.
app = app.replace(
"""  async function saveDraft() {
    if (!isTimesheetEditable || isSaving) return;
""",
"""  async function saveDraft() {
    if (!isAnyDayEditable || isSaving) return;
""")

# Adding rows should be allowed when there is at least one editable day left.
app = app.replace(
"""  function addCategory(category) {
    if (!isTimesheetEditable) return;
""",
"""  function addCategory(category) {
    if (!isAnyDayEditable) return;
""")

# Removing a row should be allowed only when there is at least one editable day left.
app = app.replace(
"""  function removeRow(rowId) {
    if (!isTimesheetEditable) return;
""",
"""  function removeRow(rowId) {
    if (!isAnyDayEditable) return;
""")

# Reset should not be blocked just because a previous day is submitted; it is used for still-open day work.
app = app.replace(
"""  function resetTimesheet() {
    if (!isTimesheetEditable) return;
""",
"""  function resetTimesheet() {
    if (!isAnyDayEditable) return;
""")

# Activity cards should be enabled if there is at least one open day.
app = app.replace(
"""                        disabled={alreadyAdded || !isTimesheetEditable}
""",
"""                        disabled={alreadyAdded || !isAnyDayEditable}
""")

# Toolbar buttons should use day-level editability, not whole-week editability.
app = app.replace(
"""            <button type="button" onClick={resetTimesheet} disabled={!isTimesheetEditable || isSaving}>Reset</button>
            <button type="button" onClick={saveDraft} disabled={!isTimesheetEditable || isSaving}>Save draft</button>
""",
"""            <button type="button" onClick={resetTimesheet} disabled={!isAnyDayEditable || isSaving}>Reset</button>
            <button type="button" onClick={saveDraft} disabled={!isAnyDayEditable || isSaving}>Save draft</button>
""")

# The weekly submit button should not imply that the entire week must be locked. Keep it available only for draft weekly mode.
app = app.replace(
"""            <button type="button" className="primary-action" onClick={handleSubmit} disabled={!isTimesheetEditable || isSaving}>Submit</button>
""",
"""            <button type="button" className="primary-action" onClick={handleSubmit} disabled={!isTimesheetEditable || isSaving}>Submit week</button>
""")

# Each time cell should be disabled only if that specific day is locked.
app = app.replace(
"""                              disabled={!isTimesheetEditable && isDayEditable(day.date)}
""",
"""                              disabled={!isDayEditable(day.date)}
""")
app = app.replace(
"""                              disabled={!isTimesheetEditable}
""",
"""                              disabled={!isDayEditable(day.date)}
""")

# Make the submitted weekly banner less misleading once daily submission is enabled.
app = app.replace(
"""          {currentTimesheetStatus === 'submitted' && timesheet.data?.unlockMessage ? (
            <span className="unlock-message">{timesheet.data.unlockMessage}</span>
          ) : null}
""",
"""          {currentTimesheetStatus === 'submitted' && timesheet.data?.unlockMessage ? (
            <span className="unlock-message">Submitted days are locked individually. Open days remain editable.</span>
          ) : null}
""")

app_file.write_text(app)
PY

echo "==> Daily open-day edit fix applied"
echo "==> Validate with: grep -n \"isAnyDayEditable\|Submit week\|disabled={!isDayEditable\" src/frontend/project-time-web/src/App.jsx"
