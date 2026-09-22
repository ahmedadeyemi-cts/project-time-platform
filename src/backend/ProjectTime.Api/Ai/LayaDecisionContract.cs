using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ProjectTime.Api.Ai;

public static class LayaDecisionContract
{
    public const string Revision = "1c5edc17a7acd8701df6fc341c0d179f1c62c982";
    public const string QuestionSchema = "document_type_smoke_v1";
    public static readonly string[] Labels = ["sow", "invoice", "purchase_order", "other"];
    public const string ExcerptPolicy = "first_nonempty_section_first_300_unicode_scalars_v1";

    public static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static string Excerpt(string text) =>
        string.Concat(text.EnumerateRunes().Take(300).Select(rune => rune.ToString()));

    public static JsonObject Validate(JsonObject body)
    {
        bool Flag(string key, bool expected) => body[key]?.GetValue<bool>() == expected;
        if (!Flag("ok", true) || !Flag("review_required", true)
            || !Flag("automation_approved", false) || !Flag("input_truncated", false)
            || !Flag("confidence_is_probability_of_correctness", false)
            || !Flag("external_fallback_allowed", false)
            || !Flag("production_accuracy_validated", false)
            || body["workflow_actions_performed"]?.GetValue<int>() != 0
            || body["model_revision"]?.GetValue<string>() != Revision
            || body["question_schema"]?.GetValue<string>() != QuestionSchema
            || !Labels.Contains(body["document_type"]?.GetValue<string>(), StringComparer.Ordinal))
            throw new InvalidDataException("decision_invalid_contract");
        if (body["probabilities"] is not JsonObject scores || scores.Count != Labels.Length
            || Labels.Any(label => !scores.ContainsKey(label)))
            throw new InvalidDataException("decision_invalid_scores");
        var values = Labels.Select(label => scores[label]!.GetValue<double>()).ToArray();
        var confidence = body["raw_model_confidence"]!.GetValue<double>();
        var latency = body["latency_ms"]!.GetValue<double>();
        var tokens = body["input_state_tokens"]!.GetValue<int>();
        if (values.Append(confidence).Any(x => !double.IsFinite(x) || x < 0 || x > 1)
            || Math.Abs(values.Sum() - 1) > .01 || !double.IsFinite(latency)
            || latency < 0 || latency > 30000 || tokens is < 1 or > 450)
            throw new InvalidDataException("decision_invalid_scores");
        return new JsonObject
        {
            ["predictedType"] = body["document_type"]!.GetValue<string>(),
            ["probabilities"] = scores.DeepClone(), ["rawModelConfidence"] = confidence,
            ["confidenceIsProbabilityOfCorrectness"] = false,
            ["modelRevision"] = Revision, ["questionSchema"] = QuestionSchema,
            ["serverLatencyMs"] = latency, ["inputStateTokens"] = tokens,
            ["inputTruncated"] = false, ["reviewRequired"] = true,
            ["automationApproved"] = false, ["workflowActionsPerformed"] = 0
        };
    }
}
