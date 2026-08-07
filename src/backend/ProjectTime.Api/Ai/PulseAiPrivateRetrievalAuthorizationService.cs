using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateRetrievalAuthorizationService
{
    private const string ProjectDocumentAuthorizationPredicate = """
        (
          @is_broad = TRUE
          OR (@is_pm_lead = TRUE AND (
              EXISTS (
                  SELECT 1 FROM reporting_relationships rr
                  WHERE rr.employee_user_id = p.project_manager_user_id
                    AND (rr.manager_user_id = @user_id OR rr.team_lead_user_id = @user_id)
                    AND rr.effective_start_date <= CURRENT_DATE
                    AND (rr.effective_end_date IS NULL OR rr.effective_end_date >= CURRENT_DATE)
              )
              OR EXISTS (
                  SELECT 1
                  FROM app_users pm
                  JOIN projectpulse_team_scope_assignments scope ON scope.scoped_user_id = @user_id
                  WHERE pm.user_id = p.project_manager_user_id
                    AND scope.is_active = TRUE
                    AND scope.scope_type = 'project_management_team_lead'
                    AND (
                        (scope.team_name IS NOT NULL AND LOWER(COALESCE(pm.team_name,'')) = LOWER(scope.team_name))
                        OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(pm.department_name,'')) = LOWER(scope.department_name))
                        OR scope.manager_user_id = pm.user_id
                    )
              )
          ))
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
        """;

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
            var sql = $$"""
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
                  AND {{ProjectDocumentAuthorizationPredicate}}

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
            AddProjectAuthorizationParameters(command, access);
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
                "Celar AI prompt-assembly reauthorization failed closed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return [];
        }
    }

    public async Task<PulseAiPrivateProjectEvidenceReadiness> LoadProjectEvidenceReadinessAsync(
        NpgsqlConnection connection,
        PulseAiPrivateRagAccess access,
        Guid projectId,
        IReadOnlyList<string> allowedDocumentCategories,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive) return PulseAiPrivateProjectEvidenceReadiness.Empty;
        var categories = allowedDocumentCategories
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sql = $$"""
            SELECT
                COUNT(DISTINCT document.project_intake_document_id)::bigint,
                COUNT(DISTINCT document.project_intake_document_id) FILTER (
                    WHERE LOWER(COALESCE(document.document_category, document.document_type, '')) IN (
                        'sow','statement_of_work','gsd','global_solution_design'
                    )
                )::bigint,
                COUNT(DISTINCT version.pulse_ai_document_version_id)::bigint,
                COUNT(DISTINCT chunk.chunk_id)::bigint,
                COUNT(DISTINCT chunk.chunk_id) FILTER (WHERE chunk.embedding_status = 'ready')::bigint,
                MAX(chunk.processed_at)
            FROM project_intake_documents document
            JOIN pulse_ai_document_versions version
              ON version.pulse_ai_document_version_id = document.pulse_ai_active_version_id
             AND version.project_intake_document_id = document.project_intake_document_id
             AND version.authority_status IN ('approved','canonical')
            JOIN pulse_ai_document_chunks chunk
              ON chunk.pulse_ai_document_version_id = version.pulse_ai_document_version_id
             AND chunk.project_intake_document_id = document.project_intake_document_id
             AND chunk.project_id = document.project_id
             AND chunk.is_active = TRUE
             AND chunk.index_status IN ('lexical_ready','embedding_ready','ready')
            JOIN projects p ON p.project_id = document.project_id
            WHERE document.project_id = @project_id
              AND document.is_active = TRUE
              AND COALESCE(document.engineering_visible, FALSE) = TRUE
              AND COALESCE(document.pulse_ai_processing_status, '') = 'ready'
              AND (cardinality(@categories) = 0 OR LOWER(COALESCE(document.document_category, document.document_type, '')) = ANY(@categories))
              AND {{ProjectDocumentAuthorizationPredicate}};
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.AddWithValue(
            "categories",
            NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text,
            categories);
        AddProjectAuthorizationParameters(command, access);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return PulseAiPrivateProjectEvidenceReadiness.Empty;
        return new PulseAiPrivateProjectEvidenceReadiness(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }

    private static void AddProjectAuthorizationParameters(
        NpgsqlCommand command,
        PulseAiPrivateRagAccess access)
    {
        command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
        command.Parameters.AddWithValue("is_pm_lead", access.IsProjectManagementLead);
        command.Parameters.AddWithValue("user_id", access.UserId);
    }

    private static IReadOnlyList<string> MissingDatabaseConfiguration()
    {
        try { return ProjectPulseAiDatabaseConnection.Resolve() is null ? ["Celar AI database connection"] : []; }
        catch (InvalidOperationException exception) { return [exception.Message]; }
    }

    private static string ConnectionString() =>
        ProjectPulseAiDatabaseConnection.Resolve()
        ?? throw new InvalidOperationException("Celar AI database configuration is unavailable.");

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        OperationCanceledException => "cancelled",
        _ => "private_retrieval_reauthorization_failure"
    };
}

public sealed record PulseAiPrivateProjectEvidenceReadiness(
    long ReadyDocumentCount,
    long ReadySowDocumentCount,
    long ActiveVersionCount,
    long ActiveChunkCount,
    long EmbeddedChunkCount,
    DateTimeOffset? LastIndexedAt)
{
    public static PulseAiPrivateProjectEvidenceReadiness Empty { get; } = new(0, 0, 0, 0, 0, null);
}
