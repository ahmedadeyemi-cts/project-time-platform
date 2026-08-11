using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Server-authoritative operational intent route. Browser-side phrase matching
/// may provide a convenience fallback only and can never authorize a mutation.
/// </summary>
public static partial class CelarAiProductionPlatformModule
{
    public static IEndpointRouteBuilder MapCelarAiOperationsIntentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/celar-ai/v1/operations/intent",
            (Func<CelarAiOperationsIntentRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)ResolveOperationsIntentAsync);
        return endpoints;
    }

    private static async Task<IResult> ResolveOperationsIntentAsync(
        CelarAiOperationsIntentRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CancellationToken cancellationToken)
    {
        var identities = OperationsIntentIdentities(context);
        if (identities is null) return Results.Unauthorized();
        var access = await system.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk)
        {
            return Results.Json(new
            {
                module = "011",
                status = "operations_intent_forbidden",
                permission = PulseAiSystemIntelligencePolicy.AskPermission,
                stateChanged = false
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        var decision = CelarAiOperationsIntentRouter.Route(request.Question);
        return Results.Ok(new
        {
            module = "011",
            feature = "ask_celar_ai_operations_intent",
            decision,
            access = new
            {
                actualUserId = identities.Value.Actual,
                effectiveUserId = identities.Value.Effective,
                viewAsActive = identities.Value.Actual != identities.Value.Effective,
                canAsk = access.CanAsk
            },
            serverAuthoritative = true,
            browserFallbackAuthoritative = false,
            stateChanged = false
        });
    }

    private static (Guid Actual, Guid Effective)? OperationsIntentIdentities(HttpContext context)
    {
        var actual = OperationsIntentUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = OperationsIntentUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        return actual.HasValue && effective.HasValue
            ? (actual.Value, effective.Value)
            : null;
    }

    private static Guid? OperationsIntentUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }
}
