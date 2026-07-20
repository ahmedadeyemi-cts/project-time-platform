namespace ProjectTime.Api.Modules;

public static class DataGovernanceRetentionModule
{
    private const string ContractVersion = "079-recovery-v1";
    public static IEndpointRouteBuilder MapDataGovernanceRetentionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/data-governance-retention");
        group.MapGet("/overview", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "overview")));
        group.MapGet("/domains", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "domains")));
        group.MapGet("/classifications", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "classifications")));
        group.MapGet("/retention-policies", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "retention-policies")));
        group.MapGet("/lineage", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "lineage")));
        group.MapGet("/legal-holds", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "legal-holds")));
        group.MapGet("/privacy-policy", (Func<HttpContext, Task<IResult>>)(c => ReadAsync(c, "privacy-policy")));
        group.MapPost("/records/classify", (Delegate)LockedAsync);
        group.MapPost("/legal-holds/create", (Delegate)LockedAsync);
        group.MapPost("/legal-holds/{id}/release", (Delegate)LockedAsync);
        group.MapPost("/retention/execute", (Delegate)LockedAsync);
        group.MapPost("/privacy/export", (Delegate)LockedAsync);
        group.MapPost("/privacy/delete", (Delegate)LockedAsync);
        return endpoints;
    }

    private static Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        if (ActualUser(context) is null) return Task.FromResult(Results.Unauthorized());
        if (!Allowed(context, false)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Ok(new { module = "079", contractVersion = ContractVersion, surface, phase = "complete-source-shared-integration-deferred", runtime = "locked", liveData = false, boundary = "Classification writes, retention execution, legal holds, exports, deletion, data movement, and persistence are not authorized." }));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (ActualUser(context) is null) return Task.FromResult(Results.Unauthorized());
        if (IsViewAs(context) || !Allowed(context, true)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_079_OPERATION_LOCKED", requestBodyRead = false, message = "Classification writes, retention execution, legal holds, exports, deletion, data movement, and persistence are not authorized." }, statusCode: StatusCodes.Status423Locked));
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
        var permission = "data-governance." + (manage ? "manage" : "view");
        return roleAllowed || context.User.Claims.Any(c => (c.Type is "permission" or "permissions" or "scope") && c.Value.Split(' ').Contains(permission, StringComparer.OrdinalIgnoreCase));
    }
    private static readonly string[] ViewRoles = ["Administrator","Manager","Data Steward","Security Administrator"];
}
