using Npgsql;
using NpgsqlTypes;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Called only inside an authorized document upload/association transaction.
/// Persists preparation with that association; never reads file bytes or calls a model in the request.
/// The existing private worker revalidates identity/source access at execution.
/// </summary>
internal static class ProjectPlanningDocumentPreparation
{
    internal sealed record Readiness(string Status, string Label, string Message, bool ReadyForAi,
        int ReadyCount, int TotalCount, IReadOnlyList<object> Documents);

    internal static Readiness Describe(ProjectPlanningDocumentResolution resolution, bool workerEnabled, bool archived)
    {
        var visible = resolution.SelectedDocuments.Where(d => d.EngineeringVisible).ToArray();
        var ready = visible.Count(d => d.ReadyForGeneration);
        var status = archived ? "archived"
            : resolution.StatementOfWork is null ? "missing_sow"
            : resolution.SelectedDocuments.Any(d => !d.EngineeringVisible) || resolution.HasTerminalProcessingFailure ? "needs_attention"
            : resolution.ReadyForGeneration ? "ready"
            : !workerEnabled ? "paused"
            : resolution.PendingDocuments.Any(d => d.ProcessingStatus == "not_requested") ? "waiting"
            : resolution.PendingDocuments.Count > 0 ? "preparing"
            : "needs_attention";
        var (label, message) = status switch
        {
            "archived" => ("Archived plan", "This project is closed. Its plan, versions and history remain available. Reopen it in Work Register to resume planning."),
            "ready" => ("Ready for AI", "Your current SOW and supporting documents are prepared. AI Planner can start building the work breakdown when you click it."),
            "missing_sow" => ("SOW needed", "Associate an active SOW file with this project in Work Register. A document link alone is not enough; its file must be available."),
            "paused" => ("Preparation paused", "Your documents are saved. Ask an administrator to restore private document processing; preparation will resume from the queue."),
            "waiting" => ("Ready to prepare", "Some existing documents have not entered the preparation queue yet. Click AI Planner once to prepare them and continue automatically."),
            "preparing" => ("Preparing documents", "Your documents are being checked and prepared in the background. You can leave this page. Click AI Planner once to continue automatically when they are ready."),
            _ => ("Needs attention", "A current document could not be prepared or is not approved for planning. Check the document details below and ask an administrator to resolve the issue before retrying.")
        };
        var documents = visible.Select(d => (object)new
        {
            d.DocumentId, d.FileName, category = d.IsSow ? "SOW" : d.IsGsd ? "GSD" : "Supporting document",
            status = d.ReadyForGeneration ? "Ready" : d.ProcessingTerminalFailure ? "Needs attention"
                : d.ProcessingReady ? "Review needed" : "Preparing",
            message = d.ProcessingTerminalFailure ? "Check the file or ask an administrator to retry after fixing the issue."
                : d.ProcessingReady && !d.ReadyForGeneration ? "The current version needs approval or searchable evidence before planning." : ""
        }).ToArray();
        return new(status, label, message, status == "ready", ready, visible.Length, documents);
    }

    internal static readonly string[] Categories =
    [
        "sow", "statement_of_work", "statementofwork", "gsd", "general_solution_design", "global_solution_design",
        "generalsolutiondesign", "architecture", "design", "order", "order_form", "orderform", "quote", "proposal",
        "supporting", "supporting_document", "requirements", "requirements_document", "customer_requirements",
        "technical_specification", "technical_specs", "technicalspecification", "project_charter", "implementation_plan",
        "deployment_plan", "runbook", "method_of_procedure", "methodofprocedure", "mop"
    ];

    internal static async Task<int> QueueAssociatedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        HttpContext context, Guid? projectId = null, Guid? documentId = null, CancellationToken token = default)
    {
        var actual = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseActualUserId", "ProjectPulseSessionUserId");
        var effective = ProjectPulseActualSessionAuthority.ReadUserId(context, "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId") ?? actual;
        if (!actual.HasValue || actual != effective || ProjectPulseActualSessionAuthority.IsViewAs(context)
            || (!projectId.HasValue && !documentId.HasValue)) return 0;
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        if (release.IsCandidate && !PulseAiProtectedTestCandidatePolicy.AllowsPrivateDocumentProcessing(release)) return 0;

        // Optional schema must never turn a successful document upload into a false AI-ready claim.
        await using (var schema = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='097_project_planning_identity_safe_admission');", connection, transaction))
            if (await schema.ExecuteScalarAsync(token) is not true) return 0;

        return await QueueAsync(connection, transaction, actual.Value, projectId, documentId, token);
    }

    internal static async Task<int> QueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid actor, Guid? projectId, Guid? documentId, CancellationToken token)
    {
        // Do not broaden visibility/AI consent or revive terminal jobs. New revisions are reset
        // by the existing Work Register bridge; active jobs deduplicate through its unique index.
        const string sql = """
            WITH queued AS (
                INSERT INTO pulse_ai_document_processing_jobs(
                    project_intake_document_id, project_id, actual_user_id, effective_user_id,
                    requested_by_user_id, requested_purpose, priority, correlation_id)
                SELECT d.project_intake_document_id, d.project_id, @actor, @actor, @actor,
                       'project_document_associated', 90, @correlation
                  FROM project_intake_documents d
                  JOIN projects p ON p.project_id=d.project_id
                  JOIN app_users actor ON actor.user_id=@actor AND actor.is_active=TRUE
                 WHERE (@project::uuid IS NULL OR d.project_id=@project)
                   AND (@document::uuid IS NULL OR d.project_intake_document_id=@document)
                   AND lower(trim(COALESCE(p.status,''))) NOT IN ('closed','completed','cancelled','canceled','archived')
                   AND d.is_active=TRUE AND d.engineering_visible=TRUE AND d.ai_timesheet_context_enabled=TRUE
                   AND COALESCE(d.pulse_ai_processing_status,'not_requested')='not_requested'
                   AND replace(replace(lower(trim(COALESCE(d.document_category,d.document_type,''))),'-','_'),' ','_')=ANY(@categories)
                   AND COALESCE(d.upload_source,'')<>'celar_ai_chat_attachment'
                ON CONFLICT (project_intake_document_id)
                    WHERE job_status IN ('queued','scanning','extracting','awaiting_ocr','embedding','indexing','retry_wait','cancel_requested')
                DO NOTHING
                RETURNING pulse_ai_document_processing_job_id, project_intake_document_id, project_id
            ), marked AS (
                UPDATE project_intake_documents d
                   SET pulse_ai_processing_status='queued', pulse_ai_processing_error_code='', pulse_ai_processing_updated_at=NOW()
                  FROM queued q WHERE d.project_intake_document_id=q.project_intake_document_id
                RETURNING d.project_intake_document_id
            ), audited AS (
                INSERT INTO pulse_ai_document_processing_events(
                    pulse_ai_document_processing_job_id, project_intake_document_id, project_id,
                    actual_user_id, effective_user_id, event_code, event_status, correlation_id, diagnostic_code, evidence_json)
                SELECT pulse_ai_document_processing_job_id, project_intake_document_id, project_id,
                       @actor, @actor, 'document_association_queued', 'requested', @correlation, '',
                       '{"admission":"authorized_document_association","externalProviderCalled":false,"rawDocumentTextLogged":false}'::jsonb
                  FROM queued
            )
            SELECT COUNT(*)::int FROM marked;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.Add("project", NpgsqlDbType.Uuid).Value = (object?)projectId ?? DBNull.Value;
        command.Parameters.Add("document", NpgsqlDbType.Uuid).Value = (object?)documentId ?? DBNull.Value;
        command.Parameters.AddWithValue("categories", Categories);
        command.Parameters.AddWithValue("correlation", $"association-{Guid.NewGuid():N}");
        return (int)(await command.ExecuteScalarAsync(token) ?? 0);
    }
}
