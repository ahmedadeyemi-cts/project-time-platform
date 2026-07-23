using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class ScopedRolePolicyModule
{
    private static async Task<IResult> EvaluateEndpointAsync(
        string moduleCode,
        string actionCode,
        Guid? targetUserId,
        Guid? projectId,
        Guid? customerId,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return SessionRequired();

        var decision = await ScopedAuthorizationEvaluator.EvaluateAsync(
            connection,
            actor,
            moduleCode,
            actionCode,
            targetUserId,
            projectId,
            customerId,
            ScopedRolePolicyRules.IsWriteAction(actionCode));

        return Results.Ok(decision);
    }

    private static async Task<IResult> ApprovalStagesAsync(HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return SessionRequired();

        var stages = new[]
        {
            new { stageCode = "MANAGER", previousStatus = "submitted", nextStatus = "manager_approved" },
            new { stageCode = "PROJECT_MANAGER", previousStatus = "manager_approved", nextStatus = "pm_approved" },
            new { stageCode = "PTC_FINAL", previousStatus = "pm_approved", nextStatus = "accounting_ready" }
        };

        var events = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                e.scoped_approval_stage_event_id,
                e.timesheet_id,
                e.work_date,
                e.required_stage,
                e.original_responsible_role,
                COALESCE(original_user.display_name, original_user.email, ''),
                COALESCE(actor_user.display_name, actor_user.email, ''),
                e.acting_role_code,
                e.delegated_action,
                e.reason,
                e.previous_status,
                e.new_status,
                e.created_at
            FROM scoped_approval_stage_events e
            LEFT JOIN app_users original_user
              ON original_user.user_id = e.original_responsible_user_id
            LEFT JOIN app_users actor_user
              ON actor_user.user_id = e.acting_user_id
            ORDER BY e.created_at DESC
            LIMIT 250;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            events.Add(new
            {
                eventId = reader.GetGuid(0),
                timesheetId = reader.GetGuid(1),
                workDate = reader.GetFieldValue<DateOnly>(2),
                requiredStage = reader.GetString(3),
                originalResponsibleRole = reader.GetString(4),
                originalResponsibleUser = reader.GetString(5),
                actingUser = reader.GetString(6),
                actingRoleCode = reader.GetString(7),
                delegatedAction = reader.GetBoolean(8),
                reason = reader.GetString(9),
                previousStatus = reader.GetString(10),
                newStatus = reader.GetString(11),
                createdAt = reader.GetFieldValue<DateTimeOffset>(12)
            });
        }

        return Results.Ok(new { stages, events });
    }

    private static async Task<IResult> DelegatedApprovalAsync(
        ScopedApprovalDecisionRequest request,
        HttpContext context)
    {
        return await ProcessScopedApprovalDecisionAsync(request, context, delegated: true);
    }

    private static async Task<IResult> PtcFinalApprovalAsync(
        ScopedApprovalDecisionRequest request,
        HttpContext context)
    {
        var normalized = request with { RequiredStage = "PTC_FINAL" };
        return await ProcessScopedApprovalDecisionAsync(normalized, context, delegated: false);
    }

    private static async Task<IResult> ProcessScopedApprovalDecisionAsync(
        ScopedApprovalDecisionRequest request,
        HttpContext context,
        bool delegated)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return SessionRequired();
        if (actor.IsViewAs)
        {
            return Results.Json(new
            {
                status = "view_as_read_only",
                message = "Approval decisions are disabled while using View-As."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                status = "reason_required",
                message = "A reason is required for staged or delegated approval."
            });
        }

        var stage = (request.RequiredStage ?? string.Empty).Trim().ToUpperInvariant();
        var approve = string.Equals(request.Decision, "APPROVE", StringComparison.OrdinalIgnoreCase);
        var actionCode = stage switch
        {
            "MANAGER" when delegated => "APPROVAL_DELEGATE_MANAGER",
            "PROJECT_MANAGER" when delegated => "APPROVAL_DELEGATE_PROJECT_MANAGER",
            "PTC_FINAL" when approve => "APPROVAL_APPROVE_PTC_FINAL",
            "PTC_FINAL" => "APPROVAL_REJECT_PTC_FINAL",
            "MANAGER" when approve => "APPROVAL_APPROVE_MANAGER",
            "MANAGER" => "APPROVAL_REJECT_MANAGER",
            "PROJECT_MANAGER" when approve => "APPROVAL_APPROVE_PROJECT_MANAGER",
            "PROJECT_MANAGER" => "APPROVAL_REJECT_PROJECT_MANAGER",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(actionCode))
        {
            return Results.BadRequest(new
            {
                status = "invalid_stage",
                message = "RequiredStage must be MANAGER, PROJECT_MANAGER, or PTC_FINAL."
            });
        }

        var decision = await ScopedAuthorizationEvaluator.EvaluateAsync(
            connection,
            actor,
            "002",
            actionCode,
            request.OriginalResponsibleUserId,
            null,
            null,
            true);
        if (!decision.Allowed)
        {
            return Results.Json(new
            {
                status = "scoped_access_denied",
                message = decision.Explanation
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var expectedStatus = stage switch
        {
            "MANAGER" => "submitted",
            "PROJECT_MANAGER" => "manager_approved",
            _ => "pm_approved"
        };
        var nextStatus = approve
            ? stage switch
            {
                "MANAGER" => "manager_approved",
                "PROJECT_MANAGER" => "pm_approved",
                _ => "accounting_ready"
            }
            : "manager_declined";

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using (var update = new NpgsqlCommand("""
                UPDATE timesheet_day_statuses
                SET status = @next_status,
                    manager_decision_comment = @reason,
                    updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date
                  AND status = @expected_status
                RETURNING timesheet_day_status_id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("next_status", nextStatus);
                update.Parameters.AddWithValue("reason", request.Reason.Trim());
                update.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
                update.Parameters.AddWithValue("work_date", request.WorkDate);
                update.Parameters.AddWithValue("expected_status", expectedStatus);
                if (await update.ExecuteScalarAsync() is not Guid)
                {
                    await transaction.RollbackAsync();
                    return Results.Conflict(new
                    {
                        status = "approval_stage_conflict",
                        expectedStatus,
                        message = "The selected time is not at the required approval stage."
                    });
                }
            }

            await using (var updateEntries = new NpgsqlCommand("""
                UPDATE time_entries
                SET status = @next_status, updated_at = NOW()
                WHERE timesheet_id = @timesheet_id
                  AND work_date = @work_date;
                """, connection, transaction))
            {
                updateEntries.Parameters.AddWithValue("next_status", nextStatus);
                updateEntries.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
                updateEntries.Parameters.AddWithValue("work_date", request.WorkDate);
                await updateEntries.ExecuteNonQueryAsync();
            }

            await using (var insert = new NpgsqlCommand("""
                INSERT INTO scoped_approval_stage_events (
                    timesheet_id, work_date, required_stage,
                    original_responsible_role, original_responsible_user_id,
                    acting_user_id, acting_role_code, delegated_action,
                    reason, previous_status, new_status, audit_metadata
                )
                VALUES (
                    @timesheet_id, @work_date, @required_stage,
                    @original_role, @original_user_id,
                    @acting_user_id, @acting_role_code, @delegated,
                    @reason, @previous_status, @new_status, @metadata::jsonb
                );
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
                insert.Parameters.AddWithValue("work_date", request.WorkDate);
                insert.Parameters.AddWithValue("required_stage", stage);
                insert.Parameters.AddWithValue(
                    "original_role",
                    request.OriginalResponsibleRole?.Trim().ToUpperInvariant() ?? stage);
                insert.Parameters.AddWithValue(
                    "original_user_id",
                    (object?)request.OriginalResponsibleUserId ?? DBNull.Value);
                insert.Parameters.AddWithValue("acting_user_id", actor.ActualUserId);
                insert.Parameters.AddWithValue(
                    "acting_role_code",
                    actor.RoleCodes.FirstOrDefault() ?? "UNKNOWN");
                insert.Parameters.AddWithValue("delegated", delegated);
                insert.Parameters.AddWithValue("reason", request.Reason.Trim());
                insert.Parameters.AddWithValue("previous_status", expectedStatus);
                insert.Parameters.AddWithValue("new_status", nextStatus);
                insert.Parameters.AddWithValue(
                    "metadata",
                    JsonSerializer.Serialize(new
                    {
                        policyDecision = decision.Explanation,
                        previousStatus = expectedStatus,
                        newStatus = nextStatus,
                        immutableAudit = true
                    }));
                await insert.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return Results.Ok(new
            {
                status = "approval_stage_recorded",
                requiredStage = stage,
                delegated,
                previousStatus = expectedStatus,
                newStatus = nextStatus,
                actingUserId = actor.ActualUserId
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Results.Problem(
                title: "Scoped approval decision failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ReopenTimeAsync(
        ScopedTimeCorrectionRequest request,
        HttpContext context) =>
        await ProcessTimeCorrectionAsync("TIME_REOPEN", request, context);

    private static async Task<IResult> CorrectTimeAsync(
        ScopedTimeCorrectionRequest request,
        HttpContext context) =>
        await ProcessTimeCorrectionAsync("TIME_CORRECT_ON_BEHALF", request, context);

    private static async Task<IResult> ReassignTimeAsync(
        ScopedTimeCorrectionRequest request,
        HttpContext context) =>
        await ProcessTimeCorrectionAsync("TIME_REASSIGN", request, context);

    private static async Task<IResult> ProcessTimeCorrectionAsync(
        string actionCode,
        ScopedTimeCorrectionRequest request,
        HttpContext context)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync();
        var readiness = await RequirePolicyTablesAsync(connection);
        if (readiness is not null) return readiness;
        var actor = await LoadActorAsync(context, connection);
        if (actor is null) return SessionRequired();
        if (actor.IsViewAs)
        {
            return Results.Json(new
            {
                status = "view_as_read_only",
                message = "Time corrections are disabled while using View-As."
            }, statusCode: StatusCodes.Status403Forbidden);
        }
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.BadRequest(new
            {
                status = "reason_required",
                message = "A reason is required for time correction operations."
            });
        }

        var decision = await ScopedAuthorizationEvaluator.EvaluateAsync(
            connection,
            actor,
            "001",
            actionCode,
            request.TargetUserId,
            request.ProjectId,
            null,
            true);
        if (!decision.Allowed)
        {
            return Results.Json(new
            {
                status = "scoped_access_denied",
                message = decision.Explanation
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var original = await LoadTimeCorrectionTargetAsync(
                connection,
                transaction,
                request.TimesheetId,
                request.WorkDate,
                request.TimeEntryId);
            if (original is null)
            {
                await transaction.RollbackAsync();
                return Results.NotFound(new
                {
                    status = "time_not_found",
                    message = "The selected time record was not found."
                });
            }

            if (actionCode == "TIME_REOPEN")
            {
                await using var reopen = new NpgsqlCommand("""
                    UPDATE timesheet_day_statuses
                    SET status = 'draft',
                        manager_decision_comment = @reason,
                        updated_at = NOW()
                    WHERE timesheet_id = @timesheet_id
                      AND work_date = @work_date
                      AND status IN (
                          'submitted','manager_approved','pm_approved',
                          'accounting_ready','locked'
                      );

                    UPDATE time_entries
                    SET status = 'draft', updated_at = NOW()
                    WHERE timesheet_id = @timesheet_id
                      AND work_date = @work_date;
                    """, connection, transaction);
                reopen.Parameters.AddWithValue("reason", request.Reason.Trim());
                reopen.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
                reopen.Parameters.AddWithValue("work_date", request.WorkDate);
                await reopen.ExecuteNonQueryAsync();
            }
            else if (request.TimeEntryId is Guid timeEntryId)
            {
                await using var update = new NpgsqlCommand("""
                    UPDATE time_entries
                    SET project_id = COALESCE(@project_id, project_id),
                        project_task_id = COALESCE(@task_id, project_task_id),
                        hours = COALESCE(@hours, hours),
                        description = COALESCE(@description, description),
                        status = 'draft',
                        updated_at = NOW()
                    WHERE time_entry_id = @time_entry_id;
                    """, connection, transaction);
                update.Parameters.AddWithValue("project_id", (object?)request.ProjectId ?? DBNull.Value);
                update.Parameters.AddWithValue("task_id", (object?)request.TaskId ?? DBNull.Value);
                update.Parameters.AddWithValue("hours", (object?)request.Hours ?? DBNull.Value);
                update.Parameters.AddWithValue("description", (object?)request.Description ?? DBNull.Value);
                update.Parameters.AddWithValue("time_entry_id", timeEntryId);
                await update.ExecuteNonQueryAsync();
            }

            var revised = await LoadTimeCorrectionTargetAsync(
                connection,
                transaction,
                request.TimesheetId,
                request.WorkDate,
                request.TimeEntryId);

            await using (var audit = new NpgsqlCommand("""
                INSERT INTO scoped_time_correction_events (
                    time_entry_id, timesheet_id, work_date, action_code,
                    actor_user_id, target_user_id, reason,
                    original_values, revised_values,
                    previous_status, new_status
                )
                VALUES (
                    @time_entry_id, @timesheet_id, @work_date, @action_code,
                    @actor_user_id, @target_user_id, @reason,
                    @original_values::jsonb, @revised_values::jsonb,
                    @previous_status, 'draft'
                );
                """, connection, transaction))
            {
                audit.Parameters.AddWithValue("time_entry_id", (object?)request.TimeEntryId ?? DBNull.Value);
                audit.Parameters.AddWithValue("timesheet_id", request.TimesheetId);
                audit.Parameters.AddWithValue("work_date", request.WorkDate);
                audit.Parameters.AddWithValue("action_code", actionCode);
                audit.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
                audit.Parameters.AddWithValue("target_user_id", request.TargetUserId);
                audit.Parameters.AddWithValue("reason", request.Reason.Trim());
                audit.Parameters.AddWithValue("original_values", original.Value.Payload.GetRawText());
                audit.Parameters.AddWithValue(
                    "revised_values",
                    revised?.Payload.GetRawText() ?? original.Value.Payload.GetRawText());
                audit.Parameters.AddWithValue("previous_status", original.Value.Status);
                await audit.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return Results.Ok(new
            {
                status = "time_correction_recorded",
                actionCode,
                requiredNextStep = "REAPPROVE",
                immutableAudit = true
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return Results.Problem(
                title: "Scoped time correction failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
