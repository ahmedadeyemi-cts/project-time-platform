using System.Diagnostics;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class CelarAiCapabilityRoutingModule
{
    private static readonly HashSet<string> AdministratorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "SYSTEM_ADMINISTRATOR",
        "ADMINISTRATOR"
    };

    public static IEndpointRouteBuilder MapCelarAiCapabilityRoutingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/ai-configuration/routes",
            (Func<HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)GetRoutesAsync);
        endpoints.MapPut(
            "/api/ai-configuration/routes/{featureCode}",
            (Func<string, CelarAiRouteUpdateRequest, HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)SaveRouteAsync);
        endpoints.MapPost(
            "/api/ai-configuration/routes/{featureCode}/reset",
            (Func<string, CelarAiRouteUpdateRequest, HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)ResetRouteAsync);
        endpoints.MapGet(
            "/api/ai-configuration/consumers",
            (Func<HttpContext, CelarAiCapabilityRoutingStore, CelarAiConsumerAssuranceRegistry, CancellationToken, Task<IResult>>)GetConsumersAsync);

        endpoints.MapGet(
            "/api/ai-configuration/private-model",
            (Func<HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)GetPrivateModelAsync);
        endpoints.MapPut(
            "/api/ai-configuration/private-model/settings",
            (Func<CelarAiPrivateModelSettingsRequest, HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)SavePrivateModelSettingsAsync);
        endpoints.MapPut(
            "/api/ai-configuration/private-model/secret",
            (Func<CelarAiPrivateModelSecretRequest, HttpContext, CelarAiCapabilityRoutingStore, CancellationToken, Task<IResult>>)SavePrivateModelSecretAsync);
        endpoints.MapPost(
            "/api/ai-configuration/private-model/test",
            (Func<HttpContext, CelarAiCapabilityRoutingStore, CelarAiPrivateGenerationTarget, CancellationToken, Task<IResult>>)TestPrivateModelAsync);

        endpoints.MapPost(
            "/api/project-flowhive/ai/generate",
            (Func<CelarAiComposeRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiEnterprisePlatformService, CancellationToken, Task<IResult>>)GenerateFlowHiveAsync);
        endpoints.MapPost(
            "/api/sow-gsd-planning/ai/generate",
            (Func<CelarAiComposeRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiEnterprisePlatformService, CancellationToken, Task<IResult>>)GenerateSowAsync);
        endpoints.MapPost(
            "/api/project-closeout/ai/communication",
            (Func<CelarAiCloseoutCommunicationRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiCapabilityRouter, CancellationToken, Task<IResult>>)GenerateCloseoutCommunicationAsync);

        return endpoints;
    }

    private static async Task<IResult> GetRoutesAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: false, cancellationToken);
        if (authorization is not null) return authorization;
        var routes = await store.LoadRoutesAsync(cancellationToken);
        return Results.Ok(new
        {
            module = "064",
            status = "celar_ai_capability_routes_loaded",
            contractVersion = "celar-ai-capability-routing-v1",
            defaultOrder = CelarAiCapabilityTargets.DefaultOrder,
            availableTargets = new object[]
            {
                new { code = CelarAiCapabilityTargets.CelarAi, displayName = "Celar AI", kind = "private_orchestrator", publicProvider = false },
                new { code = CelarAiCapabilityTargets.Claude, displayName = "Claude", kind = "sanitized_external", publicProvider = true },
                new { code = CelarAiCapabilityTargets.OpenAi, displayName = "OpenAI", kind = "sanitized_external", publicProvider = true },
                new { code = CelarAiCapabilityTargets.Local, displayName = "Governed local template", kind = "deterministic_fallback", publicProvider = false }
            },
            routes = routes.Select(route => route.ToPublicResponse()).ToArray(),
            controls = new
            {
                localFallbackRequired = true,
                duplicateTargetsAllowed = false,
                safetyRefusalFailover = false,
                privacyPolicyEditable = false,
                rawPrivateContextEligibleForPublicProviders = false,
                viewAsMutationAllowed = false
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> SaveRouteAsync(
        string featureCode,
        CelarAiRouteUpdateRequest request,
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        var actor = ActualSessionUserId(context)!.Value;
        try
        {
            var route = await store.SaveRouteAsync(
                featureCode,
                request.Targets ?? [],
                request.ExpectedRevision,
                actor,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "celar_ai_capability_route_saved",
                route = route.ToPublicResponse(),
                message = $"{route.DisplayName} now uses {string.Join(" → ", route.Targets.Select(DisplayTarget))}.",
                secretValuesReturned = false,
                stateChanged = true
            });
        }
        catch (CelarAiConfigurationConflictException exception)
        {
            return Results.Json(new { status = "revision_conflict", message = exception.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_route", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 could not save the {Feature} capability route.", featureCode);
            return Results.Json(
                new { status = "route_save_unavailable", message = "The capability route could not be saved." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> ResetRouteAsync(
        string featureCode,
        CelarAiRouteUpdateRequest request,
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        try
        {
            var route = await store.ResetRouteAsync(
                featureCode,
                request.ExpectedRevision,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "celar_ai_capability_route_reset",
                route = route.ToPublicResponse(),
                message = $"{route.DisplayName} was reset to Celar AI → Claude → OpenAI → Governed local template.",
                stateChanged = true
            });
        }
        catch (CelarAiConfigurationConflictException exception)
        {
            return Results.Json(new { status = "revision_conflict", message = exception.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_route", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 could not reset the {Feature} capability route.", featureCode);
            return Results.Json(
                new { status = "route_reset_unavailable", message = "The capability route could not be reset." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> GetConsumersAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CelarAiConsumerAssuranceRegistry assurance,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: false, cancellationToken);
        if (authorization is not null) return authorization;
        var routes = (await store.LoadRoutesAsync(cancellationToken)).ToDictionary(route => route.FeatureCode, StringComparer.OrdinalIgnoreCase);
        return Results.Ok(new
        {
            module = "064",
            status = "celar_ai_consumer_assurance_loaded",
            consumers = assurance.Snapshots().Select(item => new
            {
                feature = item.Feature,
                module = item.Module,
                entryPoint = item.EntryPoint,
                route = routes.TryGetValue(item.Feature, out var route) ? route.Targets : CelarAiCapabilityTargets.DefaultOrder,
                item.CentralRouterConnected,
                item.PrivateContextCompliant,
                item.DirectProviderFree,
                item.LastExercisedAt,
                item.LastSuccessAt,
                item.LastFailureAt,
                item.LastTarget,
                item.LastOutcome,
                item.LastCorrelationId
            }),
            buildPolicy = new
            {
                directClaudeOrOpenAiClientsAllowedInConsumers = false,
                providerKeysReadableByConsumers = false,
                module064BoundaryRequired = true,
                privateEvidenceExternalized = false
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> GetPrivateModelAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: false, cancellationToken);
        if (authorization is not null) return authorization;
        var profile = await store.LoadPrivateModelProfileAsync(cancellationToken);
        var policy = PrivateEndpointPolicy(profile);
        return Results.Ok(new
        {
            module = "064",
            status = profile.Ready && policy == "private_endpoint_approved"
                ? "celar_ai_private_model_ready"
                : profile.Configured
                    ? "celar_ai_private_model_partially_ready"
                    : "celar_ai_private_model_not_configured",
            profile = profile.ToPublicResponse(policy),
            secureStore = new
            {
                databaseAvailable = store.DatabaseAvailable,
                encryptionAvailable = store.SecretEncryptionAvailable,
                endpointWriteOnly = true,
                tokenWriteOnly = true,
                endpointReturned = false,
                tokenReturned = false
            },
            requiredRuntime = new
            {
                openAiCompatiblePrivateEndpoint = true,
                privateOrAllowlistedHost = true,
                modelNameRequired = true,
                bearerTokenOptionalWhenEndpointUsesAnotherApprovedAuthenticationMethod = true
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> SavePrivateModelSettingsAsync(
        CelarAiPrivateModelSettingsRequest request,
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        try
        {
            var profile = await store.SavePrivateModelSettingsAsync(
                request,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "celar_ai_private_model_settings_saved",
                profile = profile.ToPublicResponse(PrivateEndpointPolicy(profile)),
                message = "The private Celar AI settings were saved. Endpoint and token values are not returned.",
                endpointReturned = false,
                tokenReturned = false,
                stateChanged = true
            });
        }
        catch (CelarAiConfigurationConflictException exception)
        {
            return Results.Json(new { status = "revision_conflict", message = exception.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_private_model_settings", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 could not save private Celar AI settings.");
            return Results.Json(
                new { status = "private_model_settings_unavailable", message = "The private model settings could not be saved." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> SavePrivateModelSecretAsync(
        CelarAiPrivateModelSecretRequest request,
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        try
        {
            var profile = await store.SavePrivateModelSecretAsync(
                request,
                ActualSessionUserId(context)!.Value,
                cancellationToken);
            return Results.Ok(new
            {
                module = "064",
                status = "celar_ai_private_model_secret_saved",
                profile = profile.ToPublicResponse(PrivateEndpointPolicy(profile)),
                message = "The private Celar AI bearer token was encrypted and saved. It cannot be viewed after saving.",
                endpointReturned = false,
                tokenReturned = false,
                stateChanged = true
            });
        }
        catch (CelarAiConfigurationConflictException exception)
        {
            return Results.Json(new { status = "revision_conflict", message = exception.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { status = "invalid_private_model_secret", message = exception.Message });
        }
        catch (Exception exception)
        {
            Log(context).LogError(exception, "Module 064 could not save the private Celar AI bearer token.");
            return Results.Json(
                new { status = "private_model_secret_unavailable", message = "The private model token could not be saved securely." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> TestPrivateModelAsync(
        HttpContext context,
        CelarAiCapabilityRoutingStore store,
        CelarAiPrivateGenerationTarget target,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var authorization = await AuthorizeAdministratorAsync(context, requireSameOrigin: true, cancellationToken);
        if (authorization is not null) return authorization;
        var profile = await store.LoadPrivateModelProfileAsync(cancellationToken);
        if (!profile.Configured)
            return Results.BadRequest(new
            {
                status = "private_model_not_configured",
                message = "Save a private endpoint and model before testing Celar AI."
            });
        var stopwatch = Stopwatch.StartNew();
        var result = await target.ProbeAsync(profile, cancellationToken);
        stopwatch.Stop();
        return Results.Json(new
        {
            status = result.Available ? "private_model_available" : "private_model_unavailable",
            configured = profile.Configured,
            available = result.Available,
            model = profile.Model,
            latencyMilliseconds = stopwatch.ElapsedMilliseconds,
            diagnosticCode = result.Code,
            requestId = result.RequestId,
            endpointReturned = false,
            tokenReturned = false,
            testedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        }, statusCode: result.Available ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> GenerateFlowHiveAsync(
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
            request with { Mode = string.IsNullOrWhiteSpace(request.Mode) ? "project_plan" : request.Mode },
            context,
            cancellationToken);
        return Results.Ok(new
        {
            module = "066",
            feature = CelarAiCapabilityCatalog.ProjectFlowHivePlan,
            status = result.Status,
            result = result.ToPublicResponse(),
            reviewRequired = true,
            scheduleEngineValidationRequired = true,
            planSaved = false,
            planBaselined = false,
            customerDateCommitted = false,
            stateChanged = false
        });
    }

    private static async Task<IResult> GenerateSowAsync(
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
            request with { Mode = "sow_draft" },
            context,
            cancellationToken);
        return Results.Ok(new
        {
            module = "025",
            feature = CelarAiCapabilityCatalog.SowGsdPlanning,
            status = result.Status,
            result = result.ToPublicResponse(),
            reviewRequired = true,
            contractuallyBinding = false,
            sowPublished = false,
            approvedSowOverwritten = false,
            stateChanged = false
        });
    }

    private static async Task<IResult> GenerateCloseoutCommunicationAsync(
        CelarAiCloseoutCommunicationRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiCapabilityRouter router,
        CancellationToken cancellationToken)
    {
        var identity = Identities(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk) return Forbidden(PulseAiSystemIntelligencePolicy.AskPermission);
        var correlationId = CorrelationId(context);
        var projectCode = Clean(request.ProjectCode, 120);
        var projectName = Clean(request.ProjectName, 300);
        var prompt = $"""
            Prepare a review-only {Clean(request.Audience, 80, "internal")} project closeout communication.
            Project code: {projectCode}
            Project name: {projectName}
            Completion summary: {Clean(request.CompletionSummary, 6000)}
            Acceptance evidence: {Clean(request.AcceptanceEvidence, 6000)}
            Outstanding items: {Clean(request.OutstandingItems, 6000)}
            Operational handoff: {Clean(request.HandoffSummary, 6000)}
            Requested tone: {Clean(request.RequestedTone, 80, "professional and factual")}

            Separate completed facts, evidence, open items, risks, ownership, and next actions. Do not invent customer
            acceptance, billing completion, deliverable completion, dates, recipients, or commitments. Return an unsent
            draft only. The owning closeout and notification modules retain final authority.
            """;
        var routed = await router.GenerateAsync(
            new ProjectPulseAiGenerationRequest(
                CelarAiCapabilityCatalog.CloseoutCommunication,
                "Create concise, factual, professional-services closeout communication drafts. Never send a message or claim approval without evidence.",
                prompt,
                1800,
                0.15),
            new CelarAiCapabilityExecutionContext(
                CelarAiCapabilityCatalog.CloseoutCommunication,
                ContainsPrivateDocuments: true,
                ContainsCustomerIdentity: true,
                ContainsPeopleRecords: false,
                ContainsFinancialValues: false,
                AllowSanitizedExternalAssistance: request.AllowSanitizedExternalFallback,
                SensitiveTerms: [projectCode, projectName, "US Signal", "Pulse"],
                ConsumerModule: "040/055C",
                CorrelationId: correlationId),
            () => BuildCloseoutFallback(request),
            cancellationToken);
        return Results.Ok(new
        {
            module = "040/055C",
            feature = CelarAiCapabilityCatalog.CloseoutCommunication,
            status = routed.Outcome == ProjectPulseAiOutcomes.Refusal
                ? "closeout_draft_refused"
                : "closeout_draft_completed",
            draft = routed.Content,
            selectedTarget = routed.Provider,
            attemptedTargets = routed.AttemptedProviders,
            skippedTargets = routed.SkippedProviders,
            warning = routed.Warning,
            correlationId,
            reviewRequired = true,
            emailSent = false,
            projectClosed = false,
            billingChanged = false,
            stateChanged = false
        });
    }

    private static string BuildCloseoutFallback(CelarAiCloseoutCommunicationRequest request) => $"""
        Subject: Project closeout review — {Clean(request.ProjectName, 300, Clean(request.ProjectCode, 120, "Project"))}

        This draft is prepared for review only. Summarize the verified completion status, accepted deliverables, supporting
        evidence, operational handoff, outstanding items, owners, risks, and next actions. Confirm all dates, customer
        acceptance, billing status, recipients, and commitments in the authoritative Pulse records before sending.
        """.Trim();

    private static string PrivateEndpointPolicy(CelarAiPrivateModelProfile profile)
    {
        if (!profile.EndpointConfigured) return "private_endpoint_not_configured";
        return PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
            profile.Endpoint,
            profile.PrivateHostAllowlist,
            out _,
            out var reason)
            ? "private_endpoint_approved"
            : $"private_endpoint_rejected_{reason}";
    }

    private static async Task<IResult?> AuthorizeAdministratorAsync(
        HttpContext context,
        bool requireSameOrigin,
        CancellationToken cancellationToken)
    {
        var actual = ActualSessionUserId(context);
        if (actual is null) return SessionRequired();
        var effective = EffectiveSessionUserId(context) ?? actual;
        var isViewAs = effective != actual
            || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is bool active && active);
        if (isViewAs)
            return Results.Json(new
            {
                status = "view_as_read_only",
                message = "Module 064 configuration cannot be changed or inspected through Administrator View-As."
            }, statusCode: StatusCodes.Status403Forbidden);
        if (requireSameOrigin && !SameOrigin(context))
            return Results.Json(new { status = "origin_rejected", message = "The request origin is not allowed." }, statusCode: StatusCodes.Status403Forbidden);
        var connectionString = ConnectionString();
        if (connectionString is null)
            return Results.Json(new { status = "configuration_unavailable", message = "Administrator authorization could not be verified." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            const string sql = """
                SELECT COALESCE(string_agg(DISTINCT r.role_code, ','), '')
                FROM app_users u
                LEFT JOIN app_user_role_assignments ura
                    ON ura.user_id = u.user_id AND ura.is_active = TRUE
                LEFT JOIN app_roles r
                    ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                WHERE u.user_id = @user_id AND u.is_active = TRUE;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("user_id", actual.Value);
            var roles = ((await command.ExecuteScalarAsync(cancellationToken))?.ToString() ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roles.Any(AdministratorRoles.Contains)) return null;
        }
        catch (Exception exception)
        {
            Log(context).LogWarning(exception, "Module 064 could not verify administrator authority.");
            return Results.Json(new { status = "authorization_unavailable", message = "Administrator authorization could not be verified." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        return Results.Json(new { status = "access_denied", message = "AI Provider Configuration Center is restricted to administrators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    private static (Guid Actual, Guid Effective)? Identities(HttpContext context)
    {
        var effective = EffectiveSessionUserId(context);
        if (effective is null) return null;
        return (ActualSessionUserId(context) ?? effective.Value, effective.Value);
    }

    private static Guid? ActualSessionUserId(HttpContext context) =>
        UserId(context, "ProjectPulseActualUserId")
        ?? UserId(context, "ProjectPulseSessionUserId");

    private static Guid? EffectiveSessionUserId(HttpContext context) =>
        UserId(context, "ProjectPulseEffectiveUserId")
        ?? UserId(context, "ProjectPulseSessionUserId");

    private static Guid? UserId(HttpContext context, string key)
    {
        if (!context.Items.TryGetValue(key, out var value)) return null;
        if (value is Guid id) return id;
        return Guid.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    private static bool SameOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("https" or "http")) return false;
        if (string.Equals(context.Request.Headers["Sec-Fetch-Site"].ToString(), "same-origin", StringComparison.OrdinalIgnoreCase)) return true;
        var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var publicHost = !string.IsNullOrWhiteSpace(forwardedHost)
            ? HostString.FromUriComponent(forwardedHost)
            : context.Request.Host;
        return string.Equals(uri.Host, publicHost.Host, StringComparison.OrdinalIgnoreCase)
            && (publicHost.Port is null || uri.Port == publicHost.Port);
    }

    private static string DisplayTarget(string target) => target switch
    {
        CelarAiCapabilityTargets.CelarAi => "Celar AI",
        CelarAiCapabilityTargets.Claude => "Claude",
        CelarAiCapabilityTargets.OpenAi => "OpenAI",
        _ => "Governed local template"
    };

    private static string CorrelationId(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Correlation-ID", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()[..Math.Min(value.ToString().Length, 160)]
            : context.TraceIdentifier;

    private static ILogger Log(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CelarAiCapabilityRoutingModule");

    private static string Clean(string? value, int maximum, string fallback = "")
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) return fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static IResult SessionRequired() => Results.Json(new
    {
        status = "session_required",
        message = "A valid Pulse session is required."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden(string permission) => Results.Json(new
    {
        status = "forbidden",
        requiredPermission = permission,
        message = "The current effective user is not authorized for this Celar AI operation."
    }, statusCode: StatusCodes.Status403Forbidden);

    private static string? ConnectionString() => new[]
        {
            "ConnectionStrings__DefaultConnection",
            "ConnectionStrings__ProjectPulse",
            "ConnectionStrings__ProjectTime",
            "PROJECTPULSE_CONNECTION_STRING",
            "PROJECTTIME_DATABASE_CONNECTION",
            "PROJECTPULSE_DB_CONNECTION",
            "PROJECTTIME_DB_CONNECTION"
        }
        .Select(Environment.GetEnvironmentVariable)
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record CelarAiCloseoutCommunicationRequest(
    string? ProjectCode,
    string? ProjectName,
    string? Audience,
    string? CompletionSummary,
    string? AcceptanceEvidence,
    string? OutstandingItems,
    string? HandoffSummary,
    string? RequestedTone,
    bool AllowSanitizedExternalFallback = false);
