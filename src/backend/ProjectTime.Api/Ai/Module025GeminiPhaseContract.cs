using System.Text.Json.Nodes;

namespace ProjectTime.Api.Ai;

// Google supports the canonical field/type shape and array/numeric bounds.
// Unsupported pattern/exclusiveMinimum remain semantic server requirements.
// https://ai.google.dev/gemini-api/docs/structured-output#json_schema_support
internal static class Module025GeminiPhaseContract
{
    internal static JsonObject Schema(string phase)
    {
        var schema = Module025PhaseOutputContract.Schema(phase);
        Adapt(schema);
        return schema;
    }

    private static void Adapt(JsonObject node)
    {
        var descriptions = new List<string>();
        if (node.Remove("pattern", out var pattern))
            descriptions.Add($"Required text pattern: {pattern!.GetValue<string>()}.");
        if (node.Remove("exclusiveMinimum", out var minimum))
        {
            node["minimum"] = minimum!.DeepClone();
            descriptions.Add($"Must be strictly greater than {minimum.ToJsonString()}.");
        }
        if (descriptions.Count > 0) node["description"] = string.Join(" ", descriptions);
        if (node["properties"] is JsonObject properties)
            foreach (var property in properties) Adapt(property.Value!.AsObject());
        if (node["items"] is JsonObject items) Adapt(items);
    }
}
