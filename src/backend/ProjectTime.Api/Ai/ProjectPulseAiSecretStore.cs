using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class ProjectPulseAiSecretStore
{
    private const int MaximumSecretBytes = 8192;
    private readonly string? _connectionString;
    private readonly byte[]? _encryptionKey;
    private readonly ILogger<ProjectPulseAiSecretStore> _logger;

    public ProjectPulseAiSecretStore(ILogger<ProjectPulseAiSecretStore> logger)
    {
        _logger = logger;
        _connectionString = ConnectionString();
        _encryptionKey = ReadEncryptionKey();
    }

    public bool Available => _connectionString is not null && _encryptionKey is not null;
    public string UnavailableReason => _connectionString is null
        ? "Database configuration is unavailable."
        : _encryptionKey is null
            ? "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY must be a base64-encoded 32-byte key."
            : string.Empty;

    public async Task<IReadOnlyList<StoredSecret>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!Available) return [];
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        const string sql = "SELECT provider_code, ciphertext, nonce, tag, version, rotated_at FROM ai_provider_secrets;";
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StoredSecret>();
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                var providerCode = reader.GetString(0);
                result.Add(new StoredSecret(providerCode, Decrypt(providerCode, (byte[])reader[1], (byte[])reader[2], (byte[])reader[3]), reader.GetString(4), new DateTimeOffset(reader.GetDateTime(5).ToUniversalTime())));
            }
            catch (CryptographicException exception)
            {
                _logger.LogError(exception, "Module 064 could not decrypt the {Provider} provider secret.", reader.GetString(0));
            }
        }
        return result;
    }

    public async Task<StoredSecret> SaveAsync(string providerCode, string apiKey, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!Available) throw new InvalidOperationException(UnavailableReason);
        var secretBytes = Encoding.UTF8.GetByteCount(apiKey);
        if (secretBytes is < 1 or > MaximumSecretBytes) throw new ArgumentException($"API key must be between 1 and {MaximumSecretBytes} UTF-8 bytes.");

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(apiKey);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(_encryptionKey!, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(providerCode));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }

        var version = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var rotatedAt = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string upsert = """
            INSERT INTO ai_provider_secrets (provider_code, ciphertext, nonce, tag, version, rotated_at, rotated_by)
            VALUES (@provider, @ciphertext, @nonce, @tag, @version, @rotated_at, @actor)
            ON CONFLICT (provider_code) DO UPDATE SET ciphertext = EXCLUDED.ciphertext, nonce = EXCLUDED.nonce,
                tag = EXCLUDED.tag, version = EXCLUDED.version, rotated_at = EXCLUDED.rotated_at, rotated_by = EXCLUDED.rotated_by;
            """;
        await using (var command = new NpgsqlCommand(upsert, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerCode);
            command.Parameters.AddWithValue("ciphertext", ciphertext);
            command.Parameters.AddWithValue("nonce", nonce);
            command.Parameters.AddWithValue("tag", tag);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("rotated_at", rotatedAt);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string audit = "INSERT INTO ai_provider_secret_audit (provider_code, action, version, actor_user_id) VALUES (@provider, 'replaced', @version, @actor);";
        await using (var command = new NpgsqlCommand(audit, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerCode);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new StoredSecret(providerCode, apiKey, version, rotatedAt);
    }

    private async Task EnsureSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS ai_provider_secrets (
                provider_code TEXT PRIMARY KEY CHECK (provider_code IN ('claude','openai')),
                ciphertext BYTEA NOT NULL, nonce BYTEA NOT NULL, tag BYTEA NOT NULL,
                version TEXT NOT NULL, rotated_at TIMESTAMPTZ NOT NULL, rotated_by UUID NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ai_provider_secret_audit (
                audit_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                provider_code TEXT NOT NULL, action TEXT NOT NULL, version TEXT NOT NULL,
                actor_user_id UUID NOT NULL, occurred_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string Decrypt(string providerCode, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_encryptionKey!, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(providerCode));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static byte[]? ReadEncryptionKey()
    {
        try
        {
            var value = Environment.GetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY");
            if (string.IsNullOrWhiteSpace(value)) return null;
            var key = Convert.FromBase64String(value.Trim());
            return key.Length == 32 ? key : null;
        }
        catch (FormatException) { return null; }
    }

    private static string? ConnectionString() => new[] { "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse", "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING", "PROJECTTIME_DATABASE_CONNECTION", "PROJECTPULSE_DB_CONNECTION", "PROJECTTIME_DB_CONNECTION" }
        .Select(Environment.GetEnvironmentVariable).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    public sealed record StoredSecret(string ProviderCode, string ApiKey, string Version, DateTimeOffset RotatedAt);
}

public sealed class ProjectPulseAiSecretLoader(ProjectPulseAiSecretStore store, ProjectPulseAiConfiguration configuration, ILogger<ProjectPulseAiSecretLoader> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!store.Available) { logger.LogWarning("Module 064 write-only secret store is unavailable: {Reason}", store.UnavailableReason); return; }
        foreach (var secret in await store.LoadAsync(cancellationToken)) configuration.ApplyStoredSecret(secret.ProviderCode, secret.ApiKey, secret.Version, secret.RotatedAt);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
