using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class ProjectPulseAiSecretStore : IDisposable
{
    private const int MaximumSecretBytes = 8192;
    private readonly string? _connectionString;
    private readonly ProjectPulseAiEncryptionKeyRing _keyRing;
    private readonly ILogger<ProjectPulseAiSecretStore> _logger;

    public ProjectPulseAiSecretStore(ILogger<ProjectPulseAiSecretStore> logger)
    {
        _logger = logger;
        _connectionString = ConnectionString();
        _keyRing = ProjectPulseAiEncryptionKeyRing.Load();
    }

    public bool Available => _connectionString is not null && _keyRing.Available;
    public string UnavailableReason => _connectionString is null
        ? "Database configuration is unavailable."
        : !_keyRing.Available
            ? "PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY must be a base64-encoded 32-byte key."
            : string.Empty;

    public async Task<IReadOnlyList<StoredSecret>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsReleaseScoped) return [];
        if (!Available) return [];
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        const string sql = "SELECT provider_code, ciphertext, nonce, tag, encryption_key_id, version, rotated_at FROM ai_provider_secrets;";
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StoredSecret>();
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                var providerCode = reader.GetString(0);
                result.Add(new StoredSecret(
                    providerCode,
                    Decrypt(providerCode, reader.GetString(4), (byte[])reader[1], (byte[])reader[2], (byte[])reader[3]),
                    reader.GetString(5),
                    new DateTimeOffset(reader.GetDateTime(6).ToUniversalTime())));
            }
            catch (CryptographicException exception)
            {
                _logger.LogError(
                    exception,
                    "Module 064 could not decrypt the {Provider} provider secret.",
                    reader.GetString(0));
            }
        }
        return result;
    }

    public async Task<StoredSecret> SaveAsync(
        string providerCode,
        string apiKey,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ProjectPulseAiReleaseRuntimePolicy.RejectReleaseConfigurationMutation("Public-provider secret mutation");
        if (!Available) throw new InvalidOperationException(UnavailableReason);
        var secretBytes = Encoding.UTF8.GetByteCount(apiKey);
        if (secretBytes is < 1 or > MaximumSecretBytes)
            throw new ArgumentException($"API key must be between 1 and {MaximumSecretBytes} UTF-8 bytes.");

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(apiKey);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(_keyRing.ActiveKey(), 16);
            aes.Encrypt(
                nonce,
                plaintext,
                ciphertext,
                tag,
                Encoding.UTF8.GetBytes(providerCode));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var version = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var rotatedAt = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string upsert = """
            INSERT INTO ai_provider_secrets (provider_code, ciphertext, nonce, tag, encryption_key_id, version, rotated_at, rotated_by)
            VALUES (@provider, @ciphertext, @nonce, @tag, @key_id, @version, @rotated_at, @actor)
            ON CONFLICT (provider_code) DO UPDATE SET ciphertext = EXCLUDED.ciphertext, nonce = EXCLUDED.nonce,
                tag = EXCLUDED.tag, encryption_key_id = EXCLUDED.encryption_key_id,
                version = EXCLUDED.version, rotated_at = EXCLUDED.rotated_at, rotated_by = EXCLUDED.rotated_by;
            """;
        await using (var command = new NpgsqlCommand(upsert, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerCode);
            command.Parameters.AddWithValue("ciphertext", ciphertext);
            command.Parameters.AddWithValue("nonce", nonce);
            command.Parameters.AddWithValue("tag", tag);
            command.Parameters.AddWithValue("key_id", _keyRing.ActiveKeyId);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("rotated_at", rotatedAt);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string audit = "INSERT INTO ai_provider_secret_audit (provider_code, action, version, encryption_key_id, actor_user_id) VALUES (@provider, 'replaced', @version, @key_id, @actor);";
        await using (var command = new NpgsqlCommand(audit, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerCode);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("key_id", _keyRing.ActiveKeyId);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new StoredSecret(providerCode, apiKey, version, rotatedAt);
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadModelsAsync(
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsReleaseScoped)
            return new Dictionary<string, string>();
        if (_connectionString is null) return new Dictionary<string, string>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        const string sql = "SELECT provider_code, model FROM ai_provider_settings;";
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public async Task<IReadOnlyDictionary<string, bool>> LoadEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsReleaseScoped)
            return new Dictionary<string, bool>();
        if (_connectionString is null) return new Dictionary<string, bool>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        const string sql = "SELECT provider_code, enabled FROM ai_provider_settings;";
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
            result[reader.GetString(0)] = reader.GetBoolean(1);
        return result;
    }

    public async Task SaveModelAsync(
        string providerCode,
        string model,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ProjectPulseAiReleaseRuntimePolicy.RejectReleaseConfigurationMutation("Public-provider model mutation");
        if (_connectionString is null)
            throw new InvalidOperationException("Database configuration is unavailable.");
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string upsert = """
            INSERT INTO ai_provider_settings (provider_code, model, updated_at, updated_by)
            VALUES (@provider, @model, CURRENT_TIMESTAMP, @actor)
            ON CONFLICT (provider_code) DO UPDATE SET model = EXCLUDED.model,
                updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by;
            """;
        await using (var command = new NpgsqlCommand(upsert, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerCode);
            command.Parameters.AddWithValue("model", model);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string audit = "INSERT INTO ai_provider_settings_audit (provider_code, action, model, actor_user_id) VALUES (@provider, 'model_changed', @model, @actor);";
        await using (var command = new NpgsqlCommand(audit, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerCode);
            command.Parameters.AddWithValue("model", model);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveEnabledAsync(
        string providerCode,
        bool enabled,
        string model,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        ProjectPulseAiReleaseRuntimePolicy.RejectReleaseConfigurationMutation("Public-provider enabled-state mutation");
        if (_connectionString is null)
            throw new InvalidOperationException("Database configuration is unavailable.");
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string upsert = """
            INSERT INTO ai_provider_settings (provider_code, model, enabled, updated_at, updated_by)
            VALUES (@provider, @model, @enabled, CURRENT_TIMESTAMP, @actor)
            ON CONFLICT (provider_code) DO UPDATE SET enabled = EXCLUDED.enabled,
                updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by;
            """;
        await using (var command = new NpgsqlCommand(upsert, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerCode);
            command.Parameters.AddWithValue("model", model);
            command.Parameters.AddWithValue("enabled", enabled);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        const string audit = "INSERT INTO ai_provider_settings_audit (provider_code, action, model, actor_user_id) VALUES (@provider, @action, @model, @actor);";
        await using (var command = new NpgsqlCommand(audit, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerCode);
            command.Parameters.AddWithValue("action", enabled ? "enabled" : "disabled");
            command.Parameters.AddWithValue("model", model);
            command.Parameters.AddWithValue("actor", actorUserId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id = '071_ai_runtime_production_hardening')
                AND to_regclass('public.ai_provider_secrets') IS NOT NULL
                AND to_regclass('public.ai_provider_secret_audit') IS NOT NULL
                AND to_regclass('public.ai_provider_settings') IS NOT NULL
                AND to_regclass('public.ai_provider_settings_audit') IS NOT NULL
                AND EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'ai_provider_secrets'
                      AND column_name = 'encryption_key_id'
                );
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        if (!Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false))
            throw new InvalidOperationException("Migration 071 must be applied before Module 064 provider configuration can be read or changed.");
    }

    private string Decrypt(string providerCode, string keyId, byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(_keyRing.Key(keyId), 16);
            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                plaintext,
                Encoding.UTF8.GetBytes(providerCode));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Dispose() => _keyRing.Dispose();

    private static string? ConnectionString() => ProjectPulseAiDatabaseConnection.Resolve();

    public sealed record StoredSecret(
        string ProviderCode,
        string ApiKey,
        string Version,
        DateTimeOffset RotatedAt);
}

public sealed class ProjectPulseAiSecretLoader(
    ProjectPulseAiSecretStore store,
    ProjectPulseAiConfiguration configuration,
    ProjectPulseAiHealthRegistry health,
    ILogger<ProjectPulseAiSecretLoader> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        if (release.IsReleaseScoped)
        {
            // Candidate and active release revisions consume version-pinned
            // deployment secrets and models. Shared database values must never
            // replace the configuration covered by the release digest.
            health.ApplyConfiguration(configuration.Claude);
            health.ApplyConfiguration(configuration.OpenAi);
            logger.LogInformation(
                "Module 064 database provider loading is frozen for release phase {ReleasePhase}.",
                release.PhaseCode);
            return;
        }

        if (!store.Available)
        {
            logger.LogWarning(
                "Module 064 write-only secret store is unavailable: {Reason}",
                store.UnavailableReason);
            health.ApplyConfiguration(configuration.Claude);
            health.ApplyConfiguration(configuration.OpenAi);
            return;
        }

        foreach (var secret in await store.LoadAsync(cancellationToken))
            configuration.ApplyStoredSecret(
                secret.ProviderCode,
                secret.ApiKey,
                secret.Version,
                secret.RotatedAt);
        foreach (var setting in await store.LoadModelsAsync(cancellationToken))
            configuration.ApplyStoredModel(setting.Key, setting.Value);
        foreach (var setting in await store.LoadEnabledAsync(cancellationToken))
            configuration.ApplyStoredEnabled(setting.Key, setting.Value);

        // The health registry can be constructed before hosted services start.
        // Reconcile it only after encrypted keys and settings have been loaded.
        health.ApplyConfiguration(configuration.Claude);
        health.ApplyConfiguration(configuration.OpenAi);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ProjectPulseAiConfigurationSynchronizer(
    ProjectPulseAiSecretStore store,
    ProjectPulseAiConfiguration configuration,
    ProjectPulseAiHealthRegistry health,
    ProjectPulseAiHealthCoordinator coordinator,
    ILogger<ProjectPulseAiConfigurationSynchronizer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        if (release.IsReleaseScoped)
        {
            logger.LogInformation(
                "Module 064 database provider synchronization is frozen for release phase {ReleasePhase}.",
                release.PhaseCode);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var secret in await store.LoadAsync(stoppingToken))
                    configuration.ApplyStoredSecret(
                        secret.ProviderCode,
                        secret.ApiKey,
                        secret.Version,
                        secret.RotatedAt);
                foreach (var setting in await store.LoadModelsAsync(stoppingToken))
                    configuration.ApplyStoredModel(setting.Key, setting.Value);
                foreach (var setting in await store.LoadEnabledAsync(stoppingToken))
                    configuration.ApplyStoredEnabled(setting.Key, setting.Value);

                health.ApplyConfiguration(configuration.Claude);
                health.ApplyConfiguration(configuration.OpenAi);
                await coordinator.RefreshAsync(false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Module 064 could not synchronize provider configuration and health.");
            }
        }
    }
}
