using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProjectTime.Api.Modules;

var tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
var clientId = Guid.Parse("22222222-2222-4222-8222-222222222222");
var manifest = Guid.Parse("33333333-3333-4333-8333-333333333333");
var catalog = Guid.Parse("44444444-4444-4444-8444-444444444444");
var user = Guid.Parse("55555555-5555-4555-8555-555555555555");
var requestId = "66666666-6666-4666-8666-666666666666";
var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); count++; }
string J(object value) => JsonSerializer.Serialize(value);
HttpResponseMessage Response(int status, string body) => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
string Install(string? external = null, string? azure = null, int number = 1, bool next = false) => J(new Dictionary<string, object?> {
 ["value"] = Enumerable.Range(0, number).Select(_ => new { id = "opaque+/=installation", teamsApp = new { id = catalog, externalId = external ?? manifest.ToString() },
 teamsAppDefinition = new { teamsAppId = catalog, azureADAppId = azure ?? clientId.ToString(), version = "1.0.2", publishingState = "published" } }).ToArray(),
}.Concat(next ? new Dictionary<string, object?> { ["@odata.nextLink"] = "https://evil.invalid/next" } : new()).ToDictionary(p => p.Key, p => p.Value));
async Task<(MicrosoftTeamsNotificationProtocol.Outcome Result, FakeHttp Handler)> Run(bool send = true, string? installed = null,
 int sendStatus = 204, int? failureAt = null, int failStatus = 403, string? failBody = null, bool authorize = true,
 bool timeout = false, string? member = null, bool throwAuthority = false)
{
 var handler = new FakeHttp(async (request, index) => {
  if (failureAt == index) return Response(failStatus, failBody ?? J(new { error = new { code = "Forbidden", message = "secret-do-not-retain pilot@example.invalid", innerError = new Dictionary<string,string>{{"request-id",requestId}} } }));
  if (index == 0) return Response(200, "{\"access_token\":\"synthetic-token\"}");
  if (index == 1) return Response(200, member ?? J(new { id=user, userPrincipalName="pilot@example.invalid", userType="Member", accountEnabled=true }));
  if (index == 2) return Response(200, installed ?? Install());
  Check(index == 3, "no protocol retry");
  Check(request.RequestUri!.AbsolutePath == $"/v1.0/users/{user}/teamwork/sendActivityNotification", "resolved user endpoint");
  using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
  var root = json.RootElement;
  Check(root.GetProperty("topic").GetProperty("source").GetString() == "entityUrl", "supported entity topic");
  Check(root.GetProperty("topic").GetProperty("value").GetString() == $"https://graph.microsoft.com/v1.0/users/{user}/teamwork/installedApps/{Uri.EscapeDataString("opaque+/=installation")}", "exact encoded installation destination");
  Check(!root.GetProperty("topic").TryGetProperty("webUrl", out _), "no invalid Pulse webUrl");
  Check(root.GetProperty("teamsAppId").GetString() == catalog.ToString(), "catalog ID, not manifest ID");
  Check(root.GetProperty("activityType").GetString() == "systemDefault", "reserved activity");
  Check(!root.GetRawText().Contains("secret") && !root.GetRawText().Contains("pilot@example.invalid"), "minimal notification preview");
  if (timeout) throw new TaskCanceledException("synthetic unknown transport result");
  var response = Response(sendStatus, J(new { error = new { code = "BadRequest", message = "webUrl secret-do-not-retain" } }));
  response.Headers.Add("request-id", requestId); return response;
 });
 using var http = new HttpClient(handler);
 var result = await MicrosoftTeamsNotificationProtocol.ExecuteAsync(http,tenant,clientId,"synthetic-secret",manifest,"pilot@example.invalid",send,
  _ => throwAuthority ? throw new IOException("synthetic authority failure") : Task.FromResult(authorize), CancellationToken.None);
 Check(handler.Requests.All(r => r.Uri.Host is "graph.microsoft.com" or "login.microsoftonline.com"), "outbound origins fixed");
 Check(handler.Requests.Skip(1).All(r => r.Authorization == "Bearer synthetic-token"), "Graph bearer only");
 return (result,handler);
}
var success = await Run();
Check(success.Result.Status == "sent", "204 accepted"); Check(success.Handler.Requests.Count == 4, "one send after three preflight requests");
Check(success.Result.Diagnostic.RequestId == requestId, "correlation retained");
Check(success.Result.CatalogAppId == catalog && success.Result.InstalledVersion == "1.0.2", "installation evidence");
var check = await Run(send:false); Check(check.Result.Status == "installation_verified", "non-delivery check"); Check(check.Handler.Requests.Count == 3, "check never sends");
Check(Uri.UnescapeDataString(check.Handler.Requests[2].Uri.Query).Contains($"teamsApp/externalId eq '{manifest}'"), "exact package filter");
foreach (var item in new[] {
 (Install(number:0),"teams_app_not_installed"), (Install(number:2),"teams_installation_ambiguous"),
 (Install(external:Guid.NewGuid().ToString()),"teams_installation_identity_invalid"),
 (Install(azure:Guid.NewGuid().ToString()),"teams_app_identity_mismatch"),
 (Install(next:true),"teams_installation_response_incomplete"),
 (Install().Replace("published","submitted"),"teams_app_not_published") }) {
 var result=await Run(installed:item.Item1); Check(result.Result.Diagnostic.Code==item.Item2,item.Item2); Check(result.Handler.Requests.Count==3,"failure cannot send");
}
var blocked=await Run(authorize:false); Check(blocked.Result.Diagnostic.Code=="teams_authority_changed","revocation before send"); Check(blocked.Handler.Requests.Count==3,"revoked send stopped");
var authorityError=await Run(throwAuthority:true); Check(authorityError.Result.Status=="failed","authority read fails closed"); Check(authorityError.Handler.Requests.Count==3,"authority failure never sends");
foreach(var status in new[]{202,500,503}) { var r=await Run(sendStatus:status); Check(r.Result.Status=="outcome_unknown","ambiguous response"); Check(r.Handler.Requests.Count==4,"ambiguous response not retried"); }
var cancelled=await Run(timeout:true); Check(cancelled.Result.Status=="outcome_unknown","cancel after send unknown");
foreach(var status in new[]{400,403,404,429}) {
 var r=await Run(sendStatus:status); Check(r.Result.Status=="failed","definite rejection");
 var stored=MicrosoftTeamsNotificationProtocol.Store(r.Result.Diagnostic); Check(!stored.Contains("secret-do-not-retain"),"provider prose never retained");
 Check(stored.Contains(requestId),"safe request ID retained");
 Check(MicrosoftTeamsNotificationProtocol.ReadStored(stored).Code==$"teams_graph_http_{status}","diagnostics roundtrip");
}
foreach(var stage in new[]{0,1,2}) {var r=await Run(failureAt:stage);Check(r.Result.Status=="failed","preflight rejection");Check(r.Handler.Requests.Count==stage+1,"preflight short circuit");Check(!MicrosoftTeamsNotificationProtocol.Store(r.Result.Diagnostic).Contains("secret-do-not-retain"),"preflight redaction");}
var large=await Run(failureAt:0, failStatus:200,failBody:new string('x',32769)); Check(large.Result.Status=="failed","oversized response stops");
var malformed=await Run(installed:"[]");Check(malformed.Result.Status=="failed","malformed installation stops");
var inactive=await Run(member:J(new{id=user,userType="Member",accountEnabled=false}));Check(inactive.Handler.Requests.Count==2,"inactive recipient denied");
var guest=await Run(member:J(new{id=user,userType="Guest",accountEnabled=true}));Check(guest.Handler.Requests.Count==2,"guest excluded from pilot");
Check(MicrosoftTeamsNotificationProtocol.ReadStored("teams_graph_http_400").Code=="teams_graph_http_400","legacy history retained");
using(var response=Response(400,J(new{error=new{code="untrusted-secret",message="private data",innerError=new Dictionary<string,string>{{"request-id","not-a-guid"}}}}))) {
 var e=await MicrosoftTeamsNotificationProtocol.ErrorAsync(response,"graph",CancellationToken.None);Check(e.GraphErrorCode=="unclassified" && e.RequestId==null,"untrusted code and ID filtered");
}
string Metadata(object[] tenants)=>J(new{configuration=new{notes="PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:"+J(new{tenants})}});
object Profile(string env="test",string key="onenecklab")=>new{environmentMode=env,key,tenantId=tenant,services=new{clientId},sso=new{clientId=Guid.NewGuid()},mail=new{recipientBoundary="test_only"}};
var profile=MicrosoftTeamsServicesSnapshot.ParseMetadata(Metadata(new[]{Profile(),Profile("production","ussignal")}),"test",7);
Check(profile.ClientId==clientId && profile.TenantId==tenant && profile.Key=="onenecklab" && profile.Revision==7,"services identity, not SSO");
Check(profile.RecipientBoundary=="test_only","boundary retained");
foreach(var raw in new[]{Metadata(new[]{Profile(),Profile()}),Metadata(Array.Empty<object>()),"{}",Metadata(new[]{new{environmentMode="test",key="onenecklab",tenantId=tenant,sso=new{clientId}}})}) {
 bool failed=false;try{MicrosoftTeamsServicesSnapshot.ParseMetadata(raw,"test",1);}catch{failed=true;}Check(failed,"missing ambiguous or SSO-only metadata rejected");
}
foreach(var configured in new[]{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}) {
 var key=Convert.FromBase64String(configured);
 var nonce=RandomNumberGenerator.GetBytes(12);var plain=Encoding.UTF8.GetBytes("synthetic-stored-secret");var cipher=new byte[plain.Length];var tag=new byte[16];
 using(var aes=new AesGcm(key,16))aes.Encrypt(nonce,plain,cipher,tag,Encoding.UTF8.GetBytes("ProjectPulse:065:onenecklab"));
 Check(MicrosoftTeamsServicesSnapshot.Decrypt(cipher,nonce,tag,configured,"onenecklab")=="synthetic-stored-secret","existing credential envelope supported");
 bool failed=false;try{MicrosoftTeamsServicesSnapshot.Decrypt(cipher,nonce,tag,configured,"ussignal");}catch(CryptographicException){failed=true;}Check(failed,"cross-tenant AAD denied");
 tag[0]^=1;failed=false;try{MicrosoftTeamsServicesSnapshot.Decrypt(cipher,nonce,tag,configured,"onenecklab");}catch(CryptographicException){failed=true;}Check(failed,"tampered credential denied");
 CryptographicOperations.ZeroMemory(plain);CryptographicOperations.ZeroMemory(key);
}

foreach(var invalidKey in new[]{"synthetic-key-material",Convert.ToBase64String(new byte[16]),Convert.ToBase64String(new byte[64]),""}) {
 bool failed=false;
 try { MicrosoftTeamsServicesSnapshot.Decrypt(new byte[24],new byte[12],new byte[16],invalidKey,"onenecklab"); }
 catch(InvalidDataException) { failed=true; }
 Check(failed,"weak, malformed and missing credential keys fail closed");
}

var workflowUrl = "https://tenant.environment.api.powerplatform.com/powerautomate/automations/direct/workflows/test/triggers/manual/paths/invoke";
var workflowEnvelope = new MicrosoftTeamsWorkflowProtocol.Envelope(
    Guid.NewGuid().ToString("D"), "individual", new[] { "pilot@example.invalid" }, null, null, null,
    "Pulse test", "Test message", "information", "manual_test", "065",
    "https://phd-west-test.onenecklab.com/#dashboard", Guid.NewGuid().ToString("D"));

Check(MicrosoftTeamsWorkflowProtocol.ValidTriggerUrl(workflowUrl, out var workflowEndpoint) && workflowEndpoint is not null, "Power Automate URL accepted");
Check(!MicrosoftTeamsWorkflowProtocol.ValidTriggerUrl("http://tenant.environment.api.powerplatform.com/x", out _), "Power Automate requires HTTPS");
Check(!MicrosoftTeamsWorkflowProtocol.ValidTriggerUrl("https://example.com/x", out _), "Power Automate host allowlist");

async Task<(MicrosoftTeamsWorkflowProtocol.Outcome Result, FakeHttp Handler)> RunWorkflow(int workflowStatus = 202, int tokenStatus = 200, string? audience = null)
{
    var handler = new FakeHttp(async (request, index) => {
        if (index == 0)
        {
            Check(request.RequestUri!.Host == "login.microsoftonline.com", "workflow token authority fixed");
            var body = await request.Content!.ReadAsStringAsync();
            Check(body.Contains("https%3A%2F%2Fservice.flow.microsoft.com%2F%2F.default"), "workflow slash-terminated commercial audience requested");
            return tokenStatus == 200 ? Response(200, "{\"access_token\":\"workflow-token\"}") : Response(tokenStatus, "{}");
        }
        Check(index == 1, "workflow sends once");
        Check(request.RequestUri!.Host.EndsWith(".api.powerplatform.com", StringComparison.Ordinal), "workflow destination restricted");
        Check(request.Headers.Authorization?.ToString() == "Bearer workflow-token", "workflow bearer token");
        Check(request.Headers.Contains("x-pulse-event-id") && request.Headers.Contains("x-pulse-idempotency-key"), "workflow idempotency headers");
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var root = json.RootElement;
        Check(root.GetProperty("destinationType").GetString() == "individual", "workflow destination type");
        Check(root.GetProperty("recipients").GetArrayLength() == 1, "workflow recipients bounded");
        Check(root.GetProperty("subject").GetString() == "Pulse test", "workflow subject serialized");
        Check(root.GetProperty("sourceModule").GetString() == "065", "workflow keeps raw source ID");
        Check(root.GetProperty("sourceModuleLabel").GetString() == "Module 065 - Teams Integration", "workflow sends friendly source label");
        var response = Response(workflowStatus, workflowStatus >= 400 ? "{\"error\":{}}" : "{}");
        response.Headers.Add("x-ms-request-id", requestId);
        response.Headers.Add("x-ms-workflow-run-id", "workflow-run-1");
        return response;
    });
    using var http = new HttpClient(handler);
    var result = await MicrosoftTeamsWorkflowProtocol.ExecuteAsync(http, tenant, clientId, "synthetic-secret",
        audience ?? "https://service.flow.microsoft.com/", workflowUrl, workflowEnvelope, CancellationToken.None);
    return (result, handler);
}

var workflowSuccess = await RunWorkflow();
Check(workflowSuccess.Result.Status == "sent", "workflow 2xx accepted");
Check(workflowSuccess.Result.Diagnostic.Code == "teams_workflow_accepted", "workflow accepted diagnostic");
Check(workflowSuccess.Result.Diagnostic.RequestId == requestId, "workflow request ID retained");
Check(workflowSuccess.Result.WorkflowRunId == "workflow-run-1", "workflow run ID retained");
Check(workflowSuccess.Handler.Requests.Count == 2, "workflow one token and one send");

// Display metadata is additive for every destination; it never becomes the routing identity.
foreach (var (source, label) in new (string? Source, string Label)[]
{
    ("065", "Module 065 - Teams Integration"),
    ("025", "Module 025 - SOW & GSD Workspace"),
    (" 25 ", "Module 025 - SOW & GSD Workspace"),
    ("999", "Module 999"),
    ("Project FlowHive", "Project FlowHive"),
    ("", "Pulse"),
    (" \t\r\n ", "Pulse"),
    (null, "Pulse"),
    ("Ops\nReview", "OpsReview"),
    (new string('x', 200), new string('x', 128)),
    ("<b>Ops & Review</b>", "<b>Ops & Review</b>")
})
{
    Check(MicrosoftTeamsWorkflowProtocol.FormatSourceModuleLabel(source) == label, "source label and fallback");
    foreach (var destination in new[] { "individual", "group_chat", "channel" })
    {
        var envelope = workflowEnvelope with
        {
            SourceModule = source!, DestinationType = destination,
            ConversationId = destination == "group_chat" ? "synthetic-chat" : null,
            TeamId = destination == "channel" ? "synthetic-team" : null,
            ChannelId = destination == "channel" ? "synthetic-channel" : null
        };
        using var content = JsonContent.Create(envelope);
        using var json = JsonDocument.Parse(await content.ReadAsStringAsync());
        var root = json.RootElement;
        Check(root.GetProperty("sourceModule").GetString() == source, "raw originating source unchanged");
        Check(root.GetProperty("sourceModuleLabel").GetString() == label, "shared camelCase label for all destinations");
        Check(root.GetProperty("destinationType").GetString() == destination, "source label cannot change destination");
        Check(root.GetProperty("conversationId").GetString() == envelope.ConversationId, "source label cannot change group chat");
        Check(root.GetProperty("teamId").GetString() == envelope.TeamId && root.GetProperty("channelId").GetString() == envelope.ChannelId, "source label cannot change team or channel");
        Check(root.GetProperty("recipients")[0].GetString() == "pilot@example.invalid" && root.GetProperty("recipients").GetArrayLength() == 1, "source label cannot expand recipients");
        Check(root.GetProperty("eventId").GetString() == workflowEnvelope.EventId && root.GetProperty("idempotencyKey").GetString() == workflowEnvelope.IdempotencyKey, "source label preserves event identity");
        Check(root.GetProperty("subject").GetString() == workflowEnvelope.Subject && root.GetProperty("message").GetString() == workflowEnvelope.Message, "source label preserves content");
        Check(root.GetProperty("severity").GetString() == workflowEnvelope.Severity && root.GetProperty("notificationType").GetString() == workflowEnvelope.NotificationType, "source label preserves severity and event type");
        Check(root.GetProperty("pulseUrl").GetString() == workflowEnvelope.PulseUrl, "source label preserves Pulse link");
    }
}

var workflowDenied = await RunWorkflow(403);
Check(workflowDenied.Result.Status == "failed" && workflowDenied.Result.Diagnostic.Code == "teams_workflow_not_authorized", "workflow authorization failure classified");
string Base64Url(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+','-').Replace('/','_');
var diagnosticOid = Guid.Parse("77777777-7777-4777-8777-777777777777");
var diagnosticToken = $"{Base64Url("{\"alg\":\"none\"}")}.{Base64Url(J(new { aud="https://service.flow.microsoft.com/", tid=tenant, oid=diagnosticOid, azp=clientId }))}.signature";
var diagnosticHandler = new FakeHttp((request, index) => {
    if (index == 0) return Task.FromResult(Response(200, J(new { access_token = diagnosticToken })));
    Check(index == 1, "diagnostic workflow sends once");
    Check(request.Headers.Authorization?.Parameter == diagnosticToken, "diagnostic bearer used but never persisted");
    var response = Response(403, "{}");
    response.Headers.Add("x-ms-request-id", requestId);
    return Task.FromResult(response);
});
using (var diagnosticHttp = new HttpClient(diagnosticHandler))
{
    var diagnostic = await MicrosoftTeamsWorkflowProtocol.ExecuteAsync(diagnosticHttp, tenant, clientId, "synthetic-secret",
        "https://service.flow.microsoft.com/", workflowUrl, workflowEnvelope, CancellationToken.None, captureTokenIdentity: true);
    Check(diagnostic.Diagnostic.Code == "teams_workflow_not_authorized", "diagnostic authorization classification");
    Check(diagnostic.Diagnostic.TokenIdentity?.Audience == "https://service.flow.microsoft.com/", "diagnostic audience captured");
    Check(diagnostic.Diagnostic.TokenIdentity?.TenantId == tenant.ToString(), "diagnostic tenant captured");
    Check(diagnostic.Diagnostic.TokenIdentity?.ObjectId == diagnosticOid.ToString(), "diagnostic object captured");
    Check(diagnostic.Diagnostic.TokenIdentity?.AppId == clientId.ToString(), "diagnostic azp captured");
    var storedIdentity = MicrosoftTeamsWorkflowProtocol.ReadStoredTokenIdentity(J(new { tokenIdentity = new {
        audience = diagnostic.Diagnostic.TokenIdentity?.Audience, tenantId = diagnostic.Diagnostic.TokenIdentity?.TenantId,
        objectId = diagnostic.Diagnostic.TokenIdentity?.ObjectId, appId = diagnostic.Diagnostic.TokenIdentity?.AppId } }));
    Check(storedIdentity?.ObjectId == diagnosticOid.ToString(), "stored diagnostic identity roundtrip");
    Check(!J(diagnostic.Diagnostic).Contains(diagnosticToken), "raw bearer never retained in diagnostic");
}

var workflowLimited = await RunWorkflow(429);
Check(workflowLimited.Result.Status == "failed" && workflowLimited.Result.Diagnostic.Code == "teams_workflow_rate_limited", "workflow rate limit classified");
var workflowUnknown = await RunWorkflow(503);
Check(workflowUnknown.Result.Status == "outcome_unknown", "workflow server failure remains unknown");
var workflowTokenFailure = await RunWorkflow(tokenStatus: 401);
Check(workflowTokenFailure.Result.Status == "failed" && workflowTokenFailure.Handler.Requests.Count == 1, "workflow token failure prevents send");

var tooManyRecipients = workflowEnvelope with { Recipients = Enumerable.Range(0, 101).Select(i => $"u{i}@example.invalid").ToArray() };
using (var http = new HttpClient(new FakeHttp((_, _) => throw new Exception("network must not be called"))))
{
    var result = await MicrosoftTeamsWorkflowProtocol.ExecuteAsync(http, tenant, clientId, "synthetic-secret",
        "https://service.flow.microsoft.com/", workflowUrl, tooManyRecipients, CancellationToken.None);
    Check(result.Diagnostic.Code == "teams_workflow_recipient_count_invalid", "workflow recipient bound enforced");
}

// Actual shared parity policy: channel independence, privacy, boundaries and schedule determinism.
Check(Module065NotificationParityPolicy.MailBoundary("production_governed","locked",true,true)=="production_governed", "quiet-hours pause retains source boundary so the queued event can resume");
Check(Module065NotificationParityPolicy.MailBoundary("test_only","locked",true,true)=="test_only", "deferred Test-only event never gains live delivery");
Check(Module065NotificationParityPolicy.MailBoundary("locked","production_governed",true,true)=="locked", "defer cannot unlock a source policy");
Check(Module065NotificationParityPolicy.MailBoundary("production_governed","locked",true,false)=="locked", "actual locked transport still prevents non-deferred delivery");
Check(Module065NotificationParityPolicy.MailBoundary("production_governed","production_governed",false,true)=="locked", "revoked source cannot resume through deferred path");
var emailCalls = 0;
var emailResult = await Module065NotificationParityPolicy.IndependentChannelsAsync(
    () => Task.FromException(new IOException("synthetic queue failure")),
    () => { emailCalls++; return Task.FromResult("email-accepted"); });
Check(emailCalls == 1 && emailResult == "email-accepted", "Teams queue failure cannot suppress or replay email");
var teamQueues = 0;
try
{
    await Module065NotificationParityPolicy.IndependentChannelsAsync(
        () => { teamQueues++; return Task.CompletedTask; },
        () => Task.FromException<string>(new IOException("synthetic email failure")));
}
catch (IOException) { }
Check(teamQueues == 1, "email failure cannot undo independent Teams queue");
foreach (var boundary in new[] { "locked", "test_only", "", "unknown" })
{
    Check(!Module065NotificationParityPolicy.MaySend(boundary,"production_governed","production","production",true,false), "source boundary must authorize live delivery");
    Check(!Module065NotificationParityPolicy.MaySend("production_governed",boundary,"production","production",true,false), "services boundary must authorize live delivery");
}
Check(Module065NotificationParityPolicy.MaySend("production_governed","production_governed","production","production",true,false), "matching authorized live environment");
Check(!Module065NotificationParityPolicy.MaySend("production_governed","production_governed","test","production",true,false), "cross-environment delivery blocked");
Check(!Module065NotificationParityPolicy.MaySend("production_governed","production_governed","production","production",false,false), "disabled Teams blocked");
Check(!Module065NotificationParityPolicy.MaySend("production_governed","production_governed","production","production",true,true), "View-As cannot enqueue live Teams");
Check(Module065NotificationParityPolicy.Recipients([" A@EXAMPLE.INVALID ","a@example.invalid","bad-address",""]).SequenceEqual(["a@example.invalid"]), "same person To/CC/BCC is deduplicated without expanding recipients");
Check(Module065NotificationParityPolicy.Recipients(Enumerable.Range(1,500).Select(i=>$"u{i}@example.invalid")).Length==500, "company audience is not dropped by a 100-recipient envelope bound");
var eventKey = Module065NotificationParityPolicy.EventId("sow_sell",tenant);
Check(eventKey == Module065NotificationParityPolicy.EventId("sow_sell",tenant), "native event identity deterministic");
Check(eventKey != Module065NotificationParityPolicy.EventId("analytics_schedule",tenant), "native source namespaces do not collide");
Check(Module065NotificationParityPolicy.RecipientKey(eventKey," A@example.invalid ")==Module065NotificationParityPolicy.RecipientKey(eventKey,"a@example.invalid"), "recipient-specific idempotency stable");
Check(Module065NotificationParityPolicy.HtmlText("<script>x</script>&\nnext")=="&lt;script&gt;x&lt;/script&gt;&amp;<br />next", "plain email text escaped for HTML Compose");
Check(Encoding.UTF8.GetByteCount(Module065NotificationParityPolicy.HtmlText(string.Concat(Enumerable.Repeat("<🙂>",30000))))<12500, "encoded message remains bounded for Teams");
Check(Module065NotificationParityPolicy.Link(null,"#time-entry","production")=="", "production never falls back to Test URL");
Check(Module065NotificationParityPolicy.Link("https://pulse.example.invalid/path?token=ignored","#time-entry","production")=="https://pulse.example.invalid/#time-entry", "links strip base query and stay same origin");
Check(Module065NotificationParityPolicy.Link("https://pulse.example.invalid","#\"><img src=x>","production")=="https://pulse.example.invalid/#dashboard", "link attribute injection rejected");
Check(Module065NotificationParityPolicy.TerminalStatus("sent","teams_workflow_accepted",1)=="accepted", "workflow acceptance is not claimed as final Teams delivery");
Check(Module065NotificationParityPolicy.TerminalStatus("failed","teams_workflow_rate_limited",1)=="retry_wait", "definite rate limit can retry Teams only");
Check(Module065NotificationParityPolicy.TerminalStatus("failed","teams_workflow_rate_limited",5)=="failed", "rate-limit retry budget finite");
Check(Module065NotificationParityPolicy.TerminalStatus("outcome_unknown","teams_workflow_http_503",1)=="outcome_unknown", "ambiguous provider outcome never auto-replayed");
var monday = JsonSerializer.SerializeToElement(new { timezone="America/Chicago",dayOfWeek=1,localTime="06:00" });
Check(!EnterpriseReminderPolicy.DueToday("TIME_NOT_SUBMITTED",new DateTime(2026,9,28,5,59,0),monday), "Monday reminder not early");
Check(EnterpriseReminderPolicy.DueToday("TIME_NOT_SUBMITTED",new DateTime(2026,9,28,6,0,0),monday), "Monday reminder due at configured time");
Check(!EnterpriseReminderPolicy.DueToday("TIME_NOT_SUBMITTED",new DateTime(2026,9,29,6,0,0),monday), "weekly reminder not daily spam");
Check(EnterpriseReminderPolicy.CompletedWeek(new DateOnly(2026,9,28))==new DateOnly(2026,9,20), "completed Sunday-to-Saturday week, not current unfinished week");
Check(EnterpriseReminderPolicy.LocalTime(new DateTimeOffset(2026,7,6,11,0,0,TimeSpan.Zero),monday).Hour==6, "Central daylight-saving time supported");
Check(EnterpriseReminderPolicy.LocalTime(new DateTimeOffset(2026,1,5,12,0,0,TimeSpan.Zero),monday).Hour==6, "Central standard time supported");
var monthEnd = JsonSerializer.SerializeToElement(new { dayOfWeek=5,localTime="08:00" });
Check(EnterpriseReminderPolicy.DueToday("PM_MONTH_END_REMINDER",new DateTime(2026,10,30,8,0,0),monthEnd), "selected last Friday month-end");
Check(!EnterpriseReminderPolicy.DueToday("PM_MONTH_END_REMINDER",new DateTime(2026,10,23,8,0,0),monthEnd), "not every Friday month-end");
Check(EnterpriseReminderPolicy.HolidayOffsets(JsonSerializer.SerializeToElement(new {})).SequenceEqual([7,1]), "holiday lead times seven and one day");
foreach(var status in new[]{"submitted","manager_approved","pm_approved","accounting_ready","reconciled","locked"})
    Check(EnterpriseReminderPolicy.IsSubmitted(status),"submitted/approved time is not a non-submission violation");
var authorityHandler = new FakeHttp((_, index) => index==0
    ? Task.FromResult(Response(200,"{\"access_token\":\"synthetic\"}"))
    : throw new Exception("revoked source must never POST to Power Automate"));
using(var authorityClient=new HttpClient(authorityHandler))
{
    var denied=await MicrosoftTeamsWorkflowProtocol.ExecuteAsync(authorityClient,tenant,clientId,"synthetic-secret",
        "https://service.flow.microsoft.com/",workflowUrl,workflowEnvelope,CancellationToken.None,authorizeBeforeSend:_=>Task.FromResult(false));
    Check(denied.Diagnostic.Code=="teams_workflow_authority_changed" && authorityHandler.Requests.Count==1,"recheck authorization after token acquisition and before external send");
}

Console.WriteLine($"TEAMS_PROTOCOL_ASSERTIONS={count}; LIVE_MICROSOFT_CALLS=0; RESULT=PASS");
sealed class FakeHttp(Func<HttpRequestMessage,int,Task<HttpResponseMessage>> respond):HttpMessageHandler {
 internal List<(Uri Uri,string? Authorization)> Requests {get;}=[];
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){var index=Requests.Count;Requests.Add((request.RequestUri!,request.Headers.Authorization?.ToString()));return await respond(request,index);}
}
