using ProjectTime.Api.Modules;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiSystemOperationsService
{
    private readonly PulseAiPrivateRagRepository _accessRepository;
    private readonly PulseAiSystemOperationsRepository _repository;
    private readonly PulseAiSystemOperationsIntentClassifier _classifier;
    private readonly ILogger<PulseAiSystemOperationsService> _logger;

    public PulseAiSystemOperationsService(
        PulseAiPrivateRagRepository accessRepository,
        PulseAiSystemOperationsRepository repository,
        PulseAiSystemOperationsIntentClassifier classifier,
        ILogger<PulseAiSystemOperationsService> logger)
    {
        _accessRepository = accessRepository;
        _repository = repository;
        _classifier = classifier;
        _logger = logger;
    }

    public bool IsSystemOperationsQuestion(string? question) =>
        _classifier.IsSystemOperationsQuestion(question);

    public async Task<PulseAiPrivateRagAccess> LoadAccessAsync(
        Guid actualUserId,
        CancellationToken cancellationToken = default) =>
        await _accessRepository.LoadAccessAsync(actualUserId, cancellationToken);

    public bool CanRead(PulseAiPrivateRagAccess access) =>
        access.IsActive
        && (access.IsSuperAdministrator
            || access.RoleCodes.Overlaps(PulseAiSystemOperationsPolicy.ReadRoles)
            || access.PermissionCodes.Contains(PulseAiSystemOperationsPolicy.AskPermission)
            || access.PermissionCodes.Contains(PulseAiSystemOperationsPolicy.ViewPermission));

    public bool CanViewHistory(PulseAiPrivateRagAccess access) =>
        CanRead(access)
        && (access.IsSuperAdministrator
            || access.RoleCodes.Overlaps(PulseAiSystemOperationsPolicy.AdministratorRoles)
            || access.PermissionCodes.Contains(PulseAiSystemOperationsPolicy.HistoryPermission));

    public bool CanRetest(PulseAiPrivateRagAccess access) =>
        access.IsActive
        && (access.IsSuperAdministrator
            || access.RoleCodes.Overlaps(PulseAiSystemOperationsPolicy.AdministratorRoles)
            || access.PermissionCodes.Contains(PulseAiSystemOperationsPolicy.RetestPermission));

    public async Task<object> GetReadinessAsync(
        Guid actualUserId,
        CancellationToken cancellationToken = default)
    {
        var access = await LoadAccessAsync(actualUserId, cancellationToken);
        var schemaReady = await _repository.IsSchemaReadyAsync(cancellationToken);
        return new
        {
            status = CanRead(access) ? "system_operations_copilot_ready" : "system_operations_access_required",
            contractVersion = PulseAiSystemOperationsPolicy.ContractVersion,
            schemaReady,
            canAsk = CanRead(access),
            canViewHistory = CanViewHistory(access),
            canRetest = CanRetest(access),
            supportedIntents = PulseAiSystemOperationsPolicy.SupportedIntents,
            capabilities = new[]
            {
                "Discover every API route registered in the running Pulse API process.",
                "Map API methods, modules, authentication, permission expectations, dependencies, release, and safe-retest eligibility.",
                "Analyze live HTTP status, latency, failure rate, correlation evidence, sanitized browser diagnostics, and persistent Module 998 findings.",
                "Explain platform, database, storage, integration, worker, release, and runtime health without exposing secrets or raw logs.",
                "Prepare a prioritized troubleshooting sequence and route the operator to Modules 013, 016, 076, 077, 078, and 998.",
                "Retest only explicitly supported same-origin GET APIs and never read their response bodies."
            },
            dataBoundary = new
            {
                requestBodiesRead = false,
                queryStringsRead = false,
                rawLogsRead = false,
                rawExceptionMessagesReturned = false,
                responseBodiesReadByRetest = false,
                secretsReturned = false,
                productionChangingActionsEnabled = false
            },
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<PulseAiSystemOperationsAnswer> AskAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiSystemOperationsQuestionRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var question = Clean(request.Question, 6_000);
        var correlationId = $"pulse-ops-{Guid.NewGuid():N}";
        if (question.Length == 0)
        {
            return Blocked(
                "question_required",
                "Ask a question about APIs, system health, failures, dependencies, correlation evidence, or troubleshooting.",
                correlationId);
        }

        var access = await LoadAccessAsync(actualUserId, cancellationToken);
        if (!CanRead(access))
        {
            return Blocked(
                "forbidden",
                "System API inventory and troubleshooting evidence are restricted to authorized operations, security, and administrative roles.",
                correlationId);
        }

        var classification = _classifier.Classify(question);
        var investigationId = await _repository.CreateInvestigationAsync(
            actualUserId,
            effectiveUserId,
            question,
            classification,
            correlationId,
            cancellationToken);

        try
        {
            var query = new PulseAiSystemOperationsQuery(
                Question: question,
                Classification: classification,
                MaximumResults: Math.Clamp(request.MaximumResults, 1, 500),
                IncludeNotObserved: request.IncludeNotObserved,
                IncludeRecentEvidence: request.IncludeRecentEvidence);
            var snapshot = await PlatformOperationsModule.BuildPulseAiSystemOperationsSnapshotAsync(
                context,
                query,
                cancellationToken);
            var answer = BuildAnswer(
                investigationId,
                classification,
                snapshot,
                correlationId,
                investigationId != Guid.Empty);
            await _repository.CompleteInvestigationAsync(answer, snapshot, cancellationToken);
            return answer;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Pulse AI system operations answer failed without logging a raw question or secret. Intent={Intent} CorrelationId={CorrelationId}",
                classification.Intent,
                correlationId);
            return Failed(investigationId, classification.Intent, correlationId, Diagnostic(exception));
        }
    }

    public async Task<object> ListApisAsync(
        Guid actualUserId,
        string? search,
        string? module,
        string? status,
        int limit,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var access = await LoadAccessAsync(actualUserId, cancellationToken);
        if (!CanRead(access))
        {
            return new
            {
                status = "forbidden",
                requiredPermission = PulseAiSystemOperationsPolicy.ViewPermission,
                apis = Array.Empty<object>()
            };
        }

        var question = $"List APIs {search} module {module} status {status}";
        var classification = _classifier.Classify(question) with
        {
            ModuleCode = Clean(module, 20).ToUpperInvariant(),
            StatusFilter = NormalizeStatus(status),
            WantsAllApis = true
        };
        var snapshot = await PlatformOperationsModule.BuildPulseAiSystemOperationsSnapshotAsync(
            context,
            new PulseAiSystemOperationsQuery(
                question,
                classification,
                Math.Clamp(limit, 1, 500),
                true,
                true),
            cancellationToken);

        var searchValue = Clean(search, 300);
        var apis = snapshot.MatchingApis
            .Where(item => searchValue.Length == 0 || SearchText(item).Contains(searchValue, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 500))
            .ToArray();
        return new
        {
            status = "live_api_inventory_loaded",
            contractVersion = PulseAiSystemOperationsPolicy.ContractVersion,
            generatedAt = snapshot.DataAsOf,
            releaseSha = snapshot.Runtime.ReleaseSha,
            summary = new
            {
                total = snapshot.TotalApiCount,
                returned = apis.Length,
                healthy = snapshot.HealthyApiCount,
                failed = snapshot.FailedApiCount,
                rejected = snapshot.RejectedApiCount,
                notObserved = snapshot.NotObservedApiCount,
                safeRetestSupported = snapshot.SafeRetestApiCount,
                slow = snapshot.SlowApiCount
            },
            filters = new { search = searchValue, module = classification.ModuleCode, status = classification.StatusFilter, limit },
            apis,
            security = new
            {
                responseBodiesRead = false,
                requestBodiesReturned = false,
                queryStringsReturned = false,
                secretValuesReturned = false
            }
        };
    }

    public async Task<IReadOnlyList<PulseAiSystemOperationsHistoryItem>> ListHistoryAsync(
        Guid actualUserId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var access = await LoadAccessAsync(actualUserId, cancellationToken);
        return CanViewHistory(access)
            ? await _repository.ListHistoryAsync(actualUserId, limit, cancellationToken)
            : [];
    }

    public async Task<object?> GetInvestigationAsync(
        Guid investigationId,
        Guid actualUserId,
        CancellationToken cancellationToken = default)
    {
        var access = await LoadAccessAsync(actualUserId, cancellationToken);
        return CanViewHistory(access)
            ? await _repository.GetInvestigationAsync(investigationId, actualUserId, cancellationToken)
            : null;
    }

    private static PulseAiSystemOperationsAnswer BuildAnswer(
        Guid investigationId,
        PulseAiSystemOperationsClassification classification,
        PulseAiSystemOperationsSnapshot snapshot,
        string correlationId,
        bool persisted)
    {
        var apis = snapshot.MatchingApis;
        var citations = Citations(snapshot);
        var rootCauses = RootCauseHypotheses(snapshot, apis);
        var troubleshooting = TroubleshootingSequence(snapshot, apis, classification);
        var safeRetest = apis
            .Where(item => item.RetestCapability == "supported")
            .Take(30)
            .Select(item => $"{item.Method} {item.Path} — API ID {item.ApiId}")
            .ToArray();
        var status = snapshot.HasLiveEvidence
            ? snapshot.DiagnosticCode.Length == 0 ? "completed" : "partial"
            : "insufficient_evidence";

        var detailed = new PulseAiPrivateDetailedAnswer(
            DirectConclusion: DirectConclusion(classification, snapshot, apis),
            ExecutiveSummary: ExecutiveSummary(classification, snapshot, apis),
            ScopeAndFilters: ScopeAndFilters(classification, snapshot),
            DetailedAnalysis: DetailedAnalysis(classification, snapshot, apis),
            SourceEvidence: SourceEvidence(snapshot, apis),
            Calculations: Calculations(snapshot, apis),
            KnownUnknownAndStaleValues: KnownUnknowns(snapshot, apis),
            Assumptions: Assumptions(classification),
            Conflicts: Conflicts(snapshot, apis),
            Limitations: Limitations(snapshot),
            RisksAndImplications: Risks(snapshot, apis),
            RecommendedActions: troubleshooting,
            NavigationTargets: NavigationTargets(classification),
            CitationIds: citations.Select(item => item.CitationId).ToArray(),
            Confidence: Confidence(snapshot, apis),
            ConfidenceExplanation: ConfidenceExplanation(snapshot, apis),
            DataAsOf: snapshot.DataAsOf);

        return new PulseAiSystemOperationsAnswer(
            InvestigationId: investigationId,
            Status: status,
            Intent: classification.Intent,
            Answer: detailed,
            Apis: apis,
            OperationalCitations: citations,
            RootCauseHypotheses: rootCauses,
            TroubleshootingSequence: troubleshooting,
            SafeRetestCandidates: safeRetest,
            TotalApiCount: snapshot.TotalApiCount,
            MatchingApiCount: apis.Count,
            ReleaseSha: snapshot.Runtime.ReleaseSha,
            DataAsOf: snapshot.DataAsOf,
            CorrelationId: correlationId,
            DiagnosticCode: snapshot.DiagnosticCode,
            Persisted: persisted);
    }

    private static string DirectConclusion(
        PulseAiSystemOperationsClassification classification,
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        if (!snapshot.HasLiveEvidence)
            return "Pulse AI could not enumerate the running API process, so it cannot make a trustworthy API-health claim.";

        return classification.Intent switch
        {
            "api_inventory" => $"The running Pulse API currently exposes {snapshot.TotalApiCount:N0} registered method-and-route combinations. {snapshot.HealthyApiCount:N0} have a latest successful observation, {snapshot.FailedApiCount:N0} have a latest server-side failure, {snapshot.RejectedApiCount:N0} have a latest controlled rejection, and {snapshot.NotObservedApiCount:N0} have not yet produced runtime evidence in this process.",
            "correlation_trace" => snapshot.RecentEvents.Count == 0
                ? $"No sanitized runtime or browser evidence was found for correlation/reference ID {classification.CorrelationId}. That does not prove the request did not occur in another replica or before the current process started."
                : $"Pulse AI found {snapshot.RecentEvents.Count:N0} sanitized event(s) associated with correlation/reference ID {classification.CorrelationId}. The latest evidence is {snapshot.RecentEvents[0].Status} with HTTP {snapshot.RecentEvents[0].StatusCode} on {snapshot.RecentEvents[0].Path}.",
            "api_detail" or "api_failure_analysis" => apis.Count == 0
                ? "No running API route matched the supplied path, API ID, method, module, or status filters."
                : apis.Count == 1
                    ? $"{apis[0].Method} {apis[0].Path} is registered under Module {apis[0].ModuleCode} ({apis[0].ModuleName}). Its latest observed state is {apis[0].CurrentStatus}, with {FormatLatency(apis[0].ResponseTimeMs)} and error code {Value(apis[0].LastErrorCode)}."
                    : $"{apis.Count:N0} registered APIs match the request. {apis.Count(item => item.CurrentStatus == "failed"):N0} currently show a latest server-side failure and {apis.Count(item => item.CurrentStatus == "rejected"):N0} show a latest controlled rejection.",
            "safe_retest_candidates" => $"{apis.Count(item => item.RetestCapability == "supported"):N0} matching API(s) are eligible for a same-origin, read-only GET retest that records status and latency without reading the response body.",
            "platform_health" => $"Pulse is running release {snapshot.Runtime.ReleaseSha} in {snapshot.Runtime.Environment} on {snapshot.Runtime.ProviderDisplayName}. Database, storage, runtime-resource, integration, worker, API, and persistent diagnostic evidence are summarized below.",
            "dependency_analysis" => $"{apis.Count:N0} registered API(s) depend on {Value(classification.DependencyFilter)}. Their latest states and current dependency evidence are included below.",
            _ => $"Pulse AI completed a live operations analysis across {snapshot.TotalApiCount:N0} registered APIs and returned {apis.Count:N0} API(s) relevant to the question."
        };
    }

    private static string ExecutiveSummary(
        PulseAiSystemOperationsClassification classification,
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis) =>
        $"Intent {classification.Intent}; running release {snapshot.Runtime.ReleaseSha}; {apis.Count:N0} matching APIs; {snapshot.RecentEvents.Count:N0} recent sanitized events; {snapshot.PersistentFindings.Count:N0} active persistent diagnostic findings; {snapshot.Dependencies.Count:N0} directly checked core dependencies; data as of {snapshot.DataAsOf:O}.";

    private static IReadOnlyList<string> ScopeAndFilters(
        PulseAiSystemOperationsClassification classification,
        PulseAiSystemOperationsSnapshot snapshot)
    {
        var rows = new List<string>
        {
            $"Running release: {snapshot.Runtime.ReleaseSha}.",
            $"Runtime environment: {snapshot.Runtime.Environment}; provider: {snapshot.Runtime.ProviderDisplayName}; region: {snapshot.Runtime.Region}.",
            $"Intent: {classification.Intent}; classifier confidence: {classification.Confidence:P0}.",
            $"All registered APIs in current process: {snapshot.TotalApiCount:N0}."
        };
        Add(rows, classification.ApiMethod, value => $"HTTP method filter: {value}.");
        Add(rows, classification.ApiPath, value => $"API path filter: {value}.");
        Add(rows, classification.ApiId, value => $"API ID filter: {value}.");
        Add(rows, classification.ModuleCode, value => $"Module filter: {value}.");
        Add(rows, classification.StatusFilter, value => $"Latest-status filter: {value}.");
        Add(rows, classification.DependencyFilter, value => $"Dependency filter: {value}.");
        Add(rows, classification.CorrelationId, value => $"Correlation/reference filter: {value}.");
        rows.Add("API status reflects the latest observation held by the current API process; it is not a substitute for a long-term telemetry platform.");
        return rows;
    }

    private static IReadOnlyList<string> DetailedAnalysis(
        PulseAiSystemOperationsClassification classification,
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = new List<string>
        {
            $"API inventory: {snapshot.TotalApiCount:N0} registered method-and-route combinations; {snapshot.HealthyApiCount:N0} healthy; {snapshot.FailedApiCount:N0} failed; {snapshot.RejectedApiCount:N0} rejected; {snapshot.NotObservedApiCount:N0} not observed; {snapshot.SafeRetestApiCount:N0} support safe retest; {snapshot.SlowApiCount:N0} meet the configured slow-API threshold.",
            $"Runtime: provider {snapshot.Runtime.ProviderDisplayName}; adapter {snapshot.Runtime.Adapter}; workload {snapshot.Runtime.WorkloadKind}; instance {snapshot.Runtime.Instance}; deployment {snapshot.Runtime.Deployment}; uptime {FormatDuration(snapshot.Runtime.UptimeSeconds)}; CPU {snapshot.Runtime.CpuPercent:N1}%; process memory {FormatBytes(snapshot.Runtime.ProcessWorkingSetBytes)}.",
            $"Operational evidence: {snapshot.RecentEvents.Count:N0} sanitized runtime/client events and {snapshot.PersistentFindings.Count:N0} active Module 998 findings matched the request."
        };

        foreach (var dependency in snapshot.Dependencies)
            rows.Add($"Dependency {dependency.Name}: {dependency.Status}; latency {FormatLatency(dependency.LatencyMs)}; code {Value(dependency.ErrorCode)}; {dependency.Message}");
        foreach (var api in apis.Take(40))
        {
            rows.Add($"{api.Method} {api.Path} — Module {api.ModuleCode} {api.ModuleName}; status {api.CurrentStatus}; latency {FormatLatency(api.ResponseTimeMs)}; failures {api.FailureCount:N0}/{api.RequestCount:N0} observed requests; auth {api.AuthenticationRequirement}; permission {api.PermissionRequirement}; dependencies {string.Join(", ", api.Dependencies)}; safe retest {api.RetestCapability}.");
        }
        foreach (var finding in snapshot.PersistentFindings.Take(25))
            rows.Add($"Persistent diagnostic finding [{finding.Severity}/{finding.Status}] {finding.CheckCode}: {finding.Summary} Target {finding.TargetKind} {finding.TargetReference}; observed {finding.ObservedAt:O}.");
        foreach (var integration in snapshot.Integrations.Take(25))
            rows.Add($"Integration {integration.Name}: {integration.Status}; owner {integration.Owner}; capabilities {string.Join(", ", integration.Capabilities)}.");
        foreach (var worker in snapshot.Workers.Take(25))
            rows.Add($"Worker {worker.Name}: {worker.Status}; source {worker.Source}; {worker.RestartMessage}");
        if (classification.Intent == "correlation_trace")
        {
            foreach (var evidence in snapshot.RecentEvents.Take(50))
                rows.Add($"Correlation event {evidence.ObservedAt:O}: {evidence.Status} HTTP {evidence.StatusCode}; {evidence.Method} {evidence.Path}; code {Value(evidence.ErrorCode)}; source {evidence.Source}; release {evidence.ReleaseSha}.");
        }
        return rows;
    }

    private static IReadOnlyList<string> SourceEvidence(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = new List<string>
        {
            "Module 013 EndpointDataSource inventory from the running ASP.NET Core process.",
            "Module 013 sanitized API telemetry and safe-retest observations from the current process.",
            "Sanitized browser API-error evidence recorded through /api/client-diagnostics when available.",
            "Module 998 persisted diagnostic sessions and active findings when the operational schema is available.",
            "Current provider-neutral database, storage, integration, hosted-worker, release, process, CPU, and memory snapshot."
        };
        rows.AddRange(apis.Take(25).Select(api => $"API evidence: {api.Method} {api.Path}; Module {api.ModuleCode}; latest state {api.CurrentStatus}; release {api.CurrentRelease}."));
        return rows;
    }

    private static IReadOnlyList<string> Calculations(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var totalObservedRequests = apis.Sum(item => item.RequestCount);
        var totalFailures = apis.Sum(item => item.FailureCount);
        var observedFailureRate = totalObservedRequests == 0
            ? 0m
            : Math.Round((decimal)totalFailures / totalObservedRequests, 4);
        return
        [
            $"Status distribution = healthy {snapshot.HealthyApiCount:N0} + failed {snapshot.FailedApiCount:N0} + rejected {snapshot.RejectedApiCount:N0} + not observed {snapshot.NotObservedApiCount:N0} = {snapshot.TotalApiCount:N0} registered APIs.",
            $"Matching observed failure rate = {totalFailures:N0} failures / {totalObservedRequests:N0} observed requests = {observedFailureRate:P2}. This rate is process-local and begins again when the API process restarts.",
            $"Safe-retest coverage = {snapshot.SafeRetestApiCount:N0} / {snapshot.TotalApiCount:N0} registered APIs = {(snapshot.TotalApiCount == 0 ? 0m : (decimal)snapshot.SafeRetestApiCount / snapshot.TotalApiCount):P2}."
        ];
    }

    private static IReadOnlyList<string> KnownUnknowns(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = new List<string>
        {
            $"Known: the running process registered {snapshot.TotalApiCount:N0} APIs for release {snapshot.Runtime.ReleaseSha}.",
            $"Known: {snapshot.RecentEvents.Count:N0} matching sanitized events and {snapshot.PersistentFindings.Count:N0} persistent findings were available at {snapshot.DataAsOf:O}.",
            "Unknown: a not-observed API may be healthy but unused; Pulse AI does not treat absence of an observation as proof of failure or success.",
            "Unknown: in-memory telemetry from a prior replica or a prior process restart is not available unless it was persisted as client or Module 998 diagnostic evidence.",
            "Unknown: downstream providers that do not expose a live probe remain configured/not-configured rather than falsely marked healthy."
        };
        if (snapshot.DiagnosticCode.Length > 0)
            rows.Add($"Partial source condition: {snapshot.DiagnosticCode}. API inventory may still be current while database-backed evidence is incomplete.");
        if (apis.Any(item => item.IntroducedRelease == "not_recorded"))
            rows.Add("Some endpoints do not yet have an indexed introduced-release value; their current release remains known.");
        return rows;
    }

    private static IReadOnlyList<string> Assumptions(PulseAiSystemOperationsClassification classification) =>
    [
        "The question refers to the currently running Pulse environment reached by this authenticated session.",
        "A latest HTTP 4xx observation is a controlled rejection until evidence demonstrates an application defect.",
        "A safe retest is diagnostic evidence only; it is not a business transaction or remediation.",
        classification.ApiPath.Length == 0 ? "No exact API path was supplied, so the answer applies semantic and module/status filters." : "The supplied path was interpreted as an API route filter."
    ];

    private static IReadOnlyList<string> Conflicts(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = new List<string>();
        foreach (var api in apis)
        {
            if (api.CurrentStatus == "healthy" && api.FailureCount > 0)
                rows.Add($"{api.Method} {api.Path} is currently healthy but has {api.FailureCount:N0} earlier failure/rejection observation(s) in the current process.");
            if (api.CurrentStatus == "not_observed" && snapshot.RecentEvents.Any(item => item.Path.Equals(api.Path, StringComparison.OrdinalIgnoreCase)))
                rows.Add($"{api.Method} {api.Path} is registered as not observed for its exact method/route key, while related event evidence exists and should be reviewed by correlation ID.");
        }
        foreach (var dependency in snapshot.Dependencies.Where(item => item.Status == "failed"))
            rows.Add($"Dependency {dependency.Name} is failed while some dependent APIs may still show an older healthy observation.");
        return rows;
    }

    private static IReadOnlyList<string> Limitations(PulseAiSystemOperationsSnapshot snapshot)
    {
        var rows = new List<string>
        {
            "Pulse AI receives only sanitized operational metadata. It does not receive request bodies, query-string values, raw logs, full exception messages, connection strings, tokens, passwords, provider payloads, or response bodies.",
            "Current API observations are held in the running process. Long-term trend and cross-replica analysis require durable observability integration through Module 078.",
            "Safe retest supports only same-origin GET routes without route parameters, callbacks, downloads, exports, streams, authentication transitions, or recursion risk.",
            "Pulse AI does not restart services, alter infrastructure, modify database records, change configuration, deploy code, or perform production remediation through this package."
        };
        if (snapshot.DiagnosticCode.Length > 0)
            rows.Add($"One or more platform evidence sources were partial: {snapshot.DiagnosticCode}.");
        return rows;
    }

    private static IReadOnlyList<string> Risks(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = new List<string>();
        if (apis.Any(item => item.CurrentStatus == "failed"))
            rows.Add("One or more matching endpoints have a latest HTTP 5xx or transport failure. User workflows that depend on those endpoints may be unavailable or incomplete.");
        if (apis.Any(item => item.CurrentStatus == "rejected"))
            rows.Add("One or more matching endpoints have a latest controlled rejection. Confirm session, role, action permission, record scope, request shape, availability state, and rate limits before treating the result as a service outage.");
        if (snapshot.Dependencies.Any(item => item.Status == "failed"))
            rows.Add("A failed shared dependency can create correlated failures across multiple modules; prioritize the dependency before troubleshooting each route independently.");
        if (snapshot.NotObservedApiCount > snapshot.TotalApiCount / 2)
            rows.Add("Most registered endpoints have not been observed in the current process, so broad health conclusions require targeted safe probes or user-flow evidence.");
        if (rows.Count == 0)
            rows.Add("No current failure signal matched the request, but unobserved routes, other replicas, client-only failures, and external dependencies remain possible blind spots.");
        return rows;
    }

    private static IReadOnlyList<string> RootCauseHypotheses(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        var rows = new List<string>();
        foreach (var dependency in snapshot.Dependencies.Where(item => item.Status == "failed"))
            rows.Add($"Shared dependency failure: {dependency.Name} reports {Value(dependency.ErrorCode)}. APIs listing this dependency may fail together.");
        foreach (var api in apis.Take(40))
        {
            var code = api.LastErrorCode.ToUpperInvariant();
            if (code.Contains("HTTP_401")) rows.Add($"{api.Method} {api.Path}: the session or authentication evidence was missing, expired, or invalid.");
            else if (code.Contains("HTTP_403")) rows.Add($"{api.Method} {api.Path}: the current effective identity, action permission, module state, View-As boundary, or record scope rejected access.");
            else if (code.Contains("HTTP_404")) rows.Add($"{api.Method} {api.Path}: the route may be registered but the requested record or parameterized resource was not found; verify the client route and identifiers.");
            else if (code.Contains("HTTP_409")) rows.Add($"{api.Method} {api.Path}: current state conflicts with the requested operation; review revision, duplicate, lock, or lifecycle evidence.");
            else if (code.Contains("HTTP_423")) rows.Add($"{api.Method} {api.Path}: the operation is intentionally locked pending an adapter, approval, configuration, or migration.");
            else if (code.Contains("HTTP_429")) rows.Add($"{api.Method} {api.Path}: a rate-limit or concurrency control rejected the request.");
            else if (code.Contains("HTTP_5") || api.CurrentStatus == "failed") rows.Add($"{api.Method} {api.Path}: the API or a required dependency failed server-side; correlate {Value(api.CorrelationId)} with Module 016/998 evidence.");
            else if (code.Contains("TIMEOUT") || code.Contains("TASKCANCELED")) rows.Add($"{api.Method} {api.Path}: request timeout suggests slow processing, dependency latency, network delay, or a cancelled client request.");
        }
        foreach (var finding in snapshot.PersistentFindings.Take(25))
            rows.Add($"Module 998 finding {finding.CheckCode} ({finding.Severity}): {finding.Summary}");
        return rows.Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToArray();
    }

    private static IReadOnlyList<string> TroubleshootingSequence(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis,
        PulseAiSystemOperationsClassification classification)
    {
        var rows = new List<string>
        {
            $"1. Confirm the active environment and release: {snapshot.Runtime.Environment}, release {snapshot.Runtime.ReleaseSha}, deployment {snapshot.Runtime.Deployment}, process started {snapshot.Runtime.ProcessStartedAt:O}.",
            "2. Verify the endpoint is registered with the expected HTTP method, route, owning module, authentication requirement, and action permission.",
            "3. Separate controlled rejection from outage: HTTP 401/403/404/409/423/429 usually requires session, permission, record, lifecycle, configuration, or request-shape review before infrastructure remediation.",
            "4. Review database, storage, integration, and hosted-worker status, then correlate any shared dependency failure across all affected routes.",
            "5. Search Module 016 operational evidence and Module 998 persistent diagnostic findings using the exact correlation/reference ID, path, module, release, and observation time.",
            "6. For an eligible read-only GET route, run the exact-confirmation safe retest. It records response status and latency but does not read the response body.",
            "7. Reproduce the complete user workflow while preserving route, effective role, timestamp, correlation ID, expected behavior, observed behavior, and sanitized browser evidence.",
            "8. Open Module 998 for a persisted diagnostic session when the issue requires platform/database/identity/integration checks. Use Module 076 for a reproducible defect and Module 077 for release/deployment evidence or rollback governance."
        };
        if (!string.IsNullOrWhiteSpace(classification.CorrelationId))
            rows.Insert(5, $"6. Correlation focus: use {classification.CorrelationId} as the exact evidence key. Do not substitute a different browser reference or request ID.");
        if (apis.Count == 0)
            rows.Add("9. No API matched the current filters. Remove method/status filters, confirm the exact route, and compare the client request with the running API inventory before assuming the endpoint is missing.");
        return rows;
    }

    private static IReadOnlyList<string> NavigationTargets(PulseAiSystemOperationsClassification classification)
    {
        var rows = new List<string>
        {
            "#service-control",
            "#backup-retention",
            "#observability-slo-health",
            "#system-diagnostics",
            "#defect-tracker",
            "#release-deployment-control",
            "#work-task-builder"
        };
        if (classification.ModuleCode == "997") rows.Insert(0, "#security-operations");
        return rows.Distinct().ToArray();
    }

    private static IReadOnlyList<PulseAiSystemOperationsCitation> Citations(PulseAiSystemOperationsSnapshot snapshot)
    {
        var rows = new List<PulseAiSystemOperationsCitation>();
        var rank = 1;
        foreach (var api in snapshot.MatchingApis.Take(100))
        {
            rows.Add(new PulseAiSystemOperationsCitation(
                rank++, "api_inventory", api.ModuleCode, api.ModuleName, api.ApiId,
                api.Method, api.Path, api.CurrentStatus, null, api.ResponseTimeMs,
                api.LastErrorCode, api.CorrelationId, api.LastCheckedAt, api.CurrentRelease));
        }
        foreach (var item in snapshot.RecentEvents.Take(100))
        {
            rows.Add(new PulseAiSystemOperationsCitation(
                rank++, item.Source, item.ModuleCode, item.ModuleName, string.Empty,
                item.Method, item.Path, item.Status, item.StatusCode, item.ResponseTimeMs,
                item.ErrorCode, item.CorrelationId, item.ObservedAt, item.ReleaseSha));
        }
        foreach (var item in snapshot.PersistentFindings.Take(50))
        {
            rows.Add(new PulseAiSystemOperationsCitation(
                rank++, item.Source, "998", "System Diagnostic & Controlled Remediation Center", string.Empty,
                string.Empty, item.TargetReference, item.Status, null, null,
                item.CheckCode, string.Empty, item.ObservedAt, snapshot.Runtime.ReleaseSha));
        }
        foreach (var item in snapshot.Dependencies.Take(20))
        {
            rows.Add(new PulseAiSystemOperationsCitation(
                rank++, "dependency_check", "013", item.Name, string.Empty,
                "CHECK", item.Key, item.Status, null, item.LatencyMs,
                item.ErrorCode, string.Empty, item.CheckedAt, snapshot.Runtime.ReleaseSha));
        }
        return rows.Take(250).ToArray();
    }

    private static decimal Confidence(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis)
    {
        if (!snapshot.HasLiveEvidence) return 0.15m;
        var confidence = 0.60m;
        if (apis.Count > 0) confidence += 0.10m;
        if (snapshot.RecentEvents.Count > 0) confidence += 0.10m;
        if (snapshot.PersistentFindings.Count > 0) confidence += 0.05m;
        if (snapshot.Dependencies.Count > 0) confidence += 0.05m;
        if (snapshot.DiagnosticCode.Length == 0) confidence += 0.05m;
        return Math.Clamp(confidence, 0m, 0.95m);
    }

    private static string ConfidenceExplanation(
        PulseAiSystemOperationsSnapshot snapshot,
        IReadOnlyList<PulseAiSystemApiRecord> apis) =>
        $"Confidence reflects live endpoint registration ({snapshot.TotalApiCount:N0}), matched APIs ({apis.Count:N0}), recent sanitized events ({snapshot.RecentEvents.Count:N0}), persistent diagnostic findings ({snapshot.PersistentFindings.Count:N0}), core dependency checks ({snapshot.Dependencies.Count:N0}), and source completeness ({(snapshot.DiagnosticCode.Length == 0 ? "complete" : snapshot.DiagnosticCode)}).";

    private static PulseAiSystemOperationsAnswer Blocked(
        string diagnosticCode,
        string message,
        string correlationId)
    {
        var answer = new PulseAiPrivateDetailedAnswer(
            message,
            "Pulse AI did not access restricted operations evidence.",
            [], [], [], [], [], [], [], [message], [],
            ["Open Module 013 or contact an authorized system administrator."],
            ["#service-control", "#user-guide"],
            [], 0m,
            "No live operations evidence was accessed.",
            DateTimeOffset.UtcNow);
        return new PulseAiSystemOperationsAnswer(
            Guid.Empty, "blocked", "system_operations", answer, [], [], [], [], [],
            0, 0, "not_recorded", DateTimeOffset.UtcNow, correlationId,
            diagnosticCode, false);
    }

    private static PulseAiSystemOperationsAnswer Failed(
        Guid investigationId,
        string intent,
        string correlationId,
        string diagnosticCode)
    {
        var answer = new PulseAiPrivateDetailedAnswer(
            "Pulse AI could not complete the live system-operations analysis.",
            "No raw exception, log, request, response, or secret data is returned.",
            [], [], [], [], [], [], [],
            ["The live operations analysis failed before a trustworthy answer was completed."],
            ["The system state may remain unknown until an authorized operator reviews Module 013 or Module 998."],
            ["Use the correlation ID to review sanitized server evidence."],
            ["#service-control", "#system-diagnostics", "#defect-tracker"],
            [], 0.10m,
            "Confidence is low because the evidence pipeline did not complete.",
            DateTimeOffset.UtcNow);
        return new PulseAiSystemOperationsAnswer(
            investigationId, "failed", intent, answer, [], [], [],
            ["Review the diagnostic code and correlation ID."], [],
            0, 0, "not_recorded", DateTimeOffset.UtcNow, correlationId,
            diagnosticCode, investigationId != Guid.Empty);
    }

    private static string SearchText(PulseAiSystemApiRecord item) =>
        $"{item.ApiId} {item.RouteGroup} {item.Method} {item.Path} {item.ModuleCode} {item.ModuleName} {item.Purpose} {item.AuthenticationRequirement} {item.PermissionRequirement} {string.Join(' ', item.Dependencies)} {item.CurrentStatus} {item.LastErrorCode} {item.CorrelationId}";

    private static string NormalizeStatus(string? status)
    {
        var value = Clean(status, 40).ToLowerInvariant();
        return value switch
        {
            "failed" or "rejected" or "healthy" or "not_observed" or "failed_or_rejected" => value,
            "failure" or "errors" or "unhealthy" => "failed_or_rejected",
            _ => string.Empty
        };
    }

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static void Add(List<string> rows, string value, Func<string, string> format)
    {
        if (!string.IsNullOrWhiteSpace(value)) rows.Add(format(value));
    }

    private static string Value(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "not recorded" : value;

    private static string FormatLatency(double? value) =>
        value is null ? "not observed" : $"{value.Value:N2} ms";

    private static string FormatDuration(double seconds)
    {
        if (seconds < 0) return "not reported";
        var span = TimeSpan.FromSeconds(seconds);
        return $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
    }

    private static string FormatBytes(long value)
    {
        if (value < 0) return "not reported";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:N1} {units[unit]}";
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        OperationCanceledException => "operation_cancelled",
        TimeoutException => "operation_timeout",
        _ => "system_operations_failure"
    };
}
