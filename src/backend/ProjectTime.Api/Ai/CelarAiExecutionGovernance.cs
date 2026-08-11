using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public sealed record CelarAiExecutionAdapterDescriptor(
    string ToolCode,
    string DisplayName,
    string OwnerModule,
    string State,
    string StateReason,
    string AuthorizationPolicy,
    string InputSchema,
    string OutputSchema,
    int TimeoutSeconds,
    int MaximumRows,
    int MaximumResponseBytes,
    string FreshnessClass,
    string DataClassification,
    bool CitationRequired,
    bool DeterministicCalculationSupported,
    bool MutationAllowed,
    bool RecordScopeReauthorizationRequired,
    IReadOnlyList<string> Routes);

public sealed record CelarAiCapabilityManifestItem(
    string CapabilityCode,
    string DisplayName,
    string State,
    string StateReason,
    IReadOnlyList<string> ToolCodes,
    IReadOnlyList<string> OwningModules,
    bool PermissionScoped,
    bool PrivateDataCapable,
    bool MutationCapable);

public sealed record CelarAiSourceReceipt(
    string ReceiptType,
    string Reference,
    string SourceType,
    string SourceCode,
    string ModuleCode,
    string Method,
    string Path,
    DateTimeOffset ObservedAt,
    string Freshness,
    string EvidenceScope,
    string? ChecksumSha256);

public sealed record CelarAiCalculationReceipt(
    bool Required,
    bool Present,
    IReadOnlyList<string> ToolCodes,
    string FormulaPolicy,
    string UnitsPolicy,
    string DateRangePolicy,
    string TimeZonePolicy,
    string MissingValuePolicy,
    string RoundingPolicy);

public sealed record CelarAiAuthorizationReceipt(
    string IdentityPolicy,
    string RecordScopePolicy,
    string PermissionVersionPolicy,
    bool EffectiveIdentityRequired,
    bool RecordScopeWidened,
    DateTimeOffset EvaluatedAt);

public sealed record CelarAiEvidenceReceipt(
    string ReceiptVersion,
    string CorrelationId,
    Guid InquiryRunId,
    string IntentCode,
    string QuestionClass,
    string ToolSelectionPolicyVersion,
    IReadOnlyList<string> SelectedToolCodes,
    IReadOnlyList<CelarAiSourceReceipt> Sources,
    IReadOnlyList<CelarAiSourceReceipt> ToolExecutions,
    IReadOnlyList<int> ValidCitationIds,
    CelarAiCalculationReceipt Calculation,
    CelarAiAuthorizationReceipt Authorization,
    DateTimeOffset CreatedAt);

/// <summary>
/// One typed adapter registry shared by Ask Celar AI, Module 011, Module 064,
/// operational readiness, and the reliability workbench. Catalog membership is
/// not represented as runtime readiness.
/// </summary>
public static class CelarAiExecutionAdapterRegistry
{
    public const string ContractVersion = "celar-ai-execution-adapter-registry-v1-20260810";

    public static IReadOnlyList<CelarAiExecutionAdapterDescriptor> All()
    {
        var oracle = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        return CelarAiUniversalToolCatalog.Tools
            .Select(tool => Describe(tool, oracle.Active))
            .OrderBy(item => item.ToolCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static CelarAiExecutionAdapterDescriptor Describe(string toolCode) =>
        All().First(item => item.ToolCode.Equals(toolCode, StringComparison.OrdinalIgnoreCase));

    private static CelarAiExecutionAdapterDescriptor Describe(
        CelarAiUniversalToolCapability tool,
        bool oracleActive)
    {
        var state = tool.Availability switch
        {
            var value when value.Contains("requires_execution_adapter", StringComparison.OrdinalIgnoreCase)
                => "cataloged",
            var value when value.Contains("oracle", StringComparison.OrdinalIgnoreCase)
                => oracleActive ? "runtime_ready" : "adapter_ready",
            var value when value.Contains("protected_test", StringComparison.OrdinalIgnoreCase)
                => oracleActive ? "runtime_ready" : "adapter_ready",
            var value when value.Contains("only_when_module064", StringComparison.OrdinalIgnoreCase)
                => "adapter_ready",
            var value when value.Contains("available_existing", StringComparison.OrdinalIgnoreCase)
                => "adapter_ready",
            _ => "disabled"
        };
        var reason = state switch
        {
            "runtime_ready" => "The protected runtime boundary and typed adapter are available; every request still requires fresh authorization and health validation.",
            "adapter_ready" => "The typed adapter exists, but request-time configuration, permission, or runtime health must still be verified.",
            "cataloged" => "The capability is governed and visible, but its owning-module execution adapter is not yet active.",
            _ => "The capability is disabled by policy or lacks an approved runtime boundary."
        };
        return new CelarAiExecutionAdapterDescriptor(
            tool.Code,
            tool.DisplayName,
            tool.OwningModules.FirstOrDefault() ?? "011",
            state,
            reason,
            tool.AccessPolicy,
            $"typed:{tool.Code}:request-v1",
            $"typed:{tool.Code}:evidence-v1",
            TimeoutSeconds: tool.Domain == "documents_retrieval" ? 30 : 12,
            MaximumRows: tool.Deterministic ? 500 : 100,
            MaximumResponseBytes: tool.Domain == "documents_retrieval" ? 96_000 : 48_000,
            tool.FreshnessClass,
            tool.PrivateOnly ? "restricted_internal" : "public",
            tool.CitationRequired,
            tool.Deterministic,
            tool.MutationAllowed,
            RecordScopeReauthorizationRequired: tool.PrivateOnly,
            tool.Routes);
    }
}

/// <summary>
/// Dynamic, user-facing inventory of what Celar AI can do. The manifest reports
/// unavailable or limited states honestly instead of presenting every cataloged
/// capability as active.
/// </summary>
public static class CelarAiCapabilityManifest
{
    public const string ContractVersion = "celar-ai-capability-manifest-v1-20260810";

    public static IReadOnlyList<CelarAiCapabilityManifestItem> Build()
    {
        var adapters = CelarAiExecutionAdapterRegistry.All();
        return
        [
            Capability("internal_data_qa", "Authorized internal data questions", adapters,
                ["effective_identity", "project_portfolio", "project_assignments", "project_financial_truth"]),
            Capability("private_document_qa", "Private document questions and citations", adapters,
                ["project_documents", "private_retrieval", "document_extraction", "malware_scan"]),
            Capability("conversation_attachments", "Conversation-scoped private attachments", adapters,
                ["conversation_attachments", "malware_scan", "document_extraction"]),
            Capability("private_inference", "Private Celar AI inference", adapters,
                ["oracle_runtime_readiness", "provider_configuration"]),
            Capability("private_embeddings", "Private 768-dimensional embeddings", adapters,
                ["oracle_runtime_readiness", "private_retrieval"]),
            Capability("ocr", "Private OCR", adapters, ["ocr", "malware_scan"]),
            Capability("malware_scanning", "Private malware scanning", adapters, ["malware_scan"]),
            Capability("flowhive", "FlowHive cited planning", adapters, ["flowhive_plan", "project_documents"]),
            Capability("project_forge", "Project Forge cited estimating", adapters, ["project_forge", "project_documents"]),
            Capability("troubleshooting", "Read-only troubleshooting and diagnostics", adapters,
                ["system_diagnostics", "oracle_runtime_readiness", "live_api_inventory"]),
            Capability("defect_operations", "Guided Module 076 defect operations", adapters, ["defect_tracker"]),
            Capability("public_current", "Governed public-current questions", adapters, ["governed_public_information"]),
            Capability("provider_routing", "Module 064 provider routing", adapters, ["provider_configuration"]),
            Capability("health_automation", "Observed health and governed automatic defects", adapters,
                ["observability", "defect_tracker", "oracle_runtime_readiness"])
        ];
    }

    private static CelarAiCapabilityManifestItem Capability(
        string code,
        string name,
        IReadOnlyList<CelarAiExecutionAdapterDescriptor> adapters,
        string[] tools)
    {
        var selected = adapters.Where(item => tools.Contains(item.ToolCode, StringComparer.OrdinalIgnoreCase)).ToArray();
        var state = selected.Length == 0 || selected.All(item => item.State == "disabled")
            ? "disabled_by_policy"
            : selected.Any(item => item.State == "cataloged")
                ? "configuration_required"
                : selected.Any(item => item.State == "adapter_ready")
                    ? "available_with_limitations"
                    : "available";
        var reason = state switch
        {
            "available" => "All required typed adapters report runtime-ready; request-time permission and freshness checks still apply.",
            "available_with_limitations" => "The capability is implemented, but at least one dependency requires request-time runtime or configuration validation.",
            "configuration_required" => "At least one required owning-module execution adapter remains cataloged but inactive.",
            _ => "No approved runtime adapter is active for this capability."
        };
        var catalog = CelarAiUniversalToolCatalog.Tools
            .Where(tool => tools.Contains(tool.Code, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return new CelarAiCapabilityManifestItem(
            code,
            name,
            state,
            reason,
            tools,
            catalog.SelectMany(tool => tool.OwningModules)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PermissionScoped: catalog.Any(tool => tool.PrivateOnly),
            PrivateDataCapable: catalog.Any(tool => tool.PrivateOnly),
            MutationCapable: catalog.Any(tool => tool.MutationAllowed));
    }
}

public static class CelarAiEvidenceReceiptFactory
{
    public const string ReceiptVersion = "celar-ai-evidence-receipt-v1-20260810";

    public static CelarAiEvidenceReceipt Create(
        PulseAiSystemQuestionResult result,
        CelarAiUniversalAnswerPlan plan,
        IReadOnlyList<PulseAiSystemSourceEvidence> sources,
        IReadOnlyList<PulseAiSystemToolResult> tools,
        IReadOnlyList<int> validCitationIds,
        bool deterministicEvidence)
    {
        var sourceReceipts = sources.Select(source => new CelarAiSourceReceipt(
            "source",
            $"source:{source.SourceId}",
            source.SourceType,
            source.SourceCode,
            source.ModuleCode,
            source.Method,
            source.Path,
            source.ObservedAt,
            source.Freshness,
            source.EvidenceScope,
            ChecksumSha256: null)).ToArray();
        var toolReceipts = tools.Select(tool => new CelarAiSourceReceipt(
            "tool_execution",
            $"tool:{tool.ToolCode}:{tool.ObservedAt:O}",
            "governed_tool",
            tool.ToolCode,
            tool.ModuleCode,
            tool.Method,
            tool.Path,
            tool.ObservedAt,
            "request_time",
            $"status={tool.Status};statusCode={tool.StatusCode};bytes={tool.ResponseBytes};durationMs={tool.DurationMs:0.##}",
            ChecksumSha256: null)).ToArray();
        return new CelarAiEvidenceReceipt(
            ReceiptVersion,
            result.CorrelationId,
            result.InquiryRunId,
            plan.IntentCode,
            plan.QuestionClass.ToString(),
            plan.ToolSelectionPolicyVersion,
            plan.RequiredToolCodes,
            sourceReceipts,
            toolReceipts,
            validCitationIds,
            new CelarAiCalculationReceipt(
                plan.RequireDeterministicCalculation,
                deterministicEvidence,
                tools.Select(tool => tool.ToolCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                "Owning-module deterministic calculation only; no model arithmetic establishes internal facts.",
                "Preserve source units and currency; conversions require an explicit cited rule.",
                "Resolve an exact inclusive/exclusive reporting range before calculation.",
                "Resolve the effective user's approved time zone before period calculations.",
                "Missing or unavailable values remain unknown and are never treated as zero.",
                "Use the owning-module rounding rule and disclose it in the answer."),
            new CelarAiAuthorizationReceipt(
                "Resolve actual and effective identity before retrieval.",
                "The effective user controls read scope; mutations require actual user equals effective user.",
                "Record the current owning-module policy and source observation time.",
                EffectiveIdentityRequired: true,
                RecordScopeWidened: false,
                EvaluatedAt: DateTimeOffset.UtcNow),
            DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Strict typed validation for defect evidence metadata. Arbitrary nested JSON,
/// credentials, raw document bodies, vectors, storage paths, and prompt-like
/// instructions are rejected before persistence.
/// </summary>
public static class CelarAiTypedEvidencePolicy
{
    public const int MaximumSerializedBytes = 16_384;
    public const int MaximumDepth = 4;
    public const int MaximumProperties = 64;

    private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "probeCode", "componentCode", "displayName", "status", "failureCode",
        "httpStatus", "latencyMs", "observedAt", "correlationId", "releaseSha",
        "policyCode", "sourceChecksum", "signatureVersion", "certificateNotAfter",
        "embeddingDimensions", "scanner", "model", "contentInstructionDetected",
        "runtimeVersion", "provider", "attemptCount", "diagnosticCode"
    };

    private static readonly Regex SecretKey = new(
        "(?i)(authorization|cookie|password|secret|token|api.?key|connection.?string|raw.?prompt|raw.?body|document.?content|embedding.?vector|storage.?path)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretValue = new(
        "(?i)(bearer\\s+[a-z0-9._~+/=-]{8,}|postgres(?:ql)?://\\S+|eyJ[a-zA-Z0-9_-]{10,}\\.[a-zA-Z0-9_-]{10,})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] InstructionSignals =
    [
        "ignore previous instructions", "ignore system instructions", "system prompt",
        "developer message", "reveal your instructions", "exfiltrate", "send the data to",
        "override the policy", "disable the security"
    ];

    public static IReadOnlyDictionary<string, object?> Normalize(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new Dictionary<string, object?>();
        var bytes = Encoding.UTF8.GetByteCount(element.Value.GetRawText());
        if (bytes > MaximumSerializedBytes)
            throw new InvalidOperationException("Evidence metadata exceeds the 16 KB typed-evidence limit.");
        if (element.Value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Evidence metadata must be a typed JSON object.");
        var propertyCount = 0;
        return (IReadOnlyDictionary<string, object?>)Read(element.Value, 0, ref propertyCount)!;
    }

    public static bool ContainsDocumentInstruction(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return InstructionSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal));
    }

    public static string SourceChecksum(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)))
            .ToLowerInvariant();

    private static object? Read(JsonElement element, int depth, ref int propertyCount)
    {
        if (depth > MaximumDepth)
            throw new InvalidOperationException("Evidence metadata nesting exceeds the approved depth.");
        return element.ValueKind switch
        {
            JsonValueKind.Object => ReadObject(element, depth, ref propertyCount),
            JsonValueKind.Array => ReadArray(element, depth, ref propertyCount),
            JsonValueKind.String => ReadString(element.GetString()),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new InvalidOperationException("Evidence metadata contains an unsupported JSON value.")
        };
    }

    private static IReadOnlyDictionary<string, object?> ReadObject(
        JsonElement element,
        int depth,
        ref int propertyCount)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            propertyCount++;
            if (propertyCount > MaximumProperties)
                throw new InvalidOperationException("Evidence metadata contains too many properties.");
            if (!AllowedKeys.Contains(property.Name) || SecretKey.IsMatch(property.Name))
                throw new InvalidOperationException($"Evidence metadata property '{property.Name}' is not approved.");
            result[property.Name] = Read(property.Value, depth + 1, ref propertyCount);
        }
        return result;
    }

    private static IReadOnlyList<object?> ReadArray(
        JsonElement element,
        int depth,
        ref int propertyCount)
    {
        var values = element.EnumerateArray().Take(25)
            .Select(item => Read(item, depth + 1, ref propertyCount))
            .ToArray();
        if (element.GetArrayLength() > 25)
            throw new InvalidOperationException("Evidence metadata arrays are limited to 25 values.");
        return values;
    }

    private static string ReadString(string? value)
    {
        var clean = CelarAiOperationsPolicy.SanitizeOperationalDetail(value);
        if (SecretValue.IsMatch(clean))
            throw new InvalidOperationException("Evidence metadata contains a credential-like value.");
        if (clean.Length > 1_000)
            throw new InvalidOperationException("Evidence metadata string values are limited to 1,000 characters.");
        return ContainsDocumentInstruction(clean)
            ? "[CONTENT_INSTRUCTION_DETECTED_AND_NOT_EXECUTED]"
            : clean;
    }
}
