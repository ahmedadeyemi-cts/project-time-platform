using ProjectTime.Api.Ai;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
var configuration = new ProjectPulseAiConfiguration();
configuration.ApplyStoredSecret("gemini", "fixture-api-key-never-return", "fixture", DateTimeOffset.UtcNow);
configuration.ApplyStoredEnabled("gemini", true);
var handler = new Handler();
var provider = new ProjectPulseGeminiProvider(new Factory(handler), configuration);
var request = new ProjectPulseAiGenerationRequest(ProjectPulseAiFeatures.SowGsdPlanning, "Permission-checked technical facts only", "Cisco CUCM 14.0 to 15.0", 12288, 0)
    { StructuredSowPhase = true, SowPhase = "Plan" };
var result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.IsSuccess && result.Usage?.InputTokens == 12 && result.Usage.OutputTokens == 16
    && result.Usage.TotalTokens == 28 && result.Usage.ReasoningTokens == 8, "Gemini structured phase preserves usage including reasoning");
using (var payload = JsonDocument.Parse(handler.LastBody!))
{
    var root = payload.RootElement;
    Check(root.GetProperty("model").GetString() == configuration.Provider("gemini").Model, "uses current Module 064 model");
    Check(root.GetProperty("max_tokens").GetInt32() == 12288, "structured phase receives full bounded output budget");
    Check(root.GetProperty("reasoning_effort").GetString() == "low", "Gemini uses documented low reasoning effort");
    var format = root.GetProperty("response_format");
    Check(format.GetProperty("type").GetString() == "json_schema", "structured JSON output requested");
    var schema = format.GetProperty("json_schema");
    Check(schema.GetProperty("strict").GetBoolean(), "strict schema requested");
    Check(schema.GetProperty("schema").GetProperty("properties").GetProperty("tasks").GetProperty("items")
        .GetProperty("properties").GetProperty("phase").GetProperty("enum")[0].GetString() == "Plan", "schema constrained to current server phase");
    Check(!schema.GetRawText().Contains("\"exclusiveMinimum\"") && !schema.GetRawText().Contains("\"pattern\""), "unsupported Gemini schema keywords adapted");
    Check(schema.GetProperty("schema").GetProperty("properties").GetProperty("tasks").GetProperty("minItems").GetInt32() == 2, "task count constraints preserved");
}
Check(handler.LastUri == "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "uses configured Gemini OpenAI-compatible endpoint");
var before = handler.Calls;
Check(!(await provider.GenerateAsync(request with { SowPhase = null }, CancellationToken.None)).IsSuccess && handler.Calls == before, "invalid phase never sent");
Check(!(await provider.GenerateAsync(request with { SowPhase = "Unknown" }, CancellationToken.None)).IsSuccess && handler.Calls == before, "unknown phase never sent");
await provider.GenerateAsync(request with { StructuredSowPhase = false }, CancellationToken.None);
using (var payload = JsonDocument.Parse(handler.LastBody!))
    Check(payload.RootElement.GetProperty("max_tokens").GetInt32() == configuration.MaxOutputTokens
        && !payload.RootElement.TryGetProperty("response_format", out _), "generic traffic keeps normal token budget");

var live = configuration.Provider("gemini");
var candidate = live with { Model = "gemini-3.1-flash-lite", ApprovedModels = [live.Model, "gemini-3.1-flash-lite"] };
var candidateProbe = await provider.ProbeModelAsync(candidate, CancellationToken.None);
Check(candidateProbe.Available && configuration.Provider("gemini") == live, "candidate verification never mutates active model");
using (var payload = JsonDocument.Parse(handler.LastBody!))
    Check(payload.RootElement.GetProperty("model").GetString() == candidate.Model, "candidate probe tests exactly requested model");
before = handler.Calls;
Check(!(await provider.ProbeModelAsync(candidate with { ApprovedModels = [live.Model] }, CancellationToken.None)).Available
    && handler.Calls == before, "unapproved candidate never sent");

foreach (var body in new[] { "not json", "[]", "null", "{}", "{\"choices\":{}}", "{\"choices\":[null]}", "{\"choices\":[{\"message\":null}]}" })
{
    handler.Body = body;
    Check(!(await provider.GenerateAsync(request, CancellationToken.None)).IsSuccess, "malformed provider response rejected");
}
handler.Body = """{"choices":[{"finish_reason":"length","message":{"content":"partial"}}],"usage":{"prompt_tokens":12,"completion_tokens":120,"total_tokens":132}}""";
result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.Code == "gemini_output_truncated" && result.Content is null && result.Usage?.OutputTokens == 120, "truncation rejected while preserving billable usage");
Check(result.SowDiagnostics?.StopReason == "length", "closed phase diagnostics report truncation");
foreach (var finish in new[] { "content_filter", "SAFETY", "RECITATION" })
{
    handler.Body = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finish, message = new { content = "declined" } } } });
    Check((await provider.GenerateAsync(request, CancellationToken.None)).IsRefusal, "provider safety refusal remains terminal");
}
handler.Body = """{"choices":[{"finish_reason":"stop","message":{"content":"not usable","refusal":"declined"}}]}""";
Check((await provider.GenerateAsync(request, CancellationToken.None)).IsRefusal, "explicit refusal remains terminal");
foreach (var finish in new[] { "tool_calls", "unknown" })
{
    handler.Body = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finish, message = new { content = "{}" } } } });
    Check((await provider.GenerateAsync(request, CancellationToken.None)).Code == "structured_sow_response_incomplete", "incomplete structured response rejected");
}
handler.Body = """{"choices":[{"message":{"content":"{}"}}]}""";
Check((await provider.GenerateAsync(request, CancellationToken.None)).Code == "structured_sow_response_incomplete", "missing completion status rejected");
handler.Body = """{"choices":[{"finish_reason":"stop","message":{"content":{}}}],"usage":{"prompt_tokens":"bad","completion_tokens_details":null}}""";
Check(!(await provider.GenerateAsync(request, CancellationToken.None)).IsSuccess, "wrong JSON field types rejected without exception");
handler.Body = new string('x', 1_000_001);
Check((await provider.GenerateAsync(request, CancellationToken.None)).Code == "gemini_response_too_large", "response body bounded");

handler.Status = HttpStatusCode.TooManyRequests;
handler.Body = """{"error":{"code":429,"message":"Sensitive project fixture-api-key-never-return customer@example.test","details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier","quotaMetric":"requests","quotaDimensions":{"project":"private-project"}}]},{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"600.5s"}]}}""";
handler.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(900));
result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.Code == "gemini_http_429_daily_quota" && result.HttpStatusCode == 429, "429 classified only from structured quota detail");
Check(result.RetryAfterUtc > DateTimeOffset.UtcNow.AddSeconds(895), "longest provider retry deadline wins");
var exposed = JsonSerializer.Serialize(result);
Check(!exposed.Contains("fixture-api-key-never-return") && !exposed.Contains("private-project") && !exposed.Contains("customer@example.test"), "provider error details never exposed");
Check(result.Message!.Contains("daily quota") && result.RateLimits?.RequestsReset is not null, "actionable quota message and reset exposed");
Check(handler.Calls > 0, "transport exercised");
before = handler.Calls;
var probe = await provider.ProbeAsync(CancellationToken.None);
Check(handler.Calls == before + 1, "no hidden quota retries");
Check(probe.RetryAfterUtc is not null && probe.HttpStatusCode == 429 && probe.RateLimits is not null, "probe preserves error status and cooldown");
var health = new ProjectPulseAiHealthRegistry(configuration);
health.RecordProbe(probe);
Check(!health.CanAttempt("gemini", out _) && !health.ShouldProbe("gemini", TimeSpan.Zero, force: true), "manual and automatic checks respect provider cooldown");
var snapshot = health.Snapshot("gemini");
Check(snapshot.FailureCount == 0 && snapshot.ProbeFailureCount == 1, "probe failures remain separate from generation failures");
Check(snapshot.CircuitOpenUntil >= probe.RetryAfterUtc && snapshot.LastProbeFailureMessage!.Contains("daily quota"), "health reports actionable failure and complete cooldown");
health.RecordSuccess("gemini", null, null);
health.RecordProbe(new("gemini", true, "ok", "OK", 200, null));
Check(!health.ShouldProbe("gemini", TimeSpan.Zero, force: true) && !health.CanAttempt("gemini", out _), "older concurrent successes do not clear a newer cooldown");

var candidateHealth = new ProjectPulseAiHealthRegistry(configuration);
candidateHealth.RecordProviderCooldown("gemini", DateTimeOffset.UtcNow.AddMinutes(5));
snapshot = candidateHealth.Snapshot("gemini");
Check(snapshot.ProbeFailureCount == 0 && snapshot.FailureCount == 0 && snapshot.LastProbeFailureCode is null,
    "candidate quota result does not become active model failure");
Check(!candidateHealth.ShouldProbe("gemini", TimeSpan.Zero, force: true), "candidate account cooldown suppresses probe");
candidateHealth.ApplyConfiguration(candidate);
Check(!candidateHealth.ShouldProbe("gemini", TimeSpan.Zero, force: true), "model change does not clear project quota cooldown");
configuration.ApplyStoredSecret("gemini", "rotated-fixture-key", "fixture-v2", DateTimeOffset.UtcNow);
candidateHealth.ApplyConfiguration(configuration.Provider("gemini"));
Check(candidateHealth.ShouldProbe("gemini", TimeSpan.Zero, force: true) && candidateHealth.Snapshot("gemini").RetryAfterUtc is null,
    "credential rotation permits a new project to verify without waiting through old quota");
candidateHealth.RecordProbe(probe);
Check(candidateHealth.Snapshot("gemini").RetryAfterUtc is null, "old credential probe result cannot reapply cooldown after key rotation");
var generationHealth = new ProjectPulseAiHealthRegistry(configuration);
generationHealth.RecordFailure("gemini", result.Code!, null, result.RetryAfterUtc);
Check(!generationHealth.CanAttempt("gemini", out _) && generationHealth.Snapshot("gemini").FailureCount == 1, "generation 429 opens cooldown immediately");
generationHealth.RecordFailure("gemini", "module025_external_gemini_http_429_daily_quota", null, result.RetryAfterUtc);
Check(generationHealth.Snapshot("gemini").LastFailureMessage!.Contains("daily quota"), "phase-prefixed quota failures retain actionable explanation");

handler.RetryAfter = null;
handler.Body = """{"error":{"details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaId":"GenerateRequestsPerMinutePerProjectPerModel"}]},{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"120.25s"}]}}""";
result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.Code == "gemini_http_429_rate_limit" && result.RetryAfterUtc > DateTimeOffset.UtcNow.AddSeconds(115), "rate limit and fractional retry delay handled");
handler.Body = "upstream gateway returned no JSON";
result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.Code == "gemini_http_429" && result.RetryAfterUtc > DateTimeOffset.UtcNow.AddSeconds(55), "unknown 429 stays honest and waits at least one minute");
handler.Body = """{"error":{"details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"QUOTA_EXCEEDED"},{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"Infinitys"}]}}""";
result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.Code == "gemini_http_429_quota_exhausted" && result.RetryAfterUtc < DateTimeOffset.UtcNow.AddMinutes(2), "invalid retry delay cannot overflow or escape bound");
handler.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(15));
result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.RetryAfterUtc > DateTimeOffset.UtcNow.AddMinutes(14), "HTTP-date Retry-After accepted");
handler.RetryAfter = null;
handler.Status = HttpStatusCode.Forbidden;
result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.Code == "gemini_http_403" && result.Message!.Contains("permissions") && result.RetryAfterUtc is null, "authorization errors distinct from quota");
handler.RequestId = "invalid sensitive customer@example.test";
result = await provider.GenerateAsync(request, CancellationToken.None);
Check(result.RequestId is null, "unsafe request identifier omitted");
handler.Throw = new HttpRequestException("Sensitive upstream details");
Check((await provider.GenerateAsync(request, CancellationToken.None)).Code == "gemini_network_error", "network errors sanitized");
handler.Throw = new OperationCanceledException();
Check((await provider.GenerateAsync(request, CancellationToken.None)).Code == "gemini_timeout", "request timeout reported safely");
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
var cancelled = false;
try { await provider.GenerateAsync(request, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
Check(cancelled, "caller cancellation remains cancellation");
foreach (var changed in new[] { "model", "credential" })
{
    var concurrentConfiguration = new ProjectPulseAiConfiguration();
    concurrentConfiguration.ApplyStoredSecret("gemini", "before-rotation", "before", DateTimeOffset.UtcNow);
    concurrentConfiguration.ApplyStoredEnabled("gemini", true);
    var concurrentHealth = new ProjectPulseAiHealthRegistry(concurrentConfiguration);
    concurrentHealth.RecordProbe(new("gemini", true, "ready", "Ready", 200, null));
    var deferredProvider = new DeferredProvider();
    var coordinator = new ProjectPulseAiHealthCoordinator(concurrentConfiguration, [deferredProvider], concurrentHealth);
    var refresh = coordinator.RefreshAsync(true, CancellationToken.None);
    await deferredProvider.Started.Task;
    if (changed == "model") concurrentConfiguration.ApplyStoredModel("gemini", "gemini-3.1-flash-lite");
    else concurrentConfiguration.ApplyStoredSecret("gemini", "after-rotation", "after", DateTimeOffset.UtcNow);
    deferredProvider.Completed.SetResult(new("gemini", false, "gemini_http_429", "Quota", 429, null)
        { RetryAfterUtc = DateTimeOffset.UtcNow.AddHours(1) });
    await refresh;
    var afterRotation = concurrentHealth.Snapshot("gemini");
    Check(afterRotation.ProbeFailureCount == 0 && afterRotation.RetryAfterUtc is null,
        changed + " changed during probe cannot acquire old health failure");
    Check(concurrentHealth.ShouldProbe("gemini", TimeSpan.FromMinutes(10)), changed + " changed during probe becomes due for a fresh check");
}
Console.WriteLine($"{checks} Module 064 Gemini checks passed.");

sealed class Factory(Handler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, false);
}
sealed class Handler : HttpMessageHandler
{
    public string Body = """{"choices":[{"finish_reason":"stop","message":{"content":"{}"}}],"usage":{"prompt_tokens":12,"completion_tokens":16,"total_tokens":28,"completion_tokens_details":{"reasoning_tokens":8}}}""";
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string? LastBody;
    public string? LastUri;
    public string RequestId = "fixture-request-123";
    public RetryConditionHeaderValue? RetryAfter;
    public Exception? Throw;
    public int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastUri = request.RequestUri?.AbsoluteUri;
        LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        if (Throw is not null) throw Throw;
        var response = new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        if (RetryAfter is not null) response.Headers.RetryAfter = RetryAfter;
        response.Headers.TryAddWithoutValidation("x-request-id", RequestId);
        return response;
    }
}

sealed class DeferredProvider : IProjectPulseAiProvider
{
    public string Code => ProjectPulseAiProviders.Gemini;
    public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<ProjectPulseAiProbeResult> Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<ProjectPulseAiProviderResult> GenerateAsync(ProjectPulseAiGenerationRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public async Task<ProjectPulseAiProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        Started.SetResult(true);
        return await Completed.Task.WaitAsync(cancellationToken);
    }
}
