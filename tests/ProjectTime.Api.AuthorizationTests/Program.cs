using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using ProjectTime.Api.Modules;

var assignedProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var unassignedProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
var checks = 0;

var canonicalJsonRoutes = new[]
{
    "/api/work-register/projects/documents/save",
    "/api/work-register/projects/documents/archive",
    "/api/work-register/projects/change-orders/save",
    "/api/work-register/tasks/assignments/roster/save",
    "/api/work-register/tasks/assignments/update"
};
var aliasJsonRoutes = new[]
{
    "/api/work-register/projects/update",
    "/api/work-register/projects/lifecycle"
};
var projectUpdateRoute = "/api/work-register/projects/update";
var expectedMutationRoutes = canonicalJsonRoutes
    .Concat(aliasJsonRoutes)
    .Append("/api/work-register/projects/documents/upload")
    .Append("/api/work-register/projects/{projectId:guid}/purchase-order")
    .OrderBy(value => value, StringComparer.Ordinal)
    .ToArray();

var sourceMutationRoutes = Directory
    .EnumerateFiles("src/backend/ProjectTime.Api", "*.cs", SearchOption.AllDirectories)
    .SelectMany(path => Regex.Matches(
            File.ReadAllText(path),
            "Map(?:Post|Put|Patch|Delete)\\(\\\"(?<path>/api/work-register/[^\\\"]+)\\\"")
        .Cast<Match>()
        .Select(match => match.Groups["path"].Value))
    .Where(path => !path.StartsWith("/api/work-register/intake/packages", StringComparison.OrdinalIgnoreCase))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .OrderBy(value => value, StringComparer.Ordinal)
    .ToArray();

ExpectSequence("MUTATION_ROUTE_INVENTORY", sourceMutationRoutes, expectedMutationRoutes);

foreach (var route in canonicalJsonRoutes)
{
    await ExpectResolutionAsync(
        $"{route}:ACTUAL_PROJECT_ID",
        JsonContext(route, JsonIds(("projectId", assignedProjectId))),
        WorkRegisterProjectIdResolutionStatus.Found,
        assignedProjectId);
    await ExpectResolutionAsync(
        $"{route}:CONFLICT_REJECTED",
        JsonContext(route, JsonIds(
            ("workId", assignedProjectId),
            ("projectId", unassignedProjectId))),
        WorkRegisterProjectIdResolutionStatus.Conflicting);
}

foreach (var route in aliasJsonRoutes)
{
    await ExpectResolutionAsync(
        $"{route}:ENDPOINT_ALIAS",
        JsonContext(route, JsonIds(("workId", assignedProjectId))),
        WorkRegisterProjectIdResolutionStatus.Found,
        assignedProjectId);
    await ExpectResolutionAsync(
        $"{route}:CONFLICT_REJECTED",
        JsonContext(route, JsonIds(
            ("workId", assignedProjectId),
            ("projectId", unassignedProjectId))),
        WorkRegisterProjectIdResolutionStatus.Conflicting);
}

await ExpectResolutionAsync(
    "PROJECT_UPDATE:EXTENDED_ENDPOINT_ALIAS",
    JsonContext(
        projectUpdateRoute,
        JsonIds(("selectedProjectId", assignedProjectId))),
    WorkRegisterProjectIdResolutionStatus.Found,
    assignedProjectId);
await ExpectResolutionAsync(
    "PROJECT_UPDATE:EXTENDED_ALIAS_CONFLICT_REJECTED",
    JsonContext(
        projectUpdateRoute,
        JsonIds(
            ("projectId", unassignedProjectId),
            ("selectedProjectId", assignedProjectId))),
    WorkRegisterProjectIdResolutionStatus.Conflicting);

foreach (var alias in WorkRegisterAuthorization.ProjectUpdateIdAliases)
{
    using var payload = JsonDocument.Parse(JsonIds((alias, assignedProjectId)));
    var guardedProjectIdText = WorkRegisterAuthorization.ReadProjectUpdateIdText(payload.RootElement);
    Expect(
        $"PROJECT_UPDATE:ARCHIVE_GUARD_{alias}",
        string.Equals(guardedProjectIdText, assignedProjectId.ToString(), StringComparison.OrdinalIgnoreCase),
        $"archive guard did not resolve {alias}");
}

await ExpectResolutionAsync(
    "DOCUMENT_UPLOAD:ACTUAL_PROJECT_ID",
    FormContext("/api/work-register/projects/documents/upload", new()
    {
        ["projectId"] = assignedProjectId.ToString()
    }),
    WorkRegisterProjectIdResolutionStatus.Found,
    assignedProjectId);
await ExpectResolutionAsync(
    "DOCUMENT_UPLOAD:CONFLICT_REJECTED",
    FormContext("/api/work-register/projects/documents/upload", new()
    {
        ["workId"] = assignedProjectId.ToString(),
        ["projectId"] = unassignedProjectId.ToString()
    }),
    WorkRegisterProjectIdResolutionStatus.Conflicting);

var purchaseOrderPath = $"/api/work-register/projects/{assignedProjectId}/purchase-order";
await ExpectResolutionAsync(
    "PURCHASE_ORDER:ROUTE_PROJECT_ID",
    JsonContext(purchaseOrderPath, "{\"purchaseOrderRequired\":false}"),
    WorkRegisterProjectIdResolutionStatus.Found,
    assignedProjectId);
await ExpectResolutionAsync(
    "PURCHASE_ORDER:CONFLICT_REJECTED",
    JsonContext(purchaseOrderPath, JsonIds(("workId", unassignedProjectId))),
    WorkRegisterProjectIdResolutionStatus.Conflicting);

await ExpectResolutionAsync(
    "CANONICAL_ROUTE:ALIAS_CANNOT_AUTHORIZE",
    JsonContext(canonicalJsonRoutes[0], JsonIds(("workId", assignedProjectId))),
    WorkRegisterProjectIdResolutionStatus.Missing);
await ExpectResolutionAsync(
    "CANONICAL_ROUTE:INVALID_ACTUAL_ID",
    JsonContext(canonicalJsonRoutes[0], "{\"projectId\":\"not-a-guid\"}"),
    WorkRegisterProjectIdResolutionStatus.Invalid);
await ExpectResolutionAsync(
    "CANONICAL_ROUTE:MALFORMED_JSON",
    JsonContext(canonicalJsonRoutes[0], "{\"projectId\":"),
    WorkRegisterProjectIdResolutionStatus.Invalid);
await ExpectResolutionAsync(
    "CANONICAL_ROUTE:KESTREL_BUFFERED_BODY",
    KestrelJsonContext(
        canonicalJsonRoutes[0],
        JsonIds(("projectId", assignedProjectId))),
    WorkRegisterProjectIdResolutionStatus.Found,
    assignedProjectId);
await ExpectResolutionAsync(
    "UNKNOWN_MUTATION:FAILS_CLOSED_FOR_ASSIGNED_PM",
    JsonContext(
        "/api/work-register/projects/future-mutation",
        JsonIds(("projectId", assignedProjectId))),
    WorkRegisterProjectIdResolutionStatus.Unsupported);

Console.WriteLine($"WORK_REGISTER_AUTHORIZATION_CHECKS={checks}");
Console.WriteLine("WORK_REGISTER_AUTHORIZATION_CONTRACT=PASSED");
return 0;

async Task ExpectResolutionAsync(
    string name,
    HttpContext context,
    WorkRegisterProjectIdResolutionStatus expectedStatus,
    Guid? expectedProjectId = null)
{
    var actual = await WorkRegisterAuthorization.ResolveProjectIdAsync(context, CancellationToken.None);
    Expect(
        name,
        actual.Status == expectedStatus && actual.ProjectId == expectedProjectId,
        $"expected {expectedStatus}/{expectedProjectId}, received {actual.Status}/{actual.ProjectId}");
}

void ExpectSequence(string name, IReadOnlyList<string> actual, IReadOnlyList<string> expected)
{
    Expect(
        name,
        actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase),
        $"expected [{string.Join(", ", expected)}], received [{string.Join(", ", actual)}]");
}

void Expect(string name, bool condition, string detail)
{
    checks += 1;
    Console.WriteLine($"WORK_REGISTER_AUTHORIZATION_{Sanitize(name)}={(condition ? "PASSED" : "FAILED")}");
    if (!condition) throw new InvalidOperationException(detail);
}

static string Sanitize(string value) =>
    Regex.Replace(value.Trim('/').ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');

static string JsonIds(params (string Key, Guid Value)[] values) =>
    JsonSerializer.Serialize(values.ToDictionary(item => item.Key, item => item.Value.ToString()));

static DefaultHttpContext JsonContext(string path, string json)
{
    var context = new DefaultHttpContext();
    var bytes = Encoding.UTF8.GetBytes(json);
    context.Request.Method = HttpMethods.Post;
    context.Request.Path = path;
    context.Request.ContentType = "application/json";
    context.Request.ContentLength = bytes.Length;
    context.Request.Body = new MemoryStream(bytes);
    return context;
}

static DefaultHttpContext KestrelJsonContext(string path, string json)
{
    var context = JsonContext(path, json);
    context.Request.Body = new NonSeekableReadStream(Encoding.UTF8.GetBytes(json));
    return context;
}

static DefaultHttpContext FormContext(string path, Dictionary<string, StringValues> values)
{
    var context = new DefaultHttpContext();
    context.Request.Method = HttpMethods.Post;
    context.Request.Path = path;
    context.Request.ContentType = "multipart/form-data; boundary=projectpulse-test";
    context.Features.Set<IFormFeature>(new FormFeature(new FormCollection(values)));
    return context;
}

sealed class NonSeekableReadStream(byte[] bytes) : Stream
{
    private readonly MemoryStream inner = new(bytes);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        inner.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
