using System.Data;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public sealed record BillingRecoveryRequest(Guid OperationId, string Action, string Reference, string Reason, bool Confirmed);

public static partial class InvoiceBillingModule
{
    private static void MapBillingRecoveryEndpoints(WebApplication app)
    {
        app.MapGet("/api/billing/invoices/{invoiceId:guid}/reconciliation", (Func<Guid, HttpContext, Task<IResult>>)GetBillingRecoveryAsync);
        app.MapPost("/api/billing/invoices/{invoiceId:guid}/reconciliation", (Func<Guid, BillingRecoveryRequest, HttpContext, Task<IResult>>)SaveBillingRecoveryAsync);
    }

    private static async Task<IResult> GetBillingRecoveryAsync(Guid invoiceId, HttpContext context)
    {
        var userId = GetSessionUserId(context);
        if (userId is null) return SessionRequired();
        if (BillingViewAs(context)) return Results.Forbid();
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (ValidateConfig(config) is { } error) return error;
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var projectId = await LoadInvoiceProjectIdAsync(connection, invoiceId);
        var access = await LoadAccessContextAsync(connection, userId.Value);
        if (projectId is null || !access.CanViewBilling || !await CanAccessProjectAsync(connection, access, projectId.Value)) return Results.NotFound();
        await using var command = new NpgsqlCommand("""
            SELECT immutable_snapshot_json::text,
                COALESCE((SELECT jsonb_agg(jsonb_build_object('action',event_type,'reference',event_json->>'reference',
                    'reason',event_reason,'actorUserId',actor_user_id,'recordedAt',created_at) ORDER BY COALESCE((event_json->>'revision')::bigint,0),created_at)
                    FROM billing_invoice_events WHERE billing_invoice_id=@invoice AND event_type LIKE 'billing_recovery_%'),'[]')::text
            FROM billing_invoices WHERE billing_invoice_id=@invoice;
            """, connection);
        command.Parameters.AddWithValue("invoice", invoiceId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Results.NotFound();
        using var snapshot = JsonDocument.Parse(reader.GetString(0));
        using var history = JsonDocument.Parse(reader.GetString(1));
        return Results.Ok(new { invoiceId, canRecord = CanApproveBillingException(access),
            originalEvidence = snapshot.RootElement.Clone(), history = history.RootElement.Clone() });
    }

    private static async Task<IResult> SaveBillingRecoveryAsync(Guid invoiceId, BillingRecoveryRequest request, HttpContext context)
    {
        var userId = GetSessionUserId(context);
        if (userId is null) return SessionRequired();
        if (BillingViewAs(context)) return Results.Forbid();
        if (request.OperationId == Guid.Empty || !request.Confirmed
            || request.Action is not ("hold_delivery" or "resume_delivery" or "manual_handoff" or "certinia_match" or "sell_verified")
            || string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Trim().Length < 2 || request.Reference.Length > 500
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 10 || request.Reason.Length > 1000)
            return Results.BadRequest(new { message = "Choose a reconciliation action and supply the verified reference, audit reason and confirmation." });
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (ValidateConfig(config) is { } error) return error;
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, context.RequestAborted);
        try
        {
            var access = await LoadAccessContextAsync(connection, userId.Value, transaction);
            // Invoice lookup precedes the shared project lock; it never acquires an invoice lock first.
            Guid projectId;
            await using (var lookup = new NpgsqlCommand("SELECT project_id FROM billing_invoices WHERE billing_invoice_id=@invoice", connection, transaction))
            {
                lookup.Parameters.AddWithValue("invoice", invoiceId);
                if (await lookup.ExecuteScalarAsync() is not Guid id) return Results.NotFound();
                projectId = id;
            }
            if (!CanApproveBillingException(access) || !await CanAccessProjectAsync(connection, access, projectId, transaction)) return Results.Forbid();
            await LoadProjectForInvoiceAsync(connection, transaction, projectId);
            var hash = BillingHash(JsonSerializer.Serialize(new { invoiceId, actor = userId, request }, ManualJson));
            await using (var replay = new NpgsqlCommand("""
                SELECT event_json->>'requestHash' FROM billing_invoice_events WHERE billing_invoice_id=@invoice
                AND event_type LIKE 'billing_recovery_%' AND event_json->>'operationId'=@operation;
                """, connection, transaction))
            {
                replay.Parameters.AddWithValue("invoice", invoiceId); replay.Parameters.AddWithValue("operation", request.OperationId.ToString());
                if (await replay.ExecuteScalarAsync() is string existing)
                    return existing == hash ? Results.Ok(new { status = "already_recorded", message = "This reconciliation was already recorded. No external request was made." })
                        : Results.Conflict(new { message = "The operation ID belongs to a different reconciliation." });
            }
            await using (var current = new NpgsqlCommand("SELECT invoice_status FROM billing_invoices WHERE billing_invoice_id=@invoice FOR UPDATE", connection, transaction))
            {
                current.Parameters.AddWithValue("invoice", invoiceId);
                if ((await current.ExecuteScalarAsync() as string)?.ToLowerInvariant() is "void" or "voided")
                    return Results.Conflict(new { message = "A void invoice cannot receive a new delivery or commercial attestation." });
            }
            if (request.Action == "resume_delivery")
            {
                await using var receipts = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM billing_invoice_events WHERE billing_invoice_id=@invoice
                        AND event_type IN ('billing_recovery_manual_handoff','billing_recovery_certinia_match'))
                    OR EXISTS(SELECT 1 FROM work_lifecycle_audit_events WHERE project_id=@project AND event_type='completion_checklist_recorded'
                        AND ((event_json#>'{state,sent,coveredInvoiceIds}') @> @ids::jsonb OR event_json#>>'{state,sent,scope}'='final'));
                    """, connection, transaction);
                receipts.Parameters.AddWithValue("invoice", invoiceId); receipts.Parameters.AddWithValue("project", projectId);
                receipts.Parameters.AddWithValue("ids", JsonSerializer.Serialize(new[] { invoiceId }));
                if (Convert.ToBoolean(await receipts.ExecuteScalarAsync()))
                    return Results.Conflict(new { message = "A manual handoff or external invoice match already exists. Do not resume automatic delivery of the same invoice." });
            }
            if (request.Action != "sell_verified")
            {
                await using var check = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM external_integration_outbox WHERE local_entity='billing_invoice'
                        AND local_entity_id=@invoice AND system_code='CERTINIA' AND delivery_status='processing'),
                    EXISTS(SELECT 1 FROM external_integration_outbox WHERE local_entity='billing_invoice'
                        AND local_entity_id=@invoice AND system_code='CERTINIA' AND delivery_status='succeeded');
                    """, connection, transaction);
                check.Parameters.AddWithValue("invoice", invoiceId);
                await using var reader = await check.ExecuteReaderAsync(); await reader.ReadAsync();
                if (reader.GetBoolean(0)) return Results.Conflict(new { message = "A delivery is in progress. Verify its result before recording a manual handoff or match." });
                if (reader.GetBoolean(1) && request.Action != "certinia_match")
                    return Results.Conflict(new { message = "Certinia delivery already succeeded. Match the existing record instead of recording another handoff." });
                await reader.CloseAsync();
                await using var hold = new NpgsqlCommand("""
                    UPDATE external_integration_outbox SET delivery_status='cancelled',next_attempt_at=NULL,
                        last_error='Billing reconciliation: ' || @reason,updated_at=NOW()
                    WHERE local_entity='billing_invoice' AND local_entity_id=@invoice AND system_code='CERTINIA'
                        AND delivery_status IN ('pending','failed');
                    """, connection, transaction);
                hold.Parameters.AddWithValue("invoice", invoiceId); hold.Parameters.AddWithValue("reason", request.Reason.Trim());
                if (request.Action != "resume_delivery") await hold.ExecuteNonQueryAsync();
                else
                {
                    await using var resume = new NpgsqlCommand("""
                        UPDATE external_integration_outbox SET delivery_status='pending',next_attempt_at=NULL,last_error='',updated_at=NOW()
                        WHERE local_entity='billing_invoice' AND local_entity_id=@invoice AND system_code='CERTINIA'
                            AND delivery_status='cancelled' AND last_error LIKE 'Billing reconciliation: %';
                        """, connection, transaction);
                    resume.Parameters.AddWithValue("invoice", invoiceId); await resume.ExecuteNonQueryAsync();
                }
            }
            // Retain the current commercial snapshot alongside the original immutable invoice.
            var commercial = request.Action == "sell_verified"
                ? await SellCommercialReadModelModule.LoadProjectCommercialSummaryAsync(connection, projectId, transaction) : null;
            if (request.Action == "sell_verified" && (commercial is null || !IsSellAvailable(commercial)))
                return Results.Conflict(new { message = "SELL has no synchronized quote for this project yet. Retain the fallback evidence and reconcile after it becomes available." });
            await using var audit = new NpgsqlCommand("""
                INSERT INTO billing_invoice_events(billing_invoice_id,event_type,prior_status,new_status,actor_user_id,event_reason,event_json,created_at)
                SELECT @invoice,@event,'','',@actor,@reason,
                    @evidence::jsonb || jsonb_build_object('revision',COALESCE(max((event_json->>'revision')::bigint),0)+1),clock_timestamp()
                FROM billing_invoice_events WHERE billing_invoice_id=@invoice AND event_type LIKE 'billing_recovery_%';
                """, connection, transaction);
            audit.Parameters.AddWithValue("invoice", invoiceId); audit.Parameters.AddWithValue("event", "billing_recovery_" + request.Action);
            audit.Parameters.AddWithValue("actor", userId.Value); audit.Parameters.AddWithValue("reason", request.Reason.Trim());
            audit.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(new { request.OperationId, requestHash = hash,
                request.Reference, request.Action, actor = userId.Value, recordedAt = DateTimeOffset.UtcNow, commercial,
                evidenceType = "human_verified_reference", externalRequestMade = false, paymentConfirmed = false }, ManualJson));
            await audit.ExecuteNonQueryAsync(); await transaction.CommitAsync();
            return Results.Ok(new { status = "reconciliation_recorded", message = request.Action == "resume_delivery" ? "Delivery hold released. Existing queued work may now retry when the connector is available. No new invoice was created." : "Reconciliation recorded. Delivery holds and receipts prevent automatic retries. No invoice was sent, marked paid, or recreated." });
        }
        catch (PostgresException ex) when (ex.SqlState is "40001" or "40P01" or "23505")
        {
            await SafeRollbackAsync(transaction);
            return Results.Conflict(new { message = "Billing changed concurrently. Refresh and verify its delivery history before retrying." });
        }
    }
}
