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
    private readonly CelarAiCapabilityRouter _router;
    private readonly ILogger<CelarAiEnterprisePlatformService> _logger;

    public CelarAiEnterprisePlatformService(
        PulseAiPrivateRagService privateRag,
        CelarAiCapabilityRouter router,
        ILogger<CelarAiEnterprisePlatformService> logger)
    {
        _privateRag = privateRag;
        _router = router;
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
            new { code = "project_forge_plan_estimate", owner = "Module 033", state = "document_grounded_review_draft", authority = "PM and assigned Engineer review before explicit adoption" },
            new { code = "project_timeline", owner = "Module 066", state = "deterministic_high_level_draft", authority = "FlowHive schedule engine before baseline" },
            new { code = "project_diagram", owner = "Module 011 / 066", state = "reviewable_visual_draft", authority = "source citations and human review" },
            new { code = "sanitized_external_reasoning", owner = "Module 064", state = "automatic_when_persisted_route_and_both_runtime_privacy_flags_allow", authority = "closed backend capsule, DLP, and provider policy" }
        },
        guarantees = new[]
        {
            "Private project documents and live Pulse records remain inside the approved private boundary.",
            "External providers receive only a fixed backend-owned generic capsule selected from a closed purpose category when runtime policy allows it.",
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
            var capability = ResolveCapability(mode, request);
            var externalCapsulePurpose = ResolveExternalCapsulePurpose(mode);
            var externalCapsuleReady = CelarAiExternalCapsuleCatalog.TryResolve(
                externalCapsulePurpose,
                out _);
            var identityTerms = new[] { projectCode, projectName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            PulseAiPrivateRagAnswer? privateResult = null;
            var routed = await _router.GenerateWithPrivateTargetAsync(
                new ProjectPulseAiGenerationRequest(
                    Feature: capability,
                    SystemPrompt: "Use authorized private project evidence to create the requested review artifact. Never invent facts, commitments, approval, dates, prices, people, or completed work.",
                    UserPrompt: BuildPrivateComposePrompt(mode, request, projectCode, projectName),
                    MaxOutputTokens: _privateRag.Options().MaximumOutputTokens,
                    Temperature: 0.10),
                new CelarAiCapabilityExecutionContext(
                    Feature: capability,
                    ContainsPrivateDocuments: mode != "timesheet_description"
                        || request.ProjectId is not null
                        || request.TaskId is not null
                        || request.AssignmentId is not null
                        || projectCode.Length > 0
                        || projectName.Length > 0,
                    ContainsCustomerIdentity: identityTerms.Length > 0,
                    ContainsPeopleRecords: false,
                    ContainsFinancialValues: mode == "sow_draft",
                    // Compatibility flag cannot gate a closed router-owned
                    // capsule; persisted order plus runtime policy governs it.
                    AllowSanitizedExternalAssistance: false,
                    SensitiveTerms: identityTerms,
                    ConsumerModule: capability == CelarAiCapabilityCatalog.SowGsdPlanning
                        ? "011/025"
                        : capability == CelarAiCapabilityCatalog.ProjectForgePlanEstimate
                            ? "011/033"
                            : "011/066",
                    CorrelationId: correlationId,
                    IdentityTerms: identityTerms,
                    ExternalCapsulePurpose: externalCapsulePurpose),
                async privateCancellationToken =>
                {
                    privateResult = await ExecutePrivateComposeAsync(
                        actualUserId,
                        effectiveUserId,
                        mode,
                        request,
                        projectCode,
                        projectName,
                        privateCancellationToken);
                    return PrivateComposeTargetResult(privateResult);
                },
                localFallback: () => LocalEnterpriseFallback(mode),
                cancellationToken: cancellationToken);

            if (routed.Outcome == ProjectPulseAiOutcomes.Refusal)
            {
                // A safety refusal is terminal. Do not return any private RAG
                // artifacts that may have been assembled before the private
                // target refused, and do not construct external assistance.
                return RefusedComposeResult(mode, routed, correlationId);
            }

            var plan = privateResult?.FlowHivePlan;
            var detailed = privateResult?.Answer
                ?? (plan is null || privateResult is null ? null : BuildPlanSummary(plan, privateResult));
            var sow = mode == "sow_draft" && privateResult is not null
                ? BuildSowDraft(privateResult, projectCode, projectName)
                : null;
            var timeline = plan is null
                ? Array.Empty<CelarAiTimelineItem>()
                : BuildTimeline(plan, request.StartDate ?? NextMonday(DateOnly.FromDateTime(DateTime.UtcNow)));
            var diagram = mode is "project_diagram" or "project_plan" or "project_timeline"
                ? BuildDiagram(plan, timeline, request.DiagramType, projectCode, projectName)
                : null;

            var confidence = plan?.Confidence ?? detailed?.Confidence ?? 0.25m;
            var warnings = new List<string>(privateResult?.Warnings ?? []);
            warnings.AddRange(mode switch
            {
                "timesheet_description" => ["The Engineer must verify the factual description before applying it. Celar AI did not save or submit time."],
                "sow_draft" => ["This SOW is a non-binding draft. Commercial, legal, security, technical, and customer approval remain required."],
                _ => ["The project plan, timeline, and diagram are review artifacts. PM and Engineering must validate durations, dependencies, resources, assumptions, and customer commitments before baseline."]
            });

            CelarAiExternalReasoningResult? external = null;
            if (routed.Provider is CelarAiCapabilityTargets.Claude or CelarAiCapabilityTargets.OpenAi
                && externalCapsuleReady)
            {
                external = ToExternalAssistance(routed);
                if (!string.IsNullOrWhiteSpace(external.Content))
                {
                    warnings.Add("Celar AI received generic, sanitized reasoning assistance through Module 064. It did not send project, customer, people, financial, or document content. Apply the generic guidance only after private source verification.");
                }
            }
            if (!string.IsNullOrWhiteSpace(routed.Warning)) warnings.Add(routed.Warning);

            // The structured artifact is produced only by the private callback.
            // A later external/local target can add separate generic assistance,
            // but it does not replace or de-ground that private artifact.
            var status = privateResult?.Status == "completed"
                ? "celar_ai_solution_draft_completed"
                : privateResult?.Status == "partial"
                    ? "celar_ai_solution_draft_partial"
                    : "celar_ai_solution_draft_evidence_limited";
            var path = routed.Provider switch
                {
                    CelarAiCapabilityTargets.CelarAi => "private_celar_rag_and_deterministic_composer",
                    CelarAiCapabilityTargets.Claude or CelarAiCapabilityTargets.OpenAi
                        when privateResult?.FlowHivePlan is not null || privateResult?.Answer is not null
                        => "private_celar_rag_with_sanitized_generic_module064_assistance",
                    CelarAiCapabilityTargets.Claude or CelarAiCapabilityTargets.OpenAi => "sanitized_generic_module064_assistance",
                    _ when privateResult?.FlowHivePlan is not null || privateResult?.Answer is not null
                        => "private_evidence_composer_after_governed_local_route",
                    _ => "governed_local_template"
                };

            return new CelarAiComposeResult(
                Status: status,
                Mode: mode,
                PrimaryExecutionPath: path,
                ProjectId: privateResult?.ProjectId ?? request.ProjectId,
                ProjectCode: !string.IsNullOrWhiteSpace(privateResult?.ProjectCode) ? privateResult!.ProjectCode : projectCode,
                ProjectName: !string.IsNullOrWhiteSpace(privateResult?.ProjectName) ? privateResult!.ProjectName : projectName,
                DetailedAnswer: detailed,
                FlowHivePlan: plan,
                SowDraft: sow,
                Timeline: timeline,
                Diagram: diagram,
                Citations: privateResult?.Citations ?? [],
                Warnings: warnings,
                MissingEvidence: privateResult?.MissingEvidence ?? ["No private source-grounded composition completed."],
                Conflicts: privateResult?.Conflicts ?? [],
                CoverageScore: privateResult?.CoverageScore ?? 0m,
                Confidence: confidence,
                ConfidenceExplanation: plan?.ConfidenceExplanation
                    ?? detailed?.ConfidenceExplanation
                    ?? "Confidence is limited because no private source-grounded answer or project plan was produced.",
                ExternalAssistance: external,
                DataAsOf: privateResult?.DataAsOf ?? DateTimeOffset.UtcNow,
                CorrelationId: string.IsNullOrWhiteSpace(privateResult?.CorrelationId)
                    ? correlationId
                    : privateResult.CorrelationId,
                SelectedTarget: routed.Provider,
                AttemptedTargets: routed.AttemptedProviders,
                SkippedTargets: routed.SkippedProviders,
                TargetDecisions: routed.TargetDecisions ?? []);
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

    private static CelarAiComposeResult RefusedComposeResult(
        string mode,
        ProjectPulseAiRouteResult routed,
        string correlationId) =>
        new(
            Status: "celar_ai_solution_draft_refused",
            Mode: mode,
            PrimaryExecutionPath: "safety_refusal",
            ProjectId: null,
            ProjectCode: string.Empty,
            ProjectName: string.Empty,
            DetailedAnswer: null,
            FlowHivePlan: null,
            SowDraft: null,
            Timeline: [],
            Diagram: null,
            Citations: [],
            Warnings: ["The selected AI target declined the request. No generated or private-source content was returned."],
            MissingEvidence: [],
            Conflicts: [],
            CoverageScore: 0m,
            Confidence: 0m,
            ConfidenceExplanation: "No confidence score is available because the request was declined.",
            ExternalAssistance: null,
            DataAsOf: DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            SelectedTarget: routed.Provider,
            AttemptedTargets: routed.AttemptedProviders,
            SkippedTargets: routed.SkippedProviders,
            TargetDecisions: routed.TargetDecisions ?? []);

    private async Task<PulseAiPrivateRagAnswer> ExecutePrivateComposeAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        string mode,
        CelarAiComposeRequest request,
        string projectCode,
        string projectName,
        CancellationToken cancellationToken)
    {
        if (mode == "timesheet_description")
        {
            return await _privateRag.GenerateTimesheetAsync(
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
                    DetailLevel: request.DetailLevel ?? "detailed",
                    ProjectId: request.ProjectId,
                    TaskId: request.TaskId,
                    AssignmentId: request.AssignmentId),
                cancellationToken);
        }
        if (mode == "sow_draft")
        {
            var outcome = Clean(request.RequestedOutcome, 6_000);
            var question = $"""
                Create a comprehensive, reviewable Statement of Work draft for the authorized project.
                Project code: {projectCode}
                Project name: {projectName}
                Requested outcome: {(outcome.Length == 0 ? "Use the authorized scope, deliverables, design, responsibilities, constraints, acceptance evidence, assumptions, dependencies, risks, and open questions." : outcome)}
                The output is a draft only. Separate cited facts from assumptions. Do not invent prices, rates, dates, quantities, responsibilities, acceptance criteria, or contractual commitments.
                """;
            return await _privateRag.AskHelpSearchAsync(
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
        return await _privateRag.GenerateFlowHivePlanAsync(
            actualUserId,
            effectiveUserId,
            new PulseAiPrivateFlowHiveRequest(
                ProjectCode: projectCode,
                ProjectName: projectName,
                RequestedOutcome: request.RequestedOutcome,
                DetailLevel: request.DetailLevel ?? "comprehensive"),
            cancellationToken);
    }

    private static string ResolveCapability(string mode, CelarAiComposeRequest request)
    {
        if (mode == "timesheet_description")
        {
            return CelarAiCapabilityCatalog.ResolveTimesheetFeature(
                request.RowType,
                request.RowLabel,
                request.TaskCode,
                request.ProjectCode,
                request.ProjectName);
        }
        if (mode == "sow_draft") return CelarAiCapabilityCatalog.SowGsdPlanning;
        return string.Equals(
            request.CapabilityCode?.Trim(),
            CelarAiCapabilityCatalog.ProjectForgePlanEstimate,
            StringComparison.OrdinalIgnoreCase)
            ? CelarAiCapabilityCatalog.ProjectForgePlanEstimate
            : CelarAiCapabilityCatalog.ProjectFlowHivePlan;
    }

    private static string ResolveExternalCapsulePurpose(string mode) => mode switch
    {
        "sow_draft" => CelarAiExternalCapsuleCatalog.SowScopeQuality,
        "project_timeline" => CelarAiExternalCapsuleCatalog.ProjectTimelineQuality,
        "project_diagram" => CelarAiExternalCapsuleCatalog.ProjectDiagramQuality,
        "project_plan" => CelarAiExternalCapsuleCatalog.ProjectPlanQuality,
        _ => string.Empty
    };

    private static string BuildPrivateComposePrompt(
        string mode,
        CelarAiComposeRequest request,
        string projectCode,
        string projectName) => $"""
        Requested solution mode: {mode}
        Project code: {projectCode}
        Project name: {projectName}
        Requested outcome: {Clean(request.RequestedOutcome, 6_000)}
        Detail level: {Clean(request.DetailLevel, 80)}
        Use only authorized private project evidence. Return a review artifact; do not publish, baseline,
        assign, send, approve, contract, commit a date, or mutate any owning-module record.
        """;

    private static ProjectPulseAiProviderResult PrivateComposeTargetResult(PulseAiPrivateRagAnswer answer)
    {
        var safetyRefusal = IsPrivateSafetyRefusal(answer);
        var privateModelCompleted = !string.IsNullOrWhiteSpace(answer.ModelProvider)
            && !string.Equals(
                answer.ModelProvider,
                "governed_product_knowledge",
                StringComparison.OrdinalIgnoreCase)
            && !answer.ModelProvider.StartsWith("deterministic_", StringComparison.OrdinalIgnoreCase)
            && (answer.Answer is not null || answer.FlowHivePlan is not null);
        return new ProjectPulseAiProviderResult(
            Provider: CelarAiCapabilityTargets.CelarAi,
            Outcome: safetyRefusal
                ? ProjectPulseAiOutcomes.Refusal
                : privateModelCompleted
                ? ProjectPulseAiOutcomes.Success
                : ProjectPulseAiOutcomes.Unavailable,
            Content: privateModelCompleted && !safetyRefusal ? "private_rag_composition_completed" : null,
            Code: safetyRefusal
                ? "private_model_safety_refusal"
                : privateModelCompleted
                ? null
                : string.IsNullOrWhiteSpace(answer.DiagnosticCode)
                    ? "private_rag_model_not_used"
                    : answer.DiagnosticCode,
            Message: privateModelCompleted
                ? null
                : "The private Celar AI document composition target did not complete.",
            RequestId: null,
            Usage: null,
            HttpStatusCode: null);
    }

    private static bool IsPrivateSafetyRefusal(PulseAiPrivateRagAnswer answer) =>
        string.Equals(answer.Status, "refused", StringComparison.OrdinalIgnoreCase)
        || answer.DiagnosticCode.Contains("refus", StringComparison.OrdinalIgnoreCase)
        || answer.DiagnosticCode.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
        || answer.DiagnosticCode.Contains("safety", StringComparison.OrdinalIgnoreCase);

    private static CelarAiExternalReasoningResult ToExternalAssistance(
        ProjectPulseAiRouteResult routed)
    {
        var externalAttempted = routed.AttemptedProviders.Any(target =>
            target is CelarAiCapabilityTargets.Claude or CelarAiCapabilityTargets.OpenAi);
        var refused = routed.Outcome == ProjectPulseAiOutcomes.Refusal;
        return new CelarAiExternalReasoningResult(
            Status: refused
                ? "external_reasoning_refused"
                : routed.Provider == CelarAiCapabilityTargets.Local
                    ? "governed_generic_fallback_completed"
                    : "sanitized_external_reasoning_completed",
            Enabled: true,
            Authorized: true,
            ProviderCalled: externalAttempted,
            Provider: routed.Provider,
            Content: refused ? string.Empty : routed.Content,
            Warning: routed.Warning ?? (refused
                ? "The selected target declined the request and no later target was attempted."
                : "Generic assistance completed and must be verified against private source evidence before use."),
            Redactions: [],
            RemovedCategories: [],
            BlockedReasons: (routed.TargetDecisions ?? [])
                .Where(decision => decision.Outcome is "failed" or "skipped")
                .Select(decision => decision.ReasonCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            GeneratedAt: DateTimeOffset.UtcNow,
            AttemptedTargets: routed.AttemptedProviders,
            SkippedTargets: routed.SkippedProviders,
            TargetDecisions: routed.TargetDecisions ?? []);
    }

    private static string LocalEnterpriseFallback(string mode) => mode switch
    {
        "sow_draft" => "Use a generic review-only scope structure covering objectives, boundaries, exclusions, deliverables, responsibilities, assumptions, dependencies, acceptance criteria, milestones, risks, change control, and approval gates.",
        "project_timeline" => "Use discovery, design validation, prerequisites, implementation, testing, acceptance, handoff, and closeout as generic sequencing checkpoints; validate every duration and dependency against private evidence.",
        "project_diagram" => "Use a generic project flow showing inputs, governance, discovery, design, implementation, validation, acceptance, handoff, dependencies, risks, assumptions, and review gates.",
        "timesheet_description" => "The private model did not complete. The Engineer must write a detailed sentence-form description from work personally performed and verify it before saving or submission.",
        _ => "Use a phased review-only delivery structure with discovery, design, implementation, testing, acceptance, handoff, risks, dependencies, open questions, and human approval gates."
    };

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
