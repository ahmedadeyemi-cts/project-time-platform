using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>Reads the existing Module 065 services record and its existing secret
/// envelope. No SSO/global credential fallback, settings write or second store.</summary>
internal sealed class MicrosoftTeamsServicesSnapshot
{
    internal sealed record Metadata(string Environment, string Key, Guid TenantId, Guid ClientId, string RecipientBoundary, long Revision);
    internal Metadata Profile { get; }
    internal string Secret { get; }
    private string SecretVersion { get; }
    private MicrosoftTeamsServicesSnapshot(Metadata profile, string secret, string version)
    { Profile = profile; Secret = secret; SecretVersion = version; }
    public override string ToString() => "Module 065 services snapshot (credential redacted)";
    internal bool Matches(MicrosoftTeamsServicesSnapshot other) => Profile == other.Profile && SecretVersion == other.SecretVersion;
    private const string Marker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";

    internal static Metadata ParseMetadata(string raw, string environment, long revision)
    {
        if (environment is not ("test" or "production") || raw.Length > 1024 * 1024)
            throw new InvalidDataException("Invalid Module 065 environment or document size");
        using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 32 });
        var configuration = document.RootElement.GetProperty("configuration");
        var notes = MicrosoftTeamsNotificationProtocol.Text(configuration, "notes");
        if (!notes.StartsWith(Marker, StringComparison.Ordinal)) throw new InvalidDataException("Save the Module 065 services profile first");
        using var saved = JsonDocument.Parse(notes[Marker.Length..], new JsonDocumentOptions { MaxDepth = 32 });
        var tenants = saved.RootElement.GetProperty("tenants").EnumerateArray()
            .Where(t => MicrosoftTeamsNotificationProtocol.Text(t, "environmentMode").Equals(environment, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (tenants.Length != 1) throw new InvalidDataException("Exactly one services profile must match the runtime environment");
        var tenant = tenants[0];
        var key = MicrosoftTeamsNotificationProtocol.Text(tenant, "key");
        if (key.Length == 0) key = MicrosoftTeamsNotificationProtocol.Text(tenant, "tenantKey");
        if (key.Length is < 1 or > 80 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')))
            throw new InvalidDataException("Invalid services tenant key");
        if (!Guid.TryParse(MicrosoftTeamsNotificationProtocol.Text(tenant, "tenantId"), out var tenantId) || tenantId == Guid.Empty
            || !tenant.TryGetProperty("services", out var services)
            || !Guid.TryParse(MicrosoftTeamsNotificationProtocol.Text(services, "clientId"), out var clientId) || clientId == Guid.Empty)
            throw new InvalidDataException("The environment services application is incomplete");
        if (!tenant.TryGetProperty("mail", out var mail)) saved.RootElement.TryGetProperty("mail", out mail);
        var boundary = MicrosoftTeamsNotificationProtocol.Text(mail, "recipientBoundary");
        if (boundary is not ("test_only" or "production_governed")) boundary = "locked";
        return new(environment, key.ToLowerInvariant(), tenantId, clientId, boundary, revision);
    }

    internal static async Task<Metadata> LoadMetadataAsync(NpgsqlConnection connection, string environment, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT document_json::text,revision_number FROM projectpulse_native_admin_documents WHERE module_number='065' AND document_key='configuration'", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidDataException("The stored Module 065 services profile is missing");
        return ParseMetadata(reader.GetString(0), environment, reader.GetInt64(1));
    }

    internal static async Task<MicrosoftTeamsServicesSnapshot> LoadAsync(NpgsqlConnection connection, string environment, CancellationToken ct)
    {
        var profile = await LoadMetadataAsync(connection, environment, ct);
        await using var command = new NpgsqlCommand("SELECT ciphertext,nonce,authentication_tag FROM microsoft_integration_client_secrets WHERE tenant_key=@key", connection);
        command.Parameters.AddWithValue("key", profile.Key);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            // Exact existing Module 065 AES-GCM envelope, key derivation and AAD.
            // A broken saved envelope fails closed instead of using a stale secret.
            var ciphertext = (byte[])reader[0];
            var nonce = (byte[])reader[1];
            var tag = (byte[])reader[2];
            var configured = Environment.GetEnvironmentVariable("PROJECTPULSE_MICROSOFT_INTEGRATION_SECRET_KEY");
            if (string.IsNullOrWhiteSpace(configured)) configured = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidDataException("Module 065 credential encryption key is unavailable");
            var secret = Decrypt(ciphertext, nonce, tag, configured, profile.Key);
            return new(profile, secret, Convert.ToHexString(SHA256.HashData(ciphertext)));
        }
        // Retain only explicitly environment-bound deployment credentials. Never
        // fall back to shared M365 or SSO variables that another profile can change.
        var prefix = environment == "test" ? "PROJECTPULSE_ENTRA_TEST_" : "PROJECTPULSE_ENTRA_PRODUCTION_";
        if (Guid.TryParse(Environment.GetEnvironmentVariable(prefix + "TENANT_ID"), out var boundTenant)
            && boundTenant == profile.TenantId && Guid.TryParse(Environment.GetEnvironmentVariable(prefix + "CLIENT_ID"), out var boundClient)
            && boundClient == profile.ClientId)
        {
            var secret = Environment.GetEnvironmentVariable(prefix + "CLIENT_SECRET");
            if (!string.IsNullOrWhiteSpace(secret))
                return new(profile, secret, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        }
        throw new InvalidDataException("Save the environment-specific Microsoft services credential in Module 065");
    }

    internal static string Decrypt(byte[] ciphertext, byte[] nonce, byte[] tag, string configured, string keyName)
    {
        if (ciphertext.Length is < 8 or > 16384 || nonce.Length != 12 || tag.Length != 16)
            throw new InvalidDataException("Invalid stored credential envelope");
        byte[]? key = null;
        try { key = Convert.FromBase64String(configured); } catch (FormatException) { }
        if (key?.Length != 32)
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            key = SHA256.HashData(Encoding.UTF8.GetBytes($"ProjectPulse-Microsoft-Integration:{configured}"));
        }
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes($"ProjectPulse:065:{keyName}"));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(key); }
    }
}
