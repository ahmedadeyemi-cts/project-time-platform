using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

public sealed record CelarAiUniversalAnswerPlan(
    string ContractVersion,
    CelarAiAnswerQuestionClass QuestionClass,
    string IntentCode,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> RequiredToolCodes,
    IReadOnlyList<CelarAiEvidenceMode> RequiredEvidenceModes,
    IReadOnlyList<string> RequiredSourceTypes,
    int MinimumAuthoritativeSources,
    int MaximumEvidenceAgeSeconds,
    bool RequireCitations,
    bool RequireDeterministicCalculation,
    bool PermitPrivateModelSynthesis,
    bool PermitSanitizedExternalAssistance,
    bool RequireEvidenceLimitedAnswerWhenIncomplete,
    IReadOnlyList<string> PrivacyControls,
    IReadOnlyList<string> ClarificationsToRequest,
    string FailClosedConclusion,
    DateTimeOffset PlannedAt);

public sealed record CelarAiReliabilityFinding(
    string Code,
    string Severity,
    string Message,
    string RequiredAction);

public sealed record CelarAiAnswerQualityAssessment(
    string ContractVersion,
    bool Passed,
    string Level,
    decimal Score,
    int SuccessfulAuthoritativeSources,
    int SuccessfulTools,
    int ValidSourceCitations,
    int PrivateCitations,
    int StaleSources,
    bool DeterministicEvidencePresent,
    bool CurrentPublicEvidenceVerified,
    bool ReviewRequired,
    IReadOnlyList<CelarAiReliabilityFinding> Findings,
    DateTimeOffset AssessedAt);

public sealed record CelarAiUniversalAnswerEnforcement(
    PulseAiSystemQuestionResult Result,
    CelarAiAnswerQualityAssessment Assessment);

public sealed record CelarAiUniversalAnswerReadiness(
    string Status,
    string ContractVersion,
    string ToolCatalogVersion,
    int ToolCount,
    int DomainCount,
    int ExistingAdapterCount,
    int CatalogedAdapterGapCount,
    int ProtectedTestRuntimeCount,
    int EvaluationCaseCount,
    IReadOnlyList<string> RequiredQuestionClasses,
    IReadOnlyList<string> Guarantees,
    IReadOnlyList<string> ActivationGates,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Plans the authoritative evidence path for every Ask Celar AI question and
/// applies a final post-answer quality gate. This service never executes SQL,
/// calls a provider, reads a secret, widens authorization, or mutates a record.
/// It evaluates only the permission-scoped evidence returned by the existing
/// governed tools and private retrieval services.
/// </summary>
public sealed class CelarAiUniversalAnswerReliabilityService
{
    public const string ContractVersion = "celar-ai-universal-answer-reliability-v1-20260810";
    public const int FrozenEvaluationCaseCount = 120;

    public CelarAiUniversalAnswerPlan Plan(
        string? question,
        string? intentCode,
        string? projectCode,
        string? projectName,
        string? moduleCode,
        bool includeRepositoryContext,
        int attachmentCount)
    {
        var normalized = Normalize(question);
        var intent = (intentCode ?? string.Empty).Trim().ToLowerInvariant();
        var documentSignal = attachmentCount > 0
            || CelarAiUniversalToolCatalog.HasDocumentSignal(normalized)
            || (includeRepositoryContext
                && (!string.IsNullOrWhiteSpace(projectCode)
                    || !string.IsNullOrWhiteSpace(projectName)));
        var structuredSignal = CelarAiUniversalToolCatalog.HasStructuredInternalSignal(normalized)
            || PulseAiSystemIntelligencePolicy.IntentCodes.Contains(intent, StringComparer.OrdinalIgnoreCase)
                && intent is not "general_knowledge";
        var diagnosticSignal = CelarAiUniversalToolCatalog.HasDiagnosticSignal(normalized)
            || intent is "troubleshooting" or "api_inventory" or "release_and_deployment"
                or "observability" or "security";
        var procedureSignal = CelarAiUniversalToolCatalog.HasProcedureSignal(normalized)
            || intent is "product_help" or "procedure" or "platform_identity";
        var architectureSignal = intent == "future_enhancement"
            || normalized.Contains("design a future", StringComparison.Ordinal)
            || normalized.Contains("architecture", StringComparison.Ordinal)
            || normalized.Contains("enhancement", StringComparison.Ordinal);

        var questionClass = intent == "general_knowledge"
            ? CelarAiUniversalToolCatalog.HasCurrentPublicSignal(normalized)
                ? CelarAiAnswerQuestionClass.PublicCurrent
                : CelarAiAnswerQuestionClass.PublicStable
            : architectureSignal
                ? CelarAiAnswerQuestionClass.ArchitectureEnhancement
                : documentSignal && structuredSignal
                    ? CelarAiAnswerQuestionClass.CrossDomain
                    : documentSignal
                        ? CelarAiAnswerQuestionClass.DocumentEvidence
                        : diagnosticSignal
                            ? CelarAiAnswerQuestionClass.RuntimeDiagnostic
                            : procedureSignal
                                ? CelarAiAnswerQuestionClass.ProductProcedure
                                : structuredSignal
                                    ? CelarAiAnswerQuestionClass.StructuredOperational
                                    : CelarAiAnswerQuestionClass.Unknown;

        var matchedTools = CelarAiUniversalToolCatalog.Match(normalized, intent, 16).ToList();
        AddRequiredTools(questionClass, attachmentCount, matchedTools);
        var tools = matchedTools
            .DistinctBy(tool => tool.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidenceModes = EvidenceModes(questionClass, RequiresDeterministicCalculation(normalized, tools));
        var sourceTypes = tools
            .SelectMany(tool => tool.RequiredSourceTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var clarifications = Clarifications(
            normalized,
            questionClass,
            projectCode,
            projectName,
            moduleCode,
            attachmentCount);
        var deterministic = evidenceModes.Contains(CelarAiEvidenceMode.DeterministicCalculation);
        var minimumSources = questionClass switch
        {
            CelarAiAnswerQuestionClass.CrossDomain => 2,
            CelarAiAnswerQuestionClass.Unknown => 1,
            _ => 1
        };
        var maximumAge = questionClass switch
        {
            CelarAiAnswerQuestionClass.StructuredOperational => 3_600,
            CelarAiAnswerQuestionClass.CrossDomain => 3_600,
            CelarAiAnswerQuestionClass.RuntimeDiagnostic => 1_800,
            CelarAiAnswerQuestionClass.PublicCurrent => 3_600,
            CelarAiAnswerQuestionClass.DocumentEvidence => 86_400,
            CelarAiAnswerQuestionClass.ArchitectureEnhancement => 604_800,
            CelarAiAnswerQuestionClass.ProductProcedure => 2_592_000,
            CelarAiAnswerQuestionClass.PublicStable => 2_592_000,
            _ => 3_600
        };
        var externalAllowed = questionClass is CelarAiAnswerQuestionClass.PublicCurrent
            or CelarAiAnswerQuestionClass.PublicStable;
        var privateModelAllowed = questionClass is not CelarAiAnswerQuestionClass.PublicCurrent
            and not CelarAiAnswerQuestionClass.PublicStable;

        return new CelarAiUniversalAnswerPlan(
            ContractVersion,
            questionClass,
            intent.Length == 0 ? "general_system" : intent,
            tools.Select(tool => tool.Domain)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            tools.Select(tool => tool.Code).ToArray(),
            evidenceModes,
            sourceTypes,
            minimumSources,
            maximumAge,
            RequireCitations: true,
            RequireDeterministicCalculation: deterministic,
            PermitPrivateModelSynthesis: privateModelAllowed,
            PermitSanitizedExternalAssistance: externalAllowed,
            RequireEvidenceLimitedAnswerWhenIncomplete: true,
            PrivacyControls:
            [
                "Resolve the actual and effective user before retrieval.",
                "Each owning module remains the authorization and record-scope authority.",
                "Do not send private documents, tool results, people records, customer identity, financial values, credentials, infrastructure details, or internal question text to a public provider.",
                "Do not execute unrestricted model-generated SQL or return raw tool bodies, embeddings, secrets, or storage paths.",
                "Re-authorize project documents and conversation attachments at retrieval time.",
                "Treat provider safety refusals as terminal and never route around them.",
                "When required evidence is absent, stale, conflicting, unauthorized, or unavailable, return an evidence-limited answer rather than a guess."
            ],
            ClarificationsToRequest: clarifications,
            FailClosedConclusion: FailClosedConclusion(questionClass),
            PlannedAt: DateTimeOffset.UtcNow);
    }

    public CelarAiUniversalAnswerEnforcement Enforce(
        PulseAiSystemQuestionResult result,
        CelarAiUniversalAnswerPlan plan,
        bool includeSourceCitations,
        bool includeAssumptions)
    {
        var now = DateTimeOffset.UtcNow;
        var successfulSources = result.Sources
            .Where(IsSuccessfulSource)
            .ToArray();
        var successfulTools = result.ToolResults
            .Where(tool => tool.Succeeded)
            .ToArray();
        var privateCitations = result.PrivateCitations?.Count ?? 0;
        var knownSourceIds = successfulSources
            .Select(source => source.SourceId)
            .ToHashSet();
        var validCitationIds = result.Answer.CitationIds
            .Where(knownSourceIds.Contains)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var staleSources = successfulSources
            .Where(source => (now - source.ObservedAt).TotalSeconds > plan.MaximumEvidenceAgeSeconds)
            .ToArray();
        var evidenceFamilies = successfulSources
            .Select(source => source.SourceType)
            .Concat(privateCitations > 0 ? ["private_citation"] : Array.Empty<string>())
            .Concat(successfulTools.Select(tool => $"tool:{tool.ToolCode}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var deterministicEvidence = successfulTools.Length > 0
            || successfulSources.Any(source => ContainsAny(
                source.SourceType,
                "internal", "authorized", "calculation", "runtime", "database", "api"));
        var currentPublicVerified = plan.QuestionClass != CelarAiAnswerQuestionClass.PublicCurrent
            || successfulSources.Any(source =>
                !source.Freshness.Contains("not_live", StringComparison.OrdinalIgnoreCase)
                && (source.Freshness.Contains("current", StringComparison.OrdinalIgnoreCase)
                    || source.Freshness.Contains("retrieved", StringComparison.OrdinalIgnoreCase)
                    || source.Freshness.Contains("live", StringComparison.OrdinalIgnoreCase)));
        var findings = new List<CelarAiReliabilityFinding>();

        if (evidenceFamilies.Length < plan.MinimumAuthoritativeSources)
        {
            findings.Add(Blocker(
                "insufficient_authoritative_evidence",
                $"The answer has {evidenceFamilies.Length} authoritative evidence family or families; {plan.MinimumAuthoritativeSources} are required for {plan.QuestionClass}.",
                "Retrieve the required authorized source set or return the evidence-limited conclusion."));
        }
        if (plan.RequireCitations
            && includeSourceCitations
            && validCitationIds.Length == 0
            && privateCitations == 0)
        {
            findings.Add(Blocker(
                "required_citation_missing",
                "No valid source citation was available for a question class that requires evidence attribution.",
                "Add a citation to a successful authorized source or private document anchor; never invent one."));
        }
        if (staleSources.Length > 0)
        {
            findings.Add(Blocker(
                "evidence_freshness_failed",
                $"{staleSources.Length} successful source or sources are older than the {plan.MaximumEvidenceAgeSeconds}-second freshness limit.",
                "Refresh the source at request time or label the answer evidence-limited and stale."));
        }
        if (plan.RequireDeterministicCalculation && !deterministicEvidence)
        {
            findings.Add(Blocker(
                "deterministic_calculation_evidence_missing",
                "The question requires a count, total, schedule, variance, forecast, or other deterministic calculation, but no governed calculation or structured tool evidence was present.",
                "Run the owning deterministic tool and include its calculation basis."));
        }
        if (!currentPublicVerified)
        {
            findings.Add(Blocker(
                "current_public_fact_not_live_verified",
                "The question requests a changing public fact, but the response has no live or retrieval-time public evidence.",
                "Use the governed current-public-information route and cite retrieval-time sources."));
        }
        if (plan.QuestionClass is CelarAiAnswerQuestionClass.DocumentEvidence
                or CelarAiAnswerQuestionClass.CrossDomain
            && privateCitations == 0
            && successfulSources.All(source =>
                !source.SourceType.Contains("document", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(Blocker(
                "private_document_evidence_missing",
                "The question requires document evidence, but no authorized document citation or document source was promoted.",
                "Run permission-filtered private retrieval against the authoritative document version."));
        }
        if (result.Answer.Conflicts.Count > 0)
        {
            findings.Add(Review(
                "conflicting_evidence_requires_review",
                $"The answer contains {result.Answer.Conflicts.Count} unresolved evidence conflict or conflicts.",
                "Present the conflict explicitly and require an owning-module or human authority decision."));
        }
        if (result.Answer.Assumptions.Count > 0 && !includeAssumptions)
        {
            findings.Add(Review(
                "assumptions_hidden_by_preference",
                "The response contains assumptions while the user preference hides assumptions.",
                "Keep the answer reviewable and do not represent an assumption as a verified fact."));
        }
        if (plan.ClarificationsToRequest.Count > 0)
        {
            findings.Add(Review(
                "clarification_recommended",
                "The planner identified missing scope that could change the answer.",
                "Ask the listed clarification when authoritative resolution cannot be completed safely."));
        }
        if (result.ModelProvider is CelarAiCapabilityTargets.Claude or CelarAiCapabilityTargets.OpenAi
            && plan.QuestionClass is not CelarAiAnswerQuestionClass.PublicCurrent
            and not CelarAiAnswerQuestionClass.PublicStable
            && successfulSources.Length == 0
            && privateCitations == 0)
        {
            findings.Add(Blocker(
                "external_model_cannot_establish_internal_fact",
                "An external model response has no authorized internal evidence and therefore cannot establish a Pulse fact.",
                "Use the external text only as non-factual generic guidance or return an evidence-limited result."));
        }

        var blockers = findings.Count(finding => finding.Severity == "blocker");
        var reviews = findings.Count(finding => finding.Severity == "review");
        var score = Math.Clamp(1m - blockers * 0.24m - reviews * 0.08m, 0m, 1m);
        var passed = blockers == 0 && score >= 0.75m;
        var preservedBlocked = result.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase);
        var status = preservedBlocked
            ? result.Status
            : passed
                ? result.Status
                : "partial";
        var confidenceCap = preservedBlocked
            ? result.Answer.Confidence
            : blockers > 0
                ? 0.40m
                : reviews > 0
                    ? 0.74m
                    : 0.98m;
        var confidence = Math.Clamp(Math.Min(result.Answer.Confidence, Math.Min(score, confidenceCap)), 0m, 1m);
        var replaceConclusion = !preservedBlocked
            && findings.Any(finding => finding.Code is
                "insufficient_authoritative_evidence"
                or "current_public_fact_not_live_verified"
                or "private_document_evidence_missing"
                or "deterministic_calculation_evidence_missing"
                or "external_model_cannot_establish_internal_fact");
        var answer = result.Answer with
        {
            DirectConclusion = replaceConclusion
                ? plan.FailClosedConclusion
                : result.Answer.DirectConclusion,
            ScopeAndFilters = AppendDistinct(
                result.Answer.ScopeAndFilters,
                $"Universal answer class: {plan.QuestionClass}.",
                $"Required governed tools: {string.Join(", ", plan.RequiredToolCodes)}.",
                $"Freshness limit: {plan.MaximumEvidenceAgeSeconds} seconds."),
            SourceEvidence = AppendDistinct(
                result.Answer.SourceEvidence,
                $"Reliability contract: {ContractVersion}.",
                $"Successful authoritative evidence families: {evidenceFamilies.Length}.",
                $"Valid source citations: {validCitationIds.Length}; private citations: {privateCitations}."),
            KnownUnknownAndStaleValues = AppendDistinct(
                result.Answer.KnownUnknownAndStaleValues,
                findings.Where(finding => finding.Severity == "blocker").Select(finding => finding.Message).ToArray()),
            Limitations = AppendDistinct(
                result.Answer.Limitations,
                findings.Select(finding => $"{finding.Code}: {finding.Message}").ToArray()),
            RecommendedActions = AppendDistinct(
                result.Answer.RecommendedActions,
                plan.ClarificationsToRequest.Concat(findings.Select(finding => finding.RequiredAction)).ToArray()),
            CitationIds = validCitationIds,
            Confidence = confidence,
            ConfidenceExplanation = passed
                ? $"{result.Answer.ConfidenceExplanation} Universal evidence gate passed with score {score:0.00}."
                : $"Confidence is capped because the universal evidence gate found {blockers} blocker(s) and {reviews} review item(s).",
            DataAsOf = successfulSources.Length > 0
                ? successfulSources.Max(source => source.ObservedAt)
                : result.Answer.DataAsOf
        };
        var assessment = new CelarAiAnswerQualityAssessment(
            ContractVersion,
            passed,
            preservedBlocked ? "blocked" : passed ? "verified" : blockers > 0 ? "evidence_limited" : "review_required",
            score,
            successfulSources.Length,
            successfulTools.Length,
            validCitationIds.Length,
            privateCitations,
            staleSources.Length,
            deterministicEvidence,
            currentPublicVerified,
            ReviewRequired: !passed || plan.QuestionClass == CelarAiAnswerQuestionClass.ArchitectureEnhancement,
            Findings: findings,
            AssessedAt: now);
        var warnings = AppendDistinct(
            result.Warnings,
            passed
                ? $"Universal answer reliability gate passed ({score:0.00})."
                : $"Universal answer reliability gate returned {assessment.Level} ({score:0.00}); unsupported synthesis was not promoted as verified.");
        var enforced = result with
        {
            Status = status,
            Answer = answer,
            Warnings = warnings
        };
        return new CelarAiUniversalAnswerEnforcement(enforced, assessment);
    }

    public CelarAiUniversalAnswerReadiness GetReadiness()
    {
        var tools = CelarAiUniversalToolCatalog.Tools;
        return new CelarAiUniversalAnswerReadiness(
            Status: "universal_answer_reliability_source_ready",
            ContractVersion,
            CelarAiUniversalToolCatalog.ContractVersion,
            ToolCount: tools.Count,
            DomainCount: CelarAiUniversalToolCatalog.Domains.Count,
            ExistingAdapterCount: tools.Count(tool =>
                tool.Availability.Contains("available_existing", StringComparison.OrdinalIgnoreCase)
                || tool.Availability.Contains("available_oracle", StringComparison.OrdinalIgnoreCase)
                || tool.Availability.Contains("available_protected_test", StringComparison.OrdinalIgnoreCase)),
            CatalogedAdapterGapCount: tools.Count(tool =>
                tool.Availability.Contains("requires_execution_adapter", StringComparison.OrdinalIgnoreCase)),
            ProtectedTestRuntimeCount: tools.Count(tool =>
                tool.Availability.Contains("oracle", StringComparison.OrdinalIgnoreCase)
                || tool.Availability.Contains("protected_test", StringComparison.OrdinalIgnoreCase)),
            EvaluationCaseCount: FrozenEvaluationCaseCount,
            RequiredQuestionClasses: Enum.GetNames<CelarAiAnswerQuestionClass>(),
            Guarantees:
            [
                "Every question receives a classified evidence plan before answer promotion.",
                "Internal facts require current authorized Pulse evidence; an external model cannot establish them.",
                "Document claims require permission-filtered private evidence and citations.",
                "Counts, totals, schedules, forecasts, and financial values require deterministic calculation evidence.",
                "Changing public facts require retrieval-time public evidence rather than model memory.",
                "Missing, stale, conflicting, or unauthorized evidence produces an explicit evidence-limited answer.",
                "No unrestricted generated SQL, raw tool response, secret, embedding vector, or storage path is returned."
            ],
            ActivationGates:
            [
                "Complete protected Test deployment and Oracle runtime UAT.",
                "Pass the 120-question correctness, citation, freshness, privacy, and permission regression corpus.",
                "Validate representative SOW, GSD, PDF, DOCX, PPTX, XLSX, image, and mixed-document extraction.",
                "Add missing owning-module execution adapters without broad database credentials.",
                "Measure retrieval recall, citation precision, answer correctness, leakage, refusal behavior, and latency before Production consideration."
            ],
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    public object ToPublicEvidence(
        CelarAiUniversalAnswerPlan plan,
        CelarAiAnswerQualityAssessment assessment) => new
        {
            contractVersion = ContractVersion,
            questionClass = plan.QuestionClass.ToString(),
            intentCode = plan.IntentCode,
            domains = plan.Domains,
            requiredTools = plan.RequiredToolCodes,
            requiredEvidenceModes = plan.RequiredEvidenceModes.Select(value => value.ToString()).ToArray(),
            requiredSourceTypes = plan.RequiredSourceTypes,
            plan.MinimumAuthoritativeSources,
            plan.MaximumEvidenceAgeSeconds,
            plan.RequireCitations,
            plan.RequireDeterministicCalculation,
            plan.PermitPrivateModelSynthesis,
            plan.PermitSanitizedExternalAssistance,
            clarifications = plan.ClarificationsToRequest,
            assessment,
            privacy = new
            {
                rawQuestionPersistedByReliabilityService = false,
                rawToolBodiesReturned = false,
                rawDocumentChunksReturned = false,
                embeddingsReturned = false,
                secretsReturned = false,
                unrestrictedSqlAllowed = false,
                authorizationWidened = false
            }
        };

    private static IReadOnlyList<CelarAiEvidenceMode> EvidenceModes(
        CelarAiAnswerQuestionClass questionClass,
        bool deterministic)
    {
        var modes = questionClass switch
        {
            CelarAiAnswerQuestionClass.StructuredOperational => new List<CelarAiEvidenceMode> { CelarAiEvidenceMode.LiveStructured },
            CelarAiAnswerQuestionClass.DocumentEvidence => [CelarAiEvidenceMode.PrivateDocument],
            CelarAiAnswerQuestionClass.CrossDomain => [CelarAiEvidenceMode.LiveStructured, CelarAiEvidenceMode.PrivateDocument],
            CelarAiAnswerQuestionClass.ProductProcedure => [CelarAiEvidenceMode.SourceControlledProcedure],
            CelarAiAnswerQuestionClass.RuntimeDiagnostic => [CelarAiEvidenceMode.RuntimeDiagnostic],
            CelarAiAnswerQuestionClass.ArchitectureEnhancement => [CelarAiEvidenceMode.LiveStructured, CelarAiEvidenceMode.SourceControlledProcedure],
            CelarAiAnswerQuestionClass.PublicCurrent => [CelarAiEvidenceMode.GovernedPublicCurrent],
            CelarAiAnswerQuestionClass.PublicStable => [CelarAiEvidenceMode.GovernedPublic],
            _ => [CelarAiEvidenceMode.HumanClarification]
        };
        if (deterministic && !modes.Contains(CelarAiEvidenceMode.DeterministicCalculation))
            modes.Add(CelarAiEvidenceMode.DeterministicCalculation);
        return modes;
    }

    private static void AddRequiredTools(
        CelarAiAnswerQuestionClass questionClass,
        int attachmentCount,
        List<CelarAiUniversalToolCapability> tools)
    {
        void Add(string code)
        {
            var tool = CelarAiUniversalToolCatalog.Tools.FirstOrDefault(value => value.Code == code);
            if (tool is not null && tools.All(value => value.Code != code)) tools.Add(tool);
        }

        switch (questionClass)
        {
            case CelarAiAnswerQuestionClass.DocumentEvidence:
                Add("project_documents");
                Add("private_retrieval");
                break;
            case CelarAiAnswerQuestionClass.CrossDomain:
                Add("project_documents");
                Add("private_retrieval");
                Add("project_portfolio");
                break;
            case CelarAiAnswerQuestionClass.ProductProcedure:
                Add("product_knowledge");
                break;
            case CelarAiAnswerQuestionClass.RuntimeDiagnostic:
                Add("system_diagnostics");
                break;
            case CelarAiAnswerQuestionClass.PublicCurrent:
            case CelarAiAnswerQuestionClass.PublicStable:
                Add("governed_public_information");
                break;
            case CelarAiAnswerQuestionClass.ArchitectureEnhancement:
                Add("product_knowledge");
                Add("live_api_inventory");
                break;
        }
        if (attachmentCount > 0) Add("conversation_attachments");
    }

    private static bool RequiresDeterministicCalculation(
        string normalized,
        IReadOnlyList<CelarAiUniversalToolCapability> tools) =>
        ContainsAny(normalized,
            "how many", "count", "total", "hours", "utilization", "capacity", "budget",
            "cost", "margin", "variance", "forecast", "remaining", "critical path", "delay",
            "start date", "finish date", "percentage", "percent")
        || tools.Any(tool => tool.Deterministic
            && ContainsAny(normalized, tool.QuerySignals));

    private static IReadOnlyList<string> Clarifications(
        string normalized,
        CelarAiAnswerQuestionClass questionClass,
        string? projectCode,
        string? projectName,
        string? moduleCode,
        int attachmentCount)
    {
        var values = new List<string>();
        if (ContainsAny(normalized, "this project", "the project", "our project")
            && string.IsNullOrWhiteSpace(projectCode)
            && string.IsNullOrWhiteSpace(projectName))
            values.Add("Select or identify the project whose authorized evidence should be used.");
        if (ContainsAny(normalized, "this module", "the module")
            && string.IsNullOrWhiteSpace(moduleCode))
            values.Add("Identify the affected module number or route.");
        if (questionClass == CelarAiAnswerQuestionClass.DocumentEvidence
            && attachmentCount == 0
            && string.IsNullOrWhiteSpace(projectCode)
            && string.IsNullOrWhiteSpace(projectName))
            values.Add("Select an authorized project or conversation attachment before document retrieval.");
        if (ContainsAny(normalized, "this week", "this month", "this quarter", "current period")
            && !Regex.IsMatch(normalized, @"\b20\d{2}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant))
            values.Add("Resolve the user's time zone and the exact reporting date range before calculating the result.");
        if (normalized.Length < 8)
            values.Add("Provide a complete question and the business scope required to identify an authoritative source.");
        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string FailClosedConclusion(CelarAiAnswerQuestionClass questionClass) =>
        questionClass switch
        {
            CelarAiAnswerQuestionClass.PublicCurrent => "Celar AI could not verify the changing public fact from a retrieval-time source, so it will not answer from model memory.",
            CelarAiAnswerQuestionClass.DocumentEvidence => "Celar AI could not verify the requested document claim from an authorized citation-ready source.",
            CelarAiAnswerQuestionClass.CrossDomain => "Celar AI could not reconcile the required live Pulse data and authorized document evidence into one verified answer.",
            CelarAiAnswerQuestionClass.StructuredOperational => "Celar AI could not verify the requested internal fact from a current authorized Pulse source.",
            CelarAiAnswerQuestionClass.RuntimeDiagnostic => "Celar AI could not verify the diagnostic conclusion from current runtime evidence.",
            CelarAiAnswerQuestionClass.ProductProcedure => "Celar AI could not verify the procedure against the current source-controlled operating contract.",
            _ => "Celar AI does not yet have enough authoritative evidence to provide a verified answer."
        };

    private static bool IsSuccessfulSource(PulseAiSystemSourceEvidence source) =>
        source.Status.Equals("succeeded", StringComparison.OrdinalIgnoreCase)
        && source.StatusCode is >= 200 and < 300;

    private static CelarAiReliabilityFinding Blocker(string code, string message, string action) =>
        new(code, "blocker", message, action);

    private static CelarAiReliabilityFinding Review(string code, string message, string action) =>
        new(code, "review", message, action);

    private static string Normalize(string? value) =>
        Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ", RegexOptions.CultureInvariant);

    private static bool ContainsAny(string value, params string[] signals) =>
        signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string value, IEnumerable<string> signals) =>
        signals.Any(signal => value.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> AppendDistinct(
        IReadOnlyList<string> existing,
        params string[] additions) =>
        existing.Concat(additions)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}