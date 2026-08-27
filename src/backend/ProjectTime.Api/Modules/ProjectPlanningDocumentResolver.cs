using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Shared project-scoped document authority used by FlowHive and Project Forge.
/// It resolves the existing Work Register SOW and supporting project documents,
/// normalizes their private-processing metadata, and queues private processing
/// idempotently without requiring a duplicate upload or pasted excerpt.
/// </summary>
internal static class ProjectPlanningDocumentResolver
{
    internal const string Contract = "project-planning-document-authority-v1-20260819";

    private static readonly HashSet<string> AdditionalPlanningCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "requirements",
        "requirements_document",
        "customer_requirements",
        "technical_specification",
        "technical_specs",
        "project_charter",
        "implementation_plan",
        "deployment_plan",
        "runbook",
        "method_of_procedure",
        "mop"
    };

    internal static async Task<ProjectPlanningDocumentResolution> ResolveAndPrepareAsync(
        NpgsqlConnection connection,
        Guid projectId,
        Guid actualUserId,
        Guid effectiveUserId,
        string requestedPurpose,
        string correlationId,
        bool queuePending,
        CancellationToken cancellationToken)
    {
        var documents = await LoadAsync(connection, projectId, cancellationToken);
        var selection = SelectCurrent(documents);

        foreach (var document in selection.SelectedDocuments)
            await NormalizeAsync(connection, document, cancellationToken);

        var newlyQueued = 0;
        if (queuePending)
        {
            foreach (var document in selection.SelectedDocuments.Where(document => document.ShouldAutoQueue))
            {
                newlyQueued += await QueueAsync(
                    connection,
                    document,
                    actualUserId,
                    effectiveUserId,
                    requestedPurpose,
                    correlationId,
                    cancellationToken) ? 1 : 0;
            }
        }

        if (selection.StatementOfWork is { ProcessingReady: true })
            await ReconcileCurrentSowAuthorityAsync(connection, selection.StatementOfWork.DocumentId, cancellationToken);

        documents = await LoadAsync(connection, projectId, cancellationToken);
        selection = SelectCurrent(documents);
        return BuildResolution(selection, newlyQueued);
    }

    internal static async Task ReconcileCurrentSowAuthorityAsync(
        NpgsqlConnection connection,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var check = new NpgsqlCommand(
            "SELECT to_regprocedure('public.projectpulse094_reconcile_ready_work_register_sow(uuid)') IS NOT NULL;",
            connection);
        if (await check.ExecuteScalarAsync(cancellationToken) is not true)
            return;

        await using var reconcile = new NpgsqlCommand(
            "SELECT projectpulse094_reconcile_ready_work_register_sow(@document_id);",
            connection);
        reconcile.Parameters.AddWithValue("document_id", documentId);
        await reconcile.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ProjectPlanningDocumentSelection SelectCurrent(
        IReadOnlyList<ProjectPlanningDocumentEvidence> documents)
    {
        var active = documents
            .Where(document => document.ActiveWorkRegisterSource
                && document.DurableFileAvailable
                && document.IsPlanningDocument)
            .OrderByDescending(document => document.EffectiveAt)
            .ThenByDescending(document => document.UploadedAt)
            .ToArray();

        var sow = active.FirstOrDefault(document => document.IsSow);
        var gsd = active.FirstOrDefault(document => document.IsGsd);
        var supplemental = active
            .Where(document => !document.IsSow && !document.IsGsd)
            .GroupBy(document => document.DocumentId)
            .Select(group => group.First())
            .Take(80)
            .ToArray();

        var selected = new List<ProjectPlanningDocumentEvidence>(2 + supplemental.Length);
        if (sow is not null) selected.Add(sow);
        if (gsd is not null) selected.Add(gsd);
        selected.AddRange(supplemental);

        return new ProjectPlanningDocumentSelection(
            documents,
            sow,
            gsd,
            supplemental,
            selected);
    }

    private static ProjectPlanningDocumentResolution BuildResolution(
        ProjectPlanningDocumentSelection selection,
        int newlyQueued)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        var pending = selection.SelectedDocuments
            .Where(document => !document.ReadyForRetrieval && !document.ProcessingTerminalFailure)
            .ToArray();
        var failed = selection.SelectedDocuments
            .Where(document => document.ProcessingTerminalFailure)
            .ToArray();

        if (selection.StatementOfWork is null)
        {
            var registeredSow = selection.Documents
                .Where(document => document.IsSow && document.ActiveWorkRegisterSource)
                .OrderByDescending(document => document.EffectiveAt)
                .ThenByDescending(document => document.UploadedAt)
                .FirstOrDefault();
            if (registeredSow is not null && !registeredSow.DurableFileAvailable)
            {
                blockers.Add("The active Work Register SOW is registered, but its durable file cannot be downloaded from the current shared upload mount.");
                blockers.Add("Restore the Module 055C document file before private processing or AI planning can continue.");
            }
            else
            {
                blockers.Add("No active durable Work Register Statement of Work is associated with this project.");
                blockers.Add("FlowHive and Project Forge use the project SOW already stored in Work Register; no duplicate upload is required.");
            }
        }
        else
        {
            var sow = selection.StatementOfWork;
            if (!sow.ProcessingReady)
                blockers.Add($"The active Work Register SOW private processing state is {sow.ProcessingStatus}.");
            if (sow.ProcessingTerminalFailure && sow.ProcessingErrorCode.Length > 0)
                blockers.Add($"The active Work Register SOW private processing diagnostic is {sow.ProcessingErrorCode}.");
            if (sow.ActiveVersionId is null)
                blockers.Add("The active Work Register SOW has no current private version.");
            if (!sow.AuthorityReady)
                blockers.Add("The active Work Register SOW private version is not approved or canonical.");
            if (!sow.IndexReady)
                blockers.Add("The active Work Register SOW private version is not citation indexed.");
            if (sow.CitationCount == 0)
                blockers.Add("The active Work Register SOW has no citation-ready chunks.");
            if (sow.ScopeCitationCount == 0)
                blockers.Add("No Scope of Services citation was detected in the active Work Register SOW.");
        }

        if (selection.GeneralSolutionDesign is null)
            warnings.Add("No active GSD was located. SOW-supported work may proceed and missing design facts must remain open questions.");

        foreach (var document in failed)
        {
            var label = document.IsSow ? "SOW" : document.IsGsd ? "GSD" : document.CanonicalCategory;
            var diagnostic = document.ProcessingErrorCode.Length > 0
                ? $" Diagnostic: {document.ProcessingErrorCode}."
                : string.Empty;
            var message = $"{label} private processing is {document.ProcessingStatus}; terminal states are not automatically requeued.{diagnostic} An authorized explicit retry is required after the blocker is corrected.";
            if (document.IsSow) blockers.Add(message); else warnings.Add(message);
        }

        if (pending.Length > 0)
            blockers.Add($"Private processing is still in progress for {pending.Length} current project planning document(s).");

        return new ProjectPlanningDocumentResolution(
            Contract,
            selection.Documents,
            selection.StatementOfWork,
            selection.GeneralSolutionDesign,
            selection.SupplementalDocuments,
            selection.SelectedDocuments,
            pending,
            failed,
            newlyQueued,
            blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static async Task<IReadOnlyList<ProjectPlanningDocumentEvidence>> LoadAsync(
        NpgsqlConnection connection,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var rows = new List<ProjectPlanningDocumentEvidence>();
        await using var command = new NpgsqlCommand("""
            SELECT document.project_intake_document_id,
                   COALESCE(document.document_category,''),
                   COALESCE(document.original_file_name,''),
                   COALESCE(document.pulse_ai_processing_status,'not_requested'),
                   COALESCE(document.pulse_ai_processing_error_code,''),
                   document.pulse_ai_active_version_id,
                   COALESCE(version.authority_status,''),
                   COALESCE(version.index_status,''),
                   document.work_register_document_id,
                   COALESCE(work_register.document_type,''),
                   COALESCE(work_register.status,''),
                   COALESCE(work_register.upload_source,''),
                   COALESCE(work_register.stored_file_path,''),
                   COALESCE(document.pulse_ai_effective_at,document.uploaded_at),
                   document.uploaded_at,
                   (SELECT COUNT(*)::int
                      FROM pulse_ai_document_chunks chunk
                     WHERE chunk.pulse_ai_document_version_id=version.pulse_ai_document_version_id
                       AND chunk.is_active=TRUE
                       AND chunk.index_status IN ('lexical_ready','embedding_ready','ready')),
                   (SELECT COUNT(*)::int
                      FROM pulse_ai_document_chunks chunk
                     WHERE chunk.pulse_ai_document_version_id=version.pulse_ai_document_version_id
                       AND chunk.is_active=TRUE
                       AND chunk.index_status IN ('lexical_ready','embedding_ready','ready')
                       AND (chunk.section_title ILIKE '%scope%'
                            OR chunk.section_title ILIKE '%service%'
                            OR chunk.citation_anchor ILIKE '%scope%'
                            OR chunk.citation_anchor ILIKE '%service%'))
              FROM project_intake_documents document
              LEFT JOIN work_register_documents work_register
                ON work_register.work_register_document_id=document.work_register_document_id
              LEFT JOIN pulse_ai_document_versions version
                ON version.pulse_ai_document_version_id=document.pulse_ai_active_version_id
             WHERE document.project_id=@project_id
               AND document.is_active=TRUE
             ORDER BY COALESCE(document.pulse_ai_effective_at,document.uploaded_at) DESC,
                      document.uploaded_at DESC;
            """, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ProjectPlanningDocumentEvidence(
                projectId,
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetGuid(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetFieldValue<DateTimeOffset>(13),
                reader.GetFieldValue<DateTimeOffset>(14),
                reader.GetInt32(15),
                reader.GetInt32(16)));
        }
        return rows;
    }

    private static async Task NormalizeAsync(
        NpgsqlConnection connection,
        ProjectPlanningDocumentEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE project_intake_documents
               SET document_category=@category,
                   engineering_visible=TRUE,
                   ai_timesheet_context_enabled=TRUE,
                   pulse_ai_processing_updated_at=NOW()
             WHERE project_intake_document_id=@document_id;
            """, connection);
        command.Parameters.AddWithValue("category", evidence.CanonicalCategory);
        command.Parameters.AddWithValue("document_id", evidence.DocumentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> QueueAsync(
        NpgsqlConnection connection,
        ProjectPlanningDocumentEvidence evidence,
        Guid actualUserId,
        Guid effectiveUserId,
        string requestedPurpose,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var purpose = Clean(requestedPurpose, 80, "project_planning_ai_automatic");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO pulse_ai_document_processing_jobs(
                project_intake_document_id,project_id,actual_user_id,effective_user_id,
                requested_by_user_id,requested_purpose,correlation_id)
            SELECT @document_id,@project_id,@actual,@effective,@actual,@purpose,@correlation
             WHERE NOT EXISTS(
                SELECT 1
                  FROM pulse_ai_document_processing_jobs existing
                 WHERE existing.project_intake_document_id=@document_id
                   AND existing.job_status IN (
                       'queued','scanning','extracting','awaiting_ocr','embedding','indexing',
                       'retry_wait','cancel_requested'));
            """, connection, transaction);
        command.Parameters.AddWithValue("document_id", evidence.DocumentId);
        command.Parameters.AddWithValue("project_id", evidence.ProjectId);
        command.Parameters.AddWithValue("actual", actualUserId);
        command.Parameters.AddWithValue("effective", effectiveUserId);
        command.Parameters.AddWithValue("purpose", purpose);
        command.Parameters.AddWithValue("correlation", Clean(correlationId, 180, Guid.NewGuid().ToString("N")));
        var queued = await command.ExecuteNonQueryAsync(cancellationToken) > 0;

        if (queued)
        {
            await using var mark = new NpgsqlCommand("""
                UPDATE project_intake_documents
                   SET pulse_ai_processing_status=CASE
                           WHEN pulse_ai_processing_status='ready' THEN 'ready'
                           ELSE 'queued'
                       END,
                       pulse_ai_processing_updated_at=NOW()
                 WHERE project_intake_document_id=@document_id
                   AND COALESCE(pulse_ai_processing_status,'not_requested') NOT IN (
                       'failed','rejected','quarantined','cancelled','canceled','unsupported');
                """, connection, transaction);
            mark.Parameters.AddWithValue("document_id", evidence.DocumentId);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return queued;
    }

    private static string Clean(string? value, int maximum, string fallback)
    {
        var clean = value?.Trim() ?? string.Empty;
        if (clean.Length == 0) clean = fallback;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    internal static bool IsPlanningCategory(string value)
    {
        var normalized = NormalizeCategory(value);
        return PulseAiPrivateRagPolicy.FlowHiveCategories.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            || AdditionalPlanningCategories.Contains(normalized);
    }

    internal static string NormalizeCategory(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        return normalized switch
        {
            "statementofwork" or "statement_of_work" or "sow" => "sow",
            "generalsolutiondesign" or "general_solution_design" or "global_solution_design" or "gsd" => "gsd",
            "orderform" or "order_form" => "order_form",
            "technicalspecification" or "technical_specification" => "technical_specification",
            "methodofprocedure" or "method_of_procedure" => "method_of_procedure",
            _ => normalized.Length == 0 ? "supporting" : normalized
        };
    }

    private sealed record ProjectPlanningDocumentSelection(
        IReadOnlyList<ProjectPlanningDocumentEvidence> Documents,
        ProjectPlanningDocumentEvidence? StatementOfWork,
        ProjectPlanningDocumentEvidence? GeneralSolutionDesign,
        IReadOnlyList<ProjectPlanningDocumentEvidence> SupplementalDocuments,
        IReadOnlyList<ProjectPlanningDocumentEvidence> SelectedDocuments);
}

internal sealed record ProjectPlanningDocumentEvidence(
    Guid ProjectId,
    Guid DocumentId,
    string Category,
    string FileName,
    string ProcessingStatus,
    string ProcessingErrorCode,
    Guid? ActiveVersionId,
    string AuthorityStatus,
    string IndexStatus,
    Guid? WorkRegisterDocumentId,
    string WorkRegisterDocumentType,
    string WorkRegisterStatus,
    string WorkRegisterUploadSource,
    string WorkRegisterStoredPath,
    DateTimeOffset EffectiveAt,
    DateTimeOffset UploadedAt,
    int CitationCount,
    int ScopeCitationCount)
{
    private string NormalizedCategory => ProjectPlanningDocumentResolver.NormalizeCategory(Category);
    private string NormalizedWorkRegisterType => ProjectPlanningDocumentResolver.NormalizeCategory(WorkRegisterDocumentType);

    public string CanonicalCategory => IsSow ? "sow" : IsGsd ? "gsd"
        : ProjectPlanningDocumentResolver.IsPlanningCategory(NormalizedCategory)
            ? NormalizedCategory
            : NormalizedWorkRegisterType;

    public bool IsSow => NormalizedCategory == "sow" || NormalizedWorkRegisterType == "sow";
    public bool IsGsd => NormalizedCategory == "gsd" || NormalizedWorkRegisterType == "gsd";
    public bool IsPlanningDocument => IsSow || IsGsd
        || ProjectPlanningDocumentResolver.IsPlanningCategory(NormalizedCategory)
        || ProjectPlanningDocumentResolver.IsPlanningCategory(NormalizedWorkRegisterType);

    public bool ActiveWorkRegisterSource => WorkRegisterDocumentId.HasValue
        && WorkRegisterUploadSource.Equals("local_file", StringComparison.OrdinalIgnoreCase)
        && WorkRegisterStoredPath.Trim().Length > 0
        && (WorkRegisterStatus.Trim().Length == 0
            || WorkRegisterStatus.Equals("active", StringComparison.OrdinalIgnoreCase));

    public bool DurableFileAvailable => WorkRegisterDocumentId is Guid workRegisterDocumentId
        && ProjectPulseUploadStorage.ResolveExistingStoredFile(
            WorkRegisterStoredPath,
            ProjectId,
            workRegisterDocumentId) is not null;

    public bool ProcessingReady => ProcessingStatus.Equals("ready", StringComparison.OrdinalIgnoreCase);
    public bool ProcessingTerminalFailure => ProcessingStatus.Trim().ToLowerInvariant() is
        "failed" or "rejected" or "quarantined" or "cancelled" or "canceled" or "unsupported";
    public bool ShouldAutoQueue => !ProcessingReady && !ProcessingTerminalFailure;
    public bool AuthorityReady => AuthorityStatus.Trim().ToLowerInvariant() is "approved" or "canonical";
    public bool IndexReady => IndexStatus.Trim().ToLowerInvariant() is "lexical_ready" or "embedding_ready" or "ready";
    public bool ReadyForRetrieval => ProcessingReady
        && ActiveVersionId.HasValue
        && IndexReady
        && CitationCount > 0;
    public bool ReadyForGeneration => IsSow
        ? ReadyForRetrieval && AuthorityReady && ScopeCitationCount > 0
        : ReadyForRetrieval;
}

internal sealed record ProjectPlanningDocumentResolution(
    string Contract,
    IReadOnlyList<ProjectPlanningDocumentEvidence> Documents,
    ProjectPlanningDocumentEvidence? StatementOfWork,
    ProjectPlanningDocumentEvidence? GeneralSolutionDesign,
    IReadOnlyList<ProjectPlanningDocumentEvidence> SupplementalDocuments,
    IReadOnlyList<ProjectPlanningDocumentEvidence> SelectedDocuments,
    IReadOnlyList<ProjectPlanningDocumentEvidence> PendingDocuments,
    IReadOnlyList<ProjectPlanningDocumentEvidence> FailedDocuments,
    int NewlyQueuedCount,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings)
{
    public bool HasAuthoritativeSow => StatementOfWork is not null;
    public bool HasTerminalProcessingFailure => FailedDocuments.Count > 0;
    public string TerminalDiagnosticCode => FailedDocuments
        .Select(document => document.ProcessingErrorCode)
        .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)) ?? string.Empty;
    public bool ReadyForGeneration => StatementOfWork is { ReadyForGeneration: true }
        && PendingDocuments.Count == 0
        && FailedDocuments.Count == 0;
    public IReadOnlySet<Guid> CurrentDocumentIds => SelectedDocuments
        .Where(document => document.ReadyForRetrieval)
        .Select(document => document.DocumentId)
        .ToHashSet();
}
