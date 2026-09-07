using System.Reflection;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Modules;

var count = 0;
void Check(bool valid, string name) { if (!valid) throw new Exception("FAILED: " + name); Console.WriteLine("PASSED: " + name); count++; }
var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Directory.GetCurrentDirectory();
var cs = Environment.GetEnvironmentVariable("FLOWHIVE_TEST_DB") ?? throw new Exception("FLOWHIVE_TEST_DB is required; tests never connect to an application database implicitly.");
var config = new NpgsqlConnectionStringBuilder(cs);
if (!(config.Database ?? "").StartsWith("flowhive_execution_test", StringComparison.Ordinal)) throw new Exception("Refusing a non-test database.");
Environment.SetEnvironmentVariable("PTP_DB_HOST", config.Host);
Environment.SetEnvironmentVariable("PTP_DB_PORT", config.Port.ToString());
Environment.SetEnvironmentVariable("PTP_DB_NAME", config.Database);
Environment.SetEnvironmentVariable("PTP_DB_USER", config.Username);
Environment.SetEnvironmentVariable("PTP_DB_PASSWORD", config.Password);
async Task<object?> Sql(string sql, params (string Name, object Value)[] parameters)
{
    await using var c = new NpgsqlConnection(cs); await c.OpenAsync();
    await using var q = new NpgsqlCommand(sql, c);
    foreach (var (name, value) in parameters) q.Parameters.AddWithValue(name, value);
    return await q.ExecuteScalarAsync();
}
string Block(string path, string start, string end)
{
    var text = File.ReadAllText(Path.Combine(root, path)); var offset = text.IndexOf(start, StringComparison.Ordinal);
    if (offset < 0) throw new Exception("Fixture source not found: " + start);
    var finish = text.IndexOf(end, offset, StringComparison.Ordinal);
    return text[offset..(finish + end.Length)];
}
await Sql("""
    CREATE EXTENSION IF NOT EXISTS pgcrypto;
    CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT,applied_at TIMESTAMPTZ);
    CREATE TABLE projects(project_id UUID PRIMARY KEY);
    CREATE TABLE app_users(user_id UUID PRIMARY KEY);
    CREATE TABLE project_flowhive_plans(plan_id UUID PRIMARY KEY);
    """);
await Sql(Block("database/migrations/086_module_066_flowhive_enterprise_pm.sql", "CREATE TABLE IF NOT EXISTS project_flowhive_working_copies (", "\n);"));
await Sql(Block("database/migrations/086_module_066_flowhive_enterprise_pm.sql", "CREATE OR REPLACE FUNCTION projectpulse086_touch_working_copy()", "FOR EACH ROW EXECUTE FUNCTION projectpulse086_touch_working_copy();"));
await Sql(Block("database/migrations/095_project_planning_collaboration_access.sql", "CREATE TABLE IF NOT EXISTS project_flowhive_ai_planner_runs (", "\n);"));
await Sql("""
    CREATE UNIQUE INDEX ux_test_active_actor ON project_flowhive_ai_planner_runs(project_id,actual_actor_user_id) WHERE status IN ('queued','processing','generating');
    CREATE TABLE project_intake_documents(project_intake_document_id UUID,project_id UUID,document_category TEXT,
        original_file_name TEXT,pulse_ai_processing_status TEXT,pulse_ai_processing_error_code TEXT,pulse_ai_active_version_id UUID,
        work_register_document_id UUID,pulse_ai_effective_at TIMESTAMPTZ,uploaded_at TIMESTAMPTZ,is_active BOOLEAN,engineering_visible BOOLEAN);
    CREATE TABLE work_register_documents(work_register_document_id UUID,document_type TEXT,status TEXT,upload_source TEXT,stored_file_path TEXT);
    CREATE TABLE pulse_ai_document_versions(pulse_ai_document_version_id UUID,authority_status TEXT,index_status TEXT,source_sha256 TEXT,document_version TEXT);
    CREATE TABLE pulse_ai_document_chunks(pulse_ai_document_version_id UUID,is_active BOOLEAN,index_status TEXT,section_title TEXT,citation_anchor TEXT,chunk_text TEXT);
    CREATE TABLE pulse_ai_document_processing_events(project_intake_document_id UUID,evidence_json JSONB,diagnostic_code TEXT,created_at TIMESTAMPTZ);
    """);
var migration = File.ReadAllText(Path.Combine(root, "database/migrations/104_flowhive_bounded_ai_execution.sql"));
await Sql(migration); await Sql(migration);
Check((long)(await Sql("SELECT count(*) FROM schema_migrations WHERE migration_id='104_flowhive_bounded_ai_execution';"))! == 1, "migration is idempotent");
var project = Guid.NewGuid(); var actor = Guid.NewGuid();
await Sql("INSERT INTO projects VALUES(@p); INSERT INTO app_users VALUES(@a);", ("p", project), ("a", actor));
var seed = new ProjectFlowHivePlanRequest(project,"TEST-104","Synthetic execution test","Test customer","Test plan","draft",
    new DateOnly(2026,9,7),new DateOnly(2026,10,7),
    [new(Guid.NewGuid(),null,"1",null,"Plan","Phase summary.",0,false,"ASAP",null,0m,0m,"not_started",IsSummary:true,Phase:"Plan"),
     new(Guid.NewGuid(),null,"1.1","1","Validate the test fixture","Test-only work package.",1,false,"ASAP",null,0m,2m,"not_started",Phase:"Plan")],
    [],[new("1.1",null,"Test role",100m,2m)],null,"sow-v1","Test note");
var validation = ProjectFlowHiveScheduleEngine.Validate(seed);
var schedule = ProjectFlowHiveScheduleEngine.Calculate(seed);
if (!validation.Valid || !schedule.Valid)
    Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { validation.Issues, ScheduleIssues = schedule.Issues }));
Check(validation.Valid && schedule.Valid, "real scheduler accepts execution fixture");
var fingerprint = ProjectFlowHiveExecutionPolicy.Fingerprint(seed,actor,actor,"scope","comprehensive","source");
Check(fingerprint == ProjectFlowHiveExecutionPolicy.Fingerprint(seed,actor,actor,"scope","comprehensive","source"), "input fingerprints are deterministic");
Check(fingerprint != ProjectFlowHiveExecutionPolicy.Fingerprint(seed with { ProjectEndDate = new DateOnly(2026,11,1) },actor,actor,"scope","comprehensive","source"), "changed dates invalidate identity");
Check(fingerprint != ProjectFlowHiveExecutionPolicy.Fingerprint(seed,Guid.NewGuid(),actor,"scope","comprehensive","source"), "actor scope participates in identity");
Check(fingerprint != ProjectFlowHiveExecutionPolicy.Fingerprint(seed,actor,actor,"different","comprehensive","source"), "requested scope participates in identity");
Check(!ProjectFlowHiveExecutionPolicy.CanAttempt(2,DateTimeOffset.UtcNow.AddMinutes(1),DateTimeOffset.UtcNow), "two-attempt budget is terminal");
Check(!ProjectFlowHiveExecutionPolicy.CanAttempt(0,DateTimeOffset.UtcNow.AddSeconds(-1),DateTimeOffset.UtcNow), "expired operation cannot attempt inference");
Check(!ProjectFlowHiveExecutionPolicy.MatchesWorkingCopy(null,Guid.NewGuid()), "null starting version is not an overwrite wildcard");
var module = typeof(ProjectFlowHiveExecutionPolicy).Assembly.GetType("ProjectTime.Api.Modules.ProjectFlowHiveAiPlannerOrchestrationModule")!;
var save = module.GetMethod("SaveWorkingCopyAsync",BindingFlags.NonPublic|BindingFlags.Static)!;
async Task<object?> Invoke(string method, params object?[] args)
{
    var task = (Task)module.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;
    await task; return task.GetType().GetProperty("Result")?.GetValue(task);
}
async Task<bool> Save(Guid? expected, string note)
{
    await using var c = new NpgsqlConnection(cs); await c.OpenAsync(); await using var t = await c.BeginTransactionAsync();
    var task = (Task)save.Invoke(null,new object?[] { c,t,project,seed with { Notes=note },actor,expected,validation,schedule,CancellationToken.None })!;
    await task; var result=task.GetType().GetProperty("Result")!.GetValue(task);
    await t.CommitAsync(); return result is not null;
}
Check(await Save(null,"initial"), "new working copy creates once");
Check(!await Save(null,"must not overwrite"), "new-copy race never overwrites an existing copy");
var startingVersion=(Guid)(await Sql("SELECT row_version FROM project_flowhive_working_copies WHERE project_id=@p",("p",project)))!;
var saves=await Task.WhenAll(Save(startingVersion,"editor one"),Save(startingVersion,"editor two"));
Check(saves.Count(x=>x)==1, "exactly one concurrent editor can save the same revision");
Check(!await Save(startingVersion,"late AI result"), "stale AI output cannot overwrite newer edits");
var currentVersion=(Guid)(await Sql("SELECT row_version FROM project_flowhive_working_copies WHERE project_id=@p",("p",project)))!;
var accessType=module.GetNestedType("PlannerAccess",BindingFlags.NonPublic)!;
var access=Activator.CreateInstance(accessType,actor,actor)!;
async Task<Guid> Queue(string outcome, Guid? expected)
{
    await using var c=new NpgsqlConnection(cs); await c.OpenAsync();
    return (Guid)(await Invoke("GetOrCreateRunAsync",c,project,
        new ProjectFlowHiveAiPlannerRunRequest(seed,outcome,"comprehensive",false,expected,true),access,"test-run",CancellationToken.None))!;
}
var run=await Queue("scope",currentVersion);
Check(await Queue("scope",currentVersion)==run,"duplicate clicks reuse the exact durable run");
try { await Queue("changed scope",currentVersion); throw new Exception("A conflicting run was accepted"); }
catch (Exception e) when(e.GetType().Name=="PlannerConflict") { Check(true,"different active inputs produce a conflict instead of another job"); }
Check((long)(await Sql("SELECT count(*) FROM project_flowhive_ai_planner_runs"))! == 1,"one click sequence creates one durable operation");
Check((double)(await Sql("SELECT EXTRACT(EPOCH FROM deadline_at-created_at)::double precision FROM project_flowhive_ai_planner_runs WHERE run_id=@r",("r",run)))! <= 301,"deadline is stored at creation");
foreach(var sql in new[] {
    "UPDATE project_flowhive_ai_planner_runs SET deadline_at=deadline_at+INTERVAL '1 hour' WHERE run_id=@r",
    "UPDATE project_flowhive_ai_planner_runs SET attempt_count=3 WHERE run_id=@r",
    "UPDATE project_flowhive_ai_planner_runs SET requested_outcome='mutated' WHERE run_id=@r" })
{
    try { await Sql(sql,("r",run)); throw new Exception("Execution fence failed"); }
    catch(PostgresException) { Check(true,"database rejects execution budget/input mutation"); }
}
await using(var c=new NpgsqlConnection(cs))
{
    await c.OpenAsync();
    await Invoke("StopRunAsync",c,run,"cancelled","test cancellation",CancellationToken.None);
    await Invoke("UpdateRunAsync",c,run,"processing","ai_route_retry",70,Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),null,null,null,CancellationToken.None,false,null);
}
Check((string)(await Sql("SELECT phase FROM project_flowhive_ai_planner_runs WHERE run_id=@r",("r",run)))! == "cancelled","late retry cannot revive a cancelled run");
try { await Sql("UPDATE project_flowhive_ai_planner_runs SET status='generating' WHERE run_id=@r",("r",run)); throw new Exception("Revived terminal run"); }
catch(PostgresException) { Check(true,"database also fences terminal-state resurrection"); }
var expired=Guid.NewGuid();
await Sql("""
    INSERT INTO project_flowhive_ai_planner_runs(run_id,project_id,status,phase,requested_plan,actual_actor_user_id,effective_actor_user_id,execution_contract,deadline_at)
    VALUES(@r,@p,'generating','extract_and_expand_work_packages','{}',@a,@a,@contract,NOW()-INTERVAL '1 second');
    """,("r",expired),("p",project),("a",actor),("contract",ProjectFlowHiveExecutionPolicy.Contract));
// Exercise the actual final UPDATE and working-copy write in a transaction. Deadline failure rolls both back.
await using(var c=new NpgsqlConnection(cs))
{
    await c.OpenAsync(); await using var transaction=await c.BeginTransactionAsync();
    var saved=(Task)save.Invoke(null,new object?[] {c,transaction,project,seed with {Notes="must roll back"},actor,currentVersion,validation,schedule,CancellationToken.None})!;
    await saved;
    try {
        await Invoke("RecordWorkingCopyReceiptAsync",c,transaction,expired,currentVersion,1,CancellationToken.None);
        await Invoke("UpdateRunAsync",c,expired,"completed","working_draft_ready",100,Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),seed,schedule,validation,CancellationToken.None,true,transaction);
        throw new Exception("Expired completion was accepted");
    } catch(TimeoutException) { await transaction.RollbackAsync(); Check(true,"deadline during commit rolls back the working-copy transaction"); }
}
Check((Guid)(await Sql("SELECT row_version FROM project_flowhive_working_copies WHERE project_id=@p",("p",project)))! == currentVersion,"failed finalization preserves previous working revision");
await Invoke("ExpireRunsAsync",CancellationToken.None);
Check((string)(await Sql("SELECT phase FROM project_flowhive_ai_planner_runs WHERE run_id=@r",("r",expired)))! == "deadline_exceeded","independent watchdog ends abandoned runs");
Check(await Sql("SELECT saved_working_row_version FROM project_flowhive_ai_planner_runs WHERE run_id=@r",("r",expired)) is DBNull,
    "failed transaction never leaves a successful readback receipt");
var successful = await Queue("receipt verification",currentVersion);
Guid savedVersion;
int savedRevision;
await using(var c=new NpgsqlConnection(cs))
{
    await c.OpenAsync(); await using var transaction=await c.BeginTransactionAsync();
    var saved = (await Invoke("SaveWorkingCopyAsync",c,transaction,project,seed,actor,currentVersion,validation,schedule,CancellationToken.None))!;
    savedVersion=(Guid)saved.GetType().GetProperty("RowVersion")!.GetValue(saved)!;
    savedRevision=(int)saved.GetType().GetProperty("WorkingRevision")!.GetValue(saved)!;
    await Invoke("RecordWorkingCopyReceiptAsync",c,transaction,successful,savedVersion,savedRevision,CancellationToken.None);
    await Invoke("UpdateRunAsync",c,successful,"completed","working_draft_ready",100,Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),seed,schedule,validation,CancellationToken.None,true,transaction);
    await transaction.CommitAsync();
    var loaded=(await Invoke("LoadRunAsync",c,project,successful,CancellationToken.None))!;
    var response=module.GetMethod("ToResponse",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[loaded]);
    var projection=JsonSerializer.SerializeToElement(response,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    var receipt=projection.GetProperty("workingDraft");
    Check(receipt.GetProperty("persisted").GetBoolean(),"successful finalization exposes a committed working draft");
    Check(receipt.GetProperty("rowVersion").GetGuid()==savedVersion && receipt.GetProperty("workingRevision").GetInt32()==savedRevision,
        "API readback receipt identifies the exact committed working copy");
}
Check((Guid)(await Sql("SELECT row_version FROM project_flowhive_working_copies WHERE project_id=@p",("p",project)))! == savedVersion,
    "saved working-copy row version reconciles with the terminal run receipt");
try { await Sql("UPDATE project_flowhive_ai_planner_runs SET saved_working_revision=saved_working_revision+1 WHERE run_id=@r",("r",successful)); throw new Exception("Receipt mutation accepted"); }
catch(PostgresException) { Check(true,"database prevents rewriting the committed readback receipt"); }
try { await Sql(File.ReadAllText(Path.Combine(root,"database/rollback/104_flowhive_bounded_ai_execution_rollback.sql"))); throw new Exception("Destructive rollback was accepted"); }
catch(PostgresException) { Check(true,"rollback preserves execution evidence after use"); }
Console.WriteLine($"FLOWHIVE_EXECUTION_ASSERTIONS_PASSED={count}");
