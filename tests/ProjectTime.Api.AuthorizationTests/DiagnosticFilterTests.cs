using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProjectTime.Api.Modules;

internal static class DiagnosticFilterTests
{
    internal static async Task RunAsync()
    {
        var checks = 0;
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        foreach (var path in new[] { "/api/db-config-check", "/api/db-health", "/api/schema/tables",
                     "/api/production-data-readiness", "/api/production/data-readiness" })
        foreach (var spelling in new[] { path, path + "/", path.ToUpperInvariant() + "/" })
        foreach (var mode in new[] { "denied", "allowed", "view_as", "unavailable" })
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = spelling;
            context.Response.Body = new MemoryStream();
            if (mode == "view_as") context.Items["ProjectPulseIsViewAs"] = true;
            var filter = new DiagnosticAdministratorFilter(_ => mode == "unavailable"
                ? Task.FromException<bool>(new InvalidOperationException("test-only provider failure"))
                : Task.FromResult(mode != "denied"));
            var reached = false;
            var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(context), _ =>
            {
                reached = true;
                return ValueTask.FromResult<object?>(Results.StatusCode(204));
            });
            await ((IResult)result!).ExecuteAsync(context);
            var expected = mode == "allowed" ? 204 : mode == "unavailable" ? 503 : 403;
            if (context.Response.StatusCode != expected || reached != (mode == "allowed"))
                throw new Exception("Diagnostic endpoint filter failed: " + mode);
            checks++;
        }
        Console.WriteLine($"DIAGNOSTIC_ENDPOINT_FILTER=PASS assertions={checks}");
    }
}
