using System.Text.Json;

namespace ProjectTime.Api.Ai;

// Only closed protocol categories, schema field paths and numeric sizes. Never
// include response text, arbitrary provider error messages or matched identities.
public sealed record Module025ProviderDiagnostics(
    string? ResponseStatus = null,
    string? IncompleteReason = null,
    string? StopReason = null,
    int? OutputTextCharacters = null,
    string? OutputValidationCategory = null,
    string? OutputValidationField = null)
{
    internal static Module025ProviderDiagnostics OpenAi(JsonElement root) => new(
        ResponseStatus: Closed(Text(root, "status"), "completed", "incomplete", "failed", "in_progress", "queued", "cancelled"),
        IncompleteReason: Closed(root.TryGetProperty("incomplete_details", out var details)
            ? Text(details, "reason") : null, "max_output_tokens", "content_filter"),
        OutputTextCharacters: CountText(root, "output", "output_text"));

    internal static Module025ProviderDiagnostics Claude(JsonElement root) => new(
        StopReason: Closed(Text(root, "stop_reason"), "end_turn", "max_tokens", "stop_sequence", "tool_use", "pause_turn", "refusal", "model_context_window_exceeded"),
        OutputTextCharacters: CountText(root, "content", "text"));

    private static string Closed(string? value, params string[] allowed) =>
        value is null ? "missing" : allowed.Contains(value, StringComparer.Ordinal) ? value : "other";

    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static int CountText(JsonElement root, string arrayName, string textType)
    {
        if (!root.TryGetProperty(arrayName, out var values) || values.ValueKind != JsonValueKind.Array) return 0;
        long count = 0;
        foreach (var item in values.EnumerateArray())
        {
            if (Text(item, "type") == textType) count += Text(item, "text")?.Length ?? 0;
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array)
                foreach (var part in content.EnumerateArray())
                    if (Text(part, "type") == textType) count += Text(part, "text")?.Length ?? 0;
        }
        return (int)Math.Min(count, int.MaxValue);
    }
}
