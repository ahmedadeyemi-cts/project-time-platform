using ProjectTime.Api.Modules;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiFutureEnhancementPlanner
{
    private readonly PulseAiPrivateRagRepository _accessRepository;
    private readonly PulseAiSystemOperationsService _operations;
    private readonly PulseAiSystemOperationsIntentClassifier _operationsClassifier;
    private readonly PulseAiSystemOperationsRepository _repository;

    public PulseAiFutureEnhancementPlanner(
        PulseAiPrivateRagRepository accessRepository,
        PulseAiSystemOperationsService operations,
        PulseAiSystemOperationsIntentClassifier operationsClassifier,
        PulseAiSystemOperationsRepository repository)
    {
        _accessRepository = accessRepository;
        _operations = operations;
        _operationsClassifier = operationsClassifier;
        _repository = repository;
    }

    public bool IsFutureEnhancementQuestion(string? question) =>
        PulseAiSystemKnowledgeCatalog.IsFutureEnhancementQuestion(question);

    public async Task<bool> CanPlanAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var access = await _accessRepository.LoadAccessAsync(userId, cancellationToken);
        return access.IsActive
            && (access.IsSuperAdministrator
                || access.CanHelpSearch
                || access.PermissionCodes.Contains(PulseAiSystemOperationsPolicy.FutureEnhancementPermission));
    }

    public async Task<PulseAiFutureEnhancementPlan> PlanAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiFutureEnhancementRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var question = Clean(request.Question, 6_000);
        var access = await _accessRepository.LoadAccessAsync(effectiveUserId, cancellationToken);
        if (question.Length == 0)
            return Blocked("A future enhancement question is required.");
        if (!access.IsActive
            || !(access.IsSuperAdministrator
                || access.CanHelpSearch
                || access.PermissionCodes.Contains(PulseAiSystemOperationsPolicy.FutureEnhancementPermission)))
            return Blocked("The current effective user is not authorized to create a Pulse future-enhancement plan.");

        var modules = PulseAiSystemKnowledgeCatalog.Match(question, 15).ToList();
        if (modules.Count == 0)
        {
            modules.AddRange(DefaultCrossCuttingModules());
        }
        else
        {
            AddCrossCuttingModules(modules, question);
        }
        modules = modules
            .GroupBy(module => module.ModuleNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(20)
            .ToList();

        var operationsAccess = await _operations.LoadAccessAsync(actualUserId, cancellationToken);
        var currentApis = Array.Empty<PulseAiSystemApiRecord>();
        PulseAiSystemOperationsSnapshot? snapshot = null;
        if (request.IncludeLiveApiEvidence && _operations.CanRead(operationsAccess))
        {
            var classification = _operationsClassifier.Classify(question) with
            {
                Intent = "api_inventory",
                WantsAllApis = true,
                ModuleCode = string.Empty,
                StatusFilter = string.Empty,
                ApiPath = string.Empty,
                ApiMethod = string.Empty,
                ApiId = string.Empty,
                CorrelationId = string.Empty
            };
            snapshot = await PlatformOperationsModule.BuildPulseAiSystemOperationsSnapshotAsync(
                context,
                new PulseAiSystemOperationsQuery(question, classification, 500, true, false),
                cancellationToken);
            var moduleNumbers = modules.Select(module => module.ModuleNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
            currentApis = snapshot.AllApis
                .Where(api => moduleNumbers.Contains(api.ModuleCode))
                .OrderBy(api => api.ModuleCode)
                .ThenBy(api => api.Path)
                .ThenBy(api => api.Method)
                .Take(200)
                .ToArray();
        }

        var currentCapabilities = CurrentCapabilities(modules, currentApis);
        var gaps = CapabilityGaps(question, modules, currentApis);
        var architecture = ProposedArchitecture(question, modules);
        var dataChanges = DataAndMigrations(question, modules);
        var apiChanges = ApiAndIntegrations(question, modules, currentApis);
        var permissions = Permissions(question, modules);
        var privacy = PrivacyAndSecurity(question, modules);
        var observability = ObservabilityAndAudit(modules);
        var testing = TestingStrategy(question, modules);
        var release = ReleaseSequence(modules);
        var acceptance = AcceptanceCriteria(question, modules);
        var dependencies = Dependencies(modules, currentApis);
        var risks = Risks(question, modules);
        var phases = EstimatedPhases(question, modules);
        var title = Title(question);
        var createdAt = DateTimeOffset.UtcNow;

        var answer = new PulseAiPrivateDetailedAnswer(
            DirectConclusion: $"Pulse can support this enhancement. The recommended implementation affects {modules.Count:N0} primary module(s), reuses {currentApis.Length:N0} currently registered API route(s) where applicable, and should be delivered through the phased, permission-aware architecture below rather than as an isolated AI feature.",
            ExecutiveSummary: $"{title}. The plan compares current Pulse capabilities with the requested outcome, identifies the gaps, maps the owning modules and APIs, and defines data, security, testing, deployment, audit, and acceptance requirements.",
            ScopeAndFilters:
            [
                $"Question: {question}",
                $"Affected modules: {string.Join(", ", modules.Select(module => $"{module.ModuleNumber} {module.DisplayName}"))}.",
                $"Live API evidence included: {(snapshot is null ? "No; the current user did not request or was not authorized for operations inventory." : $"Yes; release {snapshot.Runtime.ReleaseSha}, data as of {snapshot.DataAsOf:O}.")}",
                "This is a proposed enhancement architecture, not confirmation that the feature already exists or has been approved."
            ],
            DetailedAnalysis:
            [
                .. currentCapabilities.Select(value => $"Current capability — {value}"),
                .. gaps.Select(value => $"Gap — {value}"),
                .. architecture.Select(value => $"Architecture — {value}"),
                .. dataChanges.Select(value => $"Data — {value}"),
                .. apiChanges.Select(value => $"API/Integration — {value}")
            ],
            SourceEvidence:
            [
                .. modules.Select(module => $"Pulse module catalog: Module {module.ModuleNumber} {module.DisplayName}, route #{module.Route}, group {module.Group}; {module.Purpose}"),
                .. currentApis.Take(50).Select(api => $"Running API inventory: {api.Method} {api.Path}; Module {api.ModuleCode}; status {api.CurrentStatus}; current release {api.CurrentRelease}."),
                "Pulse AI architecture and governance contracts: private-first reasoning, authorization before retrieval, governed tools, human approval, audit, and controlled deployment."
            ],
            Calculations:
            [
                $"Primary module impact count: {modules.Count:N0}.",
                $"Existing relevant API count: {currentApis.Length:N0}.",
                $"Planned implementation phases: {phases.Count:N0}.",
                $"Acceptance controls: {acceptance.Count:N0}; identified risks: {risks.Count:N0}."
            ],
            KnownUnknownAndStaleValues:
            [
                "Known: the current module ownership and any live API routes listed in this plan.",
                "Known: the required architecture, authorization, privacy, validation, and release-control patterns already used by Pulse.",
                "Unknown until discovery: detailed user stories, data volume, expected concurrency, response-time objective, final UI design, external-system contracts, and business approval.",
                "Unknown until implementation planning: whether a migration, private model, new provider adapter, or infrastructure resource is required for every requested sub-capability.",
                snapshot is null ? "Live operations inventory was not included, so API-count and runtime-status claims are intentionally limited." : $"Live API evidence represents release {snapshot.Runtime.ReleaseSha} at {snapshot.DataAsOf:O}; later releases may differ."
            ],
            Assumptions:
            [
                "The enhancement must preserve existing module ownership rather than duplicating business logic inside Module 011.",
                "Pulse authentication, Modules 012/037 permissions, record scope, View-As rules, and audit requirements remain authoritative.",
                "Changing business facts remain in live APIs or private retrieval rather than being trained permanently into a model.",
                "Any state-changing action requires an owning module API, explicit permission, validation, audit, and human approval where applicable."
            ],
            Conflicts:
            [
                .. DetectConflicts(question, modules),
                "A proposed capability must not create a second source of truth when an owning Pulse module already provides the data or calculation."
            ],
            Limitations:
            [
                "This plan does not create source code, apply a migration, configure infrastructure, activate an AI model, call an external provider, or deploy an environment.",
                "Effort and dates require sizing against current source, data volume, integration contracts, infrastructure, and acceptance criteria.",
                "External platforms may impose licensing, API, rate-limit, data-residency, and retention constraints that require separate review."
            ],
            RisksAndImplications: risks,
            RecommendedActions:
            [
                "Approve the business outcome, users, scope, and measurable acceptance criteria.",
                "Confirm module ownership, API/data source authority, permission design, and whether the enhancement is read-only or state-changing.",
                "Complete technical discovery for data volume, integrations, private networking, model/tool requirements, audit, retention, and resiliency.",
                "Implement the phases below as isolated source PRs with migration, infrastructure, deployment, and activation gates separated.",
                "Run role, record-scope, security, privacy, functional, regression, performance, failover, and rollback validation before production promotion."
            ],
            NavigationTargets: modules.Select(module => $"#{module.Route}").Concat(["#work-task-builder", "#user-guide", "#release-deployment-control"]).Distinct().Take(25).ToArray(),
            CitationIds: [],
            Confidence: snapshot is null ? 0.78m : 0.88m,
            ConfidenceExplanation: snapshot is null
                ? "Confidence reflects the comprehensive Pulse module and architecture catalog; live API evidence was not available to this request."
                : $"Confidence reflects the Pulse module catalog plus {currentApis.Length:N0} relevant APIs observed in running release {snapshot.Runtime.ReleaseSha}.",
            DataAsOf: snapshot?.DataAsOf ?? createdAt);

        var provisional = new PulseAiFutureEnhancementPlan(
            PlanId: Guid.Empty,
            Status: "draft_plan_ready",
            Title: title,
            Answer: answer,
            AffectedModules: modules,
            CurrentApis: currentApis,
            CurrentCapabilities: currentCapabilities,
            CapabilityGaps: gaps,
            ProposedArchitecture: architecture,
            DataAndMigrationChanges: dataChanges,
            ApiAndIntegrationChanges: apiChanges,
            PermissionAndRoleChanges: permissions,
            PrivacyAndSecurityControls: privacy,
            ObservabilityAndAudit: observability,
            TestingStrategy: testing,
            ReleaseSequence: release,
            AcceptanceCriteria: acceptance,
            Dependencies: dependencies,
            Risks: risks,
            EstimatedPhases: phases,
            CreatedAt: createdAt,
            Persisted: false);
        var planId = request.PersistDraft
            ? await _repository.SaveFutureEnhancementPlanAsync(
                actualUserId,
                effectiveUserId,
                title,
                question,
                provisional.ToPublicResponse(),
                modules.Select(module => module.ModuleNumber).ToArray(),
                cancellationToken)
            : Guid.Empty;
        return provisional with { PlanId = planId, Persisted = planId != Guid.Empty };
    }

    private static List<PulseAiModuleKnowledge> DefaultCrossCuttingModules() =>
    [
        PulseAiSystemKnowledgeCatalog.ByNumber("011")!,
        PulseAiSystemKnowledgeCatalog.ByNumber("012")!,
        PulseAiSystemKnowledgeCatalog.ByNumber("037")!,
        PulseAiSystemKnowledgeCatalog.ByNumber("077")!,
        PulseAiSystemKnowledgeCatalog.ByNumber("078")!,
        PulseAiSystemKnowledgeCatalog.ByNumber("079")!
    ];

    private static void AddCrossCuttingModules(List<PulseAiModuleKnowledge> modules, string question)
    {
        var lower = question.ToLowerInvariant();
        Add(modules, "012");
        Add(modules, "037");
        Add(modules, "077");
        if (ContainsAny(lower, "ai", "search", "document", "llm", "model", "assistant", "copilot"))
        {
            Add(modules, "011"); Add(modules, "064"); Add(modules, "079");
        }
        if (ContainsAny(lower, "api", "system", "troubleshoot", "diagnostic", "health", "observability"))
        {
            Add(modules, "013"); Add(modules, "016"); Add(modules, "078"); Add(modules, "998");
        }
        if (ContainsAny(lower, "integration", "connector", "webhook", "event"))
        {
            Add(modules, "026"); Add(modules, "065"); Add(modules, "075");
        }
    }

    private static void Add(List<PulseAiModuleKnowledge> modules, string number)
    {
        var module = PulseAiSystemKnowledgeCatalog.ByNumber(number);
        if (module is not null && modules.All(item => item.ModuleNumber != number)) modules.Add(module);
    }

    private static IReadOnlyList<string> CurrentCapabilities(
        IReadOnlyList<PulseAiModuleKnowledge> modules,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = modules.Select(module => $"Module {module.ModuleNumber} {module.DisplayName}: {module.Purpose}").ToList();
        foreach (var group in apis.GroupBy(api => api.ModuleCode).OrderBy(group => group.Key))
            rows.Add($"Module {group.Key} currently registers {group.Count():N0} relevant API method/route combinations; {group.Count(api => api.CurrentStatus == "healthy"):N0} latest healthy, {group.Count(api => api.CurrentStatus == "failed"):N0} failed, {group.Count(api => api.CurrentStatus == "rejected"):N0} rejected, and {group.Count(api => api.CurrentStatus == "not_observed"):N0} not observed.");
        return rows;
    }

    private static IReadOnlyList<string> CapabilityGaps(
        string question,
        IReadOnlyList<PulseAiModuleKnowledge> modules,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var lower = question.ToLowerInvariant();
        var rows = new List<string>
        {
            "Translate the requested outcome into explicit user stories, authorized personas, record scope, expected inputs, output contract, and measurable success criteria.",
            "Identify which current module remains the source of truth for every data element and calculation; do not create duplicate AI-owned business records.",
            "Add a governed orchestration layer only where existing APIs do not already provide the required read or write contract.",
            "Define failure, partial-data, stale-data, unauthorized, unsupported, and rollback behavior before enabling production use."
        };
        if (apis.Count == 0) rows.Add("No current live API evidence was associated with the selected modules; technical discovery must confirm whether APIs are absent, unavailable to the current environment, or simply unobserved.");
        if (ContainsAny(lower, "real time", "realtime", "live", "stream")) rows.Add("The current request requires event or streaming semantics, durable subscriptions, replay, ordering, dead-letter handling, and idempotency beyond ordinary request/response APIs.");
        if (ContainsAny(lower, "external", "integration", "connect", "webhook")) rows.Add("An approved connector contract, authentication method, rate-limit strategy, data mapping, retry, replay, and external-system ownership model are required.");
        if (ContainsAny(lower, "ai", "llm", "model", "search", "document")) rows.Add("The enhancement requires private evidence selection, prompt/tool contracts, citation and confidence rules, evaluation data, model readiness, and a no-leakage boundary before AI output can be trusted.");
        if (ContainsAny(lower, "write", "create", "update", "approve", "delete", "deploy", "restart", "remediate")) rows.Add("The requested state change requires an owning-module mutation API, exact authorization, validation, idempotency, reason/confirmation, immutable audit evidence, verification, and rollback.");
        if (ContainsAny(lower, "report", "financial", "revenue", "cost", "margin", "invoice", "billing")) rows.Add("Metric definitions, currency, period, included/excluded records, unknown-value behavior, source freshness, and deterministic calculation authority must be formalized.");
        return rows;
    }

    private static IReadOnlyList<string> ProposedArchitecture(string question, IReadOnlyList<PulseAiModuleKnowledge> modules)
    {
        var lower = question.ToLowerInvariant();
        var rows = new List<string>
        {
            "Experience layer: add the capability to the owning Pulse module and expose it through Pulse AI only as an authorized conversational or assisted workflow.",
            "Authorization layer: resolve actual/effective identity, module/action permission, project/customer/team/record scope, View-As restrictions, and data classification before retrieval or action.",
            "Tool layer: reuse or add versioned, allowlisted owning-module APIs. Pulse AI selects tools but never receives unrestricted database credentials.",
            "Orchestration layer: classify intent, validate required inputs, execute the minimum necessary reads/actions, preserve source evidence, and compose a detailed answer or reviewable draft.",
            "Evidence layer: record correlation ID, source modules, record/version identifiers, calculations, confidence, warnings, approvals, and user acceptance/correction without storing secrets or unnecessary raw content.",
            "Reliability layer: define health checks, timeouts, retries, circuit breakers, idempotency, partial-result behavior, monitoring, alerting, and tested rollback."
        };
        if (ContainsAny(lower, "ai", "llm", "model", "search", "document", "assistant"))
            rows.Insert(4, "Private AI layer: use authorized private RAG and governed live tools first; apply structured output, citations, confidence, private verification, and optional sanitized external reasoning through Module 064 only when approved.");
        if (ContainsAny(lower, "event", "webhook", "real time", "realtime", "automation"))
            rows.Add("Event layer: use Module 075 for governed event intake, durable delivery, replay, idempotency, dead-letter evidence, connector health, and bounded automation.");
        rows.Add($"Ownership: primary implementation remains with {string.Join(", ", modules.Take(8).Select(module => $"Module {module.ModuleNumber}"))}; Module 011 provides intelligence and explanation rather than replacing those modules.");
        return rows;
    }

    private static IReadOnlyList<string> DataAndMigrations(string question, IReadOnlyList<PulseAiModuleKnowledge> modules)
    {
        var lower = question.ToLowerInvariant();
        var rows = new List<string>
        {
            "Inventory current tables, APIs, ownership, retention, classification, volume, and authoritative identifiers before defining a new schema.",
            "Prefer additive, idempotent migrations with explicit constraints, indexes, audit fields, source/version evidence, rollback, and apply/rollback/reapply tests.",
            "Keep large documents, model artifacts, and binary evidence in approved private object storage; store metadata, references, checksums, classification, and lifecycle evidence in PostgreSQL.",
            "Preserve unknown and unavailable values rather than coercing them to zero, false, or success."
        };
        if (ContainsAny(lower, "history", "audit", "workflow", "approval", "status")) rows.Add("Add append-only lifecycle or event history so current state can be reconstructed and reviewed independently.");
        if (ContainsAny(lower, "search", "document", "ai")) rows.Add("Add versioned extraction/chunk/index metadata only if existing migration 052/053 structures cannot represent the new source type or feature evidence.");
        if (ContainsAny(lower, "financial", "cost", "revenue", "margin", "invoice")) rows.Add("Do not persist model-calculated financial truth. Persist source transactions and deterministic calculation evidence owned by reporting/billing modules.");
        return rows;
    }

    private static IReadOnlyList<string> ApiAndIntegrations(
        string question,
        IReadOnlyList<PulseAiModuleKnowledge> modules,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = new List<string>
        {
            "Reuse current read endpoints where their authorization and response contracts satisfy the enhancement; do not copy their queries into Module 011.",
            "Add new APIs under the owning module with explicit versioning, request limits, structured errors, correlation IDs, idempotency where needed, and no secret/raw-log response fields.",
            "Register every new endpoint in the running application so Module 013 and Pulse AI can discover its method, route, module, dependencies, latest status, and safe-retest eligibility.",
            "Provide a readiness endpoint for every new external connector or private service and distinguish configured, ready, degraded, failed, locked, and not observed."
        };
        if (apis.Count > 0) rows.Add($"Current relevant API inventory contains {apis.Count:N0} method/route combinations. Extend these contracts before adding parallel endpoints: {string.Join(", ", apis.Take(12).Select(api => $"{api.Method} {api.Path}"))}{(apis.Count > 12 ? ", …" : string.Empty)}.");
        if (ContainsAny(question.ToLowerInvariant(), "external", "integration", "webhook", "connector")) rows.Add("Use Module 075 and the owning integration module for authentication, allowlists, retries, rate limits, replay, dead letters, health, mapping, and sanitized audit evidence.");
        return rows;
    }

    private static IReadOnlyList<string> Permissions(string question, IReadOnlyList<PulseAiModuleKnowledge> modules) =>
    [
        "Define separate View, Create/Execute, Approve, Administer, Export, and Audit capabilities where the workflow warrants separation of duties.",
        "Publish permissions through Modules 012 and 037. Super Administrator receives Full Control; every other role receives only explicit capabilities.",
        "No Access hides the module/action and denies direct API access. View permits only authorized reading. View-As remains read-only and never transfers mutation authority.",
        "Record-level scope remains owned by the source module—for example project assignment, customer ownership, team, department, commercial scope, or security incident scope.",
        $"Review role effects across affected modules: {string.Join(", ", modules.Select(module => module.ModuleNumber))}."
    ];

    private static IReadOnlyList<string> PrivacyAndSecurity(string question, IReadOnlyList<PulseAiModuleKnowledge> modules)
    {
        var rows = new List<string>
        {
            "Classify every input, output, cache, event, export, and audit field; retrieve and return only the minimum necessary authorized data.",
            "Prohibit secrets, tokens, passwords, connection strings, raw provider payloads, unrestricted logs, and unbounded document text in browser responses and AI prompts.",
            "Apply same-origin controls, input limits, output encoding, parameterized SQL, path normalization, file-signature checks, private-endpoint policy, and outbound allowlists as applicable.",
            "Use managed identity or approved write-only secret references; never make credentials visible to Pulse AI.",
            "Run prompt-injection, information-leakage, unauthorized-record, privilege-escalation, View-As, and audit-integrity tests for any AI or retrieval feature."
        };
        if (ContainsAny(question.ToLowerInvariant(), "customer", "financial", "contract", "sow", "gsd", "employee", "document"))
            rows.Add("Keep customer, contract, SOW/GSD, financial, architecture, and employee content inside the approved private Pulse boundary by default. External LLMs may receive only a separately approved sanitized generic capsule.");
        return rows;
    }

    private static IReadOnlyList<string> ObservabilityAndAudit(IReadOnlyList<PulseAiModuleKnowledge> modules) =>
    [
        "Emit correlation IDs and sanitized operational events for every request, tool call, dependency call, rejection, failure, approval, and state transition.",
        "Expose API registration and safe health evidence through Module 013; searchable evidence through Module 016; SLOs and alerts through Module 078; persistent diagnostic findings and controlled remediation through Module 998.",
        "Measure availability, latency, error rate, rejection rate, throughput, queue depth, retry rate, stale data, source coverage, user acceptance, and rollback success.",
        "Do not log request bodies, raw documents, unrestricted prompts, full exception text, provider payloads, or secrets.",
        $"Tag telemetry with affected modules: {string.Join(", ", modules.Select(module => module.ModuleNumber))}."
    ];

    private static IReadOnlyList<string> TestingStrategy(string question, IReadOnlyList<PulseAiModuleKnowledge> modules)
    {
        var rows = new List<string>
        {
            "Unit-test intent, validation, calculations, authorization predicates, state transitions, sanitization, and response schemas.",
            "Run migration apply/rollback/reapply tests and verify constraints, indexes, immutability, role grants, and operational row preservation.",
            "Run API Release build, full production frontend build, container build, existing module validators, and cross-module regression workflows.",
            "Test every role and View-As path, direct API access, record-level scope, disabled-module behavior, stale/missing dependencies, retries, timeouts, concurrency, and idempotency.",
            "Run Test-environment smoke, failure-injection, observability, rollback, and evidence-capture validation against the exact release SHA."
        };
        if (ContainsAny(question.ToLowerInvariant(), "ai", "search", "document", "model")) rows.Add("Add frozen evaluation sets for answer correctness, citations, unsupported claims, prompt injection, unauthorized retrieval, structured output, latency, and cost before model or prompt promotion.");
        if (ContainsAny(question.ToLowerInvariant(), "financial", "report", "cost", "revenue", "margin")) rows.Add("Reconcile deterministic calculations against known accounting/reporting fixtures and verify period, currency, filters, inclusions, exclusions, and unknown-value behavior.");
        return rows;
    }

    private static IReadOnlyList<string> ReleaseSequence(IReadOnlyList<PulseAiModuleKnowledge> modules) =>
    [
        "1. Approve architecture, ownership, data classification, permissions, acceptance criteria, and non-goals.",
        "2. Implement isolated source contracts and validators without activating infrastructure or state-changing adapters.",
        "3. Add and validate migrations separately; apply them through a checksum-pinned, exact-release, private-network Test job.",
        "4. Configure private/external dependencies and secrets separately with readiness checks, no secret readback, and rollback evidence.",
        "5. Deploy the exact source SHA to Test with new capabilities disabled by default.",
        "6. Enable for authorized test roles, run functional/security/performance/failure/rollback validation, and capture immutable evidence.",
        "7. Use canary or phased production activation with explicit approval, monitoring, rollback target, and post-deployment verification.",
        $"8. Update Module 999 documentation, Module 076 defect guidance, Module 077 release history, Module 078 SLOs, and affected module documentation ({string.Join(", ", modules.Select(module => module.ModuleNumber))})."
    ];

    private static IReadOnlyList<string> AcceptanceCriteria(string question, IReadOnlyList<PulseAiModuleKnowledge> modules)
    {
        var rows = new List<string>
        {
            "Authorized users can complete the intended outcome end to end with clear status, source evidence, error recovery, and navigation.",
            "Unauthorized users and direct API attempts receive no protected data and cannot infer hidden records or capabilities.",
            "Every state change is validated, idempotent where appropriate, audited, attributable, and independently verifiable.",
            "Missing, stale, failed, unauthorized, and unsupported conditions are explicit; the interface never fabricates success or silently substitutes zero/empty values.",
            "The exact release passes API, frontend, container, migration, role, record-scope, security, privacy, observability, performance, and rollback validation.",
            "Core Pulse workflows remain available when the enhancement or an external dependency is disabled or unavailable."
        };
        if (ContainsAny(question.ToLowerInvariant(), "ai", "search", "document", "model")) rows.Add("AI output is source-grounded, cited, confidence-scored, human-reviewable, resistant to prompt injection, and produces zero unauthorized retrieval in the acceptance suite.");
        if (ContainsAny(question.ToLowerInvariant(), "api", "troubleshoot", "diagnostic")) rows.Add("All new APIs appear in the live Module 013 inventory with correct module ownership, auth/permission expectations, dependencies, current release, telemetry, correlation evidence, and safe-retest classification.");
        return rows;
    }

    private static IReadOnlyList<string> Dependencies(
        IReadOnlyList<PulseAiModuleKnowledge> modules,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Pulse authentication and effective-user resolution",
            "Modules 012 and 037 role and permission policy",
            "ProjectPulse PostgreSQL and migration governance",
            "Modules 013, 016, 076, 077, 078, 079, 998 operational and governance controls",
            "Exact-SHA CI, deployment, validation, and rollback evidence"
        };
        foreach (var module in modules) rows.Add($"Module {module.ModuleNumber} — {module.DisplayName}");
        foreach (var dependency in apis.SelectMany(api => api.Dependencies)) rows.Add(dependency);
        return rows.OrderBy(value => value).ToArray();
    }

    private static IReadOnlyList<string> Risks(string question, IReadOnlyList<PulseAiModuleKnowledge> modules)
    {
        var rows = new List<string>
        {
            "Duplicate source of truth if the enhancement stores or calculates data that an owning module already controls.",
            "Privilege expansion if conversational access bypasses module/action/record/field authorization.",
            "Inaccurate decisions if stale, partial, conflicting, or model-generated values are presented as authoritative.",
            "Operational coupling if an AI, connector, or analytics dependency can block core transactional workflows.",
            "Deployment risk if schema, source, configuration, secrets, infrastructure, activation, and production promotion are combined into one unreviewable change.",
            "Maintenance risk if future modules or APIs are not automatically registered in Help, operations inventory, documentation, observability, and regression validation."
        };
        if (ContainsAny(question.ToLowerInvariant(), "external", "integration", "llm", "openai", "claude")) rows.Add("External provider risk includes data leakage, retention, rate limits, cost, availability, model drift, licensing, and region/data-residency constraints.");
        if (ContainsAny(question.ToLowerInvariant(), "automation", "approve", "deploy", "remediate", "delete")) rows.Add("Automation can amplify an incorrect action; require bounded scope, exact authorization, dry run, human approval, verification, and rollback.");
        return rows;
    }

    private static IReadOnlyList<string> EstimatedPhases(string question, IReadOnlyList<PulseAiModuleKnowledge> modules)
    {
        var rows = new List<string>
        {
            "Phase 1 — Discovery and architecture: user stories, current-state evidence, ownership, data contracts, security/privacy, acceptance criteria, and implementation plan.",
            "Phase 2 — Read-only foundation: APIs/tools, UI, evidence, authorization, error states, documentation, and validators with all mutations/infrastructure disabled.",
            "Phase 3 — Durable data and workflow: migration, repository, lifecycle, permissions, audit, retries, idempotency, and local apply/rollback/reapply validation.",
            "Phase 4 — Dependency activation: private services or external connectors, secrets, private networking, readiness, health, limits, and failure controls.",
            "Phase 5 — Test integration: exact-SHA deployment, role/record/security/performance/failure/rollback testing, UAT, and evidence capture.",
            "Phase 6 — Controlled production: approval, canary/phased activation, monitoring, rollback, support handoff, documentation, and post-deployment review."
        };
        if (ContainsAny(question.ToLowerInvariant(), "ai", "model", "llm", "training")) rows.Insert(4, "Phase 5 — AI quality: gold-standard data, frozen evaluations, private model/tool integration, prompt/schema versioning, citations, confidence, human review, canary, and rollback.");
        return rows;
    }

    private static IReadOnlyList<string> DetectConflicts(string question, IReadOnlyList<PulseAiModuleKnowledge> modules)
    {
        var rows = new List<string>();
        if (modules.Any(module => module.ModuleNumber == "011") && modules.Any(module => module.ModuleNumber is "055C" or "055D"))
            rows.Add("Module 011 must not reclaim project creation or project/task ownership from Modules 055D and 055C; it may assist those workflows through governed APIs.");
        if (modules.Any(module => module.ModuleNumber == "011") && modules.Any(module => module.ModuleNumber == "064"))
            rows.Add("Module 011 owns intelligence lifecycle and orchestration; Module 064 remains the provider credential, model, health, routing, usage, and fallback authority.");
        if (modules.Any(module => module.ModuleNumber is "030" or "039" or "042" or "055B" or "060"))
            rows.Add("Financial truth must remain deterministic and owned by reporting, billing, rate, expense, and contract modules; an LLM may explain but not invent or replace values.");
        if (ContainsAny(question.ToLowerInvariant(), "restart api route", "restart endpoint"))
            rows.Add("Individual HTTP routes share one API process and cannot be restarted independently; remediation must target the actual process, service, dependency, or deployment through governed adapters.");
        return rows;
    }

    private static PulseAiFutureEnhancementPlan Blocked(string message)
    {
        var now = DateTimeOffset.UtcNow;
        return new PulseAiFutureEnhancementPlan(
            Guid.Empty,
            "blocked",
            "Future enhancement planning unavailable",
            new PulseAiPrivateDetailedAnswer(
                message,
                "No enhancement plan was created.",
                [], [], [], [], [], [], [], [message], [],
                ["Provide an enhancement question while signed in with Pulse AI Help/Search access."],
                ["#work-task-builder", "#user-guide"],
                [], 0m,
                "No authorized planning evidence was available.",
                now),
            [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], now, false);
    }

    private static string Title(string question)
    {
        var clean = Clean(question, 180).TrimEnd('.', '?', '!', ':', ';');
        return clean.Length == 0 ? "Pulse future enhancement plan" : $"Future enhancement plan — {clean}";
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }
}
