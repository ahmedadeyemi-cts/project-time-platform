namespace ProjectTime.Api.Ai;

/// <summary>
/// Allows one tightly bounded candidate mutation path: the background private-document
/// processor in Protected Test. The release envelope must already be valid and the
/// candidate, running, and embedded source commits must all be the exact same SHA.
/// Interactive candidate data mutations remain blocked by RejectCandidateDataMutation.
/// </summary>
internal static class PulseAiProtectedTestCandidatePolicy
{
    private const string EnvironmentVariable = "PROJECTPULSE_ENVIRONMENT";

    public static bool AllowsPrivateDocumentProcessing(ReleaseRuntimeSnapshot release)
    {
        if (!release.IsCandidate)
            return false;

        var environment = Environment.GetEnvironmentVariable(EnvironmentVariable)?.Trim() ?? string.Empty;
        if (!environment.Equals("test", StringComparison.OrdinalIgnoreCase))
            return false;

        var runningSourceCommit = Environment
            .GetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.RunningSourceCommitVariable)?
            .Trim()
            .ToLowerInvariant() ?? string.Empty;
        if (runningSourceCommit.Length != 40 || !runningSourceCommit.All(Uri.IsHexDigit))
            return false;

        return string.Equals(release.SourceCommit, runningSourceCommit, StringComparison.Ordinal)
            && string.Equals(release.RunningSourceCommit, runningSourceCommit, StringComparison.Ordinal)
            && string.Equals(release.EmbeddedSourceCommit, runningSourceCommit, StringComparison.Ordinal);
    }
}
