using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
    CREATE TABLE projects(project_id UUID PRIMARY KEY,project_code TEXT NOT NULL DEFAULT 'TEST',project_name TEXT NOT NULL DEFAULT 'Synthetic test project',status TEXT NOT NULL DEFAULT 'active',project_manager_user_id UUID NULL,account_executive_user_id UUID NULL,solution_architect_user_id UUID NULL);
    CREATE TABLE app_users(user_id UUID PRIMARY KEY,display_name TEXT NOT NULL DEFAULT '',email TEXT NOT NULL DEFAULT '',is_active BOOLEAN NOT NULL DEFAULT TRUE,team_name TEXT NOT NULL DEFAULT '',department_name TEXT NOT NULL DEFAULT '',department TEXT NOT NULL DEFAULT '');
    CREATE TABLE app_roles(app_role_id UUID PRIMARY KEY,role_code TEXT NOT NULL,is_active BOOLEAN NOT NULL DEFAULT TRUE);
    CREATE TABLE app_permissions(app_permission_id UUID PRIMARY KEY,permission_code TEXT NOT NULL);
    CREATE TABLE app_user_role_assignments(user_id UUID,app_role_id UUID,is_active BOOLEAN NOT NULL DEFAULT TRUE);
    CREATE TABLE app_role_permissions(app_role_id UUID,app_permission_id UUID);
    CREATE TABLE project_assignments(project_assignment_id UUID PRIMARY KEY,project_id UUID,task_id UUID,user_id UUID,assigned_by_user_id UUID,effective_start_date DATE,effective_end_date DATE,allocation_percent NUMERIC,assigned_hours NUMERIC,is_primary_assignee BOOLEAN,updated_by_user_id UUID);
    CREATE TABLE project_planning_collaborators(project_id UUID,user_id UUID,module_code TEXT,collaboration_level TEXT,is_active BOOLEAN,effective_start_date DATE,effective_end_date DATE);
    CREATE TABLE reporting_relationships(employee_user_id UUID,manager_user_id UUID,team_lead_user_id UUID,effective_start_date DATE,effective_end_date DATE);
    CREATE TABLE projectpulse_team_scope_assignments(scoped_user_id UUID,is_active BOOLEAN,scope_type TEXT,manager_user_id UUID,team_name TEXT,department_name TEXT);
    CREATE TABLE project_tasks(task_id UUID PRIMARY KEY,project_id UUID,task_code TEXT,task_name TEXT,task_description TEXT,billable BOOLEAN,is_active BOOLEAN,revision_number INTEGER,updated_by_user_id UUID);
    CREATE TABLE project_flowhive_plans(plan_id UUID PRIMARY KEY);
    CREATE TABLE project_forge_plans(plan_id UUID PRIMARY KEY,project_id UUID,plan_name TEXT,plan_status TEXT,source_kind TEXT,revision_number INTEGER,adopted_by_user_id UUID NULL,adopted_at TIMESTAMPTZ NULL,review_notes TEXT NOT NULL DEFAULT '',updated_by_user_id UUID,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
    CREATE TABLE project_forge_plan_tasks(plan_task_id UUID PRIMARY KEY,plan_id UUID,project_id UUID,wbs_code TEXT,parent_wbs_code TEXT,task_name TEXT,task_description TEXT,task_type TEXT,phase_name TEXT,priority_code TEXT,task_status TEXT,kanban_category TEXT,decision_action TEXT,planned_start_date DATE,planned_end_date DATE,duration_working_days INTEGER,recurrence_rule JSONB,percent_complete NUMERIC,estimated_hours NUMERIC,hourly_rate NUMERIC,material_units NUMERIC,material_unit_cost NUMERIC,fixed_cost NUMERIC,travel_cost NUMERIC,equipment_cost NUMERIC,miscellaneous_cost NUMERIC,is_important BOOLEAN,is_urgent BOOLEAN,reviewer_user_id UUID NULL,source_kind TEXT,ai_correlation_id TEXT NULL,canonical_task_id UUID NULL,blocked_reason TEXT,display_order INTEGER,revision_number INTEGER,updated_by_user_id UUID,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
    CREATE TABLE project_forge_plan_assignments(plan_id UUID,plan_task_id UUID,project_id UUID,user_id UUID,assignment_type TEXT,review_status TEXT,reviewed_task_revision INTEGER NULL);
    CREATE TABLE project_forge_task_details(task_id UUID PRIMARY KEY,project_id UUID,source_plan_task_id UUID NULL,parent_task_id UUID NULL,task_type TEXT,phase_name TEXT,priority_code TEXT,task_status TEXT,kanban_category TEXT,decision_action TEXT,planned_start_date DATE,planned_end_date DATE,duration_working_days INTEGER,display_order INTEGER,blocked_reason TEXT,recurrence_rule JSONB,percent_complete NUMERIC,estimated_hours NUMERIC,hourly_rate NUMERIC,material_units NUMERIC,material_unit_cost NUMERIC,fixed_cost NUMERIC,travel_cost NUMERIC,equipment_cost NUMERIC,miscellaneous_cost NUMERIC,is_important BOOLEAN,is_urgent BOOLEAN,source_kind TEXT,ai_correlation_id TEXT,created_by_user_id UUID,updated_by_user_id UUID,revision_number INTEGER,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
    CREATE TABLE project_forge_task_dependencies(dependency_id UUID PRIMARY KEY,plan_id UUID,predecessor_plan_task_id UUID,successor_plan_task_id UUID,dependency_type TEXT,lag_working_days INTEGER,created_at TIMESTAMPTZ DEFAULT NOW());
    CREATE TABLE project_task_dependencies(project_task_dependency_id UUID PRIMARY KEY,project_id UUID,predecessor_task_id UUID,successor_task_id UUID,dependency_type TEXT,lag_working_days INTEGER,created_by_user_id UUID,updated_by_user_id UUID);
    CREATE TABLE project_forge_audit_events(audit_event_id UUID PRIMARY KEY,project_id UUID,plan_id UUID NULL,plan_task_id UUID NULL,event_code TEXT,entity_type TEXT,entity_id UUID,actual_actor_user_id UUID,effective_actor_user_id UUID,event_metadata JSONB,correlation_id TEXT);
    CREATE TABLE enterprise_notification_policies(policy_code TEXT PRIMARY KEY,enabled BOOLEAN NOT NULL DEFAULT TRUE);
    CREATE TABLE enterprise_notification_events(enterprise_notification_event_id UUID PRIMARY KEY,policy_code TEXT,source_module TEXT,source_event_id TEXT,idempotency_key TEXT UNIQUE,entity_type TEXT,entity_id UUID,project_id UUID,subject_user_id UUID NULL,occurred_at TIMESTAMPTZ,available_at TIMESTAMPTZ,payload JSONB,ingestion_source TEXT,event_status TEXT);
    CREATE TABLE enterprise_notification_event_history(enterprise_notification_event_history_id UUID PRIMARY KEY,enterprise_notification_event_id UUID,history_code TEXT,event_status TEXT,diagnostic_code TEXT,history_metadata JSONB,correlation_id TEXT);
    CREATE TABLE time_entries(time_entry_id UUID PRIMARY KEY,project_id UUID,task_id UUID NULL,hours NUMERIC,status TEXT,work_date DATE);
    """);
await Sql(Block("database/migrations/086_module_066_flowhive_enterprise_pm.sql", "CREATE TABLE IF NOT EXISTS project_flowhive_working_copies (", "\n);"));
await Sql(Block("database/migrations/086_module_066_flowhive_enterprise_pm.sql", "CREATE OR REPLACE FUNCTION projectpulse086_touch_working_copy()", "FOR EACH ROW EXECUTE FUNCTION projectpulse086_touch_working_copy();"));
await Sql("""
    CREATE TABLE IF NOT EXISTS project_flowhive_project_controls(
        project_id UUID PRIMARY KEY,
        currency_code TEXT NULL,
        approved_budget NUMERIC NULL,
        forecast_at_completion NUMERIC NULL,
        updated_by_user_id UUID NULL
    );
    INSERT INTO schema_migrations(migration_id,description,applied_at)
    VALUES('086_module_066_flowhive_enterprise_pm','Synthetic fixture readiness marker for the extracted 086 schema','2026-09-08T00:00:00Z')
    ON CONFLICT (migration_id) DO NOTHING;
    """);
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
await Sql("INSERT INTO projects(project_id,project_code,project_name,status) VALUES(@p,'TEST-104','Synthetic execution project','active'); INSERT INTO app_users(user_id,display_name,email,is_active) VALUES(@a,'Synthetic Administrator','synthetic@example.invalid',TRUE);", ("p", project), ("a", actor));
var administratorRole = Guid.NewGuid();
await Sql("INSERT INTO app_roles(app_role_id,role_code,is_active) VALUES(@role,'ADMINISTRATOR',TRUE); INSERT INTO app_user_role_assignments(user_id,app_role_id,is_active) VALUES(@a,@role,TRUE); INSERT INTO enterprise_notification_policies(policy_code,enabled) VALUES('PROJECT_FORGE_PLAN_UPDATED',TRUE),('PROJECT_FORGE_TASK_ASSIGNED',TRUE);", ("role", administratorRole), ("a", actor));
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
Check(ProjectFlowHiveExecutionPolicy.CanRetry(1,DateTimeOffset.UtcNow.AddMinutes(5),DateTimeOffset.UtcNow), "a first transient failure can retry when the full bounded request still fits");
Check(!ProjectFlowHiveExecutionPolicy.CanRetry(1,DateTimeOffset.UtcNow.AddMinutes(4).AddSeconds(20),DateTimeOffset.UtcNow), "a late transient failure cannot start a retry that would outlive the run");
Check(!ProjectFlowHiveExecutionPolicy.CanRetry(2,DateTimeOffset.UtcNow.AddMinutes(5),DateTimeOffset.UtcNow), "the retry budget remains capped at two attempts");
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
// These checks execute only against the explicitly supplied disposable database.
// They exercise the actual atomic review write helper, not substitute SQL success.
var reviewMigration=File.ReadAllText(Path.Combine(root,"database/migrations/105_flowhive_reviewed_regeneration.sql"));
await Sql(reviewMigration);await Sql(reviewMigration);
Check((long)(await Sql("SELECT count(*) FROM schema_migrations WHERE migration_id='105_flowhive_reviewed_regeneration'"))! == 1,"review migration is idempotent");
var reviewRun=await Queue("explicit reviewed proposal",savedVersion);
await using(var c=new NpgsqlConnection(cs))
{
    await c.OpenAsync();
    await Invoke("UpdateRunAsync",c,reviewRun,"completed","candidate_review_required",100,Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),seed,schedule,validation,CancellationToken.None,true,null);
    var loaded=(await Invoke("LoadRunAsync",c,project,reviewRun,CancellationToken.None))!;
    var projected=JsonSerializer.SerializeToElement(module.GetMethod("ToResponse",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[loaded]),new JsonSerializerOptions(JsonSerializerDefaults.Web));
    Check(projected.GetProperty("candidateAvailable").GetBoolean() && projected.GetProperty("candidate").GetProperty("persisted").GetBoolean(),"staged proposal has an observable durable candidate receipt");
    Check(!projected.GetProperty("workingDraft").GetProperty("persisted").GetBoolean(),"staged proposal cannot masquerade as an adopted working copy");
}
Check((Guid)(await Sql("SELECT row_version FROM project_flowhive_working_copies WHERE project_id=@p",("p",project)))! == savedVersion,"candidate staging does not rewrite the working plan");
var reviewChoices=new[]{new ProjectFlowHiveExistingTaskDecision("1.1","1.1")};
var reviewed=ProjectFlowHivePlannerReview.Merge(reviewRun,seed,seed,reviewChoices);
var reviewNote="Explicit synthetic mapping approved for this database transaction test.";
var previewHash=ProjectFlowHivePlannerReview.Fingerprint(reviewRun,savedVersion,reviewed,reviewChoices,reviewNote);
var reviewRequest=new ProjectFlowHivePlannerReviewRequest(savedVersion,reviewChoices,reviewNote,previewHash);
async Task CommitReview(Guid candidateRun,ProjectFlowHivePlannerReviewRequest request)
{
    await using var c=new NpgsqlConnection(cs);await c.OpenAsync();await using var t=await c.BeginTransactionAsync();
    try {
        await Invoke("CommitReviewedCandidateAsync",c,t,candidateRun,project,actor,seed,seed,reviewed,request,
            reviewNote,previewHash,ProjectFlowHiveScheduleEngine.Validate(reviewed),ProjectFlowHiveScheduleEngine.Calculate(reviewed),CancellationToken.None);
        await t.CommitAsync();
    } catch { await t.RollbackAsync();throw; }
}
await CommitReview(reviewRun,reviewRequest);
var reviewedVersion=(Guid)(await Sql("SELECT row_version FROM project_flowhive_working_copies WHERE project_id=@p",("p",project)))!;
Check(reviewedVersion!=savedVersion,"explicit review advances the working revision exactly once");
Check((Guid)(await Sql("SELECT applied_row_version FROM project_flowhive_ai_plan_reviews WHERE run_id=@r",("r",reviewRun)))! == reviewedVersion,"immutable review receipt matches the saved working revision");
Check((Guid)(await Sql("SELECT saved_working_row_version FROM project_flowhive_ai_planner_runs WHERE run_id=@r",("r",reviewRun)))! == reviewedVersion,"terminal run receipt and review audit reconcile");
Check((bool)(await Sql("SELECT prior_plan=@prior::jsonb AND candidate_plan=@prior::jsonb AND applied_plan=@applied::jsonb FROM project_flowhive_ai_plan_reviews WHERE run_id=@r",
    ("r",reviewRun),("prior",JsonSerializer.Serialize(seed,new JsonSerializerOptions(JsonSerializerDefaults.Web))),
    ("applied",JsonSerializer.Serialize(reviewed,new JsonSerializerOptions(JsonSerializerDefaults.Web)))))!,"immutable review retains complete prior, candidate and applied snapshots");
try {await CommitReview(reviewRun,reviewRequest);throw new Exception("Stale duplicate review was written");}
catch(InvalidOperationException){Check(true,"a stale or duplicate direct commit cannot replace newer work");}
Check((Guid)(await Sql("SELECT row_version FROM project_flowhive_working_copies WHERE project_id=@p",("p",project)))! == reviewedVersion,"rejected duplicate leaves the saved revision unchanged");
foreach(var mutation in new[]{"UPDATE project_flowhive_ai_plan_reviews SET review_note='changed later' WHERE run_id=@r", "DELETE FROM project_flowhive_ai_plan_reviews WHERE run_id=@r"})
{
    try{await Sql(mutation,("r",reviewRun));throw new Exception("Review audit mutation allowed");}
    catch(PostgresException){Check(true,"review audit rejects later mutation or deletion");}
}
var interruptedReview=await Queue("interrupted reviewed proposal",reviewedVersion);
await using(var c=new NpgsqlConnection(cs))
{
    await c.OpenAsync();
    await Invoke("UpdateRunAsync",c,interruptedReview,"needs_attention","cancelled",100,Array.Empty<string>(),Array.Empty<string>(),Array.Empty<string>(),seed,schedule,validation,CancellationToken.None,true,null);
}
try{await CommitReview(interruptedReview,reviewRequest with {ExpectedWorkingRowVersion=reviewedVersion});throw new Exception("Cancelled review committed");}
catch(InvalidOperationException){Check(true,"failed final review guard rolls back working-copy and audit writes together");}
Check((Guid)(await Sql("SELECT row_version FROM project_flowhive_working_copies WHERE project_id=@p",("p",project)))! == reviewedVersion,"failed review preserves the prior working revision");
Check((long)(await Sql("SELECT count(*) FROM project_flowhive_ai_plan_reviews WHERE run_id=@r",("r",interruptedReview)))! == 0,"failed review does not leave a false immutable success receipt");
try{await Sql(File.ReadAllText(Path.Combine(root,"database/rollback/105_flowhive_reviewed_regeneration_rollback.sql")));throw new Exception("Destructive review rollback allowed");}
catch(PostgresException){Check(true,"migration rollback cannot remove retained review evidence");}
var readbackTaskOne = Guid.NewGuid();
var readbackTaskTwo = Guid.NewGuid();
ProjectFlowHiveRateSource InternalRate(decimal amount, string currency = "USD") =>
    new(amount, "internal_labor_cost", currency, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "synthetic_internal_cost_authority", true);
var financialSources = new[]
{
    new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-001", 10m, 10m, 7m, true, InternalRate(100m)),
    new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskTwo, "CAN-002", 8m, 8m, 6m, true, InternalRate(80m))
};
var approvedTimeSources = new[]
{
    new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 3m, "pm_approved", new DateOnly(2026, 9, 8)),
    new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskTwo, 2m, "locked", new DateOnly(2026, 9, 8)),
    new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 1m, "draft")
};
var financialReadback = ProjectFlowHiveFinancialReadback.Calculate(1000m, null, financialSources, approvedTimeSources);
Check(financialReadback.LoggedHours == 6m && financialReadback.ApprovedHours == 5m, "readback separates logged and approved time");
Check(financialReadback.OriginalEstimateHours == 18m && financialReadback.ApprovedEstimateHours == 18m, "readback preserves original and approved estimates");
Check(financialReadback.BudgetHoursRemaining == 13m && financialReadback.CurrentEstimateToCompleteHours == 13m, "readback separates budget hours remaining and current estimate to complete");
Check(financialReadback.ApprovedLaborCost == 460m && financialReadback.KnownApprovedLaborCost == 460m, "readback calculates verified internal labor cost from authoritative rate basis");
Check(financialReadback.BudgetRemainingAfterActualCosts == 540m && financialReadback.BudgetRemainingAfterKnownActualCosts == 540m, "readback separates budget remaining after actual labor cost");
Check(financialReadback.ForecastAtCompletion == 1640m && financialReadback.ForecastVariance == -640m && financialReadback.ForecastSource == "derived", "readback separates derived forecast variance from budget remaining");
Check(financialReadback.DerivedAssumptions.Any(reason => reason.Contains("expenses and commitments", StringComparison.Ordinal)), "derived forecast identifies excluded expenses and commitments");
var replayedReadback = ProjectFlowHiveFinancialReadback.Calculate(1000m, null, financialSources, approvedTimeSources);
Check(JsonSerializer.Serialize(financialReadback) == JsonSerializer.Serialize(replayedReadback), "readback replay is deterministic and idempotent");
var overrunEstimate = ProjectFlowHiveFinancialReadback.Calculate(1000m, null,
    [new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-OVERRUN", 10m, 10m, 4m, true, InternalRate(100m))],
    [new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 12m, "pm_approved")]);
Check(overrunEstimate.BudgetHoursRemaining == -2m && overrunEstimate.CurrentEstimateToCompleteHours == 4m,
    "explicit current estimate to complete is not clamped from original estimate minus approved hours");
var unknownRate = ProjectFlowHiveFinancialReadback.Calculate(null, null,
    [new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-001", 10m, 10m, 4m, true, null)],
    [new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 3m, "pm_approved")]);
Check(unknownRate.ApprovedLaborCost is null && unknownRate.BudgetRemainingAfterActualCosts is null && unknownRate.ForecastAtCompletion is null
    && unknownRate.UnknownReasons.Contains("missing_rate:CAN-001") && unknownRate.KnownApprovedLaborCost == 0m, "missing production rate preserves known subtotals without a complete-looking total");
var unmatchedTime = ProjectFlowHiveFinancialReadback.Calculate(1000m, null,
    [new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-001", 10m, 10m, 4m, true, InternalRate(100m))],
    [new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 3m, "pm_approved"), new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), null, 2m, "locked")]);
Check(unmatchedTime.ApprovedHours == 5m && unmatchedTime.Completeness.KnownApprovedHours == 3m
    && unmatchedTime.Completeness.UnmatchedApprovedHours == 2m && unmatchedTime.ApprovedLaborCost is null,
    "unmatched approved time is visible and prevents a complete-looking cost total");
var inactiveHistory = ProjectFlowHiveFinancialReadback.Calculate(1000m, null,
    [new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-INACTIVE", 10m, 10m, 4m, false, InternalRate(100m))],
    [new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 3m, "locked")]);
Check(inactiveHistory.ApprovedLaborCost == 300m && inactiveHistory.Completeness.InactiveTaskCount == 1
    && inactiveHistory.DerivedAssumptions.Any(reason => reason.Contains("Inactive canonical tasks", StringComparison.Ordinal)),
    "inactive historical tasks retain approved time and cost attribution");
var zeroCostRate = ProjectFlowHiveFinancialReadback.Calculate(1000m, null,
    [new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-ZERO", 10m, 10m, 4m, true, InternalRate(0m))],
    [new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 3m, "accounting_ready")]);
Check(zeroCostRate.ApprovedLaborCost == 0m && zeroCostRate.Tasks.Single().RateVerified,
    "legitimate zero-cost internal rate remains typed and verified");
var missingCurrentEstimate = ProjectFlowHiveFinancialReadback.Calculate(1000m, null,
    [new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-NO-ETC", 10m, 10m, null, true, InternalRate(100m))],
    [new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 3m, "pm_approved")]);
Check(missingCurrentEstimate.CurrentEstimateToCompleteHours is null && missingCurrentEstimate.ForecastAtCompletion is null
    && missingCurrentEstimate.UnknownReasons.Contains("current_estimate_to_complete_hours_is_incomplete"),
    "missing current estimates remain unknown");
var billingRate = ProjectFlowHiveFinancialReadback.Calculate(1000m, null,
    [new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-BILLING", 10m, 10m, 4m, true,
        new ProjectFlowHiveRateSource(100m, "billing_rate", "USD", new DateOnly(2026, 1, 1), "sell_billing_rate", false))],
    [new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 3m, "pm_approved")]);
Check(billingRate.ApprovedLaborCost is null && billingRate.UnknownReasons.Contains("rate_purpose_not_internal_cost:CAN-BILLING"),
    "billing rate is not treated as internal labor cost");
var recordedForecast = ProjectFlowHiveFinancialReadback.Calculate(null, 900m,
    [new ProjectFlowHiveCanonicalTaskFinancialSource(readbackTaskOne, "CAN-001", 10m, 10m, null, true, null)],
    [new ProjectFlowHiveApprovedTimeSource(Guid.NewGuid(), readbackTaskOne, 3m, "pm_approved")]);
Check(recordedForecast.ForecastAtCompletion == 900m
    && recordedForecast.ForecastSource == "recorded"
    && recordedForecast.ForecastProvenance == "project_flowhive_project_controls.forecast_at_completion",
    "recorded project-control forecast remains authoritative with provenance when task rates are unavailable");

// Exercise the real Project Forge application handlers against the same
// disposable PostgreSQL database. This is intentionally separate from the
// pure calculator assertions above.
var integrationProject = Guid.NewGuid();
var integrationPlan = Guid.NewGuid();
var integrationPlanTask = Guid.NewGuid();
await Sql("""
    INSERT INTO projects(project_id,project_code,project_name,status,project_manager_user_id)
    VALUES(@project,'INTEGRATION-033','Synthetic handler integration project','active',@actor);
    INSERT INTO project_forge_plans(plan_id,project_id,plan_name,plan_status,source_kind,revision_number,updated_by_user_id)
    VALUES(@plan,@project,'Reviewed synthetic plan','reviewed','manual',1,@actor);
    INSERT INTO project_forge_plan_tasks(
        plan_task_id,plan_id,project_id,wbs_code,parent_wbs_code,task_name,task_description,task_type,phase_name,
        priority_code,task_status,kanban_category,decision_action,planned_start_date,planned_end_date,duration_working_days,
        recurrence_rule,percent_complete,estimated_hours,hourly_rate,material_units,material_unit_cost,fixed_cost,
        travel_cost,equipment_cost,miscellaneous_cost,is_important,is_urgent,source_kind,display_order,revision_number,updated_by_user_id)
    VALUES(@plan_task,@plan,@project,'1.1','', 'Integration canonical task','Synthetic authorized task','variable','Plan',
        'normal','not_started','backlog','none','2026-09-08','2026-09-09',2,'{}',0,10,100,0,0,0,0,0,0,FALSE,FALSE,'manual',1,1,@actor);
    INSERT INTO project_flowhive_project_controls(project_id,currency_code,approved_budget,forecast_at_completion,updated_by_user_id)
    VALUES(@project,'USD',1000,1640,@actor);
    INSERT INTO enterprise_notification_policies(policy_code,enabled) VALUES('PROJECT_FORGE_PLAN_UPDATED',TRUE) ON CONFLICT DO NOTHING;
    """, ("project", integrationProject), ("plan", integrationPlan), ("plan_task", integrationPlanTask), ("actor", actor));

var projectForgeType = typeof(ProjectForgeModule);
var adoptHandler = projectForgeType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
    .Single(method => method.Name == "AdoptPlanAsync" && method.GetParameters().Length == 4);
var financialHandler = projectForgeType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
    .Single(method => method.Name == "GetFinancialReadbackAsync" && method.GetParameters().Length == 3);
DefaultHttpContext AuthorizedContext(Guid userId)
{
    var context = new DefaultHttpContext();
    context.Items["ProjectPulseSessionUserId"] = userId;
    context.Items["ProjectPulseActualUserId"] = userId;
    context.Items["ProjectPulseEffectiveUserId"] = userId;
    return context;
}
async Task<IResult> InvokeResultAsync(MethodInfo method, params object?[] arguments)
{
    var task = (Task)method.Invoke(null, arguments)!;
    await task;
    return (IResult)task.GetType().GetProperty("Result")!.GetValue(task)!;
}
async Task<(int StatusCode, JsonElement Body)> ExecuteResultAsync(IResult result)
{
    var context = new DefaultHttpContext();
    context.RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
    context.Response.Body = new MemoryStream();
    await result.ExecuteAsync(context);
    context.Response.Body.Position = 0;
    using var document = await JsonDocument.ParseAsync(context.Response.Body);
    return (context.Response.StatusCode, document.RootElement.Clone());
}
var adoptRequest = new ProjectForgeAdoptPlanRequest("ADOPT PROJECT FORGE PLAN", false, "Synthetic integration adoption", 1, "integration-adopt-033-0001");
var adoptedResponse = await ExecuteResultAsync(await InvokeResultAsync(adoptHandler, integrationPlan, adoptRequest, AuthorizedContext(actor), CancellationToken.None));
Check(adoptedResponse.StatusCode == 200 && adoptedResponse.Body.GetProperty("status").GetString() == "plan_adopted_to_canonical_project", "real adoption handler applies an authorized reviewed plan");
var canonicalTaskId = (Guid)(await Sql("SELECT canonical_task_id FROM project_forge_plan_tasks WHERE plan_task_id=@task", ("task", integrationPlanTask)))!;
Check(canonicalTaskId != Guid.Empty && (long)(await Sql("SELECT count(*) FROM project_tasks WHERE project_id=@project", ("project", integrationProject)))! == 1,
    "adoption stores one stable canonical task identity");
await Sql("INSERT INTO time_entries(time_entry_id,project_id,task_id,hours,status,work_date) VALUES(@approved,@project,@task,3,'pm_approved','2026-09-08'),(@draft,@project,@task,2,'draft','2026-09-09');", ("approved", Guid.NewGuid()), ("draft", Guid.NewGuid()), ("project", integrationProject), ("task", canonicalTaskId));
var financialResponse = await ExecuteResultAsync(await InvokeResultAsync(financialHandler, integrationProject, AuthorizedContext(actor), CancellationToken.None));
var integrationReadback = financialResponse.Body.GetProperty("readback");
Check(financialResponse.StatusCode == 200 && integrationReadback.GetProperty("loggedHours").GetDecimal() == 5m
    && integrationReadback.GetProperty("approvedHours").GetDecimal() == 3m,
    "real financial handler reads logged and approved canonical time");
Check(integrationReadback.GetProperty("approvedLaborCost").ValueKind == JsonValueKind.Null
    && integrationReadback.GetProperty("completeness").GetProperty("status").GetString() == "incomplete"
    && integrationReadback.GetProperty("unknownReasons").EnumerateArray().Any(reason => reason.GetString()!.Contains("internal_cost_rate_not_verified", StringComparison.Ordinal)),
    "real financial handler exposes unclassified task rate instead of fabricating labor cost");
var repeatedAdoption = await ExecuteResultAsync(await InvokeResultAsync(adoptHandler, integrationPlan, adoptRequest with { ClientMutationId = "integration-adopt-033-0002" }, AuthorizedContext(actor), CancellationToken.None));
Check(repeatedAdoption.StatusCode == 409 && (long)(await Sql("SELECT count(*) FROM project_tasks WHERE project_id=@project", ("project", integrationProject)))! == 1,
    "repeat adoption is rejected without duplicate canonical tasks");

var concurrentPlan = Guid.NewGuid();
var concurrentTask = Guid.NewGuid();
await Sql("""
    INSERT INTO project_forge_plans(plan_id,project_id,plan_name,plan_status,source_kind,revision_number,updated_by_user_id)
    VALUES(@plan,@project,'Concurrent reviewed plan','reviewed','manual',1,@actor);
    INSERT INTO project_forge_plan_tasks(
        plan_task_id,plan_id,project_id,wbs_code,parent_wbs_code,task_name,task_description,task_type,phase_name,
        priority_code,task_status,kanban_category,decision_action,planned_start_date,planned_end_date,duration_working_days,
        recurrence_rule,percent_complete,estimated_hours,hourly_rate,material_units,material_unit_cost,fixed_cost,
        travel_cost,equipment_cost,miscellaneous_cost,is_important,is_urgent,source_kind,display_order,revision_number,updated_by_user_id)
    VALUES(@task,@plan,@project,'2.1','', 'Concurrent canonical task','Synthetic concurrent task','variable','Plan',
        'normal','not_started','backlog','none','2026-09-10','2026-09-11',2,'{}',0,4,100,0,0,0,0,0,0,FALSE,FALSE,'manual',1,1,@actor);
    """, ("plan", concurrentPlan), ("task", concurrentTask), ("project", integrationProject), ("actor", actor));
var concurrentRequests = await Task.WhenAll(
    InvokeResultAsync(adoptHandler, concurrentPlan, adoptRequest with { ClientMutationId = "integration-concurrent-0001" }, AuthorizedContext(actor), CancellationToken.None),
    InvokeResultAsync(adoptHandler, concurrentPlan, adoptRequest with { ClientMutationId = "integration-concurrent-0002" }, AuthorizedContext(actor), CancellationToken.None));
var concurrentStatuses = await Task.WhenAll(concurrentRequests.Select(ExecuteResultAsync));
Check(concurrentStatuses.Count(result => result.StatusCode == 200) == 1 && concurrentStatuses.Count(result => result.StatusCode == 409) == 1
    && (long)(await Sql("SELECT count(*) FROM project_tasks WHERE project_id=@project AND task_code LIKE 'PF-2-1%'", ("project", integrationProject)))! == 1,
    "concurrent adoption requests serialize and create no duplicate canonical task");

var unauthorizedUser = Guid.NewGuid();
var projectManagerRole = Guid.NewGuid();
await Sql("INSERT INTO app_users(user_id,display_name,email,is_active) VALUES(@user,'Unauthorized PM','unauthorized@example.invalid',TRUE); INSERT INTO app_roles(app_role_id,role_code,is_active) VALUES(@role,'PROJECT_MANAGER',TRUE); INSERT INTO app_user_role_assignments(user_id,app_role_id,is_active) VALUES(@user,@role,TRUE);", ("user", unauthorizedUser), ("role", projectManagerRole));
var crossProjectResponse = await ExecuteResultAsync(await InvokeResultAsync(financialHandler, integrationProject, AuthorizedContext(unauthorizedUser), CancellationToken.None));
Check(crossProjectResponse.StatusCode == 403, "cross-project financial readback is denied by the real handler");
var unauthenticatedResponse = await ExecuteResultAsync(await InvokeResultAsync(financialHandler, integrationProject, new DefaultHttpContext(), CancellationToken.None));
Check(unauthenticatedResponse.StatusCode == 401, "unauthenticated financial readback is denied by the real handler");
Console.WriteLine($"FLOWHIVE_EXECUTION_ASSERTIONS_PASSED={count}");
