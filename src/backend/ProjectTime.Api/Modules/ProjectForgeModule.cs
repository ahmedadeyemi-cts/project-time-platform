using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 033. Project Forge projects every workbook view from canonical live
/// project data and keeps authored/AI plans in a review boundary until an
/// authorized human explicitly adopts them into canonical tasks/assignments.
/// </summary>
public static partial class ProjectForgeModule
{
    private const string AdoptConfirmation = "ADOPT PROJECT FORGE PLAN";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static WebApplication MapProjectForgeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/project-forge/bootstrap", (Func<Guid?, Guid?, string?, Guid?, HttpContext, CelarAiKnowledgeFabricService, PulseAiPrivateRetrievalAuthorizationService, ILoggerFactory, CancellationToken, Task<IResult>>)GetBootstrapAsync);
        app.MapPost("/api/project-forge/plans", (Func<ProjectForgePlanSaveRequest, HttpContext, CancellationToken, Task<IResult>>)CreatePlanAsync);
        app.MapPut("/api/project-forge/plans/{planId:guid}", (Func<Guid, ProjectForgePlanSaveRequest, HttpContext, CancellationToken, Task<IResult>>)UpdatePlanAsync);
        app.MapPost("/api/project-forge/projects/{projectId:guid}/ai-drafts", (Func<Guid, ProjectForgeAiDraftRequest, HttpContext, CelarAiEnterprisePlatformService, PulseAiPrivateRetrievalAuthorizationService, CancellationToken, Task<IResult>>)GenerateAiDraftAsync);
        app.MapPost("/api/project-forge/ai-drafts/{planId:guid}/assign-reviewer", (Func<Guid, ProjectForgeAssignReviewerRequest, HttpContext, CancellationToken, Task<IResult>>)AssignReviewerAsync);
        app.MapPatch("/api/project-forge/plan-tasks/{planTaskId:guid}/estimate", (Func<Guid, ProjectForgeEstimatePatchRequest, HttpContext, CancellationToken, Task<IResult>>)PatchEstimateAsync);
        app.MapPost("/api/project-forge/plans/{planId:guid}/adopt", (Func<Guid, ProjectForgeAdoptPlanRequest, HttpContext, CancellationToken, Task<IResult>>)AdoptPlanAsync);
        app.MapPost("/api/project-forge/projects/{projectId:guid}/tasks", (Func<Guid, ProjectForgeTaskCreateRequest, HttpContext, CancellationToken, Task<IResult>>)CreateTaskAsync);
        app.MapPatch("/api/project-forge/tasks/{taskId:guid}/details", (Func<Guid, ProjectForgeTaskDetailsPatchRequest, HttpContext, CancellationToken, Task<IResult>>)PatchTaskDetailsAsync);
        app.MapPatch("/api/project-forge/tasks/{taskId:guid}/workflow", (Func<Guid, ProjectForgeTaskWorkflowPatchRequest, HttpContext, CancellationToken, Task<IResult>>)PatchTaskWorkflowAsync);
        app.MapPatch("/api/project-forge/tasks/{taskId:guid}/schedule", (Func<Guid, ProjectForgeTaskSchedulePatchRequest, HttpContext, CancellationToken, Task<IResult>>)PatchTaskScheduleAsync);
        app.MapPatch("/api/project-forge/tasks/{taskId:guid}/decision", (Func<Guid, ProjectForgeTaskDecisionPatchRequest, HttpContext, CancellationToken, Task<IResult>>)PatchTaskDecisionAsync);
        app.MapPatch("/api/project-forge/tasks/{taskId:guid}/composite", (Func<Guid, ProjectForgeTaskCompositePatchRequest, HttpContext, CancellationToken, Task<IResult>>)PatchTaskCompositeAsync);
        app.MapPut("/api/project-forge/tasks/{taskId:guid}/assignee", (Func<Guid, ProjectForgeTaskAssigneePutRequest, HttpContext, CancellationToken, Task<IResult>>)PutTaskAssigneeAsync);
        app.MapDelete("/api/project-forge/tasks/{taskId:guid}", (Func<Guid, ProjectForgeTaskArchiveRequest, HttpContext, CancellationToken, Task<IResult>>)ArchiveTaskAsync);
        app.MapPost("/api/project-forge/projects/{projectId:guid}/task-dependencies", (Func<Guid, ProjectForgeTaskDependencySaveRequest, HttpContext, CancellationToken, Task<IResult>>)CreateTaskDependencyAsync);
        app.MapPatch("/api/project-forge/task-dependencies/{dependencyId:guid}", (Func<Guid, ProjectForgeTaskDependencySaveRequest, HttpContext, CancellationToken, Task<IResult>>)UpdateTaskDependencyAsync);
        app.MapDelete("/api/project-forge/task-dependencies/{dependencyId:guid}", (Func<Guid, ProjectForgeTaskDependencySaveRequest, HttpContext, CancellationToken, Task<IResult>>)DeleteTaskDependencyAsync);
        app.MapPost("/api/project-forge/plans/{planId:guid}/tasks/{planTaskId:guid}/review-completion", (Func<Guid, Guid, ProjectForgeReviewCompletionRequest, HttpContext, CancellationToken, Task<IResult>>)CompleteTaskReviewAsync);
        return app;
    }

    private static async Task<IResult> GetBootstrapAsync(
        Guid? projectManagerUserId,
        Guid? projectId,
        string? workspace,
        Guid? planId,
        HttpContext context,
        CelarAiKnowledgeFabricService knowledgeFabricService,
        PulseAiPrivateRetrievalAuthorizationService authorization,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var configured = OpenConfiguration();
        if (configured.Error is not null) return configured.Error;

        try
        {
            await using var connection = new NpgsqlConnection(configured.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            var access = await LoadAccessAsync(connection, identity.Value, context, cancellationToken);
            if (!access.CanView) return Forbidden("VIEW_PROJECT_FORGE_033");

            var managerFilter = await ResolveManagerFilterAsync(connection, access, projectManagerUserId, cancellationToken);
            if (managerFilter.Error is not null) return managerFilter.Error;
            if (projectId.HasValue && !await CanAccessProjectAsync(connection, access, projectId.Value, managerFilter.ManagerUserId, cancellationToken))
                return Forbidden("project_forge_project_scope");

            var selectedWorkspace = Normalize(workspace, "canonical", "canonical", "review_plan");
            if (!string.IsNullOrWhiteSpace(workspace) && !string.Equals(selectedWorkspace, workspace.Trim(), StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { status = "invalid_workspace", allowed = new[] { "canonical", "review_plan" } });
            if (selectedWorkspace == "review_plan" && !planId.HasValue)
                return Results.BadRequest(new { status = "review_plan_required", message = "Select one review plan before loading draft tasks." });
            if (selectedWorkspace == "canonical" && planId.HasValue)
                return Results.BadRequest(new { status = "canonical_plan_not_allowed", message = "Canonical workspace does not accept a review-plan identifier." });
            if (projectId.HasValue
                && !await CanAccessProjectAsync(connection, access, projectId.Value, managerFilter.ManagerUserId, cancellationToken))
                return Forbidden("project_forge_project_scope");
            Guid? planProjectId = null;
            if (planId.HasValue)
            {
                var selectedPlan = await LoadPlanProjectAsync(connection, planId.Value, cancellationToken);
                if (selectedPlan is null) return Results.NotFound(new { status = "plan_not_found" });
                planProjectId = selectedPlan.Value.ProjectId;
                if (projectId.HasValue && projectId.Value != selectedPlan.Value.ProjectId)
                    return Results.BadRequest(new { status = "plan_project_mismatch" });
                if (!await CanAccessProjectAsync(connection, access, selectedPlan.Value.ProjectId, managerFilter.ManagerUserId, cancellationToken))
                    return Forbidden("project_forge_project_scope");
            }

            var managers = access.CanSelectProjectManager
                ? await LoadProjectManagersAsync(connection, access, cancellationToken)
                : [];
            var projects = await ReadJsonRowsAsync(connection, ProjectsSql, command =>
            {
                AddAccessParameters(command, access);
                AddNullableUuid(command, "manager_filter", managerFilter.ManagerUserId);
                AddNullableUuid(command, "project_filter", null);
                command.Parameters.AddWithValue("workspace", selectedWorkspace);
                AddNullableUuid(command, "plan_filter", planId);
            }, cancellationToken);
            Guid? selectedProjectId = projectId ?? planProjectId;
            if (!selectedProjectId.HasValue && projects.Count > 0
                && projects[0].TryGetProperty("projectId", out var firstProjectId)
                && firstProjectId.ValueKind == JsonValueKind.String)
                selectedProjectId = firstProjectId.GetGuid();
            var detailProjectFilter = selectedProjectId ?? Guid.Empty;
            var tasks = await ReadJsonRowsAsync(connection, TasksSql, command =>
            {
                AddAccessParameters(command, access);
                AddNullableUuid(command, "manager_filter", managerFilter.ManagerUserId);
                AddNullableUuid(command, "project_filter", detailProjectFilter);
                command.Parameters.AddWithValue("workspace", selectedWorkspace);
                AddNullableUuid(command, "plan_filter", planId);
            }, cancellationToken);
            var assignments = await ReadJsonRowsAsync(connection, AssignmentsSql, command =>
            {
                AddAccessParameters(command, access);
                AddNullableUuid(command, "manager_filter", managerFilter.ManagerUserId);
                AddNullableUuid(command, "project_filter", detailProjectFilter);
                command.Parameters.AddWithValue("workspace", selectedWorkspace);
                AddNullableUuid(command, "plan_filter", planId);
            }, cancellationToken);
            var projectTeam = await ReadJsonRowsAsync(connection, ProjectTeamSql, command =>
            {
                AddAccessParameters(command, access);
                AddNullableUuid(command, "manager_filter", managerFilter.ManagerUserId);
                AddNullableUuid(command, "project_filter", detailProjectFilter);
            }, cancellationToken);
            var expenses = await ReadJsonRowsAsync(connection, ExpensesSql, command =>
            {
                AddAccessParameters(command, access);
                AddNullableUuid(command, "manager_filter", managerFilter.ManagerUserId);
                AddNullableUuid(command, "project_filter", detailProjectFilter);
            }, cancellationToken);
            var plans = await ReadJsonRowsAsync(connection, PlansSql, command =>
            {
                AddAccessParameters(command, access);
                AddNullableUuid(command, "manager_filter", managerFilter.ManagerUserId);
                AddNullableUuid(command, "project_filter", detailProjectFilter);
            }, cancellationToken);
            var dependencies = await ReadJsonRowsAsync(connection, DependenciesSql, command =>
            {
                AddAccessParameters(command, access);
                AddNullableUuid(command, "manager_filter", managerFilter.ManagerUserId);
                AddNullableUuid(command, "project_filter", detailProjectFilter);
                command.Parameters.AddWithValue("workspace", selectedWorkspace);
                AddNullableUuid(command, "plan_filter", planId);
            }, cancellationToken);
            var activity = await ReadJsonRowsAsync(connection, ActivitySql, command =>
            {
                AddAccessParameters(command, access);
                AddNullableUuid(command, "manager_filter", managerFilter.ManagerUserId);
                AddNullableUuid(command, "project_filter", detailProjectFilter);
            }, cancellationToken);
            var holidays = await ReadJsonRowsAsync(connection, HolidaysSql, null, cancellationToken);
            var projectEvidence = selectedProjectId.HasValue
                ? await authorization.LoadProjectEvidenceReadinessAsync(
                    connection,
                    ToPrivateRagAccess(access),
                    selectedProjectId.Value,
                    PulseAiPrivateRagPolicy.FlowHiveCategories,
                    cancellationToken)
                : PulseAiPrivateProjectEvidenceReadiness.Empty;
            CelarAiKnowledgeFabricSnapshot? knowledgeFabric = null;
            CelarAiCapabilityConnectionStatus? forgeConnection = null;
            try
            {
                knowledgeFabric = await knowledgeFabricService.GetSnapshotAsync(cancellationToken);
                forgeConnection = knowledgeFabric.Capabilities.FirstOrDefault(item =>
                    string.Equals(item.Feature, CelarAiCapabilityCatalog.ProjectForgePlanEstimate, StringComparison.OrdinalIgnoreCase));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                loggerFactory.CreateLogger("ProjectTime.Api.Modules.ProjectForgeModule")
                    .LogWarning("Module 033 could not load Module 064 knowledge-fabric evidence. Diagnostic=knowledge_fabric_snapshot_unavailable");
            }

            return Results.Ok(new
            {
                module = ProjectForgePolicy.ModuleCode,
                moduleName = "Project Forge",
                status = "project_forge_loaded",
                workspace = selectedWorkspace,
                selectedPlanId = planId,
                access = access.ToResponse(managerFilter.ManagerUserId),
                tabs = ProjectForgePolicy.WorkbookTabs.Select((name, index) => new
                {
                    id = Slug(name),
                    name,
                    order = index + 1,
                    source = "Ultimate Project Manager workbook",
                    dataMode = "live_authoritative"
                }),
                projectManagers = managers,
                projects,
                tasks,
                assignments,
                projectTeam,
                engineers = projectTeam,
                holidays,
                expenses,
                plans,
                dependencies,
                summary = new
                {
                    projectCount = projects.Count,
                    taskCount = tasks.Count,
                    assignmentCount = assignments.Count,
                    expenseRecordCount = expenses.Count,
                    planCount = plans.Count,
                    workbookTabCount = ProjectForgePolicy.WorkbookTabs.Length,
                    selectedProjectManagerUserId = managerFilter.ManagerUserId,
                    selectedProjectId,
                    selectedWorkspace,
                    selectedPlanId = planId
                },
                activity,
                setup = new
                {
                    currency = "USD",
                    priorities = new[] { "low", "normal", "high", "critical" },
                    decisions = new[] { "do", "delegate", "decide", "delete" },
                    kanbanCategories = new[] { "backlog", "ready", "in_progress", "blocked", "review", "done" },
                    workingDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
                    optionsSource = "governed_module_contract",
                    peopleSource = "app_users_and_project_assignments",
                    projectsSource = "projects"
                },
                ai = new
                {
                    enabled = access.CanUseAi && !access.IsViewAs,
                    capability = ProjectForgePolicy.CapabilityCode,
                    module064Connection = new
                    {
                        connected = forgeConnection?.CentralRouterConnected == true,
                        status = access.IsViewAs
                            ? "view_as_read_only"
                            : !access.CanUseAi
                                ? "permission_required"
                                : forgeConnection?.Status ?? "connection_evidence_unavailable",
                        permissionAuthorized = access.CanUseAi && !access.IsViewAs,
                        privateKnowledgeReady = forgeConnection?.PrivateKnowledgeReady == true,
                        route = forgeConnection?.Route ?? CelarAiCapabilityTargets.DefaultOrder,
                        sourceCommit = knowledgeFabric?.SourceCommit ?? "unavailable",
                        productKnowledgeVersion = knowledgeFabric?.ProductKnowledgeVersion ?? "unavailable",
                        systemKnowledgeVersion = knowledgeFabric?.SystemKnowledgeVersion ?? "unavailable",
                        readyDocumentCount = knowledgeFabric?.ReadyDocumentCount ?? 0,
                        readySowDocumentCount = knowledgeFabric?.ReadySowDocumentCount ?? 0,
                        activeVersionCount = knowledgeFabric?.ActiveVersionCount ?? 0,
                        activeChunkCount = knowledgeFabric?.ActiveChunkCount ?? 0,
                        embeddedChunkCount = knowledgeFabric?.EmbeddedChunkCount ?? 0,
                        lastIndexedAt = knowledgeFabric?.LastIndexedAt,
                        projectId = selectedProjectId,
                        projectEvidenceReady = projectEvidence.ReadyDocumentCount > 0,
                        projectReadyDocumentCount = projectEvidence.ReadyDocumentCount,
                        projectReadySowDocumentCount = projectEvidence.ReadySowDocumentCount,
                        projectActiveVersionCount = projectEvidence.ActiveVersionCount,
                        projectActiveChunkCount = projectEvidence.ActiveChunkCount,
                        projectEmbeddedChunkCount = projectEvidence.EmbeddedChunkCount,
                        projectLastIndexedAt = projectEvidence.LastIndexedAt,
                        blockers = knowledgeFabric?.Blockers.Take(8).ToArray() ?? ["knowledge_fabric_snapshot_unavailable"],
                        endpointValuesReturned = false,
                        secretValuesReturned = false
                    },
                    privateDocumentGrounding = true,
                    humanReviewRequired = true,
                    automaticAdoption = false
                },
                stateChanged = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UndefinedTable
            or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedFunction)
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            Log(context, exception, "load bootstrap");
            return Problem("Project Forge could not load its authorized live project data.");
        }
    }

    private static async Task<IResult> CreatePlanAsync(
        ProjectForgePlanSaveRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var configured = OpenConfiguration();
        if (configured.Error is not null) return configured.Error;
        await using var connection = new NpgsqlConnection(configured.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var access = await LoadAccessAsync(connection, identity.Value, context, cancellationToken);
        if (!access.CanEditReviewPlan || access.IsViewAs) return WriteForbidden(access);
        if (!await CanAccessProjectAsync(connection, access, request.ProjectId, null, cancellationToken))
            return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, request.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        var validation = ValidatePlan(request);
        if (validation is not null) return validation;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockProjectAsync(connection, transaction, request.ProjectId, cancellationToken);
        projectWriteError = await EnsureProjectWritableAsync(connection, transaction, request.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        var planId = Guid.NewGuid();
        await InsertPlanAsync(connection, transaction, planId, request, "manual", null, access.ActualUserId, cancellationToken);
        await ReplacePlanRowsAsync(connection, transaction, planId, request.Tasks!, request.Dependencies ?? [], access.ActualUserId, cancellationToken);
        await InsertAuditAsync(connection, transaction, request.ProjectId, planId, null, "PLAN_CREATED", access, new { request.PlanName }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/project-forge/plans/{planId}", new { module = "033", status = "review_plan_created", planId, stateChanged = true });
    }

    private static async Task<IResult> UpdatePlanAsync(
        Guid planId,
        ProjectForgePlanSaveRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!request.ExpectedRevision.HasValue || request.ExpectedRevision.Value < 1)
            return Results.BadRequest(new { status = "expected_revision_required" });
        if (string.IsNullOrWhiteSpace(request.ClientMutationId) || request.ClientMutationId.Trim().Length is < 8 or > 160)
            return Results.BadRequest(new { status = "client_mutation_id_required" });
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var configured = OpenConfiguration();
        if (configured.Error is not null) return configured.Error;
        await using var connection = new NpgsqlConnection(configured.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var access = await LoadAccessAsync(connection, identity.Value, context, cancellationToken);
        if (!access.CanEditReviewPlan || access.IsViewAs) return WriteForbidden(access);
        if (!await CanAccessProjectAsync(connection, access, request.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, request.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        var validation = ValidatePlan(request);
        if (validation is not null) return validation;

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockProjectAsync(connection, transaction, request.ProjectId, cancellationToken);
        projectWriteError = await EnsureProjectWritableAsync(connection, transaction, request.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        await using (var reviewEvidence = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM project_forge_plan_assignments WHERE plan_id=@plan_id)", connection, transaction))
        {
            reviewEvidence.Parameters.AddWithValue("plan_id", planId);
            if ((bool?)await reviewEvidence.ExecuteScalarAsync(cancellationToken) == true)
                return Results.Conflict(new { status = "targeted_task_mutation_required", message = "This plan has review assignments. Use the targeted Project Forge task and dependency endpoints so review evidence is not deleted." });
        }
        const string updateSql = """
            UPDATE project_forge_plans
            SET plan_name=@name, objective=@objective, planned_start_date=@start_date, planned_end_date=@end_date,
                review_notes=@review_note, updated_by_user_id=@actor, updated_at=NOW(), revision_number=revision_number+1
            WHERE plan_id=@plan_id AND project_id=@project_id AND revision_number=@expected_revision
              AND plan_status IN ('draft','in_review','changes_requested')
            """;
        await using (var command = new NpgsqlCommand(updateSql, connection, transaction))
        {
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("project_id", request.ProjectId);
            command.Parameters.AddWithValue("name", Clean(request.PlanName, 240, "Project plan"));
            command.Parameters.AddWithValue("objective", Clean(request.Objective, 4000, string.Empty));
            AddNullableDate(command, "start_date", request.StartDate);
            AddNullableDate(command, "end_date", LatestDueDate(request.Tasks));
            command.Parameters.AddWithValue("review_note", Clean(request.ReviewNote, 4000, string.Empty));
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            command.Parameters.AddWithValue("expected_revision", request.ExpectedRevision.Value);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                return Results.Conflict(new { status = "plan_revision_conflict", message = "Refresh the review plan before saving." });
        }
        await ReplacePlanRowsAsync(connection, transaction, planId, request.Tasks!, request.Dependencies ?? [], access.ActualUserId, cancellationToken);
        await InsertAuditAsync(connection, transaction, request.ProjectId, planId, null, "PLAN_UPDATED", access, new { request.PlanName }, cancellationToken);
        await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.PlanUpdatedPolicy, request.ProjectId, null, $"plan:{planId}:updated:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", new { planId, request.PlanName, updatedByName = access.DisplayName, changeSummary = "The review plan and its tasks were updated." }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { module = "033", status = "review_plan_updated", planId, stateChanged = true });
    }

    private static async Task<IResult> GenerateAiDraftAsync(
        Guid projectId,
        ProjectForgeAiDraftRequest request,
        HttpContext context,
        CelarAiEnterprisePlatformService enterprise,
        PulseAiPrivateRetrievalAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var configured = OpenConfiguration();
        if (configured.Error is not null) return configured.Error;
        await using var connection = new NpgsqlConnection(configured.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var access = await LoadAccessAsync(connection, identity.Value, context, cancellationToken);
        if (!access.CanUseAi || !access.CanManage || access.IsViewAs) return WriteForbidden(access);
        if (CandidateAiDraftMutationBlocked() is { } blocked) return blocked;
        if (!await CanAccessProjectAsync(connection, access, projectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, projectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        var project = await LoadProjectIdentityAsync(connection, projectId, cancellationToken);
        if (project is null) return Results.NotFound(new { status = "project_not_found" });

        var projectEvidence = await authorization.LoadProjectEvidenceReadinessAsync(
            connection,
            ToPrivateRagAccess(access),
            projectId,
            PulseAiPrivateRagPolicy.FlowHiveCategories,
            cancellationToken);
        if (projectEvidence.ReadyDocumentCount == 0)
        {
            return Results.UnprocessableEntity(new
            {
                status = "ai_plan_evidence_insufficient",
                message = "This project has no citation-ready private document evidence. No AI target was called and no draft was saved.",
                projectId,
                projectEvidence.ReadyDocumentCount,
                projectEvidence.ReadySowDocumentCount,
                projectEvidence.ActiveVersionCount,
                projectEvidence.ActiveChunkCount,
                projectEvidence.EmbeddedChunkCount,
                stateChanged = false
            });
        }

        var outcome = Clean(request.RequestedOutcome, 4000,
            "Create a comprehensive, reviewable project plan with WBS tasks, dependencies, roles, durations, and engineering estimates grounded in the authorized SOW, GSD, architecture, design, and other project documents.");
        var composition = await enterprise.ComposeAsync(
            access.ActualUserId,
            access.EffectiveUserId,
            new CelarAiComposeRequest(
                Mode: "project_plan",
                ProjectCode: project.Value.ProjectCode,
                ProjectName: project.Value.ProjectName,
                StartDate: request.StartDate ?? project.Value.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                RequestedOutcome: outcome,
                DetailLevel: Clean(request.DetailLevel, 40, "comprehensive"),
                DiagramType: "gantt",
                AllowSanitizedExternalFallback: request.AllowSanitizedExternalFallback,
                CapabilityCode: ProjectForgePolicy.CapabilityCode),
            context,
            cancellationToken);

        var compositionRefused = string.Equals(
                composition.Status,
                "celar_ai_solution_draft_refused",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                composition.PrimaryExecutionPath,
                "safety_refusal",
                StringComparison.OrdinalIgnoreCase);
        if (compositionRefused)
        {
            return Results.UnprocessableEntity(new
            {
                status = "ai_plan_generation_refused",
                compositionStatus = composition.Status,
                message = "The selected AI target declined the Project Forge draft request. No draft was saved.",
                composition.Warnings,
                composition.CorrelationId,
                composition.SelectedTarget,
                composition.AttemptedTargets,
                composition.SkippedTargets,
                composition.TargetDecisions,
                composition.PrimaryExecutionPath,
                stateChanged = false
            });
        }

        var groundedStatus = composition.Status is
            "celar_ai_solution_draft_completed" or
            "celar_ai_solution_draft_partial";
        var planCitationIds = composition.FlowHivePlan is null
            ? Array.Empty<int>()
            : composition.FlowHivePlan.CitationIds
                .Concat(composition.FlowHivePlan.Tasks.SelectMany(task => task.CitationIds))
                .Concat(composition.FlowHivePlan.Milestones.SelectMany(milestone => milestone.CitationIds))
                .Distinct()
                .ToArray();
        var groundedPlan = composition.FlowHivePlan is not null
            && composition.FlowHivePlan.Tasks.Count > 0
            && planCitationIds.Length > 0
            && composition.Citations.Count > 0;
        if (!groundedStatus || !groundedPlan)
        {
            return Results.UnprocessableEntity(new
            {
                status = "ai_plan_evidence_insufficient",
                compositionStatus = composition.Status,
                message = "The AI route did not return a citation-grounded private project plan. No draft was saved.",
                composition.MissingEvidence,
                composition.Warnings,
                composition.CorrelationId,
                composition.SelectedTarget,
                composition.AttemptedTargets,
                composition.SkippedTargets,
                composition.TargetDecisions,
                composition.PrimaryExecutionPath,
                stateChanged = false
            });
        }

        var generatedTasks = (composition.FlowHivePlan?.Tasks ?? [])
            .Take(500)
            .Select((task, index) => new ProjectForgePlanTaskRequest(
                null,
                task.Wbs,
                ParentWbs(task.Wbs),
                TaskName(task.Name, index + 1),
                PlanningDescription(task),
                "variable",
                task.Phase,
                Normalize(task.Priority, "normal", "low", "normal", "high", "critical"),
                "draft",
                "backlog",
                "decide",
                null,
                null,
                Math.Max(1, (int)Math.Ceiling(task.EstimatedDurationDays)),
                Math.Max(1m, task.EstimatedHours ?? task.EstimatedDurationDays * 8m),
                0m, 0m, 0m, 0m, 0m, 0m, 0m,
                0m,
                false,
                false,
                null,
                null))
            .ToArray();
        if (generatedTasks.Length == 0)
        {
            return Results.UnprocessableEntity(new
            {
                status = "ai_plan_evidence_insufficient",
                compositionStatus = composition.Status,
                message = "Celar AI did not return supported project tasks. No draft was saved.",
                composition.MissingEvidence,
                composition.Warnings,
                composition.CorrelationId,
                composition.SelectedTarget,
                composition.AttemptedTargets,
                composition.SkippedTargets,
                composition.TargetDecisions,
                composition.PrimaryExecutionPath,
                stateChanged = false
            });
        }

        var dependencyInputs = generatedTasks
            .SelectMany(task => (composition.FlowHivePlan?.Tasks.FirstOrDefault(row => row.Wbs == task.Wbs)?.Predecessors ?? [])
                .Select(predecessor => new ProjectForgeDependencyRequest(null, predecessor, task.Wbs, "FS", 0)))
            .ToArray();
        var planRequest = new ProjectForgePlanSaveRequest(
            projectId,
            $"AI project plan — {project.Value.ProjectName}",
            composition.FlowHivePlan?.Objective ?? outcome,
            request.StartDate ?? project.Value.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            generatedTasks,
            dependencyInputs,
            "Celar AI draft. PM and engineering review are required before adoption.");

        var flowHiveRequest = ToFlowHiveRequest(planRequest, project.Value.ProjectCode, project.Value.ProjectName);
        var validation = ProjectFlowHiveScheduleEngine.Validate(flowHiveRequest);
        if (!validation.Valid)
        {
            return Results.UnprocessableEntity(new
            {
                status = "ai_plan_validation_failed",
                compositionStatus = composition.Status,
                message = "Celar AI returned a draft that requires correction before it can enter the Project Forge review ledger. No plan was saved.",
                validation,
                composition.Citations,
                composition.Warnings,
                composition.CorrelationId,
                composition.SelectedTarget,
                composition.AttemptedTargets,
                composition.SkippedTargets,
                composition.TargetDecisions,
                composition.PrimaryExecutionPath,
                stateChanged = false
            });
        }
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(flowHiveRequest);
        var scheduledByWbs = schedule.Tasks.ToDictionary(row => row.WbsNumber, StringComparer.OrdinalIgnoreCase);
        generatedTasks = generatedTasks.Select(task =>
        {
            var wbs = task.Wbs ?? string.Empty;
            return scheduledByWbs.TryGetValue(wbs, out var scheduled)
                ? task with { StartDate = scheduled.StartDate, DueDate = scheduled.EndDate }
                : task;
        }).ToArray();
        planRequest = planRequest with { Tasks = generatedTasks };
        var planId = Guid.NewGuid();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockProjectAsync(connection, transaction, projectId, cancellationToken);
        projectWriteError = await EnsureProjectWritableAsync(connection, transaction, projectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        await InsertPlanAsync(connection, transaction, planId, planRequest, "ai_generated", composition, access.ActualUserId, cancellationToken);
        await ReplacePlanRowsAsync(connection, transaction, planId, generatedTasks, dependencyInputs, access.ActualUserId, cancellationToken, composition.CorrelationId);
        await InsertAuditAsync(connection, transaction, projectId, planId, null, "AI_PLAN_DRAFT_CREATED", access,
            new { capability = ProjectForgePolicy.CapabilityCode, composition.CorrelationId, composition.Confidence, taskCount = generatedTasks.Length }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Created($"/api/project-forge/plans/{planId}", new
        {
            module = "033",
            feature = ProjectForgePolicy.CapabilityCode,
            status = "document_grounded_review_draft_created",
            compositionStatus = composition.Status,
            planId,
            plan = planRequest,
            validation,
            schedule,
            composition.Citations,
            composition.Warnings,
            composition.MissingEvidence,
            composition.Conflicts,
            composition.Confidence,
            composition.ConfidenceExplanation,
            composition.CorrelationId,
            composition.SelectedTarget,
            composition.AttemptedTargets,
            composition.SkippedTargets,
            composition.TargetDecisions,
            composition.PrimaryExecutionPath,
            controls = new { humanReviewRequired = true, adopted = false, canonicalTasksCreated = false, assignmentsCreated = false },
            stateChanged = true
        });
    }

    private static async Task<IResult> AssignReviewerAsync(
        Guid planId,
        ProjectForgeAssignReviewerRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!request.ExpectedPlanRevision.HasValue || request.ExpectedPlanRevision.Value < 1)
            return Results.BadRequest(new { status = "expected_plan_revision_required" });
        if (request.ExpectedTaskRevisions is null || request.ExpectedTaskRevisions.Count == 0)
            return Results.BadRequest(new { status = "expected_task_revisions_required" });
        if (string.IsNullOrWhiteSpace(request.ClientMutationId) || request.ClientMutationId.Trim().Length is < 8 or > 160)
            return Results.BadRequest(new { status = "client_mutation_id_required" });
        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);
        var plan = await LoadPlanProjectAsync(connection, planId, cancellationToken);
        if (plan is null) return Results.NotFound(new { status = "plan_not_found" });
        if (!await CanAccessProjectAsync(connection, access, plan.Value.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        if (!await IsEligibleEngineerReviewerAsync(connection, plan.Value.ProjectId, request.ReviewerUserId, cancellationToken))
            return Results.BadRequest(new { status = "reviewer_not_on_project", message = "Choose an active engineer already assigned to this project." });
        var reviewerName = await LoadUserNameAsync(connection, request.ReviewerUserId, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockProjectAsync(connection, transaction, plan.Value.ProjectId, cancellationToken);
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, plan.Value.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        if (!await CheckAndLockPlanRevisionAsync(connection, transaction, planId, plan.Value.ProjectId, request.ExpectedPlanRevision.Value, cancellationToken))
            return Results.Conflict(new { status = "plan_revision_conflict", message = "Refresh the review plan before assigning a reviewer." });
        var selectedIds = (request.PlanTaskIds ?? []).Distinct().ToArray();
        const string revisionSql = """
            SELECT plan_task_id,revision_number FROM project_forge_plan_tasks
            WHERE plan_id=@plan_id AND canonical_task_id IS NULL AND task_status<>'cancelled'
              AND (@all_tasks OR plan_task_id=ANY(@task_ids))
            FOR UPDATE
            """;
        var currentRevisions = new Dictionary<Guid, int>();
        await using (var revisions = new NpgsqlCommand(revisionSql, connection, transaction))
        {
            revisions.Parameters.AddWithValue("plan_id", planId);
            revisions.Parameters.AddWithValue("all_tasks", selectedIds.Length == 0);
            revisions.Parameters.AddWithValue("task_ids", selectedIds);
            await using var reader = await revisions.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) currentRevisions[reader.GetGuid(0)] = reader.GetInt32(1);
        }
        if (currentRevisions.Count == 0 || currentRevisions.Any(row => !request.ExpectedTaskRevisions.TryGetValue(row.Key, out var expected) || expected != row.Value))
            return Results.Conflict(new { status = "task_revision_conflict", currentTaskRevisions = currentRevisions });
        const string reassignSql = """
            UPDATE project_forge_plan_assignments assignment
            SET review_status='reassigned',completed_at=NULL,reviewed_task_revision=NULL,updated_at=NOW()
            WHERE assignment.plan_id=@plan_id AND assignment.assignment_type='task_estimator'
              AND assignment.plan_task_id=ANY(@task_ids) AND assignment.user_id<>@reviewer
              AND assignment.review_status<>'reassigned'
            """;
        await using (var reassign = new NpgsqlCommand(reassignSql, connection, transaction))
        {
            reassign.Parameters.AddWithValue("plan_id", planId);
            reassign.Parameters.AddWithValue("task_ids", currentRevisions.Keys.ToArray());
            reassign.Parameters.AddWithValue("reviewer", request.ReviewerUserId);
            await reassign.ExecuteNonQueryAsync(cancellationToken);
        }
        const string sql = """
            UPDATE project_forge_plan_tasks
            SET reviewer_user_id=@reviewer, updated_by_user_id=@actor, updated_at=NOW(), revision_number=revision_number+1
            WHERE plan_id=@plan_id
              AND (@all_tasks OR plan_task_id = ANY(@task_ids))
              AND canonical_task_id IS NULL
              AND task_status<>'cancelled'
              AND EXISTS(SELECT 1 FROM project_forge_plans plan WHERE plan.plan_id=project_forge_plan_tasks.plan_id AND plan.plan_status IN ('draft','in_review','changes_requested','reviewed'))
            RETURNING plan_task_id, task_name, estimated_hours
            """;
        var changed = new List<(Guid Id, string Name, decimal Hours)>();
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("reviewer", request.ReviewerUserId);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("all_tasks", selectedIds.Length == 0);
            command.Parameters.AddWithValue("task_ids", selectedIds);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) changed.Add((reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2)));
        }
        if (changed.Count == 0) return Results.BadRequest(new { status = "no_review_tasks_selected" });

        foreach (var task in changed)
        {
            await UpsertPlanAssignmentAsync(connection, transaction, planId, task.Id, plan.Value.ProjectId, request.ReviewerUserId, task.Hours, request.ReviewNote, access.ActualUserId, cancellationToken);
        }
        await SetPlanInReviewAsync(connection, transaction, planId, access.ActualUserId, cancellationToken);
        var planWorkflowState = await LoadPlanWorkflowStateAsync(connection, transaction, planId, cancellationToken);
        await InsertAuditAsync(connection, transaction, plan.Value.ProjectId, planId, null, "REVIEW_ASSIGNED", access,
            new { request.ReviewerUserId, taskIds = changed.Select(row => row.Id), request.ReviewNote }, cancellationToken);
        await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.ReviewAssignedPolicy, plan.Value.ProjectId, request.ReviewerUserId,
            $"review:{planId}:v{request.ExpectedPlanRevision}:reviewer:{request.ReviewerUserId}:mutation:{request.ClientMutationId}",
            new { planId, planName = plan.Value.PlanName, reviewerUserId = request.ReviewerUserId, reviewerName, taskCount = changed.Count, request.ReviewNote }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new
        {
            module = "033",
            status = "engineering_review_assigned",
            planId,
            planStatus = planWorkflowState.Status,
            planRevision = planWorkflowState.Revision,
            taskRevisions = currentRevisions.ToDictionary(row => row.Key, row => row.Value + 1),
            request.ReviewerUserId,
            taskCount = changed.Count,
            stateChanged = true
        });
    }

    private static async Task<IResult> PatchEstimateAsync(
        Guid planTaskId,
        ProjectForgeEstimatePatchRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!request.ExpectedVersion.HasValue || request.ExpectedVersion.Value < 1)
            return Results.BadRequest(new { status = "expected_revision_required" });
        if (string.IsNullOrWhiteSpace(request.ClientMutationId) || request.ClientMutationId.Trim().Length is < 8 or > 160)
            return Results.BadRequest(new { status = "client_mutation_id_required" });
        if (request.EstimatedHours < 0 || request.EstimatedHours > 100000 || request.HourlyRate < 0)
            return Results.BadRequest(new { status = "invalid_estimate", message = "Estimated hours and rates must be non-negative and within supported limits." });
        if (request.StartDate.HasValue || request.DueDate.HasValue)
            return Results.BadRequest(new
            {
                status = "task_schedule_endpoint_required",
                message = "Save task dates through the schedule endpoint so working days, holidays, duration, and dependencies are validated atomically."
            });
        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (access.IsViewAs) return WriteForbidden(access);
        var task = await LoadPlanTaskAccessAsync(connection, planTaskId, cancellationToken);
        if (task is null) return Results.NotFound(new { status = "plan_task_not_found" });
        if (!await CanAccessProjectAsync(connection, access, task.Value.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");
        var projectWriteError = await EnsureProjectWritableAsync(connection, task.Value.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        var canEdit = access.CanManage || (access.CanEditAssignedEstimate && task.Value.ReviewerUserId == access.EffectiveUserId);
        if (!canEdit) return Forbidden("EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockProjectAsync(connection, transaction, task.Value.ProjectId, cancellationToken);
        projectWriteError = await EnsureProjectWritableAsync(connection, transaction, task.Value.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        const string sql = """
            UPDATE project_forge_plan_tasks
            SET estimated_hours=@hours,
                hourly_rate=CASE WHEN @can_manage THEN @rate ELSE hourly_rate END,
                material_units=CASE WHEN @can_manage THEN @material_units ELSE material_units END,
                material_unit_cost=CASE WHEN @can_manage THEN @material_cost ELSE material_unit_cost END,
                fixed_cost=CASE WHEN @can_manage THEN @fixed ELSE fixed_cost END,
                travel_cost=CASE WHEN @can_manage THEN @travel ELSE travel_cost END,
                equipment_cost=CASE WHEN @can_manage THEN @equipment ELSE equipment_cost END,
                miscellaneous_cost=CASE WHEN @can_manage THEN @misc ELSE miscellaneous_cost END,
                updated_by_user_id=@actor, updated_at=NOW(), revision_number=revision_number+1
            WHERE plan_task_id=@task_id
              AND canonical_task_id IS NULL
              AND task_status<>'cancelled'
              AND (@expected_version IS NULL OR revision_number=@expected_version)
            RETURNING plan_id, revision_number
            """;
        Guid changedPlanId;
        int version;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("task_id", planTaskId);
            command.Parameters.AddWithValue("hours", request.EstimatedHours);
            command.Parameters.AddWithValue("rate", request.HourlyRate);
            command.Parameters.AddWithValue("material_units", request.MaterialUnits);
            command.Parameters.AddWithValue("material_cost", request.MaterialUnitCost);
            command.Parameters.AddWithValue("fixed", request.FixedCost);
            command.Parameters.AddWithValue("travel", request.TravelCost);
            command.Parameters.AddWithValue("equipment", request.EquipmentCost);
            command.Parameters.AddWithValue("misc", request.MiscCost);
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            command.Parameters.AddWithValue("can_manage", access.CanManage);
            command.Parameters.Add(new NpgsqlParameter("expected_version", NpgsqlDbType.Integer)
            {
                Value = request.ExpectedVersion.HasValue ? (object)request.ExpectedVersion.Value : DBNull.Value
            });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return Results.Conflict(new { status = "estimate_revision_conflict", message = "Refresh the task before saving this estimate." });
            changedPlanId = reader.GetGuid(0);
            version = reader.GetInt32(1);
        }
        await MarkReviewInProgressAsync(connection, transaction, changedPlanId, planTaskId, access.EffectiveUserId, request.ReviewNote, cancellationToken);
        var planRevision = await LoadPlanRevisionAsync(connection, transaction, changedPlanId, cancellationToken);
        object estimateAudit = access.CanManage
            ? new { request.EstimatedHours, request.HourlyRate, request.MaterialUnits, request.MaterialUnitCost, request.FixedCost, request.TravelCost, request.EquipmentCost, request.MiscCost, request.ReviewNote, version }
            : new { request.EstimatedHours, request.ReviewNote, version };
        await InsertAuditAsync(connection, transaction, task.Value.ProjectId, changedPlanId, planTaskId, "ESTIMATE_UPDATED", access,
            estimateAudit, cancellationToken);
        await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.TaskUpdatedPolicy, task.Value.ProjectId, access.EffectiveUserId,
            $"estimate:{planTaskId}:v{version}", new { planId = changedPlanId, planTaskId, taskName = task.Value.TaskName, version, updatedByName = access.DisplayName, changeSummary = "The assigned engineering estimate was updated and remains pending explicit review completion." }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { module = "033", status = "estimate_updated", planTaskId, version, planRevision, stateChanged = true });
    }

    private static async Task<IResult> AdoptPlanAsync(
        Guid planId,
        ProjectForgeAdoptPlanRequest request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!request.ExpectedPlanRevision.HasValue || request.ExpectedPlanRevision.Value < 1)
            return Results.BadRequest(new { status = "expected_plan_revision_required" });
        if (string.IsNullOrWhiteSpace(request.ClientMutationId) || request.ClientMutationId.Trim().Length is < 8 or > 160)
            return Results.BadRequest(new { status = "client_mutation_id_required" });
        if (!string.Equals(request.Confirmation?.Trim(), AdoptConfirmation, StringComparison.Ordinal))
            return Results.BadRequest(new { status = "adoption_confirmation_required", requiredConfirmation = AdoptConfirmation });
        var opened = await OpenForWriteAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;
        if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);
        var plan = await LoadPlanProjectAsync(connection, planId, cancellationToken);
        if (plan is null) return Results.NotFound(new { status = "plan_not_found" });
        if (!await CanAccessProjectAsync(connection, access, plan.Value.ProjectId, null, cancellationToken)) return Forbidden("project_forge_project_scope");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockProjectAsync(connection, transaction, plan.Value.ProjectId, cancellationToken);
        var projectWriteError = await EnsureProjectWritableAsync(connection, transaction, plan.Value.ProjectId, cancellationToken);
        if (projectWriteError is not null) return projectWriteError;
        const string lockSql = """
            SELECT plan_status, source_kind, revision_number
            FROM project_forge_plans
            WHERE plan_id=@plan_id
            FOR UPDATE
            """;
        string status;
        string sourceKind;
        int planRevision;
        await using (var lockCommand = new NpgsqlCommand(lockSql, connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("plan_id", planId);
            await using var reader = await lockCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { status = "plan_not_found" });
            status = reader.GetString(0);
            sourceKind = reader.GetString(1);
            planRevision = reader.GetInt32(2);
        }
        if (planRevision != request.ExpectedPlanRevision.Value)
            return Results.Conflict(new { status = "plan_revision_conflict", revision = planRevision, message = "Refresh the review plan before adoption." });
        if (status == "adopted") return Results.Conflict(new { status = "plan_already_adopted" });
        if (status is not ("draft" or "in_review" or "reviewed" or "changes_requested"))
            return Results.Conflict(new { status = "plan_not_adoptable", planStatus = status });

        const string reviewSql = """
            SELECT COUNT(*) FILTER (WHERE reviewer_user_id IS NOT NULL)::int,
                   COUNT(*) FILTER (WHERE reviewer_user_id IS NOT NULL AND (
                       COALESCE(a.review_status,'pending') <> 'completed'
                       OR a.reviewed_task_revision IS DISTINCT FROM task.revision_number
                   ))::int,
                   COUNT(*)::int
            FROM project_forge_plan_tasks task
            LEFT JOIN project_forge_plan_assignments a
              ON a.plan_task_id=task.plan_task_id AND a.user_id=task.reviewer_user_id AND a.assignment_type='task_estimator'
            WHERE task.plan_id=@plan_id AND task.task_status<>'cancelled'
            """;
        await using (var reviewCommand = new NpgsqlCommand(reviewSql, connection, transaction))
        {
            reviewCommand.Parameters.AddWithValue("plan_id", planId);
            await using var reader = await reviewCommand.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var assignedReviews = reader.GetInt32(0);
            var incompleteReviews = reader.GetInt32(1);
            var planTaskCount = reader.GetInt32(2);
            if (sourceKind == "ai_generated" && (assignedReviews == 0 || assignedReviews != planTaskCount))
                return Results.Conflict(new { status = "engineering_review_required", message = "Every task in an AI-generated plan must be assigned to an eligible project Engineer before adoption.", assignedReviews, planTaskCount });
            if (assignedReviews > 0 && incompleteReviews > 0)
                return Results.Conflict(new { status = "engineering_review_incomplete", assignedReviews, incompleteReviews });
            if (sourceKind == "ai_generated" && status != "reviewed")
                return Results.Conflict(new { status = "engineering_review_evidence_required", message = "The AI-generated plan does not yet have completed Engineer review evidence." });
        }

        var rows = new List<AdoptionTask>();
        const string tasksSql = """
            SELECT plan_task_id, wbs_code, task_name, task_description, task_type, phase_name,
                   priority_code, task_status, kanban_category, decision_action,
                   planned_start_date, planned_end_date, duration_working_days, recurrence_rule::text,
                   percent_complete, estimated_hours, hourly_rate, material_units, material_unit_cost,
                   fixed_cost, travel_cost, equipment_cost, miscellaneous_cost, is_important, is_urgent,
                   task.reviewer_user_id, task.source_kind, task.ai_correlation_id,
                   COALESCE(NULLIF(reviewer.display_name,''),reviewer.email,'Assigned engineer'),
                   task.parent_wbs_code,task.display_order,COALESCE(task.blocked_reason,'')
            FROM project_forge_plan_tasks task
            LEFT JOIN app_users reviewer ON reviewer.user_id=task.reviewer_user_id
            WHERE task.plan_id=@plan_id AND task.canonical_task_id IS NULL AND task.task_status<>'cancelled'
            ORDER BY task.display_order, task.wbs_code
            """;
        await using (var command = new NpgsqlCommand(tasksSql, connection, transaction))
        {
            command.Parameters.AddWithValue("plan_id", planId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new AdoptionTask(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9),
                    ReadDate(reader, 10), ReadDate(reader, 11), reader.GetInt32(12), reader.GetString(13),
                    reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16), reader.GetDecimal(17), reader.GetDecimal(18),
                    reader.GetDecimal(19), reader.GetDecimal(20), reader.GetDecimal(21), reader.GetDecimal(22), reader.GetBoolean(23), reader.GetBoolean(24),
                    reader.IsDBNull(25) ? null : reader.GetGuid(25), reader.GetString(26), reader.IsDBNull(27) ? null : reader.GetString(27),
                    reader.GetString(28),reader.GetString(29),reader.GetInt32(30),reader.GetString(31)));
            }
        }
        if (rows.Count == 0) return Results.BadRequest(new { status = "no_unadopted_plan_tasks" });
        if (request.CreateAssignments)
        {
            foreach (var reviewerUserId in rows.Where(row => row.ReviewerUserId.HasValue).Select(row => row.ReviewerUserId!.Value).Distinct())
            {
                if (!await IsEligibleEngineerReviewerAsync(connection, plan.Value.ProjectId, reviewerUserId, cancellationToken))
                    return Results.Conflict(new
                    {
                        status = "adoption_assignee_no_longer_eligible",
                        reviewerUserId,
                        message = "An assigned Engineer is no longer active on this project. Reassign and complete review again before adoption."
                    });
            }
        }

        var adopted = new List<object>();
        var canonicalByPlanTask = new Dictionary<Guid, Guid>();
        var canonicalByWbs = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var canonicalTaskId = Guid.NewGuid();
            var taskCode = await NextCanonicalTaskCodeAsync(connection, transaction, plan.Value.ProjectId, row.Wbs, cancellationToken);
            await InsertCanonicalTaskAsync(connection, transaction, canonicalTaskId, plan.Value.ProjectId, taskCode, row, access.ActualUserId, cancellationToken);
            await InsertCanonicalDetailAsync(connection, transaction, canonicalTaskId, plan.Value.ProjectId, row, access.ActualUserId, cancellationToken);
            if (request.CreateAssignments && row.ReviewerUserId.HasValue)
                await InsertCanonicalAssignmentAsync(connection, transaction, plan.Value.ProjectId, canonicalTaskId, row.ReviewerUserId.Value, row.EstimatedHours, access.ActualUserId, row.PlannedStartDate, row.PlannedEndDate, cancellationToken);
            await LinkCanonicalTaskAsync(connection, transaction, row.PlanTaskId, canonicalTaskId, access.ActualUserId, cancellationToken);
            canonicalByPlanTask[row.PlanTaskId] = canonicalTaskId;
            canonicalByWbs[row.Wbs] = canonicalTaskId;
            await InsertAuditAsync(connection, transaction, plan.Value.ProjectId, planId, row.PlanTaskId, "TASK_ADOPTED", access,
                new { canonicalTaskId, taskCode, request.CreateAssignments }, cancellationToken);
            if (request.CreateAssignments && row.ReviewerUserId.HasValue)
            {
                await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.TaskAssignedPolicy, plan.Value.ProjectId, row.ReviewerUserId,
                    $"adopt:{planId}:task:{row.PlanTaskId}:assignee:{row.ReviewerUserId}",
                    new { planId, planTaskId = row.PlanTaskId, canonicalTaskId, taskCode, taskName = row.Name, assignedUserId = row.ReviewerUserId, assigneeName = row.ReviewerName }, cancellationToken);
            }
            adopted.Add(new { planTaskId = row.PlanTaskId, canonicalTaskId, taskCode });
        }
        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.ParentWbs)))
        {
            if (!canonicalByPlanTask.TryGetValue(row.PlanTaskId, out var childTaskId)
                || !canonicalByWbs.TryGetValue(row.ParentWbs, out var parentTaskId))
                throw new InvalidOperationException($"Parent WBS {row.ParentWbs} was not adopted with child {row.Wbs}.");
            await using var parent = new NpgsqlCommand("UPDATE project_forge_task_details SET parent_task_id=@parent,updated_by_user_id=@actor,updated_at=NOW() WHERE task_id=@child", connection, transaction);
            parent.Parameters.AddWithValue("parent", parentTaskId);
            parent.Parameters.AddWithValue("child", childTaskId);
            parent.Parameters.AddWithValue("actor", access.ActualUserId);
            await parent.ExecuteNonQueryAsync(cancellationToken);
        }
        const string dependencySql = """
            SELECT dependency_id,predecessor_plan_task_id,successor_plan_task_id,dependency_type,lag_working_days
            FROM project_forge_task_dependencies WHERE plan_id=@plan_id ORDER BY created_at,dependency_id
            """;
        var adoptedDependencies = new List<object>();
        await using (var command = new NpgsqlCommand(dependencySql, connection, transaction))
        {
            command.Parameters.AddWithValue("plan_id", planId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rowsToAdopt = new List<(Guid SourceId, Guid Predecessor, Guid Successor, string Type, int Lag)>();
            while (await reader.ReadAsync(cancellationToken))
                rowsToAdopt.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetInt32(4)));
            await reader.DisposeAsync();
            foreach (var dependency in rowsToAdopt)
            {
                if (!canonicalByPlanTask.TryGetValue(dependency.Predecessor, out var predecessor)
                    || !canonicalByPlanTask.TryGetValue(dependency.Successor, out var successor))
                    throw new InvalidOperationException("Every adopted dependency must reference tasks in the same adoption transaction.");
                var canonicalDependencyId = Guid.NewGuid();
                const string insertDependencySql = """
                    INSERT INTO project_task_dependencies(
                        project_task_dependency_id,project_id,predecessor_task_id,successor_task_id,dependency_type,lag_working_days,
                        created_by_user_id,updated_by_user_id)
                    VALUES(@id,@project_id,@predecessor,@successor,@type,@lag,@actor,@actor)
                    """;
                await using var insert = new NpgsqlCommand(insertDependencySql, connection, transaction);
                insert.Parameters.AddWithValue("id", canonicalDependencyId);
                insert.Parameters.AddWithValue("project_id", plan.Value.ProjectId);
                insert.Parameters.AddWithValue("predecessor", predecessor);
                insert.Parameters.AddWithValue("successor", successor);
                insert.Parameters.AddWithValue("type", dependency.Type);
                insert.Parameters.AddWithValue("lag", dependency.Lag);
                insert.Parameters.AddWithValue("actor", access.ActualUserId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                adoptedDependencies.Add(new { sourceDependencyId = dependency.SourceId, canonicalDependencyId, predecessorTaskId = predecessor, successorTaskId = successor });
            }
        }
        const string adoptSql = """
            UPDATE project_forge_plans
            SET plan_status='adopted', adopted_by_user_id=@actor, adopted_at=NOW(),
                review_notes=CASE WHEN @note='' THEN review_notes ELSE @note END,
                updated_by_user_id=@actor, updated_at=NOW(), revision_number=revision_number+1
            WHERE plan_id=@plan_id
            """;
        await using (var command = new NpgsqlCommand(adoptSql, connection, transaction))
        {
            command.Parameters.AddWithValue("actor", access.ActualUserId);
            command.Parameters.AddWithValue("note", Clean(request.AdoptionNote, 4000, string.Empty));
            command.Parameters.AddWithValue("plan_id", planId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertAuditAsync(connection, transaction, plan.Value.ProjectId, planId, null, "PLAN_ADOPTED", access,
            new { taskCount = adopted.Count, request.CreateAssignments, request.AdoptionNote }, cancellationToken);
        await InsertNotificationAsync(connection, transaction, ProjectForgePolicy.PlanUpdatedPolicy, plan.Value.ProjectId, null,
            $"plan:{planId}:adopted", new { planId, planName = plan.Value.PlanName, taskCount = adopted.Count, updatedByName = access.DisplayName, changeSummary = "The reviewed plan was adopted into canonical project tasks." }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new
        {
            module = "033",
            status = "plan_adopted_to_canonical_project",
            planId,
            canonicalTasks = adopted,
            canonicalDependencies = adoptedDependencies,
            assignmentsCreated = request.CreateAssignments,
            stateChanged = true
        });
    }

    private static IResult? CandidateAiDraftMutationBlocked()
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.Snapshot();
        if (!release.IsCandidate) return null;
        return Results.Json(new
        {
            module = ProjectForgePolicy.ModuleCode,
            status = "release_candidate_read_only",
            message = "Project Forge AI draft generation and persistence are disabled on the exact-source release candidate.",
            configurationSourceCommit = release.ConfigurationSourceCommit,
            stateChanged = false
        }, statusCode: StatusCodes.Status423Locked);
    }

    private static async Task InsertPlanAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid planId,
        ProjectForgePlanSaveRequest request,
        string sourceKind,
        CelarAiComposeResult? ai,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO project_forge_plans(
                plan_id, project_id, plan_name, objective, plan_status, source_kind,
                planned_start_date, planned_end_date, ai_capability_code, ai_provider_code, ai_correlation_id,
                ai_confidence, ai_evidence, ai_citations, ai_warnings, review_notes,
                created_by_user_id, updated_by_user_id)
            VALUES(
                @plan_id,@project_id,@name,@objective,'draft',@source_kind,
                @start_date,@end_date,@capability,@provider,@correlation,@confidence,@evidence,@citations,@warnings,@review_notes,
                @actor,@actor)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("project_id", request.ProjectId);
        command.Parameters.AddWithValue("name", Clean(request.PlanName, 240, "Project plan"));
        command.Parameters.AddWithValue("objective", Clean(request.Objective, 4000, string.Empty));
        command.Parameters.AddWithValue("source_kind", sourceKind);
        AddNullableDate(command, "start_date", request.StartDate);
        AddNullableDate(command, "end_date", LatestDueDate(request.Tasks));
        command.Parameters.AddWithValue("capability", ai is null ? string.Empty : ProjectForgePolicy.CapabilityCode);
        command.Parameters.AddWithValue("provider", ai?.PrimaryExecutionPath ?? string.Empty);
        command.Parameters.AddWithValue("correlation", ai?.CorrelationId ?? string.Empty);
        command.Parameters.AddWithValue("confidence", ai?.Confidence ?? 0m);
        AddJson(command, "evidence", ai is null ? new { } : new
        {
            ai.DetailedAnswer,
            ai.FlowHivePlan,
            ai.Timeline,
            ai.Diagram,
            ai.MissingEvidence,
            ai.Conflicts,
            ai.ConfidenceExplanation,
            ai.DataAsOf
        });
        AddJson(command, "citations", ai?.Citations ?? []);
        AddJson(command, "warnings", ai?.Warnings ?? []);
        command.Parameters.AddWithValue("review_notes", Clean(request.ReviewNote, 4000, string.Empty));
        command.Parameters.AddWithValue("actor", actorUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplacePlanRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid planId,
        IReadOnlyList<ProjectForgePlanTaskRequest> tasks,
        IReadOnlyList<ProjectForgeDependencyRequest> dependencies,
        Guid actorUserId,
        CancellationToken cancellationToken,
        string? aiCorrelationId = null)
    {
        await using (var delete = new NpgsqlCommand("DELETE FROM project_forge_plan_tasks WHERE plan_id=@plan_id", connection, transaction))
        {
            delete.Parameters.AddWithValue("plan_id", planId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        var projectId = await PlanProjectIdAsync(connection, transaction, planId, cancellationToken);
        var byWbs = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            var id = task.PlanTaskId ?? Guid.NewGuid();
            var wbs = Clean(task.Wbs, 80, (index + 1).ToString());
            if (!byWbs.TryAdd(wbs, id)) throw new InvalidOperationException($"Duplicate WBS code: {wbs}");
            const string sql = """
                INSERT INTO project_forge_plan_tasks(
                    plan_task_id,plan_id,project_id,wbs_code,parent_wbs_code,task_name,task_description,
                    task_type,phase_name,priority_code,task_status,kanban_category,decision_action,
                    planned_start_date,planned_end_date,duration_working_days,recurrence_rule,percent_complete,
                    estimated_hours,hourly_rate,material_units,material_unit_cost,fixed_cost,travel_cost,equipment_cost,miscellaneous_cost,
                    is_important,is_urgent,reviewer_user_id,source_kind,ai_correlation_id,display_order,
                    created_by_user_id,updated_by_user_id)
                VALUES(
                    @id,@plan_id,@project_id,@wbs,@parent,@name,@description,@task_type,@phase,@priority,@status,@kanban,@decision,
                    @start_date,@end_date,@duration,@recurrence,@percent,@hours,@rate,@material_units,@material_cost,@fixed,@travel,@equipment,@misc,
                    @important,@urgent,@reviewer,@source_kind,@correlation,@display_order,@actor,@actor)
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("wbs", wbs);
            command.Parameters.AddWithValue("parent", Clean(task.ParentWbs, 80, string.Empty));
            command.Parameters.AddWithValue("name", TaskName(task.Name, index + 1));
            command.Parameters.AddWithValue("description", Clean(task.Description, 4000, string.Empty));
            command.Parameters.AddWithValue("task_type", Normalize(task.TaskType, "variable", "variable", "recurring"));
            command.Parameters.AddWithValue("phase", Clean(task.Phase, 120, string.Empty));
            command.Parameters.AddWithValue("priority", Normalize(task.Priority, "normal", "low", "normal", "high", "critical"));
            command.Parameters.AddWithValue("status", Normalize(task.Status, "draft", "draft", "in_review", "approved", "rejected", "not_started", "in_progress", "blocked", "on_hold", "completed", "cancelled"));
            command.Parameters.AddWithValue("kanban", Normalize(task.KanbanCategory, "backlog", "backlog", "ready", "in_progress", "review", "blocked", "done"));
            command.Parameters.AddWithValue("decision", Normalize(task.DecisionAction, "none", "none", "do", "delegate", "decide", "delete"));
            AddNullableDate(command, "start_date", task.StartDate);
            AddNullableDate(command, "end_date", task.DueDate);
            command.Parameters.AddWithValue("duration", Math.Clamp(task.DurationWorkingDays, 0, 3660));
            AddJsonText(command, "recurrence", task.RecurrenceRule);
            command.Parameters.AddWithValue("percent", Math.Clamp(task.PercentComplete, 0m, 100m));
            command.Parameters.AddWithValue("hours", Math.Max(0m, task.EstimatedHours));
            command.Parameters.AddWithValue("rate", Math.Max(0m, task.HourlyRate));
            command.Parameters.AddWithValue("material_units", Math.Max(0m, task.MaterialUnits));
            command.Parameters.AddWithValue("material_cost", Math.Max(0m, task.MaterialUnitCost));
            command.Parameters.AddWithValue("fixed", Math.Max(0m, task.FixedCost));
            command.Parameters.AddWithValue("travel", Math.Max(0m, task.TravelCost));
            command.Parameters.AddWithValue("equipment", Math.Max(0m, task.EquipmentCost));
            command.Parameters.AddWithValue("misc", Math.Max(0m, task.MiscCost));
            command.Parameters.AddWithValue("important", task.Important);
            command.Parameters.AddWithValue("urgent", task.Urgent);
            AddNullableUuid(command, "reviewer", task.ReviewerUserId);
            command.Parameters.AddWithValue("source_kind", aiCorrelationId is null ? "manual" : "ai_generated");
            command.Parameters.AddWithValue("correlation", aiCorrelationId ?? string.Empty);
            command.Parameters.AddWithValue("display_order", index + 1);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var dependency in dependencies)
        {
            var predecessor = Clean(dependency.PredecessorWbs, 80, string.Empty);
            var successor = Clean(dependency.SuccessorWbs, 80, string.Empty);
            if (!byWbs.TryGetValue(predecessor, out var predecessorId) || !byWbs.TryGetValue(successor, out var successorId))
                throw new InvalidOperationException("Every dependency must reference WBS codes in the same plan.");
            const string sql = """
                INSERT INTO project_forge_task_dependencies(
                    dependency_id,plan_id,project_id,predecessor_plan_task_id,successor_plan_task_id,
                    dependency_type,lag_working_days,created_by_user_id,updated_by_user_id)
                VALUES(@id,@plan_id,@project_id,@predecessor,@successor,@type,@lag,@actor,@actor)
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", dependency.DependencyId ?? Guid.NewGuid());
            command.Parameters.AddWithValue("plan_id", planId);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("predecessor", predecessorId);
            command.Parameters.AddWithValue("successor", successorId);
            command.Parameters.AddWithValue("type", Normalize(dependency.DependencyType, "FS", "FS", "SS", "FF", "SF").ToUpperInvariant());
            command.Parameters.AddWithValue("lag", Math.Clamp(dependency.LagWorkingDays, -3650, 3650));
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpsertPlanAssignmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid planId,
        Guid planTaskId,
        Guid projectId,
        Guid reviewerUserId,
        decimal hours,
        string? note,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO project_forge_plan_assignments(
                plan_assignment_id,plan_id,plan_task_id,project_id,user_id,assignment_type,
                planned_hours,allocation_percent,review_status,assignment_notes,assigned_by_user_id)
            VALUES(gen_random_uuid(),@plan_id,@task_id,@project_id,@user_id,'task_estimator',@hours,100,'assigned',@note,@actor)
            ON CONFLICT(plan_task_id,user_id,assignment_type) DO UPDATE
            SET planned_hours=EXCLUDED.planned_hours, review_status='assigned', assignment_notes=EXCLUDED.assignment_notes,
                assigned_by_user_id=EXCLUDED.assigned_by_user_id, completed_at=NULL, reviewed_task_revision=NULL,
                updated_at=NOW(), revision_number=project_forge_plan_assignments.revision_number+1
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("task_id", planTaskId);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", reviewerUserId);
        command.Parameters.AddWithValue("hours", Math.Max(0m, hours));
        command.Parameters.AddWithValue("note", Clean(note, 4000, string.Empty));
        command.Parameters.AddWithValue("actor", actorUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkReviewCompleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid planId,
        Guid planTaskId,
        Guid reviewerUserId,
        string? note,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE project_forge_plan_assignments
            SET review_status='completed', assignment_notes=CASE WHEN @note='' THEN assignment_notes ELSE @note END,
                completed_at=NOW(), updated_at=NOW(), revision_number=revision_number+1
            WHERE plan_id=@plan_id AND plan_task_id=@task_id AND user_id=@user_id AND assignment_type='task_estimator'
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("task_id", planTaskId);
        command.Parameters.AddWithValue("user_id", reviewerUserId);
        command.Parameters.AddWithValue("note", Clean(note, 4000, string.Empty));
        await command.ExecuteNonQueryAsync(cancellationToken);

        const string completePlanSql = """
            UPDATE project_forge_plans plan
            SET plan_status='reviewed', reviewed_by_user_id=@user_id, reviewed_at=NOW(),
                updated_by_user_id=@user_id, updated_at=NOW(), revision_number=revision_number+1
            WHERE plan.plan_id=@plan_id
              AND plan.plan_status IN ('draft','in_review','changes_requested','reviewed')
              AND NOT EXISTS(
                  SELECT 1
                  FROM project_forge_plan_tasks task
                  LEFT JOIN project_forge_plan_assignments review
                    ON review.plan_task_id=task.plan_task_id
                   AND review.user_id=task.reviewer_user_id
                   AND review.assignment_type='task_estimator'
                  WHERE task.plan_id=plan.plan_id
                    AND (task.reviewer_user_id IS NULL OR COALESCE(review.review_status,'pending') <> 'completed')
              )
            """;
        await using var completePlan = new NpgsqlCommand(completePlanSql, connection, transaction);
        completePlan.Parameters.AddWithValue("plan_id", planId);
        completePlan.Parameters.AddWithValue("user_id", reviewerUserId);
        await completePlan.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkReviewInProgressAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid planId,
        Guid planTaskId,
        Guid reviewerUserId,
        string? note,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE project_forge_plan_assignments
            SET review_status='in_progress',assignment_notes=CASE WHEN @note='' THEN assignment_notes ELSE @note END,
                completed_at=NULL,reviewed_task_revision=NULL,updated_at=NOW()
            WHERE plan_id=@plan_id AND plan_task_id=@task_id AND user_id=@user_id AND assignment_type='task_estimator';
            UPDATE project_forge_plans
            SET plan_status='in_review',reviewed_by_user_id=NULL,reviewed_at=NULL,updated_by_user_id=@user_id,updated_at=NOW()
            WHERE plan_id=@plan_id AND plan_status IN ('draft','in_review','changes_requested','reviewed');
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("plan_id", planId);
        command.Parameters.AddWithValue("task_id", planTaskId);
        command.Parameters.AddWithValue("user_id", reviewerUserId);
        command.Parameters.AddWithValue("note", Clean(note, 4000, string.Empty));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetPlanInReviewAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid planId, Guid actor, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE project_forge_plans SET plan_status='in_review',updated_by_user_id=@actor,updated_at=NOW(),revision_number=revision_number+1 WHERE plan_id=@id AND plan_status<>'adopted'", connection, transaction);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("id", planId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCanonicalTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        Guid projectId,
        string taskCode,
        AdoptionTask row,
        Guid actor,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO project_tasks(task_id,project_id,task_code,task_name,task_description,billable,is_active,revision_number,updated_by_user_id)
            VALUES(@id,@project_id,@code,@name,@description,TRUE,TRUE,1,@actor)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", taskId);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("code", taskCode);
        command.Parameters.AddWithValue("name", row.Name);
        command.Parameters.AddWithValue("description", row.Description);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCanonicalDetailAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        Guid projectId,
        AdoptionTask row,
        Guid actor,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO project_forge_task_details(
                task_id,project_id,source_plan_task_id,task_type,phase_name,priority_code,task_status,
                kanban_category,decision_action,planned_start_date,planned_end_date,duration_working_days,display_order,blocked_reason,recurrence_rule,
                percent_complete,estimated_hours,hourly_rate,material_units,material_unit_cost,fixed_cost,
                travel_cost,equipment_cost,miscellaneous_cost,is_important,is_urgent,source_kind,ai_correlation_id,
                created_by_user_id,updated_by_user_id)
            VALUES(@task_id,@project_id,@source_task,@task_type,@phase,@priority,@status,@kanban,@decision,
                   @start_date,@end_date,@duration,@display_order,@blocked_reason,@recurrence,@percent,@hours,@rate,@material_units,@material_cost,@fixed,
                   @travel,@equipment,@misc,@important,@urgent,@source_kind,@correlation,@actor,@actor)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("source_task", row.PlanTaskId);
        command.Parameters.AddWithValue("task_type", row.TaskType == "recurring" ? "recurring" : "variable");
        command.Parameters.AddWithValue("phase", row.Phase);
        command.Parameters.AddWithValue("priority", Normalize(row.Priority, "normal", "low", "normal", "high", "critical"));
        command.Parameters.AddWithValue("status", Normalize(row.Status, "not_started", "not_started", "in_progress", "blocked", "on_hold", "completed", "cancelled"));
        command.Parameters.AddWithValue("kanban", Normalize(row.Kanban, "backlog", "backlog", "ready", "in_progress", "review", "blocked", "done"));
        command.Parameters.AddWithValue("decision", Normalize(row.Decision, "none", "none", "do", "delegate", "decide", "delete"));
        AddNullableDate(command, "start_date", row.PlannedStartDate);
        AddNullableDate(command, "end_date", row.PlannedEndDate);
        command.Parameters.AddWithValue("duration", Math.Clamp(row.DurationWorkingDays, 0, 730));
        command.Parameters.AddWithValue("display_order", Math.Max(0, row.DisplayOrder));
        command.Parameters.AddWithValue("blocked_reason", Clean(row.BlockedReason, 2000, string.Empty));
        AddJsonText(command, "recurrence", row.RecurrenceRule);
        command.Parameters.AddWithValue("percent", row.PercentComplete);
        command.Parameters.AddWithValue("hours", row.EstimatedHours);
        command.Parameters.AddWithValue("rate", row.HourlyRate);
        command.Parameters.AddWithValue("material_units", row.MaterialUnits);
        command.Parameters.AddWithValue("material_cost", row.MaterialUnitCost);
        command.Parameters.AddWithValue("fixed", row.FixedCost);
        command.Parameters.AddWithValue("travel", row.TravelCost);
        command.Parameters.AddWithValue("equipment", row.EquipmentCost);
        command.Parameters.AddWithValue("misc", row.MiscCost);
        command.Parameters.AddWithValue("important", row.Important);
        command.Parameters.AddWithValue("urgent", row.Urgent);
        command.Parameters.AddWithValue("source_kind", row.SourceKind == "ai_generated" ? "ai_draft" : "pm_created");
        command.Parameters.AddWithValue("correlation", row.AiCorrelationId ?? string.Empty);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCanonicalAssignmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        Guid taskId,
        Guid userId,
        decimal assignedHours,
        Guid actor,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO project_assignments(
                project_assignment_id,project_id,task_id,user_id,assigned_by_user_id,
                effective_start_date,effective_end_date,allocation_percent,assigned_hours,is_primary_assignee,updated_by_user_id)
            VALUES(gen_random_uuid(),@project_id,@task_id,@user_id,@actor,COALESCE(@start_date,CURRENT_DATE),@end_date,100,@hours,TRUE,@actor)
            ON CONFLICT DO NOTHING
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("actor", actor);
        AddNullableDate(command, "start_date", startDate);
        AddNullableDate(command, "end_date", endDate);
        command.Parameters.AddWithValue("hours", Math.Max(0m, assignedHours));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LinkCanonicalTaskAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid planTaskId, Guid taskId, Guid actor, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("UPDATE project_forge_plan_tasks SET canonical_task_id=@task_id,updated_by_user_id=@actor,updated_at=NOW(),revision_number=revision_number+1 WHERE plan_task_id=@plan_task_id", connection, transaction);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("plan_task_id", planTaskId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        Guid? planId,
        Guid? planTaskId,
        string eventCode,
        ProjectForgeAccess access,
        object metadata,
        CancellationToken cancellationToken)
    {
        var entityId = planTaskId ?? planId ?? projectId;
        const string sql = """
            INSERT INTO project_forge_audit_events(
                audit_event_id,project_id,plan_id,plan_task_id,event_code,entity_type,entity_id,
                actual_actor_user_id,effective_actor_user_id,event_metadata,correlation_id)
            VALUES(gen_random_uuid(),@project_id,@plan_id,@plan_task_id,@event_code,@entity_type,@entity_id,
                   @actual,@effective,@metadata,@correlation)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        AddNullableUuid(command, "plan_id", planId);
        AddNullableUuid(command, "plan_task_id", planTaskId);
        command.Parameters.AddWithValue("event_code", eventCode);
        command.Parameters.AddWithValue("entity_type", planTaskId.HasValue ? "plan_task" : planId.HasValue ? "plan" : "project");
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("actual", access.ActualUserId);
        command.Parameters.AddWithValue("effective", access.EffectiveUserId);
        AddJson(command, "metadata", metadata);
        command.Parameters.AddWithValue("correlation", contextCorrelation(metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);

        static string contextCorrelation(object value)
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("correlationId", out var property) ? property.GetString() ?? string.Empty : string.Empty;
        }
    }

    private static async Task InsertNotificationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string policyCode,
        Guid projectId,
        Guid? subjectUserId,
        string sourceEventId,
        object payload,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH policy AS (
                SELECT policy_code,enabled FROM enterprise_notification_policies WHERE policy_code=@policy
            ), inserted AS (
                INSERT INTO enterprise_notification_events(
                    enterprise_notification_event_id,policy_code,source_module,source_event_id,idempotency_key,
                    entity_type,entity_id,project_id,subject_user_id,occurred_at,available_at,payload,ingestion_source,event_status)
                SELECT gen_random_uuid(),policy.policy_code,'033',@source_id,@idempotency,'project_forge',@project_id,@project_id,@subject,
                       NOW(),NOW(),@payload,'native_bridge',CASE WHEN policy.enabled THEN 'pending' ELSE 'suppressed' END
                FROM policy
                ON CONFLICT(idempotency_key) DO NOTHING
                RETURNING enterprise_notification_event_id,event_status
            ), history AS (
                INSERT INTO enterprise_notification_event_history(
                    enterprise_notification_event_history_id,enterprise_notification_event_id,history_code,event_status,
                    diagnostic_code,history_metadata,correlation_id)
                SELECT gen_random_uuid(),enterprise_notification_event_id,'EVENT_ACCEPTED',event_status,
                       'PROJECT_FORGE_NATIVE_EVENT',jsonb_build_object('sourceModule','033','policyCode',@policy),@source_id
                FROM inserted
            )
            SELECT (SELECT COUNT(*) FROM policy)::int,(SELECT COUNT(*) FROM inserted)::int
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("policy", policyCode);
        command.Parameters.AddWithValue("source_id", Clean(sourceEventId, 320, Guid.NewGuid().ToString("N")));
        command.Parameters.AddWithValue("idempotency", $"033:{policyCode}:{sourceEventId}"[..Math.Min(420, $"033:{policyCode}:{sourceEventId}".Length)]);
        command.Parameters.AddWithValue("project_id", projectId);
        AddNullableUuid(command, "subject", subjectUserId);
        AddJson(command, "payload", payload);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var policyCount = reader.GetInt32(0);
        if (policyCount != 1)
            throw new InvalidOperationException($"Required Module 065 policy {policyCode} is not registered; the Project Forge write was rolled back.");
    }

    private const string ScopeCte = """
        WITH authorized_lead_pms AS (
            SELECT DISTINCT pm.user_id
            FROM app_users pm
            WHERE pm.is_active=TRUE
              AND (
                EXISTS (
                    SELECT 1 FROM reporting_relationships rr
                    WHERE rr.employee_user_id=pm.user_id
                      AND (rr.manager_user_id=@effective_user_id OR rr.team_lead_user_id=@effective_user_id)
                      AND rr.effective_start_date<=CURRENT_DATE
                      AND (rr.effective_end_date IS NULL OR rr.effective_end_date>=CURRENT_DATE)
                )
                OR EXISTS (
                    SELECT 1 FROM projectpulse_team_scope_assignments scope
                    WHERE scope.scoped_user_id=@effective_user_id AND scope.is_active=TRUE
                      AND scope.scope_type='project_management_team_lead'
                      AND ((scope.team_name IS NOT NULL AND LOWER(COALESCE(pm.team_name,''))=LOWER(scope.team_name))
                        OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(pm.department_name,pm.department,''))=LOWER(scope.department_name))
                        OR scope.manager_user_id=pm.user_id)
                )
              )
        ), authorized_engineering_members AS (
            SELECT DISTINCT member.user_id
            FROM app_users member
            WHERE member.is_active=TRUE
              AND (
                EXISTS(
                    SELECT 1 FROM reporting_relationships rr
                    WHERE rr.employee_user_id=member.user_id
                      AND (rr.manager_user_id=@effective_user_id OR rr.team_lead_user_id=@effective_user_id)
                      AND rr.effective_start_date<=CURRENT_DATE
                      AND (rr.effective_end_date IS NULL OR rr.effective_end_date>=CURRENT_DATE)
                )
                OR EXISTS(
                    SELECT 1 FROM projectpulse_team_scope_assignments scope
                    WHERE scope.scoped_user_id=@effective_user_id AND scope.is_active=TRUE
                      AND scope.scope_type='engineering_team_lead'
                      AND ((scope.team_name IS NOT NULL AND LOWER(COALESCE(member.team_name,''))=LOWER(scope.team_name))
                        OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(member.department_name,member.department,''))=LOWER(scope.department_name))
                        OR scope.manager_user_id=member.user_id)
                )
              )
        ), scoped_projects AS (
            SELECT p.project_id
            FROM projects p
            WHERE (
                @is_admin
                OR (@is_pm_lead AND p.project_manager_user_id IN (SELECT user_id FROM authorized_lead_pms))
                OR (@is_pm AND p.project_manager_user_id=@effective_user_id)
                OR (@is_engineer AND EXISTS (
                    SELECT 1 FROM project_assignments self_assignment
                    WHERE self_assignment.project_id=p.project_id AND self_assignment.user_id=@effective_user_id
                      AND self_assignment.effective_start_date<=CURRENT_DATE
                      AND (self_assignment.effective_end_date IS NULL OR self_assignment.effective_end_date>=CURRENT_DATE)
                ))
                OR (@is_engineering_lead AND EXISTS(
                    SELECT 1 FROM project_assignments team_assignment
                    WHERE team_assignment.project_id=p.project_id
                      AND team_assignment.user_id IN (SELECT user_id FROM authorized_engineering_members)
                      AND team_assignment.effective_start_date<=CURRENT_DATE
                      AND (team_assignment.effective_end_date IS NULL OR team_assignment.effective_end_date>=CURRENT_DATE)
                ))
                OR (@is_account_executive AND p.account_executive_user_id=@effective_user_id)
                OR (@is_solution_architect AND p.solution_architect_user_id=@effective_user_id)
                OR EXISTS(
                    SELECT 1 FROM project_planning_collaborators collaborator
                    WHERE collaborator.project_id=p.project_id
                      AND collaborator.user_id=@effective_user_id
                      AND collaborator.module_code='033'
                      AND collaborator.is_active=TRUE
                      AND collaborator.effective_start_date<=CURRENT_DATE
                      AND (collaborator.effective_end_date IS NULL OR collaborator.effective_end_date>=CURRENT_DATE)
                )
            )
            AND (@manager_filter IS NULL OR p.project_manager_user_id=@manager_filter)
            AND (@project_filter IS NULL OR p.project_id=@project_filter)
        )
        """;

    private static readonly string ProjectsSql = ScopeCte + """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT p.project_id AS "projectId",p.project_code AS "projectCode",p.project_name AS "projectName",
                   COALESCE(p.project_description,'') AS "description",COALESCE(c.client_name,'No customer') AS "customerName",
                   p.status,p.start_date AS "startDate",p.end_date AS "endDate",p.project_manager_user_id AS "projectManagerUserId",
                   COALESCE(NULLIF(pm.display_name,''),pm.email,'Unassigned') AS "projectManagerName",
                   CASE WHEN @can_view_financials THEN COALESCE(p.planned_total_project_cost,0) ELSE NULL END AS "plannedCost",
                   COALESCE(time_summary.actual_hours,0) AS "actualHours",CASE WHEN @can_view_financials THEN COALESCE(expense_summary.expenses,0) ELSE NULL END AS "expenses",
                   COALESCE(task_summary.task_count,0) AS "taskCount",COALESCE(task_summary.completed_count,0) AS "completedTaskCount",
                   COALESCE(task_summary.open_count,0) AS "openTaskCount",COALESCE(task_summary.due_this_month_count,0) AS "dueThisMonthCount",
                   COALESCE(task_summary.estimated_hours,0) AS "estimatedHours",COALESCE(task_summary.progress_percent,0) AS "progressPercent"
            FROM scoped_projects scope
            JOIN projects p ON p.project_id=scope.project_id
            LEFT JOIN clients c ON c.client_id=p.client_id
            LEFT JOIN app_users pm ON pm.user_id=p.project_manager_user_id
            LEFT JOIN LATERAL (SELECT COUNT(*)::int task_count,
                                      COUNT(*) FILTER(WHERE COALESCE(d.task_status,'not_started')='completed')::int completed_count,
                                      COUNT(*) FILTER(WHERE COALESCE(d.task_status,'not_started') NOT IN ('completed','cancelled'))::int open_count,
                                      COUNT(*) FILTER(WHERE d.planned_end_date>=date_trunc('month',CURRENT_DATE)::date
                                                       AND d.planned_end_date<(date_trunc('month',CURRENT_DATE)+INTERVAL '1 month')::date)::int due_this_month_count,
                                      COALESCE(SUM(d.estimated_hours),0)::numeric estimated_hours,
                                      COALESCE(AVG(COALESCE(d.percent_complete,0)),0)::numeric(5,2) progress_percent
                               FROM project_tasks t LEFT JOIN project_forge_task_details d ON d.task_id=t.task_id
                               WHERE t.project_id=p.project_id AND t.is_active=TRUE
                                 AND (@can_view_all_tasks OR EXISTS(
                                     SELECT 1 FROM project_assignments own
                                     WHERE own.task_id=t.task_id AND own.project_id=t.project_id
                                       AND own.user_id=@effective_user_id
                                       AND own.effective_start_date<=CURRENT_DATE
                                       AND (own.effective_end_date IS NULL OR own.effective_end_date>=CURRENT_DATE)
                                 ))) task_summary ON TRUE
            LEFT JOIN LATERAL (SELECT COALESCE(SUM(te.hours),0)::numeric actual_hours FROM time_entries te
                               WHERE te.project_id=p.project_id AND te.status NOT IN ('manager_declined','pm_declined')
                                 AND (@can_view_all_tasks OR EXISTS(
                                     SELECT 1 FROM project_assignments own
                                     WHERE own.task_id=te.task_id AND own.project_id=te.project_id
                                       AND own.user_id=@effective_user_id
                                       AND own.effective_start_date<=CURRENT_DATE
                                       AND (own.effective_end_date IS NULL OR own.effective_end_date>=CURRENT_DATE)
                                 ))) time_summary ON TRUE
            LEFT JOIN LATERAL (SELECT COALESCE(SUM(u.total_amount),0)::numeric expenses FROM project_expense_uploads u
                               WHERE u.project_id=p.project_id AND u.is_current=TRUE AND u.deleted_at IS NULL) expense_summary ON TRUE
            ORDER BY p.start_date DESC NULLS LAST,p.project_code
        ) row_data
        """;

    private static readonly string TasksSql = ScopeCte + """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT t.task_id AS "taskId",NULL::uuid AS "planTaskId",NULL::uuid AS "planId",t.project_id AS "projectId",t.task_code AS "taskCode",
                   t.task_name AS "taskName",COALESCE(t.task_description,'') AS "description",'canonical' AS "recordSource",
                   COALESCE(d.task_type,'variable') AS "taskType",COALESCE(d.phase_name,'') AS "phase",
                   COALESCE(d.priority_code,'normal') AS "priority",COALESCE(d.task_status,'not_started') AS "status",
                   COALESCE(d.kanban_category,'backlog') AS "kanbanCategory",COALESCE(d.decision_action,'none') AS "decisionAction",
                   d.planned_start_date AS "startDate",d.planned_end_date AS "dueDate",COALESCE(d.duration_working_days,0) AS "durationWorkingDays",COALESCE(d.percent_complete,0) AS "percentComplete",
                   COALESCE(d.estimated_hours,assignment_summary.assigned_hours,0) AS "estimatedHours",
                   COALESCE(actuals.actual_hours,0) AS "actualHours",CASE WHEN @can_view_financials THEN COALESCE(d.hourly_rate,0) ELSE NULL END AS "hourlyRate",
                   CASE WHEN @can_view_financials THEN COALESCE(d.material_units*d.material_unit_cost+d.fixed_cost+d.travel_cost+d.equipment_cost+d.miscellaneous_cost,0) ELSE NULL END AS "nonLaborEstimate",
                   CASE WHEN @can_view_financials THEN COALESCE(d.material_units,0) ELSE NULL END AS "materialUnits",CASE WHEN @can_view_financials THEN COALESCE(d.material_unit_cost,0) ELSE NULL END AS "materialUnitCost",
                   CASE WHEN @can_view_financials THEN COALESCE(d.fixed_cost,0) ELSE NULL END AS "fixedCost",CASE WHEN @can_view_financials THEN COALESCE(d.travel_cost,0) ELSE NULL END AS "travelCost",
                   CASE WHEN @can_view_financials THEN COALESCE(d.equipment_cost,0) ELSE NULL END AS "equipmentCost",CASE WHEN @can_view_financials THEN COALESCE(d.miscellaneous_cost,0) ELSE NULL END AS "miscCost",
                   COALESCE(d.recurrence_rule,'{}'::jsonb) AS "recurrenceRule",
                   COALESCE(d.is_important,FALSE) AS "important",COALESCE(d.is_urgent,FALSE) AS "urgent",NULL::uuid AS "reviewerUserId",NULL::text AS "reviewerName",
                   assignment_summary.assignee_names AS "assigneeName",assignment_summary.primary_assignee_user_id AS "assigneeUserId",
                   assignment_summary.primary_assignee_name AS "primaryAssigneeName",COALESCE(d.parent_task_id,NULL) AS "parentTaskId",
                   COALESCE(d.display_order,0) AS "displayOrder",COALESCE(d.blocked_reason,'') AS "blockedReason",
                   NULL::text AS "reviewStatus",NULL::text AS "reviewNote",
                   FALSE AS "canEditEstimate",(t.revision_number+COALESCE(d.revision_number,0))::int AS "revision",
                   t.revision_number AS "taskRevision",COALESCE(d.revision_number,0) AS "planningRevision",
                   (@can_manage AND NOT @is_view_as) AS "canEditDetails",
                   ((@can_manage OR (@can_update_assigned_status AND assignment_summary.is_current_user_assigned)) AND NOT @is_view_as) AS "canEditWorkflow",
                   (@can_manage AND NOT @is_view_as) AS "canEditSchedule",(@can_manage AND NOT @is_view_as) AS "canAssign",
                   (@can_manage AND NOT @is_view_as) AS "canArchive"
            FROM scoped_projects scope JOIN project_tasks t ON t.project_id=scope.project_id
            LEFT JOIN project_forge_task_details d ON d.task_id=t.task_id
            LEFT JOIN LATERAL(SELECT COALESCE(SUM(pa.assigned_hours),0)::numeric assigned_hours,
                                     string_agg(DISTINCT COALESCE(NULLIF(assignee.display_name,''),assignee.email),', ' ORDER BY COALESCE(NULLIF(assignee.display_name,''),assignee.email)) assignee_names,
                                     (array_agg(pa.user_id ORDER BY pa.is_primary_assignee DESC,pa.effective_start_date DESC,pa.project_assignment_id))[1] primary_assignee_user_id,
                                     (array_agg(COALESCE(NULLIF(assignee.display_name,''),assignee.email) ORDER BY pa.is_primary_assignee DESC,pa.effective_start_date DESC,pa.project_assignment_id))[1] primary_assignee_name,
                                     COALESCE(bool_or(pa.user_id=@effective_user_id AND pa.effective_start_date<=CURRENT_DATE AND (pa.effective_end_date IS NULL OR pa.effective_end_date>=CURRENT_DATE)),FALSE) is_current_user_assigned
                              FROM project_assignments pa JOIN app_users assignee ON assignee.user_id=pa.user_id
                              WHERE pa.task_id=t.task_id AND pa.effective_start_date<=CURRENT_DATE
                                AND (pa.effective_end_date IS NULL OR pa.effective_end_date>=CURRENT_DATE)) assignment_summary ON TRUE
            LEFT JOIN LATERAL(SELECT COALESCE(SUM(te.hours),0)::numeric actual_hours FROM time_entries te WHERE te.task_id=t.task_id AND te.status NOT IN ('manager_declined','pm_declined')) actuals ON TRUE
            WHERE @workspace='canonical' AND t.is_active=TRUE AND (@can_view_all_tasks OR EXISTS(
                SELECT 1 FROM project_assignments own
                WHERE own.task_id=t.task_id AND own.project_id=t.project_id AND own.user_id=@effective_user_id
                  AND own.effective_start_date<=CURRENT_DATE
                  AND (own.effective_end_date IS NULL OR own.effective_end_date>=CURRENT_DATE)
            ))
            UNION ALL
            SELECT NULL::uuid,pt.plan_task_id,pt.plan_id,pt.project_id,pt.wbs_code,pt.task_name,pt.task_description,'review_plan',pt.task_type,pt.phase_name,
                   pt.priority_code,pt.task_status,pt.kanban_category,pt.decision_action,pt.planned_start_date,pt.planned_end_date,
                   COALESCE(pt.duration_working_days,0),pt.percent_complete,pt.estimated_hours,0::numeric,CASE WHEN @can_view_financials THEN pt.hourly_rate ELSE NULL END,
                   CASE WHEN @can_view_financials THEN pt.material_units*pt.material_unit_cost+pt.fixed_cost+pt.travel_cost+pt.equipment_cost+pt.miscellaneous_cost ELSE NULL END,
                   CASE WHEN @can_view_financials THEN pt.material_units ELSE NULL END,CASE WHEN @can_view_financials THEN pt.material_unit_cost ELSE NULL END,
                   CASE WHEN @can_view_financials THEN pt.fixed_cost ELSE NULL END,CASE WHEN @can_view_financials THEN pt.travel_cost ELSE NULL END,
                   CASE WHEN @can_view_financials THEN pt.equipment_cost ELSE NULL END,CASE WHEN @can_view_financials THEN pt.miscellaneous_cost ELSE NULL END,
                   pt.recurrence_rule,pt.is_important,pt.is_urgent,pt.reviewer_user_id,
                   COALESCE(NULLIF(reviewer.display_name,''),reviewer.email),COALESCE(NULLIF(reviewer.display_name,''),reviewer.email),
                   pt.reviewer_user_id,COALESCE(NULLIF(reviewer.display_name,''),reviewer.email),
                   parent.plan_task_id,pt.display_order,COALESCE(pt.blocked_reason,''),
                   COALESCE(review_assignment.review_status,'unassigned'),COALESCE(review_assignment.assignment_notes,''),
                   COALESCE(@can_write_estimate AND (@can_view_all_tasks OR pt.reviewer_user_id=@effective_user_id),FALSE),pt.revision_number,
                   0,pt.revision_number,
                   ((@can_manage OR (@can_write_estimate AND pt.reviewer_user_id=@effective_user_id)) AND NOT @is_view_as),
                   (@can_manage AND NOT @is_view_as),
                   ((@can_manage OR (@can_write_estimate AND pt.reviewer_user_id=@effective_user_id)) AND NOT @is_view_as),
                   (@can_manage AND NOT @is_view_as),(@can_manage AND NOT @is_view_as)
            FROM scoped_projects scope JOIN project_forge_plan_tasks pt ON pt.project_id=scope.project_id
            LEFT JOIN app_users reviewer ON reviewer.user_id=pt.reviewer_user_id
            LEFT JOIN project_forge_plan_tasks parent ON parent.plan_id=pt.plan_id AND parent.wbs_code=pt.parent_wbs_code
            LEFT JOIN project_forge_plan_assignments review_assignment ON review_assignment.plan_id=pt.plan_id
              AND review_assignment.plan_task_id=pt.plan_task_id AND review_assignment.user_id=pt.reviewer_user_id
              AND review_assignment.assignment_type='task_estimator'
            WHERE @workspace='review_plan' AND pt.plan_id=@plan_filter AND pt.canonical_task_id IS NULL
              AND pt.task_status<>'cancelled'
              AND (@can_view_all_tasks OR pt.reviewer_user_id=@effective_user_id)
            ORDER BY "projectId","taskCode"
        ) row_data
        """;

    private static readonly string AssignmentsSql = ScopeCte + """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT pa.project_assignment_id AS "assignmentId",pa.project_id AS "projectId",pa.task_id AS "taskId",NULL::uuid AS "planTaskId",NULL::uuid AS "planId",
                   pa.user_id AS "userId",COALESCE(NULLIF(u.display_name,''),u.email) AS "userName",u.email,
                   pa.effective_start_date AS "startDate",pa.effective_end_date AS "endDate",pa.allocation_percent AS "allocationPercent",
                   COALESCE(pa.assigned_hours,0) AS "assignedHours",'canonical' AS "assignmentSource",
                   EXISTS(SELECT 1 FROM app_user_role_assignments engineering_assignment
                          JOIN app_roles engineering_role ON engineering_role.app_role_id=engineering_assignment.app_role_id
                          WHERE engineering_assignment.user_id=pa.user_id AND engineering_assignment.is_active=TRUE AND engineering_role.is_active=TRUE
                            AND UPPER(engineering_role.role_code) IN ('ENGINEER','ENGINEERING','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD','SYSTEMS_ENGINEER','NETWORK_ENGINEER','ENTERPRISE_NETWORK_ENGINEER')) AS "isEngineer",
                   (pa.effective_start_date<=CURRENT_DATE AND (pa.effective_end_date IS NULL OR pa.effective_end_date>=CURRENT_DATE)
                    AND EXISTS(SELECT 1 FROM app_user_role_assignments reviewer_assignment
                               JOIN app_roles reviewer_role ON reviewer_role.app_role_id=reviewer_assignment.app_role_id
                               WHERE reviewer_assignment.user_id=pa.user_id AND reviewer_assignment.is_active=TRUE AND reviewer_role.is_active=TRUE
                                 AND UPPER(reviewer_role.role_code) IN ('ENGINEER','ENGINEERING','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD','SYSTEMS_ENGINEER','NETWORK_ENGINEER','ENTERPRISE_NETWORK_ENGINEER'))) AS "isReviewerEligible"
            FROM scoped_projects scope JOIN project_assignments pa ON pa.project_id=scope.project_id JOIN app_users u ON u.user_id=pa.user_id AND u.is_active=TRUE
            WHERE @workspace='canonical' AND (@can_view_all_tasks OR pa.user_id=@effective_user_id)
            UNION ALL
            SELECT pfa.plan_assignment_id,pfa.project_id,NULL::uuid,pfa.plan_task_id,pfa.plan_id,pfa.user_id,COALESCE(NULLIF(u.display_name,''),u.email),u.email,
                   NULL::date,NULL::date,pfa.allocation_percent,pfa.planned_hours,'review_plan',
                   EXISTS(SELECT 1 FROM app_user_role_assignments engineering_assignment
                          JOIN app_roles engineering_role ON engineering_role.app_role_id=engineering_assignment.app_role_id
                          WHERE engineering_assignment.user_id=pfa.user_id AND engineering_assignment.is_active=TRUE AND engineering_role.is_active=TRUE
                            AND UPPER(engineering_role.role_code) IN ('ENGINEER','ENGINEERING','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD','SYSTEMS_ENGINEER','NETWORK_ENGINEER','ENTERPRISE_NETWORK_ENGINEER')),
                   FALSE
            FROM scoped_projects scope JOIN project_forge_plan_assignments pfa ON pfa.project_id=scope.project_id JOIN app_users u ON u.user_id=pfa.user_id AND u.is_active=TRUE
            WHERE @workspace='review_plan' AND pfa.plan_id=@plan_filter AND (@can_view_all_tasks OR pfa.user_id=@effective_user_id)
        ) row_data
        """;

    private static readonly string ProjectTeamSql = ScopeCte + """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT assignment.project_id AS "projectId",assignment.user_id AS "userId",
                   COALESCE(NULLIF(person.display_name,''),person.email) AS "userName",person.email,
                   MIN(assignment.effective_start_date) AS "projectStartDate",MAX(assignment.effective_end_date) AS "projectEndDate",
                   TRUE AS "isEngineer",TRUE AS "isReviewerEligible"
            FROM scoped_projects scope
            JOIN project_assignments assignment ON assignment.project_id=scope.project_id
            JOIN app_users person ON person.user_id=assignment.user_id AND person.is_active=TRUE
            WHERE assignment.effective_start_date<=CURRENT_DATE
              AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date>=CURRENT_DATE)
              AND EXISTS(
                  SELECT 1 FROM app_user_role_assignments role_assignment
                  JOIN app_roles role ON role.app_role_id=role_assignment.app_role_id
                  WHERE role_assignment.user_id=assignment.user_id AND role_assignment.is_active=TRUE AND role.is_active=TRUE
                    AND UPPER(role.role_code) IN ('ENGINEER','ENGINEERING','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD','SYSTEMS_ENGINEER','NETWORK_ENGINEER','ENTERPRISE_NETWORK_ENGINEER')
              )
            GROUP BY assignment.project_id,assignment.user_id,person.display_name,person.email
            ORDER BY "userName"
        ) row_data
        """;

    private static readonly string ExpensesSql = ScopeCte + """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT u.project_expense_upload_id AS "expenseUploadId",u.project_id AS "projectId",u.expense_owner_user_id AS "ownerUserId",
                   u.period_start AS "periodStart",u.period_end AS "periodEnd",u.currency,u.line_count AS "lineCount",
                   u.total_amount AS "totalAmount",u.reimbursable_amount AS "reimbursableAmount",u.billing_treatment AS "billingTreatment",
                   u.uploaded_at AS "uploadedAt",u.notification_status AS "notificationStatus",
                   COALESCE(NULLIF(owner.display_name,''),owner.email,'Project team') AS "ownerName"
            FROM scoped_projects scope JOIN project_expense_uploads u ON u.project_id=scope.project_id
            LEFT JOIN app_users owner ON owner.user_id=u.expense_owner_user_id
            WHERE @can_view_financials AND u.is_current=TRUE AND u.deleted_at IS NULL ORDER BY u.uploaded_at DESC LIMIT 1000
        ) row_data
        """;

    private static readonly string PlansSql = ScopeCte + """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT plan.plan_id AS "planId",plan.project_id AS "projectId",plan.plan_name AS "planName",plan.objective,
                   plan.plan_status AS "status",plan.source_kind AS "sourceKind",plan.planned_start_date AS "startDate",
                   plan.planned_end_date AS "endDate",plan.ai_capability_code AS "aiCapability",plan.ai_confidence AS "aiConfidence",
                   CASE WHEN @can_view_ai_citations THEN plan.ai_citations ELSE '[]'::jsonb END AS "citations",plan.ai_warnings AS "warnings",plan.review_notes AS "reviewNotes",
                   plan.revision_number AS "revision",plan.updated_at AS "updatedAt",
                   COALESCE(counts.task_count,0) AS "taskCount",COALESCE(counts.estimated_hours,0) AS "estimatedHours"
            FROM scoped_projects scope JOIN project_forge_plans plan ON plan.project_id=scope.project_id
            LEFT JOIN LATERAL(
                SELECT COUNT(*)::int task_count,COALESCE(SUM(pt.estimated_hours),0)::numeric estimated_hours
                FROM project_forge_plan_tasks pt
                WHERE pt.plan_id=plan.plan_id AND pt.task_status<>'cancelled'
                  AND (@can_view_all_tasks OR pt.reviewer_user_id=@effective_user_id)
            ) counts ON TRUE
            WHERE @can_view_all_tasks OR EXISTS(
                SELECT 1 FROM project_forge_plan_tasks assigned_plan_task
                WHERE assigned_plan_task.plan_id=plan.plan_id
                  AND assigned_plan_task.reviewer_user_id=@effective_user_id
                  AND assigned_plan_task.task_status<>'cancelled'
            )
            ORDER BY plan.updated_at DESC
        ) row_data
        """;

    private static readonly string DependenciesSql = ScopeCte + """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT d.project_task_dependency_id AS "dependencyId",NULL::uuid AS "planId",d.project_id AS "projectId",
                   d.predecessor_task_id AS "predecessorTaskId",d.successor_task_id AS "successorTaskId",
                   predecessor.task_code AS "predecessorWbs",successor.task_code AS "successorWbs",
                   d.dependency_type AS "dependencyType",d.lag_working_days AS "lagWorkingDays",'canonical' AS "recordSource",
                   d.revision_number AS "revision"
            FROM scoped_projects scope JOIN project_task_dependencies d ON d.project_id=scope.project_id
            JOIN project_tasks predecessor ON predecessor.task_id=d.predecessor_task_id
            JOIN project_tasks successor ON successor.task_id=d.successor_task_id
            WHERE @workspace='canonical'
            UNION ALL
            SELECT d.dependency_id,d.plan_id,d.project_id,
                   d.predecessor_plan_task_id,d.successor_plan_task_id,
                   predecessor.wbs_code AS "predecessorWbs",successor.wbs_code AS "successorWbs",
                   d.dependency_type,d.lag_working_days,'review_plan',d.revision_number
            FROM scoped_projects scope JOIN project_forge_task_dependencies d ON d.project_id=scope.project_id
            JOIN project_forge_plan_tasks predecessor ON predecessor.plan_task_id=d.predecessor_plan_task_id
            JOIN project_forge_plan_tasks successor ON successor.plan_task_id=d.successor_plan_task_id
            WHERE @workspace='review_plan' AND d.plan_id=@plan_filter
        ) row_data
        """;

    private static readonly string ActivitySql = ScopeCte + """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT audit.audit_event_id AS "activityId",audit.project_id AS "projectId",audit.plan_id AS "planId",
                   audit.plan_task_id AS "planTaskId",audit.event_code AS "eventCode",audit.entity_type AS "entityType",
                   audit.actual_actor_user_id AS "actualActorUserId",audit.effective_actor_user_id AS "effectiveActorUserId",
                   CASE WHEN @can_view_financials THEN audit.event_metadata
                        ELSE jsonb_build_object(
                            'summary',CASE audit.event_code
                                WHEN 'CANONICAL_TASK_CREATED' THEN 'A project task was created.'
                                WHEN 'TASK_COMPOSITE_UPDATED' THEN 'A project task was updated.'
                                WHEN 'TASK_DETAILS_UPDATED' THEN 'Project task details were updated.'
                                WHEN 'TASK_SCHEDULE_UPDATED' THEN 'A project task schedule was updated.'
                                WHEN 'TASK_WORKFLOW_UPDATED' THEN 'A project task workflow was updated.'
                                WHEN 'TASK_DECISION_UPDATED' THEN 'A project task priority classification was updated.'
                                WHEN 'TASK_ASSIGNEE_UPDATED' THEN 'A project task assignment was updated.'
                                ELSE 'Project Forge activity was recorded.'
                            END,
                            'financialDetailsRedacted',TRUE
                        )
                   END AS "metadata",audit.occurred_at AS "occurredAt"
            FROM scoped_projects scope JOIN project_forge_audit_events audit ON audit.project_id=scope.project_id
            ORDER BY audit.occurred_at DESC LIMIT 500
        ) row_data
        """;

    private const string HolidaysSql = """
        SELECT to_jsonb(row_data)::text FROM (
            SELECT company_holiday_id AS "holidayId",holiday_date AS "date",holiday_name AS "name",holiday_code AS "code",
                   holiday_type AS "type",is_floating_holiday AS "isFloating",auto_populate_hours AS "hours"
            FROM company_holidays WHERE is_active=TRUE AND holiday_date BETWEEN CURRENT_DATE-INTERVAL '1 year' AND CURRENT_DATE+INTERVAL '3 years'
            ORDER BY holiday_date
        ) row_data
        """;

    private static async Task<ProjectForgeAccess> LoadAccessAsync(
        NpgsqlConnection connection,
        (Guid Actual, Guid Effective) identity,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT u.user_id,COALESCE(NULLIF(u.display_name,''),u.email),u.email,
                   COALESCE(string_agg(DISTINCT role.role_code,',' ORDER BY role.role_code),'') AS roles,
                   COALESCE(string_agg(DISTINCT permission.permission_code,',' ORDER BY permission.permission_code),'') AS permissions
            FROM app_users u
            LEFT JOIN app_user_role_assignments assignment ON assignment.user_id=u.user_id AND assignment.is_active=TRUE
            LEFT JOIN app_roles role ON role.app_role_id=assignment.app_role_id AND role.is_active=TRUE
            LEFT JOIN app_role_permissions rp ON rp.app_role_id=role.app_role_id
            LEFT JOIN app_permissions permission ON permission.app_permission_id=rp.app_permission_id
            WHERE u.user_id=@user_id AND u.is_active=TRUE
            GROUP BY u.user_id,u.display_name,u.email
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", identity.Effective);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return ProjectForgeAccess.Inactive(identity.Actual, identity.Effective);
        var roles = Split(reader.GetString(3));
        var permissions = Split(reader.GetString(4));
        var isViewAs = identity.Actual != identity.Effective
            || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is bool flag && flag);
        return new ProjectForgeAccess(identity.Actual, identity.Effective, reader.GetString(1), reader.GetString(2), roles, permissions, isViewAs, true);
    }

    private static async Task<(Guid? ManagerUserId, IResult? Error)> ResolveManagerFilterAsync(
        NpgsqlConnection connection,
        ProjectForgeAccess access,
        Guid? requested,
        CancellationToken cancellationToken)
    {
        if (access.IsProjectManager) return (access.EffectiveUserId, requested.HasValue && requested != access.EffectiveUserId ? Forbidden("project_manager_own_projects_only") : null);
        if (!requested.HasValue) return (null, null);
        if (!access.CanSelectProjectManager) return (null, Forbidden("project_manager_selector_not_authorized"));
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM app_users pm
                WHERE pm.user_id=@manager AND pm.is_active=TRUE
                  AND (
                    @is_admin
                    OR EXISTS(SELECT 1 FROM reporting_relationships rr WHERE rr.employee_user_id=pm.user_id
                              AND (rr.manager_user_id=@user_id OR rr.team_lead_user_id=@user_id)
                              AND rr.effective_start_date<=CURRENT_DATE AND (rr.effective_end_date IS NULL OR rr.effective_end_date>=CURRENT_DATE))
                    OR EXISTS(SELECT 1 FROM projectpulse_team_scope_assignments scope WHERE scope.scoped_user_id=@user_id
                              AND scope.is_active=TRUE AND scope.scope_type='project_management_team_lead'
                              AND ((scope.team_name IS NOT NULL AND LOWER(COALESCE(pm.team_name,''))=LOWER(scope.team_name))
                                OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(pm.department_name,''))=LOWER(scope.department_name))
                                OR scope.manager_user_id=pm.user_id))
                  )
                  AND EXISTS(SELECT 1 FROM projects p WHERE p.project_manager_user_id=pm.user_id)
            )
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("manager", requested.Value);
        command.Parameters.AddWithValue("is_admin", access.IsAdministrator);
        command.Parameters.AddWithValue("user_id", access.EffectiveUserId);
        var allowed = (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
        return allowed ? (requested, null) : (null, Forbidden("project_manager_selector_scope"));
    }

    private static async Task<List<JsonElement>> LoadProjectManagersAsync(NpgsqlConnection connection, ProjectForgeAccess access, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT to_jsonb(row_data)::text FROM (
                SELECT pm.user_id AS "userId",COALESCE(NULLIF(pm.display_name,''),pm.email) AS "name",pm.email,
                       COUNT(DISTINCT p.project_id)::int AS "projectCount"
                FROM app_users pm JOIN projects p ON p.project_manager_user_id=pm.user_id
                WHERE pm.is_active=TRUE AND (
                    @is_admin
                    OR EXISTS(SELECT 1 FROM reporting_relationships rr WHERE rr.employee_user_id=pm.user_id
                              AND (rr.manager_user_id=@user_id OR rr.team_lead_user_id=@user_id)
                              AND rr.effective_start_date<=CURRENT_DATE AND (rr.effective_end_date IS NULL OR rr.effective_end_date>=CURRENT_DATE))
                    OR EXISTS(SELECT 1 FROM projectpulse_team_scope_assignments scope WHERE scope.scoped_user_id=@user_id
                              AND scope.is_active=TRUE AND scope.scope_type='project_management_team_lead'
                              AND ((scope.team_name IS NOT NULL AND LOWER(COALESCE(pm.team_name,''))=LOWER(scope.team_name))
                                OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(pm.department_name,''))=LOWER(scope.department_name))
                                OR scope.manager_user_id=pm.user_id))
                )
                GROUP BY pm.user_id,pm.display_name,pm.email ORDER BY "name"
            ) row_data
            """;
        return await ReadJsonRowsAsync(connection, sql, command =>
        {
            command.Parameters.AddWithValue("is_admin", access.IsAdministrator);
            command.Parameters.AddWithValue("user_id", access.EffectiveUserId);
        }, cancellationToken);
    }

    private static async Task<bool> CanAccessProjectAsync(
        NpgsqlConnection connection,
        ProjectForgeAccess access,
        Guid projectId,
        Guid? managerFilter,
        CancellationToken cancellationToken)
    {
        if (managerFilter.HasValue)
        {
            await using var managerCommand = new NpgsqlCommand(
                "SELECT project_manager_user_id FROM projects WHERE project_id=@project_id;",
                connection);
            managerCommand.Parameters.AddWithValue("project_id", projectId);
            var manager = await managerCommand.ExecuteScalarAsync(cancellationToken);
            if (manager is not Guid managerUserId || managerUserId != managerFilter.Value)
                return false;
        }

        var planningAccess = await ProjectPlanningAccessResolver.ResolveForActorAsync(
            connection,
            access.EffectiveUserId,
            projectId,
            "033",
            cancellationToken);
        return planningAccess.CanView;
    }

    private static async Task<bool> IsEligibleEngineerReviewerAsync(NpgsqlConnection connection, Guid projectId, Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM project_assignments assignment
                JOIN app_users engineer ON engineer.user_id=assignment.user_id AND engineer.is_active=TRUE
                WHERE assignment.project_id=@project_id
                  AND assignment.user_id=@user_id
                  AND assignment.effective_start_date<=CURRENT_DATE
                  AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date>=CURRENT_DATE)
                  AND EXISTS(
                      SELECT 1
                      FROM app_user_role_assignments role_assignment
                      JOIN app_roles role ON role.app_role_id=role_assignment.app_role_id
                      WHERE role_assignment.user_id=assignment.user_id
                        AND role_assignment.is_active=TRUE
                        AND role.is_active=TRUE
                        AND UPPER(role.role_code) IN (
                            'ENGINEER','ENGINEERING','ENGINEERING_LEAD','ENGINEERING_TEAM_LEAD',
                            'SYSTEMS_ENGINEER','NETWORK_ENGINEER','ENTERPRISE_NETWORK_ENGINEER'
                        )
                  )
            )
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("user_id", userId);
        return (bool?)await command.ExecuteScalarAsync(cancellationToken) == true;
    }

    private static async Task<string> LoadUserNameAsync(NpgsqlConnection connection, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT COALESCE(NULLIF(display_name,''),email) FROM app_users WHERE user_id=@id AND is_active=TRUE", connection);
        command.Parameters.AddWithValue("id", userId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "Assigned engineer";
    }

    private static void AddAccessParameters(NpgsqlCommand command, ProjectForgeAccess access)
    {
        command.Parameters.AddWithValue("effective_user_id", access.EffectiveUserId);
        command.Parameters.AddWithValue("is_admin", access.IsAdministrator);
        command.Parameters.AddWithValue("is_pm_lead", access.IsProjectManagementLead);
        command.Parameters.AddWithValue("is_pm", access.IsProjectManager);
        command.Parameters.AddWithValue("is_engineer", access.IsEngineer);
        command.Parameters.AddWithValue("is_engineering_lead", access.IsEngineeringLead);
        command.Parameters.AddWithValue("is_account_executive", access.IsAccountExecutive);
        command.Parameters.AddWithValue("is_solution_architect", access.IsSolutionArchitect);
        command.Parameters.AddWithValue("can_view_all_tasks", access.CanViewAllScopedTasks);
        command.Parameters.AddWithValue("can_manage", access.CanManage);
        command.Parameters.AddWithValue("can_view_financials", access.CanViewFinancials);
        command.Parameters.AddWithValue("can_view_ai_citations", access.CanViewAiCitations);
        command.Parameters.AddWithValue("is_view_as", access.IsViewAs);
        command.Parameters.AddWithValue("can_update_assigned_status", access.CanUpdateAssignedTaskStatus);
        command.Parameters.AddWithValue("can_write_estimate", !access.IsViewAs && (access.CanManage || access.CanEditReviewPlan || access.CanEditAssignedEstimate));
    }

    private static async Task<List<JsonElement>> ReadJsonRowsAsync(
        NpgsqlConnection connection,
        string sql,
        Action<NpgsqlCommand>? configure,
        CancellationToken cancellationToken)
    {
        var rows = new List<JsonElement>();
        await using var command = new NpgsqlCommand(sql, connection);
        configure?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var document = JsonDocument.Parse(reader.GetString(0));
            rows.Add(document.RootElement.Clone());
        }
        return rows;
    }

    private static async Task<(Guid ProjectId, string ProjectCode, string ProjectName, DateOnly? StartDate)?> LoadProjectIdentityAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT project_id,project_code,project_name,start_date FROM projects WHERE project_id=@id", connection);
        command.Parameters.AddWithValue("id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (reader.GetGuid(0), reader.GetString(1), reader.GetString(2), ReadDate(reader, 3));
    }

    private static async Task<(Guid ProjectId, string PlanName)?> LoadPlanProjectAsync(NpgsqlConnection connection, Guid planId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT project_id,plan_name FROM project_forge_plans WHERE plan_id=@id", connection);
        command.Parameters.AddWithValue("id", planId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetGuid(0), reader.GetString(1)) : null;
    }

    private static async Task<(Guid ProjectId, Guid PlanId, string TaskName, Guid? ReviewerUserId)?> LoadPlanTaskAccessAsync(NpgsqlConnection connection, Guid planTaskId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT task.project_id,task.plan_id,task.task_name,task.reviewer_user_id FROM project_forge_plan_tasks task JOIN project_forge_plans plan ON plan.plan_id=task.plan_id WHERE task.plan_task_id=@id AND task.canonical_task_id IS NULL AND task.task_status<>'cancelled' AND plan.plan_status IN ('draft','in_review','changes_requested','reviewed')", connection);
        command.Parameters.AddWithValue("id", planTaskId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3))
            : null;
    }

    private static async Task<Guid> PlanProjectIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid planId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT project_id FROM project_forge_plans WHERE plan_id=@id", connection, transaction);
        command.Parameters.AddWithValue("id", planId);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Plan project was not found."));
    }

    private static async Task<string> NextCanonicalTaskCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        string wbs,
        CancellationToken cancellationToken)
    {
        var suffixCode = Regex.Replace(wbs.ToUpperInvariant(), "[^A-Z0-9]+", "-").Trim('-');
        if (suffixCode.Length == 0) suffixCode = "TASK";
        var baseCode = $"PF-{suffixCode}";
        if (baseCode.Length > 80) baseCode = baseCode[..80];
        for (var suffix = 0; suffix < 1000; suffix++)
        {
            var candidate = suffix == 0 ? baseCode : $"{baseCode}-{suffix + 1}";
            await using var command = new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM project_tasks WHERE project_id=@project_id AND task_code=@code)", connection, transaction);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("code", candidate);
            if ((bool?)await command.ExecuteScalarAsync(cancellationToken) == true) return candidate;
        }
        throw new InvalidOperationException("A unique canonical Project Forge task code could not be allocated.");
    }

    private static async Task LockProjectAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid projectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@project_id::text,33))", connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ProjectFlowHivePlanRequest ToFlowHiveRequest(ProjectForgePlanSaveRequest request, string projectCode, string projectName)
    {
        return new ProjectFlowHivePlanRequest(
            request.ProjectId, projectCode, projectName, null, request.PlanName, "Project Forge review draft", request.StartDate, null,
            (request.Tasks ?? []).Select(task => new ProjectFlowHivePlanTaskInput(
                task.PlanTaskId, null, task.Wbs, task.ParentWbs, task.Name, task.Description,
                Math.Max(0, task.DurationWorkingDays), string.Equals(task.TaskType, "milestone", StringComparison.OrdinalIgnoreCase),
                "ASAP", null, task.PercentComplete, task.EstimatedHours, task.Status)).ToArray(),
            (request.Dependencies ?? []).Select(row => new ProjectFlowHiveDependencyInput(row.PredecessorWbs, row.SuccessorWbs, row.DependencyType, row.LagWorkingDays)).ToArray(),
            (request.Tasks ?? []).Where(row => row.ReviewerUserId.HasValue).Select(row => new ProjectFlowHivePlanAssignmentInput(
                row.Wbs, row.ReviewerUserId, null, 100m, row.EstimatedHours)).ToArray(),
            null, null, request.ReviewNote);
    }

    private static IResult? ValidatePlan(ProjectForgePlanSaveRequest request)
    {
        var tasks = request.Tasks ?? [];
        if (request.ProjectId == Guid.Empty) return Results.BadRequest(new { status = "project_required" });
        if (tasks.Count is < 1 or > 500) return Results.BadRequest(new { status = "invalid_task_count", message = "A plan must contain between 1 and 500 tasks." });
        if (tasks.Any(task => string.IsNullOrWhiteSpace(task.Name) || task.Name.Trim().Length < 3)) return Results.BadRequest(new { status = "task_name_required", message = "Every task name must contain at least three characters." });
        if (tasks.Any(task => task.EstimatedHours < 0 || task.HourlyRate < 0 || task.MaterialUnits < 0 || task.MaterialUnitCost < 0))
            return Results.BadRequest(new { status = "negative_estimate_not_allowed" });
        if (tasks.Any(task => task.StartDate.HasValue && task.DueDate.HasValue && task.DueDate.Value < task.StartDate.Value))
            return Results.BadRequest(new { status = "invalid_task_dates" });
        if (tasks.Any(task => task.ReviewerUserId.HasValue))
            return Results.BadRequest(new { status = "review_assignment_endpoint_required", message = "Save the review plan first, then assign an active project engineer through the governed reviewer endpoint so Module 065 notification evidence is created." });
        var wbs = tasks.Select(task => Clean(task.Wbs, 80, string.Empty)).ToArray();
        if (wbs.Any(string.IsNullOrWhiteSpace) || wbs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != wbs.Length)
            return Results.BadRequest(new { status = "unique_wbs_required" });
        var flow = ToFlowHiveRequest(request, string.Empty, string.Empty);
        var validation = ProjectFlowHiveScheduleEngine.Validate(flow);
        return validation.Valid ? null : Results.BadRequest(new { status = "plan_validation_failed", validation });
    }

    private static (Guid Actual, Guid Effective)? Identities(HttpContext context)
    {
        var effective = UserId(context, "ProjectPulseEffectiveUserId") ?? UserId(context, "ProjectPulseSessionUserId");
        if (!effective.HasValue) return null;
        var actual = UserId(context, "ProjectPulseActualUserId") ?? UserId(context, "ProjectPulseSessionUserId") ?? effective;
        return (actual.Value, effective.Value);
    }

    private static Guid? UserId(HttpContext context, string key) => context.Items.TryGetValue(key, out var value) && value is Guid id ? id : null;

    private static (string ConnectionString, IResult? Error) OpenConfiguration()
    {
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        return config.Missing.Count == 0
            ? (config.ConnectionString, null)
            : (string.Empty, Results.Json(new { status = "configuration_missing", missing = config.Missing }, statusCode: StatusCodes.Status503ServiceUnavailable));
    }

    private static PulseAiPrivateRagAccess ToPrivateRagAccess(ProjectForgeAccess access) =>
        new(access.EffectiveUserId, access.IsActive, access.Roles, access.Permissions);

    private static async Task<(NpgsqlConnection? Connection, ProjectForgeAccess? Access, IResult? Error)> OpenForWriteAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return (null, null, SessionRequired());
        var configured = OpenConfiguration();
        if (configured.Error is not null) return (null, null, configured.Error);
        var connection = new NpgsqlConnection(configured.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var access = await LoadAccessAsync(connection, identity.Value, context, cancellationToken);
            if (!access.CanView)
            {
                await connection.DisposeAsync();
                return (null, null, Forbidden("VIEW_PROJECT_FORGE_033"));
            }
            return (connection, access, null);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static void AddNullableUuid(NpgsqlCommand command, string name, Guid? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid) { Value = value.HasValue ? value.Value : DBNull.Value });

    private static void AddNullableDate(NpgsqlCommand command, string name, DateOnly? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Date) { Value = value.HasValue ? value.Value : DBNull.Value });

    private static void AddJson(NpgsqlCommand command, string name, object value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(value, JsonOptions) });

    private static void AddJsonText(NpgsqlCommand command, string name, string? value)
    {
        var json = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) json = JsonSerializer.Serialize(new { value }, JsonOptions);
        }
        catch (JsonException) { json = JsonSerializer.Serialize(new { description = value }, JsonOptions); }
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json });
    }

    private static DateOnly? ReadDate(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var value = reader.GetValue(ordinal);
        return value switch { DateOnly date => date, DateTime dateTime => DateOnly.FromDateTime(dateTime), _ => DateOnly.Parse(value.ToString()!) };
    }

    private static string Clean(string? value, int limit, string fallback)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return clean.Length <= limit ? clean : clean[..limit];
    }

    private static string TaskName(string? value, int index)
    {
        var clean = Clean(value, 255, $"Task {index}");
        return clean.Length >= 3 ? clean : $"{clean} task";
    }

    private static string PlanningDescription(PulseAiPrivateFlowHiveTask task)
    {
        var value = new StringBuilder();
        value.AppendLine(Clean(task.Description, 1_200, "Complete the cited delivery work package and retain review evidence."));
        AppendPlanningSection(value, "Detailed procedure", task.DetailedSteps);
        AppendPlanningSection(value, "Inputs", task.Inputs);
        AppendPlanningSection(value, "Outputs and deliverables", task.Outputs);
        AppendPlanningSection(value, "Validation", task.ValidationSteps);
        AppendPlanningSection(value, "Acceptance criteria", task.AcceptanceCriteria);
        AppendPlanningSection(value, "Customer responsibilities", task.CustomerResponsibilities);
        AppendPlanningSection(value, "US Signal responsibilities", task.UsSignalResponsibilities);
        AppendPlanningSection(value, "Prerequisites", task.Prerequisites);
        AppendPlanningSection(value, "Risks", task.Risks);
        AppendPlanningSection(value, "Open questions", task.OpenQuestions);
        if (task.CitationIds.Count > 0)
            value.AppendLine().Append("Private evidence citations: ")
                .Append(string.Join(", ", task.CitationIds.Select(id => $"[{id}]")))
                .Append('.');
        if (task.IsAssumption) value.AppendLine().Append("Assumption: One or more planning values require Project Manager, Engineering, or customer validation before adoption.");
        return Clean(value.ToString(), 4_000, task.Description);
    }

    private static void AppendPlanningSection(
        StringBuilder value,
        string heading,
        IReadOnlyList<string>? items)
    {
        var supported = (items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Take(20)
            .ToArray();
        if (supported.Length == 0) return;
        value.AppendLine().AppendLine(heading + ":");
        for (var index = 0; index < supported.Length; index++)
            value.Append(index + 1).Append(". ").AppendLine(supported[index].Trim());
    }

    private static string Normalize(string? value, string fallback, params string[] allowed)
    {
        var clean = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return allowed.Contains(clean, StringComparer.OrdinalIgnoreCase) ? clean : fallback;
    }

    private static string? ParentWbs(string? wbs)
    {
        if (string.IsNullOrWhiteSpace(wbs)) return null;
        var index = wbs.LastIndexOf('.');
        return index > 0 ? wbs[..index] : null;
    }

    private static DateOnly? LatestDueDate(IReadOnlyList<ProjectForgePlanTaskRequest>? tasks)
    {
        var dates = (tasks ?? []).Where(row => row.DueDate.HasValue).Select(row => row.DueDate!.Value).ToArray();
        return dates.Length == 0 ? null : dates.Max();
    }

    private static string Slug(string value) => value.ToLowerInvariant().Replace(' ', '-');
    private static string RevisionKey(IEnumerable<Guid> ids) => string.Join('-', ids.Order().Select(id => id.ToString("N")[..8]));
    private static HashSet<string> Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IResult SessionRequired() => Results.Json(new { status = "session_required", message = "A valid ProjectPulse session is required." }, statusCode: 401);
    private static IResult Forbidden(string permission) => Results.Json(new { status = "forbidden", requiredPermission = permission }, statusCode: 403);
    private static IResult WriteForbidden(ProjectForgeAccess access) => Results.Json(new { status = access.IsViewAs ? "view_as_read_only" : "forbidden", message = access.IsViewAs ? "Administrator View-As is read-only." : "Project Forge management access is required." }, statusCode: 403);
    private static IResult MigrationRequired() => Results.Json(new { status = "project_forge_migration_required", message = "Project Forge persistence is not available until migrations 070 and the current interactive migration have been applied." }, statusCode: 503);
    private static IResult Problem(string detail) => Results.Problem(title: "Project Forge unavailable", detail: detail, statusCode: 500);
    private static void Log(HttpContext context, Exception exception, string operation) => context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ProjectForgeModule").LogError(exception, "Module 033 failed to {Operation}.", operation);

    private sealed record ProjectForgeAccess(
        Guid ActualUserId,
        Guid EffectiveUserId,
        string DisplayName,
        string Email,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions,
        bool IsViewAs,
        bool IsActive)
    {
        public static ProjectForgeAccess Inactive(Guid actual, Guid effective) => new(actual, effective, string.Empty, string.Empty, new HashSet<string>(), new HashSet<string>(), false, false);
        private bool HasRole(params string[] codes) => codes.Any(Roles.Contains);
        private bool HasPermission(string code) => Permissions.Contains(code);
        public bool IsAdministrator => HasRole("SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR");
        public bool IsProjectManagementLead => HasRole("PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD");
        public bool IsProjectManager => !IsAdministrator && !IsProjectManagementLead && HasRole("PROJECT_MANAGER", "PROJECT_MANAGEMENT");
        public bool IsEngineeringLead => !IsAdministrator && !IsProjectManagementLead && !IsProjectManager
            && HasRole("ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD");
        public bool IsAccountExecutive => !IsAdministrator && HasRole("ACCOUNT_EXECUTIVE", "SALES_ACCOUNT_EXECUTIVE");
        public bool IsSolutionArchitect => !IsAdministrator && HasRole("SOLUTION_ARCHITECT", "SOLUTIONS_ARCHITECT");
        public bool IsEngineer => !IsAdministrator && !IsProjectManagementLead && !IsProjectManager
            && (IsEngineeringLead
                || HasRole("ENGINEER", "ENGINEERING", "SYSTEMS_ENGINEER", "NETWORK_ENGINEER", "ENTERPRISE_NETWORK_ENGINEER")
                || HasPermission("EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033"));
        public bool CanView => IsActive && (IsAdministrator || IsProjectManagementLead || IsProjectManager || IsEngineer
            || IsAccountExecutive || IsSolutionArchitect
            || HasPermission("VIEW_PROJECT_FORGE_033") || HasPermission("VIEW_ASSOCIATED_PROJECT_FORGE_033"));
        public bool CanManage => IsAdministrator || IsProjectManagementLead || IsProjectManager || HasPermission("MANAGE_PROJECT_FORGE_033");
        public bool CanReviewPlan => CanView && (CanManage || HasPermission("REVIEW_PROJECT_FORGE_PLAN_033"));
        public bool CanEditReviewPlan => CanView && (CanManage || HasPermission("EDIT_PROJECT_FORGE_REVIEW_PLAN_033"));
        public bool CanAdoptPlan => CanManage;
        public bool CanUseAi => CanManage && (IsAdministrator || IsProjectManagementLead || IsProjectManager || HasPermission("USE_PROJECT_FORGE_AI_033"));
        public bool CanEditAssignedEstimate => IsEngineer || HasPermission("EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033");
        public bool CanUpdateAssignedTaskStatus => HasPermission("UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033");
        public bool CanViewFinancials => CanManage && !IsViewAs;
        public bool CanViewAiCitations => CanManage && !IsViewAs;
        public bool CanSelectProjectManager => IsAdministrator || IsProjectManagementLead;
        public bool CanViewAllScopedTasks => IsAdministrator || IsProjectManagementLead || IsProjectManager
            || IsEngineeringLead || IsAccountExecutive || IsSolutionArchitect
            || HasPermission("VIEW_ASSOCIATED_PROJECT_FORGE_033");
        public object ToResponse(Guid? selectedManager) => new
        {
            actualUserId = ActualUserId, effectiveUserId = EffectiveUserId, DisplayName, Email,
            roles = Roles.OrderBy(value => value), isViewAs = IsViewAs,
            scope = IsAdministrator ? "all_projects"
                : IsProjectManagementLead ? "managed_pm_team_projects"
                : IsProjectManager ? "own_managed_projects"
                : IsEngineeringLead ? "assigned_engineering_team_projects"
                : IsAccountExecutive ? "associated_account_executive_projects"
                : IsSolutionArchitect ? "associated_solution_architect_projects"
                : "assigned_projects_and_tasks",
            capabilityLabel = CanManage ? "Project Owner — Full Control"
                : CanEditReviewPlan ? "Engineering Collaborator — Planner Edit"
                : CanReviewPlan ? "Technical Reviewer — Review and Comment"
                : "Project Stakeholder — Read Only",
            accessContract = ProjectPlanningAccessResolver.Contract,
            canSelectProjectManager = CanSelectProjectManager, selectedProjectManagerUserId = selectedManager,
            canManage = CanManage && !IsViewAs,
            canAdministerPlanner = CanManage && !IsViewAs,
            canReviewPlan = CanReviewPlan && !IsViewAs,
            canEditReviewPlan = CanEditReviewPlan && !IsViewAs,
            canAdoptPlan = CanAdoptPlan && !IsViewAs,
            canUseAi = CanUseAi && !IsViewAs,
            canEditAssignedEstimate = CanEditAssignedEstimate && !IsViewAs,
            canUpdateAssignedTaskStatus = CanUpdateAssignedTaskStatus && !IsViewAs,
            canViewFinancials = CanViewFinancials,
            serverAuthorized = true
        };
    }

    private sealed record AdoptionTask(
        Guid PlanTaskId,string Wbs,string Name,string Description,string TaskType,string Phase,string Priority,string Status,string Kanban,string Decision,
        DateOnly? PlannedStartDate,DateOnly? PlannedEndDate,int DurationWorkingDays,string RecurrenceRule,decimal PercentComplete,
        decimal EstimatedHours,decimal HourlyRate,decimal MaterialUnits,decimal MaterialUnitCost,decimal FixedCost,decimal TravelCost,
        decimal EquipmentCost,decimal MiscCost,bool Important,bool Urgent,Guid? ReviewerUserId,string SourceKind,string? AiCorrelationId,string ReviewerName,
        string ParentWbs,int DisplayOrder,string BlockedReason);

}
