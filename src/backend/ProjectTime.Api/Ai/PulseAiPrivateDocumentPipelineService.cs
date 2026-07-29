using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateDocumentPipelineService
{
    private static readonly HashSet<string> BroadDocumentRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "EXECUTIVE"
    };

    private static readonly HashSet<string> ProjectManagementRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    };

    private readonly PulseAiPrivateDocumentExtractionService _extractor;
    private readonly ILogger<PulseAiPrivateDocumentPipelineService> _logger;

    public PulseAiPrivateDocumentPipelineService(
        PulseAiPrivateDocumentExtractionService extractor,
        ILogger<PulseAiPrivateDocumentPipelineService> logger)
    {
        _extractor = extractor;
        _logger = logger;
    }

    public PulseAiDocumentPipelineOptions Options() =>
        PulseAiDocumentPipelineOptions.FromEnvironment();

    public async Task<PulseAiDocumentPipelineReadiness> GetReadinessAsync(
        Guid effectiveUserId,
        CancellationToken cancellationToken = default)
    {
        var options = Options();
        var generatedAt = DateTimeOffset.UtcNow;
        var missingConfiguration = MissingDatabaseConfiguration();
        var blockers = new List<string>();
        var ready = new List<string>
        {
            "private path-confinement policy",
            "file type and signature admission policy",
            "native PDF, DOCX, PPTX, XLSX, HTML, XML, CSV, Markdown, JSON, and text extraction code",
            "deterministic section and chunk checksums",
            "citation-preserving chunk projection",
            "permission-aware document inventory"
        };

        var storageRootExists = Directory.Exists(options.UploadRoot);
        if (!storageRootExists)
            blockers.Add("The configured private upload root does not exist in this runtime.");
        if (!options.ExtractionPreviewEnabled)
            blockers.Add("Private extraction preview is disabled.");
        if (!options.MalwareScanAttested)
            blockers.Add("A verifiable malware-scan attestation is required before content parsing.");
        if (!options.OcrEndpointConfigured)
            blockers.Add("The private OCR adapter is not configured for image-only documents.");
        if (!options.PrivateEmbeddingEndpointConfigured)
            blockers.Add("The private embedding endpoint and model are not configured.");
        if (!options.PrivateVectorIndexConfigured)
            blockers.Add("The permission-scoped vector index is not configured.");

        if (missingConfiguration.Count > 0)
        {
            return new PulseAiDocumentPipelineReadiness(
                Status: "database_configuration_missing",
                ContractVersion: PulseAiPrivateDocumentPipelinePolicy.ContractVersion,
                DatabaseConfigured: false,
                DocumentSchemaAvailable: false,
                StorageRootConfigured: !string.IsNullOrWhiteSpace(options.UploadRoot),
                StorageRootExists: storageRootExists,
                ExtractionPreviewEnabled: options.ExtractionPreviewEnabled,
                MalwareScanAttested: options.MalwareScanAttested,
                MalwareScannerMode: options.MalwareScannerMode,
                NativePdfExtractionAvailable: true,
                NativeOpenXmlExtractionAvailable: true,
                NativeTextExtractionAvailable: true,
                OcrEndpointConfigured: options.OcrEndpointConfigured,
                PrivateEmbeddingEndpointConfigured: options.PrivateEmbeddingEndpointConfigured,
                PrivateVectorIndexConfigured: options.PrivateVectorIndexConfigured,
                AuthorizedDocumentCount: 0,
                SupportedDocumentCount: 0,
                ExtractionReadyDocumentCount: 0,
                SupportedExtensions: PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions,
                ReadyCapabilities: ready,
                Blockers: [.. blockers, "ProjectPulse database configuration is incomplete."],
                MissingConfiguration: missingConfiguration,
                GeneratedAt: generatedAt,
                DiagnosticCode: "database_configuration_missing");
        }

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            var schema = await InspectSchemaAsync(connection, cancellationToken);
            var access = await LoadAccessAsync(connection, effectiveUserId, cancellationToken);

            if (!schema.RequiredColumnsAvailable)
            {
                blockers.Add("The project document schema does not contain all required source and security columns.");
            }
            if (!access.IsActive)
            {
                blockers.Add("The effective user could not be resolved as an active ProjectPulse user.");
            }

            var counts = schema.RequiredColumnsAvailable && access.IsActive
                ? await CountAuthorizedDocumentsAsync(connection, access, schema, cancellationToken)
                : new DocumentCounts(0, 0, 0);

            var extractionPreviewReady =
                schema.RequiredColumnsAvailable
                && access.IsActive
                && storageRootExists
                && options.ExtractionPreviewEnabled
                && options.MalwareScanAttested;

            if (extractionPreviewReady)
                ready.Add("authorized local extraction preview");
            if (options.OcrEndpointConfigured)
                ready.Add("private OCR configuration detected");
            if (options.PrivateEmbeddingEndpointConfigured)
                ready.Add("private embedding configuration detected; execution remains disabled");
            if (options.PrivateVectorIndexConfigured)
                ready.Add("private vector-index configuration detected; writes remain disabled");

            return new PulseAiDocumentPipelineReadiness(
                Status: extractionPreviewReady
                    ? "private_document_extraction_preview_ready"
                    : schema.RequiredColumnsAvailable
                        ? "private_document_pipeline_partially_ready"
                        : "private_document_schema_unavailable",
                ContractVersion: PulseAiPrivateDocumentPipelinePolicy.ContractVersion,
                DatabaseConfigured: true,
                DocumentSchemaAvailable: schema.RequiredColumnsAvailable,
                StorageRootConfigured: true,
                StorageRootExists: storageRootExists,
                ExtractionPreviewEnabled: options.ExtractionPreviewEnabled,
                MalwareScanAttested: options.MalwareScanAttested,
                MalwareScannerMode: options.MalwareScannerMode,
                NativePdfExtractionAvailable: true,
                NativeOpenXmlExtractionAvailable: true,
                NativeTextExtractionAvailable: true,
                OcrEndpointConfigured: options.OcrEndpointConfigured,
                PrivateEmbeddingEndpointConfigured: options.PrivateEmbeddingEndpointConfigured,
                PrivateVectorIndexConfigured: options.PrivateVectorIndexConfigured,
                AuthorizedDocumentCount: counts.All,
                SupportedDocumentCount: counts.Supported,
                ExtractionReadyDocumentCount: counts.ContextReady,
                SupportedExtensions: PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions,
                ReadyCapabilities: ready.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Blockers: blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                MissingConfiguration: [],
                GeneratedAt: generatedAt);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private document pipeline readiness failed. Diagnostic={Diagnostic}",
                Diagnostic(exception));

            return new PulseAiDocumentPipelineReadiness(
                Status: "private_document_pipeline_readiness_unavailable",
                ContractVersion: PulseAiPrivateDocumentPipelinePolicy.ContractVersion,
                DatabaseConfigured: true,
                DocumentSchemaAvailable: false,
                StorageRootConfigured: true,
                StorageRootExists: storageRootExists,
                ExtractionPreviewEnabled: options.ExtractionPreviewEnabled,
                MalwareScanAttested: options.MalwareScanAttested,
                MalwareScannerMode: options.MalwareScannerMode,
                NativePdfExtractionAvailable: true,
                NativeOpenXmlExtractionAvailable: true,
                NativeTextExtractionAvailable: true,
                OcrEndpointConfigured: options.OcrEndpointConfigured,
                PrivateEmbeddingEndpointConfigured: options.PrivateEmbeddingEndpointConfigured,
                PrivateVectorIndexConfigured: options.PrivateVectorIndexConfigured,
                AuthorizedDocumentCount: 0,
                SupportedDocumentCount: 0,
                ExtractionReadyDocumentCount: 0,
                SupportedExtensions: PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions,
                ReadyCapabilities: ready,
                Blockers: [.. blockers, "Pipeline readiness could not be evaluated without exposing dependency details."],
                MissingConfiguration: [],
                GeneratedAt: generatedAt,
                DiagnosticCode: Diagnostic(exception));
        }
    }

    public async Task<IReadOnlyList<PulseAiDocumentInventoryItem>> ListInventoryAsync(
        Guid effectiveUserId,
        string? projectCode,
        string? documentCategory,
        string? extractionStatus,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (MissingDatabaseConfiguration().Count > 0) return [];
        limit = Math.Clamp(limit, 1, 500);

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            var schema = await InspectSchemaAsync(connection, cancellationToken);
            if (!schema.RequiredColumnsAvailable) return [];
            var access = await LoadAccessAsync(connection, effectiveUserId, cancellationToken);
            if (!access.IsActive) return [];
            var sources = await LoadAuthorizedSourcesAsync(
                connection,
                access,
                schema,
                Clean(projectCode, 100),
                Clean(documentCategory, 80),
                Clean(extractionStatus, 60),
                limit,
                cancellationToken);
            var options = Options();
            return sources.Select(source => ToInventoryItem(source, options)).ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private document inventory failed. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return [];
        }
    }

    public async Task<PulseAiDocumentProcessingPreview?> BuildProcessingPreviewAsync(
        Guid effectiveUserId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        if (MissingDatabaseConfiguration().Count > 0) return null;

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            var schema = await InspectSchemaAsync(connection, cancellationToken);
            if (!schema.RequiredColumnsAvailable) return null;
            var access = await LoadAccessAsync(connection, effectiveUserId, cancellationToken);
            if (!access.IsActive) return null;
            var source = await LoadAuthorizedSourceAsync(
                connection,
                access,
                schema,
                documentId,
                cancellationToken);
            if (source is null) return null;

            var options = Options();
            var inventory = ToInventoryItem(source, options);
            var extraction = await _extractor.ExtractAsync(source, options, cancellationToken);
            var chunks = _extractor.CreateChunks(extraction, options);
            var projection = _extractor.BuildIndexProjection(source, chunks, options);
            var versionQuestions = await LoadVersionAuthorityQuestionsAsync(
                connection,
                source,
                access,
                cancellationToken);
            var blockers = BuildProductionBlockers(options, inventory, extraction, chunks, projection);

            return new PulseAiDocumentProcessingPreview(
                Status: extraction.ExtractionSucceeded
                    ? "private_processing_preview_ready"
                    : extraction.Status,
                ContractVersion: PulseAiPrivateDocumentPipelinePolicy.ContractVersion,
                Document: inventory,
                Extraction: extraction,
                Chunks: chunks,
                IndexProjection: projection,
                VersionAuthorityQuestions: versionQuestions,
                ProductionBlockers: blockers,
                GeneratedAt: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private document processing preview failed. DocumentId={DocumentId} Diagnostic={Diagnostic}",
                documentId,
                Diagnostic(exception));
            return null;
        }
    }

    private static IReadOnlyList<string> BuildProductionBlockers(
        PulseAiDocumentPipelineOptions options,
        PulseAiDocumentInventoryItem inventory,
        PulseAiDocumentExtractionResult extraction,
        IReadOnlyList<PulseAiDocumentChunk> chunks,
        IReadOnlyList<PulseAiIndexProjectionRecord> projection)
    {
        var blockers = new List<string>();
        blockers.AddRange(inventory.Blockers);
        blockers.AddRange(extraction.Blockers);
        if (extraction.OcrRequired && !options.OcrEndpointConfigured)
            blockers.Add("Private OCR is required but not configured.");
        if (chunks.Count == 0)
            blockers.Add("No citation-preserving chunks are available for retrieval.");
        if (projection.Count == 0)
            blockers.Add("No permission-scoped index projection is available.");
        if (!options.PrivateEmbeddingEndpointConfigured)
            blockers.Add("Private embedding execution is not configured.");
        if (!options.PrivateVectorIndexConfigured)
            blockers.Add("Private vector-index persistence is not configured.");
        blockers.Add("This phase does not write extraction text, summaries, chunks, embeddings, or index records to PostgreSQL or an external index.");
        blockers.Add("A separately reviewed processing queue, persistence schema, retention policy, and rollback plan are required before production activation.");
        return blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static PulseAiDocumentInventoryItem ToInventoryItem(
        PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions options)
    {
        var extension = Path.GetExtension(source.OriginalFileName).ToLowerInvariant();
        var supported = PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions
            .Contains(extension, StringComparer.OrdinalIgnoreCase);
        var fileExists = false;
        var confined = false;
        try
        {
            var fullPath = Path.GetFullPath(source.StoragePath);
            var root = Path.GetFullPath(options.UploadRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            confined = fullPath.StartsWith(
                root,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
            fileExists = confined && File.Exists(fullPath);
        }
        catch
        {
            confined = false;
        }

        var blockers = new List<string>();
        if (!supported) blockers.Add("Document format is not supported by the native private pipeline.");
        if (!fileExists) blockers.Add("Stored file is unavailable inside the configured private upload root.");
        if (!confined) blockers.Add("Stored path is outside the configured private upload root.");
        if (!options.MalwareScanAttested) blockers.Add("Malware scan is not attested.");
        if (!options.ExtractionPreviewEnabled) blockers.Add("Extraction preview is disabled.");

        return new PulseAiDocumentInventoryItem(
            DocumentId: source.DocumentId,
            ProjectId: source.ProjectId,
            ProjectCode: source.ProjectCode,
            ProjectName: source.ProjectName,
            CustomerName: source.CustomerName,
            DocumentType: source.DocumentType,
            DocumentCategory: source.DocumentCategory,
            OriginalFileName: source.OriginalFileName,
            ContentType: source.ContentType,
            SizeBytes: source.SizeBytes,
            Extension: extension,
            EngineeringVisible: source.EngineeringVisible,
            AiTimesheetContextEnabled: source.AiTimesheetContextEnabled,
            ExtractionStatus: source.ExtractionStatus,
            ExistingContextSummaryReady: source.ExistingContextSummaryReady,
            ContextLastProcessedAt: source.ContextLastProcessedAt,
            UploadedAt: source.UploadedAt,
            UploadSource: source.UploadSource,
            AccessScope: source.AccessScope,
            SupportedByNativePipeline: supported,
            StoredFileExists: fileExists,
            StoredPathConfined: confined,
            ProductionAdmissionReady: supported
                && fileExists
                && confined
                && options.MalwareScanAttested
                && options.ExtractionPreviewEnabled,
            Blockers: blockers);
    }

    private static async Task<DocumentSchema> InspectSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var tableAvailable = Convert.ToBoolean(await new NpgsqlCommand(
            "SELECT to_regclass('public.project_intake_documents') IS NOT NULL;",
            connection).ExecuteScalarAsync(cancellationToken));
        if (!tableAvailable) return DocumentSchema.Missing;

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'project_intake_documents';
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            columns.Add(reader.GetString(0));

        return new DocumentSchema(
            TableAvailable: true,
            DocumentId: columns.Contains("project_intake_document_id"),
            ProjectId: columns.Contains("project_id"),
            DocumentType: columns.Contains("document_type"),
            DocumentCategory: columns.Contains("document_category"),
            OriginalFileName: columns.Contains("original_file_name"),
            StoredFileName: columns.Contains("stored_file_name"),
            StoragePath: columns.Contains("storage_path"),
            ContentType: columns.Contains("content_type"),
            SizeBytes: columns.Contains("size_bytes"),
            EngineeringVisible: columns.Contains("engineering_visible"),
            AiTimesheetContextEnabled: columns.Contains("ai_timesheet_context_enabled"),
            ExtractionStatus: columns.Contains("extraction_status"),
            ContextSummary: columns.Contains("ai_context_summary"),
            ContextProcessedAt: columns.Contains("ai_context_last_processed_at"),
            UploadedAt: columns.Contains("uploaded_at"),
            UploadSource: columns.Contains("upload_source"),
            IsActive: columns.Contains("is_active"));
    }

    private static async Task<AccessContext> LoadAccessAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                u.user_id,
                COALESCE(u.display_name, ''),
                COALESCE(u.email, ''),
                COALESCE(string_agg(DISTINCT r.role_code, ',' ORDER BY r.role_code), ''),
                COALESCE(u.is_active, FALSE)
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            WHERE u.user_id = @user_id
            GROUP BY u.user_id, u.display_name, u.email, u.is_active;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return AccessContext.Empty(userId);
        var roles = reader.GetString(3)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AccessContext(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            roles,
            reader.GetBoolean(4));
    }

    private static async Task<IReadOnlyList<PulseAiAuthorizedDocumentSource>> LoadAuthorizedSourcesAsync(
        NpgsqlConnection connection,
        AccessContext access,
        DocumentSchema schema,
        string projectCode,
        string category,
        string extractionStatus,
        int limit,
        CancellationToken cancellationToken)
    {
        var sql = BuildSourceSql(schema, singleDocument: false);
        await using var command = new NpgsqlCommand(sql, connection);
        AddAccessParameters(command, access);
        command.Parameters.AddWithValue("project_code", projectCode);
        command.Parameters.AddWithValue("document_category", category);
        command.Parameters.AddWithValue("extraction_status", extractionStatus);
        command.Parameters.AddWithValue("limit", limit);
        return await ReadSourcesAsync(command, access, cancellationToken);
    }

    private static async Task<PulseAiAuthorizedDocumentSource?> LoadAuthorizedSourceAsync(
        NpgsqlConnection connection,
        AccessContext access,
        DocumentSchema schema,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var sql = BuildSourceSql(schema, singleDocument: true);
        await using var command = new NpgsqlCommand(sql, connection);
        AddAccessParameters(command, access);
        command.Parameters.AddWithValue("document_id", documentId);
        var sources = await ReadSourcesAsync(command, access, cancellationToken);
        return sources.FirstOrDefault();
    }

    private static string BuildSourceSql(DocumentSchema schema, bool singleDocument)
    {
        var category = schema.DocumentCategory
            ? "COALESCE(d.document_category, d.document_type, 'other')"
            : "COALESCE(d.document_type, 'other')";
        var contentType = schema.ContentType ? "d.content_type" : "NULL::text";
        var sizeBytes = schema.SizeBytes ? "COALESCE(d.size_bytes, 0)::bigint" : "0::bigint";
        var engineering = schema.EngineeringVisible ? "COALESCE(d.engineering_visible, FALSE)" : "FALSE";
        var aiFlag = schema.AiTimesheetContextEnabled ? "COALESCE(d.ai_timesheet_context_enabled, FALSE)" : "FALSE";
        var extraction = schema.ExtractionStatus ? "COALESCE(d.extraction_status, 'not_started')" : "'not_available'::text";
        var summaryReady = schema.ContextSummary && schema.ExtractionStatus
            ? "(NULLIF(BTRIM(d.ai_context_summary), '') IS NOT NULL AND LOWER(COALESCE(d.extraction_status, '')) IN ('completed','ready','indexed','processed'))"
            : "FALSE";
        var processed = schema.ContextProcessedAt ? "d.ai_context_last_processed_at" : "NULL::timestamptz";
        var uploadedAt = schema.UploadedAt ? "d.uploaded_at" : "NOW()";
        var uploadSource = schema.UploadSource ? "COALESCE(d.upload_source, 'manual')" : "'manual'::text";
        var active = schema.IsActive ? "d.is_active = TRUE" : "TRUE";
        var selector = singleDocument
            ? "AND d.project_intake_document_id = @document_id"
            : "AND (@project_code = '' OR LOWER(p.project_code) = LOWER(@project_code))\n"
                + "AND (@document_category = '' OR LOWER(" + category + ") = LOWER(@document_category))\n"
                + "AND (@extraction_status = '' OR LOWER(" + extraction + ") = LOWER(@extraction_status))";
        var limit = singleDocument ? "LIMIT 1" : "LIMIT @limit";

        return $"""
            SELECT
                d.project_intake_document_id,
                d.project_id,
                p.project_code,
                p.project_name,
                COALESCE(c.client_name, 'No customer'),
                COALESCE(d.document_type, 'other'),
                {category},
                d.original_file_name,
                d.stored_file_name,
                d.storage_path,
                {contentType},
                {sizeBytes},
                {engineering},
                {aiFlag},
                {extraction},
                {summaryReady},
                {processed},
                {uploadedAt},
                {uploadSource}
            FROM project_intake_documents d
            JOIN projects p ON p.project_id = d.project_id
            LEFT JOIN clients c ON c.client_id = p.client_id
            WHERE {active}
              AND d.project_id IS NOT NULL
              AND {engineering} = TRUE
              {selector}
              AND (
                  @is_broad = TRUE
                  OR p.project_manager_user_id = @user_id
                  OR EXISTS (
                      SELECT 1
                      FROM project_assignments pa
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
            ORDER BY p.project_code, LOWER({category}), {uploadedAt} DESC
            {limit};
            """;
    }

    private static async Task<IReadOnlyList<PulseAiAuthorizedDocumentSource>> ReadSourcesAsync(
        NpgsqlCommand command,
        AccessContext access,
        CancellationToken cancellationToken)
    {
        try
        {
            var sources = new List<PulseAiAuthorizedDocumentSource>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var category = reader.GetString(6).Trim().ToLowerInvariant();
                sources.Add(new PulseAiAuthorizedDocumentSource(
                    DocumentId: reader.GetGuid(0),
                    ProjectId: reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    ProjectCode: reader.GetString(2),
                    ProjectName: reader.GetString(3),
                    CustomerName: reader.GetString(4),
                    DocumentType: reader.GetString(5),
                    DocumentCategory: category,
                    OriginalFileName: reader.GetString(7),
                    StoredFileName: reader.GetString(8),
                    StoragePath: reader.GetString(9),
                    ContentType: reader.IsDBNull(10) ? null : reader.GetString(10),
                    SizeBytes: reader.GetInt64(11),
                    EngineeringVisible: reader.GetBoolean(12),
                    AiTimesheetContextEnabled: reader.GetBoolean(13),
                    ExtractionStatus: reader.GetString(14).Trim().ToLowerInvariant(),
                    ExistingContextSummaryReady: reader.GetBoolean(15),
                    ContextLastProcessedAt: reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16),
                    UploadedAt: reader.GetFieldValue<DateTimeOffset>(17),
                    UploadSource: reader.GetString(18),
                    AccessScope: access.ScopeLabel,
                    Classification: Classification(category),
                    RoleCodes: access.RoleCodes.OrderBy(value => value).ToArray()));
            }
            return sources;
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            return [];
        }
    }

    private static void AddAccessParameters(NpgsqlCommand command, AccessContext access)
    {
        command.Parameters.AddWithValue("is_broad", access.IsBroadDocumentScope);
        command.Parameters.AddWithValue("user_id", access.UserId);
    }

    private static async Task<DocumentCounts> CountAuthorizedDocumentsAsync(
        NpgsqlConnection connection,
        AccessContext access,
        DocumentSchema schema,
        CancellationToken cancellationToken)
    {
        var category = schema.DocumentCategory
            ? "COALESCE(d.document_category, d.document_type, 'other')"
            : "COALESCE(d.document_type, 'other')";
        var summaryReady = schema.ContextSummary && schema.ExtractionStatus
            ? "(NULLIF(BTRIM(d.ai_context_summary), '') IS NOT NULL AND LOWER(COALESCE(d.extraction_status, '')) IN ('completed','ready','indexed','processed'))"
            : "FALSE";
        var engineering = schema.EngineeringVisible ? "COALESCE(d.engineering_visible, FALSE)" : "FALSE";
        var active = schema.IsActive ? "d.is_active = TRUE" : "TRUE";
        var extensions = string.Join('|', PulseAiPrivateDocumentPipelinePolicy.SupportedExtensions.Select(value => value.TrimStart('.')));
        var sql = $"""
            SELECT
                COUNT(*)::bigint,
                COUNT(*) FILTER (WHERE LOWER(d.original_file_name) ~ '\\.({extensions})$')::bigint,
                COUNT(*) FILTER (WHERE {summaryReady})::bigint
            FROM project_intake_documents d
            JOIN projects p ON p.project_id = d.project_id
            WHERE {active}
              AND d.project_id IS NOT NULL
              AND {engineering} = TRUE
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
        AddAccessParameters(command, access);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new DocumentCounts(0, 0, 0);
        return new DocumentCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task<IReadOnlyList<string>> LoadVersionAuthorityQuestionsAsync(
        NpgsqlConnection connection,
        PulseAiAuthorizedDocumentSource source,
        AccessContext access,
        CancellationToken cancellationToken)
    {
        if (source.ProjectId is null) return ["Document is not linked to an authoritative project record."];
        const string sql = """
            SELECT
                LOWER(COALESCE(document_category, document_type, 'other')) AS category,
                COUNT(*)::int,
                MAX(uploaded_at) AS latest_uploaded_at
            FROM project_intake_documents
            WHERE project_id = @project_id
              AND is_active = TRUE
              AND engineering_visible = TRUE
              AND LOWER(COALESCE(document_category, document_type, 'other')) = LOWER(@category)
            GROUP BY LOWER(COALESCE(document_category, document_type, 'other'));
            """;
        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", source.ProjectId.Value);
            command.Parameters.AddWithValue("category", source.DocumentCategory);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return [];
            var count = reader.GetInt32(1);
            if (count <= 1) return [];
            return
            [
                $"{count} active engineering-visible {source.DocumentCategory.ToUpperInvariant()} documents exist for this project.",
                "Define the authoritative version by approval state, effective date, revision identifier, or explicit supersession before indexing all versions.",
                "Do not silently treat upload time alone as contractual authority."
            ];
        }
        catch (PostgresException exception) when (exception.SqlState is "42P01" or "42703")
        {
            return ["Version-authority evidence could not be evaluated from the current document schema."];
        }
    }

    private static string Classification(string category) => category switch
    {
        "sow" or "statement_of_work" or "gsd" or "global_solution_design" or "contract" or "rate" or "pricing" =>
            "restricted_internal_document",
        "architecture" or "design" or "order" or "quote" or "proposal" =>
            "confidential_project_document",
        _ => "internal_project_document"
    };

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
            CommandTimeout = 18
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
        UnauthorizedAccessException => "storage_access_denied",
        IOException => "storage_io_failure",
        OperationCanceledException => "cancelled",
        _ => "private_document_pipeline_failure"
    };

    private sealed record AccessContext(
        Guid UserId,
        string DisplayName,
        string Email,
        IReadOnlySet<string> RoleCodes,
        bool IsActive)
    {
        public bool IsBroadDocumentScope => RoleCodes.Overlaps(BroadDocumentRoles);
        public bool IsProjectManager => RoleCodes.Overlaps(ProjectManagementRoles);
        public string ScopeLabel => IsBroadDocumentScope
            ? "organization_document_scope"
            : IsProjectManager
                ? "managed_and_assigned_project_scope"
                : "assigned_project_scope";
        public static AccessContext Empty(Guid userId) =>
            new(userId, string.Empty, string.Empty, new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
    }

    private sealed record DocumentSchema(
        bool TableAvailable,
        bool DocumentId,
        bool ProjectId,
        bool DocumentType,
        bool DocumentCategory,
        bool OriginalFileName,
        bool StoredFileName,
        bool StoragePath,
        bool ContentType,
        bool SizeBytes,
        bool EngineeringVisible,
        bool AiTimesheetContextEnabled,
        bool ExtractionStatus,
        bool ContextSummary,
        bool ContextProcessedAt,
        bool UploadedAt,
        bool UploadSource,
        bool IsActive)
    {
        public bool RequiredColumnsAvailable =>
            TableAvailable
            && DocumentId
            && ProjectId
            && DocumentType
            && OriginalFileName
            && StoredFileName
            && StoragePath
            && EngineeringVisible
            && UploadedAt;
        public static DocumentSchema Missing =>
            new(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
    }

    private sealed record DocumentCounts(long All, long Supported, long ContextReady);
}
