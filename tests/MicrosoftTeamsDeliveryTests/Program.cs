using System.Net;
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
foreach(var configured in new[]{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),"synthetic-key-material"}) {
 var key=Convert.TryFromBase64String(configured,new Span<byte>(new byte[32]),out var len)&&len==32?Convert.FromBase64String(configured):SHA256.HashData(Encoding.UTF8.GetBytes($"ProjectPulse-Microsoft-Integration:{configured}"));
 var nonce=RandomNumberGenerator.GetBytes(12);var plain=Encoding.UTF8.GetBytes("synthetic-stored-secret");var cipher=new byte[plain.Length];var tag=new byte[16];
 using(var aes=new AesGcm(key,16))aes.Encrypt(nonce,plain,cipher,tag,Encoding.UTF8.GetBytes("ProjectPulse:065:onenecklab"));
 Check(MicrosoftTeamsServicesSnapshot.Decrypt(cipher,nonce,tag,configured,"onenecklab")=="synthetic-stored-secret","existing credential envelope supported");
 bool failed=false;try{MicrosoftTeamsServicesSnapshot.Decrypt(cipher,nonce,tag,configured,"ussignal");}catch(CryptographicException){failed=true;}Check(failed,"cross-tenant AAD denied");
 tag[0]^=1;failed=false;try{MicrosoftTeamsServicesSnapshot.Decrypt(cipher,nonce,tag,configured,"onenecklab");}catch(CryptographicException){failed=true;}Check(failed,"tampered credential denied");
 CryptographicOperations.ZeroMemory(plain);CryptographicOperations.ZeroMemory(key);
}
Console.WriteLine($"TEAMS_PROTOCOL_ASSERTIONS={count}; LIVE_MICROSOFT_CALLS=0; RESULT=PASS");
sealed class FakeHttp(Func<HttpRequestMessage,int,Task<HttpResponseMessage>> respond):HttpMessageHandler {
 internal List<(Uri Uri,string? Authorization)> Requests {get;}=[];
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){var index=Requests.Count;Requests.Add((request.RequestUri!,request.Headers.Authorization?.ToString()));return await respond(request,index);}
}
