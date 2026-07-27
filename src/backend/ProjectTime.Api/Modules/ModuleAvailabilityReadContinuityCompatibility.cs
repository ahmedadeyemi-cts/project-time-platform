namespace ProjectTime.Api.Modules;

/// <summary>
/// Keeps authenticated, read-only administrative operations available when the
/// optional module-availability store cannot be queried. The underlying endpoint
/// authorization remains authoritative. Mutating imports, configuration writes,
/// role changes, and mail delivery are never bypassed here.
/// </summary>
public static class ModuleAvailabilityReadContinuityCompatibility
{
    private const string ModuleHeader = "X-ProjectPulse-Module-Number";

    public static WebApplication UseModuleAvailabilityReadContinuityCompatibility(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!IsContinuityRequest(context)
                || !context.Request.Headers.TryGetValue(ModuleHeader, out var moduleValues)
                || string.IsNullOrWhiteSpace(moduleValues.FirstOrDefault()))
            {
                await next();
                return;
            }

            var requestedModule = moduleValues.FirstOrDefault()!.Trim();
            context.Items["ProjectPulseRequestedModuleNumber"] = requestedModule;
            context.Items["ProjectPulseModuleAvailabilityReadContinuity"] = true;
            context.Request.Headers.Remove(ModuleHeader);

            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey("X-ProjectPulse-Module-Availability"))
                    context.Response.Headers["X-ProjectPulse-Module-Availability"] = "authorized-read-continuity";
                if (!context.Response.Headers.ContainsKey("X-ProjectPulse-Requested-Module"))
                    context.Response.Headers["X-ProjectPulse-Requested-Module"] = requestedModule;
                return Task.CompletedTask;
            });

            await next();
        });
        return app;
    }

    private static bool IsContinuityRequest(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method))
        {
            return path.StartsWith("/api/role-policy/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/runtime/role-policy/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/runtime/v2/role-policy/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/admin/audit-history/events", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/timesheet/timers/targets", StringComparison.OrdinalIgnoreCase);
        }

        if (!HttpMethods.IsPost(context.Request.Method)) return false;
        return path.Equals("/api/admin/azure/users/preview", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/microsoft-integration/sso-test", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/microsoft-integration/test-connection", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/microsoft-integration/mail-runtime/test", StringComparison.OrdinalIgnoreCase);
    }
}
