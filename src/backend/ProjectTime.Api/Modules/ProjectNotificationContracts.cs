using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal sealed record ProjectNotificationActor(
    Guid ActualUserId,
    Guid EffectiveUserId,
    string Email,
    string DisplayName,
    HashSet<string> Roles,
    HashSet<string> Permissions,
    bool IsViewAs)
{
    internal bool IsAdministrator => Roles.Contains("SUPER_ADMINISTRATOR")
        || Roles.Contains("ADMINISTRATOR")
        || Permissions.Contains("SYSTEM_ADMINISTRATION")
        || Permissions.Contains("MANAGE_ALL");

    internal bool IsCoordinator => Roles.Contains("PROJECT_TEAM_COORDINATOR");

    internal bool CanViewRouting => IsAdministrator
        || IsCoordinator
        || Permissions.Contains("VIEW_COST_ALERT_ROUTING_RULES")
        || Permissions.Contains("VIEW_NOTIFICATION_SCHEDULES")
        || Permissions.Contains("VIEW_NOTIFICATION_DELIVERY_MONITOR")
        || Permissions.Contains("VIEW_CLOSEOUT_NOTIFICATION_ROUTING");

    internal bool CanManageRouting => !IsViewAs && (
        IsAdministrator
        || IsCoordinator
        || Permissions.Contains("MANAGE_COST_ALERT_ROUTING_RULES"));

    internal bool CanManageSchedules => !IsViewAs && (
        IsAdministrator
        || IsCoordinator
        || Permissions.Contains("MANAGE_NOTIFICATION_SCHEDULES"));

    internal bool CanDeliver => !IsViewAs && (
        IsAdministrator
        || IsCoordinator
        || Permissions.Contains("MANAGE_NOTIFICATION_DELIVERY")
        || Permissions.Contains("DELIVER_PROJECT_NOTIFICATIONS"));
}

internal sealed record ProjectCostRoutingRule(
    Guid RuleId,
    string RuleCode,
    string RuleName,
    string MetricCode,
    string ComparisonOperator,
    decimal? ThresholdValue,
    string ThresholdUnit,
    string AlertSeverity,
    string[] RecipientRoles,
    Guid? OptionalEscalationManagerUserId,
    int? EscalationAfterMinutes,
    string DeliveryBoundary,
    bool Enabled,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record ProjectNotificationSchedule(
    Guid ScheduleId,
    string ScheduleCode,
    string ScheduleName,
    string ScheduleType,
    int? DayOfWeek,
    TimeOnly LocalTime,
    string TimezoneName,
    int? DaysBeforeMonthEnd,
    int? EscalationAfterMinutes,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    bool Enabled,
    string DeliveryBoundary,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    string LastStatus,
    DateTimeOffset? NextRunAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record ProjectNotificationUser(
    Guid? UserId,
    string DisplayName,
    string Email,
    string Role,
    string DerivationSource,
    string RecipientType = "to");

internal sealed record ProjectNotificationEngineer(
    Guid UserId,
    string DisplayName,
    string Email,
    decimal AssignedHours,
    decimal UsedHours);

internal sealed record ProjectNotificationSourceState(
    string Key,
    string Name,
    string Status,
    bool Required,
    string Message,
    string DiagnosticCode,
    int RecordCount,
    DateTimeOffset ObservedAt)
{
    internal static ProjectNotificationSourceState Healthy(
        string key,
        string name,
        bool required,
        int count) => new(
            key,
            name,
            "healthy",
            required,
            "Source loaded.",
            string.Empty,
            count,
            DateTimeOffset.UtcNow);

    internal static ProjectNotificationSourceState Unavailable(
        string key,
        string name,
        bool required,
        string diagnosticCode,
        string message) => new(
            key,
            name,
            "unavailable",
            required,
            message,
            diagnosticCode,
            0,
            DateTimeOffset.UtcNow);
}

internal sealed record ProjectNotificationFinancialSnapshot(
    Guid ProjectId,
    string CustomerName,
    string ProjectCode,
    string ProjectName,
    string ProjectStatus,
    string ContractType,
    ProjectNotificationUser? ProjectManager,
    ProjectNotificationUser? ProjectTeamCoordinator,
    ProjectNotificationUser? SolutionArchitect,
    ProjectNotificationUser? AccountExecutive,
    ProjectNotificationEngineer[] Engineers,
    decimal? ContractedValue,
    decimal? LaborBudget,
    decimal? ExpenseBudget,
    decimal PlannedHours,
    decimal UsedHours,
    decimal RemainingHours,
    decimal? LaborCost,
    decimal? UploadedExpenses,
    decimal? CommittedCost,
    decimal? ForecastedFinalCost,
    decimal? CurrentVariance,
    decimal? CompletionPercentage,
    string BudgetStatus,
    string[] MissingFinancialInformation,
    DateTimeOffset CalculatedAt);

internal sealed record ProjectNotificationSnapshotResult(
    ProjectNotificationFinancialSnapshot[] Projects,
    ProjectNotificationSourceState[] Sources,
    DateTimeOffset GeneratedAt);

internal sealed record ProjectNotificationMetricEvaluation(
    bool Triggered,
    decimal? ObservedValue,
    decimal? ComparisonValue,
    string ObservedUnit,
    string Reason);

internal sealed record ProjectNotificationDispatchRow(
    Guid DispatchId,
    Guid? ProjectId,
    Guid? RoutingRuleId,
    Guid? ScheduleId,
    string EventKey,
    string NotificationType,
    string AlertSeverity,
    string SourceModule,
    string SourceStatus,
    string Subject,
    string TextBody,
    string HtmlBody,
    string DeliveryBoundary,
    string ProviderSource,
    string DeliveryStatus,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? ReleasedAt,
    Guid? ReleasedByUserId,
    DateTimeOffset? SentAt,
    string ProviderMessageId,
    string LastErrorCode,
    string LastErrorMessage,
    JsonElement Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ProjectNotificationUser[] Recipients,
    int AttemptCount);

internal sealed record Module065MailReadiness(
    string RuntimeEnvironment,
    string ConfiguredEnvironment,
    string ConfiguredProvider,
    string ActiveDeliveryProvider,
    string RecipientBoundary,
    string SenderMailbox,
    bool RuntimeReady,
    bool LiveDeliveryEnabled,
    bool GraphCredentialAvailable,
    bool SmtpCredentialAvailable,
    string DeliveryMode,
    string Message)
{
    internal static Module065MailReadiness Locked(string message) => new(
        "not_resolved",
        "not_configured",
        "locked",
        "locked",
        "locked",
        string.Empty,
        false,
        false,
        false,
        false,
        "outbox_only",
        message);
}

internal sealed record Module065MailDeliveryResult(
    bool Sent,
    string Status,
    string Provider,
    string RecipientBoundary,
    string ProviderMessageId,
    string DiagnosticCode,
    string Message);

internal sealed record ProjectNotificationEvaluationRequest(
    Guid? ProjectId,
    bool ReleaseEligible,
    string? EvaluationReason);

internal sealed record ProjectCostRoutingRuleUpdateRequest(
    string? RuleName,
    string? MetricCode,
    string? ComparisonOperator,
    decimal? ThresholdValue,
    string? ThresholdUnit,
    string? AlertSeverity,
    string[]? RecipientRoles,
    Guid? OptionalEscalationManagerUserId,
    int? EscalationAfterMinutes,
    string? DeliveryBoundary,
    bool? Enabled,
    string? Description,
    string? ChangeReason);

internal sealed record ProjectNotificationScheduleUpdateRequest(
    string? ScheduleName,
    string? ScheduleType,
    int? DayOfWeek,
    string? LocalTime,
    string? TimezoneName,
    int? DaysBeforeMonthEnd,
    int? EscalationAfterMinutes,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    bool? Enabled,
    string? DeliveryBoundary,
    string? ChangeReason);

internal sealed record ProjectCloseoutNotificationRequest(
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string? CustomerName,
    string? ProjectStatus,
    string? Subject,
    string? Body,
    string? TriggeredBy);

internal sealed record ProjectNotificationReleaseRequest(
    string? Reason,
    bool? ForceRetry);
