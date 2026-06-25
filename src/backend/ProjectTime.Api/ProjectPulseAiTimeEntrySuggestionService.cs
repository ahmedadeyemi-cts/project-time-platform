using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

static class ProjectPulseAiTimeEntrySuggestionService
{
    public static async Task<ProjectPulseAiTimeEntrySuggestionResult> GenerateAsync(ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        var fallback = BuildLocalSuggestion(request);
        var apiKey = Environment.GetEnvironmentVariable("PROJECTPULSE_CLAUDE_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ProjectPulseAiTimeEntrySuggestionResult(
                fallback,
                "local_template",
                "PROJECTPULSE_CLAUDE_API_KEY is not configured.");
        }

        var model = Environment.GetEnvironmentVariable("PROJECTPULSE_CLAUDE_MODEL");
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "claude-3-5-sonnet-20241022";
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var body = new
        {
            model,
            max_tokens = 220,
            temperature = 0.2,
            system = "You write concise, accurate, customer-facing professional services timesheet descriptions. You never change hours, submit time, create tasks, or alter allocations.",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = BuildPrompt(request)
                }
            }
        };

        try
        {
            using var response = await httpClient.PostAsync(
                "https://api.anthropic.com/v1/messages",
                new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json"));

            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new ProjectPulseAiTimeEntrySuggestionResult(
                    fallback,
                    "local_template",
                    $"Claude request failed with HTTP {(int)response.StatusCode}. Local suggestion returned.");
            }

            using var document = JsonDocument.Parse(responseText);

            if (document.RootElement.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeElement)
                        && string.Equals(typeElement.GetString(), "text", StringComparison.OrdinalIgnoreCase)
                        && item.TryGetProperty("text", out var textElement))
                    {
                        var suggestion = CleanSuggestion(textElement.GetString());

                        if (!string.IsNullOrWhiteSpace(suggestion))
                        {
                            return new ProjectPulseAiTimeEntrySuggestionResult(suggestion, "claude", null);
                        }
                    }
                }
            }

            return new ProjectPulseAiTimeEntrySuggestionResult(
                fallback,
                "local_template",
                "Claude response did not include usable text. Local suggestion returned.");
        }
        catch (Exception ex)
        {
            return new ProjectPulseAiTimeEntrySuggestionResult(
                fallback,
                "local_template",
                $"Claude request failed: {ex.Message}. Local suggestion returned.");
        }
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string CleanSuggestion(string? value)
    {
        var cleaned = (value ?? "").Trim();

        if (cleaned.StartsWith("\"") && cleaned.EndsWith("\"") && cleaned.Length > 1)
        {
            cleaned = cleaned[1..^1].Trim();
        }

        cleaned = cleaned.Replace("\r", " ").Replace("\n", " ");
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();

        if (cleaned.Length > 500)
        {
            cleaned = cleaned[..500].TrimEnd();
        }

        return cleaned;
    }

    private static string BuildLocalSuggestion(ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        var task = FirstNonBlank(request.TaskName, request.RowLabel, request.TaskCode, request.CategoryCode, "assigned activity");
        var project = FirstNonBlank(request.ProjectName, request.ProjectCode);
        var timeType = string.Equals(request.TimeType, "afterhours", StringComparison.OrdinalIgnoreCase)
            ? "after-hours"
            : "standard business hours";

        if (string.Equals(request.RowType, "nonProject", StringComparison.OrdinalIgnoreCase))
        {
            return CleanSuggestion($"Completed {task} during {timeType}, including coordination, follow-up, documentation, and required operational support activities.");
        }

        if (!string.IsNullOrWhiteSpace(project))
        {
            return CleanSuggestion($"Completed work on {task} for {project} during {timeType}, including analysis, coordination, validation, and documentation updates needed to move the assigned work forward.");
        }

        return CleanSuggestion($"Completed work on {task} during {timeType}, including analysis, coordination, validation, and documentation updates needed to move the assigned work forward.");
    }

    private static string BuildPrompt(ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        return $"""
Write one professional, customer-facing time-entry description for a PSA timesheet.

Rules:
- Return only the description sentence.
- Do not mention AI.
- Do not include hours unless the task context requires it.
- Do not invent project outcomes that are not supported by the context.
- Do not say the work is complete unless the context says it is complete.
- Keep it between 18 and 45 words.
- Make it suitable for a customer invoice or internal project audit.

Context:
Work date: {request.WorkDate}
Time type: {request.TimeType ?? "normal"}
Row type: {request.RowType ?? "unknown"}
Project code: {request.ProjectCode ?? ""}
Project name: {request.ProjectName ?? ""}
Task code: {request.TaskCode ?? ""}
Task name: {request.TaskName ?? ""}
Activity/row label: {request.RowLabel ?? ""}
Category code: {request.CategoryCode ?? ""}
Entered hours: {request.Hours?.ToString() ?? ""}
Current description: {request.CurrentDescription ?? ""}
""";
    }
}
