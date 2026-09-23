namespace ProjectTime.Api.Ai;

/// <summary>
/// Executes only policy-enabled, durable Laya jobs. The configured document
/// service identity is used for the processing-admission read; it never creates
/// a session, grants document access, approves a document, or changes category.
/// </summary>
public sealed class LayaAutomaticClassificationWorker : BackgroundService
{
    private readonly LayaAutomaticClassificationRepository _repository;
    private readonly LayaProcessedSourceReader _sourceReader;
    private readonly ILogger<LayaAutomaticClassificationWorker> _logger;

    public LayaAutomaticClassificationWorker(
        LayaAutomaticClassificationRepository repository,
        LayaProcessedSourceReader sourceReader,
        ILogger<LayaAutomaticClassificationWorker> logger)
    {
        _repository = repository;
        _sourceReader = sourceReader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var options = PulseAiPrivateRuntimeOptions.FromEnvironment();
                if (!options.WorkerEnabled || options.DocumentServicePrincipalUserId is null)
                {
                    await DelayAsync(TimeSpan.FromSeconds(Math.Max(15, options.PollSeconds)), stoppingToken);
                    continue;
                }

                var job = await _repository.ClaimNextAsync(options, stoppingToken);
                if (job is null)
                {
                    await DelayAsync(TimeSpan.FromSeconds(Math.Max(5, options.PollSeconds)), stoppingToken);
                    continue;
                }

                await ProcessAsync(job, options, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Automatic Laya classification worker encountered a bounded failure without logging document content. Diagnostic={Diagnostic}",
                    exception.GetType().Name);
                await DelayAsync(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(
        LayaClassificationJob job,
        PulseAiPrivateRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        using var processingStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RenewLeaseAsync(job, options.LeaseSeconds, processingStop, cancellationToken);
        try
        {
            if (options.DocumentServicePrincipalUserId != job.ServicePrincipalUserId)
            {
                await _repository.CompleteAsync(job, "failed", "laya_service_identity_changed",
                    "The configured Laya service identity changed while the job was queued.",
                    new { rawDocumentTextLogged = false }, cancellationToken);
                return;
            }

            var processed = await _sourceReader.ReadAsync(
                job.ServicePrincipalUserId,
                job.DocumentId,
                cancellationToken,
                classificationAdmission: true);
            if (processed is null || !processed.Ready)
            {
                await _repository.CompleteAsync(job, "failed", "laya_source_not_ready",
                    "The document no longer has a current, clean, extracted source version.",
                    new { sourceReady = false, rawDocumentTextLogged = false }, cancellationToken);
                return;
            }
            if (processed.VersionId != job.VersionId || processed.SourceSha256 != job.SourceSha256)
            {
                await _repository.CompleteAsync(job, "cancelled", "laya_source_changed",
                    "The document was replaced after classification was queued.",
                    new { sourceChanged = true, rawDocumentTextLogged = false }, cancellationToken);
                return;
            }

            var answer = LayaDecisionContract.Validate(
                await LayaDecisionTransport.SendAsync(processed.Excerpt, cancellationToken));
            await _repository.SaveDecisionAsync(job, processed, answer, cancellationToken);
        }
        catch (LayaDecisionFailure exception)
        {
            var retry = job.AttemptCount < job.MaximumAttempts
                && exception.Code is "decision_busy" or "decision_timeout" or "decision_unavailable";
            await _repository.CompleteAsync(
                job,
                retry ? "retry_wait" : "failed",
                exception.Code,
                retry
                    ? "The Laya gateway was unavailable; a bounded retry was scheduled."
                    : "Laya classification did not return an accepted typed recommendation.",
                new { rawDocumentTextLogged = false, externalFallbackAllowed = false },
                cancellationToken);
        }
        catch (OperationCanceledException) when (processingStop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The lease owner changed; the next worker will recover the bounded job.
        }
        catch (Exception exception)
        {
            var retry = job.AttemptCount < job.MaximumAttempts;
            await _repository.CompleteAsync(
                job,
                retry ? "retry_wait" : "failed",
                "laya_worker_failure",
                retry ? "Automatic Laya classification failed; a bounded retry was scheduled."
                    : "Automatic Laya classification reached its bounded retry limit.",
                new { diagnostic = exception.GetType().Name, rawDocumentTextLogged = false },
                cancellationToken);
        }
        finally
        {
            processingStop.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    private async Task RenewLeaseAsync(
        LayaClassificationJob job,
        int leaseSeconds,
        CancellationTokenSource processingStop,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, leaseSeconds / 3)));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (!await _repository.RenewLeaseAsync(job, leaseSeconds, cancellationToken))
            {
                processingStop.Cancel();
                return;
            }
        }
    }

    private static async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        try { await Task.Delay(duration, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
