using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static partial class CelarAiProductionPlatformModule
{
    public static IEndpointRouteBuilder MapCelarAiDefectQueryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/celar-ai/v1/operations");
        group.MapGet("/defects",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectQueryService, string?, string?, int?, CancellationToken, Task<IResult>>)ListScopedDefectsAsync);
        group.MapGet("/defects/matches",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectQueryService, string?, string?, string?, string?, CancellationToken, Task<IResult>>)FindScopedMatchingDefectsAsync);
        return endpoints;
    }

    private static async Task<IResult> ListScopedDefectsAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectQueryService queries,
        string? status,
        string? search,
        int? limit,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        try
        {
            // Read scope follows the effective View-As identity. The actual user
            // remains the audit actor and every mutation still requires actual
            // and effective identities to be identical.
            var defects = await queries.ListAsync(
                access.Effective,
                CanViewAllDefects(access),
                status,
                search,
                Math.Clamp(limit ?? 100, 1, 500),
                cancellationToken);
            return Results.Ok(new
            {
                module = "076",
                feature = "ask_celar_ai_defect_inventory",
                scope = CanViewAllDefects(access)
                    ? "all_authorized_defects"
                    : "effective_user_reported_or_assigned_defects",
                count = defects.Count,
                defects,
                access = AccessResponse(access),
                stateChanged = false
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "load the permission-scoped Module 076 defect inventory");
        }
    }

    private static async Task<IResult> FindScopedMatchingDefectsAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectQueryService queries,
        string? environment,
        string? affectedModule,
        string? componentCode,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        try
        {
            var defects = await queries.FindMatchesAsync(
                access.Effective,
                CanViewAllDefects(access),
                environment,
                affectedModule,
                componentCode,
                failureCode,
                cancellationToken);
            return Results.Ok(new
            {
                module = "076",
                feature = "ask_celar_ai_defect_match",
                scope = CanViewAllDefects(access)
                    ? "all_authorized_defects"
                    : "effective_user_reported_or_assigned_defects",
                count = defects.Count,
                defects,
                duplicateAutomaticDefectCreated = false,
                stateChanged = false
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "search the permission-scoped Module 076 defect inventory");
        }
    }
}
