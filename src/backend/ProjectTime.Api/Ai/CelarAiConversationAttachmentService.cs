namespace ProjectTime.Api.Ai;

public sealed class CelarAiConversationAttachmentService
{
    private readonly CelarAiConversationAttachmentRepository _attachments;
    private readonly PulseAiPrivateDocumentRuntimeRepository _runtimeRepository;
    private readonly ILogger<CelarAiConversationAttachmentService> _logger;

    public CelarAiConversationAttachmentService(
        CelarAiConversationAttachmentRepository attachments,
        PulseAiPrivateDocumentRuntimeRepository runtimeRepository,
        ILogger<CelarAiConversationAttachmentService> logger)
    {
        _attachments = attachments;
        _runtimeRepository = runtimeRepository;
        _logger = logger;
    }

    public async Task<object> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        var storage = ProjectPulseUploadStorage.InspectProductionReadiness();
        var runtime = PulseAiPrivateRuntimeOptions.FromEnvironment();
        var pipeline = PulseAiDocumentPipelineOptions.FromEnvironment();
        var schemaReady = await _attachments.IsSchemaReadyAsync(cancellationToken);
        var runtimeSchema = await _runtimeRepository.InspectRuntimeSchemaAsync(cancellationToken);
        var blockers = Blockers(storage, runtime, schemaReady, runtimeSchema.Complete);
        return new
        {
            status = blockers.Count == 0
                ? "celar_ai_chat_attachments_ready"
                : "celar_ai_chat_attachments_not_ready",
            contractVersion = CelarAiConversationAttachmentPolicy.ContractVersion,
            migration = CelarAiConversationAttachmentPolicy.MigrationId,
            schemaReady,
            privateRuntimeSchemaReady = runtimeSchema.Complete,
            privateWorkerEnabled = runtime.WorkerEnabled,
            persistentStorageReady = storage.ProductionReady,
            malwareScanningConfigured = runtime.ClamAvConfigured,
            genericPreScanAttestationAcceptedForDirectUploads = false,
            embeddingConfigured = runtime.EmbeddingConfigured,
            lexicalOnlyCompletionApproved = runtime.LexicalOnlyCompletionApproved,
            maximumFilesPerRequest = CelarAiConversationAttachmentPolicy.MaximumFilesPerRequest,
            maximumActiveFilesPerConversation = CelarAiConversationAttachmentPolicy.MaximumActiveFilesPerConversation,
            maximumActiveProcessingAttachmentsPerUser = CelarAiConversationAttachmentPolicy.MaximumActiveProcessingAttachmentsPerUser,
            maximumFileBytes = pipeline.MaximumFileBytes,
            maximumActiveBytesPerConversation = CelarAiConversationAttachmentPolicy.MaximumActiveBytesPerConversation,
            supportedExtensions = PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions,
            retentionDays = CelarAiConversationAttachmentPolicy.RetentionDays,
            retentionCleanupWorkerEnabled = true,
            blockers,
            rawDocumentsSentToClaudeOrOpenAi = false
        };
    }

    public async Task<CelarAiConversationAttachmentUploadResult> UploadAsync(
        Guid userId,
        Guid conversationId,
        IFormFileCollection files,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate)
        {
            return new(
                "release_candidate_read_only",
                [],
                ["Celar AI attachment upload is disabled on the exact-source release candidate."]);
        }
        var storage = ProjectPulseUploadStorage.InspectProductionReadiness();
        var runtime = PulseAiPrivateRuntimeOptions.FromEnvironment();
        var pipeline = PulseAiDocumentPipelineOptions.FromEnvironment();
        var schemaReady = await _attachments.IsSchemaReadyAsync(cancellationToken);
        var runtimeSchema = await _runtimeRepository.InspectRuntimeSchemaAsync(cancellationToken);
        var blockers = Blockers(storage, runtime, schemaReady, runtimeSchema.Complete);
        if (files.Count == 0) blockers.Add("Select at least one non-empty document.");
        if (files.Count > CelarAiConversationAttachmentPolicy.MaximumFilesPerRequest)
            blockers.Add($"A single upload may contain at most {CelarAiConversationAttachmentPolicy.MaximumFilesPerRequest} documents.");

        var usage = await _attachments.GetUsageAsync(conversationId, userId, cancellationToken);
        if (!usage.ConversationOwnedAndActive)
            blockers.Add("The Celar AI conversation was not found in the current user's active conversation scope.");
        if (usage.ActiveCount + files.Count > CelarAiConversationAttachmentPolicy.MaximumActiveFilesPerConversation)
            blockers.Add($"A conversation may retain at most {CelarAiConversationAttachmentPolicy.MaximumActiveFilesPerConversation} active attachments.");
        if (usage.ActiveProcessingCount + files.Count > CelarAiConversationAttachmentPolicy.MaximumActiveProcessingAttachmentsPerUser)
            blockers.Add($"A user may have at most {CelarAiConversationAttachmentPolicy.MaximumActiveProcessingAttachmentsPerUser} Celar AI attachments processing at one time.");

        long requestedBytes = 0;
        var prepared = new List<(IFormFile File, string Name, string Extension)>();
        foreach (var file in files)
        {
            var name = SafeFileName(file.FileName);
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (file.Length <= 0)
            {
                blockers.Add($"{name}: empty documents cannot be uploaded.");
                continue;
            }
            if (file.Length > pipeline.MaximumFileBytes)
            {
                blockers.Add($"{name}: the document exceeds the {pipeline.MaximumFileBytes} byte private-pipeline limit.");
                continue;
            }
            if (!PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                blockers.Add($"{name}: {extension} is not an approved Celar AI document format.");
                continue;
            }
            requestedBytes += file.Length;
            prepared.Add((file, name, extension));
        }
        if (requestedBytes > CelarAiConversationAttachmentPolicy.MaximumActiveBytesPerConversation
            || usage.ActiveBytes + requestedBytes > CelarAiConversationAttachmentPolicy.MaximumActiveBytesPerConversation)
        {
            blockers.Add($"The conversation attachment total may not exceed {CelarAiConversationAttachmentPolicy.MaximumActiveBytesPerConversation} bytes.");
        }
        if (blockers.Count > 0)
            return new("celar_ai_chat_attachment_upload_blocked", [], blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        var uploaded = new List<CelarAiConversationAttachment>();
        var root = ProjectPulseUploadStorage.ResolveRoot();
        foreach (var item in prepared)
        {
            var attachmentId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            var fileToken = Guid.NewGuid().ToString("N");
            var folder = Path.Combine(
                root,
                "celar-ai",
                "conversations",
                conversationId.ToString("N"),
                attachmentId.ToString("N"));
            var storedFileName = $"{fileToken}{item.Extension}";
            var finalPath = Path.Combine(folder, storedFileName);
            var stagingPath = Path.Combine(folder, $".{fileToken}.uploading");
            string? createdPath = null;
            CelarAiStoredConversationAttachment? stored = null;
            try
            {
                EnsureConfined(root, finalPath);
                Directory.CreateDirectory(folder);
                await using (var input = item.File.OpenReadStream())
                await using (var output = new FileStream(
                    stagingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await CopyBoundedAsync(input, output, pipeline.MaximumFileBytes, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }
                File.Move(stagingPath, finalPath);
                createdPath = finalPath;

                var correlationId = Guid.NewGuid().ToString("N");
                stored = await _attachments.CreateAsync(
                    attachmentId,
                    documentId,
                    conversationId,
                    userId,
                    item.Name,
                    storedFileName,
                    finalPath,
                    SafeContentType(item.File.ContentType),
                    item.File.Length,
                    correlationId,
                    cancellationToken);
                if (stored is null)
                    throw new InvalidOperationException("attachment_metadata_not_saved");

                var source = new PulseAiAuthorizedDocumentSource(
                    DocumentId: documentId,
                    ProjectId: null,
                    ProjectCode: string.Empty,
                    ProjectName: "Celar AI conversation attachment",
                    CustomerName: string.Empty,
                    DocumentType: "chat_attachment",
                    DocumentCategory: "chat_attachment",
                    OriginalFileName: item.Name,
                    StoredFileName: storedFileName,
                    StoragePath: finalPath,
                    ContentType: SafeContentType(item.File.ContentType),
                    SizeBytes: item.File.Length,
                    EngineeringVisible: false,
                    AiTimesheetContextEnabled: false,
                    ExtractionStatus: "not_started",
                    ExistingContextSummaryReady: false,
                    ContextLastProcessedAt: null,
                    UploadedAt: stored.Attachment.CreatedAt,
                    UploadSource: CelarAiConversationAttachmentPolicy.UploadSource,
                    AccessScope: "conversation_owner_only",
                    Classification: CelarAiConversationAttachmentPolicy.Classification,
                    RoleCodes: []);
                var job = await _runtimeRepository.EnqueueAsync(
                    userId,
                    userId,
                    source,
                    "celar_ai_chat_attachment",
                    priority: 70,
                    maximumAttempts: runtime.MaximumAttempts,
                    correlationId,
                    cancellationToken);
                if (job is null)
                {
                    await _attachments.MarkQueueFailureAsync(
                        documentId,
                        "attachment_processing_queue_unavailable",
                        cancellationToken);
                    await _attachments.RevokeAsync(
                        conversationId,
                        attachmentId,
                        userId,
                        "Attachment processing queue was unavailable.",
                        CancellationToken.None);
                    var deletionDiagnostic = TryDelete(finalPath, root);
                    await FinalizeDeletedAttachmentAsync(
                        attachmentId,
                        deletionDiagnostic,
                        CancellationToken.None);
                    blockers.Add($"{item.Name}: the private processing queue was not ready; the unscanned upload was removed.");
                }
                else
                {
                    uploaded.Add(stored.Attachment with
                    {
                        ProcessingStatus = "queued",
                        ProcessingJobId = job.JobId
                    });
                }
            }
            catch (Exception exception)
            {
                TryDelete(stagingPath, root);
                var deletionDiagnostic = createdPath is null
                    ? string.Empty
                    : TryDelete(createdPath, root);
                if (stored is not null)
                {
                    // Metadata commits before queue admission by design. If a
                    // later exception interrupts admission, fail closed and
                    // compensate even when the client cancelled the request.
                    await _attachments.MarkQueueFailureAsync(
                        documentId,
                        "attachment_processing_admission_interrupted",
                        CancellationToken.None);
                    await _attachments.RevokeAsync(
                        conversationId,
                        attachmentId,
                        userId,
                        "Attachment processing admission was interrupted.",
                        CancellationToken.None);
                    await FinalizeDeletedAttachmentAsync(
                        attachmentId,
                        deletionDiagnostic,
                        CancellationToken.None);
                }
                if (exception is OperationCanceledException) throw;
                _logger.LogWarning(
                    exception,
                    "Celar AI attachment upload failed without logging document content. ConversationId={ConversationId} Diagnostic={Diagnostic}",
                    conversationId,
                    Diagnostic(exception));
                blockers.Add($"{item.Name}: the private attachment could not be stored and queued ({Diagnostic(exception)}).");
            }
        }

        return new(
            blockers.Count == 0
                ? "celar_ai_chat_attachments_accepted"
                : uploaded.Count > 0
                    ? "celar_ai_chat_attachments_partially_accepted"
                    : "celar_ai_chat_attachment_upload_failed",
            uploaded,
            blockers);
    }

    public Task<IReadOnlyList<CelarAiConversationAttachment>> ListAsync(
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        _attachments.ListAsync(conversationId, userId, cancellationToken);

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid conversationId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate) return false;
        var storagePath = await _attachments.RevokeAsync(
            conversationId,
            attachmentId,
            userId,
            "Revoked by the conversation owner.",
            cancellationToken);
        if (storagePath is null) return false;
        var diagnostic = TryDelete(storagePath, ProjectPulseUploadStorage.ResolveRoot());
        await FinalizeDeletedAttachmentAsync(
            attachmentId,
            diagnostic,
            cancellationToken);
        return true;
    }

    public async Task<int> PurgeExpiredAsync(
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate) return 0;
        var storage = ProjectPulseUploadStorage.InspectProductionReadiness();
        if (!storage.ProductionReady) return 0;
        var root = ProjectPulseUploadStorage.ResolveRoot();
        var candidates = await _attachments.ClaimPurgeCandidatesAsync(
            CelarAiConversationAttachmentPolicy.RetentionBatchSize,
            cancellationToken);
        var purged = 0;
        foreach (var candidate in candidates)
        {
            var diagnostic = TryDelete(candidate.StoragePath, root);
            var succeeded = await FinalizeDeletedAttachmentAsync(
                candidate.AttachmentId,
                diagnostic,
                cancellationToken);
            if (succeeded) purged++;
        }
        await PurgeOrphanFilesAsync(root, cancellationToken);
        return purged;
    }

    private async Task<bool> FinalizeDeletedAttachmentAsync(
        Guid attachmentId,
        string deletionDiagnostic,
        CancellationToken cancellationToken)
    {
        if (deletionDiagnostic.Length > 0)
        {
            await _attachments.RecordStoragePurgeAsync(
                attachmentId,
                false,
                deletionDiagnostic,
                cancellationToken);
            return false;
        }
        var finalized = await _attachments.FinalizeStoragePurgeAsync(
            attachmentId,
            cancellationToken);
        if (!finalized)
        {
            await _attachments.RecordStoragePurgeAsync(
                attachmentId,
                false,
                "attachment_content_purge_not_finalized",
                cancellationToken);
        }
        return finalized;
    }

    private static List<string> Blockers(
        ProjectPulseUploadStorageReadiness storage,
        PulseAiPrivateRuntimeOptions runtime,
        bool attachmentSchemaReady,
        bool runtimeSchemaReady)
    {
        var blockers = new List<string>();
        if (!attachmentSchemaReady) blockers.Add("Migration 072 is not available.");
        if (!runtimeSchemaReady) blockers.Add("The private document processing schema is not ready.");
        if (!storage.ProductionReady) blockers.AddRange(storage.Blockers);
        if (!runtime.WorkerEnabled) blockers.Add("The private document processing worker is disabled.");
        if (!runtime.ClamAvConfigured)
            blockers.Add("A live private ClamAV scanner is required for direct Celar AI chat uploads; generic pre-scan configuration is not a per-file attestation.");
        if (!runtime.EmbeddingConfigured && !runtime.LexicalOnlyCompletionApproved)
            blockers.Add("A private embedding endpoint or explicitly approved lexical-only mode is required.");
        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task CopyBoundedAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes)
                throw new InvalidDataException("attachment_size_limit_exceeded");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total == 0) throw new InvalidDataException("attachment_empty");
    }

    private static string SafeFileName(string? value)
    {
        var name = Path.GetFileName(value?.Trim() ?? string.Empty);
        name = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (name.Length == 0) return "document.txt";
        if (name.Length <= 240) return name;
        var extension = Path.GetExtension(name);
        var stemLength = Math.Max(1, 240 - extension.Length);
        return name[..Math.Min(stemLength, name.Length)] + extension;
    }

    private static string SafeContentType(string? value)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0 || clean.Length > 160 || clean.Any(char.IsControl))
            return "application/octet-stream";
        return clean;
    }

    private static void EnsureConfined(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("attachment_storage_path_rejected");
    }

    private static string TryDelete(string path, string root)
    {
        try
        {
            EnsureConfined(root, path);
            if (File.Exists(path)) File.Delete(path);
            return string.Empty;
        }
        catch (Exception exception)
        {
            // Retrieval was already revoked. The retention worker retries
            // physical cleanup without restoring document access.
            return Diagnostic(exception);
        }
    }

    private async Task PurgeOrphanFilesAsync(
        string root,
        CancellationToken cancellationToken)
    {
        try
        {
            var attachmentRoot = Path.Combine(root, "celar-ai", "conversations");
            if (!Directory.Exists(attachmentRoot)) return;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var cutoff = DateTime.UtcNow.AddHours(-24);
            var deleted = 0;
            var tracked = await _attachments.LoadTrackedStoragePathsAsync(cancellationToken);
            if (tracked is null) return;
            foreach (var path in Directory.EnumerateFiles(attachmentRoot, "*", options))
            {
                EnsureConfined(attachmentRoot, path);
                if (File.GetLastWriteTimeUtc(path) > cutoff) continue;
                var isStaging = Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal)
                    && path.EndsWith(".uploading", StringComparison.OrdinalIgnoreCase);
                if (!isStaging && tracked.Contains(Path.GetFullPath(path))) continue;
                File.Delete(path);
                deleted++;
            }
            if (deleted > 0)
            {
                _logger.LogInformation(
                    "Celar AI attachment retention removed {Count} abandoned staging or untracked final files without reading their contents.",
                    deleted);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Celar AI attachment staging cleanup requires operator follow-up. Diagnostic={Diagnostic}",
                Diagnostic(exception));
        }
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        InvalidDataException data => data.Message,
        IOException => "attachment_storage_failure",
        UnauthorizedAccessException => "attachment_storage_access_denied",
        InvalidOperationException invalid => invalid.Message,
        _ => "celar_ai_attachment_upload_failure"
    };
}
