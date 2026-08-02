using System.Globalization;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseReportingEngine
{
    private static Dictionary<string, object?>[] RateAmountExceptions(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var project in projects)
        {
            if (project.CurrentVariance.HasValue && project.CurrentVariance.Value < 0)
            {
                rows.Add(Row(
                    ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                    ("exceptionType", "Forecast exceeds financial basis"), ("reference", project.ProjectCode),
                    ("expectedAmount", project.ContractedValue ?? project.LaborBudget), ("actualAmount", project.ForecastedFinalCost),
                    ("variance", project.CurrentVariance), ("severity", project.BudgetStatus),
                    ("diagnostic", CommercialCompleteness(project))));
            }
            foreach (var missing in project.Missing)
            {
                rows.Add(Row(
                    ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                    ("exceptionType", "Missing financial prerequisite"), ("reference", missing),
                    ("expectedAmount", null), ("actualAmount", null), ("variance", null),
                    ("severity", "warning"), ("diagnostic", $"Missing: {missing}")));
            }
        }
        return rows.Where(row => MatchesSearch(row, request.Search)).ToArray();
    }

    private static Dictionary<string, object?>[] CustomerProfitability(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context)
    {
        var revenue = InvoiceTotals(context.Supplemental.Rows("client_invoices"));
        return projects.GroupBy(project => project.CustomerName, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var invoiced = group.Sum(project => revenue.TryGetValue(project.ProjectId, out var value) ? value : 0m);
            var contracted = SumNullable(group.Select(project => project.ContractedValue));
            var cost = SumNullable(group.Select(project => project.ForecastedFinalCost ?? project.CommittedCost));
            var basis = contracted ?? (invoiced > 0 ? invoiced : null);
            var margin = basis.HasValue && cost.HasValue ? basis.Value - cost.Value : (decimal?)null;
            return Row(
                ("customer", group.Key), ("projectCount", group.Count()), ("activeProjects", group.Count(project => !IsClosedProject(project.ProjectStatus))),
                ("plannedHours", group.Sum(project => project.PlannedHours)), ("usedHours", group.Sum(project => project.UsedHours)),
                ("contractedValue", contracted), ("revenue", invoiced), ("forecastedFinalCost", cost),
                ("projectedMargin", margin), ("projectedMarginPercentage", margin.HasValue && basis.HasValue && basis.Value != 0 ? margin.Value / basis.Value * 100m : null),
                ("riskProjects", group.Count(project => project.BudgetStatus is "approaching_budget" or "over_budget" || project.HighAlertCount > 0)));
        }).OrderBy(row => row["customer"]).ToArray();
    }

    private static Dictionary<string, object?>[] SalesDeliveryHandoff(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context)
    {
        var metadata = context.Supplemental.Rows("work_register_project_metadata");
        return projects.Select(project =>
        {
            var row = LatestForProject(metadata, project.ProjectId);
            var gaps = new List<string>();
            if (string.IsNullOrWhiteSpace(project.SellQuoteNumber)) gaps.Add("SELL quote");
            if (string.IsNullOrWhiteSpace(project.ProjectManagerName)) gaps.Add("Project Manager");
            if (project.Engineers.Length == 0) gaps.Add("Engineer assignments");
            if (project.PlannedHours <= 0) gaps.Add("Planned hours");
            if (!row.HasValue) gaps.Add("Work Register metadata");
            var documents = row.HasValue ? Text(row.Value, "metadata_json") : string.Empty;
            return Row(
                ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("sellQuoteNumber", project.SellQuoteNumber), ("contractType", project.ContractType),
                ("projectManager", project.ProjectManagerName), ("engineerCount", project.Engineers.Length),
                ("plannedHours", project.PlannedHours), ("documentCoverage", string.IsNullOrWhiteSpace(documents) ? "not_recorded" : "recorded"),
                ("handoffState", gaps.Count == 0 ? "ready" : gaps.Count <= 2 ? "attention" : "incomplete"),
                ("gaps", string.Join(", ", gaps)));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] CustomerBillingSummary(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context)
    {
        var invoices = context.Supplemental.Rows("client_invoices");
        var reviews = context.Supplemental.Rows("billing_readiness_reviews");
        return projects.GroupBy(project => project.CustomerName, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var ids = group.Select(project => project.ProjectId).ToHashSet();
            var customerInvoices = invoices.Where(row => GuidValue(row, "project_id") is Guid id && ids.Contains(id)).ToArray();
            var billed = customerInvoices.Sum(row => DecimalValue(row, "invoice_total", "total_amount", "amount") ?? 0m);
            var contracted = SumNullable(group.Select(project => project.ContractedValue));
            var ready = group.Count(project => LatestForProject(reviews, project.ProjectId) is { } review
                && Text(review, "review_status", "status").Equals("ready", StringComparison.OrdinalIgnoreCase));
            return Row(
                ("customer", group.Key), ("projectCount", group.Count()), ("invoiceCount", customerInvoices.Length),
                ("billedAmount", billed), ("unbilledAmount", contracted.HasValue ? contracted.Value - billed : null),
                ("expenses", SumNullable(group.Select(project => project.UploadedExpenses))), ("contractedValue", contracted),
                ("billingReadyProjects", ready), ("blockedProjects", group.Count() - ready));
        }).OrderBy(row => row["customer"]).ToArray();
    }

    private static Dictionary<string, object?>[] ProjectReport(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context)
    {
        var metadata = context.Supplemental.Rows("work_register_project_metadata");
        return projects.Select(project =>
        {
            var row = LatestForProject(metadata, project.ProjectId);
            return Row(
                ("customer", project.CustomerName), ("projectCode", project.ProjectCode),
                ("legacyProjectCode", row.HasValue ? Text(row.Value, "legacy_project_code") : string.Empty),
                ("projectName", project.ProjectName), ("projectStatus", project.ProjectStatus),
                ("projectManager", project.ProjectManagerName), ("engineerCount", project.Engineers.Length),
                ("contractType", project.ContractType), ("startDate", project.StartDate), ("endDate", project.EndDate),
                ("plannedHours", project.PlannedHours), ("usedHours", project.UsedHours),
                ("remainingHours", project.RemainingHours), ("completionPercentage", project.CompletionPercentage));
        }).ToArray();
    }
}
