namespace ProjectTime.Api.Modules;

/// <summary>
/// Internal reporting bridge for Group 5. The bridge is compiled as another
/// declaration of the Group 3 financial-truth module so reports, closeout,
/// recovery, and billing consume the same scoped calculations and source states.
/// It does not create a second project-financial calculation system.
/// </summary>
public static partial class ProjectFinancialTruthModule
{
    internal static async Task<FinancialOperationsTruthOutcome>
        BuildFinancialOperationsTruthAsync(HttpContext context)
    {
        var result = await BuildAsync(context, "rate-card");
        if (result.Failure is not null)
            return FinancialOperationsTruthOutcome.Fail(result.Failure);

        var data = result.Data!;
        var actor = new FinancialOperationsActor(
            data.Actor.ActualUserId,
            data.Actor.EffectiveUserId,
            data.Actor.Email,
            data.Actor.DisplayName,
            data.Actor.Roles.OrderBy(value => value).ToArray(),
            data.Actor.Permissions.OrderBy(value => value).ToArray(),
            data.Actor.IsViewAs,
            data.Actor.Broad,
            data.Actor.PmLead,
            data.Actor.Sales,
            data.Actor.RateAdmin);

        var projects = data.Projects.Select(project => new FinancialOperationsProject(
            project.ProjectId,
            project.ClientId,
            project.CustomerName,
            project.ProjectCode,
            project.ProjectName,
            project.ProjectStatus,
            project.StartDate,
            project.EndDate,
            project.Billable,
            project.ProjectManagerUserId,
            project.ProjectManagerName,
            project.ProjectManagerEmail,
            MapUser(project.ProjectTeamCoordinator),
            MapUser(project.SolutionArchitect),
            MapUser(project.AccountExecutive),
            project.Engineers.Select(engineer => new FinancialOperationsEngineer(
                engineer.UserId,
                engineer.DisplayName,
                engineer.Email,
                engineer.AssignedHours,
                engineer.UsedHours,
                engineer.Tasks)).ToArray(),
            project.ContractType,
            project.Visibility.Level,
            project.Visibility.FullAmounts,
            project.Visibility.Commercial,
            project.Visibility.RateContext,
            project.Visibility.Explanation,
            project.ContractedValue,
            project.LaborBudget,
            project.ExpenseBudget,
            project.PlannedHours,
            project.UsedHours,
            project.RemainingHours,
            project.LaborCost,
            project.UploadedExpenses,
            project.CommittedCost,
            project.ForecastedFinalCost,
            project.CurrentVariance,
            project.CompletionPercentage,
            project.BudgetStatus,
            project.VarianceCompleteness,
            project.NotificationStatus,
            project.OpenAlertCount,
            project.HighAlertCount,
            project.Sell.ConnectionOwner,
            project.Sell.CommercialSource,
            project.Sell.ReadinessStatus,
            project.Sell.SellQuoteNumber,
            project.Sell.BillingMethod,
            project.Sell.ConnectorReady,
            project.Sell.LastSuccessfulSyncAt,
            project.Expenses.Select(expense => new FinancialOperationsExpense(
                expense.UploadId,
                expense.OwnerUserId,
                expense.OwnerName,
                expense.SourceMode,
                expense.SourceFormat,
                expense.OriginalFileName,
                expense.PeriodStart,
                expense.PeriodEnd,
                expense.Currency,
                expense.LineCount,
                expense.TotalAmount,
                expense.ReimbursableAmount,
                expense.BillingTreatment,
                expense.UploadedAt,
                expense.NotificationStatus)).ToArray(),
            project.Missing,
            project.CalculatedAt)).ToArray();

        var sources = data.Sources.Select(source => new FinancialOperationsSourceState(
            source.Key,
            source.Name,
            source.Status,
            source.Required,
            source.Message,
            source.DiagnosticCode,
            source.RecordCount,
            source.ObservedAt,
            $"/api/financial-operations/sources/{Uri.EscapeDataString(source.Key)}/retry"))
            .ToArray();

        return FinancialOperationsTruthOutcome.Success(new FinancialOperationsTruthSnapshot(
            actor,
            projects,
            sources,
            data.GeneratedAt));
    }

    internal static string FinancialOperationsConnectionString() => ConnectionString();

    private static FinancialOperationsPerson? MapUser(User? user) => user is null
        ? null
        : new FinancialOperationsPerson(
            user.UserId,
            user.DisplayName,
            user.Email,
            user.JobTitle);
}

internal sealed record FinancialOperationsTruthOutcome(
    FinancialOperationsTruthSnapshot? Snapshot,
    IResult? Failure)
{
    internal static FinancialOperationsTruthOutcome Success(
        FinancialOperationsTruthSnapshot snapshot) => new(snapshot, null);

    internal static FinancialOperationsTruthOutcome Fail(IResult failure) =>
        new(null, failure);
}

internal sealed record FinancialOperationsTruthSnapshot(
    FinancialOperationsActor Actor,
    FinancialOperationsProject[] Projects,
    FinancialOperationsSourceState[] Sources,
    DateTimeOffset GeneratedAt);

internal sealed record FinancialOperationsActor(
    Guid ActualUserId,
    Guid EffectiveUserId,
    string Email,
    string DisplayName,
    string[] Roles,
    string[] Permissions,
    bool IsViewAs,
    bool Broad,
    bool PmLead,
    bool Sales,
    bool RateAdmin)
{
    internal bool HasPermission(params string[] values) =>
        Permissions.Any(permission => values.Contains(
            permission,
            StringComparer.OrdinalIgnoreCase));

    internal bool HasRole(params string[] values) =>
        Roles.Any(role => values.Contains(role, StringComparer.OrdinalIgnoreCase));
}

internal sealed record FinancialOperationsPerson(
    Guid UserId,
    string DisplayName,
    string Email,
    string JobTitle);

internal sealed record FinancialOperationsEngineer(
    Guid UserId,
    string DisplayName,
    string Email,
    decimal AssignedHours,
    decimal UsedHours,
    string[] Tasks);

internal sealed record FinancialOperationsExpense(
    Guid UploadId,
    Guid OwnerUserId,
    string OwnerName,
    string SourceMode,
    string SourceFormat,
    string? OriginalFileName,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string Currency,
    int LineCount,
    decimal? TotalAmount,
    decimal? ReimbursableAmount,
    string BillingTreatment,
    DateTimeOffset UploadedAt,
    string NotificationStatus);

internal sealed record FinancialOperationsProject(
    Guid ProjectId,
    Guid? ClientId,
    string CustomerName,
    string ProjectCode,
    string ProjectName,
    string ProjectStatus,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool Billable,
    Guid? ProjectManagerUserId,
    string ProjectManagerName,
    string ProjectManagerEmail,
    FinancialOperationsPerson? ProjectTeamCoordinator,
    FinancialOperationsPerson? SolutionArchitect,
    FinancialOperationsPerson? AccountExecutive,
    FinancialOperationsEngineer[] Engineers,
    string ContractType,
    string VisibilityLevel,
    bool FullAmounts,
    bool CommercialAmounts,
    bool RateContext,
    string VisibilityExplanation,
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
    string VarianceCompleteness,
    string NotificationStatus,
    int OpenAlertCount,
    int HighAlertCount,
    string SellConnectionOwner,
    string CommercialSource,
    string SellReadinessStatus,
    string SellQuoteNumber,
    string BillingMethod,
    bool SellConnectorReady,
    DateTimeOffset? LastSuccessfulSellSyncAt,
    FinancialOperationsExpense[] Expenses,
    string[] Missing,
    DateTimeOffset CalculatedAt);

internal sealed record FinancialOperationsSourceState(
    string Key,
    string Name,
    string Status,
    bool Required,
    string Message,
    string DiagnosticCode,
    int RecordCount,
    DateTimeOffset ObservedAt,
    string RetryEndpoint);
