using System.Net;
using System.Text.Json.Serialization;

namespace ProjectTime.Api.Ai;

public static class PulseAiSystemIntelligencePolicy
{
    public const string ContractVersion = "celar-ai-system-intelligence-v2-20260806";
    public const string ApiCatalogVersion = "celar-ai-live-api-catalog-v2-20260806";
    public const string TroubleshootingVersion = "celar-ai-troubleshooting-v2-20260806";
    public const string EnhancementVersion = "celar-ai-enhancement-advisor-v2-20260806";
    public const string ConversationVersion = "celar-ai-durable-conversations-v2-20260806";
    public const string MigrationId = "054_pulse_ai_system_intelligence_conversations";
    public const string ModuleNumber = "011";
    public const string PrivacyBoundary = "private_celar_runtime_and_authorized_same_origin_tools";
    public const string FeatureCode = "pulse_ai_system_intelligence";

    public const string AskPermission = "ASK_PULSE_AI_SYSTEM_INTELLIGENCE";
    public const string ApiInventoryPermission = "VIEW_PULSE_AI_API_INVENTORY";
    public const string TroubleshootingPermission = "USE_PULSE_AI_SYSTEM_TROUBLESHOOTING";
    public const string EnhancementPermission = "USE_PULSE_AI_ENHANCEMENT_ADVISOR";
    public const string ConversationPermission = "VIEW_PULSE_AI_CONVERSATION_HISTORY";
    public const string RetestPermission = "RETEST_PULSE_AI_SAFE_API";
    public const string AuditPermission = "VIEW_PULSE_AI_SYSTEM_AUDIT";
    public const string AttachmentPermission = "ATTACH_CELAR_AI_CHAT_DOCUMENTS";
    public const string ResolvedIntentContextItem = "ProjectPulseCelarAiResolvedIntent";

    public const string RetestConfirmation = "RETEST-CELAR-AI-SAFE-API";

    public static readonly string[] DetailLevels =
    [
        "standard",
        "detailed",
        "comprehensive",
        "executive_and_detailed"
    ];

    public static readonly string[] IntentCodes =
    [
        "api_inventory",
        "troubleshooting",
        "future_enhancement",
        "architecture",
        "release_and_deployment",
        "observability",
        "security",
        "financial_and_reporting",
        "projects_and_delivery",
        "timesheets_and_approvals",
        "identity_and_permissions",
        "documents_and_rag",
        "product_help",
        "platform_identity",
        "general_knowledge",
        "general_system"
    ];

    public static readonly string[] RequiredAnswerSections =
    [
        "directConclusion",
        "executiveSummary",
        "scopeAndFilters",
        "currentState",
        "detailedAnalysis",
        "apiFindings",
        "troubleshootingFindings",
        "rootCauseHypotheses",
        "diagnosticSteps",
        "sourceEvidence",
        "knownUnknownAndStaleValues",
        "assumptions",
        "conflicts",
        "limitations",
        "risksAndImplications",
        "recommendedActions",
        "futureEnhancementBlueprint",
        "navigationTargets",
        "dataAsOf",
        "confidence"
    ];
}

public sealed record PulseAiSystemIntelligenceOptions(
    int MaximumTools,
    int MaximumToolResponseCharacters,
    int MaximumQuestionCharacters,
    int MaximumAnswerCharacters,
    int ToolTimeoutSeconds,
    int MaximumApiResults,
    bool EnablePrivateModelSynthesis,
    bool PersistToolResponseBodies,
    IReadOnlyList<string> AllowedSameOriginHosts)
{
    public static PulseAiSystemIntelligenceOptions FromEnvironment()
    {
        var allowedHosts = (Environment.GetEnvironmentVariable("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST") ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PulseAiSystemIntelligenceOptions(
            MaximumTools: Integer("PROJECTPULSE_PULSE_AI_SYSTEM_MAX_TOOLS", 12, 1, 30),
            MaximumToolResponseCharacters: Integer("PROJECTPULSE_PULSE_AI_SYSTEM_MAX_TOOL_CHARACTERS", 48_000, 2_000, 250_000),
            MaximumQuestionCharacters: Integer("PROJECTPULSE_PULSE_AI_SYSTEM_MAX_QUESTION_CHARACTERS", 8_000, 200, 40_000),
            MaximumAnswerCharacters: Integer("PROJECTPULSE_PULSE_AI_SYSTEM_MAX_ANSWER_CHARACTERS", 40_000, 2_000, 160_000),
            ToolTimeoutSeconds: Integer("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_TIMEOUT_SECONDS", 12, 2, 60),
            MaximumApiResults: Integer("PROJECTPULSE_PULSE_AI_SYSTEM_MAX_API_RESULTS", 500, 25, 2_500),
            EnablePrivateModelSynthesis: Boolean("PROJECTPULSE_PULSE_AI_SYSTEM_PRIVATE_MODEL_SYNTHESIS_ENABLED", true),
            PersistToolResponseBodies: Boolean("PROJECTPULSE_PULSE_AI_SYSTEM_PERSIST_TOOL_BODIES", false),
            AllowedSameOriginHosts: allowedHosts);
    }

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static int Integer(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
}

public sealed record PulseAiSystemAccess(
    Guid UserId,
    bool IsActive,
    IReadOnlySet<string> RoleCodes,
    IReadOnlySet<string> PermissionCodes)
{
    public bool IsSuperAdministrator => PulseAiRoleAuthority.HasAdministratorRole(RoleCodes);
    // Ask Celar AI is a baseline capability for every active authenticated
    // user. The assistant still applies the effective user's module, record,
    // project, document, API, troubleshooting, and audit permissions to every
    // source it may retrieve; this baseline never widens the underlying data.
    public bool CanUseCoreAssistant => IsActive;
    public bool CanAsk => CanUseCoreAssistant;
    public bool CanViewApis => IsSuperAdministrator || PermissionCodes.Contains(PulseAiSystemIntelligencePolicy.ApiInventoryPermission);
    public bool CanTroubleshoot => IsSuperAdministrator || PermissionCodes.Contains(PulseAiSystemIntelligencePolicy.TroubleshootingPermission);
    public bool CanEnhance => CanUseCoreAssistant;
    public bool CanViewConversations => CanUseCoreAssistant;
    public bool CanRetest => IsSuperAdministrator || PermissionCodes.Contains(PulseAiSystemIntelligencePolicy.RetestPermission);
    public bool CanViewAudit => IsSuperAdministrator || PermissionCodes.Contains(PulseAiSystemIntelligencePolicy.AuditPermission);
    public bool CanAttachDocuments => CanUseCoreAssistant;

    public static PulseAiSystemAccess From(PulseAiPrivateRagAccess access) =>
        new(access.UserId, access.IsActive, access.RoleCodes, access.PermissionCodes);
}

public sealed record PulseAiSystemQuestionRequest(
    Guid? ConversationId,
    string? Question,
    string? Mode = null,
    string? DetailLevel = "comprehensive",
    string? ProjectCode = null,
    string? ProjectName = null,
    string? ModuleCode = null,
    string? ApiSearch = null,
    bool IncludeApiInventory = true,
    bool IncludeTroubleshooting = true,
    bool IncludeFutureEnhancement = true,
    bool IncludeAuthorizedProjectDocuments = true,
    bool UsePrivateModelWhenAvailable = true,
    int? MaximumTools = null,
    bool IncludeRepositoryContext = false,
    bool IncludeAssumptions = true,
    bool IncludeSourceCitations = true,
    string? AnswerPreferenceSource = null,
    IReadOnlyList<Guid>? AttachmentIds = null);

public sealed record PulseAiConversationCreateRequest(
    string? Title,
    string? Mode,
    object? Scope);

public sealed record PulseAiSafeApiRetestRequest(
    string? Confirmation);

public sealed record PulseAiSystemApiDescriptor(
    string ApiId,
    string Method,
    string RoutePattern,
    string DisplayName,
    string EndpointName,
    int Order,
    string ModuleCode,
    string ModuleName,
    string Purpose,
    bool Parameterized,
    bool RequiresApplicationSession,
    bool AllowsAnonymous,
    bool SafeRetestSupported,
    string SafeRetestReason,
    string RegistrationStatus,
    string ReleaseSha)
{
    public string SearchText => string.Join(' ',
        Method,
        RoutePattern,
        DisplayName,
        EndpointName,
        ModuleCode,
        ModuleName,
        Purpose).ToLowerInvariant();
}

public sealed record PulseAiSystemToolDefinition(
    string Code,
    string Name,
    string ModuleCode,
    string ModuleName,
    string Method,
    string Path,
    string Purpose,
    IReadOnlyList<string> Intents,
    int Priority,
    bool RequiresApiInventoryPermission,
    bool RequiresTroubleshootingPermission,
    bool AdministrativeEvidence,
    bool SafeReadOnly);

public sealed record PulseAiSystemToolResult(
    string ToolCode,
    string ToolName,
    string ModuleCode,
    string ModuleName,
    string Method,
    string Path,
    string Status,
    int StatusCode,
    decimal DurationMs,
    int ResponseBytes,
    string DiagnosticCode,
    string ResponseJson,
    IReadOnlyList<string> EvidenceSummary,
    DateTimeOffset ObservedAt)
{
    public bool Succeeded => Status == "succeeded" && StatusCode is >= 200 and < 300;
    public bool Forbidden => StatusCode is 401 or 403;

    public object ToPublicEvidence() => new
    {
        toolCode = ToolCode,
        toolName = ToolName,
        moduleCode = ModuleCode,
        moduleName = ModuleName,
        method = Method,
        path = Path,
        status = Status,
        statusCode = StatusCode,
        durationMs = DurationMs,
        responseBytes = ResponseBytes,
        diagnosticCode = DiagnosticCode,
        evidenceSummary = EvidenceSummary,
        observedAt = ObservedAt,
        rawResponseReturned = false,
        secretValuesReturned = false
    };
}

public sealed record PulseAiEnhancementBlueprint(
    string RequestedCapability,
    string BusinessOutcome,
    IReadOnlyList<string> AffectedModules,
    IReadOnlyList<string> CurrentCapabilities,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> ProposedArchitecture,
    IReadOnlyList<string> ProposedApis,
    IReadOnlyList<string> DataAndMigrationConsiderations,
    IReadOnlyList<string> SecurityAndPrivacyControls,
    IReadOnlyList<string> OperationalAndSupportControls,
    IReadOnlyList<string> ImplementationPhases,
    IReadOnlyList<string> TestStrategy,
    IReadOnlyList<string> RolloutAndRollback,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> Dependencies);

public sealed record PulseAiSystemSourceEvidence(
    int SourceId,
    string SourceType,
    string SourceCode,
    string SourceName,
    string ModuleCode,
    string Method,
    string Path,
    string Status,
    int StatusCode,
    DateTimeOffset ObservedAt,
    string Freshness,
    string EvidenceScope);

public sealed record PulseAiSystemDetailedAnswer(
    string DirectConclusion,
    string ExecutiveSummary,
    IReadOnlyList<string> ScopeAndFilters,
    IReadOnlyList<string> CurrentState,
    IReadOnlyList<string> DetailedAnalysis,
    IReadOnlyList<string> ApiFindings,
    IReadOnlyList<string> TroubleshootingFindings,
    IReadOnlyList<string> RootCauseHypotheses,
    IReadOnlyList<string> DiagnosticSteps,
    IReadOnlyList<string> SourceEvidence,
    IReadOnlyList<string> KnownUnknownAndStaleValues,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> RisksAndImplications,
    IReadOnlyList<string> RecommendedActions,
    PulseAiEnhancementBlueprint? FutureEnhancementBlueprint,
    IReadOnlyList<string> NavigationTargets,
    IReadOnlyList<int> CitationIds,
    decimal Confidence,
    string ConfidenceExplanation,
    DateTimeOffset DataAsOf);

public sealed record PulseAiSystemQuestionResult(
    Guid ConversationId,
    Guid UserMessageId,
    Guid AssistantMessageId,
    Guid InquiryRunId,
    string Status,
    string IntentCode,
    string DetailLevel,
    PulseAiSystemDetailedAnswer Answer,
    IReadOnlyList<PulseAiSystemSourceEvidence> Sources,
    IReadOnlyList<PulseAiSystemApiDescriptor> RelevantApis,
    IReadOnlyList<PulseAiSystemToolResult> ToolResults,
    string ModelProvider,
    string ModelName,
    string CorrelationId,
    IReadOnlyList<string> Warnings,
    bool Persisted,
    IReadOnlyList<string>? AttemptedTargets = null,
    IReadOnlyList<string>? SkippedTargets = null,
    IReadOnlyList<ProjectPulseAiTargetDecision>? TargetDecisions = null,
    string ExternalAssistance = "",
    IReadOnlyList<PulseAiPrivateAnswerCitation>? PrivateCitations = null)
{
    public object ToPublicResponse() => new
    {
        conversationId = ConversationId,
        userMessageId = UserMessageId,
        assistantMessageId = AssistantMessageId,
        inquiryRunId = InquiryRunId,
        status = Status,
        intentCode = IntentCode,
        detailLevel = DetailLevel,
        answer = Answer,
        sources = Sources,
        relevantApis = RelevantApis,
        toolResults = ToolResults.Select(result => result.ToPublicEvidence()).ToArray(),
        modelProvider = ModelProvider,
        modelName = ModelName,
        correlationId = CorrelationId,
        warnings = Warnings,
        attemptedTargets = AttemptedTargets ?? [],
        skippedTargets = SkippedTargets ?? [],
        targetDecisions = TargetDecisions ?? [],
        externalAssistance = ExternalAssistance,
        privateCitations = PrivateCitations ?? [],
        persisted = Persisted,
        privacy = new
        {
            rawToolResponsesReturned = false,
            rawDocumentChunksReturned = false,
            embeddingVectorsReturned = false,
            providerSecretsReturned = false,
            arbitrarySqlAllowed = false,
            publicExternalModelUsedForPrivateContext = false
        }
    };
}

public sealed record PulseAiConversationSummary(
    Guid ConversationId,
    Guid EffectiveUserId,
    string Mode,
    string Title,
    string Status,
    int MessageCount,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PulseAiConversationMessage(
    Guid MessageId,
    Guid ConversationId,
    int SequenceNumber,
    string Role,
    string Status,
    string Text,
    object StructuredResponse,
    Guid? InquiryRunId,
    Guid? PrivateAnswerRunId,
    string CorrelationId,
    string ModelProvider,
    string ModelName,
    IReadOnlyList<string> ToolCodes,
    object SourceStates,
    DateTimeOffset? DataAsOf,
    DateTimeOffset CreatedAt);

public sealed record PulseAiConversationDetail(
    PulseAiConversationSummary Conversation,
    IReadOnlyList<PulseAiConversationMessage> Messages);

internal sealed record PulseAiSystemModelAnswerDto(
    [property: JsonPropertyName("directConclusion")] string? DirectConclusion,
    [property: JsonPropertyName("executiveSummary")] string? ExecutiveSummary,
    [property: JsonPropertyName("scopeAndFilters")] string[]? ScopeAndFilters,
    [property: JsonPropertyName("currentState")] string[]? CurrentState,
    [property: JsonPropertyName("detailedAnalysis")] string[]? DetailedAnalysis,
    [property: JsonPropertyName("apiFindings")] string[]? ApiFindings,
    [property: JsonPropertyName("troubleshootingFindings")] string[]? TroubleshootingFindings,
    [property: JsonPropertyName("rootCauseHypotheses")] string[]? RootCauseHypotheses,
    [property: JsonPropertyName("diagnosticSteps")] string[]? DiagnosticSteps,
    [property: JsonPropertyName("sourceEvidence")] string[]? SourceEvidence,
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
