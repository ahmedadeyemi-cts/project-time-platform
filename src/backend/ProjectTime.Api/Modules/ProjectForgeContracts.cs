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
    string? ReviewNote);

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
    string? ReviewNote);

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
    int? ExpectedVersion);

public sealed record ProjectForgeAdoptPlanRequest(
    string? Confirmation,
    bool CreateAssignments,
    string? AdoptionNote);
