using ProjectTime.Api.Ai;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// One active SOW document considered by the project-scoped FlowHive freshness
/// gate. UploadedAt is the immutable source chronology used to identify the
/// current same-name document; processing timestamps never establish authority.
/// </summary>
public sealed record ProjectFlowHiveSowFreshnessEvidence(
    Guid DocumentId,
    string OriginalFileName,
    string DocumentCategory,
    DateTimeOffset UploadedAt,
    bool ReadyForAiPlanner);

public sealed record ProjectFlowHivePendingSowReplacement(
    string OriginalFileName,
    Guid NewestDocumentId,
    DateTimeOffset NewestUploadedAt,
    IReadOnlyList<Guid> OlderReadyDocumentIds);

public sealed record ProjectFlowHiveSowFreshnessDecision(
    IReadOnlySet<Guid> CurrentSowDocumentIds,
    IReadOnlyList<ProjectFlowHivePendingSowReplacement> PendingReplacements);

/// <summary>
/// Deterministic authority policy shared by the production gate and executable
/// regression tests. Distinct documents are never collapsed merely because they
/// have the same display filename. Within one same-name SOW lineage, only the
/// newest uploaded active document is current.
/// </summary>
public static class ProjectFlowHiveSowFreshnessPolicy
{
    public static ProjectFlowHiveSowFreshnessDecision Evaluate(
        IEnumerable<ProjectFlowHiveSowFreshnessEvidence>? evidence)
    {
        var current = new HashSet<Guid>();
        var pending = new List<ProjectFlowHivePendingSowReplacement>();

        foreach (var group in (evidence ?? [])
                     .Where(item => item.DocumentId != Guid.Empty)
                     .GroupBy(AuthorityKey, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .GroupBy(item => item.DocumentId)
                .Select(documentRows => documentRows
                    .OrderByDescending(item => item.ReadyForAiPlanner)
                    .ThenByDescending(item => item.UploadedAt)
                    .First())
                .OrderByDescending(item => item.UploadedAt)
                .ThenByDescending(item => item.DocumentId)
                .ToArray();
            if (ordered.Length == 0) continue;

            var newest = ordered[0];
            if (newest.ReadyForAiPlanner)
            {
                current.Add(newest.DocumentId);
                continue;
            }

            var olderReady = ordered
                .Skip(1)
                .Where(item => item.ReadyForAiPlanner)
                .Select(item => item.DocumentId)
                .Distinct()
                .ToArray();
            if (olderReady.Length == 0) continue;

            pending.Add(new ProjectFlowHivePendingSowReplacement(
                newest.OriginalFileName,
                newest.DocumentId,
                newest.UploadedAt,
                olderReady));
        }

        return new ProjectFlowHiveSowFreshnessDecision(current, pending);
    }

    private static string AuthorityKey(ProjectFlowHiveSowFreshnessEvidence item)
    {
        var fileName = (item.OriginalFileName ?? string.Empty).Trim().ToLowerInvariant();
        if (fileName.Length == 0) fileName = item.DocumentId.ToString("D");

        // SOW and statement_of_work are equivalent contractual categories for
        // replacement authority. Filename-matched GSD or supporting documents
        // are not folded into this contractual SOW lineage.
        var category = (item.DocumentCategory ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "statement_of_work" => "sow",
            "sow" => "sow",
            _ => "sow"
        };
        return $"{category}|{fileName}";
    }
}

public static partial class CelarAiProductionPlatformModule
{
    private sealed record FlowHiveSowFreshnessVerification(
        IResult? Failure,
        IReadOnlySet<Guid> CurrentSowDocumentIds,
        Guid? ResolvedProjectId,
        int EvidenceDocumentCount);

    private static async Task<FlowHiveSowFreshnessVerification> VerifyFlowHiveSowFreshnessAsync(
        ProjectFlowHivePlanRequest plan,
        CancellationToken cancellationToken)
    {
        var config = ProjectFlowHiveDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0)
        {
            return FailedFreshnessVerification(
                StatusCodes.Status503ServiceUnavailable,
                "flowhive_sow_freshness_unavailable",
                "FlowHive could not verify the current project SOW because database configuration is incomplete. No plan was generated.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var projectId = await ResolveFlowHiveFreshnessProjectIdAsync(connection, plan, cancellationToken);
            if (!projectId.HasValue)
            {
                return FailedFreshnessVerification(
                    StatusCodes.Status422UnprocessableEntity,
                    "flowhive_project_identity_unresolved",
                    "FlowHive could not resolve one authoritative project identity for SOW verification. Select the project again before generating a plan.");
            }

            var evidence = await LoadFlowHiveSowFreshnessEvidenceAsync(connection, projectId.Value, cancellationToken);
            var decision = ProjectFlowHiveSowFreshnessPolicy.Evaluate(evidence);
            if (decision.PendingReplacements.Count > 0)
            {
                return new FlowHiveSowFreshnessVerification(
                    Results.Json(new
                    {
                        module = "066",
                        feature = CelarAiCapabilityCatalog.ProjectFlowHivePlan,
                        status = "flowhive_sow_replacement_not_ready",
                        message = "FlowHive blocked generation because the newest same-name replacement SOW is not yet citation-ready. An older SOW was not used as stale contractual scope.",
                        pendingReplacements = decision.PendingReplacements.Select(item => new
                        {
                            item.OriginalFileName,
                            item.NewestUploadedAt,
                            processingRequired = true
                        }).ToArray(),
                        warnings = new[]
                        {
                            "Wait for private malware scanning, extraction, indexing, Scope of Services citation detection, and authority reconciliation to complete."
                        },
                        stateChanged = false
                    }, statusCode: StatusCodes.Status422UnprocessableEntity),
                    decision.CurrentSowDocumentIds,
                    projectId,
                    evidence.Count);
            }

            return new FlowHiveSowFreshnessVerification(
                null,
                decision.CurrentSowDocumentIds,
                projectId,
                evidence.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return FailedFreshnessVerification(
                StatusCodes.Status503ServiceUnavailable,
                "flowhive_sow_freshness_unavailable",
                "FlowHive could not verify the current project SOW because the authoritative evidence store is temporarily unavailable. No plan was generated.");
        }
    }

    private static FlowHiveSowFreshnessVerification FailedFreshnessVerification(
        int statusCode,
        string status,
        string message) =>
        new(
            Results.Json(new
            {
                module = "066",
                feature = CelarAiCapabilityCatalog.ProjectFlowHivePlan,
                status,
                message,
                retryable = statusCode == StatusCodes.Status503ServiceUnavailable,
                stateChanged = false
            }, statusCode: statusCode),
            new HashSet<Guid>(),
            null,
            0);

    private static IResult FlowHiveStaleSowCitationFailure(int staleCitationCount) =>
        Results.Json(new
        {
            module = "066",
            feature = CelarAiCapabilityCatalog.ProjectFlowHivePlan,
            status = "flowhive_stale_sow_citation_blocked",
            message = "FlowHive rejected the generated draft because one or more SOW citations did not belong to the current project-scoped SOW authority set. No plan was returned or saved.",
            staleCitationCount,
            stateChanged = false
        }, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static async Task<Guid?> ResolveFlowHiveFreshnessProjectIdAsync(
        NpgsqlConnection connection,
        ProjectFlowHivePlanRequest plan,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT project_id
            FROM projects
            WHERE (
                    @project_id IS NOT NULL
                    AND project_id=@project_id
                  )
               OR (
                    @project_id IS NULL
                    AND @project_code <> ''
                    AND LOWER(project_code)=LOWER(@project_code)
                  )
               OR (
                    @project_id IS NULL
                    AND @project_code = ''
                    AND @project_name <> ''
                    AND LOWER(project_name)=LOWER(@project_name)
                  )
            ORDER BY CASE
                       WHEN @project_id IS NOT NULL AND project_id=@project_id THEN 0
                       WHEN @project_code <> '' AND LOWER(project_code)=LOWER(@project_code) THEN 1
                       ELSE 2
                     END,
                     updated_at DESC
            LIMIT 2;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("project_id", NpgsqlDbType.Uuid).Value =
            plan.ProjectId.HasValue ? plan.ProjectId.Value : DBNull.Value;
        command.Parameters.AddWithValue("project_code", (plan.ProjectCode ?? string.Empty).Trim());
        command.Parameters.AddWithValue("project_name", (plan.ProjectName ?? string.Empty).Trim());

        var matches = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) matches.Add(reader.GetGuid(0));
        return matches.Count == 1 ? matches[0] : null;
    }

    private static async Task<List<ProjectFlowHiveSowFreshnessEvidence>> LoadFlowHiveSowFreshnessEvidenceAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                document.project_intake_document_id,
                COALESCE(document.original_file_name,''),
                CASE
                  WHEN LOWER(COALESCE(document.document_category,''))='statement_of_work' THEN 'sow'
                  ELSE 'sow'
                END,
                document.uploaded_at,
                (
                    COALESCE(document.engineering_visible,FALSE)=TRUE
                    AND COALESCE(document.pulse_ai_processing_status,'')='ready'
                    AND document.pulse_ai_active_version_id IS NOT NULL
                    AND COALESCE(version.authority_status,'') IN ('approved','canonical')
                    AND COALESCE(version.index_status,'') IN ('lexical_ready','embedding_ready','ready')
                    AND COUNT(chunk.chunk_id) FILTER (
                        WHERE chunk.is_active=TRUE
                          AND chunk.index_status IN ('lexical_ready','embedding_ready','ready')
                    ) > 0
                    AND COUNT(chunk.chunk_id) FILTER (
                        WHERE chunk.is_active=TRUE
                          AND chunk.index_status IN ('lexical_ready','embedding_ready','ready')
                          AND (
                              chunk.section_title ILIKE '%scope%'
                              OR chunk.section_title ILIKE '%service%'
                              OR chunk.citation_anchor ILIKE '%scope%'
                              OR chunk.citation_anchor ILIKE '%service%'
                          )
                    ) > 0
                ) AS ready_for_ai_planner
            FROM project_intake_documents document
            LEFT JOIN pulse_ai_document_versions version
              ON version.pulse_ai_document_version_id=document.pulse_ai_active_version_id
            LEFT JOIN pulse_ai_document_chunks chunk
              ON chunk.pulse_ai_document_version_id=version.pulse_ai_document_version_id
            WHERE document.project_id=@project_id
              AND document.is_active=TRUE
              AND (
                  LOWER(COALESCE(document.document_category,'')) IN ('sow','statement_of_work')
                  OR document.original_file_name ILIKE '%statement%of%work%'
                  OR document.original_file_name ~* '(^|[^a-z])sow([^a-z]|$)'
              )
            GROUP BY
                document.project_intake_document_id,
                document.original_file_name,
                document.document_category,
                document.uploaded_at,
                document.engineering_visible,
                document.pulse_ai_processing_status,
                document.pulse_ai_active_version_id,
                version.authority_status,
                version.index_status
            ORDER BY document.uploaded_at DESC,document.project_intake_document_id DESC;
            """;

        var rows = new List<ProjectFlowHiveSowFreshnessEvidence>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ProjectFlowHiveSowFreshnessEvidence(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetBoolean(4)));
        }
        return rows;
    }
}
