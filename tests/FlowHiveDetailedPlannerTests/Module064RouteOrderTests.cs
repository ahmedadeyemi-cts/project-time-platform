using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;

internal static class Module064RouteOrderTests
{
    internal static CelarAiCapabilityRouteSnapshot Route(bool approved, IReadOnlyList<string>? targets = null) =>
        new(CelarAiCapabilityCatalog.SowGsdPlanning, "SOW/GSD", ["025"], "sanitized_generic_only",
            "private_documents", targets ?? CelarAiCapabilityTargets.DefaultOrder, 2, DateTimeOffset.UtcNow,
            Guid.NewGuid(), true) { SanitizedExternalGenerationApproved = approved, ExternalGenerationApprovalSchemaReady = true };

    internal static async Task RunAsync(PulseAiPrivateFlowHivePlan fixture,
        CelarAiAuthoritativeScopeEvidence evidence, PulseAiEscalationSanitizer sanitizer)
    {
        static void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("ASSERTION_FAILED " + label); Console.WriteLine("ASSERTION_PASSED " + label); }
        string[] saved = ["gemini", "claude", "openai", "deepseek_v4", "celar_ai", "local_template"];
        var before = Environment.GetEnvironmentVariable("PROJECTPULSE_MODULE025_PAID_FALLBACK_ENABLED");
        var policyBefore = Environment.GetEnvironmentVariable("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION");
        var privateEnvironment = new Dictionary<string, string?> {
            ["PROJECTPULSE_CELAR_AI_ENABLED"] = "true",
            ["PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT"] = "https://synthetic.invalid/v1/chat/completions",
            ["PROJECTPULSE_PRIVATE_INFERENCE_MODEL"] = "synthetic-model",
            ["PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN"] = "synthetic-only"
        };
        var previousPrivate = privateEnvironment.ToDictionary(pair => pair.Key, pair => Environment.GetEnvironmentVariable(pair.Key));
        try
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_MODULE025_PAID_FALLBACK_ENABLED", "false");
            var approved = Route(true, saved);
            Check(CelarAiRouteExecutionPolicy.ExternalGenerationApproved(approved), "persisted_explicit_approval_replaces_legacy_paid_flag");
            Check(CelarAiRouteExecutionPolicy.Order(approved, true,
                CelarAiRouteExecutionPolicy.ClosedSowMayUseSavedOrder(approved, true, true)).SequenceEqual(saved),
                "approved_closed_sow_keeps_saved_gemini_first_order_with_private_financial_context");
            Check(CelarAiRouteExecutionPolicy.Order(approved, true,
                CelarAiRouteExecutionPolicy.ClosedSowMayUseSavedOrder(approved, true, false)).SequenceEqual(saved),
                "unsupported_capsule_changes_eligibility_not_saved_sequence");
            Check(!CelarAiRouteExecutionPolicy.ClosedSowMayUseSavedOrder(approved, false, true),
                "approval_never_reclassifies_ordinary_private_documents");
            Check(!CelarAiRouteExecutionPolicy.ExternalGenerationApproved(approved with { Persisted = false }),
                "unsaved_approval_cannot_authorize_provider_spend");
            Environment.SetEnvironmentVariable("PROJECTPULSE_MODULE025_PAID_FALLBACK_ENABLED", "true");
            Check(!CelarAiRouteExecutionPolicy.ExternalGenerationApproved(Route(false, saved)),
                "legacy_environment_flag_cannot_approve_an_active_route");
            var release = approved with { DeploymentManaged = true, SanitizedExternalGenerationApproved = false };
            Check(CelarAiRouteExecutionPolicy.ExternalGenerationApproved(release)
                && !CelarAiRouteExecutionPolicy.ClosedSowMayUseSavedOrder(release, true, true),
                "release_qualification_keeps_existing_explicit_paid_gate_and_order");
            var flowHive = approved with { FeatureCode = CelarAiCapabilityCatalog.ProjectFlowHivePlan };
            Check(!CelarAiRouteExecutionPolicy.ExternalGenerationApproved(flowHive)
                && !CelarAiRouteExecutionPolicy.ApprovalEditable(flowHive),
                "sow_approval_does_not_authorize_structured_external_flowhive");
            using (var summary = JsonDocument.Parse(JsonSerializer.Serialize(flowHive.ToPublicResponse())))
                Check(summary.RootElement.GetProperty("executionPolicy").GetProperty("blockers")
                    .EnumerateArray().Any(x => x.GetString() == "structured_external_flowhive_unavailable"),
                    "flowhive_generic_assistance_limitation_is_visible_in_route_response");
            Environment.SetEnvironmentVariable("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", "false");
            Check(!CelarAiRouteExecutionPolicy.ClosedSowMayUseSavedOrder(approved, true, true),
                "route_approval_cannot_override_deployment_privacy_policy");
            Environment.SetEnvironmentVariable("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", "true");
            Environment.SetEnvironmentVariable("PROJECTPULSE_MODULE025_PAID_FALLBACK_ENABLED", "false");

            // Exercise the actual route executor with a valid structured phase,
            // invalid output, refusal, and an unapproved route. No network calls.
            foreach (var pair in privateEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            await Module064SequenceTests.RunAsync(sanitizer);
            foreach (var scenario in new[] { "success", "invalid_then_fallback", "refusal", "unapproved", "fifth_private", "unconfigured_skips" })
            {
                var events = new List<Module025GenerationProgress>();
                var phase = new Module025PhaseExecution("Plan", 0, (progress, _) => { events.Add(progress); return Task.CompletedTask; });
                var adapter = Module025ExternalSowAdapter.TryCreate(evidence with { PhaseExecution = phase })!;
                var capsule = adapter.Prepare(sanitizer, out _)!;
                var valid = JsonSerializer.Serialize(fixture with { Tasks = fixture.Tasks.Where(t => t.Phase == "Plan").ToArray() });
                var gemini = new Provider("gemini", scenario == "refusal" ? "refusal" : scenario == "fifth_private" ? "failure" : "success",
                    scenario == "invalid_then_fallback" ? "{}" : valid);
                var claude = new Provider("claude", scenario == "fifth_private" ? "failure" : "success", valid);
                var openai = new Provider("openai", "failure", "");
                var deepseek = new Provider("deepseek_v4", "failure", "");
                var configuration = new ProjectPulseAiConfiguration();
                foreach (var name in new[] { "gemini", "claude", "openai", "deepseek_v4" })
                { configuration.ApplyStoredSecret(name, "synthetic-only", "test", DateTimeOffset.UtcNow); configuration.ApplyStoredEnabled(name, true); }
                if (scenario == "unconfigured_skips") configuration.ApplyStoredEnabled("gemini", false);
                using var store = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance);
                var router = new CelarAiCapabilityRouter(store,
                    new CelarAiPrivateGenerationTarget(new NoNetwork(), NullLogger<CelarAiPrivateGenerationTarget>.Instance),
                    configuration, new ProjectPulseAiHealthRegistry(configuration), sanitizer, [gemini, claude, openai, deepseek],
                    new CelarAiConsumerAssuranceRegistry(), NullLogger<CelarAiCapabilityRouter>.Instance);
                var context = new CelarAiCapabilityExecutionContext(CelarAiCapabilityCatalog.SowGsdPlanning,
                    true, true, false, true, false, [evidence.CustomerName], "025", "synthetic-route",
                    StructuredSowPhase: true, BeforeStructuredSowAttempt: phase.BeforeAttemptAsync)
                    { ExternalSow = adapter };
                var result = await router.GenerateForRouteAsync(capsule with { UserPrompt = evidence.ServiceOverview },
                    context, Route(scenario != "unapproved", saved),
                    () => throw new InvalidOperationException("A template must never complete a structured SOW."),
                    false, _ => {
                        var target = ProjectPulseDeepSeekProvider.PrivateTarget!;
                        var success = scenario == "fifth_private" && target == "celar_ai";
                        return Task.FromResult(new ProjectPulseAiProviderResult(target, success ? "success" : "failure", success ? valid : null,
                            success ? null : "synthetic_unavailable", null, null, null, null));
                    });
                if (scenario == "success")
                    Check(result.Provider == "gemini" && result.Outcome == "success" && gemini.Calls == 1 && claude.Calls == 0,
                        "real_router_generates_valid_phase_with_saved_gemini_first_order");
                else if (scenario == "invalid_then_fallback")
                    Check(result.Provider == "claude" && result.Outcome == "success" && gemini.Calls == 1 && claude.Calls == 1,
                        "invalid_phase_uses_next_saved_provider_without_accepting_partial_output");
                else if (scenario == "refusal")
                    Check(result.Outcome == "refusal" && claude.Calls == 0,
                        "saved_order_preserves_terminal_safety_refusal");
                else if (scenario == "fifth_private")
                    Check(result.Provider == "celar_ai" && result.Outcome == "success"
                        && events.Where(e => e.Stage == "provider_started").Select(e => e.Provider).SequenceEqual(saved.Take(5)),
                        "fifth_celar_target_remains_reachable_after_four_provider_failures");
                else if (scenario == "unconfigured_skips")
                    Check(result.Provider == "claude" && result.Outcome == "success" && gemini.Calls == 0
                        && events.Count(e => e.Stage == "provider_started") == 1,
                        "skipped_providers_do_not_consume_the_durable_attempt_budget");
                else
                    Check(result.Outcome != "success" && gemini.Calls == 0 && claude.Calls == 0,
                        "unapproved_saved_order_never_calls_paid_providers");
                if (gemini.LastRequest is { } sent)
                    Check(sent.UserPrompt == capsule.UserPrompt && !sent.UserPrompt.Contains(evidence.CustomerName)
                        && !sent.UserPrompt.Contains("private@example.invalid") && !sent.UserPrompt.Contains("20000"),
                        "saved_order_never_sends_raw_source_identity_or_commercial_values_" + scenario);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_MODULE025_PAID_FALLBACK_ENABLED", before);
            Environment.SetEnvironmentVariable("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", policyBefore);
            foreach (var pair in previousPrivate) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    private sealed class Provider(string code, string outcome, string content) : IProjectPulseAiProvider
    {
        public string Code => code;
        internal int Calls;
        internal ProjectPulseAiGenerationRequest? LastRequest;
        public Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiGenerationRequest request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; LastRequest = request; return Task.FromResult(new ProjectPulseAiProviderResult(code, outcome, content, outcome == "refusal" ? "synthetic_safety_refusal" : null, null, null, null, 200)); }
        public Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class NoNetwork : IHttpClientFactory
    { public HttpClient CreateClient(string name) => throw new InvalidOperationException("Synthetic route tests must not call a provider."); }
}
