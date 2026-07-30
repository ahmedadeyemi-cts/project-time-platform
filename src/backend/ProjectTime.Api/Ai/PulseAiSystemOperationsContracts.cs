namespace ProjectTime.Api.Ai;

public static class PulseAiSystemOperationsPolicy
{
    public const string ContractVersion = "pulse-ai-system-operations-v1-20260729";
    public const string MigrationId = "054_pulse_ai_system_operations_copilot";
    public const string FeatureCode = "system_operations_copilot";
    public const string UnifiedHelpFeatureCode = "live_help_answer";
    public const string FutureEnhancementFeatureCode = "future_enhancement_planner";
    public const string SafeRetestConfirmation = "RETEST-PULSE-AI-SAFE-API";

    public const string AskPermission = "ASK_PULSE_AI_SYSTEM_OPERATIONS";
    public const string ViewPermission = "VIEW_PULSE_AI_SYSTEM_OPERATIONS";
    public const string RetestPermission = "RETEST_PULSE_AI_SAFE_API";
    public const string HistoryPermission = "VIEW_PULSE_AI_OPERATIONS_HISTORY";
    public const string ExportPermission = "EXPORT_PULSE_AI_OPERATIONS_EVIDENCE";
    public const string FutureEnhancementPermission = "PLAN_PULSE_AI_FUTURE_ENHANCEMENT";

    public static readonly string[] AdministratorRoles =
    [
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "SYSTEM_ADMINISTRATOR",
        "SECURITY_ADMINISTRATOR",
        "SECURITY_OPERATIONS"
    ];

    public static readonly string[] ReadRoles =
    [
        .. AdministratorRoles,
        "SECURITY_ANALYST"
    ];

    public static readonly string[] SupportedIntents =
    [
        "api_inventory",
        "api_health",
        "api_failure_analysis",
        "api_detail",
        "correlation_trace",
        "dependency_analysis",
        "safe_retest_candidates",
        "platform_health",
        "worker_and_integration_health",
        "troubleshooting"
    ];
}

public sealed record PulseAiUnifiedHelpRequest(
    string? Question,
    string? ProjectCode,
    string? ProjectName,
    string? DetailLevel = "comprehensive",
    bool IncludeAuthorizedProjectDocuments = true,
    bool IncludeDirectProductKnowledge = true,
    int MaximumResults = 100);

public sealed record PulseAiSystemOperationsQuestionRequest(
    string? Question,
    string? DetailLevel = "comprehensive",
    int MaximumResults = 100,
    bool IncludeNotObserved = true,
    bool IncludeRecentEvidence = true);

public sealed record PulseAiSystemOperationsRetestRequest(string? Confirmation);

public sealed record PulseAiFutureEnhancementRequest(
    string? Question,
    string? DetailLevel = "comprehensive",
    bool IncludeLiveApiEvidence = true,
    bool PersistDraft = true);

public sealed record PulseAiSystemOperationsClassification(
    string Intent,
    string NormalizedQuestion,
    string ApiPath,
    string ApiMethod,
    string ApiId,
    string ModuleCode,
    string CorrelationId,
    string StatusFilter,
    string DependencyFilter,
    bool WantsAllApis,
    bool WantsFailuresOnly,
    bool WantsSlowApis,
    bool WantsSafeRetest,
    bool WantsTroubleshooting,
    decimal Confidence,
    IReadOnlyList<string> MatchedSignals);

public sealed record PulseAiSystemOperationsQuery(
    string Question,
    PulseAiSystemOperationsClassification Classification,
    int MaximumResults,
    bool IncludeNotObserved,
    bool IncludeRecentEvidence);

public sealed record PulseAiSystemApiRecord(
    string ApiId,
    string RouteGroup,
    string Method,
    string Path,
    string ModuleCode,
    string ModuleName,
    string Purpose,
    string AuthenticationRequirement,
    string PermissionRequirement,
    IReadOnlyList<string> Dependencies,
    string CurrentStatus,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessfulRequestAt,
    DateTimeOffset? LastFailureAt,
    double? ResponseTimeMs,
    string LastErrorCode,
    string CorrelationId,
    string RetestCapability,
    string RetestReason,
    string IntroducedRelease,
    string CurrentRelease,
    long RequestCount,
    long FailureCount)
{
    public decimal FailureRate => RequestCount <= 0
        ? 0m
        : Math.Round((decimal)FailureCount / RequestCount, 4);
}

public sealed record PulseAiSystemEventRecord(
    string EvidenceId,
    DateTimeOffset ObservedAt,
    string CorrelationId,
    string ModuleCode,
    string ModuleName,
    string EventType,
    string Status,
    string Method,
    string Path,
    int StatusCode,
    double ResponseTimeMs,
    string ErrorCode,
    string Message,
    string ReleaseSha,
    string Source);

public sealed record PulseAiSystemFindingRecord(
    Guid? FindingId,
    Guid? SessionId,
    string CheckCode,
    string Category,
    string Status,
    string Severity,
    string Summary,
    DateTimeOffset ObservedAt,
    string TargetKind,
    string TargetReference,
    string Source);

public sealed record PulseAiSystemDependencyRecord(
    string Key,
    string Name,
    string Status,
    double? LatencyMs,
    DateTimeOffset? CheckedAt,
    string Message,
    string ErrorCode);

public sealed record PulseAiSystemIntegrationRecord(
    string Key,
    string Name,
    string Type,
    string Status,
    DateTimeOffset? LastCheckedAt,
    string Owner,
    IReadOnlyList<string> Capabilities);

public sealed record PulseAiSystemWorkerRecord(
    string Key,
    string Name,
    string Status,
    string Source,
    string RestartMessage);

public sealed record PulseAiSystemRuntimeRecord(
    string Provider,
    string ProviderDisplayName,
    string Adapter,
    string AdapterStatus,
    string Environment,
    string Region,
    string WorkloadKind,
    string Instance,
    string ApplicationVersion,
    string ReleaseSha,
    DateTimeOffset ProcessStartedAt,
    double UptimeSeconds,
    string Deployment,
    DateTimeOffset? LastDeploymentAt,
    double CpuPercent,
    long ProcessWorkingSetBytes,
    long ProcessPrivateMemoryBytes,
    long ManagedHeapBytes,
    long? ContainerMemoryCurrentBytes,
    long? ContainerMemoryLimitBytes,
    long? TotalMemoryBytes,
    long? AvailableMemoryBytes);

public sealed record PulseAiSystemOperationsSnapshot(
    string Status,
    PulseAiSystemRuntimeRecord Runtime,
    IReadOnlyList<PulseAiSystemApiRecord> AllApis,
    IReadOnlyList<PulseAiSystemApiRecord> MatchingApis,
    IReadOnlyList<PulseAiSystemEventRecord> RecentEvents,
    IReadOnlyList<PulseAiSystemFindingRecord> PersistentFindings,
    IReadOnlyList<PulseAiSystemDependencyRecord> Dependencies,
    IReadOnlyList<PulseAiSystemIntegrationRecord> Integrations,
    IReadOnlyList<PulseAiSystemWorkerRecord> Workers,
    int TotalApiCount,
    int MatchingApiCount,
    int HealthyApiCount,
    int FailedApiCount,
    int RejectedApiCount,
    int NotObservedApiCount,
    int SafeRetestApiCount,
    int SlowApiCount,
    DateTimeOffset DataAsOf,
    string DiagnosticCode)
{
    public bool HasLiveEvidence => AllApis.Count > 0;
}

public sealed record PulseAiSystemOperationsCitation(
    int CitationId,
    string EvidenceType,
    string SourceModule,
    string SourceName,
    string ApiId,
    string Method,
    string Path,
    string Status,
    int? StatusCode,
    double? ResponseTimeMs,
    string ErrorCode,
    string CorrelationId,
    DateTimeOffset? ObservedAt,
    string ReleaseSha);

public sealed record PulseAiSystemOperationsAnswer(
    Guid InvestigationId,
    string Status,
    string Intent,
    PulseAiPrivateDetailedAnswer Answer,
    IReadOnlyList<PulseAiSystemApiRecord> Apis,
    IReadOnlyList<PulseAiSystemOperationsCitation> OperationalCitations,
    IReadOnlyList<string> RootCauseHypotheses,
    IReadOnlyList<string> TroubleshootingSequence,
    IReadOnlyList<string> SafeRetestCandidates,
    int TotalApiCount,
    int MatchingApiCount,
    string ReleaseSha,
    DateTimeOffset DataAsOf,
    string CorrelationId,
    string DiagnosticCode,
    bool Persisted)
{
    public object ToPublicResponse() => new
    {
        investigationId = InvestigationId,
        status = Status,
        featureCode = PulseAiSystemOperationsPolicy.FeatureCode,
        intent = Intent,
        answer = Answer,
        apis = Apis,
        operationalCitations = OperationalCitations,
        rootCauseHypotheses = RootCauseHypotheses,
        troubleshootingSequence = TroubleshootingSequence,
        safeRetestCandidates = SafeRetestCandidates,
        summary = new
        {
            totalApiCount = TotalApiCount,
            matchingApiCount = MatchingApiCount,
            releaseSha = ReleaseSha,
            dataAsOf = DataAsOf
        },
        correlationId = CorrelationId,
        diagnosticCode = DiagnosticCode,
        persisted = Persisted,
        privacy = new
        {
            requestBodiesReturned = false,
            queryStringsReturned = false,
            rawLogsReturned = false,
            rawExceptionMessagesReturned = false,
            secretValuesReturned = false,
            providerCredentialsReturned = false,
            responseBodiesReadByRetest = false
        },
        stateChanged = Persisted
    };
}

public sealed record PulseAiSystemOperationsHistoryItem(
    Guid InvestigationId,
    string IntentCode,
    string Status,
    string SanitizedQuestion,
    string DirectConclusion,
    int ApiCount,
    int EvidenceCount,
    string CorrelationId,
    string ReleaseSha,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record PulseAiFutureEnhancementPlan(
    Guid PlanId,
    string Status,
    string Title,
    PulseAiPrivateDetailedAnswer Answer,
    IReadOnlyList<PulseAiModuleKnowledge> AffectedModules,
    IReadOnlyList<PulseAiSystemApiRecord> CurrentApis,
    IReadOnlyList<string> CurrentCapabilities,
    IReadOnlyList<string> CapabilityGaps,
    IReadOnlyList<string> ProposedArchitecture,
    IReadOnlyList<string> DataAndMigrationChanges,
    IReadOnlyList<string> ApiAndIntegrationChanges,
    IReadOnlyList<string> PermissionAndRoleChanges,
    IReadOnlyList<string> PrivacyAndSecurityControls,
    IReadOnlyList<string> ObservabilityAndAudit,
    IReadOnlyList<string> TestingStrategy,
    IReadOnlyList<string> ReleaseSequence,
    IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> EstimatedPhases,
    DateTimeOffset CreatedAt,
    bool Persisted)
{
    public object ToPublicResponse() => new
    {
        planId = PlanId,
        status = Status,
        featureCode = PulseAiSystemOperationsPolicy.FutureEnhancementFeatureCode,
        title = Title,
        answer = Answer,
        affectedModules = AffectedModules,
        currentApis = CurrentApis,
        currentCapabilities = CurrentCapabilities,
        capabilityGaps = CapabilityGaps,
        proposedArchitecture = ProposedArchitecture,
        dataAndMigrationChanges = DataAndMigrationChanges,
        apiAndIntegrationChanges = ApiAndIntegrationChanges,
        permissionAndRoleChanges = PermissionAndRoleChanges,
        privacyAndSecurityControls = PrivacyAndSecurityControls,
        observabilityAndAudit = ObservabilityAndAudit,
        testingStrategy = TestingStrategy,
        releaseSequence = ReleaseSequence,
        acceptanceCriteria = AcceptanceCriteria,
        dependencies = Dependencies,
        risks = Risks,
        estimatedPhases = EstimatedPhases,
        createdAt = CreatedAt,
        persisted = Persisted,
        controls = new
        {
            implementationPerformed = false,
            migrationApplied = false,
            deploymentPerformed = false,
            providerCalled = false,
            productionChanged = false,
            humanApprovalRequired = true
        }
    };
}

public sealed record PulseAiUnifiedAnswerResult(
    string Mode,
    string Status,
    object Result,
    string RoutingReason,
    DateTimeOffset GeneratedAt)
{
    public object ToPublicResponse() => new
    {
        mode = Mode,
        status = Status,
        result = Result,
        routingReason = RoutingReason,
        generatedAt = GeneratedAt,
        answerContract = new
        {
            directAnswerRequired = true,
            comprehensiveDepthRequired = true,
            sourceAndScopeDisclosureRequired = true,
            unknownValuesPreserved = true,
            unsupportedClaimsProhibited = true,
            futureEnhancementGapAnalysisRequired = Mode == "future_enhancement",
            liveOperationsEvidenceRequired = Mode == "system_operations"
        }
    };
}
