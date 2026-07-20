using System.Text.RegularExpressions;
using ProjectTime.Api.Ai;

sealed class ProjectPulseAiTimeEntrySuggestionService
{
    private readonly ProjectPulseAiRouter _router;

    public ProjectPulseAiTimeEntrySuggestionService(ProjectPulseAiRouter router)
    {
        _router = router;
    }

    public async Task<ProjectPulseAiTimeEntrySuggestionResult> GenerateAsync(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var routed = await _router.GenerateAsync(
            new ProjectPulseAiGenerationRequest(
                ProjectPulseAiFeatures.TimesheetDescription,
                "You write concise, accurate, customer-facing professional services timesheet descriptions. You never change hours, submit time, create tasks, or alter allocations.",
                BuildPrompt(request),
                MaxOutputTokens: 220,
                Temperature: 0.2),
            () => BuildLocalSuggestion(request),
            cancellationToken);

        return new ProjectPulseAiTimeEntrySuggestionResult(
            CleanSuggestion(routed.Content),
            routed.Provider,
            routed.Warning);
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

        var roughNote = CleanSuggestion(request.CurrentDescription);

        if (!string.IsNullOrWhiteSpace(roughNote))
        {
            if (!string.IsNullOrWhiteSpace(project))
            {
                return CleanSuggestion($"Worked on {task} for {project}, including {roughNote}. Additional coordination, validation, and documentation were performed as needed.");
            }

            return CleanSuggestion($"Worked on {task}, including {roughNote}. Additional coordination, validation, and documentation were performed as needed.");
        }

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

Primary instruction:
Use the engineer's rough note as the most important source of truth. Expand it into a clear, specific, professional description, but do not invent facts, completion status, customer impact, or technical outcomes that the note does not support.

Rules:
- Return only the final description sentence or short paragraph.
- Do not mention AI.
- Do not include hours unless the engineer's note specifically references hours.
- Do not invent tools, systems, incidents, outages, meetings, approvals, deliverables, or outcomes.
- Do not say the work is complete unless the engineer's note says it is complete.
- Make the wording useful for customer review, invoice review, manager approval, and audit history.
- Prefer concrete action verbs such as reviewed, configured, validated, documented, coordinated, investigated, analyzed, updated, tested, supported, or troubleshot.
- Keep it between 25 and 70 words.
- If the engineer's rough note is vague, improve clarity using only the available project/task/activity context.

Engineer rough note:
{request.CurrentDescription ?? ""}

Additional context:
Work date: {request.WorkDate}
Time type: {request.TimeType ?? "normal"}
Row type: {request.RowType ?? "unknown"}
Project code: {request.ProjectCode ?? ""}
Project name: {request.ProjectName ?? ""}
Task code: {request.TaskCode ?? ""}
Task name: {request.TaskName ?? ""}
Activity/row label: {request.RowLabel ?? ""}
Category code: {request.CategoryCode ?? ""}
""";
    }
}
