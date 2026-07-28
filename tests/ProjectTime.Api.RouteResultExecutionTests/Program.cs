using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var checks = 0;
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://127.0.0.1:0");
var app = builder.Build();

// This is the ambiguous registration shape previously used by role-policy and
// several Module 001 GET handlers. Task<IResult> can bind to RequestDelegate's
// Task return type, so ASP.NET awaits the task but does not execute the result.
app.MapGet("/method-group", MethodGroupResultAsync);

// This is the framework-safe registration/execution shape. The concrete delegate
// preserves IResult semantics and writes JSON to the response.
app.MapGet(
    "/explicit-result",
    (Func<HttpContext, Task<IResult>>)ExplicitResultAsync);

try
{
    await app.StartAsync();
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
    var baseAddress = addresses?
        .FirstOrDefault(value => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    Expect(
        "BOUND_TEST_SERVER",
        !string.IsNullOrWhiteSpace(baseAddress),
        "Kestrel must expose the ephemeral test address");

    using var client = new HttpClient { BaseAddress = new Uri(baseAddress!) };

    using var methodGroupResponse = await client.GetAsync("/method-group");
    var methodGroupBody = await methodGroupResponse.Content.ReadAsStringAsync();
    Expect(
        "METHOD_GROUP_STATUS",
        methodGroupResponse.StatusCode == HttpStatusCode.OK,
        "ambiguous method-group registration returns HTTP 200");
    Expect(
        "METHOD_GROUP_EMPTY_BODY",
        methodGroupBody.Length == 0,
        "ambiguous Task<IResult> method group reproduces the zero-byte response");
    Console.WriteLine("TASK_IRESULT_METHOD_GROUP_EMPTY_200_REPRODUCED=PASS");

    using var explicitResponse = await client.GetAsync("/explicit-result");
    var explicitBody = await explicitResponse.Content.ReadAsStringAsync();
    Expect(
        "EXPLICIT_RESULT_STATUS",
        explicitResponse.StatusCode == HttpStatusCode.OK,
        "explicit Func<HttpContext,Task<IResult>> returns HTTP 200");
    Expect(
        "EXPLICIT_RESULT_CONTENT_TYPE",
        explicitResponse.Content.Headers.ContentType?.MediaType == "application/json",
        "explicit IResult execution returns JSON content type");
    Expect(
        "EXPLICIT_RESULT_BODY",
        explicitBody.Contains("\"roles\"", StringComparison.Ordinal)
        && explicitBody.Contains("\"modules\"", StringComparison.Ordinal),
        "explicit IResult execution writes the required response collections");
    Console.WriteLine("EXPLICIT_IRESULT_JSON=PASS");

    var repositoryRoot = FindRepositoryRoot();
    var roleCompatibilityPath = Path.Combine(
        repositoryRoot,
        "src/backend/ProjectTime.Api/Modules/ScopedRolePolicyResultExecutionCompatibility.cs");
    var module001CompatibilityPath = Path.Combine(
        repositoryRoot,
        "src/backend/ProjectTime.Api/Modules/Module001ResultExecutionCompatibility.cs");
    var registrationPath = Path.Combine(
        repositoryRoot,
        "src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs");
    var projectPath = Path.Combine(
        repositoryRoot,
        "src/backend/ProjectTime.Api/ProjectTime.Api.csproj");

    var roleCompatibility = File.ReadAllText(roleCompatibilityPath);
    var module001Compatibility = File.ReadAllText(module001CompatibilityPath);
    var registration = File.ReadAllText(registrationPath);
    var project = File.ReadAllText(projectPath);

    foreach (var marker in new[]
             {
                 "summary\" => await SummaryAsync(context)",
                 "catalog\" => await CatalogAsync(context)",
                 "versions\" => await VersionsAsync(context)",
                 "matrix\" => await MatrixAsync(context)",
                 "await result.ExecuteAsync(context);",
                 "X-ProjectPulse-Role-Policy-Execution",
                 "explicit-iresult-v1"
             })
    {
        Expect(
            $"ROLE_COMPATIBILITY_{Sanitize(marker)}",
            roleCompatibility.Contains(marker, StringComparison.Ordinal),
            $"role-policy compatibility source must contain {marker}");
    }

    var executionIndex = registration.IndexOf(
        "app.UseScopedRolePolicyResultExecutionCompatibility();",
        StringComparison.Ordinal);
    var availabilityIndex = registration.IndexOf(
        "app.UseModuleAvailabilityReadContinuityCompatibility();",
        StringComparison.Ordinal);
    Expect(
        "ROLE_COMPATIBILITY_REGISTERED",
        executionIndex >= 0,
        "explicit role-policy result execution compatibility is registered");
    Expect(
        "ROLE_COMPATIBILITY_BEFORE_AVAILABILITY",
        availabilityIndex >= 0 && executionIndex < availabilityIndex,
        "role-policy result execution runs before the historic availability/endpoint path");

    foreach (var marker in new[]
             {
                 "UseModule001ResultExecutionCompatibility",
                 "RuntimePtcUsersAsync(context)",
                 "RuntimePtcWorkspaceAsync(targetUserId, context)",
                 "Module001TimerTargetsAsync(context)",
                 "Module001ActiveTimerAsync(context)",
                 "Module001TimerHistoryAsync(context)",
                 "Module001WorkQueueAsync(context)",
                 "Module001WeeklyLinesAsync(context)",
                 "X-ProjectPulse-Module001-Result-Execution",
                 "explicit-iresult-v1",
                 "await result.ExecuteAsync(context);"
             })
    {
        Expect(
            $"MODULE001_COMPATIBILITY_{Sanitize(marker)}",
            module001Compatibility.Contains(marker, StringComparison.Ordinal),
            $"Module 001 compatibility source must contain {marker}");
    }

    var module001CompatibilityIndex = project.IndexOf(
        "app.UseModule001ResultExecutionCompatibility();",
        StringComparison.Ordinal);
    var module001EndpointIndex = project.IndexOf(
        "app.MapModule001TimesheetEnhancementEndpoints();",
        StringComparison.Ordinal);
    Expect(
        "MODULE001_COMPATIBILITY_REGISTERED",
        module001CompatibilityIndex >= 0,
        "Module 001 explicit result execution compatibility is registered");
    Expect(
        "MODULE001_COMPATIBILITY_BEFORE_ENDPOINTS",
        module001EndpointIndex >= 0 && module001CompatibilityIndex < module001EndpointIndex,
        "Module 001 explicit result execution runs before the ambiguous historic endpoint registrations");

    Console.WriteLine($"ROUTE_RESULT_EXECUTION_CHECKS={checks}");
    Console.WriteLine("ROLE_POLICY_RESULT_EXECUTION_CONTRACT=PASS");
    Console.WriteLine("MODULE_001_RESULT_EXECUTION_CONTRACT=PASS");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}

static Task<IResult> MethodGroupResultAsync(HttpContext context) =>
    Task.FromResult<IResult>(Results.Ok(new
    {
        roles = new[] { "SUPER_ADMINISTRATOR" },
        modules = new[] { "001", "012", "037" }
    }));

static Task<IResult> ExplicitResultAsync(HttpContext context) =>
    Task.FromResult<IResult>(Results.Ok(new
    {
        roles = new[] { "SUPER_ADMINISTRATOR" },
        modules = new[] { "001", "012", "037" }
    }));

void Expect(string name, bool condition, string evidence)
{
    checks += 1;
    Console.WriteLine(
        $"ROUTE_RESULT_EXECUTION_{name}={(condition ? "PASSED" : "FAILED")} — {evidence}");
    if (!condition)
        throw new InvalidOperationException($"{name}: {evidence}");
}

static string FindRepositoryRoot()
{
    foreach (var startingPoint in new[]
             {
                 Directory.GetCurrentDirectory(),
                 AppContext.BaseDirectory
             })
    {
        var directory = new DirectoryInfo(startingPoint);
        while (directory is not null)
        {
            var project = Path.Combine(
                directory.FullName,
                "src/backend/ProjectTime.Api/ProjectTime.Api.csproj");
            if (File.Exists(project)) return directory.FullName;
            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException("The ProjectPulse repository root could not be located.");
}

static string Sanitize(string value) => new(
    value.ToUpperInvariant()
        .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_')
        .ToArray());
