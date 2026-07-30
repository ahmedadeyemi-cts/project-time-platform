using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiSystemOperationsIntentClassifier
{
    private static readonly Regex ApiPathPattern = new(
        @"(?<path>/api/[A-Za-z0-9_./{}:\-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CorrelationPattern = new(
        @"\b(?:correlation(?:\s+id)?|reference(?:\s+id)?)\s*[:#=]?\s*(?<value>[A-Za-z0-9][A-Za-z0-9._\-]{5,159})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModulePattern = new(
        @"\bmodule\s+(?<value>(?:\d{3}|055[cCdD]|997|998|999))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MethodPattern = new(
        @"\b(?<value>GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ApiIdPattern = new(
        @"\bapi\s+id\s*[:#=]?\s*(?<value>[A-Za-z0-9._\-]{6,300})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] StrongOperationsSignals =
    [
        "api",
        "apis",
        "endpoint",
        "endpoints",
        "route",
        "routes",
        "correlation id",
        "http 4",
        "http 5",
        "status code",
        "system health",
        "platform health",
        "system diagnostics",
        "api diagnostics",
        "module 013",
        "module 016",
        "module 078",
        "module 998",
        "safe retest",
        "retest api",
        "running on the system",
        "registered in the running"
    ];

    public bool IsSystemOperationsQuestion(string? question)
    {
        var normalized = Normalize(question);
        if (normalized.Length == 0) return false;
        if (ApiPathPattern.IsMatch(normalized) || CorrelationPattern.IsMatch(normalized)) return true;
        return StrongOperationsSignals.Any(signal => normalized.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    public PulseAiSystemOperationsClassification Classify(string? question)
    {
        var normalized = Normalize(question);
        var lower = normalized.ToLowerInvariant();
        var signals = new List<string>();

        var apiPath = Match(ApiPathPattern, normalized);
        if (apiPath.Length > 0) signals.Add("explicit_api_path");

        var correlationId = Match(CorrelationPattern, normalized);
        if (correlationId.Length > 0) signals.Add("correlation_id");

        var moduleCode = Match(ModulePattern, normalized).ToUpperInvariant();
        if (moduleCode.Length > 0) signals.Add("module_filter");

        var apiMethod = Match(MethodPattern, normalized).ToUpperInvariant();
        if (apiMethod.Length > 0) signals.Add("http_method");

        var apiId = Match(ApiIdPattern, normalized);
        if (apiId.Length > 0) signals.Add("api_id");

        var wantsFailures = ContainsAny(lower,
            "fail", "failed", "failing", "error", "errors", "broken", "unhealthy", "rejected",
            "500", "502", "503", "504", "401", "403", "404", "429", "timeout", "timed out");
        if (wantsFailures) signals.Add("failure_analysis");

        var wantsSlow = ContainsAny(lower,
            "slow", "latency", "response time", "performance", "taking too long", "timeout");
        if (wantsSlow) signals.Add("latency_analysis");

        var wantsRetest = ContainsAny(lower,
            "safe retest", "retest", "test the api", "test endpoint", "probe", "check again");
        if (wantsRetest) signals.Add("safe_retest");

        var wantsTroubleshooting = ContainsAny(lower,
            "troubleshoot", "diagnose", "diagnostic", "root cause", "why is", "why are",
            "not working", "what happened", "investigate", "resolution", "fix path");
        if (wantsTroubleshooting) signals.Add("troubleshooting");

        var wantsAll = ContainsAny(lower,
            "all api", "all endpoint", "every api", "every endpoint", "api inventory",
            "what api", "which api", "list api", "list endpoint", "running api", "registered api");
        if (wantsAll) signals.Add("inventory");

        var dependency = Dependency(lower);
        if (dependency.Length > 0) signals.Add("dependency_filter");

        var status = StatusFilter(lower, wantsFailures);
        if (status.Length > 0) signals.Add("status_filter");

        string intent;
        if (correlationId.Length > 0)
            intent = "correlation_trace";
        else if (apiPath.Length > 0 || apiId.Length > 0)
            intent = wantsTroubleshooting || wantsFailures || wantsSlow ? "api_failure_analysis" : "api_detail";
        else if (dependency.Length > 0)
            intent = "dependency_analysis";
        else if (wantsRetest)
            intent = "safe_retest_candidates";
        else if (wantsFailures || wantsSlow)
            intent = "api_health";
        else if (ContainsAny(lower, "worker", "background service", "hosted service", "integration health", "connector health"))
            intent = "worker_and_integration_health";
        else if (ContainsAny(lower, "platform health", "system health", "cpu", "memory", "storage health", "database health"))
            intent = "platform_health";
        else if (wantsTroubleshooting)
            intent = "troubleshooting";
        else
            intent = "api_inventory";

        var confidence = signals.Count switch
        {
            >= 4 => 0.99m,
            3 => 0.96m,
            2 => 0.92m,
            1 => 0.82m,
            _ => 0.65m
        };

        return new PulseAiSystemOperationsClassification(
            Intent: intent,
            NormalizedQuestion: normalized,
            ApiPath: apiPath,
            ApiMethod: apiMethod,
            ApiId: apiId,
            ModuleCode: moduleCode,
            CorrelationId: correlationId,
            StatusFilter: status,
            DependencyFilter: dependency,
            WantsAllApis: wantsAll,
            WantsFailuresOnly: wantsFailures,
            WantsSlowApis: wantsSlow,
            WantsSafeRetest: wantsRetest,
            WantsTroubleshooting: wantsTroubleshooting,
            Confidence: confidence,
            MatchedSignals: signals.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string Normalize(string? value)
    {
        var clean = Regex.Replace(value?.Trim() ?? string.Empty, @"\s+", " ");
        return clean.Length <= 6_000 ? clean : clean[..6_000];
    }

    private static string Match(Regex pattern, string value)
    {
        var match = pattern.Match(value);
        if (!match.Success) return string.Empty;
        var group = match.Groups["path"].Success ? match.Groups["path"] : match.Groups["value"];
        return group.Value.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '}');
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string StatusFilter(string value, bool wantsFailures)
    {
        if (ContainsAny(value, "not observed", "never called", "unused")) return "not_observed";
        if (ContainsAny(value, "rejected", "403", "401", "404", "409", "423", "429")) return "rejected";
        if (ContainsAny(value, "failed", "failing", "500", "502", "503", "504", "timeout")) return "failed";
        if (ContainsAny(value, "healthy", "successful", "working")) return "healthy";
        return wantsFailures ? "failed_or_rejected" : string.Empty;
    }

    private static string Dependency(string value)
    {
        var known = new (string Key, string[] Signals)[]
        {
            ("ProjectPulse database", ["database", "postgres", "postgresql", "db dependency"]),
            ("Microsoft Integration", ["microsoft graph", "graph api", "entra", "microsoft integration", "sso"]),
            ("Module 065 mail configuration", ["mail", "smtp", "email", "mail delivery"]),
            ("Artifact storage", ["artifact storage", "upload storage", "blob storage", "file storage"]),
            ("GitHub release controls", ["github", "release workflow", "deployment workflow", "github actions"]),
            ("Module 026 provider registry", ["sell", "crm", "erp", "certinia", "salesforce", "servicenow"]),
            ("ProjectPulse session", ["session", "authentication", "authorization", "login"])
        };

        foreach (var item in known)
        {
            if (item.Signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase)))
                return item.Key;
        }

        return string.Empty;
    }
}
