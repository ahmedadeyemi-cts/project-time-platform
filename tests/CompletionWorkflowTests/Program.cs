using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Modules;

// Calls actual API handlers/auth queries and actual migration-038 audit triggers.
// Fixture identities and charges are synthetic. There is no HTTP server or external send.
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "database/migrations/001_initial_schema.sql")))
    root = Directory.GetParent(root)?.FullName ?? throw new Exception("Repository root not found");
var settings = new NpgsqlConnectionStringBuilder {
    Host = "127.0.0.1", Port = int.Parse(Environment.GetEnvironmentVariable("PGPORT") ?? "5432"),
    Username = "postgres", Database = "postgres", Pooling = false,
    Password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? throw new Exception("Disposable PGPASSWORD required")
};
await using var admin = new NpgsqlConnection(settings.ConnectionString);
await admin.OpenAsync();
var db = "completion_" + Guid.NewGuid().ToString("N");
await Sql(admin, $"CREATE DATABASE {db}");
settings.Database = db;
Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", settings.ConnectionString);
var passed = 0;
var pm = Guid.NewGuid(); var ptc = Guid.NewGuid(); var billing = Guid.NewGuid(); var other = Guid.NewGuid(); var inactive = Guid.NewGuid();
try {
    await using var c = await Open();
    await Sql(c, await File.ReadAllTextAsync(Path.Combine(root, "database/migrations/001_initial_schema.sql")));
    await Sql(c, await File.ReadAllTextAsync(Path.Combine(root, "tests/CompletionWorkflowTests/fixture.sql")));
    await Sql(c, await File.ReadAllTextAsync(Path.Combine(root, "database/migrations/038_work_to_cash_lifecycle_and_audit.sql")));
    foreach (var (id, role, active) in new[] { (pm,"PROJECT_MANAGER",true), (ptc,"PROJECT_TEAM_COORDINATOR",true), (billing,"BILLING",true), (other,"PROJECT_MANAGER",true), (inactive,"PROJECT_TEAM_COORDINATOR",false) }) {
        var roleId = Guid.NewGuid();
        await Sql(c, $"INSERT INTO app_users(user_id,email,display_name,is_active) VALUES('{id}','{id}@example.invalid','Synthetic {role}',{active.ToString().ToLowerInvariant()}); INSERT INTO app_roles VALUES('{roleId}','{role}',true); INSERT INTO app_user_role_assignments VALUES('{id}','{roleId}',true);");
    }
    var project = await Project(c);
    Check(Status(await Get(project, Guid.Empty)) == 401, "anonymous denied");
    Check(Status(await Get(project, other)) == 404, "unassigned PM cannot read evidence");
    Check(Status(await Get(project, inactive)) == 404, "inactive PTC cannot read evidence");
    var initial = Value(await Get(project, pm));
    Check(!initial.GetProperty("fullyBilled").GetBoolean(), "empty source does not claim fully billed");
    var request = Request(initial);
    Check(Status(await Save(project, "sent", request, pm)) == 403, "assigned PM cannot attest PTC handoff");
    Check(Status(await Save(project, "delivery", request, billing)) == 403, "Billing cannot mark PM delivery");
    Check(Status(await Save(project, "delivery", request, ptc, true)) == 403, "View-As cannot write");
    Check(Status(await Save(project, "delivery", request, inactive)) == 404, "inactive write denied");
    Check(Status(await Save(project, "delivery", request with { Confirmed = false }, pm)) == 400, "unchecked attestation denied");
    Check(Status(await Save(project, "delivery", request with { OccurredOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2) }, pm)) == 400, "future completion date denied");
    Check(await Count(c, project) == 0, "rejected actions create no receipts");
    Check(Status(await Save(project, "delivery", request, pm)) == 200, "delivery starts without SELL, rates, or final invoice");
    Check(await Text(c, $"SELECT closeout_status FROM work_closeout_records WHERE project_id='{project}'") == "requested", "delivery starts closeout");
    Check(await Text(c, $"SELECT status FROM projects WHERE project_id='{project}'") == "active", "delivery does not prematurely close project");
    Check(Status(await Save(project, "delivery", request, pm)) == 200 && await Count(c, project) == 1, "identical retry is idempotent");
    Check(Status(await Save(project, "delivery", request with { Reference = "changed request" }, pm)) == 409, "same operation ID cannot change payload");
    Check(Status(await Save(project, "delivery", request with { OperationId = Guid.NewGuid() }, pm)) == 409, "stale revision denied");
    var decision = Request(Value(await Get(project, pm)), "conditional") with { Party = "Customer reviewer", Notes = "One sign-off condition outstanding" };
    Check(Status(await Save(project,"acceptance",decision,pm)) == 200, "conditional decision retained");
    Check(!Value(await Get(project,pm)).GetProperty("customerAcceptanceComplete").GetBoolean(), "conditions are not acceptance complete");
    Check(Status(await Save(project,"acceptance",Request(Value(await Get(project,pm)),"accepted") with {Party="Customer reviewer"},pm)) == 200, "customer acceptance recorded with identity and evidence");
    var noClose = new CloseoutSaveRequest("final_invoice_complete",true,true,true,true,"Synthetic closeout test","");
    Check(Status(await Close(project,noClose,ptc,"complete")) == 409, "checkboxes alone cannot complete billing");
    Check(Status(await Close(project,new CloseoutSaveRequest("",false,false,false,false,"Saving incomplete progress",""),pm,"request")) == 200, "PM may save progress without resolving billing");
    Check(Status(await Save(project,"sent",Request(Value(await Get(project,ptc)),"partial"),ptc)) == 200, "partial manual handoff recorded");
    Check(Status(await Save(project,"billed",Request(Value(await Get(project,ptc))),ptc)) == 400, "partial handoff cannot become fully billed");
    Check(Status(await Save(project,"sent",Request(Value(await Get(project,ptc)),"final"),ptc)) == 200, "final handoff recorded separately");
    Check(!Value(await Get(project,ptc)).GetProperty("fullyBilled").GetBoolean(), "sent is not fully billed");
    Check(Status(await Save(project,"billed",Request(Value(await Get(project,billing))),billing)) == 200, "Billing records final processing with reference");
    Check(Value(await Get(project,ptc)).GetProperty("fullyBilled").GetBoolean(), "manual final processing satisfies current evidence");
    Check(await Text(c,"SELECT count(*)::text FROM billing_invoices") == "0" && await Text(c,"SELECT count(*)::text FROM external_integration_outbox") == "0", "manual path fabricates neither invoices nor transmissions");
    var timeId = await Time(c,project,"submitted");
    Check(Value(await Get(project,ptc)).GetProperty("billingEvidenceStale").GetBoolean(), "new charges invalidate prior confirmation");
    Check(Status(await Close(project,noClose,ptc,"complete")) == 409, "stale evidence blocks actual closeout");
    Check(Status(await Save(project,"sent",Request(Value(await Get(project,ptc)),"final"),ptc)) == 200, "new charge basis can be reconciled");
    Check(Status(await Save(project,"billed",Request(Value(await Get(project,ptc))),ptc)) == 400, "pending time prevents fully billed");
    await Sql(c,$"UPDATE time_entries SET status='pm_approved' WHERE time_entry_id='{timeId}'");
    Check(Status(await Save(project,"sent",Request(Value(await Get(project,ptc)),"final"),ptc)) == 200, "approved charge basis receives new final receipt");
    Check(Status(await Save(project,"billed",Request(Value(await Get(project,ptc))),ptc)) == 200, "PTC final reconciliation covers approved externally billed time");
    Check(Status(await Close(project,noClose,ptc,"complete")) == 200, "fully evidenced manual billing can close without a fabricated local invoice or SELL");
    Check(await Text(c,$"SELECT status FROM projects WHERE project_id='{project}'") == "completed", "actual project closes");
    Check(Status(await Save(project,"delivery",Request(Value(await Get(project,ptc))),pm)) == 409, "closed project is immutable");
    Check(Status((IResult)(await Invoke("ReopenProjectAsync",project,new CloseoutReopenRequest("Customer requested a new delivery phase"),Context(ptc)))!) == 200, "governed reopen succeeds");
    var reopened = Value(await Get(project,ptc));
    Check(!reopened.GetProperty("deliveryComplete").GetBoolean() && !reopened.GetProperty("fullyBilled").GetBoolean(), "reopen invalidates receipts without deleting history");
    try { await Sql(c,$"UPDATE work_lifecycle_audit_events SET reason='tamper' WHERE project_id='{project}'"); throw new Exception("Audit mutation accepted"); }
    catch(PostgresException) { Check(true,"real migration-038 trigger prevents audit edits"); }

    var queued = await Project(c); var invoice = await Invoice(c,queued,"final");
    await Sql(c,$"INSERT INTO external_integration_outbox VALUES(gen_random_uuid(),'CERTINIA','billing_invoice','{invoice}','pending','{{}}')");
    Check(Status(await Save(queued,"sent",Request(Value(await Get(queued,ptc)),"final"),ptc)) == 400, "queued automatic send prevents manual duplication");
    await Sql(c,$"UPDATE external_integration_outbox SET delivery_status='succeeded',payload_json='{{\"certiniaExternalId\":\"SYNTHETIC-EXTERNAL\"}}' WHERE local_entity_id='{invoice}'");
    Check(Value(await Get(queued,ptc)).GetProperty("automatedFinalDelivered").GetBoolean(), "verified connected receipt shown distinctly");
    Check(!Value(await Get(queued,ptc)).GetProperty("fullyBilled").GetBoolean(), "successful automatic send is still not fully billed");
    Check(Status(await Save(queued,"billed",Request(Value(await Get(queued,billing))),billing)) == 200, "Billing may confirm processed connected invoice");
    await Invoice(c,queued,"partial");
    Check(!Value(await Get(queued,ptc)).GetProperty("fullyBilled").GetBoolean(), "additional unsent invoice invalidates connected completion");

    var partial = await Project(c); var firstInvoice = await Invoice(c,partial,"partial"); var secondInvoice = await Invoice(c,partial,"partial");
    var partialRequest = Request(Value(await Get(partial,ptc)),"partial") with {CoveredInvoiceIds=[firstInvoice]};
    Check(Status(await Save(partial,"sent",partialRequest with {CoveredInvoiceIds=[invoice]},ptc)) == 400, "cross-project invoice association denied");
    Check(Status(await Save(partial,"sent",partialRequest,ptc)) == 200, "partial handoff identifies only included invoices");
    await using(var tx=await c.BeginTransactionAsync()) {
        try { await Invoke("GuardManualCertiniaDuplicateAsync",c,tx,partial,firstInvoice,CancellationToken.None); throw new Exception("Duplicate allowed"); }
        catch(ManualCertiniaDuplicateException) { Check(true,"covered invoice duplicate guard blocks queueing"); }
        await Invoke("GuardManualCertiniaDuplicateAsync",c,tx,partial,secondInvoice,CancellationToken.None);
        Check(true,"unrelated partial invoice remains eligible for normal queue checks"); await tx.RollbackAsync();
    }
    Check(Status(await Save(partial,"reopen_billing",Request(Value(await Get(partial,ptc))),ptc)) == 200,"billing corrections append a new state");
    await using(var tx=await c.BeginTransactionAsync()) {
        try { await Invoke("GuardManualCertiniaDuplicateAsync",c,tx,partial,firstInvoice,CancellationToken.None); throw new Exception("Historical duplicate allowed"); }
        catch(ManualCertiniaDuplicateException) { Check(true,"correction cannot erase historic invoice duplicate protection"); }
        await tx.RollbackAsync();
    }
    var racing = await Project(c); var sameRevision = Request(Value(await Get(racing,pm)));
    var race = await Task.WhenAll(Save(racing,"delivery",sameRevision,pm),Save(racing,"delivery",sameRevision with {OperationId=Guid.NewGuid()},pm));
    Check(race.Select(Status).Order().SequenceEqual(new[]{200,409}) && await Count(c,racing)==1, "concurrent attestations save one revision, not two");
    passed += await CertiniaQueueChecks.RunAsync(c, pm, ptc, billing);
    Console.WriteLine($"COMPLETION_DATABASE_TESTS=PASS assertions={passed}");
} finally {
    Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",null);
    NpgsqlConnection.ClearAllPools();
    await Sql(admin,$"DROP DATABASE {db} WITH (FORCE)");
}

void Check(bool condition,string name) { if(!condition) throw new Exception(name); passed++; Console.WriteLine($"PASS {name}"); }
DefaultHttpContext Context(Guid id,bool viewAs=false) { var x=new DefaultHttpContext(); if(id!=Guid.Empty)x.Items["ProjectPulseActualUserId"]=id; x.Items["ProjectPulseIsViewAs"]=viewAs; return x; }
int Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode ?? (result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? 403 : 200);
JsonElement Value(IResult result) => JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
CompletionWorkflowRequest Request(JsonElement data,string scope="") => new(data.GetProperty("state").GetProperty("revision").GetInt64(),data.GetProperty("basisFingerprint").GetString(),Guid.NewGuid(),true,DateOnly.FromDateTime(DateTime.UtcNow),"SYNTHETIC-REF","Synthetic evidence document", "", "",scope,"Synthetic reviewed confirmation");
async Task<object?> Invoke(string name, params object?[] args) { try { var task=(Task)typeof(WorkLifecycleModule).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!; await task; return task.GetType().GetProperty("Result")?.GetValue(task); } catch(TargetInvocationException e) when(e.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; } }
async Task<IResult> Get(Guid p,Guid u) => (IResult)(await Invoke("GetCompletionChecklistAsync",p,Context(u)))!;
async Task<IResult> Save(Guid p,string a,CompletionWorkflowRequest q,Guid u,bool preview=false) => (IResult)(await Invoke("SaveCompletionChecklistAsync",p,a,q,Context(u,preview)))!;
async Task<IResult> Close(Guid p,CloseoutSaveRequest q,Guid u,string op) => (IResult)(await Invoke("SaveCloseoutAsync",p,q,Context(u),op))!;
async Task<NpgsqlConnection> Open() { var c=new NpgsqlConnection(settings.ConnectionString);await c.OpenAsync();return c; }
async Task Sql(NpgsqlConnection c,string sql) { await using var cmd=new NpgsqlCommand(sql,c);await cmd.ExecuteNonQueryAsync(); }
async Task<string> Text(NpgsqlConnection c,string sql) { await using var cmd=new NpgsqlCommand(sql,c);return Convert.ToString(await cmd.ExecuteScalarAsync())!; }
async Task<long> Count(NpgsqlConnection c,Guid p) => long.Parse(await Text(c,$"SELECT count(*)::text FROM work_lifecycle_audit_events WHERE project_id='{p}' AND event_type='completion_checklist_recorded'"));
async Task<Guid> Project(NpgsqlConnection c) { var id=Guid.NewGuid();await Sql(c,$"INSERT INTO projects(project_id,project_code,project_name,project_manager_user_id) VALUES('{id}','{id}','Synthetic project','{pm}')");return id; }
async Task<Guid> Invoice(NpgsqlConnection c,Guid p,string type) { var id=Guid.NewGuid();await Sql(c,$"INSERT INTO billing_invoices(billing_invoice_id,project_id,invoice_number,invoice_type,invoice_status) VALUES('{id}','{p}','SYNTHETIC-{id}','{type}','finalized')");return id; }
async Task<Guid> Time(NpgsqlConnection c,Guid p,string status) { var id=Guid.NewGuid();var sheet=Guid.NewGuid();await Sql(c,$"INSERT INTO timesheets(timesheet_id,user_id,week_start_date,week_end_date) VALUES('{sheet}','{pm}',current_date,current_date+6) ON CONFLICT DO NOTHING; INSERT INTO time_entries(time_entry_id,timesheet_id,user_id,project_id,work_date,hours,status) SELECT '{id}',timesheet_id,'{pm}','{p}',current_date,2,'{status}' FROM timesheets WHERE user_id='{pm}' LIMIT 1;");return id; }
