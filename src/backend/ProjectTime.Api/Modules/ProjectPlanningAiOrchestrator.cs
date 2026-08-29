using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// One source-backed planning engine for FlowHive and Project Forge. The private
/// project documents are resolved before this service is called. This service
/// enforces current-document citations, deterministic five-phase expansion, and
/// schedule calculation without silently compressing effort or duration.
/// </summary>
internal static class ProjectPlanningAiOrchestrator
{
    internal const string Contract = "project-planning-ai-orchestrator-v1-20260819";

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
            "Create a complete source-backed project planning draft. Extract each cited SOW work package once, then expand it into Plan, Design, Implement, Validate, and Release. Include detailed steps, products, platforms, versions, licensing, quantities, tools, systems, interfaces, access, inputs, outputs, responsibilities, acceptance, validation, rollback, risks, assumptions, open questions, roles, effort, duration, dependencies, milestones, and citations. Never fabricate missing information; convert it into open questions.");

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

        var currentDocumentIds = documents.CurrentDocumentIds;
        var currentCitations = composition.Citations
            .Where(citation => currentDocumentIds.Contains(citation.DocumentId))
            .ToArray();
        var currentCitationIds = currentCitations
            .Select(citation => citation.CitationId)
            .ToHashSet();
        var currentSowId = documents.StatementOfWork!.DocumentId;
        var sowCitations = currentCitations
            .Where(citation => citation.DocumentId == currentSowId)
            .ToArray();

        var privatePlan = composition.FlowHivePlan;
        var completedStatus = composition.Status is
            "celar_ai_solution_draft_completed" or
            "celar_ai_solution_draft_partial";
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
                    "Celar AI did not return a complete plan cited to the current authoritative Work Register SOW and current authorized project evidence. No generic plan was substituted."
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
            generated = ProjectFlowHiveDetailedPlanBuilder.Build(seed, privatePlan!);
        }
        catch (Exception exception)
        {
            return new ProjectPlanningGenerationResult(
                false,
                "project_planning_expansion_failed",
                "The cited work packages could not be expanded into the governed five-phase plan. No planning draft was changed.",
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
