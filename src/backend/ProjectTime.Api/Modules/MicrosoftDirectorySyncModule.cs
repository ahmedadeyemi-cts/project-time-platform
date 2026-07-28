using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Graph-backed Module 010 directory synchronization owned by Module 065.
/// Manual Sync Now and the automatic schedule use the same guarded runtime,
/// transaction, role boundary, sync-run history, and environment-specific
/// services credentials. No browser-supplied credential is accepted or returned.
/// </summary>
public static class MicrosoftDirectorySyncModule
{
    private const string ModuleNumber = "010";
    private const string ManualPath = "/api/microsoft-integration/directory-users/sync-now";
    private const string StatusPath = "/api/microsoft-integration/directory-users/sync-status";
    private const string ConfigurationMarker = "PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:";
    private const int MaximumGraphPages = 30;
    private const int MaximumGraphUsers = 20000;
    private static readonly TimeSpan InitialWorkerDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan WorkerPollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GraphTimeout = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim SyncGate = new(1, 1);
    private static int _workerStarted;
    private static DateTimeOffset? _activeRunStartedAt;

    private static readonly HashSet<string> AcceptedPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM_ADMINISTRATION",
        "MANAGE_ALL",
        "MANAGE_AZURE_AD",
        "MANAGE_AZURE_SYNC"
    };

    private static readonly HashSet<string> AllowedImportRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "ENGINEER",
        "ENGINEERING",
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "SALES",
        "INSIDE_SALES",
        "SOLUTION_ARCHITECT"
    };

    public static WebApplication MapMicrosoftDirectorySyncEndpoints(this WebApplication app)
    {
        app.MapPost(ManualPath, (Func<HttpContext, Task<IResult>>)ManualSyncAsync);
        app.MapGet(StatusPath, (Func<HttpContext, Task<IResult>>)GetStatusAsync);

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            if (Interlocked.Exchange(ref _workerStarted, 1) != 0) return;
            _ = Task.Run(() => SchedulerLoopAsync(
                app.Services,
                app.Lifetime.ApplicationStopping));
        });

        return app;
    }

    private static async Task<IResult> ManualSyncAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (IsViewAs(context))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "view_as_read_only",
                message = "Exit Administrator View-As before synchronizing Entra users."
            }, statusCode: StatusCodes.Status403Forbidden);
        }
        if (!SameOrigin(context))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "origin_rejected",
                message = "Directory synchronization requires a same-origin ProjectPulse request."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        SyncRequest? request = null;
        try
        {
            if (context.Request.ContentLength is > 0)
            {
                request = await context.Request.ReadFromJsonAsync<SyncRequest>(
                    cancellationToken: context.RequestAborted);
            }
        }
        catch
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_sync_request",
                message = "A valid optional Test or Production sync request is required."
            });
        }

        var runtimeEnvironment = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        var requestedEnvironment = MicrosoftEnvironmentRuntimeResolver.Normalize(
            request?.EnvironmentMode);
        var environmentMode = string.IsNullOrWhiteSpace(requestedEnvironment)
            ? runtimeEnvironment
            : requestedEnvironment;
        if (string.IsNullOrWhiteSpace(environmentMode))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "microsoft_environment_unresolved",
                correlationId = context.TraceIdentifier,
                message = "ProjectPulse could not determine the Test or Production Microsoft environment."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        if (!environmentMode.Equals(runtimeEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "directory_sync_environment_not_active",
                requestedEnvironment = environmentMode,
                runtimeEnvironment,
                message = "Directory synchronization can run only in the matching ProjectPulse environment."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var result = await TryRunAsync(
            context.RequestServices,
            environmentMode,
            "manual",
            access.Context!.UserId,
            access.Context.Email,
            context.TraceIdentifier,
            force: true,
            context.RequestAborted);

        return ResultFor(result);
    }

    private static async Task<IResult> GetStatusAsync(HttpContext context)
    {
        var access = await AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;

        var environmentMode = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if (string.IsNullOrWhiteSpace(environmentMode))
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "microsoft_environment_unresolved",
                message = "ProjectPulse could not determine the Test or Production Microsoft environment."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var profile = await ReadProfileAsync(connection, environmentMode, context.RequestAborted);
        if (profile is null)
        {
            return Results.Json(new
            {
                module = ModuleNumber,
                status = "directory_sync_profile_not_configured",
                environmentMode,
                message = $"Complete and save the {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} services and directory-sync profile in Module 065."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var last = await ReadLastSyncAsync(connection, context.RequestAborted);
        var nextScheduledAt = profile.Enabled
            ? (last.LastSyncAt ?? DateTimeOffset.UtcNow).AddHours(profile.FrequencyHours)
            : null;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "directory_sync_status_loaded",
            environmentMode,
            mode = profile.Enabled ? "automatic_and_manual" : "manual_only",
            automaticSyncEnabled = profile.Enabled,
            syncFrequencyHours = profile.FrequencyHours,
            lastSyncAt = last.LastSyncAt,
            lastSyncStatus = last.Status,
            lastSyncMessage = last.Message,
            nextScheduledAt,
            syncInProgress = SyncGate.CurrentCount == 0,
            activeRunStartedAt = _activeRunStartedAt,
            workerStarted = Volatile.Read(ref _workerStarted) == 1,
            configuredRole = profile.DefaultRoleCode,
            servicesSecretConfigured = !string.IsNullOrWhiteSpace(
                ResolveServicesSecret(profile.EnvironmentMode, profile.TenantKey)),
            secretValuesReturned = false
        });
    }

    private static async Task SchedulerLoopAsync(
        IServiceProvider services,
        CancellationToken stoppingToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MicrosoftDirectorySyncScheduler");

        try
        {
            await Task.Delay(InitialWorkerDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var environmentMode = MicrosoftEnvironmentRuntimeResolver.Resolve();
                if (!string.IsNullOrWhiteSpace(environmentMode))
                {
                    var connectionString = BuildConnectionString();
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        await using var connection = new NpgsqlConnection(connectionString);
                        await connection.OpenAsync(stoppingToken);
                        var profile = await ReadProfileAsync(
                            connection,
                            environmentMode,
                            stoppingToken);
                        var last = await ReadLastSyncAsync(
                            connection,
                            stoppingToken);

                        if (profile?.Enabled == true && IsDue(profile, last.LastSyncAt))
                        {
                            var actor = await ResolveAutomaticActorAsync(
                                connection,
                                stoppingToken);
                            var correlationId = $"entra-auto-{Guid.NewGuid():N}";
                            var result = await TryRunAsync(
                                services,
                                environmentMode,
                                "automatic",
                                actor.UserId,
                                actor.Email,
                                correlationId,
                                force: false,
                                stoppingToken);

                            logger.LogInformation(
                                "Module 010 automatic {Environment} directory sync finished with {Status}; correlation {CorrelationId}.",
                                environmentMode,
                                result.Status,
                                correlationId);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Module 010 automatic directory sync check failed ({ExceptionType}).",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(WorkerPollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task<SyncResult> TryRunAsync(
        IServiceProvider services,
        string environmentMode,
        string trigger,
        Guid? actorUserId,
        string actorEmail,
        string correlationId,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!await SyncGate.WaitAsync(0, cancellationToken))
        {
            return SyncResult.Conflict(
                environmentMode,
                trigger,
                correlationId,
                "directory_sync_already_running",
                "Another Entra directory synchronization is already running.");
        }

        _activeRunStartedAt = DateTimeOffset.UtcNow;
        try
        {
            var connectionString = BuildConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return SyncResult.Unavailable(
                    environmentMode,
                    trigger,
                    correlationId,
                    "directory_sync_storage_unavailable",
                    "Directory synchronization storage is unavailable.");
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            var profile = await ReadProfileAsync(
                connection,
                environmentMode,
                cancellationToken);
            if (profile is null)
            {
                return SyncResult.Conflict(
                    environmentMode,
                    trigger,
                    correlationId,
                    "directory_sync_profile_not_configured",
                    $"Complete and save the {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} services and directory-sync profile in Module 065.");
            }
            if (!force && !profile.Enabled)
            {
                return SyncResult.Skipped(
                    environmentMode,
                    trigger,
                    correlationId,
                    "automatic_sync_disabled",
                    "Automatic directory synchronization is disabled.");
            }

            var last = await ReadLastSyncAsync(connection, cancellationToken);
            if (!force && !IsDue(profile, last.LastSyncAt))
            {
                return SyncResult.Skipped(
                    environmentMode,
                    trigger,
                    correlationId,
                    "automatic_sync_not_due",
                    "The next automatic directory synchronization is not due yet.");
            }

            var governedRole = NormalizeRole(profile.DefaultRoleCode);
            if (!AllowedImportRoles.Contains(governedRole))
            {
                return SyncResult.Conflict(
                    environmentMode,
                    trigger,
                    correlationId,
                    "governed_import_role_not_allowed",
                    "The configured import role is privileged or unsupported. Select an approved non-administrative role in Module 065.");
            }

            if (!Guid.TryParse(profile.TenantId, out var tenantId)
                || !Guid.TryParse(profile.ClientId, out _))
            {
                return SyncResult.Conflict(
                    environmentMode,
                    trigger,
                    correlationId,
                    "directory_services_profile_incomplete",
                    "The Microsoft tenant and services application IDs must be valid GUIDs.");
            }

            var clientSecret = ResolveServicesSecret(
                profile.EnvironmentMode,
                profile.TenantKey);
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                return SyncResult.Conflict(
                    environmentMode,
                    trigger,
                    correlationId,
                    "directory_services_secret_missing",
                    $"Save the write-only {MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} services client secret in Module 065.");
            }

            var runId = Guid.NewGuid();
            await InsertRunAsync(
                connection,
                runId,
                trigger,
                actorEmail,
                environmentMode,
                correlationId,
                cancellationToken);

            try
            {
                var graphUsers = await ReadGraphUsersAsync(
                    services.GetRequiredService<IHttpClientFactory>(),
                    tenantId,
                    profile.ClientId,
                    clientSecret,
                    cancellationToken);

                var effectiveActor = actorUserId is not null
                    ? new SyncActor(actorUserId, actorEmail)
                    : await ResolveAutomaticActorAsync(connection, cancellationToken);
                var persistence = await PersistUsersAsync(
                    connection,
                    graphUsers,
                    profile,
                    governedRole,
                    effectiveActor,
                    runId,
                    trigger,
                    correlationId,
                    cancellationToken);

                return new SyncResult(
                    persistence.Failed == 0
                        ? "directory_sync_completed"
                        : "directory_sync_completed_with_failures",
                    environmentMode,
                    trigger,
                    correlationId,
                    runId,
                    graphUsers.Count,
                    persistence.Imported,
                    persistence.Updated,
                    persistence.Skipped,
                    persistence.Failed,
                    true,
                    false,
                    persistence.Failed == 0
                        ? $"Entra synchronization completed: {persistence.Imported} new, {persistence.Updated} refreshed, and {persistence.Skipped} skipped user(s)."
                        : $"Entra synchronization completed with failures: {persistence.Imported} new, {persistence.Updated} refreshed, {persistence.Skipped} skipped, and {persistence.Failed} failed user(s).",
                    StatusCodes.Status200OK);
            }
            catch (SyncFailure failure)
            {
                await FailRunAsync(
                    connection,
                    runId,
                    failure.Code,
                    failure.SafeMessage,
                    environmentMode,
                    correlationId,
                    cancellationToken);
                return SyncResult.Failed(
                    environmentMode,
                    trigger,
                    correlationId,
                    runId,
                    failure.Code,
                    failure.SafeMessage,
                    failure.HttpStatus);
            }
            catch (Exception exception)
            {
                services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("MicrosoftDirectorySyncModule")
                    .LogWarning(
                        "Module 010 {Trigger} directory synchronization failed ({ExceptionType}); correlation {CorrelationId}.",
                        trigger,
                        exception.GetType().Name,
                        correlationId);

                await FailRunAsync(
                    connection,
                    runId,
                    "directory_sync_failed",
                    "ProjectPulse could not complete the Entra directory synchronization.",
                    environmentMode,
                    correlationId,
                    cancellationToken);
                return SyncResult.Failed(
                    environmentMode,
                    trigger,
                    correlationId,
                    runId,
                    "directory_sync_failed",
                    "ProjectPulse could not complete the Entra directory synchronization.",
                    StatusCodes.Status502BadGateway);
            }
        }
        finally
        {
            _activeRunStartedAt = null;
            SyncGate.Release();
        }
    }

    private static async Task<List<DirectoryUser>> ReadGraphUsersAsync(
        IHttpClientFactory httpClientFactory,
        Guid tenantId,
        string clientId,
        string clientSecret,
        CancellationToken requestCancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
        timeout.CancelAfter(GraphTimeout);
        var client = httpClientFactory.CreateClient();
        client.Timeout = GraphTimeout;

        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://login.microsoftonline.com/{tenantId:D}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            })
        };
        using var tokenResponse = await client.SendAsync(tokenRequest, timeout.Token);
        var tokenRaw = await ReadBoundedStringAsync(tokenResponse.Content, 2 * 1024 * 1024, timeout.Token);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new SyncFailure(
                "graph_token_request_failed",
                $"Microsoft identity rejected the services credential with HTTP {(int)tokenResponse.StatusCode}.",
                StatusCodes.Status502BadGateway);
        }

        using var tokenDocument = JsonDocument.Parse(tokenRaw);
        var accessToken = tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement)
            ? tokenElement.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new SyncFailure(
                "graph_access_token_missing",
                "Microsoft identity did not return an application access token.",
                StatusCodes.Status502BadGateway);
        }

        var roles = ReadTokenRoles(accessToken);
        if (!roles.Contains("Directory.Read.All") || !roles.Contains("User.Read.All"))
        {
            throw new SyncFailure(
                "graph_directory_permissions_missing",
                "The services application token must contain Directory.Read.All and User.Read.All application roles.",
                StatusCodes.Status409Conflict);
        }

        var users = new List<DirectoryUser>();
        var next = "https://graph.microsoft.com/v1.0/users?$select=id,displayName,mail,userPrincipalName,jobTitle,department,officeLocation,accountEnabled&$top=999";
        for (var page = 0; page < MaximumGraphPages && !string.IsNullOrWhiteSpace(next); page++)
        {
            if (!Uri.TryCreate(next, UriKind.Absolute, out var uri)
                || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !uri.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new SyncFailure(
                    "graph_pagination_endpoint_rejected",
                    "Microsoft Graph returned an unapproved pagination endpoint.",
                    StatusCodes.Status502BadGateway);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.SendAsync(request, timeout.Token);
            var body = await ReadBoundedStringAsync(response.Content, 8 * 1024 * 1024, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new SyncFailure(
                    "graph_users_request_failed",
                    $"Microsoft Graph user retrieval returned HTTP {(int)response.StatusCode}.",
                    StatusCodes.Status502BadGateway);
            }

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("value", out var value)
                && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (users.Count >= MaximumGraphUsers)
                    {
                        throw new SyncFailure(
                            "graph_user_limit_exceeded",
                            $"Microsoft Graph returned more than the controlled {MaximumGraphUsers} user synchronization limit.",
                            StatusCodes.Status409Conflict);
                    }
                    users.Add(DirectoryUser.From(item));
                }
            }

            next = document.RootElement.TryGetProperty("@odata.nextLink", out var nextLink)
                && nextLink.ValueKind == JsonValueKind.String
                    ? nextLink.GetString() ?? string.Empty
                    : string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(next))
        {
            throw new SyncFailure(
                "graph_page_limit_exceeded",
                "Microsoft Graph returned more pages than the controlled synchronization limit.",
                StatusCodes.Status409Conflict);
        }

        return users;
    }

    private static async Task<PersistenceResult> PersistUsersAsync(
        NpgsqlConnection connection,
        List<DirectoryUser> users,
        SyncProfile profile,
        string defaultRoleCode,
        SyncActor actor,
        Guid runId,
        string trigger,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var userColumns = await ReadColumnsAsync(connection, "app_users", cancellationToken);
        var assignmentColumns = await ReadColumnsAsync(connection, "app_user_role_assignments", cancellationToken);
        if (!userColumns.Contains("user_id") || !userColumns.Contains("email"))
        {
            throw new SyncFailure(
                "app_users_schema_unavailable",
                "The ProjectPulse user directory schema is unavailable for synchronization.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var roleId = await ResolveRoleIdAsync(connection, defaultRoleCode, cancellationToken);
        if (roleId is null)
        {
            throw new SyncFailure(
                "default_import_role_not_found",
                $"The configured non-administrative import role {defaultRoleCode} was not found.",
                StatusCodes.Status409Conflict);
        }

        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        for (var index = 0; index < users.Count; index++)
        {
            var user = users[index];
            var savepoint = $"module010_sync_{index}";
            await ExecuteControlAsync(connection, transaction, $"SAVEPOINT {savepoint};", cancellationToken);
            try
            {
                if (string.IsNullOrWhiteSpace(user.Email)
                    || !user.AccountEnabled
                    || !seen.Add(First(user.ObjectId, user.Email)))
                {
                    skipped++;
                    await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", cancellationToken);
                    continue;
                }

                var candidate = new ImportCandidate(
                    user.Email,
                    user.DisplayName,
                    user.ObjectId,
                    profile.SourceProvider,
                    user.JobTitle,
                    user.Department,
                    user.OfficeLocation,
                    user.AccountEnabled);
                var existing = await FindExistingUserAsync(
                    connection,
                    transaction,
                    userColumns,
                    candidate,
                    cancellationToken);
                Guid userId;
                if (existing is null)
                {
                    userId = Guid.NewGuid();
                    await InsertUserAsync(
                        connection,
                        transaction,
                        userColumns,
                        userId,
                        candidate,
                        defaultRoleCode,
                        cancellationToken);
                    imported++;
                }
                else
                {
                    userId = existing.Value;
                    await UpdateUserAsync(
                        connection,
                        transaction,
                        userColumns,
                        userId,
                        candidate,
                        cancellationToken);
                    updated++;
                }

                await EnsureRoleAssignmentAsync(
                    connection,
                    transaction,
                    assignmentColumns,
                    userId,
                    roleId.Value,
                    actor.UserId,
                    defaultRoleCode,
                    cancellationToken);
                await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", cancellationToken);
            }
            catch
            {
                await ExecuteControlAsync(connection, transaction, $"ROLLBACK TO SAVEPOINT {savepoint};", cancellationToken);
                failed++;
                await ExecuteControlAsync(connection, transaction, $"RELEASE SAVEPOINT {savepoint};", cancellationToken);
            }
        }

        var status = failed == 0 ? "completed_graph_sync" : "completed_graph_sync_with_failures";
        var message = $"{MicrosoftEnvironmentRuntimeResolver.Display(profile.EnvironmentMode)} Graph synchronization: {imported} new, {updated} refreshed, {skipped} skipped, {failed} failed.";
        await CompleteRunAsync(
            connection,
            transaction,
            runId,
            status,
            users.Count,
            imported,
            updated,
            skipped,
            failed,
            message,
            profile.EnvironmentMode,
            correlationId,
            cancellationToken);
        await WriteAuditAsync(
            connection,
            transaction,
            actor,
            profile,
            trigger,
            correlationId,
            imported,
            updated,
            skipped,
            failed,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new(imported, updated, skipped, failed);
    }

    private static async Task InsertRunAsync(
        NpgsqlConnection connection,
        Guid runId,
        string trigger,
        string actorEmail,
        string environmentMode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO azure_entra_sync_runs (
                azure_entra_sync_run_id,
                sync_started_at,
                status,
                triggered_by_email,
                users_seen,
                users_imported,
                users_updated,
                users_skipped,
                message
            ) VALUES (
                @run_id,
                NOW(),
                'running_graph_sync',
                @actor_email,
                0,
                0,
                0,
                0,
                @message
            );
            """, connection);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("actor_email", First(actorEmail, $"system:module010-{trigger}"));
        command.Parameters.AddWithValue("message", $"{MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} {trigger} Graph synchronization started. Correlation {correlationId}.");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CompleteRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        string status,
        int usersSeen,
        int imported,
        int updated,
        int skipped,
        int failed,
        string message,
        string environmentMode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE azure_entra_sync_runs
            SET sync_completed_at = NOW(),
                status = @status,
                users_seen = @users_seen,
                users_imported = @imported,
                users_updated = @updated,
                users_skipped = @skipped,
                message = @message
            WHERE azure_entra_sync_run_id = @run_id;

            UPDATE azure_entra_settings
            SET last_sync_at = NOW(),
                last_sync_status = @status,
                last_sync_message = @message,
                updated_at = NOW();
            """, connection, transaction);
        command.Parameters.AddWithValue("run_id", runId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("users_seen", usersSeen);
        command.Parameters.AddWithValue("imported", imported);
        command.Parameters.AddWithValue("updated", updated);
        command.Parameters.AddWithValue("skipped", skipped + failed);
        command.Parameters.AddWithValue("message", $"{message} Correlation {correlationId}.");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task FailRunAsync(
        NpgsqlConnection connection,
        Guid runId,
        string code,
        string safeMessage,
        string environmentMode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                UPDATE azure_entra_sync_runs
                SET sync_completed_at = NOW(),
                    status = 'failed_graph_sync',
                    message = @message
                WHERE azure_entra_sync_run_id = @run_id;

                UPDATE azure_entra_settings
                SET last_sync_at = NOW(),
                    last_sync_status = 'failed_graph_sync',
                    last_sync_message = @message,
                    updated_at = NOW();
                """, connection);
            command.Parameters.AddWithValue("run_id", runId);
            command.Parameters.AddWithValue("message", $"{MicrosoftEnvironmentRuntimeResolver.Display(environmentMode)} Graph synchronization failed ({code}). {safeMessage} Correlation {correlationId}.");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // The controlled failure remains the response when optional run evidence cannot update.
        }
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SyncActor actor,
        SyncProfile profile,
        string trigger,
        string correlationId,
        int imported,
        int updated,
        int skipped,
        int failed,
        CancellationToken cancellationToken)
    {
        if (actor.UserId is null) return;
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
                ) VALUES (
                    @actor,
                    @email,
                    'DIRECTORY_USERS_SYNCHRONIZED',
                    @tenant_key,
                    @outcome,
                    @correlation,
                    CAST(@metadata AS jsonb)
                );
                """, connection, transaction);
            command.Parameters.AddWithValue("actor", actor.UserId.Value);
            command.Parameters.AddWithValue("email", actor.Email);
            command.Parameters.AddWithValue("tenant_key", profile.TenantKey);
            command.Parameters.AddWithValue("outcome", failed == 0 ? "success" : "completed_with_failures");
            command.Parameters.AddWithValue("correlation", correlationId);
            command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
            {
                trigger,
                profile.EnvironmentMode,
                imported,
                updated,
                skipped,
                failed,
                secretValuesReturned = false
            }));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            // Optional audit table is not available in every compatibility database.
        }
    }

    private static async Task<SyncProfile?> ReadProfileAsync(
        NpgsqlConnection connection,
        string environmentMode,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT document_json::text
            FROM projectpulse_native_admin_documents
            WHERE module_number='065' AND document_key='configuration'
            LIMIT 1;
            """, connection);
        var raw = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (string.IsNullOrWhiteSpace(raw)) return null;

        using var document = JsonDocument.Parse(raw);
        if (!TryProperty(document.RootElement, "configuration", out var configuration)) return null;
        var notes = JsonString(configuration, "notes");
        if (!notes.StartsWith(ConfigurationMarker, StringComparison.Ordinal)) return null;
        using var stored = JsonDocument.Parse(notes[ConfigurationMarker.Length..]);
        if (!TryProperty(stored.RootElement, "tenants", out var tenants)
            || tenants.ValueKind != JsonValueKind.Array) return null;

        foreach (var tenant in tenants.EnumerateArray())
        {
            var mode = MicrosoftEnvironmentRuntimeResolver.Normalize(
                JsonString(tenant, "environmentMode"));
            if (!mode.Equals(environmentMode, StringComparison.OrdinalIgnoreCase)) continue;
            if (!TryProperty(tenant, "services", out var services)) services = default;

            var graphScopes = First(
                JsonString(services, "graphScopes"),
                JsonString(tenant, "graphScopes"));
            var scopes = graphScopes
                .Split(new[] { ' ', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!scopes.Contains("Directory.Read.All") || !scopes.Contains("User.Read.All"))
                return null;

            return new SyncProfile(
                mode,
                First(JsonString(tenant, "key"), JsonString(tenant, "tenantKey")),
                JsonString(tenant, "tenantId"),
                First(JsonString(services, "clientId"), JsonString(tenant, "clientId")),
                JsonBool(tenant, "directorySyncEnabled") ?? JsonBool(tenant, "syncEnabled") ?? false,
                Math.Clamp(JsonInt(tenant, "syncFrequencyHours") ?? 24, 1, 168),
                NormalizeRole(First(JsonString(tenant, "defaultRoleCode"), "ENGINEERING")),
                First(JsonString(tenant, "sourceProvider"), mode == "production" ? "ENTRA_ID" : "ENTRA_ID_TEST"),
                graphScopes);
        }

        return null;
    }

    private static async Task<LastSync> ReadLastSyncAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT last_sync_at, COALESCE(last_sync_status,''), COALESCE(last_sync_message,'')
                FROM azure_entra_settings
                ORDER BY created_at
                LIMIT 1;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return new(null, string.Empty, string.Empty);
            return new(
                reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetString(1),
                reader.GetString(2));
        }
        catch
        {
            return new(null, string.Empty, string.Empty);
        }
    }

    private static bool IsDue(SyncProfile profile, DateTimeOffset? lastSyncAt) =>
        lastSyncAt is null || DateTimeOffset.UtcNow >= lastSyncAt.Value.AddHours(profile.FrequencyHours);

    private static async Task<SyncActor> ResolveAutomaticActorAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT user_record.user_id, lower(user_record.email)
                FROM app_users user_record
                JOIN app_user_role_assignments assignment
                  ON assignment.user_id = user_record.user_id
                 AND assignment.is_active = TRUE
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                 AND role.is_active = TRUE
                WHERE user_record.is_active = TRUE
                  AND upper(role.role_code) IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR')
                ORDER BY CASE WHEN upper(role.role_code)='SUPER_ADMINISTRATOR' THEN 0 ELSE 1 END,
                         user_record.created_at
                LIMIT 1;
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new(reader.GetGuid(0), $"system:module010-scheduler ({reader.GetString(1)})")
                : new(null, "system:module010-scheduler");
        }
        catch
        {
            return new(null, "system:module010-scheduler");
        }
    }

    private static async Task<AccessOutcome> AuthorizeAsync(HttpContext context)
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
                message = "Entra synchronization authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                SELECT COALESCE(role.role_code,''), COALESCE(permission.permission_code,'')
                FROM app_user_role_assignments assignment
                JOIN app_roles role
                  ON role.app_role_id=assignment.app_role_id
                 AND role.is_active=TRUE
                LEFT JOIN app_role_permissions role_permission
                  ON role_permission.app_role_id=role.app_role_id
                LEFT JOIN app_permissions permission
                  ON permission.app_permission_id=role_permission.app_permission_id
                WHERE assignment.user_id=@user_id
                  AND assignment.is_active=TRUE;
                """, connection);
            command.Parameters.AddWithValue("user_id", userId.Value);
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                if (!reader.IsDBNull(0)) roles.Add(reader.GetString(0));
                if (!reader.IsDBNull(1)) permissions.Add(reader.GetString(1));
            }

            var administrator = roles.Contains("SUPER_ADMINISTRATOR") || roles.Contains("ADMINISTRATOR");
            if (!administrator && !permissions.Any(AcceptedPermissions.Contains))
            {
                return new(null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "azure_directory_sync_access_required",
                    message = "Administrator or delegated Azure/Entra synchronization access is required."
                }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new(new(userId.Value, ActualEmail(context), connectionString), null);
        }
        catch
        {
            return new(null, Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Entra synchronization authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static async Task<Guid?> ResolveRoleIdAsync(
        NpgsqlConnection connection,
        string roleCode,
        CancellationToken cancellationToken)
    {
        var candidates = roleCode.Equals("ENGINEERING", StringComparison.OrdinalIgnoreCase)
            ? new[] { "ENGINEERING", "ENGINEER" }
            : roleCode.Equals("ENGINEER", StringComparison.OrdinalIgnoreCase)
                ? new[] { "ENGINEER", "ENGINEERING" }
                : new[] { roleCode };
        await using var command = new NpgsqlCommand("""
            SELECT app_role_id
            FROM app_roles
            WHERE upper(role_code)=ANY(@role_codes)
              AND is_active=TRUE
            ORDER BY display_order
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("role_codes", candidates.Select(value => value.ToUpperInvariant()).ToArray());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema='public' AND table_name=@table_name;
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
        var predicates = new List<string> { "lower(email)=lower(@email)" };
        if (columns.Contains("entra_object_id") && !string.IsNullOrWhiteSpace(candidate.ObjectId))
            predicates.Add("entra_object_id=@entra_object_id");
        await using var command = new NpgsqlCommand(
            $"SELECT user_id FROM app_users WHERE {string.Join(" OR ", predicates)} LIMIT 1;",
            connection,
            transaction);
        command.Parameters.AddWithValue("email", candidate.Email);
        if (predicates.Count > 1) command.Parameters.AddWithValue("entra_object_id", candidate.ObjectId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : null;
    }

    private static async Task InsertUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        ImportCandidate candidate,
        string roleCode,
        CancellationToken cancellationToken)
    {
        var values = UserValues(columns, candidate, roleCode, true);
        values["user_id"] = userId;
        await ExecuteInsertAsync(connection, transaction, "app_users", columns, values, cancellationToken);
    }

    private static async Task UpdateUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        ImportCandidate candidate,
        CancellationToken cancellationToken)
    {
        var values = UserValues(columns, candidate, string.Empty, false);
        values.Remove("email");
        await ExecuteUpdateAsync(connection, transaction, "app_users", "user_id", userId, columns, values, cancellationToken);
    }

    private static Dictionary<string, object> UserValues(
        HashSet<string> columns,
        ImportCandidate candidate,
        string roleCode,
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
        if (!string.IsNullOrWhiteSpace(candidate.ObjectId)) values["entra_object_id"] = candidate.ObjectId;
        if (!string.IsNullOrWhiteSpace(candidate.JobTitle)) values["job_title"] = candidate.JobTitle;
        if (!string.IsNullOrWhiteSpace(candidate.Department))
        {
            values["department_name"] = candidate.Department;
            values["department"] = candidate.Department;
        }
        if (!string.IsNullOrWhiteSpace(candidate.OfficeLocation)) values["office_location"] = candidate.OfficeLocation;
        if (!string.IsNullOrWhiteSpace(roleCode)) values["role_name"] = roleCode;
        return values.Where(item => columns.Contains(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task EnsureRoleAssignmentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HashSet<string> columns,
        Guid userId,
        Guid roleId,
        Guid? actorUserId,
        string roleCode,
        CancellationToken cancellationToken)
    {
        if (!columns.Contains("user_id") || !columns.Contains("app_role_id")) return;
        await using (var exists = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM app_user_role_assignments
                WHERE user_id=@user_id
                  AND app_role_id=@role_id
                  AND COALESCE(NULLIF(to_jsonb(app_user_role_assignments)->>'is_active','')::boolean, TRUE)=TRUE
            );
            """, connection, transaction))
        {
            exists.Parameters.AddWithValue("user_id", userId);
            exists.Parameters.AddWithValue("role_id", roleId);
            if (Convert.ToBoolean(await exists.ExecuteScalarAsync(cancellationToken))) return;
        }

        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["app_user_role_assignment_id"] = Guid.NewGuid(),
            ["user_role_assignment_id"] = Guid.NewGuid(),
            ["user_id"] = userId,
            ["app_role_id"] = roleId,
            ["is_active"] = true,
            ["assignment_reason"] = $"Default {roleCode} role from Module 010 Entra sync",
            ["assigned_at"] = DateTimeOffset.UtcNow,
            ["created_at"] = DateTimeOffset.UtcNow,
            ["updated_at"] = DateTimeOffset.UtcNow
        };
        if (actorUserId is not null)
        {
            values["assigned_by_user_id"] = actorUserId.Value;
            values["created_by_user_id"] = actorUserId.Value;
        }
        await ExecuteInsertAsync(connection, transaction, "app_user_role_assignments", columns, values, cancellationToken);
    }

    private static async Task ExecuteInsertAsync(
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
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {QuoteIdentifier(table)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", parameters)});",
            connection,
            transaction);
        for (var index = 0; index < included.Count; index++) command.Parameters.AddWithValue($"p{index}", included[index].Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteUpdateAsync(
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
        var assignments = included.Select((item, index) => $"{QuoteIdentifier(item.Key)}=@p{index}").ToArray();
        await using var command = new NpgsqlCommand(
            $"UPDATE {QuoteIdentifier(table)} SET {string.Join(", ", assignments)} WHERE {QuoteIdentifier(keyColumn)}=@key;",
            connection,
            transaction);
        for (var index = 0; index < included.Count; index++) command.Parameters.AddWithValue($"p{index}", included[index].Value);
        command.Parameters.AddWithValue("key", keyValue);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteControlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ResolveServicesSecret(string environmentMode, string tenantKey)
    {
        var token = new string((tenantKey ?? string.Empty).ToUpperInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')
            .ToArray());
        var activeMode = MicrosoftEnvironmentRuntimeResolver.Normalize(
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE"));
        var modeName = environmentMode == "production"
            ? "PROJECTPULSE_ENTRA_PRODUCTION_CLIENT_SECRET"
            : "PROJECTPULSE_ENTRA_TEST_CLIENT_SECRET";
        return First(
            Environment.GetEnvironmentVariable($"PROJECTPULSE_MICROSOFT_TENANT_{token}_CLIENT_SECRET"),
            Environment.GetEnvironmentVariable(modeName),
            activeMode == environmentMode ? Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_CLIENT_SECRET") : string.Empty,
            activeMode == environmentMode ? Environment.GetEnvironmentVariable("PROJECTPULSE_M365_CLIENT_SECRET") : string.Empty);
    }

    private static IReadOnlySet<string> ReadTokenRoles(string accessToken)
    {
        try
        {
            var segments = accessToken.Split('.');
            if (segments.Length < 2) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (!document.RootElement.TryGetProperty("roles", out var roles))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return roles.ValueKind == JsonValueKind.Array
                ? roles.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task<string> ReadBoundedStringAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (memory.Length + read > maximumBytes)
            {
                throw new SyncFailure(
                    "graph_response_too_large",
                    "Microsoft Graph returned more data than the controlled synchronization limit.",
                    StatusCodes.Status502BadGateway);
            }
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
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
            MaxPoolSize = 8
        }.ConnectionString;
    }

    private static bool SameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return true;
        return ProjectPulsePublicOriginCompatibility.TryOrigin(origin, context, out var parsed)
            && parsed.Host.Equals(context.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && parsed.Scheme.Equals(context.Request.Scheme, StringComparison.OrdinalIgnoreCase);
    }

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

    private static string NormalizeRole(string? value)
    {
        var normalized = (value ?? "ENGINEERING").Trim().ToUpperInvariant();
        return normalized == "ENGINEERING" ? "ENGINEER" : normalized;
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)
            || identifier.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            throw new InvalidOperationException("unsafe_identifier");
        return $"\"{identifier}\"";
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

    private static bool? JsonBool(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? JsonInt(JsonElement element, string name) =>
        TryProperty(element, name, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static IResult ResultFor(SyncResult result)
    {
        var payload = new
        {
            module = ModuleNumber,
            status = result.Status,
            environmentMode = result.EnvironmentMode,
            trigger = result.Trigger,
            correlationId = result.CorrelationId,
            syncRunId = result.RunId,
            usersSeen = result.UsersSeen,
            usersImported = result.Imported,
            usersUpdated = result.Updated,
            usersSkipped = result.Skipped,
            usersFailed = result.Failed,
            transactionCommitted = result.TransactionCommitted,
            alreadyRunning = result.AlreadyRunning,
            secretValuesReturned = false,
            result.Message
        };
        return Results.Json(payload, statusCode: result.HttpStatus);
    }

    private sealed record SyncRequest(string? EnvironmentMode);
    private sealed record AccessOutcome(AccessContext? Context, IResult? Failure);
    private sealed record AccessContext(Guid UserId, string Email, string ConnectionString);
    private sealed record SyncActor(Guid? UserId, string Email);
    private sealed record LastSync(DateTimeOffset? LastSyncAt, string Status, string Message);
    private sealed record SyncProfile(
        string EnvironmentMode,
        string TenantKey,
        string TenantId,
        string ClientId,
        bool Enabled,
        int FrequencyHours,
        string DefaultRoleCode,
        string SourceProvider,
        string GraphScopes);
    private sealed record ImportCandidate(
        string Email,
        string DisplayName,
        string ObjectId,
        string SourceProvider,
        string JobTitle,
        string Department,
        string OfficeLocation,
        bool AccountEnabled);
    private sealed record PersistenceResult(int Imported, int Updated, int Skipped, int Failed);

    private sealed record DirectoryUser(
        string ObjectId,
        string Email,
        string DisplayName,
        string JobTitle,
        string Department,
        string OfficeLocation,
        bool AccountEnabled)
    {
        internal static DirectoryUser From(JsonElement element)
        {
            var mail = JsonString(element, "mail");
            var upn = JsonString(element, "userPrincipalName");
            var email = First(mail, upn).Trim().ToLowerInvariant();
            return new(
                JsonString(element, "id"),
                email.Contains('@', StringComparison.Ordinal) ? email : string.Empty,
                First(JsonString(element, "displayName"), email),
                JsonString(element, "jobTitle"),
                JsonString(element, "department"),
                JsonString(element, "officeLocation"),
                JsonBool(element, "accountEnabled") ?? true);
        }
    }

    private sealed class SyncFailure : Exception
    {
        internal SyncFailure(string code, string safeMessage, int httpStatus)
            : base(code)
        {
            Code = code;
            SafeMessage = safeMessage;
            HttpStatus = httpStatus;
        }

        internal string Code { get; }
        internal string SafeMessage { get; }
        internal int HttpStatus { get; }
    }

    private sealed record SyncResult(
        string Status,
        string EnvironmentMode,
        string Trigger,
        string CorrelationId,
        Guid? RunId,
        int UsersSeen,
        int Imported,
        int Updated,
        int Skipped,
        int Failed,
        bool TransactionCommitted,
        bool AlreadyRunning,
        string Message,
        int HttpStatus)
    {
        internal static SyncResult Conflict(string environment, string trigger, string correlation, string status, string message) =>
            new(status, environment, trigger, correlation, null, 0, 0, 0, 0, 0, false, status == "directory_sync_already_running", message, StatusCodes.Status409Conflict);
        internal static SyncResult Unavailable(string environment, string trigger, string correlation, string status, string message) =>
            new(status, environment, trigger, correlation, null, 0, 0, 0, 0, 0, false, false, message, StatusCodes.Status503ServiceUnavailable);
        internal static SyncResult Skipped(string environment, string trigger, string correlation, string status, string message) =>
            new(status, environment, trigger, correlation, null, 0, 0, 0, 0, 0, false, false, message, StatusCodes.Status200OK);
        internal static SyncResult Failed(string environment, string trigger, string correlation, Guid runId, string status, string message, int httpStatus) =>
            new(status, environment, trigger, correlation, runId, 0, 0, 0, 0, 0, false, false, message, httpStatus);
    }
}
