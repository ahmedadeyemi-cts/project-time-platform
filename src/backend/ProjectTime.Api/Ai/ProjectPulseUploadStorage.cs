using System.Security.Cryptography;
using System.Text;

namespace ProjectTime.Api.Ai;

public sealed record ProjectPulseUploadStorageReadiness(
    bool CanonicalRootConfigured,
    bool LegacyRootInUse,
    bool RootExists,
    bool RootWritable,
    bool KnownEphemeralLocation,
    bool SharedPersistentStorageAttested,
    bool ProductionReady,
    string RootFingerprint,
    IReadOnlyList<string> Blockers);

public static class ProjectPulseUploadStorage
{
    public const string CanonicalEnvironmentVariable = "PROJECTPULSE_UPLOAD_ROOT";
    public const string LegacyEnvironmentVariable = "PROJECT_PULSE_UPLOAD_ROOT";
    public const string SharedPersistenceEnvironmentVariable = "PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT";
    public const string DefaultRoot = "/opt/project-time-platform/uploads";

    public static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable(CanonicalEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable(LegacyEnvironmentVariable);
        }

        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? DefaultRoot : configured.Trim());
    }

    /// <summary>
    /// Evaluates the production storage contract without returning the configured
    /// path. A runtime cannot prove that a mount is shared across replicas, so an
    /// operator must explicitly attest that infrastructure has mounted shared,
    /// durable storage. Known temporary locations fail closed even if attested.
    /// </summary>
    public static ProjectPulseUploadStorageReadiness InspectProductionReadiness()
    {
        var canonical = Environment.GetEnvironmentVariable(CanonicalEnvironmentVariable)?.Trim() ?? string.Empty;
        var legacy = Environment.GetEnvironmentVariable(LegacyEnvironmentVariable)?.Trim() ?? string.Empty;
        var root = ResolveRoot();
        var canonicalConfigured = canonical.Length > 0;
        var legacyInUse = !canonicalConfigured && legacy.Length > 0;
        var exists = Directory.Exists(root);
        var ephemeral = IsKnownEphemeral(root);
        var sharedPersistent = Boolean(SharedPersistenceEnvironmentVariable);
        var writable = exists && ProbeWritable(root);
        var blockers = new List<string>();

        if (!canonicalConfigured)
            blockers.Add($"{CanonicalEnvironmentVariable} must explicitly identify the shared persistent upload mount.");
        if (legacyInUse)
            blockers.Add($"Legacy {LegacyEnvironmentVariable} is not accepted as production persistence evidence.");
        if (!exists)
            blockers.Add("The configured upload root does not exist in this runtime.");
        if (exists && !writable)
            blockers.Add("The configured upload root is not writable by the API runtime identity.");
        if (ephemeral)
            blockers.Add("The configured upload root is a known temporary or memory-backed location.");
        if (!sharedPersistent)
            blockers.Add($"{SharedPersistenceEnvironmentVariable}=true is required after the platform team verifies a shared durable mount across replicas.");

        return new ProjectPulseUploadStorageReadiness(
            CanonicalRootConfigured: canonicalConfigured,
            LegacyRootInUse: legacyInUse,
            RootExists: exists,
            RootWritable: writable,
            KnownEphemeralLocation: ephemeral,
            SharedPersistentStorageAttested: sharedPersistent,
            ProductionReady: blockers.Count == 0,
            RootFingerprint: Fingerprint(root),
            Blockers: blockers);
    }

    private static bool ProbeWritable(string root)
    {
        var probe = Path.Combine(root, $".celar-ai-storage-probe-{Guid.NewGuid():N}");
        try
        {
            using var stream = new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { File.Delete(probe); }
            catch { /* A failed cleanup does not expose the configured path or change readiness. */ }
        }
    }

    private static bool IsKnownEphemeral(string root)
    {
        var normalized = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidates = new List<string>
        {
            Path.GetFullPath(Path.GetTempPath()),
            "/tmp",
            "/var/tmp",
            "/dev/shm",
            "/run"
        };

        return candidates.Any(candidate => IsSameOrChild(normalized, candidate));
    }

    private static bool IsSameOrChild(string path, string candidate)
    {
        var root = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Boolean(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value;

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();
}
