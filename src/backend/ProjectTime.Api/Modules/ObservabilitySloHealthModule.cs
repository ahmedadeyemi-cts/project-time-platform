namespace ProjectTime.Api.Modules;

public static class ObservabilitySloHealthModule
{
    private const string ContractVersion = "078-operational-read-v2";
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

    private static async Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        var failure = await GovernedOperationsReadModule.AuthorizeAsync(context, "078", ["SUPER_ADMINISTRATOR","ADMINISTRATOR","MANAGER","ENGINEERING_TEAM_LEAD","SECURITY_ADMINISTRATOR"], ["OBSERVABILITY.VIEW","OBSERVABILITY.MANAGE","MANAGE_ALL"]);
        return failure ?? Results.Ok(GovernedOperationsReadModule.OperationalSurface("078", ContractVersion, surface));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (!GovernedOperationsReadModule.HasActualUser(context)) return Task.FromResult(Results.Unauthorized());
        if (GovernedOperationsReadModule.IsViewAs(context)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_078_OPERATION_LOCKED", requestBodyRead = false, message = "Telemetry connectors, signal persistence, alert delivery, external notifications, and remediation are not authorized." }, statusCode: StatusCodes.Status423Locked));
    }

}
