using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class WorkLifecycleModule
{
    private const string CompletionContract = "project-completion-evidence-v1";
    private const string CompletionEvent = "completion_checklist_recorded";
    private static readonly JsonSerializerOptions CompletionJson = new(JsonSerializerDefaults.Web);

    private static void MapCompletionChecklistEndpoints(WebApplication app)
    {
        app.MapGet("/api/work-lifecycle/projects/{projectId:guid}/completion-checklist",
            (Func<Guid, HttpContext, Task<IResult>>)GetCompletionChecklistAsync);
        app.MapPost("/api/work-lifecycle/projects/{projectId:guid}/completion-checklist/{action}",
            (Func<Guid, string, CompletionWorkflowRequest, HttpContext, Task<IResult>>)SaveCompletionChecklistAsync);
    }

    private static bool CanReadCompletion(WorkRegisterAccess access, WorkLifecycleProject project) =>
        access.ActualUserId != Guid.Empty && access.RoleCodes.Count > 0
        && (access.CanEditAll || project.ProjectManagerUserId == access.ActualUserId
            || access.RoleCodes.Any(BillingRoles.Contains));

    private static bool CanWriteCompletion(string action, WorkRegisterAccess access, WorkLifecycleProject project) =>
        !access.IsViewAs && CanReadCompletion(access, project)
        && (action is "delivery" or "acceptance" or "reopen_delivery"
            ? access.CanEditAll || project.ProjectManagerUserId == access.ActualUserId
            : action is "billed" or "reopen_billing"
                ? access.CanEditAll || access.RoleCodes.Any(BillingRoles.Contains)
                : action == "sent" && access.CanEditAll);

    private static async Task<IResult> GetCompletionChecklistAsync(Guid projectId, HttpContext context)
    {
        await using var connection = await OpenAsync(context.RequestAborted);
        var session = await WorkRegisterAuthorization.GetAccessAsync(connection, context, cancellationToken: context.RequestAborted);
        if (session.ActualUserId == Guid.Empty) return Results.Unauthorized();
        var access = await ResolveReadAccessAsync(connection, context, session, context.RequestAborted);
        var project = await LoadProjectAsync(connection, null, projectId, context.RequestAborted);
        if (project is null || !CanReadCompletion(access, project)) return Results.NotFound();
        var state = await LoadCompletionStateAsync(connection, null, projectId, context.RequestAborted);
        var basis = await LoadCompletionBillingBasisAsync(connection, null, projectId, context.RequestAborted);
        return Results.Ok(CompletionResponse(project, access, state, basis));
    }

    private static object CompletionResponse(WorkLifecycleProject project, WorkRegisterAccess access,
        CompletionWorkflowState state, CompletionBillingBasis basis) => new
    {
        contract = CompletionContract,
        projectId = project.ProjectId,
        state,
        basisFingerprint = basis.Fingerprint,
        pendingTimeCount = basis.PendingTimeCount,
        invoices = basis.Invoices,
        openTransmissionCount = basis.OpenTransmissionCount,
        automatedFinalDelivered = basis.AutomatedFinalDelivered && basis.AllInvoicesDelivered,
        deliveryComplete = state.Delivery is not null,
        customerAcceptanceComplete = CompletionWorkflowPolicy.IsAccepted(state),
        fullyBilled = CompletionWorkflowPolicy.IsFullyBilled(state, basis),
        billingEvidenceStale = state.Billed is not null && !CompletionWorkflowPolicy.IsFullyBilled(state, basis),
        closed = project.IsArchived || project.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || project.Status.Equals("closed", StringComparison.OrdinalIgnoreCase),
        capabilities = CompletionWorkflowPolicy.Actions.ToDictionary(action => action,
            action => !project.IsArchived
                && project.Status.ToLowerInvariant() is not ("completed" or "closed")
                && CanWriteCompletion(action, access, project)),
        manualRecordingOnly = true,
        message = "Recording evidence does not send an invoice, collect payment or close the project. SELL is not required for the manual evidence path."
    };

    private static async Task<IResult> SaveCompletionChecklistAsync(Guid projectId, string action,
        CompletionWorkflowRequest request, HttpContext context)
    {
        if (!CompletionWorkflowPolicy.Actions.Contains(action)) return Results.BadRequest(new { message = "Unsupported checklist action." });
        await using var connection = await OpenAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, context.RequestAborted);
        try
        {
            var access = await WorkRegisterAuthorization.GetAccessAsync(connection, context, transaction, context.RequestAborted);
            if (access.ActualUserId == Guid.Empty) return Results.Unauthorized();
            // Actual identity, not a client role string, is used for every write.
            if (access.IsViewAs) return Results.Forbid();
            var project = await LoadProjectAsync(connection, transaction, projectId, context.RequestAborted, lockRow: true);
            if (project is null || !CanReadCompletion(access, project)) return Results.NotFound();
            if (!CanWriteCompletion(action, access, project)) return Results.Forbid();
            if (project.IsArchived || project.Status.ToLowerInvariant() is "completed" or "closed")
                return CompletionConflict("Reopen the closed or archived project through the governed workflow before changing evidence.");

            var state = await LoadCompletionStateAsync(connection, transaction, projectId, context.RequestAborted);
            var basis = await LoadCompletionBillingBasisAsync(connection, transaction, projectId, context.RequestAborted);
            var requestHash = HashCompletion(JsonSerializer.Serialize(new { action, request }, CompletionJson));
            var replay = await FindCompletionOperationAsync(connection, transaction, projectId, request.OperationId, context.RequestAborted);
            if (replay is not null)
            {
                if (replay.RequestHash != requestHash || replay.ActorId != access.ActualUserId)
                    return CompletionConflict("This operation ID was already used for different evidence.");
                await transaction.CommitAsync(context.RequestAborted);
                return Results.Ok(new { status = "already_recorded", stateChanged = false, message = "This confirmation was already recorded. Refresh to see the current checklist." });
            }
            if (state.Revision != request.ExpectedRevision)
                return CompletionConflict("Someone updated this checklist. Refresh before recording another decision.");
            var validation = CompletionWorkflowPolicy.Validate(action, request, state, basis, DateOnly.FromDateTime(DateTime.UtcNow));
            if (validation is not null) return Results.BadRequest(new { status = "confirmation_required", message = validation, stateChanged = false });

            var receipt = new CompletionEvidenceReceipt(Guid.NewGuid(), request.OccurredOn,
                Clean(request.Reference), Clean(request.Evidence), Clean(request.Party), Clean(request.Notes),
                access.ActualUserId, DateTimeOffset.UtcNow, basis.Fingerprint, Clean(request.Scope),
                action == "acceptance" ? state.Delivery?.ReceiptId : action == "billed" ? state.Sent?.ReceiptId : null,
                action == "sent" ? (request.Scope == "final" ? basis.InvoiceIds : (request.CoveredInvoiceIds ?? []).Distinct().ToArray()) : []);
            if (action == "billed") receipt = receipt with
            {
                Scope = state.Sent?.Scope == "final" && state.Sent.BasisFingerprint == basis.Fingerprint ? "manual" : "connected"
            };
            var next = action switch
            {
                "delivery" => state with { Delivery = receipt with { Scope = "complete" }, Acceptance = null },
                "acceptance" => state with { Acceptance = receipt },
                "sent" => state with { Sent = receipt, Billed = null },
                "billed" => state with { Billed = receipt },
                "reopen_delivery" => state with { Delivery = null, Acceptance = null },
                "reopen_billing" => state with { Sent = null, Billed = null },
                _ => throw new InvalidOperationException("Unsupported completion action.")
            };
            next = next with { Revision = checked(state.Revision + 1) };
            await AppendCompletionStateAsync(connection, transaction, projectId, next, action,
                request.OperationId, requestHash, access.ActualUserId, Clean(request.Reason), context.RequestAborted);

            // Delivery starts closeout immediately, without requiring final billing first.
            // The mirror is a convenience; final closeout checks the receipts again.
            await using (var mirror = new NpgsqlCommand("""
                INSERT INTO work_closeout_records(project_id, closeout_status, delivery_complete,
                    customer_acceptance_complete, billing_complete, prior_project_status,
                    requested_by_user_id, requested_at, reason, billing_disposition)
                VALUES (@project_id, CASE WHEN @delivery THEN 'requested' ELSE 'not_started' END,
                    @delivery, @acceptance, @billed, @prior_status,
                    CASE WHEN @delivery THEN @actor ELSE NULL END,
                    CASE WHEN @delivery THEN NOW() ELSE NULL END, @reason,
                    CASE WHEN @billed THEN 'final_invoice_complete' ELSE '' END)
                ON CONFLICT (project_id) DO UPDATE SET
                    delivery_complete = @delivery, customer_acceptance_complete = @acceptance,
                    billing_complete = @billed,
                    billing_disposition = CASE WHEN @billed THEN 'final_invoice_complete' ELSE work_closeout_records.billing_disposition END,
                    closeout_status = CASE WHEN @delivery THEN 'requested' ELSE 'not_started' END,
                    requested_by_user_id = CASE WHEN @delivery THEN COALESCE(work_closeout_records.requested_by_user_id, @actor)
                        ELSE work_closeout_records.requested_by_user_id END,
                    requested_at = CASE WHEN @delivery THEN COALESCE(work_closeout_records.requested_at, NOW())
                        ELSE work_closeout_records.requested_at END,
                    updated_at = NOW();
                """, connection, transaction))
            {
                mirror.Parameters.AddWithValue("project_id", projectId);
                mirror.Parameters.AddWithValue("delivery", next.Delivery is not null);
                mirror.Parameters.AddWithValue("acceptance", CompletionWorkflowPolicy.IsAccepted(next));
                mirror.Parameters.AddWithValue("billed", CompletionWorkflowPolicy.IsFullyBilled(next, basis));
                mirror.Parameters.AddWithValue("prior_status", project.Status);
                mirror.Parameters.AddWithValue("actor", access.ActualUserId);
                mirror.Parameters.AddWithValue("reason", Clean(request.Reason));
                await mirror.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = "completion_evidence_recorded", stateChanged = true, revision = next.Revision,
                message = action == "delivery" ? "Delivery recorded and closeout started. Billing and acceptance can now be completed separately."
                    : action == "sent" ? "Sent-to-Certinia confirmation recorded. No external transmission was performed; billing is not yet marked complete."
                    : action == "billed" ? "Fully billed confirmation recorded for the current charge evidence. Finish the remaining closeout checks before closing."
                    : "Evidence recorded with its author, date and audit history."
            });
        }
        catch (PostgresException ex) when (ex.SqlState is "40001" or "40P01")
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return CompletionConflict("The project changed concurrently. Refresh and retry; no partial confirmation was saved.");
        }
    }

    private static IResult CompletionConflict(string message) => Results.Conflict(new
        { status = "completion_revision_conflict", message, stateChanged = false });
    private static string HashCompletion(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<CompletionWorkflowState> LoadCompletionStateAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid projectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT event_json::text FROM work_lifecycle_audit_events
            WHERE project_id = @project_id AND process_area = 'closeout'
              AND event_type = @event_type AND event_json->>'contract' = @contract
            ORDER BY (event_json->>'revision')::bigint DESC LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("event_type", CompletionEvent);
        command.Parameters.AddWithValue("contract", CompletionContract);
        var raw = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (raw is null) return new CompletionWorkflowState();
        var envelope = JsonSerializer.Deserialize<CompletionEnvelope>(raw, CompletionJson)
            ?? throw new InvalidOperationException("Completion evidence is unreadable.");
        if (envelope.Revision < 1 || envelope.State is null || envelope.State.Revision != envelope.Revision)
            throw new InvalidOperationException("Completion evidence has an invalid revision.");
        return envelope.State;
    }

    private static async Task<CompletionEnvelope?> FindCompletionOperationAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid projectId, Guid operationId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT event_json::text FROM work_lifecycle_audit_events
            WHERE project_id = @project_id AND process_area = 'closeout' AND event_type = @event_type
              AND event_json->>'contract' = @contract AND event_json->>'operationId' = @operation_id LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("event_type", CompletionEvent);
        command.Parameters.AddWithValue("contract", CompletionContract);
        command.Parameters.AddWithValue("operation_id", operationId.ToString());
        var raw = await command.ExecuteScalarAsync(cancellationToken) as string;
        return raw is null ? null : JsonSerializer.Deserialize<CompletionEnvelope>(raw, CompletionJson);
    }

    private static Task AppendCompletionStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, CompletionWorkflowState state, string action, Guid operationId, string requestHash,
        Guid actorId, string reason, CancellationToken cancellationToken) =>
        InsertAuditAsync(connection, transaction, projectId, "closeout", CompletionEvent,
            (state.Revision - 1).ToString(), state.Revision.ToString(),
            $"Project completion checklist: {action.Replace('_', ' ')}. Evidence only; no external billing transmission.",
            reason, actorId, "project", projectId,
            JsonSerializer.SerializeToElement(new CompletionEnvelope(CompletionContract, state.Revision,
                operationId, requestHash, actorId, state), CompletionJson), cancellationToken);

    private static async Task ResetCompletionAfterReopenAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid projectId, Guid actorId, string reason, CancellationToken cancellationToken)
    {
        var state = await LoadCompletionStateAsync(connection, transaction, projectId, cancellationToken);
        if (state.Revision == 0) return;
        await AppendCompletionStateAsync(connection, transaction, projectId,
            new CompletionWorkflowState(checked(state.Revision + 1)), "project_reopened", Guid.NewGuid(),
            HashCompletion(reason), actorId, reason, cancellationToken);
    }

    private static async Task<CompletionBillingBasis> LoadCompletionBillingBasisAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid projectId, CancellationToken cancellationToken)
    {
        // Canonical JSON ordering makes edits, additions, removals and voids invalidate
        // prior attestations. Neither SELL connectivity nor its rate-selection gate is queried.
        const string sql = """
            SELECT jsonb_build_object(
                'project', (SELECT jsonb_build_object('client', p.client_id, 'code', p.project_code,
                    'name', p.project_name, 'contract', p.contract_type) FROM projects p WHERE p.project_id=@project_id),
                'time', COALESCE((SELECT jsonb_agg(to_jsonb(t) ORDER BY t.time_entry_id) FROM time_entries t WHERE t.project_id=@project_id), '[]'::jsonb),
                'packages', COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY r.work_billing_readiness_review_id)
                    FROM work_billing_readiness_reviews r WHERE r.project_id=@project_id), '[]'::jsonb),
                'profiles', COALESCE((SELECT jsonb_agg(to_jsonb(p)) FROM project_billing_profiles p WHERE p.project_id=@project_id), '[]'::jsonb),
                'purchaseOrders', COALESCE((SELECT jsonb_agg(to_jsonb(p) ORDER BY p.project_purchase_order_id)
                    FROM project_purchase_orders p WHERE p.project_id=@project_id), '[]'::jsonb),
                'expenses', COALESCE((SELECT jsonb_agg(jsonb_build_object('id', u.project_expense_upload_id,
                    'hash', u.source_sha256, 'version', u.version_number, 'amount', u.total_amount,
                    'reimbursable', u.reimbursable_amount, 'treatment', u.billing_treatment)
                    ORDER BY u.project_expense_upload_id) FROM project_expense_uploads u
                    WHERE u.project_id=@project_id AND u.is_current=TRUE AND u.deleted_at IS NULL), '[]'::jsonb),
                'invoices', COALESCE((SELECT jsonb_agg(to_jsonb(i)-'invoice_status'-'updated_at' ORDER BY i.billing_invoice_id)
                    FROM billing_invoices i WHERE i.project_id=@project_id AND lower(COALESCE(i.invoice_status,''))<>'void'), '[]'::jsonb),
                'invoiceLines', COALESCE((SELECT jsonb_agg(to_jsonb(l) ORDER BY l.billing_invoice_line_id)
                    FROM billing_invoice_lines l JOIN billing_invoices i ON i.billing_invoice_id=l.billing_invoice_id
                    WHERE i.project_id=@project_id AND lower(COALESCE(i.invoice_status,''))<>'void'), '[]'::jsonb)
            )::text,
            (SELECT count(*) FROM time_entries t WHERE t.project_id=@project_id AND t.billable=TRUE AND t.hours>0
                AND NOT (COALESCE(t.status,'')=ANY(@approved_statuses))),
            (SELECT count(*) FROM external_integration_outbox o JOIN billing_invoices i ON i.billing_invoice_id=o.local_entity_id
                WHERE i.project_id=@project_id AND o.system_code='CERTINIA' AND o.local_entity='billing_invoice'
                AND o.delivery_status IN ('pending','processing','failed')),
            EXISTS(SELECT 1 FROM billing_invoices i JOIN external_integration_outbox o ON o.local_entity_id=i.billing_invoice_id
                WHERE i.project_id=@project_id AND i.invoice_type='final' AND lower(COALESCE(i.invoice_status,''))<>'void'
                AND o.system_code='CERTINIA' AND o.local_entity='billing_invoice' AND o.delivery_status='succeeded'
                AND COALESCE(o.payload_json->>'certiniaExternalId','')<>''),
            NOT EXISTS(SELECT 1 FROM billing_invoices i WHERE i.project_id=@project_id AND lower(COALESCE(i.invoice_status,''))<>'void'
                AND NOT EXISTS(SELECT 1 FROM external_integration_outbox o WHERE o.local_entity_id=i.billing_invoice_id
                    AND o.system_code='CERTINIA' AND o.local_entity='billing_invoice' AND o.delivery_status='succeeded'
                    AND COALESCE(o.payload_json->>'certiniaExternalId','')<>'')),
            ARRAY(SELECT i.billing_invoice_id FROM billing_invoices i WHERE i.project_id=@project_id
                AND lower(COALESCE(i.invoice_status,''))<>'void' ORDER BY i.billing_invoice_id),
            COALESCE((SELECT jsonb_agg(jsonb_build_object('invoiceId',i.billing_invoice_id,'invoiceNumber',i.invoice_number)
                ORDER BY i.billing_invoice_id) FROM billing_invoices i WHERE i.project_id=@project_id
                AND lower(COALESCE(i.invoice_status,''))<>'void'), '[]'::jsonb)::text;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("approved_statuses", LifecycleApprovedTimeStatuses);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new CompletionBillingBasis(HashCompletion(reader.GetString(0)), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetBoolean(3), reader.GetBoolean(4), reader.GetFieldValue<Guid[]>(5))
            { Invoices = JsonSerializer.Deserialize<CompletionInvoiceReference[]>(reader.GetString(6), CompletionJson) ?? [] };
    }

    // Called inside the existing queue transaction, before its outbox insert.
    // The shared project row lock serializes manual acknowledgement against queueing.
    internal static async Task GuardManualCertiniaDuplicateAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid projectId, Guid invoiceId, CancellationToken cancellationToken)
    {
        await using (var projectLock = new NpgsqlCommand("SELECT project_id FROM projects WHERE project_id=@project_id FOR UPDATE;", connection, transaction))
        {
            projectLock.Parameters.AddWithValue("project_id", projectId);
            await projectLock.ExecuteScalarAsync(cancellationToken);
        }
        var state = await LoadCompletionStateAsync(connection, transaction, projectId, cancellationToken);
        if (state.Sent?.Scope == "final")
            throw new ManualCertiniaDuplicateException("A final manual Certinia handoff is recorded for this project. Do not send the same charges again. Review its evidence in the completion checklist.");
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM work_lifecycle_audit_events
                WHERE project_id=@project_id AND process_area='closeout' AND event_type=@event_type
                  AND event_json->>'contract'=@contract
                  AND (event_json#>'{state,sent,coveredInvoiceIds}') @> @invoice_id::jsonb);
            """, connection, transaction);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue("event_type", CompletionEvent);
        command.Parameters.AddWithValue("contract", CompletionContract);
        command.Parameters.AddWithValue("invoice_id", JsonSerializer.Serialize(new[] { invoiceId }));
        if (Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken)))
            throw new ManualCertiniaDuplicateException("This invoice has a recorded manual Certinia handoff. Its immutable receipt prevents an automatic duplicate send.");
    }

    private sealed record CompletionEnvelope(string Contract, long Revision, Guid OperationId,
        string RequestHash, Guid ActorId, CompletionWorkflowState State);
}

public sealed class ManualCertiniaDuplicateException(string message) : Exception(message);
