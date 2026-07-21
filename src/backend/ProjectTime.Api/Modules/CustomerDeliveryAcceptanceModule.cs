namespace ProjectTime.Api.Modules;

public static class CustomerDeliveryAcceptanceModule
{
    private const string ContractVersion = "080-operational-read-v2";
    public static IEndpointRouteBuilder MapCustomerDeliveryAcceptanceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/customer-delivery-acceptance");
        group.MapGet("/overview", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "overview")));
        group.MapGet("/engagements", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "engagements")));
        group.MapGet("/milestones", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "milestones")));
        group.MapGet("/artifacts", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "artifacts")));
        group.MapGet("/reviews", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "reviews")));
        group.MapGet("/acceptance-policy", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "acceptance-policy")));
        group.MapGet("/sharing-policy", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "sharing-policy")));
        group.MapPost("/engagements/{id}/invite", (Delegate)LockedAsync);
        group.MapPost("/artifacts/{id}/share", (Delegate)LockedAsync);
        group.MapPost("/reviews/{id}/comment", (Delegate)LockedAsync);
        group.MapPost("/reviews/{id}/accept", (Delegate)LockedAsync);
        group.MapPost("/reviews/{id}/reject", (Delegate)LockedAsync);
        group.MapPost("/shares/{id}/revoke", (Delegate)LockedAsync);
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        var failure = await GovernedOperationsReadModule.AuthorizeAsync(context, "080", ["SUPER_ADMINISTRATOR","ADMINISTRATOR","MANAGER","PROJECT_MANAGER","PROJECT_TEAM_COORDINATOR","SOLUTION_ARCHITECT"], ["CUSTOMER_ACCEPTANCE.VIEW","CUSTOMER_ACCEPTANCE.MANAGE","MANAGE_ALL"]);
        return failure ?? Results.Ok(GovernedOperationsReadModule.OperationalSurface("080", ContractVersion, surface));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (!GovernedOperationsReadModule.HasActualUser(context)) return Task.FromResult(Results.Unauthorized());
        if (GovernedOperationsReadModule.IsViewAs(context)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_080_OPERATION_LOCKED", requestBodyRead = false, message = "External identity, invitations, links, sharing, comments, acceptance, rejection, notifications, and persistence are not authorized." }, statusCode: StatusCodes.Status423Locked));
    }

}
