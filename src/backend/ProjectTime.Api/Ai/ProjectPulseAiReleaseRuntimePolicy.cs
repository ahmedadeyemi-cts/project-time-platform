using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Defines the immutable, revision-scoped AI configuration used only while a
/// zero-traffic release candidate is being verified. The mode is deliberately
/// fail closed: asking for release-scoped configuration without binding it to
/// the exact running source commit prevents startup and never falls back to the
/// shared database configuration.
/// </summary>
public static class ProjectPulseAiReleaseRuntimePolicy
{
    public const string ModeVariable = "PROJECTPULSE_AI_RELEASE_SCOPED_MODE";
    public const string ConfigurationSourceCommitVariable = "PROJECTPULSE_AI_RELEASE_CONFIG_SOURCE_COMMIT";
    public const string RouteOrderVariable = "PROJECTPULSE_AI_RELEASE_ROUTE_ORDER";
    public const string CandidateReadOnlyVariable = "PROJECTPULSE_AI_CANDIDATE_READ_ONLY";
    public const string RunningSourceCommitVariable = "PROJECTPULSE_SOURCE_COMMIT";
    public const string EmbeddedSourceCommitMetadataKey = "ProjectPulseSourceRevision";

    public static bool Requested => StrictBoolean(ModeVariable);

    public static bool CandidateReadOnlyRequested => StrictBoolean(CandidateReadOnlyVariable);

    public static ReleaseRuntimeSnapshot Snapshot()
    {
        var runningSourceCommit = Commit(RunningSourceCommitVariable);
        var configurationSourceCommit = Commit(ConfigurationSourceCommitVariable);
        var embeddedSourceCommit = EmbeddedSourceCommit();
        var errors = new List<string>();
        var requested = Boolean(ModeVariable, errors);
        var candidateReadOnlyRequested = Boolean(CandidateReadOnlyVariable, errors);

        if (!requested)
        {
            if (candidateReadOnlyRequested)
                errors.Add($"{CandidateReadOnlyVariable} cannot be enabled unless {ModeVariable} is enabled.");
            return new ReleaseRuntimeSnapshot(
                Requested: false,
                Active: false,
                CandidateReadOnly: candidateReadOnlyRequested,
                RunningSourceCommit: runningSourceCommit,
                ConfigurationSourceCommit: configurationSourceCommit,
                EmbeddedSourceCommit: embeddedSourceCommit,
                RouteOrder: [],
                Revision: 0,
                Errors: errors);
        }

        if (runningSourceCommit.Length != 40)
            errors.Add($"{RunningSourceCommitVariable} must contain the exact 40-character running source commit.");
        if (configurationSourceCommit.Length != 40)
            errors.Add($"{ConfigurationSourceCommitVariable} must contain the exact 40-character configuration source commit.");
        if (embeddedSourceCommit.Length != 40)
            errors.Add($"The API assembly must contain the exact 40-character {EmbeddedSourceCommitMetadataKey} build metadata value.");
        if (runningSourceCommit.Length == 40
            && configurationSourceCommit.Length == 40
            && !string.Equals(runningSourceCommit, configurationSourceCommit, StringComparison.Ordinal))
        {
            errors.Add("The deployment-managed AI configuration source commit does not match the running application source commit.");
        }
        if (embeddedSourceCommit.Length == 40
            && runningSourceCommit.Length == 40
            && !string.Equals(embeddedSourceCommit, runningSourceCommit, StringComparison.Ordinal))
        {
            errors.Add("The running application source commit does not match the immutable commit embedded in the API assembly.");
        }
        if (embeddedSourceCommit.Length == 40
            && configurationSourceCommit.Length == 40
            && !string.Equals(embeddedSourceCommit, configurationSourceCommit, StringComparison.Ordinal))
        {
            errors.Add("The deployment-managed AI configuration source commit does not match the immutable commit embedded in the API assembly.");
        }
        if (!candidateReadOnlyRequested)
            errors.Add($"{CandidateReadOnlyVariable}=true is required for release-scoped candidate configuration.");

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

        return new ReleaseRuntimeSnapshot(
            Requested: true,
            Active: errors.Count == 0,
            CandidateReadOnly: candidateReadOnlyRequested,
            RunningSourceCommit: runningSourceCommit,
            ConfigurationSourceCommit: configurationSourceCommit,
            EmbeddedSourceCommit: embeddedSourceCommit,
            RouteOrder: routeOrder,
            Revision: Revision(configurationSourceCommit),
            Errors: errors);
    }

    public static ReleaseRuntimeSnapshot RequireValid()
    {
        var snapshot = Snapshot();
        if (snapshot.Errors.Count > 0)
            throw new InvalidOperationException(
                $"Release-scoped AI configuration is invalid: {string.Join(" ", snapshot.Errors)}");
        return snapshot;
    }

    public static void RejectMutation(string operation)
    {
        var snapshot = Snapshot();
        if (!snapshot.Requested && !snapshot.CandidateReadOnly) return;
        throw new ProjectPulseAiReleaseReadOnlyException(
            $"{operation} is disabled while the exact-source release candidate uses deployment-managed read-only AI configuration.");
    }

    private static bool StrictBoolean(string name)
    {
        var errors = new List<string>();
        var value = Boolean(name, errors);
        if (errors.Count > 0) throw new InvalidOperationException(errors[0]);
        return value;
    }

    private static bool Boolean(string name, ICollection<string> errors)
    {
        var raw = Environment.GetEnvironmentVariable(name)?.Trim();
        if (string.IsNullOrEmpty(raw)) return false;
        if (bool.TryParse(raw, out var value)) return value;
        errors.Add($"{name} must be exactly true or false when supplied.");
        return false;
    }

    private static string Commit(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim().ToLowerInvariant() ?? string.Empty;
        return value.Length == 40 && value.All(Uri.IsHexDigit) ? value : string.Empty;
    }

    private static string EmbeddedSourceCommit()
    {
        var value = typeof(ProjectPulseAiReleaseRuntimePolicy).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                EmbeddedSourceCommitMetadataKey,
                StringComparison.Ordinal))
            ?.Value
            ?.Trim()
            .ToLowerInvariant() ?? string.Empty;
        return value.Length == 40 && value.All(Uri.IsHexDigit) ? value : string.Empty;
    }

    private static int Revision(string sourceCommit)
    {
        if (sourceCommit.Length != 40) return 0;
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(sourceCommit));
        return Math.Max(1, BinaryPrimitives.ReadInt32BigEndian(digest) & int.MaxValue);
    }
}

public sealed record ReleaseRuntimeSnapshot(
    bool Requested,
    bool Active,
    bool CandidateReadOnly,
    string RunningSourceCommit,
    string ConfigurationSourceCommit,
    string EmbeddedSourceCommit,
    IReadOnlyList<string> RouteOrder,
    int Revision,
    IReadOnlyList<string> Errors)
{
    public string ConfigurationAuthority => Active ? "deployment_managed_release" : "database_managed_active";
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
