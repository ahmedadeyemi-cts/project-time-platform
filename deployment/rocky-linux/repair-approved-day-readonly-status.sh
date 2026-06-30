#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
API_FILE="$REPO_DIR/src/backend/ProjectTime.Api/Program.cs"

if [ ! -f "$API_FILE" ]; then
  echo "ERROR: Missing $API_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

api = re.sub(r'version = "0\.[0-9]+\.[0-9]+"', 'version = "0.5.2"', api)

new_unlock_message = r'''static string GetDayUnlockMessage(string? status, DateTimeOffset? submittedAt)
{
    if (status is null || status == "draft") return "This day has not been submitted yet.";
    if (status == "manager_declined") return "This day was returned for correction and can be edited/resubmitted.";
    if (status == "submitted")
    {
        if (submittedAt is null) return "This submitted day is missing a submission timestamp. Please contact your manager to unlock it.";
        return DateTimeOffset.UtcNow - submittedAt.Value <= TimeSpan.FromHours(2)
            ? "This submitted day can be unlocked."
            : "This day was submitted more than two hours ago. Please contact your manager to unlock it.";
    }
    if (status == "manager_approved") return "This day has been manager-approved and is read-only for the engineer.";
    if (status == "pm_approved") return "This day has been PM-approved and is read-only for the engineer.";
    if (status == "accounting_ready") return "This day is ready for accounting review and is read-only for the engineer.";
    if (status == "reconciled") return "This day has been reconciled and is locked.";
    if (status == "locked") return "This day is locked.";

    return "This day is not editable in its current workflow state.";
}
'''

api, msg_count = re.subn(
    r'static string GetDayUnlockMessage\(string\? status, DateTimeOffset\? submittedAt\)\s*\{.*?\n\}',
    new_unlock_message,
    api,
    count=1,
    flags=re.S,
)

new_load_statuses = r'''static async Task<List<object>> LoadDayStatusesAsync(NpgsqlConnection connection, Guid? timesheetId, DateOnly weekStart)
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
                canEdit = status is "draft" or "manager_declined",
                canUnlock = CanEngineerUnlockDay(status, submittedAt),
                unlockMessage = GetDayUnlockMessage(status, submittedAt)
            };
        })
        .ToList();
}
'''

api, load_count = re.subn(
    r'static async Task<List<object>> LoadDayStatusesAsync\(NpgsqlConnection connection, Guid\? timesheetId, DateOnly weekStart\)\s*\{.*?\n\}',
    new_load_statuses,
    api,
    count=1,
    flags=re.S,
)

if load_count == 0:
    raise SystemExit('ERROR: Could not find LoadDayStatusesAsync to replace.')

api_file.write_text(api)
print(f'Replaced GetDayUnlockMessage: {msg_count}; replaced LoadDayStatusesAsync: {load_count}')
PY

echo "==> Approved-day read-only status repair applied"
echo "==> Expected API version after redeploy: 0.5.2"
