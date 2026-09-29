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
void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
var assembly=typeof(ProjectFlowHiveProjectControlsRequest).Assembly;
var endpoint=assembly.GetType("ProjectTime.Api.Modules.ProjectFlowHiveEnterpriseModule")!.GetMethod("EnableCustomerSharingAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
var handler=(Func<Guid,HttpContext,CancellationToken,Task<IResult>>)endpoint.CreateDelegate(typeof(Func<Guid,HttpContext,CancellationToken,Task<IResult>>));
var project=Guid.NewGuid();
var unauthorized=await handler(project,new DefaultHttpContext(),CancellationToken.None);
Check(unauthorized is IStatusCodeHttpResult { StatusCode:401 },"real handler rejects an anonymous request before database access");

// Run actual ASP.NET binding locally. This does not run the application or its background workers.
var builder=WebApplication.CreateBuilder(Array.Empty<string>());
builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
await using(var app=builder.Build())
{
    app.MapPut("/fixture/controls",(ProjectFlowHiveProjectControlsRequest request)=>Results.Ok(new {bound=true}));
    app.MapPost("/fixture/projects/{projectId:guid}/customer-sharing/enable",handler);
    await app.StartAsync();
    using var client=new HttpClient{BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
    async Task<HttpStatusCode> Send(HttpMethod method,string path,string payload)
    {using var request=new HttpRequestMessage(method,path){Content=new StringContent(payload,Encoding.UTF8,"application/json")};using var response=await client.SendAsync(request);return response.StatusCode;}
    Check(await Send(HttpMethod.Put,"/fixture/controls","{\"approvedBudget\":\"\",\"customerSharingEnabled\":true}")==HttpStatusCode.BadRequest,
        "full financial DTO reproduces 400 when an unrelated nullable budget arrives as an empty string");
    Check(await Send(HttpMethod.Put,"/fixture/controls","{\"approvedBudget\":null,\"customerSharingEnabled\":true}")==HttpStatusCode.OK,
        "explicit null financial DTO binds correctly");
    foreach(var payload in new[]{"{}","{\"approvedBudget\":\"invalid\",\"canShare\":true}"})
        Check(await Send(HttpMethod.Post,$"/fixture/projects/{project}/customer-sharing/enable",payload)==HttpStatusCode.Unauthorized,
            "new route reaches real authorization without financial DTO binding or trusting body permission claims");
    Check(await Send(HttpMethod.Post,"/fixture/projects/not-a-guid/customer-sharing/enable","{}")==HttpStatusCode.NotFound,"invalid route project does not bind");
    await app.StopAsync();
}
if(args.Contains("--database"))
{
    var config=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FLOWHIVE_SHARING_DB")??throw new Exception("FLOWHIVE_SHARING_DB required"));
    if(config.Host!="127.0.0.1" || config.Database!="flowhive_sharing_test")throw new Exception("Only the named disposable loopback database is permitted");
    var schema="fixture_"+Guid.NewGuid().ToString("N");
    await using var admin=new NpgsqlConnection(config.ConnectionString);await admin.OpenAsync();
    await Sql(admin,$"CREATE SCHEMA {schema};");config.SearchPath=schema;
    try
    {
        await using var db=new NpgsqlConnection(config.ConnectionString);await db.OpenAsync();
        await Sql(db,"CREATE TABLE app_users(user_id uuid PRIMARY KEY);CREATE TABLE projects(project_id uuid PRIMARY KEY);");
        var root=new DirectoryInfo(Directory.GetCurrentDirectory());while(!Directory.Exists(Path.Combine(root.FullName,"database/migrations")))root=root.Parent!;
        var migration=await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/086_module_066_flowhive_enterprise_pm.sql"));
        var table=Regex.Match(migration,@"CREATE TABLE IF NOT EXISTS project_flowhive_project_controls \([\s\S]*?\n\);").Value;
        Check(table.Length>0,"uses the real existing controls table contract");await Sql(db,table);
        var actor=Guid.NewGuid();var other=Guid.NewGuid();var empty=Guid.NewGuid();var rollback=Guid.NewGuid();var concurrent=Guid.NewGuid();
        await Sql(db,$"INSERT INTO app_users VALUES('{actor}');INSERT INTO projects VALUES('{project}'),('{other}'),('{empty}'),('{rollback}'),('{concurrent}');");
        await Sql(db,$"INSERT INTO project_flowhive_project_controls(project_id,updated_by_user_id,approved_budget,expense_budget,contingency_budget,forecast_at_completion,contract_type,currency_code,percent_complete_method,status_report_cadence,financial_notes) VALUES('{project}','{actor}',12500.75,250.25,600.00,13000.50,'fixed_price','USD','effort_weighted','monthly','Preserve private finance note'),('{other}','{actor}',500,20,10,530,'internal','USD','manual','weekly','Other project');");
        var before=await Snapshot(db,project);
        Check(await Enable(db,project,actor),"first enable changes exactly the chosen project");
        var after=await Snapshot(db,project);
        Check(before==after,"all unrelated finance/reporting columns remain byte-identical in DB readback");
        Check(await Scalar<bool>(db,$"SELECT customer_sharing_enabled FROM project_flowhive_project_controls WHERE project_id='{project}';"),"enabled state persists");
        var stamp=await Scalar<DateTime>(db,$"SELECT updated_at FROM project_flowhive_project_controls WHERE project_id='{project}';");
        Check(!await Enable(db,project,actor),"repeated enable is idempotent and reports no change");
        Check(await Scalar<DateTime>(db,$"SELECT updated_at FROM project_flowhive_project_controls WHERE project_id='{project}';")==stamp,"repeated enable does not rewrite actor or timestamp");
        Check(!await Scalar<bool>(db,$"SELECT customer_sharing_enabled FROM project_flowhive_project_controls WHERE project_id='{other}';"),"another project stays internal");
        Check(await Enable(db,empty,actor),"project without controls creates existing safe defaults");
        Check(await Scalar<bool>(db,$"SELECT approved_budget IS NULL AND expense_budget IS NULL AND currency_code='USD' AND financial_notes='' FROM project_flowhive_project_controls WHERE project_id='{empty}';"),"new row retains default finance values");
        Check(await Enable(db,rollback,actor,commit:false),"transaction can stage enablement");
        Check(await Scalar<long>(db,$"SELECT count(*) FROM project_flowhive_project_controls WHERE project_id='{rollback}';")==0,"transaction failure cannot leave partial enablement");
        await using(var replica=new NpgsqlConnection(config.ConnectionString))
        {
            await replica.OpenAsync();var results=await Task.WhenAll(Enable(db,concurrent,actor),Enable(replica,concurrent,actor));
            Check(results.Count(x=>x)==1,"concurrent requests transition only once");
        }
        Check(await Scalar<long>(db,$"SELECT count(*) FROM project_flowhive_project_controls WHERE project_id='{concurrent}';")==1,"no duplicate controls rows");
        Check(await Scalar<bool>(db,"SELECT to_regclass('project_flowhive_customer_shares') IS NULL;"),"enable operation does not need or create a customer link");
    }
    finally {await Sql(admin,$"DROP SCHEMA {schema} CASCADE;");}
}
Console.WriteLine($"FLOWHIVE_CUSTOMER_SHARING_ASSERTIONS={count}; LIVE_CUSTOMER_ACCESS_CHANGES=0; RESULT=PASS");
async Task Sql(NpgsqlConnection db,string sql){await using var command=new NpgsqlCommand(sql,db);await command.ExecuteNonQueryAsync();}
async Task<T> Scalar<T>(NpgsqlConnection db,string sql){await using var command=new NpgsqlCommand(sql,db);return (T)(await command.ExecuteScalarAsync())!;}
Task<string> Snapshot(NpgsqlConnection db,Guid id)=>Scalar<string>(db,$"SELECT (to_jsonb(c)-'customer_sharing_enabled'-'updated_by_user_id'-'updated_at')::text FROM project_flowhive_project_controls c WHERE project_id='{id}';");
async Task<bool> Enable(NpgsqlConnection db,Guid id,Guid actor,bool commit=true)
{
    var method=assembly.GetType("ProjectTime.Api.Modules.ProjectFlowHiveCustomerSharingStore")!.GetMethod("EnableAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
    await using var transaction=await db.BeginTransactionAsync();
    var changed=await (Task<bool>)method.Invoke(null,new object[]{db,transaction,id,actor,CancellationToken.None})!;
    if(commit)await transaction.CommitAsync();else await transaction.RollbackAsync();return changed;
}
