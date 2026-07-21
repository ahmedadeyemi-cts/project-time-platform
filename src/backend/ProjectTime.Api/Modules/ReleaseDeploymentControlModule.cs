namespace ProjectTime.Api.Modules;

public static class ReleaseDeploymentControlModule
{
    private const string ContractVersion = "077-operational-read-v2";
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

    private static async Task<IResult> ReadAsync(HttpContext context, string surface)
    {
        var failure = await GovernedOperationsReadModule.AuthorizeAsync(context, "077", ["SUPER_ADMINISTRATOR","ADMINISTRATOR","MANAGER","RELEASE_MANAGER","ENGINEERING_TEAM_LEAD"], ["RELEASE_CONTROL.VIEW","RELEASE_CONTROL.MANAGE","MANAGE_ALL"]);
        return failure ?? Results.Ok(GovernedOperationsReadModule.OperationalSurface("077", ContractVersion, surface));
    }

    private static Task<IResult> LockedAsync(HttpContext context)
    {
        if (!GovernedOperationsReadModule.HasActualUser(context)) return Task.FromResult(Results.Unauthorized());
        if (GovernedOperationsReadModule.IsViewAs(context)) return Task.FromResult(Results.Forbid());
        return Task.FromResult(Results.Json(new { code = "MODULE_077_OPERATION_LOCKED", requestBodyRead = false, message = "Deployment promotion, rollback, pipeline, repository, cloud execution, notifications, and persistence are not authorized." }, statusCode: StatusCodes.Status423Locked));
    }

}
