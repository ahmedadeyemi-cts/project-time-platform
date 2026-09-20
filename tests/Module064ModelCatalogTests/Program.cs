using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ProjectTime.Api.Ai;

var assertions = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    assertions++;
}

const string TestKey = "test-only-catalog-key-not-a-real-credential";
var originalKey = Environment.GetEnvironmentVariable("PROJECTPULSE_GEMINI_API_KEY");
var originalAllowlist = Environment.GetEnvironmentVariable("PROJECTPULSE_GEMINI_APPROVED_MODELS");
Environment.SetEnvironmentVariable("PROJECTPULSE_GEMINI_API_KEY", null);
Environment.SetEnvironmentVariable("PROJECTPULSE_GEMINI_APPROVED_MODELS", null);
try
{
    ProjectPulseAiConfiguration Config()
    {
        var value = new ProjectPulseAiConfiguration();
        value.ApplyStoredSecret(ProjectPulseAiProviders.Gemini, TestKey, "fixture", DateTimeOffset.UtcNow);
        return value;
    }

    object Model(string id, string[]? methods = null) => new
    {
        name = "models/" + id,
        displayName = id + " label",
        inputTokenLimit = 1048576,
        outputTokenLimit = 65536,
        supportedGenerationMethods = methods ?? ["generateContent", "countTokens"]
    };
    string Page(object[] models, string? next = null) => next is null
        ? JsonSerializer.Serialize(new { models })
        : JsonSerializer.Serialize(new { models, nextPageToken = next });

    var clock = new TestClock();
    var handler = new ScriptedHandler();
    handler.Json(Page([
        Model("gemini-next-pro"), Model("gemini-next-flash"),
        Model("gemini-next-embedding"), Model("gemini-next-live"),
        Model("gemini-next-image"), Model("gemini-next-tts"), Model("gemini-next-native-audio"),
        Model("gemini-not-generative", ["embedContent"]), Model("gemma-text"),
        Model("gemini-next-flash\n"), Model("gemini-../../escape")], "next/+?&pageSize=999"));
    handler.Json(Page([Model("gemini-next-flash-lite"), Model("gemini-next-flash")]));
    var configuration = Config();
    var initialModel = configuration.Provider(ProjectPulseAiProviders.Gemini).Model;
    var catalog = new ProjectPulseAiModelCatalog(new TestClients(handler), configuration, clock);
    var result = await catalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.Status == "available" && handler.Calls.Count == 2, "Discovery follows every model page.");
    Check(result.Models.Select(model => model.Id).SequenceEqual(new[] { "gemini-next-flash-lite", "gemini-next-flash", "gemini-next-pro" }),
        "Discovery deduplicates compatible text models without a pinned version list.");
    Check(result.Models.All(model => model.InputTokenLimit == 1048576 && model.OutputTokenLimit == 65536), "Token limits are returned from provider metadata.");
    Check(result.Models.All(model => model.PricingUrl == ProjectPulseAiModelCatalog.GeminiPricingUrl && model.CostGuidance.Contains("does not include prices")),
        "Model discovery does not fabricate prices.");
    Check(handler.Calls.All(call => call.Method == HttpMethod.Get && call.Key == TestKey && !call.Uri.Contains(TestKey)),
        "Discovery uses GET and keeps credentials exclusively in the key header.");
    Check(handler.Calls[1].Uri.Contains("pageToken=" + Uri.EscapeDataString("next/+?&pageSize=999"), StringComparison.OrdinalIgnoreCase),
        "Pagination tokens cannot inject query parameters.");
    Check(configuration.Provider(ProjectPulseAiProviders.Gemini).Model == initialModel && result.ActiveModel == initialModel,
        "Reading the catalog never activates a different model.");
    await catalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    await catalog.GetAsync(ProjectPulseAiProviders.Gemini, true, default);
    Check(handler.Calls.Count == 2, "Automatic loads and immediate forced refresh reuse the cache.");
    var parallel = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => catalog.GetAsync(ProjectPulseAiProviders.Gemini, true, default)));
    Check(handler.Calls.Count == 2 && parallel.All(item => item.Status == "available"), "Concurrent refreshes do not create a request storm.");
    Check(await catalog.IsAvailableAsync(ProjectPulseAiProviders.Gemini, "gemini-next-flash-lite", default), "Selection can use a freshly discovered compatible model.");
    Check(!await catalog.IsAvailableAsync(ProjectPulseAiProviders.Gemini, "gemini-made-up", default), "Unlisted model selections are rejected.");
    Check(!await catalog.IsAvailableAsync(ProjectPulseAiProviders.Gemini, "gemini-next-tts", default), "Special-purpose models cannot be selected as text models.");
    configuration.ApplyStoredModel(ProjectPulseAiProviders.Gemini, "gemini-next-flash-lite");
    result = await catalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.ActiveModel == "gemini-next-flash-lite" && handler.Calls.Count == 2, "Cached results show the latest selected model.");
    clock.Advance(TimeSpan.FromSeconds(31));
    handler.Json(Page([Model("gemini-new-flash")]));
    result = await catalog.GetAsync(ProjectPulseAiProviders.Gemini, true, default);
    Check(result.Models.Single().Id == "gemini-new-flash" && handler.Calls.Count == 3, "Explicit refresh updates the catalog after its minimum interval.");
    clock.Advance(TimeSpan.FromMinutes(4));
    await catalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(handler.Calls.Count == 3, "Successful inventory is cached for five minutes.");
    clock.Advance(TimeSpan.FromMinutes(2));
    handler.Json(Page([Model("gemini-renewed-flash")]));
    result = await catalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.Models.Single().Id == "gemini-renewed-flash" && handler.Calls.Count == 4, "Expired inventory is renewed automatically.");
    configuration.ApplyStoredSecret(ProjectPulseAiProviders.Gemini, "test-only-rotated-key", "rotated", clock.GetUtcNow());
    handler.Json(Page([Model("gemini-rotated-flash")]));
    result = await catalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.Models.Single().Id == "gemini-rotated-flash" && handler.Calls.Last().Key == "test-only-rotated-key",
        "Rotating a key invalidates the previous credential's cached inventory.");
    var midFlightConfig = Config();
    var midFlightHandler = new ScriptedHandler();
    midFlightHandler.Add(_ =>
    {
        midFlightConfig.ApplyStoredSecret(ProjectPulseAiProviders.Gemini, "test-only-new-key", "changed", clock.GetUtcNow());
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page([Model("gemini-obsolete-flash")])) };
    });
    result = await new ProjectPulseAiModelCatalog(new TestClients(midFlightHandler), midFlightConfig, clock).GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.Diagnostic == "gemini_model_catalog_credential_changed" && result.Models.Count == 0,
        "A credential changed during discovery cannot expose the previous credential's inventory.");

    var endpointMethod = typeof(ProjectPulseAiModelCatalog).GetMethod("IsSupportedEndpoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
    bool EndpointPermitted(string endpoint) => (bool)endpointMethod.Invoke(null, [endpoint])!;
    Check(EndpointPermitted("https://generativelanguage.googleapis.com/v1beta/openai"), "Discovery accepts the Gemini compatibility endpoint.");
    Check(new[] { "http://generativelanguage.googleapis.com/v1beta/openai", "https://untrusted.invalid/v1beta/openai",
        "https://generativelanguage.googleapis.com:444/v1beta/openai", "https://user:secret@generativelanguage.googleapis.com/v1beta/openai",
        "https://generativelanguage.googleapis.com/v1beta/openai?key=unsafe", "https://generativelanguage.googleapis.com/v1beta/openai#fragment" }
        .All(endpoint => !EndpointPermitted(endpoint)), "Untrusted endpoints, credentials in URLs, extra ports and fragments are rejected.");

    var missingHandler = new ScriptedHandler();
    var missingCatalog = new ProjectPulseAiModelCatalog(new TestClients(missingHandler), new ProjectPulseAiConfiguration(), clock);
    Check((await missingCatalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default)).Status == "not_configured"
        && missingHandler.Calls.Count == 0, "Missing credentials do not initiate discovery.");
    Check((await catalog.GetAsync(ProjectPulseAiProviders.Claude, false, default)).Status == "unsupported", "Providers without a discovery adapter are explicit.");

    var limitedHandler = new ScriptedHandler();
    limitedHandler.Add(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("secret-leak-sentinel " + TestKey) };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
        return response;
    });
    var limitedClock = new TestClock();
    var limitedCatalog = new ProjectPulseAiModelCatalog(new TestClients(limitedHandler), Config(), limitedClock);
    result = await limitedCatalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.Status == "unavailable" && result.Diagnostic == "gemini_model_catalog_http_429" && result.Message.Contains("quota"),
        "Rate limiting returns an actionable, separate discovery failure.");
    Check(!JsonSerializer.Serialize(result).Contains("secret-leak-sentinel") && !JsonSerializer.Serialize(result).Contains(TestKey),
        "Provider error bodies and credentials are never exposed by discovery.");
    limitedClock.Advance(TimeSpan.FromSeconds(31));
    await limitedCatalog.GetAsync(ProjectPulseAiProviders.Gemini, true, default);
    Check(limitedHandler.Calls.Count == 1, "Refresh honors the provider's longer Retry-After cooldown.");
    limitedClock.Advance(TimeSpan.FromSeconds(90));
    limitedHandler.Json(Page([Model("gemini-restored-flash")]));
    Check((await limitedCatalog.GetAsync(ProjectPulseAiProviders.Gemini, true, default)).Status == "available", "Discovery resumes after the quota cooldown.");

    async Task<ProjectPulseAiModelCatalogResult> Failure(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var fake = new ScriptedHandler();
        fake.Json(json, status);
        return await new ProjectPulseAiModelCatalog(new TestClients(fake), Config(), clock).GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    }
    Check((await Failure("{invalid")).Diagnostic == "gemini_model_catalog_invalid_response", "Malformed provider JSON fails closed.");
    Check((await Failure("{}")).Diagnostic == "gemini_model_catalog_invalid_response", "Missing model arrays are not advertised as a successful inventory.");
    Check((await Failure("{\"models\":[],\"nextPageToken\":42}")).Diagnostic == "gemini_model_catalog_invalid_response", "Malformed pagination fails closed.");
    Check((await Failure("denied", HttpStatusCode.Forbidden)).Diagnostic == "gemini_model_catalog_http_403", "Permission failures have stable safe diagnostics.");
    Check((await Failure(new string('x', 1_048_577))).Diagnostic == "gemini_model_catalog_response_too_large", "Oversized catalog responses are bounded.");
    var repeated = new ScriptedHandler();
    repeated.Json(Page([Model("gemini-next-pro")], "repeat"));
    repeated.Json(Page([Model("gemini-next-flash")], "repeat"));
    result = await new ProjectPulseAiModelCatalog(new TestClients(repeated), Config(), clock).GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.Diagnostic == "gemini_model_catalog_invalid_pagination" && result.Models.Count == 0 && repeated.Calls.Count == 2,
        "Repeated pagination cannot loop or expose a partial catalog.");
    var redirect = new ScriptedHandler();
    redirect.Add(_ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://untrusted.invalid/models") } });
    result = await new ProjectPulseAiModelCatalog(new TestClients(redirect), Config(), clock).GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.Diagnostic == "gemini_model_catalog_redirect_rejected" && redirect.Calls.Count == 1, "Redirects are rejected without a follow-up request.");
    var changedLocation = new ScriptedHandler();
    changedLocation.Add(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(Page([Model("gemini-next-pro")])),
        RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://untrusted.invalid/models")
    });
    Check((await new ProjectPulseAiModelCatalog(new TestClients(changedLocation), Config(), clock).GetAsync(ProjectPulseAiProviders.Gemini, false, default)).Diagnostic
        == "gemini_model_catalog_redirect_rejected", "A client reporting a changed final URL is rejected.");
    var timeout = new ScriptedHandler();
    timeout.Add(_ => throw new OperationCanceledException());
    Check((await new ProjectPulseAiModelCatalog(new TestClients(timeout), Config(), clock).GetAsync(ProjectPulseAiProviders.Gemini, false, default)).Diagnostic
        == "gemini_model_catalog_timeout", "Discovery timeout is explicit and does not activate a model.");
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    var cancelled = false;
    try { await catalog.GetAsync(ProjectPulseAiProviders.Gemini, false, canceled.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "Caller cancellation propagates rather than becoming provider degradation.");

    // Configuration hydration on a new process must retain a verified discovery
    // choice while deployment restrictions still constrain both discovery/save.
    var restarted = new ProjectPulseAiConfiguration();
    restarted.ApplyStoredModel(ProjectPulseAiProviders.Gemini, "gemini-newly-discovered-flash");
    Check(restarted.Provider(ProjectPulseAiProviders.Gemini).Model == "gemini-newly-discovered-flash"
        && restarted.Provider(ProjectPulseAiProviders.Gemini).ApprovedModels.Contains("gemini-newly-discovered-flash"),
        "A persisted discovered Gemini model survives configuration hydration.");
    Check(!ProjectPulseAiConfiguration.GeminiModelPermitted("gemini-valid\n")
        && !ProjectPulseAiModelCatalog.IsGeminiTextModelId("gemini-valid\n"), "Model identifiers cannot contain trailing control characters.");
    Environment.SetEnvironmentVariable("PROJECTPULSE_GEMINI_APPROVED_MODELS", "gemini-allowed-flash");
    var restricted = Config();
    var allowlistedHandler = new ScriptedHandler();
    allowlistedHandler.Json(Page([Model("gemini-allowed-flash"), Model("gemini-newly-discovered-flash")]));
    var restrictedCatalog = new ProjectPulseAiModelCatalog(new TestClients(allowlistedHandler), restricted, clock);
    result = await restrictedCatalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default);
    Check(result.Models.Select(model => model.Id).SequenceEqual(new[] { "gemini-allowed-flash" }), "Explicit deployment allowlists constrain discovered choices.");
    Check(!await restrictedCatalog.IsAvailableAsync(ProjectPulseAiProviders.Gemini, "gemini-newly-discovered-flash", default),
        "Selection validation rejects models excluded by deployment policy.");
    var blockedDefault = false;
    try { restricted.ApplyStoredModel(ProjectPulseAiProviders.Gemini, initialModel); }
    catch (ArgumentException) { blockedDefault = true; }
    Check(blockedDefault, "Bootstrap defaults cannot bypass an explicit deployment allowlist.");
    Environment.SetEnvironmentVariable("PROJECTPULSE_GEMINI_APPROVED_MODELS", "gemini-different-allowed");
    Check((await restrictedCatalog.GetAsync(ProjectPulseAiProviders.Gemini, false, default)).Models.Count == 0,
        "A tightened deployment allowlist also constrains cached inventory.");
    Console.WriteLine($"MODULE064_MODEL_CATALOG_TESTS=PASS assertions={assertions}");
}
finally
{
    Environment.SetEnvironmentVariable("PROJECTPULSE_GEMINI_API_KEY", originalKey);
    Environment.SetEnvironmentVariable("PROJECTPULSE_GEMINI_APPROVED_MODELS", originalAllowlist);
}

sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.Parse("2026-09-20T12:00:00Z");
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan value) => _now += value;
}

sealed class TestClients(ScriptedHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        if (name != ProjectPulseAiModelCatalog.ClientName) throw new InvalidOperationException("Discovery must use its redirect-disabled named client.");
        return new HttpClient(handler, disposeHandler: false);
    }
}

sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<(HttpMethod Method, string Uri, string Key)> Calls { get; } = [];
    public void Json(string value, HttpStatusCode status = HttpStatusCode.OK) => Add(_ => new HttpResponseMessage(status)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") });
    public void Add(Func<HttpRequestMessage, HttpResponseMessage> response) => _responses.Enqueue(response);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((request.Method, request.RequestUri!.AbsoluteUri, request.Headers.GetValues("x-goog-api-key").Single()));
        if (_responses.Count == 0) throw new InvalidOperationException("Unexpected duplicate discovery request.");
        var response = _responses.Dequeue()(request);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }
}
