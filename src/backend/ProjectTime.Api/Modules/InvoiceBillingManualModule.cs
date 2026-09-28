using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class InvoiceBillingModule
{
    private const string ManualContract = "manual-project-billing-v1";
    private static readonly JsonSerializerOptions ManualJson = new(JsonSerializerDefaults.Web);
    private static void MapManualBillingEndpoints(WebApplication app)
    {
        app.MapGet("/api/billing/projects/{projectId:guid}/manual",
            (Func<Guid, HttpContext, Task<IResult>>)GetManualBillingAsync);
        app.MapPost("/api/billing/projects/{projectId:guid}/manual-invoices",
            (Func<Guid, ManualInvoiceRequest, HttpContext, Task<IResult>>)CreateManualInvoiceAsync);
    }

    private static bool BillingViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true;
    private static string BillingHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static async Task<IResult> GetManualBillingAsync(Guid projectId, HttpContext context)
    {
        var userId = GetSessionUserId(context);
        if (userId is null) return SessionRequired();
        if (BillingViewAs(context)) return Results.Forbid();
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (ValidateConfig(config) is { } error) return error;
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        var access = await LoadAccessContextAsync(connection, userId.Value);
        if (!access.CanViewBilling || !await CanAccessProjectAsync(connection, access, projectId)) return Results.NotFound();
        var basis = await LoadManualBillingBasisAsync(connection, null, projectId);
        return Results.Ok(new { status = "manual_billing_loaded", projectId, canCreate = access.CanCreateInvoices,
            basis, currency = "USD", connectorRequired = false });
    }

    private static async Task<ManualBillingBasis> LoadManualBillingBasisAsync(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, Guid projectId)
    {
        // Snapshot includes prior invoices, project, PO, and completion evidence.
        // A concurrent change invalidates the form instead of silently changing its charge.
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
                'project', (SELECT to_jsonb(p) FROM projects p WHERE project_id=@project),
                'lifecycle', (SELECT to_jsonb(l) FROM work_register_project_lifecycle l WHERE project_id=@project),
                'po', COALESCE((SELECT jsonb_agg(to_jsonb(p) ORDER BY project_purchase_order_id) FROM project_purchase_orders p WHERE project_id=@project),'[]'),
                'invoices', COALESCE((SELECT jsonb_agg(to_jsonb(i) ORDER BY billing_invoice_id) FROM billing_invoices i WHERE project_id=@project),'[]'),
                'completion', COALESCE((SELECT jsonb_agg(to_jsonb(e) ORDER BY work_lifecycle_audit_event_id) FROM work_lifecycle_audit_events e
                    WHERE project_id=@project AND event_type='completion_checklist_recorded'),'[]')
            )::text,
            COALESCE((SELECT sum(total_amount) FROM billing_invoices WHERE project_id=@project AND invoice_status NOT IN ('void','voided')),0),
            COALESCE((SELECT max((immutable_snapshot_json->>'previouslyBilledOutsidePulse')::numeric)
                FROM billing_invoices WHERE project_id=@project AND invoice_status NOT IN ('void','voided')
                AND immutable_snapshot_json->>'contract'=@contract),0),
            EXISTS(SELECT 1 FROM billing_invoices WHERE project_id=@project AND invoice_type='final' AND invoice_status NOT IN ('void','voided')),
            EXISTS(SELECT 1 FROM billing_invoices WHERE project_id=@project AND invoice_status NOT IN ('void','voided') AND immutable_snapshot_json->>'contract'=@contract),
            EXISTS(SELECT 1 FROM external_integration_outbox o JOIN billing_invoices i ON i.billing_invoice_id=o.local_entity_id
                WHERE i.project_id=@project AND o.system_code='CERTINIA' AND o.local_entity='billing_invoice' AND o.delivery_status IN ('pending','processing','failed')),
            EXISTS(SELECT 1 FROM work_register_project_lifecycle WHERE project_id=@project AND is_archived=TRUE)
            OR EXISTS(SELECT 1 FROM projects WHERE project_id=@project AND lower(status) IN ('completed','closed','cancelled','canceled'));
            """, connection, transaction);
        command.Parameters.AddWithValue("project", projectId);
        command.Parameters.AddWithValue("contract", ManualContract);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new ManualBillingBasis(BillingHash(reader.GetString(0)), reader.GetDecimal(1), reader.GetDecimal(2),
            reader.GetBoolean(3), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6));
    }

    private static async Task<IResult> CreateManualInvoiceAsync(Guid projectId, ManualInvoiceRequest request, HttpContext context)
    {
        var userId = GetSessionUserId(context);
        if (userId is null) return SessionRequired();
        if (BillingViewAs(context)) return Results.Forbid();
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (ValidateConfig(config) is { } error) return error;
        await using var connection = new NpgsqlConnection(config.ConnectionString);
        await connection.OpenAsync(context.RequestAborted);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, context.RequestAborted);
        try
        {
            var access = await LoadAccessContextAsync(connection, userId.Value, transaction);
            if (!access.CanCreateInvoices || !await CanAccessProjectAsync(connection, access, projectId, transaction)) return Results.Forbid();
            // Same project-first lock order as standard invoices and manual/connected delivery.
            var project = await LoadProjectForInvoiceAsync(connection, transaction, projectId);
            if (project is null) return Results.NotFound();
            var requestHash = BillingHash(JsonSerializer.Serialize(new { projectId, actor = userId, request }, ManualJson));
            await using (var replay = new NpgsqlCommand("""
                SELECT billing_invoice_id, immutable_snapshot_json->>'requestHash'
                FROM billing_invoices WHERE project_id=@project AND immutable_snapshot_json->>'contract'=@contract
                    AND immutable_snapshot_json->>'operationId'=@operation;
                """, connection, transaction))
            {
                replay.Parameters.AddWithValue("project", projectId);
                replay.Parameters.AddWithValue("contract", ManualContract);
                replay.Parameters.AddWithValue("operation", request.OperationId.ToString());
                await using var reader = await replay.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var id = reader.GetGuid(0); var same = reader.GetString(1) == requestHash;
                    await reader.CloseAsync();
                    if (!same) return Results.Conflict(new { message = "This operation ID already belongs to a different invoice request." });
                    await transaction.CommitAsync();
                    return Results.Ok(new { status = "already_recorded", invoice = await LoadInvoiceDetailAsync(connection, id) });
                }
            }
            var basis = await LoadManualBillingBasisAsync(connection, transaction, projectId);
            if (basis.Closed) return Results.Conflict(new { message = "Reopen the closed or archived project before creating another invoice." });
            if (basis.OpenTransmission) return Results.Conflict(new { message = "Resolve queued or retryable Certinia deliveries before reconciling manual billing." });
            if (request.ExpectedFingerprint != basis.Fingerprint) return Results.Conflict(new { message = "The billing balance changed. Reload and review prior invoices before continuing." });
            var validation = ManualBillingPolicy.Validate(request, basis.PulseInvoiced, basis.PreviouslyBilledOutsidePulse,
                basis.FinalInvoiceExists, DateOnly.FromDateTime(DateTime.UtcNow));
            if (validation is not null) return Results.BadRequest(new { message = validation });
            var blockers = BuildProjectStructuralBlockers(project);
            if (blockers.Count > 0) return Results.Conflict(new { message = "Complete the project billing details before invoicing.", blockers });
            if (project.PurchaseOrder?.AuthorizedAmount is decimal limit && request.BillToDate > limit)
                return Results.Conflict(new { message = "Cumulative billing exceeds the active purchase order amount. Obtain an updated authorization first." });
            var amount = request.BillToDate - basis.PulseInvoiced - request.PreviouslyBilledOutsidePulse;
            var invoiceId = Guid.NewGuid();
            var identity = await AllocateInvoiceIdentityAsync(connection, transaction, projectId, userId.Value);
            var snapshot = JsonSerializer.Serialize(new {
                contract = ManualContract, operationId = request.OperationId, requestHash,
                previouslyBilledOutsidePulse = request.PreviouslyBilledOutsidePulse,
                priorPulseInvoiced = basis.PulseInvoiced, request, newCharge = amount, actor = userId.Value
            }, ManualJson);
            var notes = $"Manual amount invoice. Agreed total: USD {request.AgreedTotal:0.00}; cumulative billed: USD {request.BillToDate:0.00}; prior Pulse invoices: USD {basis.PulseInvoiced:0.00}; prior external billing: USD {request.PreviouslyBilledOutsidePulse:0.00}. This invoice charges only USD {amount:0.00}. Authorization: {Clean(request.AuthorizationReference)}.";
            await InsertInvoiceHeaderAsync(connection, transaction, invoiceId, identity, project, request.InvoiceType,
                request.PeriodStart, request.PeriodEnd, amount, notes, userId.Value, snapshot);
            await using (var line = new NpgsqlCommand("""
                INSERT INTO billing_invoice_lines(billing_invoice_id,line_number,source_type,work_date,
                    customer_facing_description,internal_description,labor_category,approved_hours,
                    rate_code_snapshot,rate_description_snapshot,unit_rate,line_amount,accounting_readiness_snapshot,source_snapshot_json)
                VALUES(@invoice,1,'other',@date,@description,@reason,'manual_amount',0,
                    'MANUAL','Authorized project amount',0,@amount,'manual_authorization',@snapshot::jsonb);
                """, connection, transaction))
            {
                line.Parameters.AddWithValue("invoice", invoiceId); line.Parameters.AddWithValue("date", request.PeriodEnd);
                line.Parameters.AddWithValue("description", Clean(request.Description)); line.Parameters.AddWithValue("reason", Clean(request.Reason));
                line.Parameters.AddWithValue("amount", amount); line.Parameters.AddWithValue("snapshot", snapshot);
                await line.ExecuteNonQueryAsync();
            }
            await using (var audit = new NpgsqlCommand("""
                INSERT INTO billing_invoice_events(billing_invoice_id,event_type,prior_status,new_status,actor_user_id,event_reason,event_json)
                VALUES(@invoice,'invoice_created','','finalized',@actor,@reason,@snapshot::jsonb);
                """, connection, transaction))
            {
                audit.Parameters.AddWithValue("invoice", invoiceId); audit.Parameters.AddWithValue("actor", userId.Value);
                audit.Parameters.AddWithValue("reason", "Manual amount invoice: " + Clean(request.Reason));
                audit.Parameters.AddWithValue("snapshot", snapshot); await audit.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
            return Results.Created($"/api/billing/invoices/{invoiceId}", new { status = "billing_invoice_created",
                invoice = await LoadInvoiceDetailAsync(connection, invoiceId) });
        }
        catch (PostgresException ex) when (ex.SqlState is "23505" or "40001" or "40P01")
        {
            await SafeRollbackAsync(transaction);
            return Results.Conflict(new { message = "Another billing transaction changed this project. Reload its balance before continuing." });
        }
    }
    private sealed record ManualBillingBasis(string Fingerprint, decimal PulseInvoiced,
        decimal PreviouslyBilledOutsidePulse, bool FinalInvoiceExists, bool ManualInvoicesExist, bool OpenTransmission, bool Closed);
}
