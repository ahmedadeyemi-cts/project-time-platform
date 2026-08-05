using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class CelarAiConversationAttachmentRepository
{
    private readonly ILogger<CelarAiConversationAttachmentRepository> _logger;

    public CelarAiConversationAttachmentRepository(
        ILogger<CelarAiConversationAttachmentRepository> logger)
    {
        _logger = logger;
    }

    public bool DatabaseConfigured => MissingDatabaseConfiguration().Count == 0;

    public async Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured) return false;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    to_regclass('public.pulse_ai_conversation_attachments') IS NOT NULL
                    AND EXISTS (
                        SELECT 1 FROM schema_migrations
                        WHERE migration_id = '072_celar_ai_conversation_attachments'
                    );
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }
        catch
        {
            return false;
        }
    }

    public async Task<CelarAiConversationAttachmentUsage> GetUsageAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken))
            return new(0, 0, 0, false);
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    EXISTS (
                        SELECT 1
                        FROM pulse_ai_conversations conversation
                        WHERE conversation.pulse_ai_conversation_id = @conversation_id
                          AND conversation.actual_user_id = @user_id
                          AND conversation.effective_user_id = @user_id
                          AND conversation.status = 'active'
                    ),
                    COUNT(attachment.pulse_ai_conversation_attachment_id)::integer,
                    COALESCE(SUM(document.size_bytes), 0)::bigint,
                    (
                        SELECT COUNT(*)::integer
                        FROM pulse_ai_conversation_attachments processing_attachment
                        JOIN pulse_ai_conversations processing_conversation
                          ON processing_conversation.pulse_ai_conversation_id = processing_attachment.pulse_ai_conversation_id
                        JOIN project_intake_documents processing_document
                          ON processing_document.project_intake_document_id = processing_attachment.project_intake_document_id
                        WHERE processing_attachment.uploaded_by_user_id = @user_id
                          AND processing_attachment.revoked_at IS NULL
                          AND processing_attachment.retention_until > NOW()
                          AND processing_conversation.actual_user_id = @user_id
                          AND processing_conversation.effective_user_id = @user_id
                          AND processing_conversation.status = 'active'
                          AND (processing_conversation.retention_until IS NULL OR processing_conversation.retention_until > NOW())
                          AND processing_document.uploaded_by_user_id = @user_id
                          AND processing_document.upload_source = 'celar_ai_chat_attachment'
                          AND processing_document.is_active = TRUE
                          AND COALESCE(processing_document.pulse_ai_processing_status, 'not_requested') IN (
                              'not_requested','queued','scanning','extracting','awaiting_ocr',
                              'embedding','indexing','retry_wait'
                          )
                    )
                FROM pulse_ai_conversation_attachments attachment
                JOIN pulse_ai_conversations conversation
                  ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                JOIN project_intake_documents document
                  ON document.project_intake_document_id = attachment.project_intake_document_id
                WHERE attachment.pulse_ai_conversation_id = @conversation_id
                  AND attachment.uploaded_by_user_id = @user_id
                  AND conversation.actual_user_id = @user_id
                  AND conversation.effective_user_id = @user_id
                  AND conversation.status = 'active'
                  AND attachment.revoked_at IS NULL
                  AND attachment.retention_until > NOW()
                  AND document.uploaded_by_user_id = @user_id
                  AND document.upload_source = 'celar_ai_chat_attachment'
                  AND document.is_active = TRUE;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("conversation_id", conversationId);
            command.Parameters.AddWithValue("user_id", userId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return new(0, 0, 0, false);
            return new(
                ActiveCount: reader.GetInt32(1),
                ActiveBytes: reader.GetInt64(2),
                ActiveProcessingCount: reader.GetInt32(3),
                ConversationOwnedAndActive: reader.GetBoolean(0));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment usage lookup failed. ConversationId={ConversationId} Diagnostic={Diagnostic}",
                conversationId,
                Diagnostic(exception));
            return new(0, 0, 0, false);
        }
    }

    public async Task<CelarAiStoredConversationAttachment?> CreateAsync(
        Guid attachmentId,
        Guid documentId,
        Guid conversationId,
        Guid userId,
        string originalFileName,
        string storedFileName,
        string storagePath,
        string contentType,
        long sizeBytes,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return null;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // Serialize per-user admission across conversations and replicas.
            // The lock key is a one-way database hash of the user UUID and is
            // held only for this metadata transaction.
            const string userAdmissionLockSql = "SELECT pg_advisory_xact_lock(hashtextextended(@user_id::text, 0));";
            await using (var userAdmissionLock = new NpgsqlCommand(userAdmissionLockSql, connection, transaction))
            {
                userAdmissionLock.Parameters.AddWithValue("user_id", userId);
                await userAdmissionLock.ExecuteNonQueryAsync(cancellationToken);
            }

            const string lockSql = """
                SELECT 1
                FROM pulse_ai_conversations
                WHERE pulse_ai_conversation_id = @conversation_id
                  AND actual_user_id = @user_id
                  AND effective_user_id = @user_id
                  AND status = 'active'
                  AND (retention_until IS NULL OR retention_until > NOW())
                FOR UPDATE;
                """;
            await using (var lockCommand = new NpgsqlCommand(lockSql, connection, transaction))
            {
                lockCommand.Parameters.AddWithValue("conversation_id", conversationId);
                lockCommand.Parameters.AddWithValue("user_id", userId);
                if (await lockCommand.ExecuteScalarAsync(cancellationToken) is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }
            }

            const string usageSql = """
                SELECT COUNT(*)::integer, COALESCE(SUM(document.size_bytes), 0)::bigint
                FROM pulse_ai_conversation_attachments attachment
                JOIN project_intake_documents document
                  ON document.project_intake_document_id = attachment.project_intake_document_id
                WHERE attachment.pulse_ai_conversation_id = @conversation_id
                  AND attachment.uploaded_by_user_id = @user_id
                  AND attachment.revoked_at IS NULL
                  AND attachment.retention_until > NOW()
                  AND document.uploaded_by_user_id = @user_id
                  AND document.upload_source = 'celar_ai_chat_attachment'
                  AND document.is_active = TRUE;
                """;
            var activeCount = 0;
            long activeBytes = 0;
            await using (var usageCommand = new NpgsqlCommand(usageSql, connection, transaction))
            {
                usageCommand.Parameters.AddWithValue("conversation_id", conversationId);
                await using var usageReader = await usageCommand.ExecuteReaderAsync(cancellationToken);
                if (await usageReader.ReadAsync(cancellationToken))
                {
                    activeCount = usageReader.GetInt32(0);
                    activeBytes = usageReader.GetInt64(1);
                }
            }
            if (activeCount >= CelarAiConversationAttachmentPolicy.MaximumActiveFilesPerConversation
                || activeBytes + sizeBytes > CelarAiConversationAttachmentPolicy.MaximumActiveBytesPerConversation)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            const string processingSql = """
                SELECT COUNT(*)::integer
                FROM pulse_ai_conversation_attachments attachment
                JOIN pulse_ai_conversations conversation
                  ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                JOIN project_intake_documents document
                  ON document.project_intake_document_id = attachment.project_intake_document_id
                WHERE attachment.uploaded_by_user_id = @user_id
                  AND attachment.revoked_at IS NULL
                  AND attachment.retention_until > NOW()
                  AND conversation.actual_user_id = @user_id
                  AND conversation.effective_user_id = @user_id
                  AND conversation.status = 'active'
                  AND (conversation.retention_until IS NULL OR conversation.retention_until > NOW())
                  AND document.uploaded_by_user_id = @user_id
                  AND document.upload_source = 'celar_ai_chat_attachment'
                  AND document.is_active = TRUE
                  AND COALESCE(document.pulse_ai_processing_status, 'not_requested') IN (
                      'not_requested','queued','scanning','extracting','awaiting_ocr',
                      'embedding','indexing','retry_wait'
                  );
                """;
            await using (var processingCommand = new NpgsqlCommand(processingSql, connection, transaction))
            {
                processingCommand.Parameters.AddWithValue("user_id", userId);
                var activeProcessing = Convert.ToInt32(
                    await processingCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
                if (activeProcessing >= CelarAiConversationAttachmentPolicy.MaximumActiveProcessingAttachmentsPerUser)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }
            }

            const string documentSql = """
                INSERT INTO project_intake_documents (
                    project_intake_document_id,
                    project_intake_request_id,
                    project_id,
                    document_type,
                    document_category,
                    original_file_name,
                    stored_file_name,
                    storage_path,
                    content_type,
                    size_bytes,
                    uploaded_by_user_id,
                    upload_source,
                    engineering_visible,
                    ai_timesheet_context_enabled,
                    extraction_status,
                    pulse_ai_processing_status,
                    pulse_ai_classification,
                    uploaded_at,
                    is_active
                ) VALUES (
                    @document_id,
                    NULL,
                    NULL,
                    'chat_attachment',
                    'chat_attachment',
                    @original_file_name,
                    @stored_file_name,
                    @storage_path,
                    @content_type,
                    @size_bytes,
                    @user_id,
                    'celar_ai_chat_attachment',
                    FALSE,
                    FALSE,
                    'not_started',
                    'not_requested',
                    'restricted_conversation_attachment',
                    NOW(),
                    TRUE
                );
                """;
            await using (var documentCommand = new NpgsqlCommand(documentSql, connection, transaction))
            {
                documentCommand.Parameters.AddWithValue("document_id", documentId);
                documentCommand.Parameters.AddWithValue("original_file_name", Clean(originalFileName, 240));
                documentCommand.Parameters.AddWithValue("stored_file_name", Clean(storedFileName, 300));
                documentCommand.Parameters.AddWithValue("storage_path", storagePath);
                documentCommand.Parameters.AddWithValue("content_type", Clean(contentType, 160));
                documentCommand.Parameters.AddWithValue("size_bytes", sizeBytes);
                documentCommand.Parameters.AddWithValue("user_id", userId);
                await documentCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var retentionUntil = DateTimeOffset.UtcNow.AddDays(CelarAiConversationAttachmentPolicy.RetentionDays);
            const string attachmentSql = """
                INSERT INTO pulse_ai_conversation_attachments (
                    pulse_ai_conversation_attachment_id,
                    pulse_ai_conversation_id,
                    project_intake_document_id,
                    uploaded_by_user_id,
                    correlation_id,
                    retention_until
                ) VALUES (
                    @attachment_id,
                    @conversation_id,
                    @document_id,
                    @user_id,
                    @correlation_id,
                    @retention_until
                );
                """;
            await using (var attachmentCommand = new NpgsqlCommand(attachmentSql, connection, transaction))
            {
                attachmentCommand.Parameters.AddWithValue("attachment_id", attachmentId);
                attachmentCommand.Parameters.AddWithValue("conversation_id", conversationId);
                attachmentCommand.Parameters.AddWithValue("document_id", documentId);
                attachmentCommand.Parameters.AddWithValue("user_id", userId);
                attachmentCommand.Parameters.AddWithValue("correlation_id", Clean(correlationId, 160));
                attachmentCommand.Parameters.AddWithValue("retention_until", retentionUntil);
                await attachmentCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            var attachment = new CelarAiConversationAttachment(
                attachmentId,
                conversationId,
                documentId,
                Clean(originalFileName, 240),
                Clean(contentType, 160),
                sizeBytes,
                "not_requested",
                string.Empty,
                null,
                retentionUntil,
                null,
                DateTimeOffset.UtcNow);
            return new(attachment, storagePath, storedFileName);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment metadata creation failed. ConversationId={ConversationId} Diagnostic={Diagnostic}",
                conversationId,
                Diagnostic(exception));
            return null;
        }
    }

    public async Task<IReadOnlyList<CelarAiConversationAttachment>> ListAsync(
        Guid conversationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return [];
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    attachment.pulse_ai_conversation_attachment_id,
                    attachment.pulse_ai_conversation_id,
                    attachment.project_intake_document_id,
                    document.original_file_name,
                    COALESCE(document.content_type, 'application/octet-stream'),
                    COALESCE(document.size_bytes, 0)::bigint,
                    CASE
                        WHEN attachment.revoked_at IS NOT NULL THEN 'revoked'
                        WHEN attachment.retention_until <= NOW() THEN 'expired'
                        ELSE COALESCE(document.pulse_ai_processing_status, 'not_requested')
                    END,
                    COALESCE(document.pulse_ai_processing_error_code, ''),
                    job.pulse_ai_document_processing_job_id,
                    attachment.retention_until,
                    attachment.revoked_at,
                    attachment.created_at
                FROM pulse_ai_conversation_attachments attachment
                JOIN pulse_ai_conversations conversation
                  ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                JOIN project_intake_documents document
                  ON document.project_intake_document_id = attachment.project_intake_document_id
                LEFT JOIN LATERAL (
                    SELECT processing.pulse_ai_document_processing_job_id
                    FROM pulse_ai_document_processing_jobs processing
                    WHERE processing.project_intake_document_id = attachment.project_intake_document_id
                    ORDER BY processing.requested_at DESC
                    LIMIT 1
                ) job ON TRUE
                WHERE attachment.pulse_ai_conversation_id = @conversation_id
                  AND attachment.uploaded_by_user_id = @user_id
                  AND conversation.actual_user_id = @user_id
                  AND conversation.effective_user_id = @user_id
                  AND conversation.status = 'active'
                  AND document.uploaded_by_user_id = @user_id
                  AND document.upload_source = 'celar_ai_chat_attachment'
                ORDER BY attachment.created_at;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("conversation_id", conversationId);
            command.Parameters.AddWithValue("user_id", userId);
            var rows = new List<CelarAiConversationAttachment>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add(Read(reader));
            return rows;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment listing failed. ConversationId={ConversationId} Diagnostic={Diagnostic}",
                conversationId,
                Diagnostic(exception));
            return [];
        }
    }

    public async Task MarkSelectedAsync(
        Guid conversationId,
        Guid userId,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default)
    {
        if (attachmentIds.Count == 0 || !await IsSchemaReadyAsync(cancellationToken)) return;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                UPDATE pulse_ai_conversation_attachments attachment
                SET last_selected_at = NOW()
                FROM pulse_ai_conversations conversation,
                     project_intake_documents document
                WHERE attachment.pulse_ai_conversation_attachment_id = ANY(@attachment_ids)
                  AND conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                  AND document.project_intake_document_id = attachment.project_intake_document_id
                  AND attachment.pulse_ai_conversation_id = @conversation_id
                  AND attachment.uploaded_by_user_id = @user_id
                  AND attachment.revoked_at IS NULL
                  AND attachment.retention_until > NOW()
                  AND conversation.actual_user_id = @user_id
                  AND conversation.effective_user_id = @user_id
                  AND conversation.status = 'active'
                  AND (conversation.retention_until IS NULL OR conversation.retention_until > NOW())
                  AND document.uploaded_by_user_id = @user_id
                  AND document.upload_source = 'celar_ai_chat_attachment'
                  AND document.is_active = TRUE
                  AND COALESCE(document.pulse_ai_processing_status, '') = 'ready';
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("attachment_ids", attachmentIds.ToArray());
            command.Parameters.AddWithValue("conversation_id", conversationId);
            command.Parameters.AddWithValue("user_id", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment selection evidence could not be saved. ConversationId={ConversationId} Diagnostic={Diagnostic}",
                conversationId,
                Diagnostic(exception));
        }
    }

    public async Task<string?> RevokeAsync(
        Guid conversationId,
        Guid attachmentId,
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return null;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            const string selectSql = """
                SELECT attachment.project_intake_document_id, document.storage_path
                FROM pulse_ai_conversation_attachments attachment
                JOIN pulse_ai_conversations conversation
                  ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                JOIN project_intake_documents document
                  ON document.project_intake_document_id = attachment.project_intake_document_id
                WHERE attachment.pulse_ai_conversation_attachment_id = @attachment_id
                  AND attachment.pulse_ai_conversation_id = @conversation_id
                  AND attachment.uploaded_by_user_id = @user_id
                  AND conversation.actual_user_id = @user_id
                  AND conversation.effective_user_id = @user_id
                  AND conversation.status = 'active'
                  AND (conversation.retention_until IS NULL OR conversation.retention_until > NOW())
                  AND document.uploaded_by_user_id = @user_id
                  AND document.upload_source = 'celar_ai_chat_attachment'
                FOR UPDATE;
                """;
            Guid documentId;
            string storagePath;
            await using (var selectCommand = new NpgsqlCommand(selectSql, connection, transaction))
            {
                selectCommand.Parameters.AddWithValue("attachment_id", attachmentId);
                selectCommand.Parameters.AddWithValue("conversation_id", conversationId);
                selectCommand.Parameters.AddWithValue("user_id", userId);
                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return null;
                }
                documentId = reader.GetGuid(0);
                storagePath = reader.GetString(1);
            }

            const string revokeSql = """
                UPDATE pulse_ai_conversation_attachments
                SET revoked_at = COALESCE(revoked_at, NOW()),
                    revocation_reason = @reason
                WHERE pulse_ai_conversation_attachment_id = @attachment_id;

                UPDATE project_intake_documents
                SET is_active = FALSE,
                    pulse_ai_processing_status = 'cancelled',
                    pulse_ai_processing_error_code = 'attachment_revoked',
                    pulse_ai_processing_updated_at = NOW()
                WHERE project_intake_document_id = @document_id;

                UPDATE pulse_ai_document_chunks
                SET is_active = FALSE,
                    index_status = 'revoked',
                    embedding_status = CASE
                        WHEN embedding_status = 'ready' THEN 'revoked'
                        ELSE embedding_status
                    END
                WHERE project_intake_document_id = @document_id;

                UPDATE pulse_ai_document_versions
                SET authority_status = 'revoked',
                    index_status = 'revoked'
                WHERE project_intake_document_id = @document_id;

                UPDATE pulse_ai_document_processing_jobs
                SET cancellation_requested = TRUE,
                    job_status = 'cancel_requested'
                WHERE project_intake_document_id = @document_id
                  AND job_status IN (
                    'queued','scanning','extracting','awaiting_ocr',
                    'embedding','indexing','retry_wait'
                  );
                """;
            await using (var revokeCommand = new NpgsqlCommand(revokeSql, connection, transaction))
            {
                revokeCommand.Parameters.AddWithValue("attachment_id", attachmentId);
                revokeCommand.Parameters.AddWithValue("document_id", documentId);
                revokeCommand.Parameters.AddWithValue("reason", Clean(reason, 300));
                await revokeCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return storagePath;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment revocation failed. AttachmentId={AttachmentId} Diagnostic={Diagnostic}",
                attachmentId,
                Diagnostic(exception));
            return null;
        }
    }

    public async Task MarkQueueFailureAsync(
        Guid documentId,
        string diagnosticCode,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured) return;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                UPDATE project_intake_documents
                SET pulse_ai_processing_status = 'failed',
                    pulse_ai_processing_error_code = @diagnostic_code,
                    pulse_ai_processing_updated_at = NOW()
                WHERE project_intake_document_id = @document_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("document_id", documentId);
            command.Parameters.AddWithValue("diagnostic_code", Clean(diagnosticCode, 120));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment queue-failure evidence could not be saved. DocumentId={DocumentId}",
                documentId);
        }
    }

    public async Task<IReadOnlySet<string>?> LoadTrackedStoragePathsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return null;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT document.storage_path
                FROM pulse_ai_conversation_attachments attachment
                JOIN project_intake_documents document
                  ON document.project_intake_document_id = attachment.project_intake_document_id
                WHERE document.upload_source = 'celar_ai_chat_attachment'
                  AND attachment.storage_purged_at IS NULL;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            var comparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var paths = new HashSet<string>(comparer);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0)) paths.Add(Path.GetFullPath(reader.GetString(0)));
            }
            return paths;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment orphan reconciliation was skipped because tracked storage paths were unavailable. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return null;
        }
    }

    public async Task<IReadOnlyList<CelarAiAttachmentPurgeCandidate>> ClaimPurgeCandidatesAsync(
        int maximum,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return [];
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            const string selectSql = """
                SELECT
                    attachment.pulse_ai_conversation_attachment_id,
                    attachment.project_intake_document_id,
                    document.storage_path
                FROM pulse_ai_conversation_attachments attachment
                JOIN pulse_ai_conversations conversation
                  ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                JOIN project_intake_documents document
                  ON document.project_intake_document_id = attachment.project_intake_document_id
                WHERE attachment.storage_purged_at IS NULL
                  AND (
                    attachment.retention_until <= NOW()
                    OR attachment.revoked_at IS NOT NULL
                    OR conversation.status <> 'active'
                    OR (conversation.retention_until IS NOT NULL AND conversation.retention_until <= NOW())
                    OR COALESCE(document.pulse_ai_processing_status, '') = 'quarantined'
                  )
                ORDER BY attachment.retention_until, attachment.created_at
                LIMIT @maximum
                FOR UPDATE OF attachment SKIP LOCKED;
                """;
            var candidates = new List<CelarAiAttachmentPurgeCandidate>();
            await using (var selectCommand = new NpgsqlCommand(selectSql, connection, transaction))
            {
                selectCommand.Parameters.AddWithValue(
                    "maximum",
                    Math.Clamp(maximum, 1, CelarAiConversationAttachmentPolicy.RetentionBatchSize));
                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    candidates.Add(new(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2)));
                }
            }
            if (candidates.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return [];
            }

            var attachmentIds = candidates.Select(value => value.AttachmentId).ToArray();
            var documentIds = candidates.Select(value => value.DocumentId).ToArray();
            const string revokeSql = """
                UPDATE pulse_ai_conversation_attachments attachment
                SET revoked_at = COALESCE(attachment.revoked_at, NOW()),
                    revocation_reason = CASE
                        WHEN attachment.revocation_reason <> '' THEN attachment.revocation_reason
                        WHEN document.pulse_ai_processing_status = 'quarantined' THEN 'malware_quarantined'
                        ELSE 'retention_expired'
                    END,
                    purge_attempt_count = attachment.purge_attempt_count + 1,
                    purge_last_attempt_at = NOW(),
                    purge_diagnostic_code = 'physical_cleanup_pending'
                FROM project_intake_documents document
                WHERE attachment.project_intake_document_id = document.project_intake_document_id
                  AND attachment.pulse_ai_conversation_attachment_id = ANY(@attachment_ids);

                UPDATE project_intake_documents
                SET is_active = FALSE,
                    pulse_ai_processing_status = CASE
                        WHEN pulse_ai_processing_status = 'quarantined' THEN 'quarantined'
                        ELSE 'cancelled'
                    END,
                    pulse_ai_processing_error_code = CASE
                        WHEN pulse_ai_processing_status = 'quarantined' THEN pulse_ai_processing_error_code
                        ELSE 'attachment_retention_expired_or_revoked'
                    END,
                    pulse_ai_processing_updated_at = NOW()
                WHERE project_intake_document_id = ANY(@document_ids);

                UPDATE pulse_ai_document_chunks
                SET is_active = FALSE,
                    index_status = 'revoked',
                    embedding_status = CASE
                        WHEN embedding_status = 'ready' THEN 'revoked'
                        ELSE embedding_status
                    END
                WHERE project_intake_document_id = ANY(@document_ids);

                UPDATE pulse_ai_document_versions
                SET authority_status = 'revoked',
                    index_status = 'revoked'
                WHERE project_intake_document_id = ANY(@document_ids);

                UPDATE pulse_ai_document_processing_jobs
                SET cancellation_requested = TRUE,
                    job_status = 'cancel_requested'
                WHERE project_intake_document_id = ANY(@document_ids)
                  AND job_status IN (
                    'queued','scanning','extracting','awaiting_ocr',
                    'embedding','indexing','retry_wait'
                  );
                """;
            await using (var revokeCommand = new NpgsqlCommand(revokeSql, connection, transaction))
            {
                revokeCommand.Parameters.AddWithValue("attachment_ids", attachmentIds);
                revokeCommand.Parameters.AddWithValue("document_ids", documentIds);
                await revokeCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return candidates;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment retention claim failed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return [];
        }
    }

    public async Task RecordStoragePurgeAsync(
        Guid attachmentId,
        bool succeeded,
        string diagnosticCode,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured) return;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                UPDATE pulse_ai_conversation_attachments
                SET storage_purged_at = CASE WHEN @succeeded THEN COALESCE(storage_purged_at, NOW()) ELSE storage_purged_at END,
                    purge_diagnostic_code = @diagnostic_code,
                    purge_last_attempt_at = NOW()
                WHERE pulse_ai_conversation_attachment_id = @attachment_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("attachment_id", attachmentId);
            command.Parameters.AddWithValue("succeeded", succeeded);
            command.Parameters.AddWithValue(
                "diagnostic_code",
                Clean(succeeded ? string.Empty : diagnosticCode, 120));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment purge evidence could not be saved. AttachmentId={AttachmentId} Diagnostic={Diagnostic}",
                attachmentId,
                Diagnostic(exception));
        }
    }

    public async Task<bool> FinalizeStoragePurgeAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return false;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            const string lockSql = """
                SELECT 1
                FROM pulse_ai_conversation_attachments
                WHERE pulse_ai_conversation_attachment_id = @attachment_id
                FOR UPDATE;
                """;
            await using (var attachmentLock = new NpgsqlCommand(lockSql, connection, transaction))
            {
                attachmentLock.Parameters.AddWithValue("attachment_id", attachmentId);
                if (await attachmentLock.ExecuteScalarAsync(cancellationToken) is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }
            const string sql = """
                INSERT INTO pulse_ai_conversation_attachment_purge_audit (
                    pulse_ai_conversation_attachment_id,
                    pulse_ai_conversation_id,
                    project_intake_document_id,
                    uploaded_by_user_id,
                    correlation_id,
                    purge_reason,
                    retention_until,
                    revoked_at,
                    storage_purged_at
                )
                SELECT
                    attachment.pulse_ai_conversation_attachment_id,
                    attachment.pulse_ai_conversation_id,
                    attachment.project_intake_document_id,
                    attachment.uploaded_by_user_id,
                    attachment.correlation_id,
                    CASE
                        WHEN attachment.revocation_reason <> '' THEN attachment.revocation_reason
                        ELSE 'retention_expired'
                    END,
                    attachment.retention_until,
                    attachment.revoked_at,
                    NOW()
                FROM pulse_ai_conversation_attachments attachment
                WHERE attachment.pulse_ai_conversation_attachment_id = @attachment_id
                ON CONFLICT (pulse_ai_conversation_attachment_id) DO NOTHING;

                UPDATE pulse_ai_conversations conversation
                SET title = 'Celar AI private attachment conversation',
                    updated_at = NOW()
                FROM pulse_ai_conversation_attachments attachment
                WHERE attachment.pulse_ai_conversation_attachment_id = @attachment_id
                  AND conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id;

                CREATE TEMP TABLE celar_ai_attachment_purge_answer_runs (
                    pulse_ai_answer_run_id UUID PRIMARY KEY
                ) ON COMMIT DROP;

                INSERT INTO celar_ai_attachment_purge_answer_runs (
                    pulse_ai_answer_run_id
                )
                SELECT citation.pulse_ai_answer_run_id
                FROM pulse_ai_answer_citations citation
                JOIN pulse_ai_conversation_attachments attachment
                  ON attachment.project_intake_document_id = citation.project_intake_document_id
                WHERE attachment.pulse_ai_conversation_attachment_id = @attachment_id
                UNION
                SELECT answer_run.pulse_ai_answer_run_id
                FROM pulse_ai_answer_runs answer_run
                JOIN pulse_ai_conversation_attachments attachment
                  ON attachment.pulse_ai_conversation_attachment_id = @attachment_id
                WHERE answer_run.request_filters_json @> jsonb_build_object(
                    'AttachmentIds',
                    jsonb_build_array(attachment.pulse_ai_conversation_attachment_id::text)
                )
                UNION
                SELECT assistant_message.private_answer_run_id
                FROM pulse_ai_system_inquiry_runs inquiry
                JOIN pulse_ai_conversation_messages user_message
                  ON user_message.pulse_ai_conversation_message_id = inquiry.user_message_id
                JOIN pulse_ai_conversation_messages assistant_message
                  ON assistant_message.pulse_ai_conversation_message_id = inquiry.assistant_message_id
                JOIN pulse_ai_conversation_attachments attachment
                  ON attachment.pulse_ai_conversation_attachment_id = @attachment_id
                 AND attachment.pulse_ai_conversation_id = user_message.pulse_ai_conversation_id
                WHERE assistant_message.private_answer_run_id IS NOT NULL
                  AND user_message.structured_response_json @> jsonb_build_object(
                      'attachmentIds',
                      jsonb_build_array(attachment.pulse_ai_conversation_attachment_id::text)
                  );

                -- Serialize retention with answer completion and feedback.
                -- Either those writes commit before this lock and are redacted
                -- below, or they observe the purged diagnostic after commit.
                SELECT answer_run.pulse_ai_answer_run_id
                FROM pulse_ai_answer_runs answer_run
                WHERE answer_run.pulse_ai_answer_run_id IN (
                    SELECT pulse_ai_answer_run_id
                    FROM celar_ai_attachment_purge_answer_runs
                )
                FOR UPDATE;

                UPDATE pulse_ai_conversation_messages message
                SET message_text = '[Private attachment-derived content removed by the governed retention policy.]',
                    structured_response_json = jsonb_build_object(
                        'status', 'private_attachment_retention_purged',
                        'rawContentRetained', FALSE
                    ),
                    private_answer_run_id = NULL,
                    model_provider = '',
                    model_name = '',
                    source_states_json = '[]'::jsonb
                WHERE message.private_answer_run_id IN (
                    SELECT pulse_ai_answer_run_id
                    FROM celar_ai_attachment_purge_answer_runs
                )
                OR message.pulse_ai_conversation_message_id IN (
                    SELECT inquiry.user_message_id
                    FROM pulse_ai_system_inquiry_runs inquiry
                    JOIN pulse_ai_conversation_messages assistant_message
                      ON assistant_message.pulse_ai_conversation_message_id = inquiry.assistant_message_id
                    WHERE assistant_message.private_answer_run_id IN (
                        SELECT pulse_ai_answer_run_id
                        FROM celar_ai_attachment_purge_answer_runs
                    )
                )
                OR EXISTS (
                    SELECT 1
                    FROM pulse_ai_conversation_attachments attachment
                    WHERE attachment.pulse_ai_conversation_attachment_id = @attachment_id
                      AND attachment.pulse_ai_conversation_id = message.pulse_ai_conversation_id
                      AND message.structured_response_json @> jsonb_build_object(
                          'attachmentIds',
                          jsonb_build_array(attachment.pulse_ai_conversation_attachment_id::text)
                      )
                );

                UPDATE pulse_ai_answer_feedback feedback
                SET feedback_reason = '[Private attachment-derived feedback removed by the governed retention policy.]',
                    corrected_answer_json = jsonb_build_object(
                        'status', 'private_attachment_retention_purged',
                        'rawContentRetained', FALSE
                    ),
                    training_candidate = FALSE,
                    training_review_status = 'needs_redaction'
                WHERE feedback.pulse_ai_answer_run_id IN (
                    SELECT pulse_ai_answer_run_id
                    FROM celar_ai_attachment_purge_answer_runs
                );

                UPDATE pulse_ai_answer_runs answer_run
                SET answer_status = 'blocked',
                    project_code = '',
                    question_text = '[Private attachment-derived question removed by the governed retention policy.]',
                    question_sha256 = repeat('0', 64),
                    request_filters_json = '{}'::jsonb,
                    private_model_provider = '',
                    private_model_name = '',
                    retrieval_mode = 'none',
                    retrieved_chunk_count = 0,
                    cited_source_count = 0,
                    source_document_count = 0,
                    source_version_count = 0,
                    input_character_count = 0,
                    output_character_count = 0,
                    confidence_score = 0,
                    coverage_score = 0,
                    citation_coverage_score = 0,
                    answer_json = jsonb_build_object(
                        'status', 'private_attachment_retention_purged',
                        'rawContentRetained', FALSE
                    ),
                    warning_codes = jsonb_build_array('private_attachment_retention_purged'),
                    missing_evidence = '[]'::jsonb,
                    conflicts_json = '[]'::jsonb,
                    source_health_json = '{}'::jsonb,
                    privacy_evidence_json = jsonb_build_object(
                        'privateAttachmentContentPurged', TRUE
                    ),
                    diagnostic_code = 'private_attachment_retention_purged',
                    diagnostic_message = ''
                WHERE answer_run.pulse_ai_answer_run_id IN (
                    SELECT pulse_ai_answer_run_id
                    FROM celar_ai_attachment_purge_answer_runs
                );

                DELETE FROM pulse_ai_answer_citations citation
                WHERE citation.pulse_ai_answer_run_id IN (
                    SELECT pulse_ai_answer_run_id
                    FROM celar_ai_attachment_purge_answer_runs
                );

                DELETE FROM project_intake_documents document
                USING pulse_ai_conversation_attachments attachment
                WHERE attachment.pulse_ai_conversation_attachment_id = @attachment_id
                  AND document.project_intake_document_id = attachment.project_intake_document_id;
                """;
            await using (var command = new NpgsqlCommand(sql, connection, transaction))
            {
                command.Parameters.AddWithValue("attachment_id", attachmentId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            const string verifySql = """
                SELECT EXISTS (
                    SELECT 1
                    FROM pulse_ai_conversation_attachment_purge_audit
                    WHERE pulse_ai_conversation_attachment_id = @attachment_id
                ) AND NOT EXISTS (
                    SELECT 1
                    FROM pulse_ai_conversation_attachments
                    WHERE pulse_ai_conversation_attachment_id = @attachment_id
                );
                """;
            await using var verify = new NpgsqlCommand(verifySql, connection, transaction);
            verify.Parameters.AddWithValue("attachment_id", attachmentId);
            var completed = Convert.ToBoolean(
                await verify.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!completed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI attachment content purge could not be finalized. AttachmentId={AttachmentId} Diagnostic={Diagnostic}",
                attachmentId,
                Diagnostic(exception));
            return false;
        }
    }

    private static CelarAiConversationAttachment Read(NpgsqlDataReader reader) => new(
        AttachmentId: reader.GetGuid(0),
        ConversationId: reader.GetGuid(1),
        DocumentId: reader.GetGuid(2),
        OriginalFileName: reader.GetString(3),
        ContentType: reader.GetString(4),
        SizeBytes: reader.GetInt64(5),
        ProcessingStatus: reader.GetString(6),
        DiagnosticCode: reader.GetString(7),
        ProcessingJobId: reader.IsDBNull(8) ? null : reader.GetGuid(8),
        RetentionUntil: reader.GetFieldValue<DateTimeOffset>(9),
        RevokedAt: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        CreatedAt: reader.GetFieldValue<DateTimeOffset>(11));

    private static IReadOnlyList<string> MissingDatabaseConfiguration()
    {
        var required = new[] { "PTP_DB_HOST", "PTP_DB_PORT", "PTP_DB_NAME", "PTP_DB_USER", "PTP_DB_PASSWORD" };
        return required.Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))).ToArray();
    }

    private static string ConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("PTP_DB_HOST"),
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = Environment.GetEnvironmentVariable("PTP_DB_NAME"),
            Username = Environment.GetEnvironmentVariable("PTP_DB_USER"),
            Password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD"),
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 5,
            Timeout = 8,
            CommandTimeout = 30
        };
        return builder.ConnectionString;
    }

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        OperationCanceledException => "cancelled",
        _ => "celar_ai_attachment_repository_failure"
    };
}
