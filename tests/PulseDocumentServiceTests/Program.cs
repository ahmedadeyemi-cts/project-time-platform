using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectTime.Api.Ai;

var checks = 0;
void Require(bool value, string name)
{ checks++; if (!value) throw new InvalidOperationException(name); }
const string Prefix = PulseDocumentServiceOptions.Prefix;
var syntheticToken = new string('t', 64);
PulseDocumentServiceOptions Read(Dictionary<string,string?> d) => PulseDocumentServiceOptions.Read(k => d.GetValueOrDefault(k));
Dictionary<string,string?> Configuration()
{
    var d = new Dictionary<string,string?>
    {
        [Prefix+"MODE"]="pulse_container", ["PROJECTPULSE_ENVIRONMENT"]="test",
        [Prefix+"ENVIRONMENT_DOMAIN"]="sample-env.westus3.azurecontainerapps.io",
        [Prefix+"ORIGIN"]="https://ca-phd-test-documents-westus3.internal.sample-env.westus3.azurecontainerapps.io",
        [Prefix+"APPROVAL_REFERENCE"]="PR-1220-bb2c9cccaf4d", [Prefix+"TOKEN_SECRET_REFERENCE"]="fixture-documents-token",
        [Prefix+"TOKEN"]=syntheticToken
    };
    d[Prefix+"CONFIGURATION_SHA256"]=Read(d).ComputeConfigurationSha256();
    return d;
}
var options = Read(Configuration());
Require(options.Valid, "reviewed Test document configuration accepted");
Require(!PulseDocumentServiceOptions.Read(_ => null).Requested, "absent mode preserves legacy");
foreach (var (key,value) in new[]
{
    ("MODE","invalid"), ("ENVIRONMENT_DOMAIN","*.azurecontainerapps.io"),
    ("ORIGIN","http://localhost"), ("ORIGIN", options.Origin+"/"),
    ("ORIGIN", options.Origin+"?redirect=1"), ("TOKEN","short"),
    ("TOKEN",new string('u',64)), ("TOKEN_SECRET_REFERENCE","../secret"),
    ("APPROVAL_REFERENCE",""), ("CONFIGURATION_SHA256",new string('0',64))
})
{
    var d=Configuration(); d[Prefix+key]=value;
    Require(!Read(d).Valid && Read(d).Requested, "invalid selection fails closed: "+key);
}
var production=Configuration(); production["PROJECTPULSE_ENVIRONMENT"]="production";
Require(!Read(production).Valid, "Production activation is not authorized");
var dormant=Configuration(); dormant[Prefix+"MODE"]="legacy";
Require(!Read(dormant).Requested, "explicit rollback retains dormant credentials without using them");
Require(!options.ToString().Contains(syntheticToken), "configuration text redacts credential");
Require(!JsonSerializer.Serialize(options).Contains(syntheticToken), "JSON redacts credential");
foreach (var address in new[] {"10.0.0.1","172.16.2.1","192.168.0.2","fc00::1","::ffff:10.0.0.1",
    "100.100.0.12","100.100.128.12","100.100.160.12","100.100.192.12"})
    Require(PulseDocumentServiceOptions.AddressesApproved([IPAddress.Parse(address)]), "private destination: "+address);
foreach (var address in new[] {"8.8.8.8","127.0.0.1","169.254.169.254","0.0.0.0","::1","fe80::1","::ffff:127.0.0.1",
    "100.99.255.255","100.100.224.1","100.101.0.1"})
    Require(!PulseDocumentServiceOptions.AddressesApproved([IPAddress.Parse(address)]), "unsafe destination: "+address);
Require(!PulseDocumentServiceOptions.AddressesApproved([]), "empty DNS fails closed");
Require(!PulseDocumentServiceOptions.AddressesApproved([IPAddress.Parse("10.0.0.1"),IPAddress.Parse("8.8.8.8")]), "mixed DNS fails closed");
using (var transport=PulseDocumentServiceClient.CreateHandler(options))
    Require(!transport.AllowAutoRedirect && !transport.UseProxy && !transport.UseCookies, "transport has no redirect/proxy/cookie fallback");
var directory=Directory.CreateTempSubdirectory("pulse-document-contracts-");
var path=Path.Combine(directory.FullName,"fixture.bin");
var bytes=Encoding.UTF8.GetBytes("harmless contract fixture");
await File.WriteAllBytesAsync(path,bytes);
var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var runtime=new PulseAiPrivateRuntimeOptions(false,15,300,3,24,false,"",false,null,"fixture",
    "not_configured","",3310,45,"","","","","","","","",[]) {DocumentService=options};
var pipeline=new PulseAiDocumentPipelineOptions(directory.FullName,true,false,"",true,false,false,
    32*1024*1024,50,1000,50,100,1000,100);
var source=new PulseAiAuthorizedDocumentSource(Guid.NewGuid(),null,"","","","document","test",
    "private-original-customer-name.png","fixture.bin",path,"image/png",bytes.Length,
    false,false,"pending",false,null,DateTimeOffset.UtcNow,"synthetic","test","restricted",[]);
JsonObject ScanReceipt(bool infected=false) => new()
{
    ["status"]=infected?"infected":"clean", ["clean"]=!infected, ["infected"]=infected,
    ["scanner"]="clamav", ["sha256"]=hash, ["sizeBytes"]=bytes.Length, ["signature"]="daily-123",
    ["engine"]=new JsonObject {["version"]="1.4.3",["signatures"]="daily-123",["updated_at"]=DateTimeOffset.UtcNow.ToString("O")}
};
JsonObject OcrReceipt() => new()
{
    ["sha256"]=hash,["documentId"]=source.DocumentId.ToString("D"),["model"]=PulseDocumentServiceOptions.OcrModel,
    ["scan"]=ScanReceipt(),["pages"]=new JsonArray(new JsonObject {["pageNumber"]=1,["text"]="Approved fixture"})
};
async Task<(PulseAiPrivateMalwareScanResult Result,int Calls)> Scan(string body,int status=200,
    PulseDocumentServiceOptions? selected=null,string type="application/json")
{
    var factory=new FakeFactory(body,status,type);
    factory.Inspect=async (request,token) =>
    {
        Require(request.RequestUri?.Host==options.ExpectedHost && request.RequestUri.AbsolutePath=="/v1/scan", "scan uses only selected service");
        Require(request.Headers.Authorization?.Parameter==syntheticToken, "separate service credential");
        Require(request.Headers.GetValues("X-Pulse-Content-SHA256").Single()==hash, "exact source hash sent");
        var content=await request.Content!.ReadAsStringAsync(token);
        Require(content.Contains("harmless contract fixture") && !content.Contains(directory.FullName), "streamed source without storage path");
    };
    var scanner=new PulseAiPrivateMalwareScanner(factory,NullLogger<PulseAiPrivateMalwareScanner>.Instance);
    var result=await scanner.ScanAsync(path,runtime with {DocumentService=selected??options},CancellationToken.None);
    return (result,factory.Calls);
}
async Task<PulseAiPrivateOcrResult> Ocr(JsonObject body)
{
    var factory=new FakeFactory(body.ToJsonString());
    factory.Inspect=async (request,token) =>
    {
        Require(request.RequestUri?.AbsolutePath=="/v1/extract", "OCR uses document endpoint");
        var content=await request.Content!.ReadAsStringAsync(token);
        Require(!content.Contains(source.OriginalFileName) && !content.Contains(directory.FullName), "OCR omits customer filename and path");
    };
    return await new PulseAiPrivateOcrClient(factory,NullLogger<PulseAiPrivateOcrClient>.Instance)
        .ExtractAsync(source,pipeline,runtime,CancellationToken.None);
}
try
{
    var clean=await Scan(ScanReceipt().ToJsonString());
    Require(clean.Result.Clean && !clean.Result.Infected && clean.Calls==1, "clean receipt accepted once");
    Require(clean.Result.SourceSha256==hash && clean.Result.EvidenceSha256.Length==64, "scan evidence binds content");
    var infected=await Scan(ScanReceipt(true).ToJsonString());
    Require(infected.Result.Infected && !infected.Result.Clean, "infected content stays quarantined");
    foreach (var (name,mutate) in new (string,Action<JsonObject>)[]
    {
        ("hash", r=>r["sha256"]=new string('0',64)), ("size", r=>r["sizeBytes"]=1),
        ("engine", r=>r["scanner"]="unknown"), ("verdict", r=>r["clean"]="true"),
        ("conflict", r=>r["infected"]=true), ("partial", r=>r["status"]="partial"),
        ("missing engine", r=>r.Remove("engine")),
        ("stale", r=>r["engine"]!["updated_at"]=DateTimeOffset.UtcNow.AddDays(-3).ToString("O")),
        ("future", r=>r["engine"]!["updated_at"]=DateTimeOffset.UtcNow.AddHours(1).ToString("O")),
        ("unknown signatures", r=>r["engine"]!["signatures"]="runtime_managed")
    })
    {
        var receipt=ScanReceipt(); mutate(receipt); var rejected=await Scan(receipt.ToJsonString());
        Require(!rejected.Result.Clean && !rejected.Result.Infected && rejected.Calls==1, "invalid scan rejected without fallback: "+name);
    }
    foreach (var status in new[] {302,307,401,403,409,413,422,429,500,503,504})
    {
        var rejected=await Scan(ScanReceipt().ToJsonString(),status);
        Require(!rejected.Result.Clean && !rejected.Result.Infected && rejected.Calls==1, "HTTP failure is never clean: "+status);
    }
    foreach (var body in new[] {"", "{}", "[]", new string(' ',65537),
        ScanReceipt().ToJsonString().Replace("\"clean\":true", "\"clean\":true,\"clean\":true")})
        Require(!(await Scan(body)).Result.Clean, "malformed or oversized response rejected");
    Require(!(await Scan(ScanReceipt().ToJsonString(),type:"text/html")).Result.Clean, "HTML response rejected");
    var invalid=Configuration(); invalid[Prefix+"CONFIGURATION_SHA256"]=new string('0',64);
    var invalidResult=await Scan(ScanReceipt().ToJsonString(),selected:Read(invalid));
    Require(!invalidResult.Result.Clean && invalidResult.Calls==0, "invalid activation cannot invoke any scanner");
    var legacy=await Scan(ScanReceipt().ToJsonString(),selected:PulseDocumentServiceOptions.Disabled);
    Require(legacy.Calls==0 && legacy.Result.Scanner!=PulseDocumentServiceOptions.Provider, "legacy default does not invoke new service");
    var extracted=await Ocr(OcrReceipt());
    Require(extracted.Succeeded && extracted.Sections.Count==1 && extracted.Sections[0].Text=="Approved fixture", "actual OCR adapter accepts verified text");
    foreach (var (name,mutate) in new (string,Action<JsonObject>)[]
    {
        ("document", r=>r["documentId"]=Guid.NewGuid().ToString("D")),
        ("content", r=>r["sha256"]=new string('0',64)), ("model", r=>r["model"]="unapproved"),
        ("scan", r=>r.Remove("scan")), ("scan hash", r=>r["scan"]!["sha256"]=new string('0',64)),
        ("page identity", r=>r["pages"]![0]!["pageNumber"]=3),
        ("text limit", r=>r["pages"]![0]!["text"]=new string('x',1001)),
        ("nontext", r=>r["pages"]![0]!["text"]=123),
        ("empty", r=>r["pages"]![0]!["text"]=" ")
    })
    {
        var receipt=OcrReceipt(); mutate(receipt);
        Require(!(await Ocr(receipt)).Succeeded, "unverified OCR rejected: "+name);
    }
    using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
    var cancelledFactory=new FakeFactory(ScanReceipt().ToJsonString());
    var cancellationObserved=false;
    try { await new PulseAiPrivateMalwareScanner(cancelledFactory,NullLogger<PulseAiPrivateMalwareScanner>.Instance)
        .ScanAsync(path,runtime,cancelled.Token); }
    catch (OperationCanceledException) { cancellationObserved=true; }
    Require(cancellationObserved && cancelledFactory.Calls==0, "caller cancellation stays terminal");
    using var deadlineClient=new PulseDocumentServiceClient(options,new FakeHandler(async (_,token) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan,token);
        throw new InvalidOperationException("unreachable");
    }));
    var deadline=await deadlineClient.ScanAsync(path,5,CancellationToken.None);
    Require(!deadline.Clean && deadline.DiagnosticCode=="document_service_deadline_exceeded", "request deadline is enforced");
    foreach (var kind in new[] { "ready", "stale", "model-dependent", "unknown-engine", "unavailable" })
    {
        var health=new JsonObject
        {
            ["status"]="ready", ["scanner"]="clamav", ["ocrModel"]=PulseDocumentServiceOptions.OcrModel,
            ["modelProviderRequired"]=false, ["engine"]=ScanReceipt()["engine"]!.DeepClone()
        };
        if (kind=="stale") health["engine"]!["updated_at"]=DateTimeOffset.UtcNow.AddDays(-3).ToString("O");
        if (kind=="model-dependent") health["modelProviderRequired"]=true;
        if (kind=="unknown-engine") health["engine"]!["signatures"]="runtime_managed";
        var factory=new FakeFactory(health.ToJsonString(),kind=="unavailable"?503:200);
        using var probeClient=new PulseDocumentServiceClient(options,factory.CreateClient("PulseDocumentService"));
        var probe=await probeClient.ProbeAsync(CancellationToken.None);
        Require(probe.Ready==(kind=="ready") && factory.Calls==1,"independent readiness: "+kind);
    }
    var previous=Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_ENABLED");
    Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_ENABLED","true");
    try { Require((await Scan(ScanReceipt().ToJsonString())).Result.Clean, "document transport does not use Oracle endpoint policy"); }
    finally { Environment.SetEnvironmentVariable("PROJECTPULSE_CELAR_AI_EXTERNAL_HTTPS_RUNTIME_ENABLED",previous); }
}
finally { directory.Delete(recursive:true); }
Console.WriteLine($"PULSE_DOCUMENT_SERVICE_CONTRACTS=PASS assertions={checks}; synthetic_transport=true; deployment=false");

sealed class FakeFactory(string body,int status=200,string contentType="application/json") : IHttpClientFactory
{
    public int Calls { get; private set; }
    public Func<HttpRequestMessage,CancellationToken,Task>? Inspect { get; set; }
    public HttpClient CreateClient(string name)
    {
        if (name!="PulseDocumentService") throw new InvalidOperationException("Unexpected legacy HTTP client");
        return new HttpClient(new FakeHandler(async (request,token) =>
        {
            Calls++;
            if (Inspect is not null) await Inspect(request,token);
            return new HttpResponseMessage((HttpStatusCode)status)
            { Content=new StringContent(body,Encoding.UTF8,contentType) };
        }));
    }
}
sealed class FakeHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        => respond(request,token);
}
