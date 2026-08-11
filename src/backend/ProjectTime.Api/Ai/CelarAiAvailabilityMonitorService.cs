namespace ProjectTime.Api.Ai;

/// <summary>
/// Protected-Test availability worker. Observe-only monitoring is independent
/// from automatic defect creation. PostgreSQL advisory-lock leadership ensures
/// that only one API replica performs a scheduled cycle while every other
/// replica remains a healthy standby.
/// </summary>
public sealed class CelarAiAvailabilityMonitorService : BackgroundService
{
    private readonly CelarAiDefectOrchestrationService _operations;
    private readonly CelarAiMonitorLeadershipService _leadership;
    private readonly ILogger<CelarAiAvailabilityMonitorService> _logger;

    public CelarAiAvailabilityMonitorService(
        CelarAiDefectOrchestrationService operations,
        CelarAiMonitorLeadershipService leadership,
        ILogger<CelarAiAvailabilityMonitorService> logger)
    {
        _operations = operations;
        _leadership = leadership;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!CelarAiOperationalFeatureFlags.MonitoringEnabled)
        {
            _logger.LogInformation(
                "Ask Celar AI monitoring is disabled. Automatic defect creation remains disabled as well.");
            return;
        }

        var interval = TimeSpan.FromSeconds(CelarAiOperationsPolicy.ProbeIntervalSeconds);
        using var timer = new PeriodicTimer(interval);
        await RunOnceAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var lease = await _leadership.TryAcquireAsync(cancellationToken);
            if (lease is null)
            {
                _logger.LogDebug("Celar AI monitoring cycle skipped by a healthy standby replica.");
                return;
            }

            try
            {
                await _operations.RunScheduledProbesAsync(cancellationToken);
                await lease.CompleteCycleAsync("cycle_completed", null, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                                               || !cancellationToken.IsCancellationRequested)
            {
                await lease.CompleteCycleAsync(
                    "cycle_failed",
                    exception.GetType().Name,
                    cancellationToken);
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Ask Celar AI monitoring cycle failed ({ExceptionType}).",
                exception.GetType().Name);
        }
    }
}
