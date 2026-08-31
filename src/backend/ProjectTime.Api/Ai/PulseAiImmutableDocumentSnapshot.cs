using System.Buffers;
using System.Security.Cryptography;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiImmutableDocumentSnapshot : IAsyncDisposable
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode SealedDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserExecute;
    private const UnixFileMode ReadOnlyFileMode = UnixFileMode.UserRead;
    private const string LocalProcessingDirectoryName = "projectpulse-private-document-processing";

    private readonly FileStream _guardian;
    private readonly string _directoryPath;
    private readonly string _jobDirectoryPath;
    private bool _disposed;

    private PulseAiImmutableDocumentSnapshot(
        PulseAiAuthorizedDocumentSource source,
        string sourceSha256,
        string directoryPath,
        string jobDirectoryPath,
        FileStream guardian)
    {
        Source = source;
        SourceSha256 = sourceSha256;
        _directoryPath = directoryPath;
        _jobDirectoryPath = jobDirectoryPath;
        _guardian = guardian;
    }

    public PulseAiAuthorizedDocumentSource Source { get; }
    public string SourceSha256 { get; }

    public static async Task<PulseAiImmutableDocumentSnapshot> CreateAsync(
        PulseAiAuthorizedDocumentSource source,
        string uploadRoot,
        Guid jobId,
        Guid leaseToken,
        long leaseGeneration,
        long maximumFileBytes,
        CancellationToken cancellationToken)
    {
        string? jobRoot = null;
        string? directory = null;
        string? partialPath = null;
        string? snapshotPath = null;

        try
        {
            var root = Path.GetFullPath(uploadRoot);
            var sourcePath = Path.GetFullPath(source.StoragePath);
            var normalizedRoot = root.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!sourcePath.StartsWith(
                    normalizedRoot,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                throw new PulseAiDocumentSnapshotException("document_snapshot_path_rejected");
            }

            var extension = Path.GetExtension(source.OriginalFileName).ToLowerInvariant();
            if (!PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new PulseAiDocumentSnapshotException("document_snapshot_format_rejected");
            }

            VerifyRegularNonReparseDirectory(root);
            VerifyRegularNonReparseFile(sourcePath);

            // The authoritative upload root may be an Azure Files SMB mount. SMB
            // permissions are mount-level by default, so exact per-file chmod
            // verification is not a reliable isolation primitive there. Keep the
            // durable source on the governed shared mount, but place the transient
            // immutable worker copy on the container-local filesystem when the
            // authoritative mount reports SMB/CIFS semantics. The copy is still
            // SHA-bound, lease-bound, non-reparse, read-only, guarded while open,
            // and deleted after processing.
            var processingRoot = ResolveProcessingRoot(root);
            jobRoot = Path.Combine(processingRoot, jobId.ToString("N"));
            directory = Path.Combine(
                jobRoot,
                $"{leaseGeneration}-{leaseToken:N}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}");
            partialPath = Path.Combine(directory, $"{Guid.NewGuid():N}.partial");
            CreateAndVerifyPrivateDirectory(processingRoot);
            CreateAndVerifyPrivateDirectory(jobRoot);
            CreateAndVerifyPrivateDirectory(directory);
            var normalizedProcessingRoot = Path.GetFullPath(processingRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedDirectory = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            if (!normalizedDirectory.StartsWith(
                    normalizedProcessingRoot,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                throw new PulseAiDocumentSnapshotException("document_snapshot_path_rejected");
            }

            long copied = 0;
            string sourceSha256;
            await using (var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
                try
                {
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                        if (read == 0) break;
                        copied = checked(copied + read);
                        if (copied > maximumFileBytes)
                            throw new PulseAiDocumentSnapshotException("document_snapshot_size_rejected");
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }

                    if (copied == 0)
                        throw new PulseAiDocumentSnapshotException("document_snapshot_size_rejected");
                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                    sourceSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }
            }

            snapshotPath = Path.Combine(directory, $"{sourceSha256}{extension}");
            File.Move(partialPath, snapshotPath);
            SetReadOnlyFileMode(snapshotPath);
            SealDirectory(directory);

            var guardian = new FileStream(
                snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshotSource = source with
            {
                StoredFileName = Path.GetFileName(snapshotPath),
                StoragePath = snapshotPath,
                SizeBytes = copied
            };
            return new PulseAiImmutableDocumentSnapshot(
                snapshotSource,
                sourceSha256,
                directory,
                jobRoot,
                guardian);
        }
        catch (OperationCanceledException)
        {
            Cleanup(snapshotPath, partialPath, directory);
            TryDeleteEmptyDirectory(jobRoot);
            throw;
        }
        catch (PulseAiDocumentSnapshotException)
        {
            Cleanup(snapshotPath, partialPath, directory);
            TryDeleteEmptyDirectory(jobRoot);
            throw;
        }
        catch (Exception exception)
        {
            Cleanup(snapshotPath, partialPath, directory);
            TryDeleteEmptyDirectory(jobRoot);
            throw new PulseAiDocumentSnapshotException(
                "document_snapshot_creation_failed",
                exception);
        }
    }

    public static async Task CleanupOrphansAsync(
        string uploadRoot,
        int maximumDirectories,
        Func<Guid, Guid, long, CancellationToken, Task<bool>> hasLiveLeaseAsync,
        CancellationToken cancellationToken)
    {
        maximumDirectories = Math.Clamp(maximumDirectories, 1, 128);
        try
        {
            var root = Path.GetFullPath(uploadRoot);
            VerifyRegularNonReparseDirectory(root);
            var processingRoot = ResolveProcessingRoot(root);
            if (!Directory.Exists(processingRoot)) return;
            VerifyRegularNonReparseDirectory(processingRoot);

            var examinedJobDirectories = 0;
            var examinedAttemptDirectories = 0;
            foreach (var jobDirectory in Directory.EnumerateDirectories(processingRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (examinedJobDirectories++ >= maximumDirectories) break;
                var jobName = Path.GetFileName(jobDirectory);
                if (!Guid.TryParseExact(jobName, "N", out var jobId)) continue;
                VerifyRegularNonReparseDirectory(jobDirectory);

                foreach (var attemptDirectory in Directory.EnumerateDirectories(jobDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (examinedAttemptDirectories++ >= maximumDirectories) break;
                    if (!TryParseAttemptDirectory(
                            Path.GetFileName(attemptDirectory),
                            out var leaseGeneration,
                            out var leaseToken))
                    {
                        continue;
                    }
                    VerifyRegularNonReparseDirectory(attemptDirectory);

                    if (await hasLiveLeaseAsync(
                            jobId,
                            leaseToken,
                            leaseGeneration,
                            cancellationToken))
                    {
                        continue;
                    }

                    DeleteVerifiedSnapshotDirectory(attemptDirectory);
                }

                TryDeleteEmptyDirectory(jobDirectory);
                if (examinedAttemptDirectories >= maximumDirectories) break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PulseAiDocumentSnapshotException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PulseAiDocumentSnapshotException(
                "document_snapshot_cleanup_unavailable",
                exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await _guardian.DisposeAsync();
        }
        catch (Exception) { }
        finally
        {
            Cleanup(Source.StoragePath, partialPath: null, _directoryPath);
            TryDeleteEmptyDirectory(_jobDirectoryPath);
        }
    }

    private static string ResolveProcessingRoot(string authoritativeRoot)
    {
        var root = Path.GetFullPath(authoritativeRoot);
        return UsesMountLevelPermissions(root)
            ? Path.Combine(Path.GetTempPath(), LocalProcessingDirectoryName)
            : Path.Combine(root, ".pulse-ai-processing");
    }

    private static bool UsesMountLevelPermissions(string root)
    {
        if (!OperatingSystem.IsLinux()) return false;
        const string mountInfoPath = "/proc/self/mountinfo";
        if (!File.Exists(mountInfoPath)) return false;

        try
        {
            string? bestMount = null;
            string? bestFileSystem = null;
            foreach (var line in File.ReadLines(mountInfoPath))
            {
                var separator = line.IndexOf(" - ", StringComparison.Ordinal);
                if (separator < 0) continue;

                var before = line[..separator]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (before.Length < 5) continue;
                var after = line[(separator + 3)..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (after.Length == 0) continue;

                var mountPoint = DecodeMountInfoPath(before[4]);
                if (!IsSameOrChild(root, mountPoint)) continue;
                if (bestMount is not null && mountPoint.Length <= bestMount.Length) continue;

                bestMount = mountPoint;
                bestFileSystem = after[0].Trim().ToLowerInvariant();
            }

            return bestFileSystem is "cifs" or "smb3" or "smbfs"
                || (bestFileSystem?.StartsWith("fuse.smb", StringComparison.Ordinal) ?? false);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSameOrChild(string path, string candidate)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullCandidate = Path.GetFullPath(candidate);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (fullPath.Equals(fullCandidate, comparison)) return true;
            var prefix = fullCandidate.EndsWith(Path.DirectorySeparatorChar)
                || fullCandidate.EndsWith(Path.AltDirectorySeparatorChar)
                ? fullCandidate
                : fullCandidate + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, comparison);
        }
        catch
        {
            return false;
        }
    }

    private static string DecodeMountInfoPath(string value) =>
        value.Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);

    private static void CreateAndVerifyPrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        VerifyRegularNonReparseDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, PrivateDirectoryMode);
            if (File.GetUnixFileMode(path) != PrivateDirectoryMode)
                throw new PulseAiDocumentSnapshotException("document_snapshot_permissions_rejected");
        }
    }

    private static void VerifyRegularNonReparseDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if (!attributes.HasFlag(FileAttributes.Directory)
            || attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new PulseAiDocumentSnapshotException("document_snapshot_path_rejected");
        }
    }

    private static void VerifyRegularNonReparseFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.Directory)
            || attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new PulseAiDocumentSnapshotException("document_snapshot_path_rejected");
        }
    }

    private static void SealDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, SealedDirectoryMode);
        if (File.GetUnixFileMode(path) != SealedDirectoryMode)
            throw new PulseAiDocumentSnapshotException("document_snapshot_permissions_rejected");
    }

    private static void SetReadOnlyFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, ReadOnlyFileMode);
            if (File.GetUnixFileMode(path) != ReadOnlyFileMode)
                throw new PulseAiDocumentSnapshotException("document_snapshot_permissions_rejected");
        }
        else
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            if (!File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                throw new PulseAiDocumentSnapshotException("document_snapshot_permissions_rejected");
        }
    }

    private static bool TryParseAttemptDirectory(
        string name,
        out long leaseGeneration,
        out Guid leaseToken)
    {
        leaseGeneration = 0;
        leaseToken = Guid.Empty;
        var parts = name.Split('-', StringSplitOptions.None);
        return parts.Length == 3
            && long.TryParse(parts[0], out leaseGeneration)
            && leaseGeneration >= 0
            && Guid.TryParseExact(parts[1], "N", out leaseToken)
            && leaseToken != Guid.Empty
            && parts[2].Length == 32
            && parts[2].All(Uri.IsHexDigit);
    }

    private static void DeleteVerifiedSnapshotDirectory(string directory)
    {
        if (Directory.EnumerateDirectories(directory).Any())
            throw new PulseAiDocumentSnapshotException("document_snapshot_cleanup_path_rejected");

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, PrivateDirectoryMode);

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var attributes = File.GetAttributes(file);
            if (attributes.HasFlag(FileAttributes.Directory)
                || attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new PulseAiDocumentSnapshotException("document_snapshot_cleanup_path_rejected");
            }

            var name = Path.GetFileName(file);
            var extension = Path.GetExtension(name).ToLowerInvariant();
            var stem = Path.GetFileNameWithoutExtension(name);
            var validPartial = extension == ".partial"
                && Guid.TryParseExact(stem, "N", out _);
            var validSnapshot = PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase)
                && stem.Length == 64
                && stem.All(Uri.IsHexDigit);
            if (!validPartial && !validSnapshot)
                throw new PulseAiDocumentSnapshotException("document_snapshot_cleanup_path_rejected");

            if (OperatingSystem.IsWindows()) File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
            if (File.Exists(file))
                throw new PulseAiDocumentSnapshotException("document_snapshot_cleanup_failed");
        }

        Directory.Delete(directory, recursive: false);
        if (Directory.Exists(directory))
            throw new PulseAiDocumentSnapshotException("document_snapshot_cleanup_failed");
    }

    private static void Cleanup(string? snapshotPath, string? partialPath, string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory) && !OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(directory, PrivateDirectoryMode); }
            catch (Exception) { }
        }
        TryDeleteFile(snapshotPath);
        TryDeleteFile(partialPath);
        if (string.IsNullOrWhiteSpace(directory)) return;
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmptyDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (OperatingSystem.IsWindows() && File.Exists(path))
                File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class PulseAiDocumentSnapshotException : Exception
{
    public PulseAiDocumentSnapshotException(string diagnosticCode, Exception? innerException = null)
        : base("The private document processing snapshot could not be established.", innerException)
    {
        DiagnosticCode = diagnosticCode;
    }

    public string DiagnosticCode { get; }
}
