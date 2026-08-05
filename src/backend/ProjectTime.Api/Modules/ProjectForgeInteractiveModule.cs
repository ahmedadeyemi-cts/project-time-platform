using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class ProjectForgeModule
{
    private sealed record InteractiveTaskState(
        Guid ProjectId,
        Guid TaskId,
        Guid? PlanId,
        string RecordSource,
        string TaskName,
        int Revision,
        int TaskRevision,
        int PlanningRevision,
        bool IsAssignedToEffectiveUser,
        Guid? ReviewerUserId,
        string PlanStatus,
        string TaskStatus,
        string KanbanCategory,
        decimal PercentComplete,
        string BlockedReason);

    private static (string? Source, IResult? Error) ValidateTaskMutation(
        string? recordSource,
        Guid? planId,
        int? expectedRevision,
        string? clientMutationId,
        bool requireRevision = true)
    {
        var source = recordSource?.Trim().ToLowerInvariant();
        if (source is not ("canonical" or "review_plan"))
            return (null, Results.BadRequest(new { status = "record_source_required", allowed = new[] { "canonical", "review_plan" } }));
        if (source == "review_plan" && !planId.HasValue)
            return (null, Results.BadRequest(new { status = "review_plan_required" }));
        if (source == "canonical" && planId.HasValue)
            return (null, Results.BadRequest(new { status = "canonical_plan_not_allowed" }));
        if (requireRevision && (!expectedRevision.HasValue || expectedRevision.Value < 1))
            return (null, Results.BadRequest(new { status = "expected_revision_required" }));
        if (string.IsNullOrWhiteSpace(clientMutationId) || clientMutationId.Trim().Length is < 8 or > 160)
            return (null, Results.BadRequest(new { status = "client_mutation_id_required", message = "Send a stable 8–160 character mutation identifier." }));
        return (source, null);
    }

    private static async Task<InteractiveTaskState?> LockInteractiveTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string source,
        Guid taskId,
        Guid? planId,
        Guid effectiveUserId,
        CancellationToken cancellationToken)
    {
        var sql = source == "canonical"
            ? """
                SELECT task.project_id,task.task_id,NULL::uuid,task.task_name,
                       (task.revision_number+COALESCE(detail.revision_number,0))::int,
                       task.revision_number,COALESCE(detail.revision_number,0)::int,
                       EXISTS(
                           SELECT 1 FROM project_assignments assignment
                           WHERE assignment.task_id=task.task_id AND assignment.user_id=@effective_user
                             AND assignment.effective_start_date<=CURRENT_DATE
                             AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date>=CURRENT_DATE)
                       ),NULL::uuid,''::text,
                       COALESCE(detail.task_status,'not_started'),COALESCE(detail.kanban_category,'backlog'),
                       COALESCE(detail.percent_complete,0),COALESCE(detail.blocked_reason,'')
                FROM project_tasks task
                LEFT JOIN project_forge_task_details detail ON detail.task_id=task.task_id
                WHERE task.task_id=@task_id AND task.is_active=TRUE
                FOR UPDATE OF task
                """
            : """
                SELECT task.project_id,task.plan_task_id,task.plan_id,task.task_name,
                       task.revision_number,0,task.revision_number,
                       task.reviewer_user_id=@effective_user,task.reviewer_user_id,plan.plan_status,
                       task.task_status,task.kanban_category,task.percent_complete,COALESCE(task.blocked_reason,'')
                FROM project_forge_plan_tasks task
                JOIN project_forge_plans plan ON plan.plan_id=task.plan_id
                WHERE task.plan_task_id=@task_id AND task.plan_id=@plan_id AND task.canonical_task_id IS NULL
                  AND task.task_status<>'cancelled'
                  AND plan.plan_status IN ('draft','in_review','changes_requested','reviewed')
                FOR UPDATE OF task,plan
                """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("effective_user", effectiveUserId);
        if (source == "review_plan") command.Parameters.AddWithValue("plan_id", planId!.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new InteractiveTaskState(
            reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), source,
            reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
            !reader.IsDBNull(7) && reader.GetBoolean(7), reader.IsDBNull(8) ? null : reader.GetGuid(8), reader.GetString(9),
            reader.GetString(10), reader.GetString(11), reader.GetDecimal(12), reader.GetString(13));
    }

    private static async Task<JsonElement?> LoadInteractiveTaskResponseAsync(
        NpgsqlConnection connection,
        ProjectForgeAccess access,
        InteractiveTaskState state,
        CancellationToken cancellationToken)
    {
        var rows = await ReadJsonRowsAsync(connection, TasksSql, command =>
        {
            AddAccessParameters(command, access);
            AddNullableUuid(command, "manager_filter", null);
            AddNullableUuid(command, "project_filter", state.ProjectId);
            command.Parameters.AddWithValue("workspace", state.RecordSource);
            AddNullableUuid(command, "plan_filter", state.PlanId);
        }, cancellationToken);
        foreach (var row in rows)
        {
            var idProperty = state.RecordSource == "canonical" ? "taskId" : "planTaskId";
            if (row.TryGetProperty(idProperty, out var id) && id.ValueKind == JsonValueKind.String && id.GetGuid() == state.TaskId)
                return row;
        }
        return null;
    }

    private static async Task<IResult> TaskConflictAsync(
        NpgsqlConnection connection,
        ProjectForgeAccess access,
        InteractiveTaskState state,
        CancellationToken cancellationToken)
    {
        var current = await LoadInteractiveTaskResponseAsync(connection, access, state, cancellationToken);
        var currentRevision = state.Revision;
        if (current.HasValue && current.Value.TryGetProperty("revision", out var revisionElement))
            currentRevision = revisionElement.GetInt32();
        return Results.Conflict(new
        {
            module = "033",
            status = "task_revision_conflict",
            recordSource = state.RecordSource,
            task = current,
            revision = currentRevision,
            message = "This task changed after it was loaded. Refresh the task before saving your change."
        });
    }

    private static bool CanManageTask(ProjectForgeAccess access, InteractiveTaskState state, bool workflowOnly)
        => !access.IsViewAs && (access.CanManage
            || (workflowOnly && state.RecordSource == "canonical" && state.IsAssignedToEffectiveUser && access.CanUpdateAssignedTaskStatus));

    private static async Task InvalidatePlanTaskReviewAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InteractiveTaskState state,
        ProjectForgeAccess access,
        int newTaskRevision,
        string changeSummary,
        CancellationToken cancellationToken,
        bool emitReviewerNotification = true)
    {
        if (state.RecordSource != "review_plan" || !state.PlanId.HasValue) return;
        const string assignmentSql = """
            UPDATE project_forge_plan_assignments
            SET review_status='assigned',completed_at=NULL,reviewed_task_revision=NULL,updated_at=NOW()
            WHERE plan_id=@plan_id AND plan_task_id=@task_id AND assignment_type='task_estimator'
              AND (review_status<>'assigned' OR completed_at IS NOT NULL OR reviewed_task_revision IS NOT NULL)
            """;
        int invalidated;
        await using (var command = new NpgsqlCommand(assignmentSql, connection, transaction))
        {
            command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
            command.Parameters.AddWithValue("task_id", state.TaskId);
            invalidated = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string planSql = """
            UPDATE project_forge_plans
            SET plan_status=CASE WHEN @invalidated THEN 'in_review' ELSE plan_status END,
                reviewed_by_user_id=CASE WHEN @invalidated THEN NULL ELSE reviewed_by_user_id END,
                reviewed_at=CASE WHEN @invalidated THEN NULL ELSE reviewed_at END,
                updated_by_user_id=@actor,updated_at=NOW()
            WHERE plan_id=@plan_id AND plan_status IN ('reviewed','changes_requested','in_review','draft')
            """;
        await using (var plan = new NpgsqlCommand(planSql, connection, transaction))
        {
            plan.Parameters.AddWithValue("plan_id", state.PlanId.Value);
            plan.Parameters.AddWithValue("actor", access.ActualUserId);
            plan.Parameters.AddWithValue("invalidated", invalidated > 0);
            await plan.ExecuteNonQueryAsync(cancellationToken);
        }
        if (emitReviewerNotification && invalidated > 0 && state.ReviewerUserId.HasValue && state.ReviewerUserId.Value != access.EffectiveUserId)
        {
            await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.ReviewAssignedPolicy, state.ProjectId, state.ReviewerUserId,
                $"review-invalidated:{state.PlanId}:task:{state.TaskId}:v{newTaskRevision}",
                new { planId = state.PlanId, planTaskId = state.TaskId, taskName = state.TaskName, reviewerUserId = state.ReviewerUserId, updatedByName = access.DisplayName, changeSummary }, cancellationToken);
        }
    }

    private static async Task InsertTaskAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InteractiveTaskState state,
        string eventCode,
        ProjectForgeAccess access,
        object metadata,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO project_forge_audit_events(
                audit_event_id,project_id,plan_id,plan_task_id,canonical_task_id,event_code,entity_type,entity_id,
                actual_actor_user_id,effective_actor_user_id,event_metadata,correlation_id)
            VALUES(gen_random_uuid(),@project_id,@plan_id,@plan_task_id,@canonical_task_id,@event_code,@entity_type,@entity_id,
                   @actual,@effective,@metadata,@correlation)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", state.ProjectId);
        AddNullableUuid(command, "plan_id", state.PlanId);
        AddNullableUuid(command, "plan_task_id", state.RecordSource == "review_plan" ? state.TaskId : null);
        AddNullableUuid(command, "canonical_task_id", state.RecordSource == "canonical" ? state.TaskId : null);
        command.Parameters.AddWithValue("event_code", Clean(eventCode, 100, "TASK_UPDATED"));
        command.Parameters.AddWithValue("entity_type", state.RecordSource == "canonical" ? "canonical_task" : "plan_task");
        command.Parameters.AddWithValue("entity_id", state.TaskId);
        command.Parameters.AddWithValue("actual", access.ActualUserId);
        command.Parameters.AddWithValue("effective", access.EffectiveUserId);
        AddJson(command, "metadata", metadata);
        command.Parameters.AddWithValue("correlation", string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCoalescedTaskNotificationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InteractiveTaskState state,
        ProjectForgeAccess access,
        string clientMutationId,
        string changeSummary,
        CancellationToken cancellationToken)
    {
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300;
        var sourceId = $"task:{state.RecordSource}:{state.TaskId}:updates:{bucket}";
        var availableAt = DateTimeOffset.FromUnixTimeSeconds((bucket + 1) * 300);
        var payload = new
        {
            taskId = state.RecordSource == "canonical" ? state.TaskId : (Guid?)null,
            planTaskId = state.RecordSource == "review_plan" ? state.TaskId : (Guid?)null,
            planId = state.PlanId,
            taskName = state.TaskName,
            updatedByName = access.DisplayName,
            changeSummary,
            clientMutationId
        };
        const string sql = """
            WITH policy AS (
                SELECT policy_code,enabled FROM enterprise_notification_policies WHERE policy_code=@policy
            ), upserted AS (
                INSERT INTO enterprise_notification_events(
                    enterprise_notification_event_id,policy_code,source_module,source_event_id,idempotency_key,
                    entity_type,entity_id,project_id,subject_user_id,occurred_at,available_at,payload,ingestion_source,event_status)
                SELECT gen_random_uuid(),policy.policy_code,'033',@source_id,@idempotency,'project_forge',@task_id,@project_id,NULL,
                       NOW(),@available_at,@payload,'native_bridge',CASE WHEN policy.enabled THEN 'pending' ELSE 'suppressed' END
                FROM policy
                ON CONFLICT(idempotency_key) DO UPDATE
                SET payload=EXCLUDED.payload,occurred_at=EXCLUDED.occurred_at,available_at=EXCLUDED.available_at
                WHERE enterprise_notification_events.event_status IN ('pending','failed')
                RETURNING enterprise_notification_event_id,event_status
            ), history AS (
                INSERT INTO enterprise_notification_event_history(
                    enterprise_notification_event_history_id,enterprise_notification_event_id,history_code,event_status,
                    diagnostic_code,history_metadata,correlation_id)
                SELECT gen_random_uuid(),enterprise_notification_event_id,'EVENT_ACCEPTED',event_status,
                       'PROJECT_FORGE_TASK_UPDATE_BUCKET',jsonb_build_object('sourceModule','033','bucket',@bucket),@source_id
                FROM upserted
            )
            SELECT (SELECT COUNT(*) FROM policy)::int
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("policy", ProjectForgePolicy.TaskUpdatedPolicy);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("idempotency", $"033:{ProjectForgePolicy.TaskUpdatedPolicy}:{sourceId}");
        command.Parameters.AddWithValue("task_id", state.TaskId);
        command.Parameters.AddWithValue("project_id", state.ProjectId);
        command.Parameters.AddWithValue("available_at", availableAt);
        command.Parameters.AddWithValue("bucket", bucket);
        AddJson(command, "payload", payload);
        var policyCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (policyCount != 1)
            throw new InvalidOperationException($"Required Module 065 policy {ProjectForgePolicy.TaskUpdatedPolicy} is not registered; the Project Forge write was rolled back.");
    }

    private static async Task<IResult> CreateTaskAsync(
        Guid projectId,
        ProjectForgeTaskCreateRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ClientMutationId) || request.ClientMutationId.Trim().Length is < 8 or > 160)
            return Results.BadRequest(new { status = "client_mutation_id_required" });
        if (string.IsNullOrWhiteSpace(request.TaskName) || request.TaskName.Trim().Length < 3)
            return Results.BadRequest(new { status = "task_name_required" });
        if (request.StartDate.HasValue != request.DueDate.HasValue
            || (request.StartDate.HasValue && request.DueDate.HasValue && request.DueDate < request.StartDate))
            return Results.BadRequest(new { status = "invalid_task_dates" });
        if (request.EstimatedHours is < 0 or > 100000 || request.DurationWorkingDays is < 0 or > 730
            || request.PercentComplete is < 0 or > 100
            || request.HourlyRate < 0 || request.MaterialUnits < 0 || request.MaterialUnitCost < 0
            || request.FixedCost < 0 || request.TravelCost < 0 || request.EquipmentCost < 0 || request.MiscCost < 0)
            return Results.BadRequest(new { status = "invalid_task_estimate" });
        if (request.RecurrenceRule?.Length > 4000)
            return Results.BadRequest(new { status = "invalid_recurrence_rule" });
        var createTaskType = Normalize(request.TaskType, "variable", "variable", "recurring");
        var recurrenceRule = "{}";
        if (createTaskType == "recurring" && !string.IsNullOrWhiteSpace(request.RecurrenceRule))
        {
            try
            {
                using var recurrence = JsonDocument.Parse(request.RecurrenceRule);
                if (recurrence.RootElement.ValueKind != JsonValueKind.Object)
                    return Results.BadRequest(new { status = "invalid_recurrence_rule" });
                recurrenceRule = recurrence.RootElement.GetRawText();
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { status = "invalid_recurrence_rule" });
            }
        }

        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);
        if (!await CanAccessProjectAsync(connection, access, projectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, projectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (request.AssigneeUserId.HasValue && !await IsEligibleEngineerReviewerAsync(connection, projectId, request.AssigneeUserId.Value, cancellationToken))
            return Results.BadRequest(new { status = "assignee_not_on_project", message = "Choose an active Engineer already assigned to this project." });

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockProjectAsync(connection, transaction, projectId, cancellationToken);
        projectWriteError = await EnsureProjectWritableAsync(connection, transaction, projectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if ((request.StartDate.HasValue && !await IsWorkingDayAsync(connection, transaction, request.StartDate.Value, cancellationToken))
            || (request.DueDate.HasValue && !await IsWorkingDayAsync(connection, transaction, request.DueDate.Value, cancellationToken)))
            return Results.BadRequest(new { status = "task_date_not_working_day", message = "Task start and due dates must be configured working days and cannot be company holidays." });
        var createDuration = request.DurationWorkingDays;
        if (request.StartDate.HasValue && request.DueDate.HasValue)
        {
            createDuration = await WorkingDayDurationAsync(connection, transaction, request.StartDate.Value, request.DueDate.Value, cancellationToken);
            if (createDuration is < 1 or > 730)
                return Results.BadRequest(new { status = "invalid_working_day_range", message = "A scheduled Project Forge task must contain between 1 and 730 configured working days." });
        }
        if (request.ParentTaskId.HasValue)
        {
            await using var parent = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM project_tasks WHERE task_id=@parent AND project_id=@project_id AND is_active=TRUE)", connection, transaction);
            parent.Parameters.AddWithValue("parent", request.ParentTaskId.Value);
            parent.Parameters.AddWithValue("project_id", projectId);
            if ((bool?)await parent.ExecuteScalarAsync(cancellationToken) != true)
                return Results.BadRequest(new { status = "parent_task_scope_mismatch" });
        }
        var taskId = Guid.NewGuid();
        var createLane = Normalize(request.KanbanCategory, "backlog", "backlog", "ready", "in_progress", "review", "blocked", "done");
        var createStatus = LaneStatus(createLane, false);
        if (!string.IsNullOrWhiteSpace(request.Status)
            && !string.Equals(request.Status.Trim(), createStatus, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new
            {
                status = "task_workflow_lane_status_conflict",
                message = $"Kanban lane '{createLane}' requires task status '{createStatus}'."
            });
        var createPercent = createLane switch
        {
            "done" => 100m,
            "backlog" or "ready" => 0m,
            _ => Math.Min(request.PercentComplete, 99m)
        };
        var createBlockedReason = createLane == "blocked"
            ? Clean(request.BlockedReason, 1000, "Blocked")
            : string.Empty;
        var decision = Normalize(request.DecisionAction, "none", "none", "do", "delegate", "decide", "delete");
        var taskCode = Clean(request.TaskCode, 80, string.Empty);
        if (string.IsNullOrWhiteSpace(taskCode)) taskCode = await NextCanonicalTaskCodeAsync(connection, transaction, projectId, "TASK", cancellationToken);
        else
        {
            await using var duplicateCode = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM project_tasks WHERE project_id=@project_id AND UPPER(task_code)=UPPER(@task_code))", connection, transaction);
            duplicateCode.Parameters.AddWithValue("project_id", projectId);
            duplicateCode.Parameters.AddWithValue("task_code", taskCode);
            if ((bool?)await duplicateCode.ExecuteScalarAsync(cancellationToken) == true)
                return Results.Conflict(new { status = "task_code_exists", taskCode });
        }
        const string taskSql = """
            INSERT INTO project_tasks(task_id,project_id,task_code,task_name,task_description,billable,is_active,revision_number,updated_by_user_id)
            VALUES(@task_id,@project_id,@task_code,@name,@description,@billable,TRUE,1,@actor)
            """;
        await using (var command = new NpgsqlCommand(taskSql, connection, transaction))
        {
            command.Parameters.AddWithValue("task_id", taskId);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("task_code", taskCode);
            command.Parameters.AddWithValue("name", Clean(request.TaskName, 255, "Project task"));
            command.Parameters.AddWithValue("description", Clean(request.Description, 4000, string.Empty));
            command.Parameters.AddWithValue("billable", request.Billable);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string detailSql = """
            INSERT INTO project_forge_task_details(
                task_id,project_id,task_type,phase_name,priority_code,task_status,kanban_category,
                planned_start_date,planned_end_date,duration_working_days,parent_task_id,estimated_hours,
                hourly_rate,material_units,material_unit_cost,fixed_cost,travel_cost,equipment_cost,miscellaneous_cost,
                recurrence_rule,decision_action,is_important,is_urgent,percent_complete,blocked_reason,display_order,
                source_kind,created_by_user_id,updated_by_user_id)
            VALUES(@task_id,@project_id,@task_type,@phase,@priority,@status,@kanban,@start,@due,@duration,@parent,@hours,
                   @rate,@material_units,@material_cost,@fixed,@travel,@equipment,@misc,@recurrence::jsonb,@decision,@important,@urgent,@percent,@blocked,
                   COALESCE((SELECT MAX(display_order)+1 FROM project_forge_task_details WHERE project_id=@project_id),1),
                   'pm_created',@actor,@actor)
            """;
        await using (var command = new NpgsqlCommand(detailSql, connection, transaction))
        {
            command.Parameters.AddWithValue("task_id", taskId);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("task_type", createTaskType);
            command.Parameters.AddWithValue("phase", Clean(request.Phase, 160, string.Empty));
            command.Parameters.AddWithValue("priority", Normalize(request.Priority, "normal", "low", "normal", "high", "critical"));
            command.Parameters.AddWithValue("status", createStatus);
            command.Parameters.AddWithValue("kanban", createLane);
            AddNullableDate(command, "start", request.StartDate);
            AddNullableDate(command, "due", request.DueDate);
            AddNullableUuid(command, "parent", request.ParentTaskId);
            command.Parameters.AddWithValue("duration", Math.Clamp(createDuration, 0, 730));
            command.Parameters.AddWithValue("hours", Math.Clamp(request.EstimatedHours, 0m, 100000m));
            command.Parameters.AddWithValue("rate", request.HourlyRate);
            command.Parameters.AddWithValue("material_units", request.MaterialUnits);
            command.Parameters.AddWithValue("material_cost", request.MaterialUnitCost);
            command.Parameters.AddWithValue("fixed", request.FixedCost);
            command.Parameters.AddWithValue("travel", request.TravelCost);
            command.Parameters.AddWithValue("equipment", request.EquipmentCost);
            command.Parameters.AddWithValue("misc", request.MiscCost);
            command.Parameters.AddWithValue("recurrence", recurrenceRule);
            command.Parameters.AddWithValue("decision", decision);
            command.Parameters.AddWithValue("important", request.Important);
            command.Parameters.AddWithValue("urgent", request.Urgent);
            command.Parameters.AddWithValue("percent", createPercent);
            command.Parameters.AddWithValue("blocked", createBlockedReason);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (request.AssigneeUserId.HasValue)
        {
            await InsertCanonicalAssignmentAsync(connection, transaction, projectId, taskId, request.AssigneeUserId.Value,
                Math.Max(0m, request.EstimatedHours), access.ActualUserId, request.StartDate, request.DueDate, cancellationToken);
        }
        var state = new InteractiveTaskState(projectId, taskId, null, "canonical", Clean(request.TaskName, 255, "Project task"), 2, 1, 1, false, null, string.Empty,
            createStatus,createLane,createPercent,createBlockedReason);
        await InsertTaskAuditAsync(connection, transaction, state, "CANONICAL_TASK_CREATED", access,
            new { taskCode, createStatus, createLane, percentComplete = createPercent, blockedReason = createBlockedReason, request.ClientMutationId }, cancellationToken);
        if (request.AssigneeUserId.HasValue)
        {
            var name = await LoadUserNameAsync(connection, request.AssigneeUserId.Value, cancellationToken);
            await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.TaskAssignedPolicy, projectId, request.AssigneeUserId,
                $"task:{taskId}:assigned:{request.AssigneeUserId}:{request.ClientMutationId}",
                new { taskId, taskCode, taskName = state.TaskName, assignedUserId = request.AssigneeUserId, assigneeName = name }, cancellationToken);
        }
        else
        {
            await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.TaskUpdatedPolicy, projectId, null,
                $"task:{taskId}:created:{request.ClientMutationId}",
                new { taskId, taskCode, taskName = state.TaskName, updatedByName = access.DisplayName, changeSummary = "A live project task was created." }, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        var responseTask = await LoadInteractiveTaskResponseAsync(connection, access, state, cancellationToken);
        var revision = 2;
        if (responseTask.HasValue && responseTask.Value.TryGetProperty("revision", out var revisionElement))
            revision = revisionElement.GetInt32();
        return Results.Created($"/api/project-forge/tasks/{taskId}", new { module = "033", status = "canonical_task_created", recordSource = "canonical", task = responseTask, revision, stateChanged = true });
    }

    private static async Task<IResult> PatchTaskCompositeAsync(
        Guid taskId,
        ProjectForgeTaskCompositePatchRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId);
        if (envelope.Error is not null) return envelope.Error;
        if (request.Details is null && request.Workflow is null && request.Schedule is null && request.Decision is null)
            return Results.BadRequest(new { status = "composite_change_required" });

        ProjectForgeTaskDetailsPatchRequest? details = null;
        if (request.Details is not null)
        {
            var value = request.Details;
            if (value.DurationWorkingDays is < 0 or > 730 || value.EstimatedHours is < 0 or > 100000 || value.HourlyRate < 0
                || value.MaterialUnits < 0 || value.MaterialUnitCost < 0 || value.FixedCost < 0 || value.TravelCost < 0
                || value.EquipmentCost < 0 || value.MiscCost < 0)
                return Results.BadRequest(new { status = "invalid_task_details" });
            if ((value.ClearParentTask && value.ParentTaskId.HasValue)
                || (value.ClearRecurrenceRule && value.RecurrenceRule is not null))
                return Results.BadRequest(new { status = "ambiguous_clear_request" });
            if (value.RecurrenceRule is not null)
            {
                if (value.RecurrenceRule.Length > 4000)
                    return Results.BadRequest(new { status = "invalid_recurrence_rule" });
                try
                {
                    using var recurrence = JsonDocument.Parse(value.RecurrenceRule);
                    if (recurrence.RootElement.ValueKind != JsonValueKind.Object)
                        return Results.BadRequest(new { status = "invalid_recurrence_rule" });
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { status = "invalid_recurrence_rule" });
                }
            }
            details = new ProjectForgeTaskDetailsPatchRequest(request.RecordSource, request.PlanId, request.ExpectedRevision,
                request.ClientMutationId, value.TaskName, value.Description, value.TaskType, value.Phase, value.Priority,
                value.DurationWorkingDays, value.ParentTaskId, value.EstimatedHours, value.HourlyRate, value.MaterialUnits,
                value.MaterialUnitCost, value.FixedCost, value.TravelCost, value.EquipmentCost, value.MiscCost,
                value.RecurrenceRule, value.ClearParentTask, value.ClearRecurrenceRule);
        }

        string? lane = null;
        string? workflowStatus = null;
        decimal workflowPercent = 0m;
        string workflowBlockedReason = string.Empty;
        if (request.Workflow is not null)
        {
            if (request.Workflow.BeforeTaskId.HasValue && request.Workflow.AfterTaskId.HasValue)
                return Results.BadRequest(new { status = "ambiguous_kanban_position" });
            if (request.Workflow.PercentComplete is < 0 or > 100)
                return Results.BadRequest(new { status = "invalid_percent_complete" });
            lane = Normalize(request.Workflow.KanbanCategory, string.Empty, "backlog", "ready", "in_progress", "review", "blocked", "done");
            if (string.IsNullOrEmpty(lane)) return Results.BadRequest(new { status = "invalid_kanban_category" });
            workflowStatus = LaneStatus(lane, envelope.Source == "review_plan");
            if (!string.IsNullOrWhiteSpace(request.Workflow.Status)
                && !string.Equals(request.Workflow.Status.Trim(), workflowStatus, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { status = "task_workflow_lane_status_conflict", requiredStatus = workflowStatus });
            workflowPercent = lane == "done" ? 100m : Math.Clamp(request.Workflow.PercentComplete ?? 0m, 0m, 100m);
            workflowBlockedReason = lane == "blocked" ? Clean(request.Workflow.BlockedReason, 2000, "Blocked") : string.Empty;
        }

        string? interaction = null;
        if (request.Schedule is not null)
        {
            interaction = Normalize(request.Schedule.Interaction, string.Empty, "move", "resize_start", "resize_end", "set_range");
            if (string.IsNullOrEmpty(interaction)) return Results.BadRequest(new { status = "invalid_schedule_interaction" });
            if (!request.Schedule.StartDate.HasValue || !request.Schedule.DueDate.HasValue || request.Schedule.DueDate < request.Schedule.StartDate)
                return Results.BadRequest(new { status = "invalid_task_dates", message = "Schedule changes require a valid start and due date." });
        }

        string? decision = null;
        if (request.Decision is not null)
        {
            decision = Normalize(request.Decision.DecisionAction, string.Empty, "none", "do", "delegate", "decide", "delete");
            if (string.IsNullOrEmpty(decision)) return Results.BadRequest(new { status = "invalid_decision_action" });
            var expectedFlags = decision switch
            {
                "do" => (Important: true, Urgent: true),
                "decide" => (Important: true, Urgent: false),
                "delegate" => (Important: false, Urgent: true),
                _ => (Important: false, Urgent: false)
            };
            if (request.Decision.Important != expectedFlags.Important || request.Decision.Urgent != expectedFlags.Urgent)
                return Results.BadRequest(new { status = "decision_quadrant_mismatch" });
        }

        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, taskId, request.PlanId, access.EffectiveUserId, cancellationToken);
        if (state is null) return Results.NotFound(new { status = "task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, state.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        await LockProjectAsync(connection, transaction, state.ProjectId, cancellationToken);
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (state.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, state, cancellationToken);
        }

        var assignedEngineerEdit = state.RecordSource == "review_plan" && state.IsAssignedToEffectiveUser
            && access.CanEditAssignedEstimate && !access.IsViewAs;
        if (details is not null)
        {
            if (!access.CanManage && !assignedEngineerEdit) return WriteForbidden(access);
            if (!access.CanManage && (details.TaskName is not null || details.TaskType is not null || details.Phase is not null
                || details.Priority is not null || details.ParentTaskId.HasValue || details.HourlyRate.HasValue || details.MaterialUnits.HasValue
                || details.MaterialUnitCost.HasValue || details.FixedCost.HasValue || details.TravelCost.HasValue
                || details.EquipmentCost.HasValue || details.MiscCost.HasValue || details.RecurrenceRule is not null
                || details.ClearParentTask || details.ClearRecurrenceRule))
                return Forbidden("MANAGE_PROJECT_FORGE_033");
            if (details.ParentTaskId.HasValue
                && (details.ParentTaskId.Value == state.TaskId
                    || !await IsValidParentTaskAsync(connection, transaction, state, details.ParentTaskId.Value, cancellationToken)))
                return Results.BadRequest(new { status = "invalid_parent_task", message = "Choose an active task in the same live project or review plan that is not a descendant of this task." });
        }
        if (request.Workflow is not null && !CanManageTask(access, state, true))
            return Forbidden("UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033");
        if (request.Schedule is not null && !access.CanManage && !assignedEngineerEdit)
            return WriteForbidden(access);
        if (request.Schedule?.CascadeSuccessors == true && state.RecordSource == "review_plan")
            return Results.BadRequest(new
            {
                status = "review_plan_schedule_cascade_not_supported",
                message = "Review-plan schedule changes must be reviewed task by task. Cascade is available after canonical adoption."
            });
        if (request.Decision is not null && !CanManageTask(access, state, false))
            return WriteForbidden(access);

        var priorSchedule = await LoadTaskScheduleAsync(connection, transaction, state, cancellationToken);
        if (details is { DurationWorkingDays: { } requestedDuration } && request.Schedule is null
            && priorSchedule.Start.HasValue && priorSchedule.Due.HasValue
            && requestedDuration != priorSchedule.Duration)
            return Results.BadRequest(new
            {
                status = "scheduled_duration_requires_schedule_change",
                message = "A scheduled task's duration is derived from its working-day date range. Save the dates with the duration change."
            });

        if (details is not null)
        {
            if (state.RecordSource == "canonical")
                await PatchCanonicalDetailsAsync(connection, transaction, state, details, access.ActualUserId, cancellationToken);
            else
                await PatchReviewPlanDetailsAsync(connection, transaction, state, details, access.CanManage, access.ActualUserId, cancellationToken);
        }

        var materialWorkflowChange = false;
        if (request.Workflow is not null)
        {
            int order;
            try
            {
                order = await ReorderKanbanLaneAsync(connection, transaction, state, lane!, request.Workflow.BeforeTaskId,
                    request.Workflow.AfterTaskId, access.ActualUserId, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { status = "invalid_kanban_position", message = exception.Message });
            }
            var sql = state.RecordSource == "canonical"
                ? """
                    INSERT INTO project_forge_task_details(task_id,project_id,task_status,kanban_category,percent_complete,blocked_reason,display_order,source_kind,created_by_user_id,updated_by_user_id)
                    VALUES(@task_id,@project_id,@status,@lane,@percent,@blocked,@display,'pm_created',@actor,@actor)
                    ON CONFLICT(task_id) DO UPDATE SET task_status=@status,kanban_category=@lane,percent_complete=@percent,
                        blocked_reason=@blocked,display_order=@display,updated_by_user_id=@actor,updated_at=NOW()
                    """
                : """
                    UPDATE project_forge_plan_tasks SET task_status=@status,kanban_category=@lane,percent_complete=@percent,
                        blocked_reason=@blocked,display_order=@display,updated_by_user_id=@actor,updated_at=NOW()
                    WHERE plan_task_id=@task_id AND plan_id=@plan_id
                    """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddWorkflowParameters(command, state, workflowStatus!, lane!, workflowPercent, workflowBlockedReason, order, access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            materialWorkflowChange = !string.Equals(state.KanbanCategory, lane, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(state.TaskStatus, workflowStatus, StringComparison.OrdinalIgnoreCase)
                || state.PercentComplete != workflowPercent
                || !string.Equals(state.BlockedReason, workflowBlockedReason, StringComparison.Ordinal);
        }

        var cascaded = 0;
        DateOnly? authoritativeStart = null;
        DateOnly? authoritativeDue = null;
        int? authoritativeDuration = null;
        if (request.Schedule is not null)
        {
            authoritativeStart = request.Schedule.StartDate!.Value;
            authoritativeDue = request.Schedule.DueDate!.Value;
            if (!await IsWorkingDayAsync(connection, transaction, authoritativeStart.Value, cancellationToken)
                || (interaction != "move" && !await IsWorkingDayAsync(connection, transaction, authoritativeDue.Value, cancellationToken)))
                return Results.BadRequest(new { status = "task_date_not_working_day", message = "Task start and due dates must be configured working days and cannot be company holidays." });
            var duration = await WorkingDayDurationAsync(connection, transaction, authoritativeStart.Value, authoritativeDue.Value, cancellationToken);
            if (interaction == "move")
            {
                var preservedDuration = priorSchedule.Duration > 0
                    ? priorSchedule.Duration
                    : priorSchedule.Start.HasValue && priorSchedule.Due.HasValue
                        ? await WorkingDayDurationAsync(connection, transaction, priorSchedule.Start.Value, priorSchedule.Due.Value, cancellationToken)
                        : duration;
                duration = Math.Max(1, preservedDuration);
                authoritativeDue = await AddWorkingDaysAsync(connection, transaction, authoritativeStart.Value, duration - 1, cancellationToken);
            }
            if (duration is < 1 or > 730)
                return Results.BadRequest(new { status = "invalid_working_day_range", message = "A scheduled Project Forge task must contain between 1 and 730 configured working days." });
            authoritativeDuration = duration;
            var workingDelta = priorSchedule.Start.HasValue
                ? await WorkingDayDeltaAsync(connection, transaction, priorSchedule.Start.Value, authoritativeStart.Value, cancellationToken)
                : 0;
            var sql = state.RecordSource == "canonical"
                ? """
                    INSERT INTO project_forge_task_details(task_id,project_id,planned_start_date,planned_end_date,duration_working_days,source_kind,created_by_user_id,updated_by_user_id)
                    VALUES(@task_id,@project_id,@start,@due,@duration,'pm_created',@actor,@actor)
                    ON CONFLICT(task_id) DO UPDATE SET planned_start_date=@start,planned_end_date=@due,duration_working_days=@duration,
                        updated_by_user_id=@actor,updated_at=NOW()
                    """
                : """
                    UPDATE project_forge_plan_tasks SET planned_start_date=@start,planned_end_date=@due,duration_working_days=@duration,
                        updated_by_user_id=@actor,updated_at=NOW()
                    WHERE plan_task_id=@task_id AND plan_id=@plan_id
                    """;
            await using (var command = new NpgsqlCommand(sql, connection, transaction))
            {
                AddScheduleParameters(command, state, authoritativeStart.Value, authoritativeDue.Value, duration, access.ActualUserId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            cascaded = request.Schedule.CascadeSuccessors && interaction == "move" && workingDelta != 0
                ? await CascadeSuccessorDatesAsync(connection, transaction, state, workingDelta, access.ActualUserId, cancellationToken)
                : 0;
            var dependencyViolations = await LoadDependencyViolationsAsync(connection, transaction, state, cancellationToken);
            if (dependencyViolations.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Conflict(new
                {
                    status = "task_dependency_constraint_violation",
                    message = "The schedule change would violate one or more predecessor constraints.",
                    dependencyIds = dependencyViolations
                });
            }
        }

        if (request.Decision is not null)
        {
            var sql = state.RecordSource == "canonical"
                ? """
                    INSERT INTO project_forge_task_details(task_id,project_id,decision_action,is_important,is_urgent,source_kind,created_by_user_id,updated_by_user_id)
                    VALUES(@task_id,@project_id,@decision,@important,@urgent,'pm_created',@actor,@actor)
                    ON CONFLICT(task_id) DO UPDATE SET decision_action=@decision,is_important=@important,is_urgent=@urgent,updated_by_user_id=@actor,updated_at=NOW()
                    """
                : """
                    UPDATE project_forge_plan_tasks SET decision_action=@decision,is_important=@important,is_urgent=@urgent,updated_by_user_id=@actor,updated_at=NOW()
                    WHERE plan_task_id=@task_id AND plan_id=@plan_id
                    """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("task_id", state.TaskId);
            command.Parameters.AddWithValue("project_id", state.ProjectId);
            if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
            command.Parameters.AddWithValue("decision", decision!);
            command.Parameters.AddWithValue("important", request.Decision.Important);
            command.Parameters.AddWithValue("urgent", request.Decision.Urgent);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var changed = await LockInteractiveTaskAsync(connection, transaction, state.RecordSource, state.TaskId, state.PlanId,
            access.EffectiveUserId, cancellationToken) ?? throw new InvalidOperationException("Updated Project Forge task could not be reloaded.");
        var materialTaskChange = details is not null || request.Schedule is not null || request.Decision is not null || materialWorkflowChange;
        if (materialTaskChange)
        {
            await InvalidatePlanTaskReviewAsync(connection, transaction, changed, access, changed.Revision,
                "Task content, workflow, schedule, or decision changed after review.", cancellationToken,
                emitReviewerNotification: false);
        }
        else if (changed.PlanId.HasValue)
        {
            await PreserveCompletedReviewRevisionsAsync(connection, transaction, changed.PlanId.Value,
                new[] { changed.TaskId }, cancellationToken);
            await TouchPlanAsync(connection, transaction, changed.PlanId.Value, access.ActualUserId, cancellationToken);
        }
        int? planRevision = null;
        if (changed.PlanId.HasValue)
            planRevision = await LoadPlanRevisionAsync(connection, transaction, changed.PlanId.Value, cancellationToken);
        var changedSections = new[]
        {
            details is not null ? "details" : null,
            request.Workflow is not null ? "workflow" : null,
            request.Schedule is not null ? "schedule" : null,
            request.Decision is not null ? "decision" : null
        }.Where(value => value is not null).ToArray();
        await InsertTaskAuditAsync(connection, transaction, changed, "TASK_COMPOSITE_UPDATED", access,
            new
            {
                changedSections,
                workflowMaterialChange = materialWorkflowChange,
                schedule = request.Schedule is null ? null : new { interaction, startDate = authoritativeStart, dueDate = authoritativeDue, durationWorkingDays = authoritativeDuration, cascaded },
                request.ClientMutationId
            }, cancellationToken);
        if (materialTaskChange)
            await InsertCoalescedTaskNotificationAsync(connection, transaction, changed, access, request.ClientMutationId!,
                $"Task {string.Join(", ", changedSections!)} were updated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var responseTask = await LoadInteractiveTaskResponseAsync(connection, access, changed, cancellationToken);
        return Results.Ok(new
        {
            module = "033",
            status = "task_composite_updated",
            recordSource = changed.RecordSource,
            planId = changed.PlanId,
            task = responseTask,
            revision = changed.Revision,
            planRevision,
            cascadedSuccessorCount = cascaded,
            stateChanged = true
        });
    }

    private static async Task<int?> LoadPlanRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid planId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT revision_number FROM project_forge_plans WHERE plan_id=@plan_id", connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        return (int?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<IResult> PatchTaskDetailsAsync(
        Guid taskId,
        ProjectForgeTaskDetailsPatchRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId);
        if (envelope.Error is not null) return envelope.Error;
        if (request.DurationWorkingDays is < 0 or > 730 || request.EstimatedHours is < 0 or > 100000 || request.HourlyRate < 0
            || request.MaterialUnits < 0 || request.MaterialUnitCost < 0 || request.FixedCost < 0 || request.TravelCost < 0 || request.EquipmentCost < 0 || request.MiscCost < 0)
            return Results.BadRequest(new { status = "invalid_task_details" });
        if ((request.ClearParentTask && request.ParentTaskId.HasValue)
            || (request.ClearRecurrenceRule && request.RecurrenceRule is not null))
            return Results.BadRequest(new { status = "ambiguous_clear_request" });
        if (request.RecurrenceRule is not null)
        {
            if (request.RecurrenceRule.Length > 4000)
                return Results.BadRequest(new { status = "invalid_recurrence_rule" });
            try
            {
                using var recurrence = JsonDocument.Parse(request.RecurrenceRule);
                if (recurrence.RootElement.ValueKind != JsonValueKind.Object)
                    return Results.BadRequest(new { status = "invalid_recurrence_rule" });
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { status = "invalid_recurrence_rule" });
            }
        }

        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, taskId, request.PlanId, access.EffectiveUserId, cancellationToken);
        if (state is null) return Results.NotFound(new { status = "task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, state.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        var assignedEngineerEdit = state.RecordSource == "review_plan" && state.IsAssignedToEffectiveUser && access.CanEditAssignedEstimate && !access.IsViewAs;
        if (!access.CanManage && !assignedEngineerEdit) return WriteForbidden(access);
        if (!access.CanManage && (request.TaskName is not null || request.TaskType is not null || request.Phase is not null
            || request.Priority is not null || request.ParentTaskId.HasValue || request.HourlyRate.HasValue || request.MaterialUnits.HasValue
            || request.MaterialUnitCost.HasValue || request.FixedCost.HasValue || request.TravelCost.HasValue
            || request.EquipmentCost.HasValue || request.MiscCost.HasValue || request.RecurrenceRule is not null
            || request.ClearParentTask || request.ClearRecurrenceRule))
            return Forbidden("MANAGE_PROJECT_FORGE_033");
        if (request.ParentTaskId.HasValue
            && (request.ParentTaskId.Value == state.TaskId
                || !await IsValidParentTaskAsync(connection, transaction, state, request.ParentTaskId.Value, cancellationToken)))
            return Results.BadRequest(new { status = "invalid_parent_task", message = "Choose an active task in the same live project or review plan that is not a descendant of this task." });
        if (request.DurationWorkingDays.HasValue)
        {
            var currentSchedule = await LoadTaskScheduleAsync(connection, transaction, state, cancellationToken);
            if (currentSchedule.Start.HasValue && currentSchedule.Due.HasValue
                && request.DurationWorkingDays.Value != currentSchedule.Duration)
                return Results.BadRequest(new
                {
                    status = "scheduled_duration_requires_schedule_change",
                    message = "A scheduled task's duration is derived from its working-day date range. Use the composite task save with schedule dates."
                });
        }
        if (state.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, state, cancellationToken);
        }
        await LockProjectAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (state.RecordSource == "canonical")
            await PatchCanonicalDetailsAsync(connection, transaction, state, request, access.ActualUserId, cancellationToken);
        else
            await PatchReviewPlanDetailsAsync(connection, transaction, state, request, access.CanManage, access.ActualUserId, cancellationToken);
        var changed = await LockInteractiveTaskAsync(connection, transaction, state.RecordSource, state.TaskId, state.PlanId, access.EffectiveUserId, cancellationToken)
            ?? throw new InvalidOperationException("Updated Project Forge task could not be reloaded.");
        await InvalidatePlanTaskReviewAsync(connection, transaction, changed, access, changed.Revision, "Task details changed after review.", cancellationToken);
        var planRevision = changed.PlanId.HasValue
            ? await LoadPlanRevisionAsync(connection, transaction, changed.PlanId.Value, cancellationToken)
            : null;
        await InsertTaskAuditAsync(connection, transaction, changed, "TASK_DETAILS_UPDATED", access, new { request.ClientMutationId }, cancellationToken);
        await InsertCoalescedTaskNotificationAsync(connection, transaction, changed, access, request.ClientMutationId!, "Task details were updated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var responseTask = await LoadInteractiveTaskResponseAsync(connection, access, changed, cancellationToken);
        return Results.Ok(new { module = "033", status = "task_details_updated", recordSource = changed.RecordSource, task = responseTask, revision = changed.Revision, planRevision, stateChanged = true });
    }

    private static async Task PatchCanonicalDetailsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, InteractiveTaskState state,
        ProjectForgeTaskDetailsPatchRequest request, Guid actor, CancellationToken cancellationToken)
    {
        const string identitySql = """
            UPDATE project_tasks SET
                task_name=COALESCE(NULLIF(@name,''),task_name),
                task_description=CASE WHEN @description IS NULL THEN task_description ELSE @description END,
                updated_by_user_id=@actor,updated_at=NOW()
            WHERE task_id=@task_id
            """;
        await using (var command = new NpgsqlCommand(identitySql, connection, transaction))
        {
            command.Parameters.AddWithValue("task_id", state.TaskId);
            command.Parameters.AddWithValue("name", Clean(request.TaskName, 255, string.Empty));
            command.Parameters.Add(new NpgsqlParameter("description", NpgsqlDbType.Text) { Value = request.Description is null ? DBNull.Value : Clean(request.Description, 4000, string.Empty) });
            command.Parameters.AddWithValue("actor", actor);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string detailSql = """
            INSERT INTO project_forge_task_details(task_id,project_id,task_type,phase_name,priority_code,duration_working_days,
                parent_task_id,estimated_hours,hourly_rate,material_units,material_unit_cost,fixed_cost,travel_cost,equipment_cost,
                miscellaneous_cost,recurrence_rule,source_kind,created_by_user_id,updated_by_user_id)
            VALUES(@task_id,@project_id,COALESCE(@task_type,'variable'),COALESCE(@phase,''),COALESCE(@priority,'normal'),COALESCE(@duration,0),
                @parent,COALESCE(@hours,0),COALESCE(@rate,0),COALESCE(@material_units,0),COALESCE(@material_cost,0),COALESCE(@fixed,0),
                COALESCE(@travel,0),COALESCE(@equipment,0),COALESCE(@misc,0),COALESCE(@recurrence::jsonb,'{}'::jsonb),'pm_created',@actor,@actor)
            ON CONFLICT(task_id) DO UPDATE SET
                task_type=COALESCE(@task_type,project_forge_task_details.task_type),phase_name=COALESCE(@phase,project_forge_task_details.phase_name),
                priority_code=COALESCE(@priority,project_forge_task_details.priority_code),duration_working_days=COALESCE(@duration,project_forge_task_details.duration_working_days),
                parent_task_id=CASE WHEN @clear_parent THEN NULL WHEN @parent IS NOT NULL THEN @parent ELSE project_forge_task_details.parent_task_id END,
                estimated_hours=COALESCE(@hours,project_forge_task_details.estimated_hours),
                hourly_rate=COALESCE(@rate,project_forge_task_details.hourly_rate),material_units=COALESCE(@material_units,project_forge_task_details.material_units),
                material_unit_cost=COALESCE(@material_cost,project_forge_task_details.material_unit_cost),fixed_cost=COALESCE(@fixed,project_forge_task_details.fixed_cost),
                travel_cost=COALESCE(@travel,project_forge_task_details.travel_cost),equipment_cost=COALESCE(@equipment,project_forge_task_details.equipment_cost),
                miscellaneous_cost=COALESCE(@misc,project_forge_task_details.miscellaneous_cost),
                recurrence_rule=CASE WHEN @clear_recurrence THEN '{}'::jsonb WHEN @recurrence IS NULL THEN project_forge_task_details.recurrence_rule ELSE @recurrence::jsonb END,
                updated_by_user_id=@actor,updated_at=NOW()
            """;
        await using var detail = new NpgsqlCommand(detailSql, connection, transaction);
        AddDetailParameters(detail, state, request, actor);
        await detail.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PatchReviewPlanDetailsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, InteractiveTaskState state,
        ProjectForgeTaskDetailsPatchRequest request, bool canManage, Guid actor, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE project_forge_plan_tasks SET
                task_name=COALESCE(NULLIF(@name,''),task_name),task_description=CASE WHEN @description IS NULL THEN task_description ELSE @description END,
                task_type=COALESCE(@task_type,task_type),phase_name=COALESCE(@phase,phase_name),priority_code=COALESCE(@priority,priority_code),
                duration_working_days=COALESCE(@duration,duration_working_days),
                parent_wbs_code=CASE WHEN NOT @can_manage THEN parent_wbs_code WHEN @clear_parent THEN '' WHEN @parent IS NOT NULL THEN COALESCE((SELECT parent.wbs_code FROM project_forge_plan_tasks parent WHERE parent.plan_task_id=@parent AND parent.plan_id=@plan_id),parent_wbs_code) ELSE parent_wbs_code END,
                estimated_hours=COALESCE(@hours,estimated_hours),hourly_rate=COALESCE(@rate,hourly_rate),material_units=COALESCE(@material_units,material_units),
                material_unit_cost=COALESCE(@material_cost,material_unit_cost),fixed_cost=COALESCE(@fixed,fixed_cost),travel_cost=COALESCE(@travel,travel_cost),
                equipment_cost=COALESCE(@equipment,equipment_cost),miscellaneous_cost=COALESCE(@misc,miscellaneous_cost),
                recurrence_rule=CASE WHEN @clear_recurrence THEN '{}'::jsonb WHEN @recurrence IS NULL THEN recurrence_rule ELSE @recurrence::jsonb END,updated_by_user_id=@actor,updated_at=NOW()
            WHERE plan_task_id=@task_id AND plan_id=@plan_id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddDetailParameters(command, state, request, actor);
        command.Parameters.AddWithValue("name", Clean(request.TaskName, 255, string.Empty));
        command.Parameters.Add(new NpgsqlParameter("description", NpgsqlDbType.Text) { Value = request.Description is null ? DBNull.Value : Clean(request.Description, 4000, string.Empty) });
        command.Parameters.AddWithValue("can_manage", canManage);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDetailParameters(NpgsqlCommand command, InteractiveTaskState state, ProjectForgeTaskDetailsPatchRequest request, Guid actor)
    {
        command.Parameters.AddWithValue("task_id", state.TaskId);
        command.Parameters.AddWithValue("project_id", state.ProjectId);
        if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
        command.Parameters.Add(new NpgsqlParameter("task_type", NpgsqlDbType.Text) { Value = request.TaskType is null ? DBNull.Value : Normalize(request.TaskType, "variable", "variable", "recurring") });
        command.Parameters.Add(new NpgsqlParameter("phase", NpgsqlDbType.Text) { Value = request.Phase is null ? DBNull.Value : Clean(request.Phase, 160, string.Empty) });
        command.Parameters.Add(new NpgsqlParameter("priority", NpgsqlDbType.Text) { Value = request.Priority is null ? DBNull.Value : Normalize(request.Priority, "normal", "low", "normal", "high", "critical") });
        command.Parameters.Add(new NpgsqlParameter("duration", NpgsqlDbType.Integer) { Value = request.DurationWorkingDays.HasValue ? request.DurationWorkingDays.Value : DBNull.Value });
        AddNullableUuid(command, "parent", request.ParentTaskId);
        AddNullableNumeric(command, "hours", request.EstimatedHours);
        AddNullableNumeric(command, "rate", request.HourlyRate);
        AddNullableNumeric(command, "material_units", request.MaterialUnits);
        AddNullableNumeric(command, "material_cost", request.MaterialUnitCost);
        AddNullableNumeric(command, "fixed", request.FixedCost);
        AddNullableNumeric(command, "travel", request.TravelCost);
        AddNullableNumeric(command, "equipment", request.EquipmentCost);
        AddNullableNumeric(command, "misc", request.MiscCost);
        command.Parameters.Add(new NpgsqlParameter("recurrence", NpgsqlDbType.Text) { Value = request.RecurrenceRule is null ? DBNull.Value : request.RecurrenceRule });
        command.Parameters.AddWithValue("clear_parent", request.ClearParentTask);
        command.Parameters.AddWithValue("clear_recurrence", request.ClearRecurrenceRule);
        command.Parameters.AddWithValue("actor", actor);
    }

    private static void AddNullableNumeric(NpgsqlCommand command, string name, decimal? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Numeric) { Value = value.HasValue ? value.Value : DBNull.Value });

    private static async Task<bool> IsValidParentTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InteractiveTaskState state,
        Guid parentTaskId,
        CancellationToken cancellationToken)
    {
        var sql = state.RecordSource == "canonical"
            ? """
                WITH RECURSIVE ancestors(task_id,parent_task_id) AS (
                    SELECT task.task_id,detail.parent_task_id
                    FROM project_tasks task
                    LEFT JOIN project_forge_task_details detail ON detail.task_id=task.task_id
                    WHERE task.task_id=@parent AND task.project_id=@project_id AND task.is_active=TRUE
                    UNION
                    SELECT task.task_id,detail.parent_task_id
                    FROM ancestors prior
                    JOIN project_tasks task ON task.task_id=prior.parent_task_id AND task.project_id=@project_id AND task.is_active=TRUE
                    LEFT JOIN project_forge_task_details detail ON detail.task_id=task.task_id
                )
                SELECT EXISTS(SELECT 1 FROM ancestors)
                   AND NOT EXISTS(SELECT 1 FROM ancestors WHERE task_id=@task_id)
                """
            : """
                WITH RECURSIVE ancestors(plan_task_id,parent_wbs_code) AS (
                    SELECT task.plan_task_id,task.parent_wbs_code
                    FROM project_forge_plan_tasks task
                    WHERE task.plan_task_id=@parent AND task.plan_id=@plan_id AND task.canonical_task_id IS NULL
                      AND task.task_status<>'cancelled'
                    UNION
                    SELECT task.plan_task_id,task.parent_wbs_code
                    FROM ancestors prior
                    JOIN project_forge_plan_tasks task ON task.plan_id=@plan_id AND task.wbs_code=prior.parent_wbs_code
                    WHERE task.canonical_task_id IS NULL AND task.task_status<>'cancelled'
                )
                SELECT EXISTS(SELECT 1 FROM ancestors)
                   AND NOT EXISTS(SELECT 1 FROM ancestors WHERE plan_task_id=@task_id)
                """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("parent", parentTaskId);
        command.Parameters.AddWithValue("task_id", state.TaskId);
        command.Parameters.AddWithValue("project_id", state.ProjectId);
        if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
        return (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
    }

    private static async Task<IResult> PatchTaskWorkflowAsync(
        Guid taskId,
        ProjectForgeTaskWorkflowPatchRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId);
        if (envelope.Error is not null) return envelope.Error;
        if (request.BeforeTaskId.HasValue && request.AfterTaskId.HasValue)
            return Results.BadRequest(new { status = "ambiguous_kanban_position" });
        if (request.PercentComplete is < 0 or > 100)
            return Results.BadRequest(new { status = "invalid_percent_complete" });
        var lane = Normalize(request.KanbanCategory, string.Empty, "backlog", "ready", "in_progress", "review", "blocked", "done");
        if (string.IsNullOrEmpty(lane)) return Results.BadRequest(new { status = "invalid_kanban_category" });
        var allowedStatus = LaneStatus(lane, envelope.Source == "review_plan");
        var percent = lane == "done" ? 100m : Math.Clamp(request.PercentComplete ?? 0m, 0m, 100m);
        var blockedReason = lane == "blocked" ? Clean(request.BlockedReason, 2000, "Blocked") : string.Empty;

        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, taskId, request.PlanId, access.EffectiveUserId, cancellationToken);
        if (state is null) return Results.NotFound(new { status = "task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, state.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (!CanManageTask(access, state, true)) return Forbidden("UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033");
        if (state.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, state, cancellationToken);
        }
        await LockProjectAsync(connection, transaction, state.ProjectId, cancellationToken);
        int order;
        try
        {
            order = await ReorderKanbanLaneAsync(connection, transaction, state, lane, request.BeforeTaskId, request.AfterTaskId, access.ActualUserId, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { status = "invalid_kanban_position", message = exception.Message });
        }
        if (state.RecordSource == "canonical")
        {
            const string sql = """
                INSERT INTO project_forge_task_details(
                    task_id,project_id,task_status,kanban_category,percent_complete,blocked_reason,display_order,
                    source_kind,created_by_user_id,updated_by_user_id)
                VALUES(@task_id,@project_id,@status,@lane,@percent,@blocked,@display,'pm_created',@actor,@actor)
                ON CONFLICT(task_id) DO UPDATE SET task_status=@status,kanban_category=@lane,percent_complete=@percent,
                    blocked_reason=@blocked,display_order=@display,updated_by_user_id=@actor,updated_at=NOW()
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddWorkflowParameters(command, state, allowedStatus, lane, percent, blockedReason, order, access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string sql = """
                UPDATE project_forge_plan_tasks SET task_status=@status,kanban_category=@lane,percent_complete=@percent,
                    blocked_reason=@blocked,display_order=@display,updated_by_user_id=@actor,updated_at=NOW()
                WHERE plan_task_id=@task_id AND plan_id=@plan_id
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddWorkflowParameters(command, state, allowedStatus, lane, percent, blockedReason, order, access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var changed = await LockInteractiveTaskAsync(connection, transaction, state.RecordSource, state.TaskId, state.PlanId, access.EffectiveUserId, cancellationToken)
            ?? throw new InvalidOperationException("Updated Project Forge task could not be reloaded.");
        var materialChange = !string.Equals(state.KanbanCategory, lane, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.TaskStatus, allowedStatus, StringComparison.OrdinalIgnoreCase)
            || state.PercentComplete != percent
            || !string.Equals(state.BlockedReason, blockedReason, StringComparison.Ordinal);
        if (materialChange)
            await InvalidatePlanTaskReviewAsync(connection, transaction, changed, access, changed.Revision, "Task workflow changed after review.", cancellationToken);
        else if (changed.RecordSource == "review_plan" && changed.PlanId.HasValue)
        {
            await PreserveCompletedReviewRevisionsAsync(connection, transaction, changed.PlanId.Value, [changed.TaskId], cancellationToken);
            await TouchPlanAsync(connection, transaction, changed.PlanId.Value, access.ActualUserId, cancellationToken);
        }
        var planRevision = changed.PlanId.HasValue
            ? await LoadPlanRevisionAsync(connection, transaction, changed.PlanId.Value, cancellationToken)
            : null;
        await InsertTaskAuditAsync(connection, transaction, changed, "TASK_WORKFLOW_UPDATED", access,
            new { lane, status = allowedStatus, percentComplete = percent, displayOrder = order, materialChange, request.ClientMutationId }, cancellationToken);
        if (materialChange)
            await InsertCoalescedTaskNotificationAsync(connection, transaction, changed, access, request.ClientMutationId!, $"Task moved to {lane.Replace('_', ' ')}.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var responseTask = await LoadInteractiveTaskResponseAsync(connection, access, changed, cancellationToken);
        return Results.Ok(new { module = "033", status = "task_workflow_updated", recordSource = changed.RecordSource, task = responseTask, revision = changed.Revision, planRevision, stateChanged = true });
    }

    private static string LaneStatus(string lane, bool reviewPlan) => lane switch
    {
        "in_progress" or "review" => reviewPlan ? "in_review" : "in_progress",
        "blocked" => "blocked",
        "done" => "completed",
        _ => reviewPlan ? "draft" : "not_started"
    };

    private static void AddWorkflowParameters(NpgsqlCommand command, InteractiveTaskState state, string status, string lane,
        decimal percent, string blockedReason, int order, Guid actor)
    {
        command.Parameters.AddWithValue("task_id", state.TaskId);
        command.Parameters.AddWithValue("project_id", state.ProjectId);
        if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("lane", lane);
        command.Parameters.AddWithValue("percent", percent);
        command.Parameters.AddWithValue("blocked", blockedReason);
        command.Parameters.AddWithValue("display", order);
        command.Parameters.AddWithValue("actor", actor);
    }

    private static async Task<int> ReorderKanbanLaneAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        InteractiveTaskState state,
        string lane,
        Guid? beforeTaskId,
        Guid? afterTaskId,
        Guid actor,
        CancellationToken cancellationToken)
    {
        var sql = state.RecordSource == "canonical"
            ? """
                SELECT task.task_id
                FROM project_tasks task JOIN project_forge_task_details detail ON detail.task_id=task.task_id
                WHERE task.project_id=@project_id AND task.is_active=TRUE AND detail.kanban_category=@lane AND task.task_id<>@task_id
                ORDER BY detail.display_order,task.task_code
                FOR UPDATE OF detail
                """
            : """
                SELECT plan_task_id FROM project_forge_plan_tasks
                WHERE plan_id=@plan_id AND canonical_task_id IS NULL AND task_status<>'cancelled'
                  AND kanban_category=@lane AND plan_task_id<>@task_id
                ORDER BY display_order,wbs_code
                FOR UPDATE
                """;
        var ids = new List<Guid>();
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("project_id", state.ProjectId);
            command.Parameters.AddWithValue("task_id", state.TaskId);
            command.Parameters.AddWithValue("lane", lane);
            if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        }
        var insertion = ids.Count;
        if (beforeTaskId.HasValue)
        {
            insertion = ids.IndexOf(beforeTaskId.Value);
            if (insertion < 0) throw new InvalidOperationException("The requested Kanban before-task is not in the destination lane.");
        }
        else if (afterTaskId.HasValue)
        {
            var index = ids.IndexOf(afterTaskId.Value);
            if (index < 0) throw new InvalidOperationException("The requested Kanban after-task is not in the destination lane.");
            insertion = index + 1;
        }
        ids.Insert(insertion, state.TaskId);
        var updateSql = state.RecordSource == "canonical"
            ? "UPDATE project_forge_task_details SET display_order=@display,updated_by_user_id=@actor,updated_at=NOW() WHERE task_id=@id AND task_id<>@target AND display_order IS DISTINCT FROM @display"
            : "UPDATE project_forge_plan_tasks SET display_order=@display,updated_by_user_id=@actor,updated_at=NOW() WHERE plan_task_id=@id AND plan_task_id<>@target AND display_order IS DISTINCT FROM @display";
        var reorderedNeighbors = new List<Guid>();
        for (var index = 0; index < ids.Count; index++)
        {
            await using var command = new NpgsqlCommand(updateSql, connection, transaction);
            command.Parameters.AddWithValue("id", ids[index]);
            command.Parameters.AddWithValue("target", state.TaskId);
            command.Parameters.AddWithValue("display", index + 1);
            command.Parameters.AddWithValue("actor", actor);
            if (await command.ExecuteNonQueryAsync(cancellationToken) > 0 && ids[index] != state.TaskId)
                reorderedNeighbors.Add(ids[index]);
        }
        if (state.RecordSource == "review_plan" && state.PlanId.HasValue && reorderedNeighbors.Count > 0)
            await PreserveCompletedReviewRevisionsAsync(connection, transaction, state.PlanId.Value, reorderedNeighbors, cancellationToken);
        return insertion + 1;
    }

    private static async Task PreserveCompletedReviewRevisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid planId,
        IReadOnlyCollection<Guid> planTaskIds,
        CancellationToken cancellationToken)
    {
        if (planTaskIds.Count == 0) return;
        const string sql = """
            UPDATE project_forge_plan_assignments assignment
            SET reviewed_task_revision=task.revision_number,updated_at=NOW()
            FROM project_forge_plan_tasks task
            WHERE task.plan_id=@plan_id AND task.plan_task_id=ANY(@task_ids)
              AND assignment.plan_id=task.plan_id AND assignment.plan_task_id=task.plan_task_id
              AND assignment.assignment_type='task_estimator' AND assignment.review_status='completed'
              AND assignment.reviewed_task_revision IS NOT NULL
              AND assignment.reviewed_task_revision IS DISTINCT FROM task.revision_number
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("task_ids", planTaskIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IResult> PatchTaskScheduleAsync(
        Guid taskId,
        ProjectForgeTaskSchedulePatchRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId);
        if (envelope.Error is not null) return envelope.Error;
        var interaction = Normalize(request.Interaction, string.Empty, "move", "resize_start", "resize_end", "set_range");
        if (string.IsNullOrEmpty(interaction)) return Results.BadRequest(new { status = "invalid_schedule_interaction" });
        if (!request.StartDate.HasValue || !request.DueDate.HasValue || request.DueDate < request.StartDate)
            return Results.BadRequest(new { status = "invalid_task_dates", message = "Schedule changes require a valid start and due date." });

        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, taskId, request.PlanId, access.EffectiveUserId, cancellationToken);
        if (state is null) return Results.NotFound(new { status = "task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, state.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (state.RecordSource == "review_plan" && request.CascadeSuccessors)
            return Results.BadRequest(new
            {
                status = "review_plan_schedule_cascade_not_supported",
                message = "Move review-plan successor tasks explicitly so each task retains independent review and revision evidence."
            });
        var assignedEngineerSchedule = state.RecordSource == "review_plan" && state.IsAssignedToEffectiveUser && access.CanEditAssignedEstimate && !access.IsViewAs;
        if (!access.CanManage && !assignedEngineerSchedule) return WriteForbidden(access);
        if (state.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, state, cancellationToken);
        }
        await LockProjectAsync(connection, transaction, state.ProjectId, cancellationToken);
        var priorSchedule = await LoadTaskScheduleAsync(connection, transaction, state, cancellationToken);
        var authoritativeStart = request.StartDate.Value;
        var authoritativeDue = request.DueDate.Value;
        if (!await IsWorkingDayAsync(connection, transaction, authoritativeStart, cancellationToken)
            || (interaction != "move" && !await IsWorkingDayAsync(connection, transaction, authoritativeDue, cancellationToken)))
            return Results.BadRequest(new { status = "task_date_not_working_day", message = "Task start and due dates must be configured working days and cannot be company holidays." });
        var duration = await WorkingDayDurationAsync(connection, transaction, authoritativeStart, authoritativeDue, cancellationToken);
        if (interaction == "move")
        {
            var preservedDuration = priorSchedule.Duration > 0
                ? priorSchedule.Duration
                : priorSchedule.Start.HasValue && priorSchedule.Due.HasValue
                    ? await WorkingDayDurationAsync(connection, transaction, priorSchedule.Start.Value, priorSchedule.Due.Value, cancellationToken)
                    : duration;
            duration = Math.Max(1, preservedDuration);
            authoritativeDue = await AddWorkingDaysAsync(connection, transaction, authoritativeStart, duration - 1, cancellationToken);
        }
        if (duration is < 1 or > 730)
            return Results.BadRequest(new { status = "invalid_working_day_range", message = "A scheduled Project Forge task must contain between 1 and 730 configured working days." });
        var workingDelta = priorSchedule.Start.HasValue
            ? await WorkingDayDeltaAsync(connection, transaction, priorSchedule.Start.Value, authoritativeStart, cancellationToken)
            : 0;
        if (state.RecordSource == "canonical")
        {
            const string sql = """
                INSERT INTO project_forge_task_details(
                    task_id,project_id,planned_start_date,planned_end_date,duration_working_days,source_kind,created_by_user_id,updated_by_user_id)
                VALUES(@task_id,@project_id,@start,@due,@duration,'pm_created',@actor,@actor)
                ON CONFLICT(task_id) DO UPDATE SET planned_start_date=@start,planned_end_date=@due,duration_working_days=@duration,
                    updated_by_user_id=@actor,updated_at=NOW()
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddScheduleParameters(command, state, authoritativeStart, authoritativeDue, duration, access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string sql = """
                UPDATE project_forge_plan_tasks SET planned_start_date=@start,planned_end_date=@due,duration_working_days=@duration,
                    updated_by_user_id=@actor,updated_at=NOW()
                WHERE plan_task_id=@task_id AND plan_id=@plan_id
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddScheduleParameters(command, state, authoritativeStart, authoritativeDue, duration, access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var cascaded = request.CascadeSuccessors && interaction == "move" && workingDelta != 0
            ? await CascadeSuccessorDatesAsync(connection, transaction, state, workingDelta, access.ActualUserId, cancellationToken)
            : 0;
        var dependencyViolations = await LoadDependencyViolationsAsync(connection, transaction, state, cancellationToken);
        if (dependencyViolations.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new
            {
                status = "task_dependency_constraint_violation",
                message = "The schedule change would violate one or more predecessor constraints.",
                dependencyIds = dependencyViolations
            });
        }
        var changed = await LockInteractiveTaskAsync(connection, transaction, state.RecordSource, state.TaskId, state.PlanId, access.EffectiveUserId, cancellationToken)
            ?? throw new InvalidOperationException("Updated Project Forge task could not be reloaded.");
        await InvalidatePlanTaskReviewAsync(connection, transaction, changed, access, changed.Revision, "Task dates changed after review.", cancellationToken);
        var planRevision = changed.PlanId.HasValue
            ? await LoadPlanRevisionAsync(connection, transaction, changed.PlanId.Value, cancellationToken)
            : null;
        await InsertTaskAuditAsync(connection, transaction, changed, "TASK_SCHEDULE_UPDATED", access,
            new { interaction, startDate = authoritativeStart, dueDate = authoritativeDue, durationWorkingDays = duration, request.CascadeSuccessors, cascaded, request.ClientMutationId }, cancellationToken);
        await InsertCoalescedTaskNotificationAsync(connection, transaction, changed, access, request.ClientMutationId!, "Task schedule was updated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var responseTask = await LoadInteractiveTaskResponseAsync(connection, access, changed, cancellationToken);
        return Results.Ok(new { module = "033", status = "task_schedule_updated", recordSource = changed.RecordSource, task = responseTask, revision = changed.Revision, planRevision, cascadedSuccessorCount = cascaded, stateChanged = true });
    }

    private static void AddScheduleParameters(NpgsqlCommand command, InteractiveTaskState state, DateOnly start, DateOnly due, int duration, Guid actor)
    {
        command.Parameters.AddWithValue("task_id", state.TaskId);
        command.Parameters.AddWithValue("project_id", state.ProjectId);
        if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("due", due);
        command.Parameters.AddWithValue("duration", duration);
        command.Parameters.AddWithValue("actor", actor);
    }

    private static async Task<(DateOnly? Start, DateOnly? Due, int Duration)> LoadTaskScheduleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, InteractiveTaskState state, CancellationToken cancellationToken)
    {
        var sql = state.RecordSource == "canonical"
            ? "SELECT planned_start_date,planned_end_date,duration_working_days FROM project_forge_task_details WHERE task_id=@task_id"
            : "SELECT planned_start_date,planned_end_date,COALESCE(duration_working_days,0) FROM project_forge_plan_tasks WHERE plan_task_id=@task_id AND plan_id=@plan_id";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("task_id", state.TaskId);
        if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return (null, null, 0);
        return (ReadDate(reader, 0), ReadDate(reader, 1), reader.GetInt32(2));
    }

    private static async Task<DateOnly> AddWorkingDaysAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DateOnly start, int days, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT projectpulse073_add_working_days(@start,@days)", connection, transaction);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("days", days);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DateOnly date ? date : DateOnly.FromDateTime((DateTime)value!);
    }

    private static async Task<bool> IsWorkingDayAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DateOnly date, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT projectpulse073_is_working_day(@date)", connection, transaction);
        command.Parameters.AddWithValue("date", date);
        return (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
    }

    private static async Task<int> WorkingDayDeltaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DateOnly oldDate, DateOnly newDate, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT projectpulse073_working_day_delta(@old,@new)", connection, transaction);
        command.Parameters.AddWithValue("old", oldDate);
        command.Parameters.AddWithValue("new", newDate);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<int> WorkingDayDurationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DateOnly start, DateOnly due, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT projectpulse073_working_day_duration(@start,@due)", connection, transaction);
        command.Parameters.AddWithValue("start", start);
        command.Parameters.AddWithValue("due", due);
        return Math.Max(0, Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)));
    }

    private static async Task<int> CascadeSuccessorDatesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, InteractiveTaskState state,
        int workingDelta, Guid actor, CancellationToken cancellationToken)
    {
        var sql = state.RecordSource == "canonical"
            ? """
                WITH RECURSIVE successors(task_id) AS (
                    SELECT dependency.successor_task_id FROM project_task_dependencies dependency WHERE dependency.predecessor_task_id=@task_id
                    UNION
                    SELECT dependency.successor_task_id FROM project_task_dependencies dependency JOIN successors prior ON prior.task_id=dependency.predecessor_task_id
                )
                UPDATE project_forge_task_details detail
                SET planned_start_date=projectpulse073_add_working_days(detail.planned_start_date,@delta),
                    planned_end_date=projectpulse073_add_working_days(detail.planned_end_date,@delta),updated_by_user_id=@actor,updated_at=NOW()
                WHERE detail.task_id IN (SELECT task_id FROM successors)
                  AND (detail.planned_start_date IS NOT NULL OR detail.planned_end_date IS NOT NULL)
                """
            : """
                WITH RECURSIVE successors(task_id) AS (
                    SELECT dependency.successor_plan_task_id FROM project_forge_task_dependencies dependency
                    WHERE dependency.plan_id=@plan_id AND dependency.predecessor_plan_task_id=@task_id
                    UNION
                    SELECT dependency.successor_plan_task_id FROM project_forge_task_dependencies dependency
                    JOIN successors prior ON prior.task_id=dependency.predecessor_plan_task_id WHERE dependency.plan_id=@plan_id
                )
                UPDATE project_forge_plan_tasks task
                SET planned_start_date=projectpulse073_add_working_days(task.planned_start_date,@delta),
                    planned_end_date=projectpulse073_add_working_days(task.planned_end_date,@delta),updated_by_user_id=@actor,updated_at=NOW()
                WHERE task.plan_id=@plan_id AND task.plan_task_id IN (SELECT task_id FROM successors)
                  AND (task.planned_start_date IS NOT NULL OR task.planned_end_date IS NOT NULL)
                """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("task_id", state.TaskId);
        command.Parameters.AddWithValue("delta", workingDelta);
        command.Parameters.AddWithValue("actor", actor);
        if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<Guid>> LoadDependencyViolationsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        InteractiveTaskState state, CancellationToken cancellationToken)
    {
        var sql = state.RecordSource == "canonical"
            ? """
                SELECT dependency.project_task_dependency_id
                FROM project_task_dependencies dependency
                JOIN project_forge_task_details predecessor ON predecessor.task_id=dependency.predecessor_task_id
                JOIN project_forge_task_details successor ON successor.task_id=dependency.successor_task_id
                WHERE dependency.project_id=@project_id AND CASE dependency.dependency_type
                    WHEN 'FS' THEN predecessor.planned_end_date IS NOT NULL AND successor.planned_start_date IS NOT NULL
                        AND projectpulse073_add_working_days(predecessor.planned_end_date,dependency.lag_working_days+1)>successor.planned_start_date
                    WHEN 'SS' THEN predecessor.planned_start_date IS NOT NULL AND successor.planned_start_date IS NOT NULL
                        AND projectpulse073_add_working_days(predecessor.planned_start_date,dependency.lag_working_days)>successor.planned_start_date
                    WHEN 'FF' THEN predecessor.planned_end_date IS NOT NULL AND successor.planned_end_date IS NOT NULL
                        AND projectpulse073_add_working_days(predecessor.planned_end_date,dependency.lag_working_days)>successor.planned_end_date
                    WHEN 'SF' THEN predecessor.planned_start_date IS NOT NULL AND successor.planned_end_date IS NOT NULL
                        AND projectpulse073_add_working_days(predecessor.planned_start_date,dependency.lag_working_days)>successor.planned_end_date
                    ELSE FALSE END
                """
            : """
                SELECT dependency.dependency_id
                FROM project_forge_task_dependencies dependency
                JOIN project_forge_plan_tasks predecessor ON predecessor.plan_task_id=dependency.predecessor_plan_task_id
                JOIN project_forge_plan_tasks successor ON successor.plan_task_id=dependency.successor_plan_task_id
                WHERE dependency.plan_id=@plan_id AND CASE dependency.dependency_type
                    WHEN 'FS' THEN predecessor.planned_end_date IS NOT NULL AND successor.planned_start_date IS NOT NULL
                        AND projectpulse073_add_working_days(predecessor.planned_end_date,dependency.lag_working_days+1)>successor.planned_start_date
                    WHEN 'SS' THEN predecessor.planned_start_date IS NOT NULL AND successor.planned_start_date IS NOT NULL
                        AND projectpulse073_add_working_days(predecessor.planned_start_date,dependency.lag_working_days)>successor.planned_start_date
                    WHEN 'FF' THEN predecessor.planned_end_date IS NOT NULL AND successor.planned_end_date IS NOT NULL
                        AND projectpulse073_add_working_days(predecessor.planned_end_date,dependency.lag_working_days)>successor.planned_end_date
                    WHEN 'SF' THEN predecessor.planned_start_date IS NOT NULL AND successor.planned_end_date IS NOT NULL
                        AND projectpulse073_add_working_days(predecessor.planned_start_date,dependency.lag_working_days)>successor.planned_end_date
                    ELSE FALSE END
                """;
        var ids = new List<Guid>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", state.ProjectId);
        if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static async Task<IResult> PatchTaskDecisionAsync(
        Guid taskId,
        ProjectForgeTaskDecisionPatchRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId);
        if (envelope.Error is not null) return envelope.Error;
        var decision = Normalize(request.DecisionAction, string.Empty, "none", "do", "delegate", "decide", "delete");
        if (string.IsNullOrEmpty(decision)) return Results.BadRequest(new { status = "invalid_decision_action" });
        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, taskId, request.PlanId, access.EffectiveUserId, cancellationToken);
        if (state is null) return Results.NotFound(new { status = "task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, state.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (!CanManageTask(access, state, false)) return WriteForbidden(access);
        if (state.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, state, cancellationToken);
        }
        await LockProjectAsync(connection, transaction, state.ProjectId, cancellationToken);
        var sql = state.RecordSource == "canonical"
            ? """
                INSERT INTO project_forge_task_details(task_id,project_id,decision_action,is_important,is_urgent,source_kind,created_by_user_id,updated_by_user_id)
                VALUES(@task_id,@project_id,@decision,@important,@urgent,'pm_created',@actor,@actor)
                ON CONFLICT(task_id) DO UPDATE SET decision_action=@decision,is_important=@important,is_urgent=@urgent,updated_by_user_id=@actor,updated_at=NOW()
                """
            : """
                UPDATE project_forge_plan_tasks SET decision_action=@decision,is_important=@important,is_urgent=@urgent,updated_by_user_id=@actor,updated_at=NOW()
                WHERE plan_task_id=@task_id AND plan_id=@plan_id
                """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("task_id", state.TaskId);
            command.Parameters.AddWithValue("project_id", state.ProjectId);
            if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
            command.Parameters.AddWithValue("decision", decision);
            command.Parameters.AddWithValue("important", request.Important);
            command.Parameters.AddWithValue("urgent", request.Urgent);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var changed = await LockInteractiveTaskAsync(connection, transaction, state.RecordSource, state.TaskId, state.PlanId, access.EffectiveUserId, cancellationToken)
            ?? throw new InvalidOperationException("Updated Project Forge task could not be reloaded.");
        await InvalidatePlanTaskReviewAsync(connection, transaction, changed, access, changed.Revision, "Task decision classification changed after review.", cancellationToken);
        var planRevision = changed.PlanId.HasValue
            ? await LoadPlanRevisionAsync(connection, transaction, changed.PlanId.Value, cancellationToken)
            : null;
        await InsertTaskAuditAsync(connection, transaction, changed, "TASK_DECISION_UPDATED", access, new { decision, request.Important, request.Urgent, request.ClientMutationId }, cancellationToken);
        await InsertCoalescedTaskNotificationAsync(connection, transaction, changed, access, request.ClientMutationId!, "Task decision classification was updated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var responseTask = await LoadInteractiveTaskResponseAsync(connection, access, changed, cancellationToken);
        return Results.Ok(new { module = "033", status = "task_decision_updated", recordSource = changed.RecordSource, task = responseTask, revision = changed.Revision, planRevision, stateChanged = true });
    }

    private static Task<IResult?> EnsureProjectWritableAsync(NpgsqlConnection connection, Guid projectId, CancellationToken cancellationToken)
        => EnsureProjectWritableAsync(connection, null, projectId, cancellationToken);

    private static async Task<IResult?> EnsureProjectWritableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(status,'') FROM projects WHERE project_id=@project_id FOR SHARE
            """;
        await using var command = transaction is null
            ? new NpgsqlCommand(sql, connection)
            : new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        var status = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (status is null) return Results.NotFound(new { status = "project_not_found" });
        if (new[] { "closed", "cancelled", "canceled", "inactive", "complete", "completed", "done", "archived" }.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
            return Results.Conflict(new { status = "project_not_open_for_task_changes", projectStatus = status });
        return null;
    }

    private static async Task<IResult> PutTaskAssigneeAsync(
        Guid taskId,
        ProjectForgeTaskAssigneePutRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId);
        if (envelope.Error is not null) return envelope.Error;
        if (request.UserId == Guid.Empty || request.AssignedHours is < 0 or > 100000 || request.AllocationPercent is <= 0 or > 100)
            return Results.BadRequest(new { status = "invalid_task_assignment" });
        if (request.StartDate.HasValue && request.EndDate.HasValue && request.EndDate < request.StartDate)
            return Results.BadRequest(new { status = "invalid_assignment_dates" });

        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, taskId, request.PlanId, access.EffectiveUserId, cancellationToken);
        if (state is null) return Results.NotFound(new { status = "task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, state.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        if (state.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, state, cancellationToken);
        }
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (!await IsEligibleEngineerReviewerAsync(connection, state.ProjectId, request.UserId, cancellationToken))
            return Results.BadRequest(new { status = "assignee_not_on_project", message = "Choose an active Engineer already assigned to this project." });
        await LockProjectAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (state.RecordSource == "canonical")
        {
            const string demoteSql = "UPDATE project_assignments SET is_primary_assignee=FALSE,updated_by_user_id=@actor WHERE task_id=@task_id AND is_primary_assignee=TRUE";
            await using (var demote = new NpgsqlCommand(demoteSql, connection, transaction))
            {
                demote.Parameters.AddWithValue("task_id", state.TaskId);
                demote.Parameters.AddWithValue("actor", access.ActualUserId);
                await demote.ExecuteNonQueryAsync(cancellationToken);
            }
            const string updateSql = """
                UPDATE project_assignments SET assigned_hours=@hours,allocation_percent=@allocation,
                    effective_start_date=COALESCE(@start,effective_start_date),effective_end_date=@end,
                    is_primary_assignee=TRUE,updated_by_user_id=@actor,updated_at=NOW()
                WHERE project_assignment_id=(
                    SELECT project_assignment_id FROM project_assignments
                    WHERE project_id=@project_id AND task_id=@task_id AND user_id=@user_id
                    ORDER BY is_primary_assignee DESC,effective_start_date DESC LIMIT 1
                )
                """;
            int changedRows;
            await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
            {
                AddAssignmentParameters(update, state, request, access.ActualUserId);
                changedRows = await update.ExecuteNonQueryAsync(cancellationToken);
            }
            if (changedRows == 0)
            {
                const string insertSql = """
                    INSERT INTO project_assignments(
                        project_assignment_id,project_id,task_id,user_id,assigned_by_user_id,effective_start_date,effective_end_date,
                        allocation_percent,assigned_hours,is_primary_assignee,revision_number,updated_by_user_id,updated_at)
                    VALUES(gen_random_uuid(),@project_id,@task_id,@user_id,@actor,COALESCE(@start,CURRENT_DATE),@end,@allocation,@hours,TRUE,1,@actor,NOW())
                    """;
                await using var insert = new NpgsqlCommand(insertSql, connection, transaction);
                AddAssignmentParameters(insert, state, request, access.ActualUserId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var touch = new NpgsqlCommand("UPDATE project_tasks SET updated_by_user_id=@actor,updated_at=NOW() WHERE task_id=@task_id", connection, transaction);
            touch.Parameters.AddWithValue("actor", access.ActualUserId);
            touch.Parameters.AddWithValue("task_id", state.TaskId);
            await touch.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string reassignSql = """
                UPDATE project_forge_plan_assignments
                SET review_status='reassigned',completed_at=NULL,reviewed_task_revision=NULL,updated_at=NOW()
                WHERE plan_id=@plan_id AND plan_task_id=@task_id AND assignment_type='task_estimator'
                  AND user_id<>@user_id AND review_status<>'reassigned'
                """;
            await using (var reassign = new NpgsqlCommand(reassignSql, connection, transaction))
            {
                reassign.Parameters.AddWithValue("plan_id", state.PlanId!.Value);
                reassign.Parameters.AddWithValue("task_id", state.TaskId);
                reassign.Parameters.AddWithValue("user_id", request.UserId);
                await reassign.ExecuteNonQueryAsync(cancellationToken);
            }
            const string taskSql = "UPDATE project_forge_plan_tasks SET reviewer_user_id=@user_id,updated_by_user_id=@actor,updated_at=NOW() WHERE plan_task_id=@task_id AND plan_id=@plan_id";
            await using (var taskCommand = new NpgsqlCommand(taskSql, connection, transaction))
            {
                taskCommand.Parameters.AddWithValue("user_id", request.UserId);
                taskCommand.Parameters.AddWithValue("actor", access.ActualUserId);
                taskCommand.Parameters.AddWithValue("task_id", state.TaskId);
                taskCommand.Parameters.AddWithValue("plan_id", state.PlanId!.Value);
                await taskCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            const string assignmentSql = """
                INSERT INTO project_forge_plan_assignments(
                    plan_assignment_id,plan_id,plan_task_id,project_id,user_id,assignment_type,planned_hours,allocation_percent,
                    review_status,assignment_notes,assigned_by_user_id,reviewed_task_revision)
                VALUES(gen_random_uuid(),@plan_id,@task_id,@project_id,@user_id,'task_estimator',@hours,@allocation,'assigned','',@actor,NULL)
                ON CONFLICT(plan_task_id,user_id,assignment_type) DO UPDATE SET planned_hours=@hours,allocation_percent=@allocation,
                    review_status='assigned',completed_at=NULL,reviewed_task_revision=NULL,assigned_by_user_id=@actor,updated_at=NOW()
                """;
            await using var assignment = new NpgsqlCommand(assignmentSql, connection, transaction);
            AddAssignmentParameters(assignment, state, request, access.ActualUserId);
            await assignment.ExecuteNonQueryAsync(cancellationToken);
            await SetPlanInReviewAsync(connection, transaction, state.PlanId.Value, access.ActualUserId, cancellationToken);
        }
        var changed = await LockInteractiveTaskAsync(connection, transaction, state.RecordSource, state.TaskId, state.PlanId, access.EffectiveUserId, cancellationToken)
            ?? throw new InvalidOperationException("Assigned Project Forge task could not be reloaded.");
        if (changed.RecordSource == "canonical")
            await InvalidatePlanTaskReviewAsync(connection, transaction, changed, access, changed.Revision, "The task assignee changed after review.", cancellationToken);
        await InsertTaskAuditAsync(connection, transaction, changed, "TASK_ASSIGNEE_UPDATED", access, new { request.UserId, request.AssignedHours, request.AllocationPercent, request.ClientMutationId }, cancellationToken);
        var assigneeName = await LoadUserNameAsync(connection, request.UserId, cancellationToken);
        await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.TaskAssignedPolicy, state.ProjectId, request.UserId,
            $"task:{state.RecordSource}:{state.TaskId}:assignee:{request.UserId}:v{changed.Revision}",
            new { taskId = state.RecordSource == "canonical" ? state.TaskId : (Guid?)null, planTaskId = state.RecordSource == "review_plan" ? state.TaskId : (Guid?)null, state.PlanId, taskName = state.TaskName, assignedUserId = request.UserId, assigneeName }, cancellationToken);
        var planRevision = changed.PlanId.HasValue
            ? await LoadPlanRevisionAsync(connection, transaction, changed.PlanId.Value, cancellationToken)
            : null;
        await transaction.CommitAsync(cancellationToken);
        var responseTask = await LoadInteractiveTaskResponseAsync(connection, access, changed, cancellationToken);
        return Results.Ok(new { module = "033", status = "task_assignee_updated", recordSource = changed.RecordSource, task = responseTask, revision = changed.Revision, planRevision, stateChanged = true });
    }

    private static void AddAssignmentParameters(NpgsqlCommand command, InteractiveTaskState state, ProjectForgeTaskAssigneePutRequest request, Guid actor)
    {
        command.Parameters.AddWithValue("project_id", state.ProjectId);
        command.Parameters.AddWithValue("task_id", state.TaskId);
        if (state.PlanId.HasValue) command.Parameters.AddWithValue("plan_id", state.PlanId.Value);
        command.Parameters.AddWithValue("user_id", request.UserId);
        command.Parameters.AddWithValue("hours", request.AssignedHours);
        command.Parameters.AddWithValue("allocation", request.AllocationPercent);
        AddNullableDate(command, "start", request.StartDate);
        AddNullableDate(command, "end", request.EndDate);
        command.Parameters.AddWithValue("actor", actor);
    }

    private static async Task<IResult> ArchiveTaskAsync(
        Guid taskId,
        ProjectForgeTaskArchiveRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId);
        if (envelope.Error is not null) return envelope.Error;
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 3)
            return Results.BadRequest(new { status = "archive_reason_required" });
        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, taskId, request.PlanId, access.EffectiveUserId, cancellationToken);
        if (state is null) return Results.NotFound(new { status = "task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, state.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        if (state.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, state, cancellationToken);
        }
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        await LockProjectAsync(connection, transaction, state.ProjectId, cancellationToken);
        var relationshipSql = state.RecordSource == "canonical"
            ? """
                SELECT EXISTS(
                    SELECT 1 FROM project_task_dependencies dependency
                    WHERE dependency.predecessor_task_id=@task_id OR dependency.successor_task_id=@task_id
                ) OR EXISTS(
                    SELECT 1 FROM project_forge_task_details child
                    JOIN project_tasks child_task ON child_task.task_id=child.task_id AND child_task.is_active=TRUE
                    WHERE child.parent_task_id=@task_id
                )
                """
            : """
                SELECT EXISTS(
                    SELECT 1 FROM project_forge_task_dependencies dependency
                    WHERE dependency.plan_id=@plan_id
                      AND (dependency.predecessor_plan_task_id=@task_id OR dependency.successor_plan_task_id=@task_id)
                ) OR EXISTS(
                    SELECT 1 FROM project_forge_plan_tasks child
                    JOIN project_forge_plan_tasks parent ON parent.plan_task_id=@task_id AND parent.plan_id=child.plan_id
                    WHERE child.plan_id=@plan_id AND child.canonical_task_id IS NULL AND child.task_status<>'cancelled'
                      AND child.parent_wbs_code=parent.wbs_code
                )
                """;
        await using (var relationships = new NpgsqlCommand(relationshipSql, connection, transaction))
        {
            relationships.Parameters.AddWithValue("task_id", state.TaskId);
            if (state.PlanId.HasValue) relationships.Parameters.AddWithValue("plan_id", state.PlanId.Value);
            if ((bool?)await relationships.ExecuteScalarAsync(cancellationToken) == true)
                return Results.Conflict(new
                {
                    status = "task_has_active_relationships",
                    message = "Remove this task's dependencies and re-parent its child tasks before archiving it."
                });
        }
        if (state.RecordSource == "canonical")
        {
            await using (var timerTable = new NpgsqlCommand("SELECT to_regclass('public.module001_timer_sessions') IS NOT NULL", connection, transaction))
            {
                if ((bool?)await timerTable.ExecuteScalarAsync(cancellationToken) == true)
                {
                    const string timerSql = """
                        SELECT EXISTS(
                            SELECT 1 FROM module001_timer_sessions timer
                            WHERE timer.assignment_id IN (
                                SELECT assignment.project_assignment_id FROM project_assignments assignment WHERE assignment.task_id=@task_id
                            ) AND timer.timer_status='RUNNING'
                        )
                        """;
                    await using var runningTimer = new NpgsqlCommand(timerSql, connection, transaction);
                    runningTimer.Parameters.AddWithValue("task_id", state.TaskId);
                    if ((bool?)await runningTimer.ExecuteScalarAsync(cancellationToken) == true)
                        return Results.Conflict(new { status = "task_timer_running", message = "Stop every active timer for this task before archiving it." });
                }
            }
            await using (var evidence = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM time_entries WHERE task_id=@task_id)", connection, transaction))
            {
                evidence.Parameters.AddWithValue("task_id", state.TaskId);
                if ((bool?)await evidence.ExecuteScalarAsync(cancellationToken) == true)
                    return Results.Conflict(new { status = "task_has_time_evidence", message = "A task with recorded time cannot be archived from Project Forge because it must remain available to authoritative time and billing history." });
            }
            const string sql = """
                UPDATE project_tasks SET is_active=FALSE,updated_by_user_id=@actor,updated_at=NOW() WHERE task_id=@task_id;
                UPDATE project_forge_task_details SET task_status='cancelled',updated_by_user_id=@actor,updated_at=NOW() WHERE task_id=@task_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("task_id", state.TaskId);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var command = new NpgsqlCommand("UPDATE project_forge_plan_tasks SET task_status='cancelled',kanban_category='done',updated_by_user_id=@actor,updated_at=NOW() WHERE plan_task_id=@task_id AND plan_id=@plan_id", connection, transaction);
            command.Parameters.AddWithValue("task_id", state.TaskId);
            command.Parameters.AddWithValue("plan_id", state.PlanId!.Value);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var canonicalDetailChanged = state.RecordSource == "canonical" && state.PlanningRevision > 0;
        var newRevision = state.RecordSource == "canonical" ? state.Revision + 1 + (canonicalDetailChanged ? 1 : 0) : state.Revision + 1;
        var changed = state with
        {
            Revision = newRevision,
            TaskRevision = state.TaskRevision + (state.RecordSource == "canonical" ? 1 : 0),
            PlanningRevision = state.PlanningRevision + (state.RecordSource == "review_plan" || canonicalDetailChanged ? 1 : 0)
        };
        await InvalidatePlanTaskReviewAsync(connection, transaction, changed, access, newRevision, "The task was cancelled after review.", cancellationToken);
        var planRevision = changed.PlanId.HasValue
            ? await LoadPlanRevisionAsync(connection, transaction, changed.PlanId.Value, cancellationToken)
            : null;
        await InsertTaskAuditAsync(connection, transaction, changed, "TASK_ARCHIVED", access, new { request.Reason, request.ClientMutationId }, cancellationToken);
        await InsertCoalescedTaskNotificationAsync(connection, transaction, changed, access, request.ClientMutationId!, "Task was archived/cancelled.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new
        {
            module = "033",
            status = state.RecordSource == "canonical" ? "canonical_task_archived" : "review_plan_task_archived",
            recordSource = state.RecordSource,
            task = new { taskId = state.RecordSource == "canonical" ? state.TaskId : (Guid?)null, planTaskId = state.RecordSource == "review_plan" ? state.TaskId : (Guid?)null, state.PlanId, taskName = state.TaskName, status = "cancelled", isActive = state.RecordSource != "canonical", revision = newRevision },
            revision = newRevision,
            planRevision,
            stateChanged = true
        });
    }

    private sealed record InteractiveDependencyState(Guid DependencyId, Guid ProjectId, Guid? PlanId, string RecordSource,
        Guid PredecessorTaskId, Guid SuccessorTaskId, string DependencyType, int LagWorkingDays, int Revision);

    private static async Task<IResult> CreateTaskDependencyAsync(
        Guid projectId,
        ProjectForgeTaskDependencySaveRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var requestedSource = request.RecordSource?.Trim().ToLowerInvariant();
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId,
            requireRevision: requestedSource == "review_plan");
        if (envelope.Error is not null) return envelope.Error;
        if (request.PredecessorTaskId == Guid.Empty || request.SuccessorTaskId == Guid.Empty || request.PredecessorTaskId == request.SuccessorTaskId)
            return Results.BadRequest(new { status = "invalid_task_dependency" });
        var type = Normalize(request.DependencyType, "FS", "FS", "SS", "FF", "SF").ToUpperInvariant();
        if (request.LagWorkingDays is < -365 or > 365)
            return Results.BadRequest(new { status = "invalid_dependency_lag", allowedRange = new { minimum = -365, maximum = 365 } });
        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);
        if (!await CanAccessProjectAsync(connection, access, projectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, projectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockProjectAsync(connection, transaction, projectId, cancellationToken);
        projectWriteError = await EnsureProjectWritableAsync(connection, transaction, projectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (!await DependencyTasksBelongToScopeAsync(connection, transaction, envelope.Source!, projectId, request.PlanId,
                request.PredecessorTaskId, request.SuccessorTaskId, cancellationToken))
            return Results.BadRequest(new { status = "dependency_task_scope_mismatch" });
        var successor = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, request.SuccessorTaskId, request.PlanId, access.EffectiveUserId, cancellationToken)
            ?? throw new InvalidOperationException("Dependency successor could not be loaded.");
        if (envelope.Source == "review_plan" && successor.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, successor, cancellationToken);
        }
        if (await WouldCreateDependencyCycleAsync(connection, transaction, envelope.Source!, request.PlanId, request.PredecessorTaskId, request.SuccessorTaskId, null, cancellationToken))
            return Results.BadRequest(new { status = "dependency_cycle_detected" });
        var dependencyId = Guid.NewGuid();
        var sql = envelope.Source == "canonical"
            ? """
                INSERT INTO project_task_dependencies(
                    project_task_dependency_id,project_id,predecessor_task_id,successor_task_id,dependency_type,lag_working_days,
                    created_by_user_id,updated_by_user_id)
                VALUES(@id,@project_id,@predecessor,@successor,@type,@lag,@actor,@actor)
                """
            : """
                INSERT INTO project_forge_task_dependencies(
                    dependency_id,plan_id,project_id,predecessor_plan_task_id,successor_plan_task_id,dependency_type,lag_working_days,
                    created_by_user_id,updated_by_user_id)
                VALUES(@id,@plan_id,@project_id,@predecessor,@successor,@type,@lag,@actor,@actor)
                """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            AddDependencyParameters(command, dependencyId, projectId, request.PlanId, request.PredecessorTaskId, request.SuccessorTaskId, type, request.LagWorkingDays, access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var dependencyViolations = await LoadDependencyViolationsAsync(connection, transaction, successor, cancellationToken);
        if (dependencyViolations.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new
            {
                status = "task_dependency_constraint_violation",
                message = "The dependency would conflict with the current task schedule.",
                dependencyIds = dependencyViolations
            });
        }
        await InvalidatePlanTaskReviewAsync(connection, transaction, successor, access, successor.Revision, "A dependency changed after review.", cancellationToken);
        var planRevision = successor.PlanId.HasValue
            ? await LoadPlanRevisionAsync(connection, transaction, successor.PlanId.Value, cancellationToken)
            : null;
        await InsertTaskAuditAsync(connection, transaction, successor, "TASK_DEPENDENCY_CREATED", access,
            new { dependencyId, request.PredecessorTaskId, request.SuccessorTaskId, type, request.LagWorkingDays, request.ClientMutationId }, cancellationToken);
        await InsertCoalescedTaskNotificationAsync(connection, transaction, successor, access, request.ClientMutationId!, "A task dependency was added.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var dependency = new { dependencyId, planId = request.PlanId, projectId, predecessorTaskId = request.PredecessorTaskId, successorTaskId = request.SuccessorTaskId, dependencyType = type, lagWorkingDays = request.LagWorkingDays, recordSource = envelope.Source, revision = 1 };
        return Results.Created($"/api/project-forge/task-dependencies/{dependencyId}", new { module = "033", status = "task_dependency_created", recordSource = envelope.Source, dependency, revision = 1, planRevision, stateChanged = true });
    }

    private static async Task<IResult> UpdateTaskDependencyAsync(
        Guid dependencyId,
        ProjectForgeTaskDependencySaveRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
        => await MutateDependencyAsync(dependencyId, request, context, delete: false, cancellationToken);

    private static async Task<IResult> DeleteTaskDependencyAsync(
        Guid dependencyId,
        ProjectForgeTaskDependencySaveRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
        => await MutateDependencyAsync(dependencyId, request, context, delete: true, cancellationToken);

    private static async Task<IResult> MutateDependencyAsync(
        Guid dependencyId,
        ProjectForgeTaskDependencySaveRequest request,
        HttpContext context,
        bool delete,
        CancellationToken cancellationToken)
    {
        var envelope = ValidateTaskMutation(request.RecordSource, request.PlanId, request.ExpectedRevision, request.ClientMutationId);
        if (envelope.Error is not null) return envelope.Error;
        var type = Normalize(request.DependencyType, "FS", "FS", "SS", "FF", "SF").ToUpperInvariant();
        if (!delete && (request.PredecessorTaskId == Guid.Empty || request.SuccessorTaskId == Guid.Empty || request.PredecessorTaskId == request.SuccessorTaskId))
            return Results.BadRequest(new { status = "invalid_task_dependency" });
        if (request.LagWorkingDays is < -365 or > 365)
            return Results.BadRequest(new { status = "invalid_dependency_lag", allowedRange = new { minimum = -365, maximum = 365 } });
        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LockDependencyAsync(connection, transaction, envelope.Source!, dependencyId, request.PlanId, cancellationToken);
        if (existing is null) return Results.NotFound(new { status = "task_dependency_not_found" });
        if (!await CanAccessProjectAsync(connection, access, existing.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        if (existing.Revision != request.ExpectedRevision)
            return Results.Conflict(new { status = "dependency_revision_conflict", revision = existing.Revision });
        await LockProjectAsync(connection, transaction, existing.ProjectId, cancellationToken);
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, existing.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        var predecessor = delete ? existing.PredecessorTaskId : request.PredecessorTaskId;
        var successorId = delete ? existing.SuccessorTaskId : request.SuccessorTaskId;
        if (!delete)
        {
            if (!await DependencyTasksBelongToScopeAsync(connection, transaction, envelope.Source!, existing.ProjectId, existing.PlanId, predecessor, successorId, cancellationToken))
                return Results.BadRequest(new { status = "dependency_task_scope_mismatch" });
            if (await WouldCreateDependencyCycleAsync(connection, transaction, envelope.Source!, existing.PlanId, predecessor, successorId, dependencyId, cancellationToken))
                return Results.BadRequest(new { status = "dependency_cycle_detected" });
        }
        var sql = delete
            ? envelope.Source == "canonical"
                ? "DELETE FROM project_task_dependencies WHERE project_task_dependency_id=@id"
                : "DELETE FROM project_forge_task_dependencies WHERE dependency_id=@id AND plan_id=@plan_id"
            : envelope.Source == "canonical"
                ? "UPDATE project_task_dependencies SET predecessor_task_id=@predecessor,successor_task_id=@successor,dependency_type=@type,lag_working_days=@lag,updated_by_user_id=@actor,updated_at=NOW() WHERE project_task_dependency_id=@id"
                : "UPDATE project_forge_task_dependencies SET predecessor_plan_task_id=@predecessor,successor_plan_task_id=@successor,dependency_type=@type,lag_working_days=@lag,updated_by_user_id=@actor,updated_at=NOW() WHERE dependency_id=@id AND plan_id=@plan_id";
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            AddDependencyParameters(command, dependencyId, existing.ProjectId, existing.PlanId, predecessor, successorId, type, request.LagWorkingDays, access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var successor = await LockInteractiveTaskAsync(connection, transaction, envelope.Source!, successorId, existing.PlanId, access.EffectiveUserId, cancellationToken)
            ?? throw new InvalidOperationException("Dependency successor could not be reloaded.");
        if (!delete)
        {
            var dependencyViolations = await LoadDependencyViolationsAsync(connection, transaction, successor, cancellationToken);
            if (dependencyViolations.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Conflict(new
                {
                    status = "task_dependency_constraint_violation",
                    message = "The dependency would conflict with the current task schedule.",
                    dependencyIds = dependencyViolations
                });
            }
        }
        await InvalidatePlanTaskReviewAsync(connection, transaction, successor, access, successor.Revision, "A dependency changed after review.", cancellationToken);
        var planRevision = successor.PlanId.HasValue
            ? await LoadPlanRevisionAsync(connection, transaction, successor.PlanId.Value, cancellationToken)
            : null;
        await InsertTaskAuditAsync(connection, transaction, successor, delete ? "TASK_DEPENDENCY_DELETED" : "TASK_DEPENDENCY_UPDATED", access,
            new { dependencyId, predecessorTaskId = predecessor, successorTaskId = successorId, type, request.LagWorkingDays, request.ClientMutationId }, cancellationToken);
        await InsertCoalescedTaskNotificationAsync(connection, transaction, successor, access, request.ClientMutationId!, delete ? "A task dependency was removed." : "A task dependency was updated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (delete)
            return Results.Ok(new { module = "033", status = "task_dependency_deleted", recordSource = envelope.Source, dependencyId, planRevision, stateChanged = true });
        var revision = existing.Revision + 1;
        var dependency = new { dependencyId, planId = existing.PlanId, projectId = existing.ProjectId, predecessorTaskId = predecessor, successorTaskId = successorId, dependencyType = type, lagWorkingDays = request.LagWorkingDays, recordSource = envelope.Source, revision };
        return Results.Ok(new { module = "033", status = "task_dependency_updated", recordSource = envelope.Source, dependency, revision, planRevision, stateChanged = true });
    }

    private static async Task<bool> CheckAndLockPlanRevisionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid planId,
        Guid projectId, int expectedRevision, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT revision_number FROM project_forge_plans WHERE plan_id=@plan_id AND project_id=@project_id FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("project_id", projectId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is int revision && revision == expectedRevision;
    }

    private static async Task TouchPlanAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid planId, Guid actor, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE project_forge_plans SET updated_by_user_id=@actor,updated_at=NOW() WHERE plan_id=@plan_id", connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDependencyParameters(NpgsqlCommand command, Guid dependencyId, Guid projectId, Guid? planId,
        Guid predecessor, Guid successor, string type, int lag, Guid actor)
    {
        command.Parameters.AddWithValue("id", dependencyId);
        command.Parameters.AddWithValue("project_id", projectId);
        if (planId.HasValue) command.Parameters.AddWithValue("plan_id", planId.Value);
        command.Parameters.AddWithValue("predecessor", predecessor);
        command.Parameters.AddWithValue("successor", successor);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("lag", lag);
        command.Parameters.AddWithValue("actor", actor);
    }

    private static async Task<bool> DependencyTasksBelongToScopeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string source,
        Guid projectId, Guid? planId, Guid predecessor, Guid successor, CancellationToken cancellationToken)
    {
        var sql = source == "canonical"
            ? "SELECT COUNT(*)=2 FROM project_tasks WHERE project_id=@project_id AND is_active=TRUE AND task_id=ANY(@ids)"
            : "SELECT COUNT(*)=2 FROM project_forge_plan_tasks WHERE project_id=@project_id AND plan_id=@plan_id AND canonical_task_id IS NULL AND task_status<>'cancelled' AND plan_task_id=ANY(@ids)";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        if (planId.HasValue) command.Parameters.AddWithValue("plan_id", planId.Value);
        command.Parameters.AddWithValue("ids", new[] { predecessor, successor });
        return (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
    }

    private static async Task<bool> WouldCreateDependencyCycleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string source,
        Guid? planId, Guid predecessor, Guid successor, Guid? excludedDependencyId, CancellationToken cancellationToken)
    {
        var sql = source == "canonical"
            ? """
                WITH RECURSIVE reachable(task_id) AS (
                    SELECT @successor::uuid
                    UNION
                    SELECT dependency.successor_task_id FROM project_task_dependencies dependency
                    JOIN reachable current_node ON current_node.task_id=dependency.predecessor_task_id
                    WHERE (@excluded IS NULL OR dependency.project_task_dependency_id<>@excluded)
                ) SELECT EXISTS(SELECT 1 FROM reachable WHERE task_id=@predecessor)
                """
            : """
                WITH RECURSIVE reachable(task_id) AS (
                    SELECT @successor::uuid
                    UNION
                    SELECT dependency.successor_plan_task_id FROM project_forge_task_dependencies dependency
                    JOIN reachable current_node ON current_node.task_id=dependency.predecessor_plan_task_id
                    WHERE dependency.plan_id=@plan_id AND (@excluded IS NULL OR dependency.dependency_id<>@excluded)
                ) SELECT EXISTS(SELECT 1 FROM reachable WHERE task_id=@predecessor)
                """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("successor", successor);
        command.Parameters.AddWithValue("predecessor", predecessor);
        AddNullableUuid(command, "excluded", excludedDependencyId);
        if (planId.HasValue) command.Parameters.AddWithValue("plan_id", planId.Value);
        return (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
    }

    private static async Task<InteractiveDependencyState?> LockDependencyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string source, Guid dependencyId, Guid? planId, CancellationToken cancellationToken)
    {
        var sql = source == "canonical"
            ? "SELECT project_task_dependency_id,project_id,NULL::uuid,predecessor_task_id,successor_task_id,dependency_type,lag_working_days,revision_number FROM project_task_dependencies WHERE project_task_dependency_id=@id FOR UPDATE"
            : "SELECT dependency_id,project_id,plan_id,predecessor_plan_task_id,successor_plan_task_id,dependency_type,lag_working_days,revision_number FROM project_forge_task_dependencies WHERE dependency_id=@id AND plan_id=@plan_id FOR UPDATE";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", dependencyId);
        if (planId.HasValue) command.Parameters.AddWithValue("plan_id", planId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new InteractiveDependencyState(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), source,
            reader.GetGuid(3), reader.GetGuid(4), reader.GetString(5), reader.GetInt32(6), reader.GetInt32(7));
    }

    private static async Task<(string Status, int Revision)> LoadPlanWorkflowStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid planId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT plan_status,revision_number FROM project_forge_plans WHERE plan_id=@plan_id", connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Project Forge review plan could not be reloaded after its workflow update.");
        return (reader.GetString(0), reader.GetInt32(1));
    }

    private static async Task<IResult> CompleteTaskReviewAsync(
        Guid planId,
        Guid planTaskId,
        ProjectForgeReviewCompletionRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!request.ExpectedRevision.HasValue || request.ExpectedRevision.Value < 1)
            return Results.BadRequest(new { status = "expected_revision_required" });
        if (string.IsNullOrWhiteSpace(request.ClientMutationId) || request.ClientMutationId.Trim().Length is < 8 or > 160)
            return Results.BadRequest(new { status = "client_mutation_id_required" });
        var decision = Normalize(request.Decision, string.Empty, "completed", "changes_requested");
        if (string.IsNullOrEmpty(decision)) return Results.BadRequest(new { status = "invalid_review_decision" });
        if (decision == "changes_requested" && string.IsNullOrWhiteSpace(request.ReviewNote))
            return Results.BadRequest(new { status = "review_note_required" });

        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (access.IsViewAs) return WriteForbidden(access);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var state = await LockInteractiveTaskAsync(connection, transaction, "review_plan", planTaskId, planId, access.EffectiveUserId, cancellationToken);
        if (state is null) return Results.NotFound(new { status = "plan_task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, state.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, state.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (state.ReviewerUserId != access.EffectiveUserId || !access.CanEditAssignedEstimate)
            return Forbidden("EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033");
        if (state.Revision != request.ExpectedRevision)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await TaskConflictAsync(connection, access, state, cancellationToken);
        }
        const string assignmentSql = """
            UPDATE project_forge_plan_assignments
            SET review_status=@status,assignment_notes=CASE WHEN @note='' THEN assignment_notes ELSE @note END,
                completed_at=CASE WHEN @status='completed' THEN NOW() ELSE NULL END,
                reviewed_task_revision=CASE WHEN @status='completed' THEN @task_revision ELSE NULL END,updated_at=NOW()
            WHERE plan_id=@plan_id AND plan_task_id=@task_id AND user_id=@reviewer AND assignment_type='task_estimator'
            """;
        int assignments;
        await using (var command = new NpgsqlCommand(assignmentSql, connection, transaction))
        {
            command.Parameters.AddWithValue("status", decision == "completed" ? "completed" : "in_progress");
            command.Parameters.AddWithValue("note", Clean(request.ReviewNote, 4000, string.Empty));
            command.Parameters.AddWithValue("task_revision", state.Revision);
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("task_id", planTaskId);
            command.Parameters.AddWithValue("reviewer", access.EffectiveUserId);
            assignments = await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (assignments != 1) return Results.Conflict(new { status = "review_assignment_missing" });
        if (decision == "changes_requested")
        {
            const string sql = """
                UPDATE project_forge_plans SET plan_status='changes_requested',reviewed_by_user_id=NULL,reviewed_at=NULL,
                    review_notes=CASE WHEN @note='' THEN review_notes ELSE @note END,updated_by_user_id=@actor,updated_at=NOW()
                WHERE plan_id=@plan_id
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("note", Clean(request.ReviewNote, 4000, string.Empty));
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            const string sql = """
                UPDATE project_forge_plans plan
                SET plan_status='reviewed',reviewed_by_user_id=@actor,reviewed_at=NOW(),updated_by_user_id=@actor,updated_at=NOW()
                WHERE plan.plan_id=@plan_id
                  AND NOT EXISTS(
                      SELECT 1 FROM project_forge_plan_tasks task
                      LEFT JOIN project_forge_plan_assignments review ON review.plan_task_id=task.plan_task_id
                        AND review.user_id=task.reviewer_user_id AND review.assignment_type='task_estimator'
                      WHERE task.plan_id=plan.plan_id AND task.task_status<>'cancelled'
                        AND (task.reviewer_user_id IS NULL OR review.review_status<>'completed'
                             OR review.reviewed_task_revision IS DISTINCT FROM task.revision_number)
                  )
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertTaskAuditAsync(connection, transaction, state, decision == "completed" ? "TASK_REVIEW_COMPLETED" : "TASK_REVIEW_CHANGES_REQUESTED", access,
            new { decision, request.ReviewNote, reviewedTaskRevision = state.Revision, request.ClientMutationId }, cancellationToken);
        await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.TaskUpdatedPolicy, state.ProjectId, null,
            $"review:{planId}:task:{planTaskId}:{decision}:v{state.Revision}",
            new { planId, planTaskId, taskName = state.TaskName, updatedByName = access.DisplayName, changeSummary = decision == "completed" ? "Engineering review was completed." : "Engineering requested changes to the task.", request.ReviewNote }, cancellationToken);
        var planWorkflowState = await LoadPlanWorkflowStateAsync(connection, transaction, planId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var responseTask = await LoadInteractiveTaskResponseAsync(connection, access, state, cancellationToken);
        return Results.Ok(new
        {
            module = "033",
            status = decision == "completed" ? "task_review_completed" : "task_review_changes_requested",
            recordSource = "review_plan",
            task = responseTask,
            revision = state.Revision,
            planId,
            planStatus = planWorkflowState.Status,
            planRevision = planWorkflowState.Revision,
            stateChanged = true
        });
    }
}
