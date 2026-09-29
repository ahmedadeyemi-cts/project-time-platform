using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Npgsql;
using ProjectTime.Api.Modules;

var count=0;
var assembly=typeof(ProjectFlowHivePlanRequest).Assembly;
var flags=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
var project=Guid.NewGuid();var actor=Guid.NewGuid();
void Check(bool success,string name){if(!success)throw new Exception("FAILED: "+name);count++;Console.WriteLine("PASS "+name);}
object? Call(string type,string method,params object?[] args)
{
 try{return assembly.GetType("ProjectTime.Api.Modules."+type)!.GetMethod(method,flags)!.Invoke(null,args);}
 catch(TargetInvocationException e){throw e.InnerException!;}
}
async Task<object?> Invoke(string method,params object?[] args)
{
 var task=(Task)Call("ProjectFlowHiveCollaborationStore",method,args)!;await task;return task.GetType().GetProperty("Result")?.GetValue(task);
}
object Parse(string value)=>(Call("ProjectFlowHiveRequestReader","ParseWorkingCopy",value,"fixture-correlation"))!;
ProjectFlowHiveWorkingCopyRequest? Parsed(object result)=>(ProjectFlowHiveWorkingCopyRequest?)result.GetType().GetProperty("Request")!.GetValue(result);
IResult? ParseError(object result)=>(IResult?)result.GetType().GetProperty("Error")!.GetValue(result);
var cleared=$$"""{"plan":{"projectId":"{{project}}","projectStartDate":"2026-09-28","projectEndDate":"","planId":"","tasks":[{"wbsNumber":"1.1","name":"Prepare","durationWorkingDays":2,"constraintDate":"","canonicalTaskId":"","percentComplete":0,"remainingEffortHours":8,"notes":"Retain notes","detailedSteps":["Review","Confirm"]}],"assignments":[],"dependencies":[]},"expectedRowVersion":""}""";
var parsed=Parse(cleared);
Check(ParseError(parsed) is null && Parsed(parsed)!.Plan!.ProjectEndDate is null,"working copy accepts a cleared optional end date");
Check(Parsed(parsed)!.ExpectedRowVersion is null && Parsed(parsed)!.Plan!.PlanId is null,"empty optional identifiers normalize to null");
Check(Parsed(parsed)!.Plan!.Tasks![0].Notes=="Retain notes" && Parsed(parsed)!.Plan!.Tasks![0].DetailedSteps!.Count==2,"request normalization preserves WBS notes and steps");
foreach(var pair in new[]{("\"durationWorkingDays\":2","\"durationWorkingDays\":1.5"),("\"constraintDate\":\"\"","\"constraintDate\":\"2026-02-30\""),("\"canonicalTaskId\":\"\"","\"canonicalTaskId\":\"not-a-guid\"")})
 Check(ParseError(Parse(cleared.Replace(pair.Item1,pair.Item2))) is IStatusCodeHttpResult{StatusCode:400},"invalid required type/date/identity remains a structured 400");
Check(ParseError(Parse("{\"plan\":{\"tasks\":[null]}}")) is IStatusCodeHttpResult{StatusCode:400},"null task rows are rejected before engine dereference");
Check(ParseError(Parse("{\"plan\":{\"tasks\":[1]}}")) is IStatusCodeHttpResult{StatusCode:400},"scalar task rows are rejected before engine dereference");

var contactJson="""{"displayName":"Customer lead","email":"lead@example.invalid","phone":"5550100012","title":"VP of Sales","organization":"Example organization","contactKind":"customer","isActive":true,"projectContactId":"","expectedRowVersion":""}""";
var contactParsed=Call("ProjectFlowHiveRequestReader","ParseContact",contactJson,"contact-fixture")!;
Check(contactParsed.GetType().GetProperty("Error")!.GetValue(contactParsed) is null,"contact form with cleared optional identifiers binds without a generic 400");
var contactRequest=(FlowHiveContactRequest)contactParsed.GetType().GetProperty("Request")!.GetValue(contactParsed)!;
Check(contactRequest.Phone=="5550100012" && contactRequest.ProjectContactId is null,"contact phone stays text and empty identity never becomes a new authority");
var badContact=Call("ProjectFlowHiveRequestReader","ParseContact",contactJson.Replace("\"phone\":\"5550100012\"","\"phone\":5550100012"),"contact-fixture")!;
Check(badContact.GetType().GetProperty("Error")!.GetValue(badContact) is IStatusCodeHttpResult{StatusCode:400},"malformed contact fields produce structured errors");
var bufferedContext=new DefaultHttpContext();bufferedContext.Request.ContentType="application/json";
bufferedContext.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(cleared));bufferedContext.Request.Body.Position=bufferedContext.Request.Body.Length;
var bufferedTask=(Task)Call("ProjectFlowHiveRequestReader","ReadAsync",bufferedContext,CancellationToken.None)!;await bufferedTask;
var bufferedResult=bufferedTask.GetType().GetProperty("Result")!.GetValue(bufferedTask)!;
Check(ParseError(bufferedResult) is null,"working-copy reader replays a previously inspected buffered body");

var builder=WebApplication.CreateBuilder(Array.Empty<string>());builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
await using(var app=builder.Build())
{
 app.UseProjectPulseSecurityHardening();
 app.MapPut("/old",(ProjectFlowHiveWorkingCopyRequest request)=>Results.Ok());
 app.MapPost("/fixture/contact-contract",(FlowHiveContactRequest request)=>Results.Ok(new {request.DisplayName,request.ProjectContactId}));
 app.MapPut("/fixture/controls-contract",(ProjectFlowHiveProjectControlsRequest request)=>Results.Ok(new {request.ApprovedBudget,request.CustomerSharingEnabled}));
 var type=assembly.GetType("ProjectTime.Api.Modules.ProjectFlowHiveEnterpriseModule")!;
 var handler=(Func<Guid,HttpContext,CancellationToken,Task<IResult>>)type.GetMethod("SaveWorkingCopyAsync",flags)!.CreateDelegate(typeof(Func<Guid,HttpContext,CancellationToken,Task<IResult>>));
 app.MapPut("/working-copy/{projectId:guid}",handler);
 app.MapPost("/contacts/{projectId:guid}",(Func<Guid,HttpContext,CancellationToken,Task<IResult>>)type.GetMethod("SaveProjectContactAsync",flags)!.CreateDelegate(typeof(Func<Guid,HttpContext,CancellationToken,Task<IResult>>)));
 app.MapPost("/meetings/{projectId:guid}",(Func<Guid,FlowHiveMeetingDraftRequest,HttpContext,CancellationToken,Task<IResult>>)type.GetMethod("CreateMeetingDraftAsync",flags)!.CreateDelegate(typeof(Func<Guid,FlowHiveMeetingDraftRequest,HttpContext,CancellationToken,Task<IResult>>)));
 await app.StartAsync();using var client=new HttpClient{BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
 async Task<HttpStatusCode> Send(HttpMethod method,string path,string body){using var req=new HttpRequestMessage(method,path){Content=new StringContent(body,Encoding.UTF8,"application/json")};using var res=await client.SendAsync(req);return res.StatusCode;}
 Check(await Send(HttpMethod.Put,"/old",cleared)==HttpStatusCode.BadRequest,"real original typed binding reproduces blank-DateOnly 400");
 Check(await Send(HttpMethod.Post,"/fixture/contact-contract","""{"displayName":"UAT QA Contact","email":"qa@example.invalid","phone":"","title":"","organization":"Synthetic","contactKind":"customer","isActive":true}""")==HttpStatusCode.OK,"valid browser contact DTO survives security inspection and real typed binding");
 Check(await Send(HttpMethod.Put,"/fixture/controls-contract","""{"contractType":"unknown","currencyCode":"USD","approvedBudget":null,"expenseBudget":null,"contingencyBudget":null,"forecastAtCompletion":null,"percentCompleteMethod":"task_weighted","statusReportCadence":"weekly","customerSharingEnabled":false,"financialNotes":"Synthetic QA"}""")==HttpStatusCode.OK,"optional financial amounts survive security inspection and real typed binding");
 Check(await Send(HttpMethod.Put,$"/working-copy/{project}",cleared)==HttpStatusCode.Unauthorized,"real new save handler checks session before parsing or DB access");
 Check(await Send(HttpMethod.Post,$"/contacts/{project}","{}") == HttpStatusCode.Unauthorized,"contact handler never trusts anonymous caller");
 Check(await Send(HttpMethod.Post,$"/meetings/{project}","{}") == HttpStatusCode.Unauthorized,"meeting draft handler never trusts anonymous caller");
 await app.StopAsync();
}
ProjectFlowHivePlanTaskInput TaskRow(string wbs,string name,int duration)=>new(Guid.NewGuid(),null,wbs,null,name,"Synthetic fixture",duration,false,"ASAP",null,0,0,"not_started");
ProjectFlowHivePlanRequest Plan(Guid id)=>new(id,"FIXTURE","Synthetic project","Synthetic customer","Synthetic working plan","fixture",new DateOnly(2026,9,28),null,
 [TaskRow("1","Prepare",2),TaskRow("2","Long branch",5),TaskRow("3","Short branch",1)],
 [new("1","2","FS",0),new("1","3","FS",0)],[],null,null,null);
var cpm=ProjectFlowHiveScheduleEngine.Calculate(Plan(project));
Check(cpm.Valid,"existing CPM accepts diamond dependency fixture");
Check(cpm.Tasks.Single(x=>x.WbsNumber=="2").IsCritical && !cpm.Tasks.Single(x=>x.WbsNumber=="3").IsCritical,"existing CPM separates controlling and noncontrolling branches");
Check(cpm.Tasks.Single(x=>x.WbsNumber=="3").TotalFloatWorkingDays==4,"existing CPM computes four days of float on short branch");
Check(!ProjectFlowHiveScheduleEngine.Calculate(Plan(project) with {Dependencies=[new("1","2","FS",0),new("2","1","FS",0)]}).Valid,"cycle rejection is intentional, not suppressed to avoid 400");
var phasePlan=Plan(project) with {Tasks=[TaskRow("1","Plan",0) with {IsSummary=true,Phase="Plan"},TaskRow("1.1","Discover",2) with {ParentWbsNumber="1",Phase="Plan"},TaskRow("1.2","Review",3) with {ParentWbsNumber="1",Phase="Plan"}],Dependencies=[new("1.1","1.2","FS",0)]};
var phaseSchedule=ProjectFlowHiveScheduleEngine.Calculate(phasePlan);
Check(phaseSchedule.Valid && phaseSchedule.Tasks.Single(t=>t.WbsNumber=="1").StartDate==new DateOnly(2026,9,28) && phaseSchedule.Tasks.Single(t=>t.WbsNumber=="1").EndDate==new DateOnly(2026,10,2),"phase dates roll up from task estimates and dependency sequence");


if(args.Contains("--database")) await Database();
Console.WriteLine($"FLOWHIVE_COLLABORATION_ASSERTIONS={count}; LIVE_CONTACT_CHANGES=0; LIVE_INVITATIONS=0; RESULT=PASS");

async Task Database()
{
 var config=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FLOWHIVE_COLLABORATION_DB")??throw new Exception("Named disposable database required"));
 if(config.Host!="127.0.0.1" || config.Database!="flowhive_sharing_test")throw new Exception("Only the named disposable loopback fixture is permitted");
 var schema="collaboration_"+Guid.NewGuid().ToString("N");await using var admin=new NpgsqlConnection(config.ConnectionString);await admin.OpenAsync();await Sql(admin,$"CREATE SCHEMA {schema}");config.SearchPath=schema;
 try
 {
  await using var db=new NpgsqlConnection(config.ConnectionString);await db.OpenAsync();
  await Sql(db,"""
   CREATE TABLE schema_migrations(migration_id text PRIMARY KEY,description text);
   CREATE TABLE app_users(user_id uuid PRIMARY KEY,display_name text,email text,is_active boolean,job_title text,phone text);
   CREATE TABLE projects(project_id uuid PRIMARY KEY,project_manager_user_id uuid,account_executive_user_id uuid,solution_architect_user_id uuid);
   CREATE TABLE project_assignments(project_id uuid,user_id uuid,effective_start_date date,effective_end_date date);
   CREATE TABLE project_planning_collaborators(project_id uuid,user_id uuid,module_code text,is_active boolean,effective_start_date date,effective_end_date date);
   CREATE TABLE project_flowhive_working_copies(project_id uuid PRIMARY KEY,working_payload jsonb);
   """);
  var other=Guid.NewGuid();var empty=Guid.NewGuid();var concurrent=Guid.NewGuid();var outsider=Guid.NewGuid();
  await Sql(db,$"INSERT INTO app_users VALUES('{actor}','Fixture PM','pm@example.invalid',true,'Project Manager','555-0100'),('{outsider}','Outside user','outside@example.invalid',true,'Private role','555-0199');INSERT INTO projects VALUES('{project}','{actor}',NULL,NULL),('{other}',NULL,NULL,NULL),('{empty}',NULL,NULL,NULL),('{concurrent}','{actor}',NULL,NULL);");
  var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(!Directory.Exists(Path.Combine(root.FullName,"database/migrations")))root=root.Parent!;
  var migration=await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/131_flowhive_project_collaboration.sql"));await Sql(db,migration);await Sql(db,migration);
  Check((bool)(await Invoke("ReadyAsync",db,CancellationToken.None))!,"actual migration replays and actual store reports ready");
  var ids=new List<(Guid Id,Guid Version)>();
  for(var i=0;i<15;i++)ids.Add(await Save(db,project,new($"Person {i}",$"person{i}@example.invalid","555-0101","IT lead","Fixture customer","customer")));
  Check(await Scalar<long>(db,$"SELECT count(*) FROM project_flowhive_contacts WHERE project_id='{project}' AND is_active") == 15,"exactly fifteen active contacts persist");
  await Rejected(async()=>{await Save(db,project,new("Sixteenth","sixteen@example.invalid",null,null,null,"customer"));},"sixteenth active contact is rejected");
  await Rejected(async()=>{await Save(db,project,new("Duplicate","PERSON0@example.invalid",null,null,null,"customer"));},"duplicate email or full roster cannot create another active contact");
  var canonical=ids[0];
  var updated=await Save(db,project,new("Updated contact","person0@example.invalid","555-0110","Infrastructure Lead","Customer","customer",canonical.Id,canonical.Version));
  Check(updated.Version!=canonical.Version,"contact edit rotates optimistic row version");
  await Rejected(async()=>{await Save(db,project,new("Stale edit","person0@example.invalid",null,null,null,"customer",canonical.Id,canonical.Version));},"stale edit is rejected instead of overwriting newer contact");
  await Rejected(async()=>{await Save(db,other,new("Wrong project","person0@example.invalid",null,null,null,"customer",canonical.Id,updated.Version));},"cross-project contact updates are rejected");
  var externalPlan=Plan(project) with {Assignments=[new("1",null,"Forged display name",100,0,canonical.Id)]};
  await using(var tx=await db.BeginTransactionAsync())
  {
   var resolved=(ProjectFlowHivePlanRequest)(await Invoke("ResolveContactsAsync",db,tx,project,externalPlan,CancellationToken.None))!;
   Check(resolved.Assignments![0].ResourceDisplayName=="Updated contact" && resolved.Assignments[0].ResourceUserId is null,"external assignment resolves canonical name without creating internal identity");
   await tx.CommitAsync();
  }
  await Rejected(async()=>{await using var tx=await db.BeginTransactionAsync();await Invoke("ResolveContactsAsync",db,tx,other,externalPlan with {ProjectId=other},CancellationToken.None);},"cross-project external assignment rejected");
  await Sql(db,$$"""INSERT INTO project_flowhive_working_copies VALUES('{{project}}','{"assignments":[{"projectContactId":"{{canonical.Id}}","resourceUserId":null},{"resourceUserId":"{{outsider}}"}]}')""");
  await Rejected(async()=>{await Save(db,project,new("Updated contact","person0@example.invalid",null,null,null,"customer",canonical.Id,updated.Version,false));},"assigned contact cannot be silently archived");
  var team=JsonSerializer.SerializeToElement(await Invoke("TeamAsync",db,project,CancellationToken.None),json);
  Check(team.GetArrayLength()==1 && team[0].GetProperty("userId").GetGuid()==actor,"roster is canonical and does not reveal an injected unrelated WBS identity");
  Check(await Scalar<long>(db,"SELECT count(*) FROM app_users")==2,"saving contacts does not provision user accounts");
  for(var i=0;i<14;i++)await Save(db,concurrent,new($"Concurrent {i}",$"race{i}@example.invalid",null,null,null,"vendor"));
  async Task<bool> Race(int i)
  {await using var c=new NpgsqlConnection(config.ConnectionString);await c.OpenAsync();try{await Save(c,concurrent,new($"Race winner {i}",$"racewinner{i}@example.invalid",null,null,null,"partner"));return true;}catch(Exception error)when(error.GetType().Name=="InputException"){return false;}}
  var races=await Task.WhenAll(Race(1),Race(2));Check(races.Count(x=>x)==1 && await Scalar<long>(db,$"SELECT count(*) FROM project_flowhive_contacts WHERE project_id='{concurrent}' AND is_active")==15,"concurrent requests cannot exceed the database contact cap");
  var start=new DateTimeOffset(2026,10,1,16,0,0,TimeSpan.Zero);
  var meeting=new FlowHiveMeetingDraftRequest("Weekly coordination","Agenda; preserve, commas\nand newlines","Customer meeting",start,start.AddHours(1),"America/Chicago",["user:"+actor,"contact:"+canonical.Id]);
  Guid meetingId;
  await using(var tx=await db.BeginTransactionAsync()){meetingId=(Guid)(await Invoke("CreateMeetingAsync",db,tx,project,actor,meeting,CancellationToken.None))!;await tx.CommitAsync();}
  Check(await Scalar<string>(db,$"SELECT status FROM project_flowhive_meeting_drafts WHERE meeting_draft_id='{meetingId}'")=="draft","meeting persists as unsent draft only");
  await Rejected(async()=>{await using var tx=await db.BeginTransactionAsync();await Invoke("CreateMeetingAsync",db,tx,project,actor,meeting with {AttendeeReferences=["user:"+outsider]},CancellationToken.None);},"meeting cannot select an unrelated user by supplied identity");
  await Rejected(async()=>{await using var tx=await db.BeginTransactionAsync();await Invoke("CreateMeetingAsync",db,tx,project,actor,meeting with {EndsAt=start.AddMinutes(-1)},CancellationToken.None);},"invalid meeting interval is rejected");
  var meetings=(System.Collections.IEnumerable)(await Invoke("MeetingsAsync",db,project,CancellationToken.None))!;
  object? savedMeeting=null;foreach(var item in meetings)savedMeeting=item;
  var calendar=(string)Call("ProjectFlowHiveCollaborationStore","CalendarDraft",savedMeeting,new (string Name,string Email)[]{("Customer \"IT\"\r\nBEGIN:VALARM","contact@example.invalid")})!;
  Check(calendar.Contains("METHOD:PUBLISH") && calendar.Contains("STATUS:TENTATIVE") && !calendar.Contains("METHOD:REQUEST"),"calendar export is tentative publication, not a sent invitation");
  Check(calendar.Contains("Agenda\\; preserve\\, commas\\nand newlines") && !calendar.Contains("\r\nBEGIN:VALARM"),"calendar export escapes text and prevents attendee-line injection");
  Check(calendar.Split("\r\n").All(line=>Encoding.UTF8.GetByteCount(line)<=75),"calendar lines are folded at UTF8-safe boundaries");
  var before=await Scalar<long>(db,$"SELECT count(*) FROM project_flowhive_contacts WHERE project_id='{empty}'");
  await using(var tx=await db.BeginTransactionAsync()){await Invoke("SaveContactAsync",db,tx,empty,actor,new FlowHiveContactRequest("Rollback person","rollback@example.invalid",null,null,null,"customer"),CancellationToken.None);await tx.RollbackAsync();}
  Check(await Scalar<long>(db,$"SELECT count(*) FROM project_flowhive_contacts WHERE project_id='{empty}'")==before,"transaction rollback does not retain partial contact writes");
  var rollback=await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/rollback/131_flowhive_project_collaboration_rollback.sql"));
  var refused=false;try{await Sql(db,rollback);}catch(PostgresException){refused=true;await Sql(db,"ROLLBACK;");}
  Check(refused && await Scalar<long>(db,"SELECT count(*) FROM project_flowhive_contacts")>0,"rollback refuses to discard retained customer information");
 }
 finally{await Sql(admin,$"DROP SCHEMA {schema} CASCADE;");}
}
async Task<(Guid Id,Guid Version)> Save(NpgsqlConnection db,Guid projectId,FlowHiveContactRequest contact)
{
 await using var tx=await db.BeginTransactionAsync();
 // Match the real route's project lock before limit evaluation and contact mutation.
 await using(var q=new NpgsqlCommand("SELECT project_id FROM projects WHERE project_id=@id FOR UPDATE",db,tx)){q.Parameters.AddWithValue("id",projectId);await q.ExecuteScalarAsync();}
 var value=await Invoke("SaveContactAsync",db,tx,projectId,actor,contact,CancellationToken.None);
 await tx.CommitAsync();var tuple=(ValueTuple<Guid,Guid>)value!;return(tuple.Item1,tuple.Item2);
}
async Task Rejected(Func<Task> action,string name)
{var rejected=false;try{await action();}catch(Exception error)when(error.GetType().Name=="InputException"){rejected=true;}Check(rejected,name);}
async Task Sql(NpgsqlConnection db,string sql){await using var q=new NpgsqlCommand(sql,db);await q.ExecuteNonQueryAsync();}
async Task<T> Scalar<T>(NpgsqlConnection db,string sql){await using var q=new NpgsqlCommand(sql,db);return (T)(await q.ExecuteScalarAsync())!;}
