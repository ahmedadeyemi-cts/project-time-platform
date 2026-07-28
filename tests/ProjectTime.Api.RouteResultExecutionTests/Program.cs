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

// This is the exact registration shape that was used by the role-policy reads.
// Task<IResult> can bind to RequestDelegate's Task return type, so ASP.NET awaits
// the task but does not execute the returned IResult.
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
        "ambiguous Task<IResult> method group reproduces the HAR zero-byte response");
    Console.WriteLine("ROLE_POLICY_METHOD_GROUP_EMPTY_200_REPRODUCED=PASS");

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
    Console.WriteLine("ROLE_POLICY_EXPLICIT_IRESULT_JSON=PASS");

    var repositoryRoot = FindRepositoryRoot();
    var compatibilityPath = Path.Combine(
        repositoryRoot,
        "src/backend/ProjectTime.Api/Modules/ScopedRolePolicyResultExecutionCompatibility.cs");
    var registrationPath = Path.Combine(
        repositoryRoot,
        "src/backend/ProjectTime.Api/Modules/GlobalMailConfigurationModule.cs");
    var compatibility = File.ReadAllText(compatibilityPath);
    var registration = File.ReadAllText(registrationPath);

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
            $"COMPATIBILITY_{Sanitize(marker)}",
            compatibility.Contains(marker, StringComparison.Ordinal),
            $"role-policy compatibility source must contain {marker}");
    }

    var executionIndex = registration.IndexOf(
        "app.UseScopedRolePolicyResultExecutionCompatibility();",
        StringComparison.Ordinal);
    var availabilityIndex = registration.IndexOf(
        "app.UseModuleAvailabilityReadContinuityCompatibility();",
        StringComparison.Ordinal);
    Expect(
        "COMPATIBILITY_REGISTERED",
        executionIndex >= 0,
        "explicit result execution compatibility is registered");
    Expect(
        "COMPATIBILITY_BEFORE_AVAILABILITY",
        availabilityIndex >= 0 && executionIndex < availabilityIndex,
        "role-policy result execution runs before the historic availability/endpoint path");

    Console.WriteLine($"ROLE_POLICY_RESULT_EXECUTION_CHECKS={checks}");
    Console.WriteLine("ROLE_POLICY_RESULT_EXECUTION_CONTRACT=PASS");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}

static Task<IResult> MethodGroupResultAsync(HttpContext context) =>
    Task.FromResult(Results.Ok(new
    {
        roles = new[] { "SUPER_ADMINISTRATOR" },
        modules = new[] { "012", "037" }
    }));

static Task<IResult> ExplicitResultAsync(HttpContext context) =>
    Task.FromResult(Results.Ok(new
    {
        roles = new[] { "SUPER_ADMINISTRATOR" },
        modules = new[] { "012", "037" }
    }));

void Expect(string name, bool condition, string evidence)
{
    checks += 1;
    Console.WriteLine(
        $"ROLE_POLICY_RESULT_EXECUTION_{name}={(condition ? "PASSED" : "FAILED")} — {evidence}");
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
