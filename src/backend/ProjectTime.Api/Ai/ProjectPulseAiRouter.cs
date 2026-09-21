namespace ProjectTime.Api.Ai;

// Compatibility entry point only. All generation, availability decisions,
// refusals and fallback sequencing belong to the persisted Module 064 router.
// Keeping a second implementation here allowed callers to use legacy
// environment-derived priorities instead of the administrator's saved route.
public sealed class ProjectPulseAiRouter(CelarAiCapabilityRouter authority)
{
    public Task<bool> IsFirstTargetAsync(string feature, string target,
        CancellationToken cancellationToken = default) =>
        authority.IsFirstTargetAsync(feature, target, cancellationToken);

    public void RecordAlreadyExecutedPrivateAttempt(string feature, string correlationId,
        bool succeeded, string diagnosticCode) =>
        authority.RecordAlreadyExecutedPrivateAttempt(feature, correlationId, succeeded, diagnosticCode);

    public Task<ProjectPulseAiRouteResult> GenerateAsync(ProjectPulseAiGenerationRequest request,
        CelarAiCapabilityExecutionContext execution, Func<string> localFallback,
        CancellationToken cancellationToken = default) =>
        authority.GenerateAsync(request, execution, localFallback, cancellationToken);

    public Task<ProjectPulseAiRouteResult> GenerateAsync(ProjectPulseAiGenerationRequest request,
        Func<string> localFallback, CancellationToken cancellationToken = default) =>
        authority.GenerateAsync(request, UnclassifiedContext(request), localFallback, cancellationToken);

    public Task<ProjectPulseAiRouteResult> GenerateAsync(ProjectPulseAiGenerationRequest request,
        Func<string> localFallback, bool skipPrivateTarget,
        CancellationToken cancellationToken = default) =>
        authority.GenerateAsync(request, UnclassifiedContext(request), localFallback, skipPrivateTarget, cancellationToken);

    // Legacy callers provide no attested public capsule. Treat that input as
    // private, rather than invent permission to disclose it to a cloud model.
    // Consumers needing sanitized external assistance must supply the canonical
    // execution context through the explicit overload above.
    private static CelarAiCapabilityExecutionContext UnclassifiedContext(ProjectPulseAiGenerationRequest request) =>
        new(CelarAiCapabilityCatalog.NormalizeFeature(request.Feature), true, false, false, false,
            false, [], "legacy", "legacy-unclassified");
}
