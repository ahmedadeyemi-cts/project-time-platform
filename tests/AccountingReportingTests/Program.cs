using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Modules;
var root=Directory.GetCurrentDirectory();
var today=DateOnly.FromDateTime(DateTime.UtcNow);var passed=0;
var q=new AccountingCommand(Guid.NewGuid(),"entry",100,today,"FIN-123","Finance approved recognition","revenue");
Check(AccountingPolicy.Validate(q,today)==null,"approved revenue accepted");
Check(AccountingPolicy.Validate(q with {Amount=-50},today)==null,"signed revenue adjustment accepted");
Check(AccountingPolicy.Validate(q with {Date=today.AddDays(1)},today)!=null,"future recognition denied");
Check(AccountingPolicy.Validate(q with {Amount=1.001m},today)!=null,"fractional cents denied");
Check(AccountingPolicy.Validate(q with {Kind="invoice"},today)!=null,"invoice cannot masquerade as recognition");
Check(AccountingPolicy.Validate(q with {SalesforceAccountId="wrong"},today)!=null,"Salesforce identity validated");
if(args.Contains("--policy-only"))return;
var cs=Environment.GetEnvironmentVariable("ACCOUNTING_TEST_CONNECTION") ?? "Host=127.0.0.1;Port=55432;Username=postgres;Database=postgres;Pooling=false";
Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",cs);
await using var c=new NpgsqlConnection(cs);await c.OpenAsync();
if(!args.Contains("--prepared"))foreach(var file in new[]{"database/migrations/001_initial_schema.sql","tests/ManualBillingTests/fixture.sql","deployment/database/056-module-042-billing-integration-foundation.sql","database/migrations/038_work_to_cash_lifecycle_and_audit.sql","database/migrations/135_accounting_engagement_reporting.sql"})await Sql(File.ReadAllText(Path.Combine(root,file)));
var actor=Guid.NewGuid();var pm=Guid.NewGuid();var unrelated=Guid.NewGuid();
foreach(var (id,role) in new[]{(actor,"FINANCE"),(pm,"PROJECT_MANAGER"),(unrelated,"PROJECT_MANAGER")}){var rid=Guid.NewGuid();await Sql($"INSERT INTO app_users(user_id,email,display_name) VALUES('{id}','{id}@example.invalid','Synthetic finance tester'); INSERT INTO app_roles VALUES('{rid}','{role}',true); INSERT INTO app_user_role_assignments VALUES('{id}','{rid}',true);");}
var client=Guid.NewGuid();var p=Guid.NewGuid();var other=Guid.NewGuid();
await Sql($"INSERT INTO clients(client_id,client_name) VALUES('{client}','Synthetic accounting customer'); INSERT INTO projects(project_id,client_id,project_code,project_name,project_manager_user_id) VALUES('{p}','{client}','ACCT-TEST','Synthetic accounting engagement','{pm}'),('{other}','{client}','ACCT-OTHER','Other engagement','{unrelated}')");
Check(Status(await Invoke("GetAccountingAsync",p,Context(Guid.Empty)))==401,"anonymous accounting denied");
Check(Status(await Invoke("GetAccountingAsync",p,Context(pm)))==404,"PM cannot read accounting ledger");
Check(Status(await Invoke("SaveAccountingAsync",p,q,Context(actor,true)))==403,"View-As cannot mutate accounting");
Check(Status(await Invoke("SaveAccountingAsync",p,q,Context(pm)))==403,"PM cannot recognize revenue");
var view=Value(await Invoke("GetAccountingAsync",p,Context(actor)));
Check(view.GetProperty("summary")[0].GetProperty("totalContractAmount").ValueKind==JsonValueKind.Null,"unverified contract remains unknown");
var profile=q with {OperationId=Guid.NewGuid(),Action="profile",Amount=10000,Reference="SOW-123",SalesforceAccountId="001000000000001AAA",ExpectedVersion=view.GetProperty("summary")[0].GetProperty("accountingVersion").GetString()!};
Check(Status(await Save(profile))==200,"Finance records contract and customer identity");
Check(Status(await Save(profile))==200,"same profile retry is idempotent");
Check(Status(await Save(profile with {OperationId=Guid.NewGuid()}))==409,"stale profile cannot overwrite current identity");
Check(Status(await Save(q))==200,"Finance posts monthly recognition");
Check(Status(await Save(q))==200,"same revenue retry does not duplicate");
Check(Status(await Save(q with {Amount=101}))==409,"same operation cannot change financial amount");
Check(Status(await Save(q with {OperationId=Guid.NewGuid(),Amount=-25}))==200,"adjustment preserves original entry");
Check(await Number($"SELECT sum(amount) FROM accounting_entries WHERE project_id='{p}' AND kind='revenue'")==75,"recognition net reconciles");
Check(Status(await Save(q with {OperationId=Guid.NewGuid(),Kind="prepaid_funding",Amount=5000}))==200,"prepaid funding recorded");
Check(Status(await Save(q with {OperationId=Guid.NewGuid(),Kind="prepaid_usage",Amount=500}))==200,"prepaid usage recorded");
Check(await Number($"SELECT \"prepaidBalance\" FROM accounting_engagement_report WHERE project_id='{p}'")==4500,"prepaid balance reconciles");
var milestone=q with {OperationId=Guid.NewGuid(),Action="milestone",Name="Design accepted",Amount=2000,Date=today.AddDays(5)};
Check(Status(await Save(milestone))==200,"future scheduled milestone recorded");
Check(Status(await Save(milestone with {OperationId=Guid.NewGuid(),Amount=9000}))==409,"milestone total cannot exceed contract");
var invoice=new ManualInvoiceRequest(Guid.NewGuid(),await Basis(),"partial",10000,2000,0,today,today,"Accepted design milestone","SOW-123","","Finance approved milestone billing",true,"exception","Acceptance evidence FIN-123","Finance approved billing before time submission","SOW-123 v1","Verified signed SOW without SELL",milestone.OperationId);
Check(Status(await Invoke("CreateManualInvoiceAsync",p,invoice,Context(actor)))==409,"unaccepted milestone cannot be billed");
var acceptance=q with {OperationId=Guid.NewGuid(),Action="accept",TargetId=milestone.OperationId};
Check(Status(await Save(acceptance))==200,"acceptance recorded with verified actor");
Check(Status(await Invoke("CreateManualInvoiceAsync",p,invoice with {BillToDate=3000},Context(actor)))==409,"milestone amount mismatch rejected");
var billed=await Invoke("CreateManualInvoiceAsync",p,invoice,Context(actor));Check(Status(billed)==201,"accepted exact milestone creates invoice");
Check(Status(await Invoke("CreateManualInvoiceAsync",p,invoice,Context(actor)))==200,"invoice retry returns same saved milestone invoice");
Check(Status(await Invoke("CreateManualInvoiceAsync",p,invoice with {OperationId=Guid.NewGuid(),ExpectedFingerprint=await Basis(),BillToDate=4000},Context(actor)))==409,"milestone cannot be billed twice");
Check(await Number($"SELECT \"totalInvoiceAmount\" FROM accounting_engagement_report WHERE project_id='{p}'")==2000,"invoice total counted once");
var sheet=Guid.NewGuid();var entry=Guid.NewGuid();await Sql($"INSERT INTO timesheets(timesheet_id,user_id,week_start_date,week_end_date) VALUES('{sheet}','{pm}',current_date,current_date+6); INSERT INTO time_entries(time_entry_id,timesheet_id,user_id,project_id,work_date,hours,status,billable) VALUES('{entry}','{sheet}','{pm}','{p}',current_date,2,'pm_approved',true)");
var rate=q with {OperationId=Guid.NewGuid(),Action="rate",TargetId=entry,Amount=225};Check(Status(await Save(rate))==200,"dated approved time rate captured");
Check(await Number($"SELECT \"billableAmount\" FROM accounting_time_report WHERE \"timeEntryId\"='{entry}'")==450,"time extends historical hours and rate");
Check(Status(await Save(rate with {OperationId=Guid.NewGuid(),TargetId=Guid.NewGuid()}))==409,"foreign or missing time entry denied");
await Sql(File.ReadAllText(Path.Combine(root,"database/migrations/135_accounting_engagement_reporting.sql")));
Check(await Number($"SELECT \"recognizedToDate\" FROM accounting_revenue_report WHERE project_id='{p}'")==75,"migration rerun preserves monthly recognition");
try{await Sql($"UPDATE accounting_entries SET amount=999 WHERE entry_id='{q.OperationId}'");throw new Exception("immutable history edited");}catch(PostgresException){Check(true,"financial ledger rejects mutation");}
Console.WriteLine($"ACCOUNTING_REPORTING_DATABASE=PASS checks={passed}");
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
DefaultHttpContext Context(Guid id,bool viewAs=false){var c=new DefaultHttpContext();if(id!=Guid.Empty)c.Items["ProjectPulseSessionUserId"]=id;c.Items["ProjectPulseIsViewAs"]=viewAs;return c;}
int Status(IResult r)=>(r as IStatusCodeHttpResult)?.StatusCode??(r is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult?403:200);
JsonElement Value(IResult r)=>JsonSerializer.SerializeToElement((r as IValueHttpResult)?.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
async Task<IResult> Invoke(string name,params object?[] args){var task=(Task)typeof(InvoiceBillingModule).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;await task;return (IResult)task.GetType().GetProperty("Result")!.GetValue(task)!;}
async Task<IResult> Save(AccountingCommand req)=>await Invoke("SaveAccountingAsync",p,req,Context(actor));
async Task<string> Basis()=>Value(await Invoke("GetManualBillingAsync",p,Context(actor))).GetProperty("basis").GetProperty("fingerprint").GetString()!;
async Task Sql(string sql){await using var cmd=new NpgsqlCommand(sql,c);await cmd.ExecuteNonQueryAsync();}
async Task<decimal> Number(string sql){await using var cmd=new NpgsqlCommand(sql,c);return Convert.ToDecimal(await cmd.ExecuteScalarAsync());}
