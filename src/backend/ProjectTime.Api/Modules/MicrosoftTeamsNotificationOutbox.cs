using System.Text.Json;
using Npgsql;
using static ProjectTime.Api.Modules.Module065NotificationParityPolicy;

namespace ProjectTime.Api.Modules;

public static partial class MicrosoftTeamsNotificationModule
{
    private static readonly JsonSerializerOptions OutboxJson = new(JsonSerializerDefaults.Web);
    private static int _outboxStarted;

    private static async Task<bool> OutboxReadyAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.module065_teams_outbox') IS NOT NULL AND to_regclass('public.module065_teams_outbox_events') IS NOT NULL AND to_regclass('public.module065_teams_outbox_attempts') IS NOT NULL;", connection);
        return await command.ExecuteScalarAsync(token) is true;
    }

    private static async Task QueueMirrorAsync(NpgsqlConnection connection, ProjectNotificationDispatchRow dispatch,
        HttpContext? context, CancellationToken token)
    {
        if (!await OutboxReadyAsync(connection, token))
            throw new InvalidOperationException("MODULE065_TEAMS_PARITY_MIGRATION_128_REQUIRED");
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(context, token);
        var environment = readiness.RuntimeEnvironment;
        if (environment is not ("test" or "production")) return;
        var configuration = await LoadAsync(connection, environment, token);
        var allowed = MaySend(dispatch.DeliveryBoundary, readiness.RecipientBoundary, environment,
            readiness.ConfiguredEnvironment, configuration.Enabled, context is not null && AdminExperienceCommon.IsViewAs(context));
        await QueueMirrorCoreAsync(connection, dispatch, environment, allowed, token);
    }

    internal static async Task QueueMirrorCoreAsync(NpgsqlConnection connection, ProjectNotificationDispatchRow dispatch,
        string environment, bool allowed, CancellationToken token)
    {
        var recipients = Recipients(dispatch.Recipients.Select(r => r.Email));
        if (recipients.Length == 0) return;
        // An event's first authorization snapshot is immutable. A later replay cannot add recipients,
        // revive a Test-only event, change its body, or resend an accepted notification.
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO module065_teams_outbox_events(dispatch_id,environment,snapshot)
            VALUES(@id,@environment,@snapshot::jsonb) ON CONFLICT DO NOTHING RETURNING dispatch_id;
            """, connection, transaction);
        insert.Parameters.AddWithValue("id", dispatch.DispatchId);
        insert.Parameters.AddWithValue("environment", environment);
        insert.Parameters.AddWithValue("snapshot", JsonSerializer.Serialize(dispatch, OutboxJson));
        if (await insert.ExecuteScalarAsync(token) is Guid)
        {
            foreach (var email in recipients)
            {
                await using var queue = new NpgsqlCommand("""
                    INSERT INTO module065_teams_outbox(dispatch_id,environment,recipient,status,diagnostic_code)
                    VALUES(@id,@environment,@recipient,@status,@code);
                    """, connection, transaction);
                queue.Parameters.AddWithValue("id", dispatch.DispatchId); queue.Parameters.AddWithValue("environment", environment);
                queue.Parameters.AddWithValue("recipient", email); queue.Parameters.AddWithValue("status", allowed ? "queued" : "suppressed");
                queue.Parameters.AddWithValue("code", allowed ? "" : "TEAMS_CONFIGURATION_OR_RECIPIENT_BOUNDARY_BLOCKED");
                await queue.ExecuteNonQueryAsync(token);
            }
        }
        await transaction.CommitAsync(token);
    }

    private static void StartOutboxWorker(WebApplication app)
    {
        if (Interlocked.Exchange(ref _outboxStarted, 1) != 0) return;
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Module065TeamsOutbox");
        lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            while (!lifetime.ApplicationStopping.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), lifetime.ApplicationStopping);
                    await RunOutboxAsync(lifetime.ApplicationStopping);
                }
                catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested) { break; }
                catch (Exception error) { logger.LogWarning("Teams mirror worker requires attention: {DiagnosticType}", error.GetType().Name); }
            }
        }, CancellationToken.None));
    }

    internal static async Task RunOutboxAsync(CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(ProjectNotificationRepository.ConnectionString());
        await connection.OpenAsync(token);
        if (!await OutboxReadyAsync(connection, token)) return;
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(null);
        if (environment is not ("test" or "production")) return;
        await using var gate = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@key, 650128));", connection);
        gate.Parameters.AddWithValue("key", "teams-notification-outbox:" + environment);
        if (await gate.ExecuteScalarAsync(token) is not true) return;
        try
        {
            await RecoverOutboxAsync(connection, environment, token);
            var rows = new List<(Guid Id, string Recipient, string Snapshot, int Attempts)>();
            await using (var due = new NpgsqlCommand("""
                SELECT q.dispatch_id,q.recipient,e.snapshot::text,q.attempt_count
                FROM module065_teams_outbox q JOIN module065_teams_outbox_events e USING(dispatch_id,environment)
                WHERE q.environment=@environment AND q.status IN ('queued','retry_wait') AND q.available_at<=now()
                  AND e.expires_at>now() AND q.attempt_count<5 ORDER BY q.available_at,e.created_at,q.recipient LIMIT 20;
                """, connection))
            {
                due.Parameters.AddWithValue("environment", environment);
                await using var reader = await due.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) rows.Add((reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetInt32(3)));
            }
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                await using var budget = new NpgsqlCommand("SELECT count(*) FROM module065_teams_outbox_attempts WHERE environment=@environment AND started_at>now()-interval '5 minutes';", connection);
                budget.Parameters.AddWithValue("environment", environment);
                if (Convert.ToInt64(await budget.ExecuteScalarAsync(token)) >= CallsPerFiveMinutes) break;
                ProjectNotificationDispatchRow? snapshot;
                try { snapshot = JsonSerializer.Deserialize<ProjectNotificationDispatchRow>(row.Snapshot, OutboxJson); }
                catch (JsonException) { snapshot = null; }
                if (snapshot is null) { await SetOutboxStatusAsync(connection,row.Id,environment,row.Recipient,"failed","TEAMS_SNAPSHOT_INVALID",token); continue; }
                var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(null, token);
                var configuration = await LoadAsync(connection, environment, token);
                if (!MaySend(snapshot.DeliveryBoundary,readiness.RecipientBoundary,environment,readiness.ConfiguredEnvironment,configuration.Enabled,false))
                { await SetOutboxStatusAsync(connection,row.Id,environment,row.Recipient,"suppressed","TEAMS_CURRENT_BOUNDARY_BLOCKED",token); continue; }
                Module065NotificationParityAuthorization.Decision authorization;
                try { authorization = await Module065NotificationParityAuthorization.ValidateAsync(connection,snapshot,row.Recipient,environment,token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception)
                { await SetOutboxStatusAsync(connection,row.Id,environment,row.Recipient,"retry_wait","TEAMS_SOURCE_READ_UNAVAILABLE",token); continue; }
                if (!authorization.Allowed)
                { await SetOutboxStatusAsync(connection,row.Id,environment,row.Recipient,authorization.Defer ? "retry_wait" : "suppressed",authorization.Code,token); continue; }
                if (authorization.Boundary != "production_governed")
                { await SetOutboxStatusAsync(connection,row.Id,environment,row.Recipient,"suppressed","TEAMS_SOURCE_BOUNDARY_BLOCKED",token); continue; }
                // A committed claim precedes the external request. A crash is UNKNOWN, never automatic resend.
                await using (var transaction = await connection.BeginTransactionAsync(token))
                {
                    await using var claim = new NpgsqlCommand("""
                        UPDATE module065_teams_outbox SET status='sending',attempt_count=attempt_count+1,updated_at=now()
                        WHERE dispatch_id=@id AND environment=@environment AND recipient=@recipient AND status IN ('queued','retry_wait');
                        INSERT INTO module065_teams_outbox_attempts(attempt_id,dispatch_id,environment,recipient)
                        VALUES(@attempt,@id,@environment,@recipient);
                        """, connection, transaction);
                    claim.Parameters.AddWithValue("id",row.Id); claim.Parameters.AddWithValue("environment",environment);
                    claim.Parameters.AddWithValue("recipient",row.Recipient); claim.Parameters.AddWithValue("attempt",Guid.NewGuid());
                    await claim.ExecuteNonQueryAsync(token); await transaction.CommitAsync(token);
                }
                var outcome = new MicrosoftTeamsWorkflowProtocol.Outcome("outcome_unknown",new("TEAMS_TRANSPORT_OUTCOME_UNKNOWN","Review provider evidence before any resend."));
                try
                {
                    var services = await MicrosoftTeamsServicesSnapshot.LoadAsync(connection,environment,token);
                    async Task<bool> StillAuthorized(CancellationToken cancellation)
                    {
                        var latest = await LoadAsync(connection,environment,cancellation);
                        var profile = await MicrosoftTeamsServicesSnapshot.LoadAsync(connection,environment,cancellation);
                        var source = await Module065NotificationParityAuthorization.ValidateAsync(connection,snapshot,row.Recipient,environment,cancellation);
                        return latest == configuration && latest.Enabled && services.Matches(profile)
                            && profile.Profile.RecipientBoundary == "production_governed" && source.Allowed
                            && source.Boundary == "production_governed" && MicrosoftEnvironmentRuntimeResolver.Resolve(null)==environment;
                    }
                    using var client = NewClient();
                    if (configuration.DeliveryMode == "power_automate")
                    {
                        var envelope = new MicrosoftTeamsWorkflowProtocol.Envelope(row.Id.ToString("D"),"individual",[row.Recipient],null,null,null,
                            HtmlText(snapshot.Subject,2000),HtmlText(snapshot.TextBody),HtmlText(snapshot.AlertSeverity,100),snapshot.NotificationType,
                            snapshot.SourceModule,Link(Environment.GetEnvironmentVariable("PROJECTPULSE_PUBLIC_BASE_URL"),Text(snapshot.Metadata,"deepLink"),environment),
                            RecipientKey(row.Id,row.Recipient));
                        outcome = await MicrosoftTeamsWorkflowProtocol.ExecuteAsync(client,services.Profile.TenantId,services.Profile.ClientId,
                            services.Secret,configuration.WorkflowAudience,configuration.WorkflowTriggerUrl ?? "",envelope,token,
                            authorizeBeforeSend: StillAuthorized);
                    }
                    else if (configuration.TeamsAppId.HasValue)
                    {
                        var graph = await MicrosoftTeamsNotificationProtocol.ExecuteAsync(client,services.Profile.TenantId,services.Profile.ClientId,
                            services.Secret,configuration.TeamsAppId.Value,row.Recipient,true,StillAuthorized,token);
                        outcome = new(graph.Status,new(graph.Diagnostic.Code,graph.Diagnostic.Message,graph.Diagnostic.RequestId));
                    }
                    else outcome = new("failed",new("TEAMS_CONFIGURATION_INCOMPLETE","Configure an approved Teams delivery mode."));
                }
                catch (Exception) { /* The committed sending claim prevents ambiguous transport replay. */ }
                using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var status = TerminalStatus(outcome.Status,outcome.Diagnostic.Code,row.Attempts+1);
                await using (var evidence = new NpgsqlCommand("""
                    INSERT INTO module065_teams_delivery(delivery_id,dispatch_id,environment,recipient,status,diagnostic_code)
                    VALUES(@delivery,@id,@environment,@recipient,@status,@diagnostic)
                    ON CONFLICT(dispatch_id,environment,recipient) DO UPDATE SET status=EXCLUDED.status,diagnostic_code=EXCLUDED.diagnostic_code,updated_at=now();
                    """,connection))
                {
                    evidence.Parameters.AddWithValue("delivery",Guid.NewGuid()); evidence.Parameters.AddWithValue("id",row.Id);
                    evidence.Parameters.AddWithValue("environment",environment); evidence.Parameters.AddWithValue("recipient","workflow-recipient:"+row.Recipient);
                    evidence.Parameters.AddWithValue("status",outcome.Status is "sent" or "failed" ? outcome.Status : "outcome_unknown");
                    evidence.Parameters.AddWithValue("diagnostic",JsonSerializer.Serialize(new { code=outcome.Diagnostic.Code,message=outcome.Diagnostic.Message,requestId=outcome.Diagnostic.RequestId,workflowRunId=outcome.WorkflowRunId }));
                    await evidence.ExecuteNonQueryAsync(persistence.Token);
                }
                await SetOutboxStatusAsync(connection,row.Id,environment,row.Recipient,status,outcome.Diagnostic.Code,persistence.Token);
            }
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@key,650128));",connection);
            release.Parameters.AddWithValue("key","teams-notification-outbox:"+environment);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    internal static async Task RecoverOutboxAsync(NpgsqlConnection connection, string environment, CancellationToken token)
    {
            await using (var recover = new NpgsqlCommand("""
                UPDATE module065_teams_outbox SET status='outcome_unknown',diagnostic_code='TEAMS_INTERRUPTED_SEND_REQUIRES_RECONCILIATION',updated_at=now()
                WHERE environment=@environment AND status='sending' AND updated_at < now()-interval '5 minutes';
                UPDATE module065_teams_outbox q SET status='suppressed',diagnostic_code='TEAMS_EVENT_EXPIRED',updated_at=now()
                FROM module065_teams_outbox_events e WHERE q.dispatch_id=e.dispatch_id AND q.environment=e.environment
                  AND q.environment=@environment AND q.status IN ('queued','retry_wait') AND e.expires_at<=now();
                """, connection))
            {
                recover.Parameters.AddWithValue("environment", environment); await recover.ExecuteNonQueryAsync(token);
            }
    }

    private static async Task SetOutboxStatusAsync(NpgsqlConnection connection,Guid id,string environment,string recipient,string status,string code,CancellationToken token)
    {
        await using var update = new NpgsqlCommand("""
            UPDATE module065_teams_outbox SET status=@status,diagnostic_code=@code,updated_at=now(),
              available_at=CASE WHEN @status='retry_wait' THEN now()+interval '5 minutes' ELSE available_at END
            WHERE dispatch_id=@id AND environment=@environment AND recipient=@recipient;
            """,connection);
        update.Parameters.AddWithValue("id",id); update.Parameters.AddWithValue("environment",environment);
        update.Parameters.AddWithValue("recipient",recipient); update.Parameters.AddWithValue("status",status); update.Parameters.AddWithValue("code",code);
        await update.ExecuteNonQueryAsync(token);
    }

    private static async Task<IResult> GetOutboxAsync(HttpContext context)
    {
        var access = await AdminExperienceCommon.AuthorizeAsync(context);
        if (access.Failure is not null) return access.Failure;
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if (environment is not ("test" or "production")) return Results.Conflict(new { message="Runtime environment is unresolved." });
        await using var connection = new NpgsqlConnection(access.Context!.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        if (!await OutboxReadyAsync(connection,context.RequestAborted))
            return Results.Ok(new { ready=false,migration="128_module065_email_teams_notification_parity",message="Teams email parity is awaiting its outbox migration." });
        var totals = new Dictionary<string,long>(); var rows = new List<object>();
        await using (var count = new NpgsqlCommand("SELECT status,count(*) FROM module065_teams_outbox WHERE environment=@environment GROUP BY status;",connection))
        {
            count.Parameters.AddWithValue("environment",environment);
            await using var reader = await count.ExecuteReaderAsync(context.RequestAborted);
            while(await reader.ReadAsync(context.RequestAborted)) totals[reader.GetString(0)]=reader.GetInt64(1);
        }
        await using (var recent = new NpgsqlCommand("SELECT dispatch_id,recipient,status,attempt_count,diagnostic_code,updated_at FROM module065_teams_outbox WHERE environment=@environment ORDER BY updated_at DESC LIMIT 50;",connection))
        {
            recent.Parameters.AddWithValue("environment",environment);
            await using var reader = await recent.ExecuteReaderAsync(context.RequestAborted);
            while(await reader.ReadAsync(context.RequestAborted)) rows.Add(new { dispatchId=reader.GetGuid(0),recipient=reader.GetString(1),status=reader.GetString(2),attempts=reader.GetInt32(3),diagnosticCode=reader.GetString(4),updatedAt=reader.GetFieldValue<DateTimeOffset>(5) });
        }
        var canRetry = !AdminExperienceCommon.IsViewAs(context) && (access.Context.Roles.Contains("SUPER_ADMINISTRATOR") || access.Context.Roles.Contains("ADMINISTRATOR"));
        return Results.Ok(new { ready=true,totals,recent=rows,canRetry,acceptedMeans="Provider accepted the request; confirm the flow run and Teams message.",
            unknownOutcomeRequiresReconciliation=true,independentOfEmail=true,maximumCallsPerFiveMinutes=CallsPerFiveMinutes });
    }
    private sealed record RetryOutboxRequest(Guid DispatchId,string Recipient,int ExpectedAttemptCount,string Confirmation);
    private static async Task<IResult> RetryOutboxAsync(HttpContext context)
    {
        if(!AiProviderConfigurationModule.SameOrigin(context)) return Results.Json(new { message="Same-origin request required." },statusCode:403);
        var access=await AdminExperienceCommon.AuthorizeAsync(context);
        if(access.Failure is not null) return access.Failure;
        if(AdminExperienceCommon.IsViewAs(context) || !(access.Context!.Roles.Contains("SUPER_ADMINISTRATOR") || access.Context.Roles.Contains("ADMINISTRATOR")))
            return Results.Json(new { message="Only an administrator outside View-As can retry a Teams notification." },statusCode:403);
        RetryOutboxRequest? request;
        try { request=await context.Request.ReadFromJsonAsync<RetryOutboxRequest>(cancellationToken:context.RequestAborted); }
        catch(JsonException) { return Results.BadRequest(new { message="Invalid Teams retry request." }); }
        if(request is null || request.DispatchId==Guid.Empty || !ValidEmail(request.Recipient) || request.ExpectedAttemptCount is <1 or >=MaximumAttempts
            || request.Confirmation!="RETRY TEAMS ONLY") return Results.BadRequest(new { message="Confirm one failed Teams-only retry. Email will not be resent." });
        var environment=MicrosoftEnvironmentRuntimeResolver.Resolve(context);
        if(environment is not ("test" or "production")) return Results.Conflict(new { message="Environment unresolved." });
        await using var connection=new NpgsqlConnection(access.Context.ConnectionString);await connection.OpenAsync(context.RequestAborted);
        if(!await OutboxReadyAsync(connection,context.RequestAborted)) return Results.Conflict(new { message="Teams notification outbox is not initialized." });
        var recipient=request.Recipient.Trim().ToLowerInvariant();
        ProjectNotificationDispatchRow? snapshot=null;
        await using(var source=new NpgsqlCommand("SELECT snapshot::text FROM module065_teams_outbox_events WHERE dispatch_id=@id AND environment=@environment AND expires_at>now();",connection))
        {
            source.Parameters.AddWithValue("id",request.DispatchId);source.Parameters.AddWithValue("environment",environment);
            if(await source.ExecuteScalarAsync(context.RequestAborted) is string json) snapshot=JsonSerializer.Deserialize<ProjectNotificationDispatchRow>(json,OutboxJson);
        }
        if(snapshot is null) return Results.Conflict(new { message="This notification is missing or expired." });
        var readiness=await Module065ProjectNotificationDelivery.GetReadinessAsync(context,context.RequestAborted);
        var configuration=await LoadAsync(connection,environment,context.RequestAborted);
        var authorization=await Module065NotificationParityAuthorization.ValidateAsync(connection,snapshot,recipient,environment,context.RequestAborted);
        if(!authorization.Allowed || !MaySend(authorization.Boundary,readiness.RecipientBoundary,environment,readiness.ConfiguredEnvironment,configuration.Enabled,false))
            return Results.Conflict(new { message="Current source, recipient or delivery policy does not permit a Teams retry." });
        await using var transaction=await connection.BeginTransactionAsync(context.RequestAborted);
        await using var retry=new NpgsqlCommand("""
            UPDATE module065_teams_outbox SET status='queued',diagnostic_code='TEAMS_ONLY_RETRY_REQUESTED',available_at=now(),updated_at=now()
            WHERE dispatch_id=@id AND environment=@environment AND recipient=@recipient
              AND status='failed' AND attempt_count=@expected AND attempt_count<5 RETURNING dispatch_id;
            """,connection,transaction);
        retry.Parameters.AddWithValue("id",request.DispatchId);retry.Parameters.AddWithValue("environment",environment);
        retry.Parameters.AddWithValue("recipient",recipient);retry.Parameters.AddWithValue("expected",request.ExpectedAttemptCount);
        if(await retry.ExecuteScalarAsync(context.RequestAborted) is not Guid)
            return Results.Conflict(new { message="Only an unchanged, definitely failed Teams request can be retried. Accepted, suppressed and unknown outcomes cannot be replayed." });
        await using var audit=new NpgsqlCommand("INSERT INTO module065_teams_outbox_actions(action_id,dispatch_id,environment,recipient,actor_user_id,action) VALUES(@action,@id,@environment,@recipient,@actor,'retry_failed_teams_only');",connection,transaction);
        audit.Parameters.AddWithValue("action",Guid.NewGuid());audit.Parameters.AddWithValue("id",request.DispatchId);
        audit.Parameters.AddWithValue("environment",environment);audit.Parameters.AddWithValue("recipient",recipient);audit.Parameters.AddWithValue("actor",access.Context.UserId);
        await audit.ExecuteNonQueryAsync(context.RequestAborted);await transaction.CommitAsync(context.RequestAborted);
        return Results.Ok(new { status="queued",message="Teams-only retry queued. Email was not resent." });
    }

}
