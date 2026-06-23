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
# Backend API patch
# -------------------------
api = api.replace('version = "0.3.0"', 'version = "0.3.2"')
api = api.replace('version = "0.3.1"', 'version = "0.3.2"')

# Ensure submitted timesheets are locked by default; edits require explicit unlock.
if 'static bool CanEditTimesheet(TimesheetEditState? editState, DateOnly weekStart)' in api:
    start = api.index('static bool CanEditTimesheet(TimesheetEditState? editState, DateOnly weekStart)')
    end = api.index('\nstatic async Task<TimesheetEditState?>', start)
    replacement = '''static bool CanEditTimesheet(TimesheetEditState? editState, DateOnly weekStart)
{
    if (editState is null) return true;
    return editState.Status is "draft" or "manager_declined";
}

static bool CanEngineerUnlockTimesheet(TimesheetEditState? editState)
{
    return editState?.Status == "submitted"
        && editState.SubmittedAt is not null
        && DateTimeOffset.UtcNow - editState.SubmittedAt.Value <= TimeSpan.FromHours(2);
}

static string GetEngineerUnlockMessage(TimesheetEditState? editState)
{
    if (editState is null) return "No submitted timesheet was found to unlock.";
    if (editState.Status != "submitted") return "Only submitted timesheets can be unlocked.";
    if (editState.SubmittedAt is null) return "The submitted time is missing a submission timestamp. Please contact your manager to unlock it.";

    return DateTimeOffset.UtcNow - editState.SubmittedAt.Value <= TimeSpan.FromHours(2)
        ? "This submitted timesheet can be unlocked by the engineer."
        : "This timesheet was submitted more than two hours ago. Please contact your manager to unlock it.";
}

'''
    api = api[:start] + replacement + api[end+1:]
elif 'static async Task<TimesheetEditState?> GetTimesheetEditStateAsync' in api:
    marker = 'static async Task<TimesheetEditState?> GetTimesheetEditStateAsync'
    helper = '''static bool CanEditTimesheet(TimesheetEditState? editState, DateOnly weekStart)
{
    if (editState is null) return true;
    return editState.Status is "draft" or "manager_declined";
}

static bool CanEngineerUnlockTimesheet(TimesheetEditState? editState)
{
    return editState?.Status == "submitted"
        && editState.SubmittedAt is not null
        && DateTimeOffset.UtcNow - editState.SubmittedAt.Value <= TimeSpan.FromHours(2);
}

static string GetEngineerUnlockMessage(TimesheetEditState? editState)
{
    if (editState is null) return "No submitted timesheet was found to unlock.";
    if (editState.Status != "submitted") return "Only submitted timesheets can be unlocked.";
    if (editState.SubmittedAt is null) return "The submitted time is missing a submission timestamp. Please contact your manager to unlock it.";

    return DateTimeOffset.UtcNow - editState.SubmittedAt.Value <= TimeSpan.FromHours(2)
        ? "This submitted timesheet can be unlocked by the engineer."
        : "This timesheet was submitted more than two hours ago. Please contact your manager to unlock it.";
}

'''
    api = api.replace(marker, helper + marker, 1)
else:
    marker = 'static async Task<string?> GetTimesheetStatusAsync'
    helper = '''static bool CanEditTimesheet(TimesheetEditState? editState, DateOnly weekStart)
{
    if (editState is null) return true;
    return editState.Status is "draft" or "manager_declined";
}

static bool CanEngineerUnlockTimesheet(TimesheetEditState? editState)
{
    return editState?.Status == "submitted"
        && editState.SubmittedAt is not null
        && DateTimeOffset.UtcNow - editState.SubmittedAt.Value <= TimeSpan.FromHours(2);
}

static string GetEngineerUnlockMessage(TimesheetEditState? editState)
{
    if (editState is null) return "No submitted timesheet was found to unlock.";
    if (editState.Status != "submitted") return "Only submitted timesheets can be unlocked.";
    if (editState.SubmittedAt is null) return "The submitted time is missing a submission timestamp. Please contact your manager to unlock it.";

    return DateTimeOffset.UtcNow - editState.SubmittedAt.Value <= TimeSpan.FromHours(2)
        ? "This submitted timesheet can be unlocked by the engineer."
        : "This timesheet was submitted more than two hours ago. Please contact your manager to unlock it.";
}

static async Task<TimesheetEditState?> GetTimesheetEditStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        SELECT status, submitted_at
        FROM timesheets
        WHERE user_id = @user_id
          AND week_start_date = @week_start_date;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new TimesheetEditState(
        reader.GetString(0),
        reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
}

'''
    api = api.replace(marker, helper + marker, 1)

# Ensure save and submit endpoints use edit-state policy.
api = api.replace('''        var existingStatus = await GetTimesheetStatusAsync(connection, transaction, userId, start);

        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be edited."
            });
        }
''', '''        var editState = await GetTimesheetEditStateAsync(connection, transaction, userId, start);

        if (!CanEditTimesheet(editState, start))
        {
            return Results.Conflict(new
            {
                status = "timesheet_locked",
                currentStatus = editState?.Status,
                message = GetEngineerUnlockMessage(editState)
            });
        }
''')

api = api.replace('''        var existingStatus = await GetTimesheetStatusAsync(connection, transaction, userId, start);

        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_submittable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be submitted."
            });
        }
''', '''        var editState = await GetTimesheetEditStateAsync(connection, transaction, userId, start);

        if (!CanEditTimesheet(editState, start))
        {
            return Results.Conflict(new
            {
                status = "timesheet_locked",
                currentStatus = editState?.Status,
                message = GetEngineerUnlockMessage(editState)
            });
        }
''')

api = api.replace('''        canEdit = CanEditTimesheet(timesheet is null ? null : new TimesheetEditState(timesheet.Status, timesheet.SubmittedAt), start),
        editPolicy = "Editable while draft, manager-declined, still in the current week, or within one hour of submission.",
        weekStart = start,
''', '''        canEdit = CanEditTimesheet(timesheet is null ? null : new TimesheetEditState(timesheet.Status, timesheet.SubmittedAt), start),
        canUnlock = CanEngineerUnlockTimesheet(timesheet is null ? null : new TimesheetEditState(timesheet.Status, timesheet.SubmittedAt)),
        unlockMessage = GetEngineerUnlockMessage(timesheet is null ? null : new TimesheetEditState(timesheet.Status, timesheet.SubmittedAt)),
        editPolicy = "Submitted timesheets are locked. Engineers can unlock their own submitted timesheet within two hours of submission. After two hours, they must contact their manager.",
        weekStart = start,
''')

api = api.replace('''        submittedAt = timesheet?.SubmittedAt,
        weekStart = start,
''', '''        submittedAt = timesheet?.SubmittedAt,
        canEdit = CanEditTimesheet(timesheet is null ? null : new TimesheetEditState(timesheet.Status, timesheet.SubmittedAt), start),
        canUnlock = CanEngineerUnlockTimesheet(timesheet is null ? null : new TimesheetEditState(timesheet.Status, timesheet.SubmittedAt)),
        unlockMessage = GetEngineerUnlockMessage(timesheet is null ? null : new TimesheetEditState(timesheet.Status, timesheet.SubmittedAt)),
        editPolicy = "Submitted timesheets are locked. Engineers can unlock their own submitted timesheet within two hours of submission. After two hours, they must contact their manager.",
        weekStart = start,
''')

unlock_endpoint = '''
app.MapPost("/api/timesheets/week/unlock", async (TimesheetUnlockRequest request) =>
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
        var start = GetSundayForDate(request.WeekStart);
        var editState = await GetTimesheetEditStateAsync(connection, transaction, userId, start);

        if (!CanEngineerUnlockTimesheet(editState))
        {
            return Results.Conflict(new
            {
                status = "unlock_denied",
                currentStatus = editState?.Status,
                message = GetEngineerUnlockMessage(editState)
            });
        }

        var timesheetId = await UnlockSubmittedTimesheetAsync(connection, transaction, userId, start);
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_engineer_unlocked", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, start);

        return Results.Ok(new
        {
            status = "unlocked_for_engineer_edit",
            timesheetId,
            message = "Timesheet unlocked. Make your correction, then submit again.",
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to unlock timesheet",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});
'''
if '/api/timesheets/week/unlock' not in api:
    api = api.replace('\napp.Run();', unlock_endpoint + '\napp.Run();', 1)

unlock_helper = '''static async Task<Guid> UnlockSubmittedTimesheetAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        UPDATE timesheets
        SET status = 'draft',
            submitted_at = NULL,
            updated_at = NOW()
        WHERE user_id = @user_id
          AND week_start_date = @week_start_date
          AND status = 'submitted'
        RETURNING timesheet_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);

    var timesheetId = (Guid?)(await command.ExecuteScalarAsync());
    if (timesheetId is null)
    {
        throw new InvalidOperationException("No submitted timesheet was found to unlock.");
    }

    await using var entryCommand = new NpgsqlCommand(
        "UPDATE time_entries SET status = 'draft', updated_at = NOW() WHERE timesheet_id = @timesheet_id;",
        connection,
        transaction);
    entryCommand.Parameters.AddWithValue("timesheet_id", timesheetId.Value);
    await entryCommand.ExecuteNonQueryAsync();

    return timesheetId.Value;
}

'''
if 'static async Task<Guid> UnlockSubmittedTimesheetAsync' not in api:
    api = api.replace('static async Task MarkTimesheetSubmittedAsync', unlock_helper + 'static async Task MarkTimesheetSubmittedAsync', 1)

if 'internal sealed record TimesheetUnlockRequest' not in api:
    api = api.replace('internal sealed record TimesheetSaveRequest', 'internal sealed record TimesheetUnlockRequest(DateOnly WeekStart);\n\ninternal sealed record TimesheetSaveRequest', 1)

if 'internal sealed record TimesheetEditState' not in api:
    api = api.replace('internal sealed record TimesheetHeader(Guid? TimesheetId, string Status, DateTimeOffset? SubmittedAt);', 'internal sealed record TimesheetHeader(Guid? TimesheetId, string Status, DateTimeOffset? SubmittedAt);\n\ninternal sealed record TimesheetEditState(string Status, DateTimeOffset? SubmittedAt);', 1)

api_file.write_text(api)

# -------------------------
# Frontend patch
# -------------------------
if 'async function unlockTimesheet()' not in app:
    unlock_function = '''
  async function unlockTimesheet() {
    if (isSaving) return;

    setIsSaving(true);
    setSaveStatus('Requesting unlock...');

    try {
      const result = await postJson('/api/timesheets/week/unlock', { weekStart: selectedWeekStart });
      setTimesheet({ loading: false, data: result.timesheet, error: null });
      setSubmissionStatus('Draft');
      setSaveStatus(result.message ?? 'Timesheet unlocked');
    } catch (error) {
      setSaveStatus(error instanceof Error ? error.message : 'Please contact your manager to unlock this timesheet.');
    } finally {
      setIsSaving(false);
    }
  }
'''
    app = app.replace('\n  function resetTimesheet() {', unlock_function + '\n  function resetTimesheet() {', 1)

if "const canRequestUnlock" not in app:
    app = app.replace(
"""  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isTimesheetEditable = timesheet.data?.canEdit ?? ['draft', 'manager_declined'].includes(currentTimesheetStatus);
""",
"""  const currentTimesheetStatus = timesheet.data?.status ?? 'draft';
  const isTimesheetEditable = timesheet.data?.canEdit ?? ['draft', 'manager_declined'].includes(currentTimesheetStatus);
  const canRequestUnlock = currentTimesheetStatus === 'submitted';
""")

unlock_button = '''            {canRequestUnlock ? (
              <button type="button" className="unlock-action" onClick={unlockTimesheet} disabled={isSaving}>Unlock</button>
            ) : null}
'''
if 'className="unlock-action"' not in app:
    app = app.replace(
"""            <button type="button" onClick={saveDraft} disabled={!isTimesheetEditable || isSaving}>Save draft</button>
            <button type="button" className="primary-action" onClick={handleSubmit} disabled={!isTimesheetEditable || isSaving}>Submit</button>
""",
"""            <button type="button" onClick={saveDraft} disabled={!isTimesheetEditable || isSaving}>Save draft</button>
""" + unlock_button + """            <button type="button" className="primary-action" onClick={handleSubmit} disabled={!isTimesheetEditable || isSaving}>Submit</button>
""", 1)

if "timesheet.data?.unlockMessage" not in app:
    app = app.replace(
"""          <span>Total: <strong>{formatNumber(grandTotal)}</strong></span>
        </div>
""",
"""          <span>Total: <strong>{formatNumber(grandTotal)}</strong></span>
          {currentTimesheetStatus === 'submitted' && timesheet.data?.unlockMessage ? (
            <span className="unlock-message">{timesheet.data.unlockMessage}</span>
          ) : null}
        </div>
""", 1)

app_file.write_text(app)

# -------------------------
# CSS patch
# -------------------------
if '.toolbar-actions .unlock-action' not in css:
    css = css.replace(
""".toolbar-actions .primary-action {
  border-color: color-mix(in srgb, var(--brand-blue) 38%, var(--border));
  background: var(--brand-blue);
  color: #ffffff;
}
""",
""".toolbar-actions .primary-action {
  border-color: color-mix(in srgb, var(--brand-blue) 38%, var(--border));
  background: var(--brand-blue);
  color: #ffffff;
}

.toolbar-actions .unlock-action {
  border-color: color-mix(in srgb, #d9902f 55%, var(--border));
  background: color-mix(in srgb, #fff4df 85%, var(--surface));
  color: #8a5300;
}

:root[data-theme='dark'] .toolbar-actions .unlock-action {
  background: rgba(217, 144, 47, 0.16);
  color: #ffd89a;
}
""", 1)

if '.unlock-message' not in css:
    css = css.replace(
""".timesheet-status-bar strong {
  color: var(--text);
}
""",
""".timesheet-status-bar strong {
  color: var(--text);
}

.unlock-message {
  flex-basis: 100%;
  color: var(--muted);
  font-size: 0.84rem;
}
""", 1)

css_file.write_text(css)
PY

echo "==> Submitted timesheet unlock policy patch applied"
echo "==> Validate backend version with: curl -s http://127.0.0.1:5080/api/version | jq"
echo "==> Expected version after API redeploy: 0.3.2"
