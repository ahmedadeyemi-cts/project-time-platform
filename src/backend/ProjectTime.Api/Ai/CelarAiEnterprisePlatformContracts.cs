namespace ProjectTime.Api.Ai;

public static class CelarAiEnterprisePlatformPolicy
{
    public const string ContractVersion = "celar-ai-enterprise-platform-v1-20260801";
    public const string ArchitectureVersion = "celar-ai-private-first-architecture-v2";
    public const string ReadinessRoute = "/api/celar-ai/v1/platform/readiness";
    public const string ComposeRoute = "/api/celar-ai/v1/compose";

    public static readonly string[] SupportedModes =
    [
        "timesheet_description",
        "sow_draft",
        "project_plan",
        "project_timeline",
        "project_diagram"
    ];

    public static readonly string[] ExternalFallbackEligibleModes =
    [
        "sow_draft",
        "project_plan",
        "project_timeline",
        "project_diagram"
    ];
}

public sealed record CelarAiComposeRequest(
    string? Mode,
    string? ProjectCode,
    string? ProjectName,
    DateOnly? StartDate,
    string? RequestedOutcome,
    string? DetailLevel = "comprehensive",
    string? DiagramType = "flowchart",
    DateOnly? WorkDate = null,
    string? TimeType = null,
    string? RowType = null,
    string? RowLabel = null,
    string? TaskCode = null,
    string? TaskName = null,
    string? CategoryCode = null,
    string? EngineerNote = null,
    bool AllowSanitizedExternalFallback = false,
    Guid? ProjectId = null,
    Guid? TaskId = null,
    Guid? AssignmentId = null,
    string? CapabilityCode = null);

/// <summary>
/// Server-owned evidence for a Module 025 draft that does not have a Project ID
/// yet. This type is intentionally internal so the public Celar compose endpoint
/// cannot accept caller-asserted authoritative evidence.
/// </summary>
internal sealed record CelarAiAuthoritativeScopeEvidence(
    Guid EngagementId,
    int Revision,
    string EngagementNumber,
    string CustomerName,
    string ServiceOverview,
    DateTimeOffset SavedAt);

public sealed record CelarAiTimelineItem(
    string Id,
    string Wbs,
    string Name,
    string Description,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal DurationBusinessDays,
    IReadOnlyList<string> Predecessors,
    IReadOnlyList<string> RequiredRoles,
    IReadOnlyList<int> CitationIds,
    bool IsAssumption);

public sealed record CelarAiDiagramNode(
    string Id,
    string Kind,
    string Label,
    string Subtitle,
    int Sequence,
    IReadOnlyList<int> CitationIds,
    bool IsAssumption);

public sealed record CelarAiDiagramEdge(
    string From,
    string To,
    string Label,
    string Kind);

public sealed record CelarAiGeneratedDiagram(
    string DiagramType,
    string Title,
    string Description,
    IReadOnlyList<CelarAiDiagramNode> Nodes,
    IReadOnlyList<CelarAiDiagramEdge> Edges,
    string MermaidSource,
    string AccessibilitySummary,
    bool CustomerCommitment,
    bool RequiresPmReview,
    bool RequiresEngineeringReview);

public sealed record CelarAiSowWorkPackage(
    string Wbs,
    string Phase,
    string Name,
    string Description,
    IReadOnlyList<string> DetailedSteps,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> ValidationSteps,
    IReadOnlyList<string> CustomerResponsibilities,
    IReadOnlyList<string> UsSignalResponsibilities,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> OpenQuestions,
    decimal EstimatedDurationDays,
    decimal? EstimatedHours,
    IReadOnlyList<string> RequiredRoles,
    IReadOnlyList<string> Predecessors,
    IReadOnlyList<int> CitationIds,
    bool IsAssumption);

public sealed record CelarAiSowDraft(
    string Title,
    string ExecutiveSummary,
    IReadOnlyList<string> Objectives,
    IReadOnlyList<string> InScope,
    IReadOnlyList<string> OutOfScope,
    IReadOnlyList<string> Deliverables,
    IReadOnlyList<string> CustomerResponsibilities,
    IReadOnlyList<string> UsSignalResponsibilities,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> TimelineAndMilestones,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<int> CitationIds,
    bool ReviewRequired,
    bool ContractuallyBinding,
    IReadOnlyList<CelarAiSowWorkPackage>? WorkPackages = null);

public sealed record CelarAiExternalReasoningRequest(
    string Mode,
    string PurposeCategory,
    bool ContainsPrivateDocumentText,
    bool ContainsFinancialValues,
    bool ContainsPeopleRecords,
    bool AcknowledgeSanitizedExternalUse,
    string? CapabilityCode = null);

public sealed record CelarAiExternalReasoningResult(
    string Status,
    bool Enabled,
    bool Authorized,
    bool ProviderCalled,
    string Provider,
    string Content,
    string Warning,
    IReadOnlyList<PulseAiRedactionEvidence> Redactions,
    IReadOnlyList<string> RemovedCategories,
    IReadOnlyList<string> BlockedReasons,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string>? AttemptedTargets = null,
    IReadOnlyList<string>? SkippedTargets = null,
    IReadOnlyList<ProjectPulseAiTargetDecision>? TargetDecisions = null);

public sealed record CelarAiComposeResult(
    string Status,
    string Mode,
    string PrimaryExecutionPath,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    PulseAiPrivateDetailedAnswer? DetailedAnswer,
    PulseAiPrivateFlowHivePlan? FlowHivePlan,
    CelarAiSowDraft? SowDraft,
    IReadOnlyList<CelarAiTimelineItem> Timeline,
    CelarAiGeneratedDiagram? Diagram,
    IReadOnlyList<PulseAiPrivateAnswerCitation> Citations,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> Conflicts,
    decimal CoverageScore,
    decimal Confidence,
    string ConfidenceExplanation,
    CelarAiExternalReasoningResult? ExternalAssistance,
    DateTimeOffset DataAsOf,
    string CorrelationId,
    string SelectedTarget = "",
    IReadOnlyList<string>? AttemptedTargets = null,
    IReadOnlyList<string>? SkippedTargets = null,
    IReadOnlyList<ProjectPulseAiTargetDecision>? TargetDecisions = null)
{
    public object ToPublicResponse() => new
    {
        status = Status,
        mode = Mode,
        primaryExecutionPath = PrimaryExecutionPath,
        project = new
        {
            projectId = ProjectId,
            projectCode = ProjectCode,
            projectName = ProjectName
        },
        detailedAnswer = DetailedAnswer,
        flowHivePlan = FlowHivePlan,
        sowDraft = SowDraft,
        timeline = Timeline,
        diagram = Diagram,
        citations = Citations,
        warnings = Warnings,
        missingEvidence = MissingEvidence,
        conflicts = Conflicts,
        coverageScore = CoverageScore,
        confidence = Confidence,
        confidenceExplanation = ConfidenceExplanation,
        externalAssistance = ExternalAssistance,
        selectedTarget = SelectedTarget,
        attemptedTargets = AttemptedTargets ?? [],
        skippedTargets = SkippedTargets ?? [],
        targetDecisions = TargetDecisions ?? [],
        dataAsOf = DataAsOf,
        correlationId = CorrelationId,
        privacy = new
        {
            rawDocumentTextReturned = false,
            embeddingVectorsReturned = false,
            modelSecretsReturned = false,
            unrestrictedSqlAllowed = false,
            externalProviderReceivesPrivateDocumentText = false,
            externalProviderReceivesCustomerIdentity = false,
            externalProviderReceivesFinancialValues = false
        },
        controls = new
        {
            timesheetSaved = false,
            timesheetSubmitted = false,
            sowPublished = false,
            projectPlanBaselined = false,
            engineersAssigned = false,
            customerDateCommitted = false,
            stateChanged = false
        }
    };
}
