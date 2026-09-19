using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Modules;
using Policy = ProjectTime.Api.Modules.ProjectFlowHiveNotificationPolicy;

var count = 0;
void Check(bool result, string name) { if (!result) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
var project = Guid.NewGuid(); var pm = Guid.NewGuid(); var owner = Guid.NewGuid(); var nextOwner = Guid.NewGuid();
var now = new DateTimeOffset(2026, 9, 21, 14, 0, 0, TimeSpan.Zero);
var settings = Policy.Default with { Timezone = "UTC", QuietStart=null, QuietEnd=null };
var task = new Policy.Task(Guid.NewGuid(), "1.1", "Validate migration prerequisites", new(2026,9,24), [owner]);
var first = Policy.Evaluate(project,"v1",[task],pm,settings,new Dictionary<Guid,Policy.State>(),now);
Check(first.Events.Count(e => e.Kind == "assigned") == 1, "assignment only to new owner");
Check(first.Events.Count(e => e.Kind == "due_in_3_days") == 2, "three-day reminder reaches owner and PM");
var repeat = Policy.Evaluate(project,"v2",[task],pm,settings,first.State,now);
Check(!repeat.Events.Any(e => e.Kind == "assigned"), "unchanged rebaseline does not notify again");
Check(repeat.Events.All(e => first.Events.Any(f => f.Key == e.Key)), "due keys stable across unchanged rebaseline");
var reassigned = Policy.Evaluate(project,"v3",[task with { Assignees=[nextOwner] }],pm,settings,repeat.State,now);
Check(reassigned.Events.Single(e => e.Kind == "assigned").Recipient == nextOwner, "reassignment notifies new owner only");
var returned = Policy.Evaluate(project,"v4",[task],pm,settings,reassigned.State,now);
Check(returned.Events.Single(e => e.Kind == "assigned").Key != first.Events.Single(e => e.Kind == "assigned").Key, "assignment back to prior owner has new identity");
Check(Policy.DueKind(task.Due,task.Due,settings)=="due_today", "due day always included");
Check(Policy.DueKind(task.Due,task.Due.AddDays(-2),settings) is null, "no stale three-day catchup");
Check(Policy.DueKind(task.Due,task.Due.AddDays(1),settings)=="overdue", "first overdue day");
var overdue1=Policy.Evaluate(project,"v1",[task],pm,settings,first.State,now.AddDays(4));
var overdue2=Policy.Evaluate(project,"v1",[task],pm,settings,overdue1.State,now.AddDays(5));
Check(overdue1.Events.Select(e=>e.Key).SequenceEqual(overdue2.Events.Select(e=>e.Key)), "overdue reminder once, not daily spam");
var moved=Policy.Evaluate(project,"v5",[task with { Due=task.Due.AddDays(1) }],pm,settings,first.State,now.AddDays(1));
Check(moved.Events.All(e=>first.Events.All(f=>f.Key!=e.Key)), "reschedule gets new due identity");
Check(Policy.Evaluate(project,"v1",[task],owner,settings,first.State,now).Events.Length==1,"PM and assignee deduplicated");
Check(Policy.Evaluate(project,"v1",[],pm,settings,first.State,now).Events.Length==0,"removed/completed work does not notify");
Check(Policy.Evaluate(project,"v1",[task],pm,settings with { Enabled=false },first.State,now).Events.Length==0,"disabled setting");
Check(Policy.Evaluate(project,"v1",[task],pm,settings with { Boundary="locked" },first.State,now).Events.Length==0,"locked boundary");
Check(Policy.LocalDate(new(2026,9,22,1,0,0,TimeSpan.Zero),Policy.Default)==new DateOnly(2026,9,21),"project-local midnight");
Check(Policy.IsQuiet(new(2026,9,22,2,0,0,TimeSpan.Zero),Policy.Default),"overnight quiet hours");
Check(!Policy.IsQuiet(new(2026,9,22,12,0,0,TimeSpan.Zero),Policy.Default),"quiet hours end");
Check(Policy.LocalDate(new(2026,11,1,6,30,0,TimeSpan.Zero),Policy.Default)==new DateOnly(2026,11,1),"DST overlap local day");
var quiet = Policy.Evaluate(project,"v1",[task],pm,Policy.Default,new Dictionary<Guid,Policy.State>(),new(2026,9,22,2,0,0,TimeSpan.Zero));
Check(quiet.State.Count==0 && quiet.Events.Length==0,"quiet scan does not consume new assignment");
if (args.Contains("--database")) await Database();
Console.WriteLine($"FlowHive notification assertions: {count}");

async System.Threading.Tasks.Task Database()
{
    var raw=Environment.GetEnvironmentVariable("FLOWHIVE_TEST_DB") ?? throw new Exception("FLOWHIVE_TEST_DB required");
    var config=new NpgsqlConnectionStringBuilder(raw);
    if (config.Database != "flowhive_execution_test" || config.Host != "127.0.0.1") throw new Exception("Only the isolated CI fixture is permitted");
    await using (var admin=new NpgsqlConnection(raw))
    {
        await admin.OpenAsync();
        await using var create=new NpgsqlCommand("CREATE DATABASE flowhive_notifications_test;",admin);
        await create.ExecuteNonQueryAsync();
    }
    config.Database="flowhive_notifications_test";
    await using var db=new NpgsqlConnection(config.ConnectionString); await db.OpenAsync();
    async System.Threading.Tasks.Task Sql(string sql) { await using var cmd=new NpgsqlCommand(sql,db); await cmd.ExecuteNonQueryAsync(); }
    async System.Threading.Tasks.Task<long> Number(string sql) { await using var cmd=new NpgsqlCommand(sql,db); return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
    string Table(string file,string table)
    {
        var s=File.ReadAllText(file); var start=s.IndexOf("CREATE TABLE IF NOT EXISTS "+table+" (",StringComparison.Ordinal);
        var end=s.IndexOf("\n);",start,StringComparison.Ordinal)+4;return s[start..end];
    }
    await Sql("""
        CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT,applied_at TIMESTAMPTZ);
        CREATE TABLE app_users(user_id UUID PRIMARY KEY,display_name TEXT,email TEXT,is_active BOOLEAN DEFAULT TRUE);
        CREATE TABLE projects(project_id UUID PRIMARY KEY,project_code TEXT,status TEXT DEFAULT 'active',project_manager_user_id UUID);
        CREATE TABLE project_notification_dispatches(project_notification_dispatch_id UUID PRIMARY KEY);
        """);
    var m64="database/migrations/064_module_065_enterprise_notification_orchestration.sql";
    foreach(var table in new[]{"enterprise_notification_policies","enterprise_notification_events","enterprise_notification_event_history","enterprise_notification_source_checkpoints"}) await Sql(Table(m64,table));
    await Sql("""
        CREATE TABLE project_flowhive_plans(plan_id UUID PRIMARY KEY,project_id UUID,baseline_version_number INTEGER,plan_status TEXT,baselined_at TIMESTAMPTZ);
        CREATE TABLE project_flowhive_plan_versions(plan_id UUID,version_number INTEGER,plan_payload JSONB,schedule_payload JSONB);
        CREATE TABLE project_flowhive_plan_reviews(plan_id UUID,version_number INTEGER,decision TEXT);
        """);
    await Sql(Table("database/migrations/103_module_066_flowhive_enterprise_psa_revamp.sql","project_flowhive_task_reminder_preferences"));
    var migration=File.ReadAllText("database/migrations/112_module_066_task_notifications.sql");
    await Sql(migration); await Sql(migration);
    Check(await ProjectFlowHiveNotificationSource.ReadyAsync(db,default),"migration replay is ready");
    var planId=Guid.NewGuid();
    await Sql($"INSERT INTO app_users VALUES('{owner}','Fixture engineer','engineer@example.invalid',TRUE),('{pm}','Fixture PM','pm@example.invalid',TRUE); INSERT INTO projects VALUES('{project}','NOTIFY-FIXTURE','active','{pm}');");
    await Sql($"INSERT INTO project_flowhive_task_reminder_preferences(project_id,enabled,lead_days,timezone_name,quiet_hours_start,quiet_hours_end,updated_by_user_id) VALUES('{project}',TRUE,ARRAY[3,0]::SMALLINT[],'UTC',NULL,NULL,'{pm}');");
    var today=DateOnly.FromDateTime(DateTime.UtcNow);
    var plan=new ProjectFlowHivePlanRequest(project,"NOTIFY-FIXTURE","Fixture",null,"Fixture","1",today,today.AddDays(20),
        [new(task.Id,null,"1.1",null,task.Name,"Fixture",1,false,null,null,0,8,"not_started")],[],[new("1.1",owner,"Fixture engineer",100,8)],null,null,null);
    var schedule=new ProjectFlowHiveScheduleResult(true,"scheduled",today,today.AddDays(20),today,1,1,8,
        [new("1.1",null,task.Name,today,today,1,0,0,0,0,true,false,0,8,"not_started")],[],"weekdays","test");
    var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
    async System.Threading.Tasks.Task Save(int version,ProjectFlowHivePlanRequest value)
    {
        await using var cmd=new NpgsqlCommand("INSERT INTO project_flowhive_plan_versions VALUES(@plan,@v,@payload::jsonb,@schedule::jsonb);",db);
        cmd.Parameters.AddWithValue("plan",planId);cmd.Parameters.AddWithValue("v",version);
        cmd.Parameters.AddWithValue("payload",JsonSerializer.Serialize(value,json));cmd.Parameters.AddWithValue("schedule",JsonSerializer.Serialize(schedule,json));
        await cmd.ExecuteNonQueryAsync();
    }
    await Sql($"INSERT INTO project_flowhive_plans VALUES('{planId}','{project}',NULL,'draft',NOW());");await Save(1,plan);
    await ProjectFlowHiveNotificationSource.ScanAsync(db,"fixture",default);
    Check(await Number("SELECT count(*) FROM enterprise_notification_events")==0,"draft emits no messages");
    await Sql($"UPDATE project_flowhive_plans SET baseline_version_number=1; INSERT INTO project_flowhive_plan_reviews VALUES('{planId}',1,'approved_for_baseline');");
    var observed=await ProjectFlowHiveNotificationSource.ScanAsync(db,"fixture",default);
    Check(observed.Status=="healthy" && observed.EventsCreated==3,"approved WBS produces assignment and due-day events");
    await ProjectFlowHiveNotificationSource.ScanAsync(db,"fixture",default);
    Check(await Number("SELECT count(*) FROM enterprise_notification_events")==3,"repeat scan idempotent");
    await Sql("DELETE FROM project_flowhive_notification_state;");
    await ProjectFlowHiveNotificationSource.ScanAsync(db,"crash-retry",default);
    Check(await Number("SELECT count(*) FROM enterprise_notification_events")==3,"crash before checkpoint replays without loss or duplicates");
    await using(var other=new NpgsqlConnection(config.ConnectionString))
    {
        await other.OpenAsync();
        var runs=await System.Threading.Tasks.Task.WhenAll(ProjectFlowHiveNotificationSource.ScanAsync(db,"replica1",default),ProjectFlowHiveNotificationSource.ScanAsync(other,"replica2",default));
        Check(runs.All(r=>r.Status=="healthy") && await Number("SELECT count(*) FROM enterprise_notification_events")==3,"concurrent replicas do not duplicate events");
    }
    var events=await EnterpriseNotificationRepository.ClaimDueEventsAsync(db,100,default);
    Check(events.Length==3,"existing Module 065 worker claims task events");
    foreach(var item in events) Check((await ProjectFlowHiveNotificationSource.ValidateAsync(db,item,default)).Current,"queued event still current");
    await Sql("UPDATE enterprise_notification_events SET updated_at=NOW()-INTERVAL '31 minutes';");
    var reclaimed=await EnterpriseNotificationRepository.ClaimDueEventsAsync(db,100,default);
    Check(reclaimed.Length==3 && reclaimed.All(e=>e.AttemptCount==2),"crashed processing leases can be reclaimed");
    Check((await EnterpriseNotificationRepository.ClaimDueEventsAsync(db,100,default)).Length==0,"live processing lease cannot be double claimed");
    await Sql($"UPDATE app_users SET is_active=FALSE WHERE user_id='{owner}';");
    var ownerEvent=events.First(e=>e.SubjectUserId==owner);
    Check(!(await ProjectFlowHiveNotificationSource.ValidateAsync(db,ownerEvent,default)).Current,"inactive recipient suppressed before delivery");
    await Sql($"UPDATE app_users SET is_active=TRUE WHERE user_id='{owner}'; UPDATE projects SET status='closed';");
    Check(!(await ProjectFlowHiveNotificationSource.ValidateAsync(db,ownerEvent,default)).Current,"closed project suppressed before delivery");
    await Sql("UPDATE projects SET status='active';");
    await Sql("UPDATE project_flowhive_task_reminder_preferences SET enabled=FALSE;");
    Check(!(await ProjectFlowHiveNotificationSource.ValidateAsync(db,events[0],default)).Current,"disabled before dispatch suppresses");
    await Sql("UPDATE project_flowhive_task_reminder_preferences SET enabled=TRUE,delivery_boundary='production_governed';");
    Check((await ProjectFlowHiveNotificationSource.ValidateAsync(db,events[0],default)).Boundary=="test_only","test events cannot later become live");
    await Save(2,plan with { Assignments=[] });
    await Sql($"UPDATE project_flowhive_plans SET baseline_version_number=2; INSERT INTO project_flowhive_plan_reviews VALUES('{planId}',2,'approved_for_baseline');");
    Check(!(await ProjectFlowHiveNotificationSource.ValidateAsync(db,ownerEvent,default)).Current,"removed assignee suppressed before delivery");
    await Save(3,plan with { Tasks=[plan.Tasks![0] with { PercentComplete=100,Status="complete" }] });
    await Sql($"UPDATE project_flowhive_plans SET baseline_version_number=3; INSERT INTO project_flowhive_plan_reviews VALUES('{planId}',3,'approved_for_baseline');");
    Check(!(await ProjectFlowHiveNotificationSource.ValidateAsync(db,events[0],default)).Current,"completed before dispatch suppresses");
    var suppressed=await EnterpriseNotificationOrchestrationService.ProcessEventAsync(db,ownerEvent,null,null,"fixture-stale",default);
    Check(suppressed.Status=="suppressed" && suppressed.DiagnosticCode=="FLOWHIVE_TASK_EVENT_STALE","real dispatcher suppresses completed source without provider call");
    await Sql(File.ReadAllText("database/rollback/112_module_066_task_notifications_rollback.sql"));
    Check(await Number("SELECT count(*) FROM enterprise_notification_events")==3 && await Number("SELECT count(*) FROM enterprise_notification_policies WHERE enabled")==0,"rollback retains evidence and disables policies");
    await Sql(migration);
    Check(await Number("SELECT count(*) FROM enterprise_notification_policies WHERE enabled")==0,"migration replay cannot reactivate disabled policies");
}
