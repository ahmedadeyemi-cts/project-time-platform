using Npgsql;
using System.Runtime.InteropServices;

const string DevelopmentUserEmail = "ahmed.adeyemi@ussignal.local";
const string DevelopmentUserDisplayName = "Ahmed Adeyemi";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();

var app = builder.Build();

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
    version = "0.3.0",
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
            upt.bonus_reference_amount,
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
            bonusReferenceAmount = reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3),
            displayOrder = reader.GetInt32(4)
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

        if (existingStatus is not null && existingStatus is not "draft" and not "manager_declined")
        {
            return Results.Conflict(new
            {
                status = "timesheet_not_editable",
                currentStatus = existingStatus,
                message = "Only draft or manager-declined timesheets can be edited."
            });
        }

        var timesheetId = await UpsertDraftTimesheetAsync(connection, transaction, userId, start);
        await ReplaceTimeEntriesAsync(connection, transaction, timesheetId, userId, request.Entries, "draft");
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

app.Run();

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

static async Task<Guid> GetOrCreateDevelopmentUserIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
{
    const string sql = """
        INSERT INTO app_users (email, display_name, job_title, department, is_active)
        VALUES (@email, @display_name, 'Development User', 'Project Time Platform', TRUE)
        ON CONFLICT (email) DO UPDATE
        SET display_name = EXCLUDED.display_name,
            updated_at = NOW()
        RETURNING user_id;
        """;

    await using var command = new NpgsqlCommand(sql, connection, transaction);
    command.Parameters.AddWithValue("email", DevelopmentUserEmail);
    command.Parameters.AddWithValue("display_name", DevelopmentUserDisplayName);

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

static async Task ReplaceTimeEntriesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid timesheetId,
    Guid userId,
    IReadOnlyList<TimesheetEntryRequest> entries,
    string status)
{
    await using (var deleteCommand = new NpgsqlCommand("DELETE FROM time_entries WHERE timesheet_id = @timesheet_id;", connection, transaction))
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

    return new
    {
        timesheetId = timesheet?.TimesheetId,
        status = timesheet?.Status ?? "draft",
        submittedAt = timesheet?.SubmittedAt,
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
            te.work_location_group_id,
            te.work_location_id,
            te.billable
        FROM time_entries te
        LEFT JOIN non_project_time_categories npt
            ON npt.non_project_time_category_id = te.non_project_time_category_id
        WHERE te.timesheet_id = @timesheet_id
        ORDER BY te.work_date, te.time_type, npt.display_order;
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
            workLocationGroupId = reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10),
            workLocationId = reader.IsDBNull(11) ? (Guid?)null : reader.GetGuid(11),
            billable = reader.GetBoolean(12)
        });
    }

    return entries;
}

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
