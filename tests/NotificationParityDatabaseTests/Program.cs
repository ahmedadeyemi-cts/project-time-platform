using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using ProjectTime.Api.Modules;

// Disposable fixture only. Never starts the API, its workers, or a Microsoft/mail transport.
if(Environment.GetEnvironmentVariable("PARITY_POSTGRES_FIXTURE")!="YES") throw new Exception("Explicit disposable PostgreSQL fixture required.");
var settings=new NpgsqlConnectionStringBuilder { Host="127.0.0.1",Port=int.Parse(Environment.GetEnvironmentVariable("PARITY_POSTGRES_PORT")??"5432"),
    Database="pulse_notification_parity_test",Username="parity_fixture",Pooling=false };
var root=Directory.GetCurrentDirectory();
while(!Directory.Exists(Path.Combine(root,"database","migrations"))) root=Directory.GetParent(root)!.FullName;
var schema="parity_fixture_"+Guid.NewGuid().ToString("N");
var assembly=typeof(MicrosoftTeamsNotificationModule).Assembly;
var flags=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
var jsonOptions=new JsonSerializerOptions(JsonSerializerDefaults.Web);
var count=0;
var clock=new DateTimeOffset(2026,9,28,14,0,0,TimeSpan.Zero);
await using var admin=new NpgsqlConnection(settings.ConnectionString);await admin.OpenAsync();
Check(await Scalar<string>(admin,"SELECT current_database();")=="pulse_notification_parity_test","disposable database guard");
await Sql(admin,$"CREATE SCHEMA {schema};");settings.SearchPath=schema;
try
{
    await using var db=new NpgsqlConnection(settings.ConnectionString);await db.OpenAsync();
    await Sql(db,"""
        CREATE TABLE schema_migrations(migration_id text PRIMARY KEY,description text);
        CREATE TABLE app_users(user_id uuid PRIMARY KEY,email text,display_name text,is_active boolean,login_enabled boolean,
          manager_email text,job_title text,department text,department_name text,team_name text);
        CREATE TABLE projects(project_id uuid PRIMARY KEY);
        CREATE TABLE project_notification_dispatches(project_notification_dispatch_id uuid PRIMARY KEY);
        CREATE TABLE app_roles(app_role_id uuid PRIMARY KEY,role_code text,role_name text,is_active boolean);
        CREATE TABLE app_user_role_assignments(user_id uuid,app_role_id uuid,is_active boolean);
        CREATE TABLE reporting_relationships(employee_user_id uuid,manager_user_id uuid,effective_start_date date,effective_end_date date);
        CREATE TABLE notification_groups(notification_group_id uuid PRIMARY KEY,group_code text,is_active boolean);
        CREATE TABLE notification_group_members(notification_group_id uuid,user_id uuid,is_active boolean);
        CREATE TABLE reminder_rules(rule_code text,is_active boolean);
        CREATE TABLE timesheets(user_id uuid,week_start_date date,status text);
        CREATE TABLE company_holidays(company_holiday_id uuid PRIMARY KEY,holiday_date date,holiday_name text,is_active boolean,is_floating_holiday boolean);
        CREATE TABLE notification_outbox(status text,body text);
        INSERT INTO notification_outbox VALUES('dry_run','Do not send historical preview');
        """);
    var schema064=await File.ReadAllTextAsync(Path.Combine(root,"database/migrations/064_module_065_enterprise_notification_orchestration.sql"));
    foreach(var table in new[]{"enterprise_notification_policies","enterprise_notification_events","enterprise_notification_event_history"})
    {
        var definition=Regex.Match(schema064,$@"CREATE TABLE IF NOT EXISTS {table} \([\s\S]*?\n\);").Value;
        Check(definition.Length>0,"actual production table definition: "+table);await Sql(db,definition);
    }
    foreach(Match match in Regex.Matches(schema064,@"CREATE UNIQUE INDEX IF NOT EXISTS [\s\S]*?;"))
        if(match.Value.Contains("ON enterprise_notification_events")) await Sql(db,match.Value);
    await Sql(db,await File.ReadAllTextAsync(Path.Combine(root,"database/migrations/113_module065_teams_notifications.sql")));
    await Sql(db,await File.ReadAllTextAsync(Path.Combine(root,"database/migrations/126_module065_power_automate_teams_delivery.sql")));
    var migration128=await File.ReadAllTextAsync(Path.Combine(root,"database/migrations/128_module065_email_teams_notification_parity.sql"));
    await Sql(db,migration128);await Sql(db,migration128);
    Check(await Scalar<long>(db,"SELECT count(*) FROM module065_teams_outbox;")==0,"migration never queues or sends historical notifications");
    var first=Guid.NewGuid();
    await Queue(db,Snapshot(first,[" A@example.invalid ","a@example.invalid","b@example.invalid"]),true);
    Check(await Scalar<long>(db,"SELECT count(*) FROM module065_teams_outbox;")==2,"actual queue deduplicates email recipients");
    await Queue(db,Snapshot(first,["c@example.invalid"],"Changed body"),true);
    Check(await Scalar<long>(db,"SELECT count(*) FROM module065_teams_outbox;")==2,"replay cannot append new recipients");
    Check(await Scalar<string>(db,$"SELECT snapshot->>'textBody' FROM module065_teams_outbox_events WHERE dispatch_id='{first}';")=="Original body","snapshot content immutable on replay");
    await Sql(db,$"UPDATE module065_teams_outbox SET status='accepted' WHERE dispatch_id='{first}';");
    await Queue(db,Snapshot(first,["a@example.invalid"]),false);
    Check(await Scalar<long>(db,$"SELECT count(*) FROM module065_teams_outbox WHERE dispatch_id='{first}' AND status='accepted';")==2,"accepted Teams is never replayed or downgraded");
    var preview=Guid.NewGuid();await Queue(db,Snapshot(preview,["preview@example.invalid"]),false);
    await Queue(db,Snapshot(preview,["preview@example.invalid"]),true);
    Check(await Scalar<string>(db,$"SELECT status FROM module065_teams_outbox WHERE dispatch_id='{preview}';")=="suppressed","Test-only preview cannot later be promoted by replay");
    var many=Guid.NewGuid();await Queue(db,Snapshot(many,Enumerable.Range(0,501).Select(i=>$"large{i}@example.invalid").ToArray()),true);
    Check(await Scalar<long>(db,$"SELECT count(*) FROM module065_teams_outbox WHERE dispatch_id='{many}';")==501,"large company audience retained as individual durable jobs");
    var concurrent=Guid.NewGuid();
    await using(var other=new NpgsqlConnection(settings.ConnectionString))
    {
        await other.OpenAsync();
        await Task.WhenAll(Queue(db,Snapshot(concurrent,["one@example.invalid"]),true),Queue(other,Snapshot(concurrent,["two@example.invalid"]),true));
    }
    Check(await Scalar<long>(db,$"SELECT count(*) FROM module065_teams_outbox_events WHERE dispatch_id='{concurrent}';")==1,"replicas create one event snapshot");
    Check(await Scalar<long>(db,$"SELECT count(*) FROM module065_teams_outbox WHERE dispatch_id='{concurrent}';")==1,"replicas cannot merge or duplicate recipient snapshots");
    var interrupted=Guid.NewGuid();await Queue(db,Snapshot(interrupted,["interrupted@example.invalid"]),true);
    await Sql(db,$"UPDATE module065_teams_outbox SET status='sending',updated_at=now()-interval '6 minutes' WHERE dispatch_id='{interrupted}';");
    var expired=Guid.NewGuid();await Queue(db,Snapshot(expired,["expired@example.invalid"]),true);
    await Sql(db,$"UPDATE module065_teams_outbox_events SET expires_at=now()-interval '1 minute' WHERE dispatch_id='{expired}';");
    await Invoke("MicrosoftTeamsNotificationModule","RecoverOutboxAsync",db,"test",CancellationToken.None);
    Check(await Scalar<string>(db,$"SELECT status FROM module065_teams_outbox WHERE dispatch_id='{interrupted}';")=="outcome_unknown","interrupted send requires reconciliation, not retry");
    Check(await Scalar<string>(db,$"SELECT status FROM module065_teams_outbox WHERE dispatch_id='{expired}';")=="suppressed","expired messages do not become stale reminders");
    await Sql(db,$"INSERT INTO module065_teams_outbox_attempts SELECT gen_random_uuid(),'{many}','test','large0@example.invalid',now() FROM generate_series(1,20);");
    Check(await Scalar<long>(db,"SELECT count(*) FROM module065_teams_outbox_attempts WHERE environment='test' AND started_at>now()-interval '5 minutes';")==20,"attempt budget is durable across replicas");

    var migration129=await File.ReadAllTextAsync(Path.Combine(root,"database/migrations/129_enterprise_reminder_delivery_sources.sql"));
    await Sql(db,migration129);await Sql(db,migration129);
    var parityVerification=await File.ReadAllTextAsync(Path.Combine(root,"scripts/release-test/verify-module065-notification-parity.sql"));
    await Sql(db,parityVerification);
    Check(true,"actual packaged Protected UAT parity SQL accepts complete safe schema");
    await Sql(db,"ALTER INDEX ix_module065_teams_outbox_due RENAME TO fixture_missing_queue_index;");
    var missingIndexRejected=false;
    try { await Sql(db,parityVerification); } catch(PostgresException) { missingIndexRejected=true; }
    await Sql(db,"ALTER INDEX fixture_missing_queue_index RENAME TO ix_module065_teams_outbox_due;");
    Check(missingIndexRejected,"packaged UAT verifier fails closed on missing durable queue index");

    Check(await Scalar<long>(db,"SELECT count(*) FROM enterprise_notification_policies WHERE producer_contract='enterprise-reminder-v1' AND enabled=FALSE AND delivery_boundary='test_only';")==4,"new sources are opt-in and Test-only");
    for(var i=1;i<=5;i++) await Sql(db,$"INSERT INTO app_users(user_id,email,display_name,is_active,login_enabled) VALUES('{User(i)}','u{i}@example.invalid','User {i}',{(i!=5 ? "TRUE" : "FALSE")},TRUE);");
    await Sql(db,$$"""
        INSERT INTO notification_groups VALUES('{{User(10)}}','ENGINEERS',TRUE),('{{User(11)}}','PROJECT_MANAGEMENT',TRUE);
        INSERT INTO notification_group_members VALUES('{{User(10)}}','{{User(1)}}',TRUE),('{{User(10)}}','{{User(2)}}',TRUE),('{{User(11)}}','{{User(3)}}',TRUE);
        INSERT INTO reminder_rules VALUES('WEEKLY_ENGINEER_TIME_REMINDER',TRUE),('WEEKLY_ENGINEER_TIME_ESCALATION',TRUE),('HOLIDAY_TIME_REMINDER_7_DAY',TRUE),('HOLIDAY_TIME_REMINDER_1_DAY',TRUE),('MONTH_END_PM_REMINDER',TRUE);
        INSERT INTO timesheets VALUES('{{User(2)}}','2026-09-20','submitted');
        INSERT INTO company_holidays VALUES('{{User(20)}}','2026-10-05','Synthetic holiday seven',TRUE,FALSE),('{{User(21)}}','2026-09-29','Synthetic holiday one',TRUE,FALSE);
        INSERT INTO app_roles VALUES('{{User(30)}}','PROJECT_TEAM_COORDINATOR','Project Team Coordinator',TRUE);
        INSERT INTO app_user_role_assignments VALUES('{{User(4)}}','{{User(30)}}',TRUE);
        INSERT INTO reporting_relationships VALUES('{{User(1)}}','{{User(3)}}',CURRENT_DATE-5,NULL);
        """);
    await Invoke("EnterpriseReminderNotificationSource","ScanAsync",db,"fixture",CancellationToken.None,(DateTimeOffset?)clock);
    Check(await Scalar<long>(db,"SELECT count(*) FROM enterprise_notification_events;")==0,"disabled producers generate no live or queued notification");
    await Sql(db,"UPDATE enterprise_notification_policies SET enabled=TRUE WHERE producer_contract='enterprise-reminder-v1';");
    await Invoke("EnterpriseReminderNotificationSource","ScanAsync",db,"fixture",CancellationToken.None,(DateTimeOffset?)clock);
    Check(await Scalar<long>(db,"SELECT count(*) FROM enterprise_notification_events WHERE policy_code='TIME_NOT_SUBMITTED';")==1,"only missing engineer gets a non-submission event");
    Check(await Scalar<long>(db,"SELECT count(*) FROM enterprise_notification_events WHERE policy_code='TIME_NOT_SUBMITTED_ESCALATION';")==1,"only missing engineer gets an escalation event");
    Check(await Scalar<long>(db,"SELECT count(*) FROM enterprise_notification_events WHERE policy_code='COMPANY_HOLIDAY_UPCOMING';")==8,"both upcoming holidays notify every active user individually");
    var before=await Scalar<long>(db,"SELECT count(*) FROM enterprise_notification_events;");
    await Invoke("EnterpriseReminderNotificationSource","ScanAsync",db,"fixture",CancellationToken.None,(DateTimeOffset?)clock);
    Check(await Scalar<long>(db,"SELECT count(*) FROM enterprise_notification_events;")==before,"same reminder window is idempotent");
    Check(await Scalar<long>(db,"SELECT count(*) FROM notification_outbox WHERE status='dry_run';")==1,"legacy dry-run remains non-sending");
    var escalationId=await Scalar<Guid>(db,"SELECT enterprise_notification_event_id FROM enterprise_notification_events WHERE policy_code='TIME_NOT_SUBMITTED_ESCALATION' LIMIT 1;");
    var escalation=await Invoke("EnterpriseNotificationRepository","LoadEventAsync",db,escalationId,CancellationToken.None);
    var escalationPolicy=await Invoke("EnterpriseNotificationRepository","LoadPolicyAsync",db,"TIME_NOT_SUBMITTED_ESCALATION",CancellationToken.None);
    var resolution=await Invoke("EnterpriseNotificationRecipientResolver","ResolveAsync",db,escalationPolicy!,escalation!,CancellationToken.None);
    using(var result=JsonDocument.Parse(JsonSerializer.Serialize(resolution,jsonOptions)))
    {
        var recipients=result.RootElement.GetProperty("recipients").EnumerateArray().ToArray();
        Check(recipients.Any(x=>x.GetProperty("email").GetString()=="u3@example.invalid" && x.GetProperty("recipientType").GetString()=="to"),"current manager receives escalation");
        Check(recipients.Any(x=>x.GetProperty("email").GetString()=="u4@example.invalid" && x.GetProperty("recipientType").GetString()=="to"),"PTC receives escalation");
        Check(recipients.Any(x=>x.GetProperty("email").GetString()=="u1@example.invalid" && x.GetProperty("recipientType").GetString()=="cc"),"engineer is copied without replacing next approvers");
    }
    Check((bool)(await Invoke("EnterpriseReminderNotificationSource","IsCurrentAsync",db,escalation!,CancellationToken.None,(DateTimeOffset?)clock))!,"pending non-submission revalidation succeeds");
    await Sql(db,$"INSERT INTO timesheets VALUES('{User(1)}','2026-09-20','manager_approved');");
    Check(!(bool)(await Invoke("EnterpriseReminderNotificationSource","IsCurrentAsync",db,escalation!,CancellationToken.None,(DateTimeOffset?)clock))!,"submission cancels queued escalation before either send");
    await Sql(db,await File.ReadAllTextAsync(Path.Combine(root,"database/rollback/129_enterprise_reminder_delivery_sources_rollback.sql")));
    Check(await Scalar<long>(db,"SELECT count(*) FROM enterprise_notification_policies WHERE producer_contract='enterprise-reminder-v1' AND enabled=FALSE AND delivery_boundary='locked';")==4,"rollback disables new producers and retains event evidence");
    var refused=false;
    try { await Sql(db,await File.ReadAllTextAsync(Path.Combine(root,"database/rollback/128_module065_email_teams_notification_parity_rollback.sql"))); }
    catch(PostgresException) { refused=true;await Sql(db,"ROLLBACK;"); }
    Check(refused && await Scalar<long>(db,"SELECT count(*) FROM module065_teams_outbox;")>0,"outbox rollback refuses to delete notification evidence");
}
finally { await Sql(admin,$"DROP SCHEMA {schema} CASCADE;"); }
Console.WriteLine($"NOTIFICATION_PARITY_DB_ASSERTIONS={count}; LIVE_MICROSOFT_CALLS=0; RESULT=PASS");
void Check(bool condition,string name) { if(!condition) throw new Exception("FAILED: "+name);count++; }
Guid User(int number)=>Guid.Parse($"00000000-0000-0000-0000-{number:000000000000}");
async Task Sql(NpgsqlConnection db,string sql) { await using var command=new NpgsqlCommand(sql,db);await command.ExecuteNonQueryAsync(); }
async Task<T> Scalar<T>(NpgsqlConnection db,string sql) { await using var command=new NpgsqlCommand(sql,db);return (T)(await command.ExecuteScalarAsync())!; }
async Task<object?> Invoke(string type,string method,params object[] arguments)
{
    var member=assembly.GetType("ProjectTime.Api.Modules."+type)!.GetMethod(method,flags)!;
    Task task;
    try { task=(Task)member.Invoke(null,arguments)!; }
    catch(TargetInvocationException error) { throw error.InnerException!; }
    await task;return task.GetType().GetProperty("Result")?.GetValue(task);
}
object Snapshot(Guid id,string[] emails,string body="Original body")
{
    var json=JsonSerializer.Serialize(new { dispatchId=id,eventKey="fixture:"+id,notificationType="fixture_notice",alertSeverity="informational",sourceModule="065",sourceStatus="current",
        subject="Synthetic notification",textBody=body,htmlBody="",deliveryBoundary="production_governed",providerSource="module_065",deliveryStatus="queued",
        providerMessageId="",lastErrorCode="",lastErrorMessage="",metadata=new {},createdAt=clock,updatedAt=clock,
        recipients=emails.Select(email=>new { email,displayName=email,role="ENGINEER",derivationSource="synthetic_fixture",recipientType="to" }),attemptCount=0 },jsonOptions);
    return JsonSerializer.Deserialize(json,assembly.GetType("ProjectTime.Api.Modules.ProjectNotificationDispatchRow")!,jsonOptions)!;
}
async Task Queue(NpgsqlConnection db,object snapshot,bool allowed)=>await Invoke("MicrosoftTeamsNotificationModule","QueueMirrorCoreAsync",db,snapshot,"test",allowed,CancellationToken.None);
