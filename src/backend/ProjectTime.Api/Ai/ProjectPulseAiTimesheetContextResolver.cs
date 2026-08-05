using Npgsql;
using NpgsqlTypes;
using TimesheetSuggestionRequest = global::ProjectPulseAiTimeEntrySuggestionRequest;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Resolves the selected Timesheet row to its database-owned work context.
/// Client-supplied names and codes are never used as authority: an existing
/// entry, assignment, task, or active non-project category must belong to the
/// current effective user and be valid for the requested work date.
/// </summary>
sealed class ProjectPulseAiTimesheetContextResolver
{
    private readonly ILogger<ProjectPulseAiTimesheetContextResolver> _logger;

    public ProjectPulseAiTimesheetContextResolver(
        ILogger<ProjectPulseAiTimesheetContextResolver> logger)
    {
        _logger = logger;
    }

    internal async Task<ProjectPulseAiTimesheetContextResolution> ResolveAsync(
        Guid effectiveUserId,
        TimesheetSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var config = global::DatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
        {
            return ProjectPulseAiTimesheetContextResolution.Unavailable(
                "database_configuration_missing",
                "Timesheet context is temporarily unavailable.");
        }

        if (request.WorkDate == default)
        {
            return ProjectPulseAiTimesheetContextResolution.Invalid(
                "work_date_required",
                "Work date is required.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            if (request.TimeEntryId is not null)
            {
                return await ResolveExistingEntryAsync(
                    connection,
                    effectiveUserId,
                    request,
                    cancellationToken);
            }

            var rowType = Normalize(request.RowType);
            var hasTaskIdentity = request.AssignmentId is not null
                || request.TaskId is not null
                || request.ProjectId is not null;
            var hasCategoryIdentity = request.NonProjectTimeCategoryId is not null;

            if (hasTaskIdentity && hasCategoryIdentity)
            {
                return ProjectPulseAiTimesheetContextResolution.Invalid(
                    "conflicting_row_identity",
                    "Select exactly one assigned project task or active non-project activity.");
            }

            if (rowType == "nonproject" || hasCategoryIdentity)
            {
                if (hasTaskIdentity)
                {
                    return ProjectPulseAiTimesheetContextResolution.Invalid(
                        "conflicting_row_identity",
                        "A non-project activity cannot include project or task identifiers.");
                }

                return await ResolveNonProjectAsync(
                    connection,
                    request,
                    cancellationToken);
            }

            if (rowType == "projecttask" || hasTaskIdentity)
            {
                if (hasCategoryIdentity)
                {
                    return ProjectPulseAiTimesheetContextResolution.Invalid(
                        "conflicting_row_identity",
                        "An assigned project task cannot include a non-project category identifier.");
                }

                return await ResolveAssignedTaskAsync(
                    connection,
                    effectiveUserId,
                    request,
                    cancellationToken);
            }

            return ProjectPulseAiTimesheetContextResolution.Invalid(
                "work_item_identity_required",
                "Select an assigned project task or active non-project activity before generating a suggestion.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Authoritative Timesheet AI context resolution failed. EffectiveUserId={EffectiveUserId} Diagnostic={Diagnostic}",
                effectiveUserId,
                Diagnostic(exception));
            return ProjectPulseAiTimesheetContextResolution.Unavailable(
                "timesheet_context_resolution_unavailable",
                "Timesheet context is temporarily unavailable.");
        }
    }

    private static async Task<ProjectPulseAiTimesheetContextResolution> ResolveExistingEntryAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        TimesheetSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                te.time_entry_id,
                te.project_id,
                te.task_id,
                te.non_project_time_category_id,
                p.project_code,
                p.project_name,
                COALESCE(c.client_name, '') AS customer_name,
                pt.task_code,
                pt.task_name,
                pt.task_description,
                COALESCE(
                    NULLIF(to_jsonb(pt)->>'work_task_category', ''),
                    NULLIF(to_jsonb(pt)->>'work_type', ''),
                    'project_task'
                ) AS work_task_category,
                COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), '') AS service_request_number,
                npt.category_code,
                npt.category_name,
                assignment.project_assignment_id,
                assignment.assignment_count
            FROM time_entries te
            LEFT JOIN projects p
              ON p.project_id = te.project_id
            LEFT JOIN clients c
              ON c.client_id = p.client_id
            LEFT JOIN project_tasks pt
              ON pt.task_id = te.task_id
             AND pt.project_id = te.project_id
             AND pt.is_active = TRUE
            LEFT JOIN non_project_time_categories npt
              ON npt.non_project_time_category_id = te.non_project_time_category_id
             AND npt.is_active = TRUE
            LEFT JOIN LATERAL (
                SELECT
                    pa.project_assignment_id,
                    COUNT(*) OVER()::integer AS assignment_count
                FROM project_assignments pa
                WHERE pa.user_id = te.user_id
                  AND pa.project_id = te.project_id
                  AND pa.task_id = te.task_id
                  AND pa.effective_start_date <= te.work_date
                  AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= te.work_date)
                  AND (@assignment_id IS NULL OR pa.project_assignment_id = @assignment_id)
                ORDER BY pa.effective_start_date DESC, pa.project_assignment_id
                LIMIT 1
            ) assignment ON TRUE
            WHERE te.time_entry_id = @time_entry_id
              AND te.user_id = @user_id
              AND te.work_date = @work_date
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("time_entry_id", request.TimeEntryId!.Value);
        command.Parameters.AddWithValue("user_id", effectiveUserId);
        command.Parameters.AddWithValue("work_date", request.WorkDate);
        command.Parameters.Add("assignment_id", NpgsqlDbType.Uuid).Value =
            request.AssignmentId is Guid requestedAssignmentId ? requestedAssignmentId : DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return ProjectPulseAiTimesheetContextResolution.Forbidden(
                "time_entry_not_authorized",
                "The selected Timesheet entry is not available for this user and work date.");
        }

        int O(string name) => reader.GetOrdinal(name);

        var projectId = reader.IsDBNull(O("project_id")) ? (Guid?)null : reader.GetGuid(O("project_id"));
        var taskId = reader.IsDBNull(O("task_id")) ? (Guid?)null : reader.GetGuid(O("task_id"));
        var categoryId = reader.IsDBNull(O("non_project_time_category_id"))
            ? (Guid?)null
            : reader.GetGuid(O("non_project_time_category_id"));

        if (projectId is not null && taskId is not null && categoryId is null)
        {
            if (reader.IsDBNull(O("project_code"))
                || reader.IsDBNull(O("project_name"))
                || reader.IsDBNull(O("task_code"))
                || reader.IsDBNull(O("task_name")))
            {
                return ProjectPulseAiTimesheetContextResolution.Forbidden(
                    "project_task_not_available",
                    "The selected project task is no longer available.");
            }

            var assignmentCount = reader.IsDBNull(O("assignment_count"))
                ? 0
                : reader.GetInt32(O("assignment_count"));
            var assignmentId = reader.IsDBNull(O("project_assignment_id"))
                ? (Guid?)null
                : reader.GetGuid(O("project_assignment_id"));
            if (assignmentCount != 1 || assignmentId is null)
            {
                return ProjectPulseAiTimesheetContextResolution.Forbidden(
                    "project_task_assignment_not_authorized",
                    "The selected project task is not actively assigned to this user for the work date.");
            }

            if ((request.ProjectId is not null && request.ProjectId != projectId)
                || (request.TaskId is not null && request.TaskId != taskId)
                || request.NonProjectTimeCategoryId is not null)
            {
                return ProjectPulseAiTimesheetContextResolution.Invalid(
                    "work_item_identity_mismatch",
                    "The selected Timesheet row no longer matches the saved entry.");
            }

            var rowType = IsServiceRequest(
                reader.GetString(O("work_task_category")),
                reader.GetString(O("service_request_number")))
                    ? "service_request"
                    : "projectTask";

            return ProjectPulseAiTimesheetContextResolution.Success(
                request with
                {
                    AssignmentId = assignmentId,
                    ProjectId = projectId,
                    TaskId = taskId,
                    NonProjectTimeCategoryId = null,
                    RowType = rowType,
                    RowLabel = reader.GetString(O("task_name")),
                    CustomerName = reader.GetString(O("customer_name")),
                    ProjectCode = reader.GetString(O("project_code")),
                    ProjectName = reader.GetString(O("project_name")),
                    TaskCode = reader.GetString(O("task_code")),
                    TaskName = reader.GetString(O("task_name")),
                    CategoryCode = null
                },
                "saved_time_entry");
        }

        if (projectId is null && taskId is null && categoryId is not null)
        {
            if ((request.ProjectId is not null || request.TaskId is not null || request.AssignmentId is not null)
                || (request.NonProjectTimeCategoryId is not null
                    && request.NonProjectTimeCategoryId != categoryId))
            {
                return ProjectPulseAiTimesheetContextResolution.Invalid(
                    "work_item_identity_mismatch",
                    "The selected Timesheet row no longer matches the saved entry.");
            }

            if (reader.IsDBNull(O("category_code")) || reader.IsDBNull(O("category_name")))
            {
                return ProjectPulseAiTimesheetContextResolution.Forbidden(
                    "non_project_category_not_authorized",
                    "The selected non-project activity is no longer available.");
            }

            return ProjectPulseAiTimesheetContextResolution.Success(
                request with
                {
                    AssignmentId = null,
                    ProjectId = null,
                    TaskId = null,
                    NonProjectTimeCategoryId = categoryId,
                    RowType = "nonProject",
                    RowLabel = reader.GetString(O("category_name")),
                    CustomerName = null,
                    ProjectCode = null,
                    ProjectName = null,
                    TaskCode = null,
                    TaskName = null,
                    CategoryCode = reader.GetString(O("category_code"))
                },
                "saved_time_entry");
        }

        return ProjectPulseAiTimesheetContextResolution.Forbidden(
            "time_entry_context_invalid",
            "The selected Timesheet entry does not have a valid project-task or non-project association.");
    }

    private static async Task<ProjectPulseAiTimesheetContextResolution> ResolveAssignedTaskAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        TimesheetSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AssignmentId is null && request.TaskId is null)
        {
            return ProjectPulseAiTimesheetContextResolution.Invalid(
                "assigned_task_identity_required",
                "The selected project task is missing its assignment or task identifier.");
        }

        const string sql = """
            SELECT
                pa.project_assignment_id,
                p.project_id,
                p.project_code,
                p.project_name,
                COALESCE(c.client_name, '') AS customer_name,
                pt.task_id,
                pt.task_code,
                pt.task_name,
                pt.task_description,
                COALESCE(
                    NULLIF(to_jsonb(pt)->>'work_task_category', ''),
                    NULLIF(to_jsonb(pt)->>'work_type', ''),
                    'project_task'
                ) AS work_task_category,
                COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), '') AS service_request_number
            FROM project_assignments pa
            JOIN projects p
              ON p.project_id = pa.project_id
            LEFT JOIN clients c
              ON c.client_id = p.client_id
            JOIN project_tasks pt
              ON pt.task_id = pa.task_id
             AND pt.project_id = pa.project_id
            WHERE pa.user_id = @user_id
              AND pa.effective_start_date <= @work_date
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @work_date)
              AND pt.is_active = TRUE
              AND LOWER(COALESCE(p.status, 'active')) NOT IN (
                  'closed', 'complete', 'completed', 'done', 'cancelled', 'canceled', 'archived')
              AND (@assignment_id IS NULL OR pa.project_assignment_id = @assignment_id)
              AND (@task_id IS NULL OR pt.task_id = @task_id)
              AND (@project_id IS NULL OR p.project_id = @project_id)
            ORDER BY pa.effective_start_date DESC, pa.project_assignment_id
            LIMIT 2;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", effectiveUserId);
        command.Parameters.AddWithValue("work_date", request.WorkDate);
        command.Parameters.Add("assignment_id", NpgsqlDbType.Uuid).Value =
            request.AssignmentId is Guid assignmentId ? assignmentId : DBNull.Value;
        command.Parameters.Add("task_id", NpgsqlDbType.Uuid).Value =
            request.TaskId is Guid taskId ? taskId : DBNull.Value;
        command.Parameters.Add("project_id", NpgsqlDbType.Uuid).Value =
            request.ProjectId is Guid projectId ? projectId : DBNull.Value;

        var rows = new List<AssignedTaskContext>(2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AssignedTaskContext(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetGuid(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        if (rows.Count == 0)
        {
            return ProjectPulseAiTimesheetContextResolution.Forbidden(
                "project_task_assignment_not_authorized",
                "The selected project task is not actively assigned to this user for the work date.");
        }

        if (rows.Count > 1)
        {
            return ProjectPulseAiTimesheetContextResolution.Conflict(
                "project_task_assignment_ambiguous",
                "Multiple active assignments match this task and work date. Select the exact assigned task again.");
        }

        var row = rows[0];
        return ProjectPulseAiTimesheetContextResolution.Success(
            request with
            {
                AssignmentId = row.AssignmentId,
                ProjectId = row.ProjectId,
                TaskId = row.TaskId,
                NonProjectTimeCategoryId = null,
                RowType = IsServiceRequest(row.WorkTaskCategory, row.ServiceRequestNumber)
                    ? "service_request"
                    : "projectTask",
                RowLabel = row.TaskName,
                CustomerName = row.CustomerName,
                ProjectCode = row.ProjectCode,
                ProjectName = row.ProjectName,
                TaskCode = row.TaskCode,
                TaskName = row.TaskName,
                CategoryCode = null
            },
            "active_project_assignment");
    }

    private static async Task<ProjectPulseAiTimesheetContextResolution> ResolveNonProjectAsync(
        NpgsqlConnection connection,
        TimesheetSuggestionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.NonProjectTimeCategoryId is null)
        {
            return ProjectPulseAiTimesheetContextResolution.Invalid(
                "non_project_category_identity_required",
                "The selected non-project activity is missing its category identifier.");
        }

        const string sql = """
            SELECT non_project_time_category_id, category_code, category_name
            FROM non_project_time_categories
            WHERE non_project_time_category_id = @category_id
              AND is_active = TRUE
            LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("category_id", request.NonProjectTimeCategoryId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return ProjectPulseAiTimesheetContextResolution.Forbidden(
                "non_project_category_not_authorized",
                "The selected non-project activity is no longer available.");
        }

        return ProjectPulseAiTimesheetContextResolution.Success(
            request with
            {
                AssignmentId = null,
                ProjectId = null,
                TaskId = null,
                NonProjectTimeCategoryId = reader.GetGuid(0),
                RowType = "nonProject",
                RowLabel = reader.GetString(2),
                CustomerName = null,
                ProjectCode = null,
                ProjectName = null,
                TaskCode = null,
                TaskName = null,
                CategoryCode = reader.GetString(1)
            },
            "active_non_project_category");
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static bool IsServiceRequest(string? workTaskCategory, string? serviceRequestNumber) =>
        string.Equals(
            workTaskCategory?.Trim(),
            "service_request_task",
            StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(serviceRequestNumber);

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        _ => "timesheet_context_resolution_failure"
    };

    private sealed record AssignedTaskContext(
        Guid AssignmentId,
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        string CustomerName,
        Guid TaskId,
        string TaskCode,
        string TaskName,
        string? TaskDescription,
        string WorkTaskCategory,
        string ServiceRequestNumber);
}

sealed record ProjectPulseAiTimesheetContextResolution(
    bool Succeeded,
    int StatusCode,
    string Status,
    string Message,
    TimesheetSuggestionRequest? Request,
    string? ContextSource)
{
    public static ProjectPulseAiTimesheetContextResolution Success(
        TimesheetSuggestionRequest request,
        string contextSource) =>
        new(true, StatusCodes.Status200OK, "timesheet_context_resolved", string.Empty, request, contextSource);

    public static ProjectPulseAiTimesheetContextResolution Invalid(string status, string message) =>
        new(false, StatusCodes.Status400BadRequest, status, message, null, null);

    public static ProjectPulseAiTimesheetContextResolution Forbidden(string status, string message) =>
        new(false, StatusCodes.Status403Forbidden, status, message, null, null);

    public static ProjectPulseAiTimesheetContextResolution Conflict(string status, string message) =>
        new(false, StatusCodes.Status409Conflict, status, message, null, null);

    public static ProjectPulseAiTimesheetContextResolution Unavailable(string status, string message) =>
        new(false, StatusCodes.Status503ServiceUnavailable, status, message, null, null);
}
