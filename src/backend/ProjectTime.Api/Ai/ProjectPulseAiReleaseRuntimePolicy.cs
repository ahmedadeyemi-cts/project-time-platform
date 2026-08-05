using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Binds a release-scoped AI runtime to immutable source, controller, and safe
/// configuration evidence. Candidate behavior is intrinsic to the phase; it is
/// not an operator-controlled read-only switch that can be accidentally unset.
/// </summary>
public static class ProjectPulseAiReleaseRuntimePolicy
{
    public const string PhaseVariable = "PROJECTPULSE_AI_RELEASE_PHASE";
    public const string SourceCommitVariable = "PROJECTPULSE_AI_RELEASE_SOURCE_COMMIT";
    public const string RunningSourceCommitVariable = "PROJECTPULSE_SOURCE_COMMIT";
    public const string ControlCommitVariable = "PROJECTPULSE_AI_RELEASE_CONTROL_COMMIT";
    public const string ConfigurationDigestVariable = "PROJECTPULSE_AI_RELEASE_CONFIG_SHA256";
    public const string RouteOrderVariable = "PROJECTPULSE_AI_RELEASE_ROUTE_ORDER";
    public const string EmbeddedSourceCommitMetadataKey = "ProjectPulseSourceRevision";

    private static readonly string[] SafeConfigurationVariables =
    [
        RouteOrderVariable,
        "PROJECTPULSE_ENVIRONMENT",
        "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID",
        "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_SECRET_REFERENCE",
        "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING_SECRET_REFERENCE",
        "PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED",
        "PROJECTPULSE_CELAR_AI_ENABLED",
        "PROJECTPULSE_CELAR_AI_TRAINING_ENABLED",
        "PROJECTPULSE_PRIVATE_INFERENCE_REQUIRED_FOR_DOCUMENTS",
        "PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT",
        "PROJECTPULSE_PRIVATE_INFERENCE_MODEL",
        "PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE",
        "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE",
        "PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST",
        "PROJECTPULSE_AI_CLAUDE_ENABLED",
        "PROJECTPULSE_CLAUDE_ENDPOINT",
        "PROJECTPULSE_CLAUDE_APPROVED_ORIGINS",
        "PROJECTPULSE_CLAUDE_MODEL",
        "PROJECTPULSE_CLAUDE_APPROVED_MODELS",
        "PROJECTPULSE_CLAUDE_API_VERSION",
        "PROJECTPULSE_CLAUDE_API_KEY_SECRET_REFERENCE",
        "PROJECTPULSE_AI_OPENAI_ENABLED",
        "PROJECTPULSE_OPENAI_ENDPOINT",
        "PROJECTPULSE_OPENAI_APPROVED_ORIGINS",
        "PROJECTPULSE_OPENAI_MODEL",
        "PROJECTPULSE_OPENAI_APPROVED_MODELS",
        "PROJECTPULSE_OPENAI_API_VERSION",
        "PROJECTPULSE_OPENAI_ORGANIZATION",
        "PROJECTPULSE_OPENAI_PROJECT",
        "PROJECTPULSE_OPENAI_API_KEY_SECRET_REFERENCE",
        "PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION",
        "PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS",
        "PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED",
        "PROJECTPULSE_AI_MODE",
        "PROJECTPULSE_AI_TIMEOUT_SECONDS",
        "PROJECTPULSE_AI_RETRY_COUNT",
        "PROJECTPULSE_AI_MAX_OUTPUT_TOKENS",
        "PROJECTPULSE_AI_HEALTH_INTERVAL_SECONDS",
        "PROJECTPULSE_AI_FAILURE_THRESHOLD",
        "PROJECTPULSE_AI_CIRCUIT_BREAK_SECONDS",
        "PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED",
        "PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS",
        "PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_POLL_SECONDS",
        "PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_LEASE_SECONDS",
        "PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_MAX_ATTEMPTS",
        "PROJECTPULSE_PULSE_AI_PRIVATE_EMBEDDING_BATCH_SIZE",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED",
        "PROJECTPULSE_PULSE_AI_CLAMAV_HOST",
        "PROJECTPULSE_PULSE_AI_CLAMAV_PORT",
        "PROJECTPULSE_PULSE_AI_CLAMAV_TIMEOUT_SECONDS",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE",
        "PROJECTPULSE_PRIVATE_OCR_ENDPOINT",
        "PROJECTPULSE_PRIVATE_OCR_MODEL",
        "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN_SECRET_REFERENCE",
        "PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT",
        "PROJECTPULSE_PRIVATE_EMBEDDING_MODEL",
        "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN_SECRET_REFERENCE",
        "PROJECTPULSE_PRIVATE_VECTOR_INDEX",
        "PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION",
        "PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_BYTES",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_PAGES",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_SECTIONS",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_CHUNKS",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_CHARACTERS",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_CHUNK_CHARACTERS",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_CHUNK_OVERLAP",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED",
        "PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL",
        "PROJECTPULSE_PULSE_AI_RAG_PERSIST_ANSWER_TEXT",
        "PROJECTPULSE_PULSE_AI_RAG_MAX_QUESTION_CHARACTERS",
        "PROJECTPULSE_PULSE_AI_RAG_MAX_CANDIDATES",
        "PROJECTPULSE_PULSE_AI_RAG_MAX_CHUNKS",
        "PROJECTPULSE_PULSE_AI_RAG_MAX_CONTEXT_CHARACTERS",
        "PROJECTPULSE_PULSE_AI_RAG_MAX_OUTPUT_TOKENS",
        "PROJECTPULSE_PULSE_AI_RAG_MAX_ANSWER_CHARACTERS",
        "PROJECTPULSE_PULSE_AI_RAG_LEXICAL_WEIGHT",
        "PROJECTPULSE_PULSE_AI_RAG_SEMANTIC_WEIGHT",
        "PROJECTPULSE_PULSE_AI_RAG_MIN_EVIDENCE_SCORE",
        "PROJECTPULSE_PULSE_AI_RAG_MIN_CONFIDENCE",
        "PROJECTPULSE_PULSE_AI_SYSTEM_PRIVATE_MODEL_SYNTHESIS_ENABLED",
        "PROJECTPULSE_PULSE_AI_SYSTEM_PERSIST_TOOL_BODIES",
        "PROJECTPULSE_PULSE_AI_SYSTEM_MAX_QUESTION_CHARACTERS",
        "PROJECTPULSE_PULSE_AI_SYSTEM_MAX_TOOLS",
        "PROJECTPULSE_PULSE_AI_SYSTEM_MAX_TOOL_CHARACTERS",
        "PROJECTPULSE_PULSE_AI_SYSTEM_MAX_API_RESULTS",
        "PROJECTPULSE_PULSE_AI_SYSTEM_MAX_ANSWER_CHARACTERS",
        "PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_TIMEOUT_SECONDS",
        "PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI",
        "PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST",
        "PROJECTPULSE_UPLOAD_ROOT",
        "PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT",
        "PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_FILE",
        "PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_SHA256",
        "PROJECTPULSE_AI_RELEASE_SOW_DOCUMENT_ID",
        "PROJECTPULSE_AI_RELEASE_SOW_VERSION_ID",
        "PROJECTPULSE_AI_RELEASE_SOW_PROJECT_ID",
        "PROJECTPULSE_AI_RELEASE_SOW_SOURCE_SHA256"
    ];

    private static readonly HashSet<string> BooleanVariables = new(StringComparer.Ordinal)
    {
        "PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED",
        "PROJECTPULSE_CELAR_AI_ENABLED",
        "PROJECTPULSE_CELAR_AI_TRAINING_ENABLED",
        "PROJECTPULSE_PRIVATE_INFERENCE_REQUIRED_FOR_DOCUMENTS",
        "PROJECTPULSE_AI_CLAUDE_ENABLED",
        "PROJECTPULSE_AI_OPENAI_ENABLED",
        "PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION",
        "PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS",
        "PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED",
        "PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED",
        "PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS",
        "PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED",
        "PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED",
        "PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL",
        "PROJECTPULSE_PULSE_AI_RAG_PERSIST_ANSWER_TEXT",
        "PROJECTPULSE_PULSE_AI_SYSTEM_PRIVATE_MODEL_SYNTHESIS_ENABLED",
        "PROJECTPULSE_PULSE_AI_SYSTEM_PERSIST_TOOL_BODIES",
        "PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT"
    };

    private static readonly HashSet<string> OrderedListVariables = new(StringComparer.Ordinal)
    {
        RouteOrderVariable
    };

    private static readonly HashSet<string> SetVariables = new(StringComparer.Ordinal)
    {
        "PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST",
        "PROJECTPULSE_CLAUDE_APPROVED_MODELS",
        "PROJECTPULSE_CLAUDE_APPROVED_ORIGINS",
        "PROJECTPULSE_OPENAI_APPROVED_MODELS",
        "PROJECTPULSE_OPENAI_APPROVED_ORIGINS",
        "PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST"
    };

    private static readonly HashSet<string> UriVariables = new(StringComparer.Ordinal)
    {
        "PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT",
        "PROJECTPULSE_CLAUDE_ENDPOINT",
        "PROJECTPULSE_OPENAI_ENDPOINT",
        "PROJECTPULSE_PRIVATE_OCR_ENDPOINT",
        "PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT",
        "PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI"
    };

    private static readonly Regex VersionedSecretReference = new(
        @"^(?:https://[a-z0-9-]+\.vault\.azure\.net/secrets/[A-Za-z0-9-]+/[A-Za-z0-9-]{16,}|secretref://[a-z0-9][a-z0-9._-]*/[A-Za-z0-9._-]+@[A-Za-z0-9._-]{8,})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsCandidate => Snapshot().IsCandidate;
    public static bool IsActiveRelease => Snapshot().IsActiveRelease;
    public static bool IsReleaseScoped => Snapshot().IsReleaseScoped;

    public static ReleaseRuntimeSnapshot Snapshot()
    {
        var errors = new List<string>();
        var phase = Phase(Environment.GetEnvironmentVariable(PhaseVariable), errors);
        var sourceCommit = Commit(SourceCommitVariable);
        var runningSourceCommit = Commit(RunningSourceCommitVariable);
        var controlCommit = Commit(ControlCommitVariable);
        var embeddedSourceCommit = EmbeddedSourceCommit();
        var expectedDigest = Sha256(ConfigurationDigestVariable);
        var computedDigest = ComputeSafeConfigurationDigest();
        var releaseScoped = phase is ProjectPulseAiReleasePhase.Candidate or ProjectPulseAiReleasePhase.Active;

        if (!releaseScoped)
        {
            return new ReleaseRuntimeSnapshot(
                phase, sourceCommit, runningSourceCommit, controlCommit,
                embeddedSourceCommit, expectedDigest, computedDigest, [], 0, errors);
        }

        if (sourceCommit.Length != 40)
            errors.Add($"{SourceCommitVariable} must contain the exact 40-character source commit.");
        if (runningSourceCommit.Length != 40)
            errors.Add($"{RunningSourceCommitVariable} must contain the exact 40-character running source commit.");
        if (controlCommit.Length != 40)
            errors.Add($"{ControlCommitVariable} must contain the exact 40-character activation-control commit.");
        if (embeddedSourceCommit.Length != 40)
            errors.Add($"The API assembly must contain the exact 40-character {EmbeddedSourceCommitMetadataKey} build metadata value.");
        if (expectedDigest.Length != 64)
            errors.Add($"{ConfigurationDigestVariable} must contain the exact SHA-256 of the canonical safe AI configuration.");

        RequireEqual(sourceCommit, runningSourceCommit,
            "The release source commit does not match the running application source commit.", errors);
        RequireEqual(sourceCommit, embeddedSourceCommit,
            "The release source commit does not match the immutable commit embedded in the API assembly.", errors);
        RequireEqual(expectedDigest, computedDigest,
            "The canonical AI configuration digest does not match the protected release configuration digest.", errors);

        IReadOnlyList<string> routeOrder = [];
        try
        {
            routeOrder = CelarAiCapabilityCatalog.ValidateTargets(
                (Environment.GetEnvironmentVariable(RouteOrderVariable) ?? string.Empty)
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (ArgumentException exception)
        {
            errors.Add($"{RouteOrderVariable} is invalid: {exception.Message}");
        }

        if (CelarAiCapabilityCatalog.Definitions.Count != 8)
            errors.Add("The release-scoped route catalog must contain exactly all eight central AI capabilities.");
        if (!routeOrder.SequenceEqual(
                CelarAiCapabilityTargets.DefaultOrder,
                StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"{RouteOrderVariable} must be exactly celar_ai,claude,openai,local_template.");
        }

        foreach (var name in new[]
                 {
                     "PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED",
                     "PROJECTPULSE_CELAR_AI_ENABLED",
                     "PROJECTPULSE_CELAR_AI_TRAINING_ENABLED",
                     "PROJECTPULSE_PRIVATE_INFERENCE_REQUIRED_FOR_DOCUMENTS",
                     "PROJECTPULSE_AI_CLAUDE_ENABLED",
                     "PROJECTPULSE_AI_OPENAI_ENABLED",
                     "PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION",
                     "PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS",
                     "PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED",
                     "PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED",
                     "PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS",
                     "PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION",
                     "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED",
                     "PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED",
                     "PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL",
                     "PROJECTPULSE_PULSE_AI_RAG_PERSIST_ANSWER_TEXT",
                     "PROJECTPULSE_PULSE_AI_SYSTEM_PRIVATE_MODEL_SYNTHESIS_ENABLED",
                     "PROJECTPULSE_PULSE_AI_SYSTEM_PERSIST_TOOL_BODIES",
                     "PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT"
                 })
        {
            RequireBoolean(name, errors);
        }

        RequireEnabled("PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED", errors);
        RequireEnabled("PROJECTPULSE_CELAR_AI_ENABLED", errors);
        if (!IsApprovedReleaseTrainingConfiguration(
                Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_ENABLED"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_ENDPOINT"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_HOST_ALLOWLIST"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_BEARER_TOKEN"),
                out var trainingReason))
            errors.Add($"Celar AI training policy failed: {trainingReason}.");
        RequireEnabled("PROJECTPULSE_PRIVATE_INFERENCE_REQUIRED_FOR_DOCUMENTS", errors);
        RequireEnabled("PROJECTPULSE_AI_CLAUDE_ENABLED", errors);
        RequireEnabled("PROJECTPULSE_AI_OPENAI_ENABLED", errors);
        RequireEnabled("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", errors);
        RequireEnabled("PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED", errors);
        RequireEnabled("PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED", errors);
        RequireEnabled("PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS", errors);
        RequireEnabled("PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL", errors);
        RequireEnabled("PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT", errors);
        if (!IsApprovedReleaseDocumentServicePrincipal(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID"),
                out var servicePrincipalReason))
            errors.Add($"Document service principal policy failed: {servicePrincipalReason}.");
        if (Enabled("PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS"))
            errors.Add("PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS=true is prohibited in a release-scoped runtime.");
        if (!IsApprovedReleaseMalwareScannerConfiguration(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_CLAMAV_HOST"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_CLAMAV_PORT"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_CLAMAV_TIMEOUT_SECONDS"),
                Enabled("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE"),
                out var malwareScannerReason))
            errors.Add($"Malware scanner policy failed: {malwareScannerReason}.");

        RejectLegacySecretAlias("ANTHROPIC_API_KEY", errors);
        RejectLegacySecretAlias("OPENAI_API_KEY", errors);
        RejectLegacySecretAlias("PROJECT_PULSE_AI_SECRET_ENCRYPTION_KEY", errors);
        RejectLegacySecretAlias("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_SECRET_NAME", errors);
        RejectLegacySecretAlias("PROJECTPULSE_PRIVATE_INFERENCE_TOKEN_SECRET_NAME", errors);
        RejectLegacySecretAlias("PROJECTPULSE_PRIVATE_OCR_TOKEN_SECRET_NAME", errors);
        RejectLegacySecretAlias("PROJECTPULSE_PRIVATE_EMBEDDING_TOKEN_SECRET_NAME", errors);
        RequireNonEmpty("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID", errors);

        RequirePinnedSecretReference(
            "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY",
            "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_SECRET_REFERENCE",
            required: true,
            errors);
        RequirePinnedSecretReference(
            "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING",
            "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING_SECRET_REFERENCE",
            required: !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING")),
            errors);
        using (var keyRing = ProjectPulseAiEncryptionKeyRing.Load())
        {
            if (!keyRing.Available)
                errors.Add("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY must decode to the configured active 32-byte AES-256 key.");
        }

        RequirePinnedSecretReference(
            "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN",
            "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE",
            required: true,
            errors);

        RequireProvider("CLAUDE", errors);
        RequireProvider("OPENAI", errors);
        RequireNonEmpty("PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT", errors);
        RequireNonEmpty("PROJECTPULSE_PRIVATE_INFERENCE_MODEL", errors);
        RequireNonEmpty("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST", errors);
        RequirePrivateEndpointSyntax("PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT", errors);
        if (!IsApprovedReleasePrivateInferenceDestination(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST"),
                out var privateDestinationReason))
            errors.Add($"Private inference destination policy failed: {privateDestinationReason}.");
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE")?.Trim(),
                "bearer",
                StringComparison.OrdinalIgnoreCase))
            errors.Add("PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE must be exactly bearer for a release-scoped runtime.");

        RequireNonEmpty("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI", errors);
        RequireNonEmpty("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST", errors);
        if (!IsApprovedReleaseSystemToolOrigin(
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST"),
                out var systemToolOriginReason))
            errors.Add($"System tool origin policy failed: {systemToolOriginReason}.");

        var embeddingConfigured = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT"));
        if (embeddingConfigured)
        {
            RequireNonEmpty("PROJECTPULSE_PRIVATE_EMBEDDING_MODEL", errors);
            RequirePrivateEndpointSyntax("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT", errors);
        }
        else if (!Enabled("PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION")
                 || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                     "PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE")))
            errors.Add("A private embedding endpoint/model is required unless lexical-only completion is explicitly enabled with an approval reference.");
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_OCR_ENDPOINT")))
        {
            RequireNonEmpty("PROJECTPULSE_PRIVATE_OCR_MODEL", errors);
            RequirePrivateEndpointSyntax("PROJECTPULSE_PRIVATE_OCR_ENDPOINT", errors);
        }

        try
        {
            if (!ProjectPulseAiDatabaseConnection.ResolveEvidence().Configured)
                errors.Add("A canonical ProjectPulse AI database connection is required.");
        }
        catch (InvalidOperationException exception)
        {
            errors.Add(exception.Message);
        }

        foreach (var name in new[]
                 {
                     "PROJECTPULSE_AI_RELEASE_SOW_DOCUMENT_ID",
                     "PROJECTPULSE_AI_RELEASE_SOW_VERSION_ID",
                     "PROJECTPULSE_AI_RELEASE_SOW_PROJECT_ID"
                 })
        {
            if (!Guid.TryParse(Environment.GetEnvironmentVariable(name), out var value) || value == Guid.Empty)
                errors.Add($"{name} must contain the exact non-empty release SOW identifier.");
        }
        if (Sha256("PROJECTPULSE_AI_RELEASE_SOW_SOURCE_SHA256").Length != 64)
            errors.Add("PROJECTPULSE_AI_RELEASE_SOW_SOURCE_SHA256 must contain the exact release SOW source digest.");
        RequirePinnedSecretReference(
            "PROJECTPULSE_CLAUDE_API_KEY",
            "PROJECTPULSE_CLAUDE_API_KEY_SECRET_REFERENCE",
            required: Enabled("PROJECTPULSE_AI_CLAUDE_ENABLED"),
            errors);
        RequirePinnedSecretReference(
            "PROJECTPULSE_OPENAI_API_KEY",
            "PROJECTPULSE_OPENAI_API_KEY_SECRET_REFERENCE",
            required: Enabled("PROJECTPULSE_AI_OPENAI_ENABLED"),
            errors);
        RequirePinnedSecretReference(
            "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN",
            "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN_SECRET_REFERENCE",
            required: !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_OCR_ENDPOINT")),
            errors);
        RequirePinnedSecretReference(
            "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN",
            "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN_SECRET_REFERENCE",
            required: !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT")),
            errors);

        return new ReleaseRuntimeSnapshot(
            phase, sourceCommit, runningSourceCommit, controlCommit,
            embeddedSourceCommit, expectedDigest, computedDigest, routeOrder,
            Revision(sourceCommit, controlCommit, expectedDigest), errors);
    }

    public static ReleaseRuntimeSnapshot RequireValid()
    {
        var snapshot = Snapshot();
        if (snapshot.Errors.Count > 0)
            throw new InvalidOperationException(
                $"Release-scoped AI configuration is invalid: {string.Join(" ", snapshot.Errors)}");
        return snapshot;
    }

    public static void RejectReleaseConfigurationMutation(string operation)
    {
        if (!RequireValid().IsReleaseScoped) return;
        throw new ProjectPulseAiReleaseReadOnlyException(
            $"{operation} is disabled because release-scoped provider configuration is deployment-managed.");
    }

    public static void RejectCandidateDataMutation(string operation)
    {
        if (!RequireValid().IsCandidate) return;
        throw new ProjectPulseAiReleaseReadOnlyException(
            $"{operation} is disabled in the intrinsically read-only release-candidate phase.");
    }

    public static string ComputeSafeConfigurationDigest()
    {
        var canonical = new StringBuilder("projectpulse-ai-release-config-v2\n");
        foreach (var name in SafeConfigurationVariables.Order(StringComparer.Ordinal))
        {
            var value = NormalizeSafeValue(name, Environment.GetEnvironmentVariable(name));
            canonical.Append(name).Append('=').Append(value).Append('\n');
        }
        using (var keyRing = ProjectPulseAiEncryptionKeyRing.Load())
        {
            canonical.Append("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING_IDS=")
                .Append(string.Join(',', keyRing.KeyIds.Order(StringComparer.Ordinal)))
                .Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static ProjectPulseAiReleasePhase Phase(string? raw, ICollection<string> errors)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Length == 0 || value == "disabled") return ProjectPulseAiReleasePhase.Disabled;
        if (value == "candidate") return ProjectPulseAiReleasePhase.Candidate;
        if (value == "active") return ProjectPulseAiReleasePhase.Active;
        errors.Add($"{PhaseVariable} must be exactly disabled, candidate, or active.");
        return ProjectPulseAiReleasePhase.Disabled;
    }

    private static void RequireEqual(string left, string right, string message, ICollection<string> errors)
    {
        if (left.Length > 0 && right.Length > 0 && !string.Equals(left, right, StringComparison.Ordinal))
            errors.Add(message);
    }

    private static void RequirePinnedSecretReference(
        string secretVariable,
        string referenceVariable,
        bool required,
        ICollection<string> errors)
    {
        if (!required) return;
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(secretVariable)))
            errors.Add($"{secretVariable} must be injected from a protected, version-pinned secret reference.");
        var reference = Environment.GetEnvironmentVariable(referenceVariable)?.Trim() ?? string.Empty;
        if (!VersionedSecretReference.IsMatch(reference))
        {
            errors.Add($"{referenceVariable} must use an approved immutable version-pinned secret reference URI.");
        }
    }

    private static void RejectLegacySecretAlias(string name, ICollection<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            errors.Add($"Legacy secret alias {name} is prohibited in a release-scoped runtime.");
    }

    private static void RequireEnabled(string name, ICollection<string> errors)
    {
        if (!Enabled(name)) errors.Add($"{name}=true is required for a release-scoped runtime.");
    }

    private static void RequireNonEmpty(string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            errors.Add($"{name} is required for a release-scoped runtime.");
    }

    private static void RequirePrivateEndpointSyntax(string name, ICollection<string> errors)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            errors.Add($"{name} must be an absolute HTTPS endpoint without user info, query, or fragment.");
    }

    /// <summary>
    /// Validates the release-only, pre-DNS private inference destination binding.
    /// Connect-time DNS resolution is revalidated separately immediately before
    /// every private request.
    /// </summary>
    public static bool IsApprovedReleasePrivateInferenceDestination(
        string? endpointText,
        string? allowlistText,
        out string reason)
    {
        reason = "private_endpoint_invalid";
        if (!Uri.TryCreate(endpointText?.Trim(), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            return false;

        var host = endpoint.DnsSafeHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (System.Net.IPAddress.TryParse(host, out _)
            || Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            reason = "private_endpoint_must_use_dns_hostname";
            return false;
        }

        var allowlist = (allowlistText ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.Trim().TrimEnd('.').ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (allowlist.Length == 0)
        {
            reason = "explicit_private_endpoint_allowlist_required";
            return false;
        }
        if (allowlist.Any(value => !PulseAiPrivateEndpointPolicy.IsValidAllowlistEntry(value)))
        {
            reason = "private_endpoint_allowlist_entry_invalid";
            return false;
        }

        var defaultEntries = PulseAiPrivateRuntimePolicy.PrivateHostSuffixDefaults
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowlist.Any(defaultEntries.Contains))
        {
            reason = "built_in_private_endpoint_suffix_prohibited";
            return false;
        }

        var matched = allowlist.Any(entry => entry.StartsWith('.')
            ? host.Length > entry.Length && host.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
            : host.Equals(entry, StringComparison.OrdinalIgnoreCase));
        reason = matched
            ? "private_endpoint_hostname_allowlisted"
            : "private_endpoint_hostname_not_allowlisted";
        return matched;
    }

    public static bool IsApprovedReleaseSystemToolOrigin(
        string? baseUriText,
        string? allowlistText,
        out string reason)
    {
        reason = "system_tool_origin_invalid";
        if (!Uri.TryCreate(baseUriText?.Trim(), UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || origin.AbsolutePath != "/"
            || Uri.CheckHostName(origin.DnsSafeHost) != UriHostNameType.Dns)
            return false;

        var expectedAuthority = origin.IsDefaultPort
            ? origin.IdnHost.ToLowerInvariant()
            : $"{origin.IdnHost.ToLowerInvariant()}:{origin.Port}";
        var allowed = Split(allowlistText).Any(raw =>
        {
            var candidate = raw.Trim();
            if (candidate.Length == 0) return false;
            if (candidate.Contains("://", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri)
                    || candidateUri.Scheme != Uri.UriSchemeHttps
                    || !string.IsNullOrEmpty(candidateUri.UserInfo)
                    || !string.IsNullOrEmpty(candidateUri.Query)
                    || !string.IsNullOrEmpty(candidateUri.Fragment)
                    || candidateUri.AbsolutePath != "/") return false;
                candidate = candidateUri.IsDefaultPort
                    ? candidateUri.IdnHost
                    : $"{candidateUri.IdnHost}:{candidateUri.Port}";
            }
            else
            {
                candidate = candidate.TrimEnd('/');
            }
            return string.Equals(candidate, expectedAuthority, StringComparison.OrdinalIgnoreCase);
        });
        reason = allowed
            ? "system_tool_https_origin_allowlisted"
            : "system_tool_origin_not_allowlisted";
        return allowed;
    }

    public static bool IsApprovedReleaseMalwareScannerConfiguration(
        string? mode,
        string? clamAvHost,
        string? clamAvPort,
        string? clamAvTimeoutSeconds,
        bool preScanAttested,
        string? signatureVersion,
        string? approvalReference,
        out string reason)
    {
        if (string.Equals(mode?.Trim(), "clamav_tcp", StringComparison.Ordinal))
        {
            var valid = !string.IsNullOrWhiteSpace(clamAvHost)
                && int.TryParse(clamAvPort, out var port) && port is >= 1 and <= 65535
                && int.TryParse(clamAvTimeoutSeconds, out var timeout) && timeout is >= 5 and <= 300;
            reason = valid ? "clamav_tcp_configuration_verified" : "clamav_tcp_configuration_incomplete";
            return valid;
        }
        if (string.Equals(mode?.Trim(), "pre_scanned_attestation", StringComparison.Ordinal))
        {
            var valid = preScanAttested
                && !string.IsNullOrWhiteSpace(signatureVersion)
                && !string.IsNullOrWhiteSpace(approvalReference);
            reason = valid
                ? "pre_scanned_attestation_configuration_verified"
                : "pre_scanned_attestation_configuration_incomplete";
            return valid;
        }
        reason = "malware_scanner_mode_invalid";
        return false;
    }

    public static bool IsApprovedReleaseDocumentServicePrincipal(string? value, out string reason)
    {
        var valid = Guid.TryParse(value?.Trim(), out var principalId) && principalId != Guid.Empty;
        reason = valid
            ? "document_service_principal_identifier_verified"
            : "document_service_principal_identifier_invalid";
        return valid;
    }

    public static bool IsApprovedReleaseTrainingConfiguration(
        string? enabledText,
        string? endpoint,
        string? hostAllowlist,
        string? bearerToken,
        out string reason)
    {
        if (!bool.TryParse(enabledText?.Trim(), out var enabled) || enabled)
        {
            reason = "release_training_must_be_explicitly_disabled";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(endpoint)
            || !string.IsNullOrWhiteSpace(hostAllowlist)
            || !string.IsNullOrWhiteSpace(bearerToken))
        {
            reason = "release_training_configuration_prohibited";
            return false;
        }
        reason = "release_training_disabled";
        return true;
    }

    private static void RequireProvider(string provider, ICollection<string> errors)
    {
        var endpointName = $"PROJECTPULSE_{provider}_ENDPOINT";
        var modelName = $"PROJECTPULSE_{provider}_MODEL";
        var modelsName = $"PROJECTPULSE_{provider}_APPROVED_MODELS";
        var originsName = $"PROJECTPULSE_{provider}_APPROVED_ORIGINS";
        var model = Environment.GetEnvironmentVariable(modelName)?.Trim() ?? string.Empty;
        var approvedModels = SplitSet(Environment.GetEnvironmentVariable(modelsName));
        if (model.Length == 0 || !approvedModels.Contains(model, StringComparer.Ordinal))
            errors.Add($"{modelName} must exactly match one entry in {modelsName}.");

        var endpointText = Environment.GetEnvironmentVariable(endpointName)?.Trim() ?? string.Empty;
        if (!TryApprovedHttpsOrigin(endpointText, SplitSet(Environment.GetEnvironmentVariable(originsName))))
            errors.Add($"{endpointName} must be an absolute HTTPS endpoint on an exact origin in {originsName}, without user info, query, or fragment.");
    }

    private static bool TryApprovedHttpsOrigin(string endpointText, IReadOnlyList<string> origins)
    {
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)) return false;
        var origin = endpoint.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return origins.Any(candidate => Uri.TryCreate(candidate, UriKind.Absolute, out var approved)
            && approved.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(approved.UserInfo)
            && string.IsNullOrEmpty(approved.Query)
            && string.IsNullOrEmpty(approved.Fragment)
            && approved.AbsolutePath == "/"
            && string.Equals(
                origin,
                approved.GetLeftPart(UriPartial.Authority).TrimEnd('/'),
                StringComparison.Ordinal));
    }

    private static string NormalizeSafeValue(string name, string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (BooleanVariables.Contains(name))
            return bool.TryParse(value, out var enabled) ? enabled ? "true" : "false" : value.ToLowerInvariant();
        if (OrderedListVariables.Contains(name))
            return string.Join(',', Split(value).Select(item => item.ToLowerInvariant()));
        if (SetVariables.Contains(name))
            return string.Join(',', Split(value).Select(item => item.ToLowerInvariant()).Order(StringComparer.Ordinal));
        if (UriVariables.Contains(name) && Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.AbsoluteUri.TrimEnd('/');
        return value;
    }

    private static IReadOnlyList<string> SplitSet(string? value) =>
        Split(value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> Split(string? value) =>
        (value ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void RequireBoolean(string name, ICollection<string> errors)
    {
        var raw = Environment.GetEnvironmentVariable(name)?.Trim();
        if (!string.IsNullOrEmpty(raw) && !bool.TryParse(raw, out _))
            errors.Add($"{name} must be exactly true or false when supplied.");
    }

    private static bool Enabled(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value;

    private static string Commit(string name) => Hex(name, 40);
    private static string Sha256(string name) => Hex(name, 64);

    private static string Hex(string name, int length)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim().ToLowerInvariant() ?? string.Empty;
        return value.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : string.Empty;
    }

    private static string EmbeddedSourceCommit()
    {
        var value = typeof(ProjectPulseAiReleaseRuntimePolicy).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key, EmbeddedSourceCommitMetadataKey, StringComparison.Ordinal))
            ?.Value?.Trim().ToLowerInvariant() ?? string.Empty;
        return value.Length == 40 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : string.Empty;
    }

    private static int Revision(params string[] values)
    {
        if (values.Any(string.IsNullOrWhiteSpace)) return 0;
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(string.Join(':', values)));
        return Math.Max(1, BinaryPrimitives.ReadInt32BigEndian(digest) & int.MaxValue);
    }
}

public enum ProjectPulseAiReleasePhase { Disabled, Candidate, Active }

public sealed record ReleaseRuntimeSnapshot(
    ProjectPulseAiReleasePhase Phase,
    string SourceCommit,
    string RunningSourceCommit,
    string ControlCommit,
    string EmbeddedSourceCommit,
    string ExpectedConfigurationDigest,
    string ComputedConfigurationDigest,
    IReadOnlyList<string> RouteOrder,
    int Revision,
    IReadOnlyList<string> Errors)
{
    public bool IsCandidate => Phase == ProjectPulseAiReleasePhase.Candidate;
    public bool IsActiveRelease => Phase == ProjectPulseAiReleasePhase.Active;
    public bool IsReleaseScoped => IsCandidate || IsActiveRelease;
    public string ConfigurationSourceCommit => SourceCommit;
    public string ConfigurationAuthority => IsReleaseScoped ? "deployment_managed_release" : "database_managed_active";
    public string PhaseCode => Phase.ToString().ToLowerInvariant();
}

public sealed class ProjectPulseAiReleaseReadOnlyException(string message) : InvalidOperationException(message);

public sealed class ProjectPulseAiReleaseRuntimeGuard : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
