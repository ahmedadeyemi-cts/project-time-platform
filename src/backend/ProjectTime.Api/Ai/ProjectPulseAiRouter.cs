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
                _health.RecordRefusal(providerCode, result.Usage, result.RequestId, result.RateLimits);
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

            _health.RecordFailure(providerCode, result.Code ?? "provider_unavailable", result.RequestId);
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

    private static string DisplayName(string provider) =>
        string.Equals(provider, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)
            ? "Claude"
            : string.Equals(provider, ProjectPulseAiProviders.OpenAi, StringComparison.OrdinalIgnoreCase)
                ? "OpenAI"
                : "The local template";
}
