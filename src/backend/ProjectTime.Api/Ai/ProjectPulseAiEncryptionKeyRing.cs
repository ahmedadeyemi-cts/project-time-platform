using System.Security.Cryptography;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Loads the Module 064 AES-256-GCM key ring from process configuration. Key IDs
/// are safe metadata; key bytes never leave this type and are never formatted or
/// logged. The legacy single-key variable remains the active-key source so an
/// existing installation can adopt key IDs before its first rotation.
/// </summary>
public sealed class ProjectPulseAiEncryptionKeyRing : IDisposable
{
    public const string MigrationId = "071_ai_runtime_production_hardening";
    private const string LegacyKeyId = "legacy-v1";
    private readonly Dictionary<string, byte[]> _keys;

    private ProjectPulseAiEncryptionKeyRing(string activeKeyId, Dictionary<string, byte[]> keys)
    {
        ActiveKeyId = activeKeyId;
        _keys = keys;
    }

    public string ActiveKeyId { get; }
    public bool Available => _keys.TryGetValue(ActiveKeyId, out var key) && key.Length == 32;
    public IReadOnlyCollection<string> KeyIds => _keys.Keys.ToArray();

    public static ProjectPulseAiEncryptionKeyRing Load()
    {
        var activeKeyId = NormalizeKeyId(
            Environment.GetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID"),
            LegacyKeyId);
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        var serialized = Environment.GetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING");
        if (!string.IsNullOrWhiteSpace(serialized))
        {
            try
            {
                var configured = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized) ?? [];
                foreach (var item in configured)
                    AddKey(keys, NormalizeKeyId(item.Key, string.Empty), item.Value);
            }
            catch (JsonException)
            {
                // Fail closed. Configuration errors are reported as missing key
                // IDs without echoing the JSON or any key material.
            }
        }

        // The dedicated active-key variable is authoritative if the ring JSON
        // repeats the same ID. AddKey zeroes the replaced buffer first.
        AddKey(keys, activeKeyId, Environment.GetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY"));

        return new ProjectPulseAiEncryptionKeyRing(activeKeyId, keys);
    }

    public byte[] ActiveKey() => Key(ActiveKeyId);

    public byte[] Key(string keyId)
    {
        var normalized = NormalizeKeyId(keyId, string.Empty);
        if (!_keys.TryGetValue(normalized, out var key) || key.Length != 32)
            throw new CryptographicException("The ciphertext encryption key ID is not present in the configured Module 064 key ring.");
        return key;
    }

    public void Dispose()
    {
        foreach (var key in _keys.Values)
            CryptographicOperations.ZeroMemory(key);
        _keys.Clear();
    }

    private static void AddKey(IDictionary<string, byte[]> keys, string keyId, string? encoded)
    {
        if (keyId.Length == 0 || string.IsNullOrWhiteSpace(encoded)) return;
        try
        {
            var key = Convert.FromBase64String(encoded.Trim());
            if (key.Length == 32)
            {
                if (keys.TryGetValue(keyId, out var previous))
                    CryptographicOperations.ZeroMemory(previous);
                keys[keyId] = key;
            }
            else CryptographicOperations.ZeroMemory(key);
        }
        catch (FormatException)
        {
            // Invalid entries are omitted; callers fail closed by key ID.
        }
    }

    private static string NormalizeKeyId(string? value, string fallback)
    {
        var normalized = new string((value ?? string.Empty)
            .Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Take(120)
            .ToArray());
        return normalized.Length == 0 ? fallback : normalized;
    }
}
