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

                await LayaWorkerLease.RunAsync(
                    token => ProcessAsync(job, options, token),
                    token => _repository.RenewLeaseAsync(job, options.LeaseSeconds, token),
                    TimeSpan.FromSeconds(Math.Max(5, options.LeaseSeconds / 3)),
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
            cancellationToken.ThrowIfCancellationRequested();
            // Permission, retention and source evidence can change during inference.
            // Re-read through the owning resolver before the existing transaction
            // locks and verifies the current version during SaveDecisionAsync.
            var current = await _sourceReader.ReadAsync(
                job.ServicePrincipalUserId, job.DocumentId, cancellationToken,
                classificationAdmission: true);
            if (current is null || !LayaProcessedSourceReader.SameEvidence(processed, current))
            {
                await _repository.CompleteAsync(job, "cancelled", "laya_source_changed",
                    "The authorized source or its evidence changed during classification.",
                    new { sourceChanged = true, rawDocumentTextLogged = false }, cancellationToken);
                return;
            }
            await _repository.SaveDecisionAsync(job, current, answer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A lost lease or stopped host must not write a failed/retry outcome
            // with stale ownership. The lease coordinator/recovery owns that path.
            throw;
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
    }

    private static async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        try { await Task.Delay(duration, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
