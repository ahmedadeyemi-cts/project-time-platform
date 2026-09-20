using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Modules;

// Actual transfer implementation and actual PostgreSQL triggers, with no HTTP
// server, integration delivery, or business database. Every run owns a new DB.
var root = FindRoot();
var settings = new NpgsqlConnectionStringBuilder {
    Host = "127.0.0.1", Port = int.Parse(Environment.GetEnvironmentVariable("PGPORT") ?? "5432"),
    Database = "postgres", Username = "postgres", Pooling = false,
    Password = Environment.GetEnvironmentVariable("PGPASSWORD") ?? throw new Exception("Disposable PostgreSQL PGPASSWORD is required.")
};
var databaseName = "module025_transfer_test_" + Guid.NewGuid().ToString("N");
await using var administration = new NpgsqlConnection(settings.ConnectionString);
await administration.OpenAsync();
await Sql(administration, $"CREATE DATABASE {databaseName};");
settings.Database = databaseName;
var count = 0;
try
{
    await using var connection = await Open();
    foreach (var migration in new[] { "001_initial_schema", "099_module025_sow_gsd_workspace", "106_module025_sow_sell_register", "109_module025_project_name", "110_module025_ungenerated_draft_delete", "116_module025_governed_ownership_transfer", "116_module025_governed_ownership_transfer" })
        await Sql(connection, await File.ReadAllTextAsync(Path.Combine(root, "database", "migrations", migration + ".sql")));
    await Sql(connection, """
        ALTER TABLE app_users ADD COLUMN department_name text, ADD COLUMN team_name text;
        CREATE TABLE app_roles(app_role_id uuid PRIMARY KEY, role_code text, is_active boolean);
        CREATE TABLE app_user_role_assignments(user_id uuid, app_role_id uuid, is_active boolean);
        INSERT INTO app_users(user_id,email,display_name,is_active,department_name,team_name)
            SELECT ('00000000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid,
                'user'||n||'@example.invalid','User '||n,n<>4,'Engineering','Identical display team'
            FROM generate_series(1,12) n;
        INSERT INTO app_roles VALUES
            ('10000000-0000-0000-0000-000000000001','SOLUTION_ARCHITECT',true),
            ('10000000-0000-0000-0000-000000000002','MANAGER',true),
            ('10000000-0000-0000-0000-000000000003','SUPER_ADMINISTRATOR',true);
        INSERT INTO app_user_role_assignments SELECT user_id,'10000000-0000-0000-0000-000000000001',true FROM app_users
            WHERE user_id IN ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002',
            '00000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000004',
            '00000000-0000-0000-0000-000000000006','00000000-0000-0000-0000-000000000007',
            '00000000-0000-0000-0000-000000000008','00000000-0000-0000-0000-000000000009');
        INSERT INTO app_user_role_assignments SELECT user_id,'10000000-0000-0000-0000-000000000002',true FROM app_users
            WHERE user_id IN ('00000000-0000-0000-0000-000000000010','00000000-0000-0000-0000-000000000011');
        INSERT INTO app_user_role_assignments VALUES ('00000000-0000-0000-0000-000000000012','10000000-0000-0000-0000-000000000003',true);
        INSERT INTO reporting_relationships(employee_user_id,manager_user_id,effective_start_date,effective_end_date)
            SELECT user_id, CASE WHEN user_id='00000000-0000-0000-0000-000000000003' THEN
                '00000000-0000-0000-0000-000000000011'::uuid ELSE '00000000-0000-0000-0000-000000000010'::uuid END,
                CASE WHEN user_id='00000000-0000-0000-0000-000000000007' THEN CURRENT_DATE+1 ELSE CURRENT_DATE-10 END,
                CASE WHEN user_id='00000000-0000-0000-0000-000000000006' THEN CURRENT_DATE-1 ELSE NULL END
            FROM app_users WHERE user_id < '00000000-0000-0000-0000-000000000010' AND user_id <> '00000000-0000-0000-0000-000000000008';
        """);
    var owner = Access(1);
    var manager = Access(10, manager: true, visible: [1,2,9]);
    var otherManager = Access(11, manager: true, visible: [3]);
    var admin = Access(12, admin: true, visible: [1,2,3,9]);
    var teamLead = Access(8, manager: true, visible: [1,2,9]);
    await Sql(connection, $"UPDATE reporting_relationships SET team_lead_user_id='{User(8)}' WHERE manager_user_id='{User(10)}';");
    Check(!(await Destinations(connection, teamLead, User(1))).Any(), "team-lead read scope does not grant manager transfer authority");
    Check((bool)(await Invoke("HasTemplateManagementScopeAsync", connection, manager, CancellationToken.None))!, "current reporting manager may maintain template candidates");
    Check(!(bool)(await Invoke("HasTemplateManagementScopeAsync", connection, teamLead, CancellationToken.None))!, "team-lead read scope does not grant manager template authority");
    var options = await Destinations(connection, owner, User(1));
    Check(options.Order().SequenceEqual(new[] { User(2), User(9) }.Order()), "destinations use current manager relationships, not matching team labels; inactive, non-SA, expired, future and unassigned users excluded");
    var systems = (IReadOnlySet<Guid>)(await Invoke("LoadDirectReportSolutionArchitectIdsAsync", connection, User(10), "Engineering", CancellationToken.None))!;
    var networking = (IReadOnlySet<Guid>)(await Invoke("LoadDirectReportSolutionArchitectIdsAsync", connection, User(11), "Engineering", CancellationToken.None))!;
    Check(systems.SetEquals([User(1), User(2), User(9)]) && networking.SetEquals([User(3)]), "Systems and Collaboration/Networking manager scopes remain separate");

    var draft = await Draft(connection);
    Check(await Transfer(connection, draft, owner, 3) == 403, "same display team never authorizes cross-manager self transfer");
    Check(await Transfer(connection, draft, admin, 3) == 403, "administrator transfer also respects same reporting manager");
    Check(await Transfer(connection, draft, otherManager, 2) == 403, "another team's manager cannot transfer the draft");
    Check(await Transfer(connection, draft, Access(1, viewAs: true), 2) == 403, "View As remains read only");
    Check(await Transfer(connection, draft, owner, 2, revision: 8) == 409, "stale expected revision is rejected");
    Check(await Scalar<long>(connection, "SELECT count(*) FROM module025_sow_gsd_events WHERE engagement_id=@id;", draft) == 0, "rejected transfers append no misleading history");
    await RejectSql(connection, $"UPDATE module025_sow_gsd_engagements SET owner_user_id='{User(2)}' WHERE engagement_id='{draft}';", "database denies owner change without a matching same-transaction event");
    Check(await Transfer(connection, draft, owner, 2) == 200, "active SA can transfer their draft to a teammate");
    Check(await Scalar<Guid>(connection, "SELECT owner_user_id FROM module025_sow_gsd_engagements WHERE engagement_id=@id;", draft) == User(2), "working ownership moves in place");
    Check(await Scalar<int>(connection, "SELECT revision FROM module025_sow_gsd_engagements WHERE engagement_id=@id;", draft) == 2, "successful transfer advances revision once");
    var evidence = JsonDocument.Parse(await Scalar<string>(connection, "SELECT evidence_json::text FROM module025_sow_gsd_events WHERE engagement_id=@id AND event_type='ownership_transferred';", draft)).RootElement;
    Check(evidence.GetProperty("previousOwnerUserId").GetGuid() == User(1)
        && evidence.GetProperty("newOwnerUserId").GetGuid() == User(2)
        && evidence.GetProperty("transferredByUserId").GetGuid() == User(1)
        && evidence.GetProperty("reason").GetString() == "PTO coverage for estimate completion"
        && evidence.GetProperty("reportingManagerUserIds")[0].GetGuid() == User(10), "immutable handoff event captures old/new owners, actor, reason and reporting scope");
    Check(!AccessFlag(owner, "CanWriteOwned", User(2)) && !AccessFlag(owner, "CanViewOwned", User(2))
        && AccessFlag(Access(2), "CanWriteOwned", User(2)) && AccessFlag(manager, "CanViewOwned", User(2))
        && !AccessFlag(otherManager, "CanViewOwned", User(2)), "former owner loses access; successor gains edit access; reporting managers retain scoped visibility");
    await RejectSql(connection, $"UPDATE module025_sow_gsd_events SET summary='changed' WHERE engagement_id='{draft}';", "transfer event cannot be rewritten");
    await RejectSql(connection, $"BEGIN; SELECT set_config('projectpulse.module025_allow_draft_delete','on',true); DELETE FROM module025_sow_gsd_events WHERE engagement_id='{draft}'; COMMIT;", "draft-delete exception cannot erase transferred-draft evidence");
    Check(await DeleteDraft(connection, draft, owner, revision: 1) == 403, "stale former-owner delete is reauthorized under the row lock");
    Check(await DeleteDraft(connection, draft, Access(2), revision: 2) == 409, "successor receives archive guidance instead of deleting transfer evidence");
    Check(await Transfer(connection, draft, manager, 9, revision: 2) == 200, "manager can reassign a team member's draft for PTO");
    Check(await Scalar<long>(connection, "SELECT count(*) FROM module025_sow_gsd_events WHERE engagement_id=@id AND event_type='ownership_transferred';", draft) == 2, "repeated transfers append history on the same record");
    Check(await Transfer(connection, draft, owner, 2, revision: 3) == 403, "former owner cannot transfer after reassignment");

    var racing = await Draft(connection);
    await using (var first = await Open())
    await using (var second = await Open())
    {
        var results = await Task.WhenAll(Transfer(first, racing, manager, 2), Transfer(second, racing, manager, 9));
        Check(results.Order().SequenceEqual(new[] {200,409}), "concurrent manager transfers serialize; exactly one wins the expected revision");
    }
    Check(await Scalar<long>(connection, "SELECT count(*) FROM module025_sow_gsd_events WHERE engagement_id=@id;", racing) == 1, "concurrent loser leaves no orphan transfer event");

    var confirmed = await Draft(connection, status: "confirmed");
    Check(await Transfer(connection, confirmed, manager, 2) == 409, "confirmed version must be reopened before transfer");
    var archived = await Draft(connection, status: "archived");
    Check(await Transfer(connection, archived, manager, 2) == 409, "archived record cannot transfer");
    var generating = await Draft(connection);
    var generation = Guid.NewGuid();
    await Sql(connection, $"INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,evidence_json) VALUES('{generating}','ai_generation_queued','{User(1)}',1,'{{\"generationId\":\"{generation}\"}}');");
    Check(await Transfer(connection, generating, manager, 2) == 409, "queued or running scope generation blocks transfer");
    Check(await DeleteDraft(connection, generating, owner) == 409, "delete eligibility is checked under the shared generation lock");
    await Sql(connection, $"INSERT INTO module025_sow_gsd_events(engagement_id,event_type,actor_user_id,engagement_revision,evidence_json) VALUES('{generating}','ai_generation_failed','{User(1)}',1,'{{\"generationId\":\"{generation}\"}}');");
    Check(await Transfer(connection, generating, manager, 2) == 200, "terminal generation allows handoff without retrying AI");

    var retained = await Draft(connection, status: "review_ready");
    var version = Guid.NewGuid();
    var submission = Guid.NewGuid();
    await Sql(connection, $$"""
        INSERT INTO module025_sow_gsd_versions(version_id,engagement_id,version_number,source_revision,content_sha256,source_json,sow_content,gsd_content,actor_user_id)
        VALUES('{{version}}','{{retained}}',1,1,repeat('a',64),'{"originalAuthor":"User 1"}',convert_to('retained SOW bytes','UTF8'),convert_to('retained GSD bytes','UTF8'),'{{User(1)}}');
        INSERT INTO module025_sow_sell_submissions(submission_id,engagement_id,version_id,destination_key,runtime_environment,actor_user_id,recipients_json)
        VALUES('{{submission}}','{{retained}}','{{version}}','zendesk_sell','test','{{User(1)}}','[{},{},{}]');
        INSERT INTO module025_sow_sell_dispatch(submission_id,engagement_id,sell_status) VALUES('{{submission}}','{{retained}}','queued');
        """);
    foreach (var stage in new[] { "queued", "publishing", "needs_reconciliation" })
    {
        await Sql(connection, $"UPDATE module025_sow_sell_dispatch SET sell_status='{stage}' WHERE engagement_id='{retained}';");
        Check(await Transfer(connection, retained, manager, 2) == 409, stage + " ConnectWise SELL handoff blocks transfer");
    }
    await Sql(connection, $"""
        UPDATE module025_sow_sell_dispatch SET sell_status='published' WHERE engagement_id='{retained}';
        INSERT INTO module025_sow_sell_receipts(submission_id,sell_record_id,sow_document_id,gsd_document_id,sow_sha256,gsd_sha256,provider_receipt_id)
        SELECT '{submission}','synthetic-sell-record','sow-document','gsd-document',sow_sha256,gsd_sha256,'synthetic-receipt' FROM module025_sow_gsd_versions WHERE version_id='{version}';
        """);
    var retainedBefore = await RetainedEvidence(connection, retained);
    Check(await Transfer(connection, retained, manager, 2) == 200, "reopened working draft with completed SELL handoff can transfer");
    Check(await RetainedEvidence(connection, retained) == retainedBefore, "handoff preserves released bytes/hashes, source author and SELL receipt/link identity");
    await RejectSql(connection, $"UPDATE module025_sow_gsd_versions SET actor_user_id='{User(2)}' WHERE engagement_id='{retained}';", "retained document author remains immutable");

    var deletable = await Draft(connection);
    Check(await DeleteDraft(connection, deletable, owner) == 200, "actual delete operation permits untouched ungenerated draft");
    Check(await Scalar<long>(connection, "SELECT count(*) FROM module025_sow_gsd_engagements WHERE engagement_id=@id;", deletable) == 0, "untouched ungenerated drafts remain deletable");
    await Sql(connection, await File.ReadAllTextAsync(Path.Combine(root,"database","rollback","116_module025_governed_ownership_transfer_rollback.sql")));
    await RejectSql(connection, $"BEGIN; SELECT set_config('projectpulse.module025_allow_draft_delete','on',true); DELETE FROM module025_sow_gsd_events WHERE engagement_id='{draft}'; COMMIT;", "rollback disables handoffs while preserving existing transfer evidence");
    await Sql(connection, await File.ReadAllTextAsync(Path.Combine(root,"database","migrations","116_module025_governed_ownership_transfer.sql")));
    Check(await Scalar<long>(connection, "SELECT count(*) FROM schema_migrations WHERE migration_id='116_module025_governed_ownership_transfer';") == 1, "migration reapply remains idempotent");
    Console.WriteLine($"{count} PostgreSQL transfer and retained-evidence checks passed; no external delivery occurred.");
}
finally { await Sql(administration, $"DROP DATABASE {databaseName} WITH (FORCE);"); }

static string FindRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "database", "migrations"))) directory=directory.Parent;
    return directory?.FullName ?? throw new Exception("Run from repository root.");
}
static Guid User(int number) => Guid.Parse($"00000000-0000-0000-0000-{number:D12}");
void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL " + name); count++; Console.WriteLine("PASS " + name); }
async Task<NpgsqlConnection> Open() { var connection=new NpgsqlConnection(settings.ConnectionString); await connection.OpenAsync(); return connection; }
static async Task Sql(NpgsqlConnection connection, string sql) { await using var command=new NpgsqlCommand(sql,connection); await command.ExecuteNonQueryAsync(); }
static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql, Guid? id=null)
{
    await using var command=new NpgsqlCommand(sql,connection);
    if(id.HasValue) command.Parameters.AddWithValue("id",id.Value);
    return (T)(await command.ExecuteScalarAsync())!;
}
async Task RejectSql(NpgsqlConnection connection, string sql, string label)
{
    var rejected=false;
    try { await Sql(connection,sql); } catch(PostgresException) { rejected=true; await Sql(connection,"ROLLBACK;"); }
    Check(rejected,label);
}
static object Access(int user, bool manager=false, bool admin=false, bool viewAs=false, int[]? visible=null)
{
    var type=typeof(Module025SowGsdModule).Assembly.GetType("ProjectTime.Api.Modules.Module025AccessContext",true)!;
    var constructor=type.GetConstructors().Single();
    return constructor.Invoke([User(user),User(user),"User "+user,$"user{user}@example.invalid","Engineering","Identical display team",
        new HashSet<string>{admin ? "SUPER_ADMINISTRATOR" : manager ? "MANAGER" : "SOLUTION_ARCHITECT"}, viewAs,admin,!manager&&!admin,false,manager,
        new HashSet<Guid>((visible??[user]).Select(User))]);
}
static bool AccessFlag(object access,string method,Guid owner) => (bool)access.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(access,[owner])!;
static async Task<object?> Invoke(string method,params object?[] args)
{
    var task=(Task)typeof(Module025SowGsdModule).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
    await task;
    return task.GetType().GetProperty("Result")!.GetValue(task);
}
static async Task<Guid[]> Destinations(NpgsqlConnection connection,object actor,Guid owner)
{
    var rows=(IEnumerable)(await Invoke("LoadTransferDestinationsAsync",connection,null,actor,owner,CancellationToken.None))!;
    return rows.Cast<object>().Select(row=>(Guid)row.GetType().GetProperty("UserId")!.GetValue(row)!).ToArray();
}
static async Task<int> Transfer(NpgsqlConnection connection,Guid id,object actor,int target,int revision=1)
{
    var result=(IResult)(await Invoke("TransferOwnershipAsync",connection,id,
        new Module025SowGsdTransferRequest(User(target),revision,"PTO coverage for estimate completion"),actor,CancellationToken.None))!;
    return ((IStatusCodeHttpResult)result).StatusCode??200;
}
static async Task<int> DeleteDraft(NpgsqlConnection connection,Guid id,object actor,int revision=1)
{
    var result=(IResult)(await Invoke("DeleteDraftOwnershipCheckedAsync",connection,id,revision,actor,CancellationToken.None))!;
    return ((IStatusCodeHttpResult)result).StatusCode??200;
}
static async Task<Guid> Draft(NpgsqlConnection connection,string status="draft")
{
    var id=Guid.NewGuid();
    await using var command=new NpgsqlCommand("""
        INSERT INTO module025_sow_gsd_engagements(engagement_id,owner_user_id,owner_display_name,owner_department_name,owner_team_name,customer_name,customer_entry_mode,status,is_active)
        VALUES(@id,@owner,'User 1','Engineering','Identical display team','Synthetic customer','manual',@status,@active);
        """,connection);
    command.Parameters.AddWithValue("id",id); command.Parameters.AddWithValue("owner",User(1)); command.Parameters.AddWithValue("status",status); command.Parameters.AddWithValue("active",status!="archived");
    await command.ExecuteNonQueryAsync(); return id;
}
static Task<string> RetainedEvidence(NpgsqlConnection connection,Guid id) => Scalar<string>(connection,"""
    SELECT jsonb_build_object('versions',(SELECT jsonb_agg(to_jsonb(v) ORDER BY version_number) FROM module025_sow_gsd_versions v WHERE engagement_id=@id),
        'links',(SELECT jsonb_agg(to_jsonb(l)) FROM module025_sow_sell_links l WHERE engagement_id=@id),
        'receipts',(SELECT jsonb_agg(to_jsonb(r)) FROM module025_sow_sell_receipts r JOIN module025_sow_sell_submissions s USING(submission_id) WHERE s.engagement_id=@id))::text;
    """,id);
