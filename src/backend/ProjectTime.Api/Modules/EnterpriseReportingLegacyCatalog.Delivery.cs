namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseReportingCatalog
{
    private static readonly EnterpriseReportDefinition[] LegacyDeliveryReports =
    [
        Alias(
            "project_closeout_readiness_report", "Project Closeout Readiness Report", "Closeout",
            "Closeout state, billing disposition, approved time, open alerts, notification evidence, and remaining blockers.",
            ["040", "041", "042"], ["projects"], ["project_closeout_records", "billing_readiness_reviews", "project_notification_dispatches"],
            ["pm_scoped", "financial_scoped"], [Customer(), Project(), ProjectManager(), ProjectStatus(), WorkflowStatus(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("projectStatus", "Project status", "status"), C("closeoutStatus", "Closeout status", "status"),
                C("billingDisposition", "Billing disposition", "status"), C("billingReadiness", "Billing readiness", "status"),
                C("approvedHours", "Approved hours", "number"), C("openAlerts", "Open alerts", "number"), C("blockers", "Blockers")
            ]),
        Alias(
            "sales_to_delivery_handoff_quality", "Sales-to-Delivery Handoff Quality Report", "Sales & Delivery",
            "SELL association, quote, customer, SOW/GSD, assignments, planned hours, and handoff gaps by project.",
            ["019", "025", "026", "036", "055B", "055D"], ["projects"], ["sell_commercial_model", "work_register_project_metadata"],
            ["commercial_scoped", "delivery_scoped"], [Customer(), Project(), ProjectManager(), ProjectStatus(), SourceStatus(), Search()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("projectName", "Project"),
                C("sellQuoteNumber", "SELL quote"), C("contractType", "Contract type"), C("projectManager", "Project Manager"),
                C("engineerCount", "Engineers", "number"), C("plannedHours", "Planned hours", "number"),
                C("documentCoverage", "Document coverage", "status"), C("handoffState", "Handoff state", "status"), C("gaps", "Gaps")
            ]),
        Alias(
            "project_report", "Project Report", "Project Delivery",
            "Complete project identity, customer, ownership, contract, schedule, team, hours, cost, risk, and lifecycle view.",
            ["006", "018", "019", "020", "030", "055C", "055D"], ["projects"], ["work_register_project_metadata"], ["all_scoped"],
            [Search(), Customer(), Project(), ProjectManager(), Engineer(), ProjectStatus(), ContractType(), Billable(), DateFrom(), DateTo()],
            [
                C("customer", "Customer"), C("projectCode", "Project code"), C("legacyProjectCode", "Legacy project code"),
                C("projectName", "Project"), C("projectStatus", "Status", "status"), C("projectManager", "Project Manager"),
                C("engineerCount", "Engineers", "number"), C("contractType", "Contract type"),
                C("startDate", "Start", "date"), C("endDate", "End", "date"), C("plannedHours", "Planned hours", "number"),
                C("usedHours", "Used hours", "number"), C("remainingHours", "Remaining hours", "number"), C("completionPercentage", "Completion", "percent")
            ])
    ];
}
