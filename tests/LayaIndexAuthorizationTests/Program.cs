using System.Security.Cryptography;
using System.Text;
using Npgsql;
using ProjectTime.Api.Ai;

// This executable uses real publication SQL and the actual migration-052 schema.
// Scanner receipts, vectors and text are synthetic; no model/network inference occurs.
var input = Environment.GetEnvironmentVariable("LAYA_PROCESSED_TEST_DB")
    ?? throw new InvalidOperationException("The isolated CI database is required; tests cannot silently skip.");
var config = new NpgsqlConnectionStringBuilder(input);
if (Environment.GetEnvironmentVariable("LAYA_INDEX_TEST_DISPOSABLE") != "1"
    || config.Host != "127.0.0.1" || config.Database != "flowhive_execution_test")
    throw new InvalidOperationException("Refusing to create a test database outside the disposable Laya CI service.");
var database = "laya_index_" + Guid.NewGuid().ToString("N");
await using var admin = new NpgsqlConnection(input);
await admin.OpenAsync();
await new NpgsqlCommand($"CREATE DATABASE {database}", admin).ExecuteNonQueryAsync();
config.Database = database;
foreach (var alias in ProjectPulseAiDatabaseConnection.DirectAliases)
    Environment.SetEnvironmentVariable(alias, null);
Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", config.ConnectionString);
var assertions = 0;
void Check(bool pass, string name)
{
    if (!pass) throw new InvalidOperationException("FAIL " + name);
    assertions++; Console.WriteLine("PASS " + name);
}
async Task Sql(string sql)
{
    await using var db = new NpgsqlConnection(config.ConnectionString); await db.OpenAsync();
    await new NpgsqlCommand(sql, db).ExecuteNonQueryAsync();
}
async Task<long> Count(string sql)
{
    await using var db = new NpgsqlConnection(config.ConnectionString); await db.OpenAsync();
    return Convert.ToInt64(await new NpgsqlCommand(sql, db).ExecuteScalarAsync());
}
var actor = Guid.NewGuid(); var other = Guid.NewGuid(); var role = Guid.NewGuid();
var repository = new PulseAiPrivateDocumentRuntimeRepository(
    Microsoft.Extensions.Logging.Abstractions.NullLogger<PulseAiPrivateDocumentRuntimeRepository>.Instance);
const string text = "Non-sensitive document indexing authorization fixture.";
var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
PulseAiPrivateMalwareScanResult Scan() => new("clean", true, false, "synthetic_private_scanner", "fixture", "", hash, hash, DateTimeOffset.UtcNow);
PulseAiDocumentExtractionResult Extraction(PulseAiAuthorizedDocumentSource source) => new(
    "extraction_preview_ready", source.DocumentId, source.OriginalFileName, "text", "synthetic_fixture", 1, 1,
    text.Length, 12, false, hash,
    new("allowed", ".txt", "text", true, true, true, true, true, false, false, false, true,
        "synthetic_private_scanner", text.Length, hash, [], []),
    [new(0, "section-1", "Fixture", text, 1, null, text.Length, hash)], [], [], DateTimeOffset.UtcNow);
IReadOnlyList<PulseAiDocumentChunk> Chunks(PulseAiAuthorizedDocumentSource source) =>
    [new(source.DocumentId.ToString("N"), source.DocumentId, 0, "section-1", "Fixture", 1, null, text, text.Length, 12, hash, hash)];
PulseAiPrivateEmbeddingResult Vectors() => new("private_embeddings_completed", "synthetic_private", "fixture", 3, [new double[] { .1, .2, .3 }], "", DateTimeOffset.UtcNow);
async Task<(PulseAiAuthorizedDocumentSource Source, PulseAiPrivateProcessingJob Job)> New(
    bool consent = true, bool visible = true, bool projectless = false, bool chat = false)
{
    var id = Guid.NewGuid(); Guid? project = projectless || chat ? null : Guid.NewGuid();
    var job = Guid.NewGuid(); var token = Guid.NewGuid();
    if (project.HasValue) await Sql($"INSERT INTO projects(project_id,status) VALUES('{project}','active');");
    var uploaded = new DateTimeOffset(2026, 9, 24, 1, 0, 0, TimeSpan.Zero);
    var uploadSource = chat ? CelarAiConversationAttachmentPolicy.UploadSource : "manual";
    await using var db = new NpgsqlConnection(config.ConnectionString); await db.OpenAsync();
    await using var command = new NpgsqlCommand("""
        INSERT INTO project_intake_documents(project_intake_document_id,project_id,document_type,document_category,
          original_file_name,stored_file_name,storage_path,size_bytes,uploaded_at,uploaded_by_user_id,upload_source,
          engineering_visible,ai_timesheet_context_enabled,pulse_ai_processing_status,pulse_ai_classification)
        VALUES(@id,@project,'other','other','fixture.txt',@stored,@path,@size,@uploaded,@actor,@upload,@visible,@consent,'extracting','internal_project_document');
        INSERT INTO pulse_ai_document_processing_jobs(pulse_ai_document_processing_job_id,project_intake_document_id,
          project_id,actual_user_id,effective_user_id,requested_by_user_id,job_status,lease_owner,lease_token,lease_generation,lease_expires_at)
        VALUES(@job,@id,@project,@actor,@actor,@actor,'extracting','fixture',@token,1,clock_timestamp()+INTERVAL '2 minutes');
        """, db);
    command.Parameters.AddWithValue("id", id);
    command.Parameters.Add("project", NpgsqlTypes.NpgsqlDbType.Uuid).Value = (object?)project ?? DBNull.Value;
    command.Parameters.AddWithValue("stored", id + ".txt"); command.Parameters.AddWithValue("path", "/fixture/" + id + ".txt");
    command.Parameters.AddWithValue("size", (long)text.Length); command.Parameters.AddWithValue("uploaded", uploaded);
    command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("upload", uploadSource);
    command.Parameters.AddWithValue("visible", chat ? false : visible); command.Parameters.AddWithValue("consent", chat ? false : consent);
    command.Parameters.AddWithValue("job", job); command.Parameters.AddWithValue("token", token);
    await command.ExecuteNonQueryAsync();
    if (chat)
    {
        var conversation = Guid.NewGuid();
        await Sql($"INSERT INTO pulse_ai_conversations VALUES('{conversation}','{actor}','{actor}','active',clock_timestamp()+INTERVAL '1 day'); INSERT INTO pulse_ai_conversation_attachments VALUES(gen_random_uuid(),'{conversation}','{id}','{actor}',NULL,NULL,clock_timestamp()+INTERVAL '1 day');");
    }
    var source = new PulseAiAuthorizedDocumentSource(id, project, "", "", "", "other", "other", "fixture.txt",
        id + ".txt", "/fixture/" + id + ".txt", "text/plain", text.Length, chat ? false : visible, chat ? false : consent,
        "not_started", false, null, uploaded, uploadSource, chat ? "conversation_owner_only" : "organization_document_scope",
        "internal_project_document", []);
    return (source, (await repository.GetJobAsync(job))!);
}
async Task<PulseAiPreparedDocumentResult> Publish((PulseAiAuthorizedDocumentSource Source, PulseAiPrivateProcessingJob Job) item) =>
    await repository.PersistProcessedDocumentAsync(item.Job, item.Source, Scan(), Extraction(item.Source), Chunks(item.Source), Vectors(), false, true);
async Task Rejected((PulseAiAuthorizedDocumentSource Source, PulseAiPrivateProcessingJob Job) item, string name)
{
    bool rejected = false;
    try { await Publish(item); } catch (PulseAiDocumentPublicationRejectedException) { rejected = true; }
    Check(rejected, name);
    Check(await Count($"SELECT count(*) FROM pulse_ai_document_versions WHERE project_intake_document_id='{item.Source.DocumentId}'") == 0,
        name + " publishes no extracted version");
}
try
{
    await Sql("""
        CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT,applied_at TIMESTAMPTZ);
        CREATE TABLE app_users(user_id UUID PRIMARY KEY,is_active BOOLEAN NOT NULL DEFAULT TRUE);
        CREATE TABLE projects(project_id UUID PRIMARY KEY,status TEXT);
        CREATE TABLE app_roles(app_role_id UUID PRIMARY KEY,role_code TEXT,is_active BOOLEAN DEFAULT TRUE);
        CREATE TABLE app_permissions(app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),permission_code TEXT UNIQUE,
          permission_name TEXT,module_code TEXT,permission_description TEXT);
        CREATE TABLE app_role_permissions(app_role_id UUID,app_permission_id UUID,PRIMARY KEY(app_role_id,app_permission_id));
        CREATE TABLE app_user_role_assignments(user_id UUID,app_role_id UUID,is_active BOOLEAN DEFAULT TRUE);
        CREATE TABLE app_feature_catalog(feature_code TEXT PRIMARY KEY,feature_name TEXT,module_code TEXT,route_anchor TEXT,
          required_permission_code TEXT,feature_description TEXT,display_order INT,is_active BOOLEAN,updated_at TIMESTAMPTZ);
        CREATE TABLE project_intake_documents(project_intake_document_id UUID PRIMARY KEY,project_id UUID REFERENCES projects,
          document_type TEXT,document_category TEXT,original_file_name TEXT,stored_file_name TEXT,storage_path TEXT,
          content_type TEXT,size_bytes BIGINT,uploaded_at TIMESTAMPTZ,uploaded_by_user_id UUID,upload_source TEXT,
          is_active BOOLEAN DEFAULT TRUE,engineering_visible BOOLEAN DEFAULT TRUE,ai_timesheet_context_enabled BOOLEAN DEFAULT TRUE,
          extraction_status TEXT DEFAULT 'not_started',ai_context_last_processed_at TIMESTAMPTZ,work_register_document_id UUID);
        CREATE TABLE work_register_documents(work_register_document_id UUID PRIMARY KEY,project_id UUID,status TEXT,archived_at TIMESTAMPTZ);
        CREATE TABLE pulse_ai_conversations(pulse_ai_conversation_id UUID PRIMARY KEY,actual_user_id UUID,effective_user_id UUID,status TEXT,retention_until TIMESTAMPTZ);
        CREATE TABLE pulse_ai_conversation_attachments(pulse_ai_conversation_attachment_id UUID PRIMARY KEY,pulse_ai_conversation_id UUID,
          project_intake_document_id UUID,uploaded_by_user_id UUID,revoked_at TIMESTAMPTZ,storage_purged_at TIMESTAMPTZ,retention_until TIMESTAMPTZ);
        """);
    await Sql(await File.ReadAllTextAsync("database/migrations/052_document_intelligence_runtime.sql"));
    await Sql("ALTER TABLE pulse_ai_document_processing_jobs ADD COLUMN lease_token UUID, ADD COLUMN lease_generation BIGINT DEFAULT 0, ADD COLUMN lease_heartbeat_at TIMESTAMPTZ;");
    // GetJobAsync's standard projection includes these project labels.
    await Sql("ALTER TABLE projects ADD COLUMN project_code TEXT DEFAULT '', ADD COLUMN project_name TEXT DEFAULT '';");
    await Sql($"INSERT INTO app_users VALUES('{actor}',TRUE),('{other}',TRUE); INSERT INTO app_roles VALUES('{role}','FIXTURE_USER',TRUE); INSERT INTO app_user_role_assignments VALUES('{actor}','{role}',TRUE); INSERT INTO app_permissions(permission_code) VALUES('{CelarAiConversationAttachmentPolicy.Permission}'); INSERT INTO app_role_permissions SELECT '{role}',app_permission_id FROM app_permissions WHERE permission_code='{CelarAiConversationAttachmentPolicy.Permission}';");

    foreach (var mode in new[] { "no-consent", "hidden", "unassociated" })
    {
        var item = await New(consent: mode != "no-consent", visible: mode != "hidden", projectless: mode == "unassociated");
        var decision = await PulseAiDocumentIndexAuthorization.InspectAsync(item.Job, item.Source, hash);
        Check(decision.SourceCurrent && !decision.IndexAllowed, mode + " is denied before embeddings");
        var saved = await Publish(item);
        Check(!saved.Indexed && saved.ChunkCount == 0 && saved.EmbeddedChunkCount == 0, mode + " cannot force index publication");
        Check(await Count($"SELECT count(*) FROM pulse_ai_document_sections WHERE project_intake_document_id='{item.Source.DocumentId}'") == 1, mode + " retains extracted security evidence");
        Check(await Count($"SELECT count(*) FROM pulse_ai_document_chunks WHERE project_intake_document_id='{item.Source.DocumentId}'") == 0, mode + " stores neither lexical entries nor vectors");
        Check(await Count($"SELECT count(*) FROM pulse_ai_document_versions WHERE pulse_ai_document_version_id='{saved.VersionId}' AND index_status='inactive' AND authority_status='candidate'") == 1, mode + " neither claims AI readiness nor approves a business version");
    }
    var allowed = await New(); var allowedSaved = await Publish(allowed);
    Check(allowedSaved.Indexed && allowedSaved.ChunkCount == 1 && allowedSaved.EmbeddedChunkCount == 1, "consented active project publishes its real fixture index");
    var revokedConsent = await New();
    Check((await PulseAiDocumentIndexAuthorization.InspectAsync(revokedConsent.Job, revokedConsent.Source, hash)).IndexAllowed, "initial consent allows embedding admission");
    await Sql($"UPDATE project_intake_documents SET ai_timesheet_context_enabled=FALSE WHERE project_intake_document_id='{revokedConsent.Source.DocumentId}';");
    Check(!(await Publish(revokedConsent)).Indexed, "consent revoked during inference discards vectors at transactional publication");
    var chat = await New(chat: true); Check((await Publish(chat)).Indexed, "chat indexing is owner-scoped without project-consent flags");
    var expired = await New(chat: true);
    await Sql($"UPDATE pulse_ai_conversation_attachments SET retention_until=clock_timestamp()-INTERVAL '1 second' WHERE project_intake_document_id='{expired.Source.DocumentId}';");
    await Rejected(expired, "expired private attachment");
    var wrongOwner = await New(chat: true);
    await Sql($"UPDATE pulse_ai_conversation_attachments SET uploaded_by_user_id='{other}' WHERE project_intake_document_id='{wrongOwner.Source.DocumentId}';");
    await Rejected(wrongOwner, "another conversation owner");
    var revokedPermission = await New(chat: true);
    await Sql($"UPDATE app_user_role_assignments SET is_active=FALSE WHERE user_id='{actor}';");
    await Rejected(revokedPermission, "revoked chat permission");
    await Sql($"UPDATE app_user_role_assignments SET is_active=TRUE WHERE user_id='{actor}';");
    var cancelled = await New(); await Sql($"UPDATE pulse_ai_document_processing_jobs SET cancellation_requested=TRUE WHERE pulse_ai_document_processing_job_id='{cancelled.Job.JobId}';");
    await Rejected(cancelled, "cancelled worker");
    var lost = await New(); await Sql($"UPDATE pulse_ai_document_processing_jobs SET lease_generation=2 WHERE pulse_ai_document_processing_job_id='{lost.Job.JobId}';");
    await Rejected(lost, "superseded worker lease");
    var closed = await New(); await Sql($"UPDATE projects SET status='archived' WHERE project_id='{closed.Source.ProjectId}';");
    await Rejected(closed, "archived project");
    var replaced = await New(); await Sql($"UPDATE project_intake_documents SET stored_file_name='replacement.txt',pulse_ai_processing_status='queued' WHERE project_intake_document_id='{replaced.Source.DocumentId}';");
    await Rejected(replaced, "replacement source");
    await repository.CancelStalePublicationAsync(replaced.Job, "index_source_changed_or_revoked", default);
    Check(await Count($"SELECT count(*) FROM project_intake_documents WHERE project_intake_document_id='{replaced.Source.DocumentId}' AND pulse_ai_processing_status='queued'") == 1, "stale completion does not overwrite replacement status");
    var inactive = await New(); await Sql($"UPDATE app_users SET is_active=FALSE WHERE user_id='{actor}';");
    await Rejected(inactive, "deactivated job actor");
    await Sql($"UPDATE app_users SET is_active=TRUE WHERE user_id='{actor}';");

    var locked = await New();
    await using (var db = new NpgsqlConnection(config.ConnectionString))
    {
        await db.OpenAsync(); await using var tx = await db.BeginTransactionAsync();
        Check((await PulseAiDocumentIndexAuthorization.LockAsync(db, tx, locked.Job, locked.Source, hash, default)).IndexAllowed, "publication locks the current consent");
        await using var otherDb = new NpgsqlConnection(config.ConnectionString); await otherDb.OpenAsync();
        bool blocked = false;
        try
        {
            await new NpgsqlCommand("SET lock_timeout='250ms'", otherDb).ExecuteNonQueryAsync();
            await new NpgsqlCommand($"UPDATE project_intake_documents SET ai_timesheet_context_enabled=FALSE WHERE project_intake_document_id='{locked.Source.DocumentId}'", otherDb).ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "55P03") { blocked = true; }
        Check(blocked, "concurrent consent change cannot race an active publication transaction");
        await tx.RollbackAsync();
    }
    Console.WriteLine($"LAYA_INDEX_AUTHORIZATION_ASSERTIONS_PASSED={assertions}; real_postgresql=true; external_inference=false");
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin).ExecuteNonQueryAsync();
}
