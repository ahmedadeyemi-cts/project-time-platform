using System.Text;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Builds a sanitized Module 064 request contract without calling a provider.
/// Execution remains unavailable until ProjectPulseAiRouter is merged and
/// registered. This file intentionally contains no HttpClient or provider SDK.
/// </summary>
internal static class ProjectFlowHiveAiRequestFactory
{
    public static object Preview(ProjectFlowHiveAiDraftPreviewRequest request)
    {
        var plan = request.Plan;
        var validation = ProjectFlowHiveScheduleEngine.Validate(plan);
        var gsdExcerpt = Limit(request.GsdExcerpt, 12000);
        var sowExcerpt = Limit(request.SowExcerpt, 12000);
        var outcome = Limit(request.RequestedOutcome, 1000);
        var localFallback = BuildLocalFallback(plan, validation);

        return new
        {
            module = "066",
            phase = "066D",
            status = "module_064_execution_not_registered",
            executionEnabled = false,
            requiredService = "ProjectPulseAiRouter",
            feature = "project_flowhive_plan",
            requiredProviderOrder = new[] { "claude", "openai", "local_template" },
            refusalFailover = "blocked",
            request = new
            {
                feature = "project_flowhive_plan",
                systemPrompt = SystemPrompt(),
                userPrompt = UserPrompt(plan, gsdExcerpt, sowExcerpt, outcome),
                maxOutputTokens = 2600,
                temperature = 0.1
            },
            sourceAuthority = new
            {
                gsdVersion = plan?.GsdVersion,
                sowVersion = plan?.SowVersion,
                gsdExcerptPresent = !string.IsNullOrWhiteSpace(gsdExcerpt),
                sowExcerptPresent = !string.IsNullOrWhiteSpace(sowExcerpt),
                citationsRequired = true,
                conflictsMustBeSurfaced = true
            },
            validation,
            localFallback,
            guardrails = new[]
            {
                "The preview does not call Claude, OpenAI, or any local model.",
                "Only Module 064 may select or call an AI provider.",
                "AI output is a draft and cannot establish a baseline.",
                "A provider safety refusal must not fail over to another provider.",
                "No API key, provider secret, or customer-sharing link is returned."
            }
        };
    }

    private static string SystemPrompt() =>
        "You are ProjectPulse Project FlowHive planning assistance. Produce a draft only. " +
        "Use the cited approved GSD and SOW excerpts as authority, identify conflicts and missing inputs, " +
        "never invent commitments, preserve WBS hierarchy, and return reviewable tasks, dependencies, " +
        "assumptions, risks, and source citations. Do not approve or baseline the plan.";

    private static string UserPrompt(
        ProjectFlowHivePlanRequest? plan,
        string gsdExcerpt,
        string sowExcerpt,
        string requestedOutcome)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Project: {plan?.ProjectCode} — {plan?.ProjectName}");
        builder.AppendLine($"Customer: {plan?.CustomerName}");
        builder.AppendLine($"Plan: {plan?.PlanName}; revision: {plan?.RevisionLabel}");
        builder.AppendLine($"Requested outcome: {requestedOutcome}");
        builder.AppendLine($"GSD version: {plan?.GsdVersion}");
        builder.AppendLine(gsdExcerpt);
        builder.AppendLine($"SOW version: {plan?.SowVersion}");
        builder.AppendLine(sowExcerpt);
        builder.AppendLine("Return a draft with explicit source citations and unresolved conflicts.");
        return builder.ToString();
    }

    private static object BuildLocalFallback(
        ProjectFlowHivePlanRequest? plan,
        ProjectFlowHivePlanValidationResult validation)
    {
        return new
        {
            provider = "local_template",
            outcome = "success",
            status = "governed_local_draft",
            planName = plan?.PlanName,
            tasks = plan?.Tasks?.Select(task => new
            {
                task.WbsNumber,
                task.Name,
                task.DurationWorkingDays,
                source = "user_supplied_plan"
            }).ToArray() ?? [],
            validation.Valid,
            warning = "The deterministic local fallback preserves supplied tasks and adds no inferred customer commitments."
        };
    }

    private static string Limit(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }
}
