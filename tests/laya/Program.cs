using System.Text;
using System.Text.Json.Nodes;
using ProjectTime.Api.Ai;

void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
var text = string.Concat(Enumerable.Repeat("😀", 500));
var excerpt = LayaDecisionContract.Excerpt(text);
Check(excerpt.EnumerateRunes().Count() == 300, "Unicode excerpt must not split surrogate pairs");
Check(LayaDecisionContract.Sha256("abc") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "stable SHA256");
var good = JsonNode.Parse("""
{"ok":true,"review_required":true,"automation_approved":false,"input_truncated":false,
"confidence_is_probability_of_correctness":false,"external_fallback_allowed":false,
"production_accuracy_validated":false,"workflow_actions_performed":0,
"model_revision":"1c5edc17a7acd8701df6fc341c0d179f1c62c982","question_schema":"document_type_smoke_v1",
"document_type":"invoice","probabilities":{"sow":0.1,"invoice":0.7,"purchase_order":0.1,"other":0.1},
"raw_model_confidence":0.7,"latency_ms":2300,"input_state_tokens":50,"private_path":"not-returned"}
""")!.AsObject();
Check(LayaDecisionContract.Validate(good)["predictedType"]!.GetValue<string>() == "invoice", "valid label");
Check(!LayaDecisionContract.Validate(good).ContainsKey("private_path"), "discard unknown fields");
foreach (var key in new[] { "review_required", "input_truncated", "automation_approved", "external_fallback_allowed" })
{
    var bad = good.DeepClone().AsObject(); bad[key] = !bad[key]!.GetValue<bool>();
    var rejected = false;
    try { LayaDecisionContract.Validate(bad); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "reject " + key);
}
foreach (var tokens in new[] { 0, 451 })
{
    var bad = good.DeepClone().AsObject(); bad["input_state_tokens"] = tokens;
    var rejected = false;
    try { LayaDecisionContract.Validate(bad); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "reject token budget");
}
Console.WriteLine("LAYA_DOTNET_CONTRACT_CHECKS=PASS");
