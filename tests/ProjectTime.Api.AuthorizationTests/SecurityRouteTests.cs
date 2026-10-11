using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;

internal static class SecurityRouteTests
{
    internal static async Task RunAsync()
    {
        await DiagnosticFilterTests.RunAsync();
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var invoke = typeof(SecurityHardeningModule).GetMethod("InvokeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        // Invoke the actual production middleware, with a sentinel endpoint that must never run.
        foreach (var route in new[] { "/api/production-data-readiness", "/api/production/data-readiness",
            "/api/auth/sso/callback", "/api/work-register/projects/documents/11111111-1111-1111-1111-111111111111/download",
            "/api/project-intake/documents/11111111-1111-1111-1111-111111111111/download" })
        foreach (var path in new[] { route, route + "/", route.ToUpperInvariant() + "/",
            route.Replace("11111111-1111-1111-1111-111111111111", "11111111111111111111111111111111") + "/" })
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = path;
            context.Request.Method = "GET";
            context.Response.Body = new MemoryStream();
            var reached = false;
            Func<Task> next = () => { reached = true; return Task.CompletedTask; };
            await (Task)invoke.Invoke(null, new object[] { context, next })!;
            Check(!reached, "Alternate route spelling cannot reach an unguarded endpoint: " + path);
            if (route.EndsWith("callback"))
                Check(context.Response.StatusCode == 302 && context.Response.Headers.Location.ToString().Contains("invalid_browser_state"),
                    "SSO callback rejects missing browser state with every route spelling");
            else Check(context.Response.StatusCode == 401, "Protected endpoint requires authentication: " + path);
        }
        foreach (var route in new[] { "/api/expenses/summary", "/api/invoicing/summary" })
        foreach (var path in new[] { route, route + "/", route.ToUpperInvariant() + "/", route.Replace("/summary", "//summary/") })
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Path = path;
            context.Request.Method = "GET";
            context.Items["ProjectPulseIsViewAs"] = true;
            context.Items["ProjectPulseSessionUserId"] = Guid.NewGuid();
            context.Items["ProjectPulseEffectiveUserId"] = Guid.NewGuid();
            context.Response.Body = new MemoryStream();
            var reached = false;
            Func<Task> next = () => { reached = true; return Task.CompletedTask; };
            await (Task)invoke.Invoke(null, new object[] { context, next })!;
            Check(!reached && context.Response.StatusCode == 403,
                "Financial summary rejects View-As before effective identity lookup: " + path);
        }
        var requiredPolicy = typeof(SecurityHardeningModule).GetMethod("RequiredPolicy", BindingFlags.Static | BindingFlags.NonPublic)!;
        var policyAllows = typeof(SecurityHardeningModule).GetMethod("PolicyAllows", BindingFlags.Static | BindingFlags.NonPublic)!;
        var accessType = typeof(SecurityHardeningModule).GetNestedType("AccessContext", BindingFlags.NonPublic)!;
        foreach (var role in new[] { "ENGINEER", "PROJECT_MANAGEMENT", "PROJECT_TEAM_COORDINATOR", "ACCOUNTING", "FINANCE", "BILLING", "EXECUTIVE", "ADMINISTRATOR", "SUPER_ADMINISTRATOR" })
        foreach (var permissions in new[] { new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MANAGE_ALL", "VIEW_EXPENSES" } })
        foreach (var route in new[] { "/api/expenses/summary", "/api/invoicing/summary" })
        foreach (var path in new[] { route, route + "/", route.ToUpperInvariant() + "/", route.Replace("/summary", "//summary/") })
        {
            var policy = requiredPolicy.Invoke(null, new object[] { CanonicalApiPaths.Normalize(path), "GET" })!;
            // Broad permissions must not turn a non-finance role into an invoice reader.
            var access = Activator.CreateInstance(accessType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { new HashSet<string>(StringComparer.OrdinalIgnoreCase) { role },
                    permissions }, null)!;
            var allowed = (bool)policyAllows.Invoke(null, new[] { policy, access })!;
            var expected = role is "ACCOUNTING" or "FINANCE" or "BILLING" or "EXECUTIVE" or "ADMINISTRATOR" or "SUPER_ADMINISTRATOR"
                || (role == "PROJECT_TEAM_COORDINATOR" && permissions.Contains("VIEW_EXPENSES") && route == "/api/expenses/summary");
            Check(allowed == expected, "Summary role scope: " + role + " " + path);
        }
        var id = Guid.NewGuid();
        foreach (var format in new[] { "N", "D", "B", "P" })
            Check(CanonicalApiPaths.Normalize($"/api/project-intake/{id.ToString(format)}/project-link/") == $"/api/project-intake/{id:D}/project-link",
                "Intake GUID variants use the same object scope");
        var correlation = typeof(PulseAiSystemIntelligenceService).GetMethod("CorrelationId", BindingFlags.Static | BindingFlags.NonPublic)!;
        var request = new DefaultHttpContext();
        request.Request.Headers["X-Correlation-ID"] = "repeated-client-id";
        request.TraceIdentifier = "repeated-client-id";
        var first = (string)correlation.Invoke(null, new object[] { request })!;
        var second = (string)correlation.Invoke(null, new object[] { request })!;
        Check(first != second && first != "repeated-client-id" && second != "repeated-client-id",
            "Caller correlation IDs cannot collide with persisted inquiry identities");
        Console.WriteLine($"SECURITY_ROUTE_REGRESSIONS=PASS assertions={checks}");
    }
}
