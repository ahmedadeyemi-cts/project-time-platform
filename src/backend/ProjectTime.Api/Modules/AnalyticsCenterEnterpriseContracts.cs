using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal sealed record AnalyticsExperienceRequest(
    string? ReportCode,
    string? Search,
    Guid? CustomerId,
    Guid? ProjectId,
    Guid? ProjectManagerUserId,
    Guid? EngineerUserId,
    Guid? TeamId,
    Guid[]? CustomerIds,
    Guid[]? ProjectIds,
    Guid[]? ProjectManagerUserIds,
    Guid[]? EngineerUserIds,
    Guid[]? TeamIds,
    string? ProjectStatus,
    string? BudgetStatus,
    string? ContractType,
    string[]? ContractTypes,
    bool? Billable,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? WorkflowStatus,
    string? Severity,
    string? ModuleCode,
    string? SourceStatus,
    int? Limit)
{
    internal static AnalyticsExperienceRequest Empty(string reportCode) => new(
        reportCode,
        null,
        null,
        null,
        null,
        null,
        null,
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        Array.Empty<Guid>(),
        null,
        null,
        null,
        Array.Empty<string>(),
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        500);
}

internal sealed record AnalyticsScheduleRecipientRequest(
    Guid? UserId,
    string? DisplayName,
    string? Email,
    string? RecipientType);

internal sealed record AnalyticsScheduleUpsertRequest(
    Guid? ScheduleId,
    string? ScheduleName,
    string? ReportCode,
    AnalyticsExperienceRequest? Criteria,
    string? Cadence,
    int? DayOfWeek,
    int? DayOfMonth,
    int? MonthOfYear,
    TimeOnly? LocalTime,
    string? TimezoneName,
    string? ExportFormat,
    string? DeliveryBoundary,
    string? EmailSubject,
    string? EmailMessage,
    bool? Enabled,
    AnalyticsScheduleRecipientRequest[]? Recipients);

internal sealed record AnalyticsActivityRequest(bool? Favorite);

internal sealed record AnalyticsScheduleRecipient(
    Guid RecipientId,
    Guid ScheduleId,
    Guid? UserId,
    string DisplayName,
    string Email,
    string RecipientType);

internal sealed record AnalyticsSchedule(
    Guid ScheduleId,
    Guid OwnerActualUserId,
    Guid OwnerEffectiveUserId,
    string ScheduleName,
    string ReportCode,
    JsonElement Criteria,
    string Cadence,
    int? DayOfWeek,
    int? DayOfMonth,
    int? MonthOfYear,
    TimeOnly LocalTime,
    string TimezoneName,
    string ExportFormat,
    string DeliveryBoundary,
    string EmailSubject,
    string EmailMessage,
    bool Enabled,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastCompletedAt,
    string LastStatus,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AnalyticsScheduleRecipient[] Recipients);

internal sealed record AnalyticsScheduleRun(
    Guid ScheduleRunId,
    Guid? ScheduleId,
    string ScheduleName,
    string ReportCode,
    Guid? OwnerActualUserId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string RunStatus,
    int RecipientCount,
    int SentCount,
    int QueuedCount,
    int FailedCount,
    string DiagnosticCode,
    string DiagnosticMessage,
    DateTimeOffset CreatedAt);

internal sealed record AnalyticsScheduleDeliveryEvidence(
    Guid ScheduleDeliveryAttemptId,
    Guid ScheduleRunId,
    Guid? ReportRunId,
    Guid? RecipientUserId,
    string RecipientEmail,
    string ExportFormat,
    string ContentSha256,
    string DeliveryStatus,
    string ProviderSource,
    string ProviderMessageId,
    string DiagnosticCode,
    string DiagnosticMessage,
    DateTimeOffset CreatedAt);

internal sealed record AnalyticsRecipientOption(
    Guid? UserId,
    string DisplayName,
    string Email,
    string JobTitle,
    string Source,
    bool SelfOnly);

internal sealed record AnalyticsDashboardMetric(
    string Key,
    string Label,
    string Value,
    string Detail,
    string Tone,
    decimal? ProgressPercentage,
    bool Available);

internal sealed record AnalyticsRecentItem(
    string ReportCode,
    string ReportName,
    string Category,
    string Description,
    bool Favorite,
    int ViewCount,
    DateTimeOffset? LastViewedAt,
    Guid? LastRunId,
    int? LastRowCount,
    string LastResultStatus);

internal sealed record AnalyticsBrandedExport(
    byte[] Content,
    string ContentType,
    string FileName,
    string Format,
    string Sha256);

internal sealed record AnalyticsScheduledReportOutcome(
    FinancialOperationsActor Actor,
    EnterpriseReportingContext Reporting,
    EnterpriseReportDefinition Definition,
    EnterpriseReportResult Result,
    Guid ReportRunId);

internal sealed record Module065MailAttachment(
    string FileName,
    string ContentType,
    byte[] Content);
