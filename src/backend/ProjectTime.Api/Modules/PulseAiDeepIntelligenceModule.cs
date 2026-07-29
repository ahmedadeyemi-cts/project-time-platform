using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class PulseAiDeepIntelligenceModule
{
    public static IEndpointRouteBuilder MapPulseAiDeepIntelligenceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/pulse-ai/v1/overview",
            (Func<HttpContext, PulseAiQuestionPlanner, IResult>)GetOverview);
        endpoints.MapGet(
            "/api/pulse-ai/v1/private-runtime/readiness",
            (Func<HttpContext, PulseAiDocumentGroundingService, CancellationToken, Task<IResult>>)GetReadinessAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/tools",
            (Func<HttpContext, PulseAiQuestionPlanner, IResult>)GetTools);
        endpoints.MapGet(
            "/api/pulse-ai/v1/timesheet/context-preview",
            (Func<HttpContext, PulseAiDocumentGroundingService, CancellationToken, Task<IResult>>)GetTimesheetContextAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/help-search/plan",
            (Func<HttpContext, PulseAiQuestionPlanner, IResult>)GetHelpSearchPlan);
        endpoints.MapGet(
            "/api/pulse-ai/v1/flowhive/context-preview",
            (Func<HttpContext, PulseAiDocumentGroundingService, CancellationToken, Task<IResult>>)GetFlowHiveContextAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/insights/plan",
            (Func<HttpContext, PulseAiQuestionPlanner, IResult>)GetInsightPlan);
        endpoints.MapPost(
            "/api/pulse-ai/v1/external-escalation/sanitize-preview",
            (Func<PulseAiSanitizationRequest, HttpContext, PulseAiEscalationSanitizer, IResult>)SanitizeEscalationPreview);

        return endpoints;
    }

    private static IResult GetOverview(
        HttpContext context,
        PulseAiQuestionPlanner planner)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();

        return Results.Ok(new
        {
            module = PulseAiIntelligencePolicy.ModuleNumber,
            moduleName = PulseAiIntelligencePolicy.ModuleName,
            contractVersion = "pulse-ai-deep-intelligence-v1-20260728",
            policyVersion = PulseAiIntelligencePolicy.PolicyVersion,
            status = "deep_intelligence_source_runtime_registered_read_only",
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessEvidence(context, effectiveUserId.Value),
            mission = new
            {
                detailLevel = "extremely_detailed_comprehensive_source_grounded",
                useCases = PulseAiIntelligencePolicy.UseCases,
                privateReasoningPath = PulseAiIntelligencePolicy.PrivateReasoningPath,
                requiredPrivatePlatformServices = PulseAiIntelligencePolicy.RequiredPrivatePlatformServices,
                nonNegotiableControls = PulseAiIntelligencePolicy.NonNegotiableControls
            },
            currentCapabilities = new[]
            {
                "Permission-aware project and document metadata resolution.",
                "Private consumption of existing approved AI document-context summaries.",
                "SOW/GSD evidence, readiness, coverage, conflict, and missing-input analysis.",
                "Detailed product Help answers and multi-domain question planning.",
                "Governed reporting and financial semantic query planning.",
                "Sanitized external-reasoning capsule preview with no provider call.",
                "Private document-grounded deterministic timesheet suggestion integration."
            },
            stillGated = new[]
            {
                "Original PDF/DOCX extraction and OCR worker registration.",
                "Private embedding execution and permission-scoped vector indexing.",
                "Private open-weight inference endpoint execution.",
                "Automatic multi-tool Help/Search answer execution across every module.",
                "FlowHive private model execution and controlled plan persistence.",
                "Runtime consumption of the Group 3 financial truth contract until PR #220 is merged and integrated.",
                "External Claude/OpenAI sanitized escalation execution.",
                "Training, model promotion, deployment, and production feature-route mutation."
            },
            toolCount = planner.GetToolRegistry().Count,
            stateChanged = false,
            databaseChanged = false,
            externalProviderCalled = false,
            deploymentPerformed = false
        });
    }

    private static async Task<IResult> GetReadinessAsync(
        HttpContext context,
        PulseAiDocumentGroundingService grounding,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();
        var readiness = await grounding.GetReadinessAsync(effectiveUserId.Value, cancellationToken);
        return Results.Ok(new
        {
            module = "011",
            status = readiness.Status,
            readiness,
            access = AccessEvidence(context, effectiveUserId.Value),
            interpretation = new
            {
                documentMetadataReady = readiness.DocumentTableAvailable
                    && readiness.EngineeringVisibilityAvailable,
                privateSummaryGroundingReady = readiness.ContextSummaryAvailable
                    && readiness.AuthorizedReadyContextDocumentCount > 0,
                semanticRetrievalReady = readiness.PrivateEmbeddingEndpointConfigured
                    && readiness.PrivateVectorIndexConfigured,
                privateModelReasoningReady = readiness.PrivateInferenceEndpointConfigured,
                externalEscalationReady = false,
                reason = "External execution is never authorized by readiness alone. It requires a separately approved sanitized-capsule policy and Module 064 route."
            },
            stateChanged = false
        });
    }

    private static IResult GetTools(
        HttpContext context,
        PulseAiQuestionPlanner planner)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();
        var tools = planner.GetToolRegistry();
        return Results.Ok(new
        {
            module = "011",
            status = "governed_read_tool_registry_loaded",
            generatedAt = DateTimeOffset.UtcNow,
            access = AccessEvidence(context, effectiveUserId.Value),
            toolCount = tools.Count,
            tools,
            rules = new[]
            {
                "A listed tool is not automatically callable by every user; the owning module and record scope remain authoritative.",
                "Pulse AI receives sanitized results from approved read-only tools and never receives unrestricted database credentials.",
                "Unknown, stale, unavailable, or optional values remain explicit and are never silently replaced with zero or a model estimate.",
                "Tool execution cannot mutate ProjectPulse state unless a separate, explicit, confirmed action contract is implemented and authorized."
            },
            stateChanged = false
        });
    }

    private static async Task<IResult> GetTimesheetContextAsync(
        HttpContext context,
        PulseAiDocumentGroundingService grounding,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();

        var input = new PulseAiTimesheetGroundingInput(
            WorkDate: DateOnly.TryParse(context.Request.Query["workDate"], out var workDate) ? workDate : null,
            TimeType: Query(context, "timeType", 40),
            RowType: Query(context, "rowType", 80),
            RowLabel: Query(context, "rowLabel", 300),
            ProjectCode: Query(context, "projectCode", 100),
            ProjectName: Query(context, "projectName", 255),
            TaskCode: Query(context, "taskCode", 100),
            TaskName: Query(context, "taskName", 255),
            CurrentDescription: Query(context, "currentDescription", 2000));

        if (string.IsNullOrWhiteSpace(input.ProjectCode)
            && string.IsNullOrWhiteSpace(input.ProjectName))
        {
            return Results.BadRequest(new
            {
                module = "011",
                status = "project_context_required",
                message = "Project code or project name is required for a permission-aware timesheet grounding preview."
            });
        }

        var result = await grounding.BuildTimesheetContextAsync(
            effectiveUserId.Value,
            input,
            cancellationToken);

        return Results.Ok(new
        {
            module = "011",
            feature = "timesheet_document_grounding",
            status = result.Status,
            input = new
            {
                input.WorkDate,
                input.TimeType,
                input.RowType,
                input.RowLabel,
                input.ProjectCode,
                input.ProjectName,
                input.TaskCode,
                input.TaskName,
                roughNotePresent = !string.IsNullOrWhiteSpace(input.CurrentDescription)
            },
            grounding = result.ToPublicEvidence(),
            sourcePrecedence = new[]
            {
                "Engineer rough note",
                "Selected canonical task or service/resource request",
                "Current assignment and project scope",
                "Approved SOW",
                "Approved GSD",
                "Other authorized engineering-visible documents"
            },
            outputRules = new[]
            {
                "The suggestion may improve terminology and scope alignment but may not claim work the Engineer did not report.",
                "Raw document text and context summaries are not returned by this endpoint.",
                "When ready private document context exists, the current source uses a private deterministic grounded suggestion and does not send that context to Claude or OpenAI.",
                "The Engineer must review and explicitly apply the suggestion; Pulse AI cannot save or submit time."
            },
            stateChanged = false,
            externalProviderCalled = false
        });
    }

    private static IResult GetHelpSearchPlan(
        HttpContext context,
        PulseAiQuestionPlanner planner)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();

        var question = Query(context, "question", 4000);
        if (string.IsNullOrWhiteSpace(question))
        {
            return Results.BadRequest(new
            {
                module = "011",
                status = "question_required",
                message = "Ask a ProjectPulse product, workflow, project, operational, reporting, or financial question."
            });
        }

        var plan = planner.PlanHelpSearch(question);
        var registry = planner.GetToolRegistry();
        var selectedTools = registry
            .Where(tool => plan.RequiredTools.Contains(tool.Code, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return Results.Ok(new
        {
            module = "011",
            feature = "system_help_search",
            status = plan.Status,
            access = AccessEvidence(context, effectiveUserId.Value),
            plan,
            selectedTools,
            answerContract = new
            {
                minimumSections = plan.AnswerSections,
                mustInclude = new[]
                {
                    "A direct conclusion that answers the question.",
                    "Detailed supporting analysis rather than a surface summary.",
                    "Source modules, documents, tools, record counts, and filters.",
                    "Data-as-of time, calculation definitions, and source freshness.",
                    "Assumptions, contradictions, missing inputs, uncertainty, and material limitations.",
                    "Actionable next steps and direct ProjectPulse navigation targets when available."
                },
                unsupportedClaimPolicy = "State that authorized evidence was insufficient; never create a confident unsupported answer.",
                mutationPolicy = "Read-only plan. No ProjectPulse state is changed."
            },
            runtimeExecution = new
            {
                directProductKnowledgeAvailable = plan.DirectKnowledgeAnswer is not null,
                automaticMultiToolExecutionEnabled = false,
                reason = "This phase registers the detailed planner and answer contract. Each live tool adapter must be integrated and evaluated without bypassing its owning module."
            },
            stateChanged = false,
            externalProviderCalled = false
        });
    }

    private static async Task<IResult> GetFlowHiveContextAsync(
        HttpContext context,
        PulseAiDocumentGroundingService grounding,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();

        var input = new PulseAiFlowHiveGroundingInput(
            ProjectCode: Query(context, "projectCode", 100),
            ProjectName: Query(context, "projectName", 255),
            RequestedOutcome: Query(context, "requestedOutcome", 2000));

        if (string.IsNullOrWhiteSpace(input.ProjectCode)
            && string.IsNullOrWhiteSpace(input.ProjectName))
        {
            return Results.BadRequest(new
            {
                module = "011",
                status = "project_context_required",
                message = "Project code or project name is required for a FlowHive grounding preview."
            });
        }

        var result = await grounding.BuildFlowHiveContextAsync(
            effectiveUserId.Value,
            input,
            cancellationToken);

        return Results.Ok(new
        {
            module = "011",
            consumerModule = "066",
            feature = "flowhive_document_planning",
            status = result.Status,
            grounding = result.ToPublicEvidence(),
            proposedStructuredOutput = new
            {
                project = new[] { "identity", "customer", "objectives", "constraints", "target dates" },
                workBreakdownStructure = new[] { "wbs", "task", "description", "duration", "source citation", "assumption state" },
                dependencies = new[] { "predecessor", "successor", "type", "lead or lag", "rationale", "source" },
                milestones = new[] { "name", "decision or deliverable", "proposed date range", "acceptance evidence" },
                resources = new[] { "required role", "skill", "estimated effort", "capacity conflict", "named person only after authorization" },
                governance = new[] { "assumptions", "risks", "mitigations", "out-of-scope items", "open questions", "conflicts" }
            },
            planningProcess = new[]
            {
                "Resolve authorized document versions and source precedence.",
                "Extract deliverables, scope, exclusions, responsibilities, prerequisites, quantities, locations, acceptance criteria, constraints, and change-control requirements.",
                "Prepare a cited WBS draft and clearly label every inference or default as an assumption.",
                "Send the structured task and dependency model—not raw customer documents—to FlowHive's deterministic schedule engine.",
                "Apply working days, holidays, dependency types, lead or lag, critical path, float, and capacity evidence.",
                "Present a comprehensive draft, source coverage, conflicts, missing inputs, risks, and timeline ranges to the Project Manager.",
                "Require Engineering modification and technical validation before any separately authorized baseline approval."
            },
            restrictions = new[]
            {
                "No raw SOW, GSD, architecture, order, contract, customer, or pricing content is sent to Claude or OpenAI.",
                "No plan is stored, baselined, assigned, customer-published, or used to reserve capacity by this preview.",
                "A private model endpoint, private retrieval index, and approved FlowHive adapter remain required for complete model-assisted generation."
            },
            stateChanged = false,
            externalProviderCalled = false
        });
    }

    private static IResult GetInsightPlan(
        HttpContext context,
        PulseAiQuestionPlanner planner)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();

        var question = Query(context, "question", 4000);
        if (string.IsNullOrWhiteSpace(question))
        {
            return Results.BadRequest(new
            {
                module = "011",
                status = "question_required",
                message = "Ask a reporting, operational, financial, commercial, utilization, capacity, project, or portfolio question."
            });
        }

        var plan = planner.PlanInsight(question);
        return Results.Ok(new
        {
            module = "011",
            feature = "reporting_and_financial_insight",
            status = plan.Status,
            access = AccessEvidence(context, effectiveUserId.Value),
            plan,
            financialTruthDependency = new
            {
                sourcePr = 220,
                contractRoutes = new[]
                {
                    "/api/project-financials/portfolio",
                    "/api/project-financials/reporting-summary",
                    "/api/project-financials/projects/{projectId}",
                    "/api/project-financials/sources"
                },
                runtimeConsumption = "not_registered_in_this_dependent_branch",
                rule = "Pulse AI will consume the authoritative Group 3 contract after it is independently reviewed and integrated. It will not duplicate or estimate those calculations."
            },
            responseRequirements = new[]
            {
                "Provide an executive conclusion and a detailed analytical explanation.",
                "Show formula, calculation definition, reporting period, currency, filters, workspace, and contract version.",
                "Show source health, known values, unknown values, stale values, optional-source gaps, and record counts.",
                "Explain the largest drivers, exceptions, trend direction, risks, and recommended follow-up.",
                "Never treat missing financial data as zero unless the authoritative calculation contract explicitly defines it that way.",
                "Never change a rate, contract, expense, billing status, invoice, reconciliation, opportunity, or accounting record."
            },
            stateChanged = false,
            externalProviderCalled = false
        });
    }

    private static IResult SanitizeEscalationPreview(
        PulseAiSanitizationRequest request,
        HttpContext context,
        PulseAiEscalationSanitizer sanitizer)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();
        var result = sanitizer.Sanitize(request);
        return Results.Ok(new
        {
            module = "011",
            feature = "sanitized_external_reasoning_capsule",
            status = result.Status,
            access = AccessEvidence(context, effectiveUserId.Value),
            result,
            enforcement = new
            {
                previewOnly = true,
                externalExecutionAuthorized = false,
                providerCalled = false,
                module064RouteChanged = false,
                rawDocumentReturned = false,
                rawDocumentSent = false,
                stateChanged = false
            }
        });
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

    private static object AccessEvidence(HttpContext context, Guid effectiveUserId)
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
            message = "A valid ProjectPulse session is required."
        }, statusCode: StatusCodes.Status401Unauthorized);
}
