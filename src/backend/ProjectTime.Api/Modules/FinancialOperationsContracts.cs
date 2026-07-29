using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal sealed record FinancialReportRequest(
    string? ReportCode,
    string? Search,
    Guid? ProjectId,
    string? Customer,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Status,
    int? Limit);

internal sealed record FinancialWorkItemActionRequest(string? Note);

internal sealed record FinancialReportColumn(
    string Key,
    string Label,
    string DataType,
    string Description);

internal sealed record FinancialReportDefinition(
    string Code,
    string Name,
    string Description,
    string[] Modules,
    string[] RequiredSources,
    string[] OptionalSources,
    FinancialReportColumn[] Columns);

internal sealed record FinancialReportResult(
    string ReportCode,
    string ReportName,
    string ResultStatus,
    string Message,
    Dictionary<string, object?> Filters,
    Dictionary<string, object?>[] Rows,
    FinancialOperationsSourceState[] Sources,
    DateTimeOffset GeneratedAt)
{
    internal int RowCount => Rows.Length;
}

internal sealed record FinancialApprovedTime(
    Guid ProjectId,
    decimal ApprovedHours,
    int ApprovedLineCount,
    DateOnly? FirstWorkDate,
    DateOnly? LastWorkDate);

internal sealed record FinancialBillingReadiness(
    Guid ReviewId,
    Guid ProjectId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string PackageType,
    string ReviewStatus,
    string EvidenceSourceType,
    string EvidenceDescription,
    decimal? EvidenceAmount,
    Guid? ReviewedByUserId,
    DateTimeOffset UpdatedAt);

internal sealed record FinancialCloseoutState(
    Guid ProjectId,
    string CloseoutStatus,
    string PriorProjectStatus,
    string BillingDisposition,
    string Reason,
    Guid? RequestedByUserId,
    Guid? CompletedByUserId,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt);

internal sealed record FinancialNotificationState(
    Guid DispatchId,
    Guid? ProjectId,
    string NotificationType,
    string Severity,
    string SourceModule,
    string SourceStatus,
    string DeliveryBoundary,
    string DeliveryStatus,
    int RecipientCount,
    string LastErrorCode,
    string LastErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

internal sealed record FinancialSupplementalData(
    IReadOnlyDictionary<Guid, FinancialApprovedTime> ApprovedTime,
    IReadOnlyDictionary<Guid, FinancialBillingReadiness> BillingReadiness,
    IReadOnlyDictionary<Guid, FinancialCloseoutState> Closeout,
    FinancialNotificationState[] Notifications,
    FinancialOperationsSourceState[] Sources);

internal sealed record FinancialOperationsContext(
    FinancialOperationsTruthSnapshot Truth,
    FinancialSupplementalData Supplemental)
{
    internal FinancialOperationsSourceState[] AllSources => Truth.Sources
        .Concat(Supplemental.Sources)
        .GroupBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last())
        .OrderBy(source => source.Required ? 0 : 1)
        .ThenBy(source => source.Name)
        .ToArray();
}

internal sealed record FinancialReportRunRow(
    Guid RunId,
    string ReportCode,
    string ReportName,
    string ResultStatus,
    int RowCount,
    Guid? ActualUserId,
    Guid? EffectiveUserId,
    JsonElement Filters,
    JsonElement Sources,
    JsonElement Results,
    string DiagnosticCode,
    string DiagnosticMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? LastExportedAt,
    DateTimeOffset CreatedAt);

internal sealed record FinancialOperationsWorkItem(
    Guid WorkItemId,
    string DeduplicationKey,
    Guid? ProjectId,
    string ModuleCode,
    string ItemType,
    string SourceKey,
    string Priority,
    string WorkStatus,
    string Title,
    string Detail,
    Guid? OwnerUserId,
    string OwnerName,
    string RetryEndpoint,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastDetectedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    string ResolutionNote,
    JsonElement Metadata);

internal sealed record FinancialOperationsAction(
    Guid ActionId,
    Guid? WorkItemId,
    Guid? ProjectId,
    string SourceKey,
    string ActionCode,
    string ActionStatus,
    Guid? ActorUserId,
    string DiagnosticCode,
    string DiagnosticMessage,
    string CorrelationId,
    JsonElement Metadata,
    DateTimeOffset CreatedAt);

internal sealed record FinancialOperationsDerivedItem(
    string DeduplicationKey,
    Guid? ProjectId,
    string ModuleCode,
    string ItemType,
    string SourceKey,
    string Priority,
    string Title,
    string Detail,
    Guid? OwnerUserId,
    string RetryEndpoint,
    object Metadata);
