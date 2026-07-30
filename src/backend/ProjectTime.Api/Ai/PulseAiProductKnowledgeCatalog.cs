namespace ProjectTime.Api.Ai;

/// <summary>
/// Stable product knowledge that should be answered directly without requiring
/// live-record tools or an external model. The catalog is intentionally limited
/// to approved Pulse operating guidance and does not expose customer, project,
/// employee, financial, or document content.
/// </summary>
public static class PulseAiProductKnowledgeCatalog
{
    public static PulseAiKnowledgeAnswer? Find(string normalizedQuestion)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuestion)) return null;

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
            return PulseAiPurpose();
        }

        return null;
    }

    private static PulseAiKnowledgeAnswer PulseAiPurpose() => new(
        "Pulse AI is the private intelligence layer for Pulse",
        "Module 011 — Pulse AI provides detailed, permission-aware intelligence across Pulse. It combines authorized internal documents, governed live system data, deterministic calculations, and approved AI models so users can receive comprehensive answers and reviewable drafts without bypassing Pulse permissions or exposing confidential information. Its primary uses are document-grounded Timesheet suggestions, system-wide Help and Search, FlowHive project-plan drafting, and reporting or financial insight.",
        [
            "For Timesheets, Pulse AI can use the Engineer’s factual work note together with authorized SOW, GSD, task, request, architecture, and supporting project documents to draft a scope-aligned description. The Engineer must review and apply the wording; Pulse AI cannot change hours, save, submit, or approve time.",
            "For Help and Search, Pulse AI explains modules, fields, buttons, statuses, permissions, workflows, and troubleshooting steps. As governed read tools are activated, it can also answer questions about authorized projects, documents, assignments, time, capacity, reports, financials, defects, releases, and operations.",
            "For FlowHive, Pulse AI privately reads authorized SOW, GSD, design, order, and project evidence to propose deliverables, a work breakdown structure, dependencies, milestones, risks, assumptions, open questions, and a draft timeline. The Project Manager and Engineering team modify and validate the plan before any baseline or customer commitment.",
            "For reports and financials, deterministic Pulse services calculate authoritative values such as planned cost, actual cost, forecast, variance, margin, utilization, billing readiness, invoice blockers, and capacity. Pulse AI explains the results, drivers, exceptions, risks, and recommended next actions; it does not invent values or mutate financial records.",
            "Pulse authentication, role policy, module permissions, project or customer scope, record scope, and field restrictions are enforced before information is retrieved. The model is never the authorization authority.",
            "Restricted SOW, GSD, customer, contract, architecture, employee, rate, and financial content remains inside the approved private Pulse boundary by default. A private Pulse AI model is the preferred reasoning path for that content.",
            "Claude or OpenAI is optional. When external generic reasoning is allowed, Pulse first creates a minimal sanitized capsule through the DLP boundary and Module 064. Raw internal documents, confidential identifiers, credentials, and unrestricted financial data are not included, and the result is verified privately before display.",
            "Module 011 governs knowledge, processing, evaluations, model lifecycle, confidence, and answer quality. Module 064 remains the provider credential, health, routing, circuit-breaker, and fallback authority. Modules 012 and 037 remain the role and permission authorities, while the owning business module remains the source of truth for each record and calculation."
        ],
        [
            "Pulse AI must provide sources, scope, filters, freshness, assumptions, conflicts, limitations, and confidence whenever they materially affect the answer.",
            "A short or surface-level response is not the default. Detailed answers should begin with the direct conclusion and then explain the supporting evidence and reasoning.",
            "Pulse AI cannot automatically submit a timesheet, approve a FlowHive baseline, change permissions, modify a project, alter financial records, deploy software, or promote a model without the separately authorized workflow.",
            "Conversations do not automatically become training data. Training candidates must be sanitized, reviewed, versioned, evaluated, and approved.",
            "When sufficient authorized evidence is unavailable, Pulse AI must say what is missing instead of fabricating an answer."
        ],
        ["001", "011", "012", "019", "030", "037", "055C", "055D", "064", "066", "999"],
        ["#work-task-builder", "#timesheet", "#project-workspace", "#project-flowhive", "#reporting", "#roles-permissions-matrix", "#ai-provider-configuration", "#user-guide"]);

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
