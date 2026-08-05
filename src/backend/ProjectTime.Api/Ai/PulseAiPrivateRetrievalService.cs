namespace ProjectTime.Api.Ai;

public sealed class PulseAiPrivateRetrievalService
{
    private readonly PulseAiPrivateRagRepository _repository;
    private readonly PulseAiPrivateRetrievalAuthorizationService _reauthorization;
    private readonly PulseAiPrivateEmbeddingClient _embeddingClient;
    private readonly ILogger<PulseAiPrivateRetrievalService> _logger;

    public PulseAiPrivateRetrievalService(
        PulseAiPrivateRagRepository repository,
        PulseAiPrivateRetrievalAuthorizationService reauthorization,
        PulseAiPrivateEmbeddingClient embeddingClient,
        ILogger<PulseAiPrivateRetrievalService> logger)
    {
        _repository = repository;
        _reauthorization = reauthorization;
        _embeddingClient = embeddingClient;
        _logger = logger;
    }

    public async Task<PulseAiPrivateRetrievalResult> RetrieveAsync(
        PulseAiPrivateRagAccess access,
        PulseAiPrivateRetrievalQuery query,
        PulseAiPrivateRagOptions ragOptions,
        CancellationToken cancellationToken = default)
    {
        if (!access.IsActive)
        {
            return Empty(
                "effective_user_unavailable",
                query,
                ["The current effective user could not be resolved."],
                "effective_user_unavailable");
        }
        if (!await _repository.IsSchemaReadyAsync(cancellationToken))
        {
            return Empty(
                "private_rag_schema_unavailable",
                query,
                ["Migration 053 and the private document index must be available before live private retrieval can run."],
                "private_rag_schema_unavailable");
        }

        PulseAiPrivateRagRepository.ProjectResolution? project = null;
        var hasProjectContext = query.ProjectId is not null
            || query.TaskId is not null
            || query.AssignmentId is not null
            || !string.IsNullOrWhiteSpace(query.ProjectCode)
            || !string.IsNullOrWhiteSpace(query.ProjectName);
        if (hasProjectContext)
        {
            project = await _repository.ResolveProjectAsync(
                access,
                query.ProjectId,
                query.TaskId,
                query.AssignmentId,
                query.ProjectCode,
                query.ProjectName,
                cancellationToken);
            if (project is null)
            {
                return Empty(
                    "project_not_resolved_or_not_authorized",
                    query,
                    ["A unique authorized project could not be resolved from the supplied project, task, or assignment identity."],
                    "project_not_resolved_or_not_authorized");
            }
            query = query with
            {
                ProjectId = project.ProjectId,
                ProjectCode = project.ProjectCode,
                ProjectName = project.ProjectName
            };
        }
        if ((query.FeatureCode == PulseAiPrivateRagPolicy.TimesheetFeature
                || query.FeatureCode == PulseAiPrivateRagPolicy.FlowHiveFeature)
            && query.ProjectId is null)
        {
            return Empty(
                "project_context_required",
                query,
                ["Timesheet and FlowHive private retrieval require an authorized project."],
                "project_context_required");
        }

        var runtimeOptions = PulseAiPrivateRuntimeOptions.FromEnvironment();
        double[]? queryEmbedding = null;
        var retrievalMode = "lexical";
        if (runtimeOptions.EmbeddingConfigured
            && PulseAiPrivateEndpointPolicy.IsApprovedPrivateEndpoint(
                runtimeOptions.EmbeddingEndpoint,
                runtimeOptions.PrivateHostAllowlist,
                out _,
                out _))
        {
            var embedding = await _embeddingClient.GenerateAsync(
                [query.Question],
                runtimeOptions,
                cancellationToken);
            if (embedding.Succeeded && embedding.Vectors.Count == 1)
            {
                queryEmbedding = embedding.Vectors[0];
                retrievalMode = "hybrid";
            }
        }

        try
        {
            var rows = await _repository.RetrieveAsync(
                access,
                query,
                queryEmbedding,
                cancellationToken);
            var reauthorized = await _reauthorization.ReauthorizeAsync(
                access,
                rows.Chunks,
                query.RequireTimesheetFlag,
                cancellationToken);

            var missing = BuildMissingEvidence(query, reauthorized);
            var conflicts = BuildConflicts(reauthorized);
            var coverage = Coverage(query, reauthorized);
            var status = reauthorized.Count == 0
                ? "authorized_evidence_not_found"
                : reauthorized.Count < Math.Min(3, query.MaximumChunks)
                    ? "private_retrieval_partial"
                    : "private_retrieval_ready";
            if (rows.Chunks.Count > 0 && reauthorized.Count == 0)
            {
                status = "prompt_assembly_reauthorization_failed_closed";
                missing = [
                    .. missing,
                    "Previously selected candidates no longer passed prompt-assembly authorization. No source text was sent to a model."
                ];
            }

            return new PulseAiPrivateRetrievalResult(
                Status: status,
                RetrievalMode: retrievalMode,
                ResolvedProjectId: query.ProjectId,
                ResolvedProjectCode: query.ProjectCode ?? project?.ProjectCode ?? string.Empty,
                ResolvedProjectName: query.ProjectName ?? project?.ProjectName ?? string.Empty,
                CandidateCount: rows.CandidateCount,
                AuthorizedCandidateCount: rows.AuthorizedCandidateCount,
                Chunks: reauthorized,
                MissingEvidence: missing,
                Conflicts: conflicts,
                CoverageScore: coverage,
                DataAsOf: reauthorized.Select(chunk => chunk.ProcessedAt).DefaultIfEmpty(DateTimeOffset.UtcNow).Max(),
                DiagnosticCode: rows.DiagnosticCode);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private retrieval orchestration failed without logging question or source text. Feature={Feature} Diagnostic={Diagnostic}",
                query.FeatureCode,
                Diagnostic(exception));
            return Empty(
                "private_retrieval_unavailable",
                query,
                ["Private retrieval could not be completed. No document content was sent to a model."],
                Diagnostic(exception));
        }
    }

    private static IReadOnlyList<string> BuildMissingEvidence(
        PulseAiPrivateRetrievalQuery query,
        IReadOnlyList<PulseAiPrivateRetrievedChunk> chunks)
    {
        var missing = new List<string>();
        if (chunks.Count == 0)
            missing.Add("No current authorized private chunk matched the question and purpose filters.");
        if (query.FeatureCode == PulseAiPrivateRagPolicy.TimesheetFeature)
        {
            if (!chunks.Any(chunk => chunk.DocumentCategory is "sow" or "statement_of_work"))
                missing.Add("No current authorized SOW evidence matched the Timesheet request.");
            if (!chunks.Any(chunk => chunk.DocumentCategory is "gsd" or "global_solution_design"))
                missing.Add("No current authorized GSD evidence matched the Timesheet request.");
        }
        if (query.FeatureCode == PulseAiPrivateRagPolicy.FlowHiveFeature)
        {
            if (!chunks.Any(chunk => chunk.DocumentCategory is "sow" or "statement_of_work"))
                missing.Add("No current authorized SOW evidence matched the planning request.");
            if (!chunks.Any(chunk => chunk.DocumentCategory is "gsd" or "global_solution_design"))
                missing.Add("No current authorized GSD evidence matched the planning request.");
            if (!chunks.Any(chunk => chunk.DocumentCategory is "architecture" or "design"))
                missing.Add("No architecture or design evidence matched the planning request.");
        }
        return missing;
    }

    private static IReadOnlyList<string> BuildConflicts(
        IReadOnlyList<PulseAiPrivateRetrievedChunk> chunks)
    {
        var conflicts = new List<string>();
        foreach (var category in new[] { "sow", "gsd" })
        {
            var versions = chunks
                .Where(chunk => category == "sow"
                    ? chunk.DocumentCategory is "sow" or "statement_of_work"
                    : chunk.DocumentCategory is "gsd" or "global_solution_design")
                .Select(chunk => chunk.DocumentVersion)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (versions.Length > 1)
            {
                conflicts.Add($"Multiple {category.ToUpperInvariant()} versions contributed evidence: {string.Join(", ", versions)}. Confirm authoritative version status before relying on a contractual conclusion.");
            }
        }
        return conflicts;
    }

    private static decimal Coverage(
        PulseAiPrivateRetrievalQuery query,
        IReadOnlyList<PulseAiPrivateRetrievedChunk> chunks)
    {
        if (chunks.Count == 0) return 0m;
        decimal score = Math.Min(0.40m, chunks.Count * 0.05m);
        if (chunks.Select(chunk => chunk.DocumentId).Distinct().Count() >= 2) score += 0.10m;
        if (chunks.Any(chunk => chunk.DocumentCategory is "sow" or "statement_of_work")) score += 0.20m;
        if (chunks.Any(chunk => chunk.DocumentCategory is "gsd" or "global_solution_design")) score += 0.20m;
        if (chunks.Any(chunk => chunk.DocumentCategory is "architecture" or "design")) score += 0.10m;
        if (query.FeatureCode == PulseAiPrivateRagPolicy.HelpSearchFeature && chunks.Count >= 3) score += 0.20m;
        return Math.Min(1m, score);
    }

    private static PulseAiPrivateRetrievalResult Empty(
        string status,
        PulseAiPrivateRetrievalQuery query,
        IReadOnlyList<string> missing,
        string diagnosticCode) =>
        new(
            Status: status,
            RetrievalMode: "none",
            ResolvedProjectId: query.ProjectId,
            ResolvedProjectCode: query.ProjectCode ?? string.Empty,
            ResolvedProjectName: query.ProjectName ?? string.Empty,
            CandidateCount: 0,
            AuthorizedCandidateCount: 0,
            Chunks: [],
            MissingEvidence: missing,
            Conflicts: [],
            CoverageScore: 0m,
            DataAsOf: DateTimeOffset.UtcNow,
            DiagnosticCode: diagnosticCode);

    private static string Diagnostic(Exception exception) => exception switch
    {
        TimeoutException => "private_retrieval_timeout",
        Npgsql.NpgsqlException => "private_retrieval_database_failure",
        OperationCanceledException => "private_retrieval_cancelled",
        _ => "private_retrieval_failure"
    };
}
