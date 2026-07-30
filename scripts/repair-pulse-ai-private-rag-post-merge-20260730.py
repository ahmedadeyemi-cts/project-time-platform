#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def write(relative: str, content: str) -> None:
    (ROOT / relative).write_text(content, encoding="utf-8")


def replace_once(relative: str, old: str, new: str) -> None:
    text = read(relative)
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one repair marker in {relative}; found {count}.")
    write(relative, text.replace(old, new, 1))


def replace_literal(relative: str, old: str, new: str) -> None:
    text = read(relative)
    if old not in text:
        if new in text:
            return
        raise SystemExit(f"Expected repair literal in {relative}: {old}")
    write(relative, text.replace(old, new))


# 1. Definite assignment and stronger source-trust language.
replace_once(
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    '''        var inferencePrivate = options.InferenceConfigured
            && PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
                options.InferenceEndpoint,
                options.PrivateHostAllowlist,
                out _,
                out var inferenceReason);
        var runtimeOptions = PulseAiPrivateRuntimeOptions.FromEnvironment();
        var embeddingPrivate = runtimeOptions.EmbeddingConfigured
            && PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
                runtimeOptions.EmbeddingEndpoint,
                runtimeOptions.PrivateHostAllowlist,
                out _,
                out var embeddingReason);''',
    '''        var inferenceReason = options.InferenceConfigured
            ? "private_endpoint_not_checked"
            : "private_inference_not_configured";
        var inferencePrivate = options.InferenceConfigured
            && PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
                options.InferenceEndpoint,
                options.PrivateHostAllowlist,
                out _,
                out inferenceReason);
        var runtimeOptions = PulseAiPrivateRuntimeOptions.FromEnvironment();
        var embeddingReason = runtimeOptions.EmbeddingConfigured
            ? "private_endpoint_not_checked"
            : "private_embedding_not_configured";
        var embeddingPrivate = runtimeOptions.EmbeddingConfigured
            && PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
                runtimeOptions.EmbeddingEndpoint,
                runtimeOptions.PrivateHostAllowlist,
                out _,
                out embeddingReason);''')
replace_literal(
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs",
    "Never invent a record, metric, permission, source, completed action, date, or financial value.",
    "Never invent a source, project record, metric, date, permission, completed action, financial value or system state.")

# 2. Nullable audit projections and checksum-bound citation persistence.
repository_path = "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs"
nullable_replacements = {
    "reader.IsDBNull(4) ? null : reader.GetGuid(4)": "reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4)",
    "reader.IsDBNull(5) ? null : reader.GetGuid(5)": "reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5)",
    "reader.IsDBNull(6) ? null : reader.GetGuid(6)": "reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6)",
    "reader.IsDBNull(29) ? null : reader.GetFieldValue<DateTimeOffset>(29)": "reader.IsDBNull(29) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(29)",
    "reader.IsDBNull(0) ? null : reader.GetGuid(0)": "reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0)",
    "reader.IsDBNull(1) ? null : reader.GetGuid(1)": "reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1)",
    "reader.IsDBNull(8) ? null : reader.GetInt32(8)": "reader.IsDBNull(8) ? (int?)null : reader.GetInt32(8)",
    "reader.IsDBNull(16) ? null : reader.GetFieldValue<DateTimeOffset>(16)": "reader.IsDBNull(16) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(16)",
}
repository = read(repository_path)
for old, new in nullable_replacements.items():
    repository = repository.replace(old, new)

old_citation_block = '''            var rank = 1;
            foreach (var citation in answer.Citations)
            {
                var chunk = citation.CitationId > 0
                    ? query.CorrelationId
                    : string.Empty;
                const string citationSql = """
                    INSERT INTO pulse_ai_answer_citations (
                        pulse_ai_answer_run_id,chunk_id,project_intake_document_id,
                        pulse_ai_document_version_id,project_id,source_type,source_module,
                        document_category,document_version,original_file_name,
                        citation_anchor,page_number,sheet_name,rank_order,combined_score,
                        source_sha256,text_sha256,source_processed_at
                    )
                    SELECT
                        @answer_run_id,ch.chunk_id,ch.project_intake_document_id,
                        ch.pulse_ai_document_version_id,ch.project_id,'project_document','011',
                        ch.document_category,ch.document_version,d.original_file_name,
                        ch.citation_anchor,ch.page_number,ch.sheet_name,@rank_order,
                        @combined_score,ch.source_sha256,ch.text_sha256,ch.processed_at
                    FROM pulse_ai_document_chunks ch
                    JOIN project_intake_documents d
                      ON d.project_intake_document_id = ch.project_intake_document_id
                    WHERE ch.chunk_id = @chunk_id
                    ON CONFLICT (pulse_ai_answer_run_id, rank_order) DO NOTHING;
                    """;
                await using var citationCommand = new NpgsqlCommand(citationSql, connection, transaction);
                citationCommand.Parameters.AddWithValue("answer_run_id", answer.AnswerRunId);
                citationCommand.Parameters.AddWithValue("rank_order", rank);
                citationCommand.Parameters.AddWithValue("combined_score", citation.RelevanceScore);
                citationCommand.Parameters.AddWithValue("chunk_id", FindChunkId(citation, answer));
                await citationCommand.ExecuteNonQueryAsync(cancellationToken);
                rank++;
                _ = chunk;
            }'''
new_citation_block = '''            var rank = 1;
            foreach (var citation in answer.Citations)
            {
                const string citationSql = """
                    INSERT INTO pulse_ai_answer_citations (
                        pulse_ai_answer_run_id,chunk_id,project_intake_document_id,
                        pulse_ai_document_version_id,project_id,source_type,source_module,
                        document_category,document_version,original_file_name,
                        citation_anchor,page_number,sheet_name,rank_order,combined_score,
                        source_sha256,text_sha256,source_processed_at
                    )
                    SELECT
                        @answer_run_id,ch.chunk_id,ch.project_intake_document_id,
                        ch.pulse_ai_document_version_id,ch.project_id,'project_document','011',
                        ch.document_category,ch.document_version,d.original_file_name,
                        ch.citation_anchor,ch.page_number,ch.sheet_name,@rank_order,
                        @combined_score,ch.source_sha256,ch.text_sha256,ch.processed_at
                    FROM pulse_ai_document_chunks ch
                    JOIN project_intake_documents d
                      ON d.project_intake_document_id = ch.project_intake_document_id
                    WHERE ch.project_intake_document_id = @document_id
                      AND ch.source_sha256 = @source_sha256
                      AND ch.text_sha256 = @text_sha256
                      AND ch.is_active = TRUE
                    ORDER BY ch.processed_at DESC
                    LIMIT 1
                    ON CONFLICT (pulse_ai_answer_run_id, rank_order) DO NOTHING;
                    """;
                await using var citationCommand = new NpgsqlCommand(citationSql, connection, transaction);
                citationCommand.Parameters.AddWithValue("answer_run_id", answer.AnswerRunId);
                citationCommand.Parameters.AddWithValue("rank_order", rank);
                citationCommand.Parameters.AddWithValue("combined_score", citation.RelevanceScore);
                citationCommand.Parameters.AddWithValue("document_id", citation.DocumentId);
                citationCommand.Parameters.AddWithValue("source_sha256", citation.SourceSha256);
                citationCommand.Parameters.AddWithValue("text_sha256", citation.TextSha256);
                await citationCommand.ExecuteNonQueryAsync(cancellationToken);
                rank++;
            }'''
if new_citation_block not in repository:
    if repository.count(old_citation_block) != 1:
        raise SystemExit("Expected the obsolete citation persistence block exactly once.")
    repository = repository.replace(old_citation_block, new_citation_block, 1)

repository, helper_count = re.subn(
    r'''(?ms)^\s*private static string FindChunkId\(.*?^\s*private static async Task<IReadOnlyList<object>> LoadCitationAuditAsync''',
    '\n    private static async Task<IReadOnlyList<object>> LoadCitationAuditAsync',
    repository,
    count=1,
)
if helper_count == 0 and "private static string FindChunkId(" in repository:
    raise SystemExit("Obsolete citation identity helpers remain after repair.")
write(repository_path, repository)

# 3. Register the private inference client and orchestration services exactly once.
replace_once(
    "src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs",
    '''        services.AddHttpClient("PulseAiPrivateEmbedding", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
        });''',
    '''        services.AddHttpClient("PulseAiPrivateEmbedding", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
        });
        services.AddHttpClient("PulseAiPrivateInference", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });''')
replace_once(
    "src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs",
    '''        services.AddSingleton<PulseAiPrivateDocumentRuntimeService>();
        services.AddHostedService<PulseAiPrivateDocumentRuntimeWorker>();
        services.AddSingleton<ProjectPulseAiTimeEntrySuggestionService>();''',
    '''        services.AddSingleton<PulseAiPrivateDocumentRuntimeService>();
        services.AddHostedService<PulseAiPrivateDocumentRuntimeWorker>();
        services.AddSingleton<PulseAiPrivateRagRepository>();
        services.AddSingleton<PulseAiPrivateRetrievalAuthorizationService>();
        services.AddSingleton<PulseAiPrivateRetrievalService>();
        services.AddSingleton<PulseAiPrivateModelClient>();
        services.AddSingleton<PulseAiPrivateRagService>();
        services.AddSingleton<ProjectPulseAiTimeEntrySuggestionService>();''')

# 4. Register private RAG routes through the existing private-runtime composition root.
replace_once(
    "src/backend/ProjectTime.Api/Modules/PulseAiPrivateRuntimeModule.cs",
    '''        endpoints.MapPost(
            "/api/pulse-ai/v1/documents/runtime/jobs/{jobId:guid}/retry",
            (Func<Guid, PulseAiRetryDocumentJobRequest, HttpContext, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)RetryAsync);
        return endpoints;''',
    '''        endpoints.MapPost(
            "/api/pulse-ai/v1/documents/runtime/jobs/{jobId:guid}/retry",
            (Func<Guid, PulseAiRetryDocumentJobRequest, HttpContext, PulseAiPrivateDocumentRuntimeService, CancellationToken, Task<IResult>>)RetryAsync);
        endpoints.MapPulseAiPrivateRagEndpoints();
        return endpoints;''')

# 5. Mount the live private RAG workbench in Module 011.
replace_once(
    "src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx",
    "import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';",
    "import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';\nimport PulseAiPrivateRagWorkbench from './PulseAiPrivateRagWorkbench.jsx';")
replace_once(
    "src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx",
    '''  PulseAiPrivateDocumentPipelineWorkbench,
  PulseAiPrivateRuntimeWorkbench
};''',
    '''  PulseAiPrivateDocumentPipelineWorkbench,
  PulseAiPrivateRuntimeWorkbench,
  PulseAiPrivateRagWorkbench
};''')
replace_once(
    "src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx",
    '''      <PulseAiMissionControl />
      <PulseAiPrivateRuntimeWorkbench />
      <PulseAiPrivateDocumentPipelineWorkbench />''',
    '''      <PulseAiMissionControl />
      <PulseAiPrivateRuntimeWorkbench />
      <PulseAiPrivateRagWorkbench />
      <PulseAiPrivateDocumentPipelineWorkbench />''')

# 6. Make Module 001 private-RAG-first without sending private evidence to public providers.
time_path = "src/backend/ProjectTime.Api/ProjectPulseAiTimeEntrySuggestionService.cs"
replace_once(
    time_path,
    '''    private readonly ProjectPulseAiRouter _router;
    private readonly PulseAiDocumentGroundingService _grounding;
    private readonly IHttpContextAccessor _httpContextAccessor;''',
    '''    private readonly ProjectPulseAiRouter _router;
    private readonly PulseAiDocumentGroundingService _grounding;
    private readonly PulseAiPrivateRagService _privateRag;
    private readonly IHttpContextAccessor _httpContextAccessor;''')
replace_once(
    time_path,
    '''    public ProjectPulseAiTimeEntrySuggestionService(
        ProjectPulseAiRouter router,
        PulseAiDocumentGroundingService grounding,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ProjectPulseAiTimeEntrySuggestionService> logger)''',
    '''    public ProjectPulseAiTimeEntrySuggestionService(
        ProjectPulseAiRouter router,
        PulseAiDocumentGroundingService grounding,
        PulseAiPrivateRagService privateRag,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ProjectPulseAiTimeEntrySuggestionService> logger)''')
replace_once(
    time_path,
    '''        _router = router;
        _grounding = grounding;
        _httpContextAccessor = httpContextAccessor;''',
    '''        _router = router;
        _grounding = grounding;
        _privateRag = privateRag;
        _httpContextAccessor = httpContextAccessor;''')
replace_once(
    time_path,
    '''    {
        var grounding = await BuildGroundingAsync(request, cancellationToken);

        if (grounding?.Authorized == true && grounding.HasReadyPrivateContext)''',
    '''    {
        var privateRag = await GeneratePrivateRagAsync(request, cancellationToken);
        if (privateRag is not null)
        {
            if (privateRag.Citations.Count > 0)
            {
                var privateDescription = CleanSuggestion(privateRag.Answer?.DirectConclusion);
                if (privateDescription.Length == 0)
                {
                    privateDescription = BuildLocalSuggestion(request);
                }
                return new ProjectPulseAiTimeEntrySuggestionResult(
                    privateDescription,
                    ProjectPulseAiProviders.Local,
                    BuildPrivateRagWarning(privateRag));
            }
        }

        var grounding = await BuildGroundingAsync(request, cancellationToken);

        if (grounding?.Authorized == true && grounding.HasReadyPrivateContext)''')
replace_once(
    time_path,
    '''    private async Task<PulseAiGroundingContext?> BuildGroundingAsync(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        CancellationToken cancellationToken)''',
    '''    private async Task<PulseAiPrivateRagAnswer?> GeneratePrivateRagAsync(
        ProjectPulseAiTimeEntrySuggestionRequest request,
        CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return null;
        var actualUserId = ActualUserId(context) ?? effectiveUserId.Value;
        if (string.IsNullOrWhiteSpace(request.ProjectCode)
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
                    request.WorkDate,
                    request.TimeType,
                    request.RowType,
                    request.RowLabel,
                    request.ProjectCode,
                    request.ProjectName,
                    request.TaskCode,
                    request.TaskName,
                    request.CategoryCode,
                    request.CurrentDescription,
                    "standard"),
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
        CancellationToken cancellationToken)''')
replace_once(
    time_path,
    '''    private static Guid? EffectiveUserId(HttpContext? context)
    {
        if (context is null) return null;''',
    '''    private static Guid? ActualUserId(HttpContext? context)
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
        if (context is null) return null;''')
replace_once(
    time_path,
    '''    private static string? BuildGroundingReadinessWarning(PulseAiGroundingContext? grounding)
    {''',
    '''    private static string BuildPrivateRagWarning(PulseAiPrivateRagAnswer privateRag)
    {
        var sourceCount = privateRag.Citations.Count;
        var diagnostic = string.IsNullOrWhiteSpace(privateRag.DiagnosticCode)
            ? string.Empty
            : $" Diagnostic: {privateRag.DiagnosticCode}.";
        return $"Pulse AI used {sourceCount} authorized private source citation(s) as of {privateRag.DataAsOf:O}; no private document text was sent to Claude or OpenAI. Engineer must review and explicitly apply the proposed description. Hours, project, task, save, submission, and approval were not changed.{diagnostic}";
    }

    private static string? BuildGroundingReadinessWarning(PulseAiGroundingContext? grounding)
    {''')

# 7. Align the validator with the current router API while retaining private-first ordering.
replace_literal(
    "src/frontend/project-time-web/scripts/validate-module-011-private-rag-orchestration.mjs",
    "timesheet.indexOf('_privateRag.GenerateTimesheetAsync') < timesheet.indexOf('_router.CompleteAsync')",
    "timesheet.indexOf('_privateRag.GenerateTimesheetAsync') < timesheet.indexOf('_router.GenerateAsync')")

# Final fail-closed assertions.
required_markers = {
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagRepository.cs": [
        "ch.project_intake_document_id = @document_id",
        "citationCommand.Parameters.AddWithValue(\"source_sha256\", citation.SourceSha256)",
        "reader.IsDBNull(29) ? (DateTimeOffset?)null",
    ],
    "src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs": [
        'AddHttpClient("PulseAiPrivateInference"',
        "AddSingleton<PulseAiPrivateRagService>()",
    ],
    "src/backend/ProjectTime.Api/Modules/PulseAiPrivateRuntimeModule.cs": [
        "endpoints.MapPulseAiPrivateRagEndpoints();",
    ],
    "src/backend/ProjectTime.Api/ProjectPulseAiTimeEntrySuggestionService.cs": [
        "_privateRag.GenerateTimesheetAsync",
        "if (privateRag.Citations.Count > 0)",
        "no private document text was sent to Claude or OpenAI",
        "Engineer must review and explicitly apply",
    ],
    "src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx": [
        "import PulseAiPrivateRagWorkbench",
        "<PulseAiPrivateRagWorkbench />",
    ],
}
for relative, markers in required_markers.items():
    text = read(relative)
    missing = [marker for marker in markers if marker not in text]
    if missing:
        raise SystemExit(f"Missing repair markers in {relative}: {missing}")

print("PULSE_AI_PRIVATE_RAG_POST_MERGE_REPAIR=APPLIED")
