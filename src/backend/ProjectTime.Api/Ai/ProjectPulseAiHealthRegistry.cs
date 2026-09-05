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
        _states[ProjectPulseAiProviders.DeepSeek] = ProviderState.Remote(configuration.DeepSeek);
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

    /// <summary>
    /// Reconciles the health registry with the live provider configuration.
    /// The registry may be constructed before encrypted database secrets have
    /// finished loading, so callers must re-apply the live configuration before
    /// health evaluation or routing decisions.
    /// </summary>
    public void ApplyConfiguration(ProjectPulseAiProviderConfiguration configuration)
    {
        if (!_states.TryGetValue(configuration.Code, out var state)) return;

        lock (state.Sync)
        {
            var wasEnabled = state.Enabled;
            var wasConfigured = state.Configured;
            state.Enabled = configuration.Enabled;
            state.Configured = configuration.Configured;

            if (!configuration.Enabled)
            {
                state.Status = "disabled";
                state.ProbeStatus = "disabled";
                return;
            }

            if (!configuration.Configured)
            {
                state.Status = "not_configured";
                state.ProbeStatus = "not_configured";
                return;
            }

            var configurationBecameReady = !wasEnabled || !wasConfigured;
            if (configurationBecameReady
                || state.ProbeStatus is "not_configured" or "disabled" or "not_checked")
            {
                state.ProbeStatus = "checking";
                if (state.Status is "not_configured" or "disabled" or "not_checked")
                {
                    state.Status = "checking";
                }
            }
        }
    }

    public bool ShouldProbe(string provider, TimeSpan maximumAge, bool force = false)
    {
        if (!_states.TryGetValue(provider, out var state)) return false;

        lock (state.Sync)
        {
            if (!state.Enabled || !state.Configured) return false;
            if (force) return true;
            if (state.ProbeStatus == "checking") return state.LastProbeAt is null;
            if (state.LastProbeAt is null) return true;
            return DateTimeOffset.UtcNow - state.LastProbeAt.Value >= maximumAge;
        }
    }

    public void ApplyPrivateConfiguration(CelarAiPrivateModelProfile profile)
    {
        var state = _states.GetOrAdd(CelarAiCapabilityTargets.CelarAi, ProviderState.PrivateTarget);
        lock (state.Sync)
        {
            state.Enabled = profile.Enabled;
            state.Configured = profile.Configured && profile.AuthenticationConfigured;
            if (!state.Enabled) state.Status = "disabled";
            else if (!state.Configured) state.Status = "not_configured";
            else if (state.Status is "disabled" or "not_configured") state.Status = "checking";
        }
    }

    public void MarkProbeStarted(string provider)
    {
        if (!_states.TryGetValue(provider, out var state)) return;

        lock (state.Sync)
        {
            if (!state.Enabled || !state.Configured) return;
            state.ProbeStatus = "checking";
            if (state.Status is "not_checked" or "not_configured" or "disabled" or "probe_due")
            {
                state.Status = "checking";
            }
        }
    }

    public void RecordSuccess(
        string provider,
        ProjectPulseAiUsage? usage,
        string? requestId,
        string outcome = ProjectPulseAiOutcomes.Success,
        ProjectPulseAiRateLimits? rateLimits = null)
    {
        if (!TryGetRecordableState(provider, out var state)) return;

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
        if (!TryGetRecordableState(provider, out var state)) return;

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

    // A rejected answer is a request-level policy failure, not proof that the
    // provider is down. Preserve its diagnostic without opening a global circuit.
    public void RecordOutputRejected(string provider, string code, string? requestId)
    {
        if (!TryGetRecordableState(provider, out var state)) return;
        lock (state.Sync)
        {
            state.LastOutcome = "output_rejected";
            state.LastCheckedAt = DateTimeOffset.UtcNow;
            state.LastFailureAt = state.LastCheckedAt;
            state.LastFailureCode = SanitizeCode(code);
            state.FailureCount++;
            state.LastRequestId = requestId;
        }
    }

    public void RecordProbe(ProjectPulseAiProbeResult result)
    {
        if (!TryGetRecordableState(result.Provider, out var state)) return;

        lock (state.Sync)
        {
            var now = DateTimeOffset.UtcNow;
            state.LastProbeAt = now;
            state.LastProbeRequestId = result.RequestId;
            state.LastCheckedAt = now;

            if (result.Available)
            {
                state.ProbeStatus = "available";
                state.LastProbeSuccessAt = now;
                state.LastProbeFailureCode = null;
                state.ProbeSuccessCount++;
                state.CircuitOpenUntil = null;
                state.ConsecutiveFailures = 0;
                state.Status = "available";
                if (state.LastOutcome == "none") state.LastOutcome = "health_probe_success";
                return;
            }

            state.ProbeStatus = "degraded";
            state.LastProbeFailureAt = now;
            state.LastProbeFailureCode = SanitizeCode(result.Code);
            state.ProbeFailureCount++;
            // A failed readiness probe is already evidence of unavailability;
            // do not spend the next user's request rediscovering the outage.
            state.CircuitOpenUntil = now.AddSeconds(_configuration.CircuitBreakSeconds);
            state.Status = "circuit_open";
        }
    }

    public IReadOnlyList<ProjectPulseAiProviderHealthSnapshot> Snapshots()
    {
        return _states.Values
            .Select(Snapshot)
            .OrderBy(item => item.Provider switch
            {
                ProjectPulseAiProviders.DeepSeek => -1,
                "celar_ai" => 0,
                ProjectPulseAiProviders.Claude => 1,
                ProjectPulseAiProviders.OpenAi => 2,
                _ => 3
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
                state.RateLimits,
                state.ProbeStatus,
                state.LastProbeAt,
                state.LastProbeSuccessAt,
                state.LastProbeFailureAt,
                state.LastProbeFailureCode,
                state.ProbeSuccessCount,
                state.ProbeFailureCount,
                state.LastProbeRequestId);
        }
    }

    private static long? Add(long? current, long? increment) =>
        increment is null ? current : (current ?? 0) + increment.Value;

    private bool TryGetRecordableState(string provider, out ProviderState state)
    {
        if (_states.TryGetValue(provider, out state!)) return true;
        if (!string.Equals(provider, "celar_ai", StringComparison.OrdinalIgnoreCase)) return false;

        // Celar AI's private profile is administered by the capability-routing
        // store rather than ProjectPulseAiConfiguration. Register its execution
        // state lazily on the first real route attempt so private generations are
        // counted without pretending that a health probe configured the target.
        state = _states.GetOrAdd(provider, ProviderState.PrivateTarget);
        return true;
    }

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
        public required bool Enabled { get; set; }
        public required bool Configured { get; set; }
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
        public required string ProbeStatus { get; set; }
        public DateTimeOffset? LastProbeAt { get; set; }
        public DateTimeOffset? LastProbeSuccessAt { get; set; }
        public DateTimeOffset? LastProbeFailureAt { get; set; }
        public string? LastProbeFailureCode { get; set; }
        public long ProbeSuccessCount { get; set; }
        public long ProbeFailureCount { get; set; }
        public string? LastProbeRequestId { get; set; }

        public static ProviderState Remote(ProjectPulseAiProviderConfiguration configuration) => new()
        {
            Provider = configuration.Code,
            Enabled = configuration.Enabled,
            Configured = configuration.Configured,
            Status = !configuration.Enabled
                ? "disabled"
                : configuration.Configured
                    ? "checking"
                    : "not_configured",
            LastOutcome = "none",
            ProbeStatus = !configuration.Enabled
                ? "disabled"
                : configuration.Configured
                    ? "checking"
                    : "not_configured"
        };

        public static ProviderState Local() => new()
        {
            Provider = ProjectPulseAiProviders.Local,
            Enabled = true,
            Configured = true,
            Status = "available",
            LastOutcome = "ready",
            ProbeStatus = "available"
        };

        public static ProviderState PrivateTarget(string provider) => new()
        {
            Provider = provider,
            Enabled = true,
            Configured = true,
            Status = "checking",
            LastOutcome = "none",
            ProbeStatus = "not_checked"
        };
    }
}
