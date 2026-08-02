namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseReportingCatalog
{
    private static readonly EnterpriseReportDefinition[] LegacyPeopleAndTimeReports =
    [
        Alias(
            "time_entry_detail_report", "Time Entry Detail Report", "Time & Utilization",
            "Individual time-entry evidence by Engineer, project, task, status, hours, and description.",
            ["001", "002", "007", "030"], ["time_entries"], ["projects"], ["time_scoped"],
            [DateFrom(true), DateTo(true), Customer(), Project(), ProjectManager(), Engineer(), WorkflowStatus(), Search(), Limit()],
            [
                C("workDate", "Work date", "date"), C("engineer", "Engineer"), C("customer", "Customer"),
                C("projectCode", "Project code"), C("projectName", "Project"), C("task", "Task"),
                C("timeType", "Time type"), C("hours", "Hours", "number"), C("status", "Workflow status", "status"),
                C("description", "Description")
            ]),
        Report(
            "utilization_over_under_by_engineer", "Utilization Over / Under Report by Engineer", "Time & Utilization",
            "Current utilization and over/under target compared with projected utilization after remaining assigned billable work.",
            ["001", "003", "018", "019", "057", "070"], ["time_entries"],
            ["utilization_policies", "utilization_targets"], ["engineering_scoped"],
            "Engineers are locked to themselves; Managers and administrators see only their authorized people scope.",
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), Customer(), Project()],
            [
                C("engineer", "Engineer"), C("email", "Email"), C("period", "Period"),
                C("currentEligibleHours", "Current eligible hours", "number"), C("currentBillableHours", "Current billable hours", "number"),
                C("currentNonBillableHours", "Current non-billable hours", "number"), C("targetPercentage", "Target percentage", "percent"),
                C("targetHours", "Target hours", "number"), C("currentUtilizationPercentage", "Current utilization", "percent"),
                C("currentOverUnderHours", "Current over / under hours", "number"),
                C("remainingAssignedBillableHours", "Remaining assigned billable hours", "number"),
                C("projectedEligibleHours", "Projected eligible hours", "number"), C("projectedBillableHours", "Projected billable hours", "number"),
                C("projectedUtilizationPercentage", "Projected utilization", "percent"),
                C("projectedOverUnderHours", "Projected over / under hours", "number"),
                C("projectionDeltaPercentage", "Utilization change", "percent"), C("currentState", "Current state", "status"),
                C("projectedState", "Projected state", "status")
            ]),
        Report(
            "engineer_vacation_pto_used", "Engineer Vacation / PTO Used Report", "People & Capacity",
            "Vacation and PTO taken, approved, pending, returned, equivalent days, and first/latest dates by Engineer.",
            ["001", "003", "012", "057"], ["time_entries", "non_project_time_categories"],
            ["app_users", "reporting_relationships"], ["people_scoped"],
            "Individuals see themselves; Managers and administrators see only their authorized people scope. Company Holiday remains separate.",
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), Search()],
            [
                C("engineer", "Engineer"), C("email", "Email"), C("department", "Department"), C("manager", "Manager"),
                C("period", "Period"), C("approvedPtoHours", "Approved Vacation / PTO hours", "number"),
                C("pendingPtoHours", "Pending Vacation / PTO hours", "number"), C("returnedPtoHours", "Returned Vacation / PTO hours", "number"),
                C("totalPtoHours", "Total Vacation / PTO hours", "number"), C("equivalentDays", "Equivalent days", "number"),
                C("firstPtoDate", "First Vacation / PTO date", "date"), C("latestPtoDate", "Latest Vacation / PTO date", "date"),
                C("entryCount", "Entries", "number"), C("categories", "Categories")
            ]),
        Alias(
            "billable_vs_non_billable", "Billable vs Non-Billable Report", "Time & Utilization",
            "Billable and non-billable time totals, percentages, after-hours, and project context by Engineer.",
            ["001", "003", "030"], ["time_entries"], ["non_project_time_categories"], ["time_scoped"],
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), Customer(), Project()],
            [
                C("engineer", "Engineer"), C("period", "Period"), C("billableHours", "Billable hours", "number"),
                C("nonBillableHours", "Non-billable hours", "number"), C("totalHours", "Total hours", "number"),
                C("billablePercentage", "Billable percentage", "percent"), C("normalHours", "Normal hours", "number"),
                C("afterHours", "After-hours", "number"), C("projectCount", "Projects", "number"),
                C("nonProjectCategories", "Non-project categories")
            ]),
        Alias(
            "approval_bottleneck", "Approval Bottleneck Report", "Workflow & Approval",
            "Pending approval stage, age, current approver, Engineer, hours, and escalation state.",
            ["001", "002", "023", "030"], ["timesheet_day_statuses"], ["approval_records", "app_users"], ["time_scoped"],
            [DateFrom(), DateTo(), Engineer(), ProjectManager(), WorkflowStatus(), Search()],
            [
                C("engineer", "Engineer"), C("workDate", "Work date", "date"), C("hours", "Hours", "number"),
                C("status", "Status", "status"), C("stage", "Current stage"), C("currentApprover", "Current approver"),
                C("ageDays", "Age (days)", "number"), C("overdue", "Overdue", "boolean"), C("lastDecisionAt", "Last decision", "datetime")
            ]),
        Alias(
            "missing_late_timesheet", "Missing Time / Late Timesheet Report", "Workflow & Approval",
            "Missing, draft, or late timesheet days by employee, Manager, expected hours, recorded hours, and age.",
            ["001", "002", "023", "030"], ["timesheet_day_statuses"], ["timesheets", "app_users", "reporting_relationships"], ["time_scoped"],
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), WorkflowStatus(), Search()],
            [
                C("engineer", "Engineer"), C("manager", "Manager"), C("workDate", "Work date", "date"),
                C("expectedHours", "Expected hours", "number"), C("recordedHours", "Recorded hours", "number"),
                C("missingHours", "Missing hours", "number"), C("status", "Status", "status"), C("lateDays", "Late days", "number")
            ]),
        Alias(
            "pm_project_workload", "PM Project Workload Report", "People & Capacity",
            "Project count, customers, team size, hours, risk, over-budget work, and closeout by Project Manager.",
            ["018", "030", "040", "057"], ["projects"], ["project_closeout_records", "cost_alerts"], ["pm_scoped", "people_scoped"],
            [ProjectManager(), Customer(), ProjectStatus(), BudgetStatus(), DateFrom(), DateTo()],
            [
                C("projectManager", "Project Manager"), C("projectCount", "Projects", "number"), C("customerCount", "Customers", "number"),
                C("engineerCount", "Engineers", "number"), C("plannedHours", "Planned hours", "number"),
                C("usedHours", "Used hours", "number"), C("remainingHours", "Remaining hours", "number"),
                C("atRiskProjects", "At-risk projects", "number"), C("overBudgetProjects", "Over-budget", "number"),
                C("closeoutPending", "Closeout pending", "number")
            ]),
        Alias(
            "engineer_utilization_detail_report", "Engineer Utilization Detail Report", "Time & Utilization",
            "Detailed utilization by Engineer with eligible, billable, non-billable, target, variance, and project context.",
            ["001", "003", "030", "057"], ["time_entries"], ["utilization_policies", "utilization_targets"], ["engineering_scoped"],
            [DateFrom(true), DateTo(true), Engineer(), ProjectManager(), Customer(), Project()],
            [
                C("engineer", "Engineer"), C("period", "Period"), C("eligibleHours", "Eligible hours", "number"),
                C("billableHours", "Billable hours", "number"), C("nonBillableHours", "Non-billable hours", "number"),
                C("targetHours", "Target hours", "number"), C("utilizationPercentage", "Utilization", "percent"),
                C("varianceHours", "Variance hours", "number"), C("scope", "Scope")
            ]),
        Alias(
            "selected_engineers_report", "Selected Engineers Report", "People & Capacity",
            "Selected Engineer directory, team, assigned projects, planned hours, used hours, remaining hours, and utilization.",
            ["012", "018", "019", "030", "057"], ["projects"], ["app_users", "teams", "team_memberships", "time_entries"], ["people_scoped"],
            [DateFrom(), DateTo(), Engineer(), ProjectManager(), Customer(), Project(), Search()],
            [
                C("engineer", "Engineer"), C("email", "Email"), C("department", "Department"), C("team", "Team"),
                C("projectCount", "Projects", "number"), C("projectCodes", "Project codes"), C("assignedHours", "Assigned hours", "number"),
                C("usedHours", "Used hours", "number"), C("remainingHours", "Remaining hours", "number"),
                C("utilizationPercentage", "Utilization", "percent")
            ]),
        Alias(
            "team_report", "Team Report", "People & Capacity",
            "Team membership, Managers, Engineers, projects, capacity, hours, utilization, and workload risk.",
            ["012", "018", "030", "057", "070"], ["projects"], ["teams", "team_memberships", "app_users", "time_entries"], ["people_scoped"],
            [DateFrom(), DateTo(), ProjectManager(), Engineer(), Customer(), Project()],
            [
                C("team", "Team"), C("manager", "Manager"), C("peopleCount", "People", "number"),
                C("projectCount", "Projects", "number"), C("plannedHours", "Planned hours", "number"),
                C("usedHours", "Used hours", "number"), C("remainingHours", "Remaining hours", "number"),
                C("billableHours", "Billable hours", "number"), C("utilizationPercentage", "Utilization", "percent"),
                C("atRiskProjects", "At-risk projects", "number")
            ]),
        Alias(
            "organization_report", "Organization Report", "Executive & Portfolio",
            "Organization totals for people, projects, customers, hours, utilization, and financial variance.",
            ["012", "018", "030", "039", "057"], ["projects"], ["app_users", "teams", "time_entries"], ["all_scoped", "financial_scoped"],
            [DateFrom(), DateTo(), ProjectStatus(), BudgetStatus(), ContractType()],
            [
                C("scope", "Organization scope"), C("peopleCount", "People", "number"), C("projectCount", "Projects", "number"),
                C("customerCount", "Customers", "number"), C("plannedHours", "Planned hours", "number"),
                C("usedHours", "Used hours", "number"), C("remainingHours", "Remaining hours", "number"),
                C("billableHours", "Billable hours", "number"), C("utilizationPercentage", "Utilization", "percent"),
                C("contractedValue", "Contracted value", "currency", true), C("forecastedFinalCost", "Forecasted final cost", "currency", true),
                C("financialVariance", "Financial variance", "currency", true), C("sourceCoverage", "Source coverage", "status")
            ])
    ];
}
