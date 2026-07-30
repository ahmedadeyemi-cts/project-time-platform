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
        var anyDegraded = sources.Any(source => source.Status is "unavailable" or "partial" or "restricted");
        var status = rows.Length == 0
            ? requiredUnavailable ? "source_unavailable" : "no_data"
            : anyDegraded ? "partial" : "complete";
        var message = status switch
        {
            "complete" => $"{rows.Length} role-scoped report row(s) loaded.",
            "partial" => $"{rows.Length} row(s) loaded. One or more independent sources are degraded; healthy results remain visible.",
            "source_unavailable" => "A required report source is unavailable or outside the current scope. Other modules remain usable.",
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

    private static IReadOnlyDictionary<string, string[]> Map(params (string Key, string[] Values)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Values, StringComparer.OrdinalIgnoreCase);
}
