namespace ProjectTime.Api.Ai;
// Only storage/access boundaries are substituted. The actual production evidence
// SQL, state checks, checksum checks and document/version locking are executed.
public static class ProjectPulseAiDatabaseConnection
{
    public static string Resolve() => Environment.GetEnvironmentVariable("LAYA_PROCESSED_TEST_DB")
        ?? throw new InvalidOperationException("LAYA_PROCESSED_TEST_DB is required");
}
public static class ProjectPulseUploadStorage
{
    public static string Root = "";
    public static string ResolveRoot() => Root;
}
public sealed record Source(string StoragePath, DateTimeOffset UploadedAt);
public sealed class PulseAiPrivateRuntimeSourceResolver
{
    public Guid AllowedUser, AllowedDocument;
    public Source? Current;
    public Task<Source?> ResolveAsync(Guid user, Guid document, CancellationToken ct,
        bool processingAdmission = false, bool classificationAdmission = false) =>
        Task.FromResult(user == AllowedUser && document == AllowedDocument ? Current : null);
}
