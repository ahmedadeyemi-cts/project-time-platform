using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// One source-backed planning engine for FlowHive and Project Forge. The private
/// project documents are resolved before this service is called. This service
/// enforces current-document citations, phase-native executable WBS assembly, and
/// schedule calculation without silently compressing effort or duration.
/// </summary>
internal static class ProjectPlanningAiOrchestrator
{
    internal const string Contract = "project-planning-ai-orchestrator-v2-20260906";
    private const string DurableRunTable = "project_flowhive_ai_planner_runs";
    private static readonly JsonSerializerOptions DurablePlannerJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<ProjectPlanningGenerationResult> GenerateAsync(
        CelarAiEnterprisePlatformService enterprise,
        Guid actualUserId,
        Guid effectiveUserId,
        ProjectFlowHivePlanRequest seed,
        ProjectPlanningDocumentResolution documents,
        string? requestedOutcome,
        string? detailLevel,
        string capabilityCode,
        bool allowSanitizedExternalFallback,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!documents.ReadyForGeneration)
        {
            return ProjectPlanningGenerationResult.NotReady(
                documents.Blockers,
                documents.Warnings,
                "The current project SOW and supporting documents are not ready for source-grounded plan generation.");
        }

        var outcome = Clean(
            requestedOutcome,
            4_000,
            "Create a complete source-backed project planning draft. Return distinct, project-specific executable tasks in Plan, Design, Implement, Validate, and Release. Assign each task to its own phase exactly once; never repeat every work package across all five phases. Include detailed steps, products, platforms, versions, licensing, quantities, tools, systems, interfaces, access, inputs, outputs, responsibilities, acceptance, validation, rollback, risks, assumptions, open questions, roles, effort, duration, predecessors, and citations. Never fabricate missing information; convert it into open questions.");
        outcome += "\nReturn at least one detailed child task in each of Plan, Design, Implement, Validate, and Release, with unique WBS references, at least two distinct execution steps, inputs, outputs, acceptance criteria, validation steps, required roles, positive effort/duration estimates, and current evidence citations. Use task-specific technical descriptions, not document titles, repeated phase boilerplate, or instructions to convert scope into work. Do not automatically create project milestones. The PM reviews proposed estimates and scope before baseline approval.";

        // Project Forge is a review projection of the same governed planning graph,
        // not a reason to invoke the model a second time behind an HTTP gateway.
        // Reuse a completed current-version FlowHive planner run, or durably queue
        // one for the background worker and let the caller poll with HTTP 202.
        if (string.Equals(
                capabilityCode,
                CelarAiCapabilityCatalog.ProjectForgePlanEstimate,
                StringComparison.OrdinalIgnoreCase))
        {
            return await ReuseOrQueueDurableFlowHivePlanAsync(
                actualUserId,
                effectiveUserId,
                seed,
                documents,
                requestedOutcome?.Trim() ?? string.Empty,
                detailLevel,
                context,
                cancellationToken);
        }

        CelarAiComposeResult composition;
        try
        {
            composition = await enterprise.ComposeAsync(
                actualUserId,
                effectiveUserId,
                new CelarAiComposeRequest(
                    Mode: "project_plan",
                    ProjectCode: seed.ProjectCode,
                    ProjectName: seed.ProjectName,
                    StartDate: seed.ProjectStartDate,
                    RequestedOutcome: outcome,
                    DetailLevel: Clean(detailLevel, 80, "comprehensive"),
                    DiagramType: "flowchart",
                    AllowSanitizedExternalFallback: allowSanitizedExternalFallback,
                    ProjectId: seed.ProjectId,
                    CapabilityCode: capabilityCode),
                context,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ProjectPlanningAiOrchestrator")
                .LogWarning(exception, "Shared project-planning generation returned an evidence-limited result.");
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "The governed AI route was temporarily unavailable. No Planner or Forge draft was changed.",
                documents.Blockers,
                documents.Warnings.Concat([
                    "The existing project documents remain available and can be retried without another upload."
                ]).ToArray());
        }

        // Preserve the terminal route outcome before evaluating evidence coverage.
        // A refusal is not a missing-document problem and must never trigger retry.
        if (composition.Status == "celar_ai_solution_draft_refused")
        {
            var provider = composition.SelectedTarget switch
            {
                CelarAiCapabilityTargets.DeepSeek => "DeepSeek",
                CelarAiCapabilityTargets.CelarAi => "Celar AI",
                CelarAiCapabilityTargets.Claude => "Claude",
                CelarAiCapabilityTargets.OpenAi => "OpenAI",
                _ => "The selected AI provider"
            };
            var message = $"{provider} reported a safety refusal. Generation stopped without provider failover, automatic retry, or changes to the planning draft. Use this run's correlation ID to review the provider diagnostic.";
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_safety_refusal",
                message,
                composition,
                null,
                null,
                null,
                [],
                composition.Warnings.Concat(documents.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        var currentDocumentIds = documents.CurrentDocumentIds;
        var currentCitations = composition.Citations
            .Where(citation => currentDocumentIds.Contains(citation.DocumentId)
                && documents.SelectedDocuments.Any(document => document.DocumentId == citation.DocumentId
                    && document.ActiveSourceSha256.Length == 64
                    && string.Equals(document.ActiveSourceSha256, citation.SourceSha256, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(document.ActiveDocumentVersion, citation.DocumentVersion, StringComparison.Ordinal)))
            .ToArray();
        var currentCitationIds = currentCitations
            .Select(citation => citation.CitationId)
            .ToHashSet();
        var currentSowId = documents.StatementOfWork!.DocumentId;
        var sowCitations = currentCitations
            .Where(citation => citation.DocumentId == currentSowId)
            .ToArray();

        var privatePlan = composition.FlowHivePlan;
        // A partial scaffold is not a successful executable plan, regardless of schema shape.
        var completedStatus = composition.Status == "celar_ai_solution_draft_completed";
        var citedPlan = privatePlan is not null
            && privatePlan.Tasks.Count > 0
            && privatePlan.CitationIds.Count > 0
            && privatePlan.CitationIds.All(currentCitationIds.Contains)
            && privatePlan.Tasks.All(task => task.CitationIds.Count > 0
                && task.CitationIds.All(currentCitationIds.Contains));
        var grounded = completedStatus
            && citedPlan
            && sowCitations.Length > 0;

        if (!grounded)
        {
            var missing = composition.MissingEvidence
                .Concat(documents.Blockers)
                .Concat([
                    "Celar AI did not return a complete plan cited to the current active Work Register SOW and current authorized project evidence. No generic plan was substituted."
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_evidence_insufficient",
                "The source-evidence quality gate did not pass. No planning draft was changed.",
                composition,
                null,
                null,
                null,
                missing,
                composition.Warnings.Concat(documents.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        ProjectFlowHivePlanRequest generated;
        try
        {
            // Bind identity to the exact SOW version before deriving stable task IDs.
            // FlowHive must preserve native task phases, not multiply a scaffold.
            generated = string.Equals(capabilityCode, CelarAiCapabilityCatalog.ProjectFlowHivePlan, StringComparison.OrdinalIgnoreCase)
                ? ProjectFlowHiveExecutablePlanBuilder.Build(seed with
                {
                    SowVersion = documents.StatementOfWork?.ActiveVersionId?.ToString("D"),
                    GsdVersion = documents.GeneralSolutionDesign?.ActiveVersionId?.ToString("D")
                }, privatePlan!, currentCitationIds)
                : ProjectFlowHiveDetailedPlanBuilder.Build(seed, privatePlan!);
        }
        catch (Exception exception)
        {
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_expansion_failed",
                "The AI result did not meet the executable five-phase work-breakdown contract. No planning draft was changed.",
                composition,
                null,
                null,
                null,
                composition.MissingEvidence
                    .Concat([exception.Message])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                composition.Warnings.Concat(documents.Warnings)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }

        var validation = ProjectFlowHiveScheduleEngine.Validate(generated);
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(generated);
        generated = ApplySchedule(generated, schedule, composition, documents);

        var warnings = composition.Warnings
            .Concat(documents.Warnings)
            .Concat(schedule.Issues
                .Where(issue => issue.Code == "project_end_exceeded")
                .Select(issue => issue.Message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var status = schedule.ProjectTargetEndDate.HasValue
            && schedule.ProjectFinishDate.HasValue
            && schedule.ProjectFinishDate.Value > schedule.ProjectTargetEndDate.Value
                ? "completed_with_schedule_overrun"
                : "completed";

        return new ProjectPlanningGenerationResult(
            validation.Valid && schedule.Valid,
            status,
            validation.Valid && schedule.Valid
                ? "The current project documents produced a source-cited five-phase planning draft."
                : "The generated plan requires correction before it can be saved.",
            composition,
            generated,
            validation,
            schedule,
            composition.MissingEvidence,
            warnings);
    }

    private static async Task<ProjectPlanningGenerationResult> ReuseOrQueueDurableFlowHivePlanAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        ProjectFlowHivePlanRequest seed,
        ProjectPlanningDocumentResolution documents,
        string outcome,
        string? detailLevel,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!seed.ProjectId.HasValue)
        {
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "Project Forge could not resolve the exact project identifier for the shared background planner.",
                ["The exact selected project UUID is required before Project Forge can reuse or queue the shared planner."],
                documents.Warnings);
        }

        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
        {
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "The durable shared planner store is temporarily unavailable.",
                config.Missing,
                documents.Warnings);
        }

        var projectId = seed.ProjectId.Value;
        var currentSowVersion = documents.StatementOfWork?.ActiveVersionId?.ToString("D") ?? string.Empty;
        var currentGsdVersion = documents.GeneralSolutionDesign?.ActiveVersionId?.ToString("D") ?? string.Empty;
        var correlationId = Clean(
            context.Response.Headers["X-ProjectPulse-Correlation-Id"].FirstOrDefault()
                ?? context.TraceIdentifier,
            180,
            Guid.NewGuid().ToString("N"));

        try
        {
            await using var connection = new NpgsqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var guard = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtextextended(@project_id::text,734));",
                connection,
                transaction))
            {
                guard.Parameters.AddWithValue("project_id", projectId);
                await guard.ExecuteNonQueryAsync(cancellationToken);
            }

            var rows = new List<DurablePlannerRow>();
            await using (var command = new NpgsqlCommand($"""
                SELECT run_id,status,phase,progress_percent,
                       COALESCE(generated_plan::text,''),
                       COALESCE(schedule_payload::text,''),
                       COALESCE(validation_payload::text,''),
                       COALESCE(warnings::text,'[]'),
                       COALESCE(correlation_id,''),
                       completed_at
                  FROM {DurableRunTable}
                 WHERE project_id=@project_id
                   AND actual_actor_user_id=@actual
                   AND effective_actor_user_id=@effective
                   AND requested_outcome=@outcome AND detail_level=@detail
                   AND execution_contract=@execution_contract
                   AND (source_version_fingerprint=@source_versions OR status IN ('queued','processing','generating'))
                   AND status IN ('queued','processing','generating','completed','completed_with_schedule_overrun')
                 ORDER BY created_at DESC
                 LIMIT 12
                 FOR UPDATE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("project_id", projectId);
                command.Parameters.AddWithValue("actual", actualUserId);
                command.Parameters.AddWithValue("effective", effectiveUserId);
                command.Parameters.AddWithValue("outcome", Clean(outcome, 4_000, string.Empty));
                command.Parameters.AddWithValue("detail", Clean(detailLevel, 80, "comprehensive"));
                command.Parameters.AddWithValue("execution_contract", ProjectFlowHiveExecutionPolicy.Contract);
                command.Parameters.AddWithValue("source_versions", ProjectFlowHiveExecutionPolicy.VersionFingerprint(documents));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new DurablePlannerRow(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        Convert.ToInt32(reader.GetValue(3)),
                        Deserialize<ProjectFlowHivePlanRequest>(reader.GetString(4)),
                        Deserialize<ProjectFlowHiveScheduleResult>(reader.GetString(5)),
                        Deserialize<ProjectFlowHivePlanValidationResult>(reader.GetString(6)),
                        Deserialize<string[]>(reader.GetString(7)) ?? [],
                        reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9)));
                }
            }

            foreach (var row in rows)
            {
                if (row.Status is not ("completed" or "completed_with_schedule_overrun")
                    || row.Plan is null
                    || row.Schedule is null
                    || row.Validation is null
                    || row.Plan.RevisionLabel != ProjectFlowHiveExecutablePlanBuilder.Contract
                    || row.Plan.ProjectStartDate != seed.ProjectStartDate
                    || row.Plan.ProjectEndDate != seed.ProjectEndDate
                    || !MatchesCurrentAuthority(row.Plan, projectId, currentSowVersion, currentGsdVersion))
                {
                    continue;
                }

                await transaction.CommitAsync(cancellationToken);
                return ReusedDurableResult(row, documents);
            }

            var active = rows.FirstOrDefault(row => row.Status is "queued" or "processing" or "generating");
            if (active is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ProjectPlanningGenerationResult.Failed(
                    "project_planning_ai_temporarily_unavailable",
                    $"The shared durable planner is still processing this project ({active.Status}/{active.Phase}, {active.ProgressPercent}%). Project Forge will reuse it when complete.",
                    [],
                    documents.Warnings.Concat([
                        "Project Forge did not start a second synchronous AI request while the shared planner was already running."
                    ]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }

            // Release the read transaction before the shared queue captures its own starting revision.
            await transaction.CommitAsync(cancellationToken);
            var runId = await ProjectFlowHiveAiPlannerOrchestrationModule.QueueForActorAsync(
                connection, projectId, actualUserId, effectiveUserId, seed,
                outcome, detailLevel, correlationId, cancellationToken);
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "Project Forge queued the shared durable planner in the background. Retry this request while the planner completes; no second synchronous AI request was started.",
                [],
                documents.Warnings.Concat([
                    $"Shared planner run {runId:D} is queued for background generation."
                ]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("ProjectPlanningAiOrchestrator")
                .LogWarning(exception, "Project Forge could not reuse or queue the durable FlowHive planner.");
            return ProjectPlanningGenerationResult.Failed(
                "project_planning_ai_temporarily_unavailable",
                "The shared durable planner is temporarily unavailable. No Project Forge draft was changed.",
                [],
                documents.Warnings.Concat([
                    "The current project documents remain available; retrying does not require another upload."
                ]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private static ProjectPlanningGenerationResult ReusedDurableResult(
        DurablePlannerRow row,
        ProjectPlanningDocumentResolution documents)
    {
        var plan = row.Plan!;
        var schedule = row.Schedule!;
        var validation = row.Validation!;
        var warnings = row.Warnings
            .Concat(documents.Warnings)
            .Concat([
                "Project Forge reused the completed durable FlowHive planning artifact for the same project and current document versions; no second model inference was executed in this HTTP request."
            ])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var correlationId = !string.IsNullOrWhiteSpace(plan.CelarAiCorrelationId)
            ? plan.CelarAiCorrelationId!
            : row.CorrelationId;
        var provider = !string.IsNullOrWhiteSpace(plan.CelarAiProviderCode)
            ? plan.CelarAiProviderCode!
            : "shared_durable_flowhive_planner";
        var composition = new CelarAiComposeResult(
            Status: "celar_ai_solution_draft_completed",
            Mode: "project_plan",
            PrimaryExecutionPath: "shared_durable_flowhive_planner",
            ProjectId: plan.ProjectId,
            ProjectCode: plan.ProjectCode ?? string.Empty,
            ProjectName: plan.ProjectName ?? string.Empty,
            DetailedAnswer: null,
            FlowHivePlan: null,
            SowDraft: null,
            Timeline: [],
            Diagram: null,
            Citations: [],
            Warnings: warnings,
            MissingEvidence: [],
            Conflicts: [],
            CoverageScore: 1m,
            Confidence: plan.CelarAiConfidence ?? 1m,
            ConfidenceExplanation: "Project Forge reused the already source-grounded durable FlowHive plan for the exact current project-document authority.",
            ExternalAssistance: null,
            DataAsOf: row.CompletedAt ?? DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            SelectedTarget: provider,
            AttemptedTargets: [provider],
            SkippedTargets: [],
            TargetDecisions: []);

        return new ProjectPlanningGenerationResult(
            validation.Valid && schedule.Valid,
            row.Status,
            validation.Valid && schedule.Valid
                ? "Project Forge reused the completed source-cited five-phase FlowHive planning artifact."
                : "The shared FlowHive planning artifact requires correction before Project Forge can save it.",
            composition,
            plan,
            validation,
            schedule,
            [],
            warnings);
    }

    private static bool MatchesCurrentAuthority(
        ProjectFlowHivePlanRequest plan,
        Guid projectId,
        string currentSowVersion,
        string currentGsdVersion)
    {
        if (plan.ProjectId != projectId) return false;
        if (string.IsNullOrWhiteSpace(currentSowVersion)
            || !string.Equals(plan.SowVersion, currentSowVersion, StringComparison.OrdinalIgnoreCase))
            return false;
        if (currentGsdVersion.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(plan.GsdVersion)) return false;
        }
        else if (!string.Equals(plan.GsdVersion, currentGsdVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var executable = (plan.Tasks ?? [])
            .Where(task => !task.IsSummary && !task.IsMilestone)
            .ToArray();
        return executable.Length > 0
            && executable.All(task => (task.CitationIds?.Count ?? 0) > 0)
            && (plan.CelarAiCitationIds?.Count ?? 0) > 0;
    }

    private static T? Deserialize<T>(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        try { return JsonSerializer.Deserialize<T>(value, DurablePlannerJson); }
        catch { return default; }
    }

    private static ProjectFlowHivePlanRequest ApplySchedule(
        ProjectFlowHivePlanRequest generated,
        ProjectFlowHiveScheduleResult schedule,
        CelarAiComposeResult composition,
        ProjectPlanningDocumentResolution documents)
    {
        var scheduledByWbs = schedule.Tasks
            .GroupBy(task => task.WbsNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return generated with
        {
            Tasks = (generated.Tasks ?? []).Select(task =>
            {
                var wbs = task.WbsNumber?.Trim() ?? string.Empty;
                return scheduledByWbs.TryGetValue(wbs, out var scheduled)
                    ? task with
                    {
                        EstimatedStartDate = scheduled.StartDate,
                        EstimatedFinishDate = scheduled.EndDate
                    }
                    : task;
            }).ToArray(),
            Milestones = (generated.Milestones ?? []).Select(milestone =>
                scheduledByWbs.TryGetValue(milestone.PredecessorWbs, out var predecessor)
                    ? milestone with { TargetDate = predecessor.EndDate }
                    : milestone).ToArray(),
            SowVersion = documents.StatementOfWork?.ActiveVersionId?.ToString("D"),
            GsdVersion = documents.GeneralSolutionDesign?.ActiveVersionId?.ToString("D"),
            SourceKind = "celar_ai",
            CelarAiProviderCode = composition.SelectedTarget.Length > 0
                ? composition.SelectedTarget
                : composition.PrimaryExecutionPath,
            CelarAiCorrelationId = composition.CorrelationId,
            CelarAiConfidence = composition.Confidence
        };
    }

    private static string Clean(string? value, int maximum, string fallback)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private sealed record DurablePlannerRow(
        Guid RunId,
        string Status,
        string Phase,
        int ProgressPercent,
        ProjectFlowHivePlanRequest? Plan,
        ProjectFlowHiveScheduleResult? Schedule,
        ProjectFlowHivePlanValidationResult? Validation,
        IReadOnlyList<string> Warnings,
        string CorrelationId,
        DateTimeOffset? CompletedAt);
}

internal sealed record ProjectPlanningGenerationResult(
    bool Succeeded,
    string Status,
    string Message,
    CelarAiComposeResult? Composition,
    ProjectFlowHivePlanRequest? Plan,
    ProjectFlowHivePlanValidationResult? Validation,
    ProjectFlowHiveScheduleResult? Schedule,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> Warnings)
{
    internal static ProjectPlanningGenerationResult NotReady(
        IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings,
        string message) => new(
            false,
            "project_planning_documents_processing",
            message,
            null,
            null,
            null,
            null,
            blockers,
            warnings);

    internal static ProjectPlanningGenerationResult Failed(
        string status,
        string message,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> warnings) => new(
            false,
            status,
            message,
            null,
            null,
            null,
            null,
            missingEvidence,
            warnings);
}
