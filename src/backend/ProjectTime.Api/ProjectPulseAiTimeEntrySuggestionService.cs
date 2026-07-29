using System.Text.RegularExpressions;
using ProjectTime.Api.Ai;

sealed class ProjectPulseAiTimeEntrySuggestionService
{
    private readonly ProjectPulseAiRouter _router;
    private readonly PulseAiDocumentGroundingService _grounding;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ProjectPulseAiTimeEntrySuggestionService> _logger;

    public ProjectPulseAiTimeEntrySuggestionService(
        ProjectPulseAiRouter router,
        PulseAiDocumentGroundingService grounding,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ProjectPulseAiTimeEntrySuggestionService> logger)
    {
        _router = router;
        _grounding = grounding;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<ProjectPulseAiTimeEntrySuggestionResult> GenerateAsync(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var grounding = await BuildGroundingAsync(request, cancellationToken);

        if (grounding?.Authorized == true && grounding.HasReadyPrivateContext)
        {
            return new ProjectPulseAiTimeEntrySuggestionResult(
                BuildPrivateGroundedSuggestion(request, grounding),
                ProjectPulseAiProviders.Local,
                BuildPrivateGroundingWarning(grounding));
        }

        var routed = await _router.GenerateAsync(
            new ProjectPulseAiGenerationRequest(
                ProjectPulseAiFeatures.TimesheetDescription,
                "You write concise, accurate, customer-facing professional services timesheet descriptions. You never change hours, submit time, create tasks, or alter allocations.",
                BuildRemotePromptWithoutPrivateDocuments(request),
                MaxOutputTokens: 220,
                Temperature: 0.2),
            () => BuildLocalSuggestion(request),
            cancellationToken);

        return new ProjectPulseAiTimeEntrySuggestionResult(
            CleanSuggestion(routed.Content),
            routed.Provider,
            MergeWarnings(routed.Warning, BuildGroundingReadinessWarning(grounding)));
    }

    private async Task<PulseAiGroundingContext?> BuildGroundingAsync(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return null;

        try
        {
            return await _grounding.BuildTimesheetContextAsync(
                effectiveUserId.Value,
                new PulseAiTimesheetGroundingInput(
                    request.WorkDate,
                    request.TimeType,
                    request.RowType,
                    request.RowLabel,
                    request.ProjectCode,
                    request.ProjectName,
                    request.TaskCode,
                    request.TaskName,
                    request.CurrentDescription),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI timesheet document grounding failed. The existing non-document suggestion path remains available without exposing source details.");
            return null;
        }
    }

    private static Guid? EffectiveUserId(HttpContext? context)
    {
        if (context is null) return null;

        if (context.Items.TryGetValue("ProjectPulseEffectiveUserId", out var effective)
            && effective is Guid effectiveUserId)
        {
            return effectiveUserId;
        }

        if (context.Items.TryGetValue("ProjectPulseSessionUserId", out var session)
            && session is Guid sessionUserId)
        {
            return sessionUserId;
        }

        return null;
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

    private static string BuildPrivateGroundedSuggestion(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        PulseAiGroundingContext grounding)
    {
        var task = FirstNonBlank(
            grounding.TaskName,
            grounding.RequestFunction,
            request.TaskName,
            request.RowLabel,
            request.TaskCode,
            request.CategoryCode,
            "assigned activity");
        var project = FirstNonBlank(
            grounding.ProjectName,
            grounding.ProjectCode,
            request.ProjectName,
            request.ProjectCode);
        var roughNote = CleanSuggestion(request.CurrentDescription);
        var sourceLabel = grounding.DocumentTypeLabel;
        var themes = grounding.ScopeThemes.Take(3).ToArray();
        var themePhrase = themes.Length > 0
            ? string.Join(", ", themes)
            : "documented project requirements, dependencies, and implementation constraints";

        if (!string.IsNullOrWhiteSpace(roughNote))
        {
            var projectPhrase = string.IsNullOrWhiteSpace(project) ? string.Empty : $" for {project}";
            return CleanSuggestion(
                $"Worked on {task}{projectPhrase}, including {roughNote}. Reviewed and aligned the reported activity with the approved {sourceLabel} context covering {themePhrase}, and documented relevant validation, coordination, and follow-up needed for the selected assignment.");
        }

        var workDatePhrase = request.WorkDate == default
            ? string.Empty
            : $" on {request.WorkDate:MMM d, yyyy}";
        var projectContext = string.IsNullOrWhiteSpace(project) ? string.Empty : $" for {project}";
        return CleanSuggestion(
            $"Supported {task}{projectContext}{workDatePhrase} in alignment with the approved {sourceLabel} context, including analysis of {themePhrase}, coordination of the assigned activity, validation of applicable project requirements, and updates to supporting work documentation.");
    }

    private static string BuildPrivateGroundingWarning(PulseAiGroundingContext grounding)
    {
        var ready = grounding.Documents.Where(document => document.SummaryReady).ToArray();
        var sources = ready
            .Take(4)
            .Select(document => $"{document.DocumentCategory.ToUpperInvariant()}: {document.OriginalFileName}")
            .ToArray();
        var sourceText = sources.Length > 0
            ? string.Join("; ", sources)
            : "approved project documentation";
        var conflicts = grounding.Conflicts.Count > 0
            ? $" Review required: {string.Join(" ", grounding.Conflicts)}"
            : string.Empty;
        var missing = grounding.MissingInputs.Count > 0
            ? $" Missing or incomplete evidence: {string.Join(" ", grounding.MissingInputs.Take(3))}"
            : string.Empty;

        return $"Private Pulse AI grounding used {ready.Length} approved document context source(s) at {grounding.GeneratedAt:O}. Sources: {sourceText}. Coverage: {grounding.CoverageLevel} ({grounding.CoverageScore:P0}). Raw document text and extracted summaries were not sent to Claude or OpenAI. The Engineer must confirm that the suggestion describes only work actually performed.{conflicts}{missing}";
    }

    private static string? BuildGroundingReadinessWarning(PulseAiGroundingContext? grounding)
    {
        if (grounding is null) return null;

        if (grounding.Status == "documents_found_context_not_ready")
        {
            return "Authorized project documents were found, but private extraction or approved AI context summaries are not ready. This suggestion used only the Engineer note and selected row context; no raw document content was sent to a remote provider.";
        }

        if (grounding.Status == "authorized_project_no_eligible_documents")
        {
            return "No authorized engineering-visible document was enabled for timesheet grounding. This suggestion used only the Engineer note and selected row context.";
        }

        if (grounding.Status is "project_not_resolved" or "project_outside_effective_user_scope")
        {
            return "Authorized document grounding was not available for the selected project context. No restricted project or document content was retrieved.";
        }

        if (grounding.Status is "document_grounding_unavailable" or "database_configuration_missing")
        {
            return "Private document grounding was temporarily unavailable. The existing non-document suggestion path was used without exposing database or document details.";
        }

        return null;
    }

    private static string? MergeWarnings(params string?[] warnings)
    {
        var values = warnings
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? null : string.Join(" ", values);
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

    private static string BuildRemotePromptWithoutPrivateDocuments(
        ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        return $"""
Write one professional, customer-facing time-entry description for a PSA timesheet.

Primary instruction:
Use the engineer's rough note as the most important source of truth. Expand it into a clear, specific, professional description, but do not invent facts, completion status, customer impact, or technical outcomes that the note does not support.

Privacy boundary:
- No SOW, GSD, architecture, contract, rate, financial, customer-document, or extracted private-document content is included in this request.
- Do not imply that you reviewed an internal document.
- Use only the row context and Engineer note below.

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

Additional non-document context:
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
