namespace ProjectTime.Api.Ai;

/// <summary>
/// Runs only when explicitly enabled by private-runtime configuration. Deploying
/// the source alone does not start document processing or external traffic. Each
/// cycle remains outside the Claude, OpenAI, and Module 064 generation path.
/// </summary>
public sealed class PulseAiPrivateDocumentRuntimeWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PulseAiPrivateDocumentRuntimeWorker> _logger;

    public PulseAiPrivateDocumentRuntimeWorker(
        IServiceProvider services,
        ILogger<PulseAiPrivateDocumentRuntimeWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = PulseAiPrivateRuntimeOptions.FromEnvironment();
            if (!options.WorkerEnabled)
            {
                await DelayAsync(TimeSpan.FromSeconds(Math.Max(30, options.PollSeconds)), stoppingToken);
                continue;
            }

            try
            {
                using var scope = _services.CreateScope();
                var runtime = scope.ServiceProvider.GetRequiredService<PulseAiPrivateDocumentRuntimeService>();
                var result = await runtime.ProcessNextAsync(stoppingToken);
                if (result.Status == "queue_empty")
                {
                    await DelayAsync(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
                    continue;
                }

                _logger.LogInformation(
                    "Pulse AI private processing cycle completed. Status={Status} JobId={JobId} DocumentId={DocumentId} Sections={SectionCount} Chunks={ChunkCount} Embedded={EmbeddedChunkCount} Diagnostic={Diagnostic}",
                    result.Status,
                    result.JobId,
                    result.DocumentId,
                    result.SectionCount,
                    result.ChunkCount,
                    result.EmbeddedChunkCount,
                    result.DiagnosticCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Pulse AI private document worker encountered a bounded processing failure. No document content was logged.");
                await DelayAsync(TimeSpan.FromSeconds(Math.Max(10, options.PollSeconds)), stoppingToken);
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
