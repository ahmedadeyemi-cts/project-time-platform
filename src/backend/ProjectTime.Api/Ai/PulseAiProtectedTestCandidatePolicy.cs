namespace ProjectTime.Api.Ai;

/// <summary>
/// Allows one tightly bounded Protected Test background-processing path. A release-scoped
/// candidate must retain the exact source envelope; the established Protected-Test
/// application controller may also deploy an unscoped candidate while preserving the
/// already-approved private runtime binding. In that case processing is allowed only
/// when the running and embedded source commits are the exact same SHA. Active release
/// phases are never auto-activated through this Test-only path. Interactive candidate
/// data mutations remain blocked by RejectCandidateDataMutation.
/// </summary>
internal static class PulseAiProtectedTestCandidatePolicy
{
    private const string EnvironmentVariable = "PROJECTPULSE_ENVIRONMENT";
    private const string DeploymentManagedVariable = "PROJECTPULSE_CELAR_AI_DEPLOYMENT_MANAGED";
    private const string ProtectedTestSecretReferencePrefix = "github-environment://test/";

    private static readonly string[] ProtectedTestSecretReferenceVariables =
    [
        "PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN_SECRET_REFERENCE",
        "PROJECTPULSE_PRIVATE_MALWARE_SCAN_BEARER_TOKEN_SECRET_REFERENCE",
        "PROJECTPULSE_PRIVATE_OCR_BEARER_TOKEN_SECRET_REFERENCE",
        "PROJECTPULSE_PRIVATE_EMBEDDING_BEARER_TOKEN_SECRET_REFERENCE"
    ];

    internal static bool IsProtectedTestEnvironment()
    {
        var environment = Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim() ?? string.Empty;
        if (environment.Equals("test", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!bool.TryParse(Environment.GetEnvironmentVariable(DeploymentManagedVariable), out var deploymentManaged)
            || !deploymentManaged)
        {
            return false;
        }

        return ProtectedTestSecretReferenceVariables.All(variable =>
        {
            var reference = Environment.GetEnvironmentVariable(variable)?.Trim() ?? string.Empty;
            return reference.StartsWith(ProtectedTestSecretReferencePrefix, StringComparison.OrdinalIgnoreCase);
        });
    }

    public static bool AllowsPrivateDocumentProcessing(ReleaseRuntimeSnapshot release)
    {
        if (!IsProtectedTestEnvironment())
            return false;

        var runningSourceCommit = Environment
            .GetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.RunningSourceCommitVariable)?
            .Trim()
            .ToLowerInvariant() ?? string.Empty;
        if (runningSourceCommit.Length != 40 || !runningSourceCommit.All(Uri.IsHexDigit))
            return false;

        if (!string.Equals(release.RunningSourceCommit, runningSourceCommit, StringComparison.Ordinal)
            || !string.Equals(release.EmbeddedSourceCommit, runningSourceCommit, StringComparison.Ordinal))
        {
            return false;
        }

        if (release.IsCandidate)
        {
            return string.Equals(release.SourceCommit, runningSourceCommit, StringComparison.Ordinal);
        }

        return !release.IsReleaseScoped;
    }
}
