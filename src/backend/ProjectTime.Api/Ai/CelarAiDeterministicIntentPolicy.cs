namespace ProjectTime.Api.Ai;

public sealed record CelarAiExecutionBudget(
    int MaximumToolCalls,
    int MaximumParallelTools,
    int PerToolTimeoutSeconds,
    int TotalTimeoutSeconds,
    int MaximumRows,
    int MaximumToolResponseBytes,
    int MaximumRetries,
    bool CancellationRequired,
    bool CircuitBreakerRequired);

public sealed record CelarAiToolSelectionDecision(
    string ToolCode,
    string Decision,
    string Strength,
    int Score,
    IReadOnlyList<string> Reasons);

public sealed record CelarAiDeterministicToolSelection(
    string PolicyVersion,
    IReadOnlyList<CelarAiUniversalToolCapability> SelectedTools,
    IReadOnlyList<CelarAiToolSelectionDecision> Decisions,
    CelarAiExecutionBudget ExecutionBudget,
    bool ClarificationRequired);

/// <summary>
/// Server-owned, deterministic intent and tool-selection policy. It applies
/// explicit intent precedence, positive and negative signals, bounded fan-out,
/// mutually exclusive evidence families, and a reason for every selection or
/// rejection. A model may never add a tool or widen a scope after this policy.
/// </summary>
public static class CelarAiDeterministicIntentPolicy
{
    public const string ContractVersion = "celar-ai-deterministic-intent-tools-v1-20260810";
    public const int MaximumSelectedTools = 8;

    public static CelarAiExecutionBudget DefaultBudget { get; } = new(
        MaximumToolCalls: MaximumSelectedTools,
        MaximumParallelTools: 4,
        PerToolTimeoutSeconds: 12,
        TotalTimeoutSeconds: 45,
        MaximumRows: 500,
        MaximumToolResponseBytes: 48_000,
        MaximumRetries: 1,
        CancellationRequired: true,
        CircuitBreakerRequired: true);

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedByIntent =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["identity_and_permissions"] = Set(
                "effective_identity", "role_permission_evidence", "people_directory",
                "team_scope", "reporting_relationships", "conversation_attachments",
                "product_knowledge"),
            ["people_and_work"] = Set(
                "people_directory", "team_scope", "reporting_relationships",
                "project_portfolio", "project_assignments", "task_assignments",
                "capacity_utilization", "product_knowledge"),
            ["people_activity"] = Set(
                "people_directory", "team_scope", "project_assignments",
                "task_assignments", "timesheet_status", "audit_history",
                "capacity_utilization", "product_knowledge"),
            ["projects_and_delivery"] = Set(
                "project_portfolio", "project_assignments", "task_assignments",
                "resource_requests", "people_directory", "team_scope",
                "capacity_utilization", "project_documents", "private_retrieval",
                "flowhive_plan", "project_forge", "risk_register", "product_knowledge"),
            ["internal_data"] = Set(
                "project_portfolio", "project_assignments", "task_assignments",
                "people_directory", "team_scope", "timesheet_status",
                "approval_status", "capacity_utilization", "project_financial_truth",
                "expense_billing", "commercial_contracts", "commercial_pipeline",
                "project_documents", "private_retrieval", "risk_register",
                "audit_history", "product_knowledge"),
            ["timesheets_and_approvals"] = Set(
                "timesheet_status", "approval_status", "capacity_utilization",
                "task_assignments", "project_assignments", "people_directory",
                "team_scope", "product_knowledge"),
            ["financial_and_reporting"] = Set(
                "project_financial_truth", "expense_billing", "commercial_contracts",
                "commercial_pipeline", "project_portfolio", "project_documents",
                "private_retrieval", "product_knowledge"),
            ["documents_and_rag"] = Set(
                "project_documents", "private_retrieval", "conversation_attachments",
                "document_extraction", "ocr", "malware_scan", "product_knowledge"),
            ["troubleshooting"] = DiagnosticSet(),
            ["api_inventory"] = DiagnosticSet(),
            ["release_and_deployment"] = DiagnosticSet(),
            ["observability"] = DiagnosticSet(),
            ["security"] = DiagnosticSet(),
            ["product_help"] = Set("product_knowledge"),
            ["procedure"] = Set("product_knowledge"),
            ["platform_identity"] = Set("product_knowledge"),
            ["general_knowledge"] = Set("governed_public_information"),
            ["future_enhancement"] = Set(
                "product_knowledge", "live_api_inventory", "system_diagnostics",
                "observability", "security_posture"),
            ["architecture"] = Set(
                "product_knowledge", "live_api_inventory", "system_diagnostics",
                "observability", "security_posture"),
            ["general_system"] = Set(
                "product_knowledge", "system_diagnostics", "live_api_inventory",
                "provider_configuration", "oracle_runtime_readiness")
        };

    public static CelarAiDeterministicToolSelection Select(
        string normalizedQuestion,
        string? intentCode,
        CelarAiAnswerQuestionClass questionClass,
        int attachmentCount,
        IEnumerable<CelarAiUniversalToolCapability> seedTools)
    {
        var question = Normalize(normalizedQuestion);
        var intent = Normalize(intentCode).Replace(' ', '_');
        var seedCodes = seedTools
            .Select(tool => tool.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scores = CelarAiUniversalToolCatalog.Tools.ToDictionary(
            tool => tool.Code,
            _ => 0,
            StringComparer.OrdinalIgnoreCase);
        var reasons = CelarAiUniversalToolCatalog.Tools.ToDictionary(
            tool => tool.Code,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddScore(string code, int score, string reason)
        {
            if (!scores.ContainsKey(code)) return;
            scores[code] += score;
            if (!reasons[code].Contains(reason, StringComparer.OrdinalIgnoreCase))
                reasons[code].Add(reason);
        }

        void Require(string code, string reason)
        {
            if (!scores.ContainsKey(code)) return;
            required.Add(code);
            AddScore(code, 100, reason);
        }

        foreach (var tool in CelarAiUniversalToolCatalog.Tools)
        {
            foreach (var signal in tool.QuerySignals)
            {
                if (ContainsPhrase(question, signal))
                    AddScore(tool.Code, 12, $"Matched explicit signal '{signal}'.");
            }
            if (seedCodes.Contains(tool.Code))
                AddScore(tool.Code, 2, "Selected by the governed capability catalog.");
        }

        var ambiguous = IsAmbiguous(question);
        if (ambiguous)
        {
            Require("product_knowledge", "Question is referential or incomplete; request scope before retrieving data.");
        }
        else
        {
            AddIntentRequirements(intent, question, Require);
            AddClassRequirements(questionClass, attachmentCount, Require);
        }

        if (attachmentCount > 0)
            Require("conversation_attachments", "The request contains conversation attachments owned by the actual user.");

        var allowed = ambiguous
            ? Set("product_knowledge")
            : Allowed(intent, questionClass);

        foreach (var code in required)
            allowed.Add(code);

        foreach (var code in allowed)
            AddScore(code, 3, intent.Length > 0
                ? $"Permitted by explicit intent '{intent}'."
                : $"Permitted by question class '{questionClass}'.");

        // Negative boundaries prevent a generic word from fanning out into an
        // unrelated business domain. Public and product questions are exclusive.
        if (intent == "general_knowledge")
        {
            foreach (var tool in CelarAiUniversalToolCatalog.Tools.Where(tool => tool.PrivateOnly))
                AddScore(tool.Code, -500, "Private/internal tools are prohibited for a public-only question.");
        }
        if (intent is "product_help" or "procedure" or "platform_identity")
        {
            foreach (var tool in CelarAiUniversalToolCatalog.Tools.Where(tool => tool.Code != "product_knowledge"))
                AddScore(tool.Code, -500, "Product-procedure intent is source-controlled and excludes live business-data fan-out.");
        }
        if (ContainsAny(question, "opportunity", "opportunities", "pipeline", "delivery work"))
        {
            Require("commercial_pipeline", "The question explicitly requests opportunity or future-delivery pipeline evidence.");
            AddScore("product_knowledge", -50, "Product guidance cannot establish opportunity-pipeline facts.");
        }
        if (ContainsAny(question, "remaining hours", "hours remain")
            && ContainsAny(question, "assignment", "assignments", "assigned"))
        {
            Require("task_assignments", "Remaining assignment hours require current task-assignment evidence.");
            Require("capacity_utilization", "Remaining assignment hours require the governed capacity formula.");
            AddScore("timesheet_status", -10, "Submitted time alone cannot establish remaining assigned work.");
        }

        var selectedCodes = required
            .OrderByDescending(code => scores[code])
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .Concat(CelarAiUniversalToolCatalog.Tools
                .Where(tool => allowed.Contains(tool.Code)
                    && !required.Contains(tool.Code)
                    && scores[tool.Code] > 0)
                .OrderByDescending(tool => scores[tool.Code])
                .ThenBy(tool => tool.Code, StringComparer.OrdinalIgnoreCase)
                .Select(tool => tool.Code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSelectedTools)
            .ToList();

        if (selectedCodes.Count == 0)
        {
            selectedCodes.Add("product_knowledge");
            AddScore("product_knowledge", 100, "No safe authoritative scope was resolved; use governed help and ask for clarification.");
            required.Add("product_knowledge");
        }

        var selectedSet = selectedCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedTools = selectedCodes
            .Select(code => CelarAiUniversalToolCatalog.Tools.First(tool =>
                tool.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var decisions = CelarAiUniversalToolCatalog.Tools
            .Select(tool =>
            {
                var selected = selectedSet.Contains(tool.Code);
                var toolReasons = reasons[tool.Code];
                if (!selected)
                {
                    if (!allowed.Contains(tool.Code))
                        toolReasons.Add("Rejected because the tool is outside the resolved intent and question-class allowlist.");
                    else if (scores[tool.Code] <= 0)
                        toolReasons.Add("Rejected because no positive question signal requires this tool.");
                    else
                        toolReasons.Add($"Rejected after the bounded {MaximumSelectedTools}-tool ranking limit.");
                }
                return new CelarAiToolSelectionDecision(
                    tool.Code,
                    selected ? "selected" : "rejected",
                    selected
                        ? required.Contains(tool.Code) ? "required" : "supplementary"
                        : "not_selected",
                    scores[tool.Code],
                    toolReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            })
            .OrderByDescending(decision => decision.Decision == "selected")
            .ThenByDescending(decision => decision.Score)
            .ThenBy(decision => decision.ToolCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CelarAiDeterministicToolSelection(
            ContractVersion,
            selectedTools,
            decisions,
            DefaultBudget,
            ClarificationRequired: ambiguous);
    }

    private static void AddIntentRequirements(
        string intent,
        string question,
        Action<string, string> require)
    {
        switch (intent)
        {
            case "identity_and_permissions":
                require("effective_identity", "Identity must be resolved before any permission answer.");
                require("role_permission_evidence", "Permission answers require the current governed policy.");
                if (ContainsAny(question, "manager", "team", "employee", "engineer"))
                    require("people_directory", "The question identifies a person, manager, or team.");
                if (ContainsAny(question, "reports to", "manager for"))
                    require("reporting_relationships", "The question requests an effective reporting relationship.");
                break;
            case "people_and_work":
            case "people_activity":
                require("people_directory", "People questions require an authorized identity match.");
                require("team_scope", "People questions must remain within authorized team scope.");
                if (ContainsAny(question, "task", "remaining hours", "work assigned"))
                    require("task_assignments", "The question requests assigned task or remaining-work evidence.");
                else
                    require("project_assignments", "The question requests current work assignments.");
                break;
            case "projects_and_delivery":
            case "internal_data":
                require("project_portfolio", "Project questions require current project lifecycle evidence.");
                if (ContainsAny(question, "assigned", "assignment", "works on", "staffed"))
                    require("project_assignments", "The question requests effective project assignments.");
                if (ContainsAny(question, "task", "remaining hours", "work assigned", "task owner"))
                    require("task_assignments", "The question requests task-level assignment evidence.");
                if (ContainsAny(question, "resource request", "unfilled", "staffing request"))
                    require("resource_requests", "The question requests engineering resource-request evidence.");
                if (ContainsAny(question, "flowhive", "wbs", "critical path", "timeline", "schedule"))
                    require("flowhive_plan", "The question requests the cited deterministic FlowHive plan.");
                if (ContainsAny(question, "project forge", "workbook", "effort estimate"))
                    require("project_forge", "The question requests the cited Project Forge workbook.");
                if (ContainsAny(question, "risk", "mitigation", "residual score"))
                    require("risk_register", "The question requests current governed risk evidence.");
                break;
            case "timesheets_and_approvals":
                if (ContainsAny(question, "timesheet", "time entry", "submitted", "missing time", "work log", "hours worked"))
                    require("timesheet_status", "The question requests current time-entry or period evidence.");
                if (ContainsAny(question, "approval", "approve", "declined", "rejected", "correction", "locked"))
                    require("approval_status", "The question requests current approval-stage evidence.");
                if (ContainsAny(question, "capacity", "utilization", "workload", "forecast", "remaining hours", "hours remain"))
                    require("capacity_utilization", "The question requires the governed capacity or utilization calculation.");
                if (ContainsAny(question, "remaining hours", "hours remain", "active assignments"))
                    require("task_assignments", "Remaining assignment work requires current task assignments.");
                if (ContainsAny(question, "engineer", "employee", "team"))
                    require("team_scope", "The calculation must remain inside authorized team scope.");
                break;
            case "financial_and_reporting":
                if (!ContainsAny(question, "expense", "billing", "invoice", "opportunity", "pipeline", "block-of-hours", "block of hours"))
                    require("project_financial_truth", "The question requests authoritative project financial truth.");
                if (ContainsAny(question, "expense", "billing", "invoice", "reconciliation", "billable"))
                    require("expense_billing", "The question requests expense, billing, or invoice evidence.");
                if (ContainsAny(question, "contract", "rate", "block of hours", "block-of-hours", "commercial assumption", "quote"))
                    require("commercial_contracts", "The question requests contract or rate authority.");
                if (ContainsAny(question, "opportunity", "opportunities", "pipeline", "future work", "delivery work"))
                    require("commercial_pipeline", "The question requests opportunity and future-delivery pipeline evidence.");
                break;
            case "documents_and_rag":
                require("project_documents", "Document questions require the authoritative document version.");
                require("private_retrieval", "Document claims require permission-filtered private citations.");
                break;
            case "troubleshooting":
            case "api_inventory":
            case "release_and_deployment":
            case "observability":
            case "security":
                require("system_diagnostics", "Operational conclusions require current sanitized runtime evidence.");
                if (ContainsAny(question, "api", "endpoint", "route"))
                    require("live_api_inventory", "The question requests current endpoint inventory.");
                if (ContainsAny(question, "deployment", "release", "rollback", "commit"))
                    require("release_deployment", "The question requests current release evidence.");
                if (ContainsAny(question, "oracle", "private runtime", "ollama", "ocr", "embedding", "clamav"))
                    require("oracle_runtime_readiness", "The question requests private-runtime readiness evidence.");
                if (ContainsAny(question, "defect", "bug", "known issue", "broken"))
                    require("defect_tracker", "The question requests governed defect evidence.");
                if (ContainsAny(question, "security", "secret", "token", "tls", "private port", "exposed"))
                    require("security_posture", "The question requests sanitized security-control evidence.");
                break;
            case "product_help":
            case "procedure":
            case "platform_identity":
                require("product_knowledge", "Procedure and capability answers come from source-controlled Pulse guidance.");
                break;
            case "general_knowledge":
                require("governed_public_information", "Public questions require an approved public source without Pulse context.");
                break;
            case "future_enhancement":
            case "architecture":
                require("product_knowledge", "Architecture advice must preserve current source-controlled contracts.");
                require("live_api_inventory", "Architecture advice must account for the current endpoint inventory.");
                break;
            case "general_system":
                require("product_knowledge", "General system questions begin with current source-controlled product guidance.");
                break;
        }
    }

    private static void AddClassRequirements(
        CelarAiAnswerQuestionClass questionClass,
        int attachmentCount,
        Action<string, string> require)
    {
        switch (questionClass)
        {
            case CelarAiAnswerQuestionClass.DocumentEvidence:
                require("project_documents", "Document evidence requires the authoritative document inventory.");
                require("private_retrieval", "Document evidence requires citation-ready private retrieval.");
                break;
            case CelarAiAnswerQuestionClass.CrossDomain:
                require("project_documents", "Cross-domain answers require authoritative document evidence.");
                require("private_retrieval", "Cross-domain answers require private citations.");
                require("project_portfolio", "Cross-domain answers require current structured project evidence.");
                break;
            case CelarAiAnswerQuestionClass.ProductProcedure:
                require("product_knowledge", "Product procedures require source-controlled operating guidance.");
                break;
            case CelarAiAnswerQuestionClass.RuntimeDiagnostic:
                require("system_diagnostics", "Runtime diagnostics require current sanitized probes.");
                break;
            case CelarAiAnswerQuestionClass.PublicCurrent:
            case CelarAiAnswerQuestionClass.PublicStable:
                require("governed_public_information", "Public answers require an approved public evidence route.");
                break;
            case CelarAiAnswerQuestionClass.ArchitectureEnhancement:
                require("product_knowledge", "Architecture advice must preserve current product contracts.");
                require("live_api_inventory", "Architecture advice must account for current APIs.");
                break;
        }
        if (attachmentCount > 0)
            require("conversation_attachments", "The answer must account for the user-owned attachment set.");
    }

    private static HashSet<string> Allowed(string intent, CelarAiAnswerQuestionClass questionClass)
    {
        if (intent.Length > 0 && AllowedByIntent.TryGetValue(intent, out var explicitSet))
            return new HashSet<string>(explicitSet, StringComparer.OrdinalIgnoreCase);
        return questionClass switch
        {
            CelarAiAnswerQuestionClass.DocumentEvidence => Set(
                "project_documents", "private_retrieval", "conversation_attachments",
                "document_extraction", "ocr", "malware_scan", "product_knowledge"),
            CelarAiAnswerQuestionClass.CrossDomain => Set(
                "project_documents", "private_retrieval", "conversation_attachments",
                "project_portfolio", "project_assignments", "task_assignments",
                "project_financial_truth", "expense_billing", "commercial_contracts",
                "flowhive_plan", "project_forge", "risk_register", "people_directory"),
            CelarAiAnswerQuestionClass.ProductProcedure => Set("product_knowledge"),
            CelarAiAnswerQuestionClass.RuntimeDiagnostic => DiagnosticSet(),
            CelarAiAnswerQuestionClass.PublicCurrent or CelarAiAnswerQuestionClass.PublicStable =>
                Set("governed_public_information"),
            CelarAiAnswerQuestionClass.ArchitectureEnhancement => Set(
                "product_knowledge", "live_api_inventory", "system_diagnostics",
                "observability", "security_posture"),
            CelarAiAnswerQuestionClass.StructuredOperational => Set(
                CelarAiUniversalToolCatalog.Tools
                    .Where(tool => tool.PrivateOnly && tool.Domain != "documents_retrieval")
                    .Select(tool => tool.Code)
                    .Append("product_knowledge")
                    .ToArray()),
            _ => Set("product_knowledge")
        };
    }

    private static HashSet<string> DiagnosticSet() => Set(
        "system_diagnostics", "live_api_inventory", "release_deployment",
        "oracle_runtime_readiness", "observability", "defect_tracker",
        "security_posture", "provider_configuration", "audit_history",
        "data_governance", "malware_scan", "ocr", "product_knowledge");

    private static bool IsAmbiguous(string question)
    {
        if (question.Length < 8) return true;
        return question is "help" or "what about that?" or "what about that"
            or "tell me more." or "tell me more" or "how many are there?"
            or "how many are there" or "is it ready?" or "is it ready";
    }

    private static bool ContainsPhrase(string question, string signal) =>
        question.Contains(Normalize(signal), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] signals) =>
        signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.OrdinalIgnoreCase);
}
