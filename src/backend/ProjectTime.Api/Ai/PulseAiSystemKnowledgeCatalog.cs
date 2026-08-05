using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public sealed record PulseAiSystemIntentPlan(
    string IntentCode,
    string Mode,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> RequestedCapabilities,
    IReadOnlyList<string> RelevantModuleCodes,
    IReadOnlyList<string> NavigationTargets,
    IReadOnlyList<string> RequiredEvidence,
    bool WantsApiInventory,
    bool WantsTroubleshooting,
    bool WantsFutureEnhancement,
    bool WantsArchitecture,
    bool WantsLiveStatus,
    bool WantsProjectDocuments);

public static class PulseAiSystemKnowledgeCatalog
{
    private static readonly PulseAiSystemToolDefinition[] ToolDefinitions =
    [
        Tool(
            "platform_overview",
            "Platform Operations Overview",
            "013",
            "System Health & API Diagnostics",
            "/api/platform-operations/overview",
            "Runtime, resource, dependency, integration, worker, deployment, replica, and capability overview.",
            ["general_system", "troubleshooting", "api_inventory", "architecture", "release_and_deployment", "observability"],
            10,
            apiPermission: true,
            troubleshootingPermission: false,
            administrativeEvidence: true),
        Tool(
            "platform_api_inventory",
            "Registered API Inventory",
            "013",
            "System Health & API Diagnostics",
            "/api/platform-operations/apis",
            "Registered runtime APIs, health observations, ownership, dependencies, and safe-retest capability.",
            ["api_inventory", "troubleshooting", "architecture", "general_system"],
            5,
            apiPermission: true,
            troubleshootingPermission: false,
            administrativeEvidence: true),
        Tool(
            "operational_evidence",
            "Operational Evidence and Diagnostic History",
            "016",
            "Operational Evidence & Backup Retention",
            "/api/platform-operations/evidence?limit=250",
            "Recent API observations, failures, rejections, workers, scheduled jobs, correlation IDs, and dependency timeline.",
            ["troubleshooting", "api_inventory", "observability", "release_and_deployment", "general_system"],
            15,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "platform_architecture",
            "Provider-Neutral System Architecture",
            "068",
            "Provider-Neutral System Architecture",
            "/api/platform-operations/architecture",
            "Live layers, nodes, trust boundaries, integrations, module-to-API relationships, regions, and redundancy evidence.",
            ["architecture", "future_enhancement", "api_inventory", "general_system", "troubleshooting"],
            12,
            apiPermission: true,
            troubleshootingPermission: false,
            administrativeEvidence: true),
        Tool(
            "system_diagnostics_overview",
            "System Diagnostics Overview",
            "998",
            "System Diagnostic & Controlled Remediation Center",
            "/api/system-diagnostics/overview",
            "Diagnostic posture, runtime environment, categories, ownership, metrics, and remediation guardrails.",
            ["troubleshooting", "observability", "security", "general_system"],
            8,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "system_diagnostic_checks",
            "System Diagnostic Checks",
            "998",
            "System Diagnostic & Controlled Remediation Center",
            "/api/system-diagnostics/checks",
            "Sanitized live checks across the API, database, identity, integrations, workers, and runtime metadata.",
            ["troubleshooting", "observability", "security"],
            4,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "system_diagnostic_issues",
            "Active Diagnostic Issues",
            "998",
            "System Diagnostic & Controlled Remediation Center",
            "/api/system-diagnostics/issues",
            "Open warning, failure, and unknown diagnostic findings ranked by severity.",
            ["troubleshooting", "observability", "security", "release_and_deployment"],
            6,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "observability_overview",
            "Observability and SLO Overview",
            "078",
            "Observability, SLO & Application Health Center",
            "/api/observability-slo-health/overview",
            "Application-health, service, signal, SLO, alert, integration, and retention posture.",
            ["observability", "troubleshooting", "release_and_deployment", "general_system"],
            20,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "observability_services",
            "Observed Services",
            "078",
            "Observability, SLO & Application Health Center",
            "/api/observability-slo-health/services",
            "Registered service-health and dependency surfaces.",
            ["observability", "troubleshooting", "api_inventory"],
            23,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "observability_alerts",
            "Observability Alerts",
            "078",
            "Observability, SLO & Application Health Center",
            "/api/observability-slo-health/alerts",
            "Current alert definitions and authorized runtime alert evidence.",
            ["observability", "troubleshooting", "release_and_deployment"],
            24,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "release_overview",
            "Release and Deployment Overview",
            "077",
            "Release, Deployment & Rollback Control Center",
            "/api/release-deployment-control/overview",
            "Current release-control, deployment, evidence, gate, and rollback posture.",
            ["release_and_deployment", "troubleshooting", "future_enhancement", "general_system"],
            16,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "release_evidence",
            "Release Evidence",
            "077",
            "Release, Deployment & Rollback Control Center",
            "/api/release-deployment-control/evidence",
            "Authorized release and deployment evidence without executing a deployment action.",
            ["release_and_deployment", "troubleshooting"],
            18,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "defect_overview",
            "Defect Tracker Overview",
            "076",
            "Defect Intake & Resolution Tracker",
            "/api/defect-tracker/overview",
            "Defect intake, lifecycle, priority, ownership, persistence, notification, and integration posture.",
            ["troubleshooting", "future_enhancement", "product_help", "general_system"],
            31,
            apiPermission: false,
            troubleshootingPermission: false,
            administrativeEvidence: false),
        Tool(
            "defect_inventory",
            "Defect Inventory",
            "076",
            "Defect Intake & Resolution Tracker",
            "/api/defect-tracker/defects",
            "Authorized defect inventory and current implementation boundary.",
            ["troubleshooting", "release_and_deployment", "product_help"],
            32,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "module_availability",
            "Module Availability",
            "PLATFORM",
            "Shared Module Availability",
            "/api/module-availability",
            "Current module registration and enabled/disabled evidence.",
            ["general_system", "product_help", "identity_and_permissions", "troubleshooting", "future_enhancement"],
            25,
            apiPermission: false,
            troubleshootingPermission: false,
            administrativeEvidence: false),
        Tool(
            "ai_provider_configuration",
            "AI Provider Configuration",
            "064",
            "AI Provider Configuration Center",
            "/api/ai-configuration",
            "Sanitized provider, model, health, and feature-routing evidence; provider secrets are never returned.",
            ["documents_and_rag", "troubleshooting", "architecture", "future_enhancement", "general_system"],
            27,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "pulse_ai_rag_readiness",
            "Pulse AI Private RAG Readiness",
            "011",
            "Pulse AI",
            "/api/pulse-ai/v1/rag/readiness",
            "Private retrieval, inference, embedding, evidence, and privacy-boundary readiness.",
            ["documents_and_rag", "troubleshooting", "architecture", "future_enhancement", "general_system"],
            7,
            apiPermission: false,
            troubleshootingPermission: false,
            administrativeEvidence: false),
        Tool(
            "pulse_ai_document_runtime_readiness",
            "Pulse AI Document Runtime Readiness",
            "011",
            "Pulse AI",
            "/api/pulse-ai/v1/documents/runtime/readiness",
            "Document queue, scanner, extraction, OCR, embeddings, index, and worker readiness.",
            ["documents_and_rag", "troubleshooting", "architecture", "future_enhancement"],
            9,
            apiPermission: false,
            troubleshootingPermission: true,
            administrativeEvidence: true),
        Tool(
            "project_financial_portfolio",
            "Project Financial Portfolio",
            "030",
            "Reporting",
            "/api/project-financials/portfolio",
            "Authorized project financial truth, source status, and portfolio values.",
            ["financial_and_reporting", "projects_and_delivery", "future_enhancement"],
            35,
            apiPermission: false,
            troubleshootingPermission: false,
            administrativeEvidence: false),
        Tool(
            "project_financial_summary",
            "Project Financial Reporting Summary",
            "030",
            "Reporting",
            "/api/project-financials/reporting-summary",
            "Authorized financial summary, calculation definitions, source health, and reporting evidence.",
            ["financial_and_reporting", "projects_and_delivery", "future_enhancement"],
            36,
            apiPermission: false,
            troubleshootingPermission: false,
            administrativeEvidence: false)
    ];

    public static IReadOnlyList<PulseAiSystemToolDefinition> Tools => ToolDefinitions;

    public static PulseAiSystemIntentPlan Analyze(string question)
    {
        var normalized = Normalize(question);
        var wantsApis = ContainsAny(normalized,
            "api", "apis", "endpoint", "endpoints", "route", "routes",
            "what is running", "running on the system", "swagger");
        var wantsTroubleshooting = ContainsAny(normalized,
            "troubleshoot", "troubleshooting", "diagnose", "diagnostic", "debug",
            "why is", "why does", "failing", "failed", "failure", "error", "errors",
            "broken", "not working", "slow", "latency", "timeout", "unhealthy",
            "incident", "outage", "correlation id", "logs");
        var wantsEnhancement = ContainsAny(normalized,
            "future enhancement", "future enhancements", "enhance", "enhancement",
            "add a feature", "new feature", "build", "design", "architecture proposal",
            "roadmap", "next phase", "how should we implement", "could pulse");
        var wantsArchitecture = ContainsAny(normalized,
            "architecture", "architectural", "dependency", "dependencies", "data flow",
            "trust boundary", "integration", "integrations", "topology");
        var wantsLive = ContainsAny(normalized,
            "current", "currently", "live", "running", "status", "health", "today",
            "now", "active", "open", "latest", "recent");
        var wantsDocuments = ContainsAny(normalized,
            "sow", "gsd", "document", "documents", "design", "architecture document",
            "project plan", "flowhive", "timesheet suggestion");

        var intent = wantsEnhancement
            ? "future_enhancement"
            : wantsTroubleshooting
                ? "troubleshooting"
                : wantsApis
                    ? "api_inventory"
                    : ContainsAny(normalized, "release", "deployment", "rollback", "pipeline", "revision")
                        ? "release_and_deployment"
                        : ContainsAny(normalized, "observability", "slo", "alert", "metric", "telemetry", "performance")
                            ? "observability"
                            : ContainsAny(normalized, "security", "threat", "incident", "audit", "privacy")
                                ? "security"
                                : ContainsAny(normalized, "financial", "finance", "cost", "margin", "revenue", "invoice", "billing", "report")
                                    ? "financial_and_reporting"
                                    : ContainsAny(normalized, "project", "customer", "assignment", "flowhive", "work register")
                                        ? "projects_and_delivery"
                                        : ContainsAny(normalized, "timesheet", "approval", "hours", "afterhours", "time entry")
                                            ? "timesheets_and_approvals"
                                            : ContainsAny(normalized, "permission", "role", "access", "403", "identity", "view as")
                                                ? "identity_and_permissions"
                                                : wantsDocuments
                                                    ? "documents_and_rag"
                                                    : ContainsAny(normalized, "how do i", "where do i", "what is", "what does", "help")
                                                        ? "product_help"
                                                        : wantsArchitecture
                                                            ? "architecture"
                                                            : "general_system";

        var domains = new List<string> { intent };
        if (wantsApis && intent != "api_inventory") domains.Add("api_inventory");
        if (wantsTroubleshooting && intent != "troubleshooting") domains.Add("troubleshooting");
        if (wantsEnhancement && intent != "future_enhancement") domains.Add("future_enhancement");
        if (wantsArchitecture && intent != "architecture") domains.Add("architecture");
        if (wantsDocuments && intent != "documents_and_rag") domains.Add("documents_and_rag");

        var modules = ModulesFor(normalized, intent);
        var navigation = NavigationFor(intent, modules);
        var evidence = EvidenceFor(intent, wantsApis, wantsTroubleshooting, wantsEnhancement, wantsDocuments);
        var capabilities = new List<string>();
        if (wantsApis) capabilities.Add("live runtime API discovery");
        if (wantsTroubleshooting) capabilities.Add("read-only troubleshooting and root-cause analysis");
        if (wantsEnhancement) capabilities.Add("future enhancement architecture and delivery blueprint");
        if (wantsArchitecture) capabilities.Add("current architecture and dependency analysis");
        if (wantsDocuments) capabilities.Add("authorized private document retrieval");
        if (wantsLive) capabilities.Add("current live system evidence");
        if (capabilities.Count == 0) capabilities.Add("comprehensive Pulse product and system guidance");

        var mode = intent switch
        {
            "api_inventory" => "api_inventory",
            "troubleshooting" => "troubleshooting",
            "future_enhancement" => "future_enhancement",
            _ => "system_help"
        };

        return new PulseAiSystemIntentPlan(
            IntentCode: intent,
            Mode: mode,
            Domains: domains.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RequestedCapabilities: capabilities,
            RelevantModuleCodes: modules,
            NavigationTargets: navigation,
            RequiredEvidence: evidence,
            WantsApiInventory: wantsApis || intent is "api_inventory" or "architecture" or "future_enhancement",
            WantsTroubleshooting: wantsTroubleshooting || intent is "troubleshooting" or "observability" or "release_and_deployment" or "security",
            WantsFutureEnhancement: wantsEnhancement || intent == "future_enhancement",
            WantsArchitecture: wantsArchitecture || intent is "architecture" or "future_enhancement",
            WantsLiveStatus: wantsLive || intent is "troubleshooting" or "api_inventory" or "release_and_deployment" or "observability",
            WantsProjectDocuments: wantsDocuments);
    }

    public static IReadOnlyList<PulseAiSystemToolDefinition> SelectTools(
        PulseAiSystemIntentPlan plan,
        PulseAiSystemAccess access,
        int maximum)
    {
        maximum = Math.Clamp(maximum, 1, 30);
        return ToolDefinitions
            .Where(tool => tool.Intents.Any(intent => plan.Domains.Contains(intent, StringComparer.OrdinalIgnoreCase)))
            .Where(tool => !tool.RequiresApiInventoryPermission || access.CanViewApis)
            .Where(tool => !tool.RequiresTroubleshootingPermission || access.CanTroubleshoot)
            .OrderBy(tool => tool.Priority)
            .ThenBy(tool => tool.Code)
            .Take(maximum)
            .ToArray();
    }

    public static PulseAiEnhancementBlueprint BuildEnhancementBlueprint(
        string question,
        PulseAiSystemIntentPlan plan,
        IReadOnlyList<PulseAiSystemApiDescriptor> relevantApis,
        IReadOnlyList<PulseAiSystemToolResult> tools)
    {
        var affectedModules = plan.RelevantModuleCodes.Count > 0
            ? plan.RelevantModuleCodes
            : relevantApis.Select(api => api.ModuleCode).Where(code => code != "PLATFORM").Distinct().Take(12).ToArray();
        var currentCapabilities = new List<string>
        {
            $"Pulse currently exposes {relevantApis.Count} relevant registered API route(s) for the selected scope.",
            $"{tools.Count(result => result.Succeeded)} governed read tool(s) returned current evidence and {tools.Count(result => !result.Succeeded)} did not return a successful result.",
            "Module 011 provides durable private document processing, private RAG, citation evidence, answer audit, and user feedback controls.",
            "Module 013 owns the registered API inventory and safe read-only retest boundary; Modules 016, 078, and 998 provide operational evidence, observability, and diagnostic evidence.",
            "Module 064 remains the provider credential, health, routing, and circuit-breaker authority."
        };
        var gaps = new List<string>
        {
            "Confirm the exact business owner, personas, permission model, record scope, and measurable success criteria.",
            "Identify which existing module is the source of truth and avoid duplicating calculations or operational state in Pulse AI.",
            "Define current and future API contracts, migration ownership, audit evidence, retention, and rollback before enabling mutation.",
            "Create frozen functional, authorization, privacy, reliability, and regression tests before production promotion."
        };
        gaps.AddRange(tools.Where(result => !result.Succeeded)
            .Take(8)
            .Select(result => $"Current source gap: {result.ToolName} returned {result.Status} ({result.StatusCode}) and requires authorization, configuration, or service review."));

        var proposedApis = relevantApis.Count > 0
            ? relevantApis.Take(20).Select(api => $"Preserve or extend {api.Method} {api.RoutePattern} ({api.ModuleCode} — {api.ModuleName}) through a versioned contract.").ToArray()
            : ["Define a versioned, permission-aware read contract before adding any mutation endpoint."];

        return new PulseAiEnhancementBlueprint(
            RequestedCapability: Limit(question, 2_000),
            BusinessOutcome: "Deliver the requested capability through the existing Pulse authorization, source-of-truth, audit, operational, and rollback architecture rather than creating an isolated AI-only workflow.",
            AffectedModules: affectedModules,
            CurrentCapabilities: currentCapabilities,
            Gaps: gaps,
            ProposedArchitecture:
            [
                "Experience layer: add the feature to the owning Pulse module and expose Pulse AI as an assistive, explainable interface rather than the system of record.",
                "Authorization layer: resolve actual/effective identity, module/action permission, project/customer/team/record scope, and field restrictions before retrieval or tool execution.",
                "Data layer: consume the owning module’s authoritative records and approved documents through read-only contracts; use a separately reviewed migration only for new durable metadata.",
                "Intelligence layer: use deterministic services for permissions, calculations, schedules, and state; use private RAG/model reasoning for explanation, synthesis, and drafts.",
                "Operations layer: register live API inventory, health, correlation IDs, source freshness, diagnostic checks, SLOs, alerts, and rollback evidence.",
                "External-provider layer: route only sanitized generic reasoning through Module 064 when policy allows; keep restricted internal context in the private Pulse boundary."
            ],
            ProposedApis: proposedApis,
            DataAndMigrationConsiderations:
            [
                "Use additive, idempotent migrations with a reviewed rollback and apply/rollback/reapply test.",
                "Store authoritative business values in the owning module; store only orchestration, evidence, feedback, or lifecycle metadata in Module 011.",
                "Record source contract versions, checksums, correlation IDs, actual/effective users, timestamps, and data-as-of evidence.",
                "Do not place credentials, unrestricted prompts, model weights, or large binary artifacts in PostgreSQL."
            ],
            SecurityAndPrivacyControls:
            [
                "Fail closed when identity, permission, record scope, source health, or current authorization cannot be established.",
                "Treat View-As as read-only and never transfer mutation authority.",
                "Apply authorization before search, ranking, tool execution, prompt assembly, and result display.",
                "Keep raw SOW, GSD, contract, architecture, customer, employee, rate, and financial content in the private Pulse boundary.",
                "Use allowlisted same-origin GET tools for diagnostics; require exact confirmation and elevated permission for any safe retest.",
                "Preserve prompt-injection defenses, output validation, citations, confidence, and unsupported-claim controls."
            ],
            OperationalAndSupportControls:
            [
                "Add Module 013 API ownership, Module 016 correlation evidence, Module 078 service/SLO/alert coverage, Module 076 defect intake, and Module 998 diagnostic checks.",
                "Define health checks, timeouts, retries, circuit breakers, queue limits, source freshness, capacity, and degraded operation.",
                "Make every support answer include direct conclusion, current state, evidence, hypotheses, diagnostic sequence, risk, and next action.",
                "Preserve safe rollback for code, migration, prompt, model, route, and configuration changes."
            ],
            ImplementationPhases:
            [
                "Phase 1 — discovery, source-of-truth ownership, architecture decision records, permissions, and acceptance criteria.",
                "Phase 2 — read-only API/data contracts, UI preview, audit evidence, and diagnostic visibility.",
                "Phase 3 — durable metadata, migrations, background processing, private AI integration, and bounded tool execution.",
                "Phase 4 — UAT, security/privacy tests, load and failure testing, observability, runbooks, and support readiness.",
                "Phase 5 — guarded Test promotion, production approval, canary where applicable, validation, and cleanup of temporary controls."
            ],
            TestStrategy:
            [
                "Unit tests for classification, permissions, deterministic calculations, schema validation, and unsafe-input rejection.",
                "Migration apply, idempotence, rollback, and reapply tests with preserved data and permission evidence.",
                "Integration tests for actual/effective identity, View-As blocking, record scope, API ownership, timeouts, and dependency failures.",
                "AI evaluations for citation accuracy, unsupported claims, prompt injection, information leakage, answer completeness, and stable structured output.",
                "Complete API, frontend, production-container, protected-flow, and cross-module regression builds."
            ],
            RolloutAndRollback:
            [
                "Deploy source disabled by default when new runtime adapters or workers are involved.",
                "Apply migrations through a private-network, checksum-pinned job and verify without additional writes.",
                "Promote exact immutable images to Test, validate health/version and user workflows, and preserve candidate-only image rollback.",
                "Enable capability by role/feature only after UAT and operational evidence pass.",
                "Retain the previous code, schema rollback, prompt/model route, and configuration target until the validation window closes."
            ],
            Risks:
            [
                "Over-broad retrieval or tool access could expose records outside the effective user’s scope.",
                "A model can produce convincing but unsupported statements unless authoritative tools, citations, and confidence gates remain mandatory.",
                "Duplicated business logic can create conflicting financial, schedule, permission, or workflow answers.",
                "A future enhancement without observability and rollback can increase operational risk even when the feature works normally.",
                "External-provider escalation can create data-loss risk if sanitization and Module 064 routing are bypassed."
            ],
            AcceptanceCriteria:
            [
                "The feature answers the approved business use cases with complete, source-grounded detail and explicit uncertainty.",
                "Unauthorized retrieval, tool execution, and record display tests return zero restricted content.",
                "Owning-module calculations and state match Pulse AI explanations exactly.",
                "All new APIs appear in the live API inventory with module ownership, permission, dependency, and safe-retest classification.",
                "Operational failures produce correlation evidence, diagnostic guidance, and a tested rollback path.",
                "No raw restricted context reaches an unapproved external model or browser surface."
            ],
            Dependencies:
            [
                "Modules 011, 012, 013, 016, 037, 064, 068, 076, 077, 078, 998, and the owning business module.",
                "Pulse PostgreSQL, authenticated application shell, source-controlled migrations, complete CI, and guarded environment promotion.",
                "Approved private inference/embedding/OCR services only when the enhancement requires those capabilities."
            ]);
    }

    public static (string ModuleCode, string ModuleName) InferModule(string route)
    {
        var value = route.ToLowerInvariant();
        foreach (var entry in ModuleRoutes)
        {
            if (entry.Prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return (entry.Code, entry.Name);
        }
        return ("PLATFORM", "Shared platform API");
    }

    public static string PurposeFor(string route, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName)) return Limit(displayName, 240);
        var owner = InferModule(route);
        var action = route.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "endpoint";
        action = Regex.Replace(action, @"\{[^}]+\}", "record");
        action = action.Replace('-', ' ').Replace('_', ' ');
        return $"{owner.ModuleName}: {action}";
    }

    private static PulseAiSystemToolDefinition Tool(
        string code,
        string name,
        string moduleCode,
        string moduleName,
        string path,
        string purpose,
        string[] intents,
        int priority,
        bool apiPermission,
        bool troubleshootingPermission,
        bool administrativeEvidence) =>
        new(
            Code: code,
            Name: name,
            ModuleCode: moduleCode,
            ModuleName: moduleName,
            Method: "GET",
            Path: path,
            Purpose: purpose,
            Intents: intents,
            Priority: priority,
            RequiresApiInventoryPermission: apiPermission,
            RequiresTroubleshootingPermission: troubleshootingPermission,
            AdministrativeEvidence: administrativeEvidence,
            SafeReadOnly: true);

    private static IReadOnlyList<string> ModulesFor(string normalized, string intent)
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "011" };
        foreach (Match match in Regex.Matches(normalized, @"\b(?:module\s*)?(\d{3}|055[a-d])\b", RegexOptions.IgnoreCase))
            modules.Add(match.Groups[1].Value.ToUpperInvariant());
        if (intent == "api_inventory") modules.UnionWith(["013", "016", "068"]);
        if (intent == "troubleshooting") modules.UnionWith(["013", "016", "076", "078", "998"]);
        if (intent == "future_enhancement") modules.UnionWith(["012", "013", "037", "064", "068", "076", "077", "078", "998"]);
        if (intent == "release_and_deployment") modules.UnionWith(["058", "077", "078"]);
        if (intent == "observability") modules.UnionWith(["013", "016", "078", "998"]);
        if (intent == "security") modules.UnionWith(["008", "037", "079", "997", "998"]);
        if (intent == "financial_and_reporting") modules.UnionWith(["003", "005", "030", "038", "039", "042", "055B", "060"]);
        if (intent == "projects_and_delivery") modules.UnionWith(["018", "019", "020", "033", "055C", "055D", "066", "070"]);
        if (intent == "timesheets_and_approvals") modules.UnionWith(["001", "002", "007", "023"]);
        if (intent == "identity_and_permissions") modules.UnionWith(["009", "010", "012", "037", "062", "065"]);
        if (intent == "documents_and_rag") modules.UnionWith(["001", "011", "019", "066"]);
        return modules.OrderBy(value => value).ToArray();
    }

    private static IReadOnlyList<string> NavigationFor(string intent, IReadOnlyList<string> modules)
    {
        var targets = new List<string> { "#work-task-builder" };
        targets.AddRange(intent switch
        {
            "api_inventory" => ["#service-control", "#system-architecture"],
            "troubleshooting" => ["#service-control", "#system-diagnostics", "#observability-slo-health", "#defect-tracker"],
            "future_enhancement" => ["#system-architecture", "#roles-permissions-matrix", "#release-deployment-control", "#user-guide"],
            "release_and_deployment" => ["#release-deployment-control", "#cicd-pipeline", "#observability-slo-health"],
            "observability" => ["#observability-slo-health", "#service-control", "#system-diagnostics"],
            "security" => ["#security-operations", "#system-diagnostics", "#audit-history", "#data-governance-retention"],
            "financial_and_reporting" => ["#reporting", "#billing-readiness", "#invoice-billing-center", "#rate-card-administration"],
            "projects_and_delivery" => ["#project-workspace", "#work-register", "#create-work-register", "#project-flowhive"],
            "timesheets_and_approvals" => ["#timesheet", "#manager-approval", "#workflow"],
            "identity_and_permissions" => ["#user-admin", "#role-admin", "#roles-permissions-matrix"],
            "documents_and_rag" => ["#project-workspace", "#project-flowhive", "#timesheet"],
            _ => ["#user-guide", "#modules-directory"]
        });
        foreach (var module in modules)
        {
            if (ModuleNavigation.TryGetValue(module, out var target)) targets.Add(target);
        }
        return targets.Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
    }

    private static IReadOnlyList<string> EvidenceFor(
        string intent,
        bool wantsApis,
        bool wantsTroubleshooting,
        bool wantsEnhancement,
        bool wantsDocuments)
    {
        var evidence = new List<string>
        {
            "Current actual/effective identity, roles, module permissions, and record scope.",
            "Current source contract versions, release SHA, data-as-of time, and source health."
        };
        if (wantsApis || intent == "api_inventory")
            evidence.Add("The running ASP.NET EndpointDataSource inventory with HTTP methods, route patterns, endpoint metadata, module ownership, and safe-retest classification.");
        if (wantsTroubleshooting || intent == "troubleshooting")
        {
            evidence.Add("Module 013 API/runtime inventory and Module 016 correlation evidence.");
            evidence.Add("Module 078 observability/SLO/alert surfaces and Module 998 diagnostic findings.");
            evidence.Add("Release, dependency, provider, database, worker, and integration readiness where authorized.");
        }
        if (wantsEnhancement || intent == "future_enhancement")
        {
            evidence.Add("Current architecture, module/API relationships, permissions, migrations, operational controls, and known implementation boundaries.");
            evidence.Add("Affected personas, business outcome, gaps, dependencies, security controls, testing, rollout, rollback, and measurable acceptance criteria.");
        }
        if (wantsDocuments)
            evidence.Add("Only current authorized private document versions and citation-preserving chunks; no raw document content is returned to the browser.");
        return evidence;
    }

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string Limit(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static readonly IReadOnlyDictionary<string, string> ModuleNavigation =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["001"] = "#timesheet", ["002"] = "#manager-approval", ["003"] = "#utilization",
            ["005"] = "#project-allocation-info", ["008"] = "#audit-history", ["009"] = "#user-admin",
            ["010"] = "#azure-admin", ["011"] = "#work-task-builder", ["012"] = "#role-admin",
            ["013"] = "#service-control", ["016"] = "#backup-retention", ["018"] = "#project-workload",
            ["019"] = "#project-workspace", ["020"] = "#project-intake", ["023"] = "#time-compliance",
            ["030"] = "#reporting", ["033"] = "#project-forge", ["037"] = "#roles-permissions-matrix", ["038"] = "#certify-integration",
            ["039"] = "#billing-readiness", ["042"] = "#invoice-billing-center", ["055B"] = "#rate-card-administration",
            ["055C"] = "#work-register", ["055D"] = "#create-work-register", ["058"] = "#cicd-pipeline",
            ["060"] = "#contracts", ["062"] = "#profile", ["064"] = "#ai-provider-configuration",
            ["065"] = "#entra-secret-administration", ["066"] = "#project-flowhive", ["068"] = "#system-architecture",
            ["070"] = "#capacity-pipeline-forecast", ["076"] = "#defect-tracker", ["077"] = "#release-deployment-control",
            ["078"] = "#observability-slo-health", ["079"] = "#data-governance-retention", ["997"] = "#security-operations",
            ["998"] = "#system-diagnostics", ["999"] = "#user-guide"
        };

    private static readonly (string Code, string Name, string[] Prefixes)[] ModuleRoutes =
    [
        ("001", "Timesheet", ["/api/timesheet", "/api/time-entr", "/api/users/timesheet"]),
        ("002", "Approval Inbox", ["/api/manager/approval", "/api/approval-center", "/api/scoped-approval"]),
        ("003", "Utilization", ["/api/utilization"]),
        ("004", "Holiday Administration", ["/api/holiday"]),
        ("005", "Project Expense Upload", ["/api/project-expense", "/api/project-allocation"]),
        ("006", "Toyota & Hyundai Pipelines", ["/api/toyota", "/api/hyundai", "/api/psa-modules"]),
        ("007", "Approval, Export & Audit Workflow", ["/api/workflow", "/api/accounting", "/api/export"]),
        ("008", "Audit History", ["/api/admin/audit", "/api/audit/history"]),
        ("009", "User Administration", ["/api/admin/users", "/api/user-administration"]),
        ("010", "Azure / Entra Directory Users", ["/api/admin/azure"]),
        ("011", "Pulse AI", ["/api/pulse-ai", "/api/ai-configuration", "/api/ai/"]),
        ("012", "Role Administration", ["/api/role-policy", "/api/rbac"]),
        ("013", "System Health & API Diagnostics", ["/api/platform-operations/overview", "/api/platform-operations/apis", "/api/system/service-control", "/api/system/api-status", "/api/system/version-inventory"]),
        ("014", "Backup & Disaster Recovery", ["/api/system/backup-dr"]),
        ("015", "Restore Validation", ["/api/system/restore-validation"]),
        ("016", "Operational Evidence & Backup Retention", ["/api/platform-operations/evidence", "/api/system/backup-retention"]),
        ("017", "Replication & Sync", ["/api/system/replication-sync"]),
        ("018", "Project Workload", ["/api/project-workload"]),
        ("019", "Project Workspace", ["/api/project-workspace"]),
        ("020", "Project Intake & Resource Handoff", ["/api/project-intake"]),
        ("021", "Customer Directory", ["/api/customers"]),
        ("022", "Cost Alerts", ["/api/cost-alert"]),
        ("023", "Time Compliance", ["/api/time-compliance"]),
        ("026", "CRM / ERP Integration Center", ["/api/integrations/026"]),
        ("030", "Reporting", ["/api/report", "/api/project-financials", "/api/financial-operations"]),
        ("033", "Project Forge", ["/api/project-forge"]),
        ("037", "Roles & Permissions Matrix", ["/api/roles-permissions"]),
        ("038", "Certify Connection & Sync", ["/api/certify"]),
        ("039", "Billing Readiness", ["/api/billing-readiness"]),
        ("040", "Project Closeout", ["/api/project-closeout"]),
        ("041", "Closeout Email Automation", ["/api/closeout-email"]),
        ("042", "Invoice & Billing Center", ["/api/invoice", "/api/billing"]),
        ("055B", "Rate Card Administration", ["/api/rate-card"]),
        ("055C", "Manage Existing Projects", ["/api/work-register"]),
        ("055D", "Create New Project", ["/api/work-register/create"]),
        ("057", "Calendar & Capacity", ["/api/calendar", "/api/capacity"]),
        ("058", "CI/CD Pipeline", ["/api/cicd"]),
        ("060", "Contracts", ["/api/contracts"]),
        ("064", "AI Provider Configuration", ["/api/ai-provider"]),
        ("065", "Microsoft Integration Connection", ["/api/microsoft-integration", "/api/global-mail", "/api/auth/sso"]),
        ("066", "Project FlowHive", ["/api/project-flowhive", "/api/flowhive"]),
        ("068", "Provider-Neutral System Architecture", ["/api/platform-operations/architecture", "/api/system-architecture"]),
        ("069", "Qualifications & Certifications", ["/api/qualifications"]),
        ("070", "Capacity & Pipeline Forecasting", ["/api/capacity-forecast"]),
        ("071", "On-Call Scheduling", ["/api/oncall"]),
        ("072", "OneAssist Routing Directory", ["/api/oneassist"]),
        ("073", "Sales Coverage Alignment", ["/api/sales-coverage"]),
        ("074", "OEM & Vendor Directory", ["/api/oem"]),
        ("075", "Integration Event Gateway", ["/api/integration-event"]),
        ("076", "Defect Tracker", ["/api/defect"]),
        ("077", "Release & Deployment Control", ["/api/release-deployment"]),
        ("078", "Observability & SLO Health", ["/api/observability"]),
        ("079", "Data Governance & Retention", ["/api/data-governance"]),
        ("080", "Customer Delivery Acceptance", ["/api/customer-delivery"]),
        ("997", "Security Operations", ["/api/security-operations"]),
        ("998", "System Diagnostics & Controlled Remediation", ["/api/system-diagnostics"])
    ];
}
