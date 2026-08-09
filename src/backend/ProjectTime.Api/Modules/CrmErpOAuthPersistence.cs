using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 026 OAuth persistence and actual-session administrator authority.
/// Access tokens, refresh tokens, and client secrets remain encrypted and are
/// never returned by these endpoints or written to logs/audit payloads.
/// </summary>
public static partial class CrmErpIntegrationModule
{
    private const int OAuthRefreshWorkerLock = 26056026;
    private const int OAuthRefreshWindowMinutes = 15;
    private static readonly TimeSpan OAuthRefreshWorkerInterval = TimeSpan.FromMinutes(5);
    private static int _oauthRefreshWorkerStarted;

    public static IEndpointRouteBuilder MapCrmErpOAuthPersistenceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/integrations/026");
        group.MapGet("/token-refresh/status", GetOAuthRefreshStatusAsync);
        group.MapPost("/providers/{providerKey}/refresh-token", RefreshOAuthTokenEndpointAsync);
        return endpoints;
    }

    public static WebApplication UseCrmErpOAuthPersistence(this WebApplication app)
    {
        if (Interlocked.Exchange(ref _oauthRefreshWorkerStarted, 1) == 1) return app;

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(
                () => RunOAuthRefreshWorkerAsync(app.Services, lifetime.ApplicationStopping),
                CancellationToken.None);
        });
        return app;
    }

    /// <summary>
    /// The actual Pulse administrator session is an immutable authority
    /// invariant. A missing optional dynamic-policy grant cannot reduce Super
    /// Administrator, Administrator, Integration Administrator, MANAGE_ALL, or
    /// MANAGE_INTEGRATIONS_026 authority. View-As remains read-only.
    /// </summary>
    private static async Task<ManageAuthority> ResolveManageAuthorityAsync(HttpContext context)
    {
        if (IsViewAs(context))
        {
            return new ManageAuthority(
                false,
                "view_as_read_only",
                "Exit Administrator View-As before changing CRM or ERP connector configuration.");
        }

        if ((context.Items.TryGetValue("ProjectPulsePermanentFullControl", out var permanent)
                && permanent is true)
            || await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
                context,
                cancellationToken: context.RequestAborted))
        {
            return new ManageAuthority(
                true,
                "actual_session_super_administrator",
                "Your actual Super Administrator session has permanent Full Control of Module 026.");
        }

        if (await HasManageAuthorityLegacyAsync(context))
        {
            return new ManageAuthority(
                true,
                "actual_session_administrator_or_permission",
                "Your actual Pulse session can manage Module 026 integrations.");
        }

        return await ResolveManageAuthorityPolicyFirstAsync(context);
    }

    private static async Task<IResult> GetOAuthRefreshStatusAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return SchemaUnavailable();

        try
        {
            var providers = new List<object>();
            await using var command = new NpgsqlCommand("""
                SELECT
                    provider.provider_key,
                    provider.provider_name,
                    provider.auth_model,
                    provider.is_enabled,
                    token.expires_at,
                    token.rotated_at,
                    token.rotated_by,
                    EXISTS (
                        SELECT 1
                        FROM crm_integration_credentials secret
                        WHERE secret.provider_key = provider.provider_key
                          AND secret.credential_kind = 'oauth_client_secret'
                    ) AS client_secret_configured,
                    latest.created_at,
                    latest.refresh_status,
                    latest.diagnostic_code,
                    latest.next_expires_at
                FROM crm_integration_providers provider
                LEFT JOIN crm_integration_credentials token
                  ON token.provider_key = provider.provider_key
                 AND token.credential_kind = 'oauth_token'
                LEFT JOIN LATERAL (
                    SELECT
                        event.created_at,
                        event.refresh_status,
                        event.diagnostic_code,
                        event.next_expires_at
                    FROM crm_integration_token_refresh_events event
                    WHERE event.provider_key = provider.provider_key
                    ORDER BY event.created_at DESC
                    LIMIT 1
                ) latest ON TRUE
                WHERE provider.auth_model = 'oauth2'
                ORDER BY lower(provider.provider_name), provider.provider_key;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                var tokenExpiresAt = reader.IsDBNull(4)
                    ? (DateTimeOffset?)null
                    : reader.GetFieldValue<DateTimeOffset>(4);
                var lastRefreshAt = reader.IsDBNull(8)
                    ? (DateTimeOffset?)null
                    : reader.GetFieldValue<DateTimeOffset>(8);
                var latestExpiresAt = reader.IsDBNull(11)
                    ? tokenExpiresAt
                    : reader.GetFieldValue<DateTimeOffset>(11);
                var clientSecretConfigured = reader.GetBoolean(7);
                var tokenConfigured = !reader.IsDBNull(5);
                var refreshState = !reader.GetBoolean(3)
                    ? "disabled"
                    : !tokenConfigured
                        ? "oauth_not_connected"
                        : !clientSecretConfigured
                            ? "client_secret_missing"
                            : latestExpiresAt.HasValue
                                && latestExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(OAuthRefreshWindowMinutes)
                                ? "renewal_due"
                                : "persistent";

                providers.Add(new
                {
                    providerKey = reader.GetString(0),
                    providerName = reader.GetString(1),
                    authModel = reader.GetString(2),
                    isEnabled = reader.GetBoolean(3),
                    expiresAt = latestExpiresAt,
                    tokenStoredAt = reader.IsDBNull(5)
                        ? (DateTimeOffset?)null
                        : reader.GetFieldValue<DateTimeOffset>(5),
                    refreshState,
                    refreshEligible = reader.GetBoolean(3)
                        && tokenConfigured
                        && clientSecretConfigured
                        && ReadEncryptionKey() is not null,
                    lastRefreshAt,
                    lastRefreshStatus = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    lastDiagnosticCode = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    tokenReturned = false,
                    refreshTokenReturned = false,
                    clientSecretReturned = false
                });
            }

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "oauth_token_persistence_status_loaded",
                backgroundRefreshEnabled = !string.IsNullOrWhiteSpace(BuildConnectionString())
                    && ReadEncryptionKey() is not null,
                refreshWindowMinutes = OAuthRefreshWindowMinutes,
                refreshIntervalMinutes = (int)OAuthRefreshWorkerInterval.TotalMinutes,
                providers,
                security = new
                {
                    credentialsEncryptedAtRest = true,
                    secretsReturned = false,
                    refreshTokensReturned = false,
                    accessTokensReturned = false,
                    multiInstanceAdvisoryLocks = true
                }
            });
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            return OAuthRefreshMigrationRequired();
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "load OAuth persistence status");
            return OperationUnavailable("OAuth persistence status is temporarily unavailable.");
        }
    }

    private static async Task<IResult> RefreshOAuthTokenEndpointAsync(
        string providerKey,
        HttpContext context,
        IHttpClientFactory httpClientFactory)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        providerKey = NormalizeProviderKey(providerKey);
        if (string.IsNullOrWhiteSpace(providerKey)) return Invalid("A valid provider key is required.");

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return SchemaUnavailable();

        try
        {
            var provider = await ReadProviderConfigurationAsync(
                connection,
                providerKey,
                context.RequestAborted);
            if (provider is null)
            {
                return Results.NotFound(new
                {
                    module = ModuleNumber,
                    status = "provider_not_found",
                    message = "The integration provider was not found."
                });
            }
            if (provider.AuthModel != "oauth2")
            {
                return Invalid("API-key connections do not use OAuth token renewal.");
            }
            if (!provider.IsEnabled)
            {
                return Invalid("Enable the provider before refreshing its OAuth connection.");
            }

            var result = await RefreshOAuthTokenAsync(
                connection,
                provider,
                ActualUserId(context),
                "manual",
                httpClientFactory,
                context.RequestAborted);
            return Results.Json(new
            {
                module = ModuleNumber,
                status = result.Status,
                providerKey,
                refreshed = result.Refreshed,
                expiresAt = result.ExpiresAt,
                diagnosticCode = result.DiagnosticCode,
                message = result.Message,
                accessTokenReturned = false,
                refreshTokenReturned = false,
                clientSecretReturned = false
            }, statusCode: result.HttpStatusCode);
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            return OAuthRefreshMigrationRequired();
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "refresh OAuth connection");
            return OperationUnavailable("The OAuth connection could not be refreshed.");
        }
    }

    private static async Task RunOAuthRefreshWorkerAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CrmErpOAuthPersistenceWorker");
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var connectionString = BuildConnectionString();
                if (!string.IsNullOrWhiteSpace(connectionString)
                    && ReadEncryptionKey() is not null)
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync(cancellationToken);
                    var workerLock = false;
                    await using (var claim = new NpgsqlCommand(
                                     "SELECT pg_try_advisory_lock(@lock_id);",
                                     connection))
                    {
                        claim.Parameters.AddWithValue("lock_id", OAuthRefreshWorkerLock);
                        workerLock = Convert.ToBoolean(
                            await claim.ExecuteScalarAsync(cancellationToken) ?? false);
                    }

                    if (workerLock)
                    {
                        try
                        {
                            var dueProviders = await LoadDueOAuthProvidersAsync(
                                connection,
                                cancellationToken);
                            foreach (var provider in dueProviders)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                await RefreshOAuthTokenAsync(
                                    connection,
                                    provider.Configuration,
                                    provider.ActorUserId,
                                    "background",
                                    httpClientFactory,
                                    cancellationToken);
                            }
                        }
                        finally
                        {
                            await using var release = new NpgsqlCommand(
                                "SELECT pg_advisory_unlock(@lock_id);",
                                connection);
                            release.Parameters.AddWithValue("lock_id", OAuthRefreshWorkerLock);
                            await release.ExecuteNonQueryAsync(CancellationToken.None);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Module 026 OAuth persistence evaluation failed ({DiagnosticType}); raw detail suppressed.",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(OAuthRefreshWorkerInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task<List<OAuthRefreshCandidate>> LoadDueOAuthProvidersAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var providers = new List<OAuthRefreshCandidate>();
        await using var command = new NpgsqlCommand("""
            SELECT
                provider.provider_key,
                provider.provider_name,
                provider.auth_model,
                provider.base_url,
                provider.health_check_url,
                provider.oauth_authorization_url,
                provider.oauth_token_url,
                provider.oauth_client_id,
                provider.oauth_scopes,
                provider.api_key_header,
                provider.api_key_prefix,
                provider.is_enabled,
                token.rotated_by
            FROM crm_integration_providers provider
            JOIN crm_integration_credentials token
              ON token.provider_key = provider.provider_key
             AND token.credential_kind = 'oauth_token'
            JOIN crm_integration_credentials secret
              ON secret.provider_key = provider.provider_key
             AND secret.credential_kind = 'oauth_client_secret'
            WHERE provider.auth_model = 'oauth2'
              AND provider.is_enabled = TRUE
              AND (
                    token.expires_at IS NULL
                    OR token.expires_at <= NOW() + (@window_minutes * INTERVAL '1 minute')
                  )
            ORDER BY token.expires_at NULLS FIRST, provider.provider_key;
            """, connection);
        command.Parameters.AddWithValue("window_minutes", OAuthRefreshWindowMinutes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            providers.Add(new(
                new ProviderConfiguration(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetBoolean(11)),
                reader.GetGuid(12)));
        }
        return providers;
    }

    private static async Task<OAuthRefreshResult> RefreshOAuthTokenAsync(
        NpgsqlConnection connection,
        ProviderConfiguration provider,
        Guid? requestedActorUserId,
        string trigger,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        var lockKey = $"module026-oauth-refresh:{provider.ProviderKey}";
        var lockAcquired = false;
        await using (var claim = new NpgsqlCommand(
                         "SELECT pg_try_advisory_lock(hashtext(@lock_key));",
                         connection))
        {
            claim.Parameters.AddWithValue("lock_key", lockKey);
            lockAcquired = Convert.ToBoolean(
                await claim.ExecuteScalarAsync(cancellationToken) ?? false);
        }

        if (!lockAcquired)
        {
            return new(
                false,
                "oauth_refresh_already_running",
                "refresh_already_running",
                null,
                "Another Pulse instance is already refreshing this connector.",
                StatusCodes.Status409Conflict);
        }

        var encryptionKey = ReadEncryptionKey();
        if (encryptionKey is null)
        {
            await ReleaseProviderRefreshLockAsync(connection, lockKey);
            return new(
                false,
                "oauth_refresh_encryption_unavailable",
                "encryption_key_unavailable",
                null,
                "The encrypted integration credential store is unavailable.",
                StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            if (!TryHttpsUri(provider.OAuthTokenUrl, out var tokenUri)
                || !await IsSafeExternalUriAsync(tokenUri!, cancellationToken))
            {
                var invalid = new OAuthRefreshResult(
                    false,
                    "oauth_refresh_endpoint_rejected",
                    "token_endpoint_not_approved",
                    null,
                    "The OAuth token endpoint is not an approved public HTTPS address.",
                    StatusCodes.Status422UnprocessableEntity);
                await RecordOAuthRefreshEventAsync(
                    connection,
                    provider.ProviderKey,
                    trigger,
                    requestedActorUserId,
                    invalid,
                    null,
                    cancellationToken);
                return invalid;
            }

            var envelope = await LoadCredentialAsync(
                connection,
                provider.ProviderKey,
                "oauth_token",
                encryptionKey,
                cancellationToken);
            var clientSecret = await LoadCredentialAsync(
                connection,
                provider.ProviderKey,
                "oauth_client_secret",
                encryptionKey,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(envelope)
                || string.IsNullOrWhiteSpace(clientSecret))
            {
                var missing = new OAuthRefreshResult(
                    false,
                    "oauth_refresh_credentials_missing",
                    "oauth_refresh_credentials_missing",
                    null,
                    "Complete OAuth authorization and save the write-only client secret before renewal.",
                    StatusCodes.Status422UnprocessableEntity);
                await RecordOAuthRefreshEventAsync(
                    connection,
                    provider.ProviderKey,
                    trigger,
                    requestedActorUserId,
                    missing,
                    null,
                    cancellationToken);
                return missing;
            }

            string? refreshToken;
            string? currentInstanceUrl;
            try
            {
                using var currentDocument = JsonDocument.Parse(envelope);
                refreshToken = JsonText(currentDocument.RootElement, "refreshToken");
                currentInstanceUrl = JsonText(currentDocument.RootElement, "instanceUrl");
            }
            catch (JsonException)
            {
                refreshToken = null;
                currentInstanceUrl = null;
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                var missingRefresh = new OAuthRefreshResult(
                    false,
                    "oauth_refresh_token_missing",
                    "refresh_token_missing",
                    null,
                    "The provider did not issue a refresh token. Reconnect OAuth with an approved offline or refresh-token scope.",
                    StatusCodes.Status422UnprocessableEntity);
                await RecordOAuthRefreshEventAsync(
                    connection,
                    provider.ProviderKey,
                    trigger,
                    requestedActorUserId,
                    missingRefresh,
                    null,
                    cancellationToken);
                return missingRefresh;
            }

            var values = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = provider.OAuthClientId,
                ["client_secret"] = clientSecret
            };
            if (!string.IsNullOrWhiteSpace(provider.OAuthScopes))
            {
                values["scope"] = provider.OAuthScopes;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri)
            {
                Content = new FormUrlEncodedContent(values)
            };
            var client = httpClientFactory.CreateClient("Module026");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var payload = await ReadBoundedResponseBodyAsync(response.Content, cancellationToken);
            if (payload is null)
            {
                var oversized = new OAuthRefreshResult(
                    false,
                    "oauth_refresh_response_rejected",
                    "provider_response_too_large",
                    null,
                    "The provider token response exceeded the allowed size.",
                    StatusCodes.Status502BadGateway);
                await RecordOAuthRefreshEventAsync(
                    connection,
                    provider.ProviderKey,
                    trigger,
                    requestedActorUserId,
                    oversized,
                    (int)response.StatusCode,
                    cancellationToken);
                return oversized;
            }
            if (!response.IsSuccessStatusCode)
            {
                var rejected = new OAuthRefreshResult(
                    false,
                    "oauth_refresh_provider_rejected",
                    response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                        or System.Net.HttpStatusCode.Forbidden
                        ? "provider_authentication_rejected"
                        : "provider_refresh_rejected",
                    null,
                    "The provider rejected token renewal. Reconnect OAuth or validate the connected application policy.",
                    StatusCodes.Status502BadGateway);
                await RecordOAuthRefreshEventAsync(
                    connection,
                    provider.ProviderKey,
                    trigger,
                    requestedActorUserId,
                    rejected,
                    (int)response.StatusCode,
                    cancellationToken);
                return rejected;
            }

            using var document = JsonDocument.Parse(payload);
            var accessToken = JsonText(document.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                var invalid = new OAuthRefreshResult(
                    false,
                    "oauth_refresh_response_invalid",
                    "access_token_missing",
                    null,
                    "The provider renewal response did not contain an access token.",
                    StatusCodes.Status502BadGateway);
                await RecordOAuthRefreshEventAsync(
                    connection,
                    provider.ProviderKey,
                    trigger,
                    requestedActorUserId,
                    invalid,
                    (int)response.StatusCode,
                    cancellationToken);
                return invalid;
            }

            var replacementRefreshToken = JsonText(document.RootElement, "refresh_token");
            var replacementInstanceUrl = JsonText(document.RootElement, "instance_url");
            var expiresIn = JsonInteger(document.RootElement, "expires_in");
            var expiresAt = expiresIn is > 0
                ? DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value)
                : DateTimeOffset.UtcNow.AddMinutes(60);
            var nextEnvelope = JsonSerializer.Serialize(new
            {
                accessToken,
                refreshToken = string.IsNullOrWhiteSpace(replacementRefreshToken)
                    ? refreshToken
                    : replacementRefreshToken,
                instanceUrl = string.IsNullOrWhiteSpace(replacementInstanceUrl)
                    ? currentInstanceUrl
                    : replacementInstanceUrl,
                expiresAt
            });
            var actorUserId = requestedActorUserId
                ?? await LoadCredentialActorAsync(
                    connection,
                    provider.ProviderKey,
                    cancellationToken);
            if (!actorUserId.HasValue)
            {
                var actorMissing = new OAuthRefreshResult(
                    false,
                    "oauth_refresh_actor_unavailable",
                    "credential_actor_unavailable",
                    null,
                    "Pulse could not identify the governed credential owner for token renewal.",
                    StatusCodes.Status503ServiceUnavailable);
                await RecordOAuthRefreshEventAsync(
                    connection,
                    provider.ProviderKey,
                    trigger,
                    null,
                    actorMissing,
                    (int)response.StatusCode,
                    cancellationToken);
                return actorMissing;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await SaveCredentialAsync(
                    connection,
                    transaction,
                    provider.ProviderKey,
                    "oauth_token",
                    nextEnvelope,
                    expiresAt,
                    actorUserId.Value,
                    encryptionKey,
                    cancellationToken);
                await using (var update = new NpgsqlCommand("""
                    UPDATE crm_integration_providers
                    SET provider_status = 'connected',
                        last_error_code = '',
                        updated_by = @actor,
                        updated_at = NOW()
                    WHERE provider_key = @provider;
                    """, connection, transaction))
                {
                    update.Parameters.AddWithValue("actor", actorUserId.Value);
                    update.Parameters.AddWithValue("provider", provider.ProviderKey);
                    await update.ExecuteNonQueryAsync(cancellationToken);
                }

                var success = new OAuthRefreshResult(
                    true,
                    "oauth_token_refreshed",
                    string.Empty,
                    expiresAt,
                    "The OAuth access token was renewed and re-encrypted. The refresh token remains write-only.",
                    StatusCodes.Status200OK);
                await RecordOAuthRefreshEventAsync(
                    connection,
                    provider.ProviderKey,
                    trigger,
                    actorUserId,
                    success,
                    (int)response.StatusCode,
                    cancellationToken,
                    transaction);
                await SecurityDiagnosticsOperations.WriteAuditAsync(
                    connection,
                    transaction,
                    ModuleNumber,
                    "crm_erp_oauth_connection",
                    provider.ProviderKey,
                    "oauth_token_refreshed",
                    actorUserId.Value,
                    new
                    {
                        providerKey = provider.ProviderKey,
                        trigger,
                        expiresAt,
                        accessTokenReturned = false,
                        refreshTokenReturned = false,
                        clientSecretReturned = false
                    },
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return success;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            var invalid = new OAuthRefreshResult(
                false,
                "oauth_refresh_response_invalid",
                "provider_response_invalid_json",
                null,
                "The provider renewal response was not valid JSON.",
                StatusCodes.Status502BadGateway);
            await RecordOAuthRefreshEventAsync(
                connection,
                provider.ProviderKey,
                trigger,
                requestedActorUserId,
                invalid,
                null,
                cancellationToken);
            return invalid;
        }
        catch (HttpRequestException)
        {
            var failed = new OAuthRefreshResult(
                false,
                "oauth_refresh_connection_failed",
                "provider_connection_failed",
                null,
                "Pulse could not reach the approved provider token endpoint.",
                StatusCodes.Status502BadGateway);
            await RecordOAuthRefreshEventAsync(
                connection,
                provider.ProviderKey,
                trigger,
                requestedActorUserId,
                failed,
                null,
                cancellationToken);
            return failed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            await ReleaseProviderRefreshLockAsync(connection, lockKey);
        }
    }

    private static async Task<Guid?> LoadCredentialActorAsync(
        NpgsqlConnection connection,
        string providerKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT rotated_by
            FROM crm_integration_credentials
            WHERE provider_key = @provider
              AND credential_kind = 'oauth_token';
            """, connection);
        command.Parameters.AddWithValue("provider", providerKey);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private static async Task RecordOAuthRefreshEventAsync(
        NpgsqlConnection connection,
        string providerKey,
        string trigger,
        Guid? actorUserId,
        OAuthRefreshResult result,
        int? providerStatusCode,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO crm_integration_token_refresh_events (
                refresh_event_id,
                provider_key,
                refresh_trigger,
                refresh_status,
                diagnostic_code,
                provider_http_status,
                next_expires_at,
                actor_user_id,
                event_metadata,
                created_at
            )
            VALUES (
                gen_random_uuid(),
                @provider,
                @trigger,
                @status,
                @diagnostic_code,
                @provider_http_status,
                @next_expires_at,
                @actor_user_id,
                @metadata::jsonb,
                NOW()
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("provider", providerKey);
        command.Parameters.AddWithValue("trigger", Clean(trigger, 40, "unknown"));
        command.Parameters.AddWithValue("status", Clean(result.Status, 80));
        command.Parameters.AddWithValue("diagnostic_code", Clean(result.DiagnosticCode, 120));
        command.Parameters.AddWithValue(
            "provider_http_status",
            providerStatusCode.HasValue ? (object)providerStatusCode.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            "next_expires_at",
            result.ExpiresAt.HasValue ? (object)result.ExpiresAt.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            "actor_user_id",
            actorUserId.HasValue ? (object)actorUserId.Value : DBNull.Value);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
        {
            result.Refreshed,
            message = Clean(result.Message, 1000),
            accessTokenReturned = false,
            refreshTokenReturned = false,
            clientSecretReturned = false
        }));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReleaseProviderRefreshLockAsync(
        NpgsqlConnection connection,
        string lockKey)
    {
        try
        {
            await using var release = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtext(@lock_key));",
                connection);
            release.Parameters.AddWithValue("lock_key", lockKey);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch
        {
            // The database session releases advisory locks when the connection closes.
        }
    }

    private static IResult OAuthRefreshMigrationRequired() => Results.Json(new
    {
        module = ModuleNumber,
        status = "oauth_refresh_migration_required",
        migration = "056_role_workspace_entra_crm_governance",
        message = "Module 026 persistent OAuth renewal requires migration 056."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private sealed record OAuthRefreshCandidate(
        ProviderConfiguration Configuration,
        Guid ActorUserId);

    private sealed record OAuthRefreshResult(
        bool Refreshed,
        string Status,
        string DiagnosticCode,
        DateTimeOffset? ExpiresAt,
        string Message,
        int HttpStatusCode);
}
