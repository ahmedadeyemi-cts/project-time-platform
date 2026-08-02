using System.Globalization;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseReportingEngine
{
    private static Dictionary<string, object?>[] EngineerProjectOverUnder(
        FinancialOperationsProject[] projects,
        EnterpriseReportRequest request)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var project in projects)
        {
            var projectOverUnder = project.PlannedHours - project.UsedHours;
            var financialOverUnder = project.ContractedValue.HasValue && project.ForecastedFinalCost.HasValue
                ? project.ContractedValue.Value - project.ForecastedFinalCost.Value
                : (decimal?)null;
            var engineers = project.Engineers
                .Where(engineer => !request.EngineerUserId.HasValue || engineer.UserId == request.EngineerUserId.Value)
                .ToArray();
            if (engineers.Length == 0 && !request.EngineerUserId.HasValue)
            {
                rows.Add(OverUnderRow(project, null, projectOverUnder, financialOverUnder));
                continue;
            }
            rows.AddRange(engineers.Select(engineer => OverUnderRow(project, engineer, projectOverUnder, financialOverUnder)));
        }
        return rows.ToArray();
    }

    private static Dictionary<string, object?> OverUnderRow(
        FinancialOperationsProject project,
        FinancialOperationsEngineer? engineer,
        decimal projectOverUnder,
        decimal? financialOverUnder) => Row(
            ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
            ("projectManager", project.ProjectManagerName), ("engineer", engineer?.DisplayName ?? "Unassigned"),
            ("assignedHours", engineer?.AssignedHours ?? 0m), ("engineerUsedHours", engineer?.UsedHours ?? 0m),
            ("engineerRemainingHours", engineer is null ? 0m : engineer.AssignedHours - engineer.UsedHours),
            ("projectPlannedHours", project.PlannedHours), ("projectUsedHours", project.UsedHours),
            ("projectHoursOverUnder", projectOverUnder), ("contractedValue", project.ContractedValue),
            ("forecastedFinalCost", project.ForecastedFinalCost), ("financialOverUnder", financialOverUnder),
            ("hoursState", OverUnderState(projectOverUnder, project.PlannedHours)), ("budgetState", project.BudgetStatus),
            ("commercialDataCompleteness", CommercialCompleteness(project)));

    private static Dictionary<string, object?>[] UnbilledTimeReadiness(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context)
    {
        var approved = AggregateHours(context.Supplemental.Rows("approved_time_entries"), projects, true);
        var billedHours = InvoiceHours(context.Supplemental.Rows("invoice_line_items"));
        var reviews = context.Supplemental.Rows("billing_readiness_reviews");
        return projects.Select(project =>
        {
            approved.TryGetValue(project.ProjectId, out var approvedHours);
            billedHours.TryGetValue(project.ProjectId, out var invoicedHours);
            var unbilled = Math.Max(0m, approvedHours - invoicedHours);
            var review = LatestForProject(reviews, project.ProjectId);
            var blockers = new List<string>();
            if (unbilled <= 0) blockers.Add("No approved unbilled time.");
            if (!review.HasValue) blockers.Add("No billing-readiness review.");
            if (project.Missing.Length > 0) blockers.Add($"Commercial data: {string.Join(", ", project.Missing)}.");
            return Row(
                ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("approvedHours", approvedHours), ("unbilledHours", unbilled),
                ("estimatedUnbilledAmount", project.RateContext && project.PlannedHours > 0 && project.ContractedValue.HasValue
                    ? unbilled * (project.ContractedValue.Value / project.PlannedHours) : null),
                ("currentExpenses", project.UploadedExpenses),
                ("billingReadiness", review.HasValue ? Text(review.Value, "review_status", "status") : "not_recorded"),
                ("sellReadiness", project.SellReadinessStatus), ("blockers", string.Join(" ", blockers)));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] ProjectMargin(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context)
    {
        var revenue = InvoiceTotals(context.Supplemental.Rows("client_invoices"));
        return projects.Select(project =>
        {
            revenue.TryGetValue(project.ProjectId, out var invoiced);
            var basis = project.ContractedValue ?? (invoiced > 0 ? invoiced : null);
            var cost = project.ForecastedFinalCost ?? project.CommittedCost;
            var margin = basis.HasValue && cost.HasValue ? basis.Value - cost.Value : (decimal?)null;
            return Row(
                ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("contractedValue", project.ContractedValue), ("revenue", invoiced), ("committedCost", project.CommittedCost),
                ("forecastedFinalCost", project.ForecastedFinalCost), ("projectedMargin", margin),
                ("projectedMarginPercentage", margin.HasValue && basis.HasValue && basis.Value != 0 ? margin.Value / basis.Value * 100m : null),
                ("budgetStatus", project.BudgetStatus), ("commercialDataCompleteness", CommercialCompleteness(project)));
        }).ToArray();
    }
}
