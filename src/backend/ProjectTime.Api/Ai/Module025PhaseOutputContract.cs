using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

// One canonical wire shape for external SOW phases. Unknown technical facts
// remain open questions; empty optional collections never become invented scope.
internal static class Module025PhaseOutputContract
{
    internal const string Name = "module025_phase_v1";

    internal static JsonObject Schema(string phase)
    {
        if (!Module025GenerationEngine.Phases.Contains(phase))
            throw new ArgumentException("A server-selected SOW phase is required.", nameof(phase));
        var task = new JsonObject {
            ["wbs"] = Text(), ["name"] = Text(), ["description"] = Text(80),
            ["estimatedDurationDays"] = PositiveNumber(), ["estimatedHours"] = PositiveNumber(),
            ["requiredRoles"] = Strings(1), ["predecessors"] = Strings(),
            ["citationIds"] = Citations(), ["isAssumption"] = new JsonObject { ["type"] = "boolean" },
            ["phase"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(phase) },
            ["priority"] = Text(), ["detailedSteps"] = Strings(2)
        };
        foreach (var field in new[] { "inputs", "outputs", "acceptanceCriteria", "validationSteps",
            "customerResponsibilities", "usSignalResponsibilities", "prerequisites", "risks" })
            task[field] = Strings(1);
        foreach (var field in new[] { "openQuestions", "products", "platforms", "manufacturers", "models",
            "softwareVersions", "firmwareVersions", "licensingRequirements", "quantities", "tools", "systems",
            "interfaces", "integrationPoints", "accessRequirements", "rollbackSteps", "assumptions" })
            task[field] = Strings();
        var milestone = new JsonObject {
            ["name"] = Text(), ["description"] = Text(40), ["proposedTiming"] = Text(),
            ["acceptanceEvidence"] = Strings(1), ["citationIds"] = Citations(),
            ["isAssumption"] = new JsonObject { ["type"] = "boolean" }
        };
        var plan = new JsonObject {
            ["objective"] = Text(120), ["tasks"] = Array(Object(task), 2),
            ["milestones"] = Array(Object(milestone)),
            ["citationIds"] = Citations(),
            ["confidence"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
            ["confidenceExplanation"] = Text()
        };
        foreach (var field in new[] { "dependencies", "requiredRoles", "assumptions", "risks",
            "outOfScopeItems", "openQuestions", "conflicts" }) plan[field] = Strings();
        return Object(plan);
    }

    private static JsonObject Text(int minimum = 1) => new() { ["type"] = "string", ["pattern"] = $"^[\\s\\S]{{{minimum},}}$" };
    private static JsonObject PositiveNumber() => new() { ["type"] = "number", ["exclusiveMinimum"] = 0 };
    private static JsonObject Strings(int minimum = 0) => Array(Text(), minimum);
    private static JsonObject Array(JsonObject items, int minimum = 0) => new() {
        ["type"] = "array", ["items"] = items, ["minItems"] = minimum
    };
    private static JsonObject Citations() => new() {
        ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 1,
        ["items"] = new JsonObject { ["type"] = "integer", ["enum"] = new JsonArray(1) }
    };
    private static JsonObject Object(JsonObject properties) => new() {
        ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties,
        ["required"] = new JsonArray(properties.Select(pair => (JsonNode?)JsonValue.Create(pair.Key)).ToArray())
    };

    // Diagnose a rejected response without recording its values or property names.
    // Existing valid legacy shapes remain accepted by the normal parser. This
    // walk runs only after rejection and uses paths from our schema, never JSON.
    internal static (string Rule, string Field)? Diagnose(JsonElement value, string phase) =>
        Diagnose(value, Schema(phase), "$");

    private static (string Rule, string Field)? Diagnose(JsonElement value, JsonObject schema, string path)
    {
        var type = schema["type"]!.GetValue<string>();
        var expectedKind = type switch {
            "object" => JsonValueKind.Object, "array" => JsonValueKind.Array,
            "string" => JsonValueKind.String, "boolean" => value.ValueKind == JsonValueKind.False ? JsonValueKind.False : JsonValueKind.True,
            _ => JsonValueKind.Number
        };
        if (value.ValueKind != expectedKind) return ("expected_" + type, path);
        if (type == "object")
        {
            var properties = schema["properties"]!.AsObject();
            foreach (var property in properties)
            {
                var matches = value.EnumerateObject().Where(p => string.Equals(p.Name, property.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 0) return ("required_field", path + "." + property.Key);
                if (matches.Length > 1) return ("duplicate_field", path + "." + property.Key);
                if (Diagnose(matches[0].Value, property.Value!.AsObject(), path + "." + property.Key) is { } violation) return violation;
            }
            if (value.EnumerateObject().Any(p => !properties.Any(s => string.Equals(s.Key, p.Name, StringComparison.OrdinalIgnoreCase))))
                return ("unexpected_field", path + ".unknown_field");
        }
        if (type == "array")
        {
            if (value.GetArrayLength() < schema["minItems"]!.GetValue<int>()) return ("minimum_items", path);
            if (schema["maxItems"] is { } maximum && value.GetArrayLength() > maximum.GetValue<int>()) return ("maximum_items", path);
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (Diagnose(item, schema["items"]!.AsObject(), path + $"[{index}]") is { } violation) return violation;
                index++;
            }
        }
        if (type == "string" && schema["pattern"] is { } pattern && !Regex.IsMatch(value.GetString()?.Trim() ?? "", pattern.GetValue<string>(), RegexOptions.None, TimeSpan.FromMilliseconds(100)))
            return ("minimum_length", path);
        if (type is "number" or "integer")
        {
            if (!value.TryGetDecimal(out var number)) return ("number_range", path);
            if (type == "integer" && number != decimal.Truncate(number)) return ("expected_integer", path);
            if ((schema["exclusiveMinimum"] is { } exclusive && number <= exclusive.GetValue<int>())
                || (schema["minimum"] is { } minimum && number < minimum.GetValue<int>())
                || (schema["maximum"] is { } maximum && number > maximum.GetValue<int>())) return ("number_range", path);
        }
        if (schema["enum"] is JsonArray choices && !choices.Any(choice => JsonNode.DeepEquals(choice, JsonNode.Parse(value.GetRawText()))))
            return ("allowed_value", path);
        return null;
    }
}

// Message stays compatible with existing private-plan diagnostics. Only the
// closed rule and schema path are exposed by the external adapter.
internal sealed class Module025PhaseContractException(string rule, string field, string message) : JsonException(message)
{
    internal string Rule { get; } = rule;
    internal string Field { get; } = field;
}
