using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 001B - Time Reallocation & Corrections.
///
/// This module is an administrative allocation correction surface for Project Team
/// Coordinators and Super Administrators. It never changes the worker, work date,
/// worked hours, or workflow status of an existing time entry and never requires
/// worker resubmission or Manager/Project Manager approval after reallocation.
/// </summary>
public static partial class ScopedRolePolicyModule
{
    private const string Module001BTimeReallocationContract =
        "module001b-time-reallocation-v1-2026-08-28";

    public static WebApplication MapModule001BTimeReallocationEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/runtime/timesheet/steward/001b/reallocation/capabilities",
            (Func<HttpContext, IResult>)Module001BTimeReallocationCapabilities);
        app.MapPost(
            "/api/runtime/timesheet/steward/001b/reallocation/entries/{timeEntryId:guid}/move",
            (Func<Guid, Module001BTimeReallocationRequest, HttpContext, Task<IResult>>)Module001BReallocateEntryAsync);
        return app;
    }

    private static IResult Module001BTimeReallocationCapabilities(HttpContext context)
        => Results.Ok(new
        {
            module = "001B",
            displayName = "Time Reallocation & Corrections",
            apiContractVersion = Module001BTimeReallocationContract,
            allowedRoles = new[] { "Project Team Coordinator", "Super Administrator" },
            allocationOnly = true,
            workerEditable = false,
            workDateEditable = false,
            workedTimeEditable = false,
            submissionStatePreserved = true,
            unsubmitRequired = false,
            workerResubmissionRequired = false,
            managerApprovalRequired = false,
            projectManagerApprovalRequired = false,
            supportedDestinations = new[]
            {
                "Project Tasks",
                "Requests / Service Requests",
                "Non-Project Time"
            }
        });

    private static async Task<IResult> Module001BReallocateEntryAsync(
        Guid timeEntryId,
        Module001BTimeReallocationRequest request,
        HttpContext context)
    {
        if (request.TargetUserId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "target_user_required",
                message = "Select an eligible user before reallocating time."
            });
        }

        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length < 5)
            return ReasonRequired("reallocate the selected time entry");

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

            // Module 001B intentionally has no Draft/unsubmit gate. Submitted and
            // approved time stays submitted/approved while its allocation changes.
            var originalStatus = original.Status;
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
                        updated_at = NOW()
                    WHERE time_entry_id = @time_entry_id;
                    """, connection, transaction))
                {
                    update.Parameters.AddWithValue("category_id", category.NonProjectTimeCategoryId);
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
                        'EXISTING_ENTRY',
                        @actor_user_id,
                        @actor_user_id
                    )
                    ON CONFLICT (time_entry_id)
                    DO UPDATE SET customer_id = NULL,
                                  project_id = NULL,
                                  task_id = NULL,
                                  assignment_id = NULL,
                                  non_project_time_category_id = EXCLUDED.non_project_time_category_id,
                                  association_source = 'EXISTING_ENTRY',
                                  updated_by_user_id = EXCLUDED.updated_by_user_id;
                    """, connection, transaction))
                {
                    association.Parameters.AddWithValue("time_entry_id", timeEntryId);
                    association.Parameters.AddWithValue("category_id", category.NonProjectTimeCategoryId);
                    association.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                    await association.ExecuteNonQueryAsync(context.RequestAborted);
                }

                destinationProjectId = null;
                destinationTaskId = null;
                destinationEvidence = new
                {
                    destinationType = "Non-Project Time",
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
                        status = "reallocation_destination_required",
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
                        'EXISTING_ENTRY',
                        @actor_user_id,
                        @actor_user_id
                    )
                    ON CONFLICT (time_entry_id)
                    DO UPDATE SET customer_id = NULL,
                                  project_id = EXCLUDED.project_id,
                                  task_id = EXCLUDED.task_id,
                                  assignment_id = EXCLUDED.assignment_id,
                                  non_project_time_category_id = NULL,
                                  association_source = 'EXISTING_ENTRY',
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
            if (revised is null)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Conflict(new
                {
                    status = "reallocation_verification_failed",
                    message = "The reallocated entry could not be reloaded for invariant verification."
                });
            }

            var workerUnchanged = revised.UserId == original.UserId;
            var workDateUnchanged = revised.WorkDate == original.WorkDate;
            var workedTimeUnchanged = revised.Hours == original.Hours;
            var statusUnchanged = string.Equals(
                revised.Status,
                originalStatus,
                StringComparison.OrdinalIgnoreCase);

            if (!workerUnchanged || !workDateUnchanged || !workedTimeUnchanged || !statusUnchanged)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Conflict(new
                {
                    status = "reallocation_invariant_violation",
                    message = "Reallocation was cancelled because a protected time-entry field changed.",
                    invariants = new
                    {
                        workerUnchanged,
                        workDateUnchanged,
                        workedTimeUnchanged,
                        statusUnchanged
                    }
                });
            }

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
                    module = "001B",
                    contractVersion = Module001BTimeReallocationContract,
                    submissionStatePreserved = true,
                    userMustResubmit = false,
                    managerApprovalRequired = false,
                    projectManagerApprovalRequired = false,
                    submissionOnBehalf = false,
                    crossActivityTypeMove = true
                });

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "reallocated",
                module = "001B",
                apiContractVersion = Module001BTimeReallocationContract,
                entry = revised,
                destination = destinationEvidence,
                previousStatus = originalStatus,
                currentStatus = revised.Status,
                submissionStatePreserved = true,
                userMustResubmit = false,
                managerApprovalRequired = false,
                projectManagerApprovalRequired = false,
                invariants = new
                {
                    workerUnchanged,
                    workDateUnchanged,
                    workedTimeUnchanged,
                    statusUnchanged
                }
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }
}

/// <summary>
/// JSON contract owned exclusively by Module 001B. Keeping these values as strings/nullable
/// GUIDs prevents the legacy Module 001 request model from rejecting valid 001B destination
/// payloads during minimal-API model binding before the endpoint can return a structured result.
/// </summary>
internal sealed class Module001BTimeReallocationRequest
{
    public Guid TargetUserId { get; init; }
    public string? DestinationType { get; init; }
    public Guid? AssignmentId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? TaskId { get; init; }
    public Guid? NonProjectTimeCategoryId { get; init; }
    public string? Reason { get; init; }
}
