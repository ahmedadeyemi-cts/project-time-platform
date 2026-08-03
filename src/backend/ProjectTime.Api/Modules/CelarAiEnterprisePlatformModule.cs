using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class CelarAiEnterprisePlatformModule
{
    public const string ArchitectureRoute = "/api/celar-ai/v1/architecture";

    public static IEndpointRouteBuilder MapCelarAiEnterprisePlatformEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            CelarAiEnterprisePlatformPolicy.ReadinessRoute,
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiEnterprisePlatformService, CancellationToken, Task<IResult>>)GetReadinessAsync);
        endpoints.MapGet(
            ArchitectureRoute,
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)GetArchitectureAsync);
        endpoints.MapPost(
            CelarAiEnterprisePlatformPolicy.ComposeRoute,
            (Func<CelarAiComposeRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiEnterprisePlatformService, CancellationToken, Task<IResult>>)ComposeAsync);
        endpoints.MapCelarAiCapabilityRoutingEndpoints();
        endpoints.MapCelarAiProductionPlatformEndpoints();
        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiEnterprisePlatformService platform,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        return Results.Ok(new
        {
            module = "011",
            brand = CelarAiBrandProfile.BrandName,
            status = "celar_ai_enterprise_platform_readiness_loaded",
            readiness = await platform.GetReadinessAsync(cancellationToken),
            access = AccessEvidence(identity.Value, access),
            stateChanged = false
        });
    }

    private static async Task<IResult> GetArchitectureAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        return Results.Ok(new
        {
            module = "011",
            status = "celar_ai_architecture_loaded",
            architectureVersion = "celar-ai-private-first-architecture-v4-production-platform",
            title = "Celar AI Private-First Enterprise Architecture",
            createdBy = "Dr. Ahmed Adeyemi",
            company = "US Signal",
            platform = "Pulse",
            layers = new object[]
            {
                new { id = "experience", label = "Pulse users and Celar AI experiences", items = new[] { "Help & Search", "People & Work", "Timesheet", "SOW", "FlowHive", "Reports", "APIs", "Troubleshooting" } },
                new { id = "authorization", label = "Authentication, roles, permissions, and record scope", items = new[] { "Actual user", "Effective user", "Module/action access", "Project/customer/team scope", "View-As read-only" } },
                new { id = "intent", label = "Intent-first production orchestration", items = new[] { "Utility facts", "Procedures", "People/work", "APIs", "Troubleshooting", "Documents", "Financials", "Planning" } },
                new { id = "routing", label = "Module 064 capability route", items = new[] { "Primary", "Secondary", "Tertiary", "Final fallback", "Per-capability policy", "Immutable privacy guardrails" } },
                new { id = "private_knowledge", label = "Private document retrieval", items = new[] { "SOW", "GSD", "IQS", "Email evidence", "Design", "Architecture", "Project evidence", "Citations" } },
                new { id = "governed_tools", label = "Governed live-data tools", items = new[] { "Projects", "Time", "Capacity", "Financials", "APIs", "Diagnostics" } },
                new { id = "private_intelligence", label = "Primary: private Celar AI intelligence", items = new[] { "Private RAG", "Private model", "Deterministic calculations", "Solution composer" } },
                new { id = "confidence", label = "Claim, confidence, and evidence assessment", items = new[] { "Coverage", "Freshness", "Conflicts", "Citation completeness", "Trust classification" } },
                new { id = "external", label = "Optional sanitized external stages", items = new[] { "DLP capsule", "Secondary: Claude", "Tertiary: OpenAI", "Generic problem only" } },
                new { id = "local", label = "Final: governed local template", items = new[] { "Deterministic", "Always available", "No remote call", "Review-only output" } },
                new { id = "verification", label = "Private evidence reassembly and verification", items = new[] { "Re-ground", "Remove unsupported claims", "Apply generic guidance privately", "Human review" } },
                new { id = "lifecycle", label = "Model lifecycle control plane", items = new[] { "Datasets", "Fine-tuning", "Evaluations", "Model registry", "Deployment plans", "Rollback evidence" } },
                new { id = "result", label = "Detailed cited answer or reviewable draft", items = new[] { "Timesheet description", "SOW draft", "Plan", "Timeline", "Diagram", "Closeout communication", "Troubleshooting answer" } }
            },
            flow = new[]
            {
                "experience->authorization",
                "authorization->intent",
                "intent->routing",
                "routing->private_knowledge",
                "routing->governed_tools",
                "private_knowledge->private_intelligence",
                "governed_tools->private_intelligence",
                "private_intelligence->confidence",
                "confidence->verification",
                "confidence->external",
                "external->verification",
                "external->local",
                "local->verification",
                "verification->result",
                "lifecycle->private_intelligence"
            },
            defaultCapabilityRoute = CelarAiCapabilityTargets.DefaultOrder,
            privacy = new
            {
                privateFirst = true,
                authorizationBeforeRetrieval = true,
                intentBeforeToolSelection = true,
                configurableTargetOrder = true,
                privacyPolicyChangedByRouteOrder = false,
                publicProviderReceivesRawDocuments = false,
                publicProviderReceivesCustomerIdentity = false,
                publicProviderReceivesPeopleRecords = false,
                publicProviderReceivesFinancialValues = false,
                module064IsOnlyExternalProviderBoundary = true,
                safetyRefusalStopsRouting = true
            },
            access = AccessEvidence(identity.Value, access),
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> ComposeAsync(
        CelarAiComposeRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiEnterprisePlatformService platform,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);

        var result = await platform.ComposeAsync(
            identity.Value.Actual,
            identity.Value.Effective,
            request,
            context,
            cancellationToken);
        var response = new
        {
            module = "011",
            brand = CelarAiBrandProfile.BrandName,
            feature = "celar_ai_enterprise_solution_composer",
            access = AccessEvidence(identity.Value, access),
            result = result.ToPublicResponse(),
            stateChanged = false
        };

        return result.Status.EndsWith("failed", StringComparison.OrdinalIgnoreCase)
            ? Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(response);
    }

    private static (Guid Actual, Guid Effective)? Identities(HttpContext context)
    {
        var effective = UserId(context, "ProjectPulseEffectiveUserId")
            ?? UserId(context, "ProjectPulseSessionUserId");
        if (effective is null) return null;
        var actual = UserId(context, "ProjectPulseActualUserId")
            ?? UserId(context, "ProjectPulseSessionUserId")
            ?? effective.Value;
        return (actual, effective.Value);
    }

    private static Guid? UserId(HttpContext context, string key) =>
        context.Items.TryGetValue(key, out var value) && value is Guid id ? id : null;

    private static object AccessEvidence(
        (Guid Actual, Guid Effective) identity,
        PulseAiSystemAccess access) => new
    {
        actualUserId = identity.Actual,
        effectiveUserId = identity.Effective,
        isViewAs = identity.Actual != identity.Effective,
        roles = access.RoleCodes.OrderBy(value => value).ToArray(),
        canAsk = access.CanAsk,
        stateMutationAuthorized = false,
        serverAuthorized = true
    };

    private static IResult SessionRequired() => Results.Json(new
    {
        module = "011",
        status = "session_required",
        message = "A valid Pulse session is required to use Celar AI."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden(string permission) => Results.Json(new
    {
        module = "011",
        status = "forbidden",
        requiredPermission = permission,
        message = "The current effective user is not authorized for this Celar AI operation."
    }, statusCode: StatusCodes.Status403Forbidden);
}