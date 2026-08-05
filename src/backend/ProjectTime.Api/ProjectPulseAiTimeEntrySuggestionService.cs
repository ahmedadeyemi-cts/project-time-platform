using System.Text.RegularExpressions;
using ProjectTime.Api.Ai;

sealed class ProjectPulseAiTimeEntrySuggestionService
{
    private const int MaximumSuggestionCharacters = 1_500;
    private const int MaximumEngineerNoteCharacters = 4_000;
    private static readonly TimeSpan ExternalSignalRegexTimeout = TimeSpan.FromMilliseconds(100);

    // Public providers receive only router-owned labels selected by these closed
    // server-authored fact codes. The
    // Engineer's free text is used for private/local processing and signal
    // detection, but no captured token, name, identifier, or substring is copied
    // into the external capsule. This also protects lowercase and unlabeled names
    // that pattern-only de-identification cannot classify with certainty.
    private static readonly (string Code, Regex Pattern)[] ExternalActivitySignals =
    [
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityReviewAnalysis, @"\b(?:review(?:ed|ing)?|analy[sz](?:e|ed|ing)|assess(?:ed|ing)?|evaluat(?:e|ed|ing))\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityInvestigationDiagnosis, @"\b(?:investigat(?:e|ed|ing)|diagnos(?:e|ed|ing)|troubleshoot(?:ing|ed)?|troubleshot|debug(?:ged|ging)?|isolat(?:e|ed|ing)|root[ \t]+cause)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityConfigurationImplementation, @"\b(?:configur(?:e|ed|ing|ation)|implement(?:ed|ing|ation)?|install(?:ed|ing|ation)?|provision(?:ed|ing)?|deploy(?:ed|ing|ment)?)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityTestingValidation, @"\b(?:test(?:ed|ing)?|validat(?:e|ed|ing|ion)|verif(?:y|ied|ying|ication)|confirm(?:ed|ing|ation))\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityDocumentationKnowledgeTransfer, @"\b(?:document(?:ed|ing|ation)?|runbook|diagram(?:med|ming)?|knowledge[ \t]+transfer|handoff)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityCoordinationSupport, @"\b(?:coordinat(?:e|ed|ing|ion)|meeting|workshop|communicat(?:e|ed|ing|ion)|support(?:ed|ing)?|assist(?:ed|ing)?)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityMonitoringObservation, @"\b(?:monitor(?:ed|ing)?|observ(?:e|ed|ing|ation)|alert(?:ed|ing)?|telemetry|log[ \t]+review)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityDesignPlanning, @"\b(?:design(?:ed|ing)?|architect(?:ed|ing|ure)?|plan(?:ned|ning)?|discovery|requirements?)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityMigrationUpgradePatching, @"\b(?:migrat(?:e|ed|ing|ion)|upgrad(?:e|ed|ing)|patch(?:ed|ing)?)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetActivityRemediationRepair, @"\b(?:remediat(?:e|ed|ing|ion)|resolv(?:e|ed|ing)|repair(?:ed|ing)?|fix(?:ed|ing)?)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainNetworkConnectivity, @"\b(?:network(?:ing)?|connectivity|routing|switching|router|switch|dns|dhcp|bgp|ospf|vlan|wan|lan)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainSecurity, @"\b(?:security|firewall|vpn|mfa|encryption|certificate|vulnerabilit(?:y|ies)|threat|incident)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainIdentityAccess, @"\b(?:identity|authentication|authorization|access[ \t]+control|directory|sso|entra)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainCloudPlatform, @"\b(?:cloud|azure|aws|gcp|saas|iaas|paas)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainComputeOs, @"\b(?:server|compute|windows|linux|operating[ \t]+system|virtual[ \t]+machine)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainStorageBackupRecovery, @"\b(?:storage|backup|restore|recovery|replication|snapshot)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainApplicationApiDatabase, @"\b(?:application|software|api|integration|database|sql|postgres|oracle|mysql)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainCollaborationMessaging, @"\b(?:email|exchange|teams|sharepoint|onedrive|collaboration|messaging)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainVirtualizationContainer, @"\b(?:vmware|virtualization|container|docker|kubernetes)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainEndpointDevice, @"\b(?:endpoint|device|desktop|laptop|workstation|firmware)\b"),
        Signal(CelarAiExternalCapsuleCatalog.TimesheetDomainServiceEventChange, @"\b(?:service[ \t]+request|change|incident|issue|ticket|case)\b")
    ];

    private readonly CelarAiCapabilityRouter _router;
    private readonly PulseAiDocumentGroundingService _grounding;
    private readonly PulseAiPrivateRagService _privateRag;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ProjectPulseAiTimeEntrySuggestionService> _logger;

    public ProjectPulseAiTimeEntrySuggestionService(
        CelarAiCapabilityRouter router,
        PulseAiDocumentGroundingService grounding,
        PulseAiPrivateRagService privateRag,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ProjectPulseAiTimeEntrySuggestionService> logger)
    {
        _router = router;
        _grounding = grounding;
        _privateRag = privateRag;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<ProjectPulseAiTimeEntrySuggestionResult> GenerateAsync(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var capability = CelarAiCapabilityCatalog.ResolveTimesheetFeature(
            request.RowType,
            request.RowLabel,
            request.TaskCode,
            request.ProjectCode,
            request.ProjectName);
        var grounding = HasProjectIdentity(request)
            ? await BuildGroundingAsync(request, cancellationToken)
            : null;
        var hasReadyPrivateDocuments = grounding?.Authorized == true
            && grounding.HasReadyPrivateContext;
        var externalFactCodes = BuildPurposeBuiltExternalFactCodes(request);
        var hasExternalActivityFacts = HasPurposeBuiltExternalActivityFacts(request.CurrentDescription);
        var execution = new CelarAiCapabilityExecutionContext(
            Feature: capability,
            ContainsPrivateDocuments: hasReadyPrivateDocuments,
            ContainsCustomerIdentity: !string.IsNullOrWhiteSpace(request.CustomerName),
            ContainsPeopleRecords: false,
            ContainsFinancialValues: false,
            AllowSanitizedExternalAssistance: hasExternalActivityFacts,
            SensitiveTerms: SensitiveTerms(request),
            ConsumerModule: "001",
            CorrelationId: _httpContextAccessor.HttpContext?.TraceIdentifier
                ?? Guid.NewGuid().ToString("N"),
            IdentityTerms: IdentityTerms(request),
            PurposeBuiltDeidentifiedInput: true,
            DeidentifiedFactsAvailable: hasExternalActivityFacts,
            ExternalCapsulePurpose: CelarAiExternalCapsuleCatalog.TimesheetCustomerDescription,
            PrivateTargetAllowed: true,
            ExternalFactCodes: externalFactCodes);
        var privateRequest = new ProjectPulseAiGenerationRequest(
            capability,
            "You write detailed, accurate, evidence-based, customer-facing professional services timesheet descriptions in complete sentences. Use only authorized private context and the Engineer's factual note. Never invent activity or outcomes, and never change hours, submit time, create tasks, or alter allocations.",
            BuildPrivatePrompt(request),
            MaxOutputTokens: 520,
            Temperature: 0.2);

        PulseAiPrivateRagAnswer? privateRag = null;
        ProjectPulseAiRouteResult routed;
        if (hasReadyPrivateDocuments)
        {
            routed = await _router.GenerateWithPrivateTargetAsync(
                privateRequest,
                execution,
                async privateCancellationToken =>
                {
                    privateRag = await GeneratePrivateRagAsync(request, privateCancellationToken);
                    return PrivateRagTargetResult(privateRag);
                },
                localFallback: () => BuildLocalSuggestion(request),
                cancellationToken);
        }
        else
        {
            // Without ready private documents, the persisted Module 064 order is
            // used exactly. If Celar AI is selected, its private target receives
            // the private prompt; Claude/OpenAI receive only the closed fact-code
            // capsule constructed by the central router.
            routed = await _router.GenerateAsync(
                privateRequest,
                execution,
                () => BuildLocalSuggestion(request),
                cancellationToken);
        }

        var suggestion = routed.Outcome == ProjectPulseAiOutcomes.Refusal
            ? string.Empty
            : FinalizeCustomerSuggestion(routed.Content, request);
        var privateContextWarning = privateRag is not null
            ? BuildPrivateRagWarning(privateRag)
            : hasReadyPrivateDocuments && grounding is not null
                ? BuildPrivateGroundingWarning(grounding)
                : BuildGroundingReadinessWarning(grounding);
        return new ProjectPulseAiTimeEntrySuggestionResult(
            suggestion,
            routed.Provider,
            MergeWarnings(routed.Warning, privateContextWarning),
            routed.TargetDecisions);
    }

    private async Task<PulseAiPrivateRagAnswer?> GeneratePrivateRagAsync(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return null;
        var actualUserId = ActualUserId(context) ?? effectiveUserId.Value;
        if (request.ProjectId is null
            && request.TaskId is null
            && request.AssignmentId is null
            && string.IsNullOrWhiteSpace(request.ProjectCode)
            && string.IsNullOrWhiteSpace(request.ProjectName))
        {
            return null;
        }

        try
        {
            return await _privateRag.GenerateTimesheetAsync(
                actualUserId,
                effectiveUserId.Value,
                new PulseAiPrivateTimesheetRequest(
                    WorkDate: request.WorkDate,
                    TimeType: request.TimeType,
                    RowType: request.RowType,
                    RowLabel: request.RowLabel,
                    ProjectCode: request.ProjectCode,
                    ProjectName: request.ProjectName,
                    TaskCode: request.TaskCode,
                    TaskName: request.TaskName,
                    CategoryCode: request.CategoryCode,
                    EngineerNote: request.CurrentDescription,
                    DetailLevel: "detailed",
                    ProjectId: request.ProjectId,
                    TaskId: request.TaskId,
                    AssignmentId: request.AssignmentId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private RAG Timesheet suggestion failed without logging the Engineer note or private source text.");
            return null;
        }
    }

    private async Task<PulseAiGroundingContext?> BuildGroundingAsync(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return null;

        try
        {
            return await _grounding.BuildTimesheetContextAsync(
                effectiveUserId.Value,
                new PulseAiTimesheetGroundingInput(
                    WorkDate: request.WorkDate,
                    TimeType: request.TimeType,
                    RowType: request.RowType,
                    RowLabel: request.RowLabel,
                    ProjectCode: request.ProjectCode,
                    ProjectName: request.ProjectName,
                    TaskCode: request.TaskCode,
                    TaskName: request.TaskName,
                    CurrentDescription: request.CurrentDescription,
                    ProjectId: request.ProjectId,
                    TaskId: request.TaskId,
                    AssignmentId: request.AssignmentId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI timesheet document grounding failed. The existing non-document suggestion path remains available without exposing source details.");
            return null;
        }
    }

    private static Guid? ActualUserId(HttpContext? context)
    {
        if (context is null) return null;
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

    private static Guid? EffectiveUserId(HttpContext? context)
    {
        if (context is null) return null;

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

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string CleanSuggestion(string? value)
    {
        var cleaned = (value ?? "").Trim();

        cleaned = Regex.Replace(cleaned, "^```(?:[A-Za-z0-9_-]+)?\\s*", string.Empty);
        cleaned = Regex.Replace(cleaned, "\\s*```$", string.Empty);

        if (cleaned.StartsWith("\"") && cleaned.EndsWith("\"") && cleaned.Length > 1)
        {
            cleaned = cleaned[1..^1].Trim();
        }

        cleaned = Regex.Replace(cleaned, "(?:^|[\\r\\n]+)\\s*(?:[-*•]|\\d+[.)])\\s+", " ");
        cleaned = cleaned.Replace("\r", " ").Replace("\n", " ");
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();

        if (cleaned.Length > MaximumSuggestionCharacters)
        {
            var bounded = cleaned[..MaximumSuggestionCharacters].TrimEnd();
            var sentenceEnd = bounded.LastIndexOfAny(['.', '!', '?']);
            cleaned = sentenceEnd >= MaximumSuggestionCharacters * 3 / 5
                ? bounded[..(sentenceEnd + 1)].TrimEnd()
                : bounded.TrimEnd(' ', ',', ';', ':', '-');
        }

        return AsSentence(cleaned);
    }

    private static string AsSentence(string? value)
    {
        var sentence = Regex.Replace((value ?? string.Empty).Trim(), "\\s+", " ");
        if (sentence.Length == 0) return string.Empty;
        if (char.IsLower(sentence[0]))
        {
            sentence = char.ToUpperInvariant(sentence[0]) + sentence[1..];
        }
        return sentence.EndsWith('.') || sentence.EndsWith('!') || sentence.EndsWith('?')
            ? sentence
            : sentence + ".";
    }

    private static bool UsedPrivateInference(PulseAiPrivateRagAnswer answer) =>
        answer.Citations.Count > 0
        && answer.Answer is not null
        && !string.IsNullOrWhiteSpace(answer.Answer.DirectConclusion)
        && !string.IsNullOrWhiteSpace(answer.ModelProvider)
        && !answer.ModelProvider.Contains("deterministic", StringComparison.OrdinalIgnoreCase);

    private static string PrivateProviderDecision(PulseAiPrivateRagAnswer answer)
    {
        if (UsedPrivateInference(answer)) return "private_model_completed";
        if (answer.ModelProvider.Contains("deterministic", StringComparison.OrdinalIgnoreCase))
            return "deterministic_private_fallback";
        if (answer.Answer is null) return "private_model_unavailable";
        return "private_model_output_unusable";
    }

    private static ProjectPulseAiProviderResult PrivateRagTargetResult(
        PulseAiPrivateRagAnswer? answer)
    {
        if (answer is not null && IsPrivateSafetyRefusal(answer))
        {
            return new ProjectPulseAiProviderResult(
                Provider: CelarAiCapabilityTargets.CelarAi,
                Outcome: ProjectPulseAiOutcomes.Refusal,
                Content: string.Empty,
                Code: "private_model_safety_refusal",
                Message: "The private Celar AI document-grounded Timesheet target declined the request.",
                RequestId: answer.CorrelationId,
                Usage: null,
                HttpStatusCode: null);
        }

        if (answer is not null && UsedPrivateInference(answer))
        {
            return new ProjectPulseAiProviderResult(
                Provider: CelarAiCapabilityTargets.CelarAi,
                Outcome: ProjectPulseAiOutcomes.Success,
                Content: answer.Answer!.DirectConclusion,
                Code: null,
                Message: null,
                RequestId: answer.CorrelationId,
                Usage: null,
                HttpStatusCode: null);
        }

        var diagnosticCode = answer is null
            ? "private_document_grounding_unavailable"
            : string.IsNullOrWhiteSpace(answer.DiagnosticCode)
                ? PrivateProviderDecision(answer)
                : answer.DiagnosticCode;
        return new ProjectPulseAiProviderResult(
            Provider: CelarAiCapabilityTargets.CelarAi,
            Outcome: ProjectPulseAiOutcomes.Unavailable,
            Content: null,
            Code: diagnosticCode,
            Message: "The private Celar AI document-grounded Timesheet target did not complete.",
            RequestId: answer?.CorrelationId,
            Usage: null,
            HttpStatusCode: null);
    }

    private static bool IsPrivateSafetyRefusal(PulseAiPrivateRagAnswer answer) =>
        string.Equals(answer.Status, "refused", StringComparison.OrdinalIgnoreCase)
        || answer.DiagnosticCode.Contains("refus", StringComparison.OrdinalIgnoreCase)
        || answer.DiagnosticCode.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
        || answer.DiagnosticCode.Contains("safety", StringComparison.OrdinalIgnoreCase);

    private static bool HasProjectIdentity(ProjectPulseAiTimeEntrySuggestionRequest request) =>
        request.ProjectId is not null
        || request.TaskId is not null
        || request.AssignmentId is not null;

    private static IReadOnlyList<string> SensitiveTerms(
        ProjectPulseAiTimeEntrySuggestionRequest request) =>
        new[]
        {
            request.CustomerName,
            request.ProjectCode,
            request.ProjectName,
            request.TaskCode,
            request.TaskName,
            request.RowLabel,
            request.CategoryCode,
            request.WorkDate == default ? null : request.WorkDate.ToString("yyyy-MM-dd"),
            request.TimeEntryId?.ToString(),
            request.AssignmentId?.ToString(),
            request.ProjectId?.ToString(),
            request.TaskId?.ToString(),
            request.NonProjectTimeCategoryId?.ToString()
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IReadOnlyList<string> IdentityTerms(
        ProjectPulseAiTimeEntrySuggestionRequest request) =>
        new[] { request.CustomerName }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string FinalizeCustomerSuggestion(
        string? value,
        ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        var cleaned = CleanSuggestion(value);
        if (cleaned.Length == 0)
        {
            cleaned = BuildLocalSuggestion(request);
        }

        var sentences = Regex.Split(cleaned, "(?<=[.!?])\\s+")
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Take(4)
            .Select(AsSentence)
            .ToList();

        if (sentences.Count < 2)
        {
            var task = FirstNonBlank(
                request.TaskName,
                request.RowLabel,
                request.TaskCode,
                request.CategoryCode,
                "the selected activity");
            var project = FirstNonBlank(request.ProjectName, request.ProjectCode);
            var projectPhrase = string.IsNullOrWhiteSpace(project) ? string.Empty : $" for {project}";
            var datePhrase = request.WorkDate == default ? string.Empty : $" on {request.WorkDate:MMM d, yyyy}";
            sentences.Add($"This work was recorded against {task}{projectPhrase}{datePhrase}.");
        }

        return CleanSuggestion(string.Join(" ", sentences));
    }

    private static string BuildPrivateGroundingWarning(PulseAiGroundingContext grounding)
    {
        var ready = grounding.Documents.Where(document => document.SummaryReady).ToArray();
        var sources = ready
            .Take(4)
            .Select(document => $"{document.DocumentCategory.ToUpperInvariant()}: {document.OriginalFileName}")
            .ToArray();
        var sourceText = sources.Length > 0
            ? string.Join("; ", sources)
            : "approved project documentation";
        var conflicts = grounding.Conflicts.Count > 0
            ? $" Review required: {string.Join(" ", grounding.Conflicts)}"
            : string.Empty;
        var missing = grounding.MissingInputs.Count > 0
            ? $" Missing or incomplete evidence: {string.Join(" ", grounding.MissingInputs.Take(3))}"
            : string.Empty;

        return $"Provider decision: private_context_withheld_from_external_route. {ready.Length} authorized private document source(s) were available at {grounding.GeneratedAt:O}. Sources: {sourceText}. Coverage: {grounding.CoverageLevel} ({grounding.CoverageScore:P0}). No successful approved private-model answer was produced. Raw document text, extracted summaries, the Engineer note, and structured customer or row identifiers were not sent to Claude or OpenAI. Any eligible external target received only the central router's closed activity, domain, and work-classification fact codes. Governed local fallback may use the Engineer note and selected Timesheet context inside ProjectPulse. The Engineer must confirm that the suggestion describes only work actually performed.{conflicts}{missing}";
    }

    private static string BuildPrivateRagWarning(PulseAiPrivateRagAnswer privateRag)
    {
        var sourceCount = privateRag.Citations.Count;
        var providerDecision = PrivateProviderDecision(privateRag);
        var diagnostic = string.IsNullOrWhiteSpace(privateRag.DiagnosticCode)
            ? string.Empty
            : $" Diagnostic: {privateRag.DiagnosticCode}.";
        return $"Provider decision: {providerDecision}. Pulse AI used {sourceCount} authorized private source citation(s) as of {privateRag.DataAsOf:O}; no private document text was sent to Claude or OpenAI. Engineer must review and explicitly apply the proposed description. Hours, project, task, save, submission, and approval were not changed.{diagnostic}";
    }

    private static string? BuildGroundingReadinessWarning(PulseAiGroundingContext? grounding)
    {
        if (grounding is null) return null;

        if (grounding.Status == "documents_found_context_not_ready")
        {
            return "Authorized project documents were found, but private extraction or approved AI context summaries are not ready. No raw document content, Engineer note, or structured customer or row identifier was sent to an external provider. An eligible external target receives only the central router's closed activity, domain, and work-classification fact codes; governed local fallback may use the Engineer note and selected row context inside ProjectPulse.";
        }

        if (grounding.Status == "authorized_project_no_eligible_documents")
        {
            return "No authorized engineering-visible document was enabled for timesheet grounding. No Engineer note or structured customer or row identifier was sent to an external provider. An eligible external target receives only the central router's closed activity, domain, and work-classification fact codes; governed local fallback may use the Engineer note and selected row context inside ProjectPulse.";
        }

        if (grounding.Status is "project_not_resolved" or "project_outside_effective_user_scope" or "task_or_assignment_not_resolved")
        {
            return "Authorized document grounding was not available for the selected project context. No restricted project or document content was retrieved.";
        }

        if (grounding.Status is "document_grounding_unavailable" or "database_configuration_missing")
        {
            return "Private document grounding was temporarily unavailable. The existing non-document suggestion path was used without exposing database or document details.";
        }

        return null;
    }

    private static string? MergeWarnings(params string?[] warnings)
    {
        var values = warnings
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? null : string.Join(" ", values);
    }

    private static string BuildLocalSuggestion(ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        var task = FirstNonBlank(request.TaskName, request.RowLabel, request.TaskCode, request.CategoryCode, "assigned activity");
        var project = FirstNonBlank(request.ProjectName, request.ProjectCode);
        var timeType = string.Equals(request.TimeType, "afterhours", StringComparison.OrdinalIgnoreCase)
            ? "after-hours"
            : "standard business hours";

        var roughNote = CleanSuggestion(request.CurrentDescription);

        if (!string.IsNullOrWhiteSpace(roughNote))
        {
            var target = string.IsNullOrWhiteSpace(project)
                ? task
                : $"{task} for {project}";
            return CleanSuggestion(
                $"{AsSentence(roughNote)} This work was recorded against {target} during {timeType}.");
        }

        var context = string.IsNullOrWhiteSpace(project) ? task : $"{task} for {project}";
        return CleanSuggestion(
            $"Time was recorded against {context} during {timeType}. Additional factual detail about the work performed is required before this description is ready for customer or invoice review.");
    }

    private static string BuildPrivatePrompt(
        ProjectPulseAiTimeEntrySuggestionRequest request)
        => $"""
            Write one professional, customer-facing time-entry description for a PSA timesheet.
            Treat the Engineer note as the primary evidence of work actually performed. Use the selected
            row context only to identify where that work was recorded; a SOW or task scope cannot prove
            that unreported work occurred. Return only a polished paragraph of two to four complete
            sentences, preferably 75 to 150 words when the evidence supports that detail. Do not mention
            AI, include hours, invent tools or outcomes, or claim completion without factual evidence.

            Work date: {(request.WorkDate == default ? "not supplied" : request.WorkDate.ToString("yyyy-MM-dd"))}
            Time classification: {FirstNonBlank(request.TimeType, "standard")}
            Row classification: {FirstNonBlank(request.RowType, "selected Timesheet row")}
            Customer: {FirstNonBlank(request.CustomerName, "not supplied")}
            Project: {FirstNonBlank(request.ProjectName, request.ProjectCode, "not supplied")}
            Task or category: {FirstNonBlank(request.TaskName, request.TaskCode, request.RowLabel, request.CategoryCode, "not supplied")}
            Engineer note: {BoundedEngineerNote(request.CurrentDescription)}
            """;

    private static (string Code, Regex Pattern) Signal(string code, string pattern) =>
        (code, new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
            ExternalSignalRegexTimeout));

    private static IReadOnlyList<string> BuildPurposeBuiltExternalFactCodes(
        ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        var note = BoundedEngineerNote(request.CurrentDescription);
        return ExternalActivitySignals
            .Where(signal => signal.Pattern.IsMatch(note))
            .Select(signal => signal.Code)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .Append(ExternalWorkClassificationCode(request))
            .ToArray();
    }

    private static bool HasPurposeBuiltExternalActivityFacts(string? value)
    {
        var note = BoundedEngineerNote(value);
        return ExternalActivitySignals.Any(signal => signal.Pattern.IsMatch(note));
    }

    private static string ExternalWorkClassificationCode(ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        var rowType = (request.RowType ?? string.Empty).Trim().ToLowerInvariant();
        if (rowType is "service_request" or "servicerequest" or "service-request")
            return CelarAiExternalCapsuleCatalog.TimesheetClassificationServiceRequest;
        if (rowType is "nonproject" or "non_project" or "non-project" or "category" or "category_code")
            return CelarAiExternalCapsuleCatalog.TimesheetClassificationNonProject;
        return CelarAiExternalCapsuleCatalog.TimesheetClassificationProjectTask;
    }

    private static string BoundedEngineerNote(string? value)
    {
        var note = (value ?? string.Empty).Trim();
        return note.Length <= MaximumEngineerNoteCharacters
            ? note
            : note[..MaximumEngineerNoteCharacters];
    }

}
