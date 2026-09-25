using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

var root = Path.Combine(Path.GetTempPath(), "laya-receipts-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
ProjectPulseUploadStorage.Root = root;
var file = Path.Combine(root,"document.txt");
var text = "SOW. SYNTHETIC-PRIVATE-DOCUMENT-NOT-FOR-HTTP. " + new string('x',400);
await File.WriteAllTextAsync(file,text);
var hash = LayaDecisionContract.Sha256(text);
var user = Guid.NewGuid(); var document = Guid.NewGuid(); var version = Guid.NewGuid(); var job = Guid.NewGuid();
var resolver = new PulseAiPrivateRuntimeSourceResolver {
    AllowedUser=user, AllowedDocument=document, Current=new Source(file,DateTimeOffset.UtcNow)
};
var service = new LayaProcessedSourceReader(resolver);
var checks=0;
void Check(bool condition,string label) { checks++; if(!condition) throw new Exception(label); }
async Task Sql(string sql, params (string Name,object Value)[] parameters) {
    await using var db=new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve()); await db.OpenAsync();
    await using var command=new NpgsqlCommand(sql,db);
    foreach(var pair in parameters) command.Parameters.AddWithValue(pair.Name,pair.Value);
    await command.ExecuteNonQueryAsync();
}
async Task Seed() {
    await Sql("""
      TRUNCATE project_intake_documents,pulse_ai_document_versions,pulse_ai_document_processing_jobs,
               pulse_ai_document_sections,pulse_ai_document_processing_events;
      INSERT INTO project_intake_documents VALUES(@d,true,'ready','',@v);
      INSERT INTO pulse_ai_document_versions VALUES(@v,@d,@hash,'candidate','native_text','fixture_scanner',@j);
      INSERT INTO pulse_ai_document_processing_jobs VALUES(@j,@d,@hash,'succeeded','fixture_scanner');
      INSERT INTO pulse_ai_document_sections VALUES(@v,@d,0,@text,@hash);
      INSERT INTO pulse_ai_document_processing_events VALUES(@j,@d,'malware_scan_completed','succeeded',
         '{"clean":true,"infected":false,"scanner":"fixture_scanner"}');
      INSERT INTO pulse_ai_document_processing_events VALUES(@j,@d,'private_extraction_completed','succeeded',
         jsonb_build_object('SourceSha256',@hash::text));
      """,("d",document),("v",version),("j",job),("hash",hash),("text",text));
    await File.WriteAllTextAsync(file,text);
}
async Task ExpectBlocked(string sql,string diagnostic) {
    await Seed(); await Sql(sql);
    var result=await service.ReadAsync(user,document);
    Check(result is not null && !result.Ready && result.DiagnosticCode==diagnostic,diagnostic);
}
try {
    await Sql(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"fixture.sql")));
    await Seed();
    Environment.SetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED","false");
    Environment.SetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED","false");
    var ready=await service.ReadAsync(user,document);
    Check(ready?.Ready==true,"durable clean receipts do not need preview flags");
    Check(ready!.Excerpt.Length==300,"excerpt remains bounded");
    Check(!JsonSerializer.Serialize(ready.ToPublicEvidence()).Contains("SYNTHETIC-PRIVATE"),"public evidence excludes text");
    Check(!JsonSerializer.Serialize(ready).Contains("SYNTHETIC-PRIVATE"),"record serialization excludes excerpt");
    Check(await service.ReadAsync(Guid.NewGuid(),document) is null,"unauthorized user has no evidence");
    Check(await service.ReadAsync(user,Guid.NewGuid()) is null,"unauthorized document has no evidence");
    await ExpectBlocked("UPDATE project_intake_documents SET pulse_ai_processing_status='queued'","document_processing_queued");
    await ExpectBlocked("DELETE FROM pulse_ai_document_processing_events WHERE event_code='malware_scan_completed'","document_processing_evidence_incomplete");
    await ExpectBlocked("UPDATE pulse_ai_document_processing_events SET evidence_json='{" + "\"clean\":true,\"infected\":true,\"scanner\":\"fixture_scanner\"}' WHERE event_code='malware_scan_completed'","document_processing_evidence_incomplete");
    await ExpectBlocked("UPDATE pulse_ai_document_processing_events SET evidence_json='{" + "\"clean\":\"true\",\"infected\":false,\"scanner\":\"fixture_scanner\"}' WHERE event_code='malware_scan_completed'","document_processing_evidence_incomplete");
    await ExpectBlocked("DELETE FROM pulse_ai_document_processing_events WHERE event_code='private_extraction_completed'","document_processing_evidence_incomplete");
    await ExpectBlocked("UPDATE pulse_ai_document_processing_jobs SET source_sha256=repeat('0',64)","document_processing_evidence_incomplete");
    await ExpectBlocked("UPDATE pulse_ai_document_versions SET authority_status='superseded'","document_processing_evidence_incomplete");
    await ExpectBlocked("UPDATE pulse_ai_document_sections SET section_text='changed text'","document_extracted_text_integrity_failed");
    await ExpectBlocked("DELETE FROM pulse_ai_document_sections","document_extracted_text_missing");
    await Seed(); await File.WriteAllTextAsync(file,"replacement bytes");
    Check((await service.ReadAsync(user,document))?.DiagnosticCode=="document_source_integrity_failed","old receipt cannot authorize replacement bytes");
    await Seed();
    await using(var db=new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve())) {
        await db.OpenAsync(); await using var tx=await db.BeginTransactionAsync();
        Check(await LayaProcessedSourceReader.LockCurrentVersionAsync(db,tx,ready,CancellationToken.None),"exact version locks");
        try {
            await Sql("SET lock_timeout='150ms'; UPDATE project_intake_documents SET pulse_ai_active_version_id=NULL");
            Check(false,"replacement must wait for publication lock");
        } catch(PostgresException e) when(e.SqlState=="55P03") { Check(true,"replacement is fenced during publication"); }
        await tx.RollbackAsync();
    }
    var stages=await service.StagesAsync([document]); Check(stages[document]=="ready","stage projection reads durable state");
    Check((await service.StagesAsync([])).Count==0,"empty authorized list cannot enumerate documents");
    Check(LayaProcessedSourceReader.SameEvidence(ready,ready),"same evidence matches");
    Check(!LayaProcessedSourceReader.SameEvidence(ready,ready with {VersionId=Guid.NewGuid()}),"different version does not match");
    resolver.Current=null; Check(await service.ReadAsync(user,document) is null,"revocation removes access");
    Console.WriteLine($"LAYA_PROCESSED_SOURCE_CHECKS=PASS ({checks} checks; actual SQL/checksum/locking, synthetic scope boundary)");
} finally { Directory.Delete(root,true); }
