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
Environment.SetEnvironmentVariable("PROJECTPULSE_DEEPSEEK_API_KEY", "test-only-injected-key");
try
{
    var injected = new ProjectPulseAiConfiguration();
    Check(injected.DeepSeek.Configured, "Deployment-injected DeepSeek credential must load.");
    Check(!JsonSerializer.Serialize(injected.ToSanitizedResponse()).Contains("test-only-injected-key"), "Injected credential must never be returned.");
    Environment.SetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.PhaseVariable, "candidate");
    Check(!new ProjectPulseAiConfiguration().DeepSeek.Enabled, "Immutable releases require explicit provider activation.");
    Environment.SetEnvironmentVariable("PROJECTPULSE_AI_DEEPSEEK_ENABLED", "true");
    var snapshot = ProjectPulseAiReleaseRuntimePolicy.Snapshot();
    Check(snapshot.Errors.Any(error => error.Contains("PROJECTPULSE_DEEPSEEK_API_KEY_SECRET_REFERENCE")), "Immutable activation must reject an unpinned key.");
}
finally
{
    Environment.SetEnvironmentVariable("PROJECTPULSE_DEEPSEEK_API_KEY", null);
    Environment.SetEnvironmentVariable("PROJECTPULSE_AI_DEEPSEEK_ENABLED", null);
    Environment.SetEnvironmentVariable(ProjectPulseAiReleaseRuntimePolicy.PhaseVariable, null);
}

// A disabled Celar host must not disable the shared evidence service used by
// the separately configured DeepSeek target. The global RAG switch still wins.
var disabledCelar = new CelarAiPrivateModelProfile(
    "test", false, "https://private.invalid/v1", "test-model", "bearer", "test-only",
    [], true, 1, DateTimeOffset.UtcNow, null, "endpoint", "token", true);
CelarAiPrivateModelRuntime.Apply(disabledCelar);
var evidenceOptions = PulseAiPrivateRagOptions.FromEnvironment() with { Enabled = true };
var appliedOptions = CelarAiPrivateModelRuntime.Apply(evidenceOptions);
Check(appliedOptions.Enabled, "Disabling Celar must not disable DeepSeek evidence execution.");
Check(!appliedOptions.InferenceConfigured && appliedOptions.InferenceBearerToken.Length == 0,
    "Disabled Celar must not remain an eligible inference fallback.");
Check(!CelarAiPrivateModelRuntime.Apply(evidenceOptions with { Enabled = false }).Enabled,
    "The deployment-level RAG disable switch must remain authoritative.");
Check(CelarAiCapabilityTargets.IsPrivate(CelarAiCapabilityTargets.DeepSeek),
    "DeepSeek document-grounded results must be eligible for adoption.");
Check(!CelarAiCapabilityTargets.IsPrivate(CelarAiCapabilityTargets.Claude),
    "Generic public assistance must not become a private evidence result.");

var healthConfig = new ProjectPulseAiConfiguration();
healthConfig.ApplyStoredSecret(ProjectPulseAiProviders.DeepSeek, "test-only", "test", DateTimeOffset.UtcNow);
healthConfig.ApplyStoredEnabled(ProjectPulseAiProviders.DeepSeek, true);
var health = new ProjectPulseAiHealthRegistry(healthConfig);
Check(health.CanAttempt(ProjectPulseAiProviders.DeepSeek, out _), "Configured DeepSeek is initially eligible.");
health.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, false, "deepseek_http_503", "Unavailable", 503, null));
Check(!health.CanAttempt(ProjectPulseAiProviders.DeepSeek, out var outageReason)
    && outageReason == "provider_circuit_open", "Failed readiness must skip generation during cooldown.");
health.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, true, "ready", "Ready", 200, null));
Check(health.CanAttempt(ProjectPulseAiProviders.DeepSeek, out _), "A successful recovery probe restores eligibility.");
health.ApplyPrivateConfiguration(disabledCelar);
Check(!health.CanAttempt(CelarAiCapabilityTargets.CelarAi, out var disabledReason)
    && disabledReason == "provider_disabled", "A disabled Celar target must be skipped before consumer execution.");
Console.WriteLine("DEEPSEEK_EVIDENCE_AND_HEALTH_REGRESSIONS=PASS");
var budgetMethod = typeof(ProjectPulseDeepSeekProvider).GetMethod("CompletionBudget", BindingFlags.Static | BindingFlags.NonPublic)!;
int Budget(int finalTokens) => (int)budgetMethod.Invoke(null, [finalTokens])!;
Check(Budget(520) > 520, "Short timesheet requests must reserve tokens for reasoning as well as final prose.");
Check(Budget(12_000) >= 12_000 && Budget(int.MaxValue) == 16_384,
    "Detailed scope retains its requested budget and oversized inputs remain bounded without integer overflow.");
var readinessConfig = new ProjectPulseAiConfiguration();
readinessConfig.ApplyStoredSecret(ProjectPulseAiProviders.DeepSeek, "test-only", "test", DateTimeOffset.UtcNow);
readinessConfig.ApplyStoredEnabled(ProjectPulseAiProviders.DeepSeek, true);
var readinessHealth = new ProjectPulseAiHealthRegistry(readinessConfig);
var readinessClient = new PulseAiPrivateModelClient(null!,
    Microsoft.Extensions.Logging.Abstractions.NullLogger<PulseAiPrivateModelClient>.Instance,
    new ProjectPulseDeepSeekProvider(null!, readinessConfig), readinessHealth, readinessConfig);
var readinessMethod = typeof(PulseAiPrivateModelClient).GetMethod("DeepSeekReadiness", BindingFlags.Instance | BindingFlags.NonPublic)!;
(bool Configured, bool Ready) Readiness() => ((bool, bool))readinessMethod.Invoke(readinessClient, null)!;
Check(Readiness() == (true, false), "Credentials alone must not claim DeepSeek runtime readiness.");
readinessHealth.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, true, "ready", "Ready", 200, null));
Check(Readiness() == (true, true), "Healthy DeepSeek must satisfy inference readiness with Celar disabled.");
readinessHealth.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, false, "deepseek_http_503", "Unavailable", 503, null));
Check(Readiness() == (true, false), "An outage must remove DeepSeek runtime readiness.");
readinessConfig.ApplyStoredEnabled(ProjectPulseAiProviders.DeepSeek, false);
Check(Readiness() == (false, false), "Disabled DeepSeek must not satisfy inference readiness.");

// Rejected content must remain rejected without turning a healthy provider into
// a global outage for unrelated consumers.
var rejectionHealth = new ProjectPulseAiHealthRegistry(healthConfig);
for (var i = 0; i < healthConfig.FailureThreshold + 1; i++)
    rejectionHealth.RecordOutputRejected(ProjectPulseAiProviders.DeepSeek, "external_output_identity_validation_failed", null);
Check(rejectionHealth.CanAttempt(ProjectPulseAiProviders.DeepSeek, out _),
    "Output rejection must not open a provider-wide availability circuit.");

var budgetType = typeof(CelarAiCapabilityRouter).Assembly.GetType("ProjectTime.Api.Ai.CelarAiRouteAttemptBudget")!;
var runBudget = budgetType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!;
Task<ProjectPulseAiProviderResult> Attempt(string target, TimeSpan timeout,
    Func<CancellationToken, Task<ProjectPulseAiProviderResult>> action, CancellationToken token = default)
    => (Task<ProjectPulseAiProviderResult>)runBudget.Invoke(null, [target, timeout, action, token])!;
var visited = new List<string>();
var timedOut = await Attempt("deepseek_v4", TimeSpan.FromMilliseconds(30), async token =>
{
    visited.Add("deepseek_v4");
    await Task.Delay(Timeout.InfiniteTimeSpan, token);
    throw new InvalidOperationException("Cancelled work must not complete.");
});
Check(timedOut.Code == "provider_deadline_exceeded" && !timedOut.IsRefusal,
    "An attempt timeout must remain eligible for next-provider fallback.");
var next = await Attempt("claude", TimeSpan.FromSeconds(1), token =>
{
    visited.Add("claude");
    return Task.FromResult(new ProjectPulseAiProviderResult("claude", ProjectPulseAiOutcomes.Success, "answer", null, null, null, null, 200));
});
Check(next.IsSuccess && visited.SequenceEqual(new[] { "deepseek_v4", "claude" }),
    "Later providers can execute after an earlier attempt times out.");
using var cancelledRequest = new CancellationTokenSource();
cancelledRequest.Cancel();
try
{
    await Attempt("deepseek_v4", TimeSpan.FromSeconds(1), _ => throw new InvalidOperationException("Do not call after user cancellation."), cancelledRequest.Token);
    Check(false, "User cancellation must terminate rather than initiate fallback.");
}
catch (OperationCanceledException) { }
var budgetRefusal = await Attempt("claude", TimeSpan.FromSeconds(1), _ => Task.FromResult(
    new ProjectPulseAiProviderResult("claude", ProjectPulseAiOutcomes.Refusal, null, "provider_safety_refusal", null, null, null, 400)));
Check(budgetRefusal.IsRefusal, "Attempt deadlines must preserve terminal safety refusals.");
var backgroundBudget = Activator.CreateInstance(budgetType, [CelarAiCapabilityCatalog.SowGsdPlanning])!;
Check(budgetType.GetMethod("NextTimeout")!.Invoke(backgroundBudget, [4]) is null,
    "Background detailed SOW generation must not inherit the interactive deadline.");
Console.WriteLine("MODULE064_ATTEMPT_DEADLINES_AND_REJECTION_ISOLATION=PASS");

// A queued readiness probe is inconclusive; it must neither cause nor heal outages.
var busyHealth = new ProjectPulseAiHealthRegistry(healthConfig);
busyHealth.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, true, "ready", "Ready", 200, null));
for (var i = 0; i < healthConfig.FailureThreshold + 1; i++)
    busyHealth.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, false, "deepseek_queue_busy", "Busy", null, null));
Check(busyHealth.CanAttempt(ProjectPulseAiProviders.DeepSeek, out _),
    "Busy readiness probes must not prevent user requests from trying DeepSeek.");
Check(busyHealth.Snapshot(ProjectPulseAiProviders.DeepSeek).ProbeStatus == "available",
    "Inconclusive probes must preserve the last verified readiness for all consumers.");
busyHealth.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, false, "deepseek_http_503", "Unavailable", 503, null));
busyHealth.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, false, "deepseek_queue_busy", "Busy", null, null));
Check(!busyHealth.CanAttempt(ProjectPulseAiProviders.DeepSeek, out _),
    "A busy probe must not heal an actual provider outage.");
busyHealth.RecordProbe(new(ProjectPulseAiProviders.DeepSeek, true, "ready", "Ready", 200, null));
Check(busyHealth.CanAttempt(ProjectPulseAiProviders.DeepSeek, out _),
    "Successful recovery must still restore eligibility after contention.");
