using System.Net;
using System.Net.Sockets;

namespace ProjectTime.Api.Ai;

public static class PulseAiPrivateRuntimePolicy
{
    public const string ContractVersion = "pulse-ai-private-runtime-v1-20260729";
    public const string MigrationId = "052_pulse_ai_private_document_runtime";
    public const string QueueConfirmation = "QUEUE-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING";
    public const string RetryConfirmation = "RETRY-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING";
    public const string CancelConfirmation = "CANCEL-PULSE-AI-PRIVATE-DOCUMENT-PROCESSING";
    public const string ApproveVersionConfirmation = "APPROVE-PULSE-AI-PRIVATE-DOCUMENT-VERSION";
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
    string LexicalOnlyApprovalReference,
    bool AutoQueueEligibleDocuments,
    Guid? DocumentServicePrincipalUserId,
    string WorkerIdentity,
    string MalwareScannerMode,
    string MalwareScannerHost,
    int MalwareScannerPort,
    int MalwareScannerTimeoutSeconds,
    string MalwareSignatureVersion,
    string PreScanAttestationApprovalReference,
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
        && attested
        && MalwareSignatureVersion.Length > 0
        && PreScanAttestationApprovalReference.Length > 0;

    public bool LexicalOnlyCompletionApproved =>
        AllowLexicalOnlyCompletion && LexicalOnlyApprovalReference.Length > 0;

    public bool AutomaticQueueConfigured =>
        AutoQueueEligibleDocuments && DocumentServicePrincipalUserId is not null;

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
            LexicalOnlyApprovalReference: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE"),
                240,
                string.Empty),
            AutoQueueEligibleDocuments: Boolean("PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS", false),
            DocumentServicePrincipalUserId: GuidValue("PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID"),
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
            MalwareSignatureVersion: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION"),
                160,
                string.Empty),
            PreScanAttestationApprovalReference: Clean(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE"),
                240,
                string.Empty),
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

    private static Guid? GuidValue(string name) =>
        Guid.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value != Guid.Empty
            ? value
            : null;

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
    public sealed record ResolutionResult(
        bool Approved,
        Uri? Endpoint,
        string Reason,
        int ResolvedAddressCount);

    public static bool IsApprovedPrivateEndpoint(
        string? value,
        IReadOnlyList<string> allowlist,
        out Uri? endpoint,
        out string reason)
    {
        endpoint = null;
        reason = "endpoint_missing";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return false;
        if (!string.IsNullOrWhiteSpace(parsed.UserInfo))
        {
            reason = "userinfo_not_allowed";
            return false;
        }
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

    /// <summary>
    /// Revalidates the endpoint immediately before a private request. Hostname
    /// allowlisting alone is not sufficient: every current DNS answer must be a
    /// private address so a public or DNS-rebound destination fails closed.
    /// </summary>
    public static async Task<ResolutionResult> VerifyResolvedPrivateEndpointAsync(
        string? value,
        IReadOnlyList<string> allowlist,
        bool requireHttps = true,
        bool allowLoopback = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsApprovedPrivateEndpoint(value, allowlist, out var endpoint, out var reason)
            || endpoint is null)
        {
            return new ResolutionResult(false, null, reason, 0);
        }

        if (requireHttps && !endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return new ResolutionResult(false, null, "https_required", 0);

        var host = endpoint.DnsSafeHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return allowLoopback
                ? new ResolutionResult(true, endpoint, "loopback_endpoint", 1)
                : new ResolutionResult(false, null, "loopback_not_allowed", 1);
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            if (IPAddress.IsLoopback(literal) && !allowLoopback)
                return new ResolutionResult(false, null, "loopback_not_allowed", 1);
            return IsPrivateAddress(literal)
                ? new ResolutionResult(true, endpoint, "private_ip_endpoint", 1)
                : new ResolutionResult(false, null, "public_ip_endpoint", 1);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            if (addresses.Length == 0)
                return new ResolutionResult(false, null, "private_dns_no_addresses", 0);
            if (!allowLoopback && addresses.Any(IPAddress.IsLoopback))
                return new ResolutionResult(false, null, "private_dns_resolved_loopback", addresses.Length);
            if (addresses.Any(address => !IsPrivateAddress(address)))
                return new ResolutionResult(false, null, "private_dns_resolved_public_address", addresses.Length);
            return new ResolutionResult(true, endpoint, "private_dns_resolution_verified", addresses.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException)
        {
            return new ResolutionResult(false, null, "private_dns_resolution_failed", 0);
        }
    }

    public static async Task<(bool Approved, string Reason, int ResolvedAddressCount)> VerifyPrivateHostAsync(
        string? host,
        bool allowLoopback = false,
        CancellationToken cancellationToken = default)
    {
        var clean = host?.Trim() ?? string.Empty;
        if (clean.Length == 0) return (false, "host_missing", 0);
        if (IPAddress.TryParse(clean, out var literal))
        {
            if (IPAddress.IsLoopback(literal) && !allowLoopback)
                return (false, "loopback_not_allowed", 1);
            return IsPrivateAddress(literal)
                ? (true, "private_ip_host", 1)
                : (false, "public_ip_host", 1);
        }
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(clean, cancellationToken);
            if (addresses.Length == 0) return (false, "private_dns_no_addresses", 0);
            if (!allowLoopback && addresses.Any(IPAddress.IsLoopback))
                return (false, "private_dns_resolved_loopback", addresses.Length);
            return addresses.All(IsPrivateAddress)
                ? (true, "private_dns_resolution_verified", addresses.Length)
                : (false, "private_dns_resolved_public_address", addresses.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SocketException)
        {
            return (false, "private_dns_resolution_failed", 0);
        }
    }

    public static bool IsValidAllowlistEntry(string? value)
    {
        var clean = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (clean.Length is 0 or > 253 || clean.Contains('/') || clean.Contains(':') || clean.Contains('*'))
            return false;
        var host = clean.TrimStart('.');
        return host.Length > 0 && Uri.CheckHostName(host) == UriHostNameType.Dns;
    }

    private static bool HostMatches(string host, string entry)
    {
        var clean = entry.Trim().ToLowerInvariant();
        if (clean.Length == 0) return false;
        if (clean.StartsWith('.')) return host.EndsWith(clean, StringComparison.OrdinalIgnoreCase);
        return host.Equals(clean, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true only for addresses that may be used by the private HTTP
    /// transport. Loopback, link-local, unspecified, multicast, and public
    /// addresses are intentionally excluded even when an endpoint hostname was
    /// already approved by the application-level allowlist.
    /// </summary>
    internal static bool IsConnectablePrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast)
        {
            return false;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6SiteLocal
                && (address.GetAddressBytes()[0] is 0xfc or 0xfd);
        }

        return false;
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address) || IsConnectablePrivateAddress(address);
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
    bool RagMigrationApplied,
    bool RoutingMigrationApplied,
    bool HardeningMigrationApplied,
    bool ProductionMigrationsApplied,
    bool WorkerEnabled,
    bool AutomaticDocumentQueueEnabled,
    bool DocumentServicePrincipalConfigured,
    bool DocumentServicePrincipalActive,
    bool DocumentServicePrincipalQueuePermissionGranted,
    bool DocumentServicePrincipalAuthorized,
    string DocumentServicePrincipalDiagnosticCode,
    bool ProcessingTablesAvailable,
    bool UploadStorageProductionReady,
    string UploadRootFingerprint,
    bool ClamAvConfigured,
    bool MalwareScannerEndpointPrivate,
    bool PreScanAttestationConfigured,
    bool OcrConfigured,
    bool OcrEndpointPrivate,
    bool EmbeddingConfigured,
    bool EmbeddingEndpointPrivate,
    bool LexicalOnlyCompletionApproved,
    bool LexicalIndexAvailable,
    bool EmbeddingStorageAvailable,
    long QueuedJobCount,
    long RunningJobCount,
    long AwaitingOcrJobCount,
    long FailedJobCount,
    long ReadyDocumentCount,
    long ReadySowDocumentCount,
    long PendingSowDocumentCount,
    long ActiveChunkCount,
    long EmbeddedChunkCount,
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

public sealed record PulseAiApproveDocumentVersionRequest(
    string? Reason,
    string? ExpectedSourceSha256,
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
    string LeaseOwner,
    Guid? LeaseToken,
    long LeaseGeneration,
    DateTimeOffset? LeaseExpiresAt,
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
        leaseOwned = LeaseToken.HasValue,
        leaseGeneration = LeaseGeneration,
        leaseExpiresAt = LeaseExpiresAt,
        leaseOwnerReturned = false,
        leaseTokenReturned = false,
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
    string ActiveVersionSourceSha256,
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
        activeVersionSourceSha256 = ActiveVersionSourceSha256,
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
