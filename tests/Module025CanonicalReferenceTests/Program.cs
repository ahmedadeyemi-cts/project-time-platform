using System.Text.Json.Nodes;
using ProjectTime.Api.Ai;

// Synthetic contract tests for the Module 025 SOW reference-source feature
// (Phase 1, canonical templates). These assert the citation/neutralization/kill-
// switch contract only; they are not evidence of live model generation.
var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }

const string killSwitch = "PROJECTPULSE_MODULE025_REFERENCE_SOURCES_ENABLED";
var previousKillSwitch = Environment.GetEnvironmentVariable(killSwitch);
try
{
    // --- Kill-switch (default OFF) ---
    Environment.SetEnvironmentVariable(killSwitch, null);
    Check(!Module025ReferenceSourcePolicy.Enabled, "kill_switch_defaults_off");
    Environment.SetEnvironmentVariable(killSwitch, "true");
    Check(Module025ReferenceSourcePolicy.Enabled, "kill_switch_opt_in_enables_feature");
    Environment.SetEnvironmentVariable(killSwitch, "false");
    Check(!Module025ReferenceSourcePolicy.Enabled, "kill_switch_explicit_off_disables_feature");

    // --- Neutralization + fail-closed on residual identity ---
    const string cleanTemplate = "This canonical template covers the standard upgrade lifecycle. Plan discovery and readiness. Design the target state. Implement the change with rollback readiness. Validate the outcome. Release the documentation and handover.";
    var reference = new Module025ReferenceSource(Module025ReferenceKind.Canonical, Guid.NewGuid(), "Reference Template", cleanTemplate, DateTimeOffset.UtcNow);
    var referenceChunk = PulseAiPrivateRagService.CreateModule025ReferenceScopeSource(reference);
    Check(referenceChunk is not null, "clean_template_produces_reference_chunk");
    Check(referenceChunk!.RankOrder == 2 && referenceChunk.SourceType == "module025_reference_scope"
        && referenceChunk.Classification == "author_selected_reference_scope"
        && referenceChunk.CitationAnchor == "Reference Template" && referenceChunk.SectionTitle == "Reference Template",
        "reference_chunk_has_distinct_citation2_provenance");
    Check(referenceChunk.TextSha256.Length == 64 && referenceChunk.SourceSha256.Length == 64
        && referenceChunk.TextSha256 == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(referenceChunk.Text))).ToLowerInvariant(),
        "reference_chunk_hashes_neutralized_text");

    foreach (var identity in new[]
    {
        "Customer: Acme Corporation performed the readiness assessment.",
        "The upgrade was completed by Mr. John Smith for Contoso Industries Inc.",
        "Contact the account owner at admin@contoso.com to confirm the maintenance window."
    })
    {
        var tainted = reference with { ReferenceId = Guid.NewGuid(), ReferenceText = identity + " " + cleanTemplate };
        Check(PulseAiPrivateRagService.CreateModule025ReferenceScopeSource(tainted) is null, "residual_identity_fails_closed");
    }

    // --- Length cap / invalid inputs ---
    Check(PulseAiPrivateRagService.CreateModule025ReferenceScopeSource(reference with { ReferenceId = Guid.NewGuid(), ReferenceText = "too short" }) is null, "short_reference_text_rejected");
    Check(PulseAiPrivateRagService.CreateModule025ReferenceScopeSource(reference with { ReferenceId = Guid.Empty }) is null, "empty_reference_id_rejected");
    var longBody = string.Concat(Enumerable.Repeat("Plan the discovery and readiness activities and record open questions. ", 1_000));
    var oversized = reference with { ReferenceId = Guid.NewGuid(), ReferenceText = longBody };
    Check(longBody.Length > Module025ReferenceSourcePolicy.MaximumReferenceTextCharacters, "oversized_fixture_exceeds_cap");
    var oversizedChunk = PulseAiPrivateRagService.CreateModule025ReferenceScopeSource(oversized);
    Check(oversizedChunk is not null && oversizedChunk.Text.Length <= 6_200, "oversized_reference_text_is_bounded");

    // --- Citation behavior in the SOW/GSD per-phase path ---
    var scope = "Upgrade the standard communications platform from version 14 to 15 without changing the dial plan. Include readiness, implementation, testing and handover.";
    var baseEvidence = new CelarAiAuthoritativeScopeEvidence(Guid.NewGuid(), 8, "RECORD-NOT-TRANSMITTED", "CUSTOMER-NOT-TRANSMITTED",
        scope, DateTimeOffset.UtcNow, new("Plan", 0, (_, _) => Task.CompletedTask)) { ServiceScopeOnly = true };
    var content = Fixture.Plan("Plan").ToJsonString();

    // Regression: absent reference => today's single-citation behavior.
    var withoutReference = PulseAiPrivateRagService.Module025ExternalAnswer(content, baseEvidence, "gemini", "synthetic");
    Check(withoutReference.Citations.Count == 1, "no_reference_keeps_single_citation");
    Check(withoutReference.Citations[0].CitationId == 1 && withoutReference.Citations[0].SourceType == "module025_saved_service_overview",
        "no_reference_citation1_is_saved_service_overview");

    // With reference => co-equal citation 2 appears with distinct provenance.
    var withReference = PulseAiPrivateRagService.Module025ExternalAnswer(content,
        baseEvidence with { Reference = reference }, "gemini", "synthetic");
    Check(withReference.Citations.Count == 2, "reference_adds_coequal_citation2");
    var citation2 = withReference.Citations.Single(c => c.CitationId == 2);
    Check(citation2.SourceType == "module025_reference_scope" && citation2.SectionTitle == "Reference Template" && citation2.CitationAnchor == "Reference Template",
        "citation2_provenance_is_distinct_from_primary");
    Check(withReference.Citations.Single(c => c.CitationId == 1).SourceType == "module025_saved_service_overview",
        "citation1_remains_saved_service_overview_when_reference_present");

    // A reference that fails neutralization fails the SOW path closed.
    var taintedEvidence = baseEvidence with { Reference = reference with { ReferenceId = Guid.NewGuid(), ReferenceText = "Customer: Globex Corporation. " + cleanTemplate } };
    var failedClosed = false;
    try { PulseAiPrivateRagService.Module025ExternalAnswer(content, taintedEvidence, "gemini", "synthetic"); }
    catch (System.Text.Json.JsonException exception) when (exception.Message == "module025_reference_scope_invalid") { failedClosed = true; }
    Check(failedClosed, "sow_path_fails_closed_on_reference_neutralization_failure");

    await DatabaseChecks.RunAsync(Check);
    Console.WriteLine($"MODULE025_CANONICAL_REFERENCE_TESTS=PASS checks={checks}; live_model_evidence=false");
}
finally { Environment.SetEnvironmentVariable(killSwitch, previousKillSwitch); }

internal static class Fixture
{
    // Mirrors the valid single-phase plan shape used by the service-scope suite so
    // Module025ExternalAnswer's strict contract parse succeeds without a live model.
    internal static JsonObject Plan(string phase)
    {
        var plan = (JsonObject)Make(Module025PhaseOutputContract.Schema(phase));
        plan["objective"] = "This proposed engagement covers the requested platform upgrade, from readiness assessment through design, controlled implementation, verification and operational handover. The work will preserve the existing dial plan. Delivery activities and effort remain proposals for review, not a claim of completed work or guaranteed compatibility.\n\nThe planning work will confirm the information necessary to establish a supported transition, identify dependencies, and agree the acceptance evidence and implementation conditions. The delivery team will prepare an implementation approach with recovery prerequisites and decision checkpoints, then document validation outcomes and operational handover information. Actual environment quantities, compatibility and final change timing remain open questions for the Solution Architect and customer to resolve before approval.";
        plan["outOfScopeItems"] = new JsonArray("The existing dial plan must be preserved and is out of scope for change.");
        for (var i = 0; i < 2; i++)
        {
            var task = plan["tasks"]![i]!;
            task["wbs"] = $"{Array.IndexOf(Module025GenerationEngine.Phases, phase) + 1}.{i + 1}";
            task["name"] = i == 0 ? "Establish platform readiness evidence" : "Coordinate change and acceptance requirements";
            task["description"] = i == 0 ? "Inspect the supplied platform inventory and version transition requirements, identify evidence needed to assess compatibility, and document unresolved technical prerequisites without claiming that checks have already passed." : "Develop the proposed change coordination and acceptance approach, clarify the responsibilities for readiness decisions, and record the conditions that must be resolved before execution may begin.";
            task["detailedSteps"] = i == 0 ? new JsonArray("Review the supplied current-state inventory and record outstanding compatibility questions.", "Document evidence needed to verify the proposed target state and preserve the current dial plan.") : new JsonArray("Identify the requested work window and the responsible change reviewers.", "Agree proposed acceptance evidence and record unresolved approval dependencies.");
            task["outputs"] = new JsonArray(i == 0 ? "Readiness assessment with unresolved technical prerequisites." : "Change coordination and acceptance review record.");
        }
        return plan;
    }

    private static JsonNode Make(JsonObject schema)
    {
        if (schema["enum"] is JsonArray choices) return choices[0]!.DeepClone();
        return schema["type"]!.GetValue<string>() switch
        {
            "object" => new JsonObject(schema["properties"]!.AsObject().Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, Make(p.Value!.AsObject())))),
            "array" => new JsonArray(Enumerable.Range(0, schema["minItems"]?.GetValue<int>() ?? 0).Select(_ => (JsonNode?)Make(schema["items"]!.AsObject())).ToArray()),
            "boolean" => JsonValue.Create(true)!,
            "number" => JsonValue.Create(schema["maximum"] is not null ? 0.8m : 4m)!,
            "integer" => JsonValue.Create(1)!,
            _ => JsonValue.Create("Review the proposed technical requirements and retain objective evidence for the authorized delivery decision. Unknown facts require review before proceeding.")!
        };
    }
}
