namespace ProjectTime.Api.Ai;

public sealed record CelarAiOperationsIntentRequest(
    string? Question,
    string? ConversationId = null,
    string? ProjectCode = null,
    string? ProjectName = null,
    string? ModuleCode = null);

public sealed record CelarAiOperationsIntentDecision(
    string ContractVersion,
    string Intent,
    string ActionKind,
    decimal Confidence,
    bool RequiresUserConfirmation,
    bool MutationRequested,
    bool MutationAuthorized,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> RequiredInputs,
    DateTimeOffset DecidedAt);

/// <summary>
/// Authoritative server-side router for Ask Celar AI operational actions. Browser
/// phrase matching may provide a convenience fallback only; it is never the
/// authorization or final intent decision.
/// </summary>
public static class CelarAiOperationsIntentRouter
{
    public const string ContractVersion = "celar-ai-operations-intent-router-v1-20260810";

    public static CelarAiOperationsIntentDecision Route(string? question)
    {
        var normalized = CelarAiOperationsPolicy.Normalize(question);
        var reasons = new List<string>();
        var required = new List<string>();
        string intent;
        string action;
        decimal confidence;
        var confirmation = false;
        var mutationRequested = false;

        if (CelarAiOperationsPolicy.IsDefectIntent(normalized))
        {
            intent = "guided_defect_intake";
            action = "open_defect_questionnaire";
            confidence = 0.98m;
            confirmation = true;
            mutationRequested = true;
            reasons.Add("The user explicitly requested that a defect or issue be opened or reported.");
            required.AddRange([
                "affected location or module",
                "expected and actual behavior",
                "reproduction steps",
                "business or user impact",
                "review and exact CREATE DEFECT confirmation"
            ]);
        }
        else if (CelarAiOperationsPolicy.IsTroubleshootingIntent(normalized))
        {
            intent = "troubleshooting";
            action = "run_read_only_diagnostics";
            confidence = 0.96m;
            reasons.Add("The question requests diagnosis, health, failure, timeout, or unavailable-state evidence.");
            required.Add("current effective-user authorization and bounded allowlisted probes");
        }
        else if (ContainsAny(normalized,
                     "health and automation", "automatic defects", "monitor policies",
                     "monitoring status", "synthetic failure", "health policy"))
        {
            intent = "health_automation";
            action = "open_health_automation";
            confidence = 0.95m;
            reasons.Add("The question explicitly requests monitor policy, health automation, or synthetic-Test controls.");
            required.Add("Module 078 visibility and protected-Test policy authorization");
        }
        else if (ContainsAny(normalized,
                     "comment on defect", "assign defect", "reassign defect", "resolve defect",
                     "reopen defect", "close defect", "block defect", "mark duplicate"))
        {
            intent = "defect_lifecycle";
            action = "open_defect_lifecycle";
            confidence = 0.93m;
            confirmation = true;
            mutationRequested = true;
            reasons.Add("The question requests a governed Module 076 lifecycle change.");
            required.AddRange([
                "defect number",
                "current revision",
                "authorized actual user with View-As disabled",
                "reason and exact action confirmation"
            ]);
        }
        else
        {
            intent = "answer_question";
            action = "continue_standard_ask_celar_ai";
            confidence = normalized.Length < 8 ? 0.35m : 0.75m;
            reasons.Add("No explicit operational mutation or diagnostic command was resolved.");
            if (normalized.Length < 8)
                required.Add("a complete question and business scope");
        }

        return new CelarAiOperationsIntentDecision(
            ContractVersion,
            intent,
            action,
            confidence,
            confirmation,
            mutationRequested,
            MutationAuthorized: false,
            reasons,
            required,
            DateTimeOffset.UtcNow);
    }

    private static bool ContainsAny(string value, params string[] signals) =>
        signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));
}
