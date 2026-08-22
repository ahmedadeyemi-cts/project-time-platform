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
    bool ReadOnlyAttestationValid,
    bool WriteDeleteProbeVerified,
    string VerificationMode,
    bool ProductionReady,
    string RootFingerprint,
    IReadOnlyList<string> Blockers);

public static class ProjectPulseUploadStorage
{
    public const string CanonicalEnvironmentVariable = "PROJECTPULSE_UPLOAD_ROOT";
    public const string LegacyEnvironmentVariable = "PROJECT_PULSE_UPLOAD_ROOT";
    public const string SharedPersistenceEnvironmentVariable = "PROJECTPULSE_UPLOAD_ROOT_SHARED_PERSISTENT";
    public const string AttestationFileEnvironmentVariable = "PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_FILE";
    public const string AttestationShaEnvironmentVariable = "PROJECTPULSE_UPLOAD_ROOT_ATTESTATION_SHA256";
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
    /// Resolves an existing project document against the current shared upload
    /// mount. Database rows may contain an absolute path written by an older API
    /// revision, so the durable work-register identity is used to relocate the
    /// file without trusting an arbitrary path from the database.
    /// </summary>
    public static string? ResolveExistingStoredFile(
        string? storedFilePath,
        Guid projectId,
        Guid documentId)
    {
        var root = ResolveRoot();
        if (!Directory.Exists(root)) return null;

        var candidates = new List<string>();
        var projectFolder = Path.Combine(
            root,
            "work-register-documents",
            projectId.ToString("N"));

        if (!string.IsNullOrWhiteSpace(storedFilePath))
        {
            var stored = storedFilePath.Trim();
            try
            {
                var absolute = Path.GetFullPath(stored);
                if (IsSameOrChild(absolute, root)) candidates.Add(absolute);
            }
            catch
            {
                // A malformed legacy path is ignored; durable identity candidates
                // below are still evaluated.
            }

            var normalized = stored.Replace('\\', '/');
            const string marker = "/work-register-documents/";
            var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                var relative = normalized[(markerIndex + 1)..]
                    .Replace('/', Path.DirectorySeparatorChar);
                try
                {
                    var relocated = Path.GetFullPath(Path.Combine(root, relative));
                    if (IsSameOrChild(relocated, root)) candidates.Add(relocated);
                }
                catch
                {
                    // Ignore an invalid relative legacy path.
                }
            }

            var fileName = Path.GetFileName(stored);
            if (!string.IsNullOrWhiteSpace(fileName))
                candidates.Add(Path.Combine(projectFolder, fileName));
        }

        if (Directory.Exists(projectFolder))
        {
            var prefix = documentId.ToString("N") + "_";
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(
                    projectFolder,
                    prefix + "*",
                    SearchOption.TopDirectoryOnly));
            }
            catch
            {
                // The caller receives a durable-storage missing response.
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (!IsSameOrChild(fullPath, root) || !File.Exists(fullPath)) continue;
                var file = new FileInfo(fullPath);
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length <= 0) continue;
                return fullPath;
            }
            catch
            {
                // Continue to the next identity-derived candidate.
            }
        }

        return null;
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
        var candidate = ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate;
        var readOnlyAttestationValid = candidate && exists && VerifyReadOnlyAttestation(root);
        var writeDeleteProbeVerified = !candidate && exists && ProbeWriteAndDelete(root);
        var writable = writeDeleteProbeVerified;
        var verificationMode = candidate
            ? "candidate_read_only_platform_attested"
            : "active_runtime_write_probe";
        var blockers = new List<string>();

        if (!canonicalConfigured)
            blockers.Add($"{CanonicalEnvironmentVariable} must explicitly identify the shared persistent upload mount.");
        if (legacyInUse)
            blockers.Add($"Legacy {LegacyEnvironmentVariable} is not accepted as production persistence evidence.");
        if (!exists)
            blockers.Add("The configured upload root does not exist in this runtime.");
        if (exists && !candidate && !writable)
            blockers.Add("The configured upload root is not writable by the API runtime identity.");
        if (exists && candidate && !readOnlyAttestationValid)
            blockers.Add($"Candidate storage requires an unchanged read-only canary matching {AttestationShaEnvironmentVariable}.");
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
            ReadOnlyAttestationValid: readOnlyAttestationValid,
            WriteDeleteProbeVerified: writeDeleteProbeVerified,
            VerificationMode: verificationMode,
            ProductionReady: blockers.Count == 0,
            RootFingerprint: Fingerprint(root),
            Blockers: blockers);
    }

    private static bool VerifyReadOnlyAttestation(string root)
    {
        var name = Environment.GetEnvironmentVariable(AttestationFileEnvironmentVariable)?.Trim() ?? string.Empty;
        var expected = Environment.GetEnvironmentVariable(AttestationShaEnvironmentVariable)?.Trim().ToLowerInvariant() ?? string.Empty;
        if (name.Length == 0
            || name != Path.GetFileName(name)
            || expected.Length != 64
            || expected.Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            return false;
        }

        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!IsSameOrChild(path, root) || !File.Exists(path)) return false;
        try
        {
            var before = new FileInfo(path);
            if ((before.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            var beforeLength = before.Length;
            var beforeWrite = before.LastWriteTimeUtc;
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16 * 1024, options: FileOptions.SequentialScan);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var after = new FileInfo(path);
            return CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected))
                && after.Length == beforeLength
                && after.LastWriteTimeUtc == beforeWrite;
        }
        catch
        {
            return false;
        }
    }

    private static bool ProbeWriteAndDelete(string root)
    {
        var probe = Path.Combine(root, $".celar-ai-storage-probe-{Guid.NewGuid():N}");
        var created = false;
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
            created = true;
            stream.Dispose();
            File.Delete(probe);
            return !File.Exists(probe);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (created && File.Exists(probe))
            {
                try { File.Delete(probe); }
                catch { /* Readiness already failed because deletion was not proven. */ }
            }
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
