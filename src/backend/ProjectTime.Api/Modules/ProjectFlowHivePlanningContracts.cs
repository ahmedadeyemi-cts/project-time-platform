namespace ProjectTime.Api.Modules;

/// <summary>
/// Module-owned, transport-safe planning contracts for Project FlowHive.
/// These contracts do not imply that plan persistence is enabled.
/// </summary>
public sealed record ProjectFlowHivePlanRequest(
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string? CustomerName,
    string? PlanName,
    string? RevisionLabel,
    DateOnly? ProjectStartDate,
    DateOnly? ProjectEndDate,
    IReadOnlyList<ProjectFlowHivePlanTaskInput>? Tasks,
    IReadOnlyList<ProjectFlowHiveDependencyInput>? Dependencies,
    IReadOnlyList<ProjectFlowHivePlanAssignmentInput>? Assignments,
    string? GsdVersion,
    string? SowVersion,
    string? Notes,
    Guid? PlanId = null,
    string? SourceKind = "manual",
    string? CelarAiProviderCode = null,
    string? CelarAiCorrelationId = null,
    decimal? CelarAiConfidence = null);

public sealed record ProjectFlowHivePlanTaskInput(
    Guid? ClientTaskId,
    Guid? CanonicalTaskId,
    string? WbsNumber,
    string? ParentWbsNumber,
    string? Name,
    string? Description,
    int DurationWorkingDays,
    bool IsMilestone,
    string? ConstraintType,
    DateOnly? ConstraintDate,
    decimal PercentComplete,
    decimal RemainingEffortHours,
    string? Status,
    bool IsSummary = false,
    string? Phase = null,
    IReadOnlyList<string>? DetailedSteps = null,
    IReadOnlyList<string>? Inputs = null,
    IReadOnlyList<string>? Outputs = null,
    IReadOnlyList<string>? AcceptanceCriteria = null,
    IReadOnlyList<string>? ValidationSteps = null,
    IReadOnlyList<string>? CustomerResponsibilities = null,
    IReadOnlyList<string>? UsSignalResponsibilities = null,
    IReadOnlyList<string>? Prerequisites = null,
    IReadOnlyList<string>? Risks = null,
    IReadOnlyList<string>? OpenQuestions = null,
    string? Priority = "normal",
    IReadOnlyList<int>? CitationIds = null);

public sealed record ProjectFlowHiveDependencyInput(
    string? PredecessorWbs,
    string? SuccessorWbs,
    string? Type,
    int LagWorkingDays);

public sealed record ProjectFlowHivePlanAssignmentInput(
    string? TaskWbs,
    Guid? ResourceUserId,
    string? ResourceDisplayName,
    decimal AllocationPercent,
    decimal PlannedHours);

public sealed record ProjectFlowHiveValidationIssue(
    string Code,
    string Severity,
    string Path,
    string Message);

public sealed record ProjectFlowHivePlanValidationResult(
    bool Valid,
    IReadOnlyList<ProjectFlowHiveValidationIssue> Issues,
    int TaskCount,
    int DependencyCount,
    int AssignmentCount,
    decimal PlannedHours,
    string ContractVersion);

public sealed record ProjectFlowHiveScheduledTask(
    string WbsNumber,
    string? ParentWbsNumber,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    int DurationWorkingDays,
    int EarliestStartIndex,
    int LatestStartIndex,
    int TotalFloatWorkingDays,
    int FreeFloatWorkingDays,
    bool IsCritical,
    bool IsMilestone,
    decimal PercentComplete,
    decimal RemainingEffortHours,
    string Status,
    bool IsSummary = false,
    string Phase = "");

public sealed record ProjectFlowHiveScheduleResult(
    bool Valid,
    string Status,
    DateOnly? ProjectStartDate,
    DateOnly? ProjectTargetEndDate,
    DateOnly? ProjectFinishDate,
    int ScheduledWorkingDays,
    int CriticalTaskCount,
    decimal PlannedHours,
    IReadOnlyList<ProjectFlowHiveScheduledTask> Tasks,
    IReadOnlyList<ProjectFlowHiveValidationIssue> Issues,
    string CalendarMode,
    string ContractVersion);

public sealed record ProjectFlowHiveAiDraftPreviewRequest(
    ProjectFlowHivePlanRequest? Plan,
    string? GsdExcerpt,
    string? SowExcerpt,
    string? RequestedOutcome);

public sealed record ProjectFlowHiveArtifactRequest(
    ProjectFlowHivePlanRequest? Plan,
    string? ArtifactTitle,
    string? Audience,
    bool ExcludeNotes,
    bool AcknowledgeInternalDraft);

public sealed record ProjectFlowHiveBaselineRequest(
    string? ApprovalNote,
    int? ExpectedVersion);

public sealed record ProjectFlowHiveRepositoryReadiness(
    bool Ready,
    string Status,
    IReadOnlyList<string> Missing,
    DateTimeOffset CheckedAt);

public sealed record ProjectFlowHivePersistedPlanSummary(
    Guid PlanId,
    Guid ProjectId,
    string PlanName,
    string PlanStatus,
    int CurrentVersion,
    int? BaselineVersion,
    string ProjectCode,
    string ProjectName,
    string UpdatedBy,
    DateTimeOffset UpdatedAt);

public sealed record ProjectFlowHivePersistedPlan(
    ProjectFlowHivePersistedPlanSummary Summary,
    ProjectFlowHivePlanRequest Plan,
    ProjectFlowHiveScheduleResult Schedule,
    ProjectFlowHivePlanValidationResult Validation,
    string SourceKind,
    string CelarAiProviderCode,
    string CelarAiCorrelationId,
    decimal? CelarAiConfidence,
    DateTimeOffset VersionCreatedAt);

public interface IProjectFlowHivePlanRepository
{
    bool WritesEnabled { get; }

    Task<ProjectFlowHiveRepositoryReadiness> GetReadinessAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectFlowHivePersistedPlanSummary>> ListAsync(
        Guid actorUserId,
        Guid? projectId,
        CancellationToken cancellationToken);

    Task<ProjectFlowHivePersistedPlan?> LoadAsync(
        Guid actorUserId,
        Guid planId,
        CancellationToken cancellationToken);

    Task<ProjectFlowHivePersistenceResult> SaveDraftAsync(
        Guid actorUserId,
        ProjectFlowHivePlanRequest request,
        CancellationToken cancellationToken);

    Task<ProjectFlowHivePersistenceResult> EstablishBaselineAsync(
        Guid actorUserId,
        Guid planId,
        string? approvalNote,
        int? expectedVersion,
        CancellationToken cancellationToken);
}

public sealed record ProjectFlowHivePersistenceResult(
    bool Succeeded,
    string Status,
    Guid? PlanId,
    int? Version,
    string Message);

/// <summary>
/// The only repository available in the source-only package. It makes an
/// accidental planning write impossible until an approved persistence adapter
/// is registered during a separately authorized database phase.
/// </summary>
public sealed class LockedProjectFlowHivePlanRepository : IProjectFlowHivePlanRepository
{
    public bool WritesEnabled => false;

    public Task<ProjectFlowHiveRepositoryReadiness> GetReadinessAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(new ProjectFlowHiveRepositoryReadiness(
            false,
            "persistence_locked",
            ["Migration 074 and the production repository are required."],
            DateTimeOffset.UtcNow));

    public Task<IReadOnlyList<ProjectFlowHivePersistedPlanSummary>> ListAsync(
        Guid actorUserId,
        Guid? projectId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProjectFlowHivePersistedPlanSummary>>([]);

    public Task<ProjectFlowHivePersistedPlan?> LoadAsync(
        Guid actorUserId,
        Guid planId,
        CancellationToken cancellationToken) =>
        Task.FromResult<ProjectFlowHivePersistedPlan?>(null);

    public Task<ProjectFlowHivePersistenceResult> SaveDraftAsync(
        Guid actorUserId,
        ProjectFlowHivePlanRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Locked());
    }

    public Task<ProjectFlowHivePersistenceResult> EstablishBaselineAsync(
        Guid actorUserId,
        Guid planId,
        string? approvalNote,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Locked());
    }

    private static ProjectFlowHivePersistenceResult Locked()
    {
        return new ProjectFlowHivePersistenceResult(
            false,
            "persistence_locked",
            null,
            null,
            "Project FlowHive persistence is not authorized or registered.");
    }
}
