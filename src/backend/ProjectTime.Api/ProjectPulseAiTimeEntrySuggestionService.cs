using System.Text.RegularExpressions;
using ProjectTime.Api.Ai;

sealed class ProjectPulseAiTimeEntrySuggestionService
{
    private const int MaximumSuggestionCharacters = 1_500;
    private const int MaximumEngineerNoteCharacters = 4_000;
    private static readonly TimeSpan ExternalSignalRegexTimeout = TimeSpan.FromMilliseconds(100);

    // Public providers receive only these server-authored category labels. The
    // Engineer's free text is used for private/local processing and signal
    // detection, but no captured token, name, identifier, or substring is copied
    // into the external capsule. This also protects lowercase and unlabeled names
    // that pattern-only de-identification cannot classify with certainty.
    private static readonly (string Label, Regex Pattern)[] ExternalActivitySignals =
    [
        Signal("technical review and analysis", @"\b(?:review(?:ed|ing)?|analy[sz](?:e|ed|ing)|assess(?:ed|ing)?|evaluat(?:e|ed|ing))\b"),
        Signal("technical investigation and diagnosis", @"\b(?:investigat(?:e|ed|ing)|diagnos(?:e|ed|ing)|troubleshoot(?:ing|ed)?|troubleshot|debug(?:ged|ging)?|isolat(?:e|ed|ing)|root[ \t]+cause)\b"),
        Signal("configuration or implementation activity", @"\b(?:configur(?:e|ed|ing|ation)|implement(?:ed|ing|ation)?|install(?:ed|ing|ation)?|provision(?:ed|ing)?|deploy(?:ed|ing|ment)?)\b"),
        Signal("testing and validation activity", @"\b(?:test(?:ed|ing)?|validat(?:e|ed|ing|ion)|verif(?:y|ied|ying|ication)|confirm(?:ed|ing|ation))\b"),
        Signal("documentation and knowledge transfer", @"\b(?:document(?:ed|ing|ation)?|runbook|diagram(?:med|ming)?|knowledge[ \t]+transfer|handoff)\b"),
        Signal("coordination or support activity", @"\b(?:coordinat(?:e|ed|ing|ion)|meeting|workshop|communicat(?:e|ed|ing|ion)|support(?:ed|ing)?|assist(?:ed|ing)?)\b"),
        Signal("monitoring and operational observation", @"\b(?:monitor(?:ed|ing)?|observ(?:e|ed|ing|ation)|alert(?:ed|ing)?|telemetry|log[ \t]+review)\b"),
        Signal("design and planning activity", @"\b(?:design(?:ed|ing)?|architect(?:ed|ing|ure)?|plan(?:ned|ning)?|discovery|requirements?)\b"),
        Signal("migration, upgrade, or patching activity", @"\b(?:migrat(?:e|ed|ing|ion)|upgrad(?:e|ed|ing)|patch(?:ed|ing)?)\b"),
        Signal("remediation or repair activity", @"\b(?:remediat(?:e|ed|ing|ion)|resolv(?:e|ed|ing)|repair(?:ed|ing)?|fix(?:ed|ing)?)\b"),
        Signal("network or connectivity domain", @"\b(?:network(?:ing)?|connectivity|routing|switching|router|switch|dns|dhcp|bgp|ospf|vlan|wan|lan)\b"),
        Signal("security domain", @"\b(?:security|firewall|vpn|mfa|encryption|certificate|vulnerabilit(?:y|ies)|threat|incident)\b"),
        Signal("identity and access domain", @"\b(?:identity|authentication|authorization|access[ \t]+control|directory|sso|entra)\b"),
        Signal("cloud platform domain", @"\b(?:cloud|azure|aws|gcp|saas|iaas|paas)\b"),
        Signal("compute or operating-system domain", @"\b(?:server|compute|windows|linux|operating[ \t]+system|virtual[ \t]+machine)\b"),
        Signal("storage, backup, or recovery domain", @"\b(?:storage|backup|restore|recovery|replication|snapshot)\b"),
        Signal("application, API, or database domain", @"\b(?:application|software|api|integration|database|sql|postgres|oracle|mysql)\b"),
        Signal("collaboration or messaging domain", @"\b(?:email|exchange|teams|sharepoint|onedrive|collaboration|messaging)\b"),
        Signal("virtualization or container domain", @"\b(?:vmware|virtualization|container|docker|kubernetes)\b"),
        Signal("endpoint or device domain", @"\b(?:endpoint|device|desktop|laptop|workstation|firmware)\b"),
        Signal("service event or change handling", @"\b(?:service[ \t]+request|change|incident|issue|ticket|case)\b")
    ];

    private readonly ProjectPulseAiRouter _router;
    private readonly PulseAiDocumentGroundingService _grounding;
    private readonly PulseAiPrivateRagService _privateRag;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ProjectPulseAiTimeEntrySuggestionService> _logger;

    public ProjectPulseAiTimeEntrySuggestionService(
        ProjectPulseAiRouter router,
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
        var privateTargetFirst = await _router.IsFirstTargetAsync(
            capability,
            CelarAiCapabilityTargets.CelarAi,
            cancellationToken);
        string? privateRagWarning = null;
        ProjectPulseAiTargetDecision? privateRagDecision = null;
        var privateRag = privateTargetFirst
            ? await GeneratePrivateRagAsync(request, cancellationToken)
            : null;
        var privateCelarAttempted = privateTargetFirst && privateRag?.Citations.Count > 0;
        if (privateRag is not null)
        {
            if (privateRag.Citations.Count > 0)
            {
                var usedPrivateInference = UsedPrivateInference(privateRag);
                var privateProviderDecision = PrivateProviderDecision(privateRag);
                _router.RecordAlreadyExecutedPrivateAttempt(
                    capability,
                    privateRag.CorrelationId,
                    usedPrivateInference,
                    privateProviderDecision);
                if (usedPrivateInference)
                {
                    var privateDescription = FinalizeCustomerSuggestion(
                        privateRag.Answer?.DirectConclusion,
                        request);
                    return new ProjectPulseAiTimeEntrySuggestionResult(
                        privateDescription,
                        CelarAiCapabilityTargets.CelarAi,
                        BuildPrivateRagWarning(privateRag),
                        [new ProjectPulseAiTargetDecision(
                            CelarAiCapabilityTargets.CelarAi,
                            "used",
                            "private_document_grounding_succeeded")]);
                }

                // Authorized SOW/project evidence remains private. A deterministic
                // evidence scaffold is not a provider execution, so continue through
                // the configured Celar -> Claude -> OpenAI -> local route using only
                // the non-document prompt below.
                privateRagWarning = BuildPrivateRagWarning(privateRag);
                privateRagDecision = new ProjectPulseAiTargetDecision(
                    CelarAiCapabilityTargets.CelarAi,
                    "failed",
                    privateProviderDecision);
            }
        }

        // BuildPrivateGroundedSuggestion is intentionally not an early-return
        // path: document-derived prose requires successful private inference.
        var grounding = privateTargetFirst && privateRagWarning is null
            ? await BuildGroundingAsync(request, cancellationToken)
            : null;

        // ProjectPulseAiProviders.Local is selected only by the central router
        // after every higher-priority eligible target has failed or been skipped.
        var routed = await _router.GenerateAsync(
            new ProjectPulseAiGenerationRequest(
                capability,
                "You write detailed, accurate, evidence-based, customer-facing professional services timesheet descriptions in complete sentences. You never invent activity or outcomes, and you never change hours, submit time, create tasks, or alter allocations.",
                BuildRemotePromptWithoutPrivateDocuments(request),
                MaxOutputTokens: 520,
                Temperature: 0.2),
            () => BuildLocalSuggestion(request),
            skipPrivateTarget: privateCelarAttempted,
            cancellationToken: cancellationToken);

        var suggestion = routed.Outcome == ProjectPulseAiOutcomes.Refusal
            ? string.Empty
            : FinalizeCustomerSuggestion(routed.Content, request);
        return new ProjectPulseAiTimeEntrySuggestionResult(
            suggestion,
            routed.Provider,
            MergeWarnings(
                routed.Warning,
                privateRagWarning,
                grounding?.Authorized == true && grounding.HasReadyPrivateContext
                    ? BuildPrivateGroundingWarning(grounding)
                    : BuildGroundingReadinessWarning(grounding)),
            MergeTargetDecisions(privateRagDecision, routed.TargetDecisions));
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
        answer.Answer is not null
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

        return $"Provider decision: private_context_withheld_from_external_route. {ready.Length} authorized private document source(s) were available at {grounding.GeneratedAt:O}. Sources: {sourceText}. Coverage: {grounding.CoverageLevel} ({grounding.CoverageScore:P0}). No successful approved private-model answer was produced. Raw document text, extracted summaries, the Engineer note, and structured customer or row identifiers were not sent to Claude or OpenAI. Any eligible external target received only backend-derived fixed activity categories and a generic work classification. Governed local fallback may use the Engineer note and selected Timesheet context inside ProjectPulse. The Engineer must confirm that the suggestion describes only work actually performed.{conflicts}{missing}";
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
            return "Authorized project documents were found, but private extraction or approved AI context summaries are not ready. No raw document content, Engineer note, or structured customer or row identifier was sent to an external provider. An eligible external target receives only backend-derived fixed activity categories and a generic work classification; governed local fallback may use the Engineer note and selected row context inside ProjectPulse.";
        }

        if (grounding.Status == "authorized_project_no_eligible_documents")
        {
            return "No authorized engineering-visible document was enabled for timesheet grounding. No Engineer note or structured customer or row identifier was sent to an external provider. An eligible external target receives only backend-derived fixed activity categories and a generic work classification; governed local fallback may use the Engineer note and selected row context inside ProjectPulse.";
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

    private static IReadOnlyList<ProjectPulseAiTargetDecision>? MergeTargetDecisions(
        ProjectPulseAiTargetDecision? first,
        IReadOnlyList<ProjectPulseAiTargetDecision>? routed)
    {
        if (first is null) return routed;
        return new[] { first }
            .Concat((routed ?? []).Where(item =>
                !(string.Equals(item.Target, CelarAiCapabilityTargets.CelarAi, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.ReasonCode, "private_target_skipped_by_caller", StringComparison.Ordinal))))
            .ToArray();
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

    private static string BuildRemotePromptWithoutPrivateDocuments(
        ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        var identityFreeFacts = BuildPurposeBuiltExternalActivityFacts(request.CurrentDescription);
        return $"""
Write one professional, customer-facing time-entry description for a PSA timesheet.

Primary instruction:
Treat the backend-derived activity categories below as the only factual evidence available to you. Write a clear professional description at the level those categories support, but never imply that you saw the Engineer's note or infer exact products, systems, commands, people, customers, locations, chronology, completion status, customer impact, or technical outcomes.

Privacy boundary:
- No restricted source material, commercial detail, architecture detail, or extracted evidence is included in this request.
- No customer name, project name, project code, task name, task code, person name, internal identifier, work date, or location is included as structured context.
- Do not imply access to restricted source material.
- Use only the backend-derived identity-free activity categories and generic work classification below.

Rules:
- Return only the final description as a polished paragraph of complete sentences.
- Do not mention AI.
- Do not include hours; no duration evidence is present in this capsule.
- Do not invent tools, systems, incidents, outages, meetings, approvals, deliverables, or outcomes.
- Do not state that work is complete; no completion evidence is present in this capsule.
- Make the wording useful for customer review, invoice review, manager approval, and audit history.
- Prefer concrete action verbs such as reviewed, configured, validated, documented, coordinated, investigated, analyzed, updated, tested, supported, or troubleshot.
- When the available evidence supports it, write two to four sentences and approximately 75 to 150 words. Never add generic filler merely to reach the target length.
- If the safe categories are sparse, remain concise and general. If they do not establish what work occurred, explicitly state that additional factual work detail is required. Never add specificity merely to make the response longer.

Backend-derived identity-free activity categories:
{identityFreeFacts}

Generic work classification: {ExternalWorkClassification(request)}
""";
    }

    private static (string Label, Regex Pattern) Signal(string label, string pattern) =>
        (label, new Regex(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
            ExternalSignalRegexTimeout));

    private static string BuildPurposeBuiltExternalActivityFacts(string? value)
    {
        var note = BoundedEngineerNote(value);
        var labels = ExternalActivitySignals
            .Where(signal => signal.Pattern.IsMatch(note))
            .Select(signal => signal.Label)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
        return labels.Length == 0
            ? "No identity-free factual activity category was available."
            : string.Join("; ", labels);
    }

    private static bool HasPurposeBuiltExternalActivityFacts(string? value)
    {
        var note = BoundedEngineerNote(value);
        return ExternalActivitySignals.Any(signal => signal.Pattern.IsMatch(note));
    }

    private static string ExternalWorkClassification(ProjectPulseAiTimeEntrySuggestionRequest request)
    {
        var rowType = (request.RowType ?? string.Empty).Trim().ToLowerInvariant();
        if (rowType is "service_request" or "servicerequest" or "service-request") return "service request activity";
        if (rowType is "nonproject" or "non_project" or "non-project" or "category" or "category_code")
            return "non-project activity";
        return "project task activity";
    }

    private static string BoundedEngineerNote(string? value)
    {
        var note = (value ?? string.Empty).Trim();
        return note.Length <= MaximumEngineerNoteCharacters
            ? note
            : note[..MaximumEngineerNoteCharacters];
    }

    private static bool ContainsPrivateDocumentMarkers(string? value) =>
        Regex.IsMatch(
            value ?? string.Empty,
            @"\b(statement\s+of\s+work|sow|global\s+solution\s+design|gsd|contract|rate\s*card|pricing|proposal|customer\s+document)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
