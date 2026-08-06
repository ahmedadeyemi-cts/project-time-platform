using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiEscalationSanitizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private const RegexOptions CommonOptions =
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex SecretAssignment = new(
        @"\b(api[_\- ]?key|access[_\- ]?token|refresh[_\- ]?token|client[_\- ]?secret|password|passwd|secret|connection[_\- ]?string)\b\s*[:=]\s*[^\s,;]+",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex HighEntropyToken = new(
        @"\b(?:eyJ[A-Za-z0-9_\-]{20,}(?:\.[A-Za-z0-9_\-]{10,}){1,2}|[A-Za-z0-9+/_=\-]{32,})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex Email = new(
        @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex Url = new(
        @"\bhttps?://[^\s)\]}>]+",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex HostName = new(
        @"(?<![\p{L}\p{N}_\-])(?:[a-z0-9](?:[a-z0-9\-]{0,61}[a-z0-9])?\.)+(?:local|internal|corp|lan|com|net|org|cloud|io)(?![\p{L}\p{N}_\-])",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex InternalHostName = new(
        @"(?<![\p{L}\p{N}_\-])(?:[a-z0-9](?:[a-z0-9\-]{0,61}[a-z0-9])?\.)+(?:local|internal|corp|lan)(?![\p{L}\p{N}_\-])",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex Ipv4 = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex Ipv6 = new(
        @"(?<![A-F0-9:])(?:[A-F0-9]{1,4}:){2,7}[A-F0-9]{1,4}(?![A-F0-9:])",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex MacAddress = new(
        @"\b(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex GuidValue = new(
        @"\b[0-9a-f]{8}\-[0-9a-f]{4}\-[1-5][0-9a-f]{3}\-[89ab][0-9a-f]{3}\-[0-9a-f]{12}\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex CurrencyValue = new(
        @"(?<!\w)(?:USD\s*)?\$\s?\d[\d,]*(?:\.\d{1,2})?|\b\d[\d,]*(?:\.\d{1,2})?\s?(?:USD|dollars?)\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex Phone = new(
        @"(?<!\d)(?:\+?1[\s.\-]?)?\(?\d{3}\)?[\s.\-]\d{3}[\s.\-]\d{4}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex SocialSecurityNumber = new(
        @"(?<!\d)\d{3}[\- ]\d{2}[\- ]\d{4}(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex PostalAddress = new(
        @"\b\d{1,6}[ \t]+(?:[\p{L}\p{N}.'’\-]+[ \t]+){1,6}(?:street|st|avenue|ave|road|rd|drive|dr|lane|ln|boulevard|blvd|court|ct|parkway|pkwy|highway|hwy)\b\.?",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex CalendarDate = new(
        @"(?<!\d)(?:\d{4}[\-/]\d{1,2}[\-/]\d{1,2}|\d{1,2}[\-/]\d{1,2}[\-/]\d{2,4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex PrivateDocumentOrCommercialMarker = new(
        @"\b(?:statement[ \t]+of[ \t]+work|sow|global[ \t]+solution[ \t]+design|gsd|master[ \t]+services[ \t]+agreement|msa|non[\- ]disclosure[ \t]+agreement|nda|contract|rate[ \t]*card|pricing|proposal|order[ \t]+form|customer[ \t\-]+document|private[ \t\-]+document|confidential|proprietary)\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex LongIdentifier = new(
        @"\b(?:[A-Z]{2,}[A-Z0-9]*[\-_][A-Z0-9\-_]{3,}|\d{8,})\b",
        CommonOptions,
        RegexTimeout);

    // Identity labels never use \s around separators because \s can cross a
    // newline and accidentally treat a following free-text field as a name.
    private static readonly Regex CustomerOrOrganizationLabel = new(
        @"\b(customer|client|account|organization|company|tenant)(?:[ \t]+(?:legal[ \t]+)?name)?[ \t]*[:=][ \t]*(?!\[REDACTED)[^\r\n,;]{2,120}",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex PersonRoleLabel = new(
        @"\b(employee|engineer|manager|project[ \t]+manager|contact|user|owner|request(?:er|or)|approver|recipient)(?:[ \t]+(?:display[ \t]+|legal[ \t]+|full[ \t]+)?name)?[ \t]*[:=][ \t]*(?!\[REDACTED)[^\r\n,;]{2,120}",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex OrganizationName = new(
        @"\b[A-Z0-9][\p{L}\p{N}&'’.\-]*(?:[ \t]+[A-Z0-9][\p{L}\p{N}&'’.\-]*){0,5}[ \t]+(?:L\.?L\.?C\.?|L\.?P\.?|Inc\.?|Incorporated|Corp\.?|Corporation|Company|Co\.?|University|Hospital|Bank|Agency|Department|Municipality)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex HonorificName = new(
        @"\b(?:Mr|Mrs|Ms|Miss|Dr|Professor|Prof)\.?[ \t]+[A-Z][\p{L}\p{M}'’\-]+(?:[ \t]+[A-Z][\p{L}\p{M}'’\-]+){0,3}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex RelationshipIdentity = new(
        @"\b(met(?:[ \t]+with)?|spoke[ \t]+with|worked[ \t]+with|coordinated[ \t]+with|contacted|called|emailed|messaged|notified|asked|told)[ \t]+(?:the[ \t]+)?(?:mr|mrs|ms|miss|dr|professor|prof)\.?[ \t]+[\p{L}\p{M}][\p{L}\p{M}'’\-]*(?:[ \t]+[\p{L}\p{M}][\p{L}\p{M}'’\-]*){0,3}\b|\b(met(?:[ \t]+with)?|spoke[ \t]+with|worked[ \t]+with|coordinated[ \t]+with|contacted|called|emailed|messaged|notified|asked|told)[ \t]+(?:the[ \t]+)?[\p{L}\p{M}][\p{L}\p{M}'’\-]*(?:[ \t]+[\p{L}\p{M}][\p{L}\p{M}'’\-]*){0,3}\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex LeadingNamedActor = new(
        @"(?:^|(?<=[.!?][ \t]))[A-Z][\p{L}\p{M}'’\-]+(?:[ \t]+[A-Z][\p{L}\p{M}'’\-]+){0,3}[ \t]+(?=(?:asked|approved|requested|confirmed|reported|provided|joined|emailed|called|reviewed|validated|assigned|submitted|supported|coordinated|configured|tested|documented|investigated|implemented|updated|prepared|performed|verified|resolved|troubleshot)\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex UnsupportedOutcomeClaim = new(
        @"\b(?:completed|resolved|fixed|closed|approved|accepted|delivered)\b|\b(?:issue|incident|problem|request|task|work|implementation|configuration|service|system|deployment|migration|testing|validation|remediation|rollout|change)[ \t]+(?:was|were|is|are|has[ \t]+been|have[ \t]+been)[ \t]+(?:implemented|validated|verified|successful)\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex PossessiveProperName = new(
        @"\b[A-Z][\p{L}\p{M}'’\-]{1,40}(?:'s|’s)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex CustomerContextName = new(
        @"\b(customer|client|tenant|account|employee|engineer|manager|contact|owner|request(?:er|or)|approver|recipient)[ \t]+(?:named[ \t]+|called[ \t]+)?[\p{L}\p{M}][\p{L}\p{M}'’\-]*(?:[ \t]+[\p{L}\p{M}][\p{L}\p{M}'’\-]*){0,4}\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex LocationOrFacilityLabel = new(
        @"\b(location|site|office|facility|data[ \t]+center|address)[ \t]*[:=][ \t]*(?!\[REDACTED)[^\r\n,;]{2,160}",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex NamedLocationContext = new(
        @"\b(at|from|inside|within)[ \t]+(?:the[ \t]+)?[A-Z][\p{L}\p{M}'’\-]+(?:[ \t]+[A-Z][\p{L}\p{M}'’\-]+){0,3}(?:[ \t]+(?:site|office|facility|data[ \t]+center))?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex UserOrAccountIdentifier = new(
        @"\b(?:user(?:name)?|login|account|employee|customer|client|tenant|ticket|case|project|task|assignment)[_\- ]?(?:id|number|no)?[ \t]*[:=][ \t]*[A-Z0-9._\\\-]{2,120}\b|\b[A-Z0-9._\-]{2,40}\\[A-Z0-9._\-]{2,80}\b|(?<![\p{L}\p{N}])@[A-Z0-9_]{2,40}\b",
        CommonOptions,
        RegexTimeout);

    private static readonly Regex PotentialProperNoun = new(
        @"(?<![\p{L}\p{N}_\[])\b(?:[A-Z][\p{L}\p{M}'’\-]{1,40}|[A-Z]{2,12})\b(?![\p{L}\p{N}_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly HashSet<string> ApprovedCapitalizedWords = new(StringComparer.Ordinal)
    {
        "A", "AI", "API", "Active", "Additional", "After", "Alto", "Analyzed", "Ansible", "Assessed",
        "Azure", "AWS", "BGP", "Category", "Cisco", "Configured", "Coordinated", "Created",
        "Customer", "Developed", "DHCP", "DNS", "Docker", "Documented", "EDR", "Engineer", "Entra",
        "Evaluated", "Exchange", "Fortinet", "Google", "HPE", "HTTP", "HTTPS", "IAM", "If",
        "Implemented", "Implementation", "Installed", "Investigated", "I",
        "JSON", "Juniper", "Kubernetes", "LAN", "Linux", "Make", "Meraki", "MFA", "Microsoft",
        "MySQL", "Never", "No", "Open", "Oracle", "OSPF", "PaaS", "Palo", "PostgreSQL", "Prefer",
        "Performed", "Prepared", "Primary", "Privacy", "Project", "PSA", "RDP", "REST",
        "Return", "Reviewed", "Row", "Rules", "SaaS", "SharePoint", "SIEM", "SQL", "SSH", "SSO",
        "Supported", "Task", "TCP", "Teams", "Terraform", "Tested", "The", "This", "Time", "TLS",
        "Troubleshot", "UDP", "Updated", "Use", "Validated", "Verified",
        "VLAN", "VMware", "VPN", "WAN", "When", "Windows", "Work", "Write"
    };

    // Provider output often begins a sentence with an ordinary work verb. Keep
    // that grammar valid without treating every first word as an approved name.
    // The list is deliberately closed: an unknown leading token such as a
    // person's or customer's name still fails the output privacy gate.
    private static readonly HashSet<string> ApprovedSentenceStarters = new(StringComparer.Ordinal)
    {
        "Analyzed", "Assessed", "Assisted", "Configured", "Coordinated", "Created",
        "Developed", "Documented", "Evaluated", "Implemented", "Installed", "Investigated",
        "Monitored", "Performed", "Planned", "Prepared", "Provided", "Reviewed",
        "Supported", "Tested", "Troubleshot", "Updated", "Validated", "Verified"
    };

    private static readonly HashSet<string> OutputBlockingCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "secrets_and_credentials",
        "high_entropy_tokens",
        "email_addresses",
        "urls_and_external_locations",
        "hostnames_and_internal_locations",
        "ip_addresses",
        "network_hardware_identifiers",
        "record_identifiers",
        "financial_values",
        "phone_numbers",
        "government_identifiers",
        "postal_addresses",
        "named_people_and_customers",
        "organization_and_customer_names",
        "locations_and_facilities",
        "user_and_account_identifiers",
        "explicit_sensitive_terms",
        "unapproved_proper_nouns"
    };

    public PulseAiSanitizationResult Sanitize(PulseAiSanitizationRequest request) =>
        SanitizeInternal(request, executionRequested: false);

    /// <summary>
    /// Produces a capsule that may be executed only when every policy gate passes.
    /// The caller must still prove that no private document text, people records,
    /// or financial values were used to construct the generic problem statement.
    /// </summary>
    public PulseAiSanitizationResult SanitizeForExecution(PulseAiSanitizationRequest request) =>
        SanitizeInternal(request, executionRequested: true);

    /// <summary>
    /// Revalidates untrusted provider output before it crosses back into a
    /// customer-visible workflow. A provider response that reintroduces a known
    /// identity, credential, internal location, or uncertain named entity is
    /// rejected instead of being silently displayed or relayed.
    /// </summary>
    public bool IsExternalOutputSafe(
        string? content,
        IReadOnlyList<string>? sensitiveTerms,
        out string decisionCode)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            decisionCode = "external_output_empty";
            return false;
        }

        var inspection = SanitizeInternal(
            new PulseAiSanitizationRequest(
                Purpose: "external_output_privacy_validation",
                Content: content,
                Classification: "generic",
                SensitiveTerms: sensitiveTerms?.ToArray() ?? [],
                AcknowledgePreviewOnly: true),
            executionRequested: false);

        var blockingCategory = inspection.RemovedCategories
            .FirstOrDefault(OutputBlockingCategories.Contains);
        if (!string.IsNullOrWhiteSpace(blockingCategory))
        {
            decisionCode = blockingCategory switch
            {
                "explicit_sensitive_terms" or "named_people_and_customers" or
                    "organization_and_customer_names" or "unapproved_proper_nouns" =>
                    "external_output_identity_validation_failed",
                "secrets_and_credentials" or "high_entropy_tokens" =>
                    "external_output_credential_validation_failed",
                _ => "external_output_privacy_validation_failed"
            };
            return false;
        }

        if (content.Contains("[REDACTED_", StringComparison.OrdinalIgnoreCase))
        {
            decisionCode = "external_output_contains_redaction_marker";
            return false;
        }

        decisionCode = "external_output_privacy_validated";
        return true;
    }

    /// <summary>
    /// Applies the stricter customer-facing Timesheet claim policy after the
    /// common external-output privacy boundary. Other governed capabilities may
    /// legitimately restate a server-supplied completion fact, including the
    /// fixed Module 064 production readiness probe.
    /// </summary>
    public bool IsTimesheetExternalOutputSafe(
        string? content,
        IReadOnlyList<string>? sensitiveTerms,
        out string decisionCode)
    {
        if (!IsExternalOutputSafe(content, sensitiveTerms, out decisionCode))
            return false;

        if (UnsupportedOutcomeClaim.IsMatch(content!))
        {
            decisionCode = "external_output_unsupported_outcome_claim";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Allows a user-authored public question to retain ordinary names, places,
    /// dates, and subject terminology while still rejecting credentials,
    /// internal identifiers, private-document markers, and explicit protected
    /// terms. This path is valid only for the router's isolated general-
    /// knowledge purpose; it is never used for Pulse or attachment questions.
    /// </summary>
    public bool TryPreparePublicQuestion(
        string? question,
        IReadOnlyList<string>? sensitiveTerms,
        out string safeQuestion,
        out string decisionCode)
    {
        safeQuestion = Clean(question, 4_000, string.Empty);
        if (safeQuestion.Length == 0)
        {
            decisionCode = "public_general_question_empty";
            return false;
        }

        var protectedTerms = (sensitiveTerms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Take(128)
            .ToArray();
        var publicQuestion = safeQuestion;
        var containsProtectedTerm = protectedTerms.Any(term =>
            term.Length >= 2 && SensitiveTermExpression(term).IsMatch(publicQuestion));
        if (SecretAssignment.IsMatch(safeQuestion)
            || HighEntropyToken.IsMatch(safeQuestion)
            || Email.IsMatch(safeQuestion)
            || InternalHostName.IsMatch(safeQuestion)
            || Ipv4.IsMatch(safeQuestion)
            || Ipv6.IsMatch(safeQuestion)
            || MacAddress.IsMatch(safeQuestion)
            || GuidValue.IsMatch(safeQuestion)
            || SocialSecurityNumber.IsMatch(safeQuestion)
            || CustomerOrOrganizationLabel.IsMatch(safeQuestion)
            || PersonRoleLabel.IsMatch(safeQuestion)
            || LocationOrFacilityLabel.IsMatch(safeQuestion)
            || UserOrAccountIdentifier.IsMatch(safeQuestion)
            || PrivateDocumentOrCommercialMarker.IsMatch(safeQuestion)
            || containsProtectedTerm
            || safeQuestion.Contains("[REDACTED_", StringComparison.OrdinalIgnoreCase))
        {
            safeQuestion = string.Empty;
            decisionCode = "public_general_question_sensitive_content_blocked";
            return false;
        }

        safeQuestion = Regex.Replace(safeQuestion, @"[ \t]+", " ").Trim();
        safeQuestion = Regex.Replace(safeQuestion, @"(?:\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        decisionCode = "public_general_question_validated";
        return true;
    }

    public bool IsPublicExternalOutputSafe(
        string? content,
        IReadOnlyList<string>? sensitiveTerms,
        out string decisionCode)
    {
        var value = Clean(content, 20_000, string.Empty);
        if (value.Length == 0)
        {
            decisionCode = "external_output_empty";
            return false;
        }

        var protectedTerms = (sensitiveTerms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Take(128)
            .ToArray();
        if (SecretAssignment.IsMatch(value)
            || HighEntropyToken.IsMatch(value)
            || Email.IsMatch(value)
            || InternalHostName.IsMatch(value)
            || Ipv4.IsMatch(value)
            || Ipv6.IsMatch(value)
            || MacAddress.IsMatch(value)
            || GuidValue.IsMatch(value)
            || SocialSecurityNumber.IsMatch(value)
            || UserOrAccountIdentifier.IsMatch(value)
            || protectedTerms.Any(term => term.Length >= 2 && SensitiveTermExpression(term).IsMatch(value))
            || value.Contains("[REDACTED_", StringComparison.OrdinalIgnoreCase))
        {
            decisionCode = "public_external_output_privacy_validation_failed";
            return false;
        }

        decisionCode = "public_external_output_privacy_validated";
        return true;
    }

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
        var invalidSensitiveTermInventory = false;

        current = Replace(current, SecretAssignment, "[REDACTED_SECRET]", "secrets_and_credentials", redactions, removed);
        current = Replace(current, HighEntropyToken, "[REDACTED_SECRET]", "high_entropy_tokens", redactions, removed);
        current = Replace(current, Email, "[REDACTED_EMAIL]", "email_addresses", redactions, removed);
        current = Replace(current, Url, "[REDACTED_URL]", "urls_and_external_locations", redactions, removed);
        current = Replace(current, HostName, "[REDACTED_HOST]", "hostnames_and_internal_locations", redactions, removed);
        current = Replace(current, Ipv4, "[REDACTED_IP]", "ip_addresses", redactions, removed);
        current = Replace(current, Ipv6, "[REDACTED_IP]", "ip_addresses", redactions, removed);
        current = Replace(current, MacAddress, "[REDACTED_NETWORK_ID]", "network_hardware_identifiers", redactions, removed);
        current = Replace(current, GuidValue, "[REDACTED_RECORD_ID]", "record_identifiers", redactions, removed);
        current = Replace(current, CurrencyValue, "[REDACTED_FINANCIAL_VALUE]", "financial_values", redactions, removed);
        current = Replace(current, Phone, "[REDACTED_PHONE]", "phone_numbers", redactions, removed);
        current = Replace(current, SocialSecurityNumber, "[REDACTED_GOVERNMENT_ID]", "government_identifiers", redactions, removed);
        current = Replace(current, PostalAddress, "[REDACTED_ADDRESS]", "postal_addresses", redactions, removed);
        current = Replace(current, CalendarDate, "[REDACTED_DATE]", "temporal_values", redactions, removed);
        current = Replace(current, CustomerOrOrganizationLabel, "$1: [REDACTED_IDENTITY]", "named_people_and_customers", redactions, removed);
        current = Replace(current, PersonRoleLabel, "$1: [REDACTED_IDENTITY]", "named_people_and_customers", redactions, removed);
        current = Replace(current, HonorificName, "[REDACTED_PERSON]", "named_people_and_customers", redactions, removed);
        current = Replace(current, OrganizationName, "[REDACTED_ORGANIZATION]", "organization_and_customer_names", redactions, removed);
        current = Replace(current, RelationshipIdentity, "[REDACTED_IDENTITY_RELATIONSHIP]", "named_people_and_customers", redactions, removed);
        current = Replace(current, LeadingNamedActor, "[REDACTED_PERSON] ", "named_people_and_customers", redactions, removed);
        current = Replace(current, PossessiveProperName, "[REDACTED_IDENTITY]", "named_people_and_customers", redactions, removed);
        current = Replace(current, CustomerContextName, "$1 [REDACTED_IDENTITY]", "named_people_and_customers", redactions, removed);
        current = Replace(current, LocationOrFacilityLabel, "$1: [REDACTED_LOCATION]", "locations_and_facilities", redactions, removed);
        current = Replace(current, NamedLocationContext, "$1 [REDACTED_LOCATION]", "locations_and_facilities", redactions, removed);
        current = Replace(current, UserOrAccountIdentifier, "[REDACTED_ACCOUNT_ID]", "user_and_account_identifiers", redactions, removed);
        current = Replace(current, LongIdentifier, "[REDACTED_IDENTIFIER]", "host_project_and_long_identifiers", redactions, removed);

        var sensitiveTerms = new List<string>();
        foreach (var sensitiveTerm in request.SensitiveTerms ?? [])
        {
            var term = sensitiveTerm?.Trim();
            if (string.IsNullOrWhiteSpace(term)) continue;
            if (term.Length > 256 || term.Any(char.IsControl))
            {
                invalidSensitiveTermInventory = true;
                continue;
            }
            if (term.Length < 2) continue;
            sensitiveTerms.Add(term);
        }

        if (sensitiveTerms.Count > 128)
        {
            invalidSensitiveTermInventory = true;
            sensitiveTerms = sensitiveTerms.Take(128).ToList();
        }

        foreach (var term in sensitiveTerms
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(value => value.Length))
        {
            var expression = SensitiveTermExpression(term);
            current = Replace(
                current,
                expression,
                "[REDACTED_EXPLICIT_TERM]",
                "explicit_sensitive_terms",
                redactions,
                removed);
        }

        // Names and customer aliases that were not part of the authoritative
        // identity inventory are conservatively removed. The small allowlist is
        // limited to sentence scaffolding and common technology terms; an
        // unfamiliar capitalized entity never reaches an external provider.
        current = ReplaceUnknownProperNouns(current, redactions, removed);

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
            if (invalidSensitiveTermInventory)
                blockers.Add("The sensitive-term inventory was invalid or exceeded the governed limit; external execution was blocked.");
            if (removed.Contains("financial_values"))
                blockers.Add("A financial value was detected. Financial and commercial content is not eligible for this external execution path.");
            if (removed.Contains("secrets_and_credentials") || removed.Contains("high_entropy_tokens"))
                blockers.Add("Credential-like content was detected. The capsule is blocked from external execution.");
            if (PrivateDocumentOrCommercialMarker.IsMatch(original))
                blockers.Add("Private-document or commercial-source markers were detected. The request must be rebuilt from non-document facts before external execution.");

            if (HasResidualSensitiveData(current, sensitiveTerms))
                blockers.Add("Personal, customer, account, location, or other sensitive identity data may remain after de-identification; external execution was blocked.");
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

        // Preview-only compatibility contract: ExternalExecutionAuthorized: false.
        // Execution can become true only through SanitizeForExecution after every
        // independent policy, classification, and redaction blocker has passed.
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
                "deidentified engineer-provided activity facts",
                "non-customer-specific constraints and dependencies",
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

    private static string ReplaceUnknownProperNouns(
        string source,
        ICollection<PulseAiRedactionEvidence> evidence,
        ISet<string> removed)
    {
        var count = 0;
        var result = PotentialProperNoun.Replace(source, match =>
        {
            if (IsApprovedCapitalizedWord(source, match)) return match.Value;
            count += 1;
            return "[REDACTED_UNAPPROVED_ENTITY]";
        });

        if (count > 0)
        {
            evidence.Add(new PulseAiRedactionEvidence(
                "unapproved_proper_nouns",
                count,
                "[REDACTED_UNAPPROVED_ENTITY]"));
            removed.Add("unapproved_proper_nouns");
        }

        return result;
    }

    private static bool IsApprovedCapitalizedWord(string source, Match match)
    {
        if (ApprovedCapitalizedWords.Contains(match.Value)) return true;
        if (!ApprovedSentenceStarters.Contains(match.Value)) return false;
        if (match.Index == 0) return true;

        var prefix = source.AsSpan(0, match.Index).TrimEnd();
        return prefix.Length > 0 && prefix[^1] is '.' or '!' or '?';
    }

    private static Regex SensitiveTermExpression(string term)
    {
        var segments = Regex.Split(term.Trim(), @"[ \t]+")
            .Where(segment => segment.Length > 0)
            .Select(Regex.Escape)
            .ToArray();
        var expression = string.Join(@"[ \t]+", segments);
        if (char.IsLetterOrDigit(term[0])) expression = @"(?<![\p{L}\p{N}])" + expression;
        if (char.IsLetterOrDigit(term[^1])) expression += @"(?![\p{L}\p{N}])";
        return new Regex(expression, CommonOptions, RegexTimeout);
    }

    private static bool HasResidualSensitiveData(string content, IReadOnlyCollection<string> sensitiveTerms)
    {
        if (SecretAssignment.IsMatch(content)
            || HighEntropyToken.IsMatch(content)
            || Email.IsMatch(content)
            || Url.IsMatch(content)
            || HostName.IsMatch(content)
            || Ipv4.IsMatch(content)
            || Ipv6.IsMatch(content)
            || MacAddress.IsMatch(content)
            || GuidValue.IsMatch(content)
            || Phone.IsMatch(content)
            || SocialSecurityNumber.IsMatch(content)
            || PostalAddress.IsMatch(content)
            || CustomerOrOrganizationLabel.IsMatch(content)
            || PersonRoleLabel.IsMatch(content)
            || OrganizationName.IsMatch(content)
            || HonorificName.IsMatch(content)
            || RelationshipIdentity.IsMatch(content)
            || LeadingNamedActor.IsMatch(content)
            || PossessiveProperName.IsMatch(content)
            || CustomerContextName.IsMatch(content)
            || LocationOrFacilityLabel.IsMatch(content)
            || NamedLocationContext.IsMatch(content)
            || UserOrAccountIdentifier.IsMatch(content))
        {
            return true;
        }

        if (PotentialProperNoun.Matches(content)
            .Cast<Match>()
            .Any(match => !IsApprovedCapitalizedWord(content, match)))
        {
            return true;
        }

        return sensitiveTerms.Any(term => SensitiveTermExpression(term).IsMatch(content));
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
