using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiEscalationSanitizer
{
    private static readonly Regex SecretAssignment = new(
        @"\b(api[_\- ]?key|access[_\- ]?token|refresh[_\- ]?token|client[_\- ]?secret|password|passwd|secret|connection[_\- ]?string)\b\s*[:=]\s*[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Email = new(
        @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Url = new(
        @"\bhttps?://[^\s)\]}>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Ipv4 = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
        RegexOptions.Compiled);

    private static readonly Regex GuidValue = new(
        @"\b[0-9a-f]{8}\-[0-9a-f]{4}\-[1-5][0-9a-f]{3}\-[89ab][0-9a-f]{3}\-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CurrencyValue = new(
        @"(?<!\w)(?:USD\s*)?\$\s?\d[\d,]*(?:\.\d{1,2})?|\b\d[\d,]*(?:\.\d{1,2})?\s?(?:USD|dollars?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Phone = new(
        @"(?<!\d)(?:\+?1[\s.\-]?)?\(?\d{3}\)?[\s.\-]\d{3}[\s.\-]\d{4}(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex LongIdentifier = new(
        @"\b(?:[A-Z]{2,}[A-Z0-9]*[\-_][A-Z0-9\-_]{3,}|\d{8,})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PersonOrCustomerLabel = new(
        @"\b(customer|client|employee|engineer|manager|contact|user)\s*(?:name)?\s*[:=]\s*[^\r\n,;]{2,80}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PulseAiSanitizationResult Sanitize(PulseAiSanitizationRequest request) =>
        SanitizeInternal(request, executionRequested: false);

    /// <summary>
    /// Produces a capsule that may be executed only when every policy gate passes.
    /// The caller must still prove that no private document text, people records,
    /// or financial values were used to construct the generic problem statement.
    /// </summary>
    public PulseAiSanitizationResult SanitizeForExecution(PulseAiSanitizationRequest request) =>
        SanitizeInternal(request, executionRequested: true);

    private static PulseAiSanitizationResult SanitizeInternal(
        PulseAiSanitizationRequest request,
        bool executionRequested)
    {
        var purpose = Clean(request.Purpose, 120, "unspecified_reasoning_support");
        var classification = Clean(request.Classification, 80, "restricted").ToLowerInvariant();
        var original = Clean(request.Content, 20000, string.Empty);
        var current = original;
        var redactions = new List<PulseAiRedactionEvidence>();
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        current = Replace(current, SecretAssignment, "[REDACTED_SECRET]", "secrets_and_credentials", redactions, removed);
        current = Replace(current, Email, "[REDACTED_EMAIL]", "email_addresses", redactions, removed);
        current = Replace(current, Url, "[REDACTED_URL]", "urls_and_external_locations", redactions, removed);
        current = Replace(current, Ipv4, "[REDACTED_IP]", "ip_addresses", redactions, removed);
        current = Replace(current, GuidValue, "[REDACTED_RECORD_ID]", "record_identifiers", redactions, removed);
        current = Replace(current, CurrencyValue, "[REDACTED_FINANCIAL_VALUE]", "financial_values", redactions, removed);
        current = Replace(current, Phone, "[REDACTED_PHONE]", "phone_numbers", redactions, removed);
        current = Replace(current, PersonOrCustomerLabel, "$1: [REDACTED_IDENTITY]", "named_people_and_customers", redactions, removed);
        current = Replace(current, LongIdentifier, "[REDACTED_IDENTIFIER]", "host_project_and_long_identifiers", redactions, removed);

        foreach (var sensitiveTerm in request.SensitiveTerms ?? [])
        {
            var term = sensitiveTerm?.Trim();
            if (string.IsNullOrWhiteSpace(term)) continue;
            var expression = new Regex(Regex.Escape(term), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            current = Replace(
                current,
                expression,
                "[REDACTED_EXPLICIT_TERM]",
                "explicit_sensitive_terms",
                redactions,
                removed);
        }

        current = Regex.Replace(current, @"[ \t]+", " ").Trim();
        current = Regex.Replace(current, @"(?:\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        if (current.Length > 6000) current = current[..6000].TrimEnd() + " [TRUNCATED]";

        var blockers = new List<string>();
        if (!executionRequested)
        {
            blockers.Add("This endpoint creates a preview capsule only and never calls Claude, OpenAI, or another external provider.");
            if (!request.AcknowledgePreviewOnly)
                blockers.Add("The caller did not acknowledge that this is a preview-only operation.");
            if (!Boolean("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", false))
                blockers.Add("Sanitized external escalation is disabled by ProjectPulse runtime policy.");
            if (classification.Contains("financial", StringComparison.OrdinalIgnoreCase))
                blockers.Add("Financial and commercial context is blocked from external escalation by default.");
            if (classification is "restricted" or "confidential")
                blockers.Add("Restricted or confidential material requires a separate approved escalation policy even after redaction.");
            if (ContainsAny(original, "statement of work", " sow ", "global solution design", " gsd ", "contract", "rate card"))
                blockers.Add("The source appears to contain internal document or commercial context; a human privacy review is required.");
        }
        else
        {
            if (!request.AcknowledgePreviewOnly)
                blockers.Add("The caller did not explicitly acknowledge sanitized external execution.");
            if (!Boolean("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", false))
                blockers.Add("Sanitized external escalation is disabled by ProjectPulse runtime policy.");
            if (classification is not ("public" or "internal_generic" or "generic"))
                blockers.Add("Only public or internal-generic problem statements are eligible for sanitized external execution.");
            if (removed.Contains("financial_values"))
                blockers.Add("A financial value was detected. Financial and commercial content is not eligible for this external execution path.");
            if (removed.Contains("named_people_and_customers"))
                blockers.Add("A named person or customer was detected. The generic capsule must be rebuilt without identity context.");
            if (removed.Contains("secrets_and_credentials"))
                blockers.Add("Credential-like content was detected. The capsule is blocked from external execution.");
        }

        if (current.Contains("[REDACTED_SECRET]", StringComparison.Ordinal))
            blockers.Add("Credential-like content was detected. The capsule must not be externally executed.");
        if (string.IsNullOrWhiteSpace(current))
            blockers.Add("No useful sanitized reasoning context remains after redaction.");

        var authorized = executionRequested && blockers.Count == 0;
        var status = string.IsNullOrWhiteSpace(current)
            ? "sanitized_capsule_empty"
            : executionRequested
                ? authorized
                    ? "sanitized_capsule_execution_ready"
                    : "sanitized_capsule_execution_blocked"
                : "sanitized_capsule_preview_ready";

        return new PulseAiSanitizationResult(
            Status: status,
            Purpose: purpose,
            Classification: classification,
            SanitizedCapsule: current,
            OriginalLength: original.Length,
            SanitizedLength: current.Length,
            Redactions: redactions,
            RemovedCategories: removed.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            RemainingAllowedContext:
            [
                "generic technical problem structure",
                "deidentified constraints and dependencies",
                "non-customer-specific reasoning question",
                "requested output schema",
                "generic technology categories after policy review"
            ],
            ExternalExecutionAuthorized: authorized,
            BlockedReasons: blockers,
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    private static string Replace(
        string source,
        Regex expression,
        string replacement,
        string category,
        ICollection<PulseAiRedactionEvidence> evidence,
        ISet<string> removed)
    {
        var count = 0;
        var result = expression.Replace(source, match =>
        {
            count += 1;
            if (replacement.Contains("$1", StringComparison.Ordinal))
            {
                return replacement.Replace("$1", match.Groups[1].Value, StringComparison.Ordinal);
            }
            return replacement;
        });

        if (count > 0)
        {
            evidence.Add(new PulseAiRedactionEvidence(category, count, replacement));
            removed.Add(category);
        }

        return result;
    }

    private static bool ContainsAny(string source, params string[] values) =>
        values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string? value, int maximumLength, string fallback)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
}
