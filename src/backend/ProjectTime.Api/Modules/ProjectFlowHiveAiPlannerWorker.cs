namespace ProjectTime.Api.Modules;

/// <summary>
/// Advances durable FlowHive AI Planner runs outside the inbound HTTP request
/// lifetime. The planner run row is the durable queue; a PostgreSQL advisory
/// lock inside the orchestration module prevents two API instances from
/// advancing the same run at the same time.
/// </summary>
internal sealed class ProjectFlowHiveAiPlannerWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ProjectFlowHiveAiPlannerWorker> _logger;

    public ProjectFlowHiveAiPlannerWorker(
        IServiceProvider services,
        ILogger<ProjectFlowHiveAiPlannerWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var processed = await ProjectFlowHiveAiPlannerOrchestrationModule
                    .ProcessNextQueuedRunAsync(scope.ServiceProvider, stoppingToken);
                await DelayAsync(
                    processed ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(3),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "FlowHive AI Planner background worker encountered a bounded failure. No project-document content was logged.");
                await DelayAsync(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private static async Task DelayAsync(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown.
        }
    }
}
