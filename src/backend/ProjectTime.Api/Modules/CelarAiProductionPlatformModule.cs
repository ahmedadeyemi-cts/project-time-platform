using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public sealed record CelarAiProductionChatRequest(
    Guid? ConversationId,
    string? Question,
    string? Mode = null,
    string? DetailLevel = "comprehensive",
    string? ProjectCode = null,
    string? ProjectName = null,
    string? ModuleCode = null,
    string? ApiSearch = null,
    bool IncludeAuthorizedProjectDocuments = true,
    bool UsePrivateModelWhenAvailable = true,
    string? ClientTimeZone = null);

public sealed record CelarAiDatasetRequest(
    string? Name,
    string? Purpose,
    string? Classification,
    string? ArtifactUri,
    string? Sha256,
    int ExampleCount,
    string? State = "reviewed");

public sealed record CelarAiTrainingRequest(
    Guid DatasetVersionId,
    string? Method,
    string? BaseModel,
    object? Configuration);

public sealed record CelarAiEvaluationRequest(
    string? SuiteCode = "basic_competency",
    Guid? ModelVersionId = null);

public sealed record CelarAiModelRequest(
    string? Name,
    string? SemanticVersion,
    string? BaseModel,
    string? ArtifactUri,
    string? Sha256,
    Guid? DatasetVersionId,
    Guid? TrainingJobId,
    Guid? EvaluationRunId,
    string? State = "draft");

public sealed record CelarAiDeploymentRequest(
    Guid ModelVersionId,
    string? Environment,
    string? CapabilityCode,
    Guid? RollbackModelVersionId = null);

public sealed record CelarAiFlowHiveProductionRequest(
    ProjectFlowHivePlanRequest? Plan,
    string? GsdExcerpt,
    string? SowExcerpt,
    string? RequestedOutcome,
    string? DetailLevel = "comprehensive",
    string? DiagramType = "flowchart",
    bool AllowSanitizedExternalFallback = false);

/// <summary>
/// Complete production control plane for Module 011 and the detailed, read-only
/// Celar AI generation path for Module 066. Lifecycle writes require the actual
/// administrator session; View-As remains read-only. Generated delivery output
/// is never persisted, baselined, assigned, approved, published, or committed.
/// </summary>
public static partial class CelarAiProductionPlatformModule
{
    public const string ContractVersion = "celar-ai-production-platform-v1-20260803";
    public const string SchemaVersion = "celar_ai_production_platform_runtime_v1";
    public const string ChatRoute = "/api/celar-ai/v2/chat";
    public const string FlowHiveRoute = "/api/project-flowhive/ai/production-generate";

    private static readonly HashSet<string> ManagementRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "ADMINISTRATOR"
    };

    private static readonly (string Code, string Question, string Intent, string Trust)[] CompetencyCases =
    [
        ("current_day", "What day is it today?", "current_date_time", "verified_current_fact"),
        ("current_time", "What time is it?", "current_date_time", "verified_current_fact"),
        ("system_version", "What is the current system version?", "system_version", "verified_current_fact"),
        ("capabilities", "What can Celar AI answer?", "capabilities", "platform_capability"),
        ("identity", "What is Celar AI?", "identity", "platform_capability"),
        ("enter_time", "How do I enter my time?", "procedure", "procedure"),
        ("submit_time", "How do I submit my Timesheet?", "procedure", "procedure"),
        ("create_project", "How do I create a project?", "procedure", "procedure"),
        ("upload_sow", "How do I upload a SOW?", "procedure", "procedure"),
        ("team_work", "What is my team working on this week?", "people_activity", "verified_with_limitations"),
        ("api_inventory", "Which APIs are running?", "api_inventory", "verified_current_fact"),
        ("http_403", "Why did this request return 403?", "troubleshooting", "verified_with_limitations"),
        ("future", "Design a future enhancement for Pulse.", "future_enhancement", "draft")
    ];

    public static IEndpointRouteBuilder MapCelarAiProductionPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/celar-ai/v1/production/readiness",
            (Func<HttpContext, PulseAiSystemIntelligenceService, PulseAiPrivateRagService, CelarAiEnterprisePlatformService, CancellationToken, Task<IResult>>)ReadinessAsync);
        endpoints.MapPost("/api/celar-ai/v1/production/schema/initialize",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)InitializeSchemaAsync);
        endpoints.MapGet("/api/celar-ai/v1/production/datasets",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)ListDatasetsAsync);
        endpoints.MapPost("/api/celar-ai/v1/production/datasets",
            (Func<CelarAiDatasetRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)CreateDatasetAsync);
        endpoints.MapGet("/api/celar-ai/v1/production/training-jobs",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)ListTrainingAsync);
        endpoints.MapPost("/api/celar-ai/v1/production/training-jobs",
            (Func<CelarAiTrainingRequest, HttpContext, PulseAiSystemIntelligenceService, IHttpClientFactory, CancellationToken, Task<IResult>>)CreateTrainingAsync);
        endpoints.MapGet("/api/celar-ai/v1/production/evaluations",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)ListEvaluationsAsync);
        endpoints.MapPost("/api/celar-ai/v1/production/evaluations",
            (Func<CelarAiEvaluationRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)CreateEvaluationAsync);
        endpoints.MapGet("/api/celar-ai/v1/production/models",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)ListModelsAsync);
        endpoints.MapPost("/api/celar-ai/v1/production/models",
            (Func<CelarAiModelRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)CreateModelAsync);
        endpoints.MapGet("/api/celar-ai/v1/production/deployments",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)ListDeploymentsAsync);
        endpoints.MapPost("/api/celar-ai/v1/production/deployments",
            (Func<CelarAiDeploymentRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)CreateDeploymentAsync);
        endpoints.MapPost(ChatRoute,
            (Func<CelarAiProductionChatRequest, HttpContext, PulseAiSystemIntelligenceService, PulseAiSystemIntelligenceRepository, CelarAiPeopleAndGuidanceService, CancellationToken, Task<IResult>>)ChatAsync);
        endpoints.MapPost(FlowHiveRoute,
            (Func<CelarAiFlowHiveProductionRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiEnterprisePlatformService, CancellationToken, Task<IResult>>)GenerateFlowHiveAsync);
        return endpoints;
    }

    private static async Task<IResult> ReadinessAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        PulseAiPrivateRagService rag,
        CelarAiEnterprisePlatformService enterprise,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var actualAccess = identity.Value.Actual == identity.Value.Effective
            ? access
            : await system.LoadAccessAsync(identity.Value.Actual, cancellationToken);
        var schemaReady = await IsSchemaReadyAsync(cancellationToken);
        var counts = schemaReady ? await CountsAsync(cancellationToken) : EmptyCounts();
        var cases = CompetencyCases.Select(test =>
        {
            var actual = ResolveIntent(test.Question);
            return new { test.Code, test.Question, expectedIntent = test.Intent, actualIntent = actual.Code, passed = actual.Code == test.Intent, expectedTrustClass = test.Trust };
        }).ToArray();
        return Results.Ok(new
        {
            module = "011",
            status = "celar_ai_production_platform_readiness_loaded",
            contractVersion = ContractVersion,
            brand = CelarAiBrandProfile.ToPublicProfile(),
            lifecycle = new
            {
                status = schemaReady ? "celar_ai_production_schema_ready" : DatabaseConfigured ? "celar_ai_production_schema_initialization_required" : "celar_ai_production_database_configuration_missing",
                schemaVersion = SchemaVersion,
                databaseConfigured = DatabaseConfigured,
                schemaReady,
                counts,
                rawTrainingExamplesStoredInPulse = false,
                modelBinariesStoredInPostgreSql = false
            },
            privateRag = await rag.GetReadinessAsync(cancellationToken),
            enterprisePlatform = await enterprise.GetReadinessAsync(cancellationToken),
            privateTraining = TrainingReadiness(),
            capabilityRouting = new
            {
                defaultTargets = CelarAiCapabilityTargets.DefaultOrder,
                finalFallbackRequired = CelarAiCapabilityTargets.Local,
                module064Authority = true,
                publicProviderReceivesRawPrivateDocuments = false
            },
            competency = new
            {
                total = cases.Length,
                passed = cases.Count(test => test.passed),
                requiredPassRate = 1m,
                currentPassRate = cases.Length == 0 ? 0m : (decimal)cases.Count(test => test.passed) / cases.Length,
                cases
            },
            access = AccessEvidence(identity.Value, access, CanManage(identity.Value, actualAccess)),
            guarantees = new[]
            {
                "Utility questions are answered directly before API, RAG, or provider selection.",
                "A current factual answer requires successful current authoritative evidence.",
                "API counts are not a substitute for an unrelated answer.",
                "Private project documents remain inside the approved Celar AI boundary.",
                "Fine-tuning uses immutable private artifact references and checksums.",
                "FlowHive output remains a PM and Engineering review draft."
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> ChatAsync(
        CelarAiProductionChatRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        PulseAiSystemIntelligenceRepository repository,
        CelarAiPeopleAndGuidanceService peopleAndGuidance,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var question = Limit(request.Question, system.Options().MaximumQuestionCharacters, string.Empty);
        if (question.Length == 0) return Validation("Enter a question for Celar AI.");
        var intent = ResolveIntent(question);
        PulseAiSystemQuestionResult result;

        if (intent.Code == "current_date_time")
        {
            result = await DirectResultAsync(request, identity.Value, access, repository, context, intent, DateTimeAnswer(request.ClientTimeZone), "celar_ai_deterministic_clock", cancellationToken);
        }
        else if (intent.Code == "system_version")
        {
            result = await DirectResultAsync(request, identity.Value, access, repository, context, intent, VersionAnswer(), "celar_ai_deterministic_release", cancellationToken);
        }
        else if (intent.Code == "capabilities")
        {
            result = await DirectResultAsync(request, identity.Value, access, repository, context, intent, CapabilityAnswer(), "celar_ai_governed_capability_catalog", cancellationToken);
        }
        else if (intent.Code == "identity")
        {
            result = await DirectResultAsync(request, identity.Value, access, repository, context, intent, CelarAiBrandProfile.CreateDetailedAnswer(DateTimeOffset.UtcNow), "celar_ai_canonical_knowledge", cancellationToken);
        }
        else if (intent.Code is "procedure" or "people_activity")
        {
            var specialized = await peopleAndGuidance.TryAnswerAsync(
                identity.Value.Actual,
                identity.Value.Effective,
                access,
                ToSystemRequest(request, intent, question),
                context,
                system.Options(),
                cancellationToken);
            result = specialized ?? await system.AskAsync(identity.Value.Actual, identity.Value.Effective, ToSystemRequest(request, intent, question), context, cancellationToken);
        }
        else
        {
            result = await system.AskAsync(identity.Value.Actual, identity.Value.Effective, ToSystemRequest(request, intent, question), context, cancellationToken);
        }

        result = EnforceAnswer(result, intent, question);
        var trust = Trust(result, intent);
        if (identity.Value.Actual == identity.Value.Effective)
        {
            try { await SaveQualityAsync(identity.Value.Actual, Sha256(question), intent.Code, trust, result.CorrelationId, cancellationToken); }
            catch { }
        }
        return Results.Ok(new
        {
            module = "011",
            brand = CelarAiBrandProfile.BrandName,
            feature = "celar_ai_production_answer_orchestration",
            decision = intent,
            trust,
            result = result.ToPublicResponse(),
            contextPolicy = CelarAiPeopleAndGuidanceService.ContextPolicy(),
            access = AccessEvidence(identity.Value, access, false),
            externalProviderCalledForPrivateContext = false,
            stateChanged = result.Persisted
        });
    }

    private static async Task<IResult> GenerateFlowHiveAsync(
        CelarAiFlowHiveProductionRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiEnterprisePlatformService enterprise,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        if (request.Plan is null) return Validation("A FlowHive plan is required.");
        if (string.IsNullOrWhiteSpace(request.Plan.ProjectCode) && string.IsNullOrWhiteSpace(request.Plan.ProjectName))
            return Validation("Select an authorized project before generating a FlowHive draft.");

        var outcome = BuildPlanningOutcome(request);
        var composition = await enterprise.ComposeAsync(
            identity.Value.Actual,
            identity.Value.Effective,
            new CelarAiComposeRequest(
                Mode: "project_plan",
                ProjectCode: request.Plan.ProjectCode,
                ProjectName: request.Plan.ProjectName,
                StartDate: request.Plan.ProjectStartDate,
                RequestedOutcome: outcome,
                DetailLevel: request.DetailLevel ?? "comprehensive",
                DiagramType: request.DiagramType ?? "flowchart",
                AllowSanitizedExternalFallback: request.AllowSanitizedExternalFallback),
            context,
            cancellationToken);

        var privatePlanAvailable = composition.FlowHivePlan?.Tasks.Count > 0;
        var generated = BuildPlan(request.Plan, composition.FlowHivePlan);
        var validation = ProjectFlowHiveScheduleEngine.Validate(generated);
        var schedule = ProjectFlowHiveScheduleEngine.Calculate(generated);
        var warnings = new List<string>(composition.Warnings)
        {
            "This is a PM and Engineering review draft, not a baseline, assignment, approval, capacity reservation, or customer date commitment.",
            "Schedule dates are deterministic weekday previews until approved Module 057 calendars, holidays, capacity, and customer constraints are applied."
        };
        if (!privatePlanAvailable) warnings.Add("Private model planning was unavailable or evidence-limited; the authorized current draft and governed deterministic structure were retained.");
        if (!validation.Valid) warnings.Add("Validation issues must be corrected before baseline review.");

        return Results.Ok(new
        {
            module = "066",
            feature = CelarAiCapabilityCatalog.ProjectFlowHivePlan,
            status = schedule.Valid ? "celar_ai_flowhive_review_draft_completed" : "celar_ai_flowhive_review_draft_requires_correction",
            executionEnabled = true,
            executionPath = privatePlanAvailable ? composition.PrimaryExecutionPath : "deterministic_flowhive_plan_from_authorized_current_draft",
            providerOrder = CelarAiCapabilityTargets.DefaultOrder,
            project = new { request.Plan.ProjectId, request.Plan.ProjectCode, request.Plan.ProjectName, request.Plan.CustomerName },
            plan = generated,
            validation,
            schedule,
            detailedAnswer = composition.DetailedAnswer,
            privatePlan = composition.FlowHivePlan,
            timeline = composition.Timeline,
            diagram = composition.Diagram,
            citations = composition.Citations,
            missingEvidence = composition.MissingEvidence,
            conflicts = composition.Conflicts,
            warnings,
            confidence = privatePlanAvailable ? composition.Confidence : Math.Min(.55m, Math.Max(.35m, composition.Confidence)),
            confidenceExplanation = privatePlanAvailable ? composition.ConfidenceExplanation : "Confidence is limited because deterministic fallback cannot replace private-model interpretation of approved documents.",
            externalAssistance = composition.ExternalAssistance,
            dataAsOf = composition.DataAsOf,
            correlationId = composition.CorrelationId,
            reviewControls = new
            {
                pmReviewRequired = true,
                engineeringReviewRequired = true,
                baselineEstablished = false,
                resourcesAssigned = false,
                capacityReserved = false,
                customerDateCommitted = false,
                persistencePerformed = false,
                stateChanged = false
            }
        });
    }

    private static PulseAiSystemQuestionRequest ToSystemRequest(CelarAiProductionChatRequest request, Intent intent, string question) => new(
        request.ConversationId,
        question,
        intent.Code,
        request.DetailLevel,
        request.ProjectCode,
        request.ProjectName,
        request.ModuleCode,
        request.ApiSearch,
        intent.IncludeApis,
        intent.IncludeTroubleshooting,
        intent.IncludeEnhancement,
        intent.IncludeDocuments,
        request.UsePrivateModelWhenAvailable,
        intent.MaximumTools);

    private static Intent ResolveIntent(string question)
    {
        var value = Whitespace().Replace(question.Trim().ToLowerInvariant(), " ");
        if (DateTimeQuestion().IsMatch(value) && !value.Contains("timesheet") && !value.Contains("timeline")) return new("current_date_time", false, false, false, false, 0, true, "Current request clock and browser time zone.");
        if (value.Contains("system version") || value.Contains("application version") || value.Contains("release sha") || value.Contains("release commit") || value.Contains("what version") || value.Contains("what environment")) return new("system_version", false, false, false, false, 0, true, "Current assembly, environment, release, and revision metadata.");
        if (value is "what can you answer" or "what can you do" or "what can celar ai answer" or "what can celar ai do" || value.Contains("celar ai capabilities")) return new("capabilities", false, false, false, false, 0, false, "Governed Celar AI capability catalog.");
        if (CelarAiBrandProfile.IsIdentityQuestion(question)) return new("identity", false, false, false, false, 0, false, "Canonical Celar AI identity profile.");
        if (value.StartsWith("how do i ") || value.StartsWith("how can i ") || value.StartsWith("how to ") || value.StartsWith("where do i ") || value.Contains("steps to ")) return new("procedure", false, false, false, false, 1, false, "Source-controlled Pulse procedure catalog.");
        if (CelarAiPeopleAndGuidanceService.IsPeopleActivityQuestion(question)) return new("people_activity", false, false, false, false, 6, true, "Current authorized people, assignment, workload, approval, capacity, and planning evidence.");
        if ((value.Contains("api") || value.Contains("endpoint"))
            && (value.Contains("running")
                || value.Contains("registered")
                || value.Contains("list")
                || value.Contains("show")
                || value.Contains("inventory")
                || value.Contains("count")
                || value.Contains("how many")
                || value.Contains("do i have")))
        {
            return new("api_inventory", true, false, false, false, 3, true, "Current ASP.NET endpoint registry.");
        }
        if (value.Contains("troubleshoot") || value.Contains("error") || value.Contains("failed") || value.Contains("not working") || value.Contains("why did") || value.Contains("why is") || HttpStatus().IsMatch(value) || value.Contains("correlation id")) return new("troubleshooting", true, true, false, false, 12, true, "Current API, diagnostic, release, dependency, and observability evidence.");
        if (FinancialQuestion().IsMatch(value)) return new("financial_and_reporting", false, false, false, false, 10, true, "Governed financial and reporting tools.");
        if (value.Contains("flowhive") || value.Contains("project plan") || value.Contains("project schedule") || value.Contains("project timeline") || value.Contains("wbs") || value.Contains("sow draft") || value.Contains("gsd planning")) return new("projects_and_delivery", false, false, false, true, 8, true, "Private project evidence and deterministic planning.");
        if (value.Contains("sow") || value.Contains("gsd") || value.Contains("iqs") || value.Contains("project document") || value.Contains("design document")) return new("documents_and_rag", false, false, false, true, 6, true, "Authorized private project-document evidence and citations.");
        if (value.Contains("future enhancement") || value.Contains("new feature") || value.Contains("could we add") || value.Contains("can we add") || value.Contains("design an enhancement")) return new("future_enhancement", true, false, true, false, 8, true, "Current-state-aware enhancement blueprint.");
        return new("general_system", false, false, false, false, 4, false, "General system answer without irrelevant broad API discovery.");
    }

    private static PulseAiSystemQuestionResult EnforceAnswer(PulseAiSystemQuestionResult result, Intent intent, string question)
    {
        if (intent.Code == "api_inventory" && result.RelevantApis.Count > 0)
        {
            var apiCount = result.RelevantApis.Count;
            var moduleCount = result.RelevantApis
                .Select(api => api.ModuleCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var getCount = result.RelevantApis.Count(api => HttpMethods.IsGet(api.Method));
            var writeCount = result.RelevantApis.Count(api =>
                HttpMethods.IsPost(api.Method)
                || HttpMethods.IsPut(api.Method)
                || HttpMethods.IsPatch(api.Method)
                || HttpMethods.IsDelete(api.Method));
            var safeRetestCount = result.RelevantApis.Count(api => api.SafeRetestSupported);
            var source = result.Sources
                .FirstOrDefault(item => item.SourceCode == "live_endpoint_registry")
                ?? result.Sources.FirstOrDefault();
            var dataAsOf = source?.ObservedAt ?? DateTimeOffset.UtcNow;
            var apiInventoryAnswer = result.Answer with
            {
                DirectConclusion = $"The running application currently registers {apiCount} API route/method combination{(apiCount == 1 ? string.Empty : "s")} across {moduleCount} module owner{(moduleCount == 1 ? string.Empty : "s")}.",
                ExecutiveSummary = "The count comes from the live ASP.NET endpoint registry for the current application revision. Registration confirms that a route is present; it does not by itself prove every downstream dependency is healthy.",
                ScopeAndFilters =
                [
                    "Environment: current authenticated application revision.",
                    "Source: live ASP.NET EndpointDataSource.",
                    "Scope: routes authorized for API-inventory evidence in the current effective-user session."
                ],
                CurrentState =
                [
                    $"Registered route/method combinations: {apiCount}.",
                    $"Module owners represented: {moduleCount}.",
                    $"Method summary: GET {getCount}; write methods {writeCount}; other methods {Math.Max(0, apiCount - getCount - writeCount)}.",
                    $"Explicitly safe read-only retest candidates: {safeRetestCount}."
                ],
                DetailedAnalysis = [],
                TroubleshootingFindings = [],
                RootCauseHypotheses = [],
                DiagnosticSteps = [],
                KnownUnknownAndStaleValues =
                [
                    "Known: these route/method combinations are registered in the running application revision.",
                    "Not implied: route registration alone does not verify database, identity, connector, or downstream service health."
                ],
                Assumptions = [],
                Conflicts = [],
                RisksAndImplications = [],
                RecommendedActions =
                [
                    "Open the collapsed API inventory below to search by module, method, route, purpose, registration status, or safe-retest eligibility."
                ],
                Confidence = .98m,
                ConfidenceExplanation = "High confidence because the count is generated from the current runtime endpoint registry.",
                DataAsOf = dataAsOf
            };
            return result with { Status = "completed", Answer = apiInventoryAnswer };
        }

        var direct = result.Answer.DirectConclusion?.Trim() ?? string.Empty;
        var boilerplate = direct.Contains("answered the question using 0 successful governed", StringComparison.OrdinalIgnoreCase)
            || direct.Contains("registered API route/method combinations, and approved operating knowledge", StringComparison.OrdinalIgnoreCase);
        var successful = result.ToolResults.Count(tool => tool.Succeeded)
            + result.Sources.Count(source => source.StatusCode is >= 200 and < 300)
            + (intent.Code == "api_inventory" && result.RelevantApis.Count > 0 ? 1 : 0);
        if (direct.Length > 0 && !boilerplate && (!intent.RequiresCurrentEvidence || successful > 0)) return result;
        var answer = result.Answer with
        {
            DirectConclusion = intent.RequiresCurrentEvidence
                ? "Celar AI did not receive enough successful current evidence to verify this request."
                : "Celar AI could not produce a direct supported answer from the selected governed capability.",
            ExecutiveSummary = $"The question was: {question}. Celar AI did not substitute API counts or generic diagnostic boilerplate for an answer.",
            CurrentState = [$"Intent: {intent.Code}.", $"Successful authoritative observations: {successful}.", $"Failed or unavailable tool results: {result.ToolResults.Count(tool => !tool.Succeeded)}."],
            KnownUnknownAndStaleValues = ["The requested value remains unknown because the required authoritative source did not succeed.", "No value was inferred from prior conversations or unverified assumptions."],
            RecommendedActions = ["Review the source diagnostics.", "Provide a narrower project, person/team, module, or date scope when applicable.", "Correct or restore the owning Pulse source."],
            Confidence = Math.Min(result.Answer.Confidence, .35m),
            ConfidenceExplanation = "Current facts require successful authoritative evidence.",
            DataAsOf = DateTimeOffset.UtcNow
        };
        return result with { Status = "partial", Answer = answer };
    }

    private static object Trust(PulseAiSystemQuestionResult result, Intent intent)
    {
        var successful = result.ToolResults.Count(tool => tool.Succeeded)
            + result.Sources.Count(source => source.StatusCode is >= 200 and < 300)
            + (intent.Code == "api_inventory" && result.RelevantApis.Count > 0 ? 1 : 0);
        var failed = result.ToolResults.Count(tool => !tool.Succeeded)
            + result.Sources.Count(source => source.StatusCode is < 200 or >= 300);
        var citations = result.Answer.CitationIds.Count;
        var answered = !result.Answer.DirectConclusion.Contains("did not receive enough", StringComparison.OrdinalIgnoreCase)
            && !result.Answer.DirectConclusion.Contains("could not produce", StringComparison.OrdinalIgnoreCase);
        var classification = intent.Code switch
        {
            "current_date_time" or "system_version" or "api_inventory" => "verified_current_fact",
            "capabilities" or "identity" => "platform_capability",
            "procedure" => "procedure",
            "future_enhancement" => "draft",
            "projects_and_delivery" => citations > 0 ? "verified_document_draft" : "draft",
            "documents_and_rag" => citations > 0 ? "verified_document_fact" : "insufficient_evidence",
            _ when !answered || intent.RequiresCurrentEvidence && successful == 0 => "insufficient_evidence",
            _ when result.Answer.Confidence >= .8m => "verified_current_fact",
            _ when result.Answer.Confidence >= .55m => "verified_with_limitations",
            _ => "insufficient_evidence"
        };
        var label = classification switch
        {
            "verified_current_fact" => "Verified current fact",
            "verified_document_fact" => "Verified document fact",
            "verified_document_draft" => "Document-grounded draft",
            "verified_with_limitations" => "Verified with limitations",
            "platform_capability" => "Platform capability",
            "procedure" => "Procedure",
            "draft" => "Reviewable draft",
            _ => "Insufficient evidence"
        };
        return new
        {
            classification,
            label,
            questionAnswered = answered,
            currentEvidenceRequired = intent.RequiresCurrentEvidence,
            successfulSourceCount = successful,
            failedSourceCount = failed,
            citationCount = citations,
            confidence = result.Answer.Confidence,
            humanReviewRequired = classification.Contains("draft") || classification == "insufficient_evidence",
            reasons = new[] { intent.Reason, $"Successful current/source observations: {successful}.", $"Failed, unavailable, or unauthorized observations: {failed}.", $"Citation identifiers: {citations}." },
            dataAsOf = result.Answer.DataAsOf
        };
    }

    private static async Task<PulseAiSystemQuestionResult> DirectResultAsync(
        CelarAiProductionChatRequest request,
        (Guid Actual, Guid Effective) identity,
        PulseAiSystemAccess access,
        PulseAiSystemIntelligenceRepository repository,
        HttpContext context,
        Intent intent,
        PulseAiSystemDetailedAnswer answer,
        string provider,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId(context);
        var detail = DetailLevel(request.DetailLevel);
        var mayPersist = identity.Actual == identity.Effective && access.CanViewConversations;
        var conversation = mayPersist ? await repository.EnsureConversationAsync(request.ConversationId, identity.Actual, identity.Effective, intent.Code, cancellationToken) : null;
        var persisted = conversation is not null;
        var conversationId = conversation?.ConversationId ?? request.ConversationId ?? Guid.NewGuid();
        var user = persisted
            ? await repository.AppendMessageAsync(conversationId, identity.Effective, "user", "completed", request.Question ?? string.Empty, new { intent = intent.Code, request.ClientTimeZone }, null, null, correlationId, string.Empty, string.Empty, [], new { source = provider }, answer.DataAsOf, cancellationToken)
            : (MessageId: Guid.NewGuid(), SequenceNumber: 1);
        var run = persisted ? await repository.CreateInquiryRunAsync(conversationId, user.MessageId, identity.Actual, identity.Effective, intent.Code, detail, Sha256(request.Question ?? string.Empty), correlationId, cancellationToken) : Guid.NewGuid();
        var source = new PulseAiSystemSourceEvidence(1, "deterministic_runtime_or_governed_catalog", intent.Code, provider, "011", "INTERNAL", "current-runtime", "succeeded", 200, answer.DataAsOf, "current_request", "No private customer or project content required");
        var provisional = new PulseAiSystemQuestionResult(conversationId, user.MessageId, Guid.Empty, run, "completed", intent.Code, detail, answer, [source], [], [], provider, provider, correlationId, [], persisted);
        var assistant = persisted
            ? await repository.AppendMessageAsync(conversationId, identity.Effective, "assistant", "completed", answer.DirectConclusion, provisional.ToPublicResponse(), run, null, correlationId, provider, provider, [], new { directCapability = intent.Code, previousConversationMessagesInjected = false }, answer.DataAsOf, cancellationToken)
            : (MessageId: Guid.NewGuid(), SequenceNumber: 2);
        if (persisted) await repository.CompleteInquiryRunAsync(run, assistant.MessageId, "completed", [], [], 0, answer.Confidence, string.Empty, cancellationToken);
        return provisional with { AssistantMessageId = assistant.MessageId, Persisted = persisted && assistant.MessageId != Guid.Empty };
    }

    private static PulseAiSystemDetailedAnswer DateTimeAnswer(string? requestedZone)
    {
        var now = DateTimeOffset.UtcNow;
        var zone = ResolveTimeZone(requestedZone, out var warning);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var zoneLabel = string.IsNullOrWhiteSpace(requestedZone) ? zone.Id : requestedZone.Trim();
        return Answer(
            $"Today is {local:dddd, MMMM d, yyyy}. The current time is {local:h:mm tt} in {zoneLabel}.",
            "The current API request clock was converted using the caller's browser time zone. No model, RAG index, API inventory, or external provider was needed.",
            [$"Date: {local:dddd, MMMM d, yyyy}.", $"Time: {local:h:mm:ss tt}.", $"Time zone: {zoneLabel}.", $"UTC: {now:yyyy-MM-dd HH:mm:ss 'UTC'}."],
            warning.Length == 0 ? [] : [warning],
            .99m,
            "Deterministic current-request time conversion.",
            now);
    }

    private static PulseAiSystemDetailedAnswer VersionAnswer()
    {
        var now = DateTimeOffset.UtcNow;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "not_recorded";
        var environment = First(Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT"), Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "unspecified");
        var release = First(Environment.GetEnvironmentVariable("PROJECTPULSE_RELEASE_COMMIT"), Environment.GetEnvironmentVariable("PROJECTPULSE_RELEASE_SHA"), Environment.GetEnvironmentVariable("GITHUB_SHA"), Environment.GetEnvironmentVariable("WEBSITE_COMMIT_ID"), Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION"), "not_recorded");
        var revision = First(Environment.GetEnvironmentVariable("CONTAINER_APP_REVISION"), "not_recorded");
        return Answer(
            $"Pulse API version {version} is running in the {environment} environment at release {release}.",
            "This answer uses current process and deployment metadata rather than documentation or conversation history.",
            [$"Assembly version: {version}.", $"Release SHA: {release}.", $"Environment: {environment}.", $"Container revision: {revision}."],
            release == "not_recorded" ? ["Populate release SHA metadata during deployment."] : [],
            release == "not_recorded" ? .78m : .98m,
            "Confidence reflects direct runtime metadata and is lower when release SHA is absent.",
            now);
    }

    private static PulseAiSystemDetailedAnswer CapabilityAnswer()
    {
        var now = DateTimeOffset.UtcNow;
        return new PulseAiSystemDetailedAnswer(
            "Celar AI can answer platform procedures, authorized current-system questions, private project-document questions, API and troubleshooting questions, reporting and financial questions, and can produce reviewable Timesheet, SOW, FlowHive plan, schedule, timeline, diagram, and enhancement drafts.",
            "Celar AI is a private, tool-augmented, RAG-enabled operational-intelligence platform. Owning Pulse modules remain authoritative and consequential actions remain human controlled.",
            ["Permission-aware current effective-user scope."],
            ["Direct product guidance", "Authorized people and work intelligence", "Private SOW/GSD/IQS retrieval", "Timesheet descriptions", "SOW drafts", "FlowHive plans and schedules", "Reports and financial explanations", "API discovery and troubleshooting", "Fine-tuning and model lifecycle governance"],
            ["Stable guidance is answered directly.", "Current facts use governed live tools.", "Private document facts use citations.", "Calculations and schedules use deterministic engines.", "Models provide explanation and drafting, not authorization."],
            [], [], [], [], ["Celar AI governed capability catalog."],
            ["Capabilities can be unavailable when their owning module, private model, document index, or required permission is not ready."],
            [], [],
            ["Celar AI cannot bypass authorization or autonomously submit time, publish SOWs, baseline plans, assign resources, change financials, grant permissions, or deploy software."],
            [], ["Ask a specific question and include project, person/team, module, or date scope when useful."],
            null, ["#celar-ai", "#user-guide", "#ai-provider-configuration", "#project-flowhive"], [1], .98m,
            "Governed source-controlled capability contract.", now);
    }

    private static PulseAiSystemDetailedAnswer Answer(string conclusion, string summary, IReadOnlyList<string> state, IReadOnlyList<string> actions, decimal confidence, string explanation, DateTimeOffset dataAsOf) => new(
        conclusion, summary, [], state, [], [], [], [], [], ["Current deterministic Celar AI production source."], [], [], [], [], [], actions, null, [], [1], confidence, explanation, dataAsOf);

    private static ProjectFlowHivePlanRequest BuildPlan(ProjectFlowHivePlanRequest source, PulseAiPrivateFlowHivePlan? privatePlan)
    {
        if (privatePlan?.Tasks.Count > 0)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tasks = privatePlan.Tasks.Take(500).Select((task, index) =>
            {
                var candidate = Regex.IsMatch(task.Wbs ?? string.Empty, @"^\d+(?:\.\d+)*$") ? task.Wbs : (index + 1).ToString();
                while (!used.Add(candidate)) candidate = $"{index + 1}.{used.Count + 1}";
                map[task.Wbs ?? string.Empty] = candidate;
                var days = Math.Clamp((int)Math.Ceiling(Math.Max(.25m, task.EstimatedDurationDays)), 1, 730);
                return new ProjectFlowHivePlanTaskInput(Guid.NewGuid(), null, candidate, ParentWbs(candidate), Limit(task.Name, 300, $"Planning task {index + 1}"), Limit(task.Description, 4000, "Review cited planning evidence."), days, false, "ASAP", null, 0m, days * 8m, "not_started");
            }).ToArray();
            var dependencies = new List<ProjectFlowHiveDependencyInput>();
            foreach (var task in privatePlan.Tasks.Take(tasks.Length))
            {
                if (!map.TryGetValue(task.Wbs ?? string.Empty, out var successor)) continue;
                foreach (var predecessor in task.Predecessors)
                    if (map.TryGetValue(predecessor ?? string.Empty, out var pred) && pred != successor)
                        dependencies.Add(new(pred, successor, "FS", 0));
            }
            if (dependencies.Count == 0)
                dependencies.AddRange(tasks.Skip(1).Select((task, index) => new ProjectFlowHiveDependencyInput(tasks[index].WbsNumber, task.WbsNumber, "FS", 0)));
            return source with
            {
                PlanName = Limit(source.PlanName, 240, $"{source.ProjectCode} Celar AI governed plan"),
                RevisionLabel = $"Celar AI review draft {DateTimeOffset.UtcNow:yyyyMMdd-HHmm}",
                ProjectStartDate = source.ProjectStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                Tasks = tasks,
                Dependencies = dependencies.GroupBy(item => $"{item.PredecessorWbs}|{item.SuccessorWbs}|{item.Type}|{item.LagWorkingDays}").Select(group => group.First()).Take(4000).ToArray(),
                Assignments = [],
                Notes = Limit($"Objective: {privatePlan.Objective}\nAssumptions: {string.Join(" | ", privatePlan.Assumptions)}\nRisks: {string.Join(" | ", privatePlan.Risks)}\nOpen questions: {string.Join(" | ", privatePlan.OpenQuestions)}", 12000, string.Empty)
            };
        }
        var current = source.Tasks?.Where(task => !string.IsNullOrWhiteSpace(task.WbsNumber)).Take(500).ToArray() ?? [];
        if (current.Length > 0)
            return source with { PlanName = Limit(source.PlanName, 240, $"{source.ProjectCode} Celar AI governed plan"), RevisionLabel = $"Celar AI deterministic review {DateTimeOffset.UtcNow:yyyyMMdd-HHmm}", ProjectStartDate = source.ProjectStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow), Tasks = current, Dependencies = source.Dependencies ?? [], Assignments = source.Assignments ?? [], Notes = Limit(source.Notes, 12000, "Current authorized draft retained because private planning evidence was unavailable.") };
        var phases = new[]
        {
            ("1","Discovery and prerequisites","Confirm scope, stakeholders, access, prerequisites, constraints, and open questions.",2),
            ("2","Design validation","Validate architecture, GSD, SOW, assumptions, responsibilities, dependencies, and acceptance.",3),
            ("3","Implementation preparation","Prepare work instructions, change controls, resources, communications, and rollback criteria.",2),
            ("4","Implementation","Execute reviewed implementation activities in controlled stages.",5),
            ("5","Testing and remediation","Validate outcomes, document defects, remediate issues, and collect evidence.",3),
            ("6","Customer acceptance","Review deliverables and objective acceptance evidence.",2),
            ("7","Operational handoff","Complete knowledge transfer, documentation, support transition, and ownership handoff.",2),
            ("8","Closeout","Confirm completion, unresolved items, financial readiness, acceptance, and communication.",1)
        };
        var generated = phases.Select(item => new ProjectFlowHivePlanTaskInput(Guid.NewGuid(), null, item.Item1, string.Empty, item.Item2, item.Item3, item.Item4, false, "ASAP", null, 0m, item.Item4 * 8m, "not_started")).ToArray();
        return source with { PlanName = $"{Limit(source.ProjectCode, 120, "Project")} Celar AI governed plan", RevisionLabel = $"Celar AI deterministic review {DateTimeOffset.UtcNow:yyyyMMdd-HHmm}", ProjectStartDate = source.ProjectStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow), Tasks = generated, Dependencies = generated.Skip(1).Select((task, index) => new ProjectFlowHiveDependencyInput(generated[index].WbsNumber, task.WbsNumber, "FS", 0)).ToArray(), Assignments = [], Notes = "Generic governed phases are assumptions until private evidence and PM/Engineering review are complete." };
    }

    private static string BuildPlanningOutcome(CelarAiFlowHiveProductionRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Limit(request.RequestedOutcome, 4000, "Create a detailed implementation plan with dependencies, risks, assumptions, milestones, acceptance, handoff, and closeout."));
        if (!string.IsNullOrWhiteSpace(request.GsdExcerpt)) { builder.AppendLine("Approved private GSD excerpt:"); builder.AppendLine(Limit(request.GsdExcerpt, 2000, string.Empty)); }
        if (!string.IsNullOrWhiteSpace(request.SowExcerpt)) { builder.AppendLine("Approved private SOW excerpt:"); builder.AppendLine(Limit(request.SowExcerpt, 2000, string.Empty)); }
        builder.AppendLine("Separate verified facts from assumptions, preserve conflicts, and do not create customer commitments.");
        return Limit(builder.ToString(), 8000, string.Empty);
    }

    private static async Task<IResult> InitializeSchemaAsync(HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ManageAsync(context, system, cancellationToken);
        if (authorization.Error is not null) return authorization.Error;
        if (CandidateMutationBlocked() is { } blocked) return blocked;
        if (!DatabaseConfigured) return Results.Json(new { status = "database_configuration_missing", stateChanged = false }, statusCode: 503);
        await using var connection = new NpgsqlConnection(ConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(SchemaSql, connection, transaction)) await command.ExecuteNonQueryAsync(cancellationToken);
        await AuditAsync(connection, transaction, authorization.Identity!.Value.Actual, "schema_initialized", "platform", SchemaVersion, new { }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "celar_ai_production_schema_initialized", schemaVersion = SchemaVersion, ready = await IsSchemaReadyAsync(cancellationToken), stateChanged = true });
    }

    private static async Task<IResult> ListDatasetsAsync(HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ReadAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        return Results.Ok(new { datasets = await QueryAsync("SELECT dataset_version_id AS \"datasetVersionId\",name,purpose,classification,artifact_uri AS \"artifactUri\",sha256,example_count AS \"exampleCount\",state,created_by AS \"createdBy\",created_at AS \"createdAt\",updated_at AS \"updatedAt\" FROM celar_ai_dataset_versions ORDER BY created_at DESC LIMIT 500", cancellationToken), stateChanged = false });
    }

    private static async Task<IResult> CreateDatasetAsync(CelarAiDatasetRequest request, HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ManageAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        if (CandidateMutationBlocked() is { } blocked) return blocked;
        try
        {
            await RequireSchemaAsync(cancellationToken);
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var name = Required(request.Name, 240, "Dataset name is required."); var purpose = Required(request.Purpose, 2000, "Dataset purpose is required.");
            var artifact = RequiredUri(request.ArtifactUri); var sha = RequiredSha(request.Sha256); var state = DatasetState(request.State); var classification = Classification(request.Classification);
            await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken); await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            const string sql = "INSERT INTO celar_ai_dataset_versions(dataset_version_id,name,purpose,classification,artifact_uri,sha256,example_count,state,created_by,created_at,updated_at) VALUES(@id,@name,@purpose,@classification,@artifact,@sha,@count,@state,@actor,@now,@now)";
            await using (var command = new NpgsqlCommand(sql, connection, transaction)) { command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("name", name); command.Parameters.AddWithValue("purpose", purpose); command.Parameters.AddWithValue("classification", classification); command.Parameters.AddWithValue("artifact", artifact); command.Parameters.AddWithValue("sha", sha); command.Parameters.AddWithValue("count", Math.Clamp(request.ExampleCount, 0, 10_000_000)); command.Parameters.AddWithValue("state", state); command.Parameters.AddWithValue("actor", authorization.Identity!.Value.Actual); command.Parameters.AddWithValue("now", now); await command.ExecuteNonQueryAsync(cancellationToken); }
            await AuditAsync(connection, transaction, authorization.Identity!.Value.Actual, "dataset_created", "dataset", id.ToString(), new { name, classification, sha, state }, cancellationToken); await transaction.CommitAsync(cancellationToken);
            return Results.Json(new { dataset = new { datasetVersionId = id, name, purpose, classification, artifactUri = artifact, sha256 = sha, exampleCount = request.ExampleCount, state, createdAt = now, updatedAt = now }, stateChanged = true }, statusCode: 201);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return Validation(exception.Message); }
    }

    private static async Task<IResult> ListTrainingAsync(HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ReadAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        return Results.Ok(new { trainingJobs = await QueryAsync("SELECT training_job_id AS \"trainingJobId\",dataset_version_id AS \"datasetVersionId\",method,base_model AS \"baseModel\",status,external_job_id AS \"externalJobId\",configuration_json AS configuration,diagnostic_code AS \"diagnosticCode\",created_by AS \"createdBy\",created_at AS \"createdAt\",updated_at AS \"updatedAt\" FROM celar_ai_training_jobs ORDER BY created_at DESC LIMIT 500", cancellationToken), stateChanged = false });
    }

    private static async Task<IResult> CreateTrainingAsync(CelarAiTrainingRequest request, HttpContext context, PulseAiSystemIntelligenceService system, IHttpClientFactory clients, CancellationToken cancellationToken)
    {
        var authorization = await ManageAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        if (CandidateMutationBlocked() is { } blocked) return blocked;
        try
        {
            await RequireSchemaAsync(cancellationToken);
            var dataset = await DatasetAsync(request.DatasetVersionId, cancellationToken) ?? throw new ArgumentException("The selected immutable dataset does not exist.");
            if (dataset.State is not ("reviewed" or "approved")) throw new ArgumentException("The dataset must be reviewed or approved before training.");
            var method = TrainingMethod(request.Method); var baseModel = Required(request.BaseModel, 300, "A base model is required."); var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var configuration = request.Configuration ?? new { };
            var initial = TrainingEnabled ? "queued" : "configuration_required";
            await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken);
            const string insert = "INSERT INTO celar_ai_training_jobs(training_job_id,dataset_version_id,method,base_model,status,external_job_id,configuration_json,diagnostic_code,created_by,created_at,updated_at) VALUES(@id,@dataset,@method,@base,@status,'',@configuration::jsonb,@diagnostic,@actor,@now,@now)";
            await using (var command = new NpgsqlCommand(insert, connection)) { command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("dataset", request.DatasetVersionId); command.Parameters.AddWithValue("method", method); command.Parameters.AddWithValue("base", baseModel); command.Parameters.AddWithValue("status", initial); command.Parameters.Add("configuration", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(configuration); command.Parameters.AddWithValue("diagnostic", TrainingEnabled ? string.Empty : "private_training_endpoint_not_configured"); command.Parameters.AddWithValue("actor", authorization.Identity!.Value.Actual); command.Parameters.AddWithValue("now", now); await command.ExecuteNonQueryAsync(cancellationToken); }
            var submission = await SubmitTrainingAsync(clients, id, method, baseModel, dataset, configuration, cancellationToken);
            const string update = "UPDATE celar_ai_training_jobs SET status=@status,external_job_id=@external,diagnostic_code=@diagnostic,updated_at=@now WHERE training_job_id=@id";
            await using (var command = new NpgsqlCommand(update, connection)) { command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("status", submission.Status); command.Parameters.AddWithValue("external", submission.ExternalJobId); command.Parameters.AddWithValue("diagnostic", submission.Diagnostic); command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow); await command.ExecuteNonQueryAsync(cancellationToken); }
            return Results.Json(new { trainingJob = new { trainingJobId = id, datasetVersionId = request.DatasetVersionId, method, baseModel, status = submission.Status, externalJobId = submission.ExternalJobId, configuration, diagnosticCode = submission.Diagnostic, createdAt = now, updatedAt = DateTimeOffset.UtcNow }, submission, stateChanged = true, modelPromoted = false }, statusCode: 202);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return Validation(exception.Message); }
    }

    private static async Task<IResult> ListEvaluationsAsync(HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ReadAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        return Results.Ok(new { evaluations = await QueryAsync("SELECT evaluation_run_id AS \"evaluationRunId\",suite_code AS \"suiteCode\",model_version_id AS \"modelVersionId\",status,score,passed,results_json AS results,created_by AS \"createdBy\",created_at AS \"createdAt\",completed_at AS \"completedAt\" FROM celar_ai_evaluation_runs ORDER BY created_at DESC LIMIT 500", cancellationToken), stateChanged = false });
    }

    private static async Task<IResult> CreateEvaluationAsync(CelarAiEvaluationRequest request, HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ManageAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        if (CandidateMutationBlocked() is { } blocked) return blocked;
        try
        {
            await RequireSchemaAsync(cancellationToken); var suite = Limit(request.SuiteCode, 120, "basic_competency").ToLowerInvariant(); var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
            var cases = CompetencyCases.Select(test => { var actual = ResolveIntent(test.Question); return new { test.Code, test.Question, expectedIntent = test.Intent, actualIntent = actual.Code, passed = actual.Code == test.Intent, expectedTrustClass = test.Trust }; }).ToArray();
            var score = cases.Length == 0 ? 0m : decimal.Round((decimal)cases.Count(test => test.passed) / cases.Length, 4); var passed = score == 1m; var results = new { suite, total = cases.Length, passed = cases.Count(test => test.passed), failed = cases.Count(test => !test.passed), requiredScore = 1m, cases };
            await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken);
            const string sql = "INSERT INTO celar_ai_evaluation_runs(evaluation_run_id,suite_code,model_version_id,status,score,passed,results_json,created_by,created_at,completed_at) VALUES(@id,@suite,@model,@status,@score,@passed,@results::jsonb,@actor,@now,@now)";
            await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("suite", suite); command.Parameters.AddWithValue("model", request.ModelVersionId is null ? DBNull.Value : request.ModelVersionId.Value); command.Parameters.AddWithValue("status", passed ? "passed" : "failed"); command.Parameters.AddWithValue("score", score); command.Parameters.AddWithValue("passed", passed); command.Parameters.Add("results", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(results); command.Parameters.AddWithValue("actor", authorization.Identity!.Value.Actual); command.Parameters.AddWithValue("now", now); await command.ExecuteNonQueryAsync(cancellationToken);
            return Results.Ok(new { evaluation = new { evaluationRunId = id, suiteCode = suite, request.ModelVersionId, status = passed ? "passed" : "failed", score, passed, results, createdAt = now, completedAt = now }, promotionBlocked = !passed, requiredPassRate = 1m, stateChanged = true });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return Validation(exception.Message); }
    }

    private static async Task<IResult> ListModelsAsync(HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ReadAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        return Results.Ok(new { models = await QueryAsync("SELECT model_version_id AS \"modelVersionId\",name,semantic_version AS \"semanticVersion\",base_model AS \"baseModel\",artifact_uri AS \"artifactUri\",sha256,dataset_version_id AS \"datasetVersionId\",training_job_id AS \"trainingJobId\",evaluation_run_id AS \"evaluationRunId\",state,created_by AS \"createdBy\",created_at AS \"createdAt\",updated_at AS \"updatedAt\" FROM celar_ai_model_versions ORDER BY created_at DESC LIMIT 500", cancellationToken), stateChanged = false });
    }

    private static async Task<IResult> CreateModelAsync(CelarAiModelRequest request, HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ManageAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        if (CandidateMutationBlocked() is { } blocked) return blocked;
        try
        {
            await RequireSchemaAsync(cancellationToken); var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var name = Required(request.Name, 240, "Model name is required."); var version = Required(request.SemanticVersion, 80, "Semantic version is required."); var baseModel = Required(request.BaseModel, 300, "Base model is required."); var artifact = RequiredUri(request.ArtifactUri); var sha = RequiredSha(request.Sha256); var state = ModelState(request.State);
            await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken);
            const string sql = "INSERT INTO celar_ai_model_versions(model_version_id,name,semantic_version,base_model,artifact_uri,sha256,dataset_version_id,training_job_id,evaluation_run_id,state,created_by,created_at,updated_at) VALUES(@id,@name,@version,@base,@artifact,@sha,@dataset,@training,@evaluation,@state,@actor,@now,@now)";
            await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("name", name); command.Parameters.AddWithValue("version", version); command.Parameters.AddWithValue("base", baseModel); command.Parameters.AddWithValue("artifact", artifact); command.Parameters.AddWithValue("sha", sha); command.Parameters.AddWithValue("dataset", request.DatasetVersionId is null ? DBNull.Value : request.DatasetVersionId.Value); command.Parameters.AddWithValue("training", request.TrainingJobId is null ? DBNull.Value : request.TrainingJobId.Value); command.Parameters.AddWithValue("evaluation", request.EvaluationRunId is null ? DBNull.Value : request.EvaluationRunId.Value); command.Parameters.AddWithValue("state", state); command.Parameters.AddWithValue("actor", authorization.Identity!.Value.Actual); command.Parameters.AddWithValue("now", now); await command.ExecuteNonQueryAsync(cancellationToken);
            return Results.Json(new { model = new { modelVersionId = id, name, semanticVersion = version, baseModel, artifactUri = artifact, sha256 = sha, request.DatasetVersionId, request.TrainingJobId, request.EvaluationRunId, state, createdAt = now, updatedAt = now }, stateChanged = true }, statusCode: 201);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return Validation(exception.Message); }
    }

    private static async Task<IResult> ListDeploymentsAsync(HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ReadAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        return Results.Ok(new { deployments = await QueryAsync("SELECT deployment_id AS \"deploymentId\",model_version_id AS \"modelVersionId\",environment_code AS environment,capability_code AS \"capabilityCode\",status,endpoint_fingerprint AS \"endpointFingerprint\",rollback_model_version_id AS \"rollbackModelVersionId\",created_by AS \"createdBy\",created_at AS \"createdAt\",updated_at AS \"updatedAt\" FROM celar_ai_model_deployments ORDER BY created_at DESC LIMIT 500", cancellationToken), stateChanged = false });
    }

    private static async Task<IResult> CreateDeploymentAsync(CelarAiDeploymentRequest request, HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var authorization = await ManageAsync(context, system, cancellationToken); if (authorization.Error is not null) return authorization.Error;
        if (CandidateMutationBlocked() is { } blocked) return blocked;
        try
        {
            await RequireSchemaAsync(cancellationToken); var model = await ModelAsync(request.ModelVersionId, cancellationToken) ?? throw new ArgumentException("The selected model does not exist.");
            var environment = EnvironmentState(request.Environment); if (environment == "production" && model.State is not ("approved_production" or "production")) throw new ArgumentException("Production requires an approved_production model."); if (environment == "test" && model.State is "draft" or "rejected") throw new ArgumentException("Test requires an evaluated or approved model.");
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var capability = Limit(request.CapabilityCode, 160, CelarAiCapabilityCatalog.HelpAssistant); const string status = "planned_human_approval_required";
            await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken);
            const string sql = "INSERT INTO celar_ai_model_deployments(deployment_id,model_version_id,environment_code,capability_code,status,endpoint_fingerprint,rollback_model_version_id,created_by,created_at,updated_at) VALUES(@id,@model,@environment,@capability,@status,'',@rollback,@actor,@now,@now)";
            await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("model", request.ModelVersionId); command.Parameters.AddWithValue("environment", environment); command.Parameters.AddWithValue("capability", capability); command.Parameters.AddWithValue("status", status); command.Parameters.AddWithValue("rollback", request.RollbackModelVersionId is null ? DBNull.Value : request.RollbackModelVersionId.Value); command.Parameters.AddWithValue("actor", authorization.Identity!.Value.Actual); command.Parameters.AddWithValue("now", now); await command.ExecuteNonQueryAsync(cancellationToken);
            return Results.Json(new { deployment = new { deploymentId = id, request.ModelVersionId, environment, capabilityCode = capability, status, endpointFingerprint = string.Empty, request.RollbackModelVersionId, createdAt = now, updatedAt = now }, stateChanged = true, endpointActivated = false, module064RouteChanged = false, humanApprovalRequired = true }, statusCode: 201);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return Validation(exception.Message); }
    }

    private static async Task SaveQualityAsync(Guid actor, string questionSha, string intent, object trust, string correlation, CancellationToken cancellationToken)
    {
        if (ProjectPulseAiReleaseRuntimePolicy.RequireValid().Active) return;
        if (!await IsSchemaReadyAsync(cancellationToken)) return;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(trust)); var root = doc.RootElement;
        await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken);
        const string sql = "INSERT INTO celar_ai_answer_quality_events(answer_quality_event_id,actor_user_id,question_sha256,intent_code,trust_classification,question_answered,successful_source_count,failed_source_count,citation_count,confidence,correlation_id,reasons_json,created_at) VALUES(@id,@actor,@question,@intent,@trust,@answered,@success,@failed,@citations,@confidence,@correlation,@reasons::jsonb,@now)";
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("question", questionSha); command.Parameters.AddWithValue("intent", intent); command.Parameters.AddWithValue("trust", root.GetProperty("classification").GetString() ?? "unknown"); command.Parameters.AddWithValue("answered", root.GetProperty("questionAnswered").GetBoolean()); command.Parameters.AddWithValue("success", root.GetProperty("successfulSourceCount").GetInt32()); command.Parameters.AddWithValue("failed", root.GetProperty("failedSourceCount").GetInt32()); command.Parameters.AddWithValue("citations", root.GetProperty("citationCount").GetInt32()); command.Parameters.AddWithValue("confidence", root.GetProperty("confidence").GetDecimal()); command.Parameters.AddWithValue("correlation", correlation); command.Parameters.Add("reasons", NpgsqlDbType.Jsonb).Value = root.GetProperty("reasons").GetRawText(); command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<TrainingSubmission> SubmitTrainingAsync(IHttpClientFactory clients, Guid jobId, string method, string baseModel, DatasetRow dataset, object configuration, CancellationToken cancellationToken)
    {
        if (!TrainingEnabled) return new("configuration_required", string.Empty, "private_training_disabled", false);
        var endpointValue = Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_ENDPOINT")?.Trim() ?? string.Empty;
        if (!PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(endpointValue, TrainingAllowlist(), out var endpoint, out var reason) || endpoint is null) return new("configuration_required", string.Empty, reason, false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(new { contractVersion = ContractVersion, trainingJobId = jobId, method, baseModel, dataset = new { dataset.DatasetVersionId, dataset.Name, dataset.Purpose, dataset.Classification, dataset.ArtifactUri, dataset.Sha256, dataset.ExampleCount, dataset.State }, configuration, controls = new { immutableInput = true, rawExamplesIncludedInRequest = false, automaticProductionPromotion = false } }) };
            var token = Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_BEARER_TOKEN")?.Trim() ?? string.Empty; if (token.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await clients.CreateClient("PulseAiPrivateInference").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); if (!response.IsSuccessStatusCode) return new("submission_failed", string.Empty, $"private_training_http_{(int)response.StatusCode}", false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken); using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken); var root = json.RootElement;
            var external = ReadString(root, "jobId", "id", "trainingJobId", "externalJobId"); var status = ReadString(root, "status", "state"); return new(status.Length == 0 ? "submitted" : status, external.Length == 0 ? jobId.ToString("N") : external, string.Empty, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return new("submission_failed", string.Empty, exception is HttpRequestException ? "private_training_transport_failure" : "private_training_failure", false); }
    }

    private static object TrainingReadiness()
    {
        var endpointValue = Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_ENDPOINT")?.Trim() ?? string.Empty; var reason = endpointValue.Length == 0 ? "private_training_endpoint_not_configured" : "not_checked";
        var approved = endpointValue.Length > 0 && PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(endpointValue, TrainingAllowlist(), out _, out reason);
        return new { status = TrainingEnabled && approved ? "private_training_route_ready" : TrainingEnabled ? "private_training_endpoint_rejected_or_missing" : "private_training_disabled", enabled = TrainingEnabled, configured = endpointValue.Length > 0, endpointPrivate = approved, endpointPolicyReason = reason, bearerTokenConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_BEARER_TOKEN")), endpointReturned = false, bearerTokenReturned = false, immutableDatasetReferenceOnly = true, rawExamplesTransmittedByPulse = false };
    }

    private static bool TrainingEnabled => bool.TryParse(Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_ENABLED"), out var enabled) && enabled;
    private static string[] TrainingAllowlist() => (Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_TRAINING_HOST_ALLOWLIST") ?? string.Empty).Split([',',';','\n','\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => value.ToLowerInvariant()).Distinct().ToArray();

    private static async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, CancellationToken cancellationToken)
    {
        if (!await IsSchemaReadyAsync(cancellationToken)) return [];
        var rows = new List<Dictionary<string, object?>>(); await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand(sql, connection); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) { var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase); for (var index = 0; index < reader.FieldCount; index++) row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index); rows.Add(row); } return rows;
    }

    private static async Task<DatasetRow?> DatasetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken); const string sql = "SELECT dataset_version_id,name,purpose,classification,artifact_uri,sha256,example_count,state FROM celar_ai_dataset_versions WHERE dataset_version_id=@id"; await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7)) : null;
    }

    private static async Task<ModelRow?> ModelAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken); const string sql = "SELECT model_version_id,state FROM celar_ai_model_versions WHERE model_version_id=@id"; await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? new(reader.GetGuid(0), reader.GetString(1)) : null;
    }

    private static async Task RequireSchemaAsync(CancellationToken cancellationToken) { if (!await IsSchemaReadyAsync(cancellationToken)) throw new InvalidOperationException("Initialize the Celar AI production lifecycle schema from Governance first."); }
    private static async Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken)
    {
        if (!DatabaseConfigured) return false; try { await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken); const string sql = "SELECT to_regclass('public.celar_ai_dataset_versions') IS NOT NULL AND to_regclass('public.celar_ai_training_jobs') IS NOT NULL AND to_regclass('public.celar_ai_evaluation_runs') IS NOT NULL AND to_regclass('public.celar_ai_model_versions') IS NOT NULL AND to_regclass('public.celar_ai_model_deployments') IS NOT NULL AND to_regclass('public.celar_ai_answer_quality_events') IS NOT NULL AND to_regclass('public.celar_ai_lifecycle_audit') IS NOT NULL"; await using var command = new NpgsqlCommand(sql, connection); return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken) ?? false); } catch { return false; }
    }

    private static async Task<Dictionary<string,long>> CountsAsync(CancellationToken cancellationToken)
    {
        var counts = EmptyCounts(); await using var connection = new NpgsqlConnection(ConnectionString()); await connection.OpenAsync(cancellationToken); const string sql = "SELECT (SELECT COUNT(*) FROM celar_ai_dataset_versions),(SELECT COUNT(*) FROM celar_ai_training_jobs),(SELECT COUNT(*) FROM celar_ai_evaluation_runs),(SELECT COUNT(*) FROM celar_ai_model_versions),(SELECT COUNT(*) FROM celar_ai_model_deployments),(SELECT COUNT(*) FROM celar_ai_answer_quality_events)"; await using var command = new NpgsqlCommand(sql, connection); await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (await reader.ReadAsync(cancellationToken)) { counts["datasets"] = reader.GetInt64(0); counts["trainingJobs"] = reader.GetInt64(1); counts["evaluations"] = reader.GetInt64(2); counts["models"] = reader.GetInt64(3); counts["deployments"] = reader.GetInt64(4); counts["qualityEvents"] = reader.GetInt64(5); } return counts;
    }
    private static Dictionary<string,long> EmptyCounts() => new() { ["datasets"] = 0, ["trainingJobs"] = 0, ["evaluations"] = 0, ["models"] = 0, ["deployments"] = 0, ["qualityEvents"] = 0 };

    private static async Task AuditAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actor, string action, string entityType, string entityId, object evidence, CancellationToken cancellationToken)
    {
        const string sql = "INSERT INTO celar_ai_lifecycle_audit(audit_id,actor_user_id,action,entity_type,entity_id,evidence_json,occurred_at) VALUES(@id,@actor,@action,@type,@entity,@evidence::jsonb,@now)"; await using var command = new NpgsqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("actor", actor); command.Parameters.AddWithValue("action", action); command.Parameters.AddWithValue("type", entityType); command.Parameters.AddWithValue("entity", entityId); command.Parameters.Add("evidence", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(evidence); command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<((Guid Actual, Guid Effective)? Identity, PulseAiSystemAccess? Access, IResult? Error)> ReadAsync(HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var identity = Identities(context); if (identity is null) return (null, null, SessionRequired()); var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken); return !access.IsActive || !access.CanAsk ? (identity, access, Forbidden(PulseAiSystemIntelligencePolicy.AskPermission)) : (identity, access, null);
    }
    private static async Task<((Guid Actual, Guid Effective)? Identity, PulseAiSystemAccess? Access, IResult? Error)> ManageAsync(HttpContext context, PulseAiSystemIntelligenceService system, CancellationToken cancellationToken)
    {
        var identity = Identities(context); if (identity is null) return (null, null, SessionRequired()); var access = await system.LoadAccessAsync(identity.Value.Actual, cancellationToken); return CanManage(identity.Value, access) ? (identity, access, null) : (identity, access, Results.Json(new { module = "011", status = "management_forbidden", message = "An actual-session Super Administrator or Administrator is required. View-As remains read-only.", stateChanged = false }, statusCode: 403));
    }
    private static bool CanManage((Guid Actual, Guid Effective) identity, PulseAiSystemAccess access) => identity.Actual == identity.Effective && access.IsActive && (access.IsSuperAdministrator || access.RoleCodes.Any(ManagementRoles.Contains));
    private static (Guid Actual, Guid Effective)? Identities(HttpContext context) { var effective = UserId(context,"ProjectPulseEffectiveUserId") ?? UserId(context,"ProjectPulseSessionUserId"); if (effective is null) return null; var actual = UserId(context,"ProjectPulseActualUserId") ?? UserId(context,"ProjectPulseSessionUserId") ?? effective.Value; return (actual,effective.Value); }
    private static Guid? UserId(HttpContext context, string key) => context.Items.TryGetValue(key,out var value) && value is Guid id ? id : null;
    private static object AccessEvidence((Guid Actual, Guid Effective) identity, PulseAiSystemAccess access, bool canManage) => new { actualUserId = identity.Actual, effectiveUserId = identity.Effective, isViewAs = identity.Actual != identity.Effective, roles = access.RoleCodes.OrderBy(value => value).ToArray(), access.CanAsk, canManage, mutationAuthorityTransferredByViewAs = false, serverAuthorized = true };

    private static TimeZoneInfo ResolveTimeZone(string? value, out string warning) { warning = string.Empty; if (string.IsNullOrWhiteSpace(value)) { warning = "The browser did not supply a time zone; UTC was used."; return TimeZoneInfo.Utc; } try { return TimeZoneInfo.FindSystemTimeZoneById(value.Trim()); } catch { warning = $"The supplied time zone '{Limit(value,120,string.Empty)}' was not recognized; UTC was used."; return TimeZoneInfo.Utc; } }
    private static string ParentWbs(string value) { var index = value.LastIndexOf('.'); return index > 0 ? value[..index] : string.Empty; }
    private static string DetailLevel(string? value) => PulseAiSystemIntelligencePolicy.DetailLevels.Contains(value ?? string.Empty, StringComparer.OrdinalIgnoreCase) ? value!.ToLowerInvariant() : "comprehensive";
    private static string CorrelationId(HttpContext context) => context.Request.Headers.TryGetValue("X-Correlation-Id",out var value) && !string.IsNullOrWhiteSpace(value.ToString()) ? Limit(value.ToString(),160,string.Empty) : Limit(context.TraceIdentifier,160,string.Empty);
    private static string First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Limit(string? value, int maximum, string fallback) { var clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim(); return clean.Length <= maximum ? clean : clean[..maximum]; }
    private static string Required(string? value, int maximum, string message) { var clean = Limit(value,maximum,string.Empty); if (clean.Length == 0) throw new ArgumentException(message); return clean; }
    private static string RequiredUri(string? value) { var clean = Required(value,2000,"An approved private artifact URI is required."); if (!Uri.TryCreate(clean,UriKind.Absolute,out var uri) || uri.Scheme is not ("https" or "az" or "s3" or "file")) throw new ArgumentException("The artifact URI must use an approved absolute private-storage scheme."); return clean; }
    private static string RequiredSha(string? value) { var clean = Required(value,64,"A SHA-256 checksum is required.").ToLowerInvariant(); if (clean.Length != 64 || clean.Any(character => !Uri.IsHexDigit(character))) throw new ArgumentException("The checksum must be 64 hexadecimal characters."); return clean; }
    private static string Classification(string? value) => Limit(value,80,"internal").ToLowerInvariant() switch { "public" => "public", "confidential" => "confidential", "restricted" => "restricted", _ => "internal" };
    private static string DatasetState(string? value) => Limit(value,80,"reviewed").ToLowerInvariant() switch { "draft" => "draft", "approved" => "approved", "retired" => "retired", _ => "reviewed" };
    private static string ModelState(string? value) => Limit(value,80,"draft").ToLowerInvariant() switch { "evaluating" => "evaluating", "approved_test" => "approved_test", "test" => "test", "approved_production" => "approved_production", "production" => "production", "retired" => "retired", "rejected" => "rejected", _ => "draft" };
    private static string EnvironmentState(string? value) => Limit(value,80,"development").ToLowerInvariant() switch { "test" => "test", "production" => "production", _ => "development" };
    private static string TrainingMethod(string? value) => Limit(value,100,"evaluation_only").ToLowerInvariant() switch { "supervised_fine_tuning" => "supervised_fine_tuning", "lora" => "lora", "qlora" => "qlora", "distillation_candidate" => "distillation_candidate", _ => "evaluation_only" };
    private static string ReadString(JsonElement root, params string[] names) { foreach (var name in names) if (root.TryGetProperty(name,out var value)) return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : string.Empty; return string.Empty; }
    private static IResult? CandidateMutationBlocked()
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.Snapshot();
        if (!release.Requested && !release.CandidateReadOnly) return null;
        return Results.Json(new
        {
            module = "011",
            status = "release_candidate_read_only",
            message = "Celar AI schema, dataset, training, evaluation, model, and deployment lifecycle mutations are disabled on the exact-source release candidate.",
            configurationSourceCommit = release.ConfigurationSourceCommit,
            stateChanged = false
        }, statusCode: StatusCodes.Status423Locked);
    }

    private static IResult SessionRequired() => Results.Json(new { module = "011", status = "session_required", message = "A valid Pulse session is required." }, statusCode: 401);
    private static IResult Forbidden(string permission) => Results.Json(new { module = "011", status = "forbidden", requiredPermission = permission, message = "The current effective user is not authorized." }, statusCode: 403);
    private static IResult Validation(string message) => Results.Json(new { module = "011", status = "validation_failed", message, stateChanged = false }, statusCode: 400);
    private static bool DatabaseConfigured => new[] { "PTP_DB_HOST","PTP_DB_PORT","PTP_DB_NAME","PTP_DB_USER","PTP_DB_PASSWORD" }.All(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
    private static string ConnectionString() => new NpgsqlConnectionStringBuilder { Host = Environment.GetEnvironmentVariable("PTP_DB_HOST"), Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"),out var port) ? port : 5432, Database = Environment.GetEnvironmentVariable("PTP_DB_NAME"), Username = Environment.GetEnvironmentVariable("PTP_DB_USER"), Password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD"), IncludeErrorDetail = false, Pooling = true, MaxPoolSize = 12, Timeout = 8, CommandTimeout = 45 }.ConnectionString;

    private sealed record Intent(string Code, bool IncludeApis, bool IncludeTroubleshooting, bool IncludeEnhancement, bool IncludeDocuments, int MaximumTools, bool RequiresCurrentEvidence, string Reason);
    private sealed record DatasetRow(Guid DatasetVersionId,string Name,string Purpose,string Classification,string ArtifactUri,string Sha256,int ExampleCount,string State);
    private sealed record ModelRow(Guid ModelVersionId,string State);
    private sealed record TrainingSubmission(string Status,string ExternalJobId,string Diagnostic,bool Submitted);

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT NOT NULL DEFAULT '',applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
        CREATE TABLE IF NOT EXISTS celar_ai_dataset_versions(dataset_version_id UUID PRIMARY KEY,name TEXT NOT NULL,purpose TEXT NOT NULL,classification TEXT NOT NULL,artifact_uri TEXT NOT NULL,sha256 TEXT NOT NULL,example_count INTEGER NOT NULL DEFAULT 0 CHECK(example_count>=0),state TEXT NOT NULL,created_by UUID NOT NULL,created_at TIMESTAMPTZ NOT NULL,updated_at TIMESTAMPTZ NOT NULL,UNIQUE(name,sha256));
        CREATE TABLE IF NOT EXISTS celar_ai_training_jobs(training_job_id UUID PRIMARY KEY,dataset_version_id UUID NOT NULL,method TEXT NOT NULL,base_model TEXT NOT NULL,status TEXT NOT NULL,external_job_id TEXT NOT NULL DEFAULT '',configuration_json JSONB NOT NULL DEFAULT '{}'::jsonb,diagnostic_code TEXT NOT NULL DEFAULT '',created_by UUID NOT NULL,created_at TIMESTAMPTZ NOT NULL,updated_at TIMESTAMPTZ NOT NULL);
        CREATE TABLE IF NOT EXISTS celar_ai_evaluation_runs(evaluation_run_id UUID PRIMARY KEY,suite_code TEXT NOT NULL,model_version_id UUID NULL,status TEXT NOT NULL,score NUMERIC(8,6) NOT NULL DEFAULT 0,passed BOOLEAN NOT NULL DEFAULT FALSE,results_json JSONB NOT NULL DEFAULT '{}'::jsonb,created_by UUID NOT NULL,created_at TIMESTAMPTZ NOT NULL,completed_at TIMESTAMPTZ NOT NULL);
        CREATE TABLE IF NOT EXISTS celar_ai_model_versions(model_version_id UUID PRIMARY KEY,name TEXT NOT NULL,semantic_version TEXT NOT NULL,base_model TEXT NOT NULL,artifact_uri TEXT NOT NULL,sha256 TEXT NOT NULL,dataset_version_id UUID NULL,training_job_id UUID NULL,evaluation_run_id UUID NULL,state TEXT NOT NULL,created_by UUID NOT NULL,created_at TIMESTAMPTZ NOT NULL,updated_at TIMESTAMPTZ NOT NULL,UNIQUE(name,semantic_version));
        CREATE TABLE IF NOT EXISTS celar_ai_model_deployments(deployment_id UUID PRIMARY KEY,model_version_id UUID NOT NULL,environment_code TEXT NOT NULL,capability_code TEXT NOT NULL,status TEXT NOT NULL,endpoint_fingerprint TEXT NOT NULL DEFAULT '',rollback_model_version_id UUID NULL,created_by UUID NOT NULL,created_at TIMESTAMPTZ NOT NULL,updated_at TIMESTAMPTZ NOT NULL);
        CREATE TABLE IF NOT EXISTS celar_ai_answer_quality_events(answer_quality_event_id UUID PRIMARY KEY,actor_user_id UUID NOT NULL,question_sha256 TEXT NOT NULL,intent_code TEXT NOT NULL,trust_classification TEXT NOT NULL,question_answered BOOLEAN NOT NULL,successful_source_count INTEGER NOT NULL,failed_source_count INTEGER NOT NULL,citation_count INTEGER NOT NULL,confidence NUMERIC(8,6) NOT NULL,correlation_id TEXT NOT NULL,reasons_json JSONB NOT NULL DEFAULT '[]'::jsonb,created_at TIMESTAMPTZ NOT NULL);
        CREATE TABLE IF NOT EXISTS celar_ai_lifecycle_audit(audit_id UUID PRIMARY KEY,actor_user_id UUID NOT NULL,action TEXT NOT NULL,entity_type TEXT NOT NULL,entity_id TEXT NOT NULL,evidence_json JSONB NOT NULL DEFAULT '{}'::jsonb,occurred_at TIMESTAMPTZ NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_celar_ai_dataset_created ON celar_ai_dataset_versions(created_at DESC); CREATE INDEX IF NOT EXISTS ix_celar_ai_training_created ON celar_ai_training_jobs(created_at DESC); CREATE INDEX IF NOT EXISTS ix_celar_ai_evaluation_created ON celar_ai_evaluation_runs(created_at DESC); CREATE INDEX IF NOT EXISTS ix_celar_ai_model_created ON celar_ai_model_versions(created_at DESC); CREATE INDEX IF NOT EXISTS ix_celar_ai_deployment_created ON celar_ai_model_deployments(created_at DESC); CREATE INDEX IF NOT EXISTS ix_celar_ai_quality_created ON celar_ai_answer_quality_events(created_at DESC); CREATE INDEX IF NOT EXISTS ix_celar_ai_audit_occurred ON celar_ai_lifecycle_audit(occurred_at DESC);
        INSERT INTO schema_migrations(migration_id,description,applied_at) VALUES('celar_ai_production_platform_runtime_v1','Celar AI production lifecycle, answer quality, private training, model registry, deployment planning, and audit schema',NOW()) ON CONFLICT(migration_id) DO UPDATE SET description=EXCLUDED.description,applied_at=EXCLUDED.applied_at;
        """;

    [GeneratedRegex(@"\s+",RegexOptions.CultureInvariant)] private static partial Regex Whitespace();
    [GeneratedRegex(@"\b(what|which)\s+(day|date|time)\b|\b(today|current date|current time|day is it)\b",RegexOptions.CultureInvariant)] private static partial Regex DateTimeQuestion();
    [GeneratedRegex(@"\b(400|401|403|404|405|408|409|422|429|500|502|503|504)\b",RegexOptions.CultureInvariant)] private static partial Regex HttpStatus();
    [GeneratedRegex(@"\b(financial|finance|revenue|cost|margin|profit|billing|invoice|expense|utilization|budget|rate|contract|forecast|report|analytics)\b",RegexOptions.CultureInvariant)] private static partial Regex FinancialQuestion();
}
