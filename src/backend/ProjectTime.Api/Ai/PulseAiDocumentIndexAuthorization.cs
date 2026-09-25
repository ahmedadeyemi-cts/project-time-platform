using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Security admission is not permission to create AI embeddings or search entries.
/// Evaluate the current owner policy before inference and again under row locks in
/// the publication transaction. These decisions never grant retrieval access.
/// </summary>
public static class PulseAiDocumentIndexAuthorization
{
    public static async Task<PulseAiDocumentIndexDecision> InspectAsync(
        PulseAiPrivateProcessingJob job, PulseAiAuthorizedDocumentSource source, string sourceSha256,
        CancellationToken cancellationToken = default)
    {
        await using var db = new NpgsqlConnection(ProjectPulseAiDatabaseConnection.Resolve());
        await db.OpenAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);
        var decision = await LockAsync(db, tx, job, source, sourceSha256, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return decision;
    }

    // Hold these locks until the caller commits its extracted version and index.
    // Cancellation uses job -> document ordering too. No network call runs here.
    public static async Task<PulseAiDocumentIndexDecision> LockAsync(
        NpgsqlConnection db, NpgsqlTransaction tx, PulseAiPrivateProcessingJob job,
        PulseAiAuthorizedDocumentSource source, string sourceSha256, CancellationToken ct)
    {
        if (job.DocumentId != source.DocumentId || job.ProjectId != source.ProjectId
            || job.LeaseToken is null || job.EffectiveUserId is null && job.ActualUserId is null)
            return PulseAiDocumentIndexDecision.Reject("index_job_identity_invalid");

        await using (var fence = new NpgsqlCommand("""
            SELECT j.pulse_ai_document_processing_job_id
            FROM pulse_ai_document_processing_jobs j
            JOIN app_users actor ON actor.user_id=COALESCE(j.effective_user_id,j.actual_user_id)
            WHERE j.pulse_ai_document_processing_job_id=@job
              AND j.project_intake_document_id=@document
              AND j.project_id IS NOT DISTINCT FROM @project
              AND j.actual_user_id IS NOT DISTINCT FROM @actual
              AND j.effective_user_id IS NOT DISTINCT FROM @effective
              AND actor.is_active=TRUE AND j.cancellation_requested=FALSE
              AND j.job_status IN ('extracting','embedding','indexing')
              AND j.lease_owner=@owner AND j.lease_token=@token
              AND j.lease_generation=@generation AND j.lease_expires_at>clock_timestamp()
            FOR UPDATE OF j FOR SHARE OF actor
            """, db, tx) { CommandTimeout = 15 })
        {
            fence.Parameters.AddWithValue("job", job.JobId);
            fence.Parameters.AddWithValue("document", source.DocumentId);
            AddUuid(fence, "project", source.ProjectId);
            AddUuid(fence, "actual", job.ActualUserId);
            AddUuid(fence, "effective", job.EffectiveUserId);
            fence.Parameters.AddWithValue("owner", job.LeaseOwner);
            fence.Parameters.AddWithValue("token", job.LeaseToken.Value);
            fence.Parameters.AddWithValue("generation", job.LeaseGeneration);
            if (await fence.ExecuteScalarAsync(ct) is not Guid)
                return PulseAiDocumentIndexDecision.Reject("index_lease_or_actor_revoked");
        }

        bool visible, consent;
        Guid? uploadedBy, workRegisterId;
        await using (var document = new NpgsqlCommand("""
            SELECT d.engineering_visible,d.ai_timesheet_context_enabled,
                   d.uploaded_by_user_id,d.work_register_document_id
            FROM project_intake_documents d
            WHERE d.project_intake_document_id=@document AND d.is_active=TRUE
              AND d.project_id IS NOT DISTINCT FROM @project
              AND d.uploaded_at=@uploaded AND d.stored_file_name=@stored
              AND d.original_file_name=@original
              AND COALESCE(d.upload_source,'manual')=@upload_source
              AND COALESCE(d.size_bytes,0)=@bytes
              AND d.pulse_ai_superseded_by_document_id IS NULL
              AND d.pulse_ai_processing_status IN ('extracting','embedding','indexing')
            FOR UPDATE OF d
            """, db, tx) { CommandTimeout = 15 })
        {
            document.Parameters.AddWithValue("document", source.DocumentId);
            AddUuid(document, "project", source.ProjectId);
            document.Parameters.AddWithValue("uploaded", source.UploadedAt);
            document.Parameters.AddWithValue("stored", source.StoredFileName);
            document.Parameters.AddWithValue("original", source.OriginalFileName);
            document.Parameters.AddWithValue("upload_source", source.UploadSource);
            document.Parameters.AddWithValue("bytes", source.SizeBytes);
            await using var reader = await document.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return PulseAiDocumentIndexDecision.Reject("index_source_changed_or_revoked");
            visible = !reader.IsDBNull(0) && reader.GetBoolean(0);
            consent = !reader.IsDBNull(1) && reader.GetBoolean(1);
            uploadedBy = reader.IsDBNull(2) ? null : reader.GetGuid(2);
            workRegisterId = reader.IsDBNull(3) ? null : reader.GetGuid(3);
        }

        // A retry must not revive an explicitly revoked/superseded source version.
        await using (var version = new NpgsqlCommand("""
            SELECT authority_status FROM pulse_ai_document_versions
            WHERE project_intake_document_id=@document AND source_sha256=@hash
            FOR SHARE
            """, db, tx) { CommandTimeout = 15 })
        {
            version.Parameters.AddWithValue("document", source.DocumentId);
            version.Parameters.AddWithValue("hash", sourceSha256);
            if (await version.ExecuteScalarAsync(ct) is string authority
                && authority is not ("candidate" or "approved" or "canonical"))
                return PulseAiDocumentIndexDecision.Reject("index_version_authority_revoked");
        }

        if (string.Equals(source.UploadSource, CelarAiConversationAttachmentPolicy.UploadSource,
                StringComparison.OrdinalIgnoreCase))
        {
            var owner = job.EffectiveUserId ?? job.ActualUserId;
            // No View-As or service/admin fallback may index another person's chat.
            if (owner is null || job.ActualUserId != owner || uploadedBy != owner
                || source.ProjectId is not null || source.AccessScope != "conversation_owner_only")
                return PulseAiDocumentIndexDecision.Reject("index_conversation_owner_mismatch");
            if (!await LockChatPermissionAsync(db, tx, owner.Value, ct))
                return PulseAiDocumentIndexDecision.Reject("index_conversation_permission_revoked");
            await using var attachment = new NpgsqlCommand("""
                SELECT a.pulse_ai_conversation_attachment_id
                FROM pulse_ai_conversation_attachments a
                JOIN pulse_ai_conversations c ON c.pulse_ai_conversation_id=a.pulse_ai_conversation_id
                WHERE a.project_intake_document_id=@document AND a.uploaded_by_user_id=@owner
                  AND a.revoked_at IS NULL AND a.storage_purged_at IS NULL
                  AND a.retention_until>clock_timestamp()
                  AND c.actual_user_id=@owner AND c.effective_user_id=@owner AND c.status='active'
                  AND (c.retention_until IS NULL OR c.retention_until>clock_timestamp())
                FOR SHARE OF a,c
                """, db, tx) { CommandTimeout = 15 };
            attachment.Parameters.AddWithValue("document", source.DocumentId);
            attachment.Parameters.AddWithValue("owner", owner.Value);
            if (await attachment.ExecuteScalarAsync(ct) is not Guid)
                return PulseAiDocumentIndexDecision.Reject("index_conversation_revoked_or_expired");
            return new(true, true, "conversation_owner_only");
        }

        // Unassociated uploads are scanned/extracted, not enrolled in project search.
        if (source.ProjectId is not Guid projectId)
            return new(true, false, "index_project_association_required");
        await using (var project = new NpgsqlCommand("""
            SELECT project_id FROM projects WHERE project_id=@project
              AND lower(trim(COALESCE(status,''))) NOT IN ('closed','completed','cancelled','canceled','archived')
            FOR SHARE
            """, db, tx) { CommandTimeout = 15 })
        {
            project.Parameters.AddWithValue("project", projectId);
            if (await project.ExecuteScalarAsync(ct) is not Guid)
                return PulseAiDocumentIndexDecision.Reject("index_project_closed_or_missing");
        }
        if (workRegisterId is Guid linkedId)
        {
            await using var linked = new NpgsqlCommand("""
                SELECT work_register_document_id FROM work_register_documents
                WHERE work_register_document_id=@linked AND project_id=@project
                  AND lower(trim(COALESCE(status,'')))='active' AND archived_at IS NULL
                FOR SHARE
                """, db, tx) { CommandTimeout = 15 };
            linked.Parameters.AddWithValue("linked", linkedId);
            linked.Parameters.AddWithValue("project", projectId);
            if (await linked.ExecuteScalarAsync(ct) is not Guid)
                return PulseAiDocumentIndexDecision.Reject("index_work_register_source_revoked");
        }
        return !visible ? new(true, false, "index_engineering_visibility_disabled")
            : !consent ? new(true, false, "index_ai_consent_disabled")
            : new(true, true, "authorized_project_index");
    }

    private static async Task<bool> LockChatPermissionAsync(
        NpgsqlConnection db, NpgsqlTransaction tx, Guid owner, CancellationToken ct)
    {
        await using var permission = new NpgsqlCommand("""
            SELECT p.app_permission_id
            FROM app_user_role_assignments a
            JOIN app_roles r ON r.app_role_id=a.app_role_id AND r.is_active=TRUE
            JOIN app_role_permissions rp ON rp.app_role_id=r.app_role_id
            JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id
            WHERE a.user_id=@owner AND a.is_active=TRUE AND p.permission_code=@permission
            FOR SHARE OF a,r,rp,p
            """, db, tx) { CommandTimeout = 15 };
        permission.Parameters.AddWithValue("owner", owner);
        permission.Parameters.AddWithValue("permission", CelarAiConversationAttachmentPolicy.Permission);
        if (await permission.ExecuteScalarAsync(ct) is not null) return true;
        await using var administrator = new NpgsqlCommand("""
            SELECT r.app_role_id FROM app_user_role_assignments a
            JOIN app_roles r ON r.app_role_id=a.app_role_id AND r.is_active=TRUE
            WHERE a.user_id=@owner AND a.is_active=TRUE
              AND upper(r.role_code) IN ('ADMINISTRATOR','SUPER_ADMINISTRATOR')
            FOR SHARE OF a,r
            """, db, tx) { CommandTimeout = 15 };
        administrator.Parameters.AddWithValue("owner", owner);
        return await administrator.ExecuteScalarAsync(ct) is not null;
    }

    public static PulseAiPrivateEmbeddingResult NotRequested(string diagnostic) =>
        new("index_not_authorized", "", "", 0, [], diagnostic, DateTimeOffset.UtcNow);

    private static void AddUuid(NpgsqlCommand command, string name, Guid? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = (object?)value ?? DBNull.Value;
}

public sealed record PulseAiDocumentIndexDecision(bool SourceCurrent, bool IndexAllowed, string DiagnosticCode)
{
    public static PulseAiDocumentIndexDecision Reject(string code) => new(false, false, code);
}

public sealed record PulseAiPreparedDocumentResult(
    Guid VersionId, bool Indexed, int ChunkCount, int EmbeddedChunkCount, string IndexDiagnosticCode);

public sealed class PulseAiDocumentPublicationRejectedException(string diagnosticCode) : Exception(diagnosticCode)
{
    public string DiagnosticCode { get; } = diagnosticCode;
}
