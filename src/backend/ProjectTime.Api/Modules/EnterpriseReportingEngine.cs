using System.Globalization;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

internal static class EnterpriseReportingEngine
{
    internal static EnterpriseReportFilterOptions BuildFilterOptions(
        EnterpriseReportingContext context,
        EnterpriseReportDefinition definition)
    {
        var projects = context.Projects;
        var actor = context.Actor;
        var locked = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var roles = actor.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var engineerOnly = roles.Overlaps(["ENGINEER", "ENGINEERING"])
            && !actor.PmLead
            && !roles.Overlaps(["MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD"]);
        var pmOnly = actor.PmLead || roles.Overlaps(["PROJECT_MANAGER", "PROJECT_MANAGEMENT"]);

        if (engineerOnly) locked["engineerUserId"] = actor.EffectiveUserId;
        if (pmOnly && !actor.Broad) locked["projectManagerUserId"] = actor.EffectiveUserId;

        var options = new Dictionary<string, EnterpriseReportOption[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["customers"] = projects
                .Where(project => !string.IsNullOrWhiteSpace(project.CustomerName))
                .GroupBy(project => project.CustomerName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new EnterpriseReportOption(group.Key, group.Key))
                .OrderBy(option => option.Label).ToArray(),
            ["projects"] = projects
                .OrderBy(project => project.CustomerName)
                .ThenBy(project => project.ProjectName)
                .Select(project => new EnterpriseReportOption(
                    project.ProjectId.ToString(),
                    $"{project.ProjectCode} · {project.ProjectName}",
                    false,
                    project.CustomerName))
                .ToArray(),
            ["projectManagers"] = projects
                .Where(project => project.ProjectManagerUserId.HasValue)
                .GroupBy(project => project.ProjectManagerUserId!.Value)
                .Select(group => new EnterpriseReportOption(
                    group.Key.ToString(),
                    group.First().ProjectManagerName,
                    locked.TryGetValue("projectManagerUserId", out var value)
                        && value is Guid lockedId && lockedId != group.Key))
                .OrderBy(option => option.Label).ToArray(),
            ["engineers"] = projects
                .SelectMany(project => project.Engineers)
                .GroupBy(engineer => engineer.UserId)
                .Select(group => new EnterpriseReportOption(
                    group.Key.ToString(),
                    group.First().DisplayName,
                    locked.TryGetValue("engineerUserId", out var value)
                        && value is Guid lockedId && lockedId != group.Key,
                    group.First().Email))
                .OrderBy(option => option.Label).ToArray(),
            ["projectStatuses"] = Options(projects.Select(project => project.ProjectStatus)),
            ["budgetStatuses"] = Options(projects.Select(project => project.BudgetStatus)),
            ["contractTypes"] = Options(projects.Select(project => project.ContractType)),
            ["workflowStatuses"] = Options([
                "draft", "submitted", "pending", "approved", "ready", "in_progress",
                "blocked", "resolved", "closed", "accepted", "rejected", "expired", "expiring"
            ]),
            ["severities"] = Options(["low", "medium", "high", "critical"]),
            ["sourceStatuses"] = Options(["healthy", "partial", "unavailable", "restricted"]),
            ["modules"] = definition.Modules
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .Select(value => new EnterpriseReportOption(value, $"Module {value}"))
                .ToArray()
        };

        var explanation = engineerOnly
            ? "Engineer scope: report data and person filters are locked to the signed-in engineer and assigned projects."
            : pmOnly && !actor.Broad
                ? "Project Manager scope: report data and Project Manager filters are locked to projects managed by the effective user."
                : actor.Broad
                    ? "Organization scope: the actual role has broad reporting authority; record and field restrictions still apply."
                    : "Role scope: filter choices are populated only from records returned by server-enforced authorization.";

        return new EnterpriseReportFilterOptions(options, locked, explanation);
    }

    internal static EnterpriseReportResult Build(
        EnterpriseReportingContext context,
        EnterpriseReportDefinition definition,
        EnterpriseReportRequest request)
    {
        var normalized = Normalize(request, context, definition);
        var projects = FilterProjects(context.Projects, normalized).ToArray();
        var rows = definition.Code switch
        {
            "project_portfolio" => ProjectPortfolio(projects),
            "project_financial_health" => FinancialHealth(projects),
            "project_budget_forecast" => BudgetForecast(projects),
            "project_hours_consumption" => HoursConsumption(projects, context.Supplemental),
            "time_entry_detail" => TimeEntryDetail(projects, context, normalized),
            "engineer_workload" => EngineerWorkload(projects, normalized),
            "engineer_utilization" => EngineerUtilization(projects, context, normalized),
            "project_manager_portfolio" => ProjectManagerPortfolio(projects, context, normalized),
            "project_team_assignments" => ProjectTeamAssignments(projects, normalized),
            "customer_project_summary" => CustomerSummary(projects),
            "expense_detail" => ExpenseDetail(projects, normalized),
            "sell_delivery_context" => SellContext(projects),
            "billing_readiness" => BillingReadiness(projects, context.Supplemental),
            "project_closeout_readiness" => CloseoutReadiness(projects, context.Supplemental),
            "notification_delivery" => NotificationDelivery(projects, context.Supplemental, normalized),
            "executive_summary_dashboard" => ExecutiveSummaryDashboard(projects, context, normalized),
            "accounting_invoice_detail" => AccountingInvoiceDetail(projects, context.Supplemental, normalized),
            "tm_sales" => TmSales(projects),
            "project_status_billed_balance" => ProjectStatusBilledBalance(projects, context.Supplemental),
            "certify_expense_invoice_breakdown" => CertifyExpenseInvoiceBreakdown(projects, context.Supplemental, normalized),
            "engineer_project_over_under_budget" => EngineerProjectOverUnder(projects, normalized),
            "utilization_over_under" => UtilizationOverUnder(projects, context, normalized),
            "engineer_vacation_pto_used" => VacationPtoUsed(projects, context, normalized),
            "billable_vs_non_billable" => BillableVsNonBillable(projects, context, normalized),
            "unbilled_time_invoice_readiness" => UnbilledTimeInvoiceReadiness(projects, context.Supplemental),
            "approval_bottleneck" => ApprovalBottleneck(projects, context, normalized),
            "missing_late_timesheet" => MissingLateTimesheet(projects, context, normalized),
            "project_margin" => ProjectMargin(projects),
            "rate_amount_exception" => RateAmountExceptions(projects),
            "customer_profitability" => CustomerProfitability(projects),
            "sales_delivery_handoff_quality" => SalesDeliveryHandoffQuality(projects),
            "customer_billing_summary" => CustomerBillingSummary(projects, context.Supplemental),
            "selected_engineers" => SelectedEngineers(projects, normalized),
            "team_report" => TeamReport(projects, context, normalized),
            "organization_report" => OrganizationReport(projects, context, normalized),
            "workflow_approval_audit" => GenericRows(
                context.Supplemental.Rows("system_audit_events"), normalized, definition.Columns,
                GenericAuditMap(), context),
            "system_stability" => GenericRows(
                context.Supplemental.Rows("platform_health").Concat(context.Supplemental.Rows("service_inventory")).ToArray(),
                normalized, definition.Columns, GenericStabilityMap(), context),
            "api_status" => GenericRows(
                context.Supplemental.Rows("platform_health").Concat(context.Supplemental.Rows("operational_control_history")).ToArray(),
                normalized, definition.Columns, GenericApiMap(), context),
            "external_connection" => GenericRows(
                context.Supplemental.Rows("external_connections"), normalized, definition.Columns,
                GenericExternalConnectionMap(), context),
            "authentication_security" => GenericRows(
                context.Supplemental.Rows("system_audit_events"), normalized, definition.Columns,
                GenericSecurityMap(), context),
            "ai_sow_scope" => AiSowScope(
                context.Supplemental.Rows("ai_capability_routing"), normalized),
            "uat_evidence" => GenericRows(
                context.Supplemental.Rows("operational_control_history").Concat(context.Supplemental.Rows("deployment_evidence")).ToArray(),
                normalized, definition.Columns, GenericUatMap(), context),
            "report_library" => ReportLibrary(context),
            "qualification_expiration" => GenericRows(
                context.Supplemental.Rows("resource_qualifications"), normalized, definition.Columns,
                GenericQualificationMap(), context),
            "oncall_coverage" => GenericRows(
                context.Supplemental.Rows("oncall_schedule"), normalized, definition.Columns,
                GenericOnCallMap(), context),
            "issue_feature_lifecycle" => GenericRows(
                context.Supplemental.Rows("module076_items"), normalized, definition.Columns,
                GenericIssueMap(), context),
            "release_deployment_readiness" => GenericRows(
                context.Supplemental.Rows("operational_control_history"), normalized, definition.Columns,
                GenericReleaseMap(), context),
            "service_health_slo" => GenericRows(
                context.Supplemental.Rows("platform_health")
                    .Concat(context.Supplemental.Rows("service_inventory")).ToArray(),
                normalized, definition.Columns, GenericHealthMap(), context),
            "data_governance_retention" => GenericRows(
                context.Supplemental.Rows("data_governance_domains"), normalized, definition.Columns,
                GenericGovernanceMap(), context),
            "customer_delivery_acceptance" => GenericRows(
                context.Supplemental.Rows("customer_acceptance_engagements"), normalized, definition.Columns,
                GenericAcceptanceMap(), context),
            "secure_project_information" => GenericRows(
                context.Supplemental.Rows("secure_project_information_requests"), normalized, definition.Columns,
                GenericSecureInformationMap(), context),
            "pmo_project_controls" => GenericRows(
                context.Supplemental.Rows("pmo_controls"), normalized, definition.Columns,
                GenericPmoMap(), context),
            _ => Array.Empty<Dictionary<string, object?>>()
        };

        rows = rows.Take(Math.Clamp(normalized.Limit ?? 500, 1, 5000)).ToArray();
        var sources = ResolveSources(context, definition);
        var requiredUnavailable = sources.Any(source => source.Required
            && source.Status is "unavailable" or "restricted");
        var degradedSources = sources
            .Where(source => source.Status is "unavailable" or "partial" or "restricted")
            .Select(source => $"{source.Name} ({(source.Required ? "required" : "optional")}: {source.Status})")
            .ToArray();
        var anyDegraded = degradedSources.Length > 0;
        var status = rows.Length == 0
            ? requiredUnavailable ? "source_unavailable" : "no_data"
            : anyDegraded ? "partial" : "complete";
        var sourceList = degradedSources.Length == 0 ? "None" : string.Join("; ", degradedSources);
        var message = status switch
        {
            "complete" => $"Report completed. {rows.Length} authorized report row(s) loaded successfully.",
            "partial" => $"Report completed with limited source coverage. {rows.Length} authorized row(s) loaded successfully. Limited source(s): {sourceList}. Displayed rows remain valid; dependent fields may be incomplete.",
            "source_unavailable" => $"The report could not produce rows because a required source is unavailable or outside the current scope. Limited source(s): {sourceList}.",
            _ => "No data matched the current role scope and report-specific filters."
        };

        return new EnterpriseReportResult(
            definition.Code,
            definition.Name,
            status,
            message,
            EffectiveFilterDictionary(normalized),
            definition.Columns,
            rows,
            sources,
            DateTimeOffset.UtcNow,
            ScopeEvidence(context, definition));
    }

    private static EnterpriseReportRequest Normalize(
        EnterpriseReportRequest request,
        EnterpriseReportingContext context,
        EnterpriseReportDefinition definition)
    {
        var roles = context.Actor.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var engineerOnly = roles.Overlaps(["ENGINEER", "ENGINEERING"])
            && !context.Actor.PmLead
            && !roles.Overlaps(["MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD"]);
        var pmOnly = (context.Actor.PmLead || roles.Overlaps(["PROJECT_MANAGER", "PROJECT_MANAGEMENT"]))
            && !context.Actor.Broad;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dateFrom = request.DateFrom;
        var dateTo = request.DateTo;
        if (definition.Filters.Any(filter => filter.Key == "dateFrom" && filter.Required) && !dateFrom.HasValue)
            dateFrom = today.AddDays(-30);
        if (definition.Filters.Any(filter => filter.Key == "dateTo" && filter.Required) && !dateTo.HasValue)
            dateTo = today;
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
            (dateFrom, dateTo) = (dateTo, dateFrom);

        return request with
        {
            ReportCode = definition.Code,
            Search = Clean(request.Search, 300),
            Customer = Clean(request.Customer, 255),
            ProjectStatus = Clean(request.ProjectStatus, 80),
            BudgetStatus = Clean(request.BudgetStatus, 80),
            ContractType = Clean(request.ContractType, 120),
            WorkflowStatus = Clean(request.WorkflowStatus, 80),
            Severity = Clean(request.Severity, 40),
            ModuleCode = Clean(request.ModuleCode, 20),
            SourceStatus = Clean(request.SourceStatus, 40),
            EngineerUserId = engineerOnly ? context.Actor.EffectiveUserId : request.EngineerUserId,
            ProjectManagerUserId = pmOnly ? context.Actor.EffectiveUserId : request.ProjectManagerUserId,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Limit = Math.Clamp(request.Limit ?? 500, 1, 5000)
        };
    }

    private static IEnumerable<FinancialOperationsProject> FilterProjects(
        IEnumerable<FinancialOperationsProject> projects,
        EnterpriseReportRequest request)
    {
        var search = request.Search ?? string.Empty;
        return projects
            .Where(project => !request.ProjectId.HasValue || project.ProjectId == request.ProjectId.Value)
            .Where(project => string.IsNullOrWhiteSpace(request.Customer)
                || project.CustomerName.Contains(request.Customer, StringComparison.OrdinalIgnoreCase))
            .Where(project => !request.ProjectManagerUserId.HasValue
                || project.ProjectManagerUserId == request.ProjectManagerUserId.Value)
            .Where(project => !request.EngineerUserId.HasValue
                || project.Engineers.Any(engineer => engineer.UserId == request.EngineerUserId.Value))
            .Where(project => string.IsNullOrWhiteSpace(request.ProjectStatus)
                || project.ProjectStatus.Equals(request.ProjectStatus, StringComparison.OrdinalIgnoreCase))
            .Where(project => string.IsNullOrWhiteSpace(request.BudgetStatus)
                || project.BudgetStatus.Equals(request.BudgetStatus, StringComparison.OrdinalIgnoreCase))
            .Where(project => string.IsNullOrWhiteSpace(request.ContractType)
                || project.ContractType.Equals(request.ContractType, StringComparison.OrdinalIgnoreCase))
            .Where(project => !request.Billable.HasValue || project.Billable == request.Billable.Value)
            .Where(project => !request.DateFrom.HasValue || !project.EndDate.HasValue || project.EndDate.Value >= request.DateFrom.Value)
            .Where(project => !request.DateTo.HasValue || !project.StartDate.HasValue || project.StartDate.Value <= request.DateTo.Value)
            .Where(project => string.IsNullOrWhiteSpace(search)
                || $"{project.CustomerName} {project.ProjectCode} {project.ProjectName} {project.ProjectManagerName} {project.ContractType} {project.ProjectStatus}"
                    .Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(project => project.CustomerName)
            .ThenBy(project => project.ProjectName);
    }

    private static Dictionary<string, object?>[] ProjectPortfolio(FinancialOperationsProject[] projects) =>
        projects.Select(project => Row(
            ("projectId", project.ProjectId), ("customer", project.CustomerName),
            ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
            ("projectStatus", project.ProjectStatus), ("projectManager", project.ProjectManagerName),
            ("engineerCount", project.Engineers.Length), ("contractType", project.ContractType),
            ("startDate", project.StartDate), ("endDate", project.EndDate),
            ("plannedHours", project.PlannedHours), ("usedHours", project.UsedHours),
            ("remainingHours", project.RemainingHours), ("completionPercentage", project.CompletionPercentage))).ToArray();

    private static Dictionary<string, object?>[] FinancialHealth(FinancialOperationsProject[] projects) =>
        projects.Select(project => Row(
            ("projectId", project.ProjectId), ("customer", project.CustomerName),
            ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
            ("projectManager", project.ProjectManagerName), ("contractedValue", project.ContractedValue),
            ("laborBudget", project.LaborBudget), ("expenseBudget", project.ExpenseBudget),
            ("laborCost", project.LaborCost), ("uploadedExpenses", project.UploadedExpenses),
            ("committedCost", project.CommittedCost), ("forecastedFinalCost", project.ForecastedFinalCost),
            ("currentVariance", project.CurrentVariance), ("budgetStatus", project.BudgetStatus),
            ("completionPercentage", project.CompletionPercentage), ("visibility", project.VisibilityExplanation))).ToArray();

    private static Dictionary<string, object?>[] BudgetForecast(FinancialOperationsProject[] projects) =>
        projects.Select(project => Row(
            ("projectId", project.ProjectId), ("customer", project.CustomerName),
            ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
            ("laborBudget", project.LaborBudget), ("expenseBudget", project.ExpenseBudget),
            ("committedCost", project.CommittedCost), ("forecastedFinalCost", project.ForecastedFinalCost),
            ("variance", project.CurrentVariance), ("budgetStatus", project.BudgetStatus),
            ("openAlerts", project.OpenAlertCount), ("highAlerts", project.HighAlertCount),
            ("missingInformation", string.Join(", ", project.Missing)))).ToArray();

    private static Dictionary<string, object?>[] HoursConsumption(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental)
    {
        var approved = AggregateHours(supplemental.Rows("approved_time_entries"), projects, approvedOnly: true);
        return projects.Select(project =>
        {
            approved.TryGetValue(project.ProjectId, out var approvedHours);
            var percentage = project.PlannedHours > 0 ? project.UsedHours / project.PlannedHours * 100m : (decimal?)null;
            var status = !percentage.HasValue ? "planned_hours_missing"
                : percentage > 100 ? "hours_exceeded"
                : percentage >= 80 ? "hours_approaching" : "within_plan";
            return Row(
                ("projectId", project.ProjectId), ("customer", project.CustomerName),
                ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("projectManager", project.ProjectManagerName), ("plannedHours", project.PlannedHours),
                ("usedHours", project.UsedHours), ("approvedHours", approvedHours),
                ("remainingHours", project.RemainingHours), ("completionPercentage", project.CompletionPercentage),
                ("hoursStatus", status));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] TimeEntryDetail(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var projectMap = projects.ToDictionary(project => project.ProjectId);
        var people = People(context);
        return context.Supplemental.Rows("time_entries")
            .Where(row => WithinDate(row, request, "work_date", "entry_date", "date"))
            .Where(row => string.IsNullOrWhiteSpace(request.WorkflowStatus)
                || Text(row, "status", "workflow_status").Equals(request.WorkflowStatus, StringComparison.OrdinalIgnoreCase))
            .Where(row => !request.EngineerUserId.HasValue
                || GuidValue(row, "user_id", "engineer_user_id") == request.EngineerUserId.Value)
            .Select(row =>
            {
                var projectId = GuidValue(row, "project_id");
                projectMap.TryGetValue(projectId ?? Guid.Empty, out var project);
                var userId = GuidValue(row, "user_id", "engineer_user_id");
                return Row(
                    ("workDate", DateValue(row, "work_date", "entry_date", "date")),
                    ("engineer", userId.HasValue && people.TryGetValue(userId.Value, out var person) ? person : Text(row, "user_name", "engineer_name")),
                    ("customer", project?.CustomerName), ("projectCode", project?.ProjectCode),
                    ("projectName", project?.ProjectName), ("task", Text(row, "task_name", "task_code", "activity", "row_label")),
                    ("timeType", Text(row, "time_type", "entry_type", "hours_type")),
                    ("hours", DecimalValue(row, "hours", "duration_hours")),
                    ("status", Text(row, "status", "workflow_status")),
                    ("description", Text(row, "description", "comment", "work_description")));
            })
            .Where(row => MatchesSearch(row, request.Search))
            .OrderByDescending(row => row["workDate"])
            .ToArray();
    }

    private static Dictionary<string, object?>[] EngineerWorkload(
        FinancialOperationsProject[] projects,
        EnterpriseReportRequest request) => projects
        .SelectMany(project => project.Engineers
            .Where(engineer => !request.EngineerUserId.HasValue || engineer.UserId == request.EngineerUserId.Value)
            .Select(engineer => Row(
                ("engineerUserId", engineer.UserId), ("engineer", engineer.DisplayName), ("email", engineer.Email),
                ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("projectManager", project.ProjectManagerName), ("assignedHours", engineer.AssignedHours),
                ("usedHours", engineer.UsedHours), ("remainingHours", Math.Max(0, engineer.AssignedHours - engineer.UsedHours)),
                ("tasks", string.Join(", ", engineer.Tasks)))))
        .OrderBy(row => row["engineer"])
        .ThenBy(row => row["projectName"])
        .ToArray();

    private static Dictionary<string, object?>[] EngineerUtilization(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var projectMap = projects.ToDictionary(project => project.ProjectId);
        var people = People(context);
        var rows = context.Supplemental.Rows("time_entries")
            .Where(row => WithinDate(row, request, "work_date", "entry_date", "date"))
            .Where(row => !request.EngineerUserId.HasValue
                || GuidValue(row, "user_id", "engineer_user_id") == request.EngineerUserId.Value)
            .Select(row => new
            {
                UserId = GuidValue(row, "user_id", "engineer_user_id"),
                ProjectId = GuidValue(row, "project_id"),
                Hours = DecimalValue(row, "hours", "duration_hours") ?? 0m
            })
            .Where(row => row.UserId.HasValue)
            .ToArray();
        var targetRows = context.Supplemental.Rows("utilization_targets");
        return rows.GroupBy(row => row.UserId!.Value)
            .Select(group =>
            {
                var billable = group.Where(item => item.ProjectId.HasValue
                    && projectMap.TryGetValue(item.ProjectId.Value, out var project) && project.Billable)
                    .Sum(item => item.Hours);
                var total = group.Sum(item => item.Hours);
                var target = targetRows
                    .Where(row => GuidValue(row, "user_id") == group.Key)
                    .Select(row => DecimalValue(row, "target_hours", "annual_target_hours", "period_target_hours"))
                    .FirstOrDefault(value => value.HasValue);
                var utilization = total > 0 ? billable / total * 100m : (decimal?)null;
                people.TryGetValue(group.Key, out var name);
                return Row(
                    ("engineerUserId", group.Key), ("engineer", name ?? group.Key.ToString()),
                    ("period", Period(request)), ("eligibleHours", total), ("billableHours", billable),
                    ("nonBillableHours", total - billable), ("targetHours", target),
                    ("utilizationPercentage", utilization), ("varianceHours", target.HasValue ? billable - target.Value : null),
                    ("scope", group.Key == context.Actor.EffectiveUserId ? "self" : "authorized_people_scope"));
            }).OrderBy(row => row["engineer"]).ToArray();
    }

    private static Dictionary<string, object?>[] ProjectManagerPortfolio(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var closeoutRows = context.Supplemental.Rows("project_closeout_records");
        return projects.Where(project => project.ProjectManagerUserId.HasValue)
            .GroupBy(project => project.ProjectManagerUserId!.Value)
            .Where(group => !request.ProjectManagerUserId.HasValue || group.Key == request.ProjectManagerUserId.Value)
            .Select(group => Row(
                ("projectManagerUserId", group.Key), ("projectManager", group.First().ProjectManagerName),
                ("projectCount", group.Count()), ("customerCount", group.Select(project => project.CustomerName).Distinct().Count()),
                ("engineerCount", group.SelectMany(project => project.Engineers).Select(engineer => engineer.UserId).Distinct().Count()),
                ("plannedHours", group.Sum(project => project.PlannedHours)), ("usedHours", group.Sum(project => project.UsedHours)),
                ("atRiskProjects", group.Count(project => project.BudgetStatus is "approaching_budget" or "over_budget" || project.HighAlertCount > 0)),
                ("overBudgetProjects", group.Count(project => project.BudgetStatus == "over_budget")),
                ("closeoutPending", group.Count(project => !HasClosedCloseout(closeoutRows, project.ProjectId))))).ToArray();
    }

    private static Dictionary<string, object?>[] ProjectTeamAssignments(
        FinancialOperationsProject[] projects,
        EnterpriseReportRequest request) => projects.SelectMany(project =>
        {
            if (project.Engineers.Length == 0)
            {
                return new[] { Row(
                    ("projectId", project.ProjectId), ("customer", project.CustomerName), ("projectCode", project.ProjectCode),
                    ("projectName", project.ProjectName), ("projectManager", project.ProjectManagerName),
                    ("projectTeamCoordinator", project.ProjectTeamCoordinator?.DisplayName),
                    ("solutionArchitect", project.SolutionArchitect?.DisplayName), ("accountExecutive", project.AccountExecutive?.DisplayName),
                    ("engineer", "Unassigned"), ("assignedHours", 0m), ("usedHours", 0m), ("tasks", "")) };
            }
            return project.Engineers
                .Where(engineer => !request.EngineerUserId.HasValue || engineer.UserId == request.EngineerUserId.Value)
                .Select(engineer => Row(
                    ("projectId", project.ProjectId), ("customer", project.CustomerName), ("projectCode", project.ProjectCode),
                    ("projectName", project.ProjectName), ("projectManager", project.ProjectManagerName),
                    ("projectTeamCoordinator", project.ProjectTeamCoordinator?.DisplayName),
                    ("solutionArchitect", project.SolutionArchitect?.DisplayName), ("accountExecutive", project.AccountExecutive?.DisplayName),
                    ("engineer", engineer.DisplayName), ("assignedHours", engineer.AssignedHours),
                    ("usedHours", engineer.UsedHours), ("tasks", string.Join(", ", engineer.Tasks))));
        }).ToArray();

    private static Dictionary<string, object?>[] CustomerSummary(FinancialOperationsProject[] projects) => projects
        .GroupBy(project => project.CustomerName, StringComparer.OrdinalIgnoreCase)
        .Select(group => Row(
            ("customer", group.Key), ("projectCount", group.Count()),
            ("activeProjects", group.Count(project => !project.ProjectStatus.Equals("completed", StringComparison.OrdinalIgnoreCase)
                && !project.ProjectStatus.Equals("archived", StringComparison.OrdinalIgnoreCase))),
            ("completedProjects", group.Count(project => project.ProjectStatus.Equals("completed", StringComparison.OrdinalIgnoreCase)
                || project.ProjectStatus.Equals("archived", StringComparison.OrdinalIgnoreCase))),
            ("projectManagers", string.Join(", ", group.Select(project => project.ProjectManagerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())),
            ("engineers", group.SelectMany(project => project.Engineers).Select(engineer => engineer.UserId).Distinct().Count()),
            ("plannedHours", group.Sum(project => project.PlannedHours)), ("usedHours", group.Sum(project => project.UsedHours)),
            ("riskProjects", group.Count(project => project.BudgetStatus is "approaching_budget" or "over_budget" || project.HighAlertCount > 0))))
        .OrderBy(row => row["customer"]).ToArray();

    private static Dictionary<string, object?>[] ExpenseDetail(
        FinancialOperationsProject[] projects,
        EnterpriseReportRequest request) => projects.SelectMany(project => project.Expenses
        .Where(expense => !request.EngineerUserId.HasValue || expense.OwnerUserId == request.EngineerUserId.Value)
        .Where(expense => !request.DateFrom.HasValue || !expense.PeriodEnd.HasValue || expense.PeriodEnd.Value >= request.DateFrom.Value)
        .Where(expense => !request.DateTo.HasValue || !expense.PeriodStart.HasValue || expense.PeriodStart.Value <= request.DateTo.Value)
        .Select(expense => Row(
            ("projectId", project.ProjectId), ("customer", project.CustomerName), ("projectCode", project.ProjectCode),
            ("projectName", project.ProjectName), ("owner", expense.OwnerName), ("periodStart", expense.PeriodStart),
            ("periodEnd", expense.PeriodEnd), ("source", expense.SourceMode), ("amount", expense.TotalAmount),
            ("reimbursableAmount", expense.ReimbursableAmount), ("billingTreatment", expense.BillingTreatment),
            ("uploadedAt", expense.UploadedAt)))).Where(row => MatchesSearch(row, request.Search)).ToArray();

    private static Dictionary<string, object?>[] SellContext(FinancialOperationsProject[] projects) => projects.Select(project => Row(
        ("projectId", project.ProjectId), ("customer", project.CustomerName), ("projectCode", project.ProjectCode),
        ("projectName", project.ProjectName), ("projectManager", project.ProjectManagerName),
        ("accountExecutive", project.AccountExecutive?.DisplayName), ("contractType", project.ContractType),
        ("sellQuoteNumber", project.SellQuoteNumber), ("billingMethod", project.BillingMethod),
        ("commercialSource", project.CommercialSource), ("connectorReady", project.SellConnectorReady),
        ("readinessStatus", project.SellReadinessStatus), ("lastSuccessfulSyncAt", project.LastSuccessfulSellSyncAt))).ToArray();

    private static Dictionary<string, object?>[] BillingReadiness(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental)
    {
        var approved = AggregateHours(supplemental.Rows("approved_time_entries"), projects, true);
        var reviews = supplemental.Rows("billing_readiness_reviews");
        return projects.Select(project =>
        {
            approved.TryGetValue(project.ProjectId, out var approvedHours);
            var review = LatestForProject(reviews, project.ProjectId);
            var packageStatus = review.HasValue ? Text(review.Value, "review_status", "status") : "not_recorded";
            var blockers = new List<string>();
            if (approvedHours <= 0) blockers.Add("No approved invoice-eligible time.");
            if (review is null) blockers.Add("No billing-readiness review.");
            if (project.Missing.Length > 0) blockers.Add($"Missing: {string.Join(", ", project.Missing)}.");
            if (project.SellReadinessStatus.Contains("not", StringComparison.OrdinalIgnoreCase)) blockers.Add($"SELL: {project.SellReadinessStatus}.");
            return Row(
                ("projectId", project.ProjectId), ("customer", project.CustomerName), ("projectCode", project.ProjectCode),
                ("projectName", project.ProjectName), ("approvedHours", approvedHours),
                ("currentExpenses", project.UploadedExpenses), ("packageStatus", packageStatus),
                ("forecastedFinalCost", project.ForecastedFinalCost), ("currentVariance", project.CurrentVariance),
                ("sellReadiness", project.SellReadinessStatus), ("blockers", string.Join("; ", blockers)));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] CloseoutReadiness(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental)
    {
        var closeouts = supplemental.Rows("project_closeout_records");
        var reviews = supplemental.Rows("billing_readiness_reviews");
        var approved = AggregateHours(supplemental.Rows("approved_time_entries"), projects, true);
        var notifications = supplemental.Rows("project_notification_dispatches");
        return projects.Select(project =>
        {
            var closeout = LatestForProject(closeouts, project.ProjectId);
            var review = LatestForProject(reviews, project.ProjectId);
            var notification = LatestForProject(notifications, project.ProjectId);
            approved.TryGetValue(project.ProjectId, out var approvedHours);
            var blockers = new List<string>();
            var closeoutStatus = closeout.HasValue ? Text(closeout.Value, "closeout_status", "status") : "not_started";
            var billingStatus = review.HasValue ? Text(review.Value, "review_status", "status") : "not_recorded";
            if (closeoutStatus != "closed") blockers.Add("Closeout is not closed.");
            if (!billingStatus.Equals("ready", StringComparison.OrdinalIgnoreCase)) blockers.Add("Billing readiness is not ready.");
            if (project.OpenAlertCount > 0) blockers.Add($"{project.OpenAlertCount} cost alert(s) remain open.");
            if (project.Missing.Length > 0) blockers.Add($"Missing: {string.Join(", ", project.Missing)}.");
            return Row(
                ("projectId", project.ProjectId), ("customer", project.CustomerName), ("projectCode", project.ProjectCode),
                ("projectName", project.ProjectName), ("projectStatus", project.ProjectStatus),
                ("closeoutStatus", closeoutStatus),
                ("billingDisposition", closeout.HasValue ? Text(closeout.Value, "billing_disposition") : "not_recorded"),
                ("billingReadiness", billingStatus), ("approvedHours", approvedHours),
                ("openAlerts", project.OpenAlertCount),
                ("notificationStatus", notification.HasValue ? Text(notification.Value, "delivery_status", "notification_status") : "not_recorded"),
                ("blockers", string.Join("; ", blockers)));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] NotificationDelivery(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental,
        EnterpriseReportRequest request)
    {
        var projectMap = projects.ToDictionary(project => project.ProjectId);
        return supplemental.Rows("project_notification_dispatches")
            .Where(row => WithinDate(row, request, "created_at", "sent_at"))
            .Where(row => string.IsNullOrWhiteSpace(request.ModuleCode)
                || Text(row, "source_module").Equals(request.ModuleCode, StringComparison.OrdinalIgnoreCase))
            .Where(row => string.IsNullOrWhiteSpace(request.Severity)
                || Text(row, "alert_severity", "severity").Equals(request.Severity, StringComparison.OrdinalIgnoreCase))
            .Where(row => string.IsNullOrWhiteSpace(request.WorkflowStatus)
                || Text(row, "delivery_status", "status").Equals(request.WorkflowStatus, StringComparison.OrdinalIgnoreCase))
            .Select(row =>
            {
                var projectId = GuidValue(row, "project_id");
                projectMap.TryGetValue(projectId ?? Guid.Empty, out var project);
                return Row(
                    ("createdAt", DateTimeValue(row, "created_at")), ("customer", project?.CustomerName),
                    ("projectCode", project?.ProjectCode), ("notificationType", Text(row, "notification_type")),
                    ("sourceModule", Text(row, "source_module")), ("severity", Text(row, "alert_severity", "severity")),
                    ("recipientCount", DecimalValue(row, "recipient_count") ?? 0),
                    ("deliveryBoundary", Text(row, "delivery_boundary")), ("deliveryStatus", Text(row, "delivery_status")),
                    ("diagnosticCode", Text(row, "last_error_code", "diagnostic_code")), ("sentAt", DateTimeValue(row, "sent_at")));
            }).ToArray();
    }

    private static Dictionary<string, object?>[] ExecutiveSummaryDashboard(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var people = projects.SelectMany(project => project.Engineers.Select(engineer => engineer.UserId))
            .Concat(projects.Where(project => project.ProjectManagerUserId.HasValue).Select(project => project.ProjectManagerUserId!.Value))
            .Distinct().Count();
        var currentVariance = projects.Where(project => project.CurrentVariance.HasValue)
            .Sum(project => project.CurrentVariance ?? 0m);
        var totalUsed = projects.Sum(project => project.UsedHours);
        var billableUsed = projects.Where(project => project.Billable).Sum(project => project.UsedHours);
        var utilization = totalUsed > 0 ? billableUsed / totalUsed * 100m : (decimal?)null;
        var closeouts = context.Supplemental.Rows("project_closeout_records");
        return
        [
            Row(
                ("visibleProjects", projects.Length),
                ("activeProjects", projects.Count(project => !IsClosedProject(project.ProjectStatus))),
                ("customers", projects.Select(project => project.CustomerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
                ("engineers", people),
                ("plannedHours", projects.Sum(project => project.PlannedHours)),
                ("usedHours", totalUsed),
                ("remainingHours", projects.Sum(project => project.RemainingHours)),
                ("billableUtilization", utilization),
                ("atRiskProjects", projects.Count(project => project.BudgetStatus is "approaching_budget" or "over_budget" || project.HighAlertCount > 0)),
                ("overBudgetProjects", projects.Count(project => project.BudgetStatus == "over_budget")),
                ("currentVariance", currentVariance),
                ("closeoutPending", projects.Count(project => !HasClosedCloseout(closeouts, project.ProjectId))),
                ("dataAsOf", projects.Select(project => project.CalculatedAt).DefaultIfEmpty(DateTimeOffset.UtcNow).Max()))
        ];
    }

    private static Dictionary<string, object?>[] AccountingInvoiceDetail(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental,
        EnterpriseReportRequest request)
    {
        var projectMap = projects.ToDictionary(project => project.ProjectId);
        var rows = supplemental.Rows("client_invoices")
            .Select(row => (Row: row, Source: "client_invoices"))
            .Concat(supplemental.Rows("billing_invoices").Select(row => (Row: row, Source: "billing_invoices")))
            .Where(item => WithinDate(item.Row, request, "invoice_date", "created_at", "billing_period_start"))
            .Select(item =>
            {
                var projectId = GuidValue(item.Row, "project_id", "affected_project_id");
                projectMap.TryGetValue(projectId ?? Guid.Empty, out var project);
                return Row(
                    ("customer", project?.CustomerName ?? Text(item.Row, "customer_name", "customer")),
                    ("projectCode", project?.ProjectCode ?? Text(item.Row, "project_code")),
                    ("projectName", project?.ProjectName ?? Text(item.Row, "project_name")),
                    ("invoiceNumber", Text(item.Row, "invoice_number", "billing_invoice_number")),
                    ("invoiceStatus", Text(item.Row, "invoice_status", "status")),
                    ("billingPeriodStart", DateValue(item.Row, "billing_period_start", "period_start")),
                    ("billingPeriodEnd", DateValue(item.Row, "billing_period_end", "period_end")),
                    ("laborAmount", DecimalValue(item.Row, "labor_amount", "labor_total")),
                    ("expenseAmount", DecimalValue(item.Row, "expense_amount", "expense_total")),
                    ("invoiceTotal", DecimalValue(item.Row, "invoice_total", "total_amount", "amount")),
                    ("invoiceDate", DateValue(item.Row, "invoice_date", "sent_at", "created_at")),
                    ("source", item.Source));
            })
            .Where(row => MatchesSearch(row, request.Search))
            .ToArray();
        return rows;
    }

    private static Dictionary<string, object?>[] TmSales(FinancialOperationsProject[] projects) => projects
        .Where(project => IsTimeAndMaterial(project.ContractType))
        .Select(project => Row(
            ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
            ("projectManager", project.ProjectManagerName), ("contractType", project.ContractType),
            ("sellQuoteNumber", project.SellQuoteNumber), ("usedHours", project.UsedHours),
            ("contractedValue", project.ContractedValue), ("laborCost", project.LaborCost),
            ("currentVariance", project.CurrentVariance), ("sellReadiness", project.SellReadinessStatus)))
        .ToArray();

    private static Dictionary<string, object?>[] ProjectStatusBilledBalance(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental)
    {
        var billed = InvoiceAmountsByProject(supplemental);
        return projects.Select(project =>
        {
            billed.TryGetValue(project.ProjectId, out var billedAmount);
            var plannedCost = NullableAdd(project.LaborBudget, project.ExpenseBudget);
            var remaining = project.ContractedValue.HasValue ? project.ContractedValue.Value - billedAmount : (decimal?)null;
            return Row(
                ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("projectStatus", project.ProjectStatus), ("projectManager", project.ProjectManagerName),
                ("plannedCost", plannedCost), ("committedCost", project.CommittedCost), ("billedAmount", billedAmount),
                ("remainingBalance", remaining), ("forecastedFinalCost", project.ForecastedFinalCost),
                ("currentVariance", project.CurrentVariance), ("budgetStatus", project.BudgetStatus));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] CertifyExpenseInvoiceBreakdown(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental,
        EnterpriseReportRequest request)
    {
        var invoices = supplemental.Rows("client_invoices").Concat(supplemental.Rows("billing_invoices")).ToArray();
        return projects.SelectMany(project => project.Expenses
            .Where(expense => !request.EngineerUserId.HasValue || expense.OwnerUserId == request.EngineerUserId.Value)
            .Where(expense => !request.DateFrom.HasValue || !expense.PeriodEnd.HasValue || expense.PeriodEnd.Value >= request.DateFrom.Value)
            .Where(expense => !request.DateTo.HasValue || !expense.PeriodStart.HasValue || expense.PeriodStart.Value <= request.DateTo.Value)
            .Select(expense =>
            {
                var invoice = invoices.Where(row => GuidValue(row, "project_id") == project.ProjectId)
                    .OrderByDescending(row => DateTimeValue(row, "invoice_date", "created_at") ?? DateTimeOffset.MinValue)
                    .Cast<JsonElement?>().FirstOrDefault();
                return Row(
                    ("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                    ("expenseOwner", expense.OwnerName), ("expenseAmount", expense.TotalAmount),
                    ("reimbursableAmount", expense.ReimbursableAmount), ("billingTreatment", expense.BillingTreatment),
                    ("invoiceNumber", invoice.HasValue ? Text(invoice.Value, "invoice_number", "billing_invoice_number") : string.Empty),
                    ("invoiceStatus", invoice.HasValue ? Text(invoice.Value, "invoice_status", "status") : "not_invoiced"),
                    ("invoiceTotal", invoice.HasValue ? DecimalValue(invoice.Value, "invoice_total", "total_amount", "amount") : null),
                    ("periodStart", expense.PeriodStart), ("periodEnd", expense.PeriodEnd));
            })).Where(row => MatchesSearch(row, request.Search)).ToArray();
    }

    private static Dictionary<string, object?>[] EngineerProjectOverUnder(
        FinancialOperationsProject[] projects,
        EnterpriseReportRequest request) => projects
        .SelectMany(project => project.Engineers
            .Where(engineer => !request.EngineerUserId.HasValue || engineer.UserId == request.EngineerUserId.Value)
            .Select(engineer =>
            {
                var variance = engineer.AssignedHours - engineer.UsedHours;
                var hoursStatus = variance < 0 ? "over" : variance == 0 ? "at_plan" : "under";
                var financialVariance = project.CurrentVariance;
                var financialStatus = !financialVariance.HasValue ? "not_available"
                    : financialVariance.Value < 0 ? "over" : financialVariance.Value == 0 ? "at_plan" : "under";
                return Row(
                    ("engineer", engineer.DisplayName), ("customer", project.CustomerName), ("projectCode", project.ProjectCode),
                    ("projectName", project.ProjectName), ("projectManager", project.ProjectManagerName),
                    ("assignedHours", engineer.AssignedHours), ("usedHours", engineer.UsedHours),
                    ("remainingHours", variance), ("hoursVariance", variance), ("hoursStatus", hoursStatus),
                    ("laborBudget", project.LaborBudget), ("laborCost", project.LaborCost),
                    ("financialVariance", financialVariance), ("financialStatus", financialStatus), ("dataAsOf", project.CalculatedAt));
            }))
        .OrderBy(row => row["engineer"])
        .ThenBy(row => row["projectName"])
        .ToArray();

    private static Dictionary<string, object?>[] UtilizationOverUnder(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var projectMap = projects.ToDictionary(project => project.ProjectId);
        var people = People(context);
        var entries = context.Supplemental.Rows("time_entries")
            .Where(row => WithinDate(row, request, "work_date", "entry_date", "date"))
            .Select(row => new
            {
                UserId = GuidValue(row, "user_id", "engineer_user_id"),
                ProjectId = GuidValue(row, "project_id"),
                Hours = DecimalValue(row, "hours", "duration_hours") ?? 0m
            })
            .Where(row => row.UserId.HasValue)
            .ToArray();
        var targetRows = context.Supplemental.Rows("utilization_targets");
        var assignmentPeople = projects.SelectMany(project => project.Engineers.Select(engineer => engineer.UserId));
        var userIds = entries.Select(row => row.UserId!.Value).Concat(assignmentPeople).Distinct()
            .Where(id => !request.EngineerUserId.HasValue || id == request.EngineerUserId.Value).ToArray();
        return userIds.Select(userId =>
        {
            var currentRows = entries.Where(row => row.UserId == userId).ToArray();
            var currentBillable = currentRows.Where(item => item.ProjectId.HasValue
                && projectMap.TryGetValue(item.ProjectId.Value, out var project) && project.Billable).Sum(item => item.Hours);
            var currentEligible = currentRows.Sum(item => item.Hours);
            var currentNonBillable = currentEligible - currentBillable;
            var assigned = projects.SelectMany(project => project.Engineers
                .Where(engineer => engineer.UserId == userId)
                .Select(engineer => new { Project = project, Remaining = Math.Max(0m, engineer.AssignedHours - engineer.UsedHours) }))
                .ToArray();
            var remainingAssigned = assigned.Sum(item => item.Remaining);
            var remainingBillable = assigned.Where(item => item.Project.Billable).Sum(item => item.Remaining);
            var projectedBillable = currentBillable + remainingBillable;
            var projectedEligible = currentEligible + remainingAssigned;
            var target = TargetPercentage(targetRows, userId);
            var currentUtilization = currentEligible > 0 ? currentBillable / currentEligible * 100m : (decimal?)null;
            var projectedUtilization = projectedEligible > 0 ? projectedBillable / projectedEligible * 100m : (decimal?)null;
            people.TryGetValue(userId, out var name);
            return Row(
                ("engineer", name ?? userId.ToString()), ("period", Period(request)), ("targetPercentage", target),
                ("currentEligibleHours", currentEligible), ("currentBillableHours", currentBillable),
                ("currentNonBillableHours", currentNonBillable), ("currentUtilization", currentUtilization),
                ("currentOverUnder", currentUtilization.HasValue ? currentUtilization.Value - target : null),
                ("currentStatus", UtilizationStatus(currentUtilization, target)), ("remainingAssignedHours", remainingAssigned),
                ("projectedBillableHours", projectedBillable), ("projectedEligibleHours", projectedEligible),
                ("projectedUtilization", projectedUtilization),
                ("projectedOverUnder", projectedUtilization.HasValue ? projectedUtilization.Value - target : null),
                ("projectedStatus", UtilizationStatus(projectedUtilization, target)),
                ("utilizationChange", currentUtilization.HasValue && projectedUtilization.HasValue ? projectedUtilization.Value - currentUtilization.Value : null));
        }).OrderBy(row => row["engineer"]).ToArray();
    }

    private static Dictionary<string, object?>[] VacationPtoUsed(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var people = People(context);
        var directoryPeople = context.Supplemental.Rows("app_users")
            .Select(row => new
            {
                UserId = GuidValue(row, "user_id"),
                DisplayName = Text(row, "display_name", "name"),
                Department = Text(row, "department", "team_name")
            })
            .Where(row => row.UserId.HasValue)
            .ToDictionary(row => row.UserId!.Value, row => row);
        foreach (var person in directoryPeople)
        {
            if (!people.ContainsKey(person.Key) && !string.IsNullOrWhiteSpace(person.Value.DisplayName))
                people[person.Key] = person.Value.DisplayName;
        }
        var categories = context.Supplemental.Rows("non_project_time_categories")
            .Select(row => new
            {
                Id = GuidValue(row, "non_project_time_category_id", "category_id"),
                Name = Text(row, "category_name", "name"),
                Code = Text(row, "category_code", "code"),
                Classification = Text(row, "utilization_classification", "classification")
            })
            .Where(row => row.Id.HasValue)
            .ToDictionary(row => row.Id!.Value, row => $"{row.Code} {row.Name} {row.Classification}");
        var reporting = context.Supplemental.Rows("reporting_relationships")
            .Select(row => new
            {
                Employee = GuidValue(row, "employee_user_id", "user_id"),
                Manager = GuidValue(row, "manager_user_id")
            })
            .Where(row => row.Employee.HasValue)
            .GroupBy(row => row.Employee!.Value)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Manager).FirstOrDefault(value => value.HasValue));
        var teamNames = context.Supplemental.Rows("teams")
            .Select(row => new { Id = GuidValue(row, "team_id"), Name = Text(row, "team_name", "name") })
            .Where(row => row.Id.HasValue)
            .ToDictionary(row => row.Id!.Value, row => row.Name);
        var memberships = context.Supplemental.Rows("team_memberships")
            .Select(row => new { UserId = GuidValue(row, "user_id"), TeamId = GuidValue(row, "team_id") })
            .Where(row => row.UserId.HasValue && row.TeamId.HasValue)
            .GroupBy(row => row.UserId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(row => row.TeamId!.Value).First());
        var yearStart = new DateOnly((request.DateTo ?? DateOnly.FromDateTime(DateTime.UtcNow)).Year, 1, 1);
        var entries = context.Supplemental.Rows("time_entries")
            .Where(row => IsVacationPto(row, categories))
            .Select(row =>
            {
                var userId = GuidValue(row, "user_id", "engineer_user_id");
                var manager = Text(row, "manager_name", "supervisor_name");
                var team = Text(row, "team_name", "department_name");
                if (userId.HasValue)
                {
                    if (string.IsNullOrWhiteSpace(manager)
                        && reporting.TryGetValue(userId.Value, out var managerId)
                        && managerId.HasValue)
                    {
                        manager = people.TryGetValue(managerId.Value, out var managerName)
                            ? managerName
                            : directoryPeople.TryGetValue(managerId.Value, out var managerPerson)
                                ? managerPerson.DisplayName
                                : string.Empty;
                    }
                    if (string.IsNullOrWhiteSpace(team)
                        && memberships.TryGetValue(userId.Value, out var teamId)
                        && teamNames.TryGetValue(teamId, out var mappedTeam))
                    {
                        team = mappedTeam;
                    }
                    if (string.IsNullOrWhiteSpace(team)
                        && directoryPeople.TryGetValue(userId.Value, out var person))
                    {
                        team = person.Department;
                    }
                }
                return new
                {
                    UserId = userId,
                    Date = DateValue(row, "work_date", "entry_date", "date"),
                    Hours = DecimalValue(row, "hours", "duration_hours") ?? 0m,
                    Status = Text(row, "status", "workflow_status"),
                    Manager = manager,
                    Team = team
                };
            })
            .Where(row => row.UserId.HasValue && row.Date.HasValue)
            .Where(row => !request.EngineerUserId.HasValue || row.UserId == request.EngineerUserId.Value)
            .ToArray();
        return entries.Where(row => (!request.DateFrom.HasValue || row.Date!.Value >= request.DateFrom.Value)
                && (!request.DateTo.HasValue || row.Date!.Value <= request.DateTo.Value))
            .GroupBy(row => row.UserId!.Value)
            .Select(group =>
            {
                people.TryGetValue(group.Key, out var name);
                var submitted = group.Sum(item => item.Hours);
                var approved = group.Where(item => ApprovedStatus(item.Status)).Sum(item => item.Hours);
                var ytd = entries.Where(item => item.UserId == group.Key && item.Date!.Value >= yearStart
                    && (!request.DateTo.HasValue || item.Date!.Value <= request.DateTo.Value)).Sum(item => item.Hours);
                return Row(
                    ("engineer", name ?? group.Key.ToString()),
                    ("manager", group.Select(item => item.Manager).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Not available from the current time-entry source"),
                    ("team", group.Select(item => item.Team).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Not available from the current time-entry source"),
                    ("firstDate", group.Min(item => item.Date)), ("latestDate", group.Max(item => item.Date)),
                    ("submittedHours", submitted), ("approvedHours", approved), ("pendingHours", submitted - approved),
                    ("equivalentDays", submitted / 8m), ("yearToDateHours", ytd), ("entryCount", group.Count()));
            }).OrderBy(row => row["engineer"]).ToArray();
    }

    private static Dictionary<string, object?>[] BillableVsNonBillable(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var projectMap = projects.ToDictionary(project => project.ProjectId);
        var people = People(context);
        var targets = context.Supplemental.Rows("utilization_targets");
        return context.Supplemental.Rows("time_entries")
            .Where(row => WithinDate(row, request, "work_date", "entry_date", "date"))
            .Select(row => new { UserId = GuidValue(row, "user_id", "engineer_user_id"), ProjectId = GuidValue(row, "project_id"), Hours = DecimalValue(row, "hours", "duration_hours") ?? 0m })
            .Where(row => row.UserId.HasValue && (!request.EngineerUserId.HasValue || row.UserId == request.EngineerUserId.Value))
            .GroupBy(row => row.UserId!.Value)
            .Select(group =>
            {
                var billable = group.Where(item => item.ProjectId.HasValue && projectMap.TryGetValue(item.ProjectId.Value, out var project) && project.Billable).Sum(item => item.Hours);
                var total = group.Sum(item => item.Hours);
                var percentage = total > 0 ? billable / total * 100m : (decimal?)null;
                var target = TargetPercentage(targets, group.Key);
                people.TryGetValue(group.Key, out var name);
                return Row(("engineer", name ?? group.Key.ToString()), ("period", Period(request)),
                    ("billableHours", billable), ("nonBillableHours", total - billable), ("totalHours", total),
                    ("billablePercentage", percentage), ("targetPercentage", target),
                    ("variancePercentage", percentage.HasValue ? percentage.Value - target : null),
                    ("status", UtilizationStatus(percentage, target)));
            }).OrderBy(row => row["engineer"]).ToArray();
    }

    private static Dictionary<string, object?>[] UnbilledTimeInvoiceReadiness(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental)
    {
        var approved = AggregateHours(supplemental.Rows("approved_time_entries"), projects, true);
        var billed = AggregateDecimalByProject(supplemental.Rows("billing_invoice_lines"), "hours", "quantity_hours", "quantity");
        var reviews = supplemental.Rows("billing_readiness_reviews");
        return projects.Select(project =>
        {
            approved.TryGetValue(project.ProjectId, out var approvedHours);
            billed.TryGetValue(project.ProjectId, out var billedHours);
            var review = LatestForProject(reviews, project.ProjectId);
            var readiness = review.HasValue ? Text(review.Value, "review_status", "status") : "not_recorded";
            var blockers = new List<string>();
            if (approvedHours <= billedHours) blockers.Add("No unbilled approved time.");
            if (readiness != "ready") blockers.Add($"Billing readiness is {readiness}.");
            if (project.Missing.Length > 0) blockers.Add($"Commercial data incomplete: {string.Join(", ", project.Missing)}.");
            return Row(("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
                ("approvedHours", approvedHours), ("billedHours", billedHours), ("unbilledHours", Math.Max(0m, approvedHours - billedHours)),
                ("currentExpenses", project.UploadedExpenses), ("billingReadiness", readiness), ("invoiceBlockers", string.Join(" ", blockers)));
        }).ToArray();
    }

    private static Dictionary<string, object?>[] ApprovalBottleneck(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var people = People(context);
        var projectMap = projects.ToDictionary(project => project.ProjectId);
        var now = DateTimeOffset.UtcNow;
        return context.Supplemental.Rows("timesheet_day_statuses")
            .Where(row => WithinDate(row, request, "work_date", "submitted_at", "updated_at"))
            .Where(row => !ApprovedStatus(Text(row, "status")))
            .Select(row =>
            {
                var userId = GuidValue(row, "user_id", "engineer_user_id");
                var projectId = GuidValue(row, "project_id");
                projectMap.TryGetValue(projectId ?? Guid.Empty, out var project);
                people.TryGetValue(userId ?? Guid.Empty, out var engineer);
                var submitted = DateTimeValue(row, "submitted_at", "updated_at", "created_at");
                var age = submitted.HasValue ? Math.Max(0, (int)(now - submitted.Value).TotalDays) : 0;
                var status = Text(row, "status", "workflow_status");
                return Row(("engineer", engineer ?? Text(row, "user_name", "engineer_name")),
                    ("projectCode", project?.ProjectCode), ("projectName", project?.ProjectName),
                    ("approvalStage", ApprovalStage(status)), ("approvalOwner", ApprovalOwner(status, project)),
                    ("status", status), ("hours", DecimalValue(row, "hours", "total_hours") ?? 0m),
                    ("submittedAt", submitted), ("ageDays", age), ("escalationStatus", age >= 3 ? "overdue" : "within_window"));
            }).Where(row => MatchesSearch(row, request.Search)).OrderByDescending(row => row["ageDays"]).ToArray();
    }

    private static Dictionary<string, object?>[] MissingLateTimesheet(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var people = People(context);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return context.Supplemental.Rows("timesheet_day_statuses")
            .Where(row => WithinDate(row, request, "work_date", "date"))
            .Select(row =>
            {
                var userId = GuidValue(row, "user_id", "engineer_user_id");
                if (request.EngineerUserId.HasValue && userId != request.EngineerUserId) return null;
                var workDate = DateValue(row, "work_date", "date");
                var recorded = DecimalValue(row, "recorded_hours", "hours", "total_hours") ?? 0m;
                var expected = DecimalValue(row, "expected_hours") ?? 8m;
                var status = Text(row, "status", "workflow_status");
                if (ApprovedStatus(status) && recorded >= expected) return null;
                people.TryGetValue(userId ?? Guid.Empty, out var name);
                return Row(("engineer", name ?? Text(row, "user_name", "engineer_name")), ("workDate", workDate),
                    ("weekStart", workDate.HasValue ? StartOfWeek(workDate.Value) : null), ("status", status),
                    ("recordedHours", recorded), ("expectedHours", expected), ("missingHours", Math.Max(0m, expected - recorded)),
                    ("lateDays", workDate.HasValue && workDate.Value < today ? today.DayNumber - workDate.Value.DayNumber : 0),
                    ("manager", Text(row, "manager_name", "supervisor_name")),
                    ("team", Text(row, "team_name", "department_name")));
            })
            .Where(row => row is not null)
            .Select(row => row!)
            .Where(row => MatchesSearch(row, request.Search))
            .OrderByDescending(row => row["lateDays"]).ToArray();
    }

    private static Dictionary<string, object?>[] ProjectMargin(FinancialOperationsProject[] projects) => projects.Select(project =>
    {
        var currentMargin = project.ContractedValue.HasValue && project.CommittedCost.HasValue
            ? project.ContractedValue.Value - project.CommittedCost.Value : (decimal?)null;
        var forecastMargin = project.ContractedValue.HasValue && project.ForecastedFinalCost.HasValue
            ? project.ContractedValue.Value - project.ForecastedFinalCost.Value : (decimal?)null;
        var percentage = project.ContractedValue is > 0 && forecastMargin.HasValue
            ? forecastMargin.Value / project.ContractedValue.Value * 100m : (decimal?)null;
        return Row(("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
            ("contractedValue", project.ContractedValue), ("committedCost", project.CommittedCost),
            ("forecastedFinalCost", project.ForecastedFinalCost), ("currentMargin", currentMargin),
            ("forecastMargin", forecastMargin), ("forecastMarginPercentage", percentage),
            ("status", !percentage.HasValue ? "not_available" : percentage.Value < 0 ? "negative_margin" : percentage.Value < 15 ? "margin_attention" : "healthy_margin"));
    }).ToArray();

    private static Dictionary<string, object?>[] RateAmountExceptions(FinancialOperationsProject[] projects) => projects
        .SelectMany(project =>
        {
            var exceptions = new List<(string Type, string Detail, string Severity)>();
            if (!project.RateContext) exceptions.Add(("rate_context_missing", "No governed rate context is available.", "high"));
            if (!project.ContractedValue.HasValue) exceptions.Add(("contracted_value_missing", "Contracted value is not available.", "warning"));
            if (!project.CommittedCost.HasValue) exceptions.Add(("committed_cost_missing", "Committed cost is not available.", "warning"));
            if (project.CurrentVariance is < 0) exceptions.Add(("negative_variance", "Current project variance is negative.", "high"));
            if (!project.SellConnectorReady) exceptions.Add(("sell_not_ready", project.SellReadinessStatus, "warning"));
            return exceptions.Select(exception => Row(("customer", project.CustomerName), ("projectCode", project.ProjectCode),
                ("projectName", project.ProjectName), ("exceptionType", exception.Type), ("exceptionDetail", exception.Detail),
                ("severity", exception.Severity), ("contractedValue", project.ContractedValue), ("committedCost", project.CommittedCost),
                ("currentVariance", project.CurrentVariance), ("sellReadiness", project.SellReadinessStatus)));
        }).ToArray();

    private static Dictionary<string, object?>[] CustomerProfitability(FinancialOperationsProject[] projects) => projects
        .GroupBy(project => project.CustomerName, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var contracted = group.Where(project => project.ContractedValue.HasValue).Sum(project => project.ContractedValue ?? 0m);
            var committed = group.Where(project => project.CommittedCost.HasValue).Sum(project => project.CommittedCost ?? 0m);
            var forecast = group.Where(project => project.ForecastedFinalCost.HasValue).Sum(project => project.ForecastedFinalCost ?? 0m);
            var margin = contracted > 0 ? contracted - forecast : (decimal?)null;
            var percentage = contracted > 0 && margin.HasValue ? margin.Value / contracted * 100m : (decimal?)null;
            return Row(("customer", group.Key), ("projectCount", group.Count()), ("contractedValue", contracted),
                ("committedCost", committed), ("forecastedFinalCost", forecast), ("forecastMargin", margin),
                ("forecastMarginPercentage", percentage),
                ("atRiskProjects", group.Count(project => project.BudgetStatus is "approaching_budget" or "over_budget" || project.HighAlertCount > 0)),
                ("status", !percentage.HasValue ? "not_available" : percentage.Value < 0 ? "negative" : percentage.Value < 15 ? "attention" : "healthy"));
        }).OrderBy(row => row["customer"]).ToArray();

    private static Dictionary<string, object?>[] SalesDeliveryHandoffQuality(FinancialOperationsProject[] projects) => projects.Select(project =>
    {
        var gaps = new List<string>();
        if (string.IsNullOrWhiteSpace(project.SellQuoteNumber)) gaps.Add("SELL quote");
        if (project.ProjectManagerUserId is null) gaps.Add("Project Manager");
        if (project.AccountExecutive is null) gaps.Add("Account Executive");
        if (project.SolutionArchitect is null) gaps.Add("Solution Architect");
        if (project.Engineers.Length == 0) gaps.Add("Engineer assignment");
        if (project.Missing.Any(value => value.Contains("document", StringComparison.OrdinalIgnoreCase))) gaps.Add("Project documents");
        var totalChecks = 6m;
        var score = Math.Max(0m, (totalChecks - gaps.Count) / totalChecks * 100m);
        return Row(("customer", project.CustomerName), ("projectCode", project.ProjectCode), ("projectName", project.ProjectName),
            ("projectManager", project.ProjectManagerName), ("accountExecutive", project.AccountExecutive?.DisplayName),
            ("solutionArchitect", project.SolutionArchitect?.DisplayName), ("sellQuoteNumber", project.SellQuoteNumber),
            ("sellReadiness", project.SellReadinessStatus), ("assignmentReady", project.ProjectManagerUserId.HasValue && project.Engineers.Length > 0),
            ("documentReady", !project.Missing.Any(value => value.Contains("document", StringComparison.OrdinalIgnoreCase))),
            ("handoffScore", score), ("gaps", string.Join(", ", gaps)));
    }).ToArray();

    private static Dictionary<string, object?>[] CustomerBillingSummary(
        FinancialOperationsProject[] projects,
        EnterpriseReportingSupplemental supplemental)
    {
        var approved = AggregateHours(supplemental.Rows("approved_time_entries"), projects, true);
        var invoices = InvoiceAmountsByProject(supplemental);
        var reviews = supplemental.Rows("billing_readiness_reviews");
        return projects.GroupBy(project => project.CustomerName, StringComparer.OrdinalIgnoreCase).Select(group =>
        {
            var approvedHours = group.Sum(project => approved.GetValueOrDefault(project.ProjectId));
            var invoiced = group.Sum(project => invoices.GetValueOrDefault(project.ProjectId));
            var expenses = group.Sum(project => project.UploadedExpenses ?? 0m);
            var contracted = group.Sum(project => project.ContractedValue ?? 0m);
            var ready = group.Count(project => LatestForProject(reviews, project.ProjectId) is { } review
                && Text(review, "review_status", "status").Equals("ready", StringComparison.OrdinalIgnoreCase));
            var blocked = group.Count() - ready;
            return Row(("customer", group.Key), ("projectCount", group.Count()), ("approvedHours", approvedHours),
                ("currentExpenses", expenses), ("invoicedAmount", invoiced),
                ("unbilledExposure", contracted > 0 ? Math.Max(0m, contracted - invoiced) : (decimal?)null),
                ("readyProjects", ready), ("blockedProjects", blocked), ("status", blocked == 0 ? "ready" : ready > 0 ? "partial" : "blocked"));
        }).OrderBy(row => row["customer"]).ToArray();
    }

    private static Dictionary<string, object?>[] SelectedEngineers(
        FinancialOperationsProject[] projects,
        EnterpriseReportRequest request) => projects.SelectMany(project => project.Engineers)
        .Where(engineer => !request.EngineerUserId.HasValue || engineer.UserId == request.EngineerUserId.Value)
        .GroupBy(engineer => engineer.UserId)
        .Select(group =>
        {
            var related = projects.Where(project => project.Engineers.Any(engineer => engineer.UserId == group.Key)).ToArray();
            return Row(("engineer", group.First().DisplayName), ("email", group.First().Email), ("projectCount", related.Length),
                ("assignedHours", group.Sum(engineer => engineer.AssignedHours)), ("usedHours", group.Sum(engineer => engineer.UsedHours)),
                ("remainingHours", group.Sum(engineer => engineer.AssignedHours - engineer.UsedHours)),
                ("billableProjects", related.Count(project => project.Billable)),
                ("projectManagers", string.Join(", ", related.Select(project => project.ProjectManagerName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())),
                ("customers", string.Join(", ", related.Select(project => project.CustomerName).Distinct(StringComparer.OrdinalIgnoreCase))));
        }).OrderBy(row => row["engineer"]).ToArray();

    private static Dictionary<string, object?>[] TeamReport(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var selectedUserIds = projects
            .SelectMany(project => project.Engineers)
            .Where(engineer => !request.EngineerUserId.HasValue || engineer.UserId == request.EngineerUserId.Value)
            .Select(engineer => engineer.UserId)
            .Distinct()
            .ToHashSet();
        var selectedProjects = projects
            .Where(project => project.Engineers.Any(engineer => selectedUserIds.Contains(engineer.UserId)))
            .ToArray();
        var assignments = selectedProjects
            .SelectMany(project => project.Engineers
                .Where(engineer => selectedUserIds.Contains(engineer.UserId))
                .Select(engineer => new
                {
                    Project = project,
                    engineer.AssignedHours,
                    engineer.UsedHours
                }))
            .ToArray();
        var totalAssigned = assignments.Sum(item => item.AssignedHours);

        var periodEntries = context.Supplemental.Rows("time_entries")
            .Where(row => WithinDate(row, request, "work_date", "entry_date", "date"))
            .Select(row => new
            {
                UserId = GuidValue(row, "user_id", "engineer_user_id"),
                ProjectId = GuidValue(row, "project_id"),
                Hours = DecimalValue(row, "hours", "duration_hours") ?? 0m
            })
            .Where(row => row.UserId.HasValue && selectedUserIds.Contains(row.UserId.Value))
            .ToArray();
        var periodRequested = request.DateFrom.HasValue || request.DateTo.HasValue;
        var usePeriodEntries = periodRequested || periodEntries.Length > 0;
        var projectMap = selectedProjects.ToDictionary(project => project.ProjectId);
        var totalUsed = usePeriodEntries
            ? periodEntries.Sum(item => item.Hours)
            : assignments.Sum(item => item.UsedHours);
        var billable = usePeriodEntries
            ? periodEntries.Where(item => item.ProjectId.HasValue
                    && projectMap.TryGetValue(item.ProjectId.Value, out var project)
                    && project.Billable)
                .Sum(item => item.Hours)
            : assignments.Where(item => item.Project.Billable).Sum(item => item.UsedHours);
        var utilization = totalUsed > 0 ? billable / totalUsed * 100m : (decimal?)null;
        return [Row(("team", context.Actor.Broad ? "Authorized organization scope" : "Authorized team scope"),
            ("people", selectedUserIds.Count), ("projects", selectedProjects.Length), ("assignedHours", totalAssigned),
            ("usedHours", totalUsed), ("remainingHours", totalAssigned - totalUsed), ("billableHours", billable),
            ("nonBillableHours", Math.Max(0m, totalUsed - billable)), ("utilization", utilization),
            ("atRiskProjects", selectedProjects.Count(project => project.BudgetStatus is "approaching_budget" or "over_budget" || project.HighAlertCount > 0)))];
    }

    private static Dictionary<string, object?>[] OrganizationReport(
        FinancialOperationsProject[] projects,
        EnterpriseReportingContext context,
        EnterpriseReportRequest request)
    {
        var used = projects.Sum(project => project.UsedHours);
        var billable = projects.Where(project => project.Billable).Sum(project => project.UsedHours);
        return [Row(("organization", "US Signal Solution Provider — authorized scope"),
            ("people", projects.SelectMany(project => project.Engineers).Select(engineer => engineer.UserId).Distinct().Count()),
            ("projects", projects.Length), ("customers", projects.Select(project => project.CustomerName).Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            ("plannedHours", projects.Sum(project => project.PlannedHours)), ("usedHours", used),
            ("billableUtilization", used > 0 ? billable / used * 100m : (decimal?)null),
            ("currentVariance", projects.Where(project => project.CurrentVariance.HasValue).Sum(project => project.CurrentVariance ?? 0m)),
            ("atRiskProjects", projects.Count(project => project.BudgetStatus is "approaching_budget" or "over_budget" || project.HighAlertCount > 0)),
            ("dataAsOf", projects.Select(project => project.CalculatedAt).DefaultIfEmpty(DateTimeOffset.UtcNow).Max()))];
    }

    private static Dictionary<string, object?>[] ReportLibrary(EnterpriseReportingContext context) =>
        EnterpriseReportingCatalog.ForContext(context).Select(report => Row(
            ("reportCode", report.Code), ("reportName", report.Name), ("category", report.Category),
            ("description", report.Description), ("modules", string.Join(", ", report.Modules)),
            ("filters", string.Join(", ", report.Filters.Select(filter => filter.Label))),
            ("requiredSources", string.Join(", ", report.RequiredSources)),
            ("optionalSources", string.Join(", ", report.OptionalSources)),
            ("exports", "US Signal PDF, Excel, CSV, JSON")))
        .OrderBy(row => row["category"]).ThenBy(row => row["reportName"]).ToArray();

    private static Dictionary<Guid, decimal> InvoiceAmountsByProject(EnterpriseReportingSupplemental supplemental)
    {
        var rows = supplemental.Rows("client_invoices").Concat(supplemental.Rows("billing_invoices"));
        var result = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
        {
            var projectId = GuidValue(row, "project_id");
            if (!projectId.HasValue) continue;
            result[projectId.Value] = result.GetValueOrDefault(projectId.Value)
                + (DecimalValue(row, "invoice_total", "total_amount", "amount") ?? 0m);
        }
        return result;
    }

    private static Dictionary<Guid, decimal> AggregateDecimalByProject(JsonElement[] rows, params string[] amountFields)
    {
        var result = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
        {
            var projectId = GuidValue(row, "project_id", "affected_project_id");
            if (!projectId.HasValue) continue;
            result[projectId.Value] = result.GetValueOrDefault(projectId.Value) + (DecimalValue(row, amountFields) ?? 0m);
        }
        return result;
    }

    private static decimal? NullableAdd(decimal? left, decimal? right) =>
        !left.HasValue && !right.HasValue ? null : (left ?? 0m) + (right ?? 0m);

    private static bool IsTimeAndMaterial(string value)
    {
        var normalized = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return normalized is "tm" or "timeandmaterial" or "timeandmaterials" or "timematerial" or "timematerials";
    }

    private static bool IsClosedProject(string value) =>
        value.Equals("completed", StringComparison.OrdinalIgnoreCase)
        || value.Equals("closed", StringComparison.OrdinalIgnoreCase)
        || value.Equals("archived", StringComparison.OrdinalIgnoreCase);

    private static decimal TargetPercentage(JsonElement[] rows, Guid userId)
    {
        var target = rows.Where(row => GuidValue(row, "user_id", "engineer_user_id") == userId)
            .Select(row => DecimalValue(row, "target_percentage", "target_percent", "utilization_target_percentage"))
            .FirstOrDefault(value => value.HasValue);
        return target is > 0 and <= 100 ? target.Value : 70m;
    }

    private static string UtilizationStatus(decimal? value, decimal target)
    {
        if (!value.HasValue) return "no_recorded_time";
        var variance = value.Value - target;
        return variance >= 0 ? "at_or_above_target" : variance >= -5m ? "near_target" : "below_target";
    }

    private static bool IsVacationPto(
        JsonElement row,
        IReadOnlyDictionary<Guid, string> categories)
    {
        var categoryId = GuidValue(
            row,
            "non_project_time_category_id",
            "category_id");
        var category = categoryId.HasValue && categories.TryGetValue(categoryId.Value, out var mapped)
            ? mapped
            : string.Empty;
        var text = string.Join(' ', new[]
        {
            category,
            Text(row, "task_name", "task_code", "activity", "row_label"),
            Text(row, "time_type", "entry_type", "hours_type"),
            Text(row, "description", "comment", "work_description"),
            Text(row, "category", "non_project_category", "non_project_time_category")
        }).ToLowerInvariant();
        return text.Contains("vacation")
            || text.Contains("paid time off")
            || text.Contains("paid_time_off")
            || text.Contains("pto");
    }

    private static bool ApprovedStatus(string value)
    {
        var status = (value ?? string.Empty).Trim().ToLowerInvariant();
        return status is "approved" or "pm_approved" or "manager_approved" or "project_approved"
            or "project_validated" or "accounting_ready" or "reconciled" or "locked" or "fully_approved";
    }

    private static string ApprovalStage(string status)
    {
        var normalized = (status ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("manager")) return "Manager approval";
        if (normalized.Contains("pm") || normalized.Contains("project")) return "Project Manager approval";
        if (normalized.Contains("accounting")) return "Accounting readiness";
        if (normalized.Contains("return") || normalized.Contains("reject")) return "Returned to employee";
        return "Submission / review";
    }

    private static string ApprovalOwner(string status, FinancialOperationsProject? project)
    {
        var normalized = (status ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("manager")) return "Manager";
        if (normalized.Contains("pm") || normalized.Contains("project")) return project?.ProjectManagerName ?? "Project Manager";
        if (normalized.Contains("accounting")) return "Accounting / Billing";
        if (normalized.Contains("return") || normalized.Contains("reject")) return "Employee";
        return "Current approver";
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

    private static Dictionary<string, object?>[] AiSowScope(
        JsonElement[] source,
        EnterpriseReportRequest request) => source
        .Select(row =>
        {
            var featureCode = Text(row, "feature_code");
            var targets = RouteTargets(row);
            var externalPolicy = Text(row, "external_context_policy");
            var status = targets.Length == 0 ? "not_configured" : "configured";
            return Row(
                ("capability", featureCode),
                ("consumerModule", CapabilityConsumerModules(featureCode)),
                ("primaryProvider", targets.ElementAtOrDefault(0) ?? "Not configured"),
                ("secondaryProvider", targets.ElementAtOrDefault(1) ?? "Not configured"),
                ("tertiaryProvider", targets.ElementAtOrDefault(2) ?? "Not configured"),
                ("fallback", targets.ElementAtOrDefault(3) ?? "Not configured"),
                ("privateFirst", targets.FirstOrDefault()?.Equals("celar_ai", StringComparison.OrdinalIgnoreCase) == true),
                ("status", status),
                ("humanReview", true),
                ("evidence", $"External context policy: {externalPolicy}; revision: {Text(row, "revision")}; ordered route: {string.Join(" → ", targets)}"));
        })
        .Where(row => string.IsNullOrWhiteSpace(request.WorkflowStatus)
            || string.Equals(row["status"]?.ToString(), request.WorkflowStatus, StringComparison.OrdinalIgnoreCase))
        .Where(row => MatchesSearch(row, request.Search))
        .OrderBy(row => row["capability"])
        .ToArray();

    private static string[] RouteTargets(JsonElement row)
    {
        if (!TryProperty(row, "route_targets", out var targets)) return Array.Empty<string>();
        if (targets.ValueKind == JsonValueKind.Array)
        {
            return targets.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToArray();
        }
        if (targets.ValueKind == JsonValueKind.String)
        {
            var raw = targets.GetString();
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            try
            {
                return JsonSerializer.Deserialize<string[]>(raw)
                    ?.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToArray() ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }
        return Array.Empty<string>();
    }

    private static string CapabilityConsumerModules(string featureCode) => featureCode switch
    {
        "timesheet_non_project_description" => "Module 001",
        "timesheet_project_task_description" => "Module 001",
        "timesheet_service_request_description" => "Modules 001 and 055C",
        "sow_gsd_planning" => "Modules 025, 055D, and 066",
        "project_flowhive_plan" => "Module 066",
        "closeout_communication" => "Modules 040 and 041",
        "help_assistant" => "Modules 011 and 999",
        _ => "Module 064 governed capability route"
    };

    private static Dictionary<string, object?>[] GenericRows(
        JsonElement[] source,
        EnterpriseReportRequest request,
        EnterpriseReportColumnDefinition[] columns,
        IReadOnlyDictionary<string, string[]> map,
        EnterpriseReportingContext context) => source
        .Where(row => WithinDate(row, request, "created_at", "updated_at", "observed_at", "work_date", "coverage_start", "due_date"))
        .Where(row => string.IsNullOrWhiteSpace(request.WorkflowStatus)
            || JsonContains(row, request.WorkflowStatus, "status", "workflow_status", "lifecycle", "decision"))
        .Where(row => string.IsNullOrWhiteSpace(request.Severity)
            || JsonContains(row, request.Severity, "severity", "priority", "alert_severity"))
        .Where(row => string.IsNullOrWhiteSpace(request.ModuleCode)
            || JsonContains(row, request.ModuleCode, "module_code", "affected_module", "source_module"))
        .Select(row =>
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                var candidates = map.TryGetValue(column.Key, out var mapped) ? mapped : [ToSnake(column.Key), column.Key];
                result[column.Key] = JsonValue(row, candidates);
            }
            return result;
        })
        .Where(row => MatchesSearch(row, request.Search))
        .ToArray();

    private static Dictionary<Guid, decimal> AggregateHours(
        JsonElement[] rows,
        FinancialOperationsProject[] projects,
        bool approvedOnly)
    {
        var visible = projects.Select(project => project.ProjectId).ToHashSet();
        var result = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
        {
            var projectId = GuidValue(row, "project_id");
            if (!projectId.HasValue || !visible.Contains(projectId.Value)) continue;
            if (approvedOnly)
            {
                var status = Text(row, "status").ToLowerInvariant();
                if (status.Length > 0 && status is not ("pm_approved" or "manager_approved" or "project_approved" or "project_validated" or "accounting_ready" or "reconciled" or "locked")) continue;
            }
            result[projectId.Value] = result.GetValueOrDefault(projectId.Value) + (DecimalValue(row, "hours", "duration_hours") ?? 0m);
        }
        return result;
    }

    private static EnterpriseReportSourceState[] ResolveSources(
        EnterpriseReportingContext context,
        EnterpriseReportDefinition definition)
    {
        var required = definition.RequiredSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = definition.RequiredSources.Concat(definition.OptionalSources)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return keys.Select(key => context.Sources.FirstOrDefault(source => source.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? new EnterpriseReportSourceState(
                key, key.Replace('_', ' '), "unavailable", required.Contains(key), 0,
                "The source did not publish readiness evidence for this report run.",
                "SOURCE_READINESS_NOT_REPORTED", DateTimeOffset.UtcNow))
            .Select(source => source with { Required = required.Contains(source.Key) })
            .ToArray();
    }

    private static object ScopeEvidence(
        EnterpriseReportingContext context,
        EnterpriseReportDefinition definition) => new
    {
        context.Actor.ActualUserId,
        context.Actor.EffectiveUserId,
        context.Actor.DisplayName,
        context.Actor.Email,
        context.Actor.Roles,
        context.Actor.IsViewAs,
        context.Actor.Broad,
        visibleProjectCount = context.Projects.Length,
        definition.ScopeRule,
        serverAuthorized = true,
        viewAsMutationAuthority = false,
        reportPermissionDoesNotExpandRecordOrFieldScope = true
    };

    private static Dictionary<string, object?> EffectiveFilterDictionary(EnterpriseReportRequest request) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["search"] = request.Search, ["projectId"] = request.ProjectId, ["customerId"] = request.CustomerId,
        ["customer"] = request.Customer, ["projectManagerUserId"] = request.ProjectManagerUserId,
        ["engineerUserId"] = request.EngineerUserId, ["projectStatus"] = request.ProjectStatus,
        ["budgetStatus"] = request.BudgetStatus, ["contractType"] = request.ContractType,
        ["billable"] = request.Billable, ["dateFrom"] = request.DateFrom, ["dateTo"] = request.DateTo,
        ["workflowStatus"] = request.WorkflowStatus, ["severity"] = request.Severity,
        ["moduleCode"] = request.ModuleCode, ["sourceStatus"] = request.SourceStatus,
        ["limit"] = request.Limit, ["includeInactive"] = request.IncludeInactive
    };

    private static Dictionary<Guid, string> People(EnterpriseReportingContext context)
    {
        var people = new Dictionary<Guid, string> { [context.Actor.EffectiveUserId] = context.Actor.DisplayName };
        foreach (var project in context.Projects)
        {
            if (project.ProjectManagerUserId.HasValue) people[project.ProjectManagerUserId.Value] = project.ProjectManagerName;
            foreach (var engineer in project.Engineers) people[engineer.UserId] = engineer.DisplayName;
            if (project.ProjectTeamCoordinator is not null) people[project.ProjectTeamCoordinator.UserId] = project.ProjectTeamCoordinator.DisplayName;
            if (project.SolutionArchitect is not null) people[project.SolutionArchitect.UserId] = project.SolutionArchitect.DisplayName;
            if (project.AccountExecutive is not null) people[project.AccountExecutive.UserId] = project.AccountExecutive.DisplayName;
        }
        return people;
    }

    private static EnterpriseReportOption[] Options(IEnumerable<string?> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value)
        .Select(value => new EnterpriseReportOption(value, Title(value)))
        .ToArray();

    private static string Title(string value) => string.Join(' ', value.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] values)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) result[value.Key] = value.Value;
        return result;
    }

    private static bool MatchesSearch(Dictionary<string, object?> row, string? search) =>
        string.IsNullOrWhiteSpace(search)
        || string.Join(' ', row.Values.Where(value => value is not null))
            .Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool WithinDate(JsonElement row, EnterpriseReportRequest request, params string[] keys)
    {
        if (!request.DateFrom.HasValue && !request.DateTo.HasValue) return true;
        var value = DateValue(row, keys);
        if (!value.HasValue) return true;
        return (!request.DateFrom.HasValue || value.Value >= request.DateFrom.Value)
            && (!request.DateTo.HasValue || value.Value <= request.DateTo.Value);
    }

    private static JsonElement? LatestForProject(JsonElement[] rows, Guid projectId) => rows
        .Where(row => GuidValue(row, "project_id", "affected_project_id") == projectId)
        .OrderByDescending(row => DateTimeValue(row, "updated_at", "created_at", "last_detected_at") ?? DateTimeOffset.MinValue)
        .Cast<JsonElement?>().FirstOrDefault();

    private static bool HasClosedCloseout(JsonElement[] rows, Guid projectId) => rows.Any(row =>
        GuidValue(row, "project_id") == projectId
        && Text(row, "closeout_status", "status").Equals("closed", StringComparison.OrdinalIgnoreCase));

    private static string Period(EnterpriseReportRequest request) =>
        $"{request.DateFrom?.ToString("yyyy-MM-dd") ?? "beginning"} to {request.DateTo?.ToString("yyyy-MM-dd") ?? "current"}";

    private static string? Clean(string? value, int maximum)
    {
        var clean = (value ?? string.Empty).Replace('\0', ' ').Trim();
        if (clean.Length == 0) return null;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static bool JsonContains(JsonElement row, string value, params string[] keys) =>
        keys.Any(key => Text(row, key).Contains(value, StringComparison.OrdinalIgnoreCase));

    private static object? JsonValue(JsonElement row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryProperty(row, key, out var value)) continue;
            return value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(item => item.ToString())),
                _ => value.ToString()
            };
        }
        return null;
    }

    private static string Text(JsonElement row, params string[] keys) =>
        JsonValue(row, keys)?.ToString()?.Trim() ?? string.Empty;

    private static Guid? GuidValue(JsonElement row, params string[] keys)
    {
        var text = Text(row, keys);
        return Guid.TryParse(text, out var value) ? value : null;
    }

    private static decimal? DecimalValue(JsonElement row, params string[] keys)
    {
        var value = JsonValue(row, keys);
        if (value is decimal number) return number;
        return decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateOnly? DateValue(JsonElement row, params string[] keys)
    {
        var value = Text(row, keys);
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        return DateTimeOffset.TryParse(value, out var timestamp) ? DateOnly.FromDateTime(timestamp.UtcDateTime) : null;
    }

    private static DateTimeOffset? DateTimeValue(JsonElement row, params string[] keys)
    {
        var value = Text(row, keys);
        return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;
    }

    private static bool TryProperty(JsonElement row, string key, out JsonElement value)
    {
        if (row.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in row.EnumerateObject())
            {
                if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string ToSnake(string value) => string.Concat(value.Select((character, index) =>
        char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    private static IReadOnlyDictionary<string, string[]> GenericQualificationMap() => Map(
        ("person", ["display_name", "person", "user_name"]), ("email", ["email"]), ("team", ["team_name", "department_name"]),
        ("category", ["qualification_category", "category"]), ("qualification", ["qualification_name", "name"]),
        ("competency", ["competency"]), ("effectiveEndDate", ["effective_end_date", "expiration_date"]),
        ("lifecycle", ["lifecycle", "status"]), ("acknowledgement", ["acknowledgement_status", "renewal_status"]),
        ("plannedRenewalDate", ["planned_renewal_date"]));

    private static IReadOnlyDictionary<string, string[]> GenericOnCallMap() => Map(
        ("department", ["department", "department_name", "team_name"]), ("coverageStart", ["coverage_start", "start_at", "starts_at"]),
        ("coverageEnd", ["coverage_end", "end_at", "ends_at"]), ("engineer", ["engineer_name", "display_name", "resource_name"]),
        ("phone", ["phone", "phone_number"]), ("acknowledgement", ["acknowledgement_status", "status"]),
        ("gap", ["is_gap", "gap"]), ("conflict", ["is_conflict", "conflict"]), ("source", ["source", "source_type"]));

    private static IReadOnlyDictionary<string, string[]> GenericIssueMap() => Map(
        ("trackingId", ["tracking_id", "item_number", "defect_id"]), ("type", ["item_type", "type", "report_type"]),
        ("affectedModule", ["affected_module", "module_code"]), ("title", ["title", "summary"]),
        ("status", ["status", "workflow_status"]), ("severity", ["severity", "priority"]),
        ("reporter", ["reporter_name", "raised_by_name", "created_by_name"]), ("assignee", ["assignee_name", "assigned_to_name"]),
        ("createdAt", ["created_at", "date_added"]), ("resolvedAt", ["resolved_at", "date_resolved"]), ("resolution", ["resolution", "resolution_summary"]));

    private static IReadOnlyDictionary<string, string[]> GenericReleaseMap() => Map(
        ("releaseSha", ["release_sha", "commit_sha", "source_sha"]), ("environment", ["environment", "target_environment"]),
        ("status", ["status", "outcome"]), ("approvals", ["approval_summary", "approvals"]),
        ("artifact", ["artifact_name", "artifact"]), ("validation", ["validation_status", "validation"]),
        ("rollbackReady", ["rollback_ready", "can_rollback"]), ("owner", ["owner_name", "responsible_owner"]),
        ("observedAt", ["observed_at", "created_at"]));

    private static IReadOnlyDictionary<string, string[]> GenericHealthMap() => Map(
        ("service", ["service_name", "service"]), ("provider", ["provider", "platform"]), ("region", ["region", "location"]),
        ("status", ["status", "health_status"]), ("sli", ["sli_name", "sli"]), ("sloTarget", ["slo_target", "target_percentage"]),
        ("currentValue", ["current_value", "observed_value"]), ("errorBudgetRemaining", ["error_budget_remaining"]),
        ("alerts", ["alert_count", "alerts"]), ("observedAt", ["observed_at", "created_at"]));

    private static IReadOnlyDictionary<string, string[]> GenericGovernanceMap() => Map(
        ("domain", ["domain_name", "domain"]), ("classification", ["classification", "data_classification"]),
        ("owner", ["owner_name", "responsible_owner"]), ("retentionPolicy", ["retention_policy", "policy_name"]),
        ("legalHold", ["legal_hold_status", "legal_hold"]), ("purgeEligible", ["purge_eligible"]),
        ("lastPurgeAt", ["last_purge_at"]), ("status", ["status"]));

    private static IReadOnlyDictionary<string, string[]> GenericAcceptanceMap() => Map(
        ("customer", ["customer_name", "customer"]), ("projectCode", ["project_code"]), ("engagement", ["engagement_name", "name"]),
        ("milestone", ["milestone_name", "milestone"]), ("deliverable", ["deliverable_name", "deliverable"]),
        ("evidenceStatus", ["evidence_status"]), ("approver", ["approver_name", "approver"]),
        ("decision", ["decision", "status"]), ("decisionAt", ["decision_at", "accepted_at"]), ("criteria", ["acceptance_criteria", "criteria"]));

    private static IReadOnlyDictionary<string, string[]> GenericSecureInformationMap() => Map(
        ("customer", ["customer_name"]), ("projectCode", ["project_code"]), ("request", ["request_name", "title"]),
        ("template", ["template_name"]), ("status", ["status"]), ("fieldCount", ["field_count"]),
        ("completedFieldCount", ["completed_field_count"]), ("submittedAt", ["submitted_at"]),
        ("revision", ["revision_number", "revision"]), ("accessMode", ["access_mode", "delivery_mode"]));

    private static IReadOnlyDictionary<string, string[]> GenericPmoMap() => Map(
        ("customer", ["customer_name"]), ("projectCode", ["project_code"]), ("projectName", ["project_name"]),
        ("controlType", ["control_type", "item_type"]), ("reference", ["reference_code", "reference"]),
        ("title", ["title", "name"]), ("owner", ["owner_name", "responsible_owner"]),
        ("status", ["status"]), ("severity", ["severity", "priority"]), ("dueDate", ["due_date"]),
        ("baseline", ["baseline_name", "baseline"]), ("updatedAt", ["updated_at"]));

    private static IReadOnlyDictionary<string, string[]> GenericAuditMap() => Map(
        ("eventTime", ["event_time", "created_at", "occurred_at"]), ("category", ["category"]),
        ("eventType", ["event_type", "action"]), ("status", ["status"]),
        ("actor", ["actor_email", "actor_name", "changed_by_name"]), ("target", ["target_label", "target_id"]),
        ("sourceModule", ["source_module", "module_number"]), ("summary", ["summary", "message"]),
        ("correlationId", ["correlation_id"]), ("immutable", ["is_immutable"]));

    private static IReadOnlyDictionary<string, string[]> GenericStabilityMap() => Map(
        ("component", ["service_name", "component_name", "service", "name"]),
        ("provider", ["provider", "platform"]), ("status", ["status", "health_status", "outcome"]),
        ("health", ["health_status", "status"]), ("latency", ["latency_ms", "duration_ms"]),
        ("errorBudget", ["error_budget_remaining"]), ("lastCheck", ["observed_at", "checked_at", "created_at"]),
        ("diagnostic", ["diagnostic_code", "message", "summary"]));

    private static IReadOnlyDictionary<string, string[]> GenericApiMap() => Map(
        ("apiName", ["api_name", "service_name", "component_name", "name"]),
        ("apiPath", ["api_path", "path", "endpoint"]), ("module", ["module_code", "source_module"]),
        ("status", ["status", "health_status", "outcome"]), ("httpStatus", ["http_status", "status_code"]),
        ("latencyMs", ["latency_ms", "duration_ms"]), ("lastCheck", ["observed_at", "checked_at", "created_at"]),
        ("diagnostic", ["diagnostic_code", "message", "summary"]));

    private static IReadOnlyDictionary<string, string[]> GenericExternalConnectionMap() => Map(
        ("connection", ["provider_name", "connection_name", "provider_key"]),
        ("provider", ["provider_key", "provider_type"]), ("environment", ["environment_mode", "environment"]),
        ("status", ["availability_status", "provider_status", "status"]),
        ("lastTest", ["last_checked_at", "checked_at"]), ("lastSuccess", ["last_available_at", "last_success_at"]),
        ("diagnostic", ["last_error_code", "diagnostic_code"]), ("secretReturned", ["secret_value_returned"]));

    private static IReadOnlyDictionary<string, string[]> GenericSecurityMap() => Map(
        ("eventTime", ["event_time", "created_at", "occurred_at"]), ("eventType", ["event_type", "action"]),
        ("actor", ["actor_email", "actor_name"]), ("role", ["actor_role", "role_code"]),
        ("viewAsTarget", ["view_as_target", "effective_user_email"]), ("status", ["status"]),
        ("sourceModule", ["source_module", "module_number"]), ("summary", ["summary", "message"]),
        ("correlationId", ["correlation_id"]));

    private static IReadOnlyDictionary<string, string[]> GenericUatMap() => Map(
        ("evidenceId", ["evidence_id", "operational_control_evidence_id", "history_id"]),
        ("releaseSha", ["release_sha", "commit_sha", "source_sha"]), ("environment", ["environment", "target_environment"]),
        ("role", ["role_code", "actor_role"]), ("scenario", ["scenario", "control_name", "summary"]),
        ("status", ["status", "outcome"]), ("observedAt", ["observed_at", "created_at"]),
        ("artifact", ["artifact_name", "artifact"]), ("notes", ["notes", "message", "summary"]));

    private static IReadOnlyDictionary<string, string[]> Map(params (string Key, string[] Values)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Values, StringComparer.OrdinalIgnoreCase);
}
