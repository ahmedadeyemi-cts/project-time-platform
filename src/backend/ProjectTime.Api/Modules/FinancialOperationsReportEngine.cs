namespace ProjectTime.Api.Modules;

internal static class FinancialOperationsReportEngine
{
    internal static readonly FinancialReportDefinition[] Catalog =
    [
        new(
            "project_financial_health",
            "Project Financial Health",
            "Project budget, cost, forecast, variance, completion, and governed SELL readiness.",
            ["030", "039", "042"],
            ["projects", "assignments", "time_entries"],
            ["project_expenses", "project_metadata", "sell_commercial_model", "cost_alerts"],
            [
                Column("customer", "Customer", "text", "Authoritative customer name."),
                Column("projectCode", "Project code", "text", "Authoritative ProjectPulse project code."),
                Column("projectName", "Project", "text", "Authoritative project name."),
                Column("projectManager", "Project Manager", "text", "Assigned Project Manager."),
                Column("contractType", "Contract type", "text", "Recorded commercial contract type."),
                Column("plannedHours", "Planned hours", "number", "Current project assignment plan."),
                Column("usedHours", "Used hours", "number", "Non-voided and non-declined project time."),
                Column("uploadedExpenses", "Uploaded expenses", "currency", "Current, non-deleted Module 005 expenses."),
                Column("forecastedFinalCost", "Forecasted final cost", "currency", "Group 3 governed forecast."),
                Column("currentVariance", "Current variance", "currency", "Known budget less governed forecast."),
                Column("budgetStatus", "Budget status", "status", "On-track, approaching, over, or missing-information state."),
                Column("completionPercentage", "Completion", "percent", "Used hours divided by planned hours."),
                Column("sellReadiness", "SELL readiness", "status", "Module 026 commercial readiness.")
            ]),
        new(
            "project_hours_consumption",
            "Project Hours Consumption",
            "Allocated, used, approved, and remaining hours by project.",
            ["030", "039", "040", "042"],
            ["projects", "assignments", "time_entries", "approved_time_entries"],
            [],
            [
                Column("customer", "Customer", "text", "Authoritative customer name."),
                Column("projectCode", "Project code", "text", "Project code."),
                Column("projectName", "Project", "text", "Project name."),
                Column("plannedHours", "Planned hours", "number", "Assigned project hours."),
                Column("usedHours", "Used hours", "number", "Current time consumption."),
                Column("approvedHours", "Approved hours", "number", "Invoice-eligible approved time states."),
                Column("remainingHours", "Remaining hours", "number", "Planned less used, floored at zero."),
                Column("completionPercentage", "Completion", "percent", "Used divided by planned hours."),
                Column("hoursStatus", "Hours status", "status", "Within plan, approaching, or exceeded.")
            ]),
        new(
            "project_expense_status",
            "Project Expense Status",
            "Actual current Module 005 expense uploads and billing treatment.",
            ["030", "039", "042"],
            ["projects", "project_expenses"],
            [],
            [
                Column("customer", "Customer", "text", "Authoritative customer name."),
                Column("projectCode", "Project code", "text", "Project code."),
                Column("projectName", "Project", "text", "Project name."),
                Column("owner", "Expense owner", "text", "Expense owner identity."),
                Column("periodStart", "Period start", "date", "Uploaded expense period start."),
                Column("periodEnd", "Period end", "date", "Uploaded expense period end."),
                Column("source", "Source", "text", "Excel/CSV or governed Certify import."),
                Column("amount", "Amount", "currency", "Role-visible current expense total."),
                Column("reimbursableAmount", "Reimbursable", "currency", "Role-visible reimbursable total."),
                Column("billingTreatment", "Billing treatment", "status", "Pass-through, fixed-fee included, or internal."),
                Column("uploadedAt", "Uploaded", "datetime", "Upload timestamp.")
            ]),
        new(
            "billing_readiness",
            "Billing Readiness",
            "Approved time, current expenses, package review, blockers, and forecast context.",
            ["030", "031", "039", "042"],
            ["projects", "approved_time_entries"],
            ["project_expenses", "billing_readiness_reviews", "sell_commercial_model"],
            [
                Column("customer", "Customer", "text", "Authoritative customer name."),
                Column("projectCode", "Project code", "text", "Project code."),
                Column("projectName", "Project", "text", "Project name."),
                Column("approvedHours", "Approved hours", "number", "Approved invoice-eligible time."),
                Column("approvedLaborEstimate", "Approved labor estimate", "currency", "Approved hours at the current governed project-cost rate basis when available."),
                Column("currentExpenses", "Current expenses", "currency", "Current Module 005 expense total."),
                Column("packageStatus", "Package status", "status", "Latest governed billing-readiness review."),
                Column("packageType", "Package type", "text", "Latest billing package type."),
                Column("forecastedFinalCost", "Forecast", "currency", "Group 3 forecast."),
                Column("currentVariance", "Variance", "currency", "Group 3 variance."),
                Column("blockers", "Blockers", "text", "Missing or unavailable billing prerequisites.")
            ]),
        new(
            "project_closeout_readiness",
            "Project Closeout Readiness",
            "Closeout state, billing disposition, financial blockers, and delivery readiness.",
            ["030", "031", "040", "041", "042"],
            ["projects"],
            ["project_closeout_records", "billing_readiness_reviews", "approved_time_entries", "project_notification_dispatches"],
            [
                Column("customer", "Customer", "text", "Authoritative customer name."),
                Column("projectCode", "Project code", "text", "Project code."),
                Column("projectName", "Project", "text", "Project name."),
                Column("projectStatus", "Project status", "status", "Current project state."),
                Column("closeoutStatus", "Closeout status", "status", "Governed Work-to-Cash closeout state."),
                Column("billingDisposition", "Billing disposition", "status", "Final billing decision."),
                Column("billingReadiness", "Billing readiness", "status", "Latest billing package review state."),
                Column("approvedHours", "Approved hours", "number", "Approved project time."),
                Column("openAlerts", "Open alerts", "number", "Open Module 022 cost alerts."),
                Column("notificationStatus", "Notification status", "status", "Group 4/Module 065 closeout delivery state."),
                Column("blockers", "Blockers", "text", "Unresolved closeout prerequisites.")
            ]),
        new(
            "notification_delivery",
            "Notification Delivery",
            "Group 4 dispatch, recipient, boundary, delivery, and diagnostic evidence.",
            ["030", "031", "041"],
            ["project_notification_dispatches"],
            [],
            [
                Column("createdAt", "Created", "datetime", "Dispatch creation time."),
                Column("customer", "Customer", "text", "Project customer when available."),
                Column("projectCode", "Project code", "text", "Project code when available."),
                Column("notificationType", "Notification type", "text", "Cost, schedule, or closeout notification type."),
                Column("sourceModule", "Source module", "text", "Module that requested the dispatch."),
                Column("severity", "Severity", "status", "Dispatch severity."),
                Column("recipientCount", "Recipients", "number", "Server-derived recipient count."),
                Column("deliveryBoundary", "Boundary", "status", "Test-only, production-governed, or locked."),
                Column("deliveryStatus", "Delivery status", "status", "Held, queued, sent, failed, or suppressed."),
                Column("diagnosticCode", "Diagnostic code", "text", "Sanitized failure code."),
                Column("sentAt", "Sent", "datetime", "Successful send timestamp when present.")
            ])
    ];

    internal static FinancialReportDefinition? Find(string? code) =>
        Catalog.FirstOrDefault(report => report.Code.Equals(
            (code ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase));

    internal static FinancialReportResult Build(
        FinancialReportDefinition definition,
        FinancialReportRequest request,
        FinancialOperationsContext context)
    {
        var filters = NormalizeFilters(request);
        var projects = FilterProjects(context.Truth.Projects, request)
            .Take(Math.Clamp(request.Limit ?? 250, 1, 500))
            .ToArray();
        var projectIds = projects.Select(project => project.ProjectId).ToHashSet();

        Dictionary<string, object?>[] rows = definition.Code switch
        {
            "project_financial_health" => FinancialHealth(projects),
            "project_hours_consumption" => Hours(projects, context.Supplemental),
            "project_expense_status" => Expenses(projects, request),
            "billing_readiness" => Billing(projects, context.Supplemental),
            "project_closeout_readiness" => Closeout(projects, context.Supplemental),
            "notification_delivery" => Notifications(
                context.Supplemental.Notifications
                    .Where(notification => !notification.ProjectId.HasValue
                        || projectIds.Contains(notification.ProjectId.Value))
                    .Take(Math.Clamp(request.Limit ?? 250, 1, 500))
                    .ToArray(),
                projects),
            _ => Array.Empty<Dictionary<string, object?>>()
        };

        var relevantKeys = definition.RequiredSources
            .Concat(definition.OptionalSources)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = context.AllSources
            .Where(source => relevantKeys.Contains(source.Key))
            .ToArray();
        var requiredUnavailable = definition.RequiredSources.Any(required =>
            sources.Any(source => source.Key.Equals(required, StringComparison.OrdinalIgnoreCase)
                && source.Status == "unavailable"));
        var anyUnavailable = sources.Any(source => source.Status == "unavailable");

        var status = rows.Length == 0
            ? requiredUnavailable ? "source_unavailable" : "no_data"
            : requiredUnavailable || anyUnavailable ? "partial" : "complete";
        var message = status switch
        {
            "complete" => $"{rows.Length} actual report row(s) loaded.",
            "partial" => $"{rows.Length} row(s) loaded. One or more sources are unavailable; healthy results remain visible.",
            "source_unavailable" => "A required report source is unavailable. Retry that source without clearing other module content.",
            _ => "No role-scoped data matched the selected filters."
        };

        return new(
            definition.Code,
            definition.Name,
            status,
            message,
            filters,
            rows,
            sources,
            DateTimeOffset.UtcNow);
    }

    private static Dictionary<string, object?>[] FinancialHealth(
        FinancialOperationsProject[] projects) => projects.Select(project => Row(
            ("projectId", project.ProjectId),
            ("customer", project.CustomerName),
            ("projectCode", project.ProjectCode),
            ("projectName", project.ProjectName),
            ("projectManager", project.ProjectManagerName),
            ("projectStatus", project.ProjectStatus),
            ("contractType", project.ContractType),
            ("contractedValue", project.ContractedValue),
            ("laborBudget", project.LaborBudget),
            ("expenseBudget", project.ExpenseBudget),
            ("plannedHours", project.PlannedHours),
            ("usedHours", project.UsedHours),
            ("remainingHours", project.RemainingHours),
            ("laborCost", project.LaborCost),
            ("uploadedExpenses", project.UploadedExpenses),
            ("committedCost", project.CommittedCost),
            ("forecastedFinalCost", project.ForecastedFinalCost),
            ("currentVariance", project.CurrentVariance),
            ("budgetStatus", project.BudgetStatus),
            ("completionPercentage", project.CompletionPercentage),
            ("sellReadiness", project.SellReadinessStatus),
            ("sellQuoteNumber", project.SellQuoteNumber),
            ("missingInformation", string.Join(", ", project.Missing))
        )).ToArray();

    private static Dictionary<string, object?>[] Hours(
        FinancialOperationsProject[] projects,
        FinancialSupplementalData supplemental) => projects.Select(project =>
        {
            supplemental.ApprovedTime.TryGetValue(project.ProjectId, out var approved);
            var usedPercent = project.PlannedHours > 0
                ? project.UsedHours / project.PlannedHours * 100m
                : (decimal?)null;
            var hoursStatus = !usedPercent.HasValue
                ? "planned_hours_missing"
                : usedPercent > 100m
                    ? "hours_exceeded"
                    : usedPercent >= 80m ? "hours_approaching" : "within_plan";
            return Row(
                ("projectId", project.ProjectId),
                ("customer", project.CustomerName),
                ("projectCode", project.ProjectCode),
                ("projectName", project.ProjectName),
                ("projectManager", project.ProjectManagerName),
                ("plannedHours", project.PlannedHours),
                ("usedHours", project.UsedHours),
                ("approvedHours", approved?.ApprovedHours),
                ("approvedLineCount", approved?.ApprovedLineCount ?? 0),
                ("remainingHours", project.RemainingHours),
                ("completionPercentage", project.CompletionPercentage),
                ("hoursStatus", hoursStatus));
        }).ToArray();

    private static Dictionary<string, object?>[] Expenses(
        FinancialOperationsProject[] projects,
        FinancialReportRequest request)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var project in projects)
        {
            foreach (var expense in project.Expenses)
            {
                if (request.DateFrom.HasValue
                    && expense.PeriodEnd.HasValue
                    && expense.PeriodEnd.Value < request.DateFrom.Value) continue;
                if (request.DateTo.HasValue
                    && expense.PeriodStart.HasValue
                    && expense.PeriodStart.Value > request.DateTo.Value) continue;

                rows.Add(Row(
                    ("projectId", project.ProjectId),
                    ("customer", project.CustomerName),
                    ("projectCode", project.ProjectCode),
                    ("projectName", project.ProjectName),
                    ("owner", expense.OwnerName),
                    ("periodStart", expense.PeriodStart),
                    ("periodEnd", expense.PeriodEnd),
                    ("source", expense.SourceMode),
                    ("sourceFormat", expense.SourceFormat),
                    ("fileName", expense.OriginalFileName),
                    ("amount", expense.TotalAmount),
                    ("reimbursableAmount", expense.ReimbursableAmount),
                    ("billingTreatment", expense.BillingTreatment),
                    ("notificationStatus", expense.NotificationStatus),
                    ("uploadedAt", expense.UploadedAt)));
            }
        }
        return rows.ToArray();
    }

    private static Dictionary<string, object?>[] Billing(
        FinancialOperationsProject[] projects,
        FinancialSupplementalData supplemental) => projects.Select(project =>
        {
            supplemental.ApprovedTime.TryGetValue(project.ProjectId, out var approved);
            supplemental.BillingReadiness.TryGetValue(project.ProjectId, out var readiness);
            var approvedLaborEstimate = approved is not null
                && project.LaborCost.HasValue
                && project.UsedHours > 0
                    ? Math.Round(
                        approved.ApprovedHours
                        * (project.LaborCost.Value / project.UsedHours),
                        2)
                    : (decimal?)null;
            var blockers = BillingBlockers(project, approved, readiness);
            return Row(
                ("projectId", project.ProjectId),
                ("customer", project.CustomerName),
                ("projectCode", project.ProjectCode),
                ("projectName", project.ProjectName),
                ("projectManager", project.ProjectManagerName),
                ("approvedHours", approved?.ApprovedHours),
                ("approvedLineCount", approved?.ApprovedLineCount ?? 0),
                ("approvedLaborEstimate", approvedLaborEstimate),
                ("currentExpenses", project.UploadedExpenses),
                ("packageStatus", readiness?.ReviewStatus ?? "not_recorded"),
                ("packageType", readiness?.PackageType ?? "not_recorded"),
                ("periodStart", readiness?.PeriodStart),
                ("periodEnd", readiness?.PeriodEnd),
                ("forecastedFinalCost", project.ForecastedFinalCost),
                ("currentVariance", project.CurrentVariance),
                ("budgetStatus", project.BudgetStatus),
                ("sellReadiness", project.SellReadinessStatus),
                ("blockerCount", blockers.Length),
                ("blockers", string.Join("; ", blockers)));
        }).ToArray();

    private static Dictionary<string, object?>[] Closeout(
        FinancialOperationsProject[] projects,
        FinancialSupplementalData supplemental) => projects.Select(project =>
        {
            supplemental.Closeout.TryGetValue(project.ProjectId, out var closeout);
            supplemental.BillingReadiness.TryGetValue(project.ProjectId, out var readiness);
            supplemental.ApprovedTime.TryGetValue(project.ProjectId, out var approved);
            var notifications = supplemental.Notifications
                .Where(item => item.ProjectId == project.ProjectId)
                .OrderByDescending(item => item.CreatedAt)
                .ToArray();
            var latestNotification = notifications.FirstOrDefault();
            var blockers = CloseoutBlockers(project, closeout, readiness, approved, latestNotification);
            return Row(
                ("projectId", project.ProjectId),
                ("customer", project.CustomerName),
                ("projectCode", project.ProjectCode),
                ("projectName", project.ProjectName),
                ("projectManager", project.ProjectManagerName),
                ("projectStatus", project.ProjectStatus),
                ("closeoutStatus", closeout?.CloseoutStatus ?? "not_started"),
                ("billingDisposition", closeout?.BillingDisposition ?? "not_recorded"),
                ("billingReadiness", readiness?.ReviewStatus ?? "not_recorded"),
                ("approvedHours", approved?.ApprovedHours),
                ("openAlerts", project.OpenAlertCount),
                ("notificationStatus", latestNotification?.DeliveryStatus ?? "not_recorded"),
                ("notificationBoundary", latestNotification?.DeliveryBoundary ?? "not_recorded"),
                ("blockerCount", blockers.Length),
                ("blockers", string.Join("; ", blockers)),
                ("lastUpdatedAt", closeout?.UpdatedAt));
        }).ToArray();

    private static Dictionary<string, object?>[] Notifications(
        FinancialNotificationState[] notifications,
        FinancialOperationsProject[] projects)
    {
        var byId = projects.ToDictionary(project => project.ProjectId);
        return notifications.Select(notification =>
        {
            var project = notification.ProjectId.HasValue
                && byId.TryGetValue(notification.ProjectId.Value, out var match)
                    ? match
                    : null;
            return Row(
                ("dispatchId", notification.DispatchId),
                ("createdAt", notification.CreatedAt),
                ("customer", project?.CustomerName ?? "Not project-specific"),
                ("projectCode", project?.ProjectCode ?? ""),
                ("projectName", project?.ProjectName ?? ""),
                ("notificationType", notification.NotificationType),
                ("sourceModule", notification.SourceModule),
                ("sourceStatus", notification.SourceStatus),
                ("severity", notification.Severity),
                ("recipientCount", notification.RecipientCount),
                ("deliveryBoundary", notification.DeliveryBoundary),
                ("deliveryStatus", notification.DeliveryStatus),
                ("diagnosticCode", notification.LastErrorCode),
                ("diagnosticMessage", notification.LastErrorMessage),
                ("sentAt", notification.SentAt));
        }).ToArray();
    }

    private static IEnumerable<FinancialOperationsProject> FilterProjects(
        IEnumerable<FinancialOperationsProject> projects,
        FinancialReportRequest request)
    {
        var search = (request.Search ?? string.Empty).Trim();
        var customer = (request.Customer ?? string.Empty).Trim();
        var status = (request.Status ?? string.Empty).Trim();

        return projects
            .Where(project => !request.ProjectId.HasValue
                || project.ProjectId == request.ProjectId.Value)
            .Where(project => string.IsNullOrWhiteSpace(search)
                || $"{project.CustomerName} {project.ProjectCode} {project.ProjectName} {project.ProjectManagerName} {project.ContractType} {project.SellQuoteNumber}"
                    .Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(project => string.IsNullOrWhiteSpace(customer)
                || customer.Equals("all", StringComparison.OrdinalIgnoreCase)
                || project.CustomerName.Contains(customer, StringComparison.OrdinalIgnoreCase))
            .Where(project => string.IsNullOrWhiteSpace(status)
                || status.Equals("all", StringComparison.OrdinalIgnoreCase)
                || project.ProjectStatus.Equals(status, StringComparison.OrdinalIgnoreCase)
                || project.BudgetStatus.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(project => !request.DateFrom.HasValue
                || !project.EndDate.HasValue
                || project.EndDate.Value >= request.DateFrom.Value)
            .Where(project => !request.DateTo.HasValue
                || !project.StartDate.HasValue
                || project.StartDate.Value <= request.DateTo.Value)
            .OrderBy(project => BudgetOrder(project.BudgetStatus))
            .ThenBy(project => project.CustomerName)
            .ThenBy(project => project.ProjectName);
    }

    private static string[] BillingBlockers(
        FinancialOperationsProject project,
        FinancialApprovedTime? approved,
        FinancialBillingReadiness? readiness)
    {
        var blockers = new List<string>();
        if (approved is null || approved.ApprovedHours <= 0)
            blockers.Add("No approved invoice-eligible project time is available.");
        if (project.Missing.Length > 0)
            blockers.Add($"Missing financial information: {string.Join(", ", project.Missing)}.");
        if (readiness is null)
            blockers.Add("No governed billing-readiness review is recorded.");
        else if (!readiness.ReviewStatus.Equals("ready", StringComparison.OrdinalIgnoreCase))
            blockers.Add($"Billing package is {readiness.ReviewStatus}.");
        if (project.SellReadinessStatus.Contains("not_ready", StringComparison.OrdinalIgnoreCase)
            || project.SellReadinessStatus.Contains("missing", StringComparison.OrdinalIgnoreCase))
            blockers.Add($"SELL readiness: {project.SellReadinessStatus}.");
        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] CloseoutBlockers(
        FinancialOperationsProject project,
        FinancialCloseoutState? closeout,
        FinancialBillingReadiness? readiness,
        FinancialApprovedTime? approved,
        FinancialNotificationState? notification)
    {
        var blockers = new List<string>();
        if (closeout is null) blockers.Add("Closeout has not been requested.");
        if (readiness is null || !readiness.ReviewStatus.Equals("ready", StringComparison.OrdinalIgnoreCase))
            blockers.Add("Billing readiness is not ready.");
        if (approved is null && project.UsedHours > 0)
            blockers.Add("Used time exists but no approved-time evidence is available.");
        if (project.OpenAlertCount > 0)
            blockers.Add($"{project.OpenAlertCount} cost alert(s) remain open.");
        if (project.Missing.Length > 0)
            blockers.Add($"Financial information is incomplete: {string.Join(", ", project.Missing)}.");
        if (closeout?.CloseoutStatus == "closed"
            && notification is not null
            && notification.DeliveryStatus is "failed" or "held")
            blockers.Add($"Closeout notification is {notification.DeliveryStatus}.");
        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static Dictionary<string, object?> NormalizeFilters(
        FinancialReportRequest request) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["search"] = (request.Search ?? string.Empty).Trim(),
        ["projectId"] = request.ProjectId,
        ["customer"] = (request.Customer ?? string.Empty).Trim(),
        ["dateFrom"] = request.DateFrom,
        ["dateTo"] = request.DateTo,
        ["status"] = (request.Status ?? string.Empty).Trim(),
        ["limit"] = Math.Clamp(request.Limit ?? 250, 1, 500)
    };

    private static int BudgetOrder(string status) => status switch
    {
        "over_budget" => 0,
        "approaching_budget" => 1,
        "missing_financial_information" => 2,
        _ => 3
    };

    private static FinancialReportColumn Column(
        string key,
        string label,
        string dataType,
        string description) => new(key, label, dataType, description);

    private static Dictionary<string, object?> Row(
        params (string Key, object? Value)[] values)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values) row[key] = value;
        return row;
    }
}
