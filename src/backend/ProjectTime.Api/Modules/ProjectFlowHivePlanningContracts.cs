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
    IReadOnlyList<ProjectFlowHivePlanTaskInput>? Tasks,
    IReadOnlyList<ProjectFlowHiveDependencyInput>? Dependencies,
    IReadOnlyList<ProjectFlowHivePlanAssignmentInput>? Assignments,
    string? GsdVersion,
    string? SowVersion,
    string? Notes);

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
    string? Status);

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
    string Status);

public sealed record ProjectFlowHiveScheduleResult(
    bool Valid,
    string Status,
    DateOnly? ProjectStartDate,
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

public interface IProjectFlowHivePlanRepository
{
    bool WritesEnabled { get; }

    Task<ProjectFlowHivePersistenceResult> SaveDraftAsync(
        Guid actorUserId,
        ProjectFlowHivePlanRequest request,
        CancellationToken cancellationToken);

    Task<ProjectFlowHivePersistenceResult> EstablishBaselineAsync(
        Guid actorUserId,
        Guid planId,
        string? approvalNote,
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
