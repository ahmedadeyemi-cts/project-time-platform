namespace ProjectTime.Api.Modules;

public static class ReleaseDeploymentControlModule
{
    private const string ContractVersion = "077-recovery-v1";
    public static IEndpointRouteBuilder MapReleaseDeploymentControlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/release-deployment-control");
        group.MapGet("/overview", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "overview")));
        group.MapGet("/releases", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "releases")));
        group.MapGet("/environments", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "environments")));
        group.MapGet("/gates", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "gates")));
        group.MapGet("/evidence", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "evidence")));
        group.MapGet("/rollback-policy", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "rollback-policy")));
        group.MapPost("/releases/prepare", (Delegate)LockedAsync);
        group.MapPost("/releases/{id}/approve", (Delegate)LockedAsync);
        group.MapPost("/releases/{id}/promote", (Delegate)LockedAsync);
        group.MapPost("/releases/{id}/verify", (Delegate)LockedAsync);
        group.MapPost("/releases/{id}/rollback", (Delegate)LockedAsync);
        return endpoints;
    }

    private static Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        if (ActualUser(context) is null) return Task.FromResult(Results.Unauthorized());
        if (!Allowed(context, false)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Ok(new { module = "077", contractVersion = ContractVersion, surface, phase = "complete-source-shared-integration-deferred", runtime = "locked", liveData = false, boundary = "Deployment promotion, rollback, pipeline, repository, cloud execution, notifications, and persistence are not authorized." }));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (ActualUser(context) is null) return Task.FromResult(Results.Unauthorized());
        if (IsViewAs(context) || !Allowed(context, true)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_077_OPERATION_LOCKED", requestBodyRead = false, message = "Deployment promotion, rollback, pipeline, repository, cloud execution, notifications, and persistence are not authorized." }, statusCode: StatusCodes.Status423Locked));
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
        var permission = "release-control." + (manage ? "manage" : "view");
        return roleAllowed || context.User.Claims.Any(c => (c.Type is "permission" or "permissions" or "scope") && c.Value.Split(' ').Contains(permission, StringComparer.OrdinalIgnoreCase));
    }
    private static readonly string[] ViewRoles = ["Administrator","Manager","Release Manager","Engineering Team Lead"];
}
