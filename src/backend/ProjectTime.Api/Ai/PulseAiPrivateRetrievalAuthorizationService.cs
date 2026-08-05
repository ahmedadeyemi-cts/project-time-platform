using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateRetrievalAuthorizationService
{
    private readonly ILogger<PulseAiPrivateRetrievalAuthorizationService> _logger;

    public PulseAiPrivateRetrievalAuthorizationService(
        ILogger<PulseAiPrivateRetrievalAuthorizationService> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<PulseAiPrivateRetrievedChunk>> ReauthorizeAsync(
        PulseAiPrivateRagAccess access,
        IReadOnlyList<PulseAiPrivateRetrievedChunk> chunks,
        PulseAiPrivateRetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive || chunks.Count == 0) return [];
        if (MissingDatabaseConfiguration().Count > 0) return [];
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT ch.chunk_id
                FROM pulse_ai_document_chunks ch
                JOIN project_intake_documents d
                  ON d.project_intake_document_id = ch.project_intake_document_id
                JOIN pulse_ai_document_versions v
                  ON v.pulse_ai_document_version_id = ch.pulse_ai_document_version_id
                JOIN projects p ON p.project_id = ch.project_id
                WHERE ch.chunk_id = ANY(@chunk_ids)
                  AND @include_project_documents = TRUE
                  AND ch.is_active = TRUE
                  AND ch.index_status IN ('lexical_ready','embedding_ready','ready')
                  AND d.is_active = TRUE
                  AND COALESCE(d.engineering_visible, FALSE) = TRUE
                  AND COALESCE(d.pulse_ai_processing_status, '') = 'ready'
                  AND d.pulse_ai_active_version_id = ch.pulse_ai_document_version_id
                  AND v.authority_status IN ('approved','canonical')
                  AND (@require_timesheet = FALSE OR ch.ai_timesheet_context_enabled = TRUE)
                  AND (
                    @is_broad = TRUE
                    OR p.project_manager_user_id = @user_id
                    OR EXISTS (
                        SELECT 1 FROM project_assignments pa
                        WHERE pa.project_id = p.project_id
                          AND pa.user_id = @user_id
                    )
                    OR EXISTS (
                        SELECT 1
                        FROM engineering_resource_requests err
                        WHERE err.project_id = p.project_id
                          AND (
                            err.fulfilled_by_user_id = @user_id
                            OR err.assigned_pm_user_id = @user_id
                            OR EXISTS (
                                SELECT 1
                                FROM engineering_resource_request_assignments erra
                                WHERE erra.engineering_resource_request_id = err.engineering_resource_request_id
                                  AND erra.user_id = @user_id
                            )
                          )
                      )
                  )

                UNION

                SELECT ch.chunk_id
                FROM pulse_ai_document_chunks ch
                JOIN project_intake_documents document
                  ON document.project_intake_document_id = ch.project_intake_document_id
                JOIN pulse_ai_document_versions version
                  ON version.pulse_ai_document_version_id = ch.pulse_ai_document_version_id
                JOIN pulse_ai_conversation_attachments attachment
                  ON attachment.project_intake_document_id = ch.project_intake_document_id
                JOIN pulse_ai_conversations conversation
                  ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                WHERE ch.chunk_id = ANY(@chunk_ids)
                  AND cardinality(@attachment_ids) > 0
                  AND @conversation_id IS NOT NULL
                  AND attachment.pulse_ai_conversation_attachment_id = ANY(@attachment_ids)
                  AND attachment.pulse_ai_conversation_id = @conversation_id
                  AND attachment.uploaded_by_user_id = @user_id
                  AND attachment.revoked_at IS NULL
                  AND attachment.retention_until > NOW()
                  AND conversation.actual_user_id = @user_id
                  AND conversation.effective_user_id = @user_id
                  AND conversation.status = 'active'
                  AND (conversation.retention_until IS NULL OR conversation.retention_until > NOW())
                  AND document.upload_source = 'celar_ai_chat_attachment'
                  AND document.uploaded_by_user_id = @user_id
                  AND document.is_active = TRUE
                  AND COALESCE(document.pulse_ai_processing_status, '') = 'ready'
                  AND document.pulse_ai_active_version_id = ch.pulse_ai_document_version_id
                  AND version.authority_status IN ('candidate','approved','canonical')
                  AND ch.is_active = TRUE
                  AND ch.index_status IN ('lexical_ready','embedding_ready','ready');
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("chunk_ids", chunks.Select(chunk => chunk.ChunkId).ToArray());
            command.Parameters.AddWithValue("require_timesheet", query.RequireTimesheetFlag);
            command.Parameters.AddWithValue("include_project_documents", query.IncludeProjectDocuments);
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("user_id", access.UserId);
            var conversationParameter = command.Parameters.Add("conversation_id", NpgsqlTypes.NpgsqlDbType.Uuid);
            conversationParameter.Value = query.ConversationId is null
                ? DBNull.Value
                : query.ConversationId.Value;
            var attachmentParameter = command.Parameters.Add(
                "attachment_ids",
                NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid);
            attachmentParameter.Value = query.AttachmentIds.ToArray();
            var authorized = new HashSet<string>(StringComparer.Ordinal);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                authorized.Add(reader.GetString(0));
            }
            return chunks
                .Where(chunk => authorized.Contains(chunk.ChunkId))
                .Select((chunk, index) => chunk with { RankOrder = index + 1 })
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI prompt-assembly reauthorization failed closed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return [];
        }
    }

    private static IReadOnlyList<string> MissingDatabaseConfiguration()
    {
        try { return ProjectPulseAiDatabaseConnection.Resolve() is null ? ["ProjectPulse AI database connection"] : []; }
        catch (InvalidOperationException exception) { return [exception.Message]; }
    }

    private static string ConnectionString() =>
        ProjectPulseAiDatabaseConnection.Resolve()
        ?? throw new InvalidOperationException("ProjectPulse AI database configuration is unavailable.");

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        OperationCanceledException => "cancelled",
        _ => "private_retrieval_reauthorization_failure"
    };
}
