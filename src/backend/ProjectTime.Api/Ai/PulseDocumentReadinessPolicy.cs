using System.Text.Json;
namespace ProjectTime.Api.Ai;

/// <summary>Replacing Oracle document utilities must replace their live proof, not waive it.</summary>
public static class PulseDocumentReadinessPolicy
{
    public static bool AcceptOracleHealth(JsonElement root, int httpStatus,
        bool independentRequested, bool independentValid, bool independentReady)
    {
        bool Flag(string key) => root.TryGetProperty(key,out var p) && p.ValueKind==JsonValueKind.True;
        bool Text(string key,string value) => root.TryGetProperty(key,out var p)
            && p.ValueKind==JsonValueKind.String && p.GetString()==value;
        var models = Flag("ollamaReady") && Flag("generationModelReady") && Flag("embeddingModelReady")
            && Text("generationModel",PulseAiExternalHttpsRuntimePolicy.GenerationModel)
            && Text("embeddingModel",PulseAiExternalHttpsRuntimePolicy.EmbeddingModel)
            && root.TryGetProperty("rawDocumentContentLogged",out var logged) && logged.ValueKind==JsonValueKind.False;
        if(!models) return false;
        if(independentRequested)
            return independentValid && independentReady && httpStatus is 200 or 503
                && (Text("status","ready") || Text("status","degraded"));
        return httpStatus==200 && Text("status","ready") && Flag("tesseractReady") && Flag("clamavReady")
            && Text("ocrModel",PulseAiExternalHttpsRuntimePolicy.OcrModel);
    }
}
