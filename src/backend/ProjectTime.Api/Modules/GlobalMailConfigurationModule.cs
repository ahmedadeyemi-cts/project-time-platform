using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 065 Microsoft Integration and the retired Module 067 compatibility surface.
/// Module 010 remains the owner of directory preview, selection, and import UX.
/// </summary>
public static class GlobalMailConfigurationModule
{
    private const string ActiveModuleNumber = "065";
    private const string LegacyModuleNumber = "067";
    private const string ActiveRoute = "entra-secret-administration";
    private const string MigrationId = "045_microsoft_integration_consolidation";
    private const int MaximumSecretBytes = 4096;
    private static readonly SemaphoreSlim HydrationGate = new(1, 1);
    private static bool hydrationAttempted;

    private static readonly HashSet<string> MicrosoftIntegrationPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_ENTRA_SECRET",
        "VIEW_GLOBAL_MAIL_CONFIGURATION",
        "MANAGE_GLOBAL_MAIL_CONFIGURATION",
        "VIEW_GLOBAL_MAIL",
        "MANAGE_GLOBAL_MAIL"
    };

    private static readonly HashSet<string> DirectoryImportPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_AZURE_AD",
        "MANAGE_AZURE_SYNC"
    };

    public static WebApplication MapGlobalMailConfigurationEndpoints(this WebApplication app)
    {
        // Module 067 API compatibility remains available, but reports its Module 065 owner.
        app.MapGet(
            "/api/global-mail/configuration",
            (Func<HttpContext, Task<IResult>>)GetLegacyConfigurationAsync);
        app.MapGet(
            "/api/global-mail/health",
            (Func<HttpContext, Task<IResult>>)GetLegacyHealthAsync);

        app.MapGet(
            "/api/microsoft-integration/overview",
            (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        app.MapPut(
            "/api/microsoft-integration/client-secret",
            (Func<HttpContext, Task<IResult>>)SaveClientSecretAsync);
        app.MapPost(
            "/api/microsoft-integration/test-connection",
            (Func<HttpContext, Task<IResult>>)TestConnectionAsync);

        // Unique endpoint avoids ambiguity with any legacy inline Module 010 route.
        app.MapPost(
            "/api/microsoft-integration/directory-users/import-selected",
            (Func<HttpContext, Task<IResult>>)ImportSelectedDirectoryUsersAsync);

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(HydrateStoredSecretsAsync);
        });

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context, DirectoryImportPermissions, MicrosoftIntegrationPermissions);
        if (access.Failure is not null) return access.Failure;

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);

        var activeTenant = await ReadActiveTenantAsync(connection, context.RequestAborted);
        var secretMetadata = await ReadSecretMetadataAsync(connection, context.RequestAborted);
        var legacyDocument = await ReadNativeDocumentAsync(connection, "067", context.RequestAborted);
        var module065Document = await ReadNativeDocumentAsync(connection, "065", context.RequestAborted);
        var sync = await ReadLatestSyncAsync(connection, context.RequestAborted);

        var secretReady = secretMetadata.Any(item =>
            string.Equals(item.TenantKey, activeTenant.TenantKey, StringComparison.OrdinalIgnoreCase))
            || HasEnvironmentSecret(activeTenant.TenantKey);
        var identityReady = !string.IsNullOrWhiteSpace(activeTenant.TenantId)
            && !string.IsNullOrWhiteSpace(activeTenant.ClientId)
            && secretReady;

        return Results.Ok(new
        {
            module = ActiveModuleNumber,
            moduleName = "Microsoft Integration",
            status = "microsoft_integration_loaded",
            activeRoute = ActiveRoute,
            retiredModule = new
            {
                module = LegacyModuleNumber,
                route = "global-mail-configuration",
                retired = true,
                redirectRoute = ActiveRoute,
                configurationPreserved = legacyDocument is not null,
                apiCompatibilityPreserved = true
            },
            activeTenant,
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
                status = identityReady ? "ready" : "configuration_incomplete",
                module062Preserved = true,
                environmentContractPreserved = true,
                directoryReadApplicationPermissions = new[] { "Directory.Read.All", "User.Read.All" },
                delegatedProfilePermission = "User.Read"
            },
            directorySync = new
            {
                status = activeTenant.DirectorySyncEnabled ? "enabled" : "disabled",
                activeTenant.SyncFrequencyHours,
                activeTenant.DefaultRoleCode,
                lastSyncAt = sync.LastSyncAt,
                lastSyncStatus = sync.Status,
                lastSyncMessage = sync.Message
            },
            sender = ReadMailConfiguration(module065Document, legacyDocument),
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
                importedUsersPersistTo = "app_users",
                migration = MigrationId
            }
        });
    }

    private static async Task<IResult> GetLegacyConfigurationAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context, MicrosoftIntegrationPermissions);
        if (access.Failure is not null) return access.Failure;

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);

        var module065Document = await ReadNativeDocumentAsync(connection, "065", context.RequestAborted);
        var legacyDocument = await ReadNativeDocumentAsync(connection, "067", context.RequestAborted);
        var activeTenant = await ReadActiveTenantAsync(connection, context.RequestAborted);
        var secrets = await ReadSecretMetadataAsync(connection, context.RequestAborted);

        return Results.Ok(new
        {
            module = ActiveModuleNumber,
            legacyModule = LegacyModuleNumber,
            moduleName = "Microsoft Integration",
            status = "configuration_loaded",
            retired = true,
            redirectRoute = ActiveRoute,
            configuration = ReadMailConfiguration(module065Document, legacyDocument),
            tenant = activeTenant,
            secretMetadata = secrets.Select(item => new
            {
                item.TenantKey,
                item.Fingerprint,
                item.UpdatedAt,
                configured = true
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
        var access = await AuthorizeAsync(context, MicrosoftIntegrationPermissions);
        if (access.Failure is not null) return access.Failure;

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var tenant = await ReadActiveTenantAsync(connection, context.RequestAborted);
        var secrets = await ReadSecretMetadataAsync(connection, context.RequestAborted);
        var secretReady = secrets.Count > 0 || HasEnvironmentSecret(tenant.TenantKey);

        var checks = new[]
        {
            Check("tenant", !string.IsNullOrWhiteSpace(tenant.TenantId), "Microsoft tenant identifier configured"),
            Check("client", !string.IsNullOrWhiteSpace(tenant.ClientId), "Entra application/client identifier configured"),
            Check("credential", secretReady, "Write-only client credential configured"),
            Check("directory_permissions", true, "Directory.Read.All and User.Read.All application permissions expected"),
            Check("delegated_identity", true, "User.Read delegated permission preserves signed-in profile integration")
        };

        return Results.Ok(new
        {
            module = ActiveModuleNumber,
            legacyModule = LegacyModuleNumber,
            status = "mail_health_loaded",
            retired = true,
            redirectRoute = ActiveRoute,
            overallState = checks.All(check => check.Ready) ? "ready_for_connectivity_validation" : "configuration_incomplete",
            checks,
            providerRequestAttempted = false,
            messageSent = false
        });
    }

    private static async Task<IResult> SaveClientSecretAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context, MicrosoftIntegrationPermissions);
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
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            if (string.IsNullOrWhiteSpace(tenantKey)) return InvalidRequest("A stable tenant key is required.");
            if (secretBytes.Length < 8 || secretBytes.Length > MaximumSecretBytes)
            {
                return InvalidRequest($"The client secret must be between 8 and {MaximumSecretBytes} bytes.");
            }

            var key = ResolveEncryptionKey();
            if (key is null)
            {
                return Results.Json(new
                {
                    module = ActiveModuleNumber,
                    status = "secret_encryption_key_unavailable",
                    message = "Secure client-secret storage is unavailable. Configure the Microsoft Integration encryption key or database credential."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[secretBytes.Length];
            var tag = new byte[16];
            var associatedData = Encoding.UTF8.GetBytes($"ProjectPulse:{ActiveModuleNumber}:{tenantKey}");
            using (var aes = new AesGcm(key.Value.Key, tag.Length))
            {
                aes.Encrypt(nonce, secretBytes, ciphertext, tag, associatedData);
            }

            var fingerprint = Convert.ToHexString(SHA256.HashData(secretBytes)).ToLowerInvariant();
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
                command.Parameters.AddWithValue("key_source", key.Value.Source);
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
                    module = ActiveModuleNumber,
                    status = "migration_required",
                    migration = MigrationId,
                    message = "Microsoft Integration secure storage is not installed yet. Apply the approved migration before saving a client secret."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            ApplySecretToEnvironment(tenantKey, secret);

            return Results.Ok(new
            {
                module = ActiveModuleNumber,
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
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    private static async Task<IResult> TestConnectionAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context, MicrosoftIntegrationPermissions);
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
        var secret = await ResolveClientSecretAsync(connection, tenantKey, context.RequestAborted);

        if (string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(secret))
        {
            return Results.BadRequest(new
            {
                module = ActiveModuleNumber,
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
                ["client_secret"] = secret,
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
                    module = ActiveModuleNumber,
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
                module = ActiveModuleNumber,
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
                delegatedIdentityPermission = "User.Read",
                tokenReturned = false,
                secretReturned = false
            }, statusCode: directoryReady ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.Json(new
            {
                module = ActiveModuleNumber,
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
                module = ActiveModuleNumber,
                status = "connection_test_failed",
                message = "The Microsoft connection test could not be completed. No provider payload, token, or secret was returned.",
                tokenReturned = false,
                secretReturned = false
            }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> ImportSelectedDirectoryUsersAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context, DirectoryImportPermissions);
        if (access.Failure is not null) return access.Failure;
        if (IsViewAs(context)) return ViewAsWriteForbidden();

        JsonDocument payloadDocument;
        try
        {
            payloadDocument = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        }
        catch
        {
            return InvalidRequest("A valid selected-user import payload is required.");
        }

        using (payloadDocument)
        {
            var root = payloadDocument.RootElement;
            var candidates = ExtractCandidates(root);
            var selectedIdentifiers = ExtractSelectedIdentifiers(root);
            var defaultRoleCode = First(
                JsonStringAny(root, "defaultRoleCode", "default_role_code", "roleCode", "role_code"),
                "ENGINEERING").ToUpperInvariant();

            if (candidates.Count == 0)
            {
                return Results.BadRequest(new
                {
                    module = "010",
                    status = "no_selected_users_received",
                    imported = 0,
                    skipped = 0,
                    duplicate = 0,
                    failed = 0,
                    message = "The import request did not include selected user records. Preview users must be included with their compatible Entra identifiers."
                });
            }

            await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
            await connection.OpenAsync(context.RequestAborted);
            var appUserColumns = await ReadColumnsAsync(connection, "app_users", context.RequestAborted);
            if (!appUserColumns.Contains("user_id") || !appUserColumns.Contains("email"))
            {
                return Results.Json(new
                {
                    module = "010",
                    status = "app_users_schema_unavailable",
                    message = "The ProjectPulse user directory schema is unavailable for import."
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var settings = await ReadActiveTenantAsync(connection, context.RequestAborted);
            if (defaultRoleCode == "ENGINEERING" && !string.IsNullOrWhiteSpace(settings.DefaultRoleCode))
            {
                defaultRoleCode = settings.DefaultRoleCode.ToUpperInvariant();
            }

            var assignmentColumns = await ReadColumnsAsync(connection, "app_user_role_assignments", context.RequestAborted);
            var roleId = await ResolveRoleIdAsync(connection, defaultRoleCode, context.RequestAborted);
            var outcomes = new List<ImportOutcome>();
            var requestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = NormalizeCandidate(candidates[index], settings.SourceProvider);
                var savepoint = $"module010_import_{index}";
                await ExecuteTransactionControlAsync(connection, transaction, $"SAVEPOINT {savepoint};", context.RequestAborted);

                try
                {
                    if (string.IsNullOrWhiteSpace(candidate.Email))
                    {
                        outcomes.Add(new(candidate.Key, candidate.Email, candidate.DisplayName, "failed", "missing_email", null, "not_attempted"));
                        await ExecuteTransactionControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    if (!candidate.AccountEnabled)
                    {
                        outcomes.Add(new(candidate.Key, candidate.Email, candidate.DisplayName, "skipped", "account_disabled", null, "not_attempted"));
                        await ExecuteTransactionControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    if (selectedIdentifiers.Count > 0 && !CandidateWasSelected(candidate, selectedIdentifiers))
                    {
                        outcomes.Add(new(candidate.Key, candidate.Email, candidate.DisplayName, "skipped", "not_selected", null, "not_attempted"));
                        await ExecuteTransactionControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    if (!requestKeys.Add(candidate.Key))
                    {
                        outcomes.Add(new(candidate.Key, candidate.Email, candidate.DisplayName, "skipped", "duplicate_in_request", null, "not_attempted"));
                        await ExecuteTransactionControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                        continue;
                    }

                    var existingUserId = await FindExistingUserAsync(
                        connection,
                        transaction,
                        appUserColumns,
                        candidate,
                        context.RequestAborted);

                    Guid userId;
                    string status;
                    string resultCode;
                    if (existingUserId is not null)
                    {
                        userId = existingUserId.Value;
                        await UpdateAppUserAsync(connection, transaction, appUserColumns, userId, candidate, context.RequestAborted);
                        status = "duplicate";
                        resultCode = "existing_user_upserted";
                    }
                    else
                    {
                        userId = Guid.NewGuid();
                        await InsertAppUserAsync(connection, transaction, appUserColumns, userId, candidate, defaultRoleCode, context.RequestAborted);
                        status = "imported";
                        resultCode = "user_inserted";
                    }

                    var roleStatus = await EnsureRoleAssignmentAsync(
                        connection,
                        transaction,
                        assignmentColumns,
                        userId,
                        roleId,
                        access.Context.UserId,
                        defaultRoleCode,
                        context.RequestAborted);

                    outcomes.Add(new(candidate.Key, candidate.Email, candidate.DisplayName, status, resultCode, userId, roleStatus));
                    await ExecuteTransactionControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                }
                catch
                {
                    await ExecuteTransactionControlAsync(connection, transaction, $"ROLLBACK TO SAVEPOINT {savepoint};", context.RequestAborted);
                    outcomes.Add(new(candidate.Key, candidate.Email, candidate.DisplayName, "failed", "database_write_failed", null, "not_completed"));
                    await ExecuteTransactionControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", context.RequestAborted);
                }
            }

            var imported = outcomes.Count(item => item.Status == "imported");
            var duplicate = outcomes.Count(item => item.Status == "duplicate");
            var skipped = outcomes.Count(item => item.Status == "skipped");
            var failed = outcomes.Count(item => item.Status == "failed");

            await InsertAuditAsync(
                connection,
                transaction,
                access.Context,
                "DIRECTORY_USERS_IMPORTED",
                settings.TenantKey,
                failed == 0 ? "success" : "completed_with_failures",
                context.TraceIdentifier,
                new { imported, duplicate, skipped, failed, defaultRoleCode },
                context.RequestAborted,
                ignoreMissingTable: true);

            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                module = "010",
                status = failed == 0 ? "selected_users_imported" : "selected_users_imported_with_failures",
                imported,
                skipped,
                duplicate,
                failed,
                defaultRoleCode,
                roleAssignment = roleId is null
                    ? "role_not_found_users_imported_without_assignment"
                    : "explicit_role_assignment_processed",
                transactionCommitted = true,
                visibility = new
                {
                    userAdministration = true,
                    activeUserSelectors = true,
                    identityProfileModule062 = true
                },
                results = outcomes,
                message = $"Import committed: {imported} new, {duplicate} existing/upserted, {skipped} skipped, and {failed} failed user(s)."
            });
        }
    }

    private static async Task<AccessResult> AuthorizeAsync(
        HttpContext context,
        params IReadOnlySet<string>[] acceptedPermissionSets)
    {
        var userId = ActualSessionUserId(context);
        if (userId is null)
        {
            return new(null, Results.Json(new
            {
                module = ActiveModuleNumber,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new(null, Results.Json(new
            {
                module = ActiveModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Microsoft Integration authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT
                    COALESCE(r.role_code, ''),
                    COALESCE(p.permission_code, '')
                FROM app_user_role_assignments ura
                JOIN app_roles r
                  ON r.app_role_id = ura.app_role_id
                 AND r.is_active = TRUE
                LEFT JOIN app_role_permissions rp
                  ON rp.app_role_id = r.app_role_id
                LEFT JOIN app_permissions p
                  ON p.app_permission_id = rp.app_permission_id
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
            var permissionAllowed = acceptedPermissionSets.Any(set => permissions.Any(set.Contains));
            if (!administrator && !permissionAllowed)
            {
                return new(null, Results.Json(new
                {
                    module = ActiveModuleNumber,
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
                module = ActiveModuleNumber,
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
            // Existing environment configuration remains a supported fallback.
        }

        var tenantDomain = First(JsonStringAny(settings, "tenantDomain", "tenant_domain"));
        var sourceProvider = First(JsonStringAny(settings, "sourceProvider", "source_provider"), "ENTRA_ID_TEST");
        var environmentMode = First(JsonStringAny(settings, "environmentMode", "environment_mode"),
            sourceProvider.Contains("TEST", StringComparison.OrdinalIgnoreCase) ? "test" : "production");
        var tenantKey = environmentMode.Contains("prod", StringComparison.OrdinalIgnoreCase) ? "ussignal" : "onenecklab";

        return new(
            tenantKey,
            First(JsonStringAny(settings, "tenantName", "tenant_name"), environmentMode == "production" ? "US Signal Production" : "OneNeck Lab"),
            tenantDomain,
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
            await using var command = new NpgsqlCommand("SELECT to_jsonb(r)::text FROM azure_entra_sync_runs r ORDER BY COALESCE((to_jsonb(r)->>'started_at')::timestamptz, NOW()) DESC LIMIT 1;", connection);
            var raw = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(raw)) return new(null, "not_run", "No directory sync has been recorded.");
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            return new(
                First(JsonStringAny(root, "completedAt", "completed_at", "startedAt", "started_at")),
                First(JsonStringAny(root, "status", "run_status"), "unknown"),
                First(JsonStringAny(root, "message", "result_message"), "Directory sync result recorded."));
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
        var legacyConfiguration = JsonObjectAny(module067, "configuration");
        var consolidatedConfiguration = ParseConsolidatedConfiguration(module065);
        var mail = JsonObjectAny(consolidatedConfiguration, "mail");

        return new
        {
            providerTarget = First(JsonStringAny(mail, "providerTarget"), JsonStringAny(legacyConfiguration, "providerTarget"), "microsoft_graph"),
            smtpHost = First(JsonStringAny(mail, "smtpHost"), "smtp.office365.com"),
            smtpPort = JsonIntAny(mail, "smtpPort") ?? 587,
            senderName = First(JsonStringAny(mail, "senderName"), JsonStringAny(legacyConfiguration, "senderName")),
            senderAddress = First(JsonStringAny(mail, "senderAddress"), JsonStringAny(legacyConfiguration, "senderAddress"), Environment.GetEnvironmentVariable("PROJECTPULSE_M365_SENDER_MAILBOX")),
            replyToAddress = First(JsonStringAny(mail, "replyToAddress"), JsonStringAny(legacyConfiguration, "replyToAddress")),
            recipientBoundary = First(JsonStringAny(mail, "recipientBoundary"), JsonStringAny(legacyConfiguration, "recipientBoundary"), "test_only"),
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
                result.Add(new(reader.GetString(0), reader.GetString(1)[..Math.Min(16, reader.GetString(1).Length)], reader.GetFieldValue<DateTimeOffset>(2)));
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
        var environment = EnvironmentSecret(tenantKey);
        if (!string.IsNullOrWhiteSpace(environment)) return environment;

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
            var associatedData = Encoding.UTF8.GetBytes($"ProjectPulse:{ActiveModuleNumber}:{tenantKey}");
            try
            {
                using var aes = new AesGcm(key.Value.Key, tag.Length);
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
            // Startup remains fail-open to the existing environment-variable identity contract.
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
            // Non-base64 values are safely stretched with SHA-256 and never returned.
        }

        return new(SHA256.HashData(Encoding.UTF8.GetBytes($"ProjectPulse-Microsoft-Integration:{configured}")), source);
    }

    private static List<JsonElement> ExtractCandidates(JsonElement root)
    {
        foreach (var name in new[] { "selectedUsers", "users", "previewUsers", "candidates", "availableUsers" })
        {
            if (TryProperty(root, name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => item.Clone())
                    .ToList();
            }
        }
        return new();
    }

    private static HashSet<string> ExtractSelectedIdentifiers(JsonElement root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "emails", "selectedEmails", "userIds", "selectedUserIds", "entraObjectIds", "selectedEntraObjectIds" })
        {
            if (!TryProperty(root, name, out var property) || property.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in property.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim().ToLowerInvariant());
            }
        }
        return result;
    }

    private static ImportCandidate NormalizeCandidate(JsonElement element, string observedSourceProvider)
    {
        var email = First(JsonStringAny(element, "email", "mail", "userPrincipalName", "upn")).ToLowerInvariant();
        var entraObjectId = First(JsonStringAny(element, "entraObjectId", "entra_object_id", "id", "userId", "user_id"));
        var key = First(JsonStringAny(element, "previewKey", "preview_key"), entraObjectId, email).ToLowerInvariant();
        return new(
            key,
            email,
            First(JsonStringAny(element, "displayName", "display_name", "name"), email),
            entraObjectId,
            First(JsonStringAny(element, "sourceProvider", "source_provider"), observedSourceProvider, "ENTRA_ID_TEST"),
            First(JsonStringAny(element, "jobTitle", "job_title")),
            First(JsonStringAny(element, "departmentName", "department_name", "department")),
            First(JsonStringAny(element, "officeLocation", "office_location", "location")),
            First(JsonStringAny(element, "managerEmail", "manager_email")),
            JsonBoolAny(element, "accountEnabled", "account_enabled", "enabled") ?? true);
    }

    private static bool CandidateWasSelected(ImportCandidate candidate, HashSet<string> selected) =>
        selected.Contains(candidate.Key)
        || selected.Contains(candidate.Email)
        || (!string.IsNullOrWhiteSpace(candidate.EntraObjectId) && selected.Contains(candidate.EntraObjectId.ToLowerInvariant()));

    private static async Task<HashSet<string>> ReadColumnsAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table_name;
            """, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(0));
        return columns;
    }

    private static async Task<Guid?> FindExistingUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        ImportCandidate candidate,
        CancellationToken cancellationToken)
    {
        var predicates = new List<string>();
        if (columns.Contains("email")) predicates.Add("LOWER(email) = LOWER(@email)");
        if (columns.Contains("entra_object_id") && !string.IsNullOrWhiteSpace(candidate.EntraObjectId)) predicates.Add("entra_object_id = @entra_object_id");
        if (predicates.Count == 0) return null;

        await using var command = new NpgsqlCommand($"SELECT user_id FROM app_users WHERE {string.Join(" OR ", predicates)} LIMIT 1;", connection, transaction);
        command.Parameters.AddWithValue("email", candidate.Email);
        if (predicates.Any(value => value.Contains("entra_object_id"))) command.Parameters.AddWithValue("entra_object_id", candidate.EntraObjectId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    private static async Task InsertAppUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        ImportCandidate candidate,
        string defaultRoleCode,
        CancellationToken cancellationToken)
    {
        var values = UserValues(columns, candidate, defaultRoleCode, includeCreatedAt: true);
        values["user_id"] = userId;
        await ExecuteDynamicInsertAsync(connection, transaction, "app_users", columns, values, cancellationToken);
    }

    private static async Task UpdateAppUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        ImportCandidate candidate,
        CancellationToken cancellationToken)
    {
        var values = UserValues(columns, candidate, string.Empty, includeCreatedAt: false);
        values.Remove("email");
        await ExecuteDynamicUpdateAsync(connection, transaction, "app_users", "user_id", userId, columns, values, cancellationToken);
    }

    private static Dictionary<string, object> UserValues(
        HashSet<string> columns,
        ImportCandidate candidate,
        string defaultRoleCode,
        bool includeCreatedAt)
    {
        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = candidate.Email,
            ["display_name"] = candidate.DisplayName,
            ["source_provider"] = candidate.SourceProvider,
            ["is_active"] = true,
            ["login_enabled"] = true,
            ["is_login_enabled"] = true,
            ["last_directory_sync_at"] = DateTimeOffset.UtcNow,
            ["updated_at"] = DateTimeOffset.UtcNow
        };
        if (includeCreatedAt) values["created_at"] = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(candidate.EntraObjectId)) values["entra_object_id"] = candidate.EntraObjectId;
        if (!string.IsNullOrWhiteSpace(candidate.JobTitle)) values["job_title"] = candidate.JobTitle;
        if (!string.IsNullOrWhiteSpace(candidate.Department))
        {
            values["department_name"] = candidate.Department;
            values["department"] = candidate.Department;
        }
        if (!string.IsNullOrWhiteSpace(candidate.OfficeLocation)) values["office_location"] = candidate.OfficeLocation;
        if (!string.IsNullOrWhiteSpace(candidate.ManagerEmail)) values["manager_email"] = candidate.ManagerEmail;
        if (!string.IsNullOrWhiteSpace(defaultRoleCode)) values["role_name"] = defaultRoleCode;
        return values.Where(item => columns.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Guid?> ResolveRoleIdAsync(NpgsqlConnection connection, string roleCode, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT app_role_id FROM app_roles WHERE UPPER(role_code) = UPPER(@role_code) AND is_active = TRUE LIMIT 1;", connection);
            command.Parameters.AddWithValue("role_code", roleCode);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is Guid id ? id : Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> EnsureRoleAssignmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        Guid? roleId,
        Guid actorUserId,
        string roleCode,
        CancellationToken cancellationToken)
    {
        if (roleId is null) return $"role_not_found:{roleCode}";
        if (columns.Count == 0 || !columns.Contains("user_id") || !columns.Contains("app_role_id")) return "assignment_table_unavailable";

        await using (var exists = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM app_user_role_assignments WHERE user_id = @user_id AND app_role_id = @role_id AND COALESCE(is_active, TRUE) = TRUE);", connection, transaction))
        {
            exists.Parameters.AddWithValue("user_id", userId);
            exists.Parameters.AddWithValue("role_id", roleId.Value);
            if (Convert.ToBoolean(await exists.ExecuteScalarAsync(cancellationToken))) return $"already_assigned:{roleCode}";
        }

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_user_role_assignment_id"] = Guid.NewGuid(),
            ["user_role_assignment_id"] = Guid.NewGuid(),
            ["user_id"] = userId,
            ["app_role_id"] = roleId.Value,
            ["is_active"] = true,
            ["assigned_by_user_id"] = actorUserId,
            ["created_by_user_id"] = actorUserId,
            ["assigned_at"] = DateTimeOffset.UtcNow,
            ["created_at"] = DateTimeOffset.UtcNow,
            ["updated_at"] = DateTimeOffset.UtcNow
        };
        await ExecuteDynamicInsertAsync(connection, transaction, "app_user_role_assignments", columns, values, cancellationToken);
        return $"assigned:{roleCode}";
    }

    private static async Task ExecuteDynamicInsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        HashSet<string> columns,
        Dictionary<string, object> values,
        CancellationToken cancellationToken)
    {
        var included = values.Where(item => columns.Contains(item.Key)).ToList();
        if (included.Count == 0) throw new InvalidOperationException("no_insertable_columns");
        var names = included.Select(item => QuoteIdentifier(item.Key)).ToArray();
        var parameters = included.Select((_, index) => $"@p{index}").ToArray();
        await using var command = new NpgsqlCommand($"INSERT INTO {QuoteIdentifier(table)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", parameters)});", connection, transaction);
        for (var index = 0; index < included.Count; index++) command.Parameters.AddWithValue($"p{index}", included[index].Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteDynamicUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string keyColumn,
        Guid keyValue,
        HashSet<string> columns,
        Dictionary<string, object> values,
        CancellationToken cancellationToken)
    {
        var included = values.Where(item => columns.Contains(item.Key) && !item.Key.Equals(keyColumn, StringComparison.OrdinalIgnoreCase)).ToList();
        if (included.Count == 0) return;
        var assignments = included.Select((item, index) => $"{QuoteIdentifier(item.Key)} = @p{index}").ToArray();
        await using var command = new NpgsqlCommand($"UPDATE {QuoteIdentifier(table)} SET {string.Join(", ", assignments)} WHERE {QuoteIdentifier(keyColumn)} = @key;", connection, transaction);
        for (var index = 0; index < included.Count; index++) command.Parameters.AddWithValue($"p{index}", included[index].Value);
        command.Parameters.AddWithValue("key", keyValue);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteTransactionControlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        CancellationToken cancellationToken,
        bool ignoreMissingTable = false)
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
                    @outcome,
                    @correlation,
                    @metadata::jsonb
                );
                """, connection, transaction);
            command.Parameters.AddWithValue("actor", access.UserId);
            command.Parameters.AddWithValue("email", access.Email);
            command.Parameters.AddWithValue("action", action);
            command.Parameters.AddWithValue("tenant_key", tenantKey ?? string.Empty);
            command.Parameters.AddWithValue("outcome", outcome);
            command.Parameters.AddWithValue("correlation", correlationId ?? string.Empty);
            command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (ignoreMissingTable && exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Module 010 import remains functional before migration 045; secret storage remains gated.
        }
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
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await InsertAuditAsync(connection, transaction, access, "MICROSOFT_CONNECTION_TEST", tenantKey, outcome, correlationId, new { tokenReturned = false, secretReturned = false }, cancellationToken, ignoreMissingTable: true);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // Connection-test result is authoritative even when optional audit storage is not installed.
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            throw new InvalidOperationException("unsafe_identifier");
        return $"\"{identifier}\"";
    }

    private static object Check(string code, bool ready, string description) => new { code, Ready = ready, description };

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
        module = ActiveModuleNumber,
        status = "invalid_request",
        message
    });

    private static IResult ViewAsWriteForbidden() => Results.Json(new
    {
        module = ActiveModuleNumber,
        status = "view_as_read_only",
        message = "Exit Administrator View-As before changing Microsoft Integration or importing users."
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
        return names.Select(name => JsonString(element.Value, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static JsonElement? JsonObjectAny(JsonElement? element, params string[] names)
    {
        if (element is null) return null;
        foreach (var name in names)
        {
            if (TryProperty(element.Value, name, out var value) && value.ValueKind == JsonValueKind.Object) return value.Clone();
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
            if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static int? JsonIntAny(JsonElement? element, params string[] names)
    {
        if (element is null) return null;
        foreach (var name in names)
        {
            if (!TryProperty(element.Value, name, out var value)) continue;
            if (value.TryGetInt32(out var parsed)) return parsed;
            if (int.TryParse(value.ToString(), out parsed)) return parsed;
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
    private sealed record ImportCandidate(
        string Key,
        string Email,
        string DisplayName,
        string EntraObjectId,
        string SourceProvider,
        string JobTitle,
        string Department,
        string OfficeLocation,
        string ManagerEmail,
        bool AccountEnabled);
    private sealed record ImportOutcome(
        string PreviewKey,
        string Email,
        string DisplayName,
        string Status,
        string ResultCode,
        Guid? UserId,
        string RoleAssignment);
}
