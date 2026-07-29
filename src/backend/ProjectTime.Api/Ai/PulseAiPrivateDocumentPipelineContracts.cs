namespace ProjectTime.Api.Ai;

public static class PulseAiPrivateDocumentPipelinePolicy
{
    public const string ContractVersion = "pulse-ai-private-document-pipeline-v1-20260729";
    public const string PrivacyBoundary = "private_pulse_runtime_only";
    public const string ExternalProviderPolicy = "raw_document_extraction_chunks_and_embeddings_never_sent_to_external_provider";
    public const long DefaultMaximumFileBytes = 25L * 1024L * 1024L;
    public const int DefaultMaximumPages = 500;
    public const int DefaultMaximumCharacters = 2_000_000;
    public const int DefaultMaximumSections = 1_000;
    public const int DefaultMaximumChunks = 1_500;
    public const int DefaultChunkCharacters = 2_400;
    public const int DefaultChunkOverlapCharacters = 280;

    public static readonly string[] SupportedExtensions =
    [
        ".pdf",
        ".docx",
        ".pptx",
        ".xlsx",
        ".txt",
        ".md",
        ".csv",
        ".json",
        ".xml",
        ".html",
        ".htm"
    ];

    public static readonly string[] ExplicitlyBlockedExtensions =
    [
        ".exe", ".dll", ".com", ".bat", ".cmd", ".ps1", ".sh",
        ".msi", ".scr", ".js", ".vbs", ".jar", ".apk", ".iso",
        ".docm", ".xlsm", ".pptm", ".xll", ".zip", ".7z", ".rar"
    ];

    public static readonly string[] RequiredSecurityMetadata =
    [
        "document_id",
        "project_id",
        "project_code",
        "customer_scope",
        "document_category",
        "document_version",
        "classification",
        "engineering_visible",
        "ai_timesheet_context_enabled",
        "authorized_user_or_role_scope",
        "source_checksum",
        "citation_anchor",
        "uploaded_at",
        "processed_at"
    ];

    public static readonly string[] RequiredProductionServices =
    [
        "malware_scanner_with_verifiable_result",
        "private_document_storage_with_path_confinement",
        "native_pdf_docx_pptx_xlsx_and_text_extraction",
        "ocr_adapter_for_image_only_documents",
        "private_embedding_endpoint",
        "permission_scoped_hybrid_vector_index",
        "document_version_authority",
        "retention_and_revocation_worker",
        "evaluation_and_quality_monitoring",
        "audited_processing_queue"
    ];
}

public sealed record PulseAiDocumentPipelineOptions(
    string UploadRoot,
    bool ExtractionPreviewEnabled,
    bool MalwareScanAttested,
    string MalwareScannerMode,
    bool OcrEndpointConfigured,
    bool PrivateEmbeddingEndpointConfigured,
    bool PrivateVectorIndexConfigured,
    long MaximumFileBytes,
    int MaximumPages,
    int MaximumCharacters,
    int MaximumSections,
    int MaximumChunks,
    int ChunkCharacters,
    int ChunkOverlapCharacters)
{
    public static PulseAiDocumentPipelineOptions FromEnvironment()
    {
        var uploadRoot = Environment.GetEnvironmentVariable("PROJECTPULSE_UPLOAD_ROOT");
        if (string.IsNullOrWhiteSpace(uploadRoot))
        {
            uploadRoot = "/opt/project-time-platform/app/uploads";
        }

        return new PulseAiDocumentPipelineOptions(
            UploadRoot: Path.GetFullPath(uploadRoot),
            ExtractionPreviewEnabled: Boolean("PROJECTPULSE_PULSE_AI_DOCUMENT_EXTRACTION_PREVIEW_ENABLED", false),
            MalwareScanAttested: Boolean("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCAN_ATTESTED", false),
            MalwareScannerMode: Clean(Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_DOCUMENT_MALWARE_SCANNER_MODE"), 80, "not_configured"),
            OcrEndpointConfigured: HasValue("PROJECTPULSE_PRIVATE_OCR_ENDPOINT"),
            PrivateEmbeddingEndpointConfigured: HasValue("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT")
                && HasValue("PROJECTPULSE_PRIVATE_EMBEDDING_MODEL"),
            PrivateVectorIndexConfigured: HasValue("PROJECTPULSE_PRIVATE_VECTOR_INDEX"),
            MaximumFileBytes: Long("PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_BYTES", PulseAiPrivateDocumentPipelinePolicy.DefaultMaximumFileBytes, 1_048_576, 104_857_600),
            MaximumPages: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_PAGES", PulseAiPrivateDocumentPipelinePolicy.DefaultMaximumPages, 1, 2_000),
            MaximumCharacters: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_CHARACTERS", PulseAiPrivateDocumentPipelinePolicy.DefaultMaximumCharacters, 10_000, 10_000_000),
            MaximumSections: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_SECTIONS", PulseAiPrivateDocumentPipelinePolicy.DefaultMaximumSections, 1, 10_000),
            MaximumChunks: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_MAX_CHUNKS", PulseAiPrivateDocumentPipelinePolicy.DefaultMaximumChunks, 1, 10_000),
            ChunkCharacters: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_CHUNK_CHARACTERS", PulseAiPrivateDocumentPipelinePolicy.DefaultChunkCharacters, 400, 12_000),
            ChunkOverlapCharacters: Integer("PROJECTPULSE_PULSE_AI_DOCUMENT_CHUNK_OVERLAP", PulseAiPrivateDocumentPipelinePolicy.DefaultChunkOverlapCharacters, 0, 2_000));
    }

    private static bool HasValue(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static int Integer(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static long Long(string name, long fallback, long minimum, long maximum) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static string Clean(string? value, int maximumLength, string fallback)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) return fallback;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }
}

public sealed record PulseAiDocumentPipelineReadiness(
    string Status,
    string ContractVersion,
    bool DatabaseConfigured,
    bool DocumentSchemaAvailable,
    bool StorageRootConfigured,
    bool StorageRootExists,
    bool ExtractionPreviewEnabled,
    bool MalwareScanAttested,
    string MalwareScannerMode,
    bool NativePdfExtractionAvailable,
    bool NativeOpenXmlExtractionAvailable,
    bool NativeTextExtractionAvailable,
    bool OcrEndpointConfigured,
    bool PrivateEmbeddingEndpointConfigured,
    bool PrivateVectorIndexConfigured,
    long AuthorizedDocumentCount,
    long SupportedDocumentCount,
    long ExtractionReadyDocumentCount,
    IReadOnlyList<string> SupportedExtensions,
    IReadOnlyList<string> ReadyCapabilities,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> MissingConfiguration,
    DateTimeOffset GeneratedAt,
    string? DiagnosticCode = null);

public sealed record PulseAiDocumentInventoryItem(
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    string DocumentType,
    string DocumentCategory,
    string OriginalFileName,
    string? ContentType,
    long SizeBytes,
    string Extension,
    bool EngineeringVisible,
    bool AiTimesheetContextEnabled,
    string ExtractionStatus,
    bool ExistingContextSummaryReady,
    DateTimeOffset? ContextLastProcessedAt,
    DateTimeOffset UploadedAt,
    string UploadSource,
    string AccessScope,
    bool SupportedByNativePipeline,
    bool StoredFileExists,
    bool StoredPathConfined,
    bool ProductionAdmissionReady,
    IReadOnlyList<string> Blockers)
{
    public object ToPublicEvidence() => new
    {
        documentId = DocumentId,
        projectId = ProjectId,
        projectCode = ProjectCode,
        projectName = ProjectName,
        customerName = CustomerName,
        documentType = DocumentType,
        documentCategory = DocumentCategory,
        originalFileName = OriginalFileName,
        contentType = ContentType,
        sizeBytes = SizeBytes,
        extension = Extension,
        engineeringVisible = EngineeringVisible,
        aiTimesheetContextEnabled = AiTimesheetContextEnabled,
        extractionStatus = ExtractionStatus,
        existingContextSummaryReady = ExistingContextSummaryReady,
        contextLastProcessedAt = ContextLastProcessedAt,
        uploadedAt = UploadedAt,
        uploadSource = UploadSource,
        accessScope = AccessScope,
        supportedByNativePipeline = SupportedByNativePipeline,
        storedFileExists = StoredFileExists,
        storedPathConfined = StoredPathConfined,
        productionAdmissionReady = ProductionAdmissionReady,
        blockers = Blockers
    };
}

public sealed record PulseAiDocumentSafetyAssessment(
    string Status,
    string Extension,
    string DetectedFormat,
    bool ExtensionAllowed,
    bool SignatureMatchesExtension,
    bool SizeWithinLimit,
    bool PathConfined,
    bool IsRegularFile,
    bool ReparsePointDetected,
    bool MacroEnabledFormat,
    bool ArchiveBombRiskDetected,
    bool MalwareScanAttested,
    string MalwareScannerMode,
    long FileSizeBytes,
    string SourceSha256,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings)
{
    public bool AllowedForPreview =>
        ExtensionAllowed
        && SignatureMatchesExtension
        && SizeWithinLimit
        && PathConfined
        && IsRegularFile
        && !ReparsePointDetected
        && !MacroEnabledFormat
        && !ArchiveBombRiskDetected
        && MalwareScanAttested
        && Blockers.Count == 0;
}

public sealed record PulseAiExtractedSection(
    int SectionIndex,
    string Anchor,
    string Title,
    string Text,
    int? PageNumber,
    string? SheetName,
    int CharacterCount,
    string TextSha256)
{
    public object ToPublicEvidence() => new
    {
        sectionIndex = SectionIndex,
        anchor = Anchor,
        title = Title,
        pageNumber = PageNumber,
        sheetName = SheetName,
        characterCount = CharacterCount,
        textSha256 = TextSha256,
        rawTextReturned = false
    };
}

public sealed record PulseAiDocumentExtractionResult(
    string Status,
    Guid DocumentId,
    string OriginalFileName,
    string DetectedFormat,
    string ExtractionMethod,
    int PageCount,
    int SectionCount,
    int CharacterCount,
    int EstimatedTokenCount,
    bool OcrRequired,
    string SourceSha256,
    PulseAiDocumentSafetyAssessment Safety,
    IReadOnlyList<PulseAiExtractedSection> Sections,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers,
    DateTimeOffset GeneratedAt)
{
    public bool ExtractionSucceeded => Status == "extraction_preview_ready";

    public object ToPublicEvidence() => new
    {
        status = Status,
        documentId = DocumentId,
        originalFileName = OriginalFileName,
        detectedFormat = DetectedFormat,
        extractionMethod = ExtractionMethod,
        pageCount = PageCount,
        sectionCount = SectionCount,
        characterCount = CharacterCount,
        estimatedTokenCount = EstimatedTokenCount,
        ocrRequired = OcrRequired,
        sourceSha256 = SourceSha256,
        safety = Safety,
        sections = Sections.Select(section => section.ToPublicEvidence()).ToArray(),
        warnings = Warnings,
        blockers = Blockers,
        generatedAt = GeneratedAt,
        rawDocumentTextReturned = false,
        rawDocumentTextSentExternally = false
    };
}

public sealed record PulseAiDocumentChunk(
    string ChunkId,
    Guid DocumentId,
    int ChunkIndex,
    string Anchor,
    string Title,
    int? PageNumber,
    string? SheetName,
    string Text,
    int CharacterCount,
    int EstimatedTokenCount,
    string TextSha256,
    string SourceSha256)
{
    public object ToPublicEvidence() => new
    {
        chunkId = ChunkId,
        documentId = DocumentId,
        chunkIndex = ChunkIndex,
        anchor = Anchor,
        title = Title,
        pageNumber = PageNumber,
        sheetName = SheetName,
        characterCount = CharacterCount,
        estimatedTokenCount = EstimatedTokenCount,
        textSha256 = TextSha256,
        sourceSha256 = SourceSha256,
        rawTextReturned = false
    };
}

public sealed record PulseAiIndexProjectionRecord(
    string ChunkId,
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    string DocumentCategory,
    string DocumentVersion,
    string Classification,
    bool EngineeringVisible,
    bool AiTimesheetContextEnabled,
    string AccessScope,
    string CitationAnchor,
    int? PageNumber,
    string? SheetName,
    string SourceSha256,
    string TextSha256,
    int CharacterCount,
    int EstimatedTokenCount,
    string EmbeddingStatus,
    string IndexStatus,
    DateTimeOffset PreparedAt)
{
    public object ToPublicEvidence() => new
    {
        chunkId = ChunkId,
        documentId = DocumentId,
        projectId = ProjectId,
        projectCode = ProjectCode,
        projectName = ProjectName,
        customerName = CustomerName,
        documentCategory = DocumentCategory,
        documentVersion = DocumentVersion,
        classification = Classification,
        engineeringVisible = EngineeringVisible,
        aiTimesheetContextEnabled = AiTimesheetContextEnabled,
        accessScope = AccessScope,
        citationAnchor = CitationAnchor,
        pageNumber = PageNumber,
        sheetName = SheetName,
        sourceSha256 = SourceSha256,
        textSha256 = TextSha256,
        characterCount = CharacterCount,
        estimatedTokenCount = EstimatedTokenCount,
        embeddingStatus = EmbeddingStatus,
        indexStatus = IndexStatus,
        preparedAt = PreparedAt,
        vectorReturned = false,
        rawTextReturned = false
    };
}

public sealed record PulseAiDocumentProcessingPreview(
    string Status,
    string ContractVersion,
    PulseAiDocumentInventoryItem Document,
    PulseAiDocumentExtractionResult Extraction,
    IReadOnlyList<PulseAiDocumentChunk> Chunks,
    IReadOnlyList<PulseAiIndexProjectionRecord> IndexProjection,
    IReadOnlyList<string> VersionAuthorityQuestions,
    IReadOnlyList<string> ProductionBlockers,
    DateTimeOffset GeneratedAt)
{
    public object ToPublicEvidence() => new
    {
        status = Status,
        contractVersion = ContractVersion,
        document = Document.ToPublicEvidence(),
        extraction = Extraction.ToPublicEvidence(),
        chunks = Chunks.Select(chunk => chunk.ToPublicEvidence()).ToArray(),
        indexProjection = IndexProjection.Select(record => record.ToPublicEvidence()).ToArray(),
        versionAuthorityQuestions = VersionAuthorityQuestions,
        productionBlockers = ProductionBlockers,
        generatedAt = GeneratedAt,
        stateChanged = false,
        databaseChanged = false,
        embeddingExecuted = false,
        vectorIndexChanged = false,
        externalProviderCalled = false
    };
}

internal sealed record PulseAiAuthorizedDocumentSource(
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    string DocumentType,
    string DocumentCategory,
    string OriginalFileName,
    string StoredFileName,
    string StoragePath,
    string? ContentType,
    long SizeBytes,
    bool EngineeringVisible,
    bool AiTimesheetContextEnabled,
    string ExtractionStatus,
    bool ExistingContextSummaryReady,
    DateTimeOffset? ContextLastProcessedAt,
    DateTimeOffset UploadedAt,
    string UploadSource,
    string AccessScope,
    string Classification,
    IReadOnlyList<string> RoleCodes);
