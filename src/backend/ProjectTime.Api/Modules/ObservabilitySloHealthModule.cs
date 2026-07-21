namespace ProjectTime.Api.Modules;

public static class ObservabilitySloHealthModule
{
    private const string ContractVersion = "078-recovery-v1";
    public static IEndpointRouteBuilder MapObservabilitySloHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/observability-slo-health");
        group.MapGet("/overview", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "overview")));
        group.MapGet("/services", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "services")));
        group.MapGet("/signals", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "signals")));
        group.MapGet("/slos", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "slos")));
        group.MapGet("/alerts", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "alerts")));
        group.MapGet("/integrations", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "integrations")));
        group.MapGet("/retention-policy", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "retention-policy")));
        group.MapPost("/signals/intake", (Delegate)LockedAsync);
        group.MapPost("/slos/{id}/evaluate", (Delegate)LockedAsync);
        group.MapPost("/alerts/{id}/acknowledge", (Delegate)LockedAsync);
        group.MapPost("/alerts/{id}/resolve", (Delegate)LockedAsync);
        group.MapPost("/connectors/{code}/test", (Delegate)LockedAsync);
        return endpoints;
    }

    private static Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        if (ActualUser(context) is null) return Task.FromResult(Results.Unauthorized());
        if (!Allowed(context, false)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Ok(new { module = "078", contractVersion = ContractVersion, surface, phase = "complete-source-shared-integration-deferred", runtime = "locked", liveData = false, boundary = "Telemetry connectors, signal persistence, alert delivery, external notifications, and remediation are not authorized." }));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (ActualUser(context) is null) return Task.FromResult(Results.Unauthorized());
        if (IsViewAs(context) || !Allowed(context, true)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_078_OPERATION_LOCKED", requestBodyRead = false, message = "Telemetry connectors, signal persistence, alert delivery, external notifications, and remediation are not authorized." }, statusCode: StatusCodes.Status423Locked));
    }

    private static Guid? ActualUser(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseActualUserId", "ProjectPulseSessionUserId" }) if (context.Items.TryGetValue(key, out var value) && Guid.TryParse(value?.ToString(), out var id)) return id;
        var claim = context.User.Claims.FirstOrDefault(c => c.Type is "sub" or "oid" or "user_id")?.Value;
        return Guid.TryParse(claim, out var claimId) ? claimId : null;
    }
    private static bool IsViewAs(HttpContext context) => context.Request.Headers.ContainsKey("X-ProjectPulse-View-As") || context.Request.Headers.ContainsKey("X-ProjectPulse-Effective-User");
    private static bool Allowed(HttpContext context, bool manage)
    {
        var roles = context.User.Claims.Where(c => c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase) || c.Type == "role").Select(c => c.Value);
        var roleAllowed = roles.Any(role => ViewRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
        var permission = "observability." + (manage ? "manage" : "view");
        return roleAllowed || context.User.Claims.Any(c => (c.Type is "permission" or "permissions" or "scope") && c.Value.Split(' ').Contains(permission, StringComparer.OrdinalIgnoreCase));
    }
    private static readonly string[] ViewRoles = ["Administrator","Manager","Engineering Team Lead","Security Administrator"];
}
