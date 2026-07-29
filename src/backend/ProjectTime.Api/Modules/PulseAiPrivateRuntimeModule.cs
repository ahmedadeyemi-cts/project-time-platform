using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class PulseAiPrivateRuntimeModule
{
    public static IEndpointRouteBuilder MapPulseAiPrivateRuntimeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/pulse-ai/v1/documents/runtime/readiness",
            (Func<HttpContext, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)GetReadinessAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/documents/runtime/jobs",
            (Func<HttpContext, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)ListJobsAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/documents/{documentId:guid}/runtime-state",
            (Func<Guid, HttpContext, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)GetDocumentStateAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/documents/{documentId:guid}/processing-jobs",
            (Func<Guid, PulseAiQueueDocumentRequest, HttpContext, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)QueueAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/documents/runtime/jobs/{jobId:guid}/cancel",
            (Func<Guid, PulseAiCancelDocumentJobRequest, HttpContext, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)CancelAsync);
        endpoints.MapPost(
            "/api/pulse-ai/v1/documents/runtime/jobs/{jobId:guid}/retry",
            (Func<Guid, PulseAiRetryDocumentJobRequest, HttpContext, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)RetryAsync);
        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        HttpContext context,
        PulseAiPrivateDocumentRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();
        var access = await runtime.LoadAccessAsync(effectiveUserId.Value, cancellationToken);
        if (!access.IsActive || !access.CanViewRuntime) return Forbidden("VIEW_PULSE_AI_DOCUMENT_RUNTIME");
        var readiness = await runtime.GetReadinessAsync(cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = "pulse_ai_private_document_runtime",
            status = readiness.Status,
            contractVersion = PulseAiPrivateRuntimePolicy.ContractVersion,
            access = AccessEvidence(context, effectiveUserId.Value, access),
            readiness,
            lifecycle = new[]
            {
                "Queue an authorized document with explicit confirmation.",
                "Revalidate current document and project authorization before processing.",
                "Require a clean private malware-scan result.",
                "Extract natively and use only an approved private OCR endpoint when required.",
                "Create citation-preserving sections and deterministic chunks.",
                "Generate embeddings only through an endpoint accepted by the private-endpoint policy.",
                "Persist lexical and optional embedding index evidence in ProjectPulse PostgreSQL.",
                "Reauthorize every retrieval and revoke access without retraining a model."
            },
            externalProviderCalled = false,
            module064RouteChanged = false,
            azureChanged = false,
            deploymentPerformed = false
        });
    }

    private static async Task<IResult> ListJobsAsync(
        HttpContext context,
        PulseAiPrivateDocumentRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();
        var access = await runtime.LoadAccessAsync(effectiveUserId.Value, cancellationToken);
        if (!access.IsActive || !access.CanViewRuntime) return Forbidden("VIEW_PULSE_AI_DOCUMENT_RUNTIME");
        var status = Query(context, "status", 40);
        var limit = int.TryParse(context.Request.Query["limit"], out var parsed)
            ? Math.Clamp(parsed, 1, 500)
            : 100;
        var jobs = await runtime.ListJobsAsync(
            effectiveUserId.Value,
            status,
            limit,
            cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = "pulse_ai_private_document_jobs",
            status = "authorized_jobs_loaded",
            contractVersion = PulseAiPrivateRuntimePolicy.ContractVersion,
            access = AccessEvidence(context, effectiveUserId.Value, access),
            filters = new { status, limit },
            summary = new
            {
                jobCount = jobs.Count,
                queued = jobs.Count(job => job.Status == "queued"),
                running = jobs.Count(job => job.Status is "scanning" or "extracting" or "embedding" or "indexing"),
                waiting = jobs.Count(job => job.Status is "awaiting_ocr" or "retry_wait"),
                failed = jobs.Count(job => job.Status is "failed" or "quarantined"),
                succeeded = jobs.Count(job => job.Status == "succeeded")
            },
            jobs = jobs.Select(job => job.ToPublicEvidence()).ToArray(),
            privacy = new
            {
                storagePathsReturned = false,
                rawDocumentTextReturned = false,
                chunksReturned = false,
                embeddingsReturned = false,
                providerSecretsReturned = false
            },
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> GetDocumentStateAsync(
        Guid documentId,
        HttpContext context,
        PulseAiPrivateDocumentRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();
        var access = await runtime.LoadAccessAsync(effectiveUserId.Value, cancellationToken);
        if (!access.IsActive || !access.CanViewRuntime) return Forbidden("VIEW_PULSE_AI_DOCUMENT_RUNTIME");
        var state = await runtime.GetDocumentStateAsync(
            effectiveUserId.Value,
            documentId,
            cancellationToken);
        if (state is null)
        {
            return Results.Json(new
            {
                module = "011",
                status = "document_not_found_or_not_authorized",
                message = "The private document runtime state is unavailable in the current effective user's authorized scope."
            }, statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(new
        {
            module = "011",
            feature = "pulse_ai_private_document_runtime_state",
            status = state.ProcessingStatus,
            contractVersion = PulseAiPrivateRuntimePolicy.ContractVersion,
            access = AccessEvidence(context, effectiveUserId.Value, access),
            document = state.ToPublicEvidence(),
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> QueueAsync(
        Guid documentId,
        PulseAiQueueDocumentRequest request,
        HttpContext context,
        PulseAiPrivateDocumentRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        if (identities.Value.Actual != identities.Value.Effective) return ViewAsMutationBlocked();
        if (!string.Equals(
                request.Confirmation?.Trim(),
                PulseAiPrivateRuntimePolicy.QueueConfirmation,
                StringComparison.Ordinal))
        {
            return ConfirmationRequired(PulseAiPrivateRuntimePolicy.QueueConfirmation);
        }
        var access = await runtime.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!access.IsActive || !access.CanQueue) return Forbidden("QUEUE_PULSE_AI_DOCUMENT_PROCESSING");
        var job = await runtime.QueueAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            documentId,
            request,
            cancellationToken);
        if (job is null)
        {
            return Results.Json(new
            {
                module = "011",
                status = "document_not_authorized_or_job_already_active",
                message = "The document is outside the current user's scope, unavailable, or already has an active processing job."
            }, statusCode: StatusCodes.Status409Conflict);
        }
        return Results.Json(new
        {
            module = "011",
            feature = "pulse_ai_private_document_processing",
            status = "processing_job_queued",
            contractVersion = PulseAiPrivateRuntimePolicy.ContractVersion,
            access = AccessEvidence(context, identities.Value.Effective, access),
            job = job.ToPublicEvidence(),
            confirmationAccepted = true,
            workerExecutionDependsOnRuntimeConfiguration = true,
            externalProviderCalled = false,
            module064RouteChanged = false
        }, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> CancelAsync(
        Guid jobId,
        PulseAiCancelDocumentJobRequest request,
        HttpContext context,
        PulseAiPrivateDocumentRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        if (identities.Value.Actual != identities.Value.Effective) return ViewAsMutationBlocked();
        if (!string.Equals(
                request.Confirmation?.Trim(),
                PulseAiPrivateRuntimePolicy.CancelConfirmation,
                StringComparison.Ordinal))
        {
            return ConfirmationRequired(PulseAiPrivateRuntimePolicy.CancelConfirmation);
        }
        var access = await runtime.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!access.IsActive || !access.CanCancel) return Forbidden("CANCEL_PULSE_AI_DOCUMENT_PROCESSING");
        var updated = await runtime.CancelAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            jobId,
            request,
            cancellationToken);
        return updated
            ? Results.Ok(new
            {
                module = "011",
                status = "processing_cancellation_requested",
                jobId,
                confirmationAccepted = true,
                stateChanged = true,
                externalProviderCalled = false
            })
            : Results.Json(new
            {
                module = "011",
                status = "processing_job_not_cancellable",
                message = "The job was not found in the authorized scope or is no longer cancellable."
            }, statusCode: StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> RetryAsync(
        Guid jobId,
        PulseAiRetryDocumentJobRequest request,
        HttpContext context,
        PulseAiPrivateDocumentRuntimeService runtime,
        CancellationToken cancellationToken)
    {
        var identities = Identities(context);
        if (identities is null) return SessionRequired();
        if (identities.Value.Actual != identities.Value.Effective) return ViewAsMutationBlocked();
        if (!string.Equals(
                request.Confirmation?.Trim(),
                PulseAiPrivateRuntimePolicy.RetryConfirmation,
                StringComparison.Ordinal))
        {
            return ConfirmationRequired(PulseAiPrivateRuntimePolicy.RetryConfirmation);
        }
        var access = await runtime.LoadAccessAsync(identities.Value.Actual, cancellationToken);
        if (!access.IsActive || !access.CanRetry) return Forbidden("RETRY_PULSE_AI_DOCUMENT_PROCESSING");
        var updated = await runtime.RetryAsync(
            identities.Value.Actual,
            identities.Value.Effective,
            jobId,
            request,
            cancellationToken);
        return updated
            ? Results.Accepted(
                $"/api/pulse-ai/v1/documents/runtime/jobs?status=queued",
                new
                {
                    module = "011",
                    status = "processing_job_requeued",
                    jobId,
                    confirmationAccepted = true,
                    stateChanged = true,
                    externalProviderCalled = false
                })
            : Results.Json(new
            {
                module = "011",
                status = "processing_job_not_retryable",
                message = "The job was not found in the authorized scope, reached its attempt limit, or is not in a retryable state."
            }, statusCode: StatusCodes.Status409Conflict);
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
        Guid effectiveUserId,
        PulseAiPrivateDocumentRuntimeRepository.RuntimeAccess access)
    {
        var actualUserId = ActualUserId(context) ?? effectiveUserId;
        var isViewAs = actualUserId != effectiveUserId
            || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
                && value is bool active
                && active);
        return new
        {
            actualUserId,
            effectiveUserId,
            isViewAs,
            mode = isViewAs ? "administrator_read_only_view_as" : "current_user",
            roles = access.RoleCodes.OrderBy(value => value).ToArray(),
            permissions = access.PermissionCodes
                .Where(PulseAiPrivateRuntimePolicy.OperatorPermissions.Contains)
                .OrderBy(value => value)
                .ToArray(),
            mutationAuthorityTransferred = false,
            serverAuthorized = true
        };
    }

    private static string Query(HttpContext context, string key, int maximumLength)
    {
        var value = context.Request.Query[key].ToString().Trim().ToLowerInvariant();
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static IResult SessionRequired() =>
        Results.Json(new
        {
            module = "011",
            status = "session_required",
            message = "A valid ProjectPulse session is required."
        }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden(string permission) =>
        Results.Json(new
        {
            module = "011",
            status = "forbidden",
            requiredPermission = permission,
            message = "The current user is not authorized for this Pulse AI private runtime operation."
        }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsMutationBlocked() =>
        Results.Json(new
        {
            module = "011",
            status = "view_as_mutation_blocked",
            message = "Administrator View-As is read-only and cannot queue, retry, cancel, approve, or otherwise mutate Pulse AI processing."
        }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ConfirmationRequired(string requiredConfirmation) =>
        Results.BadRequest(new
        {
            module = "011",
            status = "confirmation_required",
            requiredConfirmation,
            message = "The exact confirmation value is required before this private document processing operation can change state."
        });
}
