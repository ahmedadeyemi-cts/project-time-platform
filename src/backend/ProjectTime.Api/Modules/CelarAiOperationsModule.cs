using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public sealed record CelarAiMonitorActivationRequest(
    int ExpectedRevision,
    bool Enabled,
    string? Confirmation,
    string? Reason);

/// <summary>
/// Ask Celar AI is the sole user-facing entry point for operational diagnosis,
/// guided defect intake, defect evidence, monitor visibility, and protected-Test
/// failure simulation. Module 076 remains the durable system of record.
/// </summary>
public static partial class CelarAiProductionPlatformModule
{
    public static IEndpointRouteBuilder MapCelarAiOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/celar-ai/v1/operations");
        group.MapGet("/readiness",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)OperationsReadinessAsync);
        group.MapPost("/troubleshoot",
            (Func<CelarAiTroubleshootRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)TroubleshootAsync);
        group.MapPost("/defects/intake-sessions",
            (Func<CelarAiDefectIntakeCreateRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)CreateIntakeSessionAsync);
        group.MapGet("/defects/intake-sessions/{sessionId:guid}",
            (Func<Guid, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)GetIntakeSessionAsync);
        group.MapMethods("/defects/intake-sessions/{sessionId:guid}", [HttpMethods.Patch],
            (Func<Guid, CelarAiDefectIntakeUpdateRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)UpdateIntakeSessionAsync);
        group.MapPost("/defects/intake-sessions/{sessionId:guid}/submit",
            (Func<Guid, CelarAiDefectIntakeSubmitRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)SubmitIntakeSessionAsync);
        group.MapGet("/defects/matches",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, string?, string?, string?, string?, CancellationToken, Task<IResult>>)FindMatchingDefectsAsync);
        group.MapGet("/defects/{defectNumber}",
            (Func<string, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)GetDefectAsync);
        group.MapPost("/defects/{defectId:guid}/evidence",
            (Func<Guid, CelarAiDefectEvidenceRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)AddDefectEvidenceAsync);
        group.MapGet("/monitor-policies",
            (Func<HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)ListMonitorPoliciesAsync);
        group.MapPost("/monitor-policies/{policyCode}/automatic-defects",
            (Func<string, CelarAiMonitorActivationRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)SetAutomaticDefectsAsync);
        group.MapPost("/synthetic-failures",
            (Func<CelarAiSyntheticFailureRequest, HttpContext, PulseAiSystemIntelligenceService, CelarAiDefectOrchestrationService, CancellationToken, Task<IResult>>)RunSyntheticFailureAsync);
        return endpoints;
    }

    private static async Task<IResult> OperationsReadinessAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        var readiness = await operations.GetReadinessAsync(cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = "ask_celar_ai_operations",
            readiness,
            access = AccessResponse(access),
            stateChanged = false
        });
    }

    private static async Task<IResult> TroubleshootAsync(
        CelarAiTroubleshootRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        var question = CelarAiOperationsPolicy.Clean(request.Question, CelarAiOperationsPolicy.MaximumQuestionCharacters);
        if (question.Length < 4)
            return OperationsValidation("Enter a complete troubleshooting question.");
        var outcome = await operations.TroubleshootAsync(request with { Question = question }, cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            feature = "ask_celar_ai_troubleshooting",
            outcome,
            defectActions = new
            {
                openQuestionnaire = outcome.DefectIntakeRecommended,
                searchExistingDefects = outcome.ExistingDefectSearchRecommended,
                continueTroubleshooting = true,
                dismiss = true,
                durableSystemOfRecord = "Module 076"
            },
            access = AccessResponse(access),
            privacy = PrivacyResponse(),
            stateChanged = false
        });
    }

    private static async Task<IResult> CreateIntakeSessionAsync(
        CelarAiDefectIntakeCreateRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        if (access.Actual != access.Effective) return ViewAsOperationsForbidden();
        try
        {
            var session = await operations.CreateIntakeSessionAsync(
                access.Actual,
                access.Effective,
                request,
                cancellationToken);
            return Results.Ok(new
            {
                module = "011",
                feature = "ask_celar_ai_guided_defect_intake",
                status = "defect_questionnaire_started",
                session,
                defaultAssignee = new
                {
                    displayName = CelarAiOperationsPolicy.DefaultAssigneeName,
                    email = CelarAiOperationsPolicy.DefaultAssigneeEmailValue,
                    identityAuthority = "Module 062"
                },
                durableSystemOfRecord = "Module 076",
                userConfirmationRequired = true,
                stateChanged = true
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "start the guided defect questionnaire");
        }
    }

    private static async Task<IResult> GetIntakeSessionAsync(
        Guid sessionId,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        var session = await operations.GetIntakeSessionAsync(sessionId, access.Actual, cancellationToken);
        return session is null
            ? Results.NotFound(new { module = "011", status = "defect_intake_session_not_found" })
            : Results.Ok(new
            {
                module = "011",
                feature = "ask_celar_ai_guided_defect_intake",
                session,
                access = AccessResponse(access),
                stateChanged = false
            });
    }

    private static async Task<IResult> UpdateIntakeSessionAsync(
        Guid sessionId,
        CelarAiDefectIntakeUpdateRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        if (access.Actual != access.Effective) return ViewAsOperationsForbidden();
        try
        {
            var session = await operations.UpdateIntakeSessionAsync(
                sessionId,
                access.Actual,
                access.Effective,
                request,
                cancellationToken);
            return Results.Ok(new
            {
                module = "011",
                feature = "ask_celar_ai_guided_defect_intake",
                status = "defect_questionnaire_updated",
                session,
                stateChanged = true
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "update the guided defect questionnaire");
        }
    }

    private static async Task<IResult> SubmitIntakeSessionAsync(
        Guid sessionId,
        CelarAiDefectIntakeSubmitRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        if (access.Actual != access.Effective) return ViewAsOperationsForbidden();
        try
        {
            var defect = await operations.SubmitIntakeSessionAsync(
                sessionId,
                access.Actual,
                access.Effective,
                request,
                cancellationToken);
            return Results.Ok(new
            {
                module = "076",
                feature = "ask_celar_ai_defect_creation",
                status = "defect_created",
                defect,
                defaultAssigneeApplied = defect.Assignee.Email.Equals(
                    CelarAiOperationsPolicy.DefaultAssigneeEmailValue,
                    StringComparison.OrdinalIgnoreCase),
                durableSystemOfRecord = "Module 076",
                managerNotificationQueued = true,
                externalGitHubIssueCreated = false,
                stateChanged = true
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "create the Module 076 defect");
        }
    }

    private static async Task<IResult> FindMatchingDefectsAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        string? environment,
        string? affectedModule,
        string? componentCode,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        try
        {
            var defects = await operations.FindMatchingDefectsAsync(
                environment,
                affectedModule,
                componentCode,
                failureCode,
                cancellationToken);
            return Results.Ok(new
            {
                module = "076",
                feature = "ask_celar_ai_defect_match",
                count = defects.Count,
                defects,
                stateChanged = false
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "search matching defects");
        }
    }

    private static async Task<IResult> GetDefectAsync(
        string defectNumber,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        try
        {
            var defect = await operations.GetDefectAsync(
                defectNumber,
                access.Actual,
                CanViewAllDefects(access),
                cancellationToken);
            return defect is null
                ? Results.NotFound(new { module = "076", status = "defect_not_found_or_not_authorized" })
                : Results.Ok(new
                {
                    module = "076",
                    feature = "ask_celar_ai_defect_detail",
                    defect,
                    stateChanged = false
                });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "load the defect");
        }
    }

    private static async Task<IResult> AddDefectEvidenceAsync(
        Guid defectId,
        CelarAiDefectEvidenceRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        if (access.Actual != access.Effective) return ViewAsOperationsForbidden();
        try
        {
            await operations.AddEvidenceAsync(
                defectId,
                access.Actual,
                access.Effective,
                CanManageDefects(access),
                request,
                cancellationToken);
            return Results.Ok(new
            {
                module = "076",
                feature = "ask_celar_ai_add_defect_evidence",
                status = "sanitized_evidence_added",
                secretStored = false,
                rawPrivateContentStored = false,
                stateChanged = true
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "add defect evidence");
        }
    }

    private static async Task<IResult> ListMonitorPoliciesAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        try
        {
            var policies = await operations.ListMonitorPoliciesAsync(cancellationToken);
            return Results.Ok(new
            {
                module = "078",
                feature = "ask_celar_ai_monitor_policies",
                policies,
                canManage = CanManageMonitoring(access),
                automaticMonitoringEnabled = CelarAiOperationsPolicy.AutomaticMonitoringEnabled,
                stateChanged = false
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "load monitor policies");
        }
    }

    private static async Task<IResult> SetAutomaticDefectsAsync(
        string policyCode,
        CelarAiMonitorActivationRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        if (access.Actual != access.Effective) return ViewAsOperationsForbidden();
        if (!CanManageMonitoring(access)) return OperationsForbidden("MANAGE_DEFECTS or system administration authority is required.");
        if (!string.Equals(request.Confirmation, "ENABLE TEST AUTOMATIC DEFECTS", StringComparison.Ordinal)
            && request.Enabled)
            return OperationsValidation("Use the exact Test activation confirmation.");
        if (CelarAiOperationsPolicy.Clean(request.Reason, 1000).Length < 3)
            return OperationsValidation("Provide an attributable activation reason.");
        try
        {
            var policy = await operations.SetMachineCreationAsync(
                policyCode,
                request.ExpectedRevision,
                request.Enabled,
                access.Actual,
                cancellationToken);
            return Results.Ok(new
            {
                module = "078",
                feature = "ask_celar_ai_automatic_defect_policy",
                status = request.Enabled ? "test_automatic_defects_enabled" : "test_automatic_defects_disabled",
                policy,
                deploymentLevelMonitoringEnabled = CelarAiOperationsPolicy.AutomaticMonitoringEnabled,
                productionChanged = false,
                stateChanged = true
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "update automatic defect policy");
        }
    }

    private static async Task<IResult> RunSyntheticFailureAsync(
        CelarAiSyntheticFailureRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CelarAiDefectOrchestrationService operations,
        CancellationToken cancellationToken)
    {
        var access = await RequireAskAccessAsync(context, system, cancellationToken);
        if (access.Failure is not null) return access.Failure;
        if (access.Actual != access.Effective) return ViewAsOperationsForbidden();
        if (!CanManageMonitoring(access)) return OperationsForbidden("System administration authority is required for Test fault injection.");
        try
        {
            var result = await operations.RunSyntheticFailureAsync(request, cancellationToken);
            return Results.Ok(new
            {
                module = "011",
                feature = "ask_celar_ai_synthetic_failure_test",
                status = "synthetic_failure_evaluated",
                result,
                externalSystemChanged = false,
                productionChanged = false,
                stateChanged = true
            });
        }
        catch (Exception exception)
        {
            return OperationsFailure(exception, "run the Test-only synthetic failure");
        }
    }

    private static async Task<OperationsAccess> RequireAskAccessAsync(
        HttpContext context,
        PulseAiSystemIntelligenceService system,
        CancellationToken cancellationToken)
    {
        var identities = OperationIdentities(context);
        if (identities is null)
            return new OperationsAccess(Guid.Empty, Guid.Empty, null, Results.Unauthorized());
        var access = await system.LoadAccessAsync(identities.Value.Effective, cancellationToken);
        if (!access.IsActive || !access.CanAsk)
            return new OperationsAccess(
                identities.Value.Actual,
                identities.Value.Effective,
                access,
                OperationsForbidden(PulseAiSystemIntelligencePolicy.AskPermission));
        return new OperationsAccess(identities.Value.Actual, identities.Value.Effective, access, null);
    }

    private static (Guid Actual, Guid Effective)? OperationIdentities(HttpContext context)
    {
        var actual = OperationUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = OperationUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId");
        return actual.HasValue && effective.HasValue
            ? (actual.Value, effective.Value)
            : null;
    }

    private static Guid? OperationUserId(HttpContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static bool CanManageDefects(OperationsAccess access) =>
        access.Access?.IsSuperAdministrator == true
        || access.Access?.RoleCodes.Overlaps(new[]
        {
            "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "MANAGER",
            "ENGINEERING_MANAGER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
            "PROJECT_MANAGER", "PROJECT_MANAGEMENT", "PROJECT_TEAM_COORDINATOR",
            "SUPPORT_MANAGER", "RELEASE_MANAGER"
        }) == true
        || access.Access?.PermissionCodes.Overlaps(new[]
        {
            "MANAGE_DEFECTS", "VIEW_ALL_DEFECTS", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"
        }) == true;

    private static bool CanViewAllDefects(OperationsAccess access) =>
        CanManageDefects(access)
        || access.Access?.PermissionCodes.Contains("VIEW_ALL_DEFECTS") == true;

    private static bool CanManageMonitoring(OperationsAccess access) =>
        access.Access?.IsSuperAdministrator == true
        || access.Access?.RoleCodes.Overlaps(new[]
        {
            "ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "RELEASE_MANAGER",
            "SECURITY_ADMINISTRATOR"
        }) == true
        || access.Access?.PermissionCodes.Overlaps(new[]
        {
            "MANAGE_DEFECTS", "OBSERVABILITY.MANAGE", "SYSTEM_ADMINISTRATION", "MANAGE_ALL"
        }) == true;

    private static object AccessResponse(OperationsAccess access) => new
    {
        actualUserId = access.Actual,
        effectiveUserId = access.Effective,
        viewAsActive = access.Actual != access.Effective,
        canAsk = access.Access?.CanAsk == true,
        canManageDefects = CanManageDefects(access),
        canViewAllDefects = CanViewAllDefects(access),
        canManageMonitoring = CanManageMonitoring(access)
    };

    private static object PrivacyResponse() => new
    {
        rawPromptStoredInDefect = false,
        rawToolBodyStoredInDefect = false,
        secretStoredInDefect = false,
        privateDocumentStoredInDefect = false,
        embeddingVectorStoredInDefect = false,
        unrestrictedSqlAllowed = false,
        publicProviderReceivedDiagnosticEvidence = false
    };

    private static IResult ViewAsOperationsForbidden() => Results.Json(new
    {
        module = "011",
        status = "view_as_read_only",
        message = "Exit Administrator View-As before creating or changing a defect.",
        stateChanged = false
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult OperationsForbidden(string message) => Results.Json(new
    {
        module = "011",
        status = "operations_forbidden",
        message,
        stateChanged = false
    }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult OperationsValidation(string message) => Results.Json(new
    {
        module = "011",
        status = "operations_validation_failed",
        message,
        stateChanged = false
    }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult OperationsFailure(Exception exception, string operation)
    {
        var status = exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException when exception.Message.Contains("Migration 084", StringComparison.OrdinalIgnoreCase)
                => StatusCodes.Status503ServiceUnavailable,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status503ServiceUnavailable
        };
        return Results.Json(new
        {
            module = "011",
            status = "ask_celar_ai_operations_unavailable",
            operation,
            exceptionType = exception.GetType().Name,
            message = status == StatusCodes.Status503ServiceUnavailable
                ? "The governed operations service is temporarily unavailable. No unconfirmed change was made."
                : exception.Message,
            secretReturned = false,
            stateChanged = false
        }, statusCode: status);
    }

    private sealed record OperationsAccess(
        Guid Actual,
        Guid Effective,
        PulseAiSystemAccess? Access,
        IResult? Failure);
}
