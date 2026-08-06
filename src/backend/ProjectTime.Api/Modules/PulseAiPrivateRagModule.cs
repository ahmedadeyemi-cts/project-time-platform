using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class PulseAiPrivateRagModule
{
    public static IEndpointRouteBuilder MapPulseAiPrivateRagEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/celar-ai/v1/rag/readiness",
            (Func<HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)GetReadinessAsync);
        endpoints.MapPost(
            "/api/celar-ai/v1/rag/help-search",
            (Func<PulseAiPrivateHelpSearchRequest, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)AskHelpSearchAsync);
        endpoints.MapPost(
            "/api/celar-ai/v1/rag/timesheet-suggestion",
            (Func<PulseAiPrivateTimesheetRequest, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)GenerateTimesheetAsync);
        endpoints.MapPost(
            "/api/celar-ai/v1/rag/flowhive-plan",
            (Func<PulseAiPrivateFlowHiveRequest, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)GenerateFlowHiveAsync);
        endpoints.MapGet(
            "/api/celar-ai/v1/rag/answers/{answerRunId:guid}",
            (Func<Guid, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)GetAnswerAuditAsync);
        endpoints.MapPost(
            "/api/celar-ai/v1/rag/answers/{answerRunId:guid}/feedback",
            (Func<Guid, PulseAiPrivateFeedbackRequest, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)SaveFeedbackAsync);

        // Transport-only compatibility routes for already deployed callers.
        endpoints.MapGet(
            "/api/pulse-ai/v1/rag/readiness",
            (Func<HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)GetReadinessAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/rag/help-search",
            (Func<PulseAiPrivateHelpSearchRequest, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)AskHelpSearchAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/rag/timesheet-suggestion",
            (Func<PulseAiPrivateTimesheetRequest, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)GenerateTimesheetAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/rag/flowhive-plan",
            (Func<PulseAiPrivateFlowHiveRequest, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)GenerateFlowHiveAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/rag/answers/{answerRunId:guid}",
            (Func<Guid, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)GetAnswerAuditAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/rag/answers/{answerRunId:guid}/feedback",
            (Func<Guid, PulseAiPrivateFeedbackRequest, HttpContext, PulseAiPrivateRagService, CancellationToken, Task<IResult>>)SaveFeedbackAsync);
        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        HttpContext context,
        PulseAiPrivateRagService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !(access.CanHelpSearch || access.CanTimesheet || access.CanFlowHive || access.CanViewAudit))
        {
            return Forbidden("Module 011 private RAG capability");
        }
        return Results.Ok(new
        {
            module = "011",
            feature = "pulse_ai_private_rag_orchestration",
            access = AccessEvidence(context, identities.Value, access),
            readiness = await service.GetReadinessAsync(cancellationToken),
            consumers = new[]
            {
                new { feature = PulseAiPrivateRagPolicy.TimesheetFeature, module = "001", humanReview = "Engineer must review and explicitly apply the suggestion." },
                new { feature = PulseAiPrivateRagPolicy.HelpSearchFeature, module = "011 / Global Help", humanReview = "Answer displays citations, uncertainty, and source freshness." },
                new { feature = PulseAiPrivateRagPolicy.FlowHiveFeature, module = "066", humanReview = "Project Manager and Engineering must modify and validate the draft before baseline." }
            },
            externalProviderCalled = false,
            module064RouteChanged = false,
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> AskHelpSearchAsync(
        PulseAiPrivateHelpSearchRequest request,
        HttpContext context,
        PulseAiPrivateRagService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanHelpSearch) return Forbidden("ASK_PULSE_AI_HELP_SEARCH");
        var hasAttachments = (request.AttachmentIds ?? []).Any(value => value != Guid.Empty);
        if (hasAttachments && identities.Value.Actual != identities.Value.Effective)
        {
            return Results.Json(new
            {
                module = "011",
                status = "view_as_attachment_access_blocked",
                message = "Celar AI conversation attachments are unavailable in View-As.",
                mutationAuthorityTransferred = false
            }, statusCode: StatusCodes.Status403Forbidden);
        }
        if (hasAttachments && !access.CanAttachDocuments)
            return Forbidden(CelarAiConversationAttachmentPolicy.Permission);
        var answer = await service.AskHelpSearchAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = PulseAiPrivateRagPolicy.HelpSearchFeature,
            access = AccessEvidence(context, identities.Value, access),
            result = answer.ToPublicResponse(),
            answerQuality = new
            {
                depth = "extremely_detailed_comprehensive_source_grounded",
                sourceCitationsRequired = true,
                unsupportedClaimPolicy = "State that current authorized evidence is insufficient; do not fabricate.",
                liveRecordPolicy = "Live record status requires a governed read tool or current private source evidence."
            },
            externalProviderCalled = false,
            stateChanged = false
        });
    }

    private static async Task<IResult> GenerateTimesheetAsync(
        PulseAiPrivateTimesheetRequest request,
        HttpContext context,
        PulseAiPrivateRagService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanTimesheet) return Forbidden("USE_PULSE_AI_TIMESHEET_GROUNDING");
        var answer = await service.GenerateTimesheetAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            consumerModule = "001",
            feature = PulseAiPrivateRagPolicy.TimesheetFeature,
            access = AccessEvidence(context, identities.Value, access),
            result = answer.ToPublicResponse(),
            proposedDescription = answer.Answer?.DirectConclusion ?? string.Empty,
            controls = new
            {
                engineerReviewRequired = true,
                hoursChanged = false,
                workDateChanged = false,
                timeTypeChanged = false,
                projectChanged = false,
                taskChanged = false,
                requestChanged = false,
                saved = false,
                submitted = false,
                approved = false
            },
            externalProviderCalled = false,
            stateChanged = false
        });
    }

    private static async Task<IResult> GenerateFlowHiveAsync(
        PulseAiPrivateFlowHiveRequest request,
        HttpContext context,
        PulseAiPrivateRagService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanFlowHive) return Forbidden("USE_PULSE_AI_FLOWHIVE_PLANNING");
        var answer = await service.GenerateFlowHivePlanAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            consumerModule = "066",
            feature = PulseAiPrivateRagPolicy.FlowHiveFeature,
            access = AccessEvidence(context, identities.Value, access),
            result = answer.ToPublicResponse(),
            controls = new
            {
                draftOnly = true,
                deterministicSchedulingStillRequired = true,
                projectManagerReviewRequired = true,
                engineeringModificationRequired = true,
                baselineCreated = false,
                resourcesAssigned = false,
                capacityReserved = false,
                customerPublished = false,
                committedDateChanged = false
            },
            externalProviderCalled = false,
            stateChanged = false
        });
    }

    private static async Task<IResult> GetAnswerAuditAsync(
        Guid answerRunId,
        HttpContext context,
        PulseAiPrivateRagService service,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        var access = await service.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanViewAudit) return Forbidden("VIEW_PULSE_AI_ANSWER_AUDIT");
        var audit = await service.GetAnswerAuditAsync(
            answerRunId,
            identities.Value.Effective,
            cancellationToken);
        return audit is null
            ? Results.Json(new
            {
                module = "011",
                status = "answer_not_found_or_not_authorized",
                message = "The answer audit is not available in the current effective user's scope."
            }, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(new
            {
                module = "011",
                feature = "pulse_ai_answer_audit",
                access = AccessEvidence(context, identities.Value, access),
                audit,
                generatedAt = DateTimeOffset.UtcNow
            });
    }

    private static async Task<IResult> SaveFeedbackAsync(
        Guid answerRunId,
        PulseAiPrivateFeedbackRequest request,
        HttpContext context,
        PulseAiPrivateRagService service,
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
                message = "Answer feedback and training-evidence mutations are disabled on the exact-source release candidate.",
                configurationSourceCommit = release.ConfigurationSourceCommit,
                stateChanged = false
            }, statusCode: StatusCodes.Status423Locked);
        }
        var access = await service.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!access.IsActive || !access.CanSubmitFeedback) return Forbidden("SUBMIT_PULSE_AI_FEEDBACK");
        var saved = await service.SaveFeedbackAsync(
            answerRunId,
            identities.Value.Actual,
            identities.Value.Effective,
            request,
            cancellationToken);
        return saved
            ? Results.Ok(new
            {
                module = "011",
                status = "feedback_recorded",
                answerRunId,
                trainingCandidateCreated = false,
                trainingReviewRequired = true,
                stateChanged = true
            })
            : Results.Json(new
            {
                module = "011",
                status = "feedback_not_recorded",
                message = "The answer was not found in the user's scope or the feedback type was invalid."
            }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static (Guid Actual, Guid Effective)? Identities(HttpContext context)
    {
        var effective = EffectiveUserId(context);
        if (effective is null) return null;
        var actual = ActualUserId(context) ?? effective.Value;
        return (actual, effective.Value);
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
        PulseAiPrivateRagAccess access)
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
                .Where(permission => permission.StartsWith("ASK_PULSE_AI", StringComparison.OrdinalIgnoreCase)
                    || permission.StartsWith("USE_PULSE_AI", StringComparison.OrdinalIgnoreCase)
                    || permission.StartsWith("VIEW_PULSE_AI", StringComparison.OrdinalIgnoreCase)
                    || permission.StartsWith("SUBMIT_PULSE_AI", StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value)
                .ToArray(),
            mutationAuthorityTransferred = false,
            serverAuthorized = true
        };
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
            message = "The current effective user is not authorized for this private Celar AI operation."
        }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsMutationBlocked() =>
        Results.Json(new
        {
            module = "011",
            status = "view_as_mutation_blocked",
            message = "Administrator View-As is read-only and cannot submit Celar AI feedback or create training evidence."
        }, statusCode: StatusCodes.Status403Forbidden);
}
