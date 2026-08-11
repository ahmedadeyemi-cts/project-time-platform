namespace ProjectTime.Api.Ai;

/// <summary>
/// Protected-Test availability worker. It performs deterministic probes through
/// CelarAiDefectOrchestrationService and never sends prompts, private documents,
/// tool bodies, or secrets to a model. Automatic defect creation remains off
/// unless both the deployment-level Test flag and the per-policy flag are on.
/// </summary>
public sealed class CelarAiAvailabilityMonitorService : BackgroundService
{
    private readonly CelarAiDefectOrchestrationService _operations;
    private readonly ILogger<CelarAiAvailabilityMonitorService> _logger;

    public CelarAiAvailabilityMonitorService(
        CelarAiDefectOrchestrationService operations,
        ILogger<CelarAiAvailabilityMonitorService> logger)
    {
        _operations = operations;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!CelarAiOperationsPolicy.AutomaticMonitoringEnabled)
        {
            _logger.LogInformation(
                "Ask Celar AI automatic defect monitoring is disabled outside its protected Test activation boundary.");
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
            await _operations.RunScheduledProbesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Ask Celar AI automatic monitoring cycle failed ({ExceptionType}).",
                exception.GetType().Name);
        }
    }
}
