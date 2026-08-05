using System.Data;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed record ProjectPulseAiEncryptionRotationRequest(
    string? ExpectedCurrentKeyId,
    string? ExpectedActiveKeyId,
    string? Confirmation);

public sealed record ProjectPulseAiEncryptionRotationResult(
    string PreviousKeyId,
    string ActiveKeyId,
    int PublicProviderSecretsRotated,
    bool PrivateProfileRotated,
    DateTimeOffset RotatedAt,
    Guid ActorUserId);

/// <summary>
/// Performs one atomic key-ID-fenced rotation across every Module 064 encrypted
/// store. Secret plaintext exists only in zeroed process buffers inside the
/// serializable transaction and is never included in logs, exceptions, or API
/// responses.
/// </summary>
public sealed class ProjectPulseAiEncryptionRotationService
{
    public const string Confirmation = "ROTATE-PROJECTPULSE-AI-ENCRYPTION-KEY";

    public async Task<ProjectPulseAiEncryptionRotationResult> RotateAsync(
        ProjectPulseAiEncryptionRotationRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ProjectPulseAiReleaseRuntimePolicy.RejectReleaseConfigurationMutation("AI encryption-key rotation");
        if (!string.Equals(request.Confirmation?.Trim(), Confirmation, StringComparison.Ordinal))
            throw new ArgumentException($"Confirmation must exactly match {Confirmation}.");
        var expectedCurrent = CleanKeyId(request.ExpectedCurrentKeyId);
        var expectedActive = CleanKeyId(request.ExpectedActiveKeyId);
        if (expectedCurrent.Length == 0 || expectedActive.Length == 0 || expectedCurrent == expectedActive)
            throw new ArgumentException("Distinct expected current and active encryption key IDs are required.");
        var connectionString = ProjectPulseAiDatabaseConnection.Resolve();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database configuration is unavailable.");

        using var keyRing = ProjectPulseAiEncryptionKeyRing.Load();
        if (!keyRing.Available || !string.Equals(keyRing.ActiveKeyId, expectedActive, StringComparison.Ordinal))
            throw new InvalidOperationException("The configured active encryption key ID does not match the rotation request.");
        _ = keyRing.Key(expectedCurrent);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await RequireSchemaAsync(connection, transaction, cancellationToken);

        var providerRows = new List<ProviderRow>();
        const string providerSelect = """
            SELECT provider_code, ciphertext, nonce, tag, encryption_key_id, version
            FROM ai_provider_secrets
            ORDER BY provider_code
            FOR UPDATE;
            """;
        await using (var command = new NpgsqlCommand(providerSelect, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var keyId = reader.GetString(4);
                if (!string.Equals(keyId, expectedCurrent, StringComparison.Ordinal))
                    throw new InvalidOperationException("Rotation was refused because a public-provider secret has an unexpected key ID.");
                providerRows.Add(new ProviderRow(
                    reader.GetString(0),
                    (byte[])reader[1],
                    (byte[])reader[2],
                    (byte[])reader[3],
                    keyId,
                    reader.GetString(5)));
            }
        }

        var profiles = new List<PrivateProfileRow>();
        const string profileSelect = """
            SELECT environment_code, endpoint_ciphertext, endpoint_nonce, endpoint_tag,
                   endpoint_encryption_key_id, token_ciphertext, token_nonce, token_tag,
                   token_encryption_key_id, revision
            FROM ai_private_model_profiles
            FOR UPDATE;
            """;
        await using (var command = new NpgsqlCommand(profileSelect, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var endpointKeyId = reader.GetString(4);
                var tokenKeyId = reader.GetString(8);
                if (!string.Equals(endpointKeyId, expectedCurrent, StringComparison.Ordinal)
                    || !string.Equals(tokenKeyId, expectedCurrent, StringComparison.Ordinal))
                    throw new InvalidOperationException("Rotation was refused because the private profile has an unexpected key ID.");
                profiles.Add(new PrivateProfileRow(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : (byte[])reader[1],
                    reader.IsDBNull(2) ? null : (byte[])reader[2],
                    reader.IsDBNull(3) ? null : (byte[])reader[3],
                    endpointKeyId,
                    reader.IsDBNull(5) ? null : (byte[])reader[5],
                    reader.IsDBNull(6) ? null : (byte[])reader[6],
                    reader.IsDBNull(7) ? null : (byte[])reader[7],
                    tokenKeyId,
                    reader.GetInt32(9)));
            }
        }

        var rotatedAt = DateTimeOffset.UtcNow;
        foreach (var row in providerRows)
        {
            var plaintext = Decrypt(row.Provider, row.KeyId, row.Ciphertext, row.Nonce, row.Tag, keyRing);
            try
            {
                var encrypted = Encrypt(row.Provider, plaintext, keyRing.ActiveKey());
                const string update = """
                    UPDATE ai_provider_secrets
                    SET ciphertext = @ciphertext, nonce = @nonce, tag = @tag,
                        encryption_key_id = @active_key_id,
                        rotated_at = @rotated_at, rotated_by = @actor
                    WHERE provider_code = @provider AND encryption_key_id = @current_key_id;
                    """;
                await using var command = new NpgsqlCommand(update, connection, transaction);
                command.Parameters.AddWithValue("provider", row.Provider);
                command.Parameters.AddWithValue("ciphertext", encrypted.Ciphertext);
                command.Parameters.AddWithValue("nonce", encrypted.Nonce);
                command.Parameters.AddWithValue("tag", encrypted.Tag);
                command.Parameters.AddWithValue("active_key_id", expectedActive);
                command.Parameters.AddWithValue("current_key_id", expectedCurrent);
                command.Parameters.AddWithValue("rotated_at", rotatedAt);
                command.Parameters.AddWithValue("actor", actorUserId);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Public-provider secret rotation lost its key-ID fence.");
                await InsertProviderAuditAsync(connection, transaction, row, expectedCurrent, expectedActive, actorUserId, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        foreach (var profile in profiles)
            await RotatePrivateProfileAsync(connection, transaction, profile, expectedCurrent, expectedActive, actorUserId, rotatedAt, keyRing, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ProjectPulseAiEncryptionRotationResult(
            expectedCurrent,
            expectedActive,
            providerRows.Count,
            profiles.Count > 0,
            rotatedAt,
            actorUserId);
    }

    private static async Task RequireSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM schema_migrations
                WHERE migration_id = '071_ai_runtime_production_hardening'
            ) AND to_regclass('public.ai_provider_secrets') IS NOT NULL
              AND to_regclass('public.ai_private_model_profiles') IS NOT NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        if (!Convert.ToBoolean(await command.ExecuteScalarAsync(token) ?? false))
            throw new InvalidOperationException("Migration 071 must be applied before encryption-key rotation.");
    }

    private static async Task RotatePrivateProfileAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PrivateProfileRow row,
        string expectedCurrent,
        string expectedActive,
        Guid actor,
        DateTimeOffset rotatedAt,
        ProjectPulseAiEncryptionKeyRing keyRing,
        CancellationToken token)
    {
        byte[] endpoint = [];
        byte[] bearer = [];
        try
        {
            endpoint = DecryptOptional("celar_ai_private_endpoint", row.EndpointKeyId, row.EndpointCiphertext, row.EndpointNonce, row.EndpointTag, keyRing);
            bearer = DecryptOptional("celar_ai_private_token", row.TokenKeyId, row.TokenCiphertext, row.TokenNonce, row.TokenTag, keyRing);
            var encryptedEndpoint = EncryptOptional("celar_ai_private_endpoint", endpoint, keyRing.ActiveKey());
            var encryptedBearer = EncryptOptional("celar_ai_private_token", bearer, keyRing.ActiveKey());
            const string update = """
                UPDATE ai_private_model_profiles
                SET endpoint_ciphertext = @endpoint_ciphertext,
                    endpoint_nonce = @endpoint_nonce,
                    endpoint_tag = @endpoint_tag,
                    endpoint_encryption_key_id = @active_key_id,
                    token_ciphertext = @token_ciphertext,
                    token_nonce = @token_nonce,
                    token_tag = @token_tag,
                    token_encryption_key_id = @active_key_id,
                    updated_at = @rotated_at,
                    updated_by = @actor
                WHERE environment_code = @environment
                  AND endpoint_encryption_key_id = @current_key_id
                  AND token_encryption_key_id = @current_key_id;
                """;
            await using var command = new NpgsqlCommand(update, connection, transaction);
            AddBytes(command, "endpoint_ciphertext", encryptedEndpoint?.Ciphertext);
            AddBytes(command, "endpoint_nonce", encryptedEndpoint?.Nonce);
            AddBytes(command, "endpoint_tag", encryptedEndpoint?.Tag);
            AddBytes(command, "token_ciphertext", encryptedBearer?.Ciphertext);
            AddBytes(command, "token_nonce", encryptedBearer?.Nonce);
            AddBytes(command, "token_tag", encryptedBearer?.Tag);
            command.Parameters.AddWithValue("active_key_id", expectedActive);
            command.Parameters.AddWithValue("current_key_id", expectedCurrent);
            command.Parameters.AddWithValue("rotated_at", rotatedAt);
            command.Parameters.AddWithValue("actor", actor);
            command.Parameters.AddWithValue("environment", row.Environment);
            if (await command.ExecuteNonQueryAsync(token) != 1)
                throw new InvalidOperationException("Private-profile rotation lost its key-ID fence.");

            const string audit = """
                INSERT INTO ai_private_model_profile_audit
                    (environment_code, action, revision, encryption_key_id,
                     previous_encryption_key_id, actor_user_id)
                VALUES
                    (@environment, 'encryption_key_rotated', @revision, @active_key_id,
                     @current_key_id, @actor);
                """;
            await using var auditCommand = new NpgsqlCommand(audit, connection, transaction);
            auditCommand.Parameters.AddWithValue("environment", row.Environment);
            auditCommand.Parameters.AddWithValue("revision", row.Revision);
            auditCommand.Parameters.AddWithValue("active_key_id", expectedActive);
            auditCommand.Parameters.AddWithValue("current_key_id", expectedCurrent);
            auditCommand.Parameters.AddWithValue("actor", actor);
            await auditCommand.ExecuteNonQueryAsync(token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(endpoint);
            CryptographicOperations.ZeroMemory(bearer);
        }
    }

    private static async Task InsertProviderAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProviderRow row,
        string previous,
        string active,
        Guid actor,
        CancellationToken token)
    {
        const string sql = """
            INSERT INTO ai_provider_secret_audit
                (provider_code, action, version, encryption_key_id,
                 previous_encryption_key_id, actor_user_id)
            VALUES
                (@provider, 'encryption_key_rotated', @version, @active,
                 @previous, @actor);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("provider", row.Provider);
        command.Parameters.AddWithValue("version", row.Version);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.AddWithValue("previous", previous);
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(token);
    }

    private static byte[] Decrypt(string purpose, string keyId, byte[] ciphertext, byte[] nonce, byte[] tag, ProjectPulseAiEncryptionKeyRing keyRing)
    {
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(keyRing.Key(keyId), 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(purpose));
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    private static byte[] DecryptOptional(string purpose, string keyId, byte[]? ciphertext, byte[]? nonce, byte[]? tag, ProjectPulseAiEncryptionKeyRing keyRing) =>
        ciphertext is null || nonce is null || tag is null ? [] : Decrypt(purpose, keyId, ciphertext, nonce, tag, keyRing);

    private static Encrypted Encrypt(string purpose, byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(purpose));
        return new Encrypted(ciphertext, nonce, tag);
    }

    private static Encrypted? EncryptOptional(string purpose, byte[] plaintext, byte[] key) =>
        plaintext.Length == 0 ? null : Encrypt(purpose, plaintext, key);

    private static void AddBytes(NpgsqlCommand command, string name, byte[]? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlTypes.NpgsqlDbType.Bytea);
        parameter.Value = value is { Length: > 0 } ? value : DBNull.Value;
    }

    private static string CleanKeyId(string? value) => new((value ?? string.Empty)
        .Trim()
        .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
        .Take(120)
        .ToArray());

    private sealed record ProviderRow(string Provider, byte[] Ciphertext, byte[] Nonce, byte[] Tag, string KeyId, string Version);
    private sealed record PrivateProfileRow(
        string Environment,
        byte[]? EndpointCiphertext,
        byte[]? EndpointNonce,
        byte[]? EndpointTag,
        string EndpointKeyId,
        byte[]? TokenCiphertext,
        byte[]? TokenNonce,
        byte[]? TokenTag,
        string TokenKeyId,
        int Revision);
    private sealed record Encrypted(byte[] Ciphertext, byte[] Nonce, byte[] Tag);
}
