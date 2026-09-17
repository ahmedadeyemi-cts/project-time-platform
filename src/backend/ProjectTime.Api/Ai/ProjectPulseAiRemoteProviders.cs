using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

public sealed class ProjectPulseClaudeProvider : IProjectPulseAiProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProjectPulseAiConfiguration _configuration;
    private ProjectPulseAiProviderConfiguration Provider => _configuration.Claude;

    public ProjectPulseClaudeProvider(
        IHttpClientFactory httpClientFactory,
        ProjectPulseAiConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public string Code => ProjectPulseAiProviders.Claude;

    public async Task<ProjectPulseAiProviderResult> GenerateAsync(
        ProjectPulseAiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsModelApproved()) return ModelNotApproved();

        // Claude Sonnet 5 accepts only default sampling behavior. Omitting
        // temperature also keeps the adapter compatible with models that do not
        // expose sampling controls.
        var payload = JsonSerializer.Serialize(new
        {
            model = Provider.Model,
            max_tokens = request.StructuredSowPhase
                ? Math.Min(request.MaxOutputTokens, Module025GenerationEngine.MaximumOutputTokens)
                : Math.Min(request.MaxOutputTokens, _configuration.MaxOutputTokens),
            system = request.SystemPrompt,
            messages = new[] { new { role = "user", content = request.UserPrompt } }
        });

        var response = await ProjectPulseAiHttp.SendWithRetryAsync(
            _httpClientFactory,
            _configuration,
            () => CreateRequest(HttpMethod.Post, "/messages", payload),
            cancellationToken, request.StructuredSowPhase);

        if (response.ExceptionCode is not null)
        {
            return Unavailable(response.ExceptionCode, null, null);
        }

        if (response.Response is null)
        {
            return Unavailable("claude_no_response", null, null);
        }

        using var httpResponse = response.Response;
        var requestId = ProjectPulseAiHttp.Header(httpResponse, "request-id");
        var rateLimits = ProjectPulseAiHttp.ClaudeRateLimits(httpResponse);
        var body = await ProjectPulseAiHttp.ReadBodyAsync(httpResponse, cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var code = ProjectPulseAiHttp.ErrorCode(body, "claude_http_error");
            if (ProjectPulseAiHttp.IsSafetyRefusal(httpResponse.StatusCode, body))
            {
                return Refusal(code, requestId, (int)httpResponse.StatusCode, rateLimits);
            }

            return Unavailable(code, requestId, (int)httpResponse.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var usage = ProjectPulseAiHttp.ClaudeUsage(root);
            var diagnostics = request.StructuredSowPhase ? Module025ProviderDiagnostics.Claude(root) : null;
            var stopReason = ProjectPulseAiHttp.String(root, "stop_reason");
            // Refusal is terminal even when a response is also truncated.
            var containsRefusal = root.TryGetProperty("content", out var refusalContent)
                && refusalContent.ValueKind == JsonValueKind.Array
                && refusalContent.EnumerateArray().Any(item =>
                    string.Equals(ProjectPulseAiHttp.String(item, "type"), "refusal", StringComparison.OrdinalIgnoreCase));
            if (containsRefusal || string.Equals(stopReason, "refusal", StringComparison.OrdinalIgnoreCase))
            {
                return new ProjectPulseAiProviderResult(
                    Code,
                    ProjectPulseAiOutcomes.Refusal,
                    null,
                    "claude_safety_refusal",
                    "Claude declined this request under its safety controls.",
                    requestId,
                    usage,
                    (int)httpResponse.StatusCode,
                    rateLimits) { SowDiagnostics = diagnostics };
            }

            if (request.StructuredSowPhase && stopReason == "max_tokens")
                return new(Code, ProjectPulseAiOutcomes.Failure, null, "structured_sow_output_truncated", null, requestId, usage, (int)httpResponse.StatusCode) { SowDiagnostics = diagnostics };

            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    var type = ProjectPulseAiHttp.String(item, "type");
                    if (string.Equals(type, "refusal", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ProjectPulseAiProviderResult(
                            Code,
                            ProjectPulseAiOutcomes.Refusal,
                            null,
                            "claude_safety_refusal",
                            "Claude declined this request under its safety controls.",
                            requestId,
                            usage,
                            (int)httpResponse.StatusCode,
                            rateLimits) { SowDiagnostics = diagnostics };
                    }

                    if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = ProjectPulseAiHttp.String(item, "text")?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return new ProjectPulseAiProviderResult(
                                Code,
                                ProjectPulseAiOutcomes.Success,
                                text,
                                null,
                                null,
                                requestId,
                                usage,
                                (int)httpResponse.StatusCode,
                                rateLimits) { SowDiagnostics = diagnostics };
                        }
                    }
                }
            }

            return new ProjectPulseAiProviderResult(
                Code,
                ProjectPulseAiOutcomes.Failure,
                null,
                "claude_empty_response",
                "Claude returned no usable text.",
                requestId,
                usage,
                (int)httpResponse.StatusCode) { SowDiagnostics = diagnostics };
        }
        catch (JsonException)
        {
            return new ProjectPulseAiProviderResult(
                Code,
                ProjectPulseAiOutcomes.Failure,
                null,
                "claude_invalid_response",
                "Claude returned an invalid response.",
                requestId,
                null,
                (int)httpResponse.StatusCode);
        }
    }

    public async Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        if (!IsModelApproved())
        {
            return new ProjectPulseAiProbeResult(Code, false, "model_not_approved", "The configured Claude model is not approved.", null, null);
        }

        var result = await GenerateAsync(ProjectPulseAiHttp.ProbeRequest, cancellationToken);
        return ProjectPulseAiHttp.GenerationProbe(result, "Claude");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? payload)
    {
        var provider = Provider;
        var request = new HttpRequestMessage(method, provider.Endpoint + path);
        request.Headers.Add("x-api-key", provider.ApiKey);
        request.Headers.Add("anthropic-version", provider.ApiVersion);
        if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return request;
    }

    private bool IsModelApproved() =>
        Provider.ApprovedModels.Contains(Provider.Model, StringComparer.OrdinalIgnoreCase);

    private ProjectPulseAiProviderResult ModelNotApproved() => new(
        Code,
        ProjectPulseAiOutcomes.Unavailable,
        null,
        "model_not_approved",
        "The configured Claude model is not approved.",
        null,
        null,
        null);

    private ProjectPulseAiProviderResult Refusal(
        string code,
        string? requestId,
        int? status,
        ProjectPulseAiRateLimits? rateLimits) => new(
        Code,
        ProjectPulseAiOutcomes.Refusal,
        null,
        code,
        "Claude declined this request under its safety controls.",
        requestId,
        null,
        status,
        rateLimits);

    private ProjectPulseAiProviderResult Unavailable(string code, string? requestId, int? status) => new(
        Code,
        ProjectPulseAiOutcomes.Unavailable,
        null,
        code,
        "Claude is unavailable.",
        requestId,
        null,
        status);
}

public sealed class ProjectPulseOpenAiProvider : IProjectPulseAiProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProjectPulseAiConfiguration _configuration;
    private ProjectPulseAiProviderConfiguration Provider => _configuration.OpenAi;

    public ProjectPulseOpenAiProvider(
        IHttpClientFactory httpClientFactory,
        ProjectPulseAiConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public string Code => ProjectPulseAiProviders.OpenAi;

    public async Task<ProjectPulseAiProviderResult> GenerateAsync(
        ProjectPulseAiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsModelApproved()) return ModelNotApproved();

        var payload = JsonSerializer.Serialize(new
        {
            model = Provider.Model,
            instructions = request.SystemPrompt,
            input = request.UserPrompt,
            store = false,
            max_output_tokens = request.StructuredSowPhase
                ? Math.Min(request.MaxOutputTokens, Module025GenerationEngine.MaximumOutputTokens)
                : Math.Min(request.MaxOutputTokens, _configuration.MaxOutputTokens)
        });

        var response = await ProjectPulseAiHttp.SendWithRetryAsync(
            _httpClientFactory,
            _configuration,
            () => CreateRequest(HttpMethod.Post, "/responses", payload),
            cancellationToken, request.StructuredSowPhase);

        if (response.ExceptionCode is not null)
        {
            return Unavailable(response.ExceptionCode, null, null);
        }

        if (response.Response is null)
        {
            return Unavailable("openai_no_response", null, null);
        }

        using var httpResponse = response.Response;
        var requestId = ProjectPulseAiHttp.Header(httpResponse, "x-request-id");
        var rateLimits = ProjectPulseAiHttp.OpenAiRateLimits(httpResponse);
        var body = await ProjectPulseAiHttp.ReadBodyAsync(httpResponse, cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var code = ProjectPulseAiHttp.ErrorCode(body, "openai_http_error");
            if (ProjectPulseAiHttp.IsSafetyRefusal(httpResponse.StatusCode, body))
            {
                return Refusal(code, requestId, (int)httpResponse.StatusCode, rateLimits);
            }

            return Unavailable(code, requestId, (int)httpResponse.StatusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var usage = ProjectPulseAiHttp.OpenAiUsage(root);
            var diagnostics = request.StructuredSowPhase ? Module025ProviderDiagnostics.OpenAi(root) : null;
            // Inspect all content before selecting text or considering fallback.
            if (root.TryGetProperty("output", out var refusalOutput) && refusalOutput.ValueKind == JsonValueKind.Array
                && refusalOutput.EnumerateArray().Any(item => item.TryGetProperty("content", out var parts)
                    && parts.ValueKind == JsonValueKind.Array && parts.EnumerateArray().Any(part =>
                        string.Equals(ProjectPulseAiHttp.String(part, "type"), "refusal", StringComparison.OrdinalIgnoreCase))))
                return new(Code, ProjectPulseAiOutcomes.Refusal, null, "openai_safety_refusal",
                    "OpenAI declined this request under its safety controls.", requestId, usage, (int)httpResponse.StatusCode, rateLimits) { SowDiagnostics = diagnostics };
            if (request.StructuredSowPhase && ProjectPulseAiHttp.String(root, "status") != "completed")
                return new(Code, ProjectPulseAiOutcomes.Failure, null, "structured_sow_response_incomplete", null, requestId, usage, (int)httpResponse.StatusCode) { SowDiagnostics = diagnostics };

            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;

                    foreach (var part in content.EnumerateArray())
                    {
                        var type = ProjectPulseAiHttp.String(part, "type");
                        if (string.Equals(type, "refusal", StringComparison.OrdinalIgnoreCase))
                        {
                            return new ProjectPulseAiProviderResult(
                                Code,
                                ProjectPulseAiOutcomes.Refusal,
                                null,
                                "openai_safety_refusal",
                                "OpenAI declined this request under its safety controls.",
                                requestId,
                                usage,
                                (int)httpResponse.StatusCode,
                                rateLimits) { SowDiagnostics = diagnostics };
                        }

                        if (string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase))
                        {
                            var text = ProjectPulseAiHttp.String(part, "text")?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                return new ProjectPulseAiProviderResult(
                                    Code,
                                    ProjectPulseAiOutcomes.Success,
                                    text,
                                    null,
                                    null,
                                    requestId,
                                    usage,
                                    (int)httpResponse.StatusCode,
                                    rateLimits) { SowDiagnostics = diagnostics };
                            }
                        }
                    }
                }
            }

            return new ProjectPulseAiProviderResult(
                Code,
                ProjectPulseAiOutcomes.Failure,
                null,
                "openai_empty_response",
                "OpenAI returned no usable text.",
                requestId,
                usage,
                (int)httpResponse.StatusCode) { SowDiagnostics = diagnostics };
        }
        catch (JsonException)
        {
            return new ProjectPulseAiProviderResult(
                Code,
                ProjectPulseAiOutcomes.Failure,
                null,
                "openai_invalid_response",
                "OpenAI returned an invalid response.",
                requestId,
                null,
                (int)httpResponse.StatusCode);
        }
    }

    public async Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        if (!IsModelApproved())
        {
            return new ProjectPulseAiProbeResult(Code, false, "model_not_approved", "The configured OpenAI model is not approved.", null, null);
        }

        var result = await GenerateAsync(ProjectPulseAiHttp.ProbeRequest, cancellationToken);
        return ProjectPulseAiHttp.GenerationProbe(result, "OpenAI");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? payload)
    {
        var provider = Provider;
        var request = new HttpRequestMessage(method, provider.Endpoint + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        if (!string.IsNullOrWhiteSpace(provider.Organization)) request.Headers.Add("OpenAI-Organization", provider.Organization);
        if (!string.IsNullOrWhiteSpace(provider.Project)) request.Headers.Add("OpenAI-Project", provider.Project);
        if (payload is not null) request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return request;
    }

    private bool IsModelApproved() =>
        Provider.ApprovedModels.Contains(Provider.Model, StringComparer.OrdinalIgnoreCase);

    private ProjectPulseAiProviderResult ModelNotApproved() => new(
        Code,
        ProjectPulseAiOutcomes.Unavailable,
        null,
        "model_not_approved",
        "The configured OpenAI model is not approved.",
        null,
        null,
        null);

    private ProjectPulseAiProviderResult Refusal(
        string code,
        string? requestId,
        int? status,
        ProjectPulseAiRateLimits? rateLimits) => new(
        Code,
        ProjectPulseAiOutcomes.Refusal,
        null,
        code,
        "OpenAI declined this request under its safety controls.",
        requestId,
        null,
        status,
        rateLimits);

    private ProjectPulseAiProviderResult Unavailable(string code, string? requestId, int? status) => new(
        Code,
        ProjectPulseAiOutcomes.Unavailable,
        null,
        code,
        "OpenAI is unavailable.",
        requestId,
        null,
        status);
}

internal static class ProjectPulseAiHttp
{
    public static readonly ProjectPulseAiGenerationRequest ProbeRequest = new(
        ProjectPulseAiFeatures.TimesheetDescription,
        "Return only the requested word.",
        "Return OK.",
        // OpenAI Responses requires max_output_tokens to be at least 16. Keep
        // the shared probe valid for every governed provider adapter.
        MaxOutputTokens: 16,
        Temperature: 0);

    public static ProjectPulseAiProbeResult GenerationProbe(ProjectPulseAiProviderResult result, string displayName) =>
        new(
            result.Provider,
            result.IsSuccess && !string.IsNullOrWhiteSpace(result.Content),
            result.IsSuccess ? "generation_verified" : result.Code ?? "generation_probe_failed",
            result.IsSuccess ? $"{displayName} generation is verified." : $"{displayName} generation is unavailable.",
            result.HttpStatusCode,
            result.RequestId);

    public static async Task<(HttpResponseMessage? Response, string? ExceptionCode)> SendWithRetryAsync(
        IHttpClientFactory httpClientFactory,
        ProjectPulseAiConfiguration configuration,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        bool structuredSowPhase = false)
    {
        // The durable engine owns attempts. A transport retry must not silently
        // issue additional billable requests inside one reserved SOW attempt.
        var retryCount = structuredSowPhase ? 0 : configuration.RetryCount;
        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(structuredSowPhase ? Module025GenerationEngine.ProviderTimeoutSeconds : configuration.RequestTimeoutSeconds));
                var client = httpClientFactory.CreateClient("ProjectPulseAi");
                if (structuredSowPhase) client.Timeout = Timeout.InfiniteTimeSpan;
                using var request = requestFactory();
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (!IsTransient(response.StatusCode) || attempt >= retryCount)
                {
                    return (response, null);
                }

                response.Dispose();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= retryCount) return (null, "provider_timeout");
            }
            catch (HttpRequestException)
            {
                if (attempt >= retryCount) return (null, "provider_network_error");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt)), cancellationToken);
        }

        return (null, "provider_unavailable");
    }

    public static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Length <= 1_000_000 ? body : body[..1_000_000];
    }

    public static bool IsTransient(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 408 or 409 or 429 or 500 or 502 or 503 or 504 or 529;
    }

    public static bool IsSafetyRefusal(HttpStatusCode statusCode, string body)
    {
        if (statusCode != HttpStatusCode.BadRequest && statusCode != HttpStatusCode.UnprocessableEntity) return false;

        var normalized = body.ToLowerInvariant();
        return normalized.Contains("content_policy", StringComparison.Ordinal)
            || normalized.Contains("safety", StringComparison.Ordinal)
            || normalized.Contains("refusal", StringComparison.Ordinal)
            || normalized.Contains("moderation", StringComparison.Ordinal);
    }

    public static string ErrorCode(string body, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                return SafeCode(String(error, "code") ?? String(error, "type") ?? fallback);
            }
        }
        catch (JsonException)
        {
        }

        return fallback;
    }

    public static string? Header(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    public static ProjectPulseAiRateLimits? ClaudeRateLimits(HttpResponseMessage response) =>
        RateLimits(
            response,
            "anthropic-ratelimit-requests-remaining",
            "anthropic-ratelimit-tokens-remaining",
            "anthropic-ratelimit-requests-reset",
            "anthropic-ratelimit-tokens-reset");

    public static ProjectPulseAiRateLimits? OpenAiRateLimits(HttpResponseMessage response) =>
        RateLimits(
            response,
            "x-ratelimit-remaining-requests",
            "x-ratelimit-remaining-tokens",
            "x-ratelimit-reset-requests",
            "x-ratelimit-reset-tokens");

    public static string? String(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static ProjectPulseAiUsage? ClaudeUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)) return null;
        var input = Int64(usage, "input_tokens");
        var output = Int64(usage, "output_tokens");
        return new ProjectPulseAiUsage(input, output, Add(input, output));
    }

    public static ProjectPulseAiUsage? OpenAiUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)) return null;
        var input = Int64(usage, "input_tokens");
        var output = Int64(usage, "output_tokens");
        var total = Int64(usage, "total_tokens") ?? Add(input, output);
        var reasoning = usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty("output_tokens_details", out var details)
            ? Int64(details, "reasoning_tokens") : null;
        return new ProjectPulseAiUsage(input, output, total, reasoning);
    }

    private static long? Int64(JsonElement element, string property)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) && result >= 0 ? result : null;
    }

    private static long? Add(long? left, long? right) =>
        left is null && right is null ? null : (left ?? 0) + (right ?? 0);

    private static ProjectPulseAiRateLimits? RateLimits(
        HttpResponseMessage response,
        string requestsRemainingHeader,
        string tokensRemainingHeader,
        string requestsResetHeader,
        string tokensResetHeader)
    {
        var rateLimits = new ProjectPulseAiRateLimits(
            Header(response, requestsRemainingHeader),
            Header(response, tokensRemainingHeader),
            Header(response, requestsResetHeader),
            Header(response, tokensResetHeader));

        return rateLimits.RequestsRemaining is null
            && rateLimits.TokensRemaining is null
            && rateLimits.RequestsReset is null
            && rateLimits.TokensReset is null
                ? null
                : rateLimits;
    }

    private static string SafeCode(string value)
    {
        var safe = new string(value
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-')
            .Take(80)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "provider_error" : safe;
    }
}
