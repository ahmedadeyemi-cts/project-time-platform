using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class CrmErpIntegrationModule
{
    private const string ModuleNumber = "026";
    private const int MaximumBodyBytes = 32 * 1024;
    private const int MaximumSecretBytes = 16 * 1024;
    private const int MaximumProviderResponseBytes = 64 * 1024;

    private static readonly string[] ViewRoles =
    [
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "INTEGRATION_ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR",
        "SALES", "ACCOUNT_EXECUTIVE", "ACCOUNT_EXECUTIVES", "INSIDE_SALES",
        "SOLUTION_ARCHITECT", "SA", "SAA"
    ];

    private static readonly string[] ManageRoles =
    [
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "INTEGRATION_ADMINISTRATOR"
    ];

    public static IEndpointRouteBuilder MapCrmErpIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/integrations/026");
        group.MapGet("/providers", ListProvidersAsync);
        group.MapPost("/providers", CreateProviderAsync);
        group.MapPut("/providers/{providerKey}", UpdateProviderAsync);
        group.MapPut("/providers/{providerKey}/credential", ReplaceCredentialAsync);
        group.MapPost("/providers/{providerKey}/test", TestProviderAsync);
        group.MapPost("/providers/{providerKey}/oauth/start", StartOAuthAsync);

        endpoints.MapGet("/api/public/integrations/026/oauth/callback", CompleteOAuthAsync);
        return endpoints;
    }

    private static async Task<IResult> ListProvidersAsync(HttpContext context)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return SchemaUnavailable();

        var providers = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT
                p.provider_key,
                p.provider_name,
                p.provider_type,
                p.auth_model,
                p.base_url,
                p.health_check_url,
                p.oauth_authorization_url,
                p.oauth_token_url,
                p.oauth_client_id,
                p.oauth_scopes,
                p.api_key_header,
                p.api_key_prefix,
                p.is_builtin,
                p.is_enabled,
                p.availability_status,
                p.last_checked_at,
                p.last_available_at,
                p.last_status_code,
                p.last_error_code,
                p.notes,
                EXISTS (
                    SELECT 1
                    FROM crm_integration_credentials c
                    WHERE c.provider_key = p.provider_key
                      AND c.credential_kind = CASE WHEN p.auth_model = 'api_key' THEN 'api_key' ELSE 'oauth_client_secret' END
                ) AS credential_configured,
                EXISTS (
                    SELECT 1
                    FROM crm_integration_credentials c
                    WHERE c.provider_key = p.provider_key
                      AND c.credential_kind = 'oauth_token'
                ) AS oauth_connected
            FROM crm_integration_providers p
            ORDER BY
                CASE p.provider_key
                    WHEN 'zendesk_sell' THEN 10
                    WHEN 'salesforce' THEN 20
                    WHEN 'certinia' THEN 30
                    WHEN 'servicenow' THEN 40
                    ELSE 100
                END,
                lower(p.provider_name),
                p.provider_key;
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            providers.Add(new
            {
                providerKey = reader.GetString(0),
                providerName = reader.GetString(1),
                providerType = reader.GetString(2),
                authModel = reader.GetString(3),
                baseUrl = reader.GetString(4),
                healthCheckUrl = reader.GetString(5),
                oauthAuthorizationUrl = reader.GetString(6),
                oauthTokenUrl = reader.GetString(7),
                oauthClientId = reader.GetString(8),
                oauthScopes = reader.GetString(9),
                apiKeyHeader = reader.GetString(10),
                apiKeyPrefix = reader.GetString(11),
                isBuiltin = reader.GetBoolean(12),
                isEnabled = reader.GetBoolean(13),
                availabilityStatus = reader.GetString(14),
                lastCheckedAt = reader.IsDBNull(15) ? (DateTime?)null : reader.GetDateTime(15),
                lastAvailableAt = reader.IsDBNull(16) ? (DateTime?)null : reader.GetDateTime(16),
                lastStatusCode = reader.IsDBNull(17) ? (int?)null : reader.GetInt32(17),
                lastErrorCode = reader.GetString(18),
                notes = reader.GetString(19),
                credentialConfigured = reader.GetBoolean(20),
                oauthConnected = reader.GetBoolean(21),
                secretValueReturned = false
            });
        }

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "providers_loaded",
            generatedAt = DateTimeOffset.UtcNow,
            access = new
            {
                canView = true,
                canManage = await HasManageAuthorityAsync(context),
                isViewAs = IsViewAs(context),
                viewAsTransfersMutationAuthority = false
            },
            security = new
            {
                credentialsAreWriteOnly = true,
                encryptedStoreRequired = true,
                httpsEndpointsRequired = true,
                connectionTestsAreExplicit = true,
                secretsReturned = false
            },
            providers
        });
    }

    private static async Task<IResult> CreateProviderAsync(HttpContext context)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        var body = await ReadBodyAsync<ProviderRequest>(context);
        if (body.Value is null) return body.Failure!;
        var validation = ValidateProviderRequest(body.Value, creating: true);
        if (validation is not null) return validation;

        var providerKey = NormalizeProviderKey(body.Value.ProviderKey!);
        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return SchemaUnavailable();

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using (var command = new NpgsqlCommand("""
                INSERT INTO crm_integration_providers (
                    provider_key, provider_name, provider_type, provider_status, auth_model,
                    configuration_scope, secret_storage_policy, base_url, health_check_url,
                    oauth_authorization_url, oauth_token_url, oauth_client_id, oauth_scopes,
                    api_key_header, api_key_prefix, is_builtin, is_enabled,
                    availability_status, notes, created_by, updated_by
                ) VALUES (
                    @key, @name, @type, 'native_configuration', @auth,
                    'server_side_only', 'encrypted_write_only', @base_url, @health_url,
                    @authorization_url, @token_url, @client_id, @scopes,
                    @api_key_header, @api_key_prefix, FALSE, @enabled,
                    'not_configured', @notes, @actor, @actor
                );
                """, connection, transaction))
            {
                BindProvider(command, providerKey, body.Value, ActualUserId(context)!.Value);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await SecurityDiagnosticsOperations.WriteAuditAsync(
                connection,
                transaction,
                ModuleNumber,
                "crm_erp_provider",
                providerKey,
                "provider_created",
                ActualUserId(context)!.Value,
                ProviderAuditEvidence(body.Value, providerKey),
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "provider_created",
                providerKey,
                secretValueReturned = false,
                message = $"{body.Value.ProviderName!.Trim()} was added. Save its write-only credential, then test the connection."
            }, statusCode: StatusCodes.Status201Created);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Results.Conflict(new { module = ModuleNumber, status = "provider_exists", message = "That provider key already exists." });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "create provider");
            return OperationUnavailable("The provider could not be created.");
        }
    }

    private static async Task<IResult> UpdateProviderAsync(string providerKey, HttpContext context)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        providerKey = NormalizeProviderKey(providerKey);
        if (string.IsNullOrWhiteSpace(providerKey)) return Invalid("A valid provider key is required.");
        var body = await ReadBodyAsync<ProviderRequest>(context);
        if (body.Value is null) return body.Failure!;
        var validation = ValidateProviderRequest(body.Value, creating: false);
        if (validation is not null) return validation;

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return SchemaUnavailable();

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            int changed;
            await using (var command = new NpgsqlCommand("""
                UPDATE crm_integration_providers
                SET provider_name = @name,
                    provider_type = @type,
                    auth_model = @auth,
                    base_url = @base_url,
                    health_check_url = @health_url,
                    oauth_authorization_url = @authorization_url,
                    oauth_token_url = @token_url,
                    oauth_client_id = @client_id,
                    oauth_scopes = @scopes,
                    api_key_header = @api_key_header,
                    api_key_prefix = @api_key_prefix,
                    is_enabled = @enabled,
                    notes = @notes,
                    availability_status = CASE WHEN @enabled THEN 'not_configured' ELSE 'disabled' END,
                    last_error_code = '',
                    updated_by = @actor,
                    updated_at = NOW()
                WHERE provider_key = @key;
                """, connection, transaction))
            {
                BindProvider(command, providerKey, body.Value, ActualUserId(context)!.Value);
                changed = await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            if (changed == 0)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.NotFound(new { module = ModuleNumber, status = "provider_not_found", message = "The integration provider was not found." });
            }

            await SecurityDiagnosticsOperations.WriteAuditAsync(
                connection,
                transaction,
                ModuleNumber,
                "crm_erp_provider",
                providerKey,
                "provider_configuration_updated",
                ActualUserId(context)!.Value,
                ProviderAuditEvidence(body.Value, providerKey),
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "provider_updated",
                providerKey,
                secretValueReturned = false,
                message = "Integration metadata was saved. Credential values were not changed."
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "update provider");
            return OperationUnavailable("The provider configuration could not be saved.");
        }
    }

    private static async Task<IResult> ReplaceCredentialAsync(string providerKey, HttpContext context)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        providerKey = NormalizeProviderKey(providerKey);
        var body = await ReadBodyAsync<CredentialRequest>(context);
        if (body.Value is null) return body.Failure!;
        var secret = body.Value.Secret?.Trim();
        if (string.IsNullOrWhiteSpace(secret)) return Invalid("A write-only credential is required.");
        if (Encoding.UTF8.GetByteCount(secret) > MaximumSecretBytes) return Invalid("The credential is too large.");

        var encryptionKey = ReadEncryptionKey();
        if (encryptionKey is null)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "secure_store_unavailable",
                message = "PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY must be configured as a base64-encoded 32-byte key."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return SchemaUnavailable();
        var authModel = await ReadAuthModelAsync(connection, providerKey, context.RequestAborted);
        if (authModel is null) return Results.NotFound(new { module = ModuleNumber, status = "provider_not_found", message = "The integration provider was not found." });
        var credentialKind = authModel == "api_key" ? "api_key" : "oauth_client_secret";

        try
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await SaveCredentialAsync(
                connection,
                transaction,
                providerKey,
                credentialKind,
                secret,
                null,
                ActualUserId(context)!.Value,
                encryptionKey,
                context.RequestAborted);
            await using (var update = new NpgsqlCommand("""
                UPDATE crm_integration_providers
                SET availability_status = 'not_configured', last_error_code = '', updated_by = @actor, updated_at = NOW()
                WHERE provider_key = @provider;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("actor", ActualUserId(context)!.Value);
                update.Parameters.AddWithValue("provider", providerKey);
                await update.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await SecurityDiagnosticsOperations.WriteAuditAsync(
                connection,
                transaction,
                ModuleNumber,
                "crm_erp_credential",
                providerKey,
                "credential_replaced",
                ActualUserId(context)!.Value,
                new { providerKey, credentialKind, valueReturned = false },
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "credential_replaced",
                providerKey,
                credentialKind,
                configured = true,
                valueReturned = false,
                message = authModel == "api_key"
                    ? "The API key was encrypted and saved. Its value cannot be viewed after saving."
                    : "The OAuth client secret was encrypted and saved. Its value cannot be viewed after saving."
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "replace credential");
            return OperationUnavailable("The credential could not be saved securely.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static async Task<IResult> StartOAuthAsync(string providerKey, HttpContext context)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        providerKey = NormalizeProviderKey(providerKey);
        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return SchemaUnavailable();
        var provider = await ReadProviderConfigurationAsync(connection, providerKey, context.RequestAborted);
        if (provider is null) return Results.NotFound(new { module = ModuleNumber, status = "provider_not_found", message = "The integration provider was not found." });
        if (provider.AuthModel != "oauth2") return Invalid("This provider is configured for API-key authentication.");
        if (string.IsNullOrWhiteSpace(provider.OAuthClientId)
            || !TryHttpsUri(provider.OAuthAuthorizationUrl, out var authorizationUri)
            || !TryHttpsUri(provider.OAuthTokenUrl, out _))
        {
            return Invalid("OAuth client ID, authorization URL, and token URL must be configured before connecting.");
        }
        if (!await CredentialExistsAsync(connection, providerKey, "oauth_client_secret", context.RequestAborted))
        {
            return Invalid("Save the write-only OAuth client secret before connecting.");
        }

        var redirectUri = PublicCallbackUri(context);
        if (redirectUri is null) return Invalid("A valid HTTPS ProjectPulse public base URL is required for the OAuth callback.");
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var stateHash = Sha256(state);
        await using (var command = new NpgsqlCommand("""
            INSERT INTO crm_integration_oauth_states
                (state_hash, provider_key, actor_user_id, redirect_uri, expires_at)
            VALUES (@state_hash, @provider, @actor, @redirect_uri, NOW() + INTERVAL '10 minutes');
            """, connection))
        {
            command.Parameters.AddWithValue("state_hash", stateHash);
            command.Parameters.AddWithValue("provider", providerKey);
            command.Parameters.AddWithValue("actor", ActualUserId(context)!.Value);
            command.Parameters.AddWithValue("redirect_uri", redirectUri.ToString());
            await command.ExecuteNonQueryAsync(context.RequestAborted);
        }

        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = provider.OAuthClientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["state"] = state
        };
        if (!string.IsNullOrWhiteSpace(provider.OAuthScopes)) parameters["scope"] = provider.OAuthScopes;
        var query = string.Join("&", parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var separator = string.IsNullOrEmpty(authorizationUri!.Query) ? "?" : "&";

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "oauth_authorization_ready",
            providerKey,
            authorizationUrl = $"{authorizationUri}{separator}{query}",
            expiresInSeconds = 600,
            callbackUri = redirectUri,
            message = "Complete consent with the provider. ProjectPulse will store returned tokens encrypted and will not display them."
        });
    }

    private static async Task<IResult> CompleteOAuthAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var code = context.Request.Query["code"].ToString();
        var state = context.Request.Query["state"].ToString();
        var providerError = context.Request.Query["error"].ToString();
        if (string.IsNullOrWhiteSpace(state)) return OAuthPage(false, "The OAuth state is missing or invalid.");

        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return OAuthPage(false, "ProjectPulse integration storage is unavailable.");
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return OAuthPage(false, "Module 026 migration 034 has not been applied.");

        OAuthState? oauthState;
        await using (var command = new NpgsqlCommand("""
            UPDATE crm_integration_oauth_states
            SET used_at = NOW()
            WHERE crm_integration_oauth_states.state_hash = @state_hash
              AND crm_integration_oauth_states.used_at IS NULL
              AND crm_integration_oauth_states.expires_at > NOW()
            RETURNING crm_integration_oauth_states.provider_key,
                      crm_integration_oauth_states.actor_user_id,
                      crm_integration_oauth_states.redirect_uri;
            """, connection))
        {
            command.Parameters.AddWithValue("state_hash", Sha256(state));
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            oauthState = await reader.ReadAsync(context.RequestAborted)
                ? new OAuthState(reader.GetString(0), reader.GetGuid(1), reader.GetString(2))
                : null;
        }
        if (oauthState is null) return OAuthPage(false, "This OAuth request is expired, invalid, or already used.");

        if (!string.IsNullOrWhiteSpace(providerError)) return OAuthPage(false, "The provider did not authorize the connection.");
        if (string.IsNullOrWhiteSpace(code)) return OAuthPage(false, "The provider did not return an authorization code.");

        var provider = await ReadProviderConfigurationAsync(connection, oauthState.ProviderKey, context.RequestAborted);
        if (provider is null || !TryHttpsUri(provider.OAuthTokenUrl, out var tokenUri)) return OAuthPage(false, "The provider token endpoint is not configured.");
        if (!await IsSafeExternalUriAsync(tokenUri!, context.RequestAborted)) return OAuthPage(false, "The provider token endpoint is not an approved public HTTPS address.");

        var encryptionKey = ReadEncryptionKey();
        if (encryptionKey is null) return OAuthPage(false, "The ProjectPulse integration encryption key is unavailable.");
        try
        {
            var clientSecret = await LoadCredentialAsync(connection, oauthState.ProviderKey, "oauth_client_secret", encryptionKey, context.RequestAborted);
            if (string.IsNullOrWhiteSpace(clientSecret)) return OAuthPage(false, "The OAuth client secret is not configured.");

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["client_id"] = provider.OAuthClientId,
                    ["client_secret"] = clientSecret,
                    ["redirect_uri"] = oauthState.RedirectUri
                })
            };
            var client = httpClientFactory.CreateClient("Module026");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            var payload = await ReadBoundedResponseBodyAsync(response.Content, context.RequestAborted);
            if (payload is null) return OAuthPage(false, "The provider OAuth response exceeded the allowed size.");
            if (!response.IsSuccessStatusCode) return OAuthPage(false, "The provider rejected the OAuth token exchange.");

            using var document = JsonDocument.Parse(payload);
            var accessToken = JsonText(document.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken)) return OAuthPage(false, "The provider response did not contain an access token.");
            var refreshToken = JsonText(document.RootElement, "refresh_token");
            var instanceUrl = JsonText(document.RootElement, "instance_url");
            var expiresIn = JsonInteger(document.RootElement, "expires_in");
            var expiresAt = expiresIn is > 0 ? DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value) : (DateTimeOffset?)null;
            var tokenEnvelope = JsonSerializer.Serialize(new { accessToken, refreshToken, instanceUrl, expiresAt });

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await SaveCredentialAsync(
                connection,
                transaction,
                oauthState.ProviderKey,
                "oauth_token",
                tokenEnvelope,
                expiresAt,
                oauthState.ActorUserId,
                encryptionKey,
                context.RequestAborted);
            await using (var update = new NpgsqlCommand("""
                UPDATE crm_integration_providers
                SET provider_status = 'connected', availability_status = 'not_configured',
                    last_error_code = '', updated_by = @actor, updated_at = NOW()
                WHERE provider_key = @provider;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("actor", oauthState.ActorUserId);
                update.Parameters.AddWithValue("provider", oauthState.ProviderKey);
                await update.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await SecurityDiagnosticsOperations.WriteAuditAsync(
                connection,
                transaction,
                ModuleNumber,
                "crm_erp_oauth_connection",
                oauthState.ProviderKey,
                "oauth_connected",
                oauthState.ActorUserId,
                new { providerKey = oauthState.ProviderKey, tokenStored = true, tokenReturned = false },
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return OAuthPage(true, "The provider is connected. Return to Module 026 and run a connection test.");
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "complete OAuth connection");
            return OAuthPage(false, "ProjectPulse could not complete the OAuth connection.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }
    }

    private static async Task<IResult> TestProviderAsync(string providerKey, HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var authorization = await AuthorizeManageAsync(context);
        if (authorization is not null) return authorization;
        if (!SameOrigin(context)) return OriginRejected();

        providerKey = NormalizeProviderKey(providerKey);
        await using var connection = await OpenConnectionAsync(context);
        if (connection is null) return DependencyUnavailable();
        if (!await SchemaAvailableAsync(connection, context.RequestAborted)) return SchemaUnavailable();
        var provider = await ReadProviderConfigurationAsync(connection, providerKey, context.RequestAborted);
        if (provider is null) return Results.NotFound(new { module = ModuleNumber, status = "provider_not_found", message = "The integration provider was not found." });
        if (!provider.IsEnabled) return Invalid("Enable the provider before testing its connection.");
        if (!TryHttpsUri(provider.HealthCheckUrl, out var healthUri)) return Invalid("A public HTTPS health-check URL is required.");
        if (!await IsSafeExternalUriAsync(healthUri!, context.RequestAborted)) return Invalid("The health-check URL must resolve to a public HTTPS address.");

        var encryptionKey = ReadEncryptionKey();
        if (encryptionKey is null) return OperationUnavailable("The integration credential store is unavailable.");
        var stopwatch = Stopwatch.StartNew();
        string availability;
        string errorCode = string.Empty;
        int? statusCode = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, healthUri);
            if (provider.AuthModel == "api_key")
            {
                var apiKey = await LoadCredentialAsync(connection, providerKey, "api_key", encryptionKey, context.RequestAborted);
                if (string.IsNullOrWhiteSpace(apiKey)) return Invalid("Save the write-only API key before testing.");
                var value = string.IsNullOrWhiteSpace(provider.ApiKeyPrefix) ? apiKey : $"{provider.ApiKeyPrefix.Trim()} {apiKey}";
                request.Headers.TryAddWithoutValidation(provider.ApiKeyHeader, value);
            }
            else
            {
                var envelope = await LoadCredentialAsync(connection, providerKey, "oauth_token", encryptionKey, context.RequestAborted);
                if (string.IsNullOrWhiteSpace(envelope)) return Invalid("Complete OAuth authorization before testing.");
                using var document = JsonDocument.Parse(envelope);
                var accessToken = JsonText(document.RootElement, "accessToken");
                if (string.IsNullOrWhiteSpace(accessToken)) return Invalid("The saved OAuth token is invalid. Connect the provider again.");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            var client = httpClientFactory.CreateClient("Module026");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            statusCode = (int)response.StatusCode;
            availability = response.IsSuccessStatusCode
                ? "available"
                : response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "authentication_failed"
                    : "unavailable";
            errorCode = availability switch
            {
                "authentication_failed" => "remote_authentication_rejected",
                "unavailable" => "remote_non_success_status",
                _ => string.Empty
            };
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            availability = "unavailable";
            errorCode = "connection_timeout";
        }
        catch (HttpRequestException)
        {
            availability = "unavailable";
            errorCode = "connection_failed";
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "test provider connection");
            availability = "unavailable";
            errorCode = "connection_test_failed";
        }
        finally
        {
            stopwatch.Stop();
            CryptographicOperations.ZeroMemory(encryptionKey);
        }

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        await using (var command = new NpgsqlCommand("""
            INSERT INTO crm_integration_connection_checks
                (provider_key, availability_status, http_status_code, duration_ms, error_code, checked_by)
            VALUES (@provider, @availability, @status_code, @duration_ms, @error_code, @actor);

            UPDATE crm_integration_providers
            SET availability_status = @availability,
                last_checked_at = NOW(),
                last_available_at = CASE WHEN @availability = 'available' THEN NOW() ELSE last_available_at END,
                last_status_code = @status_code,
                last_error_code = @error_code,
                updated_by = @actor,
                updated_at = NOW()
            WHERE provider_key = @provider;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("provider", providerKey);
            command.Parameters.AddWithValue("availability", availability);
            command.Parameters.AddWithValue("status_code", statusCode.HasValue ? (object)statusCode.Value : DBNull.Value);
            command.Parameters.AddWithValue("duration_ms", (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue));
            command.Parameters.AddWithValue("error_code", errorCode);
            command.Parameters.AddWithValue("actor", ActualUserId(context)!.Value);
            await command.ExecuteNonQueryAsync(context.RequestAborted);
        }
        await SecurityDiagnosticsOperations.WriteAuditAsync(
            connection,
            transaction,
            ModuleNumber,
            "crm_erp_connection_check",
            providerKey,
            "connection_tested",
            ActualUserId(context)!.Value,
            new { providerKey, availability, statusCode, durationMs = stopwatch.ElapsedMilliseconds, errorCode },
            context.RequestAborted);
        await transaction.CommitAsync(context.RequestAborted);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "connection_tested",
            providerKey,
            availabilityStatus = availability,
            statusCode,
            durationMs = stopwatch.ElapsedMilliseconds,
            errorCode,
            checkedAt = DateTimeOffset.UtcNow,
            secretValueReturned = false
        });
    }

    private static async Task<IResult?> AuthorizeViewAsync(HttpContext context) =>
        await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            ModuleNumber,
            ViewRoles,
            ["VIEW_INTEGRATIONS_026", "MANAGE_INTEGRATIONS_026", "MANAGE_ALL"]);

    private static async Task<IResult?> AuthorizeManageAsync(HttpContext context)
    {
        if (IsViewAs(context)) return Results.Forbid();
        return await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            ModuleNumber,
            ManageRoles,
            ["MANAGE_INTEGRATIONS_026", "MANAGE_ALL"]);
    }

    private static async Task<bool> HasManageAuthorityAsync(HttpContext context)
    {
        if (IsViewAs(context)) return false;
        return await GovernedOperationsReadModule.AuthorizeAsync(
            context,
            ModuleNumber,
            ManageRoles,
            ["MANAGE_INTEGRATIONS_026", "MANAGE_ALL"]) is null;
    }

    private static async Task<NpgsqlConnection?> OpenConnectionAsync(HttpContext context)
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            return connection;
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "open integration storage");
            return null;
        }
    }

    private static async Task<bool> SchemaAvailableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('public.crm_integration_providers') IS NOT NULL
               AND to_regclass('public.crm_integration_credentials') IS NOT NULL
               AND to_regclass('public.crm_integration_oauth_states') IS NOT NULL
               AND to_regclass('public.crm_integration_connection_checks') IS NOT NULL;
            """, connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<ProviderConfiguration?> ReadProviderConfigurationAsync(
        NpgsqlConnection connection,
        string providerKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT provider_key, provider_name, auth_model, base_url, health_check_url,
                   oauth_authorization_url, oauth_token_url, oauth_client_id, oauth_scopes,
                   api_key_header, api_key_prefix, is_enabled
            FROM crm_integration_providers
            WHERE provider_key = @provider;
            """, connection);
        command.Parameters.AddWithValue("provider", providerKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ProviderConfiguration(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetBoolean(11));
    }

    private static async Task<string?> ReadAuthModelAsync(NpgsqlConnection connection, string providerKey, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT auth_model FROM crm_integration_providers WHERE provider_key = @provider;",
            connection);
        command.Parameters.AddWithValue("provider", providerKey);
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private static async Task<bool> CredentialExistsAsync(
        NpgsqlConnection connection,
        string providerKey,
        string credentialKind,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM crm_integration_credentials
                WHERE provider_key = @provider AND credential_kind = @kind
            );
            """, connection);
        command.Parameters.AddWithValue("provider", providerKey);
        command.Parameters.AddWithValue("kind", credentialKind);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task SaveCredentialAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string providerKey,
        string credentialKind,
        string secret,
        DateTimeOffset? expiresAt,
        Guid actorUserId,
        byte[] encryptionKey,
        CancellationToken cancellationToken)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(encryptionKey, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes($"{ModuleNumber}:{providerKey}:{credentialKind}"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        await using var command = new NpgsqlCommand("""
            INSERT INTO crm_integration_credentials (
                provider_key, credential_kind, ciphertext, nonce, authentication_tag,
                credential_version, expires_at, rotated_at, rotated_by
            ) VALUES (
                @provider, @kind, @ciphertext, @nonce, @tag,
                @version, @expires_at, NOW(), @actor
            )
            ON CONFLICT (provider_key, credential_kind) DO UPDATE
            SET ciphertext = EXCLUDED.ciphertext,
                nonce = EXCLUDED.nonce,
                authentication_tag = EXCLUDED.authentication_tag,
                credential_version = EXCLUDED.credential_version,
                expires_at = EXCLUDED.expires_at,
                rotated_at = EXCLUDED.rotated_at,
                rotated_by = EXCLUDED.rotated_by;
            """, connection, transaction);
        command.Parameters.AddWithValue("provider", providerKey);
        command.Parameters.AddWithValue("kind", credentialKind);
        command.Parameters.AddWithValue("ciphertext", ciphertext);
        command.Parameters.AddWithValue("nonce", nonce);
        command.Parameters.AddWithValue("tag", tag);
        command.Parameters.AddWithValue("version", DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
        command.Parameters.AddWithValue("expires_at", expiresAt.HasValue ? (object)expiresAt.Value : DBNull.Value);
        command.Parameters.AddWithValue("actor", actorUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> LoadCredentialAsync(
        NpgsqlConnection connection,
        string providerKey,
        string credentialKind,
        byte[] encryptionKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT ciphertext, nonce, authentication_tag
            FROM crm_integration_credentials
            WHERE provider_key = @provider AND credential_kind = @kind;
            """, connection);
        command.Parameters.AddWithValue("provider", providerKey);
        command.Parameters.AddWithValue("kind", credentialKind);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var ciphertext = (byte[])reader[0];
        var nonce = (byte[])reader[1];
        var tag = (byte[])reader[2];
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(encryptionKey, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes($"{ModuleNumber}:{providerKey}:{credentialKind}"));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static IResult? ValidateProviderRequest(ProviderRequest request, bool creating)
    {
        if (creating && string.IsNullOrWhiteSpace(request.ProviderKey)) return Invalid("Provider key is required.");
        if (creating && NormalizeProviderKey(request.ProviderKey!).Length is < 2 or > 60) return Invalid("Provider key must contain 2 to 60 letters, numbers, or underscores.");
        if (string.IsNullOrWhiteSpace(request.ProviderName) || request.ProviderName.Trim().Length > 150) return Invalid("Provider name is required and must be 150 characters or fewer.");
        if (string.IsNullOrWhiteSpace(request.ProviderType) || request.ProviderType.Trim().Length > 75) return Invalid("Provider type is required and must be 75 characters or fewer.");
        if (request.AuthModel is not ("api_key" or "oauth2")) return Invalid("Authentication type must be api_key or oauth2.");
        foreach (var value in new[] { request.BaseUrl, request.HealthCheckUrl, request.OAuthAuthorizationUrl, request.OAuthTokenUrl })
        {
            if (!string.IsNullOrWhiteSpace(value) && !TryHttpsUri(value, out _)) return Invalid("Integration endpoints must use public HTTPS URLs.");
        }
        if (request.AuthModel == "api_key" && string.IsNullOrWhiteSpace(request.ApiKeyHeader)) return Invalid("API-key header name is required.");
        if (request.ApiKeyHeader?.Any(character => !char.IsLetterOrDigit(character) && character != '-') is true) return Invalid("API-key header name is invalid.");
        return null;
    }

    private static void BindProvider(NpgsqlCommand command, string providerKey, ProviderRequest request, Guid actor)
    {
        command.Parameters.AddWithValue("key", providerKey);
        command.Parameters.AddWithValue("name", request.ProviderName!.Trim());
        command.Parameters.AddWithValue("type", request.ProviderType!.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("auth", request.AuthModel!);
        command.Parameters.AddWithValue("base_url", Clean(request.BaseUrl, 1000));
        command.Parameters.AddWithValue("health_url", Clean(request.HealthCheckUrl, 1000));
        command.Parameters.AddWithValue("authorization_url", Clean(request.OAuthAuthorizationUrl, 1000));
        command.Parameters.AddWithValue("token_url", Clean(request.OAuthTokenUrl, 1000));
        command.Parameters.AddWithValue("client_id", Clean(request.OAuthClientId, 500));
        command.Parameters.AddWithValue("scopes", Clean(request.OAuthScopes, 1000));
        command.Parameters.AddWithValue("api_key_header", Clean(request.ApiKeyHeader, 100, "Authorization"));
        command.Parameters.AddWithValue("api_key_prefix", Clean(request.ApiKeyPrefix, 100));
        command.Parameters.AddWithValue("enabled", request.IsEnabled);
        command.Parameters.AddWithValue("notes", Clean(request.Notes, 2000));
        command.Parameters.AddWithValue("actor", actor);
    }

    private static object ProviderAuditEvidence(ProviderRequest request, string providerKey) => new
    {
        providerKey,
        request.ProviderName,
        request.ProviderType,
        request.AuthModel,
        request.IsEnabled,
        baseUrlConfigured = !string.IsNullOrWhiteSpace(request.BaseUrl),
        healthCheckConfigured = !string.IsNullOrWhiteSpace(request.HealthCheckUrl),
        oauthClientConfigured = !string.IsNullOrWhiteSpace(request.OAuthClientId),
        credentialValueIncluded = false
    };

    private static async Task<BodyOutcome<T>> ReadBodyAsync<T>(HttpContext context)
    {
        if (context.Request.ContentLength is > MaximumBodyBytes)
        {
            return new BodyOutcome<T>(default, Results.Json(new
            {
                module = ModuleNumber,
                status = "request_too_large",
                message = $"Request bodies are limited to {MaximumBodyBytes} bytes."
            }, statusCode: StatusCodes.Status413PayloadTooLarge));
        }
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(context.RequestAborted);
            return value is null
                ? new BodyOutcome<T>(default, Invalid("A valid JSON request is required."))
                : new BodyOutcome<T>(value, null);
        }
        catch (JsonException)
        {
            return new BodyOutcome<T>(default, Invalid("The JSON request body is invalid."));
        }
    }

    private static async Task<string?> ReadBoundedResponseBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumProviderResponseBytes) return null;

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(capacity: MaximumProviderResponseBytes);
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumProviderResponseBytes) return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private static Uri? PublicCallbackUri(HttpContext context)
    {
        var configured = Environment.GetEnvironmentVariable("PROJECTPULSE_PUBLIC_BASE_URL")?.Trim().TrimEnd('/');
        var baseValue = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : $"{context.Request.Scheme}://{context.Request.Host}";
        if (!Uri.TryCreate(baseValue, UriKind.Absolute, out var baseUri)) return null;
        if (baseUri.Scheme != Uri.UriSchemeHttps)
        {
            if (string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                return new Uri(baseUri, "/api/public/integrations/026/oauth/callback");
            if (!IPAddress.TryParse(baseUri.Host, out var address) || !IPAddress.IsLoopback(address))
                return null;
        }
        return new Uri(baseUri, "/api/public/integrations/026/oauth/callback");
    }

    private static bool TryHttpsUri(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(parsed.Host) || !string.IsNullOrWhiteSpace(parsed.UserInfo)) return false;
        uri = parsed;
        return true;
    }

    private static async Task<bool> IsSafeExternalUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrWhiteSpace(uri.UserInfo)) return false;
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            return addresses.Length > 0 && addresses.All(IsPublicAddress);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0) return false;
            if (bytes[0] == 169 && bytes[1] == 254) return false;
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return false;
            if (bytes[0] == 192 && bytes[1] == 168) return false;
            if (bytes[0] >= 224) return false;
            return true;
        }
        return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal && !address.IsIPv6Multicast;
    }

    private static bool SameOrigin(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var values)) return true;
        if (!Uri.TryCreate(values.ToString(), UriKind.Absolute, out var origin)) return false;
        return string.Equals(origin.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == (context.Request.Host.Port ?? (context.Request.IsHttps ? 443 : 80))
            && string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    private static Guid? ActualUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true;

    private static string NormalizeProviderKey(string value)
    {
        var normalized = new string(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());
        while (normalized.Contains("__", StringComparison.Ordinal)) normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        return normalized.Trim('_');
    }

    private static string Clean(string? value, int maximum, string fallback = "")
    {
        var cleaned = value?.Trim() ?? string.Empty;
        if (cleaned.Length > maximum) cleaned = cleaned[..maximum];
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }

    private static byte[]? ReadEncryptionKey()
    {
        try
        {
            var value = Environment.GetEnvironmentVariable("PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY");
            if (string.IsNullOrWhiteSpace(value)) return null;
            var key = Convert.FromBase64String(value.Trim());
            return key.Length == 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string? JsonText(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? JsonInteger(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection", "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime", "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private static IResult Invalid(string message) => Results.BadRequest(new { module = ModuleNumber, status = "invalid_request", message });
    private static IResult OriginRejected() => Results.Json(new { module = ModuleNumber, status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: StatusCodes.Status403Forbidden);
    private static IResult DependencyUnavailable() => Results.Json(new { module = ModuleNumber, status = "integration_storage_unavailable", message = "Module 026 storage is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult SchemaUnavailable() => Results.Json(new { module = ModuleNumber, status = "integration_schema_unavailable", migration = "034_module_026_crm_erp_integrations", message = "Module 026 migration 034 has not been applied." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult OperationUnavailable(string message) => Results.Json(new { module = ModuleNumber, status = "integration_operation_unavailable", message }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult OAuthPage(bool success, string message)
    {
        var safeMessage = WebUtility.HtmlEncode(message);
        var title = success ? "Connection complete" : "Connection not completed";
        var color = success ? "#166534" : "#991b1b";
        var html = $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{title}</title></head>
            <body style="font-family:system-ui,sans-serif;background:#f4f7fb;color:#17233d;padding:32px">
              <main style="max-width:620px;margin:auto;background:white;border:1px solid #d8e0ed;border-radius:18px;padding:28px">
                <p style="font-weight:800;color:{color}">MODULE 026</p><h1>{title}</h1><p>{safeMessage}</p>
                <button type="button" onclick="window.close()" style="padding:10px 16px;border-radius:10px;border:0;background:#0f2a55;color:white;font-weight:700">Close window</button>
              </main>
            </body></html>
            """;
        return Results.Content(html, "text/html", Encoding.UTF8, success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
    }

    private static void LogFailure(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CrmErpIntegrationModule")
            .LogWarning("Module 026 could not {Operation} ({ExceptionType}).", operation, exception.GetType().Name);
    }

    private sealed record ProviderRequest(
        string? ProviderKey,
        string? ProviderName,
        string? ProviderType,
        string? AuthModel,
        string? BaseUrl,
        string? HealthCheckUrl,
        string? OAuthAuthorizationUrl,
        string? OAuthTokenUrl,
        string? OAuthClientId,
        string? OAuthScopes,
        string? ApiKeyHeader,
        string? ApiKeyPrefix,
        bool IsEnabled,
        string? Notes);

    private sealed record CredentialRequest(string? Secret);
    private sealed record BodyOutcome<T>(T? Value, IResult? Failure);
    private sealed record OAuthState(string ProviderKey, Guid ActorUserId, string RedirectUri);
    private sealed record ProviderConfiguration(
        string ProviderKey,
        string ProviderName,
        string AuthModel,
        string BaseUrl,
        string HealthCheckUrl,
        string OAuthAuthorizationUrl,
        string OAuthTokenUrl,
        string OAuthClientId,
        string OAuthScopes,
        string ApiKeyHeader,
        string ApiKeyPrefix,
        bool IsEnabled);
}
