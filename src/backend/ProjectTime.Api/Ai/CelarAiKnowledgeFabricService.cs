using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed record CelarAiKnowledgeEndpointStatus(
    string Component,
    bool Required,
    bool Configured,
    bool PrivateBoundaryVerified,
    bool RuntimeVerified,
    string Status,
    string DiagnosticCode);

public sealed record CelarAiCapabilityConnectionStatus(
    string Feature,
    string Module,
    string EntryPoint,
    IReadOnlyList<string> Route,
    bool CentralRouterConnected,
    bool PrivateContextCompliant,
    bool DirectProviderFree,
    bool PrivateKnowledgeReady,
    string Status,
    DateTimeOffset? LastExercisedAt,
    DateTimeOffset? LastSuccessAt);

public sealed record CelarAiKnowledgeFabricSnapshot(
    string Status,
    bool Ready,
    bool RouteGraphReady,
    bool ContentGraphReady,
    bool PrivateEndpointsReady,
    string SourceCommit,
    string ProductKnowledgeVersion,
    string SystemKnowledgeVersion,
    string PrivateRuntimeVersion,
    int CapabilityNodeCount,
    int ConsumerNodeCount,
    int ProviderTargetNodeCount,
    int SystemToolNodeCount,
    int RelationshipCount,
    long ReadyDocumentCount,
    long ReadySowDocumentCount,
    long PendingSowDocumentCount,
    long ActiveVersionCount,
    long ActiveChunkCount,
    long EmbeddedChunkCount,
    long UnembeddedChunkCount,
    DateTimeOffset? LastIndexedAt,
    IReadOnlyList<string> ContentGraphRelationships,
    IReadOnlyList<CelarAiKnowledgeEndpointStatus> Endpoints,
    IReadOnlyList<CelarAiCapabilityConnectionStatus> Capabilities,
    IReadOnlyList<string> Blockers,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Projects the current Module 064 routes, source-controlled knowledge,
/// permission-safe consumer graph, private document index, and private endpoint
/// evidence into one metadata-only readiness contract. It never returns a host,
/// credential, prompt, document body, embedding vector, or unrestricted record.
/// </summary>
public sealed class CelarAiKnowledgeFabricService
{
    public const string ContractVersion = "celar-ai-knowledge-fabric-v1-20260806";

    private readonly CelarAiCapabilityRoutingStore _store;
    private readonly CelarAiConsumerAssuranceRegistry _assurance;
    private readonly PulseAiPrivateDocumentRuntimeService _runtime;
    private readonly ProjectPulseAiHealthRegistry _health;

    public CelarAiKnowledgeFabricService(
        CelarAiCapabilityRoutingStore store,
        CelarAiConsumerAssuranceRegistry assurance,
        PulseAiPrivateDocumentRuntimeService runtime,
        ProjectPulseAiHealthRegistry health)
    {
        _store = store;
        _assurance = assurance;
        _runtime = runtime;
        _health = health;
    }

    public async Task<CelarAiKnowledgeFabricSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        var routes = await _store.LoadRoutesAsync(cancellationToken);
        var consumers = _assurance.Snapshots();
        var runtime = await _runtime.GetReadinessAsync(cancellationToken);
        var profile = await _store.LoadPrivateModelProfileAsync(cancellationToken);
        var inferenceResolution = profile.EndpointConfigured
            ? await PulseAiPrivateEndpointPolicy.VerifyResolvedPrivateEndpointAsync(
                profile.Endpoint,
                profile.PrivateHostAllowlist,
                requireHttps: true,
                allowLoopback: false,
                cancellationToken: cancellationToken)
            : new PulseAiPrivateEndpointPolicy.ResolutionResult(false, null, "not_configured", 0);
        var databaseResolution = await VerifyDatabaseAsync(cancellationToken);
        var probe = release.IsCandidate
            ? null
            : await _store.LoadPrivateProbeEvidenceAsync(profile.Revision, cancellationToken);
        var privateHealth = _health.Snapshots().FirstOrDefault(item =>
            string.Equals(item.Provider, CelarAiCapabilityTargets.CelarAi, StringComparison.OrdinalIgnoreCase));
        var inferenceRuntimeVerified = release.IsCandidate
            ? privateHealth is { ProbeStatus: "available", LastProbeSuccessAt: { } successAt }
                && successAt >= DateTimeOffset.UtcNow.AddMinutes(-15)
            : probe is { Available: true, Fresh: true } && probe.ProfileRevision == profile.Revision;

        var expectedFeatures = CelarAiCapabilityCatalog.Definitions.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var routeMap = routes.ToDictionary(route => route.FeatureCode, StringComparer.OrdinalIgnoreCase);
        var routeGraphReady = routeMap.Count == expectedFeatures.Count
            && expectedFeatures.All(routeMap.ContainsKey)
            && routes.All(route => route.Targets.Count == CelarAiCapabilityTargets.DefaultOrder.Length
                && route.Targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() == CelarAiCapabilityTargets.DefaultOrder.Length
                && string.Equals(route.Targets[^1], CelarAiCapabilityTargets.Local, StringComparison.OrdinalIgnoreCase))
            && consumers.Count == expectedFeatures.Count
            && consumers.All(item => item.CentralRouterConnected
                && item.PrivateContextCompliant
                && item.DirectProviderFree
                && routeMap.ContainsKey(item.Feature));

        var endpointStatuses = new[]
        {
            Endpoint("private_inference", true, profile.Ready, inferenceResolution.Approved, inferenceRuntimeVerified, inferenceResolution.Reason),
            Endpoint("private_database", true, databaseResolution.Configured, databaseResolution.Private, databaseResolution.Ready, databaseResolution.DiagnosticCode),
            Endpoint("private_malware_scanning", true, runtime.ClamAvConfigured || runtime.PreScanAttestationConfigured, runtime.MalwareScannerEndpointPrivate, runtime.MalwareScannerEndpointPrivate, runtime.MalwareScannerEndpointPrivate ? "verified" : "private_scanner_not_verified"),
            Endpoint("private_ocr", runtime.AwaitingOcrJobCount > 0, runtime.OcrConfigured, runtime.OcrEndpointPrivate, runtime.AwaitingOcrJobCount == 0 || runtime.OcrEndpointPrivate, runtime.OcrEndpointPrivate ? "verified" : runtime.AwaitingOcrJobCount == 0 ? "not_required" : "private_ocr_not_verified"),
            Endpoint("private_embedding", !runtime.LexicalOnlyCompletionApproved, runtime.EmbeddingConfigured, runtime.EmbeddingEndpointPrivate, runtime.EmbeddingEndpointPrivate || runtime.LexicalOnlyCompletionApproved, runtime.EmbeddingEndpointPrivate ? "verified" : runtime.LexicalOnlyCompletionApproved ? "approved_lexical_only" : "private_embedding_not_verified"),
            Endpoint("persistent_private_content_storage", true, runtime.UploadStorageProductionReady, runtime.UploadStorageProductionReady, runtime.UploadStorageProductionReady, runtime.UploadStorageProductionReady ? "verified" : "persistent_storage_not_ready")
        };
        var endpointsReady = endpointStatuses.All(item => !item.Required
            || (item.Configured && item.PrivateBoundaryVerified && item.RuntimeVerified));
        var contentGraphReady = runtime.ProcessingTablesAvailable
            && runtime.LexicalIndexAvailable
            && runtime.ReadyDocumentCount > 0
            && runtime.ActiveVersionCount > 0
            && runtime.ActiveChunkCount > 0
            && runtime.ReadySowDocumentCount > 0;

        var capabilities = consumers.Select(item =>
        {
            var route = routeMap.TryGetValue(item.Feature, out var configuredRoute)
                ? configuredRoute.Targets
                : CelarAiCapabilityTargets.DefaultOrder;
            var connected = item.CentralRouterConnected
                && item.PrivateContextCompliant
                && item.DirectProviderFree
                && routeMap.ContainsKey(item.Feature);
            return new CelarAiCapabilityConnectionStatus(
                item.Feature,
                item.Module,
                item.EntryPoint,
                route,
                connected,
                item.PrivateContextCompliant,
                item.DirectProviderFree,
                contentGraphReady && endpointsReady,
                connected
                    ? contentGraphReady && endpointsReady ? "connected_private_knowledge_ready" : "connected_runtime_attention_required"
                    : "not_connected",
                item.LastExercisedAt,
                item.LastSuccessAt);
        }).ToArray();

        var blockers = new List<string>();
        if (!routeGraphReady) blockers.Add("The Module 064 capability and consumer route graph is incomplete or invalid.");
        if (!contentGraphReady) blockers.Add("The private content graph does not yet have a current ready SOW/GSD, active authoritative version, and searchable chunk index.");
        blockers.AddRange(endpointStatuses
            .Where(item => item.Required && (!item.Configured || !item.PrivateBoundaryVerified || !item.RuntimeVerified))
            .Select(item => $"{item.Component} requires private-boundary and runtime verification ({item.DiagnosticCode})."));
        blockers.AddRange(runtime.Blockers);
        blockers.AddRange(runtime.MissingConfiguration);
        var ready = routeGraphReady && contentGraphReady && endpointsReady;
        var sourceCommit = release.RunningSourceCommit.Length > 0
            ? release.RunningSourceCommit
            : release.SourceCommit.Length > 0
                ? release.SourceCommit
                : Environment.GetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.RunningSourceCommitVariable)?.Trim() ?? "not_recorded";

        return new CelarAiKnowledgeFabricSnapshot(
            ready ? "celar_ai_knowledge_fabric_ready" : "celar_ai_knowledge_fabric_attention_required",
            ready,
            routeGraphReady,
            contentGraphReady,
            endpointsReady,
            sourceCommit,
            PulseAiProductKnowledgeCatalog.ContractVersion,
            PulseAiSystemKnowledgeCatalog.ContractVersion,
            runtime.ContractVersion,
            CelarAiCapabilityCatalog.Definitions.Count,
            consumers.Count,
            CelarAiCapabilityTargets.All.Length,
            PulseAiSystemKnowledgeCatalog.Tools.Count,
            routes.Sum(route => route.Targets.Count) + consumers.Count + PulseAiSystemKnowledgeCatalog.Tools.Count,
            runtime.ReadyDocumentCount,
            runtime.ReadySowDocumentCount,
            runtime.PendingSowDocumentCount,
            runtime.ActiveVersionCount,
            runtime.ActiveChunkCount,
            runtime.EmbeddedChunkCount,
            runtime.UnembeddedChunkCount,
            runtime.LastIndexedAt,
            [
                "capability -> consumer module -> Module 064 route -> governed target",
                "project -> document -> authoritative version -> section or worksheet -> chunk -> citation",
                "system question -> authorized tool -> permission-scoped evidence -> comprehensive answer"
            ],
            endpointStatuses,
            capabilities,
            blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            DateTimeOffset.UtcNow);
    }

    private static CelarAiKnowledgeEndpointStatus Endpoint(
        string component,
        bool required,
        bool configured,
        bool privateBoundaryVerified,
        bool runtimeVerified,
        string diagnosticCode) =>
        new(
            component,
            required,
            configured,
            privateBoundaryVerified,
            runtimeVerified,
            !required && !configured
                ? "not_required"
                : configured && privateBoundaryVerified && runtimeVerified
                    ? "ready"
                    : "attention_required",
            diagnosticCode);

    private static async Task<(bool Configured, bool Private, bool Ready, string DiagnosticCode)> VerifyDatabaseAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = ProjectPulseAiDatabaseConnection.Resolve();
            if (string.IsNullOrWhiteSpace(connectionString))
                return (false, false, false, "database_not_configured");
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var resolution = await PulseAiPrivateEndpointPolicy.VerifyPrivateHostAsync(
                builder.Host,
                allowLoopback: false,
                cancellationToken);
            return (true, resolution.Approved, resolution.Approved, resolution.Reason);
        }
        catch (Exception)
        {
            return (false, false, false, "database_private_dns_verification_unavailable");
        }
    }
}
