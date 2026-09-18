using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectTime.Api.Ai;

internal static class Module025StructuredContractTests
{
    internal static void Run(PulseAiPrivateFlowHivePlan fixture, CelarAiAuthoritativeScopeEvidence evidence,
        PulseAiEscalationSanitizer sanitizer)
    {
        static void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("ASSERTION_FAILED " + label); Console.WriteLine("ASSERTION_PASSED " + label); }
        var accepted = new List<PulseAiPrivateFlowHivePlan>();
        var exports = new JsonArray();
        JsonObject? planWire = null;
        foreach (var phase in Module025GenerationEngine.Phases)
        {
            var adapter = Module025ExternalSowAdapter.TryCreate(evidence with {
                PhaseExecution = new Module025PhaseExecution(phase, 0, (_, _) => Task.CompletedTask)
            })!;
            var wire = JsonSerializer.SerializeToNode(fixture with {
                Tasks = fixture.Tasks.Where(task => task.Phase == phase).ToArray()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            // DTO null collections are serialized as empty collections on the
            // canonical provider wire; no generated text is filled or shortened.
            foreach (var task in wire["tasks"]!.AsArray())
                foreach (var property in task!.AsObject().ToArray())
                    if (property.Value is null) task[property.Key] = new JsonArray();
            var schema = Module025PhaseOutputContract.Schema(phase);
            var claudeSchema = Module025PhaseOutputContract.ClaudeSchema(phase);
            Check(SameFields<PulseAiPrivateFlowHivePlan>(claudeSchema)
                && SameFields<PulseAiPrivateFlowHiveTask>(claudeSchema["properties"]!["tasks"]!["items"]!.AsObject())
                && SameFields<PulseAiPrivateFlowHiveMilestone>(claudeSchema["properties"]!["milestones"]!["items"]!.AsObject()),
                "claude_wire_schema_preserves_all_fields_and_types_" + phase);
            void CheckClaudeConstraints(JsonNode node)
            {
                if (node is JsonObject obj)
                {
                    Check(!new[] { "minimum", "maximum", "exclusiveMinimum", "maxItems", "pattern" }.Any(obj.ContainsKey)
                        && (obj["minItems"] is not { } min || min.GetValue<int>() <= 1),
                        "claude_schema_uses_supported_constraints");
                    foreach (var child in obj.Select(p => p.Value).Where(v => v is not null)) CheckClaudeConstraints(child!);
                }
                else if (node is JsonArray array)
                    foreach (var child in array.Where(v => v is not null)) CheckClaudeConstraints(child!);
            }
            CheckClaudeConstraints(claudeSchema);
            Check(JsonNode.DeepEquals(schema, Module025PhaseOutputContract.Schema(phase)),
                "claude_wire_adaptation_does_not_mutate_canonical_validation_schema");
            Check(SameFields<PulseAiPrivateFlowHivePlan>(schema)
                && SameFields<PulseAiPrivateFlowHiveTask>(schema["properties"]!["tasks"]!["items"]!.AsObject())
                && SameFields<PulseAiPrivateFlowHiveMilestone>(schema["properties"]!["milestones"]!["items"]!.AsObject()),
                "sow_schema_covers_entire_document_and_task_contract_" + phase);
            using var json = JsonDocument.Parse(wire.ToJsonString());
            Check(Module025PhaseOutputContract.Diagnose(json.RootElement, phase) is null,
                "detailed_fixture_satisfies_canonical_schema_" + phase);
            Check(adapter.Validate(wire.ToJsonString(), "openai", "contract-test", sanitizer, out var code),
                "canonical_phase_passes_real_privacy_and_semantic_parser_" + phase + "_" + code);
            accepted.Add(adapter.AcceptedAnswer!.FlowHivePlan!);
            exports.Add(new JsonObject { ["schema"] = schema.DeepClone(), ["instance"] = wire.DeepClone() });
            if (phase == "Plan") planWire = wire;
        }
        var merged = PulseAiPrivateRagService.AssembleModule025PhasePlans(accepted);
        Check(merged.Tasks.Count == fixture.Tasks.Count
            && merged.Tasks.SelectMany(t => t.DetailedSteps ?? []).SequenceEqual(fixture.Tasks.SelectMany(t => t.DetailedSteps ?? []))
            && merged.Tasks.SelectMany(t => t.AcceptanceCriteria ?? []).SequenceEqual(fixture.Tasks.SelectMany(t => t.AcceptanceCriteria ?? []))
            && merged.Tasks.Sum(t => t.EstimatedHours) == fixture.Tasks.Sum(t => t.EstimatedHours),
            "canonical_five_phase_assembly_preserves_steps_acceptance_and_effort");
        Check(PulseAiPrivateRagService.ValidateModule025Phase(merged, null, evidence).Tasks.Count == fixture.Tasks.Count, "canonical_assembled_document_passes_same_acceptance_contract");

        var rejectingAdapter = Module025ExternalSowAdapter.TryCreate(evidence)!;
        void Reject(Action<JsonObject> change, string category, string field)
        {
            var wire = planWire!.DeepClone().AsObject(); change(wire);
            var result = rejectingAdapter.ValidateResult(new("openai", "success", wire.ToJsonString(), null, null,
                null, new(100, 6000, 6100), 200) { SowDiagnostics = new() { ResponseStatus = "completed" } }, "contract-test", sanitizer);
            Check(!result.IsSuccess && result.Content is null && result.Code == "module025_external_phase_contract_invalid"
                && result.SowDiagnostics?.OutputValidationCategory == category
                && result.SowDiagnostics.OutputValidationField == field
                && result.SowDiagnostics.ResponseStatus == "completed" && result.Usage?.OutputTokens == 6000,
                "rejection_has_precise_safe_diagnostic_" + category + "_" + field + "_actual_"
                    + result.SowDiagnostics?.OutputValidationCategory + "_" + result.SowDiagnostics?.OutputValidationField);
        }
        Reject(w => w["milestones"] = new JsonArray("Readiness review"), "expected_object", "$.milestones[0]");
        Reject(w => w["milestones"] = new JsonArray(new JsonObject {
            ["name"] = "Readiness review", ["description"] = "Review the technology readiness evidence and unresolved dependencies before proceeding.",
            ["acceptanceEvidence"] = new JsonArray("Signed readiness checklist")
        }), "milestone_detail", "$.milestones[0].proposedTiming");
        Reject(w => w["milestones"] = new JsonArray(new JsonObject {
            ["name"] = "Readiness review", ["description"] = "Too brief", ["proposedTiming"] = "After prerequisite review",
            ["acceptanceEvidence"] = new JsonArray("Signed readiness checklist")
        }), "milestone_detail", "$.milestones[0].description");
        Reject(w => { w["tasks"]![0]!["inputs"] = new JsonArray(); w["tasks"]![0]!["prerequisites"] = new JsonArray(); }, "task_detail", "$.tasks[0].inputs");
        Reject(w => w["tasks"]![0]!["acceptanceCriteria"] = new JsonArray(), "task_detail", "$.tasks[0].acceptanceCriteria");
        Reject(w => w["tasks"]![0]!["inputs"] = new JsonArray(42), "expected_string", "$.tasks[0].inputs[0]");
        Reject(w => w["tasks"]![0]!["phase"] = "Release", "unrequested_phase", "$.tasks[0].phase");
        Reject(w => w["tasks"]![1]!["wbs"] = w["tasks"]![0]!["wbs"]!.DeepClone(), "duplicate_wbs", "$.tasks");
        Reject(w => { foreach (var task in w["tasks"]!.AsArray()) task!["description"] = w["tasks"]![0]!["description"]!.DeepClone(); },
            "phase_distinct_descriptions", "$.tasks");
        Reject(w => w["tasks"]![0]!["name"] = "Prepare the cited scope", "prohibited_boilerplate", "$.tasks[0]");
        Check(!rejectingAdapter.Validate("{malformed", "openai", "test", sanitizer, out _)
            && rejectingAdapter.ValidationCategory == "invalid_json" && rejectingAdapter.ValidationField == "$",
            "syntax_error_is_distinct_from_completed_but_rejected_contract");
        using var unsafeName = JsonDocument.Parse(planWire!.ToJsonString().Replace("\"inputs\"", "\"private@example.invalid\""));
        var safePath = Module025PhaseOutputContract.Diagnose(unsafeName.RootElement, "Plan");
        Check(safePath?.Field == "$.tasks[0].inputs" && !JsonSerializer.Serialize(safePath).Contains("example.invalid"),
            "missing_field_diagnostics_never_copy_provider_property_names");
        if (Environment.GetEnvironmentVariable("MODULE025_SCHEMA_TEST_EXPORT") is { Length: > 0 } export)
            File.WriteAllText(export, exports.ToJsonString());
        Console.WriteLine("MODULE025_STRUCTURED_CONTRACT_TESTS=PASS");
    }

    private static bool SameFields<T>(JsonObject schema) => schema["additionalProperties"]!.GetValue<bool>() == false
        && schema["properties"]!.AsObject().Select(p => p.Key).Order().SequenceEqual(
            typeof(T).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order())
        && schema["required"]!.AsArray().Select(p => p!.GetValue<string>()).Order().SequenceEqual(schema["properties"]!.AsObject().Select(p => p.Key).Order());
}
