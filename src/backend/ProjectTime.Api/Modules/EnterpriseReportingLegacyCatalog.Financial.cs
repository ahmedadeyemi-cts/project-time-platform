namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseReportingCatalog
{
    private static readonly EnterpriseReportDefinition[] LegacyExecutiveAndFinancialReports =
    [
        Report(
            "executive_summary_dashboard", "Executive Summary Dashboard", "Executive & Portfolio",
            "Portfolio totals for projects, customers, hours, utilization, financial variance, billing readiness, closeout, and source coverage.",
            ["030", "039", "040", "042", "057", "060"], ["projects"],
            ["time_entries", "client_invoices", "billing_readiness_reviews", "project_closeout_records"],
            ["all_scoped", "financial_scoped"], "Every metric is calculated from the signed-in user's authorized project and people scope.",
            [DateFrom(), DateTo(), Customer(), ProjectManager(), ProjectStatus(), ContractType()],
            [
                C("projectCount", "Projects", "number"), C("activeProjectCount", "Active projects", "number"),
                C("customerCount", "Customers", "number"), C("engineerCount", "Engineers", "number"),
                C("plannedHours", "Planned hours", "number"), C("usedHours", "Used hours", "number"),
                C("remainingHours", "Remaining hours", "number"), C("billableHours", "Billable hours", "number"),
                C("nonBillableHours", "Non-billable hours", "number"), C("utilizationPercentage", "Utilization", "percent"),
                C("contractedValue", "Contracted value", "currency", true), C("forecastedFinalCost", "Forecasted final cost", "currency", true),
                C("financialVariance", "Financial variance", "currency", true), C("overBudgetProjects", "Over-budget projects", "number"),
                C("closeoutPendingProjects", "Closeout pending", "number"), C("dataAsOf", "Data as of", "datetime")
            ]),
        Report(
            "accounting_invoice_detail_report", "Accounting Invoice Detail Report", "Billing & Invoicing",
            "Invoice header and line-item evidence by customer, project, billing period, amount, and lifecycle state.",
            ["030", "042"], ["client_invoices"], ["invoice_line_items", "projects"], ["financial_scoped"],
            "Invoice rows remain restricted to visible projects and financial field scope.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), WorkflowStatus(), Search(), Limit()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("invoiceNumber", "Invoice number"), C("invoiceStatus", "Invoice status", "status"),
                C("billingPeriodStart", "Billing period start", "date"), C("billingPeriodEnd", "Billing period end", "date"),
                C("lineDescription", "Line description"), C("lineTotal", "Line total", "currency", true),
                C("invoiceTotal", "Invoice total", "currency", true), C("generatedAt", "Generated", "datetime")
            ]),
        Report(
            "tm_sales_report", "T&M Sales Report", "Sales & Delivery",
            "Time-and-material project sales context, hours, billed amount, forecast, and commercial readiness.",
            ["026", "030", "036", "039", "042", "055B"], ["projects"], ["client_invoices", "sell_commercial_model"],
            ["commercial_scoped", "financial_scoped"], "Only visible T&M projects and role-visible commercial fields are included.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), Engineer(), ProjectStatus(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectManager", "Project Manager"), C("plannedHours", "Planned hours", "number"),
                C("usedHours", "Used hours", "number"), C("remainingHours", "Remaining hours", "number"),
                C("contractedValue", "Contracted value", "currency", true), C("billedAmount", "Billed amount", "currency", true),
                C("unbilledAmount", "Unbilled amount", "currency", true), C("sellQuoteNumber", "SELL quote"),
                C("commercialReadiness", "Commercial readiness", "status")
            ]),
        Report(
            "project_status_billed_cost_remaining_balance", "Project Status Report - Billed Cost and Remaining Balance", "Financial",
            "Project lifecycle, billed amount, committed cost, remaining contract balance, hours, and data completeness.",
            ["030", "039", "042", "055B"], ["projects"], ["client_invoices", "sell_commercial_model"], ["financial_scoped"],
            "Financial values follow the project financial truth service's server-calculated field visibility.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), ProjectStatus(), BudgetStatus(), ContractType()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectStatus", "Project status", "status"), C("projectManager", "Project Manager"),
                C("contractedValue", "Contracted value", "currency", true), C("billedAmount", "Billed amount", "currency", true),
                C("committedCost", "Committed cost", "currency", true), C("remainingBalance", "Remaining balance", "currency", true),
                C("plannedHours", "Planned hours", "number"), C("usedHours", "Used hours", "number"),
                C("remainingHours", "Remaining hours", "number"), C("budgetStatus", "Budget status", "status"),
                C("commercialDataCompleteness", "Commercial data completeness")
            ]),
        Report(
            "certify_expense_accounting_invoice_breakdown", "Certify Expense + Accounting Invoice Breakdown", "Billing & Invoicing",
            "Project expense uploads and accounting invoice totals by project, owner, billing treatment, and billing period.",
            ["005", "030", "038", "042"], ["project_expenses"], ["client_invoices", "invoice_line_items"], ["financial_scoped"],
            "Expense and invoice values are returned only for authorized projects and financial fields.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), Engineer(), WorkflowStatus(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("owner", "Expense owner"), C("expenseSource", "Expense source"), C("expenseAmount", "Expense amount", "currency", true),
                C("reimbursableAmount", "Reimbursable amount", "currency", true), C("billingTreatment", "Billing treatment", "status"),
                C("invoiceNumber", "Invoice number"), C("invoiceStatus", "Invoice status", "status"),
                C("invoiceExpenseAmount", "Invoiced expense amount", "currency", true), C("invoiceTotal", "Invoice total", "currency", true)
            ]),
        Report(
            "engineer_project_over_under_budget", "Engineer Project Over / Under Budget Report", "Financial",
            "Engineer assignment and project totals with hours over/under, forecast over/under, budget state, and commercial-data completeness.",
            ["001", "018", "019", "030", "039", "057"], ["projects"], ["time_entries", "approved_time_entries", "cost_alerts"],
            ["engineering_scoped", "financial_scoped"], "Engineers are locked to themselves; PMs are locked to their projects; broader roles retain governed scope.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), Engineer(), ProjectStatus(), BudgetStatus(), ContractType()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectManager", "Project Manager"), C("engineer", "Engineer"), C("assignedHours", "Assigned hours", "number"),
                C("engineerUsedHours", "Engineer used hours", "number"), C("engineerRemainingHours", "Engineer remaining hours", "number"),
                C("projectPlannedHours", "Project planned hours", "number"), C("projectUsedHours", "Project used hours", "number"),
                C("projectHoursOverUnder", "Project hours over / under", "number"), C("contractedValue", "Contracted value", "currency", true),
                C("forecastedFinalCost", "Forecasted final cost", "currency", true), C("financialOverUnder", "Financial over / under", "currency", true),
                C("hoursState", "Hours state", "status"), C("budgetState", "Budget state", "status"),
                C("commercialDataCompleteness", "Commercial data completeness")
            ]),
        Alias(
            "unbilled_time_invoice_readiness", "Unbilled Time / Invoice Readiness Report", "Billing & Invoicing",
            "Approved and unbilled time, estimated amount, expenses, billing readiness, SELL readiness, and blockers.",
            ["001", "026", "039", "042"], ["projects", "approved_time_entries"],
            ["client_invoices", "billing_readiness_reviews"], ["financial_scoped"],
            [Customer(), Project(), ProjectManager(), ProjectStatus(), WorkflowStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("approvedHours", "Approved hours", "number"), C("unbilledHours", "Unbilled hours", "number"),
                C("estimatedUnbilledAmount", "Estimated unbilled amount", "currency", true), C("currentExpenses", "Current expenses", "currency", true),
                C("billingReadiness", "Billing readiness", "status"), C("sellReadiness", "SELL readiness", "status"), C("blockers", "Blockers")
            ]),
        Alias(
            "project_margin", "Project Margin Report", "Financial",
            "Contracted value, revenue, cost, projected margin, margin percentage, and missing financial prerequisites.",
            ["030", "039", "042", "055B"], ["projects"], ["client_invoices", "project_expenses"], ["financial_scoped"],
            [Customer(), Project(), ProjectManager(), ProjectStatus(), BudgetStatus(), ContractType(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("contractedValue", "Contracted value", "currency", true), C("revenue", "Revenue", "currency", true),
                C("committedCost", "Committed cost", "currency", true), C("forecastedFinalCost", "Forecasted final cost", "currency", true),
                C("projectedMargin", "Projected margin", "currency", true), C("projectedMarginPercentage", "Projected margin", "percent"),
                C("budgetStatus", "Budget status", "status"), C("commercialDataCompleteness", "Commercial data completeness")
            ]),
        Alias(
            "rate_amount_exception", "Rate / Amount Exception Report", "Financial",
            "Rate-card, time-entry, expense, and invoice amount exceptions with project and diagnostic context.",
            ["001", "005", "030", "039", "042"], ["projects"], ["time_entries", "project_expenses", "work_rate_card_lines"], ["financial_scoped"],
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), Engineer(), Severity(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("exceptionType", "Exception type"), C("reference", "Reference"), C("expectedAmount", "Expected amount", "currency", true),
                C("actualAmount", "Actual amount", "currency", true), C("variance", "Variance", "currency", true),
                C("severity", "Severity", "status"), C("diagnostic", "Diagnostic")
            ]),
        Alias(
            "customer_profitability", "Customer Profitability Report", "Financial",
            "Customer-level revenue, cost, projected margin, projects, hours, and risk.",
            ["021", "030", "039", "042"], ["projects"], ["client_invoices", "project_expenses"], ["financial_scoped"],
            [Customer(), ProjectManager(), ProjectStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCount", "Projects", "number"), C("activeProjects", "Active projects", "number"),
                C("plannedHours", "Planned hours", "number"), C("usedHours", "Used hours", "number"),
                C("contractedValue", "Contracted value", "currency", true), C("revenue", "Revenue", "currency", true),
                C("forecastedFinalCost", "Forecasted final cost", "currency", true), C("projectedMargin", "Projected margin", "currency", true),
                C("projectedMarginPercentage", "Projected margin", "percent"), C("riskProjects", "Risk projects", "number")
            ]),
        Alias(
            "customer_billing_summary", "Customer Billing Summary Report", "Billing & Invoicing",
            "Customer totals for invoices, billed amount, unbilled amount, expenses, projects, and billing readiness.",
            ["005", "030", "039", "042"], ["projects"], ["client_invoices", "billing_readiness_reviews"], ["financial_scoped"],
            [Customer(), ProjectManager(), ProjectStatus(), WorkflowStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCount", "Projects", "number"), C("invoiceCount", "Invoices", "number"),
                C("billedAmount", "Billed amount", "currency", true), C("unbilledAmount", "Unbilled amount", "currency", true),
                C("expenses", "Expenses", "currency", true), C("contractedValue", "Contracted value", "currency", true),
                C("billingReadyProjects", "Billing-ready projects", "number"), C("blockedProjects", "Blocked projects", "number")
            ])
    ];
}
