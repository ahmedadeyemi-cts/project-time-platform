namespace ProjectTime.Api.Modules;

internal static partial class EnterpriseReportingCatalog
{
    private static readonly EnterpriseReportDefinition[] LegacyOperationsReports =
    [
        Alias(
            "workflow_approval_audit_report", "Workflow / Approval / Audit Report", "Workflow & Approval",
            "Timesheet, approval, audit, actor, decision, comment, target, and immutable workflow evidence.",
            ["002", "008", "030"], ["approval_records"], ["timesheet_day_statuses", "projectpulse_system_audit_events", "app_users"],
            ["time_scoped", "operations_scoped"], [DateFrom(), DateTo(), Engineer(), ProjectManager(), WorkflowStatus(), ModuleCode(), Search(), Limit()],
            [
                C("observedAt", "Observed", "datetime"), C("workflow", "Workflow"), C("stage", "Stage", "status"),
                C("target", "Target"), C("actor", "Actor"), C("decision", "Decision", "status"),
                C("comment", "Comment"), C("sourceModule", "Source module"), C("reference", "Reference"),
                C("immutable", "Immutable evidence", "boolean")
            ]),
        Alias(
            "system_stability_report", "System Stability Report", "Operational Control",
            "Provider-neutral service observations, availability, latency, alerts, error budget, and recovery evidence.",
            ["013", "068", "078", "998"], ["platform_health"], ["service_inventory", "alert_history"], ["control_plane"],
            [DateFrom(), DateTo(), SourceStatus(), Severity(), Search()],
            [
                C("service", "Service"), C("provider", "Provider"), C("region", "Region"), C("status", "Status", "status"),
                C("availability", "Availability", "percent"), C("latencyMs", "Latency (ms)", "number"),
                C("errorRate", "Error rate", "percent"), C("alerts", "Alerts", "number"),
                C("errorBudgetRemaining", "Error budget remaining", "percent"), C("observedAt", "Observed", "datetime")
            ]),
        Alias(
            "api_status_report", "API Status Report", "Operational Control",
            "API route or service health, provider, environment, latency, status, alerts, and observation time.",
            ["013", "016", "068", "078", "998"], ["platform_health"], ["service_inventory", "alert_history"], ["control_plane"],
            [DateFrom(), DateTo(), SourceStatus(), Severity(), Search()],
            [
                C("api", "API / service"), C("provider", "Provider"), C("environment", "Environment"),
                C("status", "Status", "status"), C("httpStatus", "HTTP status", "number"), C("latencyMs", "Latency (ms)", "number"),
                C("errorRate", "Error rate", "percent"), C("alerts", "Alerts", "number"), C("observedAt", "Observed", "datetime")
            ]),
        Alias(
            "external_connection_report", "External Connection Report", "Operations",
            "CRM/ERP and Microsoft connection checks, provider, availability, HTTP response, duration, and sanitized diagnostics.",
            ["010", "026", "065"], ["crm_integration_connection_checks"], ["projectpulse_system_audit_events"],
            ["operations_scoped", "control_plane"], [DateFrom(), DateTo(), SourceStatus(), Search(), Limit()],
            [
                C("provider", "Provider"), C("connectionType", "Connection type"), C("environment", "Environment"),
                C("availability", "Availability", "status"), C("httpStatus", "HTTP status", "number"),
                C("durationMs", "Duration (ms)", "number"), C("diagnosticCode", "Diagnostic code"),
                C("checkedBy", "Checked by"), C("checkedAt", "Checked", "datetime"), C("secretValuesReturned", "Secret returned", "boolean")
            ]),
        Alias(
            "authentication_security_report", "Authentication / Security Report", "Operational Control",
            "Session, authentication, role, security-event, and audit evidence without exposing tokens or credentials.",
            ["012", "016", "037", "997"], ["auth_sessions"], ["projectpulse_system_audit_events", "projectpulse_security_alerts"], ["control_plane"],
            [DateFrom(), DateTo(), WorkflowStatus(), Severity(), Search(), Limit()],
            [
                C("observedAt", "Observed", "datetime"), C("user", "User"), C("email", "Email"),
                C("eventType", "Event type"), C("status", "Status", "status"), C("provider", "Provider"),
                C("sourceIp", "Source IP"), C("correlationId", "Correlation ID"), C("expiresAt", "Session expires", "datetime"),
                C("secretValuesReturned", "Secret returned", "boolean")
            ]),
        Alias(
            "ai_sow_scope_report", "AI / SOW Scope Report", "Celar AI & Documents",
            "Celar AI SOW/GSD/time-description draft scope, project evidence, review state, provider route, and human-review controls.",
            ["001", "011", "025", "064", "066"], ["sow_ai_time_entry_drafts"],
            ["sow_ai_time_entry_scope_documents", "project_flowhive_plans"], ["delivery_scoped"],
            [DateFrom(), DateTo(), Customer(), Project(), ProjectManager(), Engineer(), WorkflowStatus(), Search(), Limit()],
            [
                C("createdAt", "Created", "datetime"), C("customer", "Customer"), C("projectCode", "Project code"),
                C("projectName", "Project"), C("capability", "AI capability"), C("requestType", "Request type"),
                C("scopeSummary", "Scope summary"), C("documentCount", "Documents", "number"),
                C("providerRoute", "Provider route"), C("reviewStatus", "Review status", "status"),
                C("humanReviewRequired", "Human review required", "boolean"), C("published", "Published", "boolean")
            ]),
        Alias(
            "notification_report", "Notification Report", "Operations",
            "Enterprise and project notification events, recipients, provider boundary, delivery state, diagnostics, and acknowledgement.",
            ["022", "023", "032", "041", "065"], ["project_notification_dispatches"],
            ["enterprise_notification_events", "enterprise_notification_event_history"], ["operations_scoped"],
            [DateFrom(), DateTo(), Customer(), Project(), ModuleCode(), Severity(), WorkflowStatus(), SourceStatus()],
            [
                C("createdAt", "Created", "datetime"), C("customer", "Customer"), C("projectCode", "Project code"),
                C("notificationType", "Notification type"), C("sourceModule", "Source module"), C("severity", "Severity", "status"),
                C("recipientCount", "Recipients", "number"), C("deliveryBoundary", "Boundary", "status"),
                C("deliveryStatus", "Delivery status", "status"), C("diagnosticCode", "Diagnostic code"), C("sentAt", "Sent", "datetime")
            ]),
        Alias(
            "uat_evidence_report", "UAT Evidence Report", "Operational Control",
            "Role, scenario, expected result, observed result, outcome, evidence reference, and approval timestamp for UAT.",
            ["029", "058", "077"], ["uat_approval_export_audit_checks"], ["deployment_evidence"], ["control_plane"],
            [DateFrom(), DateTo(), WorkflowStatus(), Search(), Limit()],
            [
                C("role", "Role"), C("scenario", "Scenario"), C("expectedResult", "Expected result"),
                C("observedResult", "Observed result"), C("outcome", "Outcome", "status"),
                C("evidenceReference", "Evidence reference"), C("testedBy", "Tested by"),
                C("testedAt", "Tested", "datetime"), C("approvedBy", "Approved by"), C("approvedAt", "Approved", "datetime")
            ]),
        Alias(
            "report_library", "Report Library", "Reporting Administration",
            "Complete role-scoped Analytics Center catalog with category, sources, filters, exports, audience, and scope rules.",
            ["030", "060"], [], [], ["all_scoped"], [Search(), ModuleCode(), SourceStatus(), Limit()],
            [
                C("reportCode", "Report code"), C("reportName", "Report name"), C("category", "Category"),
                C("description", "Description"), C("modules", "Modules"), C("requiredSources", "Required sources"),
                C("optionalSources", "Optional sources"), C("filters", "Criteria"), C("audience", "Audience"),
                C("scopeRule", "Scope rule"), C("exports", "Exports")
            ])
    ];
}
