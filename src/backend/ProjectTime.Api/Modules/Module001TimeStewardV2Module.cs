using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 001 Project Team Coordinator / Super Administrator runtime contract.
///
/// The V2 workspace keeps eligible-user reads role-filtered and exposes three
/// destination groups for governed corrections: project tasks, requests/service
/// requests, and non-project time. Moving to an unassigned project task creates
/// the required assignment inside the same transaction after authorization.
/// </summary>
public static partial class ScopedRolePolicyModule
{
    private const string Module001TimeStewardV2Contract =
        "module001-time-steward-v2-2026-07-28";

    public sealed record Module001PtcMoveV2Request(
        Guid TargetUserId,
        string? DestinationType,
        Guid? AssignmentId,
        Guid? ProjectId,
        Guid? TaskId,
        Guid? NonProjectTimeCategoryId,
        string? Reason);

    private sealed record Module001PtcEntryV2Snapshot(
        Guid TimeEntryId,
        Guid TimesheetId,
        Guid UserId,
        DateOnly WorkDate,
        decimal Hours,
        string Description,
        bool Billable,
        string Status,
        Guid? ProjectId,
        string ProjectCode,
        string ProjectName,
        Guid? TaskId,
        string TaskCode,
        string TaskName,
        Guid? NonProjectTimeCategoryId,
        string NonProjectCategoryCode,
        string NonProjectCategoryName,
        string TimeType,
        DateTimeOffset UpdatedAt);

    private sealed record Module001PtcProjectTargetV2(
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        Guid TaskId,
        string TaskCode,
        string TaskName,
        bool Billable,
        string WorkTaskCategory,
        string ServiceRequestNumber);

    private sealed record Module001PtcCategoryTargetV2(
        Guid NonProjectTimeCategoryId,
        string CategoryCode,
        string CategoryName);

    public static WebApplication MapModule001TimeStewardV2Endpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/runtime/timesheet/steward/v2/users",
            (Func<HttpContext, Task<IResult>>)RuntimePtcUsersAsync);
        app.MapGet(
            "/api/runtime/timesheet/steward/v2/users/{targetUserId:guid}/workspace",
            (Func<Guid, HttpContext, Task<IResult>>)Module001PtcWorkspaceV2Async);
        app.MapPost(
            "/api/runtime/timesheet/steward/v2/entries/{timeEntryId:guid}/move",
            (Func<Guid, Module001PtcMoveV2Request, HttpContext, Task<IResult>>)Module001PtcMoveEntryV2Async);
        return app;
    }

    private static async Task<IResult> Module001PtcWorkspaceV2Async(
        Guid targetUserId,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;

        var access = await RequirePtcTimeStewardAccessAsync(
            context,
            connection,
            "TIME_VIEW_ON_BEHALF",
            targetUserId,
            null,
            false);
        if (access.Error is not null) return access.Error;

        if (!await RuntimePtcManagedUserExistsAsync(connection, targetUserId))
        {
            return Results.NotFound(new
            {
                status = "ptc_managed_user_not_found",
                message = "Select an active Engineer, Engineering Lead, Project Management, or Project Management Lead user."
            });
        }

        var weekStart = PtcRequestedWeek(context);
        var weekEnd = weekStart.AddDays(6);
        object? user = null;
        object? timesheet = null;
        var entries = new List<object>();
        var assignments = new List<object>();
        var moveTargets = new List<object>();
        var nonProjectCategories = new List<object>();
        var availableProjects = new List<object>();

        await using (var command = new NpgsqlCommand("""
            SELECT u.user_id,
                   u.email,
                   COALESCE(NULLIF(u.display_name, ''), u.email),
                   ARRAY_AGG(DISTINCT UPPER(r.role_code))
            FROM app_users u
            JOIN app_user_role_assignments ura
              ON ura.user_id = u.user_id
             AND ura.is_active = TRUE
            JOIN app_roles r
              ON r.app_role_id = ura.app_role_id
             AND r.is_active = TRUE
            WHERE u.user_id = @user_id
              AND u.is_active = TRUE
              AND UPPER(r.role_code) = ANY(@role_codes)
            GROUP BY u.user_id, u.email, u.display_name;
            """, connection))
        {
            command.Parameters.AddWithValue("user_id", targetUserId);
            command.Parameters.AddWithValue("role_codes", PtcManagedRoleAliases);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            if (await reader.ReadAsync(context.RequestAborted))
            {
                var roleCodes = CanonicalPtcManagedRoles(reader.GetFieldValue<string[]>(3));
                user = new
                {
                    userId = reader.GetGuid(0),
                    email = reader.GetString(1),
                    displayName = reader.GetString(2),
                    roleCodes,
                    roleNames = roleCodes.Select(RoleDisplayName).ToArray()
                };
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT timesheet_id, status, submitted_at, updated_at
            FROM timesheets
            WHERE user_id = @user_id
              AND week_start_date = @week_start;
            """, connection))
        {
            command.Parameters.AddWithValue("user_id", targetUserId);
            command.Parameters.AddWithValue("week_start", weekStart);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            if (await reader.ReadAsync(context.RequestAborted))
            {
                timesheet = new
                {
                    timesheetId = reader.GetGuid(0),
                    status = reader.GetString(1),
                    submittedAt = reader.IsDBNull(2)
                        ? (DateTimeOffset?)null
                        : reader.GetFieldValue<DateTimeOffset>(2),
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(3)
                };
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT te.time_entry_id,
                   te.timesheet_id,
                   te.work_date,
                   te.hours,
                   COALESCE(te.description, ''),
                   te.billable,
                   te.status,
                   te.project_id,
                   COALESCE(p.project_code, ''),
                   COALESCE(p.project_name, ''),
                   te.task_id,
                   COALESCE(pt.task_code, ''),
                   COALESCE(pt.task_name, ''),
                   te.non_project_time_category_id,
                   COALESCE(npc.category_code, ''),
                   COALESCE(npc.category_name, ''),
                   COALESCE(te.time_type, 'normal'),
                   te.updated_at
            FROM time_entries te
            LEFT JOIN projects p ON p.project_id = te.project_id
            LEFT JOIN project_tasks pt ON pt.task_id = te.task_id
            LEFT JOIN non_project_time_categories npc
              ON npc.non_project_time_category_id = te.non_project_time_category_id
            WHERE te.user_id = @user_id
              AND te.work_date BETWEEN @week_start AND @week_end
            ORDER BY te.work_date,
                     COALESCE(p.project_code, ''),
                     COALESCE(pt.task_code, ''),
                     COALESCE(npc.category_name, ''),
                     te.created_at;
            """, connection))
        {
            command.Parameters.AddWithValue("user_id", targetUserId);
            command.Parameters.AddWithValue("week_start", weekStart);
            command.Parameters.AddWithValue("week_end", weekEnd);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                entries.Add(new
                {
                    timeEntryId = reader.GetGuid(0),
                    timesheetId = reader.GetGuid(1),
                    workDate = reader.GetFieldValue<DateOnly>(2),
                    hours = reader.GetDecimal(3),
                    description = reader.GetString(4),
                    billable = reader.GetBoolean(5),
                    status = reader.GetString(6),
                    projectId = reader.IsDBNull(7) ? (Guid?)null : reader.GetGuid(7),
                    projectCode = reader.GetString(8),
                    projectName = reader.GetString(9),
                    taskId = reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10),
                    taskCode = reader.GetString(11),
                    taskName = reader.GetString(12),
                    nonProjectTimeCategoryId = reader.IsDBNull(13) ? (Guid?)null : reader.GetGuid(13),
                    nonProjectCategoryCode = reader.GetString(14),
                    nonProjectCategoryName = reader.GetString(15),
                    timeType = reader.GetString(16),
                    entryGroup = reader.IsDBNull(13)
                        ? Module001PtcTaskGroup(reader.GetString(12), reader.GetString(11), string.Empty, string.Empty)
                        : "Non-Project Time",
                    updatedAt = reader.GetFieldValue<DateTimeOffset>(17)
                });
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT p.project_id,
                   p.project_code,
                   p.project_name,
                   pt.task_id,
                   pt.task_code,
                   pt.task_name,
                   COALESCE(pt.billable, p.billable, TRUE),
                   COALESCE(
                       NULLIF(to_jsonb(pt)->>'work_task_category', ''),
                       NULLIF(to_jsonb(pt)->>'work_type', ''),
                       'project_task'
                   ) AS work_task_category,
                   COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), ''),
                   assigned.project_assignment_id,
                   COALESCE(c.client_name, '')
            FROM projects p
            JOIN project_tasks pt
              ON pt.project_id = p.project_id
             AND pt.is_active = TRUE
            LEFT JOIN clients c ON c.client_id = p.client_id
            LEFT JOIN LATERAL (
                SELECT pa.project_assignment_id
                FROM project_assignments pa
                WHERE pa.user_id = @user_id
                  AND pa.project_id = p.project_id
                  AND pa.task_id = pt.task_id
                  AND pa.effective_start_date <= @week_end
                  AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @week_start)
                ORDER BY pa.effective_start_date DESC
                LIMIT 1
            ) assigned ON TRUE
            WHERE p.status IN ('active', 'on_hold')
            ORDER BY
                CASE
                    WHEN COALESCE(NULLIF(to_jsonb(pt)->>'work_task_category', ''), '') = 'service_request_task'
                      OR COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), '') <> ''
                    THEN 0 ELSE 1
                END,
                p.project_code,
                pt.task_code
            LIMIT 2000;
            """, connection))
        {
            command.Parameters.AddWithValue("user_id", targetUserId);
            command.Parameters.AddWithValue("week_start", weekStart);
            command.Parameters.AddWithValue("week_end", weekEnd);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                var projectId = reader.GetGuid(0);
                var projectCode = reader.GetString(1);
                var projectName = reader.GetString(2);
                var taskId = reader.GetGuid(3);
                var taskCode = reader.GetString(4);
                var taskName = reader.GetString(5);
                var billable = reader.GetBoolean(6);
                var workTaskCategory = reader.GetString(7).Trim().ToLowerInvariant();
                var serviceRequestNumber = reader.GetString(8).Trim();
                var assignmentId = reader.IsDBNull(9) ? (Guid?)null : reader.GetGuid(9);
                var customerName = reader.GetString(10);
                var groupLabel = Module001PtcTaskGroup(
                    taskName,
                    taskCode,
                    workTaskCategory,
                    serviceRequestNumber);
                var selectionParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(serviceRequestNumber)) selectionParts.Add(serviceRequestNumber);
                if (!string.IsNullOrWhiteSpace(customerName)) selectionParts.Add(customerName);
                selectionParts.Add(projectCode);
                selectionParts.Add(taskName);
                var selectionLabel = string.Join(" · ", selectionParts);

                var target = new
                {
                    destinationType = assignmentId.HasValue ? "assignment" : "project_task",
                    selectionValue = assignmentId.HasValue
                        ? $"assignment:{assignmentId.Value:D}"
                        : $"project-task:{projectId:D}:{taskId:D}",
                    groupLabel,
                    assignmentId,
                    requiresAssignment = !assignmentId.HasValue,
                    customerName,
                    projectId,
                    projectCode,
                    projectName,
                    taskId,
                    taskCode,
                    taskName,
                    billable,
                    workTaskCategory,
                    serviceRequestNumber,
                    selectionLabel
                };
                moveTargets.Add(target);
                if (assignmentId.HasValue) assignments.Add(target);
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT non_project_time_category_id,
                   category_code,
                   category_name
            FROM non_project_time_categories
            WHERE is_active = TRUE
            ORDER BY display_order, category_name;
            """, connection))
        {
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                var categoryId = reader.GetGuid(0);
                var categoryCode = reader.GetString(1);
                var categoryName = reader.GetString(2);
                var target = new
                {
                    destinationType = "non_project",
                    selectionValue = $"category:{categoryId:D}",
                    groupLabel = "Non-Project Time",
                    nonProjectTimeCategoryId = categoryId,
                    categoryCode,
                    categoryName,
                    selectionLabel = categoryName,
                    billable = false
                };
                nonProjectCategories.Add(target);
                moveTargets.Add(target);
            }
        }

        await using (var command = new NpgsqlCommand("""
            SELECT project_id, project_code, project_name, status
            FROM projects
            WHERE status IN ('active', 'on_hold')
            ORDER BY project_code, project_name
            LIMIT 1000;
            """, connection))
        {
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                availableProjects.Add(new
                {
                    projectId = reader.GetGuid(0),
                    projectCode = reader.GetString(1),
                    projectName = reader.GetString(2),
                    status = reader.GetString(3)
                });
            }
        }

        return Results.Ok(new
        {
            apiContractVersion = Module001TimeStewardV2Contract,
            eligibleRoleCodes = new[]
            {
                "ENGINEERING",
                "ENGINEERING_LEAD",
                "PROJECT_MANAGEMENT",
                "PROJECT_MANAGEMENT_LEAD"
            },
            user,
            weekStart,
            weekEnd,
            timesheet,
            entries,
            assignments,
            moveTargets,
            nonProjectCategories,
            availableProjects,
            destinationGroups = new[]
            {
                "Requests / Service Requests",
                "Project Tasks",
                "Non-Project Time"
            },
            canCreateAndAssignReplacementTask = true,
            canAssignExistingProjectTaskDuringMove = true,
            canMoveToNonProjectTime = true,
            canSubmitOnBehalf = false,
            requiredCorrectionFlow =
                "UNSUBMIT_OR_REOPEN_THEN_EDIT_MOVE_REMOVE_THEN_USER_RESUBMITS"
        });
    }

    private static string Module001PtcTaskGroup(
        string taskName,
        string taskCode,
        string workTaskCategory,
        string serviceRequestNumber)
    {
        var text = string.Join(' ', new[]
        {
            taskName,
            taskCode,
            workTaskCategory,
            serviceRequestNumber
        }).ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(serviceRequestNumber)
               || workTaskCategory == "service_request_task"
               || text.Contains("service request", StringComparison.Ordinal)
               || text.Contains("ticket", StringComparison.Ordinal)
               || text.Contains("incident", StringComparison.Ordinal)
               || text.Contains("case", StringComparison.Ordinal)
            ? "Requests / Service Requests"
            : "Project Tasks";
    }

    private static async Task<IResult> Module001PtcMoveEntryV2Async(
        Guid timeEntryId,
        Module001PtcMoveV2Request request,
        HttpContext context)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length < 5)
            return ReasonRequired("move the selected time entry to a different activity");

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await RequirePtcTimeStewardTablesAsync(connection);
        if (readiness is not null) return readiness;

        if (!await RuntimePtcManagedUserExistsAsync(connection, request.TargetUserId))
        {
            return Results.NotFound(new
            {
                status = "ptc_managed_user_not_found",
                message = "The selected user is no longer eligible for Project Team Coordinator time stewardship."
            });
        }

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            var original = await Module001LoadPtcEntryV2Async(
                connection,
                transaction,
                timeEntryId,
                request.TargetUserId,
                true,
                context.RequestAborted);
            if (original is null)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new { status = "time_entry_not_found" });
            }

            if (original.Status is not ("draft" or "manager_declined" or "pm_declined"))
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Conflict(new
                {
                    status = "unsubmit_required",
                    message = "Return the selected user's week to draft before moving this entry."
                });
            }

            var requestedType = (request.DestinationType ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            var nonProjectMove = request.NonProjectTimeCategoryId.HasValue
                                 || requestedType is "non_project" or "category";

            ActorContext actor;
            object destinationEvidence;
            Guid? destinationProjectId;
            Guid? destinationTaskId;

            if (nonProjectMove)
            {
                if (!request.NonProjectTimeCategoryId.HasValue)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.BadRequest(new
                    {
                        status = "non_project_category_required",
                        message = "Select an active Non-Project Time category."
                    });
                }

                var category = await Module001LoadPtcCategoryV2Async(
                    connection,
                    transaction,
                    request.NonProjectTimeCategoryId.Value,
                    context.RequestAborted);
                if (category is null)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.NotFound(new
                    {
                        status = "non_project_category_not_found",
                        message = "The selected Non-Project Time category is unavailable."
                    });
                }

                var access = await RequirePtcTimeStewardAccessAsync(
                    context,
                    connection,
                    "TIME_REASSIGN",
                    request.TargetUserId,
                    null,
                    true);
                if (access.Error is not null)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return access.Error;
                }
                actor = access.Actor!;

                await using (var update = new NpgsqlCommand("""
                    UPDATE time_entries
                    SET project_id = NULL,
                        task_id = NULL,
                        non_project_time_category_id = @category_id,
                        billable = FALSE,
                        status = 'draft',
                        updated_at = NOW()
                    WHERE time_entry_id = @time_entry_id;
                    """, connection, transaction))
                {
                    update.Parameters.AddWithValue(
                        "category_id",
                        category.NonProjectTimeCategoryId);
                    update.Parameters.AddWithValue("time_entry_id", timeEntryId);
                    await update.ExecuteNonQueryAsync(context.RequestAborted);
                }

                await using (var association = new NpgsqlCommand("""
                    INSERT INTO module001_timesheet_entry_associations (
                        time_entry_id,
                        non_project_time_category_id,
                        association_source,
                        created_by_user_id,
                        updated_by_user_id
                    ) VALUES (
                        @time_entry_id,
                        @category_id,
                        'PTC_TIME_STEWARD',
                        @actor_user_id,
                        @actor_user_id
                    )
                    ON CONFLICT (time_entry_id)
                    DO UPDATE SET customer_id = NULL,
                                  project_id = NULL,
                                  task_id = NULL,
                                  assignment_id = NULL,
                                  non_project_time_category_id = EXCLUDED.non_project_time_category_id,
                                  association_source = 'PTC_TIME_STEWARD',
                                  updated_by_user_id = EXCLUDED.updated_by_user_id;
                    """, connection, transaction))
                {
                    association.Parameters.AddWithValue("time_entry_id", timeEntryId);
                    association.Parameters.AddWithValue(
                        "category_id",
                        category.NonProjectTimeCategoryId);
                    association.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                    await association.ExecuteNonQueryAsync(context.RequestAborted);
                }

                destinationProjectId = null;
                destinationTaskId = null;
                destinationEvidence = new
                {
                    destinationType = "non_project",
                    category.NonProjectTimeCategoryId,
                    category.CategoryCode,
                    category.CategoryName,
                    assignmentCreated = false
                };
            }
            else
            {
                PtcAssignmentTarget? assignmentTarget = null;
                Module001PtcProjectTargetV2? projectTarget = null;
                Guid? assignmentId = request.AssignmentId;

                if (assignmentId.HasValue)
                {
                    assignmentTarget = await LoadPtcAssignmentAsync(
                        connection,
                        transaction,
                        assignmentId.Value,
                        request.TargetUserId,
                        original.WorkDate);
                    if (assignmentTarget is null)
                    {
                        await transaction.RollbackAsync(context.RequestAborted);
                        return Results.NotFound(new
                        {
                            status = "assignment_not_found",
                            message = "The selected assignment is no longer active for this user and date."
                        });
                    }
                    projectTarget = new Module001PtcProjectTargetV2(
                        assignmentTarget.ProjectId,
                        assignmentTarget.ProjectCode,
                        assignmentTarget.ProjectName,
                        assignmentTarget.TaskId,
                        assignmentTarget.TaskCode,
                        assignmentTarget.TaskName,
                        assignmentTarget.Billable,
                        string.Empty,
                        string.Empty);
                }
                else if (request.ProjectId.HasValue && request.TaskId.HasValue)
                {
                    projectTarget = await Module001LoadPtcProjectTargetV2Async(
                        connection,
                        transaction,
                        request.ProjectId.Value,
                        request.TaskId.Value,
                        context.RequestAborted);
                    if (projectTarget is null)
                    {
                        await transaction.RollbackAsync(context.RequestAborted);
                        return Results.NotFound(new
                        {
                            status = "project_task_not_found",
                            message = "The selected destination project task is not active."
                        });
                    }
                }
                else
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.BadRequest(new
                    {
                        status = "move_destination_required",
                        message = "Select a Project Task, Request / Service Request, or Non-Project Time destination."
                    });
                }

                var reassignAccess = await RequirePtcTimeStewardAccessAsync(
                    context,
                    connection,
                    "TIME_REASSIGN",
                    request.TargetUserId,
                    projectTarget.ProjectId,
                    true);
                if (reassignAccess.Error is not null)
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return reassignAccess.Error;
                }
                actor = reassignAccess.Actor!;

                var assignmentCreated = false;
                if (!assignmentId.HasValue)
                {
                    var assignAccess = await RequirePtcTimeStewardAccessAsync(
                        context,
                        connection,
                        "TIME_TASK_ASSIGN",
                        request.TargetUserId,
                        projectTarget.ProjectId,
                        true);
                    if (assignAccess.Error is not null)
                    {
                        await transaction.RollbackAsync(context.RequestAborted);
                        return assignAccess.Error;
                    }

                    (assignmentId, assignmentCreated) = await Module001EnsurePtcAssignmentV2Async(
                        connection,
                        transaction,
                        request.TargetUserId,
                        projectTarget,
                        original.WorkDate,
                        actor.ActualUserId,
                        context.RequestAborted);
                }

                await using (var update = new NpgsqlCommand("""
                    UPDATE time_entries
                    SET project_id = @project_id,
                        task_id = @task_id,
                        non_project_time_category_id = NULL,
                        billable = @billable,
                        status = 'draft',
                        updated_at = NOW()
                    WHERE time_entry_id = @time_entry_id;
                    """, connection, transaction))
                {
                    update.Parameters.AddWithValue("project_id", projectTarget.ProjectId);
                    update.Parameters.AddWithValue("task_id", projectTarget.TaskId);
                    update.Parameters.AddWithValue("billable", projectTarget.Billable);
                    update.Parameters.AddWithValue("time_entry_id", timeEntryId);
                    await update.ExecuteNonQueryAsync(context.RequestAborted);
                }

                await using (var association = new NpgsqlCommand("""
                    INSERT INTO module001_timesheet_entry_associations (
                        time_entry_id,
                        project_id,
                        task_id,
                        assignment_id,
                        association_source,
                        created_by_user_id,
                        updated_by_user_id
                    ) VALUES (
                        @time_entry_id,
                        @project_id,
                        @task_id,
                        @assignment_id,
                        'PTC_TIME_STEWARD',
                        @actor_user_id,
                        @actor_user_id
                    )
                    ON CONFLICT (time_entry_id)
                    DO UPDATE SET customer_id = NULL,
                                  project_id = EXCLUDED.project_id,
                                  task_id = EXCLUDED.task_id,
                                  assignment_id = EXCLUDED.assignment_id,
                                  non_project_time_category_id = NULL,
                                  association_source = 'PTC_TIME_STEWARD',
                                  updated_by_user_id = EXCLUDED.updated_by_user_id;
                    """, connection, transaction))
                {
                    association.Parameters.AddWithValue("time_entry_id", timeEntryId);
                    association.Parameters.AddWithValue("project_id", projectTarget.ProjectId);
                    association.Parameters.AddWithValue("task_id", projectTarget.TaskId);
                    association.Parameters.AddWithValue("assignment_id", assignmentId!.Value);
                    association.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                    await association.ExecuteNonQueryAsync(context.RequestAborted);
                }

                destinationProjectId = projectTarget.ProjectId;
                destinationTaskId = projectTarget.TaskId;
                destinationEvidence = new
                {
                    destinationType = Module001PtcTaskGroup(
                        projectTarget.TaskName,
                        projectTarget.TaskCode,
                        projectTarget.WorkTaskCategory,
                        projectTarget.ServiceRequestNumber),
                    assignmentId,
                    assignmentCreated,
                    projectTarget.ProjectId,
                    projectTarget.ProjectCode,
                    projectTarget.ProjectName,
                    projectTarget.TaskId,
                    projectTarget.TaskCode,
                    projectTarget.TaskName,
                    projectTarget.ServiceRequestNumber
                };
            }

            var revised = await Module001LoadPtcEntryV2Async(
                connection,
                transaction,
                timeEntryId,
                request.TargetUserId,
                false,
                context.RequestAborted);
            await InsertPtcTimeAuditAsync(
                connection,
                transaction,
                "TIME_REASSIGN",
                actor,
                request.TargetUserId,
                original.TimesheetId,
                timeEntryId,
                destinationProjectId,
                destinationTaskId,
                reason,
                original,
                new { revised, destination = destinationEvidence },
                new
                {
                    contractVersion = Module001TimeStewardV2Contract,
                    userMustResubmit = true,
                    submissionOnBehalf = false,
                    crossActivityTypeMove = true
                });

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "moved",
                apiContractVersion = Module001TimeStewardV2Contract,
                entry = revised,
                destination = destinationEvidence,
                userMustResubmit = true,
                submissionOnBehalf = false
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<Module001PtcEntryV2Snapshot?> Module001LoadPtcEntryV2Async(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid timeEntryId,
        Guid targetUserId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT te.time_entry_id,
                   te.timesheet_id,
                   te.user_id,
                   te.work_date,
                   te.hours,
                   COALESCE(te.description, ''),
                   te.billable,
                   te.status,
                   te.project_id,
                   COALESCE(p.project_code, ''),
                   COALESCE(p.project_name, ''),
                   te.task_id,
                   COALESCE(pt.task_code, ''),
                   COALESCE(pt.task_name, ''),
                   te.non_project_time_category_id,
                   COALESCE(npc.category_code, ''),
                   COALESCE(npc.category_name, ''),
                   COALESCE(te.time_type, 'normal'),
                   te.updated_at
            FROM time_entries te
            LEFT JOIN projects p ON p.project_id = te.project_id
            LEFT JOIN project_tasks pt ON pt.task_id = te.task_id
            LEFT JOIN non_project_time_categories npc
              ON npc.non_project_time_category_id = te.non_project_time_category_id
            WHERE te.time_entry_id = @time_entry_id
              AND te.user_id = @user_id
            """;
        if (forUpdate) sql += " FOR UPDATE OF te";
        sql += ";";

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("time_entry_id", timeEntryId);
        command.Parameters.AddWithValue("user_id", targetUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new Module001PtcEntryV2Snapshot(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetFieldValue<DateOnly>(3),
            reader.GetDecimal(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? (Guid?)null : reader.GetGuid(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? (Guid?)null : reader.GetGuid(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.IsDBNull(14) ? (Guid?)null : reader.GetGuid(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetFieldValue<DateTimeOffset>(18));
    }

    private static async Task<Module001PtcProjectTargetV2?> Module001LoadPtcProjectTargetV2Async(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT p.project_id,
                   p.project_code,
                   p.project_name,
                   pt.task_id,
                   pt.task_code,
                   pt.task_name,
                   COALESCE(pt.billable, p.billable, TRUE),
                   COALESCE(
                       NULLIF(to_jsonb(pt)->>'work_task_category', ''),
                       NULLIF(to_jsonb(pt)->>'work_type', ''),
                       'project_task'
                   ),
                   COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), '')
            FROM projects p
            JOIN project_tasks pt
              ON pt.project_id = p.project_id
            WHERE p.project_id = @project_id
              AND pt.task_id = @task_id
              AND p.status IN ('active', 'on_hold')
              AND pt.is_active = TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("task_id", taskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new Module001PtcProjectTargetV2(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetString(7),
            reader.GetString(8));
    }

    private static async Task<Module001PtcCategoryTargetV2?> Module001LoadPtcCategoryV2Async(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT non_project_time_category_id,
                   category_code,
                   category_name
            FROM non_project_time_categories
            WHERE non_project_time_category_id = @category_id
              AND is_active = TRUE;
            """, connection, transaction);
        command.Parameters.AddWithValue("category_id", categoryId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new Module001PtcCategoryTargetV2(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private static async Task<(Guid AssignmentId, bool Created)> Module001EnsurePtcAssignmentV2Async(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid targetUserId,
        Module001PtcProjectTargetV2 target,
        DateOnly workDate,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using (var existing = new NpgsqlCommand("""
            SELECT project_assignment_id
            FROM project_assignments
            WHERE user_id = @user_id
              AND project_id = @project_id
              AND task_id = @task_id
              AND effective_start_date <= @work_date
              AND (effective_end_date IS NULL OR effective_end_date >= @work_date)
            ORDER BY effective_start_date DESC
            LIMIT 1
            FOR UPDATE;
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue("user_id", targetUserId);
            existing.Parameters.AddWithValue("project_id", target.ProjectId);
            existing.Parameters.AddWithValue("task_id", target.TaskId);
            existing.Parameters.AddWithValue("work_date", workDate);
            var value = await existing.ExecuteScalarAsync(cancellationToken);
            if (value is Guid assignmentId) return (assignmentId, false);
        }

        await using var insert = new NpgsqlCommand("""
            INSERT INTO project_assignments (
                project_id,
                task_id,
                user_id,
                assigned_by_user_id,
                effective_start_date
            ) VALUES (
                @project_id,
                @task_id,
                @user_id,
                @actor_user_id,
                @effective_start_date
            )
            RETURNING project_assignment_id;
            """, connection, transaction);
        insert.Parameters.AddWithValue("project_id", target.ProjectId);
        insert.Parameters.AddWithValue("task_id", target.TaskId);
        insert.Parameters.AddWithValue("user_id", targetUserId);
        insert.Parameters.AddWithValue("actor_user_id", actorUserId);
        insert.Parameters.AddWithValue("effective_start_date", workDate);
        var created = (Guid)(await insert.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The destination assignment was not created."));
        return (created, true);
    }
}
