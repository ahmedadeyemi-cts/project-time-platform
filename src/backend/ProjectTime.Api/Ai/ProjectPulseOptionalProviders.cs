using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

// Optional providers use the same routing, privacy, health and usage contracts.
// Disabled until an administrator supplies credentials and explicitly enables them.
public sealed class ProjectPulseGeminiProvider(IHttpClientFactory clients, ProjectPulseAiConfiguration configuration) : IProjectPulseAiProvider
{
    public string Code => ProjectPulseAiProviders.Gemini;
    public Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiGenerationRequest request, CancellationToken cancellationToken) =>
        GenerateAsync(configuration.Provider(Code), request, cancellationToken);

    private async Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiProviderConfiguration provider, ProjectPulseAiGenerationRequest request, CancellationToken cancellationToken)
    {
        if (!provider.Enabled || !provider.Configured) return Failure("provider_not_configured");
        if (!provider.ApprovedModels.Contains(provider.Model, StringComparer.OrdinalIgnoreCase)) return Failure("model_not_approved");
        if (request.StructuredSowPhase && (request.SowPhase is null || !Module025GenerationEngine.Phases.Contains(request.SowPhase)))
            return Failure("module025_phase_request_invalid");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(request.StructuredSowPhase
            ? Module025GenerationEngine.ExternalProviderTimeoutSeconds : configuration.RequestTimeoutSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Post, provider.Endpoint + "/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        message.Content = JsonContent.Create(Payload(provider.Model, request, configuration.MaxOutputTokens));
        try
        {
            // The durable phase engine owns attempts. Never retry a billable
            // phase behind its back, or wait through a provider quota cooldown.
            var client = clients.CreateClient("OptionalAiProviders");
            if (request.StructuredSowPhase) client.Timeout = Timeout.InfiniteTimeSpan;
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await ReadBoundedBodyAsync(response, timeout.Token);
            if (body is null) return Failure("gemini_response_too_large", (int)response.StatusCode);
            if (!response.IsSuccessStatusCode) return ProjectPulseGeminiErrors.Parse(response, body, DateTimeOffset.UtcNow);
            return Parse(body, request.StructuredSowPhase) with
            {
                HttpStatusCode = (int)response.StatusCode,
                RequestId = ProjectPulseGeminiErrors.RequestId(response)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure("gemini_timeout");
        }
        catch (HttpRequestException)
        {
            return Failure("gemini_network_error");
        }
    }

    internal static Dictionary<string, object?> Payload(string model, ProjectPulseAiGenerationRequest request, int genericTokenLimit)
    {
        var fields = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[] { new { role = "system", content = request.SystemPrompt }, new { role = "user", content = request.UserPrompt } },
            ["max_tokens"] = Math.Min(request.MaxOutputTokens, request.StructuredSowPhase
                ? Module025GenerationEngine.MaximumExternalOutputTokens : genericTokenLimit)
        };
        // Google maps low effort for Gemini 2.5 and 3 to their supported
        // thinking controls. Other/new model families keep their defaults.
        // https://ai.google.dev/gemini-api/docs/openai#thinking
        if (model.StartsWith("gemini-2.5-", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
            fields["reasoning_effort"] = "low";
        if (request.StructuredSowPhase)
            fields["response_format"] = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = Module025PhaseOutputContract.Name,
                    strict = true,
                    schema = Module025GeminiPhaseContract.Schema(request.SowPhase!)
                }
            };
        return fields;
    }

    internal static ProjectPulseAiProviderResult Parse(string body, bool structuredSowPhase = false)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return Failure("gemini_empty_response", 200);
            var choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object || !choice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object) return Failure("gemini_invalid_response", 200);
            var finish = Text(choice, "finish_reason");
            var content = Text(message, "content");
            ProjectPulseAiUsage? usage = null;
            if (root.TryGetProperty("usage", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
                usage = new(Read(tokens, "prompt_tokens"), Read(tokens, "completion_tokens"), Read(tokens, "total_tokens"),
                    tokens.TryGetProperty("completion_tokens_details", out var details) ? Read(details, "reasoning_tokens") : null);
            var diagnostics = structuredSowPhase ? new Module025ProviderDiagnostics(
                StopReason: finish is "stop" or "length" or "content_filter" or "SAFETY" or "RECITATION" or "tool_calls" ? finish : finish is null ? "missing" : "other",
                OutputTextCharacters: content?.Length ?? 0) : null;
            if (finish is "content_filter" or "SAFETY" or "RECITATION" || !string.IsNullOrWhiteSpace(Text(message, "refusal")))
                return new(ProjectPulseAiProviders.Gemini, ProjectPulseAiOutcomes.Refusal, null, "gemini_safety_refusal",
                    "Gemini declined the request.", null, usage, 200) { SowDiagnostics = diagnostics };
            if (finish == "length") return Failure("gemini_output_truncated", 200) with { Usage = usage, SowDiagnostics = diagnostics };
            if (structuredSowPhase && finish != "stop")
                return Failure("structured_sow_response_incomplete", 200) with { Usage = usage, SowDiagnostics = diagnostics };
            return string.IsNullOrWhiteSpace(content) ? Failure("gemini_empty_response", 200) with { Usage = usage, SowDiagnostics = diagnostics }
                : new(ProjectPulseAiProviders.Gemini, ProjectPulseAiOutcomes.Success, content, "generation_succeeded", null, null, usage, 200) { SowDiagnostics = diagnostics };
        }
        catch (JsonException)
        {
            return Failure("gemini_invalid_response", 200);
        }
    }

    private static async Task<string?> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int limit = 1_000_000;
        if (response.Content.Headers.ContentLength > limit) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var buffer = new char[8192];
        var body = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            if (body.Length + count > limit) return null;
            body.Append(buffer, 0, count);
        }
        return body.ToString();
    }

    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
    private static long? Read(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt64(out var number) && number >= 0 ? number : null;
    private static ProjectPulseAiProviderResult Failure(string code, int? status = null) => new(ProjectPulseAiProviders.Gemini,
        ProjectPulseAiOutcomes.Unavailable, null, code, ProjectPulseGeminiErrors.Message(code) ?? "Gemini could not complete this request.", null, null, status);
    public Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken cancellationToken) =>
        ProbeModelAsync(configuration.Provider(Code), cancellationToken);

    public async Task<ProjectPulseAiProbeResult> ProbeModelAsync(ProjectPulseAiProviderConfiguration candidate, CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(candidate, new(ProjectPulseAiFeatures.HelpAssistant, "Connectivity test. Return OK only.", "OK", 64, 0), cancellationToken);
        return new(Code, result.IsSuccess, result.Code ?? "probe_failed", result.IsSuccess ? "Gemini responded." : result.Message ?? "Gemini probe failed.",
            result.HttpStatusCode, result.RequestId)
        {
            RetryAfterUtc = result.RetryAfterUtc,
            RateLimits = result.RateLimits,
            CredentialFingerprint = candidate.Secret.Fingerprint
        };
    }
}

public sealed class ProjectPulseCopilotStudioProvider(IHttpClientFactory clients, ProjectPulseAiConfiguration configuration) : IProjectPulseAiProvider
{
    public string Code => ProjectPulseAiProviders.Copilot;
    public async Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiGenerationRequest request, CancellationToken cancellationToken)
    {
        var provider = configuration.Provider(Code);
        if (!provider.Enabled || !provider.Configured) return Failure("provider_not_configured");
        if (request.StructuredSowPhase) return Failure("structured_sow_adapter_unavailable");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds));
        var client = clients.CreateClient("OptionalAiProviders");
        using var start = new HttpRequestMessage(HttpMethod.Post, provider.Endpoint + "/conversations");
        start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        using var started = await client.SendAsync(start, timeout.Token);
        if (!started.IsSuccessStatusCode) return Failure("copilot_connection_" + (int)started.StatusCode);
        using var session = JsonDocument.Parse(await started.Content.ReadAsStringAsync(timeout.Token));
        var id = session.RootElement.GetProperty("conversationId").GetString();
        var token = session.RootElement.GetProperty("token").GetString();
        var url = provider.Endpoint + "/conversations/" + Uri.EscapeDataString(id!) + "/activities";
        var sender = "pulse-" + Guid.NewGuid().ToString("N");
        using var activity = new HttpRequestMessage(HttpMethod.Post, url);
        activity.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        activity.Content = JsonContent.Create(new { type = "message", from = new { id = sender }, text = request.SystemPrompt + "\n\n" + request.UserPrompt });
        using var sent = await client.SendAsync(activity, timeout.Token);
        if (!sent.IsSuccessStatusCode) return Failure("copilot_activity_" + (int)sent.StatusCode);
        using var receipt = JsonDocument.Parse(await sent.Content.ReadAsStringAsync(timeout.Token));
        var activityId = receipt.RootElement.GetProperty("id").GetString();
        string? watermark = null;
        while (!timeout.IsCancellationRequested)
        {
            using var read = new HttpRequestMessage(HttpMethod.Get, url + (watermark is null ? "" : "?watermark=" + Uri.EscapeDataString(watermark)));
            read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(read, timeout.Token);
            if (!response.IsSuccessStatusCode) return Failure("copilot_response_" + (int)response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (body.RootElement.TryGetProperty("watermark", out var mark)) watermark = mark.GetString();
            foreach (var item in body.RootElement.GetProperty("activities").EnumerateArray())
            {
                if (!item.TryGetProperty("replyToId", out var reply) || reply.GetString() != activityId) continue;
                if (!item.TryGetProperty("from", out var from) || !from.TryGetProperty("role", out var role) || role.GetString() != "bot") continue;
                if (item.TryGetProperty("text", out var text) && !string.IsNullOrWhiteSpace(text.GetString()))
                    return new(Code, ProjectPulseAiOutcomes.Success, text.GetString(), "generation_succeeded", null, activityId, null, 200);
            }
            await Task.Delay(500, timeout.Token);
        }
        return Failure("copilot_response_timeout");
    }
    private ProjectPulseAiProviderResult Failure(string code) => new(Code, ProjectPulseAiOutcomes.Unavailable, null, code, "Copilot Studio could not complete this request.", null, null, null);
    public async Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(new(ProjectPulseAiFeatures.HelpAssistant, "Connectivity test. Return OK only.", "OK", 64, 0), cancellationToken);
        return new(Code, result.IsSuccess, result.Code ?? "probe_failed", result.IsSuccess ? "Copilot Studio responded." : "Copilot Studio probe failed.", result.HttpStatusCode, result.RequestId);
    }
}
