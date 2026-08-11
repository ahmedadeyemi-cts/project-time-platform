using System.Text;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Builds a private-first planning contract without calling any model. Detailed
/// document context stays inside the future private Celar AI runtime. The
/// optional Module 064 fallback receives only a generic abstract problem.
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
        var privatePrompt = BuildPrivatePrompt(plan, gsdExcerpt, sowExcerpt, outcome);

        return new
        {
            module = "066",
            pulseAiModule = "011",
            phase = "066D-private-first",
            status = "private_model_execution_not_registered",
            executionEnabled = false,
            requiredService = "ProjectPulseAiRouter",
            feature = "project_flowhive_plan",
            requiredProviderOrder = new[] { "private_model", "local_template" },
            legacyExternalRouteRejected = new[] { "claude", "openai", "local_template" },
            refusalFailover = "blocked",
            privateRequest = new
            {
                target = "private_projectpulse_model",
                systemPrompt = PrivateSystemPrompt(),
                promptSha256 = Sha256(privatePrompt),
                promptLength = privatePrompt.Length,
                gsdExcerptLength = gsdExcerpt.Length,
                sowExcerptLength = sowExcerpt.Length,
                maxOutputTokens = 2600,
                temperature = 0.1,
                rawPromptReturned = false,
                boundary = "private_projectpulse_runtime_only"
            },
            optionalExternalReasoning = new
            {
                executionEnabled = false,
                externalProviders = new[] { "claude", "openai" },
                routeAuthority = "Module 064",
                payloadPolicy = "sanitized_reasoning_capsule_only",
                capsule = BuildSanitizedCapsule(plan, validation, outcome),
                privateDocumentContentIncluded = false,
                projectIdentityIncluded = false,
                customerIdentityIncluded = false,
                commercialValuesIncluded = false,
                activationRequirement = "separate_policy_approval_and_private_verification"
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
            localFallback = BuildLocalFallback(plan, validation),
            guardrails = new[]
            {
                "The preview calls no model or provider.",
                "Detailed project-document context remains inside the private Pulse boundary.",
                "Module 064 may receive only an approved abstract capsule and cannot receive unrestricted document context.",
                "AI output is a draft and cannot establish a baseline, assign resources, reserve capacity, or commit customer dates.",
                "A safety refusal ends routing and cannot be bypassed through another provider."
            }
        };
    }

    private static string PrivateSystemPrompt() =>
        "You are the private Pulse FlowHive planning assistant. Produce a detailed cited draft only. " +
        "Extract scope, exclusions, deliverables, responsibilities, prerequisites, acceptance criteria, constraints, quantities, risks, dependencies, milestones, and open questions. " +
        "Preserve WBS hierarchy and delegate schedule calculations to the deterministic FlowHive engine. Do not approve, baseline, assign, publish, or commit a customer date.";

    private static string BuildPrivatePrompt(
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
        builder.AppendLine("Return a comprehensive draft with source citations and unresolved conflicts preserved.");
        return builder.ToString();
    }

    private static object BuildSanitizedCapsule(
        ProjectFlowHivePlanRequest? plan,
        ProjectFlowHivePlanValidationResult validation,
        string requestedOutcome)
    {
        return new
        {
            purpose = "generic_project_planning_reasoning_support",
            question = "Provide a generic professional-services planning and engineering-review checklist for a complex implementation. Do not infer customer commitments or project-specific facts.",
            requestedOutcomePresent = !string.IsNullOrWhiteSpace(requestedOutcome),
            suppliedStructure = new
            {
                taskCount = plan?.Tasks?.Count ?? 0,
                dependencyCount = plan?.Dependencies?.Count ?? 0,
                assignmentCount = plan?.Assignments?.Count ?? 0,
                validationIssueCount = validation.Issues?.Count ?? 0,
                deterministicScheduleRequired = true
            },
            requestedSections = new[]
            {
                "discovery and prerequisite checklist",
                "WBS quality checklist",
                "dependency and milestone review questions",
                "risk and assumption categories",
                "engineering validation checklist",
                "items that must remain unresolved without private evidence"
            },
            removed = new[]
            {
                "document excerpts",
                "project and customer identity",
                "record and infrastructure identifiers",
                "commercial values and terms",
                "sensitive authentication material"
            }
        };
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
            warning = "The deterministic fallback preserves supplied tasks and adds no inferred customer commitments."
        };
    }

    private static string Limit(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string Sha256(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
