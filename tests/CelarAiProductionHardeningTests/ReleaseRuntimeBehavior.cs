using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectTime.Api.Ai;

internal static class ReleaseRuntimeBehavior
{
    private const string SourceCommit = "1111111111111111111111111111111111111111";
    private const string ControlCommit = "2222222222222222222222222222222222222222";

    public static async Task RunAsync(string databaseConnectionString)
    {
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleasePrivateInferenceDestination(
                "https://celar.private.example/v1/chat/completions", string.Empty, out _),
            "empty private endpoint allowlist is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleasePrivateInferenceDestination(
                "https://celar.private/v1/chat/completions",
                string.Join(',', PulseAiPrivateRuntimePolicy.PrivateHostSuffixDefaults), out _),
            "default-only private endpoint allowlist is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleasePrivateInferenceDestination(
                "https://celar.private/v1/chat/completions",
                ".private,unrelated.private.example", out _),
            "mixed built-in and deployment-specific private endpoint allowlist is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleasePrivateInferenceDestination(
                "https://10.20.30.40/v1/chat/completions", "10.20.30.40", out _),
            "private inference IP literal is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleasePrivateInferenceDestination(
                "https://celar.private.example/v1/chat/completions", "other.private.example", out _),
            "unmatched private inference hostname is rejected");
        Require(ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleasePrivateInferenceDestination(
                "https://celar.private.example/v1/chat/completions", "celar.private.example", out _),
            "exact private inference hostname allowlist match is accepted");
        Require(ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleasePrivateInferenceDestination(
                "https://celar.region.corp.example/v1/chat/completions", ".corp.example", out _),
            "leading-dot private inference hostname suffix match is accepted");
        Require(ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseSystemToolOrigin(
                "https://projectpulse.internal.example/", "projectpulse.internal.example", out _),
            "exact HTTPS system-tool origin and host allowlist are accepted");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseSystemToolOrigin(
                "https://projectpulse.internal.example/", "other.internal.example", out _),
            "unmatched system-tool origin and host allowlist are rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseMalwareScannerConfiguration(
                null, null, null, null, false, null, null, out _),
            "missing release malware scanner mode is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseMalwareScannerConfiguration(
                "approved_pre_scan_attestation", null, null, null, true, "ci-signature", "ci-approval", out _),
            "invalid legacy release malware scanner mode is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseMalwareScannerConfiguration(
                "clamav_tcp", string.Empty, "3310", "45", false, null, null, out _),
            "incomplete ClamAV release scanner configuration is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseMalwareScannerConfiguration(
                "pre_scanned_attestation", null, null, null, false, "ci-signature", "ci-approval", out _),
            "incomplete pre-scan release scanner attestation is rejected");
        Require(ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseMalwareScannerConfiguration(
                "pre_scanned_attestation", null, null, null, true, "ci-signature", "ci-approval", out _),
            "complete pre-scan release scanner attestation is accepted");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseDocumentServicePrincipal(null, out _),
            "missing release document service principal is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseDocumentServicePrincipal("not-a-uuid", out _),
            "invalid release document service principal is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseDocumentServicePrincipal(Guid.Empty.ToString(), out _),
            "empty release document service principal UUID is rejected");
        Require(ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseDocumentServicePrincipal(
                "10000000-0000-0000-0000-000000000001", out _),
            "valid release document service principal UUID is accepted");
        Require(ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseTrainingConfiguration(
                "false", null, null, null, out _),
            "explicitly disabled release training configuration is accepted");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseTrainingConfiguration(
                "true", null, null, null, out _),
            "enabled release training configuration is rejected");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseTrainingConfiguration(
                "false", "https://training.private.example/", "training.private.example", null, out _),
            "configured release training endpoint is rejected while training is disabled");
        Require(!ProjectPulseAiReleaseRuntimePolicy.IsApprovedReleaseTrainingConfiguration(
                "false", null, null, "unversioned-token", out _),
            "raw release training bearer token is rejected");

        var alphaContext = string.Join(' ', Enumerable.Repeat("alpha", 32));
        var betaContext = string.Join(' ', Enumerable.Repeat("beta", 32));
        Require(CelarAiPrivateGenerationTarget.ResponseMatchesDerivedContentChallenge(
                alphaContext, "alpha|alpha|alpha"),
            "derived private SOW challenge accepts the exact content answer");
        Require(!CelarAiPrivateGenerationTarget.ResponseMatchesDerivedContentChallenge(
                alphaContext, "PROJECTPULSE_EXACT_SOW_READY"),
            "constant exact-token stub cannot satisfy the derived private SOW challenge");
        Require(!CelarAiPrivateGenerationTarget.ResponseMatchesDerivedContentChallenge(
                betaContext, "alpha|alpha|alpha"),
            "derived private SOW challenge response is content-dependent");

        var touched = ProjectPulseAiDatabaseConnection.DirectAliases
            .Concat(new[]
            {
                "PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD",
                ProjectPulseAiReleaseRuntimePolicy.PhaseVariable,
                ProjectPulseAiReleaseRuntimePolicy.SourceCommitVariable,
                ProjectPulseAiReleaseRuntimePolicy.RunningSourceCommitVariable,
                ProjectPulseAiReleaseRuntimePolicy.ControlCommitVariable,
                ProjectPulseAiReleaseRuntimePolicy.ConfigurationDigestVariable,
                ProjectPulseAiReleaseRuntimePolicy.RouteOrderVariable,
                "PROJECTPULSE_ENVIRONMENT", "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID",
                "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY", "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_SECRET_REFERENCE",
                "PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED", "PROJECTPULSE_CELAR_AI_ENABLED",
                "PROJECTPULSE_CELAR_AI_TRAINING_ENABLED", "PROJECTPULSE_CELAR_AI_TRAINING_ENDPOINT",
                "PROJECTPULSE_CELAR_AI_TRAINING_HOST_ALLOWLIST", "PROJECTPULSE_CELAR_AI_TRAINING_BEARER_TOKEN",
                "PROJECTPULSE_PRIVATE_INFERENCE_REQUIRED_FOR_DOCUMENTS", "PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT",
                "PROJECTPULSE_PRIVATE_INFERENCE_MODEL", "PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE",
                "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN", "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE",
                "PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST", "PROJECTPULSE_AI_CLAUDE_ENABLED",
                "PROJECTPULSE_CLAUDE_ENDPOINT", "PROJECTPULSE_CLAUDE_APPROVED_ORIGINS", "PROJECTPULSE_CLAUDE_MODEL",
                "PROJECTPULSE_CLAUDE_APPROVED_MODELS", "PROJECTPULSE_CLAUDE_API_VERSION",
                "PROJECTPULSE_CLAUDE_API_KEY", "PROJECTPULSE_CLAUDE_API_KEY_SECRET_REFERENCE",
                "PROJECTPULSE_AI_OPENAI_ENABLED", "PROJECTPULSE_OPENAI_ENDPOINT",
                "PROJECTPULSE_OPENAI_APPROVED_ORIGINS", "PROJECTPULSE_OPENAI_MODEL",
                "PROJECTPULSE_OPENAI_APPROVED_MODELS", "PROJECTPULSE_OPENAI_API_VERSION",
                "PROJECTPULSE_OPENAI_API_KEY", "PROJECTPULSE_OPENAI_API_KEY_SECRET_REFERENCE",
                "PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION",
                "PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS",
                "PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED", "PROJECTPULSE_AI_MODE",
                "PROJECTPULSE_AI_TIMEOUT_SECONDS", "PROJECTPULSE_AI_RETRY_COUNT", "PROJECTPULSE_AI_MAX_OUTPUT_TOKENS",
                "PROJECTPULSE_AI_HEALTH_INTERVAL_SECONDS", "PROJECTPULSE_AI_FAILURE_THRESHOLD",
                "PROJECTPULSE_AI_CIRCUIT_BREAK_SECONDS", "PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED",
                "PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS",
                "PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID",
                "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE",
                "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED",
                "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION",
                "PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE",
                "PROJECTPULSE_PULSE_AI_CLAMAV_HOST", "PROJECTPULSE_PULSE_AI_CLAMAV_PORT",
                "PROJECTPULSE_PULSE_AI_CLAMAV_TIMEOUT_SECONDS",
                "PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL",
                "PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI",
                "PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST",
                "PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION",
                "PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE", "PROJECTPULSE_UPLOAD_ROOT",
                "PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT", "PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_FILE",
                "PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_SHA256", "PROJECTPULSE_AI_RELEASE_SOW_DOCUMENT_ID",
                "PROJECTPULSE_AI_RELEASE_SOW_VERSION_ID", "PROJECTPULSE_AI_RELEASE_SOW_PROJECT_ID",
                "PROJECTPULSE_AI_RELEASE_SOW_SOURCE_SHA256", "ANTHROPIC_API_KEY", "OPENAI_API_KEY"
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var before = touched.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (var name in touched) Environment.SetEnvironmentVariable(name, null);
            Environment.SetEnvironmentVariable("PROJECTPULSE_CONNECTION_STRING", databaseConnectionString);
            ConfigureRelease();

            var secureDigest = ProjectPulseAiReleaseRuntimePolicy.ComputeSafeConfigurationDigest();
            Set("PROJECTPULSE_CELAR_AI_TRAINING_ENABLED", "true");
            var trainingEnabledDigest = ProjectPulseAiReleaseRuntimePolicy.ComputeSafeConfigurationDigest();
            Require(secureDigest != trainingEnabledDigest,
                "release digest changes with the Celar AI training toggle");
            Set("PROJECTPULSE_CELAR_AI_TRAINING_ENABLED", "false");
            Set("PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS", "true");
            var insecureDigest = ProjectPulseAiReleaseRuntimePolicy.ComputeSafeConfigurationDigest();
            Require(secureDigest != insecureDigest,
                "release digest changes with the insecure-loopback policy flag");
            Set("PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS", "false");
            Set("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI", "https://alternate.internal.example/");
            var alternateToolOriginDigest = ProjectPulseAiReleaseRuntimePolicy.ComputeSafeConfigurationDigest();
            Require(secureDigest != alternateToolOriginDigest,
                "release digest changes with the system-tool base URI");
            Set("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI", "https://projectpulse.internal.example/");

            var digestA = ProjectPulseAiReleaseRuntimePolicy.ComputeSafeConfigurationDigest();
            Environment.SetEnvironmentVariable("PROJECTPULSE_AI_CLAUDE_APPROVED_MODELS", "claude-ci, claude-alt");
            Environment.SetEnvironmentVariable("PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT", " TRUE ");
            Environment.SetEnvironmentVariable("PROJECTPULSE_CLAUDE_ENDPOINT", "https://api.anthropic.com/v1/");
            var digestB = ProjectPulseAiReleaseRuntimePolicy.ComputeSafeConfigurationDigest();
            Require(digestA == digestB, "release digest normalizes sets, booleans, and endpoint trailing slash");

            Set("PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS", "true");
            Environment.SetEnvironmentVariable(
                ProjectPulseAiReleaseRuntimePolicy.ConfigurationDigestVariable,
                ProjectPulseAiReleaseRuntimePolicy.ComputeSafeConfigurationDigest());
            Require(ProjectPulseAiReleaseRuntimePolicy.Snapshot().Errors.Any(error =>
                    error.Contains(
                        "PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS=true",
                        StringComparison.Ordinal)
                    && error.Contains("prohibited", StringComparison.OrdinalIgnoreCase)),
                "release policy rejects the insecure-loopback endpoint flag");
            Set("PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS", "false");
            Environment.SetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.ConfigurationDigestVariable, digestB);
            var candidate = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
            Require(candidate.IsCandidate, "candidate phase validates");

            var nextCalled = false;
            var services = new ServiceCollection().BuildServiceProvider();
            var app = new ApplicationBuilder(services);
            app.UseProjectPulseAiCandidateRequestFence();
            app.Run(_ => { nextCalled = true; return Task.CompletedTask; });
            var pipeline = app.Build();
            var blocked = new DefaultHttpContext { RequestServices = services };
            blocked.Request.Method = HttpMethods.Post;
            blocked.Request.Path = "/api/ai-configuration/routes/help_assistant/reset";
            blocked.Response.Body = new MemoryStream();
            await pipeline(blocked);
            Require(blocked.Response.StatusCode == StatusCodes.Status423Locked && !nextCalled,
                "candidate fence blocks mutation before downstream execution");

            Environment.SetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.PhaseVariable, "active");
            nextCalled = false;
            var active = new DefaultHttpContext { RequestServices = services };
            active.Request.Method = HttpMethods.Post;
            active.Request.Path = "/api/application-path";
            await pipeline(active);
            Require(nextCalled, "active phase preserves the normal data plane");

            Environment.SetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.PhaseVariable, "disabled");
            Environment.SetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.ConfigurationDigestVariable, "invalid");
            Require(ProjectPulseAiReleaseRuntimePolicy.RequireValid().Phase == ProjectPulseAiReleasePhase.Disabled,
                "disabled mode remains compatible with legacy runtime configuration");

            Environment.SetEnvironmentVariable("ConnectionStrings__ProjectPulse", databaseConnectionString + ";Application Name=conflict");
            var conflictRejected = false;
            try { _ = ProjectPulseAiDatabaseConnection.ResolveEvidence(); }
            catch (InvalidOperationException) { conflictRejected = true; }
            Require(conflictRejected, "conflicting database aliases fail closed");

            foreach (var alias in ProjectPulseAiDatabaseConnection.DirectAliases)
                Environment.SetEnvironmentVariable(alias, null);
            var parsed = new NpgsqlConnectionStringBuilder(databaseConnectionString);
            Set("PTP_DB_HOST", parsed.Host);
            Set("PTP_DB_PORT", parsed.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set("PTP_DB_NAME", parsed.Database);
            Set("PTP_DB_USER", parsed.Username);
            Set("PTP_DB_PASSWORD", parsed.Password);
            var ptpEvidence = ProjectPulseAiDatabaseConnection.ResolveEvidence();
            Require(ptpEvidence.Configured && ptpEvidence.Source == "PTP_DB_*",
                "canonical database resolver accepts the complete PTP_DB_* contract");
        }
        finally
        {
            foreach (var pair in before) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        Console.WriteLine("CELAR_AI_RELEASE_RUNTIME_BEHAVIOR=PASSED");
    }

    private static void ConfigureRelease()
    {
        Set(ProjectPulseAiReleaseRuntimePolicy.PhaseVariable, "candidate");
        Set(ProjectPulseAiReleaseRuntimePolicy.SourceCommitVariable, SourceCommit);
        Set(ProjectPulseAiReleaseRuntimePolicy.RunningSourceCommitVariable, SourceCommit);
        Set(ProjectPulseAiReleaseRuntimePolicy.ControlCommitVariable, ControlCommit);
        Set(ProjectPulseAiReleaseRuntimePolicy.RouteOrderVariable, "celar_ai,claude,openai,local_template");
        Set("PROJECTPULSE_ENVIRONMENT", "ci");
        Set("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID", "ci-v1");
        Set("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY", Convert.ToBase64String(new byte[32]));
        Set("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_SECRET_REFERENCE", "secretref://ci/ai-key@version-0001");
        Set("PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED", "true");
        Set("PROJECTPULSE_CELAR_AI_ENABLED", "true");
        Set("PROJECTPULSE_CELAR_AI_TRAINING_ENABLED", "false");
        Set("PROJECTPULSE_PRIVATE_INFERENCE_REQUIRED_FOR_DOCUMENTS", "true");
        Set("PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT", "https://celar.private.example/v1/chat/completions");
        Set("PROJECTPULSE_PRIVATE_INFERENCE_MODEL", "celar-ci");
        Set("PROJECTPULSE_PRIVATE_INFERENCE_AUTH_MODE", "bearer");
        Set("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN", "ci-private-token");
        Set("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE", "secretref://ci/celar-token@version-0001");
        Set("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST", "celar.private.example");
        Set("PROJECTPULSE_AI_CLAUDE_ENABLED", "true");
        Set("PROJECTPULSE_CLAUDE_ENDPOINT", "https://api.anthropic.com/v1");
        Set("PROJECTPULSE_CLAUDE_APPROVED_ORIGINS", "https://api.anthropic.com");
        Set("PROJECTPULSE_CLAUDE_MODEL", "claude-ci");
        Set("PROJECTPULSE_CLAUDE_APPROVED_MODELS", "claude-alt,claude-ci");
        Set("PROJECTPULSE_CLAUDE_API_VERSION", "2023-06-01");
        Set("PROJECTPULSE_CLAUDE_API_KEY", "ci-claude-key");
        Set("PROJECTPULSE_CLAUDE_API_KEY_SECRET_REFERENCE", "secretref://ci/claude-key@version-0001");
        Set("PROJECTPULSE_AI_OPENAI_ENABLED", "true");
        Set("PROJECTPULSE_OPENAI_ENDPOINT", "https://api.openai.com/v1");
        Set("PROJECTPULSE_OPENAI_APPROVED_ORIGINS", "https://api.openai.com");
        Set("PROJECTPULSE_OPENAI_MODEL", "gpt-ci");
        Set("PROJECTPULSE_OPENAI_APPROVED_MODELS", "gpt-ci");
        Set("PROJECTPULSE_OPENAI_API_VERSION", "responses-v1");
        Set("PROJECTPULSE_OPENAI_API_KEY", "ci-openai-key");
        Set("PROJECTPULSE_OPENAI_API_KEY_SECRET_REFERENCE", "secretref://ci/openai-key@version-0001");
        Set("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", "true");
        Set("PROJECTPULSE_AI_ALLOW_INSECURE_LOOPBACK_ENDPOINTS", "false");
        Set("PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED", "true");
        Set("PROJECTPULSE_AI_MODE", "priority_failover");
        Set("PROJECTPULSE_AI_TIMEOUT_SECONDS", "30");
        Set("PROJECTPULSE_AI_RETRY_COUNT", "2");
        Set("PROJECTPULSE_AI_MAX_OUTPUT_TOKENS", "800");
        Set("PROJECTPULSE_AI_HEALTH_INTERVAL_SECONDS", "120");
        Set("PROJECTPULSE_AI_FAILURE_THRESHOLD", "3");
        Set("PROJECTPULSE_AI_CIRCUIT_BREAK_SECONDS", "180");
        Set("PROJECTPULSE_PULSE_AI_PRIVATE_RUNTIME_WORKER_ENABLED", "true");
        Set("PROJECTPULSE_PULSE_AI_AUTO_QUEUE_ELIGIBLE_DOCUMENTS", "true");
        Set("PROJECTPULSE_PULSE_AI_DOCUMENT_SERVICE_PRINCIPAL_USER_ID", "10000000-0000-0000-0000-000000000001");
        Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE", "pre_scanned_attestation");
        Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED", "true");
        Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SIGNATURE_VERSION", "ci-signature-2026-08-05");
        Set("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_APPROVAL_REFERENCE", "ci-approval");
        Set("PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL", "true");
        Set("PROJECTPULSE_PULSE_AI_ALLOW_LEXICAL_ONLY_COMPLETION", "true");
        Set("PROJECTPULSE_PULSE_AI_LEXICAL_ONLY_APPROVAL_REFERENCE", "ci-lexical-approval");
        Set("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI", "https://projectpulse.internal.example/");
        Set("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST", "projectpulse.internal.example");
        Set("PROJECTPULSE_UPLOAD_ROOT", "/opt/project-time-platform/uploads");
        Set("PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT", "true");
        Set("PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_FILE", "release-canary.txt");
        Set("PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_SHA256", new string('a', 64));
        Set("PROJECTPULSE_AI_RELEASE_SOW_DOCUMENT_ID", "50000000-0000-0000-0000-000000000001");
        Set("PROJECTPULSE_AI_RELEASE_SOW_VERSION_ID", "60000000-0000-0000-0000-000000000001");
        Set("PROJECTPULSE_AI_RELEASE_SOW_PROJECT_ID", "30000000-0000-0000-0000-000000000001");
        Set("PROJECTPULSE_AI_RELEASE_SOW_SOURCE_SHA256", new string('b', 64));
    }

    private static void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);
    private static void Require(bool condition, string evidence)
    {
        if (!condition) throw new InvalidOperationException($"Release-runtime behavioral assertion failed: {evidence}.");
    }
}
