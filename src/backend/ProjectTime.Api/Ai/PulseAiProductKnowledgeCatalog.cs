namespace ProjectTime.Api.Ai;

/// <summary>
/// Stable product knowledge that should be answered directly without requiring
/// live-record tools or an external model. The catalog is intentionally limited
/// to approved Pulse operating guidance and does not expose customer, project,
/// employee, financial, or document content.
/// </summary>
public static class PulseAiProductKnowledgeCatalog
{
    public const string ContractVersion = "celar-ai-product-knowledge-v3-20260806";
    public const int EntryCount = 2;

    public static PulseAiKnowledgeAnswer? Find(string normalizedQuestion)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuestion)) return null;

        if (CelarAiBrandProfile.IsIdentityQuestion(normalizedQuestion))
        {
            return CelarAiPurpose();
        }

        if (ContainsAny(
            normalizedQuestion,
            "what is pulse ai",
            "what does pulse ai do",
            "purpose of pulse ai",
            "pulse ai purpose",
            "purpose of module 011",
            "what is module 011",
            "module 011 purpose",
            "why do we need pulse ai"))
        {
            return PulseAiCompatibilityPurpose();
        }

        return null;
    }

    private static PulseAiKnowledgeAnswer CelarAiPurpose() => new(
        "Celar AI is the unified operational intelligence system for the US Signal Solution Provider division",
        CelarAiBrandProfile.CanonicalAnswer,
        [
            "Core identity: Celar AI is the private, permission-aware intelligence layer inside Pulse and the central intersection where Sales, Professional Services, Project Management, Engineering, Finance, Operations, Security, and leadership exchange authorized delivery information.",
            "Creator and engineering direction: Celar AI was conceived and engineered under the direction of Dr. Ahmed Adeyemi, Manager of Professional Services, to reduce operational friction and connect the full consulting lifecycle.",
            "Meaning behind the name: Celar AI draws from Celeritas, associated with swiftness or speed, and from c, the conventional symbol for the speed of light in E=mc².",
            "US Signal connection: the speed-of-light concept reflects US Signal’s fiber-network and digital-infrastructure foundation.",
            "Professional Services mission: Celar AI translates the speed of light into the speed of delivery by accelerating scoping, SOW development, handoff, project planning, execution, troubleshooting, time administration, reporting, billing readiness, invoicing, and closeout.",
            "Changepoint catalyst: Changepoint served as a functional legacy professional-services automation system and system of record, but siloed information, rigid navigation, repetitive administration, manual handoffs, slow SOW preparation, time-entry friction, and delayed financial visibility created operational drag.",
            "For Timesheets, Celar AI can use the Engineer’s factual work note together with authorized SOW, GSD, task, request, architecture, and supporting project documents to draft a scope-aligned description. The Engineer must review and apply the wording; Celar AI cannot change hours, save, submit, or approve time.",
            "For Help, Search, and troubleshooting, Celar AI explains modules, workflows, permissions, APIs, releases, defects, operational evidence, reports, and financial information using authorized read-only tools and citations.",
            "For FlowHive, Celar AI privately reads authorized delivery documents and project evidence to propose deliverables, tasks, dependencies, milestones, risks, assumptions, open questions, and a draft timeline. Project Management and Engineering retain review and approval authority.",
            "For reports and financial insight, deterministic Pulse services calculate authoritative values; Celar AI explains the results, drivers, exceptions, risks, and recommended next actions without inventing values or mutating records.",
            "Module 064 remains the provider credential, health, model, routing, circuit-breaker, and sanitized fallback authority. A private Celar AI model is the preferred route for restricted context; Claude and OpenAI are optional and receive only policy-approved sanitized generic reasoning capsules.",
            "The application—not the model—enforces authentication, role policy, module permissions, project and customer scope, record scope, and field restrictions before information is retrieved."
        ],
        [
            "Celar AI answers should begin with a direct conclusion and then provide scope, evidence, analysis, assumptions, conflicts, limitations, risks, recommended actions, freshness, and confidence when relevant.",
            "Raw SOW, GSD, customer, contract, architecture, employee, rate, and financial content remains inside the approved private Pulse boundary by default.",
            "Celar AI cannot automatically submit a Timesheet, approve a FlowHive baseline, change permissions, alter financial records, deploy software, or promote a model without the separately authorized workflow.",
            "Conversations do not automatically become training data. Training candidates must be sanitized, reviewed, versioned, evaluated, and approved.",
            "When sufficient authorized evidence is unavailable, Celar AI must identify what is missing instead of fabricating an answer.",
            "Celar is not globally unique. Public launch, trademark, domain, pronunciation, and digital-identity use require US Signal Legal and Marketing clearance."
        ],
        ["001", "011", "012", "013", "016", "019", "030", "037", "055C", "055D", "064", "066", "076", "078", "998", "999"],
        ["#celar-ai", "#timesheet", "#project-workspace", "#project-flowhive", "#reporting", "#roles-permissions-matrix", "#ai-provider-configuration", "#service-control", "#observability-slo-health", "#system-diagnostics", "#user-guide"]);

    private static PulseAiKnowledgeAnswer PulseAiCompatibilityPurpose() => new(
        "Pulse AI is the former user-facing name for Celar AI",
        "Module 011 is now branded Celar AI. Existing Pulse AI routes, API paths, permission codes, database objects, environment variables, and internal source identifiers remain available as compatibility contracts while the visible application uses the Celar AI identity.",
        [
            "Compatibility summary: Pulse AI is the private intelligence layer for Pulse; it provides document-grounded Timesheet suggestions, FlowHive project-plan drafting, reporting or financial insight, and Claude or OpenAI is optional under the approved sanitized fallback policy.",
            "Celar AI preserves every approved private-document, Timesheet, Help, Search, FlowHive, reporting, financial, API-discovery, troubleshooting, and model-governance capability developed under the Pulse AI technical name.",
            "Use #celar-ai for the current Module 011 workspace. Existing #work-task-builder and internal /api/pulse-ai paths remain compatible during the controlled transition.",
            "The full Celar AI identity, creator attribution, Celeritas origin, US Signal fiber connection, speed-of-delivery mission, and Changepoint catalyst are available through the Celar AI product answer."
        ],
        [
            "The platform remains Pulse; Celar AI is the Module 011 intelligence brand.",
            "Technical compatibility identifiers should not be removed until all callers, tests, audit evidence, deployments, and rollback paths have been reconciled."
        ],
        ["011", "064", "999"],
        ["#celar-ai", "#ai-provider-configuration", "#user-guide"]);

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
