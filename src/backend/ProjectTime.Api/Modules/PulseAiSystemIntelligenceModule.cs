using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class PulseAiSystemIntelligenceModule
{
    public static IEndpointRouteBuilder MapPulseAiSystemIntelligenceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/pulse-ai/v1/system/readiness",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)GetReadinessAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system/tools",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)GetToolsAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system/apis",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)GetApisAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system/apis/{apiId}",
            (Func<string, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)GetApiAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/system/apis/{apiId}/retest",
            (Func<string, PulseAiSafeApiRetestRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)RetestApiAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/system/questions",
            (Func<PulseAiSystemQuestionRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)AskAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system/conversations",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)ListConversationsAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/system/conversations",
            (Func<PulseAiConversationCreateRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)CreateConversationAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/system/conversations/{conversationId:guid}",
            (Func<Guid, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)GetConversationAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/system/conversations/{conversationId:guid}/messages",
            (Func<Guid, PulseAiSystemQuestionRequest, HttpContext, PulseAiSystemIntelligenceService, CancellationToken, Task<IResult>>)AskInConversationAsync);
        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        return Results.Ok(new
        {
            module = "011",
            feature = PulseAiSystemIntelligencePolicy.FeatureCode,
            access = AccessEvidence(context, identities.Value, access),
            readiness = await service.GetReadinessAsync(access, cancellationToken),
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> GetToolsAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var tools = service.ListTools()
            .Where(tool => !tool.RequiresApiInventoryPermission || access.CanViewApis)
            .Where(tool => !tool.RequiresTroubleshootingPermission || access.CanTroubleshoot)
            .OrderBy(tool => tool.Priority)
            .ThenBy(tool => tool.Code)
            .ToArray();
        return Results.Ok(new
        {
            module = "011",
            status = "pulse_ai_system_tool_registry_loaded",
            contractVersion = PulseAiSystemIntelligencePolicy.ContractVersion,
            access = AccessEvidence(context, identities.Value, access),
            summary = new
            {
                registered = service.ListTools().Count,
                authorized = tools.Length,
                troubleshooting = tools.Count(tool => tool.RequiresTroubleshootingPermission),
                administrativeEvidence = tools.Count(tool => tool.AdministrativeEvidence)
            },
            tools,
            rules = new[]
            {
                "A tool is callable only through the source-controlled allowlist; a user or model cannot supply an arbitrary URL.",
                "Every tool is a same-origin GET and the owning endpoint re-evaluates the effective user's authorization.",
                "A 401/403 result is preserved as unauthorized evidence and is not converted into a system outage.",
                "Tool response bodies remain inside the bounded server-side answer context and are not returned by this registry."
            },
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> GetApisAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanViewApis) return Forbidden(PulseAiSystemIntelligencePolicy.ApiInventoryPermission);
        var search = Query(context, "search", 500);
        var moduleCode = Query(context, "module", 20);
        var method = Query(context, "method", 12);
        var safeRetest = bool.TryParse(context.Request.Query["safeRetest"], out var safe) ? safe : (bool?)null;
        var limit = int.TryParse(context.Request.Query["limit"], out var requested)
            ? Math.Clamp(requested, 1, service.Options().MaximumApiResults)
            : service.Options().MaximumApiResults;
        var apis = service.ListApis(search, moduleCode, method, safeRetest, limit);
        return Results.Ok(new
        {
            module = "011",
            sourceModule = "013",
            status = "live_registered_api_inventory_loaded",
            contractVersion = PulseAiSystemIntelligencePolicy.ApiCatalogVersion,
            access = AccessEvidence(context, identities.Value, access),
            filters = new { search, moduleCode, method, safeRetest, limit },
            summary = new
            {
                total = apis.Count,
                modules = apis.Select(api => api.ModuleCode).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                get = apis.Count(api => HttpMethods.IsGet(api.Method)),
                post = apis.Count(api => HttpMethods.IsPost(api.Method)),
                put = apis.Count(api => HttpMethods.IsPut(api.Method)),
                patch = apis.Count(api => HttpMethods.IsPatch(api.Method)),
                delete = apis.Count(api => HttpMethods.IsDelete(api.Method)),
                parameterized = apis.Count(api => api.Parameterized),
                safeRetestSupported = apis.Count(api => api.SafeRetestSupported),
                sessionProtected = apis.Count(api => api.RequiresApplicationSession),
                anonymous = apis.Count(api => api.AllowsAnonymous)
            },
            apis,
            interpretation = new
            {
                registrationMeans = "The route/method is registered in the running ASP.NET EndpointDataSource for this revision.",
                registrationDoesNotProve = "A registered route does not by itself prove that its database, integration, record scope, or downstream dependency is healthy.",
                safeRetest = "Only explicitly classified GET routes without parameters, downloads, authentication transitions, refresh/probe behavior, or recursion risk are eligible."
            },
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> GetApiAsync(
        string apiId,
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanViewApis) return Forbidden(PulseAiSystemIntelligencePolicy.ApiInventoryPermission);
        var api = service.FindApi(apiId);
        if (api is null)
        {
            return Results.Json(new
            {
                module = "011",
                status = "registered_api_not_found",
                apiId,
                message = "The API identifier is not present in the running application's endpoint registry."
            }, statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(new
        {
            module = "011",
            status = "registered_api_detail_loaded",
            access = AccessEvidence(context, identities.Value, access),
            api,
            troubleshooting = new
            {
                safeRetestSupported = api.SafeRetestSupported,
                safeRetestReason = api.SafeRetestReason,
                requiredConfirmation = api.SafeRetestSupported
                    ? PulseAiSystemIntelligencePolicy.RetestConfirmation
                    : string.Empty,
                recommendedModules = new[] { "013", "016", "078", "998", "076" }
            },
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> RetestApiAsync(
        string apiId,
        PulseAiSafeApiRetestRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        if (identities.Value.Actual != identities.Value.Effective) return ViewAsMutationBlocked();
        var access = await service.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!access.IsActive || !access.CanRetest) return Forbidden(PulseAiSystemIntelligencePolicy.RetestPermission);
        var api = service.FindApi(apiId);
        if (api is null)
        {
            return Results.Json(new
            {
                module = "011",
                status = "registered_api_not_found",
                apiId
            }, statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(await service.RetestApiAsync(
            context,
            api,
            request.Confirmation,
            cancellationToken));
    }

    private static async Task<IResult> AskAsync(
        PulseAiSystemQuestionRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var result = await service.AskAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            context,
            cancellationToken);
        return result.Status == "blocked"
            ? Results.Json(new
            {
                module = "011",
                feature = PulseAiSystemIntelligencePolicy.FeatureCode,
                access = AccessEvidence(context, identities.Value, access),
                result = result.ToPublicResponse()
            }, statusCode: StatusCodes.Status400BadRequest)
            : Results.Ok(new
            {
                module = "011",
                feature = PulseAiSystemIntelligencePolicy.FeatureCode,
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
            });
    }

    private static Task<IResult> AskInConversationAsync(
        Guid conversationId,
        PulseAiSystemQuestionRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken) =>
        AskAsync(
            request with { ConversationId = conversationId },
            context,
            service,
            cancellationToken);

    private static async Task<IResult> ListConversationsAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var historyUserId = identities.Value.Actual;
        var access = await service.LoadAccessAsync(historyUserId, cancellationToken);
        if (!access.IsActive || !access.CanViewConversations) return Forbidden(PulseAiSystemIntelligencePolicy.ConversationPermission);
        var limit = int.TryParse(context.Request.Query["limit"], out var requested)
            ? Math.Clamp(requested, 1, 200)
            : 50;
        var conversations = await service.ListConversationsAsync(historyUserId, limit, cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            status = "pulse_ai_conversations_loaded",
            access = AccessEvidence(context, identities.Value, access),
            summary = new { returned = conversations.Count, limit },
            conversations,
            viewAsHistoryPolicy = identities.Value.Actual != identities.Value.Effective
                ? "Only the actual administrator's own Pulse AI conversation history is returned while View-As is active."
                : "The current user's own Pulse AI conversation history is returned.",
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> CreateConversationAsync(
        PulseAiConversationCreateRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        if (identities.Value.Actual != identities.Value.Effective) return ViewAsMutationBlocked();
        var release = ProjectPulseAiReleaseRuntimePolicy.Snapshot();
        if (release.IsCandidate)
        {
            return Results.Json(new
            {
                module = "011",
                status = "release_candidate_read_only",
                message = "Durable conversation creation is disabled on the exact-source release candidate. Read-only AI questions remain available without persistence.",
                configurationSourceCommit = release.ConfigurationSourceCommit,
                stateChanged = false
            }, statusCode: StatusCodes.Status423Locked);
        }
        var access = await service.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!access.IsActive || !access.CanViewConversations) return Forbidden(PulseAiSystemIntelligencePolicy.ConversationPermission);
        var conversation = await service.CreateConversationAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            cancellationToken);
        return conversation is null
            ? Results.Json(new
            {
                module = "011",
                status = "conversation_schema_unavailable",
                message = "Migration 054 is required before durable Pulse AI conversations can be created."
            }, statusCode: StatusCodes.Status503ServiceUnavailable)
            : Results.Ok(new
            {
                module = "011",
                status = "pulse_ai_conversation_created",
                conversation,
                stateChanged = true
            });
    }

    private static async Task<IResult> GetConversationAsync(
        Guid conversationId,
        HttpContext context,
        PulseAiSystemIntelligenceService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var historyUserId = identities.Value.Actual;
        var access = await service.LoadAccessAsync(historyUserId, cancellationToken);
        if (!access.IsActive || !access.CanViewConversations) return Forbidden(PulseAiSystemIntelligencePolicy.ConversationPermission);
        var conversation = await service.GetConversationAsync(
            conversationId,
            historyUserId,
            cancellationToken);
        return conversation is null
            ? Results.Json(new
            {
                module = "011",
                status = "conversation_not_found_or_not_authorized",
                conversationId,
                message = "The conversation is not available in the actual user's Pulse AI history."
            }, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(new
            {
                module = "011",
                status = "pulse_ai_conversation_loaded",
                conversation,
                responsesPersisted = true,
                generatedAt = DateTimeOffset.UtcNow
            });
    }

    private static (Guid Actual, Guid Effective)? Identities(HttpContext context)
    {
        var effective = EffectiveUserId(context);
        if (effective is null) return null;
        return (ActualUserId(context) ?? effective.Value, effective.Value);
    }

    private static Guid? EffectiveUserId(HttpContext context)
    {
        if (context.Items.TryGetValue("ProjectPulseEffectiveUserId", out var effective)
            && effective is Guid effectiveUserId)
        {
            return effectiveUserId;
        }
        if (context.Items.TryGetValue("ProjectPulseSessionUserId", out var session)
            && session is Guid sessionUserId)
        {
            return sessionUserId;
        }
        return null;
    }

    private static Guid? ActualUserId(HttpContext context)
    {
        if (context.Items.TryGetValue("ProjectPulseActualUserId", out var actual)
            && actual is Guid actualUserId)
        {
            return actualUserId;
        }
        if (context.Items.TryGetValue("ProjectPulseSessionUserId", out var session)
            && session is Guid sessionUserId)
        {
            return sessionUserId;
        }
        return null;
    }

    private static object AccessEvidence(
        HttpContext context,
        (Guid Actual, Guid Effective) identities,
        PulseAiSystemAccess access)
    {
        var isViewAs = identities.Actual != identities.Effective
            || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
                && value is bool active
                && active);
        return new
        {
            actualUserId = identities.Actual,
            effectiveUserId = identities.Effective,
            isViewAs,
            mode = isViewAs ? "administrator_read_only_view_as" : "current_user",
            roles = access.RoleCodes.OrderBy(value => value).ToArray(),
            permissions = access.PermissionCodes
                .Where(permission => permission.Contains("PULSE_AI", StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value)
                .ToArray(),
            mutationAuthorityTransferred = false,
            serverAuthorized = true
        };
    }

    private static string Query(HttpContext context, string key, int maximumLength)
    {
        var value = context.Request.Query[key].ToString().Trim();
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

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
            message = "The current effective user is not authorized for this Pulse AI system-intelligence operation."
        }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsMutationBlocked() =>
        Results.Json(new
        {
            module = "011",
            status = "view_as_mutation_blocked",
            message = "Administrator View-As is read-only. Questions may be answered without persistence, but conversations and safe retests cannot be created for the viewed user."
        }, statusCode: StatusCodes.Status403Forbidden);
}
