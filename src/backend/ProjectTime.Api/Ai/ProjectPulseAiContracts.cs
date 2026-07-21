namespace ProjectTime.Api.Ai;

public static class ProjectPulseAiProviders
{
    public const string Claude = "claude";
    public const string OpenAi = "openai";
    public const string Local = "local_template";

    public static readonly string[] Remote = [Claude, OpenAi];
}

public static class ProjectPulseAiFeatures
{
    public const string TimesheetDescription = "timesheet_description";
    public const string SowGsdPlanning = "sow_gsd_planning";
    public const string HelpAssistant = "help_assistant";
    public const string CloseoutCommunication = "closeout_communication";
    public const string ProjectFlowHivePlan = "project_flowhive_plan";

    public static readonly string[] All =
    [
        TimesheetDescription,
        SowGsdPlanning,
        HelpAssistant,
        CloseoutCommunication,
        ProjectFlowHivePlan
    ];
}

public static class ProjectPulseAiOutcomes
{
    public const string Success = "success";
    public const string Refusal = "refusal";
    public const string Unavailable = "unavailable";
    public const string Failure = "failure";
}

public sealed record ProjectPulseAiGenerationRequest(
    string Feature,
    string SystemPrompt,
    string UserPrompt,
    int MaxOutputTokens,
    double Temperature);

public sealed record ProjectPulseAiUsage(
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens);

public sealed record ProjectPulseAiRateLimits(
    string? RequestsRemaining,
    string? TokensRemaining,
    string? RequestsReset,
    string? TokensReset);

public sealed record ProjectPulseAiProviderResult(
    string Provider,
    string Outcome,
    string? Content,
    string? Code,
    string? Message,
    string? RequestId,
    ProjectPulseAiUsage? Usage,
    int? HttpStatusCode,
    ProjectPulseAiRateLimits? RateLimits = null)
{
    public bool IsSuccess =>
        string.Equals(Outcome, ProjectPulseAiOutcomes.Success, StringComparison.Ordinal);

    public bool IsRefusal =>
        string.Equals(Outcome, ProjectPulseAiOutcomes.Refusal, StringComparison.Ordinal);
}

public sealed record ProjectPulseAiRouteResult(
    string Content,
    string Provider,
    string Outcome,
    string? Warning,
    IReadOnlyList<string> AttemptedProviders,
    IReadOnlyList<string> SkippedProviders,
    ProjectPulseAiUsage? Usage,
    string? RequestId);

public sealed record ProjectPulseAiProbeResult(
    string Provider,
    bool Available,
    string Code,
    string Message,
    int? HttpStatusCode,
    string? RequestId);

public sealed record ProjectPulseAiProviderHealthSnapshot(
    string Provider,
    bool Enabled,
    bool Configured,
    string Status,
    string LastOutcome,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastFailureCode,
    DateTimeOffset? CircuitOpenUntil,
    long SuccessCount,
    long FailureCount,
    long RefusalCount,
    long? InputTokens,
    long? OutputTokens,
    string? LastRequestId,
    ProjectPulseAiRateLimits? RateLimits,
    string ProbeStatus,
    DateTimeOffset? LastProbeAt,
    DateTimeOffset? LastProbeSuccessAt,
    DateTimeOffset? LastProbeFailureAt,
    string? LastProbeFailureCode,
    long ProbeSuccessCount,
    long ProbeFailureCount,
    string? LastProbeRequestId);

public interface IProjectPulseAiProvider
{
    string Code { get; }

    Task<ProjectPulseAiProviderResult> GenerateAsync(
        ProjectPulseAiGenerationRequest request,
        CancellationToken cancellationToken);

    Task<ProjectPulseAiProbeResult> ProbeAsync(
        CancellationToken cancellationToken);
}
