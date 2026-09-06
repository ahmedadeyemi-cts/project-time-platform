using System.Text.Json;

namespace ProjectTime.Api.Ai;

public static class CelarAiEnterpriseEvidencePolicy
{
    public sealed record Context(string Text, bool Complete);

    public static string ValidateResponse(string body, string? toolCode = null)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return "tool_json_shape_invalid";
            if (root.ValueKind == JsonValueKind.Array) return string.Empty;
            if (Unhealthy(root)) return "tool_source_not_ready";
            if (root.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array
                && sources.EnumerateArray().Any(Unhealthy)) return "tool_source_not_ready";
            if (HasMore(root)) return "tool_pagination_incomplete";
            // These existing endpoints cap their SQL/result lists without a cursor.
            // Hitting a known cap is inconclusive, never a complete portfolio total.
            var caps = toolCode switch
            {
                "enterprise_project_portfolio" => new[] { ("projects",100), ("documents",250), ("assignments",250), ("resourceRequests",250) },
                "enterprise_financials" => new[] { ("projects",100) },
                "enterprise_billing" => new[] { ("candidates",250) },
                "enterprise_audit" => new[] { ("events",100) },
                "enterprise_risks" => new[] { ("risks",100) },
                _ => Array.Empty<(string,int)>()
            };
            foreach (var (name, cap) in caps)
                if (root.TryGetProperty(name,out var rows) && rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength()>=cap)
                    return "tool_source_limit_reached";
            return string.Empty;
        }
        catch (JsonException) { return "tool_json_invalid"; }
    }

    private static bool Unhealthy(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("ready",out var ready) && ready.ValueKind == JsonValueKind.False) return true;
        if (!root.TryGetProperty("status",out var status) || status.ValueKind != JsonValueKind.String) return false;
        var value = status.GetString() ?? "";
        return new[] { "failed", "forbidden", "unavailable", "not_ready", "partial", "blocked", "error", "degraded", "migration_required", "configuration_missing" }
            .Any(marker => value.Equals(marker,StringComparison.OrdinalIgnoreCase) || value.EndsWith("_"+marker,StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasMore(JsonElement root)
    {
        foreach (var name in new[] { "hasMore", "hasNextPage", "truncated", "isTruncated" })
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True) return true;
        foreach (var name in new[] { "nextCursor", "continuationToken", "nextPageToken", "nextLink" })
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(value.GetString())) return true;
        foreach (var name in new[] { "pagination", "pageInfo", "paging" })
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object && HasMore(value)) return true;
        return false;
    }

    // This is called only with server-produced tool results, never request-body
    // evidence. Structured references have their own namespace and are not
    // manufactured document citations or used to satisfy document coverage.
    public static Context BuildContext(IReadOnlyList<PulseAiSystemToolResult> tools, int maximumCharacters)
    {
        var entries = new List<object>();
        var complete = true;
        string Render() => JsonSerializer.Serialize(new
        {
            complete, sources = entries,
            limitation = "Use only the reported source scope and dates. API references are structured records, not document citations. Missing, inaccessible, failed or omitted data is unknown, never zero. Use owning-module totals; do not extrapolate totals from samples."
        });
        if (Render().Length > maximumCharacters) return new Context(string.Empty, false);
        foreach (var tool in tools)
        {
            if (!tool.Succeeded) { complete = false; continue; }
            var entry = JsonSerializer.Serialize(new
            {
                reference = $"API:{tool.ToolCode}", module = tool.ModuleCode,
                retrievedAt = tool.ObservedAt, scope = tool.EvidenceSummary,
                records = tool.ResponseJson
            });
            entries.Add(JsonSerializer.Deserialize<JsonElement>(entry));
            // Include envelope, JSON escaping and separators in the real budget.
            if (Render().Length + 1 > maximumCharacters)
            {
                entries.RemoveAt(entries.Count-1);
                complete = false;
            }
        }
        var text = Render();
        return text.Length <= maximumCharacters ? new Context(text, complete) : new Context(string.Empty,false);
    }
}
