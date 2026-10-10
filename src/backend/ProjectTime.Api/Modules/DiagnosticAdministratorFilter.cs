namespace ProjectTime.Api.Modules;

/// <summary>Endpoint-level authority, independent of URL policy matching.</summary>
internal sealed class DiagnosticAdministratorFilter : IEndpointFilter
{
    private readonly Func<HttpContext, Task<bool>> authority;

    public DiagnosticAdministratorFilter()
        : this(context => ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(
            context, cancellationToken: context.RequestAborted)) { }

    internal DiagnosticAdministratorFilter(Func<HttpContext, Task<bool>> authority)
        => this.authority = authority;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (ProjectPulseActualSessionAuthority.IsViewAs(context.HttpContext))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            if (!await authority(context.HttpContext))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or InvalidOperationException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        return await next(context);
    }
}
