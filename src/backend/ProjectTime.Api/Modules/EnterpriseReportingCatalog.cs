namespace ProjectTime.Api.Modules;

internal static class EnterpriseReportingCatalog
{
    internal static readonly EnterpriseReportDefinition[] Core =
    [
        Report(
            "project_portfolio", "Project Report", "Project Delivery",
            "Authoritative project, customer, ownership, schedule, team, contract, hours, completion, and delivery status.",
            ["006", "018", "019", "020", "055C", "055D"],
            ["projects"], ["assignments", "project_metadata"],
            ["all_scoped"], "Only projects returned by the server-enforced project scope are reportable.",
            [Search(), Customer(), Project(), ProjectManager(), ProjectStatus(), ContractType(), Billable(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectStatus", "Status", "status"), C("projectManager", "Project Manager"), C("engineerCount", "Engineers", "number"),
                C("contractType", "Contract type"), C("startDate", "Start", "date"), C("endDate", "End", "date"),
                C("plannedHours", "Planned hours", "number"), C("usedHours", "Used hours", "number"),
                C("remainingHours", "Remaining hours", "number"), C("completionPercentage", "Completion", "percent")
            ]),
        Report(
            "project_financial_health", "Project Financial Health", "Financial",
            "Role-appropriate contracted value, budgets, labor cost, expenses, committed cost, forecast, variance, and budget state.",
            ["005", "018", "030", "039", "042", "055B"],
            ["projects", "time_entries"], ["project_expenses", "sell_commercial_model", "cost_alerts"],
            ["financial_scoped"], "Financial values follow each project's server-calculated field visibility; report access does not expand it.",
            [Search(), Customer(), Project(), ProjectManager(), Engineer(), ProjectStatus(), BudgetStatus(), ContractType(), Billable(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"), C("projectManager", "Project Manager"),
                C("contractedValue", "Contracted value", "currency", true), C("laborBudget", "Labor budget", "currency", true),
                C("expenseBudget", "Expense budget", "currency", true), C("laborCost", "Labor cost", "currency", true),
                C("uploadedExpenses", "Expenses", "currency", true), C("committedCost", "Committed cost", "currency", true),
                C("forecastedFinalCost", "Forecast", "currency", true), C("currentVariance", "Variance", "currency", true),
                C("budgetStatus", "Budget state", "status"), C("completionPercentage", "Completion", "percent"),
                C("visibility", "Financial visibility")
            ]),
        Report(
            "project_budget_forecast", "Budget, Forecast & Variance", "Financial",
            "Budget consumption, forecasted final cost, variance, alert state, and missing financial prerequisites.",
            ["018", "022", "030", "039", "055B"],
            ["projects"], ["cost_alerts", "sell_commercial_model"],
            ["financial_scoped"], "Only role-visible projects and role-visible monetary fields are returned.",
            [Customer(), Project(), ProjectManager(), BudgetStatus(), ContractType(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("laborBudget", "Labor budget", "currency", true), C("expenseBudget", "Expense budget", "currency", true),
                C("committedCost", "Committed cost", "currency", true), C("forecastedFinalCost", "Forecast", "currency", true),
                C("variance", "Variance", "currency", true), C("budgetStatus", "Budget state", "status"),
                C("openAlerts", "Open alerts", "number"), C("highAlerts", "High alerts", "number"), C("missingInformation", "Missing information")
            ]),
        Report(
            "project_hours_consumption", "Project Hours Consumption", "Time & Utilization",
            "Planned, used, approved, remaining, and completion hours for role-scoped projects.",
            ["001", "003", "018", "019", "030", "039"],
            ["projects", "time_entries"], ["approved_time_entries"],
            ["all_scoped"], "Engineers see assigned project hours; Project Managers see projects they manage; broader roles retain governed scope.",
            [Customer(), Project(), ProjectManager(), Engineer(), ProjectStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectManager", "Project Manager"), C("plannedHours", "Planned hours", "number"),
                C("usedHours", "Used hours", "number"), C("approvedHours", "Approved hours", "number"),
                C("remainingHours", "Remaining hours", "number"), C("completionPercentage", "Completion", "percent"), C("hoursStatus", "Hours state", "status")
            ]),
        Report(
            "time_entry_detail", "Time Entry Detail Report", "Time & Utilization",
            "Individual time-entry evidence with work date, project, task, status, normal/afterhours classification, hours, and description.",
            ["001", "002", "007", "030"],
            ["time_entries"], ["projects", "assignments"],
            ["time_scoped"], "Engineers are locked to their own entries. Project Managers are limited to time on projects they manage.",
            [DateFrom(true), DateTo(true), Customer(), Project(), ProjectManager(), Engineer(), WorkflowStatus(), Search(), Limit()],
            [
                C("workDate", "Work date", "date"), C("engineer", "Engineer"), C("customer", "Customer"), C("projectCode", "Project code"),
                C("projectName", "Project"), C("task", "Task"), C("timeType", "Time type"), C("hours", "Hours", "number"),
                C("status", "Workflow status", "status"), C("description", "Description")
            ]),
        Report(
            "engineer_workload", "Engineer Workload & Assignment", "People & Capacity",
            "Assigned engineers, project/task allocation, planned and used hours, remaining effort, and workload concentration.",
            ["018", "019", "057", "069", "070"],
            ["projects", "assignments"], ["resource_qualifications", "capacity"],
            ["engineering_scoped"], "Engineers are locked to themselves. Project Managers see engineers assigned to their projects.",
            [Engineer(), Project(), ProjectManager(), Customer(), ProjectStatus(), DateFrom(), DateTo()],
            [
                C("engineer", "Engineer"), C("email", "Email"), C("customer", "Customer"), C("projectCode", "Project code"),
                C("projectName", "Project"), C("projectManager", "Project Manager"), C("assignedHours", "Assigned hours", "number"),
                C("usedHours", "Used hours", "number"), C("remainingHours", "Remaining hours", "number"), C("tasks", "Tasks")
            ]),
        Report(
            "engineer_utilization", "Engineer Utilization Detail Report", "People & Capacity",
            "Role-scoped utilization evidence by engineer, period, eligible hours, target, variance, and workload context.",
            ["001", "003", "057", "070"],
            ["time_entries"], ["utilization_targets", "assignments"],
            ["engineering_scoped"], "Engineers are locked to themselves; managers receive only their authorized people scope.",
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), Customer(), Project()],
            [
                C("engineer", "Engineer"), C("period", "Period"), C("eligibleHours", "Eligible hours", "number"),
                C("billableHours", "Billable hours", "number"), C("nonBillableHours", "Non-billable hours", "number"),
                C("targetHours", "Target hours", "number"), C("utilizationPercentage", "Utilization", "percent"),
                C("varianceHours", "Variance hours", "number"), C("scope", "Scope")
            ]),
        Report(
            "project_manager_portfolio", "PM Project Workload Report", "People & Capacity",
            "Project count, customers, project state, team size, hours, delivery risk, budget state, and closeout readiness by Project Manager.",
            ["018", "030", "040", "057"],
            ["projects"], ["project_closeout_records", "cost_alerts"],
            ["pm_scoped"], "Project Managers are locked to their own portfolio; leadership sees only authorized PM scope.",
            [ProjectManager(), Customer(), ProjectStatus(), BudgetStatus(), DateFrom(), DateTo()],
            [
                C("projectManager", "Project Manager"), C("projectCount", "Projects", "number"), C("customerCount", "Customers", "number"),
                C("engineerCount", "Engineers", "number"), C("plannedHours", "Planned hours", "number"), C("usedHours", "Used hours", "number"),
                C("atRiskProjects", "At-risk projects", "number"), C("overBudgetProjects", "Over-budget", "number"), C("closeoutPending", "Closeout pending", "number")
            ]),
        Report(
            "project_team_assignments", "Project Team Assignments", "Project Delivery",
            "Project Manager, Project Team Coordinator, Solution Architect, Account Executive, engineers, tasks, and allocation evidence.",
            ["018", "019", "020", "055C", "057"],
            ["projects", "assignments"], [],
            ["all_scoped"], "Rows are limited to role-scoped projects; engineer filters are restricted to visible assignments.",
            [Customer(), Project(), ProjectManager(), Engineer(), ProjectStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"), C("projectManager", "Project Manager"),
                C("projectTeamCoordinator", "PTC"), C("solutionArchitect", "Solution Architect"), C("accountExecutive", "Account Executive"),
                C("engineer", "Engineer"), C("assignedHours", "Assigned hours", "number"), C("usedHours", "Used hours", "number"), C("tasks", "Tasks")
            ]),
        Report(
            "customer_project_summary", "Customer Project Summary Report", "Customers",
            "Customer-level project portfolio, delivery ownership, hours, cost visibility, risk, and lifecycle state.",
            ["006", "018", "021", "030", "036"],
            ["projects"], ["sell_commercial_model", "cost_alerts"],
            ["all_scoped"], "Customers and projects are derived only from the user's authorized project scope.",
            [Customer(), ProjectStatus(), ProjectManager(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCount", "Projects", "number"), C("activeProjects", "Active", "number"),
                C("completedProjects", "Completed", "number"), C("projectManagers", "Project Managers"), C("engineers", "Engineers", "number"),
                C("plannedHours", "Planned hours", "number"), C("usedHours", "Used hours", "number"), C("riskProjects", "Risk projects", "number")
            ]),
        Report(
            "expense_detail", "Project Expense Detail", "Financial",
            "Current Module 005 expense uploads, owner, period, source, billing treatment, amounts, and notification state.",
            ["005", "030", "039", "042"],
            ["project_expenses"], ["projects"],
            ["financial_scoped"], "Expense values inherit project and financial field scope.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), Engineer(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"), C("owner", "Owner"),
                C("periodStart", "Period start", "date"), C("periodEnd", "Period end", "date"), C("source", "Source"),
                C("amount", "Amount", "currency", true), C("reimbursableAmount", "Reimbursable", "currency", true),
                C("billingTreatment", "Billing treatment", "status"), C("uploadedAt", "Uploaded", "datetime")
            ]),
        Report(
            "sell_delivery_context", "SELL & Delivery Context", "Sales & Delivery",
            "Governed SELL association, quote, commercial source, billing method, connector readiness, ownership, and project delivery state.",
            ["026", "030", "036", "055B"],
            ["projects"], ["sell_commercial_model"],
            ["commercial_scoped"], "Commercial fields remain limited to roles with project and commercial visibility.",
            [Customer(), Project(), ProjectManager(), ProjectStatus(), ContractType(), SourceStatus()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"), C("projectManager", "Project Manager"),
                C("accountExecutive", "Account Executive"), C("contractType", "Contract type"), C("sellQuoteNumber", "SELL quote"),
                C("billingMethod", "Billing method"), C("commercialSource", "Commercial source"), C("connectorReady", "Connector ready", "boolean"),
                C("readinessStatus", "SELL readiness", "status"), C("lastSuccessfulSyncAt", "Last sync", "datetime")
            ]),
        Report(
            "billing_readiness", "Billing Readiness", "Financial",
            "Approved time, current expenses, package review, forecast, variance, SELL readiness, and invoice blockers.",
            ["001", "005", "026", "039", "042"],
            ["projects", "approved_time_entries"], ["project_expenses", "billing_readiness_reviews", "sell_commercial_model"],
            ["financial_scoped"], "Billing evidence is limited to projects and financial fields authorized for the user.",
            [Customer(), Project(), ProjectManager(), ProjectStatus(), WorkflowStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"), C("approvedHours", "Approved hours", "number"),
                C("currentExpenses", "Current expenses", "currency", true), C("packageStatus", "Package status", "status"),
                C("forecastedFinalCost", "Forecast", "currency", true), C("currentVariance", "Variance", "currency", true),
                C("sellReadiness", "SELL readiness", "status"), C("blockers", "Blockers")
            ]),
        Report(
            "project_closeout_readiness", "Project Closeout Readiness Report", "Project Delivery",
            "Closeout state, billing disposition, approved time, open alerts, notification evidence, and remaining blockers.",
            ["040", "041", "042"],
            ["projects"], ["project_closeout_records", "billing_readiness_reviews", "project_notification_dispatches"],
            ["pm_scoped", "financial_scoped"], "Project Managers see only their own projects; finance and operations retain governed scope.",
            [Customer(), Project(), ProjectManager(), ProjectStatus(), WorkflowStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"), C("projectStatus", "Project status", "status"),
                C("closeoutStatus", "Closeout status", "status"), C("billingDisposition", "Billing disposition", "status"),
                C("billingReadiness", "Billing readiness", "status"), C("approvedHours", "Approved hours", "number"),
                C("openAlerts", "Open alerts", "number"), C("notificationStatus", "Notification status", "status"), C("blockers", "Blockers")
            ]),
        Report(
            "notification_delivery", "Notification Report", "Operations",
            "Group 4 dispatch, source module, severity, server-derived recipients, Module 065 boundary, delivery state, and diagnostics.",
            ["022", "023", "032", "041", "065"],
            ["project_notification_dispatches"], [],
            ["operations_scoped"], "Notification data is limited to role-scoped projects unless organization operations access is present.",
            [DateFrom(), DateTo(), Customer(), Project(), ModuleCode(), Severity(), WorkflowStatus(), SourceStatus()],
            [
                C("createdAt", "Created", "datetime"), C("customer", "Customer"), C("projectCode", "Project code"),
                C("notificationType", "Notification type"), C("sourceModule", "Source module"), C("severity", "Severity", "status"),
                C("recipientCount", "Recipients", "number"), C("deliveryBoundary", "Boundary", "status"),
                C("deliveryStatus", "Delivery status", "status"), C("diagnosticCode", "Diagnostic code"), C("sentAt", "Sent", "datetime")
            ]),
        Report(
            "qualification_expiration", "Qualifications & Certification Expiration", "People & Capacity",
            "Qualification and certification category, competency, expiration lifecycle, renewal acknowledgement, and planned renewal date.",
            ["069"], ["resource_qualifications"], ["qualification_renewals"],
            ["people_scoped"], "Individuals see themselves; managers and administrators receive only their authorized people scope.",
            [Engineer(), WorkflowStatus(), DateFrom(), DateTo(), Search()],
            [
                C("person", "Person"), C("email", "Email"), C("team", "Team"), C("category", "Category"),
                C("qualification", "Qualification"), C("competency", "Competency"), C("effectiveEndDate", "Expiration", "date"),
                C("lifecycle", "Lifecycle", "status"), C("acknowledgement", "Acknowledgement", "status"), C("plannedRenewalDate", "Planned renewal", "date")
            ]),
        Report(
            "oncall_coverage", "On-Call Coverage", "Operations",
            "On-call roster, coverage periods, assigned engineer, phone, acknowledgement, rotation gaps, and conflicts.",
            ["071"], ["oncall_schedule"], ["oncall_roster", "oncall_imports"],
            ["operations_scoped"], "Engineers see published coverage; management capabilities remain permission controlled.",
            [DateFrom(true), DateTo(true), Engineer(), WorkflowStatus(), Search()],
            [
                C("department", "Department"), C("coverageStart", "Coverage start", "datetime"), C("coverageEnd", "Coverage end", "datetime"),
                C("engineer", "Engineer"), C("phone", "Phone"), C("acknowledgement", "Acknowledgement", "status"),
                C("gap", "Gap", "boolean"), C("conflict", "Conflict", "boolean"), C("source", "Source")
            ]),
        Report(
            "issue_feature_lifecycle", "Issues, Defects & Feature Requests", "Operations",
            "Module 076 intake, triage, assignment, lifecycle, severity, source module, resolution, and immutable transition evidence.",
            ["075", "076"], ["module076_items"], ["module076_transitions", "integration_gateway_events"],
            ["operations_scoped"], "Users see their own submitted items unless broader triage authority is present.",
            [DateFrom(), DateTo(), ModuleCode(), WorkflowStatus(), Severity(), Search(), Limit()],
            [
                C("trackingId", "Tracking ID"), C("type", "Type"), C("affectedModule", "Module"), C("title", "Title"),
                C("status", "Status", "status"), C("severity", "Severity", "status"), C("reporter", "Reporter"),
                C("assignee", "Assignee"), C("createdAt", "Created", "datetime"), C("resolvedAt", "Resolved", "datetime"), C("resolution", "Resolution")
            ]),
        Report(
            "release_deployment_readiness", "Release & Deployment Readiness", "Operational Control",
            "Release SHA, environments, approvals, artifacts, validation gates, rollback readiness, and operational history.",
            ["058", "077"], ["operational_control_history"], ["release_controls", "deployment_evidence"],
            ["control_plane"], "Restricted to release, platform, and administrative operational scope.",
            [DateFrom(), DateTo(), WorkflowStatus(), SourceStatus(), Search()],
            [
                C("releaseSha", "Release SHA"), C("environment", "Environment"), C("status", "Status", "status"),
                C("approvals", "Approvals"), C("artifact", "Artifact"), C("validation", "Validation", "status"),
                C("rollbackReady", "Rollback ready", "boolean"), C("owner", "Owner"), C("observedAt", "Observed", "datetime")
            ]),
        Report(
            "service_health_slo", "Service Health, SLO & Error Budget", "Operational Control",
            "Provider-neutral service inventory, telemetry adapter, SLI, SLO, error budget, alerts, and dependency health.",
            ["013", "068", "078"], ["platform_health"], ["service_inventory", "slo_definitions", "alert_history"],
            ["control_plane"], "Uses the provider-neutral platform-health foundation and does not create an Azure-only model.",
            [DateFrom(), DateTo(), SourceStatus(), Severity(), Search()],
            [
                C("service", "Service"), C("provider", "Provider"), C("region", "Region"), C("status", "Status", "status"),
                C("sli", "SLI"), C("sloTarget", "SLO target", "percent"), C("currentValue", "Current value", "percent"),
                C("errorBudgetRemaining", "Error budget remaining", "percent"), C("alerts", "Alerts", "number"), C("observedAt", "Observed", "datetime")
            ]),
        Report(
            "data_governance_retention", "Data Governance & Retention", "Operational Control",
            "Data domain, classification, owner, retention policy, legal holds, purge eligibility, and execution history.",
            ["079"], ["data_governance_domains"], ["retention_policies", "legal_holds", "purge_jobs"],
            ["control_plane"], "Restricted to governance, security, legal, and authorized administrative roles.",
            [WorkflowStatus(), SourceStatus(), Search(), DateFrom(), DateTo()],
            [
                C("domain", "Data domain"), C("classification", "Classification"), C("owner", "Owner"), C("retentionPolicy", "Retention policy"),
                C("legalHold", "Legal hold", "status"), C("purgeEligible", "Purge eligible", "boolean"),
                C("lastPurgeAt", "Last purge", "datetime"), C("status", "Status", "status")
            ]),
        Report(
            "customer_delivery_acceptance", "Customer Delivery & Acceptance", "Operational Control",
            "Engagement, milestone, deliverable, evidence readiness, approver, acceptance criteria, decision, and immutable signoff evidence.",
            ["080"], ["customer_acceptance_engagements"], ["acceptance_templates", "acceptance_evidence", "acceptance_decisions"],
            ["delivery_scoped"], "Project teams see only engagements associated with their authorized projects.",
            [Customer(), Project(), ProjectManager(), WorkflowStatus(), DateFrom(), DateTo(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("engagement", "Engagement"), C("milestone", "Milestone"),
                C("deliverable", "Deliverable"), C("evidenceStatus", "Evidence", "status"), C("approver", "Approver"),
                C("decision", "Decision", "status"), C("decisionAt", "Decision date", "datetime"), C("criteria", "Acceptance criteria")
            ]),
        Report(
            "secure_project_information", "Secure Project Information", "Project Delivery",
            "Technical-information request templates, customer submission status, field completion, access evidence, and immutable revision history without exposing secret values.",
            ["033"], ["secure_project_information_requests"], ["secure_project_information_audit"],
            ["delivery_scoped"], "Only project-authorized users receive metadata; secret values are never included in reporting.",
            [Customer(), Project(), ProjectManager(), WorkflowStatus(), DateFrom(), DateTo(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("request", "Request"), C("template", "Template"),
                C("status", "Status", "status"), C("fieldCount", "Fields", "number"), C("completedFieldCount", "Completed", "number"),
                C("submittedAt", "Submitted", "datetime"), C("revision", "Revision", "number"), C("accessMode", "Access mode")
            ]),
        Report(
            "pmo_project_controls", "Enterprise PMO Project Controls", "Project Delivery",
            "Charter, WBS, milestones, risks, issues, assumptions, decisions, changes, stakeholders, quality, benefits, and baseline evidence.",
            ["034", "066"], ["pmo_projects"], ["pmo_controls", "project_flowhive_plans"],
            ["delivery_scoped"], "Project Managers are limited to their projects; assigned engineers see role-appropriate delivery controls.",
            [Customer(), Project(), ProjectManager(), Engineer(), WorkflowStatus(), Severity(), DateFrom(), DateTo(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"), C("controlType", "Control type"),
                C("reference", "Reference"), C("title", "Title"), C("owner", "Owner"), C("status", "Status", "status"),
                C("severity", "Severity", "status"), C("dueDate", "Due date", "date"), C("baseline", "Baseline"), C("updatedAt", "Updated", "datetime")
            ])
    ];

    // Familiar report identities retained from the prior reporting experience.
    // These definitions use the same server-scoped source contracts as the
    // enterprise Analytics Center, so restoring a familiar report name never
    // widens project, person, customer, or financial-field access.
    internal static readonly EnterpriseReportDefinition[] Legacy =
    [
        Report(
            "executive_summary_dashboard", "Executive Summary Dashboard", "Executive",
            "Leadership summary of projects, customers, hours, utilization, delivery risk, financial variance, billing readiness, and closeout state.",
            ["001", "018", "030", "039", "040", "042"],
            ["projects"], ["billing_readiness_reviews", "project_closeout_records"],
            ["all_scoped", "financial_scoped"], "The summary is calculated only from the caller's server-authorized portfolio and field visibility.",
            [Customer(), ProjectManager(), ProjectStatus(), BudgetStatus(), DateFrom(), DateTo()],
            [
                C("visibleProjects", "Visible projects", "number"), C("activeProjects", "Active projects", "number"),
                C("customers", "Customers", "number"), C("engineers", "Engineers", "number"),
                C("plannedHours", "Planned hours", "number"), C("usedHours", "Used hours", "number"),
                C("remainingHours", "Remaining hours", "number"), C("billableUtilization", "Billable utilization", "percent"),
                C("atRiskProjects", "At-risk projects", "number"), C("overBudgetProjects", "Over-budget projects", "number"),
                C("currentVariance", "Current variance", "currency", true), C("closeoutPending", "Closeout pending", "number"),
                C("dataAsOf", "Data as of", "datetime")
            ]),
        Report(
            "accounting_invoice_detail", "Accounting Invoice Detail Report", "Financial",
            "Invoice evidence by customer and project, including billing period, status, labor, expense, total, and readiness information.",
            ["030", "039", "042"],
            ["projects"], ["client_invoices", "billing_invoices", "billing_invoice_lines"],
            ["financial_scoped"], "Invoice rows are restricted to visible projects and role-appropriate financial fields.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), WorkflowStatus(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("invoiceNumber", "Invoice number"), C("invoiceStatus", "Invoice status", "status"),
                C("billingPeriodStart", "Billing period start", "date"), C("billingPeriodEnd", "Billing period end", "date"),
                C("laborAmount", "Labor amount", "currency", true), C("expenseAmount", "Expense amount", "currency", true),
                C("invoiceTotal", "Invoice total", "currency", true), C("invoiceDate", "Invoice date", "date"),
                C("source", "Source")
            ]),
        Report(
            "tm_sales", "T&M Sales Report", "Sales & Delivery",
            "Time-and-material project, customer, quote, rate-context, used-hours, and current billable-value evidence.",
            ["026", "030", "036", "055B"],
            ["projects"], ["sell_commercial_model"],
            ["commercial_scoped", "financial_scoped"], "Only visible T&M projects and authorized commercial values are returned.",
            [Customer(), Project(), ProjectManager(), Engineer(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectManager", "Project Manager"), C("contractType", "Contract type"), C("sellQuoteNumber", "SELL quote"),
                C("usedHours", "Used hours", "number"), C("contractedValue", "Contracted value", "currency", true),
                C("laborCost", "Labor cost", "currency", true), C("currentVariance", "Current variance", "currency", true),
                C("sellReadiness", "SELL readiness", "status")
            ]),
        Report(
            "project_status_billed_balance", "Project Status Report — Billed Cost and Remaining Balance", "Financial",
            "Project status with planned, used, billed, remaining, forecast, and current over/under evidence.",
            ["018", "030", "039", "042", "055B"],
            ["projects"], ["client_invoices", "billing_invoices"],
            ["financial_scoped"], "Project and monetary values remain role scoped.",
            [Customer(), Project(), ProjectManager(), ProjectStatus(), BudgetStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectStatus", "Status", "status"), C("projectManager", "Project Manager"),
                C("plannedCost", "Planned cost", "currency", true), C("committedCost", "Committed cost", "currency", true),
                C("billedAmount", "Billed amount", "currency", true), C("remainingBalance", "Remaining balance", "currency", true),
                C("forecastedFinalCost", "Forecast final cost", "currency", true), C("currentVariance", "Current variance", "currency", true),
                C("budgetStatus", "Budget status", "status")
            ]),
        Report(
            "certify_expense_invoice_breakdown", "Certify Expense + Accounting Invoice Breakdown", "Financial",
            "Project expense uploads, reimbursable amounts, invoice totals, and current billing treatment in one governed view.",
            ["005", "030", "038", "042"],
            ["projects"], ["project_expenses", "client_invoices", "billing_invoices"],
            ["financial_scoped"], "Expense and invoice values inherit project and financial-field scope.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), Engineer(), WorkflowStatus(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("expenseOwner", "Expense owner"), C("expenseAmount", "Expense amount", "currency", true),
                C("reimbursableAmount", "Reimbursable amount", "currency", true), C("billingTreatment", "Billing treatment", "status"),
                C("invoiceNumber", "Invoice number"), C("invoiceStatus", "Invoice status", "status"),
                C("invoiceTotal", "Invoice total", "currency", true), C("periodStart", "Period start", "date"), C("periodEnd", "Period end", "date")
            ]),
        Report(
            "engineer_project_over_under_budget", "Engineer Project Over / Under Budget Report", "Time & Utilization",
            "Engineer-by-project assigned hours, used hours, remaining hours, hour variance, project financial variance, and over/under state.",
            ["001", "018", "019", "030", "039"],
            ["projects"], ["time_entries", "assignments"],
            ["engineering_scoped", "financial_scoped"], "Engineers are locked to themselves; PMs and managers receive only their authorized people and project scope.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), Engineer(), ProjectStatus(), BudgetStatus()],
            [
                C("engineer", "Engineer"), C("customer", "Customer"), C("projectCode", "Project code"),
                C("projectName", "Project"), C("projectManager", "Project Manager"),
                C("assignedHours", "Assigned hours", "number"), C("usedHours", "Used hours", "number"),
                C("remainingHours", "Remaining hours", "number"), C("hoursVariance", "Hours over / under", "number"),
                C("hoursStatus", "Hours status", "status"), C("laborBudget", "Labor budget", "currency", true),
                C("laborCost", "Labor cost", "currency", true), C("financialVariance", "Financial over / under", "currency", true),
                C("financialStatus", "Financial status", "status"), C("dataAsOf", "Data as of", "datetime")
            ]),
        Report(
            "utilization_over_under", "Utilization Over / Under Report by Engineer", "Time & Utilization",
            "Current utilization and over/under state compared with projected utilization after remaining assigned work.",
            ["001", "003", "019", "030", "057", "070"],
            ["time_entries"], ["utilization_targets", "assignments"],
            ["engineering_scoped"], "Engineers are locked to themselves; management receives only authorized people scope.",
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), Customer(), Project()],
            [
                C("engineer", "Engineer"), C("period", "Period"), C("targetPercentage", "Target", "percent"),
                C("currentEligibleHours", "Current eligible hours", "number"), C("currentBillableHours", "Current billable hours", "number"),
                C("currentNonBillableHours", "Current non-billable hours", "number"), C("currentUtilization", "Current utilization", "percent"),
                C("currentOverUnder", "Current over / under", "percent"), C("currentStatus", "Current status", "status"),
                C("remainingAssignedHours", "Remaining assigned hours", "number"), C("projectedBillableHours", "Projected billable hours", "number"),
                C("projectedEligibleHours", "Projected eligible hours", "number"), C("projectedUtilization", "Projected utilization", "percent"),
                C("projectedOverUnder", "Projected over / under", "percent"), C("projectedStatus", "Projected status", "status"),
                C("utilizationChange", "Utilization change", "percent")
            ]),
        Report(
            "engineer_vacation_pto_used", "Engineer Vacation / PTO Used Report", "People & Capacity",
            "Vacation and PTO time recorded by employee, team, manager, approval state, hours, equivalent days, and year-to-date usage.",
            ["001", "003", "012", "030"],
            ["time_entries"], ["timesheet_day_statuses", "non_project_time_categories", "app_users", "reporting_relationships", "team_memberships", "teams"],
            ["engineering_scoped", "people_scoped"], "Individuals see themselves; managers and administrators receive only authorized people scope.",
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), WorkflowStatus(), Search()],
            [
                C("engineer", "Engineer"), C("manager", "Manager"), C("team", "Team"),
                C("firstDate", "First vacation / PTO date", "date"), C("latestDate", "Latest vacation / PTO date", "date"),
                C("submittedHours", "Submitted hours", "number"), C("approvedHours", "Approved hours", "number"),
                C("pendingHours", "Pending / returned hours", "number"), C("equivalentDays", "Equivalent days", "number"),
                C("yearToDateHours", "Year-to-date hours", "number"), C("entryCount", "Entries", "number")
            ]),
        Report(
            "billable_vs_non_billable", "Billable vs Non-Billable Report", "Time & Utilization",
            "Billable, non-billable, total, and billable-percentage evidence by engineer for the selected period.",
            ["001", "003", "030"],
            ["time_entries"], ["projects"],
            ["engineering_scoped"], "Engineer and management scope is enforced server side.",
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), Customer(), Project()],
            [
                C("engineer", "Engineer"), C("period", "Period"), C("billableHours", "Billable hours", "number"),
                C("nonBillableHours", "Non-billable hours", "number"), C("totalHours", "Total hours", "number"),
                C("billablePercentage", "Billable percentage", "percent"), C("targetPercentage", "Target", "percent"),
                C("variancePercentage", "Variance", "percent"), C("status", "Status", "status")
            ]),
        Report(
            "unbilled_time_invoice_readiness", "Unbilled Time / Invoice Readiness Report", "Financial",
            "Approved time, billed time, unbilled time, expense exposure, billing-readiness state, and invoice blockers by project.",
            ["001", "005", "030", "039", "042"],
            ["projects", "approved_time_entries"], ["billing_invoice_lines", "billing_readiness_reviews", "project_expenses"],
            ["financial_scoped"], "Only authorized projects and financial fields are returned.",
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), WorkflowStatus()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("approvedHours", "Approved hours", "number"), C("billedHours", "Billed hours", "number"),
                C("unbilledHours", "Unbilled hours", "number"), C("currentExpenses", "Current expenses", "currency", true),
                C("billingReadiness", "Billing readiness", "status"), C("invoiceBlockers", "Invoice blockers")
            ]),
        Report(
            "approval_bottleneck", "Approval Bottleneck Report", "Operations",
            "Pending and returned approval work by stage, owner, age, hours, project, and escalation state.",
            ["001", "002", "007", "030"],
            ["timesheet_day_statuses"], ["time_entries"],
            ["time_scoped", "operations_scoped"], "Approval rows follow the signed-in user's approval and project scope.",
            [DateFrom(), DateTo(), Engineer(), ProjectManager(), WorkflowStatus(), Search()],
            [
                C("engineer", "Engineer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("approvalStage", "Approval stage"), C("approvalOwner", "Current owner"), C("status", "Status", "status"),
                C("hours", "Hours", "number"), C("submittedAt", "Submitted", "datetime"), C("ageDays", "Age (days)", "number"),
                C("escalationStatus", "Escalation", "status")
            ]),
        Report(
            "missing_late_timesheet", "Missing Time / Late Timesheet Report", "Operations",
            "Missing, late, draft, returned, or unsubmitted Timesheet-day evidence by employee and week.",
            ["001", "002", "030"],
            ["timesheet_day_statuses"], ["time_entries"],
            ["time_scoped", "operations_scoped"], "Employees are locked to themselves unless the current role has authorized team scope.",
            [DateFrom(true), DateTo(true), Engineer(), WorkflowStatus(), Search()],
            [
                C("engineer", "Engineer"), C("workDate", "Work date", "date"), C("weekStart", "Week start", "date"),
                C("status", "Timesheet status", "status"), C("recordedHours", "Recorded hours", "number"),
                C("expectedHours", "Expected hours", "number"), C("missingHours", "Missing hours", "number"),
                C("lateDays", "Late days", "number"), C("manager", "Manager"), C("team", "Team")
            ]),
        Report(
            "project_margin", "Project Margin Report", "Financial",
            "Contracted value, committed cost, forecast final cost, current margin, forecast margin, and margin percentage by project.",
            ["005", "018", "030", "039", "055B"],
            ["projects"], ["sell_commercial_model"],
            ["financial_scoped"], "Margin values follow existing role-appropriate financial visibility.",
            [Customer(), Project(), ProjectManager(), ProjectStatus(), ContractType(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("contractedValue", "Contracted value", "currency", true), C("committedCost", "Committed cost", "currency", true),
                C("forecastedFinalCost", "Forecast final cost", "currency", true), C("currentMargin", "Current margin", "currency", true),
                C("forecastMargin", "Forecast margin", "currency", true), C("forecastMarginPercentage", "Forecast margin", "percent"),
                C("status", "Margin status", "status")
            ]),
        Report(
            "rate_amount_exception", "Rate / Amount Exception Report", "Financial",
            "Projects with missing rate context, incomplete amounts, negative variance, or other commercial exceptions.",
            ["005", "026", "030", "039", "055B"],
            ["projects"], ["sell_commercial_model", "cost_alerts"],
            ["financial_scoped", "commercial_scoped"], "Only exceptions within the current project and field scope are returned.",
            [Customer(), Project(), ProjectManager(), BudgetStatus(), ContractType(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("exceptionType", "Exception type"), C("exceptionDetail", "Exception detail"), C("severity", "Severity", "status"),
                C("contractedValue", "Contracted value", "currency", true), C("committedCost", "Committed cost", "currency", true),
                C("currentVariance", "Current variance", "currency", true), C("sellReadiness", "SELL readiness", "status")
            ]),
        Report(
            "customer_profitability", "Customer Profitability Report", "Financial",
            "Customer-level contracted value, committed cost, forecast cost, margin, margin percentage, and project risk.",
            ["005", "018", "030", "039", "055B"],
            ["projects"], ["sell_commercial_model"],
            ["financial_scoped"], "Customer values aggregate only authorized projects and visible monetary fields.",
            [Customer(), ProjectManager(), ProjectStatus(), ContractType(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCount", "Projects", "number"), C("contractedValue", "Contracted value", "currency", true),
                C("committedCost", "Committed cost", "currency", true), C("forecastedFinalCost", "Forecast final cost", "currency", true),
                C("forecastMargin", "Forecast margin", "currency", true), C("forecastMarginPercentage", "Forecast margin", "percent"),
                C("atRiskProjects", "At-risk projects", "number"), C("status", "Profitability status", "status")
            ]),
        Report(
            "sales_delivery_handoff_quality", "Sales-to-Delivery Handoff Quality Report", "Sales & Delivery",
            "SELL association, quote, ownership, project-document, assignment, and commercial-readiness evidence for each project.",
            ["019", "026", "030", "036", "055B", "055D"],
            ["projects"], ["sell_commercial_model", "project_metadata"],
            ["commercial_scoped", "delivery_scoped"], "Sales and delivery fields remain limited to authorized projects.",
            [Customer(), Project(), ProjectManager(), ProjectStatus(), SourceStatus(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectManager", "Project Manager"), C("accountExecutive", "Account Executive"),
                C("solutionArchitect", "Solution Architect"), C("sellQuoteNumber", "SELL quote"),
                C("sellReadiness", "SELL readiness", "status"), C("assignmentReady", "Assignment ready", "boolean"),
                C("documentReady", "Document ready", "boolean"), C("handoffScore", "Handoff score", "percent"), C("gaps", "Handoff gaps")
            ]),
        Report(
            "customer_billing_summary", "Customer Billing Summary Report", "Financial",
            "Customer-level approved hours, current expenses, invoiced amount, unbilled exposure, and billing-readiness state.",
            ["001", "005", "030", "039", "042"],
            ["projects"], ["approved_time_entries", "client_invoices", "billing_invoices", "billing_readiness_reviews"],
            ["financial_scoped"], "Customer totals include only authorized projects and visible financial fields.",
            [Customer(), ProjectManager(), DateFrom(), DateTo(), WorkflowStatus()],
            [
                C("customer", "Customer"), C("projectCount", "Projects", "number"), C("approvedHours", "Approved hours", "number"),
                C("currentExpenses", "Current expenses", "currency", true), C("invoicedAmount", "Invoiced amount", "currency", true),
                C("unbilledExposure", "Unbilled exposure", "currency", true), C("readyProjects", "Billing ready", "number"),
                C("blockedProjects", "Billing blocked", "number"), C("status", "Billing status", "status")
            ]),
        Report(
            "selected_engineers", "Selected Engineers Report", "People & Capacity",
            "Selected Engineer assignments, projects, planned hours, used hours, remaining work, and utilization context.",
            ["001", "018", "019", "030", "057", "070"],
            ["projects"], ["assignments", "time_entries"],
            ["engineering_scoped"], "Engineer selections are populated and enforced from authorized people scope.",
            [Engineer(), Project(), ProjectManager(), Customer(), ProjectStatus(), DateFrom(), DateTo()],
            [
                C("engineer", "Engineer"), C("email", "Email"), C("projectCount", "Projects", "number"),
                C("assignedHours", "Assigned hours", "number"), C("usedHours", "Used hours", "number"),
                C("remainingHours", "Remaining hours", "number"), C("billableProjects", "Billable projects", "number"),
                C("projectManagers", "Project Managers"), C("customers", "Customers")
            ]),
        Report(
            "team_report", "Team Report", "People & Capacity",
            "Team-level people, project, workload, utilization, and risk rollup.",
            ["001", "018", "019", "030", "057", "070"],
            ["projects"], ["assignments", "time_entries"],
            ["people_scoped"], "Teams and people are derived only from authorized assignments and directory scope.",
            [Engineer(), ProjectManager(), Customer(), Project(), DateFrom(), DateTo()],
            [
                C("team", "Team"), C("people", "People", "number"), C("projects", "Projects", "number"),
                C("assignedHours", "Assigned hours", "number"), C("usedHours", "Used hours", "number"),
                C("remainingHours", "Remaining hours", "number"), C("billableHours", "Billable hours", "number"),
                C("nonBillableHours", "Non-billable hours", "number"), C("utilization", "Utilization", "percent"),
                C("atRiskProjects", "At-risk projects", "number")
            ]),
        Report(
            "organization_report", "Organization Report", "Executive",
            "Authorized organization-level people, projects, customers, hours, utilization, financial variance, and delivery risk.",
            ["001", "018", "030", "039", "057", "070"],
            ["projects"], ["time_entries", "utilization_targets"],
            ["all_scoped", "financial_scoped"], "Organization totals never exceed the caller's authorized project, person, and field scope.",
            [DateFrom(), DateTo(), ProjectManager(), Customer(), ProjectStatus(), BudgetStatus()],
            [
                C("organization", "Organization"), C("people", "People", "number"), C("projects", "Projects", "number"),
                C("customers", "Customers", "number"), C("plannedHours", "Planned hours", "number"),
                C("usedHours", "Used hours", "number"), C("billableUtilization", "Billable utilization", "percent"),
                C("currentVariance", "Current variance", "currency", true), C("atRiskProjects", "At-risk projects", "number"),
                C("dataAsOf", "Data as of", "datetime")
            ]),
        Report(
            "workflow_approval_audit", "Workflow / Approval / Audit Report", "Operations",
            "Workflow transitions, approval evidence, actor, effective user, View-As state, decision, reason, and immutable audit timestamp.",
            ["002", "007", "008", "030", "037"],
            ["system_audit_events"], ["timesheet_day_statuses", "time_entries"],
            ["operations_scoped"], "Audit evidence is limited to authorized operations and project scope.",
            [DateFrom(), DateTo(), WorkflowStatus(), ModuleCode(), Search(), Limit()],
            [
                C("eventTime", "Event time", "datetime"), C("category", "Category"), C("eventType", "Event type"),
                C("status", "Status", "status"), C("actor", "Actor"), C("target", "Target"),
                C("sourceModule", "Source module"), C("summary", "Summary"), C("correlationId", "Correlation ID"),
                C("immutable", "Immutable", "boolean")
            ]),
        Report(
            "system_stability", "System Stability Report", "Operational Control",
            "Service, dependency, health, SLO, alert, and recovery evidence from the provider-neutral operations control plane.",
            ["013", "068", "078"],
            ["platform_health"], ["service_inventory", "slo_definitions", "alert_history"],
            ["control_plane"], "Restricted to authorized platform and administrative operations scope.",
            [DateFrom(), DateTo(), SourceStatus(), Severity(), Search()],
            [
                C("component", "Component"), C("provider", "Provider"), C("status", "Status", "status"),
                C("health", "Health", "status"), C("latency", "Latency"), C("errorBudget", "Error budget", "percent"),
                C("lastCheck", "Last check", "datetime"), C("diagnostic", "Diagnostic")
            ]),
        Report(
            "api_status", "API Status Report", "Operational Control",
            "API path, module, status, health, last check, and sanitized diagnostic evidence.",
            ["013", "016", "068", "078", "998"],
            ["platform_health"], ["service_inventory", "operational_control_history"],
            ["control_plane"], "Restricted to authorized platform and administrative operations scope.",
            [DateFrom(), DateTo(), ModuleCode(), SourceStatus(), Search()],
            [
                C("apiName", "API"), C("apiPath", "Path"), C("module", "Module"), C("status", "Status", "status"),
                C("httpStatus", "HTTP status", "number"), C("latencyMs", "Latency (ms)", "number"),
                C("lastCheck", "Last check", "datetime"), C("diagnostic", "Diagnostic")
            ]),
        Report(
            "external_connection", "External Connection Report", "Operational Control",
            "CRM, Microsoft, AI-provider, mail, and other approved external-connection readiness and last-test evidence.",
            ["026", "064", "065", "068", "075", "078"],
            ["external_connections"], ["platform_health", "operational_control_history"],
            ["control_plane"], "Restricted to authorized integration and administrative operations scope; secret values are excluded.",
            [DateFrom(), DateTo(), SourceStatus(), Search()],
            [
                C("connection", "Connection"), C("provider", "Provider"), C("environment", "Environment"),
                C("status", "Status", "status"), C("lastTest", "Last test", "datetime"),
                C("lastSuccess", "Last success", "datetime"), C("diagnostic", "Diagnostic"), C("secretReturned", "Secret returned", "boolean")
            ]),
        Report(
            "authentication_security", "Authentication / Security Report", "Operational Control",
            "Authentication, session, role, View-As, denied-write, and security-control audit evidence.",
            ["008", "012", "016", "037", "997"],
            ["system_audit_events"], ["operational_control_history"],
            ["control_plane"], "Restricted to authorized security, audit, and administrative scope.",
            [DateFrom(), DateTo(), WorkflowStatus(), Search(), Limit()],
            [
                C("eventTime", "Event time", "datetime"), C("eventType", "Event type"), C("actor", "Actor"),
                C("role", "Role"), C("viewAsTarget", "View-As target"), C("status", "Status", "status"),
                C("sourceModule", "Source module"), C("summary", "Summary"), C("correlationId", "Correlation ID")
            ]),
        Report(
            "ai_sow_scope", "AI / SOW Scope Report", "AI & Governance",
            "Celar AI, private RAG, SOW/GSD, FlowHive, provider routing, evidence, confidence, and human-review scope.",
            ["011", "025", "064", "066"],
            ["ai_capability_routing"], ["project_flowhive_plans", "pmo_controls"],
            ["operations_scoped", "delivery_scoped"], "AI and project evidence remains permission scoped; secret and private document values are excluded.",
            [Customer(), Project(), ProjectManager(), WorkflowStatus(), SourceStatus(), Search()],
            [
                C("capability", "Capability"), C("consumerModule", "Consumer module"), C("primaryProvider", "Primary provider"),
                C("secondaryProvider", "Secondary provider"), C("tertiaryProvider", "Tertiary provider"),
                C("fallback", "Governed fallback"), C("privateFirst", "Private first", "boolean"),
                C("status", "Status", "status"), C("humanReview", "Human review", "boolean"), C("evidence", "Evidence")
            ]),
        Report(
            "uat_evidence", "UAT Evidence Report", "Operational Control",
            "Release, deployment, validation, role, browser, defect, and acceptance evidence for Test UAT.",
            ["058", "076", "077", "080"],
            ["operational_control_history"], ["deployment_evidence", "module076_items", "customer_acceptance_engagements"],
            ["control_plane"], "Restricted to authorized release, platform, audit, and administrative scope.",
            [DateFrom(), DateTo(), WorkflowStatus(), Search(), Limit()],
            [
                C("evidenceId", "Evidence ID"), C("releaseSha", "Release SHA"), C("environment", "Environment"),
                C("role", "Role"), C("scenario", "Scenario"), C("status", "Status", "status"),
                C("observedAt", "Observed", "datetime"), C("artifact", "Artifact"), C("notes", "Notes")
            ]),
        Report(
            "report_library", "Report Library", "Report Administration",
            "Complete role-scoped Analytics Center catalog with category, purpose, modules, filters, sources, and export support.",
            ["030"], ["projects"], [],
            ["all_scoped"], "The catalog lists only reports allowed for the current role and record scope.",
            [Search()],
            [
                C("reportCode", "Report code"), C("reportName", "Report"), C("category", "Category"),
                C("description", "Description"), C("modules", "Modules"), C("filters", "Criteria"),
                C("requiredSources", "Required sources"), C("optionalSources", "Optional sources"),
                C("exports", "Exports")
            ])
    ];

    internal static readonly EnterpriseReportDefinition[] All = Core
        .Concat(Legacy)
        .GroupBy(report => report.Code, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToArray();

    internal static EnterpriseReportDefinition[] ForContext(EnterpriseReportingContext context) =>
        All.Where(report => IsAllowed(report, context.Actor, context.Projects)).ToArray();

    internal static EnterpriseReportDefinition? Find(
        EnterpriseReportingContext context,
        string? code) => ForContext(context).FirstOrDefault(report =>
            report.Code.Equals((code ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsAllowed(
        EnterpriseReportDefinition report,
        FinancialOperationsActor actor,
        FinancialOperationsProject[] projects)
    {
        if (actor.Broad || actor.HasPermission("MANAGE_ALL", "SYSTEM_ADMINISTRATION", "MANAGE_ENTERPRISE_REPORTING"))
            return true;

        var roles = actor.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var financialVisible = projects.Any(project => project.FullAmounts || project.CommercialAmounts || project.RateContext);
        var isEngineer = roles.Overlaps(["ENGINEER", "ENGINEERING", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD"]);
        var isPm = actor.PmLead || roles.Overlaps(["PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_MANAGEMENT_LEAD", "PM_TEAM_LEAD"]);
        var isManager = roles.Overlaps(["MANAGER", "ENGINEERING_MANAGER"]);
        var isSales = actor.Sales || roles.Overlaps(["SALES", "INSIDE_SALES", "ACCOUNT_EXECUTIVE", "SOLUTION_ARCHITECT"]);
        var isOperations = roles.Overlaps(["PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE", "BILLING"]);

        return report.Audience.Any(audience => audience switch
        {
            "all_scoped" => isEngineer || isPm || isManager || isSales || isOperations,
            "financial_scoped" => financialVisible || isPm || isOperations,
            "commercial_scoped" => isSales || isPm || isOperations,
            "time_scoped" => isEngineer || isPm || isManager || isOperations,
            "engineering_scoped" => isEngineer || isPm || isManager || isOperations,
            "pm_scoped" => isPm || isOperations,
            "people_scoped" => isEngineer || isPm || isManager || isOperations,
            "operations_scoped" => isPm || isManager || isOperations,
            "delivery_scoped" => isEngineer || isPm || isManager || isOperations,
            "control_plane" => false,
            _ => false
        });
    }

    private static EnterpriseReportDefinition Report(
        string code,
        string name,
        string category,
        string description,
        string[] modules,
        string[] requiredSources,
        string[] optionalSources,
        string[] audience,
        string scopeRule,
        EnterpriseReportFilterDefinition[] filters,
        EnterpriseReportColumnDefinition[] columns) =>
        new(code, name, category, description, modules, requiredSources, optionalSources, audience, scopeRule, filters, columns);

    private static EnterpriseReportColumnDefinition C(
        string key,
        string label,
        string dataType = "text",
        bool sensitive = false) =>
        new(key, label, dataType, $"{label} returned from the report's governed source contract.", sensitive);

    private static EnterpriseReportFilterDefinition F(
        string key,
        string label,
        string type,
        bool required = false,
        string? optionSource = null,
        string? placeholder = null,
        object? defaultValue = null) =>
        new(key, label, type, required, false, null, placeholder, optionSource, defaultValue);

    private static EnterpriseReportFilterDefinition Search() => F("search", "Search", "search", placeholder: "Search visible report data");
    private static EnterpriseReportFilterDefinition Customer() => F("customer", "Customer", "select", optionSource: "customers");
    private static EnterpriseReportFilterDefinition Project() => F("projectId", "Project", "select", optionSource: "projects");
    private static EnterpriseReportFilterDefinition ProjectManager() => F("projectManagerUserId", "Project Manager", "select", optionSource: "projectManagers");
    private static EnterpriseReportFilterDefinition Engineer() => F("engineerUserId", "Engineer", "select", optionSource: "engineers");
    private static EnterpriseReportFilterDefinition ProjectStatus() => F("projectStatus", "Project status", "select", optionSource: "projectStatuses");
    private static EnterpriseReportFilterDefinition BudgetStatus() => F("budgetStatus", "Budget status", "select", optionSource: "budgetStatuses");
    private static EnterpriseReportFilterDefinition ContractType() => F("contractType", "Contract type", "select", optionSource: "contractTypes");
    private static EnterpriseReportFilterDefinition WorkflowStatus() => F("workflowStatus", "Workflow status", "select", optionSource: "workflowStatuses");
    private static EnterpriseReportFilterDefinition Severity() => F("severity", "Severity", "select", optionSource: "severities");
    private static EnterpriseReportFilterDefinition ModuleCode() => F("moduleCode", "Module", "select", optionSource: "modules");
    private static EnterpriseReportFilterDefinition SourceStatus() => F("sourceStatus", "Source status", "select", optionSource: "sourceStatuses");
    private static EnterpriseReportFilterDefinition Billable() => F("billable", "Billable", "boolean");
    private static EnterpriseReportFilterDefinition DateFrom(bool required = false) => F("dateFrom", "Date from", "date", required);
    private static EnterpriseReportFilterDefinition DateTo(bool required = false) => F("dateTo", "Date to", "date", required);
    private static EnterpriseReportFilterDefinition Limit() => F("limit", "Maximum rows", "number", defaultValue: 500);
}
