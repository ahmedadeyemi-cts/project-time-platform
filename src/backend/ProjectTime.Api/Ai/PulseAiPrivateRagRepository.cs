using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateRagRepository
{
    private readonly ILogger<PulseAiPrivateRagRepository> _logger;

    public PulseAiPrivateRagRepository(ILogger<PulseAiPrivateRagRepository> logger)
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
                    EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id = @migration_id)
                    AND to_regclass('public.pulse_ai_answer_runs') IS NOT NULL
                    AND to_regclass('public.pulse_ai_answer_citations') IS NOT NULL
                    AND to_regclass('public.pulse_ai_answer_feedback') IS NOT NULL
                    AND to_regclass('public.pulse_ai_retrieval_events') IS NOT NULL
                    AND to_regclass('public.pulse_ai_document_chunks') IS NOT NULL;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("migration_id", PulseAiPrivateRagPolicy.MigrationId);
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI private RAG schema readiness failed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return false;
        }
    }

    public async Task<PulseAiPrivateRagAccess> LoadAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseConfigured) return PulseAiPrivateRagAccess.Empty(userId);
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
            if (!await reader.ReadAsync(cancellationToken)) return PulseAiPrivateRagAccess.Empty(userId);
            return new PulseAiPrivateRagAccess(
                UserId: reader.GetGuid(0),
                IsActive: reader.GetBoolean(1),
                RoleCodes: Split(reader.GetString(2)),
                PermissionCodes: Split(reader.GetString(3)));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI private RAG access resolution failed. UserId={UserId} Diagnostic={Diagnostic}",
                userId,
                Diagnostic(exception));
            return PulseAiPrivateRagAccess.Empty(userId);
        }
    }

    public async Task<ProjectResolution?> ResolveProjectAsync(
        PulseAiPrivateRagAccess access,
        Guid? projectId,
        Guid? taskId,
        Guid? assignmentId,
        string? projectCode,
        string? projectName,
        CancellationToken cancellationToken = default)
    {
        var code = Clean(projectCode, 120);
        var name = Clean(projectName, 300);
        if (projectId is null
            && taskId is null
            && assignmentId is null
            && code.Length == 0
            && name.Length == 0) return null;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    p.project_id,
                    p.project_code,
                    p.project_name,
                    COALESCE(c.client_name, 'No customer')
                FROM projects p
                LEFT JOIN clients c ON c.client_id = p.client_id
                WHERE (
                    (
                        (@project_id IS NOT NULL OR @task_id IS NOT NULL OR @assignment_id IS NOT NULL)
                        AND (@project_id IS NULL OR p.project_id = @project_id)
                        AND (
                            @task_id IS NULL
                            OR EXISTS (
                                SELECT 1
                                FROM project_tasks identity_task
                                WHERE identity_task.task_id = @task_id
                                  AND identity_task.project_id = p.project_id
                                  AND identity_task.is_active = TRUE
                            )
                        )
                        AND (
                            @assignment_id IS NULL
                            OR EXISTS (
                                SELECT 1
                                FROM project_assignments identity_assignment
                                WHERE identity_assignment.project_assignment_id = @assignment_id
                                  AND identity_assignment.project_id = p.project_id
                                  AND (@task_id IS NULL OR identity_assignment.task_id = @task_id)
                            )
                        )
                    )
                    OR (
                        @project_id IS NULL
                        AND @task_id IS NULL
                        AND @assignment_id IS NULL
                        AND (
                            (@project_code <> '' AND (LOWER(p.project_code) = LOWER(@project_code) OR p.project_id = projectpulse_resolve_project_id(@project_code)))
                            OR (@project_name <> '' AND LOWER(p.project_name) = LOWER(@project_name))
                        )
                    )
                )
                  AND (
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
                ORDER BY
                    CASE
                        WHEN @project_id IS NOT NULL AND p.project_id = @project_id THEN 0
                        WHEN @assignment_id IS NOT NULL THEN 1
                        WHEN @task_id IS NOT NULL THEN 2
                        WHEN @project_code <> '' AND (LOWER(p.project_code) = LOWER(@project_code) OR p.project_id = projectpulse_resolve_project_id(@project_code)) THEN 3
                        ELSE 4
                    END,
                    p.updated_at DESC
                LIMIT 2;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add("project_id", NpgsqlDbType.Uuid).Value =
                projectId is Guid canonicalProjectId ? canonicalProjectId : DBNull.Value;
            command.Parameters.Add("task_id", NpgsqlDbType.Uuid).Value =
                taskId is Guid canonicalTaskId ? canonicalTaskId : DBNull.Value;
            command.Parameters.Add("assignment_id", NpgsqlDbType.Uuid).Value =
                assignmentId is Guid canonicalAssignmentId ? canonicalAssignmentId : DBNull.Value;
            command.Parameters.AddWithValue("project_code", code);
            command.Parameters.AddWithValue("project_name", name);
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("is_pm_lead", access.IsProjectManagementLead);
            command.Parameters.AddWithValue("user_id", access.UserId);
            var projects = new List<ProjectResolution>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                projects.Add(new ProjectResolution(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
            return projects.Count == 1 ? projects[0] : null;
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return null;
        }
    }

    public async Task<RetrievalRows> RetrieveAsync(
        PulseAiPrivateRagAccess access,
        PulseAiPrivateRetrievalQuery query,
        double[]? queryEmbedding,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive) return RetrievalRows.Empty;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            var categories = query.AllowedDocumentCategories
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var hasEmbedding = queryEmbedding is { Length: > 0 };

            const string sql = """
                WITH authorized_candidates AS (
                    SELECT
                        ch.chunk_id,
                        ch.pulse_ai_document_version_id,
                        ch.project_intake_document_id,
                        ch.project_id,
                        ch.project_code,
                        ch.project_name,
                        ch.customer_name,
                        ch.document_category,
                        ch.document_version,
                        ch.classification,
                        d.original_file_name,
                        ch.citation_anchor,
                        ch.page_number,
                        ch.sheet_name,
                        ch.section_title,
                        ch.chunk_text,
                        ch.source_sha256,
                        ch.text_sha256,
                        ch.embedding,
                        ch.embedding_dimension,
                        ch.search_vector,
                        ch.processed_at
                    FROM pulse_ai_document_chunks ch
                    JOIN project_intake_documents d
                      ON d.project_intake_document_id = ch.project_intake_document_id
                    JOIN pulse_ai_document_versions v
                      ON v.pulse_ai_document_version_id = ch.pulse_ai_document_version_id
                    JOIN projects p ON p.project_id = ch.project_id
                    WHERE @include_project_documents = TRUE
                      AND ch.is_active = TRUE
                      AND ch.index_status IN ('lexical_ready','embedding_ready','ready')
                      AND d.is_active = TRUE
                      AND COALESCE(d.engineering_visible, FALSE) = TRUE
                      AND COALESCE(d.pulse_ai_processing_status, '') = 'ready'
                      AND d.pulse_ai_active_version_id = ch.pulse_ai_document_version_id
                      AND v.authority_status IN ('approved','canonical')
                      AND (@project_id IS NULL OR ch.project_id = @project_id)
                      AND (@require_timesheet = FALSE OR ch.ai_timesheet_context_enabled = TRUE)
                      AND (cardinality(@categories) = 0 OR LOWER(ch.document_category) = ANY(@categories))
                      AND (
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

                    UNION ALL

                    SELECT
                        ch.chunk_id,
                        ch.pulse_ai_document_version_id,
                        ch.project_intake_document_id,
                        ch.project_id,
                        ch.project_code,
                        ch.project_name,
                        ch.customer_name,
                        ch.document_category,
                        ch.document_version,
                        ch.classification,
                        document.original_file_name,
                        ch.citation_anchor,
                        ch.page_number,
                        ch.sheet_name,
                        ch.section_title,
                        ch.chunk_text,
                        ch.source_sha256,
                        ch.text_sha256,
                        ch.embedding,
                        ch.embedding_dimension,
                        ch.search_vector,
                        ch.processed_at
                    FROM pulse_ai_document_chunks ch
                    JOIN project_intake_documents document
                      ON document.project_intake_document_id = ch.project_intake_document_id
                    JOIN pulse_ai_document_versions version
                      ON version.pulse_ai_document_version_id = ch.pulse_ai_document_version_id
                    JOIN pulse_ai_conversation_attachments attachment
                      ON attachment.project_intake_document_id = ch.project_intake_document_id
                    JOIN pulse_ai_conversations conversation
                      ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                    WHERE cardinality(@attachment_ids) > 0
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
                      AND ch.index_status IN ('lexical_ready','embedding_ready','ready')
                ), scored AS (
                    SELECT
                        candidate.*,
                        ts_rank_cd(
                            candidate.search_vector,
                            websearch_to_tsquery('english', @question),
                            32
                        )::double precision AS lexical_score,
                        CASE
                            WHEN @has_embedding = FALSE
                              OR candidate.embedding IS NULL
                              OR candidate.embedding_dimension IS NULL
                              OR candidate.embedding_dimension <> @embedding_dimension
                            THEN 0::double precision
                            ELSE COALESCE((
                                SELECT
                                    SUM(document_value * query_value)
                                    / NULLIF(
                                        SQRT(SUM(document_value * document_value))
                                        * SQRT(SUM(query_value * query_value)),
                                        0
                                    )
                                FROM unnest(candidate.embedding) WITH ORDINALITY document_vector(document_value, ordinal)
                                JOIN unnest(@query_embedding::double precision[]) WITH ORDINALITY query_vector(query_value, ordinal)
                                  USING (ordinal)
                            ), 0::double precision)
                        END AS semantic_score
                    FROM authorized_candidates candidate
                )
                SELECT
                    chunk_id,
                    pulse_ai_document_version_id,
                    project_intake_document_id,
                    project_id,
                    project_code,
                    project_name,
                    customer_name,
                    document_category,
                    document_version,
                    classification,
                    original_file_name,
                    citation_anchor,
                    page_number,
                    sheet_name,
                    section_title,
                    chunk_text,
                    source_sha256,
                    text_sha256,
                    lexical_score,
                    semantic_score,
                    ((lexical_score * @lexical_weight) + (semantic_score * @semantic_weight))::double precision AS combined_score,
                    processed_at,
                    COUNT(*) OVER()::integer AS authorized_count
                FROM scored
                WHERE lexical_score > 0 OR semantic_score > 0
                ORDER BY combined_score DESC, lexical_score DESC, processed_at DESC
                LIMIT @candidate_limit;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", query.ProjectId is null ? DBNull.Value : query.ProjectId.Value);
            command.Parameters.AddWithValue("require_timesheet", query.RequireTimesheetFlag);
            command.Parameters.AddWithValue("include_project_documents", query.IncludeProjectDocuments);
            command.Parameters.AddWithValue("categories", categories);
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("is_pm_lead", access.IsProjectManagementLead);
            command.Parameters.AddWithValue("user_id", access.UserId);
            var conversationParameter = command.Parameters.Add("conversation_id", NpgsqlDbType.Uuid);
            conversationParameter.Value = query.ConversationId is null
                ? DBNull.Value
                : query.ConversationId.Value;
            var attachmentParameter = command.Parameters.Add(
                "attachment_ids",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid);
            attachmentParameter.Value = query.AttachmentIds.ToArray();
            command.Parameters.AddWithValue("question", Clean(query.Question, 12_000));
            command.Parameters.AddWithValue("has_embedding", hasEmbedding);
            command.Parameters.AddWithValue("embedding_dimension", hasEmbedding ? queryEmbedding!.Length : 0);
            var embeddingParameter = command.Parameters.Add(
                "query_embedding",
                NpgsqlDbType.Array | NpgsqlDbType.Double);
            embeddingParameter.Value = hasEmbedding ? queryEmbedding! : Array.Empty<double>();
            command.Parameters.AddWithValue("lexical_weight", (double)query.LexicalWeight);
            command.Parameters.AddWithValue("semantic_weight", (double)query.SemanticWeight);
            command.Parameters.AddWithValue("candidate_limit", Math.Clamp(query.MaximumCandidates, 10, 1000));

            var candidates = new List<PulseAiPrivateRetrievedChunk>();
            var authorizedCount = 0;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                authorizedCount = reader.GetInt32(22);
                candidates.Add(new PulseAiPrivateRetrievedChunk(
                    ChunkId: reader.GetString(0),
                    DocumentVersionId: reader.GetGuid(1),
                    DocumentId: reader.GetGuid(2),
                    ProjectId: reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    ProjectCode: reader.GetString(4),
                    ProjectName: reader.GetString(5),
                    CustomerName: reader.GetString(6),
                    DocumentCategory: reader.GetString(7),
                    DocumentVersion: reader.GetString(8),
                    Classification: reader.GetString(9),
                    OriginalFileName: reader.GetString(10),
                    CitationAnchor: reader.GetString(11),
                    PageNumber: reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    SheetName: reader.IsDBNull(13) ? null : reader.GetString(13),
                    SectionTitle: reader.GetString(14),
                    Text: reader.GetString(15),
                    SourceSha256: reader.GetString(16),
                    TextSha256: reader.GetString(17),
                    LexicalScore: Convert.ToDecimal(reader.GetDouble(18)),
                    SemanticScore: Convert.ToDecimal(reader.GetDouble(19)),
                    CombinedScore: Convert.ToDecimal(reader.GetDouble(20)),
                    ProcessedAt: reader.GetFieldValue<DateTimeOffset>(21),
                    RankOrder: 0));
            }

            var selected = SelectDiverseChunks(
                candidates,
                query.MaximumChunks,
                minimumScore: query.MinimumEvidenceScore,
                prioritizeSowScope: query.FeatureCode is CelarAiCapabilityCatalog.ProjectFlowHivePlan
                    or CelarAiCapabilityCatalog.ProjectForgePlanEstimate);
            return new RetrievalRows(
                CandidateCount: candidates.Count,
                AuthorizedCandidateCount: authorizedCount,
                Chunks: selected,
                DiagnosticCode: candidates.Count > 0 && selected.Count == 0
                    ? "private_evidence_below_minimum_score"
                    : string.Empty);
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return RetrievalRows.Empty;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI private retrieval failed without logging question or chunk text. Feature={Feature} Diagnostic={Diagnostic}",
                query.FeatureCode,
                Diagnostic(exception));
            return RetrievalRows.Empty with { DiagnosticCode = Diagnostic(exception) };
        }
    }

    public async Task<Guid> CreateAnswerRunAsync(
        PulseAiPrivateRetrievalQuery query,
        string detailLevel,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate) return Guid.NewGuid();
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO pulse_ai_answer_runs (
                feature_code,purpose_code,answer_status,actual_user_id,effective_user_id,
                project_id,project_code,question_text,question_sha256,request_filters_json,
                detail_level,prompt_contract_version,retrieval_contract_version,
                correlation_id,data_as_of
            ) VALUES (
                @feature_code,@purpose_code,'requested',@actual_user_id,@effective_user_id,
                @project_id,@project_code,@question_text,@question_sha256,@filters::jsonb,
                @detail_level,@prompt_version,@retrieval_version,@correlation_id,NOW()
            )
            RETURNING pulse_ai_answer_run_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("feature_code", query.FeatureCode);
        command.Parameters.AddWithValue("purpose_code", query.PurposeCode);
        command.Parameters.AddWithValue("actual_user_id", query.ActualUserId);
        command.Parameters.AddWithValue("effective_user_id", query.EffectiveUserId);
        command.Parameters.AddWithValue("project_id", query.ProjectId is null ? DBNull.Value : query.ProjectId.Value);
        command.Parameters.AddWithValue("project_code", Clean(query.ProjectCode, 120));
        command.Parameters.AddWithValue("question_text", Clean(query.Question, 40_000));
        command.Parameters.AddWithValue("question_sha256", Sha256(query.Question));
        command.Parameters.AddWithValue("filters", JsonSerializer.Serialize(new
        {
            query.ProjectCode,
            query.ProjectName,
            query.RequireTimesheetFlag,
            query.IncludeProjectDocuments,
            query.AllowedDocumentCategories,
            query.MaximumChunks,
            query.ConversationId,
            query.AttachmentIds,
            rawPromptLogged = false
        }));
        command.Parameters.AddWithValue("detail_level", detailLevel);
        command.Parameters.AddWithValue("prompt_version", PulseAiPrivateRagPolicy.PromptContractVersion);
        command.Parameters.AddWithValue("retrieval_version", PulseAiPrivateRagPolicy.RetrievalContractVersion);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Celar AI answer run identifier was not returned."));
    }

    public async Task SaveRetrievalEventAsync(
        Guid answerRunId,
        PulseAiPrivateRetrievalQuery query,
        PulseAiPrivateRetrievalResult retrieval,
        string eventStatus,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate) return;
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            INSERT INTO pulse_ai_retrieval_events (
                pulse_ai_answer_run_id,actual_user_id,effective_user_id,project_id,
                feature_code,event_code,event_status,retrieval_mode,candidate_count,
                authorized_candidate_count,returned_chunk_count,correlation_id,
                diagnostic_code,evidence_json
            ) VALUES (
                @answer_run_id,@actual_user_id,@effective_user_id,@project_id,
                @feature_code,'private_retrieval_completed',@event_status,@retrieval_mode,
                @candidate_count,@authorized_candidate_count,@returned_chunk_count,
                @correlation_id,@diagnostic_code,@evidence::jsonb
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("answer_run_id", answerRunId);
        command.Parameters.AddWithValue("actual_user_id", query.ActualUserId);
        command.Parameters.AddWithValue("effective_user_id", query.EffectiveUserId);
        command.Parameters.AddWithValue("project_id", retrieval.ResolvedProjectId is null ? DBNull.Value : retrieval.ResolvedProjectId.Value);
        command.Parameters.AddWithValue("feature_code", query.FeatureCode);
        command.Parameters.AddWithValue("event_status", eventStatus);
        command.Parameters.AddWithValue("retrieval_mode", retrieval.RetrievalMode);
        command.Parameters.AddWithValue("candidate_count", retrieval.CandidateCount);
        command.Parameters.AddWithValue("authorized_candidate_count", retrieval.AuthorizedCandidateCount);
        command.Parameters.AddWithValue("returned_chunk_count", retrieval.Chunks.Count);
        command.Parameters.AddWithValue("correlation_id", query.CorrelationId);
        command.Parameters.AddWithValue("diagnostic_code", retrieval.DiagnosticCode);
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(new
        {
            authorizationAppliedBeforeRanking = true,
            reauthorizationRequiredBeforePromptAssembly = true,
            query.RequireTimesheetFlag,
            query.AllowedDocumentCategories,
            retrieval.CoverageScore,
            missingEvidenceCount = retrieval.MissingEvidence.Count,
            conflictCount = retrieval.Conflicts.Count,
            retrieval.DiagnosticCode,
            rawQuestionLogged = false,
            rawChunkTextLogged = false,
            displayStringsLogged = false,
            attachmentFileNamesLogged = false,
            vectorsLogged = false
        }));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> CompleteAnswerRunAsync(
        PulseAiPrivateRagAnswer answer,
        PulseAiPrivateRetrievalQuery query,
        PulseAiPrivateModelResult model,
        bool persistAnswerText,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate) return false;
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var attachmentIds = query.AttachmentIds
                .Where(value => value != Guid.Empty)
                .Distinct()
                .ToArray();
            if (attachmentIds.Length > 0)
            {
                if (query.ConversationId is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
                const string attachmentLockSql = """
                    SELECT attachment.pulse_ai_conversation_attachment_id
                    FROM pulse_ai_conversation_attachments attachment
                    JOIN pulse_ai_conversations conversation
                      ON conversation.pulse_ai_conversation_id = attachment.pulse_ai_conversation_id
                    JOIN project_intake_documents document
                      ON document.project_intake_document_id = attachment.project_intake_document_id
                    WHERE attachment.pulse_ai_conversation_attachment_id = ANY(@attachment_ids)
                      AND attachment.pulse_ai_conversation_id = @conversation_id
                      AND attachment.uploaded_by_user_id = @effective_user_id
                      AND attachment.revoked_at IS NULL
                      AND attachment.retention_until > NOW()
                      AND conversation.actual_user_id = @actual_user_id
                      AND conversation.effective_user_id = @effective_user_id
                      AND conversation.status = 'active'
                      AND (conversation.retention_until IS NULL OR conversation.retention_until > NOW())
                      AND document.upload_source = 'celar_ai_chat_attachment'
                      AND document.uploaded_by_user_id = @effective_user_id
                      AND document.is_active = TRUE
                      AND NOT EXISTS (
                          SELECT 1
                          FROM pulse_ai_conversation_attachment_purge_audit audit
                          WHERE audit.pulse_ai_conversation_attachment_id = attachment.pulse_ai_conversation_attachment_id
                      )
                    FOR UPDATE OF attachment;
                    """;
                await using var attachmentLock = new NpgsqlCommand(
                    attachmentLockSql,
                    connection,
                    transaction);
                attachmentLock.Parameters.AddWithValue("attachment_ids", attachmentIds);
                attachmentLock.Parameters.AddWithValue("conversation_id", query.ConversationId.Value);
                attachmentLock.Parameters.AddWithValue("actual_user_id", query.ActualUserId);
                attachmentLock.Parameters.AddWithValue("effective_user_id", query.EffectiveUserId);
                var lockedAttachmentIds = new HashSet<Guid>();
                await using (var attachmentReader = await attachmentLock.ExecuteReaderAsync(cancellationToken))
                {
                    while (await attachmentReader.ReadAsync(cancellationToken))
                        lockedAttachmentIds.Add(attachmentReader.GetGuid(0));
                }
                if (lockedAttachmentIds.Count != attachmentIds.Length)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return false;
                }
            }

            const string sql = """
                UPDATE pulse_ai_answer_runs
                SET answer_status = @answer_status,
                    project_id = @project_id,
                    project_code = @project_code,
                    private_model_provider = @model_provider,
                    private_model_name = @model_name,
                    retrieval_mode = @retrieval_mode,
                    retrieved_chunk_count = @retrieved_chunk_count,
                    cited_source_count = @cited_source_count,
                    source_document_count = @source_document_count,
                    source_version_count = @source_version_count,
                    input_character_count = @input_character_count,
                    output_character_count = @output_character_count,
                    confidence_score = @confidence_score,
                    coverage_score = @coverage_score,
                    citation_coverage_score = @citation_coverage_score,
                    answer_json = @answer_json::jsonb,
                    warning_codes = @warnings::jsonb,
                    missing_evidence = @missing::jsonb,
                    conflicts_json = @conflicts::jsonb,
                    source_health_json = @source_health::jsonb,
                    privacy_evidence_json = @privacy::jsonb,
                    diagnostic_code = @diagnostic_code,
                    diagnostic_message = @diagnostic_message,
                    data_as_of = @data_as_of,
                    completed_at = NOW()
                WHERE pulse_ai_answer_run_id = @answer_run_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("answer_run_id", answer.AnswerRunId);
            command.Parameters.AddWithValue("answer_status", answer.Status);
            command.Parameters.AddWithValue("project_id", answer.ProjectId is null ? DBNull.Value : answer.ProjectId.Value);
            command.Parameters.AddWithValue("project_code", answer.ProjectCode);
            command.Parameters.AddWithValue("model_provider", model.Provider);
            command.Parameters.AddWithValue("model_name", model.Model);
            command.Parameters.AddWithValue("retrieval_mode", answer.RetrievalMode);
            command.Parameters.AddWithValue("retrieved_chunk_count", answer.Citations.Count);
            command.Parameters.AddWithValue("cited_source_count", answer.Citations.Count);
            command.Parameters.AddWithValue("source_document_count", answer.Citations.Select(citation => citation.DocumentId).Distinct().Count());
            command.Parameters.AddWithValue("source_version_count", answer.Citations.Select(citation => citation.DocumentVersion).Distinct().Count());
            command.Parameters.AddWithValue("input_character_count", model.InputCharacters);
            command.Parameters.AddWithValue("output_character_count", model.OutputCharacters);
            command.Parameters.AddWithValue("confidence_score", Math.Clamp(answer.Answer?.Confidence ?? answer.FlowHivePlan?.Confidence ?? 0m, 0m, 1m));
            command.Parameters.AddWithValue("coverage_score", Math.Clamp(answer.CoverageScore, 0m, 1m));
            command.Parameters.AddWithValue("citation_coverage_score", Math.Clamp(answer.CitationCoverageScore, 0m, 1m));
            command.Parameters.AddWithValue("answer_json", persistAnswerText ? JsonSerializer.Serialize(answer.ToPublicResponse()) : "{}");
            command.Parameters.AddWithValue("warnings", JsonSerializer.Serialize(answer.Warnings));
            command.Parameters.AddWithValue("missing", JsonSerializer.Serialize(answer.MissingEvidence));
            command.Parameters.AddWithValue("conflicts", JsonSerializer.Serialize(answer.Conflicts));
            command.Parameters.AddWithValue("source_health", JsonSerializer.Serialize(new
            {
                answer.RetrievalMode,
                answer.Citations.Count,
                answer.DataAsOf
            }));
            command.Parameters.AddWithValue("privacy", JsonSerializer.Serialize(new
            {
                boundary = PulseAiPrivateRagPolicy.PrivacyBoundary,
                rawDocumentsSentExternally = false,
                rawChunkTextLogged = false,
                vectorsReturned = false,
                promptsReturned = false
            }));
            command.Parameters.AddWithValue("diagnostic_code", answer.DiagnosticCode);
            command.Parameters.AddWithValue("diagnostic_message", answer.DiagnosticCode.Length == 0 ? string.Empty : "See sanitized diagnostic code.");
            command.Parameters.AddWithValue("data_as_of", answer.DataAsOf);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var rank = 1;
            foreach (var citation in answer.Citations)
            {
                if (string.Equals(
                        citation.SourceType,
                        "module025_saved_service_overview",
                        StringComparison.Ordinal)
                    && string.Equals(
                        citation.SourceModule,
                        "025",
                        StringComparison.Ordinal)
                    && string.Equals(
                        citation.DocumentCategory,
                        "module025_service_overview",
                        StringComparison.Ordinal))
                {
                    const string module025CitationSql = """
                        INSERT INTO pulse_ai_answer_citations (
                            pulse_ai_answer_run_id,chunk_id,project_intake_document_id,
                            pulse_ai_document_version_id,project_id,source_type,source_module,
                            document_category,document_version,original_file_name,
                            citation_anchor,page_number,sheet_name,rank_order,combined_score,
                            source_sha256,text_sha256,source_processed_at
                        ) VALUES (
                            @answer_run_id,NULL,NULL,NULL,NULL,@source_type,@source_module,
                            @document_category,@document_version,@original_file_name,
                            @citation_anchor,NULL,NULL,@rank_order,@combined_score,
                            @source_sha256,@text_sha256,@source_processed_at
                        )
                        ON CONFLICT (pulse_ai_answer_run_id, rank_order) DO NOTHING;
                        """;
                    await using var module025Citation = new NpgsqlCommand(
                        module025CitationSql,
                        connection,
                        transaction);
                    module025Citation.Parameters.AddWithValue(
                        "answer_run_id",
                        answer.AnswerRunId);
                    module025Citation.Parameters.AddWithValue(
                        "source_type",
                        citation.SourceType);
                    module025Citation.Parameters.AddWithValue(
                        "source_module",
                        citation.SourceModule);
                    module025Citation.Parameters.AddWithValue(
                        "document_category",
                        citation.DocumentCategory);
                    module025Citation.Parameters.AddWithValue(
                        "document_version",
                        citation.DocumentVersion);
                    module025Citation.Parameters.AddWithValue(
                        "original_file_name",
                        citation.OriginalFileName);
                    module025Citation.Parameters.AddWithValue(
                        "citation_anchor",
                        citation.CitationAnchor);
                    module025Citation.Parameters.AddWithValue("rank_order", rank);
                    module025Citation.Parameters.AddWithValue(
                        "combined_score",
                        citation.RelevanceScore);
                    module025Citation.Parameters.AddWithValue(
                        "source_sha256",
                        citation.SourceSha256);
                    module025Citation.Parameters.AddWithValue(
                        "text_sha256",
                        citation.TextSha256);
                    module025Citation.Parameters.AddWithValue(
                        "source_processed_at",
                        citation.ProcessedAt);
                    await module025Citation.ExecuteNonQueryAsync(cancellationToken);
                    rank++;
                    continue;
                }

                const string citationSql = """
                    INSERT INTO pulse_ai_answer_citations (
                        pulse_ai_answer_run_id,chunk_id,project_intake_document_id,
                        pulse_ai_document_version_id,project_id,source_type,source_module,
                        document_category,document_version,original_file_name,
                        citation_anchor,page_number,sheet_name,rank_order,combined_score,
                        source_sha256,text_sha256,source_processed_at
                    )
                    SELECT
                        @answer_run_id,ch.chunk_id,ch.project_intake_document_id,
                        ch.pulse_ai_document_version_id,ch.project_id,
                        CASE
                            WHEN d.upload_source = 'celar_ai_chat_attachment' THEN 'conversation_attachment'
                            ELSE 'project_document'
                        END,
                        '011',
                        ch.document_category,ch.document_version,d.original_file_name,
                        ch.citation_anchor,ch.page_number,ch.sheet_name,@rank_order,
                        @combined_score,ch.source_sha256,ch.text_sha256,ch.processed_at
                    FROM pulse_ai_document_chunks ch
                    JOIN project_intake_documents d
                      ON d.project_intake_document_id = ch.project_intake_document_id
                    WHERE ch.project_intake_document_id = @document_id
                      AND ch.source_sha256 = @source_sha256
                      AND ch.text_sha256 = @text_sha256
                      AND ch.is_active = TRUE
                    ORDER BY ch.processed_at DESC
                    LIMIT 1
                    ON CONFLICT (pulse_ai_answer_run_id, rank_order) DO NOTHING;
                    """;
                await using var citationCommand = new NpgsqlCommand(citationSql, connection, transaction);
                citationCommand.Parameters.AddWithValue("answer_run_id", answer.AnswerRunId);
                citationCommand.Parameters.AddWithValue("rank_order", rank);
                citationCommand.Parameters.AddWithValue("combined_score", citation.RelevanceScore);
                citationCommand.Parameters.AddWithValue("document_id", citation.DocumentId);
                citationCommand.Parameters.AddWithValue("source_sha256", citation.SourceSha256);
                citationCommand.Parameters.AddWithValue("text_sha256", citation.TextSha256);
                await citationCommand.ExecuteNonQueryAsync(cancellationToken);
                rank++;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> SaveFeedbackAsync(
        Guid answerRunId,
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiPrivateFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().IsCandidate) return false;
        var feedbackType = Clean(request.FeedbackType, 40).ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "accepted",
            "accepted_with_edits",
            "rejected",
            "incorrect",
            "incomplete",
            "unsafe",
            "unauthorized_source",
            "other"
        };
        if (!allowed.Contains(feedbackType)) return false;

        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            const string lockSql = """
                SELECT diagnostic_code
                FROM pulse_ai_answer_runs
                WHERE pulse_ai_answer_run_id = @answer_run_id
                  AND actual_user_id = @actual_user_id
                  AND effective_user_id = @effective_user_id
                FOR UPDATE;
                """;
            await using var answerRunLock = new NpgsqlCommand(lockSql, connection, transaction);
            answerRunLock.Parameters.AddWithValue("answer_run_id", answerRunId);
            answerRunLock.Parameters.AddWithValue("actual_user_id", actualUserId);
            answerRunLock.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            var diagnosticCode = await answerRunLock.ExecuteScalarAsync(cancellationToken) as string;
            if (diagnosticCode is null
                || string.Equals(
                    diagnosticCode,
                    "private_attachment_retention_purged",
                    StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            const string sql = """
            INSERT INTO pulse_ai_answer_feedback (
                pulse_ai_answer_run_id,actual_user_id,effective_user_id,
                feedback_type,feedback_reason,corrected_answer_json,
                training_candidate,training_review_status
            ) VALUES (
                @answer_run_id,@actual_user_id,@effective_user_id,
                @feedback_type,@feedback_reason,@corrected_answer::jsonb,
                FALSE,'not_reviewed'
            )
            RETURNING pulse_ai_answer_feedback_id;
            """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("answer_run_id", answerRunId);
            command.Parameters.AddWithValue("actual_user_id", actualUserId);
            command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
            command.Parameters.AddWithValue("feedback_type", feedbackType);
            command.Parameters.AddWithValue("feedback_reason", Clean(request.FeedbackReason, 4000));
            command.Parameters.AddWithValue("corrected_answer", request.CorrectedAnswer is null ? "{}" : JsonSerializer.Serialize(request.CorrectedAnswer));
            var saved = await command.ExecuteScalarAsync(cancellationToken) is Guid;
            if (!saved)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<object?> GetAnswerAuditAsync(
        Guid answerRunId,
        PulseAiPrivateRagAccess access,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive || !access.CanViewAudit) return null;
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT
                    run.pulse_ai_answer_run_id,
                    run.feature_code,
                    run.purpose_code,
                    run.answer_status,
                    run.actual_user_id,
                    run.effective_user_id,
                    run.project_id,
                    run.project_code,
                    run.detail_level,
                    run.private_model_provider,
                    run.private_model_name,
                    run.retrieval_mode,
                    run.retrieved_chunk_count,
                    run.cited_source_count,
                    run.source_document_count,
                    run.source_version_count,
                    run.confidence_score,
                    run.coverage_score,
                    run.citation_coverage_score,
                    run.answer_json,
                    run.warning_codes,
                    run.missing_evidence,
                    run.conflicts_json,
                    run.source_health_json,
                    run.privacy_evidence_json,
                    run.correlation_id,
                    run.diagnostic_code,
                    run.data_as_of,
                    run.requested_at,
                    run.completed_at
                FROM pulse_ai_answer_runs run
                LEFT JOIN projects p ON p.project_id = run.project_id
                WHERE run.pulse_ai_answer_run_id = @answer_run_id
                  AND (
                    @is_broad = TRUE
                    OR run.actual_user_id = @user_id
                    OR run.effective_user_id = @user_id
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
                        WHERE pa.project_id = run.project_id AND pa.user_id = @user_id
                    )
                  );
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("answer_run_id", answerRunId);
            command.Parameters.AddWithValue("is_broad", access.IsBroadScope);
            command.Parameters.AddWithValue("is_pm_lead", access.IsProjectManagementLead);
            command.Parameters.AddWithValue("user_id", access.UserId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            var run = new
            {
                answerRunId = reader.GetGuid(0),
                featureCode = reader.GetString(1),
                purposeCode = reader.GetString(2),
                status = reader.GetString(3),
                actualUserId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                effectiveUserId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5),
                projectId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
                projectCode = reader.GetString(7),
                detailLevel = reader.GetString(8),
                modelProvider = reader.GetString(9),
                modelName = reader.GetString(10),
                retrievalMode = reader.GetString(11),
                retrievedChunkCount = reader.GetInt32(12),
                citedSourceCount = reader.GetInt32(13),
                sourceDocumentCount = reader.GetInt32(14),
                sourceVersionCount = reader.GetInt32(15),
                confidenceScore = reader.GetDecimal(16),
                coverageScore = reader.GetDecimal(17),
                citationCoverageScore = reader.GetDecimal(18),
                answer = ParseJson(reader.GetString(19)),
                warnings = ParseJson(reader.GetString(20)),
                missingEvidence = ParseJson(reader.GetString(21)),
                conflicts = ParseJson(reader.GetString(22)),
                sourceHealth = ParseJson(reader.GetString(23)),
                privacyEvidence = ParseJson(reader.GetString(24)),
                correlationId = reader.GetString(25),
                diagnosticCode = reader.GetString(26),
                dataAsOf = reader.GetFieldValue<DateTimeOffset>(27),
                requestedAt = reader.GetFieldValue<DateTimeOffset>(28),
                completedAt = reader.IsDBNull(29) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(29)
            };
            await reader.CloseAsync();
            var citations = await LoadCitationAuditAsync(connection, answerRunId, cancellationToken);
            var feedback = await LoadFeedbackAuditAsync(connection, answerRunId, cancellationToken);
            return new
            {
                run,
                citations,
                feedback,
                privateEvidence = new
                {
                    questionTextReturned = false,
                    rawChunkTextReturned = false,
                    vectorsReturned = false,
                    promptsReturned = false,
                    secretsReturned = false
                }
            };
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return null;
        }
    }

    private static IReadOnlyList<PulseAiPrivateRetrievedChunk> SelectDiverseChunks(
        IReadOnlyList<PulseAiPrivateRetrievedChunk> candidates,
        int maximum,
        decimal minimumScore,
        bool prioritizeSowScope)
    {
        maximum = Math.Clamp(maximum, 1, 40);
        var selected = new List<PulseAiPrivateRetrievedChunk>();
        var perDocument = new Dictionary<Guid, int>();
        var ordered = prioritizeSowScope
            ? candidates
                .OrderByDescending(IsApprovedSowScopeCandidate)
                .ThenByDescending(candidate => candidate.CombinedScore)
                .ThenByDescending(candidate => candidate.ProcessedAt)
            : candidates.AsEnumerable();
        foreach (var candidate in ordered)
        {
            if (candidate.CombinedScore < minimumScore) continue;
            perDocument.TryGetValue(candidate.DocumentId, out var used);
            if (used >= 4) continue;
            selected.Add(candidate with { RankOrder = selected.Count + 1 });
            perDocument[candidate.DocumentId] = used + 1;
            if (selected.Count >= maximum) break;
        }
        return selected;
    }

    private static bool IsApprovedSowScopeCandidate(PulseAiPrivateRetrievedChunk candidate)
    {
        var isSow = candidate.DocumentCategory.Equals("sow", StringComparison.OrdinalIgnoreCase)
            || candidate.DocumentCategory.Equals("statement_of_work", StringComparison.OrdinalIgnoreCase);
        if (!isSow) return false;
        var heading = $"{candidate.SectionTitle} {candidate.CitationAnchor}";
        return heading.Contains("scope", StringComparison.OrdinalIgnoreCase)
            || heading.Contains("service", StringComparison.OrdinalIgnoreCase)
            || heading.Contains("deliverable", StringComparison.OrdinalIgnoreCase)
            || heading.Contains("acceptance", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<object>> LoadCitationAuditAsync(
        NpgsqlConnection connection,
        Guid answerRunId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                project_intake_document_id,project_id,source_type,source_module,
                document_category,document_version,original_file_name,citation_anchor,
                page_number,sheet_name,rank_order,lexical_score,semantic_score,
                combined_score,source_sha256,text_sha256,source_processed_at
            FROM pulse_ai_answer_citations
            WHERE pulse_ai_answer_run_id = @answer_run_id
            ORDER BY rank_order;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("answer_run_id", answerRunId);
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                documentId = reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0),
                projectId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1),
                sourceType = reader.GetString(2),
                sourceModule = reader.GetString(3),
                documentCategory = reader.GetString(4),
                documentVersion = reader.GetString(5),
                originalFileName = reader.GetString(6),
                citationAnchor = reader.GetString(7),
                pageNumber = reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8),
                sheetName = reader.IsDBNull(9) ? null : reader.GetString(9),
                rankOrder = reader.GetInt32(10),
                lexicalScore = reader.GetDecimal(11),
                semanticScore = reader.GetDecimal(12),
                combinedScore = reader.GetDecimal(13),
                sourceSha256 = reader.GetString(14),
                textSha256 = reader.GetString(15),
                sourceProcessedAt = reader.IsDBNull(16) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(16)
            });
        }
        return rows;
    }

    private static async Task<IReadOnlyList<object>> LoadFeedbackAuditAsync(
        NpgsqlConnection connection,
        Guid answerRunId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT feedback_type,feedback_reason,training_candidate,
                   training_review_status,created_at
            FROM pulse_ai_answer_feedback
            WHERE pulse_ai_answer_run_id = @answer_run_id
            ORDER BY created_at DESC;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("answer_run_id", answerRunId);
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                feedbackType = reader.GetString(0),
                feedbackReason = reader.GetString(1),
                trainingCandidate = reader.GetBoolean(2),
                trainingReviewStatus = reader.GetString(3),
                createdAt = reader.GetFieldValue<DateTimeOffset>(4)
            });
        }
        return rows;
    }

    private static object ParseJson(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(value) ?? new { };
        }
        catch
        {
            return new { };
        }
    }

    private static IReadOnlySet<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Sha256(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
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
        _ => "private_rag_repository_failure"
    };

    public sealed record ProjectResolution(
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        string CustomerName);

    public sealed record RetrievalRows(
        int CandidateCount,
        int AuthorizedCandidateCount,
        IReadOnlyList<PulseAiPrivateRetrievedChunk> Chunks,
        string DiagnosticCode = "")
    {
        public static RetrievalRows Empty => new(0, 0, []);
    }
}
