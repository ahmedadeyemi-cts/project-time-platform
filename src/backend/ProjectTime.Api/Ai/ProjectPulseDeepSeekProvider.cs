using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Ai;

/// <summary>User-authorized, fixed DGX endpoint. Keys and reasoning never leave the server response boundary.</summary>
public sealed class ProjectPulseDeepSeekProvider(
    IHttpClientFactory clients,
    ProjectPulseAiConfiguration configuration) : IProjectPulseAiProvider
{
    public const string Endpoint = "https://dgx-spark-lab01.taile0ffc4.ts.net/v1";
    public const string Model = "deepseek-v4-flash-0731";
    private static readonly AsyncLocal<string?> SelectedPrivateTarget = new();
    internal static string? PrivateTarget => SelectedPrivateTarget.Value;
    internal static async Task<ProjectPulseAiProviderResult> RunPrivateTargetAsync(
        string target, Func<CancellationToken, Task<ProjectPulseAiProviderResult>> action, CancellationToken token)
    {
        var previous = SelectedPrivateTarget.Value;
        SelectedPrivateTarget.Value = target;
        try { return await action(token); }
        finally { SelectedPrivateTarget.Value = previous; }
    }
    public string Code => ProjectPulseAiProviders.DeepSeek;

    public async Task<ProjectPulseAiProviderResult> GenerateAsync(
        ProjectPulseAiGenerationRequest request, CancellationToken cancellationToken)
    {
        var provider = configuration.DeepSeek;
        if (!provider.Enabled || !provider.Configured) return Failure("deepseek_not_configured");
        if (provider.Endpoint != Endpoint || provider.Model != Model) return Failure("deepseek_configuration_rejected");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            // One slot across API replicas, health probes, and private consumers.
            // Transaction-scoped locking releases on cancellation or connection loss.
            var connectionString = ProjectPulseAiDatabaseConnection.Resolve();
            if (string.IsNullOrWhiteSpace(connectionString)) return Failure("deepseek_queue_unavailable");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(budget.Token);
            await using var transaction = await connection.BeginTransactionAsync(budget.Token);
            var queueDeadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (true)
            {
                await using var slot = new NpgsqlCommand(
                    "SELECT pg_try_advisory_xact_lock(64004, 1)", connection, transaction);
                if (await slot.ExecuteScalarAsync(budget.Token) is true) break;
                if (DateTimeOffset.UtcNow >= queueDeadline) return Failure("deepseek_queue_busy");
                await Task.Delay(250, budget.Token);
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/chat/completions");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
            message.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "system", content = request.SystemPrompt },
                    new { role = "user", content = request.UserPrompt }
                },
                // Reasoning consumes the same budget; short probes still need headroom.
                max_tokens = Math.Clamp(request.MaxOutputTokens, 500, 16384),
                stream = false
            }), Encoding.UTF8, "application/json");
            using var response = await clients.CreateClient("DeepSeekDgx").SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            if (!response.IsSuccessStatusCode)
            {
                if (await PulseAiPrivateModelResponsePolicy.IsSafetyRefusalErrorAsync(response, budget.Token))
                    return Refusal();
                return Failure($"deepseek_http_{(int)response.StatusCode}");
            }
            using var json = await PulseAiPrivateModelResponsePolicy.ReadBoundedJsonAsync(response.Content, budget.Token);
            return ParseCompletion(json.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Failure("deepseek_timeout"); }
        catch (HttpRequestException) { return Failure("deepseek_connection_failed"); }
        catch (JsonException) { return Failure("deepseek_invalid_response"); }
        catch (NpgsqlException) { return Failure("deepseek_queue_unavailable"); }
    }

    internal static ProjectPulseAiProviderResult ParseCompletion(JsonElement root)
    {
        if (PulseAiPrivateModelResponsePolicy.IsSafetyRefusal(root)) return Refusal();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return Failure("deepseek_invalid_response");
        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var reason)
            && reason.ValueKind == JsonValueKind.String
            && reason.GetString() is "length" or "max_tokens")
            return Failure("deepseek_output_budget_exhausted");
        if (!choice.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(content.GetString()))
            return Failure("deepseek_empty_response");
        // Do not return reasoning_content, reasoning, raw envelopes, or error text.
        return new(ProjectPulseAiProviders.DeepSeek, ProjectPulseAiOutcomes.Success,
            content.GetString()!.Trim(), null, null, null, null, 200);
    }

    private static ProjectPulseAiProviderResult Failure(string code) => new(
        ProjectPulseAiProviders.DeepSeek, ProjectPulseAiOutcomes.Unavailable,
        null, code, "DeepSeek v4 could not complete this request.", null, null, null);

    private static ProjectPulseAiProviderResult Refusal() => new(
        ProjectPulseAiProviders.DeepSeek, ProjectPulseAiOutcomes.Refusal,
        null, "deepseek_safety_refusal", "DeepSeek v4 declined this request.", null, null, null);

    public async Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var result = await GenerateAsync(new("provider_readiness", "Reply briefly.", "Say hello.", 500, 0), cancellationToken);
        return new(Code, result.IsSuccess, result.Code ?? "generation_succeeded",
            result.IsSuccess ? "DeepSeek v4 is ready." : "DeepSeek v4 is not ready.", result.HttpStatusCode, null);
    }
}
