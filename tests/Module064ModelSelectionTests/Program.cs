using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

var checks = 0;
void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; }
var configuration = new ProjectPulseAiConfiguration();
configuration.ApplyStoredSecret("gemini", "fixture-key", "test", DateTimeOffset.UtcNow);
configuration.ApplyStoredEnabled("gemini", true);
var originalModel = configuration.Provider("gemini").Model;
const string candidate = "gemini-future-flash";
var handler = new Handler(configuration, originalModel);
var factory = new Factory(handler);
var health = new ProjectPulseAiHealthRegistry(configuration);
using var store = new ProjectPulseAiSecretStore(NullLogger<ProjectPulseAiSecretStore>.Instance);
using var services = new ServiceCollection()
    .AddSingleton(new ProjectPulseAiModelCatalog(factory, configuration))
    .AddSingleton(new ProjectPulseGeminiProvider(factory, configuration)).BuildServiceProvider();
var context = new DefaultHttpContext { RequestServices = services };
var method = typeof(AiProviderConfigurationModule).GetMethod("ReplaceGeminiModelAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
async Task<IResult> Select(string model) => await (Task<IResult>)method.Invoke(null,
    [model, context, configuration, store, health, CancellationToken.None])!;

var invalid = await Select("gemini-future-flash\n");
Check(((IStatusCodeHttpResult)invalid).StatusCode == 400 && handler.Requests == 0, "Unsafe model never reaches provider");
var unavailable = await Select("gemini-not-in-catalog");
Check(((IStatusCodeHttpResult)unavailable).StatusCode == 400 && handler.GenerationRequests == 0, "Unlisted model never reaches inference");
var failed = await Select(candidate);
Check(((IStatusCodeHttpResult)failed).StatusCode == 400, "Failed candidate returns actionable error");
Check(configuration.Provider("gemini").Model == originalModel, "Failed candidate never replaces active model");
Check(handler.SawIsolatedCandidate, "Provider receives selected candidate while live config stays unchanged");
Check(health.Snapshot("gemini").RetryAfterUtc > DateTimeOffset.UtcNow, "Candidate quota failure imposes provider cooldown");
Check(health.Snapshot("gemini").ProbeFailureCount == 0, "Candidate failure is not attributed to active model");
var calls = handler.Requests;
var limited = await Select(candidate);
Check(((IStatusCodeHttpResult)limited).StatusCode == 429 && handler.Requests == calls, "Retry during quota cooldown makes no request");

var get = typeof(AiProviderConfigurationModule).GetMethod("GetModelsAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
var unauthorized = await (Task<IResult>)get.Invoke(null,
    ["gemini", context, services.GetRequiredService<ProjectPulseAiModelCatalog>(), CancellationToken.None])!;
Check(((IStatusCodeHttpResult)unauthorized).StatusCode == 401, "Anonymous model discovery denied");
Check(handler.Requests == calls, "Anonymous request does not reach provider");

configuration.ApplyStoredSecret("gemini", "second-key", "test-2", DateTimeOffset.UtcNow);
health.ApplyConfiguration(configuration.Provider("gemini"));
handler.BeforeGenerationResponse = () =>
{
    configuration.ApplyStoredSecret("gemini", "third-key", "test-3", DateTimeOffset.UtcNow);
    health.ApplyConfiguration(configuration.Provider("gemini"));
};
var rotated = await Select(candidate);
Check(((IStatusCodeHttpResult)rotated).StatusCode == 409, "Credential rotation during candidate test rejects stale result");
Check(health.Snapshot("gemini").RetryAfterUtc is null, "Old-key candidate cannot impose cooldown on new key");
Check(configuration.Provider("gemini").Model == originalModel, "Rotation conflict preserves active model");

var beforeActivation = configuration.Provider("gemini");
configuration.ApplyStoredSecret("gemini", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)), "test-4", DateTimeOffset.UtcNow);
Check(!configuration.TryApplyVerifiedModel(beforeActivation, candidate), "Local rotation after database save rejects stale activation");
Check(configuration.Provider("gemini").Model == originalModel, "Failed activation compare preserves active model");
var sameKeyNewVersion = configuration.Provider("gemini");
configuration.ApplyStoredSecret("gemini", sameKeyNewVersion.ApiKey!, "test-5", DateTimeOffset.UtcNow);
Check(!configuration.TryApplyVerifiedModel(sameKeyNewVersion, candidate), "A new credential version also invalidates activation when key text is unchanged");
var beforeDisable = configuration.Provider("gemini");
configuration.ApplyStoredEnabled("gemini", false);
Check(!configuration.TryApplyVerifiedModel(beforeDisable, candidate), "Concurrent disabling cannot be overwritten by model activation");
Check(configuration.TryApplyVerifiedModel(configuration.Provider("gemini"), candidate)
    && configuration.Provider("gemini").Model == candidate && !configuration.Provider("gemini").Enabled,
    "Matching model activation succeeds without enabling the provider");

var priorEnvironmentKey = Environment.GetEnvironmentVariable("PROJECTPULSE_GEMINI_API_KEY");
try
{
    Environment.SetEnvironmentVariable("PROJECTPULSE_GEMINI_API_KEY", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
    var environmentConfiguration = new ProjectPulseAiConfiguration();
    var requestCount = handler.Requests;
    var environmentOnly = await (Task<IResult>)method.Invoke(null, [candidate, context, environmentConfiguration,
        store, new ProjectPulseAiHealthRegistry(environmentConfiguration), CancellationToken.None])!;
    Check(((IStatusCodeHttpResult)environmentOnly).StatusCode == 400 && handler.Requests == requestCount,
        "Environment-only credentials require a shared saved version before inference or activation");
}
finally { Environment.SetEnvironmentVariable("PROJECTPULSE_GEMINI_API_KEY", priorEnvironmentKey); }
Console.WriteLine($"{checks} model-selection checks passed");

sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, false);
}
sealed class Handler(ProjectPulseAiConfiguration configuration, string originalModel) : HttpMessageHandler
{
    public int Requests, GenerationRequests;
    public bool SawIsolatedCandidate;
    public Action? BeforeGenerationResponse;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (request.Method == HttpMethod.Get)
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"models":[{"name":"models/gemini-future-flash","supportedGenerationMethods":["generateContent"]}]}""", Encoding.UTF8, "application/json") };
        GenerationRequests++;
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        SawIsolatedCandidate = body.Contains("gemini-future-flash") && configuration.Provider("gemini").Model == originalModel;
        BeforeGenerationResponse?.Invoke();
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("""{"error":{"code":429,"status":"RESOURCE_EXHAUSTED"}}""", Encoding.UTF8, "application/json")
        };
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
        return response;
    }
}
