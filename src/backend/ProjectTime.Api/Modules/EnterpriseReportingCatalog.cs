namespace ProjectTime.Api.Modules;

internal static class EnterpriseReportingCatalog
{
    internal static readonly EnterpriseReportDefinition[] All =
    [
        Report(
            "project_portfolio", "Project Portfolio", "Project Delivery",
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
            "time_entry_detail", "Time Entry Detail", "Time & Utilization",
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
            "engineer_utilization", "Engineer Utilization", "People & Capacity",
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
            "project_manager_portfolio", "Project Manager Portfolio", "People & Capacity",
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
            "customer_project_summary", "Customer Project Summary", "Customers",
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
            "project_closeout_readiness", "Project Closeout Readiness", "Project Delivery",
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
            "notification_delivery", "Notification Delivery", "Operations",
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
