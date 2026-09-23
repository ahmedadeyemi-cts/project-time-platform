using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Npgsql;

namespace ProjectTime.Api.Ai;

/// <summary>Reads the current worker-produced version. It neither parses uploads
/// nor treats a global configuration flag as a document scan receipt.</summary>
public sealed class LayaProcessedSourceReader(PulseAiPrivateRuntimeSourceResolver resolver)
{
    public const string ContractVersion = "laya-processed-source-v1";
    private const int MaximumSectionCharacters = 2_000_000;
    private const long MaximumSourceBytes = 100L * 1024 * 1024;

    // Run only after the source resolver has established the effective user's
    // existing document scope. No query in this reader grants that scope.
    public const string EvidenceSql = """
        SELECT COALESCE(d.pulse_ai_processing_status,'not_requested'),
               COALESCE(d.pulse_ai_processing_error_code,''),
               v.pulse_ai_document_version_id, COALESCE(v.source_sha256,''),
               COALESCE(j.source_sha256,''), COALESCE(j.job_status,''),
               COALESCE(v.authority_status,''), COALESCE(v.extraction_method,''),
               COALESCE(v.malware_scanner,''), COALESCE(j.malware_scanner,''),
               s.section_index, s.section_text, COALESCE(s.text_sha256,''),
               EXISTS (
                 SELECT 1 FROM pulse_ai_document_processing_events e
                 WHERE e.pulse_ai_document_processing_job_id=j.pulse_ai_document_processing_job_id
                   AND e.project_intake_document_id=d.project_intake_document_id
                   AND e.event_code='malware_scan_completed' AND e.event_status='succeeded'
                   AND e.evidence_json->'clean'='true'::jsonb
                   AND e.evidence_json->'infected'='false'::jsonb
                   AND e.evidence_json->>'scanner'=v.malware_scanner
               ),
               EXISTS (
                 SELECT 1 FROM pulse_ai_document_processing_events e
                 WHERE e.pulse_ai_document_processing_job_id=j.pulse_ai_document_processing_job_id
                   AND e.project_intake_document_id=d.project_intake_document_id
                   AND e.event_code='private_extraction_completed' AND e.event_status='succeeded'
                   AND COALESCE(e.evidence_json->>'SourceSha256',e.evidence_json->>'sourceSha256')=v.source_sha256
               )
        FROM project_intake_documents d
        LEFT JOIN pulse_ai_document_versions v
          ON v.pulse_ai_document_version_id=d.pulse_ai_active_version_id
         AND v.project_intake_document_id=d.project_intake_document_id
        LEFT JOIN pulse_ai_document_processing_jobs j
          ON j.pulse_ai_document_processing_job_id=v.processed_by_job_id
         AND j.project_intake_document_id=d.project_intake_document_id
        LEFT JOIN LATERAL (
          SELECT section_index, section_text, text_sha256
          FROM pulse_ai_document_sections
          WHERE pulse_ai_document_version_id=v.pulse_ai_document_version_id
            AND project_intake_document_id=d.project_intake_document_id
            AND NULLIF(btrim(section_text),'') IS NOT NULL
          ORDER BY section_index LIMIT 1
        ) s ON TRUE
        WHERE d.project_intake_document_id=@document AND d.is_active=TRUE
        """;

    public async Task<LayaProcessedSource?> ReadAsync(Guid effectiveUserId, Guid documentId,
        CancellationToken cancellationToken = default,
        bool processingAdmission = false,
        bool classificationAdmission = false)
    {
        var source = await resolver.ResolveAsync(
            effectiveUserId, documentId, cancellationToken, processingAdmission, classificationAdmission);
        if (source is null) return null;
        await using var db = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await db.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(EvidenceSql, db) { CommandTimeout = 15 };
        command.Parameters.AddWithValue("document", documentId);
        LayaProcessedSource result;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            var stage = Code(reader.GetString(0), "unknown");
            var diagnostic = Code(reader.GetString(1), "");
            Guid? version = reader.IsDBNull(2) ? null : reader.GetGuid(2);
            var hash = reader.GetString(3);
            if (stage != "ready")
                return new(documentId, version, hash, stage,
                    diagnostic.Length > 0 ? diagnostic : "document_processing_" + stage);
            if (version is null || !IsHash(hash) || hash != reader.GetString(4)
                || reader.GetString(5) != "succeeded"
                || reader.GetString(6) is not ("candidate" or "approved")
                || string.IsNullOrWhiteSpace(reader.GetString(7))
                || string.IsNullOrWhiteSpace(reader.GetString(8))
                || reader.GetString(8) != reader.GetString(9)
                || !reader.GetBoolean(13) || !reader.GetBoolean(14))
                return new(documentId, version, hash, "needs_attention", "document_processing_evidence_incomplete");
            if (reader.IsDBNull(10) || reader.IsDBNull(11))
                return new(documentId, version, hash, "needs_attention", "document_extracted_text_missing");
            var text = reader.GetString(11);
            if (text.Length > MaximumSectionCharacters || string.IsNullOrWhiteSpace(text)
                || LayaDecisionContract.Sha256(text) != reader.GetString(12))
                return new(documentId, version, hash, "needs_attention", "document_extracted_text_integrity_failed");
            result = new(documentId, version, hash, "ready", "")
            {
                Excerpt = LayaDecisionContract.Excerpt(text.Trim()),
                SectionIndex = reader.GetInt32(10),
                SectionSha256 = reader.GetString(12)
            };
        }
        // A current database version cannot authorize different replacement bytes.
        if (!await CurrentBytesMatchAsync(source.StoragePath, result.SourceSha256, cancellationToken))
            return new(documentId, result.VersionId, result.SourceSha256, "needs_attention", "document_source_integrity_failed");
        var current = await resolver.ResolveAsync(
            effectiveUserId, documentId, cancellationToken, processingAdmission, classificationAdmission);
        if (current is null) return null;
        if (current.StoragePath != source.StoragePath || current.UploadedAt != source.UploadedAt)
            return new(documentId, result.VersionId, result.SourceSha256, "needs_attention", "document_source_changed");
        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> StagesAsync(IReadOnlyList<Guid> authorizedIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, string>();
        if (authorizedIds.Count == 0) return result;
        await using var db = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await db.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT project_intake_document_id, COALESCE(pulse_ai_processing_status,'not_requested')
            FROM project_intake_documents WHERE is_active=TRUE AND project_intake_document_id=ANY(@ids)
            """, db) { CommandTimeout = 15 };
        command.Parameters.AddWithValue("ids", authorizedIds.Take(500).Distinct().ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result[reader.GetGuid(0)] = Code(reader.GetString(1), "unknown");
        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, LayaClassificationStatus>> ClassificationStatesAsync(
        IReadOnlyList<Guid> authorizedIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, LayaClassificationStatus>();
        if (authorizedIds.Count == 0) return result;
        await using var db = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await db.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT project_intake_document_id,
                   COALESCE(pulse_ai_laya_classification_status,'not_requested'),
                   COALESCE(pulse_ai_laya_policy_version,0),
                   COALESCE(pulse_ai_laya_model_revision,''),
                   COALESCE(pulse_ai_laya_error_code,'')
            FROM project_intake_documents
            WHERE is_active=TRUE AND project_intake_document_id=ANY(@ids)
            """, db) { CommandTimeout = 15 };
        command.Parameters.AddWithValue("ids", authorizedIds.Take(500).Distinct().ToArray());
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result[reader.GetGuid(0)] = new(
                    Code(reader.GetString(1), "unknown"), reader.GetInt64(2),
                    reader.GetString(3), Code(reader.GetString(4), ""));
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            // The additive status projection is unavailable on an older database;
            // callers retain the existing processing state instead of guessing.
        }
        return result;
    }

    // Lock the current document/version while recording a recommendation. A replacement
    // cannot commit between this check and the insert in the same transaction.
    public static async Task<bool> LockCurrentVersionAsync(NpgsqlConnection db, NpgsqlTransaction tx,
        LayaProcessedSource expected, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT v.source_sha256
            FROM project_intake_documents d
            JOIN pulse_ai_document_versions v ON v.pulse_ai_document_version_id=d.pulse_ai_active_version_id
              AND v.project_intake_document_id=d.project_intake_document_id
            WHERE d.project_intake_document_id=@document AND d.is_active=TRUE
              AND d.pulse_ai_processing_status='ready' AND v.pulse_ai_document_version_id=@version
              AND v.authority_status IN ('candidate','approved')
            FOR SHARE OF d,v
            """, db, tx) { CommandTimeout = 15 };
        command.Parameters.AddWithValue("document", expected.DocumentId);
        command.Parameters.AddWithValue("version", expected.VersionId!.Value);
        return await command.ExecuteScalarAsync(ct) is string hash && hash == expected.SourceSha256;
    }

    public static bool SameEvidence(LayaProcessedSource before, LayaProcessedSource after) =>
        before.Ready && after.Ready && before.DocumentId == after.DocumentId
        && before.VersionId == after.VersionId && before.SourceSha256 == after.SourceSha256
        && before.SectionIndex == after.SectionIndex && before.SectionSha256 == after.SectionSha256
        && before.Excerpt == after.Excerpt;

    private static async Task<bool> CurrentBytesMatchAsync(string path, string expectedHash, CancellationToken ct)
    {
        if (!IsHash(expectedHash)) return false;
        try
        {
            var root = Path.GetFullPath(ProjectPulseUploadStorage.ResolveRoot())
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(root, comparison)) return false;
            var info = new FileInfo(full);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumSourceBytes
                || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return false;
            // Reject links in every directory below the configured trusted root.
            for (var parent = info.Directory; parent is not null
                && (parent.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).Length > root.Length;
                parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long count = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                count += read;
                if (count > MaximumSourceBytes) return false;
                hash.AppendData(buffer, 0, read);
            }
            return count > 0 && Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() == expectedHash;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
    }

    public static bool IsHash(string value) => value.Length == 64
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Code(string value, string fallback) => value.Length is > 0 and <= 100
        && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_') ? value : fallback;
}

public sealed record LayaProcessedSource(Guid DocumentId, Guid? VersionId, string SourceSha256,
    string Stage, string DiagnosticCode)
{
    public bool Ready => Stage == "ready" && VersionId.HasValue && Excerpt.Length > 0;
    [JsonIgnore] public string Excerpt { get; init; } = "";
    [JsonIgnore] public int SectionIndex { get; init; }
    [JsonIgnore] public string SectionSha256 { get; init; } = "";
    public object ToPublicEvidence() => new
    {
        documentId = DocumentId, versionId = VersionId, sourceSha256 = SourceSha256,
        stage = Stage, diagnosticCode = DiagnosticCode, readyForClassification = Ready,
        evidenceSource = LayaProcessedSourceReader.ContractVersion, rawDocumentTextReturned = false
    };
}

public sealed record LayaClassificationStatus(
    string Status,
    long PolicyVersion,
    string ModelRevision,
    string ErrorCode);
