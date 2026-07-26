using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Additive Module 065 support for separate SSO App Registration connections.
/// Existing Graph/services credentials and environment contracts remain owned by
/// MicrosoftIntegrationModule and are not migrated or overwritten here.
/// </summary>
public static class MicrosoftSsoConnectionProfilesModule
{
    private const string ModuleNumber = "065";
    private const string MigrationId = "046_microsoft_sso_connection_profiles";
    private const int MaximumSecretBytes = 4096;

    private static readonly HashSet<string> ReadPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_ENTRA_SECRET",
        "VIEW_GLOBAL_MAIL_CONFIGURATION",
        "MANAGE_GLOBAL_MAIL_CONFIGURATION",
        "VIEW_GLOBAL_MAIL",
        "MANAGE_GLOBAL_MAIL"
    };

    private static readonly HashSet<string> WritePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_ENTRA_SECRET",
        "MANAGE_GLOBAL_MAIL_CONFIGURATION",
        "MANAGE_GLOBAL_MAIL"
    };

    public static WebApplication MapMicrosoftSsoConnectionProfileEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/microsoft-integration/sso-readiness",
            (Func<HttpContext, Task<IResult>>)GetReadinessAsync);
        app.MapPut(
            "/api/microsoft-integration/sso-client-secret",
            (Func<HttpContext, Task<IResult>>)SaveSsoSecretAsync);
        app.MapPost(
            "/api/microsoft-integration/sso-test",
            (Func<HttpContext, Task<IResult>>)TestSsoConfigurationAsync);

        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(HydrateSsoProfilesAsync));
        return app;
    }

    private static async Task<IResult> GetReadinessAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, write: false);
        if (access.Failure is not null) return access.Failure;

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var profiles = await ReadProfilesAsync(connection, context.RequestAborted);
        var metadata = await ReadSecretMetadataAsync(connection, context.RequestAborted);

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "microsoft_sso_profiles_loaded",
            migration = MigrationId,
            connectionModel = new
            {
                test = new[] { "sso_app_registration", "microsoft_services_enterprise_application" },
                production = new[] { "sso_app_registration", "microsoft_services_enterprise_application" },
                graphServicesCompatibilityPreserved = true,
                module010CompatibilityPreserved = true,
                module057CompatibilityPreserved = true,
                module062CompatibilityPreserved = true
            },
            profiles = profiles.Select(profile => new
            {
                profile.EnvironmentMode,
                profile.TenantKey,
                profile.TenantDomain,
                profile.TenantId,
                profile.SsoClientId,
                profile.AuthorityUrl,
                profile.RedirectUri,
                profile.AllowedDomains,
                metadataConfigured = !string.IsNullOrWhiteSpace(profile.TenantId)
                    && !string.IsNullOrWhiteSpace(profile.SsoClientId)
                    && !string.IsNullOrWhiteSpace(profile.RedirectUri),
                secretConfigured = metadata.ContainsKey(profile.EnvironmentMode),
                secretFingerprint = metadata.TryGetValue(profile.EnvironmentMode, out var item)
                    ? item.Fingerprint[..Math.Min(16, item.Fingerprint.Length)]
                    : string.Empty,
                secretUpdatedAt = metadata.TryGetValue(profile.EnvironmentMode, out item)
                    ? item.UpdatedAt
                    : (DateTimeOffset?)null,
                secretReturned = false
            }),
            controls = new
            {
                graphServiceSecretStoreChanged = false,
                ssoSecretValuesReturned = false,
                interactiveSignInRequiredForFinalUat = true
            }
        });
    }

    private static async Task<IResult> SaveSsoSecretAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, write: true);
        if (access.Failure is not null) return access.Failure;
        if (IsViewAs(context)) return ViewAsForbidden();

        SsoSecretRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SsoSecretRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid Test or Production SSO secret request is required.");
        }

        var environmentMode = NormalizeEnvironment(request?.EnvironmentMode);
        var tenantKey = NormalizeTenantKey(request?.TenantKey);
        var secret = request?.ClientSecret?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(environmentMode))
            return InvalidRequest("Environment must be Test or Production.");
        if (string.IsNullOrWhiteSpace(tenantKey))
            return InvalidRequest("A stable tenant key is required.");

        var plaintext = Encoding.UTF8.GetBytes(secret);
        try
        {
            if (plaintext.Length < 8 || plaintext.Length > MaximumSecretBytes)
                return InvalidRequest($"The client secret must be between 8 and {MaximumSecretBytes} bytes.");

            var encryptionKey = ResolveEncryptionKey();
            if (encryptionKey is null)
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "secret_encryption_key_unavailable",
                    message = "Secure SSO secret storage is unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            var associatedData = AssociatedData(environmentMode, tenantKey);
            using (var aes = new AesGcm(encryptionKey.Key, tag.Length))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }
            var fingerprint = Convert.ToHexString(SHA256.HashData(plaintext)).ToLowerInvariant();

            await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            try
            {
                await using var command = new NpgsqlCommand("""
                    INSERT INTO microsoft_integration_sso_client_secrets (
                        environment_mode,
                        tenant_key,
                        ciphertext,
                        nonce,
                        authentication_tag,
                        fingerprint_sha256,
                        encryption_key_source,
                        updated_by_user_id,
                        created_at,
                        updated_at
                    )
                    VALUES (
                        @environment_mode,
                        @tenant_key,
                        @ciphertext,
                        @nonce,
                        @tag,
                        @fingerprint,
                        @key_source,
                        @actor,
                        NOW(),
                        NOW()
                    )
                    ON CONFLICT (environment_mode)
                    DO UPDATE SET
                        tenant_key = EXCLUDED.tenant_key,
                        ciphertext = EXCLUDED.ciphertext,
                        nonce = EXCLUDED.nonce,
                        authentication_tag = EXCLUDED.authentication_tag,
                        fingerprint_sha256 = EXCLUDED.fingerprint_sha256,
                        encryption_key_source = EXCLUDED.encryption_key_source,
                        updated_by_user_id = EXCLUDED.updated_by_user_id,
                        updated_at = NOW();
                    """, connection, transaction);
                command.Parameters.AddWithValue("environment_mode", environmentMode);
                command.Parameters.AddWithValue("tenant_key", tenantKey);
                command.Parameters.AddWithValue("ciphertext", ciphertext);
                command.Parameters.AddWithValue("nonce", nonce);
                command.Parameters.AddWithValue("tag", tag);
                command.Parameters.AddWithValue("fingerprint", fingerprint);
                command.Parameters.AddWithValue("key_source", encryptionKey.Source);
                command.Parameters.AddWithValue("actor", access.Context.UserId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);

                await InsertAuditAsync(
                    connection,
                    transaction,
                    access.Context,
                    "SSO_CLIENT_SECRET_SAVED",
                    tenantKey,
                    environmentMode,
                    context.TraceIdentifier,
                    new { connectionPurpose = "sso", fingerprint = fingerprint[..16], secretReturned = false },
                    context.RequestAborted);
                await transaction.CommitAsync(context.RequestAborted);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "migration_required",
                    migration = MigrationId,
                    message = "Separate SSO App Registration storage is not installed yet."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            ApplySsoSecretEnvironment(environmentMode, secret);
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "sso_client_secret_saved",
                environmentMode,
                tenantKey,
                fingerprint = fingerprint[..16],
                connectionPurpose = "sso_app_registration",
                servicesConnectionChanged = false,
                secretStored = true,
                secretReturned = false,
                message = $"{DisplayEnvironment(environmentMode)} SSO App Registration secret saved without changing the Microsoft services connection."
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static async Task<IResult> TestSsoConfigurationAsync(HttpContext context)
    {
        var access = await ResolveAccessAsync(context, write: true);
        if (access.Failure is not null) return access.Failure;
        if (IsViewAs(context)) return ViewAsForbidden();

        SsoTestRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SsoTestRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid SSO configuration-test request is required.");
        }

        var environmentMode = NormalizeEnvironment(request?.EnvironmentMode);
        var tenantKey = NormalizeTenantKey(request?.TenantKey);
        var tenantId = request?.TenantId?.Trim() ?? string.Empty;
        var clientId = request?.ClientId?.Trim() ?? string.Empty;
        var authorityUrl = NormalizeAuthority(request?.AuthorityUrl, tenantId);
        var redirectUri = request?.RedirectUri?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(environmentMode)
            || string.IsNullOrWhiteSpace(tenantKey)
            || string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(redirectUri))
        {
            return InvalidRequest("Environment, tenant, SSO client ID, and redirect URI are required.");
        }

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var secretConfigured = await SsoSecretExistsAsync(connection, environmentMode, context.RequestAborted)
            || HasSsoEnvironmentSecret(environmentMode);

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var discoveryUrl = authorityUrl.TrimEnd('/') + "/v2.0/.well-known/openid-configuration";
            using var response = await client.GetAsync(discoveryUrl, context.RequestAborted);
            var discoveryReady = response.IsSuccessStatusCode;
            var authorizationEndpointObserved = false;
            var tokenEndpointObserved = false;
            if (discoveryReady)
            {
                var raw = await response.Content.ReadAsStringAsync(context.RequestAborted);
                using var document = JsonDocument.Parse(raw);
                authorizationEndpointObserved = !string.IsNullOrWhiteSpace(JsonString(document.RootElement, "authorization_endpoint"));
                tokenEndpointObserved = !string.IsNullOrWhiteSpace(JsonString(document.RootElement, "token_endpoint"));
            }

            var ready = discoveryReady
                && authorizationEndpointObserved
                && tokenEndpointObserved
                && secretConfigured;
            await InsertAuditAsync(
                connection,
                transaction: null,
                access.Context,
                "SSO_CONFIGURATION_TEST",
                tenantKey,
                environmentMode,
                context.TraceIdentifier,
                new
                {
                    connectionPurpose = "sso",
                    discoveryReady,
                    authorizationEndpointObserved,
                    tokenEndpointObserved,
                    secretConfigured,
                    tokenReturned = false,
                    secretReturned = false
                },
                context.RequestAborted);

            return Results.Json(new
            {
                module = ModuleNumber,
                status = ready ? "sso_configuration_ready" : "sso_configuration_incomplete",
                environmentMode,
                connectionPurpose = "sso_app_registration",
                discoveryReady,
                authorizationEndpointObserved,
                tokenEndpointObserved,
                secretConfigured,
                redirectUriConfigured = true,
                interactiveSignInRequired = true,
                tokenReturned = false,
                secretReturned = false,
                servicesConnectionChanged = false,
                message = ready
                    ? "SSO metadata and write-only credential readiness were verified. Complete an interactive sign-in for final validation."
                    : "SSO readiness is incomplete. Verify the App Registration metadata and saved SSO secret."
            }, statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
        }
        catch
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "sso_metadata_unavailable",
                environmentMode,
                connectionPurpose = "sso_app_registration",
                interactiveSignInRequired = true,
                tokenReturned = false,
                secretReturned = false,
                message = "Microsoft OpenID metadata could not be verified. No secret or provider response was returned."
            }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task HydrateSsoProfilesAsync()
    {
        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var profiles = await ReadProfilesAsync(connection, CancellationToken.None);
            var activeMode = Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE")?.Trim().ToLowerInvariant();
            foreach (var profile in profiles)
            {
                ApplySsoMetadataEnvironment(profile, activeMode);
            }

            await using var command = new NpgsqlCommand("""
                SELECT environment_mode, tenant_key, ciphertext, nonce, authentication_tag
                FROM microsoft_integration_sso_client_secrets
                ORDER BY environment_mode;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync();
            var encryptionKey = ResolveEncryptionKey();
            if (encryptionKey is null) return;
            try
            {
                while (await reader.ReadAsync())
                {
                    var environmentMode = reader.GetString(0);
                    var tenantKey = reader.GetString(1);
                    var ciphertext = (byte[])reader[2];
                    var nonce = (byte[])reader[3];
                    var tag = (byte[])reader[4];
                    var plaintext = new byte[ciphertext.Length];
                    try
                    {
                        using var aes = new AesGcm(encryptionKey.Key, tag.Length);
                        aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(environmentMode, tenantKey));
                        ApplySsoSecretEnvironment(environmentMode, Encoding.UTF8.GetString(plaintext));
                    }
                    catch
                    {
                        // One invalid SSO credential must not block the other environment.
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plaintext);
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encryptionKey.Key);
            }
        }
        catch
        {
            // Existing environment-based SSO and Graph connections remain untouched.
        }
    }

    private static async Task<IReadOnlyList<SsoProfile>> ReadProfilesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var profiles = new Dictionary<string, SsoProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["test"] = DefaultProfile("test"),
            ["production"] = DefaultProfile("production")
        };

        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT document_json::text
                FROM projectpulse_native_admin_documents
                WHERE module_number = '065'
                  AND document_key = 'configuration'
                LIMIT 1;
                """, connection);
            var raw = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(raw)) return profiles.Values.ToArray();
            using var document = JsonDocument.Parse(raw);
            if (!TryProperty(document.RootElement, "configuration", out var configuration))
                return profiles.Values.ToArray();
            var notes = JsonString(configuration, "notes");
            const string marker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";
            if (!notes.StartsWith(marker, StringComparison.Ordinal)) return profiles.Values.ToArray();
            using var configDocument = JsonDocument.Parse(notes[marker.Length..]);
            if (!TryProperty(configDocument.RootElement, "tenants", out var tenants)
                || tenants.ValueKind != JsonValueKind.Array)
                return profiles.Values.ToArray();

            foreach (var tenant in tenants.EnumerateArray())
            {
                var environmentMode = NormalizeEnvironment(JsonStringAny(tenant, "environmentMode", "environment_mode"));
                if (string.IsNullOrWhiteSpace(environmentMode)) continue;
                var current = profiles[environmentMode];
                var sso = JsonObjectAny(tenant, "sso", "ssoConnection");
                profiles[environmentMode] = current with
                {
                    TenantKey = First(JsonStringAny(tenant, "key", "tenantKey"), current.TenantKey),
                    TenantDomain = First(JsonStringAny(tenant, "tenantDomain", "tenant_domain"), current.TenantDomain),
                    TenantId = First(JsonStringAny(tenant, "tenantId", "tenant_id"), current.TenantId),
                    SsoClientId = First(
                        JsonStringAny(sso, "clientId", "applicationId"),
                        JsonStringAny(tenant, "ssoClientId", "sso_client_id")),
                    AuthorityUrl = First(
                        JsonStringAny(sso, "authorityUrl", "authority"),
                        JsonStringAny(tenant, "authorityUrl", "authority_url"),
                        current.AuthorityUrl),
                    RedirectUri = First(
                        JsonStringAny(sso, "redirectUri", "callbackUri"),
                        JsonStringAny(tenant, "redirectUri", "redirect_uri")),
                    AllowedDomains = First(
                        JsonStringAny(sso, "allowedDomains"),
                        JsonStringAny(tenant, "ssoAllowedDomains"),
                        current.AllowedDomains)
                };
            }
        }
        catch
        {
            // Defaults and existing environment values remain authoritative.
        }

        return new[] { profiles["test"], profiles["production"] };
    }

    private static SsoProfile DefaultProfile(string environmentMode)
    {
        var production = environmentMode == "production";
        var prefix = production ? "PROJECTPULSE_ENTRA_PRODUCTION_SSO_" : "PROJECTPULSE_ENTRA_TEST_SSO_";
        var tenantId = FirstEnvironment(prefix + "TENANT_ID");
        return new(
            environmentMode,
            production ? "ussignal" : "onenecklab",
            production ? "ussignal.com" : "onenecklab.com",
            tenantId,
            FirstEnvironment(prefix + "CLIENT_ID"),
            First(
                FirstEnvironment(prefix + "AUTHORITY"),
                string.IsNullOrWhiteSpace(tenantId) ? string.Empty : $"https://login.microsoftonline.com/{tenantId}"),
            FirstEnvironment(prefix + "REDIRECT_URI"),
            First(
                FirstEnvironment(prefix + "ALLOWED_DOMAINS"),
                production ? "ussignal.com" : "onenecklab.com,onitdemo.com"));
    }

    private static void ApplySsoMetadataEnvironment(SsoProfile profile, string? activeMode)
    {
        var prefix = profile.EnvironmentMode == "production"
            ? "PROJECTPULSE_ENTRA_PRODUCTION_SSO_"
            : "PROJECTPULSE_ENTRA_TEST_SSO_";
        SetIfPresent(prefix + "TENANT_ID", profile.TenantId);
        SetIfPresent(prefix + "CLIENT_ID", profile.SsoClientId);
        SetIfPresent(prefix + "AUTHORITY", profile.AuthorityUrl);
        SetIfPresent(prefix + "REDIRECT_URI", profile.RedirectUri);
        SetIfPresent(prefix + "ALLOWED_DOMAINS", profile.AllowedDomains);

        var normalizedActive = NormalizeEnvironment(activeMode);
        if (normalizedActive == profile.EnvironmentMode)
        {
            SetIfPresent("PROJECTPULSE_SSO_TENANT_ID", profile.TenantId);
            SetIfPresent("PROJECTPULSE_SSO_CLIENT_ID", profile.SsoClientId);
            SetIfPresent("PROJECTPULSE_SSO_AUTHORITY", profile.AuthorityUrl);
            SetIfPresent("PROJECTPULSE_SSO_REDIRECT_URI", profile.RedirectUri);
            SetIfPresent("PROJECTPULSE_SSO_ALLOWED_DOMAINS", profile.AllowedDomains);
        }
    }

    private static void ApplySsoSecretEnvironment(string environmentMode, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return;
        var production = environmentMode == "production";
        Environment.SetEnvironmentVariable(
            production ? "PROJECTPULSE_ENTRA_PRODUCTION_SSO_CLIENT_SECRET" : "PROJECTPULSE_ENTRA_TEST_SSO_CLIENT_SECRET",
            secret);
        var activeMode = NormalizeEnvironment(Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"));
        if (activeMode == environmentMode)
            Environment.SetEnvironmentVariable("PROJECTPULSE_SSO_CLIENT_SECRET", secret);
    }

    private static async Task<Dictionary<string, SecretMetadata>> ReadSecretMetadataAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SecretMetadata>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT environment_mode, fingerprint_sha256, updated_at
                FROM microsoft_integration_sso_client_secrets;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result[reader.GetString(0)] = new(reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Migration readiness is represented as no stored metadata.
        }
        return result;
    }

    private static async Task<bool> SsoSecretExistsAsync(
        NpgsqlConnection connection,
        string environmentMode,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM microsoft_integration_sso_client_secrets
                    WHERE environment_mode = @environment_mode
                );
                """, connection);
            command.Parameters.AddWithValue("environment_mode", environmentMode);
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
        }
        catch
        {
            return false;
        }
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AccessContext access,
        string action,
        string tenantKey,
        string environmentMode,
        string correlationId,
        object metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO microsoft_integration_audit_events (
                    actor_user_id,
                    actor_email,
                    action_code,
                    tenant_key,
                    outcome_code,
                    correlation_id,
                    event_metadata
                )
                VALUES (
                    @actor,
                    @email,
                    @action,
                    @tenant_key,
                    'success',
                    @correlation,
                    CAST(@metadata AS jsonb)
                );
                """, connection, transaction);
            command.Parameters.AddWithValue("actor", access.UserId);
            command.Parameters.AddWithValue("email", access.Email);
            command.Parameters.AddWithValue("action", action);
            command.Parameters.AddWithValue("tenant_key", tenantKey);
            command.Parameters.AddWithValue("correlation", correlationId ?? string.Empty);
            command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new { environmentMode, metadata }));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Optional audit storage cannot expose or block the sanitized result.
        }
    }

    private static async Task<AccessResult> ResolveAccessAsync(HttpContext context, bool write)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return new(null, Results.Json(new
            {
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(r.role_code, ''), COALESCE(p.permission_code, '')
                FROM app_user_role_assignments ura
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
                LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
                WHERE ura.user_id = @user_id
                  AND ura.is_active = TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                if (!reader.IsDBNull(0) && !string.IsNullOrWhiteSpace(reader.GetString(0))) roles.Add(reader.GetString(0));
                if (!reader.IsDBNull(1) && !string.IsNullOrWhiteSpace(reader.GetString(1))) permissions.Add(reader.GetString(1));
            }

            var administrator = roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR");
            var allowed = administrator || permissions.Any((write ? WritePermissions : ReadPermissions).Contains);
            if (!allowed)
            {
                return new(null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = write ? "microsoft_integration_manage_access_required" : "microsoft_integration_access_required",
                    message = write
                        ? "Manage Microsoft Integration authority is required."
                        : "Microsoft Integration access is required."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new(new(userId.Value, ActualEmail(context), connectionString), null);
        }
        catch
        {
            return new(null, Results.Json(new
            {
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static string BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
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
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return string.Empty;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 10
        }.ConnectionString;
    }

    private static EncryptionKey? ResolveEncryptionKey()
    {
        var configured = Environment.GetEnvironmentVariable("PROJECTPULSE_MICROSOFT_INTEGRATION_SECRET_KEY");
        var source = "dedicated_environment_key";
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
            source = "database_credential_derived_key";
        }
        if (string.IsNullOrWhiteSpace(configured)) return null;
        try
        {
            var decoded = Convert.FromBase64String(configured);
            if (decoded.Length == 32) return new(decoded, source);
            CryptographicOperations.ZeroMemory(decoded);
        }
        catch
        {
            // Non-base64 values are stretched without being returned.
        }
        return new(SHA256.HashData(Encoding.UTF8.GetBytes($"ProjectPulse-Microsoft-SSO:{configured}")), source);
    }

    private static byte[] AssociatedData(string environmentMode, string tenantKey) =>
        Encoding.UTF8.GetBytes($"ProjectPulse:{ModuleNumber}:SSO:{environmentMode}:{tenantKey}");

    private static string NormalizeEnvironment(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "test" or "development" or "dev" or "onenecklab" => "test",
            "production" or "prod" or "ussignal" => "production",
            _ => string.Empty
        };
    }

    private static string NormalizeTenantKey(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 100) return string.Empty;
        return normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? normalized
            : string.Empty;
    }

    private static string NormalizeAuthority(string? value, string tenantId) =>
        First(value, string.IsNullOrWhiteSpace(tenantId) ? string.Empty : $"https://login.microsoftonline.com/{tenantId}");

    private static bool HasSsoEnvironmentSecret(string environmentMode) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
            environmentMode == "production"
                ? "PROJECTPULSE_ENTRA_PRODUCTION_SSO_CLIENT_SECRET"
                : "PROJECTPULSE_ENTRA_TEST_SSO_CLIENT_SECRET"));

    private static void SetIfPresent(string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Environment.SetEnvironmentVariable(name, value);
    }

    private static string DisplayEnvironment(string value) => value == "production" ? "Production" : "Test";

    private static IResult InvalidRequest(string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_request",
        message
    });

    private static IResult ViewAsForbidden() => Results.Json(new
    {
        module = ModuleNumber,
        status = "view_as_read_only",
        message = "Exit Administrator View-As before changing or testing SSO credentials."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static Guid? ActualSessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string ActualEmail(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualEmail", "ProjectPulseSessionEmail" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (!string.IsNullOrWhiteSpace(value?.ToString())) return value!.ToString()!.Trim().ToLowerInvariant();
        }
        return "unknown";
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static string JsonString(JsonElement element, string name)
    {
        if (!TryProperty(element, name, out var value) || value.ValueKind != JsonValueKind.String)
            return string.Empty;
        return value.GetString()?.Trim() ?? string.Empty;
    }

    private static string JsonStringAny(JsonElement? element, params string[] names)
    {
        if (element is null) return string.Empty;
        return names.Select(name => JsonString(element.Value, name))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static JsonElement? JsonObjectAny(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Object)
                return value.Clone();
        }
        return null;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string FirstEnvironment(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return string.Empty;
    }

    private sealed record AccessResult(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(Guid UserId, string Email, string ConnectionString);
    private sealed record SsoSecretRequest(string EnvironmentMode, string TenantKey, string ClientSecret);
    private sealed record SsoTestRequest(
        string EnvironmentMode,
        string TenantKey,
        string TenantId,
        string ClientId,
        string AuthorityUrl,
        string RedirectUri);
    private sealed record EncryptionKey(byte[] Key, string Source);
    private sealed record SecretMetadata(string Fingerprint, DateTimeOffset UpdatedAt);
    private sealed record SsoProfile(
        string EnvironmentMode,
        string TenantKey,
        string TenantDomain,
        string TenantId,
        string SsoClientId,
        string AuthorityUrl,
        string RedirectUri,
        string AllowedDomains);
}
