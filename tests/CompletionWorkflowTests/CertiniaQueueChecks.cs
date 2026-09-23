using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Modules;

internal static class CertiniaQueueChecks
{
    // Exercise the real queue and real worker claim, never an external HTTP transport.
    public static async Task<int> RunAsync(NpgsqlConnection connection, Guid pm, Guid ptc, Guid billing)
    {
        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        await Sql(connection, """
            ALTER TABLE external_integration_outbox RENAME COLUMN outbox_id TO external_integration_outbox_id;
            ALTER TABLE external_integration_outbox ALTER COLUMN external_integration_outbox_id SET DEFAULT gen_random_uuid();
            ALTER TABLE external_integration_outbox
                ADD COLUMN idempotency_key text UNIQUE,
                ADD COLUMN operation_type text NOT NULL DEFAULT 'create',
                ADD COLUMN attempt_count integer NOT NULL DEFAULT 0,
                ADD COLUMN next_attempt_at timestamptz,
                ADD COLUMN last_attempt_at timestamptz,
                ADD COLUMN completed_at timestamptz,
                ADD COLUMN last_error text NOT NULL DEFAULT '',
                ADD COLUMN created_at timestamptz NOT NULL DEFAULT now(),
                ADD COLUMN updated_at timestamptz NOT NULL DEFAULT now();
            ALTER TABLE billing_invoice_events ALTER COLUMN billing_invoice_event_id SET DEFAULT gen_random_uuid();
            ALTER TABLE billing_invoice_events ALTER COLUMN created_at SET DEFAULT now();
            """);

        var project = await CreateProject(connection, pm);
        var invoice = await CreateInvoice(connection, project);
        var snapshot = Snapshot(invoice, project);
        var options = JsonSerializer.Deserialize("{\"includeEngineerNames\":false,\"includeProjectManagerName\":false,\"includeProjectCoordinatorName\":false}", Nested("InvoiceOutputOptions"), Json)!;
        async Task<object?> Queue(Guid actor, string format = "pdf") =>
            await Invoke(typeof(CertiniaBillingModule), "QueueInvoiceAsync", connection, snapshot, format, options, actor);

        foreach (var actor in new[] { pm, billing })
        {
            try { await Queue(actor); throw new InvalidOperationException("Unauthorized queue accepted"); }
            catch (UnauthorizedAccessException) { Check(true, "Actual queue requires active PTC authorization"); }
        }
        Check(await Count(connection, invoice) == 0, "Rejected actual queue writes no outbox record");
        var request = new CertiniaBillingModule.CertiniaInvoiceSendRequest("pdf", false, false, false, false, false);
        foreach (var (actor, preview) in new[] { (pm, false), (billing, false), (ptc, true) })
        {
            var result = (IResult)(await Invoke(typeof(CertiniaBillingModule), "QueueOrSendAsync", invoice, request, Context(actor, preview)))!;
            Check((result as IStatusCodeHttpResult)?.StatusCode == 403, "Public send handler rejects unauthorized and View-As identities before transmission");
        }

        var queued = JsonSerializer.SerializeToElement(await Queue(ptc), Json);
        Check(queued.GetProperty("inserted").GetBoolean(), "Actual PTC queue creates one immutable invoice artifact");
        var outboxId = queued.GetProperty("outboxId").GetGuid();
        var replay = JsonSerializer.SerializeToElement(await Queue(ptc, "excel"), Json);
        Check(!replay.GetProperty("inserted").GetBoolean() && replay.GetProperty("outboxId").GetGuid() == outboxId
            && await Count(connection, invoice) == 1, "Changing artifact format reuses the logical invoice instead of duplicating billing");
        Check(await Text(connection, $"SELECT count(*)::text FROM billing_invoice_events WHERE billing_invoice_id='{invoice}' AND event_type='certinia_queued'") == "1", "Actual invoice queue audit is persisted");
        Check(await ManualSent(connection, project, ptc, "partial", [invoice]) == 400,
            "Actual pending queue blocks manual handoff confirmation");

        var claim = await Invoke(typeof(CertiniaBillingModule), "ClaimOutboxAsync", connection, outboxId);
        Check(claim is not null, "Actual worker safely claims the queued item");
        Check(await Text(connection, $"SELECT delivery_status FROM external_integration_outbox WHERE external_integration_outbox_id='{outboxId}'") == "processing",
            "Worker marks processing before returning any payload for transport");
        Check(await Invoke(typeof(CertiniaBillingModule), "ClaimOutboxAsync", connection, outboxId) is null,
            "The same processing item cannot be claimed again");
        Check(await ManualSent(connection, project, ptc, "final", [invoice]) == 400,
            "Processing queue blocks manual handoff while transport could be in flight");

        var manualProject = await CreateProject(connection, pm);
        var manualInvoice = await CreateInvoice(connection, manualProject);
        Check(await ManualSent(connection, manualProject, ptc, "partial", [manualInvoice]) == 200,
            "Partial manual handoff records actual mapped invoice evidence");
        var manualSnapshot = Snapshot(manualInvoice, manualProject);
        try
        {
            await Invoke(typeof(CertiniaBillingModule), "QueueInvoiceAsync", connection, manualSnapshot, "pdf", options, ptc);
            throw new InvalidOperationException("Manual invoice queued twice");
        }
        catch (ManualCertiniaDuplicateException) { Check(true, "Actual queue rejects a recorded manual invoice before outbox insertion"); }
        Check(await Count(connection, manualInvoice) == 0, "Manual evidence did not fabricate or queue an invoice");

        // Represents a legacy pending row predating the safeguard, not a new user write.
        var legacyOutbox = Guid.NewGuid();
        await Sql(connection, $"INSERT INTO external_integration_outbox(external_integration_outbox_id,system_code,local_entity,local_entity_id,delivery_status) VALUES('{legacyOutbox}','CERTINIA','billing_invoice','{manualInvoice}','pending');");
        Check(await Invoke(typeof(CertiniaBillingModule), "ClaimOutboxAsync", connection, legacyOutbox) is null,
            "Real retry path refuses legacy pending delivery covered by manual evidence");
        Check(await Text(connection, $"SELECT delivery_status || ':' || attempt_count FROM external_integration_outbox WHERE external_integration_outbox_id='{legacyOutbox}'") == "dead_letter:0",
            "Blocked legacy delivery is retained without a transport attempt or fabricated success");
        Check(await Text(connection, $"SELECT count(*)::text FROM billing_invoice_events WHERE billing_invoice_id='{manualInvoice}' AND event_type='certinia_manual_handoff_blocked'") == "1",
            "Blocked real worker claim records invoice audit evidence");

        var finalProject = await CreateProject(connection, pm);
        var finalInvoice = await CreateInvoice(connection, finalProject);
        Check(await ManualSent(connection, finalProject, ptc, "final", [finalInvoice]) == 200,
            "Final external handoff is distinct from billing completion");
        try
        {
            await Invoke(typeof(CertiniaBillingModule), "QueueInvoiceAsync", connection, Snapshot(finalInvoice, finalProject), "pdf", options, ptc);
            throw new InvalidOperationException("Final manual package queued twice");
        }
        catch (ManualCertiniaDuplicateException) { Check(true, "Final manual handoff prevents actual automatic queueing"); }

        var voidProject = await CreateProject(connection, pm);
        var voidInvoice = await CreateInvoice(connection, voidProject);
        await Sql(connection, $"UPDATE billing_invoices SET invoice_status='void' WHERE billing_invoice_id='{voidInvoice}'");
        try
        {
            await Invoke(typeof(CertiniaBillingModule), "QueueInvoiceAsync", connection, Snapshot(voidInvoice, voidProject), "pdf", options, ptc);
            throw new InvalidOperationException("Voided invoice queued");
        }
        catch (ManualCertiniaDuplicateException) { Check(true, "Actual queue revalidates voided invoices inside its transaction"); }

        Check(await Text(connection, "SELECT count(*)::text FROM external_integration_outbox WHERE payload_json->>'certiniaExternalId' IS NOT NULL AND payload_json->>'certiniaExternalId'<>'SYNTHETIC-EXTERNAL'") == "0",
            "Tests perform no external transmission and fabricate no external result");
        Console.WriteLine($"CERTINIA_QUEUE_BOUNDARY_TESTS=PASS assertions={passed}");
        return passed;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static Type Nested(string name) => typeof(CertiniaBillingModule).GetNestedType(name, BindingFlags.NonPublic)!;
    private static object Snapshot(Guid invoice, Guid project) => JsonSerializer.Deserialize(JsonSerializer.Serialize(new
    {
        header = new
        {
            billingInvoiceId = invoice, invoiceNumber = "SYNTHETIC-TEST", projectId = project,
            invoiceType = "final", invoiceStatus = "finalized", billingPeriodStart = "2026-09-01",
            billingPeriodEnd = "2026-09-22", invoiceDate = "2026-09-22", customerName = "Synthetic customer",
            projectCode = "SYNTHETIC", projectName = "Synthetic project", contractType = "Time and Materials",
            projectManagerName = "Synthetic PM", projectCoordinatorName = "Synthetic PTC", purchaseOrderNumber = "",
            certiniaId = "", salesforceId = "", sellQuote = "", subtotalAmount = 100m, adjustmentAmount = 0m,
            taxAmount = 0m, totalAmount = 100m, notes = "Synthetic offline regression only",
            immutableSnapshotJson = "{}", createdAt = DateTimeOffset.UtcNow, finalizedAt = DateTimeOffset.UtcNow
        },
        lines = Array.Empty<object>(), immutableSnapshotSha256 = new string('a', 64)
    }, Json), Nested("CertiniaInvoiceSnapshot"), Json)!;
    private static DefaultHttpContext Context(Guid actor, bool preview = false)
    {
        var context = new DefaultHttpContext();
        context.Items["ProjectPulseActualUserId"] = actor;
        context.Items["ProjectPulseSessionUserId"] = actor;
        context.Items["ProjectPulseIsViewAs"] = preview;
        return context;
    }
    private static async Task<int> ManualSent(NpgsqlConnection connection, Guid project, Guid actor, string scope, Guid[] invoices)
    {
        var result = (IResult)(await Invoke(typeof(WorkLifecycleModule), "GetCompletionChecklistAsync", project, Context(actor)))!;
        var data = JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, Json);
        var request = new CompletionWorkflowRequest(data.GetProperty("state").GetProperty("revision").GetInt64(),
            data.GetProperty("basisFingerprint").GetString(), Guid.NewGuid(), true, DateOnly.FromDateTime(DateTime.UtcNow),
            "SYNTHETIC-HANDOFF", "Synthetic document evidence", "", "", scope, "Synthetic PTC reviewed handoff", invoices);
        result = (IResult)(await Invoke(typeof(WorkLifecycleModule), "SaveCompletionChecklistAsync", project, "sent", request, Context(actor)))!;
        return (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
    }
    private static async Task<object?> Invoke(Type type, string name, params object?[] args)
    {
        try
        {
            var task = (Task)type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
    private static async Task Sql(NpgsqlConnection connection, string sql) { await using var c = new NpgsqlCommand(sql, connection); await c.ExecuteNonQueryAsync(); }
    private static async Task<string> Text(NpgsqlConnection connection, string sql) { await using var c = new NpgsqlCommand(sql, connection); return Convert.ToString(await c.ExecuteScalarAsync())!; }
    private static async Task<long> Count(NpgsqlConnection connection, Guid invoice) => long.Parse(await Text(connection, $"SELECT count(*)::text FROM external_integration_outbox WHERE local_entity_id='{invoice}'"));
    private static async Task<Guid> CreateProject(NpgsqlConnection connection, Guid pm)
    {
        var id = Guid.NewGuid();
        await Sql(connection, $"INSERT INTO projects(project_id,project_code,project_name,project_manager_user_id) VALUES('{id}','{id}','Synthetic queue project','{pm}')");
        return id;
    }
    private static async Task<Guid> CreateInvoice(NpgsqlConnection connection, Guid project)
    {
        var id = Guid.NewGuid();
        await Sql(connection, $"INSERT INTO billing_invoices(billing_invoice_id,project_id,invoice_number,invoice_type,invoice_status) VALUES('{id}','{project}','SYNTHETIC-{id}','final','finalized')");
        return id;
    }
}
