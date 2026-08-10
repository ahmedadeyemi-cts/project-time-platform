namespace ProjectTime.Api.Ai;

public enum CelarAiAnswerQuestionClass
{
    StructuredOperational,
    DocumentEvidence,
    CrossDomain,
    ProductProcedure,
    RuntimeDiagnostic,
    ArchitectureEnhancement,
    PublicCurrent,
    PublicStable,
    Unknown
}

public enum CelarAiEvidenceMode
{
    LiveStructured,
    PrivateDocument,
    DeterministicCalculation,
    SourceControlledProcedure,
    RuntimeDiagnostic,
    GovernedPublicCurrent,
    GovernedPublic,
    HumanClarification
}

public sealed record CelarAiUniversalToolCapability(
    string Code,
    string DisplayName,
    string Domain,
    string[] OwningModules,
    string Authority,
    string Availability,
    string AccessPolicy,
    string FreshnessClass,
    bool Deterministic,
    bool CitationRequired,
    bool PrivateOnly,
    bool MutationAllowed,
    string[] QuerySignals,
    string[] RequiredSourceTypes,
    string[] Routes);

/// <summary>
/// One source-controlled catalog for every evidence family Ask Celar AI may use.
/// The catalog does not grant access, execute SQL, or widen a module boundary.
/// Each owning module remains the authorization and record-scope authority.
/// </summary>
public static class CelarAiUniversalToolCatalog
{
    public const string ContractVersion = "celar-ai-universal-tool-catalog-v1-20260810";

    public static IReadOnlyList<CelarAiUniversalToolCapability> Tools { get; } =
    [
        Tool("effective_identity", "Effective identity and View-As evidence", "identity_permissions", ["009", "010", "012", "037", "059", "062"], "app_users, active session, role and permission evidence", "available_existing_adapter", "current actual/effective user and administrator scope", "request_time", true, true, true, ["who am i", "view-as", "view as", "effective user", "signed in", "identity"], ["active_identity", "view_as_state", "role_codes"], ["/api/identity/profile", "/api/rbac/v1/bootstrap"]),
        Tool("role_permission_evidence", "Role, permission, and module-access evidence", "identity_permissions", ["012", "037", "079", "997"], "governed RBAC and module policy", "available_existing_adapter", "effective user plus record and module scope", "request_time", true, true, true, ["permission", "access", "role", "403", "forbidden", "module visibility", "full control"], ["effective_permission", "module_access", "policy_version"], ["/api/rbac/v1/matrix", "#roles-permissions-matrix"]),
        Tool("people_directory", "Authorized people directory", "people_work", ["009", "010", "018", "062"], "active app_users and authorized identity aliases", "available_existing_adapter", "self, reporting, team, project, or administrator scope", "request_time", true, true, true, ["employee", "engineer", "person", "people", "manager", "team member", "who is"], ["active_person", "display_name", "organization_scope"], ["/api/identity/profile", "internal:celar-ai/identity-resolution"]),
        Tool("team_scope", "Reporting and team-scope evidence", "people_work", ["003", "018", "037", "057", "062"], "reporting relationships and explicit team-scope assignments", "available_existing_adapter", "authorized team, reporting, or administrator scope", "request_time", true, true, true, ["team", "department", "reports to", "manager for", "team scope"], ["reporting_relationship", "team_scope_assignment"], ["internal:celar-ai/team-scope"]),
        Tool("reporting_relationships", "Manager and team-lead relationships", "people_work", ["003", "018", "062"], "effective-dated reporting relationships", "cataloged_requires_execution_adapter", "authorized self, manager, team, or administrator scope", "request_time", true, true, true, ["manager", "team lead", "reports to", "direct report"], ["manager_user_id", "team_lead_user_id", "effective_dates"], ["internal:celar-ai/reporting-relationships"]),
        Tool("project_portfolio", "Authorized project portfolio", "projects_delivery", ["018", "019", "020", "055C", "055D"], "projects and authoritative project lifecycle status", "available_existing_adapter", "project, PM, assignment, team, or administrator scope", "request_time", true, true, true, ["project", "project manager", "customer project", "active projects", "project status"], ["project_identity", "project_status", "project_manager", "customer"], ["/api/project-workspace/overview", "#work-register"]),
        Tool("project_assignments", "Current project and engineer assignments", "projects_delivery", ["001", "018", "019", "055C"], "effective project assignments and Work Register assignment history", "available_existing_adapter", "project, self, PM, team, or administrator scope", "request_time", true, true, true, ["assigned", "assignment", "works on", "staffed", "engineers on"], ["project_assignment", "effective_dates", "closeout_status"], ["internal:celar-ai/person-projects", "/api/assignments/open-tasks"]),
        Tool("task_assignments", "Current task assignments and remaining work", "projects_delivery", ["001", "001A", "019", "055C"], "active project tasks and deduplicated current assignment authority", "available_existing_adapter", "self, project, PM, team, or administrator scope", "request_time", true, true, true, ["task", "assigned task", "remaining hours", "work assigned", "task owner"], ["task_identity", "assigned_hours", "effective_dates", "assignment_source"], ["internal:celar-ai/person-tasks", "/api/assignments/open-tasks"]),
        Tool("resource_requests", "Engineering resource requests", "projects_delivery", ["018", "019", "055C"], "engineering resource requests and assignments", "cataloged_requires_execution_adapter", "request owner, project, team, PM, or administrator scope", "request_time", true, true, true, ["resource request", "unfilled", "staffing request", "requested engineer"], ["request_status", "requested_role", "target_dates", "assignment_status"], ["/api/project-workspace/overview"]),
        Tool("timesheet_status", "Timesheet, work-log, and submitted-hours evidence", "time_approval_capacity", ["001", "002", "003", "007", "023", "028"], "timesheet periods, entries, work dates, statuses, and hours", "cataloged_requires_execution_adapter", "self, manager, PM, PTC, finance, or administrator scope", "request_time", true, true, true, ["timesheet", "time", "hours", "submitted", "missing time", "after-hours", "work log"], ["timesheet_period", "time_entry", "hours", "status"], ["#timesheet", "#approval-center"]),
        Tool("approval_status", "Approval and correction state", "time_approval_capacity", ["002", "007", "023", "028"], "governed approval events and current approval stage", "cataloged_requires_execution_adapter", "self, approver, manager, PTC, or administrator scope", "request_time", true, true, true, ["approval", "approve", "declined", "rejected", "locked", "correction"], ["approval_stage", "approver", "decision_time", "reason"], ["#approval-center", "#audit-history"]),
        Tool("capacity_utilization", "Capacity, utilization, and workload forecast", "time_approval_capacity", ["003", "018", "057", "069", "070"], "approved capacity, assignments, time, leave, and pipeline demand", "available_existing_adapter", "self, manager, team, PM, executive, or administrator scope", "request_time", true, true, true, ["capacity", "utilization", "over capacity", "under target", "workload", "forecast"], ["capacity_hours", "assigned_hours", "used_hours", "leave_hours", "utilization_formula"], ["/api/capacity-forecast/model", "/api/capacity-forecast/engineers"]),
        Tool("project_financial_truth", "Authoritative project financial truth", "financial_commercial", ["005", "018", "019", "022", "030", "036", "038", "039", "042", "055B", "060", "063"], "governed financial contract, rates, time, expenses, billing, and source health", "available_existing_adapter", "project, finance, sales, executive, or administrator scope", "request_time", true, true, true, ["budget", "cost", "margin", "variance", "revenue", "forecast", "financial", "profit"], ["contract_version", "currency", "planned_cost", "actual_cost", "forecast_cost", "known_unknowns"], ["/api/project-financials/portfolio", "/api/project-financials/reporting-summary"]),
        Tool("expense_billing", "Expense, billing, invoice, and reconciliation evidence", "financial_commercial", ["005", "022", "038", "039", "042"], "approved expenses, billing candidates, invoices, and reconciliation state", "cataloged_requires_execution_adapter", "project, finance, accounting, PTC, or administrator scope", "request_time", true, true, true, ["expense", "billing", "invoice", "reconciliation", "billable"], ["expense_status", "billing_status", "invoice_status", "blocking_reason"], ["#billing-readiness", "#invoice-billing-center"]),
        Tool("commercial_contracts", "Customer contracts, rates, and commercial assumptions", "financial_commercial", ["021", "024", "025", "026", "036", "055B", "060", "063", "073", "074"], "approved customer, contract, opportunity, quote, and rate records", "cataloged_requires_execution_adapter", "sales, solution architect, PM, finance, executive, or administrator scope", "request_time", true, true, true, ["contract", "rate card", "block of hours", "commercial assumption", "quote", "opportunity"], ["contract_version", "rate_source", "commercial_status", "balance"], ["#contracts", "#rate-card-administration", "#opportunities"]),
        Tool("commercial_pipeline", "Opportunity and future-delivery pipeline", "financial_commercial", ["021", "024", "026", "063"], "approved opportunity and delivery-pipeline records", "cataloged_requires_execution_adapter", "sales, leadership, delivery planning, or administrator scope", "request_time", true, true, true, ["pipeline", "opportunity", "future work", "forecasted project"], ["opportunity_stage", "expected_date", "customer", "estimated_effort"], ["#opportunities", "#customer-directory"]),
        Tool("project_documents", "Governed project-document inventory and authority", "documents_retrieval", ["011", "019", "020", "025", "055C", "055D", "066"], "authorized SOW, GSD, design, order, and supporting document records", "available_existing_adapter", "effective-user project and document visibility scope", "request_time", true, true, true, ["sow", "gsd", "document", "deliverable", "acceptance criteria", "customer responsibility", "version"], ["document_id", "project_id", "category", "version_authority", "processing_state"], ["/api/celar-ai/v1/flowhive/context-preview", "#project-workspace"]),
        Tool("malware_scan", "Private malware-scan evidence", "documents_retrieval", ["011", "055C", "055D", "079"], "authenticated ClamAV scan evidence for the immutable file snapshot", "available_oracle_runtime", "private document pipeline only", "request_time", true, true, true, ["malware", "virus", "scan", "unsafe file"], ["scan_status", "signature_version", "source_checksum"], ["https://celarai.onenecklab.com/v1/scan"]),
        Tool("document_extraction", "Private native document extraction", "documents_retrieval", ["011", "019", "055C", "055D"], "private extraction adapters and immutable source checksum", "available_existing_adapter", "authorized private document pipeline only", "snapshot_time", true, true, true, ["extract", "document text", "page", "slide", "worksheet", "metadata"], ["extraction_status", "source_checksum", "anchors"], ["/api/celar-ai/v1/private-documents/pipeline"]),
        Tool("ocr", "Private OCR for scanned pages", "documents_retrieval", ["011", "019", "055C", "055D"], "Tesseract 5 through the authenticated private OCR adapter", "available_oracle_runtime", "authorized private document pipeline only", "snapshot_time", true, true, true, ["ocr", "scanned pdf", "image-only", "scanned page"], ["ocr_model", "page_text", "source_checksum"], ["https://celarai.onenecklab.com/v1/extract"]),
        Tool("private_retrieval", "Permission-filtered private retrieval and citations", "documents_retrieval", ["011", "019", "020", "055C", "055D", "066"], "authorized chunks, source anchors, checksums, and current permission evidence", "available_existing_adapter", "re-authorized at retrieval time for the effective user", "request_time", true, true, true, ["find section", "cite", "source", "according to", "summarize document", "what does the sow say"], ["authorized_chunk", "citation_anchor", "document_version", "retrieval_score"], ["/api/celar-ai/v1/private-rag/help-search"]),
        Tool("conversation_attachments", "Conversation-scoped private attachments", "documents_retrieval", ["011"], "owner-scoped durable conversation attachments", "available_existing_adapter", "actual user only; View-As blocked", "request_time", true, true, true, ["attached", "attachment", "uploaded file", "this document"], ["attachment_owner", "scan_state", "extraction_state", "retention_state"], ["/api/celar-ai/v1/conversations/attachments"]),
        Tool("flowhive_plan", "FlowHive cited plan and deterministic schedule", "planning", ["019", "020", "057", "066"], "approved private source citations plus deterministic schedule engine", "available_existing_adapter", "effective-user project and FlowHive scope", "request_time", true, true, true, ["flowhive", "wbs", "critical path", "dependency", "milestone", "timeline", "schedule"], ["plan_version", "task", "dependency", "citation", "schedule_calculation"], ["/api/project-flowhive/portfolio", "/api/project-flowhive/schedule/calculate"]),
        Tool("project_forge", "Project Forge cited estimate and workbook", "planning", ["019", "033", "055C"], "private SOW-grounded estimate, assumptions, dependencies, and review state", "available_existing_adapter", "effective-user project and Project Forge scope", "request_time", true, true, true, ["project forge", "estimate", "workbook", "effort estimate"], ["draft_version", "citation", "estimate", "assumption"], ["#project-forge"]),
        Tool("risk_register", "Project risk and mitigation evidence", "risk_governance", ["019", "082"], "immutable project-risk versions and authorized action ownership", "available_existing_adapter", "project, PM, team, assigned action owner, or administrator scope", "request_time", true, true, true, ["risk", "mitigation", "residual score", "issue", "threaten"], ["risk_version", "probability", "impact", "mitigation", "action_owner"], ["/api/project-risk-register/summary", "#project-risk-register"]),
        Tool("audit_history", "Cross-module audit and change history", "operations_governance", ["008", "012", "037", "079", "997", "998"], "immutable actor, effective-user, action, entity, and timestamp evidence", "cataloged_requires_execution_adapter", "least-privilege audit, security, record-owner, or administrator scope", "request_time", true, true, true, ["who changed", "change history", "audit", "viewed", "when changed", "approved by"], ["actor", "effective_user", "action", "entity", "timestamp", "policy_version"], ["#audit-history"]),
        Tool("live_api_inventory", "Current ASP.NET endpoint inventory", "operations_governance", ["011", "058", "076", "078", "998"], "running EndpointDataSource and current release metadata", "available_existing_adapter", "API inventory permission or administrator scope", "request_time", true, true, true, ["api", "endpoint", "route", "which apis", "running api"], ["method", "route_pattern", "endpoint_name", "release_sha"], ["/api/celar-ai/v1/system/apis"]),
        Tool("system_diagnostics", "System diagnostics and safe retest evidence", "operations_governance", ["013", "016", "075", "076", "078", "998"], "allowlisted read-only health, diagnostic, and correlation evidence", "available_existing_adapter", "module, environment, troubleshooting, and administrator scope", "request_time", true, true, true, ["error", "broken", "failed", "diagnose", "timeout", "unavailable", "why did"], ["diagnostic_code", "health_status", "correlation_id", "dependency"], ["#system-diagnostics", "#observability-slo-health"]),
        Tool("release_deployment", "Release, deployment, rollback, and source evidence", "operations_governance", ["058", "075", "076", "077", "078", "083"], "signed release controls, immutable image/source evidence, and environment health", "available_existing_adapter", "environment and release-control scope", "request_time", true, true, true, ["deployment", "release", "version", "commit", "rollback", "what changed"], ["release_commit", "image_digest", "environment", "run_status", "rollback_target"], ["#release-deployment-control"]),
        Tool("observability", "Observability, SLO, backup, and dependency health", "operations_governance", ["014", "015", "017", "078", "998"], "approved health, SLO, backup, replication, and dependency evidence", "cataloged_requires_execution_adapter", "operations, administrator, or module-owner scope", "request_time", true, true, true, ["health check", "slo", "backup", "recovery", "replication", "monitoring"], ["health_state", "observed_at", "slo", "backup_evidence"], ["#observability-slo-health", "#system-diagnostics"]),
        Tool("defect_tracker", "Defect, impact, ownership, and resolution history", "operations_governance", ["013", "016", "076", "998"], "governed defect records and verification history", "available_existing_adapter", "module visibility and defect-record scope", "request_time", true, true, true, ["defect", "bug", "open issue", "known issue", "resolution"], ["defect_status", "module", "impact", "owner", "verification"], ["#defect-tracker"]),
        Tool("data_governance", "Data classification, retention, revocation, and policy", "operations_governance", ["079", "997"], "approved governance and retention policy versions", "available_existing_adapter", "record, module, governance, or administrator scope", "request_time", true, true, true, ["retention", "classification", "privacy", "policy", "revocation", "delete data"], ["classification", "policy_version", "retention_rule", "revocation_state"], ["#data-governance-retention"]),
        Tool("security_posture", "Security posture and protected-boundary evidence", "operations_governance", ["064", "077", "079", "997", "998"], "sanitized runtime, transport, secret-reference, and control evidence", "available_existing_adapter", "security or administrator scope", "request_time", true, true, true, ["secret", "token", "security", "malware", "private port", "tls", "exposed"], ["control_state", "secret_reference", "transport_state", "boundary"], ["#security-operations", "#ai-provider-configuration"]),
        Tool("provider_configuration", "Module 064 provider routing and readiness", "ai_runtime", ["011", "064", "079"], "persisted capability order, protected secret references, and fresh probes", "available_existing_adapter", "sanitized read for users; mutation only through Module 064 administrators", "request_time", true, true, true, ["claude", "openai", "ollama", "provider", "model", "routing", "module 064"], ["feature_route", "target_order", "readiness", "probe_age"], ["#ai-provider-configuration"]),
        Tool("oracle_runtime_readiness", "Oracle Celar AI HTTPS runtime readiness", "ai_runtime", ["011", "064"], "authenticated protected-Test readiness and endpoint contract", "available_protected_test", "deployment-managed Test integration only", "request_time", true, true, true, ["oracle runtime", "celarai.onenecklab.com", "private runtime ready", "ollama ready", "ocr ready"], ["authenticated_health", "generation_model", "embedding_model", "ocr_model", "clamav_state"], ["https://celarai.onenecklab.com/health"]),
        Tool("product_knowledge", "Versioned Pulse operating knowledge", "product_guidance", ["011", "029", "076", "999"], "source-controlled module documentation and current navigation contracts", "available_existing_adapter", "authenticated user with module visibility", "source_version", true, true, true, ["how do", "how can", "where", "what does", "guide", "steps", "can celar ai"], ["module_number", "route", "procedure_version", "safeguards"], ["#user-guide", "/api/celar-ai/v1/help-search/plan"]),
        Tool("governed_public_information", "Governed public current and general information", "public_knowledge", ["011", "064"], "approved public provider or search source with no Pulse payload", "available_only_when_module064_route_is_ready", "public question only; no private context, identifiers, records, documents, or tool results", "source_timestamp", false, true, false, ["current president", "latest version", "today", "current weather", "public regulation", "capital of", "explain"], ["public_source", "publication_date", "retrieval_time"], ["module064:public-general-knowledge"])
    ];

    public static IReadOnlyList<string> Domains => Tools
        .Select(tool => tool.Domain)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static IReadOnlyList<CelarAiUniversalToolCapability> Match(
        string normalizedQuestion,
        string? intentCode,
        int limit = 12)
    {
        var intent = (intentCode ?? string.Empty).Trim().ToLowerInvariant();
        var scored = Tools
            .Select(tool => new
            {
                Tool = tool,
                Score = tool.QuerySignals.Count(signal => normalizedQuestion.Contains(signal, StringComparison.OrdinalIgnoreCase))
                    + (tool.Domain.Replace('_', ' ').Contains(intent.Replace('_', ' '), StringComparison.OrdinalIgnoreCase) ? 2 : 0)
            })
            .Where(value => value.Score > 0)
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Tool.Code, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 30))
            .Select(value => value.Tool)
            .ToArray();

        return scored.Length > 0
            ? scored
            : Tools.Where(tool => tool.Code == "product_knowledge").ToArray();
    }

    public static bool HasDocumentSignal(string value) => ContainsAny(value,
        "sow", "gsd", "document", "attachment", "pdf", "docx", "pptx", "xlsx",
        "deliverable", "acceptance criteria", "customer responsibility", "citation", "page", "section");

    public static bool HasStructuredInternalSignal(string value) => ContainsAny(value,
        "project", "timesheet", "assignment", "engineer", "employee", "customer", "team",
        "financial", "budget", "cost", "margin", "expense", "invoice", "risk", "utilization",
        "capacity", "approval", "task", "contract", "audit", "module", "pulse", "celar ai");

    public static bool HasCurrentPublicSignal(string value) => ContainsAny(value,
        "current president", "current prime minister", "current governor", "current mayor",
        "latest version", "today's", "today ", "right now", "current weather", "this month",
        "this week", "recent news", "latest release", "current regulation");

    public static bool HasDiagnosticSignal(string value) => ContainsAny(value,
        "error", "failed", "broken", "why did", "403", "401", "500", "timeout",
        "unavailable", "diagnose", "health check", "deployment", "release", "api");

    public static bool HasProcedureSignal(string value) => ContainsAny(value,
        "how do i", "how can i", "how to", "where do i", "steps to", "what does", "can celar ai");

    private static bool ContainsAny(string value, params string[] signals) =>
        signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static CelarAiUniversalToolCapability Tool(
        string code,
        string displayName,
        string domain,
        string[] modules,
        string authority,
        string availability,
        string accessPolicy,
        string freshnessClass,
        bool deterministic,
        bool citationRequired,
        bool privateOnly,
        string[] querySignals,
        string[] sourceTypes,
        string[] routes) =>
        new(code, displayName, domain, modules, authority, availability, accessPolicy,
            freshnessClass, deterministic, citationRequired, privateOnly,
            MutationAllowed: false, querySignals, sourceTypes, routes);
}