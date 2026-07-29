using System.Security.Cryptography;
using System.Text;

namespace ProjectTime.Api.Ai;

public static class PulseAiPrivateDocumentRuntimePolicy
{
    public const string ContractVersion = "pulse-ai-private-document-runtime-v1-20260729";
    public const string MigrationId = "052_pulse_ai_private_document_runtime";
    public const string ModuleNumber = "011";
    public const string PrivacyBoundary = "private_pulse_runtime_only";
    public const string ArtifactEncryption = "AES-256-GCM";
    public const string AuthorizationPolicyVersion = "pulse-ai-document-scope-v2";

    public static readonly HashSet<string> FullControlRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "SYSTEM_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR"
    };

    public static readonly HashSet<string> ProcessingRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "SYSTEM_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    };

    public static readonly HashSet<string> ApprovalRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "SYSTEM_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    };

    public static readonly string[] ProcessingStages =
    [
        "queued",
        "scanning",
        "extracting",
        "ocr",
        "chunking",
        "embedding",
        "indexing",
        "review_required",
        "completed"
    ];

    public static readonly string[] TerminalStates =
    [
        "review_required",
        "completed",
        "blocked",
        "failed",
        "cancelled",
        "revoked"
    ];
}

public sealed record PulseAiPrivateDocumentRuntimeOptions(
    bool WorkerEnabled,
    int PollSeconds,
    int LeaseSeconds,
    int BatchSize,
    int MaximumAttempts,
    string WorkerIdentity,
    string UploadRoot,
    string ArtifactRoot,
    string? ArtifactEncryptionKey,
    string ArtifactEncryptionKeyVersion,
    int ArtifactRetentionDays,
    string MalwareScannerMode,
    string? ClamAvHost,
    int ClamAvPort,
    int ClamAvTimeoutSeconds,
    string? OcrEndpoint,
    string? OcrApiKey,
    int OcrTimeoutSeconds,
    string? EmbeddingEndpoint,
    string? EmbeddingApiKey,
    string? EmbeddingModel,
    int EmbeddingBatchSize,
    int EmbeddingTimeoutSeconds,
    string? IndexEndpoint,
    string? IndexApiKey,
    string? IndexName,
    int IndexTimeoutSeconds,
    IReadOnlySet<string> PrivateServiceHostAllowlist)
{
    public bool DatabaseConfigured => RequiredDatabaseVariables.All(HasValue);
    public bool ArtifactEncryptionConfigured => TryEncryptionKey(out _);
    public bool MalwareScannerConfigured =>
        string.Equals(MalwareScannerMode, "clamav", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ClamAvHost)
        && ClamAvPort is > 0 and <= 65535;
    public bool OcrConfigured => PrivateEndpointAllowed(OcrEndpoint) && !string.IsNullOrWhiteSpace(OcrEndpoint);
    public bool EmbeddingConfigured =>
        PrivateEndpointAllowed(EmbeddingEndpoint)
        && !string.IsNullOrWhiteSpace(EmbeddingEndpoint)
        && !string.IsNullOrWhiteSpace(EmbeddingModel);
    public bool IndexConfigured =>
        PrivateEndpointAllowed(IndexEndpoint)
        && !string.IsNullOrWhiteSpace(IndexEndpoint)
        && !string.IsNullOrWhiteSpace(IndexName);
    public bool RuntimeReady =>
        DatabaseConfigured
        && WorkerEnabled
        && ArtifactEncryptionConfigured
        && MalwareScannerConfigured
        && EmbeddingConfigured
        && IndexConfigured;

    public static readonly string[] RequiredDatabaseVariables =
        ["PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD"];

    public static PulseAiPrivateDocumentRuntimeOptions FromEnvironment()
    {
        var uploadRoot = Value("PROJECTPULSE_UPLOAD_ROOT", "/opt/project-time-platform/app/uploads");
        var artifactRoot = Value(
            "PROJECTPULSE_PULSE_AI_ARTIFACT_ROOT",
            "/opt/project-time-platform/private/pulse-ai/artifacts");
        var allowlist = Csv("PROJECTPULSE_PRIVATE_SERVICE_HOST_ALLOWLIST")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new PulseAiPrivateDocumentRuntimeOptions(
            WorkerEnabled: Boolean("PROJECTPULSE_PULSE_AI_DOCUMENT_WORKER_ENABLED", false),
            PollSeconds: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_WORKER_POLL_SECONDS", 15, 2, 300),
            LeaseSeconds: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_WORKER_LEASE_SECONDS", 300, 30, 3600),
            BatchSize: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_WORKER_BATCH_SIZE", 2, 1, 20),
            MaximumAttempts: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_ATTEMPTS", 3, 1, 20),
            WorkerIdentity: Value("PROJECTPULSE_PULSE_AI_DOCUMENT_WORKER_ID", $"pulse-ai-{Environment.MachineName}"),
            UploadRoot: Path.GetFullPath(uploadRoot),
            ArtifactRoot: Path.GetFullPath(artifactRoot),
            ArtifactEncryptionKey: Optional("PROJECTPULSE_PULSE_AI_ARTIFACT_KEY"),
            ArtifactEncryptionKeyVersion: Value("PROJECTPULSE_PULSE_AI_ARTIFACT_KEY_VERSION", "v1"),
            ArtifactRetentionDays: Integer("PROJECTPULSE_PULSE_AI_ARTIFACT_RETENTION_DAYS", 365, 1, 3650),
            MalwareScannerMode: Value("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE", "not_configured").ToLowerInvariant(),
            ClamAvHost: Optional("PROJECTPULSE_PULSE_AI_CLAMAV_HOST"),
            ClamAvPort: Integer("PROJECTPULSE_PULSE_AI_CLAMAV_PORT", 3310, 1, 65535),
            ClamAvTimeoutSeconds: Integer("PROJECTPULSE_PULSE_AI_CLAMAV_TIMEOUT_SECONDS", 60, 5, 300),
            OcrEndpoint: Optional("PROJECTPULSE_PRIVATE_OCR_ENDPOINT"),
            OcrApiKey: Optional("PROJECTPULSE_PRIVATE_OCR_API_KEY"),
            OcrTimeoutSeconds: Integer("PROJECTPULSE_PRIVATE_OCR_TIMEOUT_SECONDS", 120, 10, 600),
            EmbeddingEndpoint: Optional("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT"),
            EmbeddingApiKey: Optional("PROJECTPULSE_PRIVATE_EMBEDDING_API_KEY"),
            EmbeddingModel: Optional("PROJECTPULSE_PRIVATE_EMBEDDING_MODEL"),
            EmbeddingBatchSize: Integer("PROJECTPULSE_PRIVATE_EMBEDDING_BATCH_SIZE", 32, 1, 256),
            EmbeddingTimeoutSeconds: Integer("PROJECTPULSE_PRIVATE_EMBEDDING_TIMEOUT_SECONDS", 90, 10, 600),
            IndexEndpoint: Optional("PROJECTPULSE_PRIVATE_VECTOR_INDEX_ENDPOINT"),
            IndexApiKey: Optional("PROJECTPULSE_PRIVATE_VECTOR_INDEX_API_KEY"),
            IndexName: Optional("PROJECTPULSE_PRIVATE_VECTOR_INDEX"),
            IndexTimeoutSeconds: Integer("PROJECTPULSE_PRIVATE_VECTOR_INDEX_TIMEOUT_SECONDS", 90, 10, 600),
            PrivateServiceHostAllowlist: allowlist);
    }

    public bool TryEncryptionKey(out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(ArtifactEncryptionKey)) return false;

        try
        {
            var candidate = Convert.FromBase64String(ArtifactEncryptionKey.Trim());
            if (candidate.Length != 32) return false;
            key = candidate;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public bool PrivateEndpointAllowed(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("https" or "http")) return false;
        if (uri.Scheme == "http" && !IsLoopbackOrPrivateHost(uri.Host)) return false;

        if (PrivateServiceHostAllowlist.Count == 0)
        {
            return IsLoopbackOrPrivateHost(uri.Host);
        }

        return PrivateServiceHostAllowlist.Any(entry => HostMatches(uri.Host, entry));
    }

    public object ToSanitizedEvidence() => new
    {
        workerEnabled = WorkerEnabled,
        pollSeconds = PollSeconds,
        leaseSeconds = LeaseSeconds,
        batchSize = BatchSize,
        maximumAttempts = MaximumAttempts,
        workerIdentity = WorkerIdentity,
        uploadRootConfigured = !string.IsNullOrWhiteSpace(UploadRoot),
        artifactRootConfigured = !string.IsNullOrWhiteSpace(ArtifactRoot),
        artifactEncryptionConfigured = ArtifactEncryptionConfigured,
        artifactEncryptionKeyVersion = ArtifactEncryptionKeyVersion,
        artifactRetentionDays = ArtifactRetentionDays,
        malwareScannerMode = MalwareScannerMode,
        malwareScannerConfigured = MalwareScannerConfigured,
        ocrConfigured = OcrConfigured,
        embeddingConfigured = EmbeddingConfigured,
        embeddingModel = EmbeddingModel,
        indexConfigured = IndexConfigured,
        indexName = IndexName,
        databaseConfigured = DatabaseConfigured,
        runtimeReady = RuntimeReady,
        secretValuesReturned = false,
        endpointValuesReturned = false
    };

    private static bool HostMatches(string host, string allowed)
    {
        var clean = allowed.Trim().TrimStart('.');
        if (clean.Length == 0) return false;
        return host.Equals(clean, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith('.' + clean, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopbackOrPrivateHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!System.Net.IPAddress.TryParse(host, out var address)) return false;
        if (System.Net.IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (
            bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168));
    }

    private static bool HasValue(string name) => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
    private static string Value(string name, string fallback) => Optional(name) ?? fallback;
    private static string? Optional(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    private static int Integer(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
    private static IReadOnlyList<string> Csv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}

public sealed record PulseAiPrivateRuntimeReadiness(
    string Status,
    string ContractVersion,
    bool MigrationApplied,
    bool WorkerEnabled,
    bool ArtifactEncryptionConfigured,
    bool MalwareScannerConfigured,
    bool OcrConfigured,
    bool EmbeddingConfigured,
    bool IndexConfigured,
    long QueuedJobs,
    long ActiveJobs,
    long CompletedJobs,
    long BlockedJobs,
    long FailedJobs,
    long IndexedChunks,
    long RevokedChunks,
    IReadOnlyList<string> ReadyCapabilities,
    IReadOnlyList<string> Blockers,
    DateTimeOffset GeneratedAt,
    string? DiagnosticCode = null);

public sealed record PulseAiPrivateDocumentQueueRequest(
    string? Reason,
    int? Priority,
    bool ForceReprocess = false);

public sealed record PulseAiPrivateDocumentRetryRequest(
    string? Reason);

public sealed record PulseAiPrivateDocumentApprovalRequest(
    string? VersionLabel,
    DateTimeOffset? EffectiveAt,
    bool CanonicalForCategory,
    string? ApprovalNote);

public sealed record PulseAiPrivateDocumentRevocationRequest(
    string? Reason,
    bool DeleteIndexEntries = true);

public sealed record PulseAiPrivateDocumentJobSummary(
    Guid JobId,
    Guid VersionId,
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string OriginalFileName,
    string DocumentCategory,
    string VersionLabel,
    string JobState,
    string CurrentStage,
    int AttemptCount,
    int MaximumAttempts,
    int Priority,
    int SectionCount,
    int ChunkCount,
    int IndexedChunkCount,
    string DiagnosticCode,
    string DiagnosticMessage,
    string CorrelationId,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset UpdatedAt)
{
    public object ToPublicEvidence() => new
    {
        jobId = JobId,
        versionId = VersionId,
        documentId = DocumentId,
        projectId = ProjectId,
        projectCode = ProjectCode,
        projectName = ProjectName,
        originalFileName = OriginalFileName,
        documentCategory = DocumentCategory,
        versionLabel = VersionLabel,
        jobState = JobState,
        currentStage = CurrentStage,
        attemptCount = AttemptCount,
        maximumAttempts = MaximumAttempts,
        priority = Priority,
        sectionCount = SectionCount,
        chunkCount = ChunkCount,
        indexedChunkCount = IndexedChunkCount,
        diagnosticCode = DiagnosticCode,
        diagnosticMessage = DiagnosticMessage,
        correlationId = CorrelationId,
        queuedAt = QueuedAt,
        startedAt = StartedAt,
        completedAt = CompletedAt,
        updatedAt = UpdatedAt
    };
}

public sealed record PulseAiPrivateDocumentVersionSummary(
    Guid VersionId,
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string OriginalFileName,
    string DocumentCategory,
    string Classification,
    string VersionLabel,
    string VersionState,
    bool CanonicalForCategory,
    string SourceSha256,
    DateTimeOffset? EffectiveAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? SupersededAt,
    DateTimeOffset? RevokedAt,
    string RevocationReason,
    string ContextSummaryStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PulseAiPrivateDocumentScanResult(
    string Status,
    string ScannerCode,
    string ScannerVersion,
    string SignatureVersion,
    string SourceSha256,
    string ThreatName,
    string DiagnosticCode,
    string DiagnosticMessage,
    string CorrelationId,
    DateTimeOffset ScannedAt)
{
    public bool Clean => Status == "clean";
}

public sealed record PulseAiPrivateOcrPage(
    int PageNumber,
    string Text,
    string TextSha256,
    decimal Confidence);

public sealed record PulseAiPrivateOcrResult(
    string Status,
    string Engine,
    string EngineVersion,
    IReadOnlyList<PulseAiPrivateOcrPage> Pages,
    string DiagnosticCode,
    string DiagnosticMessage,
    string RequestId,
    DateTimeOffset GeneratedAt)
{
    public bool Success => Status == "success" && Pages.Count > 0;
}

public sealed record PulseAiPrivateEmbeddingItem(
    string ChunkId,
    IReadOnlyList<float> Vector);

public sealed record PulseAiPrivateEmbeddingBatchResult(
    string Status,
    string Model,
    int Dimension,
    IReadOnlyList<PulseAiPrivateEmbeddingItem> Items,
    string DiagnosticCode,
    string DiagnosticMessage,
    string RequestId,
    DateTimeOffset GeneratedAt)
{
    public bool Success => Status == "success" && Items.Count > 0;
}

public sealed record PulseAiPrivateIndexDocument(
    string ChunkId,
    Guid DocumentId,
    Guid VersionId,
    Guid? ProjectId,
    string ProjectCode,
    string CustomerScope,
    string DocumentCategory,
    string DocumentVersion,
    string Classification,
    bool EngineeringVisible,
    bool AiTimesheetContextEnabled,
    string AccessScope,
    string CitationAnchor,
    int? PageNumber,
    string? SheetName,
    string SourceSha256,
    string TextSha256,
    string Text,
    IReadOnlyList<float> Vector,
    IReadOnlyDictionary<string, object?> SecurityMetadata);

public sealed record PulseAiPrivateIndexWriteReceipt(
    string ChunkId,
    string Status,
    string ExternalKey,
    string IndexVersion,
    string DiagnosticCode,
    string DiagnosticMessage);

public sealed record PulseAiPrivateIndexBatchResult(
    string Status,
    string Provider,
    string IndexName,
    IReadOnlyList<PulseAiPrivateIndexWriteReceipt> Receipts,
    string DiagnosticCode,
    string DiagnosticMessage,
    string RequestId,
    DateTimeOffset GeneratedAt)
{
    public bool Success => Status == "success" && Receipts.All(receipt => receipt.Status == "indexed");
}

public sealed record PulseAiPrivateArtifactReceipt(
    string ArtifactKind,
    string StorageUri,
    string ArtifactSha256,
    string EncryptionAlgorithm,
    string EncryptionKeyVersion,
    long ContentLengthBytes,
    DateTimeOffset RetentionUntil,
    DateTimeOffset CreatedAt);

internal sealed record PulseAiPrivateRuntimeAccessContext(
    Guid UserId,
    IReadOnlySet<string> RoleCodes,
    bool IsActive,
    bool IsBroadDocumentScope,
    bool IsProjectManager,
    string ScopeLabel)
{
    public bool CanProcess => RoleCodes.Overlaps(PulseAiPrivateDocumentRuntimePolicy.ProcessingRoles);
    public bool CanApprove => RoleCodes.Overlaps(PulseAiPrivateDocumentRuntimePolicy.ApprovalRoles);
    public bool CanRevoke => RoleCodes.Overlaps(PulseAiPrivateDocumentRuntimePolicy.FullControlRoles);
}

internal sealed record PulseAiPrivateRuntimeDocumentSource(
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    string DocumentType,
    string DocumentCategory,
    string OriginalFileName,
    string StoredFileName,
    string StoragePath,
    string? ContentType,
    long SizeBytes,
    bool EngineeringVisible,
    bool AiTimesheetContextEnabled,
    string ExtractionStatus,
    DateTimeOffset UploadedAt,
    string UploadSource,
    string Classification,
    string AccessScope,
    IReadOnlySet<string> RoleCodes);

internal static class PulseAiPrivateRuntimeHash
{
    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
