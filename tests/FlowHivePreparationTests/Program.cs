using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Modules;

var count = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
foreach (var status in new[] { "closed", " Completed ", "CANCELLED", "canceled", "archived" })
    Check(ProjectFlowHiveLifecycle.IsArchived(status), "closed lifecycle is archived: " + status);
Check(!ProjectFlowHiveLifecycle.IsArchived("active") && !ProjectFlowHiveLifecycle.IsArchived("on_hold"), "active and on-hold remain outside archive");
var project = Guid.NewGuid(); var actor = Guid.NewGuid();
var readySow = new ProjectPlanningDocumentEvidence(project, Guid.NewGuid(), "sow", "Fixture SOW.pdf", "ready", "", Guid.NewGuid(),
    "canonical", "ready", Guid.NewGuid(), "sow", "active", "local_file", "fixture.pdf", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 4, 2, EngineeringVisible: true);
ProjectPlanningDocumentResolution Resolution(ProjectPlanningDocumentEvidence? sow, params ProjectPlanningDocumentEvidence[] more)
{
    var all = (sow is null ? more : new[] { sow }.Concat(more)).ToArray();
    return new("fixture", all, sow, null, more, all, all.Where(d => !d.ReadyForRetrieval && !d.ProcessingTerminalFailure).ToArray(),
        all.Where(d => d.ProcessingTerminalFailure).ToArray(), 0, [], []);
}
Check(ProjectPlanningDocumentPreparation.Describe(Resolution(readySow), false, false).ReadyForAi, "prepared evidence remains ready without reprocessing");
Check(ProjectPlanningDocumentPreparation.Describe(Resolution(null), true, false).Status == "missing_sow", "a missing SOW never reports ready");
Check(ProjectPlanningDocumentPreparation.Describe(Resolution(readySow with { ProcessingStatus="queued" }), true, false).Status == "preparing", "queued work displays preparation");
Check(ProjectPlanningDocumentPreparation.Describe(Resolution(readySow with { ProcessingStatus="queued" }), false, false).Status == "paused", "disabled worker is visible");
Check(ProjectPlanningDocumentPreparation.Describe(Resolution(readySow with { ProcessingStatus="not_requested" }), true, false).Status == "waiting", "unqueued legacy documents do not pretend to be processing");
Check(ProjectPlanningDocumentPreparation.Describe(Resolution(readySow with { AuthorityStatus="draft" }), true, false).Status == "needs_attention", "processing alone does not grant SOW authority");
Check(ProjectPlanningDocumentPreparation.Describe(Resolution(readySow, readySow with { Category="gsd", WorkRegisterDocumentType="gsd", ProcessingStatus="failed" }), true, false).Status == "needs_attention", "failed supporting evidence blocks a ready claim");
var restricted = ProjectPlanningDocumentPreparation.Describe(Resolution(readySow with { EngineeringVisible=false }), true, false);
Check(!restricted.ReadyForAi && restricted.Documents.Count == 0, "private file details never appear in readiness");
Check(ProjectPlanningDocumentPreparation.Describe(Resolution(readySow), true, true).Status == "archived", "closed projects stop offering AI generation");
if (args.Contains("--database")) await Database();
Console.WriteLine($"FLOWHIVE_PREPARATION_ASSERTIONS_PASSED={count}");

async Task Database()
{
    var connectionString = Environment.GetEnvironmentVariable("FLOWHIVE_TEST_DB") ?? throw new Exception("FLOWHIVE_TEST_DB required");
    var config = new NpgsqlConnectionStringBuilder(connectionString);
    if (config.Host != "127.0.0.1" || config.Database != "flowhive_execution_test") throw new Exception("Only the isolated CI fixture is permitted");
    await using (var admin = new NpgsqlConnection(connectionString))
    {
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand("CREATE DATABASE flowhive_preparation_test;", admin);
        await create.ExecuteNonQueryAsync();
    }
    config.Database = "flowhive_preparation_test";
    await using var db = new NpgsqlConnection(config.ConnectionString); await db.OpenAsync();
    async Task Sql(string sql) { await using var c = new NpgsqlCommand(sql, db); await c.ExecuteNonQueryAsync(); }
    async Task<long> Number(string sql) { await using var c = new NpgsqlCommand(sql, db); return Convert.ToInt64(await c.ExecuteScalarAsync()); }
    await Sql("""
        CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY);
        INSERT INTO schema_migrations VALUES('097_project_planning_identity_safe_admission');
        CREATE TABLE app_users(user_id UUID PRIMARY KEY,is_active BOOLEAN DEFAULT TRUE);
        CREATE TABLE projects(project_id UUID PRIMARY KEY,status TEXT DEFAULT 'active');
        CREATE TABLE project_intake_documents(project_intake_document_id UUID PRIMARY KEY,project_id UUID,document_category TEXT,
          document_type TEXT,upload_source TEXT DEFAULT 'local_file',is_active BOOLEAN DEFAULT TRUE,engineering_visible BOOLEAN DEFAULT TRUE,
          ai_timesheet_context_enabled BOOLEAN DEFAULT TRUE,pulse_ai_processing_status TEXT DEFAULT 'not_requested',
          pulse_ai_processing_error_code TEXT DEFAULT '',pulse_ai_processing_updated_at TIMESTAMPTZ);
        """);
    var migration = File.ReadAllText("database/migrations/052_document_intelligence_runtime.sql");
    foreach (var table in new[] { "pulse_ai_document_processing_jobs", "pulse_ai_document_processing_events" })
    {
        var start = migration.IndexOf("CREATE TABLE IF NOT EXISTS " + table + " (", StringComparison.Ordinal);
        var end = migration.IndexOf("\n);", start, StringComparison.Ordinal) + 4;
        await Sql(migration[start..end]);
    }
    await Sql("""
        CREATE UNIQUE INDEX fixture_active_job ON pulse_ai_document_processing_jobs(project_intake_document_id)
        WHERE job_status IN ('queued','scanning','extracting','awaiting_ocr','embedding','indexing','retry_wait','cancel_requested');
        """);
    await Sql($"INSERT INTO app_users VALUES('{actor}',TRUE); INSERT INTO projects VALUES('{project}','active');");
    async Task<Guid> Document(string category = "sow", string state = "not_requested", bool visible = true, bool consent = true)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand("INSERT INTO project_intake_documents(project_intake_document_id,project_id,document_category,document_type,pulse_ai_processing_status,engineering_visible,ai_timesheet_context_enabled) VALUES(@id,@p,@category,@category,@state,@visible,@consent);", db);
        cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("p", project); cmd.Parameters.AddWithValue("category", category);
        cmd.Parameters.AddWithValue("state", state); cmd.Parameters.AddWithValue("visible", visible); cmd.Parameters.AddWithValue("consent", consent);
        await cmd.ExecuteNonQueryAsync(); return id;
    }
    async Task<int> Queue(Guid? document, bool commit = true)
    {
        await using var c = new NpgsqlConnection(config.ConnectionString); await c.OpenAsync(); await using var t = await c.BeginTransactionAsync();
        var result = await ProjectPlanningDocumentPreparation.QueueAsync(c, t, actor, project, document, default);
        if (commit) await t.CommitAsync(); else await t.RollbackAsync();
        return result;
    }
    var sow = await Document();
    Check(await Queue(sow, false) == 1 && await Number("SELECT count(*) FROM pulse_ai_document_processing_jobs") == 0, "association rollback rolls back preparation too");
    var attempts = await Task.WhenAll(Queue(sow), Queue(sow));
    Check(attempts.Sum() == 1 && await Number("SELECT count(*) FROM pulse_ai_document_processing_jobs") == 1, "concurrent association requests create one job");
    Check(await Queue(sow) == 0, "duplicate admission leaves the active job intact");
    Check(await Number($"SELECT count(*) FROM pulse_ai_document_processing_events WHERE actual_user_id='{actor}' AND effective_user_id='{actor}' AND event_code='document_association_queued'") == 1, "audit retains the real uploader identity");
    foreach (var state in new[] { "ready", "failed", "quarantined", "cancelled", "unsupported", "retry_wait" })
        Check(await Queue(await Document(state:state)) == 0, "does not automatically retry " + state);
    Check(await Queue(await Document(visible:false)) == 0 && await Queue(await Document(consent:false)) == 0, "visibility and AI consent are preserved");
    Check(await Queue(await Document("requirements_document")) == 1 && await Queue(await Document("supporting_document")) == 1, "supporting planning categories are prepared");
    Check(await Queue(await Document("other")) == 1, "Other project attachments are prepared without reclassification");
    Check(await Queue(await Document("unrelated_admin_record")) == 0, "unrelated categories are excluded");
    await Sql($"UPDATE projects SET status='closed' WHERE project_id='{project}';");
    Check(await Queue(await Document()) == 0, "closed projects do not enqueue new preparation");
    await using (var transaction = await db.BeginTransactionAsync())
    {
        Check(!await ProjectFlowHiveLifecycle.LockActiveAsync(db, transaction, project, default), "plan write fence rejects closure");
        var context = new DefaultHttpContext(); context.Items["ProjectPulseActualUserId"] = actor;
        context.Items["ProjectPulseEffectiveUserId"] = Guid.NewGuid();
        Check(await ProjectPlanningDocumentPreparation.QueueAssociatedAsync(db, transaction, context, projectId:project) == 0, "View-As cannot admit preparation");
        Check(await ProjectPlanningDocumentPreparation.QueueAssociatedAsync(db, transaction, new DefaultHttpContext(), projectId:project) == 0, "missing identity cannot admit preparation");
        await transaction.RollbackAsync();
    }
    var retainedJobs = await Number("SELECT count(*) FROM pulse_ai_document_processing_jobs");
    await Sql($"UPDATE projects SET status='active' WHERE project_id='{project}';");
    await using (var transaction = await db.BeginTransactionAsync())
    {
        Check(await ProjectFlowHiveLifecycle.LockActiveAsync(db, transaction, project, default), "reopened projects accept a plan write");
        await using var competing = new NpgsqlConnection(config.ConnectionString); await competing.OpenAsync();
        await using var close = new NpgsqlCommand($"SET lock_timeout='200ms'; UPDATE projects SET status='closed' WHERE project_id='{project}';", competing);
        var blocked = false;
        try { await close.ExecuteNonQueryAsync(); } catch (PostgresException e) when (e.SqlState == "55P03") { blocked = true; }
        Check(blocked, "closure waits for an in-flight plan transaction");
        await transaction.CommitAsync();
    }
    Check(await Number("SELECT count(*) FROM pulse_ai_document_processing_jobs") == retainedJobs, "close and reopen retain preparation history");
}
