using System.Text.Json.Serialization;

namespace ProjectTime.Api.Ai;

public static class PulseAiPrivateRagPolicy
{
    public const string ContractVersion = "pulse-ai-private-rag-v1-20260729";
    public const string RetrievalContractVersion = "pulse-ai-private-retrieval-v1-20260729";
    public const string PromptContractVersion = "pulse-ai-private-answer-v1-20260729";
    public const string MigrationId = "053_pulse_ai_private_rag_orchestration";
    public const string PrivacyBoundary = "private_pulse_runtime_only";

    public const string HelpSearchFeature = "system_help_search";
    public const string TimesheetFeature = "timesheet_document_grounding";
    public const string FlowHiveFeature = "flowhive_document_planning";

    public static readonly string[] AllowedDetailLevels =
    [
        "standard",
        "detailed",
        "comprehensive",
        "executive_and_detailed"
    ];

    public static readonly string[] FlowHiveCategories =
    [
        "sow",
        "statement_of_work",
        "gsd",
        "global_solution_design",
        "architecture",
        "design",
        "order",
        "order_form",
        "quote",
        "proposal",
        "supporting"
    ];

    public static readonly string[] AnswerSections =
    [
        "directConclusion",
        "scopeAndFilters",
        "detailedAnalysis",
        "sourceEvidence",
        "calculations",
        "knownUnknownAndStaleValues",
        "assumptions",
        "conflicts",
        "limitations",
        "risksAndImplications",
        "recommendedActions",
        "navigation",
        "dataAsOf",
        "confidence"
    ];
}

public sealed record PulseAiPrivateRagOptions(
    bool Enabled,
    string InferenceEndpoint,
    string InferenceModel,
    string InferenceBearerToken,
    int MaximumRetrievedChunks,
    int MaximumCandidateChunks,
    int MaximumContextCharacters,
    int MaximumQuestionCharacters,
    int MaximumAnswerCharacters,
    int MaximumOutputTokens,
    decimal MinimumEvidenceScore,
    decimal MinimumConfidence,
    decimal LexicalWeight,
    decimal SemanticWeight,
    bool RequirePrivateModelForDocumentAnswers,
    bool PersistAnswerText,
    IReadOnlyList<string> PrivateHostAllowlist)
{
    public bool InferenceConfigured =>
        !string.IsNullOrWhiteSpace(InferenceEndpoint)
        && !string.IsNullOrWhiteSpace(InferenceModel);

    public static PulseAiPrivateRagOptions FromEnvironment()
    {
        var hostAllowlist = (Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_ENDPOINT_HOST_ALLOWLIST") ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (hostAllowlist.Length == 0)
        {
            hostAllowlist = PulseAiPrivateRuntimePolicy.PrivateHostSuffixDefaults;
        }

        var lexicalWeight = Decimal("PROJECTPULSE_PULSE_AI_RAG_LEXICAL_WEIGHT", 0.45m, 0m, 1m);
        var semanticWeight = Decimal("PROJECTPULSE_PULSE_AI_RAG_SEMANTIC_WEIGHT", 0.55m, 0m, 1m);
        var total = lexicalWeight + semanticWeight;
        if (total <= 0)
        {
            lexicalWeight = 1m;
            semanticWeight = 0m;
        }
        else
        {
            lexicalWeight /= total;
            semanticWeight /= total;
        }

        return new PulseAiPrivateRagOptions(
            Enabled: Boolean("PROJECTPULSE_PULSE_AI_PRIVATE_RAG_ENABLED", false),
            InferenceEndpoint: Clean(Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT"), 1000),
            InferenceModel: Clean(Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_MODEL"), 240),
            InferenceBearerToken: Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN")?.Trim() ?? string.Empty,
            MaximumRetrievedChunks: Integer("PROJECTPULSE_PULSE_AI_RAG_MAX_CHUNKS", 12, 1, 40),
            MaximumCandidateChunks: Integer("PROJECTPULSE_PULSE_AI_RAG_MAX_CANDIDATES", 120, 10, 1000),
            MaximumContextCharacters: Integer("PROJECTPULSE_PULSE_AI_RAG_MAX_CONTEXT_CHARACTERS", 48_000, 2_000, 240_000),
            MaximumQuestionCharacters: Integer("PROJECTPULSE_PULSE_AI_RAG_MAX_QUESTION_CHARACTERS", 6_000, 200, 40_000),
            MaximumAnswerCharacters: Integer("PROJECTPULSE_PULSE_AI_RAG_MAX_ANSWER_CHARACTERS", 24_000, 500, 120_000),
            MaximumOutputTokens: Integer("PROJECTPULSE_PULSE_AI_RAG_MAX_OUTPUT_TOKENS", 4_000, 200, 16_000),
            MinimumEvidenceScore: Decimal("PROJECTPULSE_PULSE_AI_RAG_MIN_EVIDENCE_SCORE", 0.15m, 0m, 1m),
            MinimumConfidence: Decimal("PROJECTPULSE_PULSE_AI_RAG_MIN_CONFIDENCE", 0.55m, 0m, 1m),
            LexicalWeight: lexicalWeight,
            SemanticWeight: semanticWeight,
            RequirePrivateModelForDocumentAnswers: Boolean("PROJECTPULSE_PULSE_AI_RAG_REQUIRE_PRIVATE_MODEL", true),
            PersistAnswerText: Boolean("PROJECTPULSE_PULSE_AI_RAG_PERSIST_ANSWER_TEXT", true),
            PrivateHostAllowlist: hostAllowlist);
    }

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static int Integer(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static decimal Decimal(string name, decimal fallback, decimal minimum, decimal maximum) =>
        decimal.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }
}

public static class PulseAiRoleAuthority
{
    private static readonly HashSet<string> AdministratorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "SUPERADMINISTRATOR",
        "GLOBAL_ADMINISTRATOR",
        "GLOBALADMINISTRATOR",
        "SYSTEM_ADMINISTRATOR",
        "SYSTEMADMINISTRATOR",
        "ADMINISTRATOR"
    };

    public static bool HasAdministratorRole(IEnumerable<string> roleCodes) =>
        roleCodes.Any(roleCode => AdministratorRoles.Contains(Canonical(roleCode)));

    private static string Canonical(string? value)
    {
        var source = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (source.Length == 0) return string.Empty;
        var builder = new System.Text.StringBuilder(source.Length);
        var separator = false;
        foreach (var character in source)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (separator && builder.Length > 0) builder.Append('_');
                builder.Append(character);
                separator = false;
            }
            else
            {
                separator = true;
            }
        }
        return builder.ToString().Trim('_');
    }
}

public sealed record PulseAiPrivateRagAccess(
    Guid UserId,
    bool IsActive,
    IReadOnlySet<string> RoleCodes,
    IReadOnlySet<string> PermissionCodes)
{
    public bool IsSuperAdministrator => PulseAiRoleAuthority.HasAdministratorRole(RoleCodes);
    public bool IsBroadScope => RoleCodes.Overlaps(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "EXECUTIVE"
    });
    public bool IsProjectManagementLead => RoleCodes.Overlaps(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    });

    public bool CanHelpSearch => IsSuperAdministrator || PermissionCodes.Contains("ASK_PULSE_AI_HELP_SEARCH");
    public bool CanAttachDocuments => IsSuperAdministrator
        || PermissionCodes.Contains(CelarAiConversationAttachmentPolicy.Permission);
    public bool CanTimesheet => IsSuperAdministrator || PermissionCodes.Contains("USE_PULSE_AI_TIMESHEET_GROUNDING");
    public bool CanFlowHive => IsSuperAdministrator || PermissionCodes.Contains("USE_PULSE_AI_FLOWHIVE_PLANNING");
    public bool CanViewAudit => IsSuperAdministrator || PermissionCodes.Contains("VIEW_PULSE_AI_ANSWER_AUDIT");
    public bool CanSubmitFeedback => IsSuperAdministrator || PermissionCodes.Contains("SUBMIT_PULSE_AI_FEEDBACK");

    public static PulseAiPrivateRagAccess Empty(Guid userId) =>
        new(
            userId,
            false,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}

public sealed record PulseAiPrivateRetrievalQuery(
    Guid ActualUserId,
    Guid EffectiveUserId,
    string FeatureCode,
    string PurposeCode,
    string Question,
    Guid? ProjectId,
    Guid? TaskId,
    Guid? AssignmentId,
    string? ProjectCode,
    string? ProjectName,
    bool RequireTimesheetFlag,
    bool IncludeProjectDocuments,
    IReadOnlyList<string> AllowedDocumentCategories,
    int MaximumChunks,
    int MaximumCandidates,
    decimal LexicalWeight,
    decimal SemanticWeight,
    decimal MinimumEvidenceScore,
    Guid? ConversationId,
    IReadOnlyList<Guid> AttachmentIds,
    string CorrelationId);

public sealed record PulseAiPrivateRetrievedChunk(
    string ChunkId,
    Guid DocumentVersionId,
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string CustomerName,
    string DocumentCategory,
    string DocumentVersion,
    string Classification,
    string OriginalFileName,
    string CitationAnchor,
    int? PageNumber,
    string? SheetName,
    string SectionTitle,
    string Text,
    string SourceSha256,
    string TextSha256,
    decimal LexicalScore,
    decimal SemanticScore,
    decimal CombinedScore,
    DateTimeOffset ProcessedAt,
    int RankOrder)
{
    public object ToCitationEvidence() => new
    {
        citationId = RankOrder,
        documentId = DocumentId,
        projectId = ProjectId,
        projectCode = ProjectCode,
        projectName = ProjectName,
        customerName = CustomerName,
        documentCategory = DocumentCategory,
        documentVersion = DocumentVersion,
        originalFileName = OriginalFileName,
        citationAnchor = CitationAnchor,
        pageNumber = PageNumber,
        sheetName = SheetName,
        sectionTitle = SectionTitle,
        lexicalScore = LexicalScore,
        semanticScore = SemanticScore,
        combinedScore = CombinedScore,
        sourceSha256 = SourceSha256,
        textSha256 = TextSha256,
        processedAt = ProcessedAt,
        rawChunkTextReturned = false,
        embeddingVectorReturned = false
    };
}

public sealed record PulseAiPrivateRetrievalResult(
    string Status,
    string RetrievalMode,
    Guid? ResolvedProjectId,
    string ResolvedProjectCode,
    string ResolvedProjectName,
    int CandidateCount,
    int AuthorizedCandidateCount,
    IReadOnlyList<PulseAiPrivateRetrievedChunk> Chunks,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> Conflicts,
    decimal CoverageScore,
    DateTimeOffset DataAsOf,
    string DiagnosticCode)
{
    public bool HasEvidence => Chunks.Count > 0;
    public int DocumentCount => Chunks.Select(chunk => chunk.DocumentId).Distinct().Count();
    public int VersionCount => Chunks.Select(chunk => chunk.DocumentVersionId).Distinct().Count();
}

public sealed record PulseAiPrivateAnswerCitation(
    int CitationId,
    Guid DocumentId,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    string DocumentCategory,
    string DocumentVersion,
    string OriginalFileName,
    string CitationAnchor,
    int? PageNumber,
    string? SheetName,
    string SectionTitle,
    decimal RelevanceScore,
    string SourceSha256,
    string TextSha256,
    DateTimeOffset ProcessedAt);

public sealed record PulseAiPrivateDetailedAnswer(
    string DirectConclusion,
    string ExecutiveSummary,
    IReadOnlyList<string> ScopeAndFilters,
    IReadOnlyList<string> DetailedAnalysis,
    IReadOnlyList<string> SourceEvidence,
    IReadOnlyList<string> Calculations,
    IReadOnlyList<string> KnownUnknownAndStaleValues,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> RisksAndImplications,
    IReadOnlyList<string> RecommendedActions,
    IReadOnlyList<string> NavigationTargets,
    IReadOnlyList<int> CitationIds,
    decimal Confidence,
    string ConfidenceExplanation,
    DateTimeOffset DataAsOf);

public sealed record PulseAiPrivateFlowHiveTask(
    string Wbs,
    string Name,
    string Description,
    decimal EstimatedDurationDays,
    IReadOnlyList<string> RequiredRoles,
    IReadOnlyList<string> Predecessors,
    IReadOnlyList<int> CitationIds,
    bool IsAssumption);

public sealed record PulseAiPrivateFlowHiveMilestone(
    string Name,
    string Description,
    string ProposedTiming,
    IReadOnlyList<string> AcceptanceEvidence,
    IReadOnlyList<int> CitationIds,
    bool IsAssumption);

public sealed record PulseAiPrivateFlowHivePlan(
    string Objective,
    IReadOnlyList<PulseAiPrivateFlowHiveTask> Tasks,
    IReadOnlyList<PulseAiPrivateFlowHiveMilestone> Milestones,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> RequiredRoles,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> OutOfScopeItems,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<int> CitationIds,
    decimal Confidence,
    string ConfidenceExplanation);

public sealed record PulseAiPrivateRagAnswer(
    Guid AnswerRunId,
    string Status,
    string FeatureCode,
    string PurposeCode,
    string RetrievalMode,
    string ModelProvider,
    string ModelName,
    Guid? ProjectId,
    string ProjectCode,
    string ProjectName,
    PulseAiPrivateDetailedAnswer? Answer,
    PulseAiPrivateFlowHivePlan? FlowHivePlan,
    IReadOnlyList<PulseAiPrivateAnswerCitation> Citations,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> Conflicts,
    decimal CoverageScore,
    decimal CitationCoverageScore,
    DateTimeOffset DataAsOf,
    string CorrelationId,
    string DiagnosticCode)
{
    public object ToPublicResponse() => new
    {
        answerRunId = AnswerRunId,
        status = Status,
        featureCode = FeatureCode,
        purposeCode = PurposeCode,
        retrievalMode = RetrievalMode,
        modelProvider = ModelProvider,
        modelName = ModelName,
        project = new
        {
            projectId = ProjectId,
            projectCode = ProjectCode,
            projectName = ProjectName
        },
        answer = Answer,
        flowHivePlan = FlowHivePlan,
        citations = Citations,
        warnings = Warnings,
        missingEvidence = MissingEvidence,
        conflicts = Conflicts,
        coverageScore = CoverageScore,
        citationCoverageScore = CitationCoverageScore,
        dataAsOf = DataAsOf,
        correlationId = CorrelationId,
        diagnosticCode = DiagnosticCode,
        privateEvidence = new
        {
            rawChunkTextReturned = false,
            embeddingVectorsReturned = false,
            promptsReturned = false,
            modelSecretsReturned = false,
            rawDocumentsSentToClaudeOrOpenAi = false
        },
        stateChanged = false
    };
}

public sealed record PulseAiPrivateHelpSearchRequest(
    string? Question,
    string? ProjectCode,
    string? ProjectName,
    string? DetailLevel,
    bool IncludeAuthorizedProjectDocuments = false,
    bool IncludeDirectProductKnowledge = true,
    bool UsePrivateModelWhenAvailable = true,
    Guid? ConversationId = null,
    IReadOnlyList<Guid>? AttachmentIds = null);

public sealed record PulseAiPrivateTimesheetRequest(
    DateOnly? WorkDate,
    string? TimeType,
    string? RowType,
    string? RowLabel,
    string? ProjectCode,
    string? ProjectName,
    string? TaskCode,
    string? TaskName,
    string? CategoryCode,
    string? EngineerNote,
    string? DetailLevel = "detailed",
    Guid? ProjectId = null,
    Guid? TaskId = null,
    Guid? AssignmentId = null);

public sealed record PulseAiPrivateFlowHiveRequest(
    string? ProjectCode,
    string? ProjectName,
    string? RequestedOutcome,
    string? DetailLevel = "comprehensive");

public sealed record PulseAiPrivateFeedbackRequest(
    string? FeedbackType,
    string? FeedbackReason,
    object? CorrectedAnswer,
    bool RequestTrainingCandidate = false);

public sealed record PulseAiPrivateModelRequest(
    string FeatureCode,
    string PurposeCode,
    string DetailLevel,
    string SystemInstruction,
    string UserInstruction,
    IReadOnlyList<PulseAiPrivateRetrievedChunk> Sources,
    string OutputSchemaName,
    int MaximumOutputTokens,
    decimal Temperature,
    string CorrelationId);

public sealed record PulseAiPrivateModelResult(
    string Status,
    string Provider,
    string Model,
    string Content,
    int InputCharacters,
    int OutputCharacters,
    string DiagnosticCode,
    DateTimeOffset CompletedAt)
{
    public bool Succeeded => Status == "private_model_completed" && !string.IsNullOrWhiteSpace(Content);
}

internal sealed record PulseAiPrivateModelDetailedAnswerDto(
    [property: JsonPropertyName("directConclusion")] string? DirectConclusion,
    [property: JsonPropertyName("executiveSummary")] string? ExecutiveSummary,
    [property: JsonPropertyName("scopeAndFilters")] string[]? ScopeAndFilters,
    [property: JsonPropertyName("detailedAnalysis")] string[]? DetailedAnalysis,
    [property: JsonPropertyName("sourceEvidence")] string[]? SourceEvidence,
    [property: JsonPropertyName("calculations")] string[]? Calculations,
    [property: JsonPropertyName("knownUnknownAndStaleValues")] string[]? KnownUnknownAndStaleValues,
    [property: JsonPropertyName("assumptions")] string[]? Assumptions,
    [property: JsonPropertyName("conflicts")] string[]? Conflicts,
    [property: JsonPropertyName("limitations")] string[]? Limitations,
    [property: JsonPropertyName("risksAndImplications")] string[]? RisksAndImplications,
    [property: JsonPropertyName("recommendedActions")] string[]? RecommendedActions,
    [property: JsonPropertyName("navigationTargets")] string[]? NavigationTargets,
    [property: JsonPropertyName("citationIds")] int[]? CitationIds,
    [property: JsonPropertyName("confidence")] decimal? Confidence,
    [property: JsonPropertyName("confidenceExplanation")] string? ConfidenceExplanation);

internal sealed record PulseAiPrivateModelFlowHiveDto(
    [property: JsonPropertyName("objective")] string? Objective,
    [property: JsonPropertyName("tasks")] PulseAiPrivateFlowHiveTask[]? Tasks,
    [property: JsonPropertyName("milestones")] PulseAiPrivateFlowHiveMilestone[]? Milestones,
    [property: JsonPropertyName("dependencies")] string[]? Dependencies,
    [property: JsonPropertyName("requiredRoles")] string[]? RequiredRoles,
    [property: JsonPropertyName("assumptions")] string[]? Assumptions,
    [property: JsonPropertyName("risks")] string[]? Risks,
    [property: JsonPropertyName("outOfScopeItems")] string[]? OutOfScopeItems,
    [property: JsonPropertyName("openQuestions")] string[]? OpenQuestions,
    [property: JsonPropertyName("conflicts")] string[]? Conflicts,
    [property: JsonPropertyName("citationIds")] int[]? CitationIds,
    [property: JsonPropertyName("confidence")] decimal? Confidence,
    [property: JsonPropertyName("confidenceExplanation")] string? ConfidenceExplanation);
