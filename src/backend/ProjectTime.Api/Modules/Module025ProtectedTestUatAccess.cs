using System.Text.RegularExpressions;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Supplies a non-persistent Solution Architect authorization fixture for the exact
/// protected-Test GitHub Actions run that owns Module 025 live UAT. The fixture never
/// changes role assignments and fails closed unless every Test/run/session boundary
/// is present. It is inert in every normal Test request and in Production.
/// </summary>
internal static class Module025ProtectedTestUatAccess
{
    internal const string EnabledVariable =
        "PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_ENABLED";
    internal const string RunIdVariable =
        "PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_RUN_ID";
    internal const string SourceCommitVariable =
        "PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_SOURCE_COMMIT";
    internal const string ExpiresAtVariable =
        "PROJECTPULSE_MODULE025_PROTECTED_TEST_UAT_EXPIRES_AT";
    internal const string RunIdHeader =
        "X-ProjectPulse-Module025-Uat-Run";
    internal const string TargetEmail =
        "demo.manager@ussignal.local";

    private const string ProtectedTestHost =
        "phd-west-test.onenecklab.com";

    internal static bool Authorizes(
        HttpContext context,
        Guid actualUserId,
        Guid effectiveUserId,
        string email,
        IReadOnlySet<string> roles)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnabledVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedRunId =
            Environment.GetEnvironmentVariable(RunIdVariable)?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(expectedRunId, "^[0-9]+-[0-9]+$", RegexOptions.CultureInvariant)
            || !string.Equals(
                context.Request.Headers[RunIdHeader].ToString(),
                expectedRunId,
                StringComparison.Ordinal))
            return false;

        var sourceCommit =
            Environment.GetEnvironmentVariable("PROJECTPULSE_SOURCE_COMMIT")?.Trim() ?? string.Empty;
        var expectedSourceCommit =
            Environment.GetEnvironmentVariable(SourceCommitVariable)?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(sourceCommit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant)
            || !string.Equals(sourceCommit, expectedSourceCommit, StringComparison.Ordinal))
            return false;

        var expiresAtText =
            Environment.GetEnvironmentVariable(ExpiresAtVariable)?.Trim() ?? string.Empty;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!long.TryParse(
                expiresAtText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var expiresAt)
            || expiresAt <= now
            || expiresAt > now + 3_600)
            return false;

        if (!string.Equals(context.Request.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(context.Request.Host.Host, ProtectedTestHost, StringComparison.OrdinalIgnoreCase)
            || !context.Request.Headers.TryGetValue("Origin", out var originValues)
            || !Uri.TryCreate(originValues.ToString(), UriKind.Absolute, out var origin)
            || !string.Equals(origin.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.Host, ProtectedTestHost, StringComparison.OrdinalIgnoreCase)
            || origin.Port != 443)
            return false;

        return actualUserId == effectiveUserId
            && !ProjectPulseActualSessionAuthority.IsViewAs(context)
            && string.Equals(email, TargetEmail, StringComparison.OrdinalIgnoreCase)
            && roles.Contains("MANAGER")
            && !roles.Overlaps(new[] { "SOLUTION_ARCHITECT", "SOLUTIONS_ARCHITECT", "SA", "SAA" });
    }
}
