namespace ProjectTime.Api.Modules;

public static class CustomerDeliveryAcceptanceModule
{
    private const string ContractVersion = "080-recovery-v1";
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

    private static Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        if (ActualUser(context) is null) return Task.FromResult(Results.Unauthorized());
        if (!Allowed(context, false)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Ok(new { module = "080", contractVersion = ContractVersion, surface, phase = "complete-source-shared-integration-deferred", runtime = "locked", liveData = false, boundary = "External identity, invitations, links, sharing, comments, acceptance, rejection, notifications, and persistence are not authorized." }));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (ActualUser(context) is null) return Task.FromResult(Results.Unauthorized());
        if (IsViewAs(context) || !Allowed(context, true)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_080_OPERATION_LOCKED", requestBodyRead = false, message = "External identity, invitations, links, sharing, comments, acceptance, rejection, notifications, and persistence are not authorized." }, statusCode: StatusCodes.Status423Locked));
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
        var permission = "customer-acceptance." + (manage ? "manage" : "view");
        return roleAllowed || context.User.Claims.Any(c => (c.Type is "permission" or "permissions" or "scope") && c.Value.Split(' ').Contains(permission, StringComparer.OrdinalIgnoreCase));
    }
    private static readonly string[] ViewRoles = ["Administrator","Manager","Project Manager","Project Team Coordinator","Solution Architect"];
}
