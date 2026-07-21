namespace ProjectTime.Api.Modules;

public static class IntegrationEventGatewayModule
{
    private const string ContractVersion = "075-operational-read-v2";
    public static IEndpointRouteBuilder MapIntegrationEventGatewayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/integration-event-gateway");
        group.MapGet("/overview", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "overview")));
        group.MapGet("/sources", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "sources")));
        group.MapGet("/contracts", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "contracts")));
        group.MapGet("/deliveries", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "deliveries")));
        group.MapGet("/dead-letter-policy", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "dead-letter-policy")));
        group.MapGet("/security-policy", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "security-policy")));
        group.MapPost("/events/intake", (Delegate)LockedAsync);
        group.MapPost("/events/{id}/replay", (Delegate)LockedAsync);
        group.MapPost("/events/{id}/quarantine", (Delegate)LockedAsync);
        group.MapPost("/deliveries/{id}/retry", (Delegate)LockedAsync);
        group.MapPost("/connectors/{code}/test", (Delegate)LockedAsync);
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        var failure = await GovernedOperationsReadModule.AuthorizeAsync(context, "075", ["SUPER_ADMINISTRATOR","ADMINISTRATOR","MANAGER","INTEGRATION_ADMINISTRATOR","SECURITY_ADMINISTRATOR"], ["INTEGRATION_EVENTS.VIEW","INTEGRATION_EVENTS.MANAGE","MANAGE_ALL"]);
        if (failure is not null) return failure;
        var content = surface switch
        {
            "overview" => ("Gateway readiness", "ProjectPulse event automation control plane.", "Register source", new object[] { new { name = "ProjectPulse application events", status = "Ready for governed registration", mode = "internal" } }),
            "sources" => ("Event sources", "Systems allowed to submit governed events.", "Register source", Array.Empty<object>()),
            "contracts" => ("Event contracts", "Versioned schemas, ownership, validation, and compatibility.", "Create contract", Array.Empty<object>()),
            "deliveries" => ("Delivery history", "Attempts, outcomes, correlation IDs, and safe retry state.", "Inspect deliveries", Array.Empty<object>()),
            "dead-letter-policy" => ("Dead-letter policy", "Failed events remain contained until reviewed.", "Configure policy", new object[] { new { rule = "Unsafe or invalid payload", action = "Quarantine", execution = "Approval required" } }),
            _ => ("Security policy", "Secrets, payloads, and connector authority remain protected.", "Review controls", new object[] { new { control = "No secret values in events or logs", status = "Enforced" }, new { control = "Replay and delivery require authorization", status = "Enforced" } })
        };
        return Results.Ok(GovernedOperationsReadModule.Surface("075", ContractVersion, surface, content.Item1, content.Item2, content.Item3, content.Item4));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (!GovernedOperationsReadModule.HasActualUser(context)) return Task.FromResult(Results.Unauthorized());
        if (GovernedOperationsReadModule.IsViewAs(context)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_075_OPERATION_LOCKED", requestBodyRead = false, message = "Webhook intake, connector calls, delivery, replay, quarantine, persistence, notifications, secret access, and AI execution are not authorized." }, statusCode: StatusCodes.Status423Locked));
    }

}
