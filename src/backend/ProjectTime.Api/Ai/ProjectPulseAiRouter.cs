namespace ProjectTime.Api.Ai;

public sealed class ProjectPulseAiRouter
{
    private readonly ProjectPulseAiConfiguration _configuration;
    private readonly ProjectPulseAiHealthRegistry _health;
    private readonly IReadOnlyDictionary<string, IProjectPulseAiProvider> _providers;
    private readonly ILogger<ProjectPulseAiRouter> _logger;

    public ProjectPulseAiRouter(
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiHealthRegistry health,
        IEnumerable<IProjectPulseAiProvider> providers,
        ILogger<ProjectPulseAiRouter> logger)
    {
        _configuration = configuration;
        _health = health;
        _providers = providers.ToDictionary(provider => provider.Code, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public Task<bool> IsFirstTargetAsync(
        string feature,
        string target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = _configuration.RouteFor(feature).FirstOrDefault();
        return Task.FromResult(string.Equals(first, target, StringComparison.OrdinalIgnoreCase));
    }

    // Compatibility surface for the canonical Timesheet source. Production
    // compilation substitutes CelarAiCapabilityRouter, which additionally owns
    // consumer-assurance evidence for this already-executed private attempt.
    public void RecordAlreadyExecutedPrivateAttempt(
        string feature,
        string correlationId,
        bool succeeded,
        string diagnosticCode)
    {
        _ = feature;
        _ = correlationId;
        if (succeeded)
        {
            _health.RecordSuccess(
                CelarAiCapabilityTargets.CelarAi,
                usage: null,
                requestId: null,
                outcome: ProjectPulseAiOutcomes.Success);
            return;
        }

        _health.RecordFailure(
            CelarAiCapabilityTargets.CelarAi,
            string.IsNullOrWhiteSpace(diagnosticCode) ? "private_model_unavailable" : diagnosticCode,
            requestId: null);
    }

    public async Task<ProjectPulseAiRouteResult> GenerateAsync(
        ProjectPulseAiGenerationRequest request,
        Func<string> localFallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localFallback);

        var attempted = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (var providerCode in _configuration.RouteFor(request.Feature))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(providerCode, ProjectPulseAiProviders.Local, StringComparison.OrdinalIgnoreCase))
            {
                var local = localFallback();
                _health.RecordSuccess(ProjectPulseAiProviders.Local, null, null, "local_fallback");

                return new ProjectPulseAiRouteResult(
                    local,
                    ProjectPulseAiProviders.Local,
                    ProjectPulseAiOutcomes.Success,
                    failed.Count > 0 || skipped.Count > 0
                        ? "Remote AI providers were unavailable, disabled, or not configured. The governed local template was used."
                        : null,
                    attempted,
                    skipped,
                    null,
                    null);
            }

            if (!_providers.TryGetValue(providerCode, out var provider))
            {
                skipped.Add(providerCode);
                continue;
            }

            // The health registry can be created before encrypted secrets finish
            // loading. Reconcile from the live shared configuration before every
            // route decision so Module 001 never skips a configured provider merely
            // because the process started with an unhydrated health snapshot.
            _health.ApplyConfiguration(_configuration.Provider(providerCode));
            if (!_health.CanAttempt(providerCode, out _))
            {
                skipped.Add(providerCode);
                continue;
            }

            attempted.Add(providerCode);
            ProjectPulseAiProviderResult result;

            try
            {
                result = await provider.GenerateAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Module 064 provider {Provider} failed without exposing provider details to the caller.",
                    providerCode);
                _health.RecordFailure(providerCode, "provider_unhandled_failure", null);
                failed.Add(providerCode);
                continue;
            }

            _logger.LogInformation(
                "Module 064 provider attempt completed. Provider={Provider} Model={Model} Outcome={Outcome} Code={Code} HttpStatus={HttpStatus} RequestId={RequestId}",
                providerCode,
                _configuration.Provider(providerCode).Model,
                result.Outcome,
                result.Code,
                result.HttpStatusCode,
                result.RequestId);

            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Content))
            {
                _health.RecordSuccess(
                    providerCode,
                    result.Usage,
                    result.RequestId,
                    rateLimits: result.RateLimits);
                return new ProjectPulseAiRouteResult(
                    result.Content,
                    providerCode,
                    result.Outcome,
                    failed.Count > 0 || skipped.Count > 0
                        ? $"{DisplayName(providerCode)} generated the response after a higher-priority provider was unavailable."
                        : null,
                    attempted,
                    skipped,
                    result.Usage,
                    result.RequestId);
            }

            if (result.IsRefusal)
            {
                _health.RecordRefusal(
                    providerCode,
                    result.Usage,
                    result.RequestId,
                    result.RateLimits);
                return new ProjectPulseAiRouteResult(
                    string.Empty,
                    providerCode,
                    ProjectPulseAiOutcomes.Refusal,
                    $"{DisplayName(providerCode)} declined this request under its safety controls. No fallback provider was attempted.",
                    attempted,
                    skipped,
                    result.Usage,
                    result.RequestId);
            }

            _health.RecordFailure(
                providerCode,
                result.Code ?? "provider_unavailable",
                result.RequestId);
            failed.Add(providerCode);
        }

        var fallback = localFallback();
        _health.RecordSuccess(ProjectPulseAiProviders.Local, null, null, "local_fallback");
        return new ProjectPulseAiRouteResult(
            fallback,
            ProjectPulseAiProviders.Local,
            ProjectPulseAiOutcomes.Success,
            "No configured AI provider was available. The governed local template was used.",
            attempted,
            skipped,
            null,
            null);
    }

    // Compatibility overload used by consumers that may have already attempted
    // the private Celar target. The legacy provider router has no Celar target,
    // so its route remains unchanged.
    public Task<ProjectPulseAiRouteResult> GenerateAsync(
        ProjectPulseAiGenerationRequest request,
        Func<string> localFallback,
        bool skipPrivateTarget,
        CancellationToken cancellationToken = default) =>
        GenerateAsync(request, localFallback, cancellationToken);

    private static string DisplayName(string provider) =>
        string.Equals(provider, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)
            ? "Claude"
            : string.Equals(provider, ProjectPulseAiProviders.OpenAi, StringComparison.OrdinalIgnoreCase)
                ? "OpenAI"
                : "The local template";
}
