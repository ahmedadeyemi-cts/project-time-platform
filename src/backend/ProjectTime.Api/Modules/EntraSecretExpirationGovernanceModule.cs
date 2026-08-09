using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 065 non-secret client-secret expiration governance. This surface never
/// accepts, returns, logs, or stores a client-secret value. It owns expiration
/// metadata, Project Team Coordinator acknowledgements, recurring reminders, and
/// the organization-wide seven-day warning contract.
/// </summary>
public static class EntraSecretExpirationGovernanceModule
{
    private const string ModuleNumber = "065";
    private const string MigrationId = "056_role_workspace_entra_crm_governance";
    private const int WorkerAdvisoryLock = 56065056;
    private static readonly TimeSpan WorkerInterval = TimeSpan.FromHours(1);
    private static int _workerStarted;

    public static WebApplication MapEntraSecretExpirationGovernanceEndpoints(this WebApplication app)
    {
        app.MapGet(
            "/api/entra-secret-expiration/status",
            (Func<HttpContext, Task<IResult>>)GetStatusAsync);
        app.MapGet(
            "/api/entra-secret-expiration/profile",
            (Func<HttpContext, Task<IResult>>)GetProfileAsync);
        app.MapPut(
            "/api/entra-secret-expiration/profile",
            (Func<HttpContext, Task<IResult>>)SaveProfileAsync);
        app.MapPost(
            "/api/entra-secret-expiration/acknowledge",
            (Func<HttpContext, Task<IResult>>)AcknowledgeAsync);
        app.MapPost(
            "/api/entra-secret-expiration/reminders/run",
            (Func<HttpContext, Task<IResult>>)RunRemindersAsync);
        return app;
    }

    public static WebApplication UseEntraSecretExpirationGovernance(this WebApplication app)
    {
        if (Interlocked.Exchange(ref _workerStarted, 1) == 1) return app;

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(
                () => RunWorkerAsync(app.Services, lifetime.ApplicationStopping),
                CancellationToken.None);
        });
        return app;
    }

    private static async Task<IResult> GetStatusAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(context, _ => true);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        try
        {
            var profile = await LoadActiveProfileAsync(connection, context.RequestAborted);
            var status = BuildStatus(profile ?? FallbackProfile());
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "entra_secret_expiration_status_loaded",
                profileConfigured = profile is not null,
                status.ExpiresAt,
                status.DaysUntilExpiration,
                status.Health,
                status.ShowGlobalWarning,
                status.WarningStartsAt,
                status.SecretVersion,
                status.ApplicationName,
                message = status.Message,
                secretReturned = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            LogSanitized(context.RequestServices, exception, "load Entra secret expiration status");
            return DependencyUnavailable("entra_secret_expiration_status_unavailable");
        }
    }

    private static async Task<IResult> GetProfileAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && (
                actor.IsAdministrator
                || actor.Roles.Contains("PROJECT_TEAM_COORDINATOR")
                || actor.Roles.Contains("PROJECT_COORDINATOR")
                || actor.Roles.Contains("PTC")
                || actor.Permissions.Contains("VIEW_ENTRA_SECRET_EXPIRATION")
                || actor.Permissions.Contains("MANAGE_ENTRA_SECRET_EXPIRATION")));
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;

        try
        {
            var profile = await LoadActiveProfileAsync(connection, context.RequestAborted);
            var recipients = profile is null
                ? new List<ExpirationRecipientView>()
                : await LoadRecipientsAsync(connection, profile.ProfileId, context.RequestAborted);
            var currentRecipient = recipients.FirstOrDefault(row => row.UserId == actor.EffectiveUserId);
            var status = BuildStatus(profile ?? FallbackProfile());
            var canManage = !actor.IsViewAs && (
                actor.IsAdministrator
                || actor.Permissions.Contains("MANAGE_ENTRA_SECRET_EXPIRATION"));
            var canAcknowledge = !actor.IsViewAs
                && profile is not null
                && currentRecipient is not null;

            return Results.Ok(new
            {
                module = ModuleNumber,
                status,
                profile,
                fallbackProfile = profile is null ? FallbackProfile() : null,
                access = new
                {
                    canView = true,
                    canManage,
                    canAcknowledge,
                    isAcknowledged = currentRecipient?.AcknowledgedAt is not null,
                    acknowledgedAt = currentRecipient?.AcknowledgedAt,
                    isViewAs = actor.IsViewAs,
                    authoritySource = "actual Pulse session"
                },
                summary = new
                {
                    recipientCount = recipients.Count,
                    acknowledgedCount = recipients.Count(row => row.AcknowledgedAt is not null),
                    pendingCount = recipients.Count(row => row.AcknowledgedAt is null)
                },
                recipients,
                reminderPolicy = new
                {
                    startsDaysBeforeExpiration = profile?.ReminderStartDays ?? 30,
                    repeatsUntilIndividualAcknowledgement = true,
                    acknowledgementIsPerRecipient = true,
                    criticalWarningRequiresProfileUpdate = true
                },
                secretReturned = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            LogSanitized(context.RequestServices, exception, "load Entra expiration governance profile");
            return DependencyUnavailable("entra_secret_expiration_profile_unavailable");
        }
    }

    private static async Task<IResult> SaveProfileAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && (
                actor.IsAdministrator
                || actor.Permissions.Contains("MANAGE_ENTRA_SECRET_EXPIRATION")));
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;

        ExpirationProfileRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ExpirationProfileRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            return Invalid("A valid non-secret expiration profile is required.");
        }

        var validation = Validate(request);
        if (validation is not null) return Invalid(validation);

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            await using (var guard = new NpgsqlCommand(
                             "SELECT pg_advisory_xact_lock(@lock_id);",
                             connection,
                             transaction))
            {
                guard.Parameters.AddWithValue("lock_id", WorkerAdvisoryLock);
                await guard.ExecuteNonQueryAsync(context.RequestAborted);
            }

            var generation = 1;
            await using (var next = new NpgsqlCommand(
                             "SELECT COALESCE(MAX(generation), 0) + 1 FROM entra_secret_expiration_profile_versions;",
                             connection,
                             transaction))
            {
                generation = Convert.ToInt32(await next.ExecuteScalarAsync(context.RequestAborted) ?? 1);
            }

            var profileId = Guid.NewGuid();
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO entra_secret_expiration_profile_versions (
                    profile_id,
                    generation,
                    application_name,
                    environment_name,
                    secret_label,
                    secret_version,
                    expires_at,
                    reminder_start_days,
                    critical_start_days,
                    reminder_interval_hours,
                    change_reason,
                    created_by_user_id,
                    created_at
                )
                VALUES (
                    @profile_id,
                    @generation,
                    @application_name,
                    @environment_name,
                    @secret_label,
                    @secret_version,
                    @expires_at,
                    @reminder_start_days,
                    @critical_start_days,
                    @reminder_interval_hours,
                    @change_reason,
                    @created_by,
                    NOW()
                );
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("profile_id", profileId);
                insert.Parameters.AddWithValue("generation", generation);
                insert.Parameters.AddWithValue("application_name", Clean(request!.ApplicationName, 200));
                insert.Parameters.AddWithValue("environment_name", NormalizeEnvironment(request.Environment));
                insert.Parameters.AddWithValue("secret_label", Clean(request.SecretLabel, 200));
                insert.Parameters.AddWithValue("secret_version", Clean(request.SecretVersion, 120));
                insert.Parameters.AddWithValue("expires_at", request.ExpiresAt!.Value.ToUniversalTime());
                insert.Parameters.AddWithValue("reminder_start_days", request.ReminderStartDays);
                insert.Parameters.AddWithValue("critical_start_days", request.CriticalStartDays);
                insert.Parameters.AddWithValue("reminder_interval_hours", request.ReminderIntervalHours);
                insert.Parameters.AddWithValue("change_reason", Clean(request.Reason, 1000));
                insert.Parameters.AddWithValue("created_by", actor.ActualUserId);
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await using (var state = new NpgsqlCommand("""
                INSERT INTO entra_secret_expiration_state (
                    singleton_key,
                    active_profile_id,
                    updated_by_user_id,
                    updated_at
                )
                VALUES (TRUE, @profile_id, @updated_by, NOW())
                ON CONFLICT (singleton_key) DO UPDATE
                SET active_profile_id = EXCLUDED.active_profile_id,
                    updated_by_user_id = EXCLUDED.updated_by_user_id,
                    updated_at = NOW();
                """, connection, transaction))
            {
                state.Parameters.AddWithValue("profile_id", profileId);
                state.Parameters.AddWithValue("updated_by", actor.ActualUserId);
                await state.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await using (var recipients = new NpgsqlCommand("""
                INSERT INTO entra_secret_expiration_recipients (
                    profile_id,
                    user_id,
                    display_name,
                    email,
                    role_code,
                    snapshotted_at
                )
                SELECT DISTINCT
                    @profile_id,
                    app_user.user_id,
                    COALESCE(NULLIF(app_user.display_name, ''), app_user.email),
                    lower(app_user.email),
                    upper(role.role_code),
                    NOW()
                FROM app_users app_user
                JOIN app_user_role_assignments assignment
                  ON assignment.user_id = app_user.user_id
                 AND assignment.is_active = TRUE
                JOIN app_roles role
                  ON role.app_role_id = assignment.app_role_id
                 AND role.is_active = TRUE
                WHERE app_user.is_active = TRUE
                  AND upper(role.role_code) IN (
                    'PROJECT_TEAM_COORDINATOR',
                    'PROJECT_COORDINATOR',
                    'PTC'
                  )
                ON CONFLICT (profile_id, user_id) DO NOTHING;
                """, connection, transaction))
            {
                recipients.Parameters.AddWithValue("profile_id", profileId);
                await recipients.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await WriteAuditAsync(
                connection,
                transaction,
                profileId,
                "EXPIRATION_PROFILE_PUBLISHED",
                actor,
                request.Reason,
                new
                {
                    generation,
                    request.ApplicationName,
                    environment = NormalizeEnvironment(request.Environment),
                    request.SecretLabel,
                    request.SecretVersion,
                    expiresAt = request.ExpiresAt.Value.ToUniversalTime(),
                    request.ReminderStartDays,
                    request.CriticalStartDays,
                    request.ReminderIntervalHours,
                    secretValueStored = false
                },
                context.TraceIdentifier,
                context.RequestAborted);

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "entra_secret_expiration_profile_saved",
                profileId,
                generation,
                message = "The non-secret expiration profile was saved. A new recipient acknowledgement generation is now active.",
                secretAccepted = false,
                secretReturned = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            LogSanitized(context.RequestServices, exception, "save Entra expiration governance profile");
            return DependencyUnavailable("entra_secret_expiration_profile_save_failed");
        }
    }

    private static async Task<IResult> AcknowledgeAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(context, actor => !actor.IsViewAs);
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;
        var actor = access.Actor!;

        AcknowledgementRequest? request = null;
        try
        {
            request = await context.Request.ReadFromJsonAsync<AcknowledgementRequest>(
                cancellationToken: context.RequestAborted);
        }
        catch
        {
            // A default acknowledgement statement is sufficient.
        }

        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        try
        {
            Guid? profileId;
            await using (var active = new NpgsqlCommand("""
                SELECT state.active_profile_id
                FROM entra_secret_expiration_state state
                WHERE state.singleton_key = TRUE;
                """, connection, transaction))
            {
                var raw = await active.ExecuteScalarAsync(context.RequestAborted);
                profileId = raw is Guid id ? id : null;
            }

            if (!profileId.HasValue)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Conflict(new
                {
                    module = ModuleNumber,
                    status = "expiration_profile_not_configured",
                    message = "An administrator must save the expiration profile before it can be acknowledged."
                });
            }

            var eligible = false;
            await using (var recipient = new NpgsqlCommand("""
                SELECT EXISTS (
                    SELECT 1
                    FROM entra_secret_expiration_recipients
                    WHERE profile_id = @profile_id
                      AND user_id = @user_id
                );
                """, connection, transaction))
            {
                recipient.Parameters.AddWithValue("profile_id", profileId.Value);
                recipient.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
                eligible = Convert.ToBoolean(await recipient.ExecuteScalarAsync(context.RequestAborted) ?? false);
            }

            if (!eligible)
            {
                await transaction.RollbackAsync(context.RequestAborted);
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "acknowledgement_not_assigned",
                    message = "The current user is not a snapshotted acknowledgement recipient for this expiration profile."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var statement = Clean(
                request?.Acknowledgement,
                1000,
                "I acknowledge this client-secret expiration and will coordinate the required rotation before the deadline.");
            await using (var acknowledge = new NpgsqlCommand("""
                INSERT INTO entra_secret_expiration_acknowledgements (
                    acknowledgement_id,
                    profile_id,
                    user_id,
                    acknowledged_by_actual_user_id,
                    acknowledgement_statement,
                    acknowledged_at
                )
                VALUES (
                    gen_random_uuid(),
                    @profile_id,
                    @user_id,
                    @actual_user_id,
                    @statement,
                    NOW()
                )
                ON CONFLICT (profile_id, user_id) DO NOTHING;
                """, connection, transaction))
            {
                acknowledge.Parameters.AddWithValue("profile_id", profileId.Value);
                acknowledge.Parameters.AddWithValue("user_id", actor.EffectiveUserId);
                acknowledge.Parameters.AddWithValue("actual_user_id", actor.ActualUserId);
                acknowledge.Parameters.AddWithValue("statement", statement);
                await acknowledge.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await WriteAuditAsync(
                connection,
                transaction,
                profileId.Value,
                "RECIPIENT_ACKNOWLEDGED",
                actor,
                statement,
                new { recipientUserId = actor.EffectiveUserId },
                context.TraceIdentifier,
                context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);

            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "entra_secret_expiration_acknowledged",
                profileId,
                acknowledgedAt = DateTimeOffset.UtcNow,
                message = "Your acknowledgement was recorded. Recurring reminders for this profile will stop for you.",
                criticalWarningDismissed = false
            });
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            await transaction.RollbackAsync(context.RequestAborted);
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(context.RequestAborted);
            LogSanitized(context.RequestServices, exception, "record Entra expiration acknowledgement");
            return DependencyUnavailable("entra_secret_expiration_acknowledgement_failed");
        }
    }

    private static async Task<IResult> RunRemindersAsync(HttpContext context)
    {
        var access = await ProjectNotificationRepository.OpenAuthorizedAsync(
            context,
            actor => !actor.IsViewAs && (
                actor.IsAdministrator
                || actor.Permissions.Contains("MANAGE_ENTRA_SECRET_EXPIRATION")));
        if (access.Failure is not null) return access.Failure;
        await using var connection = access.Connection!;

        try
        {
            var result = await EvaluateAndDeliverAsync(
                connection,
                access.Actor!.ActualUserId,
                context,
                context.TraceIdentifier,
                context.RequestAborted);
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "entra_secret_expiration_reminders_evaluated",
                result.EvaluatedCount,
                result.SentCount,
                result.QueuedCount,
                result.SuppressedCount,
                result.FailedCount,
                result.SkippedAcknowledgedCount,
                message = result.Message
            });
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            return MigrationRequired();
        }
        catch (Exception exception)
        {
            LogSanitized(context.RequestServices, exception, "run Entra expiration reminders");
            return DependencyUnavailable("entra_secret_expiration_reminder_evaluation_failed");
        }
    }

    private static async Task RunWorkerAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("EntraSecretExpirationGovernanceWorker");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var connectionString = ConnectionString();
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    await connection.OpenAsync(cancellationToken);
                    var lockAcquired = false;
                    await using (var claim = new NpgsqlCommand(
                                     "SELECT pg_try_advisory_lock(@lock_id);",
                                     connection))
                    {
                        claim.Parameters.AddWithValue("lock_id", WorkerAdvisoryLock);
                        lockAcquired = Convert.ToBoolean(
                            await claim.ExecuteScalarAsync(cancellationToken) ?? false);
                    }

                    if (lockAcquired)
                    {
                        try
                        {
                            await EvaluateAndDeliverAsync(
                                connection,
                                null,
                                null,
                                $"entra-expiration-worker-{Guid.NewGuid():N}",
                                cancellationToken);
                        }
                        finally
                        {
                            await using var release = new NpgsqlCommand(
                                "SELECT pg_advisory_unlock(@lock_id);",
                                connection);
                            release.Parameters.AddWithValue("lock_id", WorkerAdvisoryLock);
                            await release.ExecuteNonQueryAsync(CancellationToken.None);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                logger.LogWarning("Module 065 expiration reminder evaluation failed; raw exception detail was suppressed.");
            }

            try
            {
                await Task.Delay(WorkerInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task<ReminderRunResult> EvaluateAndDeliverAsync(
        NpgsqlConnection connection,
        Guid? releasedBy,
        HttpContext? context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var profile = await LoadActiveProfileAsync(connection, cancellationToken);
        if (profile is null)
        {
            return ReminderRunResult.Empty("No active expiration profile is configured.");
        }

        var status = BuildStatus(profile);
        if (!status.DaysUntilExpiration.HasValue
            || status.DaysUntilExpiration.Value > profile.ReminderStartDays)
        {
            return ReminderRunResult.Empty(
                $"Reminders begin {profile.ReminderStartDays} days before expiration.");
        }

        var recipients = await LoadRecipientsAsync(connection, profile.ProfileId, cancellationToken);
        var pending = recipients.Where(row => row.AcknowledgedAt is null).ToArray();
        var skippedAcknowledged = recipients.Count - pending.Length;
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(
            context,
            cancellationToken);
        var evaluated = 0;
        var sent = 0;
        var queued = 0;
        var suppressed = 0;
        var failed = 0;
        var expiresAt = profile.ExpiresAt!.Value;

        foreach (var recipient in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            if (recipient.LastReminderAt.HasValue
                && recipient.LastReminderAt.Value.AddHours(profile.ReminderIntervalHours) > now)
            {
                continue;
            }

            var bucket = now.ToUnixTimeSeconds() / Math.Max(3600, profile.ReminderIntervalHours * 3600L);
            var claimId = await ClaimReminderAsync(
                connection,
                profile.ProfileId,
                recipient.UserId,
                bucket,
                cancellationToken);
            if (!claimId.HasValue) continue;
            evaluated++;

            var daysText = status.DaysUntilExpiration.Value < 0
                ? $"expired {Math.Abs(status.DaysUntilExpiration.Value)} day(s) ago"
                : status.DaysUntilExpiration.Value == 0
                    ? "expires today"
                    : $"expires in {status.DaysUntilExpiration.Value} day(s)";
            var subject = status.ShowGlobalWarning
                ? $"CRITICAL: Microsoft Integration client secret {daysText}"
                : $"Action required: Microsoft Integration client secret {daysText}";
            var textBody = $"""
                Pulse Module 065 expiration reminder

                Application: {profile.ApplicationName}
                Environment: {profile.Environment}
                Secret label: {profile.SecretLabel}
                Version: {profile.SecretVersion}
                Expiration: {expiresAt:O}
                Status: {daysText}

                Open Pulse Module 065 to acknowledge this reminder and coordinate rotation before expiration.
                Acknowledgement stops recurring reminders for you. An organization-wide critical warning remains until an administrator updates the version or expiration date.

                No client-secret value is included in this message.
                """;
            var htmlBody = $"""
                <h2>Pulse Module 065 expiration reminder</h2>
                <p><strong>{Escape(profile.ApplicationName)}</strong> {Escape(daysText)}.</p>
                <ul>
                  <li>Environment: {Escape(profile.Environment)}</li>
                  <li>Secret label: {Escape(profile.SecretLabel)}</li>
                  <li>Version: {Escape(profile.SecretVersion)}</li>
                  <li>Expiration: {Escape(expiresAt.ToString("O"))}</li>
                </ul>
                <p>Open Pulse Module 065 to acknowledge this reminder and coordinate rotation before expiration.</p>
                <p><strong>No client-secret value is included in this message.</strong></p>
                """;
            var notificationRecipient = new ProjectNotificationUser(
                recipient.UserId,
                recipient.DisplayName,
                recipient.Email,
                recipient.RoleCode,
                "module_065_expiration_recipient_snapshot");
            var eventKey = $"entra-secret-expiration:{profile.ProfileId:N}:{recipient.UserId:N}:{bucket}";
            var initialStatus = readiness.RecipientBoundary == "locked" ? "held" : "queued";
            var dispatchId = await ProjectNotificationRepository.UpsertDispatchAsync(
                connection,
                null,
                null,
                null,
                eventKey,
                "entra_client_secret_expiration",
                status.ShowGlobalWarning ? "critical" : "warning",
                ModuleNumber,
                status.Health,
                subject,
                textBody,
                htmlBody,
                readiness.RecipientBoundary,
                initialStatus,
                new[] { notificationRecipient },
                new
                {
                    profile.ProfileId,
                    profile.Generation,
                    recipient.UserId,
                    profile.SecretVersion,
                    expiresAt,
                    status.DaysUntilExpiration,
                    status.ShowGlobalWarning,
                    acknowledgementRequired = true,
                    secretIncluded = false
                },
                cancellationToken);

            var delivery = await Module065ProjectNotificationDelivery.DeliverAsync(
                subject,
                textBody,
                htmlBody,
                new[] { notificationRecipient },
                context,
                cancellationToken);
            var dispatch = await ProjectNotificationRepository.LoadDispatchAsync(
                connection,
                dispatchId,
                cancellationToken);
            if (dispatch is not null)
            {
                await ProjectNotificationRepository.RecordDeliveryAsync(
                    connection,
                    dispatch,
                    delivery,
                    releasedBy,
                    "Module 065 client-secret expiration reminder.",
                    correlationId,
                    cancellationToken);
            }

            await CompleteReminderAsync(
                connection,
                claimId.Value,
                profile.ProfileId,
                recipient.UserId,
                dispatchId,
                delivery,
                cancellationToken);

            if (delivery.Sent) sent++;
            else if (delivery.Status == "queued") queued++;
            else if (delivery.Status == "failed") failed++;
            else suppressed++;
        }

        return new(
            evaluated,
            sent,
            queued,
            suppressed,
            failed,
            skippedAcknowledged,
            evaluated == 0
                ? "No unacknowledged recipient was due for another reminder."
                : $"Evaluated {evaluated} due reminder recipient(s); individual acknowledgements continue to control future reminders.");
    }

    private static async Task<Guid?> ClaimReminderAsync(
        NpgsqlConnection connection,
        Guid profileId,
        Guid userId,
        long bucket,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO entra_secret_expiration_reminder_claims (
                reminder_claim_id,
                profile_id,
                user_id,
                reminder_bucket,
                claim_status,
                claimed_at,
                updated_at
            )
            VALUES (
                gen_random_uuid(),
                @profile_id,
                @user_id,
                @bucket,
                'claimed',
                NOW(),
                NOW()
            )
            ON CONFLICT (profile_id, user_id, reminder_bucket) DO NOTHING
            RETURNING reminder_claim_id;
            """, connection);
        command.Parameters.AddWithValue("profile_id", profileId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("bucket", bucket);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private static async Task CompleteReminderAsync(
        NpgsqlConnection connection,
        Guid claimId,
        Guid profileId,
        Guid userId,
        Guid dispatchId,
        Module065MailDeliveryResult result,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var update = new NpgsqlCommand("""
                UPDATE entra_secret_expiration_reminder_claims
                SET claim_status = @status,
                    dispatch_id = @dispatch_id,
                    diagnostic_code = @diagnostic_code,
                    completed_at = NOW(),
                    updated_at = NOW()
                WHERE reminder_claim_id = @claim_id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("status", Clean(result.Status, 40));
                update.Parameters.AddWithValue("dispatch_id", dispatchId);
                update.Parameters.AddWithValue("diagnostic_code", Clean(result.DiagnosticCode, 120));
                update.Parameters.AddWithValue("claim_id", claimId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var evidence = new NpgsqlCommand("""
                INSERT INTO entra_secret_expiration_reminder_events (
                    reminder_event_id,
                    reminder_claim_id,
                    profile_id,
                    user_id,
                    dispatch_id,
                    event_code,
                    delivery_status,
                    provider_source,
                    diagnostic_code,
                    event_metadata,
                    created_at
                )
                VALUES (
                    gen_random_uuid(),
                    @claim_id,
                    @profile_id,
                    @user_id,
                    @dispatch_id,
                    'REMINDER_DELIVERY_EVALUATED',
                    @delivery_status,
                    @provider_source,
                    @diagnostic_code,
                    @metadata::jsonb,
                    NOW()
                );
                """, connection, transaction))
            {
                evidence.Parameters.AddWithValue("claim_id", claimId);
                evidence.Parameters.AddWithValue("profile_id", profileId);
                evidence.Parameters.AddWithValue("user_id", userId);
                evidence.Parameters.AddWithValue("dispatch_id", dispatchId);
                evidence.Parameters.AddWithValue("delivery_status", Clean(result.Status, 40));
                evidence.Parameters.AddWithValue("provider_source", Clean(result.Provider, 80));
                evidence.Parameters.AddWithValue("diagnostic_code", Clean(result.DiagnosticCode, 120));
                evidence.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
                {
                    result.Sent,
                    result.RecipientBoundary,
                    message = Clean(result.Message, 1000),
                    secretIncluded = false
                }));
                await evidence.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<ExpirationProfile?> LoadActiveProfileAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                profile.profile_id,
                profile.generation,
                profile.application_name,
                profile.environment_name,
                profile.secret_label,
                profile.secret_version,
                profile.expires_at,
                profile.reminder_start_days,
                profile.critical_start_days,
                profile.reminder_interval_hours,
                profile.change_reason,
                profile.created_by_user_id,
                profile.created_at
            FROM entra_secret_expiration_state state
            JOIN entra_secret_expiration_profile_versions profile
              ON profile.profile_id = state.active_profile_id
            WHERE state.singleton_key = TRUE;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(
            reader.GetGuid(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetString(10),
            reader.GetGuid(11),
            reader.GetFieldValue<DateTimeOffset>(12));
    }

    private static async Task<List<ExpirationRecipientView>> LoadRecipientsAsync(
        NpgsqlConnection connection,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var rows = new List<ExpirationRecipientView>();
        await using var command = new NpgsqlCommand("""
            SELECT
                recipient.user_id,
                recipient.display_name,
                recipient.email,
                recipient.role_code,
                acknowledgement.acknowledged_at,
                latest.completed_at,
                latest.claim_status,
                latest.diagnostic_code
            FROM entra_secret_expiration_recipients recipient
            LEFT JOIN entra_secret_expiration_acknowledgements acknowledgement
              ON acknowledgement.profile_id = recipient.profile_id
             AND acknowledgement.user_id = recipient.user_id
            LEFT JOIN LATERAL (
                SELECT
                    claim.completed_at,
                    claim.claim_status,
                    claim.diagnostic_code
                FROM entra_secret_expiration_reminder_claims claim
                WHERE claim.profile_id = recipient.profile_id
                  AND claim.user_id = recipient.user_id
                ORDER BY claim.claimed_at DESC
                LIMIT 1
            ) latest ON TRUE
            WHERE recipient.profile_id = @profile_id
            ORDER BY recipient.display_name, recipient.email;
            """, connection);
        command.Parameters.AddWithValue("profile_id", profileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7)));
        }
        return rows;
    }

    private static ExpirationStatus BuildStatus(ExpirationProfile profile)
    {
        if (!profile.ExpiresAt.HasValue)
        {
            return new(
                profile.ApplicationName,
                profile.SecretVersion,
                null,
                null,
                "expiration_unknown",
                false,
                null,
                "The Microsoft Integration client-secret expiration date has not been recorded.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = profile.ExpiresAt.Value;
        var days = (int)Math.Floor((expiresAt - now).TotalDays);
        var warningStartsAt = expiresAt.AddDays(-profile.CriticalStartDays);
        var showGlobalWarning = now >= warningStartsAt;
        var health = days < 0
            ? "expired"
            : days <= profile.CriticalStartDays
                ? "critical"
                : days <= profile.ReminderStartDays
                    ? "warning"
                    : "healthy";
        var message = days < 0
            ? "The Microsoft Integration client secret has expired."
            : days == 0
                ? "The Microsoft Integration client secret expires today."
                : $"The Microsoft Integration client secret expires in {days} day(s).";
        return new(
            profile.ApplicationName,
            profile.SecretVersion,
            expiresAt,
            days,
            health,
            showGlobalWarning,
            warningStartsAt,
            message);
    }

    private static ExpirationProfile FallbackProfile()
    {
        DateTimeOffset? expiresAt = DateTimeOffset.TryParse(
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_SECRET_EXPIRES_AT"),
            out var parsed)
            ? parsed
            : null;
        return new(
            Guid.Empty,
            0,
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_APPLICATION_NAME")?.Trim()
                ?? "Pulse Microsoft Integration",
            NormalizeEnvironment(Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_MODE")),
            "Microsoft Entra application client secret",
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENTRA_SECRET_VERSION")?.Trim() ?? string.Empty,
            expiresAt,
            30,
            7,
            24,
            string.Empty,
            Guid.Empty,
            DateTimeOffset.MinValue);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid profileId,
        string eventCode,
        ProjectNotificationActor actor,
        string? reason,
        object metadata,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO entra_secret_expiration_audit_events (
                audit_event_id,
                profile_id,
                event_code,
                actor_user_id,
                actor_email,
                event_reason,
                event_metadata,
                correlation_id,
                created_at
            )
            VALUES (
                gen_random_uuid(),
                @profile_id,
                @event_code,
                @actor_user_id,
                @actor_email,
                @event_reason,
                @event_metadata::jsonb,
                @correlation_id,
                NOW()
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("profile_id", profileId);
        command.Parameters.AddWithValue("event_code", Clean(eventCode, 100));
        command.Parameters.AddWithValue("actor_user_id", actor.ActualUserId);
        command.Parameters.AddWithValue("actor_email", Clean(actor.Email, 320));
        command.Parameters.AddWithValue("event_reason", Clean(reason, 1000));
        command.Parameters.AddWithValue("event_metadata", JsonSerializer.Serialize(metadata));
        command.Parameters.AddWithValue("correlation_id", Clean(correlationId, 160));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? Validate(ExpirationProfileRequest? request)
    {
        if (request is null) return "A non-secret expiration profile is required.";
        if (string.IsNullOrWhiteSpace(request.ApplicationName)) return "Application name is required.";
        if (string.IsNullOrWhiteSpace(request.SecretLabel)) return "A non-secret secret label is required.";
        if (string.IsNullOrWhiteSpace(request.SecretVersion)) return "A non-secret version identifier is required.";
        if (!request.ExpiresAt.HasValue || request.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(5))
            return "Expiration must be a future date and time.";
        if (request.ReminderStartDays is < 7 or > 365) return "Reminder start must be between 7 and 365 days.";
        if (request.CriticalStartDays is < 1 or > 30) return "Critical warning start must be between 1 and 30 days.";
        if (request.CriticalStartDays > request.ReminderStartDays)
            return "Critical warning start cannot be earlier than the reminder start.";
        if (request.ReminderIntervalHours is < 1 or > 168)
            return "Reminder interval must be between 1 and 168 hours.";
        if (string.IsNullOrWhiteSpace(request.Reason)) return "A change reason is required.";
        return null;
    }

    private static string NormalizeEnvironment(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "production" or "prod" => "production",
            "development" or "dev" => "development",
            _ => "test"
        };
    }

    private static string Clean(string? value, int maximum, string fallback = "")
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static string? ConnectionString()
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
            || string.IsNullOrWhiteSpace(password)) return null;
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

    private static IResult Invalid(string message) => Results.BadRequest(new
    {
        module = ModuleNumber,
        status = "invalid_expiration_profile",
        message
    });

    private static IResult MigrationRequired() => Results.Json(new
    {
        module = ModuleNumber,
        status = "entra_secret_expiration_migration_required",
        migration = MigrationId,
        message = "Module 065 expiration governance requires migration 056 before it can be used."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult DependencyUnavailable(string status) => Results.Json(new
    {
        module = ModuleNumber,
        status,
        message = "Module 065 expiration governance is temporarily unavailable. No secret value was read or changed."
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static void LogSanitized(IServiceProvider services, Exception exception, string operation)
    {
        services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("EntraSecretExpirationGovernanceModule")
            .LogWarning(
                "Module 065 could not {Operation}; diagnostic type {DiagnosticType}; raw detail suppressed.",
                operation,
                exception.GetType().Name);
    }

    private sealed record ExpirationProfileRequest(
        string ApplicationName,
        string Environment,
        string SecretLabel,
        string SecretVersion,
        DateTimeOffset? ExpiresAt,
        int ReminderStartDays,
        int CriticalStartDays,
        int ReminderIntervalHours,
        string Reason);

    private sealed record AcknowledgementRequest(string? Acknowledgement);

    private sealed record ExpirationProfile(
        Guid ProfileId,
        int Generation,
        string ApplicationName,
        string Environment,
        string SecretLabel,
        string SecretVersion,
        DateTimeOffset? ExpiresAt,
        int ReminderStartDays,
        int CriticalStartDays,
        int ReminderIntervalHours,
        string ChangeReason,
        Guid CreatedByUserId,
        DateTimeOffset CreatedAt);

    private sealed record ExpirationRecipientView(
        Guid UserId,
        string DisplayName,
        string Email,
        string RoleCode,
        DateTimeOffset? AcknowledgedAt,
        DateTimeOffset? LastReminderAt,
        string LastDeliveryStatus,
        string LastDiagnosticCode);

    private sealed record ExpirationStatus(
        string ApplicationName,
        string SecretVersion,
        DateTimeOffset? ExpiresAt,
        int? DaysUntilExpiration,
        string Health,
        bool ShowGlobalWarning,
        DateTimeOffset? WarningStartsAt,
        string Message);

    private sealed record ReminderRunResult(
        int EvaluatedCount,
        int SentCount,
        int QueuedCount,
        int SuppressedCount,
        int FailedCount,
        int SkippedAcknowledgedCount,
        string Message)
    {
        internal static ReminderRunResult Empty(string message) => new(0, 0, 0, 0, 0, 0, message);
    }
}
