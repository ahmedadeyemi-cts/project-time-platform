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

for old_version in ['0.3.0', '0.3.1', '0.3.2', '0.3.3', '0.3.4', '0.3.5', '0.4.0']:
    api = api.replace(f'version = "{old_version}"', 'version = "0.4.0"')

# Repair any previously generated bad line from the first manager approval patch.
api = api.replace(
    'var comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();',
    'object comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();')
api = api.replace('manager@ussignal.local', 'ahmed.adeyemi@ussignal.com')
api = api.replace("'Development Manager'", "'Ahmed Adeyemi'", 1)

manager_endpoints = r'''
app.MapGet("/api/manager/approvals", async (DateOnly? weekStart, bool? includeAll) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(6);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var items = await LoadManagerApprovalItemsAsync(connection, start, end, includeAll ?? false);

    return Results.Ok(new
    {
        weekStart = start,
        weekEnd = end,
        includeAll = includeAll ?? false,
        count = items.Count,
        items
    });
});

app.MapPost("/api/manager/approvals/approve", async (ManagerApprovalActionRequest request) =>
{
    return await ProcessManagerApprovalActionAsync(request, "manager_approved", "approved", "timesheet_day_manager_approved", "Approved by manager.");
});

app.MapPost("/api/manager/approvals/decline", async (ManagerApprovalActionRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Comment))
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "A decline reason is required before returning time to the engineer."
        });
    }

    return await ProcessManagerApprovalActionAsync(request, "manager_declined", "declined", "timesheet_day_manager_declined", "Returned to engineer for correction.");
});

app.MapPost("/api/manager/approvals/unlock", async (ManagerApprovalActionRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var managerUserId = await GetOrCreateDevelopmentManagerUserIdAsync(connection, transaction);

        const string statusSql = """
            UPDATE timesheet_day_statuses
            SET status = 'draft',
                manager_user_id = @manager_user_id,
                manager_decision_comment = @comment,
                manager_unlocked_at = NOW(),
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
            RETURNING timesheet_day_status_id;
            """;

        await using var statusCommand = new NpgsqlCommand(statusSql, connection, transaction);
        statusCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        statusCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        statusCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
        statusCommand.Parameters.AddWithValue("comment", string.IsNullOrWhiteSpace(request.Comment) ? "Manager unlocked time for correction." : request.Comment.Trim());

        var statusId = (Guid?)(await statusCommand.ExecuteScalarAsync());
        if (statusId is null)
        {
            return Results.NotFound(new
            {
                status = "not_found",
                message = "No day-level timesheet status record was found for the selected item."
            });
        }

        await using var entryCommand = new NpgsqlCommand(
            "UPDATE time_entries SET status = 'draft', updated_at = NOW() WHERE timesheet_id = @timesheet_id AND work_date = @work_date;",
            connection,
            transaction);
        entryCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        entryCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        await entryCommand.ExecuteNonQueryAsync();

        await InsertAuditLogAsync(connection, transaction, managerUserId, "timesheet_day_manager_unlocked", "timesheet", request.TimesheetId);

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "manager_unlocked",
            timesheetId = request.TimesheetId,
            workDate = request.WorkDate,
            message = "Manager unlocked the selected day so the engineer can correct and resubmit it."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to manager-unlock timesheet day",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

'''

if 'app.MapGet("/api/manager/approvals"' not in api:
    api = api.replace('\napp.Run();', '\n' + manager_endpoints + 'app.Run();', 1)

manager_helpers = r'''
static async Task<Guid> GetOrCreateDevelopmentManagerUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
{
    const string sql = """
        INSERT INTO app_users (email, display_name, job_title, department, is_active)
        VALUES ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Development Manager', 'Project Pulse', TRUE)
        ON CONFLICT (email) DO UPDATE
        SET display_name = EXCLUDED.display_name,
            updated_at = NOW()
        RETURNING user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create development manager user."));
}

static async Task<List<object>> LoadManagerApprovalItemsAsync(NpgsqlConnection connection, DateOnly weekStart, DateOnly weekEnd, bool includeAll)
{
    var items = new List<object>();

    const string sql = """
        SELECT
            tds.timesheet_id,
            tds.user_id,
            u.display_name,
            u.email,
            tds.work_date,
            tds.status,
            tds.submitted_at,
            COALESCE(SUM(CASE WHEN te.time_type = 'normal' THEN te.hours ELSE 0 END), 0) AS normal_hours,
            COALESCE(SUM(CASE WHEN te.time_type = 'afterhours' THEN te.hours ELSE 0 END), 0) AS afterhours_hours,
            COALESCE(SUM(te.hours), 0) AS total_hours,
            COUNT(te.time_entry_id) AS entry_count,
            COALESCE(COUNT(te.time_entry_id) FILTER (WHERE te.description IS NOT NULL AND BTRIM(te.description) <> ''), 0) AS comment_count,
            COALESCE(STRING_AGG(DISTINCT COALESCE(npt.category_name, 'Project task'), ', '), 'No entries') AS activity_summary,
            tds.manager_decision_comment
        FROM timesheet_day_statuses tds
        INNER JOIN app_users u ON u.user_id = tds.user_id
        LEFT JOIN time_entries te
            ON te.timesheet_id = tds.timesheet_id
           AND te.work_date = tds.work_date
        LEFT JOIN non_project_time_categories npt
            ON npt.non_project_time_category_id = te.non_project_time_category_id
        WHERE tds.work_date BETWEEN @week_start AND @week_end
          AND (@include_all = TRUE OR tds.status = 'submitted')
        GROUP BY
            tds.timesheet_id,
            tds.user_id,
            u.display_name,
            u.email,
            tds.work_date,
            tds.status,
            tds.submitted_at,
            tds.manager_decision_comment
        ORDER BY tds.work_date, u.display_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("week_start", weekStart);
    command.Parameters.AddWithValue("week_end", weekEnd);
    command.Parameters.AddWithValue("include_all", includeAll);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        items.Add(new
        {
            timesheetId = reader.GetGuid(0),
            userId = reader.GetGuid(1),
            resourceName = reader.GetString(2),
            resourceEmail = reader.GetString(3),
            workDate = reader.GetFieldValue<DateOnly>(4),
            status = reader.GetString(5),
            submittedAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
            normalHours = reader.GetDecimal(7),
            afterhours = reader.GetDecimal(8),
            totalHours = reader.GetDecimal(9),
            entryCount = reader.GetInt64(10),
            commentCount = reader.GetInt64(11),
            activitySummary = reader.GetString(12),
            managerDecisionComment = reader.IsDBNull(13) ? null : reader.GetString(13)
        });
    }

    return items;
}

static async Task<IResult> ProcessManagerApprovalActionAsync(
    ManagerApprovalActionRequest request,
    string targetStatus,
    string approvalStatus,
    string auditAction,
    string successMessage)
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var managerUserId = await GetOrCreateDevelopmentManagerUserIdAsync(connection, transaction);
        object comment = string.IsNullOrWhiteSpace(request.Comment) ? DBNull.Value : request.Comment.Trim();

        var approvedAtSql = targetStatus == "manager_approved" ? "manager_approved_at = NOW(), manager_declined_at = NULL," : "manager_declined_at = NOW(), manager_approved_at = NULL,";

        var statusSql = $"""
            UPDATE timesheet_day_statuses
            SET status = @target_status,
                manager_user_id = @manager_user_id,
                manager_decision_comment = @comment,
                {approvedAtSql}
                updated_at = NOW()
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date
              AND status = 'submitted'
            RETURNING timesheet_day_status_id;
            """;

        await using var statusCommand = new NpgsqlCommand(statusSql, connection, transaction);
        statusCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        statusCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        statusCommand.Parameters.AddWithValue("target_status", targetStatus);
        statusCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
        statusCommand.Parameters.AddWithValue("comment", comment);

        var statusId = (Guid?)(await statusCommand.ExecuteScalarAsync());
        if (statusId is null)
        {
            return Results.Conflict(new
            {
                status = "not_pending_manager_approval",
                message = "Only submitted days that are pending manager approval can be approved or declined."
            });
        }

        await using var entryUpdateCommand = new NpgsqlCommand(
            "UPDATE time_entries SET status = @target_status, updated_at = NOW() WHERE timesheet_id = @timesheet_id AND work_date = @work_date;",
            connection,
            transaction);
        entryUpdateCommand.Parameters.AddWithValue("target_status", targetStatus);
        entryUpdateCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        entryUpdateCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        await entryUpdateCommand.ExecuteNonQueryAsync();

        await using var approvalCommand = new NpgsqlCommand("""
            INSERT INTO approval_records (time_entry_id, approval_stage, approval_status, approver_user_id, decision_comment)
            SELECT time_entry_id, 'manager', @approval_status, @manager_user_id, @comment
            FROM time_entries
            WHERE timesheet_id = @timesheet_id
              AND work_date = @work_date;
            """, connection, transaction);
        approvalCommand.Parameters.AddWithValue("approval_status", approvalStatus);
        approvalCommand.Parameters.AddWithValue("manager_user_id", managerUserId);
        approvalCommand.Parameters.AddWithValue("comment", comment);
        approvalCommand.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
        approvalCommand.Parameters.AddWithValue("work_date", request.WorkDate);
        await approvalCommand.ExecuteNonQueryAsync();

        await InsertAuditLogAsync(connection, transaction, managerUserId, auditAction, "timesheet", request.TimesheetId);

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = targetStatus,
            timesheetId = request.TimesheetId,
            workDate = request.WorkDate,
            message = successMessage
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to process manager approval action",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}

'''

if 'static async Task<Guid> GetOrCreateDevelopmentManagerUserIdAsync' not in api:
    api = api.replace('static async Task<Guid> GetOrCreateDevelopmentUserIdAsync', manager_helpers + 'static async Task<Guid> GetOrCreateDevelopmentUserIdAsync', 1)
else:
    # Make repeated runs repair the helper body created by older patches.
    api = api.replace("VALUES ('manager@ussignal.local', 'Development Manager', 'Development Manager', 'Project Pulse', TRUE)", "VALUES ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Development Manager', 'Project Pulse', TRUE)")
    api = api.replace("VALUES ('ahmed.adeyemi@ussignal.com', 'Development Manager', 'Development Manager', 'Project Pulse', TRUE)", "VALUES ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Development Manager', 'Project Pulse', TRUE)")

if 'internal sealed record ManagerApprovalActionRequest' not in api:
    api = api.replace('internal sealed record TimesheetSaveRequest', 'internal sealed record ManagerApprovalActionRequest(Guid TimesheetId, DateOnly WorkDate, string? Comment);\n\ninternal sealed record TimesheetSaveRequest', 1)

api_file.write_text(api)
PY

echo "==> Manager approval API patch applied"
echo "==> Expected API version after redeploy: 0.4.0"
echo "==> Manager development identity: ahmed.adeyemi@ussignal.com"
echo "==> Validate with: curl -s http://127.0.0.1:5080/api/version | jq"
