using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class PulseAiSystemOperationsModule
{
    public static IEndpointRouteBuilder MapPulseAiSystemOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/pulse-ai/v1/answer",
            (Func<PulseAiUnifiedHelpRequest, HttpContext, PulseAiUnifiedAnswerService, CancellationToken, Task<IResult>>)AnswerAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system-operations/readiness",
            (Func<HttpContext, PulseAiSystemOperationsService, CancellationToken, Task<IResult>>)ReadinessAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/system-operations/answer",
            (Func<PulseAiSystemOperationsQuestionRequest, HttpContext, PulseAiSystemOperationsService, CancellationToken, Task<IResult>>)OperationsAnswerAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system-operations/apis",
            (Func<HttpContext, PulseAiSystemOperationsService, CancellationToken, Task<IResult>>)ApisAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system-operations/history",
            (Func<HttpContext, PulseAiSystemOperationsService, CancellationToken, Task<IResult>>)HistoryAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system-operations/investigations/{investigationId:guid}",
            (Func<Guid, HttpContext, PulseAiSystemOperationsService, CancellationToken, Task<IResult>>)InvestigationAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/system-operations/apis/{apiId}/retest",
            (Func<string, PulseAiSystemOperationsRetestRequest, HttpContext, PulseAiSystemOperationsService, CancellationToken, Task<IResult>>)RetestAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/future-enhancements/plan",
            (Func<PulseAiFutureEnhancementRequest, HttpContext, PulseAiFutureEnhancementPlanner, CancellationToken, Task<IResult>>)FutureEnhancementAsync);
        return endpoints;
    }

    private static async Task<IResult> AnswerAsync(
        PulseAiUnifiedHelpRequest request,
        HttpContext context,
        PulseAiUnifiedAnswerService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var answer = await service.AnswerAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            context,
            cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = PulseAiSystemOperationsPolicy.UnifiedHelpFeatureCode,
            access = AccessEvidence(context, identities.Value),
            response = answer.ToPublicResponse(),
            externalProviderCalled = false,
            rawInternalDocumentSentExternally = false,
            productionChangingActionPerformed = false
        });
    }

    private static async Task<IResult> ReadinessAsync(
        HttpContext context,
        PulseAiSystemOperationsService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        return Results.Ok(new
        {
            module = "011",
            feature = PulseAiSystemOperationsPolicy.FeatureCode,
            access = AccessEvidence(context, identities.Value),
            readiness = await service.GetReadinessAsync(identities.Value.Actual, cancellationToken),
            relatedModules = new[]
            {
                new { module = "013", role = "live API inventory, runtime, dependency, and safe retest authority", route = "#service-control" },
                new { module = "016", role = "sanitized correlation evidence and operational history", route = "#backup-retention" },
                new { module = "076", role = "defect intake and resolution tracking", route = "#defect-tracker" },
                new { module = "077", role = "release, deployment, validation, and rollback governance", route = "#release-deployment-control" },
                new { module = "078", role = "durable observability, SLO, alert, and retention contracts", route = "#observability-slo-health" },
                new { module = "998", role = "persistent diagnostic sessions and controlled remediation", route = "#system-diagnostics" }
            },
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> OperationsAnswerAsync(
        PulseAiSystemOperationsQuestionRequest request,
        HttpContext context,
        PulseAiSystemOperationsService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var result = await service.AskAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            context,
            cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = PulseAiSystemOperationsPolicy.FeatureCode,
            access = AccessEvidence(context, identities.Value),
            result = result.ToPublicResponse(),
            sources = new[] { "Module 013", "Module 016", "Module 998", "sanitized client diagnostics", "running ASP.NET endpoint metadata" },
            stateChanged = result.Persisted,
            productionChangingActionPerformed = false
        });
    }

    private static async Task<IResult> ApisAsync(
        HttpContext context,
        PulseAiSystemOperationsService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var query = context.Request.Query;
        var limit = int.TryParse(query["limit"], out var requested) ? Math.Clamp(requested, 1, 500) : 200;
        var result = await service.ListApisAsync(
            identities.Value.Actual,
            query["search"],
            query["module"],
            query["status"],
            limit,
            context,
            cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = "pulse_ai_live_api_inventory",
            access = AccessEvidence(context, identities.Value),
            inventory = result,
            stateChanged = false
        });
    }

    private static async Task<IResult> HistoryAsync(
        HttpContext context,
        PulseAiSystemOperationsService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var limit = int.TryParse(context.Request.Query["limit"], out var requested)
            ? Math.Clamp(requested, 1, 200)
            : 50;
        var access = await service.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!service.CanViewHistory(access)) return Forbidden(PulseAiSystemOperationsPolicy.HistoryPermission);
        return Results.Ok(new
        {
            module = "011",
            feature = "pulse_ai_system_operations_history",
            access = AccessEvidence(context, identities.Value),
            investigations = await service.ListHistoryAsync(identities.Value.Actual, limit, cancellationToken),
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> InvestigationAsync(
        Guid investigationId,
        HttpContext context,
        PulseAiSystemOperationsService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!service.CanViewHistory(access)) return Forbidden(PulseAiSystemOperationsPolicy.HistoryPermission);
        var result = await service.GetInvestigationAsync(
            investigationId,
            identities.Value.Actual,
            cancellationToken);
        return result is null
            ? Results.Json(new
            {
                module = "011",
                status = "investigation_not_found_or_not_authorized"
            }, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(new
            {
                module = "011",
                feature = "pulse_ai_system_operations_investigation",
                access = AccessEvidence(context, identities.Value),
                result,
                generatedAt = DateTimeOffset.UtcNow
            });
    }

    private static async Task<IResult> RetestAsync(
        string apiId,
        PulseAiSystemOperationsRetestRequest request,
        HttpContext context,
        PulseAiSystemOperationsService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        if (identities.Value.Actual != identities.Value.Effective || IsViewAs(context)) return ViewAsMutationBlocked();
        var access = await service.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!service.CanRetest(access)) return Forbidden(PulseAiSystemOperationsPolicy.RetestPermission);
        if (!string.Equals(
                request.Confirmation?.Trim(),
                PulseAiSystemOperationsPolicy.SafeRetestConfirmation,
                StringComparison.Ordinal))
        {
            return Results.Json(new
            {
                module = "011",
                status = "confirmation_required",
                requiredConfirmation = PulseAiSystemOperationsPolicy.SafeRetestConfirmation,
                message = "Safe API retest requires the exact confirmation. The retest sends a same-origin GET request, forwards the current session, records status and latency, and does not read the response body."
            }, statusCode: StatusCodes.Status409Conflict);
        }

        return await PlatformOperationsModule.RetestPulseAiSafeApiAsync(apiId, context);
    }

    private static async Task<IResult> FutureEnhancementAsync(
        PulseAiFutureEnhancementRequest request,
        HttpContext context,
        PulseAiFutureEnhancementPlanner planner,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        if (!await planner.CanPlanAsync(identities.Value.Effective, cancellationToken))
            return Forbidden(PulseAiSystemOperationsPolicy.FutureEnhancementPermission);
        var plan = await planner.PlanAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            context,
            cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = PulseAiSystemOperationsPolicy.FutureEnhancementFeatureCode,
            access = AccessEvidence(context, identities.Value),
            result = plan.ToPublicResponse(),
            stateChanged = plan.Persisted,
            implementationPerformed = false,
            deploymentPerformed = false
        });
    }

    private static (Guid Actual, Guid Effective)? Identities(HttpContext context)
    {
        var effective = UserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        if (effective is null) return null;
        var actual = UserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId") ?? effective.Value;
        return (actual, effective.Value);
    }

    private static Guid? UserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid id) return id;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static object AccessEvidence(
        HttpContext context,
        (Guid Actual, Guid Effective) identities) => new
        {
            actualUserId = identities.Actual,
            effectiveUserId = identities.Effective,
            isViewAs = identities.Actual != identities.Effective || IsViewAs(context),
            mutationAuthorityTransferred = false,
            serverAuthorized = true
        };

    private static IResult SessionRequired() =>
        Results.Json(new
        {
            module = "011",
            status = "session_required",
            message = "A valid Pulse session is required."
        }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden(string permission) =>
        Results.Json(new
        {
            module = "011",
            status = "forbidden",
            requiredPermission = permission,
            message = "The current identity is not authorized for this Pulse AI operation."
        }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsMutationBlocked() =>
        Results.Json(new
        {
            module = "011",
            status = "view_as_mutation_blocked",
            message = "Administrator View-As is read-only and cannot run a safe API retest."
        }, statusCode: StatusCodes.Status403Forbidden);
}
