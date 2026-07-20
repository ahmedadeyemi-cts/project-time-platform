using System.Collections.Concurrent;

namespace ProjectTime.Api.Ai;

public sealed class ProjectPulseAiHealthRegistry
{
    private readonly ProjectPulseAiConfiguration _configuration;
    private readonly ConcurrentDictionary<string, ProviderState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    public ProjectPulseAiHealthRegistry(ProjectPulseAiConfiguration configuration)
    {
        _configuration = configuration;
        _states[ProjectPulseAiProviders.Claude] = ProviderState.Remote(configuration.Claude);
        _states[ProjectPulseAiProviders.OpenAi] = ProviderState.Remote(configuration.OpenAi);
        _states[ProjectPulseAiProviders.Local] = ProviderState.Local();
    }

    public bool CanAttempt(string provider, out string reason)
    {
        if (!_states.TryGetValue(provider, out var state))
        {
            reason = "provider_not_registered";
            return false;
        }

        lock (state.Sync)
        {
            if (!state.Enabled)
            {
                reason = "provider_disabled";
                return false;
            }

            if (!state.Configured)
            {
                reason = "provider_not_configured";
                return false;
            }

            if (state.CircuitOpenUntil is { } openUntil)
            {
                if (openUntil > DateTimeOffset.UtcNow)
                {
                    reason = "provider_circuit_open";
                    return false;
                }

                state.CircuitOpenUntil = null;
                state.Status = "probe_due";
            }

            reason = "available_for_attempt";
            return true;
        }
    }

    public void RecordSuccess(
        string provider,
        ProjectPulseAiUsage? usage,
        string? requestId,
        string outcome = ProjectPulseAiOutcomes.Success,
        ProjectPulseAiRateLimits? rateLimits = null)
    {
        if (!_states.TryGetValue(provider, out var state)) return;

        lock (state.Sync)
        {
            var now = DateTimeOffset.UtcNow;
            state.Status = "available";
            state.LastOutcome = outcome;
            state.LastCheckedAt = now;
            state.LastSuccessAt = now;
            state.LastFailureCode = null;
            state.CircuitOpenUntil = null;
            state.ConsecutiveFailures = 0;
            state.SuccessCount++;
            state.InputTokens = Add(state.InputTokens, usage?.InputTokens);
            state.OutputTokens = Add(state.OutputTokens, usage?.OutputTokens);
            state.LastRequestId = requestId;
            if (rateLimits is not null) state.RateLimits = rateLimits;
        }
    }

    public void RecordRefusal(
        string provider,
        ProjectPulseAiUsage? usage,
        string? requestId,
        ProjectPulseAiRateLimits? rateLimits)
    {
        if (!_states.TryGetValue(provider, out var state)) return;

        RecordSuccess(provider, usage, requestId, ProjectPulseAiOutcomes.Refusal, rateLimits);

        lock (state.Sync)
        {
            state.RefusalCount++;
        }
    }

    public void RecordFailure(string provider, string code, string? requestId)
    {
        if (!_states.TryGetValue(provider, out var state)) return;

        lock (state.Sync)
        {
            var now = DateTimeOffset.UtcNow;
            state.LastOutcome = ProjectPulseAiOutcomes.Unavailable;
            state.LastCheckedAt = now;
            state.LastFailureAt = now;
            state.LastFailureCode = SanitizeCode(code);
            state.FailureCount++;
            state.ConsecutiveFailures++;
            state.LastRequestId = requestId;

            if (state.ConsecutiveFailures >= _configuration.FailureThreshold)
            {
                state.Status = "circuit_open";
                state.CircuitOpenUntil = now.AddSeconds(_configuration.CircuitBreakSeconds);
            }
            else
            {
                state.Status = "degraded";
            }
        }
    }

    public void RecordProbe(ProjectPulseAiProbeResult result)
    {
        if (result.Available)
        {
            RecordSuccess(result.Provider, null, result.RequestId, "health_check_success");
            return;
        }

        RecordFailure(result.Provider, result.Code, result.RequestId);
    }

    public IReadOnlyList<ProjectPulseAiProviderHealthSnapshot> Snapshots()
    {
        return _states.Values
            .Select(Snapshot)
            .OrderBy(item => item.Provider switch
            {
                ProjectPulseAiProviders.Claude => 0,
                ProjectPulseAiProviders.OpenAi => 1,
                _ => 2
            })
            .ToArray();
    }

    public ProjectPulseAiProviderHealthSnapshot Snapshot(string provider)
    {
        if (!_states.TryGetValue(provider, out var state))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provider is not registered.");
        }

        return Snapshot(state);
    }

    private static ProjectPulseAiProviderHealthSnapshot Snapshot(ProviderState state)
    {
        lock (state.Sync)
        {
            return new ProjectPulseAiProviderHealthSnapshot(
                state.Provider,
                state.Enabled,
                state.Configured,
                state.Status,
                state.LastOutcome,
                state.LastCheckedAt,
                state.LastSuccessAt,
                state.LastFailureAt,
                state.LastFailureCode,
                state.CircuitOpenUntil,
                state.SuccessCount,
                state.FailureCount,
                state.RefusalCount,
                state.InputTokens,
                state.OutputTokens,
                state.LastRequestId,
                state.RateLimits);
        }
    }

    private static long? Add(long? current, long? increment) =>
        increment is null ? current : (current ?? 0) + increment.Value;

    private static string SanitizeCode(string value)
    {
        var safe = new string((value ?? "provider_unavailable")
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .Take(80)
            .ToArray());

        return string.IsNullOrWhiteSpace(safe) ? "provider_unavailable" : safe;
    }

    private sealed class ProviderState
    {
        public object Sync { get; } = new();
        public required string Provider { get; init; }
        public required bool Enabled { get; init; }
        public required bool Configured { get; init; }
        public required string Status { get; set; }
        public required string LastOutcome { get; set; }
        public DateTimeOffset? LastCheckedAt { get; set; }
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public string? LastFailureCode { get; set; }
        public DateTimeOffset? CircuitOpenUntil { get; set; }
        public int ConsecutiveFailures { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public long RefusalCount { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public string? LastRequestId { get; set; }
        public ProjectPulseAiRateLimits? RateLimits { get; set; }

        public static ProviderState Remote(ProjectPulseAiProviderConfiguration configuration) => new()
        {
            Provider = configuration.Code,
            Enabled = configuration.Enabled,
            Configured = configuration.Configured,
            Status = !configuration.Enabled
                ? "disabled"
                : configuration.Configured
                    ? "not_checked"
                    : "not_configured",
            LastOutcome = "none"
        };

        public static ProviderState Local() => new()
        {
            Provider = ProjectPulseAiProviders.Local,
            Enabled = true,
            Configured = true,
            Status = "available",
            LastOutcome = "ready"
        };
    }
}
