using System.Data.Common;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;

// This exact-incident diagnostic performs only a bounded READ ONLY transaction.
// It never prints credentials, source text, identity IDs, SQL errors or raw rows.
const string expectedSource="26d34e5abad4a81f0158e4f40af53fcdaf4ef03b";
var failed=false;
try
{
    if(Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT")!="test"
        ||Environment.GetEnvironmentVariable("PROJECTPULSE_SOURCE_COMMIT")!=expectedSource)
        throw new InvalidOperationException("wrong_installed_test_source");
    AssemblyLoadContext.Default.Resolving+=(_,name)=>
    {
        if(name.Name is null || !Regex.IsMatch(name.Name,@"^[A-Za-z0-9_.-]+$"))return null;
        var path=Path.Combine("/app",name.Name+".dll");
        return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;
    };
    var api=AssemblyLoadContext.Default.LoadFromAssemblyPath("/app/ProjectTime.Api.dll");
    var configType=api.GetType("ProjectTime.Api.Modules.ProjectFlowHiveDatabaseConfig",true)!;
    var config=configType.GetMethod("FromEnvironment",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,null)!;
    var connectionString=(string)configType.GetProperty("ConnectionString")!.GetValue(config)!;
    if(string.IsNullOrWhiteSpace(connectionString))throw new InvalidOperationException("database_configuration_missing");
    var pg=AssemblyLoadContext.Default.LoadFromAssemblyPath("/app/Npgsql.dll");
    await using var connection=(DbConnection)Activator.CreateInstance(pg.GetType("Npgsql.NpgsqlConnection",true)!,connectionString)!;
    connectionString="";
    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await connection.OpenAsync(deadline.Token);
    await using var transaction=await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,deadline.Token);
    await using(var mode=connection.CreateCommand())
    {
        mode.Transaction=transaction;mode.CommandText="SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='5000'; SET LOCAL lock_timeout='1000';";
        await mode.ExecuteNonQueryAsync(deadline.Token);
    }
    await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandTimeout=5;
    command.CommandText="""
        SELECT status,phase,attempt_count,blockers::text,warnings::text,
               generated_plan IS NOT NULL, schedule_payload IS NOT NULL,
               validation_payload IS NOT NULL,
               COALESCE(validation_payload::text,'{}')
        FROM project_flowhive_ai_planner_runs
        WHERE updated_at >= TIMESTAMPTZ '2026-10-01T03:49:00Z'
          AND updated_at <= TIMESTAMPTZ '2026-10-01T03:52:00Z'
        ORDER BY updated_at DESC LIMIT 4;
        """;
    var rows=new List<object>();
    await using(var reader=await command.ExecuteReaderAsync(deadline.Token))
    {
        while(await reader.ReadAsync(deadline.Token))
        {
            string Code(string value)=>Regex.IsMatch(value,"^[a-z0-9_]{1,100}$")?value:"unrecognized";
            object Summarize(string text)
            {
                if(text.Length>65536)return new {state="over_budget"};
                using var json=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=16});
                var fields=new List<object>();
                if(json.RootElement.ValueKind==JsonValueKind.Array)
                    foreach(var e in json.RootElement.EnumerateArray().Take(16))
                    {
                        if(e.ValueKind!=JsonValueKind.String)continue;var message=e.GetString()??"";
                        var code=Regex.IsMatch(message,"^(provider_|private_provider_|module025_|flowhive_|project_planning_|document_)[a-z0-9_]{1,180}$")?message:"free_text_not_exported";
                        var keywords=new[]{"citation","source","evidence","refusal","timeout","deadline","quality","phase","version","schedule","missing","task","private","model","budget"}
                            .Where(k=>message.Contains(k,StringComparison.OrdinalIgnoreCase)).ToArray();
                        fields.Add(new {code,keywords});
                    }
                return new {state="projected",items=fields};
            }
            using var validation=JsonDocument.Parse(reader.GetString(8));
            rows.Add(new {status=Code(reader.GetString(0)),phase=Code(reader.GetString(1)),attempts=reader.GetInt16(2),
                blockers=Summarize(reader.GetString(3)),warnings=Summarize(reader.GetString(4)),
                generatedPlanPresent=reader.GetBoolean(5),schedulePresent=reader.GetBoolean(6),validationPresent=reader.GetBoolean(7),
                validationValid=validation.RootElement.TryGetProperty("valid",out var valid)&&valid.ValueKind==JsonValueKind.True});
        }
    }
    await transaction.RollbackAsync(deadline.Token);
    Console.WriteLine(JsonSerializer.Serialize(new {source=expectedSource,readOnly=true,incidentWindow="2026-10-01T03:49Z..03:52Z",rows,rawContentPublished=false}));
}
catch(Exception e)
{
    failed=true;
    Console.WriteLine(JsonSerializer.Serialize(new {readOnly=true,status="diagnostic_unavailable",errorType=e.GetType().Name,rawContentPublished=false}));
}
return failed?1:0;
