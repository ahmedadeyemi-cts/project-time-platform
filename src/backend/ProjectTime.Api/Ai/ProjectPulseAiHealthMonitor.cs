namespace ProjectTime.Api.Ai;

public sealed class ProjectPulseAiHealthCoordinator
{
    private readonly ProjectPulseAiConfiguration _configuration;
    private readonly IReadOnlyDictionary<string, IProjectPulseAiProvider> _providers;
    private readonly ProjectPulseAiHealthRegistry _health;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public ProjectPulseAiHealthCoordinator(
        ProjectPulseAiConfiguration configuration,
        IEnumerable<IProjectPulseAiProvider> providers,
        ProjectPulseAiHealthRegistry health)
    {
        _configuration = configuration;
        _providers = providers.ToDictionary(provider => provider.Code, StringComparer.OrdinalIgnoreCase);
        _health = health;
    }

    public async Task<IReadOnlyList<ProjectPulseAiProviderHealthSnapshot>> RefreshAsync(
        bool force,
        CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken))
        {
            return _health.Snapshots();
        }

        try
        {
            var maximumAge = TimeSpan.FromSeconds(_configuration.HealthIntervalSeconds);
            foreach (var provider in _providers.Values)
            {
                // Encrypted secrets and provider settings can load after the health
                // registry is constructed. Always reconcile from the live shared
                // configuration before deciding whether a provider can be probed.
                var liveConfiguration = _configuration.Provider(provider.Code);
                _health.ApplyConfiguration(liveConfiguration);

                var snapshot = _health.Snapshot(provider.Code);
                if (!snapshot.Enabled || !snapshot.Configured) continue;
                if (!_health.ShouldProbe(provider.Code, maximumAge, force)) continue;
                if (!force && !_health.CanAttempt(provider.Code, out _)) continue;

                _health.MarkProbeStarted(provider.Code);
                ProjectPulseAiProbeResult result;
                try
                {
                    result = await provider.ProbeAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    result = new ProjectPulseAiProbeResult(
                        provider.Code,
                        false,
                        "health_probe_failed",
                        "Provider health probe failed.",
                        null,
                        null);
                }

                _health.RecordProbe(result);
            }

            return _health.Snapshots();
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}

public sealed class ProjectPulseAiHealthMonitor : BackgroundService
{
    private readonly ProjectPulseAiConfiguration _configuration;
    private readonly ProjectPulseAiHealthCoordinator _coordinator;
    private readonly ILogger<ProjectPulseAiHealthMonitor> _logger;

    public ProjectPulseAiHealthMonitor(
        ProjectPulseAiConfiguration configuration,
        ProjectPulseAiHealthCoordinator coordinator,
        ILogger<ProjectPulseAiHealthMonitor> logger)
    {
        _configuration = configuration;
        _coordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate)
        {
            _logger.LogInformation(
                "Module 064 background provider health probes are disabled in the release-candidate phase; the combined verification request owns all candidate probes.");
            return;
        }

        // Run once when the API starts, after the registered secret loader has
        // hydrated the shared configuration, and then keep the result current.
        await RefreshSafely(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_configuration.HealthIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshSafely(stoppingToken);
        }
    }

    private async Task RefreshSafely(CancellationToken cancellationToken)
    {
        try
        {
            await _coordinator.RefreshAsync(false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Module 064 background provider health refresh failed.");
        }
    }
}
