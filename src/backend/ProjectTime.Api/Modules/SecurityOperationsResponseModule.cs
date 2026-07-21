using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 997 provides the governed Security Operations, Threat Intelligence &
/// Response source contract. Live telemetry, threat feeds, containment,
/// eradication, recovery, notifications, AI, evidence export, secret access,
/// and durable incident mutation remain fail-closed until separately approved.
/// </summary>
public static class SecurityOperationsResponseModule
{
    private const string ModuleNumber = "997";
    private const string ContractVersion = "2026-07-20.1";
    private const string ImplementationBaseline =
        "3d9a3dca8af479c854dc4c4a9294bc8aad273074";

    public static WebApplication MapSecurityOperationsResponseEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/security-operations/overview",
            (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        app.MapGet(
            "/api/security-operations/alerts",
            (Func<HttpContext, Task<IResult>>)GetAlertsAsync);
        app.MapGet(
            "/api/security-operations/incidents",
            (Func<HttpContext, Task<IResult>>)GetIncidentsAsync);
        app.MapGet(
            "/api/security-operations/threat-intelligence",
            (Func<HttpContext, Task<IResult>>)GetThreatIntelligenceAsync);
        app.MapGet(
            "/api/security-operations/control-posture",
            (Func<HttpContext, Task<IResult>>)GetControlPostureAsync);
        app.MapGet(
            "/api/security-operations/response-policy",
            (Func<HttpContext, Task<IResult>>)GetResponsePolicyAsync);
        app.MapGet(
            "/api/security-operations/reporting-policy",
            (Func<HttpContext, Task<IResult>>)GetReportingPolicyAsync);
        app.MapGet(
            "/api/security-operations/integration-policy",
            (Func<HttpContext, Task<IResult>>)GetIntegrationPolicyAsync);

        // Registered mutation-shaped routes make the complete lifecycle
        // discoverable. Every route authorizes the actual session and returns
        // 423 before reading a body or invoking an adapter.
        app.MapPost(
            "/api/security-operations/analysis",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "ai_security_analysis")));
        app.MapPost(
            "/api/security-operations/incidents/declare",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "declare")));
        app.MapPost(
            "/api/security-operations/incidents/acknowledge",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "acknowledge")));
        app.MapPost(
            "/api/security-operations/response/contain",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "contain")));
        app.MapPost(
            "/api/security-operations/response/eradicate",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "eradicate")));
        app.MapPost(
            "/api/security-operations/response/recover",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "recover")));
        app.MapPost(
            "/api/security-operations/notifications/send",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "external_notification")));
        app.MapPost(
            "/api/security-operations/evidence/export",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "evidence_export")));
        app.MapPost(
            "/api/security-operations/case/close",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "close")));

        return app;
    }

    private static async Task<IResult> GetOverviewAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        await ConfirmDatabaseConnectionAsync(connection);

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Security Operations, Threat Intelligence & Response Center",
            status = "security_control_plane_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            generatedAt = DateTimeOffset.UtcNow,
            runtimeEnvironment = RuntimeEnvironment(),
            access = AccessResponse(authorization.Access!, context),
            posture = new
            {
                mode = "safe_local_observation_and_delegated_security_status",
                directlyObserved = new[]
                {
                    "authenticated ProjectPulse session",
                    "server-side security authorization",
                    "authorization database SELECT 1"
                },
                liveTelemetryConnected = false,
                threatFeedsConnected = false,
                containmentEnabled = false,
                responseExecutionEnabled = false,
                aiExecutionEnabled = false,
                externalNotificationsEnabled = false,
                evidenceExportEnabled = false,
                secretAccessEnabled = false
            },
            severityModel = SeverityModel(),
            operatingDomains = OperatingDomains(),
            ownership = OwnershipLinks(),
            guardrails = Guardrails()
        });
    }

    private static async Task<IResult> GetAlertsAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "security_alert_contract_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            inventoryMode = "connector_not_configured",
            activeAlerts = Array.Empty<object>(),
            queue = new
            {
                total = 0,
                critical = 0,
                high = 0,
                medium = 0,
                low = 0,
                informational = 0,
                liveCountAuthoritative = false
            },
            requiredFields = new[]
            {
                "alert ID", "source", "source event ID", "observed time",
                "ingested time", "severity", "confidence", "status",
                "owner", "asset class", "data classification",
                "redaction result", "correlation ID"
            },
            statement = "No production telemetry connector is configured, so Module 997 reports no live alert findings and does not infer a healthy environment."
        });
    }

    private static async Task<IResult> GetIncidentsAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "security_incident_contract_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            persistenceMode = "not_configured",
            activeIncidents = Array.Empty<object>(),
            lifecycle = IncidentLifecycle(),
            serviceObjectives = new[]
            {
                new { severity = "critical", acknowledge = "15 minutes", command = "30 minutes" },
                new { severity = "high", acknowledge = "30 minutes", command = "60 minutes" },
                new { severity = "medium", acknowledge = "4 hours", command = "same business day" },
                new { severity = "low", acknowledge = "1 business day", command = "planned review" }
            },
            statement = "No durable incident store is configured. An empty source list is not evidence that no incident exists."
        });
    }

    private static async Task<IResult> GetThreatIntelligenceAsync(
        HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "threat_intelligence_policy_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            intelligence = new
            {
                connectorMode = "not_configured",
                indicators = Array.Empty<object>(),
                sources = ThreatSources(),
                confidenceScale = new[]
                {
                    new { code = "unconfirmed", score = "0-24", action = "preserve and corroborate" },
                    new { code = "possible", score = "25-49", action = "enrich under approved sources" },
                    new { code = "probable", score = "50-79", action = "escalate for analyst review" },
                    new { code = "confirmed", score = "80-100", action = "invoke approved incident authority" }
                },
                handling = new[]
                {
                    "validate source authority and license",
                    "record freshness, confidence, and expiry",
                    "minimize customer and identity data",
                    "never execute blocking from an indicator alone",
                    "require human incident authority before response"
                }
            }
        });
    }

    private static async Task<IResult> GetControlPostureAsync(
        HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "security_control_posture_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            assessmentMode = "delegated_not_live",
            controls = ControlPosture(),
            interpretation = new[]
            {
                "Delegated identifies the authoritative ProjectPulse owner; it does not assert live effectiveness.",
                "Unknown is preserved until an approved, fresh security connector supplies evidence.",
                "Control gaps never trigger automatic containment or remediation.",
                "Module 998 remains the controlled-remediation governance handoff; it is not invoked by this source checkpoint."
            }
        });
    }

    private static async Task<IResult> GetResponsePolicyAsync(
        HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "security_response_policy_loaded",
            contractVersion = ContractVersion,
            lifecycle = IncidentLifecycle(),
            gates = LockedGates(),
            separationOfDuties = new
            {
                analystMaySelfAuthorizeContainment = false,
                incidentCommanderMayBypassEvidenceReview = false,
                viewAsTransfersAuthority = false,
                productionContainmentRequiresSeparateAuthorization = true,
                recoveryRequiresBusinessOwnerVerification = true,
                closureRequiresPostIncidentReview = true
            }
        });
    }

    private static async Task<IResult> GetReportingPolicyAsync(
        HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "security_reporting_policy_loaded",
            contractVersion = ContractVersion,
            reporting = new
            {
                exportEnabled = false,
                externalNotificationEnabled = false,
                classification = "restricted_security_metadata",
                allowedSummaryFields = new[]
                {
                    "case ID", "severity", "status", "incident commander",
                    "business owner", "timeline summary", "control domain",
                    "sanitized impact", "decision record", "review state"
                },
                prohibitedFields = new[]
                {
                    "secret values", "tokens", "credentials", "raw packet data",
                    "unredacted logs", "exploit payloads", "private topology",
                    "customer content", "raw exception messages"
                },
                audiences = new[]
                {
                    new { code = "operations", detail = "restricted analyst and incident-command view" },
                    new { code = "leadership", detail = "sanitized risk, impact, and decision summary" },
                    new { code = "customer", detail = "separately approved communication only" },
                    new { code = "regulatory", detail = "legal and compliance review required" }
                }
            }
        });
    }

    private static async Task<IResult> GetIntegrationPolicyAsync(
        HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "security_integration_policy_loaded",
            contractVersion = ContractVersion,
            connectors = IntegrationBoundaries(),
            statement = "Every integration is an explicit future adapter. Module 997 currently makes no telemetry, threat-feed, AI, mail, cloud, identity-provider, endpoint, firewall, ticketing, or secret-store call."
        });
    }

    private static async Task<IResult> LockedOperationAsync(
        HttpContext context,
        string operation)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Json(new
        {
            module = ModuleNumber,
            status = "operation_locked",
            operation,
            contractVersion = ContractVersion,
            requestBodyRead = false,
            adapterInvoked = false,
            stateChanged = false,
            incidentPersisted = false,
            telemetryQueried = false,
            threatFeedQueried = false,
            aiExecuted = false,
            containmentExecuted = false,
            eradicationExecuted = false,
            recoveryExecuted = false,
            externalNotificationSent = false,
            evidenceExported = false,
            secretAccessed = false,
            gates = LockedGates(),
            message = "This Module 997 operation is fail-closed pending separate authorization and an approved security adapter."
        }, statusCode: StatusCodes.Status423Locked);
    }

    private static object[] OperatingDomains() =>
    [
        new { id = "identity", name = "Identity & access", owner = "Modules 010, 012, 037, 062", status = "delegated" },
        new { id = "application", name = "Application security", owner = "Modules 013 and 998", status = "delegated" },
        new { id = "data", name = "Data protection & resilience", owner = "Modules 014-017", status = "delegated" },
        new { id = "delivery", name = "Software supply chain", owner = "Module 058", status = "delegated" },
        new { id = "cloud", name = "Cloud & network security", owner = "Future approved telemetry adapters", status = "unknown" },
        new { id = "endpoint", name = "Endpoint security", owner = "Future approved telemetry adapters", status = "unknown" },
        new { id = "intelligence", name = "Threat intelligence", owner = "Module 997", status = "connector_not_configured" }
    ];

    private static object[] SeverityModel() =>
    [
        new { code = "informational", order = 1, color = "blue", meaning = "Context requiring review but no immediate response." },
        new { code = "low", order = 2, color = "teal", meaning = "Limited exposure with low confidence or impact." },
        new { code = "medium", order = 3, color = "amber", meaning = "Credible concern with bounded potential impact." },
        new { code = "high", order = 4, color = "orange", meaning = "Likely material impact requiring immediate coordination." },
        new { code = "critical", order = 5, color = "red", meaning = "Confirmed or imminent severe impact requiring approved incident authority." }
    ];

    private static object[] IncidentLifecycle() =>
    [
        new { step = 1, code = "detect", state = "connector_locked", purpose = "Receive and normalize approved signals." },
        new { step = 2, code = "triage", state = "guidance_only", purpose = "Validate severity, confidence, scope, and owner." },
        new { step = 3, code = "declare", state = "locked", purpose = "Create a durable governed incident." },
        new { step = 4, code = "contain", state = "locked", purpose = "Limit impact under separated authority." },
        new { step = 5, code = "eradicate", state = "locked", purpose = "Remove the confirmed cause under change control." },
        new { step = 6, code = "recover", state = "locked", purpose = "Restore service and validate business outcomes." },
        new { step = 7, code = "review", state = "guidance_only", purpose = "Record lessons, decisions, and control improvements." },
        new { step = 8, code = "close", state = "locked", purpose = "Complete evidence retention and formal closure." }
    ];

    private static object[] ThreatSources() =>
    [
        new { code = "internal_telemetry", name = "Internal security telemetry", status = "not_configured", execution = false },
        new { code = "vendor_intelligence", name = "Licensed vendor intelligence", status = "not_configured", execution = false },
        new { code = "government_advisories", name = "Government and sector advisories", status = "not_configured", execution = false },
        new { code = "community_exchange", name = "Approved community exchange", status = "not_configured", execution = false },
        new { code = "analyst_observation", name = "Governed analyst observation", status = "contract_only", execution = false }
    ];

    private static object[] ControlPosture() =>
    [
        new { id = "access_control", framework = "NIST CSF Protect", owner = "Modules 010, 012, 037, 062", status = "delegated", liveEvidence = false },
        new { id = "continuous_monitoring", framework = "NIST CSF Detect", owner = "Future security telemetry", status = "unknown", liveEvidence = false },
        new { id = "incident_management", framework = "NIST CSF Respond", owner = "Module 997", status = "governed_fail_closed", liveEvidence = false },
        new { id = "recovery_planning", framework = "NIST CSF Recover", owner = "Modules 014-017 and 998", status = "delegated", liveEvidence = false },
        new { id = "secure_delivery", framework = "Software supply chain", owner = "Module 058", status = "delegated", liveEvidence = false },
        new { id = "security_reporting", framework = "Governance and risk", owner = "Module 997", status = "contract_only", liveEvidence = false }
    ];

    private static object[] OwnershipLinks() =>
    [
        new { id = "identity", name = "Identity administration", route = "#azure-admin", owner = "Modules 010 and 062" },
        new { id = "service", name = "Service Control Center", route = "#service-control", owner = "Module 013" },
        new { id = "resilience", name = "Backup / DR Center", route = "#backup-dr", owner = "Modules 014-017" },
        new { id = "delivery", name = "CI/CD Pipeline", route = "#cicd-pipeline", owner = "Module 058" },
        new { id = "ai", name = "AI Provider Configuration", route = "#ai-provider-configuration", owner = "Module 064" },
        new { id = "mail", name = "Global Mail Configuration", route = "#global-mail-configuration", owner = "Module 067" },
        new { id = "architecture", name = "System Architecture", route = "#system-architecture", owner = "Module 068" },
        new { id = "diagnostics", name = "Controlled remediation handoff", route = "#system-diagnostics", owner = "Module 998 (parallel draft source)" }
    ];

    private static object[] IntegrationBoundaries() =>
    [
        new { code = "telemetry", owner = "future approved SIEM/security adapter", status = "not_configured", secretRequired = true, execution = false },
        new { code = "threat_intelligence", owner = "future approved intelligence adapter", status = "not_configured", secretRequired = true, execution = false },
        new { code = "endpoint_containment", owner = "future approved endpoint adapter", status = "not_configured", secretRequired = true, execution = false },
        new { code = "network_containment", owner = "future approved network adapter", status = "not_configured", secretRequired = true, execution = false },
        new { code = "identity_containment", owner = "future approved Entra adapter", status = "not_configured", secretRequired = true, execution = false },
        new { code = "case_management", owner = "future approved incident store", status = "not_configured", secretRequired = false, execution = false },
        new { code = "ai_analysis", owner = "Module 064", status = "not_authorized", secretRequired = false, execution = false },
        new { code = "notifications", owner = "Module 067", status = "not_authorized", secretRequired = false, execution = false }
    ];

    private static object LockedGates() => new
    {
        sourceContractPresent = true,
        telemetryConnectorConfigured = false,
        threatFeedConfigured = false,
        incidentStoreConfigured = false,
        responseAdapterConfigured = false,
        productionAuthorizationRecorded = false,
        incidentCommanderAuthorizationRecorded = false,
        separatedApprovalRecorded = false,
        externalNotificationAuthorized = false,
        aiExecutionAuthorized = false,
        evidenceExportAuthorized = false,
        secretAccessAuthorized = false,
        enabled = false
    };

    private static string[] Guardrails() =>
    [
        "Actual-session authority is required; View-As never grants security authority.",
        "Direct status is limited to the current session, authorization query, and SELECT 1.",
        "Missing telemetry is represented as unknown or not configured, never healthy.",
        "Raw logs, payloads, packet data, exploits, secrets, credentials, tokens, customer content, and private topology are excluded.",
        "No telemetry, threat feed, AI, cloud, identity-provider, endpoint, firewall, mail, ticketing, or secret-store connector executes.",
        "No containment, eradication, recovery, notification, export, or durable incident mutation executes.",
        "No request body is read by a locked operation.",
        "Modules 002, 056E, 059, 062, and 064-074 remain preserved."
    ];

    private static async Task ConfirmDatabaseConnectionAsync(
        NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT 1;", connection);
        await command.ExecuteScalarAsync();
    }

    private static async Task<AuthorizationOutcome> OpenAuthorizedConnectionAsync(
        HttpContext context)
    {
        var actualUserId = ActualSessionUserId(context);

        if (actualUserId is null)
        {
            return new AuthorizationOutcome(
                null,
                null,
                Results.Json(new
                {
                    module = ModuleNumber,
                    status = "session_required",
                    message = "A valid ProjectPulse session is required."
                }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new AuthorizationOutcome(
                null,
                null,
                Results.Json(new
                {
                    module = ModuleNumber,
                    status = "authorization_dependency_unavailable",
                    message = "Security operations authorization is temporarily unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var connection = new NpgsqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand("""
                SELECT
                    EXISTS (
                        SELECT 1
                        FROM app_user_role_assignments ura
                        JOIN app_roles r
                          ON r.app_role_id = ura.app_role_id
                         AND r.is_active = TRUE
                        LEFT JOIN app_role_permissions rp
                          ON rp.app_role_id = r.app_role_id
                        LEFT JOIN app_permissions p
                          ON p.app_permission_id = rp.app_permission_id
                        WHERE ura.user_id = @user_id
                          AND ura.is_active = TRUE
                          AND (
                              upper(COALESCE(r.role_code, '')) IN (
                                  'SUPER_ADMINISTRATOR', 'ADMINISTRATOR',
                                  'SECURITY_ANALYST', 'SECURITY_OPERATIONS',
                                  'SECURITY_INCIDENT_COMMANDER'
                              )
                              OR upper(COALESCE(p.permission_code, '')) IN (
                                  'VIEW_SECURITY_OPERATIONS',
                                  'MANAGE_SECURITY_RESPONSE',
                                  'SYSTEM_ADMINISTRATION',
                                  'MANAGE_ALL'
                              )
                          )
                    ) AS can_view,
                    EXISTS (
                        SELECT 1
                        FROM app_user_role_assignments ura
                        JOIN app_roles r
                          ON r.app_role_id = ura.app_role_id
                         AND r.is_active = TRUE
                        LEFT JOIN app_role_permissions rp
                          ON rp.app_role_id = r.app_role_id
                        LEFT JOIN app_permissions p
                          ON p.app_permission_id = rp.app_permission_id
                        WHERE ura.user_id = @user_id
                          AND ura.is_active = TRUE
                          AND (
                              upper(COALESCE(r.role_code, '')) IN (
                                  'SUPER_ADMINISTRATOR',
                                  'SECURITY_INCIDENT_COMMANDER'
                              )
                              OR upper(COALESCE(p.permission_code, '')) IN (
                                  'MANAGE_SECURITY_RESPONSE', 'MANAGE_ALL'
                              )
                          )
                    ) AS can_manage;
                """, connection);

            command.Parameters.AddWithValue("user_id", actualUserId.Value);
            await using var reader = await command.ExecuteReaderAsync();
            await reader.ReadAsync();

            var canView = reader.GetBoolean(0);
            var canManage = reader.GetBoolean(1);

            if (!canView)
            {
                await connection.DisposeAsync();
                return new AuthorizationOutcome(
                    null,
                    null,
                    Results.Json(new
                    {
                        module = ModuleNumber,
                        status = "security_access_required",
                        message = "Security operations are restricted to authorized security and administrative roles."
                    }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new AuthorizationOutcome(
                connection,
                new SecurityAccess(canView, canManage),
                null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();

            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("SecurityOperationsResponseModule");

            logger.LogWarning(
                "Module 997 authorization dependency unavailable ({ExceptionType}).",
                exception.GetType().Name);

            return new AuthorizationOutcome(
                null,
                null,
                Results.Json(new
                {
                    module = ModuleNumber,
                    status = "authorization_dependency_unavailable",
                    message = "Security operations authorization is temporarily unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static object AccessResponse(
        SecurityAccess access,
        HttpContext context) => new
    {
        classification = "restricted_security_operations",
        serverAuthorized = access.CanView,
        canRequestResponse = access.CanManage,
        responseExecutionEnabled = false,
        authoritySource = "actual_projectpulse_session",
        viewAsTransfersAuthority = false,
        isViewAs = IsViewAs(context)
    };

    private static Guid? ActualSessionUserId(HttpContext context)
    {
        foreach (var key in new[]
                 {
                     "ProjectPulseActualUserId",
                     "ProjectPulseSessionUserId"
                 })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }

        return null;
    }

    private static bool IsViewAs(HttpContext context) =>
        context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
        && value is bool isViewAs
        && isViewAs;

    private static string? BuildConnectionString()
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
        var port = Environment.GetEnvironmentVariable("PTP_DB_PORT");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(port, out var parsedPort) ? parsedPort : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private static string RuntimeEnvironment()
    {
        var value = (
            Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "unknown").Trim().ToLowerInvariant();

        if (value.Contains("prod", StringComparison.Ordinal)) return "production";
        if (value.Contains("test", StringComparison.Ordinal)
            || value.Contains("qa", StringComparison.Ordinal)
            || value.Contains("uat", StringComparison.Ordinal)) return "test";
        if (value.Contains("dev", StringComparison.Ordinal)) return "development";
        if (value.Contains("local", StringComparison.Ordinal)) return "local";
        return "runtime_managed";
    }

    private sealed record AuthorizationOutcome(
        NpgsqlConnection? Connection,
        SecurityAccess? Access,
        IResult? Failure);

    private sealed record SecurityAccess(bool CanView, bool CanManage);
}
