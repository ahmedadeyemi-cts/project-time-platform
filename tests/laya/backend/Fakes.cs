// Test-only substitutes for existing platform boundaries. Production module code
// is linked verbatim; SQL and HTTP execute against real ephemeral services.
using System.Text.Json.Nodes;

namespace ProjectTime.Api.Ai
{
    public static class Fixture
    {
        public static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-000000000011");
        public static readonly Guid User = Guid.Parse("00000000-0000-0000-0000-000000000012");
        public static readonly Guid Inactive = Guid.Parse("00000000-0000-0000-0000-000000000013");
        public static readonly Guid ScopedAdmin = Guid.Parse("00000000-0000-0000-0000-000000000014");
        public static readonly Guid Doc = Guid.Parse("00000000-0000-0000-0000-000000000099");
        public const string PrivateText = "INVOICE. TEST-ONLY-PRIVATE-TEXT. Amount due USD 2500.";
        public static bool Candidate, Revoked, Unsafe;
        public static bool Allowed = true;
        public static int Inferences;
        public static string Hash = new('a', 64);
        public static Func<Task>? AfterInference;
        public static string? Failure;
    }
    public static class ProjectPulseAiDatabaseConnection
    {
        public static string Resolve() => Environment.GetEnvironmentVariable("LAYA_TEST_DB")
            ?? throw new InvalidOperationException("LAYA_TEST_DB is required");
    }
    public static class ProjectPulseAiReleaseRuntimePolicy
    {
        public sealed record State(bool IsCandidate);
        public static State RequireValid() => new(Fixture.Candidate);
    }
    public static class ProjectPulseActualSessionAuthority
    {
        public static bool HasPermanentAdministratorAuthority(HttpContext c, string[] roles) =>
            c.Items["ProjectPulseActualUserId"] is Guid id && id == Fixture.Admin;
        public static Task<bool> IsSuperAdministratorAsync(HttpContext c, CancellationToken cancellationToken) => Task.FromResult(false);
    }
    public sealed class LayaDecisionFailure(string code, int status = 503) : Exception(code)
    { public string Code => code; public int Status => status; }
    public static class LayaDecisionTransport
    {
        public static bool DeploymentAllowed() => Fixture.Allowed && !Fixture.Candidate;
        public static async Task<JsonObject> SendAsync(string? text, CancellationToken ct)
        {
            if (text is null) return new JsonObject { ["status"]="ready", ["runtime_connected"]=true,
                ["state_token_budget"]=450, ["inference_busy"]=false };
            Fixture.Inferences++;
            if (Fixture.Failure is not null) throw new LayaDecisionFailure(Fixture.Failure,504);
            if (Fixture.AfterInference is { } hook) { Fixture.AfterInference=null; await hook(); }
            return JsonNode.Parse("""
            {"ok":true,"review_required":true,"automation_approved":false,"input_truncated":false,
            "confidence_is_probability_of_correctness":false,"external_fallback_allowed":false,
            "production_accuracy_validated":false,"workflow_actions_performed":0,
            "model_revision":"1c5edc17a7acd8701df6fc341c0d179f1c62c982","question_schema":"document_type_smoke_v1",
            "document_type":"invoice","probabilities":{"sow":0.1,"invoice":0.7,"purchase_order":0.1,"other":0.1},
            "raw_model_confidence":0.7,"latency_ms":2300,"input_state_tokens":50}
            """)!.AsObject();
        }
    }
    public sealed record Document(Guid DocumentId, string OriginalFileName, string ProjectCode, bool ProductionAdmissionReady);
    public sealed record Safety(bool AllowedForPreview);
    public sealed record Section(int SectionIndex, string Text);
    public sealed record Extraction(bool ExtractionSucceeded, Safety Safety, bool OcrRequired, string SourceSha256, IReadOnlyList<Section> Sections);
    public sealed record Preview(Extraction Extraction);
    public sealed class PulseAiPrivateDocumentPipelineService
    {
        public Task<IReadOnlyList<Document>> ListInventoryAsync(Guid user, string? p, string? c, string? e, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Document>>(user == Fixture.Admin && !Fixture.Revoked
                ? [new Document(Fixture.Doc,"synthetic-invoice.txt","TEST",!Fixture.Unsafe)] : []);
        public Task<Preview?> BuildProcessingPreviewAsync(Guid user, Guid document, CancellationToken ct)
            => Task.FromResult(user != Fixture.Admin || document != Fixture.Doc || Fixture.Revoked ? null
                : new Preview(new Extraction(true,new Safety(!Fixture.Unsafe),false,Fixture.Hash,[new Section(0,Fixture.PrivateText)])));
    }
}
namespace ProjectTime.Api.Modules
{
    public static class AiProviderConfigurationModule
    {
        internal static bool SameOrigin(HttpContext c) => c.Request.Headers.Origin == $"{c.Request.Scheme}://{c.Request.Host}";
    }
}
