using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 997 turns ProjectPulse-native authentication and audit telemetry into
/// an operational security queue. Incident case management is native and
/// durable. High-impact containment remains dual-controlled and only the
/// explicitly enabled native session-revocation action can execute without an
/// external security adapter.
/// </summary>
public static class SecurityOperationsResponseModule
{
    private const string ModuleNumber = "997";
    private const string ContractVersion = "2026-07-21.2";
    private static readonly string[] ViewRoles =
    [
        "SUPER_ADMINISTRATOR", "ADMINISTRATOR", "SECURITY_ANALYST",
        "SECURITY_OPERATIONS", "SECURITY_INCIDENT_COMMANDER"
    ];
    private static readonly string[] ManageRoles =
    [
        "SUPER_ADMINISTRATOR", "SECURITY_INCIDENT_COMMANDER"
    ];
    private static readonly string[] Severities = ["low", "medium", "high", "critical"];

    public static WebApplication MapSecurityOperationsResponseEndpoints(this WebApplication app)
    {
        app.MapGet("/api/security-operations/overview", (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        app.MapGet("/api/security-operations/alerts", (Func<HttpContext, Task<IResult>>)GetAlertsAsync);
        app.MapGet("/api/security-operations/sessions", (Func<HttpContext, Task<IResult>>)GetSessionsAsync);
        app.MapGet("/api/security-operations/incidents", (Func<HttpContext, Task<IResult>>)GetIncidentsAsync);
        app.MapGet("/api/security-operations/incidents/{incidentId:guid}", (Guid incidentId, HttpContext context) => GetIncidentAsync(incidentId, context));
        app.MapGet("/api/security-operations/threat-intelligence", (Func<HttpContext, Task<IResult>>)GetThreatIntelligenceAsync);
        app.MapGet("/api/security-operations/control-posture", (Func<HttpContext, Task<IResult>>)GetControlPostureAsync);
        app.MapGet("/api/security-operations/response-policy", (Func<HttpContext, Task<IResult>>)GetResponsePolicyAsync);
        app.MapGet("/api/security-operations/reporting-policy", (Func<HttpContext, Task<IResult>>)GetReportingPolicyAsync);
        app.MapGet("/api/security-operations/integration-policy", (Func<HttpContext, Task<IResult>>)GetIntegrationPolicyAsync);

        app.MapPost("/api/security-operations/incidents/declare", (Func<HttpContext, Task<IResult>>)DeclareIncidentAsync);
        app.MapPost("/api/security-operations/incidents/acknowledge", (Func<HttpContext, Task<IResult>>)AcknowledgeIncidentAsync);
        app.MapPost("/api/security-operations/response/contain", (Func<HttpContext, Task<IResult>>)PrepareContainmentAsync);
        app.MapPost("/api/security-operations/response/approve", (Func<HttpContext, Task<IResult>>)ApproveContainmentAsync);
        app.MapPost("/api/security-operations/response/execute", (Func<HttpContext, Task<IResult>>)ExecuteContainmentAsync);
        app.MapPost("/api/security-operations/response/eradicate", (HttpContext context) => UpdateIncidentStageAsync(context, "eradication"));
        app.MapPost("/api/security-operations/response/recover", (HttpContext context) => UpdateIncidentStageAsync(context, "recovery"));
        app.MapPost("/api/security-operations/case/close", (Func<HttpContext, Task<IResult>>)CloseIncidentAsync);

        app.MapPost("/api/security-operations/analysis", (HttpContext context) => LockedAdapterAsync(context, "ai_security_analysis", "Configure Module 064 security-analysis authority and an approved redaction policy."));
        app.MapPost("/api/security-operations/notifications/send", (HttpContext context) => LockedAdapterAsync(context, "external_notification", "Configure Module 067 incident-notification routing and approval authority."));
        app.MapPost("/api/security-operations/evidence/export", (HttpContext context) => LockedAdapterAsync(context, "evidence_export", "Configure an approved encrypted evidence repository and export policy."));
        return app;
    }

    private static Task<SecurityDiagnosticsOperations.AccessOutcome> AuthorizeAsync(HttpContext context) =>
        SecurityDiagnosticsOperations.AuthorizeAsync(
            context, ModuleNumber, ViewRoles, ManageRoles,
            "VIEW_SECURITY_OPERATIONS", "MANAGE_SECURITY_RESPONSE");

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
            moduleName = "Security Operations, Threat Intelligence & Response Center",
            status = "security_operations_ready",
            contractVersion = ContractVersion,
            generatedAt = DateTimeOffset.UtcNow,
            runtimeEnvironment = SecurityDiagnosticsOperations.RuntimeEnvironment(),
            access = SecurityDiagnosticsOperations.AccessResponse(outcome.Access!, context, "restricted_security_operations"),
            posture = new
            {
                mode = "projectpulse_native_telemetry_and_incident_operations",
                liveTelemetryConnected = true,
                telemetrySources = new[] { "auth_login_events", "auth_sessions", "audit_logs", "projectpulse_module_audit_events" },
                incidentStoreConfigured = true,
                diagnosticHandoffEnabled = true,
                nativeSessionRevocationEnabled = NativeSessionRevocationEnabled(),
                externalContainmentAdaptersEnabled = false
            },
            metrics,
            severityModel = SeverityModel(),
            operatingDomains = OperatingDomains(),
            ownership = OwnershipLinks(),
            guardrails = Guardrails()
        });
    }

    private static async Task<IResult> GetAlertsAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(connection, context.RequestAborted))
            return SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber);

        var stored = new List<object>();
        await using (var command = new NpgsqlCommand("""
            SELECT alert_id, source_code, source_event_id, alert_type, title, summary,
                   severity, confidence, status, subject_user_id, source_ip,
                   resource_type, resource_id, observed_at, last_seen_at
            FROM projectpulse_security_alerts
            WHERE status NOT IN ('resolved','dismissed')
            ORDER BY CASE severity WHEN 'critical' THEN 5 WHEN 'high' THEN 4 WHEN 'medium' THEN 3 WHEN 'low' THEN 2 ELSE 1 END DESC,
                     last_seen_at DESC
            LIMIT 100;
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(context.RequestAborted))
        {
            while (await reader.ReadAsync(context.RequestAborted))
            {
                stored.Add(new
                {
                    alertId = reader.GetGuid(0), source = reader.GetString(1), sourceEventId = reader.GetString(2),
                    alertType = reader.GetString(3), title = reader.GetString(4), summary = reader.GetString(5),
                    severity = reader.GetString(6), confidence = reader.GetInt16(7), status = reader.GetString(8),
                    subjectUserId = reader.IsDBNull(9) ? null : reader.GetGuid(9).ToString(),
                    sourceIp = reader.IsDBNull(10) ? null : reader.GetString(10),
                    resourceType = reader.IsDBNull(11) ? null : reader.GetString(11),
                    resourceId = reader.IsDBNull(12) ? null : reader.GetString(12),
                    observedAt = reader.GetFieldValue<DateTimeOffset>(13), lastSeenAt = reader.GetFieldValue<DateTimeOffset>(14),
                    persisted = true
                });
            }
        }

        var authenticationSignals = await ReadAuthenticationSignalsAsync(connection, context.RequestAborted);
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "security_alert_queue_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            inventoryMode = "projectpulse_native_live",
            activeAlerts = stored,
            authenticationSignals,
            queue = new
            {
                total = stored.Count + authenticationSignals.Count,
                persisted = stored.Count,
                derived = authenticationSignals.Count,
                liveCountAuthoritative = true
            },
            statement = "Persisted alerts and live ProjectPulse authentication signals are shown separately; derived signals do not claim compromise without analyst confirmation."
        });
    }

    private static async Task<IResult> GetSessionsAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        var sessions = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT s.auth_session_id, s.user_id, COALESCE(u.display_name, u.email, s.user_id::text),
                   s.provider_code, s.created_at, s.last_seen_at, s.expires_at, s.revoked_at,
                   s.ip_address
            FROM auth_sessions s
            JOIN app_users u ON u.user_id = s.user_id
            WHERE s.created_at >= now() - interval '30 days'
            ORDER BY s.last_seen_at DESC
            LIMIT 100;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
        while (await reader.ReadAsync(context.RequestAborted))
        {
            sessions.Add(new
            {
                sessionId = reader.GetGuid(0), userId = reader.GetGuid(1), user = reader.GetString(2),
                provider = reader.GetString(3), createdAt = reader.GetFieldValue<DateTimeOffset>(4),
                lastSeenAt = reader.GetFieldValue<DateTimeOffset>(5), expiresAt = reader.GetFieldValue<DateTimeOffset>(6),
                revokedAt = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                sourceIp = reader.IsDBNull(8) ? null : reader.GetString(8),
                active = reader.IsDBNull(7) && reader.GetFieldValue<DateTimeOffset>(6) > DateTimeOffset.UtcNow
            });
        }
        return Results.Ok(new { module = ModuleNumber, status = "security_sessions_loaded", sessions, sessionRevocationEnabled = NativeSessionRevocationEnabled() });
    }

    private static async Task<IResult> GetIncidentsAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(connection, context.RequestAborted))
            return SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber);

        var incidents = await ReadIncidentsAsync(connection, null, context.RequestAborted);
        var requests = await ReadResponseRequestsAsync(connection, null, context.RequestAborted);
        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "security_incidents_loaded",
            contractVersion = ContractVersion,
            persistenceMode = "projectpulse_native",
            activeIncidents = incidents,
            responseRequests = requests,
            lifecycle = IncidentLifecycle(),
            diagnosticHandoff = new { enabled = true, endpoint = "/api/system-diagnostics/sessions", route = "#system-diagnostics" }
        });
    }

    private static async Task<IResult> GetIncidentAsync(Guid incidentId, HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        var incidents = await ReadIncidentsAsync(connection, incidentId, context.RequestAborted);
        if (incidents.Count == 0) return Results.NotFound(new { module = ModuleNumber, status = "incident_not_found" });

        var timeline = new List<object>();
        await using (var command = new NpgsqlCommand("""
            SELECT e.event_id, e.action_code, e.actor_user_id,
                   COALESCE(u.display_name, u.email, e.actor_user_id::text), e.note, e.evidence_json, e.occurred_at
            FROM projectpulse_security_incident_events e
            LEFT JOIN app_users u ON u.user_id = e.actor_user_id
            WHERE e.incident_id = @incident_id
            ORDER BY e.occurred_at;
            """, connection))
        {
            command.Parameters.AddWithValue("incident_id", incidentId);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                timeline.Add(new
                {
                    eventId = reader.GetGuid(0), action = reader.GetString(1), actorUserId = reader.GetGuid(2),
                    actor = reader.GetString(3), note = reader.IsDBNull(4) ? null : reader.GetString(4),
                    evidence = JsonSerializer.Deserialize<object>(reader.GetString(5)), occurredAt = reader.GetFieldValue<DateTimeOffset>(6)
                });
            }
        }
        return Results.Ok(new { module = ModuleNumber, incident = incidents[0], timeline, responseRequests = await ReadResponseRequestsAsync(connection, incidentId, context.RequestAborted) });
    }

    private static async Task<IResult> DeclareIncidentAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        var blocked = SecurityDiagnosticsOperations.RequireMutation(context, ModuleNumber, outcome.Access!);
        if (blocked is not null) return blocked;
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(connection, context.RequestAborted))
            return SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber);

        var body = await SecurityDiagnosticsOperations.ReadBodyAsync<DeclareIncidentRequest>(context, ModuleNumber);
        if (body.Failure is not null) return body.Failure;
        var request = body.Value!;
        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";
        var severity = request.Severity?.Trim().ToLowerInvariant() ?? "";
        if (title.Length is < 5 or > 250 || description.Length is < 10 or > 4000 || !Severities.Contains(severity))
            return Results.BadRequest(new { module = ModuleNumber, status = "invalid_incident", message = "Title, description, and a valid severity are required." });

        var incidentId = Guid.NewGuid();
        await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
        await using (var command = new NpgsqlCommand("""
            INSERT INTO projectpulse_security_incidents
            (incident_id, source_alert_id, title, description, severity, status, owner_user_id, declared_by)
            VALUES (@incident_id, @source_alert_id, @title, @description, @severity, 'declared', @owner_user_id, @declared_by);
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("incident_id", incidentId);
            command.Parameters.AddWithValue("source_alert_id", (object?)request.SourceAlertId ?? DBNull.Value);
            command.Parameters.AddWithValue("title", title);
            command.Parameters.AddWithValue("description", description);
            command.Parameters.AddWithValue("severity", severity);
            command.Parameters.AddWithValue("owner_user_id", (object?)request.OwnerUserId ?? outcome.Access!.UserId);
            command.Parameters.AddWithValue("declared_by", outcome.Access!.UserId);
            await command.ExecuteNonQueryAsync(context.RequestAborted);
        }
        await InsertIncidentEventAsync(connection, transaction, incidentId, "declared", outcome.Access!.UserId, request.Note, new { severity, request.SourceAlertId }, context.RequestAborted);
        await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "security_incident", incidentId.ToString(), "incident_declared", outcome.Access!.UserId, new { severity, sourceAlertId = request.SourceAlertId }, context.RequestAborted);
        await transaction.CommitAsync(context.RequestAborted);
        return Results.Ok(new { module = ModuleNumber, status = "incident_declared", incidentId, diagnosticHandoffAvailable = true });
    }

    private static async Task<IResult> AcknowledgeIncidentAsync(HttpContext context)
    {
        var body = await PrepareManagedBodyAsync<IncidentActionRequest>(context);
        if (body.Result is not null) return body.Result;
        var (connection, access, request) = body.Value!;
        await using (connection)
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                UPDATE projectpulse_security_incidents
                SET status = CASE WHEN status = 'declared' THEN 'acknowledged' ELSE status END,
                    acknowledged_by = COALESCE(acknowledged_by, @actor),
                    acknowledged_at = COALESCE(acknowledged_at, now()),
                    owner_user_id = COALESCE(owner_user_id, @actor), updated_at = now()
                WHERE incident_id = @incident_id AND status <> 'closed';
                """, connection, transaction);
            command.Parameters.AddWithValue("actor", access.UserId);
            command.Parameters.AddWithValue("incident_id", request.IncidentId);
            if (await command.ExecuteNonQueryAsync(context.RequestAborted) == 0)
                return Results.NotFound(new { module = ModuleNumber, status = "incident_not_found_or_closed" });
            await InsertIncidentEventAsync(connection, transaction, request.IncidentId, "acknowledged", access.UserId, request.Note, new { }, context.RequestAborted);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "security_incident", request.IncidentId.ToString(), "incident_acknowledged", access.UserId, new { }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "incident_acknowledged", incidentId = request.IncidentId });
        }
    }

    private static async Task<IResult> PrepareContainmentAsync(HttpContext context)
    {
        var body = await PrepareManagedBodyAsync<ContainmentRequest>(context);
        if (body.Result is not null) return body.Result;
        var (connection, access, request) = body.Value!;
        await using (connection)
        {
            var action = request.ActionCode?.Trim().ToLowerInvariant() ?? "";
            var target = request.TargetReference?.Trim() ?? "";
            var reason = request.Reason?.Trim() ?? "";
            if (!new[] { "revoke_session", "suspend_user", "restrict_role", "quarantine_integration", "block_indicator" }.Contains(action)
                || target.Length is < 1 or > 250 || reason.Length is < 10 or > 2000)
                return Results.BadRequest(new { module = ModuleNumber, status = "invalid_containment_request" });

            var requestId = Guid.NewGuid();
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using (var command = new NpgsqlCommand("""
                INSERT INTO projectpulse_security_response_requests
                (response_request_id, incident_id, action_code, target_reference, reason, requested_by)
                SELECT @request_id, incident_id, @action_code, @target_reference, @reason, @requested_by
                FROM projectpulse_security_incidents
                WHERE incident_id = @incident_id AND status <> 'closed';
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("request_id", requestId);
                command.Parameters.AddWithValue("incident_id", request.IncidentId);
                command.Parameters.AddWithValue("action_code", action);
                command.Parameters.AddWithValue("target_reference", target);
                command.Parameters.AddWithValue("reason", reason);
                command.Parameters.AddWithValue("requested_by", access.UserId);
                if (await command.ExecuteNonQueryAsync(context.RequestAborted) == 0)
                    return Results.NotFound(new { module = ModuleNumber, status = "incident_not_found_or_closed" });
            }
            await using (var update = new NpgsqlCommand("UPDATE projectpulse_security_incidents SET status = 'containment_pending', updated_at = now() WHERE incident_id = @incident_id;", connection, transaction))
            {
                update.Parameters.AddWithValue("incident_id", request.IncidentId);
                await update.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await InsertIncidentEventAsync(connection, transaction, request.IncidentId, "containment_requested", access.UserId, reason, new { responseRequestId = requestId, action, target }, context.RequestAborted);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "security_response_request", requestId.ToString(), "containment_requested", access.UserId, new { request.IncidentId, action }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "containment_awaiting_approval", responseRequestId = requestId, action, executionAvailable = action == "revoke_session" && NativeSessionRevocationEnabled() });
        }
    }

    private static async Task<IResult> ApproveContainmentAsync(HttpContext context)
    {
        var body = await PrepareManagedBodyAsync<ResponseDecisionRequest>(context);
        if (body.Result is not null) return body.Result;
        var (connection, access, request) = body.Value!;
        await using (connection)
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("""
                UPDATE projectpulse_security_response_requests
                SET state = 'approved', approved_by = @actor, approved_at = now()
                WHERE response_request_id = @request_id
                  AND state = 'awaiting_approval' AND requested_by <> @actor
                RETURNING incident_id, action_code;
                """, connection, transaction);
            command.Parameters.AddWithValue("actor", access.UserId);
            command.Parameters.AddWithValue("request_id", request.ResponseRequestId);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted))
                return Results.Json(new { module = ModuleNumber, status = "approval_rejected", message = "The request is not awaiting approval or separation of duties was not met." }, statusCode: StatusCodes.Status409Conflict);
            var incidentId = reader.GetGuid(0);
            var action = reader.GetString(1);
            await reader.DisposeAsync();
            await InsertIncidentEventAsync(connection, transaction, incidentId, "containment_approved", access.UserId, request.Note, new { request.ResponseRequestId, action }, context.RequestAborted);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "security_response_request", request.ResponseRequestId.ToString(), "containment_approved", access.UserId, new { incidentId, action }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "containment_approved", request.ResponseRequestId, action });
        }
    }

    private static async Task<IResult> ExecuteContainmentAsync(HttpContext context)
    {
        var body = await PrepareManagedBodyAsync<ResponseExecutionRequest>(context);
        if (body.Result is not null) return body.Result;
        var (connection, access, request) = body.Value!;
        await using (connection)
        {
            await using var read = new NpgsqlCommand("""
                SELECT incident_id, action_code, target_reference, state, requested_by, approved_by
                FROM projectpulse_security_response_requests
                WHERE response_request_id = @request_id;
                """, connection);
            read.Parameters.AddWithValue("request_id", request.ResponseRequestId);
            await using var reader = await read.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted)) return Results.NotFound(new { module = ModuleNumber, status = "response_request_not_found" });
            var incidentId = reader.GetGuid(0);
            var action = reader.GetString(1);
            var target = reader.GetString(2);
            var state = reader.GetString(3);
            var requestedBy = reader.GetGuid(4);
            var approvedBy = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
            await reader.DisposeAsync();

            if (state != "approved" || approvedBy is null || requestedBy == approvedBy)
                return Results.Json(new { module = ModuleNumber, status = "approved_request_required" }, statusCode: StatusCodes.Status409Conflict);
            if (action != "revoke_session")
                return Results.Json(new { module = ModuleNumber, status = "adapter_required", action, configured = false, message = "This containment type requires an approved external adapter." }, statusCode: StatusCodes.Status423Locked);
            if (!NativeSessionRevocationEnabled())
                return Results.Json(new { module = ModuleNumber, status = "native_session_revocation_disabled", requiredConfiguration = "PROJECTPULSE_SECURITY_NATIVE_SESSION_REVOCATION_ENABLED=true", message = "The approved request is preserved; enable the explicit production switch before execution." }, statusCode: StatusCodes.Status423Locked);
            if (!Guid.TryParse(target, out var sessionId)) return Results.BadRequest(new { module = ModuleNumber, status = "invalid_session_target" });

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            var revoked = 0;
            await using (var revoke = new NpgsqlCommand("""
                UPDATE auth_sessions
                SET revoked_at = now(), revoked_reason = @reason
                WHERE auth_session_id = @session_id AND revoked_at IS NULL AND expires_at > now();
                """, connection, transaction))
            {
                revoke.Parameters.AddWithValue("session_id", sessionId);
                revoke.Parameters.AddWithValue("reason", $"Module 997 approved containment {request.ResponseRequestId}");
                revoked = await revoke.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await using (var update = new NpgsqlCommand("""
                UPDATE projectpulse_security_response_requests
                SET state = @state, executed_by = @actor, executed_at = now(),
                    result_json = CAST(@result AS jsonb)
                WHERE response_request_id = @request_id;
                UPDATE projectpulse_security_incidents
                SET status = CASE WHEN @revoked > 0 THEN 'contained' ELSE status END, updated_at = now()
                WHERE incident_id = @incident_id;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("state", revoked > 0 ? "executed" : "failed");
                update.Parameters.AddWithValue("actor", access.UserId);
                update.Parameters.AddWithValue("result", JsonSerializer.Serialize(new { sessionId, revoked = revoked > 0 }));
                update.Parameters.AddWithValue("request_id", request.ResponseRequestId);
                update.Parameters.AddWithValue("revoked", revoked);
                update.Parameters.AddWithValue("incident_id", incidentId);
                await update.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await InsertIncidentEventAsync(connection, transaction, incidentId, revoked > 0 ? "session_revoked" : "session_revocation_failed", access.UserId, request.Note, new { request.ResponseRequestId, sessionId, revoked = revoked > 0 }, context.RequestAborted);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "security_response_request", request.ResponseRequestId.ToString(), revoked > 0 ? "containment_executed" : "containment_failed", access.UserId, new { incidentId, action, sessionId, revoked = revoked > 0 }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return revoked > 0
                ? Results.Ok(new { module = ModuleNumber, status = "containment_executed", request.ResponseRequestId, sessionId })
                : Results.Json(new { module = ModuleNumber, status = "session_not_active", request.ResponseRequestId, sessionId }, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> UpdateIncidentStageAsync(HttpContext context, string stage)
    {
        var body = await PrepareManagedBodyAsync<IncidentActionRequest>(context);
        if (body.Result is not null) return body.Result;
        var (connection, access, request) = body.Value!;
        await using (connection)
        {
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using var update = new NpgsqlCommand("UPDATE projectpulse_security_incidents SET status = @status, updated_at = now() WHERE incident_id = @incident_id AND status <> 'closed';", connection, transaction);
            update.Parameters.AddWithValue("status", stage);
            update.Parameters.AddWithValue("incident_id", request.IncidentId);
            if (await update.ExecuteNonQueryAsync(context.RequestAborted) == 0) return Results.NotFound(new { module = ModuleNumber, status = "incident_not_found_or_closed" });
            await InsertIncidentEventAsync(connection, transaction, request.IncidentId, stage, access.UserId, request.Note, new { }, context.RequestAborted);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "security_incident", request.IncidentId.ToString(), $"incident_{stage}", access.UserId, new { }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = $"incident_{stage}", request.IncidentId });
        }
    }

    private static async Task<IResult> CloseIncidentAsync(HttpContext context)
    {
        var body = await PrepareManagedBodyAsync<IncidentActionRequest>(context);
        if (body.Result is not null) return body.Result;
        var (connection, access, request) = body.Value!;
        await using (connection)
        {
            if (string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length < 10)
                return Results.BadRequest(new { module = ModuleNumber, status = "closure_summary_required" });
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using var update = new NpgsqlCommand("""
                UPDATE projectpulse_security_incidents
                SET status = 'closed', closed_at = now(), updated_at = now()
                WHERE incident_id = @incident_id AND status IN ('recovery','review');
                """, connection, transaction);
            update.Parameters.AddWithValue("incident_id", request.IncidentId);
            if (await update.ExecuteNonQueryAsync(context.RequestAborted) == 0)
                return Results.Json(new { module = ModuleNumber, status = "incident_not_ready_to_close", message = "Move the incident through recovery or review before closure." }, statusCode: StatusCodes.Status409Conflict);
            await InsertIncidentEventAsync(connection, transaction, request.IncidentId, "closed", access.UserId, request.Note, new { }, context.RequestAborted);
            await SecurityDiagnosticsOperations.WriteAuditAsync(connection, transaction, ModuleNumber, "security_incident", request.IncidentId.ToString(), "incident_closed", access.UserId, new { }, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = ModuleNumber, status = "incident_closed", request.IncidentId });
        }
    }

    private static async Task<IResult> LockedAdapterAsync(HttpContext context, string operation, string configurationPath)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        var blocked = SecurityDiagnosticsOperations.RequireMutation(context, ModuleNumber, outcome.Access!);
        if (blocked is not null) return blocked;
        return Results.Json(new
        {
            module = ModuleNumber,
            status = "adapter_required",
            operation,
            adapterInvoked = false,
            stateChanged = false,
            configurationPath,
            message = "The platform-native case is preserved, but this external action requires a separately approved adapter."
        }, statusCode: StatusCodes.Status423Locked);
    }

    private static async Task<(ManagedBody<T>? Value, IResult? Result)> PrepareManagedBodyAsync<T>(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context);
        if (outcome.Failure is not null) return (null, outcome.Failure);
        var blocked = SecurityDiagnosticsOperations.RequireMutation(context, ModuleNumber, outcome.Access!);
        if (blocked is not null)
        {
            await outcome.Connection!.DisposeAsync();
            return (null, blocked);
        }
        if (!await SecurityDiagnosticsOperations.OperationalSchemaAvailableAsync(outcome.Connection!, context.RequestAborted))
        {
            await outcome.Connection!.DisposeAsync();
            return (null, SecurityDiagnosticsOperations.SchemaUnavailable(ModuleNumber));
        }
        var body = await SecurityDiagnosticsOperations.ReadBodyAsync<T>(context, ModuleNumber);
        if (body.Failure is not null)
        {
            await outcome.Connection!.DisposeAsync();
            return (null, body.Failure);
        }
        return (new ManagedBody<T>(outcome.Connection!, outcome.Access!, body.Value!), null);
    }

    private static async Task<object> ReadMetricsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                (SELECT COUNT(*) FROM projectpulse_security_incidents WHERE status <> 'closed'),
                (SELECT COUNT(*) FROM projectpulse_security_incidents WHERE status <> 'closed' AND severity IN ('high','critical')),
                (SELECT COUNT(*) FROM projectpulse_security_response_requests WHERE state = 'awaiting_approval'),
                (SELECT COUNT(*) FROM projectpulse_diagnostic_sessions WHERE incident_id IS NOT NULL AND status <> 'closed'),
                (SELECT COUNT(*) FROM auth_login_events WHERE created_at >= now() - interval '24 hours' AND lower(login_result) NOT IN ('success','succeeded')),
                (SELECT COUNT(*) FROM auth_sessions WHERE revoked_at IS NULL AND expires_at > now());
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new
        {
            activeIncidents = reader.GetInt64(0), highCriticalIncidents = reader.GetInt64(1),
            containmentAwaitingApproval = reader.GetInt64(2), linkedDiagnosticSessions = reader.GetInt64(3),
            failedLogins24h = reader.GetInt64(4), activeSessions = reader.GetInt64(5)
        };
    }

    private static async Task<List<object>> ReadAuthenticationSignalsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var signals = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT COALESCE(NULLIF(username,''),'unknown'), COALESCE(NULLIF(source_ip,''),'unknown'),
                   COUNT(*), MIN(created_at), MAX(created_at), array_agg(DISTINCT login_result)
            FROM auth_login_events
            WHERE created_at >= now() - interval '24 hours'
              AND lower(login_result) NOT IN ('success','succeeded')
            GROUP BY COALESCE(NULLIF(username,''),'unknown'), COALESCE(NULLIF(source_ip,''),'unknown')
            HAVING COUNT(*) >= 3
            ORDER BY COUNT(*) DESC, MAX(created_at) DESC
            LIMIT 50;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var count = reader.GetInt64(2);
            signals.Add(new
            {
                signalId = $"auth:{reader.GetString(0)}:{reader.GetString(1)}",
                type = "repeated_authentication_failure", username = reader.GetString(0), sourceIp = reader.GetString(1),
                count, severity = count >= 20 ? "high" : count >= 10 ? "medium" : "low",
                firstSeenAt = reader.GetFieldValue<DateTimeOffset>(3), lastSeenAt = reader.GetFieldValue<DateTimeOffset>(4),
                results = reader.GetFieldValue<string[]>(5), persisted = false,
                recommendedAction = "Review correlated sessions and declare an incident if the activity is unauthorized."
            });
        }
        return signals;
    }

    private static async Task<List<object>> ReadIncidentsAsync(NpgsqlConnection connection, Guid? incidentId, CancellationToken cancellationToken)
    {
        var incidents = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT i.incident_id, i.incident_number, i.title, i.description, i.severity, i.status,
                   i.owner_user_id, COALESCE(owner.display_name, owner.email),
                   i.declared_by, COALESCE(declarer.display_name, declarer.email, i.declared_by::text),
                   i.diagnostic_session_id, i.declared_at, i.acknowledged_at, i.closed_at, i.updated_at,
                   (SELECT COUNT(*) FROM projectpulse_security_incident_events e WHERE e.incident_id = i.incident_id)
            FROM projectpulse_security_incidents i
            LEFT JOIN app_users owner ON owner.user_id = i.owner_user_id
            LEFT JOIN app_users declarer ON declarer.user_id = i.declared_by
            WHERE (@incident_id IS NULL OR i.incident_id = @incident_id)
            ORDER BY CASE i.severity WHEN 'critical' THEN 4 WHEN 'high' THEN 3 WHEN 'medium' THEN 2 ELSE 1 END DESC,
                     i.updated_at DESC
            LIMIT 200;
            """, connection);
        command.Parameters.AddWithValue("incident_id", (object?)incidentId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            incidents.Add(new
            {
                incidentId = reader.GetGuid(0), incidentNumber = reader.GetInt64(1), title = reader.GetString(2),
                description = reader.GetString(3), severity = reader.GetString(4), status = reader.GetString(5),
                ownerUserId = reader.IsDBNull(6) ? null : reader.GetGuid(6).ToString(), owner = reader.IsDBNull(7) ? "Unassigned" : reader.GetString(7),
                declaredByUserId = reader.GetGuid(8), declaredBy = reader.GetString(9),
                diagnosticSessionId = reader.IsDBNull(10) ? null : reader.GetGuid(10).ToString(),
                declaredAt = reader.GetFieldValue<DateTimeOffset>(11),
                acknowledgedAt = reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
                closedAt = reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(14), eventCount = reader.GetInt64(15)
            });
        }
        return incidents;
    }

    private static async Task<List<object>> ReadResponseRequestsAsync(NpgsqlConnection connection, Guid? incidentId, CancellationToken cancellationToken)
    {
        var requests = new List<object>();
        await using var command = new NpgsqlCommand("""
            SELECT response_request_id, incident_id, action_code, target_reference, reason, state,
                   requested_by, approved_by, executed_by, requested_at, approved_at, executed_at, result_json
            FROM projectpulse_security_response_requests
            WHERE (@incident_id IS NULL OR incident_id = @incident_id)
            ORDER BY requested_at DESC LIMIT 200;
            """, connection);
        command.Parameters.AddWithValue("incident_id", (object?)incidentId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            requests.Add(new
            {
                responseRequestId = reader.GetGuid(0), incidentId = reader.GetGuid(1), action = reader.GetString(2),
                targetReference = reader.GetString(3), reason = reader.GetString(4), state = reader.GetString(5),
                requestedBy = reader.GetGuid(6), approvedBy = reader.IsDBNull(7) ? null : reader.GetGuid(7).ToString(),
                executedBy = reader.IsDBNull(8) ? null : reader.GetGuid(8).ToString(),
                requestedAt = reader.GetFieldValue<DateTimeOffset>(9),
                approvedAt = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                executedAt = reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                result = JsonSerializer.Deserialize<object>(reader.GetString(12))
            });
        }
        return requests;
    }

    private static async Task InsertIncidentEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid incidentId, string action, Guid actor, string? note, object evidence, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO projectpulse_security_incident_events
            (event_id, incident_id, action_code, actor_user_id, note, evidence_json)
            VALUES (@event_id, @incident_id, @action, @actor, @note, CAST(@evidence AS jsonb));
            """, connection, transaction);
        command.Parameters.AddWithValue("event_id", Guid.NewGuid());
        command.Parameters.AddWithValue("incident_id", incidentId);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("note", (object?)note?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(evidence));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IResult> GetThreatIntelligenceAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new { module = ModuleNumber, status = "threat_intelligence_policy_loaded", sources = ThreatSources(), statement = "ProjectPulse native signals are active. External intelligence feeds remain unconfigured and never trigger automatic containment." });
    }

    private static async Task<IResult> GetControlPostureAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new { module = ModuleNumber, status = "security_control_posture_loaded", controls = ControlPosture(), nativeEvidence = await ReadMetricsAsync(connection, context.RequestAborted) });
    }

    private static async Task<IResult> GetResponsePolicyAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new { module = ModuleNumber, status = "security_response_policy_loaded", lifecycle = IncidentLifecycle(), gates = ResponseGates(), separationOfDuties = new { requesterCannotApprove = true, approvalRequiredBeforeExecution = true, viewAsTransfersAuthority = false } });
    }

    private static async Task<IResult> GetReportingPolicyAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new { module = ModuleNumber, status = "security_reporting_policy_loaded", timelineEvidenceEnabled = true, auditEvidenceEnabled = true, exportEnabled = false, externalNotificationEnabled = false, classification = "restricted_security_metadata", statement = "Evidence is retained in ProjectPulse; external export and notification require separately approved encrypted adapters." });
    }

    private static async Task<IResult> GetIntegrationPolicyAsync(HttpContext context)
    {
        var outcome = await AuthorizeAsync(context); if (outcome.Failure is not null) return outcome.Failure;
        await using var connection = outcome.Connection!;
        return Results.Ok(new { module = ModuleNumber, status = "security_integration_policy_loaded", connectors = IntegrationBoundaries(), projectPulseNative = true });
    }

    private static bool NativeSessionRevocationEnabled() =>
        string.Equals(Environment.GetEnvironmentVariable("PROJECTPULSE_SECURITY_NATIVE_SESSION_REVOCATION_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

    private static object[] SeverityModel() =>
    [
        new { code = "informational", order = 1, meaning = "Context requiring review." },
        new { code = "low", order = 2, meaning = "Limited exposure or low confidence." },
        new { code = "medium", order = 3, meaning = "Credible concern with bounded impact." },
        new { code = "high", order = 4, meaning = "Likely material impact requiring immediate coordination." },
        new { code = "critical", order = 5, meaning = "Confirmed or imminent severe impact." }
    ];

    private static object[] IncidentLifecycle() =>
    [
        new { step = 1, code = "detect", state = "active", purpose = "Review ProjectPulse-native signals and persisted alerts." },
        new { step = 2, code = "triage", state = "active", purpose = "Validate severity, confidence, scope, and owner." },
        new { step = 3, code = "declare", state = "active", purpose = "Create a durable governed incident." },
        new { step = 4, code = "contain", state = "approval_controlled", purpose = "Prepare, separately approve, and execute an available containment action." },
        new { step = 5, code = "eradicate", state = "evidence_workflow", purpose = "Record eradication work performed through the owning system." },
        new { step = 6, code = "recover", state = "evidence_workflow", purpose = "Record recovery and business verification." },
        new { step = 7, code = "review", state = "active", purpose = "Retain lessons and control improvements." },
        new { step = 8, code = "close", state = "active", purpose = "Complete evidence retention and closure." }
    ];

    private static object[] OperatingDomains() =>
    [
        new { id = "identity", name = "Identity & access", owner = "ProjectPulse authentication", status = "native_signal_active" },
        new { id = "application", name = "Application security", owner = "Modules 997 and 998", status = "native_operations_active" },
        new { id = "data", name = "Data protection & resilience", owner = "Modules 014-017", status = "delegated" },
        new { id = "delivery", name = "Software supply chain", owner = "Module 058", status = "delegated" },
        new { id = "cloud", name = "Cloud & network security", owner = "Approved future adapters", status = "not_configured" }
    ];

    private static object[] ThreatSources() =>
    [
        new { code = "internal_telemetry", name = "ProjectPulse authentication and audit telemetry", status = "connected", execution = true },
        new { code = "vendor_intelligence", name = "Licensed vendor intelligence", status = "not_configured", execution = false },
        new { code = "government_advisories", name = "Government advisories", status = "not_configured", execution = false },
        new { code = "analyst_observation", name = "Governed analyst observation", status = "incident_workflow_active", execution = true }
    ];

    private static object[] ControlPosture() =>
    [
        new { id = "authentication_monitoring", framework = "NIST CSF Detect", owner = "Module 997", status = "native_evidence", liveEvidence = true },
        new { id = "incident_management", framework = "NIST CSF Respond", owner = "Module 997", status = "operational", liveEvidence = true },
        new { id = "diagnostic_handoff", framework = "NIST CSF Respond/Recover", owner = "Module 998", status = "operational", liveEvidence = true },
        new { id = "external_containment", framework = "NIST CSF Respond", owner = "Approved adapters", status = "not_configured", liveEvidence = false }
    ];

    private static object[] OwnershipLinks() =>
    [
        new { id = "identity", name = "Identity administration", route = "#azure-admin", owner = "Modules 010 and 062" },
        new { id = "service", name = "Service Control Center", route = "#service-control", owner = "Module 013" },
        new { id = "delivery", name = "CI/CD Pipeline", route = "#cicd-pipeline", owner = "Module 058" },
        new { id = "diagnostics", name = "Diagnostics and remediation", route = "#system-diagnostics", owner = "Module 998" }
    ];

    private static object[] IntegrationBoundaries() =>
    [
        new { code = "projectpulse_telemetry", owner = "Module 997", status = "connected", secretRequired = false, execution = true },
        new { code = "native_session_revocation", owner = "Module 997", status = NativeSessionRevocationEnabled() ? "enabled" : "switch_disabled", secretRequired = false, execution = NativeSessionRevocationEnabled() },
        new { code = "external_identity_containment", owner = "Approved Entra adapter", status = "not_configured", secretRequired = true, execution = false },
        new { code = "network_containment", owner = "Approved network adapter", status = "not_configured", secretRequired = true, execution = false },
        new { code = "endpoint_containment", owner = "Approved endpoint adapter", status = "not_configured", secretRequired = true, execution = false }
    ];

    private static object ResponseGates() => new
    {
        incidentStoreConfigured = true,
        separatedApprovalEnforced = true,
        nativeSessionRevocationEnabled = NativeSessionRevocationEnabled(),
        externalResponseAdapterConfigured = false,
        externalNotificationAuthorized = false,
        evidenceExportAuthorized = false
    };

    private static string[] Guardrails() =>
    [
        "Actual-session authority is required; View-As never grants response authority.",
        "ProjectPulse authentication and audit telemetry is live; missing external telemetry remains explicit.",
        "Incident mutations and response requests are durable and audited.",
        "A requester cannot approve their own containment request.",
        "Only approved native session revocation can execute without an external adapter.",
        "Secrets, tokens, raw credentials, packet payloads, and unredacted provider errors are excluded."
    ];

    private sealed record ManagedBody<T>(NpgsqlConnection Connection, SecurityDiagnosticsOperations.AccessContext Access, T Request);
    private sealed record DeclareIncidentRequest(string? Title, string? Description, string? Severity, Guid? SourceAlertId, Guid? OwnerUserId, string? Note);
    private sealed record IncidentActionRequest(Guid IncidentId, string? Note);
    private sealed record ContainmentRequest(Guid IncidentId, string? ActionCode, string? TargetReference, string? Reason);
    private sealed record ResponseDecisionRequest(Guid ResponseRequestId, string? Note);
    private sealed record ResponseExecutionRequest(Guid ResponseRequestId, string? Note);
}
