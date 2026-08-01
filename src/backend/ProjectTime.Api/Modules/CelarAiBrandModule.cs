using System.Security.Cryptography;
using System.Text;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class CelarAiBrandModule
{
    private static readonly HashSet<string> Module064AdministratorRoles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "SUPER_ADMINISTRATOR",
            "SYSTEM_ADMINISTRATOR",
            "ADMINISTRATOR"
        };

    public static IEndpointRouteBuilder MapCelarAiBrandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            CelarAiBrandProfile.AboutRoute,
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)GetAboutAsync);
        endpoints.MapGet(
            CelarAiBrandProfile.ProviderBridgeRoute,
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)GetProviderBridgeAsync);
        endpoints.MapPost(
            CelarAiBrandProfile.ChatRoute,
            (Func<PulseAiSystemQuestionRequest, HttpContext, PulseAiSystemIntelligenceService, PulseAiSystemIntelligenceRepository, CancellationToken, Task<IResult>>)ChatAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAboutAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        return Results.Ok(new
        {
            module = "011",
            status = "celar_ai_identity_loaded",
            brand = CelarAiBrandProfile.ToPublicProfile(),
            access = AccessEvidence(context, identities.Value, access),
            privacy = new
            {
                customerDataRetrieved = false,
                projectDataRetrieved = false,
                documentTextRetrieved = false,
                financialDataRetrieved = false,
                providerCalled = false,
                credentialsReturned = false
            },
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> GetProviderBridgeAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var actualAccess = identities.Value.Actual == identities.Value.Effective
            ? access
            : await service.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!actualAccess.IsActive
            || !actualAccess.RoleCodes.Any(Module064AdministratorRoles.Contains))
            return Forbidden("MODULE_064_ADMINISTRATOR");
        var privateOptions = PulseAiPrivateRagOptions.FromEnvironment();
        var privateModelReady = privateOptions.Enabled && privateOptions.InferenceConfigured;
        return Results.Ok(new
        {
            module = "064",
            consumerModule = "011",
            status = privateModelReady
                ? "celar_ai_private_model_route_ready"
                : "celar_ai_private_model_route_not_configured",
            brand = new
            {
                name = CelarAiBrandProfile.BrandName,
                platform = CelarAiBrandProfile.PlatformName,
                tagline = CelarAiBrandProfile.Tagline
            },
            architecture = new
            {
                celarAiRole = "Private operational-intelligence orchestrator and governed consumer of provider routes",
                module064Role = "Provider credentials, model selection, health, routing, circuit breakers, usage, and sanitized fallback",
                celarAiIsExternalVendorProvider = false,
                privateModelIsFirstClassTarget = true,
                externalProvidersAreOptional = true
            },
            privateModel = new
            {
                enabled = privateOptions.Enabled,
                configured = privateOptions.InferenceConfigured,
                ready = privateModelReady,
                model = privateOptions.InferenceModel.Length > 0 ? privateOptions.InferenceModel : "Not configured",
                bearerTokenConfigured = privateOptions.InferenceBearerToken.Length > 0,
                endpointConfigured = privateOptions.InferenceEndpoint.Length > 0,
                endpointReturned = false,
                privateHostAllowlistCount = privateOptions.PrivateHostAllowlist.Count,
                confidentialContextEligible = privateModelReady,
                rawInternalDocumentsMayUsePublicProviders = false
            },
            featureRoutes = new object[]
            {
                new { feature = "celar_ai_system_chat", primary = "private_celar_model_or_deterministic_system_synthesis", external = "sanitized_generic_reasoning_only" },
                new { feature = "timesheet_document_grounding", primary = "private_celar_model", external = "raw_document_route_prohibited" },
                new { feature = "system_help_search", primary = "private_celar_model_and_governed_tools", external = "sanitized_generic_reasoning_only" },
                new { feature = "flowhive_document_planning", primary = "private_celar_model_and_deterministic_schedule_engine", external = "generic_planning_checklist_only" },
                new { feature = "reporting_financial_insight", primary = "deterministic_pulse_tools_and_private_celar_explanation", external = "disabled_by_default" }
            },
            rules = new[]
            {
                "Module 064 remains the only approved external-provider configuration and routing boundary.",
                "The private Celar AI endpoint is configured through private runtime settings and secret references, not through a public provider API-key form.",
                "Raw SOW, GSD, customer, contract, architecture, employee, rate, and financial context is not eligible for direct Claude or OpenAI routing.",
                "A safety refusal ends the request and is never bypassed by another provider.",
                "No secret or endpoint value is returned by this readiness response."
            },
            access = AccessEvidence(context, identities.Value, access),
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> ChatAsync(
        PulseAiSystemQuestionRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        PulseAiSystemIntelligenceRepository repository,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);

        var question = Clean(request.Question, service.Options().MaximumQuestionCharacters);
        if (question.Length == 0)
        {
            return Results.Json(new
            {
                module = "011",
                status = "question_required",
                message = "Enter a question for Celar AI."
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        PulseAiSystemQuestionResult result;
        if (CelarAiBrandProfile.IsIdentityQuestion(question))
        {
            result = await CreateIdentityAnswerAsync(
                request with { Question = question },
                identities.Value.Actual,
                identities.Value.Effective,
                context,
                repository,
                access.CanViewConversations,
                cancellationToken);
        }
        else
        {
            result = await service.AskAsync(
                identities.Value.Actual,
                identities.Value.Effective,
                request with { Question = question },
                context,
                cancellationToken);
        }

        var response = new
        {
            module = "011",
            brand = CelarAiBrandProfile.BrandName,
            feature = "celar_ai_system_intelligence",
            technicalCompatibilityFeature = PulseAiSystemIntelligencePolicy.FeatureCode,
            access = AccessEvidence(context, identities.Value, access),
            result = result.ToPublicResponse(),
            conversationPersistence = new
            {
                enabled = result.Persisted,
                viewAsQuestionPersisted = identities.Value.Actual == identities.Value.Effective && result.Persisted,
                closesOrRefreshesDoNotDeleteCompletedMessages = result.Persisted
            },
            externalProviderCalled = false,
            stateChanged = result.Persisted
        };

        return result.Status == "blocked"
            ? Results.Json(response, statusCode: StatusCodes.Status400BadRequest)
            : Results.Ok(response);
    }

    private static async Task<PulseAiSystemQuestionResult> CreateIdentityAnswerAsync(
        PulseAiSystemQuestionRequest request,
        Guid actualUserId,
        Guid effectiveUserId,
        HttpContext context,
        PulseAiSystemIntelligenceRepository repository,
        bool canViewConversations,
        CancellationToken cancellationToken)
    {
        var dataAsOf = DateTimeOffset.UtcNow;
        var detailLevel = NormalizeDetailLevel(request.DetailLevel);
        var correlationId = CorrelationId(context);
        var mayPersist = actualUserId == effectiveUserId && canViewConversations;
        var conversation = mayPersist
            ? await repository.EnsureConversationAsync(
                request.ConversationId,
                actualUserId,
                effectiveUserId,
                request.Mode ?? "system_help",
                cancellationToken)
            : null;
        var persisted = conversation is not null;
        var conversationId = conversation?.ConversationId
            ?? request.ConversationId
            ?? Guid.NewGuid();

        var userMessageId = Guid.NewGuid();
        if (persisted)
        {
            var savedUser = await repository.AppendMessageAsync(
                conversationId,
                effectiveUserId,
                role: "user",
                status: "completed",
                messageText: request.Question ?? string.Empty,
                structuredResponse: null,
                inquiryRunId: null,
                privateAnswerRunId: null,
                correlationId: correlationId,
                modelProvider: string.Empty,
                modelName: string.Empty,
                toolCodes: [],
                sourceStates: new { source = "celar_ai_identity_profile" },
                dataAsOf: dataAsOf,
                cancellationToken);
            if (savedUser.MessageId != Guid.Empty) userMessageId = savedUser.MessageId;
        }

        var inquiryRunId = persisted
            ? await repository.CreateInquiryRunAsync(
                conversationId,
                userMessageId,
                actualUserId,
                effectiveUserId,
                intentCode: "product_help",
                detailLevel,
                questionSha256: Sha256(request.Question ?? string.Empty),
                correlationId,
                cancellationToken)
            : Guid.NewGuid();

        var answer = CelarAiBrandProfile.CreateDetailedAnswer(dataAsOf);
        var provisional = new PulseAiSystemQuestionResult(
            ConversationId: conversationId,
            UserMessageId: userMessageId,
            AssistantMessageId: Guid.Empty,
            InquiryRunId: inquiryRunId,
            Status: "completed",
            IntentCode: "product_help",
            DetailLevel: detailLevel,
            Answer: answer,
            Sources: [],
            RelevantApis: [],
            ToolResults: [],
            ModelProvider: "celar_ai_canonical_knowledge",
            ModelName: "Celar AI identity profile v1",
            CorrelationId: correlationId,
            Warnings: persisted
                ? []
                : ["The answer completed, but migration 054 conversation persistence was not available for this request."],
            Persisted: persisted);

        var assistantMessageId = Guid.NewGuid();
        if (persisted)
        {
            var savedAssistant = await repository.AppendMessageAsync(
                conversationId,
                effectiveUserId,
                role: "assistant",
                status: "completed",
                messageText: answer.DirectConclusion,
                structuredResponse: provisional.ToPublicResponse(),
                inquiryRunId: inquiryRunId == Guid.Empty ? null : inquiryRunId,
                privateAnswerRunId: null,
                correlationId: correlationId,
                modelProvider: provisional.ModelProvider,
                modelName: provisional.ModelName,
                toolCodes: [],
                sourceStates: new
                {
                    identityProfile = CelarAiBrandProfile.ContractVersion,
                    liveRecordToolsUsed = false,
                    privateDocumentsUsed = false,
                    externalProviderUsed = false
                },
                dataAsOf: dataAsOf,
                cancellationToken);
            if (savedAssistant.MessageId != Guid.Empty) assistantMessageId = savedAssistant.MessageId;

            await repository.CompleteInquiryRunAsync(
                inquiryRunId,
                assistantMessageId,
                status: "completed",
                selectedTools: [],
                toolResults: [],
                registeredApiCount: 0,
                confidence: answer.Confidence,
                diagnosticCode: "celar_ai_identity_answer",
                cancellationToken);
        }

        return provisional with
        {
            AssistantMessageId = assistantMessageId,
            Persisted = persisted && assistantMessageId != Guid.Empty
        };
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
        HttpContext context,
        (Guid Actual, Guid Effective) identities,
        PulseAiSystemAccess access)
    {
        var viewAs = identities.Actual != identities.Effective
            || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
                && value is bool active
                && active);
        return new
        {
            actualUserId = identities.Actual,
            effectiveUserId = identities.Effective,
            isViewAs = viewAs,
            mode = viewAs ? "administrator_read_only_view_as" : "current_user",
            roles = access.RoleCodes.OrderBy(value => value).ToArray(),
            permissions = access.PermissionCodes
                .Where(permission => permission.Contains("PULSE_AI", StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value)
                .ToArray(),
            brand = CelarAiBrandProfile.BrandName,
            technicalPermissionCodesRetained = true,
            mutationAuthorityTransferred = false,
            serverAuthorized = true
        };
    }

    private static string NormalizeDetailLevel(string? value) =>
        PulseAiSystemIntelligencePolicy.DetailLevels.Contains(value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? value!.ToLowerInvariant()
            : "comprehensive";

    private static string CorrelationId(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Correlation-Id", out var value)
            && !string.IsNullOrWhiteSpace(value.ToString())
            ? Clean(value.ToString(), 160)
            : Clean(context.TraceIdentifier, 160);

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static IResult SessionRequired() =>
        Results.Json(new
        {
            module = "011",
            status = "session_required",
            message = "A valid Pulse session is required to use Celar AI."
        }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden(string permission) =>
        Results.Json(new
        {
            module = "011",
            status = "forbidden",
            requiredPermission = permission,
            message = "The current effective user is not authorized for this Celar AI operation."
        }, statusCode: StatusCodes.Status403Forbidden);
}
