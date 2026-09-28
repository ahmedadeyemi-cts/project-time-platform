using System.Reflection;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Modules;

var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root,"database/migrations/001_initial_schema.sql"))) root=Directory.GetParent(root)!.FullName;
var today = DateOnly.FromDateTime(DateTime.UtcNow);
var passed = 0;
var q = new ManualInvoiceRequest(Guid.NewGuid(), "basis", "partial", 10000m, 6000m, 1000m, today, today,
    "Authorized implementation milestone", "Approved SOW 123", "External invoice 001", "Approved partial billing", true, "exception", "Approved fixed-price advance invoice instruction", "Finance authorizes billing before delivery and time completion", "Approved SOW 123 version 2", "Verified signed SOW while SELL is unavailable");
Check(ManualBillingPolicy.Validate(q, 4000m, 1000m, false, today) is null, "partial permits only new 1000 charge");
Check(ManualBillingPolicy.Validate(q with {InvoiceType="final",BillToDate=10000m},4000m,1000m,false,today) is null,"full invoice deducts prior internal and external billing");
Check(ManualBillingPolicy.Validate(q with {BillToDate=5000m},4000m,1000m,false,today) is not null,"already billed amount cannot be charged again");
Check(ManualBillingPolicy.Validate(q with {BillToDate=10000.001m},0,0,false,today) is not null,"fractional cents rejected");
Check(ManualBillingPolicy.Validate(q with {Confirmed=false},0,0,false,today) is not null,"authorization confirmation required");
Check(ManualBillingPolicy.Validate(q with {PreviouslyBilledOutsidePulse=0},0,1000,false,today) is not null,"external billing cannot be erased");
Check(ManualBillingPolicy.Validate(q with {ExternalBillingReference=""},0,0,false,today) is not null,"prior external billing requires evidence");
Check(ManualBillingPolicy.Validate(q with {InvoiceType="final"},0,0,false,today) is not null,"final must reconcile full agreed amount");
Check(ManualBillingPolicy.ValidateEligibility(q,false,0,0,false,true,false) is not null,"T&M cannot bypass time eligibility");
Check(ManualBillingPolicy.ValidateEligibility(q with { BillingBasis="progress" },true,0,0,false,true,false) is not null,"zero submissions require exception");
Check(ManualBillingPolicy.ValidateEligibility(q,true,0,0,false,false,false) is not null,"PM cannot self-authorize exception");
Check(ManualBillingPolicy.ValidateEligibility(q,true,0,0,false,true,false) is null,"Billing can explicitly authorize fixed-price advance");
Check(ManualBillingPolicy.ValidateEligibility(q with { BillingBasis="progress" },true,1,1,false,false,false) is null,"incomplete time permits supported partial progress");
Check(ManualBillingPolicy.ValidateEligibility(q with { BillingBasis="completion", InvoiceType="final" },true,1,0,true,false,false) is null,"completed delivery and time permit normal final");
Check(ManualBillingPolicy.ValidateEligibility(q with { BillingBasis="completion", InvoiceType="final" },true,1,1,true,true,false) is not null,"pending time requires explicit final exception");
Check(ManualBillingPolicy.ValidateEligibility(q with { CommercialFallbackReason="" },true,1,0,true,true,false) is not null,"unavailable SELL requires documented fallback");
if(args.Contains("--policy-only")) { Console.WriteLine($"MANUAL_BILLING_POLICY=PASS checks={passed}"); return; }
var settings=new NpgsqlConnectionStringBuilder { Host="127.0.0.1", Port=int.Parse(Environment.GetEnvironmentVariable("PGPORT")??"55432"), Username="postgres", Database="postgres", Pooling=false, Password=Environment.GetEnvironmentVariable("PGPASSWORD")??throw new Exception("Disposable PGPASSWORD required") };
await using var admin=new NpgsqlConnection(settings.ConnectionString); await admin.OpenAsync();
var db="manual_billing_"+Guid.NewGuid().ToString("N"); await Sql(admin,$"CREATE DATABASE {db}"); settings.Database=db;
Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",settings.ConnectionString);
var pm=Guid.NewGuid();var billing=Guid.NewGuid();var unrelated=Guid.NewGuid();var readerId=Guid.NewGuid();
try {
 await using var c=await Open();
 foreach(var file in new[]{"database/migrations/001_initial_schema.sql","tests/ManualBillingTests/fixture.sql","deployment/database/056-module-042-billing-integration-foundation.sql","database/migrations/038_work_to_cash_lifecycle_and_audit.sql"}) await Sql(c,await File.ReadAllTextAsync(Path.Combine(root,file)));
 foreach(var (id,role) in new[]{(pm,"PROJECT_MANAGER"),(billing,"BILLING"),(unrelated,"PROJECT_MANAGER"),(readerId,"EXECUTIVE")}){
  var rid=Guid.NewGuid();await Sql(c,$"INSERT INTO app_users(user_id,email,display_name) VALUES('{id}','{id}@example.invalid','Synthetic billing tester'); INSERT INTO app_roles VALUES('{rid}','{role}',true); INSERT INTO app_user_role_assignments VALUES('{id}','{rid}',true);");
 }
 var p=await Project(c); var initial=await Basis(p,billing);
 Check(Status(await Invoke("GetManualBillingAsync",p,Context(Guid.Empty)))==401,"anonymous denied");
 Check(Status(await Invoke("GetManualBillingAsync",p,Context(unrelated)))==404,"unassigned PM denied");
 Check(Status(await Create(p,q with {ExpectedFingerprint=initial},readerId))==403,"read-only role cannot invoice");
 Check(Status(await Invoke("CreateManualInvoiceAsync",p,q with {ExpectedFingerprint=initial},Context(billing,true)))==403,"View-As cannot invoice");
 Check(Status(await Create(p,q with {ExpectedFingerprint=initial,BillingBasis="progress"},billing))==400,"handler blocks missing submissions without exception");
 Check(Status(await Create(p,q with {ExpectedFingerprint=initial},pm))==400,"handler rejects PM exception approval");
 var tm=await Project(c);await Sql(c,$"UPDATE projects SET contract_type='Time and Materials' WHERE project_id='{tm}'");
 Check(Status(await Create(tm,q with {ExpectedFingerprint=await Basis(tm,billing)},billing))==400,"handler restricts manual amounts to fixed price");
 var first=q with {ExpectedFingerprint=initial,BillToDate=3000m};
 var result=await Create(p,first,billing);
 Check(Status(result)==201,"documented Billing exception permits fixed-price partial with no time or connectors");
 Check(Amount(result)==2000m,"external 1000 deducted from first cumulative 3000");
 var retry=await Create(p,first,billing);
 Check(Status(retry)==200 && InvoiceId(result)==InvoiceId(retry),"identical retry reuses invoice and number");
 Check(Status(await Create(p,first with {Description="Different charge"},billing))==409,"operation cannot be reused for different charge");
 Check(Status(await Create(p,first with {OperationId=Guid.NewGuid()},billing))==409,"stale balance cannot create invoice");
 var second=first with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(p,billing),BillToDate=6000m};
 Check(Amount(await Create(p,second,billing))==3000m,"second partial deducts earlier Pulse and external invoices");
 var timesheet=Guid.NewGuid();var entry=Guid.NewGuid();
 await Sql(c,$"INSERT INTO timesheets(timesheet_id,user_id,week_start_date,week_end_date) VALUES('{timesheet}','{pm}',current_date,current_date+6); INSERT INTO time_entries(time_entry_id,timesheet_id,user_id,project_id,work_date,hours,status) VALUES('{entry}','{timesheet}','{pm}','{p}',current_date,2,'submitted');");
 var final=first with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(p,billing),InvoiceType="final",BillToDate=10000m};
 var full=await Create(p,final,billing); Check(Status(full)==201 && Amount(full)==4000m,"full invoice works despite unapproved time and only charges remaining balance");
 Check(await Text(c,$"SELECT status FROM time_entries WHERE time_entry_id='{entry}'")=="submitted","invoice does not approve or alter source time");
 Check(await Text(c,$"SELECT status FROM projects WHERE project_id='{p}'")=="active","full invoice does not close project");
 Check(await Text(c,"SELECT count(*)::text FROM external_integration_outbox")=="0","creation does not queue or send external invoices");
 Check(await Text(c,$"SELECT sum(total_amount)::text FROM billing_invoices WHERE project_id='{p}'")=="9000.00","cumulative local invoices plus external total reconcile to 10000");
 foreach(var format in new[]{"pdf","excel"}) {
  var ctx=Context(billing);ctx.Request.QueryString=new QueryString("?format="+format);
  var methodDoc=typeof(CertiniaBillingModule).GetMethod("GetDocumentAsync",BindingFlags.NonPublic|BindingFlags.Static)!;
  var taskDoc=(Task)methodDoc.Invoke(null,[InvoiceId(full),ctx])!;await taskDoc;
  var file=(Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult)taskDoc.GetType().GetProperty("Result")!.GetValue(taskDoc)!;
  var bytes=file.FileContents.ToArray();
  Check(bytes.Length>1000 && (format=="pdf" ? Encoding.ASCII.GetString(bytes,0,5)=="%PDF-" : bytes[0]==80 && bytes[1]==75),"manual "+format+" download is a real document");
  if(format=="excel") {
   using var zip=new ZipArchive(new MemoryStream(bytes));
   var xml=string.Join("",zip.Entries.Where(e=>e.FullName.StartsWith("xl/worksheets/")).Select(e=>new StreamReader(e.Open()).ReadToEnd()));
   Check(xml.Contains("Project billing") && xml.Contains("4000"),"Excel presents project amount with correct remaining charge");
   Check(!xml.Contains("Approved partial billing") && !xml.Contains("Synthetic billing tester"),"customer output excludes audit reason and resource names");
  }
 }
 Check(Status(await Create(p,final with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(p,billing),AgreedTotal=11000,BillToDate=11000},billing))==400,"second final cannot duplicate charges");
 // Existing time-based invoice handler must reject another path after manual amount billing.
 var method=typeof(InvoiceBillingModule).GetMethod("CreateInvoiceAsync",BindingFlags.NonPublic|BindingFlags.Static)!;
 var ordinary=JsonSerializer.Deserialize($"{{\"invoiceType\":\"partial\",\"lines\":[{{\"timeEntryId\":\"{entry}\",\"rateLineId\":\"{Guid.NewGuid()}\"}}]}}",method.GetParameters()[1].ParameterType,new JsonSerializerOptions(JsonSerializerDefaults.Web));
 Check(Status(await Invoke("CreateInvoiceAsync",p,ordinary,Context(billing)))==409,"time-based billing cannot double-charge manual amount project");
 var concurrent=await Project(c);var common=first with {ExpectedFingerprint=await Basis(concurrent,billing),PreviouslyBilledOutsidePulse=0};
 var results=await Task.WhenAll(Create(concurrent,common with {OperationId=Guid.NewGuid()},billing),Create(concurrent,common with {OperationId=Guid.NewGuid()},billing));
 Check(results.Count(r=>Status(r)==201)==1 && results.Count(r=>Status(r)==409)==1,"concurrent submissions produce only one charge");
 var po=await Project(c);await Sql(c,$"INSERT INTO project_purchase_orders(project_id,po_number,is_primary,authorized_amount) VALUES('{po}','SYNTHETIC-PO',true,2000);");
 Check(Status(await Create(po,first with {ExpectedFingerprint=await Basis(po,billing),OperationId=Guid.NewGuid()},billing))==409,"PO amount enforced even for Billing exception");
 var prior=await Project(c);var priorFirst=first with {ExpectedFingerprint=await Basis(prior,billing),OperationId=Guid.NewGuid(),PreviouslyBilledOutsidePulse=0};
 var priorInvoice=await Create(prior,priorFirst,billing);
 // A stored time invoice is included in the same balance even if created before manual billing existed.
 await Sql(c,$"UPDATE billing_invoices SET immutable_snapshot_json='{{}}' WHERE billing_invoice_id='{InvoiceId(priorInvoice)}'");
 Check(Amount(await Create(prior,final with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(prior,billing)},billing))==6000m,"existing non-manual invoices deducted from full billing");
 var voidProject=await Project(c);
 var voidRequest=first with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(voidProject,billing)};
 var voidInvoice=await Create(voidProject,voidRequest,billing);
 await Sql(c,$"UPDATE billing_invoices SET invoice_status='void' WHERE billing_invoice_id='{InvoiceId(voidInvoice)}'");
 Check(Status(await Create(voidProject,voidRequest with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(voidProject,billing),PreviouslyBilledOutsidePulse=0},billing))==400,"voiding a Pulse invoice cannot erase recorded external charges");
 var voidBasis=Value(await Invoke("GetManualBillingAsync",voidProject,Context(billing))).GetProperty("basis");
 Check(voidBasis.GetProperty("manualInvoicesExist").GetBoolean(),"voiding retains manual reconciliation mode");
 var closed=await Project(c);await Sql(c,$"INSERT INTO work_register_project_lifecycle VALUES('{closed}',true)");
 Check(Status(await Create(closed,first with {ExpectedFingerprint=await Basis(closed,billing),OperationId=Guid.NewGuid()},billing))==409,"archived project denied");
 var queued=await Project(c);
 var queuedFirst=await Create(queued,first with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(queued,billing)},billing);
 await Sql(c,$"INSERT INTO external_integration_outbox(system_code,operation_type,local_entity,local_entity_id,idempotency_key,payload_json,delivery_status) VALUES('CERTINIA','create','billing_invoice','{InvoiceId(queuedFirst)}','synthetic-queued','{{}}','pending')");
 Check(Status(await Create(queued,second with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(queued,billing)},billing))==201,"pending Certinia delivery does not block next local invoice");
 Check(await Text(c,$"SELECT immutable_snapshot_json#>>'{{request,billingBasis}}' FROM billing_invoices WHERE billing_invoice_id='{InvoiceId(queuedFirst)}'")=="exception","immutable invoice retains billing basis");
 Check(await Text(c,$"SELECT immutable_snapshot_json->>'exceptionApprovedBy' FROM billing_invoices WHERE billing_invoice_id='{InvoiceId(queuedFirst)}'")==billing.ToString(),"exception approver is verified session identity");
 Check(await Text(c,$"SELECT immutable_snapshot_json->>'commercialReconciliationRequired' FROM billing_invoices WHERE billing_invoice_id='{InvoiceId(queuedFirst)}'")=="true","SELL outage remains explicit in immutable evidence");
 var recovery=new BillingRecoveryRequest(Guid.NewGuid(),"hold_delivery","INC-123","Verified connector outage; hold for Billing review",true);
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),recovery,Context(pm)))==403,"PM cannot reconcile external billing");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),recovery,Context(billing,true)))==403,"View-As cannot reconcile billing");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),recovery,Context(billing)))==200,"Billing can hold pending delivery");
 Check(await Text(c,$"SELECT delivery_status FROM external_integration_outbox WHERE local_entity_id='{InvoiceId(queuedFirst)}'")=="cancelled","hold atomically cancels pending retries");
 Check(await DuplicateBlocked(queued,InvoiceId(queuedFirst)),"held invoice cannot be queued or claimed for send");
 var resume=recovery with {OperationId=Guid.NewGuid(),Action="resume_delivery"};
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),resume,Context(billing)))==200,"Billing can release unconsumed hold");
 Check(!await DuplicateBlocked(queued,InvoiceId(queuedFirst)),"released hold permits existing delivery");
 Check(await Text(c,$"SELECT delivery_status FROM external_integration_outbox WHERE local_entity_id='{InvoiceId(queuedFirst)}'")=="pending","release restores existing queue without duplicating invoice");
 var handoff=recovery with {OperationId=Guid.NewGuid(),Action="manual_handoff",Reference="MANUAL-SENT-001"};
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),handoff,Context(billing)))==200,"manual handoff records evidence and cancels retry");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),handoff,Context(billing)))==200,"reconciliation retry is idempotent");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),handoff with {Reference="changed"},Context(billing)))==409,"altered reconciliation replay rejected");
 Check(await DuplicateBlocked(queued,InvoiceId(queuedFirst)),"manual handoff prevents queue and worker duplicate send");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),resume with {OperationId=Guid.NewGuid()},Context(billing)))==409,"manual handoff cannot be resumed into duplicate billing");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),handoff with {OperationId=Guid.NewGuid(),Action="certinia_match",Reference="CERTINIA-001"},Context(billing)))==200,"existing Certinia invoice match records without transmission");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),handoff with {OperationId=Guid.NewGuid(),Action="sell_verified"},Context(billing)))==409,"cannot claim SELL reconciliation before synchronized quote exists");
 await Sql(c,$"UPDATE projects SET sell_quote_number='SELL-Q-123' WHERE project_id='{queued}'; INSERT INTO external_integration_connections(system_code,display_name,connection_status,inbound_enabled,last_successful_sync_at) VALUES('SELL','ConnectWise SELL','connected',true,now()) ON CONFLICT(system_code) DO UPDATE SET connection_status='connected',inbound_enabled=true,last_successful_sync_at=now();");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),handoff with {OperationId=Guid.NewGuid(),Action="sell_verified",Reference="SELL-Q-123 approved revision 2"},Context(billing)))==200,"restored synchronized SELL information can be reconciled without rewriting invoice");
 await Sql(c,"UPDATE customer_directory_source_authority SET source_mode='manual',provider_key=NULL WHERE customer_source_authority_id=1");
 Check(!Value(await Invoke("GetManualBillingAsync",queued,Context(billing))).GetProperty("sellAvailable").GetBoolean(),"manual customer directory is not misrepresented as a live SELL source");
 await Sql(c,"UPDATE customer_directory_source_authority SET source_mode='sell',provider_key='connectwise_sell' WHERE customer_source_authority_id=1");
 var recoveryView=Value(await Invoke("GetBillingRecoveryAsync",InvoiceId(queuedFirst),Context(billing)));
 Check(recoveryView.GetProperty("history").GetArrayLength()==5,"history retains hold, release, handoff, match and SELL verification once each");
 await Sql(c,$"UPDATE external_integration_outbox SET delivery_status='processing' WHERE local_entity_id='{InvoiceId(queuedFirst)}'");
 Check(Status(await Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),handoff with {OperationId=Guid.NewGuid()},Context(billing)))==409,"in-flight transmission cannot be overridden");
 Check(await Text(c,$"SELECT count(DISTINCT event_json->>'revision')::text FROM billing_invoice_events WHERE billing_invoice_id='{InvoiceId(queuedFirst)}' AND event_type LIKE 'billing_recovery_%'")=="5","recovery decisions have an explicit distinct sequence");
 // Concurrent decisions must serialize or return conflict, never share a state revision.
 await Sql(c,$"UPDATE external_integration_outbox SET delivery_status='cancelled' WHERE local_entity_id='{InvoiceId(queuedFirst)}'");
 var decisions=await Task.WhenAll(Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),recovery with {OperationId=Guid.NewGuid()},Context(billing)),Invoke("SaveBillingRecoveryAsync",InvoiceId(queuedFirst),recovery with {OperationId=Guid.NewGuid()},Context(billing)));
 Check(decisions.All(r=>Status(r) is 200 or 409) && decisions.Any(r=>Status(r)==200),"concurrent reconciliation serializes or requests a safe retry");
 Check(await Text(c,$"SELECT (count(*)=count(DISTINCT event_json->>'revision'))::text FROM billing_invoice_events WHERE billing_invoice_id='{InvoiceId(queuedFirst)}' AND event_type LIKE 'billing_recovery_%'")=="true","concurrent reconciliation cannot reuse a state revision");
 // Partial progress needs submitted evidence, but does not require all time approved.
 var progress=await Project(c);var progressSheet=timesheet;
 await Sql(c,$"INSERT INTO time_entries(timesheet_id,user_id,project_id,work_date,hours,status) VALUES('{progressSheet}','{pm}','{progress}',current_date,2,'submitted')");
 var progressRequest=first with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(progress,billing),BillingBasis="progress"};
 Check(Status(await Create(progress,progressRequest,pm))==201,"assigned PM can invoice supported partial progress with incomplete time");
 Check(Status(await Create(progress,progressRequest with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(progress,billing),InvoiceType="final",BillToDate=10000},pm))==400,"incomplete time and delivery cannot use ordinary final billing");
 var timeFingerprint=await Basis(progress,billing);
 await Sql(c,$"UPDATE time_entries SET hours=3 WHERE project_id='{progress}'");
 Check(Status(await Create(progress,progressRequest with {OperationId=Guid.NewGuid(),ExpectedFingerprint=timeFingerprint,BillToDate=6000},pm))==409,"time changes invalidate reviewed evidence");
 Console.WriteLine($"MANUAL_BILLING_DATABASE=PASS checks={passed}");
} finally { NpgsqlConnection.ClearAllPools();await Sql(admin,$"DROP DATABASE {db} WITH (FORCE)"); }
void Check(bool condition,string name){if(!condition)throw new Exception(name);passed++;Console.WriteLine($"PASS {name}");}
DefaultHttpContext Context(Guid id,bool viewAs=false){var c=new DefaultHttpContext();if(id!=Guid.Empty)c.Items["ProjectPulseSessionUserId"]=id;c.Items["ProjectPulseIsViewAs"]=viewAs;return c;}
int Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode??(r is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult?403:200);
JsonElement Value(IResult r)=>JsonSerializer.SerializeToElement((r as IValueHttpResult)?.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
decimal Amount(IResult r){Check(Status(r) is 200 or 201,"invoice creation succeeded: "+Value(r));return Value(r).GetProperty("invoice").GetProperty("header").GetProperty("totalAmount").GetDecimal();}
Guid InvoiceId(IResult r)=>Value(r).GetProperty("invoice").GetProperty("header").GetProperty("billingInvoiceId").GetGuid();
async Task<IResult> Invoke(string name,params object?[] args){var task=(Task)typeof(InvoiceBillingModule).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;await task;return (IResult)task.GetType().GetProperty("Result")!.GetValue(task)!;}
async Task<IResult> Create(Guid p,ManualInvoiceRequest req,Guid actor)=>await Invoke("CreateManualInvoiceAsync",p,req,Context(actor));
async Task<string> Basis(Guid p,Guid actor)=>Value(await Invoke("GetManualBillingAsync",p,Context(actor))).GetProperty("basis").GetProperty("fingerprint").GetString()!;
async Task<NpgsqlConnection> Open(){var c=new NpgsqlConnection(settings.ConnectionString);await c.OpenAsync();return c;}
async Task Sql(NpgsqlConnection c,string sql){await using var cmd=new NpgsqlCommand(sql,c);await cmd.ExecuteNonQueryAsync();}
async Task<string> Text(NpgsqlConnection c,string sql){await using var cmd=new NpgsqlCommand(sql,c);return Convert.ToString(await cmd.ExecuteScalarAsync())!;}
async Task<Guid> Project(NpgsqlConnection c){var id=Guid.NewGuid();var client=Guid.NewGuid();await Sql(c,$"INSERT INTO clients(client_id,client_name) VALUES('{client}','Synthetic customer {client}'); INSERT INTO projects(project_id,client_id,project_code,project_name,project_manager_user_id) VALUES('{id}','{client}','{id}','Synthetic manual billing','{pm}')");return id;}

async Task<bool> DuplicateBlocked(Guid projectId,Guid invoiceId){
 await using var c=await Open();await using var tx=await c.BeginTransactionAsync();
 var method=typeof(WorkLifecycleModule).GetMethod("GuardManualCertiniaDuplicateAsync",BindingFlags.NonPublic|BindingFlags.Static)!;
 try{await (Task)method.Invoke(null,[c,tx,projectId,invoiceId,CancellationToken.None])!;return false;}
 catch(ManualCertiniaDuplicateException){return true;}
}
