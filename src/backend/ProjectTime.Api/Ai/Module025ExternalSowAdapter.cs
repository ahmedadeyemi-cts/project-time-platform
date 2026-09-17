using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

// Constructed only after permission checks on server-loaded Module 025 evidence.
// No source passage, identity, attachment or commercial value is provider input.
internal sealed class Module025ExternalSowAdapter
{
    private static readonly (string Name, string Pattern)[] Technologies =
    [
        ("Cisco Unified Communications Manager", @"\b(?:Cisco Unified Communications Manager|CUCM)\b"),
        ("Cisco Unity Connection", @"\b(?:Cisco Unity Connection|Unity Connection)\b"),
        ("Cisco Emergency Responder", @"\bCisco Emergency Responder\b"),
        ("Cisco Expressway", @"\b(?:Cisco )?Expressway\b"),
        ("Webex Contact Center", @"\bWebex Contact Center\b"),
        ("Webex Calling", @"\bWebex Calling\b"),
        ("Microsoft Teams", @"\bMicrosoft Teams\b"),
        ("Microsoft 365", @"\b(?:Microsoft 365|M365)\b"),
        ("Microsoft Entra ID", @"\b(?:Microsoft Entra ID|Entra ID)\b"),
        ("Microsoft Azure SQL", @"\b(?:Microsoft )?Azure SQL\b"),
        ("Microsoft Fabric", @"\b(?:Microsoft )?Fabric\b"),
        ("VMware vSphere", @"\b(?:VMware vSphere|vSphere)\b"),
        ("VMware vCenter", @"\b(?:VMware vCenter|vCenter)\b"),
        ("Veeam Backup", @"\bVeeam\b"),
        ("Windows Server", @"\bWindows Server\b"),
        ("SQL Server", @"\bSQL Server\b"),
        ("Kubernetes", @"\bKubernetes\b"),
        ("Five9", @"\bFive9\b"),
        ("Palo Alto Networks firewall", @"\bPalo Alto(?: Networks)?\b"),
        ("Fortinet FortiGate", @"\b(?:Fortinet|FortiGate)\b")
    ];
    private static readonly string[] Operations = ["upgrade", "migration", "migrate", "implement", "deploy", "install", "configure", "integrate", "assessment", "assess"];
    private static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(200);
    private readonly CelarAiAuthoritativeScopeEvidence _evidence;
    private readonly string[] _technologies;
    private readonly string[] _operations;
    private readonly string[] _facts;
    internal PulseAiPrivateRagAnswer? AcceptedAnswer { get; private set; }
    internal string? ValidationCategory { get; private set; }
    internal string? ValidationField { get; private set; }
    private static readonly IReadOnlyDictionary<string, string> SchemaFields =
        new[] { typeof(PulseAiPrivateFlowHivePlan), typeof(PulseAiPrivateFlowHiveTask), typeof(PulseAiPrivateFlowHiveMilestone) }
            .SelectMany(type => type.GetProperties()).Select(property => property.Name).Distinct()
            .ToDictionary(name => name, name => JsonNamingPolicy.CamelCase.ConvertName(name), StringComparer.OrdinalIgnoreCase);
    private Module025ExternalSowAdapter(CelarAiAuthoritativeScopeEvidence evidence, string[] technologies, string[] operations, string[] facts)
    { _evidence = evidence; _technologies = technologies; _operations = operations; _facts = facts; }

    internal static bool PolicyEnabled => bool.TryParse(
        Environment.GetEnvironmentVariable("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION"), out var enabled) && enabled
        && bool.TryParse(Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED"),
            out var fallbackEnabled) && fallbackEnabled;

    internal static Module025ExternalSowAdapter? TryCreate(CelarAiAuthoritativeScopeEvidence evidence)
    {
        if (evidence.PhaseExecution is null || !Module025GenerationEngine.Phases.Contains(evidence.PhaseExecution.Phase)
            || evidence.ServiceOverview.Length > 30_000 || evidence.ServiceOverview.Length < 20) return null;
        var source = evidence.ServiceOverview;
        // A keyword capsule must not reverse exclusions or negated scope. Until
        // these constraints have a closed representation, retain the private path.
        if (Regex.IsMatch(source, @"\b(?:not|no|never|without|except|exclude[ds]?|excluding|only)\b|out[ -]of[ -]scope",
            RegexOptions.IgnoreCase, RegexBudget)) return null;
        var technologies = Technologies.Where(item => Regex.IsMatch(source, item.Pattern, RegexOptions.IgnoreCase, RegexBudget))
            .Select(item => item.Name).ToArray();
        var operations = Operations.Where(item => Regex.IsMatch(source, @"\b" + item + @"\b", RegexOptions.IgnoreCase, RegexBudget)).ToArray();
        if (technologies.Length == 0 || operations.Length == 0) return null;
        // Numeric facts are reconstructed from a closed grammar, never copied
        // from arbitrary prose or identifiers. Ambiguous associations are omitted.
        var facts = new List<string>();
        if (technologies.Length == 1)
        {
            var versions = Regex.Matches(source,
                @"\bfrom\s+(?:version\s+)?(\d{1,2}(?:\.\d{1,3}){0,2})\s+to\s+(?:version\s+)?(\d{1,2}(?:\.\d{1,3}){0,2})(?![\w.])",
                RegexOptions.IgnoreCase, RegexBudget);
            if (versions.Count == 1) facts.Add($"Requested version transition: {versions[0].Groups[1].Value} to {versions[0].Groups[2].Value}.");
            foreach (Match count in Regex.Matches(source, @"(?<![\w.])([1-9]\d{0,2}) (nodes|servers|sites|users)\b", RegexOptions.IgnoreCase, RegexBudget))
                facts.Add($"Requested quantity: {int.Parse(count.Groups[1].Value)} {count.Groups[2].Value.ToLowerInvariant()}.");
        }
        return new(evidence, technologies, operations, facts.Distinct().Take(12).ToArray());
    }

    internal ProjectPulseAiGenerationRequest? Prepare(PulseAiEscalationSanitizer sanitizer, out string diagnostic)
    {
        diagnostic = "sanitized_external_policy_disabled";
        if (!PolicyEnabled) return null;
        var capsule = "Requested technologies: " + string.Join(", ", _technologies)
            + ".\nRequested operations: " + string.Join(", ", _operations) + ".\n" + string.Join("\n", _facts);
        // A customer may itself have a technology's name. Fail closed rather
        // than accidentally transmitting that identity as a product hint.
        if (new[] { _evidence.CustomerName, _evidence.EngagementNumber }.Any(term =>
            term.Length >= 2 && capsule.Contains(term, StringComparison.OrdinalIgnoreCase)))
        { diagnostic = "module025_external_identity_collision"; return null; }
        diagnostic = "module025_closed_technical_capsule_ready";
        return new(CelarAiCapabilityCatalog.SowGsdPlanning,
            PulseAiPrivateRagService.Module025PhaseInstruction(_evidence.PhaseExecution!.Phase)
            + "\nOnly the closed technical specification below is available. Do not infer customer identities, documents, locations, environment topology, commercial values or completion. "
            + "Use generic customer and delivery-team role names. Keep unknown requirements as explicit open questions. Any implementation detail and effort estimate is a proposed plan requiring review.",
            capsule, Module025GenerationEngine.MaximumExternalOutputTokens, 0.1) { StructuredSowPhase = true };
    }

    internal bool Validate(string content, string provider, string correlationId,
        PulseAiEscalationSanitizer sanitizer, out string diagnostic)
    {
        AcceptedAnswer = null;
        ValidationCategory = null;
        ValidationField = null;
        diagnostic = "module025_external_invalid_json";
        if (content.Length > Module025GenerationEngine.MaximumPhaseCharacters)
        { diagnostic = "module025_phase_size_limit_exceeded"; ValidationCategory = "response_size_limit"; return false; }
        try
        {
            using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 32 });
            // Inspect every string, including unknown properties, before parsing
            // the plan. Inspect values, not JSON field names. Only approved public
            // product terminology is removed from the strict identity check.
            foreach (var (value, field) in Strings(json.RootElement))
            {
                if (new[] { _evidence.CustomerName, _evidence.EngagementNumber }.Any(term =>
                    term.Length >= 2 && value.Contains(term, StringComparison.OrdinalIgnoreCase)))
                { diagnostic = "external_output_identity_validation_failed"; ValidationCategory = "explicit_sensitive_terms"; ValidationField = field; return false; }
                // Inspect explicit identity labels before normalizing generic
                // schema roles below; "Engineer: Jane Doe" is not a role name.
                if (Regex.IsMatch(value, @"\b(?:customer|client|tenant|company|owner|engineer|manager|contact|user|project)(?: name)?[ \t]*[:=][ \t]*[\p{L}\p{N}]",
                    RegexOptions.IgnoreCase, RegexBudget))
                { diagnostic = "external_output_identity_validation_failed"; ValidationCategory = "named_people_and_customers"; ValidationField = field; return false; }
                var inspect = value;
                foreach (var technology in Technologies.Where(item => _technologies.Contains(item.Name)))
                {
                    inspect = Regex.Replace(inspect, technology.Pattern, "technology", RegexOptions.IgnoreCase, RegexBudget);
                    inspect = inspect.Replace(technology.Name, "technology", StringComparison.OrdinalIgnoreCase);
                }
                inspect = inspect.Replace("US Signal", "delivery team", StringComparison.Ordinal);
                // These are fixed role categories in the task schema, not a
                // named person. Known identities and explicit identity labels
                // were rejected before this normalization.
                inspect = Regex.Replace(inspect, @"\b(customer|client|engineer|manager|owner|contact|user|project)\b",
                    "party", RegexOptions.IgnoreCase, RegexBudget);
                if (!sanitizer.IsClosedTechnicalProposalOutputSafe(inspect, [_evidence.CustomerName, _evidence.EngagementNumber], out diagnostic, out var category))
                { ValidationCategory = category; ValidationField = field; return false; }
            }
            AcceptedAnswer = PulseAiPrivateRagService.Module025ExternalAnswer(content, _evidence, provider, correlationId);
            diagnostic = "module025_external_phase_validated";
            return true;
        }
        catch (JsonException) { ValidationCategory = "phase_contract_or_json"; diagnostic = "module025_external_phase_contract_invalid"; return false; }
    }

    internal ProjectPulseAiProviderResult ValidateResult(ProjectPulseAiProviderResult result,
        string correlationId, PulseAiEscalationSanitizer sanitizer)
    {
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Content)) return result;
        if (Validate(result.Content, result.Provider, correlationId, sanitizer, out var diagnostic)) return result;
        return result with { Outcome = ProjectPulseAiOutcomes.Failure, Content = null, Code = diagnostic,
            SowDiagnostics = (result.SowDiagnostics ?? new()) with {
                OutputTextCharacters = result.Content.Length,
                OutputValidationCategory = ValidationCategory, OutputValidationField = ValidationField } };
    }

    // Paths are reconstructed only from schema names and numeric array indices.
    // A provider-supplied property name can itself contain private data.
    private static IEnumerable<(string Value, string Field)> Strings(JsonElement value, string field = "$")
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text) yield return (text, field);
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                foreach (var entry in Strings(item, Path(field, $"[{index}]"))) yield return entry;
                index++;
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var item in value.EnumerateObject())
                foreach (var entry in Strings(item.Value, Path(field,
                    "." + (SchemaFields.TryGetValue(item.Name, out var name) ? name : "unknown_field")))) yield return entry;
    }
    private static string Path(string prefix, string suffix) => prefix.Length + suffix.Length <= 240 ? prefix + suffix : "$.nested_field";
}
