using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;

var reliability = new CelarAiUniversalAnswerReliabilityService();
var officialBodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["https://www.whitehouse.gov/administration/"] = "<html><h1>President Donald J. Trump</h1></html>",
    ["https://rhc.jo/en/jordans-governing-system"] = "<html><p>Jordan has a parliamentary system of government with a hereditary monarchy. His Majesty the King is the head of state.</p></html>",
    ["https://rhc.jo/en/king-abdullah"] = "<html><h1>King Abdullah II</h1></html>"
};
var factory = new StubHttpClientFactory(uri => officialBodies.TryGetValue(uri.ToString(), out var body)
    ? Response(HttpStatusCode.OK, body)
    : Response(HttpStatusCode.NotFound, "not found"));
var service = new CelarAiAuthoritativePublicFactService(
    factory,
    NullLogger<CelarAiAuthoritativePublicFactService>.Instance);

var presidentPlan = reliability.Plan(
    "Who is the current President of the United States?",
    "general_knowledge",
    null,
    null,
    null,
    false,
    0);
Require(presidentPlan.QuestionClass == CelarAiAnswerQuestionClass.PublicCurrent,
    "president question is classified as public-current");

var wrongProvider = await service.VerifyAsync(
    Result(
        "completed",
        "general_knowledge",
        "Matt Rosenberg is the President of the United States.",
        sources: [ProviderSource(1)],
        citationIds: [1],
        provider: CelarAiCapabilityTargets.OpenAi),
    presidentPlan,
    "Who is the current President of the United States?",
    CancellationToken.None);
Require(!wrongProvider.Answer.DirectConclusion.Contains("Matt Rosenberg", StringComparison.OrdinalIgnoreCase),
    "an incorrect provider answer is rejected");
Require(wrongProvider.Answer.DirectConclusion.Contains("Donald", StringComparison.OrdinalIgnoreCase),
    "the retrieval-time official answer replaces provider text");
Require(wrongProvider.Sources.Count == 1
        && wrongProvider.Sources.All(source => source.SourceType == "authoritative_public_web"),
    "only the official public source is promoted");
Require(wrongProvider.Answer.CitationIds.SequenceEqual([1])
        && wrongProvider.Answer.DirectConclusion.Contains("[1]", StringComparison.Ordinal),
    "the material current claim maps to the official source");
Require(wrongProvider.Answer.Confidence == 1m,
    "verified current-public confidence is derived from official evidence rather than provider confidence");

var staleProvider = await service.VerifyAsync(
    Result(
        "completed",
        "general_knowledge",
        "Joe Biden is the President of the United States.",
        sources: [ProviderSource(1)],
        citationIds: [1],
        provider: CelarAiCapabilityTargets.Claude),
    presidentPlan,
    "Who is the current President of the United States?",
    CancellationToken.None);
Require(!staleProvider.Answer.DirectConclusion.Contains("Joe Biden", StringComparison.OrdinalIgnoreCase),
    "a stale provider officeholder is rejected");

var jordanPlan = reliability.Plan(
    "Who is the president of Jordan?",
    "general_knowledge",
    null,
    null,
    null,
    false,
    0);
var jordan = await service.VerifyAsync(
    Result("completed", "general_knowledge", "A provider guessed an officeholder."),
    jordanPlan,
    "Who is the president of Jordan?",
    CancellationToken.None);
Require(jordan.Answer.DirectConclusion.Contains("does not have a president", StringComparison.OrdinalIgnoreCase),
    "the false premise is corrected");
Require(jordan.Answer.DirectConclusion.Contains("King Abdullah II", StringComparison.OrdinalIgnoreCase)
        && jordan.Sources.Count == 2,
    "Jordan's current head of state is grounded in both official sources");

var previousEnabled = Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED");
Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED", "false");
try
{
    var noRetrieval = await service.VerifyAsync(
        Result("completed", "general_knowledge", "Joe Biden is the President."),
        presidentPlan,
        "Who is the current President of the United States?",
        CancellationToken.None);
    Require(noRetrieval.Status == "partial"
            && noRetrieval.Answer.DirectConclusion.Contains("could not verify", StringComparison.OrdinalIgnoreCase),
        "a disabled or unavailable connector fails closed");
    Require(noRetrieval.Sources.Count == 0 && noRetrieval.Answer.Confidence == 0m,
        "failed verification returns no promoted source or model confidence");
}
finally
{
    Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED", previousEnabled);
}

var internalPlan = reliability.Plan(
    "How many active projects am I assigned to?",
    "projects_and_delivery",
    null,
    null,
    null,
    false,
    0);
var internalResult = Result(
    "completed",
    "projects_and_delivery",
    "A public model guessed twelve active projects.",
    sources: [ProviderSource(1)],
    citationIds: [1],
    provider: CelarAiCapabilityTargets.Claude);
var unchangedInternal = await service.VerifyAsync(
    internalResult,
    internalPlan,
    "How many active projects am I assigned to?",
    CancellationToken.None);
Require(ReferenceEquals(internalResult, unchangedInternal),
    "the public-fact connector never enters an internal-data path");

Require(factory.Requests.Count >= 3, "official retrieval requests were recorded");
Require(factory.Requests.All(request => request.Method == HttpMethod.Get),
    "the connector performs GET-only retrieval");
Require(factory.Requests.All(request => request.ContentLength == 0),
    "the connector sends no question, record, or attachment body");
Require(factory.Requests.All(request => !request.HasAuthorization),
    "the connector sends no Pulse or provider authorization header");
Require(factory.Requests.All(request => request.Host is "www.whitehouse.gov" or "whitehouse.gov" or "rhc.jo" or "www.rhc.jo"),
    "the connector is limited to the server-owned official host allowlist");

Console.WriteLine("CELAR_AI_AUTHORITATIVE_PUBLIC_FACT_TESTS=PASS");

static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
{
    Content = new StringContent(body, Encoding.UTF8, "text/html")
};

static PulseAiSystemSourceEvidence ProviderSource(int id) =>
    new(id, "governed_public_ai", "provider", "Provider response", "064", "INTERNAL",
        "module064:public-general-knowledge", "succeeded", 200, DateTimeOffset.UtcNow,
        "provider_knowledge_not_live_web_verified", "Provider execution only");

static PulseAiSystemQuestionResult Result(
    string status,
    string intent,
    string conclusion,
    IReadOnlyList<PulseAiSystemSourceEvidence>? sources = null,
    IReadOnlyList<int>? citationIds = null,
    string provider = "celar_ai")
{
    var now = DateTimeOffset.UtcNow;
    return new PulseAiSystemQuestionResult(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, intent,
        "comprehensive",
        new PulseAiSystemDetailedAnswer(
            conclusion, conclusion, [], [conclusion], [conclusion], [], [], [], [], [], [], [], [], [], [], [],
            null, [], citationIds ?? [], 0.72m, "Provider returned a response.", now),
        sources ?? [], [], [], provider, provider, Guid.NewGuid().ToString("N"), [], false);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException($"FAILED: {message}");
    Console.WriteLine($"PASS: {message}");
}

sealed record RequestRecord(HttpMethod Method, string Host, long ContentLength, bool HasAuthorization);

sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Func<Uri, HttpResponseMessage> _response;
    public List<RequestRecord> Requests { get; } = [];

    public StubHttpClientFactory(Func<Uri, HttpResponseMessage> response) => _response = response;

    public HttpClient CreateClient(string name) => new(new StubHandler(request =>
    {
        var contentLength = request.Content?.Headers.ContentLength ?? 0;
        Requests.Add(new RequestRecord(
            request.Method,
            request.RequestUri?.Host ?? string.Empty,
            contentLength,
            request.Headers.Authorization is not null));
        var response = _response(request.RequestUri ?? new Uri("https://invalid.example"));
        response.RequestMessage = request;
        return response;
    }), disposeHandler: true);
}

sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(_handler(request));
}
