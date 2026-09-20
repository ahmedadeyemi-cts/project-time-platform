using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Modules;

// Exercise the actual transaction, current reporting authority and PostgreSQL
// guards in a throwaway database. No live authoring record or external service.
var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName,"database","migrations"))) directory=directory.Parent;
var root = directory?.FullName ?? throw new Exception("Run from repository root.");
var settings = new NpgsqlConnectionStringBuilder { Host="127.0.0.1", Port=int.Parse(Environment.GetEnvironmentVariable("PGPORT")??"5432"),
    Database="postgres",Username="postgres",Pooling=false, Password=Environment.GetEnvironmentVariable("PGPASSWORD")??throw new Exception("Disposable PostgreSQL PGPASSWORD is required.") };
var databaseName = "module025_tracking_test_"+Guid.NewGuid().ToString("N");
await using var administration = new NpgsqlConnection(settings.ConnectionString);
await administration.OpenAsync();
await Sql(administration,$"CREATE DATABASE {databaseName};");
settings.Database=databaseName;
var count=0;
try
{
    await using var connection=await Open();
    foreach(var migration in new[]{"001_initial_schema","099_module025_sow_gsd_workspace","106_module025_sow_sell_register","109_module025_project_name","110_module025_ungenerated_draft_delete","116_module025_governed_ownership_transfer","118_module025_work_tracking","118_module025_work_tracking"})
        await Sql(connection,await File.ReadAllTextAsync(Path.Combine(root,"database","migrations",migration+".sql")));
    await Sql(connection,"""
        ALTER TABLE app_users ADD COLUMN department_name text, ADD COLUMN team_name text;
        CREATE TABLE app_roles(app_role_id uuid PRIMARY KEY,role_code text,is_active boolean);
        CREATE TABLE app_user_role_assignments(user_id uuid,app_role_id uuid,is_active boolean);
        INSERT INTO app_users(user_id,email,display_name,is_active,department_name,team_name)
            SELECT ('00000000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid,
                'user'||n||'@example.invalid','User '||n,n<>4,'Engineering','Same display team'
            FROM generate_series(1,12) n;
        INSERT INTO app_roles VALUES('10000000-0000-0000-0000-000000000001','SOLUTION_ARCHITECT',true),
            ('10000000-0000-0000-0000-000000000002','MANAGER',true);
        INSERT INTO app_user_role_assignments SELECT user_id,
            CASE WHEN display_name IN ('User 10','User 11') THEN '10000000-0000-0000-0000-000000000002'::uuid
                ELSE '10000000-0000-0000-0000-000000000001'::uuid END,true FROM app_users;
        INSERT INTO reporting_relationships(employee_user_id,manager_user_id,team_lead_user_id,effective_start_date)
            VALUES('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000010','00000000-0000-0000-0000-000000000008',CURRENT_DATE-10),
                ('00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000010',NULL,CURRENT_DATE-10),
                ('00000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000011',NULL,CURRENT_DATE-10);
        """);
    var owner=Access(1);
    var manager=Access(10,manager:true,visible:[1,2]);
    var otherManager=Access(11,manager:true,visible:[3]);
    var teamLead=Access(8,manager:true,visible:[1,2]);
    var admin=Access(12,admin:true,visible:[1,2,3]);
    var id=await Draft(connection);
    var baseline=await Scalar<string>(connection,"SELECT to_jsonb(e)::text FROM module025_sow_gsd_engagements e WHERE engagement_id=@id;",id);
    var blockerOwners=(IEnumerable)(await Invoke("LoadWorkTrackingBlockerOwnersAsync",connection,null,User(1),CancellationToken.None))!;
    Check(blockerOwners.Cast<object>().Select(row=>(Guid)Property(row,"UserId")!).Order().SequenceEqual(new[]{User(1),User(10)}.Order()),"blocker owners are current author and direct manager, not arbitrary teammates or team leads");
    Check(await Save(connection,id,owner,Request(priority:"high",hours:2.5m))==200,"SA can set a due date, priority and remaining authoring effort");
    Check(await Scalar<int>(connection,"SELECT max(tracking_revision) FROM module025_work_tracking_events WHERE engagement_id=@id;",id)==1,"tracking starts its own revision sequence");
    Check(await Scalar<string>(connection,"SELECT to_jsonb(e)::text FROM module025_sow_gsd_engagements e WHERE engagement_id=@id;",id)==baseline,"scheduling does not change document revision, status, generation or timestamps");
    Check(await Scalar<long>(connection,"SELECT count(*) FROM module025_sow_gsd_events WHERE engagement_id=@id AND event_type='work_tracking_updated';",id)==1,"canonical history links the immutable scheduling snapshot");
    Check(await Save(connection,id,owner,Request(1,priority:"high",hours:2.5m))==200,"identical saves are idempotent");
    Check(await Scalar<long>(connection,"SELECT count(*) FROM module025_work_tracking_events WHERE engagement_id=@id;",id)==1,"no-op save leaves no duplicate audit revision");
    Check(await Save(connection,id,owner,Request(0))==409,"stale tracking revision rejects overwrite");
    Check(await Save(connection,id,otherManager,Request(1))==403,"other team's manager cannot update matching display-team record");
    Check(await Save(connection,id,teamLead,Request(1))==403,"team lead read scope does not grant managerial scheduling writes");
    Check(await Save(connection,id,Access(1,viewAs:true),Request(1))==403,"View As owner remains read only");
    Check(await Save(connection,id,Access(12,admin:true,viewAs:true),Request(1))==403,"View As admin remains read only");
    Check(await Save(connection,id,owner,Request(1,blocker:"Waiting for customer inventory",blockerOwner:3))==403,"cross-team blocker recipient is rejected");
    Check(await Save(connection,id,owner,Request(1,blocker:"Waiting for customer inventory",blockerOwner:2))==403,"same-team non-owner is not assigned an inaccessible blocker");
    await Sql(connection,$"UPDATE app_users SET is_active=FALSE WHERE user_id='{User(10)}';");
    Check(await Save(connection,id,owner,Request(1,blocker:"Waiting for customer inventory",blockerOwner:10))==403,"inactive manager cannot be assigned an unresolved blocker");
    await Sql(connection,$"UPDATE app_users SET is_active=TRUE WHERE user_id='{User(10)}';");
    await Sql(connection,$"UPDATE app_user_role_assignments SET is_active=FALSE WHERE user_id='{User(10)}';");
    Check(await Save(connection,id,owner,Request(1,blocker:"Waiting for customer inventory",blockerOwner:10))==403,"manager without workspace access cannot receive an inaccessible blocker");
    await Sql(connection,$"UPDATE app_user_role_assignments SET is_active=TRUE WHERE user_id='{User(10)}';");
    Check(await Save(connection,id,manager,Request(1,priority:"urgent",blocker:"Customer inventory required",blockerOwner:10,hours:4))==200,"current direct manager can prioritize and take accountability for a blocker");
    Check(await Scalar<Guid>(connection,"SELECT blocker_owner_user_id FROM module025_work_tracking_events WHERE engagement_id=@id ORDER BY tracking_revision DESC LIMIT 1;",id)==User(10),"blocker assignment uses server-validated user identity");
    foreach(var invalid in new[]{Request(2,priority:"critical"),Request(2,hours:-1),Request(2,hours:1001),Request(2,hours:1.001m),
        Request(2,blocker:"Missing owner"),Request(2,blockerOwner:10),Request(2,blocker:new string('x',2001),blockerOwner:10),
        Request(2) with { TargetDate=new DateOnly(2101,1,1) },Request(-1)})
        Check(await Save(connection,id,owner,invalid)==400,"invalid tracking values are rejected without audit changes");
    Check(await Scalar<long>(connection,"SELECT count(*) FROM module025_work_tracking_events WHERE engagement_id=@id;",id)==2,"rejected requests do not append misleading scheduling history");
    await Sql(connection,$"UPDATE reporting_relationships SET effective_end_date=CURRENT_DATE-1 WHERE employee_user_id='{User(1)}';");
    Check(await Save(connection,id,manager,Request(2))==403,"expired manager assignment is rechecked instead of trusting stale visible IDs");
    await Sql(connection,$"UPDATE reporting_relationships SET effective_end_date=NULL,effective_start_date=CURRENT_DATE+1 WHERE employee_user_id='{User(1)}';");
    Check(await Save(connection,id,manager,Request(2))==403,"future manager assignment grants no write access");
    await Sql(connection,$"UPDATE reporting_relationships SET effective_start_date=CURRENT_DATE-1 WHERE employee_user_id='{User(1)}';");
    Check(await Save(connection,id,owner,Request(2,hours:0))==200,"owner can resolve the blocker and explicitly set zero remaining preparation work");
    Check(await Scalar<long>(connection,"SELECT count(*) FROM module025_work_tracking_events WHERE engagement_id=@id AND blocker_reason<>'';",id)==1,"resolving blocker retains its earlier immutable snapshot");
    await RejectSql(connection,$"UPDATE module025_work_tracking_events SET priority='low' WHERE engagement_id='{id}';","scheduling history cannot be rewritten");
    await RejectSql(connection,$"DELETE FROM module025_work_tracking_events WHERE engagement_id='{id}';","scheduling history cannot be deleted");
    await RejectSql(connection,"TRUNCATE module025_work_tracking_events;","scheduling history cannot be truncated");
    var race=await Draft(connection);
    await using(var first=await Open()) await using(var second=await Open())
    {
        var outcomes=await Task.WhenAll(Save(first,race,owner,Request(priority:"high")),Save(second,race,manager,Request(priority:"urgent")));
        Check(outcomes.Order().SequenceEqual(new[]{200,409}),"concurrent SA/manager scheduling saves yield one winner and one reload conflict");
    }
    Check(await Scalar<long>(connection,"SELECT count(*) FROM module025_work_tracking_events WHERE engagement_id=@id;",race)==1,"concurrent loser leaves no partial audit");
    var other=await Draft(connection,owner:3);
    var ownRows=(IEnumerable)(await Invoke("LoadScopedWorkTrackingAsync",connection,new[]{id,race,other},owner,CancellationToken.None))!;
    var otherRows=(IEnumerable)(await Invoke("LoadScopedWorkTrackingAsync",connection,new[]{id,race,other},otherManager,CancellationToken.None))!;
    Check(ownRows.Cast<object>().Count()==2 && otherRows.Cast<object>().Count()==1,"bulk tracking respects current author/manager read scope across both SA teams");
    var confirmed=await Draft(connection,status:"confirmed");
    var confirmedBefore=await Scalar<string>(connection,"SELECT to_jsonb(e)::text FROM module025_sow_gsd_engagements e WHERE engagement_id=@id;",confirmed);
    Check(await Save(connection,confirmed,manager,Request(priority:"high"))==200,"manager may track scheduling on confirmed package without reopening documents");
    Check(await Scalar<string>(connection,"SELECT to_jsonb(e)::text FROM module025_sow_gsd_engagements e WHERE engagement_id=@id;",confirmed)==confirmedBefore,"confirmed identity, revision and timestamps are unchanged");
    var archived=await Draft(connection,status:"archived");
    Check(await Save(connection,archived,admin,Request())==409,"archived package remains read only even to scheduling administrator");
    var lastActivity=(DateTimeOffset)(await Invoke("WorkTrackingLastActivityAsync",connection,null,id,DateTimeOffset.UtcNow.AddDays(-7),CancellationToken.None))!;
    Check(lastActivity<DateTimeOffset.UtcNow.AddDays(-6),"metadata edits do not reset the workflow activity timer");
    var deletion=(IResult)(await Invoke("DeleteDraftOwnershipCheckedAsync",connection,id,1,owner,CancellationToken.None))!;
    Check(((IStatusCodeHttpResult)deletion).StatusCode==200,"eligible ungenerated draft deletion remains available after scheduling edits");
    Check(await Scalar<long>(connection,"SELECT count(*) FROM module025_work_tracking_events WHERE engagement_id=@id;",id)==3,"permitted draft deletion retains immutable scheduling history");
    await RejectSql(connection,$"INSERT INTO module025_work_tracking_events SELECT gen_random_uuid(),engagement_id,engagement_number,4,document_revision,owner_user_id,owner_display_name,target_date,priority,blocker_reason,blocker_owner_user_id,blocker_owner_display_name,authoring_hours,actor_user_id,actor_display_name,now() FROM module025_work_tracking_events WHERE engagement_id='{id}' AND tracking_revision=3;","cannot append work tracking for a deleted workspace");
    await Sql(connection,await File.ReadAllTextAsync(Path.Combine(root,"database","rollback","118_module025_work_tracking_rollback.sql")));
    Check(await Save(connection,race,owner,Request(1))==409,"rolled back migration reports unavailable rather than writing legacy schema");
    await RejectSql(connection,"DELETE FROM module025_work_tracking_events;","rollback leaves retained audit protected");
    await Sql(connection,await File.ReadAllTextAsync(Path.Combine(root,"database","migrations","118_module025_work_tracking.sql")));
    Check(await Scalar<long>(connection,"SELECT count(*) FROM schema_migrations WHERE migration_id='118_module025_work_tracking';")==1,"migration reapply is idempotent");
    Console.WriteLine($"{count} PostgreSQL work tracking checks passed; no external delivery or AI requests occurred.");
}
finally { await Sql(administration,$"DROP DATABASE {databaseName} WITH (FORCE);"); }

static Guid User(int id)=>Guid.Parse($"00000000-0000-0000-0000-{id:D12}");
void Check(bool pass,string label){if(!pass)throw new Exception("FAIL "+label);count++;Console.WriteLine("PASS "+label);}
static object? Property(object value,string name)=>value.GetType().GetProperty(name)!.GetValue(value);
async Task<NpgsqlConnection> Open(){var connection=new NpgsqlConnection(settings.ConnectionString);await connection.OpenAsync();return connection;}
static async Task Sql(NpgsqlConnection connection,string sql){await using var command=new NpgsqlCommand(sql,connection);await command.ExecuteNonQueryAsync();}
static async Task<T> Scalar<T>(NpgsqlConnection connection,string sql,Guid? id=null){await using var command=new NpgsqlCommand(sql,connection);if(id.HasValue)command.Parameters.AddWithValue("id",id.Value);return (T)(await command.ExecuteScalarAsync())!;}
async Task RejectSql(NpgsqlConnection connection,string sql,string label){var rejected=false;try{await Sql(connection,sql);}catch(PostgresException){rejected=true;await Sql(connection,"ROLLBACK;");}Check(rejected,label);}
static object Access(int user,bool manager=false,bool admin=false,bool viewAs=false,int[]? visible=null)
{
    var type=typeof(Module025SowGsdModule).Assembly.GetType("ProjectTime.Api.Modules.Module025AccessContext",true)!;
    return type.GetConstructors().Single().Invoke([User(user),User(user),"User "+user,$"user{user}@example.invalid","Engineering","Same display team",
        new HashSet<string>{admin?"SUPER_ADMINISTRATOR":manager?"MANAGER":"SOLUTION_ARCHITECT"},viewAs,admin,!manager&&!admin,false,manager,new HashSet<Guid>((visible??[user]).Select(User))]);
}
static async Task<object?> Invoke(string method,params object?[] args){var task=(Task)typeof(Module025SowGsdModule).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;await task;return task.GetType().GetProperty("Result")!.GetValue(task);}
static Module025WorkTrackingRequest Request(int revision=0,string priority="normal",string? blocker=null,int? blockerOwner=null,decimal? hours=null)=>new(revision,new DateOnly(2026,10,1),priority,blocker,blockerOwner.HasValue?User(blockerOwner.Value):null,hours);
static async Task<int> Save(NpgsqlConnection connection,Guid id,object actor,Module025WorkTrackingRequest request){var result=(IResult)(await Invoke("SaveWorkTrackingCoreAsync",connection,id,request,actor,CancellationToken.None))!;return ((IStatusCodeHttpResult)result).StatusCode??200;}
static async Task<Guid> Draft(NpgsqlConnection connection,string status="draft",int owner=1)
{
    var id=Guid.NewGuid();await using var command=new NpgsqlCommand("""
        INSERT INTO module025_sow_gsd_engagements(engagement_id,owner_user_id,owner_display_name,owner_department_name,owner_team_name,customer_name,customer_entry_mode,status,is_active)
        VALUES(@id,@owner,@name,'Engineering','Same display team','Synthetic customer','manual',@status,@active);
        """,connection);
    command.Parameters.AddWithValue("id",id);command.Parameters.AddWithValue("owner",User(owner));command.Parameters.AddWithValue("name","User "+owner);command.Parameters.AddWithValue("status",status);command.Parameters.AddWithValue("active",status!="archived");await command.ExecuteNonQueryAsync();return id;
}
