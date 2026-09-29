using System.Reflection;
using Microsoft.AspNetCore.Http;
using ProjectTime.Api.Modules;
using static ProjectTime.Api.Modules.ProductionApprovalWorkModule;

var count = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
var context = new DefaultHttpContext();
foreach (var request in new[] {
 new BulkCompleteRequest("selected","manager",null,[],null),
 new BulkCompleteRequest("selected","manager",new DateOnly(2026,9,29),[],null),
 new BulkCompleteRequest("selected","manager",new DateOnly(2026,9,27),[],null),
 new BulkCompleteRequest("week","manager",null,null,null,new DateOnly(2026,9,1)),
 new BulkCompleteRequest("selected","manager",null,[],null,new DateOnly(2026,9,2)),
 new BulkCompleteRequest("selected","manager",new DateOnly(2026,9,27),[],null,new DateOnly(2026,9,1)),
 new BulkCompleteRequest("selected","pm",null,[],null,new DateOnly(9999,12,1)),
 new BulkCompleteRequest("selected","invalid",new DateOnly(2026,9,27),[],null),
 new BulkCompleteRequest("selected","manager",new DateOnly(2026,9,27),null,null)
}) {
 var result = await ProductionApprovalWorkModule.BulkCompleteAsync(request, context);
 Check(result is IStatusCodeHttpResult status && status.StatusCode == 400,"Invalid or ambiguous request must fail before database access");
}
var type = typeof(ProductionApprovalWorkModule);
object Record(string name, Dictionary<string, object?> fields) {
 var t=type.GetNestedType(name,BindingFlags.NonPublic)!;
 var ctor=t.GetConstructors().Single();
 return ctor.Invoke(ctor.GetParameters().Select(p=>fields.TryGetValue(p.Name!,out var v)?v:
   p.ParameterType==typeof(string)?"":p.ParameterType==typeof(string[])?Array.Empty<string>():
   p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null).ToArray());
}
var actor=Guid.NewGuid();var other=Guid.NewGuid();
var complete=type.GetMethod("CompleteItemAsync",BindingFlags.NonPublic|BindingFlags.Static)!;
foreach(var stage in new[]{"manager","pm","ptc"})
foreach(var reason in new[]{"self","view_as","unauthorized"}) {
 var access=Record("ApprovalAccess",new(){["ActualUserId"]=actor,["EffectiveUserId"]=actor,
 ["IsViewAs"]=reason=="view_as",["CanManagerApprove"]=reason!="unauthorized",
 ["CanProjectApprove"]=reason!="unauthorized",["CanPtcFinalApprove"]=reason!="unauthorized"});
 var item=Record("ApprovalWorkItem",new(){["UserId"]=reason=="self"?actor:other});
 // Null connections intentionally prove rejection happens before any read/write.
 var changed=await (Task<bool>)complete.Invoke(null,[null,null,access,stage,item,Guid.NewGuid(),"test",true,CancellationToken.None])!;
 Check(!changed,$"{stage} must reject {reason}");
}
Console.WriteLine($"PASS {count} approval request/write-boundary cases; no database or live approvals changed.");
