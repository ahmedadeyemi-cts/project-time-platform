using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;

// These are injected-provider regressions, not evidence of live model success.
internal static class Module064SequenceTests
{
    internal static async Task RunAsync(PulseAiEscalationSanitizer sanitizer)
    {
        var checks = 0;
        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException("ASSERTION_FAILED " + label); checks++; }
        var flags = new[] { "PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", "PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED" };
        var prior = flags.ToDictionary(flag => flag, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var flag in flags) Environment.SetEnvironmentVariable(flag, "true");
            string[][] orders = [
                ["gemini", "claude", "openai", "deepseek_v4", "celar_ai", "copilot_studio", "local_template"],
                ["celar_ai", "openai", "copilot_studio", "claude", "deepseek_v4", "gemini", "local_template"],
                ["claude", "deepseek_v4", "gemini", "celar_ai", "openai", "copilot_studio", "local_template"]
            ];
            foreach (var definition in CelarAiCapabilityCatalog.Definitions.Values)
            foreach (var order in orders)
            foreach (var restricted in new[] { false, true })
            {
                var calls = new List<string>();
                var configuration = new ProjectPulseAiConfiguration();
                var providers = ProjectPulseAiProviders.Remote.Select(code => new Provider(code, calls)).ToArray();
                foreach (var code in ProjectPulseAiProviders.Remote)
                {
                    configuration.ApplyStoredSecret(code, "synthetic-only", "test", DateTimeOffset.UtcNow);
                    configuration.ApplyStoredEnabled(code, true);
                }
                using var store = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance);
                var router = new CelarAiCapabilityRouter(store,
                    new CelarAiPrivateGenerationTarget(new NoNetwork(), NullLogger<CelarAiPrivateGenerationTarget>.Instance),
                    configuration, new ProjectPulseAiHealthRegistry(configuration), sanitizer, providers,
                    new CelarAiConsumerAssuranceRegistry(), NullLogger<CelarAiCapabilityRouter>.Instance);
                var route = new CelarAiCapabilityRouteSnapshot(definition.FeatureCode, definition.DisplayName,
                    definition.ConsumerModules, definition.ExternalContextPolicy, definition.ContextClassification,
                    order, 17, DateTimeOffset.UtcNow, null, true);
                var execution = new CelarAiCapabilityExecutionContext(definition.FeatureCode,
                    restricted, false, false, false, true, [], "test", "sequence-test",
                    ExternalCapsulePurpose: CelarAiExternalCapsuleCatalog.HelpProjectDelivery);
                var request = new ProjectPulseAiGenerationRequest(definition.FeatureCode,
                    "private-sentinel", "private-sentinel", 128, 0);
                var result = await router.GenerateForRouteAsync(request, execution, route,
                    () => "Synthetic fallback only", false, token => {
                        token.ThrowIfCancellationRequested();
                        var selected = ProjectPulseDeepSeekProvider.PrivateTarget!;
                        calls.Add(selected);
                        return Task.FromResult(new ProjectPulseAiProviderResult(selected, "unavailable", null,
                            "synthetic_unavailable", null, null, null, 503));
                    });
                var firstPrivate = Array.FindIndex(order, CelarAiCapabilityTargets.IsPrivate);
                var expected = order.Where((target, index) => target != CelarAiCapabilityTargets.Local
                    && (!restricted || index >= firstPrivate)).ToArray();
                Check(calls.SequenceEqual(expected), "every_capability_obeys_saved_sequence_" + definition.FeatureCode);
                Check(result.AttemptedProviders.SequenceEqual(expected), "attempt_audit_matches_real_callbacks");
                Check(calls.Distinct().Count() == calls.Count, "no_duplicate_or_deferred_replay");
                Check(providers.All(provider => !provider.RawSourceSeen), "raw_source_never_sent_to_external_targets");
                Check(CelarAiRouteExecutionPolicy.Order(route, restricted, false).SequenceEqual(order), "policy_never_reorders");
                Check(result.TargetDecisions!.All(decision => decision.Outcome != "deferred"), "decisions_are_used_failed_or_skipped_in_place");
                if (restricted)
                    Check(result.SkippedProviders.SequenceEqual(order.Take(firstPrivate)), "private_prerequisite_has_explicit_in_place_skips");
            }
            string[] legacy = ["claude", "celar_ai", "openai", "local_template"];
            Check(CelarAiRouteExecutionPolicy.ReadSavedOrder(legacy).SequenceEqual(legacy), "reading_legacy_route_never_inserts_deepseek");
            foreach (var invalid in new string[][] { [], ["unknown", "local_template"], ["claude", "claude", "local_template"], ["local_template", "claude"] })
            {
                var rejected = false;
                try { _ = CelarAiRouteExecutionPolicy.ReadSavedOrder(invalid); }
                catch (InvalidOperationException exception) when (exception.Message == "module064_saved_route_invalid") { rejected = true; }
                Check(rejected, "invalid_saved_route_cannot_be_replaced_by_default_order");
            }
            Console.WriteLine($"MODULE064_ALL_CAPABILITY_SEQUENCE_TESTS=PASS checks={checks}");
        }
        finally { foreach (var pair in prior) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    }

    private sealed class Provider(string code, List<string> calls) : IProjectPulseAiProvider
    {
        public string Code => code;
        internal bool RawSourceSeen;
        public Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiGenerationRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            calls.Add(code);
            RawSourceSeen |= request.SystemPrompt.Contains("private-sentinel") || request.UserPrompt.Contains("private-sentinel");
            return Task.FromResult(new ProjectPulseAiProviderResult(code, "unavailable", null, "synthetic_unavailable", null, null, null, 503));
        }
        public Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class NoNetwork : IHttpClientFactory
    { public HttpClient CreateClient(string name) => throw new InvalidOperationException("A sequence regression must not use the network."); }
}
