namespace ProjectTime.Api.Ai;

/// <summary>
/// Recognizes only the sealed container-local immutable snapshot shape created by
/// PulseAiImmutableDocumentSnapshot when the authoritative upload root is an SMB mount.
/// This does not authorize arbitrary temporary files: the job, lease-attempt, random
/// fence, SHA-256 filename, extension, source metadata, and non-reparse directory/file
/// shape must all match the worker's snapshot contract.
/// </summary>
public static class PulseAiImmutableProcessingSnapshotPolicy
{
    private const string LocalProcessingDirectoryName = "projectpulse-private-document-processing";

    public static bool IsTrustedLocalSnapshotPath(
        PulseAiAuthorizedDocumentSource source,
        string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        try
        {
            var processingRoot = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), LocalProcessingDirectoryName));
            var candidate = Path.GetFullPath(fullPath);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var prefix = processingRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, comparison)) return false;

            var relative = Path.GetRelativePath(processingRoot, candidate);
            var parts = relative.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 3) return false;

            if (!Guid.TryParseExact(parts[0], "N", out _)) return false;

            var attempt = parts[1].Split(
                '-',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (attempt.Length != 3
                || !long.TryParse(attempt[0], out var leaseGeneration)
                || leaseGeneration < 1
                || !Guid.TryParseExact(attempt[1], "N", out _)
                || !IsHex(attempt[2], 32))
            {
                return false;
            }

            var fileName = parts[2];
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var originalExtension = Path.GetExtension(source.OriginalFileName).ToLowerInvariant();
            if (!PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase)
                || !extension.Equals(originalExtension, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    source.StoredFileName,
                    fileName,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal)
                || !IsHex(Path.GetFileNameWithoutExtension(fileName), 64))
            {
                return false;
            }

            var jobDirectory = Path.Combine(processingRoot, parts[0]);
            var attemptDirectory = Path.Combine(jobDirectory, parts[1]);
            if (!IsPrivateDirectory(processingRoot)
                || !IsPrivateDirectory(jobDirectory)
                || !IsPrivateDirectory(attemptDirectory)
                || !IsRegularNonReparseFile(candidate))
            {
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsPrivateDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;
        var attributes = File.GetAttributes(path);
        return attributes.HasFlag(FileAttributes.Directory)
            && !attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static bool IsRegularNonReparseFile(string path)
    {
        if (!File.Exists(path)) return false;
        var attributes = File.GetAttributes(path);
        return !attributes.HasFlag(FileAttributes.Directory)
            && !attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static bool IsHex(string value, int exactLength) =>
        value.Length == exactLength && value.All(Uri.IsHexDigit);
}
