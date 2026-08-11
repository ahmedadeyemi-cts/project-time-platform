using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Dynamic capability and execution-adapter manifest shared by Ask Celar AI,
/// Module 011, Module 064, and the reliability workbench. It reports actual
/// readiness honestly and never presents a catalog entry as automatically live.
/// </summary>
public static partial class CelarAiProductionPlatformModule
{
    public static IEndpointRouteBuilder MapCelarAiCapabilityManifestEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/celar-ai/v1/reliability/capability-manifest",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)CapabilityManifestAsync);
        return endpoints;
    }

    private static async Task<IResult> CapabilityManifestAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CancellationToken cancellationToken)
    {
        var identities = CapabilityIdentities(context);
        if (identities is null) return Results.Unauthorized();
        var access = await system.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk)
        {
            return Results.Json(new
            {
                module = "011",
                status = "capability_manifest_forbidden",
                permission = PulseAiSystemIntelligencePolicy.AskPermission,
                stateChanged = false
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(new
        {
            module = "011",
            feature = "celar_ai_capability_manifest",
            status = "capability_manifest_loaded",
            manifestContract = CelarAiCapabilityManifest.ContractVersion,
            adapterRegistryContract = CelarAiExecutionAdapterRegistry.ContractVersion,
            deterministicIntentContract = CelarAiDeterministicIntentPolicy.ContractVersion,
            capabilities = CelarAiCapabilityManifest.Build(),
            adapters = CelarAiExecutionAdapterRegistry.All(),
            access = new
            {
                actualUserId = identities.Value.Actual,
                effectiveUserId = identities.Value.Effective,
                viewAsActive = identities.Value.Actual != identities.Value.Effective,
                permissionScoped = true,
                recordScopeWidened = false
            },
            stateChanged = false,
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static (Guid Actual, Guid Effective)? CapabilityIdentities(HttpContext context)
    {
        var actual = CapabilityUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = CapabilityUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        return actual.HasValue && effective.HasValue
            ? (actual.Value, effective.Value)
            : null;
    }

    private static Guid? CapabilityUserId(HttpContext context, params string[] keys)
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
