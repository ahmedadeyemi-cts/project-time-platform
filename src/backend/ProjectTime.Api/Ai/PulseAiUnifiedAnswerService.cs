using ProjectTime.Api.Modules;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiUnifiedAnswerService
{
    private readonly PulseAiSystemOperationsService _operations;
    private readonly PulseAiSystemOperationsIntentClassifier _operationsClassifier;
    private readonly PulseAiFutureEnhancementPlanner _futureEnhancements;
    private readonly PulseAiPrivateRagService _privateRag;
    private readonly PulseAiQuestionPlanner _questionPlanner;
    private readonly PulseAiPrivateRagRepository _accessRepository;
    private readonly ILogger<PulseAiUnifiedAnswerService> _logger;

    public PulseAiUnifiedAnswerService(
        PulseAiSystemOperationsService operations,
        PulseAiSystemOperationsIntentClassifier operationsClassifier,
        PulseAiFutureEnhancementPlanner futureEnhancements,
        PulseAiPrivateRagService privateRag,
        PulseAiQuestionPlanner questionPlanner,
        PulseAiPrivateRagRepository accessRepository,
        ILogger<PulseAiUnifiedAnswerService> logger)
    {
        _operations = operations;
        _operationsClassifier = operationsClassifier;
        _futureEnhancements = futureEnhancements;
        _privateRag = privateRag;
        _questionPlanner = questionPlanner;
        _accessRepository = accessRepository;
        _logger = logger;
    }

    public async Task<PulseAiUnifiedAnswerResult> AnswerAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiUnifiedHelpRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var question = Clean(request.Question, 6_000);
        if (question.Length == 0)
        {
            var empty = GeneralFallback(
                question,
                _questionPlanner.PlanHelpSearch("How do I use Pulse?"),
                "A question is required before Pulse AI can select authorized evidence and provide an answer.");
            return new PulseAiUnifiedAnswerResult(
                "product_knowledge",
                "question_required",
                new { answer = empty },
                "No question was supplied.",
                DateTimeOffset.UtcNow);
        }

        if (_futureEnhancements.IsFutureEnhancementQuestion(question))
        {
            var plan = await _futureEnhancements.PlanAsync(
                actualUserId,
                effectiveUserId,
                new PulseAiFutureEnhancementRequest(
                    question,
                    request.DetailLevel,
                    IncludeLiveApiEvidence: true,
                    PersistDraft: true),
                context,
                cancellationToken);
            return new PulseAiUnifiedAnswerResult(
                "future_enhancement",
                plan.Status,
                plan.ToPublicResponse(),
                "The request asks Pulse to add, improve, extend, integrate, automate, or plan a future capability.",
                DateTimeOffset.UtcNow);
        }

        if (_operations.IsSystemOperationsQuestion(question))
        {
            var answer = await _operations.AskAsync(
                actualUserId,
                effectiveUserId,
                new PulseAiSystemOperationsQuestionRequest(
                    question,
                    request.DetailLevel,
                    request.MaximumResults,
                    IncludeNotObserved: true,
                    IncludeRecentEvidence: true),
                context,
                cancellationToken);
            return new PulseAiUnifiedAnswerResult(
                "system_operations",
                answer.Status,
                answer.ToPublicResponse(),
                "The question requires live API registration, health, correlation, dependency, release, or troubleshooting evidence.",
                DateTimeOffset.UtcNow);
        }

        PulseAiPrivateRagAnswer? ragAnswer = null;
        try
        {
            ragAnswer = await _privateRag.AskHelpSearchAsync(
                actualUserId,
                effectiveUserId,
                new PulseAiPrivateHelpSearchRequest(
                    question,
                    request.ProjectCode,
                    request.ProjectName,
                    request.DetailLevel,
                    request.IncludeAuthorizedProjectDocuments,
                    request.IncludeDirectProductKnowledge),
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI unified answer could not use private RAG. Falling back to governed system knowledge without logging the question text. Diagnostic={Diagnostic}",
                exception.GetType().Name);
        }

        if (ragAnswer?.Answer is not null || ragAnswer?.FlowHivePlan is not null)
        {
            return new PulseAiUnifiedAnswerResult(
                ragAnswer.RetrievalMode == "direct_knowledge" ? "product_knowledge" : "private_rag",
                ragAnswer.Status,
                ragAnswer.ToPublicResponse(),
                ragAnswer.RetrievalMode == "direct_knowledge"
                    ? "The question matched approved Pulse product and workflow knowledge."
                    : "The question was answered from current authorized private evidence and the private Pulse AI path.",
                DateTimeOffset.UtcNow);
        }

        var matchedModules = PulseAiSystemKnowledgeCatalog.Match(question, 15);
        if (matchedModules.Count > 0)
        {
            var moduleAnswer = await BuildModuleKnowledgeAnswerAsync(
                actualUserId,
                question,
                matchedModules,
                context,
                cancellationToken);
            return new PulseAiUnifiedAnswerResult(
                "system_knowledge",
                "completed",
                moduleAnswer,
                "The question matched one or more registered Pulse modules and can be answered from the governed system knowledge catalog, with live API evidence when authorized.",
                DateTimeOffset.UtcNow);
        }

        var planFallback = _questionPlanner.PlanHelpSearch(question);
        var detailed = GeneralFallback(
            question,
            planFallback,
            ragAnswer?.DiagnosticCode is { Length: > 0 }
                ? $"Private evidence was not available ({ragAnswer.DiagnosticCode}); Pulse AI is returning a comprehensive governed answer based on system ownership, required tools, and known limitations."
                : "No exact private source matched the question, so Pulse AI is returning a comprehensive governed answer that separates known system behavior from live values that still require authorized tools.");
        return new PulseAiUnifiedAnswerResult(
            "governed_general_answer",
            "completed_with_live_data_limits",
            new
            {
                featureCode = PulseAiSystemOperationsPolicy.UnifiedHelpFeatureCode,
                answer = detailed,
                plan = planFallback,
                warnings = ragAnswer?.Warnings ?? [],
                missingEvidence = ragAnswer?.MissingEvidence ?? [],
                diagnosticCode = ragAnswer?.DiagnosticCode ?? string.Empty,
                stateChanged = false
            },
            "The question did not require privileged operations evidence, did not match a future enhancement request, and did not have sufficient private evidence for exact live values.",
            DateTimeOffset.UtcNow);
    }

    private async Task<object> BuildModuleKnowledgeAnswerAsync(
        Guid actualUserId,
        string question,
        IReadOnlyList<PulseAiModuleKnowledge> modules,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var operationsAccess = await _operations.LoadAccessAsync(actualUserId, cancellationToken);
        var apis = Array.Empty<PulseAiSystemApiRecord>();
        PulseAiSystemOperationsSnapshot? snapshot = null;
        if (_operations.CanRead(operationsAccess))
        {
            var classification = _operationsClassifier.Classify(question) with
            {
                Intent = "api_inventory",
                WantsAllApis = true,
                ModuleCode = string.Empty,
                ApiPath = string.Empty,
                ApiMethod = string.Empty,
                ApiId = string.Empty,
                CorrelationId = string.Empty,
                StatusFilter = string.Empty
            };
            snapshot = await PlatformOperationsModule.BuildPulseAiSystemOperationsSnapshotAsync(
                context,
                new PulseAiSystemOperationsQuery(question, classification, 500, true, false),
                cancellationToken);
            var moduleNumbers = modules.Select(module => module.ModuleNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
            apis = snapshot.AllApis
                .Where(api => moduleNumbers.Contains(api.ModuleCode))
                .OrderBy(api => api.ModuleCode)
                .ThenBy(api => api.Path)
                .ThenBy(api => api.Method)
                .Take(200)
                .ToArray();
        }

        var direct = modules.Count == 1
            ? $"Module {modules[0].ModuleNumber} — {modules[0].DisplayName} is Pulse's {modules[0].Group.ToLowerInvariant()} capability for {LowerFirst(modules[0].Purpose)}"
            : $"The question spans {modules.Count:N0} Pulse modules: {string.Join(", ", modules.Select(module => $"{module.ModuleNumber} {module.DisplayName}"))}. Each module retains its own business authority while Pulse AI provides cross-module explanation and governed orchestration.";
        var detailedRows = new List<string>();
        foreach (var module in modules)
        {
            detailedRows.Add($"Module {module.ModuleNumber} — {module.DisplayName}; route #{module.Route}; group {module.Group}; purpose: {module.Purpose}");
            if (module.RelatedModules.Count > 0)
                detailedRows.Add($"Module {module.ModuleNumber} related modules: {string.Join(", ", module.RelatedModules)}.");
            if (module.CommonEnhancementDirections.Count > 0)
                detailedRows.Add($"Module {module.ModuleNumber} common enhancement directions: {string.Join(", ", module.CommonEnhancementDirections)}.");
        }
        foreach (var api in apis.Take(80))
            detailedRows.Add($"Running API: {api.Method} {api.Path}; Module {api.ModuleCode}; status {api.CurrentStatus}; authentication {api.AuthenticationRequirement}; permission {api.PermissionRequirement}; dependencies {string.Join(", ", api.Dependencies)}.");

        var answer = new PulseAiPrivateDetailedAnswer(
            DirectConclusion: direct,
            ExecutiveSummary: $"Pulse AI matched {modules.Count:N0} registered module(s) and {(snapshot is null ? "did not access privileged live API inventory" : $"found {apis.Length:N0} current API routes in release {snapshot.Runtime.ReleaseSha}")}.",
            ScopeAndFilters:
            [
                $"Question: {question}",
                $"Matched modules: {string.Join(", ", modules.Select(module => module.ModuleNumber))}.",
                snapshot is null ? "Live API inventory: not included for the current permission context." : $"Live API inventory: release {snapshot.Runtime.ReleaseSha}; data as of {snapshot.DataAsOf:O}."
            ],
            DetailedAnalysis: detailedRows,
            SourceEvidence:
            [
                .. modules.Select(module => $"Pulse module catalog: {module.ModuleNumber} {module.DisplayName}, route #{module.Route}."),
                .. apis.Take(50).Select(api => $"Runtime endpoint registration: {api.Method} {api.Path}; current state {api.CurrentStatus}; release {api.CurrentRelease}.")
            ],
            Calculations:
            [
                $"Matched module count: {modules.Count:N0}.",
                $"Authorized live API count for matched modules: {apis.Length:N0}."
            ],
            KnownUnknownAndStaleValues:
            [
                "Known: module number, route, current responsibility, related modules, and common enhancement directions.",
                snapshot is null ? "Unknown in this answer: current API health and release-specific runtime evidence because operations access was not used." : $"Known at {snapshot.DataAsOf:O}: API registration and latest process-local observations for release {snapshot.Runtime.ReleaseSha}.",
                "A not-observed endpoint is not automatically failed; it may not have been called since the current process started."
            ],
            Assumptions:
            [
                "The question refers to the current Pulse platform rather than a retired historical implementation.",
                "Owning modules remain the source of truth; Pulse AI explains and orchestrates but does not silently move business ownership."
            ],
            Conflicts:
            [
                modules.Any(module => module.ModuleNumber == "011")
                    ? "Module 011 is Pulse AI. Its historical Work Task Builder identity is retired; Modules 055D and 055C own project creation and project/task management."
                    : "No explicit ownership conflict was identified from the question."
            ],
            Limitations:
            [
                "This answer does not infer a user's live record access, project status, financial value, or workflow state unless an authorized tool explicitly supplied that evidence.",
                "Future enhancements require a separate architecture, permission, data, testing, migration, and release review."
            ],
            RisksAndImplications:
            [
                "Cross-module changes can duplicate authority or create inconsistent calculations if the owning module is bypassed.",
                "New conversational access must preserve module, action, record, field, and View-As authorization."
            ],
            RecommendedActions:
            [
                "Open the owning module route listed above for the authoritative workflow and records.",
                "Ask Pulse AI an exact API, health, correlation, troubleshooting, or future-enhancement question for deeper live evidence or an implementation plan.",
                "Use Module 999 for the complete user guide, Module 013 for API inventory, Module 076 for defects, and Module 998 for persistent diagnostic sessions."
            ],
            NavigationTargets: modules.Select(module => $"#{module.Route}").Concat(["#user-guide", "#service-control", "#work-task-builder"]).Distinct().Take(25).ToArray(),
            CitationIds: [],
            Confidence: snapshot is null ? 0.82m : 0.92m,
            ConfidenceExplanation: snapshot is null
                ? "Confidence is based on the governed Pulse module catalog without privileged live operations evidence."
                : $"Confidence is based on the governed module catalog and {apis.Length:N0} live API registrations from release {snapshot.Runtime.ReleaseSha}.",
            DataAsOf: snapshot?.DataAsOf ?? DateTimeOffset.UtcNow);

        return new
        {
            featureCode = "pulse_system_knowledge",
            status = "completed",
            answer,
            modules,
            apis,
            operationalCitations = apis.Select((api, index) => new PulseAiSystemOperationsCitation(
                index + 1,
                "api_inventory",
                api.ModuleCode,
                api.ModuleName,
                api.ApiId,
                api.Method,
                api.Path,
                api.CurrentStatus,
                null,
                api.ResponseTimeMs,
                api.LastErrorCode,
                api.CorrelationId,
                api.LastCheckedAt,
                api.CurrentRelease)).ToArray(),
            releaseSha = snapshot?.Runtime.ReleaseSha ?? "not_accessed",
            dataAsOf = answer.DataAsOf,
            stateChanged = false
        };
    }

    private static PulseAiPrivateDetailedAnswer GeneralFallback(
        string question,
        PulseAiQuestionPlan plan,
        string explanation)
    {
        var direct = plan.DirectKnowledgeAnswer;
        if (direct is not null)
        {
            return new PulseAiPrivateDetailedAnswer(
                direct.Summary,
                direct.Title,
                ["Product Help mode", $"Question: {question}"],
                direct.DetailedSteps,
                direct.SourceModules.Select(module => $"Pulse source module: {module}").ToArray(),
                [], [], [], [],
                ["This product answer does not claim live record or environment status unless a governed tool supplied it."],
                direct.ImportantRules,
                direct.DetailedSteps,
                direct.NavigationTargets,
                [], 0.88m,
                "The answer is based on the approved Pulse product-knowledge catalog.",
                plan.GeneratedAt);
        }

        var directConclusion = plan.Domains.Count > 0
            ? $"Pulse AI identified the question as spanning {string.Join(", ", plan.Domains.Select(Title))}. {explanation}"
            : $"Pulse AI did not find an exact product or live-data match. {explanation}";
        return new PulseAiPrivateDetailedAnswer(
            DirectConclusion: directConclusion,
            ExecutiveSummary: "Pulse AI is providing the most complete trustworthy answer available without inventing live records or values.",
            ScopeAndFilters:
            [
                $"Question: {question}",
                $"Detail level: {plan.DetailLevel}",
                $"Owning modules: {string.Join(", ", plan.OwningModules)}"
            ],
            DetailedAnalysis:
            [
                .. plan.RequiredEvidence.Select(value => $"Required evidence — {value}"),
                .. plan.ExecutionSteps.Select(value => $"Execution — {value}"),
                .. plan.AnswerSections.Select(value => $"Answer requirement — {value}")
            ],
            SourceEvidence: plan.RequiredTools.Select(tool => $"Governed tool or knowledge source: {tool}").ToArray(),
            Calculations: plan.DeterministicCalculations,
            KnownUnknownAndStaleValues:
            [
                "Known: the owning modules, required evidence, permission boundary, calculation authority, and correct execution sequence.",
                .. plan.MissingInputs.Select(value => $"Not yet resolved — {value}"),
                "Pulse AI does not convert missing, unavailable, stale, or unauthorized values into zero or success."
            ],
            Assumptions:
            [
                "The question refers to the current effective user's authorized Pulse scope.",
                "Live values require a current owning-module tool, current private source, or deterministic calculation."
            ],
            Conflicts: [],
            Limitations:
            [
                "No exact current record value is claimed unless a governed live source was executed.",
                "Some live tools remain feature-specific and must be added to the unified orchestration layer before exact cross-module execution is automatic."
            ],
            RisksAndImplications: plan.PrivacyControls,
            RecommendedActions:
            [
                "Provide any missing project, customer, module, date range, record, environment, correlation ID, or business filter listed above.",
                "Open the owning Pulse module or ask an exact follow-up question so Pulse AI can select the correct authorized tool.",
                "For a new capability, ask Pulse AI to create a future enhancement plan with architecture, APIs, data, permissions, testing, risks, and rollout."
            ],
            NavigationTargets: ["#work-task-builder", "#user-guide", "#service-control", "#defect-tracker"],
            CitationIds: [],
            Confidence: 0.66m,
            ConfidenceExplanation: "Confidence is limited because the response describes governed system behavior and evidence requirements rather than an exact live-record result.",
            DataAsOf: plan.GeneratedAt);
    }

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string LowerFirst(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : char.ToLowerInvariant(value[0]) + value[1..];

    private static string Title(string value) =>
        string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
