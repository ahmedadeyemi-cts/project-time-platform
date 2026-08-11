using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public static class CelarAiOperationsPolicy
{
    public const string ContractVersion = "celar-ai-ask-operations-v1-20260810";
    public const string MigrationId = "084_module_076_celar_ai_defect_operations";
    public const string DefaultAssigneeEmail = "ahmed.adeyemi@ussignal.com";
    public const string DefaultAssigneeName = "Ahmed Adeyemi";
    public const string DefaultRepository = "ahmedadeyemi-cts/project-time-platform";
    public const int MaximumEvidenceItems = 25;
    public const int MaximumReproductionSteps = 25;
    public const int MaximumQuestionCharacters = 8_000;
    public const int MaximumDiagnosticDetailCharacters = 8_000;
    public const int MaximumAutomaticDefectsPerHour = 10;
    public const int DefaultProbeIntervalSeconds = 60;

    public static bool AutomaticMonitoringEnabled =>
        IsTest && Boolean("PROJECTPULSE_CELAR_AI_AUTOMATIC_DEFECTS_ENABLED", false);

    public static bool SyntheticFailureEnabled =>
        IsTest && Boolean("PROJECTPULSE_CELAR_AI_SYNTHETIC_FAILURES_ENABLED", false);

    public static bool IsTest => string.Equals(
        Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
        "test",
        StringComparison.OrdinalIgnoreCase);

    public static string DefaultAssigneeEmailValue =>
        Clean(Environment.GetEnvironmentVariable("PROJECTPULSE_DEFECT_DEFAULT_ASSIGNEE_EMAIL"), 320)
            is { Length: > 0 } configured
                ? configured
                : DefaultAssigneeEmail;

    public static int ProbeIntervalSeconds => Integer(
        "PROJECTPULSE_CELAR_AI_MONITOR_INTERVAL_SECONDS",
        DefaultProbeIntervalSeconds,
        30,
        3_600);

    public static bool IsTroubleshootingIntent(string? value)
    {
        var normalized = Normalize(value);
        return ContainsAny(
            normalized,
            "troubleshoot",
            "diagnose",
            "why is",
            "why did",
            "not working",
            "unavailable",
            "timeout",
            "failed",
            "broken",
            "error",
            "health check",
            "service status");
    }

    public static bool IsDefectIntent(string? value)
    {
        var normalized = Normalize(value);
        return ContainsAny(
            normalized,
            "open a defect",
            "create a defect",
            "report a defect",
            "file a defect",
            "log a defect",
            "raise a defect",
            "report this issue",
            "this is broken",
            "open an issue");
    }

    public static string Normalize(string? value) => Regex.Replace(
        (value ?? string.Empty).Trim().ToLowerInvariant(),
        @"\s+",
        " ",
        RegexOptions.CultureInvariant);

    public static string Clean(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Length <= maximum
                ? value.Trim()
                : value.Trim()[..maximum];

    public static string[] CleanList(IEnumerable<string>? values, int maximumItems, int maximumCharacters) =>
        (values ?? [])
            .Select(value => Clean(value, maximumCharacters))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumItems)
            .ToArray();

    public static string SanitizeOperationalDetail(string? value)
    {
        var clean = Clean(value, MaximumDiagnosticDetailCharacters);
        if (clean.Length == 0) return string.Empty;
        clean = Regex.Replace(
            clean,
            @"(?i)(authorization|proxy-authorization|cookie|set-cookie|x-projectpulse-session|api[-_ ]?key|token|password|secret)\s*[:=]\s*[^\s,;]+",
            "$1=[REDACTED]",
            RegexOptions.CultureInvariant);
        clean = Regex.Replace(
            clean,
            @"(?i)bearer\s+[a-z0-9._~+/=-]{8,}",
            "Bearer [REDACTED]",
            RegexOptions.CultureInvariant);
        clean = Regex.Replace(
            clean,
            @"(?i)(postgres(?:ql)?://)[^\s]+",
            "$1[REDACTED]",
            RegexOptions.CultureInvariant);
        return clean;
    }

    public static string EnvironmentName() => Clean(
        Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "unknown",
        32).ToLowerInvariant();

    public static string ReleaseSha() => Clean(
        Environment.GetEnvironmentVariable("PROJECTPULSE_RELEASE_SHA")
            ?? Environment.GetEnvironmentVariable("PROJECTPULSE_SOURCE_REVISION")
            ?? Environment.GetEnvironmentVariable("GITHUB_SHA"),
        40).ToLowerInvariant();

    private static bool ContainsAny(string value, params string[] signals) =>
        signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? value
            : fallback;

    private static int Integer(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;
}

public sealed record CelarAiTroubleshootRequest(
    string? Question,
    string? Environment = null,
    string? AffectedSystem = null,
    string? AffectedModule = null,
    string? AffectedRoute = null,
    string? CorrelationId = null,
    string? ProjectCode = null,
    string? ProjectName = null,
    bool IncludeAiRuntime = true,
    bool IncludeDatabase = true,
    bool IncludeModule064 = true,
    bool IncludeGitHub = false,
    bool IncludeNotifications = false);

public sealed record CelarAiProbeEvidence(
    string ProbeCode,
    string ComponentCode,
    string DisplayName,
    string Status,
    int? HttpStatus,
    int? LatencyMs,
    string FailureCode,
    string Detail,
    string Source,
    DateTimeOffset ObservedAt)
{
    public bool Healthy => Status.Equals("healthy", StringComparison.OrdinalIgnoreCase);
    public bool Failed => Status.Equals("failed", StringComparison.OrdinalIgnoreCase);
}

public sealed record CelarAiTroubleshootOutcome(
    string Status,
    string DirectConclusion,
    IReadOnlyList<CelarAiProbeEvidence> Evidence,
    IReadOnlyList<string> LikelyCauses,
    IReadOnlyList<string> RecommendedActions,
    IReadOnlyList<string> Limitations,
    string CorrelationId,
    decimal Confidence,
    bool ExistingDefectSearchRecommended,
    bool DefectIntakeRecommended,
    DateTimeOffset DataAsOf);

public sealed record CelarAiDefectIntakeCreateRequest(
    Guid? ConversationId,
    string? TriggerQuestion,
    string? Environment,
    string? AffectedSystem,
    string? AffectedModule,
    string? AffectedRoute,
    string? CorrelationId,
    string? ReleaseSha,
    string? SuggestedTitle,
    string? SuggestedDescription,
    string? SuggestedCategory,
    string? SuggestedPriority,
    IReadOnlyList<CelarAiProbeEvidence>? DiagnosticEvidence);

public sealed record CelarAiDefectIntakeUpdateRequest(
    int ExpectedRevision,
    string? CurrentStep,
    string? Title,
    string? Description,
    string? Category,
    string? Priority,
    string? Environment,
    string? AffectedSystem,
    string? AffectedModule,
    string? AffectedRoute,
    string? ExpectedBehavior,
    string? ActualBehavior,
    IReadOnlyList<string>? ReproductionSteps,
    string? BusinessImpact,
    string? Workaround,
    string? CorrelationId,
    string? ReleaseSha,
    bool? ReadyForReview);

public sealed record CelarAiDefectIntakeSubmitRequest(
    int ExpectedRevision,
    bool UserConfirmed,
    string? ConfirmationText);

public sealed record CelarAiDefectEvidenceRequest(
    string? EvidenceType,
    string? SourceCode,
    string? SourceReference,
    string? SanitizedSummary,
    JsonElement? EvidenceDocument,
    DateTimeOffset? ObservedAt);

public sealed record CelarAiDefectDraft(
    string Title,
    string Description,
    string Category,
    string Priority,
    string Environment,
    string AffectedSystem,
    string AffectedModule,
    string AffectedRoute,
    string ExpectedBehavior,
    string ActualBehavior,
    IReadOnlyList<string> ReproductionSteps,
    string BusinessImpact,
    string Workaround,
    string CorrelationId,
    string ReleaseSha);

public sealed record CelarAiDefectIdentity(
    Guid? UserId,
    string DisplayName,
    string Email,
    string State);

public sealed record CelarAiDefectRecord(
    Guid DefectId,
    string DefectNumber,
    string Title,
    string Description,
    string Category,
    string Priority,
    string Status,
    string SourceChannel,
    string Environment,
    string AffectedSystem,
    string AffectedModule,
    string AffectedRoute,
    CelarAiDefectIdentity Reporter,
    CelarAiDefectIdentity Assignee,
    bool MachineCreated,
    string CorrelationId,
    string ReleaseSha,
    int OccurrenceCount,
    int FlappingCount,
    DateTimeOffset DateAdded,
    DateTimeOffset? DateResolved,
    long? ResolutionSeconds,
    int RevisionNumber);

public sealed record CelarAiDefectIntakeSession(
    Guid IntakeSessionId,
    Guid ActualUserId,
    Guid EffectiveUserId,
    Guid? ConversationId,
    string Status,
    string CurrentStep,
    CelarAiDefectDraft Draft,
    IReadOnlyList<CelarAiProbeEvidence> DiagnosticEvidence,
    Guid? MatchedDefectId,
    Guid? SubmittedDefectId,
    int RevisionNumber,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt);

public sealed record CelarAiMonitorPolicy(
    string PolicyCode,
    string DisplayName,
    string ComponentCode,
    string Environment,
    bool Enabled,
    int ConsecutiveFailureThreshold,
    int EvaluationWindowSeconds,
    int ConsecutiveSuccessThreshold,
    int RecoveryStabilitySeconds,
    string InitialPriority,
    int MaximumNewDefectsPerHour,
    int FlappingWindowSeconds,
    int FlappingReopenThreshold,
    bool MachineCreationEnabled,
    int RevisionNumber);

public sealed record CelarAiMonitorEvaluation(
    string PolicyCode,
    string State,
    int ConsecutiveFailures,
    int ConsecutiveSuccesses,
    bool Suppressed,
    bool ThresholdCrossed,
    bool RecoveryStable,
    string Fingerprint,
    Guid? DefectId,
    string? DefectNumber,
    DateTimeOffset EvaluatedAt);

public sealed record CelarAiSyntheticFailureRequest(
    string? Scenario,
    int? Occurrences = 1,
    string? Confirmation = null);
