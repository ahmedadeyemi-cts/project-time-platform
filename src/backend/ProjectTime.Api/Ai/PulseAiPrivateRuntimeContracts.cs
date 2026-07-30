using System.Net;

namespace ProjectTime.Api.Ai;

public static class PulseAiPrivateRuntimePolicy
{
    public const string ContractVersion = "pulse-ai-private-runtime-v1-20260729";
    public const string MigrationId = "052_pulse_ai_private_document_runtime";
    public const string QueueConfirmation = "QUEUE-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING";
    public const string RetryConfirmation = "RETRY-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING";
    public const string CancelConfirmation = "CANCEL-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING";
    public const string PrivacyBoundary = "private_pulse_runtime_only";
    public const string IndexProvider = "projectpulse_postgresql_hybrid";

    public static readonly string[] OperatorPermissions =
    [
        "VIEW_PULSE_AI_DOCUMENT_RUNTIME",
        "QUEUE_PULSE_AI_DOCUMENT_PROCESSING",
        "CANCEL_PULSE_AI_DOCUMENT_PROCESSING",
        "RETRY_PULSE_AI_DOCUMENT_PROCESSING",
        "APPROVE_PULSE_AI_DOCUMENT_VERSION"
    ];

    public static readonly string[] PrivateHostSuffixDefaults =
    [
        ".internal",
        ".local",
        ".private",
        ".privatelink.azure.com",
        ".privatelink.openai.azure.com",
        ".inference.ml.azure.com",
        ".azureml.ms"
    ];
}

public sealed record PulseAiPrivateRuntimeOptions(
    bool WorkerEnabled,
    int PollSeconds,
    int LeaseSeconds,
    int MaximumAttempts,
    int EmbeddingBatchSize,
    bool AllowLexicalOnlyCompletion,
    string WorkerIdentity,
    string MalwareScannerMode,
    string MalwareScannerHost,
    int MalwareScannerPort,
    int MalwareScannerTimeoutSeconds,
    string OcrEndpoint,
    string OcrModel,
    string OcrBearerToken,
    string EmbeddingEndpoint,
    string EmbeddingModel,
    string EmbeddingBearerToken,
    IReadOnlyList<string> PrivateHostAllowlist)
{
    public bool ClamAvConfigured =>
        MalwareScannerMode.Equals("clamav_tcp", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(MalwareScannerHost)
        && MalwareScannerPort > 0;

    public bool PreScanAttestationConfigured =>
        MalwareScannerMode.Equals("pre_scanned_attestation", StringComparison.OrdinalIgnoreCase)
        && bool.TryParse(
            Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED"),
            out var attested)
        && attested;

    public bool OcrConfigured =>
        !string.IsNullOrWhiteSpace(OcrEndpoint)
        && !string.IsNullOrWhiteSpace(OcrModel);

    public bool EmbeddingConfigured =>
        !string.IsNullOrWhiteSpace(EmbeddingEndpoint)
        && !string.IsNullOrWhiteSpace(EmbeddingModel);

    public static PulseAiPrivateRuntimeOptions FromEnvironment()
    {
        var hostAllowlist = Split(
            Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST"),
            PulseAiPrivateRuntimePolicy.PrivateHostSuffixDefaults);

        return new PulseAiPrivateRuntimeOptions(
            WorkerEnabled: Boolean("PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED", false),
            PollSeconds: Integer("PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_POLL_SECONDS", 15, 2, 300),
            LeaseSeconds: Integer("PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_LEASE_SECONDS", 300, 30, 3600),
            MaximumAttempts: Integer("PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_MAX_ATTEMPTS", 3, 1, 20),
            EmbeddingBatchSize: Integer("PROJECTPULSE_PULSE_AI_PRIVATE_EMBEDDING_BATCH_SIZE", 24, 1, 128),
            AllowLexicalOnlyCompletion: Boolean("PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION", false),
            WorkerIdentity: Clean(
                Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION")
                ?? Environment.GetEnvironmentVariable("HOSTNAME"),
                180,
                $"pulse-ai-worker-{Environment.ProcessId}"),
            MalwareScannerMode: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE"),
                80,
                "not_configured"),
            MalwareScannerHost: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_CLAMAV_HOST"),
                240,
                string.Empty),
            MalwareScannerPort: Integer("PROJECTPULSE_PULSE_AI_CLAMAV_PORT", 3310, 1, 65535),
            MalwareScannerTimeoutSeconds: Integer("PROJECTPULSE_PULSE_AI_CLAMAV_TIMEOUT_SECONDS", 45, 5, 300),
            OcrEndpoint: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_OCR_ENDPOINT"),
                1000,
                string.Empty),
            OcrModel: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_OCR_MODEL"),
                240,
                string.Empty),
            OcrBearerToken: Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN")?.Trim() ?? string.Empty,
            EmbeddingEndpoint: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT"),
                1000,
                string.Empty),
            EmbeddingModel: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_EMBEDDING_MODEL"),
                240,
                string.Empty),
            EmbeddingBearerToken: Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN")?.Trim() ?? string.Empty,
            PrivateHostAllowlist: hostAllowlist);
    }

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static int Integer(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static string Clean(string? value, int maximumLength, string fallback)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) return fallback;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static IReadOnlyList<string> Split(string? value, IReadOnlyList<string> fallback)
    {
        var parts = (value ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? fallback : parts;
    }
}

public static class PulseAiPrivateEndpointPolicy
{
    public static bool IsApprovedPrivateEndpoint(
        string? value,
        IReadOnlyList<string> allowlist,
        out Uri? endpoint,
        out string reason)
    {
        endpoint = null;
        reason = "endpoint_missing";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme is not ("https" or "http"))
        {
            reason = "unsupported_scheme";
            return false;
        }

        var host = parsed.DnsSafeHost.ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1" or "::1")
        {
            endpoint = parsed;
            reason = "loopback_endpoint";
            return true;
        }

        if (IPAddress.TryParse(host, out var address) && IsPrivateAddress(address))
        {
            endpoint = parsed;
            reason = "private_ip_endpoint";
            return true;
        }

        if (allowlist.Any(entry => HostMatches(host, entry)))
        {
            endpoint = parsed;
            reason = "allowlisted_private_dns_endpoint";
            return true;
        }

        reason = "host_not_private_or_allowlisted";
        return false;
    }

    private static bool HostMatches(string host, string entry)
    {
        var clean = entry.Trim().ToLowerInvariant();
        if (clean.Length == 0) return false;
        if (clean.StartsWith('.')) return host.EndsWith(clean, StringComparison.OrdinalIgnoreCase);
        return host.Equals(clean, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith($".{clean}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.GetAddressBytes()[0] is 0xfc or 0xfd;
        }

        return false;
    }
}

public sealed record PulseAiPrivateMalwareScanResult(
    string Status,
    bool Clean,
    bool Infected,
    string Scanner,
    string SignatureVersion,
    string DiagnosticCode,
    string EvidenceSha256,
    DateTimeOffset ScannedAt)
{
    public object ToPublicEvidence() => new
    {
        status = Status,
        clean = Clean,
        infected = Infected,
        scanner = Scanner,
        signatureVersion = SignatureVersion,
        diagnosticCode = DiagnosticCode,
        evidenceSha256 = EvidenceSha256,
        scannedAt = ScannedAt,
        rawScannerResponseReturned = false
    };
}

public sealed record PulseAiPrivateOcrResult(
    string Status,
    string Provider,
    string Model,
    IReadOnlyList<PulseAiExtractedSection> Sections,
    int PageCount,
    int CharacterCount,
    IReadOnlyList<string> Warnings,
    string DiagnosticCode,
    DateTimeOffset CompletedAt)
{
    public bool Succeeded => Status == "private_ocr_completed" && Sections.Count > 0;
}

public sealed record PulseAiPrivateEmbeddingResult(
    string Status,
    string Provider,
    string Model,
    int Dimension,
    IReadOnlyList<double[]> Vectors,
    string DiagnosticCode,
    DateTimeOffset CompletedAt)
{
    public bool Succeeded =>
        Status == "private_embeddings_completed"
        && Dimension > 0
        && Vectors.Count > 0
        && Vectors.All(vector => vector.Length == Dimension);
}

public sealed record PulseAiPrivateDocumentRuntimeReadiness(
    string Status,
    string ContractVersion,
    bool MigrationApplied,
    bool WorkerEnabled,
    bool ProcessingTablesAvailable,
    bool ClamAvConfigured,
    bool PreScanAttestationConfigured,
    bool OcrConfigured,
    bool OcrEndpointPrivate,
    bool EmbeddingConfigured,
    bool EmbeddingEndpointPrivate,
    bool LexicalIndexAvailable,
    bool EmbeddingStorageAvailable,
    long QueuedJobCount,
    long RunningJobCount,
    long FailedJobCount,
    long ReadyDocumentCount,
    IReadOnlyList<string> ReadyCapabilities,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> MissingConfiguration,
    DateTimeOffset GeneratedAt,
    string? DiagnosticCode = null);

public sealed record PulseAiQueueDocumentRequest(
    string? Purpose,
    int? Priority,
    int? MaximumAttempts,
    string? Confirmation);

public sealed record PulseAiRetryDocumentJobRequest(
    string? Reason,
    string? Confirmation);

public sealed record PulseAiCancelDocumentJobRequest(
    string? Reason,
    string? Confirmation);

public sealed record PulseAiPrivateProcessingJob(
    Guid JobId,
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string OriginalFileName,
    string DocumentCategory,
    Guid? ActualUserId,
    Guid? EffectiveUserId,
    string RequestedPurpose,
    int Priority,
    string Status,
    int AttemptCount,
    int MaximumAttempts,
    bool CancellationRequested,
    string CorrelationId,
    string SourceSha256,
    string ExtractionMethod,
    string MalwareScanner,
    string OcrProvider,
    string EmbeddingModel,
    int? EmbeddingDimension,
    string IndexProvider,
    string DiagnosticCode,
    string DiagnosticMessage,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt)
{
    public object ToPublicEvidence() => new
    {
        jobId = JobId,
        documentId = DocumentId,
        projectId = ProjectId,
        projectCode = ProjectCode,
        projectName = ProjectName,
        originalFileName = OriginalFileName,
        documentCategory = DocumentCategory,
        actualUserId = ActualUserId,
        effectiveUserId = EffectiveUserId,
        requestedPurpose = RequestedPurpose,
        priority = Priority,
        status = Status,
        attemptCount = AttemptCount,
        maximumAttempts = MaximumAttempts,
        cancellationRequested = CancellationRequested,
        correlationId = CorrelationId,
        sourceSha256 = SourceSha256,
        extractionMethod = ExtractionMethod,
        malwareScanner = MalwareScanner,
        ocrProvider = OcrProvider,
        embeddingModel = EmbeddingModel,
        embeddingDimension = EmbeddingDimension,
        indexProvider = IndexProvider,
        diagnosticCode = DiagnosticCode,
        diagnosticMessage = DiagnosticMessage,
        requestedAt = RequestedAt,
        startedAt = StartedAt,
        completedAt = CompletedAt,
        updatedAt = UpdatedAt,
        storagePathReturned = false,
        rawDocumentTextReturned = false,
        chunkTextReturned = false,
        embeddingVectorReturned = false
    };
}

public sealed record PulseAiPrivateDocumentRuntimeState(
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string OriginalFileName,
    string DocumentCategory,
    string ProcessingStatus,
    string Classification,
    string Revision,
    DateTimeOffset? EffectiveAt,
    Guid? ActiveVersionId,
    string ErrorCode,
    DateTimeOffset? ProcessingUpdatedAt,
    int VersionCount,
    int ActiveChunkCount,
    int EmbeddingReadyChunkCount,
    DateTimeOffset? LastProcessedAt,
    IReadOnlyList<PulseAiPrivateProcessingJob> RecentJobs)
{
    public object ToPublicEvidence() => new
    {
        documentId = DocumentId,
        projectId = ProjectId,
        projectCode = ProjectCode,
        projectName = ProjectName,
        originalFileName = OriginalFileName,
        documentCategory = DocumentCategory,
        processingStatus = ProcessingStatus,
        classification = Classification,
        revision = Revision,
        effectiveAt = EffectiveAt,
        activeVersionId = ActiveVersionId,
        errorCode = ErrorCode,
        processingUpdatedAt = ProcessingUpdatedAt,
        versionCount = VersionCount,
        activeChunkCount = ActiveChunkCount,
        embeddingReadyChunkCount = EmbeddingReadyChunkCount,
        lastProcessedAt = LastProcessedAt,
        recentJobs = RecentJobs.Select(job => job.ToPublicEvidence()).ToArray(),
        rawDocumentTextReturned = false,
        chunkTextReturned = false,
        vectorsReturned = false
    };
}

public sealed record PulseAiPrivateWorkerResult(
    string Status,
    Guid? JobId,
    Guid? DocumentId,
    Guid? VersionId,
    int SectionCount,
    int ChunkCount,
    int EmbeddedChunkCount,
    string DiagnosticCode,
    IReadOnlyList<string> Warnings,
    DateTimeOffset CompletedAt);
