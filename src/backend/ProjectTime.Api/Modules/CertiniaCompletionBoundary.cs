using System.Data;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class CertiniaBillingModule
{
    // Recording a manual handoff never invokes this sender. Only actual PTC/admin
    // identities may authorize a new package; Billing retains its existing worker.
    private static async Task<IResult> QueueOrSendAsync(
        Guid invoiceId, CertiniaInvoiceSendRequest request, HttpContext context)
    {
        var sessionUserId = SessionUserId(context);
        if (sessionUserId is null) return SessionRequired();
        await using (var connection = await OpenConnectionAsync())
        {
            var authority = await WorkRegisterAuthorization.GetAccessAsync(
                connection, context, cancellationToken: context.RequestAborted);
            if (authority.IsViewAs || !authority.CanEditAll || authority.ActualUserId != sessionUserId.Value)
                return Results.Json(new
                {
                    status = "billing_package_authorization_required",
                    message = "PTC authorizes Certinia packages. Use an actual PTC or administrator session; View-As cannot transmit billing."
                }, statusCode: StatusCodes.Status403Forbidden);
        }
        try
        {
            return await QueueOrSendCoreAsync(invoiceId, request, context);
        }
        catch (ManualCertiniaDuplicateException exception)
        {
            return Results.Conflict(new { status = "manual_certinia_handoff_recorded", message = exception.Message, stateChanged = false });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(new { status = "billing_package_authorization_required", message = "Billing authority changed. Refresh your session before continuing." },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (PostgresException exception) when (exception.SqlState is "40001" or "40P01")
        {
            return Results.Conflict(new { status = "billing_changed_concurrently", message = "Billing evidence changed concurrently. Refresh and verify the existing delivery before trying again.", stateChanged = false });
        }
    }

    private static async Task ValidateCompletionQueueAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, CertiniaInvoiceSnapshot invoice, Guid actorUserId)
    {
        await WorkLifecycleModule.GuardManualCertiniaDuplicateAsync(connection, transaction,
            invoice.Header.ProjectId, invoice.Header.BillingInvoiceId, CancellationToken.None);
        await using var authority = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM app_users u
                JOIN app_user_role_assignments a ON a.user_id=u.user_id
                JOIN app_roles r ON r.app_role_id=a.app_role_id
                WHERE u.user_id=@actor AND u.is_active AND a.is_active AND r.is_active
                  AND upper(r.role_code) IN ('PROJECT_TEAM_COORDINATOR','ADMINISTRATOR','SUPER_ADMINISTRATOR')
            );
            """, connection, transaction);
        authority.Parameters.AddWithValue("actor", actorUserId);
        if (!Convert.ToBoolean(await authority.ExecuteScalarAsync())) throw new UnauthorizedAccessException();
        await using var current = new NpgsqlCommand("""
            SELECT invoice_status FROM billing_invoices
            WHERE billing_invoice_id=@invoice AND project_id=@project FOR SHARE;
            """, connection, transaction);
        current.Parameters.AddWithValue("invoice", invoice.Header.BillingInvoiceId);
        current.Parameters.AddWithValue("project", invoice.Header.ProjectId);
        var status = await current.ExecuteScalarAsync() as string;
        if (status is null || status.ToLowerInvariant() is "void" or "voided")
            throw new ManualCertiniaDuplicateException("The invoice is missing or voided. No Certinia package was queued.");
    }

    private static async Task<CertiniaQueueResult?> ReuseCompletionQueueAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid invoiceId)
    {
        // One business invoice is one delivery even when PDF/Excel or privacy
        // choices differ. A new artifact must not become a second invoice send.
        await using var command = new NpgsqlCommand("""
            SELECT external_integration_outbox_id, idempotency_key, delivery_status, attempt_count,
                COALESCE(payload_json#>>'{document,format}',''),
                COALESCE(payload_json#>>'{document,fileName}',''),
                COALESCE(payload_json#>>'{document,sha256}',''),
                COALESCE((payload_json->>'resourceNamesIncluded')::boolean,FALSE)
            FROM external_integration_outbox
            WHERE system_code='CERTINIA' AND local_entity='billing_invoice' AND local_entity_id=@invoice
            ORDER BY created_at, external_integration_outbox_id LIMIT 1 FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("invoice", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new CertiniaQueueResult(reader.GetGuid(0), reader.GetString(1), false,
                reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.GetBoolean(7))
            : null;
    }

    private static async Task<CertiniaOutboxClaim?> ClaimCompletionSafeOutboxAsync(
        NpgsqlConnection connection, Guid outboxId)
    {
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            Guid projectId;
            Guid invoiceId;
            await using (var lookup = new NpgsqlCommand("""
                SELECT i.project_id, i.billing_invoice_id FROM external_integration_outbox o
                JOIN billing_invoices i ON i.billing_invoice_id=o.local_entity_id
                WHERE o.external_integration_outbox_id=@outbox AND o.system_code='CERTINIA'
                  AND o.local_entity='billing_invoice' AND o.delivery_status IN ('pending','failed')
                  AND lower(COALESCE(i.invoice_status,'')) NOT IN ('void','voided');
                """, connection, transaction))
            {
                lookup.Parameters.AddWithValue("outbox", outboxId);
                await using var reader = await lookup.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return null;
                projectId = reader.GetGuid(0); invoiceId = reader.GetGuid(1);
            }
            try
            {
                // Uses the same project lock as manual attestations and queueing.
                // After commit, 'processing' prevents a concurrent manual handoff.
                await WorkLifecycleModule.GuardManualCertiniaDuplicateAsync(connection,
                    transaction, projectId, invoiceId, CancellationToken.None);
            }
            catch (ManualCertiniaDuplicateException exception)
            {
                await using var blocked = new NpgsqlCommand("""
                    UPDATE external_integration_outbox SET delivery_status='dead_letter',
                        next_attempt_at=NULL, last_error=@reason, updated_at=NOW()
                    WHERE external_integration_outbox_id=@outbox AND delivery_status IN ('pending','failed');
                    """, connection, transaction);
                blocked.Parameters.AddWithValue("outbox", outboxId);
                blocked.Parameters.AddWithValue("reason", exception.Message);
                if (await blocked.ExecuteNonQueryAsync() == 1)
                    await AppendInvoiceEventAsync(connection, transaction, invoiceId,
                        "certinia_manual_handoff_blocked", "", "", null,
                        "Automatic delivery blocked by existing manual billing evidence; no external request was made.", new { outboxId });
                await transaction.CommitAsync();
                return null;
            }
            CertiniaOutboxClaim? claim;
            await using (var command = new NpgsqlCommand("""
                UPDATE external_integration_outbox SET delivery_status='processing',
                    attempt_count=attempt_count+1, last_attempt_at=NOW(), updated_at=NOW(), last_error=''
                WHERE external_integration_outbox_id=@outbox AND system_code='CERTINIA'
                  AND local_entity='billing_invoice' AND local_entity_id=@invoice
                  AND delivery_status IN ('pending','failed') AND attempt_count<8
                  AND (next_attempt_at IS NULL OR next_attempt_at<=NOW())
                RETURNING external_integration_outbox_id, local_entity_id, attempt_count, payload_json::text;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("outbox", outboxId);
                command.Parameters.AddWithValue("invoice", invoiceId);
                await using var reader = await command.ExecuteReaderAsync();
                claim = await reader.ReadAsync()
                    ? new CertiniaOutboxClaim(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3)) : null;
            }
            await transaction.CommitAsync();
            return claim;
        }
        catch (PostgresException exception) when (exception.SqlState is "40001" or "40P01")
        {
            await transaction.RollbackAsync();
            return null; // Concurrent state changed. Leave the existing item for a later safe retry.
        }
    }
}
