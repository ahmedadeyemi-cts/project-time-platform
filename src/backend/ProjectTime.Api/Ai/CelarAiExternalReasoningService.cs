namespace ProjectTime.Api.Ai;

/// <summary>
/// Closed vocabulary for public-provider assistance. The category is an opaque
/// selector only; no caller-authored text, identity inventory, or substring is
/// copied into the outbound prompt. Every capsule is constructed from a fixed
/// backend string below.
/// </summary>
public static class CelarAiExternalReasoningPurposeCatalog
{
    public const string SowScopeQuality = CelarAiExternalCapsuleCatalog.SowScopeQuality;
    public const string ProjectPlanQuality = CelarAiExternalCapsuleCatalog.ProjectPlanQuality;
    public const string ProjectTimelineSequencing = CelarAiExternalCapsuleCatalog.ProjectTimelineQuality;
    public const string ProjectDiagramGovernance = CelarAiExternalCapsuleCatalog.ProjectDiagramQuality;

    public static string ForMode(string mode) => mode switch
    {
        "sow_draft" => SowScopeQuality,
        "project_timeline" => ProjectTimelineSequencing,
        "project_diagram" => ProjectDiagramGovernance,
        "project_plan" => ProjectPlanQuality,
        _ => string.Empty
    };

    public static bool TryBuildServerOwnedCapsule(
        string? category,
        string mode,
        out string capsule)
    {
        var expectedCategory = ForMode(mode);
        if (expectedCategory.Length == 0
            || !string.Equals(category?.Trim(), expectedCategory, StringComparison.Ordinal))
        {
            capsule = string.Empty;
            return false;
        }

        if (!CelarAiExternalCapsuleCatalog.TryResolve(expectedCategory, out var definition))
        {
            capsule = string.Empty;
            return false;
        }
        capsule = definition.Capsule;
        return true;
    }
}

/// <summary>
/// Provides an optional generic-reasoning fallback through Module 064. The
/// service accepts only a closed backend-owned purpose category. It never
/// receives private document chunks, customer/project identities, people
/// records, financial values, or unrestricted tool output.
/// </summary>
public sealed class CelarAiExternalReasoningService
{
    public const string ContractVersion = "celar-ai-sanitized-external-reasoning-v1-20260801";

    private readonly PulseAiEscalationSanitizer _sanitizer;
    private readonly CelarAiCapabilityRouter _router;
    private readonly ILogger<CelarAiExternalReasoningService> _logger;

    public CelarAiExternalReasoningService(
        PulseAiEscalationSanitizer sanitizer,
        CelarAiCapabilityRouter router,
        ILogger<CelarAiExternalReasoningService> logger)
    {
        _sanitizer = sanitizer;
        _router = router;
        _logger = logger;
    }

    public static object Readiness() => new
    {
        status = Enabled()
            ? "celar_ai_sanitized_external_fallback_enabled"
            : "celar_ai_sanitized_external_fallback_disabled",
        contractVersion = ContractVersion,
        enabled = Enabled(),
        sanitizedExternalExecutionEnabled = RuntimeFlag("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION"),
        enterpriseSanitizedFallbackEnabled = RuntimeFlag("PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED"),
        module064Boundary = true,
        allowedModes = CelarAiEnterprisePlatformPolicy.ExternalFallbackEligibleModes,
        inputPolicy = new
        {
            closedServerOwnedPurposeCategoryOnly = true,
            callerFreeTextAccepted = false,
            privateDocumentTextAllowed = false,
            customerOrProjectIdentityAllowed = false,
            peopleRecordsAllowed = false,
            financialValuesAllowed = false,
            credentialsAllowed = false,
            arbitraryToolResponsesAllowed = false
        },
        refusalPolicy = "A provider safety refusal ends the route and is not bypassed with another provider.",
        outputPolicy = "External output is untrusted generic assistance until Celar AI applies and verifies it inside the private Pulse boundary.",
        generatedAt = DateTimeOffset.UtcNow
    };

    public async Task<CelarAiExternalReasoningResult> TryGenerateAsync(
        CelarAiExternalReasoningRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var mode = NormalizeMode(request.Mode);
        var blockers = new List<string>();

        if (!Enabled())
            blockers.Add("Sanitized external reasoning is disabled by Celar AI runtime policy.");
        if (!CelarAiEnterprisePlatformPolicy.ExternalFallbackEligibleModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
            blockers.Add("The requested solution mode is not eligible for an external reasoning fallback.");
        if (request.ContainsPrivateDocumentText)
            blockers.Add("Private document text is never eligible for this external route.");
        if (request.ContainsFinancialValues)
            blockers.Add("Financial or commercial values are never eligible for this external route.");
        if (request.ContainsPeopleRecords)
            blockers.Add("People, assignment, workload, or employee records are never eligible for this external route.");
        var serverOwnedCapsuleReady = CelarAiExternalReasoningPurposeCatalog.TryBuildServerOwnedCapsule(
            request.PurposeCategory,
            mode,
            out var serverOwnedCapsule);
        var serverOwnedPurposeCategory = CelarAiExternalReasoningPurposeCatalog.ForMode(mode);
        if (!serverOwnedCapsuleReady)
            blockers.Add("The external-reasoning purpose category is unknown, empty, or does not match the requested solution mode.");

        if (blockers.Count > 0)
        {
            return Blocked(mode, blockers, now);
        }

        var sanitized = _sanitizer.SanitizeForExecution(new PulseAiSanitizationRequest(
            Purpose: $"server_owned_{serverOwnedPurposeCategory}",
            Content: serverOwnedCapsule,
            Classification: "internal_generic",
            SensitiveTerms: [],
            AcknowledgePreviewOnly: true));

        if (!sanitized.ExternalExecutionAuthorized)
        {
            return new CelarAiExternalReasoningResult(
                Status: "sanitized_external_reasoning_blocked",
                Enabled: Enabled(),
                Authorized: false,
                ProviderCalled: false,
                Provider: string.Empty,
                Content: string.Empty,
                Warning: "Celar AI blocked the external reasoning capsule before Module 064 routing.",
                Redactions: sanitized.Redactions,
                RemovedCategories: sanitized.RemovedCategories,
                BlockedReasons: sanitized.BlockedReasons,
                GeneratedAt: now);
        }

        var feature = string.Equals(
                request.CapabilityCode?.Trim(),
                ProjectPulseAiFeatures.ProjectForgePlanEstimate,
                StringComparison.OrdinalIgnoreCase)
            && mode is "project_plan" or "project_timeline" or "project_diagram"
            ? ProjectPulseAiFeatures.ProjectForgePlanEstimate
            : mode switch
        {
            "sow_draft" => ProjectPulseAiFeatures.SowGsdPlanning,
            "project_plan" or "project_timeline" or "project_diagram" => ProjectPulseAiFeatures.ProjectFlowHivePlan,
            _ => ProjectPulseAiFeatures.HelpAssistant
        };

        try
        {
            var route = await _router.GenerateExternalAsync(
                new ProjectPulseAiGenerationRequest(
                    Feature: feature,
                    SystemPrompt: SystemPrompt(mode),
                    UserPrompt: sanitized.SanitizedCapsule,
                    MaxOutputTokens: 1_800,
                    Temperature: 0.15),
                new CelarAiCapabilityExecutionContext(
                    Feature: feature,
                    ContainsPrivateDocuments: false,
                    ContainsCustomerIdentity: false,
                    ContainsPeopleRecords: false,
                    ContainsFinancialValues: false,
                    AllowSanitizedExternalAssistance: false,
                    SensitiveTerms: [],
                    ConsumerModule: "011",
                    CorrelationId: Guid.NewGuid().ToString("N"),
                    IdentityTerms: [],
                    ExternalCapsulePurpose: serverOwnedPurposeCategory),
                localFallback: () => LocalFallback(mode),
                cancellationToken);

            var providerCalled = route.AttemptedProviders.Count > 0;
            var status = route.Outcome == ProjectPulseAiOutcomes.Refusal
                ? "external_reasoning_refused"
                : route.Provider == ProjectPulseAiProviders.Local
                    ? "governed_generic_fallback_completed"
                    : "sanitized_external_reasoning_completed";
            var warning = route.Outcome == ProjectPulseAiOutcomes.Refusal
                ? route.Warning ?? "The provider declined the request and no bypass was attempted."
                : route.Warning ?? "Generic assistance completed. Celar AI must privately verify and apply it before use.";

            return new CelarAiExternalReasoningResult(
                Status: status,
                Enabled: true,
                Authorized: true,
                ProviderCalled: providerCalled,
                Provider: route.Provider,
                Content: route.Content,
                Warning: warning,
                Redactions: sanitized.Redactions,
                RemovedCategories: sanitized.RemovedCategories,
                BlockedReasons: [],
                GeneratedAt: DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Celar AI sanitized external reasoning failed without logging capsule or provider content. Mode={Mode} Diagnostic={Diagnostic}",
                mode,
                exception.GetType().Name.ToLowerInvariant());
            return new CelarAiExternalReasoningResult(
                Status: "sanitized_external_reasoning_failed",
                Enabled: true,
                Authorized: true,
                ProviderCalled: false,
                Provider: string.Empty,
                Content: string.Empty,
                Warning: "The sanitized external reasoning route did not complete. Celar AI retained the private evidence-limited draft.",
                Redactions: sanitized.Redactions,
                RemovedCategories: sanitized.RemovedCategories,
                BlockedReasons: [exception.GetType().Name.ToLowerInvariant()],
                GeneratedAt: DateTimeOffset.UtcNow);
        }
    }

    private static CelarAiExternalReasoningResult Blocked(
        string mode,
        IReadOnlyList<string> reasons,
        DateTimeOffset now) =>
        new(
            Status: "sanitized_external_reasoning_blocked",
            Enabled: Enabled(),
            Authorized: false,
            ProviderCalled: false,
            Provider: string.Empty,
            Content: string.Empty,
            Warning: $"No external provider was called for {mode}.",
            Redactions: [],
            RemovedCategories: [],
            BlockedReasons: reasons,
            GeneratedAt: now);

    private static string SystemPrompt(string mode) => $"""
        You are an optional generic professional-services reasoning assistant used by Celar AI through Module 064.
        The request has been sanitized and must remain generic. Do not request or invent customer names, project IDs,
        employee identities, internal documents, prices, rates, credentials, IP addresses, hostnames, or proprietary facts.
        Provide concise, reusable guidance for the solution mode '{mode}'. Separate assumptions from recommendations.
        Do not claim that any work was completed, approved, scheduled, contracted, or committed. Return plain text only.
        """;

    private static string LocalFallback(string mode) => mode switch
    {
        "sow_draft" => "Use a generic SOW structure covering objectives, scope, exclusions, deliverables, responsibilities, assumptions, dependencies, acceptance criteria, milestones, risks, change control, and approval.",
        "project_timeline" => "Use discovery, design validation, prerequisites, implementation, testing, acceptance, handoff, and closeout as generic sequencing checkpoints; validate durations and dependencies privately.",
        "project_diagram" => "Use a left-to-right project flow showing inputs, governance, discovery, design, implementation, validation, acceptance, and handoff; label assumptions and review gates.",
        _ => "Use a phased professional-services plan with discovery, design, implementation, testing, acceptance, handoff, risks, dependencies, open questions, and human review gates."
    };

    private static string NormalizeMode(string? value)
    {
        var mode = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return CelarAiEnterprisePlatformPolicy.SupportedModes.Contains(mode, StringComparer.OrdinalIgnoreCase)
            ? mode
            : "project_plan";
    }

    private static string Clean(string? value, int maximum)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static bool Enabled() =>
        RuntimeFlag("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION")
        && RuntimeFlag("PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED");

    private static bool RuntimeFlag(string name) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var enabled) && enabled;
}
