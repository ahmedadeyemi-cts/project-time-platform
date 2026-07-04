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

api_file = Path('/opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api/Program.cs')
api = api_file.read_text()

api = api.replace('version = "0.4.0"', 'version = "0.4.1"')
api = api.replace('version = "0.4.1"', 'version = "0.4.1"')
api = api.replace(
    'var comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();',
    'object comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')
api = api.replace("VALUES ('ahmed.adeyemi@ussignal.com', 'Development Manager', 'Development Manager', 'Project Health Dashboard', TRUE)", "VALUES ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Development Manager', 'Project Health Dashboard', TRUE)")

summary_endpoint = r'''
app.MapGet("/api/manager/approval-summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var summary = await LoadManagerApprovalSummaryAsync(connection);
    return Results.Ok(summary);
});

'''

bulk_endpoint = r'''
app.MapPost("/api/manager/approvals/bulk-approve", async (ManagerBulkApprovalRequest request) =>
{
    return await ProcessManagerBulkApprovalAsync(request);
});

'''

if 'app.MapGet("/api/manager/approval-summary"' not in api:
    api = api.replace('\napp.Run();', '\n' + summary_endpoint + 'app.Run();', 1)

if 'app.MapPost("/api/manager/approvals/bulk-approve"' not in api:
    api = api.replace('\napp.Run();', '\n' + bulk_endpoint + 'app.Run();', 1)

helpers = r'''
static async Task<object> LoadManagerApprovalSummaryAsync(NpgsqlConnection connection)
{
    const string sql = """
        SELECT COUNT(*)
        FROM timesheet_day_statuses
        WHERE status = 'submitted';
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    var pendingManagerApprovals = Convert.ToInt64(await command.ExecuteScalarAsync() ?? 0);
    const long pendingProjectApprovals = 0;

    return new
    {
        pendingManagerApprovals,
        pendingProjectApprovals,
        totalPending = pendingManagerApprovals + pendingProjectApprovals,
        refreshedAtUtc = DateTimeOffset.UtcNow
    };
}

static async Task<IResult> ProcessManagerBulkApprovalAsync(ManagerBulkApprovalRequest request)
{
    if (request.Items is null || request.Items.Count == 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "Select at least one submitted day before using bulk approval."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var managerUserId = await GetOrCreateDevelopmentManagerUserIdAsync(connection, transaction);
        object comment = string.IsNullOrWhiteSpace(request.Comment) ? "Bulk approved by manager." : request.Comment.Trim();
        var approvedCount = 0;

        foreach (var item in request.Items.DistinctBy(item => new { item.TimesheetId, item.WorkDate }))
        {
            await using var statusCommand = new NpgsqlCommand("""
                UPDATE timesheet_day_statuses
                SET status = 'manager_approved',
                    manager_user_id = @manager_user_id,
                    manager_decision_comment = @comment,
                    manager_approved_at = NOW(),
                    manager_declined_at = NULL,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = 'submitted'
                RETURNING timesheet_day_status_id;
                """, connection, transaction);
            statusCommand.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            statusCommand.Parameters.AddWithValue("work_date", item.WorkDate);
            statusCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
            statusCommand.Parameters.AddWithValue("comment", comment);

            var statusId = (Guid?)(await statusCommand.ExecuteScalarAsync());
            if (statusId is null) continue;

            await using var entryUpdateCommand = new NpgsqlCommand(
                "UPDATE time_entries SET status = 'manager_approved', updated_at = NOW() WHERE timesheet_id = @timesheet_id AND work_date = @work_date;",
                connection,
                transaction);
            entryUpdateCommand.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            entryUpdateCommand.Parameters.AddWithValue("work_date", item.WorkDate);
            await entryUpdateCommand.ExecuteNonQueryAsync();

            await using var approvalCommand = new NpgsqlCommand("""
                INSERT INTO approval_records (time_entry_id, approval_stage, approval_status, approver_user_id, decision_comment)
                SELECT time_entry_id, 'manager', 'approved', @manager_user_id, @comment
                FROM time_entries
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date;
                """, connection, transaction);
            approvalCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
            approvalCommand.Parameters.AddWithValue("comment", comment);
            approvalCommand.Parameters.AddWithValue("timesheet_id", item.TimesheetId);
            approvalCommand.Parameters.AddWithValue("work_date", item.WorkDate);
            await approvalCommand.ExecuteNonQueryAsync();

            await InsertAuditLogAsync(connection, transaction, managerUserId, "timesheet_day_manager_bulk_approved", "timesheet", item.TimesheetId);
            approvedCount++;
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "bulk_manager_approved",
            approvedCount,
            requestedCount = request.Items.Count,
            message = $"Bulk approved {approvedCount} submitted day(s)."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to bulk approve manager approval items",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}

'''

if 'static async Task<object> LoadManagerApprovalSummaryAsync' not in api:
    if 'static async Task<Guid> GetOrCreateDevelopmentManagerUserIdAsync' in api:
        api = api.replace('static async Task<Guid> GetOrCreateDevelopmentManagerUserIdAsync', helpers + 'static async Task<Guid> GetOrCreateDevelopmentManagerUserIdAsync', 1)
    else:
        api = api.replace('static async Task<Guid> GetOrCreateDevelopmentUserIdAsync', helpers + 'static async Task<Guid> GetOrCreateDevelopmentUserIdAsync', 1)

if 'internal sealed record ManagerBulkApprovalRequest' not in api:
    if 'internal sealed record ManagerApprovalActionRequest' in api:
        api = api.replace('internal sealed record ManagerApprovalActionRequest', 'internal sealed record ManagerBulkApprovalRequest(List<ManagerApprovalActionRequest> Items, string? Comment);\n\ninternal sealed record ManagerApprovalActionRequest', 1)
    else:
        api = api.replace('internal sealed record TimesheetSaveRequest', 'internal sealed record ManagerApprovalActionRequest(Guid TimesheetId, DateOnly WorkDate, string? Comment);\n\ninternal sealed record ManagerBulkApprovalRequest(List<ManagerApprovalActionRequest> Items, string? Comment);\n\ninternal sealed record TimesheetSaveRequest', 1)

api_file.write_text(api)
PY

echo "==> Approval inbox summary and bulk API patch applied"
echo "==> Expected API version after redeploy: 0.4.1"
