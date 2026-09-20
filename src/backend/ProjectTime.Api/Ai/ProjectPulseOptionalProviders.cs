using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ProjectTime.Api.Ai;

// Optional providers use the same routing, privacy, health and usage contracts.
// Disabled until an administrator supplies credentials and explicitly enables them.
public sealed class ProjectPulseGeminiProvider(IHttpClientFactory clients, ProjectPulseAiConfiguration configuration) : IProjectPulseAiProvider
{
    public string Code => ProjectPulseAiProviders.Gemini;
    public async Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiGenerationRequest request, CancellationToken cancellationToken)
    {
        var provider = configuration.Provider(Code);
        if (!provider.Enabled || !provider.Configured) return Failure("provider_not_configured");
        if (!provider.ApprovedModels.Contains(provider.Model)) return Failure("model_not_approved");
        if (request.StructuredSowPhase) return Failure("structured_sow_adapter_unavailable");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Post, provider.Endpoint + "/chat/completions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        message.Content = JsonContent.Create(new { model = provider.Model,
            messages = new[] { new { role = "system", content = request.SystemPrompt }, new { role = "user", content = request.UserPrompt } },
            max_tokens = Math.Min(request.MaxOutputTokens, configuration.MaxOutputTokens) });
        using var response = await clients.CreateClient("OptionalAiProviders").SendAsync(message, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode) return Failure("gemini_http_" + (int)response.StatusCode);
        return Parse(body);
    }
    internal static ProjectPulseAiProviderResult Parse(string body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return Failure("gemini_empty_response");
        var choice = choices[0];
        var message = choice.GetProperty("message");
        if ((choice.TryGetProperty("finish_reason", out var finish) && finish.GetString() is "content_filter" or "SAFETY")
            || (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(refusal.GetString())))
            return new(ProjectPulseAiProviders.Gemini, ProjectPulseAiOutcomes.Refusal, null, "gemini_safety_refusal", "Gemini declined the request.", null, null, 200);
        if (choice.TryGetProperty("finish_reason", out finish) && finish.GetString() == "length") return Failure("gemini_output_truncated");
        var content = message.TryGetProperty("content", out var text) ? text.GetString() : null;
        ProjectPulseAiUsage? usage = null;
        if (root.TryGetProperty("usage", out var tokens))
            usage = new(Read(tokens, "prompt_tokens"), Read(tokens, "completion_tokens"), Read(tokens, "total_tokens"));
        return string.IsNullOrWhiteSpace(content) ? Failure("gemini_empty_response")
            : new(ProjectPulseAiProviders.Gemini, ProjectPulseAiOutcomes.Success, content, "generation_succeeded", null, null, usage, 200);
    }
    private static long? Read(JsonElement value, string name) => value.TryGetProperty(name, out var n) && n.TryGetInt64(out var number) ? number : null;
    private static ProjectPulseAiProviderResult Failure(string code) => new(ProjectPulseAiProviders.Gemini, ProjectPulseAiOutcomes.Unavailable, null, code, "Gemini could not complete this request.", null, null, null);
    public async Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(new(ProjectPulseAiFeatures.HelpAssistant, "Connectivity test. Return OK only.", "OK", 64, 0), cancellationToken);
        return new(Code, result.IsSuccess, result.Code ?? "probe_failed", result.IsSuccess ? "Gemini responded." : "Gemini probe failed.", result.HttpStatusCode, result.RequestId);
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
