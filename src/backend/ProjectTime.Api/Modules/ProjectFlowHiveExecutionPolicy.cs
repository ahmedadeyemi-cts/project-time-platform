using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>One finite budget and input identity for every durable planner operation.</summary>
public static class ProjectFlowHiveExecutionPolicy
{
    public const string Contract = "flowhive-bounded-execution-v1-20260906";
    public const string Migration = "104_flowhive_bounded_ai_execution";
    public const int MaximumAttempts = 2;
    public static readonly TimeSpan OverallBudget = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan InferenceBudget = TimeSpan.FromMinutes(2);

    public static string Fingerprint(ProjectFlowHivePlanRequest plan, Guid actual, Guid effective,
        string outcome, string detail, string sources) => Hash(JsonSerializer.Serialize(new
        {
            contract = Contract, actual, effective, plan,
            outcome = outcome.Trim(), detail = detail.Trim(), sources
        }));

    public static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static bool IsActive(string status) => status is "queued" or "processing" or "generating";
    public static bool CanAttempt(int attempts, DateTimeOffset deadline, DateTimeOffset now) =>
        attempts >= 0 && attempts < MaximumAttempts && now < deadline;
    public static bool MatchesWorkingCopy(Guid? expected, Guid? actual) => expected == actual;

    internal static string SelectionFingerprint(ProjectPlanningDocumentResolution documents) =>
        Hash(JsonSerializer.Serialize(documents.SelectedDocuments.OrderBy(d => d.DocumentId).Select(d => new
        { d.DocumentId, d.WorkRegisterDocumentId, d.EffectiveAt, d.UploadedAt, d.WorkRegisterStoredPath })));

    internal static string VersionFingerprint(ProjectPlanningDocumentResolution documents) =>
        Hash(JsonSerializer.Serialize(documents.SelectedDocuments.OrderBy(d => d.DocumentId).Select(d => new
        { d.DocumentId, d.ActiveVersionId, d.ActiveSourceSha256, d.ActiveDocumentVersion, d.AuthorityStatus, d.IndexStatus })));
}
