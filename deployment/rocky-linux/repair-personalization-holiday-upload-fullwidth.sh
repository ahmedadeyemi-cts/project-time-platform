#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
CSS_FILE="$REPO_DIR/src/frontend/project-time-web/src/timesheet.css"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

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

repo = Path('/opt/project-time-platform/app/project-time-platform')
api_file = repo / 'src/backend/ProjectTime.Api/Program.cs'
app_file = repo / 'src/frontend/project-time-web/src/App.jsx'
css_file = repo / 'src/frontend/project-time-web/src/timesheet.css'
api = api_file.read_text()
app = app_file.read_text()
css = css_file.read_text()

# -----------------------------
# Backend: holiday CSV import endpoint
# -----------------------------
api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.5"', api)
api = api.replace('const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";', 'const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')

holiday_endpoint = r'''
app.MapPost("/api/holidays/import-text", async (HolidayCsvImportRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.CsvText))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "CSV text is required." });
    }

    var lines = request.CsvText
        .Replace("\r\n", "\n")
        .Replace("\r", "\n")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    if (lines.Count < 2)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "CSV must include a header and at least one holiday row." });
    }

    var header = ParseSimpleCsvLine(lines[0]).Select(item => item.Trim()).ToList();
    var dateIndex = header.FindIndex(item => item.Equals("holiday_date", StringComparison.OrdinalIgnoreCase));
    var nameIndex = header.FindIndex(item => item.Equals("holiday_name", StringComparison.OrdinalIgnoreCase));
    var typeIndex = header.FindIndex(item => item.Equals("holiday_type", StringComparison.OrdinalIgnoreCase));
    var floatingIndex = header.FindIndex(item => item.Equals("is_floating_holiday", StringComparison.OrdinalIgnoreCase));
    var hoursIndex = header.FindIndex(item => item.Equals("auto_populate_hours", StringComparison.OrdinalIgnoreCase));

    if (dateIndex < 0 || nameIndex < 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "CSV must include holiday_date and holiday_name columns." });
    }

    var rows = new List<HolidayImportRow>();
    for (var index = 1; index < lines.Count; index++)
    {
        var columns = ParseSimpleCsvLine(lines[index]);
        if (columns.Count <= Math.Max(dateIndex, nameIndex)) continue;

        var dateValue = columns[dateIndex].Trim();
        var nameValue = columns[nameIndex].Trim();
        if (string.IsNullOrWhiteSpace(dateValue) || string.IsNullOrWhiteSpace(nameValue)) continue;
        if (!DateOnly.TryParse(dateValue, out var holidayDate))
        {
            return Results.BadRequest(new { status = "validation_failed", message = $"Invalid holiday_date on row {index + 1}: {dateValue}" });
        }

        if (request.Year is not null && holidayDate.Year != request.Year.Value) continue;

        var holidayType = typeIndex >= 0 && columns.Count > typeIndex && !string.IsNullOrWhiteSpace(columns[typeIndex])
            ? columns[typeIndex].Trim()
            : "company_paid";
        var isFloating = floatingIndex >= 0 && columns.Count > floatingIndex && IsTruthy(columns[floatingIndex]);
        var hours = 8.00m;
        if (hoursIndex >= 0 && columns.Count > hoursIndex && decimal.TryParse(columns[hoursIndex], out var parsedHours)) hours = parsedHours;

        rows.Add(new HolidayImportRow(holidayDate, nameValue, holidayType, isFloating, hours));
    }

    if (rows.Count == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "No holiday rows were imported. Check the year and CSV values." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var userId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        Guid batchId;

        await using (var batchCommand = new NpgsqlCommand("""
            INSERT INTO holiday_upload_batches (upload_year, original_filename, uploaded_by_user_id, row_count, notes)
            VALUES (@upload_year, @original_filename, @uploaded_by_user_id, @row_count, @notes)
            ON CONFLICT (upload_year, original_filename) DO UPDATE
            SET uploaded_at = NOW(),
                uploaded_by_user_id = EXCLUDED.uploaded_by_user_id,
                row_count = EXCLUDED.row_count,
                notes = EXCLUDED.notes
            RETURNING holiday_upload_batch_id;
            """, connection, transaction))
        {
            batchCommand.Parameters.AddWithValue("upload_year", request.Year ?? rows[0].HolidayDate.Year);
            batchCommand.Parameters.AddWithValue("original_filename", string.IsNullOrWhiteSpace(request.Filename) ? $"holiday-upload-{DateTime.UtcNow:yyyyMMddHHmmss}.csv" : request.Filename.Trim());
            batchCommand.Parameters.AddWithValue("uploaded_by_user_id", userId);
            batchCommand.Parameters.AddWithValue("row_count", rows.Count);
            batchCommand.Parameters.AddWithValue("notes", "Uploaded through Project Health Dashboard holiday admin UI");
            batchId = (Guid)(await batchCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create holiday upload batch."));
        }

        foreach (var row in rows)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO company_holidays (holiday_date, holiday_name, holiday_code, holiday_type, is_floating_holiday, auto_populate_hours, is_active, source_batch_id)
                VALUES (@holiday_date, @holiday_name, 'HOLIDAY', @holiday_type, @is_floating_holiday, @auto_populate_hours, TRUE, @source_batch_id)
                ON CONFLICT (holiday_date) DO UPDATE
                SET holiday_name = EXCLUDED.holiday_name,
                    holiday_type = EXCLUDED.holiday_type,
                    is_floating_holiday = EXCLUDED.is_floating_holiday,
                    auto_populate_hours = EXCLUDED.auto_populate_hours,
                    is_active = TRUE,
                    source_batch_id = EXCLUDED.source_batch_id,
                    updated_at = NOW();
                """, connection, transaction);
            command.Parameters.AddWithValue("holiday_date", row.HolidayDate);
            command.Parameters.AddWithValue("holiday_name", row.HolidayName);
            command.Parameters.AddWithValue("holiday_type", row.HolidayType);
            command.Parameters.AddWithValue("is_floating_holiday", row.IsFloatingHoliday);
            command.Parameters.AddWithValue("auto_populate_hours", row.AutoPopulateHours);
            command.Parameters.AddWithValue("source_batch_id", batchId);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return Results.Ok(new { status = "holidays_imported", importedCount = rows.Count, year = request.Year ?? rows[0].HolidayDate.Year });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(title: "Failed to import holidays", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});

'''

if 'app.MapPost("/api/holidays/import-text"' not in api:
    api = api.replace('\napp.Run();', '\n' + holiday_endpoint + 'app.Run();', 1)

helpers = r'''
static List<string> ParseSimpleCsvLine(string line)
{
    var values = new List<string>();
    var current = new System.Text.StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
        var ch = line[i];
        if (ch == '"')
        {
            if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else
            {
                inQuotes = !inQuotes;
            }
        }
        else if (ch == ',' && !inQuotes)
        {
            values.Add(current.ToString());
            current.Clear();
        }
        else
        {
            current.Append(ch);
        }
    }

    values.Add(current.ToString());
    return values;
}

static bool IsTruthy(string? value)
{
    return value is not null && new[] { "true", "1", "yes", "y" }.Contains(value.Trim().ToLowerInvariant());
}

'''

if 'static List<string> ParseSimpleCsvLine' not in api:
    api = api.replace('static IResult? ValidateConfig(DatabaseConfig config)', helpers + 'static IResult? ValidateConfig(DatabaseConfig config)', 1)

records = r'''internal sealed record HolidayCsvImportRequest(int? Year, string? Filename, string CsvText);
internal sealed record HolidayImportRow(DateOnly HolidayDate, string HolidayName, string HolidayType, bool IsFloatingHoliday, decimal AutoPopulateHours);

'''
if 'internal sealed record HolidayCsvImportRequest' not in api:
    api = api.replace('internal sealed record TimesheetSaveRequest', records + 'internal sealed record TimesheetSaveRequest', 1)

api_file.write_text(api)

# -----------------------------
# Frontend: make policy reminders visible, add default action buttons,
# holiday upload UI, and use full-width ChangePoint-style layout.
# -----------------------------
if 'function getVacationHolidayReminder' not in app:
    app = app.replace('function statusToLabel(status, totalHours = 0) {', r'''function getVacationHolidayReminder(row) {
  if (!row) return null;
  const code = (row.categoryCode ?? '').toUpperCase();
  const activity = (row.activity ?? '').toUpperCase();
  if (!['VACATION', 'HOLIDAY'].includes(code) && !['VACATION', 'HOLIDAY'].includes(activity)) return null;
  return 'The code "Vacation" should be used for PTO. "Holiday" should be used only for company-paid holidays and your floating holiday. If you are taking PTO and a time entry deadline is approaching, your time should be submitted before you take your time off. All resources are required to submit 40 hours of time each week.';
}

function statusToLabel(status, totalHours = 0) {''', 1)

# Add upload/prefs state if missing.
state_anchor = "  const [activitySource, setActivitySource] = useState('nonProject');"
if "const [holidayUploadText, setHolidayUploadText]" not in app and state_anchor in app:
    app = app.replace(state_anchor, state_anchor + "\n  const [holidayUploadText, setHolidayUploadText] = useState('');\n  const [holidayUploadStatus, setHolidayUploadStatus] = useState('No holiday upload yet');\n  const [holidayUploadYear, setHolidayUploadYear] = useState('2026');", 1)

# Add save default and upload functions before addCategory.
if 'async function setRowAsPersonalDefault' not in app and '  function addCategory(category) {' in app:
    app = app.replace('  function addCategory(category) {', r'''  async function savePersonalDefaults(defaultCodes, defaultTaskIds) {
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

  async function importHolidayCsv() {
    setHolidayUploadStatus('Importing holidays...');
    try {
      const result = await postJson('/api/holidays/import-text', {
        year: Number.parseInt(holidayUploadYear, 10),
        filename: `holidays-${holidayUploadYear}.csv`,
        csvText: holidayUploadText
      });
      setHolidayUploadStatus(`Imported ${result.importedCount} holidays for ${result.year}`);
      const refreshed = await fetchJson(`/api/holidays?year=${holidayUploadYear}`);
      setCompanyHolidays({ loading: false, data: refreshed, error: null });
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

  function addCategory(category) {''', 1)

# Unhide rows when manually adding.
app = app.replace("""    const row = categoryToRow(category);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));""", """    const row = categoryToRow(category);
    if (typeof unhideRowForCurrentWeek === 'function') unhideRowForCurrentWeek(row.id);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));""")
app = app.replace("""    const row = taskToRow(task);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));""", """    const row = taskToRow(task);
    if (typeof unhideRowForCurrentWeek === 'function') unhideRowForCurrentWeek(row.id);
    setActiveRows((current) => (current.some((item) => item.id === row.id) ? current : [...current, row]));""")

# Add modal warning before the detail form.
if 'policy-reminder' not in app and '<div className="detail-form modal-detail-form">' in app:
    app = app.replace('<div className="detail-form modal-detail-form">', """{getVacationHolidayReminder(selectedRow) ? (
                <div className="policy-reminder">{getVacationHolidayReminder(selectedRow)}</div>
              ) : null}

              <div className="detail-form modal-detail-form">""", 1)

# Replace row action remove-only cell with default toggle + remove.
old_action = '<button className="link-button" type="button" onClick={() => removeRow(row.id)} disabled={!isAnyDayEditable}>Remove</button>'
new_action = '''<div className="row-action-stack">
                        <button className="link-button" type="button" onClick={() => isRowPersonalDefault(row) ? void removeRowAsPersonalDefault(row) : void setRowAsPersonalDefault(row)}>
                          {isRowPersonalDefault(row) ? 'Remove default' : 'Set default'}
                        </button>
                        <button className="link-button" type="button" onClick={() => removeRow(row.id)} disabled={!isAnyDayEditable}>Remove</button>
                      </div>'''
if old_action in app and 'Remove default' not in app:
    app = app.replace(old_action, new_action, 1)

# Add default button to activity cards by replacing approval small. This works for non-project cards.
small_line = "<small>{category.requiresApproval ? 'Approval required' : 'No approval required'}</small>"
if small_line in app and 'Set as my default' not in app:
    app = app.replace(small_line, small_line + "\n                         <span className=\"default-toggle\" onClick={(event) => { event.stopPropagation(); void setRowAsPersonalDefault(categoryToRow(category)); }}>Set as my default</span>", 1)

# Add holiday admin panel before PSA modules or utilization.
holiday_panel = r'''
      <section id="holiday-admin" className="panel holiday-admin-panel">
        <div className="section-header compact">
          <div>
            <p className="eyebrow">Holiday administration</p>
            <h2>Upload yearly holidays</h2>
            <p className="muted">Upload a CSV with holiday_date, holiday_name, holiday_type, is_floating_holiday, and auto_populate_hours. Fixed company holidays auto-populate 8.00 Holiday hours when the week includes the holiday.</p>
          </div>
          <span className="pill">{companyHolidays.data?.count ?? 0} holidays loaded</span>
        </div>

        <div className="holiday-upload-grid">
          <label>
            Year
            <input value={holidayUploadYear} onChange={(event) => setHolidayUploadYear(event.target.value)} />
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

        <div className="holiday-list">
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
    anchor = '      <section id="psa-modules"'
    if anchor in app:
        app = app.replace(anchor, holiday_panel + '\n' + anchor, 1)
    else:
        app = app.replace('      <section id="utilization"', holiday_panel + '\n      <section id="utilization"', 1)

# Full-width layout CSS overrides and UI styles.
if '/* Project Health Dashboard wide ChangePoint-style layout */' not in css:
    css += r'''

/* Project Health Dashboard wide ChangePoint-style layout */
.app-shell {
  max-width: none !important;
  width: 100% !important;
  padding-left: clamp(0.75rem, 1.5vw, 1.5rem) !important;
  padding-right: clamp(0.75rem, 1.5vw, 1.5rem) !important;
}

.top-bar,
.hero,
.status-grid,
.panel {
  max-width: none !important;
  width: 100% !important;
}

.timesheet-page {
  max-width: none !important;
  width: 100% !important;
}

.entry-grid-wrap {
  width: 100% !important;
  overflow-x: auto !important;
}

.entry-grid {
  min-width: 1450px;
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

.row-action-stack {
  display: grid;
  gap: 0.25rem;
  justify-items: start;
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

.holiday-list {
  margin-top: 1rem;
}
'''

api_file.write_text(api)
app_file.write_text(app)
css_file.write_text(css)
PY

if [ -d "$DIST_DIR" ]; then
  rm -rf "$DIST_DIR"
fi

echo "==> Personalization, holiday upload, policy reminder, and full-width layout repair applied"
echo "==> Expected API version after redeploy: 0.5.5"
