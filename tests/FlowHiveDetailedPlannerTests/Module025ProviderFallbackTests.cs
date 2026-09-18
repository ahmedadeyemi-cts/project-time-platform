using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;

internal static class Module025ProviderFallbackTests
{
    internal static async Task RunAsync(PulseAiPrivateFlowHivePlan fixture,
        CelarAiAuthoritativeScopeEvidence evidence, PulseAiEscalationSanitizer sanitizer)
    {
        static void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("ASSERTION_FAILED " + label); Console.WriteLine("ASSERTION_PASSED " + label); }
        var environment = new Dictionary<string, string?> {
            ["PROJECTPULSE_CELAR_AI_ENABLED"] = "true",
            ["PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT"] = "https://synthetic.invalid/v1/chat/completions",
            ["PROJECTPULSE_PRIVATE_INFERENCE_MODEL"] = "synthetic-model",
            ["PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN"] = "synthetic-test-only"
        };
        var previous = environment.ToDictionary(p => p.Key, p => Environment.GetEnvironmentVariable(p.Key));
        try
        {
            foreach (var pair in environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            foreach (var scenario in new[] { "private_success", "all_failed", "refusal", "cancelled" })
            {
                var events = new List<Module025GenerationProgress>();
                var phase = new Module025PhaseExecution("Implement", 0, (value, _) => { events.Add(value); return Task.CompletedTask; });
                var adapter = Module025ExternalSowAdapter.TryCreate(evidence with { PhaseExecution = phase })!;
                var request = adapter.Prepare(sanitizer, out _)!;
                var wire = JsonSerializer.SerializeToNode(fixture with {
                    Tasks = fixture.Tasks.Where(t => t.Phase == "Implement").ToArray()
                })!.AsObject();
                // Reproduce the uploaded category and location without copying
                // customer or provider text. The credential guard must reject it.
                var secretSentinel = new string('x', 32); // Deliberately synthetic; matches the 32-character guard.
                wire["Tasks"]![1]!["Prerequisites"] = new JsonArray("Approved change", "Recovery plan", secretSentinel);
                var invalid = wire.ToJsonString();
                var rejection = adapter.ValidateResult(new("openai", "success", invalid, null, null, null, null, 200), "test", sanitizer);
                Check(!rejection.IsSuccess && rejection.Content is null
                    && rejection.SowDiagnostics?.OutputValidationCategory == "high_entropy_tokens"
                    && rejection.SowDiagnostics.OutputValidationField == "$.tasks[1].prerequisites[2]",
                    "credential_content_still_rejected_without_retaining_text");
                var configuration = new ProjectPulseAiConfiguration();
                foreach (var provider in new[] { "claude", "openai", "deepseek_v4" })
                { configuration.ApplyStoredSecret(provider, "synthetic-test-only", "test", DateTimeOffset.UtcNow); configuration.ApplyStoredEnabled(provider, true); }
                var claude = new Provider("claude", scenario == "refusal" ? "refusal" : "failure", null,
                    scenario == "refusal" ? "claude_safety_refusal" : "structured_sow_output_truncated");
                var openai = new Provider("openai", "success", invalid, null);
                var deepseek = new Provider("deepseek_v4", "failure", null, "private_model_unavailable");
                using var store = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance);
                var router = new CelarAiCapabilityRouter(store,
                    new CelarAiPrivateGenerationTarget(new NoNetwork(), NullLogger<CelarAiPrivateGenerationTarget>.Instance),
                    configuration, new ProjectPulseAiHealthRegistry(configuration), sanitizer, [claude, openai, deepseek],
                    new CelarAiConsumerAssuranceRegistry(), NullLogger<CelarAiCapabilityRouter>.Instance);
                var execution = new CelarAiCapabilityExecutionContext(CelarAiCapabilityCatalog.SowGsdPlanning,
                    true, true, false, true, false, [evidence.CustomerName], "025", "test",
                    StructuredSowPhase: true, BeforeStructuredSowAttempt: phase.BeforeAttemptAsync)
                    { ExternalSow = adapter, ObserveStructuredSowAttempt = phase.ObserveProviderAsync };
                var privateCalls = new List<string>();
                using var cancel = new CancellationTokenSource();
                if (scenario == "cancelled") cancel.Cancel();
                try
                {
                    var result = await router.GenerateWithPrivateTargetAsync(request, execution, token => {
                        token.ThrowIfCancellationRequested();
                        var target = ProjectPulseDeepSeekProvider.PrivateTarget!;
                        privateCalls.Add(target);
                        var success = scenario == "private_success" && target == "celar_ai";
                        return Task.FromResult(new ProjectPulseAiProviderResult(target, success ? "success" : "failure",
                            success ? "synthetic validated private phase" : null,
                            success ? null : "private_model_unavailable", null, null, null, 200));
                    }, () => throw new InvalidOperationException("Local template must never complete a detailed SOW."), cancel.Token);
                    if (scenario == "refusal")
                        Check(result.Outcome == "refusal" && openai.Calls == 0 && privateCalls.Count == 0,
                            "structured_sow_refusal_stops_all_fallback");
                    else
                    {
                        Check(claude.Calls == 1 && openai.Calls == 1 && deepseek.Calls == 0
                            && privateCalls.SequenceEqual(new[] { "deepseek_v4", "celar_ai" })
                            && events.Count(e => e.Stage == "provider_started") == 4,
                            "two_rejected_cloud_outputs_allow_both_private_providers_" + scenario);
                        Check(scenario == "private_success" ? result.Provider == "celar_ai" && result.Outcome == "success"
                            : result.Outcome != "success", "route_result_is_truthful_" + scenario);
                        Check(!await phase.BeforeAttemptAsync("openai", CancellationToken.None),
                            "route_cannot_spend_a_fifth_attempt");
                        Check(claude.Request!.UserPrompt == request.UserPrompt && openai.Request!.UserPrompt == request.UserPrompt
                            && !openai.Request.SystemPrompt.Contains(secretSentinel)
                            && !JsonSerializer.Serialize(events).Contains(secretSentinel),
                            "rejected_credentials_never_enter_fallback_prompts_or_progress");
                    }
                }
                catch (OperationCanceledException) when (scenario == "cancelled")
                { Check(claude.Calls == 0 && openai.Calls == 0 && privateCalls.Count == 0, "deadline_cancellation_stops_before_any_provider"); }
            }
            Console.WriteLine("MODULE025_PROVIDER_FALLBACK_TESTS=PASS scenarios=4 privacy=unchanged deadline=bounded");
        }
        finally { foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    }

    private sealed class Provider(string code, string outcome, string? content, string? diagnostic) : IProjectPulseAiProvider
    {
        public string Code => code;
        internal int Calls;
        internal ProjectPulseAiGenerationRequest? Request;
        public Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiGenerationRequest request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; Request = request; return Task.FromResult(new ProjectPulseAiProviderResult(code, outcome, content, diagnostic, null, null, null, 200)); }
        public Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class NoNetwork : IHttpClientFactory
    { public HttpClient CreateClient(string name) => throw new InvalidOperationException("Synthetic regression must not contact a provider."); }
}
