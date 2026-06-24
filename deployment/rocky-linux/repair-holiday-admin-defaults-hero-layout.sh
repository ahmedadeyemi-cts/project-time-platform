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
import re

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
css_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/timesheet.css')
app = app_file.read_text()
css = css_file.read_text()

# -----------------------------
# 1) Ensure preference/holiday state exists
# -----------------------------
if "const [openTasks, setOpenTasks]" not in app:
    app = app.replace(
        "  const [activitySource, setActivitySource] = useState('nonProject');",
        "  const [activitySource, setActivitySource] = useState('nonProject');\n  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });"
    )

if "const [timesheetPreferences, setTimesheetPreferences]" not in app:
    app = app.replace(
        "  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });",
        "  const [openTasks, setOpenTasks] = useState({ loading: true, data: null, error: null });\n  const [timesheetPreferences, setTimesheetPreferences] = useState({ loading: true, data: null, error: null });\n  const [companyHolidays, setCompanyHolidays] = useState({ loading: true, data: null, error: null });"
    )

if "const [holidayUploadText, setHolidayUploadText]" not in app:
    app = app.replace(
        "  const [activitySource, setActivitySource] = useState('nonProject');",
        "  const [activitySource, setActivitySource] = useState('nonProject');\n  const [holidayUploadText, setHolidayUploadText] = useState('');\n  const [holidayUploadStatus, setHolidayUploadStatus] = useState('No holiday upload yet');\n  const [holidayUploadYear, setHolidayUploadYear] = useState(String(new Date().getFullYear()));"
    )
else:
    app = app.replace("useState('2026')", "useState(String(new Date().getFullYear()))")

# Add year option list inside component after activitySource state if missing.
if "const holidayYearOptions = useMemo" not in app:
    app = app.replace(
        "  const [activitySource, setActivitySource] = useState('nonProject');",
        "  const [activitySource, setActivitySource] = useState('nonProject');\n  const holidayYearOptions = useMemo(() => {\n    const currentYear = new Date().getFullYear();\n    return Array.from({ length: 7 }, (_, index) => String(currentYear - 2 + index));\n  }, []);"
    )

# -----------------------------
# 2) Professional hero section
# -----------------------------
hero_pattern = r'''      <section id="dashboard" className="hero">\n        <p className="eyebrow">US Signal Project Pulse</p>\n        <h1>Project Pulse: time, approval, utilization, and accounting workflow</h1>\n        <p className="hero-copy">\n          A focused internal platform for weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting\.\n        </p>\n      </section>'''
hero_replacement = r'''      <section id="dashboard" className="hero hero-polished">
        <div className="hero-content-block">
          <p className="eyebrow">US Signal Project Pulse</p>
          <h1>Operational command center for time, approvals, utilization, and billing readiness.</h1>
          <p className="hero-copy">
            Project Pulse brings weekly time entry, task-based project assignment, manager approval, project validation, accounting reconciliation, and utilization reporting into one internal workflow.
          </p>
          <div className="hero-pill-row">
            <span>Time entry</span>
            <span>Approval workflow</span>
            <span>Utilization</span>
            <span>Accounting readiness</span>
          </div>
        </div>
        <aside className="hero-side-card" aria-label="Platform direction">
          <strong>Built for Professional Services</strong>
          <span>Personalized defaults, holiday automation, reminders, approvals, and reporting.</span>
        </aside>
      </section>'''
app, hero_count = re.subn(hero_pattern, hero_replacement, app, count=1)
if hero_count == 0 and 'className="hero hero-polished"' not in app:
    # Less strict fallback.
    app = re.sub(
        r'      <section id="dashboard" className="hero">.*?      </section>',
        hero_replacement,
        app,
        count=1,
        flags=re.S,
    )

# -----------------------------
# 3) Default task/category helpers and visible controls
# -----------------------------
if "async function setRowAsPersonalDefault" not in app and "  function addCategory(category) {" in app:
    app = app.replace("  function addCategory(category) {", r'''  async function savePersonalDefaults(defaultCodes, defaultTaskIds) {
    const currentPreferences = timesheetPreferences.data ?? {};
    const result = await postJson('/api/users/timesheet-preferences', {
      defaultNonProjectCategoryCodes: defaultCodes,
      defaultProjectTaskIds: defaultTaskIds,
      autoAddHolidays: currentPreferences.autoAddHolidays !== false,
      weeklyReminderEnabled: currentPreferences.weeklyReminderEnabled !== false
    });
    setTimesheetPreferences({ loading: false, data: result.preferences, error: null });
    setSaveStatus('Personal defaults saved');
  }

  async function setRowAsPersonalDefault(row) {
    const defaultCodes = new Set(timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []);
    const defaultTaskIds = new Set(timesheetPreferences.data?.defaultProjectTaskIds ?? []);
    if (row.type === 'nonProject' && row.categoryCode) defaultCodes.add(row.categoryCode);
    if (row.type === 'projectTask' && row.taskId) defaultTaskIds.add(row.taskId);
    await savePersonalDefaults([...defaultCodes], [...defaultTaskIds]);
  }

  async function removeRowAsPersonalDefault(row) {
    const defaultCodes = new Set(timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []);
    const defaultTaskIds = new Set(timesheetPreferences.data?.defaultProjectTaskIds ?? []);
    if (row.type === 'nonProject' && row.categoryCode) defaultCodes.delete(row.categoryCode);
    if (row.type === 'projectTask' && row.taskId) defaultTaskIds.delete(row.taskId);
    await savePersonalDefaults([...defaultCodes], [...defaultTaskIds]);
  }

  function isRowPersonalDefault(row) {
    if (row.type === 'nonProject' && row.categoryCode) {
      return (timesheetPreferences.data?.defaultNonProjectCategoryCodes ?? []).includes(row.categoryCode);
    }
    if (row.type === 'projectTask' && row.taskId) {
      return (timesheetPreferences.data?.defaultProjectTaskIds ?? []).includes(row.taskId);
    }
    return false;
  }

  function addCategory(category) {''')

# Replace row action with default toggle and remove.
old_remove = '<button className="link-button" type="button" onClick={() => removeRow(row.id)} disabled={!isAnyDayEditable}>Remove</button>'
new_remove = '''<div className="row-action-stack">
                        <button className="link-button" type="button" onClick={() => isRowPersonalDefault(row) ? void removeRowAsPersonalDefault(row) : void setRowAsPersonalDefault(row)}>
                          {isRowPersonalDefault(row) ? 'Remove default' : 'Set default'}
                        </button>
                        <button className="link-button" type="button" onClick={() => removeRow(row.id)} disabled={!isAnyDayEditable}>Remove</button>
                      </div>'''
if old_remove in app and 'Remove default' not in app:
    app = app.replace(old_remove, new_remove, 1)

# Activity card default action for non-project categories.
small_line = "<small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>"
if small_line in app and 'Set as my default' not in app:
    app = app.replace(small_line, small_line + "\n                         <span className=\"default-toggle\" onClick={(event) => { event.stopPropagation(); void setRowAsPersonalDefault(categoryToRow(category)); }}>Set as my default</span>", 1)

# -----------------------------
# 4) Vacation/Holiday reminder in modal
# -----------------------------
if "function getVacationHolidayReminder" not in app:
    app = app.replace('function statusToLabel(status, totalHours = 0) {', r'''function getVacationHolidayReminder(row) {
  if (!row) return null;
  const code = (row.categoryCode ?? '').toUpperCase();
  const activity = (row.activity ?? '').toUpperCase();
  if (!['VACATION', 'HOLIDAY'].includes(code) && !['VACATION', 'HOLIDAY'].includes(activity)) return null;
  return 'The code "Vacation" should be used for PTO. "Holiday" should be used only for company-paid holidays and your floating holiday. If you are taking PTO and a time entry deadline is approaching, your time should be submitted before you take your time off. All resources are required to submit 40 hours of time each week.';
}

function statusToLabel(status, totalHours = 0) {''', 1)

if "getVacationHolidayReminder(selectedRow)" not in app and '<div className="detail-form modal-detail-form">' in app:
    app = app.replace('<div className="detail-form modal-detail-form">', """{getVacationHolidayReminder(selectedRow) ? (
                <div className="policy-reminder">{getVacationHolidayReminder(selectedRow)}</div>
              ) : null}

              <div className="detail-form modal-detail-form">""", 1)

# -----------------------------
# 5) Holiday admin section: dropdown year + current uploaded holidays display
# -----------------------------
if "async function loadHolidayAdminYear" not in app and "  function addCategory(category)" in app:
    app = app.replace("  function addCategory(category)", r'''  async function loadHolidayAdminYear(year) {
    setHolidayUploadYear(year);
    try {
      const refreshed = await fetchJson(`/api/holidays?year=${year}`);
      setCompanyHolidays({ loading: false, data: refreshed, error: null });
    } catch (error) {
      setCompanyHolidays((current) => ({ ...current, loading: false, error: error instanceof Error ? error.message : 'Failed to load holidays' }));
    }
  }

  function addCategory(category)''', 1)

if "async function importHolidayCsv" not in app and "  function addCategory(category)" in app:
    app = app.replace("  function addCategory(category)", r'''  async function importHolidayCsv() {
    setHolidayUploadStatus('Importing holidays...');
    try {
      const result = await postJson('/api/holidays/import-text', {
        year: Number.parseInt(holidayUploadYear, 10),
        filename: `holidays-${holidayUploadYear}.csv`,
        csvText: holidayUploadText
      });
      setHolidayUploadStatus(`Imported ${result.importedCount} holidays for ${result.year}`);
      await loadHolidayAdminYear(holidayUploadYear);
    } catch (error) {
      setHolidayUploadStatus(error instanceof Error ? error.message : 'Holiday import failed');
    }
  }

  function handleHolidayFileUpload(event) {
    const file = event.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => setHolidayUploadText(String(reader.result ?? ''));
    reader.readAsText(file);
  }

  function addCategory(category)''', 1)

holiday_panel = r'''
      <section id="holiday-admin" className="panel holiday-admin-panel">
        <div className="section-header compact">
          <div>
            <p className="eyebrow">Holiday administration</p>
            <h2>Yearly paid holiday upload</h2>
            <p className="muted">Select a year to view currently uploaded holidays, then upload or paste a CSV. Fixed holidays auto-populate 8.00 Holiday hours for eligible users when their selected week includes the holiday.</p>
          </div>
          <span className="pill">{companyHolidays.data?.count ?? 0} uploaded for {holidayUploadYear}</span>
        </div>

        <div className="holiday-upload-grid">
          <label>
            Year
            <select value={holidayUploadYear} onChange={(event) => void loadHolidayAdminYear(event.target.value)}>
              {holidayYearOptions.map((year) => <option value={year} key={year}>{year}</option>)}
            </select>
          </label>
          <label>
            Upload CSV
            <input type="file" accept=".csv,text/csv" onChange={handleHolidayFileUpload} />
          </label>
        </div>

        <textarea
          className="holiday-upload-textarea"
          value={holidayUploadText}
          onChange={(event) => setHolidayUploadText(event.target.value)}
          placeholder="holiday_date,holiday_name,holiday_type,is_floating_holiday,auto_populate_hours\n2026-01-01,New Year's Day,company_paid,false,8"
        />

        <div className="toolbar-actions holiday-upload-actions">
          <button type="button" className="primary-action" onClick={importHolidayCsv}>Import holidays</button>
          <span className="muted">{holidayUploadStatus}</span>
        </div>

        <div className="holiday-list-card">
          <div className="holiday-list-header">
            <h3>Currently uploaded holidays</h3>
            <span>{companyHolidays.data?.count ?? 0} records</span>
          </div>
          {(companyHolidays.data?.holidays ?? []).length === 0 ? (
            <p className="muted">No holidays uploaded for {holidayUploadYear} yet.</p>
          ) : null}
          {(companyHolidays.data?.holidays ?? []).map((holiday) => (
            <div className="module-list-row" key={holiday.holidayDate}>
              <strong>{holiday.holidayName}</strong>
              <span>{holiday.holidayDate} • {holiday.holidayType} • {formatNumber(holiday.autoPopulateHours)} hours</span>
            </div>
          ))}
        </div>
      </section>
'''
if 'id="holiday-admin"' not in app:
    if '      <section id="psa-modules"' in app:
        app = app.replace('      <section id="psa-modules"', holiday_panel + '\n      <section id="psa-modules"', 1)
    else:
        app = app.replace('      <section id="utilization"', holiday_panel + '\n      <section id="utilization"', 1)
else:
    # Replace existing holiday panel if present.
    app = re.sub(r'      <section id="holiday-admin" className="panel holiday-admin-panel">.*?\n      </section>', holiday_panel.rstrip(), app, count=1, flags=re.S)

# -----------------------------
# CSS: wide layout and polished sections
# -----------------------------
css += r'''

/* Professional hero + full-width layout refresh */
.app-shell {
  max-width: none !important;
  width: 100% !important;
  padding-left: clamp(0.75rem, 1.5vw, 1.5rem) !important;
  padding-right: clamp(0.75rem, 1.5vw, 1.5rem) !important;
}

.top-bar,
.hero,
.status-grid,
.panel,
.timesheet-page {
  max-width: none !important;
  width: 100% !important;
}

.hero-polished {
  display: grid !important;
  grid-template-columns: minmax(0, 1fr) minmax(280px, 420px);
  align-items: center;
  gap: clamp(1rem, 3vw, 3rem);
  padding: clamp(2rem, 4vw, 4rem) !important;
  background: linear-gradient(135deg, rgba(0, 87, 146, 0.12), rgba(255,255,255,0.95)) !important;
  border: 1px solid rgba(0, 87, 146, 0.14) !important;
}

.hero-polished h1 {
  max-width: 980px;
  margin-bottom: 1rem;
}

.hero-copy {
  max-width: 920px;
  font-size: clamp(1rem, 1.1vw, 1.15rem);
}

.hero-pill-row {
  display: flex;
  flex-wrap: wrap;
  gap: 0.55rem;
  margin-top: 1.25rem;
}

.hero-pill-row span,
.hero-side-card {
  border: 1px solid rgba(0, 87, 146, 0.16);
  background: rgba(255, 255, 255, 0.78);
  box-shadow: 0 16px 40px rgba(15, 23, 42, 0.08);
}

.hero-pill-row span {
  border-radius: 999px;
  padding: 0.45rem 0.8rem;
  font-weight: 900;
  color: var(--brand-blue, #005792);
}

.hero-side-card {
  border-radius: 1.25rem;
  padding: 1.25rem;
  display: grid;
  gap: 0.5rem;
}

.hero-side-card strong {
  font-size: 1.15rem;
}

.hero-side-card span {
  color: var(--muted-text, #5b6b89);
  line-height: 1.5;
}

.entry-grid-wrap {
  width: 100% !important;
  overflow-x: auto !important;
}

.entry-grid {
  min-width: 1450px;
}

.row-action-stack {
  display: grid;
  gap: 0.25rem;
  justify-items: start;
}

.default-toggle {
  display: inline-flex;
  width: fit-content;
  margin-top: 0.45rem;
  border-radius: 999px;
  padding: 0.25rem 0.65rem;
  background: rgba(0, 87, 146, 0.08);
  color: var(--brand-blue, #005792);
  font-weight: 900;
  cursor: pointer;
}

.policy-reminder {
  border: 1px solid rgba(0, 87, 146, 0.25);
  background: rgba(0, 87, 146, 0.08);
  border-radius: 0.85rem;
  padding: 0.85rem 1rem;
  margin: 1rem 0;
  line-height: 1.45;
  font-weight: 800;
}

.holiday-admin-panel {
  scroll-margin-top: 110px;
}

.holiday-upload-grid {
  display: grid;
  grid-template-columns: minmax(120px, 180px) minmax(260px, 1fr);
  gap: 1rem;
  margin-top: 1rem;
}

.holiday-upload-grid label {
  display: grid;
  gap: 0.4rem;
  font-weight: 800;
}

.holiday-upload-grid input,
.holiday-upload-grid select,
.holiday-upload-textarea {
  border: 1px solid var(--border-color, #d8dee8);
  border-radius: 0.8rem;
  padding: 0.75rem;
  font: inherit;
  background: var(--card-background, #fff);
  color: var(--text-color, #172033);
}

.holiday-upload-textarea {
  width: 100%;
  min-height: 160px;
  margin-top: 1rem;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}

.holiday-upload-actions {
  justify-content: flex-start;
  margin-top: 1rem;
}

.holiday-list-card {
  margin-top: 1rem;
  border: 1px solid var(--border-color, #d8dee8);
  border-radius: 1rem;
  padding: 1rem;
  background: var(--surface-soft, #f8fafc);
}

.holiday-list-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  margin-bottom: 0.75rem;
}

.holiday-list-header h3 {
  margin: 0;
}

@media (max-width: 900px) {
  .hero-polished {
    grid-template-columns: 1fr;
  }
}
'''

# De-duplicate repeated CSS comments by leaving harmless duplicates acceptable. Browser will use the last definitions.
app_file.write_text(app)
css_file.write_text(css)
PY

if [ -d "$DIST_DIR" ]; then
  rm -rf "$DIST_DIR"
fi

echo "==> Holiday admin display, year dropdown, default controls, policy reminder, and hero polish applied"
