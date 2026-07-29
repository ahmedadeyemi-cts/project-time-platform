namespace ProjectTime.Api.Modules;

internal static class FinancialOperationsWorkItemFactory
{
    internal static FinancialOperationsDerivedItem[] Build(
        FinancialOperationsContext context)
    {
        var items = new List<FinancialOperationsDerivedItem>();

        foreach (var source in context.AllSources.Where(source =>
                     source.Status is "unavailable" or "partial"))
        {
            items.Add(new FinancialOperationsDerivedItem(
                $"source:{source.Key}",
                null,
                ModuleForSource(source.Key),
                "source_failure",
                source.Key,
                source.Required ? "critical" : "high",
                $"{source.Name} requires attention",
                $"{source.Message} Diagnostic: {source.DiagnosticCode}.",
                context.Truth.Actor.ActualUserId,
                source.RetryEndpoint,
                new
                {
                    source.Status,
                    source.Required,
                    source.DiagnosticCode,
                    source.RecordCount,
                    source.ObservedAt
                }));
        }

        foreach (var project in context.Truth.Projects)
        {
            if (project.Missing.Length > 0)
            {
                items.Add(ProjectItem(
                    project,
                    "missing_financial_information",
                    "030",
                    project.BudgetStatus == "missing_financial_information" ? "high" : "medium",
                    "Project financial information is incomplete",
                    $"Missing: {string.Join(", ", project.Missing)}.",
                    "/api/financial-operations/sources/project_metadata/retry",
                    new { project.Missing, project.BudgetStatus }));
            }

            if (project.BudgetStatus is "over_budget" or "approaching_budget")
            {
                items.Add(ProjectItem(
                    project,
                    project.BudgetStatus,
                    "039",
                    project.BudgetStatus == "over_budget" ? "critical" : "high",
                    project.BudgetStatus == "over_budget"
                        ? "Project forecast is over the known budget"
                        : "Project forecast is approaching the known budget",
                    $"Forecast: {Money(project.ForecastedFinalCost)}; variance: {Money(project.CurrentVariance)}.",
                    "/api/financial-operations/sources/time_entries/retry",
                    new
                    {
                        project.ForecastedFinalCost,
                        project.CurrentVariance,
                        project.LaborBudget,
                        project.ExpenseBudget
                    }));
            }

            context.Supplemental.BillingReadiness.TryGetValue(
                project.ProjectId,
                out var billingReadiness);
            context.Supplemental.ApprovedTime.TryGetValue(
                project.ProjectId,
                out var approvedTime);
            if (billingReadiness is null
                || !billingReadiness.ReviewStatus.Equals(
                    "ready",
                    StringComparison.OrdinalIgnoreCase))
            {
                items.Add(ProjectItem(
                    project,
                    "billing_readiness",
                    "039",
                    project.Billable ? "high" : "medium",
                    "Billing package is not ready",
                    billingReadiness is null
                        ? "No governed billing-readiness review is recorded."
                        : $"Latest package is {billingReadiness.ReviewStatus} for {billingReadiness.PackageType}.",
                    "/api/financial-operations/sources/billing_readiness_reviews/retry",
                    new
                    {
                        reviewStatus = billingReadiness?.ReviewStatus ?? "not_recorded",
                        packageType = billingReadiness?.PackageType ?? "not_recorded",
                        approvedHours = approvedTime?.ApprovedHours
                    }));
            }

            context.Supplemental.Closeout.TryGetValue(
                project.ProjectId,
                out var closeout);
            if (project.ProjectStatus.Equals("completed", StringComparison.OrdinalIgnoreCase)
                && closeout?.CloseoutStatus != "closed")
            {
                items.Add(ProjectItem(
                    project,
                    "closeout_incomplete",
                    "040",
                    "high",
                    "Completed project is not governed-closed",
                    $"Project status is completed; closeout status is {closeout?.CloseoutStatus ?? "not_started"}.",
                    "/api/financial-operations/sources/project_closeout_records/retry",
                    new
                    {
                        project.ProjectStatus,
                        closeoutStatus = closeout?.CloseoutStatus ?? "not_started"
                    }));
            }

            var failedNotifications = context.Supplemental.Notifications
                .Where(notification => notification.ProjectId == project.ProjectId)
                .Where(notification => notification.DeliveryStatus is "failed" or "held")
                .ToArray();
            foreach (var notification in failedNotifications)
            {
                items.Add(ProjectItem(
                    project,
                    $"notification:{notification.DispatchId}",
                    "041",
                    notification.DeliveryStatus == "failed" ? "high" : "medium",
                    $"Closeout or cost notification is {notification.DeliveryStatus}",
                    string.IsNullOrWhiteSpace(notification.LastErrorMessage)
                        ? $"Delivery boundary: {notification.DeliveryBoundary}."
                        : notification.LastErrorMessage,
                    "/api/financial-operations/sources/project_notification_dispatches/retry",
                    new
                    {
                        notification.DispatchId,
                        notification.NotificationType,
                        notification.DeliveryStatus,
                        notification.DeliveryBoundary,
                        notification.LastErrorCode
                    }));
            }

            if (project.Billable
                && approvedTime is not null
                && approvedTime.ApprovedHours > 0
                && project.UploadedExpenses is null)
            {
                items.Add(ProjectItem(
                    project,
                    "billing_expense_source_unavailable",
                    "042",
                    "high",
                    "Approved time is available but current expense status is unavailable",
                    "Module 042 can continue to show approved time, but expense completeness must be verified before invoice preparation.",
                    "/api/financial-operations/sources/project_expenses/retry",
                    new { approvedTime.ApprovedHours }));
            }
        }

        return items
            .GroupBy(item => item.DeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    private static FinancialOperationsDerivedItem ProjectItem(
        FinancialOperationsProject project,
        string itemType,
        string module,
        string priority,
        string title,
        string detail,
        string retryEndpoint,
        object metadata) => new(
            $"project:{project.ProjectId}:{itemType}",
            project.ProjectId,
            module,
            itemType,
            SourceForModule(module),
            priority,
            title,
            $"{project.CustomerName} · {project.ProjectCode} · {project.ProjectName}. {detail}",
            project.ProjectManagerUserId ?? project.ProjectTeamCoordinator?.UserId,
            retryEndpoint,
            metadata);

    private static string SourceForModule(string module) => module switch
    {
        "039" => "billing_readiness_reviews",
        "040" => "project_closeout_records",
        "041" => "project_notification_dispatches",
        "042" => "approved_time_entries",
        _ => "project_metadata"
    };

    private static string ModuleForSource(string source) => source switch
    {
        "billing_readiness_reviews" => "039",
        "project_closeout_records" => "040",
        "project_notification_dispatches" => "041",
        "approved_time_entries" or "project_expenses" => "042",
        _ => "030"
    };

    private static string Money(decimal? value) => value.HasValue
        ? value.Value.ToString("C2")
        : "not available";
}
