using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Ai;

/// <summary>
/// Deterministic operational and defect orchestration behind Ask Celar AI.
/// The service performs no model-generated SQL and does not treat a model as a
/// requesting or approving authority. User submissions require the actual user
/// to confirm the questionnaire; machine-created incidents use versioned Test
/// thresholds, a stable fingerprint, rate limits, and a Module 062 identity for
/// the approved default assignee.
/// </summary>
public sealed class CelarAiDefectOrchestrationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bug", "Regression", "User Interface", "API", "Authentication",
        "Authorization", "Data", "Integration", "Performance", "Documentation",
        "Feature Gap", "Availability", "Security", "Other"
    };
    private static readonly HashSet<string> Priorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Critical", "High", "Medium", "Low"
    };
    private static readonly HashSet<string> SyntheticScenarios = new(StringComparer.OrdinalIgnoreCase)
    {
        "private_inference_timeout",
        "embedding_dimension_mismatch",
        "ocr_unavailable",
        "malware_scanner_unavailable",
        "claude_unavailable",
        "openai_unavailable",
        "all_ai_targets_unavailable",
        "module064_router_unavailable",
        "github_401",
        "github_403",
        "github_429",
        "github_500",
        "github_timeout",
        "github_actions_unavailable",
        "pulse_database_timeout",
        "module076_write_unavailable",
        "module067_delivery_unavailable",
        "high_ai_latency",
        "stale_source_evidence",
        "invalid_citation",
        "duplicate_webhook_delivery",
        "recovery_flapping"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CelarAiCapabilityRoutingStore _routing;
    private readonly ILogger<CelarAiDefectOrchestrationService> _logger;

    public CelarAiDefectOrchestrationService(
        IHttpClientFactory httpClientFactory,
        CelarAiCapabilityRoutingStore routing,
        ILogger<CelarAiDefectOrchestrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _routing = routing;
        _logger = logger;
    }

    public async Task<object> GetReadinessAsync(CancellationToken cancellationToken)
    {
        var migrationReady = false;
        var defaultAssignee = new CelarAiDefectIdentity(
            null,
            CelarAiOperationsPolicy.DefaultAssigneeName,
            CelarAiOperationsPolicy.DefaultAssigneeEmailValue,
            "database_not_checked");
        var openMachineDefects = 0;
        var pendingNotifications = 0;
        var policyCount = 0;

        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            migrationReady = await MigrationReadyAsync(connection, cancellationToken);
            if (migrationReady)
            {
                defaultAssignee = await ResolveIdentityByEmailAsync(
                    connection,
                    CelarAiOperationsPolicy.DefaultAssigneeEmailValue,
                    cancellationToken);
                await using var command = new NpgsqlCommand("""
                    SELECT
                        COUNT(*) FILTER (WHERE machine_created=TRUE AND status IN ('Open','In Progress','Blocked','Reopened')),
                        (SELECT COUNT(*) FROM module076_notification_outbox WHERE status='pending'),
                        (SELECT COUNT(*) FROM module076_monitor_policies WHERE enabled=TRUE)
                    FROM module076_defects;
                    """, connection);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    openMachineDefects = reader.GetInt32(0);
                    pendingNotifications = reader.GetInt32(1);
                    policyCount = reader.GetInt32(2);
                }
            }
        }
        catch (Exception exception)
        {
            LogFailure("load Ask Celar AI operations readiness", exception);
        }

        var externalRuntime = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        return new
        {
            status = migrationReady
                ? "ask_celar_ai_operations_ready"
                : "migration_084_required",
            contractVersion = CelarAiOperationsPolicy.ContractVersion,
            migration = CelarAiOperationsPolicy.MigrationId,
            migrationReady,
            defaultAssignee,
            askCelarAiIsPrimaryExperience = true,
            module076IsSystemOfRecord = true,
            automaticMonitoringEnabled = CelarAiOperationsPolicy.AutomaticMonitoringEnabled,
            syntheticFailureEnabled = CelarAiOperationsPolicy.SyntheticFailureEnabled,
            environment = CelarAiOperationsPolicy.EnvironmentName(),
            externalRuntime = new
            {
                externalRuntime.Enabled,
                externalRuntime.Active,
                externalRuntime.Host,
                errors = externalRuntime.Errors,
                tokenReturned = false
            },
            openMachineDefects,
            pendingNotifications,
            enabledMonitorPolicies = policyCount,
            boundaries = new
            {
                viewAsCanMutate = false,
                aiCanRequestOrApproveMutation = false,
                userQuestionnaireConfirmationRequired = true,
                unrestrictedSqlAllowed = false,
                rawPromptsStoredInDefects = false,
                rawToolBodiesStoredInDefects = false,
                secretsStoredInDefects = false,
                embeddingVectorsStoredInDefects = false,
                productionAutomaticDefectsAllowed = false
            },
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<CelarAiTroubleshootOutcome> TroubleshootAsync(
        CelarAiTroubleshootRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = CelarAiOperationsPolicy.Clean(request.CorrelationId, 160);
        if (correlationId.Length == 0) correlationId = Guid.NewGuid().ToString("N");
        var evidence = new List<CelarAiProbeEvidence>();

        if (request.IncludeDatabase)
            evidence.Add(await ProbeDatabaseAsync(correlationId, cancellationToken));
        if (request.IncludeAiRuntime)
            evidence.Add(await ProbeOracleRuntimeAsync(correlationId, cancellationToken));
        if (request.IncludeModule064)
            evidence.Add(await ProbeModule064Async(correlationId, cancellationToken));
        if (request.IncludeGitHub)
            evidence.Add(await ProbeGitHubAsync(correlationId, cancellationToken));
        if (request.IncludeNotifications)
            evidence.Add(await ProbeNotificationOutboxAsync(correlationId, cancellationToken));

        var failed = evidence.Where(item => item.Failed).ToArray();
        var degraded = evidence.Where(item => item.Status == "degraded").ToArray();
        var conclusion = failed.Length > 0
            ? $"Celar AI found {failed.Length} failed operational check(s)."
            : degraded.Length > 0
                ? $"Celar AI found {degraded.Length} degraded or incomplete operational check(s)."
                : evidence.Count > 0
                    ? "The selected operational checks completed without a confirmed failure."
                    : "No operational probe was selected, so Celar AI cannot verify the issue yet.";
        var likelyCauses = failed
            .Select(item => $"{item.DisplayName}: {item.FailureCode}.")
            .Concat(degraded.Select(item => $"{item.DisplayName}: {item.Detail}."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var actions = failed.Length > 0
            ? new[]
            {
                "Review the failed evidence and correlation ID.",
                "Search Module 076 for an existing open defect with the same component and failure code.",
                "Open the guided Ask Celar AI defect questionnaire when no matching defect exists.",
                "Do not paste credentials, bearer tokens, cookies, connection strings, or raw private documents into the defect."
            }
            : new[]
            {
                "Add the affected module, route, environment, time observed, and exact user-visible behavior.",
                "Run a narrower authorized diagnostic or continue with the guided defect questionnaire."
            };
        var limitations = evidence
            .Where(item => item.Status is "unknown" or "degraded")
            .Select(item => $"{item.DisplayName}: {item.Detail}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var confidence = evidence.Count == 0
            ? 0.15m
            : failed.Length > 0
                ? 0.90m
                : degraded.Length > 0
                    ? 0.65m
                    : 0.80m;

        return new CelarAiTroubleshootOutcome(
            failed.Length > 0 ? "failed_checks_detected" : degraded.Length > 0 ? "degraded_evidence" : "checks_completed",
            conclusion,
            evidence,
            likelyCauses,
            actions,
            limitations,
            correlationId,
            confidence,
            ExistingDefectSearchRecommended: failed.Length > 0,
            DefectIntakeRecommended: failed.Length > 0 || degraded.Length > 0,
            DataAsOf: DateTimeOffset.UtcNow);
    }

    public async Task<CelarAiDefectIntakeSession> CreateIntakeSessionAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        CelarAiDefectIntakeCreateRequest request,
        CancellationToken cancellationToken)
    {
        RequireActualAuthority(actualUserId, effectiveUserId);
        await using var connection = await OpenReadyAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();
        var title = CelarAiOperationsPolicy.Clean(request.SuggestedTitle, 180);
        var description = CelarAiOperationsPolicy.SanitizeOperationalDetail(request.SuggestedDescription);
        var trigger = CelarAiOperationsPolicy.SanitizeOperationalDetail(request.TriggerQuestion);
        if (title.Length == 0 && trigger.Length > 0)
            title = trigger.Length <= 180 ? trigger : trigger[..180];
        var draft = new CelarAiDefectDraft(
            Title: title,
            Description: description,
            Category: Category(request.SuggestedCategory),
            Priority: Priority(request.SuggestedPriority),
            Environment: ValueOr(request.Environment, CelarAiOperationsPolicy.EnvironmentName(), 32),
            AffectedSystem: ValueOr(request.AffectedSystem, "Pulse", 120),
            AffectedModule: CelarAiOperationsPolicy.Clean(request.AffectedModule, 20),
            AffectedRoute: CelarAiOperationsPolicy.Clean(request.AffectedRoute, 500),
            ExpectedBehavior: string.Empty,
            ActualBehavior: description,
            ReproductionSteps: [],
            BusinessImpact: string.Empty,
            Workaround: string.Empty,
            CorrelationId: CelarAiOperationsPolicy.Clean(request.CorrelationId, 160),
            ReleaseSha: ReleaseSha(request.ReleaseSha));
        var diagnostics = SanitizeEvidence(request.DiagnosticEvidence);

        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_intake_sessions(
                intake_session_id,actual_user_id,effective_user_id,conversation_id,
                status,current_step,draft_document,diagnostic_evidence,
                revision_number,created_at,updated_at,expires_at)
            VALUES(
                @id,@actual,@effective,@conversation,
                'draft','location',@draft::jsonb,@evidence::jsonb,
                1,@now,@now,@expires);
            """, connection);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("actual", actualUserId);
        command.Parameters.AddWithValue("effective", effectiveUserId);
        command.Parameters.AddWithValue("conversation", NpgsqlDbType.Uuid, request.ConversationId is null ? DBNull.Value : request.ConversationId.Value);
        command.Parameters.AddWithValue("draft", JsonSerializer.Serialize(draft, Json));
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(diagnostics, Json));
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("expires", now.AddHours(24));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return await LoadIntakeSessionAsync(connection, sessionId, actualUserId, cancellationToken)
            ?? throw new InvalidOperationException("The defect intake session could not be reloaded.");
    }

    public async Task<CelarAiDefectIntakeSession?> GetIntakeSessionAsync(
        Guid sessionId,
        Guid actualUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenReadyAsync(cancellationToken);
        return await LoadIntakeSessionAsync(connection, sessionId, actualUserId, cancellationToken);
    }

    public async Task<CelarAiDefectIntakeSession> UpdateIntakeSessionAsync(
        Guid sessionId,
        Guid actualUserId,
        Guid effectiveUserId,
        CelarAiDefectIntakeUpdateRequest request,
        CancellationToken cancellationToken)
    {
        RequireActualAuthority(actualUserId, effectiveUserId);
        await using var connection = await OpenReadyAsync(cancellationToken);
        var current = await LoadIntakeSessionAsync(connection, sessionId, actualUserId, cancellationToken)
            ?? throw new KeyNotFoundException("The defect intake session was not found.");
        if (current.Status is "submitted" or "cancelled" or "expired")
            throw new InvalidOperationException("The defect intake session can no longer be edited.");
        var draft = new CelarAiDefectDraft(
            Title: Coalesce(request.Title, current.Draft.Title, 180),
            Description: CoalesceSanitized(request.Description, current.Draft.Description),
            Category: request.Category is null ? current.Draft.Category : Category(request.Category),
            Priority: request.Priority is null ? current.Draft.Priority : Priority(request.Priority),
            Environment: Coalesce(request.Environment, current.Draft.Environment, 32),
            AffectedSystem: Coalesce(request.AffectedSystem, current.Draft.AffectedSystem, 120),
            AffectedModule: Coalesce(request.AffectedModule, current.Draft.AffectedModule, 20),
            AffectedRoute: Coalesce(request.AffectedRoute, current.Draft.AffectedRoute, 500),
            ExpectedBehavior: CoalesceSanitized(request.ExpectedBehavior, current.Draft.ExpectedBehavior),
            ActualBehavior: CoalesceSanitized(request.ActualBehavior, current.Draft.ActualBehavior),
            ReproductionSteps: request.ReproductionSteps is null
                ? current.Draft.ReproductionSteps
                : CelarAiOperationsPolicy.CleanList(
                    request.ReproductionSteps,
                    CelarAiOperationsPolicy.MaximumReproductionSteps,
                    1000),
            BusinessImpact: CoalesceSanitized(request.BusinessImpact, current.Draft.BusinessImpact),
            Workaround: CoalesceSanitized(request.Workaround, current.Draft.Workaround),
            CorrelationId: Coalesce(request.CorrelationId, current.Draft.CorrelationId, 160),
            ReleaseSha: request.ReleaseSha is null ? current.Draft.ReleaseSha : ReleaseSha(request.ReleaseSha));
        var status = request.ReadyForReview == true ? "ready_for_review" : current.Status;
        var step = Coalesce(request.CurrentStep, current.CurrentStep, 60);
        var now = DateTimeOffset.UtcNow;

        await using var command = new NpgsqlCommand("""
            UPDATE module076_intake_sessions
            SET status=@status,current_step=@step,draft_document=@draft::jsonb,
                revision_number=revision_number+1,updated_at=@now
            WHERE intake_session_id=@id
              AND actual_user_id=@actual
              AND revision_number=@revision
              AND status IN ('draft','ready_for_review')
              AND expires_at>@now;
            """, connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("step", step);
        command.Parameters.AddWithValue("draft", JsonSerializer.Serialize(draft, Json));
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("actual", actualUserId);
        command.Parameters.AddWithValue("revision", request.ExpectedRevision);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The intake session changed after it was loaded. Refresh and try again.");
        return await LoadIntakeSessionAsync(connection, sessionId, actualUserId, cancellationToken)
            ?? throw new InvalidOperationException("The updated intake session could not be reloaded.");
    }

    public async Task<CelarAiDefectRecord> SubmitIntakeSessionAsync(
        Guid sessionId,
        Guid actualUserId,
        Guid effectiveUserId,
        CelarAiDefectIntakeSubmitRequest request,
        CancellationToken cancellationToken)
    {
        RequireActualAuthority(actualUserId, effectiveUserId);
        if (!request.UserConfirmed
            || !string.Equals(
                request.ConfirmationText?.Trim(),
                "CREATE DEFECT",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Confirm the reviewed questionnaire with CREATE DEFECT.");
        }

        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var session = await LoadIntakeSessionAsync(
            connection,
            sessionId,
            actualUserId,
            cancellationToken,
            transaction)
            ?? throw new KeyNotFoundException("The defect intake session was not found.");
        if (session.RevisionNumber != request.ExpectedRevision)
            throw new InvalidOperationException("The intake session changed after it was loaded. Refresh and review it again.");
        if (session.Status is "submitted" or "cancelled" or "expired")
            throw new InvalidOperationException("The defect intake session can no longer be submitted.");
        ValidateDraft(session.Draft);

        var existing = await FindDefectByIdempotencyAsync(
            connection,
            transaction,
            $"ask-celar-ai:{sessionId:N}",
            cancellationToken);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return existing;
        }

        var assignee = await ResolveIdentityByEmailAsync(
            connection,
            CelarAiOperationsPolicy.DefaultAssigneeEmailValue,
            cancellationToken,
            transaction);
        if (!assignee.UserId.HasValue)
            throw new InvalidOperationException("The approved default defect assignee is not an active Module 062 identity.");
        var reporter = await ResolveIdentityByUserIdAsync(
            connection,
            actualUserId,
            cancellationToken,
            transaction);
        var record = await InsertDefectAsync(
            connection,
            transaction,
            session.Draft,
            reporter,
            assignee,
            sourceChannel: "ask_celar_ai",
            machineCreated: false,
            userConfirmed: true,
            fingerprint: null,
            idempotencyKey: $"ask-celar-ai:{sessionId:N}",
            firstObservedAt: null,
            lastObservedAt: null,
            cancellationToken);

        foreach (var item in session.DiagnosticEvidence.Take(CelarAiOperationsPolicy.MaximumEvidenceItems))
        {
            await InsertEvidenceAsync(
                connection,
                transaction,
                record.DefectId,
                "diagnostic_probe",
                item.ProbeCode,
                item.Source,
                $"{item.DisplayName}: {item.Status}; {item.FailureCode}; {item.Detail}",
                new
                {
                    item.ProbeCode,
                    item.ComponentCode,
                    item.Status,
                    item.HttpStatus,
                    item.LatencyMs,
                    item.FailureCode,
                    item.ObservedAt
                },
                item.ObservedAt,
                cancellationToken);
        }

        await InsertEventAsync(
            connection,
            transaction,
            record.DefectId,
            "defect_created_from_ask_celar_ai",
            null,
            "Open",
            "The actual user confirmed the guided Ask Celar AI questionnaire.",
            actualUserId,
            effectiveUserId,
            new { intakeSessionId = sessionId, userConfirmed = true },
            cancellationToken);
        await QueueNotificationAsync(
            connection,
            transaction,
            record,
            "defect_opened",
            "active_manager_role_group_and_default_assignee",
            $"defect_opened:{record.DefectNumber}",
            cancellationToken);

        await using var update = new NpgsqlCommand("""
            UPDATE module076_intake_sessions
            SET status='submitted',submitted_defect_id=@defect,
                revision_number=revision_number+1,updated_at=NOW()
            WHERE intake_session_id=@session
              AND actual_user_id=@actual
              AND revision_number=@revision;
            """, connection, transaction);
        update.Parameters.AddWithValue("defect", record.DefectId);
        update.Parameters.AddWithValue("session", sessionId);
        update.Parameters.AddWithValue("actual", actualUserId);
        update.Parameters.AddWithValue("revision", request.ExpectedRevision);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The intake session could not be finalized atomically.");

        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<CelarAiDefectRecord>> FindMatchingDefectsAsync(
        string? environment,
        string? affectedModule,
        string? componentCode,
        string? failureCode,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DefectSelect + """
            WHERE d.status IN ('Open','In Progress','Blocked','Reopened')
              AND (@environment='' OR lower(d.environment)=lower(@environment))
              AND (@module='' OR lower(d.affected_module)=lower(@module))
              AND (
                    @component=''
                    OR lower(d.affected_system)=lower(@component)
                    OR d.metadata->>'componentCode'=@component
                  )
              AND (@failure='' OR d.metadata->>'failureCode'=@failure)
            ORDER BY d.priority='Critical' DESC,d.date_added DESC
            LIMIT 25;
            """, connection);
        command.Parameters.AddWithValue("environment", CelarAiOperationsPolicy.Clean(environment, 32));
        command.Parameters.AddWithValue("module", CelarAiOperationsPolicy.Clean(affectedModule, 20));
        command.Parameters.AddWithValue("component", CelarAiOperationsPolicy.Clean(componentCode, 100));
        command.Parameters.AddWithValue("failure", CelarAiOperationsPolicy.Clean(failureCode, 120));
        return await ReadDefectsAsync(command, cancellationToken);
    }

    public async Task<CelarAiDefectRecord?> GetDefectAsync(
        string defectNumber,
        Guid actualUserId,
        bool canViewAll,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var command = new NpgsqlCommand(DefectSelect + """
            WHERE d.defect_number=@number
              AND (
                    @all=TRUE
                    OR d.actual_reporter_user_id=@user
                    OR d.assignee_user_id=@user
                  )
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("number", CelarAiOperationsPolicy.Clean(defectNumber, 32).ToUpperInvariant());
        command.Parameters.AddWithValue("all", canViewAll);
        command.Parameters.AddWithValue("user", actualUserId);
        var rows = await ReadDefectsAsync(command, cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task AddEvidenceAsync(
        Guid defectId,
        Guid actualUserId,
        Guid effectiveUserId,
        bool canManage,
        CelarAiDefectEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        RequireActualAuthority(actualUserId, effectiveUserId);
        var summary = CelarAiOperationsPolicy.SanitizeOperationalDetail(request.SanitizedSummary);
        if (summary.Length == 0) throw new InvalidOperationException("Provide sanitized evidence text.");
        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await CanMutateDefectAsync(connection, transaction, defectId, actualUserId, canManage, cancellationToken))
            throw new UnauthorizedAccessException("The user is not authorized to add evidence to this defect.");
        var document = request.EvidenceDocument is { ValueKind: JsonValueKind.Object } element
            ? JsonSerializer.Deserialize<object>(element.GetRawText(), Json) ?? new { }
            : new { };
        await InsertEvidenceAsync(
            connection,
            transaction,
            defectId,
            ValueOr(request.EvidenceType, "user_supplied", 60),
            ValueOr(request.SourceCode, "ask_celar_ai", 100),
            CelarAiOperationsPolicy.Clean(request.SourceReference, 500),
            summary,
            document,
            request.ObservedAt ?? DateTimeOffset.UtcNow,
            cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            defectId,
            "evidence_added_from_ask_celar_ai",
            null,
            null,
            "Authorized sanitized evidence was added from Ask Celar AI.",
            actualUserId,
            effectiveUserId,
            new { rawPrivateContentStored = false, secretStored = false },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CelarAiMonitorPolicy>> ListMonitorPoliciesAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT policy_code,display_name,component_code,environment,enabled,
                   consecutive_failure_threshold,evaluation_window_seconds,
                   consecutive_success_threshold,recovery_stability_seconds,
                   initial_priority,maximum_new_defects_per_hour,
                   flapping_window_seconds,flapping_reopen_threshold,
                   machine_creation_enabled,revision_number
            FROM module076_monitor_policies
            ORDER BY policy_code;
            """, connection);
        var rows = new List<CelarAiMonitorPolicy>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CelarAiMonitorPolicy(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetBoolean(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
                reader.GetInt32(8), reader.GetString(9), reader.GetInt32(10), reader.GetInt32(11),
                reader.GetInt32(12), reader.GetBoolean(13), reader.GetInt32(14)));
        }
        return rows;
    }

    public async Task<CelarAiMonitorPolicy> SetMachineCreationAsync(
        string policyCode,
        int expectedRevision,
        bool enabled,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (!CelarAiOperationsPolicy.IsTest)
            throw new InvalidOperationException("Automatic defect creation can be changed only in protected Test.");
        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE module076_monitor_policies
            SET machine_creation_enabled=@enabled,revision_number=revision_number+1,
                updated_by_user_id=@actor,updated_at=NOW()
            WHERE policy_code=@code AND revision_number=@revision
            RETURNING policy_code,display_name,component_code,environment,enabled,
                      consecutive_failure_threshold,evaluation_window_seconds,
                      consecutive_success_threshold,recovery_stability_seconds,
                      initial_priority,maximum_new_defects_per_hour,
                      flapping_window_seconds,flapping_reopen_threshold,
                      machine_creation_enabled,revision_number;
            """, connection);
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("actor", actorUserId);
        command.Parameters.AddWithValue("code", CelarAiOperationsPolicy.Clean(policyCode, 120).ToLowerInvariant());
        command.Parameters.AddWithValue("revision", expectedRevision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The monitor policy changed after it was loaded, or it does not exist.");
        return new CelarAiMonitorPolicy(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetBoolean(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
            reader.GetInt32(8), reader.GetString(9), reader.GetInt32(10), reader.GetInt32(11),
            reader.GetInt32(12), reader.GetBoolean(13), reader.GetInt32(14));
    }

    public async Task RunScheduledProbesAsync(CancellationToken cancellationToken)
    {
        if (!CelarAiOperationsPolicy.AutomaticMonitoringEnabled) return;
        IReadOnlyList<CelarAiMonitorPolicy> policies;
        try
        {
            policies = await ListMonitorPoliciesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            LogFailure("load monitor policies", exception);
            return;
        }

        foreach (var policy in policies.Where(item => item.Enabled))
        {
            try
            {
                var evidence = await ProbeForPolicyAsync(policy, cancellationToken);
                await RecordAndEvaluateProbeAsync(policy, evidence, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogFailure($"evaluate monitor policy {policy.PolicyCode}", exception);
            }
        }
    }

    public async Task<CelarAiMonitorEvaluation> RunSyntheticFailureAsync(
        CelarAiSyntheticFailureRequest request,
        CancellationToken cancellationToken)
    {
        if (!CelarAiOperationsPolicy.SyntheticFailureEnabled)
            throw new InvalidOperationException("Synthetic failure injection is disabled.");
        if (!string.Equals(request.Confirmation, "RUN TEST SYNTHETIC FAILURE", StringComparison.Ordinal))
            throw new InvalidOperationException("Use the exact synthetic-failure confirmation.");
        var scenario = CelarAiOperationsPolicy.Clean(request.Scenario, 120).ToLowerInvariant();
        if (!SyntheticScenarios.Contains(scenario))
            throw new InvalidOperationException("The synthetic failure scenario is not allowlisted.");
        var (policyCode, component, failure, statusCode) = SyntheticScenario(scenario);
        var policies = await ListMonitorPoliciesAsync(cancellationToken);
        var policy = policies.FirstOrDefault(item => item.PolicyCode == policyCode)
            ?? throw new InvalidOperationException($"The monitor policy {policyCode} is not configured.");
        var occurrences = Math.Clamp(request.Occurrences ?? 1, 1, 10);
        CelarAiMonitorEvaluation? evaluation = null;
        for (var index = 0; index < occurrences; index++)
        {
            var evidence = new CelarAiProbeEvidence(
                $"synthetic:{scenario}",
                component,
                $"Synthetic {scenario}",
                "failed",
                statusCode,
                1,
                failure,
                "Test-only synthetic failure; no external system was changed.",
                "test_synthetic_failure_harness",
                DateTimeOffset.UtcNow.AddMilliseconds(index));
            evaluation = await RecordAndEvaluateProbeAsync(policy, evidence, cancellationToken);
        }
        return evaluation ?? throw new InvalidOperationException("The synthetic failure was not evaluated.");
    }

    private async Task<CelarAiMonitorEvaluation> RecordAndEvaluateProbeAsync(
        CelarAiMonitorPolicy policy,
        CelarAiProbeEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenReadyAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertProbeResultAsync(connection, transaction, policy, evidence, cancellationToken);
        var suppressed = await IsSuppressedAsync(connection, transaction, policy, evidence.ObservedAt, cancellationToken);
        var windowStart = evidence.ObservedAt.AddSeconds(-policy.EvaluationWindowSeconds);
        var states = new List<(string Status, DateTimeOffset ObservedAt)>();
        await using (var command = new NpgsqlCommand("""
            SELECT status,observed_at
            FROM module076_probe_results
            WHERE policy_code=@policy AND observed_at>=@window
            ORDER BY observed_at DESC
            LIMIT 120;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("policy", policy.PolicyCode);
            command.Parameters.AddWithValue("window", windowStart);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                states.Add((reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }
        var consecutiveFailures = Consecutive(states, status => status is "failed" or "degraded");
        var consecutiveSuccesses = Consecutive(states, status => status == "healthy");
        var fingerprint = Fingerprint(
            policy.Environment,
            policy.ComponentCode,
            evidence.ProbeCode,
            evidence.FailureCode,
            CelarAiOperationsPolicy.ReleaseSha());
        CelarAiDefectRecord? defect = null;
        var thresholdCrossed = !suppressed
            && evidence.Failed
            && consecutiveFailures >= policy.ConsecutiveFailureThreshold;
        var recoveryStable = false;

        if (thresholdCrossed && policy.MachineCreationEnabled && CelarAiOperationsPolicy.AutomaticMonitoringEnabled)
        {
            defect = await UpsertMachineDefectAsync(
                connection,
                transaction,
                policy,
                evidence,
                fingerprint,
                cancellationToken);
        }
        else if (evidence.Healthy && consecutiveSuccesses >= policy.ConsecutiveSuccessThreshold)
        {
            recoveryStable = await IsRecoveryStableAsync(
                connection,
                transaction,
                policy,
                evidence.ObservedAt,
                cancellationToken);
            if (recoveryStable)
                defect = await RecoverMachineDefectAsync(
                    connection,
                    transaction,
                    policy,
                    evidence,
                    fingerprint,
                    cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new CelarAiMonitorEvaluation(
            policy.PolicyCode,
            suppressed ? "suppressed" : thresholdCrossed ? "threshold_crossed" : recoveryStable ? "recovered" : evidence.Status,
            consecutiveFailures,
            consecutiveSuccesses,
            suppressed,
            thresholdCrossed,
            recoveryStable,
            fingerprint,
            defect?.DefectId,
            defect?.DefectNumber,
            DateTimeOffset.UtcNow);
    }

    private async Task<CelarAiDefectRecord?> UpsertMachineDefectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CelarAiMonitorPolicy policy,
        CelarAiProbeEvidence evidence,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await FindByFingerprintAsync(connection, transaction, policy.Environment, fingerprint, cancellationToken);
        if (existing is not null)
        {
            await using var update = new NpgsqlCommand("""
                UPDATE module076_defects
                SET occurrence_count=occurrence_count+1,last_observed_at=@observed,
                    updated_at=NOW(),revision_number=revision_number+1
                WHERE defect_id=@id;
                """, connection, transaction);
            update.Parameters.AddWithValue("observed", evidence.ObservedAt);
            update.Parameters.AddWithValue("id", existing.DefectId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await InsertOccurrenceAsync(connection, transaction, existing.DefectId, fingerprint, evidence, cancellationToken);
            await InsertCommentAsync(
                connection,
                transaction,
                existing.DefectId,
                "monitor_occurrence",
                $"Additional failure observed: {evidence.FailureCode}. {evidence.Detail}",
                cancellationToken);
            return await FindDefectByIdAsync(connection, transaction, existing.DefectId, cancellationToken);
        }

        if (await AutomaticDefectRateLimitReachedAsync(connection, transaction, policy.MaximumNewDefectsPerHour, cancellationToken))
            return null;
        var assignee = await ResolveIdentityByEmailAsync(
            connection,
            CelarAiOperationsPolicy.DefaultAssigneeEmailValue,
            cancellationToken,
            transaction);
        if (!assignee.UserId.HasValue)
            throw new InvalidOperationException("The approved default defect assignee is not an active Module 062 identity.");
        var title = $"[AUTO][{policy.Environment}][{policy.DisplayName}] Service unavailable";
        var description = $"{policy.DisplayName} crossed its governed failure threshold. Failure code: {evidence.FailureCode}. Evidence: {evidence.Detail}";
        var draft = new CelarAiDefectDraft(
            title.Length <= 180 ? title : title[..180],
            CelarAiOperationsPolicy.SanitizeOperationalDetail(description),
            "Availability",
            policy.InitialPriority,
            policy.Environment,
            policy.ComponentCode,
            string.Empty,
            string.Empty,
            "The component should satisfy its approved availability probe.",
            evidence.Detail,
            [],
            "The monitored capability may be unavailable or degraded.",
            string.Empty,
            string.Empty,
            CelarAiOperationsPolicy.ReleaseSha());
        var reporter = new CelarAiDefectIdentity(null, "Governed monitoring service", string.Empty, "service_identity");
        var record = await InsertDefectAsync(
            connection,
            transaction,
            draft,
            reporter,
            assignee,
            "availability_monitor",
            machineCreated: true,
            userConfirmed: false,
            fingerprint,
            $"monitor:{policy.Environment}:{fingerprint}",
            evidence.ObservedAt,
            evidence.ObservedAt,
            cancellationToken,
            metadata: new { policyCode = policy.PolicyCode, componentCode = policy.ComponentCode, failureCode = evidence.FailureCode });
        await InsertOccurrenceAsync(connection, transaction, record.DefectId, fingerprint, evidence, cancellationToken);
        await InsertEvidenceAsync(
            connection,
            transaction,
            record.DefectId,
            "availability_probe",
            evidence.ProbeCode,
            evidence.Source,
            $"{evidence.DisplayName}: {evidence.Status}; {evidence.FailureCode}; {evidence.Detail}",
            new { policy.PolicyCode, evidence.HttpStatus, evidence.LatencyMs, evidence.ObservedAt },
            evidence.ObservedAt,
            cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            record.DefectId,
            "automatic_defect_created",
            null,
            "Open",
            "A versioned Test monitor policy crossed its failure threshold.",
            null,
            null,
            new { policy.PolicyCode, policy.ConsecutiveFailureThreshold, policy.EvaluationWindowSeconds },
            cancellationToken);
        await QueueNotificationAsync(
            connection,
            transaction,
            record,
            "defect_opened",
            "active_manager_role_group_and_default_assignee",
            $"defect_opened:{record.DefectNumber}",
            cancellationToken);
        return record;
    }

    private async Task<CelarAiDefectRecord?> RecoverMachineDefectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CelarAiMonitorPolicy policy,
        CelarAiProbeEvidence evidence,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await FindByFingerprintAsync(connection, transaction, policy.Environment, fingerprint, cancellationToken);
        if (existing is null || !existing.MachineCreated) return null;
        await using var update = new NpgsqlCommand("""
            UPDATE module076_defects
            SET status='Resolved',date_resolved=@resolved,last_observed_at=@resolved,
                updated_at=NOW(),revision_number=revision_number+1
            WHERE defect_id=@id
              AND machine_created=TRUE
              AND status IN ('Open','In Progress','Blocked','Reopened');
            """, connection, transaction);
        update.Parameters.AddWithValue("resolved", evidence.ObservedAt);
        update.Parameters.AddWithValue("id", existing.DefectId);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) return existing;
        await InsertOccurrenceAsync(connection, transaction, existing.DefectId, fingerprint, evidence with { Status = "recovered" }, cancellationToken);
        await InsertCommentAsync(
            connection,
            transaction,
            existing.DefectId,
            "recovery",
            $"Recovery verified after {policy.ConsecutiveSuccessThreshold} consecutive successful probes and {policy.RecoveryStabilitySeconds} seconds of stability.",
            cancellationToken);
        await InsertEventAsync(
            connection,
            transaction,
            existing.DefectId,
            "automatic_recovery_verified",
            existing.Status,
            "Resolved",
            "The machine-created incident met its governed recovery policy.",
            null,
            null,
            new { policy.PolicyCode, policy.ConsecutiveSuccessThreshold, policy.RecoveryStabilitySeconds },
            cancellationToken);
        var resolved = await FindDefectByIdAsync(connection, transaction, existing.DefectId, cancellationToken)
            ?? existing;
        await QueueNotificationAsync(
            connection,
            transaction,
            resolved,
            "defect_resolved",
            "default_assignee_and_original_reporter",
            $"defect_resolved:{resolved.DefectNumber}:{resolved.RevisionNumber}",
            cancellationToken);
        return resolved;
    }

    private async Task<CelarAiProbeEvidence> ProbeForPolicyAsync(
        CelarAiMonitorPolicy policy,
        CancellationToken cancellationToken) => policy.PolicyCode switch
    {
        "pulse_database" => await ProbeDatabaseAsync(Guid.NewGuid().ToString("N"), cancellationToken),
        "private_inference" or "private_embeddings" or "private_ocr" or "private_malware_scan" or "all_ai_targets" or "tls_certificate" or "clamav_signatures"
            => await ProbeOracleRuntimeAsync(Guid.NewGuid().ToString("N"), cancellationToken),
        "module064" => await ProbeModule064Async(Guid.NewGuid().ToString("N"), cancellationToken),
        "github_api" or "github_actions" => await ProbeGitHubAsync(Guid.NewGuid().ToString("N"), cancellationToken),
        "module067" => await ProbeNotificationOutboxAsync(Guid.NewGuid().ToString("N"), cancellationToken),
        _ => await ProbeConfiguredPulseEndpointAsync(policy, cancellationToken)
    };

    private async Task<CelarAiProbeEvidence> ProbeDatabaseAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return Probe("pulse_database", "pulse_database", "Pulse database", "healthy", null, Elapsed(started), string.Empty, "Connection and SELECT 1 succeeded.", "pulse_database", DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            LogFailure("probe Pulse database", exception);
            return Probe("pulse_database", "pulse_database", "Pulse database", "failed", null, Elapsed(started), "database_unavailable", "The database probe did not complete.", "pulse_database", DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeOracleRuntimeAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (!snapshot.Active || snapshot.ReadinessEndpoint is null)
        {
            return Probe("oracle_runtime", "all_ai_targets", "Celar AI private runtime", "degraded", null, Elapsed(started), "runtime_configuration_not_active", "The protected Test external-runtime policy is not active or valid.", "module064_and_external_runtime_policy", DateTimeOffset.UtcNow);
        }
        var endpointCheck = await PulseAiExternalHttpsRuntimePolicy.VerifyEndpointAsync(
            snapshot.ReadinessEndpoint.ToString(),
            cancellationToken);
        if (!endpointCheck.Allowed)
        {
            return Probe("oracle_runtime", "all_ai_targets", "Celar AI private runtime", "failed", null, Elapsed(started), endpointCheck.Reason, "The readiness endpoint failed exact-host and IP-pin validation.", "external_runtime_policy", DateTimeOffset.UtcNow);
        }
        var token = Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN")?.Trim() ?? string.Empty;
        if (token.Length < 32)
        {
            return Probe("oracle_runtime", "all_ai_targets", "Celar AI private runtime", "failed", null, Elapsed(started), "runtime_token_unavailable", "The protected runtime token is unavailable to the readiness client.", "protected_secret_reference", DateTimeOffset.UtcNow);
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, snapshot.ReadinessEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("X-Pulse-AI-Privacy-Boundary", "private_pulse_runtime_only");
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
            using var response = await _httpClientFactory
                .CreateClient("PulseAiExternalRuntimeReadiness")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = response.IsSuccessStatusCode ? "healthy" : "failed";
            return Probe(
                "oracle_runtime",
                "all_ai_targets",
                "Celar AI private runtime",
                status,
                (int)response.StatusCode,
                Elapsed(started),
                response.IsSuccessStatusCode ? string.Empty : "runtime_health_http_failure",
                response.IsSuccessStatusCode
                    ? "Authenticated private-runtime readiness succeeded."
                    : "The authenticated private-runtime readiness request failed.",
                "celarai.onenecklab.com/health",
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("probe Celar AI private runtime", exception);
            return Probe("oracle_runtime", "all_ai_targets", "Celar AI private runtime", "failed", null, Elapsed(started), "runtime_health_unavailable", "The authenticated private-runtime readiness request did not complete.", "celarai.onenecklab.com/health", DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeModule064Async(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var route = await _routing.LoadRouteAsync(CelarAiCapabilityCatalog.HelpAssistant, cancellationToken);
            var targets = route.Targets.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            var healthy = targets.Length > 0;
            return Probe(
                "module064_help_route",
                "module064",
                "Module 064 Ask Celar AI route",
                healthy ? "healthy" : "failed",
                null,
                Elapsed(started),
                healthy ? string.Empty : "module064_route_empty",
                healthy
                    ? $"The governed Ask Celar AI route contains {targets.Length} target(s)."
                    : "No governed Ask Celar AI target is configured.",
                "module064_capability_routing_store",
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            LogFailure("probe Module 064 route", exception);
            return Probe("module064_help_route", "module064", "Module 064 Ask Celar AI route", "failed", null, Elapsed(started), "module064_unavailable", "The governed provider route could not be loaded.", "module064_capability_routing_store", DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeGitHubAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var endpoint = Environment.GetEnvironmentVariable("PROJECTPULSE_CELAR_AI_GITHUB_HEALTH_ENDPOINT")?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = $"https://api.github.com/repos/{CelarAiOperationsPolicy.DefaultRepository}";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.AbsolutePath.Equals($"/repos/{CelarAiOperationsPolicy.DefaultRepository}", StringComparison.Ordinal))
        {
            return Probe("github_repository", "github_api", "GitHub repository access", "degraded", null, Elapsed(started), "github_endpoint_not_allowlisted", "The exact GitHub repository-health endpoint is not allowlisted.", "github_configuration", DateTimeOffset.UtcNow);
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Pulse-Celar-AI-Operations/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
            var token = Environment.GetEnvironmentVariable("PROJECTPULSE_GITHUB_MONITOR_TOKEN")?.Trim();
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClientFactory
                .CreateClient("ProjectPulseAi")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return Probe(
                "github_repository",
                "github_api",
                "GitHub repository access",
                response.IsSuccessStatusCode ? "healthy" : "failed",
                (int)response.StatusCode,
                Elapsed(started),
                response.IsSuccessStatusCode ? string.Empty : $"github_http_{(int)response.StatusCode}",
                response.IsSuccessStatusCode
                    ? "The allowlisted repository metadata request succeeded."
                    : "The allowlisted GitHub repository request failed.",
                "api.github.com",
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("probe GitHub", exception);
            return Probe("github_repository", "github_api", "GitHub repository access", "failed", null, Elapsed(started), "github_unavailable", "The allowlisted GitHub repository request did not complete.", "api.github.com", DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeNotificationOutboxAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var connection = await OpenReadyAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT COUNT(*),MIN(created_at)
                FROM module076_notification_outbox
                WHERE status='pending';
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var count = reader.GetInt32(0);
            var oldest = reader.IsDBNull(1) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(1);
            var delayed = oldest.HasValue && DateTimeOffset.UtcNow - oldest.Value > TimeSpan.FromMinutes(15);
            return Probe(
                "module067_outbox",
                "module067",
                "Module 067 defect notification handoff",
                delayed ? "failed" : count > 0 ? "degraded" : "healthy",
                null,
                Elapsed(started),
                delayed ? "notification_outbox_delayed" : string.Empty,
                count == 0
                    ? "No pending Module 076 notification events."
                    : $"{count} notification event(s) are waiting for Module 067 delivery.",
                "module076_notification_outbox",
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            LogFailure("probe Module 067 notification handoff", exception);
            return Probe("module067_outbox", "module067", "Module 067 defect notification handoff", "failed", null, Elapsed(started), "notification_outbox_unavailable", "The notification outbox could not be inspected.", "module076_notification_outbox", DateTimeOffset.UtcNow);
        }
    }

    private async Task<CelarAiProbeEvidence> ProbeConfiguredPulseEndpointAsync(
        CelarAiMonitorPolicy policy,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var name = $"PROJECTPULSE_CELAR_AI_{policy.PolicyCode.ToUpperInvariant()}_HEALTH_ENDPOINT";
        var configured = Environment.GetEnvironmentVariable(name)?.Trim();
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return Probe(policy.PolicyCode, policy.ComponentCode, policy.DisplayName, "unknown", null, Elapsed(started), "probe_endpoint_not_configured", $"{name} is not configured.", "deployment_managed_probe_configuration", DateTimeOffset.UtcNow);
        }
        var publicOrigin = Environment.GetEnvironmentVariable("PROJECTPULSE_PUBLIC_ORIGIN")?.Trim();
        if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin)
            || !endpoint.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
        {
            return Probe(policy.PolicyCode, policy.ComponentCode, policy.DisplayName, "failed", null, Elapsed(started), "probe_endpoint_host_not_approved", "The health endpoint is not on the deployment-managed Pulse public origin.", "deployment_managed_probe_configuration", DateTimeOffset.UtcNow);
        }
        try
        {
            using var response = await _httpClientFactory
                .CreateClient("ProjectPulseAi")
                .GetAsync(endpoint, cancellationToken);
            return Probe(policy.PolicyCode, policy.ComponentCode, policy.DisplayName, response.IsSuccessStatusCode ? "healthy" : "failed", (int)response.StatusCode, Elapsed(started), response.IsSuccessStatusCode ? string.Empty : $"pulse_http_{(int)response.StatusCode}", response.IsSuccessStatusCode ? "The configured Pulse health request succeeded." : "The configured Pulse health request failed.", endpoint.Host, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailure($"probe {policy.PolicyCode}", exception);
            return Probe(policy.PolicyCode, policy.ComponentCode, policy.DisplayName, "failed", null, Elapsed(started), "pulse_health_unavailable", "The configured Pulse health request did not complete.", endpoint.Host, DateTimeOffset.UtcNow);
        }
    }

    private static CelarAiProbeEvidence Probe(
        string probeCode,
        string componentCode,
        string displayName,
        string status,
        int? httpStatus,
        int? latencyMs,
        string failureCode,
        string detail,
        string source,
        DateTimeOffset observedAt) =>
        new(
            probeCode,
            componentCode,
            displayName,
            status,
            httpStatus,
            latencyMs,
            CelarAiOperationsPolicy.Clean(failureCode, 120),
            CelarAiOperationsPolicy.SanitizeOperationalDetail(detail),
            CelarAiOperationsPolicy.Clean(source, 500),
            observedAt);

    private static int Elapsed(long started) =>
        Math.Max(0, (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(started).TotalMilliseconds));

    private static (string Policy, string Component, string Failure, int? Status) SyntheticScenario(string scenario) => scenario switch
    {
        "private_inference_timeout" => ("private_inference", "private_inference", "timeout", null),
        "embedding_dimension_mismatch" => ("private_embeddings", "private_embeddings", "embedding_dimension_mismatch", 200),
        "ocr_unavailable" => ("private_ocr", "private_ocr", "ocr_unavailable", 503),
        "malware_scanner_unavailable" => ("private_malware_scan", "private_malware_scan", "malware_scanner_unavailable", 503),
        "all_ai_targets_unavailable" => ("all_ai_targets", "all_ai_targets", "all_ai_targets_unavailable", 503),
        "module064_router_unavailable" => ("module064", "module064", "module064_router_unavailable", 503),
        "github_401" => ("github_api", "github_api", "github_http_401", 401),
        "github_403" => ("github_api", "github_api", "github_http_403", 403),
        "github_429" => ("github_api", "github_api", "github_http_429", 429),
        "github_500" => ("github_api", "github_api", "github_http_500", 500),
        "github_timeout" => ("github_api", "github_api", "github_timeout", null),
        "github_actions_unavailable" => ("github_actions", "github_actions", "github_actions_unavailable", 503),
        "pulse_database_timeout" => ("pulse_database", "pulse_database", "database_timeout", null),
        "module067_delivery_unavailable" => ("module067", "module067", "notification_delivery_unavailable", 503),
        "high_ai_latency" => ("private_inference", "private_inference", "high_latency", 200),
        "claude_unavailable" => ("all_ai_targets", "all_ai_targets", "claude_unavailable", 503),
        "openai_unavailable" => ("all_ai_targets", "all_ai_targets", "openai_unavailable", 503),
        _ => ("all_ai_targets", "all_ai_targets", scenario, 500)
    };

    private static int Consecutive(
        IEnumerable<(string Status, DateTimeOffset ObservedAt)> states,
        Func<string, bool> predicate)
    {
        var count = 0;
        foreach (var state in states)
        {
            if (!predicate(state.Status)) break;
            count++;
        }
        return count;
    }

    private static string Fingerprint(params string[] values)
    {
        var normalized = string.Join('|', values.Select(value =>
            CelarAiOperationsPolicy.Normalize(value)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }

    private static string Category(string? value)
    {
        var candidate = CelarAiOperationsPolicy.Clean(value, 40);
        return Categories.FirstOrDefault(item => item.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            ?? "Bug";
    }

    private static string Priority(string? value)
    {
        var candidate = CelarAiOperationsPolicy.Clean(value, 20);
        return Priorities.FirstOrDefault(item => item.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            ?? "Medium";
    }

    private static string ValueOr(string? value, string fallback, int maximum)
    {
        var clean = CelarAiOperationsPolicy.Clean(value, maximum);
        return clean.Length > 0 ? clean : fallback;
    }

    private static string Coalesce(string? value, string current, int maximum) =>
        value is null ? current : CelarAiOperationsPolicy.Clean(value, maximum);

    private static string CoalesceSanitized(string? value, string current) =>
        value is null ? current : CelarAiOperationsPolicy.SanitizeOperationalDetail(value);

    private static string ReleaseSha(string? value)
    {
        var clean = CelarAiOperationsPolicy.Clean(value, 40).ToLowerInvariant();
        return clean.Length == 40 && clean.All(character => char.IsAsciiHexDigit(character))
            ? clean
            : CelarAiOperationsPolicy.ReleaseSha();
    }

    private static IReadOnlyList<CelarAiProbeEvidence> SanitizeEvidence(
        IReadOnlyList<CelarAiProbeEvidence>? evidence) =>
        (evidence ?? [])
            .Take(CelarAiOperationsPolicy.MaximumEvidenceItems)
            .Select(item => item with
            {
                ProbeCode = CelarAiOperationsPolicy.Clean(item.ProbeCode, 120),
                ComponentCode = CelarAiOperationsPolicy.Clean(item.ComponentCode, 100),
                DisplayName = CelarAiOperationsPolicy.Clean(item.DisplayName, 240),
                Status = CelarAiOperationsPolicy.Clean(item.Status, 24),
                FailureCode = CelarAiOperationsPolicy.Clean(item.FailureCode, 120),
                Detail = CelarAiOperationsPolicy.SanitizeOperationalDetail(item.Detail),
                Source = CelarAiOperationsPolicy.Clean(item.Source, 500)
            })
            .ToArray();

    private static void ValidateDraft(CelarAiDefectDraft draft)
    {
        if (draft.Title.Length is < 3 or > 180) throw new InvalidOperationException("Provide a defect summary between 3 and 180 characters.");
        if (draft.Description.Length is < 10 or > 8000) throw new InvalidOperationException("Provide a defect description between 10 and 8000 characters.");
        if (!Categories.Contains(draft.Category)) throw new InvalidOperationException("Select an approved defect category.");
        if (!Priorities.Contains(draft.Priority)) throw new InvalidOperationException("Select an approved defect priority.");
        if (draft.Environment.Length == 0) throw new InvalidOperationException("Identify the affected environment.");
        if (draft.ActualBehavior.Length == 0) throw new InvalidOperationException("Describe the actual behavior.");
        if (draft.ExpectedBehavior.Length == 0) throw new InvalidOperationException("Describe the expected behavior.");
        if (draft.BusinessImpact.Length == 0) throw new InvalidOperationException("Describe the business or user impact.");
    }

    private static void RequireActualAuthority(Guid actualUserId, Guid effectiveUserId)
    {
        if (actualUserId == Guid.Empty || effectiveUserId == Guid.Empty)
            throw new UnauthorizedAccessException("An active Pulse identity is required.");
        if (actualUserId != effectiveUserId)
            throw new UnauthorizedAccessException("Exit Administrator View-As before changing a defect.");
    }

    private async Task<NpgsqlConnection> OpenReadyAsync(CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        if (!await MigrationReadyAsync(connection, cancellationToken))
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException("Migration 084 is required before Ask Celar AI defect operations can run.");
        }
        return connection;
    }

    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = ConnectionString();
        if (connectionString.Length == 0)
            throw new InvalidOperationException("The Pulse database connection is unavailable.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> MigrationReadyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                to_regclass('public.module076_defects') IS NOT NULL
                AND to_regclass('public.module076_intake_sessions') IS NOT NULL
                AND to_regclass('public.module076_monitor_policies') IS NOT NULL
                AND EXISTS(
                    SELECT 1 FROM schema_migrations
                    WHERE migration_id='084_module_076_celar_ai_defect_operations'
                );
            """, connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static string ConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var configured = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
        }
        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return string.Empty;
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Require,
            Timeout = 15,
            CommandTimeout = 30
        }.ConnectionString;
    }

    private static async Task<CelarAiDefectIdentity> ResolveIdentityByEmailAsync(
        NpgsqlConnection connection,
        string email,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("""
            SELECT user_id,COALESCE(NULLIF(display_name,''),email),email
            FROM app_users
            WHERE is_active=TRUE AND lower(email)=lower(@email)
            ORDER BY user_id
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("email", email);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new CelarAiDefectIdentity(null, CelarAiOperationsPolicy.DefaultAssigneeName, email, "identity_resolution_required");
        return new CelarAiDefectIdentity(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), "resolved_from_module_062_identity");
    }

    private static async Task<CelarAiDefectIdentity> ResolveIdentityByUserIdAsync(
        NpgsqlConnection connection,
        Guid userId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("""
            SELECT user_id,COALESCE(NULLIF(display_name,''),email),email
            FROM app_users
            WHERE user_id=@id AND is_active=TRUE
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new UnauthorizedAccessException("The actual user is not an active Module 062 identity.");
        return new CelarAiDefectIdentity(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), "active_module_062_identity");
    }

    private static async Task<CelarAiDefectIntakeSession?> LoadIntakeSessionAsync(
        NpgsqlConnection connection,
        Guid sessionId,
        Guid actualUserId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("""
            SELECT intake_session_id,actual_user_id,effective_user_id,conversation_id,
                   status,current_step,draft_document::text,diagnostic_evidence::text,
                   matched_defect_id,submitted_defect_id,revision_number,
                   created_at,updated_at,expires_at
            FROM module076_intake_sessions
            WHERE intake_session_id=@id AND actual_user_id=@actual
            LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("actual", actualUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new CelarAiDefectIntakeSession(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            JsonSerializer.Deserialize<CelarAiDefectDraft>(reader.GetString(6), Json)
                ?? throw new InvalidOperationException("The intake draft is invalid."),
            JsonSerializer.Deserialize<CelarAiProbeEvidence[]>(reader.GetString(7), Json) ?? [],
            reader.IsDBNull(8) ? null : reader.GetGuid(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.GetInt32(10),
            reader.GetFieldValue<DateTimeOffset>(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            reader.GetFieldValue<DateTimeOffset>(13));
    }

    private static async Task<CelarAiDefectRecord> InsertDefectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CelarAiDefectDraft draft,
        CelarAiDefectIdentity reporter,
        CelarAiDefectIdentity assignee,
        string sourceChannel,
        bool machineCreated,
        bool userConfirmed,
        string? fingerprint,
        string idempotencyKey,
        DateTimeOffset? firstObservedAt,
        DateTimeOffset? lastObservedAt,
        CancellationToken cancellationToken,
        object? metadata = null)
    {
        var defectId = Guid.NewGuid();
        var sequence = await NextDefectSequenceAsync(connection, transaction, cancellationToken);
        var number = $"DEF-{DateTimeOffset.UtcNow:yyyy}-{sequence:000000}";
        var now = DateTimeOffset.UtcNow;
        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_defects(
                defect_id,defect_number,title,description,category,priority,status,
                source_channel,environment,affected_system,affected_module,affected_route,
                expected_behavior,actual_behavior,reproduction_steps,business_impact,workaround,
                actual_reporter_user_id,effective_reporter_user_id,reporter_display_name,reporter_email,
                assignee_user_id,assignee_display_name,assignee_email,
                machine_created,user_confirmed,fingerprint,idempotency_key,correlation_id,release_sha,
                first_observed_at,last_observed_at,occurrence_count,metadata,
                date_added,created_at,updated_at)
            VALUES(
                @id,@number,@title,@description,@category,@priority,'Open',
                @source,@environment,@system,@module,@route,
                @expected,@actual,@steps::jsonb,@impact,@workaround,
                @actual_reporter,@effective_reporter,@reporter_name,@reporter_email,
                @assignee,@assignee_name,@assignee_email,
                @machine,@confirmed,@fingerprint,@idempotency,@correlation,@release,
                @first_observed,@last_observed,1,@metadata::jsonb,
                @now,@now,@now);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", defectId);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("title", draft.Title);
        command.Parameters.AddWithValue("description", draft.Description);
        command.Parameters.AddWithValue("category", draft.Category);
        command.Parameters.AddWithValue("priority", draft.Priority);
        command.Parameters.AddWithValue("source", sourceChannel);
        command.Parameters.AddWithValue("environment", draft.Environment);
        command.Parameters.AddWithValue("system", draft.AffectedSystem);
        command.Parameters.AddWithValue("module", draft.AffectedModule);
        command.Parameters.AddWithValue("route", draft.AffectedRoute);
        command.Parameters.AddWithValue("expected", draft.ExpectedBehavior);
        command.Parameters.AddWithValue("actual", draft.ActualBehavior);
        command.Parameters.AddWithValue("steps", JsonSerializer.Serialize(draft.ReproductionSteps, Json));
        command.Parameters.AddWithValue("impact", draft.BusinessImpact);
        command.Parameters.AddWithValue("workaround", draft.Workaround);
        command.Parameters.AddWithValue("actual_reporter", NpgsqlDbType.Uuid, reporter.UserId.HasValue ? reporter.UserId.Value : DBNull.Value);
        command.Parameters.AddWithValue("effective_reporter", NpgsqlDbType.Uuid, reporter.UserId.HasValue ? reporter.UserId.Value : DBNull.Value);
        command.Parameters.AddWithValue("reporter_name", reporter.DisplayName);
        command.Parameters.AddWithValue("reporter_email", reporter.Email);
        command.Parameters.AddWithValue("assignee", NpgsqlDbType.Uuid, assignee.UserId.HasValue ? assignee.UserId.Value : DBNull.Value);
        command.Parameters.AddWithValue("assignee_name", assignee.DisplayName);
        command.Parameters.AddWithValue("assignee_email", assignee.Email);
        command.Parameters.AddWithValue("machine", machineCreated);
        command.Parameters.AddWithValue("confirmed", userConfirmed);
        command.Parameters.AddWithValue("fingerprint", NpgsqlDbType.Char, fingerprint is null ? DBNull.Value : fingerprint);
        command.Parameters.AddWithValue("idempotency", idempotencyKey);
        command.Parameters.AddWithValue("correlation", draft.CorrelationId);
        command.Parameters.AddWithValue("release", NpgsqlDbType.Char, draft.ReleaseSha.Length == 40 ? draft.ReleaseSha : DBNull.Value);
        command.Parameters.AddWithValue("first_observed", NpgsqlDbType.TimestampTz, firstObservedAt.HasValue ? firstObservedAt.Value : DBNull.Value);
        command.Parameters.AddWithValue("last_observed", NpgsqlDbType.TimestampTz, lastObservedAt.HasValue ? lastObservedAt.Value : DBNull.Value);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata ?? new { }, Json));
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return await FindDefectByIdAsync(connection, transaction, defectId, cancellationToken)
            ?? throw new InvalidOperationException("The created defect could not be reloaded.");
    }

    private static async Task<long> NextDefectSequenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT nextval('module076_defect_number_sequence');",
            connection,
            transaction);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<CelarAiDefectRecord?> FindDefectByIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            DefectSelect + " WHERE d.idempotency_key=@key LIMIT 1;",
            connection,
            transaction);
        command.Parameters.AddWithValue("key", idempotencyKey);
        var rows = await ReadDefectsAsync(command, cancellationToken);
        return rows.FirstOrDefault();
    }

    private static async Task<CelarAiDefectRecord?> FindDefectByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid defectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            DefectSelect + " WHERE d.defect_id=@id LIMIT 1;",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", defectId);
        var rows = await ReadDefectsAsync(command, cancellationToken);
        return rows.FirstOrDefault();
    }

    private static async Task<CelarAiDefectRecord?> FindByFingerprintAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string environment,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            DefectSelect + """
             WHERE d.environment=@environment
               AND d.fingerprint=@fingerprint
               AND d.machine_created=TRUE
               AND d.status IN ('Open','In Progress','Blocked','Reopened')
             ORDER BY d.date_added DESC
             LIMIT 1;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("environment", environment);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        var rows = await ReadDefectsAsync(command, cancellationToken);
        return rows.FirstOrDefault();
    }

    private static async Task<IReadOnlyList<CelarAiDefectRecord>> ReadDefectsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var rows = new List<CelarAiDefectRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadDefect(reader));
        return rows;
    }

    private static CelarAiDefectRecord ReadDefect(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
            new CelarAiDefectIdentity(reader.IsDBNull(12) ? null : reader.GetGuid(12), reader.GetString(13), reader.GetString(14), "snapshot"),
            new CelarAiDefectIdentity(reader.IsDBNull(15) ? null : reader.GetGuid(15), reader.GetString(16), reader.GetString(17), "snapshot"),
            reader.GetBoolean(18), reader.GetString(19), reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
            reader.GetInt32(21), reader.GetInt32(22), reader.GetFieldValue<DateTimeOffset>(23),
            reader.IsDBNull(24) ? null : reader.GetFieldValue<DateTimeOffset>(24),
            reader.IsDBNull(25) ? null : reader.GetInt64(25), reader.GetInt32(26));

    private const string DefectSelect = """
        SELECT d.defect_id,d.defect_number,d.title,d.description,d.category,d.priority,d.status,
               d.source_channel,d.environment,d.affected_system,d.affected_module,d.affected_route,
               d.actual_reporter_user_id,d.reporter_display_name,d.reporter_email,
               d.assignee_user_id,d.assignee_display_name,d.assignee_email,
               d.machine_created,d.correlation_id,d.release_sha,
               d.occurrence_count,d.flapping_count,d.date_added,d.date_resolved,
               d.resolution_seconds,d.revision_number
        FROM module076_defects d
        """;

    private static async Task InsertEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid defectId,
        string evidenceType,
        string sourceCode,
        string sourceReference,
        string summary,
        object document,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_defect_evidence(
                evidence_id,defect_id,evidence_type,source_code,source_reference,
                sanitized_summary,evidence_document,contains_secret,
                raw_private_content_stored,observed_at)
            VALUES(@id,@defect,@type,@source,@reference,@summary,@document::jsonb,FALSE,FALSE,@observed);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("defect", defectId);
        command.Parameters.AddWithValue("type", CelarAiOperationsPolicy.Clean(evidenceType, 60));
        command.Parameters.AddWithValue("source", CelarAiOperationsPolicy.Clean(sourceCode, 100));
        command.Parameters.AddWithValue("reference", CelarAiOperationsPolicy.Clean(sourceReference, 500));
        command.Parameters.AddWithValue("summary", CelarAiOperationsPolicy.SanitizeOperationalDetail(summary));
        command.Parameters.AddWithValue("document", JsonSerializer.Serialize(document, Json));
        command.Parameters.AddWithValue("observed", observedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid defectId,
        string eventCode,
        string? previousStatus,
        string? nextStatus,
        string reason,
        Guid? actualActor,
        Guid? effectiveActor,
        object document,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_defect_events(
                event_id,defect_id,event_code,previous_status,next_status,reason,
                actual_actor_user_id,effective_actor_user_id,event_document,occurred_at)
            VALUES(@id,@defect,@code,@previous,@next,@reason,@actual,@effective,@document::jsonb,NOW());
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("defect", defectId);
        command.Parameters.AddWithValue("code", CelarAiOperationsPolicy.Clean(eventCode, 80));
        command.Parameters.AddWithValue("previous", NpgsqlDbType.Varchar, previousStatus is null ? DBNull.Value : previousStatus);
        command.Parameters.AddWithValue("next", NpgsqlDbType.Varchar, nextStatus is null ? DBNull.Value : nextStatus);
        command.Parameters.AddWithValue("reason", CelarAiOperationsPolicy.SanitizeOperationalDetail(reason));
        command.Parameters.AddWithValue("actual", NpgsqlDbType.Uuid, actualActor.HasValue ? actualActor.Value : DBNull.Value);
        command.Parameters.AddWithValue("effective", NpgsqlDbType.Uuid, effectiveActor.HasValue ? effectiveActor.Value : DBNull.Value);
        command.Parameters.AddWithValue("document", JsonSerializer.Serialize(document, Json));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCommentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid defectId,
        string commentType,
        string body,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_defect_comments(
                comment_id,defect_id,comment_type,body,actor_display_name,created_at)
            VALUES(@id,@defect,@type,@body,'Governed monitoring service',NOW());
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("defect", defectId);
        command.Parameters.AddWithValue("type", commentType);
        command.Parameters.AddWithValue("body", CelarAiOperationsPolicy.SanitizeOperationalDetail(body));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOccurrenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid defectId,
        string fingerprint,
        CelarAiProbeEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_incident_occurrences(
                occurrence_id,defect_id,fingerprint,component_code,probe_code,state,
                failure_code,sanitized_detail,latency_ms,http_status,observed_at)
            VALUES(@id,@defect,@fingerprint,@component,@probe,@state,@failure,@detail,@latency,@http,@observed);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("defect", defectId);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.Parameters.AddWithValue("component", evidence.ComponentCode);
        command.Parameters.AddWithValue("probe", evidence.ProbeCode);
        command.Parameters.AddWithValue("state", evidence.Status == "recovered" ? "recovered" : evidence.Status == "degraded" ? "degraded" : "failed");
        command.Parameters.AddWithValue("failure", evidence.FailureCode);
        command.Parameters.AddWithValue("detail", evidence.Detail);
        command.Parameters.AddWithValue("latency", NpgsqlDbType.Integer, evidence.LatencyMs.HasValue ? evidence.LatencyMs.Value : DBNull.Value);
        command.Parameters.AddWithValue("http", NpgsqlDbType.Integer, evidence.HttpStatus.HasValue ? evidence.HttpStatus.Value : DBNull.Value);
        command.Parameters.AddWithValue("observed", evidence.ObservedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertProbeResultAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CelarAiMonitorPolicy policy,
        CelarAiProbeEvidence evidence,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_probe_results(
                probe_result_id,policy_code,component_code,probe_code,status,
                failure_code,sanitized_detail,latency_ms,http_status,
                correlation_id,release_sha,observed_at)
            VALUES(@id,@policy,@component,@probe,@status,@failure,@detail,@latency,@http,@correlation,@release,@observed);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("policy", policy.PolicyCode);
        command.Parameters.AddWithValue("component", policy.ComponentCode);
        command.Parameters.AddWithValue("probe", evidence.ProbeCode);
        command.Parameters.AddWithValue("status", evidence.Status);
        command.Parameters.AddWithValue("failure", evidence.FailureCode);
        command.Parameters.AddWithValue("detail", evidence.Detail);
        command.Parameters.AddWithValue("latency", NpgsqlDbType.Integer, evidence.LatencyMs.HasValue ? evidence.LatencyMs.Value : DBNull.Value);
        command.Parameters.AddWithValue("http", NpgsqlDbType.Integer, evidence.HttpStatus.HasValue ? evidence.HttpStatus.Value : DBNull.Value);
        command.Parameters.AddWithValue("correlation", string.Empty);
        var release = CelarAiOperationsPolicy.ReleaseSha();
        command.Parameters.AddWithValue("release", NpgsqlDbType.Char, release.Length == 40 ? release : DBNull.Value);
        command.Parameters.AddWithValue("observed", evidence.ObservedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task QueueNotificationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CelarAiDefectRecord defect,
        string eventCode,
        string recipientPolicy,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_notification_outbox(
                outbox_id,defect_id,event_code,recipient_policy,idempotency_key,payload,status)
            VALUES(@id,@defect,@event,@recipients,@key,@payload::jsonb,'pending')
            ON CONFLICT (idempotency_key) DO NOTHING;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("defect", defect.DefectId);
        command.Parameters.AddWithValue("event", eventCode);
        command.Parameters.AddWithValue("recipients", recipientPolicy);
        command.Parameters.AddWithValue("key", idempotencyKey);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(new
        {
            defect.DefectId,
            defect.DefectNumber,
            defect.Title,
            defect.Priority,
            defect.Category,
            defect.Status,
            defect.Assignee,
            defect.Reporter,
            link = $"#defect-tracker?defect={Uri.EscapeDataString(defect.DefectNumber)}",
            secretValuesIncluded = false,
            rawPrivateContentIncluded = false
        }, Json));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> CanMutateDefectAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid defectId,
        Guid userId,
        bool canManage,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(
                SELECT 1 FROM module076_defects
                WHERE defect_id=@defect
                  AND (@manage=TRUE OR actual_reporter_user_id=@user OR assignee_user_id=@user)
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("defect", defectId);
        command.Parameters.AddWithValue("manage", canManage);
        command.Parameters.AddWithValue("user", userId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<bool> IsSuppressedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CelarAiMonitorPolicy policy,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(
                SELECT 1 FROM module076_monitor_suppressions
                WHERE environment=@environment
                  AND component_code=@component
                  AND starts_at<=@observed
                  AND expires_at>@observed
            );
            """, connection, transaction);
        command.Parameters.AddWithValue("environment", policy.Environment);
        command.Parameters.AddWithValue("component", policy.ComponentCode);
        command.Parameters.AddWithValue("observed", observedAt);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<bool> IsRecoveryStableAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CelarAiMonitorPolicy policy,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT MAX(observed_at)
            FROM module076_probe_results
            WHERE policy_code=@policy AND status IN ('failed','degraded');
            """, connection, transaction);
        command.Parameters.AddWithValue("policy", policy.PolicyCode);
        var lastFailure = await command.ExecuteScalarAsync(cancellationToken);
        return lastFailure is null or DBNull
            || observedAt - (DateTimeOffset)lastFailure >= TimeSpan.FromSeconds(policy.RecoveryStabilitySeconds);
    }

    private static async Task<bool> AutomaticDefectRateLimitReachedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int maximum,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*) >= @maximum
            FROM module076_defects
            WHERE machine_created=TRUE
              AND date_added >= NOW() - INTERVAL '1 hour';
            """, connection, transaction);
        command.Parameters.AddWithValue("maximum", Math.Min(maximum, CelarAiOperationsPolicy.MaximumAutomaticDefectsPerHour));
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private void LogFailure(string operation, Exception exception) =>
        _logger.LogWarning(
            "Celar AI operations could not {Operation} ({ExceptionType}).",
            operation,
            exception.GetType().Name);
}
