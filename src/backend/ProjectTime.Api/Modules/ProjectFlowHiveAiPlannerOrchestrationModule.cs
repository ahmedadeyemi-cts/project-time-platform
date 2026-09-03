using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public sealed record ProjectFlowHiveAiPlannerRunRequest(
    ProjectFlowHivePlanRequest? Plan,
    string? RequestedOutcome,
    string? DetailLevel = "comprehensive",
    bool RetryTerminalDocumentProcessing = false);

internal static class ProjectFlowHiveAiPlannerOrchestrationModule
{
    private const string MigrationId = "095_project_planning_collaboration_access";
    private const string RunTable = "project_flowhive_ai_planner_runs";
    private const int MaximumAiRouteRetries = 2;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static IEndpointRouteBuilder MapProjectFlowHiveAiPlannerOrchestrationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/project-flowhive/projects/{projectId:guid}/ai-planner/runs",
            (Func<Guid, ProjectFlowHiveAiPlannerRunRequest?, HttpContext, CancellationToken, Task<IResult>>)CreateOrResumeAsync);
        endpoints.MapGet(
            "/api/project-flowhive/projects/{projectId:guid}/ai-planner/runs/{runId:guid}",
            (Func<Guid, Guid, HttpContext, CancellationToken, Task<IResult>>)GetAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateOrResumeAsync(
        Guid projectId,
        ProjectFlowHiveAiPlannerRunRequest? request,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        request ??= new ProjectFlowHiveAiPlannerRunRequest(null, null, "comprehensive");

        var opened = await OpenAsync(projectId, context, requireEdit: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = opened.Access!;

        var seed = request.Plan
            ?? await LoadProjectSeedAsync(connection, projectId, cancellationToken);
        if (seed is null)
            return Results.NotFound(new
            {
                status = "flowhive_project_not_found",
                message = "The selected project could not be resolved for AI Planner.",
                stateChanged = false
            });
        if (seed.ProjectId != projectId)
            return Validation("The AI Planner project does not match the selected project.");
        if (!seed.ProjectStartDate.HasValue)
            return Validation("The project Start Date is required because it drives FlowHive scheduling.");
        if (seed.ProjectEndDate.HasValue
            && seed.ProjectEndDate.Value < seed.ProjectStartDate.Value)
            return Validation("The requested project finish date cannot precede the project Start Date.");

        // The authenticated start action may perform the bounded document-admission
        // pass so the governed Protected-Test retry can be honored once. Long-running
        // AI inference is never executed inside this request.
        request = request with { Plan = seed, RetryTerminalDocumentProcessing = true };
        var runId = await GetOrCreateRunAsync(
            connection,
            projectId,
            request,
            access,
            CorrelationId(context),
            cancellationToken);

        var stored = await LoadRunAsync(connection, projectId, runId, cancellationToken);
        if (stored is null)
            return Results.Json(new
            {
                status = "flowhive_ai_planner_run_unavailable",
                message = "The durable AI Planner operation could not be reloaded.",
                stateChanged = false
            }, statusCode: StatusCodes.Status500InternalServerError);

        if (!stored.Terminal
            && stored.Status != "generating"
            && stored.GeneratedPlan is null)
        {
            await AdvanceAdmissionAsync(
                connection,
                projectId,
                runId,
                request,
                access,
                context,
                cancellationToken);
            stored = (await LoadRunAsync(connection, projectId, runId, cancellationToken))!;
        }

        return RunResult(stored);
    }

    private static async Task<IResult> GetAsync(
        Guid projectId,
        Guid runId,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(projectId, context, requireEdit: true, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var stored = await LoadRunAsync(connection, projectId, runId, cancellationToken);
        if (stored is null)
            return Results.NotFound(new
            {
                status = "flowhive_ai_planner_run_not_found",
                message = "The AI Planner operation was not found for the selected project.",
                stateChanged = false
            });
        if (stored.Plan is null)
            return Results.Json(new
            {
                status = "flowhive_ai_planner_run_invalid",
                runId,
                terminal = true,
                message = "The AI Planner operation does not contain a valid project seed.",
                stateChanged = false
            }, statusCode: StatusCodes.Status500InternalServerError);

        // Polling is deliberately read-only. The background worker owns document
        // re-evaluation, AI generation, and mutable working-copy persistence.
        return RunResult(stored);
    }

    private static async Task AdvanceAdmissionAsync(
        NpgsqlConnection connection,
        Guid projectId,
        Guid runId,
        ProjectFlowHiveAiPlannerRunRequest request,
        PlannerAccess access,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId(context);
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        var protectedTestExplicitRetry = request.RetryTerminalDocumentProcessing
            && PulseAiProtectedTestCandidatePolicy.AllowsPrivateDocumentProcessing(release);
        var documents = await ProjectPlanningDocumentResolver.ResolveAndPrepareAsync(
            connection,
            projectId,
            access.ActualUserId,
            access.EffectiveUserId,
            "flowhive_ai_planner_automatic",
            correlationId,
            queuePending: true,
            cancellationToken,
            retryTerminalSow: protectedTestExplicitRetry);

        await PersistDocumentStateAsync(
            connection,
            runId,
            documents,
            cancellationToken,
            readyPhase: "queued_for_generation");
    }

    internal static async Task<bool> ProcessNextQueuedRunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0) return false;

        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var schema = new NpgsqlCommand(
            $"SELECT to_regclass('public.{RunTable}') IS NOT NULL;",
            connection))
        {
            var present = await schema.ExecuteScalarAsync(cancellationToken);
            if (present is not bool tablePresent || !tablePresent) return false;
        }

        var candidates = new List<(Guid RunId, Guid ProjectId)>();
        await using (var command = new NpgsqlCommand($"""
            SELECT run_id,project_id
            FROM {RunTable}
            WHERE status IN ('queued','processing','generating')
              AND (phase <> 'ai_route_retry' OR updated_at <= NOW() - INTERVAL '30 seconds')
            ORDER BY CASE status
                         WHEN 'generating' THEN 0
                         WHEN 'queued' THEN 1
                         ELSE 2
                     END,
                     updated_at,
                     created_at
            LIMIT 12;
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }
        }

        foreach (var candidate in candidates)
        {
            if (!await TryLockRunAsync(connection, candidate.RunId, cancellationToken))
                continue;

            try
            {
                var stored = await LoadRunAsync(
                    connection,
                    candidate.ProjectId,
                    candidate.RunId,
                    cancellationToken);
                if (stored is null || stored.Terminal) return true;

                await AdvanceWorkerAsync(connection, stored, services, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ProjectFlowHiveAiPlannerOrchestrationModule")
                    .LogError(
                        exception,
                        "FlowHive AI Planner run stopped after a bounded background failure. RunId={RunId} Diagnostic={Diagnostic}",
                        candidate.RunId,
                        exception.GetType().Name);
                await UpdateRunAsync(
                    connection,
                    candidate.RunId,
                    "failed",
                    "background_generation_failed",
                    100,
                    ["The background AI Planner operation stopped safely. Start AI Planner again to retry."],
                    ["No working draft, immutable version, or baseline was created by the failed operation."],
                    ["Background generation stopped after a bounded internal failure; the run will not remain indefinitely at an in-progress percentage."],
                    null,
                    null,
                    null,
                    cancellationToken,
                    completed: true);
                return true;
            }
            finally
            {
                await UnlockRunAsync(connection, candidate.RunId);
            }
        }

        return false;
    }

    private static async Task AdvanceWorkerAsync(
        NpgsqlConnection connection,
        PlannerRun stored,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (stored.Plan is null)
        {
            await UpdateRunAsync(
                connection,
                stored.RunId,
                "failed",
                "invalid_requested_plan",
                100,
                ["The durable AI Planner operation does not contain a valid project seed."],
                stored.Warnings,
                ["The background worker stopped because the persisted project seed was invalid."],
                null,
                null,
                null,
                cancellationToken,
                completed: true);
            return;
        }

        if (stored.GeneratedPlan is not null
            && stored.Schedule is not null
            && stored.Validation is not null)
        {
            await PersistWorkingDraftAndCompleteAsync(
                connection,
                stored.RunId,
                stored.ProjectId,
                stored.ActualUserId,
                stored.GeneratedPlan,
                stored.Schedule,
                stored.Validation,
                stored.Warnings,
                cancellationToken);
            return;
        }

        var documents = await ProjectPlanningDocumentResolver.ResolveAndPrepareAsync(
            connection,
            stored.ProjectId,
            stored.ActualUserId,
            stored.EffectiveUserId,
            "flowhive_ai_planner_background",
            stored.CorrelationId,
            queuePending: true,
            cancellationToken,
            retryTerminalSow: false);

        if (!documents.HasAuthoritativeSow
            || documents.HasTerminalProcessingFailure
            || !documents.ReadyForGeneration)
        {
            await PersistDocumentStateAsync(
                connection,
                stored.RunId,
                documents,
                cancellationToken,
                readyPhase: "queued_for_generation");
            return;
        }

        await UpdateRunAsync(
            connection,
            stored.RunId,
            "generating",
            "extract_and_expand_work_packages",
            65,
            [],
            documents.Warnings,
            ["The current authoritative Work Register SOW and supporting project documents are citation ready. Background plan generation started."],
            null,
            null,
            null,
            cancellationToken);

        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = Clean(
                stored.CorrelationId,
                180,
                Guid.NewGuid().ToString("N"))
        };
        if (!string.IsNullOrWhiteSpace(stored.CorrelationId))
            context.Request.Headers["X-Correlation-Id"] = stored.CorrelationId;

        var enterprise = services.GetRequiredService<CelarAiEnterprisePlatformService>();
        var generation = await ProjectPlanningAiOrchestrator.GenerateAsync(
            enterprise,
            stored.ActualUserId,
            stored.EffectiveUserId,
            stored.Plan,
            documents,
            stored.RequestedOutcome,
            stored.DetailLevel,
            CelarAiCapabilityCatalog.ProjectFlowHivePlan,
            allowSanitizedExternalFallback: true,
            context,
            cancellationToken);

        if (!generation.Succeeded
            || generation.Plan is null
            || generation.Validation is null
            || generation.Schedule is null)
        {
            var transient = generation.Status == "project_planning_ai_temporarily_unavailable";
            var priorRetryCount = stored.Logs.Count(log =>
                log.StartsWith("AI route retry ", StringComparison.Ordinal));
            var retry = transient && priorRetryCount < MaximumAiRouteRetries;
            var retryLog = retry
                ? $"AI route retry {priorRetryCount + 1} of {MaximumAiRouteRetries} is scheduled after a transient private-generation failure."
                : transient
                    ? "The bounded AI route retry limit was reached. Review the evidence status and start AI Planner again when private generation is available."
                    : generation.Message;
            await UpdateRunAsync(
                connection,
                stored.RunId,
                retry ? "processing" : "needs_attention",
                retry ? "ai_route_retry" : "evidence_review",
                retry ? 70 : 100,
                generation.MissingEvidence,
                generation.Warnings,
                [retryLog],
                null,
                null,
                null,
                cancellationToken,
                completed: !retry);
            return;
        }

        // Persist generated artifacts before touching the working copy. If the API
        // restarts after model completion, the next worker resumes here instead of
        // invoking the model again.
        await UpdateRunAsync(
            connection,
            stored.RunId,
            "generating",
            "persist_working_draft",
            90,
            [],
            generation.Warnings,
            ["Source-grounded generation completed. Persisting the mutable FlowHive working draft."],
            generation.Plan,
            generation.Schedule,
            generation.Validation,
            cancellationToken);

        await PersistWorkingDraftAndCompleteAsync(
            connection,
            stored.RunId,
            stored.ProjectId,
            stored.ActualUserId,
            generation.Plan,
            generation.Schedule,
            generation.Validation,
            generation.Warnings,
            cancellationToken);
    }

    private static async Task PersistDocumentStateAsync(
        NpgsqlConnection connection,
        Guid runId,
        ProjectPlanningDocumentResolution documents,
        CancellationToken cancellationToken,
        string readyPhase)
    {
        if (!documents.HasAuthoritativeSow)
        {
            await UpdateRunAsync(
                connection,
                runId,
                "needs_attention",
                "resolve_authoritative_sow",
                10,
                documents.Blockers,
                documents.Warnings,
                ["The project-scoped Work Register resolver did not locate an active durable SOW."],
                null,
                null,
                null,
                cancellationToken,
                completed: true);
            return;
        }

        if (documents.HasTerminalProcessingFailure)
        {
            var diagnostic = documents.TerminalDiagnosticCode.Length > 0
                ? documents.TerminalDiagnosticCode
                : "private_document_processing_failed";
            await UpdateRunAsync(
                connection,
                runId,
                "failed",
                "private_document_processing",
                30,
                documents.Blockers,
                documents.Warnings,
                [
                    $"Private project-document processing reached a terminal state. Diagnostic: {diagnostic}.",
                    "Automatic planner polling did not requeue the failed document. Correct the bounded blocker and use the authorized explicit retry workflow."
                ],
                null,
                null,
                null,
                cancellationToken,
                completed: true);
            return;
        }

        if (!documents.ReadyForGeneration)
        {
            var progress = documents.PendingDocuments.Count > 0 ? 30 : 50;
            await UpdateRunAsync(
                connection,
                runId,
                "processing",
                documents.PendingDocuments.Count > 0
                    ? "private_document_processing"
                    : "authority_index_and_citations",
                progress,
                documents.Blockers,
                documents.Warnings,
                [$"Project document admission evaluated {documents.SelectedDocuments.Count} current document(s); {documents.NewlyQueuedCount} new private-processing job(s) were queued."],
                null,
                null,
                null,
                cancellationToken);
            return;
        }

        await UpdateRunAsync(
            connection,
            runId,
            "processing",
            readyPhase,
            60,
            [],
            documents.Warnings,
            ["The current authoritative Work Register SOW and supporting project documents are ready for background AI generation."],
            null,
            null,
            null,
            cancellationToken);
    }

    private static async Task PersistWorkingDraftAndCompleteAsync(
        NpgsqlConnection connection,
        Guid runId,
        Guid projectId,
        Guid actor,
        ProjectFlowHivePlanRequest generated,
        ProjectFlowHiveScheduleResult schedule,
        ProjectFlowHivePlanValidationResult validation,
        IReadOnlyList<string> sourceWarnings,
        CancellationToken cancellationToken)
    {
        // The mutable working copy and the terminal run state are one durable
        // outcome. If either write fails, roll both back so a retry cannot
        // overwrite a saved draft that the operation incorrectly reported as
        // absent or failed.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var workingCopy = await SaveWorkingCopyAsync(
            connection,
            transaction,
            projectId,
            generated,
            actor,
            validation,
            schedule,
            cancellationToken);

        var warnings = sourceWarnings
            .Concat([
                "The generated plan was saved only as the mutable FlowHive working draft.",
                "No immutable version, reviewed baseline, assignment, capacity reservation, or customer commitment was created automatically."
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await UpdateRunAsync(
            connection,
            runId,
            FinalStatus(schedule),
            "working_draft_ready",
            100,
            [],
            warnings,
            [$"Planner working-copy revision {workingCopy.WorkingRevision} was saved.",
             $"Generated {(generated.Tasks ?? []).Count(task => !task.IsSummary)} executable Plan/Design/Implement/Validate/Release tasks from the current project documents."],
            generated,
            schedule,
            validation,
            cancellationToken,
            completed: true,
            transaction: transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string FinalStatus(ProjectFlowHiveScheduleResult schedule) =>
        schedule.ProjectTargetEndDate.HasValue
        && schedule.ProjectFinishDate.HasValue
        && schedule.ProjectFinishDate.Value > schedule.ProjectTargetEndDate.Value
            ? "completed_with_schedule_overrun"
            : "completed";

    private static async Task<bool> TryLockRunAsync(
        NpgsqlConnection connection,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtextextended(@run_id::text,735));",
            connection);
        command.Parameters.AddWithValue("run_id", runId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task UnlockRunAsync(
        NpgsqlConnection connection,
        Guid runId)
    {
        if (connection.State != System.Data.ConnectionState.Open) return;
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtextextended(@run_id::text,735));",
                connection);
            command.Parameters.AddWithValue("run_id", runId);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // Closing the connection also releases session advisory locks.
        }
    }

    private static IResult RunResult(PlannerRun run) =>
        run.Terminal
            ? Results.Ok(ToResponse(run))
            : Results.Json(ToResponse(run), statusCode: StatusCodes.Status202Accepted);

    private static async Task<ProjectFlowHivePlanRequest?> LoadProjectSeedAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT project.project_id,
                   COALESCE(project.project_code,''),
                   COALESCE(project.project_name,''),
                   COALESCE(client.client_name,''),
                   project.start_date,
                   project.end_date
              FROM projects project
              LEFT JOIN clients client ON client.client_id=project.client_id
             WHERE project.project_id=@project_id
             LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var projectCode = reader.GetString(1);
        var projectName = reader.GetString(2);
        var startDate = reader.IsDBNull(4) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(4);
        var endDate = reader.IsDBNull(5) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(5);
        return new ProjectFlowHivePlanRequest(
            ProjectId: projectId,
            ProjectCode: projectCode,
            ProjectName: projectName,
            CustomerName: reader.GetString(3),
            PlanName: $"AI project plan — {(projectName.Length == 0 ? projectCode : projectName)}",
            RevisionLabel: "FlowHive AI working draft",
            ProjectStartDate: startDate,
            ProjectEndDate: endDate,
            Tasks: [],
            Dependencies: [],
            Assignments: [],
            GsdVersion: null,
            SowVersion: null,
            Notes: "FlowHive AI Planner working draft. PM and Engineering review is required before immutable versioning or baseline approval.",
            SourceKind: "celar_ai");
    }

    private static async Task<Guid> GetOrCreateRunAsync(
        NpgsqlConnection connection,
        Guid projectId,
        ProjectFlowHiveAiPlannerRunRequest request,
        PlannerAccess access,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var guard = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@project_id::text,734));",
            connection,
            transaction))
        {
            guard.Parameters.AddWithValue("project_id", projectId);
            await guard.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var existing = new NpgsqlCommand($"""
            SELECT run_id
            FROM {RunTable}
            WHERE project_id=@project_id
              AND actual_actor_user_id=@actual
              AND status IN ('queued','processing','generating')
            ORDER BY created_at DESC
            LIMIT 1
            FOR UPDATE;
            """, connection, transaction))
        {
            existing.Parameters.AddWithValue("project_id", projectId);
            existing.Parameters.AddWithValue("actual", access.ActualUserId);
            var found = await existing.ExecuteScalarAsync(cancellationToken);
            if (found is Guid run)
            {
                await transaction.CommitAsync(cancellationToken);
                return run;
            }
        }

        var runId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand($"""
            INSERT INTO {RunTable}(
                run_id,project_id,status,phase,progress_percent,requested_plan,
                requested_outcome,detail_level,actual_actor_user_id,effective_actor_user_id,
                correlation_id,operation_logs)
            VALUES(@run_id,@project_id,'queued','resolve_project',5,@plan::jsonb,
                @outcome,@detail,@actual,@effective,@correlation,@logs::jsonb);
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("run_id", runId);
            insert.Parameters.AddWithValue("project_id", projectId);
            insert.Parameters.AddWithValue("plan", JsonSerializer.Serialize(request.Plan, Json));
            insert.Parameters.AddWithValue("outcome", Clean(request.RequestedOutcome, 4_000));
            insert.Parameters.AddWithValue("detail", Clean(request.DetailLevel, 80, "comprehensive"));
            insert.Parameters.AddWithValue("actual", access.ActualUserId);
            insert.Parameters.AddWithValue("effective", access.EffectiveUserId);
            insert.Parameters.AddWithValue("correlation", Clean(correlationId, 180));
            insert.Parameters.AddWithValue("logs", JsonSerializer.Serialize(new[] { "AI Planner operation created." }, Json));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return runId;
    }

    private static async Task<WorkingCopyResult> SaveWorkingCopyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid projectId,
        ProjectFlowHivePlanRequest plan,
        Guid actor,
        ProjectFlowHivePlanValidationResult validation,
        ProjectFlowHiveScheduleResult schedule,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH upsert AS (
                INSERT INTO project_flowhive_working_copies(
                    project_id,plan_id,working_payload,updated_by_user_id)
                VALUES(@project_id,@plan_id,@payload::jsonb,@actor)
                ON CONFLICT(project_id) DO UPDATE
                SET plan_id=EXCLUDED.plan_id,
                    working_payload=EXCLUDED.working_payload,
                    updated_by_user_id=EXCLUDED.updated_by_user_id
                WHERE project_flowhive_working_copies.working_payload IS DISTINCT FROM EXCLUDED.working_payload
                RETURNING working_revision,row_version,updated_at
            )
            SELECT working_revision,row_version,updated_at
            FROM upsert
            UNION ALL
            SELECT working_revision,row_version,updated_at
            FROM project_flowhive_working_copies
            WHERE project_id=@project_id
              AND NOT EXISTS(SELECT 1 FROM upsert)
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add("plan_id", NpgsqlDbType.Uuid).Value = plan.PlanId.HasValue ? plan.PlanId.Value : DBNull.Value;
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(plan, Json));
        command.Parameters.AddWithValue("actor", actor);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("FlowHive working-copy persistence returned no durable row.");
        var result = new WorkingCopyResult(
            reader.GetInt32(0),
            reader.GetGuid(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            validation.Valid,
            schedule.Valid);
        await reader.CloseAsync();
        return result;
    }

    private static async Task UpdateRunAsync(
        NpgsqlConnection connection,
        Guid runId,
        string status,
        string phase,
        int progress,
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> logs,
        ProjectFlowHivePlanRequest? plan,
        ProjectFlowHiveScheduleResult? schedule,
        ProjectFlowHivePlanValidationResult? validation,
        CancellationToken cancellationToken,
        bool completed = false,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand($"""
            UPDATE {RunTable}
            SET status=@status,
                phase=@phase,
                progress_percent=@progress,
                blockers=@blockers::jsonb,
                warnings=@warnings::jsonb,
                operation_logs=COALESCE(operation_logs,'[]'::jsonb) || @logs::jsonb,
                generated_plan=COALESCE(@plan::jsonb,generated_plan),
                schedule_payload=COALESCE(@schedule::jsonb,schedule_payload),
                validation_payload=COALESCE(@validation::jsonb,validation_payload),
                updated_at=NOW(),
                completed_at=CASE WHEN @completed THEN NOW() ELSE completed_at END,
                row_version=gen_random_uuid()
            WHERE run_id=@run_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("phase", phase);
        command.Parameters.AddWithValue("progress", Math.Clamp(progress, 0, 100));
        command.Parameters.AddWithValue("blockers", JsonSerializer.Serialize(blockers, Json));
        command.Parameters.AddWithValue("warnings", JsonSerializer.Serialize(warnings, Json));
        command.Parameters.AddWithValue("logs", JsonSerializer.Serialize(logs, Json));
        command.Parameters.Add("plan", NpgsqlDbType.Text).Value = plan is null ? DBNull.Value : JsonSerializer.Serialize(plan, Json);
        command.Parameters.Add("schedule", NpgsqlDbType.Text).Value = schedule is null ? DBNull.Value : JsonSerializer.Serialize(schedule, Json);
        command.Parameters.Add("validation", NpgsqlDbType.Text).Value = validation is null ? DBNull.Value : JsonSerializer.Serialize(validation, Json);
        command.Parameters.AddWithValue("completed", completed);
        command.Parameters.AddWithValue("run_id", runId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PlannerRun?> LoadRunAsync(
        NpgsqlConnection connection,
        Guid projectId,
        Guid runId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT run_id,project_id,status,phase,progress_percent,
                   requested_plan::text,requested_outcome,detail_level,
                   actual_actor_user_id,effective_actor_user_id,
                   COALESCE(generated_plan::text,''),COALESCE(schedule_payload::text,''),
                   COALESCE(validation_payload::text,''),blockers::text,warnings::text,
                   operation_logs::text,correlation_id,created_at,updated_at,completed_at
            FROM {RunTable}
            WHERE run_id=@run_id AND project_id=@project_id;
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var status = reader.GetString(2);
        return new PlannerRun(
            reader.GetGuid(0),
            reader.GetGuid(1),
            status,
            reader.GetString(3),
            reader.GetInt16(4),
            Deserialize<ProjectFlowHivePlanRequest>(reader.GetString(5)),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetGuid(8),
            reader.GetGuid(9),
            Deserialize<ProjectFlowHivePlanRequest>(reader.GetString(10)),
            Deserialize<ProjectFlowHiveScheduleResult>(reader.GetString(11)),
            Deserialize<ProjectFlowHivePlanValidationResult>(reader.GetString(12)),
            Deserialize<string[]>(reader.GetString(13)) ?? [],
            Deserialize<string[]>(reader.GetString(14)) ?? [],
            Deserialize<string[]>(reader.GetString(15)) ?? [],
            reader.GetString(16),
            reader.GetFieldValue<DateTimeOffset>(17),
            reader.GetFieldValue<DateTimeOffset>(18),
            reader.IsDBNull(19) ? null : reader.GetFieldValue<DateTimeOffset>(19),
            status is "completed" or "completed_with_schedule_overrun" or "needs_attention" or "failed");
    }

    private static object ToResponse(PlannerRun run)
    {
        var schedule = run.Schedule;
        var overrun = schedule?.ProjectTargetEndDate.HasValue == true
            && schedule.ProjectFinishDate.HasValue
            && schedule.ProjectFinishDate.Value > schedule.ProjectTargetEndDate.Value;
        var criticalPath = schedule is null
            ? Array.Empty<CriticalPathItem>()
            : schedule.Tasks
                .Where(task => task.IsCritical && !task.IsSummary)
                .Select(task => new CriticalPathItem(task.WbsNumber, task.Name, task.StartDate, task.EndDate))
                .ToArray();
        return new
        {
            module = "066",
            status = run.Status,
            runId = run.RunId,
            projectId = run.ProjectId,
            phase = run.Phase,
            progressPercent = run.ProgressPercent,
            terminal = run.Terminal,
            plan = run.GeneratedPlan,
            schedule,
            validation = run.Validation,
            blockers = run.Blockers,
            warnings = run.Warnings,
            generationLogs = run.Logs,
            correlationId = run.CorrelationId,
            workingDraft = new
            {
                persisted = run.GeneratedPlan is not null
                    && run.Phase == "working_draft_ready",
                immutableVersionCreated = false,
                baselineCreated = false,
                reviewRequired = true
            },
            scheduleAssessment = new
            {
                requestedFinishDate = schedule?.ProjectTargetEndDate,
                calculatedFinishDate = schedule?.ProjectFinishDate,
                exceedsRequestedFinish = overrun,
                criticalPath,
                estimatesCompressed = false,
                options = overrun
                    ? new[]
                    {
                        "Parallelize eligible independent work without breaking dependency or acceptance gates.",
                        "Add qualified resources where effort and access permit safe parallel execution.",
                        "Phase or reduce scope through approved change control.",
                        "Revise the requested finish date to the calculated delivery window."
                    }
                    : []
            },
            planningEvidence = new
            {
                phaseOrder = new[] { "Plan", "Design", "Implement", "Validate", "Release" },
                sourceGrounded = run.GeneratedPlan?.CelarAiCitationIds?.Count > 0,
                automaticPrivateProcessing = true,
                automaticWorkingCopyPersistence = true,
                requestPollingReadOnly = true,
                backgroundGeneration = true
            },
            createdAt = run.CreatedAt,
            updatedAt = run.UpdatedAt,
            completedAt = run.CompletedAt,
            stateChanged = run.GeneratedPlan is not null
        };
    }

    private static async Task<OpenOutcome> OpenAsync(
        Guid projectId,
        HttpContext context,
        bool requireEdit,
        CancellationToken cancellationToken)
    {
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
            return OpenOutcome.Fail(Results.Json(new
            {
                status = "flowhive_database_configuration_missing",
                message = "FlowHive database configuration is unavailable.",
                stateChanged = false
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        var connection = new NpgsqlConnection(config.ConnectionString);
        try { await connection.OpenAsync(cancellationToken); }
        catch
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Results.Json(new
            {
                status = "flowhive_database_unavailable",
                message = "FlowHive could not reach its protected data store.",
                stateChanged = false
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
        await using (var schema = new NpgsqlCommand($"""
            SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration),
                   to_regclass('public.{RunTable}') IS NOT NULL;
            """, connection))
        {
            schema.Parameters.AddWithValue("migration", MigrationId);
            await using var reader = await schema.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (!reader.GetBoolean(0) || !reader.GetBoolean(1))
            {
                await connection.DisposeAsync();
                return OpenOutcome.Fail(Results.Json(new
                {
                    status = "migration_095_required",
                    requiredMigration = MigrationId,
                    message = "FlowHive AI Planner orchestration requires the protected-Test planning migration.",
                    stateChanged = false
                }, statusCode: StatusCodes.Status503ServiceUnavailable));
            }
        }
        var access = await ProjectPlanningAccessResolver.ResolveAsync(
            connection, context, projectId, "066", cancellationToken);
        if (!access.CanView || (requireEdit && !access.CanEditPlanner))
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Results.Json(new
            {
                status = "flowhive_project_access_denied",
                message = requireEdit
                    ? "The current identity cannot generate or edit this project's Planner working draft."
                    : "The project is outside the current FlowHive scope.",
                stateChanged = false
            }, statusCode: StatusCodes.Status403Forbidden));
        }
        if (!access.ActualUserId.HasValue || !access.EffectiveUserId.HasValue)
        {
            await connection.DisposeAsync();
            return OpenOutcome.Fail(Results.Json(new
            {
                status = "flowhive_session_required",
                message = "An authenticated actual and effective user session is required.",
                stateChanged = false
            }, statusCode: StatusCodes.Status401Unauthorized));
        }
        return new OpenOutcome(
            connection,
            new PlannerAccess(access.ActualUserId.Value, access.EffectiveUserId.Value),
            null);
    }

    private static T? Deserialize<T>(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        try { return JsonSerializer.Deserialize<T>(value, Json); }
        catch { return default; }
    }

    private static IResult Validation(string message) => Results.BadRequest(new
    {
        status = "flowhive_ai_planner_request_invalid",
        message,
        stateChanged = false
    });

    private static string CorrelationId(HttpContext context) =>
        Clean(context.Response.Headers["X-ProjectPulse-Correlation-Id"].FirstOrDefault()
            ?? context.TraceIdentifier, 180, Guid.NewGuid().ToString("N"));

    private static string Clean(string? value, int maximum, string fallback = "")
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private sealed record PlannerAccess(
        Guid ActualUserId,
        Guid EffectiveUserId);

    private sealed record OpenOutcome(
        NpgsqlConnection? Connection,
        PlannerAccess? Access,
        IResult? Error)
    {
        public static OpenOutcome Fail(IResult error) => new(null, null, error);
    }

    private sealed record PlannerRun(
        Guid RunId,
        Guid ProjectId,
        string Status,
        string Phase,
        short ProgressPercent,
        ProjectFlowHivePlanRequest? Plan,
        string RequestedOutcome,
        string DetailLevel,
        Guid ActualUserId,
        Guid EffectiveUserId,
        ProjectFlowHivePlanRequest? GeneratedPlan,
        ProjectFlowHiveScheduleResult? Schedule,
        ProjectFlowHivePlanValidationResult? Validation,
        IReadOnlyList<string> Blockers,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Logs,
        string CorrelationId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? CompletedAt,
        bool Terminal);

    private sealed record CriticalPathItem(
        string WbsNumber,
        string Name,
        DateOnly StartDate,
        DateOnly EndDate);

    private sealed record WorkingCopyResult(
        int WorkingRevision,
        Guid RowVersion,
        DateTimeOffset UpdatedAt,
        bool ValidationValid,
        bool ScheduleValid);
}
