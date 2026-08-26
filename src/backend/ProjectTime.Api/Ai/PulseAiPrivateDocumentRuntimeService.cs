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
        var servicePrincipal = await _repository.InspectDocumentServicePrincipalAsync(
            options.DocumentServicePrincipalUserId,
            cancellationToken);
        var storage = ProjectPulseUploadStorage.InspectProductionReadiness();
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
        if (!schema.RagMigrationApplied)
            blockers.Add("Migration 053 has not been applied.");
        if (!schema.RoutingMigrationApplied)
            blockers.Add("Migration 061 has not been applied.");
        if (!schema.HardeningMigrationApplied)
            blockers.Add("Migration 071 has not been applied.");
        if (!schema.Complete)
            blockers.Add("One or more private document runtime tables are unavailable.");
        blockers.AddRange(storage.Blockers);
        if (!options.WorkerEnabled)
            blockers.Add("The private document processing worker is disabled.");
        if (!options.AutoQueueEligibleDocuments)
            blockers.Add("Automatic admission of authorized AI-eligible project documents is disabled.");
        if (options.AutoQueueEligibleDocuments && options.DocumentServicePrincipalUserId is null)
            blockers.Add("Automatic document admission requires a dedicated Celar AI document service-principal user ID; a human identity is never substituted.");
        if (options.AutoQueueEligibleDocuments
            && servicePrincipal.DiagnosticCode == "service_principal_user_not_found")
            blockers.Add("The configured Celar AI document service principal does not match an application user.");
        if (options.AutoQueueEligibleDocuments
            && servicePrincipal.DiagnosticCode is "database_unavailable" or "service_principal_lookup_unavailable")
            blockers.Add("The configured Celar AI document service principal could not be revalidated against the application permission store.");
        if (options.AutoQueueEligibleDocuments && servicePrincipal.Exists && !servicePrincipal.Active)
            blockers.Add("The configured Celar AI document service principal is inactive.");
        if (options.AutoQueueEligibleDocuments && servicePrincipal.Exists && servicePrincipal.Active && !servicePrincipal.QueuePermissionGranted)
            blockers.Add("The configured Celar AI document service principal does not have QUEUE_PULSE_AI_DOCUMENT_PROCESSING through an active role assignment.");
        if (!options.MalwareScannerConfigured)
            blockers.Add("A private ClamAV endpoint, authenticated Test-only HTTPS scanner, or explicitly approved pre-scan attestation is required.");
        if (!options.EmbeddingConfigured && !options.LexicalOnlyCompletionApproved)
            blockers.Add("A private embedding endpoint is required unless lexical-only completion has an explicit approval reference.");

        var scannerReason = options.MalwareScannerConfigured ? "scanner_not_checked" : "scanner_not_configured";
        var scannerApproved = options.PreScanAttestationConfigured;
        if (options.HttpsMalwareScanConfigured)
        {
            var scannerResolution = await PulseAiPrivateEndpointPolicy.VerifyResolvedPrivateEndpointAsync(
                options.MalwareScanEndpoint,
                options.PrivateHostAllowlist,
                requireHttps: true,
                allowLoopback: false,
                cancellationToken: cancellationToken);
            scannerApproved = scannerResolution.Approved;
            scannerReason = scannerResolution.Reason;
        }
        else if (options.ClamAvConfigured)
        {
            var scannerResolution = await PulseAiPrivateEndpointPolicy.VerifyPrivateHostAsync(
                options.MalwareScannerHost,
                allowLoopback: false,
                cancellationToken);
            scannerApproved = scannerResolution.Approved;
            scannerReason = scannerResolution.Reason;
        }

        var ocrResolution = options.OcrConfigured
            ? await PulseAiPrivateEndpointPolicy.VerifyResolvedPrivateEndpointAsync(
                options.OcrEndpoint,
                options.PrivateHostAllowlist,
                requireHttps: true,
                allowLoopback: false,
                cancellationToken: cancellationToken)
            : new PulseAiPrivateEndpointPolicy.ResolutionResult(false, null, "not_configured", 0);
        var ocrPrivate = ocrResolution.Approved;
        var ocrReason = ocrResolution.Reason;
        var embeddingResolution = options.EmbeddingConfigured
            ? await PulseAiPrivateEndpointPolicy.VerifyResolvedPrivateEndpointAsync(
                options.EmbeddingEndpoint,
                options.PrivateHostAllowlist,
                requireHttps: true,
                allowLoopback: false,
                cancellationToken: cancellationToken)
            : new PulseAiPrivateEndpointPolicy.ResolutionResult(false, null, "not_configured", 0);
        var embeddingPrivate = embeddingResolution.Approved;
        var embeddingReason = embeddingResolution.Reason;

        if ((options.ClamAvConfigured || options.HttpsMalwareScanConfigured) && !scannerApproved)
            blockers.Add($"The configured malware scanner destination was rejected by runtime endpoint policy ({scannerReason}).");
        if (counts.AwaitingOcr > 0 && !ocrPrivate)
            blockers.Add($"{counts.AwaitingOcr} document processing job(s) require an approved private OCR endpoint ({ocrReason}).");
        if (options.OcrConfigured && !ocrPrivate)
            blockers.Add($"The configured OCR endpoint was rejected by the private endpoint policy ({ocrReason}).");
        if (options.EmbeddingConfigured && !embeddingPrivate)
            blockers.Add($"The configured embedding endpoint was rejected by the private endpoint policy ({embeddingReason}).");
        if (schema.LexicalIndex) ready.Add("PostgreSQL full-text index is available");
        if (options.HttpsMalwareScanConfigured) ready.Add("authenticated Test-only HTTPS malware scanning gateway is configured");
        else if (options.ClamAvConfigured) ready.Add("private ClamAV scanning is configured");
        if (options.PreScanAttestationConfigured) ready.Add("approved pre-scan attestation mode is configured");
        if (scannerApproved) ready.Add("malware scanning destination or attestation passed runtime endpoint policy");
        if (ocrPrivate) ready.Add("private OCR endpoint passed endpoint policy");
        if (embeddingPrivate) ready.Add("private embedding endpoint passed endpoint policy");
        if (options.LexicalOnlyCompletionApproved) ready.Add("lexical-only completion has an explicit approval reference");
        if (storage.ProductionReady) ready.Add("shared writable persistent upload storage passed production policy");
        if (counts.ReadySowDocuments > 0) ready.Add("at least one AI-authorized SOW or GSD is processed and ready");
        else blockers.Add("No AI-authorized SOW or GSD is currently in the ready state.");

        var fullyReady = schema.Complete
            && schema.ProductionMigrationsApplied
            && options.WorkerEnabled
            && options.AutoQueueEligibleDocuments
            && servicePrincipal.Authorized
            && storage.ProductionReady
            && scannerApproved
            && (counts.AwaitingOcr == 0 || ocrPrivate)
            && (embeddingPrivate || options.LexicalOnlyCompletionApproved)
            && counts.ReadySowDocuments > 0;

        return new PulseAiPrivateDocumentRuntimeReadiness(
            Status: fullyReady
                ? "private_document_runtime_ready"
                : schema.Complete
                    ? "private_document_runtime_partially_ready"
                    : "private_document_runtime_schema_unavailable",
            ContractVersion: PulseAiPrivateRuntimePolicy.ContractVersion,
            MigrationApplied: schema.MigrationApplied,
            RagMigrationApplied: schema.RagMigrationApplied,
            RoutingMigrationApplied: schema.RoutingMigrationApplied,
            HardeningMigrationApplied: schema.HardeningMigrationApplied,
            ProductionMigrationsApplied: schema.ProductionMigrationsApplied,
            WorkerEnabled: options.WorkerEnabled,
            AutomaticDocumentQueueEnabled: options.AutoQueueEligibleDocuments,
            DocumentServicePrincipalConfigured: servicePrincipal.Configured,
            DocumentServicePrincipalActive: servicePrincipal.Active,
            DocumentServicePrincipalQueuePermissionGranted: servicePrincipal.QueuePermissionGranted,
            DocumentServicePrincipalAuthorized: servicePrincipal.Authorized,
            DocumentServicePrincipalDiagnosticCode: servicePrincipal.DiagnosticCode,
            ProcessingTablesAvailable: schema.Complete,
            UploadStorageProductionReady: storage.ProductionReady,
            UploadRootFingerprint: storage.RootFingerprint,
            ClamAvConfigured: options.ClamAvConfigured || options.HttpsMalwareScanConfigured,
            MalwareScannerEndpointPrivate: scannerApproved,
            PreScanAttestationConfigured: options.PreScanAttestationConfigured,
            OcrConfigured: options.OcrConfigured,
            OcrEndpointPrivate: ocrPrivate,
            EmbeddingConfigured: options.EmbeddingConfigured,
            EmbeddingEndpointPrivate: embeddingPrivate,
            LexicalOnlyCompletionApproved: options.LexicalOnlyCompletionApproved,
            LexicalIndexAvailable: schema.LexicalIndex,
            EmbeddingStorageAvailable: schema.Chunks,
            QueuedJobCount: counts.Queued,
            RunningJobCount: counts.Running,
            AwaitingOcrJobCount: counts.AwaitingOcr,
            FailedJobCount: counts.Failed,
            ReadyDocumentCount: counts.ReadyDocuments,
            ReadySowDocumentCount: counts.ReadySowDocuments,
            PendingSowDocumentCount: counts.PendingSowDocuments,
            ActiveChunkCount: counts.ActiveChunks,
            EmbeddedChunkCount: counts.EmbeddedChunks,
            ReadyCapabilities: ready.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Blockers: blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            MissingConfiguration: missing,
            GeneratedAt: DateTimeOffset.UtcNow,
            ActiveVersionCount: counts.ActiveVersions,
            UnembeddedChunkCount: Math.Max(0, counts.ActiveChunks - counts.EmbeddedChunks),
            LastIndexedAt: counts.LastIndexedAt);
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
        ProjectPulseAiReleaseRuntimePolicy.RejectCandidateDataMutation("Private document queue mutation");
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

    public async Task<bool> ApproveVersionAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        Guid documentId,
        Guid versionId,
        PulseAiApproveDocumentVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        ProjectPulseAiReleaseRuntimePolicy.RejectCandidateDataMutation("Private document version approval");
        if (actualUserId != effectiveUserId) return false;
        if (!string.Equals(
                request.Confirmation?.Trim(),
                PulseAiPrivateRuntimePolicy.ApproveVersionConfirmation,
                StringComparison.Ordinal))
        {
            return false;
        }
        var expectedSourceSha256 = request.ExpectedSourceSha256?.Trim().ToLowerInvariant() ?? string.Empty;
        if (expectedSourceSha256.Length != 64
            || expectedSourceSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }
        var access = await _repository.LoadAccessAsync(actualUserId, cancellationToken);
        return await _repository.ApproveActiveVersionAsync(
            access,
            documentId,
            versionId,
            actualUserId,
            expectedSourceSha256,
            request.Reason ?? "Approved for permission-scoped Celar AI retrieval.",
            cancellationToken);
    }

    public async Task<bool> CancelAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        Guid jobId,
        PulseAiCancelDocumentJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ProjectPulseAiReleaseRuntimePolicy.RejectCandidateDataMutation("Private document processing cancellation");
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
            request.Reason ?? "Cancellation requested by an authorized Celar AI operator.",
            cancellationToken);
    }

    public async Task<bool> RetryAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        Guid jobId,
        PulseAiRetryDocumentJobRequest request,
        CancellationToken cancellationToken = default)
    {
        ProjectPulseAiReleaseRuntimePolicy.RejectCandidateDataMutation("Private document processing retry");
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
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        if (release.IsCandidate)
        {
            if (!PulseAiProtectedTestCandidatePolicy.AllowsPrivateDocumentProcessing(release))
                return Empty("release_candidate_read_only", "release_candidate_read_only");
        }
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

        try
        {
            await PulseAiImmutableDocumentSnapshot.CleanupOrphansAsync(
                _pipeline.Options().UploadRoot,
                maximumDirectories: 32,
                _repository.HasLiveSnapshotLeaseAsync,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PulseAiDocumentSnapshotException)
        {
            return Empty(
                "document_snapshot_cleanup_unavailable",
                "document_snapshot_cleanup_unavailable");
        }

        if (options.AutoQueueEligibleDocuments)
            await _repository.EnqueueNextEligibleDocumentAsync(options, cancellationToken);

        var job = await _repository.ClaimNextAsync(options, cancellationToken);
        if (job is null) return Empty("queue_empty", string.Empty);

        var callerCancellationToken = cancellationToken;
        using var processingStop = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        cancellationToken = processingStop.Token;
        var heartbeatTask = MaintainLeaseAsync(job, options, processingStop, heartbeatStop.Token);

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

            var pipelineOptions = _pipeline.Options() with
            {
                ExtractionPreviewEnabled = true,
                MalwareScanAttested = true
            };
            PulseAiImmutableDocumentSnapshot snapshot;
            if (job.LeaseToken is null)
            {
                return await RetryOrFailAsync(
                    job,
                    "document_snapshot_lease_missing",
                    "The immutable processing snapshot could not be bound to an active lease.",
                    new { immutableSnapshotEstablished = false, rawDocumentTextLogged = false },
                    cancellationToken);
            }
            try
            {
                snapshot = await PulseAiImmutableDocumentSnapshot.CreateAsync(
                    source,
                    pipelineOptions.UploadRoot,
                    job.JobId,
                    job.LeaseToken.Value,
                    job.LeaseGeneration,
                    pipelineOptions.MaximumFileBytes,
                    cancellationToken);
            }
            catch (PulseAiDocumentSnapshotException exception)
            {
                return await RetryOrFailAsync(
                    job,
                    exception.DiagnosticCode,
                    "A private immutable processing snapshot could not be established.",
                    new { immutableSnapshotEstablished = false, rawDocumentTextLogged = false },
                    cancellationToken);
            }
            await using var immutableSnapshot = snapshot;
            source = immutableSnapshot.Source;

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
                DeleteQuarantinedConversationAttachment(source);
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
            if (!IsSha256(scan.SourceSha256)
                || !scan.SourceSha256.Equals(
                    immutableSnapshot.SourceSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return await RetryOrFailAsync(
                    job,
                    "document_snapshot_integrity_changed",
                    "The immutable processing snapshot failed its malware-scan integrity check.",
                    new { immutableSnapshotIntegrityVerified = false, rawDocumentTextLogged = false },
                    cancellationToken);
            }

            await _repository.MarkStageAsync(
                job,
                "extracting",
                "extracting",
                "malware_scan_completed",
                scan.ToPublicEvidence(),
                options.LeaseSeconds,
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

            pipelineOptions = pipelineOptions with { MalwareScannerMode = scan.Scanner };
            var extraction = await _extractor.ExtractAsync(source, pipelineOptions, cancellationToken);
            if (!immutableSnapshot.SourceSha256.Equals(
                    extraction.SourceSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return await RetryOrFailAsync(
                    job,
                    "document_snapshot_integrity_changed",
                    "The immutable processing snapshot failed its extraction integrity check.",
                    new { immutableSnapshotIntegrityVerified = false, rawDocumentTextLogged = false },
                    cancellationToken);
            }
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
                    options.LeaseSeconds,
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

            if (!await SourceStillMatchesAsync(
                    source.StoragePath,
                    immutableSnapshot.SourceSha256,
                    cancellationToken))
            {
                return await RetryOrFailAsync(
                    job,
                    "document_snapshot_integrity_changed",
                    "The immutable processing snapshot failed its post-extraction integrity check.",
                    new { immutableSnapshotIntegrityVerified = false, rawDocumentTextLogged = false },
                    cancellationToken);
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
                options.LeaseSeconds,
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
            if (lexicalOnly && !options.LexicalOnlyCompletionApproved)
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
                options.LeaseSeconds,
                cancellationToken);
            if (!await SourceStillMatchesAsync(
                    source.StoragePath,
                    immutableSnapshot.SourceSha256,
                    cancellationToken))
            {
                return await RetryOrFailAsync(
                    job,
                    "document_snapshot_integrity_changed",
                    "The immutable processing snapshot failed its pre-persistence integrity check.",
                    new { immutableSnapshotIntegrityVerified = false, rawDocumentTextLogged = false },
                    cancellationToken);
            }
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
        catch (OperationCanceledException) when (callerCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (processingStop.IsCancellationRequested)
        {
            return Result("lease_lost", job, null, 0, 0, 0, "lease_fence_lost", []);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Celar AI private runtime processing failed without logging private document content. JobId={JobId} Diagnostic={Diagnostic}",
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
                    "Celar AI could not persist the terminal processing state. JobId={JobId}",
                    job.JobId);
            }
            return Result("failed", job, null, 0, 0, 0, Diagnostic(exception), []);
        }
        finally
        {
            heartbeatStop.Cancel();
            try { await heartbeatTask; }
            catch (OperationCanceledException) { }
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task<bool> SourceStillMatchesAsync(
        string path,
        string expectedSourceSha256,
        CancellationToken cancellationToken)
    {
        if (!IsSha256(expectedSourceSha256)) return false;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await System.Security.Cryptography.SHA256.HashDataAsync(
                stream,
                cancellationToken);
            var actualSourceSha256 = Convert.ToHexString(hash).ToLowerInvariant();
            return expectedSourceSha256.Equals(
                actualSourceSha256,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task MaintainLeaseAsync(
        PulseAiPrivateProcessingJob job,
        PulseAiPrivateRuntimeOptions options,
        CancellationTokenSource processingStop,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.LeaseSeconds / 3));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (!await _repository.RenewLeaseAsync(job, options.LeaseSeconds, cancellationToken))
            {
                _logger.LogWarning(
                    "Celar AI stopped renewing a stale or transferred document lease. JobId={JobId} LeaseGeneration={LeaseGeneration}",
                    job.JobId,
                    job.LeaseGeneration);
                processingStop.Cancel();
                return;
            }
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

    private void DeleteQuarantinedConversationAttachment(PulseAiAuthorizedDocumentSource source)
    {
        if (!string.Equals(
                source.UploadSource,
                CelarAiConversationAttachmentPolicy.UploadSource,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var root = Path.GetFullPath(Path.Combine(
                    ProjectPulseUploadStorage.ResolveRoot(),
                    "celar-ai",
                    "conversations"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(source.StoragePath);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!path.StartsWith(root, comparison))
                throw new InvalidOperationException("quarantine_storage_path_rejected");
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception)
        {
            // Database state already prevents parsing and retrieval. Emit only
            // a bounded diagnostic so operators can retry physical cleanup
            // without disclosing the document name, path, or content.
            _logger.LogWarning(
                "Celar AI quarantined attachment cleanup requires operator follow-up. DocumentId={DocumentId} Diagnostic={Diagnostic}",
                source.DocumentId,
                Diagnostic(exception));
        }
    }

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
