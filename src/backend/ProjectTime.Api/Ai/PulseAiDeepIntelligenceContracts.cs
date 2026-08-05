namespace ProjectTime.Api.Ai;

public sealed record PulseAiTimesheetGroundingInput(
    DateOnly? WorkDate,
    string? TimeType,
    string? RowType,
    string? RowLabel,
    string? ProjectCode,
    string? ProjectName,
    string? TaskCode,
    string? TaskName,
    string? CurrentDescription,
    Guid? ProjectId = null,
    Guid? TaskId = null,
    Guid? AssignmentId = null);

public sealed record PulseAiFlowHiveGroundingInput(
    string? ProjectCode,
    string? ProjectName,
    string? RequestedOutcome);

public sealed record PulseAiGroundingDocument(
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string DocumentType,
    string DocumentCategory,
    string OriginalFileName,
    string? ContentType,
    long SizeBytes,
    bool EngineeringVisible,
    bool AiTimesheetContextEnabled,
    string ExtractionStatus,
    string? ContextSummary,
    DateTimeOffset? ContextLastProcessedAt,
    DateTimeOffset UploadedAt,
    string UploadSource,
    int RetrievalPriority)
{
    public bool SummaryReady =>
        !string.IsNullOrWhiteSpace(ContextSummary)
        && ExtractionStatus is "completed" or "ready" or "indexed" or "processed";

    public object ToEvidence() => new
    {
        documentId = DocumentId,
        projectId = ProjectId,
        projectCode = ProjectCode,
        projectName = ProjectName,
        documentType = DocumentType,
        documentCategory = DocumentCategory,
        originalFileName = OriginalFileName,
        contentType = ContentType,
        sizeBytes = SizeBytes,
        engineeringVisible = EngineeringVisible,
        aiTimesheetContextEnabled = AiTimesheetContextEnabled,
        extractionStatus = ExtractionStatus,
        summaryReady = SummaryReady,
        contextLastProcessedAt = ContextLastProcessedAt,
        uploadedAt = UploadedAt,
        uploadSource = UploadSource,
        retrievalPriority = RetrievalPriority,
        sourceVersion = $"{OriginalFileName}@{UploadedAt:O}"
    };
}

public sealed record PulseAiGroundingContext(
    string Status,
    string Purpose,
    Guid EffectiveUserId,
    Guid? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string? CustomerName,
    string? ProjectStatus,
    string? TaskCode,
    string? TaskName,
    string? TaskDescription,
    string? RequestNumber,
    string? RequestFunction,
    string? RequestStatus,
    string AccessScope,
    bool ProjectResolved,
    bool Authorized,
    IReadOnlyList<string> RoleCodes,
    IReadOnlyList<PulseAiGroundingDocument> Documents,
    IReadOnlyList<string> ScopeThemes,
    IReadOnlyList<string> MissingInputs,
    IReadOnlyList<string> Conflicts,
    decimal CoverageScore,
    string CoverageLevel,
    DateTimeOffset GeneratedAt,
    string PrivacyBoundary,
    string ExternalProviderPolicy,
    string? DiagnosticCode = null,
    Guid? TaskId = null,
    Guid? AssignmentId = null)
{
    public bool HasDocuments => Documents.Count > 0;
    public bool HasReadyPrivateContext => Documents.Any(document => document.SummaryReady);

    public string DocumentTypeLabel
    {
        get
        {
            var categories = Documents
                .Select(document => document.DocumentCategory)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return categories.Length == 0
                ? "approved project documentation"
                : string.Join(" and ", categories.Select(value => value.ToUpperInvariant()));
        }
    }

    public object ToPublicEvidence() => new
    {
        status = Status,
        purpose = Purpose,
        effectiveUserId = EffectiveUserId,
        project = new
        {
            projectId = ProjectId,
            projectCode = ProjectCode,
            projectName = ProjectName,
            customerName = CustomerName,
            projectStatus = ProjectStatus,
            resolved = ProjectResolved,
            authorized = Authorized,
            accessScope = AccessScope
        },
        selectedWork = new
        {
            taskId = TaskId,
            assignmentId = AssignmentId,
            taskCode = TaskCode,
            taskName = TaskName,
            taskDescription = TaskDescription,
            requestNumber = RequestNumber,
            requestFunction = RequestFunction,
            requestStatus = RequestStatus
        },
        sourceCoverage = new
        {
            score = CoverageScore,
            level = CoverageLevel,
            documentCount = Documents.Count,
            readyPrivateContextCount = Documents.Count(document => document.SummaryReady),
            sowCount = Documents.Count(document => document.DocumentCategory.Equals("sow", StringComparison.OrdinalIgnoreCase)),
            gsdCount = Documents.Count(document => document.DocumentCategory.Equals("gsd", StringComparison.OrdinalIgnoreCase)),
            scopeThemes = ScopeThemes,
            missingInputs = MissingInputs,
            conflicts = Conflicts
        },
        documents = Documents.Select(document => document.ToEvidence()).ToArray(),
        roles = RoleCodes,
        generatedAt = GeneratedAt,
        privacy = new
        {
            boundary = PrivacyBoundary,
            externalProviderPolicy = ExternalProviderPolicy,
            rawDocumentTextReturned = false,
            rawDocumentTextSentExternally = false
        },
        diagnosticCode = DiagnosticCode
    };
}

public sealed record PulseAiPrivateRuntimeReadiness(
    string Status,
    bool DatabaseConfigured,
    bool DocumentTableAvailable,
    bool EngineeringVisibilityAvailable,
    bool TimesheetContextFlagAvailable,
    bool ExtractionStatusAvailable,
    bool ContextSummaryAvailable,
    bool ContextProcessedAtAvailable,
    bool PrivateInferenceEndpointConfigured,
    bool PrivateEmbeddingEndpointConfigured,
    bool PrivateVectorIndexConfigured,
    bool SanitizedExternalEscalationEnabled,
    long AuthorizedDocumentCount,
    long AuthorizedAiContextDocumentCount,
    long AuthorizedReadyContextDocumentCount,
    IReadOnlyList<string> ReadyCapabilities,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> MissingConfiguration,
    DateTimeOffset GeneratedAt,
    string? DiagnosticCode = null);

public sealed record PulseAiToolDescriptor(
    string Code,
    string DisplayName,
    string Domain,
    string[] OwningModules,
    string[] Routes,
    string Availability,
    string AccessPolicy,
    string DataClassification,
    string CalculationPolicy,
    string MutationPolicy,
    string EvidencePolicy,
    string[] SupportedQuestions);

public sealed record PulseAiKnowledgeAnswer(
    string Title,
    string Summary,
    IReadOnlyList<string> DetailedSteps,
    IReadOnlyList<string> ImportantRules,
    IReadOnlyList<string> SourceModules,
    IReadOnlyList<string> NavigationTargets);

public sealed record PulseAiQuestionPlan(
    string Status,
    string Mode,
    string Question,
    string DetailLevel,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> OwningModules,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> FiltersToResolve,
    IReadOnlyList<string> DeterministicCalculations,
    IReadOnlyList<string> AnswerSections,
    IReadOnlyList<string> ExecutionSteps,
    IReadOnlyList<string> PrivacyControls,
    IReadOnlyList<string> MissingInputs,
    PulseAiKnowledgeAnswer? DirectKnowledgeAnswer,
    object? SemanticQuery,
    DateTimeOffset GeneratedAt);

public sealed record PulseAiSanitizationRequest(
    string? Purpose,
    string? Content,
    string? Classification,
    string[]? SensitiveTerms,
    bool AcknowledgePreviewOnly = false);

public sealed record PulseAiRedactionEvidence(
    string Category,
    int Count,
    string Replacement);

public sealed record PulseAiSanitizationResult(
    string Status,
    string Purpose,
    string Classification,
    string SanitizedCapsule,
    int OriginalLength,
    int SanitizedLength,
    IReadOnlyList<PulseAiRedactionEvidence> Redactions,
    IReadOnlyList<string> RemovedCategories,
    IReadOnlyList<string> RemainingAllowedContext,
    bool ExternalExecutionAuthorized,
    IReadOnlyList<string> BlockedReasons,
    DateTimeOffset GeneratedAt);
