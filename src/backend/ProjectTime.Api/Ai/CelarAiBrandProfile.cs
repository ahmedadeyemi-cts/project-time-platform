using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Canonical, non-secret product identity for the Module 011 intelligence layer.
/// Technical Pulse AI identifiers remain compatible while the user-facing brand
/// transitions to Celar AI.
/// </summary>
public static partial class CelarAiBrandProfile
{
    public const string BrandName = "Celar AI";
    public const string PlatformName = "Pulse";
    public const string DivisionName = "US Signal Solution Provider division";
    public const string CreatorName = "Dr. Ahmed Adeyemi";
    public const string CreatorTitle = "Manager of Professional Services";
    public const string Tagline = "Speed of light. Speed of delivery.";
    public const string ContractVersion = "celar-ai-brand-v1-20260730";
    public const string AboutRoute = "/api/celar-ai/v1/about";
    public const string ChatRoute = "/api/celar-ai/v1/chat";
    public const string ProviderBridgeRoute = "/api/celar-ai/v1/provider-bridge/readiness";

    public static string CanonicalAnswer =>
        "Celar AI is the unified operational intelligence system for the US Signal Solution Provider division. " +
        "It was conceived and engineered under the direction of Dr. Ahmed Adeyemi, Manager of Professional Services, " +
        "to create a central intersection where consulting teams can convene, collaborate, and exchange project, delivery, " +
        "operational, and financial information. The name draws from Celeritas—the Latin concept of swiftness or speed—" +
        "and from the conventional symbol c for the speed of light in E=mc². That connection reflects US Signal's " +
        "fiber-network heritage and Celar AI's mission: translate the speed of light into the speed of delivery. " +
        "From a solution-provider perspective, Celar AI reduces the operational drag associated with legacy Changepoint " +
        "workflows, including siloed information, repetitive administration, slow SOW creation, fragmented task handoffs, " +
        "time-entry friction, and delayed financial visibility. It unifies authorized documents, live system data, workflows, " +
        "troubleshooting evidence, reports, and AI-assisted reasoning so teams can scope, execute, troubleshoot, report, and " +
        "invoice work more quickly without abandoning security, governance, source-system ownership, or human accountability.";

    public static bool IsIdentityQuestion(string? question)
    {
        var value = question?.Trim() ?? string.Empty;
        if (value.Length == 0) return false;
        if (!value.Contains("celar", StringComparison.OrdinalIgnoreCase)) return false;
        return IdentityQuestionRegex().IsMatch(value)
            || value.Equals("celar ai", StringComparison.OrdinalIgnoreCase)
            || value.Equals("celar", StringComparison.OrdinalIgnoreCase);
    }

    public static PulseAiSystemDetailedAnswer CreateDetailedAnswer(DateTimeOffset dataAsOf) => new(
        DirectConclusion: CanonicalAnswer,
        ExecutiveSummary:
            "Celar AI is the governed intelligence layer inside Pulse. It connects authorized documents, live module data, " +
            "deterministic calculations, operational evidence, and approved AI reasoning so US Signal teams can move from " +
            "scope to delivery and invoicing with less friction and stronger shared context.",
        ScopeAndFilters:
        [
            "Scope: the official Celar AI identity, name origin, business catalyst, operating mission, privacy boundary, and relationship to Pulse and Module 064.",
            "Audience: US Signal Solution Provider teams, including Sales, Professional Services, Project Management, Engineering, Finance, Operations, Security, and leadership.",
            "This is stable product knowledge. No customer, employee, project, financial, credential, or private-document record was retrieved to answer it."
        ],
        CurrentState:
        [
            "Pulse remains the business platform. Celar AI is the user-facing brand for the private operational-intelligence capability in Module 011.",
            "The current implementation preserves existing Pulse AI API paths, database objects, permission codes, environment variables, and internal class names as compatibility identifiers during the controlled transition.",
            "Module 064 remains the governed provider-credential, model-health, routing, circuit-breaker, and sanitized external-fallback boundary."
        ],
        DetailedAnalysis:
        [
            "Core identity: Celar AI is the unified operational intelligence system for the US Signal Solution Provider division and the common intersection where delivery teams exchange authorized information.",
            $"Creator and engineering direction: {CreatorName}, {CreatorTitle}, conceived and engineered the system to reduce operational friction and connect the full consulting lifecycle.",
            "Name origin: Celar AI draws from Celeritas, associated with swiftness or speed, and from c, the conventional symbol for the speed of light in E=mc².",
            "US Signal connection: the speed-of-light concept aligns with the company's fiber-network and digital-infrastructure foundation.",
            "Professional Services mission: Celar AI translates speed from a network characteristic into an operating promise—the speed of delivery across scoping, planning, execution, troubleshooting, reporting, billing readiness, invoicing, and closeout.",
            "Changepoint catalyst: Changepoint functioned as a legacy professional-services automation system and system of record, but siloed context, rigid navigation, repetitive administration, manual handoffs, time-entry burden, slow SOW preparation, and delayed financial visibility created operational drag.",
            "Operating model: Celar AI does not replace the source-of-truth responsibilities of business modules. It retrieves authorized evidence, invokes governed read-only tools, explains deterministic outputs, cites sources, and preserves human approval for consequential actions.",
            "Primary uses include document-grounded Timesheet suggestions, Help and system-wide Search, FlowHive planning, API discovery, troubleshooting, reports and financial insight, future-enhancement design, and controlled model lifecycle governance."
        ],
        ApiFindings:
        [
            "The user-facing Celar AI chat route is /api/celar-ai/v1/chat.",
            "The canonical identity route is /api/celar-ai/v1/about.",
            "The Module 064 relationship and private-model readiness route is /api/celar-ai/v1/provider-bridge/readiness.",
            "Existing /api/pulse-ai/* routes remain compatibility APIs until a separately approved technical-identifier migration is completed."
        ],
        TroubleshootingFindings:
        [
            "Celar AI can discover APIs from the running ASP.NET endpoint registry and correlate authorized evidence from Modules 013, 016, 076, 077, 078, 998, and other owning modules.",
            "It distinguishes a registered route from an authorized request, a successful dependency result, and a verified healthy workflow."
        ],
        RootCauseHypotheses: [],
        DiagnosticSteps:
        [
            "Open Module 011 through #celar-ai to use the system-intelligence and troubleshooting workspaces.",
            "Open Module 064 through #ai-provider-configuration to review Claude, OpenAI, governed local fallback, and private Celar AI model readiness.",
            "Use the API inventory and troubleshooting workspaces when a live runtime, route, permission, dependency, release, or correlation question must be answered."
        ],
        SourceEvidence:
        [
            "US Signal Celar AI Private Intelligence Architecture — Version 2.0.",
            "Celar AI Identity and Origin — canonical product narrative.",
            "Module 011 runtime contracts and Module 064 provider-governance boundary."
        ],
        KnownUnknownAndStaleValues:
        [
            "Known: the approved internal identity, creator attribution, name origin, business mission, Changepoint catalyst, and private-first operating boundary.",
            "Not evaluated by this identity answer: current provider health, model availability, live API status, project records, financial values, or document-processing state.",
            "Live operational questions are answered separately with authorized, time-stamped runtime evidence."
        ],
        Assumptions:
        [
            "Pulse remains the platform name while Celar AI becomes the Module 011 user-facing intelligence brand.",
            "Existing technical identifiers are retained during the first runtime rebrand to avoid unnecessary compatibility and migration risk."
        ],
        Conflicts: [],
        Limitations:
        [
            "This identity response does not prove that a private model or external provider is currently configured or healthy.",
            "Celar is not globally unique; public marketing, trademark, domain, pronunciation, and digital-identity use requires US Signal Legal and Marketing clearance."
        ],
        RisksAndImplications:
        [
            "A wholesale rename of API paths, database objects, permissions, source namespaces, and environment variables in one release would create avoidable operational risk. The transition therefore separates the user-facing brand from stable technical compatibility identifiers.",
            "The brand story should remain consistent across Module 011, Module 064, Help, Search, the User Guide, architecture documents, demos, and future training datasets."
        ],
        RecommendedActions:
        [
            "Use Celar AI as the visible product identity and keep Pulse as the business-platform identity.",
            "Retain compatibility aliases for existing routes and APIs until telemetry confirms that all callers have moved to Celar AI entry points.",
            "Complete Legal and Marketing name clearance before external customer-facing launch.",
            "Use the canonical answer in Help, Search, onboarding, demos, architecture reviews, and approved product documentation."
        ],
        FutureEnhancementBlueprint: null,
        NavigationTargets:
        [
            "#celar-ai",
            "#ai-provider-configuration",
            "#user-guide",
            "#system-architecture"
        ],
        CitationIds: [],
        Confidence: 1.0m,
        ConfidenceExplanation:
            "High confidence because the answer is generated from the approved Celar AI identity and architecture narrative rather than inferred from changing operational records.",
        DataAsOf: dataAsOf);

    public static object ToPublicProfile() => new
    {
        brandName = BrandName,
        platformName = PlatformName,
        module = "011",
        division = DivisionName,
        creator = new { name = CreatorName, title = CreatorTitle },
        nameOrigin = new
        {
            source = "Celeritas",
            meaning = "swiftness or speed",
            symbol = "c",
            physicsConnection = "the conventional symbol for the speed of light in E=mc²"
        },
        usSignalConnection = "Fiber-network and digital-infrastructure foundation",
        mission = "Translate the speed of light into the speed of delivery",
        catalyst = "Reduce the operational drag associated with legacy Changepoint workflows",
        tagline = Tagline,
        canonicalAnswer = CanonicalAnswer,
        contractVersion = ContractVersion,
        compatibility = new
        {
            technicalName = "Pulse AI",
            existingApiPrefix = "/api/pulse-ai",
            existingDatabasePrefix = "pulse_ai_",
            existingPermissionPrefix = "PULSE_AI",
            existingEnvironmentPrefix = "PROJECTPULSE_PULSE_AI",
            compatibilityRetained = true
        }
    };

    [GeneratedRegex(
        "(?:what\\s+is|what\\s+does|tell\\s+me\\s+about|who\\s+(?:created|built|engineered)|why\\s+(?:is\\s+it\\s+)?called|meaning|origin|story|purpose|celeritas|changepoint|speed\\s+of\\s+delivery)|(?:celar(?:\\s+ai)?).{0,90}(?:what|who|why|meaning|origin|story|purpose|created|built|engineered|celeritas|changepoint)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IdentityQuestionRegex();
}
