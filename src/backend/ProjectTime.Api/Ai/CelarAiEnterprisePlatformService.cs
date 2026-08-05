using System.Text;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Composes private Celar AI results into enterprise review artifacts for
/// Timesheet, SOW, project planning, high-level timelines, and diagrams. The
/// service never saves a Timesheet, publishes a SOW, baselines a project plan,
/// assigns a resource, or commits a customer date.
/// </summary>
public sealed class CelarAiEnterprisePlatformService
{
    private readonly PulseAiPrivateRagService _privateRag;
    private readonly CelarAiExternalReasoningService _externalReasoning;
    private readonly ILogger<CelarAiEnterprisePlatformService> _logger;

    public CelarAiEnterprisePlatformService(
        PulseAiPrivateRagService privateRag,
        CelarAiExternalReasoningService externalReasoning,
        ILogger<CelarAiEnterprisePlatformService> logger)
    {
        _privateRag = privateRag;
        _externalReasoning = externalReasoning;
        _logger = logger;
    }

    public async Task<object> GetReadinessAsync(CancellationToken cancellationToken = default) => new
    {
        status = "celar_ai_enterprise_platform_interface_ready",
        contractVersion = CelarAiEnterprisePlatformPolicy.ContractVersion,
        architectureVersion = CelarAiEnterprisePlatformPolicy.ArchitectureVersion,
        supportedModes = CelarAiEnterprisePlatformPolicy.SupportedModes,
        privateRag = await _privateRag.GetReadinessAsync(cancellationToken),
        externalFallback = CelarAiExternalReasoningService.Readiness(),
        capabilities = new object[]
        {
            new { code = "ask_and_search", owner = "Module 011", state = "available", authority = "current effective user and owning module" },
            new { code = "people_and_work", owner = "Modules 001, 002, 019, 066, 070", state = "available", authority = "server-authorized read tools" },
            new { code = "timesheet_description", owner = "Module 001", state = "private_rag_available_when_configured", authority = "engineer review and apply" },
            new { code = "sow_draft", owner = "Module 025", state = "reviewable_private_draft", authority = "authorized commercial review" },
            new { code = "project_plan", owner = "Module 066", state = "reviewable_private_draft", authority = "PM and Engineering review" },
            new { code = "project_timeline", owner = "Module 066", state = "deterministic_high_level_draft", authority = "FlowHive schedule engine before baseline" },
            new { code = "project_diagram", owner = "Module 011 / 066", state = "reviewable_visual_draft", authority = "source citations and human review" },
            new { code = "sanitized_external_reasoning", owner = "Module 064", state = "disabled_by_default", authority = "DLP and provider policy" }
        },
        guarantees = new[]
        {
            "Private project documents and live Pulse records remain inside the approved private boundary.",
            "External providers receive only a generic sanitized problem when both runtime policy and the caller allow it.",
            "A generated timeline or diagram is a review artifact, not a customer commitment or approved project baseline.",
            "All project-specific facts remain grounded in private Celar AI evidence and cited source versions.",
            "No mutation, arbitrary SQL, arbitrary URL, provider secret, model endpoint, or raw chunk text is returned."
        },
        generatedAt = DateTimeOffset.UtcNow
    };

    public async Task<CelarAiComposeResult> ComposeAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        CelarAiComposeRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var mode = NormalizeMode(request.Mode);
        var correlationId = CorrelationId(context);
        var projectCode = Clean(request.ProjectCode, 120);
        var projectName = Clean(request.ProjectName, 300);

        try
        {
            PulseAiPrivateRagAnswer privateResult;
            if (mode == "timesheet_description")
            {
                privateResult = await _privateRag.GenerateTimesheetAsync(
                    actualUserId,
                    effectiveUserId,
                    new PulseAiPrivateTimesheetRequest(
                        WorkDate: request.WorkDate,
                        TimeType: request.TimeType,
                        RowType: request.RowType,
                        RowLabel: request.RowLabel,
                        ProjectCode: projectCode,
                        ProjectName: projectName,
                        TaskCode: request.TaskCode,
                        TaskName: request.TaskName,
                        CategoryCode: request.CategoryCode,
                        EngineerNote: request.EngineerNote,
                        DetailLevel: request.DetailLevel ?? "standard"),
                    cancellationToken);
            }
            else if (mode == "sow_draft")
            {
                var outcome = Clean(request.RequestedOutcome, 6_000);
                var question = $"""
                    Create a comprehensive, reviewable Statement of Work draft for the authorized project.
                    Project code: {projectCode}
                    Project name: {projectName}
                    Requested outcome: {(outcome.Length == 0 ? "Use the authorized scope, deliverables, design, responsibilities, constraints, acceptance evidence, assumptions, dependencies, risks, and open questions." : outcome)}
                    The output is a draft only. Separate cited facts from assumptions. Do not invent prices, rates, dates, quantities, responsibilities, acceptance criteria, or contractual commitments.
                    """;
                privateResult = await _privateRag.AskHelpSearchAsync(
                    actualUserId,
                    effectiveUserId,
                    new PulseAiPrivateHelpSearchRequest(
                        Question: question,
                        ProjectCode: projectCode,
                        ProjectName: projectName,
                        DetailLevel: request.DetailLevel ?? "comprehensive",
                        IncludeAuthorizedProjectDocuments: true,
                        IncludeDirectProductKnowledge: false),
                    cancellationToken);
            }
            else
            {
                privateResult = await _privateRag.GenerateFlowHivePlanAsync(
                    actualUserId,
                    effectiveUserId,
                    new PulseAiPrivateFlowHiveRequest(
                        ProjectCode: projectCode,
                        ProjectName: projectName,
                        RequestedOutcome: request.RequestedOutcome,
                        DetailLevel: request.DetailLevel ?? "comprehensive"),
                    cancellationToken);
            }

            var plan = privateResult.FlowHivePlan;
            var detailed = privateResult.Answer ?? (plan is null ? null : BuildPlanSummary(plan, privateResult));
            var sow = mode == "sow_draft" ? BuildSowDraft(privateResult, projectCode, projectName) : null;
            var timeline = plan is null
                ? Array.Empty<CelarAiTimelineItem>()
                : BuildTimeline(plan, request.StartDate ?? NextMonday(DateOnly.FromDateTime(DateTime.UtcNow)));
            var diagram = mode is "project_diagram" or "project_plan" or "project_timeline"
                ? BuildDiagram(plan, timeline, request.DiagramType, projectCode, projectName)
                : null;

            var confidence = plan?.Confidence ?? detailed?.Confidence ?? 0m;
            var warnings = new List<string>(privateResult.Warnings);
            warnings.AddRange(mode switch
            {
                "timesheet_description" => ["The Engineer must verify the factual description before applying it. Celar AI did not save or submit time."],
                "sow_draft" => ["This SOW is a non-binding draft. Commercial, legal, security, technical, and customer approval remain required."],
                _ => ["The project plan, timeline, and diagram are review artifacts. PM and Engineering must validate durations, dependencies, resources, assumptions, and customer commitments before baseline."]
            });

            CelarAiExternalReasoningResult? external = null;
            var evidenceLimited = confidence < 0.65m
                || privateResult.Status is "partial" or "failed" or "blocked"
                || privateResult.MissingEvidence.Count > 0;
            if (request.AllowSanitizedExternalFallback
                && evidenceLimited
                && CelarAiEnterprisePlatformPolicy.ExternalFallbackEligibleModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
            {
                external = await _externalReasoning.TryGenerateAsync(
                    new CelarAiExternalReasoningRequest(
                        Mode: mode,
                        Purpose: $"generic_{mode}_reasoning_support",
                        GenericProblem: GenericExternalProblem(mode),
                        SensitiveTerms: [projectCode, projectName, "US Signal", "Pulse"],
                        ContainsPrivateDocumentText: false,
                        ContainsFinancialValues: false,
                        ContainsPeopleRecords: false,
                        AcknowledgeSanitizedExternalUse: true,
                        CapabilityCode: request.CapabilityCode),
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(external.Content))
                {
                    warnings.Add("Celar AI received generic, sanitized reasoning assistance through Module 064. It did not send project, customer, people, financial, or document content. Apply the generic guidance only after private source verification.");
                }
            }

            var status = privateResult.Status == "completed"
                ? "celar_ai_solution_draft_completed"
                : privateResult.Status == "partial"
                    ? "celar_ai_solution_draft_partial"
                    : "celar_ai_solution_draft_evidence_limited";
            var path = external?.Authorized == true && !string.IsNullOrWhiteSpace(external.Content)
                ? "private_celar_rag_plus_sanitized_generic_module064_assistance"
                : "private_celar_rag_and_deterministic_composer";

            return new CelarAiComposeResult(
                Status: status,
                Mode: mode,
                PrimaryExecutionPath: path,
                ProjectId: privateResult.ProjectId,
                ProjectCode: privateResult.ProjectCode,
                ProjectName: privateResult.ProjectName,
                DetailedAnswer: detailed,
                FlowHivePlan: plan,
                SowDraft: sow,
                Timeline: timeline,
                Diagram: diagram,
                Citations: privateResult.Citations,
                Warnings: warnings,
                MissingEvidence: privateResult.MissingEvidence,
                Conflicts: privateResult.Conflicts,
                CoverageScore: privateResult.CoverageScore,
                Confidence: confidence,
                ConfidenceExplanation: plan?.ConfidenceExplanation
                    ?? detailed?.ConfidenceExplanation
                    ?? "Confidence is limited because no private answer or project plan was produced.",
                ExternalAssistance: external,
                DataAsOf: privateResult.DataAsOf,
                CorrelationId: string.IsNullOrWhiteSpace(privateResult.CorrelationId)
                    ? correlationId
                    : privateResult.CorrelationId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Celar AI enterprise composition failed without logging question, project, document, or provider content. Mode={Mode} Diagnostic={Diagnostic}",
                mode,
                exception.GetType().Name.ToLowerInvariant());
            return new CelarAiComposeResult(
                Status: "celar_ai_solution_draft_failed",
                Mode: mode,
                PrimaryExecutionPath: "failed_closed",
                ProjectId: null,
                ProjectCode: projectCode,
                ProjectName: projectName,
                DetailedAnswer: null,
                FlowHivePlan: null,
                SowDraft: null,
                Timeline: [],
                Diagram: null,
                Citations: [],
                Warnings: ["The request failed closed without exposing private evidence. Use the correlation ID for troubleshooting."],
                MissingEvidence: ["A complete private composition result was not available."],
                Conflicts: [],
                CoverageScore: 0m,
                Confidence: 0.1m,
                ConfidenceExplanation: $"Low confidence because composition failed ({exception.GetType().Name.ToLowerInvariant()}).",
                ExternalAssistance: null,
                DataAsOf: DateTimeOffset.UtcNow,
                CorrelationId: correlationId);
        }
    }

    private static CelarAiSowDraft? BuildSowDraft(
        PulseAiPrivateRagAnswer result,
        string projectCode,
        string projectName)
    {
        var answer = result.Answer;
        if (answer is null) return null;
        var titleProject = projectName.Length > 0 ? projectName : projectCode.Length > 0 ? projectCode : "Authorized Project";
        var analysis = answer.DetailedAnalysis.Count > 0 ? answer.DetailedAnalysis : answer.SourceEvidence;
        return new CelarAiSowDraft(
            Title: $"Statement of Work Draft — {titleProject}",
            ExecutiveSummary: answer.ExecutiveSummary,
            Objectives: TakeOrFallback([answer.DirectConclusion], "Confirm the business and technical objectives with the customer and delivery stakeholders."),
            InScope: TakeOrFallback(analysis.Take(12), "Confirm the detailed in-scope services from the authoritative SOW/GSD and approved project evidence."),
            OutOfScope: TakeOrFallback(answer.Limitations.Take(8), "Anything not explicitly approved in the final scope remains out of scope."),
            Deliverables: TakeOrFallback(answer.RecommendedActions.Take(10), "Confirm named deliverables, formats, owners, and acceptance evidence during review."),
            CustomerResponsibilities:
            [
                "Provide timely access, technical contacts, decisions, prerequisites, and acceptance participation identified during review.",
                "Validate that customer responsibilities match the signed commercial and technical source documents."
            ],
            UsSignalResponsibilities:
            [
                "Perform only the reviewed and approved services using authorized delivery resources and governed project controls.",
                "Maintain project evidence, risks, decisions, status, and acceptance records in the owning Pulse modules."
            ],
            Assumptions: TakeOrFallback(answer.Assumptions.Take(12), "No assumption is contractually valid until it is reviewed and approved."),
            Dependencies: TakeOrFallback(answer.Conflicts.Concat(answer.KnownUnknownAndStaleValues).Take(12), "Confirm technical, customer, vendor, resource, and scheduling dependencies."),
            AcceptanceCriteria: TakeOrFallback(answer.SourceEvidence.Take(10), "Define objective, measurable acceptance criteria and required evidence before final approval."),
            TimelineAndMilestones:
            [
                "Use the approved FlowHive plan and deterministic schedule for dates; this draft does not commit a customer timeline.",
                "Include discovery, design validation, implementation, testing, acceptance, operational handoff, and closeout as applicable."
            ],
            Risks: TakeOrFallback(answer.RisksAndImplications.Take(10), "Review delivery, technical, resource, customer, security, and dependency risks."),
            OpenQuestions: TakeOrFallback(answer.KnownUnknownAndStaleValues.Take(10), "Resolve every material unknown before final SOW approval."),
            CitationIds: answer.CitationIds,
            ReviewRequired: true,
            ContractuallyBinding: false);
    }

    private static PulseAiPrivateDetailedAnswer BuildPlanSummary(
        PulseAiPrivateFlowHivePlan plan,
        PulseAiPrivateRagAnswer result)
    {
        var taskSummary = plan.Tasks.Take(20).Select(task =>
            $"{task.Wbs} — {task.Name}: {task.Description} Estimated duration {task.EstimatedDurationDays:0.##} business day(s); predecessors {JoinOrNone(task.Predecessors)}; roles {JoinOrNone(task.RequiredRoles)}.").ToArray();
        return new PulseAiPrivateDetailedAnswer(
            DirectConclusion: $"Celar AI prepared a reviewable project plan containing {plan.Tasks.Count} task(s) and {plan.Milestones.Count} milestone(s).",
            ExecutiveSummary: plan.Objective,
            ScopeAndFilters:
            [
                $"Project: {result.ProjectCode} {result.ProjectName}".Trim(),
                "Source scope: current authorized private project evidence only.",
                "Output scope: draft WBS, dependencies, milestones, risks, assumptions, open questions, high-level timeline, and diagram."
            ],
            DetailedAnalysis: taskSummary,
            SourceEvidence: result.Citations.Select(citation => $"{citation.OriginalFileName} {citation.DocumentVersion} — {citation.CitationAnchor}").ToArray(),
            Calculations:
            [
                "The high-level timeline uses business-day durations and predecessor order from the private FlowHive draft.",
                "The authoritative baseline must be recalculated by the deterministic FlowHive scheduling engine with calendars, holidays, lead/lag, capacity, and approved dates."
            ],
            KnownUnknownAndStaleValues: result.MissingEvidence,
            Assumptions: plan.Assumptions,
            Conflicts: plan.Conflicts,
            Limitations:
            [
                "The generated timeline is high level and is not a customer commitment.",
                "Resource availability, holidays, technical validation, and customer dependencies require PM and Engineering review."
            ],
            RisksAndImplications: plan.Risks,
            RecommendedActions:
            [
                "Review every task, duration, predecessor, role, milestone, and citation with the Project Manager and assigned Engineering team.",
                "Resolve open questions and conflicts, then run the deterministic Module 066 schedule calculation before proposing a baseline.",
                "Record the approved baseline and subsequent changes through the governed FlowHive workflow."
            ],
            NavigationTargets: ["#project-flowhive", "#project-workspace", "#capacity-pipeline-forecast"],
            CitationIds: plan.CitationIds,
            Confidence: plan.Confidence,
            ConfidenceExplanation: plan.ConfidenceExplanation,
            DataAsOf: result.DataAsOf);
    }

    private static IReadOnlyList<CelarAiTimelineItem> BuildTimeline(
        PulseAiPrivateFlowHivePlan plan,
        DateOnly requestedStart)
    {
        var start = NextBusinessDay(requestedStart);
        var endByWbs = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        var items = new List<CelarAiTimelineItem>();
        DateOnly? previousEnd = null;

        for (var index = 0; index < plan.Tasks.Count; index++)
        {
            var task = plan.Tasks[index];
            var predecessorEnds = task.Predecessors
                .Where(value => endByWbs.ContainsKey(value))
                .Select(value => endByWbs[value])
                .ToArray();
            var taskStart = predecessorEnds.Length > 0
                ? NextBusinessDay(predecessorEnds.Max().AddDays(1))
                : previousEnd is not null
                    ? NextBusinessDay(previousEnd.Value.AddDays(1))
                    : start;
            var duration = Math.Max(1, (int)Math.Ceiling(task.EstimatedDurationDays <= 0 ? 1m : task.EstimatedDurationDays));
            var taskEnd = AddBusinessDaysInclusive(taskStart, duration);
            var id = $"task-{index + 1}";
            items.Add(new CelarAiTimelineItem(
                Id: id,
                Wbs: task.Wbs,
                Name: task.Name,
                Description: task.Description,
                StartDate: taskStart,
                EndDate: taskEnd,
                DurationBusinessDays: duration,
                Predecessors: task.Predecessors,
                RequiredRoles: task.RequiredRoles,
                CitationIds: task.CitationIds,
                IsAssumption: task.IsAssumption));
            if (!string.IsNullOrWhiteSpace(task.Wbs)) endByWbs[task.Wbs] = taskEnd;
            previousEnd = taskEnd;
        }

        return items;
    }

    private static CelarAiGeneratedDiagram? BuildDiagram(
        PulseAiPrivateFlowHivePlan? plan,
        IReadOnlyList<CelarAiTimelineItem> timeline,
        string? requestedType,
        string projectCode,
        string projectName)
    {
        if (plan is null || plan.Tasks.Count == 0) return null;
        var type = NormalizeDiagramType(requestedType);
        var nodes = new List<CelarAiDiagramNode>();
        var edges = new List<CelarAiDiagramEdge>();
        var nodeByWbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < plan.Tasks.Count; index++)
        {
            var task = plan.Tasks[index];
            var id = $"N{index + 1}";
            nodeByWbs[task.Wbs] = id;
            var timelineItem = timeline.ElementAtOrDefault(index);
            var subtitle = timelineItem is null
                ? $"{task.EstimatedDurationDays:0.##} business day(s)"
                : $"{timelineItem.StartDate:yyyy-MM-dd} → {timelineItem.EndDate:yyyy-MM-dd}";
            nodes.Add(new CelarAiDiagramNode(
                Id: id,
                Kind: "task",
                Label: $"{task.Wbs} {task.Name}".Trim(),
                Subtitle: subtitle,
                Sequence: index + 1,
                CitationIds: task.CitationIds,
                IsAssumption: task.IsAssumption));
        }

        for (var index = 0; index < plan.Tasks.Count; index++)
        {
            var task = plan.Tasks[index];
            var target = $"N{index + 1}";
            var linked = false;
            foreach (var predecessor in task.Predecessors)
            {
                if (!nodeByWbs.TryGetValue(predecessor, out var source)) continue;
                edges.Add(new CelarAiDiagramEdge(source, target, "predecessor", "dependency"));
                linked = true;
            }
            if (!linked && index > 0)
                edges.Add(new CelarAiDiagramEdge($"N{index}", target, "next", "sequence"));
        }

        for (var index = 0; index < plan.Milestones.Count; index++)
        {
            var milestone = plan.Milestones[index];
            var id = $"M{index + 1}";
            nodes.Add(new CelarAiDiagramNode(
                Id: id,
                Kind: "milestone",
                Label: milestone.Name,
                Subtitle: milestone.ProposedTiming,
                Sequence: plan.Tasks.Count + index + 1,
                CitationIds: milestone.CitationIds,
                IsAssumption: milestone.IsAssumption));
            edges.Add(new CelarAiDiagramEdge(
                plan.Tasks.Count > 0 ? $"N{plan.Tasks.Count}" : "N1",
                id,
                "milestone",
                "milestone"));
        }

        var mermaid = new StringBuilder("flowchart LR\n");
        foreach (var node in nodes)
        {
            var label = MermaidLabel($"{node.Label}<br/>{node.Subtitle}");
            mermaid.AppendLine(node.Kind == "milestone"
                ? $"  {node.Id}{{\"{label}\"}}"
                : $"  {node.Id}[\"{label}\"]");
        }
        foreach (var edge in edges)
            mermaid.AppendLine($"  {edge.From} -->|{MermaidLabel(edge.Label)}| {edge.To}");

        var titleProject = projectName.Length > 0 ? projectName : projectCode.Length > 0 ? projectCode : "Authorized Project";
        return new CelarAiGeneratedDiagram(
            DiagramType: type,
            Title: $"Celar AI Project Delivery Diagram — {titleProject}",
            Description: "A private, source-grounded project-flow visualization generated from the reviewable FlowHive plan. Dates and sequencing require PM and Engineering validation.",
            Nodes: nodes,
            Edges: edges,
            MermaidSource: mermaid.ToString(),
            AccessibilitySummary: $"The diagram contains {nodes.Count} node(s) and {edges.Count} relationship(s). Tasks are arranged in delivery order and milestones are shown as review gates.",
            CustomerCommitment: false,
            RequiresPmReview: true,
            RequiresEngineeringReview: true);
    }

    private static IEnumerable<string> TakeOrFallback(IEnumerable<string> values, string fallback)
    {
        var rows = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return rows.Length > 0 ? rows : [fallback];
    }

    private static string GenericExternalProblem(string mode) => mode switch
    {
        "sow_draft" => "Provide a generic professional-services SOW quality checklist covering objectives, scope, exclusions, deliverables, responsibilities, assumptions, dependencies, acceptance criteria, milestones, risks, change control, and review gates.",
        "project_timeline" => "Provide generic sequencing guidance for a complex professional-services implementation using discovery, design validation, prerequisites, implementation, testing, acceptance, operational handoff, and closeout. Do not provide customer-specific dates.",
        "project_diagram" => "Provide generic systems-engineering diagram guidance for showing project inputs, governance, discovery, design, implementation, validation, acceptance, operational handoff, dependencies, risks, and review gates.",
        _ => "Provide a generic professional-services project-planning checklist covering WBS quality, dependencies, milestones, roles, assumptions, risks, acceptance, handoff, and human review."
    };

    private static string NormalizeMode(string? value)
    {
        var mode = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return CelarAiEnterprisePlatformPolicy.SupportedModes.Contains(mode, StringComparer.OrdinalIgnoreCase)
            ? mode
            : "project_plan";
    }

    private static string NormalizeDiagramType(string? value)
    {
        var type = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return type is "flowchart" or "timeline" or "dependency" or "swimlane"
            ? type
            : "flowchart";
    }

    private static DateOnly NextMonday(DateOnly value)
    {
        var days = ((int)DayOfWeek.Monday - (int)value.DayOfWeek + 7) % 7;
        return value.AddDays(days == 0 ? 7 : days);
    }

    private static DateOnly NextBusinessDay(DateOnly value)
    {
        var current = value;
        while (current.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            current = current.AddDays(1);
        return current;
    }

    private static DateOnly AddBusinessDaysInclusive(DateOnly start, int days)
    {
        var current = NextBusinessDay(start);
        var remaining = Math.Max(1, days) - 1;
        while (remaining > 0)
        {
            current = current.AddDays(1);
            if (current.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            remaining -= 1;
        }
        return current;
    }

    private static string MermaidLabel(string value) =>
        value.Replace("\"", "'", StringComparison.Ordinal)
            .Replace("[", "(", StringComparison.Ordinal)
            .Replace("]", ")", StringComparison.Ordinal)
            .Replace("{", "(", StringComparison.Ordinal)
            .Replace("}", ")", StringComparison.Ordinal)
            .Replace("`", "'", StringComparison.Ordinal);

    private static string JoinOrNone(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values);

    private static string CorrelationId(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Correlation-Id", out var value)
            && !string.IsNullOrWhiteSpace(value.ToString())
            ? Clean(value.ToString(), 160)
            : Clean(context.TraceIdentifier, 160);

    private static string Clean(string? value, int maximum)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }
}
