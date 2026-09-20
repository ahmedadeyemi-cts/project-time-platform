using ProjectTime.Api.Ai;
using System.Net;
using System.Text;

var checks = 0;
void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
var configuration = new ProjectPulseAiConfiguration();
var originalOpenAi = configuration.OpenAi;
foreach (var code in new[] { ProjectPulseAiProviders.Gemini, ProjectPulseAiProviders.Copilot })
{
    Check(!configuration.Provider(code).Enabled, code + " disabled by default");
    configuration.ApplyStoredSecret(code, "test-only-secret", "test-v1", DateTimeOffset.UtcNow);
    configuration.ApplyStoredEnabled(code, true);
    configuration.ApplyStoredModel(code, configuration.Provider(code).Model);
    Check(configuration.Provider(code).Configured && configuration.Provider(code).Enabled, code + " configuration independent");
    Check(configuration.OpenAi == originalOpenAi, code + " never overwrites OpenAI");
}
var order = new[] { "copilot_studio", "gemini", "celar_ai", "deepseek_v4", "openai", "claude", "local_template" };
Check(CelarAiCapabilityCatalog.ValidateTargets(order).SequenceEqual(order), "custom priorities preserved");
foreach (var invalid in new[] { order.Reverse().ToArray(), order.Select(x => x == "gemini" ? "openai" : x).ToArray(), order.Where(x => x != "celar_ai").ToArray() })
{
    var rejected = false;
    try { CelarAiCapabilityCatalog.ValidateTargets(invalid); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "invalid route rejected");
}
var handler = new FakeHandler("""{"choices":[{"finish_reason":"stop","message":{"content":"A valid response."}}],"usage":{"prompt_tokens":12,"completion_tokens":4,"total_tokens":16}}""");
var gemini = new ProjectPulseGeminiProvider(new Factory(handler), configuration);
var request = new ProjectPulseAiGenerationRequest(ProjectPulseAiFeatures.HelpAssistant, "Safe generic instruction", "Public test", 64, 0);
var result = await gemini.GenerateAsync(request, CancellationToken.None);
Check(result.IsSuccess && result.Usage?.TotalTokens == 16, "Gemini content and usage");
Check(handler.LastUri == "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "Gemini fixed endpoint");
handler.Body = """{"choices":[{"finish_reason":"content_filter","message":{"refusal":"declined"}}]}""";
Check((await gemini.GenerateAsync(request, CancellationToken.None)).IsRefusal, "Gemini refusal terminal");
handler.Body = """{"choices":[{"finish_reason":"length","message":{"content":"partial"}}]}""";
Check(!(await gemini.GenerateAsync(request, CancellationToken.None)).IsSuccess, "Gemini truncated answer rejected");
var health = new ProjectPulseAiHealthRegistry(configuration);
health.RecordProbe(new(ProjectPulseAiProviders.Gemini, false, "http_503", "failed", 503, null));
Check(!health.CanAttempt(ProjectPulseAiProviders.Gemini, out _), "failed provider skipped");
health.RecordProbe(new(ProjectPulseAiProviders.Gemini, true, "ok", "ok", 200, null));
Check(health.CanAttempt(ProjectPulseAiProviders.Gemini, out _), "successful recovery probe restores provider");
Console.WriteLine($"{checks} checks passed");
sealed class Factory(FakeHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
sealed class FakeHandler(string body) : HttpMessageHandler
{
    public string Body = body;
    public string? LastUri;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri?.AbsoluteUri;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
    }
}
