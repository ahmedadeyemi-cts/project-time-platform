#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
CSS_FILE="$REPO_DIR/src/frontend/project-time-web/src/timesheet.css"

for f in "$API_FILE" "$APP_FILE" "$CSS_FILE"; do
  if [ ! -f "$f" ]; then
    echo "ERROR: Missing $f"
    exit 1
  fi
done

python3 - <<'PY'
from pathlib import Path

repo = Path('/opt/project-time-platform/app/project-time-platform')
api_file = repo / 'src/backend/ProjectTime.Api/Program.cs'
app_file = repo / 'src/frontend/project-time-web/src/App.jsx'
css_file = repo / 'src/frontend/project-time-web/src/timesheet.css'

api = api_file.read_text()
app = app_file.read_text()
css = css_file.read_text()

# -------------------------
# Backend daily submission patch
# -------------------------
for old_version in ['0.3.0', '0.3.1', '0.3.2', '0.3.3']:
    api = api.replace(f'version = "{old_version}"', 'version = "0.3.4"')

if 'static bool CanEngineerUnlockDay(' not in api:
    helper_marker = 'static IResult? ValidateConfig(DatabaseConfig config)'
    daily_helpers = r'''
static bool CanEngineerUnlockDay(string? status, DateTimeOffset? submittedAt)
{
    return status == "submitted"
        && submittedAt is not null
        && DateTimeOffset.UtcNow - submittedAt.Value <= TimeSpan.FromHours(2);
}

static string GetDayUnlockMessage(string? status, DateTimeOffset? submittedAt)
{
    if (status is null || status == "draft") return "This day has not been submitted yet.";
    if (status != "submitted") return "Only submitted days can be unlocked by the engineer.";
    if (submittedAt is null) return "This submitted day is missing a submission timestamp. Please contact your manager to unlock it.";

    return DateTimeOffset.UtcNow - submittedAt.Value <= TimeSpan.FromHours(2)
        ? "This submitted day can be unlocked."
        : "This day was submitted more than two hours ago. Please contact your manager to unlock it.";
}

static IReadOnlyList<string> ValidateDaySubmitRequest(TimesheetDaySubmitRequest request)
{
    var errors = new List<string>();
    var weekStart = GetSundayForDate(request.WeekStart);
    var weekEnd = weekStart.AddDays(6);

    if (request.WorkDate < weekStart || request.WorkDate > weekEnd)
    {
        errors.Add($"Work date {request.WorkDate} is outside the selected week {weekStart} through {weekEnd}.");
    }

    if (request.Entries is null || request.Entries.Count == 0)
    {
        errors.Add("At least one time entry is required for the selected day.");
        return errors;
    }

    var dailyTotal = request.Entries
        .Where(entry => entry.WorkDate == request.WorkDate)
        .Sum(entry => entry.Hours);

    if (dailyTotal < 8.00m)
    {
        errors.Add($"A minimum of 8.00 hours is required before submitting {request.WorkDate}. Current total is {dailyTotal:0.00} hours.");
    }

    foreach (var entry in request.Entries)
    {
        if (entry.WorkDate != request.WorkDate)
        {
            errors.Add($"Entry date {entry.WorkDate} does not match selected submit date {request.WorkDate}.");
        }

        if (entry.TimeType is not ("normal" or "afterhours"))
        {
            errors.Add($"Invalid time type '{entry.TimeType}'. Expected normal or afterhours.");
        }

        if (entry.Hours < 0 || entry.Hours > 24)
        {
            errors.Add($"Hours for {entry.WorkDate} must be between 0 and 24.");
        }

        if (entry.Hours > 0 && string.IsNullOrWhiteSpace(entry.CategoryCode) && (entry.ProjectId is null || entry.TaskId is null))
        {
            errors.Add($"Entry for {entry.WorkDate} must identify either a non-project category or a project task.");
        }
    }

    return errors;
}

'''
    api = api.replace(helper_marker, daily_helpers + helper_marker, 1)

if 'app.MapPost("/api/timesheets/day/submit"' not in api:
    daily_endpoints = r'''
app.MapPost("/api/timesheets/day/submit", async (TimesheetDaySubmitRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var validationErrors = ValidateDaySubmitRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            errors = validationErrors
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var userId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        var weekStart = GetSundayForDate(request.WeekStart);
        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, weekStart);
        var dayState = await GetTimesheetDayStatusAsync(connection, transaction, timesheetId, request.WorkDate);

        if (dayState.Status == "submitted")
        {
            return Results.Conflict(new
            {
                status = "day_already_submitted",
                currentStatus = dayState.Status,
                message = "This day is already submitted. Use Unlock within two hours, or contact your manager after two hours."
            });
        }

        await ReplaceDayTimeEntriesAsync(connection, transaction, timesheetId, userId, request.WorkDate, request.Entries, "submitted");
        await MarkTimesheetDaySubmittedAsync(connection, transaction, timesheetId, userId, request.WorkDate);
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_day_submitted", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, weekStart);

        return Results.Ok(new
        {
            status = "day_submitted",
            timesheetId,
            workDate = request.WorkDate,
            message = $"{request.WorkDate} submitted successfully.",
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to submit timesheet day",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/timesheets/day/unlock", async (TimesheetDayUnlockRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var userId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        var weekStart = GetSundayForDate(request.WeekStart);
        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, weekStart);
        var dayState = await GetTimesheetDayStatusAsync(connection, transaction, timesheetId, request.WorkDate);

        if (!CanEngineerUnlockDay(dayState.Status, dayState.SubmittedAt))
        {
            return Results.Conflict(new
            {
                status = "day_unlock_denied",
                currentStatus = dayState.Status,
                message = GetDayUnlockMessage(dayState.Status, dayState.SubmittedAt)
            });
        }

        await UnlockTimesheetDayAsync(connection, transaction, timesheetId, userId, request.WorkDate);
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_day_engineer_unlocked", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, weekStart);

        return Results.Ok(new
        {
            status = "day_unlocked",
            timesheetId,
            workDate = request.WorkDate,
            message = "Day unlocked. Make your correction, then submit the day again.",
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to unlock timesheet day",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});
'''
    api = api.replace('\napp.Run();', daily_endpoints + '\napp.Run();', 1)

if 'static async Task ReplaceDayTimeEntriesAsync' not in api:
    helper_anchor = 'static async Task ReplaceTimeEntriesAsync'
    day_persistence_helpers = r'''
static async Task<DayStatusRecord> GetTimesheetDayStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid timesheetId, DateOnly workDate)
{
    const string sql = """
        SELECT status, submitted_at
        FROM timesheet_day_statuses
        WHERE timesheet_id = @timesheet_id
          AND work_date = @work_date;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    command.Parameters.AddWithValue("work_date", workDate);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return new DayStatusRecord("draft", null);
    }

    return new DayStatusRecord(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
}

static async Task ReplaceDayTimeEntriesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    DateOnly workDate,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    await using (var deleteCommand = new NpgsqlCommand("DELETE FROM time_entries WHERE timesheet_id = @timesheet_id AND work_date = @work_date;", connection, transaction))
    {
        deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        deleteCommand.Parameters.AddWithValue("work_date", workDate);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    foreach (var entry in entries.Where(item => item.WorkDate == workDate && item.Hours > 0))
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

static async Task MarkTimesheetDaySubmittedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid timesheetId, Guid userId, DateOnly workDate)
{
    const string sql = """
        INSERT INTO timesheet_day_statuses (timesheet_id, user_id, work_date, status, submitted_at)
        VALUES (@timesheet_id, @user_id, @work_date, 'submitted', NOW())
        ON CONFLICT (timesheet_id, work_date) DO UPDATE
        SET status = 'submitted',
            submitted_at = NOW(),
            updated_at = NOW();
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("work_date", workDate);
    await command.ExecuteNonQueryAsync();
}

static async Task UnlockTimesheetDayAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid timesheetId, Guid userId, DateOnly workDate)
{
    const string sql = """
        UPDATE timesheet_day_statuses
        SET status = 'draft',
            unlocked_at = NOW(),
            unlocked_by_user_id = @user_id,
            updated_at = NOW()
        WHERE timesheet_id = @timesheet_id
          AND work_date = @work_date
          AND status = 'submitted';
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("work_date", workDate);
    await command.ExecuteNonQueryAsync();

    await using var entryCommand = new NpgsqlCommand(
        "UPDATE time_entries SET status = 'draft', updated_at = NOW() WHERE timesheet_id = @timesheet_id AND work_date = @work_date;",
        connection,
        transaction);
    entryCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
    entryCommand.Parameters.AddWithValue("work_date", workDate);
    await entryCommand.ExecuteNonQueryAsync();
}

'''
    api = api.replace(helper_anchor, day_persistence_helpers + helper_anchor, 1)

if 'static async Task<List<object>> LoadDayStatusesAsync' not in api:
    load_status_anchor = 'static async Task<List<object>> LoadSavedTimeEntriesAsync'
    load_status_helper = r'''
static async Task<List<object>> LoadDayStatusesAsync(NpgsqlConnection connection, Guid? timesheetId, DateOnly weekStart)
{
    var statusByDate = new Dictionary<DateOnly, DayStatusRecord>();

    if (timesheetId is not null)
    {
        const string sql = """
            SELECT work_date, status, submitted_at
            FROM timesheet_day_statuses
            WHERE timesheet_id = @timesheet_id
            ORDER BY work_date;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("timesheet_id", timesheetId.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statusByDate[reader.GetFieldValue<DateOnly>(0)] = new DayStatusRecord(
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
        }
    }

    return Enumerable.Range(0, 7)
        .Select(offset => weekStart.AddDays(offset))
        .Select(date =>
        {
            statusByDate.TryGetValue(date, out var record);
            var status = record?.Status ?? "draft";
            var submittedAt = record?.SubmittedAt;

            return (object)new
            {
                workDate = date,
                status,
                submittedAt,
                canEdit = status != "submitted",
                canUnlock = CanEngineerUnlockDay(status, submittedAt),
                unlockMessage = GetDayUnlockMessage(status, submittedAt)
            };
        })
        .ToList();
}

'''
    api = api.replace(load_status_anchor, load_status_helper + load_status_anchor, 1)

api = api.replace(
'''    var entries = timesheet?.TimesheetId is null
        ? new List<object>()
        : await LoadSavedTimeEntriesAsync(connection, timesheet.TimesheetId.Value);

    return new
    {
        timesheetId = timesheet?.TimesheetId,
        status = timesheet?.Status ?? "draft",
        submittedAt = timesheet?.SubmittedAt,
''',
'''    var entries = timesheet?.TimesheetId is null
        ? new List<object>()
        : await LoadSavedTimeEntriesAsync(connection, timesheet.TimesheetId.Value);
    var dayStatuses = await LoadDayStatusesAsync(connection, timesheet?.TimesheetId, start);

    return new
    {
        timesheetId = timesheet?.TimesheetId,
        status = timesheet?.Status ?? "draft",
        submittedAt = timesheet?.SubmittedAt,
        dayStatuses,
''')

# Prevent draft saves from deleting already-submitted day rows.
api = api.replace(
'''    await using (var deleteCommand = new NpgsqlCommand("DELETE FROM time_entries WHERE timesheet_id = @timesheet_id;", connection, transaction))
    {
        deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await deleteCommand.ExecuteNonQueryAsync();
    }
''',
'''    await using (var deleteCommand = new NpgsqlCommand("""
        DELETE FROM time_entries
        WHERE timesheet_id = @timesheet_id
          AND work_date NOT IN (
              SELECT work_date
              FROM timesheet_day_statuses
              WHERE timesheet_id = @timesheet_id
                AND status = 'submitted'
          );
        """, connection, transaction))
    {
        deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await deleteCommand.ExecuteNonQueryAsync();
    }
''')

if 'internal sealed record TimesheetDaySubmitRequest' not in api:
    api = api.replace('internal sealed record TimesheetSaveRequest', 'internal sealed record TimesheetDaySubmitRequest(DateOnly WeekStart, DateOnly WorkDate, List<TimesheetEntryRequest> Entries);\n\ninternal sealed record TimesheetDayUnlockRequest(DateOnly WeekStart, DateOnly WorkDate);\n\ninternal sealed record TimesheetSaveRequest', 1)

if 'internal sealed record DayStatusRecord' not in api:
    api = api.replace('internal sealed record TimesheetHeader(Guid? TimesheetId, string Status, DateTimeOffset? SubmittedAt);', 'internal sealed record TimesheetHeader(Guid? TimesheetId, string Status, DateTimeOffset? SubmittedAt);\n\ninternal sealed record DayStatusRecord(string Status, DateTimeOffset? SubmittedAt);', 1)

api_file.write_text(api)

# -------------------------
# Frontend daily submission patch
# -------------------------
if 'function getDayStatus(workDate)' not in app:
    day_functions = r'''
  function getDayStatus(workDate) {
    return timesheet.data?.dayStatuses?.find((dayStatus) => dayStatus.workDate === workDate) ?? {
      workDate,
      status: 'draft',
      canEdit: true,
      canUnlock: false,
      unlockMessage: 'This day has not been submitted yet.'
    };
  }

  function isDayEditable(workDate) {
    return getDayStatus(workDate).canEdit !== false;
  }

  function getEntriesForDay(workDate) {
    return buildTimesheetPayload().entries.filter((entry) => entry.workDate === workDate);
  }

  function getSelectedDayTotal() {
    if (!selectedCell) return 0;
    return getDayTotal(selectedCell.date);
  }

  async function submitSelectedDay() {
    if (!selectedCell || isSaving) return;

    const dayTotal = getDayTotal(selectedCell.date);
    if (dayTotal < 8) {
      setSaveStatus(`A minimum of 8.00 hours is required before submitting ${selectedCell.date}. Current total is ${formatNumber(dayTotal)} hours.`);
      return;
    }

    setIsSaving(true);
    setSaveStatus(`Submitting ${selectedCell.date}...`);

    try {
      const result = await postJson('/api/timesheets/day/submit', {
        weekStart: selectedWeekStart,
        workDate: selectedCell.date,
        entries: getEntriesForDay(selectedCell.date)
      });
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus(`${selectedCell.date} submitted (${formatNumber(dayTotal)} hours).`);
      setSaveStatus(result.message ?? 'Day submitted');
      setSelectedCell(null);
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Failed to submit selected day');
    } finally {
      setIsSaving(false);
    }
  }

  async function unlockSelectedDay() {
    if (!selectedCell || isSaving) return;

    setIsSaving(true);
    setSaveStatus(`Requesting unlock for ${selectedCell.date}...`);

    try {
      const result = await postJson('/api/timesheets/day/unlock', {
        weekStart: selectedWeekStart,
        workDate: selectedCell.date
      });
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus('Draft');
      setSaveStatus(result.message ?? 'Day unlocked');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Please contact your manager to unlock this submitted day.');
    } finally {
      setIsSaving(false);
    }
  }

'''
    app = app.replace('\n  function openEntryDetails(rowId, date, type) {', day_functions + '\n  function openEntryDetails(rowId, date, type) {', 1)

app = app.replace('''  function updateEntry(rowId, date, type, patch) {
    if (!isTimesheetEditable) return;
''', '''  function updateEntry(rowId, date, type, patch) {
    if (!isTimesheetEditable || !isDayEditable(date)) return;
''')

app = app.replace('''  function openEntryDetails(rowId, date, type) {
    if (!isTimesheetEditable) return;
    setSelectedCell({ rowId, date, type });
  }
''', '''  function openEntryDetails(rowId, date, type) {
    setSelectedCell({ rowId, date, type });
  }
''')

app = app.replace('''                              disabled={!isTimesheetEditable}
                            >
                              {formatHoursValue(entry.hours)}
''', '''                              disabled={!isTimesheetEditable && isDayEditable(day.date)}
                            >
                              {formatHoursValue(entry.hours)}
''')

if 'selectedDayStatus' not in app:
    app = app.replace('''  const selectedRow = activeRows.find((row) => row.id === selectedCell?.rowId);
  const selectedEntry = selectedCell ? getEntry(selectedCell.rowId, selectedCell.date, selectedCell.type) : null;
''', '''  const selectedRow = activeRows.find((row) => row.id === selectedCell?.rowId);
  const selectedEntry = selectedCell ? getEntry(selectedCell.rowId, selectedCell.date, selectedCell.type) : null;
  const selectedDayStatus = selectedCell ? getDayStatus(selectedCell.date) : null;
''')

app = app.replace('''                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { hours: event.target.value })}
                />
''', '''                  disabled={!isDayEditable(selectedCell.date)}
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { hours: event.target.value })}
                />
''')

app = app.replace('''                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { comment: event.target.value })}
                />
''', '''                  disabled={!isDayEditable(selectedCell.date)}
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { comment: event.target.value })}
                />
''')

app = app.replace('''                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationGroupId: event.target.value })}
                >
''', '''                  disabled={!isDayEditable(selectedCell.date)}
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationGroupId: event.target.value })}
                >
''')

app = app.replace('''                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationId: event.target.value })}
                >
''', '''                  disabled={!isDayEditable(selectedCell.date)}
                  onChange={(event) => updateEntry(selectedCell.rowId, selectedCell.date, selectedCell.type, { workLocationId: event.target.value })}
                >
''')

modal_actions = r'''
              <div className="day-submit-actions">
                <span>
                  Day total: <strong>{formatNumber(getSelectedDayTotal())}</strong> / minimum 8.00 hours
                </span>
                {selectedDayStatus?.status === 'submitted' ? (
                  <button type="button" className="unlock-action" onClick={unlockSelectedDay} disabled={isSaving}>
                    Unlock this day
                  </button>
                ) : (
                  <button type="button" className="primary-action" onClick={submitSelectedDay} disabled={isSaving || getSelectedDayTotal() < 8}>
                    Submit this day
                  </button>
                )}
                {selectedDayStatus?.status === 'submitted' ? (
                  <small>{selectedDayStatus.unlockMessage}</small>
                ) : (
                  <small>Each submitted day must have at least 8.00 hours.</small>
                )}
              </div>
'''
if 'day-submit-actions' not in app:
    app = app.replace('''            </div>
          </section>
        </div>
''', '''            </div>
''' + modal_actions + '''          </section>
        </div>
''', 1)

app_file.write_text(app)

# -------------------------
# CSS daily submission patch
# -------------------------
if '.day-submit-actions' not in css:
    css += r'''

.day-submit-actions {
  display: grid;
  gap: 10px;
  margin-top: 18px;
  border-top: 1px solid var(--border);
  padding-top: 16px;
}

.day-submit-actions span,
.day-submit-actions small {
  color: var(--muted);
}

.day-submit-actions strong {
  color: var(--text);
}

.day-submit-actions button {
  border: 1px solid var(--border);
  border-radius: 999px;
  padding: 11px 14px;
  cursor: pointer;
  font-weight: 900;
}

.day-submit-actions .primary-action {
  border-color: color-mix(in srgb, var(--brand-blue) 38%, var(--border));
  background: var(--brand-blue);
  color: #ffffff;
}

.day-submit-actions .unlock-action {
  border-color: color-mix(in srgb, #d9902f 55%, var(--border));
  background: color-mix(in srgb, #fff4df 85%, var(--surface));
  color: #8a5300;
}

:root[data-theme='dark'] .day-submit-actions .unlock-action {
  background: rgba(217, 144, 47, 0.16);
  color: #ffd89a;
}
'''
css_file.write_text(css)
PY

echo "==> Daily submission policy patch applied"
echo "==> Expected API version after redeploy: 0.3.4"
