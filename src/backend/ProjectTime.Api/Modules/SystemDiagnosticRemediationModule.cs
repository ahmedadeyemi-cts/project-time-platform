using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 998 provides a sanitized diagnostic control plane and a complete,
/// fail-closed remediation lifecycle contract. It observes only the current
/// ProjectPulse session and authorization database directly. Every provider,
/// AI, containment, deployment, rollback, and production-remediation action is
/// locked until a separately reviewed adapter and authorization are supplied.
/// </summary>
public static class SystemDiagnosticRemediationModule
{
    private const string ModuleNumber = "998";
    private const string ContractVersion = "2026-07-20.1";
    private const string ImplementationBaseline =
        "3d9a3dca8af479c854dc4c4a9294bc8aad273074";

    public static WebApplication MapSystemDiagnosticRemediationEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/system-diagnostics/overview",
            (Func<HttpContext, Task<IResult>>)GetOverviewAsync);
        app.MapGet(
            "/api/system-diagnostics/checks",
            (Func<HttpContext, Task<IResult>>)GetChecksAsync);
        app.MapGet(
            "/api/system-diagnostics/issues",
            (Func<HttpContext, Task<IResult>>)GetIssuesAsync);
        app.MapGet(
            "/api/system-diagnostics/evidence-policy",
            (Func<HttpContext, Task<IResult>>)GetEvidencePolicyAsync);
        app.MapGet(
            "/api/system-diagnostics/remediation-policy",
            (Func<HttpContext, Task<IResult>>)GetRemediationPolicyAsync);
        app.MapGet(
            "/api/system-diagnostics/runbooks",
            (Func<HttpContext, Task<IResult>>)GetRunbooksAsync);

        // These lifecycle endpoints intentionally remain registered so clients
        // can discover the complete control contract. They always stop before
        // reading a request body or invoking any execution adapter.
        app.MapPost(
            "/api/system-diagnostics/analysis",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "ai_diagnostic_analysis")));
        app.MapPost(
            "/api/system-diagnostics/remediation/prepare",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "prepare")));
        app.MapPost(
            "/api/system-diagnostics/remediation/approve",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "approve")));
        app.MapPost(
            "/api/system-diagnostics/remediation/stage",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "stage")));
        app.MapPost(
            "/api/system-diagnostics/remediation/promote",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "promote")));
        app.MapPost(
            "/api/system-diagnostics/remediation/verify",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "verify")));
        app.MapPost(
            "/api/system-diagnostics/remediation/rollback",
            (Func<HttpContext, Task<IResult>>)(context =>
                LockedOperationAsync(context, "rollback")));
        app.MapPost(
            "/api/system-diagnostics/remediation/close",
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
            moduleName = "System Diagnostic & Controlled Remediation Center",
            status = "diagnostic_control_plane_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            generatedAt = DateTimeOffset.UtcNow,
            runtimeEnvironment = RuntimeEnvironment(),
            access = AccessResponse(authorization.Access!, context),
            posture = new
            {
                mode = "safe_local_observation_and_delegated_status",
                directlyObserved = new[]
                {
                    "authenticated ProjectPulse session",
                    "server-side diagnostic authorization",
                    "authorization database SELECT 1"
                },
                delegated = DiagnosticChecks()
                    .Where(check => check.ObservationMode != "direct")
                    .Select(check => check.Owner)
                    .Distinct()
                    .ToArray(),
                productionActionsEnabled = false,
                aiExecutionEnabled = false,
                externalNotificationsEnabled = false,
                secretAccessEnabled = false
            },
            categories = DiagnosticCategories(),
            severityModel = SeverityModel(),
            ownership = OwnershipLinks(),
            guardrails = Guardrails()
        });
    }

    private static async Task<IResult> GetChecksAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;
        await ConfirmDatabaseConnectionAsync(connection);

        var checks = DiagnosticChecks();

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "diagnostic_checks_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            summary = new
            {
                total = checks.Length,
                healthy = checks.Count(check => check.Status == "healthy"),
                delegated = checks.Count(check => check.Status == "delegated"),
                governed = checks.Count(check => check.Status == "governed"),
                unknown = checks.Count(check => check.Status == "unknown")
            },
            checks,
            interpretation = new[]
            {
                "Healthy is used only for a check observed directly during this request.",
                "Delegated checks must be verified in the named owning module.",
                "Governed checks describe an approved boundary, not live provider health.",
                "Unknown is never promoted to healthy without authoritative telemetry."
            }
        });
    }

    private static async Task<IResult> GetIssuesAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "diagnostic_issue_contract_loaded",
            contractVersion = ContractVersion,
            observedAt = DateTimeOffset.UtcNow,
            inventoryMode = "non_persistent_sanitized_source",
            activeIssues = Array.Empty<object>(),
            classifiers = IssueClassifiers(),
            statement = "Module 998 has not connected a production telemetry or durable issue store, so it reports no live issue findings.",
            rules = new[]
            {
                "Absence of an issue record is not proof that a dependency is healthy.",
                "Raw logs, stack traces, customer data, private topology, and secret material are excluded.",
                "A future issue connector must provide source, freshness, confidence, owner, severity, and redaction evidence.",
                "Containment and remediation remain separately authorized actions."
            }
        });
    }

    private static async Task<IResult> GetEvidencePolicyAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "diagnostic_evidence_policy_loaded",
            contractVersion = ContractVersion,
            evidence = new
            {
                classification = "restricted_operational_metadata",
                exportEnabled = false,
                rawLogAccessEnabled = false,
                secretAccessEnabled = false,
                retentionOwner = "central ProjectPulse governance",
                requiredFields = new[]
                {
                    "evidence ID", "source owner", "observation time",
                    "freshness", "severity", "confidence", "redaction result",
                    "correlation ID", "review status"
                },
                prohibitedFields = new[]
                {
                    "secret values", "tokens", "connection strings",
                    "raw exception messages", "private host names",
                    "tenant identifiers", "unredacted customer or user data"
                }
            },
            chainOfCustody = new[]
            {
                "collect from an approved owner",
                "minimize and redact",
                "hash the governed artifact",
                "record source and observation timestamps",
                "review access and classification",
                "retain or dispose under approved policy"
            }
        });
    }

    private static async Task<IResult> GetRemediationPolicyAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "remediation_policy_loaded",
            contractVersion = ContractVersion,
            lifecycle = new[]
            {
                new { step = 1, code = "prepare", state = "locked", purpose = "Create a bounded proposal without execution." },
                new { step = 2, code = "approve", state = "locked", purpose = "Record separated human authorization." },
                new { step = 3, code = "stage", state = "locked", purpose = "Validate a non-production execution target." },
                new { step = 4, code = "promote", state = "locked", purpose = "Execute only after production authorization." },
                new { step = 5, code = "verify", state = "locked", purpose = "Collect sanitized post-action evidence." },
                new { step = 6, code = "rollback", state = "locked", purpose = "Execute a pre-approved recovery path." },
                new { step = 7, code = "close", state = "locked", purpose = "Complete review and evidence retention." }
            },
            gates = LockedGates(),
            separationOfDuties = new
            {
                requesterMaySelfApprove = false,
                approverMayBypassStaging = false,
                viewAsTransfersAuthority = false,
                productionExecutionRequiresSeparateAuthorization = true
            }
        });
    }

    private static async Task<IResult> GetRunbooksAsync(HttpContext context)
    {
        var authorization = await OpenAuthorizedConnectionAsync(context);
        if (authorization.Failure is not null) return authorization.Failure;

        await using var connection = authorization.Connection!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            status = "diagnostic_runbooks_loaded",
            contractVersion = ContractVersion,
            executionMode = "guidance_only",
            runbooks = Runbooks(),
            statement = "Runbooks provide triage guidance and ownership links only. Module 998 executes no command, connector, notification, containment, deployment, or rollback."
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
            externalNotificationSent = false,
            secretAccessed = false,
            aiExecuted = false,
            containmentExecuted = false,
            deploymentExecuted = false,
            rollbackExecuted = false,
            gates = LockedGates(),
            message = "This Module 998 operation is fail-closed pending separate authorization and an approved execution adapter."
        }, statusCode: StatusCodes.Status423Locked);
    }

    private static DiagnosticCategory[] DiagnosticCategories() =>
    [
        new("application_runtime", "Application runtime", "Module 013", "Service, API, version, and application-shell signals"),
        new("data_resilience", "Data and resilience", "Modules 014-017", "Database, backup, restore, retention, and replication evidence"),
        new("identity_access", "Identity and access", "Modules 010, 012, 037, 062", "Authentication, authorization, role, and identity-profile controls"),
        new("delivery", "Build and delivery", "Module 058", "Source, validation, image, promotion, and rollback readiness"),
        new("shared_services", "Shared platform services", "Modules 064 and 067", "AI routing and outbound-mail configuration boundaries"),
        new("security_operations", "Security operations", "Module 997", "Threat, incident, containment, and response ownership")
    ];

    private static DiagnosticCheck[] DiagnosticChecks() =>
    [
        new("session", "Authenticated session", "application_runtime", "healthy", "direct", "Module 059", "Current request passed the ProjectPulse session boundary.", null),
        new("authorization_database", "Authorization database", "data_resilience", "healthy", "direct", "ProjectPulse API", "Role authorization and SELECT 1 completed.", "#service-control"),
        new("service_runtime", "Service and API runtime", "application_runtime", "delegated", "live_status_owner", "Module 013", "Open the Service Control Center for live status.", "#service-control"),
        new("backup_restore", "Backup and restore", "data_resilience", "delegated", "live_status_owner", "Modules 014-016", "Open the resilience centers for current evidence.", "#backup-dr"),
        new("replication", "Replication and synchronization", "data_resilience", "delegated", "live_status_owner", "Module 017", "Open Replication & Sync Status for current evidence.", "#replication-sync"),
        new("identity", "Identity and access", "identity_access", "delegated", "live_status_owner", "Modules 010 and 062", "Open the owning identity centers for current status.", "#azure-admin"),
        new("delivery", "Build and delivery", "delivery", "delegated", "live_status_owner", "Module 058", "Open CI/CD Pipeline for current source and delivery status.", "#cicd-pipeline"),
        new("ai_router", "Shared AI routing", "shared_services", "governed", "configuration_owner", "Module 064", "Provider execution is not performed by Module 998.", "#ai-provider-configuration"),
        new("outbound_mail", "Shared outbound mail", "shared_services", "governed", "configuration_owner", "Module 067", "External notifications are not sent by Module 998.", "#global-mail-configuration"),
        new("security_operations", "Security operations", "security_operations", "unknown", "future_owner", "Module 997", "Module 997 will own authoritative threat and response signals.", null)
    ];

    private static IssueClassifier[] IssueClassifiers() =>
    [
        new("informational", 1, "Context or observation that does not require immediate action.", "owner review"),
        new("low", 2, "Localized degradation with a documented workaround.", "planned triage"),
        new("medium", 3, "Material degradation or control gap with bounded impact.", "same-day owner assessment"),
        new("high", 4, "Major service, data, identity, or security risk.", "immediate incident coordination"),
        new("critical", 5, "Confirmed severe impact or active compromise.", "invoke approved incident authority; containment remains separately authorized")
    ];

    private static object[] SeverityModel() =>
    [
        new { code = "informational", order = 1, color = "blue" },
        new { code = "low", order = 2, color = "teal" },
        new { code = "medium", order = 3, color = "amber" },
        new { code = "high", order = 4, color = "orange" },
        new { code = "critical", order = 5, color = "red" }
    ];

    private static OwnershipLink[] OwnershipLinks() =>
    [
        new("service-control", "Service Control Center", "#service-control", "Module 013"),
        new("backup-dr", "Backup / DR Center", "#backup-dr", "Module 014"),
        new("restore-validation", "Restore Validation", "#restore-validation", "Module 015"),
        new("backup-retention", "Backup Retention", "#backup-retention", "Module 016"),
        new("replication-sync", "Replication & Sync Status", "#replication-sync", "Module 017"),
        new("cicd-pipeline", "CI/CD Pipeline", "#cicd-pipeline", "Module 058"),
        new("ai-provider-configuration", "AI Provider Configuration", "#ai-provider-configuration", "Module 064"),
        new("global-mail-configuration", "Global Mail Configuration", "#global-mail-configuration", "Module 067"),
        new("system-architecture", "System Architecture", "#system-architecture", "Module 068")
    ];

    private static Runbook[] Runbooks() =>
    [
        new("service_degradation", "Service degradation", "Module 013", "#service-control", new[] { "Confirm user impact", "Review sanitized runtime status", "Assign an incident owner", "Prepare a bounded remediation proposal" }),
        new("data_resilience", "Data or resilience concern", "Modules 014-017", "#backup-dr", new[] { "Stop assumptions about data currency", "Review backup and replication evidence", "Define recovery point and recovery time objectives", "Request separate restore or rollback authority" }),
        new("identity_access", "Identity or access concern", "Modules 010, 012, 037, 062", "#azure-admin", new[] { "Confirm actual-session identity", "Review role and permission evidence", "Avoid secret inspection", "Escalate provider changes under separate authorization" }),
        new("delivery_failure", "Build or deployment concern", "Module 058", "#cicd-pipeline", new[] { "Preserve source and check evidence", "Identify the last verified immutable revision", "Do not promote or roll back without authorization", "Record post-action verification criteria" }),
        new("security_event", "Suspected security event", "Module 997", null, new[] { "Preserve evidence", "Classify severity and confidence", "Avoid unauthorized containment", "Transfer response ownership to the approved security workflow" })
    ];

    private static object LockedGates() => new
    {
        sourceContractPresent = true,
        executionAdapterConfigured = false,
        productionAuthorizationRecorded = false,
        separatedApprovalRecorded = false,
        durableAuditStoreConfigured = false,
        telemetryConnectorConfigured = false,
        notificationAdapterConfigured = false,
        aiExecutionAuthorized = false,
        secretAccessAuthorized = false,
        enabled = false
    };

    private static string[] Guardrails() =>
    [
        "Actual-session authority is required; View-As never grants access.",
        "Direct health is limited to the current session, authorization query, and SELECT 1.",
        "Delegated or unknown status is never represented as healthy.",
        "Raw logs, raw exceptions, secrets, tokens, connection strings, and private topology are excluded.",
        "No production remediation, containment, notification, AI, deployment, promotion, or rollback action executes.",
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
                    message = "System diagnostics authorization is temporarily unavailable."
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
                                  'SUPER_ADMINISTRATOR', 'ADMINISTRATOR'
                              )
                              OR upper(COALESCE(p.permission_code, '')) IN (
                                  'VIEW_SYSTEM_DIAGNOSTICS',
                                  'MANAGE_SYSTEM_REMEDIATION',
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
                              upper(COALESCE(r.role_code, '')) = 'SUPER_ADMINISTRATOR'
                              OR upper(COALESCE(p.permission_code, '')) IN (
                                  'MANAGE_SYSTEM_REMEDIATION', 'MANAGE_ALL'
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
                        status = "diagnostic_access_required",
                        message = "System diagnostics are restricted to authorized administrators."
                    }, statusCode: StatusCodes.Status403Forbidden));
            }

            return new AuthorizationOutcome(
                connection,
                new DiagnosticAccess(canView, canManage),
                null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();

            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("SystemDiagnosticRemediationModule");

            logger.LogWarning(
                "Module 998 authorization dependency unavailable ({ExceptionType}).",
                exception.GetType().Name);

            return new AuthorizationOutcome(
                null,
                null,
                Results.Json(new
                {
                    module = ModuleNumber,
                    status = "authorization_dependency_unavailable",
                    message = "System diagnostics authorization is temporarily unavailable."
                }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static object AccessResponse(
        DiagnosticAccess access,
        HttpContext context) => new
    {
        classification = "restricted_operations",
        serverAuthorized = access.CanView,
        canRequestRemediation = access.CanManage,
        remediationExecutionEnabled = false,
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
        DiagnosticAccess? Access,
        IResult? Failure);

    private sealed record DiagnosticAccess(bool CanView, bool CanManage);
    private sealed record DiagnosticCategory(
        string Id,
        string Name,
        string Owner,
        string Description);
    private sealed record DiagnosticCheck(
        string Id,
        string Name,
        string Category,
        string Status,
        string ObservationMode,
        string Owner,
        string Detail,
        string? Route);
    private sealed record IssueClassifier(
        string Severity,
        int Order,
        string Definition,
        string ResponseExpectation);
    private sealed record OwnershipLink(
        string Id,
        string Name,
        string Route,
        string Owner);
    private sealed record Runbook(
        string Id,
        string Name,
        string Owner,
        string? Route,
        string[] Steps);
}
