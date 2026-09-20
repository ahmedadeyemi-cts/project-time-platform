using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

internal static class AutomaticPlanningTests
{
    internal static async Task<int> RunAsync(string cs, string root, Guid admin, Guid pmRole)
    {
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; Console.WriteLine("PASS automatic: " + message); }
        async Task<object?> Sql(string sql, params (string, object)[] values)
        {
            await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value);
            return await command.ExecuteScalarAsync();
        }
        await Sql("""
            CREATE TABLE clients(client_id UUID PRIMARY KEY,client_name TEXT);
            ALTER TABLE projects ADD COLUMN client_id UUID,ADD COLUMN start_date DATE,ADD COLUMN end_date DATE,
                ADD COLUMN created_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
            ALTER TABLE project_flowhive_plans ADD COLUMN project_id UUID;
            ALTER TABLE enterprise_notification_policies ADD COLUMN policy_name TEXT,ADD COLUMN category TEXT,
                ADD COLUMN source_module TEXT,ADD COLUMN event_code TEXT,ADD COLUMN trigger_mode TEXT,
                ADD COLUMN recipient_strategy TEXT,ADD COLUMN delivery_boundary TEXT,ADD COLUMN subject_template TEXT,
                ADD COLUMN text_template TEXT,ADD COLUMN producer_contract TEXT,ADD COLUMN source_state TEXT;
            ALTER TABLE enterprise_notification_events ALTER COLUMN enterprise_notification_event_id SET DEFAULT gen_random_uuid();
            ALTER TABLE enterprise_notification_event_history ALTER COLUMN enterprise_notification_event_history_id SET DEFAULT gen_random_uuid();
            ALTER TABLE project_intake_documents ADD COLUMN document_type TEXT,ADD COLUMN ai_timesheet_context_enabled BOOLEAN DEFAULT TRUE,
                ADD COLUMN upload_source TEXT DEFAULT 'local_file',ADD COLUMN pulse_ai_processing_updated_at TIMESTAMPTZ;
            CREATE TABLE pulse_ai_document_processing_jobs(pulse_ai_document_processing_job_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                project_intake_document_id UUID,project_id UUID,actual_user_id UUID,effective_user_id UUID,requested_by_user_id UUID,
                requested_purpose TEXT,priority INT,correlation_id TEXT,job_status TEXT DEFAULT 'queued');
            CREATE UNIQUE INDEX ux_auto_fixture_documents ON pulse_ai_document_processing_jobs(project_intake_document_id)
                WHERE job_status IN ('queued','scanning','extracting','awaiting_ocr','embedding','indexing','retry_wait','cancel_requested');
            ALTER TABLE pulse_ai_document_processing_events ADD COLUMN pulse_ai_document_processing_job_id UUID,
                ADD COLUMN project_id UUID,ADD COLUMN actual_user_id UUID,ADD COLUMN effective_user_id UUID,
                ADD COLUMN event_code TEXT,ADD COLUMN event_status TEXT,ADD COLUMN correlation_id TEXT;
            """);
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/122_flowhive_automatic_first_draft.sql"));
        await Sql(migration); await Sql(migration);
        Check((long)(await Sql("SELECT count(*) FROM schema_migrations WHERE migration_id='122_flowhive_automatic_first_draft'"))! == 1,
            "migration is idempotent");
        Check(!(bool)(await Sql("SELECT enabled FROM project_flowhive_auto_plan_defaults"))!, "installation does not enable automatic generation");
        var pm = Guid.NewGuid(); var stakeholder = Guid.NewGuid();
        await Sql("""
            INSERT INTO app_users(user_id,email) VALUES(@pm,'pm-auto@example.invalid'),(@viewer,'viewer-auto@example.invalid');
            INSERT INTO app_user_role_assignments(user_id,app_role_id) VALUES(@pm,@role);
            UPDATE app_user_role_assignments SET is_active=TRUE WHERE user_id=@pm;
            INSERT INTO app_permissions(app_permission_id,permission_code)
              SELECT gen_random_uuid(),code FROM (VALUES('VIEW_PROJECT_FLOWHIVE_066'),('MANAGE_PROJECT_FLOWHIVE_066')) p(code)
              WHERE NOT EXISTS(SELECT 1 FROM app_permissions WHERE permission_code=code);
            INSERT INTO app_role_permissions(app_role_id,app_permission_id)
              SELECT @role,app_permission_id FROM app_permissions WHERE permission_code IN ('VIEW_PROJECT_FLOWHIVE_066','MANAGE_PROJECT_FLOWHIVE_066');
            """, ("pm", pm), ("viewer", stakeholder), ("role", pmRole));
        async Task<Guid> Project(bool recent = false)
        {
            var id = Guid.NewGuid();
            await Sql("""
                INSERT INTO projects(project_id,project_code,project_name,status,project_manager_user_id,start_date,created_at)
                VALUES(@p,'AUTO-TEST','Synthetic automatic project','active',@pm,'2026-09-20',
                  CASE WHEN @recent THEN clock_timestamp()+INTERVAL '1 second' ELSE NOW()-INTERVAL '1 day' END);
                """, ("p", id), ("pm", pm), ("recent", recent));
            return id;
        }
        async Task<Guid?> Version(Guid p, bool defaults = false) => await Sql(defaults
            ? "SELECT row_version FROM project_flowhive_auto_plan_defaults;"
            : "SELECT row_version FROM project_flowhive_auto_plans WHERE project_id=@p;", ("p", p)) as Guid?;
        async Task Set(Guid p, Guid actor, bool enabled, bool defaults = false)
        {
            var version = await Version(p, defaults);
            await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync();
            await ProjectFlowHiveAiPlannerOrchestrationModule.SetAutomationPreferenceAsync(connection, p, actor,
                new(enabled, version), defaults, default);
        }
        async Task<Guid?> Queue(Guid p)
        {
            await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync();
            return await ProjectFlowHiveAiPlannerOrchestrationModule.TryQueueAutomaticPlanAsync(connection, p, default);
        }
        async Task Enroll()
        {
            await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync();
            await ProjectFlowHiveAiPlannerOrchestrationModule.EnrollNewProjectsAsync(connection, default);
        }
        var project = await Project();
        await Set(project, pm, true);
        Check((bool)(await Sql("SELECT enabled FROM project_flowhive_auto_plans WHERE project_id=@p", ("p",project)))!, "assigned PM can opt in");
        try { await Set(project, stakeholder, true); throw new Exception("Unrelated actor enabled generation"); }
        catch (Exception ex) when (ex.GetType().Name == "PlannerConflict") { Check(true, "unrelated user cannot opt in"); }
        try { await Set(project, pm, true, true); throw new Exception("PM changed global default"); }
        catch (Exception ex) when (ex.GetType().Name == "PlannerConflict") { Check(true, "PM cannot change administrator default"); }
        await using (var db = new NpgsqlConnection(cs))
        {
            await db.OpenAsync();
            try { await ProjectFlowHiveAiPlannerOrchestrationModule.SetAutomationPreferenceAsync(db,project,pm,new(false,Guid.NewGuid()),false,default); throw new Exception("Stale consent accepted"); }
            catch (Exception ex) when (ex.GetType().Name == "PlannerConflict") { Check(true, "stale settings cannot overwrite a newer choice"); }
        }
        var oldProject = await Project();
        await Set(project, admin, true, true);
        var newProject = await Project(true); var optedOut = await Project(true);
        await Set(optedOut, pm, false);
        await Task.WhenAll(Enroll(), Enroll());
        Check((long)(await Sql("SELECT count(*) FROM project_flowhive_auto_plans WHERE project_id=@p",("p",oldProject)))! == 0, "default excludes pre-existing projects");
        Check((long)(await Sql("SELECT count(*) FROM project_flowhive_auto_plans WHERE project_id=@p",("p",newProject)))! == 1, "concurrent enrollment creates one consent record");
        Check(!(bool)(await Sql("SELECT enabled FROM project_flowhive_auto_plans WHERE project_id=@p",("p",optedOut)))!, "default preserves explicit project opt-out");
        Check(await Queue(project) is null && (string)(await Sql("SELECT status FROM project_flowhive_auto_plans WHERE project_id=@p",("p",project)))! == "waiting_documents", "documents wait without starting the generation deadline");
        await Sql("UPDATE projects SET start_date=NULL WHERE project_id=@p",("p",project));
        Check(await Queue(project) is null && (string)(await Sql("SELECT status FROM project_flowhive_auto_plans WHERE project_id=@p",("p",project)))! == "waiting_start_date", "missing date does not fabricate a schedule");
        await Sql("UPDATE projects SET start_date='2026-09-20',project_manager_user_id=NULL WHERE project_id=@p",("p",newProject));
        Check(await Queue(newProject) is null && (string)(await Sql("SELECT status FROM project_flowhive_auto_plans WHERE project_id=@p",("p",newProject)))! == "waiting_pm", "missing PM does not start unattended generation");
        await Sql("UPDATE projects SET start_date='2026-09-20' WHERE project_id=@p",("p",project));
        var oldRoot = Environment.GetEnvironmentVariable(ProjectPulseUploadStorage.CanonicalEnvironmentVariable);
        var fixture = Path.Combine(Path.GetTempPath(), "flowhive-auto-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        Environment.SetEnvironmentVariable(ProjectPulseUploadStorage.CanonicalEnvironmentVariable, fixture);
        try
        {
            var file = Path.Combine(fixture,"scope.txt"); File.WriteAllText(file,"Synthetic automatic SOW");
            var doc=Guid.NewGuid(); var version=Guid.NewGuid(); var register=Guid.NewGuid();
            await Sql("""
                INSERT INTO work_register_documents VALUES(@reg,'sow','active','local_file',@file);
                INSERT INTO pulse_ai_document_versions VALUES(@v,'canonical','ready',@hash,'1');
                INSERT INTO pulse_ai_document_chunks VALUES(@v,TRUE,'ready','Scope','Service Overview','Synthetic automatic scope');
                INSERT INTO project_intake_documents(project_intake_document_id,project_id,document_category,original_file_name,
                  pulse_ai_processing_status,pulse_ai_processing_error_code,pulse_ai_active_version_id,work_register_document_id,
                  pulse_ai_effective_at,uploaded_at,is_active,engineering_visible,ai_timesheet_context_enabled,upload_source)
                VALUES(@doc,@p,'sow','scope.txt','not_requested','',@v,@reg,NOW(),NOW(),TRUE,TRUE,TRUE,'local_file');
                """,("reg",register),("file",file),("v",version),("hash",new string('c',64)),("doc",doc),("p",project));
            Check(await Queue(project) is null, "unprepared document is queued before inference");
            await Queue(project);
            Check((long)(await Sql("SELECT count(*) FROM pulse_ai_document_processing_jobs WHERE project_id=@p",("p",project)))! == 1, "repeated automation sweeps do not duplicate preparation jobs");
            await Sql("UPDATE project_intake_documents SET pulse_ai_processing_status='ready' WHERE project_intake_document_id=@doc",("doc",doc));
            var queued = await Task.WhenAll(Queue(project),Queue(project));
            var run = queued.Single(id => id.HasValue)!.Value;
            Check(queued.Count(id=>id.HasValue)==1 && (long)(await Sql("SELECT count(*) FROM project_flowhive_ai_planner_runs WHERE project_id=@p",("p",project)))! == 1,
                "replicas atomically claim exactly one first-draft run");
            Check((Guid)(await Sql("SELECT actual_actor_user_id FROM project_flowhive_ai_planner_runs WHERE run_id=@r",("r",run)))! == pm,
                "durable run retains the real authorizing PM identity");
            Check((bool)(await Sql("SELECT deadline_at>NOW()+INTERVAL '39 minutes' FROM project_flowhive_ai_planner_runs WHERE run_id=@r",("r",run)))!,
                "generation deadline starts after document readiness");
            Check(await Queue(project) is null, "a claimed first draft cannot start twice");
            await using var db = new NpgsqlConnection(cs); await db.OpenAsync();
            Check(await ProjectFlowHiveAiPlannerOrchestrationModule.AutomaticRunAllowedAsync(db,run,pm,project,default), "active PM consent allows the run");
            var seed = new ProjectFlowHivePlanRequest(project,"AUTO-TEST","Synthetic automatic project","","Synthetic plan","draft",new DateOnly(2026,9,20),null,[],[],[],null,null,"");
            try { await ProjectFlowHiveAiPlannerOrchestrationModule.QueueForActorAsync(db,project,admin,admin,seed,"manual","comprehensive","fixture",default); throw new Exception("Overlapping manual run accepted"); }
            catch (Exception ex) when (ex.GetType().Name == "PlannerConflict") { Check(true,"manual generation cannot race another actor's active automatic run"); }
            await Set(project,pm,false);
            Check(!await ProjectFlowHiveAiPlannerOrchestrationModule.AutomaticRunAllowedAsync(db,run,pm,project,default),"turning off stops late automatic phases and writes");
            Check((string)(await Sql("SELECT phase FROM project_flowhive_ai_planner_runs WHERE run_id=@r",("r",run)))! == "cancelled","turning off durably cancels active generation");
            await Set(project,pm,true);
            Check(await Queue(project) is null && (long)(await Sql("SELECT count(*) FROM project_flowhive_ai_planner_runs WHERE project_id=@p",("p",project)))! == 1,
                "reenabling after cancellation does not silently retry a terminal run");
            // A completed synthetic run tests transactional notification ingestion and canonical recipient validation.
            var receipt=Guid.NewGuid();
            await Sql("""
                INSERT INTO project_flowhive_working_copies(project_id,working_payload,updated_by_user_id,row_version)
                  VALUES(@p,'{}',@pm,@receipt);
                UPDATE project_flowhive_ai_planner_runs SET status='completed',phase='working_draft_ready',completed_at=NOW(),saved_working_row_version=@receipt WHERE run_id=@r;
                """,("p",project),("pm",pm),("receipt",receipt),("r",run));
            await ProjectFlowHiveAiPlannerOrchestrationModule.QueueAutomaticReadyNotificationsAsync(db,default);
            await ProjectFlowHiveAiPlannerOrchestrationModule.QueueAutomaticReadyNotificationsAsync(db,default);
            Check((long)(await Sql("SELECT count(*) FROM enterprise_notification_events WHERE policy_code='FLOWHIVE_FIRST_DRAFT_READY' AND entity_id=@r",("r",run)))! == 1,
                "completion queues one Module 065 event across repeated scans");
            var now=DateTimeOffset.UtcNow;
            var notification=new EnterpriseNotificationEventRow(Guid.NewGuid(),"FLOWHIVE_FIRST_DRAFT_READY","066",run.ToString(),"fixture","flowhive_auto_plan",run,project,pm,now,now,
                JsonSerializer.SerializeToElement(new {}),"native_bridge","pending",null,0,"","",null,now,now);
            Check(await ProjectFlowHiveAiPlannerOrchestrationModule.AutomaticNotificationCurrentAsync(db,notification,default),"notification validates the current PM and saved draft");
            await Sql("UPDATE projects SET project_manager_user_id=@admin WHERE project_id=@p",("admin",admin),("p",project));
            Check(!await ProjectFlowHiveAiPlannerOrchestrationModule.AutomaticNotificationCurrentAsync(db,notification,default),"reassignment prevents delivery to the former PM");
            Check(!await ProjectFlowHiveAiPlannerOrchestrationModule.AutomaticRunAllowedAsync(db,run,pm,project,default),"revoked PM authority blocks execution");
            await Sql("UPDATE projects SET project_manager_user_id=@pm,status='closed' WHERE project_id=@p",("pm",pm),("p",project));
            Check(!await ProjectFlowHiveAiPlannerOrchestrationModule.AutomaticNotificationCurrentAsync(db,notification,default),"closed project suppresses the pending draft notice");
            await Sql("UPDATE projects SET status='active' WHERE project_id=@p",("p",project));
        }
        finally { Environment.SetEnvironmentVariable(ProjectPulseUploadStorage.CanonicalEnvironmentVariable,oldRoot); Directory.Delete(fixture,true); }
        await Set(newProject,admin,false);
        await Set(oldProject,admin,true);
        await Sql("INSERT INTO project_flowhive_plans(plan_id,project_id) VALUES(gen_random_uuid(),@p)",("p",oldProject));
        Check(await Queue(oldProject) is null && (string)(await Sql("SELECT status FROM project_flowhive_auto_plans WHERE project_id=@p",("p",oldProject)))! == "existing_plan",
            "immutable plan history prevents automatic replacement");
        await Set(project,admin,false,true);
        var afterDisable=await Project(true); await Enroll();
        Check((long)(await Sql("SELECT count(*) FROM project_flowhive_auto_plans WHERE project_id=@p",("p",afterDisable)))! == 0,"disabled default does not enroll new projects");
        // Exercise the actual route's view-as guard and read-only projection.
        async Task<(int Code,JsonElement Body)> Endpoint(string name, object?[] args)
        {
            var method=typeof(ProjectFlowHiveAiPlannerOrchestrationModule).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!;
            var task=(Task<IResult>)method.Invoke(null,args)!; var result=await task;
            var context=new DefaultHttpContext { RequestServices=new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider() };
            context.Response.Body=new MemoryStream(); await result.ExecuteAsync(context); context.Response.Body.Position=0;
            using var document=await JsonDocument.ParseAsync(context.Response.Body);
            return(context.Response.StatusCode,document.RootElement.Clone());
        }
        DefaultHttpContext Context(Guid actual,Guid effective)
        {
            var c=new DefaultHttpContext(); c.Items["ProjectPulseSessionUserId"]=actual;
            c.Items["ProjectPulseActualUserId"]=actual;c.Items["ProjectPulseEffectiveUserId"]=effective;return c;
        }
        var viewAs=await Endpoint("SetAutomationAsync",[project,new FlowHiveAutomationRequest(false,await Version(project)),Context(admin,pm),false,CancellationToken.None]);
        Check(viewAs.Code==403,"View-As cannot change automation through the real endpoint");
        var auditBefore=(long)(await Sql("SELECT count(*) FROM project_flowhive_auto_plan_events"))!;
        var read=await Endpoint("GetAutomationAsync",[project,Context(pm,pm),CancellationToken.None]);
        Check(read.Code==200 && read.Body.GetProperty("status").GetString()=="ready_for_review" && !read.Body.TryGetProperty("plan",out _)
            && !read.Body.ToString().Contains("Synthetic automatic scope"),"status exposes safe progress without private scope or plan content");
        Check((long)(await Sql("SELECT count(*) FROM project_flowhive_auto_plan_events"))! == auditBefore,"status GET does not enroll or mutate automation");
        await Sql(File.ReadAllText(Path.Combine(root,"scripts/release-test/verify-flowhive-automatic-first-draft.sql")).Replace("\\set ON_ERROR_STOP on", ""));
        Check(true,"protected migration verifier validates automation and notification boundaries");
        return count;
    }
}
