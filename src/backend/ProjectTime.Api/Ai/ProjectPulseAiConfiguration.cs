using System.Security.Cryptography;
using System.Text;

namespace ProjectTime.Api.Ai;

public sealed class ProjectPulseAiConfiguration
{
    private static readonly string[] DefaultClaudeModels = ["claude-sonnet-5", "claude-opus-4-8", "claude-fable-5"];
    private static readonly string[] DefaultOpenAiModels = ["gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5", "gpt-5.4"];
    private readonly object _providerLock = new();
    private ProjectPulseAiProviderConfiguration _deepSeek;
    private ProjectPulseAiProviderConfiguration _claude;
    private ProjectPulseAiProviderConfiguration _openAi;
    private readonly Dictionary<string, ProjectPulseAiProviderConfiguration> _optional = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ValidModes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "priority_failover",
            "claude_only",
            "openai_only",
            "local_only"
        };

    public ProjectPulseAiConfiguration()
    {
        EnvironmentName = Value("PROJECTPULSE_ENVIRONMENT", "unspecified");
        Mode = NormalizedMode(Value("PROJECTPULSE_AI_MODE", "priority_failover"));
        RequestTimeoutSeconds = Integer("PROJECTPULSE_AI_TIMEOUT_SECONDS", 30, 5, 180);
        RetryCount = Integer("PROJECTPULSE_AI_RETRY_COUNT", 2, 0, 5);
        MaxOutputTokens = Integer("PROJECTPULSE_AI_MAX_OUTPUT_TOKENS", 800, 64, 8192);
        HealthIntervalSeconds = Integer("PROJECTPULSE_AI_HEALTH_INTERVAL_SECONDS", 120, 30, 3600);
        FailureThreshold = Integer("PROJECTPULSE_AI_FAILURE_THRESHOLD", 3, 1, 10);
        CircuitBreakSeconds = Integer("PROJECTPULSE_AI_CIRCUIT_BREAK_SECONDS", 180, 30, 3600);

        _deepSeek = BuildDeepSeek();
        _claude = BuildClaude();
        _openAi = BuildOpenAi();
        _optional[ProjectPulseAiProviders.Gemini] = BuildOptional(ProjectPulseAiProviders.Gemini, "Gemini", "GEMINI", "gemini-3.8-flash", "https://generativelanguage.googleapis.com/v1beta/openai");
        _optional[ProjectPulseAiProviders.Copilot] = BuildOptional(ProjectPulseAiProviders.Copilot, "Microsoft Copilot Studio", "COPILOT_STUDIO", "published-agent", "https://directline.botframework.com/v3/directline");
        FeatureRoutes = ProjectPulseAiFeatures.All.ToDictionary(
            feature => feature,
            ResolveFeatureRoute,
            StringComparer.OrdinalIgnoreCase);
    }

    public string EnvironmentName { get; }
    public string Mode { get; }
    public int RequestTimeoutSeconds { get; }
    public int RetryCount { get; }
    public int MaxOutputTokens { get; }
    public int HealthIntervalSeconds { get; }
    public int FailureThreshold { get; }
    public int CircuitBreakSeconds { get; }
    public ProjectPulseAiProviderConfiguration DeepSeek { get { lock (_providerLock) return _deepSeek; } }
    public ProjectPulseAiProviderConfiguration Claude { get { lock (_providerLock) return _claude; } }
    public ProjectPulseAiProviderConfiguration OpenAi { get { lock (_providerLock) return _openAi; } }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> FeatureRoutes { get; }

    public ProjectPulseAiProviderConfiguration Provider(string code) =>
        code is ProjectPulseAiProviders.Gemini or ProjectPulseAiProviders.Copilot ? OptionalProvider(code) :
        string.Equals(code, ProjectPulseAiProviders.DeepSeek, StringComparison.OrdinalIgnoreCase) ? DeepSeek :
        string.Equals(code, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)
            ? Claude
            : string.Equals(code, ProjectPulseAiProviders.OpenAi, StringComparison.OrdinalIgnoreCase)
                ? OpenAi
                : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown remote AI provider.");

    public void ApplyStoredSecret(string providerCode, string apiKey, string version, DateTimeOffset rotatedAt)
    {
        lock (_providerLock)
        {
            var current = _optional.TryGetValue(providerCode, out var optional) ? optional : string.Equals(providerCode, ProjectPulseAiProviders.DeepSeek, StringComparison.OrdinalIgnoreCase) ? _deepSeek :
                string.Equals(providerCode, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)
                ? _claude
                : string.Equals(providerCode, ProjectPulseAiProviders.OpenAi, StringComparison.OrdinalIgnoreCase)
                    ? _openAi
                    : throw new ArgumentOutOfRangeException(nameof(providerCode));
            var updated = current with
            {
                ApiKey = apiKey,
                Secret = new ProjectPulseAiSecretMetadata(true, "encrypted_database", version, rotatedAt, null, Fingerprint(apiKey))
            };
            if (_optional.ContainsKey(providerCode)) _optional[providerCode] = updated;
            else if (string.Equals(providerCode, ProjectPulseAiProviders.DeepSeek, StringComparison.OrdinalIgnoreCase)) _deepSeek = updated;
            else if (string.Equals(providerCode, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)) _claude = updated;
            else _openAi = updated;
        }
    }

    public void ApplyStoredModel(string providerCode, string model)
    {
        lock (_providerLock)
        {
            var current = _optional.TryGetValue(providerCode, out var optional) ? optional : string.Equals(providerCode, ProjectPulseAiProviders.DeepSeek, StringComparison.OrdinalIgnoreCase) ? _deepSeek :
                string.Equals(providerCode, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)
                ? _claude
                : string.Equals(providerCode, ProjectPulseAiProviders.OpenAi, StringComparison.OrdinalIgnoreCase)
                    ? _openAi
                    : throw new ArgumentOutOfRangeException(nameof(providerCode));
            // Persisted Gemini choices have already passed account discovery and
            // an isolated inference test in Module 064. Preserve them on restart;
            // the bootstrap default is not a permanent model catalogue.
            var isGemini = string.Equals(providerCode, ProjectPulseAiProviders.Gemini, StringComparison.OrdinalIgnoreCase);
            var discoveredGemini = isGemini && GeminiModelPermitted(model);
            if (isGemini ? !discoveredGemini : !current.ApprovedModels.Contains(model, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("The selected model is not in the provider allowlist.");
            var updated = current with
            {
                Model = model,
                ApprovedModels = discoveredGemini
                    ? current.ApprovedModels.Append(model).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : current.ApprovedModels
            };
            if (_optional.ContainsKey(providerCode)) _optional[providerCode] = updated;
            else if (string.Equals(providerCode, ProjectPulseAiProviders.DeepSeek, StringComparison.OrdinalIgnoreCase)) _deepSeek = updated;
            else if (string.Equals(providerCode, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)) _claude = updated;
            else _openAi = updated;
        }
    }

    public static bool GeminiModelPermitted(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 150
            || !System.Text.RegularExpressions.Regex.IsMatch(model, @"\Agemini-[a-z0-9][a-z0-9._-]*\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return false;
        var configuredAllowlist = Environment.GetEnvironmentVariable("PROJECTPULSE_GEMINI_APPROVED_MODELS");
        return string.IsNullOrWhiteSpace(configuredAllowlist)
            || configuredAllowlist.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(model, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryApplyVerifiedModel(ProjectPulseAiProviderConfiguration expected, string model)
    {
        lock (_providerLock)
        {
            var current = Provider(expected.Code);
            if (current.ApiKey != expected.ApiKey || current.Secret.Version != expected.Secret.Version
                || current.Secret.Source != expected.Secret.Source || current.Model != expected.Model
                || current.Enabled != expected.Enabled || current.Endpoint != expected.Endpoint)
                return false;
            ApplyStoredModel(expected.Code, model);
            return true;
        }
    }

    public void ApplyStoredEnabled(string providerCode, bool enabled)
    {
        lock (_providerLock)
        {
            var current = _optional.TryGetValue(providerCode, out var optional) ? optional : string.Equals(providerCode, ProjectPulseAiProviders.DeepSeek, StringComparison.OrdinalIgnoreCase) ? _deepSeek :
                string.Equals(providerCode, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)
                ? _claude
                : string.Equals(providerCode, ProjectPulseAiProviders.OpenAi, StringComparison.OrdinalIgnoreCase)
                    ? _openAi
                    : throw new ArgumentOutOfRangeException(nameof(providerCode));
            var updated = current with { Enabled = enabled };
            if (_optional.ContainsKey(providerCode)) _optional[providerCode] = updated;
            else if (string.Equals(providerCode, ProjectPulseAiProviders.DeepSeek, StringComparison.OrdinalIgnoreCase)) _deepSeek = updated;
            else if (string.Equals(providerCode, ProjectPulseAiProviders.Claude, StringComparison.OrdinalIgnoreCase)) _claude = updated;
            else _openAi = updated;
        }
    }

    public IReadOnlyList<string> RouteFor(string feature)
    {
        if (FeatureRoutes.TryGetValue(feature, out var route))
        {
            return route;
        }

        return DefaultRoute();
    }

    public object ToSanitizedResponse()
    {
        return new
        {
            module = "064",
            moduleName = "AI Provider Configuration Center",
            environment = EnvironmentName,
            mode = Mode,
            execution = new
            {
                strategy = "sequential_priority_no_duplicate",
                safetyRefusalFailover = false,
                localFallbackRequired = true,
                requestTimeoutSeconds = RequestTimeoutSeconds,
                retryCount = RetryCount,
                maxOutputTokens = MaxOutputTokens,
                healthIntervalSeconds = HealthIntervalSeconds,
                failureThreshold = FailureThreshold,
                circuitBreakSeconds = CircuitBreakSeconds
            },
            providers = new[]
            {
                DeepSeek.ToSanitizedResponse(),
                Claude.ToSanitizedResponse(),
                OpenAi.ToSanitizedResponse(),
                Provider(ProjectPulseAiProviders.Gemini).ToSanitizedResponse(),
                Provider(ProjectPulseAiProviders.Copilot).ToSanitizedResponse(),
                new
                {
                    code = ProjectPulseAiProviders.Local,
                    displayName = "Governed local template",
                    enabled = true,
                    configured = true,
                    model = "deterministic_template",
                    endpoint = (string?)null,
                    apiVersion = (string?)null,
                    approvedModels = new[] { "deterministic_template" },
                    organization = (string?)null,
                    project = (string?)null,
                    secret = (object?)null
                }
            },
            featureRoutes = FeatureRoutes.Select(pair => new
            {
                feature = pair.Key,
                providers = pair.Value,
                duplicateRequests = false
            }),
            secretLifecycle = new
            {
                apiKeysReturned = false,
                mutationEnabled = true,
                activationEnabled = true,
                rollbackEnabled = false,
                reason = "Administrators can replace provider keys through a write-only encrypted store. Secret values are never returned."
            }
        };
    }

    private ProjectPulseAiProviderConfiguration OptionalProvider(string code)
    {
        lock (_providerLock) return _optional[code];
    }

    private static ProjectPulseAiProviderConfiguration BuildOptional(string code, string label, string prefix, string defaultModel, string endpoint)
    {
        var key = FirstValue($"PROJECTPULSE_{prefix}_API_KEY");
        var model = Value($"PROJECTPULSE_{prefix}_MODEL", defaultModel);
        return new(code, label, Boolean($"PROJECTPULSE_AI_{prefix}_ENABLED", false), key, model, endpoint,
            code == ProjectPulseAiProviders.Gemini ? "chat-completions-v1" : "directline-v3",
            ApprovedModels($"PROJECTPULSE_{prefix}_APPROVED_MODELS", model, [defaultModel]), null, null, SecretMetadata(prefix, key));
    }

    private ProjectPulseAiProviderConfiguration BuildDeepSeek()
    {
        // Normal deployments hydrate Module 064's encrypted store. Immutable releases
        // use the same protected, version-pinned environment injection as other providers.
        var key = FirstValue("PROJECTPULSE_DEEPSEEK_API_KEY");
        return new ProjectPulseAiProviderConfiguration(
            ProjectPulseAiProviders.DeepSeek, "DeepSeek v4", Boolean("PROJECTPULSE_AI_DEEPSEEK_ENABLED", !ProjectPulseAiReleaseRuntimePolicy.IsReleaseScoped), key,
            ProjectPulseDeepSeekProvider.Model, ProjectPulseDeepSeekProvider.Endpoint,
            "chat-completions-v1", [ProjectPulseDeepSeekProvider.Model],
            null, null, SecretMetadata("DEEPSEEK", key));
    }

    private ProjectPulseAiProviderConfiguration BuildClaude()
    {
        var key = FirstValue("PROJECTPULSE_CLAUDE_API_KEY", "ANTHROPIC_API_KEY");
        var enabled = Boolean("PROJECTPULSE_AI_CLAUDE_ENABLED", true);
        var model = Value("PROJECTPULSE_CLAUDE_MODEL", "claude-sonnet-5");

        return new ProjectPulseAiProviderConfiguration(
            ProjectPulseAiProviders.Claude,
            "Claude",
            enabled,
            key,
            model,
            Value("PROJECTPULSE_CLAUDE_ENDPOINT", "https://api.anthropic.com/v1").TrimEnd('/'),
            Value("PROJECTPULSE_CLAUDE_API_VERSION", "2023-06-01"),
            ApprovedModels("PROJECTPULSE_CLAUDE_APPROVED_MODELS", model, DefaultClaudeModels),
            null,
            null,
            SecretMetadata("CLAUDE", key));
    }

    private ProjectPulseAiProviderConfiguration BuildOpenAi()
    {
        var key = FirstValue("PROJECTPULSE_OPENAI_API_KEY", "OPENAI_API_KEY");
        var enabled = Boolean("PROJECTPULSE_AI_OPENAI_ENABLED", true);
        var model = Value("PROJECTPULSE_OPENAI_MODEL", "gpt-5.6-sol");

        return new ProjectPulseAiProviderConfiguration(
            ProjectPulseAiProviders.OpenAi,
            "OpenAI",
            enabled,
            key,
            model,
            Value("PROJECTPULSE_OPENAI_ENDPOINT", "https://api.openai.com/v1").TrimEnd('/'),
            Value("PROJECTPULSE_OPENAI_API_VERSION", "responses-v1"),
            ApprovedModels("PROJECTPULSE_OPENAI_APPROVED_MODELS", model, DefaultOpenAiModels),
            Optional("PROJECTPULSE_OPENAI_ORGANIZATION"),
            Optional("PROJECTPULSE_OPENAI_PROJECT"),
            SecretMetadata("OPENAI", key));
    }

    private IReadOnlyList<string> ResolveFeatureRoute(string feature)
    {
        var variable = "PROJECTPULSE_AI_ROUTE_" + feature.ToUpperInvariant();
        var configured = Csv(variable, []);
        var requested = configured.Count > 0 ? configured : DefaultRoute();
        var valid = new HashSet<string>(
            [ProjectPulseAiProviders.DeepSeek, ProjectPulseAiProviders.Claude, ProjectPulseAiProviders.OpenAi, ProjectPulseAiProviders.Local],
            StringComparer.OrdinalIgnoreCase);

        var route = requested
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(valid.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!route.Contains(ProjectPulseAiProviders.Local, StringComparer.OrdinalIgnoreCase))
        {
            route.Add(ProjectPulseAiProviders.Local);
        }

        return route.Count > 0 ? route : [ProjectPulseAiProviders.Local];
    }

    private IReadOnlyList<string> DefaultRoute() => Mode switch
    {
        "claude_only" => [ProjectPulseAiProviders.Claude, ProjectPulseAiProviders.Local],
        "openai_only" => [ProjectPulseAiProviders.OpenAi, ProjectPulseAiProviders.Local],
        "local_only" => [ProjectPulseAiProviders.Local],
        _ => [ProjectPulseAiProviders.DeepSeek, ProjectPulseAiProviders.Claude, ProjectPulseAiProviders.OpenAi, ProjectPulseAiProviders.Local]
    };

    private static ProjectPulseAiSecretMetadata SecretMetadata(string prefix, string? key)
    {
        return new ProjectPulseAiSecretMetadata(
            Configured: !string.IsNullOrWhiteSpace(key),
            Source: Value($"PROJECTPULSE_{prefix}_SECRET_SOURCE", "environment_reference"),
            Version: Optional($"PROJECTPULSE_{prefix}_SECRET_VERSION"),
            RotatedAt: DateTimeValue($"PROJECTPULSE_{prefix}_SECRET_ROTATED_AT"),
            ExpiresAt: DateTimeValue($"PROJECTPULSE_{prefix}_SECRET_EXPIRES_AT"),
            Fingerprint: Fingerprint(key));
    }

    private static string NormalizedMode(string value) =>
        ValidModes.Contains(value) ? value.ToLowerInvariant() : "priority_failover";

    private static string? FirstValue(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return null;
    }

    private static string Value(string name, string fallback) =>
        Optional(name) ?? fallback;

    private static string? Optional(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static int Integer(string name, int fallback, int minimum, int maximum) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static IReadOnlyList<string> Csv(string name, IReadOnlyList<string> fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ApprovedModels(string name, string activeModel, IReadOnlyList<string> defaults) =>
        Csv(name, defaults).Append(activeModel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static DateTimeOffset? DateTimeValue(string name) =>
        DateTimeOffset.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : null;

    private static string? Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}

public sealed record ProjectPulseAiProviderConfiguration(
    string Code,
    string DisplayName,
    bool Enabled,
    string? ApiKey,
    string Model,
    string Endpoint,
    string ApiVersion,
    IReadOnlyList<string> ApprovedModels,
    string? Organization,
    string? Project,
    ProjectPulseAiSecretMetadata Secret)
{
    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey);

    public object ToSanitizedResponse() => new
    {
        code = Code,
        displayName = DisplayName,
        enabled = Enabled,
        configured = Configured,
        model = Model,
        endpoint = Endpoint,
        apiVersion = ApiVersion,
        approvedModels = ApprovedModels,
        organization = Organization,
        project = Project,
        secret = new
        {
            configured = Secret.Configured,
            source = Secret.Source,
            version = Secret.Version,
            rotatedAt = Secret.RotatedAt,
            expiresAt = Secret.ExpiresAt,
            fingerprint = Secret.Fingerprint,
            valueReturned = false
        }
    };
}

public sealed record ProjectPulseAiSecretMetadata(
    bool Configured,
    string Source,
    string? Version,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? ExpiresAt,
    string? Fingerprint);
