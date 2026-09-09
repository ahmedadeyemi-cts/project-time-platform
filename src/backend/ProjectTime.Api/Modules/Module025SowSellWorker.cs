using System.Net;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal sealed class Module025SowSellWorker(IServiceProvider services, ILogger<Module025SowSellWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;
            try { worked = await Module025SowGsdModule.ProcessNextSowSellAsync(services, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Never log customer content, recipients, documents or credentials.
                logger.LogWarning("Module 025 SELL worker paused. DiagnosticType={DiagnosticType}", exception.GetType().Name);
            }
            try { await Task.Delay(worked ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

public static partial class Module025SowGsdModule
{
    private sealed record SowSellWork(Module025SellPackage Package, Guid ActorUserId, int SourceRevision);

    internal static async Task<bool> ProcessNextSowSellAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var environment = MicrosoftEnvironmentRuntimeResolver.Resolve(null) ?? string.Empty;
        var connectionString = BuildConnectionString();
        if (environment is not ("test" or "production") || string.IsNullOrWhiteSpace(connectionString)) return false;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await SowSellSchemaReadyAsync(connection, cancellationToken)) return false;
        await RecoverExpiredSowClaimsAsync(connection, environment, cancellationToken);
        if (await ProcessNextSowNotificationAsync(connection, environment, cancellationToken)) return true;
        var publisher = services.GetRequiredService<IModule025SellPublisher>();
        var readiness = await publisher.GetReadinessAsync(environment, cancellationToken);
        if (!readiness.Ready) return false;
        var work = await ClaimSowSellAsync(connection, environment, cancellationToken);
        if (work is null) return false;
        if (!await SowSellAuthorityStillValidAsync(connection, work, cancellationToken)
            || !await SowRecipientsStillValidAsync(connection, work.Package.EngagementId, work.Package.Recipients, cancellationToken))
        {
            await FinishSowSellFailureAsync(connection, work, "failed", "SOURCE_OR_AUTHORITY_CHANGED_BEFORE_WRITE", cancellationToken);
            return true;
        }
        Module025SellPublishOutcome outcome;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            outcome = await publisher.PublishAsync(work.Package, timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // A lost response can follow a successful remote write. Never create
            // another SELL record or send the success email on an assumption.
            await FinishSowSellFailureAsync(connection, work, "needs_reconciliation", "SELL_OUTCOME_UNKNOWN", cancellationToken);
            return true;
        }
        if (outcome.Kind != Module025SellOutcomeKind.Accepted || !Module025SowSellPolicy.ValidReceipt(work.Package, outcome.Receipt))
        {
            await FinishSowSellFailureAsync(connection, work,
                outcome.Kind == Module025SellOutcomeKind.RejectedBeforeWrite ? "failed" : "needs_reconciliation",
                outcome.Kind == Module025SellOutcomeKind.Accepted ? "SELL_RECEIPT_MISMATCH" : SowSellDiagnostic(outcome.DiagnosticCode), cancellationToken);
            return true;
        }
        await CommitVerifiedSowReceiptAsync(connection, work, outcome.Receipt!, cancellationToken);
        return true;
    }

    private static async Task RecoverExpiredSowClaimsAsync(NpgsqlConnection connection, string environment, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH recovered AS (
                UPDATE module025_sow_sell_dispatch d
                SET sell_status=CASE WHEN d.sell_status='publishing' THEN 'needs_reconciliation' ELSE d.sell_status END,
                    mail_status=CASE WHEN d.mail_status='sending' THEN 'needs_reconciliation' ELSE d.mail_status END,
                    diagnostic_code='WORKER_CLAIM_EXPIRED',claimed_at=NULL,updated_at=now()
                FROM module025_sow_sell_submissions s
                WHERE d.submission_id=s.submission_id AND s.runtime_environment=@environment
                  AND (d.sell_status='publishing' OR d.mail_status='sending')
                  AND d.claimed_at < now()-interval '2 minutes'
                RETURNING d.submission_id,d.sell_status,d.mail_status
            )
            INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,summary,evidence_json)
            SELECT s.engagement_id,'sell_reconciliation_required',s.actor_user_id,v.source_revision,
                'A worker stopped before its external outcome was recorded. No automatic repeat write will occur.',
                jsonb_build_object('submissionId',s.submission_id,'sellStatus',r.sell_status,'mailStatus',r.mail_status,'diagnosticCode','WORKER_CLAIM_EXPIRED')
            FROM recovered r JOIN module025_sow_sell_submissions s USING(submission_id)
            JOIN module025_sow_gsd_versions v USING(version_id);
            """, connection);
        command.Parameters.AddWithValue("environment", environment);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SowSellWork?> ClaimSowSellAsync(NpgsqlConnection connection, string environment, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid? id;
        await using (var find = new NpgsqlCommand("""
            SELECT d.submission_id FROM module025_sow_sell_dispatch d
            JOIN module025_sow_sell_submissions s USING(submission_id)
            WHERE d.sell_status='queued' AND s.runtime_environment=@environment
              AND NOT EXISTS(SELECT 1 FROM module025_sow_sell_dispatch other
                  WHERE other.engagement_id=d.engagement_id AND other.sell_status IN ('publishing','needs_reconciliation'))
            ORDER BY s.created_at,s.submission_id LIMIT 1 FOR UPDATE OF d SKIP LOCKED;
            """, connection, transaction))
        {
            find.Parameters.AddWithValue("environment", environment);
            var result = await find.ExecuteScalarAsync(cancellationToken);
            id = result is Guid value ? value : null;
        }
        if (!id.HasValue) return null;
        try
        {
            await using var claim = new NpgsqlCommand("UPDATE module025_sow_sell_dispatch SET sell_status='publishing',claimed_at=now(),updated_at=now() WHERE submission_id=@id;", connection, transaction);
            claim.Parameters.AddWithValue("id", id.Value);
            await claim.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505" && exception.ConstraintName == "ux_module025_one_uncertain_sell_write")
        {
            // A second replica claimed another version of this root. This
            // transaction rolls back before either adapter invocation.
            return null;
        }
        var work = await LoadSowSellWorkAsync(connection, id.Value, cancellationToken);
        await InsertEventAsync(connection, transaction, work.Package.EngagementId, work.ActorUserId, work.SourceRevision,
            "sell_publication_started", "The governed worker claimed the version for SELL publication.",
            new { submissionId = id.Value, work.Package.VersionId, work.Package.IdempotencyKey }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return work;
    }

    private static async Task<SowSellWork> LoadSowSellWorkAsync(NpgsqlConnection connection, Guid submissionId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT s.engagement_id,s.version_id,s.runtime_environment,s.actor_user_id,s.recipients_json::text,
                v.version_number,v.source_revision,v.source_json::text,v.sow_content,v.gsd_content,v.sow_sha256,v.gsd_sha256,
                l.sell_record_id
            FROM module025_sow_sell_submissions s JOIN module025_sow_gsd_versions v USING(version_id)
            LEFT JOIN module025_sow_sell_links l ON l.engagement_id=s.engagement_id
                AND l.destination_key=s.destination_key AND l.runtime_environment=s.runtime_environment
            WHERE s.submission_id=@id;
            """, connection);
        command.Parameters.AddWithValue("id", submissionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The retained SELL submission is missing.");
        var source = JsonSerializer.Deserialize<Module025EngagementRow>(reader.GetString(7), Module025SowSellPolicy.Json)
            ?? throw new InvalidOperationException("The retained document source is invalid.");
        var recipients = JsonSerializer.Deserialize<Module025SellRecipient[]>(reader.GetString(4), Module025SowSellPolicy.Json) ?? [];
        return new(new Module025SellPackage(submissionId, reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(5),
            source.EngagementNumber, source.CustomerName, reader.GetString(2), reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.GetFieldValue<byte[]>(8), reader.GetFieldValue<byte[]>(9), reader.GetString(10), reader.GetString(11), recipients),
            reader.GetGuid(3), reader.GetInt32(6));
    }

    private static async Task<bool> SowSellAuthorityStillValidAsync(NpgsqlConnection connection, SowSellWork work, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM app_users actor
                JOIN module025_sow_gsd_engagements e ON e.engagement_id=@engagement
                WHERE actor.user_id=@actor AND actor.is_active=TRUE AND e.is_active=TRUE AND e.status='confirmed'
                  AND EXISTS(SELECT 1 FROM app_user_role_assignments a JOIN app_roles r USING(app_role_id)
                    WHERE a.user_id=actor.user_id AND a.is_active=TRUE AND r.is_active=TRUE
                      AND (upper(r.role_code)=ANY(@admins) OR (actor.user_id=e.owner_user_id AND upper(r.role_code)=ANY(@sa))))
                  AND @version=(SELECT v.version_id FROM module025_sow_gsd_versions v WHERE v.engagement_id=e.engagement_id ORDER BY v.version_number DESC LIMIT 1));
            """, connection);
        command.Parameters.AddWithValue("engagement", work.Package.EngagementId);
        command.Parameters.AddWithValue("actor", work.ActorUserId);
        command.Parameters.AddWithValue("version", work.Package.VersionId);
        command.Parameters.AddWithValue("admins", new[] { "SUPER_ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "ADMINISTRATOR" });
        command.Parameters.AddWithValue("sa", SolutionArchitectRoles.ToArray());
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<bool> SowRecipientsStillValidAsync(
        NpgsqlConnection connection,
        Guid engagementId,
        IReadOnlyList<Module025SellRecipient> recipients,
        CancellationToken cancellationToken)
    {
        if (recipients.Count != 3 || recipients.Select(r => r.Role).Distinct().Count() != 3
            || recipients.Any(r => r.Role is not ("solution_architect" or "account_executive" or "inside_sales") || !Module025SowSellPolicy.ValidEmail(r.Email))) return false;

        await using var command = new NpgsqlCommand("""
            SELECT owner.user_id, owner.email,
                   account_executive.user_id, account_executive.email,
                   inside_sales.user_id, inside_sales.email
            FROM module025_sow_gsd_engagements engagement
            JOIN app_users owner ON owner.user_id=engagement.owner_user_id AND owner.is_active=TRUE
            LEFT JOIN app_users account_executive
                ON account_executive.user_id=engagement.account_executive_user_id
               AND account_executive.is_active=TRUE
            LEFT JOIN app_users inside_sales
                ON inside_sales.user_id=engagement.resale_user_id
               AND inside_sales.is_active=TRUE
            WHERE engagement.engagement_id=@engagement_id
              AND engagement.is_active=TRUE
              AND engagement.status='confirmed'
              AND EXISTS(
                  SELECT 1 FROM app_user_role_assignments assignment
                  JOIN app_roles role USING(app_role_id)
                  WHERE assignment.user_id=engagement.account_executive_user_id
                    AND assignment.is_active=TRUE AND role.is_active=TRUE
                    AND upper(role.role_code)=ANY(@account_executive_roles))
              AND EXISTS(
                  SELECT 1 FROM app_user_role_assignments assignment
                  JOIN app_roles role USING(app_role_id)
                  WHERE assignment.user_id=engagement.resale_user_id
                    AND assignment.is_active=TRUE AND role.is_active=TRUE
                    AND upper(role.role_code)=ANY(@inside_sales_roles));
            """, connection);
        command.Parameters.AddWithValue("engagement_id", engagementId);
        command.Parameters.AddWithValue("account_executive_roles", AccountExecutiveRoles);
        command.Parameters.AddWithValue("inside_sales_roles", InsideSalesRepresentativeRoles);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)
            || reader.IsDBNull(2)
            || reader.IsDBNull(4)
            || reader.IsDBNull(3)
            || reader.IsDBNull(5)) return false;

        var expected = new[]
        {
            (Role: "solution_architect", UserId: reader.GetGuid(0), Email: reader.GetString(1)),
            (Role: "account_executive", UserId: reader.GetGuid(2), Email: reader.GetString(3)),
            (Role: "inside_sales", UserId: reader.GetGuid(4), Email: reader.GetString(5))
        };
        return expected.All(expectedRecipient => recipients.Any(actualRecipient =>
            string.Equals(actualRecipient.Role, expectedRecipient.Role, StringComparison.Ordinal)
            && actualRecipient.UserId == expectedRecipient.UserId
            && string.Equals(actualRecipient.Email.Trim(), expectedRecipient.Email.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task FinishSowSellFailureAsync(NpgsqlConnection connection, SowSellWork work, string status, string diagnostic, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE module025_sow_sell_dispatch SET sell_status=@status,diagnostic_code=@diagnostic,claimed_at=NULL,updated_at=now()
            WHERE submission_id=@id AND sell_status='publishing';
            """, connection, transaction);
        command.Parameters.AddWithValue("id", work.Package.SubmissionId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("diagnostic", SowSellDiagnostic(diagnostic));
        if (await command.ExecuteNonQueryAsync(cancellationToken) > 0)
            await InsertEventAsync(connection, transaction, work.Package.EngagementId, work.ActorUserId, work.SourceRevision,
                "sell_publication_not_confirmed", "SELL acceptance of this SOW/GSD version was not confirmed. No success notification was queued.",
                new { work.Package.SubmissionId, sellStatus = status, diagnosticCode = SowSellDiagnostic(diagnostic) }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task CommitVerifiedSowReceiptAsync(NpgsqlConnection connection, SowSellWork work, Module025SellReceipt receipt, CancellationToken cancellationToken)
    {
        if (!Module025SowSellPolicy.ValidReceipt(work.Package, receipt)) throw new InvalidOperationException("Invalid SELL receipt.");
        var notification = Module025SowSellPolicy.Notification(work.Package, receipt);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var state = new NpgsqlCommand("SELECT sell_status FROM module025_sow_sell_dispatch WHERE submission_id=@id FOR UPDATE;", connection, transaction))
        {
            state.Parameters.AddWithValue("id", work.Package.SubmissionId);
            if (await state.ExecuteScalarAsync(cancellationToken) is not string status || status != "publishing") return;
        }
        await using (var command = new NpgsqlCommand("""
            INSERT INTO module025_sow_sell_receipts(submission_id,sell_record_id,sow_document_id,gsd_document_id,sow_sha256,gsd_sha256,provider_receipt_id)
            VALUES(@id,@record,@sow,@gsd,@sow_hash,@gsd_hash,@receipt);
            INSERT INTO module025_sow_sell_notification_outbox(submission_id,subject,text_body,recipients_json)
            VALUES(@id,@subject,@body,@recipients::jsonb);
            UPDATE module025_sow_sell_dispatch SET sell_status='published',mail_status='queued',diagnostic_code='',claimed_at=NULL,updated_at=now()
            WHERE submission_id=@id;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("id", work.Package.SubmissionId);
            command.Parameters.AddWithValue("record", receipt.SellRecordId);
            command.Parameters.AddWithValue("sow", receipt.SowDocumentId);
            command.Parameters.AddWithValue("gsd", receipt.GsdDocumentId);
            command.Parameters.AddWithValue("sow_hash", work.Package.SowSha256);
            command.Parameters.AddWithValue("gsd_hash", work.Package.GsdSha256);
            command.Parameters.AddWithValue("receipt", receipt.ProviderReceiptId);
            command.Parameters.AddWithValue("subject", notification.Subject);
            command.Parameters.AddWithValue("body", notification.Body);
            command.Parameters.AddWithValue("recipients", JsonSerializer.Serialize(work.Package.Recipients, Module025SowSellPolicy.Json));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertEventAsync(connection, transaction, work.Package.EngagementId, work.ActorUserId, work.SourceRevision,
            "sell_submission_verified", "SELL record and both retained document hashes were verified. The quote-processing notification was queued atomically.", receipt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> ProcessNextSowNotificationAsync(NpgsqlConnection connection, string environment, CancellationToken cancellationToken)
    {
        Guid? id;
        string subject;
        string body;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await using (var find = new NpgsqlCommand("""
                SELECT d.submission_id,o.subject,o.text_body FROM module025_sow_sell_dispatch d
                JOIN module025_sow_sell_submissions s USING(submission_id)
                JOIN module025_sow_sell_receipts r USING(submission_id)
                JOIN module025_sow_sell_notification_outbox o USING(submission_id)
                WHERE d.sell_status='published' AND d.mail_status='queued' AND s.runtime_environment=@environment
                ORDER BY o.created_at LIMIT 1 FOR UPDATE OF d SKIP LOCKED;
                """, connection, transaction))
            {
                find.Parameters.AddWithValue("environment", environment);
                await using var reader = await find.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return false;
                id = reader.GetGuid(0);
                subject = reader.GetString(1);
                body = reader.GetString(2);
            }
            await using var claim = new NpgsqlCommand("UPDATE module025_sow_sell_dispatch SET mail_status='sending',claimed_at=now(),updated_at=now() WHERE submission_id=@id;", connection, transaction);
            claim.Parameters.AddWithValue("id", id.Value);
            await claim.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        var work = await LoadSowSellWorkAsync(connection, id.Value, cancellationToken);
        var readiness = await Module065ProjectNotificationDelivery.GetReadinessAsync(null, cancellationToken);
        Module065MailDeliveryResult? delivery = null;
        string mailStatus;
        string diagnostic;
        if (!await SowRecipientsStillValidAsync(connection, work.Package.EngagementId, work.Package.Recipients, cancellationToken))
        {
            mailStatus = "failed";
            diagnostic = "RECIPIENT_ASSIGNMENT_REVIEW_REQUIRED";
        }
        else if (!readiness.LiveDeliveryEnabled || readiness.RuntimeEnvironment != environment || readiness.ConfiguredEnvironment != environment)
        {
            // Especially important in Protected Test: do not bypass test_only,
            // and do not replay suppressed UAT mail after a profile change.
            mailStatus = "suppressed";
            diagnostic = "MODULE065_BOUNDARY_OR_TRANSPORT_BLOCKED";
        }
        else
        {
            try
            {
                var recipients = work.Package.Recipients.GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderBy(r => r.RecipientType == "to" ? 0 : 1).First())
                    .Select(r => new ProjectNotificationUser(r.UserId, r.DisplayName, r.Email, r.Role,
                        $"module025:{work.Package.SubmissionId:D}", r.RecipientType)).ToArray();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                delivery = await Module065ProjectNotificationDelivery.DeliverAsync(subject, body,
                    "<p>" + WebUtility.HtmlEncode(body).Replace("\n", "<br />", StringComparison.Ordinal) + "</p>",
                    recipients, null, timeout.Token).WaitAsync(timeout.Token);
                mailStatus = delivery.Sent ? "provider_accepted"
                    : delivery.Status is "queued" or "suppressed" ? "suppressed" : "needs_reconciliation";
                diagnostic = SowSellDiagnostic(delivery.DiagnosticCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                mailStatus = "needs_reconciliation";
                diagnostic = "MAIL_OUTCOME_UNKNOWN";
            }
        }
        await using var completed = await connection.BeginTransactionAsync(cancellationToken);
        await using var update = new NpgsqlCommand("""
            UPDATE module025_sow_sell_dispatch SET mail_status=@status,diagnostic_code=@diagnostic,claimed_at=NULL,updated_at=now()
            WHERE submission_id=@id AND mail_status='sending';
            """, connection, completed);
        update.Parameters.AddWithValue("id", work.Package.SubmissionId);
        update.Parameters.AddWithValue("status", mailStatus);
        update.Parameters.AddWithValue("diagnostic", diagnostic);
        if (await update.ExecuteNonQueryAsync(cancellationToken) > 0)
            await InsertEventAsync(connection, completed, work.Package.EngagementId, work.ActorUserId, work.SourceRevision,
                "sell_notification_result", "The governed quote-processing notification outcome was recorded. Provider acceptance is not mailbox delivery confirmation.",
                new { work.Package.SubmissionId, mailStatus, diagnosticCode = diagnostic,
                    provider = delivery?.Provider, providerMessageId = delivery?.ProviderMessageId,
                    recipientBoundary = delivery?.RecipientBoundary ?? readiness.RecipientBoundary }, cancellationToken);
        await completed.CommitAsync(cancellationToken);
        return true;
    }

    private static string SowSellDiagnostic(string value) => value.Length is > 0 and <= 160
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? value : "SELL_DELIVERY_REQUIRES_REVIEW";
}
