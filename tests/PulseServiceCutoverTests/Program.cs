using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProjectTime.Api.Ai;
var checks=0;
void Require(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
JsonObject Oracle()=>new(){["status"]="ready",["ollamaReady"]=true,["generationModelReady"]=true,["embeddingModelReady"]=true,
["tesseractReady"]=true,["clamavReady"]=true,["generationModel"]="gemma3:4b",["embeddingModel"]="embeddinggemma",["ocrModel"]="tesseract-5-eng",["rawDocumentContentLogged"]=false};
bool Accept(JsonObject data,int http,bool selected,bool valid,bool ready){using var doc=JsonDocument.Parse(data.ToJsonString());return PulseDocumentReadinessPolicy.AcceptOracleHealth(doc.RootElement,http,selected,valid,ready);}
Require(Accept(Oracle(),200,false,false,false),"Legacy retains working readiness");
foreach(var key in new[]{"ollamaReady","generationModelReady","embeddingModelReady","tesseractReady","clamavReady"}){
 var body=Oracle();body[key]=false;Require(!Accept(body,200,false,false,false),"Legacy still requires "+key);
}
var remoteDown=Oracle();remoteDown["status"]="degraded";remoteDown["tesseractReady"]=false;remoteDown["clamavReady"]=false;
Require(Accept(remoteDown,503,true,true,true),"Verified local utilities replace unavailable Oracle utilities");
foreach(var flags in new[]{(false,true),(true,false),(false,false)})Require(!Accept(remoteDown,503,true,flags.Item1,flags.Item2),"Independent proof cannot be omitted");
foreach(var code in new[]{301,401,403,404,500,502})Require(!Accept(remoteDown,code,true,true,true),"Unexpected Oracle HTTP cannot be accepted");
foreach(var key in new[]{"ollamaReady","generationModelReady","embeddingModelReady"}){
 var b=remoteDown.DeepClone().AsObject();b[key]=false;Require(!Accept(b,503,true,true,true),"Model availability remains independent and required "+key);
}
foreach(var key in new[]{"generationModel","embeddingModel"}){
 var b=remoteDown.DeepClone().AsObject();b[key]="other";Require(!Accept(b,503,true,true,true),"Model identity retained "+key);
}
var leaked=Oracle();leaked["rawDocumentContentLogged"]=true;Require(!Accept(leaked,200,true,true,true),"Privacy remains required");
const string prefix=PulseLayaServiceOptions.Prefix;
var d=new Dictionary<string,string?>{["PROJECTPULSE_ENVIRONMENT"]="test",[prefix+"MODE"]="pulse_container",[prefix+"ENVIRONMENT_DOMAIN"]="jollywave-6212cd8b.westus3.azurecontainerapps.io",[prefix+"ORIGIN"]="https://ca-phd-test-laya-westus3.internal.jollywave-6212cd8b.westus3.azurecontainerapps.io",[prefix+"TOKEN"]=new string('q',64),[prefix+"TOKEN_SECRET_REFERENCE"]="test-laya-credential",[prefix+"APPROVAL_REFERENCE"]="PR-1222-c8ac122653b2"};
PulseLayaServiceOptions Read()=>PulseLayaServiceOptions.Read(k=>d.GetValueOrDefault(k));
d[prefix+"CONFIGURATION_SHA256"]=Read().ComputeConfigurationSha256();var good=Read();Require(good.Valid,"Independent Laya selection valid");
Require(!PulseLayaServiceOptions.Read(_=>null).Requested,"Laya default remains legacy");
foreach(var (key,value) in new[]{("MODE","unknown"),("ORIGIN","http://localhost"),("ORIGIN",good.Origin+"/"),("TOKEN","short"),("TOKEN_SECRET_REFERENCE","../escape"),("APPROVAL_REFERENCE",""),("CONFIGURATION_SHA256",new string('0',64))}){
 var saved=d[prefix+key];d[prefix+key]=value;Require(Read().Requested&&!Read().Valid,"Laya rejects "+key);d[prefix+key]=saved;
}
d["PROJECTPULSE_ENVIRONMENT"]="production";Require(!Read().Valid,"No Production activation");d["PROJECTPULSE_ENVIRONMENT"]="test";
Require(!JsonSerializer.Serialize(good).Contains(new string('q',64)),"Laya credential omitted from JSON");
foreach(var ip in new[]{"127.0.0.1","169.254.169.254","8.8.8.8","::1","fe80::1","100.99.255.255","100.100.224.1","100.101.0.1"})Require(!PulseLayaServiceOptions.AddressesApproved([IPAddress.Parse(ip)]),"Laya rejects unsafe destination "+ip);
foreach(var ip in new[]{"10.1.2.3","100.100.0.12","100.100.128.12","100.100.160.12","100.100.192.12"})Require(PulseLayaServiceOptions.AddressesApproved([IPAddress.Parse(ip)]),"Private Laya address accepted "+ip);
Require(!PulseLayaServiceOptions.AddressesApproved([IPAddress.Parse("100.100.0.12"),IPAddress.Parse("8.8.8.8")]),"Mixed DNS rejected");
foreach(var path in new[]{"/v1/scan","/v1/chat/completions","/health","/v1/decisions/document-type?url=x"}){
 var rejected=false;try{good.Endpoint(path);}catch(PulseLayaServiceException){rejected=true;}Require(rejected,"Laya cannot use unrelated paths");
}
Console.WriteLine($"PULSE_SERVICE_CUTOVER_CONTRACTS=PASS assertions={checks}; runtime_mutation=false");
