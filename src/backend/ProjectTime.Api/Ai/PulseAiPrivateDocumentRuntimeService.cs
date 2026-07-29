namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateDocumentRuntimeService
{
    private readonly PulseAiPrivateDocumentRuntimeRepository _repository;
    private readonly PulseAiPrivateRuntimeSourceResolver _sourceResolver;
    private readonly PulseAiPrivateDocumentPipelineService _pipeline;
    private readonly PulseAiPrivateDocumentExtractionService _extractor;
    private readonly PulseAiPrivateMalwareScanner _malwareScanner;
    private readonly PulseAiPrivateOcrClient _ocrClient;
    private readonly PulseAiPrivateEmbeddingClient _embeddingClient;
    private readonly ILogger<PulseAiPrivateDocumentRuntimeService> _logger;

    public PulseAiPrivateDocumentRuntimeService(
        PulseAiPrivateDocumentRuntimeRepository repository,
        PulseAiPrivateRuntimeSourceResolver sourceResolver,
        PulseAiPrivateDocumentPipelineService pipeline,
        PulseAiPrivateDocumentExtractionService extractor,
        PulseAiPrivateMalwareScanner malwareScanner,
        PulseAiPrivateOcrClient ocrClient,
        PulseAiPrivateEmbeddingClient embeddingClient,
        ILogger<PulseAiPrivateDocumentRuntimeService> logger)
    {
        _repository = repository;
        _sourceResolver = sourceResolver;
        _pipeline = pipeline;
        _extractor = extractor;
        _malwareScanner = malwareScanner;
        _ocrClient = ocrClient;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public PulseAiPrivateRuntimeOptions Options() =>
        PulseAiPrivateRuntimeOptions.FromEnvironment();

    public async Task<PulseAiPrivateDocumentRuntimeReadiness> GetReadinessAsync(
        CancellationToken cancellationToken = default)
    {
        var options = Options();
        var schema = await _repository.InspectRuntimeSchemaAsync(cancellationToken);
        var counts = schema.Complete
            ? await _repository.GetCountsAsync(cancellationToken)
            : PulseAiPrivateDocumentRuntimeRepository.RuntimeCounts.Empty;
        var blockers = new List<string>();
        var ready = new List<string>
        {
            "permission-aware processing queue contract",
            "private malware scanning adapter",
            "private OCR adapter contract",
            "private OpenAI-compatible embedding adapter",
            "citation-preserving PostgreSQL lexical index",
            "private embedding storage and cosine-ready vectors",
            "document version, revocation, retry, cancellation, and immutable audit evidence"
        };
        var missing = new List<string>();

        if (!_repository.DatabaseConfigured)
            missing.Add("ProjectPulse database configuration is incomplete.");
        if (!schema.MigrationApplied)
            blockers.Add("Migration 052 has not been applied.");
        if (!schema.Complete)
            blockers.Add("One or more private document runtime tables are unavailable.");
        if (!options.WorkerEnabled)
            blockers.Add("The private document processing worker is disabled.");
        if (!options.ClamAvConfigured && !options.PreScanAttestationConfigured)
            blockers.Add("A private ClamAV endpoint or approved pre-scan attestation is required.");
        if (!options.OcrConfigured)
            blockers.Add("The private OCR endpoint is not configured; text-native documents can still be processed.");
        if (!options.EmbeddingConfigured && !options.AllowLexicalOnlyCompletion)
            blockers.Add("The private embedding endpoint is not configured and lexical-only completion is disabled.");
var ocrReason = "not_configured";
var ocrPrivate = options.OcrConfigured
    && PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
        options.OcrEndpoint,
        options.PrivateHostAllowlist,
        out _,
        out ocrReason);
var embeddingReason = "not_configured";
var embeddingPrivate = options.EmbeddingConfigured
    && PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
        options.EmbeddingEndpoint,
        options.PrivateHostAllowlist,
        out _,
        out embeddingReason);
        if (options.OcrConfigured && !ocrPrivate)
            blockers.Add($"The configured OCR endpoint was rejected by the private endpoint policy ({ocrReason}).");
        if (options.EmbeddingConfigured && !embeddingPrivate)
            blockers.Add($"The configured embedding endpoint was rejected by the private endpoint policy ({embeddingReason}).");
        if (schema.LexicalIndex) ready.Add("PostgreSQL full-text index is available");
        if (options.ClamAvConfigured) ready.Add("private ClamAV scanning is configured");
        if (options.PreScanAttestationConfigured) ready.Add("approved pre-scan attestation mode is configured");
        if (ocrPrivate) ready.Add("private OCR endpoint passed endpoint policy");
        if (embeddingPrivate) ready.Add("private embedding endpoint passed endpoint policy");
        if (options.AllowLexicalOnlyCompletion) ready.Add("lexical-only degraded completion is explicitly enabled");

        var fullyReady = schema.Complete
            && options.WorkerEnabled
            && (options.ClamAvConfigured || options.PreScanAttestationConfigured)
            && (embeddingPrivate || options.AllowLexicalOnlyCompletion);

        return new PulseAiPrivateDocumentRuntimeReadiness(
            Status: fullyReady
                ? "private_document_runtime_ready"
                : schema.Complete
                    ? "private_document_runtime_partially_ready"
                    : "private_document_runtime_schema_unavailable",
            ContractVersion: PulseAiPrivateRuntimePolicy.ContractVersion,
            MigrationApplied: schema.MigrationApplied,
            WorkerEnabled: options.WorkerEnabled,
            ProcessingTablesAvailable: schema.Complete,
            ClamAvConfigured: options.ClamAvConfigured,
            PreScanAttestationConfigured: options.PreScanAttestationConfigured,
            OcrConfigured: options.OcrConfigured,
            OcrEndpointPrivate: ocrPrivate,
            EmbeddingConfigured: options.EmbeddingConfigured,
            EmbeddingEndpointPrivate: embeddingPrivate,
            LexicalIndexAvailable: schema.LexicalIndex,
            EmbeddingStorageAvailable: schema.Chunks,
            QueuedJobCount: counts.Queued,
            RunningJobCount: counts.Running,
            FailedJobCount: counts.Failed,
            ReadyDocumentCount: counts.ReadyDocuments,
            ReadyCapabilities: ready.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Blockers: blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingConfiguration: missing,
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    public async Task<PulseAiPrivateDocumentRuntimeRepository.RuntimeAccess> LoadAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _repository.LoadAccessAsync(userId, cancellationToken);

    public async Task<PulseAiPrivateProcessingJob?> QueueAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        Guid documentId,
        PulseAiQueueDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (actualUserId != effectiveUserId) return null;
        if (!string.Equals(
                request.Confirmation?.Trim(),
                PulseAiPrivateRuntimePolicy.QueueConfirmation,
                StringComparison.Ordinal))
        {
            return null;
        }
        var access = await _repository.LoadAccessAsync(actualUserId, cancellationToken);
        if (!access.IsActive || !access.CanQueue) return null;
        var source = await _sourceResolver.ResolveAsync(effectiveUserId, documentId, cancellationToken);
        if (source is null) return null;
        return await _repository.EnqueueAsync(
            actualUserId,
            effectiveUserId,
            source,
            request.Purpose ?? "private_document_indexing",
            request.Priority ?? 50,
            request.MaximumAttempts ?? Options().MaximumAttempts,
            Guid.NewGuid().ToString("N"),
            cancellationToken);
    }

    public async Task<IReadOnlyList<PulseAiPrivateProcessingJob>> ListJobsAsync(
        Guid effectiveUserId,
        string? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var access = await _repository.LoadAccessAsync(effectiveUserId, cancellationToken);
        return await _repository.ListJobsAsync(access, status, limit, cancellationToken);
    }

    public async Task<PulseAiPrivateDocumentRuntimeState?> GetDocumentStateAsync(
        Guid effectiveUserId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var access = await _repository.LoadAccessAsync(effectiveUserId, cancellationToken);
        return await _repository.GetDocumentStateAsync(access, documentId, cancellationToken);
    }

    public async Task<bool> CancelAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        Guid jobId,
        PulseAiCancelDocumentJobRequest request,
        CancellationToken cancellationToken = default)
    {
        if (actualUserId != effectiveUserId) return false;
        if (!string.Equals(
                request.Confirmation?.Trim(),
                PulseAiPrivateRuntimePolicy.CancelConfirmation,
                StringComparison.Ordinal))
        {
            return false;
        }
        var access = await _repository.LoadAccessAsync(actualUserId, cancellationToken);
        if (!access.IsActive || !access.CanCancel) return false;
        var job = await _repository.GetJobAsync(jobId, cancellationToken);
        if (job is null) return false;
        var source = await _sourceResolver.ResolveAsync(effectiveUserId, job.DocumentId, cancellationToken);
        if (source is null) return false;
        return await _repository.RequestCancellationAsync(
            jobId,
            actualUserId,
            request.Reason ?? "Cancellation requested by an authorized Pulse AI operator.",
            cancellationToken);
    }

    public async Task<bool> RetryAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        Guid jobId,
        PulseAiRetryDocumentJobRequest request,
        CancellationToken cancellationToken = default)
    {
        if (actualUserId != effectiveUserId) return false;
        if (!string.Equals(
                request.Confirmation?.Trim(),
                PulseAiPrivateRuntimePolicy.RetryConfirmation,
                StringComparison.Ordinal))
        {
            return false;
        }
        var access = await _repository.LoadAccessAsync(actualUserId, cancellationToken);
        if (!access.IsActive || !access.CanRetry) return false;
        var job = await _repository.GetJobAsync(jobId, cancellationToken);
        if (job is null) return false;
        var source = await _sourceResolver.ResolveAsync(effectiveUserId, job.DocumentId, cancellationToken);
        if (source is null) return false;
        return await _repository.RetryAsync(
            jobId,
            actualUserId,
            request.Reason ?? "Retry requested after an authorized operator reviewed the blocker.",
            cancellationToken);
    }

    public async Task<PulseAiPrivateWorkerResult> ProcessNextAsync(
        CancellationToken cancellationToken = default)
    {
        var options = Options();
        if (!options.WorkerEnabled)
        {
            return Empty("worker_disabled", "worker_disabled");
        }
        var schema = await _repository.InspectRuntimeSchemaAsync(cancellationToken);
        if (!schema.Complete)
        {
            return Empty("runtime_schema_unavailable", "migration_052_not_ready");
        }

        var job = await _repository.ClaimNextAsync(options, cancellationToken);
        if (job is null) return Empty("queue_empty", string.Empty);

        try
        {
            if (await _repository.CancellationRequestedAsync(job.JobId, cancellationToken))
            {
                await _repository.CompleteTerminalAsync(
                    job,
                    "cancelled",
                    "cancelled",
                    "processing_cancelled_before_scan",
                    "cancelled",
                    "cancellation_requested",
                    "Processing was cancelled before document content was read.",
                    new { rawDocumentTextLogged = false },
                    cancellationToken);
                return Result("cancelled", job, null, 0, 0, 0, "cancellation_requested", []);
            }

            var effectiveUserId = job.EffectiveUserId ?? job.ActualUserId;
            if (effectiveUserId is null)
            {
                await FailAsync(job, "authorization_identity_missing", "The effective user identity is unavailable.", cancellationToken);
                return Result("failed", job, null, 0, 0, 0, "authorization_identity_missing", []);
            }
            var source = await _sourceResolver.ResolveAsync(
                effectiveUserId.Value,
                job.DocumentId,
                cancellationToken);
            if (source is null)
            {
                await FailAsync(job, "authorization_revoked", "The document is no longer available in the effective user's authorized scope.", cancellationToken);
                return Result("failed", job, null, 0, 0, 0, "authorization_revoked", []);
            }

            var scan = await _malwareScanner.ScanAsync(source.StoragePath, options, cancellationToken);
            if (scan.Infected)
            {
                await _repository.CompleteTerminalAsync(
                    job,
                    "quarantined",
                    "quarantined",
                    "malware_detected",
                    "quarantined",
                    "malware_detected",
                    "The private scanner detected malware. The document was not parsed, embedded, or indexed.",
                    scan.ToPublicEvidence(),
                    cancellationToken);
                return Result("quarantined", job, null, 0, 0, 0, "malware_detected", []);
            }
            if (!scan.Clean)
            {
                return await RetryOrFailAsync(
                    job,
                    "malware_scan_failed",
                    "The private malware scanner did not return a clean result.",
                    scan.ToPublicEvidence(),
                    cancellationToken);
            }

            await _repository.MarkStageAsync(
                job,
                "extracting",
                "extracting",
                "malware_scan_completed",
                scan.ToPublicEvidence(),
                cancellationToken);
            if (await _repository.CancellationRequestedAsync(job.JobId, cancellationToken))
            {
                await _repository.CompleteTerminalAsync(
                    job,
                    "cancelled",
                    "cancelled",
                    "processing_cancelled_before_extraction",
                    "cancelled",
                    "cancellation_requested",
                    "Processing was cancelled after scanning and before extraction.",
                    new { rawDocumentTextLogged = false },
                    cancellationToken);
                return Result("cancelled", job, null, 0, 0, 0, "cancellation_requested", []);
            }

            var pipelineOptions = _pipeline.Options() with
            {
                ExtractionPreviewEnabled = true,
                MalwareScanAttested = true,
                MalwareScannerMode = scan.Scanner
            };
            var extraction = await _extractor.ExtractAsync(source, pipelineOptions, cancellationToken);
            if (extraction.OcrRequired)
            {
                if (!options.OcrConfigured)
                {
                    await _repository.CompleteTerminalAsync(
                        job,
                        "awaiting_ocr",
                        "awaiting_ocr",
                        "private_ocr_required",
                        "blocked",
                        "private_ocr_not_configured",
                        "The document requires OCR, but no approved private OCR endpoint is configured.",
                        new
                        {
                            extraction.DocumentId,
                            extraction.SourceSha256,
                            extraction.PageCount,
                            rawDocumentTextLogged = false
                        },
                        cancellationToken);
                    return Result("awaiting_ocr", job, null, 0, 0, 0, "private_ocr_not_configured", extraction.Warnings);
                }

                await _repository.MarkStageAsync(
                    job,
                    "extracting",
                    "extracting",
                    "private_ocr_started",
                    new { endpointValidatedPrivate = true, rawDocumentTextLogged = false },
                    cancellationToken);
                var ocr = await _ocrClient.ExtractAsync(
                    source,
                    pipelineOptions,
                    options,
                    cancellationToken);
                if (!ocr.Succeeded)
                {
                    return await RetryOrFailAsync(
                        job,
                        ocr.DiagnosticCode,
                        "The approved private OCR adapter did not return usable document text.",
                        new
                        {
                            ocr.Status,
                            ocr.Provider,
                            ocr.Model,
                            ocr.DiagnosticCode,
                            rawDocumentTextLogged = false
                        },
                        cancellationToken);
                }
                extraction = new PulseAiDocumentExtractionResult(
                    Status: "extraction_preview_ready",
                    DocumentId: source.DocumentId,
                    OriginalFileName: source.OriginalFileName,
                    DetectedFormat: extraction.DetectedFormat,
                    ExtractionMethod: "private_ocr_adapter",
                    PageCount: ocr.PageCount,
                    SectionCount: ocr.Sections.Count,
                    CharacterCount: ocr.CharacterCount,
                    EstimatedTokenCount: EstimateTokens(ocr.CharacterCount),
                    OcrRequired: false,
                    SourceSha256: extraction.SourceSha256,
                    Safety: extraction.Safety,
                    Sections: ocr.Sections,
                    Warnings: [.. extraction.Warnings, .. ocr.Warnings],
                    Blockers: [],
                    GeneratedAt: DateTimeOffset.UtcNow);
            }

            if (!extraction.ExtractionSucceeded)
            {
                return await RetryOrFailAsync(
                    job,
                    extraction.Blockers.Count > 0 ? "private_extraction_blocked" : "private_extraction_failed",
                    "The private extractor did not return a usable citation-preserving document representation.",
                    extraction.ToPublicEvidence(),
                    cancellationToken);
            }

            var chunks = _extractor.CreateChunks(extraction, pipelineOptions);
            if (chunks.Count == 0)
            {
                await FailAsync(job, "no_retrievable_chunks", "No citation-preserving chunks were generated.", cancellationToken);
                return Result("failed", job, null, extraction.SectionCount, 0, 0, "no_retrievable_chunks", extraction.Warnings);
            }

            await _repository.MarkStageAsync(
                job,
                "embedding",
                "embedding",
                "private_extraction_completed",
                new
                {
                    extraction.PageCount,
                    extraction.SectionCount,
                    extraction.CharacterCount,
                    chunkCount = chunks.Count,
                    extraction.SourceSha256,
                    rawDocumentTextLogged = false
                },
                cancellationToken);
            if (await _repository.CancellationRequestedAsync(job.JobId, cancellationToken))
            {
                await _repository.CompleteTerminalAsync(
                    job,
                    "cancelled",
                    "cancelled",
                    "processing_cancelled_before_embedding",
                    "cancelled",
                    "cancellation_requested",
                    "Processing was cancelled before private embeddings or index persistence.",
                    new { rawDocumentTextLogged = false },
                    cancellationToken);
                return Result("cancelled", job, null, extraction.SectionCount, chunks.Count, 0, "cancellation_requested", extraction.Warnings);
            }

            var embeddings = options.EmbeddingConfigured
                ? await _embeddingClient.GenerateAsync(
                    chunks.Select(chunk => chunk.Text).ToArray(),
                    options,
                    cancellationToken)
                : new PulseAiPrivateEmbeddingResult(
                    "private_embeddings_not_configured",
                    string.Empty,
                    string.Empty,
                    0,
                    [],
                    "embedding_not_configured",
                    DateTimeOffset.UtcNow);
            var lexicalOnly = !embeddings.Succeeded;
            if (lexicalOnly && !options.AllowLexicalOnlyCompletion)
            {
                return await RetryOrFailAsync(
                    job,
                    embeddings.DiagnosticCode,
                    "Private embeddings were required but could not be generated.",
                    new
                    {
                        embeddings.Status,
                        embeddings.Provider,
                        embeddings.Model,
                        embeddings.DiagnosticCode,
                        inputTextLogged = false
                    },
                    cancellationToken);
            }

            await _repository.MarkStageAsync(
                job,
                "indexing",
                "indexing",
                embeddings.Succeeded ? "private_embeddings_completed" : "lexical_only_completion_selected",
                new
                {
                    embeddingCount = embeddings.Succeeded ? embeddings.Vectors.Count : 0,
                    embeddings.Dimension,
                    embeddings.Model,
                    lexicalOnly,
                    vectorReturned = false,
                    inputTextLogged = false
                },
                cancellationToken);
            var versionId = await _repository.PersistProcessedDocumentAsync(
                job,
                source,
                scan,
                extraction,
                chunks,
                embeddings,
                lexicalOnly,
                cancellationToken);
            return Result(
                lexicalOnly ? "completed_lexical_only" : "completed_private_hybrid_index",
                job,
                versionId,
                extraction.SectionCount,
                chunks.Count,
                embeddings.Succeeded ? embeddings.Vectors.Count : 0,
                string.Empty,
                extraction.Warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Pulse AI private runtime processing failed without logging private document content. JobId={JobId} Diagnostic={Diagnostic}",
                job.JobId,
                Diagnostic(exception));
            try
            {
                await RetryOrFailAsync(
                    job,
                    Diagnostic(exception),
                    "Private document processing failed without exposing source content.",
                    new { rawDocumentTextLogged = false },
                    cancellationToken);
            }
            catch (Exception completionException)
            {
                _logger.LogError(
                    completionException,
                    "Pulse AI could not persist the terminal processing state. JobId={JobId}",
                    job.JobId);
            }
            return Result("failed", job, null, 0, 0, 0, Diagnostic(exception), []);
        }
    }

    private async Task<PulseAiPrivateWorkerResult> RetryOrFailAsync(
        PulseAiPrivateProcessingJob job,
        string diagnosticCode,
        string message,
        object evidence,
        CancellationToken cancellationToken)
    {
        var retry = job.AttemptCount < job.MaximumAttempts;
        await _repository.CompleteTerminalAsync(
            job,
            retry ? "retry_wait" : "failed",
            retry ? "retry_wait" : "failed",
            retry ? "processing_retry_scheduled" : "processing_failed",
            retry ? "partial" : "failed",
            diagnosticCode,
            message,
            evidence,
            cancellationToken);
        return Result(
            retry ? "retry_scheduled" : "failed",
            job,
            null,
            0,
            0,
            0,
            diagnosticCode,
            []);
    }

    private async Task FailAsync(
        PulseAiPrivateProcessingJob job,
        string diagnosticCode,
        string message,
        CancellationToken cancellationToken)
    {
        await _repository.CompleteTerminalAsync(
            job,
            "failed",
            "failed",
            "processing_failed",
            "failed",
            diagnosticCode,
            message,
            new { rawDocumentTextLogged = false },
            cancellationToken);
    }

    private static int EstimateTokens(int characters) =>
        characters <= 0 ? 0 : (characters + 3) / 4;

    private static PulseAiPrivateWorkerResult Empty(
        string status,
        string diagnosticCode) =>
        new(
            Status: status,
            JobId: null,
            DocumentId: null,
            VersionId: null,
            SectionCount: 0,
            ChunkCount: 0,
            EmbeddedChunkCount: 0,
            DiagnosticCode: diagnosticCode,
            Warnings: [],
            CompletedAt: DateTimeOffset.UtcNow);

    private static PulseAiPrivateWorkerResult Result(
        string status,
        PulseAiPrivateProcessingJob job,
        Guid? versionId,
        int sectionCount,
        int chunkCount,
        int embeddedCount,
        string diagnosticCode,
        IReadOnlyList<string> warnings) =>
        new(
            Status: status,
            JobId: job.JobId,
            DocumentId: job.DocumentId,
            VersionId: versionId,
            SectionCount: sectionCount,
            ChunkCount: chunkCount,
            EmbeddedChunkCount: embeddedCount,
            DiagnosticCode: diagnosticCode,
            Warnings: warnings,
            CompletedAt: DateTimeOffset.UtcNow);

    private static string Diagnostic(Exception exception) => exception switch
    {
        TimeoutException => "timeout",
        UnauthorizedAccessException => "storage_access_denied",
        IOException => "storage_io_failure",
        Npgsql.NpgsqlException => "database_transport_failure",
        OperationCanceledException => "cancelled",
        _ => "private_document_runtime_failure"
    };
}
