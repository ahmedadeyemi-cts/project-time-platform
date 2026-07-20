namespace ProjectTime.Api.Ai;

public sealed class ProjectPulseAiHealthCoordinator
{
    private readonly IReadOnlyDictionary<string, IProjectPulseAiProvider> _providers;
    private readonly ProjectPulseAiHealthRegistry _health;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public ProjectPulseAiHealthCoordinator(
        IEnumerable<IProjectPulseAiProvider> providers,
        ProjectPulseAiHealthRegistry health)
    {
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
            foreach (var provider in _providers.Values)
            {
                if (!force && !_health.CanAttempt(provider.Code, out _)) continue;

                var snapshot = _health.Snapshot(provider.Code);
                if (!snapshot.Enabled || !snapshot.Configured) continue;

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
            _logger.LogWarning(exception, "Module 064 background provider health refresh failed.");
        }
    }
}
