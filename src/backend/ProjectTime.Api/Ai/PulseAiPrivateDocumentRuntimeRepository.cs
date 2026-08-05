using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateDocumentRuntimeRepository
{
    private static readonly HashSet<string> BroadRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "EXECUTIVE"
    };

    private readonly ILogger<PulseAiPrivateDocumentRuntimeRepository> _logger;

    public PulseAiPrivateDocumentRuntimeRepository(
        ILogger<PulseAiPrivateDocumentRuntimeRepository> logger)
    {
        _logger = logger;
    }

    public bool DatabaseConfigured => MissingDatabaseConfiguration().Count == 0;

    public async Task<RuntimeSchemaState> InspectRuntimeSchemaAsync(
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured) return RuntimeSchemaState.Missing;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id = @migration_id),
                    EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id = @rag_migration_id),
                    EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id = @routing_migration_id),
                    EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id = @hardening_migration_id),
                    to_regclass('public.pulse_ai_document_processing_jobs') IS NOT NULL,
                    to_regclass('public.pulse_ai_document_versions') IS NOT NULL,
                    to_regclass('public.pulse_ai_document_sections') IS NOT NULL,
                    to_regclass('public.pulse_ai_document_chunks') IS NOT NULL,
                    to_regclass('public.pulse_ai_document_processing_events') IS NOT NULL,
                    EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE schemaname = 'public'
                          AND tablename = 'pulse_ai_document_chunks'
                          AND indexname = 'ix_pulse_ai_document_chunks_search'
                    );
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("migration_id", PulseAiPrivateRuntimePolicy.MigrationId);
            command.Parameters.AddWithValue("rag_migration_id", PulseAiPrivateRagPolicy.MigrationId);
            command.Parameters.AddWithValue("routing_migration_id", "061_celar_ai_capability_routing");
            command.Parameters.AddWithValue("hardening_migration_id", ProjectPulseAiEncryptionKeyRing.MigrationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return RuntimeSchemaState.Missing;
            return new RuntimeSchemaState(
                MigrationApplied: reader.GetBoolean(0),
                RagMigrationApplied: reader.GetBoolean(1),
                RoutingMigrationApplied: reader.GetBoolean(2),
                HardeningMigrationApplied: reader.GetBoolean(3),
                Jobs: reader.GetBoolean(4),
                Versions: reader.GetBoolean(5),
                Sections: reader.GetBoolean(6),
                Chunks: reader.GetBoolean(7),
                Events: reader.GetBoolean(8),
                LexicalIndex: reader.GetBoolean(9));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI runtime schema inspection failed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return RuntimeSchemaState.Missing;
        }
    }

    public async Task<RuntimeAccess> LoadAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured) return RuntimeAccess.Empty(userId);
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    u.user_id,
                    COALESCE(u.is_active, FALSE),
                    COALESCE(string_agg(DISTINCT r.role_code, ',' ORDER BY r.role_code), ''),
                    COALESCE(string_agg(DISTINCT p.permission_code, ',' ORDER BY p.permission_code), '')
                FROM app_users u
                LEFT JOIN app_user_role_assignments ura
                    ON ura.user_id = u.user_id
                   AND ura.is_active = TRUE
                LEFT JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id
                   AND r.is_active = TRUE
                LEFT JOIN app_role_permissions rp
                    ON rp.app_role_id = r.app_role_id
                LEFT JOIN app_permissions p
                    ON p.app_permission_id = rp.app_permission_id
                WHERE u.user_id = @user_id
                GROUP BY u.user_id, u.is_active;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("user_id", userId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return RuntimeAccess.Empty(userId);
            return new RuntimeAccess(
                UserId: reader.GetGuid(0),
                IsActive: reader.GetBoolean(1),
                RoleCodes: Split(reader.GetString(2)),
                PermissionCodes: Split(reader.GetString(3)));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI runtime access resolution failed. UserId={UserId} Diagnostic={Diagnostic}",
                userId,
                Diagnostic(exception));
            return RuntimeAccess.Empty(userId);
        }
    }

    /// <summary>
    /// Resolves production-readiness evidence for the dedicated automatic
    /// document-admission identity. A configured UUID is not sufficient: the
    /// application user must still exist, be active, and receive the queue
    /// permission through an active role assignment at the time readiness is
    /// evaluated. No identity attributes are returned to the caller.
    /// </summary>
    public async Task<DocumentServicePrincipalReadiness> InspectDocumentServicePrincipalAsync(
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (userId is null) return DocumentServicePrincipalReadiness.NotConfigured;
        if (!DatabaseConfigured) return DocumentServicePrincipalReadiness.DatabaseUnavailable;

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    EXISTS (
                        SELECT 1
                        FROM app_users service_user
                        WHERE service_user.user_id = @service_principal_user_id
                    ),
                    EXISTS (
                        SELECT 1
                        FROM app_users service_user
                        WHERE service_user.user_id = @service_principal_user_id
                          AND COALESCE(service_user.is_active, FALSE) = TRUE
                    ),
                    EXISTS (
                        SELECT 1
                        FROM app_user_role_assignments service_assignment
                        JOIN app_roles service_role
                          ON service_role.app_role_id = service_assignment.app_role_id
                         AND service_role.is_active = TRUE
                        JOIN app_role_permissions service_role_permission
                          ON service_role_permission.app_role_id = service_role.app_role_id
                        JOIN app_permissions service_permission
                          ON service_permission.app_permission_id = service_role_permission.app_permission_id
                        WHERE service_assignment.user_id = @service_principal_user_id
                          AND service_assignment.is_active = TRUE
                          AND service_permission.permission_code = 'QUEUE_PULSE_AI_DOCUMENT_PROCESSING'
                    );
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("service_principal_user_id", userId.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return DocumentServicePrincipalReadiness.LookupUnavailable;

            var exists = reader.GetBoolean(0);
            var active = reader.GetBoolean(1);
            var queuePermissionGranted = reader.GetBoolean(2);
            var diagnosticCode = !exists
                ? "service_principal_user_not_found"
                : !active
                    ? "service_principal_user_inactive"
                    : !queuePermissionGranted
                        ? "service_principal_queue_permission_missing"
                        : "service_principal_authorized";

            return new DocumentServicePrincipalReadiness(
                Configured: true,
                Exists: exists,
                Active: active,
                QueuePermissionGranted: queuePermissionGranted,
                DiagnosticCode: diagnosticCode);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI document service-principal readiness inspection failed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return DocumentServicePrincipalReadiness.LookupUnavailable;
        }
    }

    public async Task<RuntimeCounts> GetCountsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured) return RuntimeCounts.Empty;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    COUNT(*) FILTER (WHERE job_status = 'queued')::bigint,
                    COUNT(*) FILTER (WHERE job_status IN ('scanning','extracting','embedding','indexing','cancel_requested'))::bigint,
                    COUNT(*) FILTER (WHERE job_status = 'awaiting_ocr')::bigint,
                    COUNT(*) FILTER (WHERE job_status IN ('failed','quarantined'))::bigint,
                    (SELECT COUNT(*)::bigint FROM project_intake_documents WHERE pulse_ai_processing_status = 'ready'),
                    (
                        SELECT COUNT(*)::bigint
                        FROM project_intake_documents d
                        WHERE d.is_active = TRUE
                          AND d.engineering_visible = TRUE
                          AND d.ai_timesheet_context_enabled = TRUE
                          AND d.project_id IS NOT NULL
                          AND LOWER(COALESCE(d.document_category, d.document_type, '')) IN (
                              'sow','statement_of_work','gsd','global_solution_design'
                          )
                          AND d.pulse_ai_processing_status = 'ready'
                          AND EXISTS (
                              SELECT 1
                              FROM pulse_ai_document_versions ready_version
                              WHERE ready_version.pulse_ai_document_version_id = d.pulse_ai_active_version_id
                                AND ready_version.authority_status IN ('approved','canonical')
                          )
                    ),
                    (
                        SELECT COUNT(*)::bigint
                        FROM project_intake_documents d
                        WHERE d.is_active = TRUE
                          AND d.engineering_visible = TRUE
                          AND d.ai_timesheet_context_enabled = TRUE
                          AND d.project_id IS NOT NULL
                          AND LOWER(COALESCE(d.document_category, d.document_type, '')) IN (
                              'sow','statement_of_work','gsd','global_solution_design'
                          )
                          AND (
                              d.pulse_ai_processing_status <> 'ready'
                              OR NOT EXISTS (
                                  SELECT 1
                                  FROM pulse_ai_document_versions ready_version
                                  WHERE ready_version.pulse_ai_document_version_id = d.pulse_ai_active_version_id
                                    AND ready_version.authority_status IN ('approved','canonical')
                              )
                          )
                    ),
                    (SELECT COUNT(*)::bigint FROM pulse_ai_document_chunks WHERE is_active = TRUE AND index_status IN ('lexical_ready','embedding_ready','ready')),
                    (SELECT COUNT(*)::bigint FROM pulse_ai_document_chunks WHERE is_active = TRUE AND embedding_status = 'ready');
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return RuntimeCounts.Empty;
            return new RuntimeCounts(
                Queued: reader.GetInt64(0),
                Running: reader.GetInt64(1),
                AwaitingOcr: reader.GetInt64(2),
                Failed: reader.GetInt64(3),
                ReadyDocuments: reader.GetInt64(4),
                ReadySowDocuments: reader.GetInt64(5),
                PendingSowDocuments: reader.GetInt64(6),
                ActiveChunks: reader.GetInt64(7),
                EmbeddedChunks: reader.GetInt64(8));
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return RuntimeCounts.Empty;
        }
    }

    /// <summary>
    /// Automatically admits one new, explicitly AI-eligible project document to
    /// the private queue. Enabling the worker is the deployment-level consent;
    /// cancelled, quarantined, failed, inactive, unlinked, or non-visible files
    /// are never silently requeued by this path.
    /// </summary>
    public async Task<AutoQueueResult?> EnqueueNextEligibleDocumentAsync(
        PulseAiPrivateRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured || !options.WorkerEnabled || !options.AutomaticQueueConfigured)
            return null;

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var correlationId = $"auto-{Guid.NewGuid():N}";
            const string insert = """
                WITH candidate AS (
                    SELECT
                        d.project_intake_document_id AS document_id,
                        d.project_id,
                        service_user.user_id AS actor_user_id
                    FROM project_intake_documents d
                    JOIN projects p ON p.project_id = d.project_id
                    JOIN app_users service_user
                      ON service_user.user_id = @service_principal_user_id
                     AND COALESCE(service_user.is_active, FALSE) = TRUE
                    WHERE d.is_active = TRUE
                      AND COALESCE(d.engineering_visible, FALSE) = TRUE
                      AND COALESCE(d.ai_timesheet_context_enabled, FALSE) = TRUE
                      AND COALESCE(d.pulse_ai_processing_status, 'not_requested') = 'not_requested'
                      AND EXISTS (
                          SELECT 1
                          FROM app_user_role_assignments service_assignment
                          JOIN app_roles service_role
                            ON service_role.app_role_id = service_assignment.app_role_id
                           AND service_role.is_active = TRUE
                          JOIN app_role_permissions service_role_permission
                            ON service_role_permission.app_role_id = service_role.app_role_id
                          JOIN app_permissions service_permission
                            ON service_permission.app_permission_id = service_role_permission.app_permission_id
                          WHERE service_assignment.user_id = service_user.user_id
                            AND service_assignment.is_active = TRUE
                            AND service_permission.permission_code = 'QUEUE_PULSE_AI_DOCUMENT_PROCESSING'
                      )
                      AND LOWER(COALESCE(d.document_category, d.document_type, '')) IN (
                          'sow','statement_of_work','gsd','global_solution_design',
                          'architecture','design','order','order_form','quote','proposal','supporting'
                      )
                      AND NOT EXISTS (
                          SELECT 1
                          FROM pulse_ai_document_processing_jobs existing
                          WHERE existing.project_intake_document_id = d.project_intake_document_id
                            AND existing.job_status IN (
                                'queued','scanning','extracting','awaiting_ocr','embedding',
                                'indexing','retry_wait','cancel_requested'
                            )
                      )
                    ORDER BY
                        CASE WHEN LOWER(COALESCE(d.document_category, d.document_type, '')) IN (
                            'sow','statement_of_work','gsd','global_solution_design'
                        ) THEN 0 ELSE 1 END,
                        d.uploaded_at,
                        d.project_intake_document_id
                    FOR UPDATE OF d SKIP LOCKED
                    LIMIT 1
                )
                INSERT INTO pulse_ai_document_processing_jobs (
                    project_intake_document_id, project_id, actual_user_id,
                    effective_user_id, requested_by_user_id, requested_purpose,
                    priority, maximum_attempts, job_status, correlation_id
                )
                SELECT
                    document_id, project_id, actor_user_id, actor_user_id,
                    actor_user_id, 'automatic_private_document_indexing',
                    90, @maximum_attempts, 'queued', @correlation_id
                FROM candidate
                RETURNING pulse_ai_document_processing_job_id,
                          project_intake_document_id, project_id,
                          actual_user_id, correlation_id;
                """;
            AutoQueueResult? queued;
            await using (var command = new NpgsqlCommand(insert, connection, transaction))
            {
                command.Parameters.AddWithValue("maximum_attempts", options.MaximumAttempts);
                command.Parameters.AddWithValue("correlation_id", correlationId);
                command.Parameters.AddWithValue("service_principal_user_id", options.DocumentServicePrincipalUserId!.Value);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                queued = await reader.ReadAsync(cancellationToken)
                    ? new AutoQueueResult(
                        JobId: reader.GetGuid(0),
                        DocumentId: reader.GetGuid(1),
                        ProjectId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                        ActorUserId: reader.GetGuid(3),
                        CorrelationId: reader.GetString(4))
                    : null;
            }

            if (queued is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await UpdateDocumentStatusAsync(
                connection,
                transaction,
                queued.DocumentId,
                "queued",
                string.Empty,
                cancellationToken);
            await InsertEventAsync(
                connection,
                transaction,
                queued.JobId,
                queued.DocumentId,
                queued.ProjectId,
                queued.ActorUserId,
                queued.ActorUserId,
                "document_automatically_queued",
                "queued",
                queued.CorrelationId,
                string.Empty,
                new
                {
                    admission = "worker_enabled_and_document_ai_context_eligible",
                    rawDocumentTextLogged = false,
                    externalProviderCalled = false
                },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return queued;
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703" or "23505")
        {
            return null;
        }
    }

    public async Task<PulseAiPrivateProcessingJob?> EnqueueAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiAuthorizedDocumentSource source,
        string purpose,
        int priority,
        int maximumAttempts,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string sql = """
                INSERT INTO pulse_ai_document_processing_jobs (
                    project_intake_document_id,
                    project_id,
                    actual_user_id,
                    effective_user_id,
                    requested_by_user_id,
                    requested_purpose,
                    priority,
                    maximum_attempts,
                    job_status,
                    correlation_id
                ) VALUES (
                    @document_id,
                    @project_id,
                    @actual_user_id,
                    @effective_user_id,
                    @requested_by_user_id,
                    @purpose,
                    @priority,
                    @maximum_attempts,
                    'queued',
                    @correlation_id
                )
                ON CONFLICT (project_intake_document_id)
                WHERE job_status IN (
                    'queued','scanning','extracting','awaiting_ocr',
                    'embedding','indexing','retry_wait','cancel_requested'
                ) DO NOTHING
                RETURNING pulse_ai_document_processing_job_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("document_id", source.DocumentId);
            command.Parameters.AddWithValue("project_id", source.ProjectId is null ? DBNull.Value : source.ProjectId.Value);
            command.Parameters.AddWithValue("actual_user_id", actualUserId);
            command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            command.Parameters.AddWithValue("requested_by_user_id", actualUserId);
            command.Parameters.AddWithValue("purpose", Clean(purpose, 80, "private_document_indexing"));
            command.Parameters.AddWithValue("priority", Math.Clamp(priority, 1, 100));
            command.Parameters.AddWithValue("maximum_attempts", Math.Clamp(maximumAttempts, 1, 20));
            command.Parameters.AddWithValue("correlation_id", Clean(correlationId, 160, Guid.NewGuid().ToString("N")));
            var jobValue = await command.ExecuteScalarAsync(cancellationToken);
            if (jobValue is not Guid jobId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            await UpdateDocumentStatusAsync(
                connection,
                transaction,
                source.DocumentId,
                "queued",
                string.Empty,
                cancellationToken);
            await InsertEventAsync(
                connection,
                transaction,
                jobId,
                source.DocumentId,
                source.ProjectId,
                actualUserId,
                effectiveUserId,
                "processing_job_queued",
                "requested",
                correlationId,
                string.Empty,
                new
                {
                    purpose = Clean(purpose, 80, "private_document_indexing"),
                    priority = Math.Clamp(priority, 1, 100),
                    rawDocumentTextLogged = false
                },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetJobAsync(jobId, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<PulseAiPrivateProcessingJob>> ListJobsAsync(
        RuntimeAccess access,
        string? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive || !access.CanViewRuntime) return [];
        limit = Math.Clamp(limit, 1, 500);
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    j.pulse_ai_document_processing_job_id,
                    j.project_intake_document_id,
                    j.project_id,
                    COALESCE(p.project_code, ''),
                    COALESCE(p.project_name, ''),
                    COALESCE(d.original_file_name, ''),
                    LOWER(COALESCE(d.document_category, d.document_type, 'other')),
                    j.actual_user_id,
                    j.effective_user_id,
                    j.requested_purpose,
                    j.priority,
                    j.job_status,
                    j.attempt_count,
                    j.maximum_attempts,
                    j.cancellation_requested,
                    j.correlation_id,
                    j.source_sha256,
                    j.extraction_method,
                    j.malware_scanner,
                    j.ocr_provider,
                    j.embedding_model,
                    j.embedding_dimension,
                    j.index_provider,
                    j.diagnostic_code,
                    j.diagnostic_message,
                    j.requested_at,
                    j.started_at,
                    j.completed_at,
                    j.updated_at
                FROM pulse_ai_document_processing_jobs j
                JOIN project_intake_documents d
                    ON d.project_intake_document_id = j.project_intake_document_id
                LEFT JOIN projects p ON p.project_id = j.project_id
                WHERE (@status = '' OR j.job_status = @status)
                  AND (
                    COALESCE(d.upload_source, '') <> 'celar_ai_chat_attachment'
                    OR (
                        d.uploaded_by_user_id = @user_id
                        AND j.actual_user_id = @user_id
                        AND j.effective_user_id = @user_id
                    )
                  )
                  AND (
                    @is_broad = TRUE
                    OR p.project_manager_user_id = @user_id
                    OR EXISTS (
                        SELECT 1 FROM project_assignments pa
                        WHERE pa.project_id = p.project_id AND pa.user_id = @user_id
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
                ORDER BY j.requested_at DESC
                LIMIT @limit;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("status", Clean(status, 40, string.Empty));
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("user_id", access.UserId);
            command.Parameters.AddWithValue("limit", limit);
            return await ReadJobsAsync(command, cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return [];
        }
    }

    public async Task<PulseAiPrivateProcessingJob?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(JobSelectSql("WHERE j.pulse_ai_document_processing_job_id = @job_id"), connection);
            command.Parameters.AddWithValue("job_id", jobId);
            return (await ReadJobsAsync(command, cancellationToken)).FirstOrDefault();
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return null;
        }
    }

    public async Task<bool> HasLiveSnapshotLeaseAsync(
        Guid jobId,
        Guid leaseToken,
        long leaseGeneration,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM pulse_ai_document_processing_jobs
                WHERE pulse_ai_document_processing_job_id = @job_id
                  AND lease_token = @lease_token
                  AND lease_generation = @lease_generation
                  AND job_status IN ('scanning','extracting','embedding','indexing','cancel_requested')
                  AND lease_expires_at > NOW()
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("lease_token", leaseToken);
        command.Parameters.AddWithValue("lease_generation", leaseGeneration);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Snapshot lease state was not returned."));
    }

    public async Task<PulseAiPrivateDocumentRuntimeState?> GetDocumentStateAsync(
        RuntimeAccess access,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive || !access.CanViewRuntime) return null;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    d.project_intake_document_id,
                    d.project_id,
                    COALESCE(p.project_code, ''),
                    COALESCE(p.project_name, ''),
                    d.original_file_name,
                    LOWER(COALESCE(d.document_category, d.document_type, 'other')),
                    d.pulse_ai_processing_status,
                    d.pulse_ai_classification,
                    d.pulse_ai_document_revision,
                    d.pulse_ai_effective_at,
                    d.pulse_ai_active_version_id,
                    d.pulse_ai_processing_error_code,
                    d.pulse_ai_processing_updated_at,
                    (SELECT COUNT(*)::int FROM pulse_ai_document_versions v WHERE v.project_intake_document_id = d.project_intake_document_id),
                    (SELECT COUNT(*)::int FROM pulse_ai_document_chunks c WHERE c.project_intake_document_id = d.project_intake_document_id AND c.is_active = TRUE),
                    (SELECT COUNT(*)::int FROM pulse_ai_document_chunks c WHERE c.project_intake_document_id = d.project_intake_document_id AND c.is_active = TRUE AND c.embedding_status = 'ready'),
                    (SELECT MAX(v.processed_at) FROM pulse_ai_document_versions v WHERE v.project_intake_document_id = d.project_intake_document_id),
                    COALESCE((
                        SELECT v.source_sha256
                        FROM pulse_ai_document_versions v
                        WHERE v.pulse_ai_document_version_id = d.pulse_ai_active_version_id
                    ), '')
                FROM project_intake_documents d
                LEFT JOIN projects p ON p.project_id = d.project_id
                WHERE d.project_intake_document_id = @document_id
                  AND d.is_active = TRUE
                  AND d.engineering_visible = TRUE
                  AND (
                    @is_broad = TRUE
                    OR p.project_manager_user_id = @user_id
                    OR EXISTS (
                        SELECT 1 FROM project_assignments pa
                        WHERE pa.project_id = p.project_id AND pa.user_id = @user_id
                    )
                  );
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("document_id", documentId);
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("user_id", access.UserId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            var state = new
            {
                DocumentId = reader.GetGuid(0),
                ProjectId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1),
                ProjectCode = reader.GetString(2),
                ProjectName = reader.GetString(3),
                OriginalFileName = reader.GetString(4),
                Category = reader.GetString(5),
                Status = reader.GetString(6),
                Classification = reader.GetString(7),
                Revision = reader.GetString(8),
                EffectiveAt = reader.IsDBNull(9) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(9),
                ActiveVersionId = reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10),
                ErrorCode = reader.GetString(11),
                UpdatedAt = reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
                VersionCount = reader.GetInt32(13),
                ChunkCount = reader.GetInt32(14),
                EmbeddedCount = reader.GetInt32(15),
                LastProcessedAt = reader.IsDBNull(16) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(16),
                ActiveVersionSourceSha256 = reader.GetString(17)
            };
            await reader.CloseAsync();
            var jobs = await ListJobsForDocumentAsync(connection, documentId, 20, cancellationToken);
            return new PulseAiPrivateDocumentRuntimeState(
                state.DocumentId,
                state.ProjectId,
                state.ProjectCode,
                state.ProjectName,
                state.OriginalFileName,
                state.Category,
                state.Status,
                state.Classification,
                state.Revision,
                state.EffectiveAt,
                state.ActiveVersionId,
                state.ErrorCode,
                state.UpdatedAt,
                state.VersionCount,
                state.ChunkCount,
                state.EmbeddedCount,
                state.LastProcessedAt,
                state.ActiveVersionSourceSha256,
                jobs);
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return null;
        }
    }

    public async Task<bool> ApproveActiveVersionAsync(
        RuntimeAccess access,
        Guid documentId,
        Guid versionId,
        Guid actorUserId,
        string expectedSourceSha256,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive || !access.CanApprove || actorUserId != access.UserId) return false;
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE pulse_ai_document_versions version
            SET authority_status = 'approved'
            FROM project_intake_documents document
            JOIN projects project ON project.project_id = document.project_id
            WHERE version.pulse_ai_document_version_id = @version_id
              AND version.project_intake_document_id = @document_id
              AND document.project_intake_document_id = version.project_intake_document_id
              AND document.is_active = TRUE
              AND document.engineering_visible = TRUE
              AND document.pulse_ai_processing_status = 'ready'
              AND document.pulse_ai_active_version_id = version.pulse_ai_document_version_id
              AND version.source_sha256 = @expected_source_sha256
              AND version.authority_status IN ('candidate','approved')
              AND (
                  @is_broad = TRUE
                  OR project.project_manager_user_id = @user_id
                  OR EXISTS (
                      SELECT 1
                      FROM project_assignments assignment
                      WHERE assignment.project_id = project.project_id
                        AND assignment.user_id = @user_id
                  )
              )
            RETURNING document.project_id;
            """;
        Guid? projectId;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("version_id", versionId);
            command.Parameters.AddWithValue("document_id", documentId);
            command.Parameters.AddWithValue("expected_source_sha256", expectedSourceSha256);
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("user_id", access.UserId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is not Guid resolvedProjectId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            projectId = resolvedProjectId;
        }

        await InsertEventAsync(
            connection,
            transaction,
            jobId: null,
            documentId: documentId,
            projectId: projectId,
            actualUserId: actorUserId,
            effectiveUserId: actorUserId,
            eventCode: "document_version_approved",
            eventStatus: "approved",
            correlationId: $"approval-{Guid.NewGuid():N}",
            diagnosticCode: string.Empty,
            evidence: new
            {
                versionId,
                sourceSha256 = expectedSourceSha256,
                authorityStatus = "approved",
                reason = Clean(reason, 1000, "Approved for permission-scoped Celar AI retrieval."),
                rawDocumentTextLogged = false,
                externalProviderCalled = false
            },
            cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RequestCancellationAsync(
        Guid jobId,
        Guid actorUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE pulse_ai_document_processing_jobs
            SET cancellation_requested = TRUE,
                job_status = CASE WHEN job_status IN ('queued','retry_wait','awaiting_ocr') THEN 'cancelled' ELSE 'cancel_requested' END,
                completed_at = CASE WHEN job_status IN ('queued','retry_wait','awaiting_ocr') THEN NOW() ELSE completed_at END,
                diagnostic_code = 'cancellation_requested',
                diagnostic_message = @reason
            WHERE pulse_ai_document_processing_job_id = @job_id
              AND job_status IN (
                'queued','scanning','extracting','awaiting_ocr','embedding','indexing','retry_wait'
              )
            RETURNING project_intake_document_id, project_id, actual_user_id, effective_user_id, correlation_id, job_status;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("reason", Clean(reason, 1000, "Cancellation requested by an authorized operator."));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        var documentId = reader.GetGuid(0);
        var projectId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
        var actualUserId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        var effectiveUserId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
        var correlationId = reader.GetString(4);
        var status = reader.GetString(5);
        await reader.CloseAsync();
        if (status == "cancelled")
        {
            await UpdateDocumentStatusAsync(connection, transaction, documentId, "cancelled", "cancellation_requested", cancellationToken);
        }
        await InsertEventAsync(
            connection,
            transaction,
            jobId,
            documentId,
            projectId,
            actorUserId,
            effectiveUserId,
            "processing_cancellation_requested",
            status == "cancelled" ? "cancelled" : "requested",
            correlationId,
            "cancellation_requested",
            new { reason = Clean(reason, 1000, "Cancellation requested by an authorized operator.") },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RetryAsync(
        Guid jobId,
        Guid actorUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE pulse_ai_document_processing_jobs
            SET job_status = 'queued',
                available_at = NOW(),
                attempt_count = CASE
                    WHEN attempt_count >= maximum_attempts THEN 0
                    ELSE attempt_count
                END,
                cancellation_requested = FALSE,
                completed_at = NULL,
                lease_owner = '',
                lease_token = NULL,
                lease_heartbeat_at = NULL,
                lease_expires_at = NULL,
                diagnostic_code = '',
                diagnostic_message = @reason
            WHERE pulse_ai_document_processing_job_id = @job_id
              AND job_status IN ('failed','quarantined','cancelled','awaiting_ocr','retry_wait')
            RETURNING project_intake_document_id, project_id, actual_user_id, effective_user_id, correlation_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("reason", Clean(reason, 1000, "Retry requested by an authorized operator."));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
        var documentId = reader.GetGuid(0);
        var projectId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1);
        var actualUserId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        var effectiveUserId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3);
        var correlationId = reader.GetString(4);
        await reader.CloseAsync();
        await UpdateDocumentStatusAsync(connection, transaction, documentId, "queued", string.Empty, cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            jobId,
            documentId,
            projectId,
            actorUserId,
            effectiveUserId,
            "processing_job_retried",
            "requested",
            correlationId,
            string.Empty,
            new { reason = Clean(reason, 1000, "Retry requested by an authorized operator.") },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<PulseAiPrivateProcessingJob?> ClaimNextAsync(
        PulseAiPrivateRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await RecoverExpiredLeasesAsync(connection, transaction, cancellationToken);
        const string selectSql = """
            SELECT pulse_ai_document_processing_job_id
            FROM pulse_ai_document_processing_jobs
            WHERE job_status IN ('queued','retry_wait')
              AND available_at <= NOW()
              AND cancellation_requested = FALSE
              AND attempt_count < maximum_attempts
              AND (lease_expires_at IS NULL OR lease_expires_at < NOW())
            ORDER BY priority DESC, requested_at
            FOR UPDATE SKIP LOCKED
            LIMIT 1;
            """;
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        var value = await select.ExecuteScalarAsync(cancellationToken);
        if (value is not Guid jobId)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        const string updateSql = """
            UPDATE pulse_ai_document_processing_jobs
            SET job_status = 'scanning',
                attempt_count = attempt_count + 1,
                started_at = COALESCE(started_at, NOW()),
                lease_owner = @lease_owner,
                lease_token = @lease_token,
                lease_generation = lease_generation + 1,
                lease_heartbeat_at = NOW(),
                lease_expires_at = NOW() + (@lease_seconds * INTERVAL '1 second'),
                diagnostic_code = '',
                diagnostic_message = ''
            WHERE pulse_ai_document_processing_job_id = @job_id;
            """;
        await using var update = new NpgsqlCommand(updateSql, connection, transaction);
        update.Parameters.AddWithValue("job_id", jobId);
        update.Parameters.AddWithValue("lease_owner", options.WorkerIdentity);
        update.Parameters.AddWithValue("lease_token", Guid.NewGuid());
        update.Parameters.AddWithValue("lease_seconds", options.LeaseSeconds);
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetJobAsync(jobId, cancellationToken);
    }

    private static async Task RecoverExpiredLeasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pulse_ai_document_processing_jobs
            SET job_status = CASE
                    WHEN cancellation_requested THEN 'cancelled'
                    WHEN attempt_count >= maximum_attempts THEN 'failed'
                    ELSE 'retry_wait'
                END,
                available_at = NOW(),
                completed_at = CASE
                    WHEN cancellation_requested OR attempt_count >= maximum_attempts THEN NOW()
                    ELSE NULL
                END,
                lease_owner = '',
                lease_token = NULL,
                lease_heartbeat_at = NULL,
                lease_expires_at = NULL,
                diagnostic_code = CASE
                    WHEN cancellation_requested THEN 'expired_lease_cancelled'
                    WHEN attempt_count >= maximum_attempts THEN 'expired_lease_attempts_exhausted'
                    ELSE 'expired_lease_requeued'
                END,
                diagnostic_message = 'The prior worker lease expired; recovery was applied without logging document content.'
            WHERE job_status IN ('scanning','extracting','embedding','indexing','cancel_requested')
              AND lease_expires_at IS NOT NULL
              AND lease_expires_at < NOW()
            RETURNING pulse_ai_document_processing_job_id,
                      project_intake_document_id, project_id,
                      actual_user_id, effective_user_id,
                      job_status, correlation_id, diagnostic_code;
            """;
        var recovered = new List<(Guid JobId, Guid DocumentId, Guid? ProjectId, Guid? ActualUserId, Guid? EffectiveUserId, string Status, string CorrelationId, string DiagnosticCode)>();
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                recovered.Add((
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7)));
            }
        }

        foreach (var item in recovered)
        {
            await UpdateDocumentStatusAsync(
                connection,
                transaction,
                item.DocumentId,
                item.Status,
                item.DiagnosticCode,
                cancellationToken);
            await InsertEventAsync(
                connection,
                transaction,
                item.JobId,
                item.DocumentId,
                item.ProjectId,
                item.ActualUserId,
                item.EffectiveUserId,
                "expired_worker_lease_recovered",
                item.Status,
                item.CorrelationId,
                item.DiagnosticCode,
                new
                {
                    recovery = "bounded_lease_recovery",
                    rawDocumentTextLogged = false,
                    externalProviderCalled = false
                },
                cancellationToken);
        }
    }

    public async Task<bool> RenewLeaseAsync(
        PulseAiPrivateProcessingJob job,
        int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        if (job.LeaseToken is null) return false;
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE pulse_ai_document_processing_jobs
            SET lease_heartbeat_at = NOW(),
                lease_expires_at = NOW() + (@lease_seconds * INTERVAL '1 second')
            WHERE pulse_ai_document_processing_job_id = @job_id
              AND lease_owner = @lease_owner
              AND lease_token = @lease_token
              AND lease_generation = @lease_generation
              AND lease_expires_at > NOW()
              AND job_status IN ('scanning','extracting','embedding','indexing','cancel_requested');
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("lease_owner", job.LeaseOwner);
        command.Parameters.AddWithValue("lease_token", job.LeaseToken.Value);
        command.Parameters.AddWithValue("lease_generation", job.LeaseGeneration);
        command.Parameters.AddWithValue("lease_seconds", leaseSeconds);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> CancellationRequestedAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT cancellation_requested OR job_status IN ('cancel_requested','cancelled')
            FROM pulse_ai_document_processing_jobs
            WHERE pulse_ai_document_processing_job_id = @job_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", jobId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task MarkStageAsync(
        PulseAiPrivateProcessingJob job,
        string jobStatus,
        string documentStatus,
        string eventCode,
        object evidence,
        int leaseSeconds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE pulse_ai_document_processing_jobs
            SET job_status = @job_status,
                lease_heartbeat_at = NOW(),
                lease_expires_at = NOW() + (@lease_seconds * INTERVAL '1 second')
            WHERE pulse_ai_document_processing_job_id = @job_id
              AND lease_owner = @lease_owner
              AND lease_token = @lease_token
              AND lease_generation = @lease_generation
              AND lease_expires_at > NOW();
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("job_status", jobStatus);
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("lease_owner", job.LeaseOwner);
        command.Parameters.AddWithValue("lease_token", job.LeaseToken is null ? DBNull.Value : job.LeaseToken.Value);
        command.Parameters.AddWithValue("lease_generation", job.LeaseGeneration);
        command.Parameters.AddWithValue("lease_seconds", Math.Clamp(leaseSeconds, 30, 3600));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The private document worker lost its fenced lease before changing stage.");
        await UpdateDocumentStatusAsync(connection, transaction, job.DocumentId, documentStatus, string.Empty, cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            job.JobId,
            job.DocumentId,
            job.ProjectId,
            job.ActualUserId,
            job.EffectiveUserId,
            eventCode,
            "succeeded",
            job.CorrelationId,
            string.Empty,
            evidence,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Guid> PersistProcessedDocumentAsync(
        PulseAiPrivateProcessingJob job,
        PulseAiAuthorizedDocumentSource source,
        PulseAiPrivateMalwareScanResult scan,
        PulseAiDocumentExtractionResult extraction,
        IReadOnlyList<PulseAiDocumentChunk> chunks,
        PulseAiPrivateEmbeddingResult embeddings,
        bool lexicalOnly,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var documentVersion = $"{source.OriginalFileName}@{source.UploadedAt:O}";
            const string versionSql = """
                INSERT INTO pulse_ai_document_versions (
                    project_intake_document_id,project_id,source_sha256,document_version,
                    document_revision,authority_status,classification,extraction_method,
                    extraction_contract_version,page_count,section_count,chunk_count,
                    character_count,estimated_token_count,ocr_used,malware_scanner,
                    malware_signature_version,embedding_model,embedding_dimension,
                    index_provider,index_status,effective_at,processed_by_job_id,processed_at
                ) VALUES (
                    @document_id,@project_id,@source_sha256,@document_version,
                    @document_revision,'candidate',@classification,@extraction_method,
                    @contract_version,@page_count,@section_count,@chunk_count,
                    @character_count,@token_count,@ocr_used,@malware_scanner,
                    @malware_signature_version,@embedding_model,@embedding_dimension,
                    @index_provider,@index_status,@effective_at,@job_id,NOW()
                )
                ON CONFLICT (project_intake_document_id, source_sha256) DO UPDATE
                SET project_id = EXCLUDED.project_id,
                    document_version = EXCLUDED.document_version,
                    document_revision = EXCLUDED.document_revision,
                    classification = EXCLUDED.classification,
                    extraction_method = EXCLUDED.extraction_method,
                    extraction_contract_version = EXCLUDED.extraction_contract_version,
                    page_count = EXCLUDED.page_count,
                    section_count = EXCLUDED.section_count,
                    chunk_count = EXCLUDED.chunk_count,
                    character_count = EXCLUDED.character_count,
                    estimated_token_count = EXCLUDED.estimated_token_count,
                    ocr_used = EXCLUDED.ocr_used,
                    malware_scanner = EXCLUDED.malware_scanner,
                    malware_signature_version = EXCLUDED.malware_signature_version,
                    embedding_model = EXCLUDED.embedding_model,
                    embedding_dimension = EXCLUDED.embedding_dimension,
                    index_provider = EXCLUDED.index_provider,
                    index_status = EXCLUDED.index_status,
                    effective_at = EXCLUDED.effective_at,
                    processed_by_job_id = EXCLUDED.processed_by_job_id,
                    processed_at = NOW()
                RETURNING pulse_ai_document_version_id;
                """;
            await using var versionCommand = new NpgsqlCommand(versionSql, connection, transaction);
            versionCommand.Parameters.AddWithValue("document_id", source.DocumentId);
            versionCommand.Parameters.AddWithValue("project_id", source.ProjectId is null ? DBNull.Value : source.ProjectId.Value);
            versionCommand.Parameters.AddWithValue("source_sha256", extraction.SourceSha256);
            versionCommand.Parameters.AddWithValue("document_version", documentVersion);
            versionCommand.Parameters.AddWithValue("document_revision", string.Empty);
            versionCommand.Parameters.AddWithValue("classification", source.Classification);
            versionCommand.Parameters.AddWithValue("extraction_method", extraction.ExtractionMethod);
            versionCommand.Parameters.AddWithValue("contract_version", PulseAiPrivateDocumentPipelinePolicy.ContractVersion);
            versionCommand.Parameters.AddWithValue("page_count", extraction.PageCount);
            versionCommand.Parameters.AddWithValue("section_count", extraction.SectionCount);
            versionCommand.Parameters.AddWithValue("chunk_count", chunks.Count);
            versionCommand.Parameters.AddWithValue("character_count", extraction.CharacterCount);
            versionCommand.Parameters.AddWithValue("token_count", extraction.EstimatedTokenCount);
            versionCommand.Parameters.AddWithValue("ocr_used", extraction.ExtractionMethod.Contains("ocr", StringComparison.OrdinalIgnoreCase));
            versionCommand.Parameters.AddWithValue("malware_scanner", scan.Scanner);
            versionCommand.Parameters.AddWithValue("malware_signature_version", scan.SignatureVersion);
            versionCommand.Parameters.AddWithValue("embedding_model", embeddings.Succeeded ? embeddings.Model : string.Empty);
            versionCommand.Parameters.AddWithValue("embedding_dimension", embeddings.Succeeded ? embeddings.Dimension : DBNull.Value);
            versionCommand.Parameters.AddWithValue("index_provider", PulseAiPrivateRuntimePolicy.IndexProvider);
            versionCommand.Parameters.AddWithValue("index_status", embeddings.Succeeded ? "embedding_ready" : "lexical_ready");
            versionCommand.Parameters.AddWithValue("effective_at", source.UploadedAt);
            versionCommand.Parameters.AddWithValue("job_id", job.JobId);
            var versionId = (Guid)(await versionCommand.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Pulse AI document version was not returned."));

            await using (var deleteSections = new NpgsqlCommand(
                "DELETE FROM pulse_ai_document_sections WHERE pulse_ai_document_version_id = @version_id;",
                connection,
                transaction))
            {
                deleteSections.Parameters.AddWithValue("version_id", versionId);
                await deleteSections.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var deactivateChunks = new NpgsqlCommand(
                "UPDATE pulse_ai_document_chunks SET is_active = FALSE, index_status = 'inactive' WHERE project_intake_document_id = @document_id;",
                connection,
                transaction))
            {
                deactivateChunks.Parameters.AddWithValue("document_id", source.DocumentId);
                await deactivateChunks.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var deleteCurrentChunks = new NpgsqlCommand(
                "DELETE FROM pulse_ai_document_chunks WHERE pulse_ai_document_version_id = @version_id;",
                connection,
                transaction))
            {
                deleteCurrentChunks.Parameters.AddWithValue("version_id", versionId);
                await deleteCurrentChunks.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var section in extraction.Sections)
            {
                const string sectionSql = """
                    INSERT INTO pulse_ai_document_sections (
                        pulse_ai_document_version_id,project_intake_document_id,
                        section_index,citation_anchor,section_title,page_number,
                        sheet_name,section_text,character_count,text_sha256
                    ) VALUES (
                        @version_id,@document_id,@section_index,@citation_anchor,
                        @section_title,@page_number,@sheet_name,@section_text,
                        @character_count,@text_sha256
                    );
                    """;
                await using var sectionCommand = new NpgsqlCommand(sectionSql, connection, transaction);
                sectionCommand.Parameters.AddWithValue("version_id", versionId);
                sectionCommand.Parameters.AddWithValue("document_id", source.DocumentId);
                sectionCommand.Parameters.AddWithValue("section_index", section.SectionIndex);
                sectionCommand.Parameters.AddWithValue("citation_anchor", section.Anchor);
                sectionCommand.Parameters.AddWithValue("section_title", section.Title);
                sectionCommand.Parameters.AddWithValue("page_number", section.PageNumber is null ? DBNull.Value : section.PageNumber.Value);
                sectionCommand.Parameters.AddWithValue("sheet_name", section.SheetName is null ? DBNull.Value : section.SheetName);
                sectionCommand.Parameters.AddWithValue("section_text", section.Text);
                sectionCommand.Parameters.AddWithValue("character_count", section.CharacterCount);
                sectionCommand.Parameters.AddWithValue("text_sha256", section.TextSha256);
                await sectionCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var authorizationSnapshot = JsonSerializer.Serialize(new
            {
                source.AccessScope,
                source.RoleCodes,
                reauthorizationRequiredAtRetrieval = true,
                snapshotIsNotAuthorizationAuthority = true
            });
            for (var index = 0; index < chunks.Count; index++)
            {
                var chunk = chunks[index];
                var embedding = embeddings.Succeeded ? embeddings.Vectors[index] : null;
                const string chunkSql = """
                    INSERT INTO pulse_ai_document_chunks (
                        chunk_id,pulse_ai_document_version_id,project_intake_document_id,
                        project_id,project_code,project_name,customer_name,document_category,
                        document_version,classification,engineering_visible,
                        ai_timesheet_context_enabled,access_scope,authorization_snapshot_json,
                        chunk_index,citation_anchor,section_title,page_number,sheet_name,
                        chunk_text,source_sha256,text_sha256,character_count,
                        estimated_token_count,embedding,embedding_dimension,embedding_model,
                        embedding_status,index_status,is_active,processed_at
                    ) VALUES (
                        @chunk_id,@version_id,@document_id,@project_id,@project_code,
                        @project_name,@customer_name,@document_category,@document_version,
                        @classification,@engineering_visible,@timesheet_enabled,@access_scope,
                        @authorization_snapshot::jsonb,@chunk_index,@citation_anchor,
                        @section_title,@page_number,@sheet_name,@chunk_text,@source_sha256,
                        @text_sha256,@character_count,@token_count,@embedding,
                        @embedding_dimension,@embedding_model,@embedding_status,
                        @index_status,TRUE,NOW()
                    );
                    """;
                await using var chunkCommand = new NpgsqlCommand(chunkSql, connection, transaction);
                chunkCommand.Parameters.AddWithValue("chunk_id", chunk.ChunkId);
                chunkCommand.Parameters.AddWithValue("version_id", versionId);
                chunkCommand.Parameters.AddWithValue("document_id", source.DocumentId);
                chunkCommand.Parameters.AddWithValue("project_id", source.ProjectId is null ? DBNull.Value : source.ProjectId.Value);
                chunkCommand.Parameters.AddWithValue("project_code", source.ProjectCode);
                chunkCommand.Parameters.AddWithValue("project_name", source.ProjectName);
                chunkCommand.Parameters.AddWithValue("customer_name", source.CustomerName);
                chunkCommand.Parameters.AddWithValue("document_category", source.DocumentCategory);
                chunkCommand.Parameters.AddWithValue("document_version", documentVersion);
                chunkCommand.Parameters.AddWithValue("classification", source.Classification);
                chunkCommand.Parameters.AddWithValue("engineering_visible", source.EngineeringVisible);
                chunkCommand.Parameters.AddWithValue("timesheet_enabled", source.AiTimesheetContextEnabled);
                chunkCommand.Parameters.AddWithValue("access_scope", source.AccessScope);
                chunkCommand.Parameters.AddWithValue("authorization_snapshot", authorizationSnapshot);
                chunkCommand.Parameters.AddWithValue("chunk_index", chunk.ChunkIndex);
                chunkCommand.Parameters.AddWithValue("citation_anchor", chunk.Anchor);
                chunkCommand.Parameters.AddWithValue("section_title", chunk.Title);
                chunkCommand.Parameters.AddWithValue("page_number", chunk.PageNumber is null ? DBNull.Value : chunk.PageNumber.Value);
                chunkCommand.Parameters.AddWithValue("sheet_name", chunk.SheetName is null ? DBNull.Value : chunk.SheetName);
                chunkCommand.Parameters.AddWithValue("chunk_text", chunk.Text);
                chunkCommand.Parameters.AddWithValue("source_sha256", chunk.SourceSha256);
                chunkCommand.Parameters.AddWithValue("text_sha256", chunk.TextSha256);
                chunkCommand.Parameters.AddWithValue("character_count", chunk.CharacterCount);
                chunkCommand.Parameters.AddWithValue("token_count", chunk.EstimatedTokenCount);
                var embeddingParameter = chunkCommand.Parameters.Add(
                    "embedding",
                    NpgsqlDbType.Array | NpgsqlDbType.Double);
                embeddingParameter.Value = embedding is null ? DBNull.Value : embedding;
                chunkCommand.Parameters.AddWithValue("embedding_dimension", embedding is null ? DBNull.Value : embedding.Length);
                chunkCommand.Parameters.AddWithValue("embedding_model", embedding is null ? string.Empty : embeddings.Model);
                chunkCommand.Parameters.AddWithValue("embedding_status", embedding is null ? "not_requested" : "ready");
                chunkCommand.Parameters.AddWithValue("index_status", embedding is null ? "lexical_ready" : "embedding_ready");
                await chunkCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            const string documentSql = """
                UPDATE project_intake_documents
                SET pulse_ai_active_version_id = @version_id,
                    pulse_ai_processing_status = 'ready',
                    pulse_ai_classification = @classification,
                    pulse_ai_processing_error_code = '',
                    pulse_ai_processing_updated_at = NOW(),
                    extraction_status = CASE WHEN @embedding_ready THEN 'indexed' ELSE 'processed' END,
                    ai_context_last_processed_at = NOW()
                WHERE project_intake_document_id = @document_id;
                """;
            await using var documentCommand = new NpgsqlCommand(documentSql, connection, transaction);
            documentCommand.Parameters.AddWithValue("version_id", versionId);
            documentCommand.Parameters.AddWithValue("classification", source.Classification);
            documentCommand.Parameters.AddWithValue("embedding_ready", embeddings.Succeeded);
            documentCommand.Parameters.AddWithValue("document_id", source.DocumentId);
            await documentCommand.ExecuteNonQueryAsync(cancellationToken);

            const string jobSql = """
                UPDATE pulse_ai_document_processing_jobs
                SET job_status = 'succeeded',
                    completed_at = NOW(),
                    lease_owner = '',
                    lease_token = NULL,
                    lease_heartbeat_at = NULL,
                    lease_expires_at = NULL,
                    source_sha256 = @source_sha256,
                    extraction_method = @extraction_method,
                    malware_scanner = @malware_scanner,
                    malware_signature_version = @malware_signature_version,
                    ocr_provider = @ocr_provider,
                    embedding_model = @embedding_model,
                    embedding_dimension = @embedding_dimension,
                    index_provider = @index_provider,
                    diagnostic_code = '',
                    diagnostic_message = '',
                    metrics_json = @metrics::jsonb
                WHERE pulse_ai_document_processing_job_id = @job_id
                  AND lease_owner = @lease_owner
                  AND lease_token = @lease_token
                  AND lease_generation = @lease_generation
                  AND lease_expires_at > NOW();
                """;
            await using var jobCommand = new NpgsqlCommand(jobSql, connection, transaction);
            jobCommand.Parameters.AddWithValue("job_id", job.JobId);
            jobCommand.Parameters.AddWithValue("lease_owner", job.LeaseOwner);
            jobCommand.Parameters.AddWithValue("lease_token", job.LeaseToken is null ? DBNull.Value : job.LeaseToken.Value);
            jobCommand.Parameters.AddWithValue("lease_generation", job.LeaseGeneration);
            jobCommand.Parameters.AddWithValue("source_sha256", extraction.SourceSha256);
            jobCommand.Parameters.AddWithValue("extraction_method", extraction.ExtractionMethod);
            jobCommand.Parameters.AddWithValue("malware_scanner", scan.Scanner);
            jobCommand.Parameters.AddWithValue("malware_signature_version", scan.SignatureVersion);
            jobCommand.Parameters.AddWithValue("ocr_provider", extraction.ExtractionMethod.Contains("ocr", StringComparison.OrdinalIgnoreCase) ? "private_ocr" : string.Empty);
            jobCommand.Parameters.AddWithValue("embedding_model", embeddings.Succeeded ? embeddings.Model : string.Empty);
            jobCommand.Parameters.AddWithValue("embedding_dimension", embeddings.Succeeded ? embeddings.Dimension : DBNull.Value);
            jobCommand.Parameters.AddWithValue("index_provider", PulseAiPrivateRuntimePolicy.IndexProvider);
            jobCommand.Parameters.AddWithValue("metrics", JsonSerializer.Serialize(new
            {
                extraction.PageCount,
                extraction.SectionCount,
                extraction.CharacterCount,
                extraction.EstimatedTokenCount,
                chunkCount = chunks.Count,
                embeddedChunkCount = embeddings.Succeeded ? embeddings.Vectors.Count : 0,
                lexicalOnly,
                rawTextLogged = false,
                externalProviderCalled = false
            }));
            if (await jobCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("The private document worker lost its fenced lease before publishing the index.");

            await InsertEventAsync(
                connection,
                transaction,
                job.JobId,
                source.DocumentId,
                source.ProjectId,
                job.ActualUserId,
                job.EffectiveUserId,
                "document_processing_completed",
                embeddings.Succeeded ? "succeeded" : "partial",
                job.CorrelationId,
                string.Empty,
                new
                {
                    versionId,
                    extraction.PageCount,
                    extraction.SectionCount,
                    chunkCount = chunks.Count,
                    embeddedChunkCount = embeddings.Succeeded ? embeddings.Vectors.Count : 0,
                    lexicalOnly,
                    rawTextLogged = false,
                    externalProviderCalled = false
                },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return versionId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task CompleteTerminalAsync(
        PulseAiPrivateProcessingJob job,
        string jobStatus,
        string documentStatus,
        string eventCode,
        string eventStatus,
        string diagnosticCode,
        string diagnosticMessage,
        object evidence,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE pulse_ai_document_processing_jobs
            SET job_status = @job_status,
                completed_at = CASE WHEN @terminal THEN NOW() ELSE completed_at END,
                available_at = CASE WHEN @job_status = 'retry_wait' THEN NOW() + INTERVAL '15 minutes' ELSE available_at END,
                lease_owner = '',
                lease_token = NULL,
                lease_heartbeat_at = NULL,
                lease_expires_at = NULL,
                diagnostic_code = @diagnostic_code,
                diagnostic_message = @diagnostic_message
            WHERE pulse_ai_document_processing_job_id = @job_id
              AND lease_owner = @lease_owner
              AND lease_token = @lease_token
              AND lease_generation = @lease_generation
              AND lease_expires_at > NOW();
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("lease_owner", job.LeaseOwner);
        command.Parameters.AddWithValue("lease_token", job.LeaseToken is null ? DBNull.Value : job.LeaseToken.Value);
        command.Parameters.AddWithValue("lease_generation", job.LeaseGeneration);
        command.Parameters.AddWithValue("job_status", jobStatus);
        command.Parameters.AddWithValue("terminal", jobStatus is "failed" or "quarantined" or "cancelled");
        command.Parameters.AddWithValue("diagnostic_code", Clean(diagnosticCode, 120, string.Empty));
        command.Parameters.AddWithValue("diagnostic_message", Clean(diagnosticMessage, 2000, string.Empty));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The private document worker lost its fenced lease before completing the job.");
        await UpdateDocumentStatusAsync(
            connection,
            transaction,
            job.DocumentId,
            documentStatus,
            diagnosticCode,
            cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            job.JobId,
            job.DocumentId,
            job.ProjectId,
            job.ActualUserId,
            job.EffectiveUserId,
            eventCode,
            eventStatus,
            job.CorrelationId,
            diagnosticCode,
            evidence,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<PulseAiPrivateProcessingJob>> ListJobsForDocumentAsync(
        NpgsqlConnection connection,
        Guid documentId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            JobSelectSql("WHERE j.project_intake_document_id = @document_id ORDER BY j.requested_at DESC LIMIT @limit"),
            connection);
        command.Parameters.AddWithValue("document_id", documentId);
        command.Parameters.AddWithValue("limit", limit);
        return await ReadJobsAsync(command, cancellationToken);
    }

    private static string JobSelectSql(string predicate) => $"""
        SELECT
            j.pulse_ai_document_processing_job_id,
            j.project_intake_document_id,
            j.project_id,
            COALESCE(p.project_code, ''),
            COALESCE(p.project_name, ''),
            COALESCE(d.original_file_name, ''),
            LOWER(COALESCE(d.document_category, d.document_type, 'other')),
            j.actual_user_id,
            j.effective_user_id,
            j.requested_purpose,
            j.priority,
            j.job_status,
            j.attempt_count,
            j.maximum_attempts,
            j.cancellation_requested,
            j.lease_owner,
            j.lease_token,
            j.lease_generation,
            j.lease_expires_at,
            j.correlation_id,
            j.source_sha256,
            j.extraction_method,
            j.malware_scanner,
            j.ocr_provider,
            j.embedding_model,
            j.embedding_dimension,
            j.index_provider,
            j.diagnostic_code,
            j.diagnostic_message,
            j.requested_at,
            j.started_at,
            j.completed_at,
            j.updated_at
        FROM pulse_ai_document_processing_jobs j
        JOIN project_intake_documents d
            ON d.project_intake_document_id = j.project_intake_document_id
        LEFT JOIN projects p ON p.project_id = j.project_id
        {predicate};
        """;

    private static async Task<IReadOnlyList<PulseAiPrivateProcessingJob>> ReadJobsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var jobs = new List<PulseAiPrivateProcessingJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(new PulseAiPrivateProcessingJob(
                JobId: reader.GetGuid(0),
                DocumentId: reader.GetGuid(1),
                ProjectId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                ProjectCode: reader.GetString(3),
                ProjectName: reader.GetString(4),
                OriginalFileName: reader.GetString(5),
                DocumentCategory: reader.GetString(6),
                ActualUserId: reader.IsDBNull(7) ? null : reader.GetGuid(7),
                EffectiveUserId: reader.IsDBNull(8) ? null : reader.GetGuid(8),
                RequestedPurpose: reader.GetString(9),
                Priority: reader.GetInt16(10),
                Status: reader.GetString(11),
                AttemptCount: reader.GetInt32(12),
                MaximumAttempts: reader.GetInt32(13),
                CancellationRequested: reader.GetBoolean(14),
                LeaseOwner: reader.GetString(15),
                LeaseToken: reader.IsDBNull(16) ? null : reader.GetGuid(16),
                LeaseGeneration: reader.GetInt64(17),
                LeaseExpiresAt: reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18),
                CorrelationId: reader.GetString(19),
                SourceSha256: reader.GetString(20),
                ExtractionMethod: reader.GetString(21),
                MalwareScanner: reader.GetString(22),
                OcrProvider: reader.GetString(23),
                EmbeddingModel: reader.GetString(24),
                EmbeddingDimension: reader.IsDBNull(25) ? null : reader.GetInt32(25),
                IndexProvider: reader.GetString(26),
                DiagnosticCode: reader.GetString(27),
                DiagnosticMessage: reader.GetString(28),
                RequestedAt: reader.GetFieldValue<DateTimeOffset>(29),
                StartedAt: reader.IsDBNull(30) ? null : reader.GetFieldValue<DateTimeOffset>(30),
                CompletedAt: reader.IsDBNull(31) ? null : reader.GetFieldValue<DateTimeOffset>(31),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(32)));
        }
        return jobs;
    }

    private static async Task UpdateDocumentStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid documentId,
        string status,
        string errorCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE project_intake_documents
            SET pulse_ai_processing_status = @status,
                pulse_ai_processing_error_code = @error_code,
                pulse_ai_processing_updated_at = NOW()
            WHERE project_intake_document_id = @document_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("document_id", documentId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("error_code", Clean(errorCode, 120, string.Empty));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? jobId,
        Guid? documentId,
        Guid? projectId,
        Guid? actualUserId,
        Guid? effectiveUserId,
        string eventCode,
        string eventStatus,
        string correlationId,
        string diagnosticCode,
        object evidence,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pulse_ai_document_processing_events (
                pulse_ai_document_processing_job_id,project_intake_document_id,
                project_id,actual_user_id,effective_user_id,event_code,event_status,
                correlation_id,diagnostic_code,evidence_json
            ) VALUES (
                @job_id,@document_id,@project_id,@actual_user_id,@effective_user_id,
                @event_code,@event_status,@correlation_id,@diagnostic_code,@evidence::jsonb
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("job_id", jobId is null ? DBNull.Value : jobId.Value);
        command.Parameters.AddWithValue("document_id", documentId is null ? DBNull.Value : documentId.Value);
        command.Parameters.AddWithValue("project_id", projectId is null ? DBNull.Value : projectId.Value);
        command.Parameters.AddWithValue("actual_user_id", actualUserId is null ? DBNull.Value : actualUserId.Value);
        command.Parameters.AddWithValue("effective_user_id", effectiveUserId is null ? DBNull.Value : effectiveUserId.Value);
        command.Parameters.AddWithValue("event_code", Clean(eventCode, 120, "runtime_event"));
        command.Parameters.AddWithValue("event_status", eventStatus);
        command.Parameters.AddWithValue("correlation_id", Clean(correlationId, 160, string.Empty));
        command.Parameters.AddWithValue("diagnostic_code", Clean(diagnosticCode, 120, string.Empty));
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(evidence));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlySet<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> MissingDatabaseConfiguration()
    {
        try { return ProjectPulseAiDatabaseConnection.Resolve() is null ? ["ProjectPulse AI database connection"] : []; }
        catch (InvalidOperationException exception) { return [exception.Message]; }
    }

    private static string ConnectionString() =>
        ProjectPulseAiDatabaseConnection.Resolve()
        ?? throw new InvalidOperationException("ProjectPulse AI database configuration is unavailable.");

    private static string Clean(string? value, int maximumLength, string fallback)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) return fallback;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        OperationCanceledException => "cancelled",
        _ => "private_runtime_repository_failure"
    };

    public sealed record RuntimeSchemaState(
        bool MigrationApplied,
        bool RagMigrationApplied,
        bool RoutingMigrationApplied,
        bool HardeningMigrationApplied,
        bool Jobs,
        bool Versions,
        bool Sections,
        bool Chunks,
        bool Events,
        bool LexicalIndex)
    {
        public bool Complete => MigrationApplied && Jobs && Versions && Sections && Chunks && Events;
        public bool ProductionMigrationsApplied => MigrationApplied && RagMigrationApplied && RoutingMigrationApplied && HardeningMigrationApplied;
        public static RuntimeSchemaState Missing => new(false, false, false, false, false, false, false, false, false, false);
    }

    public sealed record RuntimeCounts(
        long Queued,
        long Running,
        long AwaitingOcr,
        long Failed,
        long ReadyDocuments,
        long ReadySowDocuments,
        long PendingSowDocuments,
        long ActiveChunks,
        long EmbeddedChunks)
    {
        public static RuntimeCounts Empty => new(0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    public sealed record AutoQueueResult(
        Guid JobId,
        Guid DocumentId,
        Guid? ProjectId,
        Guid ActorUserId,
        string CorrelationId);

    public sealed record DocumentServicePrincipalReadiness(
        bool Configured,
        bool Exists,
        bool Active,
        bool QueuePermissionGranted,
        string DiagnosticCode)
    {
        public bool Authorized => Configured && Exists && Active && QueuePermissionGranted;

        public static DocumentServicePrincipalReadiness NotConfigured =>
            new(false, false, false, false, "service_principal_not_configured");

        public static DocumentServicePrincipalReadiness DatabaseUnavailable =>
            new(true, false, false, false, "database_unavailable");

        public static DocumentServicePrincipalReadiness LookupUnavailable =>
            new(true, false, false, false, "service_principal_lookup_unavailable");
    }

    public sealed record RuntimeAccess(
        Guid UserId,
        bool IsActive,
        IReadOnlySet<string> RoleCodes,
        IReadOnlySet<string> PermissionCodes)
    {
        public bool IsSuperAdministrator => RoleCodes.Contains("SUPER_ADMINISTRATOR");
        public bool IsBroadScope => RoleCodes.Overlaps(BroadRoles);
        public bool CanViewRuntime => IsSuperAdministrator || PermissionCodes.Contains("VIEW_PULSE_AI_DOCUMENT_RUNTIME");
        public bool CanQueue => IsSuperAdministrator || PermissionCodes.Contains("QUEUE_PULSE_AI_DOCUMENT_PROCESSING");
        public bool CanCancel => IsSuperAdministrator || PermissionCodes.Contains("CANCEL_PULSE_AI_DOCUMENT_PROCESSING");
        public bool CanRetry => IsSuperAdministrator || PermissionCodes.Contains("RETRY_PULSE_AI_DOCUMENT_PROCESSING");
        public bool CanApprove => IsSuperAdministrator || PermissionCodes.Contains("APPROVE_PULSE_AI_DOCUMENT_VERSION");
        public static RuntimeAccess Empty(Guid userId) =>
            new(
                userId,
                false,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }
}
