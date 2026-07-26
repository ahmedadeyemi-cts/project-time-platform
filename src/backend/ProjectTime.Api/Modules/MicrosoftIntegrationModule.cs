using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static class MicrosoftIntegrationModule
{
    private const string ModuleNumber = "065";
    private const string LegacyModuleNumber = "067";
    private const string ActiveRoute = "entra-secret-administration";
    private const string MigrationId = "045_microsoft_integration_consolidation";
    private const int MaximumSecretBytes = 4096;
    private static readonly SemaphoreSlim HydrationGate = new(1, 1);
    private static bool hydrationAttempted;

    private static readonly HashSet<string> AcceptedPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_ENTRA_SECRET",
        "VIEW_GLOBAL_MAIL_CONFIGURATION",
        "MANAGE_GLOBAL_MAIL_CONFIGURATION",
        "VIEW_GLOBAL_MAIL",
        "MANAGE_GLOBAL_MAIL"
    };

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/global-mail/configuration", (Func<HttpContext, Task<IResult>>)GetLegacyConfigurationAsync);
        app.MapGet("/api/global-mail/health", (Func<HttpContext, Task<IResult>>)GetLegacyHealthAsync);
        app.MapGet("/api/microsoft-integration/overview", (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        app.MapPut("/api/microsoft-integration/client-secret", (Func<HttpContext, Task<IResult>>)SaveClientSecretAsync);
        app.MapPost("/api/microsoft-integration/test-connection", (Func<HttpContext, Task<IResult>>)TestConnectionAsync);

        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(HydrateStoredSecretsAsync));
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);

        var tenant = await ReadActiveTenantAsync(connection, context.RequestAborted);
        var secretMetadata = await ReadSecretMetadataAsync(connection, context.RequestAborted);
        var module065Document = await ReadNativeDocumentAsync(connection, "065", context.RequestAborted);
        var legacy067Document = await ReadNativeDocumentAsync(connection, "067", context.RequestAborted);
        var sync = await ReadLatestSyncAsync(connection, context.RequestAborted);
        var secretReady = secretMetadata.Any(item => item.TenantKey.Equals(tenant.TenantKey, StringComparison.OrdinalIgnoreCase))
            || HasEnvironmentSecret(tenant.TenantKey);

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Microsoft Integration",
            status = "microsoft_integration_loaded",
            activeRoute = ActiveRoute,
            retiredModule = new
            {
                module = LegacyModuleNumber,
                route = "global-mail-configuration",
                retired = true,
                redirectRoute = ActiveRoute,
                configurationPreserved = legacy067Document is not null,
                apiCompatibilityPreserved = true
            },
            activeTenant = tenant,
            secretMetadata = secretMetadata.Select(item => new
            {
                item.TenantKey,
                item.Fingerprint,
                item.UpdatedAt,
                configured = true,
                valueReturned = false
            }),
            identityIntegration = new
            {
                status = !string.IsNullOrWhiteSpace(tenant.TenantId)
                    && !string.IsNullOrWhiteSpace(tenant.ClientId)
                    && secretReady
                        ? "ready"
                        : "configuration_incomplete",
                module062Preserved = true,
                environmentContractPreserved = true,
                directoryReadApplicationPermissions = new[] { "Directory.Read.All", "User.Read.All" },
                delegatedProfilePermission = "User.Read"
            },
            directorySync = new
            {
                status = tenant.DirectorySyncEnabled ? "enabled" : "disabled",
                tenant.SyncFrequencyHours,
                tenant.DefaultRoleCode,
                lastSyncAt = sync.LastSyncAt,
                lastSyncStatus = sync.Status,
                lastSyncMessage = sync.Message
            },
            sender = ReadMailConfiguration(module065Document, legacy067Document),
            access = new
            {
                actualUserId = access.Context.UserId,
                roles = access.Context.Roles.OrderBy(value => value),
                permissions = access.Context.Permissions.OrderBy(value => value),
                legacyModule067PermissionMapped = access.Context.Permissions.Any(permission =>
                    permission.Contains("GLOBAL_MAIL", StringComparison.OrdinalIgnoreCase)),
                viewAsTransfersAuthority = false
            },
            controls = new
            {
                secretValuesReturned = false,
                tokenValuesReturned = false,
                migration = MigrationId
            }
        });
    }

    private static async Task<IResult> GetLegacyConfigurationAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var tenant = await ReadActiveTenantAsync(connection, context.RequestAborted);
        var module065Document = await ReadNativeDocumentAsync(connection, "065", context.RequestAborted);
        var legacy067Document = await ReadNativeDocumentAsync(connection, "067", context.RequestAborted);
        var secrets = await ReadSecretMetadataAsync(connection, context.RequestAborted);

        return Results.Ok(new
        {
            module = ModuleNumber,
            legacyModule = LegacyModuleNumber,
            moduleName = "Microsoft Integration",
            status = "configuration_loaded",
            retired = true,
            redirectRoute = ActiveRoute,
            configuration = ReadMailConfiguration(module065Document, legacy067Document),
            tenant,
            secretMetadata = secrets.Select(item => new
            {
                item.TenantKey,
                item.Fingerprint,
                item.UpdatedAt,
                configured = true,
                valueReturned = false
            }),
            controls = new
            {
                secretValuesReturned = false,
                configurationMutationRoute = "/api/microsoft-integration/client-secret",
                connectionTestRoute = "/api/microsoft-integration/test-connection"
            }
        });
    }

    private static async Task<IResult> GetLegacyHealthAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var tenant = await ReadActiveTenantAsync(connection, context.RequestAborted);
        var secrets = await ReadSecretMetadataAsync(connection, context.RequestAborted);
        var checks = new[]
        {
            new HealthCheck("tenant", !string.IsNullOrWhiteSpace(tenant.TenantId), "Microsoft tenant identifier configured"),
            new HealthCheck("client", !string.IsNullOrWhiteSpace(tenant.ClientId), "Entra application/client identifier configured"),
            new HealthCheck("credential", secrets.Count > 0 || HasEnvironmentSecret(tenant.TenantKey), "Write-only client credential configured"),
            new HealthCheck("directory_permissions", true, "Directory.Read.All and User.Read.All application permissions expected"),
            new HealthCheck("delegated_identity", true, "User.Read delegated permission preserves signed-in profile integration")
        };

        return Results.Ok(new
        {
            module = ModuleNumber,
            legacyModule = LegacyModuleNumber,
            status = "mail_health_loaded",
            retired = true,
            redirectRoute = ActiveRoute,
            overallState = checks.All(check => check.Ready)
                ? "ready_for_connectivity_validation"
                : "configuration_incomplete",
            checks,
            providerRequestAttempted = false,
            messageSent = false
        });
    }

    private static async Task<IResult> SaveClientSecretAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (IsViewAs(context)) return ViewAsWriteForbidden();

        SecretWriteRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SecretWriteRequest>(cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid tenant key and client secret are required.");
        }

        var tenantKey = NormalizeTenantKey(request?.TenantKey);
        var secret = request?.ClientSecret?.Trim() ?? string.Empty;
        var plaintext = Encoding.UTF8.GetBytes(secret);
        try
        {
            if (string.IsNullOrWhiteSpace(tenantKey)) return InvalidRequest("A stable tenant key is required.");
            if (plaintext.Length < 8 || plaintext.Length > MaximumSecretBytes)
            {
                return InvalidRequest($"The client secret must be between 8 and {MaximumSecretBytes} bytes.");
            }

            var encryptionKey = ResolveEncryptionKey();
            if (encryptionKey is null)
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "secret_encryption_key_unavailable",
                    message = "Secure client-secret storage is unavailable. Configure the Microsoft Integration encryption key or database credential."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            var associatedData = Encoding.UTF8.GetBytes($"ProjectPulse:{ModuleNumber}:{tenantKey}");
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
                    INSERT INTO microsoft_integration_client_secrets (
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
                    ON CONFLICT (tenant_key)
                    DO UPDATE SET
                        ciphertext = EXCLUDED.ciphertext,
                        nonce = EXCLUDED.nonce,
                        authentication_tag = EXCLUDED.authentication_tag,
                        fingerprint_sha256 = EXCLUDED.fingerprint_sha256,
                        encryption_key_source = EXCLUDED.encryption_key_source,
                        updated_by_user_id = EXCLUDED.updated_by_user_id,
                        updated_at = NOW();
                    """, connection, transaction);
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
                    "CLIENT_SECRET_SAVED",
                    tenantKey,
                    "success",
                    context.TraceIdentifier,
                    new { fingerprint = fingerprint[..16], secretReturned = false },
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
                    message = "Microsoft Integration secure storage is not installed yet. Apply the approved migration before saving a client secret."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            ApplySecretToEnvironment(tenantKey, secret);
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "client_secret_saved",
                tenantKey,
                fingerprint = fingerprint[..16],
                secretStored = true,
                secretReturned = false,
                message = "Client secret saved securely. The value will not be displayed again and is available to the existing identity integration."
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static async Task<IResult> TestConnectionAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (IsViewAs(context)) return ViewAsWriteForbidden();

        ConnectionTestRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ConnectionTestRequest>(cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid Microsoft connection-test request is required.");
        }

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var observed = await ReadActiveTenantAsync(connection, context.RequestAborted);
        var tenantKey = NormalizeTenantKey(First(request?.TenantKey, observed.TenantKey, "default"));
        var tenantId = First(request?.TenantId, observed.TenantId);
        var clientId = First(request?.ClientId, observed.ClientId);
        var senderMailbox = First(request?.SenderMailbox, observed.SenderMailbox);
        var clientSecret = await ResolveClientSecretAsync(connection, tenantKey, context.RequestAborted);

        if (string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret))
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "configuration_incomplete",
                message = "Tenant ID, application/client ID, and a saved client secret are required before testing the connection.",
                secretReturned = false
            });
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var tokenContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            });
            using var tokenResponse = await client.PostAsync(
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/token",
                tokenContent,
                context.RequestAborted);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                await RecordTestAuditAsync(connection, access.Context, tenantKey, "token_failed", context.TraceIdentifier, context.RequestAborted);
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "token_acquisition_failed",
                    httpStatus = (int)tokenResponse.StatusCode,
                    message = "Microsoft rejected the application credentials. Verify the tenant ID, client ID, client secret, and admin consent.",
                    tokenReturned = false,
                    secretReturned = false
                }, statusCode: StatusCodes.Status502BadGateway);
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(context.RequestAborted);
            using var tokenDocument = JsonDocument.Parse(tokenJson);
            var token = JsonString(tokenDocument.RootElement, "access_token");
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("token_missing");

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var directoryResponse = await client.GetAsync(
                "https://graph.microsoft.com/v1.0/users?$top=1&$select=id,userPrincipalName",
                context.RequestAborted);
            var directoryReady = directoryResponse.IsSuccessStatusCode;

            object senderResult = new { status = "not_configured", checkedMailbox = false };
            if (!string.IsNullOrWhiteSpace(senderMailbox))
            {
                using var senderResponse = await client.GetAsync(
                    $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(senderMailbox)}?$select=id,mail,userPrincipalName",
                    context.RequestAborted);
                senderResult = new
                {
                    status = senderResponse.IsSuccessStatusCode ? "resolved" : "not_resolved",
                    checkedMailbox = true,
                    httpStatus = (int)senderResponse.StatusCode
                };
            }

            await RecordTestAuditAsync(
                connection,
                access.Context,
                tenantKey,
                directoryReady ? "success" : "directory_read_failed",
                context.TraceIdentifier,
                context.RequestAborted);

            return Results.Json(new
            {
                module = ModuleNumber,
                status = directoryReady ? "connection_test_passed" : "directory_permission_test_failed",
                message = directoryReady
                    ? "Microsoft Graph credentials and application directory-read permissions were validated."
                    : "Microsoft authentication succeeded, but the application could not read directory users. Verify Directory.Read.All and User.Read.All application admin consent.",
                directoryRead = new
                {
                    status = directoryReady ? "ready" : "forbidden_or_unavailable",
                    httpStatus = (int)directoryResponse.StatusCode,
                    requiredApplicationPermissions = new[] { "Directory.Read.All", "User.Read.All" }
                },
                senderMailbox = senderResult,
                delegatedProfilePermission = "User.Read",
                tokenReturned = false,
                secretReturned = false
            }, statusCode: directoryReady ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "request_cancelled",
                message = "The Microsoft connection test was cancelled.",
                tokenReturned = false,
                secretReturned = false
            }, statusCode: 499);
        }
        catch
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "connection_test_failed",
                message = "The Microsoft connection test could not be completed. No provider payload, token, or secret was returned.",
                tokenReturned = false,
                secretReturned = false
            }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<AccessResult> AuthorizeAsync(HttpContext context)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
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
            if (!administrator && !permissions.Any(AcceptedPermissions.Contains))
            {
                return new(null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "microsoft_integration_access_required",
                    message = "Administrator or delegated Microsoft Integration access is required."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new(new(userId.Value, ActualEmail(context), roles, permissions, connectionString), null);
        }
        catch
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static async Task<ActiveTenant> ReadActiveTenantAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        JsonElement? settings = null;
        try
        {
            await using var command = new NpgsqlCommand("SELECT to_jsonb(s)::text FROM azure_entra_settings s LIMIT 1;", connection);
            var raw = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (!string.IsNullOrWhiteSpace(raw))
            {
                using var document = JsonDocument.Parse(raw);
                settings = document.RootElement.Clone();
            }
        }
        catch
        {
            // Existing environment-variable identity configuration remains supported.
        }

        var sourceProvider = First(JsonStringAny(settings, "sourceProvider", "source_provider"), "ENTRA_ID_TEST");
        var environmentMode = First(
            JsonStringAny(settings, "environmentMode", "environment_mode"),
            sourceProvider.Contains("TEST", StringComparison.OrdinalIgnoreCase) ? "test" : "production");
        var tenantKey = environmentMode.Contains("prod", StringComparison.OrdinalIgnoreCase) ? "ussignal" : "onenecklab";

        return new(
            tenantKey,
            First(JsonStringAny(settings, "tenantName", "tenant_name"), environmentMode == "production" ? "US Signal Production" : "OneNeck Lab"),
            First(JsonStringAny(settings, "tenantDomain", "tenant_domain")),
            First(JsonStringAny(settings, "tenantId", "tenant_id"), EnvironmentValueForMode(environmentMode, "TENANT_ID")),
            First(JsonStringAny(settings, "clientId", "client_id"), EnvironmentValueForMode(environmentMode, "CLIENT_ID")),
            First(JsonStringAny(settings, "authorityUrl", "authority_url")),
            First(JsonStringAny(settings, "redirectUri", "redirect_uri")),
            First(JsonStringAny(settings, "graphScope", "graph_scope"), "User.Read.All Directory.Read.All"),
            sourceProvider,
            JsonBoolAny(settings, "syncEnabled", "sync_enabled") ?? false,
            JsonIntAny(settings, "syncFrequencyHours", "sync_frequency_hours") ?? 24,
            First(JsonStringAny(settings, "defaultRoleCode", "default_role_code"), "ENGINEERING"),
            First(JsonStringAny(settings, "senderMailbox", "sender_mailbox"), Environment.GetEnvironmentVariable("PROJECTPULSE_M365_SENDER_MAILBOX")));
    }

    private static async Task<SyncSnapshot> ReadLatestSyncAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT to_jsonb(r)::text FROM azure_entra_sync_runs r LIMIT 1;", connection);
            var raw = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(raw)) return new(null, "not_run", "No directory sync has been recorded.");
            using var document = JsonDocument.Parse(raw);
            return new(
                First(JsonStringAny(document.RootElement, "completedAt", "completed_at", "startedAt", "started_at")),
                First(JsonStringAny(document.RootElement, "status", "run_status"), "unknown"),
                First(JsonStringAny(document.RootElement, "message", "result_message"), "Directory sync result recorded."));
        }
        catch
        {
            return new(null, "not_observed", "Directory sync history is not available.");
        }
    }

    private static async Task<JsonElement?> ReadNativeDocumentAsync(
        NpgsqlConnection connection,
        string moduleNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT document_json::text
                FROM projectpulse_native_admin_documents
                WHERE module_number = @module_number
                  AND document_key = 'configuration'
                LIMIT 1;
                """, connection);
            command.Parameters.AddWithValue("module_number", moduleNumber);
            var raw = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(raw)) return null;
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static object ReadMailConfiguration(JsonElement? module065, JsonElement? module067)
    {
        var legacy = JsonObjectAny(module067, "configuration");
        var consolidated = ParseConsolidatedConfiguration(module065);
        var mail = JsonObjectAny(consolidated, "mail");
        return new
        {
            providerTarget = First(JsonStringAny(mail, "providerTarget"), JsonStringAny(legacy, "providerTarget"), "microsoft_graph"),
            smtpHost = First(JsonStringAny(mail, "smtpHost"), "smtp.office365.com"),
            smtpPort = JsonIntAny(mail, "smtpPort") ?? 587,
            senderName = First(JsonStringAny(mail, "senderName"), JsonStringAny(legacy, "senderName")),
            senderAddress = First(JsonStringAny(mail, "senderAddress"), JsonStringAny(legacy, "senderAddress"), Environment.GetEnvironmentVariable("PROJECTPULSE_M365_SENDER_MAILBOX")),
            replyToAddress = First(JsonStringAny(mail, "replyToAddress"), JsonStringAny(legacy, "replyToAddress")),
            recipientBoundary = First(JsonStringAny(mail, "recipientBoundary"), JsonStringAny(legacy, "recipientBoundary"), "test_only"),
            legacyModule067ConfigurationPreserved = module067 is not null
        };
    }

    private static JsonElement? ParseConsolidatedConfiguration(JsonElement? module065)
    {
        var configuration = JsonObjectAny(module065, "configuration");
        var notes = JsonStringAny(configuration, "notes");
        const string marker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";
        if (string.IsNullOrWhiteSpace(notes) || !notes.StartsWith(marker, StringComparison.Ordinal)) return null;
        try
        {
            using var document = JsonDocument.Parse(notes[marker.Length..]);
            return document.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<SecretMetadata>> ReadSecretMetadataAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var result = new List<SecretMetadata>();
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT tenant_key, fingerprint_sha256, updated_at
                FROM microsoft_integration_client_secrets
                ORDER BY tenant_key;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var fingerprint = reader.GetString(1);
                var updatedAt = reader.GetDateTime(2);
                result.Add(new(
                    reader.GetString(0),
                    fingerprint[..Math.Min(16, fingerprint.Length)],
                    new DateTimeOffset(DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc))));
            }
        }
        catch
        {
            // Migration not yet applied or secure storage unavailable.
        }
        return result;
    }

    private static async Task<string> ResolveClientSecretAsync(NpgsqlConnection connection, string tenantKey, CancellationToken cancellationToken)
    {
        var environmentSecret = EnvironmentSecret(tenantKey);
        if (!string.IsNullOrWhiteSpace(environmentSecret)) return environmentSecret;

        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT ciphertext, nonce, authentication_tag
                FROM microsoft_integration_client_secrets
                WHERE tenant_key = @tenant_key;
                """, connection);
            command.Parameters.AddWithValue("tenant_key", tenantKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return string.Empty;

            var key = ResolveEncryptionKey();
            if (key is null) return string.Empty;
            var ciphertext = (byte[])reader[0];
            var nonce = (byte[])reader[1];
            var tag = (byte[])reader[2];
            var plaintext = new byte[ciphertext.Length];
            var associatedData = Encoding.UTF8.GetBytes($"ProjectPulse:{ModuleNumber}:{tenantKey}");
            try
            {
                using var aes = new AesGcm(key.Key, tag.Length);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task HydrateStoredSecretsAsync()
    {
        if (hydrationAttempted) return;
        await HydrationGate.WaitAsync();
        try
        {
            if (hydrationAttempted) return;
            hydrationAttempted = true;
            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString)) return;
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            foreach (var tenantKey in new[] { "onenecklab", "ussignal", "default" })
            {
                var secret = await ResolveClientSecretAsync(connection, tenantKey, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(secret)) ApplySecretToEnvironment(tenantKey, secret);
            }
        }
        catch
        {
            // Startup preserves the existing environment-variable identity path.
        }
        finally
        {
            HydrationGate.Release();
        }
    }

    private static void ApplySecretToEnvironment(string tenantKey, string secret)
    {
        Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET", secret);
        if (tenantKey.Contains("ussignal", StringComparison.OrdinalIgnoreCase)
            || tenantKey.Contains("prod", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET", secret);
            Environment.SetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET", secret);
        }
        else
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET", secret);
        }
    }

    private static string EnvironmentSecret(string tenantKey)
    {
        if (tenantKey.Contains("ussignal", StringComparison.OrdinalIgnoreCase)
            || tenantKey.Contains("prod", StringComparison.OrdinalIgnoreCase))
        {
            return First(
                Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET"),
                Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET"));
        }
        return First(
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET"));
    }

    private static bool HasEnvironmentSecret(string tenantKey) => !string.IsNullOrWhiteSpace(EnvironmentSecret(tenantKey));

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
            // Non-base64 values are stretched with SHA-256 and never returned.
        }
        return new(SHA256.HashData(Encoding.UTF8.GetBytes($"ProjectPulse-Microsoft-Integration:{configured}")), source);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AccessContext access,
        string action,
        string tenantKey,
        string outcome,
        string correlationId,
        object metadata,
        CancellationToken cancellationToken)
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
                @outcome,
                @correlation,
                CAST(@metadata AS jsonb)
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("actor", access.UserId);
        command.Parameters.AddWithValue("email", access.Email);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("tenant_key", tenantKey);
        command.Parameters.AddWithValue("outcome", outcome);
        command.Parameters.AddWithValue("correlation", correlationId ?? string.Empty);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecordTestAuditAsync(
        NpgsqlConnection connection,
        AccessContext access,
        string tenantKey,
        string outcome,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var exists = new NpgsqlCommand("SELECT to_regclass('public.microsoft_integration_audit_events') IS NOT NULL;", connection);
            if (!Convert.ToBoolean(await exists.ExecuteScalarAsync(cancellationToken))) return;
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await InsertAuditAsync(
                connection,
                transaction,
                access,
                "MICROSOFT_CONNECTION_TEST",
                tenantKey,
                outcome,
                correlationId,
                new { tokenReturned = false, secretReturned = false },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // The sanitized test result remains authoritative when optional audit storage is unavailable.
        }
    }

    private static string EnvironmentValueForMode(string mode, string suffix)
    {
        var prefix = mode.Contains("prod", StringComparison.OrdinalIgnoreCase)
            ? "PROJECTPULSE_ENTRA_PRODUCTION_"
            : "PROJECTPULSE_ENTRA_TEST_";
        return First(
            Environment.GetEnvironmentVariable(prefix + suffix),
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_" + suffix));
    }

    private static string NormalizeTenantKey(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is < 1 or > 100) return string.Empty;
        return normalized.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? normalized
            : string.Empty;
    }

    private static IResult InvalidRequest(string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_request",
        message
    });

    private static IResult ViewAsWriteForbidden() => Results.Json(new
    {
        module = ModuleNumber,
        status = "view_as_read_only",
        message = "Exit Administrator View-As before changing Microsoft Integration."
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

    private static string JsonString(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static string JsonStringAny(JsonElement? element, params string[] names)
    {
        if (element is null) return string.Empty;
        return names.Select(name => JsonString(element.Value, name))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static JsonElement? JsonObjectAny(JsonElement? element, params string[] names)
    {
        if (element is null) return null;
        foreach (var name in names)
        {
            if (TryProperty(element.Value, name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                return value.Clone();
            }
        }
        return null;
    }

    private static bool? JsonBoolAny(JsonElement? element, params string[] names)
    {
        if (element is null) return null;
        foreach (var name in names)
        {
            if (!TryProperty(element.Value, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static int? JsonIntAny(JsonElement? element, params string[] names)
    {
        if (element is null) return null;
        foreach (var name in names)
        {
            if (!TryProperty(element.Value, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric)) return numeric;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out numeric)) return numeric;
        }
        return null;
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record AccessResult(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(
        Guid UserId,
        string Email,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions,
        string ConnectionString);
    private sealed record SecretWriteRequest(string TenantKey, string ClientSecret);
    private sealed record ConnectionTestRequest(string TenantKey, string TenantId, string ClientId, string? SenderMailbox);
    private sealed record EncryptionKey(byte[] Key, string Source);
    private sealed record SecretMetadata(string TenantKey, string Fingerprint, DateTimeOffset UpdatedAt);
    private sealed record HealthCheck(string Code, bool Ready, string Description);
    private sealed record SyncSnapshot(string? LastSyncAt, string Status, string Message);
    private sealed record ActiveTenant(
        string TenantKey,
        string TenantName,
        string TenantDomain,
        string TenantId,
        string ClientId,
        string AuthorityUrl,
        string RedirectUri,
        string GraphScopes,
        string SourceProvider,
        bool DirectorySyncEnabled,
        int SyncFrequencyHours,
        string DefaultRoleCode,
        string SenderMailbox);
}
