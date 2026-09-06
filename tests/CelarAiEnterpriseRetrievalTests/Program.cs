using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;

static void Require(bool value, string description)
{
    if (!value) throw new InvalidOperationException(description);
    Console.WriteLine($"PASS: {description}");
}

var cases = new Dictionary<string, string[]>
{
    ["Show our customer projects and their risks"] = ["enterprise_project_portfolio"],
    ["Show my timesheet for the week of 2026-09-06"] = ["enterprise_weekly_lines", "enterprise_work_queue"],
    ["Which approvals are submitted this week?"] = ["enterprise_approvals"],
    ["What is my team's capacity and utilization?"] = ["enterprise_capacity", "enterprise_project_portfolio"],
    ["Compare our project budget to the SOW"] = ["enterprise_project_portfolio", "enterprise_financials"],
    ["What prepaid contract balances do our customers have?"] = ["enterprise_contracts", "enterprise_project_portfolio"],
    ["Show our invoice and expense billing status"] = ["enterprise_billing"],
    ["Show our commercial pipeline opportunities"] = ["enterprise_opportunities"],
    ["Who changed this project? Show audit history"] = ["enterprise_audit", "enterprise_project_portfolio"],
    ["What is my identity in Pulse?"] = ["enterprise_identity"]
};
foreach (var (question, expected) in cases)
{
    var plan = PulseAiSystemKnowledgeCatalog.Analyze(question);
    Require(plan.IntentCode != "general_knowledge", $"Internal interpretation: {question}");
    var selected = CelarAiEnterpriseEvidenceCatalog.Select(question, plan, "America/Chicago");
    Require(expected.All(code => selected.Any(tool => tool.Code == code)), $"Executable adapters: {question}");
    Require(selected.All(tool => (tool.Method == "GET" || tool.Method == "INTERNAL") && tool.SafeReadOnly), "Only registered read adapters selected");
}
var publicQuestion = "Explain photosynthesis.";
foreach(var procedure in new[] {"How do I submit my timesheet?", "How can I approve time for last month?", "Where can I see invoices?"})
{
    Require(CelarAiEnterpriseEvidenceCatalog.Select(procedure,PulseAiSystemKnowledgeCatalog.Analyze(procedure)).Count==0,
        "Procedure guidance does not require business-record retrieval");
    Require(!CelarAiEnterpriseEvidenceCatalog.NeedsPeriodClarification(procedure),
        "Procedure guidance is not blocked by date-range requirements");
}
Require(!CelarAiEnterpriseEvidencePolicy.UseDocumentRag(false,"product_help",2),
    "Retrieved business records use structured synthesis instead of the data-free procedure fallback");
Require(CelarAiEnterpriseEvidenceCatalog.Select("How can we reduce our project costs?",PulseAiSystemKnowledgeCatalog.Analyze("How can we reduce our project costs?")).Any(tool=>tool.Code=="enterprise_financials"),
    "Business analysis phrased as how can we still retrieves current financial evidence");
Require(CelarAiEnterpriseEvidencePolicy.UseDocumentRag(true,"product_help",2),
    "Explicit mixed document/database questions retain private RAG with structured evidence");
Require(CelarAiEnterpriseEvidencePolicy.UseDocumentRag(false,"product_help",0),
    "Procedure-only questions retain existing Help RAG behavior");
Require(CelarAiEnterpriseEvidenceCatalog.Select("Show my timesheet",PulseAiSystemKnowledgeCatalog.Analyze("Show my timesheet"))
    .All(tool=>tool.Code!="enterprise_own_time"),"Unqualified timesheet status uses the declared weekly snapshot without an unnecessary date-range read");
Require(CelarAiInternalDataService.ParseQuestion("What is my team working on?") is null,
    "Team request reaches enterprise planner rather than the single-person resolver");
Require(CelarAiInternalDataService.ParseQuestion("Who is the account executive for GLH and what is its budget?") is null,
    "Mixed ownership and financial request reaches cross-domain retrieval");
Require(CelarAiEnterpriseEvidenceCatalog.NeedsPeriodClarification("Approvals for August"),
    "Named month cannot be replaced by current-week approvals");
Require(CelarAiEnterpriseEvidenceCatalog.NeedsPeriodClarification("Approvals for week 2026-02-30"),
    "Invalid weekly date cannot become the current week");
Require(CelarAiExecutionAdapterRegistry.Describe("reporting_relationships").State == "adapter_ready",
    "Capability availability reflects the executable relationship adapter");
Require(CelarAiEnterpriseEvidenceCatalog.Select(publicQuestion, PulseAiSystemKnowledgeCatalog.Analyze(publicQuestion)).Count == 0,
    "Clearly public question does not read enterprise records");
Require(CelarAiEnterpriseEvidenceCatalog.Adapters.All(adapter => adapter.Path != "/api/timesheets/week"),
    "GET endpoint that auto-submits holidays is excluded");
var weekly = CelarAiEnterpriseEvidenceCatalog.Select("My timesheet for week 2026-09-06",
    PulseAiSystemKnowledgeCatalog.Analyze("My timesheet for week 2026-09-06"));
Require(weekly.Where(tool=>tool.Method=="GET").All(tool => tool.Path.Contains("weekStart=2026-09-06")), "Explicit weekly dates are server-validated parameters");
Require(CelarAiEnterpriseEvidenceCatalog.NeedsPeriodClarification("Show my timesheet for last month"),
    "Monthly question cannot silently use current week");
Require(CelarAiEnterpriseEvidenceCatalog.Select("Show my timesheet for last month",
    PulseAiSystemKnowledgeCatalog.Analyze("Show my timesheet for last month")).All(tool=>tool.Code=="enterprise_own_time"),
    "Monthly request uses explicit date-range adapter, never a weekly snapshot");

var clock = new DateTimeOffset(2026,9,6,1,0,0,TimeSpan.Zero);
Require(CelarAiEnterprisePeriod.Parse("my time today","America/Chicago",clock) == new CelarAiEnterprisePeriod(new(2026,9,5),new(2026,9,5)), "Calendar dates use the user's timezone");
Require(CelarAiEnterprisePeriod.Parse("my hours last month","America/Chicago",clock) == new CelarAiEnterprisePeriod(new(2026,8,1),new(2026,8,31)), "Month boundaries are exact");
Require(CelarAiEnterprisePeriod.Parse("my hours last quarter","UTC",clock) == new CelarAiEnterprisePeriod(new(2026,4,1),new(2026,6,30)), "Quarter boundaries are exact");
Require(CelarAiEnterprisePeriod.Parse("my hours 2024-02-01 to 2024-02-29","UTC",clock)?.End == new DateOnly(2024,2,29), "Leap-day date range is valid");
foreach (var value in new[] {"2026-02-30", "2026-09-06 to 2026-09-01", "2020-01-01 to 2026-01-01", "since March", "last 18 months"})
    Require(CelarAiEnterprisePeriod.Parse(value,"UTC",clock) is null,"Invalid or unsupported periods cannot default to current data");

Require(CelarAiEnterpriseEvidencePolicy.ValidateResponse("{\"status\":\"loaded\",\"items\":[]}") == "", "Empty complete source is valid evidence");
foreach (var body in new[] { "{", "null", "<html>Error</html>", "{\"hasMore\":true}", "{\"pageInfo\":{\"nextCursor\":\"next\"}}", "{\"status\":\"unavailable\"}" })
    Require(CelarAiEnterpriseEvidencePolicy.ValidateResponse(body).Length > 0, "Malformed, unavailable or paginated source cannot claim completeness");

Environment.SetEnvironmentVariable("PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI", "https://test.example");
var handler = new FakeHandler();
var executor = new PulseAiSystemToolExecutor(new FakeFactory(handler), NullLogger<PulseAiSystemToolExecutor>.Instance);
var context = new DefaultHttpContext();
context.Request.Headers["X-ProjectPulse-Session"] = "synthetic-session";
context.Request.Headers["X-ProjectPulse-View-As-User"] = "synthetic-effective-user";
var options = PulseAiSystemIntelligenceOptions.FromEnvironment() with
{
    AllowedSameOriginHosts = ["test.example"], MaximumToolResponseCharacters = 2_000
};
var definition = weekly.First(tool=>tool.Method=="GET");
handler.Body = "{\"status\":\"loaded\",\"items\":[]}";
var result = await executor.ExecuteOneAsync(context, definition, options);
Require(result.Succeeded, "Registered adapter executes through bounded HTTP transport");
Require(handler.Session == "synthetic-session" && handler.ViewAs == "synthetic-effective-user", "Actual session and effective-user scope reach owning endpoint");
Require(handler.Uri?.Host == "test.example", "Tool stays on configured trusted origin");
foreach (var legacyCode in new[] {"enterprise_contracts","enterprise_billing"})
{
    var legacy = CelarAiEnterpriseEvidenceCatalog.Select("our contracts and invoices",PulseAiSystemKnowledgeCatalog.Analyze("our contracts and invoices"))
        .First(tool=>tool.Code==legacyCode);
    var calls = handler.Calls;
    var blocked = await executor.ExecuteOneAsync(context,legacy,options);
    Require(blocked.Forbidden && blocked.DiagnosticCode=="view_as_adapter_scope_unavailable" && handler.Calls==calls,
        "Legacy actual-session endpoint is never called for View-As enterprise evidence");
}
handler.Body = new string('x', 8_001);
result = await executor.ExecuteOneAsync(context, definition, options);
Require(!result.Succeeded && result.ResponseJson.Length == 0 && result.DiagnosticCode == "tool_response_incomplete", "Oversized source is not a successful truncated snapshot");
handler.Status = HttpStatusCode.Forbidden;
handler.Body = "private error detail must not enter synthesis";
result = await executor.ExecuteOneAsync(context, definition, options);
Require(result.Forbidden && result.ResponseJson.Length == 0, "Forbidden response preserves status without promoting its body");
Require(!string.Join(" ",result.EvidenceSummary).Contains("private error"),"Error summaries do not expose response content");
handler.Status = HttpStatusCode.OK;
handler.Body = "{\"hasMore\":true,\"items\":[1]}";
result = await executor.ExecuteOneAsync(context, definition, options);
Require(!result.Succeeded && result.DiagnosticCode == "tool_pagination_incomplete", "Continuation metadata prevents false complete counts");

var evidence = new PulseAiSystemToolResult("enterprise_contracts", "Contracts", "060", "Contracts", "GET", "/api/contracts/overview",
    "succeeded", 200, 1, 20, "", "{\"balance\":12}", ["Authorized contract scope"], DateTimeOffset.UtcNow);
var combined = CelarAiEnterpriseEvidencePolicy.BuildContext([evidence], 2_000);
Require(combined.Complete && combined.Text.Contains("API:enterprise_contracts") && combined.Text.Contains("balance"),
    "Private RAG supplement includes structured provenance and record values");
Require(!combined.Text.Contains("documentId") && !combined.Text.Contains("citationId"), "Structured facts are never fabricated document citations");
Require(!CelarAiEnterpriseEvidencePolicy.BuildContext([evidence], 1).Complete, "Context budget omissions remain explicit");
Require(!CelarAiEnterpriseEvidencePolicy.BuildContext([evidence with { Status = "forbidden", ResponseJson = "secret" }], 2_000).Text.Contains("secret"),
    "Failed evidence cannot enter combined synthesis");
Require(CelarAiEnterpriseEvidencePolicy.ValidateResponse("{\"status\":\"loaded\",\"sources\":[{\"status\":\"unavailable\"}]}") == "tool_source_not_ready",
    "Degraded component source prevents a healthy envelope from claiming completeness");
Require(CelarAiEnterpriseEvidencePolicy.ValidateResponse("{\"projects\":["+string.Join(",",Enumerable.Repeat("{}",100))+"]}","enterprise_financials") == "tool_source_limit_reached",
    "Silent owning-module row cap is detected");
var internalService = new CelarAiInternalDataService(new PulseAiSystemIntelligenceRepository(NullLogger<PulseAiSystemIntelligenceRepository>.Instance),
    NullLogger<CelarAiInternalDataService>.Instance);
var actorId = Guid.NewGuid();
var noTimeAccess = new PulseAiSystemAccess(actorId,true,new HashSet<string>(),new HashSet<string>());
var timeDefinition = CelarAiEnterpriseEvidenceCatalog.Select("Show my hours this month",PulseAiSystemKnowledgeCatalog.Analyze("Show my hours this month"))
    .First(tool=>tool.Code=="enterprise_own_time");
var deniedTime = await internalService.ReadEnterpriseEvidenceAsync(actorId,noTimeAccess,timeDefinition,"my hours this month","UTC",2000,default);
Require(deniedTime.Forbidden && deniedTime.DiagnosticCode=="time_view_required", "Own-time DB read requires owning TIME_VIEW permission before connecting");
var mismatchedActor = await internalService.ReadEnterpriseEvidenceAsync(Guid.NewGuid(),noTimeAccess,timeDefinition,"my hours this month","UTC",2000,default);
Require(mismatchedActor.Forbidden && mismatchedActor.DiagnosticCode=="effective_scope_required", "Caller cannot substitute another effective identity");
var database = Environment.GetEnvironmentVariable("CELAR_AI_TEST_CONNECTION_STRING");
if (!string.IsNullOrWhiteSpace(database)) await EnterpriseDatabaseChecks.RunAsync(database);
Console.WriteLine("CELAR_AI_ENTERPRISE_RETRIEVAL=PASS");

sealed class FakeFactory(FakeHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
sealed class FakeHandler : HttpMessageHandler
{
    public string Body { get; set; } = "{}";
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string? Session { get; private set; }
    public string? ViewAs { get; private set; }
    public Uri? Uri { get; private set; }
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Uri = request.RequestUri;
        Session = request.Headers.GetValues("X-ProjectPulse-Session").Single();
        ViewAs = request.Headers.GetValues("X-ProjectPulse-View-As-User").Single();
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
    }
}
