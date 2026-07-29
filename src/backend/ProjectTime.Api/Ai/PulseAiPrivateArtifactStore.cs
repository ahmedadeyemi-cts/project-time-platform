using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateArtifactStore
{
    private const string Magic = "PULSEAI1";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private readonly ILogger<PulseAiPrivateArtifactStore> _logger;

    public PulseAiPrivateArtifactStore(ILogger<PulseAiPrivateArtifactStore> logger)
    {
        _logger = logger;
    }

    public async Task<PulseAiPrivateArtifactReceipt> WriteJsonAsync<T>(
        Guid versionId,
        string artifactKind,
        T payload,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactKind);
        if (!options.TryEncryptionKey(out var key))
        {
            throw new InvalidOperationException("Pulse AI private artifact encryption is not configured.");
        }

        var plain = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var plainHash = Convert.ToHexString(SHA256.HashData(plain)).ToLowerInvariant();
        var safeKind = SafeSegment(artifactKind);
        var directory = ConfinedDirectory(options.ArtifactRoot, versionId.ToString("N"), safeKind);
        Directory.CreateDirectory(directory);
        RejectReparsePoint(directory);

        var fileName = $"{plainHash}.pulseai";
        var path = Path.Combine(directory, fileName);
        var fullPath = Path.GetFullPath(path);
        EnsureUnderRoot(fullPath, options.ArtifactRoot);

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var tag = new byte[TagBytes];
        var cipher = new byte[plain.Length];
        var aad = Encoding.UTF8.GetBytes($"{versionId:N}|{safeKind}|{options.ArtifactEncryptionKeyVersion}|{plainHash}");

        using (var aes = new AesGcm(key, TagBytes))
        {
            aes.Encrypt(nonce, plain, cipher, tag, aad);
        }

        var header = Encoding.ASCII.GetBytes(Magic);
        var envelope = new byte[header.Length + NonceBytes + TagBytes + cipher.Length];
        Buffer.BlockCopy(header, 0, envelope, 0, header.Length);
        Buffer.BlockCopy(nonce, 0, envelope, header.Length, NonceBytes);
        Buffer.BlockCopy(tag, 0, envelope, header.Length + NonceBytes, TagBytes);
        Buffer.BlockCopy(cipher, 0, envelope, header.Length + NonceBytes + TagBytes, cipher.Length);

        if (!File.Exists(fullPath))
        {
            var temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(temporary, envelope, cancellationToken);
            File.Move(temporary, fullPath, overwrite: false);
        }

        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plain);

        var retentionUntil = DateTimeOffset.UtcNow.AddDays(options.ArtifactRetentionDays);
        return new PulseAiPrivateArtifactReceipt(
            ArtifactKind: safeKind,
            StorageUri: $"pulse-artifact://{versionId:N}/{safeKind}/{fileName}",
            ArtifactSha256: plainHash,
            EncryptionAlgorithm: PulseAiPrivateDocumentRuntimePolicy.ArtifactEncryption,
            EncryptionKeyVersion: options.ArtifactEncryptionKeyVersion,
            ContentLengthBytes: envelope.LongLength,
            RetentionUntil: retentionUntil,
            CreatedAt: DateTimeOffset.UtcNow);
    }

    public async Task<T?> ReadJsonAsync<T>(
        Guid versionId,
        string storageUri,
        string artifactSha256,
        string artifactKind,
        PulseAiPrivateDocumentRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.TryEncryptionKey(out var key))
        {
            throw new InvalidOperationException("Pulse AI private artifact encryption is not configured.");
        }

        var path = ResolveStorageUri(versionId, storageUri, artifactKind, options.ArtifactRoot);
        if (!File.Exists(path)) return default;
        RejectReparsePoint(path);

        var envelope = await File.ReadAllBytesAsync(path, cancellationToken);
        var headerLength = Encoding.ASCII.GetByteCount(Magic);
        if (envelope.Length < headerLength + NonceBytes + TagBytes)
        {
            throw new InvalidDataException("The Pulse AI private artifact envelope is incomplete.");
        }

        var magic = Encoding.ASCII.GetString(envelope, 0, headerLength);
        if (!string.Equals(magic, Magic, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Pulse AI private artifact format is not recognized.");
        }

        var nonce = envelope.AsSpan(headerLength, NonceBytes).ToArray();
        var tag = envelope.AsSpan(headerLength + NonceBytes, TagBytes).ToArray();
        var cipher = envelope.AsSpan(headerLength + NonceBytes + TagBytes).ToArray();
        var plain = new byte[cipher.Length];
        var safeKind = SafeSegment(artifactKind);
        var aad = Encoding.UTF8.GetBytes($"{versionId:N}|{safeKind}|{options.ArtifactEncryptionKeyVersion}|{artifactSha256}");

        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, plain, aad);
            var actual = Convert.ToHexString(SHA256.HashData(plain)).ToLowerInvariant();
            if (!actual.Equals(artifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CryptographicException("The Pulse AI private artifact checksum did not match its metadata.");
            }

            return JsonSerializer.Deserialize<T>(plain, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public bool Delete(
        Guid versionId,
        string storageUri,
        string artifactKind,
        PulseAiPrivateDocumentRuntimeOptions options)
    {
        try
        {
            var path = ResolveStorageUri(versionId, storageUri, artifactKind, options.ArtifactRoot);
            if (!File.Exists(path)) return true;
            RejectReparsePoint(path);
            File.Delete(path);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private artifact deletion failed without exposing its path. VersionId={VersionId} Kind={Kind}",
                versionId,
                artifactKind);
            return false;
        }
    }

    private static string ResolveStorageUri(Guid versionId, string storageUri, string artifactKind, string root)
    {
        if (!Uri.TryCreate(storageUri, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("pulse-artifact", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The private artifact reference is invalid.");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var hostVersion = uri.Host;
        if (!hostVersion.Equals(versionId.ToString("N"), StringComparison.OrdinalIgnoreCase)
            || segments.Length != 2
            || !segments[0].Equals(SafeSegment(artifactKind), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The private artifact reference does not match its version and kind.");
        }

        var fileName = Path.GetFileName(segments[1]);
        if (!fileName.Equals(segments[1], StringComparison.Ordinal))
        {
            throw new InvalidDataException("The private artifact file name is invalid.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, hostVersion, segments[0], fileName));
        EnsureUnderRoot(fullPath, root);
        return fullPath;
    }

    private static string ConfinedDirectory(string root, params string[] segments)
    {
        var full = Path.GetFullPath(Path.Combine([root, .. segments]));
        EnsureUnderRoot(full, root);
        return full;
    }

    private static void EnsureUnderRoot(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The private artifact path is outside the configured artifact root.");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Symbolic links and reparse points are not permitted in the private artifact store.");
        }
    }

    private static string SafeSegment(string value)
    {
        var clean = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray())
            .Trim('-');
        if (clean.Length == 0) throw new ArgumentException("Artifact kind is invalid.", nameof(value));
        return clean.Length <= 80 ? clean : clean[..80];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
}

internal sealed record PulseAiPrivateExtractionArtifactPayload(
    string ContractVersion,
    Guid DocumentId,
    Guid VersionId,
    string SourceSha256,
    string OriginalFileName,
    string DetectedFormat,
    string ExtractionMethod,
    IReadOnlyList<PulseAiExtractedSection> Sections,
    IReadOnlyList<PulseAiDocumentChunk> Chunks,
    DateTimeOffset GeneratedAt);
