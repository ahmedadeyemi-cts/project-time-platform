using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

// Synthetic contract tests are not evidence of successful live model generation.
var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
var flags = new[] { "PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", "PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED" };
var previous = flags.ToDictionary(x => x, Environment.GetEnvironmentVariable);
try
{
    foreach (var flag in flags) Environment.SetEnvironmentVariable(flag, "true");
    const string scope = "Upgrade Example Custom Platform from version 14 to 15 without changing the dial plan. Include only readiness, implementation, testing and handover. Exclude hardware procurement.\nAfter-hours implementation is required.";
    var evidence = new CelarAiAuthoritativeScopeEvidence(Guid.NewGuid(), 8, "RECORD-NOT-TRANSMITTED", "CUSTOMER-NOT-TRANSMITTED", scope,
        DateTimeOffset.UtcNow, new("Plan", 0, (_, _) => Task.CompletedTask)) { ServiceScopeOnly = true };
    var sanitizer = new PulseAiEscalationSanitizer();
    var adapter = Module025ExternalSowAdapter.TryCreate(evidence)!;
    Check(adapter is not null && adapter.UsesFullServiceScope, "unknown_technology_and_exclusions_are_valid_scope_input");
    Check(Module025ExternalSowAdapter.TryCreate(evidence with { ServiceScopeOnly = false }) is null, "legacy_keyword_contract_is_not_silently_broadened");
    foreach (var phase in Module025GenerationEngine.Phases)
    foreach (var external in new[] { true, false })
    {
        var input = evidence with { PhaseExecution = new(phase, 0, (_, _) => Task.CompletedTask) };
        var request = Module025ServiceScopePolicy.Request(input, external);
        using var payload = JsonDocument.Parse(request.UserPrompt);
        Check(payload.RootElement.EnumerateObject().Count() == 1 && payload.RootElement.GetProperty("serviceScope").GetString() == scope,
            "exact_scope_only_payload_" + phase + "_" + external);
        Check(!request.UserPrompt.Contains(evidence.CustomerName) && !request.SystemPrompt.Contains(evidence.EngagementNumber)
            && request.StructuredSowPhase && request.BoundedPrivatePhase && request.SowPhase == phase,
            "record_identity_excluded_and_phase_owned_by_server_" + phase + "_" + external);
        Check(request.MaxOutputTokens == (external ? 12288 : 6144), "existing_output_ceiling_" + phase + "_" + external);
    }
    foreach (var invalid in new[] { "", "too short", new string('a', 30_001) })
        Check(!Module025ServiceScopePolicy.ValidInput(invalid), "empty_short_or_oversized_input_rejected_without_truncation");
    Check(Module025ServiceScopePolicy.ValidInput(new string('a', 30_000)), "complete_maximum_input_is_not_reduced_to_keywords");
    string[] order = ["gemini", "claude", "openai", "deepseek_v4", "celar_ai", "local_template"];
    var definition = CelarAiCapabilityCatalog.Definitions[CelarAiCapabilityCatalog.SowGsdPlanning];
    var route = new CelarAiCapabilityRouteSnapshot(definition.FeatureCode, definition.DisplayName, definition.ConsumerModules,
        definition.ExternalContextPolicy, definition.ContextClassification, order, 9, DateTimeOffset.UtcNow, null, true)
        { SanitizedExternalGenerationApproved = true, ExternalGenerationApprovalSchemaReady = true, ServiceScopeApprovalSchemaReady = true };
    Check(!Module025ServiceScopePolicy.Approved(route), "old_sanitized_approval_is_not_full_text_consent");
    route = route with { ServiceScopeFullTextApproved = true };
    Check(Module025ServiceScopePolicy.Approved(route), "separate_persisted_full_text_consent_accepted");
    Check(!Module025ServiceScopePolicy.Approved(route with { ServiceScopeApprovalSchemaReady = false }), "migration_required_for_full_text_consent");
    Check(!Module025ServiceScopePolicy.Approved(route with { Persisted = false }), "default_policy_cannot_opt_in_full_text");
    Check(!Module025ServiceScopePolicy.Approved(route with { FeatureCode = CelarAiCapabilityCatalog.ProjectFlowHivePlan }), "scope_consent_does_not_grant_flowhive_document_disclosure");
    foreach (var flag in flags)
    {
        Environment.SetEnvironmentVariable(flag, "false");
        Check(!Module025ServiceScopePolicy.Approved(route), "deployment_policy_remains_authoritative_" + flag);
        Environment.SetEnvironmentVariable(flag, "true");
    }

    var plan = Fixture.Plan("Plan");
    var json = plan.ToJsonString();
    Check(adapter!.Validate(json, "gemini", "synthetic", sanitizer, out var code), "complete_expanded_overview_and_tasks_accepted_" + code);
    Check(adapter.AcceptedAnswer?.FlowHivePlan?.Objective.Length >= 600, "expanded_overview_retained");
    Check(adapter.AcceptedAnswer!.Citations.Single().SourceSha256 == PulseAiPrivateRagService.CreateModule025AuthoritativeScopeSource(evidence)!.SourceSha256,
        "output_cites_exact_saved_scope_not_invented_vendor_evidence");
    foreach (var mutation in new Action<JsonObject>[] {
        x => x["objective"] = scope, x => x["objective"] = "Too short", x => x["tasks"] = new JsonArray(),
        x => x["secret-unknown-key"] = "untrusted content", x => x["tasks"]![0]!["phase"] = "Release",
        x => x["tasks"]![0]!["estimatedHours"] = 0, x => x["tasks"]![0]!["citationIds"] = new JsonArray(99) })
    {
        var invalid = (JsonObject)plan.DeepClone(); mutation(invalid);
        Check(!adapter.Validate(invalid.ToJsonString(), "gemini", "test", sanitizer, out _), "invalid_phase_or_unexpanded_overview_rejected");
        Check(adapter.AcceptedAnswer is null, "rejected_result_cannot_reuse_prior_accepted_output");
    }
    Check(!adapter.Validate(json[..^10], "gemini", "test", sanitizer, out _), "truncated_json_is_not_accepted");
    var refused = new ProjectPulseAiProviderResult("gemini", "refusal", null, "synthetic_refusal", null, null, null, 400);
    Check(adapter.ValidateResult(refused, "test", sanitizer).IsRefusal, "terminal_provider_refusal_is_preserved");

    foreach (var approved in new[] { true, false })
    foreach (var stopWithRefusal in new[] { true, false })
    {
        var calls = new List<string>();
        var configuration = new ProjectPulseAiConfiguration();
        var providers = new[] { "gemini", "claude", "openai" }.Select(name =>
            new Fixture.Provider(name, calls, name == "gemini" && stopWithRefusal ? "refusal" : name == "openai" ? "success" : "failure", json)).ToArray();
        foreach (var provider in providers)
        {
            configuration.ApplyStoredSecret(provider.Code, "synthetic-not-a-key", "test", DateTimeOffset.UtcNow);
            configuration.ApplyStoredEnabled(provider.Code, true);
        }
        // Only external targets are in this injected route; no private endpoint may be contacted.
        using var store = new CelarAiCapabilityRoutingStore(NullLogger<CelarAiCapabilityRoutingStore>.Instance);
        var router = new CelarAiCapabilityRouter(store, new(new Fixture.NoNetwork(), NullLogger<CelarAiPrivateGenerationTarget>.Instance),
            configuration, new(configuration), sanitizer, providers, new(), NullLogger<CelarAiCapabilityRouter>.Instance);
        var localAdapter = Module025ExternalSowAdapter.TryCreate(evidence)!;
        var execution = new CelarAiCapabilityExecutionContext(definition.FeatureCode, false, false, false, false, false, [], "025", "test",
            StructuredSowPhase: true, BeforeStructuredSowAttempt: evidence.PhaseExecution!.BeforeAttemptAsync) { ExternalSow = localAdapter };
        var selected = route with { Targets = ["gemini", "claude", "openai", "local_template"], ServiceScopeFullTextApproved = approved };
        var result = await router.GenerateForRouteAsync(Module025ServiceScopePolicy.Request(evidence, true), execution, selected,
            () => throw new Exception("A template must not complete the SOW"), false, null);
        Check(calls.SequenceEqual(!approved ? Array.Empty<string>() : stopWithRefusal ? new[] { "gemini" } : new[] { "gemini", "claude", "openai" }),
            "real_shared_router_obeys_saved_order_approval_and_refusal_" + approved + "_" + stopWithRefusal);
        if (approved && !stopWithRefusal) Check(result.Outcome == "success" && localAdapter.AcceptedAnswer is not null, "router_accepts_only_validated_phase");
        if (!approved) Check(result.TargetDecisions!.Take(3).All(d => d.ReasonCode == "module025_full_service_scope_approval_required"), "unapproved_targets_have_explicit_skip_codes");
        foreach (var provider in providers.Where(p => p.LastRequest is not null))
            Check(JsonDocument.Parse(provider.LastRequest!.UserPrompt).RootElement.GetProperty("serviceScope").GetString() == scope, "fallback_preserves_exact_input_" + provider.Code);
    }

    var row = Fixture.Row() with { ServiceScope = scope, ServiceOverview = "Reviewed overview", ServiceOverviewManuallyEdited = true };
    Check(Module025ServiceScopeWorkspace.WorkingOverview(row, "New AI proposal") == "Reviewed overview", "manual_overview_is_not_overwritten");
    Check(Module025ServiceScopeWorkspace.WorkingOverview(row with { ServiceOverviewManuallyEdited = false }, "New AI proposal") == "New AI proposal", "unreviewed_overview_can_receive_proposal");
    Check(!Module025ServiceScopeWorkspace.SourceChanged(row, scope, "Edited overview"), "overview_edit_is_not_a_source_scope_change");
    Check(Module025ServiceScopeWorkspace.SourceChanged(row, scope + " Include records.", row.ServiceOverview), "scope_edit_marks_generated_content_stale");
    Check(Module025ServiceScopeWorkspace.SourceChanged(row with { ServiceScope = null }, scope, row.ServiceOverview), "legacy_adoption_is_explicit_source_change");
    Check((row with { ServiceScope = null }).EffectiveServiceScope == row.ServiceOverview, "legacy_records_keep_old_input_contract");
    await DatabaseChecks.RunAsync(Check, row);
    Console.WriteLine($"MODULE025_SERVICE_SCOPE_TESTS=PASS checks={checks}; live_model_evidence=false");
}
finally { foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }

internal static class Fixture
{
    internal static Module025EngagementRow Row() => new(Guid.NewGuid(), "SOW-TEST", Guid.NewGuid(), "Author", "Engineering", "Team", null,
        "Customer", "manual", "time_and_materials", "standard", "standard", null, "AE", null, "Inside sales", "Legacy overview", "Project",
        JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), "draft", true, 1, null, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
    internal static JsonObject Plan(string phase)
    {
        var properties = Module025PhaseOutputContract.Schema(phase)["properties"]!.AsObject();
        var plan = (JsonObject)Make(Module025PhaseOutputContract.Schema(phase));
        plan["objective"] = "This proposed engagement covers the requested platform upgrade, from readiness assessment through design, controlled implementation, verification and operational handover. The work will preserve the existing dial plan and excludes hardware procurement. Delivery activities and effort remain proposals for review, not a claim of completed work or guaranteed compatibility.\n\nThe planning work will confirm the information necessary to establish a supported transition, identify dependencies, and agree the acceptance evidence and after-hours implementation conditions. The delivery team will prepare an implementation approach with recovery prerequisites and decision checkpoints, then document validation outcomes and operational handover information. Actual environment quantities, compatibility and final change timing remain open questions for the Solution Architect and customer to resolve before approval.";
        plan["outOfScopeItems"] = new JsonArray("Hardware procurement is excluded; the existing dial plan must be preserved.");
        for (var i = 0; i < 2; i++)
        {
            var task = plan["tasks"]![i]!;
            task["wbs"] = $"{Array.IndexOf(Module025GenerationEngine.Phases, phase) + 1}.{i + 1}";
            task["name"] = i == 0 ? "Establish platform readiness evidence" : "Coordinate change and acceptance requirements";
            task["description"] = i == 0 ? "Inspect the supplied platform inventory and version transition requirements, identify evidence needed to assess compatibility, and document unresolved technical prerequisites without claiming that checks have already passed." : "Develop the proposed change coordination and acceptance approach, clarify the responsibilities for readiness decisions, and record the conditions that must be resolved before execution may begin.";
            task["detailedSteps"] = i == 0 ? new JsonArray("Review the supplied current-state inventory and record outstanding compatibility questions.", "Document evidence needed to verify the proposed target state and preserve the current dial plan.") : new JsonArray("Identify the requested after-hours work window and the responsible change reviewers.", "Agree proposed acceptance evidence and record unresolved approval dependencies.");
            task["outputs"] = new JsonArray(i == 0 ? "Readiness assessment with unresolved technical prerequisites." : "Change coordination and acceptance review record.");
        }
        return plan;
    }
    private static JsonNode Make(JsonObject schema)
    {
        if (schema["enum"] is JsonArray choices) return choices[0]!.DeepClone();
        return schema["type"]!.GetValue<string>() switch {
            "object" => new JsonObject(schema["properties"]!.AsObject().Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, Make(p.Value!.AsObject())))),
            "array" => new JsonArray(Enumerable.Range(0, schema["minItems"]?.GetValue<int>() ?? 0).Select(_ => (JsonNode?)Make(schema["items"]!.AsObject())).ToArray()),
            "boolean" => JsonValue.Create(true)!, "number" => JsonValue.Create(schema["maximum"] is not null ? 0.8m : 4m)!,
            "integer" => JsonValue.Create(1)!, _ => JsonValue.Create("Review the proposed technical requirements and retain objective evidence for the authorized delivery decision. Unknown facts require review before proceeding.")!
        };
    }
    internal sealed class Provider(string code, List<string> calls, string outcome, string content) : IProjectPulseAiProvider
    {
        public string Code => code;
        internal ProjectPulseAiGenerationRequest? LastRequest;
        public Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiGenerationRequest request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); calls.Add(code); LastRequest = request; return Task.FromResult(new ProjectPulseAiProviderResult(code, outcome, outcome == "success" ? content : null, "synthetic_result", null, null, null, 200)); }
        public Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken token) => throw new NotSupportedException();
    }
    internal sealed class NoNetwork : IHttpClientFactory
    { public HttpClient CreateClient(string name) => throw new Exception("Synthetic tests cannot contact a model"); }
}
