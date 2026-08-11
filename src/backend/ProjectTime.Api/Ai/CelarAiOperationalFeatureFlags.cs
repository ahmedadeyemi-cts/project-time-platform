namespace ProjectTime.Api.Ai;

/// <summary>
/// Separates observe-only monitoring from machine defect creation. The existing
/// CelarAiOperationsPolicy.AutomaticMonitoringEnabled compatibility property is
/// retained as the automatic-defect deployment flag so older threshold code
/// cannot create a machine defect while the package is only observing.
/// </summary>
public static class CelarAiOperationalFeatureFlags
{
    public const string ContractVersion = "celar-ai-operational-feature-flags-v1-20260810";

    public static bool MonitoringEnabled =>
        CelarAiOperationsPolicy.IsTest
        && Boolean("PROJECTPULSE_CELAR_AI_MONITORING_ENABLED", false);

    public static bool AutomaticDefectsEnabled =>
        MonitoringEnabled
        && CelarAiOperationsPolicy.AutomaticMonitoringEnabled;

    public static bool SyntheticFailuresEnabled =>
        CelarAiOperationsPolicy.SyntheticFailureEnabled;

    public static bool NotificationDispatcherEnabled =>
        CelarAiOperationsPolicy.IsTest
        && Boolean("PROJECTPULSE_CELAR_AI_NOTIFICATION_DISPATCHER_ENABLED", false);

    public static bool WatchdogReplayEnabled =>
        CelarAiOperationsPolicy.IsTest
        && Boolean("PROJECTPULSE_CELAR_AI_WATCHDOG_REPLAY_ENABLED", false);

    public static bool GitHubIssueSyncEnabled =>
        CelarAiOperationsPolicy.IsTest
        && Boolean("PROJECTPULSE_CELAR_AI_GITHUB_ISSUE_SYNC_ENABLED", false);

    public static object PublicState() => new
    {
        contractVersion = ContractVersion,
        environment = CelarAiOperationsPolicy.EnvironmentName(),
        monitoringEnabled = MonitoringEnabled,
        observeOnly = MonitoringEnabled && !AutomaticDefectsEnabled,
        automaticDefectsEnabled = AutomaticDefectsEnabled,
        syntheticFailuresEnabled = SyntheticFailuresEnabled,
        notificationDispatcherEnabled = NotificationDispatcherEnabled,
        watchdogReplayEnabled = WatchdogReplayEnabled,
        githubIssueSyncEnabled = GitHubIssueSyncEnabled,
        productionAutomaticDefectsAllowed = false
    };

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? value
            : fallback;
}
