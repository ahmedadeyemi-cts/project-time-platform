namespace ProjectTime.Api.Modules;

internal sealed record AnalyticsReportRequest(
    string? ReportCode,
    string? Search,
    Guid? CustomerId,
    Guid? ProjectId,
    Guid? ProjectManagerUserId,
    Guid? EngineerUserId,
    Guid? TeamId,
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
    int? Limit);

internal sealed record AnalyticsCustomerOption(
    Guid CustomerId,
    string CustomerName,
    string CustomerCode);

internal sealed record AnalyticsTeamOption(
    Guid TeamId,
    string TeamName,
    Guid[] MemberUserIds);

internal sealed record AnalyticsDirectorySnapshot(
    AnalyticsCustomerOption[] Customers,
    AnalyticsTeamOption[] Teams,
    EnterpriseReportSourceState Source)
{
    internal static AnalyticsDirectorySnapshot Fallback(
        FinancialOperationsProject[] projects,
        string diagnosticCode,
        string message)
    {
        var customers = projects
            .Where(project => project.ClientId.HasValue && !string.IsNullOrWhiteSpace(project.CustomerName))
            .GroupBy(project => project.ClientId!.Value)
            .Select(group => new AnalyticsCustomerOption(
                group.Key,
                group.First().CustomerName,
                string.Empty))
            .OrderBy(customer => customer.CustomerName)
            .ToArray();

        return new AnalyticsDirectorySnapshot(
            customers,
            Array.Empty<AnalyticsTeamOption>(),
            new EnterpriseReportSourceState(
                "analytics_directory",
                "Customer, project, people, and team directory",
                "partial",
                false,
                customers.Length,
                message,
                diagnosticCode,
                DateTimeOffset.UtcNow));
    }
}

internal sealed record AnalyticsBuildContext(
    EnterpriseReportingContext Reporting,
    AnalyticsDirectorySnapshot Directory)
{
    internal FinancialOperationsActor Actor => Reporting.Actor;
    internal FinancialOperationsProject[] Projects => Reporting.Projects;
}
