using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal sealed record EnterpriseReportRequest(
    string? ReportCode,
    string? Search,
    Guid? ProjectId,
    Guid? CustomerId,
    string? Customer,
    Guid? ProjectManagerUserId,
    Guid? EngineerUserId,
    string? ProjectStatus,
    string? BudgetStatus,
    string? ContractType,
    bool? Billable,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? WorkflowStatus,
    string? Severity,
    string? ModuleCode,
    string? SourceStatus,
    int? Limit,
    bool? IncludeInactive);

internal sealed record EnterpriseSavedViewRequest(
    Guid? SavedViewId,
    string? Name,
    string? ReportCode,
    EnterpriseReportRequest? Filters,
    bool? IsDefault);

internal sealed record EnterpriseReportFilterDefinition(
    string Key,
    string Label,
    string Type,
    bool Required,
    bool Locked,
    string? LockedReason,
    string? Placeholder,
    string? OptionSource,
    object? DefaultValue);

internal sealed record EnterpriseReportColumnDefinition(
    string Key,
    string Label,
    string DataType,
    string Description,
    bool Sensitive = false);

internal sealed record EnterpriseReportDefinition(
    string Code,
    string Name,
    string Category,
    string Description,
    string[] Modules,
    string[] RequiredSources,
    string[] OptionalSources,
    string[] Audience,
    string ScopeRule,
    EnterpriseReportFilterDefinition[] Filters,
    EnterpriseReportColumnDefinition[] Columns);

internal sealed record EnterpriseReportOption(
    string Value,
    string Label,
    bool Locked = false,
    string? Detail = null);

internal sealed record EnterpriseReportFilterOptions(
    Dictionary<string, EnterpriseReportOption[]> Options,
    Dictionary<string, object?> LockedValues,
    string ScopeExplanation);

internal sealed record EnterpriseReportSourceState(
    string Key,
    string Name,
    string Status,
    bool Required,
    int RecordCount,
    string Message,
    string DiagnosticCode,
    DateTimeOffset ObservedAt);

internal sealed record EnterpriseReportResult(
    string ReportCode,
    string ReportName,
    string ResultStatus,
    string Message,
    Dictionary<string, object?> EffectiveFilters,
    EnterpriseReportColumnDefinition[] Columns,
    Dictionary<string, object?>[] Rows,
    EnterpriseReportSourceState[] Sources,
    DateTimeOffset GeneratedAt,
    object ScopeEvidence)
{
    internal int RowCount => Rows.Length;
}

internal sealed record EnterpriseReportRunRecord(
    Guid RunId,
    string ReportCode,
    string ReportName,
    string ResultStatus,
    int RowCount,
    Guid? ActualUserId,
    Guid? EffectiveUserId,
    JsonElement ScopeSnapshot,
    JsonElement Filters,
    JsonElement Columns,
    JsonElement Sources,
    JsonElement Results,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    DateTimeOffset CreatedAt);

internal sealed record EnterpriseSavedViewRecord(
    Guid SavedViewId,
    string Name,
    string ReportCode,
    Guid OwnerUserId,
    JsonElement Filters,
    bool IsDefault,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record EnterpriseReportingContext(
    FinancialOperationsTruthSnapshot Truth,
    EnterpriseReportingSupplemental Supplemental)
{
    internal FinancialOperationsActor Actor => Truth.Actor;
    internal FinancialOperationsProject[] Projects => Truth.Projects;

    internal EnterpriseReportSourceState[] Sources => Truth.Sources
        .Select(source => new EnterpriseReportSourceState(
            source.Key,
            source.Name,
            source.Status,
            source.Required,
            source.RecordCount,
            source.Message,
            source.DiagnosticCode,
            source.ObservedAt))
        .Concat(Supplemental.Sources)
        .GroupBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last())
        .OrderBy(source => source.Required ? 0 : 1)
        .ThenBy(source => source.Name)
        .ToArray();
}

internal sealed record EnterpriseReportingSupplemental(
    IReadOnlyDictionary<string, JsonElement[]> Data,
    EnterpriseReportSourceState[] Sources)
{
    internal JsonElement[] Rows(string key) =>
        Data.TryGetValue(key, out var rows) ? rows : Array.Empty<JsonElement>();
}
