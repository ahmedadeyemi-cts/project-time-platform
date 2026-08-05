using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

var connectionString = Environment.GetEnvironmentVariable("PROJECTPULSE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("PROJECTPULSE_CONNECTION_STRING is required.");
await ReleaseRuntimeBehavior.RunAsync(connectionString);
var previousKey = RandomNumberGenerator.GetBytes(32);
var activeKey = RandomNumberGenerator.GetBytes(32);
try
{
    Environment.SetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID", "ci-v2");
    Environment.SetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY", Convert.ToBase64String(activeKey));
    Environment.SetEnvironmentVariable(
        "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING",
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["ci-v1"] = Convert.ToBase64String(previousKey)
        }));

    var actor = Guid.NewGuid();
    var providerSecrets = new Dictionary<string, (byte[] Ciphertext, byte[] Nonce, byte[] Tag)>(StringComparer.Ordinal)
    {
        ["claude"] = Encrypt("claude", Encoding.UTF8.GetBytes("claude-ci-secret"), previousKey),
        ["openai"] = Encrypt("openai", Encoding.UTF8.GetBytes("openai-ci-secret"), previousKey)
    };
    var privateEndpointSecret = Encrypt(
        "celar_ai_private_endpoint",
        Encoding.UTF8.GetBytes("https://private.internal/v1"),
        previousKey);
    var privateTokenSecret = Encrypt(
        "celar_ai_private_token",
        Encoding.UTF8.GetBytes("ci-private-token"),
        previousKey);
    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        foreach (var provider in new[] { "claude", "openai" })
        {
            var encrypted = providerSecrets[provider];
            await using var insert = new NpgsqlCommand("""
                INSERT INTO ai_provider_secrets
                    (provider_code,ciphertext,nonce,tag,encryption_key_id,version,rotated_at,rotated_by)
                VALUES (@provider,@ciphertext,@nonce,@tag,'ci-v1','ci-version',NOW(),@actor);
                """, connection);
            insert.Parameters.AddWithValue("provider", provider);
            insert.Parameters.AddWithValue("ciphertext", encrypted.Ciphertext);
            insert.Parameters.AddWithValue("nonce", encrypted.Nonce);
            insert.Parameters.AddWithValue("tag", encrypted.Tag);
            insert.Parameters.AddWithValue("actor", actor);
            await insert.ExecuteNonQueryAsync();
        }

        await using var profile = new NpgsqlCommand("""
            INSERT INTO ai_private_model_profiles
                (environment_code,endpoint_ciphertext,endpoint_nonce,endpoint_tag,endpoint_encryption_key_id,
                 token_ciphertext,token_nonce,token_tag,token_encryption_key_id,revision)
            VALUES
                ('ci',@ec,@en,@et,'ci-v1',@tc,@tn,@tt,'ci-v1',1);
            """, connection);
        profile.Parameters.AddWithValue("ec", privateEndpointSecret.Ciphertext);
        profile.Parameters.AddWithValue("en", privateEndpointSecret.Nonce);
        profile.Parameters.AddWithValue("et", privateEndpointSecret.Tag);
        profile.Parameters.AddWithValue("tc", privateTokenSecret.Ciphertext);
        profile.Parameters.AddWithValue("tn", privateTokenSecret.Nonce);
        profile.Parameters.AddWithValue("tt", privateTokenSecret.Tag);
        await profile.ExecuteNonQueryAsync();
    }

    var service = new ProjectPulseAiEncryptionRotationService();
    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var corrupt = new NpgsqlCommand(
            "UPDATE ai_private_model_profiles SET token_tag=@invalid_tag WHERE environment_code='ci';",
            connection);
        corrupt.Parameters.AddWithValue("invalid_tag", RandomNumberGenerator.GetBytes(16));
        Require(await corrupt.ExecuteNonQueryAsync() == 1, "corrupt private profile fixture");
    }
    var beforeFailedRotation = await SecretStateFingerprintAsync(connectionString);

    var refusedCorruptCiphertext = false;
    try
    {
        _ = await service.RotateAsync(
            new ProjectPulseAiEncryptionRotationRequest("ci-v1", "ci-v2", ProjectPulseAiEncryptionRotationService.Confirmation),
            actor);
    }
    catch (CryptographicException)
    {
        refusedCorruptCiphertext = true;
    }
    Require(refusedCorruptCiphertext, "corrupt private profile ciphertext refuses rotation after public-provider writes");
    var afterFailedRotation = await SecretStateFingerprintAsync(connectionString);
    Require(
        string.Equals(beforeFailedRotation, afterFailedRotation, StringComparison.Ordinal),
        "atomic rollback preserves exact ciphertext nonce tag key IDs and audit rows across stores");

    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using (var verifyRollback = new NpgsqlCommand("""
            SELECT
              (SELECT count(*) FROM ai_provider_secrets WHERE encryption_key_id='ci-v1'),
              (SELECT count(*) FROM ai_provider_secrets WHERE encryption_key_id='ci-v2'),
              (SELECT count(*) FROM ai_private_model_profiles WHERE endpoint_encryption_key_id='ci-v1' AND token_encryption_key_id='ci-v1'),
              (SELECT count(*) FROM ai_provider_secret_audit WHERE action='encryption_key_rotated'),
              (SELECT count(*) FROM ai_private_model_profile_audit WHERE action='encryption_key_rotated');
            """, connection))
        await using (var reader = await verifyRollback.ExecuteReaderAsync())
        {
            Require(await reader.ReadAsync(), "rollback verification row");
            Require(
                reader.GetInt64(0) == 2
                && reader.GetInt64(1) == 0
                && reader.GetInt64(2) == 1
                && reader.GetInt64(3) == 0
                && reader.GetInt64(4) == 0,
                "atomic rollback preserves all key IDs and audit row counts");
        }

        await using var restore = new NpgsqlCommand(
            "UPDATE ai_private_model_profiles SET token_tag=@tag WHERE environment_code='ci' AND token_encryption_key_id='ci-v1';",
            connection);
        restore.Parameters.AddWithValue("tag", privateTokenSecret.Tag);
        Require(await restore.ExecuteNonQueryAsync() == 1, "restore valid private profile fixture");
    }

    var result = await service.RotateAsync(
        new ProjectPulseAiEncryptionRotationRequest("ci-v1", "ci-v2", ProjectPulseAiEncryptionRotationService.Confirmation),
        actor);
    Require(result.PublicProviderSecretsRotated == 2 && result.PrivateProfileRotated, "rotation result counts");

    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var verify = new NpgsqlCommand("""
            SELECT
              (SELECT count(*) FROM ai_provider_secrets WHERE encryption_key_id='ci-v2'),
              (SELECT count(*) FROM ai_private_model_profiles WHERE endpoint_encryption_key_id='ci-v2' AND token_encryption_key_id='ci-v2'),
              (SELECT count(*) FROM ai_provider_secret_audit WHERE action='encryption_key_rotated' AND encryption_key_id='ci-v2' AND previous_encryption_key_id='ci-v1'),
              (SELECT count(*) FROM ai_private_model_profile_audit WHERE action='encryption_key_rotated' AND encryption_key_id='ci-v2' AND previous_encryption_key_id='ci-v1');
            """, connection);
        await using var reader = await verify.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "verification row");
        Require(reader.GetInt64(0) == 2, "public rows use active key ID");
        Require(reader.GetInt64(1) == 1, "private profile uses active key ID");
        Require(reader.GetInt64(2) == 2 && reader.GetInt64(3) == 1, "rotation audit metadata");
        await reader.CloseAsync();

        var verifiedProviders = 0;
        await using (var ciphertext = new NpgsqlCommand(
            "SELECT provider_code, ciphertext, nonce, tag FROM ai_provider_secrets ORDER BY provider_code;",
            connection))
        await using (var cipherReader = await ciphertext.ExecuteReaderAsync())
        {
            while (await cipherReader.ReadAsync())
            {
                var provider = cipherReader.GetString(0);
                AssertDecryptsOnlyWithActiveKey(
                    provider,
                    (byte[])cipherReader[1],
                    (byte[])cipherReader[2],
                    (byte[])cipherReader[3],
                    $"{provider}-ci-secret",
                    activeKey,
                    previousKey);
                verifiedProviders++;
            }
        }

        Require(verifiedProviders == 2, "rotated ciphertext decrypts only with active key for both public providers");

        await using (var privateCiphertext = new NpgsqlCommand("""
            SELECT endpoint_ciphertext, endpoint_nonce, endpoint_tag,
                   token_ciphertext, token_nonce, token_tag
            FROM ai_private_model_profiles
            WHERE environment_code='ci';
            """, connection))
        await using (var privateReader = await privateCiphertext.ExecuteReaderAsync())
        {
            Require(await privateReader.ReadAsync(), "rotated private profile row");
            AssertDecryptsOnlyWithActiveKey(
                "celar_ai_private_endpoint",
                (byte[])privateReader[0],
                (byte[])privateReader[1],
                (byte[])privateReader[2],
                "https://private.internal/v1",
                activeKey,
                previousKey);
            AssertDecryptsOnlyWithActiveKey(
                "celar_ai_private_token",
                (byte[])privateReader[3],
                (byte[])privateReader[4],
                (byte[])privateReader[5],
                "ci-private-token",
                activeKey,
                previousKey);
        }
    }

    Console.WriteLine("CELAR_AI_KEY_RING_ROTATION_INTEGRATION=PASSED");
}
finally
{
    CryptographicOperations.ZeroMemory(previousKey);
    CryptographicOperations.ZeroMemory(activeKey);
    Environment.SetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY", null);
    Environment.SetEnvironmentVariable("PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING", null);
}

static (byte[] Ciphertext, byte[] Nonce, byte[] Tag) Encrypt(string purpose, byte[] plaintext, byte[] key)
{
    var nonce = RandomNumberGenerator.GetBytes(12);
    var ciphertext = new byte[plaintext.Length];
    var tag = new byte[16];
    using var aes = new AesGcm(key, 16);
    aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(purpose));
    CryptographicOperations.ZeroMemory(plaintext);
    return (ciphertext, nonce, tag);
}

static async Task<string> SecretStateFingerprintAsync(string connectionString)
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("""
        SELECT encode(digest(
            COALESCE((
                SELECT string_agg(
                    provider_code || ':' || encode(ciphertext, 'hex') || ':' ||
                    encode(nonce, 'hex') || ':' || encode(tag, 'hex') || ':' ||
                    encryption_key_id || ':' || version || ':' || rotated_at::text || ':' || rotated_by::text,
                    '|' ORDER BY provider_code)
                FROM ai_provider_secrets
            ), '') || '::' ||
            COALESCE((
                SELECT string_agg(
                    environment_code || ':' ||
                    COALESCE(encode(endpoint_ciphertext, 'hex'), '') || ':' ||
                    COALESCE(encode(endpoint_nonce, 'hex'), '') || ':' ||
                    COALESCE(encode(endpoint_tag, 'hex'), '') || ':' ||
                    endpoint_encryption_key_id || ':' ||
                    COALESCE(encode(token_ciphertext, 'hex'), '') || ':' ||
                    COALESCE(encode(token_nonce, 'hex'), '') || ':' ||
                    COALESCE(encode(token_tag, 'hex'), '') || ':' ||
                    token_encryption_key_id || ':' || revision::text || ':' ||
                    updated_at::text || ':' || COALESCE(updated_by::text, ''),
                    '|' ORDER BY environment_code)
                FROM ai_private_model_profiles
            ), '') || '::' || COALESCE((
                SELECT string_agg(
                    audit_id::text || ':' || provider_code || ':' || action || ':' ||
                    version || ':' || encryption_key_id || ':' ||
                    COALESCE(previous_encryption_key_id, '') || ':' ||
                    actor_user_id::text || ':' || occurred_at::text,
                    '|' ORDER BY audit_id)
                FROM ai_provider_secret_audit
            ), '') || '::' || COALESCE((
                SELECT string_agg(
                    audit_id::text || ':' || environment_code || ':' || action || ':' ||
                    revision::text || ':' || encryption_key_id || ':' ||
                    COALESCE(previous_encryption_key_id, '') || ':' ||
                    COALESCE(actor_user_id::text, '') || ':' || occurred_at::text,
                    '|' ORDER BY audit_id)
                FROM ai_private_model_profile_audit
            ), ''),
            'sha256'),
            'hex');
        """, connection);
    return Convert.ToString(await command.ExecuteScalarAsync())
        ?? throw new InvalidOperationException("Secret-state fingerprint was unavailable.");
}

static void AssertDecryptsOnlyWithActiveKey(
    string purpose,
    byte[] ciphertext,
    byte[] nonce,
    byte[] tag,
    string expected,
    byte[] activeKey,
    byte[] previousKey)
{
    var plaintext = new byte[ciphertext.Length];
    try
    {
        using var aes = new AesGcm(activeKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(purpose));
        Require(Encoding.UTF8.GetString(plaintext) == expected, $"active key decrypts {purpose}");
    }
    finally
    {
        CryptographicOperations.ZeroMemory(plaintext);
    }

    var oldPlaintext = new byte[ciphertext.Length];
    var oldKeyRejected = false;
    try
    {
        using var oldAes = new AesGcm(previousKey, 16);
        oldAes.Decrypt(nonce, ciphertext, tag, oldPlaintext, Encoding.UTF8.GetBytes(purpose));
    }
    catch (CryptographicException)
    {
        oldKeyRejected = true;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(oldPlaintext);
    }
    Require(oldKeyRejected, $"old key cannot decrypt rotated ciphertext for {purpose}");
}

static void Require(bool condition, string evidence)
{
    if (!condition) throw new InvalidOperationException($"Celar AI production-hardening assertion failed: {evidence}.");
}
