using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static readonly HashSet<string> Module001AEngineerRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ENGINEERING",
        "ENGINEER",
        "SYSTEMS_ENGINEER",
        "NETWORK_ENGINEER",
        "ENTERPRISE_NETWORK_ENGINEER"
    };

    private static readonly HashSet<string> Module001ATerminalProjectStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "closed", "complete", "completed", "done", "cancelled", "canceled", "archived"
    };

    public static WebApplication MapModule001AEngineerTaskCloseoutEndpoints(this WebApplication app)
    {
        app.MapGet("/api/engineer-task-closeout/overview", Module001AOverviewAsync);
        app.MapPost("/api/engineer-task-closeout/assignments/{assignmentId:guid}/close", Module001ACloseAsync);
        app.MapPost("/api/engineer-task-closeout/assignments/{assignmentId:guid}/reopen", Module001AReopenAsync);
        return app;
    }

    private static async Task<IResult> Module001AOverviewAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readiness = await Module001ARequireSchemaAsync(connection, context.RequestAborted);
        if (readiness is not null) return readiness;
        var access = await Module001ARequireAccessAsync(connection, context, mutation: false);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;

        var rows = new List<object>();
        await using (var command = new NpgsqlCommand("""
            SELECT
                pa.project_assignment_id,
                pa.project_id,
                pa.task_id,
                pa.user_id,
                pa.effective_start_date,
                pa.effective_end_date,
                COALESCE(pa.assigned_hours, 0)::numeric AS assigned_hours,
                COALESCE(NULLIF(pa.module001a_closeout_status, ''), 'active') AS assignment_closeout_status,
                p.project_code,
                p.project_name,
                COALESCE(p.work_type, '') AS work_type,
                COALESCE(p.status, '') AS project_status,
                COALESCE(client.client_name, 'No customer assigned') AS customer_name,
                pt.task_code,
                pt.task_name,
                COALESCE(pt.task_description, '') AS task_description,
                pt.is_active AS task_active,
                COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), '') AS service_request_number,
                COALESCE(used.total_hours, 0)::numeric AS used_hours,
                engineer.display_name AS engineer_name,
                engineer.email AS engineer_email,
                coordinator.user_id AS coordinator_user_id,
                COALESCE(coordinator.display_name, coordinator.email, '') AS coordinator_name,
                COALESCE(coordinator.email, '') AS coordinator_email,
                closeout.module001a_closeout_id,
                closeout.closeout_status,
                COALESCE(closeout.completion_summary, '') AS completion_summary,
                closeout.engineer_closed_at,
                closeout.reopened_at,
                COALESCE(closeout.reopen_reason, '') AS reopen_reason,
                closeout.ptc_final_closed_at,
                closeout.revision_number,
                closeout.latest_notification_dispatch_id,
                dispatch.delivery_status AS notification_status
            FROM project_assignments pa
            JOIN projects p ON p.project_id = pa.project_id
            JOIN project_tasks pt
              ON pt.task_id = pa.task_id
             AND pt.project_id = pa.project_id
            JOIN app_users engineer ON engineer.user_id = pa.user_id
            LEFT JOIN clients client ON client.client_id = p.client_id
            LEFT JOIN app_users coordinator ON coordinator.user_id = p.project_coordinator_user_id
            LEFT JOIN module001a_engineer_task_closeouts closeout
              ON closeout.assignment_id = pa.project_assignment_id
            LEFT JOIN project_notification_dispatches dispatch
              ON dispatch.project_notification_dispatch_id = closeout.latest_notification_dispatch_id
            LEFT JOIN LATERAL (
                SELECT SUM(entry.hours)::numeric AS total_hours
                FROM time_entries entry
                WHERE entry.user_id = pa.user_id
                  AND entry.project_id = pa.project_id
                  AND entry.task_id = pa.task_id
                  AND entry.status NOT IN ('manager_declined', 'pm_declined')
            ) used ON TRUE
            WHERE pa.user_id = @engineer_user_id
              AND (
                    regexp_replace(lower(COALESCE(p.work_type, '')), '[^a-z0-9]+', '', 'g') IN (
                        'servicerequest', 'sr', 'presales', 'presale', 'pres',
                        'internal', 'internalproject', 'internaltask'
                    )
                 OR p.project_code ~* '^(SR|PRES|INT)-'
                 OR lower(COALESCE(NULLIF(to_jsonb(pt)->>'work_task_category', ''), '')) = 'service_request_task'
                 OR NULLIF(to_jsonb(pt)->>'service_request_number', '') IS NOT NULL
              )
              AND (
                    closeout.module001a_closeout_id IS NOT NULL
                 OR (
                        pa.effective_start_date <= CURRENT_DATE
                    AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= CURRENT_DATE)
                    AND lower(COALESCE(p.status, 'active')) NOT IN ('closed', 'complete', 'completed', 'done', 'cancelled', 'canceled', 'archived')
                    AND pt.is_active = TRUE
                 )
              )
            ORDER BY
                CASE COALESCE(closeout.closeout_status, 'reopened')
                    WHEN 'engineer_closed' THEN 2
                    WHEN 'ptc_final_closed' THEN 3
                    ELSE 1
                END,
                p.project_code,
                pt.task_code;
            """, connection))
        {
            command.Parameters.AddWithValue("engineer_user_id", actor.EffectiveUserId);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                int O(string name) => reader.GetOrdinal(name);
                var projectStatus = reader.GetString(O("project_status"));
                var taskActive = reader.GetBoolean(O("task_active"));
                var closeoutStatus = reader.IsDBNull(O("closeout_status"))
                    ? "active"
                    : reader.GetString(O("closeout_status"));
                var ptcFinalClosedAt = reader.IsDBNull(O("ptc_final_closed_at"))
                    ? (DateTimeOffset?)null
                    : reader.GetFieldValue<DateTimeOffset>(O("ptc_final_closed_at"));
                var historical = closeoutStatus is "engineer_closed" or "ptc_final_closed";
                var canReopen = closeoutStatus == "engineer_closed"
                    && ptcFinalClosedAt is null
                    && taskActive
                    && !Module001ATerminalProjectStatuses.Contains(projectStatus);
                var projectCode = reader.GetString(O("project_code"));
                var workType = reader.GetString(O("work_type"));
                var requestType = Module001ARequestType(projectCode, workType);

                rows.Add(new
                {
                    assignmentId = reader.GetGuid(O("project_assignment_id")),
                    projectId = reader.GetGuid(O("project_id")),
                    taskId = reader.GetGuid(O("task_id")),
                    engineerUserId = reader.GetGuid(O("user_id")),
                    effectiveStartDate = reader.GetFieldValue<DateOnly>(O("effective_start_date")),
                    effectiveEndDate = reader.IsDBNull(O("effective_end_date")) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(O("effective_end_date")),
                    assignedHours = reader.GetDecimal(O("assigned_hours")),
                    usedHours = reader.GetDecimal(O("used_hours")),
                    remainingHours = Math.Max(reader.GetDecimal(O("assigned_hours")) - reader.GetDecimal(O("used_hours")), 0),
                    projectCode,
                    projectName = reader.GetString(O("project_name")),
                    projectStatus,
                    workType,
                    requestType,
                    customerName = reader.GetString(O("customer_name")),
                    taskCode = reader.GetString(O("task_code")),
                    taskName = reader.GetString(O("task_name")),
                    taskDescription = reader.GetString(O("task_description")),
                    serviceRequestNumber = reader.GetString(O("service_request_number")),
                    engineerName = reader.GetString(O("engineer_name")),
                    engineerEmail = reader.GetString(O("engineer_email")),
                    projectTeamCoordinatorName = reader.GetString(O("coordinator_name")),
                    projectTeamCoordinatorEmail = reader.GetString(O("coordinator_email")),
                    closeoutId = reader.IsDBNull(O("module001a_closeout_id")) ? (Guid?)null : reader.GetGuid(O("module001a_closeout_id")),
                    closeoutStatus,
                    completionSummary = reader.GetString(O("completion_summary")),
                    engineerClosedAt = reader.IsDBNull(O("engineer_closed_at")) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(O("engineer_closed_at")),
                    reopenedAt = reader.IsDBNull(O("reopened_at")) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(O("reopened_at")),
                    reopenReason = reader.GetString(O("reopen_reason")),
                    ptcFinalClosedAt,
                    revisionNumber = reader.IsDBNull(O("revision_number")) ? 0 : reader.GetInt32(O("revision_number")),
                    notificationDispatchId = reader.IsDBNull(O("latest_notification_dispatch_id")) ? (Guid?)null : reader.GetGuid(O("latest_notification_dispatch_id")),
                    notificationStatus = reader.IsDBNull(O("notification_status")) ? "not_queued" : reader.GetString(O("notification_status")),
                    billingLocked = historical,
                    canClose = !historical && taskActive && !Module001ATerminalProjectStatuses.Contains(projectStatus),
                    canReopen,
                    finalCloseAuthority = "Module 055C · Project Team Coordinator"
                });
            }
        }

        var events = new List<object>();
        await using (var command = new NpgsqlCommand("""
            SELECT event.module001a_event_id,
                   event.assignment_id,
                   event.event_type,
                   event.event_reason,
                   event.actor_user_id,
                   COALESCE(actor.display_name, actor.email, 'System lifecycle authority') AS actor_name,
                   event.notification_dispatch_id,
                   dispatch.delivery_status,
                   event.occurred_at
            FROM module001a_engineer_task_closeout_events event
            LEFT JOIN app_users actor ON actor.user_id = event.actor_user_id
            LEFT JOIN project_notification_dispatches dispatch
              ON dispatch.project_notification_dispatch_id = event.notification_dispatch_id
            WHERE event.engineer_user_id = @engineer_user_id
            ORDER BY event.occurred_at DESC
            LIMIT 250;
            """, connection))
        {
            command.Parameters.AddWithValue("engineer_user_id", actor.EffectiveUserId);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                events.Add(new
                {
                    eventId = reader.GetGuid(0),
                    assignmentId = reader.GetGuid(1),
                    eventType = reader.GetString(2),
                    reason = reader.GetString(3),
                    actorUserId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                    actorName = reader.GetString(5),
                    notificationDispatchId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
                    notificationStatus = reader.IsDBNull(7) ? "not_queued" : reader.GetString(7),
                    occurredAt = reader.GetFieldValue<DateTimeOffset>(8)
                });
            }
        }

        var active = rows.Where(row => !Module001AReadBool(row, "billingLocked")).ToArray();
        var history = rows.Where(row => Module001AReadBool(row, "billingLocked")).ToArray();
        return Results.Ok(new
        {
            module = "001A",
            status = "engineer_task_closeout_loaded",
            generatedAt = DateTimeOffset.UtcNow,
            access = new
            {
                actualUserId = actor.ActualUserId,
                effectiveUserId = actor.EffectiveUserId,
                actor.DisplayName,
                actor.Email,
                actor.IsViewAs,
                scope = "own_assignments_only",
                mutationAllowed = !actor.IsViewAs
            },
            summary = new
            {
                activeCount = active.Length,
                historyCount = history.Length,
                reopenEligibleCount = rows.Count(row => Module001AReadBool(row, "canReopen")),
                billingLockedCount = history.Length
            },
            active,
            history,
            events,
            workflow = new
            {
                eligibleWorkTypes = new[] { "Service Request", "Pre-Sales", "Internal" },
                closeEffect = "The assignment is removed from Module 001 and new or increased time is blocked.",
                notificationRoute = "Module 065 sends to the Project Team Coordinator and CCs the Engineer.",
                reopenRule = "The Engineer must enter a reason and may reopen only before Module 055C final closure.",
                finalCloseAuthority = "Module 055C · Project Team Coordinator"
            }
        });
    }

    private static bool Module001AReadBool(object source, string name)
    {
        var property = source.GetType().GetProperty(name);
        return property?.GetValue(source) is true;
    }

    private static async Task<IResult> Module001ACloseAsync(
        Guid assignmentId,
        Module001ACloseRequest request,
        HttpContext context)
    {
        var summary = Module001AClean(request.CompletionSummary, 2000);
        if (summary.Length < 5)
        {
            return Results.BadRequest(new
            {
                module = "001A",
                status = "completion_summary_required",
                message = "Enter a completion summary of at least 5 characters for the Project Team Coordinator."
            });
        }

        return await Module001ATransitionAsync(assignmentId, "engineer_closed", summary, context);
    }

    private static async Task<IResult> Module001AReopenAsync(
        Guid assignmentId,
        Module001AReopenRequest request,
        HttpContext context)
    {
        var reason = Module001AClean(request.Reason, 2000);
        if (reason.Length < 10)
        {
            return Results.BadRequest(new
            {
                module = "001A",
                status = "reopen_reason_required",
                message = "Enter a specific reopen reason of at least 10 characters. It will be included in the notification email."
            });
        }

        return await Module001ATransitionAsync(assignmentId, "engineer_reopened", reason, context);
    }

    private static async Task<IResult> Module001ATransitionAsync(
        Guid assignmentId,
        string eventType,
        string reason,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(context.RequestAborted);
        var readinessResult = await Module001ARequireSchemaAsync(connection, context.RequestAborted);
        if (readinessResult is not null) return readinessResult;
        var access = await Module001ARequireAccessAsync(connection, context, mutation: true);
        if (access.Error is not null) return access.Error;
        var actor = access.Actor!;
        var mailReadiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(context, context.RequestAborted);

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            var target = await Module001ALoadTargetAsync(
                connection,
                transaction,
                assignmentId,
                actor.EffectiveUserId,
                context.RequestAborted);
            if (target is null)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new
                {
                    module = "001A",
                    status = "eligible_assignment_not_found",
                    message = "The eligible Service Request, Pre-Sales, or Internal task is not assigned to the authenticated Engineer."
                });
            }

            var existing = await Module001ALoadCloseoutForUpdateAsync(
                connection,
                transaction,
                assignmentId,
                context.RequestAborted);
            if (eventType == "engineer_closed")
            {
                if (!target.TaskActive || Module001ATerminalProjectStatuses.Contains(target.ProjectStatus))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Conflict(new
                    {
                        module = "001A",
                        status = "request_already_final_closed",
                        message = "The original request is already closed by the Module 055C lifecycle authority."
                    });
                }
                if (existing?.Status is "engineer_closed" or "ptc_final_closed")
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Conflict(new
                    {
                        module = "001A",
                        status = existing.Status,
                        message = existing.Status == "ptc_final_closed"
                            ? "Module 055C has already completed final closure."
                            : "This task is already closed by the Engineer."
                    });
                }
            }
            else
            {
                if (existing is null || existing.Status != "engineer_closed")
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Conflict(new
                    {
                        module = "001A",
                        status = existing?.Status ?? "closeout_not_found",
                        message = existing?.Status == "ptc_final_closed"
                            ? "Reopen is blocked because Module 055C completed final closure."
                            : "Only an Engineer-closed task can be reopened."
                    });
                }
                if (existing.PtcFinalClosedAt is not null
                    || !target.TaskActive
                    || Module001ATerminalProjectStatuses.Contains(target.ProjectStatus))
                {
                    await transaction.RollbackAsync(context.RequestAborted);
                    return Results.Conflict(new
                    {
                        module = "001A",
                        status = "ptc_final_close_blocks_reopen",
                        message = "Reopen is blocked because the Project Team Coordinator has closed the original request in Module 055C."
                    });
                }
            }

            var closeoutId = existing?.CloseoutId ?? Guid.NewGuid();
            if (eventType == "engineer_closed")
            {
                await using var save = new NpgsqlCommand("""
                    INSERT INTO module001a_engineer_task_closeouts (
                        module001a_closeout_id, assignment_id, project_id, task_id,
                        engineer_user_id, closeout_status, completion_summary,
                        engineer_closed_at, engineer_closed_by_user_id,
                        reopened_at, reopened_by_user_id, reopen_reason,
                        created_at, updated_at)
                    VALUES (
                        @closeout_id, @assignment_id, @project_id, @task_id,
                        @engineer_user_id, 'engineer_closed', @reason,
                        NOW(), @actor_user_id,
                        NULL, NULL, '', NOW(), NOW())
                    ON CONFLICT(assignment_id) DO UPDATE
                    SET closeout_status = 'engineer_closed',
                        completion_summary = EXCLUDED.completion_summary,
                        engineer_closed_at = NOW(),
                        engineer_closed_by_user_id = EXCLUDED.engineer_closed_by_user_id,
                        reopened_at = NULL,
                        reopened_by_user_id = NULL,
                        reopen_reason = ''
                    RETURNING module001a_closeout_id;
                    """, connection, transaction);
                Module001AAddTargetParameters(save, target, actor, assignmentId, closeoutId, reason);
                closeoutId = (Guid)(await save.ExecuteScalarAsync(context.RequestAborted)
                    ?? throw new InvalidOperationException("Module 001A closeout ID was not returned."));
            }
            else
            {
                await using var reopen = new NpgsqlCommand("""
                    UPDATE module001a_engineer_task_closeouts
                    SET closeout_status = 'reopened',
                        reopened_at = NOW(),
                        reopened_by_user_id = @actor_user_id,
                        reopen_reason = @reason
                    WHERE module001a_closeout_id = @closeout_id;
                    """, connection, transaction);
                reopen.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                reopen.Parameters.AddWithValue("reason", reason);
                reopen.Parameters.AddWithValue("closeout_id", closeoutId);
                await reopen.ExecuteNonQueryAsync(context.RequestAborted);
            }

            var assignmentStatus = eventType == "engineer_closed" ? "engineer_closed" : "active";
            await using (var updateAssignment = new NpgsqlCommand("""
                UPDATE project_assignments
                SET module001a_closeout_status = @closeout_status,
                    module001a_closeout_updated_at = NOW()
                WHERE project_assignment_id = @assignment_id;
                """, connection, transaction))
            {
                updateAssignment.Parameters.AddWithValue("closeout_status", assignmentStatus);
                updateAssignment.Parameters.AddWithValue("assignment_id", assignmentId);
                await updateAssignment.ExecuteNonQueryAsync(context.RequestAborted);
            }

            if (eventType == "engineer_closed")
            {
                await using var hideWeeklyLines = new NpgsqlCommand("""
                    UPDATE module001_weekly_task_lines
                    SET is_active = FALSE,
                        updated_by_user_id = @actor_user_id
                    WHERE assignment_id = @assignment_id
                      AND is_active = TRUE;
                    """, connection, transaction);
                hideWeeklyLines.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                hideWeeklyLines.Parameters.AddWithValue("assignment_id", assignmentId);
                await hideWeeklyLines.ExecuteNonQueryAsync(context.RequestAborted);
            }

            var eventId = Guid.NewGuid();
            var dispatch = await Module001AQueueNotificationAsync(
                connection,
                transaction,
                closeoutId,
                eventId,
                target,
                actor,
                eventType,
                reason,
                mailReadiness.RecipientBoundary,
                context.RequestAborted);

            await using (var insertEvent = new NpgsqlCommand("""
                INSERT INTO module001a_engineer_task_closeout_events (
                    module001a_event_id, module001a_closeout_id, assignment_id,
                    project_id, task_id, engineer_user_id, event_type,
                    event_reason, actor_user_id, notification_dispatch_id,
                    evidence_json, occurred_at)
                VALUES (
                    @event_id, @closeout_id, @assignment_id,
                    @project_id, @task_id, @engineer_user_id, @event_type,
                    @reason, @actor_user_id, @dispatch_id, @evidence, NOW());
                """, connection, transaction))
            {
                insertEvent.Parameters.AddWithValue("event_id", eventId);
                insertEvent.Parameters.AddWithValue("closeout_id", closeoutId);
                insertEvent.Parameters.AddWithValue("assignment_id", assignmentId);
                insertEvent.Parameters.AddWithValue("project_id", target.ProjectId);
                insertEvent.Parameters.AddWithValue("task_id", target.TaskId);
                insertEvent.Parameters.AddWithValue("engineer_user_id", actor.EffectiveUserId);
                insertEvent.Parameters.AddWithValue("event_type", eventType);
                insertEvent.Parameters.AddWithValue("reason", reason);
                insertEvent.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                insertEvent.Parameters.AddWithValue("dispatch_id", dispatch.DispatchId);
                insertEvent.Parameters.Add("evidence", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(new
                {
                    module = "001A",
                    assignmentId,
                    target.ProjectId,
                    target.ProjectCode,
                    target.TaskId,
                    target.TaskCode,
                    priorStatus = existing?.Status ?? "active",
                    newStatus = assignmentStatus,
                    billingLocked = eventType == "engineer_closed",
                    finalCloseAuthority = "055C"
                });
                await insertEvent.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await using (var attachDispatch = new NpgsqlCommand("""
                UPDATE module001a_engineer_task_closeouts
                SET latest_notification_dispatch_id = @dispatch_id
                WHERE module001a_closeout_id = @closeout_id;
                """, connection, transaction))
            {
                attachDispatch.Parameters.AddWithValue("dispatch_id", dispatch.DispatchId);
                attachDispatch.Parameters.AddWithValue("closeout_id", closeoutId);
                await attachDispatch.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await InsertModule001PlatformAuditAsync(
                connection,
                transaction,
                actor.ActualUserId,
                eventType == "engineer_closed" ? "ENGINEER_REQUEST_TASK_CLOSED" : "ENGINEER_REQUEST_TASK_REOPENED",
                "module001a_engineer_task_closeout",
                closeoutId,
                new { status = existing?.Status ?? "active" },
                new
                {
                    status = assignmentStatus,
                    assignmentId,
                    target.ProjectId,
                    target.TaskId,
                    notificationDispatchId = dispatch.DispatchId,
                    notificationToCount = dispatch.ToCount,
                    notificationCcCount = dispatch.CcCount
                });

            await transaction.CommitAsync(context.RequestAborted);

            ProjectNotificationProcessingService.NotificationDeliveryOutcome delivery;
            try
            {
                delivery = await ProjectNotificationProcessingService.DeliverDispatchAsync(
                    connection,
                    dispatch.DispatchId,
                    actor.ActualUserId,
                    eventType == "engineer_closed"
                        ? "Module 001A Engineer closeout notification."
                        : "Module 001A Engineer reopen notification.",
                    context,
                    context.RequestAborted);
            }
            catch
            {
                delivery = new ProjectNotificationProcessingService.NotificationDeliveryOutcome(
                    false,
                    "queued",
                    "module_065",
                    mailReadiness.RecipientBoundary,
                    string.Empty,
                    "MODULE_001A_DELIVERY_RETRY_REQUIRED",
                    "The workflow completed and the notification remains queued for Module 065 retry.",
                    dispatch.DispatchId,
                    0);
            }

            return Results.Json(new
            {
                module = "001A",
                status = eventType == "engineer_closed" ? "engineer_task_closed" : "engineer_task_reopened",
                message = eventType == "engineer_closed"
                    ? "The task is closed, removed from Module 001 billing choices, and queued for PTC notification."
                    : "The task is reopened and the required reason was queued for PTC notification.",
                assignmentId,
                closeoutId,
                eventId,
                billingLocked = eventType == "engineer_closed",
                canReopen = eventType == "engineer_closed",
                finalCloseAuthority = "Module 055C · Project Team Coordinator",
                notification = new
                {
                    dispatchId = dispatch.DispatchId,
                    delivery.Status,
                    delivery.Sent,
                    delivery.Message,
                    toCount = dispatch.ToCount,
                    ccCount = dispatch.CcCount,
                    engineerCopied = true,
                    provider = delivery.Provider
                }
            }, statusCode: delivery.Sent ? StatusCodes.Status200OK : StatusCodes.Status202Accepted);
        }
        catch (PostgresException exception) when (exception.SqlState is "23514" or "23503" or "23505")
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return Results.Conflict(new
            {
                module = "001A",
                status = "closeout_transition_conflict",
                message = "The task closeout changed concurrently. Refresh Module 001A and try again."
            });
        }
        catch
        {
            await transaction.RollbackAsync(context.RequestAborted);
            throw;
        }
    }

    private static async Task<Module001ATarget?> Module001ALoadTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid assignmentId,
        Guid engineerUserId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pa.project_assignment_id,
                   p.project_id,
                   p.project_code,
                   p.project_name,
                   COALESCE(p.work_type, '') AS work_type,
                   COALESCE(p.status, '') AS project_status,
                   COALESCE(client.client_name, 'No customer assigned') AS customer_name,
                   pt.task_id,
                   pt.task_code,
                   pt.task_name,
                   pt.is_active,
                   engineer.user_id,
                   engineer.display_name,
                   engineer.email,
                   coordinator.user_id,
                   COALESCE(coordinator.display_name, coordinator.email, ''),
                   COALESCE(coordinator.email, '')
            FROM project_assignments pa
            JOIN projects p ON p.project_id = pa.project_id
            JOIN project_tasks pt ON pt.task_id = pa.task_id AND pt.project_id = pa.project_id
            JOIN app_users engineer ON engineer.user_id = pa.user_id
            LEFT JOIN clients client ON client.client_id = p.client_id
            LEFT JOIN app_users coordinator ON coordinator.user_id = p.project_coordinator_user_id
            WHERE pa.project_assignment_id = @assignment_id
              AND pa.user_id = @engineer_user_id
              AND pa.effective_start_date <= CURRENT_DATE
              AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= CURRENT_DATE)
              AND (
                    regexp_replace(lower(COALESCE(p.work_type, '')), '[^a-z0-9]+', '', 'g') IN (
                        'servicerequest', 'sr', 'presales', 'presale', 'pres',
                        'internal', 'internalproject', 'internaltask'
                    )
                 OR p.project_code ~* '^(SR|PRES|INT)-'
                 OR lower(COALESCE(NULLIF(to_jsonb(pt)->>'work_task_category', ''), '')) = 'service_request_task'
                 OR NULLIF(to_jsonb(pt)->>'service_request_number', '') IS NOT NULL
              )
            FOR UPDATE OF pa, p, pt;
            """, connection, transaction);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("engineer_user_id", engineerUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new Module001ATarget(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetGuid(7),
            reader.GetString(8), reader.GetString(9), reader.GetBoolean(10), reader.GetGuid(11),
            reader.GetString(12), reader.GetString(13), reader.IsDBNull(14) ? (Guid?)null : reader.GetGuid(14),
            reader.GetString(15), reader.GetString(16));
    }

    private static async Task<Module001ACloseoutRow?> Module001ALoadCloseoutForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT module001a_closeout_id, closeout_status, ptc_final_closed_at
            FROM module001a_engineer_task_closeouts
            WHERE assignment_id = @assignment_id
            FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new Module001ACloseoutRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2))
            : null;
    }

    private static async Task<Module001ANotificationResult> Module001AQueueNotificationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid closeoutId,
        Guid eventId,
        Module001ATarget target,
        ProjectNotificationActor actor,
        string eventType,
        string reason,
        string recipientBoundary,
        CancellationToken cancellationToken)
    {
        var recipients = await Module001ALoadRecipientsAsync(
            connection,
            transaction,
            target,
            cancellationToken);
        if (!recipients.Any(recipient => recipient.Type == "to"))
            throw new InvalidOperationException("No active Project Team Coordinator email is available for Module 001A notification.");

        var actionLabel = eventType == "engineer_closed" ? "closed" : "reopened";
        var subject = eventType == "engineer_closed"
            ? $"Engineer closeout ready: {target.ProjectCode} — {target.TaskName}"
            : $"Engineer reopened task: {target.ProjectCode} — {target.TaskName}";
        var reasonLabel = eventType == "engineer_closed" ? "Completion summary" : "Required reopen reason";
        var body = $"""
            Module 001A Engineer Request Closeout

            Engineer: {target.EngineerName} <{target.EngineerEmail}>
            Customer: {target.CustomerName}
            Request: {target.ProjectCode} — {target.ProjectName}
            Request type: {Module001ARequestType(target.ProjectCode, target.WorkType)}
            Task: {target.TaskCode} — {target.TaskName}
            Action: Engineer {actionLabel} this assigned task.
            {reasonLabel}: {reason}

            Project Team Coordinator action:
            { (eventType == "engineer_closed"
                ? "Review the request and complete final closure in Module 055C when all required work is finished."
                : "Review the reopen reason. Final closure remains in Module 055C after the additional work is completed.") }

            The Engineer is copied on this notification. Module 001 billing is { (eventType == "engineer_closed" ? "locked for this assignment" : "available again while the original request remains open") }.
            """;

        var dispatchId = Guid.NewGuid();
        await using (var insertDispatch = new NpgsqlCommand("""
            INSERT INTO project_notification_dispatches (
                project_notification_dispatch_id, project_id, event_key,
                notification_type, alert_severity, source_module, source_status,
                subject, text_body, html_body, delivery_boundary, provider_source,
                delivery_status, scheduled_for, metadata_json, created_at, updated_at)
            VALUES (
                @dispatch_id, @project_id, @event_key,
                @notification_type, 'informational', '001A', @source_status,
                @subject, @text_body, '', @delivery_boundary, 'module_065',
                'queued', NOW(), @metadata, NOW(), NOW());
            """, connection, transaction))
        {
            insertDispatch.Parameters.AddWithValue("dispatch_id", dispatchId);
            insertDispatch.Parameters.AddWithValue("project_id", target.ProjectId);
            insertDispatch.Parameters.AddWithValue("event_key", $"module001a:{eventId:D}");
            insertDispatch.Parameters.AddWithValue("notification_type", eventType == "engineer_closed" ? "engineer_task_closeout" : "engineer_task_reopened");
            insertDispatch.Parameters.AddWithValue("source_status", eventType);
            insertDispatch.Parameters.AddWithValue("subject", subject);
            insertDispatch.Parameters.AddWithValue("text_body", body);
            insertDispatch.Parameters.AddWithValue("delivery_boundary", recipientBoundary);
            insertDispatch.Parameters.Add("metadata", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(new
            {
                module = "001A",
                closeoutId,
                eventId,
                target.AssignmentId,
                target.ProjectId,
                target.TaskId,
                engineerUserId = actor.EffectiveUserId,
                actualActorUserId = actor.ActualUserId,
                serverDerivedRecipients = true,
                engineerCopied = true,
                finalCloseAuthority = "055C"
            });
            await insertDispatch.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var recipient in recipients)
        {
            await using var insertRecipient = new NpgsqlCommand("""
                INSERT INTO project_notification_dispatch_recipients (
                    project_notification_dispatch_id, recipient_role,
                    recipient_user_id, recipient_name, recipient_email,
                    recipient_type, derivation_source, delivery_status, created_at)
                VALUES (
                    @dispatch_id, @role, @user_id, @name, @email,
                    @type, @source, 'pending', NOW())
                ON CONFLICT DO NOTHING;
                """, connection, transaction);
            insertRecipient.Parameters.AddWithValue("dispatch_id", dispatchId);
            insertRecipient.Parameters.AddWithValue("role", recipient.Role);
            insertRecipient.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = recipient.UserId.HasValue ? (object)recipient.UserId.Value : DBNull.Value;
            insertRecipient.Parameters.AddWithValue("name", recipient.Name);
            insertRecipient.Parameters.AddWithValue("email", recipient.Email);
            insertRecipient.Parameters.AddWithValue("type", recipient.Type);
            insertRecipient.Parameters.AddWithValue("source", recipient.Source);
            await insertRecipient.ExecuteNonQueryAsync(cancellationToken);
        }

        return new Module001ANotificationResult(
            dispatchId,
            recipients.Count(recipient => recipient.Type == "to"),
            recipients.Count(recipient => recipient.Type == "cc"));
    }

    private static async Task<List<Module001ARecipient>> Module001ALoadRecipientsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Module001ATarget target,
        CancellationToken cancellationToken)
    {
        var recipients = new List<Module001ARecipient>();
        if (target.CoordinatorUserId.HasValue && Module001AValidEmail(target.CoordinatorEmail))
        {
            recipients.Add(new(
                target.CoordinatorUserId,
                target.CoordinatorName,
                target.CoordinatorEmail,
                "project_team_coordinator",
                "to",
                "projects.project_coordinator_user_id"));
        }
        else
        {
            await using var command = new NpgsqlCommand("""
                SELECT DISTINCT user_row.user_id,
                       COALESCE(user_row.display_name, user_row.email, ''),
                       user_row.email
                FROM app_users user_row
                JOIN app_user_role_assignments assignment
                  ON assignment.user_id = user_row.user_id
                 AND assignment.is_active = TRUE
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                 AND role.is_active = TRUE
                WHERE user_row.is_active = TRUE
                  AND upper(role.role_code) IN ('PROJECT_TEAM_COORDINATOR', 'PROJECT_COORDINATOR')
                  AND btrim(COALESCE(user_row.email, '')) <> ''
                ORDER BY COALESCE(user_row.display_name, user_row.email, '');
                """, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var email = reader.GetString(2);
                if (Module001AValidEmail(email))
                    recipients.Add(new(reader.GetGuid(0), reader.GetString(1), email, "project_team_coordinator", "to", "active_ptc_role_fallback"));
            }
        }

        if (Module001AValidEmail(target.EngineerEmail)
            && !recipients.Any(recipient => recipient.Email.Equals(target.EngineerEmail, StringComparison.OrdinalIgnoreCase)))
        {
            recipients.Add(new(
                target.EngineerUserId,
                target.EngineerName,
                target.EngineerEmail,
                "assigned_engineer",
                "cc",
                "project_assignments.user_id"));
        }

        return recipients;
    }

    private static async Task<IResult?> Module001ARequireSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.module001a_engineer_task_closeouts') IS NOT NULL
               AND to_regclass('public.module001a_engineer_task_closeout_events') IS NOT NULL
               AND EXISTS (
                    SELECT 1 FROM schema_migrations
                    WHERE migration_id = '078_module_001a_engineer_request_closeout'
               );
            """, connection);
        var ready = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        return ready
            ? null
            : Results.Json(new
            {
                module = "001A",
                status = "module001a_schema_not_ready",
                message = "Migration 078 must be applied before Engineer Request Closeout can be used."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Module001AAccessResult> Module001ARequireAccessAsync(
        NpgsqlConnection connection,
        HttpContext context,
        bool mutation)
    {
        var actualUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseActualUserId",
            "ProjectPulseSessionUserId");
        var effectiveUserId = ProjectPulseActualSessionAuthority.ReadUserId(
            context,
            "ProjectPulseEffectiveUserId",
            "ProjectPulseSessionUserId") ?? actualUserId;
        if (!actualUserId.HasValue || !effectiveUserId.HasValue)
        {
            return Module001AAccessResult.Fail(Results.Json(new
            {
                module = "001A",
                status = "session_required",
                message = "A valid Pulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var actor = await ProjectNotificationRepository.LoadActorAsync(
            connection,
            actualUserId.Value,
            effectiveUserId.Value,
            ProjectPulseActualSessionAuthority.IsViewAs(context),
            context.RequestAborted);
        var allowed = actor.Roles.Any(Module001AEngineerRoles.Contains)
            || actor.Roles.Contains("SUPER_ADMINISTRATOR")
            || actor.Permissions.Contains("VIEW_ENGINEER_TASK_CLOSEOUT_001A")
            || actor.Permissions.Contains("MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A")
            || actor.Permissions.Contains("MANAGE_ALL");
        if (!allowed)
        {
            return Module001AAccessResult.Fail(Results.Json(new
            {
                module = "001A",
                status = "engineer_access_required",
                message = "Module 001A is restricted to Engineers and permanent Super Administrators."
            }, statusCode: StatusCodes.Status403Forbidden));
        }
        if (mutation && actor.IsViewAs)
        {
            return Module001AAccessResult.Fail(Results.Json(new
            {
                module = "001A",
                status = "view_as_read_only",
                message = "Exit Administrator View-As before closing or reopening an Engineer task."
            }, statusCode: StatusCodes.Status403Forbidden));
        }
        if (mutation
            && !actor.Roles.Any(Module001AEngineerRoles.Contains)
            && !actor.Roles.Contains("SUPER_ADMINISTRATOR")
            && !actor.Permissions.Contains("MANAGE_OWN_ENGINEER_TASK_CLOSEOUT_001A")
            && !actor.Permissions.Contains("MANAGE_ALL"))
        {
            return Module001AAccessResult.Fail(Results.Json(new
            {
                module = "001A",
                status = "engineer_closeout_mutation_denied",
                message = "The current role may not close or reopen Engineer-owned request tasks."
            }, statusCode: StatusCodes.Status403Forbidden));
        }
        return new(actor, null);
    }

    private static string Module001ARequestType(string projectCode, string workType)
    {
        var code = (projectCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalized = new string((workType ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (code.StartsWith("SR-", StringComparison.Ordinal) || normalized is "servicerequest" or "sr") return "Service Request";
        if (code.StartsWith("PRES-", StringComparison.Ordinal) || normalized is "presales" or "presale" or "pres") return "Pre-Sales";
        return "Internal";
    }

    private static string Module001AClean(string? value, int maximumLength)
    {
        var clean = (value ?? string.Empty).Replace("\r", " ").Trim();
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static bool Module001AValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 320
        && email.Contains('@', StringComparison.Ordinal)
        && !email.Contains('\r')
        && !email.Contains('\n');

    private static void Module001AAddTargetParameters(
        NpgsqlCommand command,
        Module001ATarget target,
        ProjectNotificationActor actor,
        Guid assignmentId,
        Guid closeoutId,
        string reason)
    {
        command.Parameters.AddWithValue("closeout_id", closeoutId);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("project_id", target.ProjectId);
        command.Parameters.AddWithValue("task_id", target.TaskId);
        command.Parameters.AddWithValue("engineer_user_id", actor.EffectiveUserId);
        command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("reason", reason);
    }

    private sealed record Module001ATarget(
        Guid AssignmentId,
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        string WorkType,
        string ProjectStatus,
        string CustomerName,
        Guid TaskId,
        string TaskCode,
        string TaskName,
        bool TaskActive,
        Guid EngineerUserId,
        string EngineerName,
        string EngineerEmail,
        Guid? CoordinatorUserId,
        string CoordinatorName,
        string CoordinatorEmail);

    private sealed record Module001ACloseoutRow(Guid CloseoutId, string Status, DateTimeOffset? PtcFinalClosedAt);
    private sealed record Module001ARecipient(Guid? UserId, string Name, string Email, string Role, string Type, string Source);
    private sealed record Module001ANotificationResult(Guid DispatchId, int ToCount, int CcCount);
    private sealed record Module001AAccessResult(ProjectNotificationActor? Actor, IResult? Error)
    {
        internal static Module001AAccessResult Fail(IResult error) => new(null, error);
    }
}

internal sealed record Module001ACloseRequest(string? CompletionSummary);
internal sealed record Module001AReopenRequest(string? Reason);
