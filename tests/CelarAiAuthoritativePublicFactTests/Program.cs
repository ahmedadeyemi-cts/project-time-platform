using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;

var reliability = new CelarAiUniversalAnswerReliabilityService();
var officialBodies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["https://www.whitehouse.gov/administration/"] = "<html><h1>President Donald J. Trump</h1><p>President Trump</p><h2>Vice President JD Vance</h2></html>",
    ["https://rhc.jo/en/jordans-governing-system"] = "<html><p>Jordan has a parliamentary system of government with a hereditary monarchy. His Majesty the King is the head of state.</p></html>",
    ["https://rhc.jo/en/king-abdullah"] = "<html><h1>King Abdullah II</h1></html>",
    ["https://ussignal.com/why-us-signal/leadership/"] = "<html><h2>Dan Watts, Chief Executive Officer</h2></html>"
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
    "injected wrong-provider answer is rejected");
Require(wrongProvider.Answer.DirectConclusion.Contains("Donald", StringComparison.OrdinalIgnoreCase),
    "official retrieval-time answer overrides wrong-provider text");
Require(wrongProvider.Sources.All(source => source.SourceType == "authoritative_public_web"),
    "only official public web sources are promoted");

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
    "stale provider officeholder is rejected");
Require(staleProvider.Answer.CitationIds.SequenceEqual([1]),
    "verified president claim maps to the official source");

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
    "false premise is corrected");
Require(jordan.Answer.DirectConclusion.Contains("King Abdullah II", StringComparison.OrdinalIgnoreCase),
    "Jordan head of state is grounded in official sources");
Require(jordan.Sources.Count == 2 && jordan.Answer.CitationIds.Count == 2,
    "Jordan answer retains both governing-system and monarch evidence");


var usSignalPlan = reliability.Plan(
    "Who is the CEO of US Signal?",
    "general_knowledge",
    null,
    null,
    null,
    false,
    0);
var wrongUsSignalProvider = await service.VerifyAsync(
    Result(
        "completed",
        "general_knowledge",
        "Matt Rosenberg is the CEO of US Signal.",
        sources: [ProviderSource(1)],
        citationIds: [1],
        provider: CelarAiCapabilityTargets.OpenAi),
    usSignalPlan,
    "Who is the CEO of US Signal?",
    CancellationToken.None);
Require(!wrongUsSignalProvider.Answer.DirectConclusion.Contains("Matt Rosenberg", StringComparison.OrdinalIgnoreCase),
    "wrong US Signal CEO provider answer is rejected");
Require(wrongUsSignalProvider.Answer.DirectConclusion.Contains("Dan Watts", StringComparison.OrdinalIgnoreCase),
    "official US Signal leadership source establishes the CEO");
Require(wrongUsSignalProvider.Sources.Count == 1
    && wrongUsSignalProvider.Sources[0].Path.Contains("ussignal.com/why-us-signal/leadership", StringComparison.OrdinalIgnoreCase),
    "US Signal CEO answer cites the official leadership page");

var previousEnabled = Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED");
Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED", "false");
try
{
    var noRetrieval = await service.VerifyAsync(
        Result("completed", "general_knowledge", "Joe Biden is the President."),
        presidentPlan,
        "Who is the current President of the United States?",
        CancellationToken.None);
    Require(noRetrieval.Status == "partial", "disabled connector returns evidence-limited status");
    Require(noRetrieval.Answer.DirectConclusion.Contains("could not verify", StringComparison.OrdinalIgnoreCase),
        "disabled connector returns unable-to-verify text");
    Require(!noRetrieval.Answer.DirectConclusion.Contains("Joe Biden", StringComparison.OrdinalIgnoreCase),
        "disabled connector never falls back to model memory");
    Require(noRetrieval.Sources.Count == 0 && noRetrieval.Answer.Confidence == 0m,
        "disabled connector produces no evidence or confidence");
}
finally
{
    Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_CURRENT_PUBLIC_FACTS_ENABLED", previousEnabled);
}

var conflictFactory = new StubHttpClientFactory(uri => uri.Host.Contains("whitehouse", StringComparison.OrdinalIgnoreCase)
    ? Response(HttpStatusCode.OK, "<h1>President Alice Example.</h1><h1>President Bob Example.</h1>")
    : Response(HttpStatusCode.NotFound, "not found"));
var conflictService = new CelarAiAuthoritativePublicFactService(
    conflictFactory,
    NullLogger<CelarAiAuthoritativePublicFactService>.Instance);
var conflictResult = await conflictService.VerifyAsync(
    Result("completed", "general_knowledge", "A provider selected Alice."),
    presidentPlan,
    "Who is the current President of the United States?",
    CancellationToken.None);
var conflictEnforcement = reliability.Enforce(conflictResult, presidentPlan, true, true);
Require(conflictResult.Answer.Conflicts.Count > 0, "conflicting official observations are retained");
Require(!conflictEnforcement.Assessment.Passed, "source conflict blocks verification");
Require(HasFinding(conflictEnforcement, "conflicting_evidence_requires_review"),
    "source conflict produces blocker finding");

var providerOnly = reliability.Enforce(
    Result(
        "completed",
        "general_knowledge",
        "A provider returned HTTP 200. [1]",
        sources: [ProviderSource(1, "live_retrieved_current")],
        citationIds: [1],
        provider: CelarAiCapabilityTargets.OpenAi),
    presidentPlan,
    true,
    true);
Require(providerOnly.Assessment.SuccessfulAuthoritativeSources == 0,
    "provider HTTP 200 does not count as evidence");
Require(!providerOnly.Assessment.Passed, "provider-only current fact fails closed");

var uncitedOfficial = reliability.Enforce(
    Result(
        "completed",
        "general_knowledge",
        "Official answer without an inline source token.",
        sources: [OfficialSource(1)],
        citationIds: [1]),
    presidentPlan,
    true,
    true);
Require(!uncitedOfficial.Assessment.Passed, "uncited material claim fails");
Require(HasFinding(uncitedOfficial, "material_claim_citation_support_missing"),
    "claim-level citation support finding exists");

var verifiedOfficial = reliability.Enforce(wrongProvider, presidentPlan, true, true);
Require(verifiedOfficial.Assessment.Passed, "retrieval-time official cited claim passes");
Require(verifiedOfficial.Assessment.CurrentPublicEvidenceVerified,
    "official current evidence is marked verified");

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
    "public-fact service never enters the internal-data path");
var internalEnforcement = reliability.Enforce(unchangedInternal, internalPlan, true, true);
Require(!internalEnforcement.Assessment.Passed, "model memory cannot establish an internal project count");
Require(HasFinding(internalEnforcement, "external_model_cannot_establish_internal_fact"),
    "internal-data boundary finding exists");

Require(factory.Requests.Count >= 4, "official retrieval requests were recorded");
Require(factory.Requests.All(request => request.Method == HttpMethod.Get),
    "authoritative connector performs GET-only retrieval");
Require(factory.Requests.All(request => request.ContentLength == 0),
    "authoritative connector sends no question, private record, or attachment body");
Require(factory.Requests.All(request => !request.HasAuthorization),
    "authoritative connector sends no Pulse or provider authorization header");
Require(factory.Requests.All(request => request.Host is "www.whitehouse.gov" or "whitehouse.gov" or "rhc.jo" or "www.rhc.jo" or "ussignal.com" or "www.ussignal.com"),
    "authoritative connector is limited to official allowlisted hosts");

Console.WriteLine("CELAR_AI_WRONG_PROVIDER_TEST=PASS");
Console.WriteLine("CELAR_AI_STALE_PRESIDENT_TEST=PASS");
Console.WriteLine("CELAR_AI_US_SIGNAL_CEO_TEST=PASS");
Console.WriteLine("CELAR_AI_FALSE_PREMISE_TEST=PASS");
Console.WriteLine("CELAR_AI_NO_RETRIEVAL_TEST=PASS");
Console.WriteLine("CELAR_AI_SOURCE_CONFLICT_TEST=PASS");
Console.WriteLine("CELAR_AI_INTERNAL_DATA_ISOLATION_TEST=PASS");
Console.WriteLine("CELAR_AI_SOURCE_COUNT_TEST=PASS");
Console.WriteLine("CELAR_AI_CITATION_SUPPORT_TEST=PASS");
Console.WriteLine("CELAR_AI_PUBLIC_RETRIEVAL_PRIVACY_TEST=PASS");
Console.WriteLine("CELAR_AI_AUTHORITATIVE_PUBLIC_FACT_TESTS=PASS");

static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
{
    Content = new StringContent(body, Encoding.UTF8, "text/html")
};

static PulseAiSystemSourceEvidence ProviderSource(int id, string freshness = "provider_knowledge_not_live_web_verified") =>
    new(id, "governed_public_ai", "provider", "Provider response", "064", "INTERNAL",
        "module064:public-general-knowledge", "succeeded", 200, DateTimeOffset.UtcNow,
        freshness, "Provider execution only");

static PulseAiSystemSourceEvidence OfficialSource(int id) =>
    new(id, "authoritative_public_web", "official", "Official current source", "011", "GET",
        "https://www.whitehouse.gov/administration/", "succeeded", 200, DateTimeOffset.UtcNow,
        "live_retrieved_current", "Official retrieval-time evidence");

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

static bool HasFinding(CelarAiUniversalAnswerEnforcement enforcement, string code) =>
    enforcement.Assessment.Findings.Any(finding => finding.Code == code);

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
