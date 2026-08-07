using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 998 runs safe ProjectPulse-native checks, persists diagnostic
/// sessions and evidence, ranks findings, and governs remediation requests.
/// The built-in health-refresh runbook can execute and verify without changing
/// infrastructure. Production-changing runbooks require separately approved
/// adapters and remain explicit rather than silently no-op.
/// </summary>
public static class SystemDiagnosticRemediationModule
{
    private const string ModuleNumber = "998";
    private const string ContractVersion = "2026-08-07.1";
    private static readonly TimeSpan RestartExecutionLease = TimeSpan.FromMinutes(3);
    private static readonly string[] ViewRoles = ["SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SECURITY_ANALYST", "SECURITY_OPERATIONS"];
    private static readonly string[] ManageRoles = ["SUPER_ADMINISTRATOR", "ADMINISTRATOR"];

    public static WebApplication MapSystemDiagnosticRemediationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system-diagnostics/overview", (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        app.MapGet("/api/system-diagnostics/checks", (Func<HttpContext, Task<IResult>>)GetChecksAsync);
        app.MapGet("/api/system-diagnostics/issues", (Func<HttpContext, Task<IResult>>)GetIssuesAsync);
        app.MapGet("/api/system-diagnostics/sessions", (Func<HttpContext, Task<IResult>>)GetSessionsAsync);
        app.MapGet("/api/system-diagnostics/sessions/{sessionId:guid}", (Guid sessionId, HttpContext context) => GetSessionAsync(sessionId, context));
        app.MapGet("/api/system-diagnostics/evidence-policy", (Func<HttpContext, Task<IResult>>)GetEvidencePolicyAsync);
        app.MapGet("/api/system-diagnostics/remediation-policy", (Func<HttpContext, Task<IResult>>)GetRemediationPolicyAsync);
        app.MapGet("/api/system-diagnostics/operations-adapter-readiness", (Func<HttpContext, Task<IResult>>)GetOperationsAdapterReadinessAsync);
        app.MapGet("/api/system-diagnostics/runbooks", (Func<HttpContext, Task<IResult>>)GetRunbooksAsync);
        app.MapGet("/api/system-diagnostics/remediations", (Func<HttpContext, Task<IResult>>)GetRemediationsAsync);

        app.MapPost("/api/system-diagnostics/sessions", (Func<HttpContext, Task<IResult>>)CreateSessionAsync);
        app.MapPost("/api/system-diagnostics/remediation/prepare", (Func<HttpContext, Task<IResult>>)PrepareRemediationAsync);
        app.MapPost("/api/system-diagnostics/remediation/approve", (Func<HttpContext, Task<IResult>>)ApproveRemediationAsync);
        app.MapPost("/api/system-diagnostics/remediation/stage", (Func<HttpContext, Task<IResult>>)StageRemediationAsync);
        app.MapPost("/api/system-diagnostics/remediation/promote", (Func<HttpContext, Task<IResult>>)ExecuteRemediationAsync);
        app.MapPost("/api/system-diagnostics/remediation/verify", (Func<HttpContext, Task<IResult>>)VerifyRemediationAsync);
        app.MapPost("/api/system-diagnostics/remediation/rollback", (Func<HttpContext, Task<IResult>>)(context => LockedAdapterAsync(context, "rollback", "Select an approved external runbook with a verified rollback adapter.")));
        app.MapPost("/api/system-diagnostics/remediation/close", (Func<HttpContext, Task<IResult>>)CloseRemediationAsync);
        app.MapPost("/api/system-diagnostics/analysis", (Func<HttpContext, Task<IResult>>)(context => LockedAdapterAsync(context, "ai_diagnostic_analysis", "Configure Module 064 diagnostic-analysis authority and an approved evidence-redaction policy.")));
        return app;
    }

    private static Task<SecurityDiagnosticsOperations.AccessOutcome> AuthorizeAsync(HttpContext context) =>
        SecurityDiagnosticsOperations.AuthorizeAsync(
            context, ModuleNumber, ViewRoles, ManageRoles,
            "VIEW_SYSTEM_DIAGNOSTICS", "MANAGE_SYSTEM_REMEDIATION");

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(connection, context.RequestAborted))
            return SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber);
        var metrics = await ReadMetricsAsync(connection, context.RequestAborted);
        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "System Diagnostic & Controlled Remediation Center",
            status = "diagnostic_operations_ready",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            runtimeEnvironment = SecurityDiagnosticsOperations.RuntimeEnvironment(),
            access = SecurityDiagnosticsOperations.AccessResponse(outcome.Access!, context, "restricted_operations"),
            posture = new
            {
                mode = "projectpulse_native_diagnostics_and_governed_remediation",
                safeChecksEnabled = true,
                diagnosticSessionPersistenceEnabled = true,
                incidentHandoffEnabled = true,
                nativeHealthRefreshEnabled = true,
                productionActionAdaptersEnabled = false,
                secretsRead = false,
                rawLogsRead = false
            },
            metrics,
            categories = DiagnosticCategories(),
            ownership = OwnershipLinks(),
            guardrails = Guardrails()
        });
    }

    private static async Task<IResult> GetChecksAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(connection, context.RequestAborted))
            return SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber);
        var checks = await ExecuteChecksAsync(connection, "platform", "ProjectPulse", context.RequestAborted);
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "diagnostic_checks_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            summary = Summarize(checks),
            checks,
            statement = "Every direct check is evaluated from sanitized ProjectPulse runtime and database metadata. External Azure, WAF, container, and network health remains adapter-required."
        });
    }

    private static async Task<IResult> GetIssuesAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(connection, context.RequestAborted))
            return SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber);
        var findings = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT f.diagnostic_finding_id, f.diagnostic_session_id, f.check_code, f.category,
                   f.status, f.severity, f.summary, f.evidence_json, f.observed_at,
                   s.target_kind, s.target_reference
            FROM projectpulse_diagnostic_findings f
            JOIN projectpulse_diagnostic_sessions s ON s.diagnostic_session_id = f.diagnostic_session_id
            WHERE f.status IN ('warning','failed','unknown') AND s.status <> 'closed'
            ORDER BY CASE f.severity WHEN 'critical' THEN 5 WHEN 'high' THEN 4 WHEN 'medium' THEN 3 WHEN 'low' THEN 2 ELSE 1 END DESC,
                     f.observed_at DESC
            LIMIT 200;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            findings.Add(new
            {
                findingId = reader.GetGuid(0), sessionId = reader.GetGuid(1), checkCode = reader.GetString(2),
                category = reader.GetString(3), status = reader.GetString(4), severity = reader.GetString(5),
                summary = reader.GetString(6), evidence = JsonSerializer.Deserialize<object>(reader.GetString(7)),
                observedAt = reader.GetFieldValue<DateTimeOffset>(8), targetKind = reader.GetString(9), targetReference = reader.GetString(10)
            });
        }
        return Results.Ok(new { module = ModuleNumber, status = "diagnostic_issues_loaded", activeIssues = findings, classifiers = IssueClassifiers(), liveCountAuthoritative = true });
    }

    private static async Task<IResult> GetSessionsAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(connection, context.RequestAborted))
            return SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber);
        return Results.Ok(new { module = ModuleNumber, status = "diagnostic_sessions_loaded", sessions = await ReadSessionsAsync(connection, null, context.RequestAborted) });
    }

    private static async Task<IResult> GetSessionAsync(Guid sessionId, HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        var sessions = await ReadSessionsAsync(connection, sessionId, context.RequestAborted);
        if (sessions.Count == 0) return Results.NotFound(new { module = ModuleNumber, status = "diagnostic_session_not_found" });
        return Results.Ok(new { module = ModuleNumber, session = sessions[0], findings = await ReadFindingsAsync(connection, sessionId, context.RequestAborted), remediations = await ReadRemediationsAsync(connection, sessionId, context.RequestAborted) });
    }

    private static async Task<IResult> CreateSessionAsync(HttpContext context)
    {
        var managed = await PrepareManagedBodyAsync<CreateSessionRequest>(context);
        if (managed.Result is not null) return managed.Result;
        var (connection, access, request) = managed.Value!;
        await using (connection)
        {
            var targetKind = request.TargetKind?.Trim().ToLowerInvariant() ?? "";
            var targetReference = request.TargetReference?.Trim() ?? "";
            if (!new[] { "platform", "api", "web", "database", "identity", "integration", "deployment", "incident" }.Contains(targetKind)
                || targetReference.Length is < 1 or > 250)
                return Results.BadRequest(new { module = ModuleNumber, status = "invalid_diagnostic_target" });

            if (request.IncidentId is not null)
            {
                await using var incidentCheck = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM projectpulse_security_incidents WHERE incident_id = @incident_id AND status <> 'closed');", connection);
                incidentCheck.Parameters.AddWithValue("incident_id", request.IncidentId.Value);
                if (await incidentCheck.ExecuteScalarAsync(context.RequestAborted) is not true)
                    return Results.NotFound(new { module = ModuleNumber, status = "active_incident_not_found" });
            }

            var sessionId = Guid.NewGuid();
            var checks = await ExecuteChecksAsync(connection, targetKind, targetReference, context.RequestAborted);
            var summary = Summarize(checks);
            var sessionStatus = checks.Any(check => check.Status == "failed") ? "attention_required" : "completed";
            var severity = HighestSeverity(checks);

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO projectpulse_diagnostic_sessions
                (diagnostic_session_id, incident_id, target_kind, target_reference, status, severity, summary, requested_by, completed_at)
                VALUES (@session_id, @incident_id, @target_kind, @target_reference, @status, @severity, @summary, @requested_by, now());
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("session_id", sessionId);
                insert.Parameters.AddWithValue("incident_id", (object?)request.IncidentId ?? DBNull.Value);
                insert.Parameters.AddWithValue("target_kind", targetKind);
                insert.Parameters.AddWithValue("target_reference", targetReference);
                insert.Parameters.AddWithValue("status", sessionStatus);
                insert.Parameters.AddWithValue("severity", severity);
                insert.Parameters.AddWithValue("summary", $"{summary.Healthy} healthy, {summary.Warning} warning, {summary.Failed} failed, {summary.Unknown} unknown.");
                insert.Parameters.AddWithValue("requested_by", access.UserId);
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await PersistFindingsAsync(connection, transaction, sessionId, checks, context.RequestAborted);

            if (request.IncidentId is Guid incidentId)
            {
                await using (var update = new NpgsqlCommand("UPDATE projectpulse_security_incidents SET diagnostic_session_id = @session_id, status = 'investigating', updated_at = now() WHERE incident_id = @incident_id;", connection, transaction))
                {
                    update.Parameters.AddWithValue("session_id", sessionId);
                    update.Parameters.AddWithValue("incident_id", incidentId);
                    await update.ExecuteNonQueryAsync(context.RequestAborted);
                }
                await using var timeline = new NpgsqlCommand("""
                    INSERT INTO projectpulse_security_incident_events
                    (event_id, incident_id, action_code, actor_user_id, note, evidence_json)
                    VALUES (@event_id, @incident_id, 'diagnostic_session_created', @actor, @note, CAST(@evidence AS jsonb));
                    """, connection, transaction);
                timeline.Parameters.AddWithValue("event_id", Guid.NewGuid());
                timeline.Parameters.AddWithValue("incident_id", incidentId);
                timeline.Parameters.AddWithValue("actor", access.UserId);
                timeline.Parameters.AddWithValue("note", (object?)request.Note?.Trim() ?? "Diagnostic session started from Module 997.");
                timeline.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(new { diagnosticSessionId = sessionId, targetKind, targetReference }));
                await timeline.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "diagnostic_session", sessionId.ToString(), "diagnostic_session_completed", access.UserId, new { request.IncidentId, targetKind, targetReference, sessionStatus, severity }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "diagnostic_session_completed", sessionId, incidentId = request.IncidentId, sessionStatus, severity, summary, findings = checks });
        }
    }

    private static async Task<IResult> GetEvidencePolicyAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new
        {
            module = ModuleNumber, status = "diagnostic_evidence_policy_loaded",
            evidence = new
            {
                classification = "restricted_operational_metadata", persistenceEnabled = true,
                rawLogAccessEnabled = false, secretAccessEnabled = false, connectionStringAccessEnabled = false,
                requiredFields = new[] { "session ID", "check code", "status", "severity", "sanitized summary", "observation time", "actor" },
                prohibited = new[] { "secret values", "tokens", "passwords", "connection strings", "raw provider payloads", "full log bodies" }
            }
        });
    }

    private static async Task<IResult> GetRemediationPolicyAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new
        {
            module = ModuleNumber, status = "remediation_policy_loaded", lifecycle = RemediationLifecycle(),
            gates = new { persistedPlan = true, requesterApproverSeparation = true, previewRequired = true, evidenceRequired = true, rollbackRequiredForExternalActions = true },
            execution = new { nativeActions = new[] { "refresh_health_snapshot" }, managedActions = AzureOperationsReadiness().Ready ? new[] { "restart_service" } : Array.Empty<string>(), externalActions = new[] { "scale_service", "rollback_deployment", "replay_integration_event", "refresh_configuration", "database_repair" }, externalAdapterConfigured = AzureOperationsReadiness().Ready }
        });
    }

    private static async Task<IResult> GetOperationsAdapterReadinessAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        var readiness = AzureOperationsReadiness();
        return Results.Ok(new { module = ModuleNumber, status = readiness.Ready ? "azure_operations_adapter_ready" : "azure_operations_adapter_setup_required", adapter = "azure_container_apps", enabled = readiness.Enabled, ready = readiness.Ready, readiness.Missing, allowedTargets = readiness.AllowedTargets, authentication = "managed_identity_no_client_secret", apiVersion = "2026-01-01", guardrails = new[] { "Exact allowlisted Container App target", "Diagnostic evidence before action", "Separate requester and approver", "Stage before execution", "Post-action verification", "Immutable audit evidence" } });
    }

    private static async Task<IResult> GetRunbooksAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new { module = ModuleNumber, status = "diagnostic_runbooks_loaded", executionMode = "native_safe_checks_and_adapter_gated_production_actions", runbooks = Runbooks() });
    }

    private static async Task<IResult> GetRemediationsAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new { module = ModuleNumber, status = "remediation_queue_loaded", remediations = await ReadRemediationsAsync(connection, null, context.RequestAborted) });
    }

    private static async Task<IResult> PrepareRemediationAsync(HttpContext context)
    {
        var managed = await PrepareManagedBodyAsync<PrepareRemediationRequest>(context);
        if (managed.Result is not null) return managed.Result;
        var (connection, access, request) = managed.Value!;
        await using (connection)
        {
            var runbook = Runbooks().FirstOrDefault(item => string.Equals(item.Id, request.RunbookCode, StringComparison.OrdinalIgnoreCase));
            var action = request.ActionCode?.Trim().ToLowerInvariant() ?? "";
            var target = request.TargetReference?.Trim() ?? "";
            var justification = request.Justification?.Trim() ?? "";
            if (runbook is null || !runbook.Actions.Contains(action) || target.Length is < 1 or > 250 || justification.Length is < 10 or > 2000)
                return Results.BadRequest(new { module = ModuleNumber, status = "invalid_remediation_plan" });

            await using var sessionCheck = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM projectpulse_diagnostic_sessions WHERE diagnostic_session_id = @session_id AND status <> 'closed');", connection);
            sessionCheck.Parameters.AddWithValue("session_id", request.DiagnosticSessionId);
            if (await sessionCheck.ExecuteScalarAsync(context.RequestAborted) is not true)
                return Results.NotFound(new { module = ModuleNumber, status = "active_diagnostic_session_not_found" });

            var remediationId = Guid.NewGuid();
            var plan = new { runbook = runbook.Id, action, target, justification, runbook.Owner, runbook.Adapter, runbook.Rollback, preview = runbook.Preview };
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO projectpulse_remediation_requests
                (remediation_request_id, diagnostic_session_id, runbook_code, action_code, target_reference, requested_by, plan_json)
                VALUES (@request_id, @session_id, @runbook, @action, @target, @requested_by, CAST(@plan AS jsonb));
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("request_id", remediationId);
                insert.Parameters.AddWithValue("session_id", request.DiagnosticSessionId);
                insert.Parameters.AddWithValue("runbook", runbook.Id);
                insert.Parameters.AddWithValue("action", action);
                insert.Parameters.AddWithValue("target", target);
                insert.Parameters.AddWithValue("requested_by", access.UserId);
                insert.Parameters.AddWithValue("plan", JsonSerializer.Serialize(plan));
                await insert.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "remediation_request", remediationId.ToString(), "remediation_prepared", access.UserId, new { request.DiagnosticSessionId, runbook = runbook.Id, action }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "remediation_awaiting_approval", remediationRequestId = remediationId, plan, nativeExecutionAvailable = action == "refresh_health_snapshot" });
        }
    }

    private static async Task<IResult> ApproveRemediationAsync(HttpContext context)
    {
        var managed = await PrepareManagedBodyAsync<RemediationActionRequest>(context);
        if (managed.Result is not null) return managed.Result;
        var (connection, access, request) = managed.Value!;
        await using (connection)
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                UPDATE projectpulse_remediation_requests
                SET state = 'approved', approved_by = @actor, approved_at = now()
                WHERE remediation_request_id = @request_id AND state = 'awaiting_approval' AND requested_by <> @actor
                RETURNING diagnostic_session_id, action_code;
                """, connection, transaction);
            command.Parameters.AddWithValue("actor", access.UserId);
            command.Parameters.AddWithValue("request_id", request.RemediationRequestId);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted))
                return Results.Json(new { module = ModuleNumber, status = "approval_rejected", message = "The request is not awaiting approval or separation of duties was not met." }, statusCode: StatusCodes.Status409Conflict);
            var sessionId = reader.GetGuid(0); var action = reader.GetString(1); await reader.DisposeAsync();
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "remediation_request", request.RemediationRequestId.ToString(), "remediation_approved", access.UserId, new { sessionId, action }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "remediation_approved", request.RemediationRequestId, action });
        }
    }

    private static Task<IResult> StageRemediationAsync(HttpContext context) => TransitionRemediationAsync(context, "approved", "staged", "remediation_staged");

    private static async Task<IResult> ExecuteRemediationAsync(HttpContext context)
    {
        var managed = await PrepareManagedBodyAsync<RemediationActionRequest>(context);
        if (managed.Result is not null) return managed.Result;
        var (connection, access, request) = managed.Value!;
        await using (connection)
        {
            await using var read = new NpgsqlCommand("""
                SELECT diagnostic_session_id, action_code, target_reference, state, requested_by, approved_by,
                       executed_by, COALESCE(result_json::text, '{}')
                FROM projectpulse_remediation_requests WHERE remediation_request_id = @request_id;
                """, connection);
            read.Parameters.AddWithValue("request_id", request.RemediationRequestId);
            await using var reader = await read.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted)) return Results.NotFound(new { module = ModuleNumber, status = "remediation_not_found" });
            var sessionId = reader.GetGuid(0); var action = reader.GetString(1); var target = reader.GetString(2); var state = reader.GetString(3);
            var requestedBy = reader.GetGuid(4); var approvedBy = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
            var executedBy = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6); var existingResultJson = reader.GetString(7); await reader.DisposeAsync();
            if (approvedBy is null || requestedBy == approvedBy)
                return Results.Json(new { module = ModuleNumber, status = "approved_remediation_required" }, statusCode: StatusCodes.Status409Conflict);
            if (action == "restart_service")
            {
                if (state != "staged")
                    return Results.Json(new { module = ModuleNumber, status = "staged_remediation_required", message = "Restart execution requires the approved plan to complete target and rollback-readiness staging." }, statusCode: StatusCodes.Status409Conflict);

                var readiness = AzureOperationsReadiness();
                var claimedAt = DateTimeOffset.UtcNow;
                if (executedBy.HasValue)
                {
                    var existingClaim = ReadRestartExecutionClaim(existingResultJson);
                    if (existingClaim?.LeaseExpiresAt is DateTimeOffset leaseExpiresAt && leaseExpiresAt > claimedAt)
                        return Results.Json(new
                        {
                            module = ModuleNumber,
                            status = "remediation_execution_in_progress",
                            action,
                            target,
                            existingClaim.ClaimId,
                            existingClaim.ClaimedAt,
                            leaseExpiresAt,
                            retryAllowed = false
                        }, statusCode: StatusCodes.Status409Conflict);

                    var reconciledAt = DateTimeOffset.UtcNow;
                    var recoveryResult = new
                    {
                        action,
                        target,
                        outcome = "indeterminate",
                        manualReconciliationRequired = true,
                        retryAllowed = false,
                        reason = "restart_execution_lease_expired_before_final_evidence",
                        priorClaimId = existingClaim?.ClaimId,
                        priorClaimedAt = existingClaim?.ClaimedAt,
                        priorLeaseExpiresAt = existingClaim?.LeaseExpiresAt,
                        reconciledAt,
                        provider = "azure_container_apps"
                    };
                    await using var recoveryTransaction = await connection.BeginTransactionAsync(context.RequestAborted);
                    await using var recovery = new NpgsqlCommand("""
                        UPDATE projectpulse_remediation_requests
                        SET state = 'failed', executed_at = now(), result_json = CAST(@result AS jsonb)
                        WHERE remediation_request_id = @request_id
                          AND state = 'staged'
                          AND executed_by = @executed_by
                          AND result_json = CAST(@expected_result AS jsonb);
                        """, connection, recoveryTransaction);
                    recovery.Parameters.AddWithValue("result", JsonSerializer.Serialize(recoveryResult));
                    recovery.Parameters.AddWithValue("request_id", request.RemediationRequestId);
                    recovery.Parameters.AddWithValue("executed_by", executedBy.Value);
                    recovery.Parameters.AddWithValue("expected_result", existingResultJson);
                    if (await recovery.ExecuteNonQueryAsync(context.RequestAborted) != 1)
                        return Results.Json(new { module = ModuleNumber, status = "remediation_execution_already_claimed", action, target, retryAllowed = false }, statusCode: StatusCodes.Status409Conflict);

                    await SecurityDiagnosticsOperations.WriteAuditAsync(connection, recoveryTransaction, ModuleNumber, "remediation_request", request.RemediationRequestId.ToString(), "azure_container_app_restart_claim_expired", access.UserId, new
                    {
                        sessionId,
                        target,
                        priorExecutor = executedBy.Value,
                        priorClaimId = existingClaim?.ClaimId,
                        priorClaimedAt = existingClaim?.ClaimedAt,
                        priorLeaseExpiresAt = existingClaim?.LeaseExpiresAt,
                        reconciledAt,
                        manualReconciliationRequired = true
                    }, context.RequestAborted);
                    await recoveryTransaction.CommitAsync(context.RequestAborted);
                    return Results.Json(new
                    {
                        module = ModuleNumber,
                        status = "azure_restart_reconciliation_required",
                        action,
                        target,
                        remediationState = "failed",
                        manualReconciliationRequired = true,
                        retryAllowed = false,
                        message = "The restart claim expired before final Azure evidence was stored. Review Azure state and prepare a new approved request only after reconciliation."
                    }, statusCode: StatusCodes.Status409Conflict);
                }

                if (!readiness.Ready)
                    return Results.Json(new { module = ModuleNumber, status = "execution_adapter_required", action, target, configured = false, missing = readiness.Missing, requiredConfiguration = AdapterConfiguration(action) }, statusCode: StatusCodes.Status423Locked);
                if (!readiness.AllowedTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
                    return Results.Json(new { module = ModuleNumber, status = "restart_target_not_allowed", action, target, allowedTargets = readiness.AllowedTargets, message = "The target must exactly match an administrator-approved Container App." }, statusCode: StatusCodes.Status403Forbidden);

                var claimId = Guid.NewGuid();
                var leaseExpiresAt = claimedAt.Add(RestartExecutionLease);
                await using (var claimTransaction = await connection.BeginTransactionAsync(context.RequestAborted))
                {
                    await using var claim = new NpgsqlCommand("""
                        UPDATE projectpulse_remediation_requests
                        SET executed_by = @actor, result_json = CAST(@result AS jsonb)
                        WHERE remediation_request_id = @request_id
                          AND state = 'staged'
                          AND executed_by IS NULL
                        RETURNING remediation_request_id;
                        """, connection, claimTransaction);
                    claim.Parameters.AddWithValue("actor", access.UserId);
                    claim.Parameters.AddWithValue("result", JsonSerializer.Serialize(new { action, target, outcome = "in_progress", claimId, claimedAt, leaseExpiresAt, provider = "azure_container_apps" }));
                    claim.Parameters.AddWithValue("request_id", request.RemediationRequestId);
                    var claimed = await claim.ExecuteScalarAsync(context.RequestAborted);
                    if (claimed is not Guid)
                        return Results.Json(new { module = ModuleNumber, status = "remediation_execution_already_claimed", action, target, retryAllowed = false }, statusCode: StatusCodes.Status409Conflict);

                    await SecurityDiagnosticsOperations.WriteAuditAsync(connection, claimTransaction, ModuleNumber, "remediation_request", request.RemediationRequestId.ToString(), "azure_container_app_restart_claimed", access.UserId, new { sessionId, target, claimId, claimedAt, leaseExpiresAt }, context.RequestAborted);
                    await claimTransaction.CommitAsync(context.RequestAborted);
                }

                // Once the database claim is committed, execution is server-owned and
                // bounded. A client disconnect must not interrupt Azure mid-sequence and
                // prevent the accepted-revision evidence from being finalized.
                using var executionTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var restart = await RestartContainerAppAsync(readiness, target, executionTimeout.Token);
                var partialExecution = !restart.Success && restart.AcceptedRevisions.Length > 0;
                var finalState = restart.Success || partialExecution ? "executed" : "failed";
                var outcome = restart.Success ? "succeeded" : partialExecution ? "partial" : "failed";
                var auditEvent = restart.Success
                    ? "azure_container_app_revision_restart_executed"
                    : partialExecution
                        ? "azure_container_app_revision_restart_partially_executed"
                        : "azure_container_app_revision_restart_failed";
                var observedAt = DateTimeOffset.UtcNow;
                var result = new
                {
                    action,
                    target,
                    outcome,
                    claimId,
                    claimedAt,
                    leaseExpiresAt,
                    discoveredRevisions = restart.DiscoveredRevisions,
                    acceptedRevisions = restart.AcceptedRevisions,
                    restart.FailedRevision,
                    restart.HttpStatus,
                    restart.Message,
                    observedAt,
                    provider = "azure_container_apps"
                };

                using var persistenceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await using var restartTransaction = await connection.BeginTransactionAsync(persistenceTimeout.Token);
                await using (var restartUpdate = new NpgsqlCommand("""
                    UPDATE projectpulse_remediation_requests
                    SET state = @state, executed_at = now(), result_json = CAST(@result AS jsonb)
                    WHERE remediation_request_id = @request_id
                      AND state = 'staged'
                      AND executed_by = @actor
                      AND result_json->>'claimId' = @claim_id;
                    """, connection, restartTransaction))
                {
                    restartUpdate.Parameters.AddWithValue("state", finalState);
                    restartUpdate.Parameters.AddWithValue("actor", access.UserId);
                    restartUpdate.Parameters.AddWithValue("claim_id", claimId.ToString());
                    restartUpdate.Parameters.AddWithValue("result", JsonSerializer.Serialize(result));
                    restartUpdate.Parameters.AddWithValue("request_id", request.RemediationRequestId);
                    if (await restartUpdate.ExecuteNonQueryAsync(persistenceTimeout.Token) != 1)
                        return Results.Json(new { module = ModuleNumber, status = "remediation_execution_claim_lost", action, target, infrastructureStateChanged = restart.AcceptedRevisions.Length > 0, retryAllowed = false }, statusCode: StatusCodes.Status409Conflict);
                }
                await SecurityDiagnosticsOperations.WriteAuditAsync(connection, restartTransaction, ModuleNumber, "remediation_request", request.RemediationRequestId.ToString(), auditEvent, access.UserId, new { sessionId, target, outcome, discoveredRevisions = restart.DiscoveredRevisions, acceptedRevisions = restart.AcceptedRevisions, restart.FailedRevision, restart.HttpStatus }, persistenceTimeout.Token);
                await restartTransaction.CommitAsync(persistenceTimeout.Token);

                if (!restart.Success)
                    return Results.Json(new
                    {
                        module = ModuleNumber,
                        status = partialExecution ? "azure_restart_partially_accepted" : "azure_restart_failed",
                        action,
                        target,
                        restart.HttpStatus,
                        restart.Message,
                        remediationState = finalState,
                        infrastructureStateChanged = restart.AcceptedRevisions.Length > 0,
                        discoveredRevisions = restart.DiscoveredRevisions,
                        acceptedRevisions = restart.AcceptedRevisions,
                        restart.FailedRevision,
                        retryAllowed = false,
                        verificationRequired = partialExecution
                    }, statusCode: StatusCodes.Status502BadGateway);

                return Results.Accepted(value: new { module = ModuleNumber, status = "azure_container_app_restart_accepted", request.RemediationRequestId, sessionId, target, revisions = restart.AcceptedRevisions, verificationRequired = true });
            }
            if (state is not ("approved" or "staged"))
                return Results.Json(new { module = ModuleNumber, status = "approved_remediation_required" }, statusCode: StatusCodes.Status409Conflict);
            if (action != "refresh_health_snapshot")
                return Results.Json(new { module = ModuleNumber, status = "execution_adapter_required", action, target, configured = false, requiredConfiguration = AdapterConfiguration(action), message = "The approved plan is preserved; connect the owning adapter before production execution." }, statusCode: StatusCodes.Status423Locked);

            var checks = await ExecuteChecksAsync(connection, "platform", target, context.RequestAborted);
            var summary = Summarize(checks);
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using (var update = new NpgsqlCommand("""
                UPDATE projectpulse_remediation_requests
                SET state = 'executed', executed_by = @actor, executed_at = now(), result_json = CAST(@result AS jsonb)
                WHERE remediation_request_id = @request_id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("actor", access.UserId);
                update.Parameters.AddWithValue("result", JsonSerializer.Serialize(new { action, observedAt = DateTimeOffset.UtcNow, summary }));
                update.Parameters.AddWithValue("request_id", request.RemediationRequestId);
                await update.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await ReplaceFindingsAsync(connection, transaction, sessionId, checks, context.RequestAborted);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "remediation_request", request.RemediationRequestId.ToString(), "native_health_refresh_executed", access.UserId, new { sessionId, summary }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "native_health_refresh_executed", request.RemediationRequestId, sessionId, summary, findings = checks });
        }
    }

    private static async Task<IResult> VerifyRemediationAsync(HttpContext context)
    {
        var managed = await PrepareManagedBodyAsync<RemediationActionRequest>(context);
        if (managed.Result is not null) return managed.Result;
        var (connection, access, request) = managed.Value!;
        await using (connection)
        {
            await using var read = new NpgsqlCommand("SELECT diagnostic_session_id, target_reference, state, action_code, COALESCE(result_json::text, '{}') FROM projectpulse_remediation_requests WHERE remediation_request_id = @request_id;", connection);
            read.Parameters.AddWithValue("request_id", request.RemediationRequestId);
            await using var reader = await read.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted)) return Results.NotFound(new { module = ModuleNumber, status = "remediation_not_found" });
            var sessionId = reader.GetGuid(0); var target = reader.GetString(1); var state = reader.GetString(2); var action = reader.GetString(3); var executionResultJson = reader.GetString(4); await reader.DisposeAsync();
            if (state != "executed") return Results.Json(new { module = ModuleNumber, status = "executed_remediation_required" }, statusCode: StatusCodes.Status409Conflict);
            var checks = await ExecuteChecksAsync(connection, "platform", target, context.RequestAborted);
            AzureRestartVerificationResult? azureVerification = null;
            if (action == "restart_service")
            {
                var readiness = AzureOperationsReadiness();
                var acceptedRevisions = ReadStringArray(executionResultJson, "acceptedRevisions");
                if (!readiness.Ready)
                    azureVerification = new(false, 423, "Azure restart verification is unavailable because the managed-identity adapter is not ready.", acceptedRevisions, [], acceptedRevisions, []);
                else if (!readiness.AllowedTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
                    azureVerification = new(false, 403, "Azure restart verification rejected a target outside the current allowlist.", acceptedRevisions, [], acceptedRevisions, []);
                else if (acceptedRevisions.Length == 0)
                    azureVerification = new(false, 409, "No accepted Azure revision evidence was retained for verification.", acceptedRevisions, [], acceptedRevisions, []);
                else
                {
                    using var verificationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    azureVerification = await VerifyContainerAppRestartAsync(readiness, target, acceptedRevisions, verificationTimeout.Token);
                }

                checks.Add(new DiagnosticFinding(
                    "azure_restart_verification",
                    "cloud_platform",
                    azureVerification.Success ? "healthy" : "failed",
                    azureVerification.Success ? "informational" : "high",
                    azureVerification.Message,
                    new
                    {
                        provider = "azure_container_apps",
                        azureVerification.HttpStatus,
                        expectedRevisions = azureVerification.ExpectedRevisions,
                        observedRevisions = azureVerification.ObservedRevisions,
                        missingRevisions = azureVerification.MissingRevisions,
                        unhealthyRevisions = azureVerification.UnhealthyRevisions
                    }));
            }
            var summary = Summarize(checks);
            var verified = (azureVerification?.Success ?? true) && !checks.Any(check => check.Status == "failed");
            var observedAt = DateTimeOffset.UtcNow;
            var verificationResult = new
            {
                verified,
                observedAt,
                summary,
                azure = azureVerification is null ? null : new
                {
                    azureVerification.Success,
                    azureVerification.HttpStatus,
                    azureVerification.Message,
                    azureVerification.ExpectedRevisions,
                    azureVerification.ObservedRevisions,
                    azureVerification.MissingRevisions,
                    azureVerification.UnhealthyRevisions
                }
            };
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using (var update = new NpgsqlCommand("UPDATE projectpulse_remediation_requests SET state = @state, verified_at = CASE WHEN @verified THEN now() ELSE verified_at END, result_json = CAST(@result AS jsonb) WHERE remediation_request_id = @request_id;", connection, transaction))
            {
                update.Parameters.AddWithValue("state", verified ? "verified" : "failed");
                update.Parameters.AddWithValue("verified", verified);
                update.Parameters.AddWithValue("result", JsonSerializer.Serialize(new { execution = ReadJsonEvidence(executionResultJson), verification = verificationResult }));
                update.Parameters.AddWithValue("request_id", request.RemediationRequestId);
                await update.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await ReplaceFindingsAsync(connection, transaction, sessionId, checks, context.RequestAborted);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "remediation_request", request.RemediationRequestId.ToString(), verified ? "remediation_verified" : "remediation_verification_failed", access.UserId, new { sessionId, action, summary, azureVerification }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = verified ? "remediation_verified" : "remediation_verification_failed", request.RemediationRequestId, sessionId, summary, azureVerification, findings = checks });
        }
    }

    private static async Task<IResult> CloseRemediationAsync(HttpContext context)
    {
        var managed = await PrepareManagedBodyAsync<RemediationActionRequest>(context);
        if (managed.Result is not null) return managed.Result;
        var (connection, access, request) = managed.Value!;
        await using (connection)
        {
            if (string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length < 10)
                return Results.BadRequest(new { module = ModuleNumber, status = "closure_evidence_required" });
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using var update = new NpgsqlCommand("UPDATE projectpulse_remediation_requests SET state = 'closed', closed_at = now() WHERE remediation_request_id = @request_id AND state IN ('verified','rolled_back');", connection, transaction);
            update.Parameters.AddWithValue("request_id", request.RemediationRequestId);
            if (await update.ExecuteNonQueryAsync(context.RequestAborted) == 0)
                return Results.Json(new { module = ModuleNumber, status = "remediation_not_ready_to_close" }, statusCode: StatusCodes.Status409Conflict);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "remediation_request", request.RemediationRequestId.ToString(), "remediation_closed", access.UserId, new { note = request.Note.Trim() }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "remediation_closed", request.RemediationRequestId });
        }
    }

    private static async Task<IResult> TransitionRemediationAsync(HttpContext context, string from, string to, string auditAction)
    {
        var managed = await PrepareManagedBodyAsync<RemediationActionRequest>(context);
        if (managed.Result is not null) return managed.Result;
        var (connection, access, request) = managed.Value!;
        await using (connection)
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using var update = new NpgsqlCommand("UPDATE projectpulse_remediation_requests SET state = @to WHERE remediation_request_id = @request_id AND state = @from;", connection, transaction);
            update.Parameters.AddWithValue("to", to); update.Parameters.AddWithValue("from", from); update.Parameters.AddWithValue("request_id", request.RemediationRequestId);
            if (await update.ExecuteNonQueryAsync(context.RequestAborted) == 0)
                return Results.Json(new { module = ModuleNumber, status = "invalid_remediation_transition", expectedState = from }, statusCode: StatusCodes.Status409Conflict);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "remediation_request", request.RemediationRequestId.ToString(), auditAction, access.UserId, new { from, to }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = auditAction, request.RemediationRequestId });
        }
    }

    private static async Task<IResult> LockedAdapterAsync(HttpContext context, string operation, string configurationPath)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        var blocked = SecurityDiagnosticsOperations.RequireMutation(context, ModuleNumber, outcome.Access!); if (blocked is not null) return blocked;
        return Results.Json(new { module = ModuleNumber, status = "execution_adapter_required", operation, adapterInvoked = false, stateChanged = false, configurationPath, message = "The diagnostic and approval evidence remains available; this production action needs an approved adapter." }, statusCode: StatusCodes.Status423Locked);
    }

    private static async Task<(ManagedBody<T>? Value, IResult? Result)> PrepareManagedBodyAsync<T>(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return (null, outcome.Failure);
        var blocked = SecurityDiagnosticsOperations.RequireMutation(context, ModuleNumber, outcome.Access!);
        if (blocked is not null) { await outcome.Connection!.DisposeAsync(); return (null, blocked); }
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(outcome.Connection!, context.RequestAborted))
        { await outcome.Connection!.DisposeAsync(); return (null, SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber)); }
        var body = await SecurityDiagnosticsOperations.ReadBodyAsync<T>(context, ModuleNumber);
        if (body.Failure is not null) { await outcome.Connection!.DisposeAsync(); return (null, body.Failure); }
        return (new ManagedBody<T>(outcome.Connection!, outcome.Access!, body.Value!), null);
    }

    private static async Task<List<DiagnosticFinding>> ExecuteChecksAsync(NpgsqlConnection connection, string targetKind, string targetReference, CancellationToken cancellationToken)
    {
        var findings = new List<DiagnosticFinding>
        {
            new("database_connectivity", "data_resilience", "healthy", "informational", "ProjectPulse database connection and authorization query succeeded.", new { targetKind, targetReference }),
            new("api_request_path", "application_runtime", "healthy", "informational", "The authenticated Module 998 API request completed to the diagnostic engine.", new { environment = SecurityDiagnosticsOperations.RuntimeEnvironment() })
        };

        await using var command = new NpgsqlCommand("""
            SELECT
                (SELECT COUNT(*) FROM auth_login_events WHERE created_at >= now() - interval '1 hour' AND lower(login_result) NOT IN ('success','succeeded')),
                (SELECT COUNT(*) FROM projectpulse_security_incidents WHERE status <> 'closed'),
                (SELECT COUNT(*) FROM projectpulse_security_incidents WHERE status <> 'closed' AND severity IN ('high','critical')),
                (SELECT COUNT(*) FROM projectpulse_security_response_requests WHERE state = 'awaiting_approval'),
                (SELECT COUNT(*) FROM projectpulse_remediation_requests WHERE state IN ('awaiting_approval','approved','staged','executed')),
                (SELECT MAX(applied_at) FROM schema_migrations),
                (SELECT COUNT(*) FROM auth_sessions WHERE revoked_at IS NULL AND expires_at > now()),
                (SELECT COUNT(*) FROM auth_sessions WHERE revoked_at IS NOT NULL AND revoked_at >= now() - interval '24 hours');
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var failedLogins = reader.GetInt64(0); var incidents = reader.GetInt64(1); var severeIncidents = reader.GetInt64(2);
        var containmentQueue = reader.GetInt64(3); var remediationQueue = reader.GetInt64(4);
        var latestMigration = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5);
        var activeSessions = reader.GetInt64(6); var revokedSessions = reader.GetInt64(7);

        findings.Add(new("authentication_failures", "identity_access", failedLogins >= 20 ? "failed" : failedLogins >= 5 ? "warning" : "healthy", failedLogins >= 20 ? "high" : failedLogins >= 5 ? "medium" : "informational", $"{failedLogins} failed authentication events were recorded in the last hour.", new { failedLogins, windowMinutes = 60 }));
        findings.Add(new("security_incident_queue", "security_operations", severeIncidents > 0 ? "failed" : incidents > 0 ? "warning" : "healthy", severeIncidents > 0 ? "high" : incidents > 0 ? "medium" : "informational", $"{incidents} active incidents, including {severeIncidents} high or critical incidents.", new { incidents, severeIncidents, route = "#security-operations" }));
        findings.Add(new("containment_approval_queue", "security_operations", containmentQueue > 0 ? "warning" : "healthy", containmentQueue > 0 ? "medium" : "informational", $"{containmentQueue} containment requests are waiting for a separate approver.", new { containmentQueue }));
        findings.Add(new("remediation_queue", "application_runtime", remediationQueue > 10 ? "warning" : "healthy", remediationQueue > 10 ? "low" : "informational", $"{remediationQueue} remediation requests are currently open.", new { remediationQueue }));
        findings.Add(new("schema_migration_recency", "delivery", latestMigration is null ? "unknown" : "healthy", latestMigration is null ? "medium" : "informational", latestMigration is null ? "No schema migration timestamp was available." : $"Latest recorded migration was applied {latestMigration:O}.", new { latestMigration }));
        findings.Add(new("session_inventory", "identity_access", "healthy", "informational", $"{activeSessions} active sessions and {revokedSessions} revocations in the last 24 hours.", new { activeSessions, revokedSessions }));
        findings.Add(new("external_infrastructure", "cloud_platform", "unknown", "informational", "Azure Container Apps, Application Gateway/WAF, DNS, certificate, and regional resource health require the approved Azure diagnostics adapter.", new { adapter = "azure_diagnostics", configured = false }));
        return findings;
    }

    private static async Task PersistFindingsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sessionId, IEnumerable<DiagnosticFinding> findings, CancellationToken cancellationToken)
    {
        foreach (var finding in findings)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO projectpulse_diagnostic_findings
                (diagnostic_finding_id, diagnostic_session_id, check_code, category, status, severity, summary, evidence_json)
                VALUES (@finding_id, @session_id, @check_code, @category, @status, @severity, @summary, CAST(@evidence AS jsonb));
                """, connection, transaction);
            command.Parameters.AddWithValue("finding_id", Guid.NewGuid()); command.Parameters.AddWithValue("session_id", sessionId);
            command.Parameters.AddWithValue("check_code", finding.CheckCode); command.Parameters.AddWithValue("category", finding.Category);
            command.Parameters.AddWithValue("status", finding.Status); command.Parameters.AddWithValue("severity", finding.Severity);
            command.Parameters.AddWithValue("summary", finding.Summary); command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(finding.Evidence));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceFindingsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid sessionId, IEnumerable<DiagnosticFinding> findings, CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand("DELETE FROM projectpulse_diagnostic_findings WHERE diagnostic_session_id = @session_id;", connection, transaction))
        { delete.Parameters.AddWithValue("session_id", sessionId); await delete.ExecuteNonQueryAsync(cancellationToken); }
        await PersistFindingsAsync(connection, transaction, sessionId, findings, cancellationToken);
        var items = findings.ToArray(); var summary = Summarize(items);
        await using var update = new NpgsqlCommand("UPDATE projectpulse_diagnostic_sessions SET status = @status, severity = @severity, summary = @summary, completed_at = now(), updated_at = now() WHERE diagnostic_session_id = @session_id;", connection, transaction);
        update.Parameters.AddWithValue("status", items.Any(item => item.Status == "failed") ? "attention_required" : "completed");
        update.Parameters.AddWithValue("severity", HighestSeverity(items));
        update.Parameters.AddWithValue("summary", $"{summary.Healthy} healthy, {summary.Warning} warning, {summary.Failed} failed, {summary.Unknown} unknown.");
        update.Parameters.AddWithValue("session_id", sessionId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<object>> ReadSessionsAsync(NpgsqlConnection connection, Guid? sessionId, CancellationToken cancellationToken)
    {
        var sessions = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT s.diagnostic_session_id, s.incident_id, s.target_kind, s.target_reference, s.status,
                   s.severity, s.summary, s.requested_by, COALESCE(u.display_name, u.email, s.requested_by::text),
                   s.created_at, s.completed_at, s.closed_at, s.updated_at,
                   COUNT(f.diagnostic_finding_id),
                   COUNT(f.diagnostic_finding_id) FILTER (WHERE f.status = 'failed'),
                   COUNT(f.diagnostic_finding_id) FILTER (WHERE f.status = 'warning')
            FROM projectpulse_diagnostic_sessions s
            LEFT JOIN app_users u ON u.user_id = s.requested_by
            LEFT JOIN projectpulse_diagnostic_findings f ON f.diagnostic_session_id = s.diagnostic_session_id
            WHERE (@session_id IS NULL OR s.diagnostic_session_id = @session_id)
            GROUP BY s.diagnostic_session_id, u.display_name, u.email
            ORDER BY s.updated_at DESC LIMIT 200;
            """, connection);
        command.Parameters.Add("session_id", NpgsqlDbType.Uuid).Value = (object?)sessionId ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new
            {
                sessionId = reader.GetGuid(0), incidentId = reader.IsDBNull(1) ? null : reader.GetGuid(1).ToString(),
                targetKind = reader.GetString(2), targetReference = reader.GetString(3), status = reader.GetString(4), severity = reader.GetString(5),
                summary = reader.IsDBNull(6) ? null : reader.GetString(6), requestedByUserId = reader.GetGuid(7), requestedBy = reader.GetString(8),
                createdAt = reader.GetFieldValue<DateTimeOffset>(9), completedAt = reader.IsDBNull(10) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(10),
                closedAt = reader.IsDBNull(11) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(11), updatedAt = reader.GetFieldValue<DateTimeOffset>(12),
                findingCount = reader.GetInt64(13), failedCount = reader.GetInt64(14), warningCount = reader.GetInt64(15)
            });
        }
        return sessions;
    }

    private static async Task<List<object>> ReadFindingsAsync(NpgsqlConnection connection, Guid sessionId, CancellationToken cancellationToken)
    {
        var findings = new List<object>();
        await using var command = new NpgsqlCommand("SELECT diagnostic_finding_id, check_code, category, status, severity, summary, evidence_json, observed_at FROM projectpulse_diagnostic_findings WHERE diagnostic_session_id = @session_id ORDER BY observed_at, check_code;", connection);
        command.Parameters.AddWithValue("session_id", sessionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) findings.Add(new { findingId = reader.GetGuid(0), checkCode = reader.GetString(1), category = reader.GetString(2), status = reader.GetString(3), severity = reader.GetString(4), summary = reader.GetString(5), evidence = JsonSerializer.Deserialize<object>(reader.GetString(6)), observedAt = reader.GetFieldValue<DateTimeOffset>(7) });
        return findings;
    }

    private static async Task<List<object>> ReadRemediationsAsync(NpgsqlConnection connection, Guid? sessionId, CancellationToken cancellationToken)
    {
        var items = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT remediation_request_id, diagnostic_session_id, runbook_code, action_code, target_reference,
                   state, requested_by, approved_by, executed_by, plan_json, result_json,
                   requested_at, approved_at, executed_at, verified_at, closed_at
            FROM projectpulse_remediation_requests
            WHERE (@session_id IS NULL OR diagnostic_session_id = @session_id)
            ORDER BY requested_at DESC LIMIT 200;
            """, connection);
        command.Parameters.Add("session_id", NpgsqlDbType.Uuid).Value = (object?)sessionId ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new
            {
                remediationRequestId = reader.GetGuid(0), sessionId = reader.GetGuid(1), runbook = reader.GetString(2), action = reader.GetString(3),
                targetReference = reader.GetString(4), state = reader.GetString(5), requestedBy = reader.GetGuid(6),
                approvedBy = reader.IsDBNull(7) ? null : reader.GetGuid(7).ToString(), executedBy = reader.IsDBNull(8) ? null : reader.GetGuid(8).ToString(),
                plan = JsonSerializer.Deserialize<object>(reader.GetString(9)), result = JsonSerializer.Deserialize<object>(reader.GetString(10)),
                requestedAt = reader.GetFieldValue<DateTimeOffset>(11), approvedAt = reader.IsDBNull(12) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(12),
                executedAt = reader.IsDBNull(13) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(13), verifiedAt = reader.IsDBNull(14) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(14),
                closedAt = reader.IsDBNull(15) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(15)
            });
        }
        return items;
    }

    private static async Task<object> ReadMetricsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                (SELECT COUNT(*) FROM projectpulse_diagnostic_sessions WHERE status <> 'closed'),
                (SELECT COUNT(*) FROM projectpulse_diagnostic_sessions WHERE status = 'attention_required'),
                (SELECT COUNT(*) FROM projectpulse_diagnostic_findings WHERE status = 'failed'),
                (SELECT COUNT(*) FROM projectpulse_remediation_requests WHERE state = 'awaiting_approval'),
                (SELECT COUNT(*) FROM projectpulse_remediation_requests WHERE state = 'verified');
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
        return new { activeSessions = reader.GetInt64(0), attentionRequired = reader.GetInt64(1), failedFindings = reader.GetInt64(2), awaitingApproval = reader.GetInt64(3), verifiedRemediations = reader.GetInt64(4) };
    }

    private static DiagnosticSummary Summarize(IEnumerable<DiagnosticFinding> findings)
    {
        var items = findings.ToArray();
        return new DiagnosticSummary(items.Length, items.Count(item => item.Status == "healthy"), items.Count(item => item.Status == "warning"), items.Count(item => item.Status == "failed"), items.Count(item => item.Status == "unknown"));
    }

    private static string HighestSeverity(IEnumerable<DiagnosticFinding> findings)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["informational"] = 0, ["low"] = 1, ["medium"] = 2, ["high"] = 3, ["critical"] = 4 };
        return findings.OrderByDescending(item => rank.GetValueOrDefault(item.Severity)).FirstOrDefault()?.Severity ?? "informational";
    }

    private static string AdapterConfiguration(string action) => action switch
    {
        "restart_service" => "Enable the API Container App managed identity; set PROJECTPULSE_AZURE_OPERATIONS_ENABLED=true, AZURE_SUBSCRIPTION_ID, PROJECTPULSE_AZURE_RESOURCE_GROUP, and PROJECTPULSE_AZURE_ALLOWED_CONTAINER_APPS; grant only Microsoft.App/containerApps/revisions/read and Microsoft.App/containerApps/revisions/restart/action on the allowed apps.",
        "scale_service" => "Configure a separate, approved Azure Container Apps scaling adapter and bounded replica policy.",
        "rollback_deployment" => "Configure Module 077 release evidence, a known-good revision, and the approved Azure deployment adapter.",
        "replay_integration_event" => "Configure Module 075 event validation, quarantine release, and replay authority.",
        "refresh_configuration" => "Configure a bounded configuration-refresh adapter that never returns secrets.",
        "database_repair" => "Configure a reviewed database runbook, backup checkpoint, maintenance window, and rollback plan.",
        _ => "Configure the approved owner adapter and production authorization."
    };

    private static AzureOperationsAdapterReadiness AzureOperationsReadiness()
    {
        var enabled = string.Equals(Environment.GetEnvironmentVariable("PROJECTPULSE_AZURE_OPERATIONS_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
        var subscription = Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID")?.Trim() ?? string.Empty;
        var resourceGroup = Environment.GetEnvironmentVariable("PROJECTPULSE_AZURE_RESOURCE_GROUP")?.Trim() ?? string.Empty;
        var identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")?.Trim() ?? string.Empty;
        var identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER")?.Trim() ?? string.Empty;
        var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")?.Trim() ?? string.Empty;
        var allowed = (Environment.GetEnvironmentVariable("PROJECTPULSE_AZURE_ALLOWED_CONTAINER_APPS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length is > 0 and <= 63 && item.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missing = new List<string>();
        if (!enabled) missing.Add("PROJECTPULSE_AZURE_OPERATIONS_ENABLED=true");
        if (!Guid.TryParse(subscription, out _)) missing.Add("AZURE_SUBSCRIPTION_ID");
        if (resourceGroup.Length is < 1 or > 90) missing.Add("PROJECTPULSE_AZURE_RESOURCE_GROUP");
        if (!Uri.TryCreate(identityEndpoint, UriKind.Absolute, out var identityUri) || identityUri.Scheme != Uri.UriSchemeHttp) missing.Add("IDENTITY_ENDPOINT");
        if (identityHeader.Length < 10) missing.Add("IDENTITY_HEADER");
        if (allowed.Length == 0) missing.Add("PROJECTPULSE_AZURE_ALLOWED_CONTAINER_APPS");
        return new(enabled, missing.Count == 0, subscription, resourceGroup, identityEndpoint, identityHeader, clientId, allowed, missing.ToArray());
    }

    private static async Task<AzureRestartResult> RestartContainerAppAsync(AzureOperationsAdapterReadiness readiness, string target, CancellationToken cancellationToken)
    {
        string[] discoveredRevisions = [];
        var acceptedRevisions = new List<string>();
        string? failedRevision = null;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var identityUrl = $"{readiness.IdentityEndpoint}{(readiness.IdentityEndpoint.Contains('?') ? '&' : '?')}resource={Uri.EscapeDataString("https://management.azure.com/")}&api-version=2019-08-01{(string.IsNullOrWhiteSpace(readiness.ClientId) ? string.Empty : $"&client_id={Uri.EscapeDataString(readiness.ClientId)}")}";
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, identityUrl);
            tokenRequest.Headers.TryAddWithoutValidation("X-IDENTITY-HEADER", readiness.IdentityHeader);
            using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode) return new(false, (int)tokenResponse.StatusCode, "Managed identity token acquisition failed.", discoveredRevisions, acceptedRevisions.ToArray(), failedRevision);
            using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement) || string.IsNullOrWhiteSpace(tokenElement.GetString())) return new(false, 502, "Managed identity returned no access token.", discoveredRevisions, acceptedRevisions.ToArray(), failedRevision);
            var accessToken = tokenElement.GetString()!;
            var basePath = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(readiness.SubscriptionId)}/resourceGroups/{Uri.EscapeDataString(readiness.ResourceGroup)}/providers/Microsoft.App/containerApps/{Uri.EscapeDataString(target)}/revisions";
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"{basePath}?api-version=2026-01-01");
            listRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var listResponse = await client.SendAsync(listRequest, cancellationToken);
            if (!listResponse.IsSuccessStatusCode) return new(false, (int)listResponse.StatusCode, "Azure could not list the allowed app's revisions.", discoveredRevisions, acceptedRevisions.ToArray(), failedRevision);
            using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken));
            discoveredRevisions = listDocument.RootElement.TryGetProperty("value", out var values)
                ? values.EnumerateArray().Where(item => item.TryGetProperty("properties", out var properties) && properties.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True).Select(item => item.GetProperty("name").GetString() ?? string.Empty).Where(name => name.Length > 0).ToArray()
                : [];
            if (discoveredRevisions.Length == 0) return new(false, 409, "No active revision was found for the allowed Container App.", discoveredRevisions, acceptedRevisions.ToArray(), failedRevision);
            foreach (var revision in discoveredRevisions)
            {
                using var restartRequest = new HttpRequestMessage(HttpMethod.Post, $"{basePath}/{Uri.EscapeDataString(revision)}/restart?api-version=2026-01-01");
                restartRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                using var restartResponse = await client.SendAsync(restartRequest, cancellationToken);
                if (!restartResponse.IsSuccessStatusCode)
                {
                    failedRevision = revision;
                    return new(false, (int)restartResponse.StatusCode, $"Azure did not accept the restart for revision {revision}.", discoveredRevisions, acceptedRevisions.ToArray(), failedRevision);
                }
                acceptedRevisions.Add(revision);
            }
            return new(true, 200, "Azure accepted the active revision restart.", discoveredRevisions, acceptedRevisions.ToArray(), failedRevision);
        }
        catch (OperationCanceledException) { return new(false, 504, "The Azure operations adapter timed out.", discoveredRevisions, acceptedRevisions.ToArray(), failedRevision); }
        catch { return new(false, 502, "The Azure operations adapter was unavailable.", discoveredRevisions, acceptedRevisions.ToArray(), failedRevision); }
    }

    private static async Task<AzureRestartVerificationResult> VerifyContainerAppRestartAsync(
        AzureOperationsAdapterReadiness readiness,
        string target,
        string[] expectedRevisions,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var identityUrl = $"{readiness.IdentityEndpoint}{(readiness.IdentityEndpoint.Contains('?') ? '&' : '?')}resource={Uri.EscapeDataString("https://management.azure.com/")}&api-version=2019-08-01{(string.IsNullOrWhiteSpace(readiness.ClientId) ? string.Empty : $"&client_id={Uri.EscapeDataString(readiness.ClientId)}")}";
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, identityUrl);
            tokenRequest.Headers.TryAddWithoutValidation("X-IDENTITY-HEADER", readiness.IdentityHeader);
            using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
                return new(false, (int)tokenResponse.StatusCode, "Managed identity token acquisition failed during restart verification.", expectedRevisions, [], expectedRevisions, []);

            using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
            if (!tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement) || string.IsNullOrWhiteSpace(tokenElement.GetString()))
                return new(false, 502, "Managed identity returned no access token during restart verification.", expectedRevisions, [], expectedRevisions, []);

            var basePath = $"https://management.azure.com/subscriptions/{Uri.EscapeDataString(readiness.SubscriptionId)}/resourceGroups/{Uri.EscapeDataString(readiness.ResourceGroup)}/providers/Microsoft.App/containerApps/{Uri.EscapeDataString(target)}/revisions";
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"{basePath}?api-version=2026-01-01");
            listRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenElement.GetString()!);
            using var listResponse = await client.SendAsync(listRequest, cancellationToken);
            if (!listResponse.IsSuccessStatusCode)
                return new(false, (int)listResponse.StatusCode, "Azure could not read revision state after the restart.", expectedRevisions, [], expectedRevisions, []);

            using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken));
            var revisions = new Dictionary<string, (bool Active, string HealthState, string RunningState)>(StringComparer.OrdinalIgnoreCase);
            if (listDocument.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    if (!item.TryGetProperty("name", out var nameElement) || string.IsNullOrWhiteSpace(nameElement.GetString()) || !item.TryGetProperty("properties", out var properties))
                        continue;
                    var name = nameElement.GetString()!;
                    var active = properties.TryGetProperty("active", out var activeElement) && activeElement.ValueKind == JsonValueKind.True;
                    var healthState = properties.TryGetProperty("healthState", out var healthElement) ? healthElement.GetString() ?? string.Empty : string.Empty;
                    var runningState = properties.TryGetProperty("runningState", out var runningElement) ? runningElement.GetString() ?? string.Empty : string.Empty;
                    revisions[name] = (active, healthState, runningState);
                }
            }

            var observed = expectedRevisions.Where(revisions.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var missing = expectedRevisions.Where(revision => !revisions.ContainsKey(revision)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var unhealthy = observed.Where(revision =>
            {
                var status = revisions[revision];
                return !status.Active
                    || !string.Equals(status.HealthState, "Healthy", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(status.RunningState, "Running", StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            var success = missing.Length == 0 && unhealthy.Length == 0;
            return new(
                success,
                (int)listResponse.StatusCode,
                success ? "Every Azure revision accepted for restart is active, healthy, and running." : "One or more Azure revisions accepted for restart are missing or not healthy and running.",
                expectedRevisions,
                observed,
                missing,
                unhealthy);
        }
        catch (OperationCanceledException)
        {
            return new(false, 504, "Azure restart verification timed out.", expectedRevisions, [], expectedRevisions, []);
        }
        catch
        {
            return new(false, 502, "The Azure restart verification adapter was unavailable.", expectedRevisions, [], expectedRevisions, []);
        }
    }

    private static RestartExecutionClaim? ReadRestartExecutionClaim(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("outcome", out var outcome) || !string.Equals(outcome.GetString(), "in_progress", StringComparison.Ordinal))
                return null;
            var claimId = root.TryGetProperty("claimId", out var claimElement) && claimElement.TryGetGuid(out var parsedClaimId) ? parsedClaimId : (Guid?)null;
            var claimedAt = root.TryGetProperty("claimedAt", out var claimedElement) && claimedElement.TryGetDateTimeOffset(out var parsedClaimedAt) ? parsedClaimedAt : (DateTimeOffset?)null;
            var leaseExpiresAt = root.TryGetProperty("leaseExpiresAt", out var leaseElement) && leaseElement.TryGetDateTimeOffset(out var parsedLeaseExpiresAt) ? parsedLeaseExpiresAt : (DateTimeOffset?)null;
            return new(claimId, claimedAt, leaseExpiresAt);
        }
        catch
        {
            return null;
        }
    }

    private static string[] ReadStringArray(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString() ?? string.Empty).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static JsonElement ReadJsonEvidence(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return JsonSerializer.Deserialize<JsonElement>("{}"); }
    }

    private sealed record AzureOperationsAdapterReadiness(bool Enabled, bool Ready, string SubscriptionId, string ResourceGroup, string IdentityEndpoint, string IdentityHeader, string ClientId, string[] AllowedTargets, string[] Missing);
    private sealed record AzureRestartResult(bool Success, int HttpStatus, string Message, string[] DiscoveredRevisions, string[] AcceptedRevisions, string? FailedRevision);
    private sealed record AzureRestartVerificationResult(bool Success, int HttpStatus, string Message, string[] ExpectedRevisions, string[] ObservedRevisions, string[] MissingRevisions, string[] UnhealthyRevisions);
    private sealed record RestartExecutionClaim(Guid? ClaimId, DateTimeOffset? ClaimedAt, DateTimeOffset? LeaseExpiresAt);

    private static object[] DiagnosticCategories() =>
    [
        new { id = "application_runtime", name = "Application runtime", owner = "ProjectPulse API and Module 013" },
        new { id = "data_resilience", name = "Data and resilience", owner = "PostgreSQL and Modules 014-017" },
        new { id = "identity_access", name = "Identity and access", owner = "ProjectPulse authentication and Modules 010/062" },
        new { id = "delivery", name = "Build and delivery", owner = "Modules 077 and 058" },
        new { id = "security_operations", name = "Security operations", owner = "Module 997" },
        new { id = "cloud_platform", name = "Cloud platform", owner = "Approved Azure diagnostics adapter" }
    ];

    private static object[] IssueClassifiers() =>
    [
        new { severity = "informational", order = 1, definition = "Context or successful evidence.", responseExpectation = "retain" },
        new { severity = "low", order = 2, definition = "Localized degradation with workaround.", responseExpectation = "planned triage" },
        new { severity = "medium", order = 3, definition = "Material degradation or control gap.", responseExpectation = "same-day owner assessment" },
        new { severity = "high", order = 4, definition = "Major service, identity, data, or security risk.", responseExpectation = "immediate incident coordination" },
        new { severity = "critical", order = 5, definition = "Confirmed severe impact or active compromise.", responseExpectation = "invoke incident authority" }
    ];

    private static object[] RemediationLifecycle() =>
    [
        new { step = 1, code = "prepare", state = "active", purpose = "Choose a runbook and retain a preview." },
        new { step = 2, code = "approve", state = "active", purpose = "Require a separate eligible actor." },
        new { step = 3, code = "stage", state = "active", purpose = "Confirm target and rollback readiness." },
        new { step = 4, code = "promote", state = "native_or_adapter_gated", purpose = "Execute only an enabled native or approved adapter action." },
        new { step = 5, code = "verify", state = "active", purpose = "Rerun checks and retain before/after evidence." },
        new { step = 6, code = "rollback", state = "adapter_gated", purpose = "Use the approved rollback path when required." },
        new { step = 7, code = "close", state = "active", purpose = "Close only verified or rolled-back work." }
    ];

    private static Runbook[] Runbooks() =>
    [
        new("platform_health_refresh", "Refresh platform health evidence", "Module 998", "native", new[] { "refresh_health_snapshot" }, "Rerun sanitized native checks and replace session findings.", "No infrastructure change; prior evidence remains in audit."),
        new("service_recovery", "Service recovery", "Module 013 / Azure Operations", "azure_container_apps", new[] { "restart_service", "scale_service" }, "Preview service, revision, replicas, health probes, and expected impact.", "Return to the prior healthy revision or replica configuration."),
        new("deployment_recovery", "Deployment rollback", "Modules 077 and 058", "azure_deployment", new[] { "rollback_deployment" }, "Require a known-good immutable revision and gate evidence.", "Restore the pre-change revision and verify API/web health."),
        new("integration_recovery", "Integration delivery recovery", "Module 075", "integration_gateway", new[] { "replay_integration_event" }, "Validate payload, contract, idempotency, and quarantine release.", "Stop replay and re-quarantine failed deliveries."),
        new("configuration_recovery", "Configuration refresh", "Modules 064-068", "configuration_adapter", new[] { "refresh_configuration" }, "Show changed non-secret references and affected services.", "Restore prior reference versions."),
        new("database_recovery", "Database repair", "Database Operations", "database_runbook", new[] { "database_repair" }, "Require backup checkpoint, bounded SQL, lock/capacity review, and maintenance window.", "Use the approved restore or reversal script.")
    ];

    private static object[] OwnershipLinks() =>
    [
        new { id = "security-operations", name = "Security Operations", route = "#security-operations", owner = "Module 997" },
        new { id = "service-control", name = "Service Control Center", route = "#service-control", owner = "Module 013" },
        new { id = "cicd-pipeline", name = "CI/CD Pipeline", route = "#cicd-pipeline", owner = "Module 058" },
        new { id = "release-control", name = "Release and Rollback", route = "#release-deployment-control", owner = "Module 077" },
        new { id = "integration-gateway", name = "Integration Gateway", route = "#integration-event-gateway", owner = "Module 075" }
    ];

    private static string[] Guardrails() =>
    [
        "Actual-session authority is required; View-As is read-only.",
        "Diagnostic sessions store sanitized findings and never secret values or raw log bodies.",
        "A requester cannot approve their own remediation.",
        "Native health refresh changes diagnostic evidence only.",
        "Production-changing actions return the exact missing adapter or authority instead of pretending to execute.",
        "Verification reruns checks and retains before/after evidence."
    ];

    private sealed record ManagedBody<T>(NpgsqlConnection Connection, SecurityDiagnosticsOperations.AccessContext Access, T Request);
    private sealed record CreateSessionRequest(Guid? IncidentId, string? TargetKind, string? TargetReference, string? Note);
    private sealed record PrepareRemediationRequest(Guid DiagnosticSessionId, string? RunbookCode, string? ActionCode, string? TargetReference, string? Justification);
    private sealed record RemediationActionRequest(Guid RemediationRequestId, string? Note);
    private sealed record DiagnosticFinding(string CheckCode, string Category, string Status, string Severity, string Summary, object Evidence);
    private sealed record DiagnosticSummary(int Total, int Healthy, int Warning, int Failed, int Unknown);
    private sealed record Runbook(string Id, string Name, string Owner, string Adapter, string[] Actions, string Preview, string Rollback);
}
