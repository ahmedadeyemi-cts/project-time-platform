namespace ProjectTime.Api.Ai;

/// <summary>
/// Runs when explicitly enabled by private-runtime configuration. Protected Test
/// may activate the worker from source only when the exact running commit and all
/// private scanning, OCR, embedding, and RAG dependencies are present. Production
/// still requires the explicit worker configuration flag.
/// </summary>
public sealed class PulseAiPrivateDocumentRuntimeWorker : BackgroundService
{
    private const string EnvironmentVariable = "PROJECTPULSE_ENVIRONMENT";
    private const string PrivateRagEnabledVariable = "PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED";
    private const string WorkerEnabledVariable = "PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED";

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
            if (!options.WorkerEnabled && TryActivateProtectedTestWorker(options))
                options = PulseAiPrivateRuntimeOptions.FromEnvironment();

            if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate)
            {
                await DelayAsync(TimeSpan.FromSeconds(Math.Max(30, options.PollSeconds)), stoppingToken);
                continue;
            }
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
                if (result.Status == "document_snapshot_cleanup_unavailable")
                {
                    _logger.LogWarning(
                        "Celar AI private snapshot cleanup was unavailable; no orphan deletion or document processing occurred. Diagnostic=document_snapshot_cleanup_unavailable");
                    await DelayAsync(TimeSpan.FromSeconds(Math.Max(10, options.PollSeconds)), stoppingToken);
                    continue;
                }
                if (result.Status == "queue_empty")
                {
                    await DelayAsync(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
                    continue;
                }

                _logger.LogInformation(
                    "Celar AI private processing cycle completed. Status={Status} JobId={JobId} DocumentId={DocumentId} Sections={SectionCount} Chunks={ChunkCount} Embedded={EmbeddedChunkCount} Diagnostic={Diagnostic}",
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
                    "Celar AI private document worker encountered a bounded processing failure. No document content was logged.");
                await DelayAsync(TimeSpan.FromSeconds(Math.Max(10, options.PollSeconds)), stoppingToken);
            }
        }
    }

    private bool TryActivateProtectedTestWorker(PulseAiPrivateRuntimeOptions options)
    {
        var environment = Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim() ?? string.Empty;
        if (!environment.Equals("test", StringComparison.OrdinalIgnoreCase))
            return false;

        var runningSourceCommit = Environment
            .GetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.RunningSourceCommitVariable)?
            .Trim()
            .ToLowerInvariant() ?? string.Empty;
        if (runningSourceCommit.Length != 40 || !runningSourceCommit.All(Uri.IsHexDigit))
            return false;

        if (!bool.TryParse(
                Environment.GetEnvironmentVariable(PrivateRagEnabledVariable),
                out var privateRagEnabled)
            || !privateRagEnabled
            || !options.MalwareScannerConfigured
            || !options.OcrConfigured
            || !options.EmbeddingConfigured)
        {
            return false;
        }

        Environment.SetEnvironmentVariable(WorkerEnabledVariable, "true");
        _logger.LogInformation(
            "Protected Test private document worker activated from the exact running source after dependency verification. SourceCommit={SourceCommit}",
            runningSourceCommit);
        return true;
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
