namespace ProjectTime.Api.Modules;

public static class ProjectForgePolicy
{
    public const string ModuleCode = "033";
    public const string CapabilityCode = "project_forge_plan_estimate";
    public const string ReviewAssignedPolicy = "PROJECT_FORGE_REVIEW_ASSIGNED";
    public const string TaskAssignedPolicy = "PROJECT_FORGE_TASK_ASSIGNED";
    public const string TaskUpdatedPolicy = "PROJECT_FORGE_TASK_UPDATED";
    public const string PlanUpdatedPolicy = "PROJECT_FORGE_PLAN_UPDATED";

    public static readonly string[] WorkbookTabs =
    [
        "INSTRUCTIONS",
        "SETUP",
        "OVERALL DASHBOARD",
        "MONTHLY CALENDAR",
        "WEEKLY CALENDAR",
        "PROJECT OVERVIEW",
        "PROJECT MANAGER",
        "PROJECT BUDGET",
        "VARIABLE TASKS",
        "RECURRING TASKS",
        "TASKS SCHEDULE",
        "TASKS FILTER",
        "DECISION MATRIX",
        "KANBAN BOARD",
        "GANTT CHART"
    ];
}

public sealed record ProjectForgePlanSaveRequest(
    Guid ProjectId,
    string? PlanName,
    string? Objective,
    DateOnly? StartDate,
    IReadOnlyList<ProjectForgePlanTaskRequest>? Tasks,
    IReadOnlyList<ProjectForgeDependencyRequest>? Dependencies,
    string? ReviewNote,
    int? ExpectedRevision = null,
    string? ClientMutationId = null);

public sealed record ProjectForgePlanTaskRequest(
    Guid? PlanTaskId,
    string? Wbs,
    string? ParentWbs,
    string? Name,
    string? Description,
    string? TaskType,
    string? Phase,
    string? Priority,
    string? Status,
    string? KanbanCategory,
    string? DecisionAction,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int DurationWorkingDays,
    decimal EstimatedHours,
    decimal HourlyRate,
    decimal MaterialUnits,
    decimal MaterialUnitCost,
    decimal FixedCost,
    decimal TravelCost,
    decimal EquipmentCost,
    decimal MiscCost,
    decimal PercentComplete,
    bool Important,
    bool Urgent,
    string? RecurrenceRule,
    Guid? ReviewerUserId);

public sealed record ProjectForgeDependencyRequest(
    Guid? DependencyId,
    string? PredecessorWbs,
    string? SuccessorWbs,
    string? DependencyType,
    int LagWorkingDays);

public sealed record ProjectForgeAiDraftRequest(
    string? RequestedOutcome,
    DateOnly? StartDate,
    string? DetailLevel,
    bool AllowSanitizedExternalFallback);

public sealed record ProjectForgeAssignReviewerRequest(
    Guid ReviewerUserId,
    IReadOnlyList<Guid>? PlanTaskIds,
    string? ReviewNote,
    int? ExpectedPlanRevision = null,
    IReadOnlyDictionary<Guid, int>? ExpectedTaskRevisions = null,
    string? ClientMutationId = null);

public sealed record ProjectForgeEstimatePatchRequest(
    decimal EstimatedHours,
    decimal HourlyRate,
    decimal MaterialUnits,
    decimal MaterialUnitCost,
    decimal FixedCost,
    decimal TravelCost,
    decimal EquipmentCost,
    decimal MiscCost,
    DateOnly? StartDate,
    DateOnly? DueDate,
    string? ReviewNote,
    int? ExpectedVersion,
    string? ClientMutationId = null);

public sealed record ProjectForgeAdoptPlanRequest(
    string? Confirmation,
    bool CreateAssignments,
    string? AdoptionNote,
    int? ExpectedPlanRevision = null,
    string? ClientMutationId = null);

public sealed record ProjectForgeTaskMutationContext(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId);

public sealed record ProjectForgeTaskDetailsPatchRequest(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId,
    string? TaskName,
    string? Description,
    string? TaskType,
    string? Phase,
    string? Priority,
    int? DurationWorkingDays,
    Guid? ParentTaskId,
    decimal? EstimatedHours,
    decimal? HourlyRate,
    decimal? MaterialUnits,
    decimal? MaterialUnitCost,
    decimal? FixedCost,
    decimal? TravelCost,
    decimal? EquipmentCost,
    decimal? MiscCost,
    string? RecurrenceRule,
    bool ClearParentTask,
    bool ClearRecurrenceRule);

public sealed record ProjectForgeTaskWorkflowPatchRequest(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId,
    string? Status,
    string? KanbanCategory,
    decimal? PercentComplete,
    string? BlockedReason,
    Guid? BeforeTaskId,
    Guid? AfterTaskId);

public sealed record ProjectForgeTaskSchedulePatchRequest(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId,
    string? Interaction,
    DateOnly? StartDate,
    DateOnly? DueDate,
    bool CascadeSuccessors);

public sealed record ProjectForgeTaskDecisionPatchRequest(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId,
    string? DecisionAction,
    bool Important,
    bool Urgent);

public sealed record ProjectForgeTaskCompositeDetailsPatch(
    string? TaskName,
    string? Description,
    string? TaskType,
    string? Phase,
    string? Priority,
    int? DurationWorkingDays,
    Guid? ParentTaskId,
    decimal? EstimatedHours,
    decimal? HourlyRate,
    decimal? MaterialUnits,
    decimal? MaterialUnitCost,
    decimal? FixedCost,
    decimal? TravelCost,
    decimal? EquipmentCost,
    decimal? MiscCost,
    string? RecurrenceRule,
    bool ClearParentTask,
    bool ClearRecurrenceRule);

public sealed record ProjectForgeTaskCompositeWorkflowPatch(
    string? Status,
    string? KanbanCategory,
    decimal? PercentComplete,
    string? BlockedReason,
    Guid? BeforeTaskId,
    Guid? AfterTaskId);

public sealed record ProjectForgeTaskCompositeSchedulePatch(
    string? Interaction,
    DateOnly? StartDate,
    DateOnly? DueDate,
    bool CascadeSuccessors);

public sealed record ProjectForgeTaskCompositeDecisionPatch(
    string? DecisionAction,
    bool Important,
    bool Urgent);

public sealed record ProjectForgeTaskCompositePatchRequest(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId,
    ProjectForgeTaskCompositeDetailsPatch? Details,
    ProjectForgeTaskCompositeWorkflowPatch? Workflow,
    ProjectForgeTaskCompositeSchedulePatch? Schedule,
    ProjectForgeTaskCompositeDecisionPatch? Decision);

public sealed record ProjectForgeTaskAssigneePutRequest(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId,
    Guid UserId,
    decimal AssignedHours,
    decimal AllocationPercent,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record ProjectForgeTaskCreateRequest(
    string? ClientMutationId,
    string? TaskCode,
    string? TaskName,
    string? Description,
    string? TaskType,
    string? Phase,
    string? Priority,
    string? Status,
    string? KanbanCategory,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int DurationWorkingDays,
    decimal EstimatedHours,
    decimal PercentComplete,
    string? BlockedReason,
    bool Billable,
    Guid? AssigneeUserId,
    Guid? ParentTaskId,
    decimal HourlyRate,
    decimal MaterialUnits,
    decimal MaterialUnitCost,
    decimal FixedCost,
    decimal TravelCost,
    decimal EquipmentCost,
    decimal MiscCost,
    string? RecurrenceRule,
    string? DecisionAction,
    bool Important,
    bool Urgent);

public sealed record ProjectForgeTaskArchiveRequest(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId,
    string? Reason);

public sealed record ProjectForgeTaskDependencySaveRequest(
    string? RecordSource,
    Guid? PlanId,
    int? ExpectedRevision,
    string? ClientMutationId,
    Guid PredecessorTaskId,
    Guid SuccessorTaskId,
    string? DependencyType,
    int LagWorkingDays);

public sealed record ProjectForgeReviewCompletionRequest(
    int? ExpectedRevision,
    string? Decision,
    string? ReviewNote,
    string? ClientMutationId);
