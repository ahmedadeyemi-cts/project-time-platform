namespace ProjectTime.Api.Modules;

public static class DataGovernanceRetentionModule
{
    private const string ContractVersion = "079-operational-read-v2";
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

    private static async Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        var failure = await GovernedOperationsReadModule.AuthorizeAsync(context, "079", ["SUPER_ADMINISTRATOR","ADMINISTRATOR","MANAGER","DATA_STEWARD","SECURITY_ADMINISTRATOR"], ["DATA_GOVERNANCE.VIEW","DATA_GOVERNANCE.MANAGE","MANAGE_ALL"]);
        return failure ?? Results.Ok(GovernedOperationsReadModule.OperationalSurface("079", ContractVersion, surface));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (!GovernedOperationsReadModule.HasActualUser(context)) return Task.FromResult(Results.Unauthorized());
        if (GovernedOperationsReadModule.IsViewAs(context)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_079_OPERATION_LOCKED", requestBodyRead = false, message = "Classification writes, retention execution, legal holds, exports, deletion, data movement, and persistence are not authorized." }, statusCode: StatusCodes.Status423Locked));
    }

}
