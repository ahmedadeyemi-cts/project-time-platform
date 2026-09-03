using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

var assignedProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var unassignedProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
var checks = 0;

var priorModule025UatEnabled = Environment.GetEnvironmentVariable(
    Module025ProtectedTestUatAccess.EnabledVariable);
var priorModule025UatRunId = Environment.GetEnvironmentVariable(
    Module025ProtectedTestUatAccess.RunIdVariable);
var priorModule025UatSourceCommit = Environment.GetEnvironmentVariable(
    Module025ProtectedTestUatAccess.SourceCommitVariable);
var priorModule025UatExpiresAt = Environment.GetEnvironmentVariable(
    Module025ProtectedTestUatAccess.ExpiresAtVariable);
var priorSourceCommit = Environment.GetEnvironmentVariable("PROJECTPULSE_SOURCE_COMMIT");
var module025ActualUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
var module025EffectiveUserId = module025ActualUserId;
var module025ManagerRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "MANAGER"
};

try
{
    Environment.SetEnvironmentVariable(Module025ProtectedTestUatAccess.EnabledVariable, "true");
    Environment.SetEnvironmentVariable(Module025ProtectedTestUatAccess.RunIdVariable, "123456789-1");
    Environment.SetEnvironmentVariable("PROJECTPULSE_SOURCE_COMMIT", new string('a', 40));
    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.SourceCommitVariable,
        new string('a', 40));
    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.ExpiresAtVariable,
        (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1_800).ToString(
            System.Globalization.CultureInfo.InvariantCulture));

    Expect(
        "MODULE_025_PROTECTED_UAT:EXACT_BOUNDARY_AUTHORIZES",
        Module025ProtectedTestUatAccess.Authorizes(
            Module025ProtectedTestContext(),
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            module025ManagerRoles),
        "the exact protected-Test run, origin, identity, and Manager-only session must authorize the non-persistent UAT fixture");

    var wrongRun = Module025ProtectedTestContext();
    wrongRun.Request.Headers[Module025ProtectedTestUatAccess.RunIdHeader] = "123456789-2";
    Expect(
        "MODULE_025_PROTECTED_UAT:WRONG_RUN_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            wrongRun,
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            module025ManagerRoles),
        "a request not bound to the exact enabled GitHub run must be denied");

    var wrongHost = Module025ProtectedTestContext();
    wrongHost.Request.Host = new HostString("phd-west.onenecklab.com");
    wrongHost.Request.Headers["Origin"] = "https://phd-west.onenecklab.com";
    Expect(
        "MODULE_025_PROTECTED_UAT:PRODUCTION_HOST_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            wrongHost,
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            module025ManagerRoles),
        "the UAT role fixture must never authorize the Production host");

    var viewAs = Module025ProtectedTestContext();
    viewAs.Items["ProjectPulseIsViewAs"] = true;
    Expect(
        "MODULE_025_PROTECTED_UAT:VIEW_AS_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            viewAs,
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            module025ManagerRoles),
        "View-As must not inherit the UAT Solution Architect fixture");

    Expect(
        "MODULE_025_PROTECTED_UAT:IMPERSONATED_IDENTITY_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            Module025ProtectedTestContext(),
            module025ActualUserId,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Module025ProtectedTestUatAccess.TargetEmail,
            module025ManagerRoles),
        "different actual and effective identities must be denied");

    Expect(
        "MODULE_025_PROTECTED_UAT:WRONG_IDENTITY_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            Module025ProtectedTestContext(),
            module025ActualUserId,
            module025EffectiveUserId,
            "another.manager@ussignal.local",
            module025ManagerRoles),
        "the fixture must remain bound to the reviewed protected-Test identity");

    Expect(
        "MODULE_025_PROTECTED_UAT:NON_MANAGER_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            Module025ProtectedTestContext(),
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ENGINEERING" }),
        "an identity without the Manager role must be denied");

    Expect(
        "MODULE_025_PROTECTED_UAT:EXISTING_SOLUTION_ARCHITECT_NOT_RECLASSIFIED",
        !Module025ProtectedTestUatAccess.Authorizes(
            Module025ProtectedTestContext(),
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "MANAGER",
                "SOLUTION_ARCHITECT"
            }),
        "an existing Solution Architect session must use its real role instead of the UAT fixture");

    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.ExpiresAtVariable,
        (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1).ToString(
            System.Globalization.CultureInfo.InvariantCulture));
    Expect(
        "MODULE_025_PROTECTED_UAT:EXPIRED_FIXTURE_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            Module025ProtectedTestContext(),
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            module025ManagerRoles),
        "a fixture that cleanup failed to disable must self-expire");
    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.ExpiresAtVariable,
        (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1_800).ToString(
            System.Globalization.CultureInfo.InvariantCulture));

    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.SourceCommitVariable,
        new string('b', 40));
    Expect(
        "MODULE_025_PROTECTED_UAT:WRONG_SOURCE_COMMIT_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            Module025ProtectedTestContext(),
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            module025ManagerRoles),
        "the fixture must remain bound to the exact deployed candidate source commit");
    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.SourceCommitVariable,
        new string('a', 40));

    Environment.SetEnvironmentVariable(Module025ProtectedTestUatAccess.EnabledVariable, "false");
    Expect(
        "MODULE_025_PROTECTED_UAT:DISABLED_FAILS_CLOSED",
        !Module025ProtectedTestUatAccess.Authorizes(
            Module025ProtectedTestContext(),
            module025ActualUserId,
            module025EffectiveUserId,
            Module025ProtectedTestUatAccess.TargetEmail,
            module025ManagerRoles),
        "the fixture must be inert when the governed runtime flag is disabled");
}
finally
{
    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.EnabledVariable,
        priorModule025UatEnabled);
    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.RunIdVariable,
        priorModule025UatRunId);
    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.SourceCommitVariable,
        priorModule025UatSourceCommit);
    Environment.SetEnvironmentVariable(
        Module025ProtectedTestUatAccess.ExpiresAtVariable,
        priorModule025UatExpiresAt);
    Environment.SetEnvironmentVariable("PROJECTPULSE_SOURCE_COMMIT", priorSourceCommit);
}

var authoritativeScopeSavedAt = DateTimeOffset.Parse(
    "2026-09-03T00:00:00Z",
    System.Globalization.CultureInfo.InvariantCulture);
var authoritativeScope = new CelarAiAuthoritativeScopeEvidence(
    EngagementId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
    Revision: 7,
    EngagementNumber: "SOW-2026-000555",
    CustomerName: "Protected UAT Customer",
    ServiceOverview: "Plan and deliver a governed two-site network modernization with explicit validation and handoff requirements.",
    SavedAt: authoritativeScopeSavedAt);
var authoritativeSource = PulseAiPrivateRagService.CreateModule025AuthoritativeScopeSource(
    authoritativeScope);
Expect(
    "MODULE_025_PRIVATE_SCOPE:SERVER_SAVED_SOURCE_CREATED",
    authoritativeSource is
    {
        RankOrder: 1,
        ProjectId: null,
        DocumentCategory: "module025_service_overview",
        CitationAnchor: "Saved Service Overview",
        SourceType: "module025_saved_service_overview",
        SourceModule: "025"
    }
    && authoritativeSource.DocumentId == authoritativeScope.EngagementId
    && authoritativeSource.DocumentVersionId == authoritativeScope.EngagementId
    && authoritativeSource.ProjectCode == authoritativeScope.EngagementNumber
    && authoritativeSource.ProjectName == authoritativeScope.CustomerName
    && authoritativeSource.Text == authoritativeScope.ServiceOverview
    && authoritativeSource.ProcessedAt == authoritativeScopeSavedAt
    && Regex.IsMatch(authoritativeSource.SourceSha256, "^[0-9a-f]{64}$")
    && Regex.IsMatch(authoritativeSource.TextSha256, "^[0-9a-f]{64}$"),
    "the owned saved Service Overview must become one private, hashed, citation-addressable Module 025 source");
Expect(
    "MODULE_025_PRIVATE_SCOPE:SHORT_UNSAVED_SOURCE_FAILS_CLOSED",
    PulseAiPrivateRagService.CreateModule025AuthoritativeScopeSource(
        authoritativeScope with { ServiceOverview = "too short" }) is null,
    "an incomplete Service Overview must not be admitted as authoritative private evidence");

var managerAndProjectManagementRoles = new[] { "MANAGER", "PROJECT_MANAGEMENT" };
Expect(
    "MODULE_003_UTILIZATION:COMPOSITE_MANAGER_READ_COMPATIBILITY",
    ScopedRolePolicyRules.IsCompositeManagerUtilizationReadCompatibilityDeny(
        "PROJECT_MANAGEMENT",
        "003",
        "UTILIZATION_VIEW",
        "MODULE_ACCESS",
        false,
        managerAndProjectManagementRoles,
        true),
    "a published Manager utilization grant must remain usable when the same user also has the legacy Project Management role");
Expect(
    "MODULE_003_UTILIZATION:STANDALONE_PROJECT_MANAGEMENT_DENY_REMAINS",
    !ScopedRolePolicyRules.IsCompositeManagerUtilizationReadCompatibilityDeny(
        "PROJECT_MANAGEMENT",
        "003",
        "UTILIZATION_VIEW",
        "MODULE_ACCESS",
        false,
        new[] { "PROJECT_MANAGEMENT" },
        true),
    "standalone Project Management must retain its explicit Module 003 denial");
Expect(
    "MODULE_003_UTILIZATION:MANAGER_GRANT_REQUIRED",
    !ScopedRolePolicyRules.IsCompositeManagerUtilizationReadCompatibilityDeny(
        "PROJECT_MANAGEMENT",
        "003",
        "UTILIZATION_VIEW",
        "MODULE_ACCESS",
        false,
        managerAndProjectManagementRoles,
        false),
    "the compatibility must fail closed if the published policy does not contain a Manager utilization grant");
Expect(
    "MODULE_003_UTILIZATION:OTHER_ROLE_DENY_REMAINS",
    !ScopedRolePolicyRules.IsCompositeManagerUtilizationReadCompatibilityDeny(
        "ACCOUNTING",
        "003",
        "UTILIZATION_VIEW",
        "MODULE_ACCESS",
        false,
        new[] { "MANAGER", "ACCOUNTING" },
        true),
    "an unrelated role's explicit Module 003 denial must continue to block a composite Manager identity");
Expect(
    "MODULE_003_UTILIZATION:WRITE_DENY_REMAINS_NON_BYPASSABLE",
    !ScopedRolePolicyRules.IsCompositeManagerUtilizationReadCompatibilityDeny(
        "PROJECT_MANAGEMENT",
        "003",
        "UTILIZATION_EDIT",
        "MODULE_ACCESS",
        true,
        managerAndProjectManagementRoles,
        true),
    "the read-only compatibility must never relax a utilization write denial");

var roleAssignmentRoute = ScopedRolePolicyRules.RouteContract(
    "/api/admin/users/roles",
    HttpMethods.Post);
Expect(
    "MODULE_012_ROLE_ASSIGNMENT:POST_BOUNDARY",
    roleAssignmentRoute is
    {
        ModuleCode: "012",
        ActionCode: "ROLE_ASSIGN",
        IsWrite: true
    },
    "role assignment must resolve to Module 012 ROLE_ASSIGN as a write action");

var roleAssignmentTrailingSlashRoute = ScopedRolePolicyRules.RouteContract(
    "/api/admin/users/roles/",
    HttpMethods.Post);
Expect(
    "MODULE_012_ROLE_ASSIGNMENT:TRAILING_SLASH_POST_BOUNDARY",
    roleAssignmentTrailingSlashRoute is
    {
        ModuleCode: "012",
        ActionCode: "ROLE_ASSIGN",
        IsWrite: true
    },
    "the trailing-slash role-assignment route must resolve to the same non-bypassable Module 012 write action");

var roleAssignmentRepeatedSlashRoute = ScopedRolePolicyRules.RouteContract(
    "/api/admin/users/roles///",
    HttpMethods.Post);
Expect(
    "MODULE_012_ROLE_ASSIGNMENT:REPEATED_TRAILING_SLASH_POST_BOUNDARY",
    roleAssignmentRepeatedSlashRoute is
    {
        ModuleCode: "012",
        ActionCode: "ROLE_ASSIGN",
        IsWrite: true
    },
    "repeated trailing slashes must not bypass the role-assignment authorization boundary");

Expect(
    "MODULE_012_ROLE_ASSIGNMENT:NON_BYPASSABLE",
    ScopedRolePolicyRules.NonBypassableActions.Contains("ROLE_ASSIGN"),
    "role assignment must be denied to non-Super-Administrator sessions by the central evaluator");
Expect(
    "MODULE_012_ROLE_ASSIGNMENT:READ_NOT_RECLASSIFIED",
    ScopedRolePolicyRules.RouteContract("/api/admin/users/roles", HttpMethods.Get) is null,
    "the legacy POST mutation boundary must not convert a nonexistent GET route into a role-policy read");
Expect(
    "MODULE_012_ROLE_ASSIGNMENT:TRAILING_SLASH_READ_NOT_RECLASSIFIED",
    ScopedRolePolicyRules.RouteContract("/api/admin/users/roles/", HttpMethods.Get) is null,
    "the trailing-slash path must not invent a GET role-policy route");

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

static DefaultHttpContext Module025ProtectedTestContext()
{
    var context = new DefaultHttpContext();
    context.Request.Scheme = "https";
    context.Request.Host = new HostString("phd-west-test.onenecklab.com");
    context.Request.Headers["Origin"] = "https://phd-west-test.onenecklab.com";
    context.Request.Headers[Module025ProtectedTestUatAccess.RunIdHeader] = "123456789-1";
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
