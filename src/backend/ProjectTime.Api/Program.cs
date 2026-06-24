using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;

const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.com";
const string DevelopmentUserDisplayName = "Ahmed Adeyemi";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase) ||
        IsProjectPulsePublicApiPath(context))
    {
        await next();
        return;
    }

    var validation = await ValidateProjectPulseSessionAsync(context);

    if (!validation.IsValid)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            status = "session_required",
            message = validation.Message ?? "Your Project Pulse session is missing or expired. Please sign in again."
        });
        return;
    }

    context.Items["ProjectPulseSessionUserId"] = validation.UserId;
    context.Items["ProjectPulseSessionEmail"] = validation.Email;
    context.Items["ProjectPulseSessionProvider"] = validation.ProviderCode;
    context.Items["ProjectPulseSessionExpiresAt"] = validation.ExpiresAt;

    await next();
});



app.MapGet("/", () => Results.Redirect("/health"));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Project Time Platform API",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/version", () => Results.Ok(new
{
    application = "Project Time Platform",
    component = "ProjectTime.Api",
    version = "0.7.0",
    framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/db-config-check", () =>
{
    var config = DatabaseConfig.FromEnvironment();
    return Results.Ok(new
    {
        configured = config.Missing.Count == 0,
        missing = config.Missing,
        database = config.Database,
        user = config.Username,
        host = config.Host,
        port = config.Port,
        passwordConfigured = !string.IsNullOrWhiteSpace(config.Password)
    });
});

app.MapGet("/api/db-health", async () =>
{
    var config = DatabaseConfig.FromEnvironment();

    if (config.Missing.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "configuration_missing",
            missing = config.Missing
        });
    }

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT current_database(), current_user, now();", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return Results.Ok(new
        {
            status = "database_connected",
            database = reader.GetString(0),
            user = reader.GetString(1),
            timestamp = reader.GetDateTime(2)
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Database connection failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/schema/tables", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var tables = new List<string>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT table_name
        FROM information_schema.tables
        WHERE table_schema = 'public'
        ORDER BY table_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        tables.Add(reader.GetString(0));
    }

    return Results.Ok(new
    {
        count = tables.Count,
        tables
    });
});

app.MapGet("/api/non-project-time-categories", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var categories = await LoadNonProjectCategoriesAsync(connection);

    return Results.Ok(new
    {
        count = categories.Count,
        categories
    });
});

app.MapGet("/api/work-location-groups", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var groups = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT work_location_group_id, group_code, group_name, group_description, is_active, display_order
        FROM work_location_groups
        WHERE is_active = TRUE
        ORDER BY display_order, group_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        groups.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            description = reader.IsDBNull(3) ? null : reader.GetString(3),
            isActive = reader.GetBoolean(4),
            displayOrder = reader.GetInt32(5)
        });
    }

    return Results.Ok(new
    {
        count = groups.Count,
        groups
    });
});

app.MapGet("/api/work-locations", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var locations = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            wl.work_location_id,
            wl.location_code,
            wl.location_name,
            wl.city,
            wl.state_region,
            wl.country,
            wl.time_zone,
            wlg.work_location_group_id,
            wlg.group_code,
            wlg.group_name,
            wl.display_order
        FROM work_locations wl
        LEFT JOIN work_location_groups wlg ON wlg.work_location_group_id = wl.work_location_group_id
        WHERE wl.is_active = TRUE
        ORDER BY wl.display_order, wl.location_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        locations.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            city = reader.IsDBNull(3) ? null : reader.GetString(3),
            stateRegion = reader.IsDBNull(4) ? null : reader.GetString(4),
            country = reader.GetString(5),
            timeZone = reader.IsDBNull(6) ? null : reader.GetString(6),
            groupId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
            groupCode = reader.IsDBNull(8) ? null : reader.GetString(8),
            groupName = reader.IsDBNull(9) ? null : reader.GetString(9),
            displayOrder = reader.GetInt32(10)
        });
    }

    return Results.Ok(new
    {
        count = locations.Count,
        locations
    });
});

app.MapGet("/api/utilization/policies", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var policies = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            utilization_policy_id,
            policy_name,
            period_type,
            standard_period_hours,
            default_target_percent,
            presales_training_requires_approval,
            is_active
        FROM utilization_policies
        ORDER BY is_active DESC, policy_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        policies.Add(new
        {
            id = reader.GetGuid(0),
            name = reader.GetString(1),
            periodType = reader.GetString(2),
            standardPeriodHours = reader.GetDecimal(3),
            defaultTargetPercent = reader.GetDecimal(4),
            presalesTrainingRequiresApproval = reader.GetBoolean(5),
            isActive = reader.GetBoolean(6)
        });
    }

    return Results.Ok(new
    {
        count = policies.Count,
        policies
    });
});

app.MapGet("/api/utilization/targets", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var targets = new List<object>();

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            up.policy_name,
            upt.target_percent,
            upt.target_hours,
            upt.display_order
        FROM utilization_policy_targets upt
        INNER JOIN utilization_policies up ON up.utilization_policy_id = upt.utilization_policy_id
        WHERE up.is_active = TRUE
        ORDER BY upt.display_order, upt.target_percent;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        targets.Add(new
        {
            policyName = reader.GetString(0),
            targetPercent = reader.GetDecimal(1),
            targetHours = reader.GetDecimal(2),
            displayOrder = reader.GetInt32(3)
        });
    }

    return Results.Ok(new
    {
        count = targets.Count,
        targets
    });
});

app.MapGet("/api/timesheets/week", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var payload = await BuildTimesheetWeekPayloadAsync(connection, userId, start);

    return Results.Ok(payload);
});

app.MapPost("/api/timesheets/week/draft", async (TimesheetSaveRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var validationErrors = ValidateTimesheetRequest(request);
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
        var start = GetSundayForDate(request.WeekStart);
        var existingStatus = await GetTimesheetStatusAsync(connection, transaction, userId, start);

        if (existingStatus is "reconciled" or "locked")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Locked or reconciled timesheets cannot be edited."
            });
        }

        var timesheetId = await UpsertDraftShellForEditableSaveAsync(connection, transaction, userId, start);
        await ReplaceEditableTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "draft");
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_draft_saved", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, start);

        return Results.Ok(new
        {
            status = "draft_saved",
            timesheetId,
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to save draft timesheet",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/timesheets/week/submit", async (TimesheetSaveRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var validationErrors = ValidateTimesheetRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            errors = validationErrors
        });
    }

    var positiveEntryCount = request.Entries.Count(entry => entry.Hours > 0);
    if (positiveEntryCount == 0)
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            errors = new[] { "At least one time entry with hours greater than zero is required before submission." }
        });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var userId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        var start = GetSundayForDate(request.WeekStart);
        var existingStatus = await GetTimesheetStatusAsync(connection, transaction, userId, start);

        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_submittable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be submitted."
            });
        }

        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, start);
        await ReplaceTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "submitted");
        await MarkTimesheetSubmittedAsync(connection, transaction, timesheetId);
        await InsertAuditLogAsync(connection, transaction, userId, "timesheet_submitted", "timesheet", timesheetId);

        await transaction.CommitAsync();

        await using var readConnection = new NpgsqlConnection(config.ConnectionString);
        await readConnection.OpenAsync();
        var payload = await BuildTimesheetWeekPayloadAsync(readConnection, userId, start);

        return Results.Ok(new
        {
            status = "submitted_for_manager_approval",
            timesheetId,
            submittedEntryCount = positiveEntryCount,
            timesheet = payload
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to submit timesheet",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

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


app.MapPost("/api/manager/approvals/bulk-approve", async (ManagerBulkApprovalRequest request) =>
{
    return await ProcessManagerBulkApprovalAsync(request);
});


app.MapGet("/api/assignments/open-tasks", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(6);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var tasks = await LoadOpenAssignedProjectTasksAsync(connection, userId, start, end);

    return Results.Ok(new
    {
        weekStart = start,
        weekEnd = end,
        count = tasks.Count,
        tasks
    });
});


app.MapGet("/api/debug/time-entries", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(6);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var rows = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            t.week_start_date,
            t.status AS timesheet_status,
            te.work_date,
            te.time_type,
            te.hours,
            te.status AS entry_status,
            COALESCE(npt.category_name, pt.task_name, 'Unknown') AS activity,
            p.project_code,
            p.project_name,
            te.description
        FROM timesheets t
        LEFT JOIN time_entries te ON te.timesheet_id = t.timesheet_id
        LEFT JOIN non_project_time_categories npt ON npt.non_project_time_category_id = te.non_project_time_category_id
        LEFT JOIN project_tasks pt ON pt.task_id = te.task_id
        LEFT JOIN projects p ON p.project_id = te.project_id
        WHERE t.user_id = @user_id
          AND t.week_start_date = @week_start
        ORDER BY te.work_date, te.time_type, activity;
        """, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start", start);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            weekStart = reader.GetFieldValue<DateOnly>(0),
            timesheetStatus = reader.GetString(1),
            workDate = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
            timeType = reader.IsDBNull(3) ? null : reader.GetString(3),
            hours = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4),
            entryStatus = reader.IsDBNull(5) ? null : reader.GetString(5),
            activity = reader.IsDBNull(6) ? null : reader.GetString(6),
            projectCode = reader.IsDBNull(7) ? null : reader.GetString(7),
            projectName = reader.IsDBNull(8) ? null : reader.GetString(8),
            description = reader.IsDBNull(9) ? null : reader.GetString(9)
        });
    }

    return Results.Ok(new { weekStart = start, weekEnd = end, count = rows.Count, rows });
});


app.MapGet("/api/project-intake/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var requests = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT request_number, client_name, request_title, intake_status, priority, target_start_date, target_completion_date, estimated_hours
        FROM project_intake_requests
        ORDER BY created_at DESC;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            requests.Add(new
            {
                requestNumber = reader.GetString(0),
                clientName = reader.GetString(1),
                title = reader.GetString(2),
                status = reader.GetString(3),
                priority = reader.GetString(4),
                targetStartDate = reader.IsDBNull(5) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(5),
                targetCompletionDate = reader.IsDBNull(6) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(6),
                estimatedHours = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7)
            });
        }
    }

    var templates = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT template_code, template_name, service_line, default_phase_count, default_task_count
        FROM project_templates
        WHERE is_active = TRUE
        ORDER BY template_name;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            templates.Add(new
            {
                templateCode = reader.GetString(0),
                templateName = reader.GetString(1),
                serviceLine = reader.IsDBNull(2) ? null : reader.GetString(2),
                defaultPhaseCount = reader.GetInt32(3),
                defaultTaskCount = reader.GetInt32(4)
            });
        }
    }

    return Results.Ok(new { count = requests.Count, requests, templates });
});

app.MapGet("/api/project-management/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var milestones = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT p.project_code, pm.milestone_name, pm.milestone_status, pm.due_date, pm.display_order
        FROM project_milestones pm
        INNER JOIN projects p ON p.project_id = pm.project_id
        ORDER BY p.project_code, pm.display_order, pm.due_date;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            milestones.Add(new
            {
                projectCode = reader.GetString(0),
                name = reader.GetString(1),
                status = reader.GetString(2),
                dueDate = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
                displayOrder = reader.GetInt32(4)
            });
        }
    }

    var risks = new List<object>();
    await using (var command = new NpgsqlCommand("""
        SELECT p.project_code, pr.risk_title, pr.probability, pr.impact, pr.risk_status, pr.mitigation_plan
        FROM project_risks pr
        INNER JOIN projects p ON p.project_id = pr.project_id
        ORDER BY p.project_code, pr.created_at DESC;
        """, connection))
    await using (var reader = await command.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            risks.Add(new
            {
                projectCode = reader.GetString(0),
                title = reader.GetString(1),
                probability = reader.GetString(2),
                impact = reader.GetString(3),
                status = reader.GetString(4),
                mitigationPlan = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
    }

    return Results.Ok(new { milestoneCount = milestones.Count, riskCount = risks.Count, milestones, risks });
});

app.MapGet("/api/resource-scheduling/capacity", async (DateOnly? weekStart) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var start = weekStart ?? GetSundayForDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var end = start.AddDays(28);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var rows = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT u.display_name, u.email, rcp.week_start_date, rcp.available_hours, rcp.assigned_hours, rcp.planned_utilization_percent, rcp.capacity_status
        FROM resource_capacity_plans rcp
        INNER JOIN app_users u ON u.user_id = rcp.user_id
        WHERE rcp.week_start_date BETWEEN @start AND @end
        ORDER BY rcp.week_start_date, u.display_name;
        """, connection);
    command.Parameters.AddWithValue("start", start);
    command.Parameters.AddWithValue("end", end);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            resourceName = reader.GetString(0),
            resourceEmail = reader.GetString(1),
            weekStart = reader.GetFieldValue<DateOnly>(2),
            availableHours = reader.GetDecimal(3),
            assignedHours = reader.GetDecimal(4),
            plannedUtilizationPercent = reader.GetDecimal(5),
            status = reader.GetString(6)
        });
    }

    return Results.Ok(new { weekStart = start, weekEnd = end, count = rows.Count, capacity = rows });
});

app.MapGet("/api/expenses/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var reports = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT er.report_number, er.report_title, er.report_status, er.report_total, u.display_name, p.project_code
        FROM expense_reports er
        INNER JOIN app_users u ON u.user_id = er.user_id
        LEFT JOIN projects p ON p.project_id = er.project_id
        ORDER BY er.created_at DESC;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        reports.Add(new
        {
            reportNumber = reader.GetString(0),
            title = reader.GetString(1),
            status = reader.GetString(2),
            total = reader.GetDecimal(3),
            resourceName = reader.GetString(4),
            projectCode = reader.IsDBNull(5) ? null : reader.GetString(5)
        });
    }

    return Results.Ok(new { count = reports.Count, reports });
});

app.MapGet("/api/invoicing/summary", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var invoices = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT ci.invoice_number, ci.invoice_status, ci.billing_period_start, ci.billing_period_end, ci.labor_amount, ci.expense_amount, ci.invoice_total, p.project_code, p.project_name
        FROM client_invoices ci
        LEFT JOIN projects p ON p.project_id = ci.project_id
        ORDER BY ci.generated_at DESC;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        invoices.Add(new
        {
            invoiceNumber = reader.GetString(0),
            status = reader.GetString(1),
            billingPeriodStart = reader.IsDBNull(2) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(2),
            billingPeriodEnd = reader.IsDBNull(3) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(3),
            laborAmount = reader.GetDecimal(4),
            expenseAmount = reader.GetDecimal(5),
            invoiceTotal = reader.GetDecimal(6),
            projectCode = reader.IsDBNull(7) ? null : reader.GetString(7),
            projectName = reader.IsDBNull(8) ? null : reader.GetString(8)
        });
    }

    return Results.Ok(new { count = invoices.Count, invoices });
});

app.MapGet("/api/reporting/executive-dashboard", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var metrics = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT snapshot_date, metric_name, metric_value, metric_unit, metric_context::text
        FROM reporting_snapshots
        WHERE snapshot_type = 'executive_dashboard'
        ORDER BY metric_name;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        metrics.Add(new
        {
            snapshotDate = reader.GetFieldValue<DateOnly>(0),
            metricName = reader.GetString(1),
            metricValue = reader.GetDecimal(2),
            metricUnit = reader.IsDBNull(3) ? null : reader.GetString(3),
            context = reader.IsDBNull(4) ? null : reader.GetString(4)
        });
    }

    return Results.Ok(new { count = metrics.Count, metrics });
});


app.MapGet("/api/users/timesheet-preferences", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    var preferences = await LoadTimesheetPreferencesAsync(connection, userId);

    return Results.Ok(preferences);
});

app.MapPost("/api/users/timesheet-preferences", async (TimesheetPreferenceRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);

    const string sql = """
        INSERT INTO user_timesheet_preferences (
            user_id,
            default_non_project_category_codes,
            default_project_task_ids,
            auto_add_holidays,
            weekly_reminder_enabled,
            updated_at
        )
        VALUES (
            @user_id,
            @default_codes,
            @default_task_ids,
            @auto_add_holidays,
            @weekly_reminder_enabled,
            NOW()
        )
        ON CONFLICT (user_id) DO UPDATE
        SET default_non_project_category_codes = EXCLUDED.default_non_project_category_codes,
            default_project_task_ids = EXCLUDED.default_project_task_ids,
            auto_add_holidays = EXCLUDED.auto_add_holidays,
            weekly_reminder_enabled = EXCLUDED.weekly_reminder_enabled,
            updated_at = NOW();
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("default_codes", request.DefaultNonProjectCategoryCodes?.ToArray() ?? Array.Empty<string>());
    command.Parameters.AddWithValue("default_task_ids", request.DefaultProjectTaskIds?.ToArray() ?? Array.Empty<Guid>());
    command.Parameters.AddWithValue("auto_add_holidays", request.AutoAddHolidays);
    command.Parameters.AddWithValue("weekly_reminder_enabled", request.WeeklyReminderEnabled);
    await command.ExecuteNonQueryAsync();

    var preferences = await LoadTimesheetPreferencesAsync(connection, userId);
    return Results.Ok(new { status = "preferences_saved", preferences });
});

app.MapGet("/api/holidays", async (int? year) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var targetYear = year ?? DateTime.UtcNow.Year;

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        var holidays = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT holiday_date, holiday_name, holiday_code, holiday_type, is_floating_holiday, auto_populate_hours
            FROM company_holidays
            WHERE is_active = TRUE
              AND EXTRACT(YEAR FROM holiday_date) = @year
            ORDER BY holiday_date;
            """, connection);
        command.Parameters.AddWithValue("year", targetYear);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            holidays.Add(new
            {
                holidayDate = reader.GetFieldValue<DateOnly>(0),
                holidayName = reader.GetString(1),
                holidayCode = reader.GetString(2),
                holidayType = reader.GetString(3),
                isFloatingHoliday = reader.GetBoolean(4),
                autoPopulateHours = reader.GetDecimal(5)
            });
        }

        return Results.Ok(new { year = targetYear, count = holidays.Count, holidays });
    }
    catch (PostgresException ex) when (ex.SqlState == "42P01" || ex.SqlState == "42703")
    {
        return Results.Ok(new { year = targetYear, count = 0, holidays = Array.Empty<object>(), warning = "Holiday foundation tables are not ready yet." });
    }
});

app.MapPost("/api/reminders/queue-weekly-engineer", async () =>
{
    return await QueueReminderRuleAsync("WEEKLY_ENGINEER_TIME_REMINDER");
});

app.MapPost("/api/reminders/queue-month-end-pm", async () =>
{
    return await QueueReminderRuleAsync("MONTH_END_PM_REMINDER");
});

app.MapGet("/api/reminders/outbox", async (int? limit) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var maxRows = Math.Clamp(limit ?? 25, 1, 200);
    var rows = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT rule_code, recipient_email, recipient_name, subject, status, scheduled_for, sent_at, error_message
        FROM email_notification_outbox
        ORDER BY created_at DESC
        LIMIT @limit;
        """, connection);
    command.Parameters.AddWithValue("limit", maxRows);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new
        {
            ruleCode = reader.IsDBNull(0) ? null : reader.GetString(0),
            recipientEmail = reader.GetString(1),
            recipientName = reader.IsDBNull(2) ? null : reader.GetString(2),
            subject = reader.GetString(3),
            status = reader.GetString(4),
            scheduledFor = reader.GetFieldValue<DateTimeOffset>(5),
            sentAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
            errorMessage = reader.IsDBNull(7) ? null : reader.GetString(7)
        });
    }

    return Results.Ok(new { count = rows.Count, outbox = rows });
});


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
            batchCommand.Parameters.AddWithValue("notes", "Uploaded through Project Pulse holiday admin UI");
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


app.MapGet("/api/security/me", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);
    return Results.Ok(await BuildSecurityContextAsync(connection, userId));
});

app.MapGet("/api/security/role-matrix", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT
            r.role_code,
            r.role_name,
            r.role_description,
            COALESCE(array_agg(p.permission_code ORDER BY p.module_code, p.permission_code) FILTER (WHERE p.permission_code IS NOT NULL), ARRAY[]::text[]) AS permissions
        FROM app_roles r
        LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
        LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
        WHERE r.is_active = TRUE
        GROUP BY r.role_code, r.role_name, r.role_description, r.display_order
        ORDER BY r.display_order;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roles.Add(new
        {
            roleCode = reader.GetString(0),
            roleName = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            permissions = reader.GetFieldValue<string[]>(3)
        });
    }

    return Results.Ok(new { count = roles.Count, roles });
});


app.MapGet("/api/admin/roles", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var roles = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT role_code, role_name, role_description, display_order
        FROM app_roles
        WHERE is_active = TRUE
        ORDER BY display_order, role_name;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        roles.Add(new
        {
            roleCode = reader.GetString(0),
            roleName = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            displayOrder = reader.GetInt32(3)
        });
    }

    return Results.Ok(new { count = roles.Count, roles });
});

app.MapGet("/api/admin/users", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var users = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT
            u.user_id,
            u.email,
            u.display_name,
            COALESCE(u.job_title, '') AS job_title,
            COALESCE(u.department, '') AS department,
            u.is_active,
            COALESCE(array_agg(r.role_code ORDER BY r.display_order) FILTER (WHERE r.role_code IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_codes,
            COALESCE(array_agg(r.role_name ORDER BY r.display_order) FILTER (WHERE r.role_name IS NOT NULL AND ura.is_active = TRUE), ARRAY[]::text[]) AS role_names
        FROM app_users u
        LEFT JOIN app_user_role_assignments ura ON ura.user_id = u.user_id AND ura.is_active = TRUE
        LEFT JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
        WHERE u.is_active = TRUE
        GROUP BY u.user_id, u.email, u.display_name, u.job_title, u.department, u.is_active
        ORDER BY u.display_name, u.email;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        users.Add(new
        {
            userId = reader.GetGuid(0),
            email = reader.GetString(1),
            displayName = reader.GetString(2),
            jobTitle = reader.GetString(3),
            department = reader.GetString(4),
            isActive = reader.GetBoolean(5),
            roleCodes = reader.GetFieldValue<string[]>(6),
            roleNames = reader.GetFieldValue<string[]>(7)
        });
    }

    return Results.Ok(new { count = users.Count, users });
});

app.MapPost("/api/admin/users/roles", async (UserRoleAssignmentRequest request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new { status = "validation_failed", message = "Email is required." });
    }

    var roleCodes = request.RoleCodes?.Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code.Trim().ToUpperInvariant()).Distinct().ToArray() ?? Array.Empty<string>();
    if (roleCodes.Length == 0)
    {
        return Results.BadRequest(new { status = "validation_failed", message = "At least one role code is required." });
    }

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        var adminUserId = await GetOrCreateDevelopmentUserIdAsync(connection, transaction);
        Guid targetUserId;

        await using (var userCommand = new NpgsqlCommand("SELECT user_id FROM app_users WHERE lower(email) = lower(@email);", connection, transaction))
        {
            userCommand.Parameters.AddWithValue("email", request.Email.Trim());
            var result = await userCommand.ExecuteScalarAsync();
            if (result is null)
            {
                return Results.NotFound(new { status = "not_found", message = $"No user found for {request.Email}." });
            }
            targetUserId = (Guid)result;
        }

        await using (var deactivateCommand = new NpgsqlCommand("""
            UPDATE app_user_role_assignments
            SET is_active = FALSE,
                updated_at = NOW()
            WHERE user_id = @user_id;
            """, connection, transaction))
        {
            deactivateCommand.Parameters.AddWithValue("user_id", targetUserId);
            await deactivateCommand.ExecuteNonQueryAsync();
        }

        foreach (var roleCode in roleCodes)
        {
            await using var assignCommand = new NpgsqlCommand("""
                INSERT INTO app_user_role_assignments (user_id, app_role_id, assigned_by_user_id, assignment_reason, is_active)
                SELECT @user_id, app_role_id, @assigned_by_user_id, @assignment_reason, TRUE
                FROM app_roles
                WHERE role_code = @role_code
                  AND is_active = TRUE
                ON CONFLICT (user_id, app_role_id) DO UPDATE
                SET is_active = TRUE,
                    assigned_by_user_id = EXCLUDED.assigned_by_user_id,
                    assignment_reason = EXCLUDED.assignment_reason,
                    updated_at = NOW();
                """, connection, transaction);
            assignCommand.Parameters.AddWithValue("user_id", targetUserId);
            assignCommand.Parameters.AddWithValue("assigned_by_user_id", adminUserId);
            assignCommand.Parameters.AddWithValue("assignment_reason", string.IsNullOrWhiteSpace(request.Reason) ? "Role updated from Project Pulse role administration" : request.Reason.Trim());
            assignCommand.Parameters.AddWithValue("role_code", roleCode);
            await assignCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return Results.Ok(new { status = "roles_updated", email = request.Email.Trim(), roleCodes });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(title: "Failed to update user roles", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});



const int ProjectPulseSessionMinutes = 120;
const int ProjectPulseSessionWarningMinutes = 10;
const int ProjectPulsePasswordIterations = 210_000;

bool IsProjectPulsePublicApiPath(HttpContext context)
{
    var path = context.Request.Path.Value ?? string.Empty;

    if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var publicPaths = new[]
    {
        "/api/version",
        "/api/auth/login/route",
        "/api/auth/local/login",
        "/api/auth/sso/dev-login",
        "/api/auth/password-reset/request"
    };

    return publicPaths.Any(publicPath => path.Equals(publicPath, StringComparison.OrdinalIgnoreCase));
}

string? GetProjectPulseSessionToken(HttpRequest request)
{
    if (request.Headers.TryGetValue("X-ProjectPulse-Session", out var sessionHeader))
    {
        var token = sessionHeader.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(token)) return token.Trim();
    }

    var authorization = request.Headers.Authorization.ToString();
    if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return authorization["Bearer ".Length..].Trim();
    }

    return null;
}

string HashSessionToken(string token)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

string GenerateSessionToken()
{
    return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

string HashProjectPulsePassword(string password)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(
        password: Encoding.UTF8.GetBytes(password),
        salt: salt,
        iterations: ProjectPulsePasswordIterations,
        hashAlgorithm: HashAlgorithmName.SHA256,
        outputLength: 32);

    return $"PBKDF2-SHA256${ProjectPulsePasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
}

bool VerifyProjectPulsePassword(string password, string? storedHash)
{
    if (string.IsNullOrWhiteSpace(storedHash)) return false;

    var parts = storedHash.Split('$');
    if (parts.Length != 4) return false;
    if (!parts[0].Equals("PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase)) return false;
    if (!int.TryParse(parts[1], out var iterations)) return false;

    var salt = Convert.FromBase64String(parts[2]);
    var expectedHash = Convert.FromBase64String(parts[3]);

    var actualHash = Rfc2898DeriveBytes.Pbkdf2(
        password: Encoding.UTF8.GetBytes(password),
        salt: salt,
        iterations: iterations,
        hashAlgorithm: HashAlgorithmName.SHA256,
        outputLength: expectedHash.Length);

    return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
}

string? ValidatePasswordQuality(string password)
{
    if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
    {
        return "Password must be at least 12 characters long.";
    }

    if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
    {
        return "Password must include uppercase, lowercase, number, and special character.";
    }

    return null;
}

async Task<ProjectPulseCreatedSession> CreateProjectPulseSessionAsync(
    NpgsqlConnection connection,
    Guid userId,
    string providerCode,
    HttpRequest request)
{
    var rawToken = GenerateSessionToken();
    var tokenHash = HashSessionToken(rawToken);
    var sessionId = Guid.NewGuid();
    var expiresAt = DateTimeOffset.UtcNow.AddMinutes(ProjectPulseSessionMinutes);

    await using var command = new NpgsqlCommand("""
        INSERT INTO auth_sessions (
            auth_session_id,
            user_id,
            provider_code,
            session_token_hash,
            expires_at,
            ip_address,
            user_agent,
            session_window_minutes
        )
        VALUES (
            @auth_session_id,
            @user_id,
            @provider_code,
            @session_token_hash,
            @expires_at,
            @ip_address,
            @user_agent,
            @session_window_minutes
        );
        """, connection);

    command.Parameters.AddWithValue("auth_session_id", sessionId);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("provider_code", providerCode);
    command.Parameters.AddWithValue("session_token_hash", tokenHash);
    command.Parameters.AddWithValue("expires_at", expiresAt);
    command.Parameters.AddWithValue("ip_address", (object?)request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? DBNull.Value);
    command.Parameters.AddWithValue("user_agent", (object?)request.Headers.UserAgent.ToString() ?? DBNull.Value);
    command.Parameters.AddWithValue("session_window_minutes", ProjectPulseSessionMinutes);

    await command.ExecuteNonQueryAsync();

    return new ProjectPulseCreatedSession(sessionId, rawToken, expiresAt);
}

async Task<ProjectPulseSessionValidation> ValidateProjectPulseSessionAsync(HttpContext context)
{
    var token = GetProjectPulseSessionToken(context.Request);
    if (string.IsNullOrWhiteSpace(token))
    {
        return new ProjectPulseSessionValidation(false, null, null, null, null, "Missing session token.");
    }

    var config = DatabaseConfig.FromEnvironment();

    try
    {
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            SELECT
                s.auth_session_id,
                s.user_id,
                s.provider_code,
                s.expires_at,
                u.email
            FROM auth_sessions s
            JOIN app_users u ON u.user_id = s.user_id
            WHERE s.session_token_hash = @session_token_hash
              AND s.revoked_at IS NULL
              AND s.expires_at > NOW()
              AND u.is_active = TRUE
            LIMIT 1;
            """, connection);

        command.Parameters.AddWithValue("session_token_hash", HashSessionToken(token));

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return new ProjectPulseSessionValidation(false, null, null, null, null, "Session expired or invalid.");
        }

        var sessionId = reader.GetGuid(0);
        var userId = reader.GetGuid(1);
        var providerCode = reader.GetString(2);
        var expiresAt = reader.GetFieldValue<DateTimeOffset>(3);
        var email = reader.GetString(4);

        await reader.CloseAsync();

        await using var updateCommand = new NpgsqlCommand("""
            UPDATE auth_sessions
            SET last_seen_at = NOW()
            WHERE auth_session_id = @auth_session_id;
            """, connection);

        updateCommand.Parameters.AddWithValue("auth_session_id", sessionId);
        await updateCommand.ExecuteNonQueryAsync();

        return new ProjectPulseSessionValidation(true, userId, email, providerCode, expiresAt, null);
    }
    catch
    {
        return new ProjectPulseSessionValidation(false, null, null, null, null, "Unable to validate session.");
    }
}

async Task<bool> SessionUserIsAdministratorAsync(NpgsqlConnection connection, Guid userId)
{
    await using var command = new NpgsqlCommand("""
        SELECT EXISTS (
            SELECT 1
            FROM app_user_role_assignments ura
            JOIN app_roles r ON r.app_role_id = ura.app_role_id
            WHERE ura.user_id = @user_id
              AND ura.is_active = TRUE
              AND r.role_code = 'ADMINISTRATOR'
              AND r.is_active = TRUE
        );
        """, connection);

    command.Parameters.AddWithValue("user_id", userId);
    var result = await command.ExecuteScalarAsync();

    return result is bool value && value;
}


app.MapGet("/api/auth/login/route", async (string? username) =>
{
    var cleanedUsername = (username ?? string.Empty).Trim().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(cleanedUsername))
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "Username or email is required."
        });
    }

    if (cleanedUsername.EndsWith("@ussignal.com"))
    {
        return Results.Ok(new
        {
            status = "route_resolved",
            username = cleanedUsername,
            loginMethod = "sso",
            provider = "ENTRA_ID",
            displayName = "Continue with US Signal SSO",
            message = "US Signal users authenticate through Microsoft Entra ID."
        });
    }

    if (cleanedUsername.EndsWith(".local") || cleanedUsername.EndsWith("@ussignal.local"))
    {
        var config = DatabaseConfig.FromEnvironment();
        var missingResult = ValidateConfig(config);
        if (missingResult is not null) return missingResult;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM auth_local_accounts la
                JOIN app_users u ON u.user_id = la.user_id
                WHERE lower(la.username) = lower(@username)
                  AND la.is_active = TRUE
                  AND u.is_active = TRUE
            );
            """, connection);
        command.Parameters.AddWithValue("username", cleanedUsername);

        var exists = (bool)(await command.ExecuteScalarAsync() ?? false);

        return Results.Ok(new
        {
            status = exists ? "route_resolved" : "local_account_not_found",
            username = cleanedUsername,
            loginMethod = "local",
            provider = "LOCAL",
            displayName = "Project Pulse local administrator login",
            requiresPassword = true,
            message = exists
                ? "Local administrator account requires Project Pulse password authentication."
                : "No active local account was found for this username."
        });
    }

    return Results.BadRequest(new
    {
        status = "unsupported_login_domain",
        username = cleanedUsername,
        message = "Use your US Signal email address for SSO or a Project Pulse .local administrator account."
    });
});

app.MapPost("/api/auth/password-reset/request", async (PasswordResetRequest request) =>
{
    var username = (request.Username ?? string.Empty).Trim().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(username))
    {
        return Results.BadRequest(new
        {
            status = "validation_failed",
            message = "Username is required."
        });
    }

    if (!username.EndsWith(".local") && !username.EndsWith("@ussignal.local"))
    {
        return Results.BadRequest(new
        {
            status = "sso_account_reset_not_supported_here",
            message = "US Signal SSO users must reset passwords through Microsoft Entra ID. This Project Pulse reset workflow is only for .local administrator accounts."
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
        Guid userId;
        string displayName;

        await using (var userCommand = new NpgsqlCommand("""
            SELECT u.user_id, u.display_name
            FROM auth_local_accounts la
            JOIN app_users u ON u.user_id = la.user_id
            WHERE lower(la.username) = lower(@username)
              AND la.is_active = TRUE
              AND u.is_active = TRUE;
            """, connection, transaction))
        {
            userCommand.Parameters.AddWithValue("username", username);
            await using var reader = await userCommand.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Results.NotFound(new
                {
                    status = "local_account_not_found",
                    message = "No active local administrator account was found for that username."
                });
            }

            userId = reader.GetGuid(0);
            displayName = reader.GetString(1);
        }

        Guid resetRequestId;
        await using (var resetCommand = new NpgsqlCommand("""
            INSERT INTO auth_password_reset_requests (
                user_id,
                requested_by_email,
                approval_email_to,
                status,
                requested_at,
                expires_at,
                notes
            )
            VALUES (
                @user_id,
                @requested_by_email,
                ARRAY['ahmed.adeyemi@ussignal.com','customercare@ussignal.com'],
                'pending_approval',
                NOW(),
                NOW() + INTERVAL '24 hours',
                @notes
            )
            RETURNING auth_password_reset_request_id;
            """, connection, transaction))
        {
            resetCommand.Parameters.AddWithValue("user_id", userId);
            resetCommand.Parameters.AddWithValue("requested_by_email", username);
            resetCommand.Parameters.AddWithValue("notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());
            resetRequestId = (Guid)(await resetCommand.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create password reset request."));
        }

        foreach (var recipient in new[] { "ahmed.adeyemi@ussignal.com", "customercare@ussignal.com" })
        {
            await using var notifyCommand = new NpgsqlCommand("""
                INSERT INTO notification_outbox (
                    notification_type,
                    recipient_email,
                    subject,
                    body,
                    related_entity_type,
                    related_entity_id
                )
                VALUES (
                    'local_admin_password_reset_approval',
                    @recipient_email,
                    @subject,
                    @body,
                    'auth_password_reset_request',
                    @related_entity_id
                );
                """, connection, transaction);

            notifyCommand.Parameters.AddWithValue("recipient_email", recipient);
            notifyCommand.Parameters.AddWithValue("subject", "Project Pulse local administrator password reset approval required");
            notifyCommand.Parameters.AddWithValue("body", $"A password reset was requested for local administrator account {username} ({displayName}). Approval is required before reset can continue.");
            notifyCommand.Parameters.AddWithValue("related_entity_id", resetRequestId);
            await notifyCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "password_reset_pending_approval",
            username,
            resetRequestId,
            approvalRequired = true,
            approvalEmails = new[] { "ahmed.adeyemi@ussignal.com", "customercare@ussignal.com" },
            message = "Password reset request created. Approval notification has been queued."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to create password reset request",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/api/auth/local-accounts", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var accounts = new List<object>();
    await using var command = new NpgsqlCommand("""
        SELECT
            u.email,
            u.display_name,
            la.username,
            la.must_change_password,
            la.failed_login_count,
            la.locked_until,
            la.is_active
        FROM auth_local_accounts la
        JOIN app_users u ON u.user_id = la.user_id
        ORDER BY la.username;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        accounts.Add(new
        {
            email = reader.GetString(0),
            displayName = reader.GetString(1),
            username = reader.GetString(2),
            mustChangePassword = reader.GetBoolean(3),
            failedLoginCount = reader.GetInt32(4),
            lockedUntil = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5),
            isActive = reader.GetBoolean(6)
        });
    }

    return Results.Ok(new
    {
        count = accounts.Count,
        accounts
    });
});


app.MapGet("/api/utilization/current-quarter", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var quarterNumber = ((today.Month - 1) / 3) + 1;
    var quarterStartMonth = ((quarterNumber - 1) * 3) + 1;
    var quarterStart = new DateOnly(today.Year, quarterStartMonth, 1);
    var quarterEndExclusive = quarterStart.AddMonths(3);

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var userId = await GetOrCreateDevelopmentUserIdAsync(connection);

    string policyName;
    decimal standardPeriodHours;
    decimal targetPercent;

    await using (var policyCommand = new NpgsqlCommand("""
        SELECT policy_name, standard_period_hours, default_target_percent
        FROM utilization_policies
        WHERE is_active = TRUE
        ORDER BY created_at DESC
        LIMIT 1;
        """, connection))
    {
        await using var policyReader = await policyCommand.ExecuteReaderAsync();

        if (!await policyReader.ReadAsync())
        {
            return Results.Ok(new
            {
                status = "no_active_policy",
                message = "No active utilization policy is configured."
            });
        }

        policyName = policyReader.GetString(0);
        standardPeriodHours = policyReader.GetDecimal(1);
        targetPercent = policyReader.GetDecimal(2);
    }

    decimal currentBillableHours;

    await using (var hoursCommand = new NpgsqlCommand("""
        SELECT COALESCE(SUM(te.hours), 0)
        FROM time_entries te
        WHERE te.user_id = @user_id
          AND te.work_date >= @quarter_start
          AND te.work_date < @quarter_end
          AND te.billable = TRUE
          AND te.status <> 'manager_declined';
        """, connection))
    {
        hoursCommand.Parameters.AddWithValue("user_id", userId);
        hoursCommand.Parameters.AddWithValue("quarter_start", quarterStart);
        hoursCommand.Parameters.AddWithValue("quarter_end", quarterEndExclusive);

        currentBillableHours = (decimal)(await hoursCommand.ExecuteScalarAsync() ?? 0m);
    }

    var targetHours = Math.Round(standardPeriodHours * (targetPercent / 100m), 2);
    var currentUtilizationPercent = standardPeriodHours == 0
        ? 0m
        : Math.Round((currentBillableHours / standardPeriodHours) * 100m, 2);
    var hoursLeftToTarget = targetHours > currentBillableHours
        ? Math.Round(targetHours - currentBillableHours, 2)
        : 0m;

    return Results.Ok(new
    {
        policyName,
        quarter = $"Q{quarterNumber} {today.Year}",
        quarterStart,
        quarterEnd = quarterEndExclusive.AddDays(-1),
        standardPeriodHours,
        targetPercent,
        targetHours,
        currentBillableHours,
        currentUtilizationPercent,
        hoursLeftToTarget
    });
});


app.MapGet("/api/auth/password-reset/approvals", async () =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var approvals = new List<object>();

    await using var command = new NpgsqlCommand("""
        SELECT
            pr.auth_password_reset_request_id,
            pr.requested_by_email,
            pr.approval_email_to,
            pr.status,
            pr.requested_at,
            pr.expires_at,
            pr.notes,
            u.email AS account_email,
            u.display_name AS account_display_name
        FROM auth_password_reset_requests pr
        JOIN app_users u ON u.user_id = pr.user_id
        WHERE pr.status = 'pending_approval'
        ORDER BY pr.requested_at DESC;
        """, connection);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        approvals.Add(new
        {
            resetRequestId = reader.GetGuid(0),
            requestedByEmail = reader.GetString(1),
            approvalEmails = reader.GetFieldValue<string[]>(2),
            status = reader.GetString(3),
            requestedAt = reader.GetFieldValue<DateTimeOffset>(4),
            expiresAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5),
            notes = reader.IsDBNull(6) ? null : reader.GetString(6),
            accountEmail = reader.GetString(7),
            accountDisplayName = reader.GetString(8),
            approvalType = "local_admin_password_reset",
            approvalTitle = "Local administrator password reset",
            approvalDescription = "Approve or decline a password reset request for a Project Pulse local administrator account."
        });
    }

    return Results.Ok(new
    {
        count = approvals.Count,
        approvals
    });
});

app.MapPost("/api/auth/password-reset/approve", async (PasswordResetApprovalAction request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        Guid resetRequestId = request.ResetRequestId;
        string approvedByEmail = string.IsNullOrWhiteSpace(request.ActionByEmail)
            ? "ahmed.adeyemi@ussignal.com"
            : request.ActionByEmail.Trim().ToLowerInvariant();

        string? accountEmail = null;

        await using (var updateCommand = new NpgsqlCommand("""
            UPDATE auth_password_reset_requests
            SET status = 'approved',
                approved_at = NOW(),
                approved_by_email = @approved_by_email,
                notes = COALESCE(notes, '') || E'\nApproval note: ' || COALESCE(@notes, ''),
                expires_at = COALESCE(expires_at, NOW() + INTERVAL '24 hours')
            WHERE auth_password_reset_request_id = @reset_request_id
              AND status = 'pending_approval'
            RETURNING user_id;
            """, connection, transaction))
        {
            updateCommand.Parameters.AddWithValue("reset_request_id", resetRequestId);
            updateCommand.Parameters.AddWithValue("approved_by_email", approvedByEmail);
            updateCommand.Parameters.AddWithValue("notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());

            var userIdResult = await updateCommand.ExecuteScalarAsync();
            if (userIdResult is not Guid userId)
            {
                return Results.NotFound(new
                {
                    status = "not_found_or_already_processed",
                    message = "The password reset request was not found or has already been processed."
                });
            }

            await using var userCommand = new NpgsqlCommand("""
                SELECT email
                FROM app_users
                WHERE user_id = @user_id;
                """, connection, transaction);
            userCommand.Parameters.AddWithValue("user_id", userId);
            accountEmail = (string?)await userCommand.ExecuteScalarAsync();
        }

        foreach (var recipient in new[] { "ahmed.adeyemi@ussignal.com", "customercare@ussignal.com" })
        {
            await using var notifyCommand = new NpgsqlCommand("""
                INSERT INTO notification_outbox (
                    notification_type,
                    recipient_email,
                    subject,
                    body,
                    related_entity_type,
                    related_entity_id
                )
                VALUES (
                    'local_admin_password_reset_approved',
                    @recipient_email,
                    @subject,
                    @body,
                    'auth_password_reset_request',
                    @related_entity_id
                );
                """, connection, transaction);

            notifyCommand.Parameters.AddWithValue("recipient_email", recipient);
            notifyCommand.Parameters.AddWithValue("subject", "Project Pulse local administrator password reset approved");
            notifyCommand.Parameters.AddWithValue("body", $"Password reset request for {accountEmail} was approved by {approvedByEmail}. The next step is to set a temporary password once local password hashing is enabled.");
            notifyCommand.Parameters.AddWithValue("related_entity_id", resetRequestId);
            await notifyCommand.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "approved",
            resetRequestId,
            accountEmail,
            approvedByEmail,
            message = "Password reset request approved. A notification has been queued. Temporary password setup will be completed in the local password hashing phase."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to approve password reset request",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/auth/password-reset/decline", async (PasswordResetApprovalAction request) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    string declinedByEmail = string.IsNullOrWhiteSpace(request.ActionByEmail)
        ? "ahmed.adeyemi@ussignal.com"
        : request.ActionByEmail.Trim().ToLowerInvariant();

    await using var command = new NpgsqlCommand("""
        UPDATE auth_password_reset_requests
        SET status = 'declined',
            approved_by_email = @declined_by_email,
            notes = COALESCE(notes, '') || E'\nDecline note: ' || COALESCE(@notes, '')
        WHERE auth_password_reset_request_id = @reset_request_id
          AND status = 'pending_approval';
        """, connection);

    command.Parameters.AddWithValue("reset_request_id", request.ResetRequestId);
    command.Parameters.AddWithValue("declined_by_email", declinedByEmail);
    command.Parameters.AddWithValue("notes", string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim());

    var rows = await command.ExecuteNonQueryAsync();

    if (rows == 0)
    {
        return Results.NotFound(new
        {
            status = "not_found_or_already_processed",
            message = "The password reset request was not found or has already been processed."
        });
    }

    return Results.Ok(new
    {
        status = "declined",
        resetRequestId = request.ResetRequestId,
        declinedByEmail,
        message = "Password reset request declined."
    });
});


app.MapPost("/api/auth/sso/dev-login", async (SsoDevelopmentLoginRequest request, HttpRequest httpRequest) =>
{
    var email = request.Email.Trim().ToLowerInvariant();

    if (!email.EndsWith("@ussignal.com", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            status = "invalid_sso_domain",
            message = "US Signal SSO is only available for @ussignal.com accounts."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    await using var userCommand = new NpgsqlCommand("""
        SELECT user_id, email, display_name
        FROM app_users
        WHERE lower(email) = @email
          AND is_active = TRUE
        LIMIT 1;
        """, connection);

    userCommand.Parameters.AddWithValue("email", email);

    await using var reader = await userCommand.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return Results.NotFound(new
        {
            status = "sso_user_not_found",
            message = "No active Project Pulse user was found for this US Signal SSO account."
        });
    }

    var userId = reader.GetGuid(0);
    var resolvedEmail = reader.GetString(1);
    var displayName = reader.GetString(2);

    await reader.CloseAsync();

    var session = await CreateProjectPulseSessionAsync(connection, userId, "ENTRA_ID", httpRequest);

    return Results.Ok(new
    {
        status = "sso_development_session_created",
        loginMethod = "sso",
        provider = "ENTRA_ID",
        username = resolvedEmail,
        displayName,
        sessionToken = session.RawToken,
        expiresAt = session.ExpiresAt,
        sessionMinutes = ProjectPulseSessionMinutes,
        warningMinutes = ProjectPulseSessionWarningMinutes,
        message = "Development SSO session created. Replace this endpoint with Microsoft Entra ID token validation for production."
    });
});

app.MapPost("/api/auth/local/login", async (LocalLoginRequest request, HttpRequest httpRequest) =>
{
    var username = request.Username.Trim().ToLowerInvariant();

    if (!username.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
        !username.EndsWith("@ussignal.local", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            status = "invalid_local_account",
            message = "Local login is only available for .local administrator accounts."
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    await using var command = new NpgsqlCommand("""
        SELECT
            la.user_id,
            la.username,
            la.password_hash,
            la.must_change_password,
            la.failed_login_count,
            la.locked_until,
            la.is_active,
            u.email,
            u.display_name
        FROM auth_local_accounts la
        JOIN app_users u ON u.user_id = la.user_id
        WHERE lower(la.username) = @username
        LIMIT 1;
        """, connection);

    command.Parameters.AddWithValue("username", username);

    await using var reader = await command.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
    {
        return Results.Unauthorized();
    }

    var userId = reader.GetGuid(0);
    var resolvedUsername = reader.GetString(1);
    var passwordHash = reader.IsDBNull(2) ? null : reader.GetString(2);
    var mustChangePassword = reader.GetBoolean(3);
    var failedLoginCount = reader.GetInt32(4);
    var lockedUntil = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5);
    var isActive = reader.GetBoolean(6);
    var email = reader.GetString(7);
    var displayName = reader.GetString(8);

    await reader.CloseAsync();

    if (!isActive)
    {
        return Results.Unauthorized();
    }

    if (lockedUntil is not null && lockedUntil.Value > DateTimeOffset.UtcNow)
    {
        return Results.Json(new
        {
            status = "local_account_locked",
            lockedUntil,
            message = "This local account is temporarily locked because of failed login attempts."
        }, statusCode: StatusCodes.Status423Locked);
    }

    if (string.IsNullOrWhiteSpace(passwordHash))
    {
        return Results.Json(new
        {
            status = "local_password_not_configured",
            message = "This local account does not have a password configured yet. Approve a password reset and set a temporary password first."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var passwordValid = VerifyProjectPulsePassword(request.Password, passwordHash);

    if (!passwordValid)
    {
        var newFailedCount = failedLoginCount + 1;
        DateTimeOffset? newLockedUntil = newFailedCount >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15) : null;

        await using var failCommand = new NpgsqlCommand("""
            UPDATE auth_local_accounts
            SET failed_login_count = @failed_login_count,
                locked_until = @locked_until
            WHERE user_id = @user_id;
            """, connection);

        failCommand.Parameters.AddWithValue("failed_login_count", newFailedCount);
        failCommand.Parameters.AddWithValue("locked_until", (object?)newLockedUntil ?? DBNull.Value);
        failCommand.Parameters.AddWithValue("user_id", userId);
        await failCommand.ExecuteNonQueryAsync();

        return Results.Json(new
        {
            status = "invalid_local_credentials",
            failedLoginCount = newFailedCount,
            lockedUntil = newLockedUntil,
            message = newLockedUntil is null
                ? "Invalid local administrator credentials."
                : "Invalid credentials. This local account has been temporarily locked for 15 minutes."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    await using var successCommand = new NpgsqlCommand("""
        UPDATE auth_local_accounts
        SET failed_login_count = 0,
            locked_until = NULL
        WHERE user_id = @user_id;
        """, connection);

    successCommand.Parameters.AddWithValue("user_id", userId);
    await successCommand.ExecuteNonQueryAsync();

    var session = await CreateProjectPulseSessionAsync(connection, userId, "LOCAL", httpRequest);

    return Results.Ok(new
    {
        status = "local_login_success",
        loginMethod = "local",
        provider = "LOCAL",
        username = resolvedUsername,
        email,
        displayName,
        mustChangePassword,
        sessionToken = session.RawToken,
        expiresAt = session.ExpiresAt,
        sessionMinutes = ProjectPulseSessionMinutes,
        warningMinutes = ProjectPulseSessionWarningMinutes
    });
});

app.MapPost("/api/auth/session/extend", async (HttpRequest httpRequest) =>
{
    var token = GetProjectPulseSessionToken(httpRequest);

    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.Unauthorized();
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var newExpiresAt = DateTimeOffset.UtcNow.AddMinutes(ProjectPulseSessionMinutes);

    await using var command = new NpgsqlCommand("""
        UPDATE auth_sessions
        SET expires_at = @expires_at,
            last_seen_at = NOW()
        WHERE session_token_hash = @session_token_hash
          AND revoked_at IS NULL
          AND expires_at > NOW()
        RETURNING auth_session_id;
        """, connection);

    command.Parameters.AddWithValue("expires_at", newExpiresAt);
    command.Parameters.AddWithValue("session_token_hash", HashSessionToken(token));

    var result = await command.ExecuteScalarAsync();

    if (result is not Guid)
    {
        return Results.Json(new
        {
            status = "session_expired",
            message = "Your session has already expired. Please sign in again."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        status = "session_extended",
        expiresAt = newExpiresAt,
        sessionMinutes = ProjectPulseSessionMinutes,
        warningMinutes = ProjectPulseSessionWarningMinutes,
        message = "Your Project Pulse session has been extended."
    });
});

app.MapPost("/api/auth/session/logout", async (HttpRequest httpRequest) =>
{
    var token = GetProjectPulseSessionToken(httpRequest);

    if (!string.IsNullOrWhiteSpace(token))
    {
        var config = DatabaseConfig.FromEnvironment();
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            UPDATE auth_sessions
            SET revoked_at = NOW(),
                revoked_reason = 'user_logout'
            WHERE session_token_hash = @session_token_hash
              AND revoked_at IS NULL;
            """, connection);

        command.Parameters.AddWithValue("session_token_hash", HashSessionToken(token));
        await command.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        status = "signed_out",
        message = "Signed out of Project Pulse."
    });
});

app.MapPost("/api/auth/local/set-temporary-password", async (SetTemporaryPasswordRequest request, HttpRequest httpRequest) =>
{
    var token = GetProjectPulseSessionToken(httpRequest);
    if (string.IsNullOrWhiteSpace(token)) return Results.Unauthorized();

    var passwordIssue = ValidatePasswordQuality(request.TemporaryPassword);
    if (passwordIssue is not null)
    {
        return Results.BadRequest(new
        {
            status = "password_quality_failed",
            message = passwordIssue
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
        var tokenHash = HashSessionToken(token);

        Guid? actingUserId = null;
        await using (var sessionCommand = new NpgsqlCommand("""
            SELECT user_id
            FROM auth_sessions
            WHERE session_token_hash = @session_token_hash
              AND revoked_at IS NULL
              AND expires_at > NOW()
            LIMIT 1;
            """, connection, transaction))
        {
            sessionCommand.Parameters.AddWithValue("session_token_hash", tokenHash);
            var result = await sessionCommand.ExecuteScalarAsync();
            if (result is Guid resolvedUserId) actingUserId = resolvedUserId;
        }

        if (actingUserId is null)
        {
            await transaction.RollbackAsync();
            return Results.Unauthorized();
        }

        var isAdmin = await SessionUserIsAdministratorAsync(connection, actingUserId.Value);
        if (!isAdmin)
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "admin_required",
                message = "Only an administrator can set a temporary local administrator password."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var username = request.Username.Trim().ToLowerInvariant();
        var newHash = HashProjectPulsePassword(request.TemporaryPassword);

        await using var updateCommand = new NpgsqlCommand("""
            UPDATE auth_local_accounts la
            SET password_hash = @password_hash,
                must_change_password = TRUE,
                failed_login_count = 0,
                locked_until = NULL,
                password_hash_updated_at = NOW()
            FROM app_users u
            WHERE u.user_id = la.user_id
              AND lower(la.username) = @username
              AND u.is_active = TRUE
              AND EXISTS (
                  SELECT 1
                  FROM auth_password_reset_requests pr
                  WHERE pr.auth_password_reset_request_id = @reset_request_id
                    AND pr.user_id = la.user_id
                    AND pr.status IN ('approved', 'temporary_password_set')
              );
            """, connection, transaction);

        updateCommand.Parameters.AddWithValue("password_hash", newHash);
        updateCommand.Parameters.AddWithValue("username", username);
        updateCommand.Parameters.AddWithValue("reset_request_id", request.ResetRequestId);

        var rows = await updateCommand.ExecuteNonQueryAsync();

        if (rows == 0)
        {
            await transaction.RollbackAsync();
            return Results.NotFound(new
            {
                status = "approved_reset_not_found",
                message = "No approved password reset request was found for this local account."
            });
        }

        await using var resetCommand = new NpgsqlCommand("""
            UPDATE auth_password_reset_requests
            SET status = 'temporary_password_set',
                notes = COALESCE(notes, '') || E'\nTemporary password was set by administrator.'
            WHERE auth_password_reset_request_id = @reset_request_id;
            """, connection, transaction);

        resetCommand.Parameters.AddWithValue("reset_request_id", request.ResetRequestId);
        await resetCommand.ExecuteNonQueryAsync();

        await transaction.CommitAsync();

        return Results.Ok(new
        {
            status = "temporary_password_set",
            username,
            mustChangePassword = true,
            message = "Temporary password has been set. The local administrator must change it after login."
        });
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();
        return Results.Problem(
            title: "Failed to set temporary local password",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/api/auth/local/change-password", async (ChangeLocalPasswordRequest request, HttpRequest httpRequest) =>
{
    var token = GetProjectPulseSessionToken(httpRequest);
    if (string.IsNullOrWhiteSpace(token)) return Results.Unauthorized();

    var passwordIssue = ValidatePasswordQuality(request.NewPassword);
    if (passwordIssue is not null)
    {
        return Results.BadRequest(new
        {
            status = "password_quality_failed",
            message = passwordIssue
        });
    }

    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    Guid? sessionUserId = null;

    await using (var sessionCommand = new NpgsqlCommand("""
        SELECT user_id
        FROM auth_sessions
        WHERE session_token_hash = @session_token_hash
          AND provider_code = 'LOCAL'
          AND revoked_at IS NULL
          AND expires_at > NOW()
        LIMIT 1;
        """, connection))
    {
        sessionCommand.Parameters.AddWithValue("session_token_hash", HashSessionToken(token));
        var result = await sessionCommand.ExecuteScalarAsync();
        if (result is Guid resolvedUserId) sessionUserId = resolvedUserId;
    }

    if (sessionUserId is null)
    {
        return Results.Json(new
        {
            status = "local_session_required",
            message = "A valid local administrator session is required to change this password."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    string? currentHash = null;

    await using (var lookupCommand = new NpgsqlCommand("""
        SELECT password_hash
        FROM auth_local_accounts
        WHERE user_id = @user_id
          AND is_active = TRUE;
        """, connection))
    {
        lookupCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
        currentHash = (string?)await lookupCommand.ExecuteScalarAsync();
    }

    if (!VerifyProjectPulsePassword(request.CurrentPassword, currentHash))
    {
        return Results.Json(new
        {
            status = "invalid_current_password",
            message = "The current password is incorrect."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var newHash = HashProjectPulsePassword(request.NewPassword);

    await using var updateCommand = new NpgsqlCommand("""
        UPDATE auth_local_accounts
        SET password_hash = @password_hash,
            must_change_password = FALSE,
            failed_login_count = 0,
            locked_until = NULL,
            password_hash_updated_at = NOW(),
            last_password_change_at = NOW()
        WHERE user_id = @user_id;
        """, connection);

    updateCommand.Parameters.AddWithValue("password_hash", newHash);
    updateCommand.Parameters.AddWithValue("user_id", sessionUserId.Value);
    await updateCommand.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        status = "password_changed",
        mustChangePassword = false,
        message = "Local administrator password changed successfully."
    });
});


app.Run();


static bool CanEngineerUnlockDay(string? status, DateTimeOffset? submittedAt)
{
    return status == "submitted"
        && submittedAt is not null
        && DateTimeOffset.UtcNow - submittedAt.Value <= TimeSpan.FromHours(2);
}

static string GetDayUnlockMessage(string? status, DateTimeOffset? submittedAt)
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


static async Task<object> LoadTimesheetPreferencesAsync(NpgsqlConnection connection, Guid userId)
{
    const string sql = """
        INSERT INTO user_timesheet_preferences (user_id)
        VALUES (@user_id)
        ON CONFLICT (user_id) DO NOTHING;

        SELECT default_non_project_category_codes,
               default_project_task_ids,
               auto_add_holidays,
               weekly_reminder_enabled,
               reminder_day_of_week,
               reminder_local_time,
               timezone_name
        FROM user_timesheet_preferences
        WHERE user_id = @user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);

    await using var reader = await command.ExecuteReaderAsync();
    await reader.ReadAsync();

    return new
    {
        defaultNonProjectCategoryCodes = reader.GetFieldValue<string[]>(0),
        defaultProjectTaskIds = reader.GetFieldValue<Guid[]>(1),
        autoAddHolidays = reader.GetBoolean(2),
        weeklyReminderEnabled = reader.GetBoolean(3),
        reminderDayOfWeek = reader.GetInt32(4),
        reminderLocalTime = reader.GetFieldValue<TimeOnly>(5).ToString("HH:mm"),
        timezoneName = reader.GetString(6)
    };
}

static async Task<IResult> QueueReminderRuleAsync(string ruleCode)
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    const string sql = """
        INSERT INTO email_notification_outbox (rule_code, recipient_email, recipient_name, subject, body, status, scheduled_for)
        SELECT
            rr.rule_code,
            u.email,
            u.display_name,
            rr.subject_template,
            REPLACE(rr.body_template, '{{display_name}}', u.display_name),
            'queued',
            NOW()
        FROM reminder_rules rr
        INNER JOIN notification_groups ng ON ng.group_code = rr.recipient_group_code
        INNER JOIN notification_group_members ngm ON ngm.notification_group_id = ng.notification_group_id AND ngm.is_active = TRUE
        INNER JOIN app_users u ON u.user_id = ngm.user_id AND u.is_active = TRUE
        WHERE rr.rule_code = @rule_code
          AND rr.is_active = TRUE
          AND ng.is_active = TRUE;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("rule_code", ruleCode);
    var inserted = await command.ExecuteNonQueryAsync();

    return Results.Ok(new { status = "queued", ruleCode, queuedCount = inserted });
}


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


static async Task<object> BuildSecurityContextAsync(NpgsqlConnection connection, Guid userId)
{
    string? email = null;
    string? displayName = null;

    await using (var userCommand = new NpgsqlCommand("SELECT email, display_name FROM app_users WHERE user_id = @user_id;", connection))
    {
        userCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await userCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            email = reader.GetString(0);
            displayName = reader.GetString(1);
        }
    }

    var roles = new List<object>();
    await using (var roleCommand = new NpgsqlCommand("""
        SELECT r.role_code, r.role_name, r.role_description
        FROM app_user_role_assignments ura
        INNER JOIN app_roles r ON r.app_role_id = ura.app_role_id
        WHERE ura.user_id = @user_id
          AND ura.is_active = TRUE
          AND r.is_active = TRUE
        ORDER BY r.display_order;
        """, connection))
    {
        roleCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await roleCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(new
            {
                roleCode = reader.GetString(0),
                roleName = reader.GetString(1),
                description = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }
    }

    var permissions = new List<string>();
    await using (var permissionCommand = new NpgsqlCommand("""
        SELECT DISTINCT p.permission_code
        FROM app_user_role_assignments ura
        INNER JOIN app_roles r ON r.app_role_id = ura.app_role_id
        INNER JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
        INNER JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
        WHERE ura.user_id = @user_id
          AND ura.is_active = TRUE
          AND r.is_active = TRUE
        ORDER BY p.permission_code;
        """, connection))
    {
        permissionCommand.Parameters.AddWithValue("user_id", userId);
        await using var reader = await permissionCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync()) permissions.Add(reader.GetString(0));
    }

    var features = new List<object>();
    await using (var featureCommand = new NpgsqlCommand("""
        SELECT feature_code, feature_name, module_code, route_anchor, required_permission_code, feature_description
        FROM app_feature_catalog
        WHERE is_active = TRUE
          AND (required_permission_code IS NULL OR required_permission_code = ANY(@permissions))
        ORDER BY display_order;
        """, connection))
    {
        featureCommand.Parameters.AddWithValue("permissions", permissions.ToArray());
        await using var reader = await featureCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            features.Add(new
            {
                featureCode = reader.GetString(0),
                featureName = reader.GetString(1),
                moduleCode = reader.GetString(2),
                routeAnchor = reader.IsDBNull(3) ? null : reader.GetString(3),
                requiredPermissionCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                description = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }
    }

    return new
    {
        userId,
        email,
        displayName,
        roles,
        permissions,
        features,
        can = new
        {
            viewTimeEntry = permissions.Contains("VIEW_TIME_ENTRY"),
            editOwnTime = permissions.Contains("EDIT_OWN_TIME"),
            approveTime = permissions.Contains("APPROVE_TIME"),
            rejectTime = permissions.Contains("REJECT_TIME"),
            manageHolidays = permissions.Contains("MANAGE_HOLIDAYS"),
            viewHolidays = permissions.Contains("VIEW_HOLIDAYS"),
            viewProjectIntake = permissions.Contains("VIEW_PROJECT_INTAKE"),
            viewResourceScheduling = permissions.Contains("VIEW_RESOURCE_SCHEDULING"),
            viewExpenses = permissions.Contains("VIEW_EXPENSES"),
            viewExecutiveReporting = permissions.Contains("VIEW_EXECUTIVE_REPORTING"),
            viewAuditTrail = permissions.Contains("VIEW_AUDIT_TRAIL"),
            exportTimePdf = permissions.Contains("EXPORT_TIME_PDF"),
            exportTimeExcel = permissions.Contains("EXPORT_TIME_EXCEL"),
            systemAdministration = permissions.Contains("SYSTEM_ADMINISTRATION"),
            manageAll = permissions.Contains("MANAGE_ALL")
        }
    };
}

static IResult? ValidateConfig(DatabaseConfig config)
{
    if (config.Missing.Count == 0)
    {
        return null;
    }

    return Results.BadRequest(new
    {
        status = "configuration_missing",
        missing = config.Missing
    });
}

static DateOnly GetSundayForDate(DateOnly date)
{
    var offset = (int)date.DayOfWeek;
    return date.AddDays(-offset);
}

static IReadOnlyList<string> ValidateTimesheetRequest(TimesheetSaveRequest request)
{
    var errors = new List<string>();
    var start = GetSundayForDate(request.WeekStart);
    var end = start.AddDays(6);

    if (request.Entries is null)
    {
        errors.Add("Entries collection is required.");
        return errors;
    }

    foreach (var entry in request.Entries)
    {
        if (entry.WorkDate < start || entry.WorkDate > end)
        {
            errors.Add($"Entry date {entry.WorkDate} is outside the selected week {start} through {end}.");
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


static async Task<List<object>> LoadOpenAssignedProjectTasksAsync(NpgsqlConnection connection, Guid userId, DateOnly weekStart, DateOnly weekEnd)
{
    var tasks = new List<object>();

    const string sql = """
        SELECT DISTINCT
            pa.project_assignment_id,
            p.project_id,
            p.project_code,
            p.project_name,
            c.client_name,
            c.client_code,
            pt.task_id,
            pt.task_code,
            pt.task_name,
            pt.task_description,
            COALESCE(pa.allocation_percent, 0) AS allocation_percent,
            pa.effective_start_date,
            pa.effective_end_date,
            p.project_manager_user_id,
            pm.display_name AS project_manager_name
        FROM project_assignments pa
        INNER JOIN projects p ON p.project_id = pa.project_id
        INNER JOIN project_tasks pt ON pt.task_id = pa.task_id
        LEFT JOIN clients c ON c.client_id = p.client_id
        LEFT JOIN app_users pm ON pm.user_id = p.project_manager_user_id
        WHERE pa.user_id = @user_id
          AND p.status = 'active'
          AND pt.is_active = TRUE
          AND pa.effective_start_date <= @week_end
          AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start)
        ORDER BY p.project_code, pt.task_code;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start", weekStart);
    command.Parameters.AddWithValue("week_end", weekEnd);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        tasks.Add(new
        {
            assignmentId = reader.GetGuid(0),
            projectId = reader.GetGuid(1),
            projectCode = reader.GetString(2),
            projectName = reader.GetString(3),
            clientName = reader.IsDBNull(4) ? null : reader.GetString(4),
            clientCode = reader.IsDBNull(5) ? null : reader.GetString(5),
            taskId = reader.GetGuid(6),
            taskCode = reader.GetString(7),
            taskName = reader.GetString(8),
            taskDescription = reader.IsDBNull(9) ? null : reader.GetString(9),
            allocationPercent = reader.GetDecimal(10),
            effectiveStartDate = reader.GetFieldValue<DateOnly>(11),
            effectiveEndDate = reader.IsDBNull(12) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(12),
            projectManagerUserId = reader.IsDBNull(13) ? (Guid?)null : reader.GetGuid(13),
            projectManagerName = reader.IsDBNull(14) ? null : reader.GetString(14)
        });
    }

    return tasks;
}

static async Task<Guid> GetOrCreateDevelopmentUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
{
    const string sql = """
        INSERT INTO app_users (email, display_name, job_title, department, is_active)
        VALUES ('ahmed.adeyemi@ussignal.com', 'Ahmed Adeyemi', 'Development Engineer', 'Professional Services', TRUE)
        ON CONFLICT (email) DO UPDATE
        SET display_name = EXCLUDED.display_name,
            job_title = EXCLUDED.job_title,
            department = EXCLUDED.department,
            is_active = TRUE,
            updated_at = NOW()
        RETURNING user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create development user."));
}


static async Task<string?> GetTimesheetStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        SELECT status
        FROM timesheets
        WHERE user_id = @user_id
          AND week_start_date = @week_start_date;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);

    return (string?)await command.ExecuteScalarAsync();
}


static async Task<Guid> UpsertDraftShellForEditableSaveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        INSERT INTO timesheets (user_id, week_start_date, week_end_date, status, submitted_at)
        VALUES (@user_id, @week_start_date, @week_end_date, 'draft', NULL)
        ON CONFLICT (user_id, week_start_date) DO UPDATE
        SET week_end_date = EXCLUDED.week_end_date,
            status = CASE
                WHEN timesheets.status IN ('draft', 'manager_declined') THEN 'draft'
                ELSE timesheets.status
            END,
            submitted_at = CASE
                WHEN timesheets.status IN ('draft', 'manager_declined') THEN NULL
                ELSE timesheets.submitted_at
            END,
            updated_at = NOW()
        RETURNING timesheet_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);
    command.Parameters.AddWithValue("week_end_date", weekStart.AddDays(6));

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create draft timesheet shell."));
}

static async Task ReplaceEditableTimeEntriesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    var protectedDates = new HashSet<DateOnly>();

    await using (var protectedCommand = new NpgsqlCommand("""
        SELECT work_date
        FROM timesheet_day_statuses
        WHERE timesheet_id = @timesheet_id
          AND status IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked');
        """, connection, transaction))
    {
        protectedCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await using var reader = await protectedCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            protectedDates.Add(reader.GetFieldValue<DateOnly>(0));
        }
    }

    await using (var deleteCommand = new NpgsqlCommand("""
        DELETE FROM time_entries
        WHERE timesheet_id = @timesheet_id
          AND NOT EXISTS (
              SELECT 1
              FROM timesheet_day_statuses tds
              WHERE tds.timesheet_id = time_entries.timesheet_id
                AND tds.work_date = time_entries.work_date
                AND tds.status IN ('submitted', 'manager_approved', 'pm_approved', 'accounting_ready', 'reconciled', 'locked')
          );
        """, connection, transaction))
    {
        deleteCommand.Parameters.AddWithValue("timesheet_id", timesheetId);
        await deleteCommand.ExecuteNonQueryAsync();
    }

    var editableEntries = entries
        .Where(entry => entry.Hours > 0)
        .Where(entry => !protectedDates.Contains(entry.WorkDate))
        .ToList();

    if (editableEntries.Count > 0)
    {
        await ReplaceTimeEntriesForEditableDaysAsync(connection, transaction, timesheetId, userId, editableEntries, status);
    }
}

static async Task ReplaceTimeEntriesForEditableDaysAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    foreach (var entry in entries.Where(item => item.Hours > 0))
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

static async Task<Guid> UpsertDraftTimesheetAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid userId, DateOnly weekStart)
{
    const string sql = """
        INSERT INTO timesheets (user_id, week_start_date, week_end_date, status, submitted_at)
        VALUES (@user_id, @week_start_date, @week_end_date, 'draft', NULL)
        ON CONFLICT (user_id, week_start_date) DO UPDATE
        SET week_end_date = EXCLUDED.week_end_date,
            status = 'draft',
            submitted_at = NULL,
            updated_at = NOW()
        RETURNING timesheet_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);
    command.Parameters.AddWithValue("week_end_date", weekStart.AddDays(6));

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Unable to create draft timesheet."));
}


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

static async Task ReplaceTimeEntriesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    await using (var deleteCommand = new NpgsqlCommand("""
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

    foreach (var entry in entries.Where(item => item.Hours > 0))
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


static async Task InsertTimeEntriesWithoutDeletingAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    foreach (var entry in entries.Where(item => item.Hours > 0))
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

static async Task<Guid> GetNonProjectCategoryIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string categoryCode)
{
    const string sql = """
        SELECT non_project_time_category_id
        FROM non_project_time_categories
        WHERE category_code = @category_code
          AND is_active = TRUE;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("category_code", categoryCode);

    return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"Unknown or inactive non-project time category: {categoryCode}"));
}

static async Task MarkTimesheetSubmittedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid timesheetId)
{
    const string sql = """
        UPDATE timesheets
        SET status = 'submitted',
            submitted_at = NOW(),
            updated_at = NOW()
        WHERE timesheet_id = @timesheet_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);
    await command.ExecuteNonQueryAsync();
}

static async Task InsertAuditLogAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorUserId, string action, string entityType, Guid entityId)
{
    const string sql = """
        INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id)
        VALUES (@actor_user_id, @action, @entity_type, @entity_id);
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("actor_user_id", actorUserId);
    command.Parameters.AddWithValue("action", action);
    command.Parameters.AddWithValue("entity_type", entityType);
    command.Parameters.AddWithValue("entity_id", entityId);
    await command.ExecuteNonQueryAsync();
}

static async Task<IReadOnlyList<object>> LoadNonProjectCategoriesAsync(NpgsqlConnection connection)
{
    var categories = new List<object>();

    const string sql = """
        SELECT
            non_project_time_category_id,
            category_code,
            category_name,
            category_description,
            utilization_classification,
            utilization_bucket,
            requires_approval,
            is_active,
            display_order
        FROM non_project_time_categories
        WHERE is_active = TRUE
        ORDER BY display_order, category_name;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        categories.Add(new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            description = reader.IsDBNull(3) ? null : reader.GetString(3),
            utilizationClassification = reader.GetString(4),
            utilizationBucket = reader.GetString(5),
            requiresApproval = reader.GetBoolean(6),
            isActive = reader.GetBoolean(7),
            displayOrder = reader.GetInt32(8)
        });
    }

    return categories;
}

static async Task<object> BuildTimesheetWeekPayloadAsync(NpgsqlConnection connection, Guid userId, DateOnly start)
{
    var days = Enumerable.Range(0, 7)
        .Select(offset => start.AddDays(offset))
        .Select(date => new
        {
            date,
            dayName = date.DayOfWeek.ToString(),
            normalHours = 0.00m,
            afterhours = 0.00m
        })
        .ToList();

    var categories = await LoadTimesheetNonProjectCategoriesAsync(connection);
    var timesheet = await LoadTimesheetHeaderAsync(connection, userId, start);
    var entries = timesheet?.TimesheetId is null
        ? new List<object>()
        : await LoadSavedTimeEntriesAsync(connection, timesheet.TimesheetId.Value);
    var dayStatuses = await LoadDayStatusesAsync(connection, timesheet?.TimesheetId, start);

    return new
    {
        timesheetId = timesheet?.TimesheetId,
        status = timesheet?.Status ?? "draft",
        submittedAt = timesheet?.SubmittedAt,
        dayStatuses,
        weekStart = start,
        weekEnd = start.AddDays(6),
        days,
        timeTypes = new[] { "normal", "afterhours" },
        nonProjectCategories = categories,
        entries,
        note = "Weekly shell now includes saved draft and submitted time entry payloads."
    };
}

static async Task<IReadOnlyList<object>> LoadTimesheetNonProjectCategoriesAsync(NpgsqlConnection connection)
{
    var categories = new List<object>();

    const string categorySql = """
        SELECT category_code, category_name, category_description, utilization_bucket, requires_approval
        FROM non_project_time_categories
        WHERE is_active = TRUE
        ORDER BY display_order, category_name;
        """;

    await using var command = new NpgsqlCommand(categorySql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        categories.Add(new
        {
            code = reader.GetString(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            utilizationBucket = reader.GetString(3),
            requiresApproval = reader.GetBoolean(4)
        });
    }

    return categories;
}

static async Task<TimesheetHeader?> LoadTimesheetHeaderAsync(NpgsqlConnection connection, Guid userId, DateOnly weekStart)
{
    const string sql = """
        SELECT timesheet_id, status, submitted_at
        FROM timesheets
        WHERE user_id = @user_id
          AND week_start_date = @week_start_date;
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("user_id", userId);
    command.Parameters.AddWithValue("week_start_date", weekStart);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    return new TimesheetHeader(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2));
}


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
                canEdit = status is "draft" or "manager_declined",
                canUnlock = CanEngineerUnlockDay(status, submittedAt),
                unlockMessage = GetDayUnlockMessage(status, submittedAt)
            };
        })
        .ToList();
}


static async Task<List<object>> LoadSavedTimeEntriesAsync(NpgsqlConnection connection, Guid timesheetId)
{
    var entries = new List<object>();

    const string sql = """
        SELECT
            te.time_entry_id,
            te.work_date,
            te.time_type,
            te.hours,
            te.description,
            te.status,
            te.project_id,
            te.task_id,
            te.non_project_time_category_id,
            npt.category_code,
            npt.category_name,
            te.work_location_group_id,
            te.work_location_id,
            te.billable,
            p.project_code,
            p.project_name,
            pt.task_code,
            pt.task_name,
            c.client_name
        FROM time_entries te
        LEFT JOIN non_project_time_categories npt
            ON npt.non_project_time_category_id = te.non_project_time_category_id
        LEFT JOIN projects p
            ON p.project_id = te.project_id
        LEFT JOIN project_tasks pt
            ON pt.task_id = te.task_id
        LEFT JOIN clients c
            ON c.client_id = p.client_id
        WHERE te.timesheet_id = @timesheet_id
        ORDER BY te.work_date, te.time_type, COALESCE(npt.display_order, 999), COALESCE(npt.category_name, pt.task_name, p.project_name);
        """;

    await using var command = new NpgsqlCommand(sql, connection);
    command.Parameters.AddWithValue("timesheet_id", timesheetId);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var projectId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6);
        var taskId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7);
        var categoryCode = reader.IsDBNull(9) ? null : reader.GetString(9);

        entries.Add(new
        {
            id = reader.GetGuid(0),
            rowType = projectId is not null && taskId is not null ? "projectTask" : "nonProject",
            workDate = reader.GetFieldValue<DateOnly>(1),
            timeType = reader.GetString(2),
            hours = reader.GetDecimal(3),
            description = reader.IsDBNull(4) ? null : reader.GetString(4),
            status = reader.GetString(5),
            projectId,
            taskId,
            nonProjectTimeCategoryId = reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
            categoryCode,
            categoryName = reader.IsDBNull(10) ? null : reader.GetString(10),
            workLocationGroupId = reader.IsDBNull(11) ? (Guid?)null : reader.GetGuid(11),
            workLocationId = reader.IsDBNull(12) ? (Guid?)null : reader.GetGuid(12),
            billable = reader.GetBoolean(13),
            projectCode = reader.IsDBNull(14) ? null : reader.GetString(14),
            projectName = reader.IsDBNull(15) ? null : reader.GetString(15),
            taskCode = reader.IsDBNull(16) ? null : reader.GetString(16),
            taskName = reader.IsDBNull(17) ? null : reader.GetString(17),
            clientName = reader.IsDBNull(18) ? null : reader.GetString(18)
        });
    }

    return entries;
}




internal sealed record TimesheetDaySubmitRequest(DateOnly WeekStart, DateOnly WorkDate, List<TimesheetEntryRequest> Entries);

internal sealed record TimesheetDayUnlockRequest(DateOnly WeekStart, DateOnly WorkDate);

internal sealed record ManagerBulkApprovalRequest(List<ManagerApprovalActionRequest> Items, string? Comment);

internal sealed record ManagerApprovalActionRequest(Guid TimesheetId, DateOnly WorkDate, string? Comment);

internal sealed record TimesheetPreferenceRequest(List<string>? DefaultNonProjectCategoryCodes, List<Guid>? DefaultProjectTaskIds, bool AutoAddHolidays, bool WeeklyReminderEnabled);

internal sealed record HolidayCsvImportRequest(int? Year, string? Filename, string CsvText);
internal sealed record HolidayImportRow(DateOnly HolidayDate, string HolidayName, string HolidayType, bool IsFloatingHoliday, decimal AutoPopulateHours);

internal sealed record UserRoleAssignmentRequest(string Email, List<string>? RoleCodes, string? Reason);


internal sealed record LocalLoginRequest(string Username, string Password);
internal sealed record SsoDevelopmentLoginRequest(string Email);
internal sealed record SetTemporaryPasswordRequest(Guid ResetRequestId, string Username, string TemporaryPassword);
internal sealed record ChangeLocalPasswordRequest(string CurrentPassword, string NewPassword);
internal sealed record ProjectPulseCreatedSession(Guid SessionId, string RawToken, DateTimeOffset ExpiresAt);
internal sealed record ProjectPulseSessionValidation(bool IsValid, Guid? UserId, string? Email, string? ProviderCode, DateTimeOffset? ExpiresAt, string? Message);

internal sealed record PasswordResetApprovalAction(Guid ResetRequestId, string? ActionByEmail, string? Notes);

internal sealed record PasswordResetRequest(string Username, string? Notes);

internal sealed record TimesheetSaveRequest(DateOnly WeekStart, List<TimesheetEntryRequest> Entries);

internal sealed record TimesheetEntryRequest(
    string RowType,
    string? CategoryCode,
    DateOnly WorkDate,
    string TimeType,
    decimal Hours,
    string? Description,
    Guid? WorkLocationGroupId,
    Guid? WorkLocationId,
    Guid? ProjectId,
    Guid? TaskId);

internal sealed record TimesheetHeader(Guid? TimesheetId, string Status, DateTimeOffset? SubmittedAt);

internal sealed record DayStatusRecord(string Status, DateTimeOffset? SubmittedAt);

internal sealed record DatabaseConfig(
    string? Host,
    string? Port,
    string? Database,
    string? Username,
    string? Password,
    IReadOnlyList<string> Missing)
{
    public string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = int.TryParse(Port, out var parsedPort) ? parsedPort : 5432,
                Database = Database,
                Username = Username,
                Password = Password,
                IncludeErrorDetail = false,
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = 5
            };

            return builder.ConnectionString;
        }
    }

    public static DatabaseConfig FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(host)) missing.Add("PTP_DB_HOST");
        if (string.IsNullOrWhiteSpace(port)) missing.Add("PTP_DB_PORT");
        if (string.IsNullOrWhiteSpace(database)) missing.Add("PTP_DB_NAME");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("PTP_DB_USER");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("PTP_DB_PASSWORD");

        return new DatabaseConfig(host, port, database, username, password, missing);
    }
}
