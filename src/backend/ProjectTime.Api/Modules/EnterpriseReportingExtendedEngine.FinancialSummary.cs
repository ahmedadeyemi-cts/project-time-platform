using System.Globalization;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseReportingEngine
{
    private static Dictionary<string, object?>[] ExecutiveSummary(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var timeRows = context.Supplemental.Rows("time_entries")
            .Where(row => WithinDate(row, request, "work_date", "entry_date", "date"))
            .ToArray();
        var billable = timeRows.Where(row => IsBillableTime(row, projects)).Sum(row => DecimalValue(row, "hours", "duration_hours") ?? 0m);
        var nonBillable = timeRows.Sum(row => DecimalValue(row, "hours", "duration_hours") ?? 0m) - billable;
        var eligible = billable + nonBillable;
        return
        [
            Row(
                ("projectCount", projects.Length),
                ("activeProjectCount", projects.Count(project => !IsClosedProject(project.ProjectStatus))),
                ("customerCount", projects.Select(project => project.CustomerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                ("engineerCount", projects.SelectMany(project => project.Engineers).Select(engineer => engineer.UserId).Distinct().Count()),
                ("plannedHours", projects.Sum(project => project.PlannedHours)),
                ("usedHours", projects.Sum(project => project.UsedHours)),
                ("remainingHours", projects.Sum(project => project.RemainingHours)),
                ("billableHours", billable),
                ("nonBillableHours", nonBillable),
                ("utilizationPercentage", eligible > 0 ? billable / eligible * 100m : null),
                ("contractedValue", SumNullable(projects.Select(project => project.ContractedValue))),
                ("forecastedFinalCost", SumNullable(projects.Select(project => project.ForecastedFinalCost))),
                ("financialVariance", SumNullable(projects.Select(project => project.CurrentVariance))),
                ("overBudgetProjects", projects.Count(project => project.BudgetStatus.Equals("over_budget", StringComparison.OrdinalIgnoreCase))),
                ("closeoutPendingProjects", projects.Count(project => !HasClosedCloseout(context.Supplemental.Rows("project_closeout_records"), project.ProjectId))),
                ("dataAsOf", context.Truth.GeneratedAt))
        ];
    }

    private static Dictionary<string, object?>[] AccountingInvoiceDetail(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var map = projects.ToDictionary(project => project.ProjectId);
        var invoices = context.Supplemental.Rows("client_invoices");
        var lines = context.Supplemental.Rows("invoice_line_items");
        var result = new List<Dictionary<string, object?>>();
        foreach (var invoice in invoices.Where(row => WithinDate(row, request, "generated_at", "created_at", "billing_period_end")))
        {
            var invoiceId = GuidValue(invoice, "client_invoice_id", "invoice_id");
            var projectId = GuidValue(invoice, "project_id");
            map.TryGetValue(projectId ?? Guid.Empty, out var project);
            var matching = invoiceId.HasValue
                ? lines.Where(line => GuidValue(line, "client_invoice_id", "invoice_id") == invoiceId).ToArray()
                : Array.Empty<JsonElement>();
            if (matching.Length == 0) matching = [default];
            foreach (var line in matching)
            {
                result.Add(Row(
                    ("customer", project?.CustomerName ?? Text(invoice, "customer_name", "client_name")),
                    ("projectCode", project?.ProjectCode ?? Text(invoice, "project_code")),
                    ("projectName", project?.ProjectName ?? Text(invoice, "project_name")),
                    ("invoiceNumber", Text(invoice, "invoice_number", "invoice_code")),
                    ("invoiceStatus", Text(invoice, "invoice_status", "status")),
                    ("billingPeriodStart", DateValue(invoice, "billing_period_start", "period_start")),
                    ("billingPeriodEnd", DateValue(invoice, "billing_period_end", "period_end")),
                    ("lineDescription", line.ValueKind == JsonValueKind.Undefined ? string.Empty : Text(line, "description", "line_description", "item_description")),
                    ("lineTotal", line.ValueKind == JsonValueKind.Undefined ? null : DecimalValue(line, "line_total", "amount", "extended_amount")),
                    ("invoiceTotal", DecimalValue(invoice, "invoice_total", "total_amount", "amount")),
                    ("generatedAt", DateTimeValue(invoice, "generated_at", "created_at"))));
            }
        }
        return result.Where(row => MatchesSearch(row, request.Search)).ToArray();
    }

    private static Dictionary<string, object?>[] TmSales(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context)
    {
        var billed = InvoiceTotals(context.Supplemental.Rows("client_invoices"));
        return projects.Where(project => IsTimeAndMaterial(project.ContractType)).Select(project =>
        {
            billed.TryGetValue(project.ProjectId, out var billedAmount);
            var contracted = project.ContractedValue;
            return Row(
                ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("projectManager", project.ProjectManagerName), ("plannedHours", project.PlannedHours),
                ("usedHours", project.UsedHours), ("remainingHours", project.RemainingHours),
                ("contractedValue", contracted), ("billedAmount", billedAmount),
                ("unbilledAmount", contracted.HasValue ? contracted.Value - billedAmount : null),
                ("sellQuoteNumber", project.SellQuoteNumber), ("commercialReadiness", project.SellReadinessStatus));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] ProjectStatusBilledCost(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context)
    {
        var billed = InvoiceTotals(context.Supplemental.Rows("client_invoices"));
        return projects.Select(project =>
        {
            billed.TryGetValue(project.ProjectId, out var billedAmount);
            return Row(
                ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("projectStatus", project.ProjectStatus), ("projectManager", project.ProjectManagerName),
                ("contractedValue", project.ContractedValue), ("billedAmount", billedAmount),
                ("committedCost", project.CommittedCost),
                ("remainingBalance", project.ContractedValue.HasValue ? project.ContractedValue.Value - billedAmount : null),
                ("plannedHours", project.PlannedHours), ("usedHours", project.UsedHours),
                ("remainingHours", project.RemainingHours), ("budgetStatus", project.BudgetStatus),
                ("commercialDataCompleteness", CommercialCompleteness(project)));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] ExpenseInvoiceBreakdown(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var invoiceByProject = context.Supplemental.Rows("client_invoices")
            .Where(row => GuidValue(row, "project_id").HasValue)
            .GroupBy(row => GuidValue(row, "project_id")!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => DateTimeValue(row, "generated_at", "created_at") ?? DateTimeOffset.MinValue).First());
        return projects.SelectMany(project => project.Expenses
            .Where(expense => !request.EngineerUserId.HasValue || expense.OwnerUserId == request.EngineerUserId.Value)
            .Where(expense => !request.DateFrom.HasValue || !expense.PeriodEnd.HasValue || expense.PeriodEnd.Value >= request.DateFrom.Value)
            .Where(expense => !request.DateTo.HasValue || !expense.PeriodStart.HasValue || expense.PeriodStart.Value <= request.DateTo.Value)
            .Select(expense =>
            {
                invoiceByProject.TryGetValue(project.ProjectId, out var invoice);
                return Row(
                    ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                    ("owner", expense.OwnerName), ("expenseSource", expense.SourceMode), ("expenseAmount", expense.TotalAmount),
                    ("reimbursableAmount", expense.ReimbursableAmount), ("billingTreatment", expense.BillingTreatment),
                    ("invoiceNumber", invoice.ValueKind == JsonValueKind.Undefined ? string.Empty : Text(invoice, "invoice_number")),
                    ("invoiceStatus", invoice.ValueKind == JsonValueKind.Undefined ? string.Empty : Text(invoice, "invoice_status", "status")),
                    ("invoiceExpenseAmount", invoice.ValueKind == JsonValueKind.Undefined ? null : DecimalValue(invoice, "expense_amount")),
                    ("invoiceTotal", invoice.ValueKind == JsonValueKind.Undefined ? null : DecimalValue(invoice, "invoice_total", "total_amount")));
            })).Where(row => MatchesSearch(row, request.Search)).ToArray();
    }
}
