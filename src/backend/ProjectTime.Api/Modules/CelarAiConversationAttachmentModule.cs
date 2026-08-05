using Microsoft.AspNetCore.Mvc;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class CelarAiConversationAttachmentModule
{
    private const long MaximumMultipartBodyBytes =
        CelarAiConversationAttachmentPolicy.MaximumActiveBytesPerConversation + (2L * 1024L * 1024L);

    public static IEndpointRouteBuilder MapCelarAiConversationAttachmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/celar-ai/v2/attachments/readiness",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiConversationAttachmentService, CancellationToken, Task<IResult>>)ReadinessAsync);
        var upload = endpoints.MapPost(
            "/api/celar-ai/v2/conversations/{conversationId:guid}/attachments",
            (Func<Guid, HttpContext, PulseAiSystemIntelligenceService, CelarAiConversationAttachmentService, CancellationToken, Task<IResult>>)UploadAsync);
        upload.WithMetadata(new RequestSizeLimitAttribute(MaximumMultipartBodyBytes));
        upload.WithMetadata(new RequestFormLimitsAttribute
        {
            MultipartBodyLengthLimit = MaximumMultipartBodyBytes,
            ValueLengthLimit = 16 * 1024,
            KeyLengthLimit = 256
        });
        endpoints.MapGet(
            "/api/celar-ai/v2/conversations/{conversationId:guid}/attachments",
            (Func<Guid, HttpContext, PulseAiSystemIntelligenceService, CelarAiConversationAttachmentService, CancellationToken, Task<IResult>>)ListAsync);
        endpoints.MapDelete(
            "/api/celar-ai/v2/conversations/{conversationId:guid}/attachments/{attachmentId:guid}",
            (Func<Guid, Guid, HttpContext, PulseAiSystemIntelligenceService, CelarAiConversationAttachmentService, CancellationToken, Task<IResult>>)RevokeAsync);
        return endpoints;
    }

    private static async Task<IResult> ReadinessAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiConversationAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        var identity = Identity(context);
        if (identity is null) return SessionRequired();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!CanAttach(access)) return Forbidden();
        return Results.Ok(new
        {
            module = "011",
            feature = CelarAiCapabilityCatalog.HelpAssistant,
            attachmentReadiness = await attachments.GetReadinessAsync(cancellationToken),
            access = Access(identity.Value),
            stateChanged = false
        });
    }

    private static async Task<IResult> UploadAsync(
        Guid conversationId,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiConversationAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        var identity = Identity(context);
        if (identity is null) return SessionRequired();
        if (identity.Value.Actual != identity.Value.Effective) return ViewAsForbidden();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!CanAttach(access)) return Forbidden();
        if (!context.Request.HasFormContentType)
        {
            return Results.Json(new
            {
                module = "011",
                status = "validation_failed",
                message = "Celar AI document attachments must be uploaded as multipart/form-data."
            }, statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Results.Json(new
            {
                module = "011",
                status = "validation_failed",
                message = "The attachment request exceeds the governed multipart upload limits."
            }, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var result = await attachments.UploadAsync(
            identity.Value.Actual,
            conversationId,
            form.Files,
            cancellationToken);
        var response = new
        {
            module = "011",
            feature = CelarAiCapabilityCatalog.HelpAssistant,
            status = result.Status,
            attachments = result.Attachments.Select(item => item.ToPublicResponse()).ToArray(),
            blockers = result.Blockers,
            access = Access(identity.Value),
            privacy = new
            {
                processingBoundary = PulseAiPrivateRuntimePolicy.PrivacyBoundary,
                rawDocumentTextReturned = false,
                rawDocumentSentToClaudeOrOpenAi = false,
                conversationOwnerScopeRequired = true
            },
            stateChanged = result.Attachments.Count > 0
        };
        if (result.Attachments.Count > 0)
            return Results.Json(response, statusCode: StatusCodes.Status202Accepted);
        var unavailable = result.Blockers.Any(value =>
            value.Contains("not ready", StringComparison.OrdinalIgnoreCase)
            || value.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            || value.Contains("required", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Migration", StringComparison.OrdinalIgnoreCase));
        return Results.Json(
            response,
            statusCode: unavailable
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> ListAsync(
        Guid conversationId,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiConversationAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        var identity = Identity(context);
        if (identity is null) return SessionRequired();
        if (identity.Value.Actual != identity.Value.Effective) return ViewAsForbidden();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!CanAttach(access)) return Forbidden();
        var rows = await attachments.ListAsync(identity.Value.Effective, conversationId, cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = CelarAiCapabilityCatalog.HelpAssistant,
            status = "celar_ai_chat_attachments_loaded",
            attachments = rows.Select(item => item.ToPublicResponse()).ToArray(),
            access = Access(identity.Value),
            rawDocumentTextReturned = false,
            stateChanged = false
        });
    }

    private static async Task<IResult> RevokeAsync(
        Guid conversationId,
        Guid attachmentId,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiConversationAttachmentService attachments,
        CancellationToken cancellationToken)
    {
        var identity = Identity(context);
        if (identity is null) return SessionRequired();
        if (identity.Value.Actual != identity.Value.Effective) return ViewAsForbidden();
        var access = await system.LoadAccessAsync(identity.Value.Effective, cancellationToken);
        if (!CanAttach(access)) return Forbidden();
        var revoked = await attachments.RevokeAsync(
            identity.Value.Actual,
            conversationId,
            attachmentId,
            cancellationToken);
        if (!revoked)
        {
            return Results.Json(new
            {
                module = "011",
                status = "attachment_not_found_or_not_authorized",
                message = "The attachment was not found in the current user's active conversation scope."
            }, statusCode: StatusCodes.Status404NotFound);
        }
        return Results.Ok(new
        {
            module = "011",
            feature = CelarAiCapabilityCatalog.HelpAssistant,
            status = "celar_ai_chat_attachment_revoked",
            attachmentId,
            retrievalEligible = false,
            access = Access(identity.Value),
            stateChanged = true
        });
    }

    private static bool CanAttach(PulseAiSystemAccess access) =>
        access.IsActive
        && access.CanAsk
        && (access.IsSuperAdministrator
            || access.PermissionCodes.Contains(CelarAiConversationAttachmentPolicy.Permission));

    private static (Guid Actual, Guid Effective)? Identity(HttpContext context)
    {
        var actual = context.Items.TryGetValue("ProjectPulseActualUserId", out var actualValue)
            && actualValue is Guid actualUserId
            ? actualUserId
            : context.Items.TryGetValue("ProjectPulseSessionUserId", out var sessionValue)
                && sessionValue is Guid sessionUserId
                ? sessionUserId
                : (Guid?)null;
        var effective = context.Items.TryGetValue("ProjectPulseEffectiveUserId", out var effectiveValue)
            && effectiveValue is Guid effectiveUserId
            ? effectiveUserId
            : actual;
        return actual is null || effective is null ? null : (actual.Value, effective.Value);
    }

    private static object Access((Guid Actual, Guid Effective) identity) => new
    {
        actualUserId = identity.Actual,
        effectiveUserId = identity.Effective,
        isViewAs = identity.Actual != identity.Effective,
        mutationAuthorityTransferred = false,
        conversationOwnerScopeRequired = true
    };

    private static IResult SessionRequired() => Results.Json(new
    {
        module = "011",
        status = "session_required",
        message = "Sign in before using Celar AI chat attachments."
    }, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden() => Results.Json(new
    {
        module = "011",
        status = "forbidden",
        requiredPermission = CelarAiConversationAttachmentPolicy.Permission
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ViewAsForbidden() => Results.Json(new
    {
        module = "011",
        status = "view_as_attachment_access_blocked",
        message = "Celar AI conversation attachments are unavailable in View-As. Return to the actual session to protect private conversation documents.",
        mutationAuthorityTransferred = false
    }, statusCode: StatusCodes.Status403Forbidden);
}
