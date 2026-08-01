using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Provides two narrowly governed Celar AI capabilities:
/// 1. current, permission-scoped work/activity explanations using only owning
///    Pulse read APIs; and
/// 2. stable, source-controlled procedures for common Pulse workflows.
///
/// Historical conversation messages are deliberately not used as answer input.
/// A conversation is retained for the current user, but a newly started chat
/// begins with no prior conversational context.
/// </summary>
public sealed partial class CelarAiPeopleAndGuidanceService
{
    public const string ContractVersion = "celar-ai-people-guidance-v1-20260801";
    public const string ContextPolicyVersion = "celar-ai-current-thread-context-v1-20260801";

    private readonly PulseAiSystemToolExecutor _toolExecutor;
    private readonly PulseAiSystemIntelligenceRepository _repository;
    private readonly ILogger<CelarAiPeopleAndGuidanceService> _logger;

    public CelarAiPeopleAndGuidanceService(
        PulseAiSystemToolExecutor toolExecutor,
        PulseAiSystemIntelligenceRepository repository,
        ILogger<CelarAiPeopleAndGuidanceService> logger)
    {
        _toolExecutor = toolExecutor;
        _repository = repository;
        _logger = logger;
    }

    public static object ContextPolicy() => new
    {
        status = "celar_ai_context_policy_ready",
        contractVersion = ContextPolicyVersion,
        defaultNewChatBehavior = "fresh_thread_without_prior_conversation_messages",
        currentAnswerContext = "current_question_current_thread_and_authorized_live_evidence_only",
        historicalConversationRetention = "retained_per_effective_user_when_migration_054_is_available",
        historicalConversationUse = "available_only_when_the_user_explicitly_reopens_that_conversation",
        previousConversationAutoInjection = false,
        crossUserConversationAccess = false,
        viewAsMutationAuthorityTransferred = false,
        conversationContentBecomesTrainingDataAutomatically = false,
        guarantees = new[]
        {
            "Opening Celar AI starts a fresh visible chat instead of automatically reopening the most recent conversation.",
            "Selecting a historical conversation displays that conversation for review and continuation; it does not merge unrelated conversations.",
            "A new conversation does not inherit messages, assumptions, answers, filters, project selections, or tool results from another conversation.",
            "Historical conversations remain user-scoped and are not supplied to another user or to a public external model.",
            "Live project, people, work, financial, document, API, and operational answers are recalculated from currently authorized sources."
        },
        generatedAt = DateTimeOffset.UtcNow
    };

    public static object GuidanceCatalog() => new
    {
        status = "celar_ai_guidance_catalog_ready",
        contractVersion = ContractVersion,
        count = Guides.Length,
        guides = Guides.Select(guide => new
        {
            guide.Code,
            guide.Title,
            guide.ModuleCode,
            guide.Route,
            guide.Summary,
            guide.RequiredAccess,
            guide.Safeguards
        }).ToArray(),
        generatedAt = DateTimeOffset.UtcNow
    };

    public static object PeopleActivityReadiness() => new
    {
        status = "celar_ai_people_activity_ready",
        contractVersion = ContractVersion,
        interpretation = new
        {
            assignedWork = "A current assignment or resource request recorded in an authorized Pulse source.",
            recordedWork = "A submitted or saved work record exposed by an authorized owning module.",
            plannedWork = "A forecast, schedule, task, or capacity record; it is not proof of real-time activity.",
            realTimePresence = "Not inferred unless an owning source explicitly provides current presence evidence."
        },
        sourceBoundaries = PeopleTools.Select(tool => new
        {
            tool.Code,
            tool.Name,
            tool.ModuleCode,
            tool.Path,
            tool.Purpose
        }).ToArray(),
        guarantees = new[]
        {
            "Owning APIs enforce the current effective user's project, team, role, and record scope.",
            "Celar AI does not use conversation history as evidence of what a person is currently doing.",
            "Celar AI distinguishes assigned, planned, submitted, approved, and observed work from real-time presence.",
            "No arbitrary URL, arbitrary SQL, personnel surveillance, or cross-user conversation search is enabled."
        },
        generatedAt = DateTimeOffset.UtcNow
    };

    public static bool IsPeopleActivityQuestion(string? question)
    {
        var value = question?.Trim() ?? string.Empty;
        return value.Length > 0 && PeopleActivityRegex().IsMatch(value);
    }

    public static bool IsHowToQuestion(string? question)
    {
        var value = question?.Trim() ?? string.Empty;
        return value.Length > 0 && HowToRegex().IsMatch(value);
    }

    public async Task<PulseAiSystemQuestionResult?> TryAnswerAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        PulseAiSystemQuestionRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceOptions options,
        CancellationToken cancellationToken = default)
    {
        var question = Clean(request.Question, options.MaximumQuestionCharacters);
        if (question.Length == 0) return null;

        var guide = IsHowToQuestion(question) ? FindGuide(question) : null;
        if (guide is not null)
        {
            return await CreateGuidanceAnswerAsync(
                actualUserId,
                effectiveUserId,
                access,
                request with { Question = question },
                guide,
                context,
                cancellationToken);
        }

        if (!IsPeopleActivityQuestion(question)) return null;

        return await CreatePeopleActivityAnswerAsync(
            actualUserId,
            effectiveUserId,
            access,
            request with { Question = question },
            context,
            options,
            cancellationToken);
    }

    private async Task<PulseAiSystemQuestionResult> CreatePeopleActivityAnswerAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        PulseAiSystemQuestionRequest request,
        HttpContext context,
        PulseAiSystemIntelligenceOptions options,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId(context);
        var detailLevel = DetailLevel(request.DetailLevel);
        var persistence = await BeginPersistenceAsync(
            actualUserId,
            effectiveUserId,
            access,
            request,
            "people_activity",
            detailLevel,
            correlationId,
            cancellationToken);

        try
        {
            var tools = PeopleTools.Take(Math.Clamp(request.MaximumTools ?? 6, 1, 6)).ToArray();
            var results = await _toolExecutor.ExecuteAsync(context, tools, options, cancellationToken);
            if (persistence.Persisted)
            {
                foreach (var result in results)
                {
                    await _repository.SaveToolEventAsync(
                        persistence.InquiryRunId,
                        result,
                        options.PersistToolResponseBodies,
                        cancellationToken);
                }
            }

            var succeeded = results.Where(result => result.Succeeded).ToArray();
            var unavailable = results.Where(result => !result.Succeeded).ToArray();
            var evidence = succeeded
                .SelectMany(result => SummarizePeopleEvidence(result, 18))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(60)
                .ToArray();
            var sourceEvidence = results.Select((result, index) => new PulseAiSystemSourceEvidence(
                SourceId: index + 1,
                SourceType: "governed_live_tool",
                SourceCode: result.ToolCode,
                SourceName: result.ToolName,
                ModuleCode: result.ModuleCode,
                Method: result.Method,
                Path: result.Path,
                Status: result.Status,
                StatusCode: result.StatusCode,
                ObservedAt: result.ObservedAt,
                Freshness: "current_request",
                EvidenceScope: "Owning API authorization and current effective-user scope"))
                .ToArray();

            var direct = evidence.Length > 0
                ? $"Celar AI found {evidence.Length} current authorized work, assignment, capacity, approval, or planning observation(s) from {succeeded.Length} live Pulse source(s). These records describe assigned, planned, submitted, or recorded work; they do not prove a person's real-time physical activity."
                : "Celar AI could not find sufficient currently authorized people-and-work evidence for this question. It did not infer activity from prior chats, names, titles, or unverified assumptions.";

            var answer = new PulseAiSystemDetailedAnswer(
                DirectConclusion: direct,
                ExecutiveSummary:
                    "The answer is rebuilt from the current effective user's authorized Pulse scope. Celar AI checks project assignments, open work, capacity and pipeline planning, approvals, and FlowHive/project records where those owning APIs are available. Historical conversation text is retained separately and is not treated as evidence of current activity.",
                ScopeAndFilters:
                [
                    $"Effective user: {effectiveUserId}; actual user: {actualUserId}; View-As: {(actualUserId == effectiveUserId ? "no" : "yes, read-only evidence scope") }.",
                    $"Question scope: {Clean(request.Question, 1_000)}",
                    "Data scope: only records returned by current same-origin owning APIs after their own authorization checks.",
                    "Interpretation: assignment, workload, submitted time, approval, schedule, resource request, and forecast records are reported by their recorded state."
                ],
                CurrentState:
                [
                    $"Governed people/work tools selected: {results.Count}; succeeded: {succeeded.Length}; unavailable or unauthorized: {unavailable.Length}.",
                    $"Evidence observations prepared: {evidence.Length}.",
                    "Previous Celar AI conversations were not used to decide what anyone is currently doing.",
                    "Real-time presence, productivity scoring, keystroke monitoring, and inferred employee surveillance are not enabled."
                ],
                DetailedAnalysis: evidence.Length > 0
                    ? evidence
                    : ["No current assignment, workload, approval, capacity, or planning record was returned within the user's authorized scope. Ask with a specific person, team, project, or time period, or confirm that the owning module contains current records."],
                ApiFindings: results.Select(result =>
                    $"{result.Method} {result.Path} — Module {result.ModuleCode}; {result.Status}; HTTP {result.StatusCode}; observed {result.ObservedAt:O}.").ToArray(),
                TroubleshootingFindings: unavailable.Select(result =>
                    $"{result.ToolName} was not available for this answer: {result.Status}, HTTP {result.StatusCode}, diagnostic {Blank(result.DiagnosticCode, "not recorded")}.").ToArray(),
                RootCauseHypotheses: [],
                DiagnosticSteps:
                [
                    "Specify a person, team, project, customer, date range, or work status when a narrower answer is required.",
                    "Open Project Workspace (Module 019) to verify assignments, resource requests, and documents in the same authorized scope.",
                    "Open Capacity & Pipeline Forecasting (Module 070) to validate future workload and utilization calculations.",
                    "Open Approval Inbox (Module 002) to validate submitted or pending time evidence where the role is authorized.",
                    "Do not treat an assignment or forecast as proof that work occurred; confirm saved/submitted time, task status, or another owning record."
                ],
                SourceEvidence: sourceEvidence.Select(source =>
                    $"Source {source.SourceId}: {source.SourceName}; Module {source.ModuleCode}; {source.Method} {source.Path}; status {source.Status}; observed {source.ObservedAt:O}.").ToArray(),
                KnownUnknownAndStaleValues:
                [
                    "Known: only the authorized records returned by the owning Pulse APIs during this request.",
                    "Unknown: off-platform activity, informal work, unsaved time, private communications, and real-time presence not represented in an authorized source.",
                    "Unavailable and unauthorized records remain excluded and are never converted into a negative conclusion about a person.",
                    "Historical conversation content may be retained for the user, but it is not automatically inserted into this answer."
                ],
                Assumptions:
                [
                    "Owning modules contain current records and their server-side authorization is operating correctly.",
                    "The phrase 'what people are doing' refers to recorded assignments, planned workload, work status, and authorized operational evidence rather than surveillance."
                ],
                Conflicts: [],
                Limitations:
                [
                    "A successful API response proves that the source answered; it does not prove every person has entered all work or updated every task.",
                    "Celar AI does not expose people, projects, time, or workload outside the current effective user's authorized scope.",
                    "Celar AI does not claim real-time activity from assignment or capacity records alone."
                ],
                RisksAndImplications:
                [
                    "Using stale assignments as current-work evidence can produce incorrect staffing conclusions; verify data-as-of timestamps and task/time status.",
                    "Broad employee comparisons can create privacy and management risk; use authorized role scope and a legitimate delivery purpose."
                ],
                RecommendedActions:
                [
                    "Use a specific person/team/project and time period for the next question when you need a focused workload explanation.",
                    "Review the cited owning module before making staffing, performance, billing, or customer commitments.",
                    "Correct the source record in its owning module rather than teaching Celar AI an exception through conversation history."
                ],
                FutureEnhancementBlueprint: null,
                NavigationTargets:
                [
                    "#project-workspace",
                    "#project-workload",
                    "#capacity-pipeline-forecast",
                    "#manager-approval",
                    "#project-flowhive"
                ],
                CitationIds: sourceEvidence.Select(source => source.SourceId).ToArray(),
                Confidence: evidence.Length > 0 ? 0.86m : 0.42m,
                ConfidenceExplanation: evidence.Length > 0
                    ? "Confidence is based on current authorized owning-API evidence, with explicit limits on real-time interpretation."
                    : "Confidence is limited because no current authorized work evidence was returned; Celar AI avoided inferring activity from historical conversation text.",
                DataAsOf: DateTimeOffset.UtcNow);

            return await FinishAsync(
                persistence,
                answer,
                results,
                sourceEvidence,
                "people_activity",
                detailLevel,
                correlationId,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception,
                "Celar AI people activity request failed without logging question or response bodies. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            return await FailureAsync(
                persistence,
                "people_activity",
                detailLevel,
                correlationId,
                Diagnostic(exception),
                cancellationToken);
        }
    }

    private async Task<PulseAiSystemQuestionResult> CreateGuidanceAnswerAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        PulseAiSystemQuestionRequest request,
        HowToGuide guide,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId(context);
        var detailLevel = DetailLevel(request.DetailLevel);
        var persistence = await BeginPersistenceAsync(
            actualUserId,
            effectiveUserId,
            access,
            request,
            "product_help",
            detailLevel,
            correlationId,
            cancellationToken);
        var dataAsOf = DateTimeOffset.UtcNow;
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: $"{guide.Title}: {guide.Summary}",
            ExecutiveSummary:
                "This procedure is generated from the current source-controlled Pulse operating guide. It does not use a previous chat as the authority and does not claim that a live record was changed.",
            ScopeAndFilters:
            [
                $"Module: {guide.ModuleCode} — {guide.ModuleName}.",
                $"Navigation: {guide.Route}.",
                $"Question: {Clean(request.Question, 1_000)}",
                "Procedure type: stable platform guidance; current record values were not required."
            ],
            CurrentState:
            [
                "The procedure is available as operating guidance in the current Celar AI source package.",
                "The user's actual access is still determined by the target module when the page or API is opened.",
                "A new chat begins without prior conversation messages; explicitly reopened history remains available for the same user."
            ],
            DetailedAnalysis: guide.Steps.Select((step, index) => $"Step {index + 1}: {step}").ToArray(),
            ApiFindings: guide.ApiEvidence,
            TroubleshootingFindings:
            [
                "If the page is hidden or returns HTTP 403, verify the effective role and module/action permission in Modules 012 and 037.",
                "If the page opens but current records are missing, verify the owning module's project, customer, team, assignment, or record scope.",
                "If the workflow fails, capture the route, timestamp, role, environment, message, and correlation ID for Celar AI troubleshooting or Module 076 defect intake."
            ],
            RootCauseHypotheses: [],
            DiagnosticSteps:
            [
                $"Open {guide.Route} and confirm the page title identifies Module {guide.ModuleCode}.",
                "Confirm the current effective user and View-As state before comparing permissions or records.",
                "Use Module 999 for the full operating guide and Module 076 if the documented procedure and deployed behavior differ."
            ],
            SourceEvidence: guide.Sources,
            KnownUnknownAndStaleValues:
            [
                "Known: the current source-controlled workflow, navigation target, permission boundary, and safeguards.",
                "Unknown until the target page is opened: the user's exact current records, pending items, source health, and configuration state.",
                "Historical chat text is not treated as a substitute for the current target module or user guide."
            ],
            Assumptions:
            [
                "The deployed Test or Production release contains the same source contract as the answer's current release.",
                "The user has a legitimate business purpose and the target module will enforce role and record scope."
            ],
            Conflicts: [],
            Limitations:
            [
                "This answer explains the workflow but does not perform, save, approve, submit, deploy, or otherwise mutate it.",
                "A target module may require additional fields or approvals based on the selected record and role."
            ],
            RisksAndImplications: guide.Safeguards,
            RecommendedActions:
            [
                $"Open {guide.Route} and follow the numbered procedure.",
                "Use a new Celar AI question with the exact error, module, role, and record when the documented procedure does not match the observed behavior.",
                "Correct source documentation when the platform workflow changes; do not rely on an old conversation as the permanent procedure."
            ],
            FutureEnhancementBlueprint: null,
            NavigationTargets: [guide.Route, "#user-guide", "#roles-permissions-matrix", "#defect-tracker"],
            CitationIds: [],
            Confidence: 0.97m,
            ConfidenceExplanation: "High confidence because the answer matches a source-controlled Celar AI workflow guide and explicitly defers live authorization and record state to the owning module.",
            DataAsOf: dataAsOf);

        return await FinishAsync(
            persistence,
            answer,
            [],
            [],
            "product_help",
            detailLevel,
            correlationId,
            cancellationToken,
            modelProvider: "celar_ai_operating_knowledge",
            modelName: $"Celar AI procedure catalog {ContractVersion}");
    }

    private async Task<PersistenceContext> BeginPersistenceAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        PulseAiSystemQuestionRequest request,
        string intentCode,
        string detailLevel,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var mayPersist = actualUserId == effectiveUserId && access.CanViewConversations;
        var conversation = mayPersist
            ? await _repository.EnsureConversationAsync(
                request.ConversationId,
                actualUserId,
                effectiveUserId,
                request.Mode ?? "system_help",
                cancellationToken)
            : null;
        var conversationId = conversation?.ConversationId
            ?? request.ConversationId
            ?? Guid.NewGuid();
        var userMessageId = Guid.NewGuid();
        if (conversation is not null)
        {
            var saved = await _repository.AppendMessageAsync(
                conversationId,
                effectiveUserId,
                "user",
                "completed",
                request.Question ?? string.Empty,
                new
                {
                    contextPolicy = ContextPolicyVersion,
                    previousConversationMessagesInjected = false,
                    detailLevel,
                    intentCode
                },
                null,
                null,
                correlationId,
                string.Empty,
                string.Empty,
                [],
                new { },
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (saved.MessageId != Guid.Empty) userMessageId = saved.MessageId;
        }

        var inquiryRunId = conversation is not null
            ? await _repository.CreateInquiryRunAsync(
                conversationId,
                userMessageId,
                actualUserId,
                effectiveUserId,
                intentCode,
                detailLevel,
                Sha256(request.Question ?? string.Empty),
                correlationId,
                cancellationToken)
            : Guid.NewGuid();
        return new PersistenceContext(
            conversationId,
            userMessageId,
            inquiryRunId,
            conversation is not null);
    }

    private async Task<PulseAiSystemQuestionResult> FinishAsync(
        PersistenceContext persistence,
        PulseAiSystemDetailedAnswer answer,
        IReadOnlyList<PulseAiSystemToolResult> tools,
        IReadOnlyList<PulseAiSystemSourceEvidence> sources,
        string intentCode,
        string detailLevel,
        string correlationId,
        CancellationToken cancellationToken,
        string modelProvider = "celar_ai_deterministic_people_intelligence",
        string modelName = "Celar AI authorized people/work synthesis v1")
    {
        var status = answer.Confidence >= 0.55m ? "completed" : "partial";
        var assistantId = Guid.NewGuid();
        if (persistence.Persisted)
        {
            var structured = new
            {
                status,
                intentCode,
                detailLevel,
                answer,
                sources,
                relevantApis = Array.Empty<object>(),
                toolResults = tools.Select(result => result.ToPublicEvidence()).ToArray(),
                modelProvider,
                modelName,
                correlationId,
                warnings = Array.Empty<string>(),
                contextPolicy = new
                {
                    previousConversationMessagesInjected = false,
                    historicalConversationRetained = true
                }
            };
            var saved = await _repository.AppendMessageAsync(
                persistence.ConversationId,
                Guid.Empty,
                "assistant",
                status,
                answer.DirectConclusion,
                structured,
                persistence.InquiryRunId,
                null,
                correlationId,
                modelProvider,
                modelName,
                tools.Select(tool => tool.ToolCode).ToArray(),
                new
                {
                    totalSources = sources.Count,
                    successfulTools = tools.Count(tool => tool.Succeeded),
                    failedTools = tools.Count(tool => !tool.Succeeded),
                    previousConversationMessagesInjected = false
                },
                answer.DataAsOf,
                cancellationToken);
            if (saved.MessageId != Guid.Empty) assistantId = saved.MessageId;
            await _repository.CompleteInquiryRunAsync(
                persistence.InquiryRunId,
                assistantId,
                status,
                [],
                tools,
                0,
                answer.Confidence,
                string.Empty,
                cancellationToken);
        }

        return new PulseAiSystemQuestionResult(
            persistence.ConversationId,
            persistence.UserMessageId,
            assistantId,
            persistence.InquiryRunId,
            status,
            intentCode,
            detailLevel,
            answer,
            sources,
            [],
            tools,
            modelProvider,
            modelName,
            correlationId,
            [],
            persistence.Persisted);
    }

    private async Task<PulseAiSystemQuestionResult> FailureAsync(
        PersistenceContext persistence,
        string intentCode,
        string detailLevel,
        string correlationId,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var answer = new PulseAiSystemDetailedAnswer(
            "Celar AI could not complete the people or platform-guidance request from authorized current evidence.",
            "The request failed closed without using previous conversations as evidence or exposing restricted source data.",
            [], [], [], [],
            ["Use the correlation ID with Modules 013, 016, 076, 078, or 998."],
            [],
            ["Retry the request with a narrower person, team, project, date range, or platform task."],
            [],
            ["Current evidence is unavailable; no negative conclusion about a person or workflow was inferred."],
            [], [],
            ["The owning API or persistence service may be unavailable."],
            ["Do not make staffing or operational decisions from this incomplete result."],
            ["Retry after checking runtime readiness and the displayed correlation ID."],
            null,
            ["#service-control", "#defect-tracker", "#observability-slo-health", "#system-diagnostics"],
            [], 0.2m,
            $"Low confidence because the governed request failed ({diagnostic}).",
            now);
        return await FinishAsync(
            persistence,
            answer,
            [],
            [],
            intentCode,
            detailLevel,
            correlationId,
            cancellationToken,
            modelProvider: string.Empty,
            modelName: string.Empty);
    }

    private static IReadOnlyList<string> SummarizePeopleEvidence(
        PulseAiSystemToolResult result,
        int maximum)
    {
        if (string.IsNullOrWhiteSpace(result.ResponseJson)) return result.EvidenceSummary;
        try
        {
            using var document = JsonDocument.Parse(result.ResponseJson);
            var rows = new List<string>();
            Visit(document.RootElement, rows, maximum);
            if (rows.Count == 0) rows.AddRange(result.EvidenceSummary);
            return rows.Take(maximum).ToArray();
        }
        catch
        {
            return result.EvidenceSummary;
        }
    }

    private static void Visit(JsonElement element, List<string> rows, int maximum)
    {
        if (rows.Count >= maximum) return;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Visit(item, rows, maximum);
                if (rows.Count >= maximum) return;
            }
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;

        var values = new List<string>();
        Add(values, element, "displayName", "person");
        Add(values, element, "engineerDisplayName", "engineer");
        Add(values, element, "userDisplayName", "person");
        Add(values, element, "projectCode", "project");
        Add(values, element, "projectName", "project name");
        Add(values, element, "taskCode", "task");
        Add(values, element, "taskName", "task name");
        Add(values, element, "requestType", "request");
        Add(values, element, "requestedRole", "role");
        Add(values, element, "status", "status");
        Add(values, element, "assignedHours", "assigned hours");
        Add(values, element, "usedHours", "used hours");
        Add(values, element, "remainingHours", "remaining hours");
        Add(values, element, "utilizationPercent", "utilization");
        Add(values, element, "startDate", "start");
        Add(values, element, "endDate", "end");
        Add(values, element, "weekStart", "week");
        if (values.Count >= 2)
            rows.Add($"{result.ToolName}: {string.Join("; ", values)}.");

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                Visit(property.Value, rows, maximum);
            if (rows.Count >= maximum) return;
        }
    }

    private static void Add(List<string> values, JsonElement element, string property, string label)
    {
        if (!element.TryGetProperty(property, out var value)) return;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(text)) values.Add($"{label}: {Clean(text, 180)}");
    }

    private static HowToGuide? FindGuide(string question)
    {
        var normalized = question.ToLowerInvariant();
        return Guides
            .Select(guide => new
            {
                Guide = guide,
                Score = guide.Keywords.Count(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            })
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Guide.Code)
            .Select(match => match.Guide)
            .FirstOrDefault();
    }

    private static string DetailLevel(string? value) =>
        PulseAiSystemIntelligencePolicy.DetailLevels.Contains(value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? value!.ToLowerInvariant()
            : "comprehensive";

    private static string CorrelationId(HttpContext context) =>
        context.Request.Headers.TryGetValue("X-Correlation-Id", out var value)
            && !string.IsNullOrWhiteSpace(value.ToString())
            ? Clean(value.ToString(), 160)
            : Clean(context.TraceIdentifier, 160);

    private static string Clean(string? value, int maximum)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Blank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Diagnostic(Exception exception) =>
        exception.GetType().Name.ToLowerInvariant();

    private sealed record PersistenceContext(
        Guid ConversationId,
        Guid UserMessageId,
        Guid InquiryRunId,
        bool Persisted);

    private sealed record HowToGuide(
        string Code,
        string Title,
        string ModuleCode,
        string ModuleName,
        string Route,
        IReadOnlyList<string> Keywords,
        string Summary,
        IReadOnlyList<string> Steps,
        IReadOnlyList<string> RequiredAccess,
        IReadOnlyList<string> Safeguards,
        IReadOnlyList<string> ApiEvidence,
        IReadOnlyList<string> Sources);

    private static readonly PulseAiSystemToolDefinition[] PeopleTools =
    [
        new("people_project_workspace", "Authorized Project Workspace", "019", "Project Engineering Workspace", "GET", "/api/project-workspace/overview", "Projects, assignments, resource requests, and document counts in the effective user's role/project scope.", ["people_activity"], 1, false, false, false, true),
        new("people_open_tasks", "Authorized Open Work", "001", "Timesheet and Assigned Work", "GET", "/api/assignments/open-tasks", "Open assigned tasks and work targets available to the current effective user.", ["people_activity"], 2, false, false, false, true),
        new("people_capacity_engineers", "Authorized Engineer Capacity Directory", "070", "Capacity & Pipeline Forecasting", "GET", "/api/capacity-forecast/engineers", "Engineers visible in the current server-authorized capacity scope.", ["people_activity"], 3, false, false, false, true),
        new("people_capacity_forecast", "Authorized Capacity and Demand Forecast", "070", "Capacity & Pipeline Forecasting", "GET", "/api/capacity-forecast/forecast?weeks=14", "Current assigned capacity, demand, utilization, and pipeline forecast in authorized scope.", ["people_activity"], 4, false, false, false, true),
        new("people_approval_summary", "Authorized Approval Summary", "002", "Approval Inbox", "GET", "/api/manager/approval-summary", "Submitted and actionable time-approval summary available to the current role.", ["people_activity"], 5, false, false, false, true),
        new("people_flowhive_portfolio", "Authorized FlowHive Portfolio", "066", "Project FlowHive", "GET", "/api/project-flowhive/portfolio", "Project planning, milestone, assignment, and delivery evidence in the current FlowHive scope.", ["people_activity"], 6, false, false, false, true)
    ];

    private static readonly HowToGuide[] Guides =
    [
        new("create_project", "Create a new project", "055D", "Create New Project", "#create-work-register", ["create a project", "create new project", "new project", "gsd project", "sell project"], "Use Module 055D to create a governed project from an authorized GSD or SELL source.", ["Open Create New Project.", "Choose the authorized GSD or SELL source.", "Confirm the authoritative project name, customer, commercial source, PM, dates, and delivery ownership.", "Resolve any required rate or pricing review evidence.", "Review the summary and submit the project through the module's governed creation action.", "Open Module 055C after creation to maintain tasks, assignments, and delivery details."], ["Module 055D visibility and project-creation authority; current source/customer access."], ["Do not invent commercial values or bypass required source evidence.", "View-As remains read-only.", "Creation is performed by Module 055D, not by Celar AI."], ["Module 055D owns project creation; Module 055C owns maintenance after creation."], ["Module 055D current source contract", "Module 999 System User Guide"]),
        new("manage_project", "Manage an existing project", "055C", "Manage Existing Projects", "#work-register", ["manage project", "edit project", "update project", "project task", "add task", "assign engineer"], "Use Module 055C for existing-project details, tasks, assignments, status, and delivery maintenance.", ["Open Manage Existing Projects.", "Select the authorized project.", "Review the summary, source, tasks, assignments, documents, commercial visibility, and audit tabs.", "Make only changes allowed by the current role and project scope.", "Save the specific section and confirm the audit evidence.", "Use the closeout handoff when the project is ready for Module 040."], ["Assigned PM or an authorized PTC/Administrator/Super Administrator role, subject to server scope."], ["Do not change a signed scope or commercial value without the owning approval process.", "Celar AI may explain or draft but does not silently mutate the project."], ["Module 055C is the authoritative existing-project workspace."], ["Module 055C current source contract", "Module 999 System User Guide"]),
        new("upload_documents", "Upload or review a SOW, GSD, or project document", "019", "Project Engineering Workspace", "#project-workspace", ["upload sow", "upload gsd", "upload document", "project document", "sow and gsd", "add document"], "Use Module 019 to work with project documents within the current project and role scope.", ["Open Project Engineering Workspace.", "Select the authorized project.", "Open the Documents area and confirm the current authoritative SOW/GSD or supporting-document version.", "Upload through the project document control when the role has upload authority.", "Set engineering visibility and AI-timesheet-context eligibility only when the document is approved for those uses.", "Wait for private scanning/extraction readiness before expecting Celar AI grounding.", "Verify that obsolete or superseded versions are not treated as authoritative."], ["Project/document visibility; upload and classification authority where applicable."], ["Raw internal documents stay inside the approved private boundary.", "Do not send a SOW/GSD to Claude or OpenAI directly.", "A newer upload is not automatically the authoritative version."], ["GET /api/project-workspace/overview", "Module 011 private document readiness APIs"], ["Module 019 document contract", "Celar AI private document architecture"]),
        new("timesheet", "Enter time and generate a document-grounded suggestion", "001", "Timesheet", "#timesheet", ["enter time", "timesheet", "generate ai suggestion", "time entry", "regular task", "service request"], "Use Module 001 to record actual work; Celar AI can draft a description from the engineer's note, selected work item, and authorized project evidence.", ["Open Timesheet and choose the correct week and date.", "Select the project, Regular Task, Request, Service Request, or approved non-project category.", "Enter the hours and the engineer's factual rough note.", "Select Generate AI Suggestion when available.", "Review the cited SOW/GSD/task evidence and correct any statement that is not supported by work actually performed.", "Apply the suggestion, save the entry, and submit only when the week is complete."], ["Module 001 access and the selected work item/project scope."], ["The engineer remains responsible for accuracy.", "Celar AI cannot prove work occurred from a SOW alone.", "Celar AI does not change hours, dates, projects, save state, submission, or approval without the user's explicit workflow action."], ["POST /api/timesheets/ai-description-suggestions", "GET /api/assignments/open-tasks"], ["Module 001 Timesheet contract", "Celar AI document-grounding contract"]),
        new("approve_time", "Review and approve submitted time", "002", "Approval Inbox", "#manager-approval", ["approve time", "approve timesheet", "approval inbox", "reject time", "decline time", "bulk approval"], "Use Module 002 to review the submitted time that the current PM, Manager, or PTC role is authorized to act on.", ["Open Approval Inbox.", "Choose the week or All Dates when the role permits it.", "Review project, work date, hours, description, source, and current status.", "Approve supported entries or decline with a specific correction reason.", "Use bulk approval only for entries that have been reviewed and share the intended action.", "Confirm that completed items move to history and that stale items are handled through the governed stale-resolution workflow."], ["Applicable PM, Manager, PTC, Administrator, or Super Administrator time-approval authority."], ["Non-project work does not route to PMs.", "Declines require a specific reason.", "View-As cannot approve or mutate time."], ["GET /api/manager/approvals", "POST /api/manager/approvals/approve", "POST /api/manager/approvals/decline"], ["Module 002 Approval Center contract"]),
        new("flowhive", "Create a reviewable FlowHive project plan", "066", "Project FlowHive", "#project-flowhive", ["flowhive", "project plan", "create timeline", "project timeline", "wbs", "critical path"], "Use Module 066 to create a private, document-grounded planning draft and deterministic schedule for PM and Engineering review.", ["Open Project FlowHive and select the authorized project.", "Confirm the authoritative SOW, GSD, design documents, assumptions, constraints, and existing tasks.", "Generate the private planning draft.", "Review deliverables, WBS tasks, dependencies, milestones, risks, out-of-scope items, and open questions.", "Run the deterministic schedule calculation for working dates, critical path, and float.", "Have the PM present the draft to Engineering for technical modification and validation.", "Approve a baseline only through the separately authorized FlowHive workflow."], ["Module 066 and project/document scope."], ["Celar AI cannot baseline the plan, assign engineers, reserve capacity, publish externally, or commit customer dates.", "External models may receive only generic sanitized planning questions."], ["GET /api/project-flowhive/portfolio", "POST /api/project-flowhive/schedule/calculate"], ["Module 066 FlowHive contract", "Celar AI private planning architecture"]),
        new("providers", "Configure AI providers and review Celar AI routing", "064", "AI Provider Configuration Center", "#ai-provider-configuration", ["configure claude", "configure openai", "ai provider", "module 064", "provider key", "private model"], "Use Module 064 for provider credentials, approved models, health, feature routes, circuit breakers, and sanitized fallback policy.", ["Open AI Provider Configuration Center as an authorized administrator.", "Review the Celar AI provider bridge and private-model readiness.", "Configure the approved provider secret through the write-only secret workflow.", "Select approved models and enabled state for the permitted feature.", "Verify provider health and feature routing.", "Confirm that restricted document and financial routes remain private-first.", "Test only through the governed health or feature workflow; secret values are never read back."], ["Module 064 administrator authority."], ["Celar AI is not presented as a public vendor provider.", "Raw SOW/GSD/customer/financial context is not eligible for public-provider routing.", "A safety refusal ends the route and is not bypassed with another provider."], ["GET /api/celar-ai/v1/provider-bridge/readiness", "GET /api/ai-configuration"], ["Module 064 provider-governance contract"]),
        new("troubleshoot", "Troubleshoot Pulse APIs and system behavior", "011", "Celar AI / System Intelligence", "#celar-ai", ["troubleshoot", "api error", "not working", "403", "404", "500", "api running", "system health"], "Use Celar AI system intelligence with Modules 013, 016, 076, 077, 078, and 998 to diagnose the current release without arbitrary mutation.", ["Ask Celar AI with the exact module, route, method, role, environment, timestamp, error, and correlation ID.", "Confirm whether the API is registered in the running ASP.NET endpoint inventory.", "Separate authorization failure from route, validation, dependency, timeout, and application failure.", "Review Module 016 operational evidence and Module 998 diagnostics.", "Compare Module 077 release evidence and Module 078 service/SLO/alert evidence.", "Use an explicitly confirmed safe GET retest only when the API is eligible.", "Open Module 076 with sanitized evidence when the issue remains unresolved."], ["Celar AI/system-intelligence permissions; additional operational evidence depends on role."], ["No arbitrary URL, SQL, deployment, restart, permission change, or remediation is allowed through this help path.", "A registered API is not automatically a healthy dependency result."], ["GET /api/pulse-ai/v1/system/apis", "POST /api/celar-ai/v1/chat"], ["Module 011 System Intelligence contract", "Modules 013/016/076/077/078/998 contracts"]),
        new("permissions", "Review roles and module permissions", "037", "Roles & Permissions Matrix", "#roles-permissions-matrix", ["permission", "role access", "why can't i see", "no access", "view access", "full control", "view as"], "Use Modules 012 and 037 to understand configured roles and effective module/action permissions.", ["Open Roles & Permissions Matrix.", "Select the relevant role and module.", "Review Module Access and the action-level permissions.", "Compare configured policy with the current effective user and View-As state.", "Use Role Administration for authorized role maintenance.", "Retest the target page using the actual user session after an approved change."], ["View access for explanation; role/permission management authority for changes."], ["Super Administrator full control does not transfer into View-As.", "No Access hides the module and direct API access must fail closed.", "Celar AI explains permission evidence but does not grant access."], ["GET /api/rbac/v1/bootstrap", "GET /api/rbac/v1/matrix"], ["Modules 012 and 037 RBAC contracts"]),
        new("financial_reports", "Run reports and investigate financial or billing results", "030", "Analytics Center", "#reporting", ["run report", "financial report", "margin", "budget", "billing readiness", "invoice", "analytics center"], "Use Analytics Center and the owning financial modules for deterministic, permission-scoped calculations and source evidence.", ["Open Analytics Center.", "Select the date range and authorized customer, project, engineer, PM, or team filters.", "Choose the required report or metric.", "Review source health, currency, period, formulas, included/excluded records, and unknown values.", "Open Billing Readiness or Invoice & Billing when an operational blocker requires record-level follow-up.", "Ask Celar AI to explain drivers and exceptions after the authoritative result is loaded."], ["Applicable reporting, project, sales, finance, or executive scope."], ["Celar AI does not run unrestricted SQL or invent missing financial values.", "Missing and unauthorized values remain distinct from zero.", "Celar AI cannot change rates, expenses, invoices, contracts, or reconciliation state."], ["GET /api/project-financials/portfolio", "GET /api/project-financials/reporting-summary"], ["Module 030 Analytics Center contract", "Modules 039 and 042 operational contracts"]),
        new("report_defect", "Report a platform defect", "076", "Defect Intake & Resolution Tracker", "#defect-tracker", ["report defect", "create defect", "bug", "issue in platform"], "Use Module 076 to preserve a reproducible, assigned, auditable defect with the right operational evidence.", ["Open Defect Intake & Resolution Tracker.", "Record the affected module, environment, release, role, route, timestamp, and correlation ID.", "Describe expected behavior, observed behavior, impact, frequency, and reproduction steps.", "Attach only sanitized screenshots or evidence.", "Set priority and ownership according to the support process.", "Track resolution, validation, and closure evidence in the defect lifecycle."], ["Module 076 access and any role-specific defect-management authority."], ["Do not include passwords, tokens, customer documents, or unrestricted logs.", "A defect is not closed until the corrected release and verification evidence are recorded."], ["GET /api/defect-tracker/overview", "GET /api/defect-tracker/defects"], ["Module 076 defect-management contract"])
    ];

    [GeneratedRegex(@"\b(?:what\s+(?:is|are|was|were)\s+.+\s+(?:doing|working\s+on)|what\s+are\s+people\s+doing|who\s+is\s+working\s+on|team\s+(?:activity|workload)|people\s+(?:activity|workload)|employee\s+(?:activity|workload)|current\s+assignments?|assigned\s+work|workload|capacity|utilization|pending\s+approvals?|active\s+tasks?|resource\s+requests?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PeopleActivityRegex();

    [GeneratedRegex(@"\b(?:how\s+(?:do|can|should)\s+i|how\s+to|where\s+do\s+i|what\s+are\s+the\s+steps|steps\s+to|show\s+me\s+how)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HowToRegex();
}
