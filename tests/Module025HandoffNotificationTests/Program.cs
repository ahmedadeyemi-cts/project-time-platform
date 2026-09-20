using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using ProjectTime.Api.Modules;

// Real handoff producer and authoritative-recipient resolver against disposable
// PostgreSQL. This executable never starts a worker or invokes a mail transport.
var root = Directory.GetCurrentDirectory();
while (!Directory.Exists(Path.Combine(root,"database","migrations"))) root=Directory.GetParent(root)!.FullName;
var settings=new NpgsqlConnectionStringBuilder { Host="127.0.0.1",Port=int.Parse(Environment.GetEnvironmentVariable("PGPORT")??"5432"),
    Database="postgres",Username="postgres",Pooling=false,Password=Environment.GetEnvironmentVariable("PGPASSWORD")??throw new Exception("Disposable PostgreSQL PGPASSWORD required.") };
var name="module025_handoff_notifications_"+Guid.NewGuid().ToString("N");
await using var admin=new NpgsqlConnection(settings.ConnectionString); await admin.OpenAsync();
await Sql(admin,$"CREATE DATABASE {name};"); settings.Database=name;
var count=0;
try
{
    await using var connection=new NpgsqlConnection(settings.ConnectionString); await connection.OpenAsync();
    foreach(var migration in new[]{"001_initial_schema","099_module025_sow_gsd_workspace"})
        await Sql(connection,await File.ReadAllTextAsync(Path.Combine(root,"database","migrations",migration+".sql")));
    await Sql(connection,"""
        CREATE TABLE app_roles(app_role_id uuid PRIMARY KEY,role_code text,is_active boolean);
        CREATE TABLE app_user_role_assignments(user_id uuid,app_role_id uuid,is_active boolean);
        INSERT INTO app_users(user_id,email,display_name,is_active)
            SELECT ('00000000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid,
              'user'||n||'@example.invalid','User '||n,n<>6 FROM generate_series(1,7)n;
        INSERT INTO app_roles VALUES('10000000-0000-0000-0000-000000000001','SOLUTION_ARCHITECT',true);
        INSERT INTO app_user_role_assignments SELECT user_id,'10000000-0000-0000-0000-000000000001',true FROM app_users;
        INSERT INTO reporting_relationships(employee_user_id,manager_user_id,effective_start_date,effective_end_date)
            VALUES ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000003',CURRENT_DATE-5,NULL),
              ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000004',CURRENT_DATE-6,CURRENT_DATE-1),
              ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000005',CURRENT_DATE+1,NULL),
              ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000006',CURRENT_DATE-7,NULL);
        """);
    var migration050=await File.ReadAllTextAsync(Path.Combine(root,"database","migrations","050_project_notification_routing_and_schedules.sql"));
    foreach(var table in new[]{"project_cost_alert_routing_rules","project_notification_schedules","project_notification_dispatches","project_notification_dispatch_recipients","project_notification_delivery_attempts","project_notification_configuration_audit"})
    {
        var definition=Regex.Match(migration050,$@"CREATE TABLE IF NOT EXISTS {table} \([\s\S]*?\n\);").Value;
        Check(!string.IsNullOrEmpty(definition),"production definition found for "+table);
        await Sql(connection,definition);
    }
    // Use production table definitions from 064, without provisioning unrelated
    // RBAC/catalog/legacy project routing dependencies for this isolated producer.
    var migration064=await File.ReadAllTextAsync(Path.Combine(root,"database","migrations","064_module_065_enterprise_notification_orchestration.sql"));
    foreach(var table in new[]{"enterprise_notification_policies","enterprise_notification_events","enterprise_notification_event_history"})
    {
        var definition=Regex.Match(migration064,$@"CREATE TABLE IF NOT EXISTS {table} \([\s\S]*?\n\);").Value;
        Check(!string.IsNullOrEmpty(definition),"production definition found for "+table);
        await Sql(connection,definition);
    }
    var record=Guid.NewGuid();
    await Sql(connection,$$"""
        INSERT INTO module025_sow_gsd_engagements(engagement_id,owner_user_id,owner_display_name,customer_name,customer_entry_mode)
        VALUES('{{record}}','{{User(1)}}','User 1','Synthetic customer','manual');
        INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,evidence_json)
        VALUES('{{record}}','ownership_transferred','{{User(2)}}',1,'{"newOwnerUserId":"{{User(1)}}"}');
        """);
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        var outcome=await Queue(connection,transaction,record);
        Check(Property<string>(outcome,"Status")=="unconfigured","missing migration never falsely reports queued");
        await transaction.CommitAsync();
    }
    var migration120=await File.ReadAllTextAsync(Path.Combine(root,"database","migrations","120_module025_handoff_notifications.sql"));
    await Sql(connection,migration120); await Sql(connection,migration120);
    Check(await Scalar<long>(connection,"SELECT count(*) FROM enterprise_notification_policies WHERE producer_contract='module025-handoff-v1' AND delivery_boundary='test_only';")==4,"four idempotently installed policies default to test-only");
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        var rejected=false;
        try { await Queue(connection,transaction,Guid.NewGuid()); }
        catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"configured producer refuses an enqueue without an authoritative handoff audit event");
        await transaction.RollbackAsync();
    }
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        await Queue(connection,transaction,record); await transaction.RollbackAsync();
    }
    Check(await Scalar<long>(connection,"SELECT count(*) FROM enterprise_notification_events;")==0,"ownership transaction rollback removes queued notification");
    Guid notificationId;
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        var first=await Queue(connection,transaction,record); var duplicate=await Queue(connection,transaction,record);
        notificationId=Property<Guid>(first,"EventId");
        Check(Property<Guid>(duplicate,"EventId")==notificationId,"duplicate queue resolves the same event identity");
        Check(Property<string>(first,"Status")=="queued","new event is queued, never claimed sent");
        await transaction.CommitAsync();
    }
    Check(await Scalar<long>(connection,"SELECT count(*) FROM enterprise_notification_events;")==1
        && await Scalar<long>(connection,"SELECT count(*) FROM enterprise_notification_event_history;")==1,"one durable event and acceptance history per source handoff");
    // The handoff delivery branch uses this existing durable claim before any
    // provider boundary. Even a recovered processing event cannot send twice.
    var dispatch=Guid.NewGuid();
    await Sql(connection,$"INSERT INTO project_notification_dispatches(project_notification_dispatch_id,event_key,notification_type,source_module,subject,text_body,delivery_status) VALUES('{dispatch}','synthetic-handoff','sow_gsd_handoff','025','Synthetic','Synthetic','queued');");
    Check(await Claim(connection,dispatch) && !await Claim(connection,dispatch),"durable dispatch permits one claim and blocks an in-flight duplicate");
    await Sql(connection,$"UPDATE project_notification_dispatches SET delivery_status='sent' WHERE project_notification_dispatch_id='{dispatch}';");
    Check(!await Claim(connection,dispatch),"provider-confirmed sent dispatch cannot be claimed for a retry");
    await Sql(connection,$"UPDATE enterprise_notification_events SET event_status='processing',updated_at=NOW()-INTERVAL '31 minutes' WHERE enterprise_notification_event_id='{notificationId}';");
    var reclaimed=(Array)await Invoke(typeof(Module025SowGsdModule).Assembly.GetType("ProjectTime.Api.Modules.EnterpriseNotificationRepository")!,"ClaimDueEventsAsync",connection,10,CancellationToken.None);
    Check(reclaimed.Length==1,"interrupted handoff processing is recoverable through the guarded dispatch path");
    var notification=await LoadEvent(connection,notificationId);
    var releaseDispatchId=Guid.NewGuid();
    await Sql(connection,$$"""
        INSERT INTO project_notification_dispatches(project_notification_dispatch_id,event_key,notification_type,source_module,subject,text_body,delivery_status,delivery_boundary,metadata_json)
          VALUES('{{releaseDispatchId}}','manual-release-preflight','module025_handoff','025','Synthetic handoff','Synthetic','queued','production_governed','{"enterpriseNotificationEventId":"{{notificationId}}"}');
        INSERT INTO project_notification_dispatch_recipients(project_notification_dispatch_id,recipient_role,recipient_user_id,recipient_name,recipient_email,recipient_type,derivation_source)
          VALUES('{{releaseDispatchId}}','SOLUTION_ARCHITECT','{{User(1)}}','User 1','user1@example.invalid','to','synthetic-authoritative-snapshot'),
            ('{{releaseDispatchId}}','MANAGER','{{User(3)}}','User 3','user3@example.invalid','cc','synthetic-authoritative-snapshot');
        """);
    var releaseDispatch=await LoadDispatch(connection,releaseDispatchId);
    var preflight=await Preflight(connection,releaseDispatch);
    Check(Property<bool>(preflight,"Current") && Property<string>(preflight,"Boundary")=="test_only","unchanged recipients may continue only within the current policy boundary, even if the stored boundary is broader");
    var recipients=await Resolve(connection,notification);
    var rows=recipients.GetProperty("Recipients").EnumerateArray().ToArray();
    Check(rows.Length==2 && rows.Any(row=>row.GetProperty("UserId").GetGuid()==User(1)) && rows.Any(row=>row.GetProperty("UserId").GetGuid()==User(3)),
        "recipients are current owner and active current manager; expired, future and inactive managers excluded");
    Check(rows.Single(row=>row.GetProperty("UserId").GetGuid()==User(1)).GetProperty("RecipientType").GetString()=="to"
        && rows.Single(row=>row.GetProperty("UserId").GetGuid()==User(3)).GetProperty("RecipientType").GetString()=="cc","new owner is To and responsible manager is Cc");
    await Sql(connection,$"UPDATE app_user_role_assignments SET is_active=false WHERE user_id='{User(1)}';");
    Check((await Resolve(connection,notification)).GetProperty("Recipients").GetArrayLength()==0,"owner without current Solution Architect role cannot receive a stale handoff notification");
    Check(!Property<bool>(await Preflight(connection,releaseDispatch),"Current"),"manual release rejects stored recipients after owner role revocation");
    var suppressedDelivery=await Invoke(typeof(Module025SowGsdModule).Assembly.GetType("ProjectTime.Api.Modules.ProjectNotificationProcessingService")!,
        "DeliverDispatchAsync",connection,releaseDispatchId,null,"Synthetic revoked-owner release",null,CancellationToken.None);
    Check(!Property<bool>(suppressedDelivery,"Sent") && Property<string>(suppressedDelivery,"Status")=="suppressed"
        && await Scalar<string>(connection,$"SELECT delivery_status FROM project_notification_dispatches WHERE project_notification_dispatch_id='{releaseDispatchId}';")=="suppressed",
        "revoked-source release is finalized as suppressed instead of stranded sending, without reading transport configuration or invoking email/Teams");
    await Sql(connection,$"UPDATE app_user_role_assignments SET is_active=true WHERE user_id='{User(1)}';");
    await Sql(connection,$"UPDATE app_user_role_assignments SET is_active=false WHERE user_id='{User(3)}';");
    Check((await Resolve(connection,notification)).GetProperty("Recipients").GetArrayLength()==1,"reported manager without current workspace role is excluded");
    Check(!Property<bool>(await Preflight(connection,releaseDispatch),"Current"),"manual retry rejects its stored manager after role revocation");
    await Sql(connection,$"UPDATE app_user_role_assignments SET is_active=true WHERE user_id='{User(3)}';");
    await Sql(connection,$"UPDATE enterprise_notification_events SET source_event_id='999999999' WHERE enterprise_notification_event_id='{notificationId}';");
    Check((await Resolve(connection,await LoadEvent(connection,notificationId))).GetProperty("Recipients").GetArrayLength()==0,"notification must match the retained handoff audit identity");
    await Sql(connection,$"UPDATE enterprise_notification_events SET source_event_id=payload->>'sourceAuditEventId' WHERE enterprise_notification_event_id='{notificationId}';");
    await Sql(connection,$$"""
        UPDATE enterprise_notification_events SET payload=payload||'{"recipientEmails":["outside@example.invalid"],"recipientUserIds":["{{User(7)}}"]}'::jsonb WHERE enterprise_notification_event_id='{{notificationId}}';
        """);
    Check((await Resolve(connection,await LoadEvent(connection,notificationId))).GetProperty("Recipients").GetArrayLength()==2,"payload cannot inject arbitrary recipients or broaden the team");
    await Sql(connection,$"UPDATE enterprise_notification_events SET ingestion_source='signed_api' WHERE enterprise_notification_event_id='{notificationId}';");
    Check((await Resolve(connection,await LoadEvent(connection,notificationId))).GetProperty("Recipients").GetArrayLength()==0,"external signed producers cannot impersonate a native SOW/GSD handoff");
    await Sql(connection,$"UPDATE enterprise_notification_events SET ingestion_source='native_bridge',subject_user_id='{User(2)}' WHERE enterprise_notification_event_id='{notificationId}';");
    Check((await Resolve(connection,await LoadEvent(connection,notificationId))).GetProperty("Recipients").GetArrayLength()==0,"stale owner event cannot notify previous owner or their manager");
    Check(!Property<bool>(await Preflight(connection,releaseDispatch),"Current"),"stored dispatch is suppressed when the event no longer names the current owner");
    await Sql(connection,$"UPDATE enterprise_notification_events SET subject_user_id='{User(1)}' WHERE enterprise_notification_event_id='{notificationId}'; UPDATE app_users SET email='USER1@example.invalid' WHERE user_id='{User(3)}';");
    Check((await Resolve(connection,await LoadEvent(connection,notificationId))).GetProperty("Recipients").GetArrayLength()==1,"same mailbox across owner and manager is deduplicated");
    Check(!Property<bool>(await Preflight(connection,releaseDispatch),"Current"),"manual retry rejects a changed directory mailbox instead of sending to the obsolete address");
    await Sql(connection,$"UPDATE app_users SET email='Display <outside@example.invalid>' WHERE user_id='{User(1)}'; UPDATE app_users SET email='Display <OUTSIDE@example.invalid>' WHERE user_id='{User(3)}';");
    Check((await Resolve(connection,await LoadEvent(connection,notificationId))).GetProperty("Recipients").GetArrayLength()==0,"malformed directory mailbox rejected without using client alternatives");
    await Sql(connection,"UPDATE enterprise_notification_policies SET enabled=false,delivery_boundary='locked' WHERE policy_code='MODULE025_COVERAGE_STARTED';");
    await Sql(connection,migration120);
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        var result=await Queue(connection,transaction,record,"coverage_started");
        Check(Property<string>(result,"Status")=="suppressed","disabled policy retains durable suppressed evidence");
        await transaction.CommitAsync();
    }
    Check(await Scalar<string>(connection,"SELECT delivery_boundary FROM enterprise_notification_policies WHERE policy_code='MODULE025_COVERAGE_STARTED';")=="locked","migration replay preserves saved delivery boundary");
    await Sql(connection,$$"""
        INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,evidence_json)
        VALUES('{{record}}','handoff_acknowledged','{{User(1)}}',1,'{}');
        """);
    await using(var transaction=await connection.BeginTransactionAsync())
    {
        var returned=await Queue(connection,transaction,record,"coverage_returned");
        var acknowledged=await Queue(connection,transaction,record,"coverage_acknowledged");
        Check(Property<string>(returned,"Status")=="queued" && Property<string>(acknowledged,"Status")=="queued"
            && Property<Guid>(returned,"EventId")!=Property<Guid>(acknowledged,"EventId"),"return and acknowledgement have separate durable notification identities");
        await transaction.CommitAsync();
    }
    await Sql(connection,await File.ReadAllTextAsync(Path.Combine(root,"database","rollback","120_module025_handoff_notifications_rollback.sql")));
    Check(Property<string>(await Preflight(connection,releaseDispatch),"DiagnosticCode")=="MODULE025_HANDOFF_POLICY_DISABLED","manual release honors a subsequently disabled handoff policy");
    Check(await Scalar<long>(connection,"SELECT count(*) FROM enterprise_notification_events;")==4 && await Scalar<long>(connection,"SELECT count(*) FROM enterprise_notification_policies WHERE enabled;")==0,"rollback disables policies while retaining all delivery evidence");
    Check(Status("dispatched","")=="recorded" && Status("dispatched","queued")=="queued" && Status("dispatched","sent")=="sent" && Status("failed","")=="failed","status does not confuse orchestration dispatch with provider-confirmed delivery");
    Console.WriteLine($"{count} handoff notification checks passed; no email or Teams transport invoked.");
}
finally { await Sql(admin,$"DROP DATABASE {name} WITH (FORCE);"); }

static Guid User(int n)=>Guid.Parse($"00000000-0000-0000-0000-{n:D12}");
void Check(bool pass,string label){ if(!pass)throw new Exception("FAIL "+label);count++;Console.WriteLine("PASS "+label); }
static async Task Sql(NpgsqlConnection connection,string sql){await using var command=new NpgsqlCommand(sql,connection);await command.ExecuteNonQueryAsync();}
static async Task<T> Scalar<T>(NpgsqlConnection connection,string sql){await using var command=new NpgsqlCommand(sql,connection);return (T)(await command.ExecuteScalarAsync())!;}
static T Property<T>(object value,string name)=>(T)value.GetType().GetProperty(name)!.GetValue(value)!;
static async Task<object> Invoke(Type type,string name,params object?[] args){var task=(Task)type.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args)!;await task;return task.GetType().GetProperty("Result")!.GetValue(task)!;}
static Task<object> Queue(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid id,string kind="ownership_transferred")=>Invoke(typeof(Module025SowGsdModule),"QueueHandoffNotificationAsync",connection,transaction,id,1,kind,User(1),CancellationToken.None);
static async Task<object> LoadEvent(NpgsqlConnection connection,Guid id)
{
    var events=(Array)await Invoke(typeof(Module025SowGsdModule).Assembly.GetType("ProjectTime.Api.Modules.EnterpriseNotificationRepository")!,"LoadRecentEventsAsync",connection,100,CancellationToken.None);
    return events.Cast<object>().Single(row=>Property<Guid>(row,"EventId")==id);
}
static async Task<JsonElement> Resolve(NpgsqlConnection connection,object notification)=>JsonSerializer.SerializeToElement(await Invoke(typeof(Module025SowGsdModule),"ResolveHandoffNotificationRecipientsAsync",connection,notification,CancellationToken.None));
static string Status(string eventStatus,string dispatchStatus)=>(string)typeof(Module025SowGsdModule).GetMethod("HandoffDeliveryStatus",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[eventStatus,dispatchStatus])!;
static async Task<bool> Claim(NpgsqlConnection connection,Guid id)=>(bool)await Invoke(typeof(Module025SowGsdModule).Assembly.GetType("ProjectTime.Api.Modules.ProjectNotificationRepository")!,"TryClaimDispatchDeliveryAsync",connection,id,null,"Synthetic handoff claim test","synthetic-test",CancellationToken.None);
static Task<object> LoadDispatch(NpgsqlConnection connection,Guid id)=>Invoke(typeof(Module025SowGsdModule).Assembly.GetType("ProjectTime.Api.Modules.ProjectNotificationRepository")!,"LoadDispatchAsync",connection,id,CancellationToken.None);
static Task<object> Preflight(NpgsqlConnection connection,object dispatch)=>Invoke(typeof(Module025SowGsdModule),"ValidateHandoffDispatchAsync",connection,dispatch,CancellationToken.None);
