using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed class PulseAiDocumentGroundingService
{
    private static readonly HashSet<string> BroadDocumentRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUPER_ADMINISTRATOR",
        "ADMINISTRATOR",
        "PROJECT_TEAM_COORDINATOR",
        "EXECUTIVE"
    };

    private static readonly HashSet<string> ProjectManagementRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PROJECT_MANAGER",
        "PROJECT_MANAGEMENT",
        "PROJECT_MANAGEMENT_LEAD",
        "PROJECT_MANAGEMENT_TEAM_LEAD",
        "PM_TEAM_LEAD"
    };

    private static readonly IReadOnlyDictionary<string, string[]> ThemeKeywords =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["design and architecture"] = ["design", "architecture", "topology", "diagram", "solution design"],
            ["configuration and implementation"] = ["configure", "configuration", "implementation", "build", "provision"],
            ["migration, upgrade, and cutover"] = ["migration", "migrate", "upgrade", "cutover", "transition"],
            ["testing and validation"] = ["test", "testing", "validate", "validation", "verification", "uat", "acceptance"],
            ["documentation and operational handoff"] = ["document", "documentation", "runbook", "as-built", "handoff", "knowledge transfer"],
            ["integration and interoperability"] = ["integration", "interface", "api", "interoperability", "connector", "sync"],
            ["security and compliance"] = ["security", "compliance", "encryption", "certificate", "authentication", "authorization"],
            ["network and connectivity"] = ["network", "routing", "firewall", "dns", "connectivity", "vlan", "wan", "lan"],
            ["collaboration, voice, and contact center"] = ["cisco", "webex", "cucm", "uccx", "ucce", "voice", "contact center", "calling"],
            ["infrastructure, cloud, and resilience"] = ["server", "vmware", "storage", "backup", "recovery", "replication", "azure", "cloud", "container"],
            ["support and troubleshooting"] = ["support", "troubleshoot", "incident", "issue", "remediation", "diagnostic", "root cause"],
            ["project governance and dependencies"] = ["dependency", "milestone", "risk", "assumption", "constraint", "prerequisite", "change control"]
        };

    private readonly ILogger<PulseAiDocumentGroundingService> _logger;

    public PulseAiDocumentGroundingService(ILogger<PulseAiDocumentGroundingService> logger)
    {
        _logger = logger;
    }

    public Task<PulseAiGroundingContext> BuildTimesheetContextAsync(
        Guid effectiveUserId,
        PulseAiTimesheetGroundingInput input,
        CancellationToken cancellationToken = default) =>
        BuildContextAsync(
            effectiveUserId: effectiveUserId,
            purpose: "timesheet_document_grounding",
            projectId: input.ProjectId,
            taskId: input.TaskId,
            assignmentId: input.AssignmentId,
            workDate: input.WorkDate,
            projectCode: input.ProjectCode,
            projectName: input.ProjectName,
            taskCode: input.TaskCode,
            taskName: input.TaskName,
            rowLabel: input.RowLabel,
            roughNote: input.CurrentDescription,
            requireTimesheetContextFlag: true,
            cancellationToken: cancellationToken);

    public Task<PulseAiGroundingContext> BuildFlowHiveContextAsync(
        Guid effectiveUserId,
        PulseAiFlowHiveGroundingInput input,
        CancellationToken cancellationToken = default) =>
        BuildContextAsync(
            effectiveUserId: effectiveUserId,
            purpose: "flowhive_document_planning",
            projectId: null,
            taskId: null,
            assignmentId: null,
            workDate: null,
            projectCode: input.ProjectCode,
            projectName: input.ProjectName,
            taskCode: null,
            taskName: null,
            rowLabel: null,
            roughNote: input.RequestedOutcome,
            requireTimesheetContextFlag: false,
            cancellationToken: cancellationToken);

    public async Task<PulseAiPrivateRuntimeReadiness> GetReadinessAsync(
        Guid effectiveUserId,
        CancellationToken cancellationToken = default)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var missingConfiguration = MissingDatabaseConfiguration();
        var privateInference = HasValue("PROJECTPULSE_PRIVATE_INFERENCE_ENDPOINT")
            && HasValue("PROJECTPULSE_PRIVATE_INFERENCE_MODEL");
        var privateEmbedding = HasValue("PROJECTPULSE_PRIVATE_EMBEDDING_ENDPOINT")
            && HasValue("PROJECTPULSE_PRIVATE_EMBEDDING_MODEL");
        var privateVectorIndex = HasValue("PROJECTPULSE_PRIVATE_VECTOR_INDEX");
        var sanitizedEscalation = Boolean("PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION", false);

        if (missingConfiguration.Count > 0)
        {
            return new PulseAiPrivateRuntimeReadiness(
                Status: "database_configuration_missing",
                DatabaseConfigured: false,
                DocumentTableAvailable: false,
                EngineeringVisibilityAvailable: false,
                TimesheetContextFlagAvailable: false,
                ExtractionStatusAvailable: false,
                ContextSummaryAvailable: false,
                ContextProcessedAtAvailable: false,
                PrivateInferenceEndpointConfigured: privateInference,
                PrivateEmbeddingEndpointConfigured: privateEmbedding,
                PrivateVectorIndexConfigured: privateVectorIndex,
                SanitizedExternalEscalationEnabled: sanitizedEscalation,
                AuthorizedDocumentCount: 0,
                AuthorizedAiContextDocumentCount: 0,
                AuthorizedReadyContextDocumentCount: 0,
                ReadyCapabilities: ReadyCapabilities(privateInference, privateEmbedding, privateVectorIndex),
                Blockers:
                [
                    "ProjectPulse database configuration is required before document readiness can be evaluated.",
                    "No raw document or database content was read."
                ],
                MissingConfiguration: missingConfiguration,
                GeneratedAt: generatedAt);
        }

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            var schema = await InspectDocumentSchemaAsync(connection, cancellationToken);

            if (!schema.TableAvailable)
            {
                return new PulseAiPrivateRuntimeReadiness(
                    Status: "document_schema_unavailable",
                    DatabaseConfigured: true,
                    DocumentTableAvailable: false,
                    EngineeringVisibilityAvailable: false,
                    TimesheetContextFlagAvailable: false,
                    ExtractionStatusAvailable: false,
                    ContextSummaryAvailable: false,
                    ContextProcessedAtAvailable: false,
                    PrivateInferenceEndpointConfigured: privateInference,
                    PrivateEmbeddingEndpointConfigured: privateEmbedding,
                    PrivateVectorIndexConfigured: privateVectorIndex,
                    SanitizedExternalEscalationEnabled: sanitizedEscalation,
                    AuthorizedDocumentCount: 0,
                    AuthorizedAiContextDocumentCount: 0,
                    AuthorizedReadyContextDocumentCount: 0,
                    ReadyCapabilities: ReadyCapabilities(privateInference, privateEmbedding, privateVectorIndex),
                    Blockers:
                    [
                        "The project_intake_documents source is not available.",
                        "Document extraction, retrieval, and AI grounding remain unavailable."
                    ],
                    MissingConfiguration: [],
                    GeneratedAt: generatedAt);
            }

            var access = await LoadAccessAsync(connection, effectiveUserId, cancellationToken);
            if (!access.IsActive)
            {
                return new PulseAiPrivateRuntimeReadiness(
                    Status: "effective_user_unavailable",
                    DatabaseConfigured: true,
                    DocumentTableAvailable: true,
                    EngineeringVisibilityAvailable: schema.EngineeringVisible,
                    TimesheetContextFlagAvailable: schema.AiTimesheetContextEnabled,
                    ExtractionStatusAvailable: schema.ExtractionStatus,
                    ContextSummaryAvailable: schema.ContextSummary,
                    ContextProcessedAtAvailable: schema.ContextProcessedAt,
                    PrivateInferenceEndpointConfigured: privateInference,
                    PrivateEmbeddingEndpointConfigured: privateEmbedding,
                    PrivateVectorIndexConfigured: privateVectorIndex,
                    SanitizedExternalEscalationEnabled: sanitizedEscalation,
                    AuthorizedDocumentCount: 0,
                    AuthorizedAiContextDocumentCount: 0,
                    AuthorizedReadyContextDocumentCount: 0,
                    ReadyCapabilities: ReadyCapabilities(privateInference, privateEmbedding, privateVectorIndex),
                    Blockers: ["The current effective user could not be resolved for permission-aware document access."],
                    MissingConfiguration: [],
                    GeneratedAt: generatedAt);
            }

            var counts = await CountAuthorizedDocumentsAsync(connection, access, schema, cancellationToken);
            var blockers = new List<string>();
            if (!schema.EngineeringVisible) blockers.Add("engineering_visible is not available on project documents.");
            if (!schema.AiTimesheetContextEnabled) blockers.Add("ai_timesheet_context_enabled is not available on project documents.");
            if (!schema.ExtractionStatus) blockers.Add("extraction_status is not available on project documents.");
            if (!schema.ContextSummary) blockers.Add("ai_context_summary is not available; private grounding cannot consume extracted document meaning.");
            if (!privateInference) blockers.Add("A private inference endpoint and model are not configured. Raw internal context cannot be sent to remote providers.");
            if (!privateEmbedding) blockers.Add("A private embedding endpoint and model are not configured.");
            if (!privateVectorIndex) blockers.Add("A private permission-scoped vector index is not configured.");

            return new PulseAiPrivateRuntimeReadiness(
                Status: blockers.Count == 0 ? "private_runtime_ready" : "private_runtime_partially_ready",
                DatabaseConfigured: true,
                DocumentTableAvailable: true,
                EngineeringVisibilityAvailable: schema.EngineeringVisible,
                TimesheetContextFlagAvailable: schema.AiTimesheetContextEnabled,
                ExtractionStatusAvailable: schema.ExtractionStatus,
                ContextSummaryAvailable: schema.ContextSummary,
                ContextProcessedAtAvailable: schema.ContextProcessedAt,
                PrivateInferenceEndpointConfigured: privateInference,
                PrivateEmbeddingEndpointConfigured: privateEmbedding,
                PrivateVectorIndexConfigured: privateVectorIndex,
                SanitizedExternalEscalationEnabled: sanitizedEscalation,
                AuthorizedDocumentCount: counts.All,
                AuthorizedAiContextDocumentCount: counts.AiEnabled,
                AuthorizedReadyContextDocumentCount: counts.Ready,
                ReadyCapabilities: ReadyCapabilities(privateInference, privateEmbedding, privateVectorIndex),
                Blockers: blockers,
                MissingConfiguration: [],
                GeneratedAt: generatedAt);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI private runtime readiness failed without exposing database details.");

            return new PulseAiPrivateRuntimeReadiness(
                Status: "private_runtime_readiness_unavailable",
                DatabaseConfigured: true,
                DocumentTableAvailable: false,
                EngineeringVisibilityAvailable: false,
                TimesheetContextFlagAvailable: false,
                ExtractionStatusAvailable: false,
                ContextSummaryAvailable: false,
                ContextProcessedAtAvailable: false,
                PrivateInferenceEndpointConfigured: privateInference,
                PrivateEmbeddingEndpointConfigured: privateEmbedding,
                PrivateVectorIndexConfigured: privateVectorIndex,
                SanitizedExternalEscalationEnabled: sanitizedEscalation,
                AuthorizedDocumentCount: 0,
                AuthorizedAiContextDocumentCount: 0,
                AuthorizedReadyContextDocumentCount: 0,
                ReadyCapabilities: ReadyCapabilities(privateInference, privateEmbedding, privateVectorIndex),
                Blockers: ["Document readiness could not be evaluated. No values were fabricated."],
                MissingConfiguration: [],
                GeneratedAt: generatedAt,
                DiagnosticCode: Diagnostic(exception));
        }
    }

    private async Task<PulseAiGroundingContext> BuildContextAsync(
        Guid effectiveUserId,
        string purpose,
        Guid? projectId,
        Guid? taskId,
        Guid? assignmentId,
        DateOnly? workDate,
        string? projectCode,
        string? projectName,
        string? taskCode,
        string? taskName,
        string? rowLabel,
        string? roughNote,
        bool requireTimesheetContextFlag,
        CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var missingConfiguration = MissingDatabaseConfiguration();
        if (missingConfiguration.Count > 0)
        {
            return EmptyContext(
                "database_configuration_missing",
                purpose,
                effectiveUserId,
                projectCode,
                projectName,
                ["ProjectPulse database configuration is incomplete."],
                diagnosticCode: "database_configuration_missing");
        }

        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            var schema = await InspectDocumentSchemaAsync(connection, cancellationToken);
            var access = await LoadAccessAsync(connection, effectiveUserId, cancellationToken);

            if (!access.IsActive)
            {
                return EmptyContext(
                    "effective_user_unavailable",
                    purpose,
                    effectiveUserId,
                    projectCode,
                    projectName,
                    ["The current effective user could not be resolved."],
                    diagnosticCode: "effective_user_unavailable");
            }

            var project = await ResolveProjectAsync(
                connection,
                projectId,
                taskId,
                assignmentId,
                projectCode,
                projectName,
                cancellationToken);

            if (project is null)
            {
                return EmptyContext(
                    "project_not_resolved",
                    purpose,
                    effectiveUserId,
                    projectCode,
                    projectName,
                    ["A unique ProjectPulse project could not be resolved from the supplied project code or name."],
                    roles: access.RoleCodes,
                    accessScope: access.ScopeLabel);
            }

            var authorized = await CanAccessProjectAsync(
                connection,
                access,
                project.ProjectId,
                cancellationToken);

            if (!authorized)
            {
                return new PulseAiGroundingContext(
                    Status: "project_outside_effective_user_scope",
                    Purpose: purpose,
                    EffectiveUserId: effectiveUserId,
                    ProjectId: project.ProjectId,
                    ProjectCode: project.ProjectCode,
                    ProjectName: project.ProjectName,
                    CustomerName: null,
                    ProjectStatus: null,
                    TaskCode: null,
                    TaskName: null,
                    TaskDescription: null,
                    RequestNumber: null,
                    RequestFunction: null,
                    RequestStatus: null,
                    AccessScope: access.ScopeLabel,
                    ProjectResolved: true,
                    Authorized: false,
                    RoleCodes: access.RoleCodes.OrderBy(value => value).ToArray(),
                    Documents: [],
                    ScopeThemes: [],
                    MissingInputs: ["The project is outside the current effective user’s authorized project scope."],
                    Conflicts: [],
                    CoverageScore: 0,
                    CoverageLevel: "blocked",
                    GeneratedAt: generatedAt,
                    PrivacyBoundary: "private_projectpulse_runtime_only",
                    ExternalProviderPolicy: "no_context_retrieval_no_external_escalation");
            }

            var task = await ResolveTaskAsync(
                connection,
                access,
                project.ProjectId,
                workDate,
                taskId,
                assignmentId,
                taskCode,
                taskName,
                cancellationToken);
            if ((taskId is not null || assignmentId is not null) && task is null)
            {
                return EmptyContext(
                    "task_or_assignment_not_resolved",
                    purpose,
                    effectiveUserId,
                    project.ProjectCode,
                    project.ProjectName,
                    ["The selected task or assignment could not be resolved inside the authorized project and effective-user scope."],
                    roles: access.RoleCodes,
                    accessScope: access.ScopeLabel,
                    diagnosticCode: "task_or_assignment_not_resolved");
            }
            var request = await ResolveRequestAsync(
                connection,
                project.ProjectId,
                rowLabel,
                cancellationToken);
            var documents = schema.TableAvailable
                ? await LoadDocumentsAsync(
                    connection,
                    project,
                    schema,
                    requireTimesheetContextFlag,
                    cancellationToken)
                : [];

            var themes = ExtractThemes(documents, task, request, roughNote);
            var missing = BuildMissingInputs(
                purpose,
                taskCode,
                taskName,
                rowLabel,
                roughNote,
                task,
                request,
                documents,
                schema,
                requireTimesheetContextFlag);
            var conflicts = BuildConflicts(projectCode, projectName, project, documents);
            var coverage = Coverage(task, request, roughNote, documents);

            return new PulseAiGroundingContext(
                Status: documents.Any(document => document.SummaryReady)
                    ? "private_document_context_ready"
                    : documents.Count > 0
                        ? "documents_found_context_not_ready"
                        : "authorized_project_no_eligible_documents",
                Purpose: purpose,
                EffectiveUserId: effectiveUserId,
                ProjectId: project.ProjectId,
                ProjectCode: project.ProjectCode,
                ProjectName: project.ProjectName,
                CustomerName: project.CustomerName,
                ProjectStatus: project.Status,
                TaskCode: task?.TaskCode,
                TaskName: task?.TaskName,
                TaskDescription: task?.TaskDescription,
                RequestNumber: request?.RequestNumber,
                RequestFunction: request?.RequestedFunction,
                RequestStatus: request?.Status,
                AccessScope: access.ScopeLabel,
                ProjectResolved: true,
                Authorized: true,
                RoleCodes: access.RoleCodes.OrderBy(value => value).ToArray(),
                Documents: documents,
                ScopeThemes: themes,
                MissingInputs: missing,
                Conflicts: conflicts,
                CoverageScore: coverage.Score,
                CoverageLevel: coverage.Level,
                GeneratedAt: generatedAt,
                PrivacyBoundary: "private_projectpulse_runtime_only",
                ExternalProviderPolicy: documents.Count > 0
                    ? "raw_document_and_summary_context_not_sent_to_claude_or_openai"
                    : "existing_non_document_provider_route_may_apply",
                DiagnosticCode: schema.TableAvailable ? null : "document_table_unavailable",
                TaskId: task?.TaskId,
                AssignmentId: task?.AssignmentId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Pulse AI document grounding failed without exposing source details. Purpose={Purpose}",
                purpose);

            return EmptyContext(
                "document_grounding_unavailable",
                purpose,
                effectiveUserId,
                projectCode,
                projectName,
                ["Authorized document grounding could not be completed. The response contains no fabricated document evidence."],
                diagnosticCode: Diagnostic(exception));
        }
    }

    private static async Task<DocumentSchema> InspectDocumentSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var tableAvailable = false;
        await using (var tableCommand = new NpgsqlCommand(
            "SELECT to_regclass('public.project_intake_documents') IS NOT NULL;",
            connection))
        {
            tableAvailable = Convert.ToBoolean(await tableCommand.ExecuteScalarAsync(cancellationToken));
        }

        if (!tableAvailable) return DocumentSchema.Missing;

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new NpgsqlCommand("""
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'project_intake_documents';
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return new DocumentSchema(
            TableAvailable: true,
            EngineeringVisible: columns.Contains("engineering_visible"),
            AiTimesheetContextEnabled: columns.Contains("ai_timesheet_context_enabled"),
            ExtractionStatus: columns.Contains("extraction_status"),
            ContextSummary: columns.Contains("ai_context_summary"),
            ContextProcessedAt: columns.Contains("ai_context_last_processed_at"),
            DocumentCategory: columns.Contains("document_category"),
            ProjectId: columns.Contains("project_id"),
            ContentType: columns.Contains("content_type"),
            SizeBytes: columns.Contains("size_bytes"),
            UploadSource: columns.Contains("upload_source"));
    }

    private static async Task<AccessContext> LoadAccessAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                u.user_id,
                COALESCE(u.display_name, ''),
                COALESCE(u.email, ''),
                COALESCE(u.team_name, ''),
                COALESCE(u.department_name, ''),
                COALESCE(u.department, ''),
                COALESCE(string_agg(DISTINCT r.role_code, ',' ORDER BY r.role_code), ''),
                COALESCE(u.is_active, FALSE)
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
                ON ura.user_id = u.user_id
               AND ura.is_active = TRUE
            LEFT JOIN app_roles r
                ON r.app_role_id = ura.app_role_id
               AND r.is_active = TRUE
            WHERE u.user_id = @user_id
            GROUP BY
                u.user_id,
                u.display_name,
                u.email,
                u.team_name,
                u.department_name,
                u.department,
                u.is_active;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return AccessContext.Empty(userId);

        var roles = reader.GetString(6)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new AccessContext(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            roles,
            reader.GetBoolean(7));
    }

    private static async Task<ProjectContext?> ResolveProjectAsync(
        NpgsqlConnection connection,
        Guid? projectId,
        Guid? taskId,
        Guid? assignmentId,
        string? projectCode,
        string? projectName,
        CancellationToken cancellationToken)
    {
        var code = Clean(projectCode, 100);
        var name = Clean(projectName, 255);
        if (projectId is null
            && taskId is null
            && assignmentId is null
            && string.IsNullOrWhiteSpace(code)
            && string.IsNullOrWhiteSpace(name)) return null;

        const string sql = """
            SELECT
                p.project_id,
                p.project_code,
                p.project_name,
                COALESCE(c.client_name, 'No customer'),
                p.status,
                p.project_manager_user_id
            FROM projects p
            LEFT JOIN clients c ON c.client_id = p.client_id
            WHERE (
                (
                    (@project_id IS NOT NULL OR @task_id IS NOT NULL OR @assignment_id IS NOT NULL)
                    AND (@project_id IS NULL OR p.project_id = @project_id)
                    AND (
                        @task_id IS NULL
                        OR EXISTS (
                            SELECT 1 FROM project_tasks identity_task
                            WHERE identity_task.task_id = @task_id
                              AND identity_task.project_id = p.project_id
                              AND identity_task.is_active = TRUE
                        )
                    )
                    AND (
                        @assignment_id IS NULL
                        OR EXISTS (
                            SELECT 1 FROM project_assignments identity_assignment
                            WHERE identity_assignment.project_assignment_id = @assignment_id
                              AND identity_assignment.project_id = p.project_id
                              AND (@task_id IS NULL OR identity_assignment.task_id = @task_id)
                        )
                    )
                )
                OR (
                    @project_id IS NULL
                    AND @task_id IS NULL
                    AND @assignment_id IS NULL
                    AND (
                        (@project_code <> '' AND (LOWER(p.project_code) = LOWER(@project_code) OR p.project_id = projectpulse_resolve_project_id(@project_code)))
                        OR (@project_name <> '' AND LOWER(p.project_name) = LOWER(@project_name))
                    )
                )
            )
            ORDER BY
                CASE
                    WHEN @project_id IS NOT NULL AND p.project_id = @project_id THEN 0
                    WHEN @assignment_id IS NOT NULL THEN 1
                    WHEN @task_id IS NOT NULL THEN 2
                    WHEN @project_code <> '' AND (LOWER(p.project_code) = LOWER(@project_code) OR p.project_id = projectpulse_resolve_project_id(@project_code)) THEN 3
                    ELSE 4
                END,
                p.updated_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("project_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
            projectId is Guid canonicalProjectId ? canonicalProjectId : DBNull.Value;
        command.Parameters.Add("task_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
            taskId is Guid canonicalTaskId ? canonicalTaskId : DBNull.Value;
        command.Parameters.Add("assignment_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
            assignmentId is Guid canonicalAssignmentId ? canonicalAssignmentId : DBNull.Value;
        command.Parameters.AddWithValue("project_code", code);
        command.Parameters.AddWithValue("project_name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new ProjectContext(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5));
    }

    private static async Task<bool> CanAccessProjectAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (access.IsBroadDocumentScope) return true;

        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM projects p
                WHERE p.project_id = @project_id
                  AND (
                      p.project_manager_user_id = @user_id
                      OR (@is_pm_lead = TRUE AND (
                          EXISTS (
                              SELECT 1 FROM reporting_relationships rr
                              WHERE rr.employee_user_id = p.project_manager_user_id
                                AND (rr.manager_user_id = @user_id OR rr.team_lead_user_id = @user_id)
                                AND rr.effective_start_date <= CURRENT_DATE
                                AND (rr.effective_end_date IS NULL OR rr.effective_end_date >= CURRENT_DATE)
                          )
                          OR EXISTS (
                              SELECT 1
                              FROM app_users pm
                              JOIN projectpulse_team_scope_assignments scope ON scope.scoped_user_id = @user_id
                              WHERE pm.user_id = p.project_manager_user_id
                                AND scope.is_active = TRUE
                                AND scope.scope_type = 'project_management_team_lead'
                                AND (
                                    (scope.team_name IS NOT NULL AND LOWER(COALESCE(pm.team_name,'')) = LOWER(scope.team_name))
                                    OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(pm.department_name,'')) = LOWER(scope.department_name))
                                    OR scope.manager_user_id = pm.user_id
                                )
                          )
                      ))
                      OR EXISTS (
                          SELECT 1
                          FROM project_assignments pa
                          WHERE pa.project_id = p.project_id
                            AND pa.user_id = @user_id
                      )
                      OR EXISTS (
                          SELECT 1
                          FROM engineering_resource_requests err
                          WHERE err.project_id = p.project_id
                            AND (
                                err.fulfilled_by_user_id = @user_id
                                OR err.assigned_pm_user_id = @user_id
                                OR EXISTS (
                                    SELECT 1
                                    FROM engineering_resource_request_assignments erra
                                    WHERE erra.engineering_resource_request_id = err.engineering_resource_request_id
                                      AND erra.user_id = @user_id
                                )
                            )
                      )
                  )
            );
            """;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("user_id", access.UserId);
            command.Parameters.AddWithValue("is_pm_lead", access.IsProjectManagementLead);
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            const string fallback = """
                SELECT EXISTS (
                    SELECT 1
                    FROM projects p
                    WHERE p.project_id = @project_id
                      AND (
                          p.project_manager_user_id = @user_id
                          OR EXISTS (
                              SELECT 1
                              FROM project_assignments pa
                              WHERE pa.project_id = p.project_id
                                AND pa.user_id = @user_id
                          )
                      )
                );
                """;
            await using var command = new NpgsqlCommand(fallback, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("user_id", access.UserId);
            return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
        }
    }

    private static async Task<TaskContext?> ResolveTaskAsync(
        NpgsqlConnection connection,
        AccessContext access,
        Guid projectId,
        DateOnly? workDate,
        Guid? taskId,
        Guid? assignmentId,
        string? taskCode,
        string? taskName,
        CancellationToken cancellationToken)
    {
        var code = Clean(taskCode, 100);
        var name = Clean(taskName, 255);
        if (taskId is null
            && assignmentId is null
            && string.IsNullOrWhiteSpace(code)
            && string.IsNullOrWhiteSpace(name)) return null;

        const string sql = """
            SELECT
                task.task_id,
                task.task_code,
                task.task_name,
                task.task_description,
                assignment.project_assignment_id
            FROM project_tasks task
            LEFT JOIN LATERAL (
                SELECT pa.project_assignment_id
                FROM project_assignments pa
                WHERE pa.project_id = task.project_id
                  AND pa.task_id = task.task_id
                  AND (@assignment_id IS NULL OR pa.project_assignment_id = @assignment_id)
                  AND (@can_view_any_assignment = TRUE OR pa.user_id = @user_id)
                  AND pa.effective_start_date <= @effective_date
                  AND (pa.effective_end_date IS NULL OR pa.effective_end_date >= @effective_date)
                ORDER BY pa.effective_start_date DESC, pa.project_assignment_id
                LIMIT 1
            ) assignment ON TRUE
            WHERE task.project_id = @project_id
              AND task.is_active = TRUE
              AND (
                  (@assignment_id IS NOT NULL AND assignment.project_assignment_id IS NOT NULL)
                  OR (@assignment_id IS NULL AND @task_id IS NOT NULL AND task.task_id = @task_id)
                  OR (
                      @assignment_id IS NULL
                      AND @task_id IS NULL
                      AND (
                          (@task_code <> '' AND LOWER(task.task_code) = LOWER(@task_code))
                          OR (@task_name <> '' AND LOWER(task.task_name) = LOWER(@task_name))
                      )
                  )
              )
            ORDER BY
                CASE
                    WHEN @assignment_id IS NOT NULL THEN 0
                    WHEN @task_id IS NOT NULL AND task.task_id = @task_id THEN 1
                    WHEN @task_code <> '' AND LOWER(task.task_code) = LOWER(@task_code) THEN 2
                    ELSE 3
                END,
                task.updated_at DESC
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", projectId);
        command.Parameters.Add("task_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
            taskId is Guid canonicalTaskId ? canonicalTaskId : DBNull.Value;
        command.Parameters.Add("assignment_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
            assignmentId is Guid canonicalAssignmentId ? canonicalAssignmentId : DBNull.Value;
        command.Parameters.AddWithValue("can_view_any_assignment", access.IsBroadDocumentScope || access.IsProjectManager);
        command.Parameters.AddWithValue("user_id", access.UserId);
        command.Parameters.AddWithValue("effective_date", workDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
        command.Parameters.AddWithValue("task_code", code);
        command.Parameters.AddWithValue("task_name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new TaskContext(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4));
    }

    private static async Task<RequestContext?> ResolveRequestAsync(
        NpgsqlConnection connection,
        Guid projectId,
        string? rowLabel,
        CancellationToken cancellationToken)
    {
        var label = Clean(rowLabel, 300);
        if (string.IsNullOrWhiteSpace(label)) return null;

        const string sql = """
            SELECT request_number, requested_function, request_status, skill_requirements, assignment_notes
            FROM engineering_resource_requests
            WHERE project_id = @project_id
              AND (
                  LOWER(request_number) = LOWER(@label)
                  OR LOWER(requested_function) = LOWER(@label)
                  OR POSITION(LOWER(request_number) IN LOWER(@label)) > 0
              )
            ORDER BY
                CASE WHEN LOWER(request_number) = LOWER(@label) THEN 0 ELSE 1 END,
                updated_at DESC
            LIMIT 1;
            """;

        try
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("project_id", projectId);
            command.Parameters.AddWithValue("label", label);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;

            return new RequestContext(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }
        catch (PostgresException exception) when (exception.SqlState == "42P01")
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<PulseAiGroundingDocument>> LoadDocumentsAsync(
        NpgsqlConnection connection,
        ProjectContext project,
        DocumentSchema schema,
        bool requireTimesheetContextFlag,
        CancellationToken cancellationToken)
    {
        if (!schema.ProjectId) return [];
        if (!schema.EngineeringVisible) return [];
        if (requireTimesheetContextFlag && !schema.AiTimesheetContextEnabled) return [];

        var category = schema.DocumentCategory
            ? "COALESCE(d.document_category, d.document_type, 'supporting')"
            : "COALESCE(d.document_type, 'supporting')";
        var contentType = schema.ContentType ? "d.content_type" : "NULL::text";
        var sizeBytes = schema.SizeBytes ? "COALESCE(d.size_bytes, 0)::bigint" : "0::bigint";
        var extractionStatus = schema.ExtractionStatus
            ? "COALESCE(d.extraction_status, 'not_started')"
            : "'not_available'::text";
        var contextSummary = schema.ContextSummary ? "d.ai_context_summary" : "NULL::text";
        var contextProcessed = schema.ContextProcessedAt
            ? "d.ai_context_last_processed_at"
            : "NULL::timestamptz";
        var uploadSource = schema.UploadSource
            ? "COALESCE(d.upload_source, 'manual')"
            : "'manual'::text";
        var timesheetFlag = schema.AiTimesheetContextEnabled
            ? "COALESCE(d.ai_timesheet_context_enabled, FALSE)"
            : "FALSE";
        var timesheetPredicate = requireTimesheetContextFlag
            ? "AND COALESCE(d.ai_timesheet_context_enabled, FALSE) = TRUE"
            : string.Empty;

        var sql = $"""
            SELECT
                d.project_intake_document_id,
                d.project_id,
                p.project_code,
                p.project_name,
                COALESCE(d.document_type, 'supporting'),
                {category},
                d.original_file_name,
                {contentType},
                {sizeBytes},
                COALESCE(d.engineering_visible, FALSE),
                {timesheetFlag},
                {extractionStatus},
                {contextSummary},
                {contextProcessed},
                d.uploaded_at,
                {uploadSource},
                CASE LOWER({category})
                    WHEN 'sow' THEN 10
                    WHEN 'statement_of_work' THEN 10
                    WHEN 'gsd' THEN 20
                    WHEN 'global_solution_design' THEN 20
                    WHEN 'architecture' THEN 30
                    WHEN 'design' THEN 30
                    WHEN 'order' THEN 40
                    WHEN 'quote' THEN 50
                    WHEN 'proposal' THEN 50
                    ELSE 90
                END AS retrieval_priority
            FROM project_intake_documents d
            JOIN projects p ON p.project_id = d.project_id
            WHERE d.is_active = TRUE
              AND d.project_id = @project_id
              AND COALESCE(d.engineering_visible, FALSE) = TRUE
              {timesheetPredicate}
            ORDER BY retrieval_priority, d.uploaded_at DESC
            LIMIT 40;
            """;

        var documents = new List<PulseAiGroundingDocument>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("project_id", project.ProjectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new PulseAiGroundingDocument(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5).Trim().ToLowerInvariant(),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt64(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetString(11).Trim().ToLowerInvariant(),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
                reader.GetFieldValue<DateTimeOffset>(14),
                reader.GetString(15),
                reader.GetInt32(16)));
        }

        return documents;
    }

    private static async Task<DocumentCounts> CountAuthorizedDocumentsAsync(
        NpgsqlConnection connection,
        AccessContext access,
        DocumentSchema schema,
        CancellationToken cancellationToken)
    {
        if (!schema.TableAvailable || !schema.ProjectId || !schema.EngineeringVisible)
        {
            return new DocumentCounts(0, 0, 0);
        }

        var aiEnabled = schema.AiTimesheetContextEnabled
            ? "COALESCE(d.ai_timesheet_context_enabled, FALSE)"
            : "FALSE";
        var ready = schema.ContextSummary && schema.ExtractionStatus
            ? "(NULLIF(BTRIM(d.ai_context_summary), '') IS NOT NULL AND LOWER(COALESCE(d.extraction_status, '')) IN ('completed','ready','indexed','processed'))"
            : "FALSE";

        var sql = $"""
            SELECT
                COUNT(*)::bigint,
                COUNT(*) FILTER (WHERE {aiEnabled})::bigint,
                COUNT(*) FILTER (WHERE {ready})::bigint
            FROM project_intake_documents d
            JOIN projects p ON p.project_id = d.project_id
            WHERE d.is_active = TRUE
              AND COALESCE(d.engineering_visible, FALSE) = TRUE
              AND (
                  @is_broad = TRUE
                  OR (@is_pm_lead = TRUE AND (
                      EXISTS (
                          SELECT 1 FROM reporting_relationships rr
                          WHERE rr.employee_user_id = p.project_manager_user_id
                            AND (rr.manager_user_id = @user_id OR rr.team_lead_user_id = @user_id)
                            AND rr.effective_start_date <= CURRENT_DATE
                            AND (rr.effective_end_date IS NULL OR rr.effective_end_date >= CURRENT_DATE)
                      )
                      OR EXISTS (
                          SELECT 1
                          FROM app_users pm
                          JOIN projectpulse_team_scope_assignments scope ON scope.scoped_user_id = @user_id
                          WHERE pm.user_id = p.project_manager_user_id
                            AND scope.is_active = TRUE
                            AND scope.scope_type = 'project_management_team_lead'
                            AND (
                                (scope.team_name IS NOT NULL AND LOWER(COALESCE(pm.team_name,'')) = LOWER(scope.team_name))
                                OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(pm.department_name,'')) = LOWER(scope.department_name))
                                OR scope.manager_user_id = pm.user_id
                            )
                      )
                  ))
                  OR p.project_manager_user_id = @user_id
                  OR EXISTS (
                      SELECT 1
                      FROM project_assignments pa
                      WHERE pa.project_id = p.project_id
                        AND pa.user_id = @user_id
                  )
              );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("is_broad", access.IsBroadDocumentScope);
        command.Parameters.AddWithValue("is_pm_lead", access.IsProjectManagementLead);
        command.Parameters.AddWithValue("user_id", access.UserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new DocumentCounts(0, 0, 0);
        return new DocumentCounts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    private static IReadOnlyList<string> ExtractThemes(
        IReadOnlyList<PulseAiGroundingDocument> documents,
        TaskContext? task,
        RequestContext? request,
        string? roughNote)
    {
        var text = string.Join(" ", documents
            .Where(document => document.SummaryReady)
            .Select(document => document.ContextSummary)
            .Append(task?.TaskName)
            .Append(task?.TaskDescription)
            .Append(request?.RequestedFunction)
            .Append(request?.SkillRequirements)
            .Append(roughNote)
            .Where(value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(text)) return [];

        return ThemeKeywords
            .Where(pair => pair.Value.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key)
            .Take(6)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildMissingInputs(
        string purpose,
        string? taskCode,
        string? taskName,
        string? rowLabel,
        string? roughNote,
        TaskContext? task,
        RequestContext? request,
        IReadOnlyList<PulseAiGroundingDocument> documents,
        DocumentSchema schema,
        bool requireTimesheetContextFlag)
    {
        var missing = new List<string>();
        if (purpose == "timesheet_document_grounding" && string.IsNullOrWhiteSpace(roughNote))
            missing.Add("Engineer rough note is empty; the suggestion must avoid claiming unreported work.");
        if ((!string.IsNullOrWhiteSpace(taskCode) || !string.IsNullOrWhiteSpace(taskName)) && task is null)
            missing.Add("The selected project task could not be resolved from the supplied task code or name.");
        if (!string.IsNullOrWhiteSpace(rowLabel) && task is null && request is null)
            missing.Add("The selected row did not resolve to a canonical task or engineering resource request.");
        if (!schema.TableAvailable)
            missing.Add("The project document table is unavailable.");
        if (requireTimesheetContextFlag && !schema.AiTimesheetContextEnabled)
            missing.Add("The AI timesheet context flag is unavailable on project documents.");
        if (!schema.ContextSummary)
            missing.Add("Extracted AI context summaries are unavailable; document meaning cannot yet ground the response.");
        if (documents.Count == 0)
            missing.Add("No authorized engineering-visible document is enabled for this use case.");
        if (!documents.Any(document => document.DocumentCategory.Equals("sow", StringComparison.OrdinalIgnoreCase)))
            missing.Add("No eligible SOW was found in the authorized project document scope.");
        if (!documents.Any(document => document.DocumentCategory.Equals("gsd", StringComparison.OrdinalIgnoreCase)))
            missing.Add("No eligible GSD was found in the authorized project document scope.");
        if (documents.Count > 0 && !documents.Any(document => document.SummaryReady))
            missing.Add("Documents were found, but extraction or approved AI context summaries are not ready.");
        return missing;
    }

    private static IReadOnlyList<string> BuildConflicts(
        string? requestedProjectCode,
        string? requestedProjectName,
        ProjectContext project,
        IReadOnlyList<PulseAiGroundingDocument> documents)
    {
        var conflicts = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedProjectCode)
            && !project.ProjectCode.Equals(requestedProjectCode.Trim(), StringComparison.OrdinalIgnoreCase))
            conflicts.Add("The resolved project code differs from the supplied project code.");
        if (!string.IsNullOrWhiteSpace(requestedProjectName)
            && !project.ProjectName.Equals(requestedProjectName.Trim(), StringComparison.OrdinalIgnoreCase))
            conflicts.Add("The resolved project name differs from the supplied project name.");

        var activeSow = documents.Count(document => document.DocumentCategory.Equals("sow", StringComparison.OrdinalIgnoreCase));
        var activeGsd = documents.Count(document => document.DocumentCategory.Equals("gsd", StringComparison.OrdinalIgnoreCase));
        if (activeSow > 1) conflicts.Add($"{activeSow} eligible SOW documents were found; an authoritative version rule is required.");
        if (activeGsd > 1) conflicts.Add($"{activeGsd} eligible GSD documents were found; an authoritative version rule is required.");
        if (documents.Any(document => document.ExtractionStatus is "failed" or "error"))
            conflicts.Add("At least one eligible document has a failed extraction status.");
        return conflicts;
    }

    private static (decimal Score, string Level) Coverage(
        TaskContext? task,
        RequestContext? request,
        string? roughNote,
        IReadOnlyList<PulseAiGroundingDocument> documents)
    {
        decimal score = 0.30m;
        if (task is not null || request is not null) score += 0.15m;
        if (!string.IsNullOrWhiteSpace(roughNote)) score += 0.10m;
        if (documents.Any(document => document.DocumentCategory.Equals("sow", StringComparison.OrdinalIgnoreCase) && document.SummaryReady)) score += 0.20m;
        if (documents.Any(document => document.DocumentCategory.Equals("gsd", StringComparison.OrdinalIgnoreCase) && document.SummaryReady)) score += 0.20m;
        if (documents.Any(document => document.SummaryReady && document.DocumentCategory is not "sow" and not "gsd")) score += 0.05m;
        score = Math.Min(score, 1.00m);
        var level = score switch
        {
            >= 0.85m => "comprehensive",
            >= 0.65m => "strong",
            >= 0.40m => "partial",
            _ => "limited"
        };
        return (score, level);
    }

    private static PulseAiGroundingContext EmptyContext(
        string status,
        string purpose,
        Guid effectiveUserId,
        string? projectCode,
        string? projectName,
        IReadOnlyList<string> missing,
        IReadOnlyList<string>? roles = null,
        string accessScope = "unresolved",
        string? diagnosticCode = null) =>
        new(
            Status: status,
            Purpose: purpose,
            EffectiveUserId: effectiveUserId,
            ProjectId: null,
            ProjectCode: Clean(projectCode, 100),
            ProjectName: Clean(projectName, 255),
            CustomerName: null,
            ProjectStatus: null,
            TaskCode: null,
            TaskName: null,
            TaskDescription: null,
            RequestNumber: null,
            RequestFunction: null,
            RequestStatus: null,
            AccessScope: accessScope,
            ProjectResolved: false,
            Authorized: false,
            RoleCodes: roles ?? [],
            Documents: [],
            ScopeThemes: [],
            MissingInputs: missing,
            Conflicts: [],
            CoverageScore: 0,
            CoverageLevel: "unavailable",
            GeneratedAt: DateTimeOffset.UtcNow,
            PrivacyBoundary: "private_projectpulse_runtime_only",
            ExternalProviderPolicy: "no_raw_document_external_transmission",
            DiagnosticCode: diagnosticCode);

    private static IReadOnlyList<string> ReadyCapabilities(
        bool privateInference,
        bool privateEmbedding,
        bool privateVectorIndex)
    {
        var ready = new List<string>
        {
            "permission-aware project and document metadata retrieval",
            "existing extracted AI context summary consumption when available",
            "private deterministic grounding evidence",
            "sanitized readiness and source evidence responses"
        };
        if (privateInference) ready.Add("private model inference configuration detected");
        if (privateEmbedding) ready.Add("private embedding configuration detected");
        if (privateVectorIndex) ready.Add("private vector index configuration detected");
        return ready;
    }

    private static IReadOnlyList<string> MissingDatabaseConfiguration()
    {
        try { return ProjectPulseAiDatabaseConnection.Resolve() is null ? ["ProjectPulse AI database connection"] : []; }
        catch (InvalidOperationException exception) { return [exception.Message]; }
    }

    private static string ConnectionString() =>
        ProjectPulseAiDatabaseConnection.Resolve()
        ?? throw new InvalidOperationException("ProjectPulse AI database configuration is unavailable.");

    private static string Clean(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static bool HasValue(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));

    private static bool Boolean(string name, bool fallback) =>
        bool.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;

    private static string Diagnostic(Exception exception) =>
        exception switch
        {
            PostgresException postgres => $"postgres_{postgres.SqlState}",
            NpgsqlException => "database_transport_failure",
            TimeoutException => "timeout",
            OperationCanceledException => "cancelled",
            _ => "document_grounding_failure"
        };

    private sealed record DocumentSchema(
        bool TableAvailable,
        bool EngineeringVisible,
        bool AiTimesheetContextEnabled,
        bool ExtractionStatus,
        bool ContextSummary,
        bool ContextProcessedAt,
        bool DocumentCategory,
        bool ProjectId,
        bool ContentType,
        bool SizeBytes,
        bool UploadSource)
    {
        public static DocumentSchema Missing => new(false, false, false, false, false, false, false, false, false, false, false);
    }

    private sealed record AccessContext(
        Guid UserId,
        string DisplayName,
        string Email,
        string TeamName,
        string DepartmentName,
        string Department,
        IReadOnlySet<string> RoleCodes,
        bool IsActive)
    {
        public bool IsBroadDocumentScope => RoleCodes.Overlaps(BroadDocumentRoles);
        public bool IsProjectManager => RoleCodes.Overlaps(ProjectManagementRoles);
        public bool IsProjectManagementLead => RoleCodes.Overlaps(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PROJECT_MANAGEMENT_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD",
            "PM_TEAM_LEAD"
        });
        public string ScopeLabel => IsBroadDocumentScope
            ? "organization_document_scope"
            : IsProjectManager
                ? "managed_and_assigned_project_scope"
                : "assigned_project_scope";

        public static AccessContext Empty(Guid userId) =>
            new(userId, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
    }

    private sealed record ProjectContext(
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        string CustomerName,
        string Status,
        Guid? ProjectManagerUserId);

    private sealed record TaskContext(
        Guid TaskId,
        string TaskCode,
        string TaskName,
        string? TaskDescription,
        Guid? AssignmentId);

    private sealed record RequestContext(
        string RequestNumber,
        string RequestedFunction,
        string Status,
        string? SkillRequirements,
        string? AssignmentNotes);

    private sealed record DocumentCounts(long All, long AiEnabled, long Ready);
}
