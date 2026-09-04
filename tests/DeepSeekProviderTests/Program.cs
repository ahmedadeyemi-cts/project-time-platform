using System.Reflection;
using System.Text.Json;
using ProjectTime.Api.Ai;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}
var parser = typeof(ProjectPulseDeepSeekProvider).GetMethod("ParseCompletion", BindingFlags.Static | BindingFlags.NonPublic)!;
ProjectPulseAiProviderResult Parse(string json)
{
    using var doc = JsonDocument.Parse(json);
    return (ProjectPulseAiProviderResult)parser.Invoke(null, [doc.RootElement])!;
}
var success = Parse("""{"choices":[{"finish_reason":"stop","message":{"reasoning_content":"private reasoning","content":"Answer"}}]}""");
Check(success.IsSuccess && success.Content == "Answer", "Only final content may be returned.");
var limited = Parse("""{"choices":[{"finish_reason":"length","message":{"reasoning":"thinking","content":"partial"}}]}""");
Check(!limited.IsSuccess && limited.Code == "deepseek_output_budget_exhausted", "Truncated content must not be adopted.");
var refusal = Parse("""{"choices":[{"finish_reason":"length","message":{"refusal":"declined","content":""}}]}""");
Check(refusal.IsRefusal, "Refusal must take precedence over output exhaustion.");
Check(Parse("""{"choices":[{"finish_reason":"content_filter","message":{"content":"no"}}]}""").IsRefusal, "Content filtering must be terminal.");
Check(!Parse("""{"choices":[{"message":{"reasoning_content":"thinking","content":" "}}]}""").IsSuccess, "Reasoning-only output is not an answer.");
Check(!Parse("{}").IsSuccess, "Malformed envelope must fail closed.");
var config = new ProjectPulseAiConfiguration();
Check(!config.DeepSeek.Configured, "No key may be shipped.");
config.ApplyStoredSecret(ProjectPulseAiProviders.DeepSeek, "test-only-never-a-real-key", "test", DateTimeOffset.UtcNow);
Check(config.DeepSeek.Configured && config.Claude.ApiKey != "test-only-never-a-real-key", "DeepSeek key isolation.");
Check(!JsonSerializer.Serialize(config.ToSanitizedResponse()).Contains("test-only-never-a-real-key"), "Key must never be returned by configuration.");
Check(CelarAiCapabilityTargets.DefaultOrder.SequenceEqual(new[] {"deepseek_v4", "celar_ai", "claude", "openai", "local_template"}), "Default fallback order.");
config.ApplyStoredEnabled(ProjectPulseAiProviders.DeepSeek, false);
Check(!config.DeepSeek.Enabled, "DeepSeek can be disabled independently.");
Console.WriteLine("DEEPSEEK_PROVIDER_TESTS=PASS");
