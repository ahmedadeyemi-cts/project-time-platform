using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Npgsql;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

await using (var db = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve()))
{
    await db.OpenAsync();
    await using var setup = new NpgsqlCommand("""
        CREATE TABLE app_users(user_id uuid PRIMARY KEY,is_active boolean NOT NULL);
        CREATE TABLE app_roles(app_role_id uuid PRIMARY KEY,role_code text,is_active boolean);
        CREATE TABLE app_user_role_assignments(user_id uuid,app_role_id uuid,is_active boolean);
        INSERT INTO app_users VALUES
        ('00000000-0000-0000-0000-000000000011',true),('00000000-0000-0000-0000-000000000012',true),
        ('00000000-0000-0000-0000-000000000013',false),('00000000-0000-0000-0000-000000000014',true);
        INSERT INTO app_roles VALUES('00000000-0000-0000-0000-000000000088','SYSTEM_ADMINISTRATOR',true);
        INSERT INTO app_user_role_assignments VALUES('00000000-0000-0000-0000-000000000014','00000000-0000-0000-0000-000000000088',true);
        """,db);
    await setup.ExecuteNonQueryAsync();
}
var builder=WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddSingleton<PulseAiPrivateDocumentPipelineService>();
builder.Services.AddSingleton<PulseAiPrivateRuntimeSourceResolver>();
await using var app=builder.Build();
app.Use(async (context,next) =>
{
    if (Guid.TryParse(context.Request.Headers["X-Test-Actor"].ToString(),out var actor))
    {
        context.Items["ProjectPulseActualUserId"]=actor;
        context.Items["ProjectPulseSessionUserId"]=actor;
        context.Items["ProjectPulseEffectiveUserId"]=Guid.TryParse(context.Request.Headers["X-Test-Effective"].ToString(),out var effective)?effective:actor;
    }
    await next();
});
app.MapLayaDecisionEndpoints();
await app.StartAsync();
using var client=new HttpClient { BaseAddress=new Uri(app.Urls.Single()) };
const string root="/api/ai-configuration/decisions/laya";
var path=$"/documents/{Fixture.Doc}/classifications";
var checks=0;
void Check(bool condition,string label) { checks++; if(!condition) throw new Exception(label); }
async Task<JsonNode> Call(string method,string suffix,int expected,object? body=null,Guid? actor=null,string? origin=null,Guid? effective=null,bool anonymous=false)
{
    using var request=new HttpRequestMessage(new HttpMethod(method),root+suffix);
    if(!anonymous) request.Headers.Add("X-Test-Actor",(actor??Fixture.Admin).ToString());
    if(effective is not null) request.Headers.Add("X-Test-Effective",effective.ToString());
    request.Headers.Add("Origin",origin??client.BaseAddress!.GetLeftPart(UriPartial.Authority));
    if(body is not null) request.Content=JsonContent.Create(body);
    using var response=await client.SendAsync(request);
    var raw=await response.Content.ReadAsStringAsync();
    Check((int)response.StatusCode==expected,$"{method} {suffix}: expected {expected}, got {(int)response.StatusCode}: {raw}");
    Check(response.Headers.CacheControl?.NoStore==true,"cache must be no-store");
    Check(!raw.Contains(Fixture.PrivateText),"raw document text must not be returned");
    return JsonNode.Parse(raw)!;
}
await Call("GET","",401,anonymous:true);
await Call("GET","",403,actor:Fixture.User);
await Call("GET","",403,actor:Fixture.Inactive);
await Call("GET","",403,effective:Fixture.User);
Fixture.Candidate=true; await Call("GET","",423); Fixture.Candidate=false;
Fixture.Allowed=false; await Call("POST","/health",423,new{}); Fixture.Allowed=true;
await Call("PUT","",403,new{enabled=true,version=1},origin:"https://untrusted.invalid");
await Call("POST",path,409,new{requestId=Guid.NewGuid()});
Check(Fixture.Inferences==0,"disabled capability must not infer");
await Call("POST","/health",200,new{});
await Call("PUT","",200,new{enabled=true,version=1});
await Call("PUT","",409,new{enabled=false,version=1});
await Call("POST",path,404,new{requestId=Guid.NewGuid()},actor:Fixture.ScopedAdmin);
Fixture.Unsafe=true; await Call("POST",path,422,new{requestId=Guid.NewGuid()}); Fixture.Unsafe=false;
Check(Fixture.Inferences==0,"unauthorized or unsafe documents must not infer");
var state=await Call("GET",$"/documents/{Fixture.Doc}/processing-state",200);
Check(state["processing"]!["readyForClassification"]!.GetValue<bool>(),"document receipt state is exposed without raw text");
var listing=await Call("GET","/documents",200);
Check(listing["documents"]![0]!["processingStage"]!.GetValue<string>()=="ready","list uses durable processing stage");
var id=Guid.NewGuid();
var result=await Call("POST",path,200,new{requestId=id});
var decision=result["decisionId"]!.GetValue<string>();
Check(result["evidence"]!["predictedType"]!.GetValue<string>()=="invoice","recommendation returned");
await Call("POST",path,200,new{requestId=id});
Check(Fixture.Inferences==1,"idempotent replay must not infer again");
await Call("POST",path+$"/{decision}/review",200,new{label="purchase_order"});
await Call("POST",path+$"/{decision}/review",409,new{label="sow"});
var history=await Call("GET",path,200);
Check(history["history"]![0]!["review"]!["label"]!.GetValue<string>()=="purchase_order","correction recorded");
Check(history["history"]![0]!["evidence"]!["predictedType"]!.GetValue<string>()=="invoice","original prediction retained");
await Call("GET",path,404,actor:Fixture.ScopedAdmin);
Fixture.Hash=new string('b',64);
await Call("POST",path,409,new{requestId=id});
Fixture.AfterInference=()=> { Fixture.Revoked=true; return Task.CompletedTask; };
await Call("POST",path,404,new{requestId=Guid.NewGuid()}); Fixture.Revoked=false;
Fixture.AfterInference=async () => { await Call("PUT","",200,new{enabled=false,version=2}); };
await Call("POST",path,409,new{requestId=Guid.NewGuid()});
await Call("PUT","",200,new{enabled=true,version=3});
Fixture.AfterInference=()=> { Fixture.Hash=new string('c',64); return Task.CompletedTask; };
await Call("POST",path,409,new{requestId=Guid.NewGuid()});
Fixture.Failure="decision_timeout";
await Call("POST",path,504,new{requestId=Guid.NewGuid()}); Fixture.Failure=null;
var final=await Call("GET",path,200);
Check(final["history"]!.AsArray().Count==1,"failed, revoked and disabled-in-flight decisions must not persist");
await app.StopAsync();
Console.WriteLine($"LAYA_BACKEND_HTTP_AND_DATABASE_CHECKS=PASS ({checks} assertions; synthetic platform boundary substitutes)");
