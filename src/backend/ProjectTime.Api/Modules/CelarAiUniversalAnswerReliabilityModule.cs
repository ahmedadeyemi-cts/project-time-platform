using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public sealed record CelarAiUniversalAnswerPlanRequest(
    string? Question,
    string? IntentCode = null,
    string? ProjectCode = null,
    string? ProjectName = null,
    string? ModuleCode = null,
    bool IncludeRepositoryContext = false,
    int AttachmentCount = 0);

public static partial class CelarAiProductionPlatformModule
{
    public const string UniversalAnswerReadinessRoute = "/api/celar-ai/v1/reliability/readiness";
    public const string UniversalAnswerPlanRoute = "/api/celar-ai/v1/reliability/plan";
    public const string UniversalAnswerEvaluationRoute = "/api/celar-ai/v1/reliability/evaluation-catalog";

    public static IEndpointRouteBuilder MapCelarAiUniversalAnswerReliabilityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            UniversalAnswerReadinessRoute,
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiUniversalAnswerReliabilityService, CancellationToken, Task<IResult>>)UniversalAnswerReadinessAsync);
        endpoints.MapPost(
            UniversalAnswerPlanRoute,
            (Func<CelarAiUniversalAnswerPlanRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiUniversalAnswerReliabilityService, CancellationToken, Task<IResult>>)UniversalAnswerPlanAsync);
        endpoints.MapGet(
            UniversalAnswerEvaluationRoute,
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiUniversalAnswerReliabilityService, CancellationToken, Task<IResult>>)UniversalAnswerEvaluationAsync);
        return endpoints;
    }

    private static async Task<IResult> UniversalAnswerReadinessAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiUniversalAnswerReliabilityService reliability,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk)
            return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);

        var readiness = reliability.GetReadiness();
        return Results.Ok(new
        {
            module = "011",
            status = readiness.Status,
            readiness,
            domains = CelarAiUniversalToolCatalog.Domains,
            tools = CelarAiUniversalToolCatalog.Tools.Select(tool => new
            {
                tool.Code,
                tool.DisplayName,
                tool.Domain,
                tool.OwningModules,
                tool.Authority,
                tool.Availability,
                tool.AccessPolicy,
                tool.FreshnessClass,
                tool.Deterministic,
                tool.CitationRequired,
                tool.PrivateOnly,
                tool.MutationAllowed,
                tool.RequiredSourceTypes,
                tool.Routes
            }).ToArray(),
            access = new
            {
                actualUserId = identity.Value.Actual,
                effectiveUserId = identity.Value.Effective,
                viewAsActive = identity.Value.Actual != identity.Value.Effective,
                canAsk = access.CanAsk,
                canViewApis = access.CanViewApis,
                canTroubleshoot = access.CanTroubleshoot
            },
            stateChanged = false
        });
    }

    private static async Task<IResult> UniversalAnswerPlanAsync(
        CelarAiUniversalAnswerPlanRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiUniversalAnswerReliabilityService reliability,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk)
            return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);

        var question = Limit(request.Question, system.Options().MaximumQuestionCharacters, string.Empty);
        if (question.Length == 0)
            return Validation("Enter a complete question for the universal answer planner.");
        var plan = reliability.Plan(
            question,
            request.IntentCode,
            request.ProjectCode,
            request.ProjectName,
            request.ModuleCode,
            request.IncludeRepositoryContext,
            Math.Clamp(request.AttachmentCount, 0, CelarAiConversationAttachmentPolicy.MaximumFilesPerRequest));
        var tools = CelarAiUniversalToolCatalog.Tools
            .Where(tool => plan.RequiredToolCodes.Contains(tool.Code, StringComparer.OrdinalIgnoreCase))
            .Select(tool => new
            {
                tool.Code,
                tool.DisplayName,
                tool.Domain,
                tool.OwningModules,
                tool.Authority,
                tool.Availability,
                tool.AccessPolicy,
                tool.RequiredSourceTypes,
                tool.Routes
            })
            .ToArray();
        return Results.Ok(new
        {
            module = "011",
            status = "universal_answer_plan_completed",
            plan,
            tools,
            access = new
            {
                actualUserId = identity.Value.Actual,
                effectiveUserId = identity.Value.Effective,
                viewAsActive = identity.Value.Actual != identity.Value.Effective,
                owningModulesRemainAuthoritative = true,
                recordScopeWidened = false
            },
            privacy = new
            {
                questionSentToProvider = false,
                providerCalled = false,
                databaseQueried = false,
                rawDocumentsRead = false,
                secretsRead = false,
                stateChanged = false
            }
        });
    }

    private static async Task<IResult> UniversalAnswerEvaluationAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiUniversalAnswerReliabilityService reliability,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk)
            return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);

        return Results.Ok(new
        {
            module = "011",
            status = "universal_answer_evaluation_catalog_loaded",
            contractVersion = CelarAiUniversalAnswerReliabilityService.ContractVersion,
            evaluationCaseCount = CelarAiUniversalAnswerReliabilityService.FrozenEvaluationCaseCount,
            categories = new[]
            {
                "identity_permissions",
                "projects_assignments",
                "time_approval_capacity",
                "financial_commercial",
                "documents_retrieval",
                "cross_domain_delivery",
                "planning_forge",
                "operations_security_audit",
                "public_general",
                "ambiguity_privacy_failure"
            },
            requiredMeasurements = new[]
            {
                "answer correctness",
                "source correctness",
                "citation correctness",
                "retrieval recall",
                "retrieval precision",
                "calculation correctness",
                "freshness compliance",
                "permission leakage",
                "private-to-public payload leakage",
                "hallucination rate",
                "refusal correctness",
                "latency and resource use"
            },
            promotionRule = "Every blocker-class privacy and permission test must pass; factual and citation thresholds must meet the documented suite before activation.",
            readiness = reliability.GetReadiness(),
            stateChanged = false
        });
    }
}